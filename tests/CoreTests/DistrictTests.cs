using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's Districts (Core/Districts.cs): the file readers against the real install, the rules on buildings of their own.</summary>
    public static class DistrictTests
    {
        private const string Buffs = "{\"buffs\":["
            + "{\"id\":\"crit\",\"stat_type\":\"combat_stat_add\",\"stat_sub_type\":\"crit_chance\",\"amount\":0.04,\"rule_type\":\"always\",\"rule_data\":{\"float\":0,\"string\":\"\"}},"
            + "{\"id\":\"scout\",\"stat_type\":\"scouting_chance\",\"stat_sub_type\":\"\",\"amount\":0.05,\"rule_type\":\"always\"},"
            + "{\"id\":\"finale\",\"stat_type\":\"combat_stat_multiply\",\"stat_sub_type\":\"damage_low\",\"amount\":0.2,\"rule_type\":\"skill\",\"rule_data\":{\"float\":0,\"string\":\"heroic_end\"}},"
            + "{\"id\":\"eat\",\"stat_type\":\"hp_heal_received_percent\",\"stat_sub_type\":\"eat\",\"amount\":0.15,\"rule_type\":\"always\"},"
            + "{\"id\":\"odd\",\"stat_type\":\"something_new\",\"stat_sub_type\":\"\",\"amount\":1,\"rule_type\":\"always\"}]}";

        private static string Building(string name, int priority, string costs, string buffs)
        {
            return "{\"name\":\"" + name + "\",\"render_data\":{\"sprite_paths\":[\"fx/town_district_" + name + "/town_district_" + name + ".sprite.skel\",\"fx/purchase_district/purchase_district.sprite.skel\"],"
                   + "\"not_built_animation\":\"idle\",\"built_animation\":\"built\",\"town_priority\":" + priority + "},\"currency_cost\":[" + costs + "],\"buff_list\":[" + buffs + "]}";
        }

        private static string Cost(string type, int amount) => "{\"type\":\"" + type + "\",\"amount\":" + amount + "}";

        private static DistrictRules Rules(params string[] buildings)
        {
            return DistrictRules.Parse("{\"enabled\":true,\"buildings\":[" + string.Join(",", buildings) + "]}", Buffs);
        }

        private static DistrictRules Small()
        {
            return Rules(
                Building("second", 1000, Cost("gold", 2500) + "," + Cost("deed", 110) + "," + Cost("blueprint", 1),
                    "{\"type\":\"DistrictHeroBuffData\",\"name\":\"hero_buff0\",\"hero_type_tags\":[\"house_of_the_yellow_hand\"],\"buff_list\":[\"crit\",\"scout\",\"finale\",\"odd\",\"gone\"]},"
                    + "{\"type\":\"DistrictCurioInteractionBuffData\",\"name\":\"curio_buff0\",\"hero_type_tags\":[\"library\"],\"curio_tags\":[\"Knowledge\"],\"stress_heal\":15}"),
                Building("first", 0, Cost("gold", 15000) + "," + Cost("portrait", 50) + "," + Cost("blueprint", 1),
                    "{\"type\":\"DistrictEstateBuffData\",\"name\":\"estate\",\"weekly_gold_interest\":0.05},"
                    + "{\"type\":\"DistrictRosterStressReliefBuffData\",\"name\":\"relief\",\"stress_relief\":10}"),
                Building("third", 2000, Cost("gold", 20) + "," + Cost("blueprint", 1),
                    "{\"type\":\"DistrictHeroBuffData\",\"name\":\"hero_buff0\",\"hero_type_tags\":[],\"buff_list\":[\"eat\"]},"
                    + "{\"type\":\"DistrictSupplyBuffData\",\"name\":\"provision\",\"item_type\":\"provision\",\"target_inventory\":\"provision\",\"item_name\":\"\",\"range_min\":4,\"range_max\":10},"
                    + "{\"type\":\"DistrictCampingBuffData\",\"name\":\"camp_buff0\",\"hero_type_tags\":[\"outsiders_bonfire\"],\"camping_points\":2},"
                    + "{\"type\":\"DistrictOfTheFutureBuffData\",\"name\":\"what\"}"));
        }

        private static Dictionary<string, int> Purse(int gold, int blueprints, params string[] heirlooms)
        {
            var purse = new Dictionary<string, int> { { "gold", gold }, { "blueprint", blueprints } };
            for (var i = 0; i + 1 < heirlooms.Length; i += 2) purse[heirlooms[i]] = int.Parse(heirlooms[i + 1]);
            return purse;
        }

        private static string Text(IEnumerable<DistrictCost> costs) => string.Join(", ", costs.Select(c => c.ToString()));

        // ---- DD1's files ---------------------------------------------------------------------------------

        public static void BuildingsReadFromDd1()
        {
            var rules = DistrictRules.Load(Dd1.Files);
            if (rules.Missing) throw new SkipException("this DD1 install has no districts");
            Check.True(rules.Buildings.Select(b => b.Id).SequenceEqual(new[]
            {
                "spire_of_hope", "bank", "illuminators_guild", "granary", "buskers_corner", "house_of_the_yellow_hand", "altar_of_light", "training_ring", "library", "theater",
                "outsiders_bonfire"
            }), "the eleven buildings in the screen's order: " + string.Join(", ", rules.Buildings.Select(b => b.Id)));
            Check.True(!rules.StockBuffsInUse, "the buffs file is read");
            Check.True(rules.Buildings.All(b => b.Costs.Count == 3 && b.Costs[0].Currency == "gold" && b.Costs[2].Currency == "blueprint" && b.Costs[2].Amount == 1), "gold, an heirloom and one blueprint each");
            Check.True(rules.Buildings.All(b => b.Effects.All(e => e.Kind != DistrictEffectKind.Unknown)), "every buff type has a meaning");
            Check.True(rules.Buildings.All(b => Dd1.Files.Exists(b.Skeleton) && Dd1.Files.Exists(b.Skeleton.Replace(".skel", ".atlas"))), "every building has its art");
            Check.True(rules.Buildings.All(b => b.Skeleton.StartsWith(DistrictRules.Feature) && b.NotBuiltArt == "idle" && b.BuiltArt == "built"), "which is the feature's own, in two states");
            Check.True(rules.Buildings.All(b => b.PurchaseSkeleton == "fx/purchase_district/purchase_district.sprite.skel" && b.PurchasedArt == "purchased" && Dd1.Files.Exists(b.PurchaseSkeleton)),
                "the bricks and dust of a purchase are the game's own sprite");

            var bank = rules.Find("bank");
            Check.Equal("15000 gold, 50 portrait, 1 blueprint", Text(bank.Costs), "the bank's price in DD1's units");
            Check.Equal("15000 gold, 50 portrait, 1 blueprint", Text(DistrictRules.Price(bank)), "which are the estate's");
            Check.True(bank.Effects.Count == 1 && bank.Effects[0].Kind == DistrictEffectKind.Interest && bank.Effects[0].Amount == 0.05 && bank.Effects[0].Name == "estate", "5% a week");
            Check.Equal("50000 gold, 750 crest, 1 blueprint", Text(DistrictRules.Price(rules.Find("spire_of_hope"))), "the dearest");
            Check.True(rules.Find("spire_of_hope").Effects.Single().Kind == DistrictEffectKind.Prestige, "which does nothing");

            var granary = rules.Find("granary");
            var food = granary.EffectsOf(DistrictEffectKind.Supply).Single();
            Check.True(food.ItemType == "provision" && food.ItemId == "" && food.Min == 4 && food.Max == 10, "4 to 10 food");
            Check.True(granary.EffectsOf(DistrictEffectKind.HeroBuff).Single().Tags.Count == 0, "the granary's buff is for everyone");
            Check.True(rules.Find("buskers_corner").Effects.Single().Amount == 10, "ten stress a week");
            var bonfire = rules.Find("outsiders_bonfire").Effects.Single();
            Check.True(bonfire.Kind == DistrictEffectKind.CampPoints && bonfire.Amount == 2 && bonfire.Tags.SequenceEqual(new[] { "outsiders_bonfire" }), "two respite points");
            var curio = rules.Find("library").EffectsOf(DistrictEffectKind.CurioStress).Single();
            Check.True(curio.Amount == 15 && curio.CurioTags.SequenceEqual(new[] { "Knowledge" }) && curio.Tags.SequenceEqual(new[] { "library" }), "knowledge curios");
            var light = rules.Find("illuminators_guild").Effects.Single();
            Check.True(light.Kind == DistrictEffectKind.Light && light.Darkness != null && light.DarknessLoot != null, "the camp carries its own tables");

            foreach (var building in rules.Buildings)
                foreach (var effect in building.EffectsOf(DistrictEffectKind.HeroBuff))
                    foreach (var id in effect.BuffIds)
                        Check.True(rules.Buff(id) != null, "buff of " + building.Id + " is in DD1's buff file: " + id);
            Check.True(rules.Buff("districts_theatre_3").Conditional && rules.Buff("districts_theatre_3").RuleText == "heroic_end", "the Finale's buff is under a rule");
            Check.Equal(0.04, rules.BuffAmount("districts_training_ring_1"), "+4 accuracy");
        }

        public static void HeroTagsReadFromDd1()
        {
            var rules = DistrictRules.Load(Dd1.Files);
            if (rules.Missing) throw new SkipException("this DD1 install has no districts");
            Check.True(rules.TagsOf("jester").Contains("theater"), "the jester's tag");
            Check.True(rules.TagsOf("plague_doctor").Contains("library") && rules.TagsOf("occultist").Contains("library"), "the library's readers");
            Check.True(rules.TagsOf("man_at_arms").Contains("training_ring"), "the man-at-arms trains");
            foreach (var cls in new[] { "abomination", "hellion", "leper" }) Check.True(rules.TagsOf(cls).Contains("outsiders_bonfire"), "an outsider: " + cls);
            foreach (var cls in new[] { "bounty_hunter", "grave_robber", "highwayman" }) Check.True(rules.TagsOf(cls).Contains("house_of_the_yellow_hand"), "of the yellow hand: " + cls);
            Check.True(rules.TagsOf("crusader").Contains("altar_of_light") && rules.TagsOf("vestal").Contains("altar_of_light"), "the altar's");
            Check.Equal(0, rules.TagsOf("no_such_class").Count, "a class DD1 does not know");
            Check.Equal(0, rules.TagsOf(null).Count, "nobody");
        }

        public static void StockTagsAndBuffsAreDd1s()
        {
            var read = DistrictRules.Load(Dd1.Files);
            if (read.Missing) throw new SkipException("this DD1 install has no districts");
            // the same buildings with nothing else of the install in reach
            var stock = DistrictRules.Parse(Dd1.Files.ReadText(DistrictRules.File), null, new NoDd1Files());
            Check.True(stock.StockBuffsInUse, "without the buffs file the stock buffs stand in");
            foreach (var building in read.Buildings)
                foreach (var effect in building.EffectsOf(DistrictEffectKind.HeroBuff))
                    foreach (var id in effect.BuffIds)
                    {
                        CampBuffDef a = read.Buff(id), b = stock.Buff(id);
                        Check.True(b != null && a.StatType == b.StatType && a.SubType == b.SubType && a.RuleType == b.RuleType && a.RuleText == b.RuleText, "stock buff " + id);
                        Check.Equal(read.BuffAmount(id), stock.BuffAmount(id), "amount of " + id);
                    }
            foreach (var cls in new[] { "abomination", "bounty_hunter", "crusader", "flagellant", "grave_robber", "hellion", "highwayman", "jester", "leper", "man_at_arms", "occultist", "plague_doctor", "vestal",
                         "antiquarian", "arbalest", "houndmaster" })
                Check.True(stock.TagsOf(cls).All(t => read.TagsOf(cls).Contains(t)) && stock.TagsOf(cls).Count == 1, "stock tag of " + cls);
        }

        public static void NoDistrictsWithoutDd1()
        {
            var rules = DistrictRules.Load(new NoDd1Files());
            Check.True(rules.Missing && rules.Buildings.Count == 0, "no buildings, no stand-in");
            var state = new DistrictState { Unlocked = true, Blueprints = 3 };
            Check.Equal(DistrictRefusal.NoSuchBuilding, rules.Quote(state, "bank", Purse(50000, 3)).Refusal, "nothing to build");
            Check.True(rules.Interest(state) == 0 && rules.IdleRelief(state) == 0 && rules.EatBonus(state) == 0 && rules.Light(state) == null, "nothing given");
            Check.True(rules.HeroEffect(state, "jester").Lines.Count == 0 && rules.CampPoints(state, new[] { "leper" }) == 0, "nothing for heroes");
            Check.True(DistrictRules.Parse("{\"enabled\":false,\"buildings\":[" + Building("off", 0, Cost("gold", 1), "") + "]}", Buffs).Missing, "districts DD1's file switches off are not there");
            Check.True(DistrictRules.Parse("not json", Buffs).Missing, "an unreadable file is no districts");
        }

        public static void BlueprintsComeFromDd1sBosses()
        {
            var sources = BlueprintSources.Load(Dd1.Files);
            if (DistrictRules.Load(Dd1.Files).Missing) throw new SkipException("this DD1 install has no districts");
            Check.True(!sources.Missing, "DD1's quests are read");
            foreach (var boss in new[] { "necromancer", "prophet", "hag", "brigand_cannon", "swine_prince", "siren", "drowned_crew" })
            {
                Check.Equal(0, sources.For("plot_kill_" + boss + "_1"), "no blueprint from the apprentice " + boss);
                Check.Equal(1, sources.For("plot_kill_" + boss + "_2"), "the veteran " + boss);
                Check.Equal(1, sources.For("plot_kill_" + boss + "_3"), "the champion " + boss);
            }
            for (var tier = 1; tier <= 3; tier++) Check.Equal(0, sources.For("plot_kill_formless_flesh_" + tier), "the Flesh carries none");
            Check.Equal(0, sources.For("plot_darkest_dungeon_1"), "nor the Darkest Dungeon");
            Check.Equal(0, sources.For(null), "no quest");
            Check.Equal(14, sources.Total, "fourteen in all");

            var stock = BlueprintSources.Load(new NoDd1Files());
            Check.True(stock.Missing, "without DD1 the stock list stands in");
            Check.True(stock.ByQuest.Count == sources.ByQuest.Count && stock.ByQuest.All(p => sources.For(p.Key) == p.Value), "and it is DD1's own");
        }

        public static void CartographersTablesReplaceDd1s()
        {
            var rules = DistrictRules.Load(Dd1.Files);
            if (rules.Missing) throw new SkipException("this DD1 install has no districts");
            var state = new DistrictState { Unlocked = true };
            Check.True(rules.Light(state) == null, "nothing built, DD1's own table");
            state.Built.Add("illuminators_guild");
            var light = rules.Light(state);
            Check.True(light != null, "the camp's table");

            var raid = RaidRules.Load(Dd1.Files);
            var before = raid.LightBands.Select(b => b.Value("player_scouting_increase")).ToList();
            var bands = raid.LightBands.Count;
            Check.True(raid.UseDarkness(light.Darkness), "the table is taken");
            Check.Equal(bands, raid.LightBands.Count, "as many bands");
            for (var i = 0; i < bands; i++) Check.Near(before[i] + 2.5, raid.LightBands[i].Value("player_scouting_increase"), 0.001, "scouting is 2.5 points better in band " + i);
            Check.Equal(0, raid.BandIndex(100), "full light is still the first band");
            Check.Equal(bands - 1, raid.BandIndex(0), "no light the last");
            Check.True(!raid.UseDarkness(null) && raid.LightBands.Count == bands, "no table changes nothing");

            var loot = LootTables.Load(Dd1.Files);
            var rng = new Rng(5);
            var bright = 0;
            for (var i = 0; i < 1000; i++) bright += loot.DarknessBonusCodes("battle", 100, rng).Count;
            Check.Equal(0, bright, "DD1: no extra loot in full light");
            loot.UseDarknessBonuses(light.DarknessLoot);
            for (var i = 0; i < 2000; i++) bright += loot.DarknessBonusCodes("battle", 100, rng).Count;
            Check.Near(0.25, bright / 2000.0, 0.04, "the camp: a quarter of the fights pay more even in full light");
        }

        // ---- reading -------------------------------------------------------------------------------------

        public static void BuildingsStandInTownOrder()
        {
            var rules = Small();
            Check.Equal("first, second, third", string.Join(", ", rules.Buildings.Select(b => b.Id)), "by town_priority");
            Check.Equal(DistrictRules.Feature + "fx/town_district_first/town_district_first.sprite.skel", rules.Find("first").Skeleton, "the art lies in the feature's folder");
            Check.Equal(DistrictRules.Feature + "fx/purchase_district/purchase_district.sprite.skel", rules.Find("first").PurchaseSkeleton, "and so does the purchase's, as far as anyone can tell without the install");
            Check.True(rules.Find("third").Effects.Last().Kind == DistrictEffectKind.Unknown && rules.Find("third").Effects.Last().Type == "DistrictOfTheFutureBuffData", "an unknown buff type is kept by name");
            var twice = Rules(Building("a", 0, Cost("gold", 50), ""), Building("a", 5, Cost("gold", 99), ""), Building("", 1, "", ""));
            Check.True(twice.Buildings.Count == 1 && twice.Buildings[0].Costs[0].Amount == 50, "a repeated or nameless building is none");
        }

        // ---- building ------------------------------------------------------------------------------------

        public static void PriceIsDd1sOwn()
        {
            var rules = Small();
            Check.Equal("15000 gold, 50 portrait, 1 blueprint", Text(DistrictRules.Price(rules.Find("first"))), "15000 of DD1's gold is 15000");
            Check.Equal("20 gold, 1 blueprint", Text(DistrictRules.Price(rules.Find("third"))), "and 20 is 20");
            var price = DistrictRules.Price(rules.Find("first"));
            price[0].Amount = 1;
            Check.Equal(15000, rules.Find("first").Costs[0].Amount, "a quoted price is a copy: the building's own stays");
        }

        public static void BuildingNeedsTheDistrictsOpenAndThePrice()
        {
            var rules = Small();
            var state = new DistrictState { Blueprints = 1 };
            var rich = Purse(50000, 1, "portrait", "60", "deed", "200");
            Check.Equal(DistrictRefusal.Locked, rules.Quote(state, "first", rich).Refusal, "before Cornerstones nothing is built");
            Check.Equal(DistrictRefusal.Locked, rules.Build(state, "first", rich).Refusal, "nor bought");
            Check.True(rich["gold"] == 50000 && state.Built.Count == 0, "a refused purchase changes nothing");

            state.Unlocked = true;
            Check.Equal(DistrictRefusal.NoSuchBuilding, rules.Quote(state, "castle", rich).Refusal, "no such building");
            var poor = rules.Quote(state, "first", Purse(14999, 1, "portrait", "60"));
            Check.True(poor.Refusal == DistrictRefusal.CannotPay && poor.ShortOf == "gold", "one gold short");
            var noArt = rules.Quote(state, "first", Purse(50000, 1, "portrait", "49"));
            Check.True(noArt.Refusal == DistrictRefusal.CannotPay && noArt.ShortOf == "portrait", "one portrait short");
            var noPlan = rules.Quote(state, "first", Purse(50000, 0, "portrait", "60"));
            Check.True(noPlan.Refusal == DistrictRefusal.CannotPay && noPlan.ShortOf == "blueprint", "no blueprint");
            Check.True(rules.Quote(state, "first", null).Refusal == DistrictRefusal.CannotPay, "an empty purse pays for nothing");

            var built = rules.Build(state, "first", rich);
            Check.True(built.Valid && state.Has("first"), "the bank stands");
            Check.True(rich["gold"] == 35000 && rich["portrait"] == 10 && rich["blueprint"] == 0 && rich["deed"] == 200, "and is paid for: " + string.Join(", ", rich.Select(p => p.Value + " " + p.Key)));
            Check.Equal(DistrictRefusal.AlreadyBuilt, rules.Build(state, "first", Purse(250000, 5, "portrait", "500")).Refusal, "once only");
            Check.Equal(1, state.Built.Count, "one building");
        }

        public static void StateSurvivesTheSave()
        {
            var state = new DistrictState { Unlocked = true, Blueprints = 4 };
            state.Built.Add("bank");
            state.Built.Add("granary");
            var back = DistrictState.FromJson(Newtonsoft.Json.Linq.JToken.Parse(state.ToJson().ToString()));
            Check.True(back.Unlocked && back.Blueprints == 4 && back.Built.SequenceEqual(new[] { "bank", "granary" }), "unlocked, blueprints, buildings in order");
            var fresh = DistrictState.FromJson(null);
            Check.True(!fresh.Unlocked && fresh.Blueprints == 0 && fresh.Built.Count == 0, "a save without districts");
            Check.Equal(0, DistrictState.FromJson(Newtonsoft.Json.Linq.JToken.Parse("{\"blueprints\":-3,\"built\":[\"a\",\"a\",7]}")).Blueprints, "no debts");
            Check.Equal(1, DistrictState.FromJson(Newtonsoft.Json.Linq.JToken.Parse("{\"built\":[\"a\",\"a\",7]}")).Built.Count, "a building stands once");
        }

        // ---- what the buildings give ---------------------------------------------------------------------

        public static void OnlyStandingBuildingsGive()
        {
            var rules = Small();
            var state = new DistrictState { Unlocked = true };
            Check.True(rules.Interest(state) == 0 && rules.IdleRelief(state) == 0 && rules.EatBonus(state) == 0, "nothing built, nothing given");
            Check.Equal(0, rules.RollSupply(state, "provision", "", new Rng(1)), "no food");
            Check.Equal(0, rules.CampPoints(state, new[] { "leper" }), "no points");
            Check.Equal(0, rules.HeroEffect(state, "highwayman").Lines.Count, "no buffs");

            state.Built.Add("first");
            Check.Equal(0.05, rules.Interest(state), "the bank's interest");
            Check.Equal(10.0, rules.IdleRelief(state), "the theatre's relief");
            Check.Equal(0.0, rules.EatBonus(state), "the granary is not built");
        }

        public static void FreeFoodIsRolledBetweenDd1sNumbers()
        {
            var rules = Small();
            var state = new DistrictState { Unlocked = true };
            state.Built.Add("third");
            var rng = new Rng(11);
            var seen = new HashSet<int>();
            for (var i = 0; i < 2000; i++) seen.Add(rules.RollSupply(state, "provision", "", rng));
            Check.True(seen.Min() == 4 && seen.Max() == 10 && seen.Count == 7, "4 to 10, every number: " + string.Join(" ", seen.OrderBy(n => n)));
            Check.Equal(0, rules.RollSupply(state, "supply", "torch", rng), "food only");
            Check.Equal(0.15, rules.EatBonus(state), "and meals heal more");
        }

        public static void HeroBuffsFollowTheTags()
        {
            var rules = Small();
            var state = new DistrictState { Unlocked = true };
            state.Built.Add("second");
            state.Built.Add("third");

            var thief = rules.HeroEffect(state, "highwayman");
            Check.Equal("add_stat,crit_chance,0.04\n", CampingBuffMap.Text(thief.Lines), "crit as DD2 writes it");
            Check.Equal(0.05, thief.Scouting, "scouting is the party's");
            Check.True(thief.LeftOut.SequenceEqual(new[] { "finale", "odd", "gone" }), "a buff under a rule, an unknown stat and a buff DD1 does not list are not given: " + string.Join(", ", thief.LeftOut));
            Check.True(thief.Given.SequenceEqual(new[] { "crit", "scout", "eat" }), "what was given: " + string.Join(", ", thief.Given));

            var other = rules.HeroEffect(state, "jester");
            Check.True(other.Lines.Count == 0 && other.Scouting == 0 && other.Given.SequenceEqual(new[] { "eat" }), "another class gets only what is for everyone");
            Check.True(rules.AppliesTo(rules.Find("third").Effects[0], "no_such_class"), "an entry without tags is for everyone");
            Check.True(!rules.AppliesTo(rules.Find("second").Effects[0], "jester"), "an entry with tags is not");
        }

        public static void Dd1BuffsMapOntoDd2Stats()
        {
            var rules = DistrictRules.Load(Dd1.Files);
            if (rules.Missing) throw new SkipException("this DD1 install has no districts");
            var state = new DistrictState { Unlocked = true };
            foreach (var building in rules.Buildings) state.Built.Add(building.Id);

            Check.Equal("sub_stat,resistance,stun,0.1\nadd_stat,health_heal_dealt_percent,0.1\n", CampingBuffMap.Text(rules.HeroEffect(state, "vestal").Lines), "the altar");
            Check.Equal("add_stat,health_damage_dealt_percent,0.04\nmultiply_stat,health_max,0.1\n", CampingBuffMap.Text(rules.HeroEffect(state, "man_at_arms").Lines), "the ring: accuracy as damage");
            Check.Equal("sub_stat,resistance_ignore,blight,0.15\nsub_stat,resistance_ignore,debuff,0.15\n", CampingBuffMap.Text(rules.HeroEffect(state, "plague_doctor").Lines), "the athenaeum");
            var jester = rules.HeroEffect(state, "jester");
            Check.Equal("sub_stat,resistance,stress,0.1\nadd_stat,speed,2\n", CampingBuffMap.Text(jester.Lines), "the hall");
            Check.True(jester.LeftOut.SequenceEqual(new[] { "districts_theatre_3", "districts_theatre_4" }), "the Finale's damage is left out");
            var thief = rules.HeroEffect(state, "grave_robber");
            Check.True(CampingBuffMap.Text(thief.Lines) == "add_stat,crit_chance,0.04\n" && thief.Scouting == 0.05, "the yellow hand");
            Check.True(rules.HeroEffect(state, "leper").Lines.Count == 0 && rules.HeroEffect(state, "duelist").Lines.Count == 0, "nothing for the outsiders' stats, nothing for a class without a building");
            Check.Equal(0.15, rules.EatBonus(state), "the granary");
            Check.Equal(0.05, rules.Interest(state), "the bank");
            Check.Equal(10.0, rules.IdleRelief(state), "the puppet theatre");
        }

        public static void CampPointsNeedAnOutsider()
        {
            var rules = Small();
            var state = new DistrictState { Unlocked = true };
            state.Built.Add("third");
            Check.Equal(0, rules.CampPoints(state, new[] { "vestal", "crusader", "jester", "highwayman" }), "nobody of the tag");
            Check.Equal(2, rules.CampPoints(state, new[] { "vestal", "leper" }), "one is enough");
            Check.Equal(2, rules.CampPoints(state, new[] { "leper", "hellion", "abomination", "runaway" }), "four are no more than one");
            Check.Equal(0, rules.CampPoints(state, null), "no party");
        }

        public static void KnowledgeCalmsTheLearned()
        {
            var rules = Small();
            var state = new DistrictState { Unlocked = true };
            state.Built.Add("second");
            Check.Equal(15.0, rules.CurioStressHeal(state, "occultist", new[] { "Knowledge", "All" }), "a reader at a bookshelf");
            Check.Equal(15.0, rules.CurioStressHeal(state, "plague_doctor", new[] { "knowledge" }), "whatever the case of the tag");
            Check.Equal(0.0, rules.CurioStressHeal(state, "occultist", new[] { "Treasure" }), "another curio");
            Check.Equal(0.0, rules.CurioStressHeal(state, "leper", new[] { "Knowledge" }), "another hero");
            Check.Equal(0.0, rules.CurioStressHeal(state, "occultist", null), "a curio without tags");
        }
    }
}
