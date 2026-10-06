using System;
using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>
    /// A folder a player names as their Darkest Dungeon (1) install, looked at before it is taken: the Estate
    /// reads its hamlet, dungeons, rules, words, fonts and sound from there, and a folder that is not one must
    /// not get into the config. What is typed is forgiven a little (quotes of "Copy as path", a slash at the
    /// end, the folder of the game's program inside the install, the folder the install lies in) and what is
    /// wrong is said in words.
    /// </summary>
    public static class Dd1Folder
    {
        /// <summary>The file that says "this is a Darkest Dungeon install": the hamlet's backdrop.</summary>
        public const string Marker = "campaign/town/town_bg.png";

        /// <summary>Files of the base game the Estate cannot do without (each read at start-up or for the first screen).</summary>
        public static readonly string[] NeededFiles =
        {
            "campaign/town/town.layout.darkest", "campaign/estate/estate.json", "shared/rules.json",
            "colours/base.colours.darkest", "fonts/fonts.darkest", "localization/miscellaneous.string_table.xml"
        };

        /// <summary>Whole folders it reads from.</summary>
        public static readonly string[] NeededFolders = { "dungeons", "heroes", "audio" };

        /// <summary>The folders DD1 keeps its program in, one for a platform, inside the install.</summary>
        private static readonly string[] ProgramFolders = { "_windows", "_windowsnosteam", "_osx", "_osxnosteam", "_linux", "_linuxnosteam" };

        /// <summary>The name of the install's folder in a Steam library.</summary>
        public const string SteamName = "DarkestDungeon";

        public sealed class Verdict
        {
            /// <summary>True: <see cref="Folder"/> is a Darkest Dungeon install with what the Estate reads.</summary>
            public bool Ok;
            /// <summary>The install's folder (which may lie beside, above or under what was typed); null when none was found.</summary>
            public string Folder;
            /// <summary>What is wrong, as a sentence for the player; null when nothing is.</summary>
            public string Problem;
        }

        /// <summary>What was typed without its wrapping: blanks, quotes, a separator at the end.</summary>
        public static string Clean(string typed)
        {
            var text = (typed ?? "").Trim();
            while (text.Length >= 2 && (text[0] == '"' || text[0] == '\'') && text[text.Length - 1] == text[0]) text = text.Substring(1, text.Length - 2).Trim();
            text = text.Trim('"').Trim();
            // "C:\" keeps its separator: without it the path means the drive's current folder
            while (text.Length > 3 && (text[text.Length - 1] == '\\' || text[text.Length - 1] == '/')) text = text.Substring(0, text.Length - 1);
            return text;
        }

        private static string Join(string folder, string relative)
        {
            if (folder.Length == 0) return relative;
            var last = folder[folder.Length - 1];
            return last == '\\' || last == '/' ? folder + relative : folder + "/" + relative;
        }

        private static string Parent(string path)
        {
            var at = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return at <= 0 ? null : path.Substring(0, at);
        }

        private static string Name(string path)
        {
            var at = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return at < 0 ? path : path.Substring(at + 1);
        }

        /// <summary>
        /// The folders that may be meant by what was typed, the likeliest first: the folder itself; the install
        /// above a program folder (..._windows) or above the program's file; the install inside a Steam
        /// library's "common" folder.
        /// </summary>
        public static IEnumerable<string> Meant(string typed)
        {
            var folder = Clean(typed);
            if (folder.Length == 0) yield break;
            yield return folder;
            var up = Parent(folder);
            if (up != null && Array.Exists(ProgramFolders, name => string.Equals(name, Name(folder), StringComparison.OrdinalIgnoreCase))) yield return up;
            if (up != null && folder.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var above = Parent(up);
                if (above != null) yield return above;
            }
            yield return Join(folder, SteamName);
        }

        /// <summary>
        /// Looks at a typed folder. <paramref name="fileExists"/> and <paramref name="folderExists"/> are the
        /// file system (System.IO.File.Exists, Directory.Exists).
        /// </summary>
        public static Verdict Check(string typed, Func<string, bool> fileExists, Func<string, bool> folderExists)
        {
            var asked = Clean(typed);
            if (asked.Length == 0) return new Verdict { Problem = "No folder was given." };
            foreach (var folder in Meant(typed))
            {
                if (!Exists(fileExists, Join(folder, Marker))) continue;
                var missing = new List<string>();
                foreach (var file in NeededFiles)
                    if (!Exists(fileExists, Join(folder, file))) missing.Add(file);
                foreach (var dir in NeededFolders)
                    if (!Exists(folderExists, Join(folder, dir))) missing.Add(dir + "/");
                if (missing.Count == 0) return new Verdict { Ok = true, Folder = folder };
                var named = string.Join(", ", missing.GetRange(0, Math.Min(3, missing.Count)));
                if (missing.Count > 3) named += " and " + (missing.Count - 3) + " more";
                return new Verdict { Folder = folder, Problem = "Darkest Dungeon is there, but files the Estate reads are missing: " + named + ". Verify the game's files in Steam." };
            }
            if (!Exists(folderExists, asked))
                return new Verdict { Problem = Exists(fileExists, asked) ? "That is a file, not a folder: " + asked : "There is no such folder: " + asked };
            return new Verdict { Problem = "That folder is not a Darkest Dungeon install (it has no " + Marker + "). Name the folder that holds the game's \"campaign\", \"dungeons\" and \"heroes\" folders." };
        }

        // a path the file system cannot even look at (a stray character) is a path that is not there
        private static bool Exists(Func<string, bool> ask, string path)
        {
            try { return ask != null && ask(path); }
            catch (Exception) { return false; }
        }
    }
}
