using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;

namespace DD2Estate.CoreTests
{
    /// <summary>DD1's heirloom exchange (Core/HeirloomExchange.cs): the file reader against the real install, the arithmetic on rates of its own.</summary>
    public static class HeirloomExchangeTests
    {
        private static readonly string[] Estate = { "crest", "deed", "bust", "portrait" };

        private static string Rate(string from, int fromAmount, string to, int toAmount)
        {
            return "{\"exchange_from_type\":\"" + from + "\",\"exchange_from_amount\":" + fromAmount + ",\"exchange_to_type\":\"" + to + "\",\"exchange_to_amount\":" + toAmount + "}";
        }

        private static HeirloomExchangeRules Rules(params string[] rates)
        {
            return HeirloomExchangeRules.Parse("{\"exchange_rates\":[" + string.Join(",", rates) + "]}");
        }

        private static string Text(HeirloomExchangeRules rules) => string.Join("; ", rules.Rates.Select(r => r.ToString()));

        // ---- DD1's file ----------------------------------------------------------------------------------

        public static void RatesReadFromDd1()
        {
            var rules = HeirloomExchangeRules.Load(Dd1.Files, Estate);
            Check.True(!rules.Missing, "the file is read");
            Check.Equal(12, rules.Rates.Count, "twelve rates");
            Check.True(rules.Kinds().SequenceEqual(new[] { "bust", "portrait", "deed", "crest" }), "kinds in DD1's order: " + string.Join(", ", rules.Kinds()));
            var busts = rules.Find("bust", "portrait");
            Check.True(busts != null && busts.FromAmount == 3 && busts.ToAmount == 1, "3 busts for 1 portrait");
            var portraits = rules.Find("portrait", "crest");
            Check.True(portraits != null && portraits.FromAmount == 1 && portraits.ToAmount == 3, "1 portrait for 3 crests");
            var crests = rules.Find("crest", "portrait");
            Check.True(crests != null && crests.FromAmount == 6 && crests.ToAmount == 1, "6 crests for 1 portrait");
            Check.True(rules.From("deed").Select(r => r.To).SequenceEqual(new[] { "bust", "portrait", "crest" }), "what deeds buy, in the file's order");
            foreach (var kind in rules.Kinds()) Check.Equal(3, rules.From(kind).Count, "every kind buys the other three: " + kind);
            Check.True(rules.Rates.All(r => Estate.Contains(r.From) && Estate.Contains(r.To)), "only the estate's four heirlooms");
        }

        public static void StockRatesAreDd1s()
        {
            var read = HeirloomExchangeRules.Load(Dd1.Files);
            var stock = HeirloomExchangeRules.Load(new NoDd1Files());
            Check.True(stock.Missing, "without DD1 the stock rates stand in");
            Check.Equal(Text(read), Text(stock), "the stand-in is DD1's own table");
        }

        // ---- reading -------------------------------------------------------------------------------------

        public static void BadRatesAreLeftOut()
        {
            var rules = Rules(Rate("bust", 3, "portrait", 1), Rate("bust", 0, "deed", 2), Rate("bust", 2, "crest", 0), Rate("deed", 2, "deed", 3),
                Rate("bust", 5, "portrait", 2), Rate("", 1, "crest", 1));
            Check.Equal("3 bust -> 1 portrait", Text(rules), "zero amounts, a kind for itself, a repeated pair and a nameless kind are no rates");
            Check.True(!rules.Missing, "a file with one good rate is a file");

            var shards = HeirloomExchangeRules.Parse("{\"exchange_rates\":[" + Rate("shard", 3, "bust", 1) + "," + Rate("bust", 3, "portrait", 1) + "]}", Estate);
            Check.Equal("3 bust -> 1 portrait", Text(shards), "a kind the estate does not keep is left out");
            Check.True(HeirloomExchangeRules.Parse("not json").Missing && HeirloomExchangeRules.Parse(null).Rates.Count == 12, "an unreadable file falls back");
        }

        // ---- the amount ----------------------------------------------------------------------------------

        public static void AmountStepsThroughWhatBuysSomething()
        {
            // busts: 3 for a portrait, 3 for 2 deeds, 2 for 3 crests
            var rules = Rules(Rate("bust", 3, "portrait", 1), Rate("bust", 3, "deed", 2), Rate("bust", 2, "crest", 3));
            Check.Equal(2, rules.Smallest("bust"), "smallest");
            Check.True(!rules.IsStep("bust", 1) && rules.IsStep("bust", 2) && rules.IsStep("bust", 3) && rules.IsStep("bust", 4) && !rules.IsStep("bust", 5) && rules.IsStep("bust", 6), "1 and 5 buy nothing");

            var seen = new List<int>();
            var amount = rules.Step("bust", 0, 1, 10);
            for (var guard = 0; guard < 20; guard++)
            {
                seen.Add(amount);
                var next = rules.Step("bust", amount, 1, 10);
                if (next == amount) break;
                amount = next;
            }
            Check.Equal("2, 3, 4, 6, 8, 9, 10", string.Join(", ", seen), "up, with ten busts");
            Check.Equal(9, rules.Step("bust", 10, -1, 10), "down from 10");
            Check.Equal(4, rules.Step("bust", 6, -1, 10), "down from 6 skips 5");
            Check.Equal(2, rules.Step("bust", 2, -1, 10), "no further down than the smallest");
            Check.Equal(10, rules.Largest("bust", 11), "11 busts: 10 is the most that buys something");
            Check.Equal(4, rules.Step("bust", 9, 1, 4), "an amount above what the estate holds comes back down");
            Check.Equal(0, rules.Step("urn", 3, 1, 10), "a kind that buys nothing has no amount");
            Check.Equal(4, rules.Clamp("bust", 5, 10), "5 buys nothing: down to 4");
            Check.Equal(6, rules.Clamp("bust", 6, 10), "an amount in range stays");
            Check.Equal(2, rules.Clamp("bust", 0, 10), "no less than the smallest");
            Check.Equal(3, rules.Clamp("bust", 8, 3), "no more than the estate holds");
        }

        public static void PoorEstateStillSeesTheRates()
        {
            var rules = Rules(Rate("crest", 3, "bust", 1), Rate("crest", 6, "portrait", 1));
            Check.Equal(3, rules.Largest("crest", 2), "two crests: the smallest step stays on show");
            Check.Equal(3, rules.Step("crest", 3, 1, 2), "and the arrows do not move");
            var offers = rules.Offers("crest", 3, 2);
            Check.Equal(HeirloomExchangeRules.Refusal.CannotPay, offers[0].Refusal, "3 for a bust: the estate cannot pay");
            Check.Equal(HeirloomExchangeRules.Refusal.NotAMultiple, offers[1].Refusal, "3 is not 6");
            Check.Equal(1, offers[0].Takes, "the rate is still shown");
        }

        public static void OffersFollowTheirOwnRates()
        {
            var rules = HeirloomExchangeRules.Load(new NoDd1Files());
            var offers = rules.Offers("bust", 6, 10);
            Check.True(offers.Select(o => o.Rate.To).SequenceEqual(new[] { "portrait", "deed", "crest" }), "the file's order");
            Check.True(offers.All(o => o.Valid) && offers[0].Takes == 2 && offers[1].Takes == 4 && offers[2].Takes == 9, "6 busts: 2 portraits, 4 deeds, 9 crests");
            var four = rules.Offers("bust", 4, 10);
            Check.True(!four[0].Valid && !four[1].Valid && four[2].Valid && four[2].Takes == 6, "4 busts buy crests only");
            Check.Equal(HeirloomExchangeRules.Refusal.NotAMultiple, four[0].Refusal, "why not portraits");
            Check.Equal(HeirloomExchangeRules.Refusal.CannotPay, HeirloomExchangeRules.Quote(rules.Find("bust", "crest"), 4, 3).Refusal, "four of three");
        }

        // ---- the trade -----------------------------------------------------------------------------------

        public static void TradeMovesHeirlooms()
        {
            var rules = HeirloomExchangeRules.Load(new NoDd1Files());
            var purse = new Dictionary<string, int> { { "bust", 7 }, { "crest", 1 } };
            var done = rules.Trade(purse, "bust", 6, "crest");
            Check.True(done != null && done.Valid && done.Gives == 6 && done.Takes == 9, "6 busts for 9 crests");
            Check.True(purse["bust"] == 1 && purse["crest"] == 10, "the purse after the trade");

            var refused = rules.Trade(purse, "bust", 2, "crest");
            Check.True(refused != null && refused.Refusal == HeirloomExchangeRules.Refusal.CannotPay, "one bust does not pay for two");
            Check.True(purse["bust"] == 1 && purse["crest"] == 10, "a refused trade changes nothing");

            var first = rules.Trade(purse, "crest", 6, "portrait");
            Check.True(first.Valid && purse["crest"] == 4 && purse["portrait"] == 1, "a kind the purse did not list yet is added");
            Check.True(rules.Trade(purse, "crest", 4, "bust").Refusal == HeirloomExchangeRules.Refusal.NotAMultiple, "4 crests at 3 for 1");
            Check.True(rules.Trade(purse, "crest", 3, "crest") == null && rules.Trade(purse, "urn", 3, "bust") == null, "no rate, no trade");
        }

        public static void TradingThereAndBackLoses()
        {
            // DD1's rates are not fair both ways: the exchange is a convenience with a price.
            var rules = HeirloomExchangeRules.Load(new NoDd1Files());
            foreach (var there in rules.Rates)
            {
                var back = rules.Find(there.To, there.From);
                Check.True(back != null, "every rate has its way back: " + there);
                // n of From -> To -> From again, for an amount both rates divide
                var amount = there.FromAmount * back.FromAmount;
                var got = amount / there.FromAmount * there.ToAmount;
                var returned = got / back.FromAmount * back.ToAmount;
                Check.True(returned <= amount, "no profit from " + there + " and back: " + amount + " became " + returned);
            }
        }
    }
}
