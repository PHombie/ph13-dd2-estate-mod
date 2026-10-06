using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>Something that happened during one call into <see cref="Exploration"/>, in the order it happened.</summary>
    public abstract class ExplorationEvent
    {
        public override string ToString() => GetType().Name;
    }

    public sealed class EnteredSegment : ExplorationEvent
    {
        public int HallwayId, Segment;
        public bool FirstVisit, BackingUp;
        /// <summary>The party has just left a room: this is the hallway's first tile on its way.</summary>
        public bool FromRoom;
    }

    public sealed class RoomEntered : ExplorationEvent
    {
        public int RoomId;
        public bool FirstVisit;
    }

    public sealed class LightChanged : ExplorationEvent
    {
        public double Before, After;
        /// <summary>Index into <see cref="RaidRules.LightBands"/> after the change; 0 is the brightest.</summary>
        public int Band;
        public bool BandChanged;
    }

    /// <summary>DD1 hallway stress: the amount lands on one hero of the game layer's choosing.</summary>
    public sealed class WalkStress : ExplorationEvent
    {
        public double Amount;
        public bool BackingUp;
    }

    /// <summary>A scouted trap ahead: answer with <see cref="Exploration.ResolveTrap"/> or back away.</summary>
    public sealed class TrapSpotted : ExplorationEvent
    {
        public string TrapId;
    }

    /// <summary>The trap went off: apply <see cref="RaidRules.Traps"/>[TrapId].For(tier) fail effects to a hero.</summary>
    public sealed class TrapTriggered : ExplorationEvent
    {
        public string TrapId;
        /// <summary>False when the party walked into a trap it had not seen.</summary>
        public bool WasSpotted;
    }

    public sealed class TrapDisarmed : ExplorationEvent
    {
        public string TrapId;
    }

    /// <summary>The way forward is blocked: answer with <see cref="Exploration.ResolveObstacle"/> or back away.</summary>
    public sealed class ObstacleBlocks : ExplorationEvent
    {
        public string ObstacleId;
    }

    /// <summary>Without the shovel the torchlight cost is already applied; the fail effects and health loss are the game layer's.</summary>
    public sealed class ObstacleCleared : ExplorationEvent
    {
        public string ObstacleId;
        public bool UsedShovel;
    }

    /// <summary>The party must eat or starve: answer with <see cref="Exploration.ResolveHunger"/>.</summary>
    public sealed class HungerCheck : ExplorationEvent
    {
    }

    public sealed class HungerResolved : ExplorationEvent
    {
        public bool Ate;
    }

    /// <summary>A fight begins: answer with <see cref="Exploration.ResolveFight"/>.</summary>
    public sealed class FightStarts : ExplorationEvent
    {
        public EncounterSlot Slot;
        /// <summary>The party knew about it (DD1: a scouted fight cannot surprise the party).</summary>
        public bool Scouted;
        /// <summary>
        /// The DD1 monster of a fight that is not the tile's own ("shambler" called up at its altar:
        /// <see cref="Exploration.StartFight"/>); null for the fight the map placed there.
        /// </summary>
        public string Wanderer;
    }

    /// <summary>
    /// A curio is within reach: <see cref="Exploration.UseCurio"/> is optional. Raised when the party first
    /// comes to it, and again whenever it turns back to one it left alone (<see cref="Exploration.Examine"/>).
    /// </summary>
    public sealed class CurioFound : ExplorationEvent
    {
        public string CurioId;
        public bool QuestCurio;
        /// <summary>The party has turned back to a curio it left alone; false when it has just come to it.</summary>
        public bool Again;
    }

    public sealed class CurioUsed : ExplorationEvent
    {
        public CurioOutcome Outcome;
    }

    public sealed class QuestItemGained : ExplorationEvent
    {
        public string ItemId;
    }

    /// <summary>A hallway tile whose contents a scout has just made known, and what it holds.</summary>
    public struct ScoutedTile
    {
        public int HallwayId, Segment;
        public HallContent Content;
    }

    /// <summary>The map shows more: rooms and hallways with newly known contents.</summary>
    public sealed class Scouted : ExplorationEvent
    {
        public List<int> Rooms = new List<int>();
        public List<int> Hallways = new List<int>();
        /// <summary>The newly known tiles of <see cref="Hallways"/>, each hallway from its RoomA end. Empty tiles are in it too: the map shows them as seen.</summary>
        public List<ScoutedTile> Tiles = new List<ScoutedTile>();
    }

    /// <summary>
    /// A known hidden door is here: <see cref="Exploration.EnterSecretRoom"/> is optional. Raised when the party
    /// first stops at it, and again whenever it turns back to it (<see cref="Exploration.Examine"/>).
    /// </summary>
    public sealed class SecretRoomFound : ExplorationEvent
    {
        public int RoomId;
    }

    public sealed class ObjectiveProgress : ExplorationEvent
    {
        public int Progress, Required;
    }

    /// <summary>The quest goal is met. The party may go on exploring; <see cref="Exploration.Leave"/> ends the quest as a success.</summary>
    public sealed class ObjectiveCompleted : ExplorationEvent
    {
    }

    public sealed class QuestEnded : ExplorationEvent
    {
        public RaidStatus Status;
        /// <summary>Stress for every hero of the party (abandoning a quest).</summary>
        public double Stress;
    }
}
