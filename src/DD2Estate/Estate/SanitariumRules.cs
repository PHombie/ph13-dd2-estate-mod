using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Quirk;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The rules of DD1's Sanitarium, read from the player's DD1 install and applied to DD2's quirks:
    /// campaign/town/buildings/sanitarium/sanitarium.building.json gives the two wards with their prices, cells
    /// and chances per upgrade step, campaign/estate/estate.json the price rise with hero level,
    /// shared/rules.json how many good quirks a hero may have locked in.
    ///
    /// Treatment Ward: a bad quirk (DD2 tag "negative") is removed, a good one ("positive") is locked in with
    /// DD2's own lock (QuirkInstance.Lock: a locked quirk is never pushed out by a new one). Medical Ward: a
    /// disease (tag "disease") is cured, with DD1's chance that every other disease goes with it. DD2's curses
    /// are left alone, as in DD2's own hospital.
    ///
    /// The mod has no numbers of its own here but the gold scale it shares with the Tavern and the Abbey.
    /// tools/preview_sanitarium.py computes the same figures: change one, change the other.
    /// </summary>
    internal static class SanitariumRules
    {
        public const string Building = "sanitarium";
        public const string TreatmentWard = "treatment";
        public const string MedicalWard = "disease_treatment";

        /// <summary>DD1's upgrade trees of the building: prices of the Treatment Ward, of the Medical Ward, cells.</summary>
        public static readonly string[] Trees = { "sanitarium.cost", "sanitarium.disease_quirk_cost", "sanitarium.slots" };

        public enum Treatment { Remove, Lock, Cure }

        // A value that an upgrade step replaces: element 0 of DD1's *_upgrades lists names no step.
        internal class Tier
        {
            public string Tree, Code;
            public float Value;
        }

        public class Ward
        {
            public string Id;
            public int MaxSlots;

            internal readonly List<Tier> Slots = new List<Tier>();
            internal readonly List<Tier> Success = new List<Tier>();
            internal readonly List<Tier> Remove = new List<Tier>();
            internal readonly List<Tier> RemoveLocked = new List<Tier>();
            internal readonly List<Tier> Lock = new List<Tier>();
            internal readonly List<Tier> Cure = new List<Tier>();
            internal readonly List<Tier> CureAll = new List<Tier>();

            /// <summary>DD1 gives the ward prices for quirks (the Treatment Ward).</summary>
            public bool TreatsQuirks => Remove.Count > 0 || Lock.Count > 0;

            /// <summary>DD1 gives the ward a price for diseases (the Medical Ward).</summary>
            public bool TreatsDiseases => Cure.Count > 0;
        }

        /// <summary>One thing a ward can do for a hero.</summary>
        public class Option
        {
            public QuirkInstance Quirk;
            public Treatment Kind;
            /// <summary>Estate gold, for this hero at the present upgrade level.</summary>
            public int Cost;
            /// <summary>Why it cannot be chosen ("Already locked in"); null when it can.</summary>
            public string Blocked;

            public string Id => Quirk.Definition.Id;
        }

        private static readonly JArray Empty = new JArray();
        private static List<Ward> _wards;
        private static List<float> _costMultipliers;
        private static int _maxLockedPositive = -1;

        /// <summary>The wards in DD1's order; empty if they cannot be read from DD1.</summary>
        public static IReadOnlyList<Ward> Wards
        {
            get
            {
                if (_wards != null) return _wards;
                _wards = new List<Ward>();
                try { Load(_wards); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("The wards of the Sanitarium could not be read from DD1: " + e.Message);
                    _wards.Clear();
                }
                return _wards;
            }
        }

        public static Ward Find(string id)
        {
            foreach (var ward in Wards)
                if (ward.Id == id) return ward;
            return null;
        }

        // ---- values at the present upgrade level -------------------------------------------------------

        public static int Slots(Ward ward)
        {
            var tier = Pick(ward.Slots);
            return tier != null ? Math.Max(0, (int)tier.Value) : 0;
        }

        /// <summary>DD1: the chance that a treatment works at all (1 in the stock game).</summary>
        public static float SuccessChance(Ward ward)
        {
            var tier = Pick(ward.Success);
            return tier != null ? tier.Value : 1f;
        }

        /// <summary>DD1: the chance that curing one disease cures the others too.</summary>
        public static float CureAllChance(Ward ward)
        {
            var tier = Pick(ward.CureAll);
            return tier != null ? Math.Max(0f, Math.Min(1f, tier.Value)) : 0f;
        }

        /// <summary>
        /// Gold for a treatment of a hero of this resolve level; 0 when the ward does not offer it.
        /// <paramref name="lockedIn"/>: the quirk to remove is locked in (DD1's "severe" quirks cost more).
        /// </summary>
        public static int Price(Ward ward, Treatment kind, bool lockedIn, int heroLevel)
        {
            List<Tier> tiers;
            switch (kind)
            {
                case Treatment.Lock: tiers = ward.Lock; break;
                case Treatment.Cure: tiers = ward.Cure; break;
                default: tiers = lockedIn && ward.RemoveLocked.Count > 0 ? ward.RemoveLocked : ward.Remove; break;
            }
            var tier = Pick(tiers);
            if (tier == null || tier.Value <= 0f) return 0;
            return TownEventHooks.ActivityPrice(ward.Id, Math.Max(1, ActivityRules.WholeGold(tier.Value * CostMultiplier(heroLevel))));
        }

        /// <summary>DD1: how many good quirks a hero may have locked in (shared/rules.json).</summary>
        public static int MaxLockedPositive
        {
            get
            {
                if (_maxLockedPositive >= 0) return _maxLockedPositive;
                _maxLockedPositive = 3;     // fallback = DD1's stock value, used only when the file cannot be read
                try
                {
                    var text = Dd1Install.ReadText("shared/rules.json");
                    var value = text != null ? (int?)JObject.Parse(text)["quirks_max_locked_positive"] : null;
                    if (value != null && value.Value >= 0) _maxLockedPositive = value.Value;
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 rules.json could not be read: " + e.Message); }
                return _maxLockedPositive;
            }
        }

        // ---- DD2 quirks --------------------------------------------------------------------------------

        /// <summary>
        /// What a ward can do for a hero, in the order of the hero's quirks: every bad quirk can be removed and
        /// every good one locked in (Treatment Ward), every disease cured (Medical Ward).
        /// </summary>
        public static List<Option> Options(ActorInstance actor, Ward ward)
        {
            var options = new List<Option>();
            var quirks = actor != null ? actor.QuirkContainer : null;
            if (quirks == null || ward == null) return options;
            var level = Resolve.Level(actor.ActorGuid);
            var count = quirks.GetNumberOfInstances();

            var lockedGood = 0;
            for (var i = 0; i < count; i++)
            {
                var quirk = quirks.GetInstanceAtIndex(i);
                if (quirk?.Definition != null && quirk.Definition.IsPositive && quirk.IsLocked()) lockedGood++;
            }

            for (var i = 0; i < count; i++)
            {
                var quirk = quirks.GetInstanceAtIndex(i);
                var definition = quirk?.Definition;
                // A curse is not a disease: DD2's hospital offers no cure for one, and neither does DD1's ward.
                if (definition == null || definition.IsCurse) continue;

                Option option = null;
                if (definition.IsDisease)
                {
                    if (ward.TreatsDiseases) option = new Option { Kind = Treatment.Cure };
                }
                else if (definition.IsNegative)
                {
                    if (ward.Remove.Count > 0) option = new Option { Kind = Treatment.Remove };
                }
                else if (definition.IsPositive && ward.Lock.Count > 0)
                {
                    option = new Option { Kind = Treatment.Lock };
                    if (quirk.IsLocked()) option.Blocked = "Already locked in";
                    else if (lockedGood >= MaxLockedPositive) option.Blocked = "No more than " + MaxLockedPositive + " quirks can be locked in";
                }
                if (option == null) continue;
                option.Quirk = quirk;
                option.Cost = Price(ward, option.Kind, quirk.IsLocked(), level);
                options.Add(option);
            }
            return options;
        }

        public static Option OptionFor(ActorInstance actor, Ward ward, string quirkId)
        {
            foreach (var option in Options(actor, ward))
                if (option.Id == quirkId) return option;
            return null;
        }

        /// <summary>"Remove", "Lock in", "Cure".</summary>
        public static string Verb(Treatment kind)
        {
            return kind == Treatment.Lock ? "Lock in" : kind == Treatment.Cure ? "Cure" : "Remove";
        }

        /// <summary>The game's own coloured quirk name; the bare id if its text tables are not there to ask.</summary>
        public static string QuirkName(QuirkDefinition quirk, ActorInstance actor)
        {
            try { return QuirkDescription.GetNameString(quirk, actor, false); }
            catch (Exception) { return quirk.Id; }
        }

        /// <summary>
        /// A quirk's name without the game's colour marks, for a label that has a colour of its own: the
        /// Sanitarium's lists are in DD1's colours (sanitarium_treatment_*), which the game's marks would overrule.
        /// </summary>
        public static string PlainQuirkName(QuirkDefinition quirk, ActorInstance actor)
        {
            return System.Text.RegularExpressions.Regex.Replace(QuirkName(quirk, actor) ?? "", "<[^>]+>", "").Trim();
        }

        /// <summary>As <see cref="QuirkName(QuirkDefinition, ActorInstance)"/>, for a quirk known by its id only.</summary>
        public static string QuirkName(string quirkId, ActorInstance actor)
        {
            var library = SingletonMonoBehaviour<Library<string, QuirkDefinition>>.Instance;
            return library != null && !string.IsNullOrEmpty(quirkId) && library.TryGetLibraryElement(quirkId, out var quirk) && quirk != null
                ? QuirkName(quirk, actor)
                : quirkId;
        }

        /// <summary>The game's own description of a quirk, as its character sheet shows it; empty if it has none.</summary>
        public static string QuirkText(QuirkDefinition quirk, ActorInstance actor)
        {
            try { return QuirkDescription.GetDescriptionString(quirk, actor) ?? ""; }
            catch (Exception) { return ""; }
        }

        // ---- reading DD1 -------------------------------------------------------------------------------

        // The last tier that needs no upgrade or whose step is built.
        private static Tier Pick(List<Tier> tiers)
        {
            Tier best = null;
            foreach (var tier in tiers)
                if (tier.Code == null || UpgradeRules.Has(tier.Tree, tier.Code)) best = tier;
            return best;
        }

        private static float CostMultiplier(int heroLevel)
        {
            if (_costMultipliers == null)
            {
                _costMultipliers = new List<float>();
                try
                {
                    var text = Dd1Install.ReadText("campaign/estate/estate.json");
                    if (text != null && JObject.Parse(text)["hero_activity_cost_multiplier_table"] is JArray table)
                        foreach (var value in table) _costMultipliers.Add((float)value);
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 estate.json could not be read: " + e.Message); }
            }
            if (_costMultipliers.Count == 0) return 1f;
            return _costMultipliers[Math.Max(0, Math.Min(heroLevel, _costMultipliers.Count - 1))];
        }

        private static void Load(List<Ward> into)
        {
            var file = "campaign/town/buildings/" + Building + "/" + Building + ".building.json";
            var text = Dd1Install.ReadText(file);
            if (text == null)
            {
                Plugin.Log.LogWarning("DD1 file missing: " + file);
                return;
            }
            foreach (var entry in JObject.Parse(text).SelectToken("data.activities") as JArray ?? Empty)
            {
                var id = (string)entry["id"];
                if (id == null || !(entry["data"] is JObject data)) continue;
                var ward = new Ward { Id = id };
                // All of them are paid in gold; any other currency would need its own purse here.
                Tiers(data["negative_quirk_cost_upgrades"], ward, ward.Remove, "cost_currency.amount");
                Tiers(data["permanent_negative_quirk_cost_upgrades"], ward, ward.RemoveLocked, "cost_currency.amount");
                Tiers(data["positive_quirk_cost_upgrades"], ward, ward.Lock, "cost_currency.amount");
                Tiers(data["disease_quirk_cost_upgrades"], ward, ward.Cure, "cost_currency.amount");
                Tiers(data["disease_quirk_cure_all_chance_upgrades"], ward, ward.CureAll, "chance");
                Tiers(data["quirk_treatment_upgrades"], ward, ward.Success, "chance");
                Tiers(data["slot_upgrades"], ward, ward.Slots, "number_of_slots");
                foreach (var tier in ward.Slots) ward.MaxSlots = Math.Max(ward.MaxSlots, (int)tier.Value);
                if (ward.TreatsQuirks || ward.TreatsDiseases) into.Add(ward);
            }
        }

        // A list without `upgrade_tree_id` belongs to the tree named after its activity, as in the other buildings.
        private static void Tiers(JToken array, Ward ward, List<Tier> into, string valuePath)
        {
            foreach (var entry in array as JArray ?? Empty)
            {
                var value = (float?)entry.SelectToken(valuePath);
                if (value == null) continue;
                into.Add(new Tier
                {
                    Tree = (string)entry["upgrade_tree_id"] ?? Building + "." + ward.Id,
                    Code = (string)entry["upgrade_requirement_code"],
                    Value = value.Value
                });
            }
        }
    }
}
