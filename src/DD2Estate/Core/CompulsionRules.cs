using System;
using System.Collections.Generic;

namespace DD2Estate.Core
{
    /// <summary>
    /// What a DD1 quirk makes its bearer do at curios (<c>shared/quirk/quirk_library.json</c>): the kind of
    /// curio that draws them, how likely they are to reach for it unasked, and whether they keep what they find.
    /// </summary>
    public sealed class Compulsion
    {
        /// <summary>DD1 quirk id (kleptomaniac).</summary>
        public string Quirk;
        /// <summary><c>curio_tag</c>: a tag of <c>curios/curio_type_library.csv</c> (Treasure, Unholy, Worship ..., or All, which nearly every curio lists).</summary>
        public string Tag;
        /// <summary><c>curio_tag_chance</c>.</summary>
        public double Chance;
        /// <summary><c>keep_loot</c>: the loot of a curio they opened this way is theirs, not the party's.</summary>
        public bool KeepLoot;

        public override string ToString() => Quirk + " " + Tag + " " + Chance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + (KeepLoot ? " keeps the loot" : "");
    }

    /// <summary>A hero's compulsions, in the order of the hero's own list of quirks.</summary>
    public sealed class HeroCompulsions
    {
        /// <summary>The game layer's name for the hero (a guid).</summary>
        public uint Hero;
        public List<Compulsion> Compulsions = new List<Compulsion>();
    }

    /// <summary>The hero who reaches for a curio unasked, and the quirk that made them.</summary>
    public sealed class CompulsionCandidate
    {
        public uint Hero;
        public Compulsion Compulsion;
    }

    /// <summary>
    /// DD1's quirk compulsions at curios. The numbers are the install's quirk library (every quirk that names
    /// a <c>curio_tag</c>); how they are rolled is DD1's own code (read in its executable, 0xafec20):
    ///
    ///   when the party steps onto a hallway tile that holds a curio (a room's curio never rolls),
    ///   every hero in the party's order goes through their quirks in order: each quirk whose tag is one of
    ///   the curio's own rolls its <c>curio_tag_chance</c>, and the hero's first success puts them on a list;
    ///   one of the list acts: the curio is used at once, with bare hands, and nobody is asked.
    ///
    /// DD1 picks from that list with <c>floor(rand * (count - 1))</c>: of two or more the last never acts.
    /// It is DD1's rule as it runs, and is kept. The three <c>curioTriggerChance_*</c> of rules.json are read
    /// by DD1 and never used. Which heroes carry which quirks is the game layer's business (DD2's quirks are
    /// its own: Data/quirk_compulsions.json says which DD2 quirk stands for which DD1 one).
    /// </summary>
    public sealed class CompulsionRules
    {
        public const string QuirkFile = "shared/quirk/quirk_library.json";

        private readonly Dictionary<string, Compulsion> _byQuirk = new Dictionary<string, Compulsion>();

        public IEnumerable<Compulsion> All => _byQuirk.Values;

        public static CompulsionRules Load(IDd1Files files)
        {
            var rules = new CompulsionRules();
            var raw = Json.ParseFile(files.ReadText(QuirkFile));
            foreach (var quirk in Json.Array(raw?["quirks"]))
            {
                var id = (string)quirk["id"];
                var tag = (string)quirk["curio_tag"];
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(tag)) continue;
                rules._byQuirk[id] = new Compulsion
                {
                    Quirk = id,
                    Tag = tag,
                    Chance = Json.Number(quirk["curio_tag_chance"], 0),
                    KeepLoot = Json.Bool(quirk["keep_loot"], false)
                };
            }
            return rules;
        }

        /// <summary>The compulsion of a DD1 quirk; null for a quirk without one.</summary>
        public Compulsion Of(string dd1Quirk) => dd1Quirk != null && _byQuirk.TryGetValue(dd1Quirk, out var compulsion) ? compulsion : null;

        /// <summary>
        /// Whether a curio with these tags draws the bearer of a compulsion: its tag is one of the curio's.
        /// "All" is a tag like any other: a curio that does not list it (DD1's altar_of_light) draws nobody by it.
        /// </summary>
        public static bool Draws(Compulsion compulsion, ICollection<string> curioTags)
        {
            if (compulsion == null || string.IsNullOrEmpty(compulsion.Tag) || curioTags == null) return false;
            foreach (var tag in curioTags)
                if (string.Equals(tag, compulsion.Tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The heroes a curio draws this time: each one's first quirk, in their order, that fits the curio and comes up on its chance.</summary>
        public static List<CompulsionCandidate> Drawn(IEnumerable<HeroCompulsions> party, ICollection<string> curioTags, Rng rng)
        {
            var drawn = new List<CompulsionCandidate>();
            if (party == null) return drawn;
            foreach (var hero in party)
            {
                if (hero?.Compulsions == null) continue;
                foreach (var compulsion in hero.Compulsions)
                {
                    if (!Draws(compulsion, curioTags)) continue;
                    if (!(compulsion.Chance > rng.NextDouble())) continue;
                    drawn.Add(new CompulsionCandidate { Hero = hero.Hero, Compulsion = compulsion });
                    break;
                }
            }
            return drawn;
        }

        /// <summary>DD1's pick among the drawn: <c>floor(rand * (count - 1))</c>, so the last of several never acts.</summary>
        public static CompulsionCandidate Pick(IList<CompulsionCandidate> drawn, Rng rng)
        {
            if (drawn == null || drawn.Count == 0) return null;
            var index = (int)Math.Floor(rng.NextDouble() * (drawn.Count - 1));
            return drawn[Math.Max(0, Math.Min(drawn.Count - 1, index))];
        }

        /// <summary>The party steps onto a hallway tile with a curio: who, if anybody, reaches for it unasked.</summary>
        public static CompulsionCandidate Roll(IEnumerable<HeroCompulsions> party, ICollection<string> curioTags, Rng rng)
        {
            return Pick(Drawn(party, curioTags, rng), rng);
        }
    }
}
