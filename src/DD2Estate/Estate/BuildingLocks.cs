using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1 opens its town as the estate earns it. Every building's file names what it asks
    /// (campaign/town/buildings/&lt;id&gt;/&lt;id&gt;.building.json, "requirements": number_of_quests_finished and
    /// highest_dungeon_level; stock: the tavern and the abbey 2 quests, the blacksmith and the guild 3, the sanitarium
    /// 4, the survivalist a dungeon at level 2, the others nothing). Until then the building stands boarded up
    /// (the hamlet's locked art), says "Complete more quests to unlock" under its name (str_locked_building_summary)
    /// and does not open. When it opens the Activity Log says so (str_&lt;id&gt;_unlocked).
    ///
    /// What has opened stays open and is kept in the estate's save; an estate saved before this existed keeps
    /// everything open, as it had it.
    /// </summary>
    [EstateModule]
    internal static class BuildingLocks
    {
        private const string SectionKey = "building_locks";
        private const float CheckSeconds = 0.5f;

        // FALLBACK: DD1's stock requirements (quests finished, highest dungeon level), should a file not be readable.
        private static readonly Dictionary<string, int[]> Stock = new Dictionary<string, int[]>
        {
            ["tavern"] = new[] { 2, 0 }, ["abbey"] = new[] { 2, 0 }, ["blacksmith"] = new[] { 3, 0 }, ["guild"] = new[] { 3, 0 },
            ["sanitarium"] = new[] { 4, 0 }, ["camping_trainer"] = new[] { 0, 2 }
        };

        private static readonly Dictionary<string, int[]> Needs = new Dictionary<string, int[]>();
        private static readonly HashSet<string> Opened = new HashSet<string>();
        private static bool _everything;
        private static bool _stale = true;      // the hamlet has not been told of the locks as they are now

        /// <summary>Raised when a building opened (or the locks were set anew).</summary>
        public static event Action Changed;

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey,
                () => new JObject { ["opened"] = new JArray(Opened.OrderBy(id => id)), ["everything"] = _everything },
                Load,
                () => { Opened.Clear(); _everything = false; _stale = true; });

            var go = new GameObject("DD2Estate.BuildingLocks") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();

            // What each building asks and whether it is open. {"reset":true}: the locks as DD1's rules have them for
            // this estate's quests and dungeon levels (an older estate's "everything open" is dropped);
            // {"open":"all"} or {"open":"tavern"}: opened without asking.
            AgentBridge.Register("locks.state", o =>
            {
                if (o["reset"] != null && (bool)o["reset"])
                {
                    Opened.Clear();
                    _everything = false;
                    Sync(false);
                }
                var open = (string)o["open"];
                if (open == "all") _everything = true;
                else if (open != null) Opened.Add(open);
                if (o["reset"] != null || open != null) Changed?.Invoke();
                return new
                {
                    questsFinished = QuestBoard.QuestsFinished, highestDungeonLevel = HighestDungeonLevel(), everything = _everything,
                    buildings = HamletScene.BuildingIds.Select(id => new { id, quests = Need(id)[0], dungeonLevel = Need(id)[1], locked = Locked(id) }).ToArray()
                };
            });
        }

        private static void Load(JToken json)
        {
            Opened.Clear();
            // no section: an estate from before the locks, which had every door open
            _everything = json == null || ((bool?)json["everything"] ?? false);
            if (json?["opened"] is JArray opened)
                foreach (var id in opened) Opened.Add((string)id);
            _stale = true;
        }

        /// <summary>The building has not been earned yet: boarded up, and it does not open.</summary>
        public static bool Locked(string building) => !_everything && !Opened.Contains(building) && !Earned(building);

        /// <summary>What a locked building says under its name.</summary>
        public static string Summary => Dd1Strings.Get("str_locked_building_summary") is string words ? Dd1Strings.Plain(words) : "Complete more quests to unlock";

        private static bool Earned(string building)
        {
            var need = Need(building);
            return QuestBoard.QuestsFinished >= need[0] && (need[1] <= 0 || HighestDungeonLevel() >= need[1]);
        }

        private static int[] Need(string building)
        {
            if (Needs.TryGetValue(building, out var known)) return known;
            var need = Stock.TryGetValue(building, out var stock) ? stock : new[] { 0, 0 };
            try
            {
                var text = Dd1Install.Found ? Dd1Install.ReadText("campaign/town/buildings/" + building + "/" + building + ".building.json") : null;
                var asked = text != null ? JObject.Parse(text)["requirements"] : null;
                if (asked != null) need = new[] { (int?)asked["number_of_quests_finished"] ?? 0, (int?)asked["highest_dungeon_level"] ?? 0 };
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hamlet: what the " + building + " asks before it opens could not be read, DD1's stock values stand: " + e.Message);
            }
            return Needs[building] = need;
        }

        private static int HighestDungeonLevel()
        {
            var highest = 0;
            foreach (var dungeon in QuestBoard.Generation.Dungeons) highest = Math.Max(highest, QuestBoard.DungeonLevel(dungeon.Id));
            return highest;
        }

        // A building that has earned its opening is opened for good; the log is told, except of those that ask nothing.
        private static void Sync(bool tell)
        {
            var changed = false;
            foreach (var id in HamletScene.BuildingIds)
            {
                if (Opened.Contains(id) || !Earned(id)) continue;
                Opened.Add(id);
                changed = true;
                var need = Need(id);
                if (!tell || _everything || (need[0] <= 0 && need[1] <= 0)) continue;
                var name = ActivityText.Building(id);
                ActivityLog.Add(ActivityLog.Kinds.Building, ActivityLogText.Story("str_" + id + "_unlocked", "%s: The %s is now unlocked.", name, name), null, id);
                Plugin.Log.LogInfo("Hamlet: the " + id + " is open (" + QuestBoard.QuestsFinished + " quests finished, highest dungeon level " + HighestDungeonLevel() + ")");
            }
            if (changed || _stale) Changed?.Invoke();
            _stale = false;
        }

        private class Watcher : MonoBehaviour
        {
            private float _next;

            private void Update()
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + CheckSeconds;
                if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return;
                try { Sync(true); }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Hamlet: the buildings' locks failed: " + e);
                    enabled = false;
                }
            }
        }
    }
}
