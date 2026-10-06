using System;
using System.Collections.Generic;
using Assets.Code.UI.Screens;
using DD2Estate.Dev;
using DD2Estate.Estate;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Menu
{
    /// <summary>
    /// The Estate's picture on the main menu, painted as the game paints its own two.
    ///
    /// READ FROM THE RUNNING GAME (ui.dump of LightSideMenuButtons, ui.png of the three pictures): the torch of
    /// Confessions and the crest of Kingdoms (icon_logo_confessions_small, icon_logo_kingdoms_small) are drawn
    /// white through the plain UI material; no material, no mask, no vertex colours: their look is in the
    /// sprites themselves. Each runs from a tan gold at its top to a dark rust at its foot (the lit pixels'
    /// mean, top band to foot band: Kingdoms 158,132,88 to 89,40,17; Confessions 151,116,71 to 76,32,15) under a
    /// worn, speckled texture. The entry's Button tints its word alone (its target graphic is the text), so the
    /// pictures look the same at rest, under the pointer, chosen and greyed.
    ///
    /// The Estate's book is the journal of the menu's bottom row (icon_inn_journal), a white picture shaded to
    /// grey towards its foot; the first builds tinted it with one colour, the crest's mean, and it stood flat
    /// beside the two. Here it is painted once, when the menu is first built: the book's shape and its fine
    /// texture are kept, its own shading from top to foot is taken out (every pixel's light is counted against
    /// the light of its row), and the colours of the game's two pictures are laid on from its top to its foot,
    /// band by band as they were read from those two. Nothing of it is a file: the picture is made at run time
    /// from the game's own sprites.
    /// </summary>
    [EstateModule]
    internal static class EstateMenuIcon
    {
        private const int Bands = 24;
        // a pixel belongs to a picture above half alpha; of the game's pictures only the lit pixels give their
        // colour (half of the crest's opaque pixels are its black ground)
        private const byte Opaque = 128, Lit = 60;
        // How far a pixel of the book may stand from its row's light. The game's own pictures: nine in ten of
        // their lit pixels lie between 0.68 and 1.21 of their band's mean.
        private const float Darkest = 0.6f, Lightest = 1.25f;
        // the rows a row's light is the mean of, either way, as a share of the picture's height
        private const float Window = 0.08f;

        private static Sprite _made, _madeOf;
        private static Color[] _ramp;
        private static string _status = "not asked for";
        private static readonly List<string> Models = new List<string>();

        private static void Register()
        {
            // The Estate's picture on the main menu: what it was made of, the colours laid on it from top to
            // foot, and the three pictures as they stand on the menu (sprite, colour, material, box on screen in
            // the frame's pixels: left, right, middle, top, bottom).
            AgentBridge.Register("menu.icon", o =>
            {
                var logos = new List<object>();
                var screen = UnityEngine.Object.FindObjectOfType<MainMenuUiScreenBhv>();
                var light = screen != null ? screen.transform.Find("UI/MenuButtons/LightSideMenuButtons") : null;
                if (light != null)
                    foreach (var image in light.GetComponentsInChildren<Image>(false))
                    {
                        if (image.name != "Logo") continue;
                        logos.Add(new
                        {
                            entry = image.transform.parent != null ? image.transform.parent.name : null,
                            sprite = image.sprite != null ? image.sprite.name : null,
                            size = image.sprite != null ? new[] { image.sprite.rect.width, image.sprite.rect.height } : null,
                            colour = "#" + ColorUtility.ToHtmlStringRGBA(image.color),
                            material = image.material != null ? image.material.name : null,
                            box = Dungeon.DungeonLookDev.Box(image.rectTransform)
                        });
                    }
                var ramp = new List<string>();
                if (_ramp != null)
                    foreach (var colour in _ramp) ramp.Add("#" + ColorUtility.ToHtmlStringRGB(colour));
                return new { status = _status, made = _made != null ? _made.name : null, of = _madeOf != null ? _madeOf.name : null, models = Models, ramp, menu = screen != null, logos };
            });
        }

        /// <summary>
        /// The book painted in the colours of the game's own pictures (<paramref name="models"/>: the crest of
        /// Kingdoms, the torch of Confessions), drawn where and as large as the book itself; null when the
        /// pictures cannot be read (the caller then tints the book as the first builds did).
        /// </summary>
        public static Sprite Make(Sprite book, params Sprite[] models)
        {
            if (book == null) return null;
            if (_made != null && _madeOf == book) return _made;
            try
            {
                Models.Clear();
                Color[] ramp = null;
                var read = 0;
                foreach (var model in models)
                {
                    var pixels = Read(model, out var w, out var h);
                    var its = pixels != null ? Ramp(pixels, w, h) : null;
                    if (its == null) continue;
                    Models.Add(model.name);
                    if (ramp == null) ramp = its;
                    else
                        for (var i = 0; i < Bands; i++) ramp[i] += its[i];
                    read++;
                }
                if (ramp == null)
                {
                    _status = "the game's own pictures could not be read";
                    return null;
                }
                for (var i = 0; i < Bands; i++) ramp[i] /= read;
                var paint = Read(book, out var width, out var height);
                if (paint == null)
                {
                    _status = "the book's picture could not be read";
                    return null;
                }
                var made = Gild(book, paint, width, height, ramp);
                if (made == null)
                {
                    _status = "the book's picture has nothing painted";
                    return null;
                }
                _ramp = ramp;
                _made = made;
                _madeOf = book;
                _status = "made";
                return made;
            }
            catch (Exception e)
            {
                _status = "failed: " + e.Message;
                Plugin.Log.LogWarning("main menu: the Estate's picture could not be painted like the game's own: " + e.Message);
                return null;
            }
        }

        // A sprite's own pixels (the rectangle it has in its texture), rows from the bottom, in the colours the
        // screen shows; null for a sprite whose place in its texture cannot be told (packed tight, or turned).
        // The game's textures cannot be read where they lie: the texture is drawn into a render target first.
        internal static Color32[] Read(Sprite sprite, out int width, out int height)
        {
            width = height = 0;
            if (sprite == null || sprite.texture == null) return null;
            if (sprite.packed && (sprite.packingMode != SpritePackingMode.Rectangle || sprite.packingRotation != SpritePackingRotation.None)) return null;
            var texture = sprite.texture;
            var area = sprite.textureRect;
            width = Mathf.Max(1, Mathf.RoundToInt(area.width));
            height = Mathf.Max(1, Mathf.RoundToInt(area.height));
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var before = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(Mathf.Round(area.x), Mathf.Round(area.y), width, height), 0, 0, false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }

        private static int Light(Color32 pixel) => Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));

        // The rows (from the bottom) of a picture's lowest and highest opaque pixel; false when it has none.
        private static bool Rows(Color32[] pixels, int width, int height, out int foot, out int top)
        {
            foot = top = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * width + x].a < Opaque) continue;
                    if (foot < 0) foot = y;
                    top = y;
                    break;
                }
            }
            return top >= 0;
        }

        // The colours of one of the game's pictures from its top to its foot: the mean of its lit pixels in each
        // of Bands bands of its height, smoothed a little (the bands differ with what is drawn in them).
        private static Color[] Ramp(Color32[] pixels, int width, int height)
        {
            if (!Rows(pixels, width, height, out var foot, out var top)) return null;
            var sums = new Vector3[Bands];
            var counts = new int[Bands];
            float tall = top - foot + 1;
            for (var y = foot; y <= top; y++)
            {
                var band = Mathf.Min(Bands - 1, (int)((top - y) / tall * Bands));
                for (var x = 0; x < width; x++)
                {
                    var pixel = pixels[y * width + x];
                    if (pixel.a < Opaque || Light(pixel) < Lit) continue;
                    sums[band] += new Vector3(pixel.r, pixel.g, pixel.b);
                    counts[band]++;
                }
            }
            var ramp = new Color[Bands];
            var any = -1;
            for (var i = 0; i < Bands; i++)
            {
                if (counts[i] == 0) continue;
                var mean = sums[i] / (counts[i] * 255f);
                ramp[i] = new Color(mean.x, mean.y, mean.z, 1f);
                if (any < 0) any = i;
            }
            if (any < 0) return null;
            // a band with nothing lit in it takes the colour of the band above it (the first ones: of the first that has one)
            for (var i = 0; i < Bands; i++)
                if (counts[i] == 0) ramp[i] = i > any ? ramp[i - 1] : ramp[any];
            for (var pass = 0; pass < 2; pass++)
            {
                var smooth = new Color[Bands];
                for (var i = 0; i < Bands; i++) smooth[i] = (ramp[Mathf.Max(0, i - 1)] + ramp[i] * 2f + ramp[Mathf.Min(Bands - 1, i + 1)]) / 4f;
                ramp = smooth;
            }
            return ramp;
        }

        private static Color At(Color[] ramp, float share)
        {
            var at = Mathf.Clamp01(share) * (Bands - 1);
            var band = Mathf.Min(Bands - 2, Mathf.FloorToInt(at));
            return Color.Lerp(ramp[band], ramp[band + 1], at - band);
        }

        // The book in the ramp's colours. The new picture is as large as the book's whole rectangle (the sprite
        // is its painted part with a clear margin), so it is drawn exactly where and as large as the book was.
        private static Sprite Gild(Sprite book, Color32[] paint, int width, int height, Color[] ramp)
        {
            if (!Rows(paint, width, height, out var foot, out var top)) return null;
            // each row's own light (the mean over its opaque pixels), then the mean over the rows around it:
            // what the book's own shading from top to foot gives that row
            var sums = new float[height];
            var counts = new int[height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var pixel = paint[y * width + x];
                    if (pixel.a < Opaque) continue;
                    sums[y] += Light(pixel) / 255f;
                    counts[y]++;
                }
            var around = Mathf.Max(1, Mathf.RoundToInt((top - foot + 1) * Window));
            var light = new float[height];
            for (var y = 0; y < height; y++)
            {
                var sum = 0f;
                var count = 0;
                for (var near = Mathf.Max(0, y - around); near <= Mathf.Min(height - 1, y + around); near++)
                {
                    sum += sums[near];
                    count += counts[near];
                }
                light[y] = count > 0 ? sum / count : 1f;
            }

            int wide = Mathf.Max(width, Mathf.RoundToInt(book.rect.width)), tall = Mathf.Max(height, Mathf.RoundToInt(book.rect.height));
            var offset = book.textureRectOffset;
            int left = Mathf.Clamp(Mathf.RoundToInt(offset.x), 0, wide - width), low = Mathf.Clamp(Mathf.RoundToInt(offset.y), 0, tall - height);
            var pixels = new Color32[wide * tall];
            var span = Mathf.Max(1f, top - foot);
            for (var row = 0; row < tall; row++)
            {
                var y = row - low;
                Color32 colour = At(ramp, (top - y) / span);
                for (var column = 0; column < wide; column++)
                {
                    var x = column - left;
                    // what is clear has its row's colour too, so that nothing dark is mixed into the picture's edge when it is drawn smaller
                    if (x < 0 || x >= width || y < 0 || y >= height)
                    {
                        pixels[row * wide + column] = new Color32(colour.r, colour.g, colour.b, 0);
                        continue;
                    }
                    var pixel = paint[y * width + x];
                    var share = light[y] > 0.001f ? Mathf.Clamp(Light(pixel) / 255f / light[y], Darkest, Lightest) : 1f;
                    pixels[row * wide + column] = new Color32(
                        (byte)Mathf.Min(255, Mathf.RoundToInt(colour.r * share)), (byte)Mathf.Min(255, Mathf.RoundToInt(colour.g * share)), (byte)Mathf.Min(255, Mathf.RoundToInt(colour.b * share)), pixel.a);
                }
            }
            var texture = new Texture2D(wide, tall, TextureFormat.RGBA32, true)
            {
                name = "DD2Estate menu book", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            var pivot = book.pivot;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, wide, tall), new Vector2(pivot.x / wide, pivot.y / tall), book.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "estate_menu_book (" + book.name + ")";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
