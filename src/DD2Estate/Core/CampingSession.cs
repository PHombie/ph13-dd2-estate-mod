using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    public enum CampPhase { Meal, Skills, Over }

    /// <summary>A party member as the camp rules see them. The game layer keeps the flags current.</summary>
    public sealed class CampHero
    {
        public uint Id;
        public string ClassId = "";
        public bool Alive = true;
        /// <summary>DD1 "religious": the class's tag.</summary>
        public bool Religious;
        /// <summary>DD1 "afflicted".</summary>
        public bool Afflicted;
        /// <summary>DD1 "has_deaths_door_recovery_buffs": the lasting mark of a brush with death.</summary>
        public bool Mortality;
        /// <summary>The camping skills the hero has ready, in their own order.</summary>
        public List<string> Skills = new List<string>();
    }

    /// <summary>One thing the camp does to a hero or to the party, in DD1 units, for the game layer to apply.</summary>
    public sealed class CampAction
    {
        /// <summary>Who it lands on. For the party-wide types (ambush chance, torch, loot) the hero who caused it.</summary>
        public uint Hero;
        /// <summary>Who used the skill; 0 for the meal.</summary>
        public uint Source;
        /// <summary>Null for the meal.</summary>
        public string SkillId;
        /// <summary>A <see cref="CampEffectTypes"/> value.</summary>
        public string Type = "";
        public string SubType = "";
        public double Amount;
        public CampBuffDef Buff;

        public override string ToString() => Type + (SubType.Length > 0 ? ":" + SubType : "") + " " + Amount.ToString("0.###", CultureInfo.InvariantCulture) + " -> " + Hero;
    }

    /// <summary>How the night went.</summary>
    public sealed class CampNight
    {
        public bool Ambushed;
        /// <summary>The roll's chance, after everything that lowered it.</summary>
        public double AmbushChance;
        public bool PartySurprised, MonstersSurprised;
    }

    /// <summary>
    /// One camp by DD1's rules, with no presentation in it: the meal first, then respite points spent on the
    /// heroes' camping skills, then sleep and the roll for a night ambush. Every call returns what it did to
    /// the party as <see cref="CampAction"/>s; the game layer turns them into health, stress and buffs. The
    /// whole state goes to JSON and back, so a camp survives the expedition's save.
    /// </summary>
    public sealed class CampSession
    {
        private readonly CampingRules _rules;
        private readonly List<CampHero> _party;
        private readonly Dictionary<string, int> _uses = new Dictionary<string, int>();
        private Rng _rng;
        private double _ambushReduction, _partySurprise, _monstersSurprise;

        public CampPhase Phase { get; private set; }
        public int Points { get; private set; }
        /// <summary>The meal the party chose; null until it has.</summary>
        public string Meal { get; private set; }
        public IReadOnlyList<CampHero> Party => _party;

        public CampSession(CampingRules rules, IEnumerable<CampHero> party, long seed)
        {
            _rules = rules;
            _party = party.ToList();
            _rng = new Rng(seed);
            Points = rules.StartPoints;
        }

        /// <summary>Respite points from outside the camp's own rules (DD1's Outsiders Bonfire, a district building); call before anything is spent.</summary>
        public void AddPoints(int points) => Points = Math.Max(0, Points + points);

        public CampHero Hero(uint id) => _party.FirstOrDefault(h => h.Id == id);

        private List<CampHero> Living => _party.Where(h => h.Alive).ToList();

        // ---- the meal --------------------------------------------------------------------------------

        /// <summary>Food the meal takes for the heroes at the fire.</summary>
        public int FoodFor(MealOption meal) => _rules.FoodFor(meal, Living.Count);

        /// <summary>Why the party cannot have this meal; null when it can.</summary>
        public string MealRefusal(string type, int foodCarried)
        {
            if (Phase != CampPhase.Meal) return "The party has eaten.";
            var meal = _rules.Meal(type);
            if (meal == null) return "No such meal.";
            return FoodFor(meal) > foodCarried ? "Not enough food." : null;
        }

        /// <summary>
        /// The party eats (or goes without). Returns what the meal does to every hero; the caller takes
        /// <see cref="FoodFor"/> out of the bag. Empty when the meal is refused.
        /// </summary>
        public List<CampAction> Eat(string type, int foodCarried)
        {
            var actions = new List<CampAction>();
            if (MealRefusal(type, foodCarried) != null) return actions;
            var meal = _rules.Meal(type);
            Meal = type;
            Phase = CampPhase.Skills;
            foreach (var hero in Living)
            {
                if (meal.Healing > 0) actions.Add(new CampAction { Hero = hero.Id, Type = CampEffectTypes.HealthHeal, Amount = meal.Healing });
                else if (meal.Healing < 0) actions.Add(new CampAction { Hero = hero.Id, Type = CampEffectTypes.HealthDamage, Amount = -meal.Healing });
                var stress = meal.Stress - _rules.RelieveStress;
                if (stress > 0) actions.Add(new CampAction { Hero = hero.Id, Type = CampEffectTypes.StressDamage, Amount = stress });
                else if (stress < 0) actions.Add(new CampAction { Hero = hero.Id, Type = CampEffectTypes.StressHeal, Amount = -stress });
            }
            return actions;
        }

        // ---- skills ----------------------------------------------------------------------------------

        private static string UseKey(uint hero, string skillId) => hero.ToString(CultureInfo.InvariantCulture) + ":" + skillId;

        public int UsesLeft(uint hero, string skillId)
        {
            var skill = _rules.Skill(skillId);
            if (skill == null) return 0;
            _uses.TryGetValue(UseKey(hero, skillId), out var used);
            return Math.Max(0, skill.UseLimit - used);
        }

        /// <summary>The companions a skill of this hero may be pointed at.</summary>
        public List<CampHero> Companions(uint user) => Living.Where(h => h.Id != user).ToList();

        /// <summary>Why the hero cannot use the skill (on that companion) now; null when they can.</summary>
        public string Refusal(uint user, string skillId, uint target = 0u)
        {
            if (Phase == CampPhase.Meal) return "The party eats first.";
            if (Phase == CampPhase.Over) return "The camp is over.";
            var hero = Hero(user);
            if (hero == null || !hero.Alive) return "No such hero at the fire.";
            var skill = _rules.Skill(skillId);
            if (skill == null || !hero.Skills.Contains(skillId)) return "Not a skill they have ready.";
            if (UsesLeft(user, skillId) <= 0) return "Already used this skill.";
            if (skill.Cost > Points) return "Not enough respite points.";
            if (!skill.NeedsTarget) return null;
            if (Companions(user).Count == 0) return "No companion to turn to.";
            if (target == 0u) return "Choose a companion.";
            var chosen = Hero(target);
            return chosen == null || !chosen.Alive || target == user ? "Choose a companion." : null;
        }

        /// <summary>
        /// Uses a camping skill. Effects that share a chance code are the outcomes of one roll, made for every
        /// hero the roll can land on (DD1 behaviour, not in data: per hero rather than once for all); an
        /// effect with a requirement only counts for a hero who meets it. Empty when the skill is refused.
        /// </summary>
        public List<CampAction> Use(uint user, string skillId, uint target = 0u)
        {
            var actions = new List<CampAction>();
            if (Refusal(user, skillId, target) != null) return actions;
            var skill = _rules.Skill(skillId);
            Points -= skill.Cost;
            _uses.TryGetValue(UseKey(user, skillId), out var used);
            _uses[UseKey(user, skillId)] = used + 1;

            var codes = new List<string>();
            foreach (var effect in skill.Effects)
                if (!codes.Contains(effect.Code)) codes.Add(effect.Code);
            foreach (var code in codes)
            {
                var group = skill.Effects.Where(e => e.Code == code).ToList();
                foreach (var hero in Living)
                {
                    var open = group.Where(e => LandsOn(e, hero, user, target) && Meets(e, hero)).ToList();
                    if (open.Count == 0) continue;
                    var roll = _rng.NextDouble();
                    foreach (var effect in open)
                    {
                        roll -= effect.Chance;
                        if (roll >= 0) continue;
                        Apply(effect, hero, user, skillId, actions);
                        break;
                    }
                }
            }
            return actions;
        }

        private static bool LandsOn(CampEffect effect, CampHero hero, uint user, uint target)
        {
            switch (effect.Selection)
            {
                case CampSelections.Self: return hero.Id == user;
                case CampSelections.Individual: return hero.Id == target;
                case CampSelections.PartyOther: return hero.Id != user;
                case CampSelections.Party: return true;
                default: return false;
            }
        }

        private static bool Meets(CampEffect effect, CampHero hero)
        {
            foreach (var requirement in effect.Requirements)
            {
                switch (requirement)
                {
                    case CampRequirements.Afflicted: if (!hero.Afflicted) return false; break;
                    case CampRequirements.Religious: if (!hero.Religious) return false; break;
                    case CampRequirements.NotReligious: if (hero.Religious) return false; break;
                    case CampRequirements.Mortality: if (!hero.Mortality) return false; break;
                    // a requirement this reader does not know is never met: better no effect than a wrong one
                    default: return false;
                }
            }
            return true;
        }

        private void Apply(CampEffect effect, CampHero hero, uint user, string skillId, List<CampAction> actions)
        {
            switch (effect.Type)
            {
                case CampEffectTypes.ReduceAmbush:
                    _ambushReduction += effect.Amount;
                    break;
                case CampEffectTypes.RefreshUses:
                    foreach (var key in _uses.Keys.Where(k => k.StartsWith(UseKey(hero.Id, ""), StringComparison.Ordinal)).ToList()) _uses.Remove(key);
                    break;
                case CampEffectTypes.Buff:
                    // the night's own roll hears of these at once; the fights after it hear from the game layer
                    if (effect.Buff != null && effect.Buff.StatType == "party_surprise_chance") _partySurprise += effect.Amount;
                    if (effect.Buff != null && effect.Buff.StatType == "monsters_surprise_chance") _monstersSurprise += effect.Amount;
                    break;
            }
            actions.Add(new CampAction
            {
                Hero = hero.Id, Source = user, SkillId = skillId,
                Type = effect.Type, SubType = effect.SubType, Amount = effect.Amount, Buff = effect.Buff
            });
        }

        // ---- the night -------------------------------------------------------------------------------

        /// <summary>Chance of a night ambush as things stand: DD1's base chance less what the skills used took off it.</summary>
        public double AmbushChance => Math.Max(0, Math.Min(1, _rules.AmbushChance - _ambushReduction));

        /// <summary>Why the party cannot turn in yet; null when it can.</summary>
        public string SleepRefusal => Phase == CampPhase.Meal ? "The party eats first." : Phase == CampPhase.Over ? "The camp is over." : null;

        /// <summary>
        /// The party sleeps: the camp is over, and the night is rolled. In an ambush DD1 has the party
        /// surprised at its own base chance (1 in the stock rules), moved by the surprise buffs handed out at
        /// this camp; the monsters are surprised only if the party is not. Null when refused.
        /// </summary>
        public CampNight Sleep()
        {
            if (SleepRefusal != null) return null;
            Phase = CampPhase.Over;
            var night = new CampNight { AmbushChance = AmbushChance };
            night.Ambushed = _rng.Chance(night.AmbushChance);
            if (!night.Ambushed) return night;
            night.PartySurprised = _rng.Chance(Math.Max(0, Math.Min(1, _rules.AmbushPartySurprise + _partySurprise)));
            night.MonstersSurprised = !night.PartySurprised && _rng.Chance(Math.Max(0, Math.Min(1, _rules.AmbushMonstersSurprise + _monstersSurprise)));
            return night;
        }

        // ---- save ------------------------------------------------------------------------------------

        public JObject ToJson()
        {
            var uses = new JObject();
            foreach (var pair in _uses) uses[pair.Key] = pair.Value;
            return new JObject
            {
                ["phase"] = Phase.ToString(),
                ["points"] = Points,
                ["meal"] = Meal,
                ["uses"] = uses,
                ["ambushReduction"] = _ambushReduction,
                ["partySurprise"] = _partySurprise,
                ["monstersSurprise"] = _monstersSurprise,
                // a 64-bit state does not survive as a JSON number
                ["rng"] = _rng.State.ToString(CultureInfo.InvariantCulture)
            };
        }

        /// <summary>A saved camp. The party is the game layer's to supply again: who sits at the fire is not the camp's to remember.</summary>
        public static CampSession FromJson(JToken json, CampingRules rules, IEnumerable<CampHero> party)
        {
            var session = new CampSession(rules, party, 0)
            {
                Phase = Json.Enum(json?["phase"], CampPhase.Meal),
                Points = Json.Int(json?["points"], rules.StartPoints),
                Meal = (string)json?["meal"],
                _ambushReduction = Json.Number(json?["ambushReduction"], 0),
                _partySurprise = Json.Number(json?["partySurprise"], 0),
                _monstersSurprise = Json.Number(json?["monstersSurprise"], 0)
            };
            if (json?["uses"] is JObject uses)
                foreach (var p in uses.Properties()) session._uses[p.Name] = Json.Int(p.Value, 0);
            if (ulong.TryParse((string)json?["rng"], NumberStyles.None, CultureInfo.InvariantCulture, out var state)) session._rng = Rng.FromState(state);
            return session;
        }
    }
}
