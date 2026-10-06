using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Test commands for the heroes' walk (<see cref="HeroGait"/>), for the dev bridge: its numbers, any class's
    /// model put into the view on screen, and the walk measured over its cycle. tools/gait_bench.py goes through
    /// every hero class with them.
    /// </summary>
    [EstateModule]
    internal static class CorridorGaitDev
    {
        // heroes made for a look at their class's walk: gone again with {"clear":true}
        private static readonly List<uint> Temporary = new List<uint>();

        private static void Register()
        {
            // The walk's numbers: {"stride":2.56,"duty":0.55,"step":0.33,"lift":0.13,"heel":0.07,"heelStrike":14,
            // "toeOff":32,"bob":0.02,"reach":0.97,"facingMin":30,"facingMax":50,"kneeOut":0.45,"hipTwist":3,"lean":4,
            // "spread":0.7,"blend":0.16}; {"hold":0.25} the cycle stopped at that point (to look at it), {"hold":-1}
            // lets it go. {"cls":"crusader","straighten":0.65,"facing":10,"stepScale":0.8,"swing":0.7}: what one class differs by (HeroGait.Styles).
            AgentBridge.Register("corridor.gait", o =>
            {
                if (o["cls"] != null)
                {
                    var id = (string)o["cls"];
                    if (!HeroGait.Styles.TryGetValue(id, out var style)) HeroGait.Styles[id] = style = new HeroGait.Style();
                    if (o["straighten"] != null) style.Straighten = (float)o["straighten"];
                    if (o["facing"] != null) style.Facing = (float)o["facing"];
                    if (o["stepScale"] != null) style.Step = (float)o["stepScale"];
                    if (o["swing"] != null) style.Swing = (float)o["swing"];
                }
                if (o["stride"] != null) HeroGait.Stride = (float)o["stride"];
                if (o["duty"] != null) HeroGait.Duty = (float)o["duty"];
                if (o["step"] != null) HeroGait.MaxStep = (float)o["step"];
                if (o["lift"] != null) HeroGait.Lift = (float)o["lift"];
                if (o["heel"] != null) HeroGait.HeelRise = (float)o["heel"];
                if (o["heelStrike"] != null) HeroGait.HeelStrike = (float)o["heelStrike"];
                if (o["toeOff"] != null) HeroGait.ToeOff = (float)o["toeOff"];
                if (o["bob"] != null) HeroGait.Bob = (float)o["bob"];
                if (o["reach"] != null) HeroGait.Reach = (float)o["reach"];
                if (o["facingMin"] != null) HeroGait.FacingMin = (float)o["facingMin"];
                if (o["facingMax"] != null) HeroGait.FacingMax = (float)o["facingMax"];
                if (o["kneeOut"] != null) HeroGait.KneeOut = (float)o["kneeOut"];
                if (o["hipTwist"] != null) HeroGait.HipTwist = (float)o["hipTwist"];
                if (o["lean"] != null) HeroGait.Lean = (float)o["lean"];
                if (o["spread"] != null) HeroGait.Spread = (float)o["spread"];
                if (o["blend"] != null) HeroGait.BlendTime = (float)o["blend"];
                if (o["hold"] != null) HeroGait.HoldPhase = (float)o["hold"];
                // what hangs beside the pelvis goes with it: {"carry":false} the walk as it was; the names taken and left
                if (o["carry"] != null || o["carrySkip"] != null)
                {
                    if (o["carry"] != null) HeroGait.Carry = (bool)o["carry"];
                    if (o["carrySkip"] != null) HeroGait.CarrySkip = (string)o["carrySkip"];
                    CorridorView.Instance?.ForgetGaits();
                }
                return new
                {
                    carry = HeroGait.Carry, carrySkip = HeroGait.CarrySkip,
                    walk = CorridorView.WalkStyle, stride = HeroGait.Stride, duty = HeroGait.Duty, step = HeroGait.MaxStep, lift = HeroGait.Lift, heel = HeroGait.HeelRise,
                    heelStrike = HeroGait.HeelStrike, toeOff = HeroGait.ToeOff, bob = HeroGait.Bob, reach = HeroGait.Reach, facingMin = HeroGait.FacingMin, facingMax = HeroGait.FacingMax,
                    kneeOut = HeroGait.KneeOut, hipTwist = HeroGait.HipTwist, lean = HeroGait.Lean, spread = HeroGait.Spread, blend = HeroGait.BlendTime, hold = HeroGait.HoldPhase,
                    styles = HeroGait.Styles.ToDictionary(p => p.Key, p => new { straighten = p.Value.Straighten, facing = p.Value.Facing, stepScale = p.Value.Step, swing = p.Value.Swing }),
                    heroes = CorridorView.Instance != null ? CorridorView.Instance.GaitState() : null
                };
            });

            // Other heroes in the view instead of the party (their models only; the expedition goes on with its own):
            // {"guids":[3,17]} heroes of the roster, {"classes":["jester","vestal"]} one of each class (from the
            // roster, or made for the look and gone with the next "clear"), {"clear":true} the party again.
            AgentBridge.Register("corridor.cast", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
                if ((bool?)o["clear"] == true)
                {
                    CorridorView.Cast = null;
                    view.Recast();
                    var gone = Temporary.Count(RosterLifecycle.RemoveForTest);
                    Temporary.Clear();
                    return "the party stands in the view again; " + gone + " heroes made for the look are gone";
                }
                // {"reverse":true}: the heroes shown change places, as the party's order changes (the first to the last rank)
                if ((bool?)o["reverse"] == true)
                {
                    if (CorridorView.Cast == null) return "no cast";
                    var turned = new List<uint>(CorridorView.Cast);
                    turned.Reverse();
                    CorridorView.Cast = turned;
                    view.Reorder();
                    return turned;
                }
                var cast = new List<uint>();
                if (o["guids"] is JArray guids) cast.AddRange(guids.Select(g => (uint)g));
                if (o["classes"] is JArray classes)
                {
                    var roster = RosterLifecycle.Roster;
                    foreach (var cls in classes.Select(c => (string)c))
                    {
                        var have = RosterLifecycle.LivingGuids().FirstOrDefault(g => roster.GetReadOnlyRosterEntryByActorGuid(g)?.ActorClassId == cls && !cast.Contains(g));
                        if (have == 0u)
                        {
                            // any path nobody of the class walks yet
                            foreach (var path in HeroPaths.Of(cls))
                            {
                                have = RosterLifecycle.CreateHero(cls, null, false, path);
                                if (have != 0u) break;
                            }
                            if (have == 0u) return "no " + cls + " could be made";
                            Temporary.Add(have);
                        }
                        cast.Add(have);
                    }
                }
                if (cast.Count == 0) return "guids, classes or clear";
                CorridorView.Cast = cast;
                // (a frame after the old models are gone: see RecastLater)
                view.RecastLater();
                return cast.Select(g => new { guid = g, name = actors.GetLibraryElement(g)?.ActorName, made = Temporary.Contains(g) }).ToList();
            });

            // The walk of every hero in the view measured over its cycle ({"points":16}): see HeroGait.Check.
            AgentBridge.Register("corridor.gaitcheck", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                return view.GaitCheck((int?)o["points"] ?? 16).Select(r => new
                {
                    cls = r.Class, ready = r.Ready, measured = r.Measured, scale = r.Scale, poseFacing = r.PoseFacing, walkFacing = r.WalkFacing,
                    stanceKnee = r.StanceKnee, swingKnee = r.SwingKnee, idleKnee = r.IdleKnee, maxRise = r.MaxRise, ankleHeight = r.AnkleHeight, footSlope = r.FootSlope, maxDrop = r.MaxDrop,
                    maxMiss = r.MaxMiss, minKneeAhead = r.MinKneeAhead, maxToeOff = r.MaxToeOff, minBall = r.MinBall, maxStretch = r.MaxStretch,
                    hasBall = r.HasBall, hasFront = r.HasFront, frontGuessed = r.FrontGuessed, faults = r.Faults,
                    points = (bool?)o["detail"] == true ? r.Points : null
                }).ToList();
            });

            // What every hero's rig hangs beside the pelvis and on the legs, and how far the walk parts each from the
            // body over its cycle ({"points":16}; model units and degrees; 0 for what is carried): see
            // HeroGait.CheckHold. {"all":true} also what moves no mesh.
            AgentBridge.Register("corridor.hold", o =>
            {
                var view = CorridorView.Instance;
                if (view == null) return "no corridor";
                var all = (bool?)o["all"] == true;
                return view.HoldCheck((int?)o["points"] ?? 16).Select(h => new
                {
                    cls = h.Class, ready = h.Ready, model = h.Model, pelvis = h.Pelvis,
                    beside = h.Beside.Where(s => all || s.Moves.Count > 0).Select(s => new { bone = s.Bone, parent = s.Parent, shift = s.Shift, turn = s.Turn, carried = s.Carried, moves = s.Moves }).ToList(),
                    onLegs = h.OnLegs
                }).ToList();
            });
        }
    }
}
