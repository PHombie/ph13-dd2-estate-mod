using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Game;
using Assets.Code.Item;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Test commands for what passes between the expedition and a DD2 fight (docs/recon/inventory-unification.md,
    /// 4.3), for the dev bridge.
    ///
    ///   python tools/bridge.py run fight.loot kind=room enemies=3 light=20
    ///
    /// fight.loot rolls DD1's battle loot rule without a fight: the table codes the fight would pay and what
    /// they came to. The fight is described by {"kind":"hallway|room|boss","enemies":3,"large":1} or taken
    /// from a real DD2 configuration ({"config":"gaunt_mash_4"}; {"boss":"librarian"} names the quest's boss
    /// whose own actors pay nothing; {"kind":"boss","boss":"collector","enemies":0} is a wanderer's fight, which
    /// pays DD1's own codes for that monster: "rule.bosses" lists them, "monsterTrinkets" the DD2 trinkets its
    /// own DD1 rarity is drawn from). {"light":20} is the torchlight the fight ends in (the dark adds draws),
    /// {"dungeon":"crypts","tier":3} the dungeon and DD1 difficulty when no expedition is on, {"times":200}
    /// rolls that often and counts. {"show":true} offers one roll on DD1's "Victory!" scroll, as after a won
    /// fight (an expedition must be on and idle; what is taken goes into the bag).
    ///
    /// fight.items is the spike of step 6a: it puts DD2 combat items into the combat item slots of the party's
    /// heroes, as many slots as the items need (DD2 gives a hero one), so that DD2's combat bar can be looked
    /// at with one, three and six items on a hero. That picture decides how the bag's supplies are offered in a
    /// fight: every hero showing all of them, or one item a hero.
    ///
    ///   python tools/bridge.py run fight.items items=bandages:3
    ///   python tools/bridge.py run fight.items items=bandages:2,holy_water:2,antivenom:2
    ///   python tools/bridge.py run fight.items items=bandages:2,antivenom:2,medicinal_herbs:2,holy_water:2,laudanum:2,torch_consumable:2
    ///   python tools/bridge.py run fight.items clear=true
    ///
    /// {"hero":5} names one hero (default: every living hero of the party); {"clear":true} empties the slots
    /// again and takes the added ones away (nothing is given back to anybody); without arguments: what the
    /// heroes carry. THE HEROES CHANGE and the next save has it: clear before the estate is saved for keeps.
    /// Use it in the hub and start a fight, or in a fight (whether the bar follows a change made under it is
    /// one of the things to see). The six DD2 items that stand for DD1's supplies are bandages, antivenom,
    /// medicinal_herbs, holy_water, laudanum and torch_consumable.
    ///
    /// fight.torch is for the light, which goes both ways: DD1's torchlight is DD2's flame as a fight begins,
    /// and what the fight did to the flame is done to the torchlight when the party is back. {"change":-20}
    /// changes DD2's flame the way a fight's effect does (RunValues.ChangeValue), {"set":35} sets it; without
    /// arguments: both sides as they stand. In a fight: change it, win, and dungeon.state shows the torchlight
    /// moved by as much ("books" there has the flame before and after the fight). Out of a fight DD2's flame is
    /// only a copy and the next change of the torchlight writes over it. {"follow":false} switches the way
    /// back off (the torchlight then ignores what a fight did to the flame, as it used to), {"follow":true} on.
    /// </summary>
    [EstateModule]
    internal static class FightDev
    {
        private static void Register()
        {
            AgentBridge.Register("fight.loot", FightLoot);
            AgentBridge.Register("fight.items", FightItems);
            AgentBridge.Register("fight.torch", o =>
            {
                if (!EstateSession.Active) return "the Estate is not open";
                var values = Singleton<GameTypeMgr>.Instance.RunValues;
                var before = DungeonRun.Torch();
                if (o["follow"] != null) DungeonRun.LightFollowsTheFight = (bool)o["follow"];
                if (o["set"] != null) values.SetValue(Assets.Code.Run.RunValueType.TORCH, (float)o["set"]);
                else if (o["change"] != null) values.ChangeValue(Assets.Code.Run.RunValueType.TORCH, (float)o["change"], Assets.Code.Source.SourceType.DEBUG);
                return new
                {
                    mode = GameModeMgr.CurrentMode?.GetName(), follows = DungeonRun.LightFollowsTheFight, torchBefore = before, torch = DungeonRun.Torch(),
                    expedition = DungeonRun.Current?.LightBooks()
                };
            });
        }

        // ---- step 6a: DD2 combat items on the party's heroes -------------------------------------------------

        // More slots than this on one hero are not added, whatever is asked for.
        private const int MostSlots = 12;

        private static object FightItems(JObject o)
        {
            if (!EstateSession.Active) return "the Estate is not open";
            var heroes = new List<ActorInstance>();
            if (o["hero"] != null)
            {
                var one = UpgradeUi.Hero((uint)o["hero"]);
                if (one == null || !one.IsLiving) return "no such living hero";
                heroes.Add(one);
            }
            else heroes.AddRange(Provisioning.Party());
            if (heroes.Count == 0) return "no living hero in the party";

            var notes = new List<string>();
            if ((bool?)o["clear"] == true)
            {
                foreach (var hero in heroes)
                {
                    // Clear empties every slot and takes the slots past the inventory's own number away again
                    hero.GetCombatSkillInventory()?.Clear();
                }
                notes.Add("the combat item slots of " + heroes.Count + " hero(es) are empty again");
            }
            else if (o["items"] != null)
            {
                var wanted = new List<KeyValuePair<ItemDefinition, int>>();
                foreach (var part in ((string)o["items"]).Split(','))
                {
                    var pair = part.Trim().Split(':');
                    if (pair[0].Length == 0) continue;
                    var item = Trinkets.Find(pair[0]);
                    if (item == null) return "no such DD2 item: " + pair[0];
                    if (item.m_type != ItemType.COMBAT || item.GetActorDataSkill() == null) return pair[0] + " is not a combat item (DD2 has no skill for it)";
                    wanted.Add(new KeyValuePair<ItemDefinition, int>(item, pair.Length > 1 && int.TryParse(pair[1], out var amount) ? Math.Max(1, amount) : 1));
                }
                foreach (var hero in heroes)
                {
                    var slots = hero.GetCombatSkillInventory();
                    if (slots == null) continue;
                    foreach (var want in wanted)
                    {
                        // DD2's own call for it is ActorInstance.AddCombatSkillInventoryItem, which is AddItems on
                        // this inventory; a hero has one slot, so slots are added as the items ask for them
                        var left = slots.AddItems(want.Key, want.Value, false);
                        while (left > 0 && slots.GetNumberOfTotalSlots() < MostSlots)
                        {
                            slots.AddSlots(1);
                            var still = slots.AddItems(want.Key, left, false);
                            if (still >= left) break;       // the new slot took nothing: DD2 will not let this hero carry it
                            left = still;
                        }
                        if (left > 0) notes.Add(hero.ActorName + ": " + left + " " + want.Key.m_id + " did not go in");
                    }
                }
            }

            var carried = new List<object>();
            foreach (var hero in heroes)
            {
                var slots = hero.GetCombatSkillInventory();
                var items = new List<object>();
                if (slots != null)
                    for (var i = 0; i < slots.GetNumberOfTotalSlots(); i++)
                    {
                        var item = slots.GetItem(i);
                        items.Add(ItemUtils.IsValid(item) ? (object)new { slot = i, id = item.GetItemDefinition().m_id, qty = item.GetQty(), max = item.GetMaxQty(slots.GetIsRunStatModified()) } : new { slot = i, id = (string)null, qty = 0, max = 0 });
                    }
                carried.Add(new { guid = hero.ActorGuid, name = hero.ActorName, cls = hero.ActorDataId, slots = slots?.GetNumberOfTotalSlots() ?? 0, ownSlots = slots?.InventoryLimit ?? 0, items });
            }
            // an item a hero may not carry is handed to DD2's player inventory by DD2 itself (CombatItemInventory.UpdateEquipped)
            var bounced = new List<string>();
            var player = Singleton<GameTypeMgr>.Instance.PlayerInventory;
            if (player != null)
                foreach (var item in player.GetValidItems())
                    if (item.GetItemType() == ItemType.COMBAT) bounced.Add(item.GetItemDefinition().m_id + " x" + item.GetQty());
            return new { mode = GameModeMgr.CurrentMode?.GetName(), notes, heroes = carried, inDd2Inventory = bounced };
        }

        private static object FightLoot(JObject o)
        {
            var run = DungeonRun.Current;
            var dungeon = (string)o["dungeon"] ?? run?.Exploration.Map.DungeonId ?? "crypts";
            var tier = (int?)o["tier"] ?? run?.Exploration.Map.Tier ?? 1;
            var kindName = (string)o["kind"] ?? "room";
            var kind = kindName == "hallway" ? EncounterKind.Hallway : kindName == "boss" ? EncounterKind.Boss : EncounterKind.Room;
            var light = (double?)o["light"] ?? run?.Exploration.Light ?? 100.0;
            var rules = DungeonContent.BattleLoot;

            List<BattleEnemy> enemies;
            var config = (string)o["config"];
            if (config != null)
            {
                enemies = DungeonContent.Enemies(config);
                if (enemies.Count == 0) return "no such battle configuration, or it has no enemies: " + config;
            }
            else
            {
                enemies = new List<BattleEnemy>();
                for (var i = 0; i < Math.Max(0, (int?)o["enemies"] ?? 3); i++) enemies.Add(new BattleEnemy { Id = "enemy" + i, Size = 1 });
                for (var i = 0; i < Math.Max(0, (int?)o["large"] ?? 0); i++) enemies.Add(new BattleEnemy { Id = "large" + i, Size = rules.LargeSize });
            }
            var bossId = (string)o["boss"];
            var own = kind == EncounterKind.Boss && bossId != null
                ? DungeonContent.Boss(dungeon, bossId, DungeonContent.TierName(dungeon, tier))?.Actors
                : null;
            var draws = rules.Draws(dungeon, kind, enemies, own, kind == EncounterKind.Boss ? bossId : null);

            var result = new Dictionary<string, object>
            {
                ["dungeon"] = dungeon, ["tier"] = tier, ["kind"] = kind.ToString().ToLowerInvariant(), ["light"] = light,
                ["rule"] = new
                {
                    enemy = rules.Enemy.Table + "x" + rules.Enemy.Draws, large = rules.LargeEnemy.Table + "x" + rules.LargeEnemy.Draws, largeFrom = rules.LargeSize,
                    boss = rules.Boss.Table + "x" + rules.Boss.Draws, darkness = rules.DarknessKey, noLoot = rules.NoLootDungeons.OrderBy(d => d).ToList(),
                    bosses = rules.ByBoss.OrderBy(b => b.Key).ToDictionary(b => b.Key, b => string.Join(" ", b.Value.Select(d => d.Table + "x" + d.Draws)))
                },
                // DD1's rarities of one monster's fight: DD2's table for each and what the estate could still get of it
                ["monsterTrinkets"] = Trinkets.MonsterRarities.OrderBy(r => r).ToDictionary(r => r, r => (object)new
                {
                    table = Trinkets.MonsterTable(r), trinkets = Trinkets.TableTrinkets(Trinkets.MonsterTable(r)),
                    free = Trinkets.TableTrinkets(Trinkets.MonsterTable(r)).Where(Trinkets.CanHold).ToList()
                }),
                ["enemies"] = enemies.Select(e => new { id = e.Id, size = e.Size, bystander = e.Bystander }).ToList(),
                ["bossOwn"] = own?.OrderBy(a => a).ToList(),
                ["pays"] = draws.Select(d => d.Table + "x" + d.Draws).ToList(),
                ["draws"] = BattleLootRules.Count(draws)
            };

            var tables = LootTables.Load(new Dd1Files());
            var times = Math.Max(1, Math.Min(5000, (int?)o["times"] ?? 1));
            if (times > 1)
            {
                // what the rule comes to over many fights: drops a fight, and how often each kind turns up
                var rng = new Rng((long?)o["seed"] ?? DateTime.Now.Ticks);
                var kinds = new SortedDictionary<string, int>();
                var all = 0;
                long gold = 0;
                for (var i = 0; i < times; i++)
                    foreach (var drop in rules.Roll(tables, draws, light, tier, dungeon, rng))
                    {
                        all++;
                        kinds.TryGetValue(drop.Kind.ToString(), out var seen);
                        kinds[drop.Kind.ToString()] = seen + 1;
                        if (drop.Kind == LootKind.Gold) gold += drop.Amount;
                    }
                result["times"] = times;
                result["dropsAFight"] = Math.Round(all / (double)times, 2);
                result["dd1GoldAFight"] = Math.Round(gold / (double)times, 1);
                result["kinds"] = kinds;
                return result;
            }

            List<LootDrop> drops;
            var shown = false;
            if (run != null) drops = run.DevBattleLoot(draws, light, (bool?)o["show"] ?? false, out shown);
            else drops = rules.Roll(tables, draws, light, tier, dungeon, new Rng((long?)o["seed"] ?? DateTime.Now.Ticks));
            result["drops"] = drops.Select(d => d.ToString()).ToList();
            result["shown"] = shown;
            if ((bool?)o["show"] == true && !shown) result["notShown"] = run == null ? "no expedition" : drops.Count == 0 ? "nothing was found" : "the expedition is busy, or the find is trinkets only";
            if (run != null) result["onScroll"] = run.LootOnScroll.Select(find => new { source = FoundLoot.Key(find.Source), items = ProvisionScreen.StackList(find.Stacks) }).ToList();
            return result;
        }
    }
}
