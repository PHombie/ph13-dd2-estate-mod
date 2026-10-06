using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The flame on DD1's torch: the particle emitter of overlays/torch_flame.plist (a cocos2d "gravity" emitter:
    /// some hundreds of soft blobs of overlays/torch_flame.png, born at the torch's head in DD1's orange, drifting
    /// up and swirling for 1.4 s while they fade to a pale one, added onto the screen so that the thick of them
    /// burns yellow). The UI cannot add light to what is under it, so the blobs are summed here into one small
    /// picture every frame, and that picture is drawn like any other: where few blobs lie it is thin and red,
    /// where many lie it is bright, as the sum on DD1's screen is.
    ///
    /// The emitter's numbers are read from the plist (the values written beside each read are DD1's stock ones,
    /// a FALLBACK). GUESS: how the flame follows the torchlight. DD1's own frames (_lab/dd1_ref/raid/raid_torch_level_*_clean.png)
    /// show a red glow without tongues at 12, a flame some 30 px high at 37 and 62 and one some 45 px high at 87
    /// and 100; the files do not say by what rule. Here the number of blobs born and how far they rise both
    /// follow the light.
    /// </summary>
    internal class RaidTorchFlame
    {
        public const int Size = 128;                // the picture's side, in DD1's pixels
        public static readonly Vector2 Emitter = new Vector2(64f, 40f);     // where the blobs are born in it, from its lower left corner

        private struct Blob
        {
            public float X, Y, Vx, Vy, Age, Life, Size0, Size1, Alpha0, Tangential;
        }

        private readonly Texture2D _texture;
        private readonly Color32[] _pixels = new Color32[Size * Size];
        private readonly float[] _sum = new float[Size * Size * 3];
        private readonly List<Blob> _blobs = new List<Blob>();
        private readonly float[] _kernel;
        private readonly int _kernelSize;
        private readonly System.Random _random = new System.Random(7);
        private float _owed;

        // overlays/torch_flame.plist
        private readonly float _life = 1.4f, _lifeVar, _max = 500f;
        private readonly float _angle = -244.11f, _angleVar = -142.62f, _speed, _speedVar = 28f;
        private readonly float _gravityX, _gravityY = -50f, _tangential = -36f, _tangentialVar = -26f;
        private readonly float _size0 = 15f, _size0Var = 17f, _size1 = 19.5f, _size1Var = 19.5f;
        private readonly Color _start = new Color(0.8373f, 0.3032f, 0f, 1f), _finish = new Color(1f, 0.5444f, 0.3699f, 0f);
        private readonly float _startAlphaVar = 1f;
        private readonly bool _flipped = true;

        public Texture2D Texture => _texture;

        public RaidTorchFlame()
        {
            _texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave, name = "DD2Estate.TorchFlame" };
            // the blob: the particle's picture as a square of weights (its brightness times its coverage)
            _kernelSize = 16;
            _kernel = new float[_kernelSize * _kernelSize];
            var read = false;
            try
            {
                var art = Dd1Install.Found && Dd1Install.Exists("overlays/torch_flame.png") ? Dd1Install.LoadTexture("overlays/torch_flame.png", true, false) : null;
                if (art != null)
                {
                    var source = art.GetPixels32();
                    for (var y = 0; y < _kernelSize; y++)
                        for (var x = 0; x < _kernelSize; x++)
                        {
                            var p = source[Mathf.Min(art.height - 1, y * art.height / _kernelSize) * art.width + Mathf.Min(art.width - 1, x * art.width / _kernelSize)];
                            _kernel[y * _kernelSize + x] = Mathf.Max(p.r, Mathf.Max(p.g, p.b)) / 255f * (p.a / 255f);
                        }
                    UnityEngine.Object.Destroy(art);
                    read = true;
                }
                var plist = Dd1Install.Found ? Dd1Install.ReadText("overlays/torch_flame.plist") : null;
                if (plist != null)
                {
                    var values = new Dictionary<string, float>();
                    foreach (Match m in Regex.Matches(plist, "<key>(\\w+)</key>\\s*<(?:real|integer)>([-0-9.eE]+)</(?:real|integer)>"))
                        if (float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) values[m.Groups[1].Value] = value;
                    float V(string key, float stock) => values.TryGetValue(key, out var v) ? v : stock;
                    _life = Mathf.Max(0.1f, V("particleLifespan", _life));
                    _lifeVar = V("particleLifespanVariance", _lifeVar);
                    _max = Mathf.Clamp(V("maxParticles", _max), 1f, 2000f);
                    _angle = V("angle", _angle);
                    _angleVar = V("angleVariance", _angleVar);
                    _speed = V("speed", _speed);
                    _speedVar = V("speedVariance", _speedVar);
                    _gravityX = V("gravityx", _gravityX);
                    _gravityY = V("gravityy", _gravityY);
                    _tangential = V("tangentialAcceleration", _tangential);
                    _tangentialVar = V("tangentialAccelVariance", _tangentialVar);
                    _size0 = V("startParticleSize", _size0);
                    _size0Var = V("startParticleSizeVariance", _size0Var);
                    _size1 = V("finishParticleSize", _size1);
                    _size1Var = V("finishParticleSizeVariance", _size1Var);
                    _start = new Color(V("startColorRed", _start.r), V("startColorGreen", _start.g), V("startColorBlue", _start.b), V("startColorAlpha", _start.a));
                    _finish = new Color(V("finishColorRed", _finish.r), V("finishColorGreen", _finish.g), V("finishColorBlue", _finish.b), V("finishColorAlpha", _finish.a));
                    _startAlphaVar = V("startColorVarianceAlpha", _startAlphaVar);
                    _flipped = V("yCoordFlipped", -1f) < 0f;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: DD1's torch flame could not be read, its stock numbers are used: " + e.Message); }
            if (!read)
            {
                // FALLBACK: a round soft blob
                for (var y = 0; y < _kernelSize; y++)
                    for (var x = 0; x < _kernelSize; x++)
                    {
                        var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(_kernelSize * 0.5f, _kernelSize * 0.5f)) / (_kernelSize * 0.5f);
                        _kernel[y * _kernelSize + x] = Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
                    }
            }
        }

        private float Spread(float variance) => (float)(_random.NextDouble() * 2.0 - 1.0) * variance;

        /// <summary>A step of the fire: <paramref name="light"/> is the torchlight, 0..1.</summary>
        public void Tick(float dt, float light)
        {
            dt = Mathf.Clamp(dt, 0f, 0.05f);
            light = Mathf.Clamp01(light);
            // the plist's y points down the screen (yCoordFlipped): its gravity lifts the blobs
            var up = _flipped ? -1f : 1f;
            var rise = Mathf.Lerp(0.45f, 1f, light);
            for (var i = _blobs.Count - 1; i >= 0; i--)
            {
                var b = _blobs[i];
                b.Age += dt;
                if (b.Age >= b.Life)
                {
                    _blobs[i] = _blobs[_blobs.Count - 1];
                    _blobs.RemoveAt(_blobs.Count - 1);
                    continue;
                }
                // cocos2d's gravity mode: gravity, and an acceleration across the line from the emitter
                float rx = b.X - Emitter.x, ry = b.Y - Emitter.y;
                var length = Mathf.Sqrt(rx * rx + ry * ry);
                float tx = 0f, ty = 0f;
                if (length > 0.001f)
                {
                    tx = -ry / length * b.Tangential;
                    ty = rx / length * b.Tangential;
                }
                b.Vx += (_gravityX + tx) * dt;
                b.Vy += (_gravityY * up * rise + ty) * dt;
                b.X += b.Vx * dt;
                b.Y += b.Vy * dt;
                _blobs[i] = b;
            }
            // maxParticles over a lifespan are born a second at full light
            _owed += _max / _life * light * dt;
            while (_owed >= 1f && _blobs.Count < _max)
            {
                _owed -= 1f;
                var angle = (_angle + Spread(_angleVar)) * Mathf.Deg2Rad;
                var speed = _speed + Spread(_speedVar);
                _blobs.Add(new Blob
                {
                    X = Emitter.x, Y = Emitter.y, Vx = Mathf.Cos(angle) * speed, Vy = Mathf.Sin(angle) * speed * up,
                    Life = Mathf.Max(0.1f, _life + Spread(_lifeVar)), Size0 = Mathf.Max(0f, _size0 + Spread(_size0Var)), Size1 = Mathf.Max(0f, _size1 + Spread(_size1Var)),
                    Alpha0 = Mathf.Clamp01(_start.a + Spread(_startAlphaVar)), Tangential = _tangential + Spread(_tangentialVar)
                });
            }
            if (_owed > 1f) _owed = 1f;
            Draw();
        }

        // The blobs summed, as DD1's screen sums them (source alpha onto one), and written as a picture whose
        // colour is the sum's hue and whose coverage is its strength.
        private void Draw()
        {
            Array.Clear(_sum, 0, _sum.Length);
            foreach (var b in _blobs)
            {
                var t = b.Age / b.Life;
                var size = Mathf.Lerp(b.Size0, b.Size1, t);
                if (size < 1f) continue;
                var alpha = Mathf.Lerp(b.Alpha0, _finish.a, t);
                if (alpha <= 0.003f) continue;
                float r = Mathf.Lerp(_start.r, _finish.r, t) * alpha, g = Mathf.Lerp(_start.g, _finish.g, t) * alpha, bl = Mathf.Lerp(_start.b, _finish.b, t) * alpha;
                int x0 = Mathf.FloorToInt(b.X - size * 0.5f), y0 = Mathf.FloorToInt(b.Y - size * 0.5f), n = Mathf.CeilToInt(size);
                for (var py = 0; py < n; py++)
                {
                    var y = y0 + py;
                    if (y < 0 || y >= Size) continue;
                    var ky = Mathf.Min(_kernelSize - 1, py * _kernelSize / n) * _kernelSize;
                    for (var px = 0; px < n; px++)
                    {
                        var x = x0 + px;
                        if (x < 0 || x >= Size) continue;
                        var w = _kernel[ky + Mathf.Min(_kernelSize - 1, px * _kernelSize / n)];
                        if (w <= 0.003f) continue;
                        var at = (y * Size + x) * 3;
                        _sum[at] += r * w;
                        _sum[at + 1] += g * w;
                        _sum[at + 2] += bl * w;
                    }
                }
            }
            for (var i = 0; i < _pixels.Length; i++)
            {
                float r = Mathf.Min(1f, _sum[i * 3]), g = Mathf.Min(1f, _sum[i * 3 + 1]), b = Mathf.Min(1f, _sum[i * 3 + 2]);
                var strength = Mathf.Max(r, Mathf.Max(g, b));
                if (strength <= 0.004f)
                {
                    _pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }
                // the UI multiplies a picture's colour by its coverage: the hue is stored at full strength
                _pixels[i] = new Color32((byte)(r / strength * 255f), (byte)(g / strength * 255f), (byte)(b / strength * 255f), (byte)(strength * 255f));
            }
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        public int Blobs => _blobs.Count;

        public void Destroy()
        {
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
        }
    }
}
