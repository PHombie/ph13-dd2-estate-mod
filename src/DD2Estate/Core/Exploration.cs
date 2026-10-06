using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    public enum RaidStatus { InProgress, Succeeded, Abandoned, Failed }

    /// <summary>What the party has to answer before it can walk on.</summary>
    public enum PendingKind { None, Fight, Trap, Obstacle, Hunger }

    public enum FightOutcome { Won, Retreated, Lost }

    /// <summary>
    /// A party walking a <see cref="DungeonMap"/> by DD1's rules, with no presentation in it. Every call returns
    /// the events it caused, in order; the game layer plays them and answers the ones that ask for a decision
    /// (fight, trap, obstacle, hunger). The whole state, map included, goes to JSON and back.
    /// </summary>
    public sealed class Exploration
    {
        public const int Version = 1;

        // per room and per hallway tile
        private const int Known = 1;        // contents shown on the map
        private const int Visited = 2;
        private const int Done = 4;         // room: fight won; tile: content dealt with
        private const int CurioUsed = 8;    // room only
        private const int Sprung = 16;      // trap tile: it went off (a disarmed trap is Done without it)

        private readonly RaidRules _rules;
        private readonly CurioCatalog _curios;
        private Rng _rng;
        private int[] _rooms;
        private int[][] _segments;
        // where the party stood before its last step: a fled fight puts it back there
        private int _prevRoomId = -1, _prevHallwayId = -1, _prevSegment = -1;

        public DungeonMap Map { get; private set; }
        public RaidStatus Status { get; private set; }
        public PendingKind Pending { get; private set; }
        /// <summary>Room the party stands in; -1 in a hallway.</summary>
        public int RoomId { get; private set; } = -1;
        /// <summary>Hallway the party walks; -1 in a room. Kept while it visits a secret room off that hallway.</summary>
        public int HallwayId { get; private set; } = -1;
        public int Segment { get; private set; } = -1;
        /// <summary>The room the party set out for when it entered the hallway. Walking to the other end is backing up.</summary>
        public int TowardRoomId { get; private set; } = -1;
        /// <summary>Torchlight, 0 to 100.</summary>
        public double Light { get; private set; }
        public int Progress { get; private set; }
        public bool ObjectiveComplete { get; private set; }
        /// <summary>Quest items in the party's hands: gathered ones, or the ones an activation uses up.</summary>
        public int QuestItems { get; private set; }

        /// <summary>Scouting chance the party adds to DD1's base chance and the torchlight bonus (trinkets, skills).</summary>
        public double ScoutBonus;
        /// <summary>DD1 plot quest flags <c>is_scouting_enabled</c> and <c>can_retreat</c>.</summary>
        public bool ScoutingEnabled = true, RetreatAllowed = true;
        /// <summary>
        /// DD1's <c>burn_down_enabled</c> of the quest's torch setting (<c>scripts/raid_settings.json</c>): false in
        /// the last descent, where the torch stands at its full light from the first step to the last and
        /// nothing changes it (so the brightest band holds for scouting and surprise).
        /// </summary>
        public bool TorchBurns = true;

        public Exploration(DungeonMap map, RaidRules rules, CurioCatalog curios, long seed)
        {
            Map = map;
            _rules = rules;
            _curios = curios;
            _rng = new Rng(seed);
            _rooms = new int[map.Rooms.Count];
            _segments = new int[map.Hallways.Count][];
            for (var i = 0; i < _segments.Length; i++) _segments[i] = new int[map.Hallways[i].Segments.Count];
            Light = RaidRules.MaxLight;
            RoomId = map.EntranceId;
            _rooms[RoomId] = Known | Visited;
            QuestItems = map.Objective != null ? map.Objective.StartingItems : 0;
            UpdateObjective(new List<ExplorationEvent>());
        }

        public bool InSecretRoom => RoomId >= 0 && HallwayId >= 0;

        public LightBand LightBand => _rules.BandFor(Light);

        // ---- what the map shows ----

        public bool IsRoomVisited(int roomId) => (_rooms[roomId] & Visited) != 0;

        /// <summary>The room's type and contents are known (visited or scouted); otherwise DD1 shows an unknown room.</summary>
        public bool IsRoomKnown(int roomId) => (_rooms[roomId] & Known) != 0;

        public bool IsRoomFightWon(int roomId) => (_rooms[roomId] & Done) != 0;

        public bool IsRoomCurioUsed(int roomId) => (_rooms[roomId] & CurioUsed) != 0;

        public bool IsSegmentVisited(int hallwayId, int segment) => (_segments[hallwayId][segment] & Visited) != 0;

        public bool IsSegmentKnown(int hallwayId, int segment) => (_segments[hallwayId][segment] & Known) != 0;

        /// <summary>The tile's fight, trap, obstacle, hunger or curio is behind the party.</summary>
        public bool IsSegmentDone(int hallwayId, int segment) => (_segments[hallwayId][segment] & Done) != 0;

        /// <summary>The tile's trap went off under the party; false for one that was disarmed or still waits.</summary>
        public bool IsTrapSprung(int hallwayId, int segment) => (_segments[hallwayId][segment] & Sprung) != 0;

        // ---- walking ----

        public bool CanMoveTo(int roomId)
        {
            if (Status != RaidStatus.InProgress || InSecretRoom) return false;
            if (RoomId >= 0) return Pending == PendingKind.None && roomId != RoomId && Map.HallwayBetween(RoomId, roomId) != null;
            var hall = Map.Hallways[HallwayId];
            if (roomId != hall.RoomA && roomId != hall.RoomB) return false;
            if (Pending == PendingKind.Fight || Pending == PendingKind.Hunger) return false;
            // a spotted trap or an obstacle stops the way forward only
            return Pending == PendingKind.None || roomId != TowardRoomId;
        }

        /// <summary>
        /// Walks toward a room next to the current one (or to either end of the current hallway) until the party
        /// arrives or something wants attention: a fight, a trap, an obstacle, hunger, a curio, a hidden door.
        /// Call again to walk on.
        /// </summary>
        public List<ExplorationEvent> MoveTo(int roomId)
        {
            var events = new List<ExplorationEvent>();
            while (StepToward(roomId, events)) { }
            return events;
        }

        /// <summary>One tile toward a room, for a presentation that moves the party itself.</summary>
        public List<ExplorationEvent> Step(int roomId)
        {
            var events = new List<ExplorationEvent>();
            StepToward(roomId, events);
            return events;
        }

        // false: arrived, not possible, or stopped by what the tile holds
        private bool StepToward(int roomId, List<ExplorationEvent> events)
        {
            if (!CanMoveTo(roomId)) return false;
            if (RoomId >= 0)
            {
                var entered = Map.HallwayBetween(RoomId, roomId);
                RollReturnContent(entered);
                SetPrevious(RoomId, -1, -1);
                var first = entered.RoomA == RoomId ? 0 : entered.Segments.Count - 1;
                HallwayId = entered.Id;
                TowardRoomId = roomId;
                RoomId = -1;
                return EnterSegment(entered, first, false, events, true);
            }

            var hall = Map.Hallways[HallwayId];
            Pending = PendingKind.None;
            var next = Segment + (roomId == hall.RoomB ? 1 : -1);
            SetPrevious(-1, HallwayId, Segment);
            if (next < 0 || next >= hall.Segments.Count)
            {
                EnterRoom(roomId, events);
                return false;
            }
            return EnterSegment(hall, next, roomId != TowardRoomId, events);
        }

        private void SetPrevious(int roomId, int hallwayId, int segment)
        {
            _prevRoomId = roomId;
            _prevHallwayId = hallwayId;
            _prevSegment = segment;
        }

        private bool EnterSegment(Hallway hall, int index, bool backingUp, List<ExplorationEvent> events, bool fromRoom = false)
        {
            Segment = index;
            var segment = hall.Segments[index];
            var flags = _segments[hall.Id][index];
            var first = (flags & Visited) == 0;
            var known = (flags & Known) != 0;
            events.Add(new EnteredSegment { HallwayId = hall.Id, Segment = index, FirstVisit = first, BackingUp = backingUp, FromRoom = fromRoom });

            ChangeLight(-(!first ? _rules.LightLossVisited : known ? _rules.LightLossScouted : _rules.LightLossUnknown), events);
            if (_rng.Chance(backingUp ? _rules.BackingUpStressChance : _rules.WalkStressChance))
                events.Add(new WalkStress { Amount = backingUp ? _rules.BackingUpStress : _rules.WalkStress, BackingUp = backingUp });

            // a hidden door stays hidden to a party that walks past it unscouted
            flags |= Visited;
            if (segment.Content != HallContent.Secret) flags |= Known;
            _segments[hall.Id][index] = flags;
            if ((flags & Done) != 0) return true;

            switch (segment.Content)
            {
                case HallContent.Battle:
                    Pending = PendingKind.Fight;
                    events.Add(new FightStarts
                    {
                        Slot = segment.Battle ?? new EncounterSlot { Kind = EncounterKind.Hallway, Tier = Map.Tier },
                        Scouted = known
                    });
                    return false;
                case HallContent.Trap:
                    if (known)
                    {
                        Pending = PendingKind.Trap;
                        events.Add(new TrapSpotted { TrapId = segment.PropId });
                        return false;
                    }
                    _segments[hall.Id][index] |= Done | Sprung;
                    events.Add(new TrapTriggered { TrapId = segment.PropId, WasSpotted = false });
                    return false;
                case HallContent.Obstacle:
                    Pending = PendingKind.Obstacle;
                    events.Add(new ObstacleBlocks { ObstacleId = segment.PropId });
                    return false;
                case HallContent.Hunger:
                    Pending = PendingKind.Hunger;
                    events.Add(new HungerCheck());
                    return false;
                case HallContent.Curio:
                    if (!first) return true;
                    events.Add(new CurioFound { CurioId = segment.PropId });
                    return false;
                case HallContent.Secret:
                    if (!known) return true;
                    _segments[hall.Id][index] |= Done;
                    events.Add(new SecretRoomFound { RoomId = segment.SecretRoomId });
                    return false;
                default:
                    return true;
            }
        }

        private void EnterRoom(int roomId, List<ExplorationEvent> events)
        {
            var room = Map.Rooms[roomId];
            var first = (_rooms[roomId] & Visited) == 0;
            var known = (_rooms[roomId] & Known) != 0;
            RoomId = roomId;
            HallwayId = -1;
            Segment = -1;
            TowardRoomId = -1;
            _rooms[roomId] |= Known | Visited;
            events.Add(new RoomEntered { RoomId = roomId, FirstVisit = first });
            if (first)
            {
                UpdateObjective(events);
                TryScout(events);
            }
            if (room.Battle != null && (_rooms[roomId] & Done) == 0)
            {
                Pending = PendingKind.Fight;
                events.Add(new FightStarts { Slot = room.Battle, Scouted = known });
            }
            else if (first) AnnounceCurio(room, events);
        }

        private void AnnounceCurio(Room room, List<ExplorationEvent> events)
        {
            if (room.CurioId != null && (_rooms[room.Id] & CurioUsed) == 0)
                events.Add(new CurioFound { CurioId = room.CurioId, QuestCurio = room.QuestCurio });
        }

        // Walking a hallway again, something new may have moved in (rules.json corridor_return_content).
        // Rolled once per walk through a hallway whose tiles were all visited; how often DD1 rolls is not in the data.
        private void RollReturnContent(Hallway hall)
        {
            var flags = _segments[hall.Id];
            foreach (var f in flags)
                if ((f & Visited) == 0) return;
            foreach (var content in _rules.ReturnContents)
            {
                if (!_rng.Chance(content.ChanceAt(Light))) continue;
                if (content.Content == HallContent.Trap && Map.TrapId == null) continue;
                var free = new List<int>();
                for (var i = 0; i < flags.Length; i++)
                {
                    var c = hall.Segments[i].Content;
                    if (c == HallContent.Empty || ((flags[i] & Done) != 0 && c != HallContent.Curio && c != HallContent.Secret)) free.Add(i);
                }
                if (free.Count == 0) return;
                var index = free[_rng.Next(free.Count)];
                var segment = hall.Segments[index];
                segment.Content = content.Content;
                segment.Battle = content.Content == HallContent.Battle ? new EncounterSlot { Kind = EncounterKind.Hallway, Tier = Map.Tier } : null;
                segment.PropId = content.Content == HallContent.Trap ? Map.TrapId : null;
                flags[index] = Visited;     // walked before, but what is there now is not known
                return;
            }
        }

        // ---- answers ----

        /// <summary>
        /// A fight the map did not place begins where the party stands: a wanderer called up at a curio (DD1's
        /// "Summon" result: the Shambler at its altar). It is answered like any other, with
        /// <see cref="ResolveFight"/>: a party that flees stands where it came from. Empty when the party is
        /// not free to fight (something else waits for an answer, or the expedition is over).
        /// </summary>
        public List<ExplorationEvent> StartFight(string wanderer)
        {
            var events = new List<ExplorationEvent>();
            if (Status != RaidStatus.InProgress || Pending != PendingKind.None || InSecretRoom) return events;
            Pending = PendingKind.Fight;
            events.Add(new FightStarts
            {
                Slot = new EncounterSlot { Kind = RoomId >= 0 ? EncounterKind.Room : EncounterKind.Hallway, Tier = Map.Tier },
                // the party called it up itself
                Scouted = true,
                Wanderer = wanderer
            });
            return events;
        }

        public List<ExplorationEvent> ResolveFight(FightOutcome outcome)
        {
            var events = new List<ExplorationEvent>();
            if (Pending != PendingKind.Fight) return events;
            Pending = PendingKind.None;
            switch (outcome)
            {
                case FightOutcome.Won:
                    if (RoomId >= 0)
                    {
                        _rooms[RoomId] |= Done;
                        UpdateObjective(events);
                        AnnounceCurio(Map.Rooms[RoomId], events);
                    }
                    else _segments[HallwayId][Segment] |= Done;
                    break;
                case FightOutcome.Retreated:
                    // DD1 behaviour, not in data: a party that flees stands where it came from, and the monsters stay
                    if (_prevHallwayId >= 0)
                    {
                        if (RoomId >= 0) TowardRoomId = RoomId;
                        HallwayId = _prevHallwayId;
                        Segment = _prevSegment;
                        RoomId = -1;
                    }
                    else
                    {
                        RoomId = _prevRoomId;
                        HallwayId = -1;
                        Segment = -1;
                        TowardRoomId = -1;
                    }
                    break;
                default:
                    End(RaidStatus.Failed, events);
                    break;
            }
            return events;
        }

        /// <summary>
        /// A spotted trap: disarmed, or sprung (a failed attempt, or the party walking on regardless). The disarm
        /// roll is the game layer's: hero skill + <see cref="RaidRules.TrapScoutDisarmBonus"/> - <see cref="RaidRules.TrapPenalty"/>.
        /// </summary>
        public List<ExplorationEvent> ResolveTrap(bool disarmed)
        {
            var events = new List<ExplorationEvent>();
            if (Pending != PendingKind.Trap) return events;
            Pending = PendingKind.None;
            _segments[HallwayId][Segment] |= Done;
            var trapId = Map.Hallways[HallwayId].Segments[Segment].PropId;
            if (disarmed) events.Add(new TrapDisarmed { TrapId = trapId });
            else
            {
                _segments[HallwayId][Segment] |= Sprung;
                events.Add(new TrapTriggered { TrapId = trapId, WasSpotted = true });
            }
            return events;
        }

        /// <summary>Clears the obstacle, with the shovel or by hand; by hand costs torchlight (and health and stress, applied by the game layer).</summary>
        public List<ExplorationEvent> ResolveObstacle(bool usedShovel)
        {
            var events = new List<ExplorationEvent>();
            if (Pending != PendingKind.Obstacle) return events;
            Pending = PendingKind.None;
            _segments[HallwayId][Segment] |= Done;
            events.Add(new ObstacleCleared { ObstacleId = Map.Hallways[HallwayId].Segments[Segment].PropId, UsedShovel = usedShovel });
            if (!usedShovel) ChangeLight(_rules.Obstacle.Torchlight, events);
            return events;
        }

        /// <summary>The party ate or went hungry; food, healing and damage are the game layer's (<see cref="RaidRules.HungerHeal"/>).</summary>
        public List<ExplorationEvent> ResolveHunger(bool ate)
        {
            var events = new List<ExplorationEvent>();
            if (Pending != PendingKind.Hunger) return events;
            Pending = PendingKind.None;
            _segments[HallwayId][Segment] |= Done;
            events.Add(new HungerResolved { Ate = ate });
            return events;
        }

        // ---- curios ----

        /// <summary>The unused curio where the party stands; null when there is none (or a fight still guards it).</summary>
        public string CurioHere
        {
            get
            {
                if (Status != RaidStatus.InProgress || Pending != PendingKind.None) return null;
                if (RoomId >= 0) return (_rooms[RoomId] & CurioUsed) == 0 ? Map.Rooms[RoomId].CurioId : null;
                var segment = Map.Hallways[HallwayId].Segments[Segment];
                return segment.Content == HallContent.Curio && (_segments[HallwayId][Segment] & Done) == 0 ? segment.PropId : null;
            }
        }

        /// <summary>
        /// Investigates the curio here, with an item or bare hands. The outcome is rolled from the DD1 tables and
        /// comes back in a <see cref="CurioUsed"/> event; a scouting outcome is applied to the map at once.
        /// Empty when there is nothing to use, or a quest curio needs a quest item the party has run out of.
        /// </summary>
        public List<ExplorationEvent> UseCurio(string itemId = null)
        {
            var events = new List<ExplorationEvent>();
            var curioId = CurioHere;
            if (curioId == null) return events;

            if (RoomId >= 0 && Map.Rooms[RoomId].QuestCurio)
            {
                var objective = Map.Objective;
                var consumes = objective.Kind == ObjectiveKind.Activate && objective.ItemId != null;
                if (consumes && QuestItems <= 0) return events;
                if (consumes) QuestItems--;
                _rooms[RoomId] |= CurioUsed;
                events.Add(new CurioUsed
                {
                    Outcome = new CurioOutcome { CurioId = curioId, Type = CurioResultTypes.QuestItem, ItemId = consumes ? objective.ItemId : null }
                });
                if (objective.Kind == ObjectiveKind.Gather)
                {
                    QuestItems++;
                    events.Add(new QuestItemGained { ItemId = objective.ItemId });
                }
                UpdateObjective(events);
                return events;
            }

            if (RoomId >= 0) _rooms[RoomId] |= CurioUsed;
            else _segments[HallwayId][Segment] |= Done;
            var outcome = _curios != null ? _curios.Roll(curioId, itemId, _rng) : new CurioOutcome { CurioId = curioId };
            events.Add(new CurioUsed { Outcome = outcome });
            if (outcome.Type == CurioResultTypes.Scouting && CurioCatalog.ParseScouting(outcome.Value, out var reach, out var target))
                Reveal(reach, target, false, events);
            return events;
        }

        /// <summary>
        /// The party turns again to what stands where it is: the curio it left alone, the hidden door it knows
        /// of. DD1 leaves both to a click ("click or press W"), any number of times; nothing is rolled and
        /// nothing changes. Empty when there is nothing to turn to, or something else wants an answer first.
        /// </summary>
        public List<ExplorationEvent> Examine()
        {
            var events = new List<ExplorationEvent>();
            var curioId = CurioHere;
            if (curioId != null) events.Add(new CurioFound { CurioId = curioId, QuestCurio = RoomId >= 0 && Map.Rooms[RoomId].QuestCurio, Again = true });
            else if (SecretRoomHere >= 0) events.Add(new SecretRoomFound { RoomId = SecretRoomHere });
            return events;
        }

        // ---- scouting ----

        /// <summary>
        /// The chance that the next new room scouts ahead: DD1's base chance, the torchlight's share and what
        /// the party adds (<see cref="ScoutBonus"/>). 0 in a quest that forbids scouting.
        /// </summary>
        public double ScoutChance
        {
            get
            {
                if (!ScoutingEnabled) return 0;
                return Math.Max(0, Math.Min(1, _rules.ScoutChanceBase + LightBand.Value("player_scouting_increase") / 100 + ScoutBonus));
            }
        }

        /// <summary>
        /// Reveals map contents the DD1 way: rooms up to <paramref name="reach"/> hallways from the party (0: the
        /// whole map) and the hallways leading to them. Target: all, curios, traps, obstacles, hall_battles,
        /// room_battles. Hidden doors show only when asked for.
        /// </summary>
        public List<ExplorationEvent> Scout(int reach, string target = "all", bool secretDoors = false)
        {
            var events = new List<ExplorationEvent>();
            if (Status == RaidStatus.InProgress) Reveal(reach, target, secretDoors, events);
            return events;
        }

        private void TryScout(List<ExplorationEvent> events)
        {
            if (!ScoutingEnabled) return;
            var chance = _rules.ScoutChanceBase + LightBand.Value("player_scouting_increase") / 100 + ScoutBonus;
            if (!_rng.Chance(chance)) return;
            // DD1 behaviour, not in data: a scout sees the hallways out of the room and the rooms behind them.
            // A critical one (scouting_crit_success) sees a room further and, as DD1's own tutorial text says,
            // is what reveals a secret room.
            var critical = _rng.Chance(_rules.ScoutCritChance);
            Reveal(critical ? 2 : 1, "all", critical, events);
        }

        private void Reveal(int reach, string target, bool secretDoors, List<ExplorationEvent> events)
        {
            var all = target == "all";
            var dist = Map.Distances(RoomId >= 0 && !InSecretRoom ? RoomId : TowardRoomId);
            var scouted = new Scouted();
            foreach (var room in Map.Rooms)
            {
                // a secret room shows as its hallway tile
                if (room.Type == RoomType.Secret || (_rooms[room.Id] & Known) != 0) continue;
                if (reach > 0 && (dist[room.Id] < 0 || dist[room.Id] > reach)) continue;
                if (!all && !(target == "room_battles" && room.Battle != null) && !(target == "curios" && room.CurioId != null)) continue;
                _rooms[room.Id] |= Known;
                scouted.Rooms.Add(room.Id);
            }

            var wanted = target == "curios" ? HallContent.Curio
                : target == "traps" ? HallContent.Trap
                : target == "obstacles" ? HallContent.Obstacle
                : target == "hall_battles" ? HallContent.Battle
                : HallContent.Empty;
            foreach (var hall in Map.Hallways)
            {
                var near = Math.Min(dist[hall.RoomA], dist[hall.RoomB]);
                if (reach > 0 && (near < 0 || near >= reach)) continue;
                var any = false;
                for (var i = 0; i < hall.Segments.Count; i++)
                {
                    if ((_segments[hall.Id][i] & Known) != 0) continue;
                    var content = hall.Segments[i].Content;
                    if (content == HallContent.Secret ? !secretDoors : !all && (wanted == HallContent.Empty || content != wanted)) continue;
                    _segments[hall.Id][i] |= Known;
                    scouted.Tiles.Add(new ScoutedTile { HallwayId = hall.Id, Segment = i, Content = content });
                    any = true;
                }
                if (any) scouted.Hallways.Add(hall.Id);
            }
            if (scouted.Rooms.Count > 0 || scouted.Hallways.Count > 0) events.Add(scouted);
        }

        // ---- secret rooms ----

        /// <summary>The secret room behind the tile the party stands on, once its door is known; -1 when there is none to enter.</summary>
        public int SecretRoomHere
        {
            get
            {
                if (Status != RaidStatus.InProgress || Pending != PendingKind.None || RoomId >= 0 || HallwayId < 0) return -1;
                var segment = Map.Hallways[HallwayId].Segments[Segment];
                if (segment.Content != HallContent.Secret || (_segments[HallwayId][Segment] & Known) == 0) return -1;
                return Map.GetRoom(segment.SecretRoomId) != null ? segment.SecretRoomId : -1;
            }
        }

        /// <summary>Through the hidden door of the current tile, once a critical scout has shown it.</summary>
        public List<ExplorationEvent> EnterSecretRoom()
        {
            var events = new List<ExplorationEvent>();
            if (Status != RaidStatus.InProgress || Pending != PendingKind.None || RoomId >= 0) return events;
            var segment = Map.Hallways[HallwayId].Segments[Segment];
            var room = Map.GetRoom(segment.SecretRoomId);
            if (segment.Content != HallContent.Secret || room == null || (_segments[HallwayId][Segment] & Known) == 0) return events;
            var first = (_rooms[room.Id] & Visited) == 0;
            RoomId = room.Id;
            _rooms[room.Id] |= Known | Visited;
            events.Add(new RoomEntered { RoomId = room.Id, FirstVisit = first });
            if (first) AnnounceCurio(room, events);
            return events;
        }

        /// <summary>Back to the hallway tile the secret room opened from.</summary>
        public bool LeaveSecretRoom()
        {
            if (!InSecretRoom) return false;
            RoomId = -1;
            return true;
        }

        // ---- light ----

        /// <summary>Torchlight change from outside: a DD1 "Darkness N" / "Light N" effect, an ambush, a camp.</summary>
        public List<ExplorationEvent> AddLight(double delta)
        {
            var events = new List<ExplorationEvent>();
            ChangeLight(delta, events);
            return events;
        }

        /// <summary>One torch burnt. The game layer takes it out of the inventory.</summary>
        public List<ExplorationEvent> UseTorch() => AddLight(_rules.TorchLight);

        private void ChangeLight(double delta, List<ExplorationEvent> events)
        {
            // DD1 (Raid::ChangeTorch): with the torch setting's burn-down off nothing moves the torch, not the
            // tiles walked, not a trap, an obstacle, a curio or a torch from the bag
            if (!TorchBurns) return;
            var before = Light;
            var after = Math.Max(0, Math.Min(RaidRules.MaxLight, before + delta));
            if (after == before) return;
            Light = after;
            var band = _rules.BandIndex(after);
            events.Add(new LightChanged { Before = before, After = after, Band = band, BandChanged = band != _rules.BandIndex(before) });
        }

        // ---- objective and the way out ----

        private void UpdateObjective(List<ExplorationEvent> events)
        {
            var objective = Map.Objective;
            if (objective == null) return;
            var progress = 0;
            foreach (var room in Map.Rooms)
            {
                var flags = _rooms[room.Id];
                bool counts;
                switch (objective.Kind)
                {
                    case ObjectiveKind.Explore: counts = room.Type != RoomType.Secret && (flags & Visited) != 0; break;
                    case ObjectiveKind.Cleanse: counts = room.Battle != null && room.Type != RoomType.Boss && (flags & Done) != 0; break;
                    case ObjectiveKind.KillBoss: counts = room.Type == RoomType.Boss && (flags & Done) != 0; break;
                    default: counts = room.QuestCurio && (flags & CurioUsed) != 0; break;
                }
                if (counts) progress++;
            }
            if (progress == Progress) return;
            Progress = progress;
            events.Add(new ObjectiveProgress { Progress = progress, Required = objective.Required });
            if (ObjectiveComplete || progress < objective.Required) return;
            ObjectiveComplete = true;
            events.Add(new ObjectiveCompleted());
        }

        /// <summary>
        /// Leaving is possible out of combat: as a success once the objective is complete, else as abandoning
        /// the quest, which some DD1 plot quests forbid (<see cref="RetreatAllowed"/>).
        /// </summary>
        public bool CanLeave => Status == RaidStatus.InProgress && Pending != PendingKind.Fight && (ObjectiveComplete || RetreatAllowed);

        /// <summary>Back to the hamlet. Abandoning costs every hero the stress of DD1's <c>quest.exit_penalty.json</c>.</summary>
        public List<ExplorationEvent> Leave()
        {
            var events = new List<ExplorationEvent>();
            if (CanLeave) End(ObjectiveComplete ? RaidStatus.Succeeded : RaidStatus.Abandoned, events);
            return events;
        }

        /// <summary>
        /// DD1's <c>retreat_always_from_raid</c> (the Shrieker's perch): a party that flees the fight has
        /// nowhere to stand but the way home, so its flight is the quest abandoned. Called in place of
        /// <see cref="ResolveFight"/> for a fight fled; empty when no fight is on.
        /// </summary>
        public List<ExplorationEvent> FleeRaid()
        {
            var events = new List<ExplorationEvent>();
            if (Status != RaidStatus.InProgress || Pending != PendingKind.Fight) return events;
            End(RaidStatus.Abandoned, events);
            return events;
        }

        private void End(RaidStatus status, List<ExplorationEvent> events)
        {
            Status = status;
            Pending = PendingKind.None;
            events.Add(new QuestEnded { Status = status, Stress = status == RaidStatus.Abandoned ? _rules.AbandonStress : 0 });
        }

        // ---- save ----

        public JObject ToJson()
        {
            var segments = new JArray();
            foreach (var hall in _segments) segments.Add(new JArray(hall));
            return new JObject
            {
                ["version"] = Version,
                ["status"] = Status.ToString(),
                ["pending"] = Pending.ToString(),
                ["room"] = RoomId,
                ["hallway"] = HallwayId,
                ["segment"] = Segment,
                ["toward"] = TowardRoomId,
                ["prevRoom"] = _prevRoomId,
                ["prevHallway"] = _prevHallwayId,
                ["prevSegment"] = _prevSegment,
                ["light"] = Light,
                ["progress"] = Progress,
                ["complete"] = ObjectiveComplete,
                ["questItems"] = QuestItems,
                ["scoutBonus"] = ScoutBonus,
                ["scouting"] = ScoutingEnabled,
                ["retreat"] = RetreatAllowed,
                ["torchBurns"] = TorchBurns,
                // a 64-bit state does not survive as a JSON number
                ["rng"] = _rng.State.ToString(CultureInfo.InvariantCulture),
                ["rooms"] = new JArray(_rooms),
                ["segments"] = segments,
                ["map"] = Map.ToJson()
            };
        }

        public static Exploration FromJson(JObject json, RaidRules rules, CurioCatalog curios)
        {
            if (!(json["map"] is JObject mapJson)) throw new FormatException("Exploration save: no map.");
            var e = new Exploration(DungeonMap.FromJson(mapJson), rules, curios, 0)
            {
                Status = Json.Enum(json["status"], RaidStatus.InProgress),
                Pending = Json.Enum(json["pending"], PendingKind.None),
                RoomId = Json.Int(json["room"], -1),
                HallwayId = Json.Int(json["hallway"], -1),
                Segment = Json.Int(json["segment"], -1),
                TowardRoomId = Json.Int(json["toward"], -1),
                _prevRoomId = Json.Int(json["prevRoom"], -1),
                _prevHallwayId = Json.Int(json["prevHallway"], -1),
                _prevSegment = Json.Int(json["prevSegment"], -1),
                Light = Json.Number(json["light"], RaidRules.MaxLight),
                Progress = Json.Int(json["progress"], 0),
                ObjectiveComplete = Json.Bool(json["complete"], false),
                QuestItems = Json.Int(json["questItems"], 0),
                ScoutBonus = Json.Number(json["scoutBonus"], 0),
                ScoutingEnabled = Json.Bool(json["scouting"], true),
                RetreatAllowed = Json.Bool(json["retreat"], true),
                TorchBurns = Json.Bool(json["torchBurns"], true)
            };
            if (ulong.TryParse((string)json["rng"], NumberStyles.None, CultureInfo.InvariantCulture, out var state)) e._rng = Rng.FromState(state);

            var rooms = json["rooms"] as JArray;
            var segments = json["segments"] as JArray;
            if (rooms == null || rooms.Count != e._rooms.Length || segments == null || segments.Count != e._segments.Length)
                throw new FormatException("Exploration save: state does not match its map.");
            for (var i = 0; i < e._rooms.Length; i++) e._rooms[i] = Json.Int(rooms[i], 0);
            for (var h = 0; h < e._segments.Length; h++)
            {
                var hall = segments[h] as JArray;
                if (hall == null || hall.Count != e._segments[h].Length) throw new FormatException("Exploration save: state does not match its map.");
                for (var i = 0; i < hall.Count; i++) e._segments[h][i] = Json.Int(hall[i], 0);
            }

            var inRoom = e.RoomId >= 0 && e.RoomId < e._rooms.Length;
            var inHall = e.HallwayId >= 0 && e.HallwayId < e._segments.Length && e.Segment >= 0 && e.Segment < e._segments[e.HallwayId].Length;
            if (e.HallwayId < 0 ? !inRoom : !inHall) throw new FormatException("Exploration save: the party is nowhere on its map.");
            return e;
        }
    }
}
