using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Glossary for the estate: the red book on the estate's bar and the terms it holds. The terms and
    /// what they mean are DD1's own words, read from the player's install (str_glossary_term_N and
    /// str_glossary_term_definition_N in localization/*.string_table.xml and in the DLCs' tables); the mod
    /// carries none of them and changes none.
    ///
    /// No file of DD1 lists the terms or their order. Both are read out of its exe (SharedUI::Glossary):
    /// - the numbers are tried in blocks of a thousand (0.., 1000.., 2000..: the base game's terms, then each
    ///   DLC's). A block ends after ten numbers in a row without a term, the search after three blocks in a
    ///   row without any;
    /// - the terms are then sorted by their names in small letters, byte by byte (tolower and strncmp): the
    ///   book is alphabetical in English, whatever number a term was given.
    ///
    /// DD1's glossary explains DD1's rules and the estate's fights are DD2's, so not every term is shown: see
    /// the lists of numbers below.
    /// </summary>
    [EstateModule]
    internal static class Glossary
    {
        public const string Id = "glossary";
        // DD1's estate bar shows the book of fx/estate_glossary (a Spine sprite): shut, and open while open.
        private const string IconSheet = "fx/estate_glossary/estate_glossary.sprite.png";

        private const string MainTable = "miscellaneous.string_table.xml";
        private const string TermId = "str_glossary_term_", DefinitionId = "str_glossary_term_definition_";
        private static readonly string[] Prefixes = { "str_glossary_" };
        // DD1's search for term numbers (its exe; see above).
        private const int BlockSize = 1000, BlockGap = 10, EmptyBlocks = 3;

        // ---- which of DD1's terms hold here --------------------------------------------------------------
        //
        // A term is shown only when all DD1 says about it is true in the estate. To show or hide one, move
        // its number (the N of str_glossary_term_N) to another list; `glossary.state` on the dev bridge names
        // every term with its number and its list. A number in no list is not shown.

        // True as written: the estate and its currencies, a hero's gear and quirks, provisions and the
        // corridor as the mod has them, and what a fight of DD2 has in common with one of DD1. Districts
        // stand here ahead of their time: the estate is getting them.
        private static readonly int[] Shown =
        {
            2, 26, 28, 10, 14, 20, 36, 43, 1003,        // Activity Log, Gold, Heirloom, Bust, Crest, Deed, Portrait, Roster, Districts
            24, 54, 39, 42,                             // Equipment, Weapon, Quirk, Resolve XP
            38, 25, 44, 45, 50, 51, 35, 58, 11,         // Provision, Food, Shovel, Skeleton Key, Torch, Torchlight, Obstacle, Curio, Camping Skill
            12, 22, 15, 30, 31, 17, 16, 9, 18,          // Combat Skill, DMG, CRIT, HP, MAX HP, Death's Door, Death Blow, Buff, Debuff
            48, 33, 40, 49, 34, 19, 60, 61              // Stun, Move (effect), Resistance, Stun / Move / Debuff Resistance, Battle Limit, Stealth
        };

        // Rules of DD1 that the estate does not have: DD2 knows no accuracy, dodge or protection rating
        // (tokens instead); its stress ends at 10 in a meltdown or a moment of resolve, with no affliction to
        // carry home and no heart attack; the mod's chance to disarm a trap is one number for every hero.
        private static readonly int[] Dd1Rules =
        {
            1, 23, 62,                                  // ACC, DODGE, Armor Piercing
            3, 53, 55, 57, 27, 47,                      // Affliction, Virtue, Affliction Check, Affliction History, Heart Attack, Stress
            52                                          // Trap Resistance
        };

        // Terms of DD1's DLCs: the Crimson Court's courtyard, the Butcher's Circus, and the rules of the two
        // classes of The Fire's Edge.
        private static readonly int[] Dlc =
        {
            1001, 1005, 1006,                                               // Crimson Curse, The Blood, Set Bonus
            2001, 2002, 2003, 2004, 2005, 2006, 2007, 2008, 2009, 2010,     // Daze, Restoration, Prestige, Rank, Tier, League, The Circus, The Ring, Challenge, Banner
            3001, 3002, 4001, 4002                                          // Burn, Wildfire!, Combat Start Riposte, Stances
        };

        // Nearly true: one sentence of DD1's does not hold here, or could not be checked against DD2. Left
        // out until the owner says; DD1's words are not rewritten to fit.
        private static readonly int[] Undecided =
        {
            4, 6, 32, 29, 1002,     // Antivenom, Bandage, Medicinal Herbs, Holy Water, Laudanum: on a hero they give health or calm here (Core/InventoryRaidRules.cs)
            7, 8,                   // Bleed, Blight: nothing ticks on a hallway step and no supply cures them (a DD2 hero carries none out of a fight)
            59,                     // Horror: DD2 has a stress DOT; whether it shows it under this name is not known
            5, 46,                  // Armor (names DODGE), SPD ("1-8" is DD1's die; DD2's is in its data)
            37,                     // PROT: true of the hero panel's row (DD2's share of a blow not taken), which is not DD1's rating
            13, 21,                 // Corpse (DD2 leaves corpses too), Disease (cured by camping skills and the Sanitarium; by a combat skill not known)
            41, 56,                 // Resolve Level (no stress rules by level here), Surprise Check (only camp buffs and night ambushes surprise here)
            1004                    // Blueprint: goes in with the Districts
        };

        /// <summary>Why a term is shown or not.</summary>
        public enum Group { Shown, Dd1Rules, Dlc, Undecided, Unlisted }

        public class Term
        {
            /// <summary>The N of str_glossary_term_N.</summary>
            public int Number;
            /// <summary>DD1's words; the definition with DD1's colour marks as rich text.</summary>
            public string Name, Definition;
            public Group Group;
            /// <summary>What DD1 sorts by: the name as written, in small letters.</summary>
            internal string Key;
        }

        private static Dictionary<int, Group> _groups;
        private static Dictionary<string, string> _elsewhere;
        private static List<Term> _terms;

        private static void Register()
        {
            // DD1's row of the estate's bar, from the roster's end: candles, book, chest, scroll. The book is drawn
            // at 0.84 of its sheet with its middle 2.5 px under the row's place (measured on a real DD1 frame: the
            // book's middle stands at 1690, 1005.5, the place is 1690, 1003).
            TownPanelButtons.Add(new TownPanelButtons.Entry
            {
                Id = Id, Art = IconSheet + "#book_closed", OpenArt = IconSheet + "#book_open", Scale = 0.84f, Offset = new Vector2(0f, 2.5f),
                Size = new Vector2(84f, 91f), Place = 1,
                Name = () => ScreenName, Toggle = GlossaryPanel.Toggle, IsOpen = () => GlossaryPanel.IsOpen
            });
        }

        public static string ScreenName => WindowText.Plain("town_name_glossary") ?? "Glossary";

        /// <summary>The Ancestor's line over the list; empty without a DD1 install.</summary>
        public static string Quote => Dd1Strings.Plain(Dd1Strings.Get("str_glossary_ancestor_quote"));

        // ---- the terms -----------------------------------------------------------------------------------

        /// <summary>Every term DD1's own glossary would list, in DD1's order.</summary>
        public static List<Term> All()
        {
            if (_terms == null) _terms = Read();
            return _terms;
        }

        /// <summary>The terms of the estate's glossary, in DD1's order.</summary>
        public static List<Term> Terms()
        {
            var shown = new List<Term>();
            foreach (var term in All())
                if (term.Group == Group.Shown) shown.Add(term);
            return shown;
        }

        public static Group GroupOf(int number)
        {
            if (_groups == null)
            {
                _groups = new Dictionary<int, Group>();
                // a number in two lists belongs to the first of them: shown wins
                Put(Group.Shown, Shown);
                Put(Group.Dd1Rules, Dd1Rules);
                Put(Group.Dlc, Dlc);
                Put(Group.Undecided, Undecided);
            }
            return _groups.TryGetValue(number, out var group) ? group : Group.Unlisted;
        }

        private static void Put(Group group, int[] numbers)
        {
            foreach (var number in numbers)
                if (!_groups.ContainsKey(number)) _groups[number] = group;
        }

        private static List<Term> Read()
        {
            var terms = new List<Term>();
            var seen = new HashSet<int>();
            var empty = 0;
            for (var block = 0; empty < EmptyBlocks; block++)
            {
                var found = false;
                var misses = 0;
                for (var i = 0; misses < BlockGap; i++)
                {
                    var number = block * BlockSize + i;
                    var name = Text(TermId + number);
                    if (string.IsNullOrEmpty(name))
                    {
                        misses++;
                        continue;
                    }
                    misses = 0;
                    found = true;
                    // a block that runs on into the next one meets its terms twice
                    if (!seen.Add(number)) continue;
                    var term = new Term
                    {
                        Number = number, Name = Dd1Strings.Format(name), Definition = Rich(Text(DefinitionId + number)), Group = GroupOf(number), Key = Lower(name)
                    };
                    if (term.Group == Group.Unlisted) Plugin.Log.LogInfo("Glossary: DD1's term " + number + " (" + term.Name + ") is in none of the lists and is not shown");
                    terms.Add(term);
                }
                empty = found ? 0 : empty + 1;
            }
            terms.Sort((a, b) =>
            {
                var by = string.CompareOrdinal(a.Key, b.Key);
                return by != 0 ? by : a.Number.CompareTo(b.Number);
            });
            return terms;
        }

        // C's tolower as DD1 calls it: the letters A to Z and nothing else.
        private static string Lower(string text)
        {
            var lower = new StringBuilder(text.Length);
            foreach (var c in text) lower.Append(c >= 'A' && c <= 'Z' ? (char)(c + 32) : c);
            return lower.ToString();
        }

        // DD1's marks in a text: "{colour_start|id}" and "{colour_end}" become TextMeshPro colour tags (the DLCs'
        // terms name their DLC in colour), any other "{...}" is a note for DD1's translators. Line breaks stay.
        private static string Rich(string text)
        {
            if (text == null) return "";
            var rich = new StringBuilder(text.Length + 32);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var close = c == '{' ? text.IndexOf('}', i) : -1;
                if (close > i)
                {
                    var mark = text.Substring(i + 1, close - i - 1);
                    if (mark.StartsWith("colour_start|", StringComparison.Ordinal)) rich.Append("<color=").Append(ActivityLogText.Hex(mark.Substring("colour_start|".Length))).Append('>');
                    else if (mark == "colour_end") rich.Append("</color>");
                    i = close;
                }
                else if (c != '\r') rich.Append(c);
            }
            return rich.ToString().Trim();
        }

        // ---- DD1's tables --------------------------------------------------------------------------------

        // The main table is read whole by Dd1Strings. The Butcher's Circus keeps its terms in another table of
        // the same folder, the other DLCs in tables of their own folders (dlc/<id>/localization, and
        // dlc/<id>/features/<feature>/localization for The Fire's Edge).
        private static string Text(string id)
        {
            var text = Dd1Strings.Get(id);
            if (text != null) return text;
            if (_elsewhere == null) _elsewhere = ReadElsewhere();
            return _elsewhere.TryGetValue(id, out text) ? text : null;
        }

        private static Dictionary<string, string> ReadElsewhere()
        {
            var texts = new Dictionary<string, string>();
            var files = new List<string>();
            try
            {
                var tables = Dd1Install.PathOf("localization");
                if (tables != null && Directory.Exists(tables)) files.AddRange(Directory.GetFiles(tables, "*.string_table.xml"));
                var dlc = Dd1Install.PathOf("dlc");
                if (dlc != null && Directory.Exists(dlc)) files.AddRange(Directory.GetFiles(dlc, "*.string_table.xml", SearchOption.AllDirectories));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Glossary: DD1's string tables could not be listed: " + e.Message); }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (string.Equals(Path.GetFileName(file), MainTable, StringComparison.OrdinalIgnoreCase)) continue;
                try { Dd1Strings.ReadEnglish(File.ReadLines(file), texts, Prefixes); }
                catch (Exception e) { Plugin.Log.LogWarning("Glossary: " + Path.GetFileName(file) + " could not be read: " + e.Message); }
            }
            return texts;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        /// <summary>
        /// For tests and for the owner's review: the terms shown, in DD1's order, and those left out by list.
        /// <paramref name="withText"/> adds DD1's definitions.
        /// </summary>
        public static object Describe(bool withText)
        {
            var lists = new Dictionary<Group, List<object>>();
            foreach (Group group in Enum.GetValues(typeof(Group))) lists[group] = new List<object>();
            var all = All();
            foreach (var term in all)
                lists[term.Group].Add(withText ? (object)new { number = term.Number, term = term.Name, definition = term.Definition } : new { number = term.Number, term = term.Name });
            return new
            {
                name = ScreenName, dd1Terms = all.Count, shown = lists[Group.Shown],
                leftOut = new { dd1Rules = lists[Group.Dd1Rules], dlc = lists[Group.Dlc], undecided = lists[Group.Undecided], unlisted = lists[Group.Unlisted] }
            };
        }
    }
}
