using System.Collections.Generic;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the tutorials for the dev bridge (python tools/bridge.py run tutorial.show id=torch).
    /// </summary>
    [EstateModule]
    internal static class TutorialDev
    {
        private static void Register()
        {
            // DD1's tutorials the estate knows of, in DD1's order: its delay, its moment in DD1, its moment here or why
            // it is left out, whether the install has its picture and words, and whether this estate has seen it.
            AgentBridge.Register("tutorial.list", o =>
            {
                var rows = new List<object>();
                foreach (var def in Tutorials.All)
                    rows.Add(new
                    {
                        id = def.Id, delay = def.Delay, dd1 = def.Dd1, estate = def.Mod, leftOut = def.LeftOut,
                        title = Tutorials.Title(def.Id), picture = Tutorials.Picture(def.Id),
                        words = (bool?)o["text"] ?? false ? Tutorials.Description(def.Id) : null,
                        seen = Tutorials.HasSeen(def.Id), waiting = Tutorials.IsQueued(def.Id)
                    });
                return rows;
            });
            // {"id":"torch"}: the window of any tutorial DD1 has words for, now, whether seen or left out; the estate's
            // record of what it has seen is not touched.
            AgentBridge.Register("tutorial.show", o =>
            {
                var id = (string)o["id"];
                if (string.IsNullOrEmpty(id)) return "which? {\"id\":\"torch\"}; tutorial.list has them";
                return TutorialPopup.Show(id) ? TutorialPopup.Describe() : (object)("DD1 has no tutorial '" + id + "' (or no DD1 install was found)");
            });
            AgentBridge.Register("tutorial.close", o =>
            {
                TutorialPopup.Close();
                return TutorialPopup.Describe();
            });
            // {"id":"torch"}: asked for as the game's moment asks: DD1's delay, once an estate, where it may come up.
            AgentBridge.Register("tutorial.ask", o =>
            {
                var id = (string)o["id"];
                if (Tutorials.Find(id) == null) return "no such tutorial";
                return Tutorials.Queue(id) ? "waiting" : Tutorials.HasSeen(id) ? "seen before" : Tutorials.Enabled ? "not asked for (it waits already, or no estate is on)" : "the tutorials are off";
            });
            // The estate has seen none ({"id":...}: not this one), and what the watcher saw last is forgotten, so the
            // screen that is open asks again when it is opened again.
            AgentBridge.Register("tutorial.reset", o => new { forgotten = Tutorials.Forget((string)o["id"]), state = Tutorials.Describe() });
            // What is shown, what waits, what has been seen. {"enabled":false} silences them for this run of the game
            // (a test that must not be stopped by a window); the option itself is [Rules] Tutorials of the config.
            AgentBridge.Register("tutorial.state", o =>
            {
                if (o["enabled"] != null) Tutorials.Silenced = !(bool)o["enabled"];
                if (o["blur"] != null) TutorialPopup.Blur = (bool)o["blur"];
                if (o["sigma"] != null) TutorialPopup.BlurSigma = (float)o["sigma"];
                return Tutorials.Describe();
            });
        }
    }
}
