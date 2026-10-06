using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The bars under every hero of the party, in the scene: health, ten stress pips, the marks of what lasts
    /// on a hero outside a fight, and the mark of the selected hero. They stand where the heroes do: the trays
    /// follow the hero models on screen, in the corridor and at a camp alike, and fall back to DD1's own places
    /// (screen.raid.darkest, overlays: hero_start_pos and hero_spacing) while no model is to be found.
    ///
    /// Their look is DD2's ([Look] HeroBars = dd2, the owner's wish of 2026-10-06): a copy of the panel DD2's
    /// fight keeps under an actor (<see cref="Dd2Bars"/>: its health bar, pips, row of marks and selection
    /// mark), at the fight's size, <see cref="Dd2Hud.BarsRise"/> above the fight's line. What DD1's tray icons
    /// told (<see cref="TrayIcons"/>) are marks in DD2's row under the pips: DD2's own marks for death's door
    /// and what it leaves behind, DD2's own pictures where it has one, and DD1's little picture on a token
    /// where it has none. While DD2's panel is not to be had, and with [Look] HeroBars = dd1, the look is DD1's:
    /// its status bars (scripts/layout/screen.raid.status_bars.darkest), its selection bracket
    /// (overlays/selected_1.png) and its status icons (overlays/tray_*.png) in the two rows over the bar.
    ///
    /// A click on a hero (the tray, or the hero above it) selects them, as in DD1; a right click opens their
    /// sheet. DD2's stress runs 0..10: a pip is a point. The pointer on the bars is told health and stress, on
    /// a mark what it is the sign of, in DD1's words.
    /// </summary>
    internal class RaidTrays
    {
        private const int Pips = 10;
        private static readonly Vector2 PipEmpty = new Vector2(8f, 12f);        // overlays/stress_pip_empty.png
        private static readonly Vector2 PipFull = new Vector2(9f, 10f);         // overlays/stress_pip_full.png
        private static readonly Vector2 BracketArt = new Vector2(175f, 206f);   // overlays/selected_1.png, target_h_1.png
        private static readonly Vector2 IconArt = new Vector2(24f, 24f);        // overlays/tray_*.png: 20 px of picture in the middle
        private const float BracketLine = 172f;             // the bracket's bar ends on this row of its picture (measured)

        // Measured in DD1's own frames (_lab/dd1_ref/raid/raid_trap_spotted_hover.png and its neighbours).
        private const float BracketGap = 4f;                // the bracket's bar sits this far above the health bar (the picture's top 176 px above the bar's)
        private const float DragStart = 12f;                // screen pixels a hero is carried before it counts as a change of rank and not a click
        // The hero above a tray takes clicks as well. No wider or taller than the figure: the corridor's own
        // clicks (walking, a prop beside the party) pass by on either side.
        private static readonly Vector2 Zone = new Vector2(110f, 300f);
        private const float MinStep = 112f;                 // two trays never overlap (heroes sit close at a camp)
        private const float ModelSearch = 0.5f;             // seconds between looks for the hero models
        private const float TooltipRise = 44f;              // a tray's tooltip stands this far above the health bar: clear of the status icons
        private const float TooltipWidth = 260f;
        private const float Dd2TooltipRise = 12f;           // DD2's look has nothing over the bar: the tooltip stands just above it

        // A place for a status icon in one of a tray's two rows (DD1's look).
        private class Slot
        {
            public Image Image;
            public TrayIcon Icon;               // what stands there now; null: nothing
            public Vector2 At;                  // its hot spot's corner, from the tray's root
            public float Pulse = -1f;           // seconds since the icon appeared; below 0: at rest
        }

        // A mark in DD2's row.
        private class Mark
        {
            public TrayIcon Icon;
            public Dd2Bars.Mark Part;
            public string Art;                  // what it is drawn with: "own", a DD2 address, "dd1"
            public float Pulse = -1f;
        }

        private class Tray
        {
            public uint Guid;
            public int Index;
            public RectTransform Root, Zone;
            public Transform Model;
            public float X;
            public bool Seen;                   // refreshed once: an icon that comes now is new
            // DD1's look
            public RectTransform Health;
            public readonly Image[] Pips = new Image[RaidTrays.Pips];
            public readonly List<Slot> Left = new List<Slot>(), Right = new List<Slot>();
            public Image Selected, Target;
            // DD2's look
            public Dd2Bars.View Dd2;
            public readonly List<Mark> Marks = new List<Mark>();
        }

        private readonly RectTransform _screen;
        private readonly RectTransform _root;
        private readonly RaidTooltip _tooltip;
        private readonly List<Tray> _trays = new List<Tray>();
        private readonly List<uint> _party = new List<uint>();
        private bool _dd2;                      // the trays on screen are DD2's
        private float _rise, _size;             // and were built with this rise and at this size
        private float _nextSearch;
        private static Texture2D _healthBack, _healthFill;

        /// <summary>Dev bridge: icons shown on a hero whatever their state (hero guid, then the icons' keys).</summary>
        public static readonly Dictionary<uint, HashSet<string>> DevIcons = new Dictionary<uint, HashSet<string>>();

        /// <summary>Dev bridge: DD2's marks are all drawn with DD1's little pictures (to compare the two).</summary>
        public static bool Dd1Pictures;

        /// <summary>A hero was clicked: guid, and whether it was the right button.</summary>
        public Action<uint, bool> Clicked;

        /// <summary>
        /// A hero was carried with the pointer and let go over another: who, and onto whose place. DD1 changes the
        /// party's order outside a fight with a hero's move skill and a click on the place to go to; the Estate's
        /// banner holds DD2's five skills where DD1 has four and the move, so the hero is carried there instead.
        /// </summary>
        public Action<uint, uint> Carried;
        /// <summary>True while the party's order may be changed (not at a camp, not in the middle of something).</summary>
        public Func<bool> CanCarry;

        private uint _carried, _carriedOver;

        /// <summary>The hero being carried to another rank; 0 for none.</summary>
        public uint Carrying => _carried;

        /// <summary>The hero whose place on the screen a point is in (the figure over a tray); 0 for none.</summary>
        public uint HeroAt(Vector2 screen)
        {
            if (!_root.gameObject.activeInHierarchy) return 0u;
            foreach (var tray in _trays)
                if (tray.Root.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(tray.Zone, screen, null)) return tray.Guid;
            return 0u;
        }

        private void Carry(uint guid, Vector2 screen, bool end)
        {
            if (_carried != guid) return;
            var over = HeroAt(screen);
            _carriedOver = over != guid ? over : 0u;
            var onto = _carriedOver;
            if (end) _carried = _carriedOver = 0u;
            // the bracket of the place the hero would take follows the pointer at once
            foreach (var tray in _trays) ShowTarget(tray, !end && tray.Guid == onto);
            if (end && onto != 0u) Carried?.Invoke(guid, onto);
        }

        public RaidTrays(RectTransform screen, RaidTooltip tooltip)
        {
            _screen = screen;
            _tooltip = tooltip;
            _root = UiKit.Rect("Trays", screen);
            UiKit.Stretch(_root);
        }

        public IEnumerable<uint> Shown
        {
            get { foreach (var tray in _trays) yield return tray.Guid; }
        }

        /// <summary>DD1 takes the bars and brackets away while the party goes through a door.</summary>
        public bool Visible
        {
            set { if (_root != null && _root.gameObject.activeSelf != value) _root.gameObject.SetActive(value); }
        }

        private CanvasGroup _group;

        /// <summary>
        /// How much of the bars, brackets and icons is drawn, 1 all .. 0 nothing: DD1's scripts take the HUD over
        /// the scene away while a hero is shown close (layers.HUD_alpha: 0 within a tenth of a second as a hero
        /// turns to a curio, tries a trap or is caught by one; back over 0.3 s). Seen in DD1's own frame of a
        /// sprung trap (_lab/dd1_ref/raid/raid_trap_sprung_failed_disarm.png): no bars, no quest info, no torch,
        /// the two panels under the scene as they were. While they are away they take no clicks.
        /// </summary>
        public float Alpha
        {
            set
            {
                if (_group == null) _group = _root.gameObject.AddComponent<CanvasGroup>();
                if (!Mathf.Approximately(_group.alpha, value)) _group.alpha = value;
                _group.blocksRaycasts = value > 0.5f;
            }
        }

        // DD2's look is wanted and its panel is there.
        private static bool Dd2Now => Dd2Hud.BarsDd2 && Dd2Bars.Ready;

        /// <summary>One tray per living hero of the party, in marching order.</summary>
        public void Rebuild(IReadOnlyList<uint> party)
        {
            _tooltip.Hide(this);
            // what the pointer was told on an icon of a tray that goes is taken down with it
            foreach (var tray in _trays)
            {
                foreach (var slot in tray.Left) _tooltip.Hide(slot);
                foreach (var slot in tray.Right) _tooltip.Hide(slot);
                foreach (var mark in tray.Marks) _tooltip.Hide(mark);
            }
            RaidUi.Clear(_root);
            _trays.Clear();
            if (!ReferenceEquals(party, _party))
            {
                _party.Clear();
                _party.AddRange(party);
            }
            _dd2 = Dd2Now;
            _rise = Dd2Hud.BarsRise;
            _size = Dd2Hud.Scale;
            var l = RaidLayout.Current;
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            for (var i = 0; i < _party.Count; i++)
            {
                var actor = library.GetLibraryElement(_party[i]);
                if (actor == null || !actor.IsLiving) continue;
                var tray = new Tray { Guid = _party[i], Index = i };
                tray.X = l.HeroStart.x + l.HeroSpacing.x * i;
                Build(tray, l);
                _trays.Add(tray);
            }
            _nextSearch = 0f;
            Follow();
        }

        private void Build(Tray tray, RaidLayout l)
        {
            var guid = tray.Guid;
            // The tray's own origin is the hero's place on the ground line: x is set every frame.
            tray.Root = UiKit.Rect("Tray" + guid, _root);
            tray.Root.PlaceTopLeft(new Vector2(tray.X, l.TrayY), RaidUi.TopLeft, Vector2.zero);

            var zone = UiKit.Image("Zone", tray.Root, null, Color.clear, true);
            tray.Zone = (RectTransform)zone.transform;
            tray.Zone.PlaceTopLeft(new Vector2(-Zone.x * 0.5f, l.PanelTop - l.TrayY - Zone.y), RaidUi.TopLeft, Zone);
            RaidUi.Pointer(zone, () => Clicked?.Invoke(guid, false), () => Clicked?.Invoke(guid, true));
            // the hero is carried to another's place with the left button
            var carry = zone.gameObject.AddComponent<RaidCarry>();
            carry.Threshold = DragStart;
            carry.CanBegin = () => _carried == 0u && (CanCarry == null || CanCarry());
            carry.Began = () =>
            {
                _carried = guid;
                _carriedOver = 0u;
                _tooltip.Hide(this);
            };
            carry.Moved = at => Carry(guid, at, false);
            carry.Ended = at => Carry(guid, at, true);

            if (_dd2 && BuildDd2(tray, l)) return;
            BuildDd1(tray, l);
        }

        // ---- DD2's look ------------------------------------------------------------------------------------

        // Where the top of DD2's panel stands on the dungeon's screen (y down from its top): the fight's line
        // (the fight's HUD is smaller on a display narrower than 16:9, and stands on the display's foot), and
        // the rise above it.
        private float Dd2Top => 1080f - (Dd2Bars.FightTopFromFoot + _rise) * _size;

        // The top of DD2's health bar on the screen: where the tooltips stand.
        private float Dd2BarTop => Dd2Top + (Dd2Bars.BarMiddle - Dd2Bars.BarHeight * 0.5f) * _size;

        /// <summary>
        /// The top of the health bars on the dungeon's screen (y down from its top), in the look on screen: what
        /// is drawn around a hero's bars (the marks of "move") stands by this line.
        /// </summary>
        public float BarTop => _dd2 ? Dd2BarTop : RaidLayout.Current.TrayY + RaidLayout.Current.TrayHealthOffset.y;

        private bool BuildDd2(Tray tray, RaidLayout l)
        {
            var view = Dd2Bars.Create(tray.Root);
            if (view == null) return false;
            var guid = tray.Guid;
            tray.Dd2 = view;
            view.Root.anchoredPosition = new Vector2(0f, l.TrayY - Dd2Top);
            // DD2's own area over the bar and the pips tells health and stress, in DD1's two lines
            if (view.BarsArea != null)
                RaidUi.Pointer(view.BarsArea, () => Clicked?.Invoke(guid, false), () => Clicked?.Invoke(guid, true), inside =>
                {
                    if (inside) _tooltip.Show(this, Words(guid), new Vector2(tray.X, Dd2BarTop - Dd2TooltipRise), TooltipWidth, above: true, centred: true);
                    else _tooltip.Hide(this);
                });
            // FALLBACK: the fight's selection marks are not to be had: DD1's brackets
            if (!view.HasSelectionMarks) Brackets(tray, l);
            return true;
        }

        // What a mark of DD2's row is drawn with for an icon: the game's own mark, a picture of DD2's, or
        // DD1's little picture on DD2's token. Null while a picture of DD2's is still on its way.
        private static string ArtFor(TrayIcon icon, Dd2Bars.View view)
        {
            if (!Dd1Pictures)
            {
                if (icon.Dd2Own == "deathsdoor" && view.DeathsDoor != null) return "own";
                if (icon.Dd2Own == "recovery" && view.Recovery != null) return "own";
                if (icon.Dd2Art != null)
                    foreach (var art in icon.Dd2Art)
                    {
                        if (art == "buff" || art == "debuff")
                        {
                            if ((art == "buff" ? Dd2Bars.BuffSprite : Dd2Bars.DebuffSprite) != null) return art;
                            continue;
                        }
                        if (Dd2Hud.Picture(art, out var pending) != null) return art;
                        if (pending) return null;
                    }
            }
            return "dd1";
        }

        private Mark AddMark(Tray tray, TrayIcon icon, string art)
        {
            var view = tray.Dd2;
            Dd2Bars.Mark part;
            if (art == "own") part = icon.Dd2Own == "deathsdoor" ? view.DeathsDoor : view.Recovery;
            else if (art == "dd1")
            {
                var picture = RaidUi.Sprite(icon.Art);
                // DD1's picture at its own 24 px in DD2's row, bare as DD2's own signs are: nothing is scaled
                part = view.AddMark(picture, IconArt, picture == null);
                // without DD1's picture a square in the row's colour still says that something is there
                if (part != null && picture == null && part.Graphic != null) part.Graphic.color = icon.Left ? UiKit.Harmful : UiKit.Notable;
            }
            else
            {
                var picture = art == "buff" ? Dd2Bars.BuffSprite : art == "debuff" ? Dd2Bars.DebuffSprite : Dd2Hud.Picture(art, out _);
                part = view.AddMark(picture, icon.Dd2Size, icon.Dd2Plate);
            }
            if (part == null || part.Rect == null) return null;
            var mark = new Mark { Icon = icon, Part = part, Art = art };
            part.Rect.gameObject.SetActive(true);
            part.Rect.localScale = Vector3.one;
            var guid = tray.Guid;
            if (part.Graphic != null)
            {
                // one of the game's own marks is switched on again and again: what its pointer knew of the last time goes
                var old = part.Graphic.GetComponent<RaidPointer>();
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                RaidUi.Pointer(part.Graphic, () => Clicked?.Invoke(guid, false), () => Clicked?.Invoke(guid, true), inside =>
                {
                    if (inside) _tooltip.Show(mark, IconWords(guid, icon), new Vector2(tray.X, Dd2BarTop - Dd2TooltipRise), TooltipWidth, above: true, centred: true);
                    else _tooltip.Hide(mark);
                });
            }
            return mark;
        }

        private void RemoveMark(Tray tray, Mark mark)
        {
            _tooltip.Hide(mark);
            tray.Dd2.RemoveMark(mark.Part);
            tray.Marks.Remove(mark);
        }

        // The marks of DD2's row are those of the icons that hold now, in the icons' order.
        private void ShowMarks(Tray tray, List<TrayIcon> wanted)
        {
            for (var i = tray.Marks.Count - 1; i >= 0; i--)
            {
                var mark = tray.Marks[i];
                // gone, or to be drawn with something else now (a picture of DD2's has come)
                if (!wanted.Contains(mark.Icon) || ArtFor(mark.Icon, tray.Dd2) != mark.Art) RemoveMark(tray, mark);
            }
            var changed = false;
            foreach (var icon in wanted)
            {
                if (tray.Marks.Exists(m => ReferenceEquals(m.Icon, icon))) continue;
                var art = ArtFor(icon, tray.Dd2);
                if (art == null) continue;      // its picture is on its way
                var mark = AddMark(tray, icon, art);
                if (mark == null) continue;
                // a mark that comes while the tray is on screen swells and settles (DD1's status_bar_tray_icon_pulse)
                mark.Pulse = tray.Seen ? 0f : -1f;
                tray.Marks.Add(mark);
                changed = true;
            }
            if (!changed) return;
            tray.Marks.Sort((a, b) => Array.IndexOf(TrayIcons.All, a.Icon).CompareTo(Array.IndexOf(TrayIcons.All, b.Icon)));
            var parts = new List<Dd2Bars.Mark>();
            foreach (var mark in tray.Marks) parts.Add(mark.Part);
            tray.Dd2.Order(parts);
        }

        // ---- DD1's look ------------------------------------------------------------------------------------

        private void Brackets(Tray tray, RaidLayout l)
        {
            var bracket = new Vector2(-BracketArt.x * 0.5f, -BracketGap - BracketLine);
            tray.Target = RaidUi.Art("Target", tray.Root, "overlays/target_h_1.png", bracket, BracketArt);
            tray.Target.gameObject.SetActive(false);
            tray.Selected = RaidUi.Art("Selected", tray.Root, "overlays/selected_1.png", bracket, BracketArt);
            tray.Selected.gameObject.SetActive(false);
            // behind the bars
            tray.Target.transform.SetSiblingIndex(1);
            tray.Selected.transform.SetSiblingIndex(2);
        }

        private void BuildDd1(Tray tray, RaidLayout l)
        {
            var guid = tray.Guid;
            // DD1 tells health and stress over the bars themselves (status_bars: status_bar_tooltip_hot_area_offset
            // 50 -10, status_bar_tooltip_hot_area_height 35: an area a bar wide from 10 px above the bar's top;
            // status_bar_tooltip_offset 50 -12: over the bar's middle)
            var bars = UiKit.Image("Bars", tray.Root, null, Color.clear, true);
            ((RectTransform)bars.transform).PlaceTopLeft(new Vector2(l.TrayCharX + l.TrayBarsHot.x - l.TrayHealthWidth * 0.5f, l.TrayBarsHot.y), RaidUi.TopLeft, new Vector2(l.TrayHealthWidth, l.TrayBarsHotHeight));
            RaidUi.Pointer(bars, () => Clicked?.Invoke(guid, false), () => Clicked?.Invoke(guid, true), inside =>
            {
                if (inside) _tooltip.Show(this, Words(guid), new Vector2(tray.X + l.TrayCharX + l.TrayBarsTooltip.x, l.TrayY + l.TrayBarsTooltip.y), TooltipWidth, above: true, centred: true);
                else _tooltip.Hide(this);
            });

            Brackets(tray, l);

            // The bar is centred on the hero: char_x_offset takes the tray half a bar to the left,
            // health_bar_offset brings the bar's middle back.
            var width = l.TrayHealthWidth;
            var barAt = new Vector2(l.TrayCharX + l.TrayHealthOffset.x - width * 0.5f, l.TrayHealthOffset.y);
            var back = UiKit.Rect("HealthBack", tray.Root);
            back.PlaceTopLeft(barAt, RaidUi.TopLeft, new Vector2(width, l.TrayHealthHeight));
            back.gameObject.AddComponent<RawImage>().texture = Gradient(ref _healthBack, "tray_health_bar_background_top", new Color32(18, 18, 18, 255), "tray_health_bar_background_bottom", new Color32(45, 45, 45, 255));
            back.GetComponent<RawImage>().raycastTarget = false;
            tray.Health = UiKit.Rect("Health", tray.Root);
            tray.Health.PlaceTopLeft(barAt, RaidUi.TopLeft, new Vector2(width, l.TrayHealthHeight));
            tray.Health.gameObject.AddComponent<RawImage>().texture = Gradient(ref _healthFill, "tray_health_bar_default_current_top", new Color32(0xcd, 0, 0, 255), "tray_health_bar_default_current_bottom", new Color32(0x15, 0, 0, 255));
            tray.Health.GetComponent<RawImage>().raycastTarget = false;

            for (var i = 0; i < Pips; i++) tray.Pips[i] = UiKit.Image("Pip" + i, tray.Root, null);

            // The status icons: places that spread from the bar's middle to both sides. The first ends at
            // tray_icon_left_offset, the second starts at tray_icon_right_offset, the third stands left of the
            // first, and so on outwards; a step is a hot spot's width. What a hero wears is put in them in order,
            // the first in the first place. Seen in DD1's own frame (raid_trap_spotted_hover.png): a hero's one
            // icon stands with its middle 38 px from the bar's left end, a second one at 62; so an icon's 24 px
            // picture has its middle on the LEFT edge of its hot spot, not in the hot spot's middle.
            for (var place = 0; place < TrayIcons.All.Length; place++)
            {
                var left = place % 2 == 0;
                var row = left ? tray.Left : tray.Right;
                var x = left
                    ? l.TrayCharX + l.TrayIconLeft.x - row.Count * l.TrayIconLeftSpacing - l.TrayIconSize.x
                    : l.TrayCharX + l.TrayIconRight.x + row.Count * l.TrayIconRightSpacing;
                var slot = new Slot { At = new Vector2(x, left ? l.TrayIconLeft.y : l.TrayIconRight.y) };
                slot.Image = UiKit.Image((left ? "IconLeft" : "IconRight") + row.Count, tray.Root, null, Color.white, true);
                ((RectTransform)slot.Image.transform).PlaceTopLeft(slot.At + new Vector2(0f, l.TrayIconSize.y * 0.5f), RaidUi.Middle, IconArt);
                // DD1's files give an icon's tooltip no place of its own: it stands where the bars' does, over the tray
                RaidUi.Pointer(slot.Image, () => Clicked?.Invoke(guid, false), () => Clicked?.Invoke(guid, true), inside =>
                {
                    if (inside && slot.Icon != null) _tooltip.Show(slot, IconWords(guid, slot.Icon), new Vector2(tray.X, l.TrayY - TooltipRise), TooltipWidth, above: true, centred: true);
                    else _tooltip.Hide(slot);
                });
                slot.Image.gameObject.SetActive(false);
                row.Add(slot);
            }
        }

        private static string IconWords(uint guid, TrayIcon icon)
        {
            try
            {
                var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
                var words = actor != null ? icon.Words(actor, DungeonRun.Current) : null;
                return string.IsNullOrEmpty(words) ? icon.Key : words;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Dungeon HUD: no words for the status icon " + icon.Key + ": " + e.Message);
                return icon.Key;
            }
        }

        // The n-th place for an icon, counted from the tray's middle outwards: left, right, left...
        private static Slot IconPlace(Tray tray, int n) => n % 2 == 0 ? tray.Left[n / 2] : tray.Right[n / 2];

        // What stands in a place of a row; an icon that comes while the tray is on screen swells and settles
        // (status_bar_tray_icon_pulse).
        private void Fill(Slot slot, TrayIcon icon, bool isNew)
        {
            if (ReferenceEquals(slot.Icon, icon)) return;
            slot.Icon = icon;
            _tooltip.Hide(slot);
            slot.Pulse = icon != null && isNew ? 0f : -1f;
            slot.Image.transform.localScale = Vector3.one;
            if (icon == null)
            {
                slot.Image.gameObject.SetActive(false);
                return;
            }
            var sprite = RaidUi.Sprite(icon.Art);
            slot.Image.sprite = sprite;
            // without DD1's picture a square in the row's colour still says that something is there
            slot.Image.color = sprite != null ? Color.white : icon.Left ? UiKit.Harmful : UiKit.Notable;
            slot.Image.gameObject.SetActive(true);
        }

        // An icon that has just come swells to status_bar_tray_icon_pulse's size and settles; returns the time
        // gone since it came, below 0 once it is at rest.
        private static float Pulse(Transform icon, float since, float dt, RaidLayout l)
        {
            if (since < 0f || icon == null) return -1f;
            since += dt;
            var size = 1f;
            if (since < l.TrayIconPulseOut) size = Mathf.Lerp(1f, l.TrayIconPulseScale, since / Mathf.Max(0.001f, l.TrayIconPulseOut));
            else if (since < l.TrayIconPulseOut + l.TrayIconPulseIn) size = Mathf.Lerp(l.TrayIconPulseScale, 1f, (since - l.TrayIconPulseOut) / Mathf.Max(0.001f, l.TrayIconPulseIn));
            else since = -1f;
            icon.localScale = new Vector3(size, size, 1f);
            return since;
        }

        /// <summary>
        /// Where a point so far above a hero's feet (in DD1's pixels of the scene) is on the screen; false for a
        /// hero without a tray. While no model is to be found the hero stands in DD1's own place for the rank.
        /// </summary>
        public bool Place(uint guid, float height, out Vector2 at)
        {
            at = Vector2.zero;
            var l = RaidLayout.Current;
            foreach (var tray in _trays)
            {
                if (tray.Guid != guid) continue;
                at = new Vector2(tray.X, l.HeroStart.y - height);
                var camera = Camera.main;
                // the model hangs in a slot that the scene scales (a hero DD1's scripts bring up close is larger)
                var size = tray.Model != null && tray.Model.parent != null ? tray.Model.parent.lossyScale.y : 1f;
                if (tray.Model == null || camera == null || size < 0.001f) return true;
                var point = camera.WorldToScreenPoint(tray.Model.position + Vector3.up * (height * CorridorNumbers.UnitScale * size));
                if (point.z <= 0f) return true;
                var scale = _screen.lossyScale.x > 0f ? _screen.lossyScale.x : 1f;
                at = new Vector2((point.x - UnityEngine.Screen.width * 0.5f) / scale + 960f, 1080f - point.y / scale);
                return true;
            }
            return false;
        }

        // Where something of a tray is on DD1's screen (its middle; y down from the top).
        private Vector2 PointOf(Transform part)
        {
            var local = _screen.InverseTransformPoint(part is RectTransform rect ? rect.TransformPoint(rect.rect.center) : part.position);
            return new Vector2(local.x + 960f, 1080f - local.y);
        }

        /// <summary>
        /// Dev bridge (hud.bars): where every hero and the parts under them stand on the display, in pixels of
        /// the frame, each part as left, right, middle, top, bottom; "Off" is a part's middle from the bar's.
        /// </summary>
        public object Measure()
        {
            var camera = Camera.main;
            var rows = new List<object>();
            foreach (var tray in _trays)
            {
                var hero = tray.Model != null && camera != null ? Mathf.Round(camera.WorldToScreenPoint(tray.Model.position).x * 10f) / 10f : float.NaN;
                var view = tray.Dd2;
                var bar = view != null ? DungeonLookDev.Box(view.BarPart) : DungeonLookDev.Box(tray.Health);
                var pips = view != null ? DungeonLookDev.Around(view.PipRowPart) : null;
                var row = view != null ? DungeonLookDev.Box(view.MarkRowPart) : null;
                var selected = view != null ? DungeonLookDev.Box(view.SelectedPart) : null;
                var target = view != null ? DungeonLookDev.Box(view.TargetPart) : null;
                rows.Add(new
                {
                    guid = tray.Guid, look = view != null ? "dd2" : "dd1", hero, trayX = tray.X,
                    panel = view != null ? DungeonLookDev.Box(view.Root) : null,
                    bar, pips, pipsOff = Off(pips, bar),
                    marksRow = row, marksRowOff = Off(row, bar), marks = view != null ? DungeonLookDev.Around(view.MarkRowPart) : null,
                    selected, selectedOff = Off(selected, bar), target, targetOff = Off(target, bar),
                    barOffHero = bar != null && !float.IsNaN(hero) ? (object)(Mathf.Round((bar[2] - hero) * 10f) / 10f) : null
                });
            }
            return rows;
        }

        private static object Off(float[] part, float[] bar)
        {
            return part != null && bar != null ? (object)(Mathf.Round((part[2] - bar[2]) * 10f) / 10f) : null;
        }

        /// <summary>Dev bridge: the trays, and what stands over (DD1) or under (DD2) their bars.</summary>
        public object Describe()
        {
            var l = RaidLayout.Current;
            var trays = new List<object>();
            foreach (var tray in _trays)
            {
                var icons = new List<object>();
                foreach (var row in new[] { tray.Left, tray.Right })
                    foreach (var slot in row)
                        if (slot.Icon != null)
                            icons.Add(new { key = slot.Icon.Key, row = slot.Icon.Left ? "left" : "right", at = new[] { tray.X + slot.At.x, l.TrayY + slot.At.y }, art = RaidUi.Sprite(slot.Icon.Art) != null });
                foreach (var mark in tray.Marks)
                {
                    var at = PointOf(mark.Part.Rect);
                    icons.Add(new { key = mark.Icon.Key, row = "dd2", at = new[] { at.x, at.y }, art = mark.Part.Picture != null && mark.Part.Picture.sprite != null, drawn = mark.Art });
                }
                trays.Add(new
                {
                    guid = tray.Guid, x = tray.X, y = l.TrayY, model = tray.Model != null, shown = tray.Root.gameObject.activeSelf, icons,
                    look = tray.Dd2 != null ? "dd2" : "dd1",
                    dd2 = tray.Dd2?.Describe(),
                    barTop = tray.Dd2 != null ? Dd2BarTop : l.TrayY + l.TrayHealthOffset.y
                });
            }
            return trays;
        }

        // A health bar is a vertical blend of two colours of colours/base.colours.darkest.
        private static Texture2D Gradient(ref Texture2D cache, string top, Color topFallback, string bottom, Color bottomFallback)
        {
            if (cache != null) return cache;
            const int height = 16;
            cache = new Texture2D(1, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var a = Dd1Fonts.Colour(top, topFallback);
            var b = Dd1Fonts.Colour(bottom, bottomFallback);
            for (var y = 0; y < height; y++) cache.SetPixel(0, y, Color.Lerp(b, a, y / (height - 1f)));
            cache.Apply();
            return cache;
        }

        private static string Words(uint guid)
        {
            var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
            if (actor == null) return "";
            var health = RaidText.Format(RaidText.Get("tray_status_bar_tooltip_health_format", "HP: %d/%d"), Mathf.RoundToInt(actor.HpRounded), Mathf.RoundToInt(actor.CurrentHpMax));
            var stress = RaidText.Format(RaidText.Get("tray_status_bar_tooltip_stress_format", "Stress: %d/%d"), Mathf.RoundToInt(actor.Stress), Mathf.RoundToInt(actor.StressMax));
            // DD1's two lines, the first in its colour for health; no name over them
            return "<color=" + RaidText.Hex(Dd1Fonts.Colour("tray_status_bar_tooltip_health", UiKit.Harmful)) + ">" + health + "</color>\n" + stress;
        }

        /// <summary>
        /// Off while heroes change places (CorridorView.RanksMoving): two trays then pass through one another with
        /// their heroes, instead of the one pushing the other aside and the two leaping as the heroes cross.
        /// </summary>
        public bool KeepApart = true;

        /// <summary>Every frame: the trays stand under their heroes.</summary>
        public void Follow()
        {
            // the look asked for is not the one on screen (DD2's panel has come, or the option was changed): the trays are made again
            if (_party.Count > 0 && (_dd2 != Dd2Now || (_dd2 && (!Mathf.Approximately(_rise, Dd2Hud.BarsRise) || !Mathf.Approximately(_size, Dd2Hud.Scale)))))
            {
                Rebuild(_party);
                return;
            }
            if (_trays.Count == 0) return;
            var l = RaidLayout.Current;
            if (Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + ModelSearch;
                foreach (var tray in _trays) tray.Model = null;
                foreach (var model in UnityEngine.Object.FindObjectsOfType<ActorBhv>())
                {
                    if (model == null || !model.isActiveAndEnabled) continue;
                    var guid = model.GetActorGuid();
                    foreach (var tray in _trays)
                        if (tray.Guid == guid && tray.Model == null) tray.Model = model.transform;
                }
            }

            var camera = Camera.main;
            var scale = _screen.lossyScale.x > 0f ? _screen.lossyScale.x : 1f;
            foreach (var tray in _trays)
            {
                var x = l.HeroStart.x + l.HeroSpacing.x * tray.Index;       // FALLBACK: DD1's own place for the rank
                if (tray.Model != null && camera != null)
                {
                    var point = camera.WorldToScreenPoint(tray.Model.position);
                    if (point.z > 0f) x = (point.x - UnityEngine.Screen.width * 0.5f) / scale + 960f;
                }
                tray.X = x;
            }

            // keep the trays apart, the leftmost first
            _trays.Sort((a, b) => a.X.CompareTo(b.X));
            for (var i = 1; KeepApart && i < _trays.Count; i++)
                if (_trays[i].X - _trays[i - 1].X < MinStep) _trays[i].X = _trays[i - 1].X + MinStep;
            var dt = Time.unscaledDeltaTime;
            foreach (var tray in _trays)
            {
                // DD1's pixel pictures stand on whole pixels; DD2's panel stands where its hero stands, as a fight's does
                tray.Root.anchoredPosition = new Vector2(tray.Dd2 != null ? tray.X : Mathf.Round(tray.X), -l.TrayY);
                foreach (var slot in tray.Left) slot.Pulse = Pulse(slot.Image.transform, slot.Pulse, dt, l);
                foreach (var slot in tray.Right) slot.Pulse = Pulse(slot.Image.transform, slot.Pulse, dt, l);
                foreach (var mark in tray.Marks) mark.Pulse = Pulse(mark.Part.Rect, mark.Pulse, dt, l);
                tray.Dd2?.Tick(dt);
            }
        }

        private static void Show(Image mark, bool on)
        {
            if (mark != null && mark.gameObject.activeSelf != on) mark.gameObject.SetActive(on);
        }

        // The mark of a place that may be chosen: DD2's green indicator, or DD1's bracket.
        private static void ShowTarget(Tray tray, bool on)
        {
            if (tray.Dd2 != null && tray.Dd2.HasSelectionMarks) tray.Dd2.Target = on;
            else Show(tray.Target, on);
        }

        /// <summary>A few times a second: health, stress, who is selected, who may be chosen.</summary>
        public void Refresh(uint selected, Func<uint, bool> target)
        {
            var l = RaidLayout.Current;
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            var run = DungeonRun.Current;
            var wanted = new List<TrayIcon>();
            foreach (var tray in _trays)
            {
                var actor = library.GetLibraryElement(tray.Guid);
                var alive = actor != null && actor.IsLiving;
                tray.Root.gameObject.SetActive(alive);
                if (!alive) continue;
                var stress = Mathf.RoundToInt(Pips * Mathf.Clamp01(actor.Stress / Mathf.Max(1f, actor.StressMax)));
                var isSelected = tray.Guid == selected;
                // the mark of a place that may be chosen: a companion a camping skill may go to, or the hero
                // whose rank a carried hero would take
                var isTarget = _carried != 0u ? tray.Guid == _carriedOver : !isSelected && target != null && target(tray.Guid);

                // what lasts on the hero now, in the icons' order
                wanted.Clear();
                DevIcons.TryGetValue(tray.Guid, out var forced);
                foreach (var icon in TrayIcons.All)
                {
                    bool on;
                    try { on = (forced != null && forced.Contains(icon.Key)) || icon.Shows(actor, run); }
                    catch (Exception) { on = false; }
                    if (on) wanted.Add(icon);
                }

                if (tray.Dd2 != null)
                {
                    // ActorInfoUiBhv.Refresh: the bar is the health with the wounded part at its end
                    var whole = Mathf.Max(1f, actor.DisplayedHpMax + actor.WoundedHp);
                    tray.Dd2.SetHealth(Mathf.Max(0f, actor.DisplayedHp) / whole, actor.WoundedHp > 0f ? actor.WoundPercent : 0f, tray.Seen);
                    tray.Dd2.SetStress(stress);
                    if (tray.Dd2.HasSelectionMarks) tray.Dd2.Selected = isSelected;
                    else Show(tray.Selected, isSelected);
                    ShowTarget(tray, isTarget);
                    ShowMarks(tray, wanted);
                    tray.Seen = true;
                    continue;
                }

                var share = Mathf.Clamp01(actor.HpRounded / Mathf.Max(1f, actor.CurrentHpMax));
                tray.Health.sizeDelta = new Vector2(l.TrayHealthWidth * share, l.TrayHealthHeight);
                for (var i = 0; i < Pips; i++)
                {
                    var full = i < stress;
                    var pip = tray.Pips[i];
                    var sprite = RaidUi.Sprite(full ? "overlays/stress_pip_full.png" : "overlays/stress_pip_empty.png");
                    if (pip.sprite != sprite) pip.sprite = sprite;
                    pip.color = sprite != null ? Color.white : full ? new Color(0.86f, 0.86f, 0.9f) : new Color(0.14f, 0.14f, 0.14f);
                    // the lit pip is a pixel shorter at either end than the socket it sits in
                    var at = new Vector2(l.TrayCharX + l.TrayStressOffset.x + i * l.TrayStressSpacing, l.TrayStressOffset.y + (full ? 1f : 0f));
                    ((RectTransform)pip.transform).PlaceTopLeft(at, RaidUi.TopLeft, full ? PipFull : PipEmpty);
                }
                Show(tray.Selected, isSelected);
                Show(tray.Target, isTarget);

                // the status icons: the places filled from the tray's middle outwards with what holds now
                for (var n = 0; n < wanted.Count; n++) Fill(IconPlace(tray, n), wanted[n], tray.Seen);
                for (var rest = wanted.Count; rest < tray.Left.Count + tray.Right.Count; rest++) Fill(IconPlace(tray, rest), null, false);
                tray.Seen = true;
            }
        }
    }

    /// <summary>
    /// DD1's quest info in the top left corner (screen.raid.darkest, quest_info): the quest's scroll with its
    /// name and goal beside it and the retreat flag under it; once the goal is met, DD1's seal with "Quest
    /// Complete!" in their place. A click on the flag asks DD1's question before the quest is abandoned; a
    /// click on the seal offers DD1's choice in the middle of the screen: return to the hamlet, or go on.
    /// </summary>
    internal class RaidQuestInfo
    {
        private static readonly Vector2 LogArt = new Vector2(80f, 80f);         // overlays/quest_log.png
        private static readonly Vector2 GlowArt = new Vector2(374f, 158f);      // overlays/quest_log_glow.png
        private static readonly Vector2 RetreatArt = new Vector2(64f, 64f);     // panels/retreat_button.png
        private static readonly Vector2 SealArt = new Vector2(120f, 152f);      // overlays/quest_complete.png
        private static readonly Vector2 FrameArt = new Vector2(619f, 136f);     // panels/quest_complete_choice_shared_frame.png
        private static readonly Vector2 ChoiceArt = new Vector2(124f, 69f);     // panels/quest_return_to_hamlet.png, quest_continue_raid.png, scrolls/choice_button_frame.png
        // fx/quest_complete_seal: its "seals" picture (the wax and its ribbons) from the sprite's root, y down
        private static readonly Rect SealHot = new Rect(-60f, -38f, 115f, 149f);
        private const string CrestSprite = "fx/quest_complete_crest/quest_complete_crest.sprite";

        // The mod's own numbers.
        private const float NameWidth = 384f;               // a quest's name may run past quest_info.size: up to the torch's strip
        private const float NameSmallest = 20f;
        // DD1 blurs the whole screen behind the choice over quest_info.complete_blur_time, the panels with the
        // scene, and does not darken it (its own frames, raid_quest_complete_*.png: the side decor is as light as
        // in any other frame, and its edges are those of the retreat question's blur). As under that question
        // it is a blurred picture of the frame as it stands (RaidConfirm), which comes over the screen in that
        // time. FALLBACK, when no picture can be had: the scene blurred as DD1's scripts blur it, and a shade.
        private const float ChoiceShade = 0.5f;
        private const float ChoiceBlur = 4f;                // in the units of DD1's scripts (layers.A_blurriness: 3 behind a curio, 5 behind a trap)

        private readonly RaidTooltip _tooltip;
        private readonly RectTransform _screen;
        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly GameObject _open, _done;
        private readonly TextMeshProUGUI _name, _goal;
        private readonly Button _retreat;
        private readonly SpineView _seal;
        private readonly MonoBehaviour _host;
        private RectTransform _choice;
        private Image _shade;
        private RawImage _behind;
        private Texture2D _behindPicture;
        // the choice is asked for and waits for the frame's end, when the picture behind it is taken
        private bool _waiting;
        private SpineView _crest;
        private float _choiceSince, _closing = -1f;
        private bool _sealShown;
        // DD1's camp bonus icons beside the flag, and what they show
        private readonly RectTransform _bonus;
        private string _bonusKey = "";

        /// <summary>The player means to abandon the quest (the flag was clicked and the question answered yes).</summary>
        public Action Leave;
        /// <summary>The flag was clicked: ask before leaving.</summary>
        public Action AskRetreat;
        /// <summary>DD1's banner over the choice ("Quest Complete!"): put up, and taken down again.</summary>
        public Action<bool> Banner;

        /// <summary>True while the choice waits for an answer (not while it is only going away).</summary>
        public bool ChoiceOpen => _choice != null && _closing < 0f;

        /// <param name="host">Whose coroutine waits for the frame's end to take the picture behind the choice.</param>
        public RaidQuestInfo(RectTransform screen, RaidTooltip tooltip, MonoBehaviour host = null)
        {
            _screen = screen;
            _tooltip = tooltip;
            _host = host;
            var l = RaidLayout.Current;
            var root = _root = UiKit.Rect("QuestInfo", screen);
            root.PlaceTopLeft(l.QuestPos, RaidUi.TopLeft, l.QuestSize);
            _group = root.gameObject.AddComponent<CanvasGroup>();

            // ---- the quest in progress ----
            var open = UiKit.Rect("Open", root).PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, Vector2.zero);
            _open = open.gameObject;
            RaidUi.Art("Glow", open, "overlays/quest_log_glow.png", l.QuestGlow, GlowArt);
            // info_button_offset and retreat_button_offset are the middles of their pictures
            RaidUi.Art("Log", open, "overlays/quest_log.png", l.QuestButton - LogArt * 0.5f, LogArt);
            _name = RaidUi.Label("Name", open, "raid_quest_info_name", l.QuestName, new Vector2(NameWidth, 44f));
            RaidUi.Fit(_name, NameSmallest);
            _goal = RaidUi.Label("Goal", open, "raid_quest_info_goals", l.QuestGoals, new Vector2(NameWidth, 28f));
            _goal.richText = true;
            RaidUi.Fit(_goal, 13f);
            var flagAt = l.QuestRetreat - RetreatArt * 0.5f;
            _retreat = RaidUi.ArtButton("Retreat", open, "panels/retreat_button.png", flagAt, () => AskRetreat?.Invoke(), RetreatArt);
            RaidUi.Pointer(_retreat.targetGraphic, null, null, inside =>
            {
                // DD1's words for the flag, in its colour raid_retreat_tooltip
                if (inside) _tooltip.Show(this, "<color=" + RaidText.Hex(Dd1Fonts.Colour("raid_retreat_tooltip", UiKit.Neutral)) + ">" + RaidText.Get("retreat_raid_tooltip", "Abandon Quest") + "</color>",
                    l.QuestPos + l.QuestRetreat + l.QuestRetreatTooltip, 260f);
                else _tooltip.Hide(this);
            });

            // DD1's camp bonus icons: what the last camp gave the party as a whole, beside the flag (ShowCampBonus)
            _bonus = UiKit.Rect("CampBonus", root).PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, Vector2.zero);

            // ---- the quest complete ----
            // DD1's seal in the corner (fx/quest_complete_seal: it comes with "appear" and glows on in
            // "idle_exploration"), where the quest's scroll was, with "Quest Complete!" beside it. GUESS: where
            // the sprite's root stands; quest_info.complete_button_offset (0 30) is read as counted from the
            // scroll's own middle (info_button_offset), which puts the wax over the scroll's place and its
            // ribbons over the flag's. Without the sprite the still picture overlays/quest_complete.png stands there.
            var done = UiKit.Rect("Done", root).PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, Vector2.zero);
            _done = done.gameObject;
            var sealAt = l.QuestButton + l.QuestCompleteButton;
            _seal = SpineView.Create("Seal", done, "fx/quest_complete_seal/quest_complete_seal.sprite");
            if (_seal != null) _seal.Rect.PlaceTopLeft(sealAt, RaidUi.Middle, Vector2.zero);
            else RaidUi.Art("SealArt", done, "overlays/quest_complete.png", sealAt + SealHot.position, SealArt);
            // the wax and its ribbons take the click (the sprite's "seals" picture: 115x149 around its root)
            var hit = UiKit.Image("SealHit", done, null, Color.clear, true);
            ((RectTransform)hit.transform).PlaceTopLeft(sealAt + SealHot.position, RaidUi.TopLeft, SealHot.size);
            RaidUi.Pointer(hit, () => { if (!ChoiceOpen) ShowChoice(); }, null, inside =>
            {
                if (inside) _tooltip.Show(this, RaidText.Get("str_return_to_hamlet_tooltip", "Return to Hamlet"), l.QuestPos + sealAt + new Vector2(SealHot.xMax, 0f), 260f);
                else _tooltip.Hide(this);
            });
            RaidUi.Label("Complete", done, "raid_quest_info_complete", l.QuestCompleteText, new Vector2(NameWidth, 44f)).text = RaidText.Get("raid_quest_complete", "Quest Complete!");
            _done.SetActive(false);
        }

        /// <summary>Moves the corner's content to the right by so much (a display narrower than DD1's screen cuts the corner off).</summary>
        public void Shift(float x)
        {
            var l = RaidLayout.Current;
            _root.anchoredPosition = new Vector2(l.QuestPos.x + x, -l.QuestPos.y);
        }

        /// <summary>DD1 takes the quest's words away while the party goes through a door.</summary>
        public bool Visible
        {
            set { if (_root != null && _root.gameObject.activeSelf != value) _root.gameObject.SetActive(value); }
        }

        /// <summary>
        /// How much of the corner is drawn, 1 all .. 0 nothing: DD1's scripts take the HUD over the scene away while
        /// a hero is shown close (layers.HUD_alpha).
        /// </summary>
        public float Alpha
        {
            set
            {
                if (!Mathf.Approximately(_group.alpha, value)) _group.alpha = value;
                _group.blocksRaycasts = value > 0.5f;
            }
        }

        // DD1 shows what a camp gave the party beside the retreat flag: the camping skill's own icon at half
        // its size, a row from quest_info.camp_bonus_icon_offset (140 110) every camp_bonus_icon_spacing (50),
        // with the skill's words for the pointer (camp_bonus_icon_tooltip_max_width 240). GUESS: which skills
        // stand there. Read here as the ones whose gift is the party's and not one hero's (scouting, the two
        // chances of a surprise): what a hero carries has DD1's own icon over that hero's bar. GUESS: the
        // tooltip hangs under its icon.
        private void ShowCampBonus(DungeonRun run)
        {
            var skills = new List<CampSkill>();
            var key = new System.Text.StringBuilder();
            var ledger = run.CampLedger;
            if (ledger != null && ledger.Any)
            {
                var rules = CampContent.Rules;
                foreach (var entry in ledger.Entries)
                {
                    var kind = CampingBuffMap.KindOf(rules.Buff(entry.BuffId));
                    if (kind != CampBuffKind.Scouting && kind != CampBuffKind.PartySurprise && kind != CampBuffKind.MonstersSurprise) continue;
                    var skill = rules.Skill(entry.SkillId);
                    if (skill == null || skills.Contains(skill)) continue;
                    skills.Add(skill);
                    key.Append(skill.Id).Append('|');
                }
            }
            if (key.ToString() == _bonusKey) return;
            _bonusKey = key.ToString();
            _tooltip.Hide(_bonus);
            RaidUi.Clear(_bonus);
            var l = RaidLayout.Current;
            for (var i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                var art = CampContent.Icon(skill);
                var size = (art != null ? art.rect.size : new Vector2(72f, 72f)) * l.CampBonusScale;
                var at = l.CampBonusIcon + l.CampBonusSpacing * i;
                var icon = UiKit.Image("Bonus" + i, _bonus, art, art != null ? Color.white : new Color(0.25f, 0.2f, 0.12f), true);
                ((RectTransform)icon.transform).PlaceTopLeft(at, RaidUi.TopLeft, size);
                var words = RaidTooltip.Titled(CampText.Name(skill), CampText.Describe(skill));
                RaidUi.Pointer(icon, null, null, inside =>
                {
                    if (inside) _tooltip.Show(_bonus, words, l.QuestPos + at + new Vector2(0f, size.y + 4f), l.CampBonusTooltipWidth);
                    else _tooltip.Hide(_bonus);
                });
            }
        }

        /// <summary>Dev bridge: the camping skills whose icons stand beside the flag.</summary>
        public string CampBonus => _bonusKey;

        public void Show(DungeonRun run)
        {
            ShowCampBonus(run);
            var complete = run.Exploration.ObjectiveComplete;
            if (_open.activeSelf == complete) _open.SetActive(!complete);
            // DD1's seal takes the scroll's place in the corner once the choice it was first offered in is over
            var seal = complete && _choice == null;
            if (_done.activeSelf != seal) _done.SetActive(seal);
            if (seal && !_sealShown && _seal != null) _seal.Play("appear", "idle_exploration");
            _sealShown = seal;
            if (complete) return;
            if (_name.text != run.Title) _name.text = run.Title;
            var goal = run.GoalLine;
            if (_goal.text != goal) _goal.text = goal;
            _retreat.interactable = !run.Busy && run.Exploration.CanLeave;
        }

        /// <summary>
        /// DD1's choice once the quest is complete (its own frame: _lab/dd1_ref/raid/raid_quest_complete_room.png):
        /// the screen blurred and darkened, "Quest Complete!" in the banner under the torch, the crest (fx/quest_complete_crest)
        /// with its root on quest_info.complete_mid_screen_pos, the frame hanging 55 under it and the two ways
        /// in the frame. All of quest_info's offsets are counted from quest_info.pos, this one too: the crest's
        /// root stands at 960,610 of the screen, which is where the frame measures it.
        /// </summary>
        public void ShowChoice()
        {
            EndChoice();
            var l = RaidLayout.Current;
            var mid = l.QuestPos + l.CompleteMid;
            // the picture of the screen behind, across the whole display, under the choice (see RaidConfirm)
            var blur = RaidConfirm.Blur && _host != null && _host.isActiveAndEnabled;
            if (blur)
            {
                var behind = UiKit.Rect("QuestCompleteBehind", _screen);
                behind.anchorMin = behind.anchorMax = behind.pivot = new Vector2(0.5f, 0f);
                behind.anchoredPosition = Vector2.zero;
                behind.sizeDelta = _screen.parent is RectTransform canvas ? canvas.rect.size : new Vector2(1920f, 1080f);
                _behind = behind.gameObject.AddComponent<RawImage>();
                _behind.raycastTarget = false;
                _behind.color = Color.clear;
                _tooltip.HideAll();
            }
            _choice = UiKit.Rect("QuestComplete", _screen);
            _choice.PlaceTopLeft(mid, RaidUi.TopLeft, Vector2.zero);
            _choiceSince = 0f;
            _closing = -1f;
            // nothing behind the choice is clicked; it is left by one of its two ways only
            _shade = UiKit.Image("Shade", _choice, null, new Color(0f, 0f, 0f, 0f), true);
            ((RectTransform)_shade.transform).PlaceTopLeft(new Vector2(-mid.x - 2000f, -mid.y), RaidUi.TopLeft, new Vector2(1920f + 4000f, 1080f));
            _crest = SpineView.Create("Crest", _choice, CrestSprite);
            if (_crest != null)
            {
                _crest.Rect.PlaceTopLeft(Vector2.zero, RaidUi.Middle, Vector2.zero);
                _crest.Play("appear", "idle");
            }
            // FALLBACK: without the sprite, the still seal on the point
            else RaidUi.Art("Seal", _choice, "overlays/quest_complete.png", -SealArt * 0.5f, SealArt);
            // the frame hangs from the middle of its top edge
            RaidUi.Art("Frame", _choice, "panels/quest_complete_choice_shared_frame.png", l.CompleteFrame - new Vector2(FrameArt.x * 0.5f, 0f), FrameArt, new Color(0.04f, 0.03f, 0.03f, 0.95f), true);
            Way("Return", "panels/quest_return_to_hamlet.png", l.CompleteReturn, RaidText.Get("str_return_to_hamlet_tooltip", "Return to Hamlet"), () =>
            {
                HideChoice();
                Leave?.Invoke();
            });
            Way("Continue", "panels/quest_continue_raid.png", l.CompleteContinue, RaidText.Get("str_continue_raid_tooltip", "Continue Adventuring"), HideChoice);
            if (!blur)
            {
                Banner?.Invoke(true);
                return;
            }
            // the choice and its banner come when the picture has been taken: they are not to be in it
            _waiting = true;
            _choice.gameObject.SetActive(false);
            _host.StartCoroutine(TakeBehind(_choice));
        }

        private IEnumerator TakeBehind(RectTransform choice)
        {
            yield return new WaitForEndOfFrame();
            if (choice == null || _choice != choice) yield break;       // put away in the meantime
            try { _behindPicture = Estate.TutorialPopup.BlurredScreen(Estate.TutorialPopup.BlurSigma, "DD2Estate.QuestCompleteBehind"); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: the screen behind the quest's choice could not be blurred: " + e.Message); }
            if (_behindPicture != null) _behind.texture = _behindPicture;
            else
            {
                UnityEngine.Object.Destroy(_behind.gameObject);
                _behind = null;
            }
            _waiting = false;
            choice.gameObject.SetActive(true);
            Banner?.Invoke(true);
        }

        // One of the two ways: DD1's scroll button (its picture over the two rails of scrolls/choice_button_frame.png,
        // at choice_button.frame_offset from the layout's position). DD1 says what it is in a tooltip under it
        // (complete_tooltip_offset, complete_tooltip_width) in the style quest_complete_tooltip.
        private void Way(string name, string art, Vector2 at, string words, Action click)
        {
            var l = RaidLayout.Current;
            var corner = l.ChoiceCorner(at, ChoiceArt.x);
            RaidUi.Art(name + ".Frame", _choice, "scrolls/choice_button_frame.png", corner, ChoiceArt);
            var button = RaidUi.ArtButton(name, _choice, art, corner, click, ChoiceArt);
            var under = l.QuestPos + l.CompleteMid + corner + new Vector2(ChoiceArt.x * 0.5f, ChoiceArt.y) + l.CompleteTooltipOffset;
            RaidUi.Pointer(button.targetGraphic, null, null, inside =>
            {
                if (inside && ChoiceOpen) _tooltip.Show(button, words, under, l.CompleteTooltipWidth, centred: true, style: "quest_complete_tooltip");
                else _tooltip.Hide(button);
            });
        }

        /// <summary>
        /// The choice goes: the crest leaves as DD1's sprite does ("disappear"), the blur and the dark lift, and
        /// the seal comes up in the corner.
        /// </summary>
        public void HideChoice()
        {
            if (_choice == null || _closing >= 0f) return;
            if (_waiting)
            {
                // not yet on screen: nothing to go
                _closing = 0f;
                EndChoice();
                return;
            }
            _tooltip.HideAll();
            Banner?.Invoke(false);
            _closing = 0f;
            // the two ways and the frame go at once; the crest has its own going
            for (var i = _choice.childCount - 1; i >= 0; i--)
            {
                var child = _choice.GetChild(i);
                if (child != _shade.transform && (_crest == null || child != _crest.transform)) child.gameObject.SetActive(false);
            }
            _shade.raycastTarget = false;
            if (_crest != null && _crest.Has("disappear")) _crest.Play("disappear", false);
            else EndChoice();
        }

        /// <summary>The choice is gone at once (the expedition that offered it is over, the screen is put away).</summary>
        public void EndChoice()
        {
            if (_choice == null) return;
            if (_closing < 0f) Banner?.Invoke(false);
            _tooltip.HideAll();
            _choice.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_choice.gameObject);
            _choice = null;
            _shade = null;
            _crest = null;
            _closing = -1f;
            _waiting = false;
            if (_behind != null) UnityEngine.Object.Destroy(_behind.gameObject);
            _behind = null;
            if (_behindPicture != null) UnityEngine.Object.Destroy(_behindPicture);
            _behindPicture = null;
            CorridorView.ExtraBlur = 0f;
        }

        /// <summary>Dev bridge: true while DD1's blurred picture of the screen lies under the choice.</summary>
        public bool ChoiceBlurred => _choice != null && _behind != null && _behindPicture != null;

        /// <summary>Every frame: the blur and the dark behind the choice come over complete_blur_time and go the same way.</summary>
        public void Tick(float dt)
        {
            if (_choice == null || _waiting) return;
            var l = RaidLayout.Current;
            var time = Mathf.Max(0.01f, l.CompleteBlurTime);
            float share;
            if (_closing < 0f)
            {
                _choiceSince += dt;
                share = Mathf.Clamp01(_choiceSince / time);
            }
            else
            {
                _closing += dt;
                share = 1f - Mathf.Clamp01(_closing / time);
                var gone = _crest == null || !_crest.Has("disappear") ? time : Mathf.Max(time, _crest.Duration("disappear"));
                if (_closing >= gone)
                {
                    EndChoice();
                    return;
                }
            }
            if (_behind != null)
            {
                // the blurred picture comes over the screen, and lifts off it again
                _behind.color = new Color(1f, 1f, 1f, share);
                return;
            }
            _shade.color = new Color(0f, 0f, 0f, ChoiceShade * share);
            CorridorView.ExtraBlur = ChoiceBlur * share;
        }
    }

    /// <summary>
    /// DD1's confirm dialog (shared/confirm_dialog): a question over its answers, each a line of text that
    /// lights up under the pointer. Nothing behind it can be clicked while it is open.
    ///
    /// What is behind it DD1 blurs, the scene and the panels alike, and darkens hardly at all (its own frame of
    /// the retreat question, _lab/dd1_ref/raid/raid_retreat_confirmation.png, against the frame before the
    /// click: the panels' light is the same, their edges are those of the same panels under a Gaussian of 2.3
    /// px). It is the blur of DD1's tutorial window, and made the same way: a picture of the frame as it
    /// stands, blurred, laid across the display under the dialog (<see cref="Estate.TutorialPopup.BlurredScreen"/>).
    /// The dialog comes a frame after it is asked for, when that picture has been taken.
    /// </summary>
    internal class RaidConfirm
    {
        private const string Dir = "shared/confirm_dialog/";
        private static readonly Vector2 ArtSize = new Vector2(840f, 600f);      // confirm_dialog.background.png
        private static readonly Vector2 LitArt = new Vector2(466f, 81f);        // confirm_dialog.answer_text_selected_overlay.png
        // the mod's own: how dark the screen goes under the dialog when no picture of it can be had
        private static readonly Color PlainShade = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>Dev bridge: false leaves the screen sharp under a plain shade.</summary>
        public static bool Blur = true;

        private readonly RectTransform _screen;
        private readonly MonoBehaviour _host;
        private RectTransform _root;
        private Texture2D _behind;

        public bool IsOpen => _root != null;

        /// <summary>Dev bridge: true once the blurred picture lies under the dialog.</summary>
        public bool Blurred => _root != null && _behind != null;

        /// <param name="host">
        /// Whose coroutine waits for the frame's end to take the picture behind the dialog. A screen that gives
        /// none (the hamlet's, which have their own ground) gets the dialog at once over a plain shade.
        /// </param>
        public RaidConfirm(RectTransform screen, MonoBehaviour host = null)
        {
            _screen = screen;
            _host = host;
        }

        public void Ask(string question, string[] answers, Action<int> answered)
        {
            Close();
            var l = RaidLayout.Current;
            _root = UiKit.Rect("Confirm", _screen);
            UiKit.Stretch(_root);
            // What is behind: one picture across the whole display (which may be wider than DD1's screen, or
            // taller: the screen stands on the display's foot, in its middle), and nothing under it can be clicked.
            var behind = UiKit.Rect("Behind", _root);
            var display = _screen.parent is RectTransform canvas ? canvas.rect.size : new Vector2(1920f, 1080f);
            behind.anchorMin = behind.anchorMax = behind.pivot = new Vector2(0.5f, 0f);
            behind.anchoredPosition = Vector2.zero;
            behind.sizeDelta = display;
            var picture = behind.gameObject.AddComponent<RawImage>();
            picture.raycastTarget = true;
            picture.color = PlainShade;

            var dialog = UiKit.Rect("Dialog", _root);
            UiKit.Stretch(dialog);
            if (Blur && _host != null && _host.isActiveAndEnabled)
            {
                // all but clear until the picture is taken: the picture is of the screen, not of a shade
                picture.color = new Color(0f, 0f, 0f, 0.004f);
                dialog.gameObject.SetActive(false);
                _host.StartCoroutine(TakeBehind(_root, picture, dialog.gameObject));
            }

            RaidUi.Art("Art", dialog, Dir + "confirm_dialog.background.png", new Vector2(l.ConfirmBase.x - ArtSize.x * 0.5f, l.ConfirmBase.y), ArtSize, new Color(0.03f, 0.03f, 0.035f, 0.98f), true);
            var text = RaidUi.Paragraph("Question", dialog, "confirm_dialog_question", l.ConfirmBase + l.ConfirmQuestion,
                new Vector2(l.ConfirmQuestionWidth, l.ConfirmAnswersStart.y + l.ConfirmAnswersOffset.y - l.ConfirmQuestion.y - 8f), TextAlignmentOptions.Top);
            text.text = question;

            for (var i = 0; i < answers.Length; i++)
            {
                var index = i;
                var centre = l.ConfirmBase + l.ConfirmAnswersOffset + l.ConfirmAnswersStart + l.ConfirmAnswerSpacing * i;
                var row = UiKit.Image("Answer" + i, dialog, null, Color.clear, true);
                ((RectTransform)row.transform).PlaceTopLeft(new Vector2(centre.x - l.ConfirmButtonSize.x * 0.5f, centre.y), RaidUi.TopLeft, l.ConfirmButtonSize);
                var lit = RaidUi.Art("Lit", row.transform, Dir + "confirm_dialog.answer_text_selected_overlay.png",
                    new Vector2((l.ConfirmButtonSize.x - LitArt.x) * 0.5f + l.ConfirmSelected.x, l.ConfirmSelected.y), LitArt);
                lit.gameObject.SetActive(false);
                var words = RaidUi.Label("Text", row.transform, "confirm_dialog_answer", new Vector2(l.ConfirmButtonSize.x * 0.5f, (l.ConfirmButtonSize.y - 28f) * 0.5f),
                    new Vector2(l.ConfirmButtonSize.x, 28f), TextAlignmentOptions.Top);
                words.text = answers[i];
                RaidUi.Pointer(row, () =>
                {
                    Close();
                    answered?.Invoke(index);
                }, null, inside => lit.gameObject.SetActive(inside));
            }
        }

        private IEnumerator TakeBehind(RectTransform root, RawImage picture, GameObject dialog)
        {
            yield return new WaitForEndOfFrame();
            if (root == null || _root != root) yield break;     // answered or closed in the meantime
            Texture2D blurred = null;
            try { blurred = Estate.TutorialPopup.BlurredScreen(Estate.TutorialPopup.BlurSigma, "DD2Estate.ConfirmBehind"); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: the screen behind the question could not be blurred: " + e.Message); }
            if (blurred != null)
            {
                _behind = blurred;
                picture.texture = blurred;
                picture.color = Color.white;
            }
            else picture.color = PlainShade;
            dialog.SetActive(true);
        }

        public void Close()
        {
            if (_behind != null) UnityEngine.Object.Destroy(_behind);
            _behind = null;
            if (_root == null) return;
            _root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }
    }
}
