using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Actor;
using Assets.Code.Combat.Events;
using Assets.Code.Events;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Roster;
using Assets.Code.Run;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// One expedition: a DD1-rules dungeon (<see cref="Exploration"/>) presented with the corridor view and
    /// played with DD2 fights. The exploration state is the truth; the view is rebuilt from it after anything
    /// that can move the party (entering a room, fleeing a fight, loading).
    /// </summary>
    internal partial class DungeonRun
    {
        public static DungeonRun Current { get; private set; }

        /// <summary>DD1 stress runs 0..200, DD2's 0..10.</summary>
        private const double StressScale = 10.0 / 200.0;

        private readonly Exploration _x;
        private readonly DungeonMap _map;
        private Quest _quest;
        private readonly RaidRules _raid;
        private readonly CurioCatalog _curios;
        private readonly LootTables _loot;
        private readonly Rng _rng;
        private readonly ItemCatalog _items = InventoryContent.Items;
        private readonly InventoryRaidRules _supply = InventoryContent.Rules;
        private readonly Core.Inventory _bag;
        // food eaten out of a meal since the last hunger check or fight, per hero: DD1 heroes get full
        private readonly Dictionary<uint, int> _eaten = new Dictionary<uint, int>();
        private readonly Queue<ExplorationEvent> _events = new Queue<ExplorationEvent>();
        private readonly List<string> _log = new List<string>();

        // what the view shows: a room, or a hallway laid out left (origin) to right (target)
        private int _shownRoom = -1;
        private int _shownHallway = -1;
        private int _origin = -1, _target = -1;
        private int _viewTile;

        private bool _fighting;
        // the fight in progress is a night ambush: the exploration did not ask for it and is not told its outcome
        private bool _ambush;
        private FightOutcome _fightOutcome;
        private Camp _camp;
        private CampBuffLedger _campBuffs = new CampBuffLedger();
        // A save from before trinkets were items of the bag carried the ones found on the way in a list beside
        // it (DD2 trinket id, the DD1 rarity it was rolled for). They wait here until the hub is up and are put
        // into the bag then (MoveSideTrinkets).
        private readonly List<KeyValuePair<string, string>> _sideTrinkets = new List<KeyValuePair<string, string>>();
        // What the fight in progress pays if it is won, in DD1's loot table codes (Core/BattleLoot.cs).
        private List<LootDraw> _fightDraws = new List<LootDraw>();
        // DD2's flame as the fight in progress began (NaN: no fight, or it could not be read), and what the last
        // fight came to: the flame before and after it, for the dev bridge.
        private double _torchAtFight = double.NaN;
        private double _lastFightBefore = double.NaN, _lastFightAfter = double.NaN;
        // a difference smaller than this is the number's own (DD2 keeps the flame as a float)
        private const double LightTolerance = 0.01;

        /// <summary>
        /// What a fight did to DD2's flame is done to DD1's torchlight afterwards. A switch (fight.torch
        /// follow=false) for the case that DD2 turns out to move the flame in every fight by itself: read from
        /// its code it does not, but that has not been seen in the game yet.
        /// </summary>
        public static bool LightFollowsTheFight = true;
        // Loot lying on DD1's scroll or waiting for its turn there (what the fallen wore, then the fight's own),
        // not yet taken or left: a save written meanwhile keeps it.
        private readonly List<FoundLoot> _finds = new List<FoundLoot>();

        /// <summary>
        /// OWNER'S CHOICE Q6 (docs/recon/inventory-unification.md 4.6): what becomes of carried trinkets when
        /// the whole party is lost. DD1 offers what a hero who died was wearing as loot if somebody of the party
        /// lives; with nobody left, false loses the fallen's trinkets and those in the bag with the party, true
        /// sends them home to the estate (DD1 has an option for exactly this, "KEEP TRINKETS?",
        /// menu_options_element_keep_battle_quest_fail_trinkets).
        /// </summary>
        public static bool KeepTrinketsOfALostParty = false;

        // An event of the expedition's own, beside the exploration's: loot waits on the scroll.
        private sealed class LootWaits : ExplorationEvent
        {
        }

        // The party as it set out. DD1's results screen has a row for every one of them, the dead too, and the
        // game forgets a dead hero (the actor is dropped, the roster entry buried, the experience with it).
        private sealed class Member
        {
            public uint Guid;
            public string Name, ClassId;
            /// <summary>Resolve experience at setting out; a living hero's is read again as the quest ends.</summary>
            public int Xp;
        }

        private readonly List<Member> _party = new List<Member>();
        // the camp's share of the exploration's scouting bonus, to take it back out when the buffs end
        private double _campScouting;
        private JToken _savedCamp;
        private bool _processing;
        private int _answer = -1;
        private int _picked;

        public Exploration Exploration => _x;

        /// <summary>The party's bag: provisions bought before setting out and what was picked up since.</summary>
        public Core.Inventory Bag => _bag;

        /// <summary>
        /// How many of a trinket the expedition under way has, unworn: in the bag, on the loot scroll, and in an
        /// older save's list beside the bag (0 when the party is in town).
        /// </summary>
        public static int TrinketsCarried(string id)
        {
            var run = Current;
            if (run == null || string.IsNullOrEmpty(id)) return 0;
            var count = run._sideTrinkets.Count(t => t.Key == id);
            for (var i = 0; i < run._bag.SlotCount; i++)
            {
                var stack = run._bag.Slot(i);
                if (stack != null && stack.Item.Type == ItemTypes.Trinket && stack.Item.Id == id) count += stack.Amount;
            }
            foreach (var find in run._finds)
                foreach (var stack in find.Stacks)
                    if (stack.Amount > 0 && stack.Item.Type == ItemTypes.Trinket && stack.Item.Id == id) count += stack.Amount;
            return count;
        }

        /// <summary>The unworn trinkets the bag holds, by DD2 item id, in the bag's order.</summary>
        public List<string> BagTrinkets()
        {
            var ids = new List<string>();
            for (var i = 0; i < _bag.SlotCount; i++)
            {
                var stack = _bag.Slot(i);
                if (stack == null || stack.Item.Type != ItemTypes.Trinket) continue;
                for (var n = 0; n < stack.Amount; n++) ids.Add(stack.Item.Id);
            }
            return ids;
        }

        /// <summary>The camp in progress; null when the party is not camping.</summary>
        public Camp Camp => _camp;

        /// <summary>Out of a save and not yet put up (that is done when the hub is in again).</summary>
        public bool AwaitsResume => _restored;

        /// <summary>The fight the party is coming back from was won: it comes back to where it was. After a flight
        /// it comes back to the hallway behind it, which is put up only once the hub is in. A quest that is
        /// nothing but its fight (DungeonRunBoss.cs) has no place to come back to: its fight ends in the quest's end.</summary>
        public bool StaysWhereItFought => (!_fighting || _fightOutcome == FightOutcome.Won) && !IsBossQuest;

        /// <summary>The camping buffs the party carries (DD1: for four fights, or until the next camp).</summary>
        public CampBuffLedger CampLedger => _campBuffs;

        internal Rng Dice => _rng;

        public bool Busy => _fighting || _processing || _camp != null;

        // the expedition is being wound up (Finish): what is paid from here on is its end, not its middle
        private bool _ending;

        /// <summary>
        /// A party is out and its expedition has not come to its end yet. Nothing reaches the estate's purse
        /// meanwhile: what the party finds lies in the bag until it is home (the purse's journal marks every
        /// change by this, and tools/ledger_check.py holds it to it).
        /// </summary>
        public static bool InTheField => Current != null && !Current._ending;

        private DungeonRun(Exploration exploration, RaidRules raid, CurioCatalog curios, LootTables loot, Core.Inventory bag, long seed)
        {
            _x = exploration;
            _map = exploration.Map;
            _raid = raid;
            _curios = curios;
            _loot = loot;
            _bag = bag ?? InventoryContent.NewBag();
            _rng = new Rng(seed);
        }

        /// <param name="bag">What the party bought at the provision screen; null sets out with empty hands.</param>
        public static DungeonRun Start(string dungeonId, string questType, int length, int tier, Quest quest = null, Core.Inventory bag = null)
        {
            // The hamlet's report of the last expedition waits for a click; unread, it must not follow the party
            // into the next dungeon and sit on the raid panel.
            NarrationBox.Hide();
            QuestBoard.PendingNarration = null;
            Current?.Dispose();
            var files = new Dd1Files();
            var seed = DateTime.Now.Ticks;
            // DD1's quests with a hand-made map of their own (the Brigand Incursion's streets, the Shrieker's perch)
            // are walked on that map; one of the Darkest Dungeon's five stand-ins is its boss's room and nothing else
            // (DungeonRunBoss.cs); every other expedition's is generated
            var map = PlotQuests.MapFor(quest, tier, seed) ?? DarkestBosses.MapFor(quest, tier, seed) ?? DungeonGenerator.Generate(dungeonId, questType, length, tier, seed, files);
            var raid = RaidRules.Load(files);
            var curios = CurioCatalog.Load(files);
            var exploration = new Exploration(map, raid, curios, seed ^ 0x5DEECE66DL);
            // DD1's rules of the plot quest a story quest stands for: no scouting in the Darkest Dungeon
            // (is_scouting_enabled), and a torch that does not burn down in the last descent (torch_setting dd4).
            var plot = QuestBoard.Dd1Plot(quest);
            if (plot != null)
            {
                exploration.ScoutingEnabled = plot.ScoutingEnabled;
                exploration.TorchBurns = raid.TorchBurns(plot.TorchSetting);
            }
            var run = new DungeonRun(exploration, raid, curios, LootTables.Load(files), bag, seed ^ 0x2545F491L);
            run._quest = quest;
            Plugin.Log.LogInfo($"Dungeon: {quest?.Id ?? "free"} {dungeonId} {questType} length {length} tier {tier}: {map.Rooms.Count} rooms, {map.Hallways.Count} hallways, seed {seed}");
            run.Begin();
            return run;
        }

        // ---- save: an expedition in progress travels in the estate section and is resumed on continue ----

        private bool _restored;
        private bool _fightAtDoor;      // the fight that is next in the queue is the room's the party is walking into

        public JObject ToJson()
        {
            var eaten = new JObject();
            foreach (var pair in _eaten) eaten[pair.Key.ToString()] = pair.Value;
            return new JObject
            {
                ["quest"] = _quest?.ToJson(),
                ["bag"] = _bag.ToJson(),
                ["eaten"] = eaten,
                ["campBuffs"] = _campBuffs.ToJson(),
                // an older save's trinkets beside the bag, while they have not been put into it (ledger.hold)
                ["trinkets"] = _sideTrinkets.Count > 0 ? new JArray(_sideTrinkets.Select(t => new JObject { ["id"] = t.Key, ["rarity"] = t.Value })) : null,
                // what lies on the loot scroll, and what waits for it, as the save is written
                ["loot"] = new JArray(_finds.Where(f => f.Any).Select(f => f.ToJson())),
                ["party"] = new JArray(_party.Select(m => new JObject { ["guid"] = m.Guid, ["name"] = m.Name, ["cls"] = m.ClassId, ["xp"] = m.Xp })),
                ["rules"] = RulesToJson(),
                // a camp the save catches the party in; before the hub is up after a load it is still the saved one
                ["camp"] = _camp != null ? _camp.ToJson() : _savedCamp,
                ["exploration"] = _x.ToJson()
            };
        }

        /// <summary>Rebuilds a saved expedition while the playthrough loads; the view follows once the hub is up.</summary>
        public static void Restore(JObject json)
        {
            Current?.Dispose();
            LastSideTrinkets = null;
            if (json == null || !(json["exploration"] is JObject saved)) return;
            var files = new Dd1Files();
            var raid = RaidRules.Load(files);
            var curios = CurioCatalog.Load(files);
            var exploration = Exploration.FromJson(saved, raid, curios);
            if (exploration.Status != RaidStatus.InProgress) return;
            var bag = Core.Inventory.FromJson(json["bag"], InventoryContent.Items, InventoryContent.Rules.RaidSlots);
            var run = new DungeonRun(exploration, raid, curios, LootTables.Load(files), bag, DateTime.Now.Ticks)
            {
                _quest = json["quest"] is JObject quest ? Quest.FromJson(quest) : null,
                _restored = true
            };
            // a save from before the bag existed carried a bare torch count
            if (json["bag"] == null) bag.Add(run._items.Find("torch"), (int?)json["torches"] ?? 0);
            if (json["eaten"] is JObject eaten)
                foreach (var p in eaten.Properties())
                    if (uint.TryParse(p.Name, out var guid)) run._eaten[guid] = (int?)p.Value ?? 0;
            run._campBuffs = CampBuffLedger.FromJson(json["campBuffs"]);
            // A save from before trinkets were bag items: the hub is not up and the other sections of the save
            // may not be read yet, so they only wait here (ResumeAfterLoad puts them into the bag).
            foreach (var trinket in json["trinkets"] as JArray ?? new JArray())
                if ((string)trinket["id"] != null) run._sideTrinkets.Add(new KeyValuePair<string, string>((string)trinket["id"], (string)trinket["rarity"]));
            // one find (a save of the step before) or the list of them
            foreach (var saved1 in json["loot"] is JArray finds ? (IEnumerable<JToken>)finds : new[] { json["loot"] })
            {
                var find = FoundLoot.FromJson(saved1, InventoryContent.Items);
                if (find != null) run._finds.Add(find);
            }
            // a save from before the party was kept has none: whoever is left of it is taken when the hub is up
            foreach (var member in json["party"] as JArray ?? new JArray())
                if ((uint?)member["guid"] is uint guid && guid != 0u)
                    run._party.Add(new Member { Guid = guid, Name = (string)member["name"] ?? "?", ClassId = (string)member["cls"] ?? "", Xp = (int?)member["xp"] ?? 0 });
            run.RulesFromJson(json["rules"]);
            // the saved scouting bonus has the camp's share in it already
            run._campScouting = run._campBuffs.Total(CampContent.Rules, CampBuffKind.Scouting);
            run._savedCamp = json["camp"];
            Current = run;
            EstateSession.View = EstateSession.Screen.Dungeon;
            Plugin.Log.LogInfo("Dungeon: expedition restored (" + (run._quest?.Id ?? exploration.Map.DungeonId) + ")");
        }

        private void ResumeAfterLoad()
        {
            _restored = false;
            if (_party.Count == 0) RememberParty();
            Graveyard.Place = DungeonContent.DisplayName(_map.DungeonId);
            EventManager.AddListener<EventBattleResult>(OnBattleResult);
            RaidFeedback.Listen();
            // as in Begin: by now the districts' own section of the save is read
            Districts.Adjust(_raid, _loot);
            ApplyLight();
            SyncQuestItems();
            SyncView();
            DungeonHud.ShowMap(_x);
            Say("The expedition continues.");
            // the game saves no stats container of the mod's: the camping buffs go back on the heroes
            SyncCampBuffs();
            _camp = Camp.Restore(this, _savedCamp);
            _savedCamp = null;
            if (_camp != null) _camp.Open();

            // an older save's trinkets beside the bag become items of it
            if (!Dd2Sweep.Hold) MoveSideTrinkets(false);

            // The snapshot is taken on entering a room, before its fight or curio is dealt with.
            var events = new List<ExplorationEvent>();
            // ... or after a fight or a curio, with its loot still on the scroll: the scroll is back
            foreach (var find in _finds) events.Add(new LootWaits());
            // ... or what a hero who died was wearing is still unclaimed (an older save's loot manager held it)
            if (ClaimFallenGear(LivingParty().Count > 0)) events.Add(new LootWaits());
            if (_x.Pending == PendingKind.Fight)
            {
                var slot = _x.RoomId >= 0 ? _map.Rooms[_x.RoomId].Battle : _map.Hallways[_x.HallwayId].Segments[_x.Segment].Battle;
                events.Add(new FightStarts { Slot = slot ?? new EncounterSlot { Kind = EncounterKind.Hallway, Tier = _map.Tier } });
            }
            // what bars the way is asked about again, as when the party came to it; a curio waits to be turned to
            else if (_x.Pending == PendingKind.Obstacle) events.Add(new ObstacleBlocks { ObstacleId = _map.Hallways[_x.HallwayId].Segments[_x.Segment].PropId });
            else if (_x.Pending == PendingKind.Hunger) events.Add(new HungerCheck());
            Enqueue(events);
            // ... or before the fight of a quest that is nothing but its fight had begun (DungeonRunPlot.cs, DungeonRunBoss.cs)
            BeginPlotQuest();
            BeginBossQuest(fresh: false);
        }

        private void RememberParty()
        {
            _party.Clear();
            foreach (var hero in Provisioning.Party())
                _party.Add(new Member { Guid = hero.ActorGuid, Name = hero.ActorName, ClassId = hero.ActorDataId, Xp = Resolve.Experience(hero.ActorGuid) });
        }

        private void Begin()
        {
            Current = this;
            RememberParty();
            Graveyard.Place = DungeonContent.DisplayName(_map.DungeonId);
            EventManager.AddListener<EventBattleResult>(OnBattleResult);
            RaidFeedback.Listen();
            EstateSession.View = EstateSession.Screen.Dungeon;
            // DD1's districts change an expedition's rules (the Cartographer's torchlight, the Granary's meals)
            Districts.Adjust(_raid, _loot);
            ApplyLight();
            SyncQuestItems();
            SyncView();
            DungeonHud.ShowMap(_x);
            Say("The party enters the " + DungeonContent.DisplayName(_map.DungeonId) + ".");
            // DD1: what a quest asks of those who are not up to it, or should never have come back (DungeonRules.cs)
            EnterAsDd1Has();
            // DD1's quests on a map of one room begin with their fight (DungeonRunPlot.cs)
            BeginPlotQuest();
            // one of the Darkest Dungeon's five stand-ins is nothing but its fight (DungeonRunBoss.cs)
            BeginBossQuest(fresh: true);
        }

        public void Dispose()
        {
            EventManager.RemoveListener<EventBattleResult>(OnBattleResult);
            var view = CorridorView.Instance;
            if (view != null) view.TileEntered -= OnTileEntered;
            CorridorProps.Clicked -= OnPropClicked;
            if (Current == this)
            {
                Current = null;
                Graveyard.Place = null;
                RaidFeedback.Stop();
                DungeonHud.Clear();
                Scouting.Clear();
                // an expedition's camp and its buffs end with it (a saved one comes back with the save)
                if (_camp != null)
                {
                    CampScreen.Close();
                    CampView.HideNow();
                }
                FightSurprise.Clear();
                CampBuffs.Sync(null);
            }
        }

        // ---- HUD text --------------------------------------------------------------------------------

        public string Title => _quest != null ? _quest.Name : DungeonContent.DisplayName(_map.DungeonId);

        /// <summary>The whole status as one rich text: title and light, the goal, the last log lines (the graveyard reads its first line).</summary>
        public string StatusText
        {
            get
            {
                var text = Title + "   <size=24>Light " + Mathf.RoundToInt((float)_x.Light) + "</size>\n<size=24>" + GoalLine + "</size>";
                var log = LogText(3);
                return log.Length > 0 ? text + "\n<size=21><color=#8a8272>" + log + "</color></size>" : text;
            }
        }

        /// <summary>
        /// What the quest asks for, as DD1's quest info says it: the sentence the quest was offered with in the
        /// hamlet (town_quest_goal_start_*: "Explore 90% of rooms."). DD1 keeps no count beside it.
        /// </summary>
        public string GoalLine
        {
            get
            {
                var objective = _map.Objective;
                if (objective == null) return "";
                if (_x.ObjectiveComplete) return "<color=" + RaidText.Hex(DD2Estate.UI.UiKit.Notable) + ">" + RaidText.Get("raid_quest_complete", "Quest Complete!") + "</color>";
                return !string.IsNullOrEmpty(_quest?.Goal) ? _quest.Goal : GoalText(objective);
            }
        }

        /// <summary>The last lines of the expedition's log, oldest first.</summary>
        public string LogText(int lines)
        {
            var text = "";
            for (var i = Mathf.Max(0, _log.Count - lines); i < _log.Count; i++) text += (text.Length > 0 ? "\n" : "") + _log[i];
            return text;
        }

        // An expedition without a quest of the board (a test run) has no sentence of its own: DD1's for the kind
        // of goal, filled from the map as Estate/QuestMapText.Goal fills it from the quest's goal. DD1's sentence
        // for a boss names the monster, which only a quest knows.
        private string GoalText(QuestObjective objective)
        {
            var one = objective.Required == 1;
            switch (objective.Kind)
            {
                case ObjectiveKind.Explore:
                    return RaidText.Format(RaidText.Get("town_quest_goal_start_plural_explore_room", "Explore %.0f%% of rooms."), Share(objective.Required, _map.GridRoomCount));
                case ObjectiveKind.Cleanse:
                    return RaidText.Format(RaidText.Get("town_quest_goal_start_plural_battle_room", "Complete %.0f%% of room battles."),
                        Share(objective.Required, _map.Rooms.Count(room => room.Battle != null && room.Type != RoomType.Boss)));
                case ObjectiveKind.Gather:
                    return RaidText.Format(one ? RaidText.Get("town_quest_goal_start_single_gather", "Gather %d %s.") : RaidText.Get("town_quest_goal_start_plural_gather", "Gather %d %ss."),
                        objective.Required, objective.ItemId != null ? InventoryText.Name(_items.Ensure(ItemTypes.QuestItem, objective.ItemId)) : "Relic");
                case ObjectiveKind.Activate:
                    return RaidText.Format(one ? RaidText.Get("town_quest_goal_start_single_activate", "Activate %d %s.") : RaidText.Get("town_quest_goal_start_plural_activate", "Activate %d %ss."),
                        objective.Required, RaidText.Get("str_curio_title_" + objective.CurioId, "Altar"));
                default: return "Slay the master of this place.";
            }
        }

        private static double Share(int required, int of) => of > 0 ? 100.0 * required / of : 100.0;

        private void Say(string line)
        {
            _log.Add(line);
            if (_log.Count > 30) _log.RemoveAt(0);
        }

        public int Torches => _bag.Count(_items.Find("torch"));

        /// <summary>Hooks the expedition into the estate save.</summary>
        [EstateModule]
        private static class SaveSection
        {
            public static void Register()
            {
                EstateState.RegisterSection("expedition", () => Current?.ToJson(), json => Restore(json as JObject), () => Current?.Dispose());
            }
        }

        /// <summary>The light levels of DD1's darkness table, the brightest first (the torch's tooltip counts a change's steps in them).</summary>
        public IReadOnlyList<LightBand> LightBands => _raid.LightBands;

        /// <summary>
        /// The party turns its light down: DD1's "[SHIFT+CLICK] to reduce torch", "[SHIFT+CTRL+CLICK] to snuff out
        /// torch" on the torch's tooltip. GUESS: by how much a step lowers it; DD1's files do not say, and a
        /// torch's own worth is taken (the same 25 a torch adds).
        /// </summary>
        public void ReduceTorch(bool snuff)
        {
            if (!ItemsAtHand || _x.Light <= 0) return;
            Enqueue(_x.AddLight(snuff ? -_x.Light : -_raid.TorchLight));
            ApplyLight();
        }

        /// <summary>Lights a torch from the bag, if there is one.</summary>
        public void UseTorch()
        {
            var torch = _items.Find("torch");
            for (var i = 0; i < _bag.SlotCount; i++)
            {
                if (_bag.Slot(i)?.Item != torch) continue;
                UseItem(i, 0u, out _);
                return;
            }
        }

        // ---- the bag ---------------------------------------------------------------------------------

        public bool NeedsHero(int slot) => _supply.UseOf(_bag.Slot(slot)?.Item).NeedsHero;

        // The bag can be used: the party is not in a fight or at a camp, and the expedition is in the middle of
        // nothing but a question of its own (DD1 lets a torch be lit and a hero be tended while a curio's or an
        // obstacle's window is up: its inventory shows them lit there, raid_obstacle_window_rubble.png).
        private bool ItemsAtHand => !_fighting && _camp == null && (!_processing || DungeonHud.AskingExpedition) && !_restored && EstateSession.InHub && _x.Status == RaidStatus.InProgress;

        // The party does nothing of its own while the expedition is in the middle of something or the screen
        // waits for an answer (DD1's confirm dialog, the choice of a completed quest).
        private bool Held => Busy || DungeonHud.Asking;

        /// <summary>
        /// True when using the stack of a slot would do something now, on this hero where a hero takes it. DD1
        /// draws the cards of what cannot be used at the moment grey (inventory_unselectable; seen in its own
        /// frame of a party at rest, raid_hud_at_rest_entrance_room_ruins_inventory.png: the food, the bandages,
        /// the shovel, the key and the torches of a rested party by full light are all grey). Gold, gems and
        /// heirlooms are never used and never grey.
        /// </summary>
        public bool CanUse(int slot, uint heroGuid)
        {
            var stack = _bag.Slot(slot);
            if (stack == null || !ItemsAtHand) return false;
            var use = _supply.UseOf(stack.Item);
            switch (use.Kind)
            {
                case ItemUseKind.None: return false;
                case ItemUseKind.Camp: return CampRefusal == null;
                case ItemUseKind.Light: return _x.Light < RaidRules.MaxLight;
            }
            var hero = heroGuid != 0u && EstateSession.IsInParty(heroGuid) ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(heroGuid) : null;
            if (hero == null || !hero.IsLiving) return false;
            if (use.Kind == ItemUseKind.StressHeal) return hero.Stress > 0f;
            if (hero.HpRounded >= hero.CurrentHpMax) return false;
            return use.Kind != ItemUseKind.Food || Eaten(heroGuid) < _supply.FoodBeforeFull;
        }

        /// <summary>True for an item that is used at all (a supply, food): DD1 greys only these while they cannot be.</summary>
        public static bool IsUsedItem(ItemDef item) => item != null && (item.Type == ItemTypes.Supply || item.Type == ItemTypes.Provision);

        /// <summary>True for an item the curio at hand reacts to (food on the one curio that takes an offering).</summary>
        public bool CurioTakes(ItemDef item)
        {
            var curio = _x.CurioHere;
            var id = item?.CurioItemId;
            return curio != null && id != null && _curios.TrackerFor(curio, id) != null;
        }

        /// <summary>
        /// Uses one unit of the stack in a slot, on a hero where the item is taken by one (food, bandage,
        /// antivenom, herbs, holy water, laudanum). False when nothing was used; the message says why.
        /// </summary>
        public bool UseItem(int slot, uint heroGuid, out string message)
        {
            message = null;
            var stack = _bag.Slot(slot);
            if (stack == null) return false;
            if (!ItemsAtHand)
            {
                message = "Not now.";
                return false;
            }
            var item = stack.Item;
            var use = _supply.UseOf(item);
            if (use.Kind == ItemUseKind.None)
            {
                message = InventoryText.Name(item) + ": " + InventoryText.Hint(item);
                return false;
            }
            if (use.Kind == ItemUseKind.Camp) return MakeCamp(out message);
            if (use.Kind == ItemUseKind.Light)
            {
                if (_x.Light >= RaidRules.MaxLight)
                {
                    // DD1's own words for a torch too many
                    message = RaidText.Get("str_cant_use_torch_at_limit", "It's no use...this is all the light we are getting for now.");
                    return false;
                }
                _bag.RemoveAt(slot, 1);
                Say(message = "A torch is lit.");
                Enqueue(_x.UseTorch());
                return true;
            }

            var hero = heroGuid != 0u && EstateSession.IsInParty(heroGuid) ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(heroGuid) : null;
            if (hero == null || !hero.IsLiving)
            {
                message = "Choose a hero of the party for the " + InventoryText.Lower(item) + ".";
                return false;
            }
            switch (use.Kind)
            {
                case ItemUseKind.StressHeal:
                    if (hero.Stress <= 0f)
                    {
                        message = hero.ActorName + " is calm already.";
                        return false;
                    }
                    hero.ApplyStressHeal((float)use.Amount, SourceType.STORY);
                    message = hero.ActorName + " steadies: " + InventoryText.Lower(item) + ".";
                    break;
                default:
                    if (hero.HpRounded >= hero.CurrentHpMax)
                    {
                        message = hero.ActorName + " is not hurt.";
                        return false;
                    }
                    if (use.Kind == ItemUseKind.Food)
                    {
                        _eaten.TryGetValue(heroGuid, out var eaten);
                        if (eaten >= _supply.FoodBeforeFull)
                        {
                            message = hero.ActorName + " is full.";
                            // DD1 writes "Full!" over a hero who will eat no more
                            DungeonHud.Pop(heroGuid, RaidPop.Full);
                            return false;
                        }
                        _eaten[heroGuid] = eaten + 1;
                    }
                    hero.ApplyHealthHeal(Mathf.Max(1f, hero.CurrentHpMax * (float)use.Amount), false, SourceType.STORY, false);
                    message = use.Kind == ItemUseKind.Food ? hero.ActorName + " eats." : hero.ActorName + " is treated: " + InventoryText.Lower(item) + ".";
                    break;
            }
            _bag.RemoveAt(slot, 1);
            Say(message);
            return true;
        }

        // DD1 keeps quest items in the bag, one to a slot. The exploration is what counts them; the bag shows as
        // many as it has room for, so a full bag never costs the party its quest.
        private void SyncQuestItems()
        {
            var id = _map.Objective?.ItemId;
            if (id == null) return;
            var item = _items.Ensure(ItemTypes.QuestItem, id);
            var shown = _bag.Count(item);
            if (shown < _x.QuestItems) _bag.Add(item, _x.QuestItems - shown);
            else if (shown > _x.QuestItems) _bag.Remove(item, shown - _x.QuestItems);
        }

        /// <summary>Food a hero has eaten since the last meal or fight (DD1: full after a few).</summary>
        public int Eaten(uint heroGuid) => _eaten.TryGetValue(heroGuid, out var eaten) ? eaten : 0;

        // ---- view ------------------------------------------------------------------------------------

        /// <summary>Makes the corridor view show where the exploration says the party is.</summary>
        public void SyncView()
        {
            var view = CorridorView.Ensure();
            view.TileEntered -= OnTileEntered;
            view.TileEntered += OnTileEntered;
            CorridorProps.Clicked -= OnPropClicked;
            CorridorProps.Clicked += OnPropClicked;

            if (_x.RoomId >= 0)
            {
                var room = _map.Rooms[_x.RoomId];
                if (_shownRoom != room.Id || _shownHallway >= 0)
                {
                    view.ShowArea(_map.DungeonId, new[] { RoomWall(room) }, isRoom: true, startTile: 0);
                    CorridorProps.ShowRoom(_x, room);
                    _shownRoom = room.Id;
                    _shownHallway = -1;
                }
                return;
            }

            var hall = _map.Hallways[_x.HallwayId];
            var target = _x.TowardRoomId >= 0 ? _x.TowardRoomId : hall.RoomB;
            ShowHallway(hall, hall.Other(target), target, 1 + TravelIndex(hall, target, _x.Segment));
        }

        // DD1 names room art per dungeon; the Darkest Dungeon's entrance and final rooms carry the plot quest's name.
        private string RoomWall(Room room)
        {
            // DD1's secret room is one picture for every dungeon
            if (room.Type == RoomType.Secret) return CorridorProps.SecretRoomWall;
            var prefix = "dungeons/" + _map.DungeonId + "/" + _map.DungeonId + ".";
            var candidates = new List<string>();
            if (room.Wall == "entrance")
            {
                candidates.Add("entrance_room_wall");
                candidates.Add("entrance_room_wall.plot_darkest_dungeon_" + DD2Estate.Dd1.Dd1Install.DarkestQuestArt);
            }
            else if (!string.IsNullOrEmpty(room.Wall)) candidates.Add("room_wall." + room.Wall);
            if (room.Type == RoomType.Boss) candidates.Insert(0, "final_room_wall.plot_darkest_dungeon_" + DD2Estate.Dd1.Dd1Install.DarkestQuestArt);
            // DD1 has an entrance and a final room of their own for some plot quests (the Shrieker's perch)
            PlotRoomWalls(room, candidates);
            candidates.Add("room_wall.empty");
            candidates.Add("room_wall.random01");
            foreach (var name in candidates)
                if (DD2Estate.Dd1.Dd1Install.Exists(DD2Estate.Dd1.Dd1Install.ArtPath(prefix + name + ".png"))) return name;
            return candidates[0];
        }

        // Segment order as walked from the origin end to the target end.
        private static int TravelIndex(Hallway hall, int target, int segment) => target == hall.RoomB ? segment : hall.Segments.Count - 1 - segment;

        private void ShowHallway(Hallway hall, int origin, int target, int startTile)
        {
            var tiles = new List<string> { "corridor_door.basic" };
            for (var i = 0; i < hall.Segments.Count; i++)
            {
                var segment = hall.Segments[target == hall.RoomB ? i : hall.Segments.Count - 1 - i];
                tiles.Add("corridor_wall." + segment.Wall.ToString("00"));
            }
            tiles.Add("corridor_door.basic");
            CorridorView.Ensure().ShowArea(_map.DungeonId, tiles, isRoom: false, startTile: startTile);
            CorridorProps.ShowHallway(_x, hall, target);
            _shownHallway = hall.Id;
            _shownRoom = -1;
            _origin = origin;
            _target = target;
            _viewTile = startTile;
        }

        /// <summary>
        /// The room the hallway on screen leads to, which DD1 marks on its map (panels/icons_map/moving_room.png);
        /// -1 while a room is on screen.
        /// </summary>
        public int Destination => _shownHallway >= 0 && _target >= 0 && _target != _x.RoomId ? _target : -1;

        /// <summary>Minimap click: leave the room toward a neighbour (the party stands at the door until it walks),
        /// or step back into the room view when the party has not left yet.</summary>
        public void TravelTo(int roomId)
        {
            if (Held || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return;
            // a secret room has no hallway of its own: any room clicked on the map leads back out to the hallway it opens from
            if (_x.InSecretRoom)
            {
                LeaveSecretRoom();
                return;
            }
            if (_x.RoomId >= 0 && roomId == _x.RoomId)
            {
                SyncView();
                return;
            }
            if (!_x.CanMoveTo(roomId)) return;
            if (_x.RoomId >= 0)
            {
                ShowHallway(_map.HallwayBetween(_x.RoomId, roomId), _x.RoomId, roomId, 0);
                return;
            }
            // in a hallway: face the chosen end
            if (roomId != _target) ShowHallway(_map.Hallways[_x.HallwayId], _target, roomId, 1 + TravelIndex(_map.Hallways[_x.HallwayId], roomId, _x.Segment));
        }

        private bool IsDoorTile(int tile)
        {
            var view = CorridorView.Instance;
            return _shownHallway >= 0 && view != null && (tile <= 0 || tile >= view.TileCount - 1);
        }

        /// <summary>
        /// The party goes through the door it stands at: the last step of the way, which DD1 leaves to the player
        /// (W, or a click on the door). From the doorway of a room it has not walked away from, it is back in that room.
        /// </summary>
        public void UseDoor(CorridorProp door)
        {
            if (Current != this || door == null || Held || !EstateSession.InHub || _x.Status != RaidStatus.InProgress || _shownHallway < 0) return;
            var forward = door.Tile > 0;
            var toward = forward ? _target : _origin;
            if (_x.RoomId >= 0)
            {
                if (toward == _x.RoomId) SyncView();
                return;
            }
            // the tiles between the party's last step and the door, should the view have run ahead of the expedition
            var next = CorridorView.Instance != null ? (forward ? CorridorView.Instance.TileCount - 2 : 1) : _viewTile;
            if (_viewTile != next) OnTileEntered(next);
            if (Busy || _viewTile != next || !_x.CanMoveTo(toward)) return;
            var events = _x.Step(toward);
            _viewTile += forward ? 1 : -1;
            Enqueue(events);
        }

        // What the player turned to with a click or with W, taken up in the line of events like what the rules raise.
        private sealed class TurnedTo : ExplorationEvent
        {
            public PropKind Kind;
            public string Id;
            public bool QuestCurio;
            public CorridorProp Prop;
        }

        /// <summary>
        /// The player turned to something the party stands at (a click in the corridor: the view has walked the
        /// party up to it; or W). DD1 asks about nothing by itself but what bars the way: a curio's window opens
        /// on this ("[CLICK] on objects or press [W] to interact with them"), a curio without a window (the
        /// library's "FULL CURIO?" is No: sconce, crate, sack, discarded pack) is opened at once, a spotted trap is
        /// tried by the selected hero, an obstacle the party left standing is asked about again.
        /// </summary>
        private void OnPropClicked(CorridorProp prop)
        {
            if (Current != this || prop == null || Held || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return;
            switch (prop.Kind)
            {
                case PropKind.Door:
                    UseDoor(prop);
                    return;
                case PropKind.Exit:
                    LeaveSecretRoom();
                    return;
                case PropKind.SecretDoor:
                    // DD1: "advance to the tile marked with a star, and press W or click to enter"
                    Enqueue(_x.EnterSecretRoom());
                    return;
                case PropKind.Trap:
                    if (TrapAtHand(prop)) Enqueue(new List<ExplorationEvent> { new TurnedTo { Kind = PropKind.Trap, Id = prop.Id, Prop = prop } });
                    return;
                case PropKind.Obstacle:
                    if (_x.Pending == PendingKind.Obstacle) Enqueue(new List<ExplorationEvent> { new TurnedTo { Kind = PropKind.Obstacle, Id = prop.Id, Prop = prop } });
                    return;
                case PropKind.Curio:
                    TurnToCurio();
                    return;
            }
        }

        /// <summary>The party turns to the curio it stands at; false when there is none to turn to (also the dev bridge's way in).</summary>
        public bool TurnToCurio()
        {
            if (Held || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return false;
            var curio = _x.CurioHere;
            if (curio == null) return false;
            var quest = _x.RoomId >= 0 && _map.Rooms[_x.RoomId].QuestCurio;
            if (!quest && !_curios.HasWindow(curio)) Enqueue(_x.UseCurio());
            else Enqueue(new List<ExplorationEvent> { new TurnedTo { Kind = PropKind.Curio, Id = curio, QuestCurio = quest } });
            return true;
        }

        // ---- the party's order ---------------------------------------------------------------------------

        /// <summary>True while the party's order may be changed: outside a fight and a camp, with nothing else going on.</summary>
        public bool CanReorder => !Held && !_restored && EstateSession.InHub && _x.Status == RaidStatus.InProgress;

        /// <summary>The party as it marches now, the leader first.</summary>
        public static List<uint> MarchingOrder()
        {
            return new List<uint>(Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY));
        }

        /// <summary>
        /// A hero takes another's rank, and those between the two move up one towards the place left free: DD1's
        /// move outside a fight (its help: "[CLICK] on a hero's move skill to change their party position").
        /// GUESS: DD1's files do not say what becomes of the heroes in between; they are moved as a move of
        /// several ranks moves them in a fight, where each hero passed gives way by one.
        /// </summary>
        public bool MoveHero(uint hero, uint onto)
        {
            if (!CanReorder || hero == onto) return false;
            var order = MarchingOrder();
            int from = order.IndexOf(hero), to = order.IndexOf(onto);
            if (from < 0 || to < 0) return false;
            order.RemoveAt(from);
            order.Insert(to, hero);
            return SetOrder(order);
        }

        /// <summary>
        /// True when the party no longer marches as it set out: DD1's button for it is then of use
        /// (panels/party_order_button.png, str_party_default_order "Default Party order").
        /// </summary>
        public bool OrderChanged
        {
            get
            {
                var order = MarchingOrder();
                var wanted = DefaultOrder(order);
                for (var i = 0; i < order.Count; i++)
                    if (order[i] != wanted[i]) return true;
                return false;
            }
        }

        // those of the party as it set out who still march, in that order; anybody else after them
        private List<uint> DefaultOrder(List<uint> now)
        {
            var order = new List<uint>();
            foreach (var member in _party)
                if (now.Contains(member.Guid)) order.Add(member.Guid);
            foreach (var guid in now)
                if (!order.Contains(guid)) order.Add(guid);
            return order;
        }

        /// <summary>The party marches again as it set out.</summary>
        public bool RestoreOrder()
        {
            return CanReorder && OrderChanged && SetOrder(DefaultOrder(MarchingOrder()));
        }

        // DD2's own way of ranking a party (ActorTeamPositionCalculation.SetActorTeamPositions, which its hero
        // ribbons use when one is dragged): every hero's team position and front rank, without the effects a
        // move has in a fight. The roster lists the party by front rank, so a fight, the corridor and the
        // screen's trays all follow.
        private bool SetOrder(List<uint> order)
        {
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            var actors = new List<ActorInstance>();
            foreach (var guid in order)
            {
                var actor = library.GetLibraryElement(guid);
                if (actor == null) return false;
                actors.Add(actor);
            }
            try { ActorTeamPositionCalculation.SetActorTeamPositions(actors, true, false); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Dungeon: the party's order could not be changed: " + e.Message);
                return false;
            }
            Plugin.Log.LogInfo("Dungeon: the party marches as " + string.Join(", ", actors.Select(a => a.ActorName)));
            if (CorridorView.Instance != null) CorridorView.Instance.Reorder();
            // camping buffs under a rank rule are answered by where each hero stands
            SyncCampBuffs();
            return true;
        }

        // ---- traps -------------------------------------------------------------------------------------

        /// <summary>
        /// A hero's chance at the trap of this dungeon, as DD1 tells it on the trays while the pointer is on a
        /// spotted trap (<see cref="TrapDisarm"/>).
        /// </summary>
        public double DisarmChance(ActorInstance hero) => TrapDisarm.Chance(hero, _raid, _map.Tier);

        // The hallway tile after the one the rules have the party on, as the view walks it (towards the room
        // ahead); -1 when there is none (the door tile comes next, or the party is not in this hallway).
        private int SegmentAhead()
        {
            if (_shownHallway < 0 || _target < 0) return -1;
            var hall = _map.Hallways[_shownHallway];
            int next;
            if (_x.RoomId >= 0) next = _x.RoomId == _origin ? (_target == hall.RoomB ? 0 : hall.Segments.Count - 1) : -1;
            else if (_x.HallwayId != hall.Id) next = -1;
            else next = _x.Segment + (_target == hall.RoomB ? 1 : -1);
            return next >= 0 && next < hall.Segments.Count ? next : -1;
        }

        // A spotted trap that has not gone off and was not disarmed, in the party's reach: the tile ahead of the
        // one the rules have the party on (DD1 lets a trap be tried from 235 short of it, and it goes off 180
        // short of it: the party never stands on the tile of a trap it may still try), or the party's own tile
        // for an expedition saved while the mod still asked about traps.
        private bool TrapAtHand(CorridorProp prop)
        {
            if (prop == null || prop.Kind != PropKind.Trap || prop.Staged || _shownHallway < 0 || prop.Segment < 0) return false;
            var hall = _map.Hallways[_shownHallway];
            if (prop.Segment >= hall.Segments.Count || hall.Segments[prop.Segment].Content != HallContent.Trap) return false;
            if (!_x.IsSegmentKnown(hall.Id, prop.Segment) || _x.IsSegmentDone(hall.Id, prop.Segment)) return false;
            if (_x.Pending == PendingKind.Trap) return _x.HallwayId == hall.Id && _x.Segment == prop.Segment;
            return _x.Pending == PendingKind.None && prop.Segment == SegmentAhead() && _x.CanMoveTo(_target);
        }

        /// <summary>True for a trap the party may try to disarm from where it stands (the view lights it up, W turns to it).</summary>
        public bool CanDisarm(CorridorProp prop) => Current == this && !Held && TrapAtHand(prop);

        // The hero who acts: DD1's "The selected hero will perform the interaction."
        private ActorInstance Acting()
        {
            var guid = DungeonHud.Selected;
            var hero = guid != 0u && EstateSession.IsInParty(guid) ? SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid) : null;
            return hero != null && hero.IsLiving ? hero : RandomHero();
        }

        // DD1 (Overlay_Event::DisarmTrap): the selected hero bends over the trap (disarm_intro.times), and the roll
        // is the hero's: disarmed ("Disarmed!", the trap's success_effects for the hero), or the trap goes off
        // under those very hands.
        private IEnumerator Disarm(TurnedTo turned)
        {
            if (!TrapAtHand(turned.Prop)) yield break;
            if (_x.Pending != PendingKind.Trap)
            {
                // The rules know tiles and nothing finer: the party steps onto the trap's tile in them, though on
                // screen it stands a pace short of it. What the step brings besides (the light it costs, a
                // walker's unease) is played after the trap.
                var stepped = _x.Step(_target);
                _viewTile += 1;
                stepped.RemoveAll(e => e is TrapSpotted);
                Add(stepped);
            }
            if (_x.Pending != PendingKind.Trap) yield break;
            var hero = Acting();
            if (hero == null) yield break;
            var trapId = _map.Hallways[_x.HallwayId].Segments[_x.Segment].PropId ?? _map.TrapId;
            var chance = DisarmChance(hero);
            var disarmed = _rng.Chance(chance);
            Plugin.Log.LogInfo("Dungeon: " + hero.ActorName + " tries the trap " + trapId + " at " + chance.ToString("0.00") + ": " + (disarmed ? "disarmed" : "it goes off"));
            yield return CorridorView.Investigate(PropKind.Trap, hero.ActorGuid, turned.Prop);
            if (disarmed)
            {
                Say("The trap is disarmed.");
                // DD1's banner for it, and what the trap's definition gives the hero who did it ("Heal Stress TrapD")
                var until = Time.unscaledTime + DungeonHud.Announce(RaidAnnounce.Disarm);
                var level = TrapLevelOf(trapId);
                var calm = level != null ? _supply.EffectStressHeal(level.SuccessEffects) * StressScale : 0;
                if (calm > 0 && hero.Stress > 0f) hero.ApplyStressHeal(Mathf.Max(1f, Mathf.Round((float)calm)), SourceType.STORY);
                while (Current == this && Time.unscaledTime < until) yield return null;
            }
            else
            {
                Say("The mechanism slips.");
                _trapVictim = hero.ActorGuid;
            }
            yield return CorridorView.EndInteraction();
            Add(_x.ResolveTrap(disarmed));
        }

        // whom the trap that is about to go off catches: the hero who tried it (0: whoever walks into it)
        private uint _trapVictim;

        private TrapLevel TrapLevelOf(string trapId)
        {
            return trapId != null && _raid.Traps.TryGetValue(trapId, out var trap) && trap.Levels.Count > 0 ? trap.For(_map.Tier) : null;
        }

        private void LeaveSecretRoom()
        {
            if (!_x.LeaveSecretRoom()) return;
            Say("The party slips back into the hallway.");
            SyncView();
        }

        private void OnTileEntered(int tile)
        {
            if (Current != this || Busy || _shownHallway < 0) return;
            while (tile != _viewTile && !Busy)
            {
                var forward = tile > _viewTile;
                // DD1: the party walks up to a door and stands there; the door is used (W, or a click on it:
                // UseDoor), it is not walked through. A door tile is the hallway's first or last.
                if (IsDoorTile(_viewTile + (forward ? 1 : -1))) return;
                var toward = forward ? _target : _origin;
                // an expedition saved while the mod still asked about traps stands on the trap's tile: walking on is walking into it
                if (forward && _x.Pending == PendingKind.Trap && _x.TowardRoomId == toward)
                {
                    Enqueue(_x.ResolveTrap(false));
                    CorridorView.Instance.PlaceInTile(_viewTile);
                    return;
                }
                if (!_x.CanMoveTo(toward))
                {
                    CorridorView.Instance.PlaceInTile(_viewTile);
                    return;
                }
                var events = _x.Step(toward);
                _viewTile += forward ? 1 : -1;
                // DD1 (RaidDisplay::HandleTrapOrObstacleTile): a trap goes off the moment the party's place comes
                // onto its tile, seen or not. The rules stop the party before a trap it knows of and wait for an
                // answer; walking onto the tile is the answer.
                if (_x.Pending == PendingKind.Trap) events.AddRange(_x.ResolveTrap(false));
                Enqueue(events);
            }
        }

        // ---- events ----------------------------------------------------------------------------------

        private void Enqueue(List<ExplorationEvent> events)
        {
            foreach (var e in events) _events.Enqueue(e);
            if (!_processing && _events.Count > 0) Plugin.Host.StartCoroutine(Guarded(Process()));
        }

        /// <summary>
        /// Runs the event routine and whatever it yields to (prompts, meals, curios) step by step, so that an
        /// exception in any of them is caught here. Unity would log it and drop the routine, and the expedition
        /// would stay busy for ever: nothing could be clicked and nobody could leave. Instead the event at
        /// fault is given up and the rest of the line goes on.
        /// </summary>
        private IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                object current = null;
                bool moved;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) current = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Dungeon: an event could not be handled and is given up: " + e);
                    _processing = false;
                    if (Current == this)
                    {
                        SetFrozen(false);
                        if (_events.Count > 0) Plugin.Host.StartCoroutine(Guarded(Process()));
                    }
                    yield break;
                }
                if (!moved) stack.Pop();
                else if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
        }

        private IEnumerator Process()
        {
            _processing = true;
            SetFrozen(true);
            while (_events.Count > 0 && Current == this)
            {
                var e = _events.Dequeue();
                switch (e)
                {
                    case RoomEntered entered:
                        SyncView();
                        // DD1: the party walks into the door, the hallway fades out and the room fades in before anything
                        // happens in it. A room with a fight does not wait for that (the user: "start the fight much
                        // earlier"): the fight is asked for as the party goes through the door, and the game's own
                        // wipe closes over the hallway.
                        _fightAtDoor = _events.Count > 0 && _events.Peek() is FightStarts;
                        while (!_fightAtDoor && CorridorView.TransitionRunning) yield return null;
                        if (entered.FirstVisit && EstateSession.InHub) EstatePersistence.SaveNow("room");
                        break;
                    case EnteredSegment tile:
                        // DD1's narration moment "enter_hallway": the party has left a room for a hallway
                        if (tile.FromRoom) Narrate("enter_hallway");
                        break;
                    case LightChanged light:
                        ApplyLight();
                        if (light.BandChanged) Say(light.After < light.Before ? "The darkness presses closer." : "The light pushes the dark back.");
                        NarrateLight(light);
                        break;
                    case WalkStress walk:
                        if (_rng.Chance(walk.Amount * StressScale))
                        {
                            var uneasy = RandomHero();
                            if (uneasy != null)
                            {
                                uneasy.ApplyStressDamage(StressFor(uneasy, 1f), false, SourceType.STORY, null, 0u);
                                Say(uneasy.ActorName + " grows uneasy in the dark.");
                            }
                        }
                        break;
                    case FightStarts fight:
                        if (!_fightAtDoor) yield return new WaitForSeconds(0.6f);
                        _fightAtDoor = false;
                        while (!EstateSession.InHub) yield return null;
                        // the queue resumes from OnReturnedFromFight
                        _processing = false;
                        StartFight(fight);
                        yield break;
                    case TrapTriggered trap:
                        // DD1 (trap.times): the camera goes to the victim and shakes, the scene darkens and blurs
                        yield return CorridorView.TrapSprung(SpringTrap(trap.TrapId));
                        // DD1's narration moment "trap": a trap that went off (walked into, or a disarm that failed)
                        Narrate("trap", trap.TrapId);
                        break;
                    case TrapSpotted _:
                        // DD1 has no window for a trap it shows on the floor: the party walks into it (OnTileEntered)
                        // or a hero tries it from a pace before it (a click on it, or W: Disarm)
                        break;
                    case TurnedTo turned:
                        if (turned.Kind == PropKind.Trap) yield return Disarm(turned);
                        else if (turned.Kind == PropKind.Obstacle) yield return ClearObstacle(turned.Id);
                        else yield return Investigate(turned.Id, turned.QuestCurio);
                        break;
                    case ObstacleBlocks obstacle:
                        // DD1 (HandleTrapOrObstacleTile, Overlay_Event::SetActiveProp): what bars the way is asked
                        // about as the party comes onto its tile; a curio is not
                        yield return ClearObstacle(obstacle.ObstacleId);
                        break;
                    case HungerCheck _:
                        yield return Eat();
                        break;
                    case CurioFound found:
                        // DD1 asks nothing as the party comes to a curio: it lights up (CorridorProps: "active") and
                        // its window opens when the player turns to it (a click on it or W: OnPropClicked). What
                        // DD1 does do as the party comes onto a curio's tile (RaidDisplay::HandleCurioTileTemptations)
                        // is let a hero whose quirk draws them to its kind reach for it unasked (DungeonRules.cs).
                        if (!found.Again) Compelled(found);
                        break;
                    case CurioUsed used:
                        // DD1 (investigate_intro): the chosen hero steps up, the camera closes in, the scene blurs
                        yield return CorridorView.Investigate(PropKind.Curio, DungeonHud.Selected);
                        yield return ApplyCurio(used.Outcome);
                        Districts.OnCurio(_curios.Get(used.Outcome.CurioId), DungeonHud.Selected, Say);
                        // whoever reached for it unasked has had their way
                        _compelled = null;
                        // DD1's narration moment "curio": after the curio has given its result (its one line
                        // belongs to a tutorial's curio)
                        Narrate("curio", used.Outcome.CurioId);
                        SyncQuestItems();
                        // investigate_extro: everything back in 0.3 s
                        yield return CorridorView.EndInteraction();
                        break;
                    case QuestItemGained _:
                        Say("A quest item is secured.");
                        SyncQuestItems();
                        break;
                    case Scouted scouted:
                        // DD1 brings a scout's finds up on the map one after another under its banner; a scouted
                        // trap shows on the hallway's floor by itself (CorridorProps follows the rules)
                        Scouting.Report(_x, scouted);
                        Say(Scouting.Summary(_x, scouted));
                        break;
                    case SecretRoomFound _:
                        // DD1 has no window for a hidden door either: the map marks its tile with a star, and it is
                        // entered there with W or a click on the wall (tutorial_popup_scout_hidden_door_description)
                        break;
                    case ObjectiveCompleted _:
                        Say("The quest's goal is met. Leave when ready.");
                        break;
                    case LootWaits _:
                        yield return OfferLoot();
                        break;
                    case QuestEnded ended:
                        _processing = false;
                        Finish(ended);
                        yield break;
                }
                // somebody died of it (a trap, hunger, a curio): what they wore is offered to the party at once
                if (Current == this && ClaimFallenGear(LivingParty().Count > 0)) Add(new List<ExplorationEvent> { new LootWaits() });
                // DD1's narration moment "half_health_half_stress": looked for after whatever has just happened
                NarrateParty();
            }
            _processing = false;
            SetFrozen(false);
        }

        // results of an answer go to the front of the line: they belong to the event being handled
        private void Add(List<ExplorationEvent> events)
        {
            if (events.Count == 0) return;
            var rest = _events.ToArray();
            _events.Clear();
            foreach (var e in events) _events.Enqueue(e);
            foreach (var e in rest) _events.Enqueue(e);
        }

        private IEnumerator Ask(string text, params string[] options)
        {
            _answer = -1;
            DungeonHud.Ask(text, options, i => _answer = i);
            while (_answer < 0 && Current == this) yield return null;
        }

        // a question in a window the expedition has dressed itself (a curio's, an obstacle's)
        private IEnumerator Ask(RaidPrompt prompt)
        {
            _answer = -1;
            _offered = null;
            DungeonHud.Ask(prompt, i => _answer = i);
            while (_answer < 0 && Current == this) yield return null;
        }

        // the item the open window's slot took (RaidPrompt.Offer), for the answer RaidPrompt.ItemAnswer
        private ItemDef _offered;

        // the party stands still while the player picks a stack of the bag; -1: none picked
        private IEnumerator PickSlot(string text)
        {
            _picked = int.MinValue;
            DungeonHud.PickSlot(text, slot => _picked = slot);
            while (_picked == int.MinValue && Current == this) yield return null;
        }

        private static void SetFrozen(bool frozen)
        {
            if (CorridorView.Instance != null) CorridorView.Instance.Frozen = frozen;
        }

        // ---- obstacles, hunger, curios ----------------------------------------------------------------

        // DD1: the obstacle's item clears it for free; by hand costs every hero the fail effects (stress) and a
        // share of health, and the party its torchlight (taken by the exploration).
        //
        // DD1's window for it (seen in its own frame, _lab/dd1_ref/raid/raid_obstacle_window_rubble.png): the
        // sidebar scroll with the obstacle's name and sentence (str_obstacle_<id>_title, _description), the hand
        // (obstacle_tooltip_clear_by_hand), the slot with the shovel lying in it, and the way past: the party
        // may leave it standing and turn back, and turn to it again later (a click on it, or W).
        private IEnumerator ClearObstacle(string obstacleId)
        {
            if (_x.Pending != PendingKind.Obstacle) yield break;
            var tool = _items.ForCurio(_raid.Obstacle.ClearItem);
            // DD1's narration moment "obstacle", as its question comes up (its lines are the dungeon's own)
            Narrate("obstacle", obstacleId);
            var prompt = RaidPrompt.Sidebar(RaidPromptKind.Obstacle,
                RaidText.Get("str_obstacle_" + obstacleId + "_title", RaidPrompt.Words(obstacleId) ?? RaidText.Get("str_map_ac_obstacle_tooltip", "Obstacle")),
                RaidText.Get("str_obstacle_" + obstacleId + "_description", ""),
                "Clear it by hand", RaidText.Get("obstacle_tooltip_clear_by_hand"), "Leave it", RaidText.Get("curio_tooltip_pass", "Ignore"));
            prompt.SlotItem = tool;
            // the pointer on the slot is told what lies in it, as on a card of the bag (the mod's reading: DD1 has no words for this slot)
            prompt.SlotTooltip = tool != null ? RaidTooltip.Titled(InventoryText.Name(tool), InventoryText.Description(tool)) : null;
            // the shovel is used by a click on the slot it lies in, or by being offered from the bag as an item is to a curio
            prompt.SlotClicked = () =>
            {
                if (tool != null && _bag.Count(tool) > 0) DungeonHud.Answer(RaidPrompt.ItemAnswer);
                else DungeonHud.Inform(RaidText.Get("str_user_information_no_shovel", "You don't have a shovel!"));
            };
            prompt.Offer = slot => tool != null && _bag.Slot(slot)?.Item == tool;
            yield return Ask(prompt);
            if (Current != this) yield break;
            var view = CorridorView.Instance;
            if (_answer == prompt.IndexOf(RaidOptionRole.Pass))
            {
                // left standing: the party goes no further than where it was stopped
                if (view != null) view.Barrier = view.LeadX;
                yield break;
            }
            if (view != null) view.Barrier = float.MaxValue;
            var used = _answer == RaidPrompt.ItemAnswer && tool != null && _bag.Count(tool) > 0;
            // DD1 plays the investigate scripts for an obstacle too
            yield return CorridorView.Investigate(PropKind.Obstacle, DungeonHud.Selected);
            Add(_x.ResolveObstacle(used));
            if (used)
            {
                _bag.Remove(tool, 1);
                Say("The " + InventoryText.Lower(tool) + " makes short work of it.");
                yield return CorridorView.EndInteraction();
                yield break;
            }
            var stress = _supply.EffectStress(_raid.Obstacle.FailEffects) * StressScale;
            foreach (var digger in LivingParty())
            {
                // without DD1's numbers digging still frays the nerves, as it did before the bag existed
                digger.ApplyStressDamage(StressFor(digger, Mathf.Max(1f, Mathf.Round((float)stress))), false, SourceType.STORY, obstacleId, 0u);
                if (_raid.Obstacle.Health < 0) Hurt(digger, -_raid.Obstacle.Health, obstacleId);
            }
            Say("The party digs through by hand; hands bleed and nerves fray.");
            // DD1's narration moment "obstacle_clear_no_item"
            Narrate("obstacle_clear_no_item", obstacleId);
            yield return CorridorView.EndInteraction();
        }

        // DD1: every hero eats one unit and regains a little health, or the party starves: health and stress.
        private IEnumerator Eat()
        {
            var party = LivingParty();
            var food = _items.Food;
            var need = _supply.HungerFood(party.Count);
            var carried = food != null ? _bag.Count(food) : 0;
            var healed = Mathf.RoundToInt((float)(_raid.HungerHeal * 100));
            var lost = Mathf.RoundToInt((float)(_raid.HungerStarveDamage * 100));
            // DD1's own sentence on the scroll and its own words for the two bowls, which the pointer is told
            var hunger = RaidText.Get("str_ui_hunger_content", "The exertions of adventuring have produced a growing hunger amongst the party.");
            var eat = RaidText.Format(RaidText.Get("str_ui_hunger_choice_eat", "Eat %.0f food, regain %d%% health"), need, healed);
            var starve = RaidText.Format(RaidText.Get("str_ui_hunger_choice_starve", "Eat nothing, take %d%% damage plus stress damage"), lost);
            // DD1's narration moment "hunger" (its table gives the moment no chance: the Ancestor keeps silent)
            Narrate("hunger");
            if (carried >= need) yield return Ask(hunger, eat, starve);
            else yield return Ask(hunger, starve);
            var ate = carried >= need && _answer == 0;
            Add(_x.ResolveHunger(ate));
            _eaten.Clear();
            if (ate)
            {
                _bag.Remove(food, need);
                foreach (var hero in party) hero.ApplyHealthHeal(Mathf.Max(1f, hero.CurrentHpMax * (float)_raid.HungerHeal), false, SourceType.STORY, false);
                Say("The party stops to eat.");
                yield break;
            }
            foreach (var hero in party)
            {
                Hurt(hero, _raid.HungerStarveDamage, "hunger");
                hero.ApplyStressDamage(StressFor(hero, Mathf.Max(1f, Mathf.Round((float)(_supply.StarveStress * StressScale)))), false, SourceType.STORY, "hunger", 0u);
            }
            Say("The party goes hungry.");
            // DD1's narration moment "hunger_starve"
            Narrate("hunger_starve");
        }

        private static void Hurt(ActorInstance hero, double shareOfMaxHealth, string sourceId)
        {
            hero.ApplyHealthDamage(Mathf.Max(1f, hero.CurrentHpMax * (float)shareOfMaxHealth), false, false, hero, DeathType.EFFECT, SourceType.STORY, sourceId, false);
        }

        // DD1's window for a curio the player turned to (Overlay_Event::ShowCurio; its own frames:
        // _lab/dd1_ref/raid/raid_curio_window_*.png): the sidebar scroll with the curio's name and sentence
        // (str_curio_title_<ui name>, str_curio_content_<ui name>), the hand (str_curio_tooltip_investigate_<ui name>),
        // the slot for an item of the bag and the way past ("Ignore"). An item is offered to the slot (dragged
        // there, or right-clicked in the inventory): one the curio reacts to is used on it at once; any other
        // stays in the bag, the hero says that it did nothing ("str_curio_item_had_no_effect": DD1's own code
        // for it, Overlay_Event at 0x7069d0 of its executable, barks the line and writes the curio tracker) and
        // the window stays up. A quest's curio that takes the quest's item has a dead hand and wants that item
        // in the slot (curio_tooltip_quest_item_slot).
        private IEnumerator Investigate(string curioId, bool questCurio)
        {
            if (_x.CurioHere != curioId) yield break;
            var type = _curios.Get(curioId)?.Id;
            var objective = _map.Objective;
            var wantsQuestItem = questCurio && objective != null && objective.Kind == ObjectiveKind.Activate && objective.ItemId != null;
            var questItem = wantsQuestItem ? _items.Ensure(ItemTypes.QuestItem, objective.ItemId) : null;
            var ui = _curios.Prop(curioId)?.UiString;
            if (string.IsNullOrEmpty(ui)) ui = curioId;
            var byHand = PropText.CurioInvestigate(curioId) ?? RaidText.Get("str_curio_tooltip_investigate_" + curioId);
            if (wantsQuestItem) byHand = RaidText.Get("str_curio_tooltip_investigate_" + ui + "_with_quest_item") ?? byHand;
            var prompt = RaidPrompt.Sidebar(RaidPromptKind.Curio,
                PropText.CurioTitle(curioId) ?? RaidText.Get("str_curio_title_" + curioId, RaidPrompt.Words(curioId)),
                PropText.CurioContent(curioId) ?? RaidText.Get("str_curio_content_" + curioId, ""),
                "Investigate", byHand, "Leave it", RaidText.Get("curio_tooltip_pass", "Ignore"));
            prompt.HandDisabled = wantsQuestItem;
            prompt.SlotTooltip = wantsQuestItem ? RaidText.Get("curio_tooltip_quest_item_slot", "Drag QUEST ITEM from inventory here.")
                : questCurio ? RaidText.Get("curio_tooltip_item_slot_gather_curio", "[This is quest curio. No item is necessary to open it.]")
                : RaidText.Get("curio_tooltip_item_slot", "[OPTIONAL] Drag an item here to use it on this object.");
            prompt.TrackerCurio = questCurio ? null : type;
            prompt.Offer = slot =>
            {
                var stack = _bag.Slot(slot);
                if (stack == null) return false;
                if (wantsQuestItem)
                {
                    if (stack.Item != questItem) return false;
                    _offered = stack.Item;
                    return true;
                }
                var id = stack.Item.CurioItemId;
                // gold, gems and heirlooms are not things a curio is tried with
                if (questCurio || id == null) return false;
                if (_curios.TrackerFor(curioId, id) != null)
                {
                    _offered = stack.Item;
                    return true;
                }
                CurioMemory.Learn(type, id, TrackerIds.NoEffect);
                DungeonHud.Bark(DungeonHud.Selected, RaidText.Get("str_curio_item_had_no_effect", "That item had no effect."));
                return false;
            };
            yield return Ask(prompt);
            if (Current != this) yield break;
            if (_answer == prompt.IndexOf(RaidOptionRole.Hand))
            {
                if (!wantsQuestItem) Add(_x.UseCurio());
            }
            else if (_answer == RaidPrompt.ItemAnswer && _offered != null)
            {
                var item = _offered;
                if (wantsQuestItem)
                {
                    // the exploration counts the quest's items and takes this one
                    Add(_x.UseCurio());
                    yield break;
                }
                var events = _x.UseCurio(item.CurioItemId);
                // the item is spent when it changed what the curio did, and what it did is remembered
                if (events.OfType<CurioUsed>().Any(u => u.Outcome.ItemId != null))
                {
                    _bag.Remove(item, 1);
                    CurioMemory.Learn(type, item.CurioItemId, _curios.TrackerFor(curioId, item.CurioItemId));
                    Say("Used: " + InventoryText.Lower(item) + ".");
                }
                Add(events);
            }
        }

        /// <summary>
        /// Dev bridge and the playing bot: the item of the bag the curio at hand reacts to (the first there is),
        /// as a stack's slot; -1 for none. A player learns these from DD1's tracker marks.
        /// </summary>
        public int ItemForCurio()
        {
            var curio = _x.CurioHere;
            if (curio == null) return -1;
            if (_x.RoomId >= 0 && _map.Rooms[_x.RoomId].QuestCurio)
            {
                // a quest's curio that takes the quest's item: that item
                var objective = _map.Objective;
                if (objective == null || objective.Kind != ObjectiveKind.Activate || objective.ItemId == null) return -1;
                var wanted = _items.Ensure(ItemTypes.QuestItem, objective.ItemId);
                for (var i = 0; i < _bag.SlotCount; i++)
                    if (_bag.Slot(i)?.Item == wanted) return i;
                return -1;
            }
            for (var i = 0; i < _bag.SlotCount; i++)
            {
                var id = _bag.Slot(i)?.Item.CurioItemId;
                if (id != null && _curios.TrackerFor(curio, id) != null) return i;
            }
            return -1;
        }

        // ---- fights ----------------------------------------------------------------------------------

        private void StartFight(FightStarts fight)
        {
            var slot = fight.Slot;
            string config = null, arena = null;
            var tier = _quest?.Tier ?? DungeonContent.TierName(_map.DungeonId, slot.Tier);
            // DD1's wanderers: one called up at its altar, or one that comes in a hallway fight's place
            // (DungeonRules.cs). The fight is the wanderer's own; the tile is cleared by winning it all the same.
            var wanderer = PickWanderer(fight, tier, out config, out arena);
            if (wanderer == null)
            {
                if (slot.Kind == EncounterKind.Boss && _quest?.Boss != null) DungeonContent.PickBoss(_map.DungeonId, _quest.Boss, _quest.Tier, out config, out arena);
                else DungeonContent.Pick(_map.DungeonId, slot, _rng, out config, out arena);
            }
            if (config == null)
            {
                Plugin.Log.LogError("Dungeon: no fight available for " + slot.Kind + " tier " + slot.Tier + "; treated as won");
                Enqueue(_x.ResolveFight(FightOutcome.Won));
                return;
            }
            _fighting = true;
            _fightOutcome = FightOutcome.Retreated;
            SetFrozen(true);
            NoteTorch();
            // What the fight pays if it is won: DD1's loot code of each of its monsters. A wanderer's fight is its
            // own: DD1's codes of the Collector and the Shambler (the "bosses" map of the battle_loot block), and
            // for a wanderer DD1 has no monster for what a quest's boss pays (GUESS); what stands with the
            // wanderer pays nothing more.
            var boss = wanderer ?? (slot.Kind == EncounterKind.Boss ? _quest?.Boss : null);
            _fightDraws = DungeonContent.BattleLootDraws(_map.DungeonId, wanderer != null ? EncounterKind.Boss : slot.Kind, config, boss, tier);
            // a plot quest's own fight has rules of its own (the Shrieker's rounds and its nest: DungeonRunPlot.cs)
            PlotFightBegins(boss);
            // The enemies of this fight are as strong as the quest's tier asks (stats, ordainment, gang bosses).
            if (wanderer != null) Difficulty.Apply(_map.DungeonId, tier, EncounterKind.Boss, wanderer);
            else Difficulty.Apply(_map.DungeonId, tier, slot.Kind, slot.Kind == EncounterKind.Boss ? _quest?.Boss : null);
            // Camping buffs under a rank rule are answered by where each hero stands as the fight starts.
            SyncCampBuffs();
            // DD1's roll for who is caught off guard (DungeonRules.cs): its base chances, the torchlight's
            // share and what the camp gave the party.
            var surprised = RollSurprise(fight, wanderer);
            FightSurprise.Set(surprised == SurpriseSide.Party ? FightSurprise.HeroTeam : surprised == SurpriseSide.Monsters ? Difficulty.EnemyTeam : FightSurprise.None);
            // DD1 writes "Surprised!" over the side caught off guard as its fight begins. A fight is DD2's own
            // screen here: the banner goes up over the dungeon, on the side DD1 has the surprised stand, and
            // the fight waits until it has been read.
            var wait = surprised == SurpriseSide.Party ? DungeonHud.Announce(RaidAnnounce.Surprised) : surprised == SurpriseSide.Monsters ? DungeonHud.Announce(RaidAnnounce.MonstersSurprised) : 0f;
            BeginBattle(config, arena, wait);
        }

        // The battle starts at once, or when the banners that announce it have been up for their time.
        private void BeginBattle(string config, string arena, float wait)
        {
            if (wait <= 0f) EstateSession.StartBattle(config, arena);
            else Plugin.Host.StartCoroutine(BattleAfter(config, arena, wait));
        }

        private IEnumerator BattleAfter(string config, string arena, float wait)
        {
            var until = Time.unscaledTime + wait;
            while (Current == this && (Time.unscaledTime < until || !EstateSession.InHub))
            {
                // the session went elsewhere while the banner was up (to the menu): no fight
                if (!EstateSession.Active) yield break;
                yield return null;
            }
            if (Current != this) yield break;
            try { EstateSession.StartBattle(config, arena); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dungeon: the announced fight could not start: " + e);
                var night = _ambush;
                _fighting = false;
                _ambush = false;
                Difficulty.Clear();
                FightSurprise.Clear();
                SetFrozen(false);
                if (night)
                {
                    // as a night nothing came of: the party is back in its room
                    CorridorView.Resume();
                    SyncView();
                }
                // as a fight the content has none for: the exploration must not wait for it for ever
                else Enqueue(_x.ResolveFight(FightOutcome.Won));
            }
        }

        private void OnBattleResult(EventBattleResult e)
        {
            if (!_fighting) return;
            var result = e.m_BattleResult;
            _fightOutcome = result.m_IsRetreat ? FightOutcome.Retreated : result.IsFightComplete ? FightOutcome.Won : FightOutcome.Retreated;
            Plugin.Log.LogInfo($"Dungeon: battle result complete={result.IsFightComplete} retreat={result.m_IsRetreat}");
        }

        /// <summary>Called by the session when the hub mode is back after a fight (and on every dungeon resume).</summary>
        public void OnReturnedFromFight()
        {
            if (_restored)
            {
                ResumeAfterLoad();
                return;
            }
            DungeonHud.ShowMap(_x);
            if (!_fighting)
            {
                // The hub came back with a camp still on (it was left by something other than the night's
                // ambush): the session has just put the corridor back, the rest stop takes its place again.
                if (_camp != null && !_camp.Closing) _camp.Open();
                return;
            }
            _fighting = false;
            Difficulty.Clear();
            FightSurprise.Clear();
            if (Singleton<GameTypeMgr>.Instance.RosterManager.GetIsPartyDead()) _fightOutcome = FightOutcome.Lost;
            _eaten.Clear();
            // (a plot quest's fight that ended by its own rule says so itself: the Shrieker gone after its rounds)
            if (!PlotFightOver())
                Say(_fightOutcome == FightOutcome.Won ? "The enemy is beaten." : _fightOutcome == FightOutcome.Retreated ? "The party flees." : "The party is lost.");
            // DD1: a camping buff lasts four fights
            if (_campBuffs.FightOver()) Say("What the camp gave the party has worn off.");
            SyncCampBuffs();
            // The light is one thing: what the fight did to DD2's flame, DD1's torchlight has undergone. First of
            // all: the dark's share of the fight's loot goes by the light the fight ended in.
            var events = TakeLightBack();
            // The fight's loot comes first: DD1's scrolls are up before the room's curio is looked at and before
            // the quest says it is complete: what the fallen wore ("Reclaimed:"), or it is lost with the party,
            // then the monsters' own ("Victory!"). GUESS: the order of the two; DD1's files do not say. Both are
            // found before the save, which keeps them. (A quest that is nothing but its fight has no room to lay a
            // scroll out in: its finds go into the bag, DungeonRunBoss.cs.)
            if (IsBossQuest) StowBossFightLoot();
            else
            {
                if (ClaimFallenGear(_fightOutcome != FightOutcome.Lost)) events.Add(new LootWaits());
                if (FindBattleLoot()) events.Add(new LootWaits());
            }
            if (_ambush)
            {
                // The exploration has nothing pending for a night ambush: the party stays in its room, or is lost.
                _ambush = false;
                SyncView();
                SetFrozen(false);
                // a lost party ends the expedition the way a lost room fight does: through the event queue
                if (_fightOutcome == FightOutcome.Lost) events.Add(new QuestEnded { Status = RaidStatus.Failed });
                else if (EstateSession.InHub) EstatePersistence.SaveNow("ambush over");
                Enqueue(events);
                return;
            }
            AfterFight(_fightOutcome);
            // DD1 (retreat_always_from_raid): a fight fled that is the whole quest is the quest abandoned
            events.AddRange(FleesTheQuest ? _x.FleeRaid() : _x.ResolveFight(_fightOutcome));
            // One of the Darkest Dungeon's five stand-ins is nothing but its fight: won, its goal is met and the
            // party goes home with it at once. Whatever the outcome, the quest's end follows in this same frame
            // (and saves the estate), under the black the hub came back in: no room is shown.
            var fightOnly = IsBossQuest;
            if (fightOnly && _fightOutcome == FightOutcome.Won) events.AddRange(_x.Leave());
            if (!fightOnly) SyncView();
            SetFrozen(false);
            if (EstateSession.InHub && _fightOutcome != FightOutcome.Lost && !fightOnly) EstatePersistence.SaveNow("fight over");
            if (_fightOutcome != FightOutcome.Lost) NarrateParty();
            Enqueue(events);
        }

        // ---- effects on the party ---------------------------------------------------------------------

        private List<ActorInstance> LivingParty()
        {
            var heroes = new List<ActorInstance>();
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            foreach (var guid in Singleton<GameTypeMgr>.Instance.RosterManager.GetActorGuids(RosterStatusType.PARTY))
            {
                var hero = library.GetLibraryElement(guid);
                if (hero != null && hero.IsLiving) heroes.Add(hero);
            }
            return heroes;
        }

        private ActorInstance RandomHero()
        {
            var heroes = LivingParty();
            return heroes.Count > 0 ? heroes[_rng.Next(heroes.Count)] : null;
        }

        /// <summary>
        /// The trap goes off under a hero: the one whose try at it failed, else one who walked into it. Returns
        /// who (0: nobody was left to). What it takes is the trap's own (props/trap_definitions.json at the
        /// dungeon's difficulty: spikes a quarter of the hero's health and "Stress 2"; seen in DD1's frames
        /// raid_trap_sprung_*.png: 18 of 22 health down to 12, stress up by 19).
        /// </summary>
        private uint SpringTrap(string trapId)
        {
            ActorInstance hero = null;
            if (_trapVictim != 0u && EstateSession.IsInParty(_trapVictim)) hero = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(_trapVictim);
            _trapVictim = 0u;
            if (hero == null || !hero.IsLiving) hero = RandomHero();
            if (hero == null) return 0u;
            var guid = hero.ActorGuid;
            // DD1's banner for it: "TRAP!"; what the trap takes pops up over the hero (RaidFeedback)
            DungeonHud.Announce(RaidAnnounce.Trap);
            var level = TrapLevelOf(trapId);
            if (level == null)
            {
                // FALLBACK: a trap DD1's files do not define
                Hurt(hero, 0.1, trapId);
                hero.ApplyStressDamage(StressFor(hero, 1f), false, SourceType.STORY, trapId, 0u);
            }
            else
            {
                if (level.Health < 0) Hurt(hero, -level.Health, trapId);
                ApplyEffects(hero, level.FailEffects, trapId);
            }
            Say(hero.ActorName + " springs a trap!");
            return guid;
        }

        // DD1's named effects on a hero outside a fight, as far as a DD2 hero has something for them to act on:
        // the stress of "Stress N" (effects/base.effects.darkest, on DD2's scale), the stress "Heal Stress ..."
        // takes off; a bleed or a blight, which a DD2 hero does not carry out of a fight, is the tenth of the
        // hero's health the mod makes of a curio's (THE MOD'S OWN MAPPING). Debuffs are not played.
        private void ApplyEffects(ActorInstance hero, IEnumerable<string> effects, string sourceId)
        {
            if (hero == null || effects == null) return;
            var stress = _supply.EffectStress(effects) * StressScale;
            if (stress > 0) hero.ApplyStressDamage(StressFor(hero, Mathf.Max(1f, Mathf.Round((float)stress))), false, SourceType.STORY, sourceId, 0u);
            var calm = _supply.EffectStressHeal(effects) * StressScale;
            if (calm > 0 && hero.Stress > 0f) hero.ApplyStressHeal(Mathf.Max(1f, Mathf.Round((float)calm)), SourceType.STORY);
            foreach (var effect in effects)
                if (effect != null && (effect.StartsWith("Bleed", StringComparison.OrdinalIgnoreCase) || effect.StartsWith("Blight", StringComparison.OrdinalIgnoreCase)))
                    Hurt(hero, 0.1, effect);
        }

        // DD2 reads the flame from the TORCH run value; keeping it equal to DD1's light lets fights feel the dark.
        // The light has one keeper, the exploration: DD2's value is where a fight reads it, and where a fight
        // leaves what it did to it (NoteTorch, TakeLightBack).
        private void ApplyLight()
        {
            try { Singleton<GameTypeMgr>.Instance.RunValues.SetValue(RunValueType.TORCH, (float)_x.Light); }
            catch (Exception e) { Plugin.Log.LogWarning("could not set the torch value: " + e.Message); }
        }

        /// <summary>DD2's flame (the TORCH run value) as it stands; NaN when it cannot be read.</summary>
        internal static double Torch()
        {
            try { return Singleton<GameTypeMgr>.Instance.RunValues.GetValue(RunValueType.TORCH); }
            catch (Exception) { return double.NaN; }
        }

        // A fight begins: DD2's flame is DD1's light, and what it reads now is remembered (DD2 may keep the
        // value within bounds of its own, so it is read back, not taken for granted).
        private void NoteTorch()
        {
            ApplyLight();
            _torchAtFight = Torch();
        }

        /// <summary>
        /// The light as the dungeon's screen is to draw it. The exploration's own; and from a fight's start until
        /// its change of DD2's flame has been taken back (<see cref="TakeLightBack"/>, when the hub is "in": the
        /// corridor is put up under the game's fade some frames before that), with that change in it: the
        /// corridor comes up out of the black in the light the fight left, not in the light it began in.
        /// </summary>
        internal double LightInView
        {
            get
            {
                var light = _x.Light;
                if (double.IsNaN(_torchAtFight) || !LightFollowsTheFight) return light;
                var now = Torch();
                if (double.IsNaN(now) || Math.Abs(now - _torchAtFight) < LightTolerance) return light;
                return Math.Max(0, Math.Min(RaidRules.MaxLight, light + now - _torchAtFight));
            }
        }

        // A fight is over, however it went: what it added to DD2's flame or took from it (an item, a monster's
        // skill, a hero's) is added to DD1's light or taken from it, and DD2's flame is DD1's light again.
        private List<ExplorationEvent> TakeLightBack()
        {
            var events = new List<ExplorationEvent>();
            var before = _torchAtFight;
            var after = Torch();
            _torchAtFight = double.NaN;
            if (!double.IsNaN(before) && !double.IsNaN(after))
            {
                _lastFightBefore = before;
                _lastFightAfter = after;
                var change = after - before;
                if (LightFollowsTheFight && Math.Abs(change) >= LightTolerance)
                {
                    var was = _x.Light;
                    events = _x.AddLight(change);
                    Plugin.Log.LogInfo("Dungeon: the fight changed DD2's flame from " + before.ToString("0.##") + " to " + after.ToString("0.##") + "; the torchlight goes from "
                                       + was.ToString("0.##") + " to " + _x.Light.ToString("0.##"));
                }
            }
            ApplyLight();
            return events;
        }

        /// <summary>
        /// Dev bridge: the light on both sides: DD1's torchlight, DD2's flame, the flame as the fight in progress
        /// began, and the flame before and after the last fight (null where there was none).
        /// </summary>
        internal object LightBooks()
        {
            return new
            {
                light = _x.Light,
                torch = Num(Torch()),
                follows = LightFollowsTheFight,
                fighting = _fighting,
                torchAtFightStart = Num(_torchAtFight),
                lastFight = double.IsNaN(_lastFightBefore) ? null : new { before = _lastFightBefore, after = _lastFightAfter, change = _lastFightAfter - _lastFightBefore }
            };
        }

        private static double? Num(double value) => double.IsNaN(value) ? (double?)null : value;

        private static string CurioName(string propId)
        {
            return string.IsNullOrEmpty(propId) ? "something" : "a " + propId.Replace('_', ' ');
        }

        /// <summary>
        /// DD1's sentence for what an interaction did ("The pack contains loot!", "It's trapped!":
        /// str_curio_&lt;curio&gt;[_&lt;item&gt;]_&lt;result&gt; of localization/curios.string_table.xml); null when DD1 has none.
        /// </summary>
        public string CurioResultText(CurioOutcome outcome)
        {
            foreach (var id in _curios.ResultTextIds(outcome))
            {
                var text = RaidText.Get(id);
                if (!string.IsNullOrEmpty(text)) return RaidText.Format(text);
            }
            return null;
        }

        private IEnumerator ApplyCurio(CurioOutcome outcome)
        {
            // DD1 tells what the curio did in its banner, over the hero who did it (seen in its own frame,
            // _lab/dd1_ref/raid/raid_curio_result_banner_pack_contains_loot_kleptomaniac.png: the banner's high
            // place), for announcement_times.curio; what was found comes up after it
            var told = CurioResultText(outcome);
            if (told != null)
            {
                Say(told);
                var until = Time.unscaledTime + DungeonHud.Announce(RaidAnnounce.Curio, told);
                while (Current == this && Time.unscaledTime < until) yield return null;
            }
            switch (outcome.Type)
            {
                case CurioResultTypes.Loot:
                    var drops = new List<LootDrop>();
                    foreach (var draw in outcome.Loot) drops.AddRange(_loot.Draw(draw.Table, draw.Draws, _map.Tier, _map.DungeonId, _rng));
                    // DD1 (keep_loot): what a kleptomaniac's own hands found is the kleptomaniac's
                    if (KeepsLoot(drops)) break;
                    yield return Stow(drops);
                    break;
                case CurioResultTypes.Summon:
                    // DD1: "summon_mash_shambler", a torch set to the Shambler's altar
                    Summon(outcome.Value);
                    break;
                case CurioResultTypes.Effect:
                    ApplyCurioEffect(outcome.Value);
                    break;
                case CurioResultTypes.Scouting:
                case CurioResultTypes.QuestItem:
                case CurioResultTypes.Nothing:
                    break;
                default:
                    // a quirk, a disease, a purge, a summons: DD1's sentence is told; what it does to a DD2 hero is not played yet
                    Plugin.Log.LogInfo("Dungeon: the curio " + outcome.CurioId + " gives " + outcome.Type + (outcome.Value != null ? " " + outcome.Value : "") + ", which the Estate does not play");
                    break;
            }
        }

        private static string Listed(List<ItemStack> stacks) => string.Join(", ", stacks.Select(s => InventoryText.Counted(s.Item, s.Amount)));

        /// <summary>
        /// Loot as DD1 items the party can carry: gold in stacks, gems, heirlooms, food, supplies. A trinket of
        /// DD1's tables is a DD2 trinket of the matching rarity, and an item like the others: a card of its own
        /// on the scroll, a slot of its own in the bag. (Journal pages have no counterpart and are skipped.)
        /// </summary>
        private List<ItemStack> Carried(List<LootDrop> drops, bool firewood = false)
        {
            var found = new List<ItemStack>();
            foreach (var drop in drops)
            {
                if (drop.Kind == LootKind.Trinket)
                {
                    // not one the estate has already, here or at home, and not one of this very find
                    var trinket = Trinkets.Roll(drop.Id, _rng, found.Where(s => s.Item.Type == ItemTypes.Trinket).Select(s => s.Item.Id).ToList());
                    if (trinket == null)
                    {
                        // every trinket of the kind is the estate's already: its worth in coin
                        Trinkets.CoinInstead(drop.Id, _items, found);
                        continue;
                    }
                    // the DD1 rarity it was found under travels with it: its card, and its price at home
                    Trinkets.NoteGrade(trinket, drop.Id);
                    found.Add(new ItemStack { Item = _items.Trinket(trinket), Amount = 1 });
                    continue;
                }
                var item = drop.Amount > 0 ? _items.ForLoot(drop) : null;
                if (item == null || (!firewood && item.Id == "firewood")) continue;
                var stack = found.Find(s => s.Item == item);
                if (stack == null) found.Add(stack = new ItemStack { Item = item });
                stack.Amount += drop.Amount;
            }
            return found;
        }

        // The find is the expedition's from now on: it lies on the scroll, or waits for its turn there, until it
        // is taken or left, and a save written meanwhile keeps it. Whoever calls this sees to it that the scroll
        // is shown once for it (OfferLoot, or a LootWaits event).
        private bool Find(LootSource source, List<ItemStack> found)
        {
            if (found == null || found.Count == 0) return false;
            Say((source == LootSource.HeroDeath ? "Reclaimed from the fallen: " : "Found: ") + Listed(found) + ".");
            var find = new FoundLoot { Source = source };
            find.Stacks.AddRange(found);
            _finds.Add(find);
            return true;
        }

        /// <summary>
        /// Loot goes into the bag through DD1's loot scroll, under the heading of where it came from. What does
        /// not fit is DD1's choice: leave it, or drop a stack to make room.
        /// </summary>
        private IEnumerator Stow(List<LootDrop> drops, LootSource source = LootSource.Chest)
        {
            if (!Find(source, Carried(drops)))
            {
                Say("Nothing of value.");
                yield break;
            }
            // DD1's narration moment "loot"
            Narrate("loot");
            yield return OfferLoot();
        }

        // DD1's loot scroll: cards are taken one by one or all at once; what the bag has no room for stays on
        // the scroll until room is made (a shift-click on a stack of the bag) or the scroll is closed.
        private IEnumerator OfferLoot()
        {
            _finds.RemoveAll(f => !f.Any);
            if (_finds.Count == 0) yield break;
            var loot = _finds[0];
            var open = true;
            DungeonHud.Loot(FoundLoot.Title(loot.Source), FoundLoot.Description(loot.Source), loot.Stacks,
                i => _bag.Add(loot.Stacks[i].Item, loot.Stacks[i].Amount), () => open = false);
            while (open && Current == this) yield return null;
            _finds.Remove(loot);
            var left = loot.Stacks.Where(stack => stack.Amount > 0).ToList();
            if (left.Count > 0) Say("Left behind: " + Listed(left) + ".");
        }

        /// <summary>
        /// DD1: every monster of a won fight leaves its loot (the codes noted as the fight began), and the dark
        /// adds to it; it is offered on the "Victory!" scroll before anything else happens. Nothing after a
        /// flight or a lost fight. True when there is something to offer.
        /// </summary>
        private bool FindBattleLoot()
        {
            var draws = _fightDraws;
            _fightDraws = new List<LootDraw>();
            if (_fightOutcome != FightOutcome.Won || draws.Count == 0) return false;
            try
            {
                var drops = DungeonContent.BattleLoot.Roll(_loot, draws, _x.Light, _map.Tier, _map.DungeonId, _rng);
                Plugin.Log.LogInfo("Dungeon: battle loot " + string.Join(" ", draws.Select(d => d.Table + "x" + d.Draws)) + " at light " + Mathf.RoundToInt((float)_x.Light)
                                   + ": " + (drops.Count > 0 ? string.Join(", ", drops) : "nothing"));
                return Find(LootSource.Battle, Carried(drops));
            }
            catch (Exception e)
            {
                // a fight without its loot is still a fight won
                Plugin.Log.LogError("Dungeon: the fight's loot could not be drawn: " + e);
                return false;
            }
        }

        /// <summary>
        /// What heroes who died were wearing. DD2 would hold it for its own loot window; the estate keeps it
        /// instead (Dd2Loot). DD1: if somebody of the party lives it is offered as loot at once, on the scroll
        /// headed "Reclaimed:" (true comes back: the caller shows the scroll once), and it is lost with a lost
        /// party (<see cref="KeepTrinketsOfALostParty"/>).
        /// </summary>
        private bool ClaimFallenGear(bool partyLives)
        {
            if (Dd2Loot.FallenTrinkets.Count == 0) return false;
            var gear = Dd2Loot.ClaimFallen();
            if (!partyLives)
            {
                if (KeepTrinketsOfALostParty)
                    foreach (var id in gear) Trinkets.Put(id);
                // DD1: what a lost party wore is the Shrieker's, to be won back at its perch (Estate/Shrieker.cs)
                var hoarded = KeepTrinketsOfALostParty ? 0 : Shrieker.TakeFromTheFallen(gear);
                Plugin.Log.LogInfo("Dungeon: the trinkets of the fallen (" + string.Join(", ", gear.Select(Trinkets.Name)) + ") "
                                   + (KeepTrinketsOfALostParty ? "are sent home to the estate" : hoarded > 0 ? "are carried off by the Shrieker" : "are lost with the party"));
                return false;
            }
            return Find(LootSource.HeroDeath, gear.Where(id => Trinkets.Find(id) != null).Select(id => new ItemStack { Item = _items.Trinket(id), Amount = 1 }).ToList());
        }

        /// <summary>
        /// Called now and then while the hub is up (the sweep): what a hero who died was wearing and nobody has
        /// been offered yet is offered now. A death in the course of the expedition is seen to where it happens;
        /// this is for one that came from outside it (the dev bridge's roster.kill).
        /// </summary>
        internal void Tend()
        {
            if (Current != this || Busy || _restored || !EstateSession.InHub || _x.Status != RaidStatus.InProgress || Dd2Loot.FallenTrinkets.Count == 0) return;
            if (ClaimFallenGear(LivingParty().Count > 0)) Enqueue(new List<ExplorationEvent> { new LootWaits() });
        }

        /// <summary>
        /// The trinkets an older save carried beside the bag become items of it. What the sixteen slots cannot
        /// take goes to the estate's stores at once: in the player's favour, once. Returns what was (or, with
        /// <paramref name="dry"/>, would be) moved, in words.
        /// </summary>
        internal List<string> MoveSideTrinkets(bool dry)
        {
            var moved = new List<string>();
            foreach (var trinket in _sideTrinkets)
            {
                var item = Trinkets.Find(trinket.Key) != null ? _items.Trinket(trinket.Key) : null;
                if (item == null)
                {
                    moved.Add(trinket.Key + ": not a trinket of this game build, dropped");
                    continue;
                }
                var fits = dry ? moved.Count(m => m.EndsWith("into the bag", StringComparison.Ordinal)) < _bag.FreeSlots : _bag.Add(item, 1) == 0;
                if (!dry)
                {
                    Trinkets.NoteGrade(trinket.Key, trinket.Value);
                    if (!fits) Trinkets.Put(trinket.Key);
                }
                moved.Add(trinket.Key + (fits ? " into the bag" : " to the estate's stores (the bag is full)"));
            }
            if (dry || moved.Count == 0) return moved;
            _sideTrinkets.Clear();
            LastSideTrinkets = moved;
            Plugin.Log.LogInfo("Dungeon: the trinkets an older save carried beside the bag: " + string.Join("; ", moved));
            try
            {
                var home = moved.Count(m => m.EndsWith("(the bag is full)", StringComparison.Ordinal));
                var inBag = moved.Count(m => m.EndsWith("into the bag", StringComparison.Ordinal));
                ActivityLog.Add(ActivityLog.Kinds.Note, "The trinkets the party found on its way are carried in the bag now: " + inBag + " put into it"
                                                        + (home > 0 ? ", " + home + " sent home to the estate for want of room." : "."));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon: the Activity Log did not take the line about the bag's trinkets: " + e.Message); }
            return moved;
        }

        /// <summary>What <see cref="MoveSideTrinkets"/> did when the expedition of the save loaded last was put up; null when it had none.</summary>
        internal static List<string> LastSideTrinkets { get; private set; }

        /// <summary>Dev bridge: an older save's trinkets beside the bag that have not been put into it yet (ledger.hold).</summary>
        internal int SideTrinkets => _sideTrinkets.Count;

        /// <summary>A trinket that came off a hero of the party without anybody's doing (DD2 took it off) goes into the bag. False: no room.</summary>
        internal bool TakeIntoBag(string trinketId)
        {
            var item = Trinkets.Find(trinketId) != null ? _items.Trinket(trinketId) : null;
            return item != null && _bag.Add(item, 1) == 0;
        }

        /// <summary>Dev bridge: puts items in the bag as if found; returns what did not fit.</summary>
        public int Give(ItemDef item, int amount) => _bag.Add(item, amount);

        /// <summary>Dev bridge: what the fight in progress pays if it is won (DD1's table codes); empty out of a fight.</summary>
        internal IReadOnlyList<LootDraw> FightPays => _fightDraws;

        /// <summary>Dev bridge: the loot lying on the scroll, then what waits for its turn there.</summary>
        internal IReadOnlyList<FoundLoot> LootOnScroll => _finds;

        /// <summary>
        /// Dev bridge: the battle loot rule rolled without a fight, for the expedition's dungeon and difficulty.
        /// With <paramref name="show"/> the drops are offered on the "Victory!" scroll as after a won fight
        /// (THE BAG CHANGES when they are taken); false comes back when the expedition is in the middle of
        /// something and nothing was shown.
        /// </summary>
        internal List<LootDrop> DevBattleLoot(List<LootDraw> draws, double light, bool show, out bool shown)
        {
            shown = false;
            var drops = DungeonContent.BattleLoot.Roll(_loot, draws, light, _map.Tier, _map.DungeonId, _rng);
            if (!show || Busy || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return drops;
            if (Find(LootSource.Battle, Carried(drops)))
            {
                shown = true;
                Enqueue(new List<ExplorationEvent> { new LootWaits() });
            }
            return drops;
        }

        /// <summary>
        /// Dev bridge: items named outright, offered on DD1's loot scroll under the heading of a source, as the
        /// expedition's own finds are (they are in the save while they lie there). False when the expedition is
        /// in the middle of something.
        /// </summary>
        internal bool DevFind(LootSource source, List<ItemStack> stacks)
        {
            if (Busy || !EstateSession.InHub || _x.Status != RaidStatus.InProgress || !Find(source, stacks)) return false;
            Enqueue(new List<ExplorationEvent> { new LootWaits() });
            return true;
        }

        // What a curio's effect does lands on the hero who turned to it (DD1: "The selected hero will perform the
        // interaction."), and shows over that hero as DD1's pop text (RaidFeedback); the sentence was the banner's.
        private void ApplyCurioEffect(string effect)
        {
            var hero = Acting();
            if (hero == null || effect == null) return;
            var name = effect.ToLowerInvariant();
            if (name.Contains("healstress") || (name.Contains("heal") && name.Contains("stress"))) hero.ApplyStressHeal(2f, SourceType.STORY);
            else if (name.StartsWith("stress") || name.Contains("curse")) hero.ApplyStressDamage(2f, false, SourceType.STORY, effect, 0u);
            else if (name.Contains("heal")) hero.ApplyHealthHeal(hero.CurrentHpMax * 0.25f, false, SourceType.STORY, false);
            else if (name.StartsWith("bleed") || name.StartsWith("blight")) hero.ApplyHealthDamage(Mathf.Max(1f, hero.CurrentHpMax * 0.1f), false, false, hero, DeathType.EFFECT, SourceType.STORY, effect, false);
            else if (name.Contains("darkness")) Add(_x.AddLight(-25));
            else if (name.Contains("light")) Add(_x.AddLight(100));
            else Plugin.Log.LogInfo("Dungeon: the curio's effect " + effect + " is not played in the Estate");
        }

        // ---- camping ---------------------------------------------------------------------------------

        public int Firewood => _bag.Count(_items.Find("firewood"));

        /// <summary>Food in the bag, for the camp's meal.</summary>
        public int Food => _items.Food != null ? _bag.Count(_items.Food) : 0;

        /// <summary>
        /// Why the party cannot make camp here and now; null when it can. DD1: a log of firewood, in a room
        /// (not a hallway) that is not the first one (DD1's own words for it) and has no fight left in it.
        /// </summary>
        public string CampRefusal
        {
            get
            {
                if (_camp != null) return "The party is camping.";
                if (_fighting || _processing || _restored || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return "Not now.";
                if (Firewood <= 0) return "No firewood.";
                var room = _x.RoomId >= 0 ? _map.GetRoom(_x.RoomId) : null;
                if (room == null) return "The party can only camp in a room.";
                if (room.Id == _map.EntranceId) return RaidText.Get("str_cant_use_firewood_in_entrance_room", "Can't camp in first room");
                if (_x.Pending != PendingKind.None || (room.Battle != null && !_x.IsRoomFightWon(room.Id))) return "The room has to be cleared first.";
                return null;
            }
        }

        /// <summary>
        /// Burns a log of firewood and makes camp: DD1 has the torch burn at full again
        /// (camp_restore_torch), and what the last camp gave the party ends ("until next Camp").
        /// </summary>
        public bool MakeCamp(out string message)
        {
            message = CampRefusal;
            if (message != null) return false;
            _bag.Remove(_items.Find("firewood"), 1);
            _campBuffs.Clear();
            SyncCampBuffs();
            ChangeLight(CampContent.Rules.RestoreTorch);
            Say(message = "The party makes camp.");
            _camp = Camp.Begin(this);
            Narration.Trigger("camp", Narration.Scope.Dungeon);
            if (EstateSession.InHub) EstatePersistence.SaveNow("camp");
            return true;
        }

        /// <summary>Dev bridge: a camp wherever the party stands, with or without firewood.</summary>
        internal string ForceCamp()
        {
            if (_camp != null) return "The party is camping.";
            if (_fighting || _restored || !EstateSession.InHub || _x.Status != RaidStatus.InProgress) return "Not now.";
            _campBuffs.Clear();
            SyncCampBuffs();
            ChangeLight(CampContent.Rules.RestoreTorch);
            Say("The party makes camp.");
            _camp = Camp.Begin(this);
            return null;
        }

        /// <summary>The camp is over and its rest stop put away: the night's ambush, or back to the dungeon.</summary>
        internal void EndCamp(Camp camp, CampNight night)
        {
            if (_camp != camp) return;
            _camp = null;
            // DD1: a hero who ate at camp is not full any more by the next hunger
            _eaten.Clear();
            SyncCampBuffs();
            if (night != null && night.Ambushed && EstateSession.InHub)
            {
                // DD1 (ambush_torch_reduction): the fire is out and the fight is in the dark
                ChangeLight(CampContent.Rules.AmbushTorch);
                Say("Ambush! The camp is attacked in the night.");
                if (StartAmbush(night)) return;
            }
            else Say("The night passes quietly.");
            CorridorView.Resume();
            SyncView();
            if (EstateSession.InHub) EstatePersistence.SaveNow("camp over");
        }

        // DD1 fills a night ambush from the dungeon's own monsters: a hallway fight of the quest's tier, here in
        // the arena DD2's Kingdoms uses for an ambush at a rest stop.
        private bool StartAmbush(CampNight night)
        {
            var slot = new EncounterSlot { Kind = EncounterKind.Hallway, Tier = _map.Tier };
            DungeonContent.Pick(_map.DungeonId, slot, _rng, out var config, out var arena);
            if (config == null)
            {
                Plugin.Log.LogError("Dungeon: no fight available for a night ambush; the night passes");
                Say("Shapes move beyond the firelight, and are gone.");
                return false;
            }
            if (!string.IsNullOrEmpty(CampView.AmbushArena)) arena = CampView.AmbushArena;
            _fighting = true;
            _ambush = true;
            _fightOutcome = FightOutcome.Retreated;
            SetFrozen(true);
            var tier = _quest?.Tier ?? DungeonContent.TierName(_map.DungeonId, slot.Tier);
            NoteTorch();
            // a night ambush pays like the hallway fight it is
            _fightDraws = DungeonContent.BattleLootDraws(_map.DungeonId, slot.Kind, config, null, tier);
            Difficulty.Apply(_map.DungeonId, tier, slot.Kind, null);
            SyncCampBuffs();
            // DD1 (surprise_ambush_party_base_chance): the sleepers are surprised; DD2's turn order has a place
            // for a surprised team, which loses the first round.
            // (DD1 asks the quest first: where is_surprise_enabled is off, in the Darkest Dungeon, not even the
            // sleepers are surprised.)
            var sleepersSurprised = SurpriseEnabled && night.PartySurprised;
            var attackersSurprised = SurpriseEnabled && !sleepersSurprised && night.MonstersSurprised;
            FightSurprise.Set(sleepersSurprised ? FightSurprise.HeroTeam : attackersSurprised ? Difficulty.EnemyTeam : FightSurprise.None);
            // DD1's banners for it: "Ambush!" over the camp, then "Surprised!" over whoever is caught off guard
            // (the sleepers on the heroes' side, the attackers on the monsters'). The rest stop is put away by now
            // and the fight is DD2's own screen: they go up over the dark, and the fight waits for them.
            var wait = DungeonHud.Announce(RaidAnnounce.Ambush);
            if (sleepersSurprised) wait = Mathf.Max(wait, DungeonHud.Announce(RaidAnnounce.Surprised));
            else if (attackersSurprised) wait = Mathf.Max(wait, DungeonHud.Announce(RaidAnnounce.MonstersSurprised));
            BeginBattle(config, arena, wait);
            return true;
        }

        /// <summary>Puts the camping buffs the ledger holds on the heroes, and their scouting share into the exploration.</summary>
        internal void SyncCampBuffs()
        {
            CampBuffs.Sync(_campBuffs);
            var scouting = _campBuffs.Total(CampContent.Rules, CampBuffKind.Scouting);
            _x.ScoutBonus += scouting - _campScouting;
            _campScouting = scouting;
        }

        internal void CampSay(string line) => Say(line);

        /// <summary>Torchlight changed by a camp (lit, ambushed, a dark ritual).</summary>
        internal void ChangeLight(double delta)
        {
            _x.AddLight(delta);
            ApplyLight();
        }

        internal void TakeFood(int amount)
        {
            if (amount > 0 && _items.Food != null) _bag.Remove(_items.Food, amount);
        }

        /// <summary>
        /// Loot a camping skill turns up (DD1 loot table by id), put in the bag as far as it fits: nobody is
        /// asked what to drop at a camp. Returns what was found, in words.
        /// </summary>
        internal string CampLoot(string table, int draws)
        {
            var found = Carried(_loot.Draw(table, draws, _map.Tier, _map.DungeonId, _rng), firewood: true);
            if (found.Count == 0) return "nothing the party can carry was found";
            var text = "found " + Listed(found);
            var left = new List<ItemStack>();
            foreach (var stack in found)
            {
                var over = _bag.Add(stack.Item, stack.Amount);
                if (over > 0) left.Add(new ItemStack { Item = stack.Item, Amount = over });
            }
            return left.Count > 0 ? text + " (no room for " + Listed(left) + ")" : text;
        }

        // ---- leaving ---------------------------------------------------------------------------------

        /// <summary>HUD button: go home with the quest done, or abandon it.</summary>
        public void Leave()
        {
            if (Busy || !EstateSession.InHub || !_x.CanLeave) return;
            // DD1 (can_retreat): the last descent has no way back until its work is done.
            var noWayBack = _x.ObjectiveComplete ? null : QuestBoard.RetreatBlockReason(_quest);
            if (noWayBack != null)
            {
                Say(noWayBack);
                return;
            }
            Enqueue(_x.Leave());
        }

        /// <summary>
        /// The expedition is over. DD1's order (as_raid_finish, then the results screens, then the town): all
        /// of it is applied first (stress, the retreat's price, the quest's pay, the bag, experience, the
        /// quirks of the quest's end, the week), the estate is saved, and what is shown between the dungeon
        /// and the hamlet is a record of what was done. "Return to Town" on its second page leads on to the
        /// hamlet; if the record or the screen cannot be made the hamlet comes up at once, as it used to.
        /// </summary>
        private void Finish(QuestEnded ended)
        {
            var success = ended.Status == RaidStatus.Succeeded;
            Plugin.Log.LogInfo("Dungeon: finished, " + ended.Status);
            _ending = true;
            _campBuffs.Clear();
            SyncCampBuffs();
            // What still lay on a loot scroll stays behind. What the fallen wore and nobody has been offered yet
            // (the hero died as the expedition ended) is not left lying: into the bag if it has room, else home.
            _finds.Clear();
            if (ClaimFallenGear(LivingParty().Count > 0))
            {
                foreach (var stack in _finds.SelectMany(f => f.Stacks))
                    if (_bag.Add(stack.Item, stack.Amount) > 0) Trinkets.Put(stack.Item.Id);
                _finds.Clear();
            }
            // What each of them has before anything of the quest's end is applied: the results screen shows the difference.
            if (_party.Count == 0) RememberParty();
            foreach (var hero in LivingParty())
            {
                var member = _party.Find(m => m.Guid == hero.ActorGuid);
                if (member != null) member.Xp = Resolve.Experience(hero.ActorGuid);
            }
            if (ended.Stress > 0)
                foreach (var hero in LivingParty()) hero.ApplyStressDamage(Mathf.Max(1f, (float)(ended.Stress * StressScale)), false, SourceType.STORY, null, 0u);

            var estate = EstateState.Current;
            if (success) estate.ExpeditionsWon++;
            else estate.ExpeditionsLost++;
            // DD1 (retreat_party_kill_count): leaving the Darkest Dungeon unfinished costs a hero.
            if (ended.Status == RaidStatus.Abandoned)
            {
                var fallen = QuestBoard.PayForRetreat(_quest, LivingParty().Select(h => h.ActorGuid).ToList());
                if (fallen.Count > 0) QuestBoard.Report(UpgradeText.Join(fallen) + (fallen.Count == 1 ? " stays behind so the others may leave." : " stay behind so the others may leave."));
            }
            // The quest's pay and the bag are all the expedition brings the estate, and they are written down
            // once: the purse, the results screen and the Activity Log are read against the same record.
            var purseBefore = EstateState.Gold;
            var rewards = QuestBoard.Finished(_quest, success);
            var haul = BringHome(ended.Status, out var haulGold);
            // DD1's blueprint is the boss's loot: the districts' own currency, and a card of the results' heirlooms
            Districts.BossLoot(_quest?.Id, success, haul);
            var books = ExpeditionBooks.Close(_quest?.Id ?? "(no quest)", _map.DungeonId, ended.Status, purseBefore, rewards, haul, haulGold);
            EstateState.RaiseExpeditionEnded(new EstateState.Expedition
            {
                Dungeon = _map.DungeonId,
                QuestId = _quest?.Id,
                Difficulty = _quest != null && _quest.Tier == "darkest" ? 6 : _map.Tier,
                Length = _map.Length,
                Success = success,
                ResolveXp = QuestBoard.ResolveXpOf(_quest),
                Survivors = LivingParty().Select(h => h.ActorGuid).ToList(),
                Books = books,
                Party = _party.Select(m => m.Guid).ToList(),
                // (a member's experience is what they set out with: the quest's own is paid after this)
                PartyLevels = _party.Select(m => Resolve.LevelOf(m.Xp)).ToList(),
                Dd1Plot = QuestBoard.Dd1Plot(_quest)
            });
            // DD1's roll at the quest's end and the record of it all; then the week turns (the town event's
            // share of experience is given there) and the record reads the experience again.
            var results = ComeHome(success, rewards, haul);
            if (results != null) results.Books = books;
            estate.AdvanceWeek();
            if (results != null)
            {
                foreach (var row in results.Heroes)
                    if (!row.Dead) row.XpAfter = Math.Max(row.XpBefore, Resolve.Experience(row.Guid));
                Plugin.Log.LogInfo("Dungeon: results: " + results);
            }

            Dispose();
            SetFrozen(false);
            CorridorView.Hide();
            // The session's view stays the dungeon's while the results are up (the hamlet opens with "Return to
            // Town"); with the expedition gone its HUD has nothing to show.
            if (results != null && RaidResultsScreen.Show(results, () => EstateSession.View = EstateSession.Screen.Hamlet)) DungeonHud.Hide();
            else EstateSession.View = EstateSession.Screen.Hamlet;
            if (EstateSession.InHub) EstatePersistence.SaveNow("expedition over");
        }

        /// <summary>
        /// The record for DD1's results screens, and DD1's roll for those who came home (a quirk of either
        /// kind, a disease: <see cref="QuestAftermath"/>). Null when it could not be made: the expedition still
        /// ends, without the screens.
        /// </summary>
        private RaidResults ComeHome(bool success, List<QuestPayment> rewards, RaidHaul haul)
        {
            try
            {
                var living = LivingParty();
                var results = new RaidResults
                {
                    // DD1: Victory by the goal; otherwise Escape while enough of the party lives (three), else Defeat
                    Outcome = QuestAftermath.Rules.Outcome(success, living.Count),
                    QuestName = _quest == null ? DungeonContent.DisplayName(_map.DungeonId) : _quest.Plot ? _quest.Name : QuestMapText.TypeName(_quest),
                    Dungeon = _map.DungeonId,
                    Rewards = rewards ?? new List<QuestPayment>()
                };
                results.SetHaul(haul);
                foreach (var member in _party)
                {
                    var hero = living.Find(h => h.ActorGuid == member.Guid);
                    var row = new RaidResults.Hero { Guid = member.Guid, Name = member.Name, ClassId = member.ClassId, Dead = hero == null, XpBefore = member.Xp, XpAfter = member.Xp };
                    if (hero != null)
                    {
                        row.NewQuirks = QuestAftermath.Roll(hero, success, _rng);
                        // DD1's quirks of a plot quest's own (party_quirks_to_apply_on_*: the Shrieker's)
                        row.NewQuirks.AddRange(PlotQuests.QuirksFor(_quest, hero, success, _rng));
                        row.Stress = Mathf.RoundToInt(10f * Mathf.Clamp01(hero.Stress / Mathf.Max(1f, hero.StressMax)));
                        row.Diseased = QuestAftermath.Diseased(hero);
                    }
                    results.Heroes.Add(row);
                }
                return results;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Dungeon: the results of the expedition could not be put together: " + e);
                return null;
            }
        }

        // DD1: coins, gems (at their sell value) and heirlooms go to the estate, leftover food and supplies are
        // sold at their sell value, the trinkets found go to the trinket storage. A retreat keeps the shares of
        // estate.json (a trinket's share is its chance); a lost party's bag is gone, its trinkets too unless
        // the owner's choice keeps those (KeepTrinketsOfALostParty).
        private RaidHaul BringHome(RaidStatus status, out int gold)
        {
            var haul = _bag.Settle(type => type == ItemTypes.Trinket && status == RaidStatus.Failed && KeepTrinketsOfALostParty ? 1 : _supply.KeepRate(type, status), _rng);
            gold = haul.Gold;
            EstateState.AddGold(gold, "haul");
            var parts = new List<string>();
            if (gold > 0) parts.Add(gold + " gold");
            foreach (var pair in haul.Heirlooms)
            {
                EstateState.Current.AddHeirloom(pair.Key, pair.Value);
                parts.Add(InventoryText.Counted(_items.Ensure(ItemTypes.Heirloom, pair.Key), pair.Value));
            }
            Plugin.Log.LogInfo($"Dungeon: haul {status}: {haul.Coins} coins + {haul.GemGold} gems + {haul.SupplyGold} supplies = {gold} gold; heirlooms {string.Join(", ", haul.Heirlooms.Select(h => h.Value + " " + h.Key))}");
            // The trinkets of the bag lie in the estate's stores now; each has the DD1 rarity it was found under.
            foreach (var id in haul.Trinkets)
            {
                if (Trinkets.Find(id) == null) continue;
                Trinkets.Put(id);
                parts.Add("the trinket " + Trinkets.Name(id));
            }
            // an older save's trinkets that never got into the bag (the sweep was held back) are not lost for it
            foreach (var trinket in _sideTrinkets)
                if (status != RaidStatus.Failed || KeepTrinketsOfALostParty) Trinkets.Give(trinket.Key, trinket.Value);
            _sideTrinkets.Clear();
            // What came home is told by the results screen card by card (and by the activity log), not by the hamlet.
            if (parts.Count > 0) Plugin.Log.LogInfo("Dungeon: brought home: " + string.Join(", ", parts));
            return haul;
        }
    }
}
