using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.Audio;
using Assets.Code.Combat;
using Assets.Code.Utils;
using DD2Estate.Dev;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A change of the party's order, shown the way DD2's fight shows a move (CombatPresentationBhv,
    /// CombatActorBhv.MoveToRank): every hero whose rank changes plays the animator's own step for it
    /// (move_forward towards the front, move_backward away from it), goes to the new place along the game's
    /// own curve for a change of rank (half a second, fast at first and slowing into the place: the hero
    /// model carries it, CombatActorBhv.m_ChangeRankForwardCurves), and ends the step with move_complete; the
    /// game's sound of a change of position is heard once. Nobody is put anywhere at once: a hero who is on
    /// the way when the order changes again goes on from where they are.
    ///
    /// The models hang in one slot a rank (<see cref="PlaceParty"/>); a hero on the way is that slot drawn
    /// short of its place by what is left of the way. The bars under the heroes follow the models
    /// (<see cref="RaidTrays"/>), and pass through one another with them. Who passes before whom is left as
    /// the view has it (the hero nearer the front is drawn over the one behind), which is DD2's rule in a
    /// fight as well: a hero on the way back passes behind the others, one on the way to the front before them.
    /// </summary>
    internal partial class CorridorView
    {
        /// <summary>
        /// How a change of rank is shown: 0 at once (the mod until 2026-10-06); 1 the heroes walk to their places
        /// on the corridor's own legs (<see cref="HeroGait"/>); 2 DD2's step of a fight (the animator's
        /// move_forward / move_backward, move_complete on arrival).
        /// </summary>
        public static int RankMoveStyle = 2;

        /// <summary>Dev bridge: seconds a hero takes to another rank; 0: as long as the game's curve is (half a second).</summary>
        public static float RankMoveSeconds;

        /// <summary>DD2's sound of a hero changing position (AudioPathsBhv.ActorChangePosition), once a change of order.</summary>
        public static bool RankMoveSound = true;

        // FALLBACK: DD2's curve for a change of rank as its hero models carry it (read in the running game,
        // v2.04: the same two keys in every one of a hero's forward and back curves, whatever the distance):
        // the share of the way against seconds.
        private static readonly AnimationCurve StockRankCurve = new AnimationCurve(new Keyframe(0f, 0f, 4.78264427f, 4.78264427f), new Keyframe(0.5f, 1f, 0.0548378527f, 0.0548378527f));
        private static readonly FieldInfo ForwardCurves = AccessTools.Field(typeof(CombatActorBhv), "m_ChangeRankForwardCurves");
        private static readonly FieldInfo BackCurves = AccessTools.Field(typeof(CombatActorBhv), "m_ChangeRankBackCurves");

        private class RankMove
        {
            public ActorBhv Actor;
            public float From;              // where the hero stood when the order changed, from the new place (DD1 units along the line)
            public AnimationCurve Curve;    // the share of the way against the curve's own seconds
            public float Length;            // the curve's seconds
            public float Time, Seconds;
            public bool Stepping;           // the animator's step is on: move_complete is owed
        }

        // by the rank the hero is going to
        private readonly Dictionary<int, RankMove> _rankMoves = new Dictionary<int, RankMove>();

        /// <summary>True while a hero is on the way to another rank.</summary>
        public static bool RanksMoving => Instance != null && Instance._rankMoves.Count > 0;

        // Where the hero of a rank is drawn, from that rank's place.
        private float RankShift(int rank)
        {
            if (!_rankMoves.TryGetValue(rank, out var move)) return 0f;
            var done = move.Seconds > 0f ? Mathf.Clamp01(move.Time / move.Seconds) : 1f;
            return move.From * (1f - move.Curve.Evaluate(done * move.Length));
        }

        // DD2's curve for a hero who changes rank by so many places, as CombatActorBhv.MoveToRank picks it: the
        // first of the forward curves, or the back curve of that many ranks.
        private static AnimationCurve RankCurve(ActorBhv actor, bool back, int ranks)
        {
            try
            {
                var combat = actor != null ? actor.GetComponent<CombatActorBhv>() : null;
                if (combat != null && (back ? BackCurves : ForwardCurves)?.GetValue(combat) is List<AnimationCurve> curves && curves.Count > 0)
                {
                    var curve = curves[back ? Mathf.Clamp(ranks, 1, curves.Count) - 1 : 0];
                    if (curve != null && curve.length >= 2 && curve[curve.length - 1].time > 0.01f) return curve;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Corridor: the game's curve for a change of rank could not be read: " + e.Message); }
            return StockRankCurve;
        }

        // Where every model stands on the party's line now, by the model's index.
        private float[] RankPlaces()
        {
            var places = new float[_actors.Count];
            for (var i = 0; i < places.Length; i++) places[i] = _leadX - _actorRank[i] * _spacing + RankShift(_actorRank[i]);
            return places;
        }

        // The models have been given their new ranks: each sets out from where it stood.
        private void BeginRankMoves(float[] from)
        {
            EndRankMoves(false);
            if (RankMoveStyle <= 0 || !isActiveAndEnabled) return;
            for (var i = 0; i < _actors.Count && i < from.Length; i++)
            {
                var actor = _actors[i];
                if (actor == null) continue;
                var rank = _actorRank[i];
                var away = from[i] - (_leadX - rank * _spacing);
                if (Mathf.Abs(away) < 1f) continue;
                // towards the front is towards a lower rank: the place lies ahead of where the hero stands
                var back = away > 0f;
                var move = new RankMove { Actor = actor, From = away, Curve = RankCurve(actor, back, Mathf.RoundToInt(Mathf.Abs(away) / Mathf.Max(1f, _spacing))) };
                move.Length = move.Curve[move.Curve.length - 1].time;
                move.Seconds = RankMoveSeconds > 0f ? RankMoveSeconds : move.Length;
                if (RankMoveStyle >= 2)
                {
                    try { move.Stepping = actor.AttemptAnimatorTrigger(back ? "move_backward" : "move_forward"); }
                    catch (Exception e) { Plugin.Log.LogWarning("Corridor: the game's step for a change of rank could not be played: " + e.Message); }
                    // its weapons are looked after from now on (CorridorWeaponGuard.cs)
                    StirWeaponGuard(actor);
                }
                _rankMoves[rank] = move;
            }
            if (_rankMoves.Count == 0 || !RankMoveSound) return;
            try { SingletonMonoBehaviour<AudioMgr>.Instance.Play(AudioPathsBhv.ActorChangePosition, transform); }
            catch (Exception e) { Plugin.Log.LogWarning("Corridor: the game's sound of a change of position could not be played: " + e.Message); }
        }

        // Every frame, before the party is placed.
        private void TickRankMoves(float dt)
        {
            if (_rankMoves.Count == 0) return;
            List<int> arrived = null;
            foreach (var pair in _rankMoves)
            {
                pair.Value.Time += dt;
                if (pair.Value.Time >= pair.Value.Seconds) (arrived ?? (arrived = new List<int>())).Add(pair.Key);
            }
            if (arrived == null) return;
            foreach (var rank in arrived)
            {
                Arrived(_rankMoves[rank]);
                _rankMoves.Remove(rank);
            }
        }

        private static void Arrived(RankMove move)
        {
            if (!move.Stepping || move.Actor == null) return;
            try { move.Actor.AttemptAnimatorTrigger("move_complete"); }
            catch (Exception e) { Plugin.Log.LogWarning("Corridor: the game's step for a change of rank could not be ended: " + e.Message); }
        }

        // Everybody stands in their place at once (the models are going away, or another order has come).
        private void EndRankMoves(bool modelsGone)
        {
            if (!modelsGone)
                foreach (var move in _rankMoves.Values) Arrived(move);
            _rankMoves.Clear();
        }

        // The walk's own legs stand still under a hero who steps as in a fight.
        private bool StepsAsInAFight(int rank) => RankMoveStyle >= 2 && _rankMoves.TryGetValue(rank, out var move) && move.Stepping;

        /// <summary>Dev bridge: who is on the way to which rank.</summary>
        internal object RankMoveState()
        {
            var moves = new List<object>();
            foreach (var pair in _rankMoves)
                moves.Add(new
                {
                    rank = pair.Key + 1, hero = pair.Value.Actor != null ? pair.Value.Actor.GetActorGuid() : 0u, from = pair.Value.From, shift = RankShift(pair.Key),
                    time = pair.Value.Time, seconds = pair.Value.Seconds, stepping = pair.Value.Stepping, gameCurve = !ReferenceEquals(pair.Value.Curve, StockRankCurve)
                });
            var places = new List<object>();
            for (var i = 0; i < _actors.Count; i++)
                if (_actors[i] != null) places.Add(new { hero = _actors[i].GetActorGuid(), rank = _actorRank[i] + 1, x = _leadX - _actorRank[i] * _spacing + RankShift(_actorRank[i]) });
            return new { moving = _rankMoves.Count > 0, style = RankMoveStyle, seconds = RankMoveSeconds, sound = RankMoveSound, moves, places };
        }
    }

    /// <summary>
    /// Dev bridge: corridor.ranks {} who stands where and who is on the way; {"style":0|1|2} how a change of rank
    /// is shown (<see cref="CorridorView.RankMoveStyle"/>), {"seconds":0.8} how long it takes (0: the game's own
    /// half second), {"sound":false} without the game's sound. The order itself is changed by party.order.
    /// </summary>
    [EstateModule]
    internal static class CorridorRanksDev
    {
        private static void Register()
        {
            AgentBridge.Register("corridor.ranks", o =>
            {
                if (o["style"] != null) CorridorView.RankMoveStyle = (int)o["style"];
                if (o["seconds"] != null) CorridorView.RankMoveSeconds = (float)o["seconds"];
                if (o["sound"] != null) CorridorView.RankMoveSound = (bool)o["sound"];
                return CorridorView.Instance != null ? CorridorView.Instance.RankMoveState() : "no corridor";
            });
        }
    }
}
