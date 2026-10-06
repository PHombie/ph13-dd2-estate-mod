using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// DD1's camping rules as the core reads them. The expected numbers are the ones in a stock DD1 install
    /// (shared/rules.json, campaign/provision/provision.json, raid/camping/default.camping_skills.json,
    /// shared/buffs/base.buffs.json); a modded install may fail the "stock" tests and still be read right.
    /// </summary>
    public static class CampingTests
    {
        private static CampingRules _rules;

        private static CampingRules Rules => _rules ?? (_rules = CampingRules.Load(Dd1.Files));

        private static bool HasFiresEdge => Directory.Exists(Path.Combine(Dd1.Root, "dlc", "4964110_fires_edge", "features", "runaway", "raid", "camping"));

        private static bool HasFlagellant => Directory.Exists(Path.Combine(Dd1.Root, "dlc", "580100_crimson_court", "features", "flagellant", "raid", "camping"));

        private static CampHero Hero(uint id, string cls, params string[] skills)
        {
            return new CampHero { Id = id, ClassId = cls, Religious = Rules.IsReligious(cls), Skills = skills.ToList() };
        }

        private static CampSession Camp(long seed = 1)
        {
            return new CampSession(Rules, new[]
            {
                Hero(1, "crusader", "encourage", "zealous_speech", "zealous_vigil", "stand_tall"),
                Hero(2, "highwayman", "first_aid", "gallows_humor", "bandits_sense", "clean_guns"),
                Hero(3, "plague_doctor", "leeches", "experimental_vapours", "pep_talk", "self_medicate"),
                Hero(4, "vestal", "chant", "pray", "sanctuary", "bless")
            }, seed);
        }

        // ---- rules -----------------------------------------------------------------------------------

        public static void StockRulesAreRead()
        {
            var rules = Rules;
            Check.Equal(12, rules.StartPoints, "respite points (camp_start_camping_points)");
            Check.Near(0.33, rules.AmbushChance, 1e-9, "night ambush chance (ambush_camping_base_chance)");
            Check.Near(100, rules.RestoreTorch, 1e-9, "torchlight a camp restores");
            Check.Near(-100, rules.AmbushTorch, 1e-9, "torchlight an ambush takes");
            Check.Near(1, rules.AmbushPartySurprise, 1e-9, "party surprised in an ambush");
            Check.Near(0, rules.AmbushMonstersSurprise, 1e-9, "monsters surprised in an ambush");
            Check.Equal(4, rules.SharedThreshold, "classes that make a skill a shared one");

            Check.Equal("none,half,full,feast", string.Join(",", rules.Meals.Select(m => m.Type)), "meals in file order");
            var none = rules.Meal("none");
            Check.Near(-0.20, none.Healing, 1e-9, "going without: health");
            Check.Near(15, none.Stress, 1e-9, "going without: stress");
            Check.Near(0, rules.Meal("half").Healing, 1e-9, "half: health");
            Check.Near(0.10, rules.Meal("full").Healing, 1e-9, "full: health");
            Check.Near(0.25, rules.Meal("feast").Healing, 1e-9, "feast: health");
            Check.Near(-10, rules.Meal("feast").Stress, 1e-9, "feast: stress");
            Check.Equal(0, rules.FoodFor(none, 4), "food for no meal");
            Check.Equal(2, rules.FoodFor(rules.Meal("half"), 4), "half rations for four");
            Check.Equal(2, rules.FoodFor(rules.Meal("half"), 3), "half rations for three round up");
            Check.Equal(4, rules.FoodFor(rules.Meal("full"), 4), "full rations for four");
            Check.Equal(8, rules.FoodFor(rules.Meal("feast"), 4), "a feast for four");
        }

        public static void FirewoodFollowsQuestLength()
        {
            Check.Equal(0, Rules.Firewood(1), "short quest");
            Check.Equal(1, Rules.Firewood(2), "medium quest");
            Check.Equal(2, Rules.Firewood(3), "long quest");
            Check.Equal(0, Rules.Firewood(99), "a length DD1 does not know");
            Check.Equal(0, Rules.Firewood(-1), "a negative length");

            // the same number the provision rules hand the party as items
            var provision = ProvisionRules.Load(Dd1.Files, ItemCatalog.Load(Dd1.Files));
            for (var length = 1; length <= 3; length++)
                Check.Equal(Rules.Firewood(length), provision.StartingItems(length).Where(s => s.Item.Id == "firewood").Sum(s => s.Amount), "firewood of length " + length);
        }

        public static void SkillsMatchTheFile()
        {
            // read once more with nothing but JSON, to check the reader against
            var raw = JObject.Parse(File.ReadAllText(Path.Combine(Dd1.Root, "raid", "camping", "default.camping_skills.json")).TrimStart((char)0xFEFF));
            foreach (var entry in (JArray)raw["skills"])
            {
                var skill = Rules.Skill((string)entry["id"]);
                Check.True(skill != null, "skill " + entry["id"] + " is read");
                Check.Equal((int)entry["cost"], skill.Cost, skill.Id + " cost");
                Check.Equal((int)entry["use_limit"], skill.UseLimit, skill.Id + " uses");
                Check.Equal(((JArray)entry["effects"]).Count, skill.Effects.Count, skill.Id + " effects");
                Check.Equal((int)entry["upgrade_requirements"][0]["currency_cost"][0]["amount"], skill.PriceGold, skill.Id + " price");
                foreach (var cls in (JArray)entry["hero_classes"]) Check.True(skill.Classes.Contains((string)cls), skill.Id + " is open to " + cls);
            }

            var encourage = Rules.Skill("encourage");
            Check.Equal(2, encourage.Cost, "encourage cost");
            Check.Equal(1750, encourage.PriceGold, "encourage price");
            Check.True(encourage.NeedsTarget, "encourage is aimed at a companion");
            Check.Equal(CampEffectTypes.StressHeal, encourage.Effects[0].Type, "encourage relieves stress");
            Check.Near(15, encourage.Effects[0].Amount, 1e-9, "encourage amount");
            Check.True(Rules.IsShared(encourage), "encourage is shared");
            Check.True(!Rules.IsShared(Rules.Skill("zealous_vigil")), "zealous vigil is the crusader's");
            Check.Equal("raid/camping/skill_icons/camp_skill_encourage.png", encourage.IconPath, "icon path");
            Check.True(Dd1.Files.Exists(encourage.IconPath), "the icon exists");
            foreach (var skill in Rules.Skills) Check.True(Dd1.Files.Exists(skill.IconPath), "icon of " + skill.Id + " at " + skill.IconPath);
        }

        public static void ClassesGetTheirSkills()
        {
            var crusader = Rules.SkillsFor("crusader").Select(s => s.Id).ToList();
            Check.Equal("unshakeable_leader,stand_tall,zealous_speech,zealous_vigil,encourage,first_aid,pep_talk", string.Join(",", crusader), "the crusader's seven, his own first");
            Check.Equal(4, Rules.ClassSkills("highwayman").Count, "the highwayman's own (gallows humor is shared with the grave robber)");
            Check.True(Rules.ClassSkills("highwayman").Any(s => s.Id == "gallows_humor"), "gallows humor is the highwayman's too");
            Check.Equal(0, Rules.SkillsFor("crusader").Count(s => s.Id == "hobby"), "a skill no class is listed for is open to none");

            // every DD2 hero with a DD1 namesake has four of their own and the shared three
            foreach (var cls in new[] { "crusader", "highwayman", "plague_doctor", "occultist", "vestal", "jester", "leper", "hellion", "grave_robber", "man_at_arms", "abomination", "bounty_hunter" })
            {
                Check.Equal(4, Rules.ClassSkills(cls).Count, cls + ": class skills");
                Check.Equal(7, Rules.SkillsFor(cls).Count, cls + ": skills in all");
                Check.True(!Rules.UsesAnalogue(cls), cls + " needs no analogue");
            }
            Check.True(Rules.IsReligious("crusader") && Rules.IsReligious("vestal") && Rules.IsReligious("leper"), "DD1's religious classes");
            Check.True(!Rules.IsReligious("highwayman") && !Rules.IsReligious("occultist") && !Rules.IsReligious("no_such_class"), "the others are not");
        }

        public static void DlcClassesAndAnalogues()
        {
            if (HasFlagellant)
            {
                Check.Equal("lash_anger,lash_solace,lash_kiss,lash_cure", string.Join(",", Rules.ClassSkills("flagellant").Select(s => s.Id)), "the flagellant's own");
                // DD1's file lists him for none of the shared three, his hero file gives him one at the start
                Check.Equal(7, Rules.SkillsFor("flagellant").Count, "the flagellant learns the shared skills too");
                Check.True(Rules.IsReligious("flagellant"), "the flagellant is religious");
            }
            if (HasFiresEdge)
            {
                Check.Equal("kindle,cauterize,play_with_fire,pickpocket", string.Join(",", Rules.ClassSkills("runaway").Select(s => s.Id)), "the runaway's own");
                Check.Equal("meditation,preparation,ruthless_instruction,again", string.Join(",", Rules.ClassSkills("duelist").Select(s => s.Id)), "the duelist's own");
                Check.True(!Rules.UsesAnalogue("runaway") && !Rules.UsesAnalogue("duelist"), "DD1's own skills win over the analogue");
                Check.True(Rules.Skill("kindle").IconPath.StartsWith("dlc/"), "a DLC skill's icon is in the DLC's folder");
                Check.Equal(3, Rules.Skill("pickpocket").UseLimit, "pickpocket can be used three times");
                Check.True(Rules.Skill("meditation").Effects[0].Buff != null && Rules.Skill("meditation").Effects[0].Buff.RuleType == "has_buff", "a DLC buff is read with its rule");
            }

            // An install without the DLC: the analogue's class skills stand in.
            var bare = CampingRules.Load(new WithoutFolder(Dd1.Files, "dlc/"));
            Check.True(bare.UsesAnalogue("runaway") && bare.UsesAnalogue("duelist"), "no DLC: analogues");
            Check.Equal(string.Join(",", bare.ClassSkills("grave_robber").Select(s => s.Id)), string.Join(",", bare.ClassSkills("runaway").Select(s => s.Id)), "the runaway camps like the grave robber");
            Check.Equal(string.Join(",", bare.ClassSkills("man_at_arms").Select(s => s.Id)), string.Join(",", bare.ClassSkills("duelist").Select(s => s.Id)), "the duelist camps like the man-at-arms");
            Check.Equal(7, bare.SkillsFor("runaway").Count, "the analogue's four and the shared three");
            Check.Equal(3, bare.RollStartingSkills("duelist", new Rng(5)).Count, "a recruit without DD1 files still arrives with skills");
        }

        public static void RecruitsArriveWithThreeSkills()
        {
            for (var seed = 1; seed <= 40; seed++)
            {
                var book = CampSkillBook.Roll(Rules, "hellion", new Rng(seed));
                Check.Equal(3, book.Known.Count, "skills known at hire");
                Check.Equal(2, book.Known.Count(id => !Rules.IsShared(Rules.Skill(id))), "class skills at hire (number_of_class_specific_camping_skills)");
                Check.Equal(1, book.Known.Count(id => Rules.IsShared(Rules.Skill(id))), "shared skills at hire (number_of_shared_camping_skills)");
                Check.Equal(3, book.Active.Count, "all of them ready");
            }
            var seen = new HashSet<string>();
            for (var seed = 1; seed <= 200; seed++)
                foreach (var id in CampSkillBook.Roll(Rules, "hellion", new Rng(seed)).Known) seen.Add(id);
            Check.Equal(7, seen.Count, "every skill of the class turns up on some recruit");
        }

        public static void SkillBookLearnsAndToggles()
        {
            var limit = Rules.ActiveLimit;
            Check.Equal(4, limit, "skills ready at a time");
            var book = new CampSkillBook { ClassId = "crusader" };
            Check.True(book.Learn(Rules, "clean_guns") != null, "not a crusader's skill");
            foreach (var id in new[] { "encourage", "stand_tall", "zealous_speech", "zealous_vigil" }) Check.True(book.Learn(Rules, id) == null, "learns " + id);
            Check.True(book.Learn(Rules, "encourage") != null, "not twice");
            Check.Equal(4, book.Active.Count, "the first four are ready at once");
            Check.True(book.Learn(Rules, "first_aid") == null, "a fifth can be learned");
            Check.True(!book.IsActive("first_aid"), "but it is not ready: the places are taken");
            Check.True(book.Toggle(Rules, "first_aid") != null, "nor can it be made ready over the limit");
            Check.True(book.Toggle(Rules, "encourage") == null && !book.IsActive("encourage"), "putting one aside");
            Check.True(book.Toggle(Rules, "first_aid") == null && book.IsActive("first_aid"), "makes room");
            Check.True(book.Toggle(Rules, "pep_talk") != null, "an unknown skill cannot be readied");

            var copy = CampSkillBook.FromJson(JToken.Parse(book.ToJson().ToString()));
            Check.Equal(string.Join(",", book.Known), string.Join(",", copy.Known), "known skills survive the save");
            Check.Equal(string.Join(",", book.Active), string.Join(",", copy.Active), "ready skills survive the save");

            copy.Known.Add("no_such_skill");
            copy.Active.Add("no_such_skill");
            copy.Repair(Rules);
            Check.True(!copy.Knows("no_such_skill") && copy.Active.Count <= limit, "repair drops what the install no longer has");

            Check.True(Rules.ReadActiveLimit("Heroes can only have {colour_start|notable}4 Camping skills active{colour_end} at a time."), "DD1's tutorial text is understood");
            Check.Equal(4, Rules.ActiveLimit, "and says four");
            Check.True(!Rules.ReadActiveLimit("no number here") && Rules.ActiveLimit == 4, "a text without a number changes nothing");
            var text = File.ReadLines(Path.Combine(Dd1.Root, "localization", "miscellaneous.string_table.xml"))
                .Select(l => Regex.Match(l, "<entry id=\"tutorial_popup_map_camping_skills_description\"><!\\[CDATA\\[(.*?)\\]\\]>"))
                .FirstOrDefault(m => m.Success);
            Check.True(text != null && Rules.ReadActiveLimit(text.Groups[1].Value), "the install's own tutorial text names the limit");
        }

        // ---- a camp ----------------------------------------------------------------------------------

        public static void MealComesFirst()
        {
            var camp = Camp();
            Check.Equal(CampPhase.Meal, camp.Phase, "a camp starts with the meal");
            Check.Equal(12, camp.Points, "respite points");
            Check.True(camp.Refusal(1, "encourage", 2) != null, "no skills before the meal");
            Check.True(camp.Sleep() == null, "no sleep before the meal");
            Check.True(camp.MealRefusal("feast", 7) != null, "a feast for four takes eight");
            Check.Equal(0, camp.Eat("feast", 7).Count, "a refused meal does nothing");
            Check.Equal(CampPhase.Meal, camp.Phase, "and the party has still to eat");

            var actions = camp.Eat("feast", 8);
            Check.Equal(CampPhase.Skills, camp.Phase, "then the skills");
            Check.Equal("feast", camp.Meal, "the meal is remembered");
            Check.Equal(4, actions.Count(a => a.Type == CampEffectTypes.HealthHeal && a.Amount == 0.25), "a feast heals a quarter");
            Check.Equal(4, actions.Count(a => a.Type == CampEffectTypes.StressHeal && a.Amount == 10), "and relieves 10 stress");
            Check.Equal(0, camp.Eat("full", 8).Count, "one meal a camp");

            var hungry = Camp();
            actions = hungry.Eat("none", 0);
            Check.Equal(4, actions.Count(a => a.Type == CampEffectTypes.HealthDamage && a.Amount == 0.20), "going without costs a fifth of health");
            Check.Equal(4, actions.Count(a => a.Type == CampEffectTypes.StressDamage && a.Amount == 15), "and 15 stress");
            Check.Equal(0, Camp().Eat("half", 2).Count, "half rations do nothing either way");
        }

        public static void SkillsSpendRespite()
        {
            var camp = Camp();
            camp.Eat("half", 2);
            Check.True(camp.Refusal(1, "encourage") != null, "encourage needs a companion");
            Check.True(camp.Refusal(1, "encourage", 1) != null, "not oneself");
            Check.True(camp.Refusal(1, "encourage", 9) != null, "nor a stranger");
            Check.True(camp.Refusal(1, "clean_guns") != null, "nor another's skill");
            Check.True(camp.Refusal(9, "encourage", 1) != null, "nor by a stranger");

            var actions = camp.Use(1, "encourage", 2);
            Check.Equal(1, actions.Count, "encourage: one effect");
            Check.True(actions[0].Hero == 2 && actions[0].Source == 1 && actions[0].Type == CampEffectTypes.StressHeal && actions[0].Amount == 15, "15 stress off the companion");
            Check.Equal(10, camp.Points, "two points spent");
            Check.Equal(0, camp.UsesLeft(1, "encourage"), "used up for this camp");
            Check.True(camp.Refusal(1, "encourage", 2) != null, "not twice");
            Check.Equal(0, camp.Use(1, "encourage", 2).Count, "a refused skill does nothing");
            Check.Equal(10, camp.Points, "and costs nothing");

            actions = camp.Use(1, "zealous_speech");
            Check.Equal(5, camp.Points, "five points for the speech");
            Check.Equal(4, actions.Count(a => a.Type == CampEffectTypes.StressHeal), "the whole party is heartened");
            Check.Equal(3, actions.Count(a => a.Type == CampEffectTypes.Buff && a.SubType == "campingStressResistBuff" && a.Hero != 1 && a.Amount == -0.15), "the companions are steeled");
            Check.True(actions.Where(a => a.Type == CampEffectTypes.Buff).All(a => a.Buff != null && a.Buff.StatType == "stress_dmg_received_percent"), "with DD1's buff behind it");

            camp.Use(1, "zealous_vigil");
            Check.Equal(1, camp.Points, "four for the vigil");
            Check.True(camp.Refusal(1, "stand_tall", 2) != null, "three points are not there any more");
        }

        public static void RequirementsDecideWhoGains()
        {
            var camp = Camp();
            camp.Eat("half", 2);
            // chant on a religious companion: 20% resolve and 15 stress; on another: 10% and 5
            var onCrusader = camp.Use(4, "chant", 1);
            Check.True(onCrusader.Any(a => a.Type == CampEffectTypes.StressHeal && a.Amount == 15) && onCrusader.Any(a => a.Type == CampEffectTypes.Buff && a.Amount == -0.2), "chant for the faithful");
            var other = Camp();
            other.Eat("half", 2);
            var onRogue = other.Use(4, "chant", 2);
            Check.True(onRogue.Any(a => a.Type == CampEffectTypes.StressHeal && a.Amount == 5) && onRogue.Any(a => a.Type == CampEffectTypes.Buff && a.Amount == -0.1), "chant for the rest");
            Check.Equal(2, onRogue.Count, "and nothing more");

            // the vigil's second relief is for the afflicted only
            var calm = Camp();
            calm.Eat("half", 2);
            Check.Equal(1, calm.Use(1, "zealous_vigil").Count(a => a.Type == CampEffectTypes.StressHeal), "a calm crusader: 25");
            var shaken = Camp();
            shaken.Hero(1).Afflicted = true;
            shaken.Eat("half", 2);
            Check.Near(40, shaken.Use(1, "zealous_vigil").Where(a => a.Type == CampEffectTypes.StressHeal).Sum(a => a.Amount), 1e-9, "an afflicted one: 25 and 15");

            // sanctuary mends only those marked by death's door
            var marked = Camp();
            marked.Hero(2).Mortality = true;
            marked.Eat("half", 2);
            var mended = marked.Use(4, "sanctuary");
            Check.True(mended.Count(a => a.Type == CampEffectTypes.HealthHeal) == 1 && mended.First(a => a.Type == CampEffectTypes.HealthHeal).Hero == 2, "sanctuary heals the marked");
            Check.Near(0, marked.AmbushChance, 1e-9, "and a religious vestal keeps the night quiet");
        }

        public static void SharedChanceCodesAreOneRoll()
        {
            // gallows humor: every companion either laughs (75%: -20 stress) or does not (25%: +10), never both
            int laughed = 0, soured = 0;
            for (var seed = 1; seed <= 400; seed++)
            {
                var camp = Camp(seed);
                camp.Eat("half", 2);
                var actions = camp.Use(2, "gallows_humor");
                Check.Equal(1, actions.Count(a => a.Hero == 2), "the joker relieves his own stress");
                foreach (var companion in new uint[] { 1, 3, 4 })
                {
                    var own = actions.Where(a => a.Hero == companion).ToList();
                    Check.Equal(1, own.Count, "one outcome per companion");
                    if (own[0].Type == CampEffectTypes.StressHeal) laughed++;
                    else soured++;
                }
            }
            Check.Near(0.75, laughed / (double)(laughed + soured), 0.04, "three laughs in four");
        }

        public static void NightAmbushAndPrevention()
        {
            var ambushes = 0;
            const int nights = 3000;
            for (var seed = 1; seed <= nights; seed++)
            {
                var camp = Camp(seed);
                camp.Eat("half", 2);
                Check.Near(0.33, camp.AmbushChance, 1e-9, "DD1's base chance");
                var night = camp.Sleep();
                Check.Equal(CampPhase.Over, camp.Phase, "sleep ends the camp");
                if (!night.Ambushed) continue;
                ambushes++;
                Check.True(night.PartySurprised && !night.MonstersSurprised, "an ambushed party is surprised (surprise_ambush_party_base_chance 1)");
            }
            Check.Near(0.33, ambushes / (double)nights, 0.03, "a third of the nights");

            for (var seed = 1; seed <= 300; seed++)
            {
                var camp = Camp(seed);
                camp.Eat("half", 2);
                var actions = camp.Use(1, "zealous_vigil");
                Check.True(actions.Any(a => a.Type == CampEffectTypes.ReduceAmbush && a.Amount == 1), "the vigil takes the whole chance off");
                Check.Near(0, camp.AmbushChance, 1e-9, "no ambush under a vigil");
                Check.True(!camp.Sleep().Ambushed, "none at all");
                Check.True(camp.Sleep() == null && camp.Refusal(1, "encourage", 2) != null, "a camp that is over stays over");
            }

            // the highwayman's sense: no ambush, and its surprise buffs are remembered for the roll
            var wary = Camp();
            wary.Eat("half", 2);
            var sense = wary.Use(2, "bandits_sense");
            Check.True(sense.Any(a => a.Type == CampEffectTypes.Buff && a.Buff != null && a.Buff.StatType == "party_surprise_chance" && a.Amount == -0.2), "less likely to be surprised");
            Check.Near(0, wary.AmbushChance, 1e-9, "and no ambush");
        }

        public static void CampSurvivesTheSave()
        {
            var camp = Camp(77);
            camp.Eat("full", 4);
            camp.Use(1, "encourage", 3);
            camp.Use(2, "bandits_sense");
            var copy = CampSession.FromJson(JToken.Parse(camp.ToJson().ToString()), Rules, camp.Party);
            Check.Equal(camp.Phase, copy.Phase, "phase");
            Check.Equal(camp.Points, copy.Points, "points");
            Check.Equal("full", copy.Meal, "meal");
            Check.Equal(0, copy.UsesLeft(1, "encourage"), "what was used stays used");
            Check.Near(camp.AmbushChance, copy.AmbushChance, 1e-9, "ambush prevention");
            // the same dice from here on
            var a = camp.Use(2, "gallows_humor");
            var b = copy.Use(2, "gallows_humor");
            Check.Equal(string.Join(";", a.Select(x => x.ToString())), string.Join(";", b.Select(x => x.ToString())), "the same rolls after a reload");

            var fresh = CampSession.FromJson(null, Rules, camp.Party);
            Check.True(fresh.Phase == CampPhase.Meal && fresh.Points == Rules.StartPoints, "nothing saved: a camp at its start");
        }

        public static void DuelistRefreshesACompanionsSkills()
        {
            if (!HasFiresEdge) throw new SkipException("needs DD1's Fire's Edge DLC");
            var camp = new CampSession(Rules, new[]
            {
                Hero(1, "duelist", "again", "encourage"),
                Hero(2, "crusader", "zealous_vigil", "encourage")
            }, 3);
            camp.Eat("half", 1);
            camp.Use(2, "encourage", 1);
            Check.Equal(0, camp.UsesLeft(2, "encourage"), "used");
            var actions = camp.Use(1, "again", 2);
            Check.True(actions.Any(a => a.Type == CampEffectTypes.RefreshUses && a.Hero == 2), "again: the companion's skills are fresh");
            Check.True(actions.Any(a => a.Type == CampEffectTypes.StressDamage && a.Hero == 2 && a.Amount == 15), "at a price in stress");
            Check.Equal(1, camp.UsesLeft(2, "encourage"), "and can be used once more");
            Check.Equal(0, camp.UsesLeft(1, "again"), "the duelist's own use is spent");
        }

        // ---- buffs -----------------------------------------------------------------------------------

        public static void BuffsAreReadWithTheirRules()
        {
            var acc = Rules.Buff("campingACCBuff");
            Check.True(acc != null && acc.StatType == "combat_stat_add" && acc.SubType == "attack_rating" && !acc.Conditional, "accuracy buff");
            Check.Equal("combat_end", acc.DurationType, "a camping buff counts fights");
            Check.Equal(4, acc.Duration, "four of them");
            Check.Equal("rangedonly", Rules.Buff("campingDMGLowBuffRanged").RuleType, "a ranged-only buff");
            Check.Equal("monsterSize", Rules.Buff("campingACCBuffLargeMonsters").RuleType, "a large-monster buff");
            var notFront = Rules.Buff("campingDMGLowBuffNotFrontRank");
            Check.True(notFront.RuleType == "in_rank" && notFront.RuleFalse && notFront.RuleNumber == 0, "a not-in-the-front-rank buff");
            foreach (var skill in Rules.Skills)
                foreach (var effect in skill.Effects)
                    if (effect.Type == CampEffectTypes.Buff) Check.True(effect.Buff != null, skill.Id + ": buff " + effect.SubType + " is in DD1's buff files");
        }

        public static void BuffsMapOntoDd2Stats()
        {
            string Text(string buff, double amount, int rank = -1) => CampingBuffMap.Text(CampingBuffMap.Lines(Rules.Buff(buff), amount, rank));

            Check.Equal("add_stat,health_damage_dealt_percent,0.1\n", Text("campingACCBuff", 0.1), "accuracy is damage dealt");
            Check.Equal("add_stat,health_damage_dealt_percent,0.1\n", Text("campingDMGLowBuff", 0.2), "low damage is half the damage");
            Check.Equal("add_stat,crit_chance,0.08\n", Text("campingCRITBuff", 0.08), "crit is crit");
            Check.Equal("add_stat,health_damage_received_percent,-0.1\n", Text("campingDEFBuff", 0.1), "dodge is damage not taken");
            Check.Equal("add_stat,health_damage_received_percent,-0.15\n", Text("campingPROTBuff", 0.15), "so is protection");
            Check.Equal("add_stat,speed,2\n", Text("campingSPDBuff", 2), "speed is speed");
            Check.Equal("sub_stat,resistance,stress,0.15\n", Text("campingStressResistBuff", -0.15), "less stress taken is stress resisted");
            Check.Equal("sub_stat,resistance,stress,-0.2\n", Text("campingStressResistBuff", 0.2), "and more stress taken is less of it");
            Check.Equal("sub_stat,resistance,blight,0.25\n", Text("campingBlightResistBuff", 0.25), "DD1's poison is DD2's blight");
            Check.Equal("sub_stat,resistance,disease,0.2\n", Text("campingDiseaseResistBuff", 0.2), "disease resistance");
            Check.Equal("add_stat,health_heal_received_percent,0.33\n", Text("campingHealReceivedBuff", 0.33), "healing received");

            // rules: half always, except the rank, which the fight's start answers
            Check.Equal("add_stat,health_damage_dealt_percent,0.05\n", Text("campingDMGLowBuffRanged", 0.2), "a ranged-only buff counts half");
            Check.Equal("add_stat,crit_chance,0.04\n", Text("campingCRITBuffRanged", 0.08), "so does its crit");
            Check.Equal("add_stat,health_damage_dealt_percent,0.125\n", Text("campingDMGLowBuffFrontRank", 0.25, 0), "in the front rank: all of it");
            Check.Equal("", Text("campingDMGLowBuffFrontRank", 0.25, 2), "behind: none of it");
            Check.Equal("", Text("campingDMGLowBuffNotFrontRank", -0.25, 0), "the penalty for standing back spares the front");
            Check.Equal("add_stat,health_damage_dealt_percent,-0.125\n", Text("campingDMGLowBuffNotFrontRank", -0.25, 3), "and lands on the back");

            Check.Equal(CampBuffKind.Scouting, CampingBuffMap.KindOf(Rules.Buff("campingScoutingBuff")), "scouting is the expedition's");
            Check.Equal(CampBuffKind.PartySurprise, CampingBuffMap.KindOf(Rules.Buff("campingPartySurprise")), "surprise is the fight's");
            Check.Equal(CampBuffKind.MonstersSurprise, CampingBuffMap.KindOf(Rules.Buff("campingMonstersSurprise")), "both ways");
            Check.Equal(CampBuffKind.Stat, CampingBuffMap.KindOf(Rules.Buff("campingACCBuff")), "the rest are stats");
            Check.Equal(CampBuffKind.None, CampingBuffMap.KindOf(null), "an unknown buff is nothing");

            // every buff a DD2 hero's skills hand out has a counterpart, bar the ones named here
            var without = new List<string>();
            foreach (var cls in new[] { "crusader", "highwayman", "plague_doctor", "occultist", "vestal", "jester", "leper", "hellion", "grave_robber", "man_at_arms", "flagellant", "abomination", "bounty_hunter", "runaway", "duelist" })
                foreach (var skill in Rules.SkillsFor(cls))
                    foreach (var effect in skill.Effects)
                        if (effect.Type == CampEffectTypes.Buff && CampingBuffMap.KindOf(effect.Buff) == CampBuffKind.None && !without.Contains(effect.SubType)) without.Add(effect.SubType);
            Check.Equal("", string.Join(",", without), "buffs without a DD2 counterpart");

            if (HasFiresEdge)
            {
                var fire = Rules.Buff("rw_play_with_fire_burn_on_hit");
                Check.Equal(CampBuffKind.Effect, CampingBuffMap.KindOf(fire), "play with fire is one of DD2's own effects");
                Check.Equal("runaway_firestarter_burn_buff", CampingBuffMap.EffectsOf(fire), "DD2's firestarter");
                Check.Equal(0, CampingBuffMap.Lines(fire, 0, -1).Count, "and no stat");
                var ledger = new CampBuffLedger();
                ledger.Add(3, "play_with_fire", fire, 0);
                ledger.Add(3, "play_with_fire", Rules.Buff("rw_play_with_fire_dmg_taken"), 0.1);
                Check.Equal("runaway_firestarter_burn_buff", string.Join(",", ledger.EffectIds(Rules, 3)), "the hero carries the effect");
                Check.Equal(0, ledger.EffectIds(Rules, 4).Count, "nobody else does");
                Check.Equal("add_stat,health_damage_received_percent,0.1\n", ledger.StatsText(Rules, 3, 0), "at its price in damage taken");
            }
            Check.True(CampingBuffMap.EffectsOf(Rules.Buff("campingACCBuff")) == null && CampingBuffMap.EffectsOf(null) == null, "a plain buff is no effect");
        }

        public static void BuffLedgerLastsFourFights()
        {
            var ledger = new CampBuffLedger();
            Check.True(!ledger.Any && ledger.StatsText(Rules, 1, 0) == "", "nothing to begin with");
            ledger.Add(1, "battle_trance", Rules.Buff("campingDMGLowBuffFrontRank"), 0.25);
            ledger.Add(1, "battle_trance", Rules.Buff("campingDMGHighBuffFrontRank"), 0.25);
            ledger.Add(1, "sharpen_spear", Rules.Buff("campingCRITBuff"), 0.1);
            ledger.Add(2, "scout_ahead", Rules.Buff("campingScoutingBuff"), 0.25);
            ledger.Add(2, "tracking", Rules.Buff("campingMonstersSurprise"), 0.1);
            Check.Equal(4, ledger.FightsLeft, "DD1's four fights");
            Check.Equal("add_stat,health_damage_dealt_percent,0.25\nadd_stat,crit_chance,0.1\n", ledger.StatsText(Rules, 1, 0), "the hellion in front: both ends of the damage add up");
            Check.Equal("add_stat,crit_chance,0.1\n", ledger.StatsText(Rules, 1, 1), "the hellion behind keeps the crit");
            Check.Equal("", ledger.StatsText(Rules, 2, 0), "a scout's buffs are not stats");
            Check.Near(0.25, ledger.Total(Rules, CampBuffKind.Scouting), 1e-9, "scouting bonus");
            Check.Near(0.1, ledger.Total(Rules, CampBuffKind.MonstersSurprise), 1e-9, "surprise bonus");

            var copy = CampBuffLedger.FromJson(JToken.Parse(ledger.ToJson().ToString()));
            Check.Equal(ledger.StatsText(Rules, 1, 0), copy.StatsText(Rules, 1, 0), "buffs survive the save");
            Check.Equal(4, copy.FightsLeft, "with their fights");

            Check.True(!ledger.FightOver() && !ledger.FightOver() && !ledger.FightOver(), "three fights on");
            Check.Equal(1, ledger.FightsLeft, "one left");
            Check.True(ledger.FightOver(), "the fourth ends them");
            Check.True(!ledger.Any && ledger.Entries.Count == 0 && ledger.Total(Rules, CampBuffKind.Scouting) == 0, "all gone");
            Check.True(!ledger.FightOver(), "nothing more to end");

            copy.Forget(1);
            Check.Equal("", copy.StatsText(Rules, 1, 0), "the fallen carry nothing");
            copy.Clear();
            Check.True(!copy.Any && copy.FightsLeft == 0, "a new camp starts clean");
        }

        // ---- no DD1 ----------------------------------------------------------------------------------

        public static void WithoutDd1ThereIsNoCamping()
        {
            var rules = CampingRules.Load(new NoDd1Files());
            Check.Equal(0, rules.Skills.Count, "no skills");
            Check.Equal(0, rules.Firewood(2), "no firewood, so no camps");
            Check.Equal(0, rules.SkillsFor("crusader").Count, "nothing to learn");
            Check.Equal(0, CampSkillBook.Roll(rules, "crusader", new Rng(1)).Known.Count, "a recruit arrives without");
            var camp = new CampSession(rules, new[] { new CampHero { Id = 1, ClassId = "crusader" } }, 1);
            Check.True(camp.Eat("none", 0).Count > 0 && camp.Phase == CampPhase.Skills, "the fallback meal still works");
            Check.True(camp.Sleep() != null, "and the night still falls");
        }

        /// <summary>The install with one folder taken out of it.</summary>
        private sealed class WithoutFolder : IDd1Files
        {
            private readonly IDd1Files _inner;
            private readonly string _prefix;

            public WithoutFolder(IDd1Files inner, string prefix)
            {
                _inner = inner;
                _prefix = prefix;
            }

            public bool Exists(string relativePath) => !relativePath.StartsWith(_prefix) && _inner.Exists(relativePath);
            public string ReadText(string relativePath) => relativePath.StartsWith(_prefix) ? null : _inner.ReadText(relativePath);
            public string[] List(string relativeDir, string pattern) => relativeDir.StartsWith(_prefix) ? new string[0] : _inner.List(relativeDir, pattern);
        }
    }
}
