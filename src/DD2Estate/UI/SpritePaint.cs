using System;
using UnityEngine;

namespace DD2Estate.UI
{
    /// <summary>
    /// How much of its own rectangle a picture of the game's fills: the box of its painted pixels. The game's
    /// textures cannot be read where they lie, so a texture is drawn into a render target and read from there.
    /// For measuring (the trinket pictures' margins) and for laying a hot spot on what is painted (a path's badge).
    /// </summary>
    internal static class SpritePaint
    {
        // a pixel counts as painted above a tenth of full alpha
        private const byte Painted = 25;

        /// <summary>Every pixel of a texture, rows from the bottom, as GetPixels32 gives them.</summary>
        public static Color32[] Pixels(Texture2D texture)
        {
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0, false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }

        /// <summary>
        /// The box of a sprite's painted pixels in the sprite's own pixels: left, top, right, bottom, y down.
        /// False for a sprite whose place in its texture cannot be told (packed tight or turned) or that has
        /// nothing painted. <paramref name="pixels"/> are its texture's (<see cref="Pixels"/>).
        /// </summary>
        public static bool Box(Sprite sprite, Color32[] pixels, int textureWidth, out Rect box)
        {
            box = default;
            Rect inTexture;
            Vector2 offset;
            try
            {
                if (sprite.packed && (sprite.packingMode != SpritePackingMode.Rectangle || sprite.packingRotation != SpritePackingRotation.None)) return false;
                inTexture = sprite.textureRect;
                offset = sprite.textureRectOffset;
            }
            catch (Exception) { return false; }
            int x0 = Mathf.RoundToInt(inTexture.xMin), x1 = Mathf.RoundToInt(inTexture.xMax), y0 = Mathf.RoundToInt(inTexture.yMin), y1 = Mathf.RoundToInt(inTexture.yMax);
            int left = int.MaxValue, right = int.MinValue, low = int.MaxValue, high = int.MinValue;
            for (var y = y0; y < y1; y++)
            {
                var row = y * textureWidth;
                for (var x = x0; x < x1; x++)
                {
                    if (row + x >= pixels.Length || pixels[row + x].a <= Painted) continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < low) low = y;
                    if (y > high) high = y;
                }
            }
            if (left > right) return false;
            var size = sprite.rect.size;
            // a texture's rows count from the bottom
            box = Rect.MinMaxRect(left - x0 + offset.x, size.y - (high + 1 - y0 + offset.y), right + 1 - x0 + offset.x, size.y - (low - y0 + offset.y));
            return true;
        }

        /// <summary>The same for one sprite alone; false also when its texture cannot be read.</summary>
        public static bool Box(Sprite sprite, out Rect box)
        {
            box = default;
            if (sprite == null || sprite.texture == null) return false;
            try
            {
                return Box(sprite, Pixels(sprite.texture), sprite.texture.width, out box);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("The picture " + sprite.name + " could not be read for its painted part: " + e.Message);
                return false;
            }
        }

        /// <summary>The box of a sprite's mesh in the sprite's own pixels, y down: what Unity keeps of a picture when it packs it, a little more than the paint.</summary>
        public static Rect MeshBox(Sprite sprite)
        {
            var size = sprite.rect.size;
            float left = float.MaxValue, right = float.MinValue, low = float.MaxValue, high = float.MinValue;
            foreach (var vertex in sprite.vertices)
            {
                var x = vertex.x * sprite.pixelsPerUnit + sprite.pivot.x;
                var y = vertex.y * sprite.pixelsPerUnit + sprite.pivot.y;
                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x);
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }
            if (left > right) return new Rect(0f, 0f, size.x, size.y);
            return Rect.MinMaxRect(left, size.y - high, right, size.y - low);
        }
    }
}
