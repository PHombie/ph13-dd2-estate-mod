using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Assets.Code.Actor;
using Assets.Code.Actor.Events;
using Assets.Code.Combat.Events;
using Assets.Code.DLC;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Roster;
using Assets.Code.Source;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using HarmonyLib;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Who is on the estate and how that changes: the four heroes a new estate starts with, recruits hired at
    /// the stage coach, dismissal, and death. The heroes are the game's own (an ActorInstance plus an entry in
    /// the Kingdom roster); this class only decides when one is created and when one is gone for good.
    /// The estate holds one hero per class and path (<see cref="HeroPaths"/>): up to four of a class, each on
    /// a path of their own. The game's roster wants one per class; EstateKeepsHeroesOfOneClass lifts that.
    /// </summary>
    internal static class RosterLifecycle
    {
        /// <summary>DD1 starts the estate with a party of four and nobody on the bench.</summary>
        public const int StartingHeroes = RosterManager.FULL_PARTY_SIZE;

        /// <summary>Raised when a hero joined or left the roster (hired, dismissed, buried).</summary>
        public static event Action Changed;

        /// <summary>Raised with the guid of a hero who has left the estate for good (dismissed or buried).</summary>
        public static event Action<uint> Left;

        /// <summary>Raised when a hero has been sent away, with their guid and the resolve level they left at.</summary>
        public static event Action<uint, int> Dismissed;

        // The roster is read through its public surface. Three things have none: creating an entry, taking one
        // out, and the engine's own size limit.
        private static readonly FieldInfo EntriesField = AccessTools.Field(typeof(RosterManager), "m_Entries");
        private static readonly FieldInfo EntryLimitField = AccessTools.Field(typeof(RosterManager), "m_ActiveEntryLimit");
        private static readonly MethodInfo CreateEntryMethod = AccessTools.Method(typeof(RosterManager), "CreateRosterEntry");

        private static readonly Predicate<RosterStatusType> Anyone = status => true;
        private static readonly Predicate<RosterStatusType> Dead = status => status == RosterStatusType.DEAD;
        private static readonly Predicate<RosterStatusType> NotDead = status => status != RosterStatusType.DEAD;

        private static List<string> _available;
        private static bool _burialQueued;
        private static RosterManager _startedFor;

        public static RosterManager Roster => Singleton<GameTypeMgr>.Instance.RosterManager;

        private static Library<uint, ActorInstance> Actors => SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;

        private static RosterEntry Entry(RosterManager roster, uint guid) => roster.GetReadOnlyRosterEntryByActorGuid(guid) as RosterEntry;

        private static bool Remove(RosterManager roster, RosterEntry entry)
        {
            if (entry != null && EntriesField?.GetValue(roster) is List<RosterEntry> entries) return entries.Remove(entry);
            Plugin.Log.LogError("Estate roster: the game's roster list is out of reach; nobody can leave the roster");
            return false;
        }

        // ---- session ---------------------------------------------------------------------------------

        /// <summary>
        /// Once per session, when the game's roster exists (new estate and continue alike). The Kingdom host
        /// caps the roster at its difficulty's value and trims idle heroes above it whenever a party is
        /// confirmed; the estate's cap is the stage coach's, so the engine's own is switched off.
        /// </summary>
        public static void OnSessionStart()
        {
            _available = null;
            var roster = Roster;
            if (roster == null || EntryLimitField == null) return;
            EntryLimitField.SetValue(roster, 0);
        }

        // ---- classes ---------------------------------------------------------------------------------

        /// <summary>
        /// Hero classes this install can field, by the game's own test for filling a roster: a roster class,
        /// its DLC owned, unlocked on the profile, startable in this game type.
        /// </summary>
        public static IReadOnlyList<string> AvailableClasses()
        {
            if (_available != null) return _available;
            var result = new List<string>();
            var type = Singleton<GameTypeMgr>.Instance.CurrentGameType;
            if (type == null || !SingletonMonoBehaviour<Library<string, ActorDataClass>>.HasInstance()) return result;
            var unowned = DLCManager.Instance != null ? DLCManager.Instance.GetUnownedDLCNumbers() : new List<int>();
            foreach (var cls in SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance.GetLibraryElements())
            {
                if (cls == null || !cls.IsPopulateInRoster || unowned.Contains(cls.DLCNumber)) continue;
                if (!cls.GetIsUnlocked() || Array.IndexOf(type.m_ValidStartingRosterStatusTypes, cls.m_StartingRosterStatusType) < 0) continue;
                result.Add(cls.Id);
            }
            result.Sort(StringComparer.Ordinal);
            if (result.Count > 0) _available = result;
            return result;
        }

        private static bool IsBaseClass(string classId)
        {
            var cls = SingletonMonoBehaviour<Library<string, ActorDataClass>>.Instance.GetLibraryElement(classId);
            return cls != null && cls.DLCNumber == 0;
        }

        /// <summary>Classes that have a hero on the estate (the fallen who are not buried yet do not count).</summary>
        public static HashSet<string> ClassesOnRoster()
        {
            var classes = new HashSet<string>();
            var roster = Roster;
            if (roster == null) return classes;
            foreach (var guid in roster.GetActorGuids(NotDead)) classes.Add(roster.GetReadOnlyRosterEntryByActorGuid(guid).ActorClassId);
            return classes;
        }

        /// <summary>The class-and-path seats taken by the living (<see cref="HeroPaths.Key(string, string)"/>).</summary>
        public static HashSet<string> SeatsTaken()
        {
            var seats = new HashSet<string>();
            var actors = Actors;
            foreach (var guid in LivingGuids())
            {
                var actor = actors.GetLibraryElement(guid);
                if (actor != null) seats.Add(HeroPaths.Key(actor));
            }
            return seats;
        }

        /// <summary>
        /// How many heroes the estate could hold if the barracks had no end: every class on every path the
        /// coach brings (those here on other paths keep their place).
        /// </summary>
        public static int SeatsInAll()
        {
            var seats = new HashSet<string>(SeatsTaken());
            foreach (var classId in AvailableClasses())
                foreach (var path in HeroPaths.Open(classId)) seats.Add(HeroPaths.Key(classId, path));
            return seats.Count;
        }

        /// <summary>The heroes of the estate, in the order they joined.</summary>
        public static List<uint> LivingGuids()
        {
            var guids = new List<uint>();
            var roster = Roster;
            if (roster == null) return guids;
            var actors = Actors;
            foreach (var guid in roster.GetActorGuids(NotDead))
                if (actors.GetHasLibraryKey(guid)) guids.Add(guid);
            return guids;
        }

        public static int Count => LivingGuids().Count;

        // ---- joining ---------------------------------------------------------------------------------

        /// <summary>
        /// New estate: Crusader and Highwayman (Man-at-Arms and Highwayman without the Crusader's DLC) plus two
        /// base-game classes picked at random, all four in the party. Everyone else arrives by stage coach.
        /// Safe to call more than once for the same playthrough: the session calls it, and so does the new-game
        /// reset of the estate state in case the session does not.
        /// </summary>
        public static void CreateStartingRoster()
        {
            var roster = Roster;
            if (roster == null)
            {
                Plugin.Log.LogError("Estate roster: the game has no roster yet");
                return;
            }
            if (ReferenceEquals(_startedFor, roster)) return;
            _startedFor = roster;
            OnSessionStart();

            var present = roster.GetActorGuids(Anyone).Count;
            if (present > 0)
            {
                // The game filled the roster itself (the patch that stops it did not apply): play with that.
                Plugin.Log.LogWarning("Estate roster: the game created " + present + " heroes by itself; starting with those");
                foreach (var guid in LivingGuids())
                {
                    if (roster.GetStatusCount(RosterStatusType.PARTY) >= RosterManager.FULL_PARTY_SIZE) break;
                    Entry(roster, guid).SetRosterStatus(RosterStatusType.PARTY, 0u);
                }
                return;
            }

            var available = AvailableClasses();
            var picks = new List<string>();
            void Take(string id)
            {
                if (picks.Count < StartingHeroes && Contains(available, id) && !picks.Contains(id)) picks.Add(id);
            }

            Take(Contains(available, "crusader") ? "crusader" : "man_at_arms");
            Take("highwayman");

            var random = new Random();
            foreach (var baseOnly in new[] { true, false })
            {
                var pool = new List<string>();
                foreach (var id in available)
                    if (!picks.Contains(id) && (!baseOnly || IsBaseClass(id))) pool.Add(id);
                while (picks.Count < StartingHeroes && pool.Count > 0)
                {
                    var i = random.Next(pool.Count);
                    picks.Add(pool[i]);
                    pool.RemoveAt(i);
                }
            }

            foreach (var id in picks) CreateHero(id, null, party: true);
            Plugin.Log.LogInfo("Estate roster: starts with " + string.Join(", ", picks) + " (" + available.Count + " classes can be recruited in this install)");
        }

        /// <summary>
        /// Creates a hero: the same private call the game uses to fill a roster, so the hero gets skills and
        /// starting quirks the native way, then the path they came with (<paramref name="pathId"/>; null for
        /// the class's own). A <paramref name="name"/> replaces the class's canonical one. Nobody is created
        /// when the estate already has a hero of that class on that path. Returns the hero's guid, or 0.
        /// </summary>
        public static uint CreateHero(string classId, string name, bool party, string pathId = null)
        {
            var roster = Roster;
            if (roster == null || CreateEntryMethod == null) return 0u;
            BuryDead();
            var path = pathId ?? HeroPaths.Default(classId);
            if (SeatsTaken().Contains(HeroPaths.Key(classId, path)))
            {
                Plugin.Log.LogWarning("Estate roster: there already is a " + classId + " on the path " + path);
                return 0u;
            }
            // Same lookup as RosterManager.AddMissingHeroesToRoster, including its odd third argument.
            var resource = Singleton<ResourceDatabaseActors>.Instance.GetResource(classId, true, false);
            if (resource == null)
            {
                Plugin.Log.LogWarning("Estate roster: no hero class " + classId);
                return 0u;
            }
            RosterEntry entry;
            try
            {
                entry = (RosterEntry)CreateEntryMethod.Invoke(roster, new object[] { resource, null });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate roster: the game could not create a " + classId + ": " + (e.InnerException ?? e));
                return 0u;
            }
            var actor = Actors.GetLibraryElement(entry.ActorGuid);
            if (actor != null && !string.IsNullOrEmpty(name)) actor.SetActorName(name);
            if (actor != null && HeroPaths.Of(actor) != path && !HeroPaths.Set(actor, path))
                Plugin.Log.LogWarning("Estate roster: " + classId + " has no path " + path + "; the hero keeps " + HeroPaths.Of(actor));
            if (party && roster.GetStatusCount(RosterStatusType.PARTY) < RosterManager.FULL_PARTY_SIZE) entry.SetRosterStatus(RosterStatusType.PARTY, 0u);
            StageCoach.MarkSeen(classId);
            Plugin.Log.LogInfo("Estate roster: " + (actor != null ? actor.ActorName : "?") + " the " + classId + " (" + HeroPaths.Of(actor) + ") joins (#" + entry.ActorGuid + ")");
            RaiseChanged();
            return entry.ActorGuid;
        }

        // ---- dismissal -------------------------------------------------------------------------------

        /// <summary>Why this hero cannot be sent away right now; null when they can.</summary>
        public static string DismissBlockReason(uint guid)
        {
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            var roster = Roster;
            var entry = roster != null ? Entry(roster, guid) : null;
            if (entry == null || entry.GetRosterStatus() == RosterStatusType.DEAD) return "Not on the roster";
            if (roster.GetIsActorInParty(guid)) return "In the party";
            var busy = EstateSession.PartyBlockReason(guid);
            if (busy != null) return busy;
            // DD1: the last hero cannot be dismissed (an estate with nobody could not embark again).
            if (Count <= 1) return LastHeroStays;
            return null;
        }

        /// <summary>The reason <see cref="DismissBlockReason"/> gives for the estate's last hero: DD1 says so in a dialog of its own.</summary>
        public const string LastHeroStays = "The last hero stays";

        /// <summary>
        /// The roster's dismiss control: DD1's questions in DD1's dialog. "This will dismiss the hero
        /// permanently..." (str_hero_dismiss_confirm), or its warning when the estate would be left with fewer
        /// than four heroes (str_hero_dismiss_warning_confirm), each with "Yes" and "No"; the last hero gets
        /// DD1's refusal with "OK" (str_hero_cant_dismiss_confirm). GUESS: the warning's test. DD1 speaks of
        /// "4 compatible heroes" and its files do not say how it counts them: here, fewer than four living
        /// heroes after the dismissal.
        /// </summary>
        public static void RequestDismiss(uint guid)
        {
            var blocked = DismissBlockReason(guid);
            if (blocked == LastHeroStays)
            {
                TownConfirm.Tell(WindowText.Plain("str_hero_cant_dismiss_confirm") ?? "You need at least 1 hero to venture on. This one cannot be dismissed just yet.",
                    WindowText.Plain("str_hero_cant_dismiss_confirm_ok") ?? "OK");
                return;
            }
            if (blocked != null || Actors.GetLibraryElement(guid) == null) return;
            var question = Count - 1 < RosterManager.FULL_PARTY_SIZE
                ? WindowText.Plain("str_hero_dismiss_warning_confirm") ?? "You will no longer have 4 compatible heroes to venture on with if you dismiss this hero. Are you sure you want to dismiss this hero?"
                : WindowText.Plain("str_hero_dismiss_confirm") ?? "This will dismiss the hero permanently. Are you sure you want to dismiss this hero?";
            TownConfirm.Ask(question, WindowText.Plain("str_hero_dismiss_confirm_yes") ?? "Yes", WindowText.Plain("str_hero_dismiss_confirm_no") ?? "No", () => Dismiss(guid));
        }

        /// <summary>Removes a benched hero from the roster for good; their class and path can be recruited again.</summary>
        public static bool Dismiss(uint guid)
        {
            var reason = DismissBlockReason(guid);
            if (reason != null)
            {
                Plugin.Log.LogInfo("Estate roster: cannot dismiss #" + guid + ": " + reason);
                return false;
            }
            var roster = Roster;
            var entry = Entry(roster, guid);
            var actor = Actors.GetLibraryElement(guid);
            var who = actor != null ? actor.ActorName + " the " + entry.ActorClassId : entry.ActorClassId;
            // read before the hero's record goes with them (Resolve forgets a hero who has left)
            var level = Resolve.Level(guid);
            if (!Remove(roster, entry)) return false;
            if (actor != null)
            {
                // What the hero wore goes back to the estate's stores. A combat item, should the hero carry one, is
                // DD2's own: it goes the way a hired hero hands it back (OnHireEnd), into DD2's inventory, where
                // the hub's sweep finds it (Dd2Sweep).
                var back = RealmInventory.HandBack(actor);
                if (back > 0) Plugin.Log.LogInfo("Estate roster: " + back + " trinket(s) of " + who + " go back to the estate's stores");
                var transit = Singleton<GameTypeMgr>.Instance.PlayerInventory;
                if (transit != null && !transit.TryTakeAllFrom(actor.GetCombatSkillInventory()))
                    Plugin.Log.LogWarning("Estate roster: DD2's inventory could not take the combat items " + who + " carried");
                // Without its roster entry the game would drop the actor at the next mode change anyway, but a new
                // hero of the same class hired before that would make the old one count as "in the roster" again.
                Actors.RemoveLibraryElement(actor);
            }
            Plugin.Log.LogInfo("Estate roster: " + who + " was dismissed (#" + guid + ")");
            RaiseLeft(guid);
            try { Dismissed?.Invoke(guid, level); }
            catch (Exception e) { Plugin.Log.LogError("Estate roster: a Dismissed handler failed: " + e); }
            RaiseChanged();
            EstatePersistence.SaveNow("dismissal");
            return true;
        }

        /// <summary>Dev: takes a hero made for a test out of the roster again, wherever the estate is; nothing is saved.</summary>
        public static bool RemoveForTest(uint guid)
        {
            var roster = Roster;
            var entry = roster != null ? Entry(roster, guid) : null;
            if (entry == null || !Remove(roster, entry)) return false;
            var actor = Actors.GetLibraryElement(guid);
            if (actor != null) Actors.RemoveLibraryElement(actor);
            RaiseChanged();
            return true;
        }

        // ---- death -----------------------------------------------------------------------------------

        /// <summary>
        /// From the patch on the roster's own death handler, before it marks the entry dead: the moment a hero
        /// of the estate dies. The actor still knows its name here; a mode change later it no longer exists.
        /// </summary>
        public static void OnActorDeath(RosterManager roster, EventActorDeath death)
        {
            try
            {
                if (!EstateSession.Active || roster == null || death == null) return;
                var entry = roster.GetReadOnlyRosterEntryByActorGuid(death.m_DyingActorGuid);
                // A dead entry dying again is the hero's corpse leaving the battle line.
                if (entry == null || entry.GetRosterStatus() == RosterStatusType.DEAD) return;

                var actors = Actors;
                var actor = actors.GetLibraryElement(death.m_DyingActorGuid);
                var killerGuid = death.m_KillingActorGuids != null && death.m_KillingActorGuids.Count > 0 ? death.m_KillingActorGuids[0] : 0u;
                var killer = killerGuid != 0u && killerGuid != death.m_DyingActorGuid ? actors.GetLibraryElement(killerGuid) : null;
                Dd1Death(death, killer, out var cause, out var source);
                Graveyard.Add(new Graveyard.Fallen
                {
                    Cause = cause,
                    Source = source,
                    Name = actor != null && !string.IsNullOrEmpty(actor.ActorName) ? actor.ActorName : HeroNames.ClassName(entry.ActorClassId),
                    ClassId = entry.ActorClassId,
                    Week = EstateState.Current.Week,
                    Where = Graveyard.CurrentPlace(),
                    // the game colours some monsters' names (rich text): a gravestone is plain
                    By = killer != null ? System.Text.RegularExpressions.Regex.Replace(HeroNames.ClassName(killer.ActorDataId), "<[^>]*>", "") : null,
                    Level = Resolve.Level(death.m_DyingActorGuid)
                });
                QueueBurial();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate roster: recording a death failed: " + e);
            }
        }

        /// <summary>
        /// DD1's two ids of a death (localization: str_death_&lt;cause&gt;_&lt;source&gt;, the Graveyard's line under a
        /// name) from what DD2's own event tells of it:
        ///
        ///   the expedition's own perils (they hurt with SourceType.STORY and name themselves, Dungeon/DungeonRun):
        ///     "hunger"                         hunger_hunger      "perished of hunger."
        ///     one of DD1's obstacles           obstacle_obstacle  "collapsed permanently while clearing an obstacle."
        ///     a curio's bleed / blight         bleed_unknown, poisoned_unknown
        ///     anything else with a name        trap_trap          "was skewered by a trap." (the corridor's traps)
        ///     nothing named                    ddexit_unknown     the one left behind on a retreat from the Darkest Dungeon
        ///   a fight:
        ///     a damage over time               bleed_*, poisoned_* by its id (DD2's burn has no DD1 wording: a blow)
        ///     stress                           heart_attack       "had a heart attack due to unsustainable stress."
        ///     a blow                           attack_monster, or attack_hero when one of the party dealt it
        ///
        /// Both stay null for a death the game tells nothing of (the dev bridge's): the grave then reads as it
        /// did before, a blow when the killer is known and "an unknown peril" otherwise.
        /// </summary>
        private static void Dd1Death(EventActorDeath death, ActorInstance killer, out string cause, out string source)
        {
            cause = source = null;
            var type = death.m_SourceType;
            var id = "";
            if (death.m_SourceIds != null)
                foreach (var one in death.m_SourceIds)
                    if (!string.IsNullOrEmpty(one))
                    {
                        id = one.ToLowerInvariant();
                        break;
                    }
            var from = killer == null ? "unknown" : killer.TeamIndex == death.m_DyingActorTeamIndex ? "hero" : "monster";

            if (type == SourceType.DEBUG) return;
            if (type == SourceType.STORY)
            {
                if (id.Length == 0)
                {
                    cause = "ddexit";
                    source = "unknown";
                }
                else if (id == "hunger") cause = source = "hunger";
                else if (id.StartsWith("bleed", StringComparison.Ordinal))
                {
                    cause = "bleed";
                    source = "unknown";
                }
                else if (id.StartsWith("blight", StringComparison.Ordinal) || id.StartsWith("poison", StringComparison.Ordinal))
                {
                    cause = "poisoned";
                    source = "unknown";
                }
                else if (IsObstacle(id)) cause = source = "obstacle";
                else cause = source = "trap";
                return;
            }
            if (type == SourceType.STRESS || type == SourceType.OVERSTRESS)
            {
                cause = "heart_attack";
                return;
            }
            if (type == SourceType.DOT && id.Contains("bleed"))
            {
                cause = "bleed";
                source = from;
                return;
            }
            if (type == SourceType.DOT && (id.Contains("blight") || id.Contains("poison")))
            {
                cause = "poisoned";
                source = from;
                return;
            }
            if (killer == null) return;
            cause = "attack";
            source = from;
        }

        // DD1's obstacles by name (props/obstacle_definitions.json: rubble, thorny_thicket, shipwreck, ...).
        private static bool IsObstacle(string id)
        {
            if (_obstacles == null)
            {
                _obstacles = new HashSet<string> { "rubble", "thorny_thicket", "shipwreck", "ancestor", "town_rubble" };     // FALLBACK: DD1's own five
                try
                {
                    var text = DD2Estate.Dd1.Dd1Install.Found ? DD2Estate.Dd1.Dd1Install.ReadText("props/obstacle_definitions.json") : null;
                    if (text != null)
                        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, "\"name\"\\s*:\\s*\"([^\"]+)\""))
                            _obstacles.Add(match.Groups[1].Value.ToLowerInvariant());
                }
                catch (Exception e) { Plugin.Log.LogWarning("Estate roster: DD1's obstacles could not be read: " + e.Message); }
            }
            return _obstacles.Contains(id) || id.Contains("obstacle");
        }

        private static HashSet<string> _obstacles;

        // A death outside a fight (a trap, a curio) has no mode change to bury it at.
        private static void QueueBurial()
        {
            if (_burialQueued || Plugin.Host == null) return;
            _burialQueued = true;
            Plugin.Host.StartCoroutine(BuryWhenInHub());
        }

        private static IEnumerator BuryWhenInHub()
        {
            yield return null;
            _burialQueued = false;
            if (EstateSession.InHub) BuryDead();
        }

        /// <summary>
        /// Takes the fallen off the roster. The Kingdom host keeps a dead hero as a roster entry with status
        /// DEAD for ever (its actor is dropped at the next mode change), which would also keep the class from
        /// being recruited again. Returns how many were removed.
        /// </summary>
        public static int BuryDead()
        {
            try
            {
                if (!EstateSession.Active) return 0;
                var roster = Roster;
                if (roster == null) return 0;
                var buried = 0;
                foreach (var guid in roster.GetActorGuids(Dead))
                {
                    // Only the entry: the corpse actor is the game's to drop (it does at every mode change), and
                    // after a death outside a fight the dungeon view may still be showing it.
                    var entry = Entry(roster, guid);
                    if (!Remove(roster, entry)) break;
                    buried++;
                    RaiseLeft(guid);
                    Plugin.Log.LogInfo("Estate roster: the " + entry.ActorClassId + " (#" + guid + ") is buried; their place can be filled again");
                }
                if (buried > 0) RaiseChanged();
                return buried;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate roster: burying the dead failed: " + e);
                return 0;
            }
        }

        /// <summary>
        /// From the patch on the Kingdom's battle-result handler. Kingdoms answers a wiped party there (game
        /// over, or back to the inn); for the Estate that handler is switched off, and the combat presentation
        /// does nothing by itself when nobody is left, so the way home is set here.
        /// </summary>
        public static void OnBattleResult(EventBattleResult result)
        {
            try
            {
                if (!EstateSession.Active) return;
                var roster = Roster;
                if (roster == null || !roster.GetIsPartyDead()) return;
                // Somebody else is already taking the game home.
                var modes = Singleton<GameModeMgr>.Instance;
                if (modes.IsChangingState() && modes.GetNextMode() == EstateMode.Hub) return;
                Plugin.Log.LogInfo("Estate: the party is lost; returning to the estate");
                Singleton<GameTypeMgr>.Instance.LootManager.ClearShowWindowVariables();
                modes.SetMode(EstateMode.Hub, isLoad: false);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Estate: handling the lost party failed: " + e);
            }
        }

        // ---- testing ---------------------------------------------------------------------------------

        /// <summary>
        /// Kills a hero the way a killing effect does (ActorInstance.Kill) when nobody else did it: the hero is
        /// named as their own killer, which is what the game passes for a death without one. Not guid 0 (the
        /// game's narration looks every killer up and an unknown one throws) and not an empty list (the roster
        /// and the tokens take the first killer): an exception in one listener ends the death for all later
        /// ones, and the hero is left neither alive nor dead.
        /// </summary>
        public static void Kill(ActorInstance actor, SourceType source)
        {
            actor.Kill(DeathType.SKILL, source, (string)null, 0f, actor.ActorGuid);
        }

        /// <summary>Dev bridge: <see cref="Kill"/> on a hero by guid.</summary>
        public static string KillForTest(uint guid)
        {
            var actor = Actors.GetLibraryElement(guid);
            if (actor == null) return "no actor #" + guid;
            if (!actor.IsLiving) return actor.ActorName + " is already dead";
            var name = actor.ActorName;
            Kill(actor, SourceType.DEBUG);
            return name + " (#" + guid + ") was killed";
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }

        private static void RaiseLeft(uint guid)
        {
            try { Left?.Invoke(guid); }
            catch (Exception e) { Plugin.Log.LogError("Estate roster: a Left handler failed: " + e); }
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogError("Estate roster: a Changed handler failed: " + e); }
        }
    }
}
