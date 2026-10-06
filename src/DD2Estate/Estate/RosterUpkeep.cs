using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.Source;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's upkeep of the roster, with DD1's numbers (<see cref="RosterUpkeepRules"/>):
    ///
    ///   a week passes         every hero who stayed at home and had no place in a building sheds
    ///                         idle_hero_stress_heal (5 of DD1's 200). DD1: (what the estate adds + 5) x (1 + the
    ///                         hero's stress-heal buffs); here the Puppet Theatre's share is given by Districts
    ///                         and the town event's by TownEvents ("One Good Week"), each on top of this one;
    ///   a quest begins        a hero below the quest's level starts it stressed
    ///                         (effectiveDifficultyDungeonStartingStress), takes more stress while it lasts
    ///                         (effectiveDifficultyStressDmgModifiers) and breaks more easily
    ///                         (effectiveDifficultyAfflictionOnsetModifiers); far below it, they say so;
    ///   a hero is sent away   the table dismissed_hero_stress_penalties. DD1'S OWN GAME NEVER READS THAT
    ///                         TABLE (neither of its executables holds the key's name, and its dismiss function
    ///                         touches nobody's stress), so the rule is built and switched OFF:
    ///                         <see cref="DismissalStresses"/>, bridge "upkeep.dismissal".
    ///
    /// REMAPPING: DD1's stress runs 0..200 and DD2's 0..10, in whole points. An amount is brought over at the
    /// same share of the hero's scale and its fraction is the chance of a point (5 of 200: a point one week in
    /// four), as the Tavern's relief does it. "More stress taken" is, as for camping buffs, that much less of
    /// DD2's resistance to stress, and the same share more of the stress the expedition itself deals out
    /// (hallways, hunger, traps, obstacles). "Breaks more easily" is that much off DD2's chance to come out of
    /// a stress test resolute, as the town events' buffs to that chance are given.
    /// </summary>
    [EstateModule]
    internal static class RosterUpkeep
    {
        private const string SectionKey = "upkeep";
        /// <summary>DD2's id of the good outcome of a stress test (as TownEvents has it).</summary>
        private const string Resolute = "resolute";

        private static RosterUpkeepRules _rules;
        private static Rng _dice = new Rng(DateTime.Now.Ticks);
        // Heroes who sat the last expedition out, taken when it ended (the buildings empty their slots as the
        // week turns, in no particular order).
        private static List<uint> _idle;
        // The expedition under way: the effective difficulty of each hero it is too hard for.
        private static long _seed;
        private static readonly Dictionary<uint, double> Underlevel = new Dictionary<uint, double>();
        private static readonly ExpeditionStats Stats = new ExpeditionStats("estate_underlevel");
        private static float _nextCheck;
        private static string _lastWeek = "";

        /// <summary>Whether sending a hero away stresses the others. Off: DD1 has the table and does not use it.</summary>
        public static bool DismissalStresses;

        public static RosterUpkeepRules Rules => _rules ?? (_rules = RosterUpkeepRules.Load(new Dd1Files()));

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.ExpeditionEnded += expedition => _idle = TownEvents.IdleHeroes();
            EstateState.WeekAdvanced += OnWeekAdvanced;
            RosterLifecycle.Dismissed += (guid, level) => OnDismissed(guid, level, false);
            RosterLifecycle.Left += guid => Underlevel.Remove(guid);
            NarrationMoments.Tick += OnTick;
            NarrationMoments.SessionEnded += Stats.Forget;
            RegisterBridge();
        }

        // ---- a week passes -------------------------------------------------------------------------------

        private static void OnWeekAdvanced(int week)
        {
            if (!EstateSession.Active) return;
            try
            {
                var idle = _idle ?? TownEvents.IdleHeroes();
                _idle = null;
                var eased = Relieve(idle, Rules.IdleStressHeal, out var points);
                _lastWeek = "week " + (week - 1) + ": " + idle.Count + " idle, " + eased + " shed " + points + " point(s)";
                Plugin.Log.LogInfo("Roster upkeep, " + _lastWeek + " (DD1: " + Rules.IdleStressHeal.ToString("0.##", CultureInfo.InvariantCulture) + " of 200 each)");
            }
            catch (Exception e) { Plugin.Log.LogError("Roster upkeep: the week's relief failed: " + e); }
        }

        /// <summary>DD1 stress (of 200) off each of these heroes; returns how many shed a point or more.</summary>
        private static int Relieve(IEnumerable<uint> heroes, double dd1Stress, out int points)
        {
            points = 0;
            var eased = 0;
            foreach (var guid in heroes)
            {
                var actor = SanitariumLedger.Actor(guid);
                if (actor == null || !actor.IsLiving || actor.Stress <= 0f) continue;
                var shed = RosterUpkeepRules.Whole(RosterUpkeepRules.ToDd2(dd1Stress, actor.StressMax), _dice);
                if (shed <= 0) continue;
                actor.ApplyStressHeal(shed, SourceType.INN);
                points += shed;
                eased++;
            }
            return eased;
        }

        // ---- a hero is sent away -------------------------------------------------------------------------

        /// <summary>
        /// The table's stress for everyone left on the estate when a hero of this level has gone. Returns the
        /// names of those who took a point or more. Nothing happens unless the rule is on or forced.
        /// </summary>
        private static List<string> OnDismissed(uint leaver, int level, bool force)
        {
            var shaken = new List<string>();
            if (!force && !DismissalStresses) return shaken;
            try
            {
                var dd1 = Rules.DismissalStress(level);
                foreach (var guid in RosterLifecycle.LivingGuids())
                {
                    if (guid == leaver) continue;
                    var actor = SanitariumLedger.Actor(guid);
                    if (actor == null || !actor.IsLiving) continue;
                    var points = RosterUpkeepRules.Whole(RosterUpkeepRules.ToDd2(dd1, actor.StressMax), _dice);
                    if (points <= 0) continue;
                    actor.ApplyStressDamage(points, false, SourceType.STORY, "dismissal", 0u);
                    shaken.Add(ActivityLedger.HeroName(actor));
                }
                Plugin.Log.LogInfo("Roster upkeep: a hero of resolve " + level + " was sent away: " + dd1.ToString("0.##", CultureInfo.InvariantCulture) + " of 200 for the others; "
                                   + (shaken.Count > 0 ? string.Join(", ", shaken) + " took a point" : "nobody took a point"));
            }
            catch (Exception e) { Plugin.Log.LogError("Roster upkeep: the dismissal's stress failed: " + e); }
            return shaken;
        }

        // ---- a quest begins ------------------------------------------------------------------------------

        /// <summary>The hero's resolve level with the share of the way to the next (DD1 counts the experience in between).</summary>
        public static double ExactLevel(uint guid)
        {
            return RosterUpkeepRules.ExactLevel(Resolve.Level(guid), Resolve.Progress(guid), Resolve.MaxLevel);
        }

        /// <summary>DD1's effective difficulty of a quest of this difficulty (1, 3, 5, 6) for a hero; 0: they are up to it.</summary>
        public static double EffectiveDifficulty(uint guid, int questDifficulty)
        {
            return Rules.EffectiveDifficulty(questDifficulty, ExactLevel(guid));
        }

        /// <summary>The share more stress a hero takes on the expedition under way (0.25: a quarter more); 0 for a hero who is up to it.</summary>
        public static double StressModifier(uint guid)
        {
            return Underlevel.TryGetValue(guid, out var difficulty) ? Rules.StressDamageModifierAt(difficulty) : 0;
        }

        /// <summary>
        /// A fresh expedition has begun (not one out of a save): the heroes it is too hard for start it
        /// stressed, and are remembered for as long as it lasts. Returns what was done, for the log.
        /// </summary>
        public static List<string> OnEmbark(long seed, int questDifficulty, IEnumerable<ActorInstance> party)
        {
            var lines = new List<string>();
            _seed = seed;
            Underlevel.Clear();
            foreach (var hero in party)
            {
                var difficulty = EffectiveDifficulty(hero.ActorGuid, questDifficulty);
                if (difficulty <= 0) continue;
                Underlevel[hero.ActorGuid] = difficulty;
                // DD1: added to what the hero carries, and never past 100 of its 200
                var carried = hero.Stress / Math.Max(1f, hero.StressMax) * RosterUpkeepRules.Dd1StressMax;
                var dd1 = Rules.StartingStressFor(difficulty, carried);
                var points = RosterUpkeepRules.Whole(RosterUpkeepRules.ToDd2(dd1, hero.StressMax), _dice);
                if (points > 0) hero.ApplyStressDamage(points, false, SourceType.STORY, "quest_level", 0u);
                // DD1's bark of a hero far out of their depth
                if (Rules.Barks(difficulty))
                    lines.Add(ActivityLedger.HeroName(hero) + ": \"" + (Dd1Strings.Get("bark_effective_difficulty") ?? "THIS DUNGEON IS TOO DIFFICULT FOR ME!") + "\"");
                Plugin.Log.LogInfo("Roster upkeep: " + hero.ActorName + " at resolve " + ExactLevel(hero.ActorGuid).ToString("0.##", CultureInfo.InvariantCulture) + " on a quest of difficulty " + questDifficulty
                                   + ": effective difficulty " + difficulty.ToString("0.##", CultureInfo.InvariantCulture) + ", starting stress " + dd1.ToString("0.##", CultureInfo.InvariantCulture)
                                   + " of 200 = " + points + " point(s), stress taken " + Rules.StressDamageModifierAt(difficulty).ToString("+0%", CultureInfo.InvariantCulture)
                                   + ", resolve breaks " + Rules.AfflictionOnsetModifierAt(difficulty).ToString("+0.#%", CultureInfo.InvariantCulture));
            }
            Sync();
            return lines;
        }

        // DD2's stats for "takes more stress" and "breaks more easily", for the expedition.
        private static string StatsOf(uint guid)
        {
            if (!Underlevel.TryGetValue(guid, out var difficulty)) return "";
            var text = new StringBuilder();
            var more = Rules.StressDamageModifierAt(difficulty);
            if (Math.Abs(more) > 1e-9)
                text.Append("sub_stat,").Append(CampingBuffMap.Resistance).Append(",stress,").Append((-more).ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            var onset = Rules.AfflictionOnsetModifierAt(difficulty);
            if (Math.Abs(onset) > 1e-9)
                text.Append("sub_stat,").Append(ActorStatType.OVERSTRESS_CHANCE_MODIFIER.GetName()).Append(',').Append(Resolute).Append(',').Append((-onset).ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }

        // The game saves no stats container of the mod's: the heroes get theirs back after a load, and lose
        // them when the expedition is over.
        private static void Sync()
        {
            var run = DungeonRun.Current;
            if (run == null || run.Exploration.Map.Seed != _seed) Stats.Clear();
            else if (EstateSession.InHub) Stats.Sync(StatsOf);
        }

        private static void OnTick()
        {
            if (UnityEngine.Time.unscaledTime < _nextCheck) return;
            _nextCheck = UnityEngine.Time.unscaledTime + 1f;
            try { Sync(); }
            catch (Exception e) { Plugin.Log.LogError("Roster upkeep: the stress of the under-levelled could not be kept up: " + e); }
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JToken Save()
        {
            var heroes = new JObject();
            foreach (var pair in Underlevel) heroes[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value;
            return new JObject
            {
                ["dismissal_stresses"] = DismissalStresses,
                ["seed"] = _seed.ToString(CultureInfo.InvariantCulture),
                ["underlevel"] = heroes
            };
        }

        private static void Load(JToken token)
        {
            Reset();
            if (!(token is JObject json)) return;
            DismissalStresses = (bool?)json["dismissal_stresses"] ?? false;
            long.TryParse((string)json["seed"], NumberStyles.Integer, CultureInfo.InvariantCulture, out _seed);
            if (json["underlevel"] is JObject heroes)
                foreach (var property in heroes.Properties())
                    if (uint.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var guid)) Underlevel[guid] = (double?)property.Value ?? 0;
        }

        private static void Reset()
        {
            DismissalStresses = false;
            _seed = 0;
            _idle = null;
            Underlevel.Clear();
            // the heroes the containers hung on are gone with the session they belonged to
            Stats.Forget();
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        private static object Describe()
        {
            var rules = Rules;
            var roster = new List<object>();
            foreach (var guid in RosterLifecycle.LivingGuids())
            {
                var actor = SanitariumLedger.Actor(guid);
                if (actor == null) continue;
                var level = ExactLevel(guid);
                roster.Add(new
                {
                    guid, name = actor.ActorName, level, stress = actor.Stress, stressMax = actor.StressMax,
                    idle = !EstateSession.IsInParty(guid) && EstateSession.PartyBlockReason(guid) == null,
                    effectiveDifficulty = new { apprentice = rules.EffectiveDifficulty(1, level), veteran = rules.EffectiveDifficulty(3, level), champion = rules.EffectiveDifficulty(5, level), darkest = rules.EffectiveDifficulty(6, level) },
                    onThisQuest = Underlevel.TryGetValue(guid, out var difficulty) ? difficulty : 0,
                    carries = Stats.TextOn(guid)?.Trim().Replace("\n", " | ")
                });
            }
            return new
            {
                dd1 = new
                {
                    idleStressHeal = rules.IdleStressHeal, idleStressHealNoWeek = rules.IdleStressHealNoWeek,
                    effectiveDifficultyMin = rules.EffectiveDifficultyMin, effectiveDifficultyMax = rules.EffectiveDifficultyMax,
                    startingStress = rules.StartingStress, startingStressCeiling = RosterUpkeepRules.StartingStressCeiling,
                    stressDamageModifiers = rules.StressDamageModifiers, afflictionOnsetModifiers = rules.AfflictionOnsetModifiers, barkAbove = rules.BarkStress,
                    dismissalPenalties = rules.DismissalPenalties.Select(p => new { upperLevel = p.UpperLevel, stress = p.Stress }).ToList()
                },
                onDd2 = new
                {
                    idlePointsAWeek = RosterUpkeepRules.ToDd2(rules.IdleStressHeal, ActivityRules.Dd2StressMax),
                    startingPoints = rules.StartingStress.Select(s => RosterUpkeepRules.ToDd2(s, ActivityRules.Dd2StressMax)).ToList(),
                    dismissalPoints = rules.DismissalPenalties.Select(p => RosterUpkeepRules.ToDd2(p.Stress, ActivityRules.Dd2StressMax)).ToList()
                },
                dismissalStresses = DismissalStresses,
                lastWeek = _lastWeek,
                expeditionSeed = _seed,
                roster
            };
        }

        /// <summary>
        /// Test commands for the dev bridge:
        ///   upkeep.state                         DD1's numbers, what they come to on a DD2 hero, the roster (who is idle, who is below which quest)
        ///   upkeep.week [dd1=5] [all=true]       the week's relief now, for the idle (all: everybody); dd1: another amount of DD1 stress
        ///   upkeep.dismissal [on=true|false]     switches the dismissal rule (off by default: DD1 does not use its table)
        ///   upkeep.dismissal level=3 [guid=..]   the others take the stress as if a hero of that level (or that hero) had been sent away
        ///   upkeep.underlevel difficulty=3       the party starts a quest of that difficulty now: starting stress, and "more stress taken" while an expedition lasts
        ///   upkeep.seed n=5                      the dice start over from a seed
        /// </summary>
        private static void RegisterBridge()
        {
            AgentBridge.Register("upkeep.state", o => Describe());
            AgentBridge.Register("upkeep.week", o =>
            {
                if (!EstateSession.Active) return "no estate";
                var heroes = (bool?)o["all"] == true ? RosterLifecycle.LivingGuids() : TownEvents.IdleHeroes();
                var dd1 = (double?)o["dd1"] ?? Rules.IdleStressHeal;
                var eased = Relieve(heroes, dd1, out var points);
                return new { heroes = heroes.Count, dd1, eased, points, state = Describe() };
            });
            AgentBridge.Register("upkeep.dismissal", o =>
            {
                if (o["on"] != null) DismissalStresses = (bool)o["on"];
                if (o["level"] == null && o["guid"] == null) return new { dismissalStresses = DismissalStresses, table = Rules.DismissalPenalties.Select(p => new { upperLevel = p.UpperLevel, stress = p.Stress }).ToList() };
                if (!EstateSession.Active) return "no estate";
                var guid = o["guid"] != null ? (uint)o["guid"] : 0u;
                var level = (int?)o["level"] ?? Resolve.Level(guid);
                return new { level, dd1 = Rules.DismissalStress(level), shaken = OnDismissed(guid, level, true), dismissalStresses = DismissalStresses };
            });
            AgentBridge.Register("upkeep.underlevel", o =>
            {
                if (!EstateSession.Active) return "no estate";
                var run = DungeonRun.Current;
                var difficulty = (int?)o["difficulty"] ?? 3;
                return new { difficulty, said = OnEmbark(run != null ? run.Exploration.Map.Seed : 0, difficulty, Provisioning.Party()), state = Describe() };
            });
            AgentBridge.Register("upkeep.seed", o =>
            {
                _dice = new Rng((long?)o["n"] ?? 1);
                return "seeded";
            });
        }
    }
}
