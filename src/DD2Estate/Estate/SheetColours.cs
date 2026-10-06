using System;
using System.Collections.Generic;
using Assets.Code.Locale;
using Assets.Code.UI;
using Assets.Code.UI.Canvases;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Tooltips;
using Assets.Code.UI.Widgets;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Harm is red on the hero's sheet. DD2's colour of what is bad is a blue (#3B6196: a negative quirk's name,
    /// the lines and numbers of a tooltip that hurt, the names of harmful tokens); DD1's is its red
    /// (colours/base.colours.darkest, "harmful"). While DD2's character sheet is open in an Estate session:
    ///
    ///   - the words. DD2 keeps its text colours as strings of its string table ("negative_quirk", "harmful",
    ///     "debuff", "stat_debuff", and a dark one for light ground, "negative_quirk_dark") and writes them into
    ///     its texts as it builds them. Asked for one of these while the sheet is open, the table answers with
    ///     DD1's red, so whatever the sheet and its tooltips write then is red: quirks, tooltips of quirks,
    ///     skills, stats and trinkets, a lowered stat's number;
    ///   - the tints. What the sheet's own layout draws in that blue is turned by its hue (the brightness
    ///     kept): a negative quirk's label and glow, the "Target" pips of the skills (DD1's are red beside the
    ///     yellow of the ranks), the debuff resistance's letters, a skill's frame of a strained relationship, the
    ///     unfriendly bands of the relationships tab, the target marks in a skill's tooltip;
    ///   - two pictures drawn in blue: the lower jaw of the quirks' device and the debuff resistance's sign.
    ///     They get a red copy for as long as the sheet is open (Image.overrideSprite).
    ///
    /// DD2's own token and status pictures inside a text (TextMeshPro sprites) stay as DD2 drew them.
    ///
    /// Everything is put back as the sheet closes, and outside an Estate session nothing is touched.
    /// </summary>
    [EstateModule]
    internal static class SheetColours
    {
        private static ConfigEntry<bool> _enabled;
        private static bool _held;      // dev: switched off by the bridge, to look at the sheet as DD2 has it
        private static string[] _harmWords, _harmDarkWords;
        private static float _hue = -1f;

        // DD2's blues of harm as its layouts have them (RGB), by where they are used. A graphic is turned only
        // when its colour is one of these, so nothing else that happens to be blue is touched.
        private static readonly Color32[] Blues =
        {
            new Color32(0x3B, 0x61, 0x96, 255),     // text: a negative quirk; the relationships' unfriendly band and tint
            new Color32(0x2A, 0x63, 0x83, 255),     // a rare negative quirk's glow; a skill's negative frame; target marks of a skill's tooltip
            new Color32(0x2E, 0xA4, 0xE4, 255),     // the "Target" pips: fill
            new Color32(0x09, 0x66, 0x98, 255),     // the "Target" pips: glow
            new Color32(0x28, 0x5F, 0x80, 255),     // "DBF", the debuff resistance's letters
            new Color32(0x37, 0x7B, 0xAD, 255),     // a skill's negative frame: glow
            new Color32(0x62, 0xAD, 0xE5, 255),     // a skill's negative frame: aura
            new Color32(0x4F, 0x7E, 0xB6, 255)      // the relationships' very unfriendly band
        };
        private static Color32[] _reds;
        // Pictures of the sheet that are drawn in the blue of harm.
        private static readonly string[] BluePictures = { "ui_quirk_image", "icon_debuff_outline" };
        private static readonly Dictionary<int, Sprite> RedPictures = new Dictionary<int, Sprite>();

        /// <summary>A character sheet is open in an Estate session and harm is to be red on it.</summary>
        public static bool On { get; private set; }

        public static bool Enabled => _enabled != null && _enabled.Value && !_held;

        /// <summary>DD1's colour of what harms (colours/base.colours.darkest, "harmful").</summary>
        public static Color Harm => Dd1Fonts.Colour("harmful", new Color32(177, 25, 0, 255));

        private static void Register()
        {
            _enabled = Plugin.Settings.Bind("Sheet", "RedForHarm", true,
                "On the hero's sheet in the Estate, what DD2 marks as bad in its blue (negative quirks, harmful lines of tooltips, target pips) is DD1's red. Off: DD2's own colours.");
            // The state: {"on":false} shows the open sheet as DD2 has it (until {"on":true}); a dev switch, not saved.
            AgentBridge.Register("sheet.colours", o =>
            {
                if (o["on"] != null)
                {
                    _held = !(bool)o["on"];
                    var sheet = SheetCosmeticsDev.Sheet();
                    if (_held) Closed();
                    else if (sheet != null && EstateSession.Active) Opening(sheet);
                    if (sheet != null)
                    {
                        // the sheet's words are written anew, in the colours that hold now
                        AccessTools.Method(typeof(CharacterSheetUiBhv), "PopulateSheet")?.Invoke(sheet, null);
                        var view = sheet.GetComponent<SheetColoursView>();
                        if (view != null && On) view.Sweep();
                    }
                }
                var open = SheetCosmeticsDev.Sheet();
                var at = open != null ? open.GetComponent<SheetColoursView>() : null;
                return new
                {
                    on = On, enabled = Enabled, harm = "#" + ColorUtility.ToHtmlStringRGB(Harm),
                    words = HarmWords()[0], darkWords = _harmDarkWords != null ? _harmDarkWords[0] : null,
                    turned = at != null ? at.Turned() : null, pictures = at != null ? at.Pictures() : null
                };
            });
        }

        // ---- the sheet opens and closes -----------------------------------------------------------------

        /// <summary>DD2 is asked for a hero's sheet: from here until no sheet is open, harm is red (in the Estate).</summary>
        public static void Opening(CharacterSheetUiBhv sheet)
        {
            if (!EstateSession.Active || !Enabled)
            {
                Closed();
                return;
            }
            On = true;
            SheetColoursWatch.Ensure();
            if (sheet == null) return;
            var view = sheet.GetComponent<SheetColoursView>();
            if (view == null) view = sheet.gameObject.AddComponent<SheetColoursView>();
            view.Begin();
        }

        /// <summary>No sheet is open any more: DD2's colours are DD2's again.</summary>
        public static void Closed()
        {
            if (!On && SheetColoursView.Live.Count == 0) return;
            On = false;
            foreach (var view in SheetColoursView.Live.ToArray())
                if (view != null) view.End();
        }

        // ---- the words ------------------------------------------------------------------------------------

        private static string[] HarmWords()
        {
            if (_harmWords == null)
            {
                var harm = Harm;
                _harmWords = new[] { "#" + ColorUtility.ToHtmlStringRGBA(harm) };
                // DD2's dark blue (#0E1E37) is its blue at a quarter of the light: the same of the red
                _harmDarkWords = new[] { "#" + ColorUtility.ToHtmlStringRGBA(new Color(harm.r * 0.31f, harm.g * 0.31f, harm.b * 0.31f, 1f)) };
            }
            return _harmWords;
        }

        /// <summary>DD1's red for one of DD2's colour entries of harm; null for every other entry of the string table.</summary>
        public static IReadOnlyList<string> Words(string key)
        {
            switch (key)
            {
                case "negative_quirk":
                case "harmful":
                case "debuff":
                case "stat_debuff":
                    return HarmWords();
                case "negative_quirk_dark":
                    HarmWords();
                    return _harmDarkWords;
                default:
                    return null;
            }
        }

        // ---- the tints ------------------------------------------------------------------------------------

        private static void Table()
        {
            if (_reds != null) return;
            Color.RGBToHSV(Harm, out _hue, out var harmS, out var harmV);
            Color.RGBToHSV(Blues[0], out _, out var blueS, out var blueV);
            // DD2's text blue becomes DD1's red exactly; the others keep their distance from it in light and colour
            var moreS = blueS > 0.01f ? harmS / blueS : 1f;
            var moreV = blueV > 0.01f ? harmV / blueV : 1f;
            _reds = new Color32[Blues.Length];
            for (var i = 0; i < Blues.Length; i++)
            {
                Color.RGBToHSV(Blues[i], out _, out var s, out var v);
                _reds[i] = Color.HSVToRGB(_hue, Mathf.Clamp01(s * moreS), Mathf.Clamp01(v * moreV));
            }
            _reds[0] = Harm;
        }

        private static bool Near(Color32 a, Color32 b)
        {
            return Math.Abs(a.r - b.r) <= 2 && Math.Abs(a.g - b.g) <= 2 && Math.Abs(a.b - b.b) <= 2;
        }

        /// <summary>The red of one of DD2's blues of harm, its alpha kept; false when the colour is not one of them.</summary>
        public static bool Red(Color colour, out Color red)
        {
            Table();
            Color32 bytes = colour;
            for (var i = 0; i < Blues.Length; i++)
            {
                if (!Near(bytes, Blues[i])) continue;
                red = _reds[i];
                red.a = colour.a;
                return true;
            }
            red = colour;
            return false;
        }

        // ---- the pictures ---------------------------------------------------------------------------------

        public static bool IsBluePicture(Sprite sprite)
        {
            return sprite != null && Array.IndexOf(BluePictures, sprite.name) >= 0;
        }

        /// <summary>
        /// A copy of one of DD2's pictures with its blues turned to DD1's red (made once, kept for the game's
        /// run). DD2's pictures lie in atlases that cannot be read: the picture's part of its atlas is drawn into
        /// a texture that can. Null when the picture is packed in a way that has no rectangle of its own.
        /// </summary>
        public static Sprite RedPicture(Sprite sprite)
        {
            if (sprite == null) return null;
            if (RedPictures.TryGetValue(sprite.GetInstanceID(), out var made)) return made;
            Sprite red = null;
            RenderTexture target = null;
            var active = RenderTexture.active;
            try
            {
                Table();
                if (sprite.packed && (sprite.packingMode != SpritePackingMode.Rectangle || sprite.packingRotation != SpritePackingRotation.None))
                    throw new InvalidOperationException("packed without a rectangle of its own");
                var part = sprite.textureRect;
                var offset = sprite.textureRectOffset;
                var atlas = sprite.texture;
                int width = Mathf.RoundToInt(part.width), height = Mathf.RoundToInt(part.height);
                int fullWidth = Mathf.RoundToInt(sprite.rect.width), fullHeight = Mathf.RoundToInt(sprite.rect.height);
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(atlas, target, new Vector2(part.width / atlas.width, part.height / atlas.height), new Vector2(part.x / atlas.width, part.y / atlas.height));
                RenderTexture.active = target;
                var read = new Texture2D(width, height, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                var pixels = read.GetPixels32();
                UnityEngine.Object.Destroy(read);

                var copy = new Texture2D(fullWidth, fullHeight, TextureFormat.RGBA32, false);
                var all = new Color32[fullWidth * fullHeight];      // clear: what the atlas trimmed away
                int left = Mathf.RoundToInt(offset.x), bottom = Mathf.RoundToInt(offset.y);
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var p = pixels[y * width + x];
                        if (p.a > 0)
                        {
                            Color.RGBToHSV(p, out var h, out var s, out var v);
                            // the blues, from teal to violet-blue; greys and the picture's golds are left alone
                            if (h > 0.47f && h < 0.75f && s > 0.12f)
                            {
                                Color turned = Color.HSVToRGB(_hue, Mathf.Clamp01(s * 1.45f), Mathf.Clamp01(v * 1.12f));
                                var a = p.a;
                                p = turned;
                                p.a = a;
                            }
                        }
                        int tx = x + left, ty = y + bottom;
                        if (tx >= 0 && tx < fullWidth && ty >= 0 && ty < fullHeight) all[ty * fullWidth + tx] = p;
                    }
                copy.SetPixels32(all);
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.filterMode = atlas.filterMode;
                copy.Apply(false, true);
                copy.hideFlags = HideFlags.HideAndDontSave;
                var pivot = new Vector2(sprite.rect.width > 0f ? sprite.pivot.x / sprite.rect.width : 0.5f, sprite.rect.height > 0f ? sprite.pivot.y / sprite.rect.height : 0.5f);
                red = Sprite.Create(copy, new Rect(0f, 0f, fullWidth, fullHeight), pivot, sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, sprite.border);
                red.name = sprite.name + " (red)";
                red.hideFlags = HideFlags.HideAndDontSave;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Sheet: DD2's picture " + sprite.name + " stays blue (" + e.Message + ")"); }
            finally
            {
                RenderTexture.active = active;
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
            RedPictures[sprite.GetInstanceID()] = red;
            return red;
        }
    }

    // ---- hooks ----------------------------------------------------------------------------------------------

    [HarmonyPatch(typeof(CommonUiBhv), nameof(CommonUiBhv.ShowCharacterSheet))]
    internal static class SheetIsAskedFor
    {
        private static void Prefix()
        {
            // before DD2 writes a word of the sheet (the sheet itself is found when it opens)
            try { SheetColours.Opening(null); }
            catch (Exception e) { Plugin.Log.LogError("Sheet: " + e); }
        }
    }

    [HarmonyPatch(typeof(CharacterSheetUiBhv), nameof(CharacterSheetUiBhv.OnScreenOpenStart))]
    internal static class SheetOpensInDd1sColours
    {
        private static void Prefix(CharacterSheetUiBhv __instance)
        {
            try { SheetColours.Opening(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Sheet: the colours of harm could not be set: " + e); }
        }
    }

    [HarmonyPatch(typeof(CharacterSheetStatsUiBhv), nameof(CharacterSheetStatsUiBhv.Populate))]
    internal static class SheetPopulatedInDd1sColours
    {
        private static void Postfix(CharacterSheetStatsUiBhv __instance)
        {
            if (!SheetColours.On) return;
            var view = __instance.GetComponentInParent<SheetColoursView>();
            if (view != null) view.Sweep();
        }
    }

    /// <summary>
    /// DD2's colours of harm, asked of its string table while the sheet is open, or anywhere in an Estate
    /// session when the fight's red is on (<see cref="DD2Estate.Dungeon.FightColours"/>).
    /// </summary>
    [HarmonyPatch(typeof(Localization), nameof(Localization.GetLocalizedStrings))]
    internal static class SheetWritesHarmInRed
    {
        private static void Postfix(string key, ref IReadOnlyList<string> __result)
        {
            // (every string DD2 shows comes through here: the first tests stay cheap ones)
            if (key == null || key.Length < 6) return;
            if (SheetColours.On || DD2Estate.Dungeon.FightColours.WordsOn)
            {
                var red = SheetColours.Words(key);
                if (red != null)
                {
                    __result = red;
                    return;
                }
            }
            // the words of the Estate's entry on the main menu (a key of the mod's own: MainMenuPatch)
            if (key.Length == DD2Estate.Menu.MainMenuPatch.TooltipKey.Length && key == DD2Estate.Menu.MainMenuPatch.TooltipKey)
            {
                __result = DD2Estate.Menu.MainMenuPatch.TooltipWords;
                return;
            }
            // one more answer of the string table that is the Estate's (see SheetCosmetics)
            if (key.Length == SheetCosmetics.InnOnlyKey.Length && EstateSession.Active && key == SheetCosmetics.InnOnlyKey) __result = SheetCosmetics.HamletOnly;
        }
    }

    /// <summary>A tooltip comes up while the sheet is open: what it draws in the blue of harm is turned too.</summary>
    [HarmonyPatch(typeof(TooltipCanvasUiBhv), nameof(TooltipCanvasUiBhv.SetTooltipActive))]
    internal static class SheetTooltipsInDd1sColours
    {
        private static void Postfix(TooltipUiBhv tooltipBhv, bool active, Dictionary<TooltipUiBhv, GameObject> ___m_tooltipInstanceDict)
        {
            if (!active || tooltipBhv == null || ___m_tooltipInstanceDict == null) return;
            if (!SheetColours.On && !DD2Estate.Dungeon.FightColours.On) return;
            if (!___m_tooltipInstanceDict.TryGetValue(tooltipBhv, out var box) || box == null) return;
            DD2Estate.Dungeon.FightColours.Tooltip(box);
            if (!SheetColours.On) return;
            foreach (var view in SheetColoursView.Live)
                if (view != null)
                {
                    view.Tooltip(box);
                    break;
                }
        }
    }

    /// <summary>Notices that no sheet is open any more, whichever way it went.</summary>
    internal class SheetColoursWatch : MonoBehaviour
    {
        private static SheetColoursWatch _watch;
        private int _gone;

        public static void Ensure()
        {
            if (_watch != null || Plugin.Host == null) return;
            _watch = Plugin.Host.gameObject.AddComponent<SheetColoursWatch>();
        }

        private void Update()
        {
            if (!SheetColours.On)
            {
                _gone = 0;
                return;
            }
            var open = EstateSession.Active && SingletonMonoBehaviour<CommonUiBhv>.HasInstance() && SingletonMonoBehaviour<CommonUiBhv>.Instance.IsCharacterSheetActive;
            _gone = open ? 0 : _gone + 1;
            // a sheet that was asked for is there within a frame or two, or it was refused
            if (_gone > 5) SheetColours.Closed();
        }
    }

    /// <summary>The tints and pictures of one character sheet, and of the tooltips it brings up, while it is open.</summary>
    internal class SheetColoursView : MonoBehaviour
    {
        public static readonly List<SheetColoursView> Live = new List<SheetColoursView>();

        private readonly Dictionary<Graphic, Color> _was = new Dictionary<Graphic, Color>();
        private readonly Dictionary<Image, Sprite> _pictures = new Dictionary<Image, Sprite>();
        private readonly List<KeyValuePair<GameObject, int>> _fresh = new List<KeyValuePair<GameObject, int>>();
        private readonly List<Graphic> _scratch = new List<Graphic>();
        private readonly HashSet<Transform> _patterns = new HashSet<Transform>();
        private readonly List<Assets.Code.CommonLogic.Pooling.GameObjectPoolBhv> _pools = new List<Assets.Code.CommonLogic.Pooling.GameObjectPoolBhv>();
        private Component _relationships;
        private Color _relationshipsTint;
        private bool _relationshipsTurned, _active;
        private float _nextSweep;

        public void Begin()
        {
            if (!Live.Contains(this)) Live.Add(this);
            _active = true;
            Sweep();
        }

        private void OnDisable()
        {
            if (_active) End();
        }

        private void OnDestroy()
        {
            Live.Remove(this);
        }

        /// <summary>Every graphic of the sheet that is drawn in one of DD2's blues of harm is turned red.</summary>
        public void Sweep()
        {
            if (!_active || !SheetColours.On) return;
            _nextSweep = Time.unscaledTime + 0.5f;
            Turn(gameObject, true);
            if (!_relationshipsTurned)
            {
                var field = AccessTools.Field(typeof(CharacterSheetRelationshipDisplay), "m_negativeTint");
                _relationships = GetComponentInChildren<CharacterSheetRelationshipDisplay>(true);
                if (field != null && _relationships != null)
                {
                    _relationshipsTint = (Color)field.GetValue(_relationships);
                    if (SheetColours.Red(_relationshipsTint, out var red))
                    {
                        field.SetValue(_relationships, red);
                        _relationshipsTurned = true;
                    }
                }
            }
        }

        // DD2 makes its quirk labels, pips, skill buttons and tooltip parts as copies of a pattern that lies switched
        // off beside them. A pattern is never turned: a copy made of a red one would be red without anyone knowing
        // to turn it back. Its copies are turned as they are found (at once after the sheet is filled).
        private void Patterns(GameObject root)
        {
            root.GetComponentsInChildren(true, _pools);
            foreach (var pool in _pools)
                if (pool != null && pool.prefab != null) _patterns.Add(pool.prefab.transform);
            _pools.Clear();
        }

        private bool OfPattern(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (_patterns.Contains(t)) return true;
                if (t == root) break;
            }
            return false;
        }

        private void Turn(GameObject root, bool pictures)
        {
            Patterns(root);
            root.GetComponentsInChildren(true, _scratch);
            foreach (var graphic in _scratch)
            {
                if (graphic == null) continue;
                // (a palette's swatch is the palette's colour, whatever it is)
                if (SheetColours.Red(graphic.color, out var red) && !OfPattern(graphic.transform, root.transform)
                    && graphic.GetComponentInParent<CharacterSheetCosmeticButtonBhv>(true) == null)
                {
                    if (!_was.ContainsKey(graphic)) _was[graphic] = graphic.color;
                    graphic.color = red;
                }
                if (pictures && graphic is Image image && image.overrideSprite == image.sprite && SheetColours.IsBluePicture(image.sprite) && !_pictures.ContainsKey(image))
                {
                    var copy = SheetColours.RedPicture(image.sprite);
                    if (copy != null)
                    {
                        _pictures[image] = image.sprite;
                        image.overrideSprite = copy;
                    }
                }
            }
            _scratch.Clear();
        }

        /// <summary>A tooltip's box came up: it is looked at now and for the next few frames (DD2 fills some of it late).</summary>
        public void Tooltip(GameObject box)
        {
            if (!_active || box == null) return;
            Turn(box, false);
            _fresh.Add(new KeyValuePair<GameObject, int>(box, 4));
        }

        private void LateUpdate()
        {
            if (!_active) return;
            if (!SheetColours.On)
            {
                End();
                return;
            }
            // an animation of DD2's may write its blue back into a graphic it owns
            foreach (var pair in _was)
                if (pair.Key != null && SheetColours.Red(pair.Key.color, out var red)) _scratch.Add(pair.Key);
            if (_scratch.Count > 0)
            {
                foreach (var graphic in _scratch)
                    if (SheetColours.Red(graphic.color, out var red)) graphic.color = red;
                _scratch.Clear();
            }
            for (var i = _fresh.Count - 1; i >= 0; i--)
            {
                var box = _fresh[i].Key;
                var left = _fresh[i].Value - 1;
                if (box != null) Turn(box, false);
                if (left <= 0 || box == null) _fresh.RemoveAt(i);
                else _fresh[i] = new KeyValuePair<GameObject, int>(box, left);
            }
            if (Time.unscaledTime >= _nextSweep) Sweep();
        }

        /// <summary>The sheet has closed: every colour and picture is DD2's again.</summary>
        public void End()
        {
            _active = false;
            Live.Remove(this);
            foreach (var pair in _was)
                if (pair.Key != null)
                {
                    // the alpha may have been animated meanwhile: only the colour goes back
                    var colour = pair.Value;
                    colour.a = pair.Key.color.a;
                    pair.Key.color = colour;
                }
            _was.Clear();
            _patterns.Clear();
            _fresh.Clear();
            foreach (var pair in _pictures)
                if (pair.Key != null) pair.Key.overrideSprite = null;
            _pictures.Clear();
            if (_relationshipsTurned && _relationships != null)
                AccessTools.Field(typeof(CharacterSheetRelationshipDisplay), "m_negativeTint")?.SetValue(_relationships, _relationshipsTint);
            _relationshipsTurned = false;
            if (Live.Count == 0) SheetColours.Closed();
        }

        public List<string> Turned()
        {
            var list = new List<string>();
            foreach (var pair in _was)
                if (pair.Key != null)
                    list.Add(AgentBridge.PathOf(pair.Key.transform) + " #" + ColorUtility.ToHtmlStringRGB(pair.Value) + " -> #" + ColorUtility.ToHtmlStringRGB(pair.Key.color));
            return list;
        }

        public List<string> Pictures()
        {
            var list = new List<string>();
            foreach (var pair in _pictures)
                if (pair.Key != null) list.Add(AgentBridge.PathOf(pair.Key.transform) + " " + (pair.Value != null ? pair.Value.name : "?"));
            return list;
        }
    }
}
