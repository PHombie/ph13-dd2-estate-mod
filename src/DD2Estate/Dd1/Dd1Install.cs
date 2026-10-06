using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// The player's own Darkest Dungeon (1) install. The mod ships no DD1 files: estate and corridor art is read
    /// from here at runtime.
    /// </summary>
    internal static class Dd1Install
    {
        private const string Marker = DD2Estate.Core.Dd1Folder.Marker;
        private static string _root;
        private static bool _searched;

        /// <summary>How the folder in use was come by.</summary>
        public enum Origin { None, ByHand, Steam }

        /// <summary>
        /// Whether <see cref="Root"/> is a folder a player named ([Paths] DarkestDungeon1 of the config, or the
        /// main menu's question) or one the search through Steam's libraries found. The folder in use is settled
        /// once a run of the game: everything read from it is kept (pictures, fonts, words, layouts, rules,
        /// sound), so another folder named in the options is used from the next start on (<see cref="Next"/>).
        /// The one exception is a run in which none was found and nothing was read: see <see cref="Use"/>.
        /// </summary>
        public static Origin FoundBy { get; private set; }

        /// <summary>How the folder in use was come by, in a few words (Core/Dd1Search.cs); null when none is.</summary>
        public static string FoundHow { get; private set; }

        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        public static bool Found => Root != null;

        public static string Root
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    HideIfAsked();
                    _root = Search(true, out var origin, out var how, LastLooked);
                    FoundBy = origin;
                    FoundHow = how;
                    if (_root != null) Plugin.Log.LogInfo("Darkest Dungeon 1 found at " + _root + " (" + how + ")");
                    else Plugin.Log.LogWarning("Darkest Dungeon 1 not found" + (Hidden ? " (dev: it is being hidden)" : "") + ". The main menu's Estate entry asks for its folder; it is [Paths] DarkestDungeon1 in BepInEx/config/" + Plugin.Guid + ".cfg");
                }
                if (_root == null) NoteAsker();
                return _root;
            }
        }

        private static bool IsDd1(string dir)
        {
            try { return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, Marker)); }
            catch { return false; }
        }

        /// <summary>A place the search looked at, and whether a Darkest Dungeon was there.</summary>
        public sealed class Looked
        {
            public string Folder, By;
            public bool Is;
        }

        /// <summary>What the search that settled <see cref="Root"/> looked at, in its order, up to the place it found.</summary>
        public static readonly List<Looked> LastLooked = new List<Looked>();

        // The first folder that is a Darkest Dungeon: the config's own if asked for and right, else Steam's.
        private static string Search(bool withConfig, out Origin origin, out string how, List<Looked> looked = null)
        {
            origin = Origin.None;
            how = null;
            looked?.Clear();
            if (Hidden)
            {
                // (dev: nothing is found, but a folder named at the main menu while hidden is the config's folder as a player's would be)
                if (!withConfig || _namedWhileHidden == null || !IsDd1(_namedWhileHidden)) return null;
                looked?.Add(new Looked { Folder = _namedWhileHidden, By = DD2Estate.Core.Dd1Search.ByConfig, Is = true });
                origin = Origin.ByHand;
                how = DD2Estate.Core.Dd1Search.ByConfig;
                return _namedWhileHidden;
            }
            foreach (var place in Places())
            {
                if (place.ByHand && !withConfig) continue;
                var there = IsDd1(place.Folder);
                looked?.Add(new Looked { Folder = place.Folder, By = place.By, Is = there });
                if (!there) continue;
                try
                {
                    var full = Path.GetFullPath(place.Folder);
                    origin = place.ByHand ? Origin.ByHand : Origin.Steam;
                    how = place.By;
                    return full;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>The folder the search through Steam's libraries finds, whatever the config names; null when it finds none.</summary>
        public static string FromSteam() => Search(false, out _, out _);

        /// <summary>
        /// The folder the next start of the game will use, as the config stands now, and how it will have been
        /// come by. The same as <see cref="Root"/> and <see cref="FoundBy"/> until the config's folder is changed.
        /// </summary>
        public static string Next(out Origin origin) => Search(true, out origin, out _);

        /// <summary>The same, with how the folder is come by in words.</summary>
        public static string Next(out Origin origin, out string how) => Search(true, out origin, out how);

        /// <summary>
        /// A folder a player names, looked at (Core/Dd1Folder.cs): the install's full path when it is a Darkest
        /// Dungeon with the files the Estate reads, else null and <paramref name="problem"/> says in words what is wrong.
        /// </summary>
        public static string Check(string typed, out string problem)
        {
            var verdict = DD2Estate.Core.Dd1Folder.Check(typed, File.Exists, Directory.Exists);
            problem = verdict.Problem;
            if (!verdict.Ok) return null;
            try { return Path.GetFullPath(verdict.Folder); }
            catch (Exception e)
            {
                problem = "That folder's name cannot be read: " + e.Message;
                return null;
            }
        }

        // Where a Darkest Dungeon may lie, the config's folder first (Core/Dd1Search.cs has the order and why).
        private static List<DD2Estate.Core.Dd1Search.Place> Places()
        {
            string[] drives;
            try { drives = Directory.GetLogicalDrives(); }
            catch { drives = new string[0]; }
            return DD2Estate.Core.Dd1Search.Places(
                DD2Estate.Core.Dd1Folder.Clean(Plugin.Dd1Path.Value),
                Path.GetDirectoryName(Application.dataPath),
                RegistrySteam(),
                new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
                drives,
                file => File.Exists(file) ? File.ReadAllText(file) : null);
        }

        /// <summary>Dev bridge: every place the search would look at as things stand, each with whether a Darkest Dungeon is there (the search itself stops at the first).</summary>
        public static List<Looked> AllPlaces()
        {
            var all = new List<Looked>();
            foreach (var place in Places()) all.Add(new Looked { Folder = place.Folder, By = place.By, Is = IsDd1(place.Folder) });
            return all;
        }

        // ---- Steam's own folder, as Windows has it written down ------------------------------------------------
        // Steam writes where it is installed into the registry: the user's key holds "SteamPath" (with forward
        // slashes), the machine's "InstallPath". The plugin is built against a library without the registry's
        // classes; the game's own runtime has them, so they are asked for by name. On a system without a
        // registry there is simply nothing to read.

        private static readonly string[][] SteamKeys =
        {
            new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath" },
            new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath" },
            new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath" }
        };

        private static string[] _registrySteam;

        /// <summary>Dev bridge: what the registry was asked and whether it answered (the answers themselves are in the list of places).</summary>
        public static readonly List<string> RegistryAnswers = new List<string>();

        private static string[] RegistrySteam()
        {
            if (_registrySteam != null) return _registrySteam;
            var found = new List<string>();
            RegistryAnswers.Clear();
            try
            {
                var registry = Type.GetType("Microsoft.Win32.Registry, mscorlib") ?? Type.GetType("Microsoft.Win32.Registry");
                var get = registry?.GetMethod("GetValue", new[] { typeof(string), typeof(string), typeof(object) });
                if (get == null) RegistryAnswers.Add("this runtime has no registry to ask");
                else
                    foreach (var key in SteamKeys)
                    {
                        string value;
                        try { value = get.Invoke(null, new object[] { key[0], key[1], null }) as string; }
                        catch (Exception e)
                        {
                            RegistryAnswers.Add(key[0] + " " + key[1] + ": " + (e.InnerException ?? e).Message);
                            continue;
                        }
                        RegistryAnswers.Add(key[0] + " " + key[1] + ": " + (string.IsNullOrEmpty(value) ? "nothing" : "a folder"));
                        if (!string.IsNullOrEmpty(value)) found.Add(value.Replace('/', '\\'));
                    }
            }
            catch (Exception e) { RegistryAnswers.Add("the registry could not be asked: " + e.Message); }
            return _registrySteam = found.ToArray();
        }

        // ---- a folder named when none had been found -----------------------------------------------------------

        /// <summary>
        /// True when a folder can be taken into use in this run of the game: none is in use, no Estate session
        /// runs or has run without one, and whoever has asked for the install so far (and got "not there")
        /// either keeps no answer or forgets it (Dd1/Dd1Forget.cs). Else <paramref name="why"/> says what stands
        /// in the way, and the folder is one for the next start.
        /// </summary>
        public static bool CanUseNow(out string why)
        {
            why = null;
            if (_searched && _root != null) why = "another Darkest Dungeon folder is in use in this run of the game";
            else if (DD2Estate.Dd2.EstateSession.Active || DD2Estate.Dd2.EstateSession.Starting) why = "an Estate session is running";
            else if (SessionsWithout > 0) why = "the Estate has been entered without Darkest Dungeon in this run of the game";
            else
            {
                var unknown = Dd1Forget.Unknown(AskerTypes);
                if (unknown.Count > 0) why = "Darkest Dungeon was asked for by parts of the mod that keep their answer: " + string.Join(", ", unknown);
            }
            return why == null;
        }

        /// <summary>How many Estate sessions were entered in this run with no Darkest Dungeon found: their screens were built without it and much is kept.</summary>
        public static int SessionsWithout { get; private set; }

        /// <summary>An Estate session begins: counted when it begins without Darkest Dungeon.</summary>
        public static void SessionBegins()
        {
            if (!Found) SessionsWithout++;
        }

        /// <summary>
        /// A folder a player has just named (checked: <see cref="Check"/>), or one a second search has found, is
        /// the folder in use from now on. Only when <see cref="CanUseNow"/>: every "not there" that was kept is
        /// forgotten, and whatever is read from here on is read from the folder.
        /// </summary>
        public static bool Use(string folder, Origin by, string how, out string why)
        {
            if (!CanUseNow(out why)) return false;
            if (!IsDd1(folder))
            {
                why = "that folder is not a Darkest Dungeon install";
                return false;
            }
            Hidden = false;
            _searched = true;
            _root = folder;
            FoundBy = by;
            FoundHow = how;
            LastLooked.Clear();
            LastLooked.Add(new Looked { Folder = folder, By = how, Is = true });
            var asked = AskerTypes.Count;
            Askers.Clear();
            AskerTypes.Clear();
            ForgetPictures();
            Told.Clear();
            Dd1Forget.All();
            Plugin.Log.LogInfo("Darkest Dungeon 1 is now read from " + folder + " (" + how + "); " + asked + " part(s) of the mod had asked for it before and ask again from here on");
            return true;
        }

        // ---- who asked while there was none ---------------------------------------------------------------------
        // Each asker is a chain of the mod's classes, the one that asked first: "Dd1Fonts < UiKit < HamletScreen".
        // It is what CanUseNow goes by, and what the dev bridge shows (dd1.state).

        private static readonly Dictionary<string, int> Askers = new Dictionary<string, int>();
        private static readonly HashSet<string> AskerTypes = new HashSet<string>();
        private const int MaxAskers = 400;

        /// <summary>Dev bridge: off, an ask is not written down (the bridge's own questions).</summary>
        public static bool NoteAskers = true;

        public static IEnumerable<KeyValuePair<string, int>> AskedWhileMissing => Askers;

        public static IEnumerable<string> AskerClasses => AskerTypes;

        private static void NoteAsker()
        {
            if (!NoteAskers) return;
            try
            {
                var trace = new System.Diagnostics.StackTrace(2, false);
                var chain = new List<string>();
                var mine = typeof(Dd1Install).Assembly;
                for (var i = 0; i < trace.FrameCount; i++)
                {
                    var type = trace.GetFrame(i)?.GetMethod()?.DeclaringType;
                    if (type == null || type.Assembly != mine) continue;
                    while (type.DeclaringType != null) type = type.DeclaringType;
                    if (type == typeof(Dd1Install)) continue;
                    if (chain.Count > 0 && chain[chain.Count - 1] == type.Name) continue;
                    AskerTypes.Add(type.Name);
                    if (chain.Count < 10) chain.Add(type.Name);
                }
                var key = chain.Count == 0 ? "(not the mod's own code)" : string.Join(" < ", chain);
                if (Askers.TryGetValue(key, out var times)) Askers[key] = times + 1;
                else if (Askers.Count < MaxAskers) Askers[key] = 1;
            }
            catch (Exception) { }
        }

        // ---- dev: as if there were none --------------------------------------------------------------------------
        // To see what a player without Darkest Dungeon sees on a PC that has it. Hidden: the search finds nothing,
        // whatever the config and Steam say, until it is told otherwise or a folder is named at the main menu.
        // The config file is not touched. For a whole run without it (a player's first start) the switch must be
        // on before anything asks: "next" leaves a mark beside the config that the next start of the game takes
        // (and removes), so the start after that is as always. Only with the dev bridge on.

        /// <summary>Dev: true while the mod behaves as if no Darkest Dungeon were to be found.</summary>
        public static bool Hidden { get; private set; }

        private static bool _hideLooked;

        private static string HideMark => Path.Combine(BepInEx.Paths.ConfigPath, Plugin.Guid + ".hide-dd1-once");

        public static bool HideAtNextStart
        {
            get
            {
                try { return File.Exists(HideMark); }
                catch { return false; }
            }
        }

        /// <summary>
        /// The mark for this start is taken, if there is one: at start-up (the dev bridge's module calls it), so
        /// that a game closed before anything asked for Darkest Dungeon does not leave the mark to the start
        /// after; and again before the first search, should start-up not have come by here.
        /// </summary>
        internal static void HideIfAsked()
        {
            if (_hideLooked) return;
            _hideLooked = true;
            if (Plugin.BridgePort == null || Plugin.BridgePort.Value <= 0) return;
            try
            {
                if (!File.Exists(HideMark)) return;
                File.Delete(HideMark);
                Hidden = true;
                Plugin.Log.LogWarning("Dev: Darkest Dungeon 1 is hidden for this run of the game (dd1.hide); the next start finds it as always");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dev: the mark that hides Darkest Dungeon 1 could not be read: " + e.Message); }
        }

        /// <summary>
        /// Dev bridge. <paramref name="on"/>: hidden or not from now on (hiding it in a run that has read from it
        /// leaves what was read; showing it again is a new search, taken into use only when <see cref="CanUseNow"/>).
        /// <paramref name="next"/>: the mark for the next start, set or removed. Returns what was done, in words.
        /// </summary>
        public static string Hide(bool? on, bool? next)
        {
            var done = new List<string>();
            if (next != null)
            {
                try
                {
                    if (next.Value) File.WriteAllText(HideMark, "Dev: the next start of the game behaves as if no Darkest Dungeon (1) were installed, and removes this file.");
                    else if (File.Exists(HideMark)) File.Delete(HideMark);
                    done.Add(next.Value ? "the next start of the game finds no Darkest Dungeon" : "the next start of the game is as always");
                }
                catch (Exception e) { done.Add("the mark for the next start could not be " + (next.Value ? "written" : "removed") + ": " + e.Message); }
            }
            if (on == true && !Hidden)
            {
                // (the folder this run has read from is kept aside: it is the one that comes back)
                _hiddenRoot = Root;
                _hiddenBy = FoundBy;
                _hiddenHow = FoundHow;
                Hidden = true;
                _root = null;
                FoundBy = Origin.None;
                FoundHow = null;
                LastLooked.Clear();
                done.Add(_hiddenRoot != null ? "hidden from now on (what this run has read from it stays read: for a clean run use next=true and restart the game)" : "hidden from now on");
            }
            else if (on == false && Hidden)
            {
                if (_hiddenRoot != null)
                {
                    Hidden = false;
                    _root = _hiddenRoot;
                    FoundBy = _hiddenBy;
                    FoundHow = _hiddenHow;
                    _hiddenRoot = null;
                    Askers.Clear();
                    AskerTypes.Clear();
                    done.Add("no longer hidden; in use again: " + _root + " (what was asked for meanwhile may keep its \"not there\" until the game is restarted)");
                }
                else
                {
                    Hidden = false;
                    var found = Search(true, out var origin, out var how, LastLooked);
                    if (found == null) done.Add("no longer hidden; the search finds none");
                    else if (!Use(found, origin, how, out var why))
                    {
                        Hidden = true;
                        done.Add("still hidden: it cannot be taken into use in this run (" + why + "). Restart the game.");
                    }
                    else done.Add("no longer hidden; in use: " + found);
                }
            }
            return done.Count == 0 ? "nothing to do" : string.Join("; ", done);
        }

        private static string _hiddenRoot, _hiddenHow;
        private static Origin _hiddenBy;
        private static string _namedWhileHidden;

        /// <summary>
        /// A folder has been named at the main menu and written to the config. While the install is hidden (dev)
        /// the config is not looked at; this folder is, so that what follows is what a player would see.
        /// </summary>
        public static void Named(string folder)
        {
            if (Hidden) _namedWhileHidden = folder;
        }

        public static string PathOf(string relative)
        {
            if (relative == null) return null;
            return OverrideOf(relative) ?? (Root == null ? null : Path.Combine(Root, relative));
        }

        // ---- pictures that take the place of DD1's ---------------------------------------------------------
        // A PNG at <overrides>/<its path in the DD1 install> is read instead of DD1's file of that path: better
        // art for a dungeon's walls, say. Nothing but PNGs: DD1's rules, layouts and words stay DD1's. Such a picture
        // may be larger than the one it stands for; whoever sizes a thing by its picture's pixels asks Scale().

        private static string _overrides;
        private static bool _overridesSearched;
        private static readonly Dictionary<string, float> Scales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Told = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The folder of replacement pictures ([Paths] ArtOverrides, else "assets" beside the plugin); null when there is none.</summary>
        public static string OverrideRoot
        {
            get
            {
                if (_overridesSearched) return _overrides;
                _overridesSearched = true;
                try
                {
                    var asked = Plugin.ArtOverrides != null ? Plugin.ArtOverrides.Value : null;
                    var folder = !string.IsNullOrEmpty(asked) ? asked : Path.Combine(Path.GetDirectoryName(typeof(Dd1Install).Assembly.Location) ?? "", "assets");
                    if (Directory.Exists(folder))
                    {
                        _overrides = Path.GetFullPath(folder);
                        Plugin.Log.LogInfo("Pictures in place of DD1's are read from " + _overrides);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("The folder of replacement pictures could not be looked for: " + e.Message);
                }
                return _overrides;
            }
        }

        /// <summary>Dev bridge: off, DD1's own pictures are read again (to look at the two side by side).</summary>
        public static bool OverridesOn = true;

        /// <summary>Dev bridge: the pictures that have been replaced so far, by their DD1 paths.</summary>
        public static IEnumerable<string> ReplacedSoFar => Told;

        /// <summary>
        /// Dev bridge: what has been read is forgotten, so that the next reader gets the picture as the switch
        /// stands now. The textures themselves are left alone: something on screen may still draw them.
        /// </summary>
        public static void ForgetPictures()
        {
            Textures.Clear();
            Sprites.Clear();
            Scales.Clear();
        }

        private static string OverrideOf(string relative)
        {
            if (!OverridesOn || OverrideRoot == null || !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
            var path = Path.Combine(OverrideRoot, relative);
            if (!File.Exists(path)) return null;
            if (Told.Add(relative)) Plugin.Log.LogInfo("Picture replaced: " + relative + " (x" + Scale(relative).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " of DD1's)");
            return path;
        }

        /// <summary>True when a picture of the mod's stands in for this DD1 picture.</summary>
        public static bool Replaced(string relative) => relative != null && OverrideOf(relative) != null;

        /// <summary>
        /// How many pixels of the picture that is read stand for one pixel of DD1's own (1 when it is DD1's own, or
        /// DD1 has no such picture): a wall 1200 wide in place of DD1's 720 answers 1.667. What DD1 counts in its
        /// art's pixels (a parallax layer's repeat, the clear ring at a wall's edge) is multiplied by it.
        /// </summary>
        public static float Scale(string relative)
        {
            if (relative == null || !OverridesOn || OverrideRoot == null) return 1f;
            if (Scales.TryGetValue(relative, out var known)) return known;
            var scale = 1f;
            try
            {
                var own = Path.Combine(OverrideRoot, relative);
                var theirs = Root != null ? Path.Combine(Root, relative) : null;
                if (File.Exists(own) && theirs != null && File.Exists(theirs))
                {
                    var a = PngWidth(own);
                    var b = PngWidth(theirs);
                    if (a > 0 && b > 0) scale = (float)a / b;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("A replacement picture's size could not be read (" + relative + "): " + e.Message);
            }
            return Scales[relative] = scale;
        }

        // A PNG's width stands in its header: bytes 16 to 19, big end first.
        private static int PngWidth(string path)
        {
            using (var file = File.OpenRead(path))
            {
                var head = new byte[24];
                if (file.Read(head, 0, 24) < 24 || head[1] != 'P' || head[2] != 'N' || head[3] != 'G') return 0;
                return (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            }
        }

        /// <summary>DD1 keeps the Darkest Dungeon's art per quest (quest_1..quest_4) instead of in the dungeon folder.</summary>
        public static int DarkestQuestArt = 1;

        public const string DarkestFolder = "dungeons/darkestdungeon/";

        /// <summary>The path an art file really lives at: itself, or its per-quest copy for the Darkest Dungeon.</summary>
        public static string ArtPath(string relative)
        {
            if (Root == null || relative == null) return relative;
            if (File.Exists(Path.Combine(Root, relative))) return relative;
            if (relative.StartsWith(DarkestFolder, StringComparison.OrdinalIgnoreCase) && relative.IndexOf('/', DarkestFolder.Length) < 0)
            {
                var quest = DarkestFolder + "quest_" + DarkestQuestArt + "/" + relative.Substring(DarkestFolder.Length);
                if (File.Exists(Path.Combine(Root, quest))) return quest;
            }
            return relative;
        }

        public static bool Exists(string relative)
        {
            var path = PathOf(relative);
            return path != null && File.Exists(path);
        }

        public static string ReadText(string relative)
        {
            var path = PathOf(relative);
            return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public static byte[] ReadBytes(string relative)
        {
            var path = PathOf(relative);
            return path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>Loads a PNG from the DD1 install (cached). Null if DD1 or the file is missing.</summary>
        public static Texture2D Texture(string relative)
        {
            relative = ArtPath(relative);
            if (Textures.TryGetValue(relative, out var cached) && cached != null) return cached;
            var tex = LoadTexture(relative, false, false);
            if (tex != null) Textures[relative] = tex;
            return tex;
        }

        /// <summary>
        /// Loads a PNG outside the cache: the caller owns the texture. <paramref name="readable"/> keeps the
        /// pixels in memory for GetPixels32 (release them with Apply(false, true) when done).
        /// </summary>
        public static Texture2D LoadTexture(string relative, bool readable, bool mipmaps)
        {
            var path = PathOf(relative);
            if (path == null || !File.Exists(path))
            {
                Plugin.Log.LogWarning("DD1 file missing: " + relative);
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps)
            {
                name = relative,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var bytes = File.ReadAllBytes(path);
            var bleed = BleedEdges && HasAlpha(bytes);
            if (!tex.LoadImage(bytes, !readable && !bleed))
            {
                Plugin.Log.LogWarning("DD1 image failed to decode: " + relative);
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            if (bleed)
            {
                try
                {
                    Bleed(tex);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("DD1 image " + relative + ": its clear pixels could not be given the art's colour: " + e.Message);
                }
                tex.Apply(mipmaps, !readable);
            }
            return tex;
        }

        /// <summary>
        /// DD1's pictures are not premultiplied and keep whatever colour their painter left under the fully clear
        /// pixels (white in a fifth of the Warrens' foreground strip). The game's sprite shader filters the
        /// colours as they are, so that colour bleeds into every soft edge: a pale line round each shape. On: the
        /// clear pixels next to the art take the art's colour (what Unity's importer does for "alpha is
        /// transparency"), the others go black.
        /// </summary>
        public static bool BleedEdges = true;

        // A PNG's colour type (the byte after the header's width, height and bit depth): 4 grey with alpha,
        // 6 RGB with alpha, 3 a palette (which may carry clear entries).
        private static bool HasAlpha(byte[] png)
        {
            if (png == null || png.Length < 26 || png[0] != 0x89 || png[1] != 'P') return false;
            var type = png[25];
            return type == 4 || type == 6 || type == 3;
        }

        private static void Bleed(Texture2D tex)
        {
            if (tex.format != TextureFormat.RGBA32 && tex.format != TextureFormat.ARGB32) return;
            var w = tex.width;
            var h = tex.height;
            var pixels = tex.GetPixels32();
            // Rows are independent: only clear pixels are written and only pixels with some alpha are read.
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    var i = row + x;
                    if (pixels[i].a != 0) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            var p = pixels[yy * w + xx];
                            if (p.a == 0) continue;
                            // the more solid a neighbour, the more its colour counts
                            r += p.r * p.a;
                            g += p.g * p.a;
                            b += p.b * p.a;
                            n += p.a;
                        }
                    }
                    pixels[i] = n == 0 ? new Color32(0, 0, 0, 0) : new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                }
            });
            tex.SetPixels32(pixels);
        }

        /// <summary>
        /// One picture out of a DD1 sprite sheet, upright, as a texture of its own. The sheet's atlas file lies beside
        /// it (sheet.png -> sheet.atlas, Spine's text format): "xy" is the picture's top left corner on the sheet,
        /// "size" its size upright, and "rotate: true" says it lies on the sheet turned a quarter to the left (seen
        /// on the estate bar's scroll and chest), so it is turned back to the right here.
        /// </summary>
        private static Texture2D Region(string sheet, string name)
        {
            var bytes = ReadBytes(sheet.Substring(0, sheet.Length - 4) + ".atlas");
            if (bytes == null) return null;
            var lines = System.Text.Encoding.UTF8.GetString(bytes).Replace("\r", "").Split('\n');
            int x = 0, y = 0, w = 0, h = 0;
            bool rotated = false, found = false;
            for (var i = 0; i < lines.Length && !found; i++)
            {
                if (lines[i] != name) continue;
                found = true;
                for (var j = i + 1; j < lines.Length && lines[j].StartsWith(" "); j++)
                {
                    var colon = lines[j].IndexOf(':');
                    if (colon < 0) continue;
                    var key = lines[j].Substring(0, colon).Trim();
                    var values = lines[j].Substring(colon + 1).Split(',');
                    if (key == "rotate") rotated = values[0].Trim() == "true";
                    else if (key == "xy" && values.Length == 2) { x = int.Parse(values[0].Trim()); y = int.Parse(values[1].Trim()); }
                    else if (key == "size" && values.Length == 2) { w = int.Parse(values[0].Trim()); h = int.Parse(values[1].Trim()); }
                }
            }
            if (!found || w <= 0 || h <= 0)
            {
                Plugin.Log.LogWarning("DD1 sprite sheet " + sheet + " has no picture named " + name);
                return null;
            }
            var page = LoadTexture(sheet, true, false);
            if (page == null) return null;
            var from = page.GetPixels32();
            int pageWidth = page.width, pageHeight = page.height;
            UnityEngine.Object.Destroy(page);
            var to = new Color32[w * h];
            for (var v = 0; v < h; v++)
            {
                for (var u = 0; u < w; u++)
                {
                    // from the top left corner on both; a turned picture takes h columns and w rows of the sheet
                    var px = rotated ? x + v : x + u;
                    var py = rotated ? y + (w - 1 - u) : y + v;
                    if (px < 0 || py < 0 || px >= pageWidth || py >= pageHeight) continue;
                    to[(h - 1 - v) * w + u] = from[(pageHeight - 1 - py) * pageWidth + px];
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = sheet + "#" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels32(to);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>A whole DD1 picture, or one picture out of a sprite sheet: "sheet.png#name" (see <see cref="Region"/>).</summary>
        public static Sprite Sprite(string relative)
        {
            if (Sprites.TryGetValue(relative, out var cached) && cached != null) return cached;
            var hash = relative.IndexOf('#');
            var tex = hash > 0 ? Region(relative.Substring(0, hash), relative.Substring(hash + 1)) : Texture(relative);
            if (tex == null) return null;
            var sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = relative;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Sprites[relative] = sprite;
            return sprite;
        }
    }
}
