using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Combat.Events;
using Assets.Code.Events;
using Assets.Code.Source;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// A buff an item of the bag put on a hero for some rounds of the fight (holy water: DD1's resistances for
    /// three rounds). It is a stats container hung off ActorInstance.ActorData, the way the camping buffs and the
    /// Blacksmith's gear are (<see cref="CampBuffs"/>); a round's start counts it down, and the end of the fight
    /// takes every one off. DD1 gives holy water no <c>replace_buffs</c>: a second one is a second buff.
    /// </summary>
    internal static class FightBagBuffs
    {
        private const string StatsId = "estate_fight_item";

        private class Entry
        {
            public ActorInstance Actor;
            public ActorDataStats Stats;
            public string Item, Text;
            public int Rounds;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static bool _listening;
        private static int _serial;

        public static void Add(ActorInstance hero, string item, string statsText, int rounds)
        {
            if (hero == null || string.IsNullOrEmpty(statsText) || rounds <= 0) return;
            Listen();
            var entry = new Entry { Actor = hero, Item = item, Text = statsText, Rounds = rounds, Stats = new ActorDataStats(StatsId, statsText) };
            entry.Stats.Init(StatsId + "_" + hero.ActorGuid + "_" + (++_serial));
            // the damage calculation sums stats per source and needs every container to have one (CampBuffs.Set)
            entry.Stats.SetSource(SourceType.CLASS, StatsId);
            hero.ActorData.AddChild(entry.Stats);
            Entries.Add(entry);
            Refresh(hero);
        }

        /// <summary>The fight is over (or the session is): nothing of a fight's stays on a hero.</summary>
        public static void Clear()
        {
            if (Entries.Count == 0) return;
            var heroes = new HashSet<ActorInstance>();
            foreach (var entry in Entries)
                if (Remove(entry)) heroes.Add(entry.Actor);
            Entries.Clear();
            foreach (var hero in heroes) Refresh(hero);
        }

        private static void Listen()
        {
            if (_listening) return;
            EventManager.AddListener<EventBattleStartRound>(OnRound);
            _listening = true;
        }

        private static void OnRound(EventBattleStartRound round)
        {
            if (Entries.Count == 0) return;
            var heroes = new HashSet<ActorInstance>();
            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (--entry.Rounds > 0) continue;
                if (Remove(entry)) heroes.Add(entry.Actor);
                Entries.RemoveAt(i);
                Plugin.Log.LogInfo("Fight bag: " + entry.Item + " has worn off " + Name(entry.Actor));
            }
            foreach (var hero in heroes) Refresh(hero);
        }

        private static bool Remove(Entry entry)
        {
            try
            {
                entry.Actor.ActorData.RemoveChild(entry.Stats);
                return true;
            }
            catch (Exception e)
            {
                // the hero is gone with the session: nothing to take anything off
                Plugin.Log.LogInfo("Fight bag: a buff's hero is gone (" + e.Message + ")");
                return false;
            }
        }

        private static void Refresh(ActorInstance hero)
        {
            try
            {
                hero.RefreshStats();
                hero.UpdatePreviousHpMax();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Fight bag: a hero's stats could not be refreshed: " + e.Message); }
        }

        private static string Name(ActorInstance hero)
        {
            try { return hero.ActorName; }
            catch (Exception) { return "a hero"; }
        }

        /// <summary>Dev bridge: what is on the heroes now.</summary>
        public static List<object> Describe()
        {
            var list = new List<object>();
            foreach (var entry in Entries) list.Add(new { hero = entry.Actor.ActorGuid, name = Name(entry.Actor), item = entry.Item, rounds = entry.Rounds, stats = entry.Text.Trim().Replace("\n", "; ") });
            return list;
        }
    }
}
