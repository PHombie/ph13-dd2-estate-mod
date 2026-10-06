using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DD2Estate.Dd1;

// Stand-ins for the few Unity types the skeleton reader touches: same names, same arithmetic.
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Color32
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color operator *(Color x, Color y) => new Color(x.r * y.r, x.g * y.g, x.b * y.b, x.a * y.a);

        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);

        public static float Cos(float f) => (float)Math.Cos(f);

        public static float Sin(float f) => (float)Math.Sin(f);

        public static float Max(float a, float b) => a > b ? a : b;

        public static float Repeat(float t, float length) => Math.Clamp(t - (float)Math.Floor(t / length) * length, 0f, length);

        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
    }
}

namespace DD2Estate
{
    public static class Plugin
    {
        public static readonly Logger Log = new Logger();

        public class Logger
        {
            public void LogWarning(string text) => Console.Error.WriteLine("WARN " + text);
        }
    }
}

namespace DD2Estate.Dd1
{
    internal static class Dd1Install
    {
        public static string Root;

        public static byte[] ReadBytes(string relative)
        {
            var path = Path.Combine(Root, relative);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
    }

    /// <summary>What the reader asks of an atlas: where a whitespace-stripped region sits in its original bounds.</summary>
    internal class SpineAtlas
    {
        public class Region
        {
            public int Width, Height, OriginalWidth, OriginalHeight, OffsetX, OffsetY;
        }

        private readonly Dictionary<string, Region> _regions = new Dictionary<string, Region>();

        public Region Find(string name) => _regions.TryGetValue(name, out var region) ? region : null;

        public static SpineAtlas Parse(string text)
        {
            var atlas = new SpineAtlas();
            Region region = null;
            var expectPage = true;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                {
                    expectPage = true;
                    region = null;
                    continue;
                }
                var colon = line.IndexOf(':');
                if (expectPage) expectPage = false;
                else if (colon < 0) atlas._regions[line] = region = new Region();
                else if (region != null)
                {
                    var values = line.Substring(colon + 1).Split(',').Select(v => v.Trim()).ToArray();
                    switch (line.Substring(0, colon).Trim())
                    {
                        case "size":
                            region.Width = int.Parse(values[0]);
                            region.Height = int.Parse(values[1]);
                            break;
                        case "orig":
                            region.OriginalWidth = int.Parse(values[0]);
                            region.OriginalHeight = int.Parse(values[1]);
                            break;
                        case "offset":
                            region.OffsetX = int.Parse(values[0]);
                            region.OffsetY = int.Parse(values[1]);
                            break;
                    }
                }
            }
            return atlas;
        }
    }
}

namespace DD2Estate.SpineCheck
{
    /// <summary>
    /// SpineCheck &lt;DD1 install&gt; &lt;output file&gt; &lt;folder&gt;...: every skeleton under the folders, its setup parts
    /// and its poses at the start, two moments between and the end of every animation, as text.
    /// </summary>
    internal static class Program
    {
        /// <summary>The moments of an animation that are written, as shares of its length (tools/check_spine.py reads them back).</summary>
        private static readonly float[] Moments = { 0f, 0.31f, 0.77f, 1f };

        private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        private static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: SpineCheck <DD1 install> <output file> <folder> [<folder>...]");
                return 2;
            }
            Dd1Install.Root = args[0];
            var text = new StringBuilder();
            var pieces = new List<SpineSkeleton.Piece>();
            int skeletons = 0, poses = 0, failed = 0;
            foreach (var folder in args.Skip(2))
                foreach (var file in Directory.GetFiles(Path.Combine(Dd1Install.Root, folder), "*.skel", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(Dd1Install.Root, file).Replace('\\', '/');
                    var skeleton = SpineSkeleton.Load(relative);
                    if (skeleton == null)
                    {
                        failed++;
                        text.Append("FAILED ").Append(relative).Append('\n');
                        continue;
                    }
                    skeletons++;
                    var atlasFile = Path.ChangeExtension(file, ".atlas");
                    var atlas = File.Exists(atlasFile) ? SpineAtlas.Parse(File.ReadAllText(atlasFile)) : null;
                    text.Append("SKEL ").Append(relative).Append(" parts ").Append(skeleton.Parts.Count).Append('\n');
                    foreach (var part in skeleton.Parts)
                        text.Append("PART ").Append(part.Slot).Append(' ').Append(part.Region).Append(' ').Append(F(part.X)).Append(' ').Append(F(part.Y)).Append(' ')
                            .Append(F(part.Width)).Append(' ').Append(F(part.Height)).Append(' ').Append(F(part.Rotation)).Append(' ').Append(F(part.Color.a)).Append('\n');
                    var names = new List<string> { null };
                    names.AddRange(skeleton.Animations.OrderBy(n => n, StringComparer.Ordinal));
                    foreach (var name in names)
                    {
                        var duration = skeleton.Duration(name);
                        foreach (var share in Moments)
                        {
                            if (share > 0f && duration <= 0f) continue;
                            skeleton.Pose(name, duration * share, atlas, pieces);
                            poses++;
                            text.Append("POSE ").Append(name ?? "-").Append(' ').Append(F(share)).Append(' ').Append(pieces.Count).Append('\n');
                            foreach (var piece in pieces)
                            {
                                text.Append(piece.Slot).Append(' ').Append(piece.Region).Append(' ').Append(piece.Triangles.Length / 3).Append(' ')
                                    .Append(F(piece.Color.r)).Append(' ').Append(F(piece.Color.g)).Append(' ').Append(F(piece.Color.b)).Append(' ').Append(F(piece.Color.a));
                                foreach (var v in piece.Vertices) text.Append(' ').Append(F(v.x)).Append(' ').Append(F(v.y));
                                text.Append('\n');
                            }
                        }
                    }
                }
            File.WriteAllText(args[1], text.ToString());
            Console.WriteLine(skeletons + " skeletons, " + poses + " poses, " + failed + " not readable -> " + args[1]);
            return failed;
        }
    }
}
