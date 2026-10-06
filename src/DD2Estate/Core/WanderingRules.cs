using System;
using System.Collections.Generic;
using System.Globalization;

namespace DD2Estate.Core
{
    /// <summary>
    /// One line of a DD1 conditional mash (<c>dungeons/&lt;d&gt;/&lt;d&gt;.conditional.&lt;level&gt;.mash.darkest</c>):
    /// a fight that may take a hallway fight's place while its conditions hold.
    /// <code>hall: .chance 0.03 .types collector_A .inventory_valid_item_percent_range 0.79 1.0 .limit 1 .can_be_ambush false</code>
    /// </summary>
    public sealed class ConditionalMash
    {
        /// <summary>The block's name: "hall" in every file DD1 ships.</summary>
        public string Where = "hall";
        public double Chance;
        /// <summary>DD1 monster classes of the fight, front to back (collector_A).</summary>
        public List<string> Types = new List<string>();
        /// <summary><c>.darkness_range</c>: the torchlight the line asks for, both ends included; null when it asks for none.</summary>
        public double[] Light;
        /// <summary><c>.inventory_valid_item_percent_range</c>: the share of the bag that has to be taken, both ends included; null when it asks for none.</summary>
        public double[] BagShare;
        /// <summary><c>.limit</c>: how often the fight may come in one expedition; 0: as often as the dice say.</summary>
        public int Limit;
        /// <summary><c>.can_be_ambush</c>.</summary>
        public bool CanBeAmbush = true;

        /// <summary>The monster the line is about, without DD1's level letter: "collector" for collector_A, _B and _C.</summary>
        public string Monster
        {
            get
            {
                if (Types.Count == 0) return "";
                var type = Types[0];
                var cut = type.LastIndexOf('_');
                return cut > 0 && cut == type.Length - 2 && char.IsUpper(type[cut + 1]) ? type.Substring(0, cut) : type;
            }
        }

        /// <summary>Whether the line's conditions hold: the torchlight (0..100) and the share of the bag's slots that are taken (0..1).</summary>
        public bool Holds(double light, double bagShare)
        {
            if (Light != null && (light < Light[0] || light > Light[1])) return false;
            if (BagShare != null && (bagShare < BagShare[0] || bagShare > BagShare[1])) return false;
            return true;
        }

        public override string ToString()
        {
            var text = Where + " " + string.Join("+", Types) + " " + Chance.ToString("0.###", CultureInfo.InvariantCulture);
            if (Light != null) text += " light " + Light[0].ToString("0.##", CultureInfo.InvariantCulture) + ".." + Light[1].ToString("0.##", CultureInfo.InvariantCulture);
            if (BagShare != null) text += " bag " + BagShare[0].ToString("0.##", CultureInfo.InvariantCulture) + ".." + BagShare[1].ToString("0.##", CultureInfo.InvariantCulture);
            return text + (Limit > 0 ? " limit " + Limit : "");
        }
    }

    /// <summary>
    /// DD1's wandering bosses of one dungeon at one difficulty, read from the install: the Collector while the
    /// bag is nearly full, the Shambler in the dark. Every number is the file's; a dungeon or a level without a
    /// file has no wanderers (the Darkest Dungeon has none). How the lines are used is DD1's own code (read
    /// in its executable, 0x9a2130 and the line's test at 0x8928d0):
    ///
    ///   the roll is made as a battle is created on a tile, among the lines of that kind of tile ("hall:" for
    ///   a hallway; DD1 ships no "room:" line), and its answer stays on the tile: a party that flees finds the
    ///   same wanderer there again;
    ///   a line is valid while the torch is inside its <c>.darkness_range</c> (both ends included: "0 0" is a
    ///   torch that is out), the share of the bag's slots that hold anything is inside its
    ///   <c>.inventory_valid_item_percent_range</c> (whatever the slots hold), it has come fewer than
    ///   <c>.limit</c> times on this expedition, and, for <c>.can_be_ambush false</c>, the battle is not a
    ///   night ambush;
    ///   ONE roll among the valid lines, each weighing its <c>.chance</c>, "nobody" weighing what is left of 1.
    /// </summary>
    public sealed class WanderingRules
    {
        public readonly List<ConditionalMash> Lines = new List<ConditionalMash>();
        /// <summary>The file the lines were read from; null when DD1 has none for the dungeon and level.</summary>
        public string Source;

        public static string FileOf(string dungeonId, int level) => "dungeons/" + dungeonId + "/" + dungeonId + ".conditional." + level + ".mash.darkest";

        /// <param name="level">DD1's dungeon level of the quest: 1, 3 or 5.</param>
        public static WanderingRules Load(IDd1Files files, string dungeonId, int level)
        {
            var rules = new WanderingRules();
            var file = FileOf(dungeonId, level);
            var text = files != null && dungeonId != null ? files.ReadText(file) : null;
            if (text == null) return rules;
            rules.Source = file;
            rules.Lines.AddRange(Parse(text));
            return rules;
        }

        public static List<ConditionalMash> Parse(string text)
        {
            var lines = new List<ConditionalMash>();
            foreach (var block in DarkestFile.Parse(text))
            {
                var line = new ConditionalMash
                {
                    Where = block.Name,
                    Chance = block.Number("chance", 0, 0),
                    Limit = Math.Max(0, block.Int("limit", 0, 0)),
                    CanBeAmbush = !string.Equals(block.Text("can_be_ambush", 0, "true"), "false", StringComparison.OrdinalIgnoreCase)
                };
                line.Types.AddRange(block.All("types"));
                if (block.Fields.ContainsKey("darkness_range")) line.Light = Range(block, "darkness_range");
                if (block.Fields.ContainsKey("inventory_valid_item_percent_range")) line.BagShare = Range(block, "inventory_valid_item_percent_range");
                if (line.Types.Count > 0) lines.Add(line);
            }
            return lines;
        }

        private static double[] Range(DarkestBlock block, string field)
        {
            var low = block.Number(field, 0, 0);
            return new[] { low, block.Number(field, 1, low) };
        }

        /// <summary>The line about a monster ("collector", "shambler"); null when the dungeon has none.</summary>
        public ConditionalMash Find(string monster)
        {
            foreach (var line in Lines)
                if (line.Monster == monster) return line;
            return null;
        }

        /// <summary>Whether a line may still come: under its limit by what the expedition has met so far.</summary>
        public static bool Open(ConditionalMash line, IDictionary<string, int> met)
        {
            if (line.Limit <= 0 || met == null) return true;
            return !met.TryGetValue(line.Monster, out var times) || times < line.Limit;
        }

        /// <summary>The lines that may come now: of this kind of place, their conditions holding, their limit not used up; in the file's order.</summary>
        public List<ConditionalMash> Valid(double light, double bagShare, IDictionary<string, int> met, string where = "hall")
        {
            var valid = new List<ConditionalMash>();
            foreach (var line in Lines)
                if (line.Where == where && line.Chance > 0 && line.Holds(light, bagShare) && Open(line, met)) valid.Add(line);
            return valid;
        }

        /// <summary>
        /// A fight is about to begin (DD1 rolls as it makes the battle, exe 0x9a2130): the wanderer that takes
        /// its place, or null. ONE roll: every valid line weighs its <c>.chance</c>, in the file's order, and
        /// "nobody" weighs what is left of 1. One draw of the dice whatever comes of it.
        /// </summary>
        public ConditionalMash Roll(double light, double bagShare, IDictionary<string, int> met, Rng rng, string where = "hall")
        {
            var valid = Valid(light, bagShare, met, where);
            double sum = 0;
            foreach (var line in valid) sum += line.Chance;
            var roll = rng.NextDouble() * Math.Max(1, sum);
            foreach (var line in valid)
            {
                if (roll < line.Chance) return line;
                roll -= line.Chance;
            }
            return null;
        }

        /// <summary>The chance that some wanderer comes at the next fight of this kind of place as things stand.</summary>
        public double ChanceNow(double light, double bagShare, IDictionary<string, int> met, string where = "hall")
        {
            double sum = 0;
            foreach (var line in Valid(light, bagShare, met, where)) sum += line.Chance;
            return Math.Min(1, sum);
        }
    }
}
