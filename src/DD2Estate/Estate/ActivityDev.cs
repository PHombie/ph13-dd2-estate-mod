using System.Collections.Generic;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands for the dev bridge ({"cmd":"run","name":"activity.place","guid":3,"building":"tavern",
    /// "activity":"bar","slot":0}); they do what the building screen does, without the mouse.
    /// </summary>
    [EstateModule]
    internal static class ActivityDev
    {
        private static void Register()
        {
            AgentBridge.Register("activity.state", o => ActivityLedger.Describe());
            AgentBridge.Register("activity.rules", o =>
            {
                var rules = new List<object>();
                foreach (var building in ActivityRules.BuildingIds)
                {
                    foreach (var activity in ActivityRules.For(building))
                    {
                        rules.Add(new
                        {
                            building,
                            activity = activity.Id,
                            name = ActivityText.Activity(activity.Id),
                            level = ActivityRules.Level(activity),
                            cost = ActivityRules.Cost(activity, 0),
                            relief = ActivityRules.ReliefText(activity),
                            slots = ActivityRules.Slots(activity),
                            sideEffectChance = activity.SideEffectChance
                        });
                    }
                }
                return rules;
            });
            AgentBridge.Register("activity.open", o =>
            {
                BuildingPanel.Open((string)o["building"] ?? "tavern");
                return BuildingPanel.IsOpen ? "open" : "not opened";
            });
            AgentBridge.Register("activity.close", o =>
            {
                BuildingPanel.Close();
                return "closed";
            });
            // The open screen with a hero standing in a slot, not yet paid for (the price over the hero, the check
            // mark under the slot), as after a drag from the roster: {"activity":"bar","slot":0,"guid":3}.
            AgentBridge.Register("activity.stand", o =>
                BuildingPanel.StandForTest((string)o["activity"], (int?)o["slot"] ?? 0, (uint?)o["guid"] ?? 0u) ?? (object)BuildingPanel.Snapshot());
            // Seats the Caretaker for this week: {"building":"tavern","activity":"bar"}; without an activity he sits nowhere.
            AgentBridge.Register("activity.caretaker", o =>
                ActivityLedger.SeatCaretakerForTest((string)o["building"], (string)o["activity"]) ?? ActivityLedger.Describe());
            AgentBridge.Register("activity.place", o =>
            {
                var activity = ActivityRules.Find((string)o["building"] ?? "tavern", (string)o["activity"]);
                if (activity == null) return "no such activity";
                return ActivityLedger.Place((uint)o["guid"], activity, (int?)o["slot"] ?? 0) ?? "placed";
            });
            AgentBridge.Register("activity.cancel", o =>
            {
                var stay = ActivityLedger.StayOf((uint)o["guid"]);
                return stay == null ? "not in an activity" : ActivityLedger.Cancel(stay) ?? "cancelled";
            });
        }
    }
}
