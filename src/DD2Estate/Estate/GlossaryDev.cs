using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Glossary for the dev bridge ({"cmd":"run","name":"glossary.scroll","lines":3}); they
    /// open the screen and move its list without the mouse, and say which of DD1's terms the estate shows.
    /// </summary>
    [EstateModule]
    internal static class GlossaryDev
    {
        private static void Register()
        {
            // The terms shown, in DD1's order, and those left out with the list that leaves them out (DD1's rules, a
            // DLC, undecided, in no list); {"text":true} adds DD1's definitions. The screen and the bar's buttons.
            AgentBridge.Register("glossary.state", o => new
            {
                glossary = Glossary.Describe((bool?)o["text"] ?? false), screen = GlossaryPanel.Snapshot(), bar = TownPanelButtons.Describe()
            });
            AgentBridge.Register("glossary.open", o => GlossaryPanel.Open() ? GlossaryPanel.Snapshot() : (object)(TownPanel.BlockReason() ?? "not opened"));
            AgentBridge.Register("glossary.close", o =>
            {
                GlossaryPanel.Close();
                return "closed";
            });
            // Moves the list by lines of a definition's text: {"lines":3} down, {"lines":-3} back up (a click on a
            // scroll arrow is three lines).
            AgentBridge.Register("glossary.scroll", o => GlossaryPanel.ScrollBy((int?)o["lines"] ?? 1) ? GlossaryPanel.Snapshot() : (object)"not open");
        }
    }
}
