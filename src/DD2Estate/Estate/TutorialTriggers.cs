using System;
using System.Collections.Generic;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The moments at which DD1 asks for a tutorial, found in the estate the way <see cref="NarrationMoments"/>
    /// finds its own: by looking at what the screens and the expedition say of themselves once a frame, so none
    /// of them calls in here. Each moment is DD1's, read at the place in its exe that asks for the tutorial
    /// (_windows/win32/Darkest.exe, the address in brackets):
    /// <list type="bullet">
    /// <item>The town (TownUI's building screen, 0xa75bf8): opening the Stage Coach asks for "stage_coach", the
    /// Tavern or the Abbey for "stress_relief", the Sanitarium for "locking_pos_quirks". A hire that leaves no
    /// recruit at the coach asks for "stage_coach2" (0xa5d38b). The Estate Map coming up asks for "quest_select"
    /// (0xa37a93); the Provision screen for "camping_quest" when the quest's free kit has firewood in it, for
    /// "supplies" when it has none (0xa34740). Every time the town comes up (TownDisplay::OnAppear, 0xa7163d) a
    /// waiting "embark" is dropped and asked for anew, to be shown when four heroes are on the roster and no
    /// panel is open (its condition, 0xa7f480). A town event that names tutorials asks for them as its notice
    /// comes (0xa69247); the base game and the Districts have one, Cornerstones. The Districts screen asks for
    /// "districts_panel" as it opens (0xa19e1f).</item>
    /// <item>An expedition (the raid screen): "hero_panel" as it comes up, unless it is the Old Road (0xaf0cea);
    /// "hallway_nav" when the party enters a hallway (0xafcbe7), "map_nav" when a room comes up (0xaff415); a
    /// curio that stands in the area on screen and has not been touched asks for "curio", a trap seen there
    /// for "disarm_trap" (the prop display, 0xac353a); the torch falling from half or more to under half for
    /// "torch" (0xb11b15); a scouted tile that is a hidden room for "scout_hidden_door" (0xad7681); the loot
    /// window for "loot" (0xaa9e01: "blueprints" instead when a blueprint lies in it); the camp's meal window
    /// for "camping" (0xa9e1aa); the quest's goal reached for "quest_complete" (0xadf277); a hero's death for
    /// "consider_retreat" (Party::CheckForDeath, 0x9b0b4f).</item>
    /// <item>"resolve_level" is asked for by DD1's resolve bar when the level it draws is higher than the one it
    /// drew before, and it drew one before (0xb97564: a bar that showed 0 says nothing, so the step from 0 to 1
    /// passes in silence and the first to speak is 1 to 2).</item>
    /// </list>
    /// </summary>
    [EstateModule]
    internal static class TutorialTriggers
    {
        /// <summary>DD1's resolve bar speaks of a level only after it has shown one that is not 0.</summary>
        public const int ResolveFromLevel = 1;
        /// <summary>DD1: the torch tutorial comes when the light falls under half of its 100.</summary>
        private const double TorchHalf = 50.0;
        private const float LevelsEvery = 0.5f;

        // the town
        private static bool _hamlet, _map, _provision, _crier, _districts;
        private static int _blueprints = -1;
        private static string _window;
        private static int _recruits = -1;
        // an expedition
        private static DungeonRun _run;
        private static int _room = int.MinValue, _hallway = int.MinValue;
        private static double _light = -1.0;
        private static bool _camp, _loot;
        // the roster
        private static readonly Dictionary<uint, int> Levels = new Dictionary<uint, int>();
        private static float _nextLevels;

        private static void Register()
        {
            NarrationMoments.Tick += Tick;
            NarrationMoments.SessionEnded += Reset;
            NarrationMoments.ObjectiveCompleted += run => Tutorials.Queue("quest_complete", OnExpedition);
            NarrationMoments.HeroFell += fallen =>
            {
                if (DungeonRun.Current != null) Tutorials.Queue("consider_retreat", OnExpedition);
            };
            Scouting.Reported += scouted =>
            {
                if (scouted == null || !Tutorials.Enabled) return;
                foreach (var tile in scouted.Tiles)
                {
                    if (tile.Content != HallContent.Secret) continue;
                    Tutorials.Queue("scout_hidden_door", OnExpedition);
                    return;
                }
            };
        }

        /// <summary>What was seen last is forgotten: the next look takes things as they are, without a moment.</summary>
        public static void Reset()
        {
            _hamlet = _map = _provision = _crier = _districts = false;
            _blueprints = -1;
            _window = null;
            _recruits = -1;
            _run = null;
            _room = _hallway = int.MinValue;
            _light = -1.0;
            _camp = _loot = false;
            Levels.Clear();
            _nextLevels = 0f;
        }

        // ---- where a tutorial of each kind may come up -------------------------------------------------------

        private static bool InHamlet() => HamletScreen.IsOpen && EstateSession.View == EstateSession.Screen.Hamlet && !RaidResultsScreen.IsOpen;

        private static bool OnExpedition() => DungeonRun.Current != null && EstateSession.View == EstateSession.Screen.Dungeon && !RaidResultsScreen.IsOpen;

        // DD1's condition of "embark" (0xa7f480): a campaign, four heroes or more, no panel of the town open.
        // GUESS: which heroes DD1 counts there (a filter on the roster that was not read); here every living one.
        private static bool EmbarkReady()
        {
            return InHamlet() && RosterLifecycle.Count >= 4 && !RosterWindow.IsOpen && TownPanel.Current == null
                   && !QuestPanel.IsOpen && !ProvisionScreen.IsOpen && !TownEventPanel.IsOpen;
        }

        private static void Tick()
        {
            if (!Tutorials.Enabled) return;
            try
            {
                Town();
                Expedition();
                Roster();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Tutorial: watching for the moments failed: " + e);
            }
        }

        // ---- the town ------------------------------------------------------------------------------------------

        private static void Town()
        {
            var hamlet = InHamlet();
            if (hamlet && !_hamlet)
            {
                Tutorials.Cancel("embark");
                Tutorials.Queue("embark", EmbarkReady);
            }
            _hamlet = hamlet;

            var window = hamlet && RosterWindow.Current != null ? RosterWindow.Current.Id : null;
            if (window != _window)
            {
                _window = window;
                _recruits = -1;
                switch (window)
                {
                    case StageCoach.BuildingId: Tutorials.Queue("stage_coach", () => RosterWindow.Shows(StageCoach.BuildingId)); break;
                    case "tavern":
                    case "abbey": Tutorials.Queue("stress_relief", InHamlet); break;
                    case SanitariumRules.Building: Tutorials.Queue("locking_pos_quirks", InHamlet); break;
                }
            }
            if (window == StageCoach.BuildingId)
            {
                // the last recruit of the week has just been taken on
                var recruits = StageCoach.Offer.Count;
                if (_recruits > 0 && recruits == 0) Tutorials.Queue("stage_coach2", () => RosterWindow.Shows(StageCoach.BuildingId));
                _recruits = recruits;
            }

            var map = hamlet && QuestPanel.IsOpen;
            if (map && !_map) Tutorials.Queue("quest_select", () => QuestPanel.IsOpen);
            _map = map;

            var provision = hamlet && ProvisionScreen.IsOpen;
            if (provision && !_provision) Tutorials.Queue(HasFirewood(ProvisionScreen.Quest) ? "camping_quest" : "supplies", () => ProvisionScreen.IsOpen);
            _provision = provision;

            var crier = hamlet && TownEventPanel.IsOpen;
            if (crier && !_crier)
            {
                var week = TownEvents.Current;
                // DD1's town event that names the Districts' tutorial (districts.town_events.events.json: tutorial_ids)
                if (week != null && week.Id == Districts.UnlockEvent) Tutorials.Queue("districts", InHamlet);
            }
            _crier = crier;

            // The Districts (0xa19e1f: their screen asks as it opens). DD1 asks for "blueprints" in place of "loot"
            // when a blueprint lies in a loot window; the estate hands a boss's blueprint over with the quest's
            // rewards, so the tutorial comes when the estate holds more of them than it did.
            var districts = hamlet && DistrictsPanel.IsOpen;
            if (districts && !_districts) Tutorials.Queue("districts_panel", () => DistrictsPanel.IsOpen);
            _districts = districts;
            var blueprints = Districts.Blueprints;
            if (_blueprints >= 0 && blueprints > _blueprints) Tutorials.Queue("blueprints", InHamlet);
            _blueprints = blueprints;
        }

        // DD1 counts the firewood among what the quest's length gives for free (raid_starting_length_inventory_item_lists).
        private static bool HasFirewood(Quest quest)
        {
            if (quest == null) return false;
            foreach (var stack in InventoryContent.Provision.StartingItems(quest.Length))
                if (stack.Item != null && stack.Item.Id == "firewood" && stack.Amount > 0) return true;
            return false;
        }

        // ---- an expedition --------------------------------------------------------------------------------------

        private static void Expedition()
        {
            var run = DungeonRun.Current;
            if (run == null || EstateSession.View != EstateSession.Screen.Dungeon)
            {
                // the expedition a hero fell on is over: its question of going on or turning back is too
                if (_run != null && run == null) Tutorials.Cancel("consider_retreat");
                _run = null;
                _camp = _loot = false;
                return;
            }
            var x = run.Exploration;
            if (!ReferenceEquals(run, _run))
            {
                _run = run;
                _room = _hallway = int.MinValue;
                _light = x.Light;
                Tutorials.Queue("hero_panel", OnExpedition);
            }
            if (!EstateSession.InHub) return;       // a fight is on, or the way into one

            // where the party stands: a hallway (a secret room hangs on one), or a room
            var hallway = x.InSecretRoom ? -1 : x.HallwayId;
            var room = hallway >= 0 ? -1 : x.RoomId;
            if (room != _room || hallway != _hallway)
            {
                _room = room;
                _hallway = hallway;
                if (hallway >= 0) Tutorials.Queue("hallway_nav", () => OnExpedition() && DungeonRun.Current.Exploration.HallwayId >= 0);
                else if (room >= 0 && !x.InSecretRoom) Tutorials.Queue("map_nav", () => OnExpedition() && DungeonRun.Current.Exploration.HallwayId < 0);
            }

            var light = x.Light;
            if (_light >= TorchHalf && light < TorchHalf) Tutorials.Queue("torch", OnExpedition);
            _light = light;

            Props(run);

            var camp = CampScreen.IsOpen;
            if (camp && !_camp) Tutorials.Queue("camping", () => CampScreen.IsOpen);
            _camp = camp;

            var loot = DungeonHud.LootOpen;
            if (loot && !_loot) Tutorials.Queue("loot", () => DungeonHud.LootOpen);
            _loot = loot;
        }

        // DD1's prop display asks as it draws a prop of the area on screen: a curio not yet used, a trap that is
        // there to be seen. Here: such a prop within the width of the view.
        private static void Props(DungeonRun run)
        {
            var curio = Tutorials.HasSeen("curio") || Tutorials.IsQueued("curio");
            var trap = Tutorials.HasSeen("disarm_trap") || Tutorials.IsQueued("disarm_trap");
            if (curio && trap) return;
            var view = CorridorView.Instance;
            if (view == null || view.Frozen) return;
            var half = view.ViewPixels * 0.5f;
            foreach (var prop in view.Props.All)
            {
                if (prop == null || prop.Staged || prop.State == null) continue;
                if (!view.IsRoom && Mathf.Abs(prop.Foot.x - view.CameraAt.x) > half) continue;
                if (prop.Kind == PropKind.Curio && !curio && prop.State != ExplorationProps.Investigated)
                {
                    curio = true;
                    Tutorials.Queue("curio", OnExpedition);
                }
                else if (prop.Kind == PropKind.Trap && !trap && prop.State != ExplorationProps.Sprung
                         && prop.Segment >= 0 && view.Props.Hallway != null && !run.Exploration.IsSegmentDone(view.Props.Hallway.Id, prop.Segment))
                {
                    trap = true;
                    Tutorials.Queue("disarm_trap", OnExpedition);
                }
            }
        }

        // ---- the roster ------------------------------------------------------------------------------------------

        // DD1 hears of a level from the bar that draws it. The estate has more than one bar and none of them is this
        // file's: the heroes' levels are looked at instead, and the tutorial comes in the hamlet, where DD1's roster
        // has the same bar.
        private static void Roster()
        {
            if (Time.unscaledTime < _nextLevels) return;
            _nextLevels = Time.unscaledTime + LevelsEvery;
            foreach (var guid in RosterLifecycle.LivingGuids())
            {
                var level = Resolve.Level(guid);
                if (Levels.TryGetValue(guid, out var before) && level > before && before >= ResolveFromLevel)
                    Tutorials.Queue("resolve_level", InHamlet);
                Levels[guid] = level;
            }
        }
    }
}
