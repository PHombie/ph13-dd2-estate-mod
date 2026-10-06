using System.Collections.Generic;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands for the dev bridge ({"cmd":"run","name":"quests.select","index":1}); they do what the
    /// estate map does, without the mouse, and show the quest board's rules at work. quests.list,
    /// quests.embark and quests.provision are older and live with the other commands.
    /// </summary>
    [EstateModule]
    internal static class QuestMapDev
    {
        private static void Register()
        {
            // The map's state: regions (open, level, share of the bar, why locked), offers with their rewards, the party check.
            AgentBridge.Register("quests.map", o => QuestPanel.Describe());
            // Opens the map as the hamlet's Embark button does (again: redraws it after a change below).
            AgentBridge.Register("quests.open", o => QuestPanel.DevOpen() ? QuestPanel.Describe() : "the hamlet is not open");
            AgentBridge.Register("quests.close", o => QuestPanel.DevPress("back"));
            AgentBridge.Register("quests.select", o => QuestPanel.SelectOffer((int?)o["index"] ?? 0) ? QuestPanel.Describe() : "the map is not open, or no such offer");
            // "Provision": false while the party check fails (nobody goes, or a hero refuses the quest). A party DD1
            // has a question for (short of four, no retreat, trinkets left in the stores) gets the question in
            // DD1's dialog; the next call answers it and all that would follow with "Still Embark". quests.map
            // tells the question that is up ("question").
            AgentBridge.Register("quests.forward", o => QuestPanel.DevPress("provision"));

            // DD1's rules in force and where the estate stands by them: quests finished, the row of the
            // quests-per-visit table, each region's gate, points, level and the quest types of that level, the
            // difficulties the roster calls for, and what the last roll could not offer.
            AgentBridge.Register("quests.rules", o => QuestBoard.Describe());
            // This week's offers, one line each, with what they pay: {"dungeon":"crypts"} narrows it to a region.
            AgentBridge.Register("quests.offers", o => Offers((string)o["dungeon"]));
            // Rolls the offers again. {"advance":true} first lets a week pass, as an expedition would (the
            // wagon restocks, the coach returns, activities end); {"finished":5} sets the quests finished first.
            AgentBridge.Register("quests.roll", o =>
            {
                if (o["finished"] != null) QuestBoard.SetFinished((int)o["finished"]);
                if ((bool?)o["advance"] ?? false) EstateState.Current.AdvanceWeek();
                QuestBoard.Reroll();
                QuestBoard.Current();
                if (QuestPanel.IsOpen) QuestPanel.DevOpen();
                return Offers((string)o["dungeon"]);
            });
            // Ends an offer without playing it: {"index":0} pays it and counts it, {"index":0,"success":false}
            // fails it; a week passes either way, as after an expedition ({"week":false}: it does not).
            AgentBridge.Register("quests.finish", o =>
            {
                var offers = QuestBoard.Current();
                var index = (int?)o["index"] ?? 0;
                if (index < 0 || index >= offers.Count) return "no such offer";
                var quest = offers[index];
                var success = (bool?)o["success"] ?? true;
                QuestBoard.Finished(quest, success);
                if ((bool?)o["week"] ?? true) EstateState.Current.AdvanceWeek();
                if (QuestPanel.IsOpen) QuestPanel.DevOpen();
                return new { quest = quest.Id, success, report = QuestBoard.PendingNarration, rules = QuestBoard.Describe() };
            });
            // Region levels and story progress without playing: {"dungeon":"crypts","done":7} sets the region's
            // points (DD1: a short quest counts 2, a medium 3, a long 4), {"finished":5} the quests finished on
            // the estate (DD1 opens regions by it), {"id":"plot_librarian_1"} finishes a story quest.
            AgentBridge.Register("quests.progress", o =>
            {
                if (o["dungeon"] != null || o["done"] != null) QuestBoard.SetDone((string)o["dungeon"] ?? "crypts", (int?)o["done"] ?? 0);
                if (o["finished"] != null) QuestBoard.SetFinished((int)o["finished"]);
                if (QuestPanel.IsOpen) QuestPanel.DevOpen();
                return QuestPanel.Describe();
            });
            AgentBridge.Register("quests.complete", o =>
            {
                if (!QuestBoard.SetCompleted((string)o["id"])) return "no such story quest";
                if (QuestPanel.IsOpen) QuestPanel.DevOpen();
                return QuestPanel.Describe();
            });
        }

        private static object Offers(string only)
        {
            var lines = new List<object>();
            var offers = QuestBoard.Current();
            for (var i = 0; i < offers.Count; i++)
            {
                var q = offers[i];
                if (only != null && q.Dungeon != only) continue;
                lines.Add(new
                {
                    index = i, id = q.Id, name = q.Name, dungeon = q.Dungeon, type = q.Type, length = q.LengthName, tier = q.Tier, plot = q.Plot,
                    boss = q.Boss, goal = q.Goal, countsForRegion = q.DungeonXp, canRetreat = q.CanRetreat, retreatDeaths = q.RetreatDeaths,
                    clearsStress = q.ClearsStress, reward = QuestBoard.RewardText(q)
                });
            }
            return new { week = EstateState.Current.Week, wanted = QuestBoard.Wanted, offered = offers.Count, notOffered = QuestBoard.Notes, offers = lines };
        }
    }
}
