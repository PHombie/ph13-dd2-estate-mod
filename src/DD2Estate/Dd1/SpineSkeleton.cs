using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// A Spine 2.1 binary skeleton (`*.skel`, DD1 ships 2.1.27) read without a Spine runtime: bones, slots, the
    /// default skin's region, mesh and skinned mesh attachments, and the animations as far as they decide what
    /// is drawn where (bone, colour, attachment, draw order and mesh deformation keys).
    ///
    /// Two ways out: <see cref="Parts"/> is the setup pose's region attachments, as the hamlet's buildings are
    /// drawn; <see cref="Pose"/> is everything an animation shows at a moment, as triangles, which is what
    /// DD1's dungeon props need (they are meshes, and their states are animations that fade slots in and out).
    /// Not read: named skins, events, IK and flip keys (one prop has the first, none the second).
    /// tools/preview_corridor.py is the same reader in Python: change one, change the other.
    /// </summary>
    internal class SpineSkeleton
    {
        /// <summary>One atlas region as the setup pose draws it.</summary>
        public class Part
        {
            public string Slot;
            public string Region;
            /// <summary>Centre of the region relative to the skeleton origin, y up.</summary>
            public float X, Y;
            /// <summary>Drawn size of the region's original (unstripped) bounds.</summary>
            public float Width, Height;
            /// <summary>Degrees, counter-clockwise.</summary>
            public float Rotation;
            public Color Color;
            public bool Additive;
        }

        /// <summary>One attachment as a pose draws it: triangles over an atlas region.</summary>
        public class Piece
        {
            public string Slot;
            public string Region;
            /// <summary>Relative to the skeleton origin, y up. The array is reused from pose to pose.</summary>
            public Vector2[] Vertices;
            /// <summary>From the top-left corner of the upright atlas region, v down. Shared, not to be changed.</summary>
            public Vector2[] Uvs;
            /// <summary>Shared, not to be changed.</summary>
            public int[] Triangles;
            public Color Color;
            public bool Additive;
        }

        private class Bone
        {
            public int Parent;
            public float X, Y, ScaleX, ScaleY, Rotation;
            public bool FlipX, FlipY, InheritScale, InheritRotation;
        }

        private class Slot
        {
            public string Name, Attachment;
            public int Bone;
            public Color Color;
            public bool Additive;
        }

        private enum Shape { Region, Mesh, Skinned }

        private class Attachment
        {
            public string Key, Region;
            public Shape Shape;
            public Color Color;
            // region
            public float X, Y, ScaleX, ScaleY, Rotation, Width, Height;
            // mesh: Vertices are x, y pairs in the slot's bone; skinned: per vertex a bone count, then that
            // many bone indices in Bones, and x, y, weight triples in Weights
            public float[] Vertices;
            public int[] Bones;
            public float[] Weights;
            public Vector2[] Uvs;
            public int[] Triangles;

            /// <summary>Numbers a deformation key carries for this attachment.</summary>
            public int DeformSize => Shape == Shape.Mesh ? Vertices.Length : Weights.Length / 3 * 2;
        }

        /// <summary>Keys of one timeline: a time, <see cref="Stride"/> numbers and the curve to the next key.</summary>
        private class Keys
        {
            public float[] Times;
            public float[] Values;
            public int Stride;
            /// <summary>Per key: 0 linear, 1 stepped, 2 bezier with its two control points.</summary>
            public byte[] Kinds;
            public float[] Controls;

            public float Last => Times[Times.Length - 1];

            /// <summary>False before the first key (Spine leaves the setup value alone there).</summary>
            public bool Sample(float time, out int from, out int to, out float share)
            {
                from = to = 0;
                share = 0f;
                if (time < Times[0]) return false;
                for (int i = 0; i < Times.Length - 1; i++)
                {
                    if (time >= Times[i + 1]) continue;
                    from = i;
                    to = i + 1;
                    float span = Times[i + 1] - Times[i];
                    share = Curve(i, span > 0f ? (time - Times[i]) / span : 0f);
                    return true;
                }
                from = to = Times.Length - 1;
                return true;
            }

            public float Value(int key, int index) => Values[key * Stride + index];

            // Spine's CurveTimeline: a bezier is walked in ten straight pieces.
            private float Curve(int key, float percent)
            {
                if (Kinds[key] == 0) return percent;
                if (Kinds[key] == 1) return 0f;
                float cx1 = Controls[key * 4], cy1 = Controls[key * 4 + 1], cx2 = Controls[key * 4 + 2], cy2 = Controls[key * 4 + 3];
                float px = 0f, py = 0f;
                for (int i = 1; i <= BezierSegments; i++)
                {
                    float t = i / (float)BezierSegments, u = 1f - t;
                    float x = 3f * u * u * t * cx1 + 3f * u * t * t * cx2 + t * t * t;
                    float y = 3f * u * u * t * cy1 + 3f * u * t * t * cy2 + t * t * t;
                    if (x >= percent) return x > px ? py + (y - py) * (percent - px) / (x - px) : y;
                    px = x;
                    py = y;
                }
                return 1f;
            }
        }

        private class AttachmentKeys
        {
            public float[] Times;
            public string[] Names;
        }

        private class DeformKeys
        {
            public int Slot;
            public string Key;
            public Keys Timing;             // Stride 0: times and curves only
            public float[][] Deltas;        // per key; null = the mesh as it is
        }

        private class OrderKey
        {
            public float Time;
            /// <summary>As read: slot, offset pairs. After <see cref="Allocate"/>: the slot drawn at each place.</summary>
            public int[] Order;
        }

        /// <summary>A skin: per slot, its attachments by the name slots and attachment keys call them.</summary>
        private class Skin
        {
            private readonly Dictionary<int, Dictionary<string, Attachment>> _slots = new Dictionary<int, Dictionary<string, Attachment>>();

            public void Add(int slot, Attachment attachment)
            {
                if (!_slots.TryGetValue(slot, out var named)) _slots[slot] = named = new Dictionary<string, Attachment>();
                named[attachment.Key ?? ""] = attachment;
            }

            public Attachment Find(int slot, string key)
            {
                return key != null && _slots.TryGetValue(slot, out var named) && named.TryGetValue(key, out var attachment) ? attachment : null;
            }
        }

        private class Animation
        {
            public float Duration;
            public readonly Dictionary<int, Keys> Colors = new Dictionary<int, Keys>();
            public readonly Dictionary<int, AttachmentKeys> Attachments = new Dictionary<int, AttachmentKeys>();
            public readonly Dictionary<int, Keys> Rotations = new Dictionary<int, Keys>();
            public readonly Dictionary<int, Keys> Translations = new Dictionary<int, Keys>();
            public readonly Dictionary<int, Keys> Scales = new Dictionary<int, Keys>();
            public readonly List<DeformKeys> Deforms = new List<DeformKeys>();
            public readonly List<OrderKey> Orders = new List<OrderKey>();
        }

        private const int BezierSegments = 10;

        private static readonly Dictionary<string, SpineSkeleton> Cache = new Dictionary<string, SpineSkeleton>(StringComparer.OrdinalIgnoreCase);
        private static readonly Vector2[] QuadUvs = { new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f) };
        private static readonly int[] QuadTriangles = { 0, 1, 2, 0, 2, 3 };

        public string Version;
        /// <summary>The setup pose's region attachments in draw order (first = furthest back).</summary>
        public readonly List<Part> Parts = new List<Part>();

        private readonly List<Bone> _bones = new List<Bone>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Skin _skin = new Skin();
        private readonly Dictionary<string, Animation> _animations = new Dictionary<string, Animation>();

        // scratch for Pose: the bones (local, then world as [A B; C D] + (X, Y)) and the slots of the moment
        private float[] _x, _y, _rotation, _scaleX, _scaleY, _a, _b, _c, _d;
        private bool[] _flipX, _flipY;
        private Color[] _colors;
        private string[] _attached;
        private int[] _order;

        /// <summary>Loads a skeleton from the DD1 install (cached). Null if missing or not readable.</summary>
        public static SpineSkeleton Load(string relative)
        {
            if (Cache.TryGetValue(relative, out var cached)) return cached;
            var data = Dd1Install.ReadBytes(relative);
            if (data == null)
            {
                Plugin.Log.LogWarning("DD1 file missing: " + relative);
                return null;
            }
            SpineSkeleton skeleton;
            try { skeleton = Read(data); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("DD1 skeleton not readable: " + relative + " (" + e.Message + ")");
                return null;
            }
            Cache[relative] = skeleton;
            return skeleton;
        }

        public IEnumerable<string> Animations => _animations.Keys;

        public bool Has(string animation) => animation != null && _animations.ContainsKey(animation);

        /// <summary>Seconds to the last key of an animation; 0 for one that only sets a state (or is unknown).</summary>
        public float Duration(string animation)
        {
            return animation != null && _animations.TryGetValue(animation, out var anim) ? anim.Duration : 0f;
        }

        // ---- reading ---------------------------------------------------------------------------------

        public static SpineSkeleton Read(byte[] data)
        {
            var skeleton = new SpineSkeleton();
            var input = new Reader(data);
            input.String();                                 // hash
            skeleton.Version = input.String();
            input.Float();                                  // width
            input.Float();                                  // height
            bool nonessential = input.Bool();
            if (nonessential) input.String();               // images path

            for (int i = 0, n = input.VarInt(); i < n; i++)
            {
                input.String();                             // name
                var bone = new Bone { Parent = input.VarInt() - 1 };
                bone.X = input.Float();
                bone.Y = input.Float();
                bone.ScaleX = input.Float();
                bone.ScaleY = input.Float();
                bone.Rotation = input.Float();
                input.Float();                              // length
                bone.FlipX = input.Bool();
                bone.FlipY = input.Bool();
                bone.InheritScale = input.Bool();
                bone.InheritRotation = input.Bool();
                if (nonessential) input.Skip(4);            // colour
                skeleton._bones.Add(bone);
            }

            for (int i = 0, n = input.VarInt(); i < n; i++)  // ik constraints
            {
                input.String();
                for (int b = 0, bn = input.VarInt(); b < bn; b++) input.VarInt();
                input.VarInt();                             // target
                input.Float();                              // mix
                input.Skip(1);                              // bend direction
            }

            for (int i = 0, n = input.VarInt(); i < n; i++)
            {
                var slot = new Slot { Name = input.String(), Bone = input.VarInt(), Color = input.Color() };
                slot.Attachment = input.String();
                slot.Additive = input.Bool();
                skeleton._slots.Add(slot);
            }

            // The default skin is the one drawn. What follows it only adds animations: a skeleton whose tail
            // cannot be read still has its setup pose (the hamlet needs no more than that).
            var skins = new List<Skin> { skeleton._skin };
            ReadSkin(input, nonessential, skeleton._skin);
            try
            {
                for (int i = 0, n = input.VarInt(); i < n; i++)
                {
                    input.String();
                    var skin = new Skin();
                    ReadSkin(input, nonessential, skin);
                    skins.Add(skin);
                }

                for (int i = 0, n = input.VarInt(); i < n; i++)  // events
                {
                    input.String();
                    input.VarInt();
                    input.Float();
                    input.String();
                }

                for (int i = 0, n = input.VarInt(); i < n; i++)
                {
                    var name = input.String();
                    skeleton._animations[name ?? ""] = ReadAnimation(input, skins);
                }
            }
            catch (Exception e)
            {
                skeleton._animations.Clear();
                Plugin.Log.LogWarning("DD1 skeleton: animations not readable, the setup pose is used (" + e.Message + ")");
            }

            skeleton.Allocate();
            skeleton.BuildParts();
            return skeleton;
        }

        private static void ReadSkin(Reader input, bool nonessential, Skin skin)
        {
            for (int i = 0, n = input.VarInt(); i < n; i++)
            {
                int slot = input.VarInt();
                for (int a = 0, an = input.VarInt(); a < an; a++)
                {
                    var key = input.String();
                    var name = input.String() ?? key;
                    int type = input.Byte();
                    if (type == 1)                          // bounding box: nothing to draw
                    {
                        input.Skip(4 * input.VarInt());
                        continue;
                    }
                    var att = new Attachment { Key = key };
                    if (type == 0)
                    {
                        att.Shape = Shape.Region;
                        att.Region = input.String() ?? name;
                        att.X = input.Float();
                        att.Y = input.Float();
                        att.ScaleX = input.Float();
                        att.ScaleY = input.Float();
                        att.Rotation = input.Float();
                        att.Width = input.Float();
                        att.Height = input.Float();
                        att.Color = input.Color();
                    }
                    else if (type == 2 || type == 3)
                    {
                        att.Shape = type == 2 ? Shape.Mesh : Shape.Skinned;
                        att.Region = input.String() ?? name;
                        var uvs = input.Floats();
                        att.Uvs = new Vector2[uvs.Length / 2];
                        for (int u = 0; u < att.Uvs.Length; u++) att.Uvs[u] = new Vector2(uvs[u * 2], uvs[u * 2 + 1]);
                        att.Triangles = input.Shorts();
                        if (type == 2) att.Vertices = input.Floats();
                        else
                        {
                            // a run of floats: per vertex the number of bones, then bone, x, y, weight for each
                            var bones = new List<int>();
                            var weights = new List<float>();
                            for (int v = 0, count = input.VarInt(); v < count;)
                            {
                                int links = (int)input.Float();
                                bones.Add(links);
                                for (int l = 0; l < links; l++)
                                {
                                    bones.Add((int)input.Float());
                                    weights.Add(input.Float());
                                    weights.Add(input.Float());
                                    weights.Add(input.Float());
                                }
                                v += 1 + links * 4;
                            }
                            att.Bones = bones.ToArray();
                            att.Weights = weights.ToArray();
                        }
                        att.Color = input.Color();
                        input.VarInt();                     // hull length
                        if (nonessential)
                        {
                            for (int e = 0, en = input.VarInt(); e < en; e++) input.VarInt();   // edges
                            input.Skip(8);                  // width, height
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Spine attachment type " + type);
                    }
                    skin.Add(slot, att);
                }
            }
        }

        private static Keys ReadKeys(Reader input, int count, int stride, bool colour)
        {
            var keys = new Keys { Times = new float[count], Values = new float[count * stride], Stride = stride, Kinds = new byte[count], Controls = new float[count * 4] };
            for (int f = 0; f < count; f++)
            {
                keys.Times[f] = input.Float();
                if (colour)
                {
                    var c = input.Color();
                    keys.Values[f * 4] = c.r;
                    keys.Values[f * 4 + 1] = c.g;
                    keys.Values[f * 4 + 2] = c.b;
                    keys.Values[f * 4 + 3] = c.a;
                }
                else
                {
                    for (int v = 0; v < stride; v++) keys.Values[f * stride + v] = input.Float();
                }
                if (f < count - 1) ReadCurve(input, keys, f);
            }
            return keys;
        }

        private static void ReadCurve(Reader input, Keys keys, int key)
        {
            keys.Kinds[key] = (byte)input.Byte();
            if (keys.Kinds[key] != 2) return;
            for (int i = 0; i < 4; i++) keys.Controls[key * 4 + i] = input.Float();
        }

        private static Animation ReadAnimation(Reader input, List<Skin> skins)
        {
            var anim = new Animation();
            for (int i = 0, n = input.VarInt(); i < n; i++)      // slot timelines
            {
                int slot = input.VarInt();
                for (int t = 0, tn = input.VarInt(); t < tn; t++)
                {
                    int type = input.Byte(), count = input.VarInt();
                    if (type == 4)
                    {
                        var keys = ReadKeys(input, count, 4, true);
                        anim.Colors[slot] = keys;
                        anim.Duration = Mathf.Max(anim.Duration, keys.Last);
                    }
                    else if (type == 3)
                    {
                        var keys = new AttachmentKeys { Times = new float[count], Names = new string[count] };
                        for (int f = 0; f < count; f++)
                        {
                            keys.Times[f] = input.Float();
                            keys.Names[f] = input.String();
                        }
                        anim.Attachments[slot] = keys;
                        anim.Duration = Mathf.Max(anim.Duration, keys.Times[count - 1]);
                    }
                    else throw new NotSupportedException("Spine slot timeline " + type);
                }
            }

            for (int i = 0, n = input.VarInt(); i < n; i++)      // bone timelines
            {
                int bone = input.VarInt();
                for (int t = 0, tn = input.VarInt(); t < tn; t++)
                {
                    int type = input.Byte(), count = input.VarInt();
                    if (type == 5 || type == 6)             // flips: no DD1 prop keys them
                    {
                        for (int f = 0; f < count; f++)
                        {
                            anim.Duration = Mathf.Max(anim.Duration, input.Float());
                            input.Skip(1);
                        }
                        continue;
                    }
                    if (type < 0 || type > 2) throw new NotSupportedException("Spine bone timeline " + type);
                    var keys = ReadKeys(input, count, type == 1 ? 1 : 2, false);
                    (type == 1 ? anim.Rotations : type == 2 ? anim.Translations : anim.Scales)[bone] = keys;
                    anim.Duration = Mathf.Max(anim.Duration, keys.Last);
                }
            }

            for (int i = 0, n = input.VarInt(); i < n; i++)      // ik timelines: read past
            {
                input.VarInt();
                var keys = new Keys { Kinds = new byte[1], Controls = new float[4] };
                for (int f = 0, count = input.VarInt(); f < count; f++)
                {
                    anim.Duration = Mathf.Max(anim.Duration, input.Float());
                    input.Skip(5);                          // mix, bend direction
                    if (f < count - 1) ReadCurve(input, keys, 0);
                }
            }

            for (int i = 0, n = input.VarInt(); i < n; i++)      // mesh deformation
            {
                var skin = skins[input.VarInt()];
                for (int s = 0, sn = input.VarInt(); s < sn; s++)
                {
                    int slot = input.VarInt();
                    for (int a = 0, an = input.VarInt(); a < an; a++)
                    {
                        var key = input.String();
                        var att = skin.Find(slot, key);
                        if (att == null || att.Shape == Shape.Region) throw new NotSupportedException("Spine deformation of " + key);
                        int count = input.VarInt(), size = att.DeformSize;
                        var deform = new DeformKeys
                        {
                            Slot = slot, Key = key, Deltas = new float[count][],
                            Timing = new Keys { Times = new float[count], Values = new float[0], Kinds = new byte[count], Controls = new float[count * 4] }
                        };
                        for (int f = 0; f < count; f++)
                        {
                            deform.Timing.Times[f] = input.Float();
                            int changed = input.VarInt();
                            if (changed > 0)
                            {
                                // only the run of numbers that differ from the mesh is stored
                                var deltas = new float[size];
                                int start = input.VarInt();
                                for (int v = start; v < start + changed; v++) deltas[v] = input.Float();
                                deform.Deltas[f] = deltas;
                            }
                            if (f < count - 1) ReadCurve(input, deform.Timing, f);
                        }
                        // deformations of a named skin belong to art that is never drawn
                        if (skin == skins[0]) anim.Deforms.Add(deform);
                        anim.Duration = Mathf.Max(anim.Duration, deform.Timing.Last);
                    }
                }
            }

            for (int i = 0, n = input.VarInt(); i < n; i++)      // draw order
            {
                var moved = new int[input.VarInt() * 2];
                for (int m = 0; m < moved.Length; m++) moved[m] = input.VarInt();
                var key = new OrderKey { Order = moved, Time = input.Float() };
                anim.Orders.Add(key);
                anim.Duration = Mathf.Max(anim.Duration, key.Time);
            }

            for (int i = 0, n = input.VarInt(); i < n; i++)      // events
            {
                anim.Duration = Mathf.Max(anim.Duration, input.Float());
                input.VarInt();
                input.VarInt();
                input.Float();
                if (input.Bool()) input.String();
            }
            return anim;
        }

        private void Allocate()
        {
            int bones = _bones.Count, slots = _slots.Count;
            _x = new float[bones];
            _y = new float[bones];
            _rotation = new float[bones];
            _scaleX = new float[bones];
            _scaleY = new float[bones];
            _a = new float[bones];
            _b = new float[bones];
            _c = new float[bones];
            _d = new float[bones];
            _flipX = new bool[bones];
            _flipY = new bool[bones];
            _colors = new Color[slots];
            _attached = new string[slots];
            _order = new int[slots];

            // Spine's draw order key: the listed slots move by their offset, the others keep their order.
            foreach (var anim in _animations.Values)
                foreach (var key in anim.Orders)
                {
                    var order = new int[slots];
                    for (int i = 0; i < slots; i++) order[i] = -1;
                    var unchanged = new List<int>();
                    int original = 0;
                    for (int m = 0; m < key.Order.Length; m += 2)
                    {
                        while (original != key.Order[m]) unchanged.Add(original++);
                        order[original + key.Order[m + 1]] = original++;
                    }
                    while (original < slots) unchanged.Add(original++);
                    for (int i = slots - 1, u = unchanged.Count - 1; i >= 0; i--)
                        if (order[i] == -1) order[i] = unchanged[u--];
                    key.Order = order;
                }
        }

        // ---- posing ----------------------------------------------------------------------------------

        private void SetupBones()
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                var bone = _bones[i];
                _x[i] = bone.X;
                _y[i] = bone.Y;
                _rotation[i] = bone.Rotation;
                _scaleX[i] = bone.ScaleX;
                _scaleY[i] = bone.ScaleY;
            }
        }

        // Bones come parent-first, so world transforms resolve in one pass (Spine's Bone.updateWorldTransform).
        private void UpdateWorld()
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                var bone = _bones[i];
                float x = _x[i], y = _y[i];
                if (bone.Parent >= 0)
                {
                    int p = bone.Parent;
                    _x[i] = _a[p] * x + _b[p] * y + _x[p];
                    _y[i] = _c[p] * x + _d[p] * y + _y[p];
                    if (bone.InheritScale)
                    {
                        _scaleX[i] *= _scaleX[p];
                        _scaleY[i] *= _scaleY[p];
                    }
                    if (bone.InheritRotation) _rotation[i] += _rotation[p];
                    _flipX[i] = _flipX[p] != bone.FlipX;
                    _flipY[i] = _flipY[p] != bone.FlipY;
                }
                else
                {
                    _flipX[i] = bone.FlipX;
                    _flipY[i] = bone.FlipY;
                }
                float rad = _rotation[i] * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                _a[i] = _flipX[i] ? -cos * _scaleX[i] : cos * _scaleX[i];
                _b[i] = _flipX[i] ? sin * _scaleY[i] : -sin * _scaleY[i];
                _c[i] = _flipY[i] ? -sin * _scaleX[i] : sin * _scaleX[i];
                _d[i] = _flipY[i] ? -cos * _scaleY[i] : cos * _scaleY[i];
            }
        }

        private void BuildParts()
        {
            SetupBones();
            UpdateWorld();
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Bone < 0 || slot.Bone >= _bones.Count) continue;
                var att = _skin.Find(i, slot.Attachment);
                if (att == null || att.Shape != Shape.Region) continue;
                int bone = slot.Bone;
                Parts.Add(new Part
                {
                    Slot = slot.Name,
                    Region = att.Region,
                    X = _a[bone] * att.X + _b[bone] * att.Y + _x[bone],
                    Y = _c[bone] * att.X + _d[bone] * att.Y + _y[bone],
                    Width = att.Width * att.ScaleX * _scaleX[bone],
                    Height = att.Height * att.ScaleY * _scaleY[bone],
                    Rotation = _rotation[bone] + att.Rotation,
                    Color = slot.Color * att.Color,
                    Additive = slot.Additive
                });
            }
        }

        /// <summary>
        /// What the skeleton draws at a moment of an animation, back to front, written into
        /// <paramref name="pieces"/> (whose entries and vertex arrays are reused: call it every frame with the
        /// same list). A null or unknown animation gives the setup pose. Slots faded to nothing are left out.
        /// The atlas is asked where a whitespace-stripped region sits in its attachment; null takes regions whole.
        /// </summary>
        public void Pose(string animation, float time, SpineAtlas atlas, List<Piece> pieces)
        {
            Animation anim = null;
            if (animation != null) _animations.TryGetValue(animation, out anim);

            SetupBones();
            for (int i = 0; i < _slots.Count; i++)
            {
                _colors[i] = _slots[i].Color;
                _attached[i] = _slots[i].Attachment;
                _order[i] = i;
            }
            float[] deformFrom = null, deformTo = null;
            float deformShare = 0f;
            DeformKeys deformed = null;
            if (anim != null)
            {
                foreach (var pair in anim.Rotations)
                {
                    var keys = pair.Value;
                    if (!keys.Sample(time, out var from, out var to, out var share)) continue;
                    // the short way round, as Spine turns
                    float turn = Mathf.Repeat(keys.Value(to, 0) - keys.Value(from, 0) + 180f, 360f) - 180f;
                    _rotation[pair.Key] = _bones[pair.Key].Rotation + keys.Value(from, 0) + turn * share;
                }
                foreach (var pair in anim.Translations)
                {
                    var keys = pair.Value;
                    if (!keys.Sample(time, out var from, out var to, out var share)) continue;
                    _x[pair.Key] = _bones[pair.Key].X + Mathf.LerpUnclamped(keys.Value(from, 0), keys.Value(to, 0), share);
                    _y[pair.Key] = _bones[pair.Key].Y + Mathf.LerpUnclamped(keys.Value(from, 1), keys.Value(to, 1), share);
                }
                foreach (var pair in anim.Scales)
                {
                    var keys = pair.Value;
                    if (!keys.Sample(time, out var from, out var to, out var share)) continue;
                    _scaleX[pair.Key] = _bones[pair.Key].ScaleX * Mathf.LerpUnclamped(keys.Value(from, 0), keys.Value(to, 0), share);
                    _scaleY[pair.Key] = _bones[pair.Key].ScaleY * Mathf.LerpUnclamped(keys.Value(from, 1), keys.Value(to, 1), share);
                }
                foreach (var pair in anim.Colors)
                {
                    var keys = pair.Value;
                    if (!keys.Sample(time, out var from, out var to, out var share)) continue;
                    _colors[pair.Key] = new Color(
                        Mathf.LerpUnclamped(keys.Value(from, 0), keys.Value(to, 0), share), Mathf.LerpUnclamped(keys.Value(from, 1), keys.Value(to, 1), share),
                        Mathf.LerpUnclamped(keys.Value(from, 2), keys.Value(to, 2), share), Mathf.LerpUnclamped(keys.Value(from, 3), keys.Value(to, 3), share));
                }
                foreach (var pair in anim.Attachments)
                {
                    var keys = pair.Value;
                    for (int f = keys.Times.Length - 1; f >= 0; f--)
                    {
                        if (time < keys.Times[f]) continue;
                        _attached[pair.Key] = keys.Names[f];
                        break;
                    }
                }
                foreach (var key in anim.Orders)
                {
                    if (key.Time > time) break;
                    Array.Copy(key.Order, _order, _order.Length);
                }
            }
            UpdateWorld();

            int used = 0;
            for (int o = 0; o < _order.Length; o++)
            {
                int index = _order[o];
                var slot = _slots[index];
                var att = _skin.Find(index, _attached[index]);
                if (att == null || slot.Bone < 0 || slot.Bone >= _bones.Count) continue;
                var color = _colors[index] * att.Color;
                if (color.a <= 0f) continue;

                int count = att.Shape == Shape.Region ? 4 : att.Uvs.Length;
                Piece piece;
                if (used < pieces.Count) piece = pieces[used];
                else pieces.Add(piece = new Piece());
                used++;
                if (piece.Vertices == null || piece.Vertices.Length != count) piece.Vertices = new Vector2[count];
                piece.Slot = slot.Name;
                piece.Region = att.Region;
                piece.Color = color;
                piece.Additive = slot.Additive;
                piece.Uvs = att.Shape == Shape.Region ? QuadUvs : att.Uvs;
                piece.Triangles = att.Shape == Shape.Region ? QuadTriangles : att.Triangles;

                // a deformation key moves this attachment's vertices before its bones do
                deformed = null;
                if (anim != null && att.Shape != Shape.Region)
                    foreach (var deform in anim.Deforms)
                    {
                        if (deform.Slot != index || deform.Key != att.Key) continue;
                        if (!deform.Timing.Sample(time, out var from, out var to, out deformShare)) continue;
                        deformed = deform;
                        deformFrom = deform.Deltas[from];
                        deformTo = deform.Deltas[to];
                    }

                int bone = slot.Bone;
                var vertices = piece.Vertices;
                if (att.Shape == Shape.Region) RegionQuad(att, bone, atlas?.Find(att.Region), vertices);
                else if (att.Shape == Shape.Mesh)
                {
                    for (int v = 0; v < count; v++)
                    {
                        float vx = att.Vertices[v * 2], vy = att.Vertices[v * 2 + 1];
                        if (deformed != null)
                        {
                            vx += Delta(deformFrom, deformTo, deformShare, v * 2);
                            vy += Delta(deformFrom, deformTo, deformShare, v * 2 + 1);
                        }
                        vertices[v] = new Vector2(vx * _a[bone] + vy * _b[bone] + _x[bone], vx * _c[bone] + vy * _d[bone] + _y[bone]);
                    }
                }
                else
                {
                    for (int v = 0, b = 0, w = 0, f = 0; v < count; v++)
                    {
                        float wx = 0f, wy = 0f;
                        for (int links = att.Bones[b++]; links > 0; links--, b++, w += 3, f += 2)
                        {
                            int link = att.Bones[b];
                            float vx = att.Weights[w], vy = att.Weights[w + 1], weight = att.Weights[w + 2];
                            if (deformed != null)
                            {
                                vx += Delta(deformFrom, deformTo, deformShare, f);
                                vy += Delta(deformFrom, deformTo, deformShare, f + 1);
                            }
                            wx += (vx * _a[link] + vy * _b[link] + _x[link]) * weight;
                            wy += (vx * _c[link] + vy * _d[link] + _y[link]) * weight;
                        }
                        vertices[v] = new Vector2(wx, wy);
                    }
                }
            }
            if (used < pieces.Count) pieces.RemoveRange(used, pieces.Count - used);
        }

        private static float Delta(float[] from, float[] to, float share, int index)
        {
            float a = from != null ? from[index] : 0f, b = to != null ? to[index] : 0f;
            return a + (b - a) * share;
        }

        // Spine's RegionAttachment.updateOffset: the quad covers the packed pixels of a stripped region.
        private void RegionQuad(Attachment att, int bone, SpineAtlas.Region region, Vector2[] vertices)
        {
            float x0 = -att.Width * 0.5f, y0 = -att.Height * 0.5f, x1 = att.Width * 0.5f, y1 = att.Height * 0.5f;
            if (region != null && region.OriginalWidth > 0 && region.OriginalHeight > 0)
            {
                x0 += region.OffsetX / (float)region.OriginalWidth * att.Width;
                y0 += region.OffsetY / (float)region.OriginalHeight * att.Height;
                x1 -= (region.OriginalWidth - region.OffsetX - region.Width) / (float)region.OriginalWidth * att.Width;
                y1 -= (region.OriginalHeight - region.OffsetY - region.Height) / (float)region.OriginalHeight * att.Height;
            }
            float rad = att.Rotation * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            for (int v = 0; v < 4; v++)
            {
                // bottom-left, top-left, top-right, bottom-right: the order of QuadUvs
                float lx = (v < 2 ? x0 : x1) * att.ScaleX, ly = (v == 0 || v == 3 ? y0 : y1) * att.ScaleY;
                float ax = lx * cos - ly * sin + att.X, ay = lx * sin + ly * cos + att.Y;
                vertices[v] = new Vector2(ax * _a[bone] + ay * _b[bone] + _x[bone], ax * _c[bone] + ay * _d[bone] + _y[bone]);
            }
        }

        /// <summary>Spine's binary encoding: big-endian, with libgdx variable-length ints and strings.</summary>
        private class Reader
        {
            private readonly byte[] _data;
            private readonly byte[] _four = new byte[4];
            private int _pos;

            public Reader(byte[] data) { _data = data; }

            public int Byte() => _data[_pos++];

            public bool Bool() => _data[_pos++] != 0;

            public void Skip(int bytes) { _pos += bytes; }

            public int VarInt()
            {
                int result = 0;
                for (int shift = 0; shift < 35; shift += 7)
                {
                    int b = _data[_pos++];
                    result |= (b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                }
                return result;
            }

            public float Float()
            {
                _four[0] = _data[_pos + 3];
                _four[1] = _data[_pos + 2];
                _four[2] = _data[_pos + 1];
                _four[3] = _data[_pos];
                _pos += 4;
                return BitConverter.ToSingle(_four, 0);
            }

            /// <summary>A count, then that many floats.</summary>
            public float[] Floats()
            {
                var values = new float[VarInt()];
                for (int i = 0; i < values.Length; i++) values[i] = Float();
                return values;
            }

            /// <summary>A count, then that many unsigned 16-bit numbers (triangle corners).</summary>
            public int[] Shorts()
            {
                var values = new int[VarInt()];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (_data[_pos] << 8) | _data[_pos + 1];
                    _pos += 2;
                }
                return values;
            }

            /// <summary>RGBA8888.</summary>
            public Color Color()
            {
                var c = new Color32(_data[_pos], _data[_pos + 1], _data[_pos + 2], _data[_pos + 3]);
                _pos += 4;
                return c;
            }

            /// <summary>Length + 1 as a varint, then UTF-8; length 0 is null.</summary>
            public string String()
            {
                int length = VarInt();
                if (length == 0) return null;
                var text = Encoding.UTF8.GetString(_data, _pos, length - 1);
                _pos += length - 1;
                return text;
            }
        }
    }
}
