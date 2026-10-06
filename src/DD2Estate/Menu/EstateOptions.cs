using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Menu
{
    /// <summary>
    /// The settings of the mod a player changes in the game: the rows of the "Estate" tab in DD2's options
    /// screen (<see cref="EstateOptionsTab"/> draws them), what each says, and how a change is taken.
    ///
    /// Every row but the last is an entry of the plugin's config, bound by the module it belongs to. A change
    /// is written to the entry at once (BepInEx saves the file) and the module follows its entry's
    /// SettingChanged or reads the entry when it needs it, so nothing waits for a restart:
    ///
    ///   fonts       [Look] Fonts            every label on screen changes (UI/Dd2Fonts)
    ///   loading     [Look] LoadingScreen    the next loading screen (Dd2/LoadingScreen reads the entry)
    ///   trinkets    [Rules] UniqueTrinkets  the next draw (Estate/Trinkets)
    ///   supplies    [Fight] BagStandIns     the next item used (Dungeon/FightBag)
    ///   sound       [Audio] Dd1Audio        music and ambience stop or start (Dd2/EstateAudio looks every frame)
    ///   level       [Audio] Dd1Level        heard while the slider is dragged; the file is written when it rests
    ///   tutorials   [Rules] TutorialPopups  the next one due (Estate/Tutorials)
    ///   folder      [Paths] DarkestDungeon1 THE ONE THAT WAITS: see below
    ///
    /// The Darkest Dungeon folder. Everything the mod has read from DD1 is kept for the run of the game, in
    /// well over a hundred places (pictures, fonts, words, layouts, rules, the sound banks) and in what the
    /// screens that are up were built from; to read it all again from another folder in the middle of a
    /// session could not be made safe. So the folder IN USE never changes while the game runs: a folder the
    /// player names is checked (Core/Dd1Folder: it must be a Darkest Dungeon install with the files the Estate
    /// reads; a wrong one is refused with the reason in words and the config is left as it was), a right one
    /// is saved, and the row says "in use after the game is restarted". "Find through Steam" empties the
    /// entry again, unless Steam's search finds no install: then the folder set by hand stays.
    /// (The one time a folder is taken into use without a restart is before any of this: at the main menu, in
    /// a run in which no Darkest Dungeon was found and so nothing has been read. Menu/Dd1Prompt.cs.)
    /// </summary>
    internal static class EstateOptions
    {
        internal enum Kind { Choice, Switch, Level, Folder }

        internal sealed class Setting
        {
            public string Id, Label, Tip, Section, Key;
            public Kind Kind;
            /// <summary>A choice's values as the config has them, and as the player reads them.</summary>
            public string[] Values, Words;

            public ConfigEntryBase Entry
            {
                get
                {
                    var file = Plugin.Settings;
                    var definition = new ConfigDefinition(Section, Key);
                    return file != null && file.ContainsKey(definition) ? file[definition] : null;
                }
            }
        }

        private static readonly string[] Looks = { "dd2", "dd1" };
        private static readonly string[] LookWords = { "Darkest Dungeon II", "Darkest Dungeon" };

        public const string FolderId = "folder";

        /// <summary>The rows, in the order of the tab.</summary>
        public static readonly Setting[] All =
        {
            new Setting
            {
                Id = "fonts", Kind = Kind.Choice, Section = "Look", Key = "Fonts", Values = Looks, Words = LookWords, Label = "Fonts",
                Tip = "Darkest Dungeon II's lettering stays sharp at any resolution; the original's was drawn for 1920 x 1080."
            },
            new Setting
            {
                Id = "loading", Kind = Kind.Choice, Section = "Look", Key = "LoadingScreen", Values = Looks, Words = LookWords, Label = "Loading Screens",
                Tip = "Darkest Dungeon II's loading screens pass by themselves; the original's wait for a key."
            },
            new Setting
            {
                Id = "trinkets", Kind = Kind.Switch, Section = "Rules", Key = "UniqueTrinkets", Label = "One of Each Trinket",
                Tip = "Quests, the Nomad Wagon, battles and curios yield only trinkets the Estate lacks. Off: copies may drop, as in the original."
            },
            new Setting
            {
                Id = "supplies", Kind = Kind.Switch, Section = "Fight", Key = "BagStandIns", Label = "Supplies Heal in Combat",
                Tip = "Used in combat, bandages, antivenom, herbs, holy water and laudanum also restore a little health or stress. Off: the original's effects alone."
            },
            new Setting
            {
                Id = "sound", Kind = Kind.Switch, Section = "Audio", Key = "Dd1Audio", Label = "Darkest Dungeon Sound",
                Tip = "The Ancestor's voice, the music and the ambience of the original, played from your Darkest Dungeon install."
            },
            new Setting
            {
                Id = "level", Kind = Kind.Level, Section = "Audio", Key = "Dd1Level", Label = "Darkest Dungeon Sound Level",
                Tip = "How loud the original's sound is against Darkest Dungeon II's own."
            },
            new Setting
            {
                Id = "tutorials", Kind = Kind.Switch, Section = "Rules", Key = "TutorialPopups", Label = "Tutorial Pop-ups",
                Tip = "The original's tutorial messages, each shown once in an Estate."
            },
            new Setting
            {
                Id = FolderId, Kind = Kind.Folder, Section = "Paths", Key = "DarkestDungeon1", Label = "Darkest Dungeon Folder",
                Tip = "The Estate reads its Hamlet, dungeons, words and sound from your Darkest Dungeon install. Another folder is in use after the game is restarted."
            }
        };

        public static Setting Find(string id)
        {
            foreach (var setting in All)
                if (string.Equals(setting.Id, id, StringComparison.OrdinalIgnoreCase)) return setting;
            return null;
        }

        // ---- values ----------------------------------------------------------------------------------------

        /// <summary>A choice's place in its list (0 for a word the list does not know), a switch's bool, a level's 0..1, the folder's config text.</summary>
        public static object Get(Setting setting)
        {
            var entry = setting.Entry;
            switch (setting.Kind)
            {
                case Kind.Choice:
                    var word = ((entry?.BoxedValue as string) ?? "").Trim();
                    var at = Array.FindIndex(setting.Values, value => string.Equals(value, word, StringComparison.OrdinalIgnoreCase));
                    return at < 0 ? 0 : at;
                case Kind.Switch:
                    return entry?.BoxedValue is bool on && on;
                case Kind.Level:
                    return entry?.BoxedValue is float level ? Mathf.Clamp01(level) : 1f;
                default:
                    return (entry?.BoxedValue as string) ?? "";
            }
        }

        /// <summary>The value as a word, for the bridge.</summary>
        public static string Shown(Setting setting)
        {
            var value = Get(setting);
            switch (setting.Kind)
            {
                case Kind.Choice: return setting.Values[(int)value];
                case Kind.Switch: return (bool)value ? "true" : "false";
                case Kind.Level: return ((float)value).ToString("0.###", CultureInfo.InvariantCulture);
                default: return (string)value;
            }
        }

        /// <summary>
        /// A change, as the tab's widgets make it. Null when it was taken, else why it was not. A level is put
        /// into its entry at once and the file is written a moment after the last change (a slider that is
        /// dragged changes sixty times a second).
        /// </summary>
        public static string Set(Setting setting, object value)
        {
            if (setting.Kind == Kind.Folder) return SetFolder(value as string);
            var entry = setting.Entry;
            if (entry == null) return "the plugin has no setting [" + setting.Section + "] " + setting.Key;
            switch (setting.Kind)
            {
                case Kind.Choice:
                    var at = value is int index ? index : -1;
                    if (at < 0 || at >= setting.Values.Length) return "no such choice";
                    entry.BoxedValue = setting.Values[at];
                    break;
                case Kind.Switch:
                    if (!(value is bool on)) return "true or false";
                    entry.BoxedValue = on;
                    break;
                case Kind.Level:
                    if (!(value is float level) || float.IsNaN(level)) return "a number from 0 to 1";
                    Quietly(entry, Mathf.Clamp01(level));
                    break;
            }
            return null;
        }

        /// <summary>A value given as a word (the bridge): "dd1", "true", "0.4", a folder.</summary>
        public static string SetByWord(Setting setting, string word)
        {
            word = (word ?? "").Trim();
            switch (setting.Kind)
            {
                case Kind.Choice:
                    var at = Array.FindIndex(setting.Values, value => string.Equals(value, word, StringComparison.OrdinalIgnoreCase));
                    if (at < 0) at = Array.FindIndex(setting.Words, value => string.Equals(value, word, StringComparison.OrdinalIgnoreCase));
                    return at < 0 ? "one of: " + string.Join(", ", setting.Values) : Set(setting, at);
                case Kind.Switch:
                    if (string.Equals(word, "true", StringComparison.OrdinalIgnoreCase) || word == "1" || string.Equals(word, "on", StringComparison.OrdinalIgnoreCase)) return Set(setting, true);
                    if (string.Equals(word, "false", StringComparison.OrdinalIgnoreCase) || word == "0" || string.Equals(word, "off", StringComparison.OrdinalIgnoreCase)) return Set(setting, false);
                    return "true or false";
                case Kind.Level:
                    return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var level) ? Set(setting, level) : "a number from 0 to 1";
                default:
                    return string.Equals(word, "steam", StringComparison.OrdinalIgnoreCase) ? FindThroughSteam() : SetFolder(word);
            }
        }

        // ---- the file --------------------------------------------------------------------------------------

        private const float SaveAfter = 0.4f;
        private static float _saveDue;
        private static bool _saving;

        // The entry's value now (who follows the entry hears it at once), the file when the value has rested.
        private static void Quietly(ConfigEntryBase entry, object value)
        {
            var file = Plugin.Settings;
            var keep = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
            try { entry.BoxedValue = value; }
            finally { file.SaveOnConfigSet = keep; }
            _saveDue = Time.unscaledTime + SaveAfter;
            if (_saving || Plugin.Host == null) return;
            _saving = true;
            Plugin.Host.StartCoroutine(SaveSoon());
        }

        private static IEnumerator SaveSoon()
        {
            while (_saving && Time.unscaledTime < _saveDue) yield return null;
            Flush();
        }

        /// <summary>Writes the file now if a value waits to be written (the options screen is closed).</summary>
        public static void Flush()
        {
            if (!_saving) return;
            _saving = false;
            try { Plugin.Settings.Save(); }
            catch (Exception e) { Plugin.Log.LogError("options: the config file could not be written: " + e.Message); }
        }

        public static bool SavePending => _saving;

        /// <summary>
        /// What the config FILE says of a setting right now (not the entry in memory): the text after "Key =" under
        /// the setting's section; null when the file has no such line. For the bridge: proof that a change reached the disk.
        /// </summary>
        public static string InFile(Setting setting)
        {
            try
            {
                var path = Plugin.Settings.ConfigFilePath;
                if (!File.Exists(path)) return null;
                var section = "";
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        section = line.Substring(1, line.Length - 2);
                        continue;
                    }
                    var equals = line.IndexOf('=');
                    if (equals <= 0 || section != setting.Section) continue;
                    if (line.Substring(0, equals).Trim() == setting.Key) return line.Substring(equals + 1).Trim();
                }
            }
            catch (Exception e) { return "(not readable: " + e.Message + ")"; }
            return null;
        }

        // ---- the Darkest Dungeon folder ----------------------------------------------------------------------

        /// <summary>Why the folder named last was not taken; null after one that was, and when the options are closed.</summary>
        public static string FolderProblem { get; private set; }

        public static void ForgetFolderProblem() => FolderProblem = null;

        /// <summary>A folder named by the player. Null when it was taken (and saved), else what is wrong with it: the config is then as it was.</summary>
        public static string SetFolder(string typed)
        {
            var folder = Dd1Install.Check(typed, out var problem);
            if (folder == null)
            {
                FolderProblem = problem ?? "That folder cannot be used.";
                Plugin.Log.LogInfo("options: the folder '" + typed + "' was not taken: " + FolderProblem);
                return FolderProblem;
            }
            FolderProblem = null;
            Plugin.Dd1Path.Value = folder;
            Plugin.Log.LogInfo("options: Darkest Dungeon folder set by hand: " + folder + (SameFolder(folder, Dd1Install.Root) ? " (the one in use)" : " (in use after a restart)"));
            return null;
        }

        /// <summary>Back to the search through Steam's libraries. Null when done; refused (with the reason) when that search finds no install.</summary>
        public static string FindThroughSteam()
        {
            var found = Dd1Install.FromSteam();
            if (found == null)
            {
                FolderProblem = string.IsNullOrEmpty(DD2Estate.Core.Dd1Folder.Clean(Plugin.Dd1Path.Value))
                    ? "No Darkest Dungeon was found in Steam's libraries. Name its folder by hand."
                    : "No Darkest Dungeon was found in Steam's libraries. The folder set by hand is kept.";
                return FolderProblem;
            }
            FolderProblem = null;
            // (already left to the search: nothing to write, nothing to tell)
            if (string.IsNullOrEmpty(Plugin.Dd1Path.Value)) return null;
            Plugin.Dd1Path.Value = "";
            Plugin.Log.LogInfo("options: Darkest Dungeon folder found through Steam: " + found + (SameFolder(found, Dd1Install.Root) ? " (the one in use)" : " (in use after a restart)"));
            return null;
        }

        private static bool SameFolder(string a, string b)
        {
            if (a == null || b == null) return a == b;
            return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        internal sealed class FolderState
        {
            /// <summary>The folder this run of the game reads from; null: none was found.</summary>
            public string InUse;
            public Dd1Install.Origin InUseBy;
            /// <summary>The folder the next start will read from, as the config stands.</summary>
            public string Next;
            public Dd1Install.Origin NextBy;
            /// <summary>True: the next start reads from another folder than this run.</summary>
            public bool Restart;
            /// <summary>The config's own text, and whether it names a folder that is no Darkest Dungeon (written by hand into the file).</summary>
            public string Config;
            public bool ConfigIgnored;
            public string Problem;
            /// <summary>The row's two lines; <see cref="Bad"/>: the second tells of a refusal.</summary>
            public string Folder, Status;
            public bool Bad;
        }

        /// <summary>Where the folder stands, with the words the row shows. It looks at the disk: ask when something changed, not every frame.</summary>
        public static FolderState Folder()
        {
            var state = new FolderState { InUse = Dd1Install.Root, InUseBy = Dd1Install.FoundBy, Config = DD2Estate.Core.Dd1Folder.Clean(Plugin.Dd1Path.Value), Problem = FolderProblem };
            state.Next = Dd1Install.Next(out state.NextBy);
            state.Restart = !SameFolder(state.InUse, state.Next);
            state.ConfigIgnored = state.Config.Length > 0 && state.NextBy != Dd1Install.Origin.ByHand;
            state.Folder = state.InUse ?? "Not found";
            if (state.Problem != null)
            {
                state.Status = "Not taken. " + state.Problem;
                state.Bad = true;
            }
            else if (state.Restart)
            {
                state.Status = state.Next == null
                    ? "After the game is restarted: none (the folder is gone)."
                    : "In use after the game is restarted: " + state.Next + (state.NextBy == Dd1Install.Origin.ByHand ? " (set by hand)." : " (found through Steam).");
            }
            else if (state.InUse == null)
            {
                state.Status = "The Estate cannot draw its Hamlet without it. Name the folder of your Darkest Dungeon install.";
                state.Bad = true;
            }
            else if (state.ConfigIgnored)
            {
                state.Status = "Found through Steam. The folder in the config file is not a Darkest Dungeon install: " + state.Config;
            }
            else
            {
                state.Status = state.NextBy == Dd1Install.Origin.ByHand ? "Set by hand." : "Found through Steam.";
            }
            return state;
        }

        public static object Describe(Setting setting)
        {
            return new
            {
                id = setting.Id, label = setting.Label, kind = setting.Kind.ToString().ToLowerInvariant(),
                value = Shown(setting),
                config = "[" + setting.Section + "] " + setting.Key,
                inFile = InFile(setting),
                choices = setting.Values,
                tip = setting.Tip
            };
        }

        public static List<object> DescribeAll()
        {
            var rows = new List<object>();
            foreach (var setting in All) rows.Add(Describe(setting));
            return rows;
        }
    }
}
