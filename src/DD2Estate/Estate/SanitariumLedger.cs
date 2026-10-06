using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Quirk;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Dd2;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Who lies in which cell of the Sanitarium, for what, and what the last week end did for them. A hero pays
    /// on being admitted, leaves the party and cannot embark; the treatment is done when the week advances and
    /// the hero walks out. Until then the cell can be cleared, but the price is not given back (DD1). Saved as
    /// the "sanitarium" section of the estate.
    ///
    /// The treatments are the calls DD2's own hospital makes (HospitalScreenBhv): QuirkInstance.Lock() for a
    /// quirk to keep, QuirkContainer.Remove(instance, SourceType.HOSPITAL, null, 0) for one to lose.
    /// </summary>
    [EstateModule]
    internal static class SanitariumLedger
    {
        public class Stay
        {
            public uint Guid;
            public string Ward;
            public int Slot;
            /// <summary>Id of the DD2 quirk to treat.</summary>
            public string Quirk;
            public SanitariumRules.Treatment Kind;
            public int Paid;
        }

        private static readonly List<Stay> Stays = new List<Stay>();
        private static readonly List<string> Results = new List<string>();
        private static int _resultsWeek;
        private static readonly System.Random Random = new System.Random();

        /// <summary>Raised after any change to the cells or the results.</summary>
        public static event Action Changed;

        private static void Register()
        {
            EstateState.RegisterSection("sanitarium", Save, Load, Reset);
            EstateState.WeekAdvanced += OnWeekAdvanced;
            EstateSession.PartyBlockers.Add(BlockReason);
            RosterLifecycle.Left += Forget;
        }

        // ---- queries -------------------------------------------------------------------------------------

        public static Stay At(SanitariumRules.Ward ward, int slot)
        {
            foreach (var stay in Stays)
                if (stay.Slot == slot && stay.Ward == ward.Id) return stay;
            return null;
        }

        public static Stay StayOf(uint guid)
        {
            foreach (var stay in Stays)
                if (stay.Guid == guid) return stay;
            return null;
        }

        /// <summary>Why a hero cannot embark, for the party checks; null if free.</summary>
        public static string BlockReason(uint guid)
        {
            return StayOf(guid) != null ? "In the " + ActivityText.Building(SanitariumRules.Building) : null;
        }

        /// <summary>
        /// Why a hero cannot be admitted to this ward; null if nothing stands in the way but the choice of a
        /// treatment and its price. A hero busy anywhere on the estate (here, at the Tavern, missing) is refused.
        /// </summary>
        public static string Refusal(uint guid, SanitariumRules.Ward ward)
        {
            var busy = EstateSession.PartyBlockReason(guid);
            if (busy != null) return busy;
            var actor = Actor(guid);
            if (actor == null || !actor.IsLiving) return "Gone";
            if (SanitariumRules.Options(actor, ward).Count > 0) return null;
            // DD1: the Medical Ward takes the diseased only.
            return ward.TreatsDiseases && !ward.TreatsQuirks ? "No disease" : "No quirks to treat";
        }

        /// <summary>"Remove: Clumsy", with the game's own coloured quirk name.</summary>
        public static string TreatmentText(Stay stay)
        {
            return SanitariumRules.Verb(stay.Kind) + ": " + SanitariumRules.QuirkName(stay.Quirk, Actor(stay.Guid));
        }

        /// <summary>Last week end's lines; empty once another week has passed.</summary>
        public static List<string> LastResults()
        {
            return _resultsWeek == EstateState.Current.Week ? new List<string>(Results) : new List<string>();
        }

        // ---- player actions ------------------------------------------------------------------------------

        /// <summary>Admits a hero to a cell for one treatment and takes the price. Returns what went wrong, or null.</summary>
        public static string Place(uint guid, SanitariumRules.Ward ward, int slot, string quirkId)
        {
            var refusal = Refusal(guid, ward);
            if (refusal != null) return refusal;
            if (slot < 0 || slot >= SanitariumRules.Slots(ward)) return "That cell is not open";
            if (At(ward, slot) != null) return "That cell is taken";
            var option = SanitariumRules.OptionFor(Actor(guid), ward, quirkId);
            if (option == null) return "Nothing of that kind to treat in the " + ActivityText.Activity(ward.Id);
            if (option.Blocked != null) return option.Blocked;
            // A free week (a town event) must not read as "not offered".
            if (option.Cost <= 0 && TownEventHooks.ActivityPriceFactor(ward.Id) > 0f) return "The " + ActivityText.Activity(ward.Id) + " does not offer that";
            if (EstateState.Gold < option.Cost) return "Not enough gold: " + option.Cost + " needed";

            EstateState.AddGold(-option.Cost);
            EstateSession.Bench(guid);
            Stays.Add(new Stay { Guid = guid, Ward = ward.Id, Slot = slot, Quirk = option.Id, Kind = option.Kind, Paid = option.Cost });
            EstateAudio.Ui(ward.Id == "disease_treatment" ? "town/sanitarium_disease_treatment" : "town/sanitarium_treatment");
            Changed?.Invoke();
            EstatePersistence.SaveNow("sanitarium");
            return null;
        }

        /// <summary>
        /// Takes a hero out before the week ends. As in DD1 the price is not given back; the hero is free for
        /// quests at once (str_hero_slot_locked_cancel_confirm). Returns what went wrong, or null.
        /// </summary>
        public static string Cancel(Stay stay)
        {
            if (!Stays.Remove(stay)) return "Nobody is there";
            Changed?.Invoke();
            EstatePersistence.SaveNow("sanitarium");
            return null;
        }

        /// <summary>Drops a hero who is leaving the estate for good (dismissed, dead) from the cells.</summary>
        public static void Forget(uint guid)
        {
            if (Stays.RemoveAll(stay => stay.Guid == guid) > 0) Changed?.Invoke();
        }

        /// <summary>Snapshot for the log and the test bridge.</summary>
        public static object Describe()
        {
            var stays = new List<object>();
            foreach (var stay in Stays)
                stays.Add(new { guid = stay.Guid, ward = stay.Ward, slot = stay.Slot, quirk = stay.Quirk, treatment = stay.Kind.ToString().ToLowerInvariant(), paid = stay.Paid });
            return new { week = EstateState.Current.Week, gold = EstateState.Gold, stays, resultsWeek = _resultsWeek, results = new List<string>(Results) };
        }

        // ---- week end ------------------------------------------------------------------------------------

        private static void OnWeekAdvanced(int week)
        {
            Results.Clear();
            _resultsWeek = week;
            foreach (var stay in Stays.ToArray())
            {
                // DD1: a treatment takes the week, whatever comes of it.
                Stays.Remove(stay);
                var actor = Actor(stay.Guid);
                var ward = SanitariumRules.Find(stay.Ward);
                if (actor == null || !actor.IsLiving) continue;
                try { EndWeek(stay, actor, ward); }
                catch (Exception e) { Plugin.Log.LogError("Sanitarium: the treatment of " + stay.Quirk + " failed at the week end: " + e); }
            }
            Changed?.Invoke();
        }

        private static void EndWeek(Stay stay, ActorInstance actor, SanitariumRules.Ward ward)
        {
            var who = ActivityLedger.HeroName(actor) + ", " + ActivityText.Activity(stay.Ward) + ": ";
            var quirks = actor.QuirkContainer;
            var quirk = quirks != null ? Find(quirks, stay.Quirk) : null;
            if (quirk == null || ward == null)
            {
                // Nothing left to treat (or DD1's rules are gone): the estate does not pay for nothing.
                EstateState.AddGold(stay.Paid);
                Report(who + "there was nothing left to treat. " + stay.Paid + " gold returned.");
                return;
            }
            var name = SanitariumRules.QuirkName(quirk.Definition, actor);
            if (Random.NextDouble() >= SanitariumRules.SuccessChance(ward))
            {
                Report(who + "the treatment of " + name + " did not take.");
                return;
            }

            switch (stay.Kind)
            {
                case SanitariumRules.Treatment.Lock:
                    quirk.Lock();
                    Report(who + name + " is locked in.");
                    break;
                case SanitariumRules.Treatment.Cure:
                    quirks.Remove(quirk, SourceType.HOSPITAL, null, 0u);
                    var text = who + "cured of " + name + ".";
                    var others = Diseases(quirks);
                    if (others.Count > 0 && Random.NextDouble() < SanitariumRules.CureAllChance(ward))
                    {
                        var names = new List<string>();
                        foreach (var other in others)
                        {
                            names.Add(SanitariumRules.QuirkName(other.Definition, actor));
                            quirks.Remove(other, SourceType.HOSPITAL, null, 0u);
                        }
                        text += " The cure took " + string.Join(", ", names) + " with it.";
                    }
                    Report(text);
                    break;
                default:
                    quirks.Remove(quirk, SourceType.HOSPITAL, null, 0u);
                    Report(who + name + " is gone.");
                    break;
            }
        }

        private static QuirkInstance Find(QuirkContainer quirks, string id)
        {
            for (var i = 0; i < quirks.GetNumberOfInstances(); i++)
            {
                var quirk = quirks.GetInstanceAtIndex(i);
                if (quirk?.Definition != null && quirk.Definition.Id == id) return quirk;
            }
            return null;
        }

        private static List<QuirkInstance> Diseases(QuirkContainer quirks)
        {
            var found = new List<QuirkInstance>();
            for (var i = 0; i < quirks.GetNumberOfInstances(); i++)
            {
                var quirk = quirks.GetInstanceAtIndex(i);
                if (quirk?.Definition != null && quirk.Definition.IsDisease && !quirk.Definition.IsCurse) found.Add(quirk);
            }
            return found;
        }

        private static void Report(string text)
        {
            Results.Add(text);
            Plugin.Log.LogInfo("Week end, sanitarium: " + text);
        }

        // ---- save ----------------------------------------------------------------------------------------

        private static JToken Save()
        {
            var stays = new JArray();
            foreach (var stay in Stays)
            {
                stays.Add(new JObject
                {
                    ["guid"] = stay.Guid, ["ward"] = stay.Ward, ["slot"] = stay.Slot,
                    ["quirk"] = stay.Quirk, ["treatment"] = stay.Kind.ToString(), ["paid"] = stay.Paid
                });
            }
            return new JObject { ["stays"] = stays, ["results_week"] = _resultsWeek, ["results"] = new JArray(Results) };
        }

        private static void Load(JToken token)
        {
            Clear();
            if (token is JObject json)
            {
                foreach (var s in json["stays"] as JArray ?? new JArray())
                {
                    if (!Enum.TryParse((string)s["treatment"], true, out SanitariumRules.Treatment kind)) continue;
                    Stays.Add(new Stay
                    {
                        Guid = (uint)s["guid"], Ward = (string)s["ward"], Slot = (int?)s["slot"] ?? 0,
                        Quirk = (string)s["quirk"], Kind = kind, Paid = (int?)s["paid"] ?? 0
                    });
                }
                _resultsWeek = (int?)json["results_week"] ?? 0;
                foreach (var r in json["results"] as JArray ?? new JArray()) Results.Add((string)r);
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
            Results.Clear();
            _resultsWeek = 0;
        }

        // ---- helpers -------------------------------------------------------------------------------------

        public static ActorInstance Actor(uint guid)
        {
            var library = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance;
            return library != null ? library.GetLibraryElement(guid) : null;
        }
    }
}
