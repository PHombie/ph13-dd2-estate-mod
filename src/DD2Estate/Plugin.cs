using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using HarmonyLib;
using UnityEngine;

namespace DD2Estate
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "ph13.dd2.estate";
        public const string Name = "DD2 Estate";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static Harmony Harmony;
        internal static EstateHost Host;

        /// <summary>The plugin's config file: a module binds its own entries with it in its Register (not here).</summary>
        internal static ConfigFile Settings;
        internal static ConfigEntry<string> Dd1Path;
        internal static ConfigEntry<string> ArtOverrides;
        internal static ConfigEntry<bool> DevHotkeys;
        internal static ConfigEntry<int> BridgePort;
        internal static ConfigEntry<int> ProfileGuid;
        internal static ConfigEntry<int> PreviousModProfile;

        private void Awake()
        {
            Log = Logger;
            Settings = Config;

            Dd1Path = Config.Bind("Paths", "DarkestDungeon1", "",
                "Folder of your own Darkest Dungeon (1) install. Empty = find it through Steam. The estate art, corridors and audio are read from there at runtime.");
            ArtOverrides = Config.Bind("Paths", "ArtOverrides", "",
                "Folder of pictures that take the place of Darkest Dungeon (1)'s own: a PNG at <folder>/<the picture's path in the DD1 install> (dungeons/warrens/warrens.corridor_wall.01.png) is shown instead of DD1's. It may be larger than DD1's: it is drawn at DD1's size. Empty = the 'assets' folder beside the plugin.");
            DevHotkeys = Config.Bind("Dev", "Hotkeys", false,
                "Development only: keys for testing inside the Estate. Numpad 5 wins the fight on screen; Numpad + and Numpad - step the game's speed up and down (x0.5, x1, x2, x3, x4).");
            BridgePort = Config.Bind("Dev", "BridgePort", 0,
                "Development only: TCP port on 127.0.0.1 for the test bridge. 0 = off.");

            ProfileGuid = Config.Bind("State", "EstateProfileGuid", 0,
                "Id of the mod profile the Estate created for itself. Managed by the mod.");

            PreviousModProfile = Config.Bind("State", "PreviousModProfile", 0,
                "Mod profile that was selected before the Estate switched to its own; restored on leaving. Managed by the mod.");

            // These three follow their entries while the game runs (the Estate tab of the game's options changes them).
            var dd1Audio = Config.Bind("Audio", "Dd1Audio", true, "Play Darkest Dungeon (1)'s own narration voice, town music and ambience from your DD1 install.");
            EstateAudio.Enabled = dd1Audio.Value;
            dd1Audio.SettingChanged += (s, e) => EstateAudio.Enabled = dd1Audio.Value;
            var dd1Level = Config.Bind("Audio", "Dd1Level", 1f, "Volume of DD1's sound against DD2's own (0..1), on top of the game's sliders.");
            EstateAudio.Level = dd1Level.Value;
            dd1Level.SettingChanged += (s, e) => EstateAudio.Level = dd1Level.Value;

            // Off unless asked for: the owner does not want DD1's in-game guides in the mod (2026-10-05). The key is
            // not "Tutorials", which an earlier build wrote into the config file as true.
            var tutorials = Config.Bind("Rules", "TutorialPopups", false, "DD1's tutorial pop-up messages (its option \"Tutorials\"), each once an estate. Off by default.");
            DD2Estate.Estate.Tutorials.Option = tutorials.Value;
            tutorials.SettingChanged += (s, e) => DD2Estate.Estate.Tutorials.Option = tutorials.Value;
            DD2Estate.Estate.TownTime.Enabled = Config.Bind("Rules", "TownTimeOfDay", true, "DD1's hamlet by time of day: the town is regraded as a visit goes on (morning, afternoon, evening, night).").Value;

            EstateMode.Register();
            EstatePersistence.SerializeEstate = () => DD2Estate.Estate.EstateState.Current.ToJson();
            EstatePersistence.DeserializeEstate = DD2Estate.Estate.EstateState.LoadFrom;
            DD2Estate.Estate.EstateModules.RegisterAll();
            Harmony = new Harmony(Guid);
            PatchAll();

            // DD2 destroys the BepInEx manager object when it leaves the splash scene, so nothing may live on
            // this component. The mod runs on its own hidden host object instead.
            var go = new GameObject("DD2Estate.Host") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            Host = go.AddComponent<EstateHost>();

            DevCommands.Register();
            AgentBridge.Start(BridgePort.Value);
            Log.LogInfo($"{Name} {Version} loaded (Unity {Application.unityVersion}, game {Application.version})");
        }

        // One class at a time: PatchAll stops at the first failure, and a game update that renames one target
        // should cost one feature, not the whole mod.
        private static void PatchAll()
        {
            foreach (var type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { Harmony.CreateClassProcessor(type).Patch(); }
                catch (System.Exception e) { Log.LogError("patch " + type.Name + " failed: " + e.Message); }
            }
        }
    }

    /// <summary>The mod's own persistent MonoBehaviour: survives scene changes, drives per-frame work.</summary>
    internal class EstateHost : MonoBehaviour
    {
        private void Update()
        {
            if (AgentBridge.Enabled) AgentBridge.Pump();
            try { EstateSession.Tick(); }
            catch (System.Exception e) { Plugin.Log.LogError("EstateSession.Tick: " + e); }
        }

        private void OnApplicationQuit()
        {
            AgentBridge.Stop();
        }
    }
}
