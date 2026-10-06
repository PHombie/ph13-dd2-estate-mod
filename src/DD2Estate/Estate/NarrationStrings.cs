using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD2Estate.Dd1;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The English text of the player's DD1 install (localization/*.string_table.xml), for the systems that speak
    /// with DD1's words: town event notices, the Ancestor's lines, the memoirs' prologue. The mod carries none of
    /// these texts. An entry may run over several lines of the file (the Ancestor's longer lines do), which the
    /// one-line readers of the upgrade and activity names do not need to handle.
    /// </summary>
    internal static class Dd1Strings
    {
        private const string Folder = "localization";
        private const string MainTable = "miscellaneous.string_table.xml";
        // The main table is kept whole. The other tables are platform and DLC leftovers, but two of the base
        // game's town event texts live in one of them (the stage coach event's, in PSN.string_table.xml), so
        // they are searched for these prefixes only.
        // (And one line of the heroes' own words, in dialogue.string_table.xml: what a kleptomaniac says over
        // the loot they keep.)
        private static readonly string[] ElsewherePrefixes = { "town_event_", "str_vo_", "str_keep_loot_string" };

        private const string EntryStart = "<entry id=\"";
        private const string TextStart = "<![CDATA[";
        private const string TextEnd = "]]>";

        private static Dictionary<string, string> _texts;

        /// <summary>
        /// The table is read again at the next ask. Read while no Darkest Dungeon was found it is an empty
        /// table, kept like a full one: a folder taken into use afterwards (Dd1Install.Use) has it forgotten.
        /// </summary>
        public static void Forget() => _texts = null;

        /// <summary>The text of a DD1 string id; null when DD1 has none (or DD1 is not installed).</summary>
        public static string Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_texts == null) _texts = Read();
            return _texts.TryGetValue(id, out var text) ? text : null;
        }

        public static bool Has(string id) => Get(id) != null;

        /// <summary>
        /// A line DD1 keeps in parts ("str_vo_load_crypts_01_0", "_1", ...: the subtitles of one voice clip) as
        /// one text; null when there is no part 0.
        /// </summary>
        public static string Parts(string idWithoutPart)
        {
            var text = new StringBuilder();
            for (var part = 0; part < 64; part++)
            {
                var piece = Get(idWithoutPart + "_" + part.ToString(CultureInfo.InvariantCulture));
                if (piece == null) break;
                piece = Plain(piece);
                if (piece.Length == 0) continue;
                // A part that ends in a hyphen breaks a word ("life-" / "laden shadows").
                if (text.Length > 0 && text[text.Length - 1] != '-') text.Append(' ');
                text.Append(piece);
            }
            return text.Length > 0 ? text.ToString() : null;
        }

        /// <summary>DD1 breaks its subtitles by hand; the mod's labels wrap by themselves.</summary>
        public static string Plain(string text)
        {
            if (text == null) return "";
            var plain = new StringBuilder(text.Length);
            var space = false;
            foreach (var c in text)
            {
                if (c == '\r' || c == '\n' || c == '\t' || c == ' ')
                {
                    space = plain.Length > 0;
                    continue;
                }
                if (space) plain.Append(' ');
                space = false;
                plain.Append(c);
            }
            return plain.ToString();
        }

        /// <summary>
        /// Fills one of DD1's printf-style texts: %s, %d, %+d and %% are understood, the "{?name}" notes in
        /// front of a placeholder and the "{colour_start|x}" / "{colour_end}" marks are dropped.
        /// </summary>
        public static string Format(string format, params object[] args)
        {
            if (format == null) return null;
            var text = new StringBuilder();
            var next = 0;
            for (var i = 0; i < format.Length; i++)
            {
                var c = format[i];
                if (c == '{')
                {
                    var close = format.IndexOf('}', i);
                    if (close > i)
                    {
                        i = close;
                        continue;
                    }
                }
                if (c != '%' || i + 1 >= format.Length)
                {
                    text.Append(c);
                    continue;
                }
                var plus = format[i + 1] == '+';
                var kind = format[plus && i + 2 < format.Length ? i + 2 : i + 1];
                if (kind == '%')
                {
                    text.Append('%');
                    i++;
                    continue;
                }
                if (kind != 's' && kind != 'd')
                {
                    text.Append(c);
                    continue;
                }
                var arg = next < args.Length ? args[next] : null;
                next++;
                if (kind == 'd')
                {
                    var number = Convert.ToInt32(arg ?? 0, CultureInfo.InvariantCulture);
                    text.Append(plus && number >= 0 ? "+" : "").Append(number.ToString(CultureInfo.InvariantCulture));
                }
                else text.Append(arg);
                i += plus ? 2 : 1;
            }
            return Plain(text.ToString());
        }

        // ---- reading -------------------------------------------------------------------------------------

        private static Dictionary<string, string> Read()
        {
            var texts = new Dictionary<string, string>();
            var folder = Dd1Install.PathOf(Folder);
            if (folder == null || !Directory.Exists(folder)) return texts;
            try
            {
                var main = Path.Combine(folder, MainTable);
                if (File.Exists(main)) ReadEnglish(File.ReadLines(main), texts, null);
                var others = Directory.GetFiles(folder, "*.string_table.xml");
                Array.Sort(others, StringComparer.OrdinalIgnoreCase);
                foreach (var file in others)
                {
                    if (string.Equals(Path.GetFileName(file), MainTable, StringComparison.OrdinalIgnoreCase)) continue;
                    ReadEnglish(File.ReadLines(file), texts, ElsewherePrefixes);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 string tables could not be read: " + e.Message); }
            return texts;
        }

        /// <summary>
        /// Adds the entries of a table's English section. Each file holds every language, English first, so
        /// reading stops where its section ends. A repeated id is an alternative line in DD1; the first is kept.
        /// </summary>
        internal static void ReadEnglish(IEnumerable<string> lines, Dictionary<string, string> into, string[] prefixes)
        {
            var english = false;
            string id = null;
            StringBuilder text = null;
            foreach (var line in lines)
            {
                if (id == null)
                {
                    if (line.IndexOf("<language ", StringComparison.Ordinal) >= 0)
                    {
                        if (english) break;
                        english = line.IndexOf("id=\"english\"", StringComparison.Ordinal) >= 0;
                        continue;
                    }
                    if (!english) continue;
                    if (line.IndexOf("</language>", StringComparison.Ordinal) >= 0) break;
                    var at = line.IndexOf(EntryStart, StringComparison.Ordinal);
                    if (at < 0) continue;
                    var idEnd = line.IndexOf('"', at + EntryStart.Length);
                    var textAt = idEnd < 0 ? -1 : line.IndexOf(TextStart, idEnd, StringComparison.Ordinal);
                    if (textAt < 0) continue;
                    id = line.Substring(at + EntryStart.Length, idEnd - at - EntryStart.Length);
                    text = new StringBuilder();
                    var rest = line.Substring(textAt + TextStart.Length);
                    if (!Finish(rest, text)) continue;
                }
                else if (!Finish(line, text.Append('\n'))) continue;

                if (!into.ContainsKey(id) && Wanted(id, prefixes)) into[id] = text.ToString();
                id = null;
            }
        }

        // Appends a line's share of an entry's text; true when the entry ends on this line.
        private static bool Finish(string line, StringBuilder text)
        {
            var end = line.IndexOf(TextEnd, StringComparison.Ordinal);
            if (end < 0)
            {
                text.Append(line);
                return false;
            }
            text.Append(line, 0, end);
            return true;
        }

        private static bool Wanted(string id, string[] prefixes)
        {
            if (prefixes == null) return true;
            foreach (var prefix in prefixes)
                if (id.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
