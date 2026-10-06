using System;
using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>What DD1's results screen calls the way an expedition ended.</summary>
    public enum RaidOutcome { Victory, Escape, Defeat }

    /// <summary>What DD1's roll at the end of a quest gives one hero who came home.</summary>
    public sealed class AftermathRoll
    {
        public bool Negative, Positive, Disease;

        public int Count => (Negative ? 1 : 0) + (Positive ? 1 : 0) + (Disease ? 1 : 0);
    }

    /// <summary>One card of the two rows DD1's results screen lays the party's bag out in.</summary>
    public sealed class HaulCard
    {
        public ItemDef Item;
        public int Amount;
        /// <summary>DD1 gold the card adds to the treasure's total as it is counted (an heirloom: 0).</summary>
        public int Gold;

        public override string ToString() => Item + " x" + Amount + " (" + Gold + ")";
    }

    /// <summary>
    /// DD1's rules behind its two results screens (docs/recon/dd1-raid-results.md, section 4), read from the
    /// install's <c>shared/rules.json</c>: which outcome an expedition is called by, and the quirk and disease
    /// a hero may come home with. The initial values of the fields are DD1's stock values, FALLBACKS for a
    /// missing file or key. What the screens show of the bag is worked out here too, from the haul
    /// <see cref="Inventory.Settle"/> returns.
    /// </summary>
    public sealed class RaidResultRules
    {
        public const string RulesFile = "shared/rules.json";

        /// <summary><c>min_hero_count_for_escape</c>: living heroes of the party a quest not completed needs to read "Escape".</summary>
        public int MinHeroesForEscape = 3;

        /// <summary>The chance of a negative and of a positive quirk at stress 0 and at stress 100, quest completed or not.</summary>
        public double NegStress0Success = 0.30, NegStress100Success = 0.50, PosStress0Success = 0.50, PosStress100Success = 0.40;
        public double NegStress0Failure = 0.40, NegStress100Failure = 0.70, PosStress0Failure = 0.40, PosStress100Failure = 0.25;

        /// <summary><c>disease_after_quest_min_resolve_level</c>, <c>disease_max_chance</c>, <c>disease_hero_disease_resist_weight</c>, <c>disease_after_quest_min_chance</c>.</summary>
        public int DiseaseMinResolveLevel = 2;
        public double DiseaseMaxChance = 0.32, DiseaseResistWeight = 0.33, DiseaseMinChance = 0.05;

        // DD1 behaviour, not in data (a default in the exe, questQuirksPerHeroLimit): new entries a hero a quest.
        // It is why the plate of the results screen holds three lines.
        public const int QuirksPerHeroLimit = 3;

        public static RaidResultRules Load(IDd1Files files)
        {
            var rules = new RaidResultRules();
            var raw = Json.ParseFile(files.ReadText(RulesFile));
            if (raw == null) return rules;
            rules.MinHeroesForEscape = Json.Int(raw["min_hero_count_for_escape"], rules.MinHeroesForEscape);
            rules.NegStress0Success = Json.Number(raw["negStress0Success"], rules.NegStress0Success);
            rules.NegStress100Success = Json.Number(raw["negStress100Success"], rules.NegStress100Success);
            rules.PosStress0Success = Json.Number(raw["posStress0Success"], rules.PosStress0Success);
            rules.PosStress100Success = Json.Number(raw["posStress100Success"], rules.PosStress100Success);
            rules.NegStress0Failure = Json.Number(raw["negStress0Failure"], rules.NegStress0Failure);
            rules.NegStress100Failure = Json.Number(raw["negStress100Failure"], rules.NegStress100Failure);
            rules.PosStress0Failure = Json.Number(raw["posStress0Failure"], rules.PosStress0Failure);
            rules.PosStress100Failure = Json.Number(raw["posStress100Failure"], rules.PosStress100Failure);
            rules.DiseaseMinResolveLevel = Json.Int(raw["disease_after_quest_min_resolve_level"], rules.DiseaseMinResolveLevel);
            rules.DiseaseMaxChance = Json.Number(raw["disease_max_chance"], rules.DiseaseMaxChance);
            rules.DiseaseResistWeight = Json.Number(raw["disease_hero_disease_resist_weight"], rules.DiseaseResistWeight);
            rules.DiseaseMinChance = Json.Number(raw["disease_after_quest_min_chance"], rules.DiseaseMinChance);
            return rules;
        }

        // ---- the outcome -----------------------------------------------------------------------------

        /// <summary>
        /// DD1 (RaidResultsDisplay::LoadAssets): the goal met when the party left is Victory. Otherwise the
        /// heroes of the party who are not dead are counted: enough of them is an Escape, fewer a Defeat, so
        /// an abandoned quest reads "Escape" only while three or four came home.
        /// </summary>
        public RaidOutcome Outcome(bool goalMet, int livingHeroes)
        {
            if (goalMet) return RaidOutcome.Victory;
            return livingHeroes >= MinHeroesForEscape ? RaidOutcome.Escape : RaidOutcome.Defeat;
        }

        // ---- quirks and diseases at the quest's end ----------------------------------------------------

        /// <summary>
        /// The chance of a new quirk of one kind: DD1 draws a line from its value at stress 0 to its value at
        /// stress 100 and reads it at the hero's stress. <paramref name="stress"/> is the share of that way,
        /// 0..1 (DD1's stress runs on to 200; beyond 100 the chance stays).
        /// </summary>
        public double QuirkChance(bool positive, bool completed, double stress)
        {
            var s = Math.Max(0, Math.Min(1, stress));
            double at0, at100;
            if (positive)
            {
                at0 = completed ? PosStress0Success : PosStress0Failure;
                at100 = completed ? PosStress100Success : PosStress100Failure;
            }
            else
            {
                at0 = completed ? NegStress0Success : NegStress0Failure;
                at100 = completed ? NegStress100Success : NegStress100Failure;
            }
            return at0 + (at100 - at0) * s;
        }

        /// <summary>
        /// The chance of a disease: none below the resolve level DD1 names; otherwise its highest chance less
        /// the hero's disease resistance (0..1) at the weight of the file, and never under its lowest.
        /// </summary>
        public double DiseaseChance(int resolveLevel, double diseaseResist)
        {
            if (resolveLevel < DiseaseMinResolveLevel) return 0;
            return Math.Max(DiseaseMaxChance - diseaseResist * DiseaseResistWeight, DiseaseMinChance);
        }

        /// <summary>
        /// DD1's roll for one living hero: a negative quirk and a positive one, each on its own, then the
        /// disease. Three draws of the dice whatever comes of them, so a seed tells the same story.
        /// </summary>
        public AftermathRoll Roll(bool completed, double stress, int resolveLevel, double diseaseResist, Rng rng)
        {
            var negative = rng.NextDouble();
            var positive = rng.NextDouble();
            var disease = rng.NextDouble();
            return new AftermathRoll
            {
                Negative = negative < QuirkChance(false, completed, stress),
                Positive = positive < QuirkChance(true, completed, stress),
                Disease = disease < DiseaseChance(resolveLevel, diseaseResist)
            };
        }

        // ---- resolve experience ------------------------------------------------------------------------

        /// <summary>
        /// Resolve experience a completed quest pays each survivor: the quest's own where it states one (DD1's
        /// plot quests do, <c>completion_reward.resolve_xp</c>: a boss 4, 8 or 16, a descent into the Darkest
        /// Dungeon 16), else the table's by length (<c>resolve_xp_table</c>, whose row of difficulty 6 is all
        /// zero: that row is never what a descent pays).
        /// </summary>
        public static int QuestExperience(int stated, IReadOnlyList<int> tableRow, int length)
        {
            if (stated > 0) return stated;
            if (tableRow == null || tableRow.Count == 0) return 0;
            return tableRow[Math.Min(Math.Max(length, 1), tableRow.Count - 1)];
        }

        /// <summary>The resolve level a sum of experience comes to (<c>resolve_level_thresholds</c>: the sum each level starts at).</summary>
        public static int Level(int experience, IReadOnlyList<int> thresholds, int maxLevel)
        {
            var level = 0;
            for (var i = 0; thresholds != null && i < thresholds.Count && i <= maxLevel; i++)
                if (experience >= thresholds[i]) level = i;
            return level;
        }

        /// <summary>How far a sum of experience is from its level's threshold to the next, 0..1; 1 at the top level.</summary>
        public static double LevelShare(int experience, IReadOnlyList<int> thresholds, int maxLevel)
        {
            var level = Level(experience, thresholds, maxLevel);
            if (thresholds == null || level >= maxLevel || level + 1 >= thresholds.Count) return 1;
            int floor = thresholds[level], ceiling = thresholds[level + 1];
            return ceiling > floor ? Math.Max(0, Math.Min(1, (experience - floor) / (double)(ceiling - floor))) : 1;
        }

        // ---- the bag on the screen -----------------------------------------------------------------------

        /// <summary>
        /// DD1's "Collected Treasure" row: every stack that came home and is gold or is worth gold, in the
        /// bag's order (heirlooms have a row of their own; what is worth nothing, firewood, is not shown). A
        /// stack DD1 unpacks (<see cref="ItemDef.CanUnpack"/>: food) is laid out a card a unit. The cards'
        /// gold adds up to the haul's.
        /// </summary>
        public static List<HaulCard> TreasureCards(RaidHaul haul)
        {
            var cards = new List<HaulCard>();
            if (haul == null) return cards;
            foreach (var stack in haul.Stacks)
            {
                var item = stack.Item;
                if (item.Type == ItemTypes.Heirloom || stack.Gold <= 0) continue;
                if (item.CanUnpack && stack.Amount > 1)
                {
                    var each = stack.Gold / stack.Amount;
                    for (var i = 0; i < stack.Amount; i++)
                        // the last card takes what a division left over, so the cards still add up
                        cards.Add(new HaulCard { Item = item, Amount = 1, Gold = i == stack.Amount - 1 ? stack.Gold - each * (stack.Amount - 1) : each });
                }
                else cards.Add(new HaulCard { Item = item, Amount = stack.Amount, Gold = stack.Gold });
            }
            return cards;
        }

        /// <summary>DD1's "Collected Heirlooms" row: the bag's heirloom stacks, a card a stack, in the bag's order.</summary>
        public static List<HaulCard> HeirloomCards(RaidHaul haul)
        {
            var cards = new List<HaulCard>();
            if (haul == null) return cards;
            foreach (var stack in haul.Stacks)
                if (stack.Item.Type == ItemTypes.Heirloom) cards.Add(new HaulCard { Item = stack.Item, Amount = stack.Amount });
            return cards;
        }

        /// <summary>
        /// DD1's shared inventory grid: where the slot of item <paramref name="index"/> out of
        /// <paramref name="count"/> stands, counted from the grid's start_pos. Rows of
        /// <paramref name="columns"/>; a centred grid moves a row that is not full right by half of what is
        /// missing. <paramref name="oneRow"/> is the mode the results screen sets on its two rows of the bag:
        /// every card in the first row, and more cards than columns move closer together so that the row
        /// keeps its width (twelve cards on six columns: half the pitch).
        /// </summary>
        public static void GridSlot(int index, int count, int columns, double pitchX, double pitchY, bool centred, bool oneRow, out double x, out double y)
        {
            columns = Math.Max(1, columns);
            count = Math.Max(count, index + 1);
            if (oneRow)
            {
                var pitch = count > columns ? pitchX * columns / count : pitchX;
                x = index * pitch + (centred && count < columns ? (columns - count) * pitchX / 2 : 0);
                y = 0;
                return;
            }
            int row = index / columns, column = index % columns;
            var inRow = Math.Min(columns, count - row * columns);
            x = column * pitchX + (centred ? (columns - inRow) * pitchX / 2 : 0);
            y = row * pitchY;
        }
    }
}
