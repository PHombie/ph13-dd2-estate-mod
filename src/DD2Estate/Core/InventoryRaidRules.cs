using System.Collections.Generic;
using System.Globalization;

namespace DD2Estate.Core
{
    public enum ItemUseKind { None, Light, Food, Heal, StressHeal, Camp }

    /// <summary>What clicking an item in the bag does out of a fight.</summary>
    public sealed class ItemUse
    {
        public static readonly ItemUse Nothing = new ItemUse();

        public ItemUseKind Kind;
        /// <summary>Food, Heal: share of the hero's max health. StressHeal: points on DD2's 0..10 stress scale.</summary>
        public double Amount;

        public bool NeedsHero => Kind == ItemUseKind.Food || Kind == ItemUseKind.Heal || Kind == ItemUseKind.StressHeal;
    }

    /// <summary>
    /// The DD1 rules around the raid inventory, read from the install: <c>inventory/base.inventory.system_configs.darkest</c>
    /// (slots), <c>shared/rules.json</c> (food), <c>campaign/estate/estate.json</c> (what a failed quest keeps),
    /// <c>effects/base.effects.darkest</c> (stress of a named effect). The initial values of the fields are
    /// FALLBACKS for a missing file or key.
    /// </summary>
    public sealed class InventoryRaidRules
    {
        public int RaidSlots = 16;
        public bool RaidStackLimits = true;

        /// <summary>Share of max health one unit of food restores when a hero eats between meals (provision_hp_heal).</summary>
        public double FoodHeal = 0.05;
        /// <summary>Share added to what eating restores (DD1's buff hp_heal_received_percent of sub type eat: the Granary's 0.15); set by the game layer, 0 as a rule.</summary>
        public double EatBonus;
        /// <summary>Units a hero can eat before being full (max_provisions_before_full).</summary>
        public int FoodBeforeFull = 4;
        // DD1 behaviour, not in data: at a hunger check every hero eats one unit (the game's own item text says so).
        public int FoodPerHero = 1;
        /// <summary>
        /// DD1 stress for every hero of a party that starves at a hunger check. rules.json has no key of its own for
        /// it; the "none" row of meals_table (the same 20% health loss) is the nearest number in the data.
        /// </summary>
        public double StarveStress = 15;

        private readonly Dictionary<string, double> _failKeepRates = new Dictionary<string, double>();
        private Dictionary<string, double> _effectStress;
        private readonly IDd1Files _files;

        private InventoryRaidRules(IDd1Files files) { _files = files; }

        public static InventoryRaidRules Load(IDd1Files files)
        {
            var rules = new InventoryRaidRules(files);
            foreach (var block in DarkestFile.Parse(files.ReadText("inventory/base.inventory.system_configs.darkest")))
            {
                if (block.Name != "inventory_system_config" || block.Text("type") != "raid") continue;
                rules.RaidSlots = System.Math.Max(1, block.Int("max_slots", 0, rules.RaidSlots));
                rules.RaidStackLimits = block.Text("use_stack_limits", 0, "true") != "false";
            }

            var raw = Json.ParseFile(files.ReadText("shared/rules.json"));
            if (raw != null)
            {
                rules.FoodHeal = Json.Number(raw["provision_hp_heal"], rules.FoodHeal);
                rules.FoodBeforeFull = Json.Int(raw["max_provisions_before_full"], rules.FoodBeforeFull);
                foreach (var meal in Json.Array(raw["meals_table"]))
                    if (Json.Number(meal["rations_per"], -1) == 0) rules.StarveStress = Json.Number(meal["stress"], rules.StarveStress);
            }

            var estate = Json.ParseFile(files.ReadText("campaign/estate/estate.json"));
            if (estate != null)
                foreach (var rate in Json.Array(estate["quest_fail_keep_rates"]))
                    if ((string)rate["type"] != null) rules._failKeepRates[(string)rate["type"]] = Json.Number(rate["rate"], 1);
            return rules;
        }

        /// <summary>Food the party eats at a hunger check.</summary>
        public int HungerFood(int heroes) => System.Math.Max(0, heroes) * FoodPerHero;

        /// <summary>
        /// Share of an item type the estate still gets when the expedition ends: everything after a success, DD1's
        /// quest_fail_keep_rates after a retreat, nothing when no one comes back.
        /// </summary>
        public double KeepRate(string itemType, RaidStatus status)
        {
            if (status == RaidStatus.Failed) return 0;
            if (status != RaidStatus.Abandoned) return 1;
            return _failKeepRates.TryGetValue(itemType ?? "", out var rate) ? rate : 1;
        }

        /// <summary>DD1 stress of named effects ("Stress 2" of an obstacle's fail_effects), summed; unknown names count 0.</summary>
        public double EffectStress(IEnumerable<string> effects)
        {
            if (effects == null) return 0;
            ReadEffects();
            double total = 0;
            foreach (var effect in effects)
                if (effect != null && _effectStress.TryGetValue(effect, out var stress)) total += stress;
            return total;
        }

        /// <summary>DD1 stress that named effects take off a hero ("Heal Stress TrapD" of a disarmed trap: 8), summed; unknown names count 0.</summary>
        public double EffectStressHeal(IEnumerable<string> effects)
        {
            if (effects == null) return 0;
            ReadEffects();
            double total = 0;
            foreach (var effect in effects)
                if (effect != null && _effectStressHeal.TryGetValue(effect, out var heal)) total += heal;
            return total;
        }

        private Dictionary<string, double> _effectStressHeal;

        private void ReadEffects()
        {
            if (_effectStress != null) return;
            _effectStress = new Dictionary<string, double>();
            _effectStressHeal = new Dictionary<string, double>();
            foreach (var block in DarkestFile.Parse(_files.ReadText("effects/base.effects.darkest")))
            {
                var name = block.Text("name");
                if (block.Name != "effect" || name == null) continue;
                var stress = block.Text("stress");
                if (stress != null && !_effectStress.ContainsKey(name) && double.TryParse(stress, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) _effectStress[name] = value;
                var heal = block.Text("healstress");
                if (heal != null && !_effectStressHeal.ContainsKey(name) && double.TryParse(heal, NumberStyles.Float, CultureInfo.InvariantCulture, out var healed)) _effectStressHeal[name] = healed;
            }
        }

        // THE MOD'S OWN MAPPING, not DD1 numbers. DD1's supplies cure what only exists inside a DD1 fight (bleed,
        // blight, debuffs, horror) or buff resistances; a DD2 hero between fights has health and stress, nothing
        // else for them to act on. Bandage and antivenom give back what the mod turns a curio's bleed or blight
        // into (a tenth of max health); herbs, the dearer cure-all, a little more; holy water and laudanum calm
        // (DD2's own laudanum relieves 1 stress).
        private static readonly Dictionary<string, ItemUse> SupplyUses = new Dictionary<string, ItemUse>
        {
            ["torch"] = new ItemUse { Kind = ItemUseKind.Light },
            // DD1's own use of it: a click on the firewood makes camp
            ["firewood"] = new ItemUse { Kind = ItemUseKind.Camp },
            ["bandage"] = new ItemUse { Kind = ItemUseKind.Heal, Amount = 0.10 },
            ["antivenom"] = new ItemUse { Kind = ItemUseKind.Heal, Amount = 0.10 },
            ["medicinal_herbs"] = new ItemUse { Kind = ItemUseKind.Heal, Amount = 0.15 },
            ["holy_water"] = new ItemUse { Kind = ItemUseKind.StressHeal, Amount = 1 },
            ["laudanum"] = new ItemUse { Kind = ItemUseKind.StressHeal, Amount = 1 }
        };

        /// <summary>
        /// OWNER'S CHOICE Q8 (docs/recon/inventory-unification.md 4.6): the mod's stand-in effects of bandage,
        /// antivenom, herbs, holy water and laudanum on a hero outside a fight (the mapping above). They were made
        /// because the bag's supplies do nothing in a DD2 fight; once they do (the plan's step 6) they are to go:
        /// false takes them out. Lighting a torch, making camp and eating between meals are DD1's and stay.
        /// Since 2026-10-06 the supplies do DD1's own in a fight (<see cref="FightItemRules"/>), and the stand-ins
        /// count there beside it while they are on (<see cref="FightItemRules.StandInsInAFight"/>): the choice to
        /// take them out is still the owner's.
        /// </summary>
        public static bool StandInSupplyUses = true;

        public ItemUse UseOf(ItemDef item)
        {
            if (item == null) return ItemUse.Nothing;
            if (item.Type == ItemTypes.Provision) return new ItemUse { Kind = ItemUseKind.Food, Amount = FoodHeal * (1 + EatBonus) };
            if (item.Type != ItemTypes.Supply || !SupplyUses.TryGetValue(item.Id, out var use)) return ItemUse.Nothing;
            return StandInSupplyUses || !use.NeedsHero ? use : ItemUse.Nothing;
        }

        // DD1's supplies that are used in a fight, each with the DD2 combat item that is the same thing there
        // (docs/recon/inventory-unification.md 4.3): bandages against bleeding, antivenom against blight, herbs
        // against debuffs, holy water for resistances, laudanum against horror, a torch for light. Food, the
        // shovel, the key and firewood have no DD2 combat item (DD1 has food eaten in a battle all the same:
        // what the bag's items do in a fight is Core/FightItems.cs; this table is the sweep's).
        private static readonly Dictionary<string, string> Dd2Twins = new Dictionary<string, string>
        {
            ["bandage"] = "bandages",
            ["antivenom"] = "antivenom",
            ["medicinal_herbs"] = "medicinal_herbs",
            ["holy_water"] = "holy_water",
            ["laudanum"] = "laudanum",
            ["torch"] = "torch_consumable"
        };

        /// <summary>The DD2 combat item a DD1 supply is in a fight; null for a supply that is no fight item.</summary>
        public static string Dd2ItemOf(string supplyId)
        {
            return supplyId != null && Dd2Twins.TryGetValue(supplyId, out var twin) ? twin : null;
        }

        /// <summary>The DD1 supply a DD2 combat item is the twin of; null for an item DD1 has nothing like.</summary>
        public static string SupplyOfDd2Item(string dd2ItemId)
        {
            foreach (var pair in Dd2Twins)
                if (pair.Value == dd2ItemId) return pair.Key;
            return null;
        }
    }
}
