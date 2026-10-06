using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Resist;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The DD2 side of the districts' class buffs (<see cref="DistrictRules.HeroEffect"/>): every hero of the
    /// estate whose class a standing building favours carries one stats container with the DD2 stats the
    /// buffs map to, hung off ActorInstance.ActorData the way the Blacksmith hangs gear. DD1 gives these buffs
    /// for good, so they are on the hero in town as on an expedition. The game does not save that tree: the
    /// containers are put back when a save is loaded (with the health the hero was saved with, which the game
    /// has by then clamped to the maximum without them) and kept in step once a second with what stands and
    /// who serves (a building built, a recruit hired).
    ///
    /// DD1's scouting buff is no hero stat here: each living party member's share goes into the expedition's
    /// scouting chance, as the camping buffs' does, and is taken back out when the hero falls.
    /// </summary>
    internal static class DistrictBuffs
    {
        private const string StatsId = "estate_district";

        private class Attached
        {
            public ActorInstance Actor;
            public ActorDataStats Stats;
            public string Text;
        }

        private static readonly Dictionary<uint, Attached> OnHero = new Dictionary<uint, Attached>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(ActorInstance), "m_Hp");
        private static float _nextCheck;
        // The expedition whose scouting chance carries the districts' share, and how much of it that is: both
        // travel in the save beside the exploration's own number, which has the share in it.
        private static long _scoutSeed;
        private static double _scoutShare;

        // ---- keeping in step -----------------------------------------------------------------------------

        public static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            Sync();
        }

        /// <summary>Brings every hero's container and the expedition's scouting in step with the buildings that stand.</summary>
        public static void Sync()
        {
            try
            {
                SyncHeroes(null);
                SyncScouting();
            }
            catch (Exception e) { Plugin.Log.LogError("Districts: the heroes' buffs could not be kept up: " + e); }
        }

        /// <summary>The session's heroes are gone (a new estate, the main menu): nothing to take anything off.</summary>
        public static void Forget()
        {
            OnHero.Clear();
            _scoutSeed = 0;
            _scoutShare = 0;
        }

        /// <summary>For tests: takes everything off again while the heroes are still there (the districts of a new estate, mid-session).</summary>
        public static void TakeOff()
        {
            foreach (var attached in OnHero.Values)
            {
                try
                {
                    if (attached.Actor == null || !attached.Actor.IsLiving) continue;
                    attached.Actor.ActorData.RemoveChild(attached.Stats);
                    attached.Actor.RefreshStats();
                    attached.Actor.UpdatePreviousHpMax();
                }
                catch (Exception e) { Plugin.Log.LogWarning("Districts: a buff could not be taken off: " + e.Message); }
            }
            var run = DungeonRun.Current;
            if (run != null && run.Exploration.Map.Seed == _scoutSeed) run.Exploration.ScoutBonus -= _scoutShare;
            Forget();
        }

        // The stats text a hero of this class carries; empty for none.
        private static string StatsText(string classId)
        {
            return Districts.Available ? Checked(CampingBuffMap.Text(Districts.Rules.HeroEffect(Districts.State, classId).Lines)) : "";
        }

        // savedHealth: only while a save is being loaded (hero -> the health they were saved with).
        private static void SyncHeroes(Dictionary<uint, float> savedHealth)
        {
            if (!SingletonMonoBehaviour<Library<uint, ActorInstance>>.HasInstance())
            {
                OnHero.Clear();
                return;
            }
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            var living = RosterLifecycle.LivingGuids();
            // the fallen and the dismissed take their container with them
            foreach (var guid in new List<uint>(OnHero.Keys))
                if (!living.Contains(guid)) OnHero.Remove(guid);
            foreach (var guid in living)
            {
                var actor = library.GetLibraryElement(guid);
                if (actor == null || !actor.IsLiving) continue;
                var saved = -1f;
                var loading = savedHealth != null;
                if (loading) savedHealth.TryGetValue(guid, out saved);
                Set(actor, StatsText(Districts.ClassId(actor)), loading, loading && savedHealth.ContainsKey(guid) ? saved : -1f);
            }
        }

        // Swaps what the hero carries for this. The source is CLASS, as for the Blacksmith's gear: the damage
        // calculation sums stats per source (ActorInstance.GetAddStatValuesBySource) and needs every container
        // to have one.
        private static void Set(ActorInstance actor, string text, bool loading, float savedHealth)
        {
            var guid = actor.ActorGuid;
            if (OnHero.TryGetValue(guid, out var old))
            {
                if (old.Text == text && ReferenceEquals(old.Actor, actor)) return;
                if (ReferenceEquals(old.Actor, actor)) actor.ActorData.RemoveChild(old.Stats);
                OnHero.Remove(guid);
            }
            else if (text.Length == 0) return;

            if (text.Length > 0)
            {
                var stats = new ActorDataStats(StatsId, text);
                stats.Init(StatsId + "_" + guid);
                stats.SetSource(SourceType.CLASS, StatsId);
                actor.ActorData.AddChild(stats);
                OnHero[guid] = new Attached { Actor = actor, Stats = stats, Text = text };
                Plugin.Log.LogInfo("Districts: " + actor.ActorName + " (" + Districts.ClassId(actor) + ") carries " + text.Trim().Replace("\n", " | "));
            }
            if (loading)
            {
                // as the Blacksmith does for its gear: the health of the save, now that the maximum is whole again
                if (savedHealth > actor.HpRaw && HealthField != null) HealthField.SetValue(actor, Math.Min(savedHealth, actor.CurrentHpMax));
            }
            // The game's own way to take a changed maximum: health keeps its share of it (ActorInstance.RefreshStats).
            else actor.RefreshStats();
            actor.UpdatePreviousHpMax();
        }

        // A stat or a resistance this game build does not know would make the container's reader log an error
        // for the line on every rebuild: such a line is dropped, with one warning.
        private static string Checked(string text)
        {
            if (text.Length == 0) return text;
            var kept = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                var cells = line.Split(',');
                var known = cells.Length >= 3 && CustomEnum<ActorStatType>.Cast(cells[1]) != null;
                if (known && cells[0] == "sub_stat" && (cells[1] == CampingBuffMap.Resistance || cells[1] == DistrictRules.ResistanceIgnore))
                    known = cells.Length >= 4 && SingletonMonoBehaviour<Library<string, ResistDefinition>>.HasInstance()
                            && SingletonMonoBehaviour<Library<string, ResistDefinition>>.Instance.GetHasLibraryKey(cells[2]);
                if (known) kept.Append(line).Append('\n');
                else if (Reported.Add(line)) Plugin.Log.LogWarning("Districts: this game build has no hero stat for '" + line + "'; the buff line is left out");
            }
            return kept.ToString();
        }

        // ---- scouting ------------------------------------------------------------------------------------

        // DD1's scouting_chance on a hero is the party's chance to scout while the hero walks with it.
        private static void SyncScouting()
        {
            var run = DungeonRun.Current;
            if (run == null || !EstateSession.Active) return;
            var x = run.Exploration;
            if (x.Map.Seed != _scoutSeed)
            {
                // another expedition: nothing of this one's chance is the districts' yet
                _scoutSeed = x.Map.Seed;
                _scoutShare = 0;
            }
            var share = 0.0;
            if (Districts.Available)
                foreach (var hero in Provisioning.Party())
                    share += Districts.Rules.HeroEffect(Districts.State, Districts.ClassId(hero)).Scouting;
            if (Math.Abs(share - _scoutShare) < 1e-9) return;
            x.ScoutBonus += share - _scoutShare;
            Plugin.Log.LogInfo("Districts: the party's scouting chance carries " + (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + " points of the districts'");
            _scoutShare = share;
        }

        // ---- save ----------------------------------------------------------------------------------------

        public static JToken Save()
        {
            var health = new JObject();
            foreach (var pair in OnHero)
                if (pair.Value.Actor != null && pair.Value.Actor.IsLiving) health[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value.Actor.HpRaw;
            return new JObject { ["hp"] = health, ["scout_seed"] = _scoutSeed.ToString(CultureInfo.InvariantCulture), ["scout_share"] = _scoutShare };
        }

        public static void Load(JToken token)
        {
            Forget();
            var saved = new Dictionary<uint, float>();
            if (token is JObject json)
            {
                if (json["hp"] is JObject health)
                    foreach (var property in health.Properties())
                        if (uint.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var guid)) saved[guid] = (float?)property.Value ?? -1f;
                if (long.TryParse((string)json["scout_seed"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) _scoutSeed = seed;
                _scoutShare = (double?)json["scout_share"] ?? 0;
            }
            try { SyncHeroes(saved); }
            catch (Exception e) { Plugin.Log.LogError("Districts: the heroes' buffs could not be restored: " + e); }
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe()
        {
            var heroes = new List<object>();
            foreach (var pair in OnHero)
            {
                var actor = pair.Value.Actor;
                heroes.Add(new
                {
                    guid = pair.Key,
                    hero = actor != null ? actor.ActorName : null,
                    cls = Districts.ClassId(actor),
                    stats = pair.Value.Text.Trim().Replace("\n", " | "),
                    health = actor != null ? actor.HpRaw + " / " + actor.CurrentHpMax : null
                });
            }
            return new { heroes, scoutSeed = _scoutSeed, scoutShare = _scoutShare };
        }
    }
}
