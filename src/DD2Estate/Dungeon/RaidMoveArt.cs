using System;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The pictures of "move", the narrow place beside the banner's five skills (DD1's "pass" stood there:
    /// panels/icons_ability/ability_pass.png, 20x72). The place is 40 wide (the owner, 2026-10-06: "the move
    /// button can be twice as wide; leave the arrows this size, but better find single-colour arrows and paint
    /// them a fitting colour"). Nothing of it is a file of the mod's: everything is put together at run time.
    ///
    /// - The arrow (<see cref="Arrow"/>) is DD2's own sign of a move, icon_move of its icon atlas: a double
    ///   chevron in one flat colour. Its shape is taken (the picture's alpha) and its colour left white, so
    ///   that the banner paints it (<see cref="ArrowColour"/>, DD2's colour for the sign on its own move
    ///   button). It stands twice in the place, back and forth, 14 px each as before.
    ///   FALLBACK when the game does not have the picture in memory: the shape of DD1's small move sign
    ///   (overlays/tray_move.png).
    /// - The ground and the frames of the place in DD2's look are DD2's (<see cref="Dd2Skills"/>). In DD1's
    ///   look (the fallback, and [Look] BannerSkills = dd1) they are made here of DD1's own pictures of a
    ///   square place of 72, with the middle of their width taken out: the empty place (ability_none.png), the
    ///   frame of the chosen skill and of the pointed-at one (selected_ability.png, focused_ability.png: 110
    ///   around a square of 72, 78 around the place of 40).
    /// </summary>
    internal static class RaidMoveArt
    {
        private const string Empty = "panels/icons_ability/ability_none.png";
        private const string Chosen = "panels/icons_ability/selected_ability.png";
        private const string Focused = "panels/icons_ability/focused_ability.png";
        private const string Dd1Sign = "overlays/tray_move.png";
        private const string Dd2Sign = "icon_move";

        /// <summary>The place's width and height in the banner's pixels.</summary>
        public const float Width = 40f, Height = 72f;
        /// <summary>The two arrows: each this large, this far apart, one over the other in the place's middle.</summary>
        public const float ArrowSize = 14f, ArrowGap = 4f;

        // DD1's pixels
        private const float Square = 72f;                                           // ability_none.png
        private const float FrameArt = 110f, FrameSide = 22f;                       // selected_ability.png: a side with its line
        private const float FrameMiddle = Width - Square + FrameArt - 2f * FrameSide;      // what is left of its middle around the narrower place
        private const float SignArt = 24f, SignInset = 2f;                          // tray_move.png: the picture inside the file

        /// <summary>The width of the frames of the chosen and the pointed-at place in DD1's look (their height is DD1's 110).</summary>
        public const float FrameWidth = FrameSide + FrameMiddle + FrameSide;
        public const float FrameHeight = FrameArt;

        /// <summary>
        /// What the arrows are painted with at rest, while "move" waits for a companion, and under the pointer:
        /// DD2's own three colours for its move button (the sign #CFCFCF, the mark of the chosen one #AF925D,
        /// of the pointed-at one #79C9CC; read from the fight's HUD).
        /// </summary>
        public static Color ArrowColour = new Color32(0xCF, 0xCF, 0xCF, 0xFF);
        public static Color ArrowChosen = new Color32(0xAF, 0x92, 0x5D, 0xFF);
        public static Color ArrowPointed = new Color32(0x79, 0xC9, 0xCC, 0xFF);

        private static Sprite _arrow, _ground, _chosen, _focused;
        private static bool _arrowFromDd2, _arrowFallbackTried, _groundTried, _chosenTried, _focusedTried;

        /// <summary>Where the arrow's shape comes from, for the dev bridge.</summary>
        public static string ArrowSource => _arrow == null ? "none" : _arrowFromDd2 ? "DD2 " + Dd2Sign : "DD1 " + Dd1Sign;

        /// <summary>
        /// The double chevron pointing right, white on clear ground; null while neither game's picture is to be
        /// had. DD2's comes when its icons are in memory: until then DD1's stands in, and is replaced.
        /// </summary>
        public static Sprite Arrow
        {
            get
            {
                if (_arrow != null && _arrowFromDd2) return _arrow;
                var own = Dd2Hud.InMemorySprite(Dd2Sign);
                if (own != null)
                {
                    try
                    {
                        var made = White(own);
                        if (made != null)
                        {
                            _arrow = made;
                            _arrowFromDd2 = true;
                            return _arrow;
                        }
                    }
                    catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: DD2's move sign could not be read (" + e.Message + "); DD1's is used"); }
                }
                if (_arrow != null || _arrowFallbackTried || !Dd1Install.Found) return _arrow;
                _arrowFallbackTried = true;
                try
                {
                    // FALLBACK: DD1's sign without its clear rim, its shape alone
                    var sign = Pixels.Read(Dd1Sign);
                    if (sign != null)
                    {
                        var inset = Mathf.RoundToInt(SignInset / SignArt * sign.Width);
                        var side = sign.Width - 2 * inset;
                        var cut = new Pixels(side, sign.Height - 2 * inset);
                        for (var y = 0; y < cut.Height; y++)
                            for (var x = 0; x < cut.Width; x++)
                                cut.Data[y * cut.Width + x] = new Color32(255, 255, 255, sign.At(x + inset, y + inset).a);
                        _arrow = cut.ToSprite("DD2Estate.MoveArrow.Dd1");
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: the arrow of \"move\" could not be made: " + e.Message); }
                return _arrow;
            }
        }

        /// <summary>DD1's look: the empty place at 40x72; null when DD1's picture is not there.</summary>
        public static Sprite Dd1Ground => Made(ref _ground, ref _groundTried, () => Narrowed(Empty, Square, Width * 0.5f, 0f, 0f, Width * 0.5f, "DD2Estate.MoveGround"));

        /// <summary>DD1's frame of a chosen skill around the place (<see cref="FrameWidth"/> x 110); null without DD1's picture.</summary>
        public static Sprite Dd1Chosen => Made(ref _chosen, ref _chosenTried, () => Narrowed(Chosen, FrameArt, FrameSide, (FrameArt - FrameMiddle) * 0.5f, FrameMiddle, FrameSide, "DD2Estate.MoveChosen"));

        /// <summary>DD1's frame of the skill under the pointer around the place; null without DD1's picture.</summary>
        public static Sprite Dd1Focused => Made(ref _focused, ref _focusedTried, () => Narrowed(Focused, FrameArt, FrameSide, (FrameArt - FrameMiddle) * 0.5f, FrameMiddle, FrameSide, "DD2Estate.MoveFocused"));

        private static Sprite Made(ref Sprite sprite, ref bool tried, Func<Sprite> make)
        {
            if (sprite != null || tried) return sprite;
            if (!Dd1Install.Found) return null;
            tried = true;
            try { sprite = make(); }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: a picture of the narrow \"move\" could not be made: " + e.Message); }
            return sprite;
        }

        /// <summary>Dev bridge: the pictures are made anew (after assets/ was switched).</summary>
        public static void Forget()
        {
            _arrow = _ground = _chosen = _focused = null;
            _arrowFromDd2 = _arrowFallbackTried = _groundTried = _chosenTried = _focusedTried = false;
        }

        // The shape of a picture of DD2's in white. The game's atlases cannot be read, so the picture is drawn
        // into a texture that can (its own part of the atlas only), and of what comes back the alpha is kept.
        private static Sprite White(Sprite source)
        {
            var texture = source.texture;
            if (texture == null || (source.packed && source.packingRotation != SpritePackingRotation.None)) return null;
            var area = source.textureRect;
            int width = Mathf.Max(1, Mathf.RoundToInt(area.width)), height = Mathf.Max(1, Mathf.RoundToInt(area.height));
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var before = RenderTexture.active;
            Texture2D read = null;
            try
            {
                Graphics.Blit(texture, target, new Vector2(area.width / texture.width, area.height / texture.height), new Vector2(area.x / texture.width, area.y / texture.height));
                RenderTexture.active = target;
                read = new Texture2D(width, height, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                read.Apply();
                var pixels = read.GetPixels32();
                var solid = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a > 127) solid++;
                    pixels[i] = new Color32(255, 255, 255, pixels[i].a);
                }
                // nothing of a shape came back (a texture that would not be drawn): the fallback
                if (solid < pixels.Length / 20) return null;
                // drawn far smaller than it is: with smaller copies of itself to draw from
                var made = new Texture2D(width, height, TextureFormat.RGBA32, true)
                {
                    name = "DD2Estate.MoveArrow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.HideAndDontSave
                };
                made.SetPixels32(pixels);
                made.Apply(true, true);
                var sprite = Sprite.Create(made, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = "DD2Estate.MoveArrow";
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                if (read != null) UnityEngine.Object.Destroy(read);
            }
        }

        // A picture's pixels, rows from the top.
        private sealed class Pixels
        {
            public readonly int Width, Height;
            public readonly Color32[] Data;

            public Pixels(int width, int height)
            {
                Width = width;
                Height = height;
                Data = new Color32[width * height];
            }

            public static Pixels Read(string dd1File)
            {
                if (!Dd1Install.Exists(dd1File)) return null;
                var texture = Dd1Install.LoadTexture(dd1File, true, false);
                if (texture == null) return null;
                try
                {
                    var read = new Pixels(texture.width, texture.height);
                    var raw = texture.GetPixels32();
                    for (var y = 0; y < read.Height; y++) Array.Copy(raw, (read.Height - 1 - y) * read.Width, read.Data, y * read.Width, read.Width);
                    return read;
                }
                finally { UnityEngine.Object.Destroy(texture); }
            }

            public Color32 At(int x, int y) => Data[Mathf.Clamp(y, 0, Height - 1) * Width + Mathf.Clamp(x, 0, Width - 1)];

            public Sprite ToSprite(string name)
            {
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                {
                    name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
                };
                var raw = new Color32[Data.Length];
                for (var y = 0; y < Height; y++) Array.Copy(Data, y * Width, raw, (Height - 1 - y) * Width, Width);
                texture.SetPixels32(raw);
                texture.Apply(false, true);
                var sprite = Sprite.Create(texture, new Rect(0f, 0f, Width, Height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }
        }

        // A picture of DD1's with the middle of its width taken out: what is left of the left side, of the middle
        // and of the right side stand side by side (all in DD1's pixels of a picture `art` wide, so a picture of
        // the mod's own in DD1's place, assets/, may have any size).
        private static Sprite Narrowed(string dd1File, float art, float left, float middleFrom, float middle, float right, string name)
        {
            var from = Pixels.Read(dd1File);
            if (from == null) return null;
            var scale = from.Width / art;
            var kept = left + middle + right;
            var to = new Pixels(Mathf.Max(1, Mathf.RoundToInt(kept * scale)), from.Height);
            for (var x = 0; x < to.Width; x++)
            {
                var u = (x + 0.5f) / to.Width * kept;
                var source = u < left ? u : u < left + middle ? middleFrom + (u - left) : art - (kept - u);
                var column = Mathf.FloorToInt(source * scale);
                for (var y = 0; y < to.Height; y++) to.Data[y * to.Width + x] = from.At(column, y);
            }
            return to.ToSprite(name);
        }
    }
}
