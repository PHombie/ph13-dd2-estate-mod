using System;
using System.Collections.Generic;
using DD2Estate.Dd2;
using DD2Estate.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The places a hero can move to, marked on the companions who stand in them while "move" is chosen. The
    /// marks follow the heroes as their bars do (<see cref="RaidTrays.Place"/>), breathe so that the eye finds
    /// them, and the one whose hero the pointer is on is lit. A click on a mark is a click on its hero.
    ///
    /// The marks are DD2's own, as its fight draws them once "move" is chosen there: over the bars of every
    /// friend the skill can go to a thin line forked at both ends, green (ActorSelectionIndicatorBhv, the
    /// friendly indicator: ui_indicator_unselected_target tinted #829852), and under the pointer the larger
    /// bracket of a chosen friend (ui_indicator_selected_target_heal tinted #9ABE4E). The two pictures are the
    /// game's: its scene of a fight holds them, which is not loaded before a session's first fight, so they are
    /// asked of the game by their addresses (each lies in a bundle of its own) when the screen is built.
    ///
    /// Until they have come, and should they not come at all, the mark is DD1's own for such a place
    /// (overlays/move.png, 175x69: the teal bracket with the two arrows in its middle, which DD1 draws around
    /// a hero's bars in a fight once "move" is chosen), lit as DD1 lights a button.
    /// </summary>
    internal class RaidMoveMarks
    {
        /// <summary>1: DD2's mark of a friend who can be chosen (DD1's while the game's pictures are not there). 0: DD1's mark.</summary>
        public static int Look = 1;

        private const string Art = "overlays/move.png";
        private static readonly Vector2 ArtSize = new Vector2(175f, 69f);
        // The mod's own numbers.
        private const float BarRow = 26f;           // the middle of DD1's mark's two bars, rows from the picture's top (measured)
        private const float BarRise = 10f;          // that middle stands this far above the tray's line: where DD1's selection bracket has its bar
        private const float BreathSeconds = 1.6f;
        private const float BreathLow = 0.62f;      // the least of a mark's light while nothing points at it

        // DD2's mark, measured in its own frame of a fight (2560 wide, here in the 1920 of the mod's screen): the
        // waiting mark is 157 wide and 39 high with its line 9 above the health bar; the one under the pointer
        // is the performer's size, 157 by 68. The line lies at this share of each picture's height.
        private const string Dd2Folder = "Assets/Art/UI/HUD/";
        private const string Dd2Waiting = "ui_indicator_unselected_target", Dd2Pointed = "ui_indicator_selected_target_heal";
        private static readonly Color Dd2WaitingColour = new Color32(0x82, 0x98, 0x52, 0xFF), Dd2PointedColour = new Color32(0x9A, 0xBE, 0x4E, 0xFF);
        private static readonly Vector2 Dd2WaitingSize = new Vector2(157f, 39f), Dd2PointedSize = new Vector2(157f, 68f);
        private const float Dd2WaitingLine = 0.47f, Dd2PointedLine = 0.49f, Dd2LineRise = 9f;

        private static bool _asked;
        private static Sprite _dd2Waiting, _dd2Pointed;
        // kept for the session: the game may load and let go of its own as fights come and go
        private static readonly List<AsyncOperationHandle<Sprite>> Held = new List<AsyncOperationHandle<Sprite>>();

        /// <summary>Dev bridge: how the asking for DD2's pictures went.</summary>
        public static string Dd2Status { get; private set; } = "not asked for";

        /// <summary>True once the game has given its two pictures of a friend's mark.</summary>
        public static bool Dd2PicturesLoaded => _dd2Waiting != null && _dd2Pointed != null;

        private class Mark
        {
            public uint Guid;
            public Image Image;
            public bool Hovered, Lit;
        }

        private readonly RectTransform _root;
        private readonly RaidTrays _trays;
        private readonly List<Mark> _marks = new List<Mark>();
        private string _key = "";
        private bool _dd2;      // this showing draws DD2's pictures

        /// <summary>A marked hero was clicked.</summary>
        public Action<uint> Clicked;
        /// <summary>The right button on a mark: "move" is put away.</summary>
        public Action Cancelled;

        public RaidMoveMarks(RectTransform screen, RaidTrays trays)
        {
            _trays = trays;
            _root = UiKit.Rect("MoveMarks", screen);
            UiKit.Stretch(_root);
            Prepare();
        }

        /// <summary>Asks the game for its two pictures (once); they are there some frames later.</summary>
        public static void Prepare()
        {
            if (_asked) return;
            _asked = true;
            Dd2Status = "loading";
            Ask(Dd2Waiting, sprite => _dd2Waiting = sprite);
            Ask(Dd2Pointed, sprite => _dd2Pointed = sprite);
        }

        // A picture of the game's by its address. Where it is asked first: an address the game does not have is
        // an error in its log when it is loaded outright.
        private static void Ask(string name, Action<Sprite> got)
        {
            try
            {
                var address = Dd2Folder + name + ".png";
                var where = Addressables.LoadResourceLocationsAsync(address, typeof(Sprite));
                where.Completed += found =>
                {
                    try
                    {
                        if (found.Status != AsyncOperationStatus.Succeeded || found.Result == null || found.Result.Count == 0)
                        {
                            Dd2Status = "the game has no picture at " + address;
                            Plugin.Log.LogWarning("Dungeon HUD: " + Dd2Status + "; DD1's mark of a place to move to is shown");
                            return;
                        }
                        var handle = Addressables.LoadAssetAsync<Sprite>(found.Result[0]);
                        Held.Add(handle);
                        handle.Completed += loaded =>
                        {
                            if (loaded.Status == AsyncOperationStatus.Succeeded && loaded.Result != null)
                            {
                                got(loaded.Result);
                                if (Dd2PicturesLoaded) Dd2Status = "loaded";
                            }
                            else
                            {
                                Dd2Status = "the game did not give " + address;
                                Plugin.Log.LogWarning("Dungeon HUD: " + Dd2Status + "; DD1's mark of a place to move to is shown");
                            }
                        };
                    }
                    catch (Exception e)
                    {
                        Dd2Status = "failed: " + e.Message;
                        Plugin.Log.LogWarning("Dungeon HUD: DD2's target mark could not be loaded (" + e.Message + "); DD1's mark of a place to move to is shown");
                    }
                };
            }
            catch (Exception e)
            {
                Dd2Status = "failed: " + e.Message;
                Plugin.Log.LogWarning("Dungeon HUD: DD2's target mark could not be asked for (" + e.Message + "); DD1's mark of a place to move to is shown");
            }
        }

        /// <summary>True while places are marked.</summary>
        public bool Shown => _marks.Count > 0;

        /// <summary>Marks the places of these heroes (none: the marks go). Cheap to call with the same heroes again.</summary>
        public void Show(IReadOnlyList<uint> heroes)
        {
            var dd2 = Look == 1 && Dd2PicturesLoaded;
            var key = heroes == null || heroes.Count == 0 ? "" : (dd2 ? "dd2:" : "dd1:") + string.Join(",", heroes);
            if (key == _key) return;
            _key = key;
            RaidUi.Clear(_root);
            _marks.Clear();
            _dd2 = dd2;
            if (heroes == null || heroes.Count == 0) return;
            foreach (var guid in heroes)
            {
                var hero = guid;
                var mark = new Mark { Guid = guid };
                if (_dd2)
                {
                    mark.Image = UiKit.Image("Move" + guid, _root, _dd2Waiting, Dd2WaitingColour, true);
                    ((RectTransform)mark.Image.transform).PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, Dd2WaitingSize);
                }
                else mark.Image = RaidUi.Art("Move" + guid, _root, Art, Vector2.zero, ArtSize, new Color(0.15f, 0.72f, 0.7f, 0.5f), true);
                RaidUi.Pointer(mark.Image, () => Clicked?.Invoke(hero), () => Cancelled?.Invoke(), inside => mark.Hovered = inside);
                _marks.Add(mark);
            }
            Follow();
        }

        /// <summary>Every frame: the marks stand at their heroes' feet, and the one pointed at is lit.</summary>
        public void Follow()
        {
            if (_marks.Count == 0) return;
            var l = RaidLayout.Current;
            var breath = Mathf.Lerp(BreathLow, 1f, 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime / BreathSeconds * 2f * Mathf.PI));
            // the pointer on a hero is the pointer on their mark (the figure above a tray takes the pointer itself)
            var over = Mouse.current != null ? _trays.HeroAt(Mouse.current.position.ReadValue()) : 0u;
            foreach (var mark in _marks)
            {
                var seen = _trays.Place(mark.Guid, 0f, out var at);
                if (mark.Image.enabled != seen) mark.Image.enabled = seen;
                if (!seen) continue;
                var lit = mark.Hovered || over == mark.Guid;
                var rect = (RectTransform)mark.Image.transform;
                if (_dd2)
                {
                    // DD2: another picture under the pointer, larger, in a lighter green
                    var size = lit ? Dd2PointedSize : Dd2WaitingSize;
                    if (lit != mark.Lit)
                    {
                        mark.Image.sprite = lit ? _dd2Pointed : _dd2Waiting;
                        rect.sizeDelta = size;
                    }
                    // its line stands over the health bar as drawn (DD2's bars stand higher than DD1's: RaidTrays.BarTop)
                    rect.anchoredPosition = new Vector2(Mathf.Round(at.x - size.x * 0.5f), -(_trays.BarTop - Dd2LineRise - size.y * (lit ? Dd2PointedLine : Dd2WaitingLine)));
                    var colour = lit ? Dd2PointedColour : Dd2WaitingColour;
                    colour.a = lit ? 1f : breath;
                    mark.Image.color = colour;
                }
                else
                {
                    // DD1's light of a button under the pointer
                    if (lit != mark.Lit) mark.Image.material = lit ? RaidHighlight.Tinted("button_highlight", new Color(1.5f, 1.5f, 1.3f, 1f)) : null;
                    rect.anchoredPosition = new Vector2(Mathf.Round(at.x - ArtSize.x * 0.5f), -(l.TrayY - BarRise - BarRow));
                    var colour = mark.Image.color;
                    colour.a = lit ? 1f : breath;
                    mark.Image.color = colour;
                }
                mark.Lit = lit;
            }
        }

        /// <summary>Dev bridge: whose places are marked, where the marks stand on DD1's screen, and whose pictures they are.</summary>
        public object Describe()
        {
            var list = new List<object>();
            foreach (var mark in _marks)
            {
                var rect = (RectTransform)mark.Image.transform;
                var at = rect.anchoredPosition;
                list.Add(new { hero = mark.Guid, x = at.x + rect.sizeDelta.x * 0.5f, y = -at.y, art = mark.Image.sprite != null ? mark.Image.sprite.name : null, shown = mark.Image.enabled, lit = mark.Lit });
            }
            return new { look = Look, dd2 = _dd2, dd2Pictures = Dd2Status, marks = list };
        }
    }

    /// <summary>
    /// Escape is the game's key for its menu. While "move" is chosen on the dungeon's screen the key puts the
    /// skill away, and the menu stays shut for that press.
    /// </summary>
    [HarmonyPatch(typeof(Assets.Code.UI.Managers.CommonUiBhv), "TogglePauseMenu")]
    internal static class EscapePutsMoveAway
    {
        private static bool Prefix()
        {
            try { return !(EstateSession.Active && DungeonHud.TakesEscape()); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Dungeon HUD: the menu's key could not be looked at: " + e.Message);
                return true;
            }
        }
    }
}
