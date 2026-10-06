using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    public enum FightItemKind
    {
        /// <summary>Nothing a fight has a use for: gold, a gem, a trinket, the shovel, the key, firewood.</summary>
        None,
        /// <summary>Food: the acting hero eats.</summary>
        Food,
        /// <summary>A torch is lit.</summary>
        Torch,
        /// <summary>A supply with an item effect of DD1's own (bandage, antivenom, herbs, holy water, laudanum).</summary>
        Effect
    }

    public enum FightItemRefusal
    {
        None,
        /// <summary>DD1: "Can't use items during battle" (a click on the shovel, the key, the firewood).</summary>
        NotAFightItem,
        NotBleeding,
        NotBlighted,
        NoDebuff,
        NoHorror,
        NotHurt,
        /// <summary>DD1 writes "Full!" over a hero who has eaten max_provisions_before_full.</summary>
        Full,
        /// <summary>DD1: str_cant_use_torch_at_limit.</summary>
        TorchAtLimit,
        /// <summary>DD1: str_cant_use_torch_during_ambush.</summary>
        TorchInAmbush,
        /// <summary>The item's effect has nothing in this game to act on.</summary>
        NothingToDo
    }

    /// <summary>One buff of an item's effect: the DD1 buff and its amount (shared/buffs/*.buffs.json).</summary>
    public sealed class FightItemBuff
    {
        public CampBuffDef Buff;
        public double Amount;
    }

    /// <summary>
    /// What DD1 says a supply does when it is used in a fight: the effect of the item's own name in
    /// <c>effects/base.effects.darkest</c> that carries <c>.item 1</c>
    /// (<c>effect: .name "bandage" .target "performer" .item 1 .chance 100% .cure_bleed 1</c>).
    /// </summary>
    public sealed class FightItemEffect
    {
        public string Id = "";
        /// <summary>DD1's <c>.target</c>: "performer" for every item of the stock game, the hero whose turn it is.</summary>
        public string Target = "performer";
        public bool CureBleed, CureBlight, ClearDebuffs, ClearHorror;
        /// <summary>Rounds the buffs last (<c>.duration</c>); 0 when the effect has none.</summary>
        public int Rounds;
        public readonly List<FightItemBuff> Buffs = new List<FightItemBuff>();
        /// <summary>The effect asks for something this reader does not know (dog treats: a combat stat buff written into the effect).</summary>
        public bool Unknown;

        public bool Any => CureBleed || CureBlight || ClearDebuffs || ClearHorror || Buffs.Count > 0;
    }

    /// <summary>The hero whose turn it is, as far as an item cares.</summary>
    public sealed class FightHeroState
    {
        public bool Hurt, Bleeding, Blighted, Debuffed, Horrified, Stressed;
        /// <summary>Food eaten since the last meal or fight.</summary>
        public int Eaten;
    }

    /// <summary>The fight, as far as an item cares.</summary>
    public sealed class FightScene
    {
        public double Light = RaidRules.MaxLight;
        /// <summary>The fight is a camp's night ambush (DD1: no torches then).</summary>
        public bool Ambush;
        /// <summary>False under a quest's torch setting that lets nothing move the torch.</summary>
        public bool TorchBurns = true;
    }

    /// <summary>What using an item now would do; <see cref="Refusal"/> says why it would do nothing.</summary>
    public sealed class FightItemUse
    {
        public FightItemKind Kind;
        public FightItemRefusal Refusal;
        public bool CureBleed, CureBlight, ClearDebuffs, ClearHorror;
        public List<FightItemBuff> Buffs = new List<FightItemBuff>();
        public int Rounds;
        /// <summary>Share of the hero's max health given back.</summary>
        public double Heal;
        /// <summary>Stress taken off, on DD2's 0..10 scale.</summary>
        public double StressHeal;
        /// <summary>Torchlight added.</summary>
        public double Light;

        public bool Possible => Refusal == FightItemRefusal.None;
    }

    /// <summary>
    /// DD1's rules for the raid inventory during a battle.
    ///
    /// Which items: the supplies whose own effect DD1's effect file marks as an item's (bandage, antivenom,
    /// medicinal herbs, holy water, laudanum; dog treats too, which this mod's heroes have no use for), the
    /// torch (the game's own code: +25 light, refused with str_cant_use_torch_at_limit and
    /// str_cant_use_torch_during_ambush) and food (rules.json provision_hp_heal, max_provisions_before_full).
    /// Everything else is answered with str_user_information_cant_use_items_in_battle.
    ///
    /// On whom: every item effect of the stock game has <c>.target "performer"</c>: the hero whose turn it is,
    /// nobody else. Using an item is no action of the turn (DD1 behaviour, not in data).
    ///
    /// DD1 draws an item that would do nothing now grey (seen in its frames of a rested party: bandages with
    /// nobody bleeding, food with nobody hurt, torches by full light), so a use that would do nothing is refused
    /// here and nothing is spent.
    ///
    /// Beside DD1's effect an item does what it does on a hero between fights
    /// (<see cref="InventoryRaidRules.UseOf"/>: THE MOD'S OWN stand-ins, a share of health or a point of
    /// stress), as long as <see cref="StandInsInAFight"/> and the stand-ins themselves are on.
    /// </summary>
    public sealed class FightItemRules
    {
        /// <summary>
        /// The mod's stand-in effects of bandage, antivenom, herbs, holy water and laudanum (a share of health,
        /// a point of stress) also apply in a fight, beside DD1's own effect. False: DD1's effect alone.
        /// </summary>
        public static bool StandInsInAFight = true;

        private readonly InventoryRaidRules _supply;
        private readonly Dictionary<string, FightItemEffect> _effects = new Dictionary<string, FightItemEffect>();

        /// <summary>DD1 behaviour, not in data: the light a torch adds (<see cref="RaidRules.TorchLight"/>).</summary>
        public double TorchLight = 25;

        private FightItemRules(InventoryRaidRules supply) { _supply = supply; }

        public IReadOnlyDictionary<string, FightItemEffect> Effects => _effects;

        public static FightItemRules Load(IDd1Files files, InventoryRaidRules supply, RaidRules raid = null)
        {
            var rules = new FightItemRules(supply);
            if (raid != null) rules.TorchLight = raid.TorchLight;

            var wantedBuffs = new Dictionary<string, List<FightItemEffect>>();
            foreach (var file in files.List("effects", "*.effects.darkest").OrderBy(f => f, System.StringComparer.Ordinal))
                foreach (var block in DarkestFile.Parse(files.ReadText("effects/" + file)))
                {
                    var name = block.Text("name");
                    if (block.Name != "effect" || name == null || !IsSet(block.Text("item")) || rules._effects.ContainsKey(name)) continue;
                    var effect = new FightItemEffect
                    {
                        Id = name,
                        Target = block.Text("target", 0, "performer"),
                        CureBleed = IsSet(block.Text("cure_bleed")),
                        CureBlight = IsSet(block.Text("cure_poison")),
                        ClearDebuffs = IsSet(block.Text("clear_debuff")),
                        ClearHorror = IsSet(block.Text("clearDotStress")),
                        Rounds = block.Int("duration", 0, 0),
                        Unknown = block.Fields.ContainsKey("combat_stat_buff")
                    };
                    foreach (var id in block.All("buff_ids"))
                    {
                        if (!wantedBuffs.TryGetValue(id, out var users)) wantedBuffs[id] = users = new List<FightItemEffect>();
                        users.Add(effect);
                    }
                    rules._effects[name] = effect;
                }

            if (wantedBuffs.Count > 0)
                foreach (var file in files.List("shared/buffs", "*.buffs.json").OrderBy(f => f, System.StringComparer.Ordinal))
                {
                    var json = Json.ParseFile(files.ReadText("shared/buffs/" + file));
                    if (json == null) continue;
                    foreach (var b in Json.Array(json["buffs"]))
                    {
                        var id = (string)b["id"];
                        if (id == null || !wantedBuffs.TryGetValue(id, out var users)) continue;
                        var buff = new FightItemBuff
                        {
                            Buff = new CampBuffDef { Id = id, StatType = (string)b["stat_type"] ?? "", SubType = (string)b["stat_sub_type"] ?? "", RuleType = (string)b["rule_type"] ?? "always" },
                            Amount = Json.Number(b["amount"], 0)
                        };
                        foreach (var user in users) user.Buffs.Add(buff);
                        wantedBuffs.Remove(id);
                    }
                }

            if (rules._effects.Count == 0) rules.AddFallback();
            return rules;
        }

        // FALLBACK for an install whose effect file cannot be read: the stock game's five item effects.
        private void AddFallback()
        {
            _effects["bandage"] = new FightItemEffect { Id = "bandage", CureBleed = true };
            _effects["antivenom"] = new FightItemEffect { Id = "antivenom", CureBlight = true };
            _effects["medicinal_herbs"] = new FightItemEffect { Id = "medicinal_herbs", ClearDebuffs = true };
            _effects["laudanum"] = new FightItemEffect { Id = "laudanum", ClearHorror = true };
            var water = new FightItemEffect { Id = "holy_water", Rounds = 3 };
            foreach (var sub in new[] { "poison", "bleed", "disease", "debuff" })
                water.Buffs.Add(new FightItemBuff { Buff = new CampBuffDef { Id = "holy_water_" + sub + "_resist", StatType = "resistance", SubType = sub }, Amount = 0.33 });
            _effects["holy_water"] = water;
        }

        private static bool IsSet(string value) => value != null && value != "0" && value != "false";

        /// <summary>DD1's item effect of a supply; null for an item that has none.</summary>
        public FightItemEffect EffectOf(ItemDef item)
        {
            return item != null && item.Type == ItemTypes.Supply && _effects.TryGetValue(item.Id, out var effect) && !effect.Unknown && effect.Any ? effect : null;
        }

        public FightItemKind KindOf(ItemDef item)
        {
            if (item == null) return FightItemKind.None;
            if (item.Type == ItemTypes.Provision) return FightItemKind.Food;
            if (item.Type != ItemTypes.Supply) return FightItemKind.None;
            if (item.Id == "torch") return FightItemKind.Torch;
            return EffectOf(item) != null ? FightItemKind.Effect : FightItemKind.None;
        }

        /// <summary>
        /// True for an item DD1 greys while it cannot be used (a supply, food); gold, gems, heirlooms, quest
        /// items and trinkets are carried, never used and never grey.
        /// </summary>
        public static bool IsUsedItem(ItemDef item) => item != null && (item.Type == ItemTypes.Supply || item.Type == ItemTypes.Provision);

        /// <summary>What the acting hero's use of this item would do right now.</summary>
        public FightItemUse Plan(ItemDef item, FightHeroState hero, FightScene scene)
        {
            var use = new FightItemUse { Kind = KindOf(item) };
            hero = hero ?? new FightHeroState();
            scene = scene ?? new FightScene();
            switch (use.Kind)
            {
                case FightItemKind.None:
                    use.Refusal = FightItemRefusal.NotAFightItem;
                    return use;
                case FightItemKind.Food:
                    if (!hero.Hurt) use.Refusal = FightItemRefusal.NotHurt;
                    else if (hero.Eaten >= _supply.FoodBeforeFull) use.Refusal = FightItemRefusal.Full;
                    else use.Heal = _supply.FoodHeal * (1 + _supply.EatBonus);
                    return use;
                case FightItemKind.Torch:
                    if (scene.Ambush) use.Refusal = FightItemRefusal.TorchInAmbush;
                    else if (!scene.TorchBurns || scene.Light >= RaidRules.MaxLight) use.Refusal = FightItemRefusal.TorchAtLimit;
                    else use.Light = TorchLight;
                    return use;
            }

            var effect = EffectOf(item);
            use.CureBleed = effect.CureBleed && hero.Bleeding;
            use.CureBlight = effect.CureBlight && hero.Blighted;
            use.ClearDebuffs = effect.ClearDebuffs && hero.Debuffed;
            use.ClearHorror = effect.ClearHorror && hero.Horrified;
            // a buff of DD1's that has a stat of this game to become: it can always be put on
            foreach (var buff in effect.Buffs)
                if (CampingBuffMap.Lines(buff.Buff, buff.Amount, -1).Count > 0) use.Buffs.Add(buff);
            use.Rounds = use.Buffs.Count > 0 ? System.Math.Max(1, effect.Rounds) : 0;

            if (StandInsInAFight)
            {
                var between = _supply.UseOf(item);
                if (between.Kind == ItemUseKind.Heal && hero.Hurt) use.Heal = between.Amount;
                else if (between.Kind == ItemUseKind.StressHeal && hero.Stressed) use.StressHeal = between.Amount;
            }

            if (use.CureBleed || use.CureBlight || use.ClearDebuffs || use.ClearHorror || use.Buffs.Count > 0 || use.Heal > 0 || use.StressHeal > 0) return use;
            use.Refusal = effect.CureBleed ? FightItemRefusal.NotBleeding
                : effect.CureBlight ? FightItemRefusal.NotBlighted
                : effect.ClearDebuffs ? FightItemRefusal.NoDebuff
                : effect.ClearHorror ? FightItemRefusal.NoHorror
                : FightItemRefusal.NothingToDo;
            return use;
        }
    }
}
