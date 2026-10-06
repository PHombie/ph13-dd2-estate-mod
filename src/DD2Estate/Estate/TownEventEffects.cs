using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Roster;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What a town event does to the estate, for the effects this class can carry out by itself:
    ///   on arrival   recruits on the stage coach, free upgrades handed out, the fallen who may return chosen;
    ///   all week     the Nomad Wagon's prices (it keeps a discount for exactly this);
    ///   on the quest the party's buffs (DD1's "on next quest": damage in a dungeon, the chance to hold fast under
    ///                stress, resolve experience);
    ///   at week end  what the heroes who stayed at home gain (a resolve level, stress relief).
    /// Prices, closed activities, provisions and level restrictions belong to other systems, which ask
    /// <see cref="TownEventHooks"/>. All numbers are DD1's (the event files, shared/buffs/base.buffs.json,
    /// campaign/roster/roster.variables.json); how a DD1 buff lands on a DD2 hero is the mod's remapping and is
    /// said where it happens.
    /// </summary>
    internal static partial class TownEvents
    {
        private const string StatsId = "estate_town_event";
        /// <summary>DD2's id of the good outcome of a stress test (Excel/overstress_data_export: "resolute", 0.2 against "meltdown" 0.8).</summary>
        private const string Resolute = "resolute";

        // ---- on arrival ----------------------------------------------------------------------------------

        private static void Arrive(Active active)
        {
            var def = active.Def;
            if (def == null) return;
            foreach (var effect in def.Effects)
            {
                var amount = (int)Math.Round(effect.Number);
                switch (effect.Type)
                {
                    case "bonus_recruit":
                    {
                        if (!ClassExists(effect.Text)) break;
                        // DESIGN CALL: the event's recruits may walk any path of their class, also one the Hero
                        // Paths tree has not opened yet. With one hero per class and path, DD1's "two of a class"
                        // would otherwise seldom find two places.
                        var came = AddRecruits(effect.Text, amount, true);
                        var name = HeroNames.ClassName(effect.Text);
                        active.Notes.Add(came > 0
                            ? came + " " + name + (came == 1 ? " recruit waits" : " recruits wait") + " at the Stage Coach"
                            : "No " + name + " found a place on the Stage Coach");
                        break;
                    }
                    case "stage_coach_bonus_recruits":
                    {
                        var came = AddAnyRecruits(amount);
                        active.Notes.Add(came + (came == 1 ? " more recruit waits" : " more recruits wait") + " at the Stage Coach");
                        break;
                    }
                    case "upgrade_tag_free":
                        active.FreeUpgrades.TryGetValue(effect.Text, out var have);
                        active.FreeUpgrades[effect.Text] = have + Math.Max(1, amount);
                        break;
                    case "districts_unlocked":
                        // DD1's "Cornerstones": from now on the districts can be built in
                        if (Districts.Unlock("town event " + def.Id)) Narration.Say(Dd1Strings.Parts("str_vo_crimson_court_town_event_" + def.Id), Narration.Scope.Hamlet, "districts");
                        break;
                    case "bonus_currency":
                        // DD1's notice has its own line for it ("1 Blueprint awarded!")
                        if (effect.Text == DistrictRules.Blueprint) Districts.AddBlueprints(Math.Max(1, amount), "town event " + def.Id);
                        break;
                    case "plot_quest":
                        // DD1's quest of the event stands on the Estate Map for the week (Estate/PlotQuests.cs)
                        PlotQuests.Arrived(def.Id, effect.Text);
                        break;
                    case "trinket_retention_add_from_storage":
                    {
                        // DD1's "A Thief in the Night": the Shrieker carries off that many of the stores' trinkets
                        var taken = Shrieker.Steal(Math.Max(1, amount));
                        // (the notice's own line says DD1's number; what really went is named beside it)
                        if (taken.Count > 0) active.Notes.Add("Gone from the stores: " + UpgradeText.Join(taken.Select(Trinkets.Name).ToList()));
                        QuestBoard.SyncEventQuests();
                        break;
                    }
                    case "dead_recruit":
                    {
                        var graves = ReturnableFallen();
                        _rng.Shuffle(graves);
                        foreach (var fallen in graves.Take(Math.Max(1, amount))) active.Returnable.Add(FallenKey(fallen));
                        if (active.Returnable.Count == 0) active.Notes.Add("None of the fallen can find a place among the living");
                        break;
                    }
                }
            }
            Plugin.Log.LogInfo("Town events: " + active.Id + " arrives" + (active.Notes.Count > 0 ? ": " + string.Join("; ", active.Notes) : ""));
        }

        // Puts up to `count` recruits of a class on this week's coach. The coach names them the way it names its own.
        private static int AddRecruits(string classId, int count, bool anyPath)
        {
            var added = 0;
            for (var i = 0; i < count; i++)
            {
                var path = FreePath(classId, null, anyPath, true);
                if (path == null) break;
                var before = StageCoach.Offer.Count;
                var answer = StageCoach.OfferForTest(classId, 0, path);
                if (StageCoach.Offer.Count <= before)
                {
                    Plugin.Log.LogInfo("Town events: the coach took no " + classId + ": " + answer);
                    break;
                }
                added++;
            }
            return added;
        }

        // "More recruits than usual": the coach's own rule (a class and a path nobody has, among the paths that
        // are open), one of every class before a second of any.
        private static int AddAnyRecruits(int count)
        {
            var classes = new List<string>();
            foreach (var classId in RosterLifecycle.AvailableClasses())
            {
                var blocked = false;
                foreach (var blocker in StageCoach.ClassBlockers)
                {
                    try { blocked |= blocker(classId); }
                    catch (Exception e) { Plugin.Log.LogError("Town events: a class blocker failed: " + e); }
                }
                if (!blocked) classes.Add(classId);
            }
            _rng.Shuffle(classes);
            var added = 0;
            while (added < count)
            {
                var before = added;
                foreach (var classId in classes)
                {
                    if (added >= count) break;
                    added += AddRecruits(classId, 1, false);
                }
                if (added == before) break;
            }
            return added;
        }

        /// <summary>
        /// A path of the class nobody on the estate walks (nor, with <paramref name="countCoach"/>, anybody
        /// waiting at the coach): the preferred one if it is free, else the first; null when every place is taken.
        /// </summary>
        private static string FreePath(string classId, string preferred, bool anyPath, bool countCoach = true)
        {
            var taken = RosterLifecycle.SeatsTaken();
            if (countCoach)
                foreach (var waiting in StageCoach.Offer) taken.Add(HeroPaths.Key(waiting.ClassId, StageCoach.PathOf(waiting)));
            var all = HeroPaths.Of(classId);
            if (preferred != null && all.Contains(preferred) && !taken.Contains(HeroPaths.Key(classId, preferred))) return preferred;
            foreach (var path in anyPath ? all : HeroPaths.Open(classId))
                if (!taken.Contains(HeroPaths.Key(classId, path))) return path;
            return null;
        }

        // ---- at week end ---------------------------------------------------------------------------------

        internal static List<uint> IdleHeroes()
        {
            // DD1's "idle": at home and not busy. The party is away, and whoever cannot join it has a place in
            // a building (or is missing).
            var idle = new List<uint>();
            foreach (var guid in RosterLifecycle.LivingGuids())
                if (!EstateSession.IsInParty(guid) && EstateSession.PartyBlockReason(guid) == null) idle.Add(guid);
            return idle;
        }

        // Ends the event of a week: what the idle gain, and the resolve experience of the party's buff.
        private static void CloseWeek(int week)
        {
            GrantExperienceBonus();
            ClearBuffs();
            var active = _current != null && _current.Week == week ? _current : null;
            var def = active?.Def;
            if (def == null) return;
            var idle = _idle ?? IdleHeroes();
            var lines = new List<string>();
            foreach (var effect in def.Effects)
            {
                switch (effect.Type)
                {
                    case "idle_resolve_level":
                    {
                        var levels = Math.Max(1, (int)Math.Round(effect.Number));
                        var gained = new List<string>();
                        foreach (var guid in idle)
                        {
                            var actor = SanitariumLedger.Actor(guid);
                            if (actor == null || !actor.IsLiving || actor.ActorDataId != effect.Text) continue;
                            var before = Resolve.Level(guid);
                            for (var i = 0; i < levels; i++)
                            {
                                var missing = Resolve.ToNextLevel(guid);
                                if (missing <= 0) break;
                                Resolve.Grant(guid, missing);
                            }
                            if (Resolve.Level(guid) > before) gained.Add(ActivityLedger.HeroName(actor) + " (resolve " + Resolve.Level(guid) + ")");
                        }
                        if (gained.Count > 0) lines.Add(TownEventText.Title(def.Id) + ": " + UpgradeText.Join(gained) + (gained.Count == 1 ? " has" : " have") + " grown at home.");
                        break;
                    }
                    case "idle_buff":
                    {
                        var eased = IdleRelief(effect.Text, idle);
                        if (eased > 0) lines.Add(TownEventText.Title(def.Id) + ": " + eased + (eased == 1 ? " hero" : " heroes") + " at home shed some stress.");
                        break;
                    }
                }
            }
            foreach (var line in lines)
            {
                Plugin.Log.LogInfo("Town events, week end: " + line);
                // The hamlet reads these out when the party is back, after the quest's own report.
                QuestBoard.Report(line);
            }
        }

        private static float _idleStressHeal = -1f;

        /// <summary>DD1: stress an idle hero sheds in a week (campaign/roster/roster.variables.json), on DD1's scale of 200.</summary>
        private static float Dd1IdleStressHeal
        {
            get
            {
                if (_idleStressHeal >= 0f) return _idleStressHeal;
                _idleStressHeal = 5f;   // FALLBACK = DD1's stock value, used only when the file cannot be read
                try
                {
                    var json = Json.ParseFile(Dd1Install.ReadText("campaign/roster/roster.variables.json"));
                    var value = (float?)json?.SelectToken("town_visit_town_progression.idle_hero_stress_heal");
                    if (value != null && value.Value >= 0f) _idleStressHeal = value.Value;
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 roster.variables.json could not be read: " + e.Message); }
                return _idleStressHeal;
            }
        }

        // DD1's buff multiplies the stress an idle hero sheds anyway (5 of 200 a week, +200% makes 15). The
        // week's own 5 is given by RosterUpkeep, so the event gives what the buff adds to it (10). On DD2's
        // scale that is under a point: the fraction is the chance of a point, as with the Tavern's relief.
        private static int IdleRelief(string buffId, List<uint> idle)
        {
            var buff = Catalog.Buff(buffId);
            if (buff == null || buff.Stat != "stress_heal_received_percent") return 0;
            var dd1 = Dd1IdleStressHeal * (float)buff.Amount;
            var eased = 0;
            foreach (var guid in idle)
            {
                var actor = SanitariumLedger.Actor(guid);
                if (actor == null || !actor.IsLiving || actor.Stress <= 0f) continue;
                var points = Whole(dd1 * actor.StressMax / ActivityRules.Dd1StressMax);
                if (points <= 0) continue;
                actor.ApplyStressHeal(points, SourceType.INN);
                eased++;
            }
            return eased;
        }

        // A whole number with the same average: the fraction is the chance of one more.
        private static int Whole(double value)
        {
            var whole = Math.Floor(value);
            return (int)whole + (_rng.NextDouble() < value - whole ? 1 : 0);
        }

        // ---- on the quest --------------------------------------------------------------------------------

        // The expedition the party is on: which week's event it set out under, and the resolve experience its
        // members had, so that the event's share can be added to what the quest pays.
        private class ExpeditionMemo
        {
            public long Seed;
            public int Week;
            public string Dungeon;
            public bool Success;
            public readonly Dictionary<uint, int> Experience = new Dictionary<uint, int>();
        }

        private class Attached
        {
            public ActorInstance Actor;
            public ActorDataStats Stats;
        }

        private static ExpeditionMemo _expedition;
        private static readonly Dictionary<uint, Attached> Buffed = new Dictionary<uint, Attached>();
        private static float _nextBuffCheck;

        private static void OnExpeditionSeen(DungeonRun run)
        {
            var map = run.Exploration.Map;
            // A run with the seed already on record is the same expedition, restored from a save.
            if (_expedition == null || _expedition.Seed != map.Seed)
            {
                _expedition = new ExpeditionMemo { Seed = map.Seed, Week = EstateState.Current.Week, Dungeon = map.DungeonId };
                foreach (var hero in Provisioning.Party()) _expedition.Experience[hero.ActorGuid] = Resolve.Experience(hero.ActorGuid);
            }
            AttachBuffs();
        }

        private static void EndExpedition(EstateState.Expedition expedition)
        {
            if (_expedition != null) _expedition.Success = expedition.Success;
            ClearBuffs();
        }

        // The game does not save the stats tree the buffs hang in, so they are put back when a save is loaded
        // in the middle of an expedition, and taken off when the expedition is gone.
        private static void OnTick()
        {
            if (Time.unscaledTime < _nextBuffCheck) return;
            _nextBuffCheck = Time.unscaledTime + 1f;
            try
            {
                if (DungeonRun.Current == null) ClearBuffs();
                else if (EstateSession.InHub) AttachBuffs();
                SyncWagon();
            }
            catch (Exception e) { Plugin.Log.LogError("Town events: the party's buffs could not be kept up: " + e); }
        }

        // DD1's upgrade_tag_discount on "trinket" ("Nomad New Year"): the number is the share of the price that
        // is still asked (the notice reads "cost 50%"). The wagon has a week's discount for a town event to set;
        // it does not save it, so it is kept in step with the week's event here.
        private static void SyncWagon()
        {
            var asked = 1f;
            foreach (var effect in Effects(Current, "upgrade_tag_discount"))
                if (effect.Text == "trinket") asked *= Mathf.Clamp01((float)effect.Number);
            var discount = 1f - asked;
            if (Mathf.Approximately(NomadWagon.EventDiscount, discount)) return;
            NomadWagon.EventDiscount = discount;
            Plugin.Log.LogInfo("Town events: the Nomad Wagon's prices are " + (discount > 0f ? Mathf.RoundToInt(discount * 100f) + "% lower this week" : "back to normal"));
        }

        private static bool Applies(TownEventBuff buff, string dungeon)
        {
            return buff.Rule == "always" || (buff.Rule == "in_dungeon" && buff.RuleText == dungeon);
        }

        /// <summary>
        /// The party's buffs of an expedition as DD2 writes stats (StatDataContainer.SetFromCsv). REMAPPING:
        /// DD1's damage_low / damage_high multipliers (both ends of the weapon's range) become DD2's share of
        /// damage dealt; DD1's resolve_check_percent (the chance of a virtue at 100 stress) becomes DD2's
        /// overstress modifier for "resolute", which DD2's own buffs change by the same kind of amounts.
        /// </summary>
        private static string BuffStats(ExpeditionMemo expedition)
        {
            double damage = 0, resolute = 0;
            foreach (var effect in Effects(EventOfWeek(expedition.Week), "embark_party_buff"))
            {
                var buff = Catalog.Buff(effect.Text);
                if (buff == null || !Applies(buff, expedition.Dungeon)) continue;
                if (buff.Stat == "combat_stat_multiply" && (buff.SubStat == "damage_low" || buff.SubStat == "damage_high")) damage += buff.Amount / 2;
                else if (buff.Stat == "resolve_check_percent") resolute += buff.Amount;
            }
            var text = new StringBuilder();
            if (Math.Abs(damage) > 0.0001)
                text.Append("add_stat,").Append(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT.GetName()).Append(',').Append(damage.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            if (Math.Abs(resolute) > 0.0001)
                text.Append("sub_stat,").Append(ActorStatType.OVERSTRESS_CHANCE_MODIFIER.GetName()).Append(',').Append(Resolute).Append(',').Append(resolute.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }

        // Same way the blacksmith hangs gear on a hero: one more stats container under the hero's data, with a
        // source (the damage calculation asks every container for one).
        private static void AttachBuffs()
        {
            if (_expedition == null) return;
            var text = BuffStats(_expedition);
            if (text.Length == 0) return;
            foreach (var hero in Provisioning.Party())
            {
                var guid = hero.ActorGuid;
                if (Buffed.TryGetValue(guid, out var old) && ReferenceEquals(old.Actor, hero)) continue;
                var stats = new ActorDataStats(StatsId, text);
                stats.Init(StatsId + "_" + guid);
                stats.SetSource(SourceType.CLASS, StatsId);
                hero.ActorData.AddChild(stats);
                hero.RefreshStats();
                Buffed[guid] = new Attached { Actor = hero, Stats = stats };
                Plugin.Log.LogInfo("Town events: " + hero.ActorName + " carries the week's blessing into the " + _expedition.Dungeon + ": " + text.Trim().Replace("\n", " | "));
            }
        }

        private static void ClearBuffs()
        {
            if (Buffed.Count == 0) return;
            foreach (var attached in Buffed.Values)
            {
                try
                {
                    attached.Actor.ActorData.RemoveChild(attached.Stats);
                    if (attached.Actor.IsLiving) attached.Actor.RefreshStats();
                }
                catch (Exception e) { Plugin.Log.LogWarning("Town events: a buff could not be taken off: " + e.Message); }
            }
            Buffed.Clear();
        }

        // DD1's resolve_xp_bonus_percent: a share on top of what the quest pays its survivors. DD1 adds every
        // such buff of a hero up and rounds their share of the quest's pay up (Resolve.BonusShare), so the
        // event does not pay by itself: it answers Resolve with its share for the heroes who set out under it.
        private static double ExperienceShare(uint guid, EstateState.Expedition ended)
        {
            var expedition = _expedition;
            if (expedition == null || !expedition.Experience.ContainsKey(guid)) return 0;
            double share = 0;
            foreach (var effect in Effects(EventOfWeek(expedition.Week), "embark_party_buff"))
            {
                var buff = Catalog.Buff(effect.Text);
                if (buff != null && buff.Stat == "resolve_xp_bonus_percent" && Applies(buff, expedition.Dungeon)) share += buff.Amount;
            }
            return share;
        }

        // The week has turned: the expedition's memo has done its work.
        private static void GrantExperienceBonus()
        {
            _expedition = null;
        }

        private static object DescribeExpedition()
        {
            if (_expedition == null) return null;
            return new { week = _expedition.Week, dungeon = _expedition.Dungeon, buffs = BuffStats(_expedition).Trim().Replace("\n", " | "), carriedBy = Buffed.Keys.ToList() };
        }

        // ---- the fallen ----------------------------------------------------------------------------------

        // What the graveyard does not keep and a hero who returns needs: the path they walked, their resolve
        // experience and their gear. Quirks and learned skills die with the game's own actor.
        private class FallenMemo
        {
            public string Path;
            public int Experience, Weapon = 1, Armour = 1;
        }

        private static readonly Dictionary<string, FallenMemo> Fallen = new Dictionary<string, FallenMemo>();

        public static string FallenKey(Graveyard.Fallen fallen) => fallen.Name + "|" + fallen.ClassId + "|" + fallen.Week;

        public static Graveyard.Fallen FindFallen(string key)
        {
            foreach (var fallen in Graveyard.All)
                if (FallenKey(fallen) == key) return fallen;
            return null;
        }

        // Called a frame after the death: the roster still has the entry (marked dead) and the game still has
        // the actor, which is the last moment the path can be read.
        private static void Remember(Graveyard.Fallen fallen)
        {
            var memo = new FallenMemo();
            try
            {
                var roster = RosterLifecycle.Roster;
                var actors = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
                if (roster != null && actors != null)
                {
                    foreach (var guid in roster.GetActorGuids(RosterStatusType.DEAD))
                    {
                        var entry = roster.GetReadOnlyRosterEntryByActorGuid(guid);
                        if (entry == null || entry.ActorClassId != fallen.ClassId) continue;
                        var actor = actors.GetLibraryElement(guid);
                        if (actor != null && !string.IsNullOrEmpty(actor.ActorName) && actor.ActorName != fallen.Name) continue;
                        memo.Path = actor != null ? HeroPaths.Of(actor) : null;
                        memo.Experience = Resolve.Experience(guid);
                        memo.Weapon = Blacksmith.Level(guid, Blacksmith.Gear.Weapon);
                        memo.Armour = Blacksmith.Level(guid, Blacksmith.Gear.Armour);
                        break;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Town events: what " + fallen.Name + " was could not be noted: " + e.Message); }
            Fallen[FallenKey(fallen)] = memo;
        }

        /// <summary>Why one of the fallen cannot come back; null when they can.</summary>
        public static string ReturnBlock(Graveyard.Fallen fallen)
        {
            if (!ClassExists(fallen.ClassId)) return "No " + HeroNames.ClassName(fallen.ClassId) + " can serve on this estate";
            Fallen.TryGetValue(FallenKey(fallen), out var memo);
            // A recruit waiting at the coach holds no place yet: the returning hero comes first.
            return FreePath(fallen.ClassId, memo?.Path, true, false) == null
                ? "The estate has a " + HeroNames.ClassName(fallen.ClassId) + " on every path"
                : null;
        }

        private static List<Graveyard.Fallen> ReturnableFallen()
        {
            var list = new List<Graveyard.Fallen>();
            foreach (var fallen in Graveyard.All)
                if (ReturnBlock(fallen) == null) list.Add(fallen);
            return list;
        }

        /// <summary>
        /// "From Beyond": one of the week's chosen dead comes back as a hero of the estate, on the bench, with
        /// their name, class, path, resolve experience and gear. Returns null, or why nobody came back.
        /// </summary>
        public static string Return(string key)
        {
            var active = Current;
            if (active == null || !active.Returnable.Contains(key)) return "Not among those who may return";
            if (active.Returned) return "One has returned already";
            if (!EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet) return "Not while away from the hamlet";
            var fallen = FindFallen(key);
            if (fallen == null) return "The grave is empty";
            var block = ReturnBlock(fallen);
            if (block != null) return block;
            if (RosterLifecycle.Count >= StageCoach.RosterSize) return "The barracks are full";

            Fallen.TryGetValue(key, out var memo);
            var path = FreePath(fallen.ClassId, memo?.Path, true, false);
            var guid = RosterLifecycle.CreateHero(fallen.ClassId, fallen.Name, false, path);
            if (guid == 0u) return "The way back is closed";
            if (memo != null)
            {
                if (memo.Experience > 0) Resolve.Grant(guid, memo.Experience);
                var actor = SanitariumLedger.Actor(guid);
                if (actor != null) Blacksmith.Grant(actor, memo.Weapon, memo.Armour);
            }
            // The graveyard hands its own list out read-only; a hero who walks out of it has to come off it.
            // (A Graveyard.Remove would say this better; the list is the graveyard's.)
            if (Graveyard.All is IList<Graveyard.Fallen> graves) graves.Remove(fallen);
            Fallen.Remove(key);
            active.Returned = true;
            active.Notes.Add(fallen.Name + " has returned from the grave");
            Plugin.Log.LogInfo("Town events: " + fallen.Name + " the " + fallen.ClassId + " returns from the grave (#" + guid + ", resolve " + Resolve.Level(guid) + ")");
            RaiseChanged();
            EstatePersistence.SaveNow("town event");
            return null;
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JToken SaveFallen()
        {
            var json = new JObject();
            foreach (var fallen in Graveyard.All)
            {
                var key = FallenKey(fallen);
                if (!Fallen.TryGetValue(key, out var memo)) continue;
                json[key] = new JObject { ["path"] = memo.Path, ["xp"] = memo.Experience, ["w"] = memo.Weapon, ["a"] = memo.Armour };
            }
            return json;
        }

        private static void LoadFallen(JToken token)
        {
            Fallen.Clear();
            if (!(token is JObject json)) return;
            foreach (var property in json.Properties())
            {
                if (!(property.Value is JObject memo)) continue;
                Fallen[property.Name] = new FallenMemo
                {
                    Path = (string)memo["path"],
                    Experience = (int?)memo["xp"] ?? 0,
                    Weapon = Math.Max(1, (int?)memo["w"] ?? 1),
                    Armour = Math.Max(1, (int?)memo["a"] ?? 1)
                };
            }
        }

        private static JToken SaveExpedition()
        {
            if (_expedition == null) return JValue.CreateNull();
            var experience = new JObject();
            foreach (var pair in _expedition.Experience) experience[pair.Key.ToString(CultureInfo.InvariantCulture)] = pair.Value;
            return new JObject
            {
                ["seed"] = _expedition.Seed.ToString(CultureInfo.InvariantCulture),
                ["week"] = _expedition.Week,
                ["dungeon"] = _expedition.Dungeon,
                ["xp"] = experience
            };
        }

        private static void LoadExpedition(JToken token)
        {
            _expedition = null;
            if (!(token is JObject json) || !long.TryParse((string)json["seed"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) return;
            _expedition = new ExpeditionMemo { Seed = seed, Week = (int?)json["week"] ?? 0, Dungeon = (string)json["dungeon"] };
            if (json["xp"] is JObject experience)
                foreach (var property in experience.Properties())
                    if (uint.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var guid)) _expedition.Experience[guid] = (int?)property.Value ?? 0;
        }

        private static void ResetEffects()
        {
            // The heroes the buffs hung on are gone with the session they belonged to.
            Buffed.Clear();
            Fallen.Clear();
            _expedition = null;
        }
    }
}
