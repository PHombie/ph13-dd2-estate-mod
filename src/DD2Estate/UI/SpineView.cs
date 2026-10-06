using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.UI
{
    /// <summary>
    /// One of DD1's animated sprites in a canvas: a Spine skeleton with its atlas and sheet
    /// (fx/&lt;name&gt;/&lt;name&gt;.sprite.skel, .atlas, .png), posed by <see cref="SpineSkeleton.Pose"/> while its
    /// animation runs and drawn as triangles over the sheet. What swells, turns, flies and fades in DD1 does so
    /// here, and nothing of an animation is written down in the mod: the estate bar's gold pile and buttons, the
    /// alert over a building and on a screen's name, the ring round a screen's "+", a building's lights and
    /// smoke, the sparkle round a trinket's card, the effects of the results screens.
    /// The rect has no size: its place is the skeleton's root and its units are DD1's pixels, y up.
    /// Slots DD1 blends additively are drawn like the others (none of the town's sprites has one).
    ///
    ///     var pile = SpineView.Create("GoldPile", bar, "fx/estate_gold_pile/estate_gold_pile.sprite", at);
    ///     pile.Play("idle");                          // round and round
    ///     pile.Play("spend", "idle");                 // once, then round and round
    ///     glow.Play("treasure_glow", false, done);    // once; done is called when it has run out
    ///     SpineView.Loop("Alert", parent, SpineView.Fx("estate_exclamation"), at, "alert");
    /// </summary>
    internal class SpineView : MonoBehaviour
    {
        /// <summary>
        /// One texture's share of a pose. Pictures cut straight from the sheet share a layer; a picture that
        /// lies turned on the sheet has a texture of its own (<see cref="SpineAtlas"/>), and so a layer.
        /// </summary>
        private class Layer : MaskableGraphic
        {
            public Texture Texture;
            public readonly List<UIVertex> Vertices = new List<UIVertex>();
            public readonly List<int> Corners = new List<int>();

            public override Texture mainTexture => Texture != null ? Texture : s_WhiteTexture;

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (Vertices.Count > 0) vh.AddUIVertexStream(Vertices, Corners);
            }
        }

        private SpineSkeleton _skeleton;
        private SpineAtlas _atlas;
        private readonly List<SpineSkeleton.Piece> _pieces = new List<SpineSkeleton.Piece>();
        private readonly List<Layer> _layers = new List<Layer>();
        private string _animation, _next;
        private float _time, _drawn, _reach;
        private bool _loop, _playing, _stale;
        private Color _tint = Color.white;
        private Action _done;

        /// <summary>Slots that are not drawn (a building's own picture, which the hamlet draws and hits itself).</summary>
        public Func<string, bool> Leaves;

        /// <summary>
        /// The least time between two drawings of a running animation; 0: every frame. DD1's animations are keyed
        /// at 30 a second: a sprite that stands on many things at once (a trinket's sparkle) is drawn that often.
        /// </summary>
        public float Step;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>The animation on show; null for the skeleton as it was set up.</summary>
        public string Animation => _animation;

        /// <summary>How far into its animation the sprite is.</summary>
        public float Seconds => _time;

        /// <summary>An animation played once is still running.</summary>
        public bool Playing => _playing && !_loop;

        /// <summary>Multiplies every picture's colour.</summary>
        public Color Tint
        {
            get => _tint;
            set
            {
                if (_tint == value) return;
                _tint = value;
                _stale = true;
            }
        }

        /// <summary>The files of one of DD1's effects, by its folder's name: "fx/estate_gold_pile/estate_gold_pile.sprite".</summary>
        public static string Fx(string name) => "fx/" + name + "/" + name + ".sprite";

        /// <summary>
        /// The sprite of "fx/estate_gold_pile/estate_gold_pile.sprite" (the files' name without .skel, .atlas),
        /// showing the skeleton as it was set up until something is played. Null, and nothing in the log, when
        /// the install has no such sprite. <paramref name="straight"/>: the sheet's colours as DD1 draws them
        /// (see <see cref="SpineAtlas.Load"/>).
        /// </summary>
        public static SpineView Create(string name, Transform parent, string stem, bool straight = false)
        {
            if (stem == null || !Dd1Install.Found || !Dd1Install.Exists(stem + ".skel") || !Dd1Install.Exists(stem + ".atlas")) return null;
            var skeleton = SpineSkeleton.Load(stem + ".skel");
            var atlas = skeleton != null ? SpineAtlas.Load(stem + ".atlas", false, straight) : null;
            if (atlas == null) return null;

            var rect = UiKit.Rect(name, parent);
            rect.sizeDelta = Vector2.zero;
            var view = rect.gameObject.AddComponent<SpineView>();
            view._skeleton = skeleton;
            view._atlas = atlas;
            // The setup pose shows every part at once: twice its reach is the room the layers ask the canvas for
            // (a mask culls by a graphic's rect, not by what it draws).
            skeleton.Pose(null, 0f, atlas, view._pieces);
            foreach (var piece in view._pieces)
                foreach (var point in piece.Vertices)
                    view._reach = Mathf.Max(view._reach, Mathf.Abs(point.x), Mathf.Abs(point.y));
            view._reach = view._reach * 4f + 64f;
            view.Draw();
            return view;
        }

        /// <summary>
        /// The same with the skeleton's root at <paramref name="at"/> of the parent (pixels from its top left
        /// corner, y down, as DD1's layout files count).
        /// </summary>
        public static SpineView Create(string name, Transform parent, string stem, Vector2 at)
        {
            var view = Create(name, parent, stem);
            if (view != null) view.Rect.PlaceTopLeft(at, new Vector2(0.5f, 0.5f), Vector2.zero);
            return view;
        }

        /// <summary>
        /// The sprite at <paramref name="at"/> of the parent, playing one of its animations round and round. Null
        /// when the install has no such sprite or the sprite no such animation (the skeleton as it was set up
        /// shows all its parts at once, which is no stand-in for an effect).
        /// </summary>
        public static SpineView Loop(string name, Transform parent, string stem, Vector2 at, string animation)
        {
            var view = Create(name, parent, stem, at);
            if (view == null) return null;
            if (!view.Has(animation))
            {
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
                return null;
            }
            view.Play(animation);
            return view;
        }

        public bool Has(string animation) => _skeleton.Has(animation);

        public float Duration(string animation) => _skeleton.Duration(animation);

        /// <summary>
        /// Runs an animation from its start: round and round, or once (it then holds its last moment). One that
        /// only sets a state is shown at once.
        /// </summary>
        public void Play(string animation, bool loop = true)
        {
            Play(animation, loop, null);
        }

        /// <summary>
        /// The same; <paramref name="done"/> is called once when an animation played once has run out (at once
        /// for one that only sets a state).
        /// </summary>
        public void Play(string animation, bool loop, Action done)
        {
            _next = null;
            _done = null;
            Begin(animation, loop);
            _done = done;
            if (!_playing) Finished();
        }

        /// <summary>Runs an animation once and goes on with another, round and round.</summary>
        public void Play(string once, string then)
        {
            if (!_skeleton.Has(once) || _skeleton.Duration(once) <= 0f)
            {
                Play(then);
                return;
            }
            _done = null;
            Begin(once, false);
            _next = then;
        }

        /// <summary>Holds an animation's last moment (what one played once leaves behind).</summary>
        public void ShowEnd(string animation)
        {
            _next = null;
            _done = null;
            _animation = _skeleton.Has(animation) ? animation : null;
            _time = _skeleton.Duration(_animation);
            _loop = false;
            _playing = false;
            Draw();
        }

        private void Begin(string animation, bool loop)
        {
            _animation = _skeleton.Has(animation) ? animation : null;
            _time = 0f;
            _loop = loop;
            _playing = _skeleton.Duration(_animation) > 0f;
            Draw();
        }

        private void Finished()
        {
            var done = _done;
            _done = null;
            done?.Invoke();
        }

        private void OnEnable()
        {
            _stale = true;
        }

        private void Update()
        {
            if (_skeleton == null) return;
            var ended = false;
            if (_playing)
            {
                var duration = _skeleton.Duration(_animation);
                // the screens' time is the player's: a paused or slowed game does not hold a sprite up
                _time += Time.unscaledDeltaTime;
                if (_time >= duration)
                {
                    if (_loop) _time = Mathf.Repeat(_time, duration);
                    else if (_next != null)
                    {
                        var next = _next;
                        _next = null;
                        Begin(next, true);
                        return;
                    }
                    else
                    {
                        _time = duration;
                        _playing = false;
                        ended = true;
                    }
                }
                // a last moment is always drawn; between two a sprite may wait for its step
                if (ended || Step <= 0f || _time < _drawn || _time - _drawn >= Step) _stale = true;
            }
            if (_stale) Draw();
            if (ended) Finished();
        }

        private void Draw()
        {
            _stale = false;
            _drawn = _time;
            _skeleton.Pose(_animation, _time, _atlas, _pieces);
            var used = 0;
            Layer layer = null;
            foreach (var piece in _pieces)
            {
                if (Leaves != null && Leaves(piece.Slot)) continue;
                var region = _atlas.Find(piece.Region);
                if (region == null || region.Texture == null) continue;
                if (layer == null || layer.Texture != region.Texture)
                {
                    layer = LayerAt(used++);
                    layer.Vertices.Clear();
                    layer.Corners.Clear();
                    if (layer.Texture != region.Texture)
                    {
                        layer.Texture = region.Texture;
                        layer.SetMaterialDirty();
                    }
                }

                var first = layer.Vertices.Count;
                float width = region.Texture.width, height = region.Texture.height;
                var rect = region.TextureRect;
                var vertex = UIVertex.simpleVert;
                vertex.color = (Color32)(piece.Color * _tint);
                for (var v = 0; v < piece.Vertices.Length; v++)
                {
                    var point = piece.Vertices[v];
                    vertex.position = new Vector3(point.x, point.y, 0f);
                    // a piece counts u, v from the top left corner of its upright picture, the texture from its bottom left
                    vertex.uv0 = new Vector2((rect.x + piece.Uvs[v].x * rect.width) / width, (rect.y + (1f - piece.Uvs[v].y) * rect.height) / height);
                    layer.Vertices.Add(vertex);
                }
                foreach (var corner in piece.Triangles) layer.Corners.Add(first + corner);
            }
            for (var i = 0; i < _layers.Count; i++)
            {
                if (i >= used)
                {
                    if (_layers[i].Vertices.Count == 0) continue;
                    _layers[i].Vertices.Clear();
                    _layers[i].Corners.Clear();
                }
                _layers[i].SetVerticesDirty();
            }
        }

        // Layers are children in the order they are drawn.
        private Layer LayerAt(int index)
        {
            while (_layers.Count <= index)
            {
                var rect = UiKit.Rect("Layer" + _layers.Count, transform);
                rect.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(_reach, _reach));
                var layer = rect.gameObject.AddComponent<Layer>();
                layer.raycastTarget = false;
                _layers.Add(layer);
            }
            return _layers[index];
        }
    }
}
