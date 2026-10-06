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
    /// DD1's rules of the heroes who go down into the Darkest Dungeon, with DD1's numbers
    /// (<see cref="DarkestDungeonRules"/>, and the plot quests' own fields: <see cref="PlotQuestInfo"/>):
    ///
    ///   Never Again            a hero who has finished a quest there will not go again (DD1's option "Never
    ///                          Again" at Strict, its setting outside Radiant); at Permissive they go, starting
    ///                          at 80 stress of DD1's 200 or more and less likely to hold fast under it
    ///                          (never_again_affliction);
    ///   the veteran's lesson   in a party with such a hero in it, on any quest, those who have NOT been down
    ///                          there earn half as much resolve experience again
    ///                          (completed_darkest_dungeon_quest_party_resolve_xp);
    ///   a failed descent       when a party whose every hero is of resolve 5 or more comes back from there
    ///                          without its goal (a retreat or a rout), every living hero of the estate, the
    ///                          party's too, earns twice the experience on their own next completed quest
    ///                          (roster_buffs_to_apply_on_failure: darkest_dungeon_failure_roster_resolve_xp).
    ///
    /// The shares of experience are added up with the town event's and what they come to is rounded up
    /// (<see cref="Resolve"/>). Who gets which buff when was read in DD1's executable
    /// (<see cref="DarkestDungeonRules"/> names the places).
    ///
    /// No scouting, no surprise and the last descent's torch are the expedition's own business
    /// (<see cref="DungeonRun"/>, <see cref="Exploration"/>); the stress lifted off the estate by a finished
    /// descent and the price of a retreat are <see cref="QuestBoard"/>'s.
    ///
    /// REMAPPING: stress as everywhere (DD1's 80 of 200 is 4 of DD2's 10); DD1's resolve_check_percent (the
    /// chance of a virtue when the hero breaks) is DD2's overstress modifier for "resolute", as for the town
    /// events' buffs.
    ///
    /// WHAT THE PLACE IS FOR NOW is the setting [Rules] DarkestDungeon (<see cref="Mode"/>), one of three words:
    ///
    ///   bosses (the default)   the owner, 2026-10-06: "for now, temporarily: simply 5 dungeons, opening one
    ///                          after another; each at once starts the battle with the final bosses of a
    ///                          Confession". The place is open from the first week and holds the five quests of
    ///                          <see cref="DarkestBosses"/>. They are stand-ins: NOTHING of the rules above is
    ///                          asked of them (<see cref="IsDescent"/> is false for them): nobody becomes a
    ///                          veteran by them or refuses them, no party is taught on them, no failed one
    ///                          emboldens the estate, and what a hero carries of DD1's two buffs from an estate
    ///                          played under "dd1" is neither paid on them nor spent by them. The mod's descents
    ///                          (Data/plot_quests.json, plot_darkest_1..5) are off the board meanwhile.
    ///   dd1                    DD1's Darkest Dungeon as the mod had it: the descents behind the eight story
    ///                          quests, and every rule above. This is how all of it is switched back.
    ///   shut                   on the map and shut (the owner, earlier the same day): the Estate Map shows it
    ///                          the way DD1 shows a region that cannot be entered yet (no bar, the padlock), the
    ///                          pointer is told <see cref="SealedLine"/>, none of its quests is on the board (one
    ///                          a saved estate had there is taken off it) and none can be set out on
    ///                          (<see cref="QuestBoard"/>).
    ///
    /// The marks of those who have been down there (veterans, the emboldened) are kept in the save under every
    /// word of the setting.
    /// </summary>
    [EstateModule]
    internal static class DarkestDungeon
    {
        private const string SectionKey = "darkest";
        /// <summary>DD2's id of the good outcome of a stress test (as TownEvents has it).</summary>
        private const string Resolute = "resolute";

        private static DarkestDungeonRules _rules;
        private static Rng _dice = new Rng(DateTime.Now.Ticks);

        // Heroes who have finished a quest in the Darkest Dungeon.
        private static readonly HashSet<uint> Veterans = new HashSet<uint>();
        // Heroes who carry the failed descent's buff until they finish a quest.
        private static readonly HashSet<uint> Emboldened = new HashSet<uint>();
        private static string _setting = DarkestDungeonRules.DefaultSetting;

        // The expedition under way: who earns the veteran's share, and who came back against their oath.
        private static long _seed;
        private static readonly HashSet<uint> Taught = new HashSet<uint>();
        private static readonly HashSet<uint> Returned = new HashSet<uint>();
        private static readonly ExpeditionStats Stats = new ExpeditionStats("estate_never_again");
        private static float _nextCheck;

        // The expedition that has just ended, until the week has turned (its marks are made then, when
        // everybody who reads them at the expedition's end has done so).
        private static EstateState.Expedition _ended;
        private static string _last = "";

        public static DarkestDungeonRules Rules => _rules ?? (_rules = DarkestDungeonRules.Load(new Dd1Files()));

        /// <summary>
        /// The setting [Rules] DarkestDungeon, read at start-up: bosses (the five stand-ins), dd1 (DD1's descents)
        /// or shut. The dev bridge may change it for the session (darkest.mode).
        /// </summary>
        public static DarkestMode Mode = DarkestMode.Bosses;

        /// <summary>The Darkest Dungeon is on the map and cannot be entered.</summary>
        public static bool Sealed => Mode == DarkestMode.Shut;

        /// <summary>What the pointer is told on the shut region, in the mod's own words.</summary>
        public const string SealedLine = "The Darkest Dungeon is sealed for now.";

        /// <summary>DD1's option "Never Again": "strict" or "permissive".</summary>
        public static string Setting => _setting;

        private static void Register()
        {
            var setting = Plugin.Settings.Bind("Rules", "DarkestDungeon", DarkestBossRules.DefaultSetting,
                "What the Darkest Dungeon is. bosses (the default, for the time being): five quests in a row, open from the first week, each nothing but the fight with the final boss of one of DD2's five Confessions; the next appears when the one before it is won. "
                + "dd1: DD1's Darkest Dungeon as the mod has it (its descents behind the story quests, Never Again and DD1's other rules of those who go down). shut: it stays on the Estate Map, shut, and none of its quests is offered.").Value;
            Mode = DarkestBossRules.ParseMode(setting, out var known);
            if (!known) Plugin.Log.LogWarning("Darkest Dungeon: [Rules] DarkestDungeon = '" + setting + "' is none of bosses, dd1, shut: taken for " + DarkestBossRules.NameOf(Mode));
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.ExpeditionEnded += expedition => _ended = expedition;
            EstateState.WeekAdvanced += OnWeekAdvanced;
            RosterLifecycle.Left += Forget;
            NarrationMoments.Tick += OnTick;
            NarrationMoments.SessionEnded += Stats.Forget;
            Resolve.ExperienceBonuses.Add(ExperienceBonus);
            RegisterBridge();
        }

        // A quest of DD1's Darkest Dungeon, whose rules are this class. The five stand-ins (DarkestBosses) are
        // fought in the same place and are none: nothing here is asked of them.
        private static bool IsDescent(string dungeonId, string questId) => dungeonId == QuestBoard.DarkestDungeon && !IsStandIn(questId);

        private static bool IsStandIn(string questId) => DarkestBossRules.ActOf(questId) != null;

        // ---- Never Again ---------------------------------------------------------------------------------

        /// <summary>Whether the hero has finished a quest in the Darkest Dungeon (DD1 marks them with a torch in the roster).</summary>
        public static bool IsVeteran(uint guid) => Veterans.Contains(guid);

        /// <summary>
        /// What a veteran of the place says to another quest there when DD1's setting keeps them out (its own
        /// words, str_quest_darkest_dungeon); null when the hero will go.
        /// </summary>
        public static string Refusal(uint guid, Quest quest)
        {
            if (quest == null || !IsDescent(quest.Dungeon, quest.Id) || !IsVeteran(guid)) return null;
            if (Rules.Setting(_setting).CanEnter) return null;
            return Dd1Strings.Get("str_quest_darkest_dungeon") ?? "I'm never going back to that place.";
        }

        // ---- the quest begins ----------------------------------------------------------------------------

        /// <summary>
        /// A fresh expedition has begun (not one out of a save). Returns what was done, for the log: who
        /// teaches the party, and who was made to come back.
        /// </summary>
        public static List<string> OnEmbark(long seed, Quest quest, IReadOnlyList<ActorInstance> party)
        {
            var lines = new List<string>();
            _seed = seed;
            Taught.Clear();
            Returned.Clear();
            // one of the five stand-ins: no veteran teaches on it and none is made to pay for coming back
            if (IsStandIn(quest?.Id))
            {
                Sync();
                return lines;
            }
            var veterans = party.Where(hero => IsVeteran(hero.ActorGuid)).ToList();
            // DD1: one who has been down there teaches those of the party who have not, in any dungeon
            var taught = DarkestDungeonRules.Taught(party, hero => IsVeteran(hero.ActorGuid));
            foreach (var hero in taught) Taught.Add(hero.ActorGuid);
            if (taught.Count > 0)
                lines.Add(UpgradeText.Join(veterans.Select(ActivityLedger.HeroName).ToList()) + (veterans.Count == 1 ? " has" : " have") + " seen the Darkest Dungeon: the others learn faster ("
                          + Rules.VeteranXpBonus.ToString("+0%", CultureInfo.InvariantCulture) + " resolve experience).");
            if (quest != null && IsDescent(quest.Dungeon, quest.Id))
            {
                var setting = Rules.Setting(_setting);
                foreach (var hero in veterans)
                {
                    Returned.Add(hero.ActorGuid);
                    // DD1 (darkest_duungeon_minimum_stress): the hero sets out with that much stress at least
                    var least = RosterUpkeepRules.ToDd2(setting.MinimumStress, hero.StressMax);
                    var missing = least - hero.Stress;
                    var points = missing > 0 ? RosterUpkeepRules.Whole(missing, _dice) : 0;
                    if (points > 0) hero.ApplyStressDamage(points, false, SourceType.STORY, "never_again", 0u);
                    lines.Add(ActivityLedger.HeroName(hero) + " swore never to come back here" + (points > 0 ? ": +" + points + " stress." : "."));
                }
            }
            Sync();
            if (lines.Count > 0) Plugin.Log.LogInfo("Darkest Dungeon: " + string.Join(" ", lines));
            return lines;
        }

        // DD1's never_again_affliction, for as long as the descent lasts.
        private static string StatsOf(uint guid)
        {
            if (!Returned.Contains(guid)) return "";
            var setting = Rules.Setting(_setting);
            if (!setting.BuffIds.Contains(DarkestDungeonRules.NeverAgainBuff) || Math.Abs(Rules.NeverAgainResolveCheck) < 1e-9) return "";
            return new StringBuilder().Append("sub_stat,").Append(ActorStatType.OVERSTRESS_CHANCE_MODIFIER.GetName()).Append(',').Append(Resolute).Append(',')
                .Append(Rules.NeverAgainResolveCheck.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n').ToString();
        }

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
            catch (Exception e) { Plugin.Log.LogError("Darkest Dungeon: a returning hero's stats could not be kept up: " + e); }
        }

        // ---- resolve experience --------------------------------------------------------------------------

        /// <summary>
        /// The share more resolve experience a survivor of a completed quest earns by DD1's two buffs (asked by
        /// <see cref="Resolve"/> as it pays the quest): the veteran's lesson for the party that set out with
        /// one, and the failed descent's for whoever still carries it.
        /// </summary>
        private static double ExperienceBonus(uint guid, EstateState.Expedition expedition)
        {
            double share = 0;
            // (one of the five stand-ins pays neither of the two; the failed descent's is kept for a quest that does)
            if (IsStandIn(expedition?.QuestId)) return share;
            if (Taught.Contains(guid)) share += Rules.VeteranXpBonus;
            if (Emboldened.Contains(guid)) share += Rules.FailureXpBonus;
            return share;
        }

        // ---- the expedition is over ----------------------------------------------------------------------

        // The marks of an expedition's end are made as the week turns: its experience has been paid by then.
        private static void OnWeekAdvanced(int week)
        {
            var expedition = _ended;
            _ended = null;
            if (expedition == null || !EstateSession.Active) return;
            try { Close(expedition); }
            catch (Exception e) { Plugin.Log.LogError("Darkest Dungeon: the expedition's end could not be marked: " + e); }
            Taught.Clear();
            Returned.Clear();
        }

        private static void Close(EstateState.Expedition expedition)
        {
            var notes = new List<string>();
            // one of the five stand-ins makes no veterans, spends nobody's lesson and emboldens nobody
            if (IsStandIn(expedition.QuestId))
            {
                _last = expedition.QuestId + (expedition.Success ? " completed" : " not completed") + ": a stand-in, DD1's rules of the place are not asked";
                return;
            }
            if (expedition.Success)
            {
                // DD1 (duration_type quest_complete): the failed descent's buff is spent on a completed quest
                var spent = expedition.Survivors.Count(guid => Emboldened.Remove(guid));
                if (spent > 0) notes.Add(spent + " spent the failed descent's lesson");
                if (IsDescent(expedition.Dungeon, expedition.QuestId))
                {
                    foreach (var guid in expedition.Survivors) Veterans.Add(guid);
                    notes.Add(expedition.Survivors.Count + " now know the place");
                }
            }
            else if (FailureEmboldens(expedition))
            {
                var living = RosterLifecycle.LivingGuids();
                foreach (var guid in living) Emboldened.Add(guid);
                notes.Add("the estate is emboldened (" + living.Count + " heroes)");
                // DD1's own words for it
                QuestBoard.Report(Dd1Strings.Format(Dd1Strings.Get("str_darkest_dungeon_failure_bonus")
                                                    ?? "Failure in the Darkest Dungeon has only strengthened the resolve of those left behind. (x2 Resolve XP Bonus in the next quest.)"));
            }
            _last = (expedition.QuestId ?? expedition.Dungeon) + (expedition.Success ? " completed" : " not completed") + (notes.Count > 0 ? ": " + string.Join("; ", notes) : "");
            if (notes.Count > 0) Plugin.Log.LogInfo("Darkest Dungeon: " + _last);
        }

        /// <summary>
        /// DD1's condition for a failed plot quest's roster buffs: the quest is in the Darkest Dungeon and
        /// names them, and every hero of its party is of the resolve level it asks for
        /// (roster_buff_on_failure_minimum_party_resolve_level). GUESS: the party's fallen are counted with
        /// the living, at the level they set out with (DD1 goes through its list of the quest's heroes; whether
        /// the dead are on it was not found).
        /// </summary>
        private static bool FailureEmboldens(EstateState.Expedition expedition)
        {
            var plot = expedition.Dd1Plot;
            if (plot == null || !IsDescent(expedition.Dungeon, expedition.QuestId) || !plot.RosterBuffsOnFailure.Contains(DarkestDungeonRules.FailureBuff)) return false;
            return DarkestDungeonRules.PartyQualifies(expedition.PartyLevels, plot.RosterBuffMinPartyLevel);
        }

        private static void Forget(uint guid)
        {
            Veterans.Remove(guid);
            Emboldened.Remove(guid);
            Taught.Remove(guid);
            Returned.Remove(guid);
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JArray Guids(IEnumerable<uint> guids) => new JArray(guids.Select(g => (object)g).ToArray());

        private static void Read(JToken token, HashSet<uint> into)
        {
            foreach (var guid in token as JArray ?? new JArray())
                if (guid.Type == JTokenType.Integer) into.Add((uint)guid);
        }

        private static JToken Save()
        {
            return new JObject
            {
                ["never_again"] = _setting,
                ["veterans"] = Guids(Veterans),
                ["emboldened"] = Guids(Emboldened),
                ["seed"] = _seed.ToString(CultureInfo.InvariantCulture),
                ["taught"] = Guids(Taught),
                ["returned"] = Guids(Returned)
            };
        }

        private static void Load(JToken token)
        {
            Reset();
            if (!(token is JObject json)) return;
            _setting = (string)json["never_again"] ?? DarkestDungeonRules.DefaultSetting;
            Read(json["veterans"], Veterans);
            Read(json["emboldened"], Emboldened);
            long.TryParse((string)json["seed"], NumberStyles.Integer, CultureInfo.InvariantCulture, out _seed);
            Read(json["taught"], Taught);
            Read(json["returned"], Returned);
        }

        private static void Reset()
        {
            _setting = DarkestDungeonRules.DefaultSetting;
            Veterans.Clear();
            Emboldened.Clear();
            Taught.Clear();
            Returned.Clear();
            _seed = 0;
            _ended = null;
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
                roster.Add(new
                {
                    guid, name = actor.ActorName, level = Resolve.Level(guid), xp = Resolve.Experience(guid), stress = actor.Stress,
                    veteran = Veterans.Contains(guid), emboldened = Emboldened.Contains(guid), taught = Taught.Contains(guid), returned = Returned.Contains(guid),
                    carries = Stats.TextOn(guid)?.Trim()
                });
            }
            var run = DungeonRun.Current;
            return new
            {
                dd1 = new
                {
                    neverAgain = rules.NeverAgain.Select(s => new { id = s.Id, canEnter = s.CanEnter, buffs = s.BuffIds, minimumStress = s.MinimumStress }).ToList(),
                    veteranXpBonus = rules.VeteranXpBonus, failureXpBonus = rules.FailureXpBonus, neverAgainResolveCheck = rules.NeverAgainResolveCheck
                },
                setting = _setting,
                // the place itself: the setting [Rules] DarkestDungeon (bosses, dd1, shut), what the map says of it, and its quests on the board (none while shut)
                mode = DarkestBossRules.NameOf(Mode),
                open = !Sealed,
                map = new { open = QuestBoard.IsOpen(QuestBoard.DarkestDungeon), locked = QuestBoard.LockReason(QuestBoard.DarkestDungeon) },
                questsOnTheBoard = QuestBoard.Current().Where(q => q.Dungeon == QuestBoard.DarkestDungeon).Select(q => q.Id).ToList(),
                expedition = run == null ? null : run.DescribeRules(),
                last = _last,
                roster
            };
        }

        /// <summary>
        /// Test commands for the dev bridge:
        ///   darkest.state                          DD1's numbers, the setting, every hero's marks, the expedition's rules;
        ///                                          "mode", what the map says of the place, its quests on the board
        ///   darkest.mode [mode=bosses|dd1|shut]    what the place is, for this session (the setting [Rules] DarkestDungeon is
        ///                                          read when the game starts); the board and an open map follow at once.
        ///                                          The five stand-ins have commands of their own: bosses.*
        ///   darkest.embark [quest=plot_darkest_1]  tries to set out on a quest of the place past the board, as nothing in the
        ///                                          game can: says what stopped it (shut), and starts nothing while it is shut
        ///   darkest.setting id=strict|permissive   DD1's option "Never Again"
        ///   darkest.veteran guid=3 [on=false]      marks a hero as one who has finished a quest there (or takes the mark off)
        ///   darkest.emboldened [guid=3] [on=false] the failed descent's buff on a hero, or on the whole estate without a guid
        ///   darkest.refusal guid=3                 what the hero says to a descent now (null: they go)
        ///   darkest.bonus guid=3                   the share more experience the hero would earn on the expedition under way
        ///   darkest.end [success=true] [levels=5,5,6,6] [plot=plot_darkest_dungeon_1]
        ///                                          the marks of a descent's end made now for the party as it stands, without an expedition
        ///                                          and without experience: completed (they become veterans, an emboldened hero's buff is
        ///                                          spent) or not (the estate is emboldened if the levels, the party's own unless given, are
        ///                                          all at the quest's minimum). No week passes.
        /// </summary>
        private static void RegisterBridge()
        {
            AgentBridge.Register("darkest.state", o => Describe());
            AgentBridge.Register("darkest.mode", o =>
            {
                if (o["mode"] != null)
                {
                    var mode = DarkestBossRules.ParseMode((string)o["mode"], out var known);
                    if (!known) return "one of: bosses, dd1, shut";
                    Mode = mode;
                }
                if (QuestPanel.IsOpen) QuestPanel.DevOpen();
                return new
                {
                    mode = DarkestBossRules.NameOf(Mode), mapOpen = QuestBoard.IsOpen(QuestBoard.DarkestDungeon), locked = QuestBoard.LockReason(QuestBoard.DarkestDungeon),
                    tooltip = QuestMapText.RegionTooltip(QuestBoard.DarkestDungeon, QuestBoard.Progress(QuestBoard.DarkestDungeon), false),
                    questsOnTheBoard = QuestBoard.Current().Where(q => q.Dungeon == QuestBoard.DarkestDungeon).Select(q => q.Id).ToList()
                };
            });
            AgentBridge.Register("darkest.embark", o =>
            {
                if (!EstateSession.InHub || DungeonRun.Current != null) return "not in the hamlet";
                var quest = new Quest
                {
                    Id = (string)o["quest"] ?? "plot_darkest_1", Name = "dev", Dungeon = QuestBoard.DarkestDungeon, Type = QuestTypes.Explore, Tier = "darkest", LengthName = "short", Plot = true
                };
                var stopped = QuestBoard.EmbarkBlockReason(quest);
                // only a refusal is tried: with the place open this command starts nothing either
                if (stopped != null) QuestBoard.Embark(quest);
                return new { quest = quest.Id, stopped, expedition = DungeonRun.Current != null };
            });
            AgentBridge.Register("darkest.setting", o =>
            {
                var id = (string)o["id"];
                if (id != null && Rules.NeverAgain.All(s => s.Id != id)) return "DD1 has: " + string.Join(", ", Rules.NeverAgain.Select(s => s.Id));
                if (id != null) _setting = id;
                return new { setting = _setting, canEnter = Rules.Setting(_setting).CanEnter, minimumStress = Rules.Setting(_setting).MinimumStress };
            });
            AgentBridge.Register("darkest.veteran", o =>
            {
                var guid = (uint?)o["guid"] ?? 0u;
                if (SanitariumLedger.Actor(guid) == null) return "no such hero";
                if ((bool?)o["on"] ?? true) Veterans.Add(guid);
                else Veterans.Remove(guid);
                return Describe();
            });
            AgentBridge.Register("darkest.emboldened", o =>
            {
                var on = (bool?)o["on"] ?? true;
                var heroes = o["guid"] != null ? new List<uint> { (uint)o["guid"] } : RosterLifecycle.LivingGuids();
                foreach (var guid in heroes)
                {
                    if (on) Emboldened.Add(guid);
                    else Emboldened.Remove(guid);
                }
                return Describe();
            });
            AgentBridge.Register("darkest.refusal", o =>
            {
                var guid = (uint?)o["guid"] ?? 0u;
                // {"quest":"darkest_boss_brain"}: what the hero says to that quest (one of the five stand-ins: nothing)
                return new { guid, veteran = IsVeteran(guid), setting = _setting, says = Refusal(guid, new Quest { Id = (string)o["quest"], Dungeon = QuestBoard.DarkestDungeon }) };
            });
            AgentBridge.Register("darkest.bonus", o =>
            {
                var guid = (uint?)o["guid"] ?? 0u;
                var all = Resolve.BonusShare(guid, null);
                return new
                {
                    guid, taught = Taught.Contains(guid), emboldened = Emboldened.Contains(guid), share = ExperienceBonus(guid, null),
                    shareWithEveryBuff = all, sixteenBecomes = DarkestDungeonRules.WithBonus(16, all)
                };
            });
            AgentBridge.Register("darkest.end", o =>
            {
                if (!EstateSession.Active) return "no estate";
                var party = Provisioning.Party();
                var levels = o["levels"] != null
                    ? ((string)o["levels"]).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList()
                    : party.Select(hero => Resolve.Level(hero.ActorGuid)).ToList();
                var plotId = (string)o["plot"] ?? "plot_darkest_dungeon_1";
                var plot = QuestBoard.Rewards.Plot(plotId);
                if (plot == null) return "DD1 has no plot quest " + plotId;
                var expedition = new EstateState.Expedition
                {
                    Dungeon = plot.Dungeon, QuestId = "dev " + plotId, Difficulty = plot.Difficulty, Length = plot.Length, Success = (bool?)o["success"] ?? false,
                    Survivors = party.Select(hero => hero.ActorGuid).ToList(), Party = party.Select(hero => hero.ActorGuid).ToList(), PartyLevels = levels, Dd1Plot = plot
                };
                var qualifies = !expedition.Success && FailureEmboldens(expedition);
                Close(expedition);
                return new { plot = plotId, success = expedition.Success, partyLevels = levels, minimumLevel = plot.RosterBuffMinPartyLevel, failureEmboldens = qualifies, state = Describe() };
            });
        }
    }
}
