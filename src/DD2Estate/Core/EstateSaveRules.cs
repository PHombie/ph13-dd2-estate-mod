using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>
    /// What an estate saved by an older build needs before it is read (the "estate_mod" section of the
    /// playthrough, as <c>EstateState.ToJson</c> writes it).
    ///
    /// Up to format 3 the estate counted gold in a scale of its own, one for fifty of DD1's: the purse had been
    /// stacks of DD2's own gold. From format 4 on every amount of gold is DD1's own number. An older save is
    /// brought over by multiplying every amount it holds: the purse, what the quests on the board (and the one
    /// the party is out on) pay, what was paid for a week in the Tavern, the Abbey and the Sanitarium (it is
    /// given back when the stay comes to nothing), the purse the Activity Log noted as the party set out, and
    /// the sums written out in words in the log and in the week's reports. A quest's pay was rounded to the
    /// old scale when it was rolled, so it comes back as a multiple of fifty; nothing else loses anything.
    /// The save itself is not touched: the migration is made on a copy, and the file changes with the next save.
    /// </summary>
    public static class EstateSaveRules
    {
        /// <summary>The first format that counts gold in DD1's numbers.</summary>
        public const int GoldInDd1Numbers = 4;

        /// <summary>DD1 gold for one gold of an estate saved before that.</summary>
        public const int OldGoldScale = 50;

        // "35 gold", "1,750 gold": a sum written out in a line of the log
        private static readonly Regex GoldInWords = new Regex(@"(?<![\w.,])(\d{1,3}(?:,\d{3})+|\d+) gold\b", RegexOptions.CultureInvariant);

        /// <summary>
        /// The section as the present build reads it. <paramref name="saved"/> is left as it is; what comes
        /// back is a copy when anything had to change. <paramref name="notes"/> gets a line for what was done.
        /// </summary>
        public static JObject BroughtOver(JObject saved, List<string> notes = null)
        {
            if (saved == null) return null;
            var format = Json.Int(saved["format"], 0);
            if (format >= GoldInDd1Numbers) return saved;
            var estate = (JObject)saved.DeepClone();
            var changed = GoldToDd1Numbers(estate);
            notes?.Add("gold of a format " + format + " save multiplied by " + OldGoldScale + " (" + changed + " amounts)");
            return estate;
        }

        /// <summary>Multiplies every amount of gold in an estate section of the old scale; returns how many were changed.</summary>
        public static int GoldToDd1Numbers(JObject estate)
        {
            var changed = 0;
            if (estate == null) return changed;
            changed += Scale(estate, "gold");
            var sections = estate["sections"] as JObject;
            if (sections == null) return changed;

            foreach (var offer in Json.Array(Part(sections, "quests")?["offers"])) changed += Scale(offer as JObject, "gold");
            changed += Scale(Part(sections, "expedition")?["quest"] as JObject, "gold");

            foreach (var key in new[] { "activities", "sanitarium" })
            {
                foreach (var stay in Json.Array(Part(sections, key)?["stays"])) changed += Scale(stay as JObject, "paid");
                // the week's reports: an activity's are {building, text}, the Sanitarium's bare lines
                var results = Part(sections, key)?["results"] as JArray;
                for (var i = 0; results != null && i < results.Count; i++)
                {
                    if (results[i] is JObject result) changed += Words(result, "text");
                    else if (results[i].Type == JTokenType.String)
                    {
                        var text = GoldWords((string)results[i]);
                        if (text == (string)results[i]) continue;
                        results[i] = text;
                        changed++;
                    }
                }
            }

            var log = Part(sections, "activity_log");
            changed += Scale(log?["expedition"] as JObject, "gold");
            foreach (var entry in Json.Array(log?["entries"])) changed += Words(entry as JObject, "t");
            return changed;
        }

        /// <summary>An amount of the old scale in DD1's numbers; a number that stands for "no amount" stays what it is.</summary>
        public static int Scaled(int amount)
        {
            if (amount == int.MaxValue || amount == int.MinValue) return amount;
            var scaled = (long)amount * OldGoldScale;
            return (int)Math.Max(int.MinValue + 1, Math.Min(int.MaxValue - 1, scaled));
        }

        /// <summary>A line with its sums of gold multiplied: "bought for 30 gold" becomes "bought for 1500 gold".</summary>
        public static string GoldWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return GoldInWords.Replace(text, match =>
            {
                var written = match.Groups[1].Value;
                if (!long.TryParse(written.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)) return match.Value;
                var scaled = amount * OldGoldScale;
                return scaled.ToString(written.IndexOf(',') >= 0 ? "N0" : "0", CultureInfo.InvariantCulture) + " gold";
            });
        }

        // A section of the save; null for one that is missing or of another shape (it is passed over, not tripped on).
        private static JObject Part(JObject sections, string key) => sections[key] as JObject;

        private static int Scale(JObject holder, string key)
        {
            var token = holder?[key];
            if (token == null || token.Type != JTokenType.Integer) return 0;
            holder[key] = Scaled(Json.Int(token, 0));
            return 1;
        }

        private static int Words(JObject holder, string key)
        {
            var token = holder?[key];
            if (token == null || token.Type != JTokenType.String) return 0;
            var text = GoldWords((string)token);
            if (text == (string)token) return 0;
            holder[key] = text;
            return 1;
        }
    }
}
