using System.Collections.Generic;
using Assets.Code.Game;
using Assets.Code.Roster;
using Assets.Code.Utils;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A hero's walk. DD2's heroes ride a coach: their models have no walk cycle (the animators hold fighting
    /// idles, skill poses, a one-off step for a change of rank). Every hero rig is built on one skeleton
    /// (ROOTSHJnt, l_/r_Leg_Hip/Knee/Ankle/BallSHJnt; two rigs put their own prefix before the names:
    /// MAA_ROOTSHJnt, pd_ROOTSHJnt), so the walk is made here, the same for all of them: after
    /// the animator has posed the model for the frame, the legs are taken through a step cycle (two-bone IK to a
    /// foot path) and the pelvis rides over them, turns with them and leans; the fighting stance walks.
    /// Nothing between the pelvis and the hands is moved on its own: the rigs hang their weapons in different
    /// places (the Highwayman's dagger and pistol on bones in the hands, the Hellion's glaive and the Crusader's
    /// sword on bones of the pelvis that the hands are animated to meet), and a spine or an arm turned by itself
    /// takes the hands off them. Three rigs hang a weapon on joints beside the pelvis (the Duelist's foil, the
    /// Abomination's chain, the Bounty Hunter's rope): those are taken along with it (<see cref="CarrySet"/>).
    /// The body stays turned towards the camera as DD1's painted heroes are. The cycle
    /// advances with the distance the hero has covered; walking back runs it the other way (DD1's heroes back away
    /// facing forward).
    /// All lengths are in model units (a hero is about 1.45 tall), all angles in degrees.
    /// </summary>
    internal class HeroGait
    {
        /// <summary>
        /// DD1's walk animation (heroes/*/anim/*.sprite.walk.skel) lasts 1.067 s for its two steps, the same for
        /// every hero, and plays at that rate whatever the ground covered (tools/dd1_walk_cycle.py): at DD1's
        /// walking speed of 400 a second that is 427 units a cycle (2.56 of the model's), with feet that reach
        /// some 55 ahead and behind (0.33). The feet slide, as DD1's do.
        /// </summary>
        public const float Dd1WalkSeconds = 1.0667f, Dd1Step = 55f;

        // ---- tunables (static: set through the bridge, corridor.gait) ----------------------------------------
        /// <summary>Ground covered by a full cycle (two steps).</summary>
        public static float Stride = 2.56f;
        /// <summary>The share of the cycle a foot is on the ground.</summary>
        public static float Duty = 0.55f;
        /// <summary>The furthest a foot is put ahead of the hip (a longer stride slides the feet instead).</summary>
        public static float MaxStep = 0.33f;
        /// <summary>How high the foot swings.</summary>
        public static float Lift = 0.13f;
        /// <summary>How far the heel rises before the foot leaves the ground.</summary>
        public static float HeelRise = 0.07f;
        /// <summary>Toe up at the heel strike, toe down at the push-off.</summary>
        public static float HeelStrike = 14f, ToeOff = 32f;
        /// <summary>The steepest the line from ankle to ball may get (90: the toe straight down).</summary>
        public static float MaxFootSlope = 78f;
        /// <summary>The pelvis rides up and down by this much each step.</summary>
        public static float Bob = 0.02f;
        /// <summary>The share of the leg's length it is stretched to at the most (the pelvis comes down to keep it).</summary>
        public static float Reach = 0.97f;
        /// <summary>
        /// How far the pelvis, and the body on it, is turned from the walk towards the camera while walking
        /// (degrees: 0 in profile, 90 facing the camera). A pose inside the two keeps its own turn (the fighting
        /// idles stand about three quarters to the camera, as DD2 shows its fights); one that stands squarer to the
        /// camera, or in profile, is brought to the nearer of them.
        /// </summary>
        public static float FacingMin = 30f, FacingMax = 50f;
        /// <summary>Where the knees and toes point: 0 along the walk, 1 where the pelvis faces.</summary>
        public static float KneeOut = 0.45f;
        /// <summary>The pelvis, and all the body on it, turns with the legs by this much; the lean into the walk.</summary>
        public static float HipTwist = 3f, Lean = 4f;
        /// <summary>How far apart the feet are put down, as a share of the hips' width.</summary>
        public static float Spread = 0.7f;
        /// <summary>Seconds from the idle's legs to the walk's and back.</summary>
        public static float BlendTime = 0.16f;
        /// <summary>Dev bridge: the cycle held at this point (0..1) at full weight; below 0: as the hero walks.</summary>
        public static float HoldPhase = -1f;
        /// <summary>
        /// The joints of a rig that hang beside the pelvis and carry a mesh go with it (see <see cref="CarrySet"/>).
        /// Off: the walk as it was before (a weapon on such a joint stays where the animator put it while the body
        /// turns and rides: the Duelist's foil, the Abomination's chain, the Bounty Hunter's rope).
        /// </summary>
        public static bool Carry = true;
        /// <summary>A joint beside the pelvis is left where it is when its name matches this (an effect's own little rig).</summary>
        public static string CarrySkip = "(?i)^vfx";

        /// <summary>What one class's walk differs by.</summary>
        public class Style
        {
            /// <summary>
            /// The share of the room its legs leave it that the pelvis rises by while walking (0: it stays at the
            /// idle's height; 1: as high as the cycle's longest reach allows). A fighting idle with a wide, low
            /// stance walks in a crouch otherwise: the feet come in under a pelvis that stays low.
            /// </summary>
            public float Straighten;
            /// <summary>Added to <see cref="FacingMax"/>: the class turns that much less from its idle into the walk.</summary>
            public float Facing;
            /// <summary>On <see cref="MaxStep"/>: a shorter reach leaves the legs straighter (the feet slide more).</summary>
            public float Step = 1f;
            /// <summary>On <see cref="Lift"/> and <see cref="HeelRise"/>: a lower swing bends the knee less.</summary>
            public float Swing = 1f;
        }

        /// <summary>
        /// By class id. The user's, after looking at every class walking (2026-10-05): the Hellion, the Leper, the
        /// Vestal and the Crusader bent their knees too much; the Vestal and the Abomination turned too far from
        /// their idle (10 degrees less).
        /// </summary>
        public static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>
        {
            { "hellion", Upright() },
            { "leper", Upright() },
            { "crusader", Upright() },
            // she stands on one straight leg: the pelvis has no room to rise, the steps are shortened further instead
            { "vestal", new Style { Straighten = 1f, Step = 0.6f, Swing = 0.5f, Facing = 10f } },
            { "abomination", new Style { Facing = 10f } }
        };

        // Measured (tools/gait_knees.py; the knee's angle at its most bent, on the ground / in the air; 180 is a
        // straight leg): Crusader 98/76 before, 122/109 with this; Leper 95/76 -> 118/110; Hellion 108/87 -> 126/115;
        // Vestal 113/96 -> 124/115. The other classes stand at 106..118 / 76..102.
        private static Style Upright()
        {
            return new Style { Straighten = 1f, Step = 0.7f, Swing = 0.6f };
        }

        private static readonly Style Plain = new Style();
        private const int CyclePoints = 16;

        private class Kept
        {
            public Transform Bone;
            public bool Position;
            public Quaternion Rotation, Written;
            public Vector3 At, WrittenAt;
            public bool Wrote;
        }

        private class Leg
        {
            public Transform Hip, Knee, Ankle, Ball;
            // the side of the thigh and of the shin the knee points to, in each bone's own space
            public Vector3 ThighFront, ShinFront;
            public bool HasFront;
            // taken from where the foot points: the idle stands on straight legs
            public bool FrontGuessed;
            // the foot as it stands on the ground in the idle, against the floor it stands on: which way it points
            // and the ankle's rotation then; how steeply the ball lies below the ankle (a raised heel is steeper)
            public Vector3 FootHeading;
            public Quaternion FootRotation;
            public float FootSlope;
            public bool HasFoot;
            // the frame's work, for the check
            public Vector3 Target;
        }

        private readonly string _class;
        private Transform _model;
        private Transform _pelvis;
        private readonly Leg[] _legs = { new Leg(), new Leg() };
        private readonly List<Kept> _kept = new List<Kept>();
        // what hangs beside the pelvis and is taken along with it
        private readonly List<Kept> _carried = new List<Kept>();
        private float _nextSearch;
        private float _ankleHeight = -1f;

        private float _phase;
        private float _weight;
        private Vector3 _lastAt;
        private bool _hasLast;
        private float _still;
        // the frame's work, for the check
        private Vector3 _stepWay;
        private float _poseFacing, _drop, _rise;

        public HeroGait(string classId)
        {
            _class = classId ?? "";
        }

        public string Class => _class;

        // The size of the model against its own units (the bones themselves are scaled to a hundredth: the rigs
        // are in centimetres).
        private float Scale => _model != null ? Mathf.Abs(_model.lossyScale.y) : 0f;

        /// <summary>0 standing .. 1 walking.</summary>
        public float Weight => _weight;
        public float Phase => _phase;
        public bool Ready => _pelvis != null;
        /// <summary>The rig has been seen standing: its feet and ankle height are known.</summary>
        public bool Measured => _ankleHeight > 0f && (_legs[0].Ball == null || _legs[0].HasFoot) && (_legs[1].Ball == null || _legs[1].HasFoot);

        /// <summary>
        /// Once a frame, after the animator. <paramref name="floor"/> is where the hero stands (its scale is the
        /// hero's), forward where it walks, side its left.
        /// </summary>
        public void Apply(Transform actor, Transform floor, Vector3 forward, Vector3 up, Vector3 side, float dt, float offset, bool on)
        {
            if (actor == null || floor == null) return;
            if (!Find(actor)) return;
            Restore();

            // the ground covered since the last frame (a jump is a new area, not a walk)
            var at = floor.position;
            var moved = _hasLast ? Vector3.Dot(at - _lastAt, forward) : 0f;
            _lastAt = at;
            _hasLast = true;
            var scale = Scale;
            if (scale < 1e-4f || !on)
            {
                _weight = 0f;
                return;
            }
            if (Mathf.Abs(moved) > 0.6f) moved = 0f;
            var walking = dt > 0f && Mathf.Abs(moved) / dt > 0.12f;
            _still = walking ? 0f : _still + dt;
            _phase = Mathf.Repeat(_phase + moved / (Mathf.Max(0.2f, Stride) * scale), 1f);
            // a frame without movement (a hitch) does not stop the walk
            var wanted = walking || _still < 0.05f && _weight > 0f ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, wanted, dt / Mathf.Max(0.01f, BlendTime));

            var phase = Mathf.Repeat(_phase + offset, 1f);
            var weight = _weight;
            if (HoldPhase >= 0f)
            {
                phase = Mathf.Repeat(HoldPhase + offset, 1f);
                weight = 1f;
            }
            if (weight <= 0.001f)
            {
                Measure(floor, up);
                return;
            }
            // the pelvis as the animator has it: what hangs beside it is taken along by what the walk does to it
            var pelvisAt = _pelvis.position;
            var pelvisTurn = _pelvis.rotation;
            Pose(floor, forward, up, side, phase, moved >= 0f, scale);

            // ---- between the idle's pose and the walk's ----
            foreach (var kept in _kept)
            {
                if (kept.Bone == null) continue;
                if (weight < 0.999f)
                {
                    kept.Bone.localRotation = Quaternion.Slerp(kept.Rotation, kept.Bone.localRotation, weight);
                    if (kept.Position) kept.Bone.localPosition = Vector3.Lerp(kept.At, kept.Bone.localPosition, weight);
                }
                kept.Written = kept.Bone.localRotation;
                kept.WrittenAt = kept.Bone.localPosition;
                kept.Wrote = true;
            }
            TakeAlong(pelvisAt, pelvisTurn);
        }

        // Three rigs hang a weapon on joints beside the pelvis, not under it: the Duelist's foil (j_rapier), the
        // Abomination's chain (chain_a_skin_jnt1..16) and the Bounty Hunter's coil of rope (rope_skirt_shjnt1..22)
        // are children of the trajectory joint, the pelvis's own parent. DD2's clips move them in the model's space
        // to where the hand or the hip is. The walk turns the pelvis into the camera's bounds (the Duelist's by 27
        // degrees, the Abomination's by 30), lets it ride over the legs and lean, and the body goes with it: the
        // joints beside it stayed, and the weapon left the hand by a seventh of the hero's height (measured:
        // corridor.hold). They are moved as the pelvis was moved from where the animator had it, as one piece
        // with the body: standing, walking on and back, and through the blend between the two.
        private void TakeAlong(Vector3 pelvisAt, Quaternion pelvisTurn)
        {
            if (_carried.Count == 0) return;
            var turn = _pelvis.rotation * Quaternion.Inverse(pelvisTurn);
            var at = _pelvis.position;
            foreach (var kept in _carried)
            {
                if (kept.Bone == null) continue;
                kept.Bone.SetPositionAndRotation(at + turn * (kept.Bone.position - pelvisAt), turn * kept.Bone.rotation);
                kept.Written = kept.Bone.localRotation;
                kept.WrittenAt = kept.Bone.localPosition;
                kept.Wrote = true;
            }
        }

        /// <summary>
        /// What of a rig hangs beside the pelvis and carries a mesh: the children of the pelvis's own forebears
        /// inside the model (the trajectory joint, the joint group) that are not on the pelvis's own line, and to
        /// which, or to something under which, a skinned mesh is bound. Much else hangs there and is left alone:
        /// the places DD2's interface reads (pop_text_loc, stamp_loc, hit_root), effects and their planes on the
        /// ground (vfx_*; <see cref="CarrySkip"/> leaves an effect that has a rig of its own), the meshes' own
        /// objects.
        /// </summary>
        public static List<Transform> CarrySet(Transform model, Transform pelvis)
        {
            var set = new List<Transform>();
            if (model == null || pelvis == null) return set;
            var beside = new HashSet<Transform>();
            var below = pelvis;
            for (var parent = pelvis.parent; parent != null && below != model; below = parent, parent = parent.parent)
                for (var i = 0; i < parent.childCount; i++)
                    if (parent.GetChild(i) != below) beside.Add(parent.GetChild(i));
            if (beside.Count == 0) return set;
            var skip = string.IsNullOrEmpty(CarrySkip) ? null : new System.Text.RegularExpressions.Regex(CarrySkip);
            var taken = new HashSet<Transform>();
            foreach (var mesh in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var joint in mesh.bones)
                    for (var t = joint; t != null && t != model; t = t.parent)
                    {
                        if (!beside.Contains(t)) continue;
                        if ((skip == null || !skip.IsMatch(t.name)) && taken.Add(t)) set.Add(t);
                        break;
                    }
            return set;
        }

        // The walk's pose at one point of the cycle, laid over what the animator gave.
        private void Pose(Transform floor, Vector3 forward, Vector3 up, Vector3 side, float phase, bool ahead, float scale)
        {
            // ---- the pelvis: turned to the camera within the bounds, leaning, riding over the legs ----
            var style = Styles.TryGetValue(_class, out var own) ? own : Plain;
            var across = Vector3.ProjectOnPlane(_legs[0].Hip.position - _legs[1].Hip.position, up);
            var facing = forward;       // where the pelvis looks, on the ground
            var turn = 0f;
            var toCamera = Mathf.Sign(Vector3.SignedAngle(forward, -side, up));     // the sense of a turn about "up" towards the camera
            _poseFacing = 0f;
            if (across.sqrMagnitude > 1e-8f)
            {
                facing = Vector3.Cross(up, across).normalized;
                _poseFacing = toCamera * Vector3.SignedAngle(forward, facing, up);
                var most = FacingMax + style.Facing;
                var bounded = Mathf.Clamp(_poseFacing, Mathf.Min(FacingMin, most), most);
                turn = toCamera * (bounded - _poseFacing);
                facing = Quaternion.AngleAxis(turn, up) * facing;
            }
            var swing = Mathf.Cos(phase * 2f * Mathf.PI);       // +1: the left foot is put down ahead
            var lean = ahead ? Lean : 0f;
            _pelvis.rotation = Quaternion.AngleAxis(-lean, side) * Quaternion.AngleAxis(turn + HipTwist * swing, up) * _pelvis.rotation;
            // lowest as a foot lands, highest as the other passes under
            float BobAt(float at) => -Bob * Mathf.Cos(at * 4f * Mathf.PI) * scale;
            _pelvis.position += up * BobAt(phase);
            // the knees and toes: between the walk's line and where the pelvis faces
            var stepWay = Vector3.Slerp(forward, facing, Mathf.Clamp01(KneeOut)).normalized;
            var stepSide = Vector3.Cross(stepWay, up).normalized;
            _stepWay = stepWay;

            // ---- where the feet go ----
            var step = Mathf.Min(Mathf.Clamp01(Duty) * Stride * 0.5f, MaxStep * Mathf.Clamp(style.Step, 0.2f, 1.5f)) * scale;
            var swingScale = Mathf.Clamp(style.Swing, 0f, 1.5f);
            var ground = Vector3.Dot(floor.position, up);
            var ankleHeight = _ankleHeight > 0f ? _ankleHeight : 0.12f * scale;
            var middle = (_legs[0].Hip.position + _legs[1].Hip.position) * 0.5f;
            var reach = new float[2];
            for (var i = 0; i < 2; i++)
                reach[i] = ((_legs[i].Knee.position - _legs[i].Hip.position).magnitude + (_legs[i].Ankle.position - _legs[i].Knee.position).magnitude) * Mathf.Clamp(Reach, 0.5f, 0.999f);

            // where leg i's ankle goes at a point of the cycle
            Vector3 TargetAt(int i, float at, out float toe)
            {
                var hip = _legs[i].Hip.position;
                Foot(Mathf.Repeat(at + i * 0.5f, 1f), out var along, out var lift, out toe);
                var x = Vector3.Dot(Vector3.Lerp(middle, hip, 0.5f), forward) + along * step;
                var z = Vector3.Dot(middle, side) + Vector3.Dot(hip - middle, side) * Spread;
                return forward * x + side * z + up * (ground + ankleHeight + lift * swingScale * scale);
            }

            // how far up the hips could go at that point before a leg were stretched to its reach (below 0: how far
            // down they must come for the feet to be within it)
            float RoomAt(float at)
            {
                var least = float.MaxValue;
                for (var i = 0; i < 2; i++)
                {
                    var to = TargetAt(i, at, out _) - _legs[i].Hip.position;
                    var down = -Vector3.Dot(to, up);
                    var flat = to.sqrMagnitude - down * down;
                    if (flat < reach[i] * reach[i]) least = Mathf.Min(least, Mathf.Sqrt(reach[i] * reach[i] - flat) - down);
                }
                return least;
            }

            var toes = new float[2];
            for (var i = 0; i < 2; i++) _legs[i].Target = TargetAt(i, phase, out toes[i]);

            // The pelvis keeps the idle's height, and comes down where a foot would be out of reach. A class that is
            // to walk on straighter legs has it raised by a share of the room the cycle's tightest moment leaves: a
            // height held through the whole cycle (raised by each moment's own room it would dip at one foot's
            // landing more than at the other's, the hips being turned to the camera: a limp).
            var rise = 0f;
            var straighten = Mathf.Clamp01(style.Straighten);
            if (straighten > 0f)
            {
                var least = float.MaxValue;
                // (the hips are where this moment's bob has them: each point is given its own)
                for (var k = 0; k < CyclePoints; k++)
                {
                    var at = k / (float)CyclePoints;
                    var there = RoomAt(at);
                    if (there != float.MaxValue) least = Mathf.Min(least, there + BobAt(phase) - BobAt(at));
                }
                if (least != float.MaxValue && least > 0f) rise = least * straighten;
            }
            var room = RoomAt(phase);
            if (room != float.MaxValue && room < rise) rise = room;
            _drop = Mathf.Max(0f, -rise);
            _rise = Mathf.Max(0f, rise);
            if (rise != 0f) _pelvis.position += up * rise;

            for (var i = 0; i < 2; i++) Solve(_legs[i], floor, stepWay, up, stepSide, toes[i]);
        }

        // One foot's path over the cycle: ahead of the hip in steps (-1..1), off the ground (model units), the toe's
        // pitch (degrees, down positive).
        private static void Foot(float p, out float ahead, out float lift, out float toe)
        {
            var duty = Mathf.Clamp(Duty, 0.2f, 0.8f);
            if (p < duty)
            {
                // on the ground: carried back under the body; the heel comes off towards the end
                var s = p / duty;
                ahead = 1f - 2f * s;
                var off = Mathf.SmoothStep(0f, 1f, (s - 0.6f) / 0.4f);
                lift = HeelRise * off;
                toe = s < 0.25f ? Mathf.Lerp(-HeelStrike, 0f, Mathf.SmoothStep(0f, 1f, s / 0.25f)) : ToeOff * off;
            }
            else
            {
                // in the air: swung forward
                var s = (p - duty) / (1f - duty);
                ahead = -1f + 2f * Mathf.SmoothStep(0f, 1f, s);
                lift = HeelRise * (1f - s) * (1f - s) + Lift * Mathf.Sin(s * Mathf.PI);
                toe = Mathf.Lerp(ToeOff, -HeelStrike, Mathf.SmoothStep(0f, 1f, s));
            }
        }

        // Two-bone IK: the hip is where the pelvis has it, the ankle goes to the target, the knee bends the way the
        // step goes; the foot is the idle's foot on the ground, turned to the step and pitched.
        private static void Solve(Leg leg, Transform floor, Vector3 stepWay, Vector3 up, Vector3 stepSide, float toeDown)
        {
            var hip = leg.Hip.position;
            var thigh = (leg.Knee.position - hip).magnitude;
            var shin = (leg.Ankle.position - leg.Knee.position).magnitude;
            if (thigh < 1e-5f || shin < 1e-5f) return;
            var to = leg.Target - hip;
            var distance = Mathf.Clamp(to.magnitude, Mathf.Abs(thigh - shin) + 0.01f * (thigh + shin), (thigh + shin) * 0.999f);
            var direction = to.sqrMagnitude > 1e-10f ? to.normalized : -up;
            var bend = stepWay - direction * Vector3.Dot(stepWay, direction);
            if (bend.sqrMagnitude < 1e-6f) bend = up - direction * Vector3.Dot(up, direction);
            bend.Normalize();
            var cos = Mathf.Clamp((thigh * thigh + distance * distance - shin * shin) / (2f * thigh * distance), -1f, 1f);
            var sin = Mathf.Sqrt(1f - cos * cos);
            var kneeAt = hip + (direction * cos + bend * sin) * thigh;
            var ankleAt = hip + direction * distance;

            leg.Hip.rotation = Quaternion.FromToRotation(leg.Knee.position - hip, kneeAt - hip) * leg.Hip.rotation;
            if (leg.HasFront) Twist(leg.Hip, kneeAt - hip, leg.Hip.rotation * leg.ThighFront, bend);
            var knee = leg.Knee.position;
            leg.Knee.rotation = Quaternion.FromToRotation(leg.Ankle.position - knee, ankleAt - knee) * leg.Knee.rotation;
            if (leg.HasFront) Twist(leg.Knee, ankleAt - knee, leg.Knee.rotation * leg.ShinFront, bend);

            if (!leg.HasFoot) return;
            // The foot is not aimed from where the leg has left it: a fighting stance may have it pointing sideways
            // or back (the Crusader's), and the shortest turn from there to the step's way goes over the toe, sole
            // up. It is the idle's own foot on the ground, turned about the upright to the step, then pitched.
            var heading = floor.TransformDirection(leg.FootHeading);
            var yaw = Vector3.SignedAngle(heading, stepWay, up);
            // a foot whose ball lies steeply under the ankle (the Bounty Hunter's, the Grave Robber's) is not
            // pitched past the upright at the push-off
            var pitch = Mathf.Min(toeDown, Mathf.Max(0f, MaxFootSlope - leg.FootSlope));
            leg.Ankle.rotation = Quaternion.AngleAxis(-pitch, stepSide) * Quaternion.AngleAxis(yaw, up) * (floor.rotation * leg.FootRotation);
        }

        // Turns a bone about its own length until its front looks where it should.
        private static void Twist(Transform bone, Vector3 axis, Vector3 front, Vector3 wanted)
        {
            axis.Normalize();
            var from = Vector3.ProjectOnPlane(front, axis);
            var to = Vector3.ProjectOnPlane(wanted, axis);
            if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(from, to, axis), axis) * bone.rotation;
        }

        // What the rig tells about itself while the hero stands in its idle: which way each knee bends, how each
        // foot stands on the ground, how high the ankle is.
        private void Measure(Transform floor, Vector3 up)
        {
            var lowest = float.MaxValue;
            var flattest = float.MaxValue;
            foreach (var leg in _legs)
            {
                var hip = leg.Hip.position;
                var knee = leg.Knee.position;
                var ankle = leg.Ankle.position;
                var line = (ankle - hip).normalized;
                if (!leg.HasFront || leg.FrontGuessed)
                {
                    var bend = (knee - hip) - line * Vector3.Dot(knee - hip, line);
                    // a knee bent enough to tell (a sixth of the thigh's length out of the line)
                    if (bend.magnitude > 0.16f * (knee - hip).magnitude)
                    {
                        bend.Normalize();
                        leg.ThighFront = Quaternion.Inverse(leg.Hip.rotation) * bend;
                        leg.ShinFront = Quaternion.Inverse(leg.Knee.rotation) * bend;
                        leg.HasFront = true;
                        leg.FrontGuessed = false;
                    }
                }
                if (leg.Ball != null)
                {
                    var toe = leg.Ball.position - ankle;
                    var below = -Vector3.Dot(toe, up);
                    var along = toe + up * below;
                    if (along.sqrMagnitude > 1e-10f)
                    {
                        leg.FootHeading = floor.InverseTransformDirection(along.normalized);
                        leg.FootRotation = Quaternion.Inverse(floor.rotation) * leg.Ankle.rotation;
                        leg.FootSlope = Mathf.Atan2(below, along.magnitude) * Mathf.Rad2Deg;
                        leg.HasFoot = true;
                        flattest = Mathf.Min(flattest, leg.FootSlope);
                        // an idle on straight legs (the Grave Robber's) does not show which way the knee bends:
                        // it bends where the foot points
                        var ahead = along - line * Vector3.Dot(along, line);
                        if (!leg.HasFront && ahead.sqrMagnitude > 1e-10f)
                        {
                            ahead.Normalize();
                            leg.ThighFront = Quaternion.Inverse(leg.Hip.rotation) * ahead;
                            leg.ShinFront = Quaternion.Inverse(leg.Knee.rotation) * ahead;
                            leg.HasFront = leg.FrontGuessed = true;
                        }
                    }
                }
                lowest = Mathf.Min(lowest, Vector3.Dot(ankle - floor.position, up));
            }
            // a stance with a heel off the ground (a fencer's back foot): that foot is laid flat as the other one is
            foreach (var leg in _legs)
            {
                if (!leg.HasFoot || leg.FootSlope - flattest < 0.5f) continue;
                var heading = floor.TransformDirection(leg.FootHeading);
                var raised = Quaternion.AngleAxis(leg.FootSlope - flattest, Vector3.Cross(heading, up).normalized) * (floor.rotation * leg.FootRotation);
                leg.FootRotation = Quaternion.Inverse(floor.rotation) * raised;
                leg.FootSlope = flattest;
            }
            var scale = Scale;
            if (lowest < float.MaxValue && scale > 1e-4f) _ankleHeight = Mathf.Clamp(lowest, 0.07f * scale, 0.17f * scale);
        }

        // A bone the animator did not pose this frame still holds what was written to it the frame before: it is
        // given back what it had, so that nothing adds up.
        private void Restore()
        {
            Restore(_kept);
            Restore(_carried);
        }

        private static void Restore(List<Kept> bones)
        {
            foreach (var kept in bones)
            {
                if (kept.Bone == null) continue;
                var rotation = kept.Bone.localRotation;
                if (kept.Wrote && Same(rotation, kept.Written)) kept.Bone.localRotation = kept.Rotation;
                else kept.Rotation = rotation;
                if (kept.Position)
                {
                    var at = kept.Bone.localPosition;
                    if (kept.Wrote && at == kept.WrittenAt) kept.Bone.localPosition = kept.At;
                    else kept.At = at;
                }
                kept.Wrote = false;
            }
        }

        private static bool Same(Quaternion a, Quaternion b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        }

        // The model loads after its actor is made and may be made again: the bones are looked for until found,
        // and again when they are gone.
        private bool Find(Transform actor)
        {
            if (_pelvis != null && _model != null) return true;
            if (Time.unscaledTime < _nextSearch) return false;
            _nextSearch = Time.unscaledTime + 0.5f;
            _kept.Clear();
            _carried.Clear();
            _pelvis = null;
            _hasLast = false;
            _weight = 0f;
            var bones = new Dictionary<string, Transform>();
            foreach (var bone in actor.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);
            // by the usual name, or by it behind a rig's own prefix (MAA_l_Leg_HipSHJnt, pd_ROOTSHJnt)
            Transform Bone(string name)
            {
                if (bones.TryGetValue(name, out var exact)) return exact;
                foreach (var pair in bones)
                    if (pair.Key.EndsWith("_" + name)) return pair.Value;
                return null;
            }
            var pelvis = Bone("ROOTSHJnt");
            if (pelvis == null) return false;
            for (var i = 0; i < 2; i++)
            {
                var prefix = i == 0 ? "l_" : "r_";
                var leg = _legs[i];
                leg.HasFront = leg.HasFoot = leg.FrontGuessed = false;
                leg.Hip = Bone(prefix + "Leg_HipSHJnt");
                leg.Knee = Bone(prefix + "Leg_KneeSHJnt");
                leg.Ankle = Bone(prefix + "Leg_AnkleSHJnt");
                leg.Ball = Bone(prefix + "Leg_BallSHJnt");
                if (leg.Hip == null || leg.Knee == null || leg.Ankle == null) return false;
            }
            _pelvis = pelvis;
            var animator = pelvis.GetComponentInParent<Animator>();
            _model = animator != null ? animator.transform : pelvis.parent;
            _ankleHeight = -1f;

            Keep(_pelvis, true);
            for (var i = 0; i < 2; i++)
            {
                Keep(_legs[i].Hip, false);
                Keep(_legs[i].Knee, false);
                Keep(_legs[i].Ankle, false);
            }
            if (Carry)
                foreach (var bone in CarrySet(_model, _pelvis))
                    _carried.Add(new Kept { Bone = bone, Position = true, Rotation = bone.localRotation, At = bone.localPosition });
            return true;
        }

        /// <summary>Dev: the rig is looked for anew (after <see cref="Carry"/> or its names have changed).</summary>
        public void Forget()
        {
            Restore();
            _pelvis = null;
            _nextSearch = 0f;
        }

        private void Keep(Transform bone, bool position)
        {
            _kept.Add(new Kept { Bone = bone, Position = position, Rotation = bone.localRotation, At = bone.localPosition });
        }

        // ---- the check ---------------------------------------------------------------------------------------

        /// <summary>What a rig's walk measures at the worst point of its cycle, and what is wrong with it.</summary>
        public class Report
        {
            public bool Ready, Measured;
            public string Class;
            public float Scale, PoseFacing, WalkFacing, AnkleHeight, FootSlope, MaxDrop, MaxRise, MaxMiss, MinKneeAhead, MaxToeOff, MinBall, MaxStretch;
            // the knee's angle (180: a straight leg) at its most bent, for a leg on the ground and for one in the air,
            // and in the idle the hero stands in
            public float StanceKnee, SwingKnee, IdleKnee;
            // per point of the cycle: the left and the right knee's angle, the pelvis's rise (+) or drop (-)
            public List<float[]> Points = new List<float[]>();
            public bool HasBall, HasFront, FrontGuessed;
            public List<string> Faults = new List<string>();
        }

        /// <summary>
        /// Dev: the walk's pose at <paramref name="points"/> points of the cycle, measured: do the ankles get where
        /// they are sent, do the knees bend the way of the step, do the toes point that way, do the feet stay on
        /// the ground. The bones are left as the animator gave them. For a hero that stands.
        /// </summary>
        public Report Check(Transform actor, Transform floor, Vector3 forward, Vector3 up, Vector3 side, int points)
        {
            var report = new Report { Ready = Find(actor), Class = _class };
            if (!report.Ready)
            {
                report.Faults.Add("the rig has no ROOTSHJnt or leg bones of the usual names");
                return report;
            }
            Restore();
            var scale = Scale;
            report.Scale = scale;
            report.Measured = Measured;
            report.HasBall = _legs[0].Ball != null && _legs[1].Ball != null;
            report.HasFront = _legs[0].HasFront && _legs[1].HasFront;
            report.FrontGuessed = _legs[0].FrontGuessed || _legs[1].FrontGuessed;
            report.FootSlope = Mathf.Max(_legs[0].FootSlope, _legs[1].FootSlope);
            if (!report.Measured) report.Faults.Add("not seen standing yet (no ankle height or feet)");
            if (!report.HasBall) report.Faults.Add("no ball bones: the feet keep the shins' turn");
            if (!report.HasFront) report.Faults.Add("nothing tells which way the knees bend: no twist");
            report.AnkleHeight = _ankleHeight / Mathf.Max(1e-4f, scale);
            report.MinKneeAhead = float.MaxValue;
            report.MinBall = float.MaxValue;
            report.StanceKnee = report.SwingKnee = report.IdleKnee = 180f;
            foreach (var leg in _legs)
                report.IdleKnee = Mathf.Min(report.IdleKnee, Vector3.Angle(leg.Hip.position - leg.Knee.position, leg.Ankle.position - leg.Knee.position));
            var ground = Vector3.Dot(floor.position, up);
            for (var n = 0; n < points; n++)
            {
                Pose(floor, forward, up, side, n / (float)points, true, scale);
                report.PoseFacing = _poseFacing;
                report.MaxDrop = Mathf.Max(report.MaxDrop, _drop / scale);
                report.MaxRise = Mathf.Max(report.MaxRise, _rise / scale);
                report.Points.Add(new[]
                {
                    Vector3.Angle(_legs[0].Hip.position - _legs[0].Knee.position, _legs[0].Ankle.position - _legs[0].Knee.position),
                    Vector3.Angle(_legs[1].Hip.position - _legs[1].Knee.position, _legs[1].Ankle.position - _legs[1].Knee.position),
                    (_rise - _drop) / scale
                });
                if (n == points / 4)
                {
                    // a quarter into the cycle the hips are square to where the pelvis faces (no twist with the legs)
                    var hips = Vector3.ProjectOnPlane(_legs[0].Hip.position - _legs[1].Hip.position, up);
                    report.WalkFacing = Mathf.Sign(Vector3.SignedAngle(forward, -side, up)) * Vector3.SignedAngle(forward, Vector3.Cross(up, hips), up);
                }
                for (var i = 0; i < 2; i++)
                {
                    var leg = _legs[i];
                    var hip = leg.Hip.position;
                    var knee = leg.Knee.position;
                    var ankle = leg.Ankle.position;
                    report.MaxMiss = Mathf.Max(report.MaxMiss, (ankle - leg.Target).magnitude / scale);
                    report.MinKneeAhead = Mathf.Min(report.MinKneeAhead, Vector3.Dot(knee - (hip + ankle) * 0.5f, _stepWay) / scale);
                    report.MaxStretch = Mathf.Max(report.MaxStretch, (ankle - hip).magnitude / Mathf.Max(1e-5f, (knee - hip).magnitude + (ankle - knee).magnitude));
                    var bent = Vector3.Angle(hip - knee, ankle - knee);
                    if (Mathf.Repeat(n / (float)points + i * 0.5f, 1f) < Mathf.Clamp(Duty, 0.2f, 0.8f)) report.StanceKnee = Mathf.Min(report.StanceKnee, bent);
                    else report.SwingKnee = Mathf.Min(report.SwingKnee, bent);
                    if (leg.Ball == null || !leg.HasFoot) continue;
                    // the foot's own forward (the way it pointed on the ground in the idle), as it lies now
                    var own = Quaternion.Inverse(floor.rotation * leg.FootRotation) * floor.TransformDirection(leg.FootHeading);
                    report.MaxToeOff = Mathf.Max(report.MaxToeOff, Vector3.Angle(Vector3.ProjectOnPlane(leg.Ankle.rotation * own, up), _stepWay));
                    report.MinBall = Mathf.Min(report.MinBall, (Vector3.Dot(leg.Ball.position, up) - ground) / scale);
                }
                // back to what the animator gave, for the next point
                foreach (var kept in _kept)
                {
                    if (kept.Bone == null) continue;
                    kept.Bone.localRotation = kept.Rotation;
                    if (kept.Position) kept.Bone.localPosition = kept.At;
                }
            }
            if (report.MaxMiss > 0.03f) report.Faults.Add("an ankle misses its place by " + report.MaxMiss.ToString("0.00"));
            if (report.MinKneeAhead < 0.005f) report.Faults.Add("a knee does not bend the way of the step (" + report.MinKneeAhead.ToString("0.000") + ")");
            if (report.HasBall && report.MaxToeOff > 3f) report.Faults.Add("a foot points " + report.MaxToeOff.ToString("0") + " degrees off the step");
            if (report.HasBall && report.MinBall < -0.03f) report.Faults.Add("a foot goes " + (-report.MinBall).ToString("0.00") + " under the floor");
            if (report.MaxDrop > 0.2f) report.Faults.Add("the pelvis comes down by " + report.MaxDrop.ToString("0.00") + " to reach the steps");
            return report;
        }

        /// <summary>A part of a rig beside the pelvis, and how far the walk leaves it behind the body.</summary>
        public class Slip
        {
            public string Bone, Parent;
            /// <summary>The most it leaves its place against the pelvis over the cycle: model units, degrees.</summary>
            public float Shift, Turn;
            public bool Carried;
            /// <summary>The meshes it moves (skinned to it or to what hangs on it, or hanging on it themselves).</summary>
            public List<string> Moves = new List<string>();
        }

        /// <summary>What a rig hangs where the walk can part it from the body.</summary>
        public class Hold
        {
            public bool Ready;
            public string Class, Model, Pelvis;
            public List<Slip> Beside = new List<Slip>();
            /// <summary>What hangs on the legs besides the legs' own joints (it goes where the steps take the leg).</summary>
            public List<string> OnLegs = new List<string>();
        }

        /// <summary>
        /// Dev: the walk's pose at <paramref name="points"/> points of the cycle, and everything of the model that
        /// hangs beside the pelvis measured against it: what does not go with the body. The bones are left as the
        /// animator gave them. For a hero that stands.
        /// </summary>
        public Hold CheckHold(Transform actor, Transform floor, Vector3 forward, Vector3 up, Vector3 side, int points)
        {
            var hold = new Hold { Ready = Find(actor), Class = _class };
            if (!hold.Ready) return hold;
            Restore();
            var scale = Mathf.Max(1e-4f, Scale);
            hold.Model = _model.name;
            hold.Pelvis = _pelvis.name;
            var skinned = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshes = actor.GetComponentsInChildren<MeshRenderer>(true);
            List<string> Moved(Transform bone)
            {
                var names = new List<string>();
                foreach (var mesh in skinned)
                {
                    var moves = false;
                    foreach (var joint in mesh.bones)
                        if (joint != null && joint.IsChildOf(bone)) { moves = true; break; }
                    if (moves) names.Add(mesh.name + (mesh.gameObject.activeInHierarchy && mesh.enabled ? "" : " (off)"));
                }
                foreach (var mesh in meshes)
                    if (mesh.transform.IsChildOf(bone)) names.Add(mesh.name + (mesh.gameObject.activeInHierarchy && mesh.enabled ? "" : " (off)"));
                return names;
            }

            // everything beside the pelvis's own line, whatever its name
            var beside = new List<Transform>();
            var below = _pelvis;
            for (var parent = _pelvis.parent; parent != null && below != _model; below = parent, parent = parent.parent)
                for (var i = 0; i < parent.childCount; i++)
                    if (parent.GetChild(i) != below) beside.Add(parent.GetChild(i));
            var at = new Vector3[beside.Count];
            var turned = new Quaternion[beside.Count];
            var back = Quaternion.Inverse(_pelvis.rotation);
            for (var i = 0; i < beside.Count; i++)
            {
                at[i] = back * (beside[i].position - _pelvis.position);
                turned[i] = back * beside[i].rotation;
                var slip = new Slip { Bone = beside[i].name, Parent = beside[i].parent.name, Moves = Moved(beside[i]) };
                foreach (var kept in _carried) slip.Carried |= kept.Bone == beside[i];
                hold.Beside.Add(slip);
            }
            for (var n = 0; n < points; n++)
            {
                var pelvisAt = _pelvis.position;
                var pelvisTurn = _pelvis.rotation;
                Pose(floor, forward, up, side, n / (float)points, true, scale);
                TakeAlong(pelvisAt, pelvisTurn);
                back = Quaternion.Inverse(_pelvis.rotation);
                for (var i = 0; i < beside.Count; i++)
                {
                    hold.Beside[i].Shift = Mathf.Max(hold.Beside[i].Shift, (back * (beside[i].position - _pelvis.position) - at[i]).magnitude / scale);
                    hold.Beside[i].Turn = Mathf.Max(hold.Beside[i].Turn, Quaternion.Angle(back * beside[i].rotation, turned[i]));
                }
                // back to what the animator gave, for the next point
                foreach (var list in new[] { _kept, _carried })
                    foreach (var kept in list)
                    {
                        if (kept.Bone == null) continue;
                        kept.Bone.localRotation = kept.Rotation;
                        if (kept.Position) kept.Bone.localPosition = kept.At;
                        kept.Wrote = false;
                    }
            }
            foreach (var leg in _legs)
            {
                var own = new[] { leg.Hip, leg.Knee, leg.Ankle, leg.Ball };
                for (var j = 0; j < 3; j++)
                {
                    if (own[j] == null) continue;
                    for (var i = 0; i < own[j].childCount; i++)
                    {
                        var child = own[j].GetChild(i);
                        if (child == own[j + 1]) continue;
                        var moved = Moved(child);
                        hold.OnLegs.Add(own[j].name + "/" + child.name + (moved.Count > 0 ? " -> " + string.Join(", ", moved) : ""));
                    }
                }
            }
            return hold;
        }
    }

    internal partial class CorridorView
    {
        /// <summary>
        /// DD2 turns its heroes a quarter round in a fight (the models are made facing away from the camera); the
        /// corridor does the same, so that they face the way they walk as DD1's do.
        /// </summary>
        public static float HeroYaw = 90f;

        /// <summary>
        /// Dev (corridor.cast): the heroes shown instead of the party, to look at any class's walk without an
        /// expedition of its own; null: the party.
        /// </summary>
        public static List<uint> Cast;

        private readonly List<HeroGait> _gaits = new List<HeroGait>();

        /// <summary>The walk styles that move the legs.</summary>
        private static bool Stepping => WalkStyle >= 4;

        /// <summary>Whose models stand in the view, front to back.</summary>
        private static IList<uint> Standing()
        {
            return Cast != null ? Cast : (IList<uint>)Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY);
        }

        /// <summary>Dev: the models are made anew (after <see cref="Cast"/> has changed).</summary>
        internal void Recast()
        {
            if (_scene.Root == null) return;
            ClearParty();
            BuildParty();
        }

        /// <summary>
        /// Dev (corridor.cast): as <see cref="Recast"/>, the new models a frame after the old ones are gone. A hero
        /// outside the party holds the prefab of its model only through the model itself (ActorBhv.Destroy keeps a
        /// party member's handle and lets any other's go): a model made in the frame its forerunner is destroyed
        /// in could be left without art ("The Object you want to instantiate is null" out of ActorBhv.AddResource,
        /// and DD2 files a crash report). The party's own models are not at risk: the game keeps their prefabs.
        /// </summary>
        internal void RecastLater()
        {
            if (_scene.Root == null) return;
            ClearParty();
            StartCoroutine(BuildPartyNext());
        }

        private System.Collections.IEnumerator BuildPartyNext()
        {
            yield return null;
            yield return null;
            if (_party != null || _scene.Root == null) yield break;
            BuildParty();
            SetLeadX(_leadX, notify: false);
        }

        // After the animators: the walk is laid over the frame's pose.
        private void LateUpdate()
        {
            if (_scene.Root == null) return;
            var root = _scene.Root;
            for (var i = 0; i < _actors.Count && i < _gaits.Count; i++)
            {
                var actor = _actors[i];
                if (actor == null) continue;
                var rank = _actorRank[i];
                if (rank >= _slots.Count || _slots[rank] == null) continue;
                try
                {
                    // (a hero who changes rank with the game's own step keeps the animator's legs: CorridorRanks.cs)
                    _gaits[i].Apply(actor.transform, _slots[rank], root.right, root.up, root.forward, Time.deltaTime, rank * 0.31f, Stepping && !StepsAsInAFight(rank));
                }
                catch (System.Exception e)
                {
                    // cosmetic: never let it stop the view
                    Plugin.Log.LogWarning("Corridor: the walk could not be played: " + e.Message);
                    WalkStyle = 2;
                }
            }
        }

        /// <summary>Dev bridge: each hero's walk, leader first.</summary>
        internal object GaitState()
        {
            var list = new List<object>();
            foreach (var gait in _gaits) list.Add(new { ready = gait.Ready, measured = gait.Measured, weight = gait.Weight, phase = gait.Phase });
            return list;
        }

        /// <summary>Dev bridge: each hero's walk measured over its cycle, leader first (see <see cref="HeroGait.Check"/>).</summary>
        internal List<HeroGait.Report> GaitCheck(int points)
        {
            var reports = new List<HeroGait.Report>();
            if (_scene.Root == null) return reports;
            var root = _scene.Root;
            for (var i = 0; i < _actors.Count && i < _gaits.Count; i++)
            {
                var rank = _actorRank[i];
                if (_actors[i] == null || rank >= _slots.Count || _slots[rank] == null) continue;
                reports.Add(_gaits[i].Check(_actors[i].transform, _slots[rank], root.right, root.up, root.forward, points));
            }
            return reports;
        }

        /// <summary>Dev bridge: what each hero's rig hangs beside the pelvis and on the legs, and how far the walk parts it from the body (see <see cref="HeroGait.CheckHold"/>).</summary>
        internal List<HeroGait.Hold> HoldCheck(int points)
        {
            var holds = new List<HeroGait.Hold>();
            if (_scene.Root == null) return holds;
            var root = _scene.Root;
            for (var i = 0; i < _actors.Count && i < _gaits.Count; i++)
            {
                var rank = _actorRank[i];
                if (_actors[i] == null || rank >= _slots.Count || _slots[rank] == null) continue;
                holds.Add(_gaits[i].CheckHold(_actors[i].transform, _slots[rank], root.right, root.up, root.forward, points));
            }
            return holds;
        }

        /// <summary>Dev bridge: every rig is looked for anew.</summary>
        internal void ForgetGaits()
        {
            foreach (var gait in _gaits) gait.Forget();
        }
    }
}
