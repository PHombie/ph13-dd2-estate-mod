using System;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>Where the fight's bag and its button stand on the 1920x1080 screen (y down), and how large.</summary>
    internal struct FightBagLayout
    {
        /// <summary>Top left corner of the first card's cell.</summary>
        public Vector2 Grid;
        /// <summary>DD1's inventory panel is drawn at this share of its own size.</summary>
        public float Scale;
        /// <summary>Top left corner and side of the button's place: a place of DD2's bar, like a skill button's.</summary>
        public Vector2 Button;
        public float ButtonSize;
        /// <summary>The black ground under the bag: what of DD2's bar it hides. x, y, width, height.</summary>
        public Rect Cover;
    }

    /// <summary>
    /// What the button is made of: the pictures of DD2's own "move" button beside it (the diamond plate, the
    /// mark it wears under the pointer and while it is chosen, the grey of its sign), read from the running
    /// game, and the sign on the plate: DD2's picture for its inventory.
    /// </summary>
    internal sealed class FightBagSkin
    {
        public Sprite Plate, Mark, Sign;
        public Color SignColour = new Color32(0xCF, 0xCF, 0xCF, 0xFF);
        public Color Hovered = new Color32(0x79, 0xC9, 0xCC, 0xFF);
        public Color Chosen = new Color32(0xAF, 0x92, 0x5D, 0xFF);
        /// <summary>The sign is one of DD2's white signs (it takes the sign's grey); false for DD1's painted sack.</summary>
        public bool SignIsDd2;
    }

    /// <summary>
    /// What the fight's bag draws: a canvas of the mod's own over DD2's fight screen with
    ///   the button that switches DD2's skill bar for the bag and back (always there while the bar is): a
    ///   diamond like DD2's "move" and "pass" at the other end of the bar, with DD2's inventory sign on it;
    ///   the cells of DD1's raid inventory panel (panels/panel_inventory.png without its frame and its two
    ///   tabs: the lines of the 8 by 2 cells) with the bag's cards on them (<see cref="InventoryGrid"/>, the
    ///   same grid the corridor's panel uses), on a black ground that hides the skills under it;
    ///   DD1's tooltip beside a card, and DD1's line of user information for a click that does nothing.
    /// It decides nothing: <see cref="FightBag"/> tells it what to show and gets the clicks.
    /// </summary>
    internal sealed class FightBagView
    {
        // DD1's panels/panel_inventory.png is 720x360: an outer frame (its lines at x 6..10 and 669..673, y
        // 11..14), a thin inner one (x 14..16 and 663..665, y 19..20), the lines of the cells inside it (the
        // first cell at 20, 28; a line every 80 px, the rows' at y 180) and the two tabs (map, inventory) right
        // of the frame. The fight's bag is the cells and the thin line around them, without the outer frame:
        // as on the owner's picture of it, the cards begin where the hero's name plate does.
        private static readonly Vector2 PanelArt = new Vector2(720f, 360f);
        private static readonly Rect CellsArt = new Rect(12f, 18f, 654f, 342f);
        /// <summary>Where the cells' picture begins, in the panel's own pixels from its top.</summary>
        public const float CellsTop = 18f;
        // the inventory tab's sack inside that picture
        private static readonly Rect TabArt = new Rect(672f, 252f, 40f, 68f);
        private const string PanelFile = "panels/panel_inventory.png";
        private const float NoticeSeconds = 2.5f;
        private const float NoticeWidth = 560f;

        // DD2's "move" button, measured in the running game on a place of 100: the plate 104 x 105 two pixels
        // out to the left and the top, the sign 42 in the middle, the mark 118 x 38 across the middle
        private const float Dd2Place = 100f;
        private static readonly Rect PlateAt = new Rect(-2f, -2f, 104f, 105f);
        private static readonly Rect MarkAt = new Rect(-9f, 32f, 118f, 38f);
        private const float SignSide = 46f;

        private readonly Canvas _canvas;
        private readonly CanvasGroup _group;
        private readonly RectTransform _screen, _panel, _buttonRect, _noticeRect;
        private readonly Image _cover, _ground, _button, _plate, _sign, _mark, _noticeGround;
        private readonly InventoryGrid _grid;
        private readonly RaidTooltip _tooltip;
        private readonly TextMeshProUGUI _notice;
        private FightBagLayout _layout;
        private FightBagSkin _skin = new FightBagSkin();
        private float _noticeUntil;
        private int _hover = -1;
        private bool _bag, _buttonInside;

        /// <summary>A card was clicked: its slot, and whether with the right button.</summary>
        public Action<int, bool> Clicked;
        public Action Toggled;
        /// <summary>The words of a card's tooltip (rich text); null for none.</summary>
        public Func<int, string> TooltipOf;
        public Func<string> ButtonTooltip;

        public Canvas Canvas => _canvas;
        public int Hovered => _hover;
        public bool ShowsBag => _bag && _canvas.gameObject.activeSelf;
        public bool Shown => _canvas.gameObject.activeSelf;
        public FightBagSkin Skin => _skin;
        public FightBagLayout Layout => _layout;

        public FightBagView(int order)
        {
            _canvas = UiKit.Canvas("DD2Estate.FightBag", order);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _screen = RaidUi.Screen("Screen", _canvas.transform);
            var l = RaidLayout.Current;

            _cover = UiKit.Image("Cover", _screen, null, Color.black, true);

            _panel = UiKit.Rect("Panel", _screen);
            _panel.PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, PanelArt);
            _ground = UiKit.Image("Cells", _panel, Crop(PanelFile, CellsArt), Color.white);
            ((RectTransform)_ground.transform).PlaceTopLeft(CellsArt.position, RaidUi.TopLeft, CellsArt.size);
            if (_ground.sprite == null) _ground.color = Color.clear;
            _grid = InventoryGrid.Build("Bag", _panel, l.BagStart, InventoryContent.NewBag().SlotCount, l.BagColumns, l.BagOffset);
            _grid.Clicked = (slot, right) => Clicked?.Invoke(slot, right);
            _grid.Hovered = OnHovered;

            // the button: its place takes the pointer, the pictures on it are DD2's
            _button = UiKit.Image("Toggle", _screen, null, Color.clear, true);
            _buttonRect = (RectTransform)_button.transform;
            _plate = UiKit.Image("Plate", _buttonRect, null);
            _sign = UiKit.Image("Sign", _buttonRect, null);
            _sign.preserveAspect = true;
            _mark = UiKit.Image("Mark", _buttonRect, null);
            _mark.gameObject.SetActive(false);
            RaidUi.Pointer(_button, () => Toggled?.Invoke(), null, inside =>
            {
                _buttonInside = inside;
                PaintButton();
                ShowButtonTooltip();
            });

            _tooltip = new RaidTooltip(_screen);

            // DD1's line of user information; it stands over the fight's own marks for a moment, on a ground of its own
            _noticeGround = UiKit.Image("UserInformation", _screen, null, new Color(0f, 0f, 0f, 0.8f));
            _noticeRect = (RectTransform)_noticeGround.transform;
            _notice = UiKit.Text("Text", _noticeRect, "", Dd1Fonts.FontOf("user_information_popup") != null ? "user_information_popup" : "tooltip",
                Dd1Fonts.Colour("user_information_neutral", new Color32(143, 8, 0, 255)), TextAlignmentOptions.Top);
            _notice.raycastTarget = false;
            _notice.textWrappingMode = TextWrappingModes.Normal;
            _noticeGround.gameObject.SetActive(false);
            _canvas.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
        }

        public int Order
        {
            get => _canvas.sortingOrder;
            set => _canvas.sortingOrder = value;
        }

        /// <summary>The button's pictures.</summary>
        public void SetSkin(FightBagSkin skin)
        {
            _skin = skin ?? new FightBagSkin();
            _plate.sprite = _skin.Plate;
            _plate.color = _skin.Plate != null ? Color.white : Color.clear;
            _sign.sprite = _skin.Sign;
            _mark.sprite = _skin.Mark;
            PaintButton();
        }

        /// <summary>DD1's own picture for it: the sack of the raid panel's inventory tab.</summary>
        public static Sprite Dd1TabIcon() => Crop(PanelFile, TabArt);

        // A part of a DD1 picture, given in the picture's own pixels (y down); a picture of the mod's that
        // takes DD1's place may be larger and is cut in proportion.
        private static Sprite Crop(string dd1File, Rect part)
        {
            if (!Dd1Install.Found || !Dd1Install.Exists(dd1File)) return null;
            var texture = Dd1Install.Texture(dd1File);
            if (texture == null) return null;
            var kx = texture.width / PanelArt.x;
            var ky = texture.height / PanelArt.y;
            var rect = new Rect(part.x * kx, (PanelArt.y - part.y - part.height) * ky, part.width * kx, part.height * ky);
            var sprite = Sprite.Create(texture, rect, RaidUi.Middle, 100f, 0, SpriteMeshType.FullRect);
            sprite.name = dd1File + "@" + part.x + "," + part.y;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public void Apply(FightBagLayout layout)
        {
            _layout = layout;
            var l = RaidLayout.Current;
            _panel.localScale = new Vector3(layout.Scale, layout.Scale, 1f);
            // the panel's corner: the first cell stands start_pos inside it
            _panel.PlaceTopLeft(layout.Grid - l.BagStart * layout.Scale, RaidUi.TopLeft, PanelArt);
            ((RectTransform)_cover.transform).PlaceTopLeft(layout.Cover.position, RaidUi.TopLeft, layout.Cover.size);

            var side = layout.ButtonSize;
            var k = side / Dd2Place;
            _buttonRect.PlaceTopLeft(layout.Button, RaidUi.TopLeft, new Vector2(side, side));
            ((RectTransform)_plate.transform).PlaceTopLeft(PlateAt.position * k, RaidUi.TopLeft, PlateAt.size * k);
            ((RectTransform)_mark.transform).PlaceTopLeft(MarkAt.position * k, RaidUi.TopLeft, MarkAt.size * k);
            ((RectTransform)_sign.transform).Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, new Vector2(SignSide, SignSide) * k);
        }

        /// <summary>
        /// The button with DD2's bar (<paramref name="bar"/>), the bag in the bar's place (<paramref name="bag"/>);
        /// <paramref name="alpha"/> is the bar's own, so that it comes and goes with it.
        /// </summary>
        public void Show(bool bar, bool bag, float alpha)
        {
            if (!bar)
            {
                Hide();
                return;
            }
            if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);
            _group.alpha = alpha;
            if (_bag != bag || _panel.gameObject.activeSelf != bag)
            {
                _bag = bag;
                _panel.gameObject.SetActive(bag);
                _cover.gameObject.SetActive(bag);
                if (!bag)
                {
                    _tooltip.Hide(_grid);
                    _noticeGround.gameObject.SetActive(false);
                }
                PaintButton();
                if (_buttonInside) ShowButtonTooltip();
            }
            if (_noticeGround.gameObject.activeSelf && Time.unscaledTime >= _noticeUntil) _noticeGround.gameObject.SetActive(false);
        }

        public void Hide()
        {
            if (!_canvas.gameObject.activeSelf) return;
            _tooltip.HideAll();
            _noticeGround.gameObject.SetActive(false);
            _hover = -1;
            _canvas.gameObject.SetActive(false);
        }

        /// <summary>The bag's stacks; <paramref name="dim"/> says which of them cannot be used now.</summary>
        public void Fill(Core.Inventory bag, Func<int, ItemStack, bool> dim)
        {
            for (var i = 0; i < _grid.SlotCount; i++)
            {
                var stack = bag?.Slot(i);
                _grid.Set(i, stack?.Item, stack?.Amount ?? 0, stack != null && dim != null && dim(i, stack));
            }
            if (_hover >= 0 && (bag == null || bag.Slot(_hover) == null)) _tooltip.Hide(_grid);
        }

        private void OnHovered(int slot)
        {
            _hover = slot;
            ShowTooltip();
        }

        /// <summary>The tooltip of the card under the pointer, written again (what it says changes with a use).</summary>
        public void ShowTooltip()
        {
            var words = _hover >= 0 && _bag ? TooltipOf?.Invoke(_hover) : null;
            if (words == null)
            {
                _tooltip.Hide(_grid);
                return;
            }
            var l = RaidLayout.Current;
            var at = _layout.Grid + (_grid.SlotPosition(_hover) + l.ItemTooltipOffset) * _layout.Scale;
            _tooltip.Show(_grid, words, at, l.ItemTooltipWidth);
        }

        /// <summary>Dev bridge: as if the pointer went onto a card (-1: off it).</summary>
        public void DevHover(int slot) => OnHovered(slot);

        private void ShowButtonTooltip()
        {
            var words = _buttonInside ? ButtonTooltip?.Invoke() : null;
            if (words == null)
            {
                _tooltip.Hide(_button);
                return;
            }
            // over the button, its bottom edge a little above the diamond's tip
            _tooltip.Show(_button, words, _layout.Button + new Vector2(_layout.ButtonSize * 0.5f, -6f), RaidLayout.Current.SideButtonTooltipWidth, true, true);
        }

        /// <summary>Dev bridge: as if the pointer went onto the button.</summary>
        public void DevHoverButton(bool inside)
        {
            _buttonInside = inside;
            PaintButton();
            ShowButtonTooltip();
        }

        // DD2's own way with the buttons of its bar: the mark in its pointer colour under the pointer, in its
        // gold while the button's thing is chosen (here: while the bag is up); the sign is brighter then.
        private void PaintButton()
        {
            var lit = _bag || _buttonInside;
            if (_skin.Sign == null) _sign.color = Color.clear;
            else if (_skin.SignIsDd2) _sign.color = lit ? Color.white : _skin.SignColour;
            else _sign.color = lit ? Color.white : new Color(0.8f, 0.8f, 0.8f);
            var mark = _skin.Mark != null && lit;
            if (_mark.gameObject.activeSelf != mark) _mark.gameObject.SetActive(mark);
            if (mark) _mark.color = _bag ? _skin.Chosen : _skin.Hovered;
        }

        /// <summary>
        /// DD1's line of user information ("Can't use items during battle"), over the bag for a moment;
        /// <paramref name="good"/>: not a refusal but something done, in DD1's notable colour.
        /// </summary>
        public void Notice(string text, bool good = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            _notice.color = good ? UiKit.Notable : Dd1Fonts.Colour("user_information_neutral", new Color32(143, 8, 0, 255));
            _notice.text = text;
            var wanted = _notice.GetPreferredValues(text, NoticeWidth, 0f);
            var size = new Vector2(Mathf.Min(NoticeWidth, Mathf.Ceil(wanted.x) + 2f), Mathf.Ceil(wanted.y));
            var l = RaidLayout.Current;
            var middle = _layout.Grid.x + l.BagOffset.x * l.BagColumns * _layout.Scale * 0.5f;
            // its foot on the line over the cards
            var box = size + new Vector2(24f, 8f);
            _noticeRect.PlaceTopLeft(new Vector2(middle, _layout.Cover.y - box.y), RaidUi.TopCentre, box);
            ((RectTransform)_notice.transform).Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, size);
            _noticeGround.gameObject.SetActive(true);
            _noticeRect.SetAsLastSibling();
            _noticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        public string NoticeText => _noticeGround.gameObject.activeSelf ? _notice.text : null;

        /// <summary>Dev bridge: a card's rectangle on the 1920x1080 screen (x, y, width, height; y down).</summary>
        public Rect CardRect(int slot)
        {
            var l = RaidLayout.Current;
            var at = _layout.Grid + (_grid.SlotPosition(slot) + l.ItemIconOffset) * _layout.Scale;
            return new Rect(at, l.ItemIconSize * _layout.Scale);
        }
    }
}
