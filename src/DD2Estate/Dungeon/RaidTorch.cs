using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's torch meter at the top of the screen: overlays/torch.png (the grey spiked ring, the black torch, the
    /// bar with its diamond marks) hanging from torch_layout.pos_y, the two gauges running out from its middle
    /// (gauge_offset, gauge_size) in the colours torch_centre to torch_ends of colours/base.colours.darkest, and
    /// DD1's own flame on the torch's head (flamepos; <see cref="RaidTorchFlame"/>). Checked against DD1's own
    /// frames at five light levels (_lab/dd1_ref/raid/raid_torch_level_0..4_clean.png).
    ///
    /// The pointer over the torch or its strip (torch_info's two hot areas) is told what DD1 tells there (its
    /// frames raid_torch_level_*_tooltip.png): the light level's name with the light in brackets ("DARK ( 12 )"),
    /// a line for everything the level changes with as many plus signs as the change has steps in
    /// shared/rules.json's darkness table (the ill ones in DD1's harmful red, the good ones in its notable
    /// gold), and DD1's two lines on turning the light down, which a shift-click on the torch then does. The
    /// box hangs torch_info.tooltip_offset from the foot of the torch's picture, its text centred.
    ///
    /// Since 2026-10-06 the torch on screen is DD2's ([Look] Torch = dd2, the owner's final word): the widget of
    /// DD2's fights (<see cref="Dd2Torch"/>) stands in the place and at the size a fight has it, so that nothing
    /// jumps when a fight begins or ends, and DD1's picture, gauges and flame are put away. What the pointer is
    /// told and the shift-click are the same: the two hot areas lie over DD2's crown and over its bar, and the
    /// box hangs under DD2's torch. With [Look] Torch = dd1, and while DD2's widget is not to be had, DD1's
    /// torch is the one on screen.
    /// </summary>
    internal class RaidTorch
    {
        private static readonly Vector2 ArtSize = new Vector2(900f, 188f);      // overlays/torch.png
        private const int GradientWidth = 128;
        private const float TooltipWidth = 420f;            // the mod's own: DD1's longest line ("[SHIFT+CTRL+CLICK] to snuff out torch") on one line
        private const float Dd2TooltipGap = 6f;             // the mod's own: the box hangs this far under the foot of DD2's torch

        // What a light level changes, as DD1's tooltip lists it: the number of shared/rules.json's darkness table
        // that sets the plus signs, DD1's word for it, and whether it is against the party. Order as in DD1's frames.
        private static readonly string[][] Lines =
        {
            new[] { "stress_damage_increase", "str_darkness_stress", "Stress", "bad" },
            new[] { "monster_attack_increase", "str_darkness_monster", "Monster ACC and DMG", "bad" },
            new[] { "monster_crit_increase", "str_darkness_monster_crit", "Monster Crits", "bad" },
            new[] { "heroes_surprised_increase", "str_darkness_heroesSurprised", "Heroes Surprised", "bad" },
            new[] { "loot_increase_gold", "str_darkness_loot", "Loot", "good" },
            new[] { "player_crit_increase", "str_darkness_player_crit", "Player Crits", "good" },
            new[] { "player_def_increase", "str_darkness_player_def", "Dodge", "good" },
            new[] { "player_scouting_increase", "str_darkness_player_scout", "Scouting", "good" },
            new[] { "monsters_surprised_increase", "str_darkness_monstersSurprised", "Monsters Surprised", "good" }
        };

        private readonly RaidTooltip _tooltip;
        private readonly RectTransform _screen, _group, _root;
        private readonly CanvasGroup _fade;
        private Dd2Torch.View _dd2;         // DD2's own torch, once the game has handed it over (see Dd2Torch)
        private CanvasGroup _dd2Fade;
        private RectTransform _hot, _hotStrip;
        private bool _hotOnDd2;
        private int _made;                  // which copy of DD2's widget the one on screen was made from
        private float _share = 1f;          // the light the torch shows, 0..1
        private int _flares = ShownLight.Flares;
        private float _size = 1f;
        private readonly RectTransform _left, _right;
        private readonly RawImage _leftImage, _rightImage, _flameImage;
        private readonly Texture2D _gradient;
        private readonly RaidTorchFlame _flame;
        private float _shown = -1f;
        private double _light;
        private LightBand _band;
        private IReadOnlyList<LightBand> _bands;

        /// <summary>The player turns the light down with a shift-click on the torch: a step (false), or out (true).</summary>
        public Action<bool> Reduce;

        public RaidTorch(RectTransform screen, RaidTooltip tooltip)
        {
            _tooltip = tooltip;
            _screen = screen;
            Dd2Torch.Prepare();
            var l = RaidLayout.Current;
            // everything of the torch in one group: DD1 fades it with the scene on the way through a door, and takes
            // it away with the rest of the HUD while a hero is shown close
            _group = UiKit.Rect("Torch", screen);
            _group.PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, new Vector2(1920f, 1080f));
            _fade = _group.gameObject.AddComponent<CanvasGroup>();
            _fade.blocksRaycasts = false;
            _fade.interactable = false;

            var at = new Vector2(l.XCentre - ArtSize.x * 0.5f, l.TorchY);
            var root = _root = UiKit.Rect("Art", _group);
            root.PlaceTopLeft(at, RaidUi.TopLeft, ArtSize);

            // DD1's flame burns behind the torch's black head and in front of nothing else
            _flame = new RaidTorchFlame();
            var flame = UiKit.Rect("Flame", _group);
            flame.PlaceTopLeft(new Vector2(l.TorchFlame.x - RaidTorchFlame.Emitter.x, l.TorchFlame.y - (RaidTorchFlame.Size - RaidTorchFlame.Emitter.y)), RaidUi.TopLeft, new Vector2(RaidTorchFlame.Size, RaidTorchFlame.Size));
            _flameImage = flame.gameObject.AddComponent<RawImage>();
            _flameImage.texture = _flame.Texture;
            _flameImage.raycastTarget = false;
            flame.SetAsFirstSibling();

            RaidUi.Art("Picture", root, "overlays/torch.png", Vector2.zero, ArtSize);
            _gradient = new Texture2D(GradientWidth, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            // the right gauge grows from the torch's middle to the right, the left one is its mirror image
            _right = UiKit.Rect("GaugeRight", root);
            _right.PlaceTopLeft(new Vector2(ArtSize.x - l.TorchGaugeOffset.x - l.TorchGaugeSize.x, l.TorchGaugeOffset.y), RaidUi.TopLeft, l.TorchGaugeSize);
            _rightImage = _right.gameObject.AddComponent<RawImage>();
            _left = UiKit.Rect("GaugeLeft", root);
            _left.PlaceTopLeft(new Vector2(l.TorchGaugeOffset.x + l.TorchGaugeSize.x, l.TorchGaugeOffset.y), RaidUi.TopRight, l.TorchGaugeSize);
            _leftImage = _left.gameObject.AddComponent<RawImage>();
            _leftImage.uvRect = new Rect(1f, 0f, -1f, 1f);
            _rightImage.texture = _leftImage.texture = _gradient;
            _rightImage.raycastTarget = _leftImage.raycastTarget = false;

            _hot = Hot("Hot", screen);
            _hotStrip = Hot("HotStrip", screen);
            PlaceHot(false);
        }

        private RectTransform Hot(string name, Transform parent)
        {
            var area = UiKit.Image(name, parent, null, Color.clear, true);
            RaidUi.Pointer(area, () =>
            {
                // DD1: "[SHIFT+CLICK] to reduce torch", "[SHIFT+CTRL+CLICK] to snuff out torch"
                if (RaidUi.ShiftHeld) Reduce?.Invoke(RaidUi.ControlHeld);
            }, null, inside =>
            {
                if (inside && _fade.alpha > 0.5f) _tooltip.Show(this, Words(), TooltipAt(), TooltipWidth, centred: true);
                else _tooltip.Hide(this);
            });
            return (RectTransform)area.transform;
        }

        // True while DD2's widget is the torch on screen.
        private bool Dd2Shown => _dd2 != null && _dd2.Root != null && Dd2Torch.Enabled;

        // The two areas that take the pointer: DD1's (torch_info: over the torch, and over the gauge's strip), or
        // the same two over DD2's crown and bar.
        private void PlaceHot(bool dd2)
        {
            _hotOnDd2 = dd2;
            var l = RaidLayout.Current;
            if (dd2)
            {
                var crown = _dd2.CrownArea;
                var bar = _dd2.BarArea;
                _hot.PlaceTopLeft(crown.position, RaidUi.TopLeft, crown.size);
                _hotStrip.PlaceTopLeft(bar.position, RaidUi.TopLeft, bar.size);
                return;
            }
            _hot.PlaceTopLeft(new Vector2(l.XCentre - l.TorchHotSize.x * 0.5f + l.TorchHot.x, l.TorchY + l.TorchHot.y), RaidUi.TopLeft, l.TorchHotSize);
            _hotStrip.PlaceTopLeft(new Vector2(l.XCentre - l.TorchStripHotSize.x * 0.5f + l.TorchStripHot.x, l.TorchY + l.TorchStripHot.y), RaidUi.TopLeft, l.TorchStripHotSize);
        }

        // Where the tooltip's box hangs from: torch_info.tooltip_offset from the foot of DD1's picture (DD1's
        // frames: the box's top at 146), or just under the foot of DD2's torch.
        private Vector2 TooltipAt()
        {
            var l = RaidLayout.Current;
            if (Dd2Shown) return new Vector2(l.XCentre, _dd2.CrownArea.yMax + Dd2TooltipGap);
            return new Vector2(l.XCentre + l.TorchTooltip.x, l.TorchY + ArtSize.y + l.TorchTooltip.y);
        }

        /// <summary>
        /// How much of the torch is drawn, 1 all .. 0 nothing: it fades with the scene on the way through a door
        /// and goes with the HUD while a hero is shown close.
        /// </summary>
        public float Alpha
        {
            set
            {
                value = Mathf.Clamp01(value);
                if (!Mathf.Approximately(_fade.alpha, value)) _fade.alpha = value;
                if (_dd2Fade != null && !Mathf.Approximately(_dd2Fade.alpha, value)) _dd2Fade.alpha = value;
                if (value <= 0.5f) _tooltip.Hide(this);
            }
        }

        public void Show(double light, LightBand band, IReadOnlyList<LightBand> bands = null)
        {
            _light = light;
            _band = band;
            if (bands != null) _bands = bands;
            // (what the gauge shows of it is drawn a frame at a time: Flicker)
        }

        // DD1's gauge at a share of the full light: its two bars' length and their colours.
        private void DrawGauge(float share)
        {
            if (Mathf.Approximately(share, _shown)) return;
            _shown = share;
            var l = RaidLayout.Current;
            var length = Mathf.Round(l.TorchGaugeSize.x * share);
            _right.sizeDelta = _left.sizeDelta = new Vector2(length, l.TorchGaugeSize.y);
            // the gauge keeps its colours by distance from the torch and ends softly (fade_amount of its full length)
            var centre = Dd1Fonts.Colour("torch_centre", new Color32(0xff, 0x7a, 0x00, 255));
            var ends = Dd1Fonts.Colour("torch_ends", new Color32(0x5f, 0x04, 0x00, 255));
            var fade = Mathf.Max(1f, l.TorchFade * l.TorchGaugeSize.x);
            for (var x = 0; x < GradientWidth; x++)
            {
                var u = x / (GradientWidth - 1f);
                var colour = Color.Lerp(centre, ends, u * share);
                colour.a = Mathf.Clamp01((1f - u) * length / fade);
                _gradient.SetPixel(x, 0, colour);
            }
            _gradient.Apply();
        }

        /// <summary>
        /// Every frame: the gauge goes towards the light and the flame burns, higher the brighter the light.
        /// The light the rules have jumps (a torch lit, a tile's burn); what the torch shows of it slides
        /// (<see cref="ShownLight"/>: the dev bridge's light for the view, corridor.torch level=, is shown as it
        /// is: a picture of the corridor at a light level has the torch of that level in it).
        /// </summary>
        public void Flicker()
        {
            _share = ShownLight.GaugeShare;
            if (_dd2 == null && Dd2Torch.Enabled) Dd2Torch.Prepare();
            // the copy was made again (the dev bridge), or the display changed its shape: the one on screen is the old one
            if (_dd2 != null && (_dd2.Root == null || _made != Dd2Torch.Made || !Mathf.Approximately(_size, Dd2Hud.Scale)))
            {
                _dd2.Destroy();
                _dd2 = null;
                _dd2Fade = null;
            }
            if (_dd2 == null && Dd2Torch.Enabled && Dd2Torch.Ready)
            {
                _dd2 = Dd2Torch.Create(_screen);
                if (_dd2 != null)
                {
                    _made = Dd2Torch.Made;
                    _size = Dd2Hud.Scale;
                    // in the place of DD1's picture, under whatever was laid over it
                    _dd2.Root.SetSiblingIndex(_group.GetSiblingIndex());
                    _dd2Fade = _dd2.Root.gameObject.GetComponent<CanvasGroup>() ?? _dd2.Root.gameObject.AddComponent<CanvasGroup>();
                    _dd2Fade.alpha = _fade.alpha;
                    _dd2Fade.blocksRaycasts = false;
                }
            }
            var wanted = Dd2Shown;
            if (_dd2 != null && _dd2.Root != null && _dd2.Root.gameObject.activeSelf != wanted) _dd2.Root.gameObject.SetActive(wanted);
            if (_group.gameObject.activeSelf == wanted) _group.gameObject.SetActive(!wanted);
            if (_hotOnDd2 != wanted)
            {
                _tooltip.Hide(this);
                PlaceHot(wanted);
            }
            // a change of the light the torch answers itself (a torch lit, a torch put down, another band of light)
            var flared = _flares != ShownLight.Flares;
            _flares = ShownLight.Flares;
            if (wanted)
            {
                // the game's shader moves the flame
                _dd2.Set(_share);
                if (flared) _dd2.Flare(ShownLight.FlareUp);
                return;
            }
            DrawGauge(_share);
            if (_fade.alpha > 0.01f) _flame.Tick(Time.unscaledDeltaTime, Mathf.Max(0f, _shown));
        }

        /// <summary>Dev bridge: what the torch on screen shows of the light right now.</summary>
        public object DescribeShown()
        {
            return new { light = _light, share = _share, dd2 = Dd2Shown, dd2View = Dd2Shown ? _dd2.Describe() : null, dd1GaugePixels = _right.sizeDelta.x };
        }

        /// <summary>DD1's five levels by the band's lower edge in shared/rules.json (75, 50, 25, 0, below).</summary>
        public int BandNumber
        {
            get
            {
                if (_band == null) return 0;
                return _band.Lower >= 75 ? 0 : _band.Lower >= 50 ? 1 : _band.Lower >= 25 ? 2 : _band.Lower >= 0 ? 3 : 4;
            }
        }

        // How many steps of a number's ladder the level stands on: the number's place among the values the
        // darkness table gives it in its bands, the smallest that is not nought being one. DD1's tooltip
        // writes a plus sign a step ("+++Stress" in the dark, "+Stress" in dim light).
        private int Steps(string key)
        {
            var value = _band != null ? _band.Value(key) : 0;
            if (value <= 0) return 0;
            var smaller = new HashSet<double>();
            if (_bands != null)
                foreach (var band in _bands)
                {
                    var other = band.Value(key);
                    if (other > 0 && other < value) smaller.Add(other);
                }
            return smaller.Count + 1;
        }

        /// <summary>The tooltip's text (TextMeshPro rich text), as DD1 words it.</summary>
        public string Words()
        {
            var text = new StringBuilder();
            text.Append(RaidText.Get("str_darkness_title_" + BandNumber, "Torchlight")).Append(" ( ").Append(Mathf.RoundToInt((float)_light).ToString(CultureInfo.InvariantCulture)).Append(" )");
            foreach (var line in Lines)
            {
                var steps = Steps(line[0]);
                if (steps <= 0) continue;
                text.Append("\n<color=").Append(RaidText.Hex(line[3] == "bad" ? UiKit.Harmful : UiKit.Notable)).Append('>').Append('+', steps).Append(RaidText.Get(line[1], line[2])).Append("</color>");
            }
            text.Append("\n<color=").Append(RaidText.Hex(Dd1Fonts.Colour("torch_reduce_tip", new Color32(70, 70, 70, 255)))).Append('>').Append(RaidText.Get("str_reduce_torch_tip", "[SHIFT+CLICK] to reduce torch")).Append("</color>");
            text.Append("\n<color=").Append(RaidText.Hex(Dd1Fonts.Colour("torch_snuff_tip", new Color32(70, 70, 70, 255)))).Append('>').Append(RaidText.Get("str_snuff_torch_tip", "[SHIFT+CTRL+CLICK] to snuff out torch")).Append("</color>");
            return text.ToString();
        }

        public object Describe()
        {
            return new
            {
                light = _light, band = BandNumber, dd2 = Dd2Shown, alpha = _fade.alpha, blobs = _flame.Blobs,
                art = new[] { RaidLayout.Current.XCentre - ArtSize.x * 0.5f, RaidLayout.Current.TorchY, ArtSize.x, ArtSize.y },
                // DD2's widget: where its copy came from, where it hangs, and the two areas that take the pointer on it
                dd2Status = Dd2Torch.Status, dd2FromFight = Dd2Torch.FromFight,
                dd2At = Dd2Shown ? new[] { _dd2.Root.anchoredPosition.x, _dd2.Root.anchoredPosition.y, _dd2.Root.localScale.x } : null,
                hot = new[] { _hot.anchoredPosition.x, -_hot.anchoredPosition.y, _hot.sizeDelta.x, _hot.sizeDelta.y },
                hotStrip = new[] { _hotStrip.anchoredPosition.x, -_hotStrip.anchoredPosition.y, _hotStrip.sizeDelta.x, _hotStrip.sizeDelta.y },
                tooltipAt = new[] { TooltipAt().x, TooltipAt().y },
                tooltip = System.Text.RegularExpressions.Regex.Replace(Words(), "<[^>]+>", "")
            };
        }
    }
}
