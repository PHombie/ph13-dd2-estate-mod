using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's hamlet progression: the upgrade trees of the buildings, paid with heirlooms. Everything is read
    /// from the player's DD1 install: upgrades/building/&lt;id&gt;.upgrades.json gives each tree's steps, their
    /// price and what they need first; every `*_upgrades` list in
    /// campaign/town/buildings/&lt;id&gt;/&lt;id&gt;.building.json says what a step changes (a track of values, one
    /// per level). The mod has no numbers of its own here. A system of the mod may add a tree DD1 does not have
    /// (<see cref="Define"/>, e.g. the stage coach's hero paths); it prices its steps from DD1's trees.
    ///
    /// What has been built is kept in <see cref="EstateState.Buildings"/> under the tree's DD1 id
    /// ("tavern.bar", "stage_coach.rostersize", "guild.cost" ...): the number of steps bought, in file order.
    /// tools/preview_upgrades.py reads the same files the same way: change one, change the other.
    /// </summary>
    internal static class UpgradeRules
    {
        /// <summary>The buildings that have trees, in DD1's own order (HamletScene.BuildingIds).</summary>
        public static readonly string[] BuildingIds =
        {
            "stage_coach", "blacksmith", "guild", "camping_trainer", "tavern", "abbey", "sanitarium", "nomad_wagon"
        };

        public const string Gold = "gold";

        public class Cost
        {
            public string Currency;     // an heirloom id, or "gold" (DD1 gold, scaled like every other price)
            public int Amount;
        }

        /// <summary>A step of another tree that has to be built first.</summary>
        public class Need
        {
            public string Tree;
            public string Code;
        }

        public class Step
        {
            public string Code;
            public readonly List<Cost> Costs = new List<Cost>();
            /// <summary>Steps of other trees; the step before it in its own tree is always needed.</summary>
            public readonly List<Need> Needs = new List<Need>();
        }

        /// <summary>One value of the building that the tree changes, e.g. the slots of an activity.</summary>
        public class Track
        {
            /// <summary>Name of DD1's list, e.g. "slot_upgrades", "roster_size_upgrades".</summary>
            public string Kind;
            /// <summary>Activity or store the list belongs to; null for the building itself.</summary>
            public string Subject;
            /// <summary>Discounts add up; every other list replaces the value.</summary>
            public bool Additive;
            /// <summary>The value with 0, 1, 2 ... steps built.</summary>
            public float[] Values;
            /// <summary>DD1's entry in force at each level (the last one added, for discounts); may be null.</summary>
            public JObject[] Entries;
        }

        public class Tree
        {
            public string Id;
            public string Building;
            public readonly List<Step> Steps = new List<Step>();
            public readonly List<Track> Tracks = new List<Track>();

            public int IndexOf(string code)
            {
                for (var i = 0; i < Steps.Count; i++)
                    if (Steps[i].Code == code) return i;
                return -1;
            }
        }

        /// <summary>Raised when a step has just been built and paid for, with its tree.</summary>
        public static event Action<Tree> Built;

        /// <summary>Raised when levels may differ from before: a step was built, a save loaded, a new estate begun.</summary>
        public static event Action Changed;

        private static readonly JArray Empty = new JArray();
        private static readonly string[] ValueKeys = { "cost_currency.amount", "number_of_slots", "heal_low", "amount", "discount_percent", "chance" };
        private static Dictionary<string, Tree> _trees;
        private static List<Tree> _ordered;
        private static readonly List<Func<Tree>> OwnTrees = new List<Func<Tree>>();

        /// <summary>
        /// Adds a tree of the mod's own. The builder runs once DD1's trees are read, so it can take its prices
        /// from them (<see cref="Find"/>); its tree comes last among its building's.
        /// </summary>
        public static void Define(Func<Tree> build)
        {
            OwnTrees.Add(build);
            _trees = null;
            _ordered = null;
        }

        // ---- trees -------------------------------------------------------------------------------------

        public static IReadOnlyList<Tree> All
        {
            get
            {
                Load();
                return _ordered;
            }
        }

        /// <summary>A building's trees in DD1's order; empty when it has none or DD1 cannot be read.</summary>
        public static List<Tree> TreesOf(string building)
        {
            var list = new List<Tree>();
            foreach (var tree in All)
                if (tree.Building == building) list.Add(tree);
            return list;
        }

        public static Tree Find(string treeId)
        {
            Load();
            return treeId != null && _trees.TryGetValue(treeId, out var tree) ? tree : null;
        }

        public static Track FindTrack(string treeId, string kind, string subject = null)
        {
            var tree = Find(treeId);
            if (tree == null) return null;
            foreach (var track in tree.Tracks)
                if (track.Kind == kind && (subject == null || track.Subject == subject)) return track;
            return null;
        }

        // ---- levels ------------------------------------------------------------------------------------

        /// <summary>
        /// Steps built in a tree. A number stored under the bare building id counts for all its trees (a test
        /// shortcut: "tavern" = 6 upgrades the whole tavern).
        /// </summary>
        public static int Level(Tree tree)
        {
            return tree == null ? 0 : Math.Min(tree.Steps.Count, Stored(tree.Id));
        }

        /// <summary>As <see cref="Level(Tree)"/>; for a tree DD1's files do not list, the stored number as it is.</summary>
        public static int Level(string treeId)
        {
            var tree = Find(treeId);
            return tree != null ? Level(tree) : Stored(treeId);
        }

        private static int Stored(string treeId)
        {
            var state = EstateState.Current;
            var dot = treeId.IndexOf('.');
            var own = state.BuildingLevel(treeId);
            return dot < 0 ? own : Math.Max(own, state.BuildingLevel(treeId.Substring(0, dot)));
        }

        public static bool Has(string treeId, string code)
        {
            var tree = Find(treeId);
            var index = tree != null ? tree.IndexOf(code) : -1;
            return index >= 0 && index < Level(tree);
        }

        public static bool Has(Need need) => Has(need.Tree, need.Code);

        /// <summary>A track's value at the tree's present level.</summary>
        public static float Value(string treeId, string kind, float fallback, string subject = null)
        {
            var track = FindTrack(treeId, kind, subject);
            if (track == null || track.Values.Length == 0) return fallback;
            return track.Values[Math.Min(Level(treeId), track.Values.Length - 1)];
        }

        /// <summary>The share taken off prices by a tree of discounts ("guild.cost"): 0 .. 1.</summary>
        public static float Discount(string treeId)
        {
            var tree = Find(treeId);
            if (tree == null) return 0f;
            foreach (var track in tree.Tracks)
                if (track.Additive) return Mathf01(track.Values[Math.Min(Level(tree), track.Values.Length - 1)]);
            return 0f;
        }

        /// <summary>A price after a tree's discount; never free.</summary>
        public static int Price(int gold, string discountTree)
        {
            if (gold <= 0) return 0;
            return Math.Max(1, ActivityRules.WholeGold(gold * (1f - Discount(discountTree))));
        }

        /// <summary>Steps built over steps there are, across a building's trees: 0 .. 1.</summary>
        public static float Fraction(string building)
        {
            int built = 0, total = 0;
            foreach (var tree in TreesOf(building))
            {
                built += Level(tree);
                total += tree.Steps.Count;
            }
            return total > 0 ? (float)built / total : 0f;
        }

        // ---- building a step ---------------------------------------------------------------------------

        /// <summary>The next step of a tree, null when the tree is complete.</summary>
        public static Step Next(Tree tree)
        {
            var level = Level(tree);
            return level < tree.Steps.Count ? tree.Steps[level] : null;
        }

        /// <summary>Steps of other trees the next step still waits for.</summary>
        public static List<Need> Missing(Step step)
        {
            var missing = new List<Need>();
            foreach (var need in step.Needs)
                if (!Has(need)) missing.Add(need);
            return missing;
        }

        /// <summary>The first currency of a price the estate is short of; null when it can pay.</summary>
        public static string ShortOf(Step step)
        {
            if (TownEventHooks.FreeUpgrades("building") > 0) return null;      // DD1 upgrade_tag_free: the week's gift
            foreach (var cost in step.Costs)
                if (Purse(cost.Currency) < Amount(cost)) return cost.Currency;
            return null;
        }

        public static int Purse(string currency)
        {
            return currency == Gold ? EstateState.Gold : EstateState.Current.Heirloom(currency);
        }

        /// <summary>What the estate pays: gold and heirlooms as DD1 writes them.</summary>
        public static int Amount(Cost cost)
        {
            return cost.Amount;
        }

        /// <summary>Why the next step of a tree cannot be built right now; null when it can.</summary>
        public static string BlockReason(Tree tree)
        {
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            var step = Next(tree);
            if (step == null) return "Nothing left to build";
            var missing = Missing(step);
            if (missing.Count > 0) return "Needs " + UpgradeText.Needs(missing);
            var shortOf = ShortOf(step);
            return shortOf != null ? "Not enough " + UpgradeText.Plural(shortOf) : null;
        }

        /// <summary>Builds the next step of a tree and pays for it. Returns null, or why nothing was built.</summary>
        public static string Buy(Tree tree)
        {
            var reason = BlockReason(tree);
            if (reason != null) return reason;
            var step = Next(tree);
            if (!TownEventHooks.UseFreeUpgrade("building"))
            {
                foreach (var cost in step.Costs)
                {
                    if (cost.Currency == Gold) EstateState.AddGold(-Amount(cost));
                    else EstateState.Current.AddHeirloom(cost.Currency, -cost.Amount);
                }
            }
            EstateState.Current.Buildings[tree.Id] = Level(tree) + 1;
            Plugin.Log.LogInfo("Upgrade: " + tree.Id + " " + step.Code + " built (level " + Level(tree) + " of " + tree.Steps.Count + ")");
            try { Built?.Invoke(tree); }
            catch (Exception e) { Plugin.Log.LogError("Upgrade: a Built handler failed: " + e); }
            RaiseChanged();
            EstatePersistence.SaveNow("upgrade");
            return null;
        }

        internal static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Upgrade: a Changed handler failed: " + e); }
        }

        // ---- reading DD1 -------------------------------------------------------------------------------

        private static void Load()
        {
            if (_trees != null) return;
            _trees = new Dictionary<string, Tree>();
            _ordered = new List<Tree>();
            foreach (var building in BuildingIds)
            {
                try { LoadTrees(building); }
                catch (Exception e) { Plugin.Log.LogWarning("Upgrades of the " + building + " could not be read from DD1: " + e.Message); }
            }
            // Tracks come second: a list in one building's file may name another building's tree.
            foreach (var building in BuildingIds)
            {
                try
                {
                    var text = Dd1Install.ReadText("campaign/town/buildings/" + building + "/" + building + ".building.json");
                    if (text != null) CollectTracks(building, JObject.Parse(text)["data"], null);
                }
                catch (Exception e) { Plugin.Log.LogWarning("Upgrade effects of the " + building + " could not be read from DD1: " + e.Message); }
            }
            var fromDd1 = _ordered.Count;
            foreach (var build in OwnTrees)
            {
                try
                {
                    var tree = build();
                    if (tree == null || tree.Id == null || _trees.ContainsKey(tree.Id)) continue;
                    _trees[tree.Id] = tree;
                    _ordered.Add(tree);
                }
                catch (Exception e) { Plugin.Log.LogWarning("A tree of the mod's own could not be built: " + e.Message); }
            }
            Plugin.Log.LogInfo("Upgrade rules: " + fromDd1 + " trees from DD1, " + (_ordered.Count - fromDd1) + " of the mod's own");
        }

        private static void LoadTrees(string building)
        {
            var file = "upgrades/building/" + building + ".upgrades.json";
            var text = Dd1Install.ReadText(file);
            if (text == null)
            {
                Plugin.Log.LogWarning("DD1 file missing: " + file);
                return;
            }
            foreach (var entry in JObject.Parse(text)["trees"] as JArray ?? Empty)
            {
                var id = (string)entry["id"];
                if (id == null || _trees.ContainsKey(id)) continue;
                var tree = new Tree { Id = id, Building = building };
                foreach (var requirement in entry["requirements"] as JArray ?? Empty)
                {
                    var code = (string)requirement["code"];
                    if (code == null) continue;
                    var step = new Step { Code = code };
                    foreach (var cost in requirement["currency_cost"] as JArray ?? Empty)
                    {
                        var amount = (int?)cost["amount"] ?? 0;
                        var type = (string)cost["type"];
                        if (amount > 0 && type != null) step.Costs.Add(new Cost { Currency = type, Amount = amount });
                    }
                    foreach (var need in requirement["prerequisite_requirements"] as JArray ?? Empty)
                    {
                        var other = (string)need["tree_id"];
                        var otherCode = (string)need["requirement_code"];
                        if (other != null && otherCode != null && other != id) step.Needs.Add(new Need { Tree = other, Code = otherCode });
                    }
                    tree.Steps.Add(step);
                }
                _trees[id] = tree;
                _ordered.Add(tree);
            }
        }

        // Walks a building file; an object with "id" and "data" is an activity or a store and names what lies below.
        private static void CollectTracks(string building, JToken node, string subject)
        {
            if (node is JArray array)
            {
                foreach (var item in array) CollectTracks(building, item, subject);
                return;
            }
            if (!(node is JObject obj)) return;
            if (obj["id"] != null && obj["data"] is JObject) subject = (string)obj["id"];
            foreach (var property in obj.Properties())
            {
                if (property.Name.EndsWith("_upgrades", StringComparison.Ordinal) && property.Value is JArray list) AddTracks(building, property.Name, subject, list);
                else CollectTracks(building, property.Value, subject);
            }
        }

        // One track per tree the list names: element(s) without a code are the value before any step, the others
        // come into force with their step. A list without `upgrade_tree_id` belongs to the tree of its activity.
        private static void AddTracks(string building, string kind, string subject, JArray list)
        {
            var byTree = new Dictionary<string, List<JObject>>();
            JObject start = null;
            foreach (var item in list)
            {
                if (!(item is JObject entry)) continue;
                var code = (string)entry["upgrade_requirement_code"];
                if (code == null)
                {
                    start = entry;
                    continue;
                }
                var treeId = (string)entry["upgrade_tree_id"] ?? (subject != null ? building + "." + subject : null);
                if (treeId == null || !_trees.ContainsKey(treeId)) continue;
                if (!byTree.TryGetValue(treeId, out var entries)) byTree[treeId] = entries = new List<JObject>();
                entries.Add(entry);
            }

            var additive = kind.EndsWith("_discount_upgrades", StringComparison.Ordinal);
            foreach (var pair in byTree)
            {
                var tree = _trees[pair.Key];
                var levels = tree.Steps.Count + 1;
                var track = new Track { Kind = kind, Subject = subject, Additive = additive, Values = new float[levels], Entries = new JObject[levels] };
                for (var level = 0; level < levels; level++)
                {
                    var value = additive ? 0f : EntryValue(start);
                    var inForce = additive ? null : start;
                    foreach (var entry in pair.Value)
                    {
                        var at = tree.IndexOf((string)entry["upgrade_requirement_code"]);
                        if (at < 0 || at >= level) continue;
                        value = additive ? value + EntryValue(entry) : EntryValue(entry);
                        inForce = entry;
                    }
                    track.Values[level] = value;
                    track.Entries[level] = inForce;
                }
                tree.Tracks.Add(track);
            }
        }

        private static float EntryValue(JObject entry)
        {
            if (entry == null) return 0f;
            foreach (var key in ValueKeys)
            {
                var value = (float?)entry.SelectToken(key);
                if (value != null) return value.Value;
            }
            return 0f;
        }

        private static float Mathf01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
