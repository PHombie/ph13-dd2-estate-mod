using System;
using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>
    /// How a trinket is drawn for a rarity, whoever hands it out (a quest's reward, the Nomad Wagon's table, a
    /// fight's or a curio's loot, a boss's trophy), and what "never two of the same" means.
    ///
    /// The estate holds a trinket once: worn by a living hero, lying in the stores, carried by the expedition
    /// (in its bag, or on a loot scroll not yet taken or left), or kept in the Shrieker's hoard to be won back.
    /// A draw leaves out what is held and what is spoken for already (<c>taken</c>: the other rewards and wares
    /// of the week, the other cards of the same find). When every trinket of the rarity asked for is out, the
    /// nearest other rarity is drawn from, cheaper and dearer by turns; the Ancestor's own only when they are
    /// asked for by name. When nothing is left at all the draw gives no trinket, and whoever asked pays what
    /// one of the rarity would have sold for instead (DD1's sell value of the rarity, in gold).
    /// With the rule switched off (DD1's own way: copies) only <c>taken</c> narrows a draw.
    /// </summary>
    public static class TrinketDraws
    {
        /// <summary>The game's rarities, rarest last.</summary>
        public static readonly string[] Order = { "common", "rare", "epic", "ancestral" };

        /// <summary>The rarity whose trinkets stand in for no other and are stood in for by no other.</summary>
        public const string Apart = "ancestral";

        /// <summary>Whether the estate may take one more of a trinket it holds <paramref name="held"/> of.</summary>
        public static bool MayHold(int held, bool unique) => !unique || held <= 0;

        /// <summary>The rarity asked for, then the others by distance (the rarer first at the same distance); <see cref="Apart"/> only when asked for.</summary>
        public static IEnumerable<string> Nearest(string rarity)
        {
            var at = Array.IndexOf(Order, rarity);
            if (at < 0) yield break;
            yield return rarity;
            for (var distance = 1; distance < Order.Length; distance++)
                foreach (var index in new[] { at + distance, at - distance })
                    if (index >= 0 && index < Order.Length && Order[index] != Apart) yield return Order[index];
        }

        /// <summary>
        /// One trinket of a pool that may still be handed out; null when the pool has none. The pool's order
        /// decides which one a seed gives, so it should be a sorted one.
        /// </summary>
        public static string From(IEnumerable<string> pool, Func<string, bool> mayHold, ICollection<string> taken, Rng rng)
        {
            var free = new List<string>();
            if (pool != null)
                foreach (var id in pool)
                    if (id != null && !free.Contains(id) && (taken == null || !taken.Contains(id)) && (mayHold == null || mayHold(id))) free.Add(id);
            return free.Count > 0 ? free[rng.Next(free.Count)] : null;
        }

        /// <summary>
        /// A trinket for a rarity: from its own pool, or from the nearest other one that has something left.
        /// <paramref name="from"/> is the rarity it came out of; null with a null result: nothing is left to give.
        /// </summary>
        public static string Draw(string rarity, Func<string, IEnumerable<string>> pool, Func<string, bool> mayHold, ICollection<string> taken, Rng rng, out string from)
        {
            from = null;
            if (pool == null || rng == null) return null;
            foreach (var candidate in Nearest(rarity))
            {
                var id = From(pool(candidate), mayHold, taken, rng);
                if (id == null) continue;
                from = candidate;
                return id;
            }
            return null;
        }
    }
}
