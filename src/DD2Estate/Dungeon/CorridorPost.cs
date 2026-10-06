using System;
using DD2Estate.Dd1;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's film pass (shaders/film.glsl; spec section 6) with what the game's render pipeline has: a global
    /// <see cref="Volume"/> of the mod's own, on a layer only the mod's last overlay camera listens to
    /// (<see cref="CorridorScene.ForegroundCamera"/>), so it is applied once, over the finished dungeon view,
    /// and to nothing else of the game.
    /// <list type="bullet">
    /// <item>Colour grade: DD1 keeps five 16x16x16 tables per dungeon (dungeons/&lt;d&gt;/colour_grade_0..4.png, 0 the
    /// dark, 4 full light) and blends the two around g / 25, where g is not the torch itself but a number that
    /// goes after the torch's band (<see cref="ShownLight"/>, Core/LightEase.cs: at rest one table a band, a
    /// second or so of both on the way into another). The blend is built into one table of the size the
    /// pipeline wants and handed to its ColorLookup override, which looks the picture up in display (sRGB)
    /// values as DD1's shader does.</item>
    /// <item>Grain: DD1 brightens each pixel by a random 1..10 % every frame. The pipeline's FilmGrain override
    /// supplies the noise; the mean (5.5 %) is put into the table.</item>
    /// </list>
    /// </summary>
    internal static class CorridorPost
    {
        // ---- switches and tunables -----------------------------------------------------------------------
        public static bool Grade = true;
        /// <summary>1 is DD1; less mixes the ungraded picture in.</summary>
        public static float GradeContribution = 1f;
        public static bool GrainOn = true;
        /// <summary>The pipeline's FilmGrain intensity. Its shader adds colour * noise(-1..1) * 4 * intensity; DD1's noise
        /// swings 4.5 % about its mean: 0.045 / 4.</summary>
        public static float GrainIntensity = 0.011f;
        public static float GrainResponse = 0f;
        public static FilmGrainLookup GrainType = FilmGrainLookup.Thin1;

        public static string Status { get; private set; } = "not made";
        /// <summary>The two tables in use and the blend between them, as DD1 picks them.</summary>
        public static int Level { get; private set; }
        public static float Blend { get; private set; }

        /// <summary>The mod's last camera has to run the pipeline's post effects.</summary>
        public static bool Wanted => Grade || GrainOn;

        private const int Dd1Size = 16;
        private static GameObject _object;
        private static Volume _volume;
        private static VolumeProfile _profile;
        private static ColorLookup _lookup;
        private static FilmGrain _grain;
        private static Texture2D _lut;
        private static float[][] _tables;          // five tables, [((b * 16 + g) * 16 + r) * 3 + channel], 0..1
        private static string _tablesFor;
        private static string _lutFor;

        /// <summary>Every frame while the corridor is shown.</summary>
        public static void Update(Transform parent, string dungeon, float torch)
        {
            try
            {
                if (_object == null)
                {
                    _object = new GameObject("DD2Estate.CorridorVolume");
                    _object.transform.SetParent(parent, false);
                    _volume = _object.AddComponent<Volume>();
                    _volume.isGlobal = true;
                    _volume.priority = 100f;
                    if (_profile == null)
                    {
                        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                        _profile.hideFlags = HideFlags.HideAndDontSave;
                        _lookup = _profile.Add<ColorLookup>(false);
                        _grain = _profile.Add<FilmGrain>(false);
                    }
                    _volume.sharedProfile = _profile;
                }
                // only the mod's own camera has this layer in its volume mask
                _object.layer = CorridorScene.ForegroundLayer;

                var graded = Grade && Table(dungeon, torch);
                _lookup.active = graded;
                if (graded)
                {
                    _lookup.texture.Override(_lut);
                    _lookup.contribution.Override(Mathf.Clamp01(GradeContribution));
                }
                _grain.active = GrainOn && GrainIntensity > 0f;
                if (_grain.active)
                {
                    _grain.type.Override(GrainType);
                    _grain.intensity.Override(Mathf.Clamp01(GrainIntensity));
                    _grain.response.Override(Mathf.Clamp01(GrainResponse));
                }
                if (graded || !Grade) Status = (graded ? "grade " + Level + "-" + Mathf.Min(4, Level + 1) + " at " + Blend.ToString("0.00") : "no grade") + (_grain.active ? ", grain" : "");
            }
            catch (Exception e)
            {
                Status = "failed: " + e.Message;
                Plugin.Log.LogWarning("Corridor: the colour grade could not be set up: " + e.Message);
                Grade = false;
                GrainOn = false;
            }
        }

        // ---- the scene behind a scripted moment (DD1's layer A: saturation, intensity, blur) ------------------

        private static GameObject _sceneObject;
        private static VolumeProfile _sceneProfile;
        private static ColorAdjustments _adjust;
        private static DepthOfField _blur;
        public static string SceneStatus { get; private set; } = "at rest";

        /// <summary>
        /// DD1's timescripts take the colour out of the scene behind the acting hero, darken it and blur it. A second
        /// volume of the mod's, on a layer only the MAIN camera listens to: the main camera draws walls, floor and
        /// props, the heroes come after it, so they stay as they are. The blur is the pipeline's gaussian depth of
        /// field: nothing of the scene writes depth, so all of it lies at the far end and blurs alike.
        /// </summary>
        public static void Scene(Transform parent, float saturation, float intensity, float blur)
        {
            var wanted = Mathf.Abs(saturation - 1f) > 0.005f || Mathf.Abs(intensity - 1f) > 0.005f || blur > 0.01f;
            if (!wanted)
            {
                if (_sceneObject != null && _sceneObject.activeSelf) _sceneObject.SetActive(false);
                SceneStatus = "at rest";
                return;
            }
            try
            {
                var layer = SceneLayer();
                if (layer < 0)
                {
                    SceneStatus = "the main camera has no volume layer of its own";
                    return;
                }
                if (_sceneObject == null)
                {
                    _sceneObject = new GameObject("DD2Estate.CorridorSceneVolume");
                    _sceneObject.transform.SetParent(parent, false);
                    var volume = _sceneObject.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 100f;
                    if (_sceneProfile == null)
                    {
                        _sceneProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                        _sceneProfile.hideFlags = HideFlags.HideAndDontSave;
                        _adjust = _sceneProfile.Add<ColorAdjustments>(false);
                        _blur = _sceneProfile.Add<DepthOfField>(false);
                    }
                    volume.sharedProfile = _sceneProfile;
                }
                _sceneObject.layer = layer;
                if (!_sceneObject.activeSelf) _sceneObject.SetActive(true);
                _adjust.active = true;
                _adjust.saturation.Override(Mathf.Clamp((saturation - 1f) * 100f, -100f, 100f));
                // DD1 multiplies the stored colour; in exposure stops that is the light's own exponent times log2
                _adjust.postExposure.Override(Mathf.Log(Mathf.Max(0.02f, intensity), 2f) * CorridorLight.Gamma);
                _blur.active = blur > 0.01f;
                if (_blur.active)
                {
                    _blur.mode.Override(DepthOfFieldMode.Gaussian);
                    _blur.gaussianStart.Override(0f);
                    _blur.gaussianEnd.Override(0.01f);
                    _blur.gaussianMaxRadius.Override(Mathf.Clamp(blur * CorridorScript.BlurRadiusPerUnit, 0.5f, 1.5f));
                    _blur.highQualitySampling.Override(true);
                }
                SceneStatus = "saturation " + saturation.ToString("0.00") + ", intensity " + intensity.ToString("0.00") + ", blur " + blur.ToString("0.0") + " on layer " + layer;
            }
            catch (Exception e)
            {
                SceneStatus = "failed: " + e.Message;
            }
        }

        // A layer in the main camera's volume mask that no other camera of its stack listens to.
        private static int SceneLayer()
        {
            var main = Camera.main;
            if (main == null) return -1;
            var data = main.GetUniversalAdditionalCameraData();
            int mask = data.volumeLayerMask;
            if (data.cameraStack != null)
                foreach (var camera in data.cameraStack)
                    if (camera != null) mask &= ~(int)camera.GetUniversalAdditionalCameraData().volumeLayerMask;
            for (var layer = 0; layer < 32; layer++)
                if ((mask & (1 << layer)) != 0) return layer;
            return -1;
        }

        /// <summary>The expedition is over: the volumes, the grade's texture and DD1's tables are let go.</summary>
        public static void ReleaseAll()
        {
            Release();
            if (_sceneObject != null) UnityEngine.Object.Destroy(_sceneObject);
            _sceneObject = null;
            if (_lut != null) UnityEngine.Object.Destroy(_lut);
            _lut = null;
            _lutFor = null;
            _tables = null;
            _tablesFor = null;
            _strips = null;
            _stripsFor = null;
            Status = "released";
        }

        public static void Release()
        {
            if (_object != null) UnityEngine.Object.Destroy(_object);
            _object = null;
            _volume = null;
        }

        // DD1 (0xb04680, 0xac2a81): i = min(4, floor(g / 25)); the picture is lut[i] blended to lut[min(4, i + 1)] by
        // (g - 25 i) / 25. g is the grade's own number (RaidDisplay+0x3588), on its way to ceil(torch / 25) * 25: the
        // caller's (ShownLight.Grade), not the torch's value.
        private static bool Table(string dungeon, float torch)
        {
            if (!Tables(dungeon)) return false;
            var asset = UniversalRenderPipeline.asset;
            if (asset == null)
            {
                Status = "no render pipeline asset";
                return false;
            }
            var size = asset.colorGradingLutSize;
            var level = Mathf.Min(4, Mathf.FloorToInt(torch / 25f));
            var blend = Mathf.Clamp01((torch - level * 25f) / 25f);
            // the same picture again: keep the table (a hundredth of a step is not seen)
            var key = dungeon + "|" + size + "|" + level + "|" + Mathf.RoundToInt(blend * 100f) + "|" + (GrainOn ? CorridorNumbers.Grain.ToString("0.000") : "0");
            Level = level;
            Blend = blend;
            if (key == _lutFor && _lut != null) return true;

            // Each of DD1's five tables at the pipeline's size is worked out once a dungeon; the picture in use is
            // a plain mix of two of them. (While the light crosses into another band the mix changes every frame
            // for a second or more: ShownLight. Resampling 16^3 to the pipeline's size there cost a frame's worth.)
            var a = Strip(level, size);
            var b = Strip(Mathf.Min(4, level + 1), size);
            // DD1's grain brightens by 0.1 * (0.1 .. 1): its mean goes into the table, the noise is the pipeline's
            var lift = (GrainOn ? 1f + CorridorNumbers.Grain * 0.55f : 1f) * 255f;

            if (_lut == null || _lut.height != size)
            {
                if (_lut != null) UnityEngine.Object.Destroy(_lut);
                // the pipeline wants a strip of `size` slices side by side, read as plain numbers (not sRGB-decoded)
                _lut = new Texture2D(size * size, size, TextureFormat.RGBA32, false, true)
                {
                    name = "DD2Estate.Dd1ColourGrade",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            if (_pixels == null || _pixels.Length != size * size * size) _pixels = new Color32[size * size * size];
            for (int i = 0, at = 0; i < _pixels.Length; i++, at += 3)
            {
                _pixels[i] = new Color32(
                    (byte)Mathf.Min(255f, (a[at] + (b[at] - a[at]) * blend) * lift + 0.5f),
                    (byte)Mathf.Min(255f, (a[at + 1] + (b[at + 1] - a[at + 1]) * blend) * lift + 0.5f),
                    (byte)Mathf.Min(255f, (a[at + 2] + (b[at + 2] - a[at + 2]) * blend) * lift + 0.5f), 255);
            }
            _lut.SetPixels32(_pixels);
            _lut.Apply(false, false);
            _lutFor = key;
            Mixes++;
            return true;
        }

        private static Color32[] _pixels;
        private static float[][] _strips;           // DD1's five tables at the pipeline's size, in its strip's order, 0..1
        private static string _stripsFor;
        /// <summary>Dev bridge: how often the picture in use was mixed anew.</summary>
        public static int Mixes { get; private set; }

        // One of DD1's tables as the pipeline's strip: slice = blue along x, red inside the slice, green up from the bottom row.
        private static float[] Strip(int level, int size)
        {
            var key = _tablesFor + "|" + size;
            if (_strips == null || _stripsFor != key)
            {
                _strips = new float[5][];
                _stripsFor = key;
            }
            if (_strips[level] != null) return _strips[level];
            var table = _tables[level];
            var strip = new float[size * size * size * 3];
            var last = Dd1Size - 1;
            for (var blue = 0; blue < size; blue++)
                for (var green = 0; green < size; green++)
                    for (var red = 0; red < size; red++)
                    {
                        // film.glsl: texture3D(lut, rgb * 15/16 + 1/32): trilinear between the 16 texel centres
                        float fr = red * (float)last / (size - 1), fg = green * (float)last / (size - 1), fb = blue * (float)last / (size - 1);
                        int r0 = Mathf.Min(last - 1, (int)fr), g0 = Mathf.Min(last - 1, (int)fg), b0 = Mathf.Min(last - 1, (int)fb);
                        float tr = fr - r0, tg = fg - g0, tb = fb - b0;
                        float outR = 0f, outG = 0f, outB = 0f;
                        for (var corner = 0; corner < 8; corner++)
                        {
                            int dr = corner & 1, dg = (corner >> 1) & 1, db = (corner >> 2) & 1;
                            var weight = (dr == 1 ? tr : 1f - tr) * (dg == 1 ? tg : 1f - tg) * (db == 1 ? tb : 1f - tb);
                            if (weight <= 0f) continue;
                            var at = (((b0 + db) * Dd1Size + g0 + dg) * Dd1Size + r0 + dr) * 3;
                            outR += table[at] * weight;
                            outG += table[at + 1] * weight;
                            outB += table[at + 2] * weight;
                        }
                        var to = (green * size * size + blue * size + red) * 3;
                        strip[to] = outR;
                        strip[to + 1] = outG;
                        strip[to + 2] = outB;
                    }
            return _strips[level] = strip;
        }

        // DD1's file: 16 wide, 256 high; x is red, y (from the top) inside a block of 16 rows is green, the block is blue.
        private static bool Tables(string dungeon)
        {
            if (_tablesFor == dungeon) return _tables != null;
            _tablesFor = dungeon;
            _tables = null;
            _lutFor = null;
            var tables = new float[5][];
            for (var level = 0; level < 5; level++)
            {
                var file = "dungeons/" + dungeon + "/colour_grade_" + level + ".png";
                if (!Dd1Install.Exists(file)) file = Dd1Install.ArtPath(file);
                var texture = Dd1Install.Exists(file) ? Dd1Install.LoadTexture(file, true, false) : null;
                if (texture == null || texture.width != Dd1Size || texture.height != Dd1Size * Dd1Size)
                {
                    if (texture != null) UnityEngine.Object.Destroy(texture);
                    Status = "DD1 has no " + file + ": the view is not graded";
                    Plugin.Log.LogWarning("Corridor: " + Status);
                    return false;
                }
                var pixels = texture.GetPixels32();      // rows from the bottom
                var table = new float[Dd1Size * Dd1Size * Dd1Size * 3];
                for (var blue = 0; blue < Dd1Size; blue++)
                    for (var green = 0; green < Dd1Size; green++)
                        for (var red = 0; red < Dd1Size; red++)
                        {
                            var pixel = pixels[(Dd1Size * Dd1Size - 1 - (blue * Dd1Size + green)) * Dd1Size + red];
                            var at = ((blue * Dd1Size + green) * Dd1Size + red) * 3;
                            table[at] = pixel.r / 255f;
                            table[at + 1] = pixel.g / 255f;
                            table[at + 2] = pixel.b / 255f;
                        }
                tables[level] = table;
                UnityEngine.Object.Destroy(texture);
            }
            _tables = tables;
            return true;
        }

        /// <summary>Dev bridge: what DD1's grade makes of a display colour (0..255) at the torch level in use.</summary>
        public static int[] Probe(int red, int green, int blue)
        {
            if (_lut == null || _lutFor == null) return null;
            var size = _lut.height;
            Func<int, int> cell = v => Mathf.Clamp(Mathf.RoundToInt(v / 255f * (size - 1)), 0, size - 1);
            var pixel = _lut.GetPixel(cell(blue) * size + cell(red), cell(green));
            return new[] { Mathf.RoundToInt(pixel.r * 255f), Mathf.RoundToInt(pixel.g * 255f), Mathf.RoundToInt(pixel.b * 255f) };
        }
    }
}
