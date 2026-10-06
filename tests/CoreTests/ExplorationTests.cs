using System;
using System.Collections.Generic;
using System.Linq;
using DD2Estate.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DD2Estate.CoreTests
{
    public static class ExplorationTests
    {
        private static readonly RaidRules FallbackRules = RaidRules.Load(new NoDd1Files());

        private static EncounterSlot Fight(EncounterKind kind) => new EncounterSlot { Kind = kind, Tier = 1 };

        /// <summary>
        /// Room 0 (entrance) - trap, obstacle, hunger, fight - room 1 (fight guarding a chest) - four empty tiles -
        /// room 2 (empty). Hand-made, so these tests need no DD1 files.
        /// </summary>
        private static DungeonMap Corridor()
        {
            var map = new DungeonMap { DungeonId = "crypts", QuestType = QuestTypes.Explore, Length = 1, Tier = 1, Width = 3, Height = 1, TrapId = "spikes" };
            map.Rooms.Add(new Room { Id = 0, X = 0, Type = RoomType.Entrance });
            map.Rooms.Add(new Room { Id = 1, X = 1, Type = RoomType.Treasure, Battle = Fight(EncounterKind.Room), CurioId = "heirloom_chest" });
            map.Rooms.Add(new Room { Id = 2, X = 2, Type = RoomType.Empty });
            var first = new Hallway { Id = 0, RoomA = 0, RoomB = 1 };
            first.Segments.Add(new Segment { Content = HallContent.Trap, PropId = "spikes" });
            first.Segments.Add(new Segment { Content = HallContent.Obstacle, PropId = "rubble" });
            first.Segments.Add(new Segment { Content = HallContent.Hunger });
            first.Segments.Add(new Segment { Content = HallContent.Battle, Battle = Fight(EncounterKind.Hallway) });
            var second = new Hallway { Id = 1, RoomA = 1, RoomB = 2 };
            for (var i = 0; i < 4; i++) second.Segments.Add(new Segment());
            map.Hallways.Add(first);
            map.Hallways.Add(second);
            map.Objective = new QuestObjective { Kind = ObjectiveKind.Explore, Required = 3 };
            return map;
        }

        private static Exploration Start(DungeonMap map, long seed = 1)
        {
            // no room-entry scouting unless a test asks for it: the event lists below stay exact
            return new Exploration(map, FallbackRules, null, seed) { ScoutingEnabled = false };
        }

        private static T One<T>(List<ExplorationEvent> events) where T : ExplorationEvent
        {
            var found = events.OfType<T>().ToList();
            Check.Equal(1, found.Count, typeof(T).Name + " events in [" + string.Join(", ", events) + "]");
            return found[0];
        }

        private static void None<T>(List<ExplorationEvent> events) where T : ExplorationEvent
        {
            Check.Equal(0, events.OfType<T>().Count(), typeof(T).Name + " events");
        }

        public static void HallwayStepByStep()
        {
            var ex = Start(Corridor());
            Check.Equal(0, ex.RoomId, "start room");
            Check.True(ex.IsRoomVisited(0) && !ex.IsRoomVisited(1) && !ex.IsRoomKnown(1), "what is known at the start");
            Check.True(ex.CanMoveTo(1) && !ex.CanMoveTo(2) && !ex.CanMoveTo(0), "rooms the party can walk to");
            Check.Equal(0, ex.MoveTo(2).Count, "walking to a room that is not next door");

            // an unseen trap goes off under the party
            var ev = ex.MoveTo(1);
            Check.True(ev[0] is EnteredSegment, "first event");
            Check.Equal("spikes", One<TrapTriggered>(ev).TrapId, "trap");
            Check.True(!One<TrapTriggered>(ev).WasSpotted, "the trap was not scouted");
            Check.True(ex.RoomId == -1 && ex.HallwayId == 0 && ex.Segment == 0 && ex.TowardRoomId == 1, "position on the first tile");
            Check.Equal(PendingKind.None, ex.Pending, "a sprung trap asks nothing");
            Check.Equal(RaidRules.MaxLight - FallbackRules.LightLossUnknown, ex.Light, "light after one unknown tile");

            // obstacle: no way forward until it is cleared, backing off is allowed
            ev = ex.MoveTo(1);
            Check.Equal("rubble", One<ObstacleBlocks>(ev).ObstacleId, "obstacle");
            Check.Equal(PendingKind.Obstacle, ex.Pending, "pending");
            Check.True(!ex.CanMoveTo(1) && ex.CanMoveTo(0), "obstacle blocks forward only");
            Check.Equal(0, ex.MoveTo(1).Count, "walking into the obstacle");
            ev = ex.Step(0);
            Check.True(One<EnteredSegment>(ev).BackingUp && ex.Segment == 0 && ex.Pending == PendingKind.None, "backed off the obstacle");
            Check.Equal(RaidRules.MaxLight - 2 * FallbackRules.LightLossUnknown - FallbackRules.LightLossVisited, ex.Light, "a walked tile costs less light");
            ev = ex.MoveTo(1);
            One<ObstacleBlocks>(ev);
            var lightBefore = ex.Light;
            ev = ex.ResolveObstacle(false);
            Check.True(!One<ObstacleCleared>(ev).UsedShovel, "cleared by hand");
            Check.Equal(lightBefore + FallbackRules.Obstacle.Torchlight, ex.Light, "clearing by hand costs torchlight");
            Check.True(ex.IsSegmentDone(0, 1), "obstacle gone");

            // hunger has to be answered, in either direction
            ev = ex.MoveTo(1);
            One<HungerCheck>(ev);
            Check.True(!ex.CanMoveTo(1) && !ex.CanMoveTo(0), "hunger cannot be walked away from");
            Check.Equal(0, ex.ResolveFight(FightOutcome.Won).Count, "wrong answer to hunger");
            Check.True(One<HungerResolved>(ex.ResolveHunger(true)).Ate, "ate");

            // hallway fight: fleeing puts the party back one tile, the fight stays
            ev = ex.MoveTo(1);
            Check.Equal(EncounterKind.Hallway, One<FightStarts>(ev).Slot.Kind, "hallway fight");
            Check.True(!ex.CanLeave, "no leaving in a fight");
            ex.ResolveFight(FightOutcome.Retreated);
            Check.True(ex.Segment == 2 && ex.Pending == PendingKind.None && !ex.IsSegmentDone(0, 3), "fled back to the hunger tile");
            ev = ex.MoveTo(1);
            One<FightStarts>(ev);
            ex.ResolveFight(FightOutcome.Won);
            Check.True(ex.IsSegmentDone(0, 3), "hallway fight won");

            // room: fight first, then the chest it guarded
            ev = ex.MoveTo(1);
            Check.True(One<RoomEntered>(ev).FirstVisit && One<RoomEntered>(ev).RoomId == 1, "entered room 1");
            Check.Equal(EncounterKind.Room, One<FightStarts>(ev).Slot.Kind, "room fight");
            None<CurioFound>(ev);
            Check.True(ex.CurioHere == null && ex.UseCurio().Count == 0, "the chest is guarded");
            ex.ResolveFight(FightOutcome.Retreated);
            Check.True(ex.RoomId == -1 && ex.HallwayId == 0 && ex.Segment == 3 && ex.TowardRoomId == 1, "fled back into the hallway");
            ev = ex.MoveTo(1);
            Check.True(!One<RoomEntered>(ev).FirstVisit, "second entry");
            ev = ex.ResolveFight(FightOutcome.Won);
            Check.Equal("heirloom_chest", One<CurioFound>(ev).CurioId, "chest after the fight");
            Check.True(ex.IsRoomFightWon(1) && ex.CurioHere == "heirloom_chest", "room cleared");
            One<CurioUsed>(ex.UseCurio());
            Check.True(ex.IsRoomCurioUsed(1) && ex.CurioHere == null && ex.UseCurio().Count == 0, "a curio is used once");

            // last room: objective
            Check.True(!ex.ObjectiveComplete && ex.Progress == 2, "two of three rooms");
            ev = ex.MoveTo(2);
            Check.Equal(4, ev.OfType<EnteredSegment>().Count(), "an empty hallway is walked in one go");
            Check.Equal(3, One<ObjectiveProgress>(ev).Progress, "progress");
            One<ObjectiveCompleted>(ev);
            Check.True(ex.ObjectiveComplete && ex.Status == RaidStatus.InProgress, "complete, still inside");
            Check.Equal(RaidStatus.Succeeded, One<QuestEnded>(ex.Leave()).Status, "left after completing");
            Check.Equal(0, ex.MoveTo(1).Count, "no walking after the quest ended");
        }

        public static void ScoutedTrapCanBeDisarmed()
        {
            var ex = Start(Corridor());
            var scouted = One<Scouted>(ex.Scout(1, "traps"));
            Check.True(scouted.Hallways.SequenceEqual(new[] { 0 }) && scouted.Rooms.Count == 0, "only the trap hallway is revealed");
            Check.True(ex.IsSegmentKnown(0, 0) && !ex.IsSegmentKnown(0, 1) && !ex.IsRoomKnown(1), "only the trap tile is known");

            var ev = ex.MoveTo(1);
            Check.Equal("spikes", One<TrapSpotted>(ev).TrapId, "spotted");
            None<TrapTriggered>(ev);
            Check.Equal(RaidRules.MaxLight - FallbackRules.LightLossScouted, ex.Light, "light after a scouted tile");
            Check.True(ex.Pending == PendingKind.Trap && !ex.CanMoveTo(1) && ex.CanMoveTo(0), "a spotted trap blocks forward only");

            // back out, come again, disarm
            ex.MoveTo(0);
            Check.True(ex.RoomId == 0 && ex.Pending == PendingKind.None, "back in the entrance");
            One<TrapSpotted>(ex.MoveTo(1));
            One<TrapDisarmed>(ex.ResolveTrap(true));
            One<ObstacleBlocks>(ex.MoveTo(1));

            var other = Start(Corridor());
            other.Scout(0, "all");
            One<TrapSpotted>(other.MoveTo(1));
            Check.True(One<TrapTriggered>(other.ResolveTrap(false)).WasSpotted, "failed disarm springs it");
        }

        public static void ScoutingReachAndTargets()
        {
            var ex = Start(Corridor());
            var s = One<Scouted>(ex.Scout(1, "room_battles"));
            Check.True(s.Rooms.SequenceEqual(new[] { 1 }) && s.Hallways.Count == 0, "room battle next door");
            Check.Equal(0, ex.Scout(1, "room_battles").Count, "nothing new the second time");

            s = One<Scouted>(ex.Scout(1, "hall_battles"));
            Check.True(s.Hallways.SequenceEqual(new[] { 0 }) && ex.IsSegmentKnown(0, 3) && !ex.IsSegmentKnown(0, 2), "hallway fight tile");

            s = One<Scouted>(ex.Scout(1, "all"));
            Check.True(s.Rooms.Count == 0 && s.Hallways.SequenceEqual(new[] { 0 }), "reach 1 stops at the first room");
            Check.True(Enumerable.Range(0, 4).All(i => ex.IsSegmentKnown(0, i)) && !ex.IsSegmentKnown(1, 0) && !ex.IsRoomKnown(2), "reach 1");

            s = One<Scouted>(ex.Scout(0, "all"));
            Check.True(s.Rooms.SequenceEqual(new[] { 2 }) && s.Hallways.SequenceEqual(new[] { 1 }), "reach 0 is the whole map");

            // a scouted fight is announced as known
            var known = Start(Corridor());
            known.Scout(0, "all");
            One<TrapSpotted>(known.MoveTo(1));
            known.ResolveTrap(true);
            known.MoveTo(1);
            known.ResolveObstacle(true);
            Check.Equal(RaidRules.MaxLight - 2 * FallbackRules.LightLossScouted, known.Light, "the shovel costs no light");
            known.MoveTo(1);
            known.ResolveHunger(false);
            Check.True(One<FightStarts>(known.MoveTo(1)).Scouted, "scouted hallway fight");
            known.ResolveFight(FightOutcome.Won);
            Check.True(One<FightStarts>(known.MoveTo(1)).Scouted, "scouted room fight");
        }

        /// <summary>The corridor map with nothing in the first hallway and no fight in room 1.</summary>
        private static DungeonMap QuietCorridor()
        {
            var map = Corridor();
            foreach (var segment in map.Hallways[0].Segments)
            {
                segment.Content = HallContent.Empty;
                segment.Battle = null;
                segment.PropId = null;
            }
            map.Rooms[1].Battle = null;
            return map;
        }

        public static void RoomEntryScoutingUsesTheChance()
        {
            int hits = 0, criticals = 0;
            const int runs = 4000;
            for (var seed = 0; seed < runs; seed++)
            {
                var map = QuietCorridor();
                map.Rooms.Add(new Room { Id = 3, X = -1, Y = -1, Type = RoomType.Secret, CurioId = "secret_stash" });
                map.Hallways[1].Segments[1].Content = HallContent.Secret;
                map.Hallways[1].Segments[1].SecretRoomId = 3;
                var ex = new Exploration(map, FallbackRules, null, seed) { ScoutBonus = 0.15 };
                var scouted = ex.MoveTo(1).OfType<Scouted>().FirstOrDefault();
                if (scouted == null) continue;
                hits++;
                Check.True(scouted.Hallways.SequenceEqual(new[] { 1 }), "the hallway out of the room is revealed");
                Check.True(scouted.Rooms.SequenceEqual(new[] { 2 }), "the room behind it is revealed");
                Check.True(ex.IsSegmentKnown(1, 0) && ex.IsSegmentKnown(1, 3) && ex.IsRoomKnown(2) && !ex.IsRoomVisited(2), "what a scout shows");
                // only a critical scout finds the hidden door
                if (ex.IsSegmentKnown(1, 1)) criticals++;
                Check.Equal(0, ex.MoveTo(0).OfType<Scouted>().Count(), "no scouting in a room seen before");
            }
            // base chance + the party's bonus; the fallback rules have no light bands, so no torch bonus
            Check.Near(FallbackRules.ScoutChanceBase + 0.15, hits / (double)runs, 0.03, "scouting rate");
            Check.Near(FallbackRules.ScoutCritChance, criticals / (double)hits, 0.05, "critical scouting rate");

            var off = new Exploration(QuietCorridor(), FallbackRules, null, 5) { ScoutingEnabled = false, ScoutBonus = 5 };
            Check.Equal(0, off.MoveTo(1).OfType<Scouted>().Count(), "scouting disabled");
            var sure = new Exploration(QuietCorridor(), FallbackRules, null, 5) { ScoutBonus = 5 };
            One<Scouted>(sure.MoveTo(1));

            // with DD1's rules a bright torch scouts more often than a dead one
            var rules = Dd1.Raid;
            int bright = 0, dark = 0;
            for (var seed = 0; seed < runs; seed++)
            {
                var lit = new Exploration(QuietCorridor(), rules, null, seed);
                if (lit.MoveTo(1).OfType<Scouted>().Any()) bright++;
                var unlit = new Exploration(QuietCorridor(), rules, null, seed);
                unlit.AddLight(-1000);
                if (unlit.MoveTo(1).OfType<Scouted>().Any()) dark++;
            }
            Check.Near(rules.ScoutChanceBase + rules.LightBands[0].Value("player_scouting_increase") / 100, bright / (double)runs, 0.03, "scouting rate in full light");
            Check.Near(rules.ScoutChanceBase, dark / (double)runs, 0.03, "scouting rate in the dark");
        }

        public static void SecretRoomNeedsScouting()
        {
            DungeonMap Secret()
            {
                var map = QuietCorridor();
                map.Rooms.Add(new Room { Id = 3, X = -1, Y = -1, Type = RoomType.Secret, CurioId = "secret_stash" });
                map.Hallways[0].Segments[2].Content = HallContent.Secret;
                map.Hallways[0].Segments[2].SecretRoomId = 3;
                return map;
            }

            // walked past unseen
            var blind = Start(Secret());
            var ev = blind.MoveTo(1);
            None<SecretRoomFound>(ev);
            Check.True(blind.RoomId == 1 && !blind.IsSegmentKnown(0, 2) && blind.IsSegmentVisited(0, 2), "walked past the hidden door");
            blind.MoveTo(0);
            Check.Equal(2, blind.Progress, "a secret room is not a room to explore");

            var ex = Start(Secret());
            ex.Scout(0, "all");
            Check.True(!ex.IsSegmentKnown(0, 2) && ex.IsSegmentKnown(0, 1), "a plain scout does not show hidden doors");
            ex.Scout(1, "curios", true);
            Check.True(ex.IsSegmentKnown(0, 2), "a critical scout shows the hidden door");
            ev = ex.MoveTo(1);
            Check.Equal(3, One<SecretRoomFound>(ev).RoomId, "found");
            Check.True(ex.Segment == 2 && !ex.InSecretRoom, "stopped at the door");
            ev = ex.EnterSecretRoom();
            Check.True(One<RoomEntered>(ev).RoomId == 3 && One<CurioFound>(ev).CurioId == "secret_stash", "inside");
            Check.True(ex.InSecretRoom && ex.RoomId == 3 && !ex.CanMoveTo(1) && ex.MoveTo(1).Count == 0, "no walking out through a wall");
            One<CurioUsed>(ex.UseCurio());
            Check.True(ex.LeaveSecretRoom() && !ex.InSecretRoom && ex.HallwayId == 0 && ex.Segment == 2, "back in the hallway");
            ev = ex.MoveTo(1);
            Check.True(One<RoomEntered>(ev).RoomId == 1, "walked on");
            ev = ex.MoveTo(0);
            None<SecretRoomFound>(ev);
            Check.Equal(0, ex.RoomId, "the door does not stop the party twice");
        }

        public static void ExamineTurnsBackToWhatWasLeft()
        {
            // a crate in the first hallway, a hidden door in the second
            var map = QuietCorridor();
            map.Hallways[0].Segments[1].Content = HallContent.Curio;
            map.Hallways[0].Segments[1].PropId = "crate";
            map.Rooms.Add(new Room { Id = 3, X = -1, Y = -1, Type = RoomType.Secret, CurioId = "secret_stash" });
            map.Hallways[1].Segments[2].Content = HallContent.Secret;
            map.Hallways[1].Segments[2].SecretRoomId = 3;
            Check.True(map.FindSecretDoor(3, out var doorHall, out var doorTile) && doorHall == 1 && doorTile == 2, "where the secret room opens");
            Check.True(!map.FindSecretDoor(1, out _, out _), "an ordinary room has no hidden door");

            var ex = Start(map);
            Check.Equal(0, ex.Examine().Count, "nothing to turn to in an empty room");
            var ev = ex.MoveTo(1);
            Check.True(One<CurioFound>(ev).CurioId == "crate" && ex.Segment == 1, "stopped at the crate");
            // left alone and walked past: the crate stays, and announces itself only when asked
            var before = ex.ToJson().ToString(Formatting.None);
            Check.Equal("crate", One<CurioFound>(ex.Examine()).CurioId, "the crate again");
            Check.Equal(before, ex.ToJson().ToString(Formatting.None), "examining changes nothing");
            ex.MoveTo(1);
            Check.Equal(1, ex.RoomId, "walked on to the room");
            Check.Equal("heirloom_chest", One<CurioFound>(ex.Examine()).CurioId, "the room's chest again");
            ev = ex.MoveTo(0);
            None<CurioFound>(ev);
            Check.Equal(0, ex.RoomId, "a curio stops the party once");
            ex.Step(1);
            Check.Equal(0, ex.Examine().Count, "the tile before the crate is empty");
            ex.Step(1);
            Check.Equal("crate", One<CurioFound>(ex.Examine()).CurioId, "back at the crate");
            One<CurioUsed>(ex.UseCurio());
            Check.Equal(0, ex.Examine().Count, "an emptied crate is not announced");

            // the hidden door: nothing to turn to until a scout has shown it, then as often as the player likes
            ex.MoveTo(1);
            ex.Step(2);
            ex.Step(2);
            ex.Step(2);
            Check.True(ex.HallwayId == 1 && ex.Segment == 2 && ex.SecretRoomHere < 0 && ex.Examine().Count == 0, "standing at a door nobody knows of");
            ex.Scout(0, "all", true);
            Check.Equal(3, ex.SecretRoomHere, "the door is known now");
            Check.Equal(3, One<SecretRoomFound>(ex.Examine()).RoomId, "the hidden door");
            Check.Equal(3, One<SecretRoomFound>(ex.Examine()).RoomId, "and again");
            ex.EnterSecretRoom();
            Check.True(ex.InSecretRoom && ex.SecretRoomHere < 0, "inside, there is no door to find");
            Check.Equal("secret_stash", One<CurioFound>(ex.Examine()).CurioId, "the stash");
            var inside = Exploration.FromJson(JObject.Parse(ex.ToJson().ToString(Formatting.None)), FallbackRules, null);
            Check.True(inside.InSecretRoom && inside.RoomId == 3 && inside.HallwayId == 1 && inside.Segment == 2 && inside.CurioHere == "secret_stash", "a save made inside the secret room loads inside it");
            Check.True(inside.LeaveSecretRoom() && inside.SecretRoomHere == 3, "and the way out is where it was");
            One<CurioUsed>(ex.UseCurio());
            Check.Equal(0, ex.Examine().Count, "the stash is empty");
            ex.LeaveSecretRoom();
            One<SecretRoomFound>(ex.Examine());

            // nothing is examined while something else wants an answer
            var busy = Start(Corridor());
            busy.Scout(0, "all");
            One<TrapSpotted>(busy.MoveTo(1));
            Check.Equal(0, busy.Examine().Count, "a trap first");
            busy.ResolveTrap(true);
            One<QuestEnded>(busy.Leave());
            Check.Equal(0, busy.Examine().Count, "nothing after the quest ended");
        }

        public static void TrapsRememberHowTheyEnded()
        {
            // walked into blind: sprung
            var ex = Start(Corridor());
            Check.True(!ex.IsTrapSprung(0, 0), "a trap waits");
            One<TrapTriggered>(ex.MoveTo(1));
            Check.True(ex.IsTrapSprung(0, 0) && ex.IsSegmentDone(0, 0), "sprung under the party");
            var loaded = Exploration.FromJson(JObject.Parse(ex.ToJson().ToString(Formatting.None)), FallbackRules, null);
            Check.True(loaded.IsTrapSprung(0, 0) && !loaded.IsTrapSprung(0, 1), "the save remembers it");

            // spotted and disarmed: done, not sprung
            var careful = Start(Corridor());
            careful.Scout(1, "traps");
            One<TrapSpotted>(careful.MoveTo(1));
            Check.True(!careful.IsTrapSprung(0, 0) && !careful.IsSegmentDone(0, 0), "a spotted trap still waits");
            One<TrapDisarmed>(careful.ResolveTrap(true));
            Check.True(!careful.IsTrapSprung(0, 0) && careful.IsSegmentDone(0, 0), "disarmed");

            // spotted and fumbled: sprung
            var clumsy = Start(Corridor());
            clumsy.Scout(1, "traps");
            clumsy.MoveTo(1);
            One<TrapTriggered>(clumsy.ResolveTrap(false));
            Check.True(clumsy.IsTrapSprung(0, 0), "a failed disarm springs it");

            // a save from before the mark existed reads as "not sprung"
            var old = ex.ToJson();
            ((JArray)((JArray)old["segments"])[0])[0] = 7;
            var before = Exploration.FromJson(old, FallbackRules, null);
            Check.True(before.IsSegmentDone(0, 0) && !before.IsTrapSprung(0, 0), "an old save");
        }

        public static void ScoutedNamesItsTiles()
        {
            var ex = Start(Corridor());
            var traps = One<Scouted>(ex.Scout(1, "traps"));
            Check.True(traps.Tiles.Count == 1 && traps.Tiles[0].HallwayId == 0 && traps.Tiles[0].Segment == 0 && traps.Tiles[0].Content == HallContent.Trap, "the trap tile");

            var rest = One<Scouted>(ex.Scout(0, "all"));
            Check.True(rest.Tiles.Select(t => t.HallwayId * 10 + t.Segment).SequenceEqual(new[] { 1, 2, 3, 10, 11, 12, 13 }), "every other tile, hallway by hallway");
            Check.True(rest.Tiles.Take(3).Select(t => t.Content).SequenceEqual(new[] { HallContent.Obstacle, HallContent.Hunger, HallContent.Battle }), "what the tiles hold");
            Check.True(rest.Tiles.Skip(3).All(t => t.Content == HallContent.Empty), "empty tiles are seen too");
            Check.True(rest.Tiles.All(t => ex.IsSegmentKnown(t.HallwayId, t.Segment)), "named tiles are known");

            // a hidden door is named only by the scout that finds it
            var map = QuietCorridor();
            map.Rooms.Add(new Room { Id = 3, X = -1, Y = -1, Type = RoomType.Secret, CurioId = "secret_stash" });
            map.Hallways[0].Segments[2].Content = HallContent.Secret;
            map.Hallways[0].Segments[2].SecretRoomId = 3;
            var secret = Start(map);
            Check.True(One<Scouted>(secret.Scout(1, "all")).Tiles.All(t => t.Content != HallContent.Secret), "a plain scout");
            var critical = One<Scouted>(secret.Scout(1, "all", true));
            Check.True(critical.Tiles.Count == 1 && critical.Tiles[0].Content == HallContent.Secret && critical.Tiles[0].Segment == 2 && critical.Rooms.Count == 0, "the critical one");

            // the chance the party is told about is the chance that is rolled
            var chance = new Exploration(QuietCorridor(), FallbackRules, null, 1) { ScoutBonus = 0.15 };
            Check.Near(FallbackRules.ScoutChanceBase + 0.15, chance.ScoutChance, 1e-9, "base and bonus");
            chance.ScoutBonus = 5;
            Check.Equal(1.0, chance.ScoutChance, "never above certain");
            chance.ScoutingEnabled = false;
            Check.Equal(0.0, chance.ScoutChance, "a quest without scouting");
            var lit = new Exploration(QuietCorridor(), Dd1.Raid, null, 1);
            var bright = lit.ScoutChance;
            lit.AddLight(-1000);
            Check.True(bright > lit.ScoutChance && Math.Abs(lit.ScoutChance - Dd1.Raid.ScoutChanceBase) < 1e-9, "torchlight scouts further: " + bright + " against " + lit.ScoutChance);
        }

        public static void SecretRoomHoldsWhatDd1Says()
        {
            // DD1's library: the stash is three draws of table B, or of the collector's table for a key
            var catalog = Dd1.Curios;
            var stash = catalog.Get("secret_stash");
            Check.True(stash != null && stash.Results.Count == 1 && stash.Results[0].Type == CurioResultTypes.Loot, "the stash is loot and nothing else");
            Check.True(stash.Results[0].Values.Count == 1 && stash.Results[0].Values[0].Value == "B" && stash.Results[0].Values[0].Amount == 3, "three draws of B");
            Check.True(catalog.ItemsFor("secret_stash").SequenceEqual(new[] { "skeleton_key" }), "the key is the only item it takes");
            var keyed = stash.ItemUse("skeleton_key").Result;
            Check.True(keyed.Type == CurioResultTypes.Loot && keyed.Values.Count == 1 && keyed.Values[0].Value == "COLLECTOR" && keyed.Values[0].Amount == 3, "three draws of COLLECTOR with the key");
            Check.True(Dd1.Loot.Has("B") && Dd1.Loot.Has("COLLECTOR"), "both tables exist");

            // DD1's maps: none in a short quest, at most one otherwise, each behind a hallway tile, each with the dungeon's stash
            int found = 0, opened = 0;
            foreach (var dungeon in Dd1.Dungeons)
                for (var seed = 0; seed < 40; seed++)
                {
                    var none = DungeonGenerator.Generate(dungeon, QuestTypes.Explore, 1, 1, seed, Dd1.Generation);
                    Check.True(none.Rooms.All(r => r.Type != RoomType.Secret), "a short quest has no secret room");
                    var map = DungeonGenerator.Generate(dungeon, QuestTypes.Explore, 2, 3, seed, Dd1.Generation);
                    var secrets = map.Rooms.Where(r => r.Type == RoomType.Secret).ToList();
                    Check.True(secrets.Count <= 1, "secret rooms: " + secrets.Count);
                    if (secrets.Count == 0) continue;
                    found++;
                    var room = secrets[0];
                    Check.True(Dd1.Generation.PropsFor(dungeon).SecretTreasures.Any(w => w.Id == room.CurioId), dungeon + ": the secret room holds " + room.CurioId);
                    Check.True(room.Battle == null && room.X < 0 && map.HallwaysOf(room.Id).Count == 0, "off the grid, unguarded, no hallway of its own");
                    Check.True(map.FindSecretDoor(room.Id, out var hall, out var tile), "its door");

                    // scout it, walk to it, go in, open the stash with a key, come back out
                    var ex = new Exploration(map, Dd1.Raid, catalog, seed) { ScoutingEnabled = false };
                    ex.Scout(0, "all", true);
                    Check.True(ex.IsSegmentKnown(hall, tile), "the door is on the map");
                    var door = map.Hallways[hall];
                    var dist = map.Distances(door.RoomA);
                    var guard = 0;
                    while (ex.SecretRoomHere != room.Id)
                    {
                        Check.True(++guard < 3000, "the party never came to the hidden door");
                        if (ex.Pending == PendingKind.Fight) ex.ResolveFight(FightOutcome.Won);
                        else if (ex.Pending == PendingKind.Trap) ex.ResolveTrap(true);
                        else if (ex.Pending == PendingKind.Obstacle) ex.ResolveObstacle(true);
                        else if (ex.Pending == PendingKind.Hunger) ex.ResolveHunger(true);
                        else if (ex.RoomId < 0) ex.MoveTo(ex.TowardRoomId);
                        else if (ex.RoomId == door.RoomA) ex.MoveTo(door.RoomB);
                        else ex.MoveTo(map.HallwaysOf(ex.RoomId).Select(h => h.Other(ex.RoomId)).OrderBy(r => dist[r]).First());
                    }
                    Check.True(ex.HallwayId == hall && ex.Segment == tile && !ex.InSecretRoom, "stopped at the door");
                    var entered = ex.EnterSecretRoom();
                    Check.True(One<RoomEntered>(entered).RoomId == room.Id && One<CurioFound>(entered).CurioId == room.CurioId, "inside, before the stash");
                    Check.True(ex.InSecretRoom && ex.HallwayId == hall && ex.Segment == tile, "the party's tile is kept");
                    var outcome = One<CurioUsed>(ex.UseCurio("skeleton_key")).Outcome;
                    Check.True(outcome.ItemId == "skeleton_key" && outcome.Loot.Count == 1 && outcome.Loot[0].Table == "COLLECTOR" && outcome.Loot[0].Draws == 3, "the keyed stash");
                    var drops = Dd1.Loot.Draw(outcome.Loot[0].Table, outcome.Loot[0].Draws, map.Tier, dungeon, new Rng(seed));
                    Check.True(drops.Count == 3 && drops.All(d => d.Amount > 0), "three things of worth: " + string.Join(", ", drops));
                    Check.True(ex.LeaveSecretRoom() && !ex.InSecretRoom && ex.Segment == tile, "back on the tile");
                    opened++;
                }
            Check.True(found > 20 && opened == found, "maps with a secret room: " + found + ", stashes opened: " + opened);
        }

        public static void LightBandsAndTorch()
        {
            var rules = Dd1.Raid;
            Check.True(rules.LightBands.Count >= 2, "light bands from rules.json");
            for (var i = 1; i < rules.LightBands.Count; i++)
                Check.Equal(rules.LightBands[i - 1].Lower, rules.LightBands[i].Upper, "bands are contiguous");

            // lowest_excluded: a band's lower bound belongs to the band below
            var top = rules.LightBands[0];
            Check.Equal(0, rules.BandIndex(100), "full light");
            Check.Equal(0, rules.BandIndex(top.Lower + 1), "just above the first threshold");
            Check.Equal(1, rules.BandIndex(top.Lower), "on the first threshold");
            Check.Equal(rules.LightBands.Count - 1, rules.BandIndex(0), "no light");
            Check.Equal(rules.LightBands.Count - 2, rules.BandIndex(1), "a glimmer");
            Check.True(top.Value("player_scouting_increase") > 0 && rules.BandFor(0).Value("player_scouting_increase") == 0, "scouting bonus only in the light");
            Check.True(rules.BandFor(0).Value("stress_damage_increase") > top.Value("stress_damage_increase"), "more stress in the dark");
            Check.True(rules.LightLossUnknown > rules.LightLossVisited && rules.LightLossVisited > 0, "tile light loss");

            // walk an explore map until the torch is out, counting bands on the way
            var map = DungeonGenerator.Generate("crypts", QuestTypes.Explore, 3, 1, 3, Dd1.Generation);
            var ex = new Exploration(map, rules, Dd1.Curios, 3);
            var bandChanges = WalkUntilDark(ex);
            Check.Equal(0.0, ex.Light, "light bottoms out at 0");
            Check.Equal(rules.LightBands.Count - 1, bandChanges, "band changes on the way down");
            Check.True(ex.LightBand == rules.LightBands[rules.LightBands.Count - 1], "darkest band");

            var ev = ex.UseTorch();
            var changed = One<LightChanged>(ev);
            Check.True(changed.Before == 0 && changed.After == rules.TorchLight && changed.BandChanged, "a torch lifts the light");
            ex.AddLight(1000);
            Check.Equal(RaidRules.MaxLight, ex.Light, "light tops out at 100");
            Check.Equal(0, ex.AddLight(5).Count, "no event when nothing changes");
        }

        // Wanders with no torch and no curio touched; returns how often the light band changed.
        private static int WalkUntilDark(Exploration ex)
        {
            var changes = 0;
            for (var guard = 0; guard < 5000 && ex.Light > 0; guard++)
            {
                List<ExplorationEvent> ev;
                if (ex.Pending == PendingKind.Fight) ev = ex.ResolveFight(FightOutcome.Won);
                else if (ex.Pending == PendingKind.Trap) ev = ex.ResolveTrap(true);
                else if (ex.Pending == PendingKind.Obstacle) ev = ex.ResolveObstacle(true);
                else if (ex.Pending == PendingKind.Hunger) ev = ex.ResolveHunger(true);
                else if (ex.RoomId < 0) ev = ex.MoveTo(ex.TowardRoomId);
                else
                {
                    var next = ex.Map.HallwaysOf(ex.RoomId).Select(h => h.Other(ex.RoomId))
                        .OrderBy(r => ex.IsRoomVisited(r) ? 1 : 0).ThenBy(r => (r * 7 + guard) % 5).First();
                    ev = ex.MoveTo(next);
                }
                changes += ev.OfType<LightChanged>().Count(c => c.BandChanged);
            }
            return changes;
        }

        public static void RetreatRules()
        {
            var rules = Dd1.Raid;
            var ex = new Exploration(Corridor(), rules, null, 1);
            Check.True(ex.CanLeave, "abandoning is allowed by default");
            var ended = One<QuestEnded>(ex.Leave());
            Check.Equal(RaidStatus.Abandoned, ended.Status, "left before the objective");
            var penalty = JObject.Parse(Dd1.Files.ReadText("campaign/quest/quest.exit_penalty.json"));
            Check.Equal((double)penalty["fail_penalty"]["stress_damage"], ended.Stress, "abandon stress from DD1");
            Check.True(ended.Stress > 0, "abandoning costs stress");
            Check.True(ex.Status == RaidStatus.Abandoned && !ex.CanLeave && ex.Leave().Count == 0, "over");

            // a quest that forbids retreat can only be left once complete
            var locked = new Exploration(Corridor(), rules, null, 1) { RetreatAllowed = false, ScoutingEnabled = false };
            Check.True(!locked.CanLeave && locked.Leave().Count == 0, "no retreat");
            new Explorer(1).Run(locked);
            Check.True(locked.ObjectiveComplete && locked.CanLeave, "complete: may leave");
            Check.Equal(0.0, One<QuestEnded>(locked.Leave()).Stress, "no stress for a finished quest");
            Check.Equal(RaidStatus.Succeeded, locked.Status, "succeeded");

            // a lost fight ends the quest
            var lost = new Exploration(Corridor(), rules, null, 1) { ScoutingEnabled = false };
            lost.Scout(0, "all");
            lost.MoveTo(1);
            lost.ResolveTrap(true);
            lost.MoveTo(1);
            lost.ResolveObstacle(true);
            lost.MoveTo(1);
            lost.ResolveHunger(true);
            One<FightStarts>(lost.MoveTo(1));
            Check.Equal(RaidStatus.Failed, One<QuestEnded>(lost.ResolveFight(FightOutcome.Lost)).Status, "party wiped");
            Check.True(lost.Status == RaidStatus.Failed && lost.Pending == PendingKind.None && !lost.CanMoveTo(1), "nothing after a wipe");
        }

        public static void ReturnTripCanBringNewContent()
        {
            var rules = Dd1.Raid;
            Check.True(rules.ReturnContents.Count > 0, "corridor_return_content from rules.json");
            foreach (var content in rules.ReturnContents)
                Check.True(content.ChanceAt(0) > content.ChanceAt(100) && content.ChanceAt(100) > 0, "more comes back in the dark: " + content.Content);

            // walk an empty hallway there and back many times in the dark and count what turns up
            int walks = 0, found = 0;
            for (var seed = 0; seed < 300; seed++)
            {
                var map = Corridor();
                var ex = new Exploration(map, rules, null, seed) { ScoutingEnabled = false };
                ex.Scout(0, "all");
                var explorer = new Explorer(seed);
                explorer.Run(ex);
                Check.Equal(2, ex.RoomId, "at the far end");
                ex.AddLight(-1000);
                for (var i = 0; i < 10; i++)
                {
                    var target = i % 2 == 0 ? 1 : 2;
                    walks++;
                    var ev = ex.MoveTo(target);
                    while (ex.RoomId != target)
                    {
                        Check.True(ev.OfType<FightStarts>().Any() || ev.OfType<HungerCheck>().Any() || ev.OfType<TrapTriggered>().Any(), "stopped for nothing");
                        if (ev.OfType<FightStarts>().Any()) Check.True(!ev.OfType<FightStarts>().First().Scouted, "what comes back is not known");
                        if (ex.Pending == PendingKind.Fight) ex.ResolveFight(FightOutcome.Won);
                        if (ex.Pending == PendingKind.Hunger) ex.ResolveHunger(true);
                        found++;
                        ev = ex.MoveTo(target);
                    }
                }
            }
            // one roll per kind in file order, the first hit wins
            double none = 1;
            foreach (var content in rules.ReturnContents) none *= 1 - content.ChanceAt(0);
            Check.Near(1 - none, found / (double)walks, 0.03, "share of return walks with new content");
        }

        private static IEnumerable<(string quest, int length)> WalkQuests()
        {
            yield return (QuestTypes.Explore, 1);
            yield return (QuestTypes.Explore, 3);
            yield return (QuestTypes.Cleanse, 1);
            yield return (QuestTypes.Cleanse, 2);
            yield return (QuestTypes.Gather, 2);
            yield return (QuestTypes.Activate, 2);
            yield return (QuestTypes.InventoryActivate, 2);
            yield return (QuestTypes.KillBoss, 2);
            yield return (QuestTypes.KillBoss, 3);
        }

        public static void WalkthroughCompletesEveryQuestType()
        {
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var (quest, length) in WalkQuests())
                    for (var seed = 0; seed < 25; seed++)
                    {
                        var where = dungeon + " " + quest + " " + length + " seed " + seed;
                        var map = DungeonGenerator.Generate(dungeon, quest, length, 3, seed, Dd1.Generation);
                        var ex = new Exploration(map, Dd1.Raid, Dd1.Curios, seed * 31 + 7) { ScoutBonus = 0.2 };
                        Check.True(!ex.ObjectiveComplete, "complete before the first step, " + where);
                        Check.Equal(map.Objective.StartingItems, ex.QuestItems, "starting quest items, " + where);

                        var explorer = new Explorer(seed);
                        Check.True(explorer.Run(ex), "the explorer did not finish, " + where);
                        Check.Equal(RaidStatus.InProgress, ex.Status, "status, " + where);
                        Check.True(map.Rooms.Where(r => r.Type != RoomType.Secret).All(r => ex.IsRoomVisited(r.Id)), "rooms left unvisited, " + where);
                        Check.True(ex.ObjectiveComplete, "objective not complete (" + ex.Progress + "/" + map.Objective.Required + "), " + where);
                        Check.Equal(1, explorer.Log.Count(l => l.StartsWith("ObjectiveCompleted")), "ObjectiveCompleted events, " + where);
                        Check.True(ex.Progress >= map.Objective.Required, "progress, " + where);

                        // every fight and curio on the way was met
                        Check.True(map.Rooms.Where(r => r.Battle != null).All(r => ex.IsRoomFightWon(r.Id)), "room fights left, " + where);
                        Check.True(map.Rooms.Where(r => r.CurioId != null && r.Type != RoomType.Secret).All(r => ex.IsRoomCurioUsed(r.Id)), "room curios left, " + where);
                        Check.Equal(map.Rooms.Count(r => r.Battle != null), explorer.Log.Count(l => l.StartsWith("FightStarts") && !l.Contains("\"Kind\":0")), "room fights started, " + where);
                        if (map.Objective.Kind == ObjectiveKind.Gather)
                        {
                            Check.Equal(map.Objective.Required, ex.QuestItems, "gathered items, " + where);
                            Check.Equal(map.Objective.Required, explorer.Log.Count(l => l.StartsWith("QuestItemGained")), "QuestItemGained events, " + where);
                        }
                        if (map.QuestType == QuestTypes.InventoryActivate)
                            Check.Equal(map.Objective.StartingItems - map.Objective.Required, ex.QuestItems, "quest items left, " + where);

                        var ended = ex.Leave().OfType<QuestEnded>().Single();
                        Check.True(ended.Status == RaidStatus.Succeeded && ended.Stress == 0, "leaving a finished quest, " + where);
                    }
        }

        public static void ObjectiveCompletesWhenDd1SaysSo()
        {
            // explore: done at DD1's share of the rooms, before the last one
            var map = DungeonGenerator.Generate("weald", QuestTypes.Explore, 2, 1, 11, Dd1.Generation);
            var ex = new Exploration(map, Dd1.Raid, Dd1.Curios, 11);
            RunUntilComplete(ex);
            var visited = map.Rooms.Count(r => r.Type != RoomType.Secret && ex.IsRoomVisited(r.Id));
            Check.Equal(map.Objective.Required, visited, "rooms visited when the explore quest completed");
            Check.True(visited < map.GridRoomCount, "completed before the last room");

            // boss: only the boss fight counts
            map = DungeonGenerator.Generate("cove", QuestTypes.KillBoss, 2, 1, 11, Dd1.Generation);
            ex = new Exploration(map, Dd1.Raid, Dd1.Curios, 11);
            RunUntilComplete(ex);
            Check.True(ex.RoomId == map.FinalRoomId && ex.IsRoomFightWon(map.FinalRoomId), "completed in the boss room");

            // inventory-activate: a curio cannot be activated without its quest item
            map = DungeonGenerator.Generate("crypts", QuestTypes.InventoryActivate, 2, 1, 11, Dd1.Generation);
            var json = new Exploration(map, Dd1.Raid, Dd1.Curios, 11).ToJson();
            json["questItems"] = 0;
            ex = Exploration.FromJson(json, Dd1.Raid, Dd1.Curios);
            ex.ScoutingEnabled = false;
            var quest = map.Rooms.First(r => r.QuestCurio);
            var guard = 0;
            while (ex.RoomId != quest.Id)
            {
                Check.True(++guard < 2000, "never reached the quest curio");
                if (ex.Pending == PendingKind.Fight) ex.ResolveFight(FightOutcome.Won);
                else if (ex.Pending == PendingKind.Trap) ex.ResolveTrap(true);
                else if (ex.Pending == PendingKind.Obstacle) ex.ResolveObstacle(true);
                else if (ex.Pending == PendingKind.Hunger) ex.ResolveHunger(true);
                else if (ex.RoomId < 0) ex.MoveTo(ex.TowardRoomId);
                else
                {
                    var dist = map.Distances(quest.Id);
                    ex.MoveTo(map.HallwaysOf(ex.RoomId).Select(h => h.Other(ex.RoomId)).OrderBy(r => dist[r]).First());
                }
            }
            if (ex.Pending == PendingKind.Fight) ex.ResolveFight(FightOutcome.Won);
            Check.True(ex.CurioHere == quest.CurioId && ex.UseCurio().Count == 0 && !ex.IsRoomCurioUsed(quest.Id), "no quest item, no activation");
        }

        // one call at a time, so the state is looked at the moment the objective completes
        private static void RunUntilComplete(Exploration ex)
        {
            var explorer = new Explorer(11);
            while (!ex.ObjectiveComplete)
                Check.True(!explorer.Run(ex, explorer.Calls + 1), "everything walked and the objective is not complete");
        }

        public static void SaveLoadRoundTripMidDungeon()
        {
            int savesInHallway = 0, savesWithPending = 0;
            foreach (var dungeon in Dd1.Dungeons)
                foreach (var (quest, length) in WalkQuests())
                    for (var seed = 0; seed < 6; seed++)
                    {
                        var where = dungeon + " " + quest + " " + length + " seed " + seed;
                        var map = DungeonGenerator.Generate(dungeon, quest, length, 5, seed, Dd1.Generation);
                        // walking changes the map (things move into walked hallways): each run gets its own copy
                        var copy = DungeonMap.FromJson(map.ToJson());
                        // the uninterrupted run
                        var whole = new Exploration(map, Dd1.Raid, Dd1.Curios, seed) { ScoutBonus = 0.1 };
                        var wholeRun = new Explorer(seed);
                        wholeRun.Run(whole);

                        // the same run, saved and loaded at several points, some of them mid-hallway with a decision pending
                        var ex = new Exploration(copy, Dd1.Raid, Dd1.Curios, seed) { ScoutBonus = 0.1 };
                        var run = new Explorer(seed);
                        for (var stop = 3; !run.Run(ex, stop); stop += 5 + seed)
                        {
                            if (ex.RoomId < 0) savesInHallway++;
                            if (ex.Pending != PendingKind.None) savesWithPending++;
                            var text = ex.ToJson().ToString(Formatting.None);
                            ex = Exploration.FromJson(JObject.Parse(text), Dd1.Raid, Dd1.Curios);
                            Check.Equal(text, ex.ToJson().ToString(Formatting.None), "save, load, save, " + where);
                        }
                        Check.True(wholeRun.Log.SequenceEqual(run.Log), "the loaded run went differently, " + where);
                        Check.Equal(whole.ToJson().ToString(Formatting.None), ex.ToJson().ToString(Formatting.None), "final state, " + where);
                    }

            Check.True(savesInHallway > 100 && savesWithPending > 100, "saves in a hallway: " + savesInHallway + ", with a decision pending: " + savesWithPending);
            Check.True(Throws(() => Exploration.FromJson(new JObject(), Dd1.Raid, Dd1.Curios)), "a save without a map is refused");
        }

        private static bool Throws(Action action)
        {
            try { action(); }
            catch (FormatException) { return true; }
            return false;
        }
    }
}
