using System.Collections.Generic;
using DD2Estate.Core;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    /// <summary>An estate saved in the old scale of gold (one for fifty of DD1's) read by a build that counts in DD1's numbers (Core/EstateSaveRules.cs).</summary>
    public static class EstateSaveTests
    {
        // an estate of format 3 as EstateState.ToJson wrote it: the party out on a quest, a hero in the Tavern, one in the Sanitarium
        private static JObject Old(int format = 3)
        {
            return JObject.Parse(@"{
                'format': " + format + @", 'week': 7, 'gold': 1320,
                'heirlooms': {'crest': 12, 'deed': 3}, 'buildings': {'guild.cost': 2},
                'sections': {
                    'quests': {'week': 7, 'finished': 5, 'xp': {'crypts': 9}, 'offers': [
                        {'id': 'gen_7_1', 'gold': 60, 'heirlooms': {'crest': 4}, 'trinkets': [{'rarity': 'common', 'item': 'x'}]},
                        {'id': 'gen_7_2', 'gold': 125},
                        {'id': 'no_pay', 'gold': 0}]},
                    'expedition': {'quest': {'id': 'gen_7_1', 'gold': 60}, 'bag': [{'item': 'gold', 'amount': 750}]},
                    'activities': {'stays': [{'guid': 3, 'building': 'tavern', 'activity': 'bar', 'slot': 0, 'paid': 20, 'locked': false}],
                                   'results_week': 7, 'results': [{'building': 'tavern', 'text': 'Reynauld: -3 stress. Cost the estate another 4 gold.'}]},
                    'sanitarium': {'stays': [{'guid': 4, 'ward': 'treatment', 'slot': 1, 'quirk': 'q', 'treatment': 'Remove', 'paid': 15}],
                                   'results_week': 7, 'results': ['Dismas: there was nothing left to treat. 15 gold returned.']},
                    'activity_log': {'entries': [
                        {'w': 6, 'k': 'note', 't': 'The Nomad Wagon: <color=#fff>Sun Ring</color> bought for 150 gold.'},
                        {'w': 6, 'k': 'note', 't': 'Brought home: <color=#0f0>1,320 gold</color>, 2 crests.'},
                        {'w': 6, 'k': 'note', 't': 'A week of 3 golden days and 12 heroes.'}],
                        'expedition': {'seed': 5, 'gold': 1320, 'heirlooms': {'crest': 12}}},
                    'trinkets': {'stored': ['a', 'b'], 'grades': {'a': 'rare'}},
                    'blacksmith': {'3': {'w': 2, 'a': 1, 'hp': 30.0}}
                }}");
        }

        public static void AnOldSavesGoldIsMultiplied()
        {
            var saved = Old();
            var notes = new List<string>();
            var estate = EstateSaveRules.BroughtOver(saved, notes);
            Check.True(!ReferenceEquals(saved, estate), "the save itself is left alone: a copy comes back");
            Check.Equal(1320, (int)saved["gold"], "the file's own number");
            Check.Equal(1320 * 50, (int)estate["gold"], "the purse");
            Check.Equal(1, notes.Count, "one line on what was done");

            var offers = (JArray)estate["sections"]["quests"]["offers"];
            Check.True((int)offers[0]["gold"] == 3000 && (int)offers[1]["gold"] == 6250 && (int)offers[2]["gold"] == 0, "what the board's quests pay");
            Check.Equal(4, (int)offers[0]["heirlooms"]["crest"], "heirlooms are what they were");
            Check.Equal(3000, (int)estate["sections"]["expedition"]["quest"]["gold"], "the quest the party is out on");
            Check.Equal(750, (int)estate["sections"]["expedition"]["bag"][0]["amount"], "the bag's gold was DD1's already");
            Check.Equal(1000, (int)estate["sections"]["activities"]["stays"][0]["paid"], "a week at the bar, to be given back if it comes to nothing");
            Check.Equal(750, (int)estate["sections"]["sanitarium"]["stays"][0]["paid"], "a treatment likewise");
            Check.Equal(1320 * 50, (int)estate["sections"]["activity_log"]["expedition"]["gold"], "the purse the log noted as the party set out");

            // everything that is not gold
            Check.True((int)estate["week"] == 7 && (int)estate["heirlooms"]["crest"] == 12 && (int)estate["buildings"]["guild.cost"] == 2, "week, heirlooms, buildings");
            Check.True((int)estate["sections"]["quests"]["finished"] == 5 && (int)estate["sections"]["quests"]["xp"]["crypts"] == 9, "the board's counts");
            Check.True((int)estate["sections"]["activity_log"]["expedition"]["heirlooms"]["crest"] == 12 && (int)estate["sections"]["activities"]["stays"][0]["slot"] == 0
                       && (int)estate["sections"]["blacksmith"]["3"]["w"] == 2, "nothing else is touched");
        }

        public static void SumsWrittenInWordsFollow()
        {
            var estate = EstateSaveRules.BroughtOver(Old());
            var entries = (JArray)estate["sections"]["activity_log"]["entries"];
            Check.Equal("The Nomad Wagon: <color=#fff>Sun Ring</color> bought for 7500 gold.", (string)entries[0]["t"], "a price in the log");
            Check.Equal("Brought home: <color=#0f0>66,000 gold</color>, 2 crests.", (string)entries[1]["t"], "a sum with its comma keeps the comma");
            Check.Equal("A week of 3 golden days and 12 heroes.", (string)entries[2]["t"], "a number that is not gold is left");
            Check.Equal("Reynauld: -3 stress. Cost the estate another 200 gold.", (string)estate["sections"]["activities"]["results"][0]["text"], "the Tavern's report of the week");
            Check.Equal("Dismas: there was nothing left to treat. 750 gold returned.", (string)estate["sections"]["sanitarium"]["results"][0], "the Sanitarium's");

            Check.Equal("+100 gold of interest", EstateSaveRules.GoldWords("+2 gold of interest"), "a sign before the sum");
            Check.Equal("lost 500 gold, gained 50 gold.", EstateSaveRules.GoldWords("lost 10 gold, gained 1 gold."), "two sums in a line");
            Check.Equal("a ring of gold", EstateSaveRules.GoldWords("a ring of gold"), "no number, no change");
            Check.Equal("page 1.5 gold", EstateSaveRules.GoldWords("page 1.5 gold"), "a fraction's tail is not a sum");
            Check.True(EstateSaveRules.GoldWords(null) == null && EstateSaveRules.GoldWords("") == "", "nothing in, nothing out");
        }

        public static void EveryOlderFormatIsBroughtOverAndThePresentOneIsNot()
        {
            foreach (var format in new[] { 0, 1, 2, 3 })
                Check.Equal(66000, (int)EstateSaveRules.BroughtOver(Old(format))["gold"], "format " + format);
            var present = Old(EstateSaveRules.GoldInDd1Numbers);
            var notes = new List<string>();
            Check.True(ReferenceEquals(present, EstateSaveRules.BroughtOver(present, notes)) && notes.Count == 0, "a save of the present format is read as it is");
            Check.Equal(1320, (int)present["gold"], "and its purse is its purse");
            Check.Equal(1320, (int)EstateSaveRules.BroughtOver(Old(EstateSaveRules.GoldInDd1Numbers + 1))["gold"], "a later one too");

            // a save from before the format was written, and before the purse was a number: it has neither
            var first = JObject.Parse("{'week': 2, 'sections': {}}");
            var brought = EstateSaveRules.BroughtOver(first);
            Check.True(brought["gold"] == null && (int)brought["week"] == 2, "no purse in the file: none is made up (the gold lies in DD2's inventory and is swept from there)");
            Check.True(EstateSaveRules.BroughtOver(null) == null, "no save at all");
            Check.Equal(0, EstateSaveRules.GoldToDd1Numbers(JObject.Parse("{'sections': {'quests': {'offers': 'broken'}, 'activities': 7, 'activity_log': {'entries': [1, null, {'t': 5}]}}}")), "a section of another shape is passed over");
        }

        public static void TwiceIsNotFiftyTimesFifty()
        {
            // the copy is what is read; the saved section is read again unchanged if the estate is loaded twice in a session
            var saved = Old();
            Check.Equal(66000, (int)EstateSaveRules.BroughtOver(saved)["gold"], "once");
            Check.Equal(66000, (int)EstateSaveRules.BroughtOver(saved)["gold"], "and once again from the same section");
        }

        /// <summary>
        /// A real estate off the disk, when one is named: ESTATE_SAVE=&lt;a snapshot's kingdom.json&gt;. Prints what
        /// bringing it over does and holds the result against the file. Skipped otherwise.
        /// </summary>
        public static void AnEstateOnDiskIsBroughtOver()
        {
            var path = System.Environment.GetEnvironmentVariable("ESTATE_SAVE");
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) throw new SkipException("no ESTATE_SAVE named");
            var saved = JObject.Parse(System.IO.File.ReadAllText(path).TrimStart((char)0xFEFF))["estate_mod"] as JObject;
            Check.True(saved != null, "the file has an estate_mod section");
            var format = (int?)saved["format"] ?? 0;
            var notes = new List<string>();
            var estate = EstateSaveRules.BroughtOver(saved, notes);
            System.Console.WriteLine("      format " + format + ", week " + saved["week"] + ": purse " + saved["gold"] + " -> " + estate["gold"] + "; " + string.Join("; ", notes));
            var offersBefore = saved["sections"]?["quests"]?["offers"] as JArray ?? new JArray();
            var offersAfter = estate["sections"]?["quests"]?["offers"] as JArray ?? new JArray();
            for (var i = 0; i < offersBefore.Count; i++)
                System.Console.WriteLine("      offer " + offersBefore[i]["id"] + ": " + offersBefore[i]["gold"] + " -> " + offersAfter[i]["gold"]);
            var before = saved["sections"]?["activity_log"]?["entries"] as JArray ?? new JArray();
            var after = estate["sections"]?["activity_log"]?["entries"] as JArray ?? new JArray();
            for (var i = 0; i < before.Count; i++)
                if ((string)before[i]["t"] != (string)after[i]["t"]) System.Console.WriteLine("      log: " + before[i]["t"] + "\n        -> " + after[i]["t"]);
            var old = format < EstateSaveRules.GoldInDd1Numbers;
            if (saved["gold"] != null) Check.Equal((long)saved["gold"] * (old ? EstateSaveRules.OldGoldScale : 1), (long)estate["gold"], "the purse");
            for (var i = 0; i < offersBefore.Count; i++)
                Check.Equal((long)offersBefore[i]["gold"] * (old ? EstateSaveRules.OldGoldScale : 1), (long)offersAfter[i]["gold"], "offer " + offersBefore[i]["id"]);
            Check.Equal(saved["heirlooms"]?.ToString(), estate["heirlooms"]?.ToString(), "the heirlooms");
            Check.Equal(saved["sections"]?["trinkets"]?.ToString(), estate["sections"]?["trinkets"]?.ToString(), "the trinkets");
            Check.Equal(saved["sections"]?["expedition"]?["bag"]?.ToString(), estate["sections"]?["expedition"]?["bag"]?.ToString(), "the expedition's bag");
        }

        public static void ANumberThatStandsForNoAmountStays()
        {
            Check.Equal(int.MaxValue, EstateSaveRules.Scaled(int.MaxValue), "the log's mark for 'no purse noted'");
            Check.Equal(3000, EstateSaveRules.Scaled(60), "sixty of the old scale is DD1's three thousand");
            Check.Equal(0, EstateSaveRules.Scaled(0), "nothing stays nothing");
            Check.True(EstateSaveRules.Scaled(int.MaxValue / 10) < int.MaxValue && EstateSaveRules.Scaled(int.MaxValue / 10) > 0, "a purse too large to multiply is as large as a purse gets, not a negative one");
        }
    }
}
