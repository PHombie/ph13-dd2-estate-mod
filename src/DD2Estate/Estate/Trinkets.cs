using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Game;
using Assets.Code.Item;
using Assets.Code.Library;
using Assets.Code.Loot;
using Assets.Code.UI.Items;
using Assets.Code.Utils;
using Assets.Code.Utils.Behaviors;
using DD2Estate.Core;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The estate's trinkets. The trinkets are DD2's own items; DD1 decides which rarity a quest pays and the
    /// wagon stocks and what a rarity costs (<see cref="TrinketRules"/>). This class is the bridge:
    ///
    /// - a DD1 rarity becomes a DD2 one (<see cref="SubTypes"/>) and a trinket is drawn from the pools DD2
    ///   itself sells that rarity from (<see cref="Pools"/>), leaving out what the estate holds already:
    ///   never two of the same (<see cref="OneOfEach"/>, the setting [Rules] UniqueTrinkets; the rule itself
    ///   is <see cref="TrinketDraws"/>). Every source of a trinket draws through <see cref="Roll"/> or
    ///   <see cref="RollTrophy"/>: a quest's reward, the wagon's table, the loot of fights and curios, a boss's
    ///   trophy, a monster's own trinket;
    /// - the estate's trinket store is a list of the estate's own (DD1's trinket storage: any number, in the
    ///   order they came), saved with the estate. A worn trinket is in the hero's DD2 trinket slots, where the
    ///   fight reads it; DD2's player inventory holds none (what DD2 itself drops there, a trinket a hero may
    ///   no longer wear, is taken into the stores when the hub is back: <see cref="Dd2.Dd2Sweep"/>);
    /// - every trinket that came through a DD1 rule keeps the DD1 rarity it came in under (its "grade"), which
    ///   is what the wagon pays for it when it is sold.
    ///
    /// Design calls of the mod, with their reasons, are in docs/recon/quests-and-trinkets.md.
    /// </summary>
    [EstateModule]
    internal static class Trinkets
    {
        private const string SectionKey = "trinkets";

        /// <summary>DD1's rarity of the trophies its boss quests name; they have no price and are not sold.</summary>
        public const string Trophy = "trophy";

        /// <summary>
        /// DD1 rarity to DD2 rarity (the item's sub type). DD1 has five steps and DD2 three, so neighbours
        /// share one; DD1 itself shows "very_common" and "common" under one name.
        /// </summary>
        private static readonly Dictionary<string, string> SubTypes = new Dictionary<string, string>
        {
            { "very_common", "common" }, { "common", "common" },
            { "uncommon", "rare" }, { "rare", "rare" },
            { "very_rare", "epic" },
            { "ancestral", "ancestral" },
            // DD1's rarity of the Shrieker's own four trinkets (the reward of "Shrieker's Prize"). DD2 has no
            // Shrieker and no trinkets of its: one of DD2's rarest ordinary trinkets stands for it and keeps
            // "crow" as its grade (DD1 has a card backing for it and gives it no price).
            { "crow", "epic" }
        };

        // Rarest last; a pool that has run dry is replaced by the nearest one, never by the ancestral one.
        private static readonly string[] SubTypeOrder = TrinketDraws.Order;

        /// <summary>
        /// DD2's loot tables per rarity: what its own trinket shop (hoarder_shop_trinkets_common/rare/epic)
        /// and generic trinket loot draw from, with the region tables named outright because the estate is in
        /// no DD2 region, and the Ancestor's three for DD1's ancestral trinkets. A table this game build or
        /// its DLC does not have is skipped.
        /// </summary>
        private static readonly Dictionary<string, string[]> Pools = new Dictionary<string, string[]>
        {
            { "common", new[] { "TRINKETS_COMMODITY_COMMON" } },
            {
                "rare", new[]
                {
                    "TRINKETS_COMMODITY_RARE", "TRINKETS_GENERAL_ALL", "TRINKETS_CAVE_RARE", "TRINKETS_CITY_RARE", "TRINKETS_COAST_RARE",
                    "TRINKETS_FARM_RARE", "TRINKETS_FOREST_RARE", "TRINKETS_TUNDRA_RARE"
                }
            },
            {
                "epic", new[]
                {
                    "TRINKETS_COMMODITY_EPIC", "TRINKETS_CAVE_EPIC", "TRINKETS_CITY_EPIC", "TRINKETS_COAST_EPIC", "TRINKETS_FARM_EPIC",
                    "TRINKETS_FOREST_EPIC", "TRINKETS_TUNDRA_EPIC", "TRINKETS_CATACOMBS_EPIC", "TRINKETS_HERO_ALL_UNCONDITIONAL",
                    "TRINKETS_HERO_ALL", "TRINKETS_HOARDER"
                }
            },
            { "ancestral", new[] { "TRINKETS_ANCESTOR_STATUE" } }
        };

        /// <summary>
        /// Where a DD1 boss trophy comes from: DD2's own trinket table of the boss that stands in its place
        /// (the mod's boss id, Data/dungeons.json). A boss without a table of its own pays a very rare trinket.
        /// </summary>
        private static readonly Dictionary<string, string> BossTables = new Dictionary<string, string>
        {
            { "librarian", "TRINKETS_CITY_BOSS" }, { "dreaming_general", "TRINKETS_FOREST_BOSS" }, { "harvest_child", "TRINKETS_FARM_BOSS" },
            { "leviathan", "TRINKETS_COAST_BOSS" }, { "exemplar", "TRINKETS_CULTIST" }, { "meat_hook", "TRINKETS_WARLORD_BOSS" },
            // the Brigand Incursion's Vvulf (DD1's trophy: Vvulf's Tassel) is stood in by DD2's Warlord: his own trinkets
            { "vvulf", "TRINKETS_WARLORD_BOSS" }
        };

        /// <summary>
        /// DD1's rarities that belong to one monster's fight (award_category "battle": the Collector's heads,
        /// the Shambler's ancestral trinkets) and DD2's own trinket table of the same monster. A trinket drawn
        /// from one keeps the DD1 rarity as its grade: DD1 prices it and has a card backing for it.
        /// </summary>
        private static readonly Dictionary<string, string> MonsterTables = new Dictionary<string, string>
        {
            { "collector", "TRINKETS_COLLECTOR_BOSS" }, { "ancestral_shambler", "TRINKETS_SHAMBLER_BOSS" }
        };

        /// <summary>
        /// The DD1 rarity a trinket without a grade is valued at (one DD2 itself handed out after a fight): the
        /// cheapest DD1 rarity of its DD2 rarity. DD2 rarities DD1 has no step for are valued like DD1's own
        /// fight-only trinkets, which cost what a rare one does.
        /// </summary>
        private static readonly Dictionary<string, string> UngradedAs = new Dictionary<string, string>
        {
            { "common", "very_common" }, { "rare", "uncommon" }, { "epic", "very_rare" }, { "ancestral", "ancestral" }
        };
        private const string UngradedOther = "rare";

        /// <summary>
        /// The owner's rule (2026-10-06: "never more than one of the same"), the setting [Rules] UniqueTrinkets:
        /// true, the estate holds a trinket once and nothing hands out one it holds; false is DD1's own way,
        /// where the estate may hold copies. Counted over the stores, the expedition's bag and loot scroll, what
        /// the living wear, what the fallen wore until it is claimed, and the Shrieker's hoard (<see cref="Held"/>).
        /// The dev bridge may change it for the session (trinkets.oneofeach); it follows the setting whenever that
        /// is changed (the Estate tab of the game's options).
        /// </summary>
        public static bool OneOfEach = true;

        private static TrinketRules _rules;
        private static readonly Dictionary<string, string> Grades = new Dictionary<string, string>();
        // The unworn trinkets of the estate by DD2 item id, in the order they came. DD1's storage has no limit.
        private static readonly List<string> Store = new List<string>();
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> NoIcon = new HashSet<string>();
        private static readonly Dictionary<string, int> IconAsked = new Dictionary<string, int>();
        // A screen asks a few times a second while an icon loads; one that has not come after this many asks never will.
        private const int IconPatience = 150;

        private static void Register()
        {
            var oneOfEach = Plugin.Settings.Bind("Rules", "UniqueTrinkets", true,
                "Never two of the same trinket: quests, the Nomad Wagon, fights and curios hand out only trinkets the estate does not hold (worn, in the stores or in the expedition's bag). When none is left, another rarity is given, or gold of the trinket's worth. Off: copies may drop, as in Darkest Dungeon (1).");
            OneOfEach = oneOfEach.Value;
            // changed while the game runs (the Estate tab of the options): the next draw goes by it
            oneOfEach.SettingChanged += (s, e) => OneOfEach = oneOfEach.Value;
            EstateState.RegisterSection(SectionKey, Save, Load, () =>
            {
                Grades.Clear();
                Store.Clear();
            });
        }

        private static JToken Save()
        {
            return new JObject { ["grades"] = JObject.FromObject(Grades), ["stored"] = new JArray(Store) };
        }

        // A save from before the stores were the estate's own has no "stored": its unworn trinkets lie in DD2's
        // player inventory, which the game has put back by now, and the sweep that follows every load
        // (EstateState.LoadFrom) takes them from there.
        private static void Load(JToken json)
        {
            Grades.Clear();
            Store.Clear();
            if (json?["grades"] is JObject grades)
                foreach (var p in grades.Properties()) Grades[p.Name] = (string)p.Value;
            if (json?["stored"] is JArray stored)
                foreach (var id in stored)
                    if (!string.IsNullOrEmpty((string)id)) Store.Add((string)id);
        }

        /// <summary>DD1's trinket rarities, prices and wagon tables, read from the install on first use.</summary>
        public static TrinketRules Rules
        {
            get
            {
                if (_rules != null) return _rules;
                _rules = TrinketRules.Load(new Dd1Files());
                if (_rules.Missing.Count > 0) Plugin.Log.LogWarning("Trinkets: DD1 files not readable, stand-in values in use: " + string.Join(", ", _rules.Missing));
                return _rules;
            }
        }

        // ---- DD2's items ---------------------------------------------------------------------------------

        private static Library<string, ItemDefinition> Items => SingletonMonoBehaviour<Library<string, ItemDefinition>>.Instance;

        /// <summary>The DD2 item; null for an id this game build (or its owned DLC) does not have.</summary>
        public static ItemDefinition Find(string id)
        {
            return !string.IsNullOrEmpty(id) && Items != null && Items.GetHasLibraryKey(id) ? Items.GetLibraryElement(id) : null;
        }

        /// <summary>DD2's rarity of a trinket: "common", "rare", "epic", "ancestral", "cultist"...</summary>
        public static string SubTypeOf(string id) => Find(id)?.SubType?.m_Id;

        /// <summary>The DD2 rarity a DD1 one stands for; null for DD1's special rarities.</summary>
        public static string SubTypeFor(string dd1Rarity) => dd1Rarity != null && SubTypes.TryGetValue(dd1Rarity, out var subType) ? subType : null;

        /// <summary>DD2's name of the trinket.</summary>
        public static string Name(string id)
        {
            var item = Find(id);
            if (item == null) return id ?? "";
            try { return StripTags(ItemDescription.GetTitle(item, 1)); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Trinkets: no name for " + id + ": " + e.Message);
                return id;
            }
        }

        /// <summary>
        /// What DD2's own item tooltip says under the name: type, effects, flavour (ItemTooltipBhv.Populate
        /// builds its text with the same call). TextMeshPro rich text.
        /// </summary>
        public static string Description(string id)
        {
            var item = Find(id);
            if (item == null) return "";
            try { return ItemDescription.GetDescription(item, 1, false, 0, false, false, hideTitle: true) ?? ""; }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Trinkets: no description for " + id + ": " + e.Message);
                return "";
            }
        }

        /// <summary>
        /// DD2's icon of the trinket. Icons are addressable prefabs loaded on demand: null while one is still
        /// loading (ask again a moment later) and for an item without one.
        /// </summary>
        public static Sprite Icon(string id)
        {
            if (string.IsNullOrEmpty(id) || NoIcon.Contains(id)) return null;
            if (Icons.TryGetValue(id, out var cached) && cached != null) return cached;
            var item = Find(id);
            if (item == null) return null;
            try
            {
                // asking starts the load
                if (!InventoryUiUtils.IsItemIconLoaded(item))
                {
                    IconAsked.TryGetValue(id, out var asked);
                    IconAsked[id] = asked + 1;
                    if (asked + 1 >= IconPatience)
                    {
                        Plugin.Log.LogWarning("Trinkets: the icon of " + id + " never loaded");
                        NoIcon.Add(id);
                    }
                    return null;
                }
                var sprite = SpriteOf(InventoryUiUtils.GetItemIconPrefab(item));
                if (sprite != null) Icons[id] = sprite;
                else NoIcon.Add(id);
                return sprite;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Trinkets: no icon for " + id + ": " + e.Message);
                NoIcon.Add(id);
                return null;
            }
        }

        /// <summary>True once it is known that the trinket has no icon to wait for.</summary>
        public static bool HasNoIcon(string id) => string.IsNullOrEmpty(id) || NoIcon.Contains(id) || Find(id) == null;

        // The game's own way to a bare sprite (AltarMemoryBhv.LoadIcon): spawn the prefab from the pool, set
        // its appearance to full, read the image that is showing, hand the instance back.
        private static Sprite SpriteOf(GameObject prefab)
        {
            if (prefab == null) return null;
            var instance = InventoryUiUtils.SpawnItemPrefabFromObjectPool(prefab);
            if (instance == null) return null;
            try
            {
                Image image = null;
                var appearance = instance.GetComponent<VariableAppearanceBhv>();
                if (appearance != null)
                {
                    appearance.SetAppearance(1f);
                    var shown = appearance.GetActiveChild();
                    if (shown != null) image = shown.GetComponent<Image>();
                }
                if (image == null || image.sprite == null)
                    image = instance.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null);
                return image != null ? image.sprite : null;
            }
            finally
            {
                InventoryUiUtils.RecycleItemPrefabToObjectPool(instance);
            }
        }

        private static string StripTags(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Trim();
        }

        // ---- what the estate holds -----------------------------------------------------------------------

        /// <summary>
        /// Whether the estate may take one more of this trinket: not while it holds one (<see cref="Held"/>).
        /// DD2's own possession limit of the item is not asked: some of its trinkets may be owned twice or
        /// without limit, and the rule here is one. With <see cref="OneOfEach"/> off, always.
        /// </summary>
        public static bool CanHold(string id)
        {
            return Find(id) != null && TrinketDraws.MayHold(OneOfEach ? Held(id) : 0, OneOfEach);
        }

        /// <summary>
        /// How many of this trinket the estate has in all: in the stores, with the expedition, on the living,
        /// in the Shrieker's hoard (taken, but to be won back: the estate is not sold a second one meanwhile),
        /// and what a hero who has just died was wearing, until the expedition or the stores have claimed it.
        /// </summary>
        public static int Held(string id)
        {
            var fallen = 0;
            foreach (var worn in Dd2.Dd2Loot.FallenTrinkets)
                if (worn == id) fallen++;
            return InStore(id) + DungeonRun.TrinketsCarried(id) + Worn(id) + Shrieker.InHoard(id) + fallen;
        }

        /// <summary>
        /// Every trinket the estate holds more than one of, with the number: none while <see cref="OneOfEach"/>
        /// has been in force for the estate's whole life (an older save, or the rule switched off for a while,
        /// may have left some; they stay, and no third is added).
        /// </summary>
        public static Dictionary<string, int> Duplicates()
        {
            var ids = new HashSet<string>(Store);
            var run = DungeonRun.Current;
            if (run != null)
                foreach (var id in run.BagTrinkets()) ids.Add(id);
            foreach (var guid in RosterLifecycle.LivingGuids())
                foreach (var id in RealmInventory.Worn(guid))
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
            var twice = new Dictionary<string, int>();
            foreach (var id in ids)
            {
                var held = Held(id);
                if (held > 1) twice[id] = held;
            }
            return twice;
        }

        /// <summary>How many of this trinket the estate's living heroes wear.</summary>
        public static int Worn(string id)
        {
            var item = Find(id);
            if (item == null) return 0;
            var worn = 0;
            try
            {
                foreach (var guid in RosterLifecycle.LivingGuids())
                {
                    var slots = UpgradeUi.Hero(guid)?.GetTrinketInventory();
                    if (slots != null) worn += slots.GetItemQty(item);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Trinkets: the heroes' trinket slots could not be read (" + e.Message + ")"); }
            return worn;
        }

        /// <summary>How many of this trinket lie unworn in the estate's stores.</summary>
        public static int InStore(string id)
        {
            var count = 0;
            foreach (var stored in Store)
                if (stored == id) count++;
            return count;
        }

        /// <summary>The unworn trinkets in the estate's stores, by DD2 item id, in the order they came.</summary>
        public static List<string> Stored() => new List<string>(Store);

        /// <summary>
        /// Puts a trinket into the estate's stores and notes the DD1 rarity it came in under. DD1's storage
        /// has no limit, so nothing is ever turned away.
        /// </summary>
        public static bool Give(string id, string dd1Rarity)
        {
            if (Find(id) == null) return false;
            Store.Add(id);
            if (dd1Rarity != null) Grades[id] = dd1Rarity;
            Plugin.Log.LogInfo("Trinkets: " + id + " (" + (dd1Rarity ?? "ungraded") + ") goes to the estate's stores");
            return true;
        }

        /// <summary>
        /// A trinket the estate has already (taken off a hero, handed back by a hero who leaves, swept out of
        /// DD2's inventory) lies in the stores again; its grade is what it was.
        /// </summary>
        public static void Put(string id)
        {
            if (!string.IsNullOrEmpty(id)) Store.Add(id);
        }

        /// <summary>Takes one trinket out of the stores (to be worn). False when none lies there.</summary>
        public static bool Take(string id)
        {
            var at = Store.IndexOf(id);
            if (at < 0) return false;
            Store.RemoveAt(at);
            return true;
        }

        /// <summary>Notes the DD1 rarity a trinket was found under before it reaches the stores (it travels in the bag).</summary>
        public static void NoteGrade(string id, string dd1Rarity)
        {
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(dd1Rarity)) Grades[id] = dd1Rarity;
        }

        // The estate has none of it any more: its grade is forgotten with it.
        private static void Forget(string id)
        {
            if (id != null && Held(id) <= 0) Grades.Remove(id);
        }

        // ---- drawing a trinket ---------------------------------------------------------------------------

        /// <summary>Every trinket of DD2's pools for a DD2 rarity that the game would hand out right now, sorted.</summary>
        public static List<string> Pool(string subType)
        {
            var ids = new List<string>();
            if (subType == null || !Pools.TryGetValue(subType, out var tables)) return ids;
            var loot = LibraryLoot.LibraryLootInstance;
            if (loot == null) return ids;
            var rewards = new HashSet<Reward<LootType>>();
            foreach (var table in tables)
            {
                if (!loot.GetHasLibraryKey(table)) continue;
                try { LibraryLoot.CollectAllPossibleLoot(table, isValidOnly: true, rewards); }
                catch (Exception e) { Plugin.Log.LogWarning("Trinkets: loot table " + table + " could not be read: " + e.Message); }
            }
            foreach (var reward in rewards)
            {
                if (reward.m_type != LootType.ITEM || ids.Contains(reward.m_id)) continue;
                var item = Find(reward.m_id);
                // a table may mix rarities (a general one, a DLC's): only what DD2 itself calls this rarity
                if (item == null || item.m_type != ItemType.TRINKET || item.SubType == null || item.SubType.m_Id != subType) continue;
                ids.Add(reward.m_id);
            }
            // a set has no order: sort, so that a seed gives the same trinket every time
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>
        /// A DD2 trinket for a DD1 rarity that the estate does not hold yet and that is not in
        /// <paramref name="taken"/> (other rewards and wares of the same week). When every trinket of the
        /// rarity is held, the nearest other rarity is drawn from. Null when there is nothing left at all.
        /// </summary>
        public static string Roll(string dd1Rarity, Rng rng, ICollection<string> taken = null)
        {
            // a monster's own rarity: from DD2's table of that monster, and nothing when it is spent
            if (dd1Rarity != null && MonsterTables.TryGetValue(dd1Rarity, out var table)) return RollFrom(table, rng, taken);
            var wanted = SubTypeFor(dd1Rarity);
            if (wanted == null) return null;
            try
            {
                var id = TrinketDraws.Draw(wanted, Pool, CanHold, taken, rng, out var from);
                if (id != null && from != wanted) Plugin.Log.LogInfo("Trinkets: no " + wanted + " trinket left for a " + dd1Rarity + " roll, drawing a " + from + " one");
                if (id == null) Plugin.Log.LogInfo("Trinkets: nothing left to give for a " + dd1Rarity + " roll: every trinket is the estate's already");
                return id;
            }
            catch (Exception e)
            {
                // a draw that fails is a draw without a trinket, not a broken quest board
                Plugin.Log.LogError("Trinkets: drawing a " + dd1Rarity + " trinket failed: " + e);
            }
            return null;
        }

        /// <summary>The DD2 trinket standing for a trophy a DD1 boss quest names; null when the boss has no table or it is spent.</summary>
        public static string RollTrophy(string bossId, Rng rng, ICollection<string> taken = null)
        {
            return bossId != null && BossTables.TryGetValue(bossId, out var table) ? RollFrom(table, rng, taken) : null;
        }

        /// <summary>The trinkets of one DD2 loot table that the game would hand out right now, sorted; empty for a table this build lacks.</summary>
        public static List<string> TableTrinkets(string table)
        {
            var ids = new List<string>();
            var loot = LibraryLoot.LibraryLootInstance;
            if (table == null || loot == null || !loot.GetHasLibraryKey(table)) return ids;
            var rewards = new HashSet<Reward<LootType>>();
            try { LibraryLoot.CollectAllPossibleLoot(table, isValidOnly: true, rewards); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Trinkets: loot table " + table + " could not be read: " + e.Message);
                return ids;
            }
            ids.AddRange(rewards.Where(r => r.m_type == LootType.ITEM).Select(r => r.m_id).Distinct().Where(id => Find(id)?.m_type == ItemType.TRINKET));
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>DD1's rarities that have a DD2 table of their own (<see cref="MonsterTable"/>).</summary>
        public static IEnumerable<string> MonsterRarities => MonsterTables.Keys;

        /// <summary>DD2's table a monster's own DD1 rarity is drawn from; null for the rarities of the ladder.</summary>
        public static string MonsterTable(string dd1Rarity) => dd1Rarity != null && MonsterTables.TryGetValue(dd1Rarity, out var table) ? table : null;

        // One trinket of a DD2 table that the estate may still hold and that is not taken; null when there is none.
        private static string RollFrom(string table, Rng rng, ICollection<string> taken)
        {
            try
            {
                return TrinketDraws.From(TableTrinkets(table), CanHold, taken, rng);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trinkets: drawing from " + table + " failed: " + e);
                return null;
            }
        }

        // ---- DD1's value of a trinket ---------------------------------------------------------------------

        /// <summary>The DD1 rarity the trinket came in under, or the one it is valued at when DD2 handed it out.</summary>
        public static string Grade(string id)
        {
            if (id != null && Grades.TryGetValue(id, out var grade)) return grade;
            var subType = SubTypeOf(id);
            return subType != null && UngradedAs.TryGetValue(subType, out var stand) ? stand : UngradedOther;
        }

        /// <summary>Gold the wagon pays for the trinket: DD1's sell value of its grade. 0: not for sale (a trophy).</summary>
        public static int SellPrice(string id) => Math.Max(0, Rules.SellValue(Grade(id)));

        /// <summary>Gold a rarity's stand-in payment comes to when no trinket could be found: DD1's sell value.</summary>
        public static int GoldInstead(string dd1Rarity) => Math.Max(0, Rules.SellValue(dd1Rarity));

        /// <summary>
        /// A find's trinket that could not be drawn (every one of the kind is the estate's already) is paid in
        /// coin: DD1's sell value of the rarity joins the find's gold, as a quest's reward is paid when nothing
        /// is left to give. Nothing for a rarity DD1 gives no price.
        /// </summary>
        public static void CoinInstead(string dd1Rarity, ItemCatalog items, List<ItemStack> found)
        {
            var gold = GoldInstead(dd1Rarity);
            var coin = gold > 0 && items != null ? items.Ensure(ItemTypes.Gold, "") : null;
            if (coin == null || found == null) return;
            var stack = found.Find(s => s.Item == coin);
            if (stack == null) found.Add(stack = new ItemStack { Item = coin });
            stack.Amount += gold;
            Plugin.Log.LogInfo("Trinkets: " + gold + " gold in place of a " + dd1Rarity + " trinket");
        }

        /// <summary>Sells one unworn trinket from the stores. Returns null, or why nothing was sold.</summary>
        public static string Sell(string id)
        {
            if (Find(id) == null || InStore(id) <= 0) return "Not in the estate's stores";
            var price = SellPrice(id);
            if (price <= 0) return "The wagon does not buy trophies";
            Take(id);
            EstateState.AddGold(price);
            Forget(id);     // the last one is gone
            Plugin.Log.LogInfo("Trinkets: " + id + " sold for " + price + " gold");
            return null;
        }

        /// <summary>DD1's name of a rarity ("Very Rare"); the mod's wording when the table has none.</summary>
        public static string RarityName(string dd1Rarity)
        {
            var words = (dd1Rarity ?? "").Split('_');
            for (var i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        /// <summary>DD1's card backing of a rarity (panels/icons_equip/trinket/rarity_&lt;rarity&gt;.png).</summary>
        public static string RarityArt(string dd1Rarity)
        {
            return "panels/icons_equip/trinket/rarity_" + (string.IsNullOrEmpty(dd1Rarity) ? "common" : dd1Rarity) + ".png";
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe()
        {
            var pools = new List<object>();
            foreach (var subType in SubTypeOrder)
            {
                var pool = Pool(subType);
                pools.Add(new { subType, dd1 = SubTypes.Where(s => s.Value == subType).Select(s => s.Key).ToList(), size = pool.Count, free = pool.Count(CanHold), ids = pool });
            }
            var prices = new List<object>();
            foreach (var rarity in TrinketRules.Ladder)
                prices.Add(new
                {
                    rarity, dd2 = SubTypeFor(rarity), dd1Price = Rules.Price(rarity), buy = UpgradeRules.Price(Rules.Price(rarity), NomadWagon.DiscountTree),
                    dd1Sell = Rules.SellValue(rarity), sell = GoldInstead(rarity)
                });
            var stored = new List<object>();
            foreach (var id in Stored()) stored.Add(new { id, name = Name(id), subType = SubTypeOf(id), grade = Grade(id), sell = SellPrice(id) });
            return new { oneOfEach = OneOfEach, duplicates = Duplicates(), prices, pools, stored, grades = Grades, missing = Rules.Missing };
        }
    }
}
