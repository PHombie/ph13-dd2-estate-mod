using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Code.Boss;
using Assets.Code.Library;
using Assets.Code.Locale;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The Darkest Dungeon for the time being (the owner, 2026-10-06: "let us for now, temporarily, make the
    /// Darkest Dungeon like this: simply 5 dungeons, opening one after another as they are completed; one is
    /// available from the start; each dungeon at once starts the battle with the final bosses of a Confession").
    /// It holds the place while [Rules] DarkestDungeon is "bosses" (<see cref="DarkestDungeon.Mode"/>), in the
    /// stead of DD1's descents and of the padlock.
    ///
    /// THE FIVE are DD2's Confessions, each with its final boss, named and ordered by the game itself:
    ///   which and in what order   the game's boss rows (Library of BossDefinition: "brain", "lungs", "eyes",
    ///                             "arms", "body"), each naming the one to be won before it
    ///                             (m_PrerequisiteBossVictoryIds); <see cref="DarkestBossRules.Order"/>;
    ///   their names               the game's own words: the Confession's (boss_choice_&lt;id&gt;_label: "I. Denial")
    ///                             is the quest's name, the boss's (boss_&lt;id&gt;_core: "Shackles of Denial") is in
    ///                             its goal, which is DD1's sentence for a kill ("Kill 1 ...");
    ///   their pictures            the game's (ResourceBoss.m_BlessingSprite of its boss database, the picture it
    ///                             shows beside a Confession's flames), where it can be had;
    ///   the fight                 Data/dungeons.json has the five as the Darkest Dungeon's bosses
    ///                             (<c>run_boss</c>): the game's own battle and its own arena, and the act the
    ///                             boss is told it is fought in (Dd2/Difficulty.cs);
    ///   what the map says of it   the mod's own lines for these five bosses (Data/plot_quests.json);
    ///   the pay                   DD1's own, of the Darkest Dungeon quest the boss stands for in the mod's story
    ///                             (quest.plot_quests.json: 15,000 gold, 18 crests, a very rare trinket, 16
    ///                             resolve experience: DD1 pays its four alike).
    ///
    /// THE RULES (Core/DarkestBossRules.cs, tested): the first is on offer from the first week; a won one is won
    /// for good and the next is on offer from the week after; a quest lost or abandoned is there again next
    /// week; after the fifth the place has nothing more (the campaign's ending is not built). The progress is
    /// the estate's own section of the save ("darkest_bosses"); an estate saved before it existed starts at the
    /// first.
    ///
    /// THE QUEST ITSELF is set out on like any other (a party chosen on the Estate Map, the provision screen: a
    /// fight's bag takes supplies, torches and food: Dungeon/FightBag.cs) and is nothing but its fight
    /// (Dungeon/DungeonRunBoss.cs): the loading screen, the fight, DD1's results. No room is ever shown.
    ///
    /// What it is NOT: one of DD1's descents. None of DD1's rules of those (Estate/DarkestDungeon.cs: Never
    /// Again, the veteran's lesson, the failed descent's) is asked of the five, nor a descent's own (no way
    /// back, a hero's life for a retreat, the stress lifted off the estate). [Rules] DarkestDungeon = dd1 gives
    /// all of that back, with the mod's five descents.
    ///
    /// Bridge: bosses.state, bosses.win, bosses.reset, bosses.embark, bosses.fight, bosses.round, bosses.lose.
    /// </summary>
    [EstateModule]
    internal static class DarkestBosses
    {
        private const string SectionKey = "darkest_bosses";

        /// <summary>One of the five.</summary>
        internal sealed class Confession
        {
            /// <summary>DD2's id of the Confession's boss row ("brain").</summary>
            public string Act;
            /// <summary>The boss's id in Data/dungeons.json ("shackles_of_denial").</summary>
            public string Boss;
            /// <summary>1 to 5, in the game's order.</summary>
            public int Number;

            public string QuestId => DarkestBossRules.QuestId(Act);
        }

        /// <summary>One place of the row on the Estate Map.</summary>
        internal sealed class RowPlace
        {
            public Confession Confession;
            public DarkestBossPlace Place;
        }

        private static readonly DarkestBossProgress Progress = new DarkestBossProgress();
        private static List<Confession> _chain;
        private static string _orderFrom = "";
        private static ResourceDatabaseBoss _pictures;
        private static readonly Dictionary<string, Sprite> Pictures = new Dictionary<string, Sprite>();
        private static string _last = "";

        private static void Register()
        {
            EstateState.RegisterSection(SectionKey, () => Progress.ToJson(), Progress.FromJson, Progress.Clear);
            Bridge();
        }

        /// <summary>The five hold the Darkest Dungeon ([Rules] DarkestDungeon = bosses).</summary>
        public static bool Holds => DarkestDungeon.Mode == DarkestMode.Bosses;

        // ---- the five ------------------------------------------------------------------------------------

        /// <summary>
        /// The five in the game's order. Read once the game's boss rows are there (inside a session); before
        /// that, and should the game not have them, in the order Data/dungeons.json lists them.
        /// </summary>
        public static IReadOnlyList<Confession> Chain
        {
            get
            {
                if (_chain != null) return _chain;
                var data = DungeonContent.ActBosses(QuestBoard.DarkestDungeon);
                var acts = data.Select(pair => pair.Key).ToList();
                var library = BossRows();
                var order = library != null
                    ? DarkestBossRules.Order(acts, act => library.GetHasLibraryKey(act) ? library.GetLibraryElement(act).PrerequisiteBossVictoryIds : new List<string>())
                    : acts;
                var chain = new List<Confession>();
                foreach (var act in order) chain.Add(new Confession { Act = act, Boss = data.First(pair => pair.Key == act).Value, Number = chain.Count + 1 });
                if (library == null) return chain;      // not kept: asked again when the game's rows are there
                _orderFrom = "the game's boss rows (" + string.Join(", ", order.Select(act => library.GetHasLibraryKey(act) ? act : act + "?")) + ")";
                Plugin.Log.LogInfo("Darkest Dungeon: the five Confessions by " + _orderFrom);
                return _chain = chain;
            }
        }

        private static Library<string, BossDefinition> BossRows()
        {
            try
            {
                if (!SingletonMonoBehaviour<Library<string, BossDefinition>>.HasInstance()) return null;
                var library = SingletonMonoBehaviour<Library<string, BossDefinition>>.Instance;
                return library != null && library.GetNumberOfLibraryElements() > 0 ? library : null;
            }
            catch (Exception) { return null; }
        }

        private static List<string> Order => Chain.Select(c => c.Act).ToList();

        public static int Count => Chain.Count;

        /// <summary>How many of the five are won.</summary>
        public static int WonCount => Progress.Count(Order);

        /// <summary>The one on offer; null when all are won.</summary>
        public static Confession Next
        {
            get
            {
                var act = Progress.Next(Order);
                return act == null ? null : Chain.First(c => c.Act == act);
            }
        }

        /// <summary>The id of the quest that is due on the board; null when all are won.</summary>
        public static string DueQuestId => Next?.QuestId;

        /// <summary>One of the five quests (by its id and its place), whichever of them and whatever the setting.</summary>
        public static bool IsOurs(Quest quest) => quest != null && quest.Dungeon == QuestBoard.DarkestDungeon && DarkestBossRules.ActOf(quest.Id) != null;

        private static Confession Of(Quest quest)
        {
            var act = IsOurs(quest) ? DarkestBossRules.ActOf(quest.Id) : null;
            return act == null ? null : Chain.FirstOrDefault(c => c.Act == act);
        }

        /// <summary>The row of the Estate Map: the five in order, each won, on offer or yet to come.</summary>
        public static List<RowPlace> Row()
        {
            var order = Order;
            return Chain.Select(c => new RowPlace { Confession = c, Place = Progress.PlaceOf(order, c.Act) }).ToList();
        }

        // ---- the game's words and pictures -----------------------------------------------------------------

        private static string GameText(string key)
        {
            try
            {
                var text = Singleton<Localization>.Instance.TryGetString(key);
                return string.IsNullOrEmpty(text) || text == key ? null : text;
            }
            catch (Exception) { return null; }
        }

        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>The game's name of the Confession, as it lists them at the altar ("I. Denial").</summary>
        public static string Label(Confession c)
        {
            return GameText("boss_choice_" + c.Act + "_label") ?? (c.Number >= 1 && c.Number <= Roman.Length ? Roman[c.Number - 1] : c.Number.ToString()) + ". " + BossName(c);
        }

        /// <summary>The game's name of the Confession's boss ("Shackles of Denial").</summary>
        public static string BossName(Confession c)
        {
            return GameText("boss_" + c.Act + "_core") ?? DungeonContent.BossName(QuestBoard.DarkestDungeon, c.Boss) ?? c.Boss;
        }

        /// <summary>The game's word for a Confession it does not name yet ("???").</summary>
        public static string Unknown => GameText("boss_choice_unknown_label") ?? "???";

        // The game's boss database hangs on its fight HUD, which the mod keeps loaded for a session (Dd2Hud).
        private static ResourceDatabaseBoss Database()
        {
            if (_pictures != null) return _pictures;
            try
            {
                var hud = Dd2Hud.FightHud(out _);
                var ui = hud != null ? hud.GetComponentInChildren<CombatUiBhv>(true) : null;
                if (ui != null && ui.BossDatabase != null) return _pictures = ui.BossDatabase;
                foreach (var loaded in Resources.FindObjectsOfTypeAll<ResourceDatabaseBoss>())
                    if (loaded != null) return _pictures = loaded;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Darkest Dungeon: the game's boss pictures could not be looked for: " + e.Message); }
            return null;
        }

        /// <summary>
        /// The game's picture of the Confession; null while it is not to be had (the game's fight HUD is still
        /// on its way) or for good (<paramref name="pending"/> false).
        /// </summary>
        public static Sprite Picture(Confession c, out bool pending)
        {
            pending = false;
            if (c == null) return null;
            if (Pictures.TryGetValue(c.Act, out var known) && known != null) return known;
            var database = Database();
            if (database == null)
            {
                Dd2Hud.FightHud(out pending);
                return null;
            }
            try
            {
                // (kept by the game's own handle: not released, the picture is the map's for the session)
                var resource = database.GetResource(c.Act, false, false);
                var sprite = resource != null ? resource.m_BlessingSprite != null ? resource.m_BlessingSprite : resource.m_BlessingSprite_Small : null;
                if (sprite != null) Pictures[c.Act] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Darkest Dungeon: the game's picture of " + c.Act + " could not be had: " + e.Message);
                return null;
            }
        }

        // ---- the quest -------------------------------------------------------------------------------------

        private static JToken Story(Confession c) => QuestBoard.StoryOfBoss(QuestBoard.DarkestDungeon, c.Boss);

        /// <summary>The quest that is due, without its pay (the board pays it: QuestBoard.AddBossQuest); null when the five do not hold the place or all are won.</summary>
        public static Quest Offer()
        {
            if (!Holds) return null;
            var next = Next;
            return next == null ? null : Make(next);
        }

        private static Quest Make(Confession c)
        {
            var story = Story(c);
            return new Quest
            {
                Id = c.QuestId, Name = Label(c), Dungeon = QuestBoard.DarkestDungeon, Type = QuestTypes.KillBoss, Boss = c.Boss,
                Tier = DungeonContent.Darkest, LengthName = "short", Plot = true,
                // DD1's sentence of a kill goal with the game's name of the boss: "Kill 1 Shackles of Denial."
                Goal = Dd1Strings.Plain(Dd1Strings.Format(Dd1Strings.Get("town_quest_goal_start_plural_kill_monster") ?? "Kill %d %s.", 1, BossName(c))),
                Intro = (string)story?["intro_narration"], Victory = (string)story?["victory_narration"]
                // (Dd1Quest stays empty: DD1's rules of a descent are not asked of a stand-in)
            };
        }

        /// <summary>The DD1 quest whose pay the stand-in takes: the descent its boss stands for in the mod's story.</summary>
        public static string Dd1QuestOf(Quest quest)
        {
            var c = Of(quest);
            return (c != null ? (string)Story(c)?["dd1_quest_id"] : null) ?? "plot_darkest_dungeon_1";
        }

        /// <summary>The quest has been completed and paid (QuestBoard.Finished): it is won for good.</summary>
        public static void Won(Quest quest)
        {
            var c = Of(quest);
            if (c == null) return;
            var order = Order;
            if (!Progress.Win(order, c.Act))
            {
                if (Progress.IsWon(c.Act)) return;
                // not the one that was due (the board offers no other): the party did win it all the same
                Plugin.Log.LogWarning("Darkest Dungeon: " + quest.Id + " was won out of its turn (due: " + (Progress.Next(order) ?? "none") + ")");
                Progress.Set(c.Act, true);
            }
            var next = Next;
            _last = Label(c) + " won; " + (next != null ? Label(next) + " opens with the new week" : "all " + Count + " are won");
            Plugin.Log.LogInfo("Darkest Dungeon: " + _last);
        }

        /// <summary>Why a quest of the Darkest Dungeon is not what the place holds now; null when it is.</summary>
        public static string BlockReason(Quest quest)
        {
            if (quest == null || quest.Dungeon != QuestBoard.DarkestDungeon) return null;
            const string none = "The Darkest Dungeon holds no such quest now.";
            if (!IsOurs(quest)) return Holds ? none : null;
            if (!Holds) return none;
            return quest.Id == DueQuestId ? null : "That Confession does not wait now.";
        }

        /// <summary>The expedition's map of one of the five: the boss's room and nothing else. Null for any other quest.</summary>
        public static DungeonMap MapFor(Quest quest, int tier, long seed)
        {
            return IsOurs(quest) ? DarkestBossRules.FightMap(quest.Dungeon, tier, seed) : null;
        }

        // ---- what the Estate Map says ------------------------------------------------------------------------

        /// <summary>The words the map itself carries under the row once every one of the five is won; null before that.</summary>
        public static string DoneLine => Count > 0 && Next == null ? "All " + Count + " Confessions are answered." : null;

        /// <summary>What the pointer is told on the region's bar while the five hold the place.</summary>
        public static string RegionLine()
        {
            var next = Next;
            if (next == null) return DoneLine + " Nothing more waits down there for now.";
            return QuestMapText.DarkestHint + "\n" + Label(next) + " (" + (WonCount + 1) + " of " + Count + ")";
        }

        /// <summary>What the pointer is told on a place of the row that is not the quest on offer: its heading and its line.</summary>
        public static void PlaceWords(RowPlace place, out string title, out string body)
        {
            var c = place.Confession;
            if (place.Place == DarkestBossPlace.Won)
            {
                title = Label(c);
                body = BossName(c) + "\n" + RaidText.Get("raid_quest_complete", "Quest Complete!");
                return;
            }
            // the game does not name a Confession that is not open yet
            title = Unknown;
            var before = c.Number >= 2 && c.Number - 2 < Chain.Count ? Chain[c.Number - 2] : null;
            var open = before != null && Progress.IsWon(before.Act);
            body = open ? "Opens with the new week." : before != null ? "Opens when " + (Progress.PlaceOf(Order, before.Act) == DarkestBossPlace.Locked ? "the one before it" : Label(before)) + " is won." : "Not yet.";
        }

        // ---- dev bridge --------------------------------------------------------------------------------------

        private static object Describe()
        {
            var order = Order;
            var rows = new List<object>();
            foreach (var c in Chain)
            {
                string config = null, arena = null;
                try { DungeonContent.PickBoss(QuestBoard.DarkestDungeon, c.Boss, DungeonContent.Darkest, out config, out arena); }
                catch (Exception e) { config = "(not known outside a session: " + e.Message + ")"; }
                var picture = Picture(c, out var pending);
                rows.Add(new
                {
                    number = c.Number, act = c.Act, quest = c.QuestId, label = Label(c), boss = c.Boss, bossName = BossName(c), place = Progress.PlaceOf(order, c.Act).ToString(),
                    fight = new { config, arena }, paidAs = (string)Story(c)?["dd1_quest_id"],
                    picture = picture != null ? picture.name + " " + picture.rect.width + "x" + picture.rect.height : pending ? "(on its way)" : null
                });
            }
            var onBoard = EstateSession.Active ? QuestBoard.Current().Where(IsOurs).Select(q => new
            {
                id = q.Id, name = q.Name, goal = q.Goal, tier = q.Tier, difficulty = q.Difficulty, length = q.LengthName, reward = QuestBoard.RewardText(q), resolveXp = q.ResolveXp,
                canRetreat = q.CanRetreat, retreatDeaths = q.RetreatDeaths, clearsStress = q.ClearsStress, index = IndexOnBoard(q.Id)
            }).ToList() : null;
            var run = DungeonRun.Current;
            return new
            {
                mode = DarkestBossRules.NameOf(DarkestDungeon.Mode), holds = Holds, orderFrom = _orderFrom.Length > 0 ? _orderFrom : "Data/dungeons.json (the game's boss rows are not there yet)",
                won = Progress.Won.OrderBy(a => a).ToList(), wonCount = WonCount, of = Count, due = DueQuestId,
                map = EstateSession.Active ? new { open = QuestBoard.IsOpen(QuestBoard.DarkestDungeon), level = QuestBoard.Progress(QuestBoard.DarkestDungeon).Level, share = QuestBoard.Progress(QuestBoard.DarkestDungeon).Share, says = Holds ? RegionLine() : null } : null,
                row = rows, onBoard,
                expedition = run != null && run.IsBossQuest ? run.DescribeBossQuest() : null,
                last = _last
            };
        }

        private static int IndexOnBoard(string questId)
        {
            var offers = QuestBoard.Current();
            for (var i = 0; i < offers.Count; i++)
                if (offers[i].Id == questId) return i;
            return -1;
        }

        /// <summary>
        /// Test commands for the dev bridge:
        ///   bosses.state                    the five (the game's order, names and pictures, each one's fight and place), what the
        ///                                   map says, the quest on the board with its pay, the expedition under way
        ///   bosses.win [act=brain]          the quest on offer (or that Confession's) marked won without playing it: no pay, no week;
        ///                                   the board and an open map follow at once
        ///   bosses.reset [won=brain,lungs]  the progress set to exactly these (none: the first is on offer again)
        ///   bosses.embark [free=true]       sets out on the quest on offer with the party as it stands and the standard kit, past the
        ///                                   provision screen but through the loading screen, as "Set Off" does
        ///   bosses.fight act=eyes           the fight of a Confession at once, outside any quest (as estate.boss, with its act and tier told)
        ///   bosses.round [pass=true]        any fight as it stands: the round, whose turn it is, both sides' health; with pass the hero
        ///                                   whose turn it is passes it, so that rounds go by and the boss is seen to act
        ///   bosses.lose [flee=true] [leave=1]  the fight on screen lost on purpose (the party dies), or ended as not won (a flight)
        /// </summary>
        private static void Bridge()
        {
            AgentBridge.Register("bosses.state", o => Describe());
            AgentBridge.Register("bosses.win", o =>
            {
                if (!EstateSession.Active) return "no estate";
                var act = (string)o["act"] ?? Next?.Act;
                if (act == null || Chain.All(c => c.Act != act)) return "no such Confession (or all are won)";
                Progress.Set(act, true);
                if (QuestPanel.IsOpen) QuestPanel.DevOpen();
                return Describe();
            });
            AgentBridge.Register("bosses.reset", o =>
            {
                if (!EstateSession.Active) return "no estate";
                Progress.Clear();
                foreach (var act in ((string)o["won"] ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) Progress.Set(act, true);
                if (QuestPanel.IsOpen) QuestPanel.DevOpen();
                return Describe();
            });
            AgentBridge.Register("bosses.embark", o =>
            {
                if (!EstateSession.InHub || DungeonRun.Current != null) return "not in the hamlet";
                if (EstateSession.PartySize == 0) return "nobody is in the party";
                var due = DueQuestId;
                var index = due != null ? IndexOnBoard(due) : -1;
                if (index < 0) return "no Confession is on offer";
                var quest = QuestBoard.Current()[index];
                var bag = Provisioning.StandardBag(quest.Length, (bool?)o["free"] ?? true);
                QuestPanel.Close();
                ProvisionScreen.Close();
                // as the provision screen's "Set Off": the expedition is made behind the loading screen
                if (!LoadingScreen.Show(LoadingScreen.Kind.Raid, quest.Dungeon, LoadingScreen.Dd1Quest(quest.Id), () => QuestBoard.Embark(quest, bag))) QuestBoard.Embark(quest, bag);
                return new { quest = quest.Id, name = quest.Name, loading = LoadingScreen.IsUp };
            });
            AgentBridge.Register("bosses.fight", o =>
            {
                if (!EstateSession.InHub) return "not in the Estate's hub";
                var c = Chain.FirstOrDefault(x => x.Act == (string)o["act"]) ?? Next;
                if (c == null) return "no such Confession";
                DungeonContent.PickBoss(QuestBoard.DarkestDungeon, c.Boss, DungeonContent.Darkest, out var config, out var arena);
                if (config == null) return "no fight in the data";
                Difficulty.Apply(QuestBoard.DarkestDungeon, DungeonContent.Darkest, EncounterKind.Boss, c.Boss);
                EstateSession.StartBattle(config, arena);
                return new { act = c.Act, boss = c.Boss, config, arena };
            });
            // a fight as it stands (any fight): the round, whose turn it is, both sides' health; {"pass":true}: the hero
            // whose turn it is passes it (the game's own pass), so that a script can let rounds go by and see the boss act
            AgentBridge.Register("bosses.round", o => FightNow((bool?)o["pass"] ?? false));
            // (There is no command that kills the enemies one by one. One was tried, ActorInstance.Kill on each of them: they
            // are dead and the battle does not know it; it goes on asking the heroes for their turns against nobody. A boss's
            // later shapes are seen by fighting it, tools/auto_fight.py, or not at all: dev.win ends the whole fight at once.)
            // the fight on screen lost on purpose: every living hero of the party dies the game's own death ({"leave":1}: all
            // but one, to see a quest won with the dead); or {"flee":true}: the fight is ended as not won, which the
            // expedition takes for a flight (DD2 itself lets nobody flee an act boss: its battles are "no_retreat")
            AgentBridge.Register("bosses.lose", o =>
            {
                if (!SingletonMonoBehaviour<Assets.Code.Combat.CombatBhv>.HasInstance() || !SingletonMonoBehaviour<Assets.Code.Combat.CombatBhv>.Instance.IsBattleRunning) return "no fight is running";
                if ((bool?)o["flee"] ?? false)
                {
                    SingletonMonoBehaviour<Assets.Code.Combat.CombatBhv>.Instance.ForceEndCombat(isForceComplete: false);
                    return "the fight was ended as not won";
                }
                var living = Provisioning.Party().Where(hero => hero.IsLiving).ToList();
                var killed = new List<string>();
                for (var i = 0; i < living.Count - ((int?)o["leave"] ?? 0); i++)
                {
                    killed.Add(living[i].ActorName);
                    RosterLifecycle.Kill(living[i], Assets.Code.Source.SourceType.DEBUG);
                }
                return new { killed, fight = FightNow(false) };
            });
        }

        private static object FightNow(bool pass)
        {
            if (!SingletonMonoBehaviour<Assets.Code.Combat.CombatBhv>.HasInstance()) return "no fight";
            var combat = SingletonMonoBehaviour<Assets.Code.Combat.CombatBhv>.Instance;
            if (!combat.IsBattleRunning) return "no fight is running";
            var actors = SingletonMonoBehaviour<Library<uint, Assets.Code.Actor.ActorInstance>>.Instance;
            var acting = combat.GetHasCurrentActor() ? combat.GetCurrentActor() : null;
            var passed = false;
            if (pass && acting != null && acting.TeamIndex != Difficulty.EnemyTeam)
            {
                Assets.Code.Combat.Events.EventBattlePass.Trigger(acting.ActorGuid);
                passed = true;
            }
            var heroes = new List<object>();
            var enemies = new List<object>();
            foreach (var actor in actors.GetLibraryElements(a => a != null && a.TeamIndex >= 0))
            {
                var row = new { guid = actor.ActorGuid, cls = actor.ActorDataId, living = actor.IsLiving, hp = Mathf.RoundToInt(actor.HpRaw), hpMax = Mathf.RoundToInt(actor.CurrentHpMax), stress = actor.Stress };
                if (actor.TeamIndex == Difficulty.EnemyTeam) enemies.Add(row);
                else if (EstateSession.IsInParty(actor.ActorGuid)) heroes.Add(row);
            }
            return new
            {
                round = combat.CurrentRound, turn = combat.CurrentTurn, state = combat.CurrentBattleState.ToString(),
                acting = acting != null ? new { guid = acting.ActorGuid, cls = acting.ActorDataId, team = acting.TeamIndex } : null,
                passed, heroes, enemies
            };
        }
    }
}
