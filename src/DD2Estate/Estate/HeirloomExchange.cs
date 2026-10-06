using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's "Trade Heirlooms" for the estate's four heirlooms. The rates are DD1's, read from the install
    /// (<see cref="HeirloomExchangeRules"/>); the heirlooms are the estate's own counts
    /// (<see cref="EstateState.Heirloom"/>). A trade is made at once and saved.
    /// </summary>
    [EstateModule]
    internal static class HeirloomExchange
    {
        public const string Id = "heirloom_exchange";
        private const string IconDir = "campaign/town/heirloom_exchange/";

        private static HeirloomExchangeRules _rules;

        /// <summary>Raised after a trade, with what was given and taken.</summary>
        public static event Action<HeirloomExchangeRules.Offer> Traded;

        private static void Register()
        {
            // DD1 opens the exchange from the estate's bar, with the icon after the heirloom counts; the icon
            // goes grey while the exchange is up.
            TownPanelButtons.Add(new TownPanelButtons.Entry
            {
                Id = Id, Art = IconDir + "he_icon_idle.png", OpenArt = IconDir + "he_icon_selected.png", Size = new UnityEngine.Vector2(58f, 59f), Place = -1,
                Name = () => ScreenName, Toggle = HeirloomExchangePanel.Toggle, IsOpen = () => HeirloomExchangePanel.IsOpen
            });
        }

        /// <summary>DD1's rates between the heirlooms the estate keeps (its DLC's shards are not among them).</summary>
        public static HeirloomExchangeRules Rules
        {
            get
            {
                if (_rules != null) return _rules;
                _rules = HeirloomExchangeRules.Load(new Dd1Files(), EstateState.HeirloomIds);
                if (_rules.Missing) Plugin.Log.LogWarning("Heirloom exchange: " + HeirloomExchangeRules.File + " not readable, DD1's stock rates in use");
                else Plugin.Log.LogInfo("Heirloom exchange (DD1): " + _rules.Rates.Count + " rates");
                return _rules;
            }
        }

        public static string ScreenName => WindowText.Plain("town_name_heirloom_exchange") ?? "Trade Heirlooms";

        public static int Held(string kind) => EstateState.Current.Heirloom(kind);

        /// <summary>DD1's name of a heirloom ("Bust"), in the plural for any number but one.</summary>
        public static string Name(string kind, int amount = 1)
        {
            var name = WindowText.Plain("str_inventory_title_heirloom" + kind);
            if (string.IsNullOrEmpty(name)) name = string.IsNullOrEmpty(kind) ? "" : char.ToUpperInvariant(kind[0]) + kind.Substring(1);
            return amount == 1 ? name : name + "s";
        }

        /// <summary>"6 Busts".</summary>
        public static string Counted(string kind, int amount) => amount + " " + Name(kind, amount);

        /// <summary>Why an offer cannot be taken, in words; null for one that can.</summary>
        public static string Reason(HeirloomExchangeRules.Offer offer)
        {
            if (offer == null) return "DD1 has no rate for that";
            switch (offer.Refusal)
            {
                case HeirloomExchangeRules.Refusal.CannotPay:
                    return "The estate holds " + Counted(offer.Rate.From, Held(offer.Rate.From));
                case HeirloomExchangeRules.Refusal.NotAMultiple:
                    return Counted(offer.Rate.To, offer.Rate.ToAmount) + " cost " + Counted(offer.Rate.From, offer.Rate.FromAmount) + ": " + offer.Gives + " does not divide";
                default:
                    return null;
            }
        }

        /// <summary>What an amount of one kind buys of another right now; null for a pair without a rate.</summary>
        public static HeirloomExchangeRules.Offer Quote(string from, int amount, string to)
        {
            var rate = Rules.Find(from, to);
            return rate != null ? HeirloomExchangeRules.Quote(rate, amount, Held(from)) : null;
        }

        /// <summary>Gives <paramref name="amount"/> of one heirloom for another at DD1's rate. Returns null, or why nothing was traded.</summary>
        public static string Trade(string from, int amount, string to)
        {
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            var purse = new Dictionary<string, int>();
            foreach (var kind in EstateState.HeirloomIds) purse[kind] = Held(kind);
            var offer = Rules.Trade(purse, from, amount, to);
            var reason = Reason(offer);
            if (reason != null) return reason;

            var estate = EstateState.Current;
            estate.AddHeirloom(from, -offer.Gives);
            estate.AddHeirloom(to, offer.Takes);
            Plugin.Log.LogInfo("Heirloom exchange: " + offer.Gives + " " + from + " for " + offer.Takes + " " + to);
            try { Traded?.Invoke(offer); }
            catch (Exception e) { Plugin.Log.LogError("Heirloom exchange: a Traded handler failed: " + e); }
            EstatePersistence.SaveNow("heirlooms");
            return null;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe()
        {
            var rules = Rules;
            var held = new Dictionary<string, int>();
            foreach (var kind in EstateState.HeirloomIds) held[kind] = Held(kind);
            var kinds = new List<object>();
            foreach (var kind in rules.Kinds())
            {
                var rates = new List<object>();
                foreach (var rate in rules.From(kind)) rates.Add(new { to = rate.To, give = rate.FromAmount, take = rate.ToAmount });
                kinds.Add(new { kind, name = Name(kind), held = Held(kind), smallest = rules.Smallest(kind), largest = rules.Largest(kind, Held(kind)), rates });
            }
            return new { stockRates = rules.Missing, held, kinds };
        }
    }
}
