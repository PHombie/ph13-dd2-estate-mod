using System.Collections.Generic;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Activity Log for the dev bridge ({"cmd":"run","name":"log.state","weeks":2}); they
    /// read the log and drive its screen without the mouse.
    /// </summary>
    [EstateModule]
    internal static class ActivityLogDev
    {
        private static void Register()
        {
            // The log, newest week first, as plain text; {"weeks":3} only the last three weeks. The screen and the bar's buttons.
            AgentBridge.Register("log.state", o => new { log = ActivityLog.Describe((int?)o["weeks"] ?? 0), screen = ActivityLogPanel.Snapshot(), bar = TownPanelButtons.Describe() });
            // One week's entries as they are stored (rich text): {"week":7}; without a week, the present one.
            AgentBridge.Register("log.week", o =>
            {
                var week = (int?)o["week"] ?? EstateState.Current.Week;
                var entries = new List<object>();
                foreach (var entry in ActivityLog.Of(week)) entries.Add(new { kind = entry.Kind, text = entry.Text, hero = entry.Hero, building = entry.Building });
                return new { week, entries };
            });
            // Writes a line into the present week: {"text":"...","kind":"hero","hero":"crusader","building":"tavern"}
            // (kinds: note, success, failure, abandon, hero, level, building, region, darkest).
            AgentBridge.Register("log.add", o =>
            {
                ActivityLog.Add((string)o["kind"] ?? ActivityLog.Kinds.Note, (string)o["text"] ?? "A line for a test.", (string)o["hero"], (string)o["building"]);
                return ActivityLog.Describe(1);
            });
            // Empties the log.
            AgentBridge.Register("log.clear", o =>
            {
                ActivityLog.Clear();
                return "cleared";
            });
            // The caretaker's goals as the screen lists them.
            AgentBridge.Register("log.goals", o => new { quests = ActivityLog.QuestGoals(), roster = ActivityLog.RosterGoals() });
            AgentBridge.Register("log.open", o => ActivityLogPanel.Open() ? ActivityLogPanel.Snapshot() : (object)(TownPanel.BlockReason() ?? "not opened"));
            AgentBridge.Register("log.close", o =>
            {
                ActivityLogPanel.Close();
                return "closed";
            });
            // The screen's scroll arrows: {"steps":3} down the log, {"steps":-3} back up.
            AgentBridge.Register("log.scroll", o => ActivityLogPanel.ScrollBy((int?)o["steps"] ?? 1) ? ActivityLogPanel.Snapshot() : (object)"not open");
        }
    }
}
