using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// One of DD1's fonts as a TextMeshPro font asset: DD1's own bitmap glyphs, or (the look that is on by
    /// default, <see cref="DD2Estate.UI.Dd2Fonts"/>) a face of DD2's fitted to the DD1 font's measures, so that
    /// either takes the same box at the same size. The measures are DD1's in both looks.
    /// </summary>
    internal class Dd1Font
    {
        /// <summary>The font's id in fonts/fonts.darkest ("dwarven_axe_medium").</summary>
        public string Id;
        /// <summary>The asset of the look that is on.</summary>
        public TMP_FontAsset Asset;
        /// <summary>
        /// The TextMeshPro size at which the font is drawn pixel for pixel on the 1920x1080 canvas, the way DD1
        /// draws it. A size means what it means for DD2's own font: at the same size a DD1 font's capitals are
        /// no taller and its line is no taller, so a label can change fonts and keep its size and its box.
        /// </summary>
        public float NativeSize;
        public int LineHeight;
        public int Base;
        public int CapHeight;
        /// <summary>
        /// For a font drawn with an outline (fonts/popup.fnt): the same glyphs with their outline, as a second
        /// asset. DD1 colours the two apart (pop_text_damage, pop_text_outline_damage...), and a UI label has one
        /// colour: a label of this asset lies under a label of <see cref="Asset"/>, which then holds the glyphs'
        /// inside alone. Null for a font without an outline. Give it to a label with <see cref="Dd1Fonts.Set"/>:
        /// in DD2's look the two assets share an atlas and differ by their material.
        /// </summary>
        public TMP_FontAsset Outline;
        /// <summary>How wide DD1 draws the outline, in pixels; 0 for a font without one. <see cref="CapHeight"/> counts it twice.</summary>
        public int OutlineWidth;

        // The two looks; either is made when it is first wanted.
        internal TMP_FontAsset Dd1Asset, Dd1Outline, Dd2Asset, Dd2Outline;
        /// <summary>What DD1 advances by after each character of the font (pixels), for comparing widths.</summary>
        internal Dictionary<int, int> Advances;

        /// <summary>True while the font is set in DD2's face.</summary>
        public bool InDd2Face => Asset != null && Asset == Dd2Asset;
    }

    /// <summary>
    /// DD1's text styles, read from the player's DD1 install. fonts/fonts.darkest names the fonts
    /// (`font: .id "dwarven_axe_large" .file "fonts/dwarvenaxe-l.fnt"`) and says which of them a text style
    /// uses (`font_ref: .id "town_roster_name" .font "dwarven_axe_medium"`); colours/base.colours.darkest gives
    /// the same style ids their colours. DD1 has no font sizes: a font is drawn as it is stored.
    ///
    /// A font is an AngelCode BMFont text file with TGA pages. Its glyphs are copied into a new atlas (white,
    /// coverage in alpha, a gutter around each) and described to TextMeshPro by hand: a static bitmap font
    /// asset on Unity's own UI shader, which every build carries and which multiplies the atlas by the vertex
    /// colour like TMP's bitmap shader does. A glyph the font lacks is looked up in the fallback list (DD2's
    /// own font), then in the game's TMP settings. tools/dd1_bmfont.py reads the files the same way.
    ///
    /// A bitmap is sharp at its own size only, and the canvas is scaled to the window. So by default
    /// ([Look] Fonts = dd2) a font keeps DD1's measures and is drawn in a face of DD2's, which is a distance
    /// field and sharp at any size (<see cref="DD2Estate.UI.Dd2Fonts"/>). Only the .fnt text is read then; the
    /// pages are decoded when the bitmap look is asked for.
    /// </summary>
    internal static class Dd1Fonts
    {
        public const string Large = "dwarven_axe_large";
        public const string Medium = "dwarven_axe_medium";
        public const string TextMedium = "ubuntu_medium";
        public const string TextSmall = "ubuntu_small";
        /// <summary>The outlined DwarvenAxe of the numbers and words that pop up over a hero (style pop_text).</summary>
        public const string Popup = "popup";

        private const string Table = "fonts/fonts.darkest";
        private const string ColourTable = "colours/base.colours.darkest";
        // A font's native size: DD2's NDDunkel font has capitals 0.705 of the point size tall and a line (ascent
        // to descent) 1.2 of it (face info: cap line 62, line height 105.6 at 88). DwarvenAxe's line is tall for
        // its capitals; were its size set by the capitals alone, a label boxed for DD2's font would lose it to
        // TMP's truncation. The arithmetic is Core/FontFit.cs.
        // TMP widens a bitmap glyph's quad by 1 texel (5 with "extra padding") on every side.
        private const int Gutter = 6;
        private const int MaxAtlas = 4096;

        // FALLBACK, used only when fonts/fonts.darkest cannot be read: DD1's own table.
        private static readonly Dictionary<string, string> StockFiles = new Dictionary<string, string>
        {
            { TextSmall, "fonts/ubuntu.fnt" }, { TextMedium, "fonts/ubuntu_m.fnt" },
            { Medium, "fonts/dwarvenaxe-m.fnt" }, { Large, "fonts/dwarvenaxe-l.fnt" }, { Popup, "fonts/popup.fnt" }
        };

        // FALLBACK, used only when colours/base.colours.darkest cannot be read: DD1's three text colours.
        private static readonly Dictionary<string, Color> StockColours = new Dictionary<string, Color>
        {
            { "neutral", new Color32(174, 172, 162, 255) }, { "notable", new Color32(200, 180, 110, 255) },
            { "harmful", new Color32(177, 25, 0, 255) }
        };

        private static readonly Regex Pair = new Regex("(\\w+)=(\"[^\"]*\"|\\S+)");
        private static readonly Regex ColourLine = new Regex("\\.id\\s+\"?([\\w.]+)\"?\\s+\\.(rgba|shared_id)\\s+(.*)");
        private static readonly Regex FactorLine = new Regex("\\.id\\s+\"?([\\w.]+)\"?\\s+\\.hvec4\\s+([\\d.]+)\\s+([\\d.]+)\\s+([\\d.]+)(?:\\s+([\\d.]+))?");

        private static readonly Dictionary<string, Dd1Font> Fonts = new Dictionary<string, Dd1Font>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static Dictionary<string, string> _files;
        private static Dictionary<string, string> _styles;
        private static bool _tableRead;
        private static Dictionary<string, Color> _colours;
        private static Dictionary<string, Color> _factors;

        // ---- styles ------------------------------------------------------------------------------------

        /// <summary>The DD1 font a text style is set in ("town_roster_name" → "dwarven_axe_medium"); null if DD1 has no such style.</summary>
        public static string FontOf(string styleId)
        {
            ReadTable();
            return styleId != null && _styles.TryGetValue(styleId, out var font) ? font : null;
        }

        /// <summary>The font of a text style, or of a font id when no style has that name. Null when it cannot be had.</summary>
        public static Dd1Font Style(string styleId)
        {
            return Font(FontOf(styleId) ?? styleId);
        }

        /// <summary>DD1's colour of a text style or a named colour ("notable"); <paramref name="fallback"/> if it has none.</summary>
        public static Color Colour(string id, Color fallback)
        {
            if (_colours == null)
            {
                // Asked before there is an install to read, the stock colours answer and the file is read next time.
                bool found;
                try { found = Dd1Install.Found; }
                catch (Exception) { found = false; }
                var colours = ReadColours(found);
                if (!found) return id != null && colours.TryGetValue(id, out var stock) ? stock : fallback;
                _colours = colours;
            }
            return id != null && _colours.TryGetValue(id, out var colour) ? colour : fallback;
        }

        /// <summary>
        /// A colour DD1 multiplies a picture by (`colour: .id "button_highlight" .hvec4 1.5 1.5 1.3 1.0`): its
        /// channels may pass 1, which draws the picture brighter than its art. <paramref name="fallback"/> if
        /// the table has none of that name.
        /// </summary>
        public static Color Factor(string id, Color fallback)
        {
            if (_factors == null)
            {
                // Asked before there is an install to read, the fallback answers and the file is read next time.
                bool found;
                try { found = Dd1Install.Found; }
                catch (Exception) { found = false; }
                if (!found) return fallback;
                _factors = ReadFactors();
            }
            return id != null && _factors.TryGetValue(id, out var factor) ? factor : fallback;
        }

        // ---- fonts -------------------------------------------------------------------------------------

        /// <summary>
        /// A DD1 font by its id in fonts.darkest, in the look that is on. It is made in one go the first time it
        /// is asked for (or by <see cref="Preload"/>) and handed out whole or not at all: null when DD1 is not
        /// installed, the font's files are missing or unreadable, or no asset could be made. The caller then
        /// keeps DD2's font.
        /// </summary>
        public static Dd1Font Font(string fontId)
        {
            if (string.IsNullOrEmpty(fontId)) return null;
            // The asset can be destroyed behind our back (Unity then reports it as null): build it again.
            if (Fonts.TryGetValue(fontId, out var font) && font.Asset != null)
            {
                if (!DD2Estate.UI.Dd2Fonts.Whole(font)) Remake(font);
                else Late(font);
                return font;
            }
            if (Failed.Contains(fontId)) return null;
            string file = null;
            try
            {
                // Not a failure of the font: nothing is remembered about it while there is no install.
                if (!Dd1Install.Found) return null;
                ReadTable();
                if (!_files.TryGetValue(fontId, out file))
                {
                    Failed.Add(fontId);
                    return null;
                }
                if (font == null) font = Measure(fontId, file);
                Dress(font);
                Fonts[fontId] = font;
                if (font.InDd2Face)
                    Plugin.Log.LogInfo("DD1 font " + fontId + " (" + file + ", native size " + font.NativeSize + ") is set in DD2's " + DD2Estate.UI.Dd2Fonts.FaceOf(font));
                return font;
            }
            catch (Exception e)
            {
                // Once is enough: every label would ask again. Nothing of a font that failed is kept.
                Failed.Add(fontId);
                Fonts.Remove(fontId);
                Plugin.Log.LogWarning("DD1 font " + fontId + " (" + (file ?? "?") + ") could not be built, DD2's font is used instead: " + e);
                return null;
            }
        }

        /// <summary>
        /// Makes DD1's four text fonts now instead of each when a label first asks for it (a bitmap font is a file
        /// to parse, a page to decode and an atlas to fill: some tens of milliseconds). UiKit calls this before
        /// the first label of a session is made, so no line of text waits for its font in mid-play.
        /// </summary>
        public static void Preload()
        {
            Font(Large);
            Font(Medium);
            Font(TextMedium);
            Font(TextSmall);
        }

        /// <summary>
        /// Makes <paramref name="dd2"/> the first place a glyph missing from the DD1 bitmap font is looked for.
        /// (A font in DD2's face looks in the face it was made from and in that face's own fallbacks.)
        /// </summary>
        public static void Fallback(Dd1Font font, TMP_FontAsset dd2)
        {
            if (font == null) return;
            Fallback(font.Dd1Asset, dd2);
            Fallback(font.Dd1Outline, dd2);
        }

        private static void Fallback(TMP_FontAsset asset, TMP_FontAsset dd2)
        {
            if (asset == null || dd2 == null || dd2 == asset) return;
            var list = asset.fallbackFontAssetTable;
            if (list == null) asset.fallbackFontAssetTable = list = new List<TMP_FontAsset>();
            list.RemoveAll(entry => entry == null);
            if (!list.Contains(dd2)) list.Insert(0, dd2);
        }

        /// <summary>
        /// Sets a label in a font asset of <see cref="Dd1Font"/> (its <see cref="Dd1Font.Asset"/> or its
        /// <see cref="Dd1Font.Outline"/>). Use this instead of `label.font = asset`: TextMeshPro keeps a label's
        /// material when the new font shares the old one's atlas, and two fonts in DD2's face do; the outline of
        /// the pop-up font differs from its inside by the material alone.
        /// </summary>
        public static void Set(TMP_Text label, TMP_FontAsset asset)
        {
            if (label == null || asset == null) return;
            label.font = asset;
            var material = asset.material;
            if (material != null && label.fontSharedMaterial != material) label.fontSharedMaterial = material;
            // "Extra padding" widens every glyph's quad over its neighbours in a bitmap atlas; a distance field
            // font is given the padding its material needs without it.
            label.extraPadding = false;
        }

        /// <summary>Every font made so far (for the dev bridge).</summary>
        internal static IEnumerable<Dd1Font> Known => Fonts.Values;

        /// <summary>
        /// Puts every font made so far, and every label set in one, into the look that is wanted now
        /// (<see cref="DD2Estate.UI.Dd2Fonts.Wanted"/>). A size means the same in both looks, so nothing but the
        /// asset changes. Returns how many labels changed.
        /// </summary>
        internal static int Apply()
        {
            var swap = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
            foreach (var font in new List<Dd1Font>(Fonts.Values))
            {
                try { Dress(font); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("DD1 font " + font.Id + " keeps its look: " + e.Message);
                    continue;
                }
                Other(swap, font.Dd1Asset, font.Dd2Asset, font.Asset);
                Other(swap, font.Dd1Outline, font.Dd2Outline, font.Outline);
            }
            // assets that were made anew of another face: what held the old one takes what the font has now
            foreach (var gone in Replaced)
            {
                var wanted = gone.Outline ? gone.Font.Outline : gone.Font.Asset;
                if (gone.Asset != null && wanted != null && gone.Asset != wanted) swap[gone.Asset] = wanted;
            }
            Replaced.Clear();
            if (swap.Count == 0) return 0;
            var changed = 0;
            foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (label == null) continue;
                var now = label.font;
                if (now == null || !swap.TryGetValue(now, out var wanted)) continue;
                if (!DD2Estate.UI.Dd2Fonts.Wanted) Fallback(wanted, DD2Estate.UI.UiKit.Font);
                Set(label, wanted);
                changed++;
            }
            return changed;
        }

        private struct Gone
        {
            public TMP_FontAsset Asset;
            public Dd1Font Font;
            public bool Outline;
        }

        private static readonly List<Gone> Replaced = new List<Gone>();

        /// <summary>
        /// Says that an asset of a font (its DD2 look, or the outline of it) is about to be replaced by another
        /// object: the next <see cref="Apply"/> hands the labels that hold it the asset the font has then.
        /// </summary>
        internal static void Replace(Dd1Font font, TMP_FontAsset old, TMP_FontAsset now, bool outline)
        {
            if (font == null || old == null || old == now) return;
            Replaced.Add(new Gone { Asset = old, Font = font, Outline = outline });
        }

        // The game let go of the face a font was made of (and of its atlas): the font is made again of what the
        // game has loaded now, or goes back to its bitmap, and its labels with it.
        private static float _remade = -100f;

        private static void Remake(Dd1Font font)
        {
            if (Time.unscaledTime - _remade < 3f) return;
            _remade = Time.unscaledTime;
            try
            {
                if (!DD2Estate.UI.Dd2Fonts.Fit(font))
                {
                    Replace(font, font.Dd2Asset, null, false);
                    Replace(font, font.Dd2Outline, null, true);
                    font.Dd2Asset = null;
                    font.Dd2Outline = null;
                }
                var changed = Apply();
                Plugin.Log.LogInfo("DD1 font " + font.Id + " was made again (DD2's face had been unloaded), " + changed + " labels changed");
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 font " + font.Id + " could not be made again: " + e.Message); }
        }

        private static void Other(Dictionary<TMP_FontAsset, TMP_FontAsset> swap, TMP_FontAsset dd1, TMP_FontAsset dd2, TMP_FontAsset wanted)
        {
            if (wanted == null) return;
            if (dd1 != null && dd1 != wanted) swap[dd1] = wanted;
            if (dd2 != null && dd2 != wanted) swap[dd2] = wanted;
        }

        // A font's two assets in the look that is wanted, made if they are not there yet. DD2's face stands in
        // for the bitmap only when the game has it loaded; the bitmap is the look that can always be had.
        private static void Dress(Dd1Font font)
        {
            if (DD2Estate.UI.Dd2Fonts.Wanted)
            {
                if (font.Dd2Asset == null) DD2Estate.UI.Dd2Fonts.Fit(font);
                if (font.Dd2Asset != null)
                {
                    font.Asset = font.Dd2Asset;
                    font.Outline = font.Dd2Outline;
                    return;
                }
            }
            if (font.Dd1Asset == null)
            {
                ReadTable();
                BuildBitmap(font, _files[font.Id]);
                Plugin.Log.LogInfo("DD1 font " + font.Id + ": " + font.Dd1Asset.characterTable.Count + " characters from " + _files[font.Id]
                                   + ", atlas " + font.Dd1Asset.atlasWidth + "x" + font.Dd1Asset.atlasHeight + ", native size " + font.NativeSize);
            }
            font.Asset = font.Dd1Asset;
            font.Outline = font.Dd1Outline;
        }

        // A font that had to take its bitmap because DD2's faces were not loaded yet takes its face as soon as
        // they are, and its labels with it. Looked at no more than once in a few seconds.
        private static float _lateAsked = -100f;

        private static void Late(Dd1Font font)
        {
            if (!DD2Estate.UI.Dd2Fonts.Wanted || Time.unscaledTime - _lateAsked < 3f) return;
            // the same for a font in DD2's face whose figures were to come from a face that was not loaded yet
            var figures = font.InDd2Face && DD2Estate.UI.Dd2Fonts.Lacking(font);
            if (font.InDd2Face && !figures) return;
            _lateAsked = Time.unscaledTime;
            try
            {
                if (figures)
                {
                    if (DD2Estate.UI.Dd2Fonts.Fit(font)) Plugin.Log.LogInfo("DD1 font " + font.Id + " took its figures once the game had their face loaded (" + DD2Estate.UI.Dd2Fonts.Refresh(font) + " labels)");
                    return;
                }
                var changed = Apply();
                if (font.InDd2Face) Plugin.Log.LogInfo("DD1 fonts took DD2's faces once the game had them loaded (" + changed + " labels)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 fonts keep their bitmaps: " + e.Message); }
        }

        private static void ReadTable()
        {
            if (_tableRead) return;
            _files = new Dictionary<string, string>(StockFiles);
            _styles = new Dictionary<string, string>();
            DarkestFile table = null;
            try
            {
                // Asked before there is an install to read, the table is read again next time.
                if (!Dd1Install.Found) return;
                table = DarkestFile.Load(Table);
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 font table could not be read: " + e.Message); }
            _tableRead = true;
            if (table == null) return;
            foreach (var block in table.All("font"))
            {
                var id = block.String("id");
                var file = block.String("file");
                if (id != null && file != null) _files[id] = file;
            }
            foreach (var block in table.All("font_ref"))
            {
                var id = block.String("id");
                var font = block.String("font");
                if (id != null && font != null) _styles[id] = font;
            }
        }

        // `colour: .id "notable" .rgba 200 180 110 255`, `.rgba #e0ddce` or `.shared_id "notable"`. Read line by
        // line: the .darkest reader takes `#` for a comment.
        private static Dictionary<string, Color> ReadColours(bool installed)
        {
            var colours = new Dictionary<string, Color>(StockColours);
            var shares = new Dictionary<string, string>();
            string text = null;
            try { text = installed ? Dd1Install.ReadText(ColourTable) : null; }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 colour table could not be read: " + e.Message); }
            if (text == null) return colours;
            foreach (var raw in text.Split('\n'))
            {
                var match = ColourLine.Match(raw);
                if (!match.Success) continue;
                var id = match.Groups[1].Value;
                var value = match.Groups[3].Value.Trim();
                if (match.Groups[2].Value == "shared_id") shares[id] = value.Split(' ', '\t')[0].Trim('"');
                else if (TryColour(value, out var colour)) colours[id] = colour;
            }
            // A shared id may point at another shared id.
            for (var pass = 0; pass < 4; pass++)
                foreach (var share in shares)
                    if (colours.TryGetValue(share.Value, out var colour)) colours[share.Key] = colour;
            return colours;
        }

        private static Dictionary<string, Color> ReadFactors()
        {
            var factors = new Dictionary<string, Color>();
            string text = null;
            try { text = Dd1Install.ReadText(ColourTable); }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 colour table could not be read: " + e.Message); }
            if (text == null) return factors;
            foreach (var raw in text.Split('\n'))
            {
                var match = FactorLine.Match(raw);
                if (!match.Success) continue;
                var channel = new[] { 1f, 1f, 1f, 1f };
                var whole = true;
                for (var i = 0; i < 4 && whole; i++)
                    if (match.Groups[i + 2].Success) whole = float.TryParse(match.Groups[i + 2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out channel[i]);
                if (whole) factors[match.Groups[1].Value] = new Color(channel[0], channel[1], channel[2], channel[3]);
            }
            return factors;
        }

        private static bool TryColour(string value, out Color colour)
        {
            colour = Color.white;
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = value.Substring(1).Split(' ', '\t')[0];
                if (hex.Length == 3) hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
                if (hex.Length < 6 || !int.TryParse(hex.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
                colour = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                return true;
            }
            var parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            var channel = new byte[3];
            for (var i = 0; i < 3; i++)
                if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out channel[i])) return false;
            colour = new Color32(channel[0], channel[1], channel[2], 255);
            return true;
        }

        // ---- BMFont ------------------------------------------------------------------------------------

        private class Glyph
        {
            public int Id, X, Y, Width, Height, XOffset, YOffset, Advance, Page;
            // Where the glyph went in the new atlas (top-left origin).
            public int AtlasX, AtlasY;
        }

        private class Kerning
        {
            public int First, Second, Amount;
        }

        private class Page
        {
            public int Width, Height;
            public Color32[] Pixels;
            public bool HasAlpha;
        }

        private class BmFont
        {
            public string Face;
            public int LineHeight, Base, Outline;
            public readonly Dictionary<int, string> PageFiles = new Dictionary<int, string>();
            public readonly List<Glyph> Glyphs = new List<Glyph>();
            public readonly List<Kerning> Kernings = new List<Kerning>();
        }

        private static BmFont Parse(string text)
        {
            var font = new BmFont();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                var space = line.IndexOf(' ');
                if (space <= 0) continue;
                var tag = line.Substring(0, space);
                if (tag != "info" && tag != "common" && tag != "page" && tag != "char" && tag != "kerning") continue;
                var values = new Dictionary<string, string>();
                foreach (Match pair in Pair.Matches(line)) values[pair.Groups[1].Value] = pair.Groups[2].Value.Trim('"');
                switch (tag)
                {
                    case "info":
                        values.TryGetValue("face", out font.Face);
                        font.Outline = Int(values, "outline");
                        break;
                    case "common":
                        font.LineHeight = Int(values, "lineHeight");
                        font.Base = Int(values, "base");
                        break;
                    case "page":
                        if (values.TryGetValue("file", out var file)) font.PageFiles[Int(values, "id")] = file;
                        break;
                    case "char":
                        font.Glyphs.Add(new Glyph
                        {
                            Id = Int(values, "id"), X = Int(values, "x"), Y = Int(values, "y"), Width = Int(values, "width"),
                            Height = Int(values, "height"), XOffset = Int(values, "xoffset"), YOffset = Int(values, "yoffset"),
                            Advance = Int(values, "xadvance"), Page = Int(values, "page")
                        });
                        break;
                    case "kerning":
                        font.Kernings.Add(new Kerning { First = Int(values, "first"), Second = Int(values, "second"), Amount = Int(values, "amount") });
                        break;
                }
            }
            return font;
        }

        private static int Int(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        // FALLBACK, used only when a font's .fnt cannot be read and DD2's face is to stand in for it: DD1's own
        // measures of its five fonts (line height, base, capital H, outline).
        private static readonly Dictionary<string, int[]> StockMeasures = new Dictionary<string, int[]>
        {
            { TextSmall, new[] { 25, 21, 16, 0 } }, { TextMedium, new[] { 28, 23, 17, 0 } }, { Medium, new[] { 40, 30, 22, 0 } },
            { Large, new[] { 63, 48, 34, 0 } }, { Popup, new[] { 74, 57, 48, 4 } }
        };

        /// <summary>
        /// A font's measures, from its .fnt alone: no page is decoded and no asset made. That is all DD2's face
        /// needs to take the font's place.
        /// </summary>
        private static Dd1Font Measure(string fontId, string file)
        {
            BmFont bm = null;
            try
            {
                var text = Dd1Install.ReadText(file);
                if (text != null) bm = Parse(text);
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 font " + fontId + " (" + file + ") could not be read: " + e.Message); }
            if (bm == null || bm.LineHeight <= 0 || bm.Glyphs.Count == 0)
            {
                if (!DD2Estate.UI.Dd2Fonts.Wanted || !StockMeasures.TryGetValue(fontId, out var stock))
                    throw new InvalidOperationException(bm == null ? "file missing" : "not a BMFont text file");
                return Measured(fontId, stock[0], stock[1], stock[2], stock[3], null);
            }
            var cap = Find(bm.Glyphs, 'H');
            var advances = new Dictionary<int, int>();
            foreach (var glyph in bm.Glyphs)
                if (glyph.Id >= 32 && glyph.Id < 0x2100 && !advances.ContainsKey(glyph.Id)) advances[glyph.Id] = glyph.Advance;
            return Measured(fontId, bm.LineHeight, bm.Base, cap != null ? cap.Height : bm.Base, bm.Outline, advances);
        }

        private static Dd1Font Measured(string fontId, int lineHeight, int baseLine, int capHeight, int outline, Dictionary<int, int> advances)
        {
            return new Dd1Font
            {
                Id = fontId, LineHeight = lineHeight, Base = baseLine, CapHeight = capHeight, OutlineWidth = Mathf.Max(0, outline),
                NativeSize = DD2Estate.Core.FontFit.NativeSize(capHeight, lineHeight), Advances = advances
            };
        }

        /// <summary>The bitmap look of a font: <see cref="Dd1Font.Dd1Asset"/> and, for a font with an outline, <see cref="Dd1Font.Dd1Outline"/>.</summary>
        private static void BuildBitmap(Dd1Font font, string file)
        {
            var fontId = font.Id;
            var text = Dd1Install.ReadText(file);
            if (text == null) throw new InvalidOperationException("file missing");
            var bm = Parse(text);
            if (bm.LineHeight <= 0 || bm.Glyphs.Count == 0) throw new InvalidOperationException("not a BMFont text file");

            // Pages lie beside the .fnt. A page that is not there only costs its glyphs (ubuntu.fnt lists
            // pages of other languages).
            var folder = file.Contains("/") ? file.Substring(0, file.LastIndexOf('/') + 1) : "";
            var pages = new Dictionary<int, Page>();
            foreach (var entry in bm.PageFiles)
            {
                var bytes = Dd1Install.Exists(folder + entry.Value) ? Dd1Install.ReadBytes(folder + entry.Value) : null;
                if (bytes == null) continue;
                var page = new Page();
                if (Tga.TryRead(bytes, out page.Width, out page.Height, out page.Pixels, out page.HasAlpha)) pages[entry.Key] = page;
                else Plugin.Log.LogWarning("DD1 font page not readable: " + folder + entry.Value);
            }
            if (pages.Count == 0) throw new InvalidOperationException("no readable page");

            // x/y/width/height are pixels of the page as stored. (scaleW/scaleH are not to be trusted: the
            // DwarvenAxe fonts declare 512x256 and 1024x512 over pages of 256x256 and 512x512.)
            var drawn = new List<Glyph>();
            var seen = new HashSet<int>();
            foreach (var glyph in bm.Glyphs)
            {
                if (!seen.Add(glyph.Id)) continue;
                var inPage = pages.TryGetValue(glyph.Page, out var page) && glyph.X >= 0 && glyph.Y >= 0
                             && glyph.X + glyph.Width <= page.Width && glyph.Y + glyph.Height <= page.Height;
                if (!inPage)
                {
                    // Keep the advance of a glyph whose page is missing only if it draws nothing anyway.
                    if (glyph.Width > 1 || glyph.Height > 1) continue;
                    glyph.Width = glyph.Height = 0;
                }
                drawn.Add(glyph);
            }

            // A font with an outline keeps two coverages on its page (BMFont: alphaChnl=1, redChnl=0): the glyph in
            // the colour channels, glyph and outline together in alpha. Each becomes an atlas of its own.
            var outlined = bm.Outline > 0;
            var atlas = Pack(drawn, pages, outlined, out var atlasWidth, out var atlasHeight);
            atlas.name = "DD1 " + fontId + " Atlas";

            var cap = Find(drawn, 'H');
            var mean = Find(drawn, 'x');
            var capLine = cap != null ? bm.Base - cap.YOffset : bm.Base * 0.75f;
            var meanLine = mean != null ? bm.Base - mean.YOffset : bm.Base * 0.5f;
            var asset = Asset(font, bm, drawn, atlas, atlasWidth, atlasHeight, capLine, meanLine, "");
            TMP_FontAsset rimAsset = null;
            if (outlined)
            {
                // the same glyphs in the same places: only the coverage differs
                var rim = Pack(drawn, pages, false, out atlasWidth, out atlasHeight);
                rim.name = "DD1 " + fontId + " Outline Atlas";
                rimAsset = Asset(font, bm, drawn, rim, atlasWidth, atlasHeight, capLine, meanLine, " Outline");
            }
            font.Dd1Asset = asset;
            font.Dd1Outline = rimAsset;
        }

        private static Glyph Find(List<Glyph> glyphs, char character)
        {
            foreach (var glyph in glyphs)
                if (glyph.Id == character && glyph.Height > 0) return glyph;
            return null;
        }

        /// <summary>
        /// Copies the glyphs into one texture, tallest first on shelves, each inside a clear gutter. Pixels are
        /// white with the coverage in alpha (also where nothing is drawn: a black neighbour would darken the
        /// filtered edge). <paramref name="inside"/>: of an outlined font's page the glyphs alone, without the
        /// outline around them (the colour channels' share of the alpha).
        /// </summary>
        private static Texture2D Pack(List<Glyph> glyphs, Dictionary<int, Page> pages, bool inside, out int width, out int height)
        {
            var order = new List<Glyph>(glyphs);
            order.Sort((a, b) => b.Height != a.Height ? b.Height.CompareTo(a.Height) : a.Id.CompareTo(b.Id));

            width = 256;
            height = 0;
            for (; width <= MaxAtlas; width *= 2)
            {
                int x = Gutter, y = Gutter, shelf = 0;
                var fits = true;
                foreach (var glyph in order)
                {
                    if (glyph.Width == 0 || glyph.Height == 0) continue;
                    if (glyph.Width + 2 * Gutter > width) { fits = false; break; }
                    if (x + glyph.Width + Gutter > width)
                    {
                        x = Gutter;
                        y += shelf + Gutter;
                        shelf = 0;
                    }
                    glyph.AtlasX = x;
                    glyph.AtlasY = y;
                    x += glyph.Width + Gutter;
                    if (glyph.Height > shelf) shelf = glyph.Height;
                }
                height = Mathf.NextPowerOfTwo(y + shelf + Gutter);
                if (fits && height <= width) break;
            }
            if (width > MaxAtlas) throw new InvalidOperationException("glyphs do not fit a " + MaxAtlas + " px atlas");

            var pixels = new Color32[width * height];
            var clear = new Color32(255, 255, 255, 0);
            for (var i = 0; i < pixels.Length; i++) pixels[i] = clear;
            foreach (var glyph in order)
            {
                if (glyph.Width == 0 || glyph.Height == 0) continue;
                var page = pages[glyph.Page];
                for (var row = 0; row < glyph.Height; row++)
                {
                    var from = (glyph.Y + row) * page.Width + glyph.X;
                    // Unity keeps a texture's first row at the bottom.
                    var to = (height - 1 - (glyph.AtlasY + row)) * width + glyph.AtlasX;
                    for (var column = 0; column < glyph.Width; column++)
                    {
                        var source = page.Pixels[from + column];
                        var coverage = page.HasAlpha ? source.a : source.r;
                        if (inside && page.HasAlpha) coverage = (byte)(source.r * source.a / 255);
                        pixels[to + column] = new Color32(255, 255, 255, coverage);
                    }
                }
            }

            var atlas = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            atlas.SetPixels32(pixels);
            atlas.Apply(false, true);
            return atlas;
        }

        // ---- TextMeshPro -------------------------------------------------------------------------------

        private static TMP_FontAsset Asset(Dd1Font font, BmFont bm, List<Glyph> glyphs, Texture2D atlas, int atlasWidth, int atlasHeight, float capLine, float meanLine, string suffix)
        {
            var shader = BitmapShader();
            if (shader == null) throw new InvalidOperationException("no UI shader to draw bitmap text with");

            var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
            asset.name = "DD1 " + font.Id + suffix;
            asset.hideFlags = HideFlags.HideAndDontSave;
            // An asset without a version is taken for a pre-1.1 one and "upgraded" from tables it does not have.
            Field(typeof(TMP_Asset), "m_Version").SetValue(asset, "1.1.0");

            // BMFont's cursor is the top of the line cell; `base` below it lies the baseline. With the ascent
            // line there, TMP's top alignment puts every glyph where DD1 does.
            var descent = bm.Base - bm.LineHeight;
            var thickness = Mathf.Max(1f, Mathf.Round(bm.LineHeight / 20f));
            object face = new FaceInfo
            {
                familyName = string.IsNullOrEmpty(bm.Face) ? font.Id : bm.Face,
                styleName = "Regular",
                pointSize = Mathf.RoundToInt(font.NativeSize),
                scale = 1f,
                lineHeight = bm.LineHeight,
                ascentLine = bm.Base,
                capLine = capLine,
                meanLine = meanLine,
                baseline = 0f,
                descentLine = descent,
                superscriptOffset = capLine * 0.5f,
                superscriptSize = 0.5f,
                subscriptOffset = descent * 0.5f,
                subscriptSize = 0.5f,
                underlineOffset = descent * 0.5f,
                underlineThickness = thickness,
                strikethroughOffset = meanLine * 0.5f,
                strikethroughThickness = thickness,
                tabWidth = FirstAdvance(glyphs, ' ')
            };
            // Left at 0, TMP asks the font engine for the value of a face that was never loaded.
            AccessTools.Field(typeof(FaceInfo), "m_UnitsPerEM")?.SetValue(face, 1000);
            Field(typeof(TMP_Asset), "m_FaceInfo").SetValue(asset, face);

            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            asset.isMultiAtlasTexturesEnabled = false;
            asset.atlasTextures = new[] { atlas };
            Field(typeof(TMP_FontAsset), "m_AtlasWidth").SetValue(asset, atlasWidth);
            Field(typeof(TMP_FontAsset), "m_AtlasHeight").SetValue(asset, atlasHeight);
            Field(typeof(TMP_FontAsset), "m_AtlasPadding").SetValue(asset, Gutter);
            // A raster mode without the colour bit: TMP then tints the glyphs with the label's colour.
            Field(typeof(TMP_FontAsset), "m_AtlasRenderMode").SetValue(asset, GlyphRenderMode.SMOOTH);

            // Glyph indices are our own (0 means "missing" to TMP; pair lookups pack two of them into 32 bits).
            var index = new Dictionary<int, uint>();
            foreach (var glyph in glyphs)
            {
                var id = (uint)index.Count + 1;
                index[glyph.Id] = id;
                var metrics = new GlyphMetrics(glyph.Width, glyph.Height, glyph.XOffset, bm.Base - glyph.YOffset, glyph.Advance);
                var rect = glyph.Width == 0 || glyph.Height == 0
                    ? GlyphRect.zero
                    : new GlyphRect(glyph.AtlasX, atlasHeight - glyph.AtlasY - glyph.Height, glyph.Width, glyph.Height);
                var entry = new UnityEngine.TextCore.Glyph(id, metrics, rect, 1f, 0);
                asset.glyphTable.Add(entry);
                asset.characterTable.Add(new TMP_Character((uint)glyph.Id, entry));
            }
            var pairs = asset.fontFeatureTable.glyphPairAdjustmentRecords;
            foreach (var kerning in bm.Kernings)
            {
                if (!index.TryGetValue(kerning.First, out var first) || !index.TryGetValue(kerning.Second, out var second)) continue;
                pairs.Add(new GlyphPairAdjustmentRecord(
                    new GlyphAdjustmentRecord(first, new GlyphValueRecord(0f, 0f, kerning.Amount, 0f)),
                    new GlyphAdjustmentRecord(second, new GlyphValueRecord(0f, 0f, 0f, 0f))));
            }

            var material = new Material(shader) { name = asset.name + " Atlas Material", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            asset.material = material;
            asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            asset.ReadFontAssetDefinition();
            return asset;
        }

        private static float FirstAdvance(List<Glyph> glyphs, char character)
        {
            foreach (var glyph in glyphs)
                if (glyph.Id == character) return glyph.Advance;
            return 0f;
        }

        /// <summary>
        /// UI/Default: texture times vertex colour, with the rect clipping and stencil masking of every other
        /// UI element. TMP's own bitmap shaders sit in an addressable bundle and are not loaded unless some
        /// material of the game uses them; they are the second choice.
        /// </summary>
        private static Shader BitmapShader()
        {
            var material = Canvas.GetDefaultCanvasMaterial();
            var shader = material != null ? material.shader : null;
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("TextMeshPro/Mobile/Bitmap");
            if (shader == null) shader = Shader.Find("TextMeshPro/Bitmap");
            return shader;
        }

        private static FieldInfo Field(Type type, string name)
        {
            var field = AccessTools.Field(type, name);
            if (field == null) throw new MissingFieldException(type.Name, name);
            return field;
        }
    }
}
