using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>One torchlight band of DD1's <c>rules.json</c> "darkness" table, with every modifier the file lists for it.</summary>
    public sealed class LightBand
    {
        public double Lower, Upper;
        /// <summary>DD1 names: stress_damage_increase, monster_damage_increase, player_scouting_increase, loot_increase_gold...</summary>
        public Dictionary<string, double> Values = new Dictionary<string, double>();

        public double Value(string name) => Values.TryGetValue(name, out var value) ? value : 0;
    }

    /// <summary>A trap at one difficulty: DD1 effect ids for the game layer to map, and the hit as a share of max health.</summary>
    public sealed class TrapLevel
    {
        public int Level;
        public string[] SuccessEffects = new string[0];
        public string[] FailEffects = new string[0];
        /// <summary>Negative: share of max health lost.</summary>
        public double Health;
    }

    public sealed class TrapDef
    {
        public string Id;
        /// <summary>Level 0 is the base; DD1 lists changes for levels 3 and 5.</summary>
        public List<TrapLevel> Levels = new List<TrapLevel>();

        public TrapLevel For(int tier)
        {
            var best = Levels[0];
            foreach (var level in Levels)
                if (level.Level <= tier && level.Level >= best.Level) best = level;
            return best;
        }
    }

    /// <summary>What forcing through an obstacle without the clearing item costs.</summary>
    public sealed class ObstacleRules
    {
        public string ClearItem = "shovel";
        public string[] FailEffects = new string[0];
        public double Health;
        public double Torchlight;
    }

    /// <summary>Content that can turn up in a hallway already walked (<c>corridor_return_content</c>).</summary>
    public sealed class ReturnContent
    {
        public HallContent Content;
        public double BaseChance;
        internal List<RaidRules.Range> DarkMods = new List<RaidRules.Range>();
        internal bool LowestExcluded;

        public double ChanceAt(double light) => BaseChance + RaidRules.Lookup(DarkMods, LowestExcluded, light);
    }

    /// <summary>
    /// DD1 raid rules read from the install: <c>shared/rules.json</c>, <c>props/*.json</c>,
    /// <c>campaign/quest/quest.exit_penalty.json</c>. The initial values of the fields are FALLBACKS for a missing
    /// file or key, not a copy of the DD1 tables.
    /// </summary>
    public sealed class RaidRules
    {
        internal sealed class Range
        {
            public double Lower, Upper;
            public JToken Value;
        }

        private JObject _raw = new JObject();
        private bool _bandsLowestExcluded = true;

        public const double MaxLight = 100;

        public List<LightBand> LightBands = new List<LightBand>();
        /// <summary>Torchlight lost per hallway tile: one already walked, one scouted but not walked, one unknown.</summary>
        public double LightLossVisited = 1, LightLossScouted = 6, LightLossUnknown = 6;
        // DD1 behaviour, not in data: the item list gives the torch no light value, the game adds 25 per torch.
        public double TorchLight = 25;

        public double ScoutChanceBase = 0.25, ScoutCritChance = 0.5;
        public double WalkStress = 2, WalkStressChance = 0.3, BackingUpStress = 5, BackingUpStressChance = 0.55;
        /// <summary>Hunger tile: share of max health healed when the party eats, lost when it starves.</summary>
        public double HungerHeal = 0.05, HungerStarveDamage = 0.2;
        public double TrapScoutDisarmBonus = 0.4;
        public double[] TrapDifficultyPenalty = new double[0];
        public double AbandonStress = 20;

        public List<ReturnContent> ReturnContents = new List<ReturnContent>();
        public Dictionary<string, TrapDef> Traps = new Dictionary<string, TrapDef>();
        public ObstacleRules Obstacle = new ObstacleRules();

        public static RaidRules Load(IDd1Files files)
        {
            var rules = new RaidRules();
            var raw = Json.ParseFile(files.ReadText("shared/rules.json"));
            if (raw != null)
            {
                rules._raw = raw;
                rules.ReadRules(raw);
            }
            rules.ReadProps(files);
            var penalty = Json.ParseFile(files.ReadText("campaign/quest/quest.exit_penalty.json"));
            if (penalty != null && penalty["fail_penalty"] != null)
                rules.AbandonStress = Json.Number(penalty["fail_penalty"]["stress_damage"], rules.AbandonStress);
            if (rules.LightBands.Count == 0)
                rules.LightBands.Add(new LightBand { Lower = double.MinValue, Upper = double.MaxValue });
            var settings = Json.ParseFile(files.ReadText("scripts/raid_settings.json"));
            foreach (var entry in Json.Array(settings?["torch_settings_data_table"]))
            {
                var key = (string)entry["key"];
                var data = entry["data"];
                if (key != null && data != null && data.Type == JTokenType.Object) rules.TorchBurnDown[key] = Json.Bool(data["burn_down_enabled"], true);
            }
            return rules;
        }

        /// <summary><c>scripts/raid_settings.json</c> <c>torch_settings_data_table</c>: whether the torch burns down, by setting ("default", "dd4").</summary>
        public Dictionary<string, bool> TorchBurnDown = new Dictionary<string, bool>();

        /// <summary>
        /// Whether walking costs torchlight under a quest's torch setting (a plot quest's <c>torch_setting</c>;
        /// null: the default). DD1's last descent is the one quest whose setting, "dd4", says it does not.
        /// A setting the file does not know burns as the default does.
        /// </summary>
        public bool TorchBurns(string setting)
        {
            if (setting != null && TorchBurnDown.TryGetValue(setting, out var burns)) return burns;
            return !TorchBurnDown.TryGetValue("default", out var usual) || usual;
        }

        /// <summary>Any other number of <c>rules.json</c> by its DD1 key (surprise chances, camping, meals...).</summary>
        public double Number(string key, double fallback) => Json.Number(_raw[key], fallback);

        private void ReadRules(JObject raw)
        {
            foreach (var entry in Json.Array(raw["tile_light_loss"]))
            {
                var loss = Json.Number(entry["data"], -1);
                if (loss < 0) continue;
                switch ((string)entry["key"])
                {
                    case "clear": LightLossVisited = loss; break;
                    case "dim": LightLossScouted = loss; break;
                    case "dark": LightLossUnknown = loss; break;
                }
            }

            UseDarkness(raw["darkness"]);

            ScoutChanceBase = Number("scouting_chance_base", ScoutChanceBase);
            ScoutCritChance = Number("scouting_crit_success", ScoutCritChance);
            HungerHeal = Number("hallway_hunger_HPrestore", HungerHeal);
            HungerStarveDamage = Number("hallway_hunger_starve_HPdmg", HungerStarveDamage);
            TrapScoutDisarmBonus = Number("trap_scout_disarm_bonus", TrapScoutDisarmBonus);
            TrapDifficultyPenalty = Json.Array(raw["difficulty_trap_base"]).Select(t => Json.Number(t, 0)).ToArray();

            var stress = raw["hallway_stress"];
            if (stress != null && stress.Type == JTokenType.Object)
            {
                WalkStress = Json.Number(stress["hallway_per_tile_stress_damage_fwd"], WalkStress);
                WalkStressChance = Json.Number(stress["hallway_per_tile_stress_damage_chance_fwd"], WalkStressChance);
                BackingUpStress = Json.Number(stress["hallway_per_tile_stress_damage_backing_up"], BackingUpStress);
                BackingUpStressChance = Json.Number(stress["hallway_per_tile_stress_damage_chance_backing_up"], BackingUpStressChance);
            }

            foreach (var entry in Json.Array(raw["corridor_return_content"]))
            {
                HallContent content;
                switch ((string)entry["key"])
                {
                    case "ac_battle": content = HallContent.Battle; break;
                    case "ac_hunger": content = HallContent.Hunger; break;
                    case "ac_trap": content = HallContent.Trap; break;
                    default: continue;
                }
                var data = entry["data"];
                if (data == null || data.Type != JTokenType.Object) continue;
                var item = new ReturnContent { Content = content, BaseChance = Json.Number(data["base_chance"], 0) };
                item.DarkMods = ReadRanges(data["base_dark_mod_table"], out item.LowestExcluded);
                ReturnContents.Add(item);
            }
        }

        /// <summary>
        /// Puts a torchlight table in the place of the one in force: an object written as rules.json writes its
        /// "darkness" (DD1's Cartographer's Camp carries one of its own). A table without bands changes nothing.
        /// </summary>
        public bool UseDarkness(JToken table)
        {
            var bands = new List<LightBand>();
            foreach (var range in ReadRanges(table, out var lowestExcluded))
            {
                var band = new LightBand { Lower = range.Lower, Upper = range.Upper };
                if (range.Value is JObject values)
                    foreach (var p in values.Properties())
                        band.Values[p.Name] = Json.Number(p.Value, 0);
                bands.Add(band);
                _bandsLowestExcluded = lowestExcluded;
            }
            if (bands.Count == 0) return false;
            // brightest first, whatever the order in the file
            LightBands = bands.OrderByDescending(b => b.Lower).ToList();
            return true;
        }

        private static List<Range> ReadRanges(JToken table, out bool lowestExcluded)
        {
            var ranges = new List<Range>();
            lowestExcluded = true;
            if (table == null || table.Type != JTokenType.Object) return ranges;
            lowestExcluded = Json.Bool(table["lowest_excluded"], true);
            foreach (var entry in Json.Array(table["range_table"]))
            {
                var range = entry["range"];
                if (range == null || range.Type != JTokenType.Object) continue;
                ranges.Add(new Range
                {
                    Lower = Json.Number(range["lower"], double.MinValue),
                    Upper = Json.Number(range["upper"], double.MaxValue),
                    Value = entry["value"]
                });
            }
            return ranges;
        }

        // lowest_excluded: a range is (lower, upper], so torch 75 is in the 50-75 band and torch 0 in the lowest one
        private static bool Inside(double lower, double upper, bool lowestExcluded, double x)
        {
            return lowestExcluded ? x > lower && x <= upper : x >= lower && x < upper;
        }

        internal static double Lookup(List<Range> ranges, bool lowestExcluded, double x)
        {
            foreach (var range in ranges)
                if (Inside(range.Lower, range.Upper, lowestExcluded, x)) return Json.Number(range.Value, 0);
            return 0;
        }

        /// <summary>Index into <see cref="LightBands"/>: 0 is the brightest band.</summary>
        public int BandIndex(double light)
        {
            for (var i = 0; i < LightBands.Count; i++)
                if (Inside(LightBands[i].Lower, LightBands[i].Upper, _bandsLowestExcluded, light)) return i;
            return LightBands.Count - 1;
        }

        public LightBand BandFor(double light) => LightBands[BandIndex(light)];

        /// <summary>What a dungeon's difficulty takes off a hero's trap disarm chance (<c>difficulty_trap_base</c>).</summary>
        public double TrapPenalty(int tier)
        {
            return tier >= 0 && tier < TrapDifficultyPenalty.Length ? TrapDifficultyPenalty[tier] : 0;
        }

        private void ReadProps(IDd1Files files)
        {
            var baseTrap = new TrapLevel();
            var shared = Json.ParseFile(files.ReadText("props/prop_definitions.json"));
            if (shared != null)
            {
                var interactionItems = new Dictionary<string, string>();
                foreach (var type in Json.Array(shared["interaction_types"]))
                {
                    var item = Json.Array(type["item_interaction_table"]).FirstOrDefault();
                    if (item != null && item["item_name"] != null) interactionItems[(string)type["name"] ?? ""] = (string)item["item_name"];
                }
                foreach (var prop in Json.Array(shared["props"]))
                {
                    var data = prop["default_data"];
                    if (data == null || data.Type != JTokenType.Object) continue;
                    if ((string)prop["name"] == "trap") baseTrap = ReadTrapLevel(data, 0, baseTrap);
                    if ((string)prop["name"] != "obstacle") continue;
                    Obstacle.FailEffects = Strings(data["fail_effects"]) ?? Obstacle.FailEffects;
                    Obstacle.Health = Json.Number(data["health"], 0);
                    Obstacle.Torchlight = Json.Number(data["torchlight"], 0);
                    if (interactionItems.TryGetValue((string)data["interaction_type"] ?? "", out var clearItem)) Obstacle.ClearItem = clearItem;
                }
            }

            var traps = Json.ParseFile(files.ReadText("props/trap_definitions.json"));
            if (traps == null) return;
            foreach (var prop in Json.Array(traps["props"]))
            {
                var id = (string)prop["name"];
                var data = prop["default_data"];
                if (id == null || data == null || data.Type != JTokenType.Object) continue;
                var trap = new TrapDef { Id = id };
                trap.Levels.Add(ReadTrapLevel(data, 0, baseTrap));
                foreach (var variation in Json.Array(prop["difficulty_variations"]))
                    trap.Levels.Add(ReadTrapLevel(variation, Json.Int(variation["level"], 0), trap.Levels[0]));
                Traps[id] = trap;
            }
        }

        private static TrapLevel ReadTrapLevel(JToken data, int level, TrapLevel inherited)
        {
            return new TrapLevel
            {
                Level = level,
                SuccessEffects = Strings(data["success_effects"]) ?? inherited.SuccessEffects,
                FailEffects = Strings(data["fail_effects"]) ?? inherited.FailEffects,
                Health = Json.Number(data["health"], inherited.Health)
            };
        }

        private static string[] Strings(JToken token)
        {
            return token is JArray array ? array.Select(t => (string)t).Where(s => s != null).ToArray() : null;
        }
    }
}
