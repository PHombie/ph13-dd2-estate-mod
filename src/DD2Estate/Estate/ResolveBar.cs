using DD2Estate.Dd1;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's resolve level bar (shared/resolve_level_bar): the badge of the level with its number, and hanging
    /// from it the holder of the experience bar, whose 16x40 window fills from the foot with the share of the
    /// way to the next level. Places come from resolve_level_bar.layout.darkest
    /// (<see cref="RaidResultsLayout"/>), the fill's two colours from base.colours.darkest
    /// (resolve_gradient_top over resolve_gradient_bottom).
    ///
    /// On the results screen the bar runs from the experience a hero had to what the quest left them with; a
    /// threshold passed on the way puts up the next level's badge, lights it for a moment with its glowing
    /// twin and starts DD1's pulse behind it (fx/raid_results_resolve_pulse), and the window fills again
    /// from its foot. Built to stand in a roster row as well: <see cref="Set(int)"/> is all that needs.
    /// </summary>
    internal class ResolveBar
    {
        private const string Dir = "shared/resolve_level_bar/";
        private static readonly Vector2 BadgeArt = new Vector2(64f, 64f);       // resolve_level_bar_number_background_lvl*.png
        private static readonly Vector2 HolderArt = new Vector2(37f, 59f);      // resolve_level_bar_mask.png
        // The mod's own number: how long the glowing twin of a new level's badge takes to fade (DD1's files name no time for it).
        private const float GlowSeconds = 0.8f;

        private static Sprite _gradient;

        private readonly RectTransform _root;
        private readonly Image _badge, _glow, _fill;
        private readonly TextMeshProUGUI _number;
        private readonly Vector2 _pulseAt;
        private SpineView _pulse;
        private float _from, _to, _seconds, _elapsed, _glowLeft;
        // _counting: a level passed now is a level gained (the bar is on its way, or was cut short to its end)
        private bool _running, _counting;

        /// <summary>The level the badge shows now.</summary>
        public int Level { get; private set; } = -1;
        /// <summary>The window's fill, 0..1.</summary>
        public float Share { get; private set; }
        /// <summary>A level was gained while the bar ran (the badge is lit, the pulse plays).</summary>
        public bool Gained { get; private set; }
        public bool Running => _running;

        /// <param name="origin">The bar's origin in the parent (pixels from its top left corner, y down): what DD1's layouts call resolve_level_bar_offset.</param>
        public ResolveBar(Transform parent, Vector2 origin)
        {
            var l = RaidResultsLayout.Current;
            _root = UiKit.Rect("ResolveBar", parent);
            _root.PlaceTopLeft(origin, Dd1Ui.TopLeft, Vector2.zero);

            // back to front: the pulse of a level gained, the fill, the holder with its window, the badge, its number
            _pulseAt = l.Pulse;
            var holder = l.BarHolder + l.BarMask;
            _fill = UiKit.Image("Fill", _root, Gradient());
            ((RectTransform)_fill.transform).PlaceTopLeft(l.BarHolder + l.BarFill, Dd1Ui.TopLeft, l.BarFillSize);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Vertical;
            _fill.fillOrigin = (int)Image.OriginVertical.Bottom;
            _fill.fillAmount = 0f;
            Dd1Ui.Art("Holder", _root, Dir + "resolve_level_bar_mask.png", holder, HolderArt);
            _badge = Dd1Ui.Art("Badge", _root, Dir + "resolve_level_bar_number_background_lvl0.png", l.BadgeBack, BadgeArt, new Color(0.2f, 0.2f, 0.2f));
            _glow = Dd1Ui.Art("Glow", _root, Dir + "resolve_level_bar_number_background_glow_lvl0.png", l.BadgeBack, BadgeArt);
            _glow.gameObject.SetActive(false);
            _number = UiKit.Text("Level", _root, "", "resolve_number", Dd1Fonts.Colour("resolve_number", Color.black));
            _number.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)_number.transform).PlaceTopLeft(l.BadgeNumber, Dd1Ui.Middle, new Vector2(40f, 28f));
        }

        // The window's fill: DD1's two colours, top over bottom.
        private static Sprite Gradient()
        {
            if (_gradient != null) return _gradient;
            const int height = 40;
            var top = Dd1Fonts.Colour("resolve_gradient_top", new Color32(0xae, 0xa9, 0x96, 255));
            var bottom = Dd1Fonts.Colour("resolve_gradient_bottom", new Color32(0x6d, 0x6a, 0x5e, 255));
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                name = "resolve bar fill", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[height];
            for (var y = 0; y < height; y++) pixels[y] = Color.Lerp(bottom, top, y / (height - 1f));
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _gradient = Sprite.Create(texture, new Rect(0f, 0f, 1f, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _gradient.name = texture.name;
            _gradient.hideFlags = HideFlags.HideAndDontSave;
            return _gradient;
        }

        /// <summary>Shows a sum of experience as it stands: its level's badge, its share of the way on.</summary>
        public void Set(int experience)
        {
            _running = _counting = false;
            Show(experience);
        }

        /// <summary>Runs the bar from one sum of experience to another over <paramref name="seconds"/>; <see cref="Tick"/> moves it.</summary>
        public void Animate(int from, int to, float seconds)
        {
            _from = from;
            _to = Mathf.Max(from, to);
            _seconds = Mathf.Max(0.01f, seconds);
            _elapsed = 0f;
            _running = _counting = false;
            Show(_from);
            _running = _counting = _to > _from;
        }

        /// <summary>Once a frame while the screen is up. False when the bar has come to rest.</summary>
        public bool Tick(float seconds)
        {
            if (_glowLeft > 0f)
            {
                _glowLeft -= seconds;
                var alpha = Mathf.Clamp01(_glowLeft / GlowSeconds);
                _glow.color = new Color(1f, 1f, 1f, alpha);
                if (_glowLeft <= 0f) _glow.gameObject.SetActive(false);
            }
            if (!_running) return false;
            _elapsed += seconds;
            // DD1's file gives the bar a time and no curve: it runs evenly
            Show(Mathf.Lerp(_from, _to, Mathf.Clamp01(_elapsed / _seconds)));
            if (_elapsed >= _seconds) _running = false;
            return _running;
        }

        /// <summary>Puts a running bar at its end at once (the click that cuts a reveal short).</summary>
        public void Finish()
        {
            if (!_running) return;
            _running = false;
            Show(_to);
        }

        private void Show(float experience)
        {
            // experience between two whole points still belongs to the level of the point below it
            var level = Mathf.Clamp(Resolve.LevelOf(Mathf.FloorToInt(experience + 0.0001f)), 0, Resolve.MaxLevel);
            float floor = Resolve.Threshold(level), ceiling = level < Resolve.MaxLevel ? Resolve.Threshold(level + 1) : floor;
            Share = ceiling > floor ? Mathf.Clamp01((experience - floor) / (ceiling - floor)) : 1f;
            _fill.fillAmount = Share;
            if (level == Level) return;
            var gained = _counting && Level >= 0 && level > Level;
            Level = level;
            var art = Dd1Ui.Sprite(Dir + "resolve_level_bar_number_background_lvl" + level + ".png");
            if (art != null) _badge.sprite = art;
            _number.text = level.ToString();
            if (!gained) return;

            // DD1 says a level gained without a word: the next badge, lit, and the pulse behind it.
            Gained = true;
            _number.color = Dd1Fonts.Colour("resolve_new", Color.white);
            var glow = Dd1Ui.Sprite(Dir + "resolve_level_bar_number_background_glow_lvl" + level + ".png");
            if (glow != null)
            {
                _glow.sprite = glow;
                _glow.color = Color.white;
                _glow.gameObject.SetActive(true);
                _glowLeft = GlowSeconds;
            }
            if (_pulse == null)
            {
                _pulse = SpineView.Create("Pulse", _root, SpineView.Fx("raid_results_resolve_pulse"), _pulseAt);
                if (_pulse != null) _pulse.transform.SetAsFirstSibling();
            }
            _pulse?.Play("pulse", true);
        }
    }
}
