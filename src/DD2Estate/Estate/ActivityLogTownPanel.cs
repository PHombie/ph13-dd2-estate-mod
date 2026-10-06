using System;
using System.Collections.Generic;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A screen of the town that is not a building: the Activity Log, the Trinket Inventory, the heirloom
    /// exchange. DD1 gives each a place of its own on the town screen (town.layout.darkest town_screen_layout:
    /// activity_log_pos, realm_inventory_pos, heirloom_exchange_pos) and opens them from the estate's bar, so
    /// they cannot be <see cref="RosterWindow"/>s, which are a building's backdrop at area_pos. What they share
    /// with one is kept: they live in the hamlet's canvas, end where the roster column and the estate's bar
    /// begin (the exchange slides up from behind the bar, as in DD1), one is open at a time and none together
    /// with a building, and they go away with the hamlet.
    /// </summary>
    internal class TownPanel : MonoBehaviour
    {
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string SummaryLayout = "campaign/town/estate_summary/estate_summary.layout.darkest";

        // DD1's screen: 1920x1080, the roster column begins at town_screen_layout roster_list_pos.
        public const float ScreenWidth = 1920f, ScreenHeight = 1080f;
        public const float RoomWidth = ScreenWidth - RosterPanel.Width;

        /// <summary>What a part keeps its distance to when the screen is not 16:9.</summary>
        public enum Side
        {
            /// <summary>The screen's left edge and the bar, at full size: the currencies it belongs to do not move either.</summary>
            Left,
            /// <summary>The middle of the room left of the roster, as a building's window.</summary>
            Centre,
            /// <summary>The roster column.</summary>
            Right
        }

        private class Part
        {
            public RectTransform Rect;
            public Vector2 At;
            public Side Side;
        }

        private static TownPanel _current;

        private readonly List<Part> _parts = new List<Part>();
        private float _nextRefresh;

        public string Id { get; private set; }

        /// <summary>Where the estate's bar begins (screen pixels from the top): nothing of a panel shows below it.</summary>
        public float BarTop { get; private set; }

        /// <summary>Called twice a second while the panel is open.</summary>
        public Action Refresh;

        /// <summary>A right click asks this first and closes the panel only when it returns false.</summary>
        public Func<bool> BackOut;

        /// <summary>
        /// Called once, at the moment the panel is closed and before its objects are gone: what it changed
        /// outside itself (the roster's rows) is put back here, not a frame later.
        /// </summary>
        public Action Closed;

        public static TownPanel Current => _current;

        public static bool Shows(string id) => _current != null && _current.Id == id;

        /// <summary>
        /// Why no panel can open right now; null when one can. The estate map and the provision screen own the
        /// roster (a click picks the party) and the town crier waits for an answer.
        /// </summary>
        public static string BlockReason()
        {
            if (!EstateSession.Active || !HamletScreen.IsOpen || RosterPanel.CanvasRoot == null) return "The hamlet is not on screen";
            if (EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            if (QuestPanel.IsOpen || ProvisionScreen.IsOpen) return "Not while the estate map is open";
            if (TownEventPanel.IsOpen) return "The town crier has the floor";
            return null;
        }

        /// <param name="shade">Darkens the town behind the panel and closes it on a click there, as a building's window does.</param>
        public static TownPanel Open(string id, bool shade)
        {
            var blocked = BlockReason();
            if (blocked != null)
            {
                Plugin.Log.LogInfo("Hamlet: " + id + " not opened: " + blocked);
                return null;
            }
            RosterWindow.Close();
            Close();

            // FALLBACK numbers: DD1's own values of these entries, used when a layout file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var summary = Dd1Ui.Layout(SummaryLayout);
            var barTop = Dd1Ui.Offset(town, "town_screen_layout", "estate_summary_pos", 0f, 975f).y + Dd1Ui.Offset(summary, "estate_summary_layout", "pos_offset", 0f, -17f).y;

            var root = UiKit.Rect("Panel." + id, RosterPanel.CanvasRoot);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = new Vector2(0f, Mathf.Max(0f, ScreenHeight - barTop));
            root.offsetMax = new Vector2(-RosterPanel.Width, 0f);
            root.gameObject.AddComponent<RectMask2D>();     // DD1's bar covers what hangs below its top edge
            var panel = root.gameObject.AddComponent<TownPanel>();
            panel.Id = id;
            panel.BarTop = barTop;
            if (shade)
            {
                var wash = UiKit.Image("Shade", root, null, HamletShade.Colour, true);
                UiKit.Stretch((RectTransform)wash.transform);
                wash.gameObject.AddComponent<TownPanelClick>();
                HamletShade.Attach(wash);
            }
            _current = panel;
            return panel;
        }

        public static void Close()
        {
            if (_current == null) return;
            var panel = _current;
            _current = null;
            panel.Leave();
            Destroy(panel.gameObject);
        }

        private void Leave()
        {
            var closed = Closed;
            Closed = null;
            Refresh = null;
            try { closed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: " + name + " failed to close: " + e); }
        }

        /// <summary>
        /// A part of the panel with its corner at DD1's place on the 1920x1080 screen; children are placed in
        /// its pixels (top-left origin, y down).
        /// </summary>
        public RectTransform AddPart(string name, Vector2 screenPos, Vector2 size, Side side)
        {
            var rect = UiKit.Rect(name, transform).PlaceTopLeft(screenPos, Dd1Ui.TopLeft, size);
            _parts.Add(new Part { Rect = rect, At = screenPos, Side = side });
            Fit();
            return rect;
        }

        /// <summary>A graphic that takes the pointer for the panel: a right click on it steps back or closes, as in DD1.</summary>
        public static void RightClickCloses(Graphic graphic)
        {
            graphic.raycastTarget = true;
            graphic.gameObject.AddComponent<TownPanelClick>().RightClickOnly = true;
        }

        // DD1's screen leaves 1550 pixels left of the roster. On a wider screen a part keeps to its side; on a
        // narrower one the parts that belong to the room shrink with it.
        private void Fit()
        {
            var area = ((RectTransform)transform).rect;
            if (area.width <= 0f) return;
            var fit = Mathf.Min(1f, area.width / RoomWidth);
            foreach (var part in _parts)
            {
                if (part.Rect == null) continue;
                var scale = part.Side == Side.Left ? 1f : fit;
                float x;
                switch (part.Side)
                {
                    case Side.Centre: x = Mathf.Max(0f, (area.width - RoomWidth) * 0.5f) + part.At.x * fit; break;
                    case Side.Right: x = area.width - (RoomWidth - part.At.x) * fit; break;
                    default: x = part.At.x; break;
                }
                var at = new Vector2(x, -part.At.y * scale);
                if (!Mathf.Approximately(scale, part.Rect.localScale.x)) part.Rect.localScale = new Vector3(scale, scale, 1f);
                if ((part.Rect.anchoredPosition - at).sqrMagnitude > 0.01f) part.Rect.anchoredPosition = at;
            }
        }

        private void Update()
        {
            // A building, the estate map or the crier came up: they take the screen and the roster.
            if (RosterWindow.IsOpen || BlockReason() != null)
            {
                if (_current == this) Close();
                return;
            }
            Fit();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            try { Refresh?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Hamlet: " + name + " failed to refresh: " + e); }
        }

        // The hamlet is hidden by deactivating its canvas; a panel does not wait for it to come back.
        private void OnDisable()
        {
            if (_current == this) _current = null;
            Leave();
            Destroy(gameObject);
        }
    }

    /// <summary>Closes the town panel: any click on the darkened town around it, a right click on the panel itself.</summary>
    internal class TownPanelClick : MonoBehaviour, IPointerClickHandler
    {
        public bool RightClickOnly;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (RightClickOnly && eventData.button != PointerEventData.InputButton.Right) return;
            var panel = TownPanel.Current;
            if (RightClickOnly && panel != null && panel.BackOut != null && panel.BackOut()) return;
            TownPanel.Close();
        }
    }

    /// <summary>
    /// The buttons DD1 keeps on the estate's bar (estate_summary.layout.darkest): its "trade heirlooms" icon
    /// after the heirloom counts (heirloom_exchange_offset) and the row of screens at the bar's right end
    /// (navigation_button_start_pos, one every navigation_button_offset). The hamlet's bar is not this code's
    /// to change, so the buttons are laid over it once the hamlet's canvas is there, just above the bar and
    /// Embark and below everything that opens later. A screen adds its button from its own Register().
    ///
    /// A button is DD1's own sprite of it (fx/estate_*: a Spine skeleton, <see cref="SpineView"/>), seated with
    /// its root on the button's place: the pictures' sizes and seats, the candles' flame, glow and wax, and what
    /// a button shows while its screen is open (the ink splatter behind it, the open scroll, the chest's flash
    /// and sparkles, the taller flames, the exchange's icon turning over) are the skeleton's. The pictures a
    /// screen names for its button stand in where the sprite cannot be had.
    ///
    /// What DD1's own screens show of the row (1920x1080 frames of its hamlet, its buildings, its Estate Map and
    /// its provision screen), and what is done here:
    /// - the row is as long as it has buttons: from the roster's end the candles 1800, the book 1690, the chest
    ///   1580, the scroll 1470, and in a week with a town event the crier's bell at 1360. A button that is away
    ///   (<see cref="Entry.ShownWhile"/>) leaves no gap: the next one takes its place;
    /// - under the pointer a button gets the two rules of estate_summary.selected_overlay.png, the exchange's
    ///   icon as much as the row's, and no words: DD1 names none of them in a tooltip;
    /// - the row is drawn at full strength on every screen, the Estate Map and the provision screen included;
    ///   only the exchange's icon is dark there (a quarter of its light on the frames), and dead.
    /// GUESS: what a click on the scroll, the chest or the book does on DD1's Estate Map cannot be seen on a
    /// still. Here a screen of the hamlet does not open over the map, the shop or the crier's notice: the click
    /// gets DD1's sound of a way that is shut (/ui/town/button_click_locked) and nothing else.
    /// </summary>
    [EstateModule]
    internal static class TownPanelButtons
    {
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string SummaryLayout = "campaign/town/estate_summary/estate_summary.layout.darkest";
        private const string GlowArt = "campaign/town/estate_summary/estate_summary.selected_overlay.png";
        private static readonly Vector2 GlowSize = new Vector2(103f, 139f);
        // DD1's sprite of the "trade heirlooms" icon. A row button's sprite is the one its art's sheet belongs to.
        private const string ExchangeSprite = "fx/estate_heirloom_exchange/estate_heirloom_exchange.sprite";
        // A button that cannot be used: the exchange's icon on the Estate Map and the provision screen (DD1's
        // frames: 9.8 of its 35.6 of light there), a button whose screen is locked.
        private static readonly Color Dimmed = new Color(0.28f, 0.28f, 0.28f, 1f);

        public class Entry
        {
            public string Id;
            /// <summary>DD1 art of the button; <see cref="OpenArt"/> while its screen is open (null: the same).</summary>
            public string Art, OpenArt;
            /// <summary>The button's size where its art cannot be read.</summary>
            public Vector2 Size;
            /// <summary>The art is shown at this share of its own size (DD1 draws its bar's sprites smaller than their sheets).</summary>
            public float Scale = 1f;
            /// <summary>The art's middle from the place's (DD1's pixels, y down): a button's place is the root of
            /// its sprite (navigation_button_start_pos and its steps, heirloom_exchange_offset).</summary>
            public Vector2 Offset;
            /// <summary>More pictures of the same button over its art (the candle's wax and flame), where its sprite cannot be had.</summary>
            public Layer[] Layers;
            /// <summary>
            /// Order in the row at the bar's right end, 0 = the end: the buttons on show stand one place after
            /// the other in this order. Below 0: DD1's heirloom exchange spot.
            /// </summary>
            public int Place;
            /// <summary>The screen's name, for the test bridge (DD1 writes no name over a button).</summary>
            public Func<string> Name;
            public Action Toggle;
            public Func<bool> IsOpen;
            /// <summary>The button is on the bar only while this holds (the crier's bell: a week with a town event); null: always.</summary>
            public Func<bool> ShownWhile;
            /// <summary>True while the screen cannot be opened at all: the button is there, dimmed and dead.</summary>
            public Func<bool> Locked;
            /// <summary>
            /// The button does its work whatever is on screen (the options behind the candles). The others open a
            /// screen of the hamlet and are refused while none can open (<see cref="TownPanel.BlockReason"/>).
            /// </summary>
            public bool Always;
            /// <summary>The pointer's box, where the resting sprite is more than the one picture named as its art (DD1 pixels); nought: the art's size.</summary>
            public Vector2 Box;

            /// <summary>What takes the pointer: the resting picture's box (and the picture itself without a sprite).</summary>
            internal Image Icon;
            internal Button Button;
            internal SpineView View;
            /// <summary>The button's place, the root of its sprite, in DD1's screen pixels.</summary>
            internal Vector2 At;
            internal Vector2 BoxSize;
            internal bool RightAnchored;
            /// <summary>Whether the button is on the bar now (<see cref="ShownWhile"/>).</summary>
            internal bool Present = true;
            /// <summary>What the button shows: its screen open or shut; nothing yet.</summary>
            internal bool Open, Shown;
        }

        /// <summary>A picture over a button's art: one file, or several as the frames of a loop.</summary>
        public class Layer
        {
            public string[] Art;
            /// <summary>Its middle from the place's middle (DD1's pixels, y down).</summary>
            public Vector2 Offset;
            public float Scale = 1f, FramesPerSecond = 10f;
        }

        private class Frames : MonoBehaviour
        {
            public Image Image;
            public Sprite[] Sprites;
            public float Scale, PerSecond;

            private void Update()
            {
                if (Sprites == null || Sprites.Length == 0) return;
                var sprite = Sprites[(int)(Time.unscaledTime * PerSecond) % Sprites.Length];
                if (sprite == null || Image.sprite == sprite) return;
                Image.sprite = sprite;
                Image.rectTransform.sizeDelta = sprite.rect.size * Scale;
            }
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static RectTransform _root;
        private static float _builtWidth;
        private static bool _stale;

        public static void Add(Entry entry)
        {
            Entries.RemoveAll(e => e.Id == entry.Id);
            Entries.Add(entry);
            _stale = true;
        }

        private static void Register()
        {
            // The same kind of object the plugin runs on: the game destroys the loader's own (MODLOG, gotcha 1).
            var go = new GameObject("DD2Estate.TownPanelButtons") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();

            // DD1's row, from the roster's end: the candles (options), the book (glossary), the chest, the scroll, the
            // crier's bell in a week with a town event and the districts' button once they are open. The candles open the game's own menu, which is where its options are; while it is
            // up they burn as DD1's do over its options (the sprite's "selected": taller flames, the ink behind).
            // The pictures named here are what is shown if the sprite itself cannot be read.
            const string candles = "fx/estate_settings/estate_settings.sprite.png";
            Add(new Entry
            {
                Id = "options", Art = candles + "#candles", Scale = 0.65f, Offset = new Vector2(0f, 9.5f), Size = new Vector2(62f, 71f), Place = 0,
                Layers = new[]
                {
                    new Layer { Art = new[] { candles + "#wax" }, Offset = new Vector2(3f, -8.5f), Scale = 0.65f },
                    new Layer { Art = new[] { candles + "#flame01", candles + "#flame02", candles + "#flame03", candles + "#flame04" }, Offset = new Vector2(9f, -36f), Scale = 0.5f, FramesPerSecond = 9f }
                },
                Name = () => WindowText.Plain("menu_base_element_view_options") ?? "Options", Always = true,
                Toggle = () => Assets.Code.Utils.SingletonMonoBehaviour<Assets.Code.UI.Managers.CommonUiBhv>.Instance.TogglePauseMenu(),
                IsOpen = () =>
                {
                    var ui = Assets.Code.Utils.SingletonMonoBehaviour<Assets.Code.UI.Managers.CommonUiBhv>.Instance;
                    return ui != null && ui.IsPauseOrOptionsMenuActive();
                }
            });
        }

        private class Watcher : MonoBehaviour
        {
            private void Update()
            {
                if (!EstateSession.Active || !HamletScreen.IsOpen)
                {
                    // what closed while the hamlet was away closed unseen: the buttons come back as things are, without a sound
                    foreach (var entry in Entries) entry.Shown = false;
                    return;
                }
                var canvas = RosterPanel.CanvasRoot as RectTransform;
                if (canvas == null) return;
                try
                {
                    // a button that comes or goes (the crier's bell with the week's event) moves the row
                    foreach (var entry in Entries)
                    {
                        var present = entry.ShownWhile == null || entry.ShownWhile();
                        if (present == entry.Present) continue;
                        entry.Present = present;
                        _stale = true;
                    }
                    // the row keeps to the canvas's right edge, which a new resolution moves
                    if (_root == null || _stale || !Mathf.Approximately(_builtWidth, canvas.rect.width)) Build(canvas);
                    Show();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Hamlet: the bar's buttons failed: " + e);
                    enabled = false;
                }
            }
        }

        private static void Build(RectTransform canvas)
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _stale = false;
            _builtWidth = canvas.rect.width;

            // FALLBACK numbers: DD1's own values of these entries, used when a layout file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var strip = Dd1Ui.Layout(SummaryLayout);
            const string summary = "estate_summary_layout";
            var bar = Dd1Ui.Offset(town, "town_screen_layout", "estate_summary_pos", 0f, 975f) + Dd1Ui.Offset(strip, summary, "pos_offset", 0f, -17f);
            var exchange = bar + Dd1Ui.Offset(strip, summary, "heirloom_exchange_offset", 700f, 51f);
            var first = bar + Dd1Ui.Offset(strip, summary, "navigation_button_start_pos", 1800f, 45f);
            var step = Dd1Ui.Offset(strip, summary, "navigation_button_offset", -110f, 0f);
            var glowAt = Dd1Ui.Offset(strip, summary, "selected_overlay_offset", 0f, 0f);

            _root = UiKit.Stretch(UiKit.Rect("TownPanelButtons", canvas));
            var under = canvas.Find("EmbarkButton") ?? canvas.Find("EstateSummary");
            if (under != null) _root.SetSiblingIndex(under.GetSiblingIndex() + 1);

            // The row's seats, from the roster's end, go to the buttons on show in the order of their places.
            var row = new List<Entry>();
            foreach (var entry in Entries)
                if (entry.Present && entry.Place >= 0) row.Add(entry);
            row.Sort((a, b) => a.Place.CompareTo(b.Place));

            foreach (var entry in Entries)
            {
                // DD1's numbers are the middle of a button, where the root of its sprite stands: the row's as much as the
                // exchange's. Measured on a real frame of DD1's hamlet with tools/dd1_fit_sprite.py: the row's sprites
                // at 1800, 1690, 1580, 1470 by 1003, and the exchange's icon with its middle at 755, 1008.5 under the
                // Color of Madness' heirloom_exchange_offset 755 51.
                entry.RightAnchored = entry.Place >= 0;
                entry.Shown = false;
                entry.Icon = null;
                entry.Button = null;
                entry.View = null;
                if (!entry.Present) continue;
                entry.At = entry.RightAnchored ? first + step * row.IndexOf(entry) : exchange;
                // the row keeps to the roster's end of the bar, the exchange to the currencies' end
                var seat = UiKit.Rect("Button." + entry.Id, _root);
                seat.Place(entry.RightAnchored ? new Vector2(1f, 1f) : Dd1Ui.TopLeft, Dd1Ui.Middle,
                    new Vector2(entry.RightAnchored ? entry.At.x - TownPanel.ScreenWidth : entry.At.x, -entry.At.y), Vector2.zero);

                entry.View = SpineView.Create("Sprite", seat, SpriteOf(entry));
                var sprite = Dd1Ui.Sprite(entry.Art);
                entry.BoxSize = entry.Box.x > 0f && entry.Box.y > 0f ? entry.Box : sprite != null ? sprite.rect.size * entry.Scale : entry.Size;

                // DD1 marks the button under the pointer with its lines above and below (frames with the pointer on
                // the scroll, the chest, the candles and the exchange's icon: the overlay's middle on the button's
                // place), not with a brighter picture, and writes no name over it
                var lines = Dd1Ui.Sprite(GlowArt);
                var glowImage = UiKit.Image("Glow", seat, lines, lines != null ? Color.white : Color.clear);
                ((RectTransform)glowImage.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, new Vector2(glowAt.x, -glowAt.y), GlowSize);
                var glow = glowImage.gameObject;
                glow.SetActive(false);

                // The pointer's box is the resting picture's. With the sprite on show it is clear glass over it.
                var plain = entry.View == null;
                entry.Icon = UiKit.Image("Box", seat, plain ? sprite : null, !plain ? Color.clear : sprite != null ? Color.white : new Color(0.2f, 0.17f, 0.1f, 0.95f), true);
                var rect = (RectTransform)entry.Icon.transform;
                rect.Place(Dd1Ui.Middle, Dd1Ui.Middle, new Vector2(entry.Offset.x, -entry.Offset.y), entry.BoxSize);
                foreach (var layer in plain && entry.Layers != null ? entry.Layers : new Layer[0])
                {
                    var frames = new List<Sprite>();
                    foreach (var art in layer.Art) frames.Add(Dd1Ui.Sprite(art));
                    if (frames.Count == 0 || frames[0] == null) continue;
                    var over = UiKit.Image("Layer", rect, frames[0], Color.white);
                    over.raycastTarget = false;
                    ((RectTransform)over.transform).Place(Dd1Ui.Middle, Dd1Ui.Middle, new Vector2(layer.Offset.x - entry.Offset.x, -(layer.Offset.y - entry.Offset.y)), frames[0].rect.size * layer.Scale);
                    if (frames.Count < 2) continue;
                    var loop = over.gameObject.AddComponent<Frames>();
                    loop.Image = over;
                    loop.Sprites = frames.ToArray();
                    loop.Scale = layer.Scale;
                    loop.PerSecond = layer.FramesPerSecond;
                }

                var button = entry.Icon.gameObject.AddComponent<Button>();
                button.targetGraphic = entry.Icon;
                var colours = button.colors;
                // DD1 draws the bar's sprites as they are (a real frame against the sheet: 4 of 255 apart)
                colours.normalColor = Color.white;
                colours.highlightedColor = Color.white;
                colours.pressedColor = new Color(0.7f, 0.6f, 0.5f);
                colours.disabledColor = Dimmed;
                button.colors = colours;
                entry.Button = button;
                var captured = entry;
                button.onClick.AddListener(() =>
                {
                    try { Press(captured); }
                    catch (Exception e) { Plugin.Log.LogError("Hamlet: " + captured.Id + " failed to open: " + e); }
                });
                UiKit.Hover(entry.Icon.gameObject, inside =>
                {
                    var alive = button != null && button.interactable;
                    glow.SetActive(inside && alive);
                    // DD1's sound of the pointer coming onto a button (which of its buttons play it is in no file)
                    if (inside && alive) EstateAudio.Ui("ui/town/button_mouse_over");
                });
            }
        }

        // A click on a button. A screen of the hamlet does not open while none can (the Estate Map, the shop or
        // the crier's notice is up): DD1's sound of a way that is shut. A button whose screen is open always
        // closes it.
        private static string Press(Entry entry)
        {
            if (entry.Locked != null && entry.Locked()) return "locked";
            var open = entry.IsOpen != null && entry.IsOpen();
            var blocked = !open && !entry.Always ? TownPanel.BlockReason() : null;
            if (blocked != null)
            {
                EstateAudio.Ui("ui/town/button_click_locked");
                return "not now: " + blocked;
            }
            entry.Toggle?.Invoke();
            return entry.IsOpen != null && entry.IsOpen() ? "open" : "closed";
        }

        // "fx/estate_activity_log/estate_activity_log.sprite.png#scroll_closed" is a picture of the sprite
        // "fx/estate_activity_log/estate_activity_log.sprite"; the exchange's art is a plain file, its sprite DD1's own.
        private static string SpriteOf(Entry entry)
        {
            var hash = entry.Art != null ? entry.Art.IndexOf('#') : -1;
            if (hash > 4 && entry.Art.Substring(0, hash).EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return entry.Art.Substring(0, hash - 4);
            return entry.Place < 0 ? ExchangeSprite : null;
        }

        private static void Show()
        {
            // DD1's Estate Map and provision screen: the row as ever, the exchange's icon dark
            var away = QuestPanel.IsOpen || ProvisionScreen.IsOpen;
            foreach (var entry in Entries)
            {
                if (entry.Icon == null) continue;
                var open = entry.IsOpen != null && entry.IsOpen();
                if (!entry.Shown || open != entry.Open)
                {
                    // a button just built says nothing: it shows what is
                    var sound = entry.Shown ? Sound(entry, open) : null;
                    if (sound != null) EstateAudio.Ui(sound);
                    if (entry.View != null) Pose(entry.View, open, !entry.Shown);
                    else
                    {
                        // the open picture has its own size: it keeps the middle of the button's box
                        var sprite = Dd1Ui.Sprite(open && entry.OpenArt != null ? entry.OpenArt : entry.Art);
                        if (sprite != null)
                        {
                            entry.Icon.sprite = sprite;
                            ((RectTransform)entry.Icon.transform).sizeDelta = sprite.rect.size * entry.Scale;
                        }
                    }
                    entry.Open = open;
                    entry.Shown = true;
                }
                var off = (!entry.RightAnchored && away) || (entry.Locked != null && entry.Locked());
                if (entry.Button != null && entry.Button.interactable == off) entry.Button.interactable = !off;
                if (entry.View != null) entry.View.Tint = off ? Dimmed : Color.white;
            }
        }

        // DD1's bar sprites name their states alike: "idle" (or "idle_loop") at rest and "selected" while the button's
        // screen is open; some run "selected" into a "selected_loop", and the exchange's icon leaves it by "unselected".
        // (That "selected" is the open screen and not the pointer: a frame of DD1 with the pointer on the scroll
        // shows the scroll shut.)
        private static void Pose(SpineView view, bool open, bool atOnce)
        {
            var idle = view.Has("idle_loop") ? "idle_loop" : "idle";
            if (!open)
            {
                if (!atOnce && view.Has("unselected")) view.Play("unselected", idle);
                else view.Play(idle);
            }
            else if (!view.Has("selected_loop")) view.Play("selected");
            else if (atOnce) view.Play("selected_loop");
            else view.Play("selected", "selected_loop");
        }

        // The Trinket Inventory and the heirloom exchange play DD1's sounds of their coming and going themselves
        // (ui/town/trinket_*, heirloom_exchange_*: DD1's exe names them beside the bar's sprites), and the candles open
        // the game's own menu. The scroll's screen has none of its own: unrolled and rolled up it is DD1's page_open
        // and page_close (the mod's reading: the exe names the two apart from any screen).
        private static string Sound(Entry entry, bool open)
        {
            var sprite = SpriteOf(entry) ?? "";
            if (sprite.IndexOf("estate_activity_log", StringComparison.Ordinal) >= 0) return open ? "ui/town/page_open" : "ui/town/page_close";
            return null;
        }

        /// <summary>For tests: the buttons on the bar and where they stand (DD1 screen pixels: a button's place, which is its sprite's middle, and the left edge of the box that takes the pointer).</summary>
        public static object Describe()
        {
            var buttons = new List<object>();
            foreach (var entry in Entries)
                buttons.Add(new
                {
                    id = entry.Id, name = entry.Name?.Invoke(), onBar = entry.Present, x = entry.At.x, left = entry.At.x + entry.Offset.x - entry.BoxSize.x * 0.5f, middle = entry.At.y,
                    fromRight = entry.RightAnchored, built = entry.Icon != null, sprite = entry.View != null ? entry.View.Animation : null,
                    open = entry.IsOpen != null && entry.IsOpen(), dead = entry.Button != null && !entry.Button.interactable
                });
            return new { built = _root != null, blocked = TownPanel.BlockReason(), panel = TownPanel.Current != null ? TownPanel.Current.Id : null, buttons };
        }

        /// <summary>For tests: what a click on a bar button does.</summary>
        public static string Click(string id)
        {
            var entry = Entries.Find(e => e.Id == id);
            if (entry == null) return "no such button";
            if (!entry.Present) return "not on the bar";
            if (entry.Button != null && !entry.Button.interactable) return "dead";
            return Press(entry);
        }
    }
}
