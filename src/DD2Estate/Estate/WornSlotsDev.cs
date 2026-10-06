using System.Collections.Generic;
using DD2Estate.Dev;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands for a worn trinket carried from one of a hero's slots to the other, without the mouse
    /// (the mouse's own way is equipment.drag between the two slots of equipment.state):
    ///   equipment.move {"guid":3,"id":"trinket_x","slot":1}   what a drop on that slot does (the two change places)
    ///   equipment.canmove {"guid":3,"id":"trinket_x","slot":1} whether it would, and why not
    /// and on an expedition, on the dungeon's hero panel (the selected hero's two slots):
    ///   hud.worn {"slot":0,"to":"worn:1"}    the trinket in that slot is carried onto the other slot
    ///   hud.worn {"slot":0,"to":"bag"}       ... or into the inventory
    /// (a card of the bag onto a slot is hud.item {"slot":n,"to":"worn:1"})
    /// </summary>
    [EstateModule]
    internal static class WornSlotsDev
    {
        private static void Register()
        {
            AgentBridge.Register("equipment.canmove", o =>
            {
                var guid = (uint?)o["guid"] ?? 0u;
                var refusal = RealmInventory.MoveWornRefusal(guid, (string)o["id"], (int?)o["slot"] ?? -1);
                return new { allowed = refusal == null, refusal, worn = RealmInventory.Worn(guid) };
            });
            AgentBridge.Register("equipment.move", o =>
            {
                var guid = (uint?)o["guid"] ?? 0u;
                var refusal = RealmInventory.MoveWorn(guid, (string)o["id"], (int?)o["slot"] ?? -1);
                // a move made without a panel is saved at once, as the sheet's own is
                if (refusal == null) RealmInventory.Flush();
                return new { moved = refusal == null, refusal, worn = RealmInventory.Worn(guid) };
            });
            AgentBridge.Register("hud.worn", o => Dungeon.DungeonHud.DevWorn((int?)o["slot"] ?? 0, (string)o["to"]));
            // Where the roster's rows are on screen (real pixels from the bottom left: the middle of each row's left
            // half, clear of the badge and the controls at its right), for a drag that ends on a hero's row.
            AgentBridge.Register("equipment.rows", o =>
            {
                var list = RosterPanel.CanvasRoot != null ? RosterPanel.CanvasRoot.Find("Roster/List") : null;
                if (list == null) return "no roster column";
                var rows = new List<object>();
                var corners = new Vector3[4];
                for (var i = 0; i < list.childCount; i++)
                {
                    var row = list.GetChild(i) as RectTransform;
                    var click = row != null ? row.GetComponent<RosterRowClick>() : null;
                    if (click == null) continue;
                    row.GetWorldCorners(corners);
                    var hero = UpgradeUi.Hero(click.Guid);
                    rows.Add(new
                    {
                        guid = click.Guid, name = hero != null ? hero.ActorName : null, worn = RealmInventory.Worn(click.Guid),
                        x = Mathf.Lerp(corners[0].x, corners[2].x, 0.3f), y = (corners[0].y + corners[2].y) * 0.5f
                    });
                }
                return rows;
            });
        }
    }
}
