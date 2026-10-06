using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.CommonLogic.Pooling;
using Assets.Code.Locale;
using Assets.Code.UI;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Screens;
using Assets.Code.UI.Widgets;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's "Equipment" on the hero's sheet. The estate's hero sheet is DD2's own character sheet; where that
    /// has its "Trinkets" (a heading and two slots) stands what DD1's sheet has there instead: the hero's weapon
    /// and armour, DD1's own pictures of the class's pieces at the level the Blacksmith has made them
    /// (heroes/&lt;class&gt;/icons_equip/eqp_weapon_&lt;n&gt;.png, their level beside DD1's glyphs), and the two trinket
    /// slots, cut from DD1's art of its sheet (shared/character/characterpanel_frames.png) and placed by its
    /// layout (character.layout.darkest, hero.layout.darkest), under DD1's word for it (character_title_equipment).
    ///
    /// What a hero wears lies where it always did, in the hero's DD2 trinket slots (the fight reads it there;
    /// <see cref="RealmInventory"/> is the only writer). The two slots drawn here show it with the Trinket
    /// Inventory's own cards (<see cref="RealmInventoryPanel.WornCard"/>). DD2's own slot widgets are put away
    /// with their heading, and so is its combat item slot in the skills' grid: in the Estate both are dead (the
    /// sheet edits no inventory, DD2's inventory screen never opens: Dd2/InventoryPatches.cs, and the Estate has one bag).
    ///
    /// In the hamlet the sheet and the Trinket Inventory work together, as DD1's do: the panel stands against
    /// the roster column where DD1 has it, and for as long as both are up the sheet stands aside, its right edge
    /// at the panel's left (DD2's sheet is wider than the room DD1's panel leaves; what leaves the screen is the
    /// skills' column, which DD1's panel covers on DD1's sheet too). A card of the panel is dragged onto a slot
    /// here (or onto the hero anywhere on the sheet), a worn one from here onto the panel, a roster row or the
    /// other slot of the two (it changes places with what lies there, also with nothing: panel open or not); a
    /// click on a worn one takes it off while the panel is open and opens the panel while it is not. In a
    /// dungeon and in a fight the block is for looking: trinkets change hands with the bag on the raid panel.
    ///
    /// Pointing at the weapon or the armour says what its level gives, in the Blacksmith's own words
    /// (<see cref="Blacksmith.Effect"/>): the numbers are written there and nowhere else.
    /// </summary>
    [EstateModule]
    internal class SheetEquipment : MonoBehaviour
    {
        private const string SheetLayout = "shared/character/character.layout.darkest";
        private const string HeroLayout = "shared/hero/hero.layout.darkest";
        private const string ItemLayout = "shared/inventory/inventory.layout.darkest";
        private const string SheetArt = "shared/character/characterpanel_frames.png";
        private const string SmithArt = UpgradeUi.BuildingsDir + "blacksmith/blacksmith";

        private static readonly Vector2 Card = new Vector2(72f, 144f);           // eqp_weapon_<n>.png, panels/icons_equip/trinket/rarity_*.png
        private static readonly Vector2 StandIn = new Vector2(72f, 72f);         // blacksmith.weapon.icon.png, blacksmith.armour.icon.png
        // MEASURED on characterpanel_frames.png (1395x776, black and opaque down to y 746): DD1's equipment band.
        // The bar's two rules lie at y 493..495 and 533..535, the glyphs of weapon, armour and trinkets at y 550..582,
        // the four slots' frames at x 268..346, 359..437, 485..563, 576..654, y 588..736 (their insides 72x144).
        // The cut is the band with a little of the sheet's black round it, no wider than the column of DD2's sheet
        // it stands in; its edges are faded out so that DD1's black meets DD2's without a seam.
        private static readonly Rect Cut = new Rect(261f, 485f, 402f, 261f);
        private const float BarFoot = 540f;         // under DD1's bar: the cut without its bar begins here
        private const float Fade = 8f;
        // The middle of the four slots in the art: it is put on the middle of DD2's two slots. MEASURED on DD2's
        // sheet at 1920x1080, where that middle is 1274, 853: the sheet's quirk lists end at y 691, the lower rule
        // of the heading lies at y 748..751, the sheet's own frame runs down x 1463..1466 and along y 965..968, the
        // speed's number ends by x 1064; the heading's two rules run from x 1104 to 1447 (their middle 1275.5,
        // their left third fading out), and the last line of the pillar of the hero's arch stands at x 1084
        // (the arch's picture, ArchImage, ends at 1095). DD1's four slots are 386 wide, DD2's column 340: the
        // band stands a little lower (its glyphs clear of the heading's rule).
        private static readonly Vector2 SlotsMiddle = new Vector2(461f, 662f);
        private const float Lowered = 16f;
        // The first build stood the band 12 px left of DD2's middle, so that its last slot kept clear of the
        // sheet's frame: its first slot then stood on the foot of the hero's arch, 37 px left of the heading's
        // rules (the owner, 2026-10-06: "shift this block a little to the right"). Now its middle is DD2's, under
        // the heading, and the room is found between the cards: DD1 leaves 48 px (frame to frame) between the
        // armour and the first trinket and 13 inside each pair; the 48 become 25. The art is laid down in two
        // halves, cut in the middle of that gap, each this half of the difference nearer the other. The cards
        // are DD1's size. SEEN: the four frames run from x 1092 to 1456, 8 px clear of the arch's last line and
        // 7 of the sheet's frame line (which is as far from the last card as it was); the cards' middle is
        // 1274.4, the heading's rules' 1275.4.
        private const float Closed = 23f;
        // DD1's bar (the heading "dd1") is as wide as the cut and would run across the sheet's frame line now that
        // the block stands further right: it is cut this much shorter at either end.
        private const float BarTrim = 13f;
        private const string BarPiece = "bar", GearPiece = "gear", WornPiece = "trinkets";
        // FALLBACK: base.colours.darkest equipment_level_0 .. _4 (the last is "harmful").
        private static readonly Color[] LevelColours =
        {
            new Color32(215, 213, 205, 255), new Color32(215, 213, 205, 255), new Color32(105, 190, 75, 255), new Color32(62, 114, 212, 255), new Color32(177, 25, 0, 255)
        };
        // The sheet's art ends a few pixels inside its box: that much of it may lie over the panel's edge.
        private const float Overlap = 2f;
        private const float SlideSpeed = 16f;       // the sheet covers this share of the way left, per second (eased)
        // DD2's sheet ends this far above the screen's lower edge (the mod's pixels: its box is 2,51 1486x945 of
        // 1920x1080): the block's tooltips keep above that line, clear of the estate's bar.
        private const float SheetFoot = 84f;
        private const int TopOrder = 60;            // over DD2's screens (10), its dialogs (30) and its own drag layer (50); under its tooltips (100)

        private static readonly FieldInfo TrinketContainer = AccessTools.Field(typeof(CharacterSheetStatsUiBhv), "m_trinketContainerBhv");
        private static readonly FieldInfo CombatItems = AccessTools.Field(typeof(CharacterSheetStatsUiBhv), "m_combatItemsAdded");
        private static readonly FieldInfo ParentSheet = AccessTools.Field(typeof(CharacterSheetStatsUiBhv), "m_parentBhv");
        private static readonly FieldInfo SheetScreen = AccessTools.Field(typeof(CharacterSheetUiBhv), "m_screenBhv");

        /// <summary>The heading over the block: "dd2" (DD2's own heading of the sheet, with DD1's word) or "dd1" (DD1's bar).</summary>
        private static ConfigEntry<string> _heading;
        private static readonly Dictionary<string, Sprite> Pieces = new Dictionary<string, Sprite>();
        private static bool _piecesAsked;
        private static SheetEquipment _live;
        private static Canvas _top;
        private static RectTransform _tipFrame;
        private static Dd1Tooltip _tooltip;
        private static Vector2 _tipFrameSize;
        private static readonly Vector3[] Corners = new Vector3[4];

        private CharacterSheetStatsUiBhv _stats;
        private CharacterSheetUiBhv _sheet;
        private UiScreenBhv _screen;
        private RectTransform _sheetRect, _dd2Slots, _block, _worn;
        private GameObject _dd2Title, _title, _bar;
        private List<GameObject> _dd2Items;
        private CanvasGroup _group, _titleGroup, _follow;
        private Image _backWithBar, _backGear, _backWorn;
        private readonly Image[] _gear = new Image[2];
        private readonly Image[] _standIn = new Image[2];
        private readonly TextMeshProUGUI[] _gearLevel = new TextMeshProUGUI[2];
        private readonly RectTransform[] _slots = new RectTransform[2];
        private Vector2 _gearTip, _itemTip;
        private float _itemTipWidth;
        private uint _guid;
        private string _shown;
        private bool _held, _dd1Heading;
        private float _nextIcons, _nextLook;
        // The sheet aside: how far it stands from its own place (its parent's pixels), where that place is, and
        // where it was put last (a place it is found at and was not put at is its own again: DD2 moved it).
        private float _shift;
        private Vector2 _base, _applied;
        private bool _placed, _snap = true;

        // ---- for the Trinket Inventory ---------------------------------------------------------------------

        /// <summary>The hero whose sheet is up in the Estate; 0 while none is (or it is closing).</summary>
        public static uint Hero
        {
            get
            {
                if (!EstateSession.Active || !SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return 0u;
                try { return SingletonMonoBehaviour<CommonUiBhv>.Instance.ActiveCharacterSheetActorGuid; }
                catch (Exception) { return 0u; }
            }
        }

        // A card is being dragged: its copy is on the layer over the sheet (TrinketDrag).
        private static bool CardInHand => _top != null && _top.transform.Find("DraggedTrinket") != null;

        private static SheetEquipment Live => _live != null && _live.isActiveAndEnabled && _live._held ? _live : null;

        /// <summary>True while a place on screen (real pixels from the bottom left) lies on the hero's sheet.</summary>
        public static bool Over(Vector2 screen)
        {
            var live = Live;
            return live != null && Hero != 0u && RectTransformUtility.RectangleContainsScreenPoint(live._sheetRect, screen, null);
        }

        /// <summary>The trinket slot of the sheet under a place on screen: 0 or 1; -1 for none (also for a slot the hero does not have).</summary>
        public static int SlotAt(Vector2 screen)
        {
            var live = Live;
            if (live == null) return -1;
            var slots = RealmInventory.Worn(live._guid).Count;
            for (var i = 0; i < live._slots.Length && i < slots; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(live._slots[i], screen, null)) return i;
            return -1;
        }

        /// <summary>
        /// A layer over DD2's screens and the hamlet alike, that takes no pointer: a card in the player's hand is
        /// drawn here (on the hamlet's canvas it would pass under the sheet), and the block's tooltips.
        /// </summary>
        public static Transform Overlay
        {
            get
            {
                if (_top != null) return _top.transform;
                try
                {
                    _top = UiKit.Canvas("DD2Estate.SheetTop", TopOrder);
                    var raycaster = _top.GetComponent<GraphicRaycaster>();
                    if (raycaster != null) Destroy(raycaster);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Hero sheet: no layer over the sheet: " + e.Message);
                    return null;
                }
                return _top.transform;
            }
        }

        // ---- attaching to DD2's sheet ------------------------------------------------------------------------

        /// <summary>DD2 has filled a hero's sheet (it opened, or stepped on to another hero): the block shows that hero.</summary>
        public static void Shown(CharacterSheetStatsUiBhv stats, ActorInstance actor)
        {
            if (stats == null || actor == null || !EstateSession.Active) return;
            var block = stats.GetComponent<SheetEquipment>();
            if (block == null)
            {
                block = stats.gameObject.AddComponent<SheetEquipment>();
                block._stats = stats;
                var built = false;
                try { built = block.Build(); }
                catch (Exception e) { Plugin.Log.LogError("Hero sheet: the Equipment block could not be built: " + e); }
                if (!built)
                {
                    Plugin.Log.LogWarning("Hero sheet: DD2's sheet is not made as expected; its Trinkets stay as they are.");
                    block.Undo();
                    return;
                }
            }
            _live = block;
            block._guid = actor.ActorGuid;
            block._shown = null;
            block.Hold(true);
            block.Refresh();
        }

        // What a build that failed half-way has made goes again, and DD2's own parts show.
        private void Undo()
        {
            if (_block != null) Destroy(_block.gameObject);
            if (_title != null) Destroy(_title);
            _block = null;
            Show(_dd2Title, true);
            Show(_dd2Slots != null ? _dd2Slots.gameObject : null, true);
            if (_live == this) _live = null;
            Destroy(this);
        }

        private bool Build()
        {
            _sheet = ParentSheet != null ? ParentSheet.GetValue(_stats) as CharacterSheetUiBhv : null;
            if (_sheet == null) _sheet = _stats.GetComponentInParent<CharacterSheetUiBhv>();
            var container = TrinketContainer != null ? TrinketContainer.GetValue(_stats) as Component : null;
            if (_sheet == null || container == null) return false;
            _screen = SheetScreen != null ? SheetScreen.GetValue(_sheet) as UiScreenBhv : null;
            _sheetRect = (RectTransform)_sheet.transform;
            _dd2Slots = (RectTransform)container.transform;
            _dd2Items = CombatItems != null ? CombatItems.GetValue(_stats) as List<GameObject> : null;
            var heading = _stats.transform.Find("TrinketsPanel");
            _dd2Title = heading != null ? heading.gameObject : null;
            var quirks = _stats.transform.Find("QuirksPanel");
            _follow = quirks != null ? quirks.GetComponent<CanvasGroup>() : null;

            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var sheet = Dd1Ui.Layout(SheetLayout);
            var hero = Dd1Ui.Layout(HeroLayout);
            var items = Dd1Ui.Layout(ItemLayout);
            const string band = "character_equipment_layout", worn = "hero_equipment_layout", grid = "hero_trinket_grid_layout";
            var frames = Dd1Ui.Offset(sheet, "character_layout", "frames_pos", 10f, 10f);

            _block = UiKit.Rect("DD2Estate.Equipment", _dd2Slots.parent);
            _block.SetSiblingIndex(_dd2Slots.GetSiblingIndex() + 1);
            _block.Place(Dd1Ui.Middle, Dd1Ui.TopLeft, Vector2.zero, Cut.size);
            _group = _block.gameObject.AddComponent<CanvasGroup>();

            // DD1's art of the band: its bar (shown for the heading "dd1"; DD2's own heading stands above the
            // block otherwise), and under it the slots in two halves, weapon and armour, and the two trinkets,
            // each moved towards the other (Closed).
            var half = Closed * 0.5f;
            var bar = Piece(BarPiece);
            _backWithBar = UiKit.Image("Band", _block, bar, bar != null ? Color.white : Color.clear);
            ((RectTransform)_backWithBar.transform).PlaceTopLeft(new Vector2(BarTrim, 0f), Dd1Ui.TopLeft, new Vector2(Cut.width - 2f * BarTrim, BarFoot + Fade - Cut.y));
            var gearSlots = Piece(GearPiece);
            _backGear = UiKit.Image("GearSlots", _block, gearSlots, gearSlots != null ? Color.white : Color.clear);
            ((RectTransform)_backGear.transform).PlaceTopLeft(new Vector2(half, BarFoot - Cut.y), Dd1Ui.TopLeft, new Vector2(SlotsMiddle.x - Cut.x, Cut.yMax - BarFoot));
            var wornSlots = Piece(WornPiece);
            _backWorn = UiKit.Image("TrinketSlots", _block, wornSlots, wornSlots != null ? Color.white : Color.clear);
            ((RectTransform)_backWorn.transform).PlaceTopLeft(new Vector2(SlotsMiddle.x - Cut.x - half, BarFoot - Cut.y), Dd1Ui.TopLeft, new Vector2(Cut.xMax - SlotsMiddle.x, Cut.yMax - BarFoot));

            // DD1's sheet counts from its own corner and draws this art at frames_pos: the band's things stand
            // where character.layout.darkest (equipment_pos and character_equipment_layout) and hero.layout.darkest
            // (hero_equipment_layout, hero_trinket_grid_layout) put them. A title's x is its middle.
            var equipment = Dd1Ui.Offset(sheet, "character_layout", "equipment_pos", 141f, 516f) - frames - Cut.position;
            var word = WindowText.Plain("character_title_equipment") ?? "Equipment";
            _bar = UiKit.Rect("Bar", _block).gameObject;
            UiKit.Stretch((RectTransform)_bar.transform);
            Dd1Ui.Line("Equipment", _bar.transform, "town_character_title", equipment + Dd1Ui.Offset(sheet, band, "title_offset", 323f, -12f), new Vector2(300f, 40f), TextAlignmentOptions.Center).text = word;
            _title = Dd2Heading(word);

            var gearAt = equipment + Dd1Ui.Offset(sheet, band, "equipment_offset", 107f, 34f) + new Vector2(half, 0f);
            var gearIcon = Dd1Ui.Offset(hero, worn, "icon_offset", 29f, 52f);
            var gearLevel = Dd1Ui.Offset(hero, worn, "level_offset", 90f, 12f);
            // DD1's tooltip of a piece of gear counts from the piece's own corner, as its picture does
            _gearTip = Dd1Ui.Offset(hero, worn, "tooltip_offset", 120f, 52f) - gearIcon;
            _itemTip = Dd1Ui.Offset(items, "inventory_item_tooltip_layout", "offset", 85f, 0f);
            _itemTipWidth = Dd1Ui.Number(items, "inventory_item_tooltip_layout", "text_width", 200f);
            for (var i = 0; i < _gear.Length; i++)
            {
                var gear = i == 0 ? Blacksmith.Gear.Weapon : Blacksmith.Gear.Armour;
                var at = gearAt + (i == 0 ? Dd1Ui.Offset(hero, worn, "weapon_pos", 4f, 0f) : Dd1Ui.Offset(hero, worn, "armour_pos", 95f, 0f));
                var image = UiKit.Image(i == 0 ? "Weapon" : "Armour", _block, null, Color.clear, true);
                ((RectTransform)image.transform).PlaceTopLeft(at + gearIcon, Dd1Ui.TopLeft, Card);
                _gear[i] = image;
                // a class DD1 never drew: the smithy's picture of the kind of piece stands in the slot (as at the Blacksmith's)
                _standIn[i] = Dd1Ui.Art("StandIn", image.transform, SmithArt + "." + Blacksmith.Tag(gear) + ".icon.png", new Vector2(0f, (Card.y - StandIn.y) * 0.5f), StandIn);
                // the gear's level beside the glyph painted over its slot, in the colour of the level
                _gearLevel[i] = Dd1Ui.Line("Level" + i, _block, "equipment_level", at + gearLevel, new Vector2(40f, 30f));
                UiKit.Hover(image.gameObject, inside => GearTip(image, gear, inside));
            }
            var slotAt = equipment + Dd1Ui.Offset(sheet, band, "trinkets_offset", 321f, 34f) + Dd1Ui.Offset(hero, grid, "start_pos", 32f, 52f)
                + Dd1Ui.Offset(items, "inventory_item_layout", "icon_offset", 4f, 0f) - new Vector2(half, 0f);
            var slotStep = Dd1Ui.Offset(hero, grid, "offset", 92f, 160f).x;
            for (var i = 0; i < _slots.Length; i++)
            {
                var spot = UiKit.Image("Slot" + i, _block, null, Color.clear, true);
                _slots[i] = ((RectTransform)spot.transform).PlaceTopLeft(slotAt + new Vector2(slotStep * i, 0f), Dd1Ui.TopLeft, Card);
                UpgradeUi.Pointer(spot, EmptySlotClicked, null, () => SlotTip(spot, true), () => SlotTip(spot, false));
            }
            _worn = UiKit.Stretch(UiKit.Rect("Worn", _block));
            if (_titleGroup != null) _titleGroup.alpha = 1f;
            // the layer for tooltips and the card in the hand is there before it is first drawn on
            if (Overlay == null) Plugin.Log.LogWarning("Hero sheet: tooltips of the Equipment block are off.");
            return true;
        }

        // DD2's own heading of this place on its sheet (the word between two rules, its emblem beside it), with
        // DD1's word in it: a copy, so that DD2's own stays what it is for a game outside the Estate.
        private GameObject Dd2Heading(string word)
        {
            if (_dd2Title == null) return null;
            try
            {
                // an inactive copy wakes nothing up: what must not wake is taken out first
                var was = _dd2Title.activeSelf;
                _dd2Title.SetActive(false);
                var copy = Instantiate(_dd2Title, _dd2Title.transform.parent);
                _dd2Title.SetActive(was);
                copy.name = "DD2Estate.EquipmentTitle";
                // over the band: its lower rule lies on the band's upper edge
                copy.transform.SetSiblingIndex(_block.GetSiblingIndex() + 1);
                foreach (var pool in copy.GetComponentsInChildren<GameObjectPoolBhv>(true)) DestroyImmediate(pool.gameObject);
                foreach (var localized in copy.GetComponentsInChildren<LocalizeTextBhv>(true)) DestroyImmediate(localized);
                // DD2's emblem of the heading (a trinket pouch) hangs down to where DD1's glyph of the weapon stands;
                // DD1's own glyphs (weapon, armour, pouch) are the pictures of this block
                var emblem = copy.transform.Find("pouch");
                if (emblem != null) emblem.gameObject.SetActive(false);
                var label = copy.GetComponentInChildren<TMP_Text>(true);
                if (label == null)
                {
                    Destroy(copy);
                    return null;
                }
                label.text = word;
                _titleGroup = copy.GetComponent<CanvasGroup>();
                return copy;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hero sheet: DD2's heading could not be copied (DD1's bar stands instead): " + e.Message);
                return null;
            }
        }

        // A cut of DD1's art, its edges faded out: the bar, or one half of the slots under it. A texture's rows count from the bottom.
        private static Sprite Piece(string part)
        {
            if (!_piecesAsked)
            {
                _piecesAsked = true;
                Texture2D source = null;
                try
                {
                    source = Dd1Install.Found && Dd1Install.Exists(SheetArt) ? Dd1Install.LoadTexture(SheetArt, true, false) : null;
                    if (source != null && source.width >= Cut.xMax && source.height >= Cut.yMax)
                    {
                        var pixels = source.GetPixels32();
                        // (the bar's cut ends a fade's width under the bar, so that its lower rule is not faded with the edge)
                        Pieces[BarPiece] = Faded(pixels, source.width, source.height, Rect.MinMaxRect(Cut.xMin + BarTrim, Cut.yMin, Cut.xMax - BarTrim, BarFoot + Fade), "bar");
                        Pieces[GearPiece] = Faded(pixels, source.width, source.height, Rect.MinMaxRect(Cut.xMin, BarFoot, SlotsMiddle.x, Cut.yMax), "weapon and armour");
                        Pieces[WornPiece] = Faded(pixels, source.width, source.height, Rect.MinMaxRect(SlotsMiddle.x, BarFoot, Cut.xMax, Cut.yMax), "trinkets");
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Hero sheet: DD1's equipment band could not be cut from " + SheetArt + ": " + e.Message); }
                finally
                {
                    if (source != null) Destroy(source);
                }
            }
            return Pieces.TryGetValue(part, out var piece) ? piece : null;
        }

        private static Sprite Faded(Color32[] pixels, int width, int height, Rect cut, string name)
        {
            int w = Mathf.RoundToInt(cut.width), h = Mathf.RoundToInt(cut.height), left = Mathf.RoundToInt(cut.xMin), low = height - Mathf.RoundToInt(cut.yMax);
            var faded = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var pixel = pixels[(low + y) * width + left + x];
                    var edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    var share = Mathf.Clamp01((edge + 0.5f) / Fade);
                    pixel.a = (byte)Mathf.RoundToInt(pixel.a * share * share * (3f - 2f * share));
                    faded[y * w + x] = pixel;
                }
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = SheetArt + " (" + name + ")", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(faded);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, w, h), Dd1Ui.Middle, 100f, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ---- every frame -------------------------------------------------------------------------------------

        private void OnEnable()
        {
            _snap = true;
            RealmInventory.Changed += Redraw;
            Blacksmith.Changed += Redraw;
        }

        private void OnDisable()
        {
            RealmInventory.Changed -= Redraw;
            Blacksmith.Changed -= Redraw;
            // DD2 puts its sheet away where it had it
            if (_placed && _sheetRect != null && Mathf.Abs(_shift) > 0.01f && (_sheetRect.anchoredPosition - _applied).sqrMagnitude < 0.01f) _sheetRect.anchoredPosition = _base;
            _shift = 0f;
            _placed = false;
            if (_tooltip != null) _tooltip.Hide(null);
        }

        private void OnDestroy()
        {
            if (_live == this) _live = null;
        }

        private void Redraw()
        {
            _shown = null;
        }

        private void LateUpdate()
        {
            if (_block == null) return;
            // outside the Estate the sheet is DD2's again, as it was made
            if (!EstateSession.Active)
            {
                if (_held) Hold(false);
                Slide(false);
                return;
            }
            Hold(true);
            Place();
            Slide(true);
            Refresh();
            if (Time.unscaledTime >= _nextIcons)
            {
                _nextIcons = Time.unscaledTime + 0.1f;
                RealmInventoryPanel.LoadIcons();
            }
        }

        // DD2's "Trinkets" (heading and slots) and its combat item slot stay away while the block stands in their
        // place: DD2's own timelines switch parts of the sheet on as it opens, so this is seen to every frame.
        private void Hold(bool mine)
        {
            _held = mine;
            _dd1Heading = _title == null || string.Equals(_heading != null ? _heading.Value : null, "dd1", StringComparison.OrdinalIgnoreCase);
            Show(_dd2Title, !mine);
            Show(_dd2Slots != null ? _dd2Slots.gameObject : null, !mine);
            if (_dd2Items != null)
                foreach (var item in _dd2Items) Show(item, !mine);
            Show(_block.gameObject, mine);
            Show(_title, mine && !_dd1Heading);
            Show(_bar, _dd1Heading);
            Show(_backWithBar.gameObject, _dd1Heading);
            // the block comes and goes with the sheet's own parts
            if (mine && _follow != null)
            {
                _group.alpha = _follow.alpha;
                if (_titleGroup != null) _titleGroup.alpha = _follow.alpha;
            }
        }

        private static void Show(GameObject thing, bool shown)
        {
            if (thing != null && thing.activeSelf != shown) thing.SetActive(shown);
        }

        // The middle of the four slots on the middle of DD2's two, wherever DD2's layout has them on this screen:
        // under the middle of the heading.
        private void Place()
        {
            var parent = (RectTransform)_block.parent;
            var middle = parent.InverseTransformPoint(_dd2Slots.TransformPoint(_dd2Slots.rect.center));
            var at = new Vector3(middle.x - (SlotsMiddle.x - Cut.x), middle.y + (SlotsMiddle.y - Cut.y) - Lowered, 0f);
            if ((_block.localPosition - at).sqrMagnitude > 0.0001f) _block.localPosition = at;
        }

        // How far left of its own place the sheet has to stand for the Trinket Inventory (the sheet's parent's
        // pixels; 0: where DD2 has it): its right edge at the panel's left. <paramref name="standsAside"/>: how
        // far aside it stands at this moment, which its corners on screen have in them.
        private float Aside(float standsAside)
        {
            var panel = RealmInventoryPanel.ScreenBox;
            if (panel == null) return 0f;
            var scale = ((RectTransform)_sheetRect.parent).lossyScale.x;
            if (scale <= 0f) return 0f;
            _sheetRect.GetWorldCorners(Corners);
            // overlay canvases: a world point is a screen pixel
            var right = Mathf.Max(Corners[2].x, Corners[0].x) - standsAside * scale;
            var edge = (panel.Value.xMin + Overlap) * Screen.height / 1080f;
            return Mathf.Min(0f, (edge - right) / scale);
        }

        // DD2's own timeline slides the sheet in and out (it writes the sheet's place every frame of its opening
        // and closing, MEASURED: from some 250 px to the left in a quarter of a second) and then lets go of it.
        // So every frame: a sheet found where it was not put was put there by DD2, and that is its own place,
        // with nothing of the way aside in it; a sheet found where it was put still stands aside by that much.
        // (Counting the way aside off a place DD2 had just written made the sheet run away to the left.)
        private void Slide(bool forPanel)
        {
            var now = _sheetRect.anchoredPosition;
            var moved = !_placed || (now - _applied).sqrMagnitude > 0.01f;
            if (moved) _base = now;
            _placed = true;
            var aside = forPanel ? Aside(moved ? 0f : _applied.x - _base.x) : 0f;
            // while DD2 itself moves the sheet the way aside is taken at once (eased behind DD2's own slide the
            // sheet swayed some 40 px at the panel's edge); the easing is for the panel coming and going
            if (_snap || moved || Mathf.Abs(aside - _shift) < 0.5f) _shift = aside;
            else _shift = Mathf.Lerp(_shift, aside, 1f - Mathf.Exp(-SlideSpeed * Time.unscaledDeltaTime));
            _snap = false;
            var want = _base + new Vector2(_shift, 0f);
            if ((now - want).sqrMagnitude > 0.0001f) _sheetRect.anchoredPosition = want;
            _applied = want;
        }

        // ---- what the block shows ----------------------------------------------------------------------------

        private static bool InTown => EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet;

        private void Refresh()
        {
            // told when something changed (a trinket moved, the smith worked, another hero); looked at now and then besides
            if (_shown != null && Time.unscaledTime < _nextLook) return;
            _nextLook = Time.unscaledTime + 0.25f;
            var hero = UpgradeUi.Hero(_guid);
            var worn = hero != null ? RealmInventory.Worn(_guid) : new List<string>();
            var town = InTown;
            var key = new StringBuilder().Append(_guid).Append(town ? 't' : 'a').Append(_dd1Heading ? '1' : '2');
            for (var i = 0; i < _gear.Length; i++) key.Append('|').Append(hero != null ? Blacksmith.Level(_guid, i == 0 ? Blacksmith.Gear.Weapon : Blacksmith.Gear.Armour) : 0);
            foreach (var id in worn) key.Append('|').Append(id ?? "-");
            if (key.ToString() == _shown) return;
            _shown = key.ToString();

            if (_tooltip != null) _tooltip.Hide(null);
            for (var i = 0; i < _gear.Length; i++)
            {
                var gear = i == 0 ? Blacksmith.Gear.Weapon : Blacksmith.Gear.Armour;
                var level = hero != null ? Mathf.Max(1, Blacksmith.Level(_guid, gear)) : 0;
                // DD1's own picture of the class's piece at that level
                var card = hero != null ? Blacksmith.Card(hero, gear, level) : null;
                var art = card != null ? Dd1Ui.Sprite(card) : null;
                if (_gear[i].sprite != art) _gear[i].sprite = art;
                _gear[i].color = art != null ? Color.white : Color.clear;
                _standIn[i].gameObject.SetActive(hero != null && art == null && _standIn[i].sprite != null);
                _gearLevel[i].text = hero != null ? level.ToString() : "";
                var step = Mathf.Clamp(level - 1, 0, LevelColours.Length - 1);
                _gearLevel[i].color = Dd1Fonts.Colour("equipment_level_" + step, LevelColours[step]);
            }

            UpgradeUi.Clear(_worn);
            for (var i = 0; i < _slots.Length; i++)
            {
                var id = i < worn.Count ? worn[i] : null;
                // a slot the hero does not have (DD2 unlocks them) takes nothing; an empty one waits for a drop
                _slots[i].gameObject.SetActive(hero != null && id == null && i < worn.Count);
                if (id != null) AddWorn(id, _slots[i].anchoredPosition, town);
            }
            RealmInventoryPanel.LoadIcons();
        }

        private void AddWorn(string id, Vector2 slotPosition, bool town)
        {
            var guid = _guid;
            var drag = RealmInventoryPanel.WornCard(_worn, id, new Vector2(slotPosition.x, -slotPosition.y));
            var card = drag.GetComponent<Image>();
            var refusal = RealmInventory.UnequipRefusal(guid, id);
            // away from the hamlet the block is for looking: nothing is picked up here
            drag.Fixed = refusal != null || !town;
            drag.Clicked = () =>
            {
                if (!InTown) return;
                if (!RealmInventoryPanel.IsOpen) RealmInventoryPanel.Open();
                else RealmInventoryPanel.WornMoved(guid, id, null);
            };
            drag.Dropped = pointer => RealmInventoryPanel.WornMoved(guid, id, pointer);
            drag.Hovered = inside =>
            {
                if (!inside)
                {
                    if (_tooltip != null) _tooltip.Hide(card);
                    return;
                }
                // (a hero with one slot has no other slot to carry it to)
                var other = RealmInventory.Worn(guid).Count > 1;
                var action = !InTown ? null
                    : refusal != null ? Dd1Ui.Tint(refusal + ".", "harmful")
                    : RealmInventoryPanel.IsOpen ? (other ? "Click to take it off, or drag it: back to the inventory, or onto the other slot." : "Click to take it off, or drag it back to the inventory.")
                    : other ? "Click to open the Trinket Inventory. Drag it onto the other slot to move it there."
                    : "Click to open the Trinket Inventory.";
                Tip(card, (RectTransform)card.transform, _itemTip, Trinkets.Name(id), RealmInventoryPanel.CardText(id, action), _itemTipWidth);
            };
        }

        // An empty slot: DD2's own slot opens its inventory at a click, and so does this one (the Estate's).
        private void EmptySlotClicked()
        {
            if (InTown && !RealmInventoryPanel.IsOpen) RealmInventoryPanel.Open();
        }

        private void SlotTip(Image spot, bool inside)
        {
            if (!inside || !InTown)
            {
                if (_tooltip != null) _tooltip.Hide(spot);
                return;
            }
            Tip(spot, (RectTransform)spot.transform, _itemTip, null, RealmInventoryPanel.IsOpen ? "Drag a trinket here, or click one in the inventory." : "Click to open the Trinket Inventory.", _itemTipWidth);
        }

        // What the level of a piece gives: the Blacksmith's own words, the numbers written there and nowhere else.
        private void GearTip(Image image, Blacksmith.Gear gear, bool inside)
        {
            var hero = inside ? UpgradeUi.Hero(_guid) : null;
            if (hero == null)
            {
                if (_tooltip != null) _tooltip.Hide(image);
                return;
            }
            var level = Mathf.Max(1, Blacksmith.Level(_guid, gear));
            Tip(image, (RectTransform)image.transform, _gearTip, Blacksmith.PieceName(hero, gear, level),
                Blacksmith.Name(gear) + ", level " + level + " of " + Blacksmith.MaxLevel(hero, gear) + "\n" + Blacksmith.Effect(gear, level), Dd1TipWidth);
        }

        private const float Dd1TipWidth = 300f;

        // DD1's tooltip of an item: beside the thing, level with its top (inventory_item_tooltip_layout), on the
        // layer over the sheet.
        private static void Tip(object owner, RectTransform of, Vector2 offset, string title, string body, float width)
        {
            var layer = Overlay as RectTransform;
            // a card in the hand says nothing of what it passes over
            if (layer == null || CardInHand) return;
            if (_tipFrame == null || _tooltip == null || (layer.rect.size - _tipFrameSize).sqrMagnitude > 1f)
            {
                if (_tipFrame != null) Destroy(_tipFrame.gameObject);
                _tipFrame = UiKit.Rect("Tips", layer);
                _tipFrame.anchorMin = Vector2.zero;
                _tipFrame.anchorMax = Vector2.one;
                _tipFrame.pivot = Dd1Ui.TopLeft;
                _tipFrame.offsetMin = Vector2.zero;
                _tipFrame.offsetMax = Vector2.zero;
                _tipFrameSize = layer.rect.size;
                _tooltip = new Dd1Tooltip(_tipFrame, _tipFrameSize.x, Mathf.Max(0f, _tipFrameSize.y - SheetFoot));
            }
            of.GetWorldCorners(Corners);          // 1 = the upper left corner
            var local = _tipFrame.InverseTransformPoint(Corners[1]);
            var scale = _tipFrame.lossyScale.x > 0f ? of.lossyScale.x / _tipFrame.lossyScale.x : 1f;
            _tooltip.ShowAt(owner, new Vector2(local.x, -local.y) + offset * scale, title, body, width);
        }

        // ---- where things are on screen --------------------------------------------------------------------

        /// <summary>A rect's box on screen in the mod's pixels (1080 high, y down from the top).</summary>
        public static Rect ScreenBox(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            rect.GetWorldCorners(Corners);
            var low = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
            var high = RectTransformUtility.WorldToScreenPoint(camera, Corners[2]);
            var unit = 1080f / Mathf.Max(1, Screen.height);
            return new Rect(Mathf.Min(low.x, high.x) * unit, (Screen.height - Mathf.Max(low.y, high.y)) * unit, Mathf.Abs(high.x - low.x) * unit, Mathf.Abs(high.y - low.y) * unit);
        }

        /// <summary>The same box as text: "x,y wxh".</summary>
        public static string Box(RectTransform rect) => Box(ScreenBox(rect));

        public static string Box(Rect box)
        {
            return Mathf.RoundToInt(box.x) + "," + Mathf.RoundToInt(box.y) + " " + Mathf.RoundToInt(box.width) + "x" + Mathf.RoundToInt(box.height);
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        /// <summary>For tests: where the block stands and what it shows; boxes are the mod's pixels (1080 high, y down), "x,y wxh".</summary>
        public static object Describe()
        {
            var live = _live;
            if (live == null || live._block == null) return new { built = false, hero = Hero };
            var hero = UpgradeUi.Hero(live._guid);
            var gear = new List<object>();
            for (var i = 0; i < live._gear.Length; i++)
            {
                var piece = i == 0 ? Blacksmith.Gear.Weapon : Blacksmith.Gear.Armour;
                var level = hero != null ? Mathf.Max(1, Blacksmith.Level(live._guid, piece)) : 0;
                gear.Add(new
                {
                    piece = Blacksmith.Tag(piece), level, shownLevel = live._gearLevel[i].text, art = live._gear[i].sprite != null ? live._gear[i].sprite.name : null,
                    standIn = live._standIn[i].gameObject.activeSelf, box = Box((RectTransform)live._gear[i].transform),
                    name = hero != null ? Blacksmith.PieceName(hero, piece, level) : null, gives = hero != null ? Blacksmith.Effect(piece, level) : null
                });
            }
            var slots = new List<object>();
            var worn = hero != null ? RealmInventory.Worn(live._guid) : new List<string>();
            for (var i = 0; i < live._slots.Length; i++)
            {
                live._slots[i].GetWorldCorners(Corners);
                var middle = (Corners[0] + Corners[2]) * 0.5f;
                slots.Add(new { slot = i, worn = i < worn.Count ? worn[i] : null, has = i < worn.Count, box = Box(live._slots[i]), x = middle.x, y = middle.y });
            }
            // Across the screen (the mod's pixels): the four cards from the first one's left edge to the last one's
            // right, their middle, the middle of DD2's two slots (which it is put on) and the heading's rules.
            var first = ScreenBox((RectTransform)live._gear[0].transform);
            var last = ScreenBox(live._slots[live._slots.Length - 1]);
            var dd2 = ScreenBox(live._dd2Slots);
            float ruleLeft = float.MaxValue, ruleRight = float.MinValue;
            var heading = live._title != null ? live._title : live._dd2Title;
            if (heading != null)
                foreach (var image in heading.GetComponentsInChildren<Image>(true))
                {
                    // (the heading's two rules; DD2 calls them "Bookend")
                    if (image.transform.parent != heading.transform || image.sprite == null || image.sprite.name != "ui_divider-line") continue;
                    var rule = ScreenBox(image.rectTransform);
                    ruleLeft = Mathf.Min(ruleLeft, rule.xMin);
                    ruleRight = Mathf.Max(ruleRight, rule.xMax);
                }
            var across = new
            {
                cardsLeft = first.xMin, cardsRight = last.xMax, cardsMiddle = (first.xMin + last.xMax) * 0.5f, dd2Middle = dd2.center.x,
                rulesLeft = ruleLeft <= ruleRight ? ruleLeft : (float?)null, rulesRight = ruleLeft <= ruleRight ? ruleRight : (float?)null,
                rulesMiddle = ruleLeft <= ruleRight ? (ruleLeft + ruleRight) * 0.5f : (float?)null, closed = Closed
            };
            return new
            {
                built = true, up = live.isActiveAndEnabled, held = live._held, hero = Hero, shows = live._guid, town = InTown,
                heading = live._dd1Heading ? "dd1" : "dd2", word = WindowText.Plain("character_title_equipment") ?? "Equipment",
                block = Box(live._block), sheet = Box(live._sheetRect), aside = live._shift, across,
                home = new[] { live._base.x, live._base.y }, screen = live._screen != null ? live._screen.ScreenState.ToString() : null,
                panel = RealmInventoryPanel.ScreenBox != null ? Box(RealmInventoryPanel.ScreenBox.Value) : null,
                dd2Trinkets = live._dd2Slots.gameObject.activeInHierarchy, dd2Heading = live._dd2Title != null && live._dd2Title.activeInHierarchy,
                dd2CombatItem = live._dd2Items != null && live._dd2Items.Exists(item => item != null && item.activeInHierarchy),
                cards = live._worn.childCount, gear, slots
            };
        }

        private static void Register()
        {
            _heading = Plugin.Settings.Bind("Look", "SheetEquipmentHeading", "dd2",
                "The heading over the Equipment block of the hero sheet: dd2 = the sheet's own heading (as over its quirks) with DD1's word, dd1 = DD1's bar.");
        }

        /// <summary>For tests: the heading of the block for this session ("dd1" or "dd2").</summary>
        public static string SetHeading(string value)
        {
            if (_heading != null && !string.IsNullOrEmpty(value)) _heading.Value = value;
            return _heading != null ? _heading.Value : null;
        }
    }

    /// <summary>The block follows DD2's sheet: every time DD2 fills the sheet for a hero.</summary>
    [HarmonyPatch(typeof(CharacterSheetStatsUiBhv), nameof(CharacterSheetStatsUiBhv.Populate))]
    internal static class SheetEquipmentFollowsTheSheet
    {
        private static void Postfix(CharacterSheetStatsUiBhv __instance, ActorInstance actor)
        {
            try { SheetEquipment.Shown(__instance, actor); }
            catch (Exception e) { Plugin.Log.LogError("Hero sheet: the Equipment block failed: " + e); }
        }
    }
}
