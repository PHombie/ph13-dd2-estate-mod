using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Estate;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What an expedition came to, as DD1's two results screens tell it (docs/recon/dd1-raid-results.md): the
    /// outcome, the quest's rewards given or withheld, the bag card by card, and for every hero who set out
    /// what they come home with. DD1's order is kept: everything here has been applied to the estate (and
    /// saved) by the time the record is made, and the screens only read it. It is not saved itself: a game
    /// closed on the results screen has lost nothing.
    /// </summary>
    internal sealed class RaidResults
    {
        /// <summary>One row of the second page: a hero of the party as it set out.</summary>
        public sealed class Hero
        {
            public uint Guid;
            public string Name, ClassId;
            /// <summary>Died on the expedition: the row keeps name, portrait (under DD1's skull) and badge, and nothing else.</summary>
            public bool Dead;
            /// <summary>Resolve experience before the quest and after it (town events' bonus included).</summary>
            public int XpBefore, XpAfter;
            /// <summary>Stress the hero comes home with, in DD1's ten pips.</summary>
            public int Stress;
            public bool Diseased;
            /// <summary>Quirks and the disease gained at the quest's end: behind the masks until they are opened.</summary>
            public List<QuestAftermath.Gain> NewQuirks = new List<QuestAftermath.Gain>();

            public int LevelBefore => Resolve.LevelOf(XpBefore);
            public int LevelAfter => Resolve.LevelOf(XpAfter);
            public int Gained => XpAfter - XpBefore;
        }

        public RaidOutcome Outcome;
        /// <summary>The quest's name under the title, and DD1's id of the region (its picture stands behind the panel).</summary>
        public string QuestName, Dungeon;
        public List<QuestPayment> Rewards = new List<QuestPayment>();
        /// <summary>"Collected Treasure": the cards in the bag's order, each with the gold it adds to the total.</summary>
        public List<HaulCard> Treasure = new List<HaulCard>();
        /// <summary>The haul's gold: what the cards add up to, and what the estate was paid.</summary>
        public int TreasureGold;
        public List<HaulCard> Heirlooms = new List<HaulCard>();
        public List<Hero> Heroes = new List<Hero>();
        /// <summary>
        /// What the purse really got, for the quest and for the bag: the record this one is checked against
        /// ("Collected Treasure" counts up to what the haul paid). Null for a record made to be looked at.
        /// </summary>
        public ExpeditionBooks Books;

        /// <summary>The treasure row adds up to what the purse got for the bag, and the reward row's gold to what the quest paid.</summary>
        public bool AgreesWithThePurse
        {
            get
            {
                if (Books == null) return true;
                var rewardGold = Rewards.Where(r => r.Given && r.Kind == QuestPayment.Gold).Sum(r => r.Amount);
                return Books.Agrees && TreasureGold == Books.HaulGold && rewardGold == Books.QuestGold
                       && Treasure.Sum(card => card.Gold) == Books.HaulGold;
            }
        }

        public bool Completed => Outcome == RaidOutcome.Victory;

        /// <summary>How many of a kind of heirloom came home in the bag.</summary>
        public int HeirloomTotal(string kind) => Heirlooms.Where(card => card.Item.Id == kind).Sum(card => card.Amount);

        /// <summary>The record from what the bag settled to.</summary>
        public void SetHaul(RaidHaul haul)
        {
            Treasure = RaidResultRules.TreasureCards(haul);
            Heirlooms = RaidResultRules.HeirloomCards(haul);
            TreasureGold = haul != null ? haul.Gold : 0;
        }

        /// <summary>For the log and the dev bridge.</summary>
        public object Describe()
        {
            return new
            {
                outcome = Outcome.ToString(),
                quest = QuestName,
                dungeon = Dungeon,
                rewards = Rewards.Select(r => new { kind = r.Kind, id = r.Id, rarity = r.Rarity, amount = r.Amount, given = r.Given }).ToList(),
                treasureGold = TreasureGold,
                // what the purse got (null: a record made to be looked at) and whether the screen's rows add up to it
                books = Books?.Describe(),
                agreesWithThePurse = AgreesWithThePurse,
                treasure = Treasure.Select(c => new { item = c.Item.Key, amount = c.Amount, gold = c.Gold }).ToList(),
                heirlooms = Heirlooms.Select(c => new { item = c.Item.Key, amount = c.Amount }).ToList(),
                heroes = Heroes.Select(h => new
                {
                    guid = h.Guid, name = h.Name, cls = h.ClassId, dead = h.Dead, xpBefore = h.XpBefore, xpAfter = h.XpAfter, levelBefore = h.LevelBefore, levelAfter = h.LevelAfter,
                    stress = h.Stress, diseased = h.Diseased, quirks = h.NewQuirks.Select(q => new { kind = q.Kind.ToString(), id = q.Id, name = q.Name }).ToList()
                }).ToList()
            };
        }

        public override string ToString()
        {
            return Outcome + " (" + QuestName + ", " + Dungeon + "): " + Rewards.Count + " reward cards" + (Rewards.Any(r => r.Given) ? " given" : " withheld") + ", " + Treasure.Count + " treasure cards worth "
                   + TreasureGold + " gold, " + Heirlooms.Count + " heirloom stacks; "
                   + string.Join(", ", Heroes.Select(h => h.Name + (h.Dead ? " (dead)" : " +" + h.Gained + " xp, level " + h.LevelBefore + (h.LevelAfter != h.LevelBefore ? " to " + h.LevelAfter : "")
                                                                                         + (h.NewQuirks.Count > 0 ? ", " + string.Join(" / ", h.NewQuirks.Select(q => q.Name)) : ""))));
        }
    }
}
