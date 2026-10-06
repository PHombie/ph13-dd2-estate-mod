using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Combat;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Roster;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using HarmonyLib;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The DD2 side of an expedition's camping buffs (<see cref="CampBuffLedger"/>): every hero who carries
    /// any gets one stats container with the DD2 stats the buffs map to (<see cref="CampingBuffMap"/>), hung
    /// off ActorInstance.ActorData the way the Blacksmith hangs gear; a buff that stands for one of DD2's own
    /// effect containers gets that container hung beside it (the way DD2's buffs hang theirs, BuffContainer.
    /// ActivateBuff). The game does not save that tree, so it is rebuilt from the ledger whenever the ledger
    /// changes, before every fight (a rank rule is answered by where the hero stands then) and after a load.
    /// </summary>
    internal static class CampBuffs
    {
        private const string StatsId = "estate_camp";

        private class Attached
        {
            public ActorInstance Actor;
            public ActorDataStats Stats;
            public readonly List<ActorDataEffects> Effects = new List<ActorDataEffects>();
            /// <summary>The stats text and the effect ids: what the hero was given.</summary>
            public string Key;
            public string Text;
        }

        private class Wanted
        {
            public string Text = "";
            public List<string> Effects = new List<string>();

            public string Key => Text + "|" + string.Join(",", Effects);
            public bool Empty => Text.Length == 0 && Effects.Count == 0;
        }

        private static readonly Dictionary<uint, Attached> OnHero = new Dictionary<uint, Attached>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>Brings the heroes' containers in step with the ledger; a null ledger takes them all off.</summary>
        public static void Sync(CampBuffLedger ledger)
        {
            try
            {
                // The session is gone (back at the main menu) and its heroes with it: nothing to take anything off.
                if (!EstateSession.Active || !SingletonMonoBehaviour<Library<uint, ActorInstance>>.HasInstance())
                {
                    OnHero.Clear();
                    return;
                }
                var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
                var roster = Singleton<GameTypeMgr>.Instance.RosterManager;
                IReadOnlyList<uint> party = new List<uint>();
                if (roster != null) party = roster.GetActorGuids(RosterStatusType.PARTY);
                var wanted = new Dictionary<uint, Wanted>();
                if (ledger != null && ledger.Any)
                {
                    for (var rank = 0; rank < party.Count; rank++)
                    {
                        var want = new Wanted
                        {
                            Text = CheckedStats(ledger.StatsText(CampContent.Rules, party[rank], rank)),
                            Effects = CheckedEffects(ledger.EffectIds(CampContent.Rules, party[rank]))
                        };
                        if (!want.Empty) wanted[party[rank]] = want;
                    }
                }
                foreach (var guid in new List<uint>(OnHero.Keys))
                    if (!wanted.ContainsKey(guid)) Set(library.GetLibraryElement(guid), guid, new Wanted());
                foreach (var pair in wanted) Set(library.GetLibraryElement(pair.Key), pair.Key, pair.Value);
            }
            catch (Exception e) { Plugin.Log.LogError("Camp: the camping buffs could not be put on the heroes: " + e); }
        }

        /// <summary>What a hero carries right now: the stats container's own text, then the effect containers; empty when nothing.</summary>
        public static string TextOn(uint guid)
        {
            if (!OnHero.TryGetValue(guid, out var attached)) return "";
            var text = attached.Text;
            foreach (var effects in attached.Effects) text += "effects," + effects.Id + "\n";
            return text;
        }

        // Swaps what the hero carries for this. The stats container's source is CLASS, as for the Blacksmith's
        // gear: the damage calculation sums stats per source (ActorInstance.GetAddStatValuesBySource) and needs
        // every container to have one. DD2's own effect containers come with theirs.
        private static void Set(ActorInstance actor, uint guid, Wanted want)
        {
            if (OnHero.TryGetValue(guid, out var old))
            {
                if (old.Key == want.Key && ReferenceEquals(old.Actor, actor)) return;
                if (actor != null && ReferenceEquals(old.Actor, actor))
                {
                    if (old.Stats != null) actor.ActorData.RemoveChild(old.Stats);
                    foreach (var effects in old.Effects) actor.ActorData.RemoveChild(effects);
                }
                OnHero.Remove(guid);
            }
            else if (want.Empty) return;
            if (actor == null) return;
            if (!want.Empty)
            {
                var attached = new Attached { Actor = actor, Key = want.Key, Text = want.Text };
                if (want.Text.Length > 0)
                {
                    attached.Stats = new ActorDataStats(StatsId, want.Text);
                    attached.Stats.Init(StatsId + "_" + guid);
                    attached.Stats.SetSource(SourceType.CLASS, StatsId);
                    actor.ActorData.AddChild(attached.Stats);
                }
                var library = SingletonMonoBehaviour<Library<string, ActorDataEffects>>.Instance;
                foreach (var id in want.Effects)
                {
                    var effects = library.GetLibraryElement(id);
                    if (effects == null) continue;
                    actor.ActorData.AddChild(effects);
                    attached.Effects.Add(effects);
                }
                OnHero[guid] = attached;
            }
            // The game's own way to take changed stats: health keeps its share of a changed maximum.
            actor.RefreshStats();
            actor.UpdatePreviousHpMax();
        }

        // A stat this game build does not know would make the container's reader log an error for every line
        // on every rebuild: such a line is dropped, with one warning.
        private static string CheckedStats(string text)
        {
            if (text.Length == 0) return text;
            var kept = new System.Text.StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                var cells = line.Split(',');
                if (cells.Length >= 3 && CustomEnum<ActorStatType>.Cast(cells[1]) != null) kept.Append(line).Append('\n');
                else if (Reported.Add(cells.Length > 1 ? cells[1] : line)) Plugin.Log.LogWarning("Camp: this game build has no hero stat for '" + line + "'; the buff line is left out");
            }
            return kept.ToString();
        }

        private static List<string> CheckedEffects(List<string> ids)
        {
            if (ids.Count == 0) return ids;
            var library = SingletonMonoBehaviour<Library<string, ActorDataEffects>>.Instance;
            var kept = new List<string>();
            foreach (var id in ids)
            {
                if (library != null && library.GetHasLibraryKey(id)) kept.Add(id);
                else if (Reported.Add(id)) Plugin.Log.LogWarning("Camp: this game build has no effects container '" + id + "'; the buff is left out");
            }
            return kept;
        }
    }

    /// <summary>
    /// Who is caught off guard in the fight about to start. DD2 has the notion and never uses it outside its own
    /// tests: BattleTurnOrder.Parameters.m_AmbushTeamIndex names a team that gets no turn in the first round
    /// (BattleTurnOrder.RollTurnOrder). The dungeon sets it for a night ambush and for a party that a camping
    /// skill lets steal up on its enemies.
    /// </summary>
    internal static class FightSurprise
    {
        /// <summary>BattleTeams.HERO_TEAM_INDEX.</summary>
        public const int HeroTeam = 0;
        public const int None = -1;

        private static int _team = None;

        /// <summary>The team surprised in the next fight an Estate session starts.</summary>
        public static void Set(int team) => _team = team;

        public static void Clear() => _team = None;

        public static int Pending => _team;

        /// <summary>The fight is being made: its first round is the surprised team's to lose.</summary>
        internal static int Take()
        {
            var team = _team;
            _team = None;
            return EstateSession.Active ? team : None;
        }
    }

    /// <summary>Every battle's turn order is made from its parameters once, in Battle's constructor.</summary>
    [HarmonyPatch(typeof(BattleTurnOrder), nameof(BattleTurnOrder.Create))]
    internal static class EstateFightsCanStartWithASurprise
    {
        private static void Prefix(BattleTurnOrder.Parameters battleTurnOrderParameters)
        {
            var team = FightSurprise.Take();
            if (team == FightSurprise.None || battleTurnOrderParameters == null) return;
            battleTurnOrderParameters.m_AmbushTeamIndex = team;
            Plugin.Log.LogInfo("Estate fight: team " + team + " is surprised and misses the first round");
        }
    }
}
