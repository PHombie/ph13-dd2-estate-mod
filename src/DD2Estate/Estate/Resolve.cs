using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's resolve levels for DD2 heroes: experience from finished quests, level 0..6, and the rule that
    /// seasoned heroes refuse quests beneath them. Thresholds, experience per quest and the level caps are read
    /// from the player's DD1 install (<c>campaign/roster/roster.variables.json</c>,
    /// <c>campaign/quest/quest.generation.json</c>, <c>campaign/quest/quest.restriction.json</c>).
    /// The Guild and the Blacksmith cap what they sell by this level, as in DD1.
    /// </summary>
    [EstateModule]
    internal static class Resolve
    {
        public const int MaxLevel = 6;

        // Fallbacks = the DD1 values, used only when a file cannot be read.
        private static int[] _thresholds = { 0, 2, 8, 14, 24, 36, 48 };
        private static int[] _levelCapByDifficulty = { 2, 2, 3, 4, 5, 99, 99 };
        private static readonly Dictionary<int, int[]> XpByDifficulty = new Dictionary<int, int[]>
        {
            { 1, new[] { 0, 2, 3, 4 } }, { 3, new[] { 0, 4, 6, 8 } }, { 5, new[] { 0, 8, 12, 16 } }
        };

        private static readonly Dictionary<uint, int> Xp = new Dictionary<uint, int>();
        private static bool _rulesLoaded;

        public static void Register()
        {
            EstateState.RegisterSection("resolve", Save, Load, () => Xp.Clear());
            EstateState.ExpeditionEnded += OnExpeditionEnded;
            RosterLifecycle.Left += Forget;
            // DD1 prices the town's comforts by the hero's level.
            ActivityRules.HeroLevel = Level;
        }

        private static JToken Save()
        {
            var json = new JObject();
            foreach (var pair in Xp) json[pair.Key.ToString()] = pair.Value;
            return json;
        }

        private static void Load(JToken json)
        {
            Xp.Clear();
            if (!(json is JObject saved)) return;
            foreach (var p in saved.Properties())
                if (uint.TryParse(p.Name, out var guid)) Xp[guid] = (int)p.Value;
        }

        private static void LoadRules()
        {
            if (_rulesLoaded) return;
            _rulesLoaded = true;
            try
            {
                var roster = Dd1Install.ReadText("campaign/roster/roster.variables.json");
                var thresholds = roster != null ? JObject.Parse(roster).SelectToken("$..resolve_level_thresholds") as JArray : null;
                if (thresholds != null && thresholds.Count > 1) _thresholds = thresholds.Select(t => (int)t).ToArray();

                var restriction = Dd1Install.ReadText("campaign/quest/quest.restriction.json");
                var caps = restriction != null ? JObject.Parse(restriction).SelectToken("$..resolve_level_threshold_table") as JArray : null;
                if (caps != null && caps.Count > 1) _levelCapByDifficulty = caps.Select(t => (int)t).ToArray();

                var generation = Dd1Install.ReadText("campaign/quest/quest.generation.json");
                var table = generation != null ? JObject.Parse(generation).SelectToken("$..resolve_xp_table") as JArray : null;
                if (table != null)
                    for (var difficulty = 0; difficulty < table.Count; difficulty++)
                        if (table[difficulty] is JArray row && row.Count > 1) XpByDifficulty[difficulty] = row.Select(t => (int)t).ToArray();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("resolve rules: DD1 data not readable, using defaults (" + e.Message + ")");
            }
        }

        public static int Experience(uint guid) => Xp.TryGetValue(guid, out var xp) ? xp : 0;

        public static int Level(uint guid)
        {
            LoadRules();
            var xp = Experience(guid);
            var level = 0;
            for (var i = 0; i < _thresholds.Length && i <= MaxLevel; i++)
                if (xp >= _thresholds[i]) level = i;
            return level;
        }

        /// <summary>How far the hero is from its level's threshold to the next one's, 0..1 (1 at the top): DD1's bar under the badge.</summary>
        public static float Progress(uint guid)
        {
            var level = Level(guid);
            if (level >= MaxLevel || level + 1 >= _thresholds.Length) return 1f;
            var span = _thresholds[level + 1] - _thresholds[level];
            return span <= 0 ? 1f : UnityEngine.Mathf.Clamp01((Experience(guid) - _thresholds[level]) / (float)span);
        }

        /// <summary>Experience still missing for the next level; 0 at the top.</summary>
        public static int ToNextLevel(uint guid)
        {
            var level = Level(guid);
            return level >= MaxLevel || level + 1 >= _thresholds.Length ? 0 : _thresholds[level + 1] - Experience(guid);
        }

        /// <summary>Highest resolve level DD1 lets on a quest of this difficulty (1, 3, 5, 6).</summary>
        public static int LevelCap(int difficulty)
        {
            LoadRules();
            return difficulty >= 0 && difficulty < _levelCapByDifficulty.Length ? _levelCapByDifficulty[difficulty] : 99;
        }

        /// <summary>Null when the hero may go; otherwise why not ("Level 4: will not take apprentice work").</summary>
        public static string RefusalReason(uint guid, int difficulty)
        {
            if (TownEventHooks.LevelRestrictionLifted) return null;      // DD1 town event: any hero takes any quest this week
            var level = Level(guid);
            return level > LevelCap(difficulty) ? "Resolve " + level + ": this quest is beneath them" : null;
        }

        /// <summary>
        /// Null when the hero may go on this quest; otherwise why not: it is beneath them, or (DD1's "Never
        /// Again") it is a descent into the Darkest Dungeon and they have finished one.
        /// </summary>
        public static string RefusalReason(uint guid, Quest quest)
        {
            if (quest == null) return null;
            return DarkestDungeon.Refusal(guid, quest) ?? RefusalReason(guid, quest.Difficulty);
        }

        public static void Forget(uint guid) => Xp.Remove(guid);

        /// <summary>Dev/test and town events: add experience directly.</summary>
        public static void Grant(uint guid, int xp)
        {
            Xp[guid] = Math.Max(0, Experience(guid) + xp);
        }

        /// <summary>The resolve level a sum of experience comes to (a hero's before a quest, or a dead hero's).</summary>
        public static int LevelOf(int experience)
        {
            LoadRules();
            return RaidResultRules.Level(experience, _thresholds, MaxLevel);
        }

        /// <summary>How far a sum of experience is from its level to the next, 0..1 (1 at the top): DD1's bar under the badge.</summary>
        public static float Share(int experience)
        {
            LoadRules();
            return (float)RaidResultRules.LevelShare(experience, _thresholds, MaxLevel);
        }

        /// <summary>The experience a level starts at; the top level's for anything above it.</summary>
        public static int Threshold(int level)
        {
            LoadRules();
            return _thresholds[Math.Min(Math.Max(level, 0), Math.Min(MaxLevel, _thresholds.Length - 1))];
        }

        /// <summary>
        /// DD1's buffs to the experience of a quest (resolve_xp_bonus_percent): each answers with the share a
        /// survivor earns on top (0.5: half as much again), or 0. Systems that give such a buff add theirs at
        /// start-up (the town events' "on next quest", the Darkest Dungeon's two).
        /// </summary>
        public static readonly List<Func<uint, EstateState.Expedition, double>> ExperienceBonuses = new List<Func<uint, EstateState.Expedition, double>>();

        /// <summary>The share more experience a survivor of this expedition earns, all buffs added up.</summary>
        public static double BonusShare(uint guid, EstateState.Expedition expedition)
        {
            double share = 0;
            foreach (var bonus in ExperienceBonuses)
            {
                try { share += Math.Max(0, bonus(guid, expedition)); }
                catch (Exception e) { Plugin.Log.LogError("resolve: an experience bonus failed: " + e); }
            }
            return share;
        }

        // DD1 pays experience for a completed quest only, and only to those alive at its end: the table's by
        // difficulty and length, or the quest's own. Its plot quests all state one (a boss 4, 8 or 16 where
        // the table's medium column has 3, 6 and 12; a descent into the Darkest Dungeon 16, where the table's
        // row of difficulty 6 is all zero). A hero's buffs to it are added up and their share of the quest's
        // pay, rounded up, is paid on top.
        private static void OnExpeditionEnded(EstateState.Expedition expedition)
        {
            if (!expedition.Success) return;
            LoadRules();
            if (!XpByDifficulty.TryGetValue(expedition.Difficulty, out var row)) row = XpByDifficulty[1];
            var gained = RaidResultRules.QuestExperience(expedition.ResolveXp, row, expedition.Length);
            foreach (var guid in expedition.Survivors)
            {
                var share = BonusShare(guid, expedition);
                var paid = DarkestDungeonRules.WithBonus(gained, share);
                if (paid != gained) Plugin.Log.LogInfo("resolve: #" + guid + " earns " + paid + " experience (" + gained + " +" + (share * 100).ToString("0") + "%)");
                Grant(guid, paid);
            }
        }
    }
}
