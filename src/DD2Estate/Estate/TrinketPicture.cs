using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Item;
using Assets.Code.Library;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Dev;
using DD2Estate.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD2's picture of a trinket on DD1's card, for every place that draws one: the Trinket Inventory, the
    /// hero sheet's Equipment slots, the Nomad Wagon, the quest's rewards on the Estate Map and on the results,
    /// the bag and the loot scroll, the dungeon hero panel's two slots, a card in the player's hand.
    ///
    /// DD1's own trinket art is made for its card (72x144). Measured over the 502 pictures of the install
    /// (panels/icons_equip/trinket/inv_trinket+*.png, the box of what is painted): 65 of the card's 72 pixels
    /// wide in the middle of the range, a tenth of them 51 or less, a tenth all 72; 111 of 144 high. 181 of
    /// them are painted up to the card's very edge: DD1 shows a large trinket cut off by its card.
    /// DD2's pictures are squares (512 or 550 a side) with a wide clear margin round the thing itself. Measured
    /// in the running game over its 211 trinkets (<c>trinkets.art measure=true</c>): the painted part is 0.684
    /// of the square's side wide in the middle of the range (a tenth 0.53 or less, a tenth 0.86 or more) and
    /// 0.818 high, and sits in the square's middle (off by less than 0.02 for nine in ten).
    ///
    /// So all of them are drawn larger by ONE factor (<see cref="Factor"/>, the setting
    /// <c>[Look] TrinketPictureScale</c>), and what reaches past the card's FRAME is cut off there: the picture
    /// lies in a window that ends at the inner edge of the rarity border (<see cref="FrameWidth"/>), so the
    /// border runs clean round every trinket however large it is drawn. Nothing is cut out of DD2's art: the
    /// window is a mask. No trinket has a size of its own.
    /// </summary>
    [EstateModule]
    internal static class TrinketPicture
    {
        /// <summary>The width of DD1's card (panels/icons_equip/trinket/rarity_*.png, 72x144): what the factor counts from.</summary>
        public const float CardWidth = 72f;

        /// <summary>
        /// The factor a new config file starts with: a trinket's square picture is drawn this many card widths
        /// wide. The owner asked for "larger still" than 1.32 (the factor that made the middle DD2 trinket as
        /// wide as the middle DD1 one, 65 of 72 pixels, and left DD2's a third lower than DD1's: 78 against 111),
        /// "it is fine if they reach past the frame a little", cut by the frame as DD1 cuts its own by the card.
        /// CHOSEN BY LOOKING, on a sheet of all 211 on their cards at 1.5, 1.6, 1.7, 1.8 and 1.9 beside DD1's own
        /// (_lab/batch_1007/sheet3_trinket_factors_*.png): at 1.7 the middle trinket is 83 wide and 100 high
        /// (DD1's: 65 and 111), the narrow tenth 65 wide; the window is 62 wide, so 191 of the 211 are cut at
        /// the sides (114 of them by more than a quarter of their painted width, mostly handles, chains and
        /// spikes), none at top or foot. Up to 1.7 a mug is still a mug with a handle and a ring a ring; at 1.8
        /// and 1.9 (DD1's height) the common shapes become close-ups: a ring is its seal, a cleaver a piece of blade.
        /// THE OWNER, having played with 1.7 (2026-10-06): "make the trinkets 1.5". By the shares measured above
        /// the middle trinket is then 74 wide and 88 high in its square of 108, in the window of 62.
        /// (A config file written by an earlier build keeps the value it has: a new default does not reach it.)
        /// </summary>
        public const float Stock = 1.5f;

        /// <summary>
        /// The rarity border of DD1's card. MEASURED on all fourteen rarity_*.png, on every side alike: a black
        /// line, three pixels in the rarity's colour, a black line; the card's own dark ground begins at the
        /// sixth pixel. The picture's window ends there.
        /// </summary>
        public const float FrameWidth = 5f;

        private const float Least = 0.5f, Most = 3f;
        private const string WindowName = "TrinketWindow";
        private const string SparkleArt = "fx/trinket_sparkle/trinket_sparkle.sprite";
        private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        private static ConfigEntry<float> _scale;
        // set by a test for this session (trinkets.art factor= / frame=); nothing of it is written to the config
        private static float? _sessionFactor;
        private static float _frame = FrameWidth;

        /// <summary>The factor in force: the setting, unless a test has set another for this session (<c>trinkets.art factor=</c>).</summary>
        public static float Factor => Mathf.Clamp(_sessionFactor ?? (_scale != null ? _scale.Value : Stock), Least, Most);

        /// <summary>The side of the square a trinket's picture is drawn in, in DD1's pixels.</summary>
        public static float Side => Mathf.Round(CardWidth * Factor);

        /// <summary>How far inside the card's edge the picture's window ends (the card's border), in DD1's pixels.</summary>
        public static float Frame => _frame;

        /// <summary>
        /// The picture of a trinket on a card (or in a cell of a card's size): a square of <see cref="Side"/> in
        /// the card's middle, cut off at the inner edge of the card's border. The picture comes back empty and
        /// clear; whoever asked gives it DD2's sprite once that has loaded (<see cref="Trinkets.Icon"/>) and a colour.
        /// </summary>
        public static Image Add(Transform card, string name = "Icon")
        {
            var window = UiKit.Rect(WindowName, card);
            Fit(window);
            window.gameObject.AddComponent<RectMask2D>();
            var picture = UiKit.Image(name, window, null, Color.clear);
            picture.preserveAspect = true;
            ((RectTransform)picture.transform).Place(Middle, Middle, Vector2.zero, new Vector2(Side, Side));
            picture.gameObject.AddComponent<TrinketPictureMark>();
            return picture;
        }

        // The window on the card: the card less its border on every side. Its middle is the card's middle.
        private static void Fit(RectTransform window)
        {
            window.anchorMin = Vector2.zero;
            window.anchorMax = Vector2.one;
            window.offsetMin = new Vector2(_frame, _frame);
            window.offsetMax = new Vector2(-_frame, -_frame);
        }

        /// <summary>
        /// For a card that shows now one thing and now another (a slot, a card in the player's hand): the
        /// picture of this trinket on it, lit as the card is; none for <c>null</c> and while DD2 still loads it.
        /// </summary>
        public static void Show(Image card, string id)
        {
            if (card == null) return;
            var window = card.transform.Find(WindowName);
            if (window == null && id == null) return;
            var picture = window != null ? window.GetComponentInChildren<Image>(true) : Add(card.transform);
            if (picture == null) return;
            var sprite = id != null ? Trinkets.Icon(id) : null;
            if (picture.sprite != sprite) picture.sprite = sprite;
            picture.color = sprite != null ? Color.white : Color.clear;
            picture.material = card.material;
        }

        /// <summary>
        /// DD1's sparkle running round the card of an ancestral trinket and of a trophy (fx/trinket_sparkle: its
        /// skeleton's origin is the card's middle), as the Trinket Inventory has it; null for any other rarity.
        /// </summary>
        public static SpineView Sparkle(Transform card, string rarity)
        {
            var animation = rarity == "ancestral" ? "ancestral_sparkle" : rarity == Trinkets.Trophy ? "boss_sparkle" : null;
            if (animation == null) return null;
            var size = ((RectTransform)card).rect.size;
            var around = SpineView.Loop("Sparkle", card, SparkleArt, size * 0.5f, animation);
            // drawn as often as DD1 keys its animations
            if (around != null) around.Step = 1f / 30f;
            return around;
        }

        // Every picture that exists takes the size and the window in force.
        private static void Resize()
        {
            foreach (var mark in UnityEngine.Object.FindObjectsOfType<TrinketPictureMark>(true))
            {
                var picture = (RectTransform)mark.transform;
                picture.sizeDelta = new Vector2(Side, Side);
                if (picture.parent is RectTransform window && window.name == WindowName) Fit(window);
            }
        }

        // ---- settings and dev bridge -----------------------------------------------------------------------

        private static void Register()
        {
            _scale = Plugin.Settings.Bind("Look", "TrinketPictureScale", Stock, new ConfigDescription(
                "How large DD2's trinket pictures are drawn on DD1's cards, everywhere: the picture's square is this many card widths wide (1 = as wide as the card; the first builds drew 1.32). What reaches past the card's coloured frame is cut off by it.",
                new AcceptableValueRange<float>(Least, Most)));
            // (changed while the game runs, by a config manager: the pictures on screen follow)
            _scale.SettingChanged += (sender, change) => Resize();

            // The factor and every trinket picture that exists right now (where it lies, its side, its card's size).
            // {"factor":1.5}: another factor until the game is closed, for a look (the pictures on screen are resized;
            // {"factor":0} the setting again); {"frame":0}: the window on the card ends that many pixels inside the
            // card's edge, until the game is closed (5: at the border's inner edge; 0: the whole card, as it was);
            // {"setting":1.6}: the setting itself, as a config manager would change it (it is written to the config file);
            // {"measure":true}: how much of its own square every DD2 trinket picture fills (the game loads the
            // pictures on demand: ask again while "loading" is not 0); with "detail":true every picture's numbers.
            AgentBridge.Register("trinkets.art", o =>
            {
                if (o["setting"] != null) _scale.Value = (float)o["setting"];
                if (o["factor"] != null)
                {
                    var factor = (float)o["factor"];
                    _sessionFactor = factor > 0f ? factor : (float?)null;
                }
                if (o["frame"] != null) _frame = Mathf.Clamp((float)o["frame"], 0f, 30f);
                if (o["factor"] != null || o["frame"] != null) Resize();
                if ((bool?)o["measure"] ?? false) return Measure((bool?)o["detail"] ?? false);
                return Describe();
            });
        }

        private static object Describe()
        {
            var pictures = new List<object>();
            foreach (var mark in UnityEngine.Object.FindObjectsOfType<TrinketPictureMark>(true))
            {
                var rect = (RectTransform)mark.transform;
                var window = rect.parent as RectTransform;
                var card = window != null ? window.parent as RectTransform : null;
                var image = mark.GetComponent<Image>();
                var cell = card != null ? card.rect.size : Vector2.zero;
                var seen = window != null ? window.rect.size : Vector2.zero;
                var cardImage = card != null ? card.GetComponent<Image>() : null;
                pictures.Add(new
                {
                    at = Path(card), shown = mark.gameObject.activeInHierarchy && image != null && image.sprite != null && image.color.a > 0f,
                    sprite = image != null && image.sprite != null ? image.sprite.name : null, side = rect.rect.width,
                    card = new[] { cell.x, cell.y }, window = new[] { seen.x, seen.y }, cardArt = cardImage != null && cardImage.sprite != null ? cardImage.sprite.name : null,
                    masked = window != null && window.GetComponent<RectMask2D>() != null,
                    cut = rect.rect.width > seen.x + 0.01f || rect.rect.height > seen.y + 0.01f
                });
            }
            return new
            {
                factor = Factor, setting = _scale != null ? _scale.Value : Stock, session = _sessionFactor, stock = Stock, side = Side, cardWidth = CardWidth,
                frame = _frame, count = pictures.Count, pictures
            };
        }

        private static string Path(Transform transform)
        {
            var names = new List<string>();
            for (var at = transform; at != null && names.Count < 5; at = at.parent) names.Insert(0, at.name);
            return string.Join("/", names);
        }

        // How much of its square each of DD2's trinket pictures fills. Two measures: the sprite's mesh (what
        // Unity keeps of a picture when it packs it, a little more than the paint) and the painted pixels
        // themselves (SpritePaint). Boxes are left, top, right, bottom in the sprite's own pixels, y down.
        private static object Measure(bool detail)
        {
            var items = SingletonMonoBehaviour<Library<string, ItemDefinition>>.Instance;
            if (items == null) return "no item library yet";
            var sprites = new List<KeyValuePair<string, Sprite>>();
            int loading = 0, none = 0;
            for (var i = 0; i < items.GetNumberOfLibraryElements(); i++)
            {
                var item = items.GetLibraryElementAtIndex(i);
                if (item == null || item.m_type != ItemType.TRINKET) continue;
                var sprite = Trinkets.Icon(item.m_id);
                if (sprite != null) sprites.Add(new KeyValuePair<string, Sprite>(item.m_id, sprite));
                else if (Trinkets.HasNoIcon(item.m_id)) none++;
                else loading++;
            }

            var rows = new List<object>();
            var meshWide = new List<float>();
            var meshTall = new List<float>();
            var paintWide = new List<float>();
            var paintTall = new List<float>();
            var sizes = new Dictionary<string, int>();
            foreach (var group in sprites.GroupBy(pair => pair.Value.texture))
            {
                Color32[] pixels = null;
                try { pixels = group.Key != null ? SpritePaint.Pixels(group.Key) : null; }
                catch (Exception e) { Plugin.Log.LogWarning("Trinket pictures: " + (group.Key != null ? group.Key.name : "?") + " could not be read: " + e.Message); }
                foreach (var pair in group)
                {
                    var sprite = pair.Value;
                    var size = sprite.rect.size;
                    var key = size.x + "x" + size.y;
                    sizes[key] = sizes.TryGetValue(key, out var seen) ? seen + 1 : 1;
                    var mesh = SpritePaint.MeshBox(sprite);
                    meshWide.Add(mesh.width / size.x);
                    meshTall.Add(mesh.height / size.y);
                    float[] paint = null;
                    if (pixels != null && SpritePaint.Box(sprite, pixels, group.Key.width, out var box))
                    {
                        paint = new[] { box.xMin, box.yMin, box.xMax, box.yMax };
                        paintWide.Add(box.width / size.x);
                        paintTall.Add(box.height / size.y);
                    }
                    if (detail)
                        rows.Add(new { id = pair.Key, w = size.x, h = size.y, mesh = new[] { mesh.xMin, mesh.yMin, mesh.xMax, mesh.yMax }, paint });
                }
            }
            return new
            {
                factor = Factor, measured = sprites.Count, loading, withoutPicture = none, sizes,
                mesh = new { wide = Middlemost(meshWide), tall = Middlemost(meshTall) },
                paint = new { read = paintWide.Count, wide = Middlemost(paintWide), tall = Middlemost(paintTall) },
                pictures = detail ? rows : null
            };
        }

        private static float Middlemost(List<float> values)
        {
            if (values.Count == 0) return 0f;
            values.Sort();
            return values[values.Count / 2];
        }
    }

    /// <summary>Marks a trinket's picture made by <see cref="TrinketPicture.Add"/>, so that a test can find them all.</summary>
    internal class TrinketPictureMark : MonoBehaviour
    {
    }
}
