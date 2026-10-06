using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's sentences for its Activity Log, read from the player's install (the main string table, through
    /// <see cref="Dd1Strings"/>) and filled in the way DD1 writes them: printf places for the names and
    /// numbers, "{colour_start|id}" and "{colour_end}" around what DD1 colours (the building's name, the
    /// hero's, a good or a bad result; colours/base.colours.darkest), "{?name}" notes for its translators.
    /// The marks become TextMeshPro colour tags, so a line is stored and shown as DD1 would colour it.
    /// The quoted texts in the callers are DD1's own wording, kept as FALLBACKS for a missing table.
    /// </summary>
    internal static class ActivityLogText
    {
        // DD1's colours of the log (base.colours.darkest); the stock values stand in when the file is gone.
        public const string BuildingColour = "town_activity_log_building_name";
        public const string HeroColour = "town_activity_log_character_name";
        public const string GoodColour = "town_activity_log_positive_result";
        public const string BadColour = "town_activity_log_negative_result";

        private static readonly Regex Tags = new Regex("<[^>]+>");

        /// <summary>A DD1 sentence by its string id, filled in; <paramref name="fallback"/> is DD1's own wording of it.</summary>
        public static string Story(string id, string fallback, params object[] args)
        {
            return Rich(Dd1Strings.Get(id) ?? fallback, args);
        }

        public static bool Has(string id) => Dd1Strings.Has(id);

        /// <summary>
        /// Fills a DD1 format: %s, %d, %+d and %% as printf, colour marks as TextMeshPro tags, every other
        /// "{...}" dropped. An argument may carry tags of its own (a DD2 quirk's coloured name).
        /// </summary>
        public static string Rich(string format, params object[] args)
        {
            if (format == null) return "";
            var text = new StringBuilder(format.Length + 32);
            var next = 0;
            var space = false;
            for (var i = 0; i < format.Length; i++)
            {
                var c = format[i];
                if (c == '{')
                {
                    var close = format.IndexOf('}', i);
                    if (close > i)
                    {
                        var mark = format.Substring(i + 1, close - i - 1);
                        if (mark.StartsWith("colour_start|", StringComparison.Ordinal))
                        {
                            Flush(text, ref space);
                            text.Append("<color=").Append(Hex(mark.Substring("colour_start|".Length))).Append('>');
                        }
                        else if (mark == "colour_end") text.Append("</color>");
                        i = close;
                        continue;
                    }
                }
                // DD1 breaks its lines by hand; a label wraps by itself
                if (c == '\r' || c == '\n' || c == '\t' || c == ' ')
                {
                    space = text.Length > 0;
                    continue;
                }
                Flush(text, ref space);
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
                var arg = args != null && next < args.Length ? args[next] : null;
                next++;
                if (kind == 'd')
                {
                    int number;
                    try { number = Convert.ToInt32(arg ?? 0, CultureInfo.InvariantCulture); }
                    catch (Exception) { number = 0; }
                    text.Append(plus && number >= 0 ? "+" : "").Append(number.ToString(CultureInfo.InvariantCulture));
                }
                else text.Append(arg);
                i += plus ? 2 : 1;
            }
            return text.ToString();
        }

        private static void Flush(StringBuilder text, ref bool space)
        {
            if (space) text.Append(' ');
            space = false;
        }

        /// <summary>"#c8b46e": a DD1 colour for a rich text tag (its neutral text colour for one it does not have).</summary>
        public static string Hex(string dd1Colour)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(Dd1Fonts.Colour(dd1Colour, Dd1Fonts.Colour("neutral", new Color32(174, 172, 162, 255))));
        }

        public static string Tint(string text, string dd1Colour) => "<color=" + Hex(dd1Colour) + ">" + text + "</color>";

        public static string Hero(string name) => Tint(name, HeroColour);

        public static string Building(string name) => Tint(name + ":", BuildingColour);

        public static string Good(string text) => Tint(text, GoodColour);

        public static string Bad(string text) => Tint(text, BadColour);

        /// <summary>"Reynauld, Dismas and Junia", each name in DD1's colour for a hero, joined with DD1's "and".</summary>
        public static string Names(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return Hero("Nobody");
            var and = WindowText.Plain("str_and") ?? "and";
            var text = new StringBuilder();
            for (var i = 0; i < names.Count; i++)
            {
                if (i > 0) text.Append(i == names.Count - 1 ? " " + and + " " : ", ");
                text.Append(Hero(names[i]));
            }
            return text.ToString();
        }

        /// <summary>DD1's short word for a quest's difficulty ("Lvl. 3").</summary>
        public static string Difficulty(int dd1Difficulty)
        {
            return WindowText.Plain("str_difficulty_" + dd1Difficulty) ?? "Lvl. " + dd1Difficulty;
        }

        /// <summary>DD1's word for a quest's length ("Short").</summary>
        public static string Length(int length)
        {
            return WindowText.Plain("town_quest_length_" + length) ?? (length >= 3 ? "Long" : length == 2 ? "Medium" : "Short");
        }

        /// <summary>DD1's name of a resolve level ("Veteran").</summary>
        public static string ResolveName(int level)
        {
            return WindowText.Plain("str_resolve_" + level) ?? "Level " + level;
        }

        /// <summary>A line without its tags, for the test bridge and the game's log.</summary>
        public static string Plain(string rich)
        {
            return string.IsNullOrEmpty(rich) ? "" : Tags.Replace(rich, "").Trim();
        }
    }
}
