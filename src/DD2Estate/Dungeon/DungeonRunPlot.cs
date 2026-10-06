using System.Collections;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What an expedition does differently when its quest is one of DD1's plot quests outside the story
    /// (<see cref="PlotQuests"/>: the Brigand Incursion, the Shrieker's perch):
    ///
    ///   a map of one room      DD1's perch (maps/crow_map1.dm) is one room with its fight: the fight begins as
    ///                          the party arrives, and it is the whole quest;
    ///   the Shrieker's fight   lasts DD1's four rounds and pays the nest's loot only when won outright
    ///                          (<see cref="Shrieker"/>);
    ///   fleeing such a fight   is abandoning the quest (DD1: retreat_always_from_raid);
    ///   the room's picture     DD1 has an entrance of its own for these quests
    ///                          (weald.entrance_room_wall.plot_trinket_retention_0.png).
    ///
    /// The Incursion needs none of this: its map is walked like any other and its boss waits in the map's
    /// final room.
    /// </summary>
    internal partial class DungeonRun
    {
        /// <summary>A moment to see the perch before its fight begins. The mod's own number (GUESS).</summary>
        public static float ArrivalPause = 1.2f;

        private bool _plotFightAsked;

        /// <summary>The expedition's quest by its id; null for a free expedition.</summary>
        public string QuestId => _quest?.Id;

        private PlotEventQuest Plot => PlotQuests.Find(_quest);

        // (one of the Darkest Dungeon's five stand-ins is nothing but its fight too: DungeonRunBoss.cs)
        private bool FleesTheQuest => _fightOutcome == FightOutcome.Retreated && ((Plot?.RetreatAlwaysFromRaid ?? false) || IsBossQuest);

        private void BeginPlotQuest()
        {
            if (_plotFightAsked || Plot == null) return;
            if (_map.Rooms.Count != 1 || _map.Rooms[0].Battle == null || _x.IsRoomFightWon(0) || _x.Status != RaidStatus.InProgress) return;
            _plotFightAsked = true;
            Plugin.Host.StartCoroutine(FightOnArrival());
        }

        private IEnumerator FightOnArrival()
        {
            // not under the loading picture, and not before the room stands
            while (Current == this && (!EstateSession.InHub || LoadingScreen.IsUp || CorridorView.TransitionRunning || Busy)) yield return null;
            var until = Time.unscaledTime + ArrivalPause;
            while (Current == this && Time.unscaledTime < until) yield return null;
            if (Current != this || !EstateSession.InHub || _x.Status != RaidStatus.InProgress || _x.Pending != PendingKind.None || _x.IsRoomFightWon(0)) yield break;
            // the quest's monster by DD1's name: the data's wanderer of that name is its fight
            var monster = DungeonContent.WandererDd1(_quest?.Boss) ?? ShriekerRules.Monster;
            Plugin.Log.LogInfo("Dungeon: " + _quest?.Id + ": the party has arrived; the fight (" + monster + ") begins");
            Enqueue(_x.StartFight(monster));
        }

        // DD1's own pictures of a plot quest's first and last room, before the dungeon's.
        private void PlotRoomWalls(Room room, List<string> candidates)
        {
            var id = Plot?.Id;
            if (id == null) return;
            if (room.Wall == "entrance") candidates.Insert(0, "entrance_room_wall." + id);
            else if (room.Type == RoomType.Boss) candidates.Insert(0, "final_room_wall." + id);
        }

        private void PlotFightBegins(string boss)
        {
            // (a count left running by a fight that never came back must not end another fight)
            Shrieker.FightEnded();
            if (boss != Shrieker.BossId) return;
            Shrieker.FightBegins(QuestDifficulty);
            // DD1: the Shrieker leaves nothing itself (loot NONE); its nest pays when it is broken up
            _fightDraws = Shrieker.NestLoot(QuestDifficulty);
        }

        /// <summary>The hub is back from a fight. True when the fight ended by the plot quest's own rule, which is said here.</summary>
        private bool PlotFightOver()
        {
            if (!Shrieker.FightEnded()) return false;
            // the nest was not broken up: it pays nothing
            _fightDraws = new List<LootDraw>();
            Say("The Shrieker takes wing. What it kept lies where it left it.");
            return true;
        }
    }
}
