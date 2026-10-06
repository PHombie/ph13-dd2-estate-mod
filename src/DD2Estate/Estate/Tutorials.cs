using System;
using System.Collections.Generic;
using System.IO;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's tutorial pop-ups: which there are, when each has been shown, and the queue between a moment of the
    /// game and the window (<see cref="TutorialPopup"/>). The moments themselves are watched in
    /// <see cref="TutorialTriggers"/>.
    ///
    /// How DD1 does it, read out of its exe (_windows/win32/Darkest.exe; the addresses are of that build):
    /// <list type="bullet">
    /// <item>The list is a table in the exe, 72 entries of a name, a kind and a delay in seconds (built at
    /// 0x4ad840): the base game's 34, then the DLCs'. A tutorial's picture is
    /// shared/tutorial_popup/tutorial_popup.&lt;id&gt;.png, its words tutorial_popup_&lt;id&gt;_title and
    /// _description (0xb9c232, 0xb9c3a3).</item>
    /// <item>A moment of the game asks for a tutorial by its number (0x9c5f80: number, "even if seen", and a
    /// condition that may be empty). Nothing happens when the option "Tutorials" is off (option 0x21 of
    /// shared/options, "Show tutorial pop-up messages."), when the campaign has shown that tutorial before, or
    /// when it is already waiting. Otherwise it waits for its delay, then for its condition, and is shown; the
    /// moment it is shown it is written down as seen (0x9c68a0) in the campaign's save (persist.tutorial.json).
    /// </item>
    /// </list>
    /// Here the seen ones are a section of the estate's save ("tutorials"), the option is [Rules] Tutorials of
    /// the plugin's config, and one window is up at a time.
    /// </summary>
    [EstateModule]
    internal static class Tutorials
    {
        public sealed class Def
        {
            public string Id;
            /// <summary>DD1's seconds between the moment and the window (the exe's table).</summary>
            public float Delay;
            /// <summary>The moment in DD1.</summary>
            public string Dd1;
            /// <summary>The moment in the estate; null when the tutorial is left out.</summary>
            public string Mod;
            /// <summary>Why it is left out.</summary>
            public string LeftOut;
        }

        private sealed class Waiting
        {
            public Def Def;
            public float Delay;
            public Func<bool> When;
        }

        // DD1's table in DD1's order. The delays are the exe's; the moments were read at the call sites of
        // 0x9c5f20 / 0x9c5f80 (see TutorialTriggers for each address).
        private static readonly Def[] Table =
        {
            Out("press_h", 1f, "the Activity Log is closed (mouse and keyboard)", "the estate has no hold-[H] help sheet"),
            Out("combat", 0f, "a fight begins", "fights are DD2's"),
            In("map_nav", 2.5f, "a room comes up on the raid screen", "the party stands in a room of an expedition"),
            In("hallway_nav", 3f, "the party enters a hallway", "the party enters a hallway"),
            In("curio", 0f, "a curio stands in the hallway or room on screen", "an untouched curio is in view"),
            Out("bandage", 0f, "a hero bleeds in a fight", "fights are DD2's"),
            Out("antivenom", 0f, "a hero is blighted in a fight", "fights are DD2's"),
            In("torch", 0f, "the light falls under half (50)", "the light falls under 50"),
            In("supplies", 1f, "the Provision screen opens for a quest without firewood", "the Provision screen opens for a quest without firewood"),
            Out("map_combat_skills", 1f, "the skills of DD1's character sheet", "the hero sheet is DD2's"),
            Out("map_camping_skills", 1f, "the camping skills of DD1's character sheet", "the hero sheet is DD2's"),
            In("camping_quest", 0f, "the Provision screen opens for a quest with firewood", "the Provision screen opens for a quest with firewood"),
            In("camping", 1f, "the camp's meal window opens", "the camp screen opens"),
            In("stress_relief", 0f, "the Tavern or the Abbey is opened", "the Tavern or the Abbey is opened"),
            Out("afflicted", 1f, "a hero's resolve is tested and fails", "DD2's meltdown passes with the fight"),
            Out("virtued", 1f, "a hero's resolve is tested and holds", "DD2's resolute passes with the fight"),
            Out("death_door", 1f, "a hero falls to Death's Door", "fights are DD2's"),
            In("consider_retreat", 1f, "a hero of the party dies", "a hero dies on an expedition (shown once the corridor is back)"),
            In("disarm_trap", 0f, "a scouted trap lies in the hallway on screen", "a spotted trap that has not gone off is in view"),
            In("stage_coach", 0f, "the Stage Coach is opened", "the Stage Coach is opened"),
            In("resolve_level", 0f, "a resolve bar passes to a higher level than the one it showed (it showed 1 or more)", "a hero of level 1 or more gains a level (shown in the hamlet)"),
            Out("heart_attack", 0f, "a hero's stress fills a second time", "DD2 has no heart attack"),
            In("locking_pos_quirks", 0f, "the Sanitarium is opened", "the Sanitarium is opened"),
            In("quest_complete", 0f, "the quest's goal is reached (its banner)", "the quest's goal is reached"),
            Out("permanent_neg_quirks", 0f, "a negative quirk locks into place", "DD2's quirks do not harden"),
            Out("high_prot", 0f, "a monster with high PROT in a fight", "fights are DD2's"),
            In("hero_panel", 0f, "an expedition begins (not the Old Road)", "an expedition comes up"),
            Out("death_class", 0f, "a monster leaves a corpse", "fights are DD2's"),
            Out("quest_restriction_darkest_dungeon", 0f, "a hero refuses the Darkest Dungeon a second time", "the estate has no Never Again"),
            In("scout_hidden_door", 0f, "scouting finds a secret room", "scouting finds a secret room"),
            In("embark", 5f, "the town comes up; shown once four heroes are on the roster and no window is open", "the hamlet comes up; shown once four heroes are on the roster and no window is open"),
            In("quest_select", 0f, "the Estate Map opens", "the Estate Map opens"),
            In("stage_coach2", 0f, "a recruit is hired and the coach stands empty", "a recruit is hired and the coach stands empty"),
            In("loot", 0f, "a loot window opens (without a blueprint or a memory in it)", "the loot scroll opens"),
            // The Districts (their own feature in DD1's Crimson Court folder; the Courtyard's own tutorials are left out with it).
            In("districts", 0f, "the town event Cornerstones (its tutorial_ids)", "the town crier tells of Cornerstones (cc_districts_unlock)"),
            In("districts_panel", 0f, "the Districts screen opens", "the Districts screen opens"),
            In("blueprints", 0f, "a loot window opens with a blueprint in it", "the estate comes to hold more blueprints (a boss's is handed over at the quest's end; shown in the hamlet)")
        };

        /// <summary>Art and words of the Districts' tutorials lie with the feature.</summary>
        internal static readonly string[] ArtRoots = { "", "dlc/580100_crimson_court/features/districts/" };

        private static readonly Dictionary<string, Def> ById = new Dictionary<string, Def>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Waiting> Queued = new List<Waiting>();

        /// <summary>DD1's option "Tutorials" ([Rules] Tutorials of the plugin's config).</summary>
        public static bool Option = true;
        /// <summary>Off for this run of the game only (the dev bridge: a test that must not be stopped by a window).</summary>
        public static bool Silenced;

        public static bool Enabled => Option && !Silenced;

        public static IReadOnlyList<Def> All => Table;

        private static Def In(string id, float delay, string dd1, string mod) => new Def { Id = id, Delay = delay, Dd1 = dd1, Mod = mod };

        private static Def Out(string id, float delay, string dd1, string why) => new Def { Id = id, Delay = delay, Dd1 = dd1, LeftOut = why };

        private static void Register()
        {
            foreach (var def in Table) ById[def.Id] = def;
            EstateState.RegisterSection("tutorials", Save, Load, () =>
            {
                Seen.Clear();
                Queued.Clear();
            });
            NarrationMoments.Tick += Tick;
            NarrationMoments.SessionEnded += () =>
            {
                Queued.Clear();
                TutorialPopup.Close(false);
            };
        }

        private static JToken Save() => new JObject { ["seen"] = new JArray(Seen) };

        // An estate saved before there were tutorials is past its first steps: it counts as having seen them all
        // (as such an estate keeps every building open). The bridge's tutorial.reset puts them back on it.
        private static void Load(JToken json)
        {
            Seen.Clear();
            Queued.Clear();
            if (json is JObject saved && saved["seen"] is JArray seen)
            {
                foreach (var id in seen) Seen.Add((string)id);
            }
            else
            {
                foreach (var def in Table) Seen.Add(def.Id);
            }
        }

        public static Def Find(string id) => id != null && ById.TryGetValue(id, out var def) ? def : null;

        public static bool HasSeen(string id) => Seen.Contains(id);

        public static bool IsQueued(string id)
        {
            foreach (var waiting in Queued)
                if (string.Equals(waiting.Def.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// A moment of the game asks for its tutorial (DD1 0x9c5f80): not while the option is off, not twice an
        /// estate, not while it already waits. It is shown after DD1's delay, once <paramref name="when"/> holds
        /// (null: at once) and the screen is free.
        /// </summary>
        public static bool Queue(string id, Func<bool> when = null)
        {
            var def = Find(id);
            if (def == null || !Enabled || !EstateSession.Active) return false;
            if (Seen.Contains(def.Id) || IsQueued(def.Id) || TutorialPopup.Current == def.Id) return false;
            Queued.Add(new Waiting { Def = def, Delay = def.Delay, When = when });
            Plugin.Log.LogInfo("Tutorial: " + def.Id + " is asked for" + (def.Delay > 0f ? " (in " + def.Delay.ToString("0.#") + " s)" : ""));
            return true;
        }

        /// <summary>DD1 0x9c61c0: a tutorial that waits is dropped (the town does it with Embark every time it comes up).</summary>
        public static void Cancel(string id)
        {
            Queued.RemoveAll(waiting => string.Equals(waiting.Def.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether a window may come up over what is on screen: one of the estate's own screens, standing still.</summary>
        public static bool ScreenFree
        {
            get
            {
                if (!EstateSession.Active || EstateSession.Starting || !EstateSession.InHub || EstateCurtain.Down) return false;
                return HamletScreen.IsOpen || Dungeon.DungeonRun.Current != null || Dungeon.RaidResultsScreen.IsOpen;
            }
        }

        private static void Tick()
        {
            if (Queued.Count == 0) return;
            if (!Enabled)
            {
                Queued.Clear();
                return;
            }
            var dt = Time.unscaledDeltaTime;
            foreach (var waiting in Queued)
                if (waiting.Delay > 0f) waiting.Delay -= dt;
            if (TutorialPopup.Busy || !ScreenFree) return;
            for (var i = 0; i < Queued.Count; i++)
            {
                var waiting = Queued[i];
                if (waiting.Delay > 0f) continue;
                bool ready;
                try { ready = waiting.When == null || waiting.When(); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Tutorial: the condition of " + waiting.Def.Id + " failed, it is dropped: " + e.Message);
                    Queued.RemoveAt(i);
                    return;
                }
                if (!ready) continue;
                Queued.RemoveAt(i);
                // DD1 writes it down as seen the moment it is shown, whether the window could be drawn or not
                Seen.Add(waiting.Def.Id);
                if (!TutorialPopup.Show(waiting.Def.Id)) Plugin.Log.LogInfo("Tutorial: " + waiting.Def.Id + " could not be shown (DD1 has no words or picture for it)");
                return;
            }
        }

        // ---- DD1's words and pictures ------------------------------------------------------------------------

        private static Dictionary<string, string> _words;

        /// <summary>DD1's title of a tutorial as written; null when DD1 has none.</summary>
        public static string Title(string id) => Word("tutorial_popup_" + id + "_title");

        public static string Description(string id) => Word("tutorial_popup_" + id + "_description");

        // Most are in the main table. A few of the base game's (the Estate Map's, the Stage Coach's two) lie in the
        // table DD1 keeps its platform variants in; the PC's are the entries without a platform in front.
        private static string Word(string id)
        {
            var text = Dd1Strings.Get(id);
            if (text != null) return text;
            if (_words == null) _words = ReadWords();
            return _words.TryGetValue(id, out text) ? text : null;
        }

        private static Dictionary<string, string> ReadWords()
        {
            var words = new Dictionary<string, string>();
            if (!Dd1Install.Found) return words;
            var prefixes = new[] { "tutorial_popup_" };
            try
            {
                foreach (var root in ArtRoots)
                {
                    var folder = Dd1Install.PathOf(root + "localization");
                    if (folder == null || !Directory.Exists(folder)) continue;
                    var tables = Directory.GetFiles(folder, "*.string_table.xml");
                    Array.Sort(tables, StringComparer.OrdinalIgnoreCase);
                    foreach (var table in tables) Dd1Strings.ReadEnglish(File.ReadLines(table), words, prefixes);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Tutorial: DD1's string tables could not be read: " + e.Message); }
            return words;
        }

        /// <summary>The picture's path in the DD1 install; null when DD1 has none.</summary>
        public static string Picture(string id)
        {
            if (!Dd1Install.Found || string.IsNullOrEmpty(id)) return null;
            foreach (var root in ArtRoots)
            {
                var file = root + TutorialPopup.Dir + "tutorial_popup." + id + ".png";
                if (Dd1Install.Exists(file)) return file;
            }
            return null;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------------

        public static object Describe()
        {
            var waiting = new List<object>();
            foreach (var w in Queued) waiting.Add(new { id = w.Def.Id, seconds = Mathf.Max(0f, w.Delay), conditional = w.When != null });
            var seen = new List<string>(Seen);
            seen.Sort(StringComparer.Ordinal);
            return new
            {
                option = Option, silenced = Silenced, enabled = Enabled,
                screenFree = ScreenFree,
                showing = TutorialPopup.Current,
                window = TutorialPopup.Describe(),
                waiting, seen
            };
        }

        /// <summary>The bridge's tutorial.reset: the estate has seen none (or not this one).</summary>
        public static int Forget(string id = null)
        {
            if (id != null) return Seen.Remove(id) ? 1 : 0;
            var count = Seen.Count;
            Seen.Clear();
            Queued.Clear();
            TutorialTriggers.Reset();
            return count;
        }
    }
}
