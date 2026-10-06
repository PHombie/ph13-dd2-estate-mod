using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>DD1's camping data behind the camp and the Survivalist, read once from the player's install.</summary>
    internal static class CampContent
    {
        private static CampingRules _rules;

        public static CampingRules Rules
        {
            get
            {
                if (_rules != null) return _rules;
                _rules = CampingRules.Load(new Dd1Files());
                // rules.json and the game's own tutorial disagree on how many skills a hero keeps ready; the
                // tutorial is what DD1 plays by (see CampingRules.ActiveLimit).
                _rules.ReadActiveLimit(CampText.Dd1String("tutorial_popup_map_camping_skills_description"));
                Plugin.Log.LogInfo("Camping rules: " + _rules.Skills.Count + " skills from DD1, " + _rules.StartPoints + " respite points, ambush "
                                   + Mathf.RoundToInt((float)(_rules.AmbushChance * 100)) + "%, " + _rules.ActiveLimit + " skills ready at a time");
                return _rules;
            }
        }

        public static Sprite Icon(CampSkill skill)
        {
            return skill != null && Dd1Install.Exists(skill.IconPath) ? Dd1Install.Sprite(skill.IconPath) : null;
        }

        /// <summary>DD1 stress points (of 200) on a hero's own DD2 scale; usually not a whole number.</summary>
        public static float ToDd2Stress(double dd1Stress, float stressMax)
        {
            return (float)(dd1Stress * stressMax / ActivityRules.Dd1StressMax);
        }

        /// <summary>
        /// DD2 stress moves in whole points and DD1's amounts do not scale to whole points (15 of 200 is 0.75
        /// of 10). The fraction is the chance of one more point, so the average stays DD1's; the Tavern rolls
        /// its relief the same way (ActivityRules.RollRelief).
        /// </summary>
        public static int RollStress(double dd1Stress, float stressMax, Rng rng)
        {
            var value = ToDd2Stress(dd1Stress, stressMax);
            var whole = Mathf.FloorToInt(value);
            return whole + (rng.NextDouble() < value - whole ? 1 : 0);
        }
    }

    /// <summary>
    /// Words for the camp, DD1's own, from the English section of the player's DD1 string tables (the game's
    /// and its DLC's), so the mod carries no DD1 text: a skill's name (camping_skill_name_&lt;id&gt;), its cost
    /// and uses (camping_skill_cost, camping_skill_uses_remaining_format), whom an effect lands on
    /// (camping_skill_selection_*) and what it does (camping_skill_effect_*, and for a buff DD1's words for
    /// the stat, buff_stat_tooltip_*, under its rule, buff_rule_tooltip_*). The numbers are the Estate's: a
    /// skill is worded as it works here (DD1's stress points on DD2's scale, a buff as the DD2 stats it
    /// becomes), so a line can differ from DD1's in its number and, for accuracy, dodge and protection, in
    /// its stat.
    /// </summary>
    internal static class CampText
    {
        // The tables that name camping skills: the game's and the DLC that add any.
        private static readonly string[] Tables =
        {
            "localization/heroes.string_table.xml",
            "localization/miscellaneous.string_table.xml",
            "dlc/580100_crimson_court/localization/CC.string_table.xml",
            "dlc/702540_shieldbreaker/localization/shieldbreaker.string_table.xml",
            "dlc/4964110_fires_edge/features/duelist/localization/duelist.string_table.xml",
            "dlc/4964110_fires_edge/features/runaway/localization/runaway.string_table.xml"
        };

        private static readonly string[] Wanted =
        {
            "\"camping_skill_", "tutorial_popup_map_camping_skills_description", "str_meal_title_", "str_ui_meal_title", "camping_respite_",
            "\"buff_stat_tooltip_", "\"buff_rule_tooltip_"
        };
        private static readonly Regex Entry = new Regex("<entry id=\"([^\"]+)\"><!\\[CDATA\\[(.*?)\\]\\]></entry>");
        private static readonly string[] MealOrder = { "none", "half", "full", "feast" };
        private static Dictionary<string, string> _strings;

        /// <summary>An English string of DD1 by id; null when the install has none.</summary>
        public static string Dd1String(string id)
        {
            if (_strings == null) _strings = Read();
            return _strings.TryGetValue(id, out var text) ? text : null;
        }

        /// <summary>"Zealous Vigil": DD1 writes skill names in capitals.</summary>
        public static string Name(CampSkill skill)
        {
            if (skill == null) return "";
            var name = Dd1String("camping_skill_name_" + skill.Id);
            return Title(string.IsNullOrEmpty(name) ? skill.Id.Replace('_', ' ') : name);
        }

        /// <summary>DD1's name of a meal ("None", "Half", "Full", "Feast").</summary>
        public static string MealName(MealOption meal)
        {
            var index = Array.IndexOf(MealOrder, meal.Type);
            var name = index >= 0 ? Dd1String("str_meal_title_" + index) : null;
            return string.IsNullOrEmpty(name) ? Title(meal.Type) : name;
        }

        /// <summary>"+10% health", "-20% health, +0.75 stress", "no effect".</summary>
        public static string MealEffect(MealOption meal)
        {
            var parts = new List<string>();
            if (meal.Healing != 0) parts.Add(Signed(meal.Healing * 100, "0") + "% health");
            if (meal.Stress != 0) parts.Add(Signed(CampContent.ToDd2Stress(meal.Stress, ActivityRules.Dd2StressMax), "0.##") + " stress");
            return parts.Count > 0 ? string.Join(", ", parts) : "no effect";
        }

        // ---- what a skill does -------------------------------------------------------------------------

        /// <summary>DD1's line for what a use costs: "Time Cost: 3", in its colour (attacktype).</summary>
        public static string Cost(CampSkill skill)
        {
            return skill == null ? "" : Words("camping_skill_cost", "{colour_start|attacktype}Time Cost: %d {colour_end}", skill.Cost).TrimEnd();
        }

        /// <summary>DD1's line for the uses a hero has left of a skill at this camp: "Uses Remaining: 1".</summary>
        public static string UsesLeft(int left) => Words("camping_skill_uses_remaining_format", "Uses Remaining: %d", Math.Max(0, left));

        // Stat buffs that come unconditionally and under the same DD1 rule, summed: DD1 writes "+20% DMG" as
        // two buffs (the low and the high end) and often an accuracy buff beside them, which the Estate turns
        // into damage too.
        private class StatGroup
        {
            public string Rule;
            public CampBuffDef Buff;
            public int Slot;
            public readonly List<Dd2StatLine> Lines = new List<Dd2StatLine>();
        }

        /// <summary>
        /// The skill's effects in DD1's words, one line per group of heroes it lands on ("Self Only: -1.25
        /// Stress, Prevents nighttime ambush"). TextMeshPro rich text: DD1 colours some of its words.
        /// </summary>
        public static List<string> Lines(CampSkill skill)
        {
            var lines = new List<string>();
            if (skill == null) return lines;
            foreach (var selection in new[] { CampSelections.Self, CampSelections.Individual, CampSelections.PartyOther, CampSelections.Party })
            {
                var parts = new List<string>();
                var groups = new List<StatGroup>();
                foreach (var effect in skill.Effects)
                {
                    if (effect.Selection != selection) continue;
                    var summed = effect.Type == CampEffectTypes.Buff && effect.Chance >= 0.999 && effect.Requirements.Length == 0 && CampingBuffMap.KindOf(effect.Buff) == CampBuffKind.Stat;
                    if (!summed)
                    {
                        parts.Add(Effect(effect));
                        continue;
                    }
                    var rule = RuleKey(effect.Buff);
                    var group = groups.Find(g => g.Rule == rule);
                    if (group == null)
                    {
                        groups.Add(group = new StatGroup { Rule = rule, Buff = effect.Buff, Slot = parts.Count });
                        parts.Add(null);
                    }
                    group.Lines.AddRange(CampingBuffMap.Lines(effect.Buff, effect.Amount, ShownRank(effect.Buff)));
                }
                foreach (var group in groups) parts[group.Slot] = Stats(group.Lines, group.Buff);
                parts.RemoveAll(string.IsNullOrEmpty);
                if (parts.Count > 0) lines.Add(Who(selection) + " " + string.Join(", ", parts));
            }
            return lines;
        }

        public static string Describe(CampSkill skill) => string.Join("\n", Lines(skill));

        // camping_skill_selection_*: "Self Only:", "One Companion:", "All Companions:", "Party:"
        private static string Who(string selection)
        {
            switch (selection)
            {
                case CampSelections.Self: return Words("camping_skill_selection_self", "Self Only:");
                case CampSelections.Individual: return Words("camping_skill_selection_individual", "One Companion:");
                case CampSelections.PartyOther: return Words("camping_skill_selection_party_other", "All Companions:");
                default: return Words("camping_skill_selection_party", "Party:");
            }
        }

        /// <summary>
        /// One effect as it works in the Estate, in DD1's words, inside DD1's frames for a chance
        /// ("(75% chance) ...") and for a condition ("If religious: ...").
        /// </summary>
        public static string Effect(CampEffect effect)
        {
            var text = What(effect);
            if (string.IsNullOrEmpty(text)) return null;
            if (effect.Chance < 0.999) text = Words("camping_skill_chance_effect_format", "(%d%% chance) %s", Math.Round(effect.Chance * 100), text);
            foreach (var requirement in effect.Requirements) text = Words("camping_skill_requirement_effect_format", "If %s: %s", If(requirement), text);
            return text;
        }

        // camping_skill_requirement_*: "religious", "not religious", "afflicted", "Mortality debuffs". What the
        // last two are in the Estate (stress past half the scale, a wound) is docs/recon/camping.md's.
        private static string If(string requirement)
        {
            return Words("camping_skill_requirement_" + requirement, requirement.Replace('_', ' '));
        }

        private static string What(CampEffect effect)
        {
            switch (effect.Type)
            {
                case CampEffectTypes.StressHeal: return Words("camping_skill_effect_stress_heal_amount", "-%d Stress", Stress(effect.Amount));
                case CampEffectTypes.StressDamage: return Words("camping_skill_effect_stress_damage_amount", "%+d Stress", Stress(effect.Amount));
                case CampEffectTypes.HealthHeal: return Words("camping_skill_effect_health_heal_max_health_percent", "Heal %d%% HP", Number(effect.Amount * 100));
                case CampEffectTypes.HealthDamage: return Words("camping_skill_effect_health_damage_max_health_percent", "Suffer %d%% HP DMG", Number(effect.Amount * 100));
                case CampEffectTypes.RemoveBleed: return Words("camping_skill_effect_remove_bleeding", "Remove Bleeding");
                case CampEffectTypes.RemoveBlight: return Words("camping_skill_effect_remove_poison", "Remove Blight");
                case CampEffectTypes.RemoveDisease: return Words("camping_skill_effect_remove_disease", "Remove Disease");
                case CampEffectTypes.RemoveMortality: return Words("camping_skill_effect_remove_deaths_door_recovery_buffs", "Remove Mortality debuffs");
                case CampEffectTypes.ReduceAmbush:
                    // DD1's every skill of this kind takes the whole chance away, and its one sentence says so;
                    // for a part of it (a mod's skill) there is no DD1 sentence
                    return effect.Amount >= CampContent.Rules.AmbushChance ? Words("camping_skill_effect_reduce_ambush_chance", "Prevents nighttime ambush") : "Nighttime ambush " + Signed(-effect.Amount * 100, "0") + "%";
                case CampEffectTypes.ReduceTorch: return Words("camping_skill_effect_reduce_torch", "Reduce torchlight by %d", Number(effect.Amount));
                case CampEffectTypes.RefreshUses: return Words("camping_skill_effect_refresh_camp_skill_uses", "Refresh Camping Skill Uses");
                case CampEffectTypes.Loot: return Loot(effect);
                case CampEffectTypes.Buff: return Buff(effect);
                default: return effect.Type.Replace('_', ' ');
            }
        }

        // camping_skill_effect_loot_<table>: "Produces a random supply item", "Produce a Skeleton Key",
        // "Produces a random trinket". The last gets a note of the mod's: a camp's loot goes into the bag, and a
        // trinket is not a thing of the bag.
        private static string Loot(CampEffect effect)
        {
            var trinket = effect.SubType.StartsWith("T_", StringComparison.Ordinal);
            var text = Words("camping_skill_effect_loot_" + effect.SubType, trinket ? "Produces a random trinket" : effect.SubType == "KEYONLY" ? "Produce a Skeleton Key" : "Produces a random supply item");
            return trinket ? text + " (trinkets are not carried yet)" : text;
        }

        // A buff as it works in the Estate: the DD2 stats it becomes (CampingBuffMap), each in DD1's words for
        // that stat.
        private static string Buff(CampEffect effect)
        {
            var buff = effect.Buff;
            switch (CampingBuffMap.KindOf(buff))
            {
                case CampBuffKind.Scouting: return Words("buff_stat_tooltip_scouting_chance", "%+d%% Scouting Chance", Number(effect.Amount * 100));
                case CampBuffKind.PartySurprise: return Words("buff_stat_tooltip_party_surprise_chance", "%+d%% Chance Party Surprised", Number(effect.Amount * 100));
                case CampBuffKind.MonstersSurprise: return Words("buff_stat_tooltip_monsters_surprise_chance", "%+d%% Chance Monsters Surprised", Number(effect.Amount * 100));
                case CampBuffKind.Effect: return Dd2Effect(CampingBuffMap.EffectsOf(buff));
                case CampBuffKind.None: return buff != null && buff.HasHitEffect ? "(an on-hit effect: no DD2 counterpart)" : "(no DD2 counterpart)";
            }
            return Stats(CampingBuffMap.Lines(buff, effect.Amount, ShownRank(buff)), buff);
        }

        // The rank rule is shown as it will be asked when a fight starts: whole, in or out of the front rank.
        private static int ShownRank(CampBuffDef buff) => buff.RuleType == "in_rank" ? (buff.RuleFalse ? 1 : 0) : -1;

        // Buffs under the same rule are worded together.
        private static string RuleKey(CampBuffDef buff)
        {
            return !buff.Conditional ? "" : buff.RuleType + (buff.RuleFalse ? "!" : "") + buff.RuleNumber.ToString("0.##", CultureInfo.InvariantCulture) + buff.RuleText;
        }

        // DD1's words for the rule a buff holds under (buff_rule_tooltip_in_rank: "%s if in position %d"; DD1
        // counts its ranks from 0 and names them from 1). The hero's place is the one rule the Estate asks,
        // when a fight starts. A buff under any other rule (melee or ranged skills only, large monsters, the
        // first round, while a riposte is up) is given at half its amount, always, so it is worded as the
        // plain buff of that half.
        private static string Ruled(CampBuffDef buff, string text)
        {
            if (buff == null || buff.RuleType != "in_rank") return text;
            return buff.RuleFalse
                ? Words("buff_rule_tooltip_in_rank_false", "%s if not in position %d", text, (int)Math.Round(buff.RuleNumber) + 1)
                : Words("buff_rule_tooltip_in_rank", "%s if in position %d", text, (int)Math.Round(buff.RuleNumber) + 1);
        }

        private static string Dd2Effect(string effectsId)
        {
            switch (effectsId)
            {
                case "runaway_firestarter_burn_buff": return "blows set the target alight";
                default: return "DD2 effect " + effectsId;
            }
        }

        // Lines of one stat added up, in the order they first came, each under the buff's rule.
        private static string Stats(List<Dd2StatLine> lines, CampBuffDef buff)
        {
            var sums = new List<Dd2StatLine>();
            foreach (var line in lines)
            {
                var sum = sums.Find(s => s.Key == line.Key);
                if (sum == null) sums.Add(sum = new Dd2StatLine { Kind = line.Kind, Stat = line.Stat, Sub = line.Sub });
                sum.Value += line.Value;
            }
            var parts = new List<string>();
            foreach (var sum in sums)
                if (Math.Abs(sum.Value) > 1e-9) parts.Add(Ruled(buff, Stat(sum)));
            return string.Join(", ", parts);
        }

        // A DD2 stat in DD1's words for the DD1 stat it stands for (buff_stat_tooltip_<stat>[_<sub stat>]):
        // "+10% DMG", "-10% DMG Taken", "+5% CRT", "+2 SPD", "+15% Bleed Resist", "-15% Stress".
        private static string Stat(Dd2StatLine line)
        {
            var percent = Number(line.Value * 100);
            switch (line.Stat)
            {
                case CampingBuffMap.DamageDealt: return Words("buff_stat_tooltip_combat_stat_multiply_damage_low", "%+d%% DMG", percent);
                case CampingBuffMap.DamageReceived: return Words("buff_stat_tooltip_damage_received_percent", "%+d%% DMG Taken", percent);
                case CampingBuffMap.CritChance: return Words("buff_stat_tooltip_combat_stat_add_crit_chance", "%+d%% CRT", percent);
                case CampingBuffMap.Speed: return Words("buff_stat_tooltip_combat_stat_add_speed_rating", "%+d SPD", Number(line.Value));
                case CampingBuffMap.HealthMax: return Words("buff_stat_tooltip_combat_stat_multiply_max_hp", "%+d%% MAX HP", percent);
                case CampingBuffMap.HealReceived: return Words("buff_stat_tooltip_hp_heal_received_percent", "%+d%% Healing Received", percent);
                case CampingBuffMap.Resistance:
                    // DD2 resists a stress hit whole at that chance; DD1 says the same as so much less stress
                    if (line.Sub == "stress") return Words("buff_stat_tooltip_stress_dmg_received_percent", "%+d%% Stress", Number(-line.Value * 100));
                    var dd1 = line.Sub == "blight" ? "poison" : line.Sub == "death" ? "death_blow" : line.Sub;
                    return Words("buff_stat_tooltip_resistance_" + dd1, "%+d%% " + Title(line.Sub) + " Resist", percent);
                default: return Signed(line.Value, "0.##") + " " + line.Stat.Replace('_', ' ');
            }
        }

        // A DD1 line with its places filled and its colour marks made TextMeshPro's.
        private static string Words(string id, string fallback, params object[] values)
        {
            return RaidText.Format(Dd1String(id) ?? fallback, values);
        }

        // A number as it goes into a DD1 line: whole where it is, else with its decimals ("12.5").
        private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // DD1 stress on DD2's scale of ten, as it is on average ("0.75")
        private static string Stress(double dd1Stress)
        {
            return CampContent.ToDd2Stress(dd1Stress, ActivityRules.Dd2StressMax).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Signed(double value, string format)
        {
            return (value >= 0 ? "+" : "-") + Math.Abs(value).ToString(format, CultureInfo.InvariantCulture);
        }

        private static string Title(string text)
        {
            var words = (text ?? "").ToLowerInvariant().Split(' ');
            for (var i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        // ---- DD1 strings -------------------------------------------------------------------------------

        // Each file holds every language; English comes first, so reading stops at the end of its section.
        private static Dictionary<string, string> Read()
        {
            var strings = new Dictionary<string, string>();
            foreach (var table in Tables)
            {
                var path = Dd1Install.PathOf(table);
                if (path == null || !File.Exists(path)) continue;
                try
                {
                    var english = false;
                    foreach (var line in File.ReadLines(path))
                    {
                        if (line.IndexOf("<language ", StringComparison.Ordinal) >= 0)
                        {
                            if (english) break;
                            english = line.IndexOf("id=\"english\"", StringComparison.Ordinal) >= 0;
                            continue;
                        }
                        if (!english) continue;
                        if (line.IndexOf("</language>", StringComparison.Ordinal) >= 0) break;
                        var wanted = false;
                        foreach (var prefix in Wanted)
                            if (line.IndexOf(prefix, StringComparison.Ordinal) >= 0) wanted = true;
                        if (!wanted) continue;
                        var match = Entry.Match(line);
                        if (match.Success && !strings.ContainsKey(match.Groups[1].Value)) strings[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 string table " + table + " could not be read: " + e.Message); }
            }
            return strings;
        }
    }
}
