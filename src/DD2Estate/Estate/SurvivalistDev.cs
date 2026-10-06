using System.Collections.Generic;
using DD2Estate.Dev;
using DD2Estate.Dungeon;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Survivalist for the dev bridge (python tools/bridge.py run survivalist.state guid=3,
    /// ... run survivalist.learn guid=3 skill=zealous_vigil, ... run survivalist.toggle guid=3 skill=encourage).
    /// They do what the screen does, without the mouse. The Bonfire tree is the upgrade commands'
    /// (upgrade.buy tree=camping_trainer.cost).
    /// </summary>
    [EstateModule]
    internal static class SurvivalistDev
    {
        private static void Register()
        {
            AgentBridge.Register("survivalist.open", o =>
            {
                SurvivalistPanel.Open();
                return RosterWindow.IsOpen ? "open" : "not open";
            });
            AgentBridge.Register("survivalist.close", o =>
            {
                RosterWindow.Close();
                return "closed";
            });
            // A hero's camping skills with prices and what each does: {"guid":3}.
            AgentBridge.Register("survivalist.state", o => Survivalist.Describe((uint)o["guid"]));
            // The whole roster in short.
            AgentBridge.Register("survivalist.roster", o =>
            {
                var heroes = new List<object>();
                foreach (var guid in RosterLifecycle.LivingGuids())
                {
                    var book = Survivalist.Book(guid);
                    heroes.Add(new { guid, cls = book.ClassId, known = string.Join(",", book.Known), ready = string.Join(",", book.Active) });
                }
                return new
                {
                    gold = EstateState.Gold,
                    discount = UpgradeRules.Discount(Survivalist.CostTree),
                    readyLimit = CampContent.Rules.ActiveLimit,
                    heroes
                };
            });
            AgentBridge.Register("survivalist.learn", o => Survivalist.Learn((uint)o["guid"], (string)o["skill"]) ?? "learned");
            AgentBridge.Register("survivalist.toggle", o => Survivalist.Toggle((uint)o["guid"], (string)o["skill"]) ?? Survivalist.Describe((uint)o["guid"]));
            // Every skill of the hero's class, free: {"guid":3}.
            AgentBridge.Register("survivalist.grant", o => Survivalist.GrantAll((uint)o["guid"]));
        }
    }
}
