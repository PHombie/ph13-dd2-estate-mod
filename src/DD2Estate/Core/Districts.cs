using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>What an entry of a district building's <c>buff_list</c> is, by DD1's <c>type</c>.</summary>
    public enum DistrictEffectKind
    {
        /// <summary>A type this reader has no meaning for: kept by name, it does nothing.</summary>
        Unknown,
        /// <summary>DistrictPrestigeBuffData: nothing at all.</summary>
        Prestige,
        /// <summary>DistrictEstateBuffData: interest on the gold held, every week.</summary>
        Interest,
        /// <summary>DistrictLightBuffData: another torchlight table and other loot of the dark.</summary>
        Light,
        /// <summary>DistrictSupplyBuffData: items handed out before an expedition.</summary>
        Supply,
        /// <summary>DistrictRosterStressReliefBuffData: more stress shed by the heroes who stay at home.</summary>
        IdleRelief,
        /// <summary>DistrictHeroBuffData: buffs on the heroes of some classes.</summary>
        HeroBuff,
        /// <summary>DistrictCurioInteractionBuffData: stress a hero of some classes sheds at curios of a kind.</summary>
        CurioStress,
        /// <summary>DistrictCampingBuffData: respite points while a hero of some classes is in the party.</summary>
        CampPoints
    }

    /// <summary>One entry of a building's <c>buff_list</c>. Which fields mean something depends on <see cref="Kind"/>.</summary>
    public sealed class DistrictEffect
    {
        public DistrictEffectKind Kind;
        /// <summary>DD1's <c>type</c> and <c>name</c>; the building's text for it is <c>str_&lt;building&gt;_buff_&lt;name&gt;</c>.</summary>
        public string Type = "", Name = "";
        /// <summary>Interest as a share of the gold held; DD1 stress (of 200) for IdleRelief and CurioStress; respite points for CampPoints.</summary>
        public double Amount;
        /// <summary>Supply: how many are handed out, both ends included.</summary>
        public int Min, Max;
        /// <summary>Supply: DD1 item type ("provision") and id ("" for food).</summary>
        public string ItemType = "", ItemId = "";
        /// <summary>The hero tags the entry is for (<c>hero_type_tags</c>); empty: every hero.</summary>
        public readonly List<string> Tags = new List<string>();
        /// <summary>CurioStress: the curio tags it answers to ("Knowledge").</summary>
        public readonly List<string> CurioTags = new List<string>();
        /// <summary>HeroBuff: ids of the buffs handed out.</summary>
        public readonly List<string> BuffIds = new List<string>();
        /// <summary>Light: the table that takes the place of rules.json "darkness", and of loot's "darkness_bonuses".</summary>
        public JToken Darkness, DarknessLoot;
    }

    /// <summary>A currency and an amount, in DD1's units unless said otherwise.</summary>
    public sealed class DistrictCost
    {
        public string Currency = "";
        public int Amount;

        public override string ToString() => Amount + " " + Currency;
    }

    /// <summary>One building of DD1's districts as <c>districts_districts.json</c> writes it.</summary>
    public sealed class DistrictBuilding
    {
        public string Id = "";
        /// <summary>The building's Spine skeleton in the install ("dlc/.../fx/town_district_bank/town_district_bank.sprite.skel"); its atlas and sheet lie beside it.</summary>
        public string Skeleton = "";
        /// <summary>The pictures of the two states: DD1's animations of the same names show one attachment each.</summary>
        public string NotBuiltArt = "idle", BuiltArt = "built";
        /// <summary>The sprite DD1 plays over a building as it is bought (bricks and dust: fx/purchase_district), and its animation; empty when the file names none.</summary>
        public string PurchaseSkeleton = "", PurchasedArt = "purchased";
        /// <summary>DD1's <c>town_priority</c>: the order on the screen, lowest first.</summary>
        public int TownPriority;
        /// <summary>The price in DD1's units: DD1 gold, heirlooms, blueprints.</summary>
        public readonly List<DistrictCost> Costs = new List<DistrictCost>();
        public readonly List<DistrictEffect> Effects = new List<DistrictEffect>();

        public IEnumerable<DistrictEffect> EffectsOf(DistrictEffectKind kind) => Effects.Where(e => e.Kind == kind);
    }

    /// <summary>What a campaign remembers of its districts; saved with the estate.</summary>
    public sealed class DistrictState
    {
        /// <summary>DD1: the screen opens with the town event "Cornerstones".</summary>
        public bool Unlocked;
        public int Blueprints;
        /// <summary>The buildings that stand, in the order they were built.</summary>
        public readonly List<string> Built = new List<string>();

        public bool Has(string id) => id != null && Built.Contains(id);

        public JObject ToJson()
        {
            return new JObject { ["unlocked"] = Unlocked, ["blueprints"] = Blueprints, ["built"] = new JArray(Built) };
        }

        public static DistrictState FromJson(JToken json)
        {
            var state = new DistrictState();
            if (json == null || json.Type != JTokenType.Object) return state;
            state.Unlocked = Json.Bool(json["unlocked"], false);
            state.Blueprints = Math.Max(0, Json.Int(json["blueprints"], 0));
            foreach (var id in Json.Array(json["built"]))
                if (id.Type == JTokenType.String && !state.Built.Contains((string)id)) state.Built.Add((string)id);
            return state;
        }
    }

    public enum DistrictRefusal
    {
        None,
        NoSuchBuilding,
        /// <summary>The districts are not open yet.</summary>
        Locked,
        AlreadyBuilt,
        /// <summary>The estate is short of <see cref="DistrictOffer.ShortOf"/>.</summary>
        CannotPay
    }

    /// <summary>A building's price and whether it can be built now.</summary>
    public sealed class DistrictOffer
    {
        public DistrictBuilding Building;
        /// <summary>Estate gold, heirlooms and blueprints, in the file's order.</summary>
        public readonly List<DistrictCost> Price = new List<DistrictCost>();
        public DistrictRefusal Refusal;
        /// <summary>The first currency of the price the estate is short of; null when it can pay.</summary>
        public string ShortOf;

        public bool Valid => Refusal == DistrictRefusal.None;
    }

    /// <summary>What the district buildings that stand give one hero, in DD2's terms.</summary>
    public sealed class DistrictHeroEffect
    {
        /// <summary>Lines of a DD2 stats container on the hero.</summary>
        public readonly List<Dd2StatLine> Lines = new List<Dd2StatLine>();
        /// <summary>The hero's share of the party's scouting chance (0.05 = five points).</summary>
        public double Scouting;
        /// <summary>DD1 buffs that have no counterpart here and are not given, by id.</summary>
        public readonly List<string> LeftOut = new List<string>();
        /// <summary>The buffs that were given, by id.</summary>
        public readonly List<string> Given = new List<string>();
    }

    /// <summary>
    /// DD1's Districts, read from the player's install: eleven buildings bought once each with gold, heirlooms
    /// and a blueprint, every one of them a standing bonus for the estate or for some hero classes. The feature
    /// lies in the Crimson Court's folder but is its own (DD1 asks "Enable Districts?" by itself), so only its
    /// own files are read: the Courtyard's blood bank and the other DLCs' districts are in other files.
    /// Without those files there are no buildings and nothing here does anything; there is no stand-in table.
    ///
    /// DD1 is native code, so what an entry of <c>buff_list</c> does is read off its type name and its numbers.
    /// How a DD1 hero buff lands on a DD2 hero is THE MOD'S OWN MAPPING and follows <see cref="CampingBuffMap"/>
    /// (docs/recon/camping.md), with three additions for stats the camping buffs never use:
    /// - healing done (hp_heal_percent) is DD2's own heal-dealt share;
    /// - the chance of a blight or a debuff skill to land (poison_chance, debuff_chance) is DD2's resistance
    ///   piercing of that kind: both are taken off the target's resistance roll;
    /// - more healing from eating (hp_heal_received_percent, sub type eat) is no stat of a DD2 hero: the game
    ///   layer multiplies the three places where the party eats (<see cref="EatBonus"/>).
    /// A buff under a rule (the Jester's Finale alone) is not given at all: a camping buff lasts four fights
    /// and gets the half share, a district's is for good and one skill of eleven is no half.
    /// </summary>
    public sealed class DistrictRules
    {
        public const string Feature = "dlc/580100_crimson_court/features/districts/";
        public const string File = Feature + "campaign/town/districts/districts_districts.json";
        public const string BuffsFile = Feature + "shared/buffs/districts.buffs.json";
        public const string Blueprint = "blueprint";
        public const string Gold = "gold";

        // DD2 ActorStatType ids the camping buffs have no use for
        public const string HealDealt = "health_heal_dealt_percent";
        public const string ResistanceIgnore = "resistance_ignore";

        // DD1 "<kind>_chance": the chance of a skill's effect of that kind to land -> DD2 resistance sub-stat keys
        private static readonly Dictionary<string, string> SkillChances = new Dictionary<string, string>
        {
            { "poison_chance", "blight" }, { "bleed_chance", "bleed" }, { "debuff_chance", "debuff" }, { "stun_chance", "stun" }, { "move_chance", "move" }
        };

        // FALLBACK, not read: the district tag of each of DD1's hero files, for an install whose hero files are gone.
        private static readonly Dictionary<string, string> StockTags = new Dictionary<string, string>
        {
            { "bounty_hunter", "house_of_the_yellow_hand" }, { "grave_robber", "house_of_the_yellow_hand" }, { "highwayman", "house_of_the_yellow_hand" },
            { "crusader", "altar_of_light" }, { "vestal", "altar_of_light" }, { "flagellant", "altar_of_light" },
            { "arbalest", "training_ring" }, { "houndmaster", "training_ring" }, { "man_at_arms", "training_ring" }, { "musketeer", "training_ring" }, { "shieldbreaker", "training_ring" },
            { "antiquarian", "library" }, { "occultist", "library" }, { "plague_doctor", "library" },
            { "jester", "theater" },
            { "abomination", "outsiders_bonfire" }, { "hellion", "outsiders_bonfire" }, { "leper", "outsiders_bonfire" }, { "runaway", "outsiders_bonfire" }
        };

        // FALLBACK, not read: DD1's stock district buffs (id, stat, sub type, amount, rule, rule text), for a buffs file that is gone.
        private static readonly object[][] StockBuffs =
        {
            new object[] { "districts_altar_1", "resistance", "stun", 0.1, "always", "" },
            new object[] { "districts_altar_2", "hp_heal_percent", "", 0.1, "always", "" },
            new object[] { "districts_yellowhand_1", "combat_stat_add", "crit_chance", 0.04, "always", "" },
            new object[] { "districts_yellowhand_2", "scouting_chance", "", 0.05, "always", "" },
            new object[] { "districts_theatre_1", "stress_dmg_received_percent", "", -0.1, "always", "" },
            new object[] { "districts_theatre_2", "combat_stat_add", "speed_rating", 2.0, "always", "" },
            new object[] { "districts_theatre_3", "combat_stat_multiply", "damage_low", 0.2, "skill", "heroic_end" },
            new object[] { "districts_theatre_4", "combat_stat_multiply", "damage_high", 0.2, "skill", "heroic_end" },
            new object[] { "districts_training_ring_1", "combat_stat_add", "attack_rating", 0.04, "always", "" },
            new object[] { "districts_training_ring_2", "combat_stat_multiply", "max_hp", 0.1, "always", "" },
            new object[] { "districts_library_1", "poison_chance", "", 0.15, "always", "" },
            new object[] { "districts_library_2", "debuff_chance", "", 0.15, "always", "" },
            new object[] { "districts_granary_1", "hp_heal_received_percent", "eat", 0.15, "always", "" }
        };

        private readonly IDd1Files _files;
        private readonly Dictionary<string, CampBuffDef> _buffs = new Dictionary<string, CampBuffDef>();
        private readonly Dictionary<string, double> _amounts = new Dictionary<string, double>();
        private readonly Dictionary<string, List<string>> _tags = new Dictionary<string, List<string>>();

        /// <summary>The buildings in the order of the screen (<c>town_priority</c>, then the file's).</summary>
        public readonly List<DistrictBuilding> Buildings = new List<DistrictBuilding>();
        /// <summary>The buffs file could not be read: DD1's stock buffs stand in.</summary>
        public bool StockBuffsInUse { get; private set; }

        /// <summary>True when the install has no districts (or the file says they are off): the feature stays out of sight.</summary>
        public bool Missing => Buildings.Count == 0;

        private DistrictRules(IDd1Files files) { _files = files; }

        public static DistrictRules Load(IDd1Files files)
        {
            var rules = new DistrictRules(files);
            rules.ReadBuildings(files.ReadText(File));
            if (!rules.Missing) rules.ReadBuffs(files.ReadText(BuffsFile));
            return rules;
        }

        /// <summary>Rules from JSON texts as DD1 writes them (the tests build their own buildings this way).</summary>
        public static DistrictRules Parse(string districtsJson, string buffsJson, IDd1Files files = null)
        {
            var rules = new DistrictRules(files);
            rules.ReadBuildings(districtsJson);
            if (!rules.Missing) rules.ReadBuffs(buffsJson);
            return rules;
        }

        public DistrictBuilding Find(string id) => id == null ? null : Buildings.FirstOrDefault(b => b.Id == id);

        /// <summary>A buff the buildings name; null for one DD1 does not list.</summary>
        public CampBuffDef Buff(string id) => id != null && _buffs.TryGetValue(id, out var buff) ? buff : null;

        /// <summary>The amount DD1's buff file gives a buff (0.04 for +4%).</summary>
        public double BuffAmount(string id) => id != null && _amounts.TryGetValue(id, out var amount) ? amount : 0;

        // ---- reading -------------------------------------------------------------------------------------

        private void ReadBuildings(string text)
        {
            var json = Json.ParseFile(text);
            // DD1's own switch of the file: districts that are off are not there
            if (json == null || !Json.Bool(json["enabled"], true)) return;
            var order = 0;
            var read = new List<KeyValuePair<int, DistrictBuilding>>();
            foreach (var entry in Json.Array(json["buildings"]))
            {
                var id = (string)entry["name"];
                if (string.IsNullOrEmpty(id) || read.Any(r => r.Value.Id == id)) continue;
                var building = new DistrictBuilding { Id = id };
                var render = entry["render_data"];
                if (render != null && render.Type == JTokenType.Object)
                {
                    var sprites = Json.Array(render["sprite_paths"]).Select(p => p.Type == JTokenType.String ? (string)p : null).Where(p => !string.IsNullOrEmpty(p)).ToList();
                    if (sprites.Count > 0) building.Skeleton = Locate(sprites[0]);
                    if (sprites.Count > 1) building.PurchaseSkeleton = Locate(sprites[1]);
                    building.NotBuiltArt = (string)render["not_built_animation"] ?? building.NotBuiltArt;
                    building.BuiltArt = (string)render["built_animation"] ?? building.BuiltArt;
                    building.PurchasedArt = (string)render["purchased_animation"] ?? building.PurchasedArt;
                    building.TownPriority = Json.Int(render["town_priority"], 0);
                }
                foreach (var cost in Json.Array(entry["currency_cost"]))
                {
                    var currency = (string)cost["type"];
                    var amount = Json.Int(cost["amount"], 0);
                    if (!string.IsNullOrEmpty(currency) && amount > 0) building.Costs.Add(new DistrictCost { Currency = currency, Amount = amount });
                }
                foreach (var buff in Json.Array(entry["buff_list"])) building.Effects.Add(ReadEffect(buff));
                read.Add(new KeyValuePair<int, DistrictBuilding>(order++, building));
            }
            foreach (var pair in read.OrderBy(r => r.Value.TownPriority).ThenBy(r => r.Key)) Buildings.Add(pair.Value);
        }

        // DD1 writes a feature's paths as the game's own: a file is the feature's where the feature has it (a
        // building's sprite) and the game's where only the game does (the purchase's bricks and dust).
        private string Locate(string path)
        {
            return _files != null && !_files.Exists(Feature + path) && _files.Exists(path) ? path : Feature + path;
        }

        private static DistrictEffect ReadEffect(JToken json)
        {
            var effect = new DistrictEffect { Type = (string)json["type"] ?? "", Name = (string)json["name"] ?? "" };
            foreach (var tag in Json.Array(json["hero_type_tags"]))
                if (tag.Type == JTokenType.String) effect.Tags.Add((string)tag);
            switch (effect.Type)
            {
                case "DistrictPrestigeBuffData":
                    effect.Kind = DistrictEffectKind.Prestige;
                    break;
                case "DistrictEstateBuffData":
                    effect.Kind = DistrictEffectKind.Interest;
                    effect.Amount = Json.Number(json["weekly_gold_interest"], 0);
                    break;
                case "DistrictLightBuffData":
                    effect.Kind = DistrictEffectKind.Light;
                    effect.Darkness = json["darkness"];
                    effect.DarknessLoot = json["loot_darkness_bonus"];
                    break;
                case "DistrictSupplyBuffData":
                    effect.Kind = DistrictEffectKind.Supply;
                    effect.ItemType = (string)json["item_type"] ?? "";
                    effect.ItemId = (string)json["item_name"] ?? "";
                    effect.Min = Math.Max(0, Json.Int(json["range_min"], 0));
                    effect.Max = Math.Max(effect.Min, Json.Int(json["range_max"], effect.Min));
                    break;
                case "DistrictRosterStressReliefBuffData":
                    effect.Kind = DistrictEffectKind.IdleRelief;
                    effect.Amount = Json.Number(json["stress_relief"], 0);
                    break;
                case "DistrictHeroBuffData":
                    effect.Kind = DistrictEffectKind.HeroBuff;
                    foreach (var id in Json.Array(json["buff_list"]))
                        if (id.Type == JTokenType.String) effect.BuffIds.Add((string)id);
                    break;
                case "DistrictCurioInteractionBuffData":
                    effect.Kind = DistrictEffectKind.CurioStress;
                    effect.Amount = Json.Number(json["stress_heal"], 0);
                    foreach (var tag in Json.Array(json["curio_tags"]))
                        if (tag.Type == JTokenType.String) effect.CurioTags.Add((string)tag);
                    break;
                case "DistrictCampingBuffData":
                    effect.Kind = DistrictEffectKind.CampPoints;
                    effect.Amount = Json.Number(json["camping_points"], 0);
                    break;
            }
            return effect;
        }

        private void ReadBuffs(string text)
        {
            foreach (var b in Json.Array(Json.ParseFile(text)?["buffs"]))
            {
                var id = (string)b["id"];
                if (string.IsNullOrEmpty(id) || _buffs.ContainsKey(id)) continue;
                var rule = b["rule_data"];
                AddBuff(id, (string)b["stat_type"] ?? "", (string)b["stat_sub_type"] ?? "", Json.Number(b["amount"], 0), (string)b["rule_type"] ?? "always",
                    (string)rule?["string"] ?? "", Json.Bool(b["is_false_rule"], false), Json.Number(rule?["float"], 0));
            }
            if (_buffs.Count > 0) return;
            StockBuffsInUse = true;
            foreach (var row in StockBuffs) AddBuff((string)row[0], (string)row[1], (string)row[2], (double)row[3], (string)row[4], (string)row[5], false, 0);
        }

        private void AddBuff(string id, string stat, string sub, double amount, string rule, string ruleText, bool ruleFalse, double ruleNumber)
        {
            _buffs[id] = new CampBuffDef { Id = id, StatType = stat, SubType = sub, RuleType = rule, RuleText = ruleText, RuleFalse = ruleFalse, RuleNumber = ruleNumber };
            _amounts[id] = amount;
        }

        // ---- hero tags -----------------------------------------------------------------------------------

        /// <summary>
        /// The tags DD1 gives a hero class (<c>tag: .id "library"</c> in heroes/&lt;class&gt;/&lt;class&gt;.info.darkest,
        /// also in the DLC folders that carry hero classes). DD2's class ids are DD1's for every hero both games
        /// have; a class DD1 does not know has no tags.
        /// </summary>
        public IReadOnlyList<string> TagsOf(string classId)
        {
            classId = classId ?? "";
            if (_tags.TryGetValue(classId, out var tags)) return tags;
            tags = new List<string>();
            var found = false;
            if (_files != null && classId.Length > 0)
                foreach (var root in CampingRules.Roots)
                {
                    var text = _files.ReadText(root + "heroes/" + classId + "/" + classId + ".info.darkest");
                    if (text == null) continue;
                    found = true;
                    foreach (var block in DarkestFile.Parse(text))
                    {
                        var id = block.Name == "tag" ? block.Text("id") : null;
                        if (!string.IsNullOrEmpty(id) && !tags.Contains(id)) tags.Add(id);
                    }
                    break;
                }
            if (!found && StockTags.TryGetValue(classId, out var stock)) tags.Add(stock);
            _tags[classId] = tags;
            return tags;
        }

        /// <summary>Whether an entry is for a hero of this class: one without tags is for everyone.</summary>
        public bool AppliesTo(DistrictEffect effect, string classId)
        {
            if (effect.Tags.Count == 0) return true;
            var tags = TagsOf(classId);
            return effect.Tags.Any(tags.Contains);
        }

        // ---- building ------------------------------------------------------------------------------------

        /// <summary>A building's price as DD1 writes it: gold, heirlooms, the blueprint.</summary>
        public static List<DistrictCost> Price(DistrictBuilding building)
        {
            var price = new List<DistrictCost>();
            foreach (var cost in building.Costs)
                price.Add(new DistrictCost { Currency = cost.Currency, Amount = cost.Amount });
            return price;
        }

        /// <summary>
        /// What a building costs and whether it can be built. <paramref name="purse"/> holds what the estate
        /// has by currency ("gold", the heirlooms, "blueprint").
        /// </summary>
        public DistrictOffer Quote(DistrictState state, string id, IDictionary<string, int> purse)
        {
            var offer = new DistrictOffer { Building = Find(id) };
            if (offer.Building == null)
            {
                offer.Refusal = DistrictRefusal.NoSuchBuilding;
                return offer;
            }
            offer.Price.AddRange(Price(offer.Building));
            if (state.Has(id)) offer.Refusal = DistrictRefusal.AlreadyBuilt;
            else if (!state.Unlocked) offer.Refusal = DistrictRefusal.Locked;
            foreach (var cost in offer.Price)
            {
                var held = 0;
                purse?.TryGetValue(cost.Currency, out held);
                if (held >= cost.Amount) continue;
                offer.ShortOf = cost.Currency;
                if (offer.Refusal == DistrictRefusal.None) offer.Refusal = DistrictRefusal.CannotPay;
                break;
            }
            return offer;
        }

        /// <summary>
        /// Builds a building: the price leaves the purse (the blueprint with it) and the building stands. Nothing
        /// changes when the offer is refused.
        /// </summary>
        public DistrictOffer Build(DistrictState state, string id, IDictionary<string, int> purse)
        {
            var offer = Quote(state, id, purse);
            if (!offer.Valid) return offer;
            foreach (var cost in offer.Price) purse[cost.Currency] = purse[cost.Currency] - cost.Amount;
            state.Built.Add(id);
            return offer;
        }

        // ---- what the buildings give ---------------------------------------------------------------------

        private IEnumerable<DistrictEffect> Standing(DistrictState state, DistrictEffectKind kind)
        {
            foreach (var building in Buildings)
            {
                if (!state.Has(building.Id)) continue;
                foreach (var effect in building.Effects)
                    if (effect.Kind == kind) yield return effect;
            }
        }

        /// <summary>The share of the gold held that comes on top of it every week (DD1's bank: 0.05).</summary>
        public double Interest(DistrictState state) => Standing(state, DistrictEffectKind.Interest).Sum(e => e.Amount);

        /// <summary>DD1 stress (of 200) a hero left at home sheds in a week on top of anything else.</summary>
        public double IdleRelief(DistrictState state) => Standing(state, DistrictEffectKind.IdleRelief).Sum(e => e.Amount);

        /// <summary>
        /// The week's free items of a DD1 type ("provision"): each building's own roll between its two numbers.
        /// DD1 writes the granary's food with an empty item name, which is how it names food everywhere.
        /// </summary>
        public int RollSupply(DistrictState state, string itemType, string itemId, Rng rng)
        {
            var amount = 0;
            foreach (var effect in Standing(state, DistrictEffectKind.Supply))
                if (effect.ItemType == itemType && effect.ItemId == (itemId ?? "")) amount += rng.Range(effect.Min, effect.Max);
            return amount;
        }

        /// <summary>Respite points a camp gets on top of its own: a building's, when at least one hero of its tags is in the party.</summary>
        public int CampPoints(DistrictState state, IEnumerable<string> partyClasses)
        {
            var classes = (partyClasses ?? new string[0]).ToList();
            var points = 0.0;
            foreach (var effect in Standing(state, DistrictEffectKind.CampPoints))
                if (classes.Any(c => AppliesTo(effect, c))) points += effect.Amount;
            return (int)Math.Round(points);
        }

        /// <summary>DD1 stress (of 200) a hero of this class sheds on using a curio with these tags.</summary>
        public double CurioStressHeal(DistrictState state, string heroClass, IEnumerable<string> curioTags)
        {
            var tags = (curioTags ?? new string[0]).ToList();
            var heal = 0.0;
            foreach (var effect in Standing(state, DistrictEffectKind.CurioStress))
                if (AppliesTo(effect, heroClass) && effect.CurioTags.Any(t => tags.Any(have => string.Equals(have, t, StringComparison.OrdinalIgnoreCase)))) heal += effect.Amount;
            return heal;
        }

        /// <summary>The building whose torchlight table is in force (the last one built, should there ever be two); null for DD1's own table.</summary>
        public DistrictEffect Light(DistrictState state) => Standing(state, DistrictEffectKind.Light).LastOrDefault(e => e.Darkness != null || e.DarknessLoot != null);

        private static bool IsEatBuff(CampBuffDef buff) => buff.StatType == "hp_heal_received_percent" && buff.SubType == "eat";

        /// <summary>
        /// The share added to what eating restores (0.15). DD1 hands the buff to every hero (the granary's has
        /// no tags); a party eats as one here, so only such a buff for everyone counts.
        /// </summary>
        public double EatBonus(DistrictState state)
        {
            var bonus = 0.0;
            foreach (var effect in Standing(state, DistrictEffectKind.HeroBuff))
            {
                if (effect.Tags.Count > 0) continue;
                foreach (var id in effect.BuffIds)
                {
                    var buff = Buff(id);
                    if (buff != null && IsEatBuff(buff) && !buff.Conditional) bonus += BuffAmount(id);
                }
            }
            return bonus;
        }

        /// <summary>What the buildings that stand give a hero of this class.</summary>
        public DistrictHeroEffect HeroEffect(DistrictState state, string classId)
        {
            var result = new DistrictHeroEffect();
            foreach (var effect in Standing(state, DistrictEffectKind.HeroBuff))
            {
                if (!AppliesTo(effect, classId)) continue;
                foreach (var id in effect.BuffIds) Map(id, effect.Tags.Count == 0, result);
            }
            return result;
        }

        /// <summary>What one buff is worth to a hero, whatever stands: for the screen's own words.</summary>
        public DistrictHeroEffect BuffEffect(string buffId, bool forEveryone)
        {
            var result = new DistrictHeroEffect();
            Map(buffId, forEveryone, result);
            return result;
        }

        private void Map(string id, bool forEveryone, DistrictHeroEffect into)
        {
            var buff = Buff(id);
            var amount = BuffAmount(id);
            if (buff == null || buff.Conditional)
            {
                into.LeftOut.Add(id);
                return;
            }
            var before = into.Lines.Count;
            if (buff.StatType == "scouting_chance") into.Scouting += amount;
            else if (IsEatBuff(buff))
            {
                // the party's meals, not the hero's stats: see EatBonus
                if (!forEveryone)
                {
                    into.LeftOut.Add(id);
                    return;
                }
            }
            else if (buff.StatType == "hp_heal_percent") into.Lines.Add(new Dd2StatLine { Stat = HealDealt, Value = amount });
            else if (SkillChances.TryGetValue(buff.StatType, out var kind)) into.Lines.Add(new Dd2StatLine { Kind = "sub_stat", Stat = ResistanceIgnore, Sub = kind, Value = amount });
            else
            {
                into.Lines.AddRange(CampingBuffMap.Lines(buff, amount, -1));
                if (into.Lines.Count == before)
                {
                    into.LeftOut.Add(id);
                    return;
                }
            }
            into.Given.Add(id);
        }
    }

    /// <summary>
    /// Where DD1's blueprints come from: a monster's <c>loot: .code "BLUEPRINT" .count 1</c>, which stock DD1
    /// writes into the veteran and the champion form of seven of its eight dungeon bosses (the Flesh carries
    /// none). The estate's story quests stand where DD1's plot quests stand, so a won quest pays the blueprints
    /// of the monsters its DD1 quest asks to be killed: quest.plot_quests.json (goal_ids) ->
    /// quest.types.json (the goal's monster_class_ids) -> monsters/&lt;boss&gt;/&lt;boss&gt;_B/&lt;boss&gt;_B.info.darkest.
    /// </summary>
    public sealed class BlueprintSources
    {
        public const string LootCode = "BLUEPRINT";
        public const string LootFile = DistrictRules.Feature + "loot/blueprint_overrides.loot.json";
        private const string GoalsFile = "campaign/quest/quest.types.json";

        // FALLBACK, not read: the bosses of stock DD1 whose second and third quest pay one blueprint each.
        private static readonly string[] StockBosses = { "necromancer", "prophet", "hag", "brigand_cannon", "swine_prince", "siren", "drowned_crew" };

        private readonly Dictionary<string, int> _byQuest = new Dictionary<string, int>();

        /// <summary>True when DD1's quests could not be read and the stock list stands in.</summary>
        public bool Missing { get; private set; }

        /// <summary>DD1 plot quest id -> blueprints its monsters carry; quests that pay none are not listed.</summary>
        public IReadOnlyDictionary<string, int> ByQuest => _byQuest;

        public int Total => _byQuest.Values.Sum();

        /// <summary>The blueprints a won DD1 plot quest brings home.</summary>
        public int For(string dd1QuestId) => dd1QuestId != null && _byQuest.TryGetValue(dd1QuestId, out var count) ? count : 0;

        public static BlueprintSources Load(IDd1Files files)
        {
            var sources = new BlueprintSources();
            // the feature's own loot table says what one draw of the code is worth (one blueprint)
            var perDraw = 1;
            foreach (var table in Json.Array(Json.ParseFile(files.ReadText(LootFile))?["loot_tables"]))
            {
                if ((string)table["id"] != LootCode) continue;
                var amount = 0;
                foreach (var entry in Json.Array(table["entries"]))
                    if ((string)entry.SelectToken("data.id") == DistrictRules.Blueprint) amount = Math.Max(amount, Json.Int(entry.SelectToken("data.amount"), 1));
                if (amount > 0) perDraw = amount;
            }

            var monsters = new Dictionary<string, List<string>>();
            foreach (var goal in Json.Array(Json.ParseFile(files.ReadText(GoalsFile))?["goals"]))
            {
                var id = (string)goal["id"];
                if (id == null || (string)goal["type"] != "kill_monster") continue;
                monsters[id] = Json.Array(goal.SelectToken("data.monster_class_ids")).Select(m => (string)m).Where(m => !string.IsNullOrEmpty(m)).ToList();
            }
            var plots = Json.Array(Json.ParseFile(files.ReadText(QuestRewardRules.PlotFile))?["plot_quests"]).ToList();
            if (plots.Count == 0 || monsters.Count == 0)
            {
                sources.Missing = true;
                foreach (var boss in StockBosses)
                    for (var tier = 2; tier <= 3; tier++) sources._byQuest["plot_kill_" + boss + "_" + tier.ToString(CultureInfo.InvariantCulture)] = perDraw;
                return sources;
            }

            var carried = new Dictionary<string, int>();
            foreach (var plot in plots)
            {
                var id = (string)plot["id"];
                if (id == null) continue;
                var draws = 0;
                foreach (var goal in Json.Array(plot.SelectToken("quest.goal_ids")))
                {
                    if (goal.Type != JTokenType.String || !monsters.TryGetValue((string)goal, out var classes)) continue;
                    foreach (var monster in classes)
                    {
                        if (!carried.TryGetValue(monster, out var count)) carried[monster] = count = Draws(files, monster);
                        draws += count;
                    }
                }
                if (draws > 0) sources._byQuest[id] = draws * perDraw;
            }
            return sources;
        }

        // "necromancer_B" lies in monsters/necromancer/necromancer_B/necromancer_B.info.darkest
        private static int Draws(IDd1Files files, string monsterClass)
        {
            var cut = monsterClass.LastIndexOf('_');
            if (cut <= 0) return 0;
            var text = files.ReadText("monsters/" + monsterClass.Substring(0, cut) + "/" + monsterClass + "/" + monsterClass + ".info.darkest");
            var draws = 0;
            foreach (var block in DarkestFile.Parse(text))
                if (block.Name == "loot" && block.Text("code") == LootCode) draws += Math.Max(1, block.Int("count", 0, 1));
            return draws;
        }
    }
}
