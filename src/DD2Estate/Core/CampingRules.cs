using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>Who a camping skill's effect lands on, spelled as in DD1's <c>*.camping_skills.json</c>.</summary>
    public static class CampSelections
    {
        public const string Self = "self";
        /// <summary>One companion the player picks (never the hero using the skill).</summary>
        public const string Individual = "individual";
        /// <summary>Every companion, not the hero using the skill.</summary>
        public const string PartyOther = "party_other";
        public const string Party = "party";
    }

    /// <summary>DD1's camping effect types, spelled as in <c>*.camping_skills.json</c>.</summary>
    public static class CampEffectTypes
    {
        public const string StressHeal = "stress_heal_amount";
        public const string StressDamage = "stress_damage_amount";
        public const string HealthHeal = "health_heal_max_health_percent";
        public const string HealthDamage = "health_damage_max_health_percent";
        public const string RemoveBleed = "remove_bleeding";
        public const string RemoveBlight = "remove_poison";
        public const string RemoveDisease = "remove_disease";
        public const string RemoveMortality = "remove_deaths_door_recovery_buffs";
        public const string Buff = "buff";
        public const string ReduceAmbush = "reduce_ambush_chance";
        public const string ReduceTorch = "reduce_torch";
        public const string Loot = "loot";
        public const string RefreshUses = "refresh_camp_skill_uses";
    }

    public static class CampRequirements
    {
        public const string Afflicted = "afflicted";
        public const string Religious = "religious";
        public const string NotReligious = "not_religious";
        public const string Mortality = "has_deaths_door_recovery_buffs";
    }

    /// <summary>
    /// A DD1 buff a camping effect hands out by id (<c>shared/buffs/*.buffs.json</c>): what it changes, and the
    /// rule it holds under. The amount in the buff file is a default; the camping skill names its own.
    /// </summary>
    public sealed class CampBuffDef
    {
        public string Id = "";
        /// <summary>DD1 stat: combat_stat_add, combat_stat_multiply, resistance, stress_dmg_received_percent, scouting_chance...</summary>
        public string StatType = "";
        /// <summary>attack_rating, damage_low, crit_chance, speed_rating, bleed, poison...; empty for stats without one.</summary>
        public string SubType = "";
        /// <summary>always, meleeonly, rangedonly, monsterSize, in_rank, firstroundonly, has_buff...</summary>
        public string RuleType = "always";
        public bool RuleFalse;
        public double RuleNumber;
        public string RuleText = "";
        /// <summary>DD1: "combat_end" and 4: a camping buff lasts four fights.</summary>
        public string DurationType = "";
        public int Duration;
        /// <summary>The buff carries an extra effect on every hit (DD1 attack_additional_effect_ids).</summary>
        public bool HasHitEffect;

        public bool Conditional => RuleType != "always";
    }

    public sealed class CampEffect
    {
        public string Selection = CampSelections.Self;
        public string Type = "";
        /// <summary>Buff id for a buff, loot table for loot, empty otherwise.</summary>
        public string SubType = "";
        /// <summary>Effects sharing a code are the outcomes of one roll; <see cref="Chance"/> is the outcome's share of it.</summary>
        public string Code = "";
        public double Chance = 1;
        /// <summary>DD1 units: stress points of 200, shares of max health, the buff's amount, torchlight, loot draws.</summary>
        public double Amount;
        public string[] Requirements = new string[0];
        /// <summary>The buff behind <see cref="SubType"/>; null for other effects and for a buff DD1's files do not list.</summary>
        public CampBuffDef Buff;
    }

    public sealed class CampSkill
    {
        public string Id = "";
        /// <summary>Respite points one use costs.</summary>
        public int Cost;
        /// <summary>Uses per camp.</summary>
        public int UseLimit = 1;
        /// <summary>DD1 gold the Survivalist asks for it.</summary>
        public int PriceGold;
        public readonly List<CampEffect> Effects = new List<CampEffect>();
        /// <summary>DD1 hero classes the files list for it, over every file that defines it.</summary>
        public readonly HashSet<string> Classes = new HashSet<string>();
        /// <summary>The install folder its file sits in ("raid/camping", or a DLC's): its icon is in skill_icons below it.</summary>
        public string Folder = "raid/camping";

        public string IconPath => Folder + "/skill_icons/camp_skill_" + Id + ".png";

        /// <summary>The player picks a companion for it.</summary>
        public bool NeedsTarget => Effects.Any(e => e.Selection == CampSelections.Individual);
    }

    /// <summary>One row of DD1's meals_table.</summary>
    public sealed class MealOption
    {
        /// <summary>none, half, full, feast.</summary>
        public string Type = "";
        /// <summary>Food per hero.</summary>
        public double RationsPer;
        /// <summary>Share of max health: negative starves.</summary>
        public double Healing;
        /// <summary>DD1 stress points (of 200): positive adds stress, negative relieves it.</summary>
        public double Stress;
    }

    /// <summary>
    /// DD1's camping rules read from the player's install: <c>shared/rules.json</c> (respite points, meals,
    /// torchlight, the night ambush), <c>campaign/provision/provision.json</c> (firewood per quest length),
    /// <c>raid/camping/*.camping_skills.json</c> of the game and its DLC (skills, their effects, classes and
    /// price), <c>shared/buffs/*.buffs.json</c> (what a camping buff changes and for how long) and the hero
    /// classes' <c>*.info.darkest</c> (who is religious, how many camping skills a recruit arrives with).
    /// The initial values of the fields are FALLBACKS for a missing file or key.
    /// </summary>
    public sealed class CampingRules
    {
        // DD1 keeps a DLC's files in a folder of its own, laid out like the game's root. These are the ones
        // that carry camping skills, camping buffs or hero classes; a DLC the player does not own is not there.
        internal static readonly string[] Roots =
        {
            "",
            "dlc/445700_musketeer/",
            "dlc/580100_crimson_court/features/flagellant/",
            "dlc/702540_shieldbreaker/",
            "dlc/4964110_fires_edge/features/duelist/",
            "dlc/4964110_fires_edge/features/runaway/"
        };

        /// <summary>
        /// THE MOD'S OWN, used only where DD1 has nothing: a DD2 class without DD1 camping skills of its own
        /// takes the class skills of the DD1 class nearest to it. (With DD1's Fire's Edge DLC installed the
        /// runaway and the duelist have their own and this table is not consulted.) The runaway, a scavenging
        /// scout, camps like the grave robber (scouting, pilfering, a cure); the duelist, a fencing master,
        /// like the man-at-arms (drill, instruction, kept equipment).
        /// </summary>
        public static readonly Dictionary<string, string> Analogues = new Dictionary<string, string>
        {
            { "runaway", "grave_robber" },
            { "duelist", "man_at_arms" }
        };

        private readonly IDd1Files _files;
        private readonly List<CampSkill> _skills = new List<CampSkill>();
        private readonly Dictionary<string, CampSkill> _byId = new Dictionary<string, CampSkill>();
        private readonly Dictionary<string, CampBuffDef> _buffs = new Dictionary<string, CampBuffDef>();
        private readonly List<int> _firewood = new List<int>();
        private readonly Dictionary<string, ClassInfo> _classes = new Dictionary<string, ClassInfo>();

        private sealed class ClassInfo
        {
            public bool Found, Religious;
            public int ClassSkills = 2, SharedSkills = 1;
        }

        public readonly List<MealOption> Meals = new List<MealOption>();
        /// <summary>Respite points a camp starts with (camp_start_camping_points).</summary>
        public int StartPoints = 12;
        /// <summary>Torchlight a camp adds (camp_restore_torch) and an ambush takes (ambush_torch_reduction).</summary>
        public double RestoreTorch = 100, AmbushTorch = -100;
        /// <summary>DD1 stress every hero sheds by camping at all (camp_relieve_stress; 0 in the stock rules).</summary>
        public double RelieveStress;
        /// <summary>Chance the party is attacked in its sleep (ambush_camping_base_chance).</summary>
        public double AmbushChance = 0.33;
        /// <summary>In a night ambush: chance the party is surprised, chance the monsters are (surprise_ambush_*_base_chance).</summary>
        public double AmbushPartySurprise = 1, AmbushMonstersSurprise;
        /// <summary>A skill listed for this many classes or more is a shared one (class_specific_number_of_classes_threshold).</summary>
        public int SharedThreshold = 4;
        /// <summary>
        /// Camping skills a hero may have ready for an expedition. rules.json says 3
        /// (max_number_of_camping_skills), the number a recruit arrives with; the game's own tutorial text says
        /// 4 (<see cref="ReadActiveLimit"/>), which is what DD1 plays by.
        /// </summary>
        public int ActiveLimit = 4;

        public IReadOnlyList<CampSkill> Skills => _skills;

        private CampingRules(IDd1Files files) { _files = files; }

        public static CampingRules Load(IDd1Files files)
        {
            var rules = new CampingRules(files);
            var raw = Json.ParseFile(files.ReadText("shared/rules.json"));
            if (raw != null)
            {
                rules.StartPoints = Json.Int(raw["camp_start_camping_points"], rules.StartPoints);
                rules.RestoreTorch = Json.Number(raw["camp_restore_torch"], rules.RestoreTorch);
                rules.AmbushTorch = Json.Number(raw["ambush_torch_reduction"], rules.AmbushTorch);
                rules.RelieveStress = Json.Number(raw["camp_relieve_stress"], rules.RelieveStress);
                rules.AmbushChance = Json.Number(raw["ambush_camping_base_chance"], rules.AmbushChance);
                rules.AmbushPartySurprise = Json.Number(raw["surprise_ambush_party_base_chance"], rules.AmbushPartySurprise);
                rules.AmbushMonstersSurprise = Json.Number(raw["surprise_ambush_monsters_base_chance"], rules.AmbushMonstersSurprise);
                rules.ActiveLimit = Math.Max(rules.ActiveLimit, Json.Int(raw["max_number_of_camping_skills"], 0));
                foreach (var meal in Json.Array(raw["meals_table"]))
                {
                    var type = (string)meal["type"];
                    if (string.IsNullOrEmpty(type)) continue;
                    rules.Meals.Add(new MealOption
                    {
                        Type = type,
                        RationsPer = Json.Number(meal["rations_per"], 0),
                        Healing = Json.Number(meal["healing"], 0),
                        Stress = Json.Number(meal["stress"], 0)
                    });
                }
            }
            if (rules.Meals.Count == 0)
            {
                // FALLBACK: going without is always possible; what it costs is DD1's stock "none" row.
                rules.Meals.Add(new MealOption { Type = "none", Healing = -0.20, Stress = 15 });
            }

            var provision = Json.ParseFile(files.ReadText("campaign/provision/provision.json"));
            if (provision != null)
                foreach (var list in Json.Array(provision["raid_starting_length_inventory_item_lists"]))
                {
                    var amount = 0;
                    foreach (var entry in Json.Array(list))
                        if ((string)entry["type"] == ItemTypes.Supply && (string)entry["id"] == "firewood") amount += Json.Int(entry["amount"], 0);
                    rules._firewood.Add(amount);
                }

            foreach (var root in Roots) rules.ReadBuffs(root);
            foreach (var root in Roots) rules.ReadSkills(root);
            return rules;
        }

        private void ReadBuffs(string root)
        {
            var dir = root + "shared/buffs";
            foreach (var file in _files.List(dir, "*.buffs.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var json = Json.ParseFile(_files.ReadText(dir + "/" + file));
                if (json == null) continue;
                foreach (var b in Json.Array(json["buffs"]))
                {
                    var id = (string)b["id"];
                    if (string.IsNullOrEmpty(id) || _buffs.ContainsKey(id)) continue;
                    var rule = b["rule_data"];
                    _buffs[id] = new CampBuffDef
                    {
                        Id = id,
                        StatType = (string)b["stat_type"] ?? "",
                        SubType = (string)b["stat_sub_type"] ?? "",
                        RuleType = (string)b["rule_type"] ?? "always",
                        RuleFalse = Json.Bool(b["is_false_rule"], false),
                        RuleNumber = Json.Number(rule?["float"], 0),
                        RuleText = (string)rule?["string"] ?? "",
                        DurationType = (string)b["duration_type"] ?? "",
                        Duration = Json.Int(b["duration"], 0),
                        HasHitEffect = Json.Array(b["attack_additional_effect_ids"]).Any()
                    };
                }
            }
        }

        private void ReadSkills(string root)
        {
            var dir = root + "raid/camping";
            foreach (var file in _files.List(dir, "*.camping_skills.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var json = Json.ParseFile(_files.ReadText(dir + "/" + file));
                if (json == null) continue;
                if (root.Length == 0) SharedThreshold = Json.Int(json["configuration"]?["class_specific_number_of_classes_threshold"], SharedThreshold);
                foreach (var s in Json.Array(json["skills"]))
                {
                    var id = (string)s["id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    // A DLC lists a shared skill again for its own class (or ships the whole stock file with
                    // its class added): the classes add up, the first definition stands.
                    if (!_byId.TryGetValue(id, out var skill))
                    {
                        skill = ReadSkill(id, s, dir);
                        _byId[id] = skill;
                        _skills.Add(skill);
                    }
                    foreach (var c in Json.Array(s["hero_classes"]))
                        if ((string)c != null) skill.Classes.Add((string)c);
                }
            }
        }

        private CampSkill ReadSkill(string id, JToken s, string dir)
        {
            var skill = new CampSkill
            {
                Id = id,
                Cost = Json.Int(s["cost"], 0),
                UseLimit = Math.Max(1, Json.Int(s["use_limit"], 1)),
                // a skill whose own folder has no picture of it (the shared three a DLC lists again) uses the stock one
                Folder = _files.Exists(dir + "/skill_icons/camp_skill_" + id + ".png") ? dir : "raid/camping"
            };
            foreach (var requirement in Json.Array(s["upgrade_requirements"]))
                foreach (var cost in Json.Array(requirement["currency_cost"]))
                    if ((string)cost["type"] == ItemTypes.Gold) skill.PriceGold += Json.Int(cost["amount"], 0);
            foreach (var e in Json.Array(s["effects"]))
            {
                var effect = new CampEffect
                {
                    Selection = (string)e["selection"] ?? CampSelections.Self,
                    Type = (string)e["type"] ?? "",
                    SubType = (string)e["sub_type"] ?? "",
                    Code = (string)e["chance"]?["code"] ?? "",
                    Chance = Json.Number(e["chance"]?["amount"], 1),
                    Amount = Json.Number(e["amount"], 0),
                    Requirements = Json.Array(e["requirements"]).Select(r => (string)r).Where(r => !string.IsNullOrEmpty(r)).ToArray()
                };
                if (effect.Type == CampEffectTypes.Buff) _buffs.TryGetValue(effect.SubType, out effect.Buff);
                skill.Effects.Add(effect);
            }
            return skill;
        }

        // ---- quests ----------------------------------------------------------------------------------

        /// <summary>
        /// Firewood DD1 hands a party for a quest of this length (1 short, 2 medium, 3 long): one log is one
        /// camp. 0 when the install cannot be read: no camping, rather than camps DD1 would not give.
        /// </summary>
        public int Firewood(int length)
        {
            return length >= 0 && length < _firewood.Count ? _firewood[length] : 0;
        }

        // ---- meals -----------------------------------------------------------------------------------

        public MealOption Meal(string type) => Meals.FirstOrDefault(m => m.Type == type);

        /// <summary>Food a meal takes for a party of this size. DD1 behaviour, not in data: a half ration left over is rounded up.</summary>
        public int FoodFor(MealOption meal, int heroes)
        {
            return meal == null ? 0 : (int)Math.Ceiling(meal.RationsPer * Math.Max(0, heroes) - 1e-9);
        }

        // ---- skills ----------------------------------------------------------------------------------

        public CampSkill Skill(string id) => id != null && _byId.TryGetValue(id, out var skill) ? skill : null;

        public CampBuffDef Buff(string id) => id != null && _buffs.TryGetValue(id, out var buff) ? buff : null;

        /// <summary>A skill most classes share (DD1 shows these in a grid of their own at the Survivalist).</summary>
        public bool IsShared(CampSkill skill) => skill != null && skill.Classes.Count >= SharedThreshold;

        /// <summary>The class's own skills in DD1's order, by the class's analogue where DD1 has none for it.</summary>
        public List<CampSkill> ClassSkills(string classId)
        {
            var own = _skills.Where(s => !IsShared(s) && s.Classes.Contains(classId ?? "")).ToList();
            if (own.Count == 0 && classId != null && Analogues.TryGetValue(classId, out var analogue))
                own = _skills.Where(s => !IsShared(s) && s.Classes.Contains(analogue)).ToList();
            return own;
        }

        /// <summary>True when the class's own skills are borrowed from its analogue.</summary>
        public bool UsesAnalogue(string classId)
        {
            return classId != null && Analogues.ContainsKey(classId) && !_skills.Any(s => !IsShared(s) && s.Classes.Contains(classId));
        }

        /// <summary>
        /// The shared skills. Every class may learn them, listed or not: DD1's flagellant file names only
        /// his own four, yet his hero file still has him arrive with one shared skill
        /// (number_of_shared_camping_skills 1), which he could not if the shared three were closed to him.
        /// </summary>
        public List<CampSkill> SharedSkills() => _skills.Where(IsShared).ToList();

        /// <summary>Everything a hero of the class can learn: the class's own first, then the shared ones.</summary>
        public List<CampSkill> SkillsFor(string classId)
        {
            var skills = ClassSkills(classId);
            skills.AddRange(SharedSkills());
            return skills;
        }

        /// <summary>
        /// The limit as DD1's own tutorial puts it ("Heroes can only have 4 Camping skills active at a time",
        /// tutorial_popup_map_camping_skills_description, English). Leaves the limit alone when the text
        /// names no number.
        /// </summary>
        public bool ReadActiveLimit(string tutorialText)
        {
            if (string.IsNullOrEmpty(tutorialText)) return false;
            var plain = Regex.Replace(tutorialText, "\\{[^}]*\\}", "");
            var match = Regex.Match(plain, "(\\d+)\\s+Camping skills active", RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var limit) || limit <= 0) return false;
            ActiveLimit = limit;
            return true;
        }

        // ---- hero classes ----------------------------------------------------------------------------

        private ClassInfo Class(string classId)
        {
            classId = classId ?? "";
            if (_classes.TryGetValue(classId, out var info)) return info;
            info = new ClassInfo();
            foreach (var root in Roots)
            {
                var text = classId.Length > 0 ? _files.ReadText(root + "heroes/" + classId + "/" + classId + ".info.darkest") : null;
                if (text == null) continue;
                info.Found = true;
                foreach (var block in DarkestFile.Parse(text))
                {
                    if (block.Name == "tag" && block.Text("id") == "religious") info.Religious = true;
                    if (block.Name != "generation") continue;
                    info.ClassSkills = block.Int("number_of_class_specific_camping_skills", 0, info.ClassSkills);
                    info.SharedSkills = block.Int("number_of_shared_camping_skills", 0, info.SharedSkills);
                }
                break;
            }
            _classes[classId] = info;
            return info;
        }

        /// <summary>DD1 tags the class religious (crusader, vestal, leper, flagellant). A class DD1 does not have is not.</summary>
        public bool IsReligious(string classId) => Class(classId).Religious;

        /// <summary>
        /// The camping skills a recruit of the class arrives with: DD1 rolls so many of the class's own and
        /// so many shared ones (the hero file's generation line; 2 and 1 for every stock class).
        /// </summary>
        public List<string> RollStartingSkills(string classId, Rng rng)
        {
            var info = Class(classId);
            if (!info.Found && classId != null && Analogues.TryGetValue(classId, out var analogue)) info = Class(analogue);
            var known = new List<string>();
            Pick(ClassSkills(classId), info.ClassSkills, rng, known);
            Pick(SharedSkills(), info.SharedSkills, rng, known);
            return known;
        }

        private static void Pick(List<CampSkill> from, int count, Rng rng, List<string> into)
        {
            var pool = from.Select(s => s.Id).ToList();
            rng.Shuffle(pool);
            // file order again: the roll decides which skills, not how they are listed
            foreach (var skill in from)
                if (pool.IndexOf(skill.Id) < count) into.Add(skill.Id);
        }
    }
}
