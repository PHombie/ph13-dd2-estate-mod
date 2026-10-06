using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using DD2Estate.Dev;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Test commands of the roster lifecycle for the dev bridge (python tools/bridge.py run roster.state ...).
    /// They do what the screens do, without the screens.
    /// </summary>
    [EstateModule]
    internal static class RosterDev
    {
        public static void Register()
        {
            AgentBridge.Register("roster.state", o => Describe());
            AgentBridge.Register("roster.hire", o =>
            {
                var offer = StageCoach.Offer;
                var cls = (string)o["cls"];
                var path = (string)o["path"];
                var recruit = cls != null ? offer.FirstOrDefault(r => r.ClassId == cls && (path == null || StageCoach.PathOf(r) == path)) : offer.ElementAtOrDefault((int?)o["index"] ?? 0);
                if (recruit == null) return "no such recruit this week";
                return StageCoach.Hire(recruit) ? "hired the " + recruit.ClassId + " (" + StageCoach.PathOf(recruit) + ")" : "not hired: " + (StageCoach.HireBlockReason(recruit) ?? "see the log");
            });
            AgentBridge.Register("roster.dismiss", o =>
            {
                var guid = (uint)o["guid"];
                if ((bool?)o["ask"] ?? false)
                {
                    RosterLifecycle.RequestDismiss(guid);
                    return "asked";
                }
                return RosterLifecycle.Dismiss(guid) ? "dismissed" : "not dismissed: " + RosterLifecycle.DismissBlockReason(guid);
            });
            // {"cls":"highwayman","path":"highwayman_sharpshot","tier":0}: path and tier are optional.
            AgentBridge.Register("roster.offer", o => StageCoach.OfferForTest((string)o["cls"], (int?)o["tier"] ?? 0, (string)o["path"]));
            // Every class of this install with its paths, the game's names beside the ids.
            AgentBridge.Register("roster.paths", o => RosterLifecycle.AvailableClasses()
                .Select(c => c + ": " + string.Join(", ", HeroPaths.Of(c).Select(p => p + " (" + HeroPaths.Name(p, c) + ")"))).ToList());
            AgentBridge.Register("roster.kill", o => RosterLifecycle.KillForTest((uint)o["guid"]));
            AgentBridge.Register("roster.bury", o => RosterLifecycle.BuryDead() + " buried");
            AgentBridge.Register("roster.week", o =>
            {
                // What the end of an expedition does to the calendar, without the expedition.
                EstateState.Current.AdvanceWeek();
                return StageCoach.Describe();
            });
            AgentBridge.Register("roster.open", o =>
            {
                Buildings.Open((string)o["building"] ?? StageCoach.BuildingId);
                return RosterWindow.IsOpen ? "open" : "not open";
            });
            // The hero a window works on (Guild, Blacksmith, Survivalist; a slot's hero in the others), as a roster click gives it.
            AgentBridge.Register("window.pick", o => RosterWindow.Pick((uint)o["guid"]) ? RosterWindow.Current.Snapshot() : (object)"no window that takes heroes is open");
            AgentBridge.Register("window.state", o => RosterWindow.Current != null ? RosterWindow.Current.Snapshot() : (object)"no window is open");
            AgentBridge.Register("roster.close", o =>
            {
                RosterWindow.Close();
                return "closed";
            });
        }

        public static object Describe()
        {
            var heroes = new List<object>();
            var roster = RosterLifecycle.Roster;
            if (EstateSession.Active && roster != null)
            {
                var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
                // Every entry, the dead included, so a test can see what the game keeps.
                foreach (var guid in EstateSession.RosterGuids())
                {
                    var entry = roster.GetReadOnlyRosterEntryByActorGuid(guid);
                    var actor = actors.GetLibraryElement(guid);
                    heroes.Add(new
                    {
                        guid,
                        cls = entry?.ActorClassId,
                        status = entry?.GetRosterStatus()?.GetName(),
                        name = actor?.ActorName,
                        actor = actor != null ? actor.ActorDataId : null,
                        path = actor != null ? HeroPaths.Of(actor) : null,
                        living = actor != null && actor.IsLiving,
                        quirks = actor?.QuirkContainer?.GetIds(),
                        dismissBlocked = RosterLifecycle.DismissBlockReason(guid)
                    });
                }
            }
            return new
            {
                active = EstateSession.Active,
                replacement = roster?.RosterReplacementType?.GetName(),
                classes = RosterLifecycle.AvailableClasses(),
                seats = RosterLifecycle.SeatsInAll(),
                heroes,
                stageCoach = StageCoach.Describe(),
                fallen = Graveyard.All.Select(f => new { name = f.Name, cls = f.ClassId, week = f.Week, where = f.Where, by = f.By }).ToList()
            };
        }
    }
}
