using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Display names of buildings and activities, taken from the English section of the player's DD1 string
    /// table (town_name_&lt;id&gt;, town_activity_name_&lt;id&gt;) so that the mod carries no DD1 text.
    /// </summary>
    internal static class ActivityText
    {
        private const string Table = "localization/miscellaneous.string_table.xml";
        private static readonly Regex Entry = new Regex("<entry id=\"(town_name_[^\"]+|town_activity_name_[^\"]+)\"><!\\[CDATA\\[(.*?)\\]\\]></entry>");
        private static Dictionary<string, string> _names;

        public static string Building(string id)
        {
            return Dd1("town_name_" + id) ?? HamletScene.DisplayName(id);
        }

        public static string Activity(string id)
        {
            return Dd1("town_activity_name_" + id) ?? Title(id);
        }

        private static string Dd1(string key)
        {
            if (_names == null) _names = Read();
            return _names.TryGetValue(key, out var text) ? text : null;
        }

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
                    if (line.IndexOf("\"town_", StringComparison.Ordinal) < 0) continue;
                    var match = Entry.Match(line);
                    // Repeated ids are alternative lines in DD1; the first is as good as any for a name.
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
    }
}
