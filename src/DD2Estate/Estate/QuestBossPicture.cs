using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Menu;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// How large the game's picture of a Confession stands in DD1's marker on the Estate Map (the Darkest
    /// Dungeon's row: Estate/QuestPanel.cs, the boss places).
    ///
    /// DD1's marker (quest_select_length_plot_1.png, 96 by 96) is a black disc with a ring of light near its
    /// rim: measured about the marker's middle, the disc is dark out to 23.5 pixels, the ring's light begins at
    /// 24 and ends at 28.5, and the disc itself at 30. The game's picture (512 by 512, an emblem with spikes,
    /// drawn in light on a broad black outline) was fitted by its rectangle into a square of 44 and stood small
    /// in the ring: its rectangle has clear room round the emblem, the outline cannot be seen on the black
    /// disc, and drawn at a twelfth of its size from a texture without smaller copies of itself only its
    /// brightest lines came through. (The owner, 2026-10-06, of that marker: "make the icons inside the circle
    /// bigger".)
    ///
    /// Now the picture is fitted by what can be SEEN of it on the black disc: the smallest circle round its lit
    /// pixels (Core/RingFit.cs) is made the disc, and stands in the ring's middle. That is as large as the
    /// picture can be without crossing the ring, whatever its shape and wherever it stands in its rectangle.
    /// What of its black outline would then lie on the ring is cut away (the picture is clear beyond the
    /// radius where the ring's light begins), and the map draws a copy of the picture that has smaller copies
    /// of itself, so that it is as full at this size as it is at its own.
    /// A picture whose pixels cannot be read (packed tight into its sheet, or turned) is drawn as the game has
    /// it, its rectangle fitted into the disc.
    /// </summary>
    [EstateModule]
    internal static class QuestBossPicture
    {
        // Dev bridge:
        //   quests.bossfit                       each Confession's picture as it is fitted (its size, the circle
        //                                        round what is seen of it, the scale), and every one of them that
        //                                        stands on the map now: where on screen, and how far its circle
        //                                        reaches from the marker's middle, in DD1's pixels
        //   quests.bossfit {"fill":false}        the old size (the picture's rectangle in a square of 44);
        //                  {"inside":23.5} {"clip":24} {"lit":40}   the measures. The map shows it when it is next built.
        private static void Register()
        {
            DD2Estate.Dev.AgentBridge.Register("quests.bossfit", o =>
            {
                if (o["fill"] != null) Fill = (bool)o["fill"];
                if (o["inside"] != null) Inside = (float)o["inside"];
                if (o["clip"] != null) Clip = (float)o["clip"];
                if (o["lit"] != null) Lit = (int)o["lit"];
                if (o["inside"] != null || o["clip"] != null || o["lit"] != null) Forget();
                var pictures = new List<object>();
                foreach (var confession in DarkestBosses.Chain)
                {
                    var sprite = DarkestBosses.Picture(confession, out var pending);
                    if (sprite == null)
                    {
                        pictures.Add(new { act = confession.Act, picture = (string)null, pending });
                        continue;
                    }
                    var fit = Of(sprite);
                    pictures.Add(new
                    {
                        act = confession.Act, picture = sprite.name, size = new[] { sprite.rect.width, sprite.rect.height },
                        inTexture = sprite.texture != null ? sprite.texture.width + "x" + sprite.texture.height + (sprite.texture.mipmapCount > 1 ? " with " + sprite.texture.mipmapCount + " sizes" : " in one size") : null,
                        measured = fit.Measured, own = fit.Sprite != sprite, drawnFrom = new[] { fit.Sprite.rect.width, fit.Sprite.rect.height },
                        circle = new[] { fit.Centre.x, fit.Centre.y, fit.Radius }, scale = fit.Scale,
                        offset = new[] { fit.Offset.x, fit.Offset.y }, drawn = new[] { fit.Size.x, fit.Size.y }
                    });
                }
                var onMap = new List<object>();
                foreach (var image in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>())
                {
                    if (image.name != "Confession" || image.sprite == null || !image.gameObject.activeInHierarchy) continue;
                    var rect = image.rectTransform;
                    var marker = rect.parent as RectTransform;
                    // from the marker's middle to the picture's, in DD1's pixels (x to the right, y up)
                    var middle = rect.anchoredPosition - (marker != null ? new Vector2(marker.rect.width * 0.5f, -marker.rect.height * 0.5f) : Vector2.zero);
                    Made made = null;
                    foreach (var known in Pictures.Values)
                        if (known.Sprite == image.sprite) made = known;
                    var per = rect.rect.width / Mathf.Max(1f, image.sprite.rect.width);
                    var centre = made != null ? middle + (made.Centre - image.sprite.rect.size * 0.5f) * per : middle;
                    onMap.Add(new
                    {
                        place = marker != null && marker.parent != null && marker.name == "Body" ? marker.parent.name : marker != null ? marker.name : null,
                        picture = image.sprite.name,
                        at = EstateOptionsDev.OnScreen(rect),
                        size = new[] { rect.rect.width, rect.rect.height },
                        // the circle round what is seen: where its middle is from the marker's, and how far it reaches from the marker's middle
                        circleFromMiddle = made != null ? new[] { centre.x, centre.y } : null,
                        reach = made != null ? centre.magnitude + made.Radius * per : -1f,
                        tint = "#" + ColorUtility.ToHtmlStringRGBA(image.color)
                    });
                }
                return new { fill = Fill, inside = Inside, clip = Clip, lit = Lit, old = QuestPanel.BossPictureSize, pictures, onMap, window = new[] { Screen.width, Screen.height } };
            });
        }

        /// <summary>The radius of the dark disc inside the ring, in DD1's pixels: what is seen of the picture reaches this far.</summary>
        public static float Inside = 23.5f;
        /// <summary>Where the ring's light begins: the picture is clear from here out, so that nothing of it lies on the ring.</summary>
        public static float Clip = 24f;
        /// <summary>
        /// What counts as seen on the black disc: a pixel at least this light (its alpha times its lightest
        /// colour, of 255). The pictures' black outlines and the dim ends of their thinnest lines are below it.
        /// </summary>
        public static int Lit = 40;
        /// <summary>False: the picture as it was, the game's own, its rectangle in a square of <see cref="QuestPanel.BossPictureSize"/> (dev bridge, to look at the two).</summary>
        public static bool Fill = true;

        public struct Fit
        {
            /// <summary>The picture to draw: the map's own copy of the game's, or the game's.</summary>
            public Sprite Sprite;
            /// <summary>Its size on the map, in DD1's pixels, and DD1's pixels to one pixel of it.</summary>
            public Vector2 Size;
            public float Scale;
            /// <summary>Where its rectangle has its middle, from the ring's middle: x to the right, y up, in DD1's pixels.</summary>
            public Vector2 Offset;
            /// <summary>True: fitted by what is seen of it; false: by its rectangle.</summary>
            public bool Measured;
            /// <summary>The circle round what is seen, in the drawn picture's own pixels from its lower left corner.</summary>
            public Vector2 Centre;
            public float Radius;
        }

        private sealed class Made
        {
            public Sprite Sprite;
            public Vector2 Centre;
            public float Radius;
        }

        // by the game's picture: its pixels are read once, the map's copy is made once (null: it could not be)
        private static readonly Dictionary<int, Made> Pictures = new Dictionary<int, Made>();

        /// <summary>Dev bridge: the pictures are measured and copied again (a measure was changed).</summary>
        public static void Forget() => Pictures.Clear();

        public static Fit Of(Sprite sprite)
        {
            var size = sprite.rect.size;
            var longest = Mathf.Max(1f, Mathf.Max(size.x, size.y));
            if (!Fill)
            {
                var old = QuestPanel.BossPictureSize / longest;
                return new Fit { Sprite = sprite, Scale = old, Size = size * old };
            }
            if (!Pictures.TryGetValue(sprite.GetInstanceID(), out var made) || (made != null && made.Sprite == null))
            {
                made = Make(sprite);
                Pictures[sprite.GetInstanceID()] = made;
            }
            if (made == null)
            {
                var whole = Inside * 2f / longest;
                return new Fit { Sprite = sprite, Scale = whole, Size = size * whole, Centre = size * 0.5f, Radius = longest * 0.5f };
            }
            var own = made.Sprite.rect.size;
            var circle = new RingFit.Circle { X = made.Centre.x, Y = made.Centre.y, Radius = made.Radius };
            var scale = (float)RingFit.Scale(circle, Inside, Mathf.RoundToInt(own.x), Mathf.RoundToInt(own.y), out var dx, out var dy);
            return new Fit { Sprite = made.Sprite, Scale = scale, Size = own * scale, Offset = new Vector2((float)dx, (float)dy), Measured = true, Centre = made.Centre, Radius = made.Radius };
        }

        // The map's copy of a picture of the game's: the part of it that is painted, clear beyond the circle
        // that will lie where the ring's light begins, with smaller copies of itself. Null when the picture's
        // pixels cannot be read or nothing of it is light.
        private static Made Make(Sprite sprite)
        {
            try
            {
                // (rows from the bottom; the rectangle the picture has in its texture: its clear rim is cut off there)
                var pixels = EstateMenuIcon.Read(sprite, out var width, out var height);
                if (pixels == null) return null;
                var light = new byte[pixels.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    light[i] = (byte)(p.a * Mathf.Max(p.r, Mathf.Max(p.g, p.b)) / 255);
                }
                if (!RingFit.Around(light, width, height, (byte)Mathf.Clamp(Lit, 1, 255), out var circle)) return null;

                // whole out to the disc's edge, clear from where the ring's light begins, and a slope between the two
                var perPixel = Inside / Mathf.Max(1f, (float)circle.Radius);            // DD1's pixels to one of the picture's
                var cut = Clip / perPixel;
                var soft = Mathf.Max(0.5f, (Clip - Inside) / perPixel);
                for (var y = 0; y < height; y++)
                {
                    var dy = y + 0.5 - circle.Y;
                    for (var x = 0; x < width; x++)
                    {
                        var dx = x + 0.5 - circle.X;
                        var far = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (far <= cut - soft) continue;
                        var i = y * width + x;
                        var keep = Mathf.Clamp01((cut - far) / soft);
                        pixels[i].a = (byte)Mathf.RoundToInt(pixels[i].a * keep);
                    }
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
                {
                    name = "DD2Estate map " + sprite.name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(true, true);
                var copy = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                copy.name = sprite.name + " (estate map)";
                copy.hideFlags = HideFlags.HideAndDontSave;
                return new Made { Sprite = copy, Centre = new Vector2((float)circle.X, (float)circle.Y), Radius = (float)circle.Radius };
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Estate Map: the picture " + sprite.name + " could not be read; it is drawn as the game has it, fitted by its rectangle: " + e.Message);
                return null;
            }
        }
    }
}
