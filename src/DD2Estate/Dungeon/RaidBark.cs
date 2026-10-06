using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A hero of the party says something, in DD1's speech balloon: scrolls/bark_balloon.png (359x180) with its
    /// tail over the hero and the words in DD1's style "bark" (colour bark_neutral), written a letter at a time
    /// (shared/ui.layout.darkest, bark_layout: offset, tail, text, char_delay, lifetime_min, lifetime_per_char).
    /// One balloon at a time; a new line takes the place of the one that is up.
    ///
    /// DD1's heroes say a great deal (localization/dialogue.string_table.xml); the Estate's heroes are DD2's and
    /// keep to the few lines DD1's interface itself puts in a hero's mouth: "That item had no effect." for an
    /// item a curio has no use for (str_curio_item_had_no_effect, barked by DD1's own code).
    ///
    /// NOT in any file, and so the mod's reading: bark_layout.offset is counted from the hero's feet (x right,
    /// y up) to the point the balloon's tail (bark_layout.tail, a point of its picture) stands on; how the
    /// balloon goes (here: at once).
    /// </summary>
    internal class RaidBark
    {
        private static readonly Vector2 Art = new Vector2(359f, 180f);     // scrolls/bark_balloon.png

        private readonly RaidTrays _trays;
        private readonly RectTransform _root;
        private readonly TextMeshProUGUI _text;
        private uint _hero;
        private float _since, _lifetime;
        private int _letters;

        public RaidBark(RectTransform screen, RaidTrays trays)
        {
            _trays = trays;
            var l = RaidLayout.Current;
            var balloon = RaidUi.Art("Bark", screen, "scrolls/bark_balloon.png", Vector2.zero, Art, new Color(0.05f, 0.05f, 0.05f, 0.9f));
            _root = (RectTransform)balloon.transform;
            var box = l.BarkText;
            _text = UiKit.Text("Words", _root, "", Dd1Fonts.FontOf("bark") != null ? "bark" : "tooltip", Dd1Fonts.Colour("bark_neutral", UiKit.Neutral), TextAlignmentOptions.Center);
            ((RectTransform)_text.transform).PlaceTopLeft(new Vector2(box.xMin, box.yMin), RaidUi.TopLeft, box.size);
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Truncate;
            _text.enableAutoSizing = true;
            _text.fontSizeMax = _text.fontSize;
            _text.fontSizeMin = Mathf.Min(13f, _text.fontSize);
            _text.raycastTarget = false;
            _root.gameObject.SetActive(false);
        }

        public bool Up => _root.gameObject.activeSelf;
        public uint Hero => _hero;
        public string Words => Up ? _text.text : null;

        /// <summary>The hero says a line; false when the hero has no place on the screen (nobody to hang the balloon on).</summary>
        public bool Say(uint hero, string text)
        {
            if (string.IsNullOrEmpty(text) || !_trays.Place(hero, 0f, out _)) return false;
            var l = RaidLayout.Current;
            _hero = hero;
            _text.text = RaidText.Marks(text);
            _text.maxVisibleCharacters = 0;
            _letters = text.Length;
            _since = 0f;
            // the words are written out, and stay for their length after that
            _lifetime = _letters * l.BarkCharDelay + Mathf.Max(l.BarkLifetimeMin, _letters * l.BarkLifetimePerChar);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Follow();
            return true;
        }

        public void Clear()
        {
            if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        /// <summary>Every frame.</summary>
        public void Tick(float dt)
        {
            if (!_root.gameObject.activeSelf) return;
            _since += dt;
            if (_since >= _lifetime || !Follow())
            {
                _root.gameObject.SetActive(false);
                return;
            }
            _text.maxVisibleCharacters = Mathf.Min(_letters, Mathf.FloorToInt(_since / Mathf.Max(0.001f, RaidLayout.Current.BarkCharDelay)) + 1);
        }

        // The balloon's tail stands on the point bark_layout.offset names over the hero.
        private bool Follow()
        {
            var l = RaidLayout.Current;
            if (!_trays.Place(_hero, l.BarkOffset.y, out var at)) return false;
            var corner = new Vector2(at.x + l.BarkOffset.x - l.BarkTail.x, at.y - l.BarkTail.y);
            corner.x = Mathf.Clamp(corner.x, 0f, 1920f - Art.x);
            corner.y = Mathf.Max(0f, corner.y);
            _root.anchoredPosition = new Vector2(Mathf.Round(corner.x), -Mathf.Round(corner.y));
            return true;
        }

        public object Describe()
        {
            return new { up = Up, hero = _hero, words = Words, at = new[] { _root.anchoredPosition.x, -_root.anchoredPosition.y }, since = _since, lifetime = _lifetime };
        }
    }
}
