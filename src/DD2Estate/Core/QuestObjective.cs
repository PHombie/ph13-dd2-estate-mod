using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>DD1 quest type ids, as used in <c>campaign/quest</c> and <c>scripts/map_generator.darkest</c>.</summary>
    public static class QuestTypes
    {
        public const string Explore = "explore";
        public const string Cleanse = "cleanse";
        public const string Gather = "gather";
        public const string Activate = "activate";
        public const string InventoryActivate = "inventory_activate";
        public const string KillBoss = "kill_boss";

        public static readonly string[] All = { Explore, Cleanse, Gather, Activate, InventoryActivate, KillBoss };
    }

    public enum ObjectiveKind { Explore, Cleanse, Gather, Activate, KillBoss }

    /// <summary>What finishes the quest in one generated dungeon.</summary>
    public sealed class QuestObjective
    {
        public ObjectiveKind Kind;
        /// <summary>DD1 goal id (<c>quest.types.json</c>), for texts and icons. Null for boss quests: the plot quest names the boss.</summary>
        public string GoalId;
        /// <summary>Rooms to visit, room battles to win, items to gather, curios to activate, or 1 boss.</summary>
        public int Required;
        /// <summary>Quest curio of gather and activate quests.</summary>
        public string CurioId;
        /// <summary>Gather: the quest item each curio yields. Inventory-activate: the quest item one activation uses up.</summary>
        public string ItemId;
        /// <summary>Quest items handed to the party at the start (inventory-activate).</summary>
        public int StartingItems;

        public JObject ToJson()
        {
            var json = new JObject { ["kind"] = Kind.ToString(), ["required"] = Required };
            if (GoalId != null) json["goal"] = GoalId;
            if (CurioId != null) json["curio"] = CurioId;
            if (ItemId != null) json["item"] = ItemId;
            if (StartingItems > 0) json["startingItems"] = StartingItems;
            return json;
        }

        public static QuestObjective FromJson(JToken json)
        {
            if (json == null || json.Type != JTokenType.Object) return null;
            return new QuestObjective
            {
                Kind = Json.Enum(json["kind"], ObjectiveKind.Explore),
                Required = Json.Int(json["required"], 1),
                GoalId = (string)json["goal"],
                CurioId = (string)json["curio"],
                ItemId = (string)json["item"],
                StartingItems = Json.Int(json["startingItems"], 0)
            };
        }
    }
}
