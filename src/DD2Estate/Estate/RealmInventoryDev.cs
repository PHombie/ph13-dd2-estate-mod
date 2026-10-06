using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Trinket Inventory for the dev bridge ({"cmd":"run","name":"inventory.equip",
    /// "guid":3,"id":"trinket_ancestors_pistol"}); they do what the panel does, without the mouse.
    /// Trinkets come into the stores with the wagon's commands (trinkets.give, trinkets.roll).
    /// </summary>
    [EstateModule]
    internal static class RealmInventoryDev
    {
        private static void Register()
        {
            // The stores in one of DD1's orders ({"by":"name"|"class"|"rarity","descending":true}) with DD1 grade, class
            // restriction and sell value; every hero's trinket slots; the panel and the bar's buttons.
            AgentBridge.Register("inventory.state", o => new
            {
                inventory = RealmInventory.Describe(RealmInventory.ParseOrder((string)o["by"]), (bool?)o["descending"] ?? false),
                screen = RealmInventoryPanel.Snapshot(), bar = TownPanelButtons.Describe()
            });
            // Whether DD2 would let a hero wear a trinket of the stores, and its reason when not: {"guid":3,"id":"..."};
            // {"slot":1} asks for a drop on that slot.
            AgentBridge.Register("inventory.can", o =>
            {
                var guid = (uint?)o["guid"] ?? 0u;
                var id = (string)o["id"];
                var refusal = RealmInventory.EquipRefusal(guid, id, (int?)o["slot"] ?? -1);
                return new
                {
                    guid, id, name = Trinkets.Name(id), forClass = RealmInventory.ClassOf(id), allowed = refusal == null, refusal,
                    inAnySlot = RealmInventory.WearRefusal(guid, id) == null, inStores = Trinkets.InStore(id)
                };
            });
            // Puts a trinket of the stores on a hero with DD2's own calls: {"guid":3,"id":"...","slot":0} (no slot: the first free one).
            AgentBridge.Register("inventory.equip", o =>
                Finish(RealmInventory.Equip((uint?)o["guid"] ?? 0u, (string)o["id"], (int?)o["slot"] ?? -1)));
            // Takes a worn trinket off, back to the stores: {"guid":3,"id":"..."}.
            AgentBridge.Register("inventory.unequip", o => Finish(RealmInventory.Unequip((uint?)o["guid"] ?? 0u, (string)o["id"])));
            // Hands a worn trinket to another hero: {"guid":3,"id":"...","to":5}.
            AgentBridge.Register("inventory.give", o => Finish(RealmInventory.Give((uint?)o["guid"] ?? 0u, (string)o["id"], (uint?)o["to"] ?? 0u)));
            // DD1's "Unequip all trinkets".
            AgentBridge.Register("inventory.unequip_all", o =>
            {
                var moved = RealmInventory.UnequipAll();
                RealmInventory.Flush();
                return new { moved, inventory = RealmInventory.Describe() };
            });
            // Sells an unworn trinket at DD1's price for its grade, no question asked: {"id":"..."}.
            AgentBridge.Register("inventory.sell", o => RealmInventory.Sell((string)o["id"]) ?? (object)RealmInventory.Describe());
            AgentBridge.Register("inventory.open", o =>
            {
                if (!RealmInventoryPanel.Open()) return TownPanel.BlockReason() ?? "not opened";
                if (o["guid"] != null) RealmInventoryPanel.Pick((uint)o["guid"]);
                return RealmInventoryPanel.Snapshot();
            });
            AgentBridge.Register("inventory.close", o =>
            {
                RealmInventoryPanel.Close();
                return "closed";
            });
            // What a click on a hero's roster row does while the panel is open: {"guid":3}.
            AgentBridge.Register("inventory.hero", o => RealmInventoryPanel.Pick((uint?)o["guid"] ?? 0u) ? RealmInventoryPanel.Snapshot() : (object)"no such hero, or the panel is closed");
            // What a click on a card does (a stored one goes on the panel's hero, a worn one comes off): {"id":"..."}.
            AgentBridge.Register("inventory.click", o => new { said = RealmInventoryPanel.Click((string)o["id"]), screen = RealmInventoryPanel.Snapshot() });
            // The panel's sort buttons: {"by":"rarity","descending":true}; {"by":"arrival"} is DD1's "Custom".
            AgentBridge.Register("inventory.sort", o =>
            {
                var order = RealmInventory.ParseOrder((string)o["by"]);
                var descending = (bool?)o["descending"] ?? false;
                RealmInventoryPanel.SetOrder(order, descending);
                return RealmInventory.Describe(order, descending);
            });
        }

        // Bridge moves are saved at once: nothing closes a panel behind them.
        private static object Finish(string trouble)
        {
            if (trouble != null) return trouble;
            RealmInventory.Flush();
            return RealmInventory.Describe();
        }
    }
}
