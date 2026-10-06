using UnityEngine;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// Truevision TGA as far as DD1's font pages need it (Unity's LoadImage reads PNG and JPG only): 8-bit grey,
    /// grey with alpha, 24- and 32-bit colour, raw or run-length packed, rows stored in either order.
    /// Colour-mapped files are not read.
    /// </summary>
    internal static class Tga
    {
        private const int HeaderSize = 18;

        /// <summary>
        /// Decodes to RGBA with the rows top to bottom. Grey pixels come back as (v, v, v, 255). The fourth
        /// byte of a 32-bit pixel is always taken as alpha: DD1's Ubuntu pages keep their glyphs there while
        /// their header declares no alpha bits. <paramref name="hasAlpha"/>: the file stores an alpha value per
        /// pixel. False if the data is not a TGA this reader knows.
        /// </summary>
        public static bool TryRead(byte[] data, out int width, out int height, out Color32[] pixels, out bool hasAlpha)
        {
            width = height = 0;
            pixels = null;
            hasAlpha = false;
            if (data == null || data.Length < HeaderSize) return false;

            int idLength = data[0];
            int colourMapType = data[1];
            int type = data[2];
            int mapLength = data[5] | (data[6] << 8);
            int mapEntryBits = data[7];
            width = data[12] | (data[13] << 8);
            height = data[14] | (data[15] << 8);
            int bytes = data[16] / 8;
            int descriptor = data[17];

            var packed = type == 10 || type == 11;
            var grey = type == 3 || type == 11;
            var colour = type == 2 || type == 10;
            if (!grey && !colour) return false;
            if (width <= 0 || height <= 0) return false;
            if (grey ? bytes != 1 && bytes != 2 : bytes != 3 && bytes != 4) return false;
            hasAlpha = grey ? bytes == 2 : bytes == 4;

            var at = HeaderSize + idLength + (colourMapType == 1 ? mapLength * ((mapEntryBits + 7) / 8) : 0);
            var count = width * height;
            var stored = new Color32[count];
            var done = 0;
            if (!packed)
            {
                if (at + count * bytes > data.Length) return false;
                for (; done < count; done++, at += bytes) stored[done] = Pixel(data, at, bytes, grey);
            }
            else
            {
                // Packets: a count byte, then one pixel repeated (high bit set) or that many pixels as they are.
                while (done < count)
                {
                    if (at >= data.Length) return false;
                    int head = data[at++];
                    var run = (head & 0x7F) + 1;
                    if (done + run > count) run = count - done;
                    if ((head & 0x80) != 0)
                    {
                        if (at + bytes > data.Length) return false;
                        var pixel = Pixel(data, at, bytes, grey);
                        at += bytes;
                        for (var i = 0; i < run; i++) stored[done++] = pixel;
                    }
                    else
                    {
                        if (at + run * bytes > data.Length) return false;
                        for (var i = 0; i < run; i++, at += bytes) stored[done++] = Pixel(data, at, bytes, grey);
                    }
                }
            }

            // Descriptor bit 5: rows run top to bottom; bit 4: pixels run right to left.
            var topDown = (descriptor & 0x20) != 0;
            var rightToLeft = (descriptor & 0x10) != 0;
            if (topDown && !rightToLeft)
            {
                pixels = stored;
                return true;
            }
            pixels = new Color32[count];
            for (var y = 0; y < height; y++)
            {
                var from = (topDown ? y : height - 1 - y) * width;
                var to = y * width;
                for (var x = 0; x < width; x++) pixels[to + x] = stored[from + (rightToLeft ? width - 1 - x : x)];
            }
            return true;
        }

        private static Color32 Pixel(byte[] data, int at, int bytes, bool grey)
        {
            if (grey) return new Color32(data[at], data[at], data[at], bytes == 2 ? data[at + 1] : (byte)255);
            return new Color32(data[at + 2], data[at + 1], data[at], bytes == 4 ? data[at + 3] : (byte)255);     // stored as BGR(A)
        }
    }
}
