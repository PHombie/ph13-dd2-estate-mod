using DD2Estate.Core;
using DD2Estate.Dev;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// DD1's curio tracker: what the player has learnt items do to curios, kept with the estate as DD1 keeps it
    /// with its own (persist.curio_tracker.json). While a curio's window is up, every card of the inventory
    /// that can be tried on a curio wears the mark of what it did to this kind the last time
    /// (panels/icons_curio_tracker/&lt;id&gt;.curio_tracker.png at shared/inventory/inventory.layout.darkest,
    /// inventory_curio_tracker_layout.icon_offset 24 118 from the cell's corner): a question mark until it has
    /// been tried ("unkown", DD1's spelling), the library's "CURIO TRACKER ID" of the interaction once it has
    /// (loot, purge_neg, torch_up...), and "no_effect" for an item the curio turned out to have no use for.
    /// Seen in DD1's own frames (_lab/dd1_ref/raid/raid_curio_window_chest_room.png: question marks under the
    /// supplies, none under the food, the gold and the heirlooms).
    /// </summary>
    [EstateModule]
    internal static class CurioMemory
    {
        private const string SectionKey = "curioTracker";
        private const string ArtDir = "panels/icons_curio_tracker/";

        public static readonly CurioTracker Tracker = new CurioTracker();

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, () => Tracker.ToJson(), json => Tracker.FromJson(json), Tracker.Clear);
            // What the estate has learnt: {} lists it; {"curio":"heirloom_chest","item":"skeleton_key","id":"loot"}
            // teaches it one thing (id "unkown" is as good as forgetting); {"clear":true} forgets everything.
            AgentBridge.Register("curio.tracker", o =>
            {
                if ((bool?)o["clear"] == true) Tracker.Clear();
                if (o["curio"] != null && o["item"] != null) Tracker.Learn((string)o["curio"], (string)o["item"], (string)o["id"] ?? TrackerIds.NoEffect);
                return new { known = Tracker.Count, tracker = Tracker.ToJson().ToString(Newtonsoft.Json.Formatting.None) };
            });
        }

        public static void Learn(string curioType, string itemId, string trackerId) => Tracker.Learn(curioType, itemId, trackerId);

        /// <summary>
        /// DD1's picture of what an item is known to do to a curio of this type; null for an item that is not a
        /// thing curios are tried with (gold, a gem, an heirloom, a quest's item) or when no curio is asked about.
        /// </summary>
        public static string Art(string curioType, ItemDef item)
        {
            if (string.IsNullOrEmpty(curioType) || item == null || item.Type != ItemTypes.Supply) return null;
            return ArtDir + Tracker.Get(curioType, item.Id) + ".curio_tracker.png";
        }
    }
}
