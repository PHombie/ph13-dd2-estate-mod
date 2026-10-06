using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's rules for the raid inventory during a battle (Core/FightItems.cs).</summary>
    internal static class FightItemTests
    {
        private static FightItemRules Rules(out ItemCatalog items)
        {
            var files = Dd1.Files;
            items = ItemCatalog.Load(files);
            return FightItemRules.Load(files, InventoryRaidRules.Load(files), RaidRules.Load(files));
        }

        private static FightHeroState Well => new FightHeroState();

        // effects/base.effects.darkest: the five supplies with an item effect, each on the "performer"
        public static void Dd1NamesTheItemsOfAFightAndWhoTakesThem()
        {
            var rules = Rules(out var items);
            foreach (var id in new[] { "bandage", "antivenom", "medicinal_herbs", "holy_water", "laudanum" })
            {
                var effect = rules.EffectOf(items.Find(id));
                Check.True(effect != null, id + " has no item effect");
                Check.Equal("performer", effect.Target, id + " is used on");
                Check.Equal(FightItemKind.Effect, rules.KindOf(items.Find(id)), "the kind of " + id);
            }
            Check.True(rules.EffectOf(items.Find("bandage")).CureBleed && !rules.EffectOf(items.Find("bandage")).CureBlight, "a bandage cures bleeding and nothing else");
            Check.True(rules.EffectOf(items.Find("antivenom")).CureBlight, "antivenom cures blight");
            Check.True(rules.EffectOf(items.Find("medicinal_herbs")).ClearDebuffs, "herbs clear debuffs");
            Check.True(rules.EffectOf(items.Find("laudanum")).ClearHorror, "laudanum clears horror");

            var water = rules.EffectOf(items.Find("holy_water"));
            Check.Equal(3, water.Rounds, "holy water's rounds");
            Check.Equal(4, water.Buffs.Count, "holy water's buffs");
            Check.True(water.Buffs.All(b => b.Buff.StatType == "resistance" && System.Math.Abs(b.Amount - 0.33) < 1e-9), "holy water: four resistances of 33%");
            Check.True(water.Buffs.Select(b => b.Buff.SubType).OrderBy(s => s).SequenceEqual(new[] { "bleed", "debuff", "disease", "poison" }), "holy water's resistances");

            Check.Equal(FightItemKind.Torch, rules.KindOf(items.Find("torch")), "a torch");
            Check.Equal(FightItemKind.Food, rules.KindOf(items.Food), "food");
            foreach (var id in new[] { "shovel", "skeleton_key", "firewood", "gold" })
                Check.Equal(FightItemKind.None, rules.KindOf(items.Find(id)), id + " in a fight");
            Check.Equal(FightItemKind.None, rules.KindOf(items.Trinket("some_trinket")), "a trinket in a fight");
            // the hound's treats are DD1's too, and nothing this mod's heroes can use
            Check.Equal(FightItemKind.None, rules.KindOf(items.Ensure(ItemTypes.Supply, "dog_treats")), "dog treats");
        }

        public static void WhatIsNoFightItemIsRefusedInDd1sWords()
        {
            var rules = Rules(out var items);
            foreach (var id in new[] { "shovel", "skeleton_key", "firewood", "gold" })
            {
                var use = rules.Plan(items.Find(id), new FightHeroState { Hurt = true, Bleeding = true, Stressed = true }, new FightScene());
                Check.Equal(FightItemRefusal.NotAFightItem, use.Refusal, id);
            }
            Check.True(FightItemRules.IsUsedItem(items.Find("shovel")) && FightItemRules.IsUsedItem(items.Food), "supplies and food are greyed while they cannot be used");
            Check.True(!FightItemRules.IsUsedItem(items.Find("gold")) && !FightItemRules.IsUsedItem(items.Trinket("t")), "what is only carried is never grey");
        }

        public static void FoodIsEatenByTheHurtUntilFull()
        {
            var rules = Rules(out var items);
            var supply = InventoryRaidRules.Load(Dd1.Files);
            Check.Equal(FightItemRefusal.NotHurt, rules.Plan(items.Food, Well, new FightScene()).Refusal, "a hero in full health");
            var use = rules.Plan(items.Food, new FightHeroState { Hurt = true, Eaten = supply.FoodBeforeFull - 1 }, new FightScene());
            Check.True(use.Possible, "the last bite before full");
            Check.Near(supply.FoodHeal, use.Heal, 1e-9, "what a unit of food heals");
            Check.Equal(FightItemRefusal.Full, rules.Plan(items.Food, new FightHeroState { Hurt = true, Eaten = supply.FoodBeforeFull }, new FightScene()).Refusal, "a full hero");
        }

        public static void ATorchIsLitUnlessTheLightIsFullOrTheNightAmbushIsOn()
        {
            var rules = Rules(out var items);
            var torch = items.Find("torch");
            var use = rules.Plan(torch, Well, new FightScene { Light = 50 });
            Check.True(use.Possible, "a torch at half light");
            Check.Near(25, use.Light, 1e-9, "a torch's light");
            Check.Equal(FightItemRefusal.TorchAtLimit, rules.Plan(torch, Well, new FightScene { Light = RaidRules.MaxLight }).Refusal, "by full light");
            Check.Equal(FightItemRefusal.TorchInAmbush, rules.Plan(torch, Well, new FightScene { Light = 0, Ambush = true }).Refusal, "in a night ambush");
            Check.Equal(FightItemRefusal.TorchAtLimit, rules.Plan(torch, Well, new FightScene { Light = 50, TorchBurns = false }).Refusal, "under a torch setting that lets nothing move the light");
        }

        public static void ACureNeedsSomethingToCure()
        {
            var rules = Rules(out var items);
            var was = FightItemRules.StandInsInAFight;
            try
            {
                // DD1's effect alone
                FightItemRules.StandInsInAFight = false;
                var hurt = new FightHeroState { Hurt = true, Stressed = true };
                Check.Equal(FightItemRefusal.NotBleeding, rules.Plan(items.Find("bandage"), hurt, null).Refusal, "a bandage on a hero who does not bleed");
                Check.Equal(FightItemRefusal.NotBlighted, rules.Plan(items.Find("antivenom"), hurt, null).Refusal, "antivenom without blight");
                Check.Equal(FightItemRefusal.NoDebuff, rules.Plan(items.Find("medicinal_herbs"), hurt, null).Refusal, "herbs without a debuff");
                Check.Equal(FightItemRefusal.NoHorror, rules.Plan(items.Find("laudanum"), hurt, null).Refusal, "laudanum without horror");

                var bandage = rules.Plan(items.Find("bandage"), new FightHeroState { Bleeding = true, Blighted = true, Hurt = true }, null);
                Check.True(bandage.Possible && bandage.CureBleed && !bandage.CureBlight && bandage.Heal == 0, "a bandage stops the bleeding and that is all");
                var antivenom = rules.Plan(items.Find("antivenom"), new FightHeroState { Blighted = true }, null);
                Check.True(antivenom.Possible && antivenom.CureBlight, "antivenom on blight");
                Check.True(rules.Plan(items.Find("medicinal_herbs"), new FightHeroState { Debuffed = true }, null).ClearDebuffs, "herbs on a debuff");
                Check.True(rules.Plan(items.Find("laudanum"), new FightHeroState { Horrified = true }, null).ClearHorror, "laudanum on horror");

                // a buff can always be put on
                var water = rules.Plan(items.Find("holy_water"), Well, null);
                Check.True(water.Possible && water.Buffs.Count == 4 && water.Rounds == 3 && water.StressHeal == 0, "holy water on anybody: four resistances for three rounds");
            }
            finally { FightItemRules.StandInsInAFight = was; }
        }

        // the mod's own: what a supply does on a hero between fights it does in a fight as well, beside DD1's effect
        public static void TheStandInsOfTheCorridorCountInAFightWhileTheyAreOn()
        {
            var rules = Rules(out var items);
            var supply = InventoryRaidRules.Load(Dd1.Files);
            var was = FightItemRules.StandInsInAFight;
            try
            {
                FightItemRules.StandInsInAFight = true;
                var bandage = rules.Plan(items.Find("bandage"), new FightHeroState { Hurt = true }, null);
                Check.True(bandage.Possible && !bandage.CureBleed, "a bandage on a hurt hero who does not bleed");
                Check.Near(supply.UseOf(items.Find("bandage")).Amount, bandage.Heal, 1e-9, "the bandage's share of health");
                var both = rules.Plan(items.Find("bandage"), new FightHeroState { Hurt = true, Bleeding = true }, null);
                Check.True(both.CureBleed && both.Heal > 0, "bleeding and hurt: both");
                Check.Equal(FightItemRefusal.NotBleeding, rules.Plan(items.Find("bandage"), Well, null).Refusal, "a bandage on a hero in full health");

                var laudanum = rules.Plan(items.Find("laudanum"), new FightHeroState { Stressed = true }, null);
                Check.True(laudanum.Possible && laudanum.StressHeal > 0 && laudanum.Heal == 0, "laudanum on a stressed hero");
                Check.Equal(FightItemRefusal.NoHorror, rules.Plan(items.Find("laudanum"), Well, null).Refusal, "laudanum on a calm hero");

                // without the stand-ins of the corridor there is nothing to carry over
                InventoryRaidRules.StandInSupplyUses = false;
                Check.Equal(FightItemRefusal.NotBleeding, rules.Plan(items.Find("bandage"), new FightHeroState { Hurt = true }, null).Refusal, "the stand-ins are out everywhere");
            }
            finally
            {
                InventoryRaidRules.StandInSupplyUses = true;
                FightItemRules.StandInsInAFight = was;
            }
        }

        public static void WithoutDd1TheStockItemEffectsStand()
        {
            var files = new NoDd1Files();
            var items = ItemCatalog.Load(files);
            var rules = FightItemRules.Load(files, InventoryRaidRules.Load(files));
            Check.True(rules.EffectOf(items.Find("bandage")).CureBleed, "the bandage");
            Check.Equal(4, rules.EffectOf(items.Find("holy_water")).Buffs.Count, "holy water's buffs");
            Check.Equal(FightItemKind.Torch, rules.KindOf(items.Find("torch")), "the torch");
            Check.True(rules.Plan(items.Find("torch"), null, new FightScene { Light = 0 }).Light > 0, "a torch gives light");
        }
    }
}
