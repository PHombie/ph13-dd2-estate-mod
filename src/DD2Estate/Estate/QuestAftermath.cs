using System;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.Quirk;
using Assets.Code.Source;
using DD2Estate.Core;
using DD2Estate.Dungeon;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What a hero comes home changed by. DD1 rolls at the end of every quest, for each hero of the party who
    /// lived (the roster's raid-finish roll; docs/recon/dd1-raid-results.md 4.7): a negative quirk and a
    /// positive one, each on a chance that runs with the hero's stress and differs by whether the quest was
    /// completed, and a disease for heroes of resolve level 2 and up, the less likely the better they resist
    /// it. The chances are DD1's, read from shared/rules.json (<see cref="RaidResultRules"/>).
    ///
    /// The quirks themselves are DD2's, drawn the way the Stage Coach draws a recruit's
    /// (QuirkContainer.AddRandomQuirks by DD2's "positive", "negative" and "disease" tags): DD2 leaves out
    /// what the hero has already or cannot take, and when a hero is full it pushes an unlocked old quirk out
    /// for the new one, as DD1 does. REMAPPING: DD1's stress of 0..100 on its scale of 200 is the lower half
    /// of DD2's 0..10; DD1's disease resistance is DD2's resistance to "disease".
    /// </summary>
    internal static class QuestAftermath
    {
        public enum Kind { Positive, Negative, Disease }

        /// <summary>One thing a hero came home with, in DD2's words.</summary>
        public class Gain
        {
            public Kind Kind;
            public string Id, Name, Description;
        }

        private const string DiseaseResist = "disease";     // DD2's key of the resistance (ActorStatType.RESISTANCE)

        private static RaidResultRules _rules;

        /// <summary>DD1's numbers of the roll (its stock values when the install cannot be read).</summary>
        public static RaidResultRules Rules => _rules ?? (_rules = RaidResultRules.Load(new Dd1Files()));

        /// <summary>DD1's 0..100 of stress as a share, from DD2's 0..10: half of DD2's scale is DD1's 100.</summary>
        public static double StressShare(ActorInstance hero)
        {
            return hero == null ? 0 : Mathf.Clamp01(hero.Stress / Mathf.Max(1f, hero.StressMax) * 2f);
        }

        /// <summary>The hero's resistance to disease, 0..1; 0 when the game has none for them.</summary>
        public static double Resistance(ActorInstance hero)
        {
            try
            {
                var values = hero.GetClampedSubstatValues(ActorStatType.RESISTANCE);
                return values != null && values.TryGetValue(DiseaseResist, out var value) ? Mathf.Clamp01(value) : 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// Rolls for one living hero and gives them what the dice say. Returns what they really got, in the
        /// order DD1's plate lists it: the game may have nothing left to give of a kind, and takes a disease
        /// back at once from a hero who carries a curse.
        /// </summary>
        public static List<Gain> Roll(ActorInstance hero, bool completed, Rng rng)
        {
            var gains = new List<Gain>();
            var quirks = hero != null && hero.IsLiving ? hero.QuirkContainer : null;
            if (quirks == null || !quirks.IsEnabled) return gains;
            var stress = StressShare(hero);
            var level = Resolve.Level(hero.ActorGuid);
            var resist = Resistance(hero);
            var roll = Rules.Roll(completed, stress, level, resist, rng);
            if (roll.Positive) Add(hero, quirks, QuirkDefinition.QUIRK_POSITIVE_TAG, gains);
            if (roll.Negative) Add(hero, quirks, QuirkDefinition.QUIRK_NEGATIVE_TAG, gains);
            if (roll.Disease) Add(hero, quirks, QuirkDefinition.QUIRK_DISEASE_TAG, gains);
            if (gains.Count > RaidResultRules.QuirksPerHeroLimit) gains.RemoveRange(RaidResultRules.QuirksPerHeroLimit, gains.Count - RaidResultRules.QuirksPerHeroLimit);
            Plugin.Log.LogInfo("Quest aftermath: " + hero.ActorName + " (stress " + (stress * 100).ToString("0") + "/100, resolve " + level + ", disease resistance " + (resist * 100).ToString("0")
                               + "%): rolled" + (roll.Positive ? " positive" : "") + (roll.Negative ? " negative" : "") + (roll.Disease ? " disease" : "") + (roll.Count == 0 ? " nothing" : "")
                               + "; got " + (gains.Count > 0 ? string.Join(", ", gains.ConvertAll(g => g.Id)) : "nothing"));
            return gains;
        }

        private static void Add(ActorInstance hero, QuirkContainer quirks, string tag, List<Gain> gains)
        {
            try
            {
                var before = new HashSet<string>(quirks.GetIds());
                quirks.AddRandomQuirks(null, tag, 1, SourceType.QUIRK, null, 0u);
                foreach (var quirk in quirks.GetInstances())
                {
                    var definition = quirk?.Definition;
                    if (definition == null || before.Contains(definition.Id)) continue;
                    gains.Add(Describe(definition, hero));
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Quest aftermath: " + hero.ActorName + " could not be given a " + tag + " quirk: " + e); }
        }

        /// <summary>A quirk of DD2's as the results screen lists it.</summary>
        public static Gain Describe(QuirkDefinition definition, ActorInstance hero)
        {
            return new Gain
            {
                Kind = definition.IsDisease ? Kind.Disease : definition.IsPositive ? Kind.Positive : Kind.Negative,
                Id = definition.Id,
                Name = StripTags(SanitariumRules.QuirkName(definition, hero)),
                Description = SanitariumRules.QuirkText(definition, hero)
            };
        }

        /// <summary>Whether a hero carries a disease (DD1 marks the portrait).</summary>
        public static bool Diseased(ActorInstance hero)
        {
            var quirks = hero?.QuirkContainer;
            if (quirks == null) return false;
            try
            {
                foreach (var quirk in quirks.GetInstances())
                    if (quirk?.Definition != null && quirk.Definition.IsDisease && !quirk.Definition.IsCurse) return true;
            }
            catch (Exception) { /* a hero without quirks is not diseased */ }
            return false;
        }

        private static string StripTags(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Trim();
        }

        /// <summary>Dev bridge: the chances a hero would be rolled on, without rolling.</summary>
        public static object Chances(ActorInstance hero, bool completed)
        {
            var stress = StressShare(hero);
            var level = Resolve.Level(hero.ActorGuid);
            var resist = Resistance(hero);
            return new
            {
                hero = hero.ActorName, stress, level, diseaseResist = resist,
                negative = Rules.QuirkChance(false, completed, stress), positive = Rules.QuirkChance(true, completed, stress), disease = Rules.DiseaseChance(level, resist)
            };
        }
    }
}
