using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Assets.Code.Actor;
using Assets.Code.Audio;
using Assets.Code.Condition;
using Assets.Code.Game;
using Assets.Code.Item;
using Assets.Code.Library;
using Assets.Code.Player;
using Assets.Code.Trinket;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Trinket Inventory ("realm inventory") for the estate: the unworn trinkets of the stores, and
    /// putting them on a hero and taking them off in town. On an expedition the other side is the bag: a
    /// trinket found on the way lies there, and changes hands with a hero of the party anywhere outside a fight
    /// (<see cref="WearFromBag"/>, <see cref="TakeOffToBag"/>). The trinkets and a hero's trinket slots are DD2's
    /// own (the fight reads what is worn from there); the stores are the estate's own list
    /// (<see cref="Trinkets"/>). This class is the only writer of a hero's trinket slots, with DD2's own tests
    /// and calls on the hero's side:
    ///
    /// - a click (DD2's "auto transfer", PlayerInventoryItemBhv.TryAutoTransferTrinket): the hero's
    ///   <c>TrinketItemInventory.CanAdd</c> and <c>GetCanEquip(item, checkInventory, checkConditions)</c>, then
    ///   <c>AddItems</c> on the hero, and the trinket leaves the stores;
    /// - a drop on one of the hero's slots: <c>SwapItems(new ItemInstance(item, 1), slot)</c>, which hands back
    ///   whatever was in the slot: that goes to the stores (not <c>AddItemToSlot</c>: it does not tell the
    ///   inventory that a quantity changed, and a trinket's effects hang on that);
    /// - taking off (TrinketInventoryItemBhv.OnTryAutoTransfer): <c>TakeItemQty</c> on the hero, never for an
    ///   item DD2 marks <c>m_IsUnequipInvalid</c>, and the trinket lies in the stores again.
    ///
    /// What DD2 would refuse is refused, with the reason DD2 gives where it has words for one (the line of an
    /// unmet condition in its item tooltip) and the mod's where DD2 only plays its "invalid" click. A trinket
    /// only changes hands inside the estate, so the possession limit is never in question here.
    /// Selling is the Nomad Wagon's trade (<see cref="NomadWagon.Sell"/>), at DD1's price for the trinket's grade.
    /// </summary>
    [EstateModule]
    internal static class RealmInventory
    {
        public const string Id = "realm_inventory";
        // DD1's estate bar shows the chest of fx/estate_realm_inventory (a Spine sprite): shut, and open while open.
        private const string IconSheet = "fx/estate_realm_inventory/estate_realm_inventory.sprite.png";
        // DD2 restricts a trinket to a class with a condition of this name ("performer_is_plague_doctor").
        private const string ClassCondition = "performer_is_";

        /// <summary>DD1's orders of the inventory (its three sort buttons) and the stores' own.</summary>
        public enum Order { Arrival, Name, Class, Rarity }

        private static readonly Regex Tags = new Regex("<[^>]+>");
        private static bool _unsaved;

        /// <summary>Raised when a trinket went on or came off a hero through this class.</summary>
        public static event Action Changed;

        private static void Register()
        {
            // DD1 keeps the inventory's button on the estate's bar, beside the log's.
            TownPanelButtons.Add(new TownPanelButtons.Entry
            {
                Id = Id, Art = IconSheet + "#chest_closed", OpenArt = IconSheet + "#chest_open", Scale = 0.65f, Size = new Vector2(82f, 79f), Place = 2,
                Name = () => ScreenName, Toggle = RealmInventoryPanel.Toggle, IsOpen = () => RealmInventoryPanel.IsOpen
            });
        }

        public static string ScreenName => WindowText.Plain("town_name_realm_inventory") ?? "Trinket Inventory";

        private static string Away => !EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet ? "Not while away from the hamlet" : null;

        // ---- what there is -------------------------------------------------------------------------------

        /// <summary>The unworn trinkets of the stores in one of DD1's orders.</summary>
        public static List<string> Stored(Order order, bool descending)
        {
            var ids = Trinkets.Stored();
            if (order == Order.Arrival) return ids;
            var arrival = new Dictionary<string, int>();
            for (var i = 0; i < ids.Count; i++)
                if (!arrival.ContainsKey(ids[i])) arrival[ids[i]] = i;
            var sign = descending ? -1 : 1;
            ids.Sort((a, b) =>
            {
                var by = sign * Compare(a, b, order);
                if (by == 0 && order != Order.Name) by = string.Compare(Trinkets.Name(a), Trinkets.Name(b), StringComparison.CurrentCulture);
                return by != 0 ? by : arrival[a].CompareTo(arrival[b]);
            });
            return ids;
        }

        private static int Compare(string a, string b, Order order)
        {
            switch (order)
            {
                case Order.Class:
                    // a class's own trinkets together, class by class; what anyone may wear after them
                    var first = ClassOf(a);
                    var second = ClassOf(b);
                    if (first == null || second == null) return (first == null).CompareTo(second == null);
                    return string.Compare(HeroNames.ClassName(first), HeroNames.ClassName(second), StringComparison.CurrentCulture);
                case Order.Rarity:
                    return Rank(a).CompareTo(Rank(b));
                default:
                    return string.Compare(Trinkets.Name(a), Trinkets.Name(b), StringComparison.CurrentCulture);
            }
        }

        // DD1's ladder of rarities; its special ones (a boss's trophy) stand above the ladder.
        private static int Rank(string id)
        {
            var rank = TrinketRules.Rank(Trinkets.Grade(id));
            return rank < 0 ? TrinketRules.Ladder.Length : rank;
        }

        /// <summary>The hero class a trinket is restricted to by DD2; null for one anyone may wear.</summary>
        public static string ClassOf(string id)
        {
            var item = Trinkets.Find(id);
            if (item == null || !item.HasCondition()) return null;
            try
            {
                foreach (var condition in item.GetConditions())
                {
                    var name = condition != null ? condition.Id : null;
                    if (name != null && name.StartsWith(ClassCondition, StringComparison.Ordinal)) return name.Substring(ClassCondition.Length);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Trinket inventory: the conditions of " + id + " could not be read: " + e.Message); }
            return null;
        }

        private static TrinketItemInventory WornBy(ActorInstance actor) => actor != null ? actor.GetTrinketInventory() as TrinketItemInventory : null;

        /// <summary>A hero's trinket slots in order: the id worn in each, null for an empty one.</summary>
        public static List<string> Worn(uint guid)
        {
            var slots = new List<string>();
            var worn = WornBy(UpgradeUi.Hero(guid));
            if (worn == null) return slots;
            foreach (var item in worn.GetItems()) slots.Add(ItemUtils.IsValid(item) ? item.GetItemDefinition().m_id : null);
            return slots;
        }

        // ---- putting on ----------------------------------------------------------------------------------

        /// <summary>
        /// Why this hero could not wear this trinket in any slot (not a trinket, one worn already, DD2's
        /// conditions of the item unmet); null when a slot is all that could stand in the way.
        /// </summary>
        public static string WearRefusal(uint guid, string id) => EquipRefusal(guid, id, -1, false);

        /// <summary>
        /// Why DD2 would not let this hero wear this trinket from the stores; null when it would.
        /// <paramref name="slot"/>: the slot it is dropped on (what is there goes back to the stores), or -1
        /// for the first free one.
        /// </summary>
        public static string EquipRefusal(uint guid, string id, int slot = -1) => EquipRefusal(guid, id, slot, true);

        private static string EquipRefusal(uint guid, string id, int slot, bool withSlot)
        {
            var actor = UpgradeUi.Hero(guid);
            if (actor == null) return "No such hero";
            var item = Trinkets.Find(id);
            if (item == null) return "No such trinket";
            var worn = WornBy(actor);
            if (worn == null) return actor.ActorName + " wears no trinkets";
            var name = Trinkets.Name(id);
            try
            {
                // TrinketItemInventory.GetCanEquip, checkInventory: a trinket, and not one the hero wears already
                if (item.m_type != ItemType.TRINKET) return name + " is not a trinket";
                if (worn.GetItemQty(item) > 0) return actor.ActorName + " already wears " + name;
                // ... checkConditions: ActorInstance.GetIsItemConditionsMet
                if (!actor.GetIsItemConditionsMet(item)) return Unmet(actor, item) ?? actor.ActorName + " cannot wear " + name;
                if (!withSlot) return null;
                if (slot >= 0)
                {
                    if (slot >= worn.GetNumberOfTotalSlots()) return actor.ActorName + " has no such trinket slot";
                    var there = worn.GetItem(slot);
                    // TrinketInventoryItemBhv.GetSwapFunction: nothing is swapped for a trinket that cannot come off
                    if (ItemUtils.IsValid(there) && there.GetItemDefinition().m_IsUnequipInvalid) return Trinkets.Name(there.GetItemDefinition().m_id) + " cannot be taken off";
                }
                else if (!worn.CanAdd(item, 1)) return actor.ActorName + " has no free trinket slot";
                return null;
            }
            catch (Exception e)
            {
                // a move the game cannot judge is a move not made
                Plugin.Log.LogWarning("Trinket inventory: the game could not say whether #" + guid + " may wear " + id + ": " + e.Message);
                return actor.ActorName + " cannot wear " + name + " right now";
            }
        }

        // DD2 says why in the item's tooltip: one line per condition. The lines of those this hero fails.
        private static string Unmet(ActorInstance actor, ItemDefinition item)
        {
            var lines = new List<string>();
            try
            {
                if (item.HasCondition())
                {
                    var input = new ConditionCalculation.Input(actor);
                    foreach (var condition in item.GetConditions())
                    {
                        if (condition == null || ConditionCalculation.IsConditionMet(condition, input)) continue;
                        var line = Tags.Replace(ConditionDescription.GetConditionString(false, condition) ?? "", "").Trim().TrimEnd('.', ':');
                        if (line.Length > 0 && !lines.Contains(line)) lines.Add(line);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Trinket inventory: DD2 would not say why " + item.m_id + " cannot be worn: " + e.Message); }
            // DD2 has no words for its other test: some trinkets are only for a hero of the party
            if (lines.Count == 0 && item.m_IsUnequipIfNotInParty && !actor.IsInParty) return Trinkets.Name(item.m_id) + " is only for a hero of the party";
            return lines.Count > 0 ? string.Join("; ", lines) : null;
        }

        /// <summary>Moves a trinket from the stores onto a hero. Returns null, or why nothing was moved.</summary>
        public static string Equip(uint guid, string id, int slot = -1)
        {
            var away = Away;
            if (away != null) return away;
            var item = Trinkets.Find(id);
            if (item == null || Trinkets.InStore(id) <= 0) return "Not in the estate's stores";
            var refusal = EquipRefusal(guid, id, slot);
            if (refusal != null)
            {
                Play(() => AudioPathsBhv.ClickInvalid);
                return refusal;
            }
            var actor = UpgradeUi.Hero(guid);
            var worn = WornBy(actor);
            try
            {
                var back = PutOn(worn, item, slot);
                if (back == Refused) return actor.ActorName + " has no free trinket slot";
                Trinkets.Take(id);
                if (back != null) Trinkets.Put(back);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trinket inventory: " + id + " could not be put on #" + guid + ": " + e);
                return "The game would not move it";
            }
            Plugin.Log.LogInfo("Trinket inventory: " + actor.ActorName + " wears " + id);
            Play("ui/town/character_equip", () => item.SubType != null ? item.SubType.TrinketEquipSfxEventRef : AudioPathsBhv.TrinketEquip);
            Moved();
            return null;
        }

        private const string Refused = "\0refused";

        /// <summary>
        /// The hero's side of putting a trinket on, by DD2's own calls: into the slot named (what was there is
        /// handed back: its id is returned) or into the first free one. Returns null when nothing came back, or
        /// <see cref="Refused"/> when the hero's slots took nothing. The caller has asked <see cref="EquipRefusal"/>.
        /// </summary>
        private static string PutOn(TrinketItemInventory worn, ItemDefinition item, int slot)
        {
            if (slot >= 0)
            {
                var old = worn.SwapItems(new ItemInstance(item, 1), slot);
                return ItemUtils.IsValid(old) ? old.GetItemDefinition().m_id : null;
            }
            return worn.AddItems(item, 1, false) > 0 ? Refused : null;
        }

        /// <summary>The hero's side of taking a trinket off. False when the hero does not wear it.</summary>
        private static bool TakeOff(TrinketItemInventory worn, ItemDefinition item)
        {
            var at = worn.FindIndex(item);
            if (at < 0) return false;
            worn.TakeItemQty(at, 1);
            return true;
        }

        // ---- taking off ----------------------------------------------------------------------------------

        public static string UnequipRefusal(uint guid, string id)
        {
            var actor = UpgradeUi.Hero(guid);
            if (actor == null) return "No such hero";
            var item = Trinkets.Find(id);
            var worn = WornBy(actor);
            if (item == null || worn == null || worn.GetItemQty(item) <= 0) return actor.ActorName + " does not wear it";
            // TrinketInventoryItemBhv.IsSelectable: DD2 does not let go of these
            return item.m_IsUnequipInvalid ? Trinkets.Name(id) + " cannot be taken off" : null;
        }

        /// <summary>Moves a trinket a hero wears back to the stores. Returns null, or why nothing was moved.</summary>
        public static string Unequip(uint guid, string id)
        {
            var away = Away;
            if (away != null) return away;
            var refusal = UnequipRefusal(guid, id);
            if (refusal != null)
            {
                Play(() => AudioPathsBhv.ClickInvalid);
                return refusal;
            }
            var actor = UpgradeUi.Hero(guid);
            var item = Trinkets.Find(id);
            var worn = WornBy(actor);
            try
            {
                // DD1's storage has no limit: a trinket can always come off
                if (!TakeOff(worn, item)) return actor.ActorName + " does not wear it";
                Trinkets.Put(id);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trinket inventory: " + id + " could not be taken off #" + guid + ": " + e);
                return "The game would not move it";
            }
            Plugin.Log.LogInfo("Trinket inventory: " + actor.ActorName + " takes off " + id);
            Play("ui/town/character_unequip", () => AudioPathsBhv.TrinketUnequip);
            Moved();
            return null;
        }

        // ---- from one slot of a hero to the other -----------------------------------------------------------

        // An expedition's corridor or room, where trinkets change hands with the bag; the hamlet is the other place.
        private static bool OnExpedition => DungeonRun.Current != null && (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet);

        /// <summary>
        /// Why a trinket a hero wears cannot go into another of the hero's own slots; null when it can.
        /// DD2's own tests for a drag between two of its slots (TrinketInventoryItemBhv): a trinket that cannot
        /// be taken off is not picked up, and nothing changes places with one.
        /// </summary>
        public static string MoveWornRefusal(uint guid, string id, int slot)
        {
            var away = OnExpedition ? BagRefusal(DungeonRun.Current, guid) : Away;
            if (away != null) return away;
            var refusal = UnequipRefusal(guid, id);
            if (refusal != null) return refusal;
            var actor = UpgradeUi.Hero(guid);
            var worn = WornBy(actor);
            if (slot < 0 || slot >= worn.GetNumberOfTotalSlots()) return actor.ActorName + " has no such trinket slot";
            var there = worn.GetItem(slot);
            if (ItemUtils.IsValid(there) && there.GetItemDefinition().m_IsUnequipInvalid) return Trinkets.Name(there.GetItemDefinition().m_id) + " cannot be taken off";
            return null;
        }

        /// <summary>
        /// Moves a trinket a hero wears into another of the hero's own slots; what lies there takes its place
        /// (DD2's own call for it: <c>ItemInventory.SwapItems(from, to)</c>, which changes no quantity, so what
        /// the two trinkets do stays in force). In the hamlet, and on an expedition anywhere outside a fight.
        /// Returns null (also for the slot it lies in already), or why nothing was moved.
        /// </summary>
        public static string MoveWorn(uint guid, string id, int slot)
        {
            var refusal = MoveWornRefusal(guid, id, slot);
            if (refusal != null)
            {
                Play(() => AudioPathsBhv.ClickInvalid);
                return refusal;
            }
            var actor = UpgradeUi.Hero(guid);
            var item = Trinkets.Find(id);
            var worn = WornBy(actor);
            int from;
            try
            {
                from = worn.FindIndex(item);
                if (from < 0) return actor.ActorName + " does not wear it";
                if (from == slot) return null;
                worn.SwapItems(from, slot);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trinket inventory: " + id + " could not be moved to slot " + slot + " of #" + guid + ": " + e);
                return "The game would not move it";
            }
            Plugin.Log.LogInfo("Trinket inventory: " + actor.ActorName + " moves " + id + " from slot " + from + " to slot " + slot);
            Play("ui/town/character_equip", () => item.SubType != null ? item.SubType.TrinketEquipSfxEventRef : AudioPathsBhv.TrinketEquip);
            // (a move on an expedition is saved with the expedition)
            if (OnExpedition) Raise();
            else Moved();
            return null;
        }

        /// <summary>DD1's "Unequip all trinkets": every trinket of every hero that can come off goes back to the stores. Returns how many did.</summary>
        public static int UnequipAll()
        {
            if (Away != null) return 0;
            var moved = 0;
            // one sound for all of them, not one each on top of the other
            _quiet = true;
            try
            {
                foreach (var guid in RosterLifecycle.LivingGuids())
                    foreach (var id in Worn(guid))
                        if (id != null && Unequip(guid, id) == null) moved++;
            }
            finally { _quiet = false; }
            if (moved > 0) Play("ui/town/character_unequip", () => AudioPathsBhv.TrinketUnequip);
            return moved;
        }

        // ---- on an expedition: the bag is the other side -----------------------------------------------------

        // DD1: trinkets change hands between the raid inventory and a hero's two slots anywhere outside a fight.
        // Why this hero cannot change trinkets with the bag right now; null when they can.
        private static string BagRefusal(DungeonRun run, uint guid)
        {
            if (run == null) return "No expedition is under way";
            // DD1's own words for it (cant_unequip_trinket_in_battle)
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Dungeon) return RaidText.Get("cant_unequip_trinket_in_battle", "Can't unequip trinket during battle");
            var actor = UpgradeUi.Hero(guid);
            if (actor == null || !actor.IsLiving || !EstateSession.IsInParty(guid)) return "Not a living hero of the party";
            return null;
        }

        /// <summary>
        /// Why the trinket in a slot of the expedition's bag cannot go onto a hero of the party; null when it can.
        /// <paramref name="heroSlot"/>: the hero's slot it changes places with, or -1 for the first free one.
        /// </summary>
        public static string WearFromBagRefusal(uint guid, int bagSlot, int heroSlot = -1)
        {
            var run = DungeonRun.Current;
            var refusal = BagRefusal(run, guid);
            if (refusal != null) return refusal;
            var stack = run.Bag.Slot(bagSlot);
            if (stack == null || stack.Item.Type != ItemTypes.Trinket) return "No trinket lies in that slot of the bag";
            return EquipRefusal(guid, stack.Item.Id, heroSlot);
        }

        /// <summary>
        /// Puts the trinket in a slot of the expedition's bag on a hero of the party; what was in the hero's slot
        /// (when one is named) takes its place in the bag. Returns null, or why nothing was moved.
        /// </summary>
        public static string WearFromBag(uint guid, int bagSlot, int heroSlot = -1)
        {
            var refusal = WearFromBagRefusal(guid, bagSlot, heroSlot);
            if (refusal != null)
            {
                Play(() => AudioPathsBhv.ClickInvalid);
                return refusal;
            }
            var run = DungeonRun.Current;
            var id = run.Bag.Slot(bagSlot).Item.Id;
            var item = Trinkets.Find(id);
            var actor = UpgradeUi.Hero(guid);
            try
            {
                var back = PutOn(WornBy(actor), item, heroSlot);
                if (back == Refused) return actor.ActorName + " has no free trinket slot";
                run.Bag.RemoveAt(bagSlot, 1);
                // the slot just emptied has room for what came off
                if (back != null) run.Bag.Add(InventoryContent.Items.Trinket(back), 1);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trinkets on the expedition: " + id + " could not be put on #" + guid + ": " + e);
                return "The game would not move it";
            }
            Plugin.Log.LogInfo("Trinkets on the expedition: " + actor.ActorName + " wears " + id + " out of the bag");
            Play("ui/town/character_equip", () => item.SubType != null ? item.SubType.TrinketEquipSfxEventRef : AudioPathsBhv.TrinketEquip);
            Raise();
            return null;
        }

        /// <summary>Why a trinket a hero of the party wears cannot go into the expedition's bag; null when it can.</summary>
        public static string TakeOffToBagRefusal(uint guid, string id)
        {
            var run = DungeonRun.Current;
            var refusal = BagRefusal(run, guid) ?? UnequipRefusal(guid, id);
            if (refusal != null) return refusal;
            // DD1's words for a bag without a slot to spare
            return run.Bag.Room(InventoryContent.Items.Trinket(id)) > 0 ? null : RaidText.Get("not_enough_room", "Not enough room!");
        }

        /// <summary>Takes a trinket off a hero of the party into the expedition's bag. Returns null, or why nothing was moved.</summary>
        public static string TakeOffToBag(uint guid, string id)
        {
            var refusal = TakeOffToBagRefusal(guid, id);
            if (refusal != null)
            {
                Play(() => AudioPathsBhv.ClickInvalid);
                return refusal;
            }
            var run = DungeonRun.Current;
            var actor = UpgradeUi.Hero(guid);
            try
            {
                if (!TakeOff(WornBy(actor), Trinkets.Find(id))) return actor.ActorName + " does not wear it";
                run.Bag.Add(InventoryContent.Items.Trinket(id), 1);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trinkets on the expedition: " + id + " could not be taken off #" + guid + ": " + e);
                return "The game would not move it";
            }
            Plugin.Log.LogInfo("Trinkets on the expedition: " + actor.ActorName + " takes off " + id + " into the bag");
            Play("ui/town/character_unequip", () => AudioPathsBhv.TrinketUnequip);
            Raise();
            return null;
        }

        /// <summary>
        /// Everything a hero wears goes back to the estate's stores, whatever DD2 says of taking it off: the hero
        /// is leaving the estate (dismissal). Returns how many trinkets came back.
        /// </summary>
        public static int HandBack(ActorInstance actor)
        {
            var worn = WornBy(actor);
            if (worn == null) return 0;
            var taken = new List<ItemInstance>();
            try { worn.PopAllItemsInto(taken); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Trinket inventory: what " + actor.ActorName + " wore could not be taken back: " + e);
                return 0;
            }
            var back = 0;
            foreach (var item in taken)
            {
                if (!ItemUtils.IsValid(item)) continue;
                for (var i = 0; i < Math.Max(1, item.GetQty()); i++)
                {
                    Trinkets.Put(item.GetItemDefinition().m_id);
                    back++;
                }
            }
            if (back > 0) Moved();
            return back;
        }

        /// <summary>Hands a worn trinket to another hero: off the one, onto the other, or nothing at all.</summary>
        public static string Give(uint from, string id, uint to)
        {
            if (from == to) return null;
            var refusal = UnequipRefusal(from, id);
            if (refusal != null) return refusal;
            // asked before it comes off: the stores do not hold it yet, everything else can be told
            refusal = EquipRefusal(to, id);
            if (refusal != null)
            {
                Play(() => AudioPathsBhv.ClickInvalid);
                return refusal;
            }
            return Unequip(from, id) ?? Equip(to, id);
        }

        /// <summary>Sells an unworn trinket of the stores, as the Nomad Wagon buys it. Returns null, or why nothing was sold.</summary>
        public static string Sell(string id) => NomadWagon.Sell(id);

        private static void Moved()
        {
            _unsaved = true;
            Raise();
        }

        // A move on an expedition is saved with the expedition (its next save carries bag and heroes alike).
        private static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Trinket inventory: a Changed handler failed: " + e); }
        }

        /// <summary>Saves what was moved since the last save: once when the inventory closes, not at every drag.</summary>
        public static void Flush()
        {
            if (!_unsaved) return;
            _unsaved = false;
            if (EstateSession.InHub) EstatePersistence.SaveNow("trinkets");
        }

        private static bool _quiet;

        // DD1's sound of a trinket going on or coming off (DD1's executable names /ui/town/character_equip and
        // character_unequip); DD2's own where DD1's audio is not to be had.
        private static void Play(string dd1Sound, Func<FMODUnity.EventReference> sound)
        {
            if (_quiet) return;
            try
            {
                if (EstateAudio.Enabled && Dd1.Dd1Audio.Project != null && Dd1.Dd1Audio.Project.Knows(dd1Sound))
                {
                    EstateAudio.Ui(dd1Sound);
                    return;
                }
            }
            catch (Exception) { /* DD1's audio is not up: DD2's sound */ }
            Play(sound);
        }

        // DD2's own sounds. A sound that will not play is no reason to stop.
        private static void Play(Func<FMODUnity.EventReference> sound)
        {
            if (_quiet) return;
            try { SingletonMonoBehaviour<AudioMgr>.Instance.Play(sound()); }
            catch (Exception) { /* no audio manager, no event: silence */ }
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Describe(Order order = Order.Arrival, bool descending = false)
        {
            var stored = new List<object>();
            foreach (var id in Stored(order, descending))
                stored.Add(new { id, name = Trinkets.Name(id), grade = Trinkets.Grade(id), dd2 = Trinkets.SubTypeOf(id), forClass = ClassOf(id), sell = Trinkets.SellPrice(id) });
            var heroes = new List<object>();
            foreach (var guid in RosterLifecycle.LivingGuids())
            {
                var actor = UpgradeUi.Hero(guid);
                if (actor == null) continue;
                heroes.Add(new { guid, name = actor.ActorName, cls = actor.ActorDataId, inParty = actor.IsInParty, worn = Worn(guid) });
            }
            return new { order = order.ToString().ToLowerInvariant(), descending, unsaved = _unsaved, stored, heroes };
        }

        public static Order ParseOrder(string word)
        {
            return Enum.TryParse(word ?? "", true, out Order order) ? order : Order.Arrival;
        }
    }
}
