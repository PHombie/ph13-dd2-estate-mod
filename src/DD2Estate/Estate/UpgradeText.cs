using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What the upgrade screen says. Tree names come from the English section of the player's DD1 string table
    /// (upgrade_tree_name_&lt;tree&gt;); what a step does is said with DD1's own sentence where its table has one
    /// for that kind of step (upgrade_tree_tooltip_description_*_format), and otherwise in the mod's own words,
    /// built from the values DD1's files hold for the level before and after (<see cref="UpgradeRules.Track"/>).
    /// Trees whose effect lives in the mod rather than in a DD1 building file (the guild's and the blacksmith's)
    /// register a describer. tools/preview_upgrades.py words the lines the same way: change one, change the other.
    /// </summary>
    internal static class UpgradeText
    {
        private const string Table = "localization/miscellaneous.string_table.xml";
        private const string Prefix = "upgrade_tree_name_";
        private static readonly Regex Entry = new Regex("<entry id=\"" + Prefix + "([^\"]+)\"><!\\[CDATA\\[(.*?)\\]\\]></entry>");
        private static readonly string[] Numerals = { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        private static Dictionary<string, string> _names;

        /// <summary>What a list of DD1's building files is called on the screen; others show their own name.</summary>
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "cost_upgrades", "Price" },
            { "slot_upgrades", "Places" },
            { "stress_upgrades", "Stress relief" },
            { "number_of_recruits_upgrades", "Recruits each week" },
            { "roster_size_upgrades", "Barracks" },
            { "number_of_trinkets_upgrades", "Trinkets on offer" },
            { "positive_quirk_cost_upgrades", "Lock in a good quirk" },
            { "negative_quirk_cost_upgrades", "Remove a bad quirk" },
            { "permanent_negative_quirk_cost_upgrades", "Remove a locked-in quirk" },
            { "disease_quirk_cost_upgrades", "Cure a disease" },
            { "disease_quirk_cure_all_chance_upgrades", "Chance to cure all diseases at once" }
        };

        /// <summary>
        /// Lists of DD1's building files whose change DD1's tooltip does not mention: its string table has a
        /// sentence for the cost of treating a positive, a negative and a disease quirk, none for a locked-in one
        /// (the Treatment Library lowers all three prices with one step).
        /// </summary>
        private static readonly HashSet<string> Unsaid = new HashSet<string> { "permanent_negative_quirk_cost_upgrades" };

        /// <summary>
        /// Lines for trees the mod gives a meaning of its own, by tree id: (tree, level counted from 1) to
        /// text, null for nothing. Added at start-up by the system that owns the effect.
        /// </summary>
        public static readonly Dictionary<string, Func<UpgradeRules.Tree, int, string>> Describers = new Dictionary<string, Func<UpgradeRules.Tree, int, string>>();

        /// <summary>
        /// Trees that can be built but change nothing yet; the screen says so. Empty now that every building
        /// has its own screen and rules. Add an id while a new tree's effect is still to be wired.
        /// </summary>
        public static readonly HashSet<string> Pending = new HashSet<string>();

        public const string PendingNote = "Not in effect yet in this version.";

        /// <summary>Names of the mod's own trees: DD1's string table has none for them.</summary>
        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>();

        /// <summary>DD1 art that stands for a tree without an icon file of its own, by tree id.</summary>
        public static readonly Dictionary<string, string> Icons = new Dictionary<string, string>();

        /// <summary>The DD1 file of a tree's icon.</summary>
        public static string Icon(UpgradeRules.Tree tree)
        {
            return Icons.TryGetValue(tree.Id, out var art) ? art : UpgradeUi.BuildingsDir + tree.Building + "/" + tree.Id + ".icon.png";
        }

        // ---- names -------------------------------------------------------------------------------------

        public static string TreeName(string treeId)
        {
            if (Names.TryGetValue(treeId, out var own)) return own;
            if (_names == null) _names = Read();
            if (_names.TryGetValue(treeId, out var name)) return name;
            var dot = treeId.IndexOf('.');
            return Title(dot >= 0 ? treeId.Substring(dot + 1) : treeId);
        }

        public static string Roman(int number)
        {
            return number >= 0 && number < Numerals.Length ? Numerals[number] : number.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"crests", "portraits", "gold".</summary>
        public static string Plural(string currency)
        {
            return currency == UpgradeRules.Gold ? "gold" : currency + "s";
        }

        /// <summary>"Instructor Mastery II".</summary>
        public static string NeedName(UpgradeRules.Need need)
        {
            var tree = UpgradeRules.Find(need.Tree);
            var index = tree != null ? tree.IndexOf(need.Code) : -1;
            return TreeName(need.Tree) + " " + (index >= 0 ? Roman(index + 1) : need.Code);
        }

        public static string Needs(IReadOnlyList<UpgradeRules.Need> needs)
        {
            var names = new List<string>();
            foreach (var need in needs) names.Add(NeedName(need));
            return Join(names);
        }

        /// <summary>
        /// What a step still waits for, the way DD1's tooltip lists it under "Prerequisites:": one line each,
        /// "Instructor Mastery Level 2" (upgrade_prerequisite_requirement_tooltip_body_format "%s Level %d").
        /// The step before it in its own tree comes first while that is not built (DD1's files name it among
        /// a step's prerequisites), then the steps of other trees. <paramref name="index"/> counts from 0.
        /// </summary>
        public static List<string> Prerequisites(UpgradeRules.Tree tree, int index)
        {
            var lines = new List<string>();
            if (tree == null || index < 0 || index >= tree.Steps.Count) return lines;
            if (index > UpgradeRules.Level(tree)) lines.Add(Prerequisite(tree.Id, index));
            foreach (var need in UpgradeRules.Missing(tree.Steps[index]))
            {
                var other = UpgradeRules.Find(need.Tree);
                var at = other != null ? other.IndexOf(need.Code) : -1;
                lines.Add(at >= 0 ? Prerequisite(need.Tree, at + 1) : TreeName(need.Tree) + " " + need.Code);
            }
            return lines;
        }

        private static string Prerequisite(string treeId, int level)
        {
            return WindowText.Format("upgrade_prerequisite_requirement_tooltip_body_format", "%s Level %d", TreeName(treeId), level);
        }

        /// <summary>"a", "a and b", "a, b and c".</summary>
        public static string Join(IReadOnlyList<string> items)
        {
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0];
            var head = new List<string>(items);
            head.RemoveAt(head.Count - 1);
            return string.Join(", ", head) + " and " + items[items.Count - 1];
        }

        /// <summary>At most two decimals, none when whole.</summary>
        public static string Number(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ---- what a step does --------------------------------------------------------------------------

        /// <summary>The effect of a tree's step, one change per line. <paramref name="level"/> counts from 1.</summary>
        public static List<string> StepLines(UpgradeRules.Tree tree, int level)
        {
            var lines = new List<string>();
            if (level < 1 || level > tree.Steps.Count) return lines;
            var subjects = new HashSet<string>();
            foreach (var track in tree.Tracks) subjects.Add(track.Subject ?? "");
            foreach (var track in tree.Tracks)
            {
                var line = TrackLine(track, level, subjects.Count > 1);
                if (line == null) continue;
                // DD1's own sentence where it has one for this kind of step, as DD1 writes it; the mod's numbers
                // otherwise. A change DD1's tooltip passes over in silence is passed over here too.
                var dd1 = Dd1Line(tree, track, level);
                if (dd1 == null && Unsaid.Contains(track.Kind)) continue;
                lines.Add(dd1 ?? line);
            }
            if (Describers.TryGetValue(tree.Id, out var describe))
            {
                try
                {
                    var line = describe(tree, level);
                    if (!string.IsNullOrEmpty(line)) lines.Add(line);
                }
                catch (Exception e) { Plugin.Log.LogWarning("Upgrade text for " + tree.Id + " failed: " + e.Message); }
            }
            if (Pending.Contains(tree.Id)) lines.Add(PendingNote);
            return lines;
        }

        /// <summary>What DD1 says a whole tree is for (upgrade_tree_tooltip_description_&lt;tree&gt;); null for a tree DD1 does not have.</summary>
        public static string TreeDescription(string treeId)
        {
            return WindowText.Plain("upgrade_tree_tooltip_description_" + treeId);
        }

        // DD1's sentence for a step of this track (upgrade_tree_tooltip_description_*_format), filled with the
        // value the step brings; null where DD1 has none that fits what the step does here.
        private static string Dd1Line(UpgradeRules.Tree tree, UpgradeRules.Track track, int level)
        {
            var after = track.Values[level];
            string key = null;
            var number = (int)Math.Round(after);
            if (track.Additive)
            {
                switch (tree.Id)
                {
                    case "guild.cost": key = "reduces_cost_of_combat_skill_upgrades_format"; break;
                    case "blacksmith.cost": key = "reduces_cost_of_weapon_and_armour_upgrades_format"; break;
                    case "camping_trainer.cost": key = "reduces_cost_of_camping_skills_format"; break;
                    case "nomad_wagon.cost": key = "reduces_cost_of_items_format"; break;
                }
                number = (int)Math.Round(after * 100f);
            }
            else
            {
                switch (track.Kind)
                {
                    case "number_of_recruits_upgrades": key = "increases_number_of_heroes_generated_format"; break;
                    case "roster_size_upgrades": key = "increases_size_of_roster_format"; break;
                    case "number_of_trinkets_upgrades": key = "increases_number_of_items_generated_format"; break;
                    case "slot_upgrades":
                        key = track.Subject == "treatment" ? "increases_number_of_quirk_slots_format"
                            : track.Subject == "disease_treatment" ? "increases_number_of_disease_slots_format" : "increases_number_of_slots_format";
                        break;
                    case "stress_upgrades": key = "increases_stress_recovery"; break;
                    case "affliction_cure_upgrades": key = "increases_chance_of_affliction_cure"; break;
                    // "Reduces treatment cost by %d%%": the share taken off the price the building began with
                    // (DD1's files hold the prices; counted so, every step of the stock game is a round number).
                    case "cost_upgrades":
                        key = "reduces_treatment_cost_format";
                        number = Reduction(track, level);
                        break;
                    case "positive_quirk_cost_upgrades":
                        key = "reduces_positive_quirk_treatment_cost_format";
                        number = Reduction(track, level);
                        break;
                    case "negative_quirk_cost_upgrades":
                        key = "reduces_negative_quirk_treatment_cost_format";
                        number = Reduction(track, level);
                        break;
                    case "disease_quirk_cost_upgrades":
                        key = "reduces_disease_quirk_treatment_cost_format";
                        number = Reduction(track, level);
                        break;
                    case "upgraded_recruits_upgrades":
                        key = "increases_upgraded_recruit_level_format";
                        number = (int?)track.Entries[level]?["level"] ?? level;
                        break;
                    case "disease_quirk_cure_all_chance_upgrades":
                        key = "disease_cure_all_chance_format";
                        number = (int)Math.Round(after * 100f);
                        break;
                }
            }
            if (key == null) return null;
            var format = WindowText.Get("upgrade_tree_tooltip_description_" + key);
            return format != null ? Dd1Strings.Format(format, number) : null;
        }

        // Per cent off a track's first value at a level.
        private static int Reduction(UpgradeRules.Track track, int level)
        {
            var start = track.Values[0];
            return start > 0f ? (int)Math.Round((1f - track.Values[level] / start) * 100f) : 0;
        }

        private static string TrackLine(UpgradeRules.Track track, int level, bool nameSubject)
        {
            float before = track.Values[level - 1], after = track.Values[level];
            if (before == after && ReferenceEquals(track.Entries[level - 1], track.Entries[level])) return null;
            if (track.Additive)
                return "Prices " + Percent(after - before) + " lower (" + Percent(after) + " in all)";

            var label = Labels.TryGetValue(track.Kind, out var known) ? known : Sentence(track.Kind.Replace("_upgrades", ""));
            // One tree serving several activities (the sanitarium's cells): say which one gains.
            if (nameSubject && track.Subject != null) label += " (" + ActivityText.Activity(track.Subject) + ")";

            if (track.Kind == "stress_upgrades")
                return label + ": " + Relief(track.Entries[level]) + ", was " + Relief(track.Entries[level - 1]);
            if (track.Kind == "upgraded_recruits_upgrades")
            {
                var entry = track.Entries[level];
                var rank = (int?)entry?["level"] ?? level;
                return "Seasoned recruits: " + Number(after * 100f) + "% arrive as veterans of rank " + rank + ", trained and equipped";
            }
            if (track.Kind.EndsWith("chance_upgrades", StringComparison.Ordinal))
                return label + ": " + Percent(after) + ", was " + Percent(before);
            if (track.Kind.Contains("cost"))
                return label + ": " + UpgradeUi.Amount(Math.Max(1, ActivityRules.WholeGold(after))) + " gold, was " + UpgradeUi.Amount(Math.Max(1, ActivityRules.WholeGold(before)));
            return label + ": " + Number(after) + ", was " + Number(before);
        }

        // In DD2 stress points, with decimals: the steps between DD1's tiers are smaller than a point.
        private static string Relief(JObject entry)
        {
            var low = ((float?)entry?["heal_low"] ?? 0f) * ActivityRules.Dd2StressMax / ActivityRules.Dd1StressMax;
            var high = ((float?)entry?["heal_high"] ?? low) * ActivityRules.Dd2StressMax / ActivityRules.Dd1StressMax;
            return Math.Abs(high - low) < 0.005f ? Number(low) : Number(low) + " to " + Number(high);
        }

        private static string Percent(float share)
        {
            return ((int)Math.Round(share * 100f)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // ---- DD1 strings -------------------------------------------------------------------------------

        // The file holds every language (6 MB); English comes first, so reading stops at the end of its section.
        private static Dictionary<string, string> Read()
        {
            var names = new Dictionary<string, string>();
            var path = Dd1Install.PathOf(Table);
            if (path == null || !File.Exists(path)) return names;
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
                    if (line.IndexOf(Prefix, StringComparison.Ordinal) < 0) continue;
                    var match = Entry.Match(line);
                    if (match.Success && !names.ContainsKey(match.Groups[1].Value)) names[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 string table could not be read: " + e.Message); }
            return names;
        }

        private static string Title(string id)
        {
            var words = (id ?? "").Split('_');
            for (var i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        private static string Sentence(string id)
        {
            var text = (id ?? "").Replace('_', ' ').Trim();
            return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
        }
    }
}
