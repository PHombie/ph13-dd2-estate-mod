using DD2Estate.Core;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Nomad Wagon and the estate's trinkets for the dev bridge
    /// ({"cmd":"run","name":"wagon.buy","index":0}); they do what the screen does, without the mouse.
    /// </summary>
    [EstateModule]
    internal static class NomadWagonDev
    {
        private static void Register()
        {
            // This week's wares with DD1 rarity, DD1 price and the estate's price, the stores with sell values, the screen.
            AgentBridge.Register("wagon.state", o => new { wagon = NomadWagon.Describe(), screen = NomadWagonPanel.Snapshot() });
            AgentBridge.Register("wagon.open", o =>
            {
                NomadWagonPanel.Open();
                if (o["upgrades"] != null) NomadWagonPanel.ShowUpgrades((bool)o["upgrades"]);
                return NomadWagonPanel.Snapshot();
            });
            AgentBridge.Register("wagon.close", o =>
            {
                NomadWagonPanel.Close();
                return "closed";
            });
            // Rolls the stock again as a new week would (the estate's week is not changed).
            AgentBridge.Register("wagon.restock", o =>
            {
                NomadWagon.Restock(EstateState.Current.Week);
                return NomadWagon.Describe();
            });
            // {"index":0}: buys that ware into the estate's stores.
            AgentBridge.Register("wagon.buy", o => NomadWagon.Buy((int?)o["index"] ?? 0) ?? (object)NomadWagon.Describe());
            // {"id":"trinket_tiered_wolfsblood_minor"}: sells one unworn trinket of the stores (no question asked).
            AgentBridge.Register("wagon.sell", o => NomadWagon.Sell((string)o["id"]) ?? (object)NomadWagon.Describe());
            // A town event's share off the prices: {"share":0.5}; {"share":0} ends it.
            AgentBridge.Register("wagon.discount", o =>
            {
                NomadWagon.EventDiscount = (float?)o["share"] ?? 0f;
                return NomadWagon.Describe();
            });

            // DD1 rarities with their DD2 counterpart and prices, DD2's pools per rarity (size, how many are free), the stores.
            AgentBridge.Register("trinkets.state", o => Trinkets.Describe());
            // One trinket's name, DD2 rarity, DD1 grade, sell value, whether the estate may take one, its description.
            AgentBridge.Register("trinkets.inspect", o =>
            {
                var id = (string)o["id"];
                if (Trinkets.Find(id) == null) return "no such item";
                return new
                {
                    id, name = Trinkets.Name(id), dd2 = Trinkets.SubTypeOf(id), grade = Trinkets.Grade(id), sell = Trinkets.SellPrice(id),
                    canHold = Trinkets.CanHold(id), inStores = Trinkets.InStore(id), worn = Trinkets.Worn(id), held = Trinkets.Held(id),
                    icon = Trinkets.Icon(id) != null, description = Trinkets.Description(id)
                };
            });
            // The owner's rule, [Rules] UniqueTrinkets: {"on":true} never two of the same trinket (the default),
            // {"on":false} the estate may hold copies (DD1's way). For this session: the setting is read again when
            // the game starts. "duplicates": what the estate holds more than one of (none under the rule).
            AgentBridge.Register("trinkets.oneofeach", o =>
            {
                if (o["on"] != null) Trinkets.OneOfEach = (bool)o["on"];
                return new { oneOfEach = Trinkets.OneOfEach, duplicates = Trinkets.Duplicates() };
            });
            // Whether a draw can still give a trinket, rarity by rarity, as every source draws (a quest, the wagon,
            // loot): {"times":200} draws that often for each DD1 rarity without giving anything and says how many
            // different trinkets came, how many of them the estate holds (none under the rule), from which DD2
            // rarities, and what is paid when nothing is left ("goldInstead").
            AgentBridge.Register("trinkets.draws", o =>
            {
                var times = System.Math.Max(1, (int?)o["times"] ?? 100);
                var rng = new Rng(System.DateTime.Now.Ticks);
                var rows = new System.Collections.Generic.List<object>();
                foreach (var rarity in TrinketRules.Ladder)
                {
                    var seen = new System.Collections.Generic.HashSet<string>();
                    var from = new System.Collections.Generic.SortedDictionary<string, int>();
                    int none = 0, held = 0;
                    for (var i = 0; i < times; i++)
                    {
                        var id = Trinkets.Roll(rarity, rng);
                        if (id == null)
                        {
                            none++;
                            continue;
                        }
                        if (seen.Add(id) && Trinkets.Held(id) > 0) held++;
                        var subType = Trinkets.SubTypeOf(id) ?? "?";
                        from.TryGetValue(subType, out var n);
                        from[subType] = n + 1;
                    }
                    rows.Add(new { rarity, draws = times, different = seen.Count, heldAmongThem = held, nothingLeft = none, from, goldInstead = Trinkets.GoldInstead(rarity) });
                }
                return new { oneOfEach = Trinkets.OneOfEach, duplicates = Trinkets.Duplicates(), rarities = rows };
            });
            // Draws a DD2 trinket for a DD1 rarity as a quest reward would and puts it in the stores:
            // {"rarity":"rare"}; {"rarity":"rare","give":false} only says which one it would be.
            AgentBridge.Register("trinkets.roll", o =>
            {
                var rarity = (string)o["rarity"] ?? "common";
                var id = Trinkets.Roll(rarity, new Rng(System.DateTime.Now.Ticks));
                if (id == null) return "nothing left to give for " + rarity;
                var give = (bool?)o["give"] ?? true;
                if (give) Trinkets.Give(id, rarity);
                return new { rarity, id, name = Trinkets.Name(id), dd2 = Trinkets.SubTypeOf(id), given = give };
            });
            // Puts a named DD2 trinket in the stores: {"id":"trinket_ancestors_pistol","rarity":"ancestral"}.
            AgentBridge.Register("trinkets.give", o =>
                Trinkets.Give((string)o["id"], (string)o["rarity"]) ? (object)Trinkets.Describe() : "no such item");
        }
    }
}
