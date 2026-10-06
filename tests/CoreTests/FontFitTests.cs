using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// Another face in place of a DD1 bitmap font (Core/FontFit.cs): at the DD1 font's native size the fitted face
    /// has DD1's capitals, baseline and line, whatever the face's own measures are.
    /// </summary>
    public static class FontFitTests
    {
        // DD1's five fonts as their .fnt files give them: line, base, capital H (with the outline of popup).
        private static readonly float[][] Dd1 =
        {
            new[] { 25f, 21f, 16f }, new[] { 28f, 23f, 17f }, new[] { 40f, 30f, 22f }, new[] { 63f, 48f, 34f }, new[] { 74f, 57f, 48f }
        };

        public static void NativeSizesAreDd1s()
        {
            var sizes = new[] { 23f, 24f, 34f, 53f, 68f };
            for (var i = 0; i < Dd1.Length; i++)
                Check.Equal(sizes[i], FontFit.NativeSize(Dd1[i][2], Dd1[i][0]), "native size of font " + i);
        }

        public static void FittedFaceHasDd1sLines()
        {
            // a face sampled at 88 with capitals of 62 (DD2's heading face) and one at 60 with capitals of 38
            var faces = new[] { new[] { 88f, 62f }, new[] { 60f, 38f } };
            foreach (var dd1 in Dd1)
            {
                foreach (var face in faces)
                {
                    float line = dd1[0], baseLine = dd1[1], cap = dd1[2];
                    var size = FontFit.NativeSize(cap, line);
                    var fit = FontFit.Fit(size, line, baseLine, cap, face[0], face[1]);
                    // TextMeshPro draws a unit of the face as size / pointSize * scale pixels
                    var pixels = size / face[0] * fit.Scale;
                    Check.Near(fit.PixelsPerUnit, pixels, 0.0001, "pixels per unit");
                    Check.Near(cap, face[1] * pixels, 0.001, "capitals");
                    Check.Near(baseLine, fit.AscentLine * pixels, 0.001, "baseline under the top of the line");
                    Check.Near(baseLine - line, fit.DescentLine * pixels, 0.001, "foot of the line");
                    Check.Near(line, fit.LineHeight * pixels, 0.001, "line");
                    Check.Near(fit.LineHeight, fit.AscentLine - fit.DescentLine, 0.001, "no gap between two lines beyond the line itself");
                }
            }
        }

        public static void SmallerCapitalsStayAboutTheMiddle()
        {
            float line = 40f, baseLine = 30f, cap = 22f;
            var size = FontFit.NativeSize(cap, line);
            var fit = FontFit.Fit(size, line, baseLine, cap, 88f, 62f, 0.9f);
            var pixels = size / 88f * fit.Scale;
            var capitals = 62f * pixels;
            Check.Near(cap * 0.9f, capitals, 0.001, "capitals nine tenths of DD1's");
            var baseline = fit.AscentLine * pixels;
            // DD1's capitals stand from 8 to 30 under the top of the line: their middle is at 19
            Check.Near(19.0, baseline - capitals * 0.5, 0.001, "the middle of the capitals");
            Check.Near(line, fit.LineHeight * pixels, 0.001, "the line stays DD1's");
            Check.Near(line, (fit.AscentLine - fit.DescentLine) * pixels, 0.001, "ascent to descent stays DD1's line");
        }

        public static void DilateForAnOutline()
        {
            // a gradient of 10 units, the face drawn at 0.645 px a unit: a dilate of 1 is nine units, 5.8 px
            var dilate = FontFit.Dilate(2f, 0.645f, 10f, 1f, out var reached);
            Check.Near(2.0 / 5.805, dilate, 0.001, "two pixels");
            Check.Near(2.0, reached, 0.001, "two pixels reached");
            dilate = FontFit.Dilate(8f, 0.645f, 10f, 1f, out reached);
            Check.Near(1.0, dilate, 0.0001, "no more than the room");
            Check.Near(5.805, reached, 0.001, "what the room allows");
            Check.Near(0.0, FontFit.Dilate(0f, 0.645f, 10f, 1f, out reached), 0.0001, "no outline, no dilate");
            // DD2's heading face as its plain preset has it: weight 1 (a quarter) and a dilate of -0.284; its gradient
            // is 12 units, and DD1's pop-up font wants an outline of 4 px at 0.6494 px a unit
            var room = FontFit.FieldUse - 0.25f + 0.284f;
            dilate = FontFit.Dilate(4f, 0.6494f, 12f, room, out reached);
            Check.Near(0.56, dilate, 0.001, "four pixels of the heading face");
            Check.Near(4.0, reached, 0.001, "all four pixels");
            Check.True(dilate < room, "well inside the field");
            dilate = FontFit.Dilate(9f, 0.6494f, 12f, room, out reached);
            Check.Near(room, dilate, 0.0001, "what the material has left");
            Check.Near(room * 11.0 * 0.6494, reached, 0.001, "6.3 of the 9 pixels");
            // thinner: a negative dilate
            dilate = FontFit.Dilate(-0.5f, 0.645f, 10f, 1f, out reached);
            Check.Near(-0.5 / 5.805, dilate, 0.001, "half a pixel in");
            Check.Near(-0.5, reached, 0.001, "half a pixel in, reached");
        }

        public static void BadMeasuresAreRefused()
        {
            var refused = false;
            try { FontFit.Fit(34f, 40f, 30f, 22f, 88f, 0f); }
            catch (System.ArgumentException) { refused = true; }
            Check.True(refused, "a face without capitals cannot be fitted");
        }
    }
}
