using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Names for recruits who are not the first of their class. DD2 has one canonical hero per class (the
    /// Highwayman is always Dismas); when that hero is gone, the next one of the class is a different person
    /// and takes a name from DD1's own recruit name table, read from the player's DD1 install.
    /// </summary>
    internal static class RosterNames
    {
        private const string Table = "localization/names.string_table.xml";

        // Used only when the DD1 table cannot be read.
        private static readonly string[] Spare =
        {
            "Aldous", "Berengar", "Cuthbert", "Drogo", "Edmund", "Fulk", "Godfrey", "Hamon", "Isolde", "Jocelyn",
            "Lambert", "Maud", "Osbert", "Petronilla", "Ranulf", "Sibyl", "Tancred", "Ursula", "Warin", "Ysolt"
        };

        private static List<string> _names;

        public static IReadOnlyList<string> All => _names ?? (_names = Load());

        /// <summary>A name nobody in <paramref name="taken"/> carries, as long as the table has one left.</summary>
        public static string Pick(RosterDice dice, ICollection<string> taken)
        {
            var names = All;
            var start = dice.Below(names.Count);
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[(start + i) % names.Count];
                if (taken == null || !taken.Contains(name)) return name;
            }
            return names[start];
        }

        private static List<string> Load()
        {
            var names = new List<string>();
            try
            {
                // One file holds every language; the hero names are the entries hero_name_<n> of the english block.
                var text = Dd1Install.ReadText(Table);
                var start = text != null ? text.IndexOf("<language id=\"english\"", StringComparison.Ordinal) : -1;
                if (start >= 0)
                {
                    var end = text.IndexOf("</language>", start, StringComparison.Ordinal);
                    var block = end > start ? text.Substring(start, end - start) : text.Substring(start);
                    var seen = new HashSet<string>();
                    foreach (Match match in Regex.Matches(block, "<entry id=\"hero_name_\\d+\">\\s*<!\\[CDATA\\[(.*?)\\]\\]>"))
                    {
                        var name = match.Groups[1].Value.Trim();
                        if (name.Length > 0 && seen.Add(name)) names.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("recruit names: " + Table + " could not be read: " + e.Message);
            }
            if (names.Count == 0)
            {
                Plugin.Log.LogWarning("recruit names: no DD1 name table, using the short built-in list");
                names.AddRange(Spare);
            }
            return names;
        }
    }
}
