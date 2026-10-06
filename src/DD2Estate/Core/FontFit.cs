using System;

namespace DD2Estate.Core
{
    /// <summary>
    /// How another font face is set so that it takes the place of one of DD1's bitmap fonts without moving
    /// anything: at the same size its capitals are as tall, its baseline lies as far under the top of the line
    /// and its line is as tall as the DD1 font's, so a label keeps its size, its box and the place of its text.
    ///
    /// A DD1 font is measured in pixels of DD1's 1920x1080 screen (AngelCode: lineHeight, base, the height of
    /// its capital H). A face is measured in its own units at its point size (TextMeshPro: glyph metrics are
    /// pixels of the atlas the face was sampled into).
    /// </summary>
    public static class FontFit
    {
        /// <summary>Capitals and line of DD2's heading face (NDDunkel) over its point size: what a size means in the mod.</summary>
        public const float CapPerEm = 0.705f;
        public const float LinePerEm = 1.2f;

        /// <summary>A face's measures once it is fitted: its scale and its three lines, in the face's own units.</summary>
        public struct Face
        {
            /// <summary>What the face's own scale becomes (TextMeshPro: FaceInfo.scale).</summary>
            public float Scale;
            public float AscentLine, DescentLine, LineHeight;
            /// <summary>Pixels of DD1's screen per unit of the face at the DD1 font's native size.</summary>
            public float PixelsPerUnit;
        }

        /// <summary>
        /// The size at which a DD1 font is drawn pixel for pixel: at it neither the capitals nor the line are
        /// taller than those of DD2's heading face at that size, so a box made for DD2's font holds DD1's.
        /// <paramref name="capHeight"/> is the capital H as stored (with its outline, where the font has one).
        /// </summary>
        public static float NativeSize(float capHeight, float lineHeight)
        {
            return (float)Math.Max(Math.Round(capHeight / CapPerEm), Math.Ceiling(lineHeight / LinePerEm));
        }

        /// <summary>
        /// Fits a face to a DD1 font. <paramref name="cap"/> is the height of DD1's capitals without their
        /// outline, <paramref name="faceCap"/> that of the face's at <paramref name="pointSize"/>.
        /// <paramref name="capShare"/> below 1 draws the face's capitals that much lower than DD1's; they stay
        /// about the middle of DD1's capitals, so a text set small still sits where DD1's sat.
        /// </summary>
        public static Face Fit(float nativeSize, float lineHeight, float baseLine, float cap, float pointSize, float faceCap, float capShare = 1f)
        {
            if (nativeSize <= 0f || cap <= 0f || pointSize <= 0f || faceCap <= 0f) throw new ArgumentException("a font's measures must be above 0");
            if (capShare <= 0f) capShare = 1f;
            var pixels = cap * capShare / faceCap;
            // the baseline rises by half of what the capitals lost
            var ascent = baseLine - cap * (1f - capShare) * 0.5f;
            return new Face
            {
                PixelsPerUnit = pixels,
                Scale = pixels * pointSize / nativeSize,
                AscentLine = ascent / pixels,
                DescentLine = (ascent - lineHeight) / pixels,
                LineHeight = lineHeight / pixels
            };
        }

        /// <summary>
        /// The share of a distance field a material may use: where the face is let out to the very end of the
        /// field, what little the atlas holds at a glyph's border (measured in DD2's heading atlas: 0.035 of 1)
        /// shows as a haze over the glyph's whole box.
        /// </summary>
        public const float FieldUse = 0.85f;

        /// <summary>
        /// How much face dilate (TextMeshPro's material property) lets the face of a distance field font out by
        /// <paramref name="pixels"/> on every side; below 0 it is taken in. The field runs from 0 to 1 over twice
        /// <paramref name="gradientScale"/> units of the atlas (measured in DD2's atlases: 1/24 a unit at 12,
        /// 1/20 at 10), the glyph's edge in its middle; the shader moves the edge by half the dilate times a
        /// ratio that keeps one unit of the gradient back (ShaderUtilities.UpdateShaderRatios): a dilate of 1
        /// is gradientScale - 1 units. <paramref name="room"/> is the dilate the material has left
        /// (<see cref="FieldUse"/> less its weight and the dilate it has); more is not given.
        /// <paramref name="reached"/>: the pixels the answer gives.
        /// </summary>
        public static float Dilate(float pixels, float pixelsPerUnit, float gradientScale, float room, out float reached)
        {
            reached = 0f;
            if (pixels == 0f || pixelsPerUnit <= 0f || gradientScale <= 1f) return 0f;
            var perDilate = (gradientScale - 1f) * pixelsPerUnit;
            var dilate = pixels / perDilate;
            if (dilate > room) dilate = Math.Max(0f, room);
            if (dilate < -1f) dilate = -1f;
            reached = dilate * perDilate;
            return dilate;
        }
    }
}
