using System.Collections.Generic;
using DD2Estate.Dev;
using DD2Estate.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands for what the town's screens share ({"cmd":"run","name":"confirm.state"}): DD1's confirm
    /// dialog, DD1's scroll bars, the estate bar's row of buttons and the hero in hand.
    /// </summary>
    [EstateModule]
    internal static class TownUiDev
    {
        private static void Register()
        {
            // DD1's dialog on the town's side: the question that stands and its answers.
            AgentBridge.Register("confirm.state", o => TownConfirm.Snapshot());
            // What a click on an answer does: {"index":0} the first ("Yes", "Still Embark"), 1 the second ...
            AgentBridge.Register("confirm.answer", o => TownConfirm.Answer((int?)o["index"] ?? 0) ? TownConfirm.Snapshot() : (object)"no such answer, or no question");
            // The dialog with any words, to look at it: {"question":"...","answers":["Yes","No","Always"]}; nothing happens on an answer.
            AgentBridge.Register("confirm.ask", o =>
            {
                var answers = new List<string>();
                if (o["answers"] is JArray given)
                    foreach (var answer in given) answers.Add((string)answer);
                if (answers.Count == 0) answers.AddRange(new[] { "Yes", "No" });
                TownConfirm.Ask(null, (string)o["question"] ?? "This will dismiss the hero permanently. Are you sure you want to dismiss this hero?", answers.ToArray(), null);
                return TownConfirm.Snapshot();
            });
            AgentBridge.Register("confirm.close", o =>
            {
                TownConfirm.Close();
                return "closed";
            });

            // Every scroll bar of the hamlet's screens as it stands: whose it is, where its rail lies on DD1's
            // screen (corner and size in the canvas's pixels from the top left), whether it is shown and how far down
            // its list is.
            AgentBridge.Register("ui.scrollbars", o => ScrollBars());
            // What a click on an arrow does: {"index":0,"steps":2} two steps down, {"steps":-1} one up.
            AgentBridge.Register("ui.scroll", o =>
            {
                var bars = Bars();
                var index = (int?)o["index"] ?? 0;
                if (index < 0 || index >= bars.Length) return "no such scroll bar";
                var steps = (int?)o["steps"] ?? 1;
                for (var i = 0; i < Mathf.Abs(steps); i++) bars[index].Nudge(steps > 0 ? 1 : -1);
                return ScrollBars();
            });

            // The hero in the player's hand, as while a roster row is dragged: {"guid":123} picks one up without the
            // mouse (an open Tavern, Abbey or Sanitarium shows DD1's frames on its free slots), {"guid":0} lets go.
            AgentBridge.Register("roster.hold", o =>
            {
                if (o["guid"] != null) HeroDrag.HoldForTest((uint)o["guid"]);
                return new { inHand = HeroDrag.InHand };
            });

            // The estate bar's row: {"click":"town_event"} does what a click on that button does.
            AgentBridge.Register("bar.state", o =>
            {
                var click = (string)o["click"];
                return click != null ? new { clicked = TownPanelButtons.Click(click), bar = TownPanelButtons.Describe() } : TownPanelButtons.Describe();
            });
        }

        private static Dd1ScrollBar[] Bars()
        {
            var canvas = RosterPanel.CanvasRoot;
            return canvas != null ? canvas.GetComponentsInChildren<Dd1ScrollBar>(false) : new Dd1ScrollBar[0];
        }

        private static object ScrollBars()
        {
            var canvas = RosterPanel.CanvasRoot as RectTransform;
            var list = new List<object>();
            var bars = Bars();
            var corners = new Vector3[4];
            for (var i = 0; i < bars.Length; i++)
            {
                var bar = bars[i];
                bar.Rail.GetWorldCorners(corners);      // 0 = lower left, 2 = upper right
                var low = canvas.InverseTransformPoint(corners[0]);
                var high = canvas.InverseTransformPoint(corners[2]);
                var area = canvas.rect;
                list.Add(new
                {
                    index = i, list = bar.name, screen = bar.transform.parent != null ? bar.transform.parent.name : null,
                    railX = low.x - area.xMin, railTop = area.yMax - high.y, railWidth = high.x - low.x, railHeight = high.y - low.y, bar = bar.Snapshot()
                });
            }
            return list;
        }
    }
}
