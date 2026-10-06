using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Actor;
using Assets.Code.Library;
using Assets.Code.Quirk;
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
    /// One camp of an expedition as the game plays it: DD1's rules (<see cref="CampSession"/>) with their
    /// effects turned into what a DD2 hero has, shown at DD2's own rest stop (<see cref="CampView"/>) under the
    /// camp screen (<see cref="CampScreen"/>). The dungeon run owns it: a camp exists from the moment the
    /// firewood is lit until the party has slept, and travels in the expedition's save in between.
    ///
    /// DD1's camping effects on a DD2 hero (docs/recon/camping.md has the game calls behind each):
    /// - stress: DD1's 0..200 on the hero's own scale, the fraction rolled (as the Tavern does);
    /// - health: shares of the hero's maximum; what a camp takes never kills (DD1 leaves a starving hero at
    ///   death's door, a DD2 hero out of a fight has no such state: one point of health is left);
    /// - bleed and blight cures: DD2's own removal of those marks, which a DD2 hero does not carry out of a
    ///   fight, so in practice the line does nothing;
    /// - disease cure: every disease quirk goes, the way the Sanitarium takes one;
    /// - "mortality debuffs" (the mark DD1 leaves on a hero pulled back from death's door): DD2's Kingdoms
    ///   wound, which a hero takes at death's door there;
    /// - buffs: DD2 stats for DD1's four fights (<see cref="CampBuffs"/>);
    /// - "afflicted": a hero at half their stress or more, the point where DD1 afflicts.
    /// </summary>
    internal sealed class Camp
    {
        /// <summary>The share of a hero's stress scale at which DD1 afflicts (100 of 200).</summary>
        public const float AfflictedShare = 0.5f;
        private const int LogSize = 40;

        private readonly List<string> _log = new List<string>();
        // the heroes a skill being applied has given a buff, and whether it helps them
        private readonly Dictionary<uint, bool> _buffed = new Dictionary<uint, bool>();

        public DungeonRun Run { get; }
        public CampSession Session { get; }
        /// <summary>The hero whose skills the screen shows.</summary>
        public uint Selected;
        /// <summary>A skill picked that still wants a companion; null when none.</summary>
        public string Armed;
        /// <summary>The party has turned in: the night is being played out.</summary>
        public bool Closing { get; private set; }

        public static Camp Current => DungeonRun.Current?.Camp;

        public IReadOnlyList<string> Log => _log;

        private static CampingRules Rules => CampContent.Rules;

        private Camp(DungeonRun run, CampSession session)
        {
            Run = run;
            Session = session;
            foreach (var hero in session.Party)
                if (hero.Alive && Selected == 0u) Selected = hero.Id;
        }

        // ---- a camp begins ---------------------------------------------------------------------------

        internal static Camp Begin(DungeonRun run)
        {
            var camp = new Camp(run, new CampSession(Rules, Heroes(), (long)run.Dice.NextU64()));
            camp.Say("The fire is lit. First, the meal.");
            // DD1's Outsiders Bonfire (a district building): respite points for a party with one of its heroes in it.
            var outsiders = Districts.CampPoints();
            if (outsiders > 0)
            {
                camp.Session.AddPoints(outsiders);
                camp.Say("The outsiders know how to make a fire last: " + outsiders + " more respite points.");
            }
            camp.Open();
            return camp;
        }

        /// <summary>A camp out of the expedition's save; null when there was none in progress.</summary>
        internal static Camp Restore(DungeonRun run, JToken json)
        {
            if (!(json is JObject saved) || saved["session"] == null) return null;
            var session = CampSession.FromJson(saved["session"], Rules, Heroes());
            if (session.Phase == CampPhase.Over) return null;
            var camp = new Camp(run, session);
            foreach (var line in Json.Array(saved["log"]))
                if ((string)line != null) camp._log.Add((string)line);
            return camp;
        }

        public JObject ToJson()
        {
            return new JObject { ["session"] = Session.ToJson(), ["log"] = new JArray(_log) };
        }

        /// <summary>Puts the camp on screen: the corridor gives way to the rest stop, the camp screen goes over the HUD.</summary>
        internal void Open()
        {
            CorridorView.Suspend();
            CampView.Show();
            CampScreen.Open(this);
        }

        // The party as the rules see it. Which skills a hero has ready is settled before setting out (the
        // Survivalist's book) and does not change in the dungeon.
        private static List<CampHero> Heroes()
        {
            var heroes = new List<CampHero>();
            foreach (var actor in Provisioning.Party())
            {
                var cls = Survivalist.ClassOf(actor);
                var hero = new CampHero { Id = actor.ActorGuid, ClassId = cls, Religious = Rules.IsReligious(cls) };
                hero.Skills.AddRange(Survivalist.Book(actor).Active);
                Read(hero, actor);
                heroes.Add(hero);
            }
            return heroes;
        }

        private static void Read(CampHero hero, ActorInstance actor)
        {
            hero.Alive = actor != null && actor.IsLiving;
            if (!hero.Alive) return;
            hero.Afflicted = actor.Stress >= actor.StressMax * AfflictedShare;
            hero.Mortality = actor.IsWounded;
        }

        private static ActorInstance Actor(uint guid)
        {
            return SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
        }

        private void Refresh()
        {
            foreach (var hero in Session.Party) Read(hero, Actor(hero.Id));
        }

        public static string Name(uint guid)
        {
            var actor = Actor(guid);
            return actor != null ? actor.ActorName : "someone";
        }

        private void Say(string line)
        {
            _log.Add(line);
            if (_log.Count > LogSize) _log.RemoveAt(0);
            Run.CampSay(line);
        }

        // ---- the meal --------------------------------------------------------------------------------

        public int FoodCarried => Run.Food;

        /// <summary>Why the party cannot have this meal; null when it can.</summary>
        public string MealRefusal(string type) => Closing ? "The party has turned in." : Session.MealRefusal(type, FoodCarried);

        /// <summary>The party eats. Returns null, or why not.</summary>
        public string Eat(string type)
        {
            Refresh();
            var refusal = MealRefusal(type);
            if (refusal != null) return refusal;
            var meal = Rules.Meal(type);
            var food = Session.FoodFor(meal);
            var actions = Session.Eat(type, FoodCarried);
            // DD1's Granary (a district building): more of the meal's healing, as of anything eaten.
            foreach (var action in actions)
                if (action.Type == CampEffectTypes.HealthHeal) action.Amount *= Districts.EatFactor;
            Run.TakeFood(food);
            var changes = Apply(actions);
            // those who ate, as a hero given food at DD2's inn
            if (food > 0) CampHeroes.Used(Touched(actions), true);
            Say((food > 0 ? "The party eats " + food + " food (" + CampText.MealName(meal).ToLowerInvariant() + " rations)" : "The party goes without a meal") + changes + ".");
            return null;
        }

        // ---- skills ----------------------------------------------------------------------------------

        /// <summary>Why the hero cannot use the skill (on that companion) now; null when they can.</summary>
        public string Refusal(uint user, string skillId, uint target = 0u)
        {
            if (Closing) return "The party has turned in.";
            Refresh();
            return Session.Refusal(user, skillId, target);
        }

        /// <summary>The hero uses a camping skill. Returns null, or why not.</summary>
        public string Use(uint user, string skillId, uint target = 0u)
        {
            var refusal = Refusal(user, skillId, target);
            if (refusal != null) return refusal;
            var skill = Rules.Skill(skillId);
            if (!skill.NeedsTarget) target = 0u;
            var actions = Session.Use(user, skillId, target);
            var changes = Apply(actions);
            Armed = null;
            // those it was for, as a hero given an item at DD2's inn
            var touched = Touched(actions);
            if (touched.Count == 0) touched.Add(target != 0u ? target : user);
            CampHeroes.Used(touched, false);
            Say(Name(user) + ": " + CampText.Name(skill) + (target != 0u ? " for " + Name(target) : "") + changes + ".");
            return null;
        }

        private static HashSet<uint> Touched(List<CampAction> actions)
        {
            var heroes = new HashSet<uint>();
            foreach (var action in actions) heroes.Add(action.Hero);
            return heroes;
        }

        // ---- the night -------------------------------------------------------------------------------

        /// <summary>
        /// The party turns in. Returns null, or why not. The dev bridge may order the night instead of having
        /// it rolled (<paramref name="forceAmbush"/>, and with an ambush whether the party is surprised).
        /// </summary>
        public string Sleep(bool? forceAmbush = null, bool? forceSurprised = null)
        {
            if (Closing) return "The party has turned in.";
            var refusal = Session.SleepRefusal;
            if (refusal != null) return refusal;
            var night = Session.Sleep();
            if (forceAmbush != null)
            {
                night.Ambushed = forceAmbush.Value;
                night.PartySurprised = night.Ambushed && (forceSurprised ?? true);
                night.MonstersSurprised = false;
            }
            Closing = true;
            Armed = null;
            Plugin.Log.LogInfo("Camp: the party sleeps; ambush chance " + night.AmbushChance.ToString("0.##", CultureInfo.InvariantCulture)
                               + (night.Ambushed ? ", ambushed" + (night.PartySurprised ? ", surprised" : "") : ", a quiet night"));
            Plugin.Host.StartCoroutine(Night(night));
            return null;
        }

        // The rest stop is put away before anything else happens: a fight loads its own scene and spawns its
        // own models of the same heroes.
        private IEnumerator Night(CampNight night)
        {
            // the dark first, so that nothing is seen of the scene going away
            CampScreen.Darken();
            var dark = Time.unscaledTime + CampScreen.FadeTime;
            while (Time.unscaledTime < dark) yield return null;
            yield return CampView.Hide();
            if (DungeonRun.Current == Run) Run.EndCamp(this, night);
            // A night ambush: the fight's arena is loaded at once, a moment before the game's wipe has closed
            // over the screen, and with the rest stop gone and the corridor not up it stood in full view behind
            // the HUD (DD2's night forest, its stagecoach in it). The dark stays until the wipe has closed.
            if (night.Ambushed && DungeonRun.Current == Run && Run.Camp == null && Run.Busy) CampScreen.HoldDark();
            CampScreen.Close();
        }

        // ---- DD1's effects on DD2 heroes --------------------------------------------------------------

        // Applies what the rules decided and says what changed, hero by hero: ": Dismas -1 stress, +4 health".
        private string Apply(List<CampAction> actions)
        {
            var before = new Dictionary<uint, float[]>();
            foreach (var hero in Session.Party)
            {
                var actor = Actor(hero.Id);
                if (actor != null && actor.IsLiving) before[hero.Id] = new[] { actor.HpRounded, actor.Stress };
            }

            var notes = new List<string>();
            _buffed.Clear();
            foreach (var action in actions)
            {
                try { Apply(action, notes); }
                catch (Exception e) { Plugin.Log.LogError("Camp: " + action + " could not be applied: " + e); }
            }
            Run.SyncCampBuffs();
            // DD1 writes "Buff!" over a hero a skill has strengthened (what it heals or costs pops up by itself,
            // RaidFeedback): once a hero, of however many buffs DD1 makes the skill
            foreach (var pair in _buffed) DungeonHud.Pop(pair.Key, pair.Value ? RaidPop.Buff : RaidPop.Debuff);

            var parts = new List<string>();
            foreach (var pair in before)
            {
                var actor = Actor(pair.Key);
                if (actor == null) continue;
                var changes = new List<string>();
                var health = Mathf.RoundToInt(actor.HpRounded - pair.Value[0]);
                var stress = Mathf.RoundToInt(actor.Stress - pair.Value[1]);
                if (health != 0) changes.Add((health > 0 ? "+" : "") + health + " health");
                if (stress != 0) changes.Add((stress > 0 ? "+" : "") + stress + " stress");
                if (changes.Count > 0) parts.Add(actor.ActorName + " " + string.Join(", ", changes));
            }
            parts.AddRange(notes);
            return parts.Count > 0 ? ": " + string.Join("; ", parts) : "";
        }

        private void Apply(CampAction action, List<string> notes)
        {
            var hero = Actor(action.Hero);
            if (hero == null || !hero.IsLiving) return;
            var source = action.SkillId ?? "camp";
            switch (action.Type)
            {
                case CampEffectTypes.StressHeal:
                {
                    var points = CampContent.RollStress(action.Amount, hero.StressMax, Run.Dice);
                    if (points > 0 && hero.Stress > 0f) hero.ApplyStressHeal(points, SourceType.INN);
                    break;
                }
                case CampEffectTypes.StressDamage:
                {
                    var points = CampContent.RollStress(action.Amount, hero.StressMax, Run.Dice);
                    if (points > 0) hero.ApplyStressDamage(points, false, SourceType.STORY, source, 0u);
                    break;
                }
                case CampEffectTypes.HealthHeal:
                    if (hero.HpRounded < hero.CurrentHpMax) hero.ApplyHealthHeal(Mathf.Max(1f, hero.CurrentHpMax * (float)action.Amount), false, SourceType.INN, false);
                    break;
                case CampEffectTypes.HealthDamage:
                {
                    // never the last point: nobody dies of a missed meal
                    var damage = Mathf.Min(hero.HpRounded - 1f, Mathf.Max(1f, hero.CurrentHpMax * (float)action.Amount));
                    if (damage >= 1f) hero.ApplyHealthDamage(damage, false, false, hero, DeathType.EFFECT, SourceType.STORY, source, false);
                    break;
                }
                case CampEffectTypes.RemoveBleed:
                    hero.DotContainer.RemoveAllInstancesWithTypes(new[] { "bleed" }, false, SourceType.INN, source, 0u);
                    break;
                case CampEffectTypes.RemoveBlight:
                    hero.DotContainer.RemoveAllInstancesWithTypes(new[] { "blight" }, false, SourceType.INN, source, 0u);
                    break;
                case CampEffectTypes.RemoveDisease:
                {
                    var cured = CureDiseases(hero);
                    if (cured.Count > 0) notes.Add(hero.ActorName + " is cured of " + string.Join(", ", cured));
                    break;
                }
                case CampEffectTypes.RemoveMortality:
                    if (hero.IsWounded)
                    {
                        hero.ChangeWoundPercent(-hero.WoundPercent, SourceType.INN);
                        notes.Add(hero.ActorName + "'s wounds are mended");
                        // DD1's word for something taken off a hero (a disease says so with its name, RaidFeedback)
                        DungeonHud.Pop(action.Hero, RaidPop.Cured);
                    }
                    break;
                case CampEffectTypes.Buff:
                    if (CampingBuffMap.KindOf(action.Buff) != CampBuffKind.None)
                    {
                        Run.CampLedger.Add(action.Hero, action.SkillId, action.Buff, action.Amount);
                        // a hero is told of a debuff only when nothing else the skill gave them helps
                        var helps = TrayIcons.Helps(action.Buff, action.Amount);
                        _buffed[action.Hero] = helps || (_buffed.TryGetValue(action.Hero, out var helped) && helped);
                    }
                    break;
                case CampEffectTypes.ReduceAmbush:
                    notes.Add(Session.AmbushChance <= 0 ? "the night will be quiet" : "the night is safer");
                    break;
                case CampEffectTypes.ReduceTorch:
                    Run.ChangeLight(-action.Amount);
                    notes.Add("the torch gutters out");
                    break;
                case CampEffectTypes.Loot:
                    notes.Add(Run.CampLoot(action.SubType, Math.Max(1, (int)Math.Round(action.Amount))));
                    break;
                case CampEffectTypes.RefreshUses:
                    notes.Add(hero.ActorName + " is ready to use their skills again");
                    break;
            }
        }

        // DD1 behaviour, not in data (the effect has no amount): a camping cure takes every disease the hero
        // has. The calls are the Sanitarium's (SanitariumLedger), which are DD2's own hospital's.
        private static List<string> CureDiseases(ActorInstance hero)
        {
            var cured = new List<string>();
            var quirks = hero.QuirkContainer;
            if (quirks == null) return cured;
            var found = new List<QuirkInstance>();
            for (var i = 0; i < quirks.GetNumberOfInstances(); i++)
            {
                var quirk = quirks.GetInstanceAtIndex(i);
                if (quirk?.Definition != null && quirk.Definition.IsDisease && !quirk.Definition.IsCurse) found.Add(quirk);
            }
            foreach (var quirk in found)
            {
                cured.Add(SanitariumRules.QuirkName(quirk.Definition, hero));
                quirks.Remove(quirk, SourceType.HOSPITAL, null, 0u);
            }
            return cured;
        }

        // ---- dev bridge ------------------------------------------------------------------------------

        public object Describe()
        {
            Refresh();
            var meals = new List<object>();
            foreach (var meal in Rules.Meals)
                meals.Add(new { type = meal.Type, name = CampText.MealName(meal), food = Session.FoodFor(meal), effect = CampText.MealEffect(meal), refusal = MealRefusal(meal.Type) });
            var heroes = new List<object>();
            foreach (var hero in Session.Party)
            {
                var actor = Actor(hero.Id);
                var skills = new List<object>();
                foreach (var id in hero.Skills)
                {
                    var skill = Rules.Skill(id);
                    if (skill == null) continue;
                    skills.Add(new
                    {
                        id,
                        name = CampText.Name(skill),
                        cost = skill.Cost,
                        usesLeft = Session.UsesLeft(hero.Id, id),
                        needsTarget = skill.NeedsTarget,
                        refusal = skill.NeedsTarget ? Refusal(hero.Id, id, FirstCompanion(hero.Id)) : Refusal(hero.Id, id),
                        effect = string.Join(" | ", CampText.Lines(skill))
                    });
                }
                heroes.Add(new
                {
                    guid = hero.Id,
                    name = actor != null ? actor.ActorName : null,
                    cls = hero.ClassId,
                    alive = hero.Alive,
                    hp = actor != null ? Mathf.RoundToInt(actor.HpRounded) : 0,
                    hpMax = actor != null ? Mathf.RoundToInt(actor.CurrentHpMax) : 0,
                    stress = actor != null ? actor.Stress : 0f,
                    religious = hero.Religious,
                    shaken = hero.Afflicted,
                    wounded = hero.Mortality,
                    buffs = CampBuffs.TextOn(hero.Id).Replace('\n', ';'),
                    skills
                });
            }
            return new
            {
                phase = Session.Phase.ToString().ToLowerInvariant(),
                points = Session.Points,
                meal = Session.Meal,
                food = FoodCarried,
                ambushChance = Session.AmbushChance,
                closing = Closing,
                selected = Selected,
                armed = Armed,
                view = CampView.State,
                meals,
                heroes,
                log = _log
            };
        }

        private uint FirstCompanion(uint user)
        {
            var companions = Session.Companions(user);
            return companions.Count > 0 ? companions[0].Id : 0u;
        }
    }
}
