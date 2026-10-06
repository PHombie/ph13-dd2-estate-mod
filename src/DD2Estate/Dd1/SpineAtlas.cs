using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// A libgdx texture atlas (`*.atlas` text next to its page PNGs), the packing DD1 uses for all Spine art.
    /// Regions come out as upright sprites: a `rotate: true` region is stored turned 90 degrees
    /// counter-clockwise in the page and Unity sprites cannot be rotated, so those are copied into a small
    /// texture of their own when the atlas loads.
    /// </summary>
    internal class SpineAtlas
    {
        public class Region
        {
            public string Name;
            /// <summary>Top-left corner in the page, y down.</summary>
            public int X, Y;
            /// <summary>Upright size of the packed pixels (a rotated region occupies Height x Width in the page).</summary>
            public int Width, Height;
            public bool Rotated;
            /// <summary>Size before whitespace stripping, and where the packed pixels sit in it (from the bottom-left).</summary>
            public int OriginalWidth, OriginalHeight, OffsetX, OffsetY;
            public int Index = -1;
            /// <summary>Upright, pivot at the centre of the packed pixels.</summary>
            public Sprite Sprite;
            /// <summary>
            /// Where the upright pixels are, for whoever draws the region as a mesh: the page, or the region's own
            /// texture when it was packed rotated, and the pixel rectangle in it (from the bottom-left).
            /// </summary>
            public Texture2D Texture;
            public Rect TextureRect;
            /// <summary>Coarse opacity map; only when the atlas was loaded with alphaMasks.</summary>
            public AlphaMask Mask;

            internal int Page;
        }

        /// <summary>Which parts of a region are opaque enough to click, sampled every few pixels.</summary>
        public class AlphaMask
        {
            public const int Step = 4;
            public const byte Threshold = 64;

            private readonly int _width, _height, _columns, _rows;
            private readonly bool[] _solid;       // one cell per Step x Step pixels, row 0 = bottom

            internal AlphaMask(int width, int height)
            {
                _width = width;
                _height = height;
                _columns = Mathf.Max(1, (width + Step - 1) / Step);
                _rows = Mathf.Max(1, (height + Step - 1) / Step);
                _solid = new bool[_columns * _rows];
            }

            internal int Columns => _columns;
            internal int Rows => _rows;
            internal void Set(int column, int row, bool solid) { _solid[row * _columns + column] = solid; }

            /// <summary>u, v in 0..1 from the bottom-left of the upright region.</summary>
            public bool Solid(float u, float v)
            {
                if (u < 0f || v < 0f || u >= 1f || v >= 1f) return false;
                return _solid[(int)(v * _height) / Step * _columns + (int)(u * _width) / Step];
            }
        }

        private static readonly Dictionary<string, SpineAtlas> Cache = new Dictionary<string, SpineAtlas>(StringComparer.OrdinalIgnoreCase);

        public readonly string Path;
        public readonly Dictionary<string, Region> Regions = new Dictionary<string, Region>();
        private readonly List<string> _pages = new List<string>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private bool _masks, _straight;

        private SpineAtlas(string path) { Path = path; }

        public Region Find(string name)
        {
            return Regions.TryGetValue(name, out var region) ? region : null;
        }

        public Sprite Sprite(string name)
        {
            return Regions.TryGetValue(name, out var region) ? region.Sprite : null;
        }

        /// <summary>
        /// Loads an atlas from the DD1 install (cached), e.g. "fx/town_abbey_level01/town_abbey_level01.sprite.atlas".
        /// Null if the atlas or one of its pages is missing.
        /// <paramref name="straight"/>: DD1 stores a sprite sheet with its colours multiplied by their alpha and
        /// draws it so (seen on the loading screen's torch, whose glow is as bright in the real game as the
        /// sheet's own numbers). A canvas multiplies a picture's colours by its alpha itself; for one, the colours
        /// are divided by the alpha first. Such an atlas is kept apart from the sheet as it is stored.
        /// </summary>
        public static SpineAtlas Load(string relative, bool alphaMasks = false, bool straight = false)
        {
            var key = straight ? relative + "|straight" : relative;
            if (Cache.TryGetValue(key, out var cached) && (cached._masks || !alphaMasks)) return cached;
            var text = Dd1Install.ReadText(relative);
            if (text == null)
            {
                Plugin.Log.LogWarning("DD1 file missing: " + relative);
                return null;
            }

            var atlas = new SpineAtlas(relative) { _masks = alphaMasks, _straight = straight };
            atlas.Parse(text);
            int slash = relative.LastIndexOfAny(new[] { '/', '\\' });
            var folder = slash < 0 ? "" : relative.Substring(0, slash + 1);
            for (int page = 0; page < atlas._pages.Count; page++)
            {
                if (atlas.BuildPage(page, folder + atlas._pages[page], alphaMasks)) continue;
                atlas.Destroy();
                return null;
            }
            // An atlas reloaded for its masks leaves the old one to whoever still shows its sprites.
            Cache[key] = atlas;
            return atlas;
        }

        /// <summary>Frees an atlas's textures. Only for atlases whose sprites are no longer on screen.</summary>
        public static void Unload(string relative)
        {
            if (!Cache.TryGetValue(relative, out var atlas)) return;
            Cache.Remove(relative);
            atlas.Destroy();
        }

        private void Destroy()
        {
            foreach (var obj in _owned)
                if (obj != null) UnityEngine.Object.Destroy(obj);
            _owned.Clear();
        }

        // Page: a file name line, then `key: value` lines; a blank line starts the next page. Region: a name
        // line, then indented `key: value` lines.
        private void Parse(string text)
        {
            Region region = null;
            bool expectPage = true;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    expectPage = true;
                    region = null;
                    continue;
                }
                int colon = line.IndexOf(':');
                if (expectPage)
                {
                    _pages.Add(line);
                    expectPage = false;
                }
                else if (colon < 0)
                {
                    region = new Region { Name = line, Page = _pages.Count - 1 };
                    Regions[line] = region;
                }
                else if (region != null)
                {
                    var key = line.Substring(0, colon).Trim();
                    var values = line.Substring(colon + 1).Split(',');
                    switch (key)
                    {
                        case "rotate": region.Rotated = values[0].Trim() == "true"; break;
                        case "xy": region.X = Int(values, 0); region.Y = Int(values, 1); break;
                        case "size": region.Width = Int(values, 0); region.Height = Int(values, 1); break;
                        case "orig": region.OriginalWidth = Int(values, 0); region.OriginalHeight = Int(values, 1); break;
                        case "offset": region.OffsetX = Int(values, 0); region.OffsetY = Int(values, 1); break;
                        case "index": region.Index = Int(values, 0); break;
                    }
                }
            }
            foreach (var r in Regions.Values)
            {
                if (r.OriginalWidth == 0) r.OriginalWidth = r.Width;
                if (r.OriginalHeight == 0) r.OriginalHeight = r.Height;
            }
        }

        private static int Int(string[] values, int index)
        {
            return index < values.Length && int.TryParse(values[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private bool BuildPage(int page, string relative, bool alphaMasks)
        {
            bool needPixels = alphaMasks || _straight;
            foreach (var r in Regions.Values)
                if (r.Page == page && r.Rotated) needPixels = true;

            var texture = Dd1Install.LoadTexture(relative, needPixels, false);
            if (texture == null) return false;
            _owned.Add(texture);
            int pageWidth = texture.width, pageHeight = texture.height;
            Color32[] pixels = needPixels ? texture.GetPixels32() : null;   // rows bottom-up
            if (_straight)
            {
                // colour times alpha, as stored, back to the colour itself
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    if (p.a == 0 || p.a == 255) continue;
                    pixels[i] = new Color32((byte)Mathf.Min(255, (p.r * 255 + p.a / 2) / p.a), (byte)Mathf.Min(255, (p.g * 255 + p.a / 2) / p.a),
                        (byte)Mathf.Min(255, (p.b * 255 + p.a / 2) / p.a), p.a);
                }
                texture.SetPixels32(pixels);
            }

            foreach (var r in Regions.Values)
            {
                if (r.Page != page) continue;
                int packedWidth = r.Rotated ? r.Height : r.Width;
                int packedHeight = r.Rotated ? r.Width : r.Height;
                if (r.Width <= 0 || r.Height <= 0 || r.X < 0 || r.Y < 0 || r.X + packedWidth > pageWidth || r.Y + packedHeight > pageHeight)
                {
                    Plugin.Log.LogWarning("DD1 atlas region outside its page: " + Path + " / " + r.Name);
                    continue;
                }

                Texture2D source = texture;
                var rect = new Rect(r.X, pageHeight - r.Y - r.Height, r.Width, r.Height);
                Color32[] upright = null;
                if (r.Rotated)
                {
                    // Upright pixel (x, y from the top) is page pixel (X + y, Y + Width - 1 - x).
                    upright = new Color32[r.Width * r.Height];
                    for (int y = 0; y < r.Height; y++)
                    {
                        int row = (r.Height - 1 - y) * r.Width;
                        for (int x = 0; x < r.Width; x++)
                            upright[row + x] = pixels[(pageHeight - 1 - (r.Y + r.Width - 1 - x)) * pageWidth + r.X + y];
                    }
                    source = new Texture2D(r.Width, r.Height, TextureFormat.RGBA32, false)
                    {
                        name = Path + "/" + r.Name,
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    source.SetPixels32(upright);
                    source.Apply(false, true);
                    _owned.Add(source);
                    rect = new Rect(0, 0, r.Width, r.Height);
                }

                r.Texture = source;
                r.TextureRect = rect;
                r.Sprite = UnityEngine.Sprite.Create(source, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                r.Sprite.name = r.Name;
                r.Sprite.hideFlags = HideFlags.HideAndDontSave;
                _owned.Add(r.Sprite);

                if (!alphaMasks) continue;
                var mask = new AlphaMask(r.Width, r.Height);
                for (int row = 0; row < mask.Rows; row++)
                {
                    int y = Mathf.Min(r.Height - 1, row * AlphaMask.Step + AlphaMask.Step / 2);    // from the bottom
                    for (int column = 0; column < mask.Columns; column++)
                    {
                        int x = Mathf.Min(r.Width - 1, column * AlphaMask.Step + AlphaMask.Step / 2);
                        byte alpha = upright != null
                            ? upright[y * r.Width + x].a
                            : pixels[(pageHeight - r.Y - r.Height + y) * pageWidth + r.X + x].a;
                        mask.Set(column, row, alpha >= AlphaMask.Threshold);
                    }
                }
                r.Mask = mask;
            }

            // Drop the CPU copy; the page stays on the GPU for the sprites cut straight from it.
            if (needPixels) texture.Apply(false, true);
            return true;
        }
    }
}
