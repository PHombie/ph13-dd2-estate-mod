using System.Collections;
using System.Linq;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Estate;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// What an expedition does differently when its quest is one of the Darkest Dungeon's five stand-ins
    /// (<see cref="DarkestBosses"/>): it is nothing but its fight.
    ///
    ///   its map            one room, the boss's (Core/DarkestBossRules.FightMap); it is never shown;
    ///   the way in         the loading screen is up as for any quest; where it would lift off the first room it
    ///                      hands the party to its fight instead (<see cref="FightFromLoading"/>): in DD2's look
    ///                      the picture has gone back into the game's black, which the game lifts off the arena;
    ///                      in DD1's look the picture stays until the game's black has closed over it. Without a
    ///                      loading screen (no DD1 picture) the fight begins as soon as the hub is in;
    ///   the fight          the room's own: the quest's boss (DungeonContent.PickBoss), told its act and its tier
    ///                      (Difficulty.Apply), with no surprise on either side (DD1's bosses);
    ///   the way out        won, the quest is complete and ends at once (the expedition's own end: stress, pay,
    ///                      bag, experience, the week, DD1's results screens); fled, it is abandoned; lost, failed.
    ///                      The hub comes back under the mod's black sheet (EstateSession: the expedition does not
    ///                      "stay where it fought"), and what comes up out of it is the results.
    ///   loot               nothing is laid out on a scroll, for want of a room to stand in: what the fallen wore
    ///                      and what the fight paid (nothing, in the Darkest Dungeon) go into the bag and home.
    ///
    ///   the save           the expedition is saved as it begins (any other is in the save from its first room on):
    ///                      a game left in the middle of the fight is continued behind the save's loading screen
    ///                      and straight into the fight again, as it began. (Nothing is saved during a fight.)
    /// </summary>
    internal partial class DungeonRun
    {
        private bool _bossFightWatched;
        private string _bossFightHow = "";

        /// <summary>The expedition's quest is one of the Darkest Dungeon's five stand-ins.</summary>
        public bool IsBossQuest => DarkestBosses.IsOurs(_quest);

        // the fight has yet to be won, and the quest has not ended some other way
        private bool BossFightDue => IsBossQuest && _map.Rooms.Count == 1 && _map.Rooms[0].Battle != null && !_x.IsRoomFightWon(0) && _x.Status == RaidStatus.InProgress;

        private bool BossFightCanStart => BossFightDue && !_fighting && !_restored && !_processing && _camp == null && _x.Pending == PendingKind.None && EstateSession.InHub;

        // Called as the expedition begins (fresh) and as one out of a save is put up.
        private void BeginBossQuest(bool fresh)
        {
            if (_bossFightWatched || !BossFightDue) return;
            _bossFightWatched = true;
            // Any other expedition is in the save from its first room on. This one has no room to come to, so it
            // is saved as it begins: a game left in the middle of the fight is continued into that fight (the
            // party has set out, as on any quest), not in the hamlet of before with the provisions unbought.
            if (fresh && EstateSession.InHub)
            {
                try { EstatePersistence.SaveNow("boss quest begun"); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Dungeon: " + _quest?.Id + " could not be saved as it began: " + e.Message); }
            }
            Plugin.Host.StartCoroutine(BossFightWhenNothingBringsIt());
        }

        // The loading screen sends the party into its fight as it leaves, and a save caught in the fight asks for
        // it again by itself. This is for when neither does: no loading screen could be shown, or it went down
        // without handing over.
        private IEnumerator BossFightWhenNothingBringsIt()
        {
            while (Current == this && BossFightDue && !_fighting && _x.Pending == PendingKind.None && (!BossFightCanStart || LoadingScreen.IsUp)) yield return null;
            if (Current == this && BossFightCanStart) StartBossFight("no loading screen brought it");
        }

        /// <summary>
        /// The loading screen is about to leave (LoadingScreen.Leave / LeaveDd2). True: the expedition is nothing
        /// but its fight and has gone into it; the screen does not lift off a room.
        /// </summary>
        public bool FightFromLoading()
        {
            return BossFightCanStart && StartBossFight("from under the loading screen");
        }

        private bool StartBossFight(string how)
        {
            var events = _x.StartFight(null);
            var fight = events.OfType<FightStarts>().FirstOrDefault();
            if (fight == null) return false;
            // the room's own fight, the boss's (the exploration takes a fight it did not place for an ordinary one)
            fight.Slot = _map.Rooms[0].Battle;
            _bossFightHow = how;
            Plugin.Log.LogInfo("Dungeon: " + _quest.Id + ": straight into its fight (" + how + ")");
            // no pause before it: there is no room to be looked at
            _fightAtDoor = true;
            Enqueue(events);
            return _fighting;
        }

        // What the fallen wore and what the fight paid go straight into the bag (a trinket it has no room for goes
        // home, as at an expedition's end): there is no room to lay DD1's scroll out in.
        private void StowBossFightLoot()
        {
            ClaimFallenGear(_fightOutcome != FightOutcome.Lost);
            FindBattleLoot();
            foreach (var stack in _finds.SelectMany(find => find.Stacks))
                if (stack.Amount > 0 && _bag.Add(stack.Item, stack.Amount) > 0 && stack.Item.Type == ItemTypes.Trinket) Trinkets.Put(stack.Item.Id);
            _finds.Clear();
        }

        /// <summary>Dev bridge: where the stand-in's expedition stands.</summary>
        internal object DescribeBossQuest()
        {
            return new
            {
                quest = _quest?.Id, boss = _quest?.Boss, fightDue = BossFightDue, fighting = _fighting, wentIn = _bossFightHow,
                status = _x.Status.ToString(), pending = _x.Pending.ToString(), complete = _x.ObjectiveComplete, light = _x.Light,
                loadingScreen = LoadingScreen.IsUp
            };
        }
    }
}
