using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Quirk;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Who spends the week in which activity slot, who has gone missing, and what the last week end did to
    /// them. A hero pays on entering a slot, leaves the party and cannot embark; the stress relief and DD1's
    /// side-effect roll come when the week advances. Saved as the "activities" section of the estate.
    ///
    /// The Caretaker takes his ease in the town too, as in DD1: every week he sits in a slot of one of its
    /// comforts and no hero can have it (campaign/town/hero_slot/caretaker_portrait.png;
    /// str_cant_place_hero_here_caretaker). DD1's files say where he may sit (an activity's
    /// miscellaneous.caretaker_friendly); how often and in which slot is in DD1's code. Here he is somewhere
    /// every week: in the first slot (where DD1's own picture of the tavern, tutorial_popup.stress_relief.png,
    /// has him) of an activity drawn by lot among those whose first slot is open and empty.
    /// </summary>
    [EstateModule]
    internal static class ActivityLedger
    {
        public class Stay
        {
            public uint Guid;
            public string Building;
            public string Activity;
            public int Slot;
            public int Paid;
            /// <summary>Refused to leave at the last week end: one more week, no refund, no second roll.</summary>
            public bool Locked;
        }

        private class Absence
        {
            public uint Guid;
            public string Building;     // where the hero was last seen; the return is reported there
            public int Weeks;
        }

        private class Result
        {
            public string Building;
            public string Text;
        }

        private static readonly List<Stay> Stays = new List<Stay>();
        private static readonly List<Absence> Absences = new List<Absence>();
        private static readonly List<Result> Results = new List<Result>();
        private static int _resultsWeek;
        private static readonly System.Random Random = new System.Random();

        /// <summary>The slot of an activity the Caretaker sits in.</summary>
        public const int CaretakerSlot = 0;
        // Where the Caretaker spends the week it was drawn for; no activity: nowhere (every first slot was shut or taken).
        private static string _caretakerBuilding, _caretakerActivity;
        private static int _caretakerWeek = -1;

        /// <summary>Raised after any change to the slots, the missing list or the results.</summary>
        public static event Action Changed;

        private static void Register()
        {
            EstateState.RegisterSection("activities", Save, Load, Reset);
            EstateState.WeekAdvanced += OnWeekAdvanced;
            EstateSession.PartyBlockers.Add(BlockReason);
            RosterLifecycle.Left += Forget;
        }

        // ---- queries -------------------------------------------------------------------------------------

        public static Stay At(ActivityRules.Activity activity, int slot)
        {
            foreach (var stay in Stays)
                if (stay.Slot == slot && stay.Activity == activity.Id && stay.Building == activity.Building) return stay;
            return null;
        }

        public static Stay StayOf(uint guid)
        {
            foreach (var stay in Stays)
                if (stay.Guid == guid) return stay;
            return null;
        }

        /// <summary>Whether the Caretaker has this slot for the week.</summary>
        public static bool CaretakerAt(ActivityRules.Activity activity, int slot)
        {
            SeatCaretaker();
            return slot == CaretakerSlot && activity.Id == _caretakerActivity && activity.Building == _caretakerBuilding;
        }

        // Draws the Caretaker's place for the present week, once. Done when the week turns; a week that has
        // none yet (a new estate, a save from before he came to town) gets it the first time it is asked for.
        private static void SeatCaretaker()
        {
            var week = EstateState.Current.Week;
            if (_caretakerWeek == week) return;
            _caretakerWeek = week;
            _caretakerBuilding = _caretakerActivity = null;
            var free = new List<ActivityRules.Activity>();
            foreach (var building in ActivityRules.BuildingIds)
                foreach (var activity in ActivityRules.For(building))
                    if (activity.CaretakerFriendly && ActivityRules.Slots(activity) > CaretakerSlot && At(activity, CaretakerSlot) == null) free.Add(activity);
            if (free.Count == 0) return;
            var seat = free[Random.Next(free.Count)];
            _caretakerBuilding = seat.Building;
            _caretakerActivity = seat.Id;
            Plugin.Log.LogInfo("Week " + week + ": the Caretaker spends it at the " + seat.Building + " (" + seat.Id + ")");
        }

        /// <summary>
        /// Dev bridge: seats the Caretaker for the present week in an activity of a building; no activity:
        /// nowhere. Returns what went wrong, or null.
        /// </summary>
        public static string SeatCaretakerForTest(string building, string activityId)
        {
            ActivityRules.Activity seat = null;
            if (!string.IsNullOrEmpty(activityId))
            {
                seat = ActivityRules.Find(building ?? "tavern", activityId);
                if (seat == null) return "no such activity";
                if (At(seat, CaretakerSlot) != null) return "a hero has that slot";
            }
            _caretakerWeek = EstateState.Current.Week;
            _caretakerBuilding = seat?.Building;
            _caretakerActivity = seat?.Id;
            Changed?.Invoke();
            return null;
        }

        public static int MissingWeeks(uint guid)
        {
            foreach (var absence in Absences)
                if (absence.Guid == guid) return absence.Weeks;
            return 0;
        }

        /// <summary>Why a hero cannot embark, for the party checks ("At the Tavern", "Missing"); null if free.</summary>
        public static string BlockReason(uint guid)
        {
            var stay = StayOf(guid);
            if (stay != null) return "At the " + ActivityText.Building(stay.Building);
            return MissingWeeks(guid) > 0 ? "Missing" : null;
        }

        /// <summary>Why a hero cannot be put into this activity; null if nothing stands in the way but the price.</summary>
        public static string Refusal(uint guid, ActivityRules.Activity activity)
        {
            // Anything that keeps a hero from the party keeps them from the town's comforts too (a ward's patient).
            var busy = EstateSession.PartyBlockReason(guid);
            if (busy != null) return busy;
            var actor = Actor(guid);
            if (actor == null || !actor.IsLiving) return "Gone";
            var quirks = actor.QuirkContainer;
            if (quirks == null) return null;
            foreach (var dd1Quirk in activity.RefusedByQuirks)
            {
                var quirk = Dd2Quirk(dd1Quirk);
                if (quirk != null && quirks.GetHasInstanceWithId(quirk.Id, false, 0u)) return "Refuses: " + QuirkName(quirk, actor);
            }
            return null;
        }

        /// <summary>Last week end's lines for a building; empty once another week has passed.</summary>
        public static List<string> ResultsFor(string building)
        {
            var lines = new List<string>();
            if (_resultsWeek != EstateState.Current.Week) return lines;
            foreach (var result in Results)
                if (result.Building == building) lines.Add(result.Text);
            return lines;
        }

        /// <summary>
        /// The DD2 quirk that carries a DD1 quirk's id. DD2 wraps its ids ("quirk_calm_pos",
        /// "quirk_resolution_neg", "disease_syphilis"); a DD1 quirk that DD2 does not have gives null, and the
        /// side effect or restriction that names it is dropped.
        /// </summary>
        public static QuirkDefinition Dd2Quirk(string dd1Id)
        {
            var library = SingletonMonoBehaviour<Library<string, QuirkDefinition>>.Instance;
            if (string.IsNullOrEmpty(dd1Id) || library == null) return null;
            foreach (var id in new[] { "quirk_" + dd1Id + "_pos", "quirk_" + dd1Id + "_neg", "disease_" + dd1Id, dd1Id })
                if (library.TryGetLibraryElement(id, out var quirk) && quirk != null) return quirk;
            return null;
        }

        // ---- player actions ------------------------------------------------------------------------------

        /// <summary>Puts a hero into a slot and takes the price. Returns what went wrong, or null.</summary>
        public static string Place(uint guid, ActivityRules.Activity activity, int slot)
        {
            var refusal = Refusal(guid, activity);
            if (refusal != null) return refusal;
            if (slot < 0 || slot >= ActivityRules.Slots(activity)) return "That slot is not open";
            if (At(activity, slot) != null) return "That slot is taken";
            if (CaretakerAt(activity, slot)) return "The Caretaker has that slot this week";
            var cost = ActivityRules.CostFor(activity, guid);
            if (EstateState.Gold < cost) return "Not enough gold: " + cost + " needed";

            EstateState.AddGold(-cost);
            EstateSession.Bench(guid);
            Stays.Add(new Stay { Guid = guid, Building = activity.Building, Activity = activity.Id, Slot = slot, Paid = cost });
            // DD1 names these sounds by building and activity: town/tavern_bar, town/abbey_prayer ...
            EstateAudio.Ui("town/" + activity.Building + "_" + activity.Id);
            Changed?.Invoke();
            EstatePersistence.SaveNow("activity");
            return null;
        }

        /// <summary>
        /// Takes a hero out before the week ends. As in DD1 the price is not given back; the hero is free for
        /// quests at once (str_hero_slot_locked_cancel_confirm). Returns what went wrong, or null.
        /// </summary>
        public static string Cancel(Stay stay)
        {
            if (!Stays.Contains(stay)) return "Nobody is there";
            if (stay.Locked) return "Refuses to leave before the week is out";
            Stays.Remove(stay);
            Changed?.Invoke();
            EstatePersistence.SaveNow("activity");
            return null;
        }

        /// <summary>Drops a hero who is leaving the estate for good (dismissed, dead) from the slots and the missing list.</summary>
        public static void Forget(uint guid)
        {
            var removed = Stays.RemoveAll(stay => stay.Guid == guid) + Absences.RemoveAll(absence => absence.Guid == guid);
            if (removed > 0) Changed?.Invoke();
        }

        /// <summary>Snapshot for the log and the test bridge.</summary>
        public static object Describe()
        {
            var stays = new List<object>();
            foreach (var stay in Stays)
                stays.Add(new { guid = stay.Guid, building = stay.Building, activity = stay.Activity, slot = stay.Slot, paid = stay.Paid, locked = stay.Locked });
            var missing = new List<object>();
            foreach (var absence in Absences)
                missing.Add(new { guid = absence.Guid, building = absence.Building, weeks = absence.Weeks });
            var results = new List<string>();
            foreach (var result in Results) results.Add(result.Building + ": " + result.Text);
            SeatCaretaker();
            return new
            {
                week = EstateState.Current.Week, gold = EstateState.Gold, stays, missing, resultsWeek = _resultsWeek, results,
                caretaker = new { building = _caretakerBuilding, activity = _caretakerActivity, slot = CaretakerSlot }
            };
        }

        // ---- week end ------------------------------------------------------------------------------------

        private static void OnWeekAdvanced(int week)
        {
            Results.Clear();
            _resultsWeek = week;

            // Those already missing come a week closer to home; this week's leavers are added after them.
            for (var i = Absences.Count - 1; i >= 0; i--)
            {
                var absence = Absences[i];
                if (--absence.Weeks > 0) continue;
                Absences.RemoveAt(i);
                var actor = Actor(absence.Guid);
                if (actor != null && actor.IsLiving) Report(absence.Building, HeroName(actor) + " has found the way back to the hamlet.");
            }

            foreach (var stay in Stays.ToArray())
            {
                var actor = Actor(stay.Guid);
                var activity = ActivityRules.Find(stay.Building, stay.Activity);
                if (actor == null || !actor.IsLiving || activity == null)
                {
                    Stays.Remove(stay);
                    continue;
                }
                try { EndWeek(stay, actor, activity); }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Activity " + stay.Activity + " failed at the week end: " + e);
                    Stays.Remove(stay);
                }
            }
            // The new week's place for the Caretaker, among the slots nobody refused to leave.
            try { SeatCaretaker(); }
            catch (Exception e) { Plugin.Log.LogError("The Caretaker found no place for the week: " + e); }
            Changed?.Invoke();
        }

        private static void EndWeek(Stay stay, ActorInstance actor, ActivityRules.Activity activity)
        {
            var before = Mathf.RoundToInt(actor.Stress);
            // A Ray of Sunlight / The Miserable Dark: the week's event scales what the town's comforts give.
            actor.ApplyStressHeal(ActivityRules.RollRelief(activity, actor.StressMax * TownEventHooks.ActivityReliefFactor(activity.Id), Random), SourceType.INN);
            var after = Mathf.RoundToInt(actor.Stress);
            var text = HeroName(actor) + ", " + ActivityText.Activity(activity.Id) + ": "
                       + (after < before ? "stress " + before + " to " + after + "." : "had no stress to shed.");

            var leaves = true;
            if (!stay.Locked)
            {
                var effect = ActivityRules.RollSideEffect(activity, Random, out var outcome);
                switch (effect?.Type)
                {
                    case "activity_lock":
                        stay.Locked = true;
                        leaves = false;
                        text += " Refuses to leave and stays another week.";
                        break;
                    case "go_missing":
                        var weeks = Math.Max(1, outcome?.Weeks ?? 1);
                        Absences.Add(new Absence { Guid = stay.Guid, Building = stay.Building, Weeks = weeks });
                        text += " Wandered off: missing for " + weeks + (weeks == 1 ? " week." : " weeks.");
                        break;
                    case "add_quirk":
                        var quirk = Dd2Quirk(outcome?.Quirk);
                        var quirks = actor.QuirkContainer;
                        if (quirk == null || quirks == null || !quirks.IsEnabled || quirks.GetHasInstanceWithId(quirk.Id, false, 0u)) break;
                        quirks.Add(quirk, SourceType.QUIRK, null, 0u);
                        text += " Came away with a quirk: " + QuirkName(quirk, actor) + ".";
                        break;
                    case "remove_currency":
                        if (outcome == null || outcome.Currency != "gold") break;
                        var lost = Math.Min(EstateState.Gold, outcome.Amount);
                        if (lost <= 0) break;
                        EstateState.AddGold(-lost);
                        text += " Cost the estate another " + lost + " gold.";
                        break;
                    case "add_currency":
                        if (outcome == null || outcome.Currency != "gold") break;
                        var won = outcome.Amount;
                        if (won <= 0) break;
                        EstateState.AddGold(won);
                        text += " Brought " + won + " gold home.";
                        break;
                    // apply_buff, add_trinket, remove_trinket: DD1's town buffs and trinket rarities have no
                    // DD2 counterpart here, so the roll is spent and nothing happens (as with an unknown quirk).
                }
            }

            if (leaves) Stays.Remove(stay);
            Report(stay.Building, text);
        }

        private static void Report(string building, string text)
        {
            Results.Add(new Result { Building = building, Text = text });
            Plugin.Log.LogInfo("Week end, " + building + ": " + text);
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JToken Save()
        {
            var stays = new JArray();
            foreach (var stay in Stays)
            {
                stays.Add(new JObject
                {
                    ["guid"] = stay.Guid, ["building"] = stay.Building, ["activity"] = stay.Activity,
                    ["slot"] = stay.Slot, ["paid"] = stay.Paid, ["locked"] = stay.Locked
                });
            }
            var missing = new JArray();
            foreach (var absence in Absences)
                missing.Add(new JObject { ["guid"] = absence.Guid, ["building"] = absence.Building, ["weeks"] = absence.Weeks });
            var results = new JArray();
            foreach (var result in Results)
                results.Add(new JObject { ["building"] = result.Building, ["text"] = result.Text });
            // The week's seat is drawn before it is written: a reload finds the Caretaker where he was.
            try { SeatCaretaker(); }
            catch (Exception e) { Plugin.Log.LogError("The Caretaker found no place for the week: " + e); }
            return new JObject
            {
                ["stays"] = stays, ["missing"] = missing, ["results_week"] = _resultsWeek, ["results"] = results,
                ["caretaker"] = new JObject { ["week"] = _caretakerWeek, ["building"] = _caretakerBuilding, ["activity"] = _caretakerActivity }
            };
        }

        private static void Load(JToken token)
        {
            Clear();
            if (token is JObject json)
            {
                foreach (var s in json["stays"] as JArray ?? new JArray())
                {
                    Stays.Add(new Stay
                    {
                        Guid = (uint)s["guid"], Building = (string)s["building"], Activity = (string)s["activity"],
                        Slot = (int?)s["slot"] ?? 0, Paid = (int?)s["paid"] ?? 0, Locked = (bool?)s["locked"] ?? false
                    });
                }
                foreach (var m in json["missing"] as JArray ?? new JArray())
                    Absences.Add(new Absence { Guid = (uint)m["guid"], Building = (string)m["building"], Weeks = (int?)m["weeks"] ?? 1 });
                _resultsWeek = (int?)json["results_week"] ?? 0;
                foreach (var r in json["results"] as JArray ?? new JArray())
                    Results.Add(new Result { Building = (string)r["building"], Text = (string)r["text"] });
                if (json["caretaker"] is JObject caretaker)
                {
                    _caretakerWeek = (int?)caretaker["week"] ?? -1;
                    _caretakerBuilding = (string)caretaker["building"];
                    _caretakerActivity = (string)caretaker["activity"];
                }
            }
            Changed?.Invoke();
        }

        private static void Reset()
        {
            Clear();
            Changed?.Invoke();
        }

        private static void Clear()
        {
            Stays.Clear();
            Absences.Clear();
            Results.Clear();
            _resultsWeek = 0;
            _caretakerWeek = -1;
            _caretakerBuilding = _caretakerActivity = null;
        }

        // ---- helpers -------------------------------------------------------------------------------------

        private static ActorInstance Actor(uint guid)
        {
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            return library != null ? library.GetLibraryElement(guid) : null;
        }

        public static string HeroName(ActorInstance actor)
        {
            return string.IsNullOrEmpty(actor.ActorName) ? HeroNames.ClassName(actor) : actor.ActorName;
        }

        // The game's own coloured quirk name; the bare id if its text tables are not there to ask.
        private static string QuirkName(QuirkDefinition quirk, ActorInstance actor)
        {
            try { return QuirkDescription.GetNameString(quirk, actor, false); }
            catch (Exception) { return quirk.Id; }
        }
    }
}
