using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dungeon;
using DD2Estate.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The districts' words. Names are DD1's, read from the English section of the player's own string tables:
    /// the screen's, the confirm question's and the locked tooltip's from the main table
    /// (localization/miscellaneous.string_table.xml), the buildings' from the Crimson Court's
    /// (dlc/580100_crimson_court/localization/CC.string_table.xml: str_&lt;building&gt;_title,
    /// str_&lt;building&gt;_buff_&lt;name&gt;). What a building gives is said as DD1 says it wherever the estate
    /// gives the same thing; where a DD1 number or a hero class does not carry over the line says what
    /// happens here, in the mod's words, and a bonus that is not given has no line: an entry never promises
    /// what does not happen. Those places are marked.
    /// </summary>
    internal static class DistrictText
    {
        private const string Table = "dlc/580100_crimson_court/localization/CC.string_table.xml";
        // DD2's stress bar (ActorInstance.StressMax of a hero without quirks), for a DD1 amount said in DD2's points
        private const double Dd2StressMax = 10;

        private static Dictionary<string, string> _texts;
        private static double _scouting = double.NaN;

        // ---- DD1's tables --------------------------------------------------------------------------------

        // The main tables first; then the Crimson Court's, of which only the districts' own entries are kept.
        private static string Get(string id)
        {
            var text = WindowText.Get(id);
            if (text != null) return text;
            if (_texts == null) _texts = Read();
            return _texts.TryGetValue(id, out text) ? text : null;
        }

        private static string Plain(string id)
        {
            var text = Get(id);
            return text != null ? Dd1Strings.Format(text) : null;
        }

        private static Dictionary<string, string> Read()
        {
            var texts = new Dictionary<string, string>();
            var path = Dd1Install.PathOf(Table);
            if (path == null || !File.Exists(path)) return texts;
            var prefixes = new List<string> { "str_inventory_title_heirloom" + DistrictRules.Blueprint, "str_inventory_description_heirloom" + DistrictRules.Blueprint };
            foreach (var building in Districts.Rules.Buildings) prefixes.Add("str_" + building.Id + "_");
            try { Dd1Strings.ReadEnglish(File.ReadLines(path), texts, prefixes.ToArray()); }
            catch (Exception e) { Plugin.Log.LogWarning("DD1's Crimson Court string table could not be read: " + e.Message); }
            return texts;
        }

        // ---- names ---------------------------------------------------------------------------------------

        public static string ScreenName => Plain("town_name_district") ?? "Districts";

        public static string LockedName => Plain("str_districts_locked_tooltip") ?? "Districts Locked";

        public static string ConfirmQuestion => Plain("str_district_build_building_confirm") ?? "Construct this building?";

        public static string ConfirmYes => Plain("str_district_build_building_confirm_yes") ?? "Yes";

        public static string ConfirmNo => Plain("str_district_build_building_confirm_no") ?? "No";

        public static string Title(DistrictBuilding building)
        {
            return building == null ? "" : Plain("str_" + building.Id + "_title") ?? TownEventText.Words(building.Id);
        }

        /// <summary>DD1's line about a blueprint ("Meticulous architectural sketches..."); null when the table is not there.</summary>
        public static string BlueprintDescription => Plain("str_inventory_description_heirloom" + DistrictRules.Blueprint);

        /// <summary>"Gold", "Blueprint", "Crests": a currency of a building's price, in the plural for any number but one.</summary>
        public static string Currency(string id, int amount)
        {
            if (id == DistrictRules.Gold) return "Gold";
            if (id == DistrictRules.Blueprint) return (Plain("str_inventory_title_heirloom" + DistrictRules.Blueprint) ?? "Blueprint") + (amount == 1 ? "" : "s");
            return HeirloomExchange.Name(id, amount);
        }

        // ---- what a building gives -----------------------------------------------------------------------

        /// <summary>The names of the hero classes of this install an entry is for; empty for an entry for everyone.</summary>
        private static List<string> ClassNames(DistrictEffect effect)
        {
            var names = new List<string>();
            if (effect.Tags.Count == 0) return names;
            foreach (var classId in RosterLifecycle.AvailableClasses())
                if (Districts.Rules.AppliesTo(effect, classId)) names.Add(HeroNames.ClassName(classId));
            return names;
        }

        /// <summary>For tests: the classes of this install each of a building's class bonuses is for.</summary>
        public static List<string> Classes(DistrictBuilding building)
        {
            var names = new List<string>();
            foreach (var effect in building.Effects)
                foreach (var name in ClassNames(effect))
                    if (!names.Contains(name)) names.Add(name);
            return names;
        }

        // "a", "a or b", "a, b or c"
        private static string Either(IReadOnlyList<string> items)
        {
            if (items.Count <= 1) return items.Count == 0 ? "" : items[0];
            return string.Join(", ", items.Take(items.Count - 1)) + " or " + items[items.Count - 1];
        }

        private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // A DD1 stress amount (of 200) in DD2's points (of 10).
        private static double Stress(double dd1) => dd1 * Dd2StressMax / ActivityRules.Dd1StressMax;

        /// <summary>
        /// What a standing building gives the estate, a line per bonus (TextMeshPro rich text). The classes a
        /// bonus is for come first, in DD1's colour for names, once for the entries that share them.
        /// </summary>
        public static List<string> Lines(DistrictBuilding building)
        {
            var lines = new List<string>();
            if (building == null) return lines;
            string named = null;
            foreach (var effect in building.Effects)
            {
                var own = new List<string>();
                try { Describe(building, effect, own); }
                catch (Exception e) { Plugin.Log.LogWarning("District text for " + building.Id + " failed: " + e.Message); }
                if (own.Count == 0) continue;
                var classes = ClassNames(effect);
                // An entry for classes this install has none of gives nothing here.
                if (effect.Tags.Count > 0 && classes.Count == 0) continue;
                var heading = string.Join(", ", classes);
                // DD1's sentence of the bonfire names its heroes itself
                if (effect.Kind != DistrictEffectKind.CampPoints && heading.Length > 0 && heading != named)
                {
                    lines.Add(Dd1Ui.Tint(heading + ":", "notable"));
                    named = heading;
                }
                lines.AddRange(own);
            }
            return lines;
        }

        private static void Describe(DistrictBuilding building, DistrictEffect effect, List<string> lines)
        {
            var own = Get("str_" + building.Id + "_buff_" + effect.Name);
            switch (effect.Kind)
            {
                case DistrictEffectKind.Prestige:
                    if (own != null) lines.Add(Dd1Strings.Format(own));
                    break;
                case DistrictEffectKind.Interest:
                    lines.Add(Dd1Strings.Format(own ?? "Interest gained on saved gold: %d%% per week.", (int)Math.Round(effect.Amount * 100)));
                    break;
                case DistrictEffectKind.Light:
                    if (own != null) lines.Add(Dd1Strings.Format(own));
                    // THE MOD'S WORDS: of DD1's torchlight table the estate reads the scouting (the fight's share
                    // of the light is DD2's own flame), so that is what the line names.
                    var scouting = ScoutingGain(effect);
                    if (Math.Abs(scouting) > 0.001)
                        lines.Add(Dd1Strings.Format((Dd1Strings.Get("buff_stat_tooltip_scouting_chance") ?? "%+d%% Scouting Chance").Replace("%+d", "%s"), Signed(scouting)) + " by torchlight");
                    break;
                case DistrictEffectKind.Supply:
                    if (effect.ItemType == ItemTypes.Provision) lines.Add(own != null ? Dd1Strings.Format(own) : "Some food is granted for free each week.");
                    break;
                case DistrictEffectKind.IdleRelief:
                {
                    // THE MOD'S NUMBER: DD1's sentence with the amount in DD2's stress points (10 of DD1's 200 is half a point).
                    var format = own ?? "Idle stress relief in town increased by %d per week.";
                    lines.Add(Dd1Strings.Format(format.Replace("%d", "%s"), Number(Stress(effect.Amount))));
                    break;
                }
                case DistrictEffectKind.HeroBuff:
                    foreach (var id in effect.BuffIds)
                    {
                        var line = BuffLine(id, effect.Tags.Count == 0);
                        if (line != null && !lines.Contains(line)) lines.Add(line);
                    }
                    break;
                case DistrictEffectKind.CurioStress:
                {
                    // THE MOD'S NUMBER: DD1 writes its own 15 into the sentence.
                    var dd1 = Number(effect.Amount);
                    var points = Number(Stress(effect.Amount));
                    var text = own != null ? Dd1Strings.Format(own) : null;
                    lines.Add(text != null && text.Contains(dd1) ? text.Replace(dd1, points) : string.Join(", ", effect.CurioTags) + " curios heal " + points + " stress");
                    break;
                }
                case DistrictEffectKind.CampPoints:
                {
                    // THE MOD'S WORDS after DD1's: its sentence names three classes, its hero files tag a fourth.
                    var classes = ClassNames(effect);
                    if (classes.Count > 0) lines.Add(Number(effect.Amount) + " additional Respite Points if you have at least one " + Either(classes) + " in your party.");
                    break;
                }
            }
        }

        private static string Signed(double value) => (value >= 0 ? "+" : "") + Number(value);

        /// <summary>
        /// One hero buff as DD1's tooltips word it ("+10% Stun Resist"); null for a buff that is not given.
        /// REMAPPING said in the line: DD1's accuracy is damage dealt here, so it reads as damage.
        /// </summary>
        public static string BuffLine(string buffId, bool forEveryone)
        {
            var rules = Districts.Rules;
            var buff = rules.Buff(buffId);
            if (buff == null || rules.BuffEffect(buffId, forEveryone).Given.Count == 0) return null;
            var amount = rules.BuffAmount(buffId);
            var stat = buff.StatType + (buff.SubType.Length > 0 ? "_" + buff.SubType : "");
            if (buff.StatType == "combat_stat_add" && buff.SubType == "attack_rating") stat = "combat_stat_multiply_damage_low";
            // DD1 writes speed in points and everything else as a share
            var value = buff.SubType == "speed_rating" ? amount : amount * 100;
            var format = Dd1Strings.Get("buff_stat_tooltip_" + stat);
            return format != null ? Dd1Strings.Format(format, (int)Math.Round(value)) : Signed(Math.Round(value)) + " " + TownEventText.Words(stat);
        }

        // Scouting the building's torchlight table adds in full light over DD1's own (rules.json "darkness").
        private static double ScoutingGain(DistrictEffect effect)
        {
            if (!double.IsNaN(_scouting)) return _scouting;
            _scouting = 0;
            try
            {
                var files = new Dd1Files();
                var stock = RaidRules.Load(files);
                var built = RaidRules.Load(files);
                if (built.UseDarkness(effect.Darkness)) _scouting = built.BandFor(RaidRules.MaxLight).Value("player_scouting_increase") - stock.BandFor(RaidRules.MaxLight).Value("player_scouting_increase");
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1's torchlight tables could not be compared: " + e.Message); }
            return _scouting;
        }
    }
}
