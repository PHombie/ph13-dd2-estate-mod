using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The Ancestor speaks, as text, at the moments DD1 has him speak and the estate has too:
    ///
    ///   moment in the estate                     DD1 trigger (audio/narration.json)      tags given
    ///   the hamlet comes up in a new week        town_visit_start                        the week's town event
    ///   a building is clicked                    enter_building                          the building
    ///   the Estate Map / the provisions open     enter_quest_select, enter_provision_select
    ///   a building step is built                 upgrade_building                        the building
    ///   a hero is hired / sent away              recruit_hero / dismiss_hero             the class
    ///   an expedition begins                     quest_start                             dungeon, quest type
    ///   its goal is reached                      quest_end_completed                     dungeon, quest type
    ///   it ends without success                  quest_end_not_completed
    ///   a hero dies                              kill_hero
    ///
    /// Which line answers, how often and with what chance is DD1's (<see cref="NarrationRules"/>); the words are
    /// DD1's subtitles (<see cref="Dd1Strings"/>). DD1's lines about its own bosses and plot quests are tagged
    /// with those bosses and quests; the estate never gives such a tag, so they stay unsaid, and the mod's own
    /// story speaks instead: a story quest's confession when its expedition begins and its closing words when
    /// its goal is reached (Data/plot_quests.json). What has been said is remembered in the "narration" section
    /// of the estate's save.
    ///
    /// The lines are shown with <see cref="NarrationBox"/>, one after another. How a line is picked is DD1's
    /// own (read in its executable, the narration queue at 0x7d6bb0): the moment's own chance decides whether
    /// it is answered at all; then only the lines of the highest priority among those that fit and are not
    /// used up are looked at; of those, lines that name tags stand before lines that name none; and one of
    /// what is left is always picked, its chance being its weight among them (0.5 + 0.5, five times 0.2 ...).
    /// The only silence in such a group is an entry without a clip, which DD1 put there on purpose (eight
    /// trap lines of 0.25 and a silent 0.2).
    ///
    /// On an expedition the moments of the walk are raised by the dungeon (<see cref="RaidNarration"/> lists
    /// them): the torch lit and out, hunger, a trap, an obstacle, a curio, loot, the party at half health.
    /// </summary>
    [EstateModule]
    internal static class Narration
    {
        private const string SectionKey = "narration";

        /// <summary>Where a line still makes sense when its turn comes.</summary>
        public enum Scope { Anywhere, Hamlet, Dungeon }

        private class Spoken
        {
            public string Text, Source;
            public Scope Scope;
            /// <summary>A remark on a moment gives way when too many lines wait; the story is always told.</summary>
            public bool Remark;
        }

        // DESIGN CALL (DD1 times a subtitle by its voice clip; there is no clip here): a line of the Ancestor's
        // stays for a reading time and goes by itself, so that it never has to be clicked away in a corridor.
        private const float ReadBase = 2.5f, ReadPerCharacter = 0.055f, ReadMin = 4f, ReadMax = 20f;
        private const float Pause = 0.6f;           // between two lines
        private const int MaxWaiting = 5;           // more than this and the oldest remark is not made

        private static readonly Dictionary<string, int> Campaign = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> TownVisit = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> Raid = new Dictionary<string, int>();
        private static int _visitWeek;              // the week whose town visit has been greeted
        private static long _raidSeed;              // the expedition whose beginning has been spoken
        private static string _victorySpoken;       // story quest whose closing words were said in the dungeon

        private static readonly List<Spoken> Waiting = new List<Spoken>();
        private static Spoken _showing;
        private static float _hideAt, _nextAt, _nextSearch, _nextComplaint;
        private static readonly System.Random Random = new System.Random();

        // The band itself belongs to NarrationBox; this class only decides which canvas it stands on.
        private static NarrationBox _box;
        private static Transform _hud;

        private static HashSet<uint> _roster;
        private static int _graves;
        private static bool _mapOpen, _provisionsOpen;

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, Save, Load, Reset);
            EstateState.WeekAdvanced += week => TownVisit.Clear();
            EstateState.ExpeditionEnded += OnExpeditionEnded;
            NarrationMoments.HamletOpened += OnHamletOpened;
            NarrationMoments.ExpeditionSeen += OnExpeditionSeen;
            NarrationMoments.ObjectiveCompleted += OnObjectiveCompleted;
            NarrationMoments.HeroFell += fallen => Trigger("kill_hero", Scope.Anywhere);
            NarrationMoments.Tick += OnTick;
            NarrationMoments.SessionEnded += OnSessionEnded;
            HamletScene.BuildingClicked += id => Trigger("enter_building", Scope.Hamlet, id);
            UpgradeRules.Built += tree => Trigger("upgrade_building", Scope.Hamlet, tree.Building);
            RosterLifecycle.Changed += OnRosterChanged;
        }

        // ---- DD1's rule ----------------------------------------------------------------------------------

        // Lines DD1 has said before its hamlet is ever seen, on the Old Road (a walk the estate does not have):
        // "Brigands have the run of these lanes, keep to the side path, the Hamlet is just ahead." carries no
        // tag and is allowed once a campaign, which DD1 spends in that first hallway.
        private static readonly HashSet<string> SaidBeforeTheEstate = new HashSet<string> { "/vo/tutorial/first_dungeon" };

        /// <summary>
        /// A moment of the game, by DD1's trigger id and tags. Returns the line that will be spoken, or null
        /// when the Ancestor keeps silent (no line fits, the dice, the limits).
        /// </summary>
        public static string Trigger(string triggerId, Scope scope, params string[] tags)
        {
            try { return Answer(triggerId, scope, tags, false); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Narration: " + triggerId + " failed: " + e);
                return null;
            }
        }

        /// <summary>As <see cref="Trigger"/>; <paramref name="force"/> skips the dice and the limits (dev bridge).</summary>
        public static string Answer(string triggerId, Scope scope, ICollection<string> tags, bool force)
        {
            if (!EstateSession.Active) return null;
            var trigger = NarrationRules.Find(triggerId);
            if (trigger == null) return null;
            if (!force && Random.NextDouble() >= trigger.Chance) return null;

            // Lines that fit, have words in the player's DD1 (a silent entry needs none) and are not used up.
            var fitting = new List<NarrationRules.Line>();
            foreach (var line in trigger.Lines)
            {
                if (!line.Fits(tags)) continue;
                if (!force && SaidBeforeTheEstate.Contains(line.Path)) continue;
                if (!line.Silence && !Dd1Strings.Has(line.TextId + "_0")) continue;
                if (!force && UsedUp(line)) continue;
                if (force && line.Silence) continue;
                fitting.Add(line);
            }
            if (fitting.Count == 0) return null;
            // One roll among the lines of the highest priority, those that name tags before those that name
            // none: a line's chance is its weight among them, and one of them is always picked.
            var top = fitting.Max(l => l.Priority);
            var group = fitting.Where(l => l.Priority == top).ToList();
            if (group.Any(l => l.Tags.Length > 0)) group = group.Where(l => l.Tags.Length > 0).ToList();
            NarrationRules.Line pick = null;
            if (force) pick = group[Random.Next(group.Count)];
            else
            {
                var roll = Random.NextDouble() * group.Sum(l => Math.Max(0f, l.Chance));
                foreach (var line in group)
                {
                    roll -= Math.Max(0f, line.Chance);
                    if (roll >= 0) continue;
                    pick = line;
                    break;
                }
            }
            if (pick == null) return null;
            // DD1's queue_only_on_empty: a remark does not wait behind another line (the hamlet's report is one).
            if (!force && pick.OnlyWhenSilent && (Speaking || NarrationBox.IsOpen)) return null;
            Count(Campaign, pick.Key);
            Count(TownVisit, pick.Key);
            Count(Raid, pick.Key);
            if (pick.Silence) return null;
            var text = Dd1Strings.Parts(pick.TextId);
            if (text == null) return null;
            Enqueue(text, scope, triggerId + " " + pick.Path, true);
            return text;
        }

        /// <summary>How often a line (by its count key: its audio event) has been spoken on the expedition under way.</summary>
        public static int SaidThisRaid(string lineKey) => lineKey != null && Raid.TryGetValue(lineKey, out var said) ? said : 0;

        private static bool UsedUp(NarrationRules.Line line)
        {
            return Over(Campaign, line.Key, line.MaxCampaign) || Over(TownVisit, line.Key, line.MaxTownVisit) || Over(Raid, line.Key, line.MaxRaid);
        }

        private static bool Over(Dictionary<string, int> counts, string path, int most)
        {
            return most > 0 && counts.TryGetValue(path, out var said) && said >= most;
        }

        private static void Count(Dictionary<string, int> counts, string path)
        {
            counts.TryGetValue(path, out var said);
            counts[path] = said + 1;
        }

        // ---- the mod's own words -------------------------------------------------------------------------

        /// <summary>The Ancestor says a text of the mod's own (a story quest's narration); it waits its turn and is never dropped as stale.</summary>
        public static void Say(string text, Scope scope = Scope.Anywhere, string source = "story")
        {
            if (!string.IsNullOrEmpty(text)) Enqueue(text, scope, source, false);
        }

        private static void Enqueue(string text, Scope scope, string source, bool remark)
        {
            // Too many remarks in a row: the oldest that is not story gives way.
            if (Waiting.Count >= MaxWaiting)
            {
                var drop = Waiting.FindIndex(w => w.Remark);
                if (drop < 0) return;
                Waiting.RemoveAt(drop);
            }
            Waiting.Add(new Spoken { Text = text, Scope = scope, Source = source, Remark = remark });
            Plugin.Log.LogInfo("Narration (" + source + "): " + text);
        }

        /// <summary>True while a line of this class is on the screen or waits for its turn.</summary>
        public static bool Speaking => _showing != null || Waiting.Count > 0;

        // ---- moments -------------------------------------------------------------------------------------

        private static void OnHamletOpened()
        {
            // Whatever the band holds now is the hamlet's to show (its report of the expedition): no timer on it.
            _showing = null;
            _roster = new HashSet<uint>(RosterLifecycle.LivingGuids());
            _graves = Graveyard.All.Count;
            var week = EstateState.Current.Week;
            if (_visitWeek == week) return;     // DD1 greets a town visit once; a loaded save is the same visit
            _visitWeek = week;
            var active = TownEvents.Current;
            if (active != null) Trigger("town_visit_start", Scope.Hamlet, active.Id);
            else Trigger("town_visit_start", Scope.Hamlet);
        }

        private static JToken StoryQuest(DungeonRun run)
        {
            // The run does not say which quest it is, but a story quest's name is its title.
            var quest = Memoirs.PlotQuest(run.Title);
            return quest != null && (string)quest["dungeon"] == run.Exploration.Map.DungeonId ? quest : null;
        }

        private static void OnExpeditionSeen(DungeonRun run)
        {
            var map = run.Exploration.Map;
            if (_raidSeed == map.Seed) return;      // restored from a save: this expedition has had its beginning
            _raidSeed = map.Seed;
            Raid.Clear();
            _victorySpoken = null;
            var quest = StoryQuest(run);
            // DD1 reads a boss quest's memoir on the loading screen and adds a line in the first room; the
            // estate's story has one text for both.
            if (quest != null) Say((string)quest["intro_narration"], Scope.Dungeon, "story " + (string)quest["id"]);
            else Trigger("quest_start", Scope.Dungeon, map.DungeonId, TownEvents.Dd1QuestType(map.QuestType));
        }

        private static void OnObjectiveCompleted(DungeonRun run)
        {
            var map = run.Exploration.Map;
            var quest = StoryQuest(run);
            if (quest == null)
            {
                Trigger("quest_end_completed", Scope.Dungeon, map.DungeonId, TownEvents.Dd1QuestType(map.QuestType));
                return;
            }
            // Only the first time: a story quest played again is no longer a chapter (QuestBoard pays no story for it).
            var id = (string)quest["id"];
            if (EstateState.Current.CompletedQuests.Contains(id)) return;
            _victorySpoken = id;
            Say((string)quest["victory_narration"], Scope.Anywhere, "story " + id);
        }

        private static void OnExpeditionEnded(EstateState.Expedition expedition)
        {
            if (!EstateSession.Active) return;
            if (!expedition.Success) Trigger("quest_end_not_completed", Scope.Anywhere);

            // The hamlet reads a finished story quest's closing words out itself (QuestBoard.PendingNarration).
            // They are the words this class says when the goal is reached, so they are said once: if they are
            // still waiting here the hamlet keeps them, if they were shown the hamlet's copy is taken out.
            var spoken = _victorySpoken;
            _victorySpoken = null;
            if (spoken == null || expedition.QuestId != spoken) return;
            var victory = (string)Memoirs.PlotQuest(spoken)?["victory_narration"];
            if (string.IsNullOrEmpty(victory)) return;
            if (Waiting.RemoveAll(w => w.Text == victory) > 0) return;
            var pending = QuestBoard.PendingNarration;
            if (pending == null || !pending.StartsWith(victory, StringComparison.Ordinal)) return;
            var rest = pending.Substring(victory.Length).TrimStart('\n');
            QuestBoard.PendingNarration = rest.Length > 0 ? rest : null;
        }

        // A hero more or a hero less while the hamlet is up: hired at the coach, or sent away.
        private static void OnRosterChanged()
        {
            if (!EstateSession.Active) return;
            var now = new HashSet<uint>(RosterLifecycle.LivingGuids());
            var graves = Graveyard.All.Count;
            var before = _roster;
            var gravesBefore = _graves;
            _roster = now;
            _graves = graves;
            if (before == null || !HamletScreen.IsOpen || TownEventPanel.IsOpen) return;
            foreach (var guid in now)
            {
                if (before.Contains(guid)) continue;
                var actor = SanitariumLedger.Actor(guid);
                if (actor != null) Trigger("recruit_hero", Scope.Hamlet, actor.ActorDataId);
                return;
            }
            // Fewer heroes and no new grave: a dismissal (the dead are taken off the roster too).
            if (now.Count < before.Count && graves == gravesBefore) Trigger("dismiss_hero", Scope.Hamlet);
        }

        // ---- showing -------------------------------------------------------------------------------------

        private static void OnTick()
        {
            try
            {
                Watch();
                Present();
            }
            catch (Exception e)
            {
                if (Time.unscaledTime < _nextComplaint) return;
                _nextComplaint = Time.unscaledTime + 10f;
                Plugin.Log.LogError("Narration failed: " + e);
            }
        }

        // Screens that open without telling anyone.
        private static void Watch()
        {
            var map = QuestPanel.IsOpen;
            if (map && !_mapOpen) Trigger("enter_quest_select", Scope.Hamlet);
            _mapOpen = map;
            var provisions = ProvisionScreen.IsOpen;
            if (provisions && !_provisionsOpen) Trigger("enter_provision_select", Scope.Hamlet);
            _provisionsOpen = provisions;
        }

        // The canvas a line can be read on right now: the hamlet's, or the dungeon's; null during fights and loading.
        private static Transform Stage(out bool dungeon)
        {
            // Which of the two it is comes from the session, not from what is drawn: for a frame after an
            // expedition begins the hamlet is still up, and a line meant for the dungeon must not be judged by it.
            dungeon = EstateSession.View == EstateSession.Screen.Dungeon;
            // under the loading screen neither canvas can be read: a line waits for the place it was meant for
            if (!EstateSession.InHub || LoadingScreen.IsUp) return null;
            // Between the expedition and the hamlet: DD1 speaks a quest's last line over its results screens and
            // writes it where the town's stand (shared/app.darkest: s_RaidResultsSubtitleContextTunables).
            if (RaidResultsScreen.IsOpen) return RaidResultsScreen.Stage;
            if (!dungeon) return HamletScreen.IsOpen ? RosterPanel.CanvasRoot : null;
            if (DungeonRun.Current == null) return null;
            if (_hud == null)
            {
                // The dungeon's canvas, by the name DungeonHud gives it.
                var hud = GameObject.Find("DD2Estate.DungeonHud");
                _hud = hud != null ? hud.transform : null;
            }
            return _hud != null && _hud.gameObject.activeInHierarchy ? _hud : null;
        }

        private static void Present()
        {
            var stage = Stage(out var dungeon);
            KeepBandOnStage(stage, _showing != null);

            if (_showing != null)
            {
                // A spoken line belongs to the voice: no click sends it away while he speaks, and it goes when he
                // has done (DD1 times its subtitles by the clip). A line without a voice keeps its reading time.
                NarrationBox.KeepUntil = EstateAudio.SpokenUntil;
                // Clicked away, or its time is up. On another stage (a fight began) it simply goes.
                if (!NarrationBox.IsOpen) _showing = null;
                else if (stage == null || EstateAudio.LineIsOver(_hideAt))
                {
                    NarrationBox.Hide();
                    _showing = null;
                }
                if (_showing == null) _nextAt = Time.unscaledTime + Pause;
                return;
            }
            if (Waiting.Count == 0 || stage == null) return;
            // A line for the other place has missed its moment (the party set out, or came home).
            Waiting.RemoveAll(w => (w.Scope == Scope.Hamlet && dungeon) || (w.Scope == Scope.Dungeon && !dungeon));
            // One voice at a time: the hamlet's own report (which is clicked away) and the crier's notice come first.
            if (NarrationBox.IsOpen || TownEventPanel.IsOpen || TownEventPanel.Pending || Time.unscaledTime < _nextAt) return;

            if (Waiting.Count > 0)
            {
                var next = Waiting[0];
                Waiting.RemoveAt(0);
                // First onto the canvas that is up (the band may have been left on the other one, which is
                // switched off: it lays its text out when it is shown).
                KeepBandOnStage(stage, true);
                NarrationBox.Show(stage, next.Text);
                _showing = next;
                _hideAt = Time.unscaledTime + Mathf.Clamp(ReadBase + ReadPerCharacter * next.Text.Length, ReadMin, ReadMax);
                // The voice, for a line DD1 has one for (its audio event is the end of the line's source); the mod's own words have none.
                EstateAudio.Speak(next.Source);
            }
        }

        // NarrationBox builds its band once, on the canvas of whoever shows a text first, and keeps it there. The
        // hamlet and the dungeon have a canvas each and only one is up at a time, so the band is moved to the one
        // that is. Its place on the screen stays NarrationBox's: DD1 puts its subtitles at the same spot in town
        // and on an expedition (shared/app.darkest: s_TownSubtitleContextTunables, s_RaidSubtitleContextTunables).
        private static void KeepBandOnStage(Transform stage, bool searchNow)
        {
            if (stage == null) return;
            if (_box == null)
            {
                // The band may be made at any time by whoever speaks first; looking for it costs a scene search.
                if (!searchNow && Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + 1f;
                _box = UnityEngine.Object.FindObjectOfType<NarrationBox>(true);
                if (_box == null) return;
            }
            if (_box.transform.parent != stage) _box.transform.SetParent(stage, false);
        }

        private static void OnSessionEnded()
        {
            Waiting.Clear();
            if (_showing != null) NarrationBox.Hide();
            _showing = null;
            _roster = null;
            _mapOpen = _provisionsOpen = false;
        }

        /// <summary>Dev bridge: silences the Ancestor (the line on screen and those waiting).</summary>
        public static void Hush()
        {
            Waiting.Clear();
            if (_showing != null) NarrationBox.Hide();
            _showing = null;
        }

        /// <summary>Dev bridge: forgets what was said: "raid", "visit", "campaign" or everything.</summary>
        public static void Forget(string what)
        {
            if (what == null || what == "raid") { Raid.Clear(); _raidSeed = 0; }
            if (what == null || what == "visit") { TownVisit.Clear(); _visitWeek = 0; }
            if (what == null || what == "campaign") Campaign.Clear();
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JToken Save()
        {
            return new JObject
            {
                ["campaign"] = JObject.FromObject(Campaign),
                ["visit"] = JObject.FromObject(TownVisit),
                ["raid"] = JObject.FromObject(Raid),
                ["visit_week"] = _visitWeek,
                ["raid_seed"] = _raidSeed.ToString(CultureInfo.InvariantCulture),
                ["victory_spoken"] = _victorySpoken
            };
        }

        private static void Load(JToken token)
        {
            Reset();
            if (!(token is JObject json)) return;
            Read(json["campaign"], Campaign);
            Read(json["visit"], TownVisit);
            Read(json["raid"], Raid);
            _visitWeek = (int?)json["visit_week"] ?? 0;
            long.TryParse((string)json["raid_seed"], NumberStyles.Integer, CultureInfo.InvariantCulture, out _raidSeed);
            _victorySpoken = (string)json["victory_spoken"];
        }

        private static void Read(JToken token, Dictionary<string, int> into)
        {
            if (!(token is JObject counts)) return;
            foreach (var property in counts.Properties()) into[property.Name] = (int?)property.Value ?? 0;
        }

        private static void Reset()
        {
            Campaign.Clear();
            TownVisit.Clear();
            Raid.Clear();
            _visitWeek = 0;
            _raidSeed = 0;
            _victorySpoken = null;
            Waiting.Clear();
            _showing = null;
            _roster = null;
        }

        /// <summary>Snapshot for the log and the test bridge.</summary>
        public static object Describe()
        {
            return new
            {
                showing = _showing?.Text,
                waiting = Waiting.Select(w => new { text = w.Text, scope = w.Scope.ToString(), source = w.Source }).ToList(),
                bandOpen = NarrationBox.IsOpen,
                visitWeek = _visitWeek,
                raidSeed = _raidSeed,
                victorySpoken = _victorySpoken,
                campaign = Campaign,
                townVisit = TownVisit,
                raid = Raid
            };
        }
    }
}
