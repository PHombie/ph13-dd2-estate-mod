using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DD2Estate.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    internal sealed class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }

    internal static class Check
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(what + ": expected " + expected + ", got " + actual);
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new Exception(what + ": expected " + expected.ToString("0.###") + " +/- " + tolerance.ToString("0.###") + ", got " + actual.ToString("0.###"));
        }
    }

    /// <summary>The DD1 install as the core sees it.</summary>
    internal sealed class FolderDd1Files : IDd1Files
    {
        private readonly string _root;

        public FolderDd1Files(string root) { _root = root; }

        private string PathOf(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        public bool Exists(string relativePath) => File.Exists(PathOf(relativePath));

        public string ReadText(string relativePath) => Exists(relativePath) ? File.ReadAllText(PathOf(relativePath)) : null;

        public string[] List(string relativeDir, string pattern)
        {
            var dir = PathOf(relativeDir);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Select(Path.GetFileName).ToArray() : new string[0];
        }
    }

    /// <summary>A machine without DD1: every rule falls back.</summary>
    internal sealed class NoDd1Files : IDd1Files
    {
        public bool Exists(string relativePath) => false;
        public string ReadText(string relativePath) => null;
        public string[] List(string relativeDir, string pattern) => new string[0];
    }

    internal static class Dd1
    {
        public static readonly string[] Dungeons = { "crypts", "weald", "warrens", "cove" };

        public static string Root = Environment.GetEnvironmentVariable("DD1_DIR") ?? @"E:\Steam\steamapps\common\DarkestDungeon";

        private static GenerationRules _generation;
        private static RaidRules _raid;
        private static CurioCatalog _curios;
        private static LootTables _loot;

        public static bool Available => File.Exists(Path.Combine(Root, "scripts", "map_generator.darkest"));

        public static IDd1Files Files
        {
            get
            {
                if (!Available) throw new SkipException("needs the DD1 install");
                return new FolderDd1Files(Root);
            }
        }

        public static GenerationRules Generation => _generation ?? (_generation = GenerationRules.Load(Files));
        public static RaidRules Raid => _raid ?? (_raid = RaidRules.Load(Files));
        public static CurioCatalog Curios => _curios ?? (_curios = CurioCatalog.Load(Files));
        public static LootTables Loot => _loot ?? (_loot = LootTables.Load(Files));

        /// <summary>The blocks of map_generator.darkest read with nothing but string splitting, to check the core's reader against.</summary>
        public static List<Dictionary<string, string[]>> RawMapBlocks()
        {
            if (!Available) throw new SkipException("needs the DD1 install");
            var blocks = new List<Dictionary<string, string[]>>();
            Dictionary<string, string[]> block = null;
            foreach (var raw in File.ReadAllLines(Path.Combine(Root, "scripts", "map_generator.darkest")))
            {
                var line = raw.Trim();
                if (line == "map:") blocks.Add(block = new Dictionary<string, string[]>());
                else if (line.StartsWith(".") && block != null)
                {
                    var parts = line.Substring(1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    block[parts[0]] = parts.Skip(1).ToArray();
                }
            }
            return blocks;
        }

        public static Dictionary<string, string[]> RawMapBlock(string quest, string size, string dungeon)
        {
            return RawMapBlocks().FirstOrDefault(b => b["quest_type"][0] == quest && b["size"][0] == size && b["dungeon_type"][0] == dungeon);
        }
    }

    /// <summary>A player that walks every room, wins every fight and opens every curio.</summary>
    internal sealed class Explorer
    {
        private readonly Rng _choices;
        public readonly List<string> Log = new List<string>();
        public int Calls;

        public Explorer(long seed) { _choices = new Rng(seed); }

        public static string Describe(ExplorationEvent e) => e.GetType().Name + JObject.FromObject(e).ToString(Formatting.None);

        private void Play(List<ExplorationEvent> events, Exploration ex)
        {
            Calls++;
            foreach (var e in events)
            {
                Log.Add(Describe(e));
                Check.True(ex.Light >= 0 && ex.Light <= RaidRules.MaxLight, "light out of range");
            }
            if (events.OfType<SecretRoomFound>().Any())
            {
                Play(ex.EnterSecretRoom(), ex);
                Check.True(ex.InSecretRoom, "the hidden door did not open");
                Play(ex.UseCurio(), ex);
                Check.True(ex.LeaveSecretRoom(), "could not leave the secret room");
            }
        }

        /// <summary>Plays until every room is visited or <paramref name="maxCalls"/> calls were made. True when nothing is left to do.</summary>
        public bool Run(Exploration ex, int maxCalls = int.MaxValue)
        {
            var guard = 0;
            while (ex.Status == RaidStatus.InProgress && Calls < maxCalls)
            {
                if (++guard > 20000) throw new Exception("the explorer is going in circles");
                switch (ex.Pending)
                {
                    case PendingKind.Fight: Play(ex.ResolveFight(FightOutcome.Won), ex); continue;
                    case PendingKind.Trap: Play(ex.ResolveTrap(_choices.Chance(0.5)), ex); continue;
                    case PendingKind.Obstacle: Play(ex.ResolveObstacle(_choices.Chance(0.5)), ex); continue;
                    case PendingKind.Hunger: Play(ex.ResolveHunger(_choices.Chance(0.5)), ex); continue;
                }
                if (ex.CurioHere != null)
                {
                    var before = Log.Count;
                    Play(ex.UseCurio(), ex);
                    Check.True(Log.Count > before, "using a curio did nothing");
                    continue;
                }
                if (ex.Light < 30 && _choices.Chance(0.3)) Play(ex.UseTorch(), ex);

                if (ex.RoomId < 0)
                {
                    Play(ex.MoveTo(ex.TowardRoomId), ex);
                    continue;
                }
                var next = NextRoom(ex);
                if (next < 0) return true;
                Check.True(ex.CanMoveTo(next), "cannot walk to the next room");
                Play(ex.MoveTo(next), ex);
            }
            return ex.Status != RaidStatus.InProgress;
        }

        // first step of the shortest walk to the nearest room not visited yet
        private static int NextRoom(Exploration ex)
        {
            var map = ex.Map;
            var from = new int[map.Rooms.Count];
            for (var i = 0; i < from.Length; i++) from[i] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(ex.RoomId);
            from[ex.RoomId] = ex.RoomId;
            while (queue.Count > 0)
            {
                var room = queue.Dequeue();
                if (!ex.IsRoomVisited(room))
                {
                    while (from[room] != ex.RoomId) room = from[room];
                    return room;
                }
                foreach (var hall in map.HallwaysOf(room))
                {
                    var other = hall.Other(room);
                    if (from[other] >= 0) continue;
                    from[other] = room;
                    queue.Enqueue(other);
                }
            }
            return -1;
        }
    }
}
