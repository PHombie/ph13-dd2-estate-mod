using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A kind of DD1's pop text. Everything about a kind goes by its one name in DD1's files:
    /// scripts/layout/base.popup_text.layout.darkest has its motion (pop_text_layout_&lt;kind&gt;: start_offset,
    /// pop_y_offset, pop_time), colours/base.colours.darkest its two colours (pop_text_&lt;kind&gt; for the glyphs,
    /// pop_text_outline_&lt;kind&gt; for the outline around them), overlays/poptext_&lt;kind&gt;.png the icon beside
    /// its word where DD1 has one, and the string tables the word (str_ui_&lt;kind&gt; for most). A kind that
    /// shows a number has no word. The numbers written beside each kind are DD1's stock values: FALLBACKS.
    /// Of DD1's 42 kinds these are the ones a hero meets outside a fight.
    /// </summary>
    internal sealed class RaidPop
    {
        private const string LayoutFile = "scripts/layout/base.popup_text.layout.darkest";

        public readonly string Kind;
        public readonly string TextId, TextFallback;
        private readonly Vector2 _start;
        private readonly float _rise, _time;
        private readonly Color _fill, _rim;

        private static readonly List<RaidPop> Known = new List<RaidPop>();
        private static DarkestFile _layout;
        private static bool _read;

        private RaidPop(string kind, string textId, string textFallback, float startY, float rise, float time, uint fill, uint rim)
        {
            Kind = kind;
            TextId = textId;
            TextFallback = textFallback;
            _start = new Vector2(0f, startY);
            _rise = rise;
            _time = time;
            _fill = Rgb(fill);
            _rim = Rgb(rim);
            Known.Add(this);
        }

        public static readonly RaidPop Damage = new RaidPop("damage", null, null, 20f, 100f, 1.5f, 0xe30900, 0x390000);
        public static readonly RaidPop Heal = new RaidPop("hero_heal", null, null, 20f, 100f, 1.5f, 0x4fcb4f, 0x2a4416);
        public static readonly RaidPop Stress = new RaidPop("stress_damage", null, null, 120f, -80f, 1f, 0x000000, 0xc2c2c2);
        public static readonly RaidPop StressHeal = new RaidPop("stress_reduce", null, null, 40f, 80f, 1f, 0xfdf8c7, 0x7c663f);
        public static readonly RaidPop Full = new RaidPop("full", "str_full", "Full!", 80f, 100f, 0.8f, 0x000000, 0x9a988f);
        public static readonly RaidPop Cured = new RaidPop("cured", "str_ui_cured", "Cured!", 80f, 100f, 0.8f, 0xfdf8c7, 0x7c663f);
        public static readonly RaidPop Buff = new RaidPop("buff", "str_ui_buff", "Buff!", 80f, 100f, 0.8f, 0x79f8ff, 0x127298);
        public static readonly RaidPop Debuff = new RaidPop("debuff", "str_ui_debuff", "Debuff!", 80f, 100f, 0.8f, 0xe26826, 0x7f290b);
        /// <summary>A hero at death's door takes a blow and lives: DD1 writes "Death's Door!" over them.</summary>
        public static readonly RaidPop DeathAvoided = new RaidPop("death_avoided", "str_ui_death_avoided", "Death's Door!", 80f, 100f, 1.5f, 0x920400, 0x1e0400);
        public static readonly RaidPop Deathblow = new RaidPop("deathblow", "str_ui_deathblow", "DEATHBLOW!", 80f, 100f, 1.5f, 0xff7100, 0x480800);

        public static IReadOnlyList<RaidPop> All => Known;

        /// <summary>A kind by DD1's name for it, or by the short names the dev bridge takes (heal, stress, stress_heal).</summary>
        public static RaidPop Find(string name)
        {
            switch (name)
            {
                case "heal": return Heal;
                case "stress": return Stress;
                case "stress_heal": return StressHeal;
            }
            foreach (var known in Known)
                if (known.Kind == name) return known;
            return null;
        }

        // Read once the DD1 install is found; before that the stock values answer.
        private static DarkestFile Layout
        {
            get
            {
                if (!_read && Dd1Install.Found)
                {
                    _read = true;
                    _layout = DarkestFile.Load(LayoutFile);
                }
                return _layout;
            }
        }

        private DarkestFile.Block Block => Layout?.Find("pop_text_layout_" + Kind);

        /// <summary>Where the text starts from the hero's middle, x to the right and y UP.</summary>
        public Vector2 Start
        {
            get
            {
                var block = Block;
                return block != null && block.Values("start_offset").Count >= 2 ? block.Vector2("start_offset") : _start;
            }
        }

        /// <summary>How far it travels upwards (a stress number sinks: below 0) in <see cref="Time"/> seconds.</summary>
        public float Rise => Block != null && Block.Has("pop_y_offset") ? Block.Float("pop_y_offset", 0, _rise) : _rise;

        public float Time => Block != null && Block.Has("pop_time") ? Block.Float("pop_time", 0, _time) : _time;

        public Color Fill => Dd1Fonts.Colour("pop_text_" + Kind, _fill);

        public Color Rim => Dd1Fonts.Colour("pop_text_outline_" + Kind, _rim);

        /// <summary>overlays/poptext_&lt;kind&gt;.png; null for the kinds DD1 has no picture for.</summary>
        public Sprite Icon => RaidUi.Sprite("overlays/poptext_" + Kind + ".png");

        /// <summary>DD1's word for the kind; null for a kind that shows a number.</summary>
        public string Word => TextId != null ? RaidText.Format(RaidText.Get(TextId, TextFallback)) : null;

        /// <summary>pop_text_shared.type_limit_delay_time: two texts of one kind over one hero start this far apart.</summary>
        public static float TypeDelay
        {
            get
            {
                var shared = Layout?.Find("pop_text_shared");
                return shared != null && shared.Has("type_limit_delay_time") ? shared.Float("type_limit_delay_time", 0, 0.3f) : 0.3f;
            }
        }

        /// <summary>pop_text_shared.icon_offset: the gap between a kind's picture and its word.</summary>
        public static Vector2 IconOffset
        {
            get
            {
                var shared = Layout?.Find("pop_text_shared");
                return shared != null && shared.Values("icon_offset").Count >= 2 ? shared.Vector2("icon_offset") : new Vector2(5f, 0f);
            }
        }

        private static Color Rgb(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    /// <summary>
    /// DD1's pop text: the numbers and words that rise over a hero when something changes their health, their
    /// stress, their quirks or their buffs (a trap, hunger, a curio, a camping skill, an item from the bag). Set
    /// in DD1's own outlined font (fonts/popup.fnt, style pop_text) in the two colours of the kind, drawn 1:1,
    /// moving as the kind's layout says (<see cref="RaidPop"/>).
    ///
    /// What DD1's files do not say, and the mod's reading of it (docs/recon/dd1-raid-ui.md, 9): the offsets are
    /// counted from the hero's middle (status_bars.icon_world_y_offset above the ground, where DD1 hangs what it
    /// shows on a hero) with y upwards, to the middle of the text; the travel is even; the text fades over the
    /// last quarter of its time; the picture stands left of the word.
    /// </summary>
    internal class RaidPopText
    {
        private const float IconSize = 64f;                 // overlays/poptext_*.png

        // The mod's own numbers.
        private const float FadeShare = 0.25f;              // the share of its time a text fades over, at the end
        private const float FallbackSize = 62f;             // without DD1's font: DD2's, at about the popup font's size
        private const float EdgeMargin = 8f;                // a text keeps this far inside the screen's sides
        private const int Most = 24;                        // texts on screen at once; older ones go first

        /// <summary>Dev bridge: the height above a hero's feet the offsets are counted from; below 0: DD1's icon_world_y_offset.</summary>
        public static float Anchor = -1f;

        private class Pop
        {
            public uint Hero;
            public RaidPop Kind;
            public string Text;
            public RectTransform Root;
            public TextMeshProUGUI Fill, Rim;
            public Image Icon;
            public Vector2 At, Start;
            public float Rise, Time, Age, Delay;
        }

        private readonly RectTransform _root;
        private readonly RaidTrays _trays;
        private readonly List<Pop> _pops = new List<Pop>();
        // when the next text of a kind may start over a hero (unscaled time)
        private readonly Dictionary<string, float> _next = new Dictionary<string, float>();

        public RaidPopText(RectTransform screen, RaidTrays trays)
        {
            _trays = trays;
            _root = UiKit.Rect("PopText", screen);
            UiKit.Stretch(_root);
            // the font is a page to decode and two atlases to fill: before the first number is due, not with it
            if (UiKit.UseDd1Fonts) Dd1Fonts.Font(Dd1Fonts.FontOf("pop_text") ?? Dd1Fonts.Popup);
        }

        public int Count => _pops.Count;

        /// <summary>A text over a hero: the kind's own word, or <paramref name="text"/> (a number, a word with a name in it).</summary>
        public bool Show(uint hero, RaidPop kind, string text = null)
        {
            if (kind == null) return false;
            text = string.IsNullOrEmpty(text) ? kind.Word : text;
            if (string.IsNullOrEmpty(text)) return false;
            if (!_trays.Place(hero, Height, out var at)) return false;
            while (_pops.Count >= Most) Remove(0);

            var pop = new Pop { Hero = hero, Kind = kind, Text = text, At = at, Start = kind.Start, Rise = kind.Rise, Time = Mathf.Max(0.05f, kind.Time) };
            var key = hero + ":" + kind.Kind;
            var now = UnityEngine.Time.unscaledTime;
            var start = _next.TryGetValue(key, out var free) ? Mathf.Max(now, free) : now;
            _next[key] = start + RaidPop.TypeDelay;
            pop.Delay = start - now;
            Build(pop);
            _pops.Add(pop);
            Move(pop);
            pop.Root.gameObject.SetActive(pop.Delay <= 0f);
            return true;
        }

        private static float Height => Anchor >= 0f ? Anchor : RaidLayout.Current.HeroMiddle;

        private void Build(Pop pop)
        {
            var kind = pop.Kind;
            // FALLBACK: fonts.darkest sets pop_text in the popup font
            var style = Dd1Fonts.FontOf("pop_text") != null ? "pop_text" : Dd1Fonts.Popup;
            var font = UiKit.UseDd1Fonts ? Dd1Fonts.Style(style) : null;
            pop.Root = UiKit.Rect("Pop." + kind.Kind, _root);
            pop.Fill = UiKit.Text("Fill", pop.Root, pop.Text, style, kind.Fill, TextAlignmentOptions.TopLeft, font != null ? 0f : FallbackSize);
            Plain(pop.Fill);
            if (font?.Outline != null)
            {
                // the outline in its own colour, under the glyphs (see Dd1Font.Outline)
                pop.Rim = UiKit.Text("Rim", pop.Root, pop.Text, style, kind.Rim, TextAlignmentOptions.TopLeft);
                Dd1Fonts.Set(pop.Rim, font.Outline);
                Plain(pop.Rim);
                pop.Rim.transform.SetAsFirstSibling();
            }

            var line = font != null ? font.LineHeight : Mathf.Ceil(pop.Fill.fontSize * 1.2f);
            var width = Mathf.Ceil((pop.Rim != null ? pop.Rim : pop.Fill).GetPreferredValues(pop.Text).x);
            var sprite = kind.Icon;
            var gap = RaidPop.IconOffset;
            var lead = sprite != null ? IconSize + gap.x : 0f;
            pop.Root.PlaceTopLeft(Vector2.zero, RaidUi.Middle, new Vector2(lead + width, line));
            ((RectTransform)pop.Fill.transform).PlaceTopLeft(new Vector2(lead, 0f), RaidUi.TopLeft, new Vector2(width + 8f, line));
            if (pop.Rim != null) ((RectTransform)pop.Rim.transform).PlaceTopLeft(new Vector2(lead, 0f), RaidUi.TopLeft, new Vector2(width + 8f, line));
            if (sprite != null)
            {
                pop.Icon = UiKit.Image("Icon", pop.Root, sprite);
                ((RectTransform)pop.Icon.transform).PlaceTopLeft(new Vector2(0f, (line - IconSize) * 0.5f + gap.y), RaidUi.TopLeft, new Vector2(IconSize, IconSize));
            }
        }

        private static void Plain(TextMeshProUGUI label)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
        }

        /// <summary>Every frame: the texts travel with their heroes and go when their time is up.</summary>
        public void Tick(float dt)
        {
            for (var i = _pops.Count - 1; i >= 0; i--)
            {
                var pop = _pops[i];
                if (pop.Delay > 0f)
                {
                    pop.Delay -= dt;
                    if (pop.Delay > 0f) continue;
                    pop.Root.gameObject.SetActive(true);
                }
                else pop.Age += dt;
                if (pop.Age >= pop.Time)
                {
                    Remove(i);
                    continue;
                }
                Move(pop);
            }
        }

        private void Move(Pop pop)
        {
            // a hero who is gone (fallen) leaves the text where they stood
            if (_trays.Place(pop.Hero, Height, out var at)) pop.At = at;
            var share = Mathf.Clamp01(pop.Age / pop.Time);
            // DD1's last hero stands 284 px in; the corridor's can stand at the screen's edge, and a word is wide
            var half = pop.Root.sizeDelta.x * 0.5f + EdgeMargin;
            var x = half * 2f < 1920f ? Mathf.Clamp(pop.At.x + pop.Start.x, half, 1920f - half) : 960f;
            var y = pop.At.y - pop.Start.y - pop.Rise * share;
            pop.Root.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
            var alpha = Mathf.Clamp01((1f - share) / FadeShare);
            pop.Fill.alpha = alpha;
            if (pop.Rim != null) pop.Rim.alpha = alpha;
            if (pop.Icon != null) pop.Icon.color = new Color(1f, 1f, 1f, alpha);
        }

        private void Remove(int index)
        {
            var pop = _pops[index];
            _pops.RemoveAt(index);
            if (pop.Root == null) return;
            pop.Root.gameObject.SetActive(false);
            Object.Destroy(pop.Root.gameObject);
        }

        /// <summary>Takes every text away (the screen is hidden, the expedition over).</summary>
        public void Clear()
        {
            for (var i = _pops.Count - 1; i >= 0; i--) Remove(i);
            _next.Clear();
        }

        public object Describe()
        {
            var texts = new List<object>();
            foreach (var pop in _pops)
            {
                texts.Add(new
                {
                    hero = pop.Hero,
                    kind = pop.Kind.Kind,
                    text = pop.Text,
                    at = new[] { pop.Root.anchoredPosition.x, -pop.Root.anchoredPosition.y },
                    size = new[] { pop.Root.sizeDelta.x, pop.Root.sizeDelta.y },
                    age = pop.Age,
                    time = pop.Time,
                    waits = pop.Delay > 0f ? pop.Delay : 0f,
                    outlined = pop.Rim != null,
                    icon = pop.Icon != null
                });
            }
            return new { anchor = Height, typeDelay = RaidPop.TypeDelay, texts };
        }
    }
}
