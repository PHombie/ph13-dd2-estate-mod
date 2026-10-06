using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    public static class CurioTests
    {
        private static readonly string[] KnownTypes =
        {
            CurioResultTypes.Nothing, CurioResultTypes.Loot, CurioResultTypes.Quirk, CurioResultTypes.Effect, CurioResultTypes.Purge,
            CurioResultTypes.Scouting, CurioResultTypes.Teleport, CurioResultTypes.Disease, CurioResultTypes.Summon
        };

        public static void CatalogueCoversTheFourDungeons()
        {
            var catalog = Dd1.Curios;
            Check.True(catalog.All.Count >= 50, "curio types read: " + catalog.All.Count);
            Check.Equal(catalog.All.Count, catalog.All.Select(c => c.Id).Distinct().Count(), "curio ids are unique");

            foreach (var dungeon in Dd1.Dungeons)
            {
                var props = Dd1.Generation.PropsFor(dungeon);
                var ids = props.HallCurios.Concat(props.RoomCurios).Concat(props.RoomTreasures).Concat(props.SecretTreasures).Select(w => w.Id).Distinct().ToList();
                Check.True(ids.Count >= 15, dungeon + " curio ids: " + ids.Count);
                foreach (var id in ids)
                {
                    var def = catalog.Get(id);
                    Check.True(def != null, dungeon + ": curio " + id + " is not in the library");
                    Check.True(catalog.Prop(id) != null && catalog.Prop(id).Sprite.Length > 0, dungeon + ": curio " + id + " has no prop row");
                    Check.True(def.Results.Count > 0 && def.Results.All(r => r.Weight > 0), id + ": results");
                    Check.True(def.Region != null && def.Region.Length > 0 && def.Tags.Count > 0 && def.Name.Length > 0, id + ": header fields");
                    foreach (var result in def.Results.Concat(def.ItemUses.Select(u => u.Result)))
                    {
                        Check.True(KnownTypes.Contains(result.Type), id + ": unknown result type " + result.Type);
                        if (result.Type != CurioResultTypes.Nothing)
                            Check.True(result.Values.Count > 0 && result.Values.All(v => v.Value.Length > 0 && v.Amount > 0), id + ": " + result.Type + " without values");
                    }
                }
            }
        }

        public static void LibraryRowsAreReadAsDd1WritesThem()
        {
            var catalog = Dd1.Curios;

            // a result type per row with its weight; loot cells are table + draws
            var strongbox = catalog.Get("locked_strongbox");
            Check.Equal("Mixed", strongbox.Alignment, "alignment");
            Check.Equal("ALL", strongbox.Region, "region");
            Check.True(strongbox.Tags.Contains("Treasure"), "tags");
            Check.Equal(2, strongbox.Results.Count, "locked strongbox result types");
            var loot = strongbox.Results.Single(r => r.Type == CurioResultTypes.Loot);
            var trap = strongbox.Results.Single(r => r.Type == CurioResultTypes.Effect);
            Check.Equal(loot.Weight, trap.Weight, "loot and trap weigh the same");
            Check.True(loot.Values.Count == 1 && loot.Values[0].Value == "A" && loot.Values[0].Amount >= 1, "loot table and draws");
            Check.Equal(2, trap.Values.Count, "two trap effects");

            // item interactions: the key opens it safely with more draws, the shovel may destroy the contents
            Check.True(catalog.ItemsFor("locked_strongbox").SequenceEqual(new[] { "skeleton_key", "shovel" }), "items of the locked strongbox");
            var key = strongbox.ItemUse("skeleton_key");
            Check.True(key.Result.Type == CurioResultTypes.Loot && key.Result.NothingChance == 0 && key.Result.Values[0].Amount > loot.Values[0].Amount, "key");
            Check.Equal("loot", key.TrackerId, "tracker icon");
            var shovel = strongbox.ItemUse("shovel");
            Check.True(shovel.Result.NothingChance > 0 && shovel.Result.NothingChance < 1 && shovel.Result.Values.Single().Value == "A", "shovel");

            // several loot tables in one row are all drawn
            var tent = catalog.Get("travellers_tent");
            Check.Equal(3, tent.Results.Single(r => r.Type == CurioResultTypes.Loot).Values.Count, "tent loot tables");
            var scouting = tent.Results.Single(r => r.Type == CurioResultTypes.Scouting);
            foreach (var value in scouting.Values)
                Check.True(CurioCatalog.ParseScouting(value.Value, out var reach, out var target) && reach >= 0 && target.Length > 0, "scouting value " + value.Value);

            // a prop can borrow another curio's art
            Check.Equal("heirloom_chest", catalog.Prop("locked_strongbox").Sprite, "sprite of the locked strongbox");
            // quest curios are props with a quest interaction, not library entries
            Check.True(catalog.Get("reliquary") == null && catalog.Roll("reliquary", null, new Rng(1)).Type == CurioResultTypes.Nothing, "quest curio");
        }

        public static void RollsFollowTheWeights()
        {
            var catalog = Dd1.Curios;
            var rng = new Rng(99);
            foreach (var def in catalog.All)
            {
                const int rolls = 3000;
                var counts = new Dictionary<string, int>();
                for (var i = 0; i < rolls; i++)
                {
                    var outcome = catalog.Roll(def.Id, null, rng);
                    Check.True(outcome.CurioId == def.Id && outcome.ItemId == null, def.Id + ": outcome header");
                    Check.Equal(outcome.Type == CurioResultTypes.Loot, outcome.Loot.Count > 0, def.Id + ": loot draws only for loot");
                    Check.Equal(outcome.Type != CurioResultTypes.Loot && outcome.Type != CurioResultTypes.Nothing, outcome.Value != null, def.Id + ": value");
                    counts[outcome.Type] = counts.TryGetValue(outcome.Type, out var n) ? n + 1 : 1;
                }
                var total = def.Results.Sum(r => r.Weight);
                foreach (var result in def.Results)
                    Check.Near(result.Weight / total, counts.TryGetValue(result.Type, out var n) ? n / (double)rolls : 0, 0.04, def.Id + " share of " + result.Type);

                foreach (var use in def.ItemUses)
                {
                    var outcome = catalog.Roll(def.Id, use.ItemId, rng);
                    Check.Equal(use.ItemId, outcome.ItemId, def.Id + ": item used");
                    Check.True(outcome.Type == use.Result.Type || (use.Result.NothingChance > 0 && outcome.Type == CurioResultTypes.Nothing), def.Id + " with " + use.ItemId);
                }
                // an item the curio has no use for changes nothing and is not consumed
                Check.True(catalog.Roll(def.Id, "no_such_item", rng).ItemId == null, def.Id + ": useless item");
            }

            // the shovel on a locked strongbox loses the contents about as often as DD1 says
            var chance = catalog.Get("locked_strongbox").ItemUse("shovel").Result.NothingChance;
            var empty = Enumerable.Range(0, 4000).Count(i => catalog.Roll("locked_strongbox", "shovel", rng).Type == CurioResultTypes.Nothing);
            Check.Near(chance, empty / 4000.0, 0.03, "smashed strongbox");
        }

        public static void EveryLootCodeHasATable()
        {
            var catalog = Dd1.Curios;
            var loot = Dd1.Loot;
            var codes = catalog.All.SelectMany(c => c.Results.Concat(c.ItemUses.Select(u => u.Result)))
                .Where(r => r.Type == CurioResultTypes.Loot).SelectMany(r => r.Values).Select(v => v.Value).Distinct().ToList();
            Check.True(codes.Count >= 15, "loot codes: " + codes.Count);
            var rng = new Rng(5);
            foreach (var code in codes)
            {
                Check.True(loot.Has(code), "no loot table " + code);
                foreach (var tier in new[] { 1, 3, 5 })
                    foreach (var dungeon in Dd1.Dungeons)
                    {
                        var drops = loot.Draw(code, 3, tier, dungeon, rng);
                        Check.True(drops.Count <= 3 && drops.All(d => d.Amount > 0 && d.Kind != LootKind.Nothing), code + " drops");
                    }
            }

            // gold comes out as amounts, heirlooms by type, trinkets as a rarity to roll
            var gold = loot.Draw("C", 200, 1, "crypts", rng);
            Check.True(gold.Count == 200 && gold.All(d => d.Kind == LootKind.Gold && d.Amount >= 1), "table C is gold");
            Check.True(loot.Draw("C", 2000, 5, "crypts", rng).Average(d => d.Amount) > loot.Draw("C", 2000, 1, "crypts", rng).Average(d => d.Amount), "more gold at higher difficulty");
            var heirlooms = loot.Draw("H", 400, 1, "crypts", rng);
            Check.True(heirlooms.All(d => d.Kind == LootKind.Heirloom && d.Id.Length > 0), "table H is heirlooms");
            Check.True(heirlooms.Select(d => d.Id).Distinct().Count() >= 3, "several heirloom types");
            Check.True(Share(loot.Draw("H", 3000, 1, "warrens", rng), "portrait") > Share(loot.Draw("H", 3000, 1, "crypts", rng), "portrait") + 0.1, "heirloom weights differ by dungeon");
            var trinkets = loot.Draw("T", 300, 3, "cove", rng);
            Check.True(trinkets.All(d => d.Kind == LootKind.Trinket && d.Id.Length > 0), "table T is trinket rarities");
            var chest = loot.Draw("A", 3000, 3, "weald", rng);
            foreach (var kind in new[] { LootKind.Gold, LootKind.Gem, LootKind.Heirloom, LootKind.Supply, LootKind.Provision, LootKind.Trinket, LootKind.JournalPage })
                Check.True(chest.Any(d => d.Kind == kind), "chest table never gave " + kind);
            Check.Equal(0, loot.Draw("no_such_table", 5, 1, "crypts", rng).Count, "unknown table");

            // darkness adds draws, bright light does not
            int dark = 0, bright = 0;
            for (var i = 0; i < 1000; i++)
            {
                dark += loot.DarknessBonusCodes("battle", 0, rng).Count;
                bright += loot.DarknessBonusCodes("battle", 100, rng).Count;
            }
            Check.True(dark > 500 && bright == 0, "darkness loot bonus: dark " + dark + ", bright " + bright);
        }

        private static double Share(List<LootDrop> drops, string id) => drops.Count(d => d.Id == id) / (double)drops.Count;

        // The English ids of DD1's curio string table, read with nothing but string searching.
        private static HashSet<string> CurioStringIds()
        {
            var ids = new HashSet<string>();
            var english = false;
            foreach (var line in System.IO.File.ReadLines(System.IO.Path.Combine(Dd1.Root, "localization", "curios.string_table.xml")))
            {
                if (line.Contains("<language "))
                {
                    if (english) break;
                    english = line.Contains("id=\"english\"");
                    continue;
                }
                var at = line.IndexOf("<entry id=\"", StringComparison.Ordinal);
                if (!english || at < 0) continue;
                var end = line.IndexOf('"', at + 11);
                if (end > at) ids.Add(line.Substring(at + 11, end - at - 11));
            }
            return ids;
        }

        public static void FullCuriosHaveAWindowTheOthersDoNot()
        {
            var catalog = Dd1.Curios;
            // DD1's library: "FULL CURIO?" is No for what is opened the moment it is turned to
            foreach (var id in new[] { "discarded_pack", "sconce", "crate", "sack" })
                Check.True(!catalog.HasWindow(id), id + " is opened without a window");
            foreach (var id in new[] { "unlocked_strongbox", "heirloom_chest", "travellers_tent", "holy_fountain", "eldritch_altar" })
                Check.True(catalog.HasWindow(id), id + " is asked about in a window");
            Check.True(catalog.HasWindow("no_such_curio"), "what the library does not hold (a quest's curio) has the window");
            // none of the windowless ones takes an item: there is no slot to put it in
            foreach (var def in catalog.All.Where(d => !d.Full))
                Check.Equal(0, def.ItemUses.Count, def.Id + ": a curio without a window has no item interactions");
        }

        public static void ResultSentencesAreFoundInDd1sTable()
        {
            var catalog = Dd1.Curios;
            var table = CurioStringIds();
            Check.True(table.Count > 300, "curio strings read: " + table.Count);

            string Found(CurioOutcome outcome) => catalog.ResultTextIds(outcome).FirstOrDefault(table.Contains);
            Check.Equal("str_curio_heirloom_chest_nothing", Found(new CurioOutcome { CurioId = "heirloom_chest" }), "bare hands, nothing");
            Check.Equal("str_curio_heirloom_chest_skeleton_key_loot", Found(new CurioOutcome { CurioId = "heirloom_chest", ItemId = "skeleton_key", Type = CurioResultTypes.Loot }), "an item's own sentence");
            Check.Equal("str_curio_locked_strongbox_shovel_nothing", Found(new CurioOutcome { CurioId = "locked_strongbox", ItemId = "shovel" }), "the shovel that destroys the contents");
            Check.Equal("str_curio_discarded_pack_scout", Found(new CurioOutcome { CurioId = "discarded_pack", Type = CurioResultTypes.Scouting, Value = "1 - all" }), "a map is a scout");
            Check.Equal("str_curio_eerie_coral_effect_eerie_coral_stress", Found(new CurioOutcome { CurioId = "eerie_coral", Type = CurioResultTypes.Effect, Value = "eerie_coral_stress" }), "a sentence per effect");
            Check.Equal("str_curio_shamblers_altar_torch_summon_summon_mash_shambler",
                Found(new CurioOutcome { CurioId = "shamblers_altar", ItemId = "torch", Type = CurioResultTypes.Summon, Value = "summon_mash_shambler" }), "the summon");
            Check.Equal("str_curio_sacrificial_stone_provisions_purge", Found(new CurioOutcome { CurioId = "sacrificial_stone", ItemId = "provision", Type = CurioResultTypes.Purge }), "food is 'provisions' in the table");
            Check.Equal("str_curio_eldritch_altar_holy_water", Found(new CurioOutcome { CurioId = "eldritch_altar", ItemId = "holy_water", Type = CurioResultTypes.Purge }), "the one id without a result");

            // every result of every curio the four dungeons place: most have a sentence, and none is found under a wrong curio's name
            int with = 0, without = 0;
            foreach (var dungeon in Dd1.Dungeons)
            {
                var props = Dd1.Generation.PropsFor(dungeon);
                foreach (var id in props.HallCurios.Concat(props.RoomCurios).Concat(props.RoomTreasures).Concat(props.SecretTreasures).Select(w => w.Id).Distinct())
                {
                    var def = catalog.Get(id);
                    foreach (var result in def.Results)
                    {
                        var found = Found(new CurioOutcome { CurioId = id, Type = result.Type, Value = result.Values.Count > 0 && result.Type != CurioResultTypes.Loot ? result.Values[0].Value : null });
                        if (found == null) without++;
                        else
                        {
                            with++;
                            Check.True(found.StartsWith("str_curio_" + def.Id, StringComparison.Ordinal), id + ": " + found);
                        }
                    }
                }
            }
            Check.True(with > 100 && with > without * 4, "sentences found for " + with + " results, none for " + without);
        }

        public static void TrackerRemembersWhatAnItemDid()
        {
            var catalog = Dd1.Curios;
            Check.Equal("loot", catalog.TrackerFor("heirloom_chest", "skeleton_key"), "the library's tracker id");
            Check.True(catalog.TrackerFor("heirloom_chest", "torch") == null, "no use for a torch");
            Check.True(catalog.TrackerFor("heirloom_chest", null) == null, "bare hands");

            var tracker = new CurioTracker();
            Check.Equal(TrackerIds.Unknown, tracker.Get("heirloom_chest", "skeleton_key"), "not tried yet");
            tracker.Learn("heirloom_chest", "skeleton_key", "loot");
            tracker.Learn("heirloom_chest", "torch", TrackerIds.NoEffect);
            Check.Equal("loot", tracker.Get("heirloom_chest", "skeleton_key"), "learnt");
            Check.Equal(TrackerIds.NoEffect, tracker.Get("heirloom_chest", "torch"), "learnt that it does nothing");
            Check.Equal(TrackerIds.Unknown, tracker.Get("sarcophagus", "skeleton_key"), "another curio");
            var again = new CurioTracker();
            again.FromJson(Newtonsoft.Json.Linq.JToken.Parse(tracker.ToJson().ToString()));
            Check.True(again.Count == 2 && again.Get("heirloom_chest", "skeleton_key") == "loot", "survives a save");
        }

        /// <summary>Not a check: prints the distinct DD1 result types and values the game layer has to map.</summary>
        public static void ListResultTypesAndValues()
        {
            var catalog = Dd1.Curios;
            var results = catalog.All.SelectMany(c => c.Results.Concat(c.ItemUses.Select(u => u.Result))).ToList();
            foreach (var type in results.Select(r => r.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal))
            {
                var values = results.Where(r => r.Type == type).SelectMany(r => r.Values).Select(v => v.Value).Distinct().OrderBy(v => v, StringComparer.Ordinal);
                Console.WriteLine("      " + type + ": " + string.Join(", ", values));
            }
            Console.WriteLine("      items: " + string.Join(", ", catalog.All.SelectMany(c => c.ItemUses).Select(u => u.ItemId).Distinct().OrderBy(v => v, StringComparer.Ordinal)));
            Console.WriteLine("      tracker ids: " + string.Join(", ", catalog.All.SelectMany(c => c.ItemUses).Select(u => u.TrackerId).Distinct().OrderBy(v => v, StringComparer.Ordinal)));
        }
    }
}
