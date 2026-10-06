using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>What the Darkest Dungeon is on this estate (the setting [Rules] DarkestDungeon).</summary>
    public enum DarkestMode
    {
        /// <summary>For the time being: five quests in a row, each nothing but the fight with the final boss of one of DD2's Confessions.</summary>
        Bosses,
        /// <summary>DD1's Darkest Dungeon as the mod has it: the descents behind the story quests, DD1's rules of those who go down.</summary>
        Dd1,
        /// <summary>On the map and shut: nothing of it is offered.</summary>
        Shut
    }

    /// <summary>Where one of the five stands on the Estate Map.</summary>
    public enum DarkestBossPlace
    {
        /// <summary>Not yet: the one before it has not been won.</summary>
        Locked,
        /// <summary>On offer this week.</summary>
        Open,
        /// <summary>Won. It does not come back.</summary>
        Won
    }

    /// <summary>
    /// The plain rules of the temporary Darkest Dungeon (the owner, 2026-10-06: "simply 5 dungeons, opening one
    /// after another as they are completed; one is available from the start; each at once starts the battle
    /// with the final bosses of a Confession"): which setting means what, the order of the five, and which of
    /// them is on offer.
    ///
    /// The five are named by DD2's own ids of its Confessions' bosses ("brain", "lungs", "eyes", "arms",
    /// "body"), and the order is the game's own: each of its boss rows names the one that has to be won before
    /// it (m_PrerequisiteBossVictoryIds of boss_data_export).
    /// </summary>
    public static class DarkestBossRules
    {
        public const string Bosses = "bosses", Dd1 = "dd1", Shut = "shut";
        public const string DefaultSetting = Bosses;

        /// <summary>What every one of the five quests' ids begins with; the rest is the Confession's id.</summary>
        public const string QuestPrefix = "darkest_boss_";

        /// <summary>
        /// The setting's meaning. Anything that is not one of the three words is the default
        /// (<paramref name="known"/> false: the caller says so in the log).
        /// </summary>
        public static DarkestMode ParseMode(string setting, out bool known)
        {
            known = true;
            switch ((setting ?? "").Trim().ToLowerInvariant())
            {
                case Bosses: return DarkestMode.Bosses;
                case Dd1: return DarkestMode.Dd1;
                case Shut: return DarkestMode.Shut;
                default:
                    known = false;
                    return DarkestMode.Bosses;
            }
        }

        public static string NameOf(DarkestMode mode) => mode == DarkestMode.Dd1 ? Dd1 : mode == DarkestMode.Shut ? Shut : Bosses;

        public static string QuestId(string act) => QuestPrefix + act;

        /// <summary>The Confession a quest id stands for; null for any other quest.</summary>
        public static string ActOf(string questId)
        {
            return questId != null && questId.Length > QuestPrefix.Length && questId.StartsWith(QuestPrefix, StringComparison.Ordinal) ? questId.Substring(QuestPrefix.Length) : null;
        }

        /// <summary>
        /// The order the game plays its Confessions in: one is placed when everything it waits for
        /// (<paramref name="prerequisites"/>, as far as it is among <paramref name="acts"/>) is placed. Among
        /// those that could come next, the list's own order decides. What can never be placed (two that wait
        /// for each other) follows at the end, in the list's order: nothing is lost and nothing hangs.
        /// </summary>
        public static List<string> Order(IEnumerable<string> acts, Func<string, IEnumerable<string>> prerequisites)
        {
            var left = (acts ?? new string[0]).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
            var all = new HashSet<string>(left);
            var order = new List<string>();
            while (left.Count > 0)
            {
                var next = left.FirstOrDefault(act => (prerequisites?.Invoke(act) ?? new string[0]).All(before => before == act || !all.Contains(before) || order.Contains(before)));
                if (next == null) break;
                order.Add(next);
                left.Remove(next);
            }
            order.AddRange(left);
            return order;
        }

        /// <summary>
        /// The map of a quest that is nothing but its fight: one room, the boss's, which is where the party
        /// stands. The expedition's own rules do the rest: the fight won is the quest's goal met
        /// (<see cref="ObjectiveKind.KillBoss"/>), a party that flees has abandoned it
        /// (<see cref="Exploration.FleeRaid"/>), a party lost has failed it. Nothing of the room is ever walked.
        /// </summary>
        public static DungeonMap FightMap(string dungeonId, int tier, long seed)
        {
            var map = new DungeonMap { DungeonId = dungeonId, QuestType = QuestTypes.KillBoss, Length = 1, Tier = tier, Seed = seed, Width = 1, Height = 1 };
            map.Rooms.Add(new Room { Id = 0, X = 0, Y = 0, Type = RoomType.Boss, Battle = new EncounterSlot { Kind = EncounterKind.Boss, Tier = tier }, Wall = "entrance" });
            map.EntranceId = 0;
            map.FinalRoomId = 0;
            map.Objective = new QuestObjective { Kind = ObjectiveKind.KillBoss, Required = 1 };
            return map;
        }
    }

    /// <summary>
    /// How far an estate has come with the five: the ones it has won. One is on offer at a time, the first of
    /// the order that has not been won; a won one is never offered again. Kept in the estate's save; an estate
    /// saved before there were any has won none and starts at the first.
    /// </summary>
    public sealed class DarkestBossProgress
    {
        private readonly HashSet<string> _won = new HashSet<string>();

        public IReadOnlyCollection<string> Won => _won;

        public bool IsWon(string act) => act != null && _won.Contains(act);

        /// <summary>The one on offer; null when every one of the order is won (or the order is empty).</summary>
        public string Next(IReadOnlyList<string> order)
        {
            if (order == null) return null;
            foreach (var act in order)
                if (!_won.Contains(act)) return act;
            return null;
        }

        /// <summary>How many of the order are won.</summary>
        public int Count(IReadOnlyList<string> order) => order == null ? 0 : order.Count(_won.Contains);

        public DarkestBossPlace PlaceOf(IReadOnlyList<string> order, string act)
        {
            if (IsWon(act)) return DarkestBossPlace.Won;
            return act != null && Next(order) == act ? DarkestBossPlace.Open : DarkestBossPlace.Locked;
        }

        /// <summary>
        /// The quest of <paramref name="act"/> has been won. True when that was the one on offer and it is
        /// marked; false, and nothing changes, for one that is locked, won already or not of the order.
        /// </summary>
        public bool Win(IReadOnlyList<string> order, string act)
        {
            if (act == null || Next(order) != act) return false;
            _won.Add(act);
            return true;
        }

        /// <summary>Dev: marks one won, or takes the mark off, whatever the order says.</summary>
        public void Set(string act, bool won)
        {
            if (string.IsNullOrEmpty(act)) return;
            if (won) _won.Add(act);
            else _won.Remove(act);
        }

        public void Clear() => _won.Clear();

        public JToken ToJson() => new JObject { ["won"] = new JArray(_won.OrderBy(a => a, StringComparer.Ordinal).Cast<object>().ToArray()) };

        /// <summary>Reads a save's section; one without it (or with anything else in its place) has won nothing.</summary>
        public void FromJson(JToken token)
        {
            _won.Clear();
            if (!(token is JObject json) || !(json["won"] is JArray won)) return;
            foreach (var act in won)
                if (act.Type == JTokenType.String && !string.IsNullOrEmpty((string)act)) _won.Add((string)act);
        }
    }
}
