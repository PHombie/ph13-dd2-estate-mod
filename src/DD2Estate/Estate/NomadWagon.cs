using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dd2;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Nomad Wagon, selling DD2's trinkets. Everything about the trade is DD1's, read from the install
    /// (campaign/town/buildings/nomad_wagon/nomad_wagon.building.json through <see cref="TrinketRules"/> and
    /// <see cref="UpgradeRules"/>): how many trinkets the wagon brings (<c>number_of_trinkets_upgrades</c>,
    /// the Wagon Size tree), the rarity each is rolled at (<c>rarity_generation_table</c>), what a rarity costs
    /// (the trinkets' own prices) less the Merchant Network's discount
    /// (<c>trinket_cost_discount_upgrades</c>), and what the wagon pays for a trinket the estate sells
    /// (<c>trinket_sell_value_discount_upgrades</c>). The stock is rolled once a week and saved; what is
    /// bought goes to the estate's trinket store (<see cref="Trinkets.Give"/>).
    /// </summary>
    [EstateModule]
    internal static class NomadWagon
    {
        public const string BuildingId = "nomad_wagon";
        public const string SizeTree = "nomad_wagon.numitems";
        public const string DiscountTree = "nomad_wagon.cost";
        private const string SectionKey = "nomad_wagon";
        // FALLBACK, used only when DD1's building file cannot be read: its stock before any upgrade.
        private const float BaseStock = 2f;

        /// <summary>One trinket on the wagon's table.</summary>
        public class Ware
        {
            /// <summary>DD2 item id.</summary>
            public string Id;
            /// <summary>The DD1 rarity the ware was rolled at: its price and its card.</summary>
            public string Rarity;
            public bool Sold;
        }

        private static readonly List<Ware> Stock = new List<Ware>();
        private static int _stockWeek = -1;

        /// <summary>
        /// A further share off the wagon's prices for the week, on top of the Merchant Network: set by a town
        /// event (DD1's Nomad New Year halves trinket prices), cleared by whoever set it.
        /// </summary>
        public static float EventDiscount;

        /// <summary>Raised when the stock or the stores changed through the wagon.</summary>
        public static event Action Changed;

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.WeekAdvanced += week => Restock(week);
            UpgradeRules.Built += OnBuilt;
            Buildings.Register(BuildingId, NomadWagonPanel.Open);
            // both trees do something now: the upgrade pane no longer says otherwise
            UpgradeText.Pending.Remove(SizeTree);
            UpgradeText.Pending.Remove(DiscountTree);
        }

        // ---- save section --------------------------------------------------------------------------------

        private static JToken Save()
        {
            var wares = new JArray();
            foreach (var ware in Stock) wares.Add(new JObject { ["id"] = ware.Id, ["rarity"] = ware.Rarity, ["sold"] = ware.Sold });
            return new JObject { ["week"] = _stockWeek, ["stock"] = wares };
        }

        private static void Load(JToken json)
        {
            Reset();
            if (json == null) return;
            _stockWeek = (int?)json["week"] ?? -1;
            foreach (var ware in json["stock"] as JArray ?? new JArray())
                if ((string)ware["id"] != null)
                    Stock.Add(new Ware { Id = (string)ware["id"], Rarity = (string)ware["rarity"] ?? "common", Sold = (bool?)ware["sold"] ?? false });
        }

        private static void Reset()
        {
            Stock.Clear();
            _stockWeek = -1;
            EventDiscount = 0f;
        }

        // ---- the stock -----------------------------------------------------------------------------------

        /// <summary>Trinkets the wagon brings each week at its present size.</summary>
        public static int StockSize => Math.Max(0, (int)UpgradeRules.Value(SizeTree, "number_of_trinkets_upgrades", BaseStock));

        /// <summary>This week's wares, sold ones included (rolled on first use each week).</summary>
        public static IReadOnlyList<Ware> Current()
        {
            if (_stockWeek != EstateState.Current.Week) Restock(EstateState.Current.Week);
            return Stock;
        }

        /// <summary>Rolls the week's stock: DD1's rarity table once per place on the table, a DD2 trinket for each.</summary>
        public static void Restock(int week)
        {
            Stock.Clear();
            _stockWeek = week;
            var rng = new Rng(DateTime.Now.Ticks ^ (week * 104729L));
            Fill(StockSize, rng);
            Plugin.Log.LogInfo("Nomad wagon: week " + week + ", " + Stock.Count + " of " + StockSize + " trinkets: " + string.Join(", ", Stock.Select(w => w.Id + " (" + w.Rarity + ")")));
            Raise();
        }

        // Wares up to a size. A trinket is on the table once, and never one a quest of the week pays.
        private static void Fill(int size, Rng rng)
        {
            var taken = new HashSet<string>(Stock.Select(w => w.Id));
            foreach (var id in QuestBoard.RewardTrinkets()) taken.Add(id);
            foreach (var rarity in Trinkets.Rules.RollWagonStock(Math.Max(0, size - Stock.Count), rng))
            {
                var id = Trinkets.Roll(rarity, rng, taken);
                if (id == null)
                {
                    Plugin.Log.LogInfo("Nomad wagon: no trinket left for a " + rarity + " place on the table");
                    continue;
                }
                taken.Add(id);
                Stock.Add(new Ware { Id = id, Rarity = rarity });
            }
        }

        // DD1 shows a bigger wagon's extra wares at once: the new places are filled, the rest of the week's stock stays.
        private static void OnBuilt(UpgradeRules.Tree tree)
        {
            if (tree == null || tree.Id != SizeTree || _stockWeek != EstateState.Current.Week) return;
            Fill(StockSize, new Rng(DateTime.Now.Ticks));
            Raise();
        }

        /// <summary>Trinkets on the table this week, for systems that must not hand out the same one.</summary>
        public static IEnumerable<string> WareIds()
        {
            return _stockWeek == EstateState.Current.Week ? Stock.Where(w => !w.Sold).Select(w => w.Id).ToList() : new List<string>();
        }

        // ---- trade ---------------------------------------------------------------------------------------

        /// <summary>The share taken off DD1's price: the Merchant Network's steps plus a town event's, at most all of it.</summary>
        public static float Discount => Math.Min(1f, UpgradeRules.Discount(DiscountTree) + Math.Max(0f, EventDiscount));

        /// <summary>Gold for a ware: DD1's price of its rarity after the discounts; never free.</summary>
        public static int Price(Ware ware)
        {
            var gold = Trinkets.Rules.Price(ware.Rarity);
            if (gold <= 0) return 0;
            var price = UpgradeRules.Price(gold, DiscountTree);
            return EventDiscount > 0f ? Math.Max(1, ActivityRules.WholeGold(gold * (1f - Discount))) : price;
        }

        /// <summary>Why a ware cannot be bought right now; null when it can.</summary>
        public static string BuyBlockReason(Ware ware)
        {
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            if (ware == null) return "No such trinket";
            if (ware.Sold) return "Sold";
            if (Trinkets.Find(ware.Id) == null) return "The wagon has lost it";
            if (!Trinkets.CanHold(ware.Id)) return "The estate holds one already";
            return Price(ware) > EstateState.Gold ? "Not enough gold" : null;
        }

        /// <summary>Buys a ware of this week's stock into the estate's stores. Returns null, or why nothing was bought.</summary>
        public static string Buy(int index)
        {
            var stock = Current();
            var ware = index >= 0 && index < stock.Count ? stock[index] : null;
            var reason = BuyBlockReason(ware);
            if (reason != null) return reason;
            var price = Price(ware);
            if (!Trinkets.Give(ware.Id, ware.Rarity)) return "The stores would not take it";
            EstateState.AddGold(-price);
            ware.Sold = true;
            Plugin.Log.LogInfo("Nomad wagon: " + ware.Id + " (" + ware.Rarity + ") bought for " + price + " gold");
            Raise();
            EstatePersistence.SaveNow("wagon");
            return null;
        }

        /// <summary>Sells an unworn trinket of the estate's stores to the wagon. Returns null, or why nothing was sold.</summary>
        public static string Sell(string id)
        {
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            var reason = Trinkets.Sell(id);
            if (reason != null) return reason;
            Raise();
            EstatePersistence.SaveNow("wagon");
            return null;
        }

        private static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Nomad wagon: a Changed handler failed: " + e); }
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe()
        {
            var wares = new List<object>();
            var stock = Current();
            for (var i = 0; i < stock.Count; i++)
            {
                var ware = stock[i];
                wares.Add(new
                {
                    index = i, id = ware.Id, name = Trinkets.Name(ware.Id), rarity = ware.Rarity, dd2 = Trinkets.SubTypeOf(ware.Id), dd1Price = Trinkets.Rules.Price(ware.Rarity),
                    price = Price(ware), sold = ware.Sold, blocked = BuyBlockReason(ware)
                });
            }
            var stores = new List<object>();
            foreach (var id in Trinkets.Stored()) stores.Add(new { id, name = Trinkets.Name(id), grade = Trinkets.Grade(id), sell = Trinkets.SellPrice(id) });
            return new
            {
                week = _stockWeek, size = StockSize, sizeLevel = UpgradeRules.Level(SizeTree), discount = Discount, discountLevel = UpgradeRules.Level(DiscountTree),
                eventDiscount = EventDiscount, sellShare = 1 - Trinkets.Rules.SellDiscount, gold = EstateState.Gold, wares, stores
            };
        }
    }
}
