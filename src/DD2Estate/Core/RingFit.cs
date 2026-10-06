using System;
using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>
    /// A picture fitted into a ring: the smallest circle that holds everything of the picture that can be
    /// seen, so that the picture can be drawn as large as the ring's inside allows and no larger. A picture's
    /// own rectangle says little about that: the game's pictures stand in rectangles with clear room round
    /// them, and a shape with points fills its rectangle's corners not at all.
    /// </summary>
    public static class RingFit
    {
        public struct Circle
        {
            public double X, Y, Radius;

            public bool Holds(double x, double y, double slack = 1e-7)
            {
                var dx = x - X;
                var dy = y - Y;
                return dx * dx + dy * dy <= (Radius + slack) * (Radius + slack);
            }
        }

        /// <summary>
        /// The smallest circle round every pixel of a picture that is seen at least <paramref name="visible"/>
        /// well, each pixel taken as the whole square it is. <paramref name="alpha"/> says how well each pixel is
        /// seen (its alpha; or, for a picture that stands on black, its alpha times its light), row after row,
        /// <paramref name="width"/> to a row; the circle is in the picture's own pixels, (0,0) the outer corner
        /// of the first pixel of the first row. False when nothing of the picture can be seen.
        /// </summary>
        public static bool Around(byte[] alpha, int width, int height, byte visible, out Circle circle)
        {
            circle = default;
            if (alpha == null || width <= 0 || height <= 0 || alpha.Length < width * height) return false;
            // Only the first and the last seen pixel of a row can lie on the outline of all of them.
            var corners = new List<double[]>();
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                var first = -1;
                var last = -1;
                for (var x = 0; x < width; x++)
                {
                    if (alpha[row + x] < visible) continue;
                    if (first < 0) first = x;
                    last = x;
                }
                if (first < 0) continue;
                corners.Add(new double[] { first, y });
                corners.Add(new double[] { first, y + 1 });
                corners.Add(new double[] { last + 1, y });
                corners.Add(new double[] { last + 1, y + 1 });
            }
            if (corners.Count == 0) return false;
            circle = Smallest(Hull(corners));
            return true;
        }

        /// <summary>
        /// How much larger a picture may be drawn so that its circle is the ring's inside: the scale, and where
        /// the picture's rectangle (width by height, in its own pixels) has its middle then, counted from the
        /// ring's middle in the units of <paramref name="inside"/> (x to the right, y in the direction of the
        /// rows).
        /// </summary>
        public static double Scale(Circle circle, double inside, int width, int height, out double offsetX, out double offsetY)
        {
            var scale = circle.Radius > 1e-9 ? inside / circle.Radius : 1.0;
            offsetX = (width * 0.5 - circle.X) * scale;
            offsetY = (height * 0.5 - circle.Y) * scale;
            return scale;
        }

        // ---- the smallest circle round a set of points ------------------------------------------------------

        private static double Cross(double[] o, double[] a, double[] b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

        // The corners of the outline (Andrew's chain): the circle round them is the circle round all.
        private static List<double[]> Hull(List<double[]> points)
        {
            points.Sort((a, b) => a[0] != b[0] ? a[0].CompareTo(b[0]) : a[1].CompareTo(b[1]));
            if (points.Count < 3) return points;
            var hull = new List<double[]>();
            for (var pass = 0; pass < 2; pass++)
            {
                var start = hull.Count;
                for (var n = 0; n < points.Count; n++)
                {
                    var p = pass == 0 ? points[n] : points[points.Count - 1 - n];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        /// <summary>The smallest circle round points (each a pair x, y): the one of two or three of them that holds the rest.</summary>
        public static Circle Smallest(IList<double[]> points)
        {
            var circle = new Circle { X = 0, Y = 0, Radius = -1 };
            for (var i = 0; i < points.Count; i++)
            {
                if (circle.Radius >= 0 && circle.Holds(points[i][0], points[i][1])) continue;
                circle = new Circle { X = points[i][0], Y = points[i][1], Radius = 0 };
                for (var j = 0; j < i; j++)
                {
                    if (circle.Holds(points[j][0], points[j][1])) continue;
                    circle = Of(points[i], points[j]);
                    for (var k = 0; k < j; k++)
                    {
                        if (circle.Holds(points[k][0], points[k][1])) continue;
                        circle = Of(points[i], points[j], points[k]);
                    }
                }
            }
            if (circle.Radius < 0) circle.Radius = 0;
            return circle;
        }

        private static Circle Of(double[] a, double[] b)
        {
            var x = (a[0] + b[0]) * 0.5;
            var y = (a[1] + b[1]) * 0.5;
            return new Circle { X = x, Y = y, Radius = Math.Sqrt((a[0] - x) * (a[0] - x) + (a[1] - y) * (a[1] - y)) };
        }

        private static Circle Of(double[] a, double[] b, double[] c)
        {
            var d = 2 * (a[0] * (b[1] - c[1]) + b[0] * (c[1] - a[1]) + c[0] * (a[1] - b[1]));
            if (Math.Abs(d) < 1e-12)
            {
                // three in a line: the two furthest apart
                var ab = Of(a, b);
                var ac = Of(a, c);
                var bc = Of(b, c);
                return ab.Radius >= ac.Radius && ab.Radius >= bc.Radius ? ab : ac.Radius >= bc.Radius ? ac : bc;
            }
            var aa = a[0] * a[0] + a[1] * a[1];
            var bb = b[0] * b[0] + b[1] * b[1];
            var cc = c[0] * c[0] + c[1] * c[1];
            var x = (aa * (b[1] - c[1]) + bb * (c[1] - a[1]) + cc * (a[1] - b[1])) / d;
            var y = (aa * (c[0] - b[0]) + bb * (a[0] - c[0]) + cc * (b[0] - a[0])) / d;
            return new Circle { X = x, Y = y, Radius = Math.Sqrt((a[0] - x) * (a[0] - x) + (a[1] - y) * (a[1] - y)) };
        }
    }
}
