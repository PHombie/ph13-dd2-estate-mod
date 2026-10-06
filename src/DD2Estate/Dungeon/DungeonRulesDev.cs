using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Test commands of DD1's expedition rules for the dev bridge (python tools/bridge.py run surprise.state):
    ///
    ///   surprise.state                       DD1's numbers (shared/rules.json) and, on an expedition, each side's chance where the party stands
    ///   surprise.force side=party|monsters|none    the next fight starts that way whatever the dice say (side= clears it)
    ///   surprise.roll [room=true] [known=true] [light=0] [heroes=4] [party=0] [monsters=0] [n=20000]
    ///                                        DD1's roll made n times with those conditions: how often each side came up
    ///   wander.state                         DD1's conditional mash of the dungeon (file, lines, which hold now), what the expedition has met
    ///   wander.force monster=collector|shambler    the next hallway fight is that wanderer's (monster= clears it)
    ///   wander.summon monster=shambler       the wanderer here and now, as a torch at its altar calls it up
    ///   wander.roll [light=0] [bag=1] [n=20000]    DD1's roll made n times for this dungeon: how often each wanderer came
    ///   compulsion.state                     the quirk map (DD2 quirk to DD1 compulsion, with DD1's numbers), the party's quirks, who the curio at hand draws
    ///   compulsion.force hero=3 [keep=true]  that hero reaches for the next curio the party comes to (keep: and keeps its loot); hero=0 clears it
    ///   compulsion.map dd2=quirk_greed_neg on=true     switches one of the data's mappings for this session
    ///   dungeon.rules                        the plot quest's rules of the expedition (scouting, surprise, the torch) and all of the above at once
    ///   dungeon.rules surprise=false scouting=false torchBurns=false
    ///                                        the Darkest Dungeon's three switches on this expedition, each as given (to look at them anywhere)
    ///
    /// (The Ancestor's moments of the walk: narration.raid. The roster's week: upkeep.*. The Darkest Dungeon: darkest.*.)
    /// </summary>
    [EstateModule]
    internal static class DungeonRulesDev
    {
        private static void Register()
        {
            AgentBridge.Register("surprise.state", o => new { dd1 = DungeonRun.DescribeSurpriseRules(), forced = DungeonRun.ForcedSurprise?.ToString(), expedition = DungeonRun.Current?.DescribeSurprise() });
            AgentBridge.Register("surprise.force", o =>
            {
                var side = (string)o["side"];
                if (string.IsNullOrEmpty(side)) DungeonRun.ForcedSurprise = null;
                else if (Enum.TryParse(side, true, out SurpriseSide forced)) DungeonRun.ForcedSurprise = forced;
                else return "side: party, monsters or none";
                return new { forced = DungeonRun.ForcedSurprise?.ToString() };
            });
            AgentBridge.Register("surprise.roll", o =>
            {
                var raid = RaidRules.Load(new Dd1Files());
                var rules = DungeonRun.Surprise;
                var room = (bool?)o["room"] ?? false;
                var known = (bool?)o["known"] ?? false;
                var light = (double?)o["light"] ?? 100;
                var heroes = (int?)o["heroes"] ?? 4;
                double partyBuff = (double?)o["party"] ?? 0, monstersBuff = (double?)o["monsters"] ?? 0;
                var n = Math.Max(1, Math.Min(1000000, (int?)o["n"] ?? 20000));
                var band = raid.BandFor(light);
                var odds = rules.Odds(room, known, band, partyBuff, monstersBuff, heroes);
                var rng = new Rng(DateTime.Now.Ticks);
                int party = 0, monsters = 0;
                for (var i = 0; i < n; i++)
                {
                    var side = rules.Roll(room, known, band, partyBuff, monstersBuff, heroes, rng);
                    if (side == SurpriseSide.Party) party++;
                    else if (side == SurpriseSide.Monsters) monsters++;
                }
                return new
                {
                    room, known, light, heroes,
                    weights = new { nobody = odds.None, party = odds.Party, monsters = odds.Monsters },
                    expected = new { party = odds.PartyShare, monsters = odds.MonstersShare },
                    rolled = new { n, party = party / (double)n, monsters = monsters / (double)n }
                };
            });

            AgentBridge.Register("wander.state", o => DungeonRun.Current != null ? DungeonRun.Current.DescribeWanderers() : (object)new { forced = DungeonRun.ForcedWanderer, expedition = (object)null });
            AgentBridge.Register("wander.force", o =>
            {
                var monster = (string)o["monster"];
                DungeonRun.ForcedWanderer = string.IsNullOrEmpty(monster) ? null : monster;
                var run = DungeonRun.Current;
                return new { forced = DungeonRun.ForcedWanderer, fight = run != null && DungeonRun.ForcedWanderer != null ? DungeonContent.Wanderer(DungeonRun.ForcedWanderer, run.Exploration.Map.DungeonId) : null };
            });
            AgentBridge.Register("wander.summon", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                return run.SummonForTest((string)o["monster"] ?? "shambler") ?? "summoned";
            });
            AgentBridge.Register("wander.roll", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                var rules = run.Wandering;
                var light = (double?)o["light"] ?? run.Exploration.Light;
                var bag = (double?)o["bag"] ?? run.BagShare;
                var n = Math.Max(1, Math.Min(1000000, (int?)o["n"] ?? 20000));
                var rng = new Rng(DateTime.Now.Ticks);
                var met = new Dictionary<string, int>();
                var came = new Dictionary<string, int>();
                for (var i = 0; i < n; i++)
                {
                    var line = rules.Roll(light, bag, met, rng);
                    if (line == null) continue;
                    came.TryGetValue(line.Monster, out var times);
                    came[line.Monster] = times + 1;
                }
                var shares = new Dictionary<string, double>();
                foreach (var pair in came) shares[pair.Key] = pair.Value / (double)n;
                return new { source = rules.Source, light, bag, n, expectedAny = rules.ChanceNow(light, bag, met), rolled = shares };
            });

            AgentBridge.Register("compulsion.state", o => DungeonRun.Current != null ? DungeonRun.Current.DescribeCompulsions() : CurioCompulsions.Describe());
            AgentBridge.Register("compulsion.force", o =>
            {
                DungeonRun.ForcedCompulsion = (uint?)o["hero"] ?? 0u;
                DungeonRun.ForcedKeepLoot = (bool?)o["keep"] ?? false;
                return new { hero = DungeonRun.ForcedCompulsion, keep = DungeonRun.ForcedKeepLoot };
            });
            AgentBridge.Register("compulsion.map", o =>
            {
                var dd2 = (string)o["dd2"];
                if (dd2 != null && !CurioCompulsions.Set(dd2, (bool?)o["on"] ?? true)) return "the data has no mapping for " + dd2;
                return CurioCompulsions.Describe();
            });

            AgentBridge.Register("dungeon.rules", o =>
            {
                var run = DungeonRun.Current;
                if (run == null) return "no expedition";
                run.OverrideRules((bool?)o["surprise"], (bool?)o["scouting"], (bool?)o["torchBurns"]);
                return run.DescribeRules();
            });
        }
    }
}
