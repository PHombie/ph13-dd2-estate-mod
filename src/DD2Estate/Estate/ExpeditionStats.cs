using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Source;
using DD2Estate.Dungeon;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Stats a hero carries for as long as an expedition lasts (what DD1 hangs on a hero as a buff "until the
    /// quest ends"): one DD2 stats container per hero under the hero's data, the way the Blacksmith hangs gear
    /// and the camp its buffs. The game saves no container of the mod's, so whoever owns one of these calls
    /// <see cref="Sync"/> now and then: the heroes of the party get theirs back after a load, and
    /// <see cref="Clear"/> takes them off when the expedition is over.
    /// </summary>
    internal sealed class ExpeditionStats
    {
        private class Attached
        {
            public ActorInstance Actor;
            public ActorDataStats Stats;
            public string Text;
        }

        private readonly string _id;
        private readonly Dictionary<uint, Attached> _on = new Dictionary<uint, Attached>();

        /// <param name="statsId">The containers' id, one per owner ("estate_underlevel").</param>
        public ExpeditionStats(string statsId) { _id = statsId; }

        /// <summary>What a hero carries now, as the container's own text; null when nothing.</summary>
        public string TextOn(uint guid) => _on.TryGetValue(guid, out var attached) ? attached.Text : null;

        /// <summary>
        /// Brings the party's containers in step: <paramref name="textOf"/> gives a hero's stats as DD2 writes
        /// them (StatDataContainer.SetFromCsv: "sub_stat,resistance,stress,-0.25"), or nothing.
        /// </summary>
        public void Sync(Func<uint, string> textOf)
        {
            var wanted = new HashSet<uint>();
            foreach (var hero in Provisioning.Party())
            {
                var guid = hero.ActorGuid;
                var text = textOf(guid) ?? "";
                if (text.Length == 0) continue;
                wanted.Add(guid);
                if (_on.TryGetValue(guid, out var old))
                {
                    if (ReferenceEquals(old.Actor, hero) && old.Text == text) continue;
                    Remove(old);
                }
                // The source is CLASS, as for the Blacksmith's gear: the game sums stats per source and wants every container to have one.
                var stats = new ActorDataStats(_id, text);
                stats.Init(_id + "_" + guid);
                stats.SetSource(SourceType.CLASS, _id);
                hero.ActorData.AddChild(stats);
                hero.RefreshStats();
                _on[guid] = new Attached { Actor = hero, Stats = stats, Text = text };
            }
            foreach (var guid in new List<uint>(_on.Keys))
            {
                if (wanted.Contains(guid)) continue;
                Remove(_on[guid]);
                _on.Remove(guid);
            }
        }

        /// <summary>Takes every container off.</summary>
        public void Clear()
        {
            foreach (var attached in _on.Values) Remove(attached);
            _on.Clear();
        }

        /// <summary>The session is gone and its heroes with it: nothing is left to take anything off.</summary>
        public void Forget() => _on.Clear();

        private static void Remove(Attached attached)
        {
            try
            {
                attached.Actor.ActorData.RemoveChild(attached.Stats);
                if (attached.Actor.IsLiving) attached.Actor.RefreshStats();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Expedition stats: a hero's container could not be taken off: " + e.Message); }
        }
    }
}
