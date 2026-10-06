using System;
using System.Collections.Generic;
using System.Linq;

namespace DD2Estate.Core
{
    /// <summary>
    /// DD1's heirloom exchange ("Trade Heirlooms"), read from the player's install
    /// (<c>campaign/heirloom_exchange/heirloom_exchange.json</c>): twelve rates, each saying how many heirlooms
    /// of one kind buy how many of another (3 busts for 1 portrait, 1 portrait for 3 crests). The player picks
    /// what to give and how many; every kind that can be had for it is offered at its own rate.
    ///
    /// How many may be given is the screen's own arithmetic in DD1 (no file states it). Here the amount steps
    /// through the numbers at least one of the kind's rates divides, so that the arrows never stop on an amount
    /// nothing can be had for; an offer whose rate does not divide the amount, or that the estate cannot pay,
    /// is shown but cannot be taken (DD1's "invalid" frame).
    /// </summary>
    public sealed class HeirloomExchangeRules
    {
        public const string File = "campaign/heirloom_exchange/heirloom_exchange.json";

        public sealed class Rate
        {
            public string From, To;
            public int FromAmount, ToAmount;

            public override string ToString() => FromAmount + " " + From + " -> " + ToAmount + " " + To;
        }

        public enum Refusal
        {
            None,
            /// <summary>The rate does not divide the amount (4 busts at 3 for 1).</summary>
            NotAMultiple,
            /// <summary>The estate holds fewer than the amount.</summary>
            CannotPay
        }

        /// <summary>What an amount of one kind comes to at one rate.</summary>
        public sealed class Offer
        {
            public Rate Rate;
            /// <summary>Heirlooms given.</summary>
            public int Gives;
            /// <summary>Heirlooms received: whole exchanges only.</summary>
            public int Takes;
            public Refusal Refusal;

            public bool Valid => Refusal == Refusal.None;
        }

        // FALLBACK, not read: DD1's stock rates, so that the exchange is still DD1's when the file is gone.
        private static readonly object[][] Stock =
        {
            new object[] { "bust", 3, "portrait", 1 }, new object[] { "bust", 3, "deed", 2 }, new object[] { "bust", 2, "crest", 3 },
            new object[] { "portrait", 2, "bust", 3 }, new object[] { "portrait", 2, "deed", 3 }, new object[] { "portrait", 1, "crest", 3 },
            new object[] { "deed", 3, "bust", 2 }, new object[] { "deed", 3, "portrait", 1 }, new object[] { "deed", 2, "crest", 3 },
            new object[] { "crest", 3, "bust", 1 }, new object[] { "crest", 6, "portrait", 1 }, new object[] { "crest", 3, "deed", 1 }
        };

        /// <summary>The rates in the file's order: DD1 lists what a kind buys in the order it shows the offers.</summary>
        public readonly List<Rate> Rates = new List<Rate>();

        /// <summary>True when the file could not be read and the stock rates stand in.</summary>
        public bool Missing;

        /// <param name="known">The heirlooms the estate keeps; a rate that names any other kind is left out (DD1's DLC adds shards). Null: every rate.</param>
        public static HeirloomExchangeRules Load(IDd1Files files, IEnumerable<string> known = null)
        {
            return Parse(files.ReadText(File), known);
        }

        public static HeirloomExchangeRules Parse(string json, IEnumerable<string> known = null)
        {
            var rules = new HeirloomExchangeRules();
            var kinds = known != null ? new HashSet<string>(known) : null;
            foreach (var entry in Json.Array(Json.ParseFile(json)?["exchange_rates"]))
            {
                var rate = new Rate
                {
                    From = (string)entry["exchange_from_type"], FromAmount = Json.Int(entry["exchange_from_amount"], 0),
                    To = (string)entry["exchange_to_type"], ToAmount = Json.Int(entry["exchange_to_amount"], 0)
                };
                rules.Add(rate, kinds);
            }
            if (rules.Rates.Count > 0) return rules;

            rules.Missing = true;
            foreach (var row in Stock)
                rules.Add(new Rate { From = (string)row[0], FromAmount = (int)row[1], To = (string)row[2], ToAmount = (int)row[3] }, kinds);
            return rules;
        }

        // A rate that gives or takes nothing, trades a kind for itself or repeats an earlier pair is not a rate.
        private void Add(Rate rate, HashSet<string> kinds)
        {
            if (string.IsNullOrEmpty(rate.From) || string.IsNullOrEmpty(rate.To) || rate.From == rate.To) return;
            if (rate.FromAmount <= 0 || rate.ToAmount <= 0) return;
            if (kinds != null && (!kinds.Contains(rate.From) || !kinds.Contains(rate.To))) return;
            if (Find(rate.From, rate.To) != null) return;
            Rates.Add(rate);
        }

        /// <summary>The kinds that can be given, in the order the file first names them (bust, portrait, deed, crest).</summary>
        public List<string> Kinds()
        {
            var kinds = new List<string>();
            foreach (var rate in Rates)
                if (!kinds.Contains(rate.From)) kinds.Add(rate.From);
            return kinds;
        }

        /// <summary>What one kind buys, in the file's order.</summary>
        public List<Rate> From(string kind) => Rates.Where(r => r.From == kind).ToList();

        public Rate Find(string from, string to) => Rates.FirstOrDefault(r => r.From == from && r.To == to);

        /// <summary>Whether something can be had for this many: at least one of the kind's rates divides the amount.</summary>
        public bool IsStep(string kind, int amount)
        {
            return amount > 0 && Rates.Any(r => r.From == kind && amount % r.FromAmount == 0);
        }

        /// <summary>The fewest of a kind anything can be had for; 0 for a kind that buys nothing.</summary>
        public int Smallest(string kind)
        {
            var rates = From(kind);
            return rates.Count > 0 ? rates.Min(r => r.FromAmount) : 0;
        }

        /// <summary>
        /// The most the arrows go up to: the largest amount the estate can pay that buys something, and the
        /// smallest step when it cannot pay even that (the rates stay on show, every offer refused).
        /// </summary>
        public int Largest(string kind, int held)
        {
            var smallest = Smallest(kind);
            if (smallest <= 0) return 0;
            for (var amount = held; amount > smallest; amount--)
                if (IsStep(kind, amount)) return amount;
            return smallest;
        }

        /// <summary>
        /// An amount brought back between <see cref="Smallest"/> and <see cref="Largest"/> (the estate has grown
        /// poorer, another kind was picked) and down onto a number something can be had for.
        /// </summary>
        public int Clamp(string kind, int amount, int held)
        {
            var smallest = Smallest(kind);
            if (smallest <= 0) return 0;
            amount = Math.Max(smallest, Math.Min(amount, Largest(kind, held)));
            while (amount > smallest && !IsStep(kind, amount)) amount--;
            return amount;
        }

        /// <summary>
        /// Where an arrow takes the amount: up (<paramref name="direction"/> above 0) or down to the next
        /// number something can be had for. An amount outside the range only comes back inside it.
        /// </summary>
        public int Step(string kind, int amount, int direction, int held)
        {
            var smallest = Smallest(kind);
            if (smallest <= 0) return 0;
            var inside = Clamp(kind, amount, held);
            if (inside != amount) return inside;
            var largest = Largest(kind, held);
            var step = direction > 0 ? 1 : -1;
            for (var next = amount + step; next >= smallest && next <= largest; next += step)
                if (IsStep(kind, next)) return next;
            return amount;
        }

        /// <summary>What <paramref name="amount"/> of a rate's kind comes to, for an estate holding <paramref name="held"/> of it.</summary>
        public static Offer Quote(Rate rate, int amount, int held)
        {
            var offer = new Offer { Rate = rate, Gives = amount, Takes = amount > 0 ? amount / rate.FromAmount * rate.ToAmount : 0 };
            if (amount <= 0 || amount % rate.FromAmount != 0) offer.Refusal = Refusal.NotAMultiple;
            else if (amount > held) offer.Refusal = Refusal.CannotPay;
            return offer;
        }

        /// <summary>Every offer for an amount of one kind, in the file's order.</summary>
        public List<Offer> Offers(string kind, int amount, int held)
        {
            return From(kind).Select(rate => Quote(rate, amount, held)).ToList();
        }

        /// <summary>
        /// Makes the exchange in a purse of heirloom counts. Returns the offer taken; nothing changes when it
        /// is refused, and null stands for a pair DD1 has no rate for.
        /// </summary>
        public Offer Trade(IDictionary<string, int> purse, string from, int amount, string to)
        {
            var rate = Find(from, to);
            if (rate == null || purse == null) return null;
            purse.TryGetValue(from, out var held);
            var offer = Quote(rate, amount, held);
            if (!offer.Valid) return offer;
            purse.TryGetValue(to, out var had);
            purse[from] = Math.Max(0, held - offer.Gives);
            purse[to] = had + offer.Takes;
            return offer;
        }
    }
}
