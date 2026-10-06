using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Estate;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What the end of an expedition paid the estate, the quest's pay and the bag apart: one record that the
    /// purse, DD1's results screen ("Quest Rewards", "Collected Treasure", "Collected Heirlooms") and the
    /// Activity Log's "Brought home" line are all read against, so that they cannot say different things.
    /// Since a fight's loot lies in the bag too, the bag is all the party brings: nothing reaches the estate
    /// in the middle of an expedition.
    /// </summary>
    internal sealed class ExpeditionBooks
    {
        /// <summary>The record of the expedition that ended last in this session; null before any has.</summary>
        public static ExpeditionBooks Last { get; private set; }

        public string Quest, Dungeon, Status;
        /// <summary>The week it ended in.</summary>
        public int Week;
        /// <summary>The purse as the end of the expedition began, and after the quest's pay and the haul.</summary>
        public int PurseBefore, PurseAfter;
        /// <summary>The gold the quest paid (its reward cards that were given).</summary>
        public int QuestGold;
        /// <summary>The bag's worth (coins, gems and leftover supplies at their sell value): what the purse got for it.</summary>
        public int HaulGold;
        public readonly Dictionary<string, int> QuestHeirlooms = new Dictionary<string, int>();
        public readonly Dictionary<string, int> HaulHeirlooms = new Dictionary<string, int>();
        /// <summary>DD2 trinket ids: the quest's reward, and those of the bag that came home.</summary>
        public readonly List<string> QuestTrinkets = new List<string>();
        public readonly List<string> HaulTrinkets = new List<string>();

        /// <summary>The purse moved by the quest's pay and the haul, and by nothing else.</summary>
        public bool Agrees => PurseAfter - PurseBefore == QuestGold + HaulGold;

        /// <summary>
        /// The record of an expedition's end, from what the quest gave (its cards) and what the bag settled to,
        /// with the purse before both and now.
        /// </summary>
        public static ExpeditionBooks Close(string quest, string dungeon, RaidStatus status, int purseBefore, IEnumerable<QuestPayment> rewards, RaidHaul haul, int haulGold)
        {
            var books = new ExpeditionBooks
            {
                Quest = quest, Dungeon = dungeon, Status = status.ToString(), Week = EstateState.Current.Week, PurseBefore = purseBefore, PurseAfter = EstateState.Gold,
                HaulGold = haulGold
            };
            foreach (var card in rewards ?? Enumerable.Empty<QuestPayment>())
            {
                if (card == null || !card.Given) continue;
                if (card.Kind == QuestPayment.Gold) books.QuestGold += card.Amount;
                else if (card.Kind == QuestPayment.Heirloom && card.Id != null) books.QuestHeirlooms[card.Id] = Have(books.QuestHeirlooms, card.Id) + card.Amount;
                else if (card.Kind == QuestPayment.Trinket && card.Id != null) books.QuestTrinkets.Add(card.Id);
            }
            if (haul != null)
            {
                foreach (var pair in haul.Heirlooms) books.HaulHeirlooms[pair.Key] = pair.Value;
                books.HaulTrinkets.AddRange(haul.Trinkets);
            }
            Last = books;
            if (!books.Agrees)
                Plugin.Log.LogWarning("Dungeon: the books do not agree: the purse went from " + purseBefore + " to " + books.PurseAfter + ", the quest paid " + books.QuestGold
                                      + " and the haul " + books.HaulGold);
            return books;
        }

        private static int Have(Dictionary<string, int> counts, string key) => counts.TryGetValue(key, out var n) ? n : 0;

        /// <summary>Gold in all, and every kind of heirloom in all.</summary>
        public int Gold => QuestGold + HaulGold;

        public int Heirloom(string kind) => Have(QuestHeirlooms, kind) + Have(HaulHeirlooms, kind);

        public IEnumerable<string> HeirloomKinds => QuestHeirlooms.Keys.Concat(HaulHeirlooms.Keys).Distinct();

        public object Describe()
        {
            return new
            {
                quest = Quest, dungeon = Dungeon, status = Status, week = Week,
                purseBefore = PurseBefore, purseAfter = PurseAfter, questGold = QuestGold, haulGold = HaulGold, agrees = Agrees,
                questHeirlooms = QuestHeirlooms, haulHeirlooms = HaulHeirlooms, questTrinkets = QuestTrinkets, haulTrinkets = HaulTrinkets
            };
        }
    }
}
