using System;
using System.Collections.Generic;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// A picture fitted into a ring (Core/RingFit.cs): the smallest circle round what can be seen of it, and
    /// how large the picture is drawn for that circle to be the ring's inside.
    /// </summary>
    public static class RingFitTests
    {
        private static byte[] Picture(int width, int height, Func<int, int, bool> seen)
        {
            var alpha = new byte[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    alpha[y * width + x] = seen(x, y) ? (byte)255 : (byte)0;
            return alpha;
        }

        // every corner of every seen pixel lies in the circle, and the circle could not be smaller by much
        private static void HoldsAll(byte[] alpha, int width, int height, RingFit.Circle circle, string what)
        {
            var furthest = 0.0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (alpha[y * width + x] == 0) continue;
                    for (var corner = 0; corner < 4; corner++)
                    {
                        var dx = x + (corner & 1) - circle.X;
                        var dy = y + (corner >> 1) - circle.Y;
                        furthest = Math.Max(furthest, Math.Sqrt(dx * dx + dy * dy));
                    }
                }
            Check.True(furthest <= circle.Radius + 1e-6, what + ": a pixel lies outside the circle (" + furthest.ToString("0.###") + " of " + circle.Radius.ToString("0.###") + ")");
            Check.True(furthest >= circle.Radius - 1e-6, what + ": the circle touches nothing");
        }

        public static void ASquareInAWideMarginIsFoundWhereItStands()
        {
            // 10 by 10, its corner at (30, 12) of a picture 64 by 48
            var alpha = Picture(64, 48, (x, y) => x >= 30 && x < 40 && y >= 12 && y < 22);
            Check.True(RingFit.Around(alpha, 64, 48, 1, out var circle), "something is seen");
            Check.Near(35, circle.X, 1e-6, "the middle, across");
            Check.Near(17, circle.Y, 1e-6, "the middle, down");
            Check.Near(5 * Math.Sqrt(2), circle.Radius, 1e-6, "half the square's diagonal");
            HoldsAll(alpha, 64, 48, circle, "the square");
        }

        public static void ADiscIsItsOwnCircle()
        {
            var alpha = Picture(101, 101, (x, y) => (x - 50) * (x - 50) + (y - 50) * (y - 50) <= 40 * 40);
            Check.True(RingFit.Around(alpha, 101, 101, 1, out var circle), "something is seen");
            Check.Near(50.5, circle.X, 0.01, "the middle, across");
            Check.Near(50.5, circle.Y, 0.01, "the middle, down");
            Check.Near(40.7, circle.Radius, 0.4, "the disc's radius and a pixel's half");
            HoldsAll(alpha, 101, 101, circle, "the disc");
        }

        public static void AShapeWithPointsIsHeldByItsPoints()
        {
            // a star of four thin arms: its rectangle is 61 by 61, its circle has a radius of half an arm's reach
            var alpha = Picture(80, 80, (x, y) => (Math.Abs(x - 40) <= 1 && Math.Abs(y - 40) <= 30) || (Math.Abs(y - 40) <= 1 && Math.Abs(x - 40) <= 30));
            Check.True(RingFit.Around(alpha, 80, 80, 1, out var circle), "something is seen");
            Check.Near(40.5, circle.X, 1e-6, "the middle, across");
            Check.Near(40.5, circle.Y, 1e-6, "the middle, down");
            Check.Near(Math.Sqrt(30.5 * 30.5 + 1.5 * 1.5), circle.Radius, 1e-6, "to an arm's far corner");
            HoldsAll(alpha, 80, 80, circle, "the star");
        }

        public static void AShapeOffItsMiddleIsCentredByItsCircle()
        {
            // a triangle in a corner of its picture: no circle about the picture's middle would do
            var alpha = Picture(60, 60, (x, y) => x >= 5 && y >= 5 && x + y <= 40);
            Check.True(RingFit.Around(alpha, 60, 60, 1, out var circle), "something is seen");
            HoldsAll(alpha, 60, 60, circle, "the triangle");
            Check.True(circle.X < 30 && circle.Y < 30, "the circle is where the triangle is");
        }

        public static void FaintPixelsDoNotCount()
        {
            // a solid square with a faint haze far round it
            var alpha = Picture(64, 64, (x, y) => x >= 28 && x < 36 && y >= 28 && y < 36);
            for (var i = 0; i < alpha.Length; i++)
                if (alpha[i] == 0) alpha[i] = 20;
            Check.True(RingFit.Around(alpha, 64, 64, 64, out var solid), "the square is seen");
            Check.Near(4 * Math.Sqrt(2), solid.Radius, 1e-6, "the square alone");
            Check.True(RingFit.Around(alpha, 64, 64, 10, out var all), "with the haze");
            Check.Near(32 * Math.Sqrt(2), all.Radius, 1e-6, "the whole picture");
        }

        public static void NothingSeenNoCircle()
        {
            Check.True(!RingFit.Around(new byte[16], 4, 4, 1, out _), "a clear picture");
            Check.True(!RingFit.Around(null, 4, 4, 1, out _), "no picture");
            Check.True(!RingFit.Around(new byte[3], 4, 4, 1, out _), "too few pixels for its size");
        }

        public static void OnePixelIsACircleRoundItsSquare()
        {
            var alpha = Picture(9, 9, (x, y) => x == 2 && y == 6);
            Check.True(RingFit.Around(alpha, 9, 9, 1, out var circle), "a pixel is seen");
            Check.Near(2.5, circle.X, 1e-9, "across");
            Check.Near(6.5, circle.Y, 1e-9, "down");
            Check.Near(Math.Sqrt(0.5), circle.Radius, 1e-9, "half its diagonal");
        }

        public static void ThePictureIsDrawnSoThatItsCircleIsTheRingsInside()
        {
            // a square of 20 in the lower left of a picture 100 by 50: drawn so that its circle has a radius of 23
            var alpha = Picture(100, 50, (x, y) => x >= 10 && x < 30 && y >= 5 && y < 25);
            RingFit.Around(alpha, 100, 50, 1, out var circle);
            var scale = RingFit.Scale(circle, 23, 100, 50, out var dx, out var dy);
            Check.Near(23 / (10 * Math.Sqrt(2)), scale, 1e-6, "the scale");
            // the picture's middle is at (50, 25), the square's at (20, 15): the picture is shifted by the difference
            Check.Near(30 * scale, dx, 1e-6, "the picture's middle, across from the ring's");
            Check.Near(10 * scale, dy, 1e-6, "the picture's middle, down from the ring's");
        }

        public static void TheSmallestCircleOfPointsInALineIsOnTheTwoFurthest()
        {
            var circle = RingFit.Smallest(new List<double[]> { new double[] { 0, 0 }, new double[] { 4, 0 }, new double[] { 10, 0 }, new double[] { 7, 0 } });
            Check.Near(5, circle.X, 1e-9, "the middle");
            Check.Near(5, circle.Radius, 1e-9, "half the length");
        }
    }
}
