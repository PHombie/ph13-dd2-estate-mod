using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Roster;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Estate;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The step between choosing a quest and walking into the dungeon: a visit to DD1's provision shop with the
    /// estate's purse. The provision screen drives a shop made here; the dev bridge can skip the screen and take
    /// the standard kit.
    /// </summary>
    internal static class Provisioning
    {
        /// <summary>What a party member brings along by DD1's class table (a crusader's holy water...).</summary>
        public class Gift
        {
            public ActorInstance Hero;
            public ItemStack Stack;
        }

        public static List<ActorInstance> Party()
        {
            var heroes = new List<ActorInstance>();
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            foreach (var guid in Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY))
            {
                var hero = library.GetLibraryElement(guid);
                if (hero != null && hero.IsLiving) heroes.Add(hero);
            }
            return heroes;
        }

        // DD2's class ids are DD1's for the heroes both games have; the others bring nothing.
        public static List<Gift> Gifts()
        {
            var gifts = new List<Gift>();
            foreach (var hero in Party())
                foreach (var stack in InventoryContent.Provision.ClassItems(hero.ActorDataId))
                    gifts.Add(new Gift { Hero = hero, Stack = stack });
            return gifts;
        }

        /// <summary>A shop for a quest of this length, with the estate's purse and the party's own items already in the bag.</summary>
        public static ProvisionShop NewShop(int length, bool free = false)
        {
            var shop = new ProvisionShop(InventoryContent.Provision, InventoryContent.Rules, length, free ? int.MaxValue : EstateState.Gold);
            foreach (var gift in Gifts()) shop.AddFree(gift.Stack.Item, gift.Stack.Amount);
            // DD1 hands out firewood by length (raid_starting_length_inventory_item_lists): one log, one camp.
            foreach (var stack in InventoryContent.Provision.StartingItems(length)) shop.AddFree(stack.Item, stack.Amount);
            // DD1's Granary (a district building): some of the week's food is the party's before anything is bought.
            var granary = Districts.FreeFood();
            if (granary > 0 && InventoryContent.Items.Food != null) shop.AddFree(InventoryContent.Items.Food, granary);
            // The week's town event may change the shelves: cheaper or free supplies, or fewer of them.
            foreach (var line in shop.Lines)
            {
                line.PriceFactor = TownEventHooks.ProvisionPriceFactor(line.Item.Type);
                line.Stock = TownEventHooks.ProvisionStock(line.Item.Type, line.Stock);
            }
            return shop;
        }

        /// <summary>Pays the bill and hands over the bag.</summary>
        public static Core.Inventory SetOut(ProvisionShop shop, bool free = false)
        {
            if (!free) EstateState.AddGold(-shop.Cost);
            Plugin.Log.LogInfo("Provisions: " + (free ? "free" : shop.Cost + " gold") + ", " + shop.Food + " food, " + (shop.Bag.SlotCount - shop.Bag.FreeSlots) + " slots used");
            return shop.Bag;
        }

        /// <summary>The standard kit as far as the purse goes (all of it when free), without the screen.</summary>
        public static Core.Inventory StandardBag(int length, bool free)
        {
            var shop = NewShop(length, free);
            shop.BuyStandardKit();
            return SetOut(shop, free);
        }
    }
}
