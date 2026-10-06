using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Locale;
using Assets.Code.Quirk;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's stage coach: every week a few recruits wait in the hamlet, hiring one is free, and the barracks
    /// only hold so many heroes. How many arrive and how many fit comes from DD1's own building file at the
    /// stage coach's upgrade level. A recruit is a DD2 hero class on one of its paths (<see cref="HeroPaths"/>):
    /// DD1 lets an estate keep several heroes of a class, and here up to four of a class can serve, each on a
    /// path of their own, so the coach only brings a class and path nobody on the estate has. At first that is
    /// the class's own path; the Hero Paths tree opens the others one by one. Hiring creates the DD2 hero. The week's offer is rolled once from the estate's saved seed and kept in the save.
    /// </summary>
    [EstateModule]
    internal static class StageCoach
    {
        public const string BuildingId = "stage_coach";
        private const string RulesFile = "campaign/town/buildings/stage_coach/stage_coach.building.json";
        private const string SectionKey = "stage_coach";

        public class Recruit
        {
            public string ClassId;
            /// <summary>The DD2 path the recruit walks; null in an old save: the class's own.</summary>
            public string PathId;
            /// <summary>Null for the first hero of a class on this estate: they carry the class's own DD2 name.</summary>
            public string Name;
            /// <summary>
            /// The class's own name as the coach shows it, for a recruit without a <see cref="Name"/>. The game
            /// draws some classes' names anew every time it is asked (the Bounty Hunter's), so the name is
            /// asked for once, when the coach is filled, and the hero is hired under it. Null: never asked, or
            /// the game has no name of its own for the class.
            /// </summary>
            public string Shown;
            /// <summary>
            /// 0 for a green recruit; 1..3 for DD1's experienced recruits (the "level" of its
            /// upgraded_recruits_upgrades). DD1 sends those at a higher resolve level with skills and gear to
            /// match; here a veteran of rank n arrives at resolve level n with n skills mastered and weapon
            /// and armour of level n+1, plus the extra skills and quirks DD1 lists for the rank.
            /// </summary>
            public int Tier;
        }

        /// <summary>
        /// Reasons a class may not step off the coach (a town event, a quest lock, ...): return true to keep it
        /// away. Systems add their own check at start-up.
        /// </summary>
        public static readonly List<Func<string, bool>> ClassBlockers = new List<Func<string, bool>>();

        private const string RecruitsTree = BuildingId + ".numrecruits";
        private const string BarracksTree = BuildingId + ".rostersize";
        private const string VeteransTree = BuildingId + ".upgraded_recruits";
        private const string VeteransList = "upgraded_recruits_upgrades";

        private static int _seed;
        private static int _offerWeek;
        // Seats the coach had when this week's offer was made: a bigger coach built mid-week brings the difference.
        private static int _seats;
        private static readonly List<Recruit> _offer = new List<Recruit>();
        // Classes that have had a hero on this estate: the next one of the class is somebody else.
        private static readonly HashSet<string> _seen = new HashSet<string>();

        // DD1 rules, read once: amounts per upgrade level (index 0 = not upgraded).
        private static bool _rulesRead;
        private static int[] _recruitsByLevel;
        private static int[] _rosterSizeByLevel;
        private static List<string> _firstClasses = new List<string>();

        public static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.WeekAdvanced += OnWeekAdvanced;
            UpgradeRules.Built += OnUpgradeBuilt;
            Buildings.Register(BuildingId, StageCoachPanel.Open);
        }

        // ---- DD1 rules -------------------------------------------------------------------------------

        public static int RecruitsPerWeek
        {
            get
            {
                ReadRules();
                return Amount(_recruitsByLevel, UpgradeRules.Level(RecruitsTree), 2);
            }
        }

        /// <summary>How many heroes the estate can hold: DD1's barracks, but never more than classes and paths allow.</summary>
        public static int RosterSize
        {
            get
            {
                ReadRules();
                var seats = RosterLifecycle.SeatsInAll();
                var barracks = Amount(_rosterSizeByLevel, UpgradeRules.Level(BarracksTree), seats);
                return seats > 0 ? Math.Min(barracks, seats) : barracks;
            }
        }

        private static int Amount(int[] byLevel, int level, int fallback)
        {
            if (byLevel == null || byLevel.Length == 0) return fallback;
            return byLevel[Math.Max(0, Math.Min(level, byLevel.Length - 1))];
        }

        private static void ReadRules()
        {
            if (_rulesRead) return;
            _rulesRead = true;
            try
            {
                var text = Dd1Install.ReadText(RulesFile);
                if (text == null)
                {
                    Plugin.Log.LogWarning("Stage coach: " + RulesFile + " not found in the DD1 install; two recruits a week, no barracks limit");
                    return;
                }
                var stores = JObject.Parse(text).SelectToken("data.stores") as JArray;
                var store = stores?.FirstOrDefault(s => (string)s["id"] == "hero_recruit")?["data"];
                _recruitsByLevel = Amounts(store?["number_of_recruits_upgrades"]);
                _rosterSizeByLevel = Amounts(store?["roster_size_upgrades"]);
                if (store?["first_hero_classes"] is JArray first) _firstClasses = first.Select(t => (string)t).ToList();
                Plugin.Log.LogInfo("Stage coach rules (DD1): recruits " + Join(_recruitsByLevel) + ", roster size " + Join(_rosterSizeByLevel));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Stage coach: " + RulesFile + " could not be read: " + e.Message);
            }
        }

        private static int[] Amounts(JToken upgrades)
        {
            return upgrades is JArray list ? list.Select(u => (int?)u["amount"] ?? 0).ToArray() : null;
        }

        private static string Join(int[] values) => values == null ? "?" : string.Join("/", values);

        // ---- the week's recruits -----------------------------------------------------------------------

        /// <summary>Who waits at the coach this week (rolled on first use if the week has no offer yet).</summary>
        public static IReadOnlyList<Recruit> Offer
        {
            get
            {
                EnsureOffer();
                return _offer;
            }
        }

        public static string DisplayName(Recruit recruit)
        {
            if (!string.IsNullOrEmpty(recruit.Name)) return recruit.Name;
            Pin(recruit);
            return string.IsNullOrEmpty(recruit.Shown) ? HeroNames.ClassName(recruit.ClassId) : recruit.Shown;
        }

        // The name ActorInstance gives a new hero of a roster class; kept on the recruit from the first asking on.
        private static void Pin(Recruit recruit)
        {
            if (!string.IsNullOrEmpty(recruit.Name) || recruit.Shown != null) return;
            var localization = Singleton<Localization>.Instance;
            if (localization == null) return;
            recruit.Shown = localization.TryGetString("hero_name_canonical_" + recruit.ClassId) ?? "";
        }

        public static void MarkSeen(string classId) => _seen.Add(classId);

        public static string PathOf(Recruit recruit) => recruit.PathId ?? HeroPaths.Default(recruit.ClassId);

        private static string Seat(Recruit recruit) => HeroPaths.Key(recruit.ClassId, PathOf(recruit));

        private static bool SessionReady => EstateSession.Active && RosterLifecycle.Roster != null;

        private static void EnsureOffer()
        {
            if (!SessionReady || _offerWeek == EstateState.Current.Week) return;
            Roll(EstateState.Current.Week);
        }

        private static void OnWeekAdvanced(int week)
        {
            if (!SessionReady) return;
            // Those who fell this week free their class before the coach is filled.
            RosterLifecycle.BuryDead();
            Roll(week);
        }

        /// <summary>Fills the coach for a week. The same seed, week and roster always give the same recruits.</summary>
        public static void Roll(int week)
        {
            if (_seed == 0) _seed = NewSeed();
            var dice = new RosterDice(_seed, week);
            var onRoster = RosterLifecycle.ClassesOnRoster();
            foreach (var classId in onRoster) _seen.Add(classId);

            var candidates = new List<string>();
            foreach (var classId in RosterLifecycle.AvailableClasses())
                if (!IsBlocked(classId)) candidates.Add(classId);
            dice.Shuffle(candidates);
            if (week <= 1)
            {
                // DD1's very first coach brings fixed classes (its Plague Doctor and Vestal), unless the estate
                // started with them.
                ReadRules();
                var first = candidates.Where(c => _firstClasses.Contains(c) && !onRoster.Contains(c)).ToList();
                candidates = first.Concat(candidates.Where(c => !first.Contains(c))).ToList();
            }

            _offer.Clear();
            _seats = RecruitsPerWeek;
            Fill(dice, candidates, _seats);
            _offerWeek = week;
            Plugin.Log.LogInfo("Stage coach, week " + week + ": " + (_offer.Count == 0 ? "nobody" : string.Join(", ", _offer.Select(Describe))));
        }

        // Puts up to `seats` recruits on the coach, taking the classes in the order given: each on a path of the
        // class nobody on the estate or the coach walks, named if their class has been here before, and ranked.
        // One of every class before a second of any, so a coach brings different classes while it can.
        private static void Fill(RosterDice dice, List<string> classes, int seats)
        {
            var names = NamesInUse();
            var taken = RosterLifecycle.SeatsTaken();
            foreach (var waiting in _offer)
            {
                if (waiting.Name != null) names.Add(waiting.Name);
                taken.Add(Seat(waiting));
            }
            var added = 0;
            while (added < seats)
            {
                var before = added;
                foreach (var classId in classes)
                {
                    if (added >= seats) break;
                    var free = HeroPaths.Open(classId).Where(path => !taken.Contains(HeroPaths.Key(classId, path))).ToList();
                    if (free.Count == 0) continue;
                    var recruit = new Recruit { ClassId = classId, PathId = free[dice.Below(free.Count)] };
                    taken.Add(Seat(recruit));
                    // The class's own name goes to its first hero only.
                    if (_seen.Contains(classId) || _offer.Any(r => r.ClassId == classId && r.Name == null))
                    {
                        recruit.Name = RosterNames.Pick(dice, names);
                        names.Add(recruit.Name);
                    }
                    recruit.Tier = RollTier(dice);
                    Pin(recruit);
                    _offer.Add(recruit);
                    added++;
                }
                if (added == before) break;
            }
        }

        private static string Describe(Recruit recruit)
        {
            return DisplayName(recruit) + " (" + recruit.ClassId + "/" + PathOf(recruit) + (recruit.Tier > 0 ? ", veteran " + recruit.Tier : "") + ")";
        }

        /// <summary>
        /// DD1 fills a bigger coach from the next week on; the estate does not make the player wait: when the
        /// Stagecoach Network grows, the extra seats arrive with this week's coach.
        /// </summary>
        private static void OnUpgradeBuilt(UpgradeRules.Tree tree)
        {
            if (tree.Id != RecruitsTree || !SessionReady || _offerWeek != EstateState.Current.Week) return;
            var extra = RecruitsPerWeek - _seats;
            if (extra <= 0) return;
            var candidates = new List<string>();
            foreach (var classId in RosterLifecycle.AvailableClasses())
                if (!IsBlocked(classId)) candidates.Add(classId);
            var dice = new RosterDice(_seed, _offerWeek * 1000 + _seats);
            dice.Shuffle(candidates);
            // Classes that wait already come last.
            candidates = candidates.Where(c => _offer.All(r => r.ClassId != c)).Concat(candidates.Where(c => _offer.Any(r => r.ClassId == c))).ToList();
            var before = _offer.Count;
            Fill(dice, candidates, extra);
            _seats = RecruitsPerWeek;
            Plugin.Log.LogInfo("Stage coach: " + (_offer.Count - before) + " more step off the bigger coach");
        }

        // ---- experienced recruits ----------------------------------------------------------------------

        // DD1's entries for the ranks whose step is built, the highest first.
        private static List<JObject> VeteranRanks()
        {
            var ranks = new List<JObject>();
            var track = UpgradeRules.FindTrack(VeteransTree, VeteransList);
            if (track == null) return ranks;
            for (var level = Math.Min(UpgradeRules.Level(VeteransTree), track.Entries.Length - 1); level >= 1; level--)
                if (track.Entries[level] != null && !ranks.Contains(track.Entries[level])) ranks.Add(track.Entries[level]);
            return ranks;
        }

        // Every built rank has its own chance (DD1's `chance`); they do not overlap. The roll is always made,
        // so what the dice give next does not depend on the upgrades.
        private static int RollTier(RosterDice dice)
        {
            var roll = dice.Below(10000) / 10000f;
            foreach (var rank in VeteranRanks())
            {
                var chance = (float?)rank["chance"] ?? 0f;
                if (roll < chance) return (int?)rank["level"] ?? 0;
                roll -= chance;
            }
            return 0;
        }

        // What a veteran of rank n brings: resolve level n and what DD1 lets a hero of that level have, n skills
        // mastered and weapon and armour of level n+1, plus DD1's extra skills and quirks for the rank. Camping
        // skills have no DD2 counterpart.
        private static void Season(uint guid, int tier)
        {
            try
            {
                var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
                if (actor == null) return;
                JObject entry = null;
                var track = UpgradeRules.FindTrack(VeteransTree, VeteransList);
                foreach (var candidate in track != null ? track.Entries : new JObject[0])
                    if (candidate != null && ((int?)candidate["level"] ?? 0) == tier) entry = candidate;

                for (var level = Resolve.Level(guid); level < tier; level = Resolve.Level(guid))
                {
                    var missing = Resolve.ToNextLevel(guid);
                    if (missing <= 0) break;
                    Resolve.Grant(guid, missing);
                    if (Resolve.Level(guid) <= level) break;
                }
                var dice = new RosterDice(_seed, unchecked((int)guid * 7919 + tier));
                var trained = Guild.Grant(actor, (int?)entry?["number_of_extra_combat_skills"] ?? 0, tier, dice);
                Blacksmith.Grant(actor, tier + 1, tier + 1);
                var good = (int?)entry?["number_of_extra_positive_quirks"] ?? 0;
                var bad = (int?)entry?["number_of_extra_negative_quirks"] ?? 0;
                if (good > 0) actor.QuirkContainer.AddRandomQuirks(null, QuirkDefinition.QUIRK_POSITIVE_TAG, good, SourceType.QUIRK, null, 0u);
                if (bad > 0) actor.QuirkContainer.AddRandomQuirks(null, QuirkDefinition.QUIRK_NEGATIVE_TAG, bad, SourceType.QUIRK, null, 0u);
                Plugin.Log.LogInfo("Stage coach: " + actor.ActorName + " is a veteran of rank " + tier + ": resolve " + Resolve.Level(guid) + ", " + trained
                                   + ", gear level " + (tier + 1) + ", quirks +" + good + "/-" + bad);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Stage coach: a veteran's training could not be given: " + e);
            }
        }

        /// <summary>
        /// Dev bridge: puts a class on this week's coach, named the way a rolled recruit would be; on the path
        /// asked for, or on the first one nobody walks.
        /// </summary>
        public static string OfferForTest(string classId, int tier = 0, string pathId = null)
        {
            if (!SessionReady) return "no estate session";
            EnsureOffer();
            if (!RosterLifecycle.AvailableClasses().Contains(classId)) return classId + " is not a hero class of this install";
            var taken = RosterLifecycle.SeatsTaken();
            foreach (var waiting in _offer) taken.Add(Seat(waiting));
            var paths = HeroPaths.Of(classId);
            if (pathId != null && !paths.Contains(pathId)) return classId + " has no path " + pathId + " (it has " + string.Join(", ", paths) + ")";
            var path = pathId ?? paths.FirstOrDefault(p => !taken.Contains(HeroPaths.Key(classId, p)));
            if (path == null || taken.Contains(HeroPaths.Key(classId, path))) return "the estate or the coach already has a " + classId + (path != null ? " on the path " + path : " on every path");
            var recruit = new Recruit { ClassId = classId, PathId = path, Tier = Math.Max(0, tier) };
            if (_seen.Contains(classId) || _offer.Any(r => r.ClassId == classId && r.Name == null))
                recruit.Name = RosterNames.Pick(new RosterDice(_seed, _offerWeek * 1000 + _offer.Count + 1), NamesInUse());
            Pin(recruit);
            _offer.Add(recruit);
            return DisplayName(recruit) + " the " + classId + " (" + path + ") waits at the coach";
        }

        private static bool IsBlocked(string classId)
        {
            foreach (var blocker in ClassBlockers)
            {
                try
                {
                    if (blocker(classId)) return true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Stage coach: a class blocker failed: " + e);
                }
            }
            return false;
        }

        // The living and the dead keep their names to themselves.
        private static HashSet<string> NamesInUse()
        {
            var names = new HashSet<string>();
            var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            foreach (var guid in RosterLifecycle.LivingGuids())
            {
                var actor = actors.GetLibraryElement(guid);
                if (actor != null) names.Add(actor.ActorName);
            }
            foreach (var fallen in Graveyard.All) names.Add(fallen.Name);
            return names;
        }

        private static int NewSeed()
        {
            var seed = Guid.NewGuid().GetHashCode();
            return seed != 0 ? seed : 1;
        }

        // ---- hiring ----------------------------------------------------------------------------------

        /// <summary>The reason <see cref="HireBlockReason"/> gives when the roster has no room left.</summary>
        public const string BarracksFull = "The barracks are full";

        // DD1's words to the player when a recruit is dragged to a full roster (dialogue.string_table.xml).
        private const string DialogueTable = "localization/dialogue.string_table.xml";
        private const string FullRejectionId = "str_stagecoach_roster_full_rejection";
        private static string _fullRejection;

        /// <summary>DD1's sentence for a full roster: "Your barracks are full! Upgrade the Stagecoach or dismiss a hero."</summary>
        public static string FullRejection
        {
            get
            {
                if (_fullRejection != null) return _fullRejection;
                // FALLBACK: DD1's own English wording, used when the table cannot be read.
                _fullRejection = "Your barracks are full! Upgrade the Stagecoach or dismiss a hero.";
                try
                {
                    var path = Dd1Install.PathOf(DialogueTable);
                    if (path != null && System.IO.File.Exists(path))
                    {
                        var texts = new Dictionary<string, string>();
                        Dd1Strings.ReadEnglish(System.IO.File.ReadLines(path), texts, new[] { FullRejectionId });
                        if (texts.TryGetValue(FullRejectionId, out var text) && !string.IsNullOrEmpty(text)) _fullRejection = Dd1Strings.Format(text);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Stage coach: DD1's dialogue table could not be read: " + e.Message); }
                return _fullRejection;
            }
        }

        /// <summary>Why this recruit cannot be hired right now; null when they can.</summary>
        public static string HireBlockReason(Recruit recruit)
        {
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            if (!_offer.Contains(recruit)) return "No longer here";
            if (RosterLifecycle.Count >= RosterSize) return BarracksFull;
            if (RosterLifecycle.SeatsTaken().Contains(Seat(recruit))) return "The estate has a " + HeroNames.Title(recruit.ClassId, PathOf(recruit));
            return null;
        }

        /// <summary>Hiring is free, as in DD1; the recruit joins the bench.</summary>
        public static bool Hire(Recruit recruit)
        {
            RosterLifecycle.BuryDead();
            var reason = HireBlockReason(recruit);
            if (reason != null)
            {
                Plugin.Log.LogInfo("Stage coach: cannot hire the " + recruit.ClassId + ": " + reason);
                return false;
            }
            // The class's own name went to somebody else while this one waited.
            if (recruit.Name == null && NamesInUse().Contains(DisplayName(recruit)))
                recruit.Name = RosterNames.Pick(new RosterDice(_seed, _offerWeek * 1000 + 500 + _offer.IndexOf(recruit)), NamesInUse());
            // Hired under the name the coach showed: the game may draw another one for the new hero.
            Pin(recruit);
            var hireAs = !string.IsNullOrEmpty(recruit.Name) ? recruit.Name : string.IsNullOrEmpty(recruit.Shown) ? null : recruit.Shown;
            var guid = RosterLifecycle.CreateHero(recruit.ClassId, hireAs, party: false, pathId: PathOf(recruit));
            if (guid == 0u) return false;
            if (recruit.Tier > 0) Season(guid, recruit.Tier);
            _offer.Remove(recruit);
            EstatePersistence.SaveNow("recruit");
            return true;
        }

        // ---- save section ----------------------------------------------------------------------------

        private static JToken Save()
        {
            EnsureOffer();
            return new JObject
            {
                ["seed"] = _seed,
                ["week"] = _offerWeek,
                ["seats"] = _seats,
                ["offer"] = new JArray(_offer.Select(r => new JObject { ["cls"] = r.ClassId, ["path"] = r.PathId, ["name"] = r.Name, ["shown"] = string.IsNullOrEmpty(r.Shown) ? null : r.Shown, ["tier"] = r.Tier })),
                ["seen"] = new JArray(_seen.OrderBy(c => c, StringComparer.Ordinal))
            };
        }

        private static void Load(JToken token)
        {
            _offer.Clear();
            _seen.Clear();
            var json = token as JObject;
            _seed = (int?)json?["seed"] ?? 0;
            _offerWeek = (int?)json?["week"] ?? 0;
            if (json?["offer"] is JArray offer)
            {
                foreach (var item in offer)
                {
                    var classId = (string)item["cls"];
                    if (!string.IsNullOrEmpty(classId)) _offer.Add(new Recruit { ClassId = classId, PathId = (string)item["path"], Name = (string)item["name"], Shown = (string)item["shown"], Tier = (int?)item["tier"] ?? 0 });
                }
            }
            if (json?["seen"] is JArray seen)
                foreach (var classId in seen) _seen.Add((string)classId);
            // A save from before the seats were kept: the coach was as big as it is now.
            _seats = (int?)json?["seats"] ?? RecruitsPerWeek;
            RosterLifecycle.OnSessionStart();
        }

        private static void Reset()
        {
            _offer.Clear();
            _seen.Clear();
            _seed = NewSeed();
            _offerWeek = 0;
            _seats = 0;
            if (!SessionReady) return;
            // A new estate. The game no longer fills the roster by itself (RosterPatches), so the four starting
            // heroes are made here unless the session has made them already; either way they count as seen.
            RosterLifecycle.CreateStartingRoster();
            foreach (var classId in RosterLifecycle.ClassesOnRoster()) _seen.Add(classId);
        }

        public static object Describe()
        {
            return new
            {
                week = EstateState.Current.Week,
                offerWeek = _offerWeek,
                seed = _seed,
                recruitsPerWeek = RecruitsPerWeek,
                pathsOpen = HeroPaths.OpenCount,
                rosterSize = RosterSize,
                roster = SessionReady ? RosterLifecycle.Count : 0,
                offer = _offer.Select(r => new { cls = r.ClassId, path = PathOf(r), name = DisplayName(r), renamed = r.Name != null, tier = r.Tier, blocked = HireBlockReason(r) }).ToList(),
                seen = _seen.OrderBy(c => c, StringComparer.Ordinal).ToList()
            };
        }
    }
}
