using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Assets.Code.Actor;
using DD2Estate.Core;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The DD2 side of DD1's quirk compulsions at curios (<see cref="CompulsionRules"/>). A DD2 hero carries
    /// DD2 quirks; the embedded <c>Data/quirk_compulsions.json</c> says which of them stands for which DD1
    /// quirk. THE MOD'S OWN MAPPING, and a short one: a DD2 quirk is given DD1's compulsion only where it is
    /// the same quirk in both games (the same name, or the same sentence of description); the nearest
    /// candidates are listed there switched off. The tag, the chance and "keeps the loot" are DD1's, read from
    /// its quirk library by the DD1 quirk's id.
    /// </summary>
    internal static class CurioCompulsions
    {
        public class Mapping
        {
            public string Dd2, Dd1, Basis;
            public bool On;
        }

        private static List<Mapping> _map;
        private static CompulsionRules _rules;

        /// <summary>DD1's compulsions (none when the install cannot be read).</summary>
        public static CompulsionRules Rules => _rules ?? (_rules = CompulsionRules.Load(new Dd1Files()));

        /// <summary>DD2 quirk to DD1 quirk, as the data has it.</summary>
        public static IReadOnlyList<Mapping> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new List<Mapping>();
                try
                {
                    using (var stream = typeof(CurioCompulsions).Assembly.GetManifestResourceStream("DD2Estate.Data.quirk_compulsions.json"))
                    using (var reader = new StreamReader(stream))
                        foreach (var entry in JObject.Parse(reader.ReadToEnd())["map"] as JArray ?? new JArray())
                            if ((string)entry["dd2"] != null && (string)entry["dd1"] != null)
                                _map.Add(new Mapping { Dd2 = (string)entry["dd2"], Dd1 = (string)entry["dd1"], Basis = (string)entry["basis"], On = (bool?)entry["on"] ?? false });
                }
                catch (Exception e) { Plugin.Log.LogError("Curio compulsions: the quirk map could not be read: " + e); }
                return _map;
            }
        }

        /// <summary>Dev bridge: switches one of the data's mappings on or off for this session.</summary>
        public static bool Set(string dd2Quirk, bool on)
        {
            foreach (var mapping in Map)
            {
                if (mapping.Dd2 != dd2Quirk) continue;
                mapping.On = on;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The party's compulsions, hero by hero in marching order, each hero's in the order of their own list
        /// of quirks: every DD2 quirk they carry that the map gives a DD1 compulsion.
        /// </summary>
        public static List<HeroCompulsions> Of(IEnumerable<ActorInstance> party)
        {
            var heroes = new List<HeroCompulsions>();
            if (party == null) return heroes;
            foreach (var hero in party)
            {
                var quirks = hero != null && hero.IsLiving ? hero.QuirkContainer : null;
                if (quirks == null || !quirks.IsEnabled) continue;
                var own = new HeroCompulsions { Hero = hero.ActorGuid };
                foreach (var quirk in quirks.GetInstances())
                {
                    var id = quirk?.Definition?.Id;
                    if (id == null) continue;
                    foreach (var mapping in Map)
                    {
                        if (!mapping.On || mapping.Dd2 != id) continue;
                        var compulsion = Rules.Of(mapping.Dd1);
                        if (compulsion != null) own.Compulsions.Add(compulsion);
                    }
                }
                if (own.Compulsions.Count > 0) heroes.Add(own);
            }
            return heroes;
        }

        /// <summary>The DD2 quirk that stands for a DD1 compulsion, by the name the game shows; the DD1 id when it cannot be asked.</summary>
        public static string QuirkName(ActorInstance hero, Compulsion compulsion)
        {
            foreach (var mapping in Map)
            {
                if (mapping.Dd1 != compulsion.Quirk) continue;
                try
                {
                    if (hero?.QuirkContainer == null || !hero.QuirkContainer.GetHasInstanceWithId(mapping.Dd2, false, 0u)) continue;
                    var name = SanitariumRules.QuirkName(mapping.Dd2, hero);
                    if (!string.IsNullOrEmpty(name)) return Regex.Replace(name, "<[^>]+>", "").Trim();
                }
                catch (Exception) { /* the name is for the log line only */ }
            }
            return compulsion.Quirk;
        }

        public static object Describe()
        {
            var map = new List<object>();
            foreach (var mapping in Map)
            {
                var compulsion = Rules.Of(mapping.Dd1);
                map.Add(new
                {
                    dd2 = mapping.Dd2, dd1 = mapping.Dd1, on = mapping.On, basis = mapping.Basis,
                    tag = compulsion?.Tag, chance = compulsion?.Chance, keepLoot = compulsion?.KeepLoot
                });
            }
            var unmapped = new List<object>();
            foreach (var compulsion in Rules.All)
            {
                var mapped = false;
                foreach (var mapping in Map) mapped |= mapping.Dd1 == compulsion.Quirk;
                if (!mapped) unmapped.Add(new { dd1 = compulsion.Quirk, tag = compulsion.Tag, chance = compulsion.Chance, keepLoot = compulsion.KeepLoot });
            }
            return new { map, dd1WithoutADd2Quirk = unmapped };
        }
    }
}
