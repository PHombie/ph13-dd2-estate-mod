using System;
using System.Collections.Generic;
using System.Globalization;
using DD2Estate.Dev;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The loading screen's config entry, and the screen from outside the game (dev bridge):
    ///
    ///   loading.show {"kind":"raid","dungeon":"crypts"}             the way into a dungeon, put up to be looked at: the
    ///            {"quest":"plot_tutorial_crypts","tip":2}            region's picture, its name and a tip. The dd2 look
    ///            {"look":"dd1"}                                      stays until loading.hide or loading.continue; the
    ///                                                               dd1 look asks for its key at once
    ///   loading.show {"kind":"town"}                                the way home; it stays until loading.hide (or, dd2, loading.continue)
    ///   loading.hide                                                takes it down at once
    ///   loading.continue                                            dd1: the player's key. dd2: a screen that was put up
    ///                                                               to be looked at leaves by its own course
    ///   loading.state                                               what is up, in which look and part of its course, with what words
    ///   loading.layout                                              every part's place on 1920x1080 (dd1: by DD1's numbers and as built; dd2: as built)
    ///   loading.tips {"dungeon":"crypts"} / {"key":"town"}          DD1's tips under str_&lt;key&gt;_tip, and which the mod leaves out
    ///   loading.auto {"on":true}                                    dd1 screens leave without a key (scripts that press nothing; dd2 screens always do)
    ///   loading.look {"titleY":118,"titleSize":64,"tipY":955, ...}  the dd2 look's own numbers, read or set (a screen that is up is not rebuilt: show it again)
    ///   loading.dd2 {"depth":6,"fonts":true,"texts":true}           DD2's own loading screen as it stands in the game: the fader's state, its timelines, its objects;
    ///            {"path":"GameInstaller(Clone)/SubtitlesMgr"}        another object's in place of the fader's; "material": what a text material does
    ///   loading.fader {"to":"black","type":"RIGHT_TO_LEFT"}          plays one of the game's own transitions ("to":"clear" lifts it; "throbber":true/false its sign alone)
    ///
    /// "quest" is a DD1 plot quest id: its own picture and tip are shown if DD1 has them. "tip": that one of
    /// the key's texts, counted from 0 over all of them. "order": the dd1 canvas' place among the canvases.
    /// "look": dd2 or dd1 from here on, whatever the config says (until the game is closed); "look":"config" gives it back.
    /// A screen put up from here covers nothing and waits for nothing; it goes after
    /// <see cref="LoadingScreen.KeyPatience"/> at the latest.
    /// </summary>
    [EstateModule]
    internal static class LoadingScreenDev
    {
        private static void Register()
        {
            LoadingScreen.Setting = Plugin.Settings.Bind("Look", "LoadingScreen", "dd2",
                "The loading screens (into a dungeon, home to the Hamlet, into the Estate). dd2 = laid out as Darkest Dungeon II lays out a loading screen (its ink, its sign in the corner, " +
                "leaving by itself) with DD1's picture of the place, its name and DD1's tip. dd1 = DD1's own loading screen (title plate, tip box, torch, \"Press [SPACE] to continue\").");

            AgentBridge.Register("loading.show", o =>
            {
                var kind = string.Equals((string)o["kind"], "town", StringComparison.OrdinalIgnoreCase) ? LoadingScreen.Kind.Town : LoadingScreen.Kind.Raid;
                if ((int?)o["order"] is int order) LoadingScreen.Order = order;
                var look = (string)o["look"];
                if (string.Equals(look, "dd1", StringComparison.OrdinalIgnoreCase)) LoadingScreen.Forced = LoadingScreen.Look.Dd1;
                else if (string.Equals(look, "dd2", StringComparison.OrdinalIgnoreCase)) LoadingScreen.Forced = LoadingScreen.Look.Dd2;
                else if (!string.IsNullOrEmpty(look)) LoadingScreen.Forced = null;
                var dungeon = (string)o["dungeon"] ?? "crypts";
                var shown = LoadingScreen.Show(kind, dungeon, (string)o["quest"], hold: true, tip: (int?)o["tip"] ?? -1);
                return shown ? LoadingScreen.Describe() : "not shown (no DD1 install, or DD1 has no loading picture for '" + dungeon + "'; see the log)";
            });
            AgentBridge.Register("loading.hide", o =>
            {
                LoadingScreen.Hide();
                return LoadingScreen.Describe();
            });
            AgentBridge.Register("loading.continue", o => new { pressed = LoadingScreen.Continue(), state = LoadingScreen.Describe() });
            AgentBridge.Register("loading.state", o => LoadingScreen.Describe());
            AgentBridge.Register("loading.layout", o => LoadingScreen.Layout());
            AgentBridge.Register("loading.tips", o =>
            {
                var key = (string)o["key"] ?? (string)o["dungeon"] ?? "town";
                var tips = new List<object>();
                foreach (var tip in LoadingScreen.Tips(key)) tips.Add(new { index = tips.Count, shown = !LoadingScreen.LeftOut(tip), text = tip });
                return new { id = "str_" + key + "_tip", tips, leftOut = LoadingScreen.NotInTheMod };
            });
            AgentBridge.Register("loading.auto", o =>
            {
                LoadingScreen.WaitForKey = !((bool?)o["on"] ?? true);
                return new { waitForKey = LoadingScreen.WaitForKey, look = LoadingScreen.Chosen.ToString().ToLowerInvariant() };
            });
            AgentBridge.Register("loading.look", o =>
            {
                if ((float?)o["titleX"] is float tx) Dd2LoadingLook.TitleAt.x = tx;
                if ((float?)o["titleY"] is float ty) Dd2LoadingLook.TitleAt.y = ty;
                if ((float?)o["titleSize"] is float ts) Dd2LoadingLook.TitleSize = ts;
                if (o["titlePreset"] != null) Dd2LoadingLook.TitlePreset = (string)o["titlePreset"];
                if (o["titleColour"] != null) Dd2LoadingLook.TitleColour = Colour((string)o["titleColour"], Dd2LoadingLook.TitleColour);
                if ((float?)o["tipX"] is float px) Dd2LoadingLook.TipAt.x = px;
                if ((float?)o["tipY"] is float py) Dd2LoadingLook.TipAt.y = py;
                if ((float?)o["tipSize"] is float ps) Dd2LoadingLook.TipSize = ps;
                if ((bool?)o["band"] is bool band) Dd2LoadingLook.Band = band;
                if ((float?)o["fadeIn"] is float fi) Dd2LoadingLook.FadeIn = fi;
                if ((float?)o["fadeOut"] is float fo) Dd2LoadingLook.FadeOut = fo;
                if ((float?)o["leastSeen"] is float seen) LoadingScreen.LeastSeen = seen;
                if (o["raidClose"] != null) Dd2LoadingLook.RaidClose = (string)o["raidClose"];
                if (o["raidLift"] != null) Dd2LoadingLook.RaidLift = (string)o["raidLift"];
                if (o["townClose"] != null) Dd2LoadingLook.TownClose = (string)o["townClose"];
                if (o["townLift"] != null) Dd2LoadingLook.TownLift = (string)o["townLift"];
                if ((float?)o["edgeSide"] is float es) Dd2LoadingLook.EdgeDarkSide = es;
                if ((float?)o["edgeHead"] is float eh) Dd2LoadingLook.EdgeDarkHead = eh;
                if ((float?)o["edgeDark"] is float ed) Dd2LoadingLook.EdgeDark = ed;
                if ((float?)o["bandHead"] is float bh) Dd2LoadingLook.EdgeBandHead = bh;
                if ((float?)o["bandFoot"] is float bf) Dd2LoadingLook.EdgeBandFoot = bf;
                return new { numbers = Dd2LoadingLook.Numbers(), leastSeen = LoadingScreen.LeastSeen };
            });
            AgentBridge.Register("loading.dd2", o =>
            {
                var material = (string)o["material"];
                if (!string.IsNullOrEmpty(material)) return Dd2Fader.MaterialInfo(Dd2Fader.FontPreset(material)) ?? "not loaded yet (asked for: ask again), or the game has no such material";
                return Dd2Fader.Describe((string)o["path"], (int?)o["depth"] ?? 6, (bool?)o["fonts"] ?? false, (bool?)o["texts"] ?? false);
            });
            AgentBridge.Register("loading.fader", o =>
            {
                var fader = Dd2Fader.Fader;
                if (fader == null) return "the game has no fader yet";
                var type = Assets.Code.UI.Transitions.TransitionType.FADE;
                var asked = (string)o["type"];
                if (!string.IsNullOrEmpty(asked))
                    foreach (var known in new[] { Assets.Code.UI.Transitions.TransitionType.FADE, Assets.Code.UI.Transitions.TransitionType.LEFT_TO_RIGHT, Assets.Code.UI.Transitions.TransitionType.RIGHT_TO_LEFT,
                                 Assets.Code.UI.Transitions.TransitionType.TOP_TO_BOTTOM, Assets.Code.UI.Transitions.TransitionType.BOTTOM_TO_TOP, Assets.Code.UI.Transitions.TransitionType.SKIP })
                        if (string.Equals(known.ToString(), asked, StringComparison.OrdinalIgnoreCase)) type = known;
                var to = (string)o["to"];
                if (string.Equals(to, "black", StringComparison.OrdinalIgnoreCase)) fader.PlayTransition(type, true, (bool?)o["throbber"] ?? true);
                else if (string.Equals(to, "clear", StringComparison.OrdinalIgnoreCase)) fader.PlayTransition(type, false, false);
                else if ((bool?)o["throbber"] is bool sign) fader.SetThrobberVisible(sign);
                return new { type = type.ToString(), black = fader.IsBlack, clear = fader.IsClear, blocking = fader.IsBlocking };
            });
        }

        // "r,g,b" or "r,g,b,a", each 0..1
        private static Color Colour(string text, Color stock)
        {
            var parts = (text ?? "").Split(',');
            if (parts.Length < 3) return stock;
            var v = new float[4];
            v[3] = 1f;
            for (var i = 0; i < parts.Length && i < 4; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return stock;
            return new Color(v[0], v[1], v[2], v[3]);
        }
    }
}
