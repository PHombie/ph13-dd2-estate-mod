using System.Collections.Generic;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the Ancestor's Memoirs for the dev bridge ({"cmd":"run","name":"memoirs.read","id":"plot_librarian_1"}):
    ///   memoirs.list [text=true]     the shelves: every entry with its category, its part of its slab, whether it can be read, and its words
    ///   memoirs.open / memoirs.close the statue's screen
    ///   memoirs.read id=..           opens a memoir's page and returns its words
    ///   memoirs.back                 from the page to the shelves
    ///   memoirs.unlock id=..         marks a story quest finished without playing it (its memoir appears)
    /// </summary>
    [EstateModule]
    internal static class MemoirsDev
    {
        private static void Register()
        {
            AgentBridge.Register("memoirs.list", o =>
            {
                var withText = (bool?)o["text"] ?? false;
                var entries = new List<object>();
                foreach (var entry in Memoirs.All())
                {
                    entries.Add(new
                    {
                        id = entry.Id,
                        category = entry.Category.Label,
                        title = entry.Title,
                        subtitle = entry.Subtitle,
                        part = entry.Part,
                        unlocked = entry.Unlocked,
                        icon = entry.Icon,
                        actor = entry.Actor,
                        text = withText ? entry.Text : null
                    });
                }
                return new { told = Memoirs.PlotDone, of = Memoirs.PlotCount, entries, screen = MemoirsPanel.Snapshot() };
            });
            AgentBridge.Register("memoirs.open", o =>
            {
                MemoirsPanel.Open();
                return MemoirsPanel.Snapshot();
            });
            AgentBridge.Register("memoirs.close", o =>
            {
                MemoirsPanel.Close();
                return "closed";
            });
            AgentBridge.Register("memoirs.read", o =>
            {
                var entry = Memoirs.Find((string)o["id"]);
                if (entry == null) return "no such memoir on the shelves (memoirs.list shows them)";
                var shown = MemoirsPanel.Read(entry.Id);
                return new { id = entry.Id, title = entry.Title, subtitle = entry.Subtitle, unlocked = entry.Unlocked, text = entry.Text, shown };
            });
            AgentBridge.Register("memoirs.back", o =>
            {
                MemoirsPanel.Back();
                return MemoirsPanel.Snapshot();
            });
            AgentBridge.Register("memoirs.unlock", o =>
            {
                var id = (string)o["id"];
                return QuestBoard.SetCompleted(id) ? (object)new { unlocked = id, told = Memoirs.PlotDone } : "no such story quest";
            });
        }
    }
}
