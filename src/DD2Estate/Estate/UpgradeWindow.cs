using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.Dev;
using DD2Estate.UI;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A window for the upgrades of a building whose own screen cannot carry the upgrade pane. Two uses:
    ///
    /// - On its own (<see cref="Open(string)"/>): the building's DD1 backdrop with the pane open and nothing else.
    /// - As a lobby (<see cref="Open(string, string, Action)"/>): keeper, name, the "Upgrades" button and one
    ///   more button that leaves for the building's real screen (for a building shown by a native DD2 screen).
    ///
    /// Every building has its own screen now and shows the pane inside it behind an "Upgrades" button; the
    /// window is kept for a building added before its screen exists.
    ///
    /// Also the place where the upgrade system plugs into the estate: when steps are built the hamlet shows
    /// the building's better art, the way DD1 does (level_thresholds in campaign/town/town.layout.darkest:
    /// the share of a building's steps that are built picks level 1, 2 or 3; the Ancestor's statue, which has
    /// no steps of its own, goes by the whole town's: see <see cref="Share"/>). The hamlet is told whenever
    /// the levels may have changed: a step built, an estate loaded or begun, a building opened.
    /// </summary>
    [EstateModule]
    internal static class UpgradeWindow
    {
        private const string SectionKey = "upgrades";
        // Pixels of the backdrop (1395x776): the body's side, where the other screens have their lists.
        private static readonly Vector2 EnterPos = new Vector2(860f, 330f);
        private static readonly Vector2 EnterSize = new Vector2(300f, 64f);
        private static DarkestFile _townLayout;
        private static bool _townLayoutRead;

        private static void Register()
        {
            // Nothing of its own to save (levels are in EstateState.Buildings); the section is the signal
            // that another estate's levels are in force.
            EstateState.RegisterSection(SectionKey, () => new JObject { ["format"] = 1 }, token => OnEstateReplaced(), OnEstateReplaced);
            UpgradeRules.Changed += SyncHamlet;

            // For a script that looks at a building's screen the moment it has asked for it: {"on":false} lets the
            // windows stand, and go, at once instead of growing and shrinking; without "on" it only tells.
            AgentBridge.Register("window.animate", o =>
            {
                if (o["on"] != null) RosterWindow.Animate = (bool)o["on"];
                return RosterWindow.Animate;
            });
            // What a click on a button of the open window's quick navigation does: {"building":"guild"}.
            AgentBridge.Register("window.go", o => RosterWindow.Go((string)o["building"]));
            // Which buildings carry DD1's exclamation mark now (a step can be built that the player has not looked at).
            AgentBridge.Register("upgrade.alerts", o =>
            {
                var news = new JObject();
                foreach (var building in UpgradeRules.BuildingIds) news[building] = UpgradeAlerts.Has(building);
                return news;
            });
            BuildingLocks.Changed += SyncHamlet;
            // The town's look: for every building the share of upgrades its exterior goes by, the level that share
            // picks by DD1's thresholds, whether it is shut, and which of DD1's skeletons the hamlet shows for it.
            AgentBridge.Register("upgrade.town", o =>
            {
                var town = new JObject();
                foreach (var building in HamletScene.BuildingIds)
                    town[building] = new JObject
                    {
                        ["share"] = Math.Round(Share(building), 3), ["artLevel"] = ArtLevel(building), ["locked"] = BuildingLocks.Locked(building),
                        ["hamlet"] = JObject.FromObject(HamletScene.Describe(building))
                    };
                return town;
            });
        }

        /// <summary>The building's upgrades and nothing else.</summary>
        public static void Open(string building)
        {
            if (UpgradeRules.TreesOf(building).Count == 0)
            {
                Plugin.Log.LogInfo("Hamlet: the " + building + " has no upgrades to show");
                return;
            }
            RosterWindow.Open(building, ActivityText.Building(building), UpgradeUi.BuildingsDir + building + "/" + building, upgradesOnly: true);
        }

        /// <summary>
        /// The building's lobby: its upgrades behind the "Upgrades" button, and a button labelled
        /// <paramref name="enterLabel"/> that closes the window and calls <paramref name="enter"/> (the
        /// building's own screen).
        /// </summary>
        public static void Open(string building, string enterLabel, Action enter)
        {
            var window = RosterWindow.Open(building, ActivityText.Building(building), UpgradeUi.BuildingsDir + building + "/" + building);
            if (window == null)
            {
                enter?.Invoke();
                return;
            }
            var button = UiKit.Button("Enter", window.Frame, enterLabel, 28f, () =>
            {
                RosterWindow.Close();
                try { enter?.Invoke(); }
                catch (Exception e) { Plugin.Log.LogError("Hamlet: the " + building + " failed to open: " + e); }
            });
            ((RectTransform)button.transform).PlaceTopLeft(EnterPos, UpgradeUi.TopLeft, EnterSize);
            var built = UpgradeUi.Label("Built", window.Frame, EnterPos + new Vector2(-100f, EnterSize.y + 12f), new Vector2(EnterSize.x + 200f, 28f), 18f, UpgradeUi.Dim, TextAlignmentOptions.Center);
            window.Refresh = () => built.text = Mathf.RoundToInt(UpgradeRules.Fraction(building) * 100f) + "% upgraded";
            window.Refresh();
        }

        private static void OnEstateReplaced()
        {
            UpgradeAlerts.Forget();
            UpgradeRules.RaiseChanged();
        }

        // ---- the hamlet's art --------------------------------------------------------------------------

        /// <summary>
        /// DD1's exterior of a building for the share of its upgrades that are built: 1, 2 or 3
        /// (fx/town_&lt;id&gt;_level01..03; DD1's exe builds the name as fx/town_%s_level%02d and has
        /// fx/town_%s_locked for a building still shut).
        /// </summary>
        public static int ArtLevel(string building)
        {
            if (!_townLayoutRead)
            {
                _townLayoutRead = true;
                _townLayout = DarkestFile.Load("campaign/town/town.layout.darkest");
            }
            // Stock DD1 values, used only where the file cannot be read.
            float second = 0.33f, third = 0.66f;
            var block = _townLayout?.Find(building + "_layout");
            if (block != null && block.Values("level_thresholds").Count >= 2)
            {
                second = block.Float("level_thresholds", 0, second);
                third = block.Float("level_thresholds", 1, third);
            }
            var share = Share(building) + 0.0001f;
            return share >= third ? 3 : share >= second ? 2 : 1;
        }

        /// <summary>
        /// What a building's thresholds are measured against: the steps built of all there are in its own
        /// trees. The Ancestor's statue has three exteriors in DD1 (fx/town_statue_level01..03: a stump under a
        /// dead tree, the pedestal in scaffolding, the statue standing) and thresholds of its own (0.25 0.50),
        /// but no upgrade tree. GUESS: it is measured against the whole town, the steps built of all there are
        /// in every building's trees; DD1's files do not say and its code was not read. (The graveyard has one
        /// exterior and DD1's "ground" one picture, whatever their thresholds.)
        /// </summary>
        public static float Share(string building)
        {
            if (UpgradeRules.TreesOf(building).Count > 0) return UpgradeRules.Fraction(building);
            int built = 0, total = 0;
            foreach (var tree in UpgradeRules.All)
            {
                built += UpgradeRules.Level(tree);
                total += tree.Steps.Count;
            }
            return total > 0 ? (float)built / total : 0f;
        }

        private static void SyncHamlet()
        {
            foreach (var building in HamletScene.BuildingIds)
            {
                try
                {
                    HamletScene.SetBuilding(building, ArtLevel(building), BuildingLocks.Locked(building));
                }
                catch (Exception e) { Plugin.Log.LogWarning("Hamlet: art level of the " + building + " failed: " + e.Message); }
            }
        }
    }

    /// <summary>
    /// Which buildings have news. DD1 marks such a building with its red exclamation mark
    /// (fx/estate_exclamation); its executable keeps a "novelty tracker" for that and names one kind of news in
    /// it, "building_upgrade". What exactly DD1 counts as new is in its code, not in its files; here a building
    /// has news while a step of one of its trees can be built this moment (nothing it waits for is missing and
    /// the estate can pay) and the player has not had the building's upgrades open since that became so. What
    /// has been looked at is remembered for the session, not in the save.
    /// </summary>
    internal static class UpgradeAlerts
    {
        // tree id + ":" + step code of the steps the player has seen on offer
        private static readonly HashSet<string> Seen = new HashSet<string>();

        public static bool Has(string building)
        {
            if (BuildingLocks.Locked(building)) return false;
            var news = false;
            foreach (var tree in UpgradeRules.TreesOf(building))
            {
                var step = UpgradeRules.Next(tree);
                if (step == null) continue;
                var key = tree.Id + ":" + step.Code;
                // a step that went out of reach is news again when it comes back
                if (UpgradeRules.Missing(step).Count > 0 || UpgradeRules.ShortOf(step) != null) Seen.Remove(key);
                else if (!Seen.Contains(key)) news = true;
            }
            return news;
        }

        /// <summary>The player has the building's upgrades before them: what can be built now is news no longer.</summary>
        public static void Looked(string building)
        {
            foreach (var tree in UpgradeRules.TreesOf(building))
            {
                var step = UpgradeRules.Next(tree);
                if (step != null && UpgradeRules.Missing(step).Count == 0 && UpgradeRules.ShortOf(step) == null) Seen.Add(tree.Id + ":" + step.Code);
            }
        }

        /// <summary>Another estate is in force.</summary>
        public static void Forget() => Seen.Clear();
    }
}
