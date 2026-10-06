using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The rules of DD1's stress-relief activities (Tavern: bar, gambling, brothel; Abbey: meditation, prayer,
    /// flagellation), read from the player's DD1 install: campaign/town/buildings/&lt;id&gt;/&lt;id&gt;.building.json
    /// for costs, relief, slots and side effects, upgrades/building/&lt;id&gt;.upgrades.json for the order the
    /// upgrade codes are bought in, campaign/estate/estate.json for the price rise with hero level.
    /// Gold is DD1's own number; the only number of the mod's own is the stress scale.
    /// tools/preview_building_panel.py computes the same figures: change one, change the other.
    /// </summary>
    internal static class ActivityRules
    {
        /// <summary>DD1's stress scale. The DD2 end of the conversion is the hero's own StressMax.</summary>
        public const float Dd1StressMax = 200f;

        /// <summary>DD2's stress scale for text shown before a particular hero is known.</summary>
        public const float Dd2StressMax = 10f;

        public static readonly string[] BuildingIds = { "tavern", "abbey" };

        /// <summary>
        /// Resolve level of a hero (index into DD1's hero_activity_cost_multiplier_table). The estate has no
        /// hero levels yet; the system that adds them replaces this.
        /// </summary>
        public static Func<uint, int> HeroLevel = guid => 0;

        /// <summary>One entry of a side effect's data list; which fields are set depends on the effect type.</summary>
        public class Outcome
        {
            public float Weight;
            public int Weeks;           // go_missing
            public string Quirk;        // add_quirk: DD1 quirk id
            public string Currency;     // add_currency, remove_currency
            public int Amount;          // in DD1 units
        }

        public class SideEffect
        {
            public string Type;
            public float Weight;
            public readonly List<Outcome> Outcomes = new List<Outcome>();
        }

        // A value that an upgrade code replaces: element 0 of DD1's *_upgrades arrays has no code.
        internal class Tier
        {
            public string Code;
            public float Low, High;
        }

        public class Activity
        {
            public string Building;
            public string Id;
            public float SideEffectChance;
            public int MaxSlots;
            /// <summary>DD1's miscellaneous.caretaker_friendly: the Caretaker may spend a week here himself.</summary>
            public bool CaretakerFriendly;
            public readonly List<SideEffect> SideEffects = new List<SideEffect>();
            /// <summary>DD1 quirk ids whose owners will not use this activity.</summary>
            public readonly List<string> RefusedByQuirks = new List<string>();

            internal readonly List<Tier> Costs = new List<Tier>();
            internal readonly List<Tier> Slots = new List<Tier>();
            internal readonly List<Tier> Relief = new List<Tier>();
            /// <summary>Upgrade codes of the activity's tree in the order they are bought.</summary>
            internal readonly List<string> Codes = new List<string>();

            /// <summary>DD1's upgrade tree of the activity, e.g. "tavern.bar".</summary>
            public string Tree => Building + "." + Id;
        }

        private static readonly Dictionary<string, List<Activity>> Loaded = new Dictionary<string, List<Activity>>();
        private static readonly JArray Empty = new JArray();
        private static List<float> _costMultipliers;

        /// <summary>The activities of a building in DD1's order; empty if they cannot be read from DD1.</summary>
        public static IReadOnlyList<Activity> For(string building)
        {
            if (Loaded.TryGetValue(building, out var list)) return list;
            list = new List<Activity>();
            try { Load(building, list); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Activities of the " + building + " could not be read from DD1: " + e.Message);
                list.Clear();
            }
            Loaded[building] = list;
            return list;
        }

        public static Activity Find(string building, string id)
        {
            foreach (var activity in For(building))
                if (activity.Id == id) return activity;
            return null;
        }

        // ---- values at the current upgrade level ---------------------------------------------------------

        /// <summary>
        /// Upgrade codes bought in the activity's tree ("tavern.bar"), as the upgrade screen keeps them
        /// (<see cref="UpgradeRules"/>); a number stored under the bare building id counts for all its trees.
        /// </summary>
        public static int Level(Activity activity)
        {
            return UpgradeRules.Level(activity.Tree);
        }

        public static int Slots(Activity activity)
        {
            if (TownEventHooks.ActivityClosed(activity.Id)) return 0;      // DD1 activity_lock: closed for the week
            var tier = Pick(activity.Slots, activity);
            return tier != null ? Math.Max(0, (int)tier.Low) : 0;
        }

        /// <summary>Gold a hero of this resolve level pays.</summary>
        public static int Cost(Activity activity, int heroLevel)
        {
            var tier = Pick(activity.Costs, activity);
            if (tier == null || tier.Low <= 0f) return 0;
            // A town event may make the week's visits cheaper or free.
            return TownEventHooks.ActivityPrice(activity.Id, Math.Max(1, WholeGold(tier.Low * CostMultiplier(heroLevel))));
        }

        public static int CostFor(Activity activity, uint heroGuid)
        {
            return Cost(activity, HeroLevel(heroGuid));
        }

        /// <summary>A price that a multiplier or a discount has left with a fraction, in whole gold: halves rounded up.</summary>
        public static int WholeGold(float gold)
        {
            return (int)Math.Floor(gold + 0.5f);
        }

        /// <summary>Stress relief on a DD2 scale of <paramref name="stressMax"/>; usually not a whole number.</summary>
        public static void Relief(Activity activity, float stressMax, out float low, out float high)
        {
            var tier = Pick(activity.Relief, activity);
            low = tier != null ? tier.Low * stressMax / Dd1StressMax : 0f;
            high = tier != null ? Math.Max(low, tier.High * stressMax / Dd1StressMax) : 0f;
        }

        /// <summary>"-2 to -3": the whole numbers a roll can land on.</summary>
        public static string ReliefText(Activity activity)
        {
            Relief(activity, Dd2StressMax, out var low, out var high);
            var from = (int)Math.Floor(low);
            var to = (int)Math.Ceiling(high);
            return from == to ? "-" + from : "-" + from + " to -" + to;
        }

        /// <summary>
        /// DD2 stress moves in whole points, DD1's relief does not scale to whole points (45 of 200 is 2.25 of
        /// 10). The fraction is the chance of one more point, so the average stays DD1's.
        /// </summary>
        public static int RollRelief(Activity activity, float stressMax, Random random)
        {
            Relief(activity, stressMax, out var low, out var high);
            var value = low + (high - low) * random.NextDouble();
            var whole = Math.Floor(value);
            return (int)whole + (random.NextDouble() < value - whole ? 1 : 0);
        }

        /// <summary>
        /// DD1's side-effect roll: the activity's chance decides whether anything happens, then one result is
        /// drawn by weight and one entry of its data by weight. Null when nothing happens.
        /// </summary>
        public static SideEffect RollSideEffect(Activity activity, Random random, out Outcome outcome)
        {
            outcome = null;
            if (random.NextDouble() >= activity.SideEffectChance) return null;
            var effect = Draw(activity.SideEffects, e => e.Weight, random);
            if (effect != null) outcome = Draw(effect.Outcomes, o => o.Weight, random);
            return effect;
        }

        private static T Draw<T>(List<T> items, Func<T, float> weight, Random random) where T : class
        {
            var total = 0f;
            foreach (var item in items) total += Math.Max(0f, weight(item));
            if (total <= 0f) return null;
            var roll = random.NextDouble() * total;
            T last = null;
            foreach (var item in items)
            {
                var w = Math.Max(0f, weight(item));
                if (w <= 0f) continue;
                last = item;
                if (roll < w) return item;
                roll -= w;
            }
            return last;
        }

        // The last tier that needs no upgrade or whose code is among the first `level` codes of the tree.
        private static Tier Pick(List<Tier> tiers, Activity activity)
        {
            var level = Level(activity);
            Tier best = null;
            foreach (var tier in tiers)
            {
                var at = tier.Code == null ? -1 : activity.Codes.IndexOf(tier.Code);
                if (tier.Code == null || (at >= 0 && at < level)) best = tier;
            }
            return best;
        }

        private static float CostMultiplier(int heroLevel)
        {
            if (_costMultipliers == null)
            {
                _costMultipliers = new List<float>();
                try
                {
                    var text = Dd1Install.ReadText("campaign/estate/estate.json");
                    if (text != null && JObject.Parse(text)["hero_activity_cost_multiplier_table"] is JArray table)
                        foreach (var value in table) _costMultipliers.Add((float)value);
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 estate.json could not be read: " + e.Message); }
            }
            if (_costMultipliers.Count == 0) return 1f;
            return _costMultipliers[Math.Max(0, Math.Min(heroLevel, _costMultipliers.Count - 1))];
        }

        // ---- reading DD1 ---------------------------------------------------------------------------------

        private static void Load(string building, List<Activity> into)
        {
            var file = "campaign/town/buildings/" + building + "/" + building + ".building.json";
            var text = Dd1Install.ReadText(file);
            if (text == null)
            {
                Plugin.Log.LogWarning("DD1 file missing: " + file);
                return;
            }
            var trees = UpgradeTrees(building);
            foreach (var entry in JObject.Parse(text).SelectToken("data.activities") as JArray ?? Empty)
            {
                var id = (string)entry["id"];
                if (id == null || !(entry["data"] is JObject data)) continue;
                var activity = new Activity { Building = building, Id = id };
                activity.CaretakerFriendly = (bool?)data.SelectToken("miscellaneous.caretaker_friendly") ?? false;

                var side = data["side_effects"] as JObject;
                activity.SideEffectChance = (float?)side?["chance"] ?? 0f;
                foreach (var result in side?["results"] as JArray ?? Empty)
                {
                    var effect = new SideEffect { Type = (string)result["type"], Weight = (float?)result["chance"] ?? 0f };
                    foreach (var option in result["data"] as JArray ?? Empty)
                    {
                        effect.Outcomes.Add(new Outcome
                        {
                            Weight = (float?)option["chance"] ?? 1f,
                            Weeks = (int?)option["duration"] ?? 0,
                            Quirk = (string)option["quirk_library_name"],
                            Currency = (string)option["type"],
                            Amount = (int?)option["amount"] ?? 0
                        });
                    }
                    activity.SideEffects.Add(effect);
                }

                foreach (var requirement in data["requirements"] as JArray ?? Empty)
                {
                    if ((string)requirement["type"] != "not_have_quirks") continue;
                    foreach (var quirk in requirement.SelectToken("data.quirk_library_names") as JArray ?? Empty)
                        activity.RefusedByQuirks.Add((string)quirk);
                }

                // All six activities are paid in gold; any other currency would need its own purse here.
                Tiers(data["cost_upgrades"], activity.Costs, "cost_currency.amount", "cost_currency.amount");
                Tiers(data["slot_upgrades"], activity.Slots, "number_of_slots", "number_of_slots");
                Tiers(data["stress_upgrades"], activity.Relief, "heal_low", "heal_high");
                foreach (var tier in activity.Slots) activity.MaxSlots = Math.Max(activity.MaxSlots, (int)tier.Low);

                if (trees.TryGetValue(activity.Tree, out var codes)) activity.Codes.AddRange(codes);
                else AlphabeticalCodes(activity);
                into.Add(activity);
            }
        }

        private static void Tiers(JToken array, List<Tier> into, string lowPath, string highPath)
        {
            foreach (var entry in array as JArray ?? Empty)
            {
                var low = (float?)entry.SelectToken(lowPath);
                if (low == null) continue;
                into.Add(new Tier
                {
                    Code = (string)entry["upgrade_requirement_code"],
                    Low = low.Value,
                    High = (float?)entry.SelectToken(highPath) ?? low.Value
                });
            }
        }

        // Tree id -> its requirement codes in file order (DD1's trees are linear: each code needs the one before).
        private static Dictionary<string, List<string>> UpgradeTrees(string building)
        {
            var trees = new Dictionary<string, List<string>>();
            var text = Dd1Install.ReadText("upgrades/building/" + building + ".upgrades.json");
            if (text == null) return trees;
            foreach (var tree in JObject.Parse(text)["trees"] as JArray ?? Empty)
            {
                var id = (string)tree["id"];
                if (id == null) continue;
                var codes = new List<string>();
                foreach (var requirement in tree["requirements"] as JArray ?? Empty)
                {
                    var code = (string)requirement["code"];
                    if (code != null) codes.Add(code);
                }
                trees[id] = codes;
            }
            return trees;
        }

        // Without the tree file the codes the activity itself mentions are taken in letter order (a, b, c ...).
        private static void AlphabeticalCodes(Activity activity)
        {
            foreach (var tiers in new[] { activity.Costs, activity.Slots, activity.Relief })
                foreach (var tier in tiers)
                    if (tier.Code != null && !activity.Codes.Contains(tier.Code)) activity.Codes.Add(tier.Code);
            activity.Codes.Sort(string.CompareOrdinal);
        }
    }
}
