using System;
using System.Collections;
using System.Collections.Generic;
using DD2Estate.Dd1;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A hallway or a room built the way DD1 builds it (docs/recon/dd1-corridor-rendering.md, section 4), in
    /// DD1's own units under a root scaled by <see cref="CorridorNumbers.UnitScale"/>:
    /// the wall art's upper 600 rows upright at the wall's depth and its lower 120 rows flat as the floor, a
    /// ground plane the party and the props stand on; end caps beyond the doors; the far background and the
    /// midground; the foreground strips between the camera and the party. Walls and floor are cut into narrow
    /// columns whose vertex colours carry DD1's light ramp (<see cref="CorridorLight"/>).
    /// </summary>
    internal class CorridorScene
    {
        // Tunables, live through the dev bridge (then corridor.rebuild).
        /// <summary>Width of a lit column in DD1 units: the ramp is linear between its stops, 384 apart in a hallway.</summary>
        public static float ColumnWidth = 45f;
        public static bool ShowFar = true, ShowMid = true, ShowEndCaps = true;
        /// <summary>
        /// How the foreground strips get in front of the heroes, whom the game's "Character Camera" (an overlay
        /// camera in the main camera's stack) draws after everything the main camera draws.
        /// 0: no strips. 1: a further overlay camera of the mod's own, last in that stack, that draws only the
        /// strips' layer. 2: the strips on the heroes' own layer with a high sorting order, drawn by the game's
        /// character camera after the models.
        /// </summary>
        public static int ForegroundMode = 1;
        /// <summary>A layer neither the main camera nor the character camera draws (mode 1).</summary>
        public static int ForegroundLayer = 27;
        /// <summary>0..1: DD1 fades the strips out for fights and while a curio is looked at.</summary>
        public static float ForegroundAlpha = 1f;
        /// <summary>
        /// A test switch, off: the strips shown while the party walks only. DD1's code has nothing of the kind: out
        /// of a fight the strips' alpha follows camera.foreground_active alone [exe 0xab7540]. It is here because
        /// the real frames of 2026-10-05 (a party standing in the tutorial's hallway) show no strips at all, which
        /// that code does not explain; see MODLOG.
        /// </summary>
        public static bool ForegroundWhileWalking = false;

        public const int BackdropOrder = -1000, FarOrder = -400, MidOrder = -390, WallOrder = -300, ForegroundOrder = 5000;

        // The share of the wall art that is floor: its lower 120 of 720 rows (DD1 passes 600/720 as the split).
        private const float FloorShare = 120f / 720f;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        /// <summary>A mesh whose vertices stand in columns of one x each and take the ramp's colour there.</summary>
        private class Lit
        {
            public string Name;
            public GameObject Object;
            public Mesh Mesh;
            public float[] ColumnX;
            public int PerColumn;
            public readonly List<Color> Colours = new List<Color>();
            public float MinX, MaxX, Z;
            public int Order;
            /// <summary>On the light's multiplier (as DD1 counts it) at the mesh's left and right edge: 1 for a wall tile.</summary>
            public float GainLeft = 1f, GainRight = 1f;
        }

        /// <summary>A far layer: the picture at the view's own size, repeated, its texture moved with the camera.</summary>
        private class Flat
        {
            public string Name;
            public GameObject Object;
            public Mesh Mesh;
            public float Z, Follow;
            public int Columns, Order;
            public float TexturePixels;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Color> Colours = new List<Color>();
        }

        private readonly List<Lit> _lit = new List<Lit>();
        private readonly List<Flat> _flat = new List<Flat>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<MeshRenderer> _foreground = new List<MeshRenderer>();
        private readonly List<Mesh> _foregroundMeshes = new List<Mesh>();
        private float _foregroundAlpha = -1f;
        private int _foregroundMode = -1;
        private Transform _backdrop;
        private Vector4 _lastLight = new Vector4(float.NaN, 0f, 0f, 0f);
        private Color _lastBase, _lastEdge;

        /// <summary>Everything of the scene hangs here; one of its units is one DD1 unit.</summary>
        public Transform Root { get; private set; }
        public bool IsRoom { get; private set; }
        public int Tiles { get; private set; }
        /// <summary>A room's wall as drawn: its left edge and its width, DD1 units.</summary>
        public float RoomLeft { get; private set; }
        public float RoomWidth { get; private set; }

        // ---- building ------------------------------------------------------------------------------------

        public void Build(Transform parent, Func<string, string> artFile, IList<string> tiles, bool isRoom)
        {
            Destroy();
            CorridorNumbers.Load();
            var n = CorridorNumbers.TileWidth;
            Root = new GameObject("Scene").transform;
            Root.SetParent(parent, false);
            Root.localScale = Vector3.one * CorridorNumbers.UnitScale;
            IsRoom = isRoom;
            Tiles = tiles.Count;

            // The engine clears to a fog gradient when no scene supplies a sky: black far behind everything.
            var backdrop = Quad("Backdrop", Texture2D.whiteTexture, BackdropOrder, 0, new Vector3(-1e6f, -1e5f, CorridorNumbers.FarZ + 600f),
                new Vector3(1e6f, 1e5f, CorridorNumbers.FarZ + 600f), Color.black);
            _backdrop = backdrop.transform;
            if (tiles.Count == 0) return;

            if (isRoom)
            {
                var tex = Dd1Install.Texture(artFile(tiles[0]));
                if (tex != null)
                {
                    // DD1 draws a room's picture as wide as its camera sees at the wall's depth: one pixel of art to one of the view
                    RoomWidth = CorridorNumbers.RoomWallWidth;
                    RoomLeft = CorridorNumbers.RoomCamera.x - RoomWidth * 0.5f;
                    // (a picture of the mod's own in DD1's place may be larger: DD1's pixels are what is counted)
                    Wall("room", tex, RoomLeft, RoomWidth, CorridorNumbers.WallTop * RoomWidth / (tex.width / ArtScale(tex)), false);
                }
                return;
            }

            if (ShowFar) Far("far background", artFile("corridor_bg"), CorridorNumbers.FarZ, CorridorNumbers.FarFollow, FarOrder);
            if (ShowMid)
            {
                // DD1's town has no plain midground, only numbered ones (town.corridor_mid.01..03): one of them
                // by the hallway's own tiles, so that a hallway keeps its own. GUESS: how DD1 picks among them.
                var mid = artFile("corridor_mid");
                if (!Dd1Install.Exists(Dd1Install.ArtPath(mid)))
                {
                    var numbered = new List<string>();
                    for (var i = 1; i <= 9; i++)
                    {
                        var file = artFile("corridor_mid.0" + i);
                        if (!Dd1Install.Exists(Dd1Install.ArtPath(file))) break;
                        numbered.Add(file);
                    }
                    if (numbered.Count > 0)
                    {
                        var pick = 0;
                        foreach (var tile in tiles) pick = unchecked(pick * 31 + (string.IsNullOrEmpty(tile) ? 0 : tile.Length + tile[tile.Length - 1]));
                        mid = numbered[(pick & 0x7fffffff) % numbered.Count];
                    }
                }
                Far("midground", mid, CorridorNumbers.MidZ, CorridorNumbers.MidFollow, MidOrder);
            }
            for (var i = 0; i < tiles.Count; i++)
            {
                var tex = Dd1Install.Texture(artFile(tiles[i]));
                if (tex != null) Wall("tile " + i + " " + tiles[i], tex, i * n - n * 0.5f, n, CorridorNumbers.WallTop, false);
            }
            // Beyond each door tile DD1 draws one more wall and floor, with the picture of the hallway's tile next to
            // that door; the far one mirrored [exe 0xabe91d, 0xabf06d; seen in the running game].
            if (ExtendEnds && tiles.Count >= 3)
            {
                var left = Dd1Install.Texture(artFile(tiles[1]));
                var right = Dd1Install.Texture(artFile(tiles[tiles.Count - 2]));
                if (left != null) Wall("beyond the first door", left, -n * 1.5f, n, CorridorNumbers.WallTop, false);
                if (right != null) Wall("beyond the last door", right, tiles.Count * n - n * 0.5f, n, CorridorNumbers.WallTop, true);
            }
            // DD1's end of a hallway [exe 0xab3850, 0xab39d0; seen in the running game]: the endhall picture stands
            // in front of the wall, at the curios' depth, a tile wide from the middle of the door tile outwards and
            // 80 below the floor line upwards. The left one is the mirror image.
            var cap = artFile("endhall.01");
            if (ShowEndCaps && Dd1Install.Exists(Dd1Install.ArtPath(cap)))
            {
                var tex = Dd1Install.Texture(cap);
                if (tex != null)
                {
                    if (ExtendEnds)
                    {
                        Cap("end cap left", tex, -n, n, true);
                        Cap("end cap right", tex, (tiles.Count - 1) * n, n, false);
                        // The cap is nearer than the wall, so past its outer edge the wall behind it shows again,
                        // cut off sharply, whenever the camera goes further out than where it rests (it goes to the
                        // door as the party walks into it). Black from the cap's edge outwards, as far as is seen.
                        Quad("end curtain left", Texture2D.whiteTexture, EndCapOrder, 0, new Vector3(-n - EndCurtain, -EndCapDrop, CorridorNumbers.CurioZ),
                            new Vector3(-n, EndCurtain, CorridorNumbers.CurioZ), Color.black);
                        Quad("end curtain right", Texture2D.whiteTexture, EndCapOrder, 0, new Vector3(tiles.Count * n, -EndCapDrop, CorridorNumbers.CurioZ),
                            new Vector3(tiles.Count * n + EndCurtain, EndCurtain, CorridorNumbers.CurioZ), Color.black);
                    }
                    else
                    {
                        Wall("end cap left", tex, -n * 1.5f, n, CorridorNumbers.WallTop, false, EndCapGain, EndCapGain);
                        Wall("end cap right", tex, tiles.Count * n - n * 0.5f, n, CorridorNumbers.WallTop, true, EndCapGain, EndCapGain);
                    }
                }
            }
            Foreground(artFile, tiles.Count);
        }

        /// <summary>
        /// The ends of a hallway as DD1 draws them. Read from the executable (0xabe140: the extra wall and floor
        /// beyond each door tile; 0xab3850 / 0xab39d0: the endhall picture) and seen in the running game
        /// (2026-10-05, tools/dd1_drive.py and tools/dd1_match.py on the tutorial's Weald hallway; a rebuilt frame
        /// with the picture in that place matches the real one at 0.67 against 0.42 without it,
        /// _lab/shots/dd1_end_cap_test.png):
        /// <list type="bullet">
        /// <item>beyond each door tile one more wall and floor, with the picture of the tile next to that door, the
        /// far one mirrored;</item>
        /// <item>the endhall picture in front of the wall at the curios' depth (z 75), a tile wide from the door
        /// tile's middle outwards, from 80 below the floor line up; the left one mirrored. It is an overlay with a
        /// pillar or a tree of its own that stands over the door tile's outer half, not a tile beside it.</item>
        /// </list>
        /// Off: the mod's first reading, the endhall picture as a wall tile beside each door tile (it read as "the
        /// wrong texture": its bricks never met the door tile's).
        /// </summary>
        public static bool ExtendEnds = true;

        /// <summary>How far below the floor line the end cap's picture begins [exe: y from -80].</summary>
        public static float EndCapDrop = 80f;
        /// <summary>How far the black past an end cap reaches, outwards and up (DD1 units).</summary>
        public const float EndCurtain = 4000f;

        public const int EndCapOrder = WallOrder + 10;

        /// <summary>On the end caps' light (as DD1 counts it); 1: DD1's art as it is.</summary>
        public static float EndCapGain = 1f;

        /// <summary>
        /// The mod's own. DD1's foreground strips carry hair-thin pale strokes in their black (the outlines of the
        /// painter's cut-out shapes, 16..32 of 255 on 0): over a dark dungeon they read as a white fringe round
        /// nothing (the user's report of 2026-10-05). On: such strokes are taken out of the strips (see
        /// <see cref="Cleaned"/>); the painted bricks and chains stay.
        /// </summary>
        public static bool CleanForeground = true;

        /// <summary>Pixels of a wall picture's edge left out (see <see cref="Wall"/>); 0: the whole picture.</summary>
        public static float EdgeInset = 1.5f;

        private static Material Material => PropModel.SpriteMaterial();

        private GameObject NewMesh(string name, Texture texture, int order, int layer, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.layer = layer;
            mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            _meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortingOrder = order;
            renderer.GetPropertyBlock(Block);
            Block.SetTexture(MainTex, texture);
            renderer.SetPropertyBlock(Block);
            return go;
        }

        private GameObject Quad(string name, Texture texture, int order, int layer, Vector3 bottomLeft, Vector3 topRight, Color colour)
        {
            var go = NewMesh(name, texture, order, layer, out var mesh);
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(bottomLeft.x, bottomLeft.y, bottomLeft.z), new Vector3(bottomLeft.x, topRight.y, topRight.z),
                new Vector3(topRight.x, topRight.y, topRight.z), new Vector3(topRight.x, bottomLeft.y, bottomLeft.z)
            });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            mesh.SetColors(new List<Color> { colour, colour, colour, colour });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return go;
        }

        /// <summary>
        /// One wall picture as DD1 draws it: its rows above the floor line upright at the wall's depth from the
        /// floor to <paramref name="top"/>, its last 120 rows flat on the floor from the wall towards the camera.
        /// </summary>
        private void Wall(string name, Texture texture, float left, float width, float top, bool mirrored, float gainLeft = 1f, float gainRight = 1f)
        {
            var columns = Mathf.Max(1, Mathf.CeilToInt(width / Mathf.Max(8f, ColumnWidth)));
            var go = NewMesh(name, texture, WallOrder, 0, out var mesh);
            var lit = new Lit { Name = name, Object = go, Mesh = mesh, ColumnX = new float[columns + 1], PerColumn = 3, MinX = left, MaxX = left + width, Z = CorridorNumbers.WallZ, Order = WallOrder, GainLeft = gainLeft, GainRight = gainRight };
            var vertices = new List<Vector3>((columns + 1) * 3);
            var uvs = new List<Vector2>((columns + 1) * 3);
            var triangles = new List<int>(columns * 12);
            // DD1's wall pictures have an outermost ring of pixels that is a little clear (alpha 242 on average
            // where the next ring is solid): two tiles side by side show the layer behind them as a thin line.
            // The picture is drawn from the middle of its second pixel to the middle of its last but one.
            var insetU = EdgeInset * ArtScale(texture) / Mathf.Max(1, texture.width);
            var insetV = EdgeInset * ArtScale(texture) / Mathf.Max(1, texture.height);
            for (var c = 0; c <= columns; c++)
            {
                var share = (float)c / columns;
                var x = left + width * share;
                var u = Mathf.Lerp(insetU, 1f - insetU, mirrored ? 1f - share : share);
                lit.ColumnX[c] = x;
                vertices.Add(new Vector3(x, top, CorridorNumbers.WallZ));
                vertices.Add(new Vector3(x, 0f, CorridorNumbers.WallZ));
                vertices.Add(new Vector3(x, 0f, CorridorNumbers.FloorNearZ));
                uvs.Add(new Vector2(u, 1f - insetV));
                uvs.Add(new Vector2(u, FloorShare));
                uvs.Add(new Vector2(u, insetV));
                for (var k = 0; k < 3; k++) lit.Colours.Add(Color.white);
                if (c == columns) break;
                var a = c * 3;
                triangles.Add(a); triangles.Add(a + 3); triangles.Add(a + 4);
                triangles.Add(a); triangles.Add(a + 4); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(a + 4); triangles.Add(a + 5);
                triangles.Add(a + 1); triangles.Add(a + 5); triangles.Add(a + 2);
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(lit.Colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            _lit.Add(lit);
        }

        /// <summary>DD1's end cap: an upright picture at the curios' depth, in columns that take the light's ramp.</summary>
        private void Cap(string name, Texture texture, float left, float width, bool mirrored)
        {
            var columns = Mathf.Max(1, Mathf.CeilToInt(width / Mathf.Max(8f, ColumnWidth)));
            var go = NewMesh(name, texture, EndCapOrder, 0, out var mesh);
            var lit = new Lit { Name = name, Object = go, Mesh = mesh, ColumnX = new float[columns + 1], PerColumn = 2, MinX = left, MaxX = left + width, Z = CorridorNumbers.CurioZ, Order = EndCapOrder, GainLeft = EndCapGain, GainRight = EndCapGain };
            var height = width * texture.height / Mathf.Max(1, texture.width);
            var vertices = new List<Vector3>((columns + 1) * 2);
            var uvs = new List<Vector2>((columns + 1) * 2);
            var triangles = new List<int>(columns * 6);
            for (var c = 0; c <= columns; c++)
            {
                var share = (float)c / columns;
                var x = left + width * share;
                lit.ColumnX[c] = x;
                vertices.Add(new Vector3(x, -EndCapDrop, CorridorNumbers.CurioZ));
                vertices.Add(new Vector3(x, height - EndCapDrop, CorridorNumbers.CurioZ));
                var u = mirrored ? 1f - share : share;
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
                lit.Colours.Add(Color.white);
                lit.Colours.Add(Color.white);
                if (c == columns) break;
                var a = c * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 3);
                triangles.Add(a); triangles.Add(a + 3); triangles.Add(a + 2);
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(lit.Colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            _lit.Add(lit);
        }

        // A picture read through Dd1Install is named by its path there: how many of its pixels stand for one of DD1's.
        private static float ArtScale(Texture texture) => texture != null ? Mathf.Max(0.01f, Dd1Install.Scale(texture.name)) : 1f;

        private void Far(string name, string file, float z, float follow, int order)
        {
            var texture = Dd1Install.Texture(file);
            if (texture == null) return;
            // the picture repeats along the hallway
            texture.wrapMode = TextureWrapMode.Repeat;
            var go = NewMesh(name, texture, order, 0, out var mesh);
            var flat = new Flat { Name = name, Object = go, Mesh = mesh, Z = z, Follow = follow, Columns = 48, Order = order, TexturePixels = texture.width / ArtScale(texture) };
            var triangles = new List<int>(flat.Columns * 6);
            for (var c = 0; c <= flat.Columns; c++)
            {
                flat.Vertices.Add(Vector3.zero);
                flat.Vertices.Add(Vector3.zero);
                flat.Uvs.Add(Vector2.zero);
                flat.Uvs.Add(Vector2.zero);
                flat.Colours.Add(Color.white);
                flat.Colours.Add(Color.white);
                if (c == flat.Columns) break;
                var a = c * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 3);
                triangles.Add(a); triangles.Add(a + 3); triangles.Add(a + 2);
            }
            mesh.SetVertices(flat.Vertices);
            mesh.SetUVs(0, flat.Uvs);
            mesh.SetColors(flat.Colours);
            mesh.SetTriangles(triangles, 0);
            // it is moved every frame to where the camera looks: never culled by a stale box
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e7f, 1e6f, 1e5f));
            _flat.Add(flat);
        }

        // DD1 scales each strip to the tile's width and hangs it per tile in front of the party: the upper one
        // from the wall's top downwards, the lower one from just under the floor line upwards.
        private void Foreground(Func<string, string> artFile, int tiles)
        {
            var n = CorridorNumbers.TileWidth;
            for (var kind = 0; kind < 2; kind++)
            {
                var file = artFile(kind == 0 ? "foreground_top.01" : "foreground_bottom.01");
                if (!Dd1Install.Exists(Dd1Install.ArtPath(file))) continue;
                var texture = CleanForeground ? Cleaned(file) : Dd1Install.Texture(file);
                if (texture == null) continue;
                var height = texture.height * n / texture.width;
                var top = kind == 0 ? CorridorNumbers.WallTop + CorridorNumbers.ForegroundTopY : CorridorNumbers.ForegroundBottomY + height;
                var go = NewMesh(kind == 0 ? "foreground top" : "foreground bottom", texture, ForegroundOrder + kind, 0, out var mesh);
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                for (var i = -3; i <= tiles + 2; i++)
                {
                    var left = i * n - n * 0.5f;
                    var a = vertices.Count;
                    vertices.Add(new Vector3(left, top - height, CorridorNumbers.ForegroundZ));
                    vertices.Add(new Vector3(left, top, CorridorNumbers.ForegroundZ));
                    vertices.Add(new Vector3(left + n, top, CorridorNumbers.ForegroundZ));
                    vertices.Add(new Vector3(left + n, top - height, CorridorNumbers.ForegroundZ));
                    uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2);
                    triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 3);
                }
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                _foreground.Add(go.GetComponent<MeshRenderer>());
                _foregroundMeshes.Add(mesh);
            }
            _foregroundAlpha = -1f;
            _foregroundMode = -1;
        }

        private static readonly Dictionary<string, Texture2D> CleanedStrips = new Dictionary<string, Texture2D>();

        // A foreground strip with its stray strokes taken out (kept for the session: a dungeon has two). A pixel
        // that is not black goes black when fewer than three in ten of the 11x11 pixels round it are not black
        // either: a hair-thin line in the dark is that, a painted brick or a chain's link is not. (An opening
        // with a 5x5 block was tried first: it took the strokes and chewed the bricks into squares.)
        private static Texture2D Cleaned(string file)
        {
            // (a picture of the mod's own may stand in for DD1's, or not: each is cleaned and kept by itself)
            var key = file + (Dd1Install.Replaced(Dd1Install.ArtPath(file)) ? "|own" : "");
            if (CleanedStrips.TryGetValue(key, out var cached) && cached != null) return cached;
            var texture = Dd1Install.LoadTexture(file, true, false);
            if (texture == null) return null;
            try
            {
                // (5 pixels of DD1's art each way: more of them in a larger picture that stands in for it)
                var reach = Mathf.Max(1, Mathf.RoundToInt(5f * Dd1Install.Scale(Dd1Install.ArtPath(file))));
                const int dark = 6;
                const float share = 0.3f;
                var w = texture.width;
                var h = texture.height;
                var pixels = texture.GetPixels32();
                // how many pixels that are not black lie above and to the left of each point
                var sums = new int[(w + 1) * (h + 1)];
                for (var y = 0; y < h; y++)
                {
                    var row = 0;
                    for (var x = 0; x < w; x++)
                    {
                        var p = pixels[y * w + x];
                        if (Mathf.Max(p.r, Mathf.Max(p.g, p.b)) > dark) row++;
                        sums[(y + 1) * (w + 1) + x + 1] = sums[y * (w + 1) + x + 1] + row;
                    }
                }
                var removed = 0;
                for (var y = 0; y < h; y++)
                {
                    var y0 = Mathf.Max(0, y - reach);
                    var y1 = Mathf.Min(h, y + reach + 1);
                    for (var x = 0; x < w; x++)
                    {
                        var p = pixels[y * w + x];
                        if (Mathf.Max(p.r, Mathf.Max(p.g, p.b)) <= dark) continue;
                        var x0 = Mathf.Max(0, x - reach);
                        var x1 = Mathf.Min(w, x + reach + 1);
                        var count = sums[y1 * (w + 1) + x1] - sums[y0 * (w + 1) + x1] - sums[y1 * (w + 1) + x0] + sums[y0 * (w + 1) + x0];
                        if (count >= share * (y1 - y0) * (x1 - x0)) continue;
                        pixels[y * w + x] = new Color32(0, 0, 0, p.a);
                        removed++;
                    }
                }
                texture.SetPixels32(pixels);
                Plugin.Log.LogInfo("Corridor: " + removed + " stray pixels taken out of " + file);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Corridor: the foreground strip " + file + " is drawn as it is (" + e.Message + ")");
            }
            texture.Apply(false, true);
            CleanedStrips[key] = texture;
            return texture;
        }

        // ---- per frame -----------------------------------------------------------------------------------

        /// <summary>The camera in DD1 units and its zoom; <paramref name="viewPixels"/> the width of the dungeon view
        /// in DD1's pixels (720 rows high: 1920 on a 16:9 screen).</summary>
        public void Tick(Vector3 camera, float zoom, float viewPixels)
        {
            if (Root == null) return;
            if (_backdrop != null) _backdrop.localPosition = new Vector3(camera.x, camera.y, 0f);
            Light(camera, zoom);
            foreach (var flat in _flat) Place(flat, camera, zoom, viewPixels);
            ShowForeground();
        }

        private void Light(Vector3 camera, float zoom)
        {
            // nothing moved and the light is the same: the colours stand
            var now = new Vector4(camera.x, zoom, CorridorLight.Gamma + EndCapGain * 16f, CorridorLight.Enabled ? 1f : 0f);
            if (now == _lastLight && CorridorLight.Base == _lastBase && CorridorLight.Edge == _lastEdge) return;
            _lastLight = now;
            _lastBase = CorridorLight.Base;
            _lastEdge = CorridorLight.Edge;
            // what the camera can see of the wall, and a tile to spare
            var reach = (CorridorNumbers.WallZ - camera.z) * 1.6f / Mathf.Max(0.2f, zoom) + CorridorNumbers.TileWidth;
            foreach (var lit in _lit)
            {
                if (lit.MaxX < camera.x - reach || lit.MinX > camera.x + reach) continue;
                for (var c = 0; c < lit.ColumnX.Length; c++)
                {
                    var colour = CorridorLight.Ramp((lit.ColumnX[c] - camera.x) * zoom);
                    if (lit.GainLeft != 1f || lit.GainRight != 1f)
                    {
                        // the gain is on DD1's multiplier: taken to the same power as the ramp
                        var share = lit.ColumnX.Length > 1 ? c / (float)(lit.ColumnX.Length - 1) : 0f;
                        var gain = Mathf.Pow(Mathf.Max(0f, Mathf.Lerp(lit.GainLeft, lit.GainRight, share)), QualitySettings.activeColorSpace == ColorSpace.Linear ? CorridorLight.Gamma : 1f);
                        colour = new Color(colour.r * gain, colour.g * gain, colour.b * gain, 1f);
                    }
                    for (var k = 0; k < lit.PerColumn; k++) lit.Colours[c * lit.PerColumn + k] = colour;
                }
                lit.Mesh.SetColors(lit.Colours);
            }
        }

        // [inferred, spec 4.4] The picture stands at the view's own size with its middle row on the horizon,
        // and its texture moves Follow pixels for every unit the camera travels.
        private static void Place(Flat flat, Vector3 camera, float zoom, float viewPixels)
        {
            var perUnit = zoom * CorridorNumbers.Focal / (flat.Z - camera.z);      // view pixels to a unit at this depth
            var halfHeight = 360f / perUnit;
            // wider than the view: the steady cam turns the camera a little, and the layer must reach past both edges
            var width = viewPixels + 160f;
            for (var c = 0; c <= flat.Columns; c++)
            {
                var pixel = (c / (float)flat.Columns - 0.5f) * width;                 // from the view's middle
                var x = camera.x + pixel / perUnit;
                var u = (pixel + 960f + camera.x * flat.Follow) / flat.TexturePixels;
                var colour = CorridorLight.Ramp(pixel);
                flat.Vertices[c * 2] = new Vector3(x, camera.y - halfHeight, flat.Z);
                flat.Vertices[c * 2 + 1] = new Vector3(x, camera.y + halfHeight, flat.Z);
                flat.Uvs[c * 2] = new Vector2(u, 0f);
                flat.Uvs[c * 2 + 1] = new Vector2(u, 1f);
                flat.Colours[c * 2] = colour;
                flat.Colours[c * 2 + 1] = colour;
            }
            flat.Mesh.SetVertices(flat.Vertices);
            flat.Mesh.SetUVs(0, flat.Uvs);
            flat.Mesh.SetColors(flat.Colours);
        }

        private void ShowForeground()
        {
            if (_foreground.Count == 0) return;
            if (_foregroundMode != ForegroundMode)
            {
                _foregroundMode = ForegroundMode;
                var layer = ForegroundMode == 2 ? LayerMask.NameToLayer("Characters") : ForegroundLayer;
                foreach (var renderer in _foreground)
                {
                    renderer.gameObject.SetActive(ForegroundMode != 0);
                    renderer.gameObject.layer = layer < 0 ? 0 : layer;
                }
            }
            if (Mathf.Approximately(_foregroundAlpha, ForegroundAlpha)) return;
            _foregroundAlpha = ForegroundAlpha;
            var colour = new Color(1f, 1f, 1f, Mathf.Clamp01(ForegroundAlpha));
            foreach (var mesh in _foregroundMeshes)
            {
                var colours = new List<Color>(mesh.vertexCount);
                for (var i = 0; i < mesh.vertexCount; i++) colours.Add(colour);
                mesh.SetColors(colours);
            }
        }

        public void Destroy()
        {
            foreach (var mesh in _meshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            _meshes.Clear();
            _lit.Clear();
            _flat.Clear();
            _foreground.Clear();
            _foregroundMeshes.Clear();
            _backdrop = null;
            _lastLight = new Vector4(float.NaN, 0f, 0f, 0f);
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            Root = null;
        }

        /// <summary>Dev bridge: every layer with its depth, order and how large the camera shows it.</summary>
        public List<object> Describe(Vector3 camera, float zoom)
        {
            var list = new List<object>();
            Func<float, float> perUnit = z => zoom * CorridorNumbers.Focal / (z - camera.z);
            foreach (var flat in _flat)
                list.Add(new { name = flat.Name, z = flat.Z, order = flat.Order, follow = flat.Follow, pxPerUnit = 1f, note = "at the view's own size" });
            var walls = 0;
            foreach (var lit in _lit)
            {
                if (lit.MaxX < camera.x - 1500f || lit.MinX > camera.x + 1500f) continue;
                walls++;
                list.Add(new { name = lit.Name, z = lit.Z, order = lit.Order, x = new[] { lit.MinX, lit.MaxX }, columns = lit.ColumnX.Length - 1, pxPerUnit = perUnit(lit.Z), floorTo = CorridorNumbers.FloorNearZ });
            }
            foreach (var renderer in _foreground)
                list.Add(new { name = renderer.name, z = CorridorNumbers.ForegroundZ, order = renderer.sortingOrder, layer = renderer.gameObject.layer, shown = renderer.gameObject.activeSelf, pxPerUnit = perUnit(CorridorNumbers.ForegroundZ) });
            list.Add(new { name = "walls in all", count = _lit.Count, near = walls });
            return list;
        }

        // ---- the mod's own camera -------------------------------------------------------------------------

        /// <summary>
        /// The overlay camera the mod keeps last in the main camera's stack. It draws the foreground strips after
        /// the heroes (ForegroundMode 1), and it is the one camera that listens to the mod's own volume
        /// (<see cref="CorridorPost"/>: colour grade and grain), so those are applied once, over the whole view.
        /// </summary>
        internal static class ForegroundCamera
        {
            private static Camera _camera;
            private static UniversalAdditionalCameraData _data;

            public static Camera Camera => _camera;
            public static string Status { get; private set; } = "not made";

            /// <summary>Every frame while the corridor is shown: the camera exists and is the last of the main camera's stack.</summary>
            public static void Keep(Camera main, bool strips, bool post)
            {
                if (main == null || (!strips && !post))
                {
                    Release();
                    return;
                }
                try
                {
                    if (_camera == null)
                    {
                        var go = new GameObject("DD2Estate.ForegroundCamera");
                        go.transform.SetParent(main.transform, false);
                        _camera = go.AddComponent<Camera>();
                        _camera.CopyFrom(main);
                        _data = _camera.GetUniversalAdditionalCameraData();
                        _data.renderType = CameraRenderType.Overlay;
                        // the game's own way of keeping an overlay camera on the main camera's lens
                        go.AddComponent<Assets.Code.Rendering.CopyCameraFrustumBhv>();
                    }
                    _camera.cullingMask = strips ? 1 << ForegroundLayer : 0;
                    // the mod's volume lies on the strips' layer: no other camera of the game has it in its mask
                    _data.volumeLayerMask = 1 << ForegroundLayer;
                    _data.renderPostProcessing = post;
                    if (!_camera.gameObject.activeSelf) _camera.gameObject.SetActive(true);
                    var stack = main.GetUniversalAdditionalCameraData().cameraStack;
                    if (stack == null)
                    {
                        Status = "the main camera has no camera stack";
                        return;
                    }
                    // the game's character camera puts itself behind whatever it finds when it is switched on again
                    if (stack.Count == 0 || stack[stack.Count - 1] != _camera)
                    {
                        stack.Remove(_camera);
                        stack.Add(_camera);
                    }
                    Status = "last of " + stack.Count + " in the stack" + (strips ? ", strips" : "") + (post ? ", post effects" : "");
                }
                catch (Exception e)
                {
                    Status = "failed: " + e.Message;
                    Plugin.Log.LogWarning("Corridor: the mod's overlay camera could not be set up (" + e.Message + "); the strips go on the heroes' layer, no grade");
                    Release();
                    if (ForegroundMode == 1) ForegroundMode = 2;
                    CorridorPost.Grade = false;
                    CorridorPost.GrainOn = false;
                }
            }

            /// <summary>Dev bridge: the main camera and its stack, with what decides who runs post effects on which volumes.
            /// The mod's volume must be taken by the mod's camera alone, or the grade is applied twice.</summary>
            public static List<object> Stack(Camera main)
            {
                var list = new List<object>();
                if (main == null) return list;
                var data = main.GetUniversalAdditionalCameraData();
                list.Add(Row(main, data));
                if (data.cameraStack != null)
                    foreach (var camera in data.cameraStack)
                        if (camera != null) list.Add(Row(camera, camera.GetUniversalAdditionalCameraData()));
                return list;
            }

            private static object Row(Camera camera, UniversalAdditionalCameraData data)
            {
                return new
                {
                    name = camera.name,
                    active = camera.isActiveAndEnabled,
                    postEffects = data.renderPostProcessing,
                    volumeMask = (int)data.volumeLayerMask,
                    takesModVolume = (data.volumeLayerMask & (1 << ForegroundLayer)) != 0,
                    cullingMask = camera.cullingMask
                };
            }

            public static void Release()
            {
                if (_camera == null) return;
                try
                {
                    var main = Camera.main;
                    if (main != null) main.GetUniversalAdditionalCameraData().cameraStack?.Remove(_camera);
                }
                catch (Exception)
                {
                    // the stack drops a destroyed camera by itself
                }
                UnityEngine.Object.Destroy(_camera.gameObject);
                _camera = null;
                _data = null;
                Status = "released";
            }
        }
    }
}
