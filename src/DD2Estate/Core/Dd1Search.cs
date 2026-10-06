using System;
using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>
    /// Where a Darkest Dungeon (1) install may lie, the likeliest place first. Nothing is searched wholesale:
    /// each place is one folder, named by something that knows (the config, Darkest Dungeon II's own folder,
    /// Steam's list of its libraries, Steam's own folder as Windows has it written down) or guessed by the
    /// names Steam gives its folders.
    ///
    ///   1  the config's folder                              [Paths] DarkestDungeon1, when it names one
    ///   2  beside Darkest Dungeon II                        (library)/steamapps/common/DarkestDungeon
    ///   3  the libraries of that Steam                      (library)/steamapps/libraryfolders.vdf
    ///   4  Steam's own folder, from the registry            its steamapps/common, then the libraries its
    ///                                                       steamapps/libraryfolders.vdf and
    ///                                                       config/libraryfolders.vdf name
    ///   5  Steam under Program Files                        the same
    ///   6  (drive):/Steam and (drive):/SteamLibrary         the guesses, last
    ///
    /// A library other than Steam's first has no list of the others (its own file, libraryfolder.vdf, names only
    /// itself): with Darkest Dungeon II in such a library and Darkest Dungeon in a third, 4 is what finds it.
    /// </summary>
    public static class Dd1Search
    {
        public sealed class Place
        {
            /// <summary>The folder that would be the install.</summary>
            public string Folder;
            /// <summary>How the place was come by, in a few words (for the log and the dev bridge).</summary>
            public string By;
            /// <summary>True for the config's own folder: one a player named, not one that was found.</summary>
            public bool ByHand;
        }

        public const string ByConfig = "the config's folder";
        public const string ByNeighbour = "beside Darkest Dungeon II";
        public const string ByNeighbourLibraries = "a Steam library (the list beside Darkest Dungeon II)";
        public const string ByRegistry = "Steam's folder (the registry)";
        public const string ByRegistryLibraries = "a Steam library (the list of the registry's Steam)";
        public const string ByProgramFiles = "Steam under Program Files";
        public const string ByProgramFilesLibraries = "a Steam library (the list under Program Files)";
        public const string ByDrive = "a guess by drive";

        private static string Join(string folder, params string[] parts)
        {
            var path = folder ?? "";
            foreach (var part in parts)
            {
                if (path.Length == 0) path = part;
                else
                {
                    var last = path[path.Length - 1];
                    path = last == '\\' || last == '/' ? path + part : path + "\\" + part;
                }
            }
            return path;
        }

        private static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var trimmed = path.TrimEnd('\\', '/');
            var at = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
            return at <= 0 ? null : trimmed.Substring(0, at);
        }

        /// <summary>A folder's name as one to compare by: one kind of separator, none at the end, no capitals.</summary>
        public static string Key(string folder)
        {
            return (folder ?? "").Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }

        /// <summary>
        /// The library folders a Steam list names (libraryfolders.vdf): every "path" "X:\\Folder" of it, in the
        /// file's order, a doubled backslash read as one. An older list has its folders under numbers
        /// ("1" "X:\\Folder"): those are read too.
        /// </summary>
        public static List<string> Libraries(string vdf)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(vdf)) return found;
            var words = Quoted(vdf);
            for (var i = 0; i + 1 < words.Count; i++)
            {
                var key = words[i];
                var numbered = key.Length > 0 && key.Length <= 3 && IsNumber(key);
                if (!string.Equals(key, "path", StringComparison.OrdinalIgnoreCase) && !numbered) continue;
                var value = words[i + 1].Replace("\\\\", "\\").Trim();
                // (a numbered key of the newer form is followed by a block, not by a folder: its next word is "path")
                if (value.Length < 3 || !LooksLikeFolder(value)) continue;
                found.Add(value);
                i++;
            }
            return found;
        }

        private static bool IsNumber(string text)
        {
            foreach (var c in text)
                if (c < '0' || c > '9') return false;
            return true;
        }

        private static bool LooksLikeFolder(string text)
        {
            return text.IndexOf('\\') >= 0 || text.IndexOf('/') >= 0 || (text.Length >= 2 && text[1] == ':');
        }

        // Every "..." of a text, in order, without its quotes (an escaped quote stays inside its word).
        private static List<string> Quoted(string text)
        {
            var words = new List<string>();
            var at = 0;
            while (at < text.Length)
            {
                var open = text.IndexOf('"', at);
                if (open < 0) break;
                var close = open + 1;
                while (close < text.Length && text[close] != '"')
                {
                    if (text[close] == '\\' && close + 1 < text.Length) close++;
                    close++;
                }
                if (close >= text.Length) break;
                words.Add(text.Substring(open + 1, close - open - 1));
                at = close + 1;
            }
            return words;
        }

        /// <summary>
        /// The places, in the order they are looked at. No found place is named twice; the config's folder is
        /// named as the config's and again where Steam's own knowledge leads to it, because the two are asked
        /// for apart: "what does Steam find, whatever the config says" must find an install that the config
        /// happens to name as well.
        /// <paramref name="config"/> is the config's folder as written (cleaned by the caller; empty: none),
        /// <paramref name="dd2Folder"/> Darkest Dungeon II's own folder, <paramref name="registrySteam"/> the
        /// folders the registry names as Steam's, <paramref name="programFiles"/> the Program Files folders,
        /// <paramref name="drives"/> the drives' roots ("C:\"), <paramref name="readText"/> reads a file
        /// (null when it is not there).
        /// </summary>
        public static List<Place> Places(string config, string dd2Folder, IEnumerable<string> registrySteam, IEnumerable<string> programFiles, IEnumerable<string> drives, Func<string, string> readText)
        {
            var places = new List<Place>();
            var seen = new HashSet<string>();
            void Add(string folder, string by)
            {
                if (string.IsNullOrEmpty(folder) || !seen.Add(Key(folder))) return;
                places.Add(new Place { Folder = folder, By = by });
            }
            string Read(string file)
            {
                try { return readText != null ? readText(file) : null; }
                catch (Exception) { return null; }
            }
            void Steam(string steam, string by, string byLibraries)
            {
                if (string.IsNullOrEmpty(steam)) return;
                steam = steam.Trim().Replace('/', '\\');
                Add(Join(steam, "steamapps", "common", Dd1Folder.SteamName), by);
                foreach (var list in new[] { Join(steam, "steamapps", "libraryfolders.vdf"), Join(steam, "config", "libraryfolders.vdf") })
                    foreach (var library in Libraries(Read(list)))
                        Add(Join(library, "steamapps", "common", Dd1Folder.SteamName), byLibraries);
            }

            // (not among the "seen": see above)
            if (!string.IsNullOrEmpty(config)) places.Add(new Place { Folder = config, By = ByConfig, ByHand = true });

            // Darkest Dungeon II lives in (library)/steamapps/common/(its folder)
            var common = Parent(dd2Folder);
            if (common != null)
            {
                Add(Join(common, Dd1Folder.SteamName), ByNeighbour);
                var steamapps = Parent(common);
                if (steamapps != null)
                    foreach (var library in Libraries(Read(Join(steamapps, "libraryfolders.vdf"))))
                        Add(Join(library, "steamapps", "common", Dd1Folder.SteamName), ByNeighbourLibraries);
            }

            if (registrySteam != null)
                foreach (var steam in registrySteam) Steam(steam, ByRegistry, ByRegistryLibraries);

            if (programFiles != null)
                foreach (var folder in programFiles)
                    if (!string.IsNullOrEmpty(folder)) Steam(Join(folder, "Steam"), ByProgramFiles, ByProgramFilesLibraries);

            if (drives != null)
                foreach (var drive in drives)
                {
                    if (string.IsNullOrEmpty(drive)) continue;
                    Add(Join(drive, "Steam", "steamapps", "common", Dd1Folder.SteamName), ByDrive);
                    Add(Join(drive, "SteamLibrary", "steamapps", "common", Dd1Folder.SteamName), ByDrive);
                }
            return places;
        }
    }
}
