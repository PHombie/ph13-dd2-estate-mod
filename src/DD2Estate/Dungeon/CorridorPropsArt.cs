using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Where DD1 keeps the art of what stands in a dungeon. Curios: <c>curios/curio_props.csv</c> names the
    /// sprite of each prop, the skeleton is <c>props/shared/curios/&lt;sprite&gt;/</c>. Traps, obstacles and
    /// doors: the <c>graphics_file</c> of their entry in <c>props/trap_definitions.json</c>,
    /// <c>props/obstacle_definitions.json</c> and <c>props/&lt;dungeon&gt;/prop_definitions.json</c>.
    /// </summary>
    internal static class PropArt
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Tables = new Dictionary<string, Dictionary<string, string>>();
        private static CurioCatalog _curios;

        public static string Curio(string propId)
        {
            if (string.IsNullOrEmpty(propId)) return null;
            if (_curios == null) _curios = CurioCatalog.Load(new Dd1Files());
            // a prop may borrow another's art (the locked strongbox is drawn as the heirloom chest); quest curios
            // have no row and are drawn under their own name
            var sprite = _curios.Prop(propId)?.Sprite;
            if (string.IsNullOrEmpty(sprite)) sprite = propId;
            return Existing("props/shared/curios/" + sprite + "/" + sprite + ".skel");
        }

        public static string Trap(string propId)
        {
            return Defined("props/trap_definitions.json", propId) ?? Existing("props/shared/traps/" + propId + "/" + propId + ".skel");
        }

        public static string Obstacle(string propId)
        {
            return Defined("props/obstacle_definitions.json", propId) ?? Existing("props/shared/obstacles/" + propId + "/" + propId + ".skel");
        }

        /// <summary>The door of a dungeon's hallways; the Darkest Dungeon has one per quest.</summary>
        public static string Door(string dungeon)
        {
            var name = dungeon == "darkestdungeon" ? dungeon + "_quest_" + Dd1Install.DarkestQuestArt + "_door" : dungeon + "_door";
            return Defined("props/" + dungeon + "/prop_definitions.json", name) ?? Existing("props/" + dungeon + "/doors/door0/door0.skel");
        }

        private static string Existing(string skeleton) => Dd1Install.Exists(skeleton) ? skeleton : null;

        private static string Defined(string table, string propId)
        {
            if (string.IsNullOrEmpty(propId)) return null;
            if (!Tables.TryGetValue(table, out var files))
            {
                Tables[table] = files = new Dictionary<string, string>();
                var json = Json.ParseFile(Dd1Install.ReadText(table));
                foreach (var prop in Json.Array(json?["props"]))
                {
                    var name = (string)prop["name"];
                    var file = prop["default_data"] is JObject data ? (string)data["graphics_file"] : null;
                    if (name != null && !string.IsNullOrEmpty(file)) files[name] = file;
                }
            }
            return files.TryGetValue(propId, out var skeleton) ? Existing(skeleton) : null;
        }
    }

    /// <summary>
    /// One DD1 prop on screen: a Spine skeleton posed by <see cref="SpineSkeleton.Pose"/> and drawn as meshes
    /// over its atlas page. DD1's props are meshes, some of them bent by bones, so they cannot be sprites; the
    /// meshes use the material the corridor's sprites use and are ordered among them by sorting order.
    /// The transform's units are the skeleton's pixels: the owner scales and places it.
    /// </summary>
    internal class PropModel
    {
        private class Layer
        {
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public Texture Texture;
            /// <summary>The pose's own colours (slot fades), before the light.</summary>
            public readonly List<Color32> Plain = new List<Color32>();
        }

        private const float FrameSeconds = 1f / 30f;        // DD1's own animations are keyed at 30 a second
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static Material _material;
        private static readonly List<Vector3> Vertices = new List<Vector3>();
        private static readonly List<Vector2> Uvs = new List<Vector2>();
        private static readonly List<Color32> Colors = new List<Color32>();
        private static readonly List<int> Triangles = new List<int>();
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        public readonly Transform Root;
        public readonly string Skeleton;
        private readonly SpineSkeleton _skeleton;
        private readonly SpineAtlas _atlas;
        private readonly string _atlasPath;
        private readonly List<SpineSkeleton.Piece> _pieces = new List<SpineSkeleton.Piece>();
        private readonly List<Layer> _layers = new List<Layer>();
        private readonly int _order;
        private string _animation;
        private float _time, _drawn = -1f;
        private bool _loop, _playing;
        private Color _tint = Color.white;
        private static readonly List<Color32> Tinted = new List<Color32>();

        public string Animation => _animation;
        public float Time => _time;
        /// <summary>A one-shot animation is still running.</summary>
        public bool Playing => _playing && !_loop;
        /// <summary>What the pose covers, in skeleton pixels from its origin.</summary>
        public Rect Bounds { get; private set; }
        public int Layers { get; private set; }
        public string AtlasPath => _atlasPath;

        private PropModel(Transform root, string skeletonPath, SpineSkeleton skeleton, SpineAtlas atlas, string atlasPath, int order)
        {
            Root = root;
            Skeleton = skeletonPath;
            _skeleton = skeleton;
            _atlas = atlas;
            _atlasPath = atlasPath;
            _order = order;
        }

        /// <summary>Null when DD1 has no such skeleton (or its atlas is missing).</summary>
        public static PropModel Create(string skeletonPath, Transform parent, string name, int sortingOrder)
        {
            if (skeletonPath == null || !skeletonPath.EndsWith(".skel", StringComparison.OrdinalIgnoreCase)) return null;
            var atlasPath = skeletonPath.Substring(0, skeletonPath.Length - ".skel".Length) + ".atlas";
            var skeleton = SpineSkeleton.Load(skeletonPath);
            // the masks tell a click on the prop from a click on the air around it
            var atlas = skeleton != null ? SpineAtlas.Load(atlasPath, true) : null;
            if (atlas == null) return null;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            return new PropModel(root, skeletonPath, skeleton, atlas, atlasPath, sortingOrder);
        }

        public bool Has(string animation) => _skeleton.Has(animation);

        public float Duration(string animation) => _skeleton.Duration(animation);

        /// <summary>Holds an animation's first or last moment (a closed chest, an opened one).</summary>
        public void Show(string animation, bool atEnd)
        {
            _animation = _skeleton.Has(animation) ? animation : null;
            _time = atEnd ? _skeleton.Duration(_animation) : 0f;
            _playing = false;
            Draw();
        }

        /// <summary>
        /// Plays an animation from its start; one that only sets a state is shown at once. A looping one takes
        /// over the running time of the loop before it, so a prop that starts to glow does not jump.
        /// </summary>
        public void Play(string animation, bool loop)
        {
            var keep = loop && _loop && _playing;
            _animation = _skeleton.Has(animation) ? animation : null;
            var duration = _skeleton.Duration(_animation);
            _time = keep && duration > 0f ? Mathf.Repeat(_time, duration) : 0f;
            _loop = loop;
            _playing = duration > 0f;
            Draw();
        }

        public void Tick(float seconds)
        {
            if (!_playing || !Root.gameObject.activeInHierarchy) return;
            var duration = _skeleton.Duration(_animation);
            _time += seconds;
            if (_time >= duration)
            {
                if (_loop) _time = Mathf.Repeat(_time, duration);
                else
                {
                    _time = duration;
                    _playing = false;
                }
            }
            if (!_playing || Mathf.Abs(_time - _drawn) >= FrameSeconds || _time < _drawn) Draw();
        }

        private void Draw()
        {
            _drawn = _time;
            _skeleton.Pose(_animation, _time, _atlas, _pieces);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var used = 0;
            Texture run = null;
            Vertices.Clear();
            Uvs.Clear();
            Colors.Clear();
            Triangles.Clear();
            foreach (var piece in _pieces)
            {
                var region = _atlas.Find(piece.Region);
                if (region == null || region.Texture == null) continue;
                // pieces cut from one texture share a mesh; a region packed rotated has a texture of its own
                if (run != null && region.Texture != run) Flush(used++, run);
                run = region.Texture;
                var first = Vertices.Count;
                float width = region.Texture.width, height = region.Texture.height;
                var rect = region.TextureRect;
                Color32 color = piece.Color;
                for (var v = 0; v < piece.Vertices.Length; v++)
                {
                    var p = piece.Vertices[v];
                    Vertices.Add(new Vector3(p.x, p.y, 0f));
                    Uvs.Add(new Vector2((rect.x + piece.Uvs[v].x * rect.width) / width, (rect.y + (1f - piece.Uvs[v].y) * rect.height) / height));
                    Colors.Add(color);
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
                foreach (var corner in piece.Triangles) Triangles.Add(first + corner);
            }
            if (run != null) Flush(used++, run);
            for (var i = used; i < _layers.Count; i++) _layers[i].Object.SetActive(false);
            Layers = used;
            Bounds = used > 0 ? Rect.MinMaxRect(minX, minY, maxX, maxY) : new Rect(0f, 0f, 0f, 0f);
        }

        private void Flush(int index, Texture texture)
        {
            while (_layers.Count <= index) _layers.Add(NewLayer(_layers.Count));
            var layer = _layers[index];
            layer.Object.SetActive(true);
            layer.Mesh.Clear();
            layer.Mesh.SetVertices(Vertices);
            layer.Mesh.SetUVs(0, Uvs);
            layer.Plain.Clear();
            layer.Plain.AddRange(Colors);
            layer.Mesh.SetColors(Lit(layer.Plain));
            layer.Mesh.SetTriangles(Triangles, 0);
            layer.Mesh.RecalculateBounds();
            if (layer.Texture != texture)
            {
                layer.Texture = texture;
                layer.Renderer.GetPropertyBlock(Block);
                Block.SetTexture(MainTex, texture);
                layer.Renderer.SetPropertyBlock(Block);
            }
            Vertices.Clear();
            Uvs.Clear();
            Colors.Clear();
            Triangles.Clear();
        }

        /// <summary>
        /// DD1's light on the prop: one colour for the whole of it, the ramp's at its foot (DD1 lights it per pixel;
        /// a prop is narrow against the ramp's stops). Multiplies the pose's own colours.
        /// </summary>
        public void SetTint(Color tint)
        {
            if (Mathf.Abs(tint.r - _tint.r) + Mathf.Abs(tint.g - _tint.g) + Mathf.Abs(tint.b - _tint.b) < 0.002f) return;
            _tint = tint;
            for (var i = 0; i < Layers && i < _layers.Count; i++) _layers[i].Mesh.SetColors(Lit(_layers[i].Plain));
        }

        public Color Tint => _tint;

        /// <summary>The layer its meshes are on: the main camera's, or the heroes' while a timescript brings the prop forward.</summary>
        public void SetLayer(int layer)
        {
            if (layer < 0 || layer == _layer) return;
            _layer = layer;
            foreach (var mesh in _layers) mesh.Object.layer = layer;
        }

        private int _layer;

        private List<Color32> Lit(List<Color32> plain)
        {
            if (_tint.r >= 0.999f && _tint.g >= 0.999f && _tint.b >= 0.999f) return plain;
            Tinted.Clear();
            foreach (var c in plain)
                Tinted.Add(new Color32((byte)(c.r * _tint.r + 0.5f), (byte)(c.g * _tint.g + 0.5f), (byte)(c.b * _tint.b + 0.5f), c.a));
            return Tinted;
        }

        private Layer NewLayer(int index)
        {
            var go = new GameObject("Mesh" + index);
            go.transform.SetParent(Root, false);
            go.layer = _layer;
            var mesh = new Mesh { name = Root.name, hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SpriteMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortingOrder = _order + index;
            return new Layer { Object = go, Mesh = mesh, Renderer = renderer };
        }

        // The material a new SpriteRenderer gets is the one the corridor's walls are drawn with: unlit, alpha
        // blended, tinted by vertex colour, in whatever shader this build of the game has for sprites.
        internal static Material SpriteMaterial()
        {
            if (_material != null) return _material;
            var probe = new GameObject("DD2Estate.SpriteMaterialProbe");
            var shared = probe.AddComponent<SpriteRenderer>().sharedMaterial;
            UnityEngine.Object.Destroy(probe);
            if (shared != null) _material = new Material(shared);
            else
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) Plugin.Log.LogError("Corridor props: no sprite shader, DD1's props cannot be drawn");
                _material = new Material(shader);
            }
            _material.name = "DD2Estate.Props";
            _material.hideFlags = HideFlags.HideAndDontSave;
            return _material;
        }

        public string MaterialInfo => _material != null && _material.shader != null ? _material.shader.name : "none";

        /// <summary>
        /// Is a point (skeleton pixels from the origin) on the prop: inside one of its triangles, where the
        /// art is opaque enough to click. A slot faded most of the way out does not count.
        /// </summary>
        public bool Hit(Vector2 point)
        {
            if (!Root.gameObject.activeInHierarchy || !Bounds.Contains(point)) return false;
            for (var i = _pieces.Count - 1; i >= 0; i--)
            {
                var piece = _pieces[i];
                if (piece.Color.a < 0.25f) continue;
                var region = _atlas.Find(piece.Region);
                var corners = piece.Triangles;
                for (var t = 0; t + 2 < corners.Length; t += 3)
                {
                    Vector2 a = piece.Vertices[corners[t]], b = piece.Vertices[corners[t + 1]], c = piece.Vertices[corners[t + 2]];
                    var area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                    if (Mathf.Abs(area) < 1e-6f) continue;
                    var wb = ((point.x - a.x) * (c.y - a.y) - (c.x - a.x) * (point.y - a.y)) / area;
                    var wc = ((b.x - a.x) * (point.y - a.y) - (point.x - a.x) * (b.y - a.y)) / area;
                    if (wb < 0f || wc < 0f || wb + wc > 1f) continue;
                    if (region?.Mask == null) return true;
                    var uv = piece.Uvs[corners[t]] * (1f - wb - wc) + piece.Uvs[corners[t + 1]] * wb + piece.Uvs[corners[t + 2]] * wc;
                    // the mask counts v from the bottom of the upright region
                    if (region.Mask.Solid(uv.x, 1f - uv.y)) return true;
                }
            }
            return false;
        }

        public void Destroy()
        {
            foreach (var layer in _layers)
                if (layer.Mesh != null) UnityEngine.Object.Destroy(layer.Mesh);
            _layers.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        }
    }
}
