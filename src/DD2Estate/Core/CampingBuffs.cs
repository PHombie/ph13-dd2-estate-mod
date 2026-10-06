using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Core
{
    /// <summary>What a DD1 camping buff becomes in the Estate.</summary>
    public enum CampBuffKind
    {
        /// <summary>No DD2 counterpart: the buff is not given.</summary>
        None,
        /// <summary>Lines of a DD2 stats container on the hero.</summary>
        Stat,
        /// <summary>One of DD2's own effect containers on the hero (<see cref="CampingBuffMap.EffectsOf"/>).</summary>
        Effect,
        /// <summary>The expedition's scouting chance.</summary>
        Scouting,
        /// <summary>The chance the party is caught off guard.</summary>
        PartySurprise,
        /// <summary>The chance the enemies are.</summary>
        MonstersSurprise
    }

    /// <summary>One line of a DD2 stats container, as DD2's own tables write them (StatDataContainer.SetFromCsv).</summary>
    public sealed class Dd2StatLine
    {
        /// <summary>add_stat, multiply_stat or sub_stat.</summary>
        public string Kind = "add_stat";
        public string Stat = "";
        /// <summary>The sub-stat key of a sub_stat line (bleed, stress...); null otherwise.</summary>
        public string Sub;
        public double Value;

        public string Key => Kind + "," + Stat + (Sub != null ? "," + Sub : "");
    }

    /// <summary>
    /// THE MOD'S OWN MAPPING of DD1's camping buffs onto DD2's hero stats. The amounts stay DD1's; what they
    /// are amounts of is the nearest thing a DD2 hero has. DD2 stat names are the game's own ids
    /// (ActorStatType): docs/recon/camping.md lists where each is read.
    ///
    /// - Damage (damage_low and damage_high, which DD1 always raises together) is damage dealt: each end
    ///   counts half.
    /// - DD2 has no accuracy and no dodge: a blow lands unless a token says otherwise. DD1's ACC is given as
    ///   that share more damage dealt (the blows that would have missed), DODGE and PROT as that share less
    ///   damage taken. The Blacksmith reads DD1's dodge the same way.
    /// - Less stress taken is stress resistance: DD2 resists a stress hit whole, at that chance.
    /// - Resistances map one to one (DD1's poison is DD2's blight).
    /// - A buff under a rule (melee or ranged blows only, large monsters only, the first round only, while a
    ///   riposte is up) cannot be asked of a DD2 stats container, which holds always. It is given at
    ///   <see cref="ConditionalShare"/> of its amount, always. The one rule the Estate can answer is the hero's
    ///   rank: it is read when a fight starts, and the buff counts in full or not at all.
    /// - A buff that is no number at all but an extra effect on every hit has a counterpart only where DD2
    ///   has the same trick as data of its own (<see cref="Dd2Effects"/>).
    /// </summary>
    public static class CampingBuffMap
    {
        /// <summary>The share of a rule-bound buff that is given unconditionally: a hero whose blows meet the rule half the time gets the same on average.</summary>
        public const double ConditionalShare = 0.5;

        // DD2 ActorStatType ids
        public const string DamageDealt = "health_damage_dealt_percent";
        public const string DamageReceived = "health_damage_received_percent";
        public const string CritChance = "crit_chance";
        public const string Speed = "speed";
        public const string HealthMax = "health_max";
        public const string HealReceived = "health_heal_received_percent";
        public const string Resistance = "resistance";

        // DD1 resistance sub types -> DD2 resistance sub-stat keys
        private static readonly Dictionary<string, string> Resistances = new Dictionary<string, string>
        {
            { "bleed", "bleed" }, { "poison", "blight" }, { "disease", "disease" }, { "move", "move" },
            { "debuff", "debuff" }, { "stun", "stun" }, { "death_blow", "death" }
        };

        /// <summary>
        /// DD1 buffs that are DD2 effect containers (ActorDataEffects ids of DD2's buff table), by DD1 buff id.
        /// The runaway's Play with Fire has a companion's blows set their target alight, which is what her
        /// Firestarter does in DD2 (runaway_firestarter_burn_buff: on hit, a small burn on the target).
        /// </summary>
        public static readonly Dictionary<string, string> Dd2Effects = new Dictionary<string, string>
        {
            { "rw_play_with_fire_burn_on_hit", "runaway_firestarter_burn_buff" }
        };

        /// <summary>The DD2 effects container a DD1 buff stands for; null when it is not one.</summary>
        public static string EffectsOf(CampBuffDef buff)
        {
            return buff != null && Dd2Effects.TryGetValue(buff.Id, out var id) ? id : null;
        }

        public static CampBuffKind KindOf(CampBuffDef buff)
        {
            if (buff == null) return CampBuffKind.None;
            if (EffectsOf(buff) != null) return CampBuffKind.Effect;
            switch (buff.StatType)
            {
                case "scouting_chance": return CampBuffKind.Scouting;
                case "party_surprise_chance": return CampBuffKind.PartySurprise;
                case "monsters_surprise_chance": return CampBuffKind.MonstersSurprise;
                default: return Lines(buff, 1, -1).Count > 0 ? CampBuffKind.Stat : CampBuffKind.None;
            }
        }

        /// <summary>
        /// How much of the buff counts: all of it when it has no rule, all or nothing for a rank rule once the
        /// rank is known (<paramref name="rank"/> 0 is the front; -1: not known), half otherwise.
        /// </summary>
        public static double Share(CampBuffDef buff, int rank)
        {
            if (buff == null || !buff.Conditional) return 1;
            if (buff.RuleType == "in_rank" && rank >= 0) return (rank == (int)Math.Round(buff.RuleNumber)) != buff.RuleFalse ? 1 : 0;
            return ConditionalShare;
        }

        /// <summary>The DD2 stat lines a DD1 buff of this amount is worth to a hero standing at this rank; empty when it has no counterpart.</summary>
        public static List<Dd2StatLine> Lines(CampBuffDef buff, double amount, int rank)
        {
            var lines = new List<Dd2StatLine>();
            if (buff == null) return lines;
            var value = amount * Share(buff, rank);
            switch (buff.StatType)
            {
                case "combat_stat_add":
                    switch (buff.SubType)
                    {
                        case "attack_rating": lines.Add(Add(DamageDealt, value)); break;
                        case "crit_chance": lines.Add(Add(CritChance, value)); break;
                        case "defense_rating":
                        case "protection_rating": lines.Add(Add(DamageReceived, -value)); break;
                        // DD2 speed is whole points
                        case "speed_rating": lines.Add(Add(Speed, Math.Round(value, MidpointRounding.AwayFromZero))); break;
                    }
                    break;
                case "combat_stat_multiply":
                    switch (buff.SubType)
                    {
                        case "damage_low":
                        case "damage_high": lines.Add(Add(DamageDealt, value / 2)); break;
                        case "max_hp": lines.Add(new Dd2StatLine { Kind = "multiply_stat", Stat = HealthMax, Value = value }); break;
                    }
                    break;
                case "stress_dmg_received_percent":
                    lines.Add(Sub(Resistance, "stress", -value));
                    break;
                case "resistance":
                    if (Resistances.TryGetValue(buff.SubType, out var key)) lines.Add(Sub(Resistance, key, value));
                    break;
                case "hp_heal_received_percent":
                    lines.Add(Add(HealReceived, value));
                    break;
                case "damage_received_percent":
                    lines.Add(Add(DamageReceived, value));
                    break;
            }
            lines.RemoveAll(l => Math.Abs(l.Value) < 1e-9);
            return lines;
        }

        private static Dd2StatLine Add(string stat, double value) => new Dd2StatLine { Stat = stat, Value = value };

        private static Dd2StatLine Sub(string stat, string sub, double value) => new Dd2StatLine { Kind = "sub_stat", Stat = stat, Sub = sub, Value = value };

        /// <summary>Lines of the same stat added up and written as DD2's tables write them, one per line.</summary>
        public static string Text(IEnumerable<Dd2StatLine> lines)
        {
            var sums = new List<Dd2StatLine>();
            foreach (var line in lines)
            {
                var sum = sums.FirstOrDefault(s => s.Key == line.Key);
                if (sum == null) sums.Add(sum = new Dd2StatLine { Kind = line.Kind, Stat = line.Stat, Sub = line.Sub });
                sum.Value += line.Value;
            }
            var text = new StringBuilder();
            foreach (var sum in sums)
            {
                if (Math.Abs(sum.Value) < 0.0001) continue;
                text.Append(sum.Key).Append(',').Append(sum.Value.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            }
            return text.ToString();
        }
    }

    /// <summary>One camping buff a hero carries.</summary>
    public sealed class CampBuffEntry
    {
        public uint Hero;
        public string SkillId = "";
        public string BuffId = "";
        public double Amount;
    }

    /// <summary>
    /// The camping buffs an expedition's heroes carry, by DD1's terms: a buff lasts the number of fights
    /// DD1's buff file gives it (duration_type combat_end, duration 4) and ends sooner when the party camps
    /// again ("until next Camp", as DD1's own tooltip has it) or goes home. Goes to JSON with the expedition.
    /// </summary>
    public sealed class CampBuffLedger
    {
        private readonly List<CampBuffEntry> _entries = new List<CampBuffEntry>();

        /// <summary>FALLBACK for a buff whose file gives no duration: DD1's stock camping buffs all last 4 fights.</summary>
        public const int DefaultFights = 4;

        public IReadOnlyList<CampBuffEntry> Entries => _entries;
        /// <summary>Fights the buffs still cover; 0: none are in force.</summary>
        public int FightsLeft { get; private set; }

        public bool Any => _entries.Count > 0 && FightsLeft > 0;

        /// <summary>A hero gains a buff at camp. The count of fights starts over: all of a camp's buffs run out together.</summary>
        public void Add(uint hero, string skillId, CampBuffDef buff, double amount)
        {
            if (buff == null) return;
            _entries.Add(new CampBuffEntry { Hero = hero, SkillId = skillId ?? "", BuffId = buff.Id, Amount = amount });
            FightsLeft = Math.Max(FightsLeft, buff.DurationType == "combat_end" && buff.Duration > 0 ? buff.Duration : DefaultFights);
        }

        /// <summary>A new camp, or the way home.</summary>
        public void Clear()
        {
            _entries.Clear();
            FightsLeft = 0;
        }

        /// <summary>A fight is over. True when that was the last one the buffs covered.</summary>
        public bool FightOver()
        {
            if (_entries.Count == 0) return false;
            FightsLeft = Math.Max(0, FightsLeft - 1);
            if (FightsLeft > 0) return false;
            _entries.Clear();
            return true;
        }

        /// <summary>The fallen carry nothing.</summary>
        public void Forget(uint hero) => _entries.RemoveAll(e => e.Hero == hero);

        /// <summary>Sum of the buffs of one of the party-wide kinds.</summary>
        public double Total(CampingRules rules, CampBuffKind kind)
        {
            if (!Any) return 0;
            return _entries.Where(e => CampingBuffMap.KindOf(rules.Buff(e.BuffId)) == kind).Sum(e => e.Amount);
        }

        /// <summary>A hero's stat buffs as the text of a DD2 stats container; empty when they have none.</summary>
        public string StatsText(CampingRules rules, uint hero, int rank)
        {
            if (!Any) return "";
            var lines = new List<Dd2StatLine>();
            foreach (var entry in _entries)
                if (entry.Hero == hero) lines.AddRange(CampingBuffMap.Lines(rules.Buff(entry.BuffId), entry.Amount, rank));
            return CampingBuffMap.Text(lines);
        }

        /// <summary>The DD2 effect containers a hero's buffs stand for (ids, each once); empty when none.</summary>
        public List<string> EffectIds(CampingRules rules, uint hero)
        {
            var ids = new List<string>();
            if (!Any) return ids;
            foreach (var entry in _entries)
            {
                var id = entry.Hero == hero ? CampingBuffMap.EffectsOf(rules.Buff(entry.BuffId)) : null;
                if (id != null && !ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        public JObject ToJson()
        {
            var entries = new JArray();
            foreach (var e in _entries)
                entries.Add(new JObject { ["hero"] = e.Hero, ["skill"] = e.SkillId, ["buff"] = e.BuffId, ["amount"] = e.Amount });
            return new JObject { ["fights"] = FightsLeft, ["entries"] = entries };
        }

        public static CampBuffLedger FromJson(JToken json)
        {
            var ledger = new CampBuffLedger { FightsLeft = Json.Int(json?["fights"], 0) };
            foreach (var e in Json.Array(json?["entries"]))
            {
                var buff = (string)e["buff"];
                if (string.IsNullOrEmpty(buff)) continue;
                ledger._entries.Add(new CampBuffEntry
                {
                    Hero = (uint)Json.Long(e["hero"], 0), SkillId = (string)e["skill"] ?? "", BuffId = buff, Amount = Json.Number(e["amount"], 0)
                });
            }
            return ledger;
        }
    }

    /// <summary>
    /// The camping skills one hero knows and which of them are ready for an expedition, by DD1's rules: a
    /// recruit arrives with a few, the Survivalist teaches the rest, and only so many can be ready at a time
    /// (<see cref="CampingRules.ActiveLimit"/>).
    /// </summary>
    public sealed class CampSkillBook
    {
        public string ClassId = "";
        public readonly List<string> Known = new List<string>();
        public readonly List<string> Active = new List<string>();

        /// <summary>A recruit's book: DD1's roll of starting skills, all of them ready.</summary>
        public static CampSkillBook Roll(CampingRules rules, string classId, Rng rng)
        {
            var book = new CampSkillBook { ClassId = classId ?? "" };
            book.Known.AddRange(rules.RollStartingSkills(classId, rng));
            foreach (var id in book.Known)
                if (book.Active.Count < rules.ActiveLimit) book.Active.Add(id);
            return book;
        }

        public bool Knows(string id) => Known.Contains(id);

        public bool IsActive(string id) => Active.Contains(id);

        /// <summary>Why the hero cannot learn the skill; null when they can. The price is the caller's.</summary>
        public string LearnRefusal(CampingRules rules, string id)
        {
            if (rules.Skill(id) == null || rules.SkillsFor(ClassId).All(s => s.Id != id)) return "Not a skill of their class.";
            return Knows(id) ? "Already known." : null;
        }

        /// <summary>Learns a skill; it is ready at once while a place is free. Null, or why not.</summary>
        public string Learn(CampingRules rules, string id)
        {
            var refusal = LearnRefusal(rules, id);
            if (refusal != null) return refusal;
            Known.Add(id);
            if (Active.Count < rules.ActiveLimit) Active.Add(id);
            return null;
        }

        /// <summary>Makes a known skill ready, or puts a ready one aside. Null, or why not.</summary>
        public string Toggle(CampingRules rules, string id)
        {
            if (!Knows(id)) return "Not learned yet.";
            if (Active.Remove(id)) return null;
            if (Active.Count >= rules.ActiveLimit) return "Only " + rules.ActiveLimit + " camping skills can be ready at a time.";
            Active.Add(id);
            return null;
        }

        /// <summary>Drops what the install no longer has (a DLC removed) and keeps the ready skills within the limit.</summary>
        public void Repair(CampingRules rules)
        {
            var open = rules.SkillsFor(ClassId).Select(s => s.Id).ToList();
            Known.RemoveAll(id => !open.Contains(id));
            Active.RemoveAll(id => !Known.Contains(id));
            while (Active.Count > rules.ActiveLimit) Active.RemoveAt(Active.Count - 1);
        }

        public JObject ToJson()
        {
            return new JObject { ["cls"] = ClassId, ["known"] = new JArray(Known), ["active"] = new JArray(Active) };
        }

        public static CampSkillBook FromJson(JToken json)
        {
            var book = new CampSkillBook { ClassId = (string)json?["cls"] ?? "" };
            foreach (var id in Json.Array(json?["known"]))
                if ((string)id != null && !book.Known.Contains((string)id)) book.Known.Add((string)id);
            foreach (var id in Json.Array(json?["active"]))
                if ((string)id != null && book.Known.Contains((string)id) && !book.Active.Contains((string)id)) book.Active.Add((string)id);
            return book;
        }
    }
}
