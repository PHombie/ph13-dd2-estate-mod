using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace DD2Estate.UI
{
    /// <summary>
    /// DD2's own fonts in place of DD1's bitmap fonts ([Look] Fonts = dd2, the default; dd1 brings the bitmaps
    /// back). DD1's fonts are pictures of glyphs at one size; the mod's canvas is 1920x1080 scaled to the window,
    /// and at any window but a 1080 one the pictures are resampled and soft. DD2's fonts are distance fields:
    /// sharp at any scale.
    ///
    /// Every DD1 font (fonts/fonts.darkest: dwarven_axe_large, dwarven_axe_medium, ubuntu_medium, ubuntu_small,
    /// popup) is given a face of DD2's (<see cref="Looks"/>) as a font asset of its own: a copy of DD2's asset
    /// that shares its atlas and has its measures rewritten (Core/FontFit.cs), so that at the DD1 font's native
    /// size the capitals are as tall as DD1's, the baseline lies where DD1's lies and a line is as tall. A size
    /// therefore means the same in both looks and every label keeps its box; only the widths are the face's own
    /// (a look may squeeze them: DD1 itself draws DwarvenAxe at 75 and 62 percent of its width).
    ///
    /// The copy is static: it never adds a glyph to the atlas it shares. A character it lacks is looked for in
    /// the face it was made from, and through it in that face's fallbacks (the game swaps those by language).
    /// A copy is never destroyed: TextMeshPro destroys a font asset's atlas with it, and the atlas is DD2's.
    ///
    /// Dev bridge: fonts.state, fonts.use which=dd1|dd2 (every label changes on the spot), fonts.list (the font
    /// assets and text materials the game has loaded), fonts.usage (which of them DD2's own labels use, and
    /// where), fonts.map (a look tried without a rebuild), fonts.audit (the mod's labels whose text does not
    /// fit its box).
    /// </summary>
    [EstateModule]
    internal static class Dd2Fonts
    {
        /// <summary>How one DD1 font is set in DD2's faces.</summary>
        internal sealed class Look
        {
            /// <summary>Names of DD2's font assets; the first the game has loaded is taken.</summary>
            public string[] Faces;
            /// <summary>Which of the common UI's own fonts stands in when none of the names is loaded: its title font (true) or its text font.</summary>
            public bool Heading;
            /// <summary>The capitals' height as a share of the DD1 font's.</summary>
            public float Cap = 1f;
            /// <summary>Glyph widths and advances times this (DD1 draws DwarvenAxe squeezed, too).</summary>
            public float Stretch = 1f;
            /// <summary>Pixels added to every advance at the native size.</summary>
            public float Track;
            /// <summary>How far the face is let out for weight (below 0: taken in), in pixels at the native size. DD1's ubuntu_medium is a bold; DD2 has its text face in one weight.</summary>
            public float Weight;
            /// <summary>The outline's width in pixels at the native size, for a font that has one; below 0: as wide as DD1 draws it.</summary>
            public float Rim = -1f;
            /// <summary>
            /// The name of the game's material preset the face is drawn with (it must be loaded and be of the
            /// face's atlas); null: the font asset's own material.
            /// </summary>
            public string Preset;
            /// <summary>FALLBACK, used only when the preset is not loaded: its face dilate, over the asset's own material without its outline.</summary>
            public float PresetDilate;
            /// <summary>
            /// Names of DD2's font assets whose figures stand in for the face's own; the first that is loaded is
            /// taken, null or none loaded: the face's own figures.
            /// </summary>
            public string[] DigitFaces;

            public Look Copy() => (Look)MemberwiseClone();
        }

        // DD2's faces as the game names them (Font/*.asset in its catalog). NDDunkel Bold is its heading face:
        // screens' and buttons' names, heroes' names, costs, the words and numbers that pop up in a fight.
        // AlegreyaSans Regular is its text face: descriptions, tooltips, item counts, health numbers. (Its barks
        // are Cuprum Italic.) The game has one weight of each.
        private static readonly string[] HeadingFaces = { "NDDunkelD-Bold SDF" };
        private static readonly string[] TextFaces = { "AlegreyaSans-Regular SDF" };
        // The text face has old-style figures: 0, 1 and 2 as low as an x (a 0 reads as an o), 3 to 9 hanging under
        // the line. In running text that is the game's look; in a price, a counter or a row of stats (DD1 sets
        // all of those in its text font) it reads badly and stands lower than the coin beside it. The game itself
        // sets its shop's prices and its drag counter in LiberationSans for that: lining figures, all one width
        // like DD1's. So a DD1 text font takes its letters from the text face and its figures from that one.
        private static readonly string[] FigureFaces = { "LiberationSans SDF" };
        // The heading face's own material carries a black outline (the game's titles over pictures); its plain
        // labels (buttons, names: the most of them) use this preset, the face alone and a little lighter.
        private const string HeadingPreset = "NDDunkelD-Bold SDF Default";
        private const float HeadingPresetDilate = -0.284f;

        // The mod's own numbers (what the two games do not say), chosen by eye from pictures of both looks:
        // - DwarvenAxe is a narrow face and DD2's heading face a wide one: at equal capitals a name runs 1.3 (large)
        //   to 1.45 (medium) times as far, past the plates DD1 draws names on ("Nomad Wagon"). Set at 0.82 of its
        //   width it runs 1.07 and 1.19 times as far and still reads as DD2's face.
        // - The pop-up font is DwarvenAxe at its full width: the heading face as it is runs 1.07 times as far in
        //   mixed text and 1.25 in capitals ("DEATHBLOW!" over one hero reaches the next one's word). At 0.88 a
        //   word in capitals runs 1.12 times as far. DD1 draws the font fat under its outline, and DD2 sets its
        //   own pop-up words in the heading face at its heaviest: a pixel of weight, and a pixel of air for the
        //   outlines of two letters.
        // - DD1's small text is denser than DD2's text face at that size: 0.4 px of weight on every side. Its
        //   medium text is Ubuntu Bold: 0.9 px, and a little air between the letters for it.
        private const float HeadingStretch = 0.82f;
        private const float TextWeight = 0.4f;
        private const float BoldTextWeight = 0.9f;
        private const float BoldTextTrack = 0.4f;
        private const float PopupStretch = 0.88f;
        private const float PopupWeight = 1f;
        private const float PopupTrack = 1f;

        /// <summary>DD1 font id to its look. A font the table does not know is set like DD1's small text.</summary>
        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            { Dd1Fonts.Large, new Look { Faces = HeadingFaces, Heading = true, Preset = HeadingPreset, PresetDilate = HeadingPresetDilate, Stretch = HeadingStretch } },
            { Dd1Fonts.Medium, new Look { Faces = HeadingFaces, Heading = true, Preset = HeadingPreset, PresetDilate = HeadingPresetDilate, Stretch = HeadingStretch } },
            { Dd1Fonts.Popup, new Look { Faces = HeadingFaces, Heading = true, Preset = HeadingPreset, PresetDilate = HeadingPresetDilate, Stretch = PopupStretch, Weight = PopupWeight, Track = PopupTrack } },
            { Dd1Fonts.TextMedium, new Look { Faces = TextFaces, DigitFaces = FigureFaces, Weight = BoldTextWeight, Track = BoldTextTrack } },
            { Dd1Fonts.TextSmall, new Look { Faces = TextFaces, DigitFaces = FigureFaces, Weight = TextWeight } }
        };

        private static readonly Look OtherLook = new Look { Faces = TextFaces, DigitFaces = FigureFaces, Weight = TextWeight };

        /// <summary>True: DD1's fonts are drawn in DD2's faces. False: in DD1's own bitmaps.</summary>
        public static bool Wanted = true;

        private static ConfigEntry<string> _config;

        // What was made of each DD1 font, for the bridge.
        private sealed class Made
        {
            public TMP_FontAsset Source;
            public Look Look;
            public FontFit.Face Fit;
            public float CapUnits, RimAsked, RimReached, WeightReached;
            public string Plain;
            /// <summary>The face the figures are taken from and the asset made of them; null: the face's own figures.</summary>
            public TMP_FontAsset DigitSource, Digits;
            public float FigureStretch = 1f;
        }

        private enum Part { Face, Outline, Digits }

        private static readonly Dictionary<string, Made> MadeOf = new Dictionary<string, Made>();
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        private static void Register()
        {
            _config = Plugin.Settings.Bind("Look", "Fonts", "dd2",
                "The fonts of everything the mod draws. dd2: DD2's own fonts, set to the measures of DD1's (sharp at any window size). dd1: DD1's bitmap fonts, read from the DD1 install (sharp at 1920x1080 only).");
            Wanted = !string.Equals((_config.Value ?? "").Trim(), "dd1", StringComparison.OrdinalIgnoreCase);
            Plugin.Log.LogInfo("Fonts: " + (Wanted ? "DD2's faces at DD1's measures" : "DD1's bitmaps"));
            // The setting changed while the game runs (the Estate tab of the options): every label follows at once.
            _config.SettingChanged += (s, e) =>
            {
                var dd2 = !string.Equals((_config.Value ?? "").Trim(), "dd1", StringComparison.OrdinalIgnoreCase);
                if (dd2 == Wanted) return;
                try { Use(dd2); }
                catch (Exception error) { Plugin.Log.LogError("Fonts: the look could not be changed on the spot: " + error); }
            };

            AgentBridge.Register("fonts.state", o => State());
            AgentBridge.Register("fonts.use", o =>
            {
                var which = ((string)o["which"] ?? "").Trim().ToLowerInvariant();
                if (which != "dd1" && which != "dd2") throw new ArgumentException("which=dd1|dd2");
                return Use(which == "dd2");
            });
            AgentBridge.Register("fonts.list", o => List((string)o["like"]));
            AgentBridge.Register("fonts.usage", o => Usage((string)o["font"], (string)o["path"], (int?)o["max"] ?? 40));
            AgentBridge.Register("fonts.map", Map);
            AgentBridge.Register("fonts.audit", o => Audit((bool?)o["all"] ?? false, (string)o["path"], (int?)o["max"] ?? 80));
            AgentBridge.Register("fonts.measure", o => Measure((string)o["text"] ?? Sample, (string)o["id"]));
        }

        /// <summary>Changes the look at run time: every font made so far and every label set in one.</summary>
        public static object Use(bool dd2)
        {
            Wanted = dd2;
            var labels = Dd1Fonts.Apply();
            Plugin.Log.LogInfo("Fonts: " + (dd2 ? "DD2's faces" : "DD1's bitmaps") + " from now on, " + labels + " labels changed");
            return new { look = dd2 ? "dd2" : "dd1", labelsChanged = labels, state = State() };
        }

        private static Look LookOf(string fontId) => fontId != null && Looks.TryGetValue(fontId, out var look) ? look : OtherLook;

        /// <summary>The name of the DD2 face a font is set in, for the log.</summary>
        public static string FaceOf(Dd1Font font)
        {
            if (font == null || !MadeOf.TryGetValue(font.Id, out var made) || made.Source == null) return "?";
            return "'" + made.Source.name + "' (scale " + made.Fit.Scale.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        // ---- DD2's faces -----------------------------------------------------------------------------------

        private static readonly Dictionary<string, TMP_FontAsset> Loaded = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);
        private static float _scanned = -100f;

        private static bool Ours(TMP_FontAsset asset)
        {
            var name = asset != null ? asset.name : null;
            return name != null && (name.StartsWith("DD1 ", StringComparison.Ordinal) || name.StartsWith("DD2Estate ", StringComparison.Ordinal));
        }

        // The game's font assets by name, looked for again (no more than once in a few seconds) while one is missing.
        private static void Scan(bool force)
        {
            if (!force && Time.unscaledTime - _scanned < 3f) return;
            _scanned = Time.unscaledTime;
            Loaded.Clear();
            foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (asset == null || Ours(asset) || asset.material == null) continue;
                if (asset.atlasTextures == null || asset.atlasTextures.Length == 0 || asset.atlasTextures[0] == null) continue;
                if (!Loaded.ContainsKey(asset.name)) Loaded[asset.name] = asset;
            }
        }

        // The first of the named font assets the game has loaded; null if none is.
        private static TMP_FontAsset Named(string[] names)
        {
            if (names == null || names.Length == 0) return null;
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var name in names)
                    if (Loaded.TryGetValue(name, out var asset) && asset != null) return asset;
                if (pass == 0) Scan(Loaded.Count == 0);
            }
            return null;
        }

        private static TMP_FontAsset Source(Look look)
        {
            var named = Named(look.Faces);
            if (named != null) return named;
            // FALLBACK: the fonts DD2's common UI holds as its title and text fonts; for a heading, the font of the
            // main menu's entries
            TMP_FontAsset common = null;
            try
            {
                if (SingletonMonoBehaviour<CommonUiBhv>.HasInstance())
                    common = Traverse.Create(SingletonMonoBehaviour<CommonUiBhv>.Instance).Field(look.Heading ? "m_titleFont" : "m_descFont").GetValue<TMP_FontAsset>();
            }
            catch (Exception) { common = null; }
            if (common != null && !Ours(common) && common.material != null) return common;
            return look.Heading && UiKit.Font != null && !Ours(UiKit.Font) && UiKit.Font.material != null ? UiKit.Font : null;
        }

        /// <summary>
        /// False when the face a font was made of is gone (the game unloaded it and its atlas with it): the font's
        /// assets then draw nothing and must be made again of what the game has loaded now.
        /// </summary>
        public static bool Whole(Dd1Font font)
        {
            if (font == null || !font.InDd2Face) return true;
            if (!MadeOf.TryGetValue(font.Id, out var made) || made.Source == null) return false;
            var textures = font.Dd2Asset.atlasTextures;
            return textures != null && textures.Length > 0 && textures[0] != null && font.Dd2Asset.material != null;
        }

        /// <summary>
        /// True when a font in DD2's face still draws that face's own figures although its look names another face
        /// for them and the game has that one loaded by now: the font is to be fitted again.
        /// </summary>
        public static bool Lacking(Dd1Font font)
        {
            if (font == null || !font.InDd2Face || !MadeOf.TryGetValue(font.Id, out var made)) return false;
            if (made.Digits != null || made.Look.DigitFaces == null || Warned.Contains(font.Id + "|digits")) return false;
            var face = Named(made.Look.DigitFaces);
            return face != null && face != made.Source;
        }

        // ---- a DD1 font in a DD2 face ----------------------------------------------------------------------

        /// <summary>
        /// Gives a DD1 font its look in DD2's face: <see cref="Dd1Font.Dd2Asset"/> and, for a font with an
        /// outline, <see cref="Dd1Font.Dd2Outline"/>. False (and nothing changed) while the game has no face
        /// loaded that could stand in, or when the copy cannot be made; the font then keeps its bitmap.
        /// </summary>
        public static bool Fit(Dd1Font font)
        {
            if (font == null) return false;
            var look = LookOf(font.Id);
            try
            {
                var source = Source(look);
                if (source == null) return false;
                var made = new Made { Source = source, Look = look };
                Complete(source, font);
                // an asset made of this face before is made over in place (labels hold it); one made of another
                // face is left as it is (never destroyed: its atlas is DD2's) and a new one takes its place
                MadeOf.TryGetValue(font.Id, out var before);
                var same = before != null && before.Source == source;
                // the figures first: the face's own asset looks for them there
                made.DigitSource = Named(look.DigitFaces);
                if (made.DigitSource == source) made.DigitSource = null;
                if (made.DigitSource != null)
                {
                    try { made.Digits = Make(font, made, made.DigitSource, Part.Digits, before != null && before.DigitSource == made.DigitSource ? before.Digits : null); }
                    catch (Exception e)
                    {
                        if (Warned.Add(font.Id + "|digits")) Plugin.Log.LogWarning("DD1 font " + font.Id + " keeps the figures of DD2's face: " + e.Message);
                        made.DigitSource = null;
                        made.Digits = null;
                    }
                }
                var asset = Make(font, made, source, Part.Face, same ? font.Dd2Asset : null);
                var outline = font.OutlineWidth > 0 ? Make(font, made, source, Part.Outline, same ? font.Dd2Outline : null) : null;
                // labels that hold an asset this one replaces are handed the new one by Dd1Fonts.Apply
                Dd1Fonts.Replace(font, font.Dd2Asset, asset, false);
                Dd1Fonts.Replace(font, font.Dd2Outline, outline, true);
                font.Dd2Asset = asset;
                font.Dd2Outline = outline;
                MadeOf[font.Id] = made;
                return true;
            }
            catch (Exception e)
            {
                if (Warned.Add(font.Id)) Plugin.Log.LogWarning("DD1 font " + font.Id + " could not be set in DD2's face, its bitmap is used: " + e);
                return false;
            }
        }

        // DD2's faces fill their atlases as they go: a character no text of the game has shown yet is not in it,
        // and the copy, which is static, would have to borrow it from the face at the face's own measures. So the
        // face is asked for the characters of English and Western European text that the DD1 font has, the way
        // the game's own labels would ask for them one by one.
        private static readonly HashSet<int> Completed = new HashSet<int>();

        private static void Complete(TMP_FontAsset source, Dd1Font font)
        {
            if (source.atlasPopulationMode == AtlasPopulationMode.Static || font.Advances == null) return;
            if (!Completed.Add(source.GetInstanceID() ^ font.Id.GetHashCode())) return;
            try
            {
                var table = source.characterLookupTable;
                var missing = new System.Text.StringBuilder();
                foreach (var id in font.Advances.Keys)
                {
                    var wanted = (id >= 0x20 && id <= 0x7E) || (id >= 0xA0 && id <= 0xFF) || (id >= 0x2010 && id <= 0x2026);
                    if (wanted && !table.ContainsKey((uint)id)) missing.Append((char)id);
                }
                if (missing.Length == 0) return;
                source.TryAddCharacters(missing.ToString(), out var refused);
                Plugin.Log.LogInfo("Fonts: '" + source.name + "' was asked for " + missing.Length + " characters DD1's " + font.Id + " has"
                                   + (string.IsNullOrEmpty(refused) ? "" : "; its font file lacks " + refused.Length + " of them"));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Fonts: '" + source.name + "' could not be asked for more characters: " + e.Message); }
        }

        // One asset of a font: a face's glyphs at DD1's measures (the face itself, the same under its outline, or
        // the figures of another face). `existing` is made over in place (labels hold it).
        private static TMP_FontAsset Make(Dd1Font font, Made made, TMP_FontAsset source, Part part, TMP_FontAsset existing)
        {
            var look = made.Look;
            var rim = part == Part.Outline;
            var figures = part == Part.Digits;
            ShaderUtilities.GetShaderPropertyIDs();
            // reading the tables also makes sure the face's own lookup is there
            var characters = source.characterLookupTable;
            if (characters == null || source.glyphTable == null || source.glyphTable.Count == 0) throw new InvalidOperationException("'" + source.name + "' has no glyphs");

            var face = source.faceInfo;
            float capUnits = 0f;
            if (characters.TryGetValue('H', out var capital) && capital.glyph != null)
                capUnits = capital.glyph.metrics.horizontalBearingY * capital.scale * capital.glyph.scale;
            if (capUnits <= 0f) capUnits = face.capLine;
            if (capUnits <= 0f) capUnits = face.pointSize * FontFit.CapPerEm;
            // DD1's capital H as stored carries the outline above and below it
            var capPixels = Mathf.Max(1f, font.CapHeight - 2f * font.OutlineWidth);
            var fit = FontFit.Fit(font.NativeSize, font.LineHeight, font.Base, capPixels, face.pointSize, capUnits, look.Cap);
            if (!figures)
            {
                made.Fit = fit;
                made.CapUnits = capUnits;
            }

            var clone = existing != null ? existing : UnityEngine.Object.Instantiate(source);
            clone.name = "DD2Estate " + font.Id + (rim ? " Outline" : figures ? " Figures" : "") + " (" + source.name + ")";
            clone.hideFlags = HideFlags.HideAndDontSave;
            // never a glyph of its own into the atlas it shares
            clone.atlasPopulationMode = AtlasPopulationMode.Static;
            clone.atlasTextures = source.atlasTextures;

            face.scale = fit.Scale;
            face.ascentLine = fit.AscentLine;
            face.descentLine = fit.DescentLine;
            face.lineHeight = fit.LineHeight;
            // (the property's setter is not public in the game's TextMeshPro)
            var faceField = AccessTools.Field(typeof(TMP_Asset), "m_FaceInfo");
            if (faceField == null) throw new MissingFieldException("TMP_Asset", "m_FaceInfo");
            faceField.SetValue(clone, face);

            // glyphs: the face's own, widths squeezed and advances tracked as the look says
            var stretch = look.Stretch > 0f ? look.Stretch : 1f;
            var track = look.Track / fit.PixelsPerUnit;
            if (figures)
            {
                // Figures of one width, like DD1's, and no wider than DD1's: a counter or a price tag that held
                // DD1's figures holds these. (They are set a few hundredths narrower for it, never wider.)
                track = 0f;
                if (font.Advances != null && font.Advances.TryGetValue('0', out var dd1Zero) && characters.TryGetValue('0', out var zero) && zero.glyph != null)
                {
                    var own = zero.glyph.metrics.horizontalAdvance * fit.PixelsPerUnit;
                    if (own > 0f && dd1Zero > 0) stretch = Mathf.Clamp(dd1Zero / own, 0.85f, 1f);
                }
                made.FigureStretch = stretch;
            }
            var glyphs = clone.glyphTable;
            glyphs.Clear();
            var byIndex = new Dictionary<uint, Glyph>();
            foreach (var glyph in source.glyphTable)
            {
                if (glyph == null || byIndex.ContainsKey(glyph.index)) continue;
                var m = glyph.metrics;
                var advance = m.horizontalAdvance * stretch + (m.horizontalAdvance > 0f ? track : 0f);
                var copy = new Glyph(glyph.index, new GlyphMetrics(m.width * stretch, m.height, m.horizontalBearingX * stretch, m.horizontalBearingY, advance), glyph.glyphRect, glyph.scale, glyph.atlasIndex);
                glyphs.Add(copy);
                byIndex[glyph.index] = copy;
            }
            // characters: all of the face's, but for the figures where another face gives them
            var table = clone.characterTable;
            table.Clear();
            foreach (var character in source.characterTable)
            {
                if (character == null || !byIndex.TryGetValue(character.glyphIndex, out var glyph)) continue;
                var figure = character.unicode >= '0' && character.unicode <= '9';
                if (figures ? !figure : figure && made.Digits != null) continue;
                table.Add(new TMP_Character(character.unicode, glyph) { scale = character.scale });
            }
            if (figures && table.Count < 10) throw new InvalidOperationException("'" + source.name + "' has not all ten figures");
            var pairs = clone.fontFeatureTable.glyphPairAdjustmentRecords;
            pairs.Clear();
            var sourcePairs = source.fontFeatureTable != null ? source.fontFeatureTable.glyphPairAdjustmentRecords : null;
            if (sourcePairs != null)
            {
                foreach (var pair in sourcePairs)
                {
                    var first = pair.firstAdjustmentRecord;
                    var second = pair.secondAdjustmentRecord;
                    pairs.Add(new GlyphPairAdjustmentRecord(
                        new GlyphAdjustmentRecord(first.glyphIndex, Squeeze(first.glyphValueRecord, stretch)),
                        new GlyphAdjustmentRecord(second.glyphIndex, Squeeze(second.glyphValueRecord, stretch))));
                }
            }

            // what the copy lacks: the figures of the other face; then what the face may have by now (a face that
            // fills its atlas as it goes) or its fallbacks have
            var fallbacks = new List<TMP_FontAsset>();
            if (!figures)
            {
                if (made.Digits != null) fallbacks.Add(made.Digits);
                fallbacks.Add(source);
            }
            clone.fallbackFontAssetTable = fallbacks;

            // the material: the look's plain one, its face let out for weight and, under the glyphs, for the outline
            var plain = Plain(source, figures ? OtherLook : look, out var plainName);
            if (!figures) made.Plain = plainName;
            var gradient = plain.HasProperty(ShaderUtilities.ID_GradientScale) ? plain.GetFloat(ShaderUtilities.ID_GradientScale) : source.atlasPadding + 1f;
            var has = plain.HasProperty(ShaderUtilities.ID_FaceDilate) ? plain.GetFloat(ShaderUtilities.ID_FaceDilate) : 0f;
            var heavy = plain.HasProperty(ShaderUtilities.ID_WeightNormal) ? plain.GetFloat(ShaderUtilities.ID_WeightNormal) / 4f : 0f;
            var room = FontFit.FieldUse - heavy - has;
            var dilate = FontFit.Dilate(look.Weight, fit.PixelsPerUnit, gradient, room, out var weightReached);
            if (!figures) made.WeightReached = weightReached;
            if (rim)
            {
                var asked = look.Rim >= 0f ? look.Rim : font.OutlineWidth;
                dilate += FontFit.Dilate(asked, fit.PixelsPerUnit, gradient, room - dilate, out var reached);
                made.RimAsked = asked;
                made.RimReached = reached;
            }
            clone.material = MaterialOf(plain, has, dilate);
            clone.ReadFontAssetDefinition();
            return clone;
        }

        private static GlyphValueRecord Squeeze(GlyphValueRecord record, float stretch)
        {
            return new GlyphValueRecord(record.xPlacement * stretch, record.yPlacement, record.xAdvance * stretch, record.yAdvance);
        }

        // A face's plain material for a look: a copy of the game's preset when that is loaded, else of the font
        // asset's own material set to the preset's values. Copies, so that nothing the game does to its material
        // (and nothing done here) reaches the other side.
        private static Material Plain(TMP_FontAsset source, Look look, out string from)
        {
            var key = source.GetInstanceID() + "|plain|" + look.Preset;
            if (Materials.TryGetValue(key, out var material) && material != null)
            {
                from = material.name;
                return material;
            }
            Material preset = null;
            if (!string.IsNullOrEmpty(look.Preset))
            {
                var atlas = source.material.HasProperty(ShaderUtilities.ID_MainTex) ? source.material.GetTexture(ShaderUtilities.ID_MainTex) : null;
                foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (candidate == null || candidate.name != look.Preset || !candidate.HasProperty(ShaderUtilities.ID_MainTex)) continue;
                    if (atlas != null && candidate.GetTexture(ShaderUtilities.ID_MainTex) != atlas) continue;
                    preset = candidate;
                    break;
                }
            }
            material = new Material(preset != null ? preset : source.material) { hideFlags = HideFlags.HideAndDontSave };
            if (preset != null) material.name = "DD2Estate " + preset.name;
            else if (!string.IsNullOrEmpty(look.Preset))
            {
                // FALLBACK: the preset is not loaded; its values over the asset's own material
                if (material.HasProperty(ShaderUtilities.ID_OutlineWidth)) material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
                if (material.HasProperty(ShaderUtilities.ID_FaceDilate)) material.SetFloat(ShaderUtilities.ID_FaceDilate, look.PresetDilate);
                material.name = "DD2Estate " + source.name + " (as " + look.Preset + ")";
                Plugin.Log.LogInfo("Fonts: the game's material '" + look.Preset + "' is not loaded; its values are set over '" + source.material.name + "'");
            }
            else material.name = "DD2Estate " + source.material.name;
            Materials[key] = material;
            from = material.name;
            return material;
        }

        // A plain material with its face let out (or taken in) by `dilate`; one for all fonts of that face and
        // weight, so their labels draw in one batch.
        private static Material MaterialOf(Material plain, float has, float dilate)
        {
            if (Mathf.Abs(dilate) < 0.0005f || !plain.HasProperty(ShaderUtilities.ID_FaceDilate)) return plain;
            var key = plain.GetInstanceID() + "|" + dilate.ToString("0.000", CultureInfo.InvariantCulture);
            if (Materials.TryGetValue(key, out var material) && material != null) return material;
            material = new Material(plain)
            {
                name = plain.name + " " + (dilate > 0f ? "+" : "") + dilate.ToString("0.00", CultureInfo.InvariantCulture),
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetFloat(ShaderUtilities.ID_FaceDilate, Mathf.Clamp(has + dilate, -1f, 1f));
            Materials[key] = material;
            return material;
        }

        /// <summary>Labels that hold an asset made over in place draw from its new measures.</summary>
        internal static int Refresh(Dd1Font font)
        {
            var count = 0;
            foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (label == null) continue;
                var now = label.font;
                if (now == null || (now != font.Dd2Asset && now != font.Dd2Outline)) continue;
                if (now.material != null && label.fontSharedMaterial != now.material) label.fontSharedMaterial = now.material;
                label.havePropertiesChanged = true;
                label.SetAllDirty();
                count++;
            }
            return count;
        }

        // ---- dev bridge ------------------------------------------------------------------------------------

        private const string Sample = "The Ruins Reynauld Crusader 1234567890";

        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static object State()
        {
            var fonts = new List<object>();
            foreach (var font in Dd1Fonts.Known.OrderBy(f => f.Id))
            {
                MadeOf.TryGetValue(font.Id, out var made);
                fonts.Add(new
                {
                    id = font.Id,
                    look = font.InDd2Face ? "dd2" : "dd1",
                    asset = font.Asset != null ? font.Asset.name : null,
                    outline = font.Outline != null ? font.Outline.name : null,
                    nativeSize = font.NativeSize,
                    line = font.LineHeight,
                    @base = font.Base,
                    cap = font.CapHeight,
                    outlineWidth = font.OutlineWidth,
                    dd2 = made == null ? null : new
                    {
                        face = made.Source != null ? made.Source.name : null,
                        scale = F(made.Fit.Scale),
                        pixelsPerUnit = F(made.Fit.PixelsPerUnit),
                        capShare = F(made.Look.Cap),
                        stretch = F(made.Look.Stretch),
                        track = F(made.Look.Track),
                        weight = F(made.Look.Weight) + " asked, " + F(made.WeightReached) + " px",
                        rim = font.OutlineWidth > 0 ? F(made.RimAsked) + " asked, " + F(made.RimReached) + " px" : null,
                        plain = made.Plain,
                        figures = made.DigitSource != null ? made.DigitSource.name + " at " + F(made.FigureStretch) + " of its width" : null,
                        material = font.Dd2Asset != null ? MaterialFacts(font.Dd2Asset.material) : null,
                        outlineMaterial = font.Dd2Outline != null ? MaterialFacts(font.Dd2Outline.material) : null
                    },
                    sampleWidth = Widths(font, Sample)
                });
            }
            return new
            {
                config = _config != null ? _config.Value : null,
                wanted = Wanted ? "dd2" : "dd1",
                useDd1Styles = UiKit.UseDd1Fonts,
                fonts,
                labels = CountLabels()
            };
        }

        private static object CountLabels()
        {
            var counts = new Dictionary<string, int>();
            foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (label == null || label.font == null || !Ours(label.font)) continue;
                counts.TryGetValue(label.font.name, out var n);
                counts[label.font.name] = n + 1;
            }
            return counts;
        }

        // The width DD1 gives a text and the width the face gives it, both at the font's native size (no kerning).
        private static object Widths(Dd1Font font, string text)
        {
            float dd1 = 0f, dd2 = 0f;
            var known = font.Advances != null;
            foreach (var c in text)
                if (known && font.Advances.TryGetValue(c, out var advance)) dd1 += advance;
            var asset = font.Dd2Asset;
            if (asset != null)
            {
                var face = asset.faceInfo;
                var pixels = font.NativeSize / face.pointSize * face.scale;
                var table = asset.characterLookupTable;
                // figures may come from another asset, at its own scale
                TMP_FontAsset digits = MadeOf.TryGetValue(font.Id, out var made) ? made.Digits : null;
                var digitPixels = digits != null ? font.NativeSize / digits.faceInfo.pointSize * digits.faceInfo.scale : 0f;
                foreach (var c in text)
                {
                    if (digits != null && c >= '0' && c <= '9' && digits.characterLookupTable.TryGetValue(c, out var figure) && figure.glyph != null)
                        dd2 += figure.glyph.metrics.horizontalAdvance * digitPixels;
                    else if (table.TryGetValue(c, out var character) && character.glyph != null && character.textAsset == asset)
                        dd2 += character.glyph.metrics.horizontalAdvance * pixels;
                }
            }
            return new { dd1 = known ? F(dd1) : null, dd2 = asset != null ? F(dd2) : null, ratio = known && asset != null && dd1 > 0f ? F(dd2 / dd1) : null };
        }

        private static object Measure(string text, string id)
        {
            var result = new Dictionary<string, object>();
            foreach (var font in Dd1Fonts.Known.OrderBy(f => f.Id))
                if (id == null || font.Id == id) result[font.Id] = Widths(font, text);
            return new { text, widths = result };
        }

        /// <summary>fonts.map id=dwarven_axe_medium [font="NDDunkelD-Bold SDF"] [cap=] [stretch=] [track=] [weight=] [rim=]: a look tried on the spot.</summary>
        private static object Map(JObject o)
        {
            var id = (string)o["id"];
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id=<DD1 font id>");
            var font = Dd1Fonts.Font(id);
            if (font == null) throw new ArgumentException("no DD1 font '" + id + "'");
            var look = LookOf(id).Copy();
            if (o["font"] != null) look.Faces = new[] { (string)o["font"] };
            if (o["cap"] != null) look.Cap = (float)o["cap"];
            if (o["stretch"] != null) look.Stretch = (float)o["stretch"];
            if (o["track"] != null) look.Track = (float)o["track"];
            if (o["weight"] != null) look.Weight = (float)o["weight"];
            if (o["rim"] != null) look.Rim = (float)o["rim"];
            if (o["preset"] != null) look.Preset = (string)o["preset"] == "" ? null : (string)o["preset"];
            if (o["figures"] != null) look.DigitFaces = (string)o["figures"] == "" ? null : new[] { (string)o["figures"] };
            Looks[id] = look;
            Scan(true);
            Warned.Remove(id);
            if (!Fit(font)) throw new InvalidOperationException("the look could not be made (is the face loaded? fonts.list)");
            // made of another face, the font has new assets: its labels are handed them; made over in place, they
            // are drawn anew
            Dd1Fonts.Apply();
            return new { id, labels = font.InDd2Face ? Refresh(font) : 0, state = State() };
        }

        private static string[] Keywords(Material material) => material != null ? material.shaderKeywords : new string[0];

        private static object MaterialFacts(Material material)
        {
            if (material == null) return null;
            ShaderUtilities.GetShaderPropertyIDs();
            var floats = new Dictionary<string, string>();
            foreach (var name in new[] { "_FaceDilate", "_OutlineWidth", "_OutlineSoftness", "_WeightNormal", "_WeightBold", "_GradientScale", "_Sharpness", "_UnderlayOffsetX", "_UnderlayOffsetY", "_UnderlayDilate", "_UnderlaySoftness", "_GlowOuter", "_ScaleRatioA" })
                if (material.HasProperty(name)) floats[name] = F(material.GetFloat(name));
            var colours = new Dictionary<string, string>();
            foreach (var name in new[] { "_FaceColor", "_OutlineColor", "_UnderlayColor", "_GlowColor" })
                if (material.HasProperty(name)) colours[name] = "#" + ColorUtility.ToHtmlStringRGBA(material.GetColor(name));
            var texture = material.HasProperty(ShaderUtilities.ID_MainTex) ? material.GetTexture(ShaderUtilities.ID_MainTex) : null;
            return new
            {
                name = material.name,
                shader = material.shader != null ? material.shader.name : null,
                keywords = Keywords(material),
                atlas = texture != null ? texture.name + " " + texture.width + "x" + texture.height : null,
                floats,
                colours
            };
        }

        /// <summary>fonts.list [like=part]: the font assets the game has loaded and the text materials (presets) beside them.</summary>
        private static object List(string name)
        {
            var used = new Dictionary<int, int>();
            foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (label == null || label.font == null) continue;
                var key = label.font.GetInstanceID();
                used.TryGetValue(key, out var n);
                used[key] = n + 1;
            }
            var fonts = new List<object>();
            foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>().OrderBy(a => a.name))
            {
                if (asset == null) continue;
                if (name != null && asset.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var face = asset.faceInfo;
                var table = asset.characterLookupTable;
                float cap = 0f, digit = 0f, lower = 0f;
                if (table != null)
                {
                    if (table.TryGetValue('H', out var h) && h.glyph != null) cap = h.glyph.metrics.horizontalBearingY;
                    if (table.TryGetValue('0', out var zero) && zero.glyph != null) digit = zero.glyph.metrics.horizontalBearingY;
                    if (table.TryGetValue('x', out var x) && x.glyph != null) lower = x.glyph.metrics.horizontalBearingY;
                }
                used.TryGetValue(asset.GetInstanceID(), out var labels);
                fonts.Add(new
                {
                    name = asset.name,
                    ours = Ours(asset),
                    family = face.familyName + " " + face.styleName,
                    pointSize = face.pointSize,
                    scale = F(face.scale),
                    lineHeight = F(face.lineHeight),
                    ascent = F(face.ascentLine),
                    capLine = F(face.capLine),
                    descent = F(face.descentLine),
                    capH = F(cap),
                    digit0 = F(digit),
                    xHeight = F(lower),
                    atlas = asset.atlasWidth + "x" + asset.atlasHeight + " x" + (asset.atlasTextures != null ? asset.atlasTextures.Length : 0),
                    padding = asset.atlasPadding,
                    mode = asset.atlasPopulationMode.ToString(),
                    render = asset.atlasRenderMode.ToString(),
                    glyphs = asset.glyphTable != null ? asset.glyphTable.Count : 0,
                    characters = asset.characterTable != null ? asset.characterTable.Count : 0,
                    latin = table != null && table.ContainsKey('a') && table.ContainsKey('Z'),
                    latin1 = table != null && table.ContainsKey(0xE9),
                    cyrillic = table != null && table.ContainsKey(0x416),
                    kerning = asset.fontFeatureTable != null && asset.fontFeatureTable.glyphPairAdjustmentRecords != null ? asset.fontFeatureTable.glyphPairAdjustmentRecords.Count : 0,
                    boldStyle = F(asset.boldStyle),
                    boldSpacing = F(asset.boldSpacing),
                    fallbacks = asset.fallbackFontAssetTable != null ? asset.fallbackFontAssetTable.Where(f => f != null).Select(f => f.name).ToArray() : new string[0],
                    weights = asset.fontWeightTable != null ? asset.fontWeightTable.Select((w, i) => (w.regularTypeface != null ? i * 100 + ":" + w.regularTypeface.name : null)).Where(w => w != null).ToArray() : new string[0],
                    labels,
                    material = MaterialFacts(asset.material)
                });
            }
            var materials = new List<object>();
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>().OrderBy(m => m.name))
            {
                if (material == null || material.shader == null) continue;
                var shader = material.shader.name;
                if (shader.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) < 0 && shader.IndexOf("Distance Field", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (name != null && material.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                materials.Add(MaterialFacts(material));
            }
            object settings = null;
            try
            {
                settings = new
                {
                    defaultFont = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset.name : null,
                    fallbacks = TMP_Settings.fallbackFontAssets != null ? TMP_Settings.fallbackFontAssets.Where(f => f != null).Select(f => f.name).ToArray() : new string[0],
                    extraPadding = TMP_Settings.enableExtraPadding
                };
            }
            catch (Exception e) { settings = e.Message; }
            object common = null;
            try
            {
                if (SingletonMonoBehaviour<CommonUiBhv>.HasInstance())
                {
                    var ui = Traverse.Create(SingletonMonoBehaviour<CommonUiBhv>.Instance);
                    common = new
                    {
                        title = ui.Field("m_titleFont").GetValue<TMP_FontAsset>()?.name,
                        desc = ui.Field("m_descFont").GetValue<TMP_FontAsset>()?.name,
                        bark = ui.Field("m_barkFont").GetValue<TMP_FontAsset>()?.name
                    };
                }
            }
            catch (Exception e) { common = e.Message; }
            return new { fonts, materials, settings, commonUi = common, uiKitFont = UiKit.Font != null ? UiKit.Font.name : null };
        }

        private static string PathOf(Transform transform, int most)
        {
            var parts = new List<string>();
            for (var at = transform; at != null && parts.Count < most; at = at.parent) parts.Add(at.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// fonts.usage [font=part] [path=part] [max=40]: which fonts and materials the labels of DD2's own screens
        /// use (every label the game has loaded, shown or not), grouped; with a filter, the labels themselves.
        /// </summary>
        private static object Usage(string fontPart, string pathPart, int max)
        {
            var groups = new Dictionary<string, List<TMP_Text>>();
            foreach (var label in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (label == null || label.font == null || Ours(label.font)) continue;
                var material = label.fontSharedMaterial;
                var key = label.font.name + " | " + (material != null ? material.name : "?");
                if (fontPart != null && key.IndexOf(fontPart, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (pathPart != null && PathOf(label.transform, 12).IndexOf(pathPart, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<TMP_Text>();
                list.Add(label);
            }
            var listed = fontPart != null || pathPart != null;
            var result = new List<object>();
            foreach (var group in groups.OrderByDescending(g => g.Value.Count))
            {
                var labels = group.Value;
                var sizes = labels.GroupBy(l => Mathf.Round(l.enableAutoSizing ? l.fontSizeMax : l.fontSize)).OrderByDescending(g => g.Count()).Take(10).Select(g => g.Key + "x" + g.Count()).ToArray();
                var styles = labels.GroupBy(l => l.fontStyle.ToString()).OrderByDescending(g => g.Count()).Take(5).Select(g => g.Key + "x" + g.Count()).ToArray();
                var samples = labels.OrderByDescending(l => l.gameObject.activeInHierarchy).Take(listed ? max : 6).Select(l => (object)new
                {
                    path = PathOf(l.transform, listed ? 7 : 5),
                    size = F(l.enableAutoSizing ? l.fontSizeMax : l.fontSize) + (l.enableAutoSizing ? " auto from " + F(l.fontSizeMin) : ""),
                    style = l.fontStyle.ToString(),
                    colour = "#" + ColorUtility.ToHtmlStringRGBA(l.color),
                    spacing = F(l.characterSpacing),
                    shown = l.gameObject.activeInHierarchy,
                    text = l.text != null && l.text.Length > 40 ? l.text.Substring(0, 40) : l.text
                }).ToArray();
                result.Add(new { use = group.Key, labels = labels.Count, shown = labels.Count(l => l.gameObject.activeInHierarchy), sizes, styles, samples });
            }
            return result;
        }

        /// <summary>
        /// fonts.audit [all=true] [path=part] [max=80]: the mod's labels on screen whose text does not fit its box
        /// (drawn wider or taller than the box, cut, or shrunk by auto-sizing), so the two looks can be compared
        /// without pictures.
        /// </summary>
        private static object Audit(bool all, string pathPart, int max)
        {
            int seen = 0, wide = 0, tall = 0, cut = 0, shrunk = 0;
            var rows = new List<object>();
            foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (label == null || label.font == null || !Ours(label.font)) continue;
                if (!label.gameObject.activeInHierarchy || !label.enabled || string.IsNullOrEmpty(label.text)) continue;
                var path = PathOf(label.transform, 6);
                if (pathPart != null && path.IndexOf(pathPart, StringComparison.OrdinalIgnoreCase) < 0) continue;
                // a label shown this frame may not have been laid out yet
                label.ForceMeshUpdate();
                var info = label.textInfo;
                if (info == null || info.characterCount == 0) continue;
                seen++;
                var box = label.rectTransform.rect.size;
                var bounds = label.textBounds.size;
                var isWide = bounds.x > box.x + 1.5f;
                var isTall = bounds.y > box.y + 1.5f && info.lineCount > 1;
                var isCut = label.isTextTruncated || (label.overflowMode != TextOverflowModes.Overflow && label.isTextOverflowing);
                var share = label.enableAutoSizing && label.fontSizeMax > 0f ? label.fontSize / label.fontSizeMax : 1f;
                var isShrunk = share < 0.995f;
                if (isWide) wide++;
                if (isTall) tall++;
                if (isCut) cut++;
                if (isShrunk) shrunk++;
                if (!all && !isWide && !isTall && !isCut && !isShrunk) continue;
                if (rows.Count >= max) continue;
                rows.Add(new
                {
                    path,
                    text = label.text.Length > 36 ? label.text.Substring(0, 36) : label.text,
                    font = label.font.name,
                    size = F(label.fontSize) + (label.enableAutoSizing ? "/" + F(label.fontSizeMax) : ""),
                    box = F(box.x) + "x" + F(box.y),
                    drawn = F(bounds.x) + "x" + F(bounds.y),
                    lines = info.lineCount,
                    wide = isWide,
                    tall = isTall,
                    cut = isCut,
                    shrunk = isShrunk ? F(share) : null
                });
            }
            return new { look = Wanted ? "dd2" : "dd1", screen = new[] { Screen.width, Screen.height }, labels = seen, wide, tall, cut, shrunk, rows };
        }
    }
}
