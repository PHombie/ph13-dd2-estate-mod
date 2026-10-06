using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The estate's dead. DD2 forgets a dead hero (the actor is dropped, only a roster entry is left), so the
    /// graveyard keeps its own record of who fell, when, where and how; it travels in the estate's save
    /// section. How a hero died is said in DD1's words (str_death_&lt;cause&gt;_&lt;source&gt; of its string table).
    /// </summary>
    [EstateModule]
    internal static class Graveyard
    {
        public const string BuildingId = "graveyard";
        private const string SectionKey = "graveyard";
        private const string StringTable = "localization/miscellaneous.string_table.xml";
        private const string DeathId = "str_death_";

        public class Fallen
        {
            public string Name;
            public string ClassId;
            public int Week;
            public string Where;
            /// <summary>What dealt the blow, when the game knows; null otherwise.</summary>
            public string By;
            /// <summary>Resolve level at death: DD1 gives the grander stones to the more seasoned.</summary>
            public int Level;
            /// <summary>
            /// How the hero died, as DD1 names it (the first half of str_death_&lt;cause&gt;_&lt;source&gt;: attack, bleed,
            /// poisoned, heart_attack, hunger, trap, obstacle, ...); null for a death recorded without it: a
            /// blow when <see cref="By"/> is known, an unknown peril otherwise.
            /// </summary>
            public string Cause;
            /// <summary>What it came from, as DD1 names it (the second half: monster, hero, friendly, trap, obstacle, hunger, unknown); may be null.</summary>
            public string Source;
        }

        /// <summary>
        /// Where the party is, for the record of a death ("the Ruins"). The expedition sets it while it lasts
        /// and clears it when it ends; without it the place is read off the expedition's status line.
        /// </summary>
        public static string Place { get; set; }

        private static readonly List<Fallen> _fallen = new List<Fallen>();
        private static Dictionary<string, List<string>> _deaths;

        /// <summary>Oldest first.</summary>
        public static IReadOnlyList<Fallen> All => _fallen;

        /// <summary>Takes a record off the list: the hero is no longer among the dead (a town event brought them back).</summary>
        public static bool Remove(Fallen fallen) => _fallen.Remove(fallen);

        public static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, _fallen.Clear);
            Buildings.Register(BuildingId, GraveyardPanel.Open);
        }

        public static void Add(Fallen fallen)
        {
            _fallen.Add(fallen);
            Plugin.Log.LogInfo("Graveyard: " + fallen.Name + " the " + fallen.ClassId + " fell in week " + fallen.Week + " (" + fallen.Where + (fallen.By != null ? ", " + fallen.By : "") + ")");
        }

        public static string CurrentPlace()
        {
            if (!string.IsNullOrEmpty(Place)) return Place;
            var run = DungeonRun.Current;
            if (run == null) return EstateSession.View == EstateSession.Screen.Dungeon ? "an expedition" : "the Hamlet";
            // The status text of an expedition opens with the dungeon's name.
            var text = run.StatusText ?? "";
            var end = text.IndexOfAny(new[] { '\n', '<' });
            if (end >= 0) text = text.Substring(0, end);
            text = text.Trim();
            return text.Length > 0 ? text : "an expedition";
        }

        // ---- DD1's words for a death ---------------------------------------------------------------------

        /// <summary>
        /// How a hero died, in DD1's words, to follow their name: "was slain by a vile Bone Rabble.", "bled
        /// out.", "succumbed to an unknown peril." DD1 has two wordings for a monster's blow ("was slain by a
        /// vile", "met their end against a"): a grave keeps the one it drew.
        /// </summary>
        public static string Story(Fallen fallen)
        {
            var cause = fallen.Cause;
            var source = fallen.Source;
            if (string.IsNullOrEmpty(cause) && !string.IsNullOrEmpty(fallen.By))
            {
                cause = "attack";
                source = "monster";
            }
            string words = null;
            if (!string.IsNullOrEmpty(cause))
            {
                words = Words(cause + (string.IsNullOrEmpty(source) ? "" : "_" + source), fallen) ?? Words(cause + "_unknown", fallen) ?? Words(cause, fallen);
                // FALLBACK: DD1's own wording, for an install whose table cannot be read.
                if (words == null && cause == "attack" && source == "monster") words = "was slain by a vile";
            }
            if (words == null) return Words("unknown_unknown", fallen) ?? "succumbed to an unknown peril.";
            // A wording that does not end its sentence waits for the killer's name.
            var last = words[words.Length - 1];
            if (last == '.' || last == '!' || last == '?') return words;
            return string.IsNullOrEmpty(fallen.By) ? words.TrimEnd() + "." : words + " " + fallen.By + ".";
        }

        // One of DD1's wordings of str_death_<id>, always the same one for the same grave.
        private static string Words(string id, Fallen fallen)
        {
            if (_deaths == null) _deaths = ReadDeaths();
            if (!_deaths.TryGetValue(DeathId + id, out var wordings) || wordings.Count == 0) return null;
            if (wordings.Count == 1) return wordings[0];
            unchecked
            {
                // FNV-1a: the same on every runtime, which string.GetHashCode is not
                var hash = 2166136261u;
                foreach (var c in (fallen.Name ?? "") + "|" + (fallen.ClassId ?? "") + "|" + fallen.Week) hash = (hash ^ c) * 16777619u;
                return wordings[(int)(hash % (uint)wordings.Count)];
            }
        }

        // The table's English section, every wording of a death id: an id written twice is two wordings DD1
        // picks from (the mod's reader of the table keeps the first only).
        private static Dictionary<string, List<string>> ReadDeaths()
        {
            var deaths = new Dictionary<string, List<string>>();
            try
            {
                var path = Dd1Install.PathOf(StringTable);
                if (path == null || !File.Exists(path)) return deaths;
                const string entry = "<entry id=\"", open = "<![CDATA[", close = "]]>";
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
                    var at = line.IndexOf(entry + DeathId, StringComparison.Ordinal);
                    if (at < 0) continue;
                    var idEnd = line.IndexOf('"', at + entry.Length);
                    var textAt = idEnd < 0 ? -1 : line.IndexOf(open, idEnd, StringComparison.Ordinal);
                    var textEnd = textAt < 0 ? -1 : line.IndexOf(close, textAt, StringComparison.Ordinal);
                    if (textEnd < 0) continue;
                    var id = line.Substring(at + entry.Length, idEnd - at - entry.Length);
                    // without DD1's colour marks: a grave's line is one colour
                    var text = Dd1Strings.Format(line.Substring(textAt + open.Length, textEnd - textAt - open.Length));
                    if (string.IsNullOrEmpty(text)) continue;
                    if (!deaths.TryGetValue(id, out var wordings)) deaths[id] = wordings = new List<string>();
                    if (!wordings.Contains(text)) wordings.Add(text);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1's words for a death could not be read: " + e.Message); }
            return deaths;
        }

        // ---- save section --------------------------------------------------------------------------------

        private static JToken Save()
        {
            return new JArray(_fallen.Select(f => new JObject
            {
                ["name"] = f.Name,
                ["cls"] = f.ClassId,
                ["week"] = f.Week,
                ["where"] = f.Where,
                ["by"] = f.By,
                ["level"] = f.Level,
                ["cause"] = f.Cause,
                ["source"] = f.Source
            }));
        }

        private static void Load(JToken token)
        {
            _fallen.Clear();
            if (!(token is JArray list)) return;
            foreach (var item in list)
            {
                _fallen.Add(new Fallen
                {
                    Name = (string)item["name"] ?? "?",
                    ClassId = (string)item["cls"] ?? "",
                    Week = (int?)item["week"] ?? 0,
                    Where = (string)item["where"],
                    By = (string)item["by"],
                    Level = (int?)item["level"] ?? 0,
                    Cause = (string)item["cause"],
                    Source = (string)item["source"]
                });
            }
        }
    }
}
