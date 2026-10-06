using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using DD2Estate.Dungeon;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Estate
{
    /// <summary>
    /// What the estate map says, in DD1's words: names of lengths and difficulties, the headings of the details
    /// panel, a region's progress and the hints of locked regions, the names and descriptions DD1 gives its
    /// generated quests, the questions asked before a party goes on, item names and descriptions (English
    /// section of the player's DD1 string table: town_quest_*, town_name_quest_select,
    /// town_progression_forward_*, str_inventory_*), the party's name (party_names.string_table.xml by
    /// shared/party_name/party_name_library.json) and what a hero says to a quest beneath them
    /// (dialogue.string_table.xml). Story quests bring their own texts from the quest board.
    /// tools/preview_quest_map.py words the lines the same way: change one, change the other.
    /// </summary>
    internal static class QuestMapText
    {
        private const string Table = "localization/miscellaneous.string_table.xml";
        private const string PartyTable = "localization/party_names.string_table.xml";
        private const string DialogueTable = "localization/dialogue.string_table.xml";
        private const string PartyLibrary = "shared/party_name/party_name_library.json";
        private static readonly string[] Prefixes =
        {
            "\"town_quest_", "\"town_name_quest_select\"", "\"town_progression_forward_", "\"trinket_rarity_", "\"str_inventory_title_",
            "\"str_inventory_description_", "\"realm_inventory_trinket_sell", "\"town_name_nomad_wagon\"", "\"town_name_realm_inventory\"",
            "\"str_nomad_wagon_summary\"", "\"str_quest_select_", "\"town_provision_", "\"confirm_ok\""
        };
        private static readonly string[] PartyPrefixes = { "\"party_name_" };
        private static readonly string[] BarkPrefixes = { "\"str_quest_too_easy\"" };
        private static readonly Regex Entry = new Regex("<entry id=\"([^\"]+)\"><!\\[CDATA\\[(.*?)\\]\\]></entry>");
        private static readonly Regex Markup = new Regex("\\{[^}]*\\}");
        private static Dictionary<string, string> _strings, _partyNames, _partyIds;
        private static List<string> _tooEasy;

        /// <summary>The mod's own name of a quest type, used where DD1 has none for this type, length and region.</summary>
        private static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>
        {
            { "explore", "Exploration" }, { "cleanse", "Cleansing" }, { "gather", "Gathering" }, { "activate", "Activation" },
            { "inventory_activate", "Activation" }, { "kill_boss", "Boss" }
        };

        // ---- DD1's words (the quoted text is what DD1 says, kept as a fallback for a missing table) --------

        public static string ScreenName => Dd1("town_name_quest_select") ?? "Estate Map";
        public static string Forward => Dd1("town_progression_forward_provision") ?? "Provision";
        public static string SetOff => Dd1("town_progression_forward_set_off") ?? "Set Off";
        public static string StillEmbark => Dd1("town_quest_select_confirm_yes") ?? "Still Embark";
        public static string CancelEmbark => Dd1("town_quest_select_confirm_no") ?? "Cancel Embark";
        public static string Ok => Dd1("confirm_ok") ?? "OK";
        public static string Goals => Dd1("town_quest_goals") ?? "Goals:";
        public static string Rewards => Dd1("town_quest_rewards") ?? "Rewards";
        public static string LockedHint => Dd1("town_quest_locked") ?? "Explore other regions to unlock this one.";
        public static string DarkestHint => Dd1("town_quest_dungeon_not_released") ?? "Enter this, the most dreaded of regions.";
        public static string SmallParty => Dd1("town_quest_quest_select_small_team_confirm") ?? "Grave danger awaits the underprepared. Do you wish to continue without a full contingent?";
        public static string CannotRetreat => Dd1("town_quest_quest_select_cant_retreat_confirm") ?? "You can not retreat from this quest. Are you sure you want to proceed?";
        public static string DarkestRetreat => Dd1("town_quest_quest_select_darkest_dungeon_confirm")
                                               ?? "If a Darkest Dungeon quest is abandoned, not all heroes are guaranteed to survive. Someone must sacrifice themselves to hold off the fiends during retreat.";
        public static string NoTrinkets => Dd1("town_provision_no_trinkets_equipped") ?? "Your party is not fully outfitted with trinkets. Really embark?";
        public static string PartyNameDefault => PartyNames.TryGetValue("party_name_default", out var text) ? text : "Build a Party From the Roster";
        public static string TownEventTitle => Dd1("str_quest_select_town_event_title") ?? "Town Event:";

        public static string Camps(int camps)
        {
            return Dd1("town_quest_number_of_camps" + camps) ?? (camps == 0 ? "This quest does not involve Camping" : "Heroes will Camp during this quest");
        }

        public static string Length(Quest quest)
        {
            return Dd1("town_quest_length_" + quest.Length) ?? Title(quest.LengthName);
        }

        public static string Difficulty(Quest quest)
        {
            return Dd1("town_quest_difficulty_" + quest.Difficulty) ?? QuestBoard.TierLabel(quest);
        }

        /// <summary>
        /// "Short | Apprentice (Lvl 1)" (rich text). The difficulty is in DD1's colour of that difficulty: its
        /// colour table names one for each under the very id of the word (town_quest_difficulty_1: green,
        /// _3: orange, _5: red, _6: the red of harm).
        /// </summary>
        public static string Specifics(Quest quest)
        {
            var difficulty = Difficulty(quest);
            if (QuestMapColours.TryGet("town_quest_difficulty_" + quest.Difficulty, out var colour))
                difficulty = "<color=#" + ColorUtility.ToHtmlStringRGB(colour) + ">" + difficulty + "</color>";
            var format = Dd1("town_quest_specifics_format") ?? "%s | %s";
            var first = format.IndexOf("%s", StringComparison.Ordinal);
            var second = first < 0 ? -1 : format.IndexOf("%s", first + 2, StringComparison.Ordinal);
            if (second < 0) return Length(quest) + " | " + difficulty;
            return format.Substring(0, first) + Length(quest) + format.Substring(first + 2, second - first - 2) + difficulty + format.Substring(second + 2);
        }

        /// <summary>What DD1 calls a generated quest of this kind ("Skirmish", "Scout"); the mod's word for a story quest.</summary>
        public static string TypeName(Quest quest)
        {
            var name = quest.Plot ? null : Dd1QuestText("name", quest);
            if (name != null) return name;
            return quest.Type != null && TypeNames.TryGetValue(quest.Type, out var own) ? own : Title(quest.Type);
        }

        /// <summary>
        /// The name on the details panel's band: DD1's own of a generated quest (town_quest_name_&lt;type&gt;+&lt;length&gt;+
        /// &lt;dungeon&gt;+&lt;goal&gt;: "Scout", "Cleanse", "Reclaim Relics of the Light"), a story quest's as the quest
        /// board has it.
        /// </summary>
        public static string QuestName(Quest quest)
        {
            return (quest.Plot ? null : Dd1QuestText("name", quest)) ?? quest.Name;
        }

        /// <summary>
        /// A story quest's own narration; for a generated quest the lines DD1 writes for its type, length and
        /// region, or the mod's where DD1 has none (it never generates "activate" quests outside the Darkest
        /// Dungeon; its "inventory_activate" lines speak of quest items such a party does not carry).
        /// </summary>
        public static string Description(Quest quest)
        {
            if (!string.IsNullOrEmpty(quest.Intro)) return quest.Intro;     // upright: DD1's fonts have no italic
            var dd1 = Dd1QuestText("description", quest);
            if (dd1 != null) return dd1;
            return quest.Type != null && Descriptions.TryGetValue(quest.Type, out var own) ? own : "";
        }

        private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            { "explore", "Walk the halls, mark what is there, and come back to tell of it." },
            { "cleanse", "Meet whatever holds the rooms in battle, and leave none of it standing." },
            { "gather", "What the estate lost lies where it fell. Find it and bring it home." },
            { "activate", "Old works stand idle in the dark. Find them and set them going again." },
            { "inventory_activate", "Carry what was entrusted to you to the places that wait for it." },
            { "kill_boss", "Something has made this place its own. Find it and put an end to it." }
        };

        // DD1 keys a generated quest's texts by type+length+dungeon+goal; the goal is whatever the region has.
        private static string Dd1QuestText(string kind, Quest quest)
        {
            if (quest.Type == null || quest.Dungeon == null) return null;
            foreach (var length in new[] { quest.Length, 2, 1, 3 })
            {
                var prefix = "town_quest_" + kind + "_" + quest.Type + "+" + length + "+" + quest.Dungeon + "+";
                string found = null, foundKey = null;
                foreach (var pair in Strings)
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal) && (foundKey == null || string.CompareOrdinal(pair.Key, foundKey) < 0))
                    {
                        foundKey = pair.Key;
                        found = pair.Value;
                    }
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// What a generated quest asks for, in DD1's sentence for the kind of goal ("Explore 90% of rooms.",
        /// "Gather 3 Holy Relics.") filled from the goal DD1 gives the type in the region (quest.types.json).
        /// The mod's own words where DD1's are missing. Curios are named by their id: their titles sit in
        /// another of DD1's tables and read the same but for one.
        /// </summary>
        public static string Goal(string questType, Core.QuestGoal goal)
        {
            if (goal != null)
            {
                var percent = Mathf.RoundToInt((float)(goal.Percentage * 100.0)).ToString(CultureInfo.InvariantCulture);
                switch (goal.Type)
                {
                    case "explore_room":
                        return Fill(Dd1("town_quest_goal_start_plural_explore_room") ?? "Explore %.0f%% of rooms.", percent, null);
                    case "battle_room":
                        return Fill(Dd1("town_quest_goal_start_plural_battle_room") ?? "Complete %.0f%% of room battles.", percent, null);
                    case "gather":
                        var item = Dd1("str_inventory_title_quest_item" + goal.ItemId) ?? Title(goal.ItemId);
                        return Fill(Dd1(goal.Amount == 1 ? "town_quest_goal_start_single_gather" : "town_quest_goal_start_plural_gather") ?? "Gather %d %ss.", goal.Amount.ToString(CultureInfo.InvariantCulture), item);
                    case "activate":
                        return Fill(Dd1(goal.Amount == 1 ? "town_quest_goal_start_single_activate" : "town_quest_goal_start_plural_activate") ?? "Activate %d %ss.", goal.Amount.ToString(CultureInfo.InvariantCulture), Title(goal.CurioId));
                }
            }
            switch (questType)
            {
                case Core.QuestTypes.Cleanse: return "Win every room battle.";
                case Core.QuestTypes.Gather: return "Recover what was left behind.";
                case Core.QuestTypes.Activate:
                case Core.QuestTypes.InventoryActivate: return "Activate the altars.";
                default: return "Explore 90% of the rooms.";
            }
        }

        // DD1's printf pieces: one number (%d, %.0f or %3.0f), "%%" for a percent sign, one name (%s).
        private static string Fill(string format, string number, string name)
        {
            var text = format.Replace("%3.0f", number).Replace("%.0f", number).Replace("%d", number).Replace("%%", "%");
            return name != null ? text.Replace("%s", name) : text;
        }

        /// <summary>DD1's name of a trinket rarity ("Very Rare"; it calls very_common and common both "Common").</summary>
        public static string TrinketRarity(string rarity)
        {
            return Dd1("trinket_rarity_" + rarity) ?? Title(rarity);
        }

        /// <summary>A line of DD1's string table among those this screen and the wagon read; null when it has none.</summary>
        public static string Dd1String(string key) => Dd1(key);

        // ---- the party --------------------------------------------------------------------------------

        /// <summary>
        /// DD1's name of a party of four ("The Usual Suspects"), null when it has none for these classes in this
        /// order. shared/party_name/party_name_library.json lists the classes a name asks for; the same four
        /// in another order carry another name there, so the order counts. GUESS: the list runs as the tray
        /// shows the party, from the back rank to the front (DD1's first entry is vestal, plague doctor,
        /// highwayman, crusader: the four its own first weeks put together, the crusader in front).
        /// </summary>
        public static string PartyName(IReadOnlyList<string> classesBackToFront)
        {
            if (classesBackToFront == null || classesBackToFront.Count == 0) return null;
            if (_partyIds == null) _partyIds = ReadPartyLibrary();
            return _partyIds.TryGetValue(string.Join("|", classesBackToFront), out var id) && PartyNames.TryGetValue("party_name_" + id, out var name) ? name : null;
        }

        /// <summary>
        /// What a hero says to a quest beneath them (DD1's str_quest_too_easy: "This quest is beneath my
        /// experience."). DD1 has several lines; a hero keeps one for a quest.
        /// </summary>
        public static string TooEasy(uint guid, string questId)
        {
            if (_tooEasy == null)
            {
                _tooEasy = new List<string>();
                ReadTable(DialogueTable, BarkPrefixes, (id, text) => _tooEasy.Add(text));
            }
            if (_tooEasy.Count == 0) return "This quest is beneath my experience.";
            var pick = guid;
            foreach (var letter in questId ?? "") pick = pick * 31u + letter;
            return _tooEasy[(int)(pick % (uint)_tooEasy.Count)];
        }

        // ---- regions and rewards -----------------------------------------------------------------------

        /// <summary>
        /// What the pointer is told on a region's bar, in DD1's words: how far the way to its master is cleared
        /// (town_quest_progress_format), that the master waits while a quest for one is on offer there
        /// (town_quest_progress_&lt;plot quest&gt;: "Slay the boss!" for every one of them), or what opens the region.
        /// </summary>
        public static string RegionTooltip(string dungeonId, DungeonProgress progress, bool bossOnOffer)
        {
            // the Darkest Dungeon while it is shut: the mod's own line, not DD1's "explore other regions" (nothing opens it)
            if (dungeonId == QuestBoard.DarkestDungeon && DarkestDungeon.Sealed) return DarkestDungeon.SealedLine;
            if (!progress.Open) return LockedHint;
            // while its five stand-ins hold it: which of them waits, or that none is left
            if (dungeonId == QuestBoard.DarkestDungeon && DarkestBosses.Holds) return DarkestBosses.RegionLine();
            if (dungeonId == QuestBoard.DarkestDungeon) return DarkestHint;
            // (DD1's quests of a town event and of the Shrieker have a line of their own: "Defend the Hamlet from brigand raid!")
            if (bossOnOffer) return PlotQuests.RegionLine(dungeonId) ?? Dd1("town_quest_progress_plot_kill_necromancer_1") ?? "Slay the boss!";
            return Fill(Dd1("town_quest_progress_format") ?? "Clear a path to the boss! Progress: %3.0f%%",
                Mathf.RoundToInt(Mathf.Clamp01(progress.Share) * 100f).ToString(CultureInfo.InvariantCulture), null);
        }

        /// <summary>DD1's name of what a quest pays besides trinkets ("Gold", "Bust", "Deed").</summary>
        public static string RewardName(string id)
        {
            return Dd1(id == "gold" ? "str_inventory_title_gold" : "str_inventory_title_heirloom" + id) ?? Title(id);
        }

        /// <summary>DD1's line about it ("A sculpted bust of a once prominent local figure.").</summary>
        public static string RewardDescription(string id)
        {
            return Dd1(id == "gold" ? "str_inventory_description_gold" : "str_inventory_description_heirloom" + id);
        }

        // ---- the mod's words ---------------------------------------------------------------------------

        /// <summary>The line under a reward trinket's name: its DD1 rarity, and what stands behind a card without a trinket.</summary>
        public static string TrinketLine(QuestTrinket trinket)
        {
            if (trinket.IsTrophy) return "The trophy of this place's master";
            var rarity = TrinketRarity(trinket.Rarity) + " trinket";
            return trinket.Item != null ? rarity : rarity + ": drawn when the quest is done";
        }

        // ---- the table ---------------------------------------------------------------------------------

        private static Dictionary<string, string> Strings => _strings ?? (_strings = Read(Table, Prefixes));

        private static Dictionary<string, string> PartyNames => _partyNames ?? (_partyNames = Read(PartyTable, PartyPrefixes));

        private static string Dd1(string key)
        {
            return Strings.TryGetValue(key, out var text) && text.Length > 0 ? text : null;
        }

        // Repeated ids are alternative lines in DD1; the first is as good as any here.
        private static Dictionary<string, string> Read(string table, string[] prefixes)
        {
            var strings = new Dictionary<string, string>();
            ReadTable(table, prefixes, (id, text) =>
            {
                if (!strings.ContainsKey(id)) strings[id] = text;
            });
            return strings;
        }

        // A table holds every language (the main one is 6 MB); English comes first, so reading stops at the end of its section.
        private static void ReadTable(string table, string[] prefixes, Action<string, string> found)
        {
            var path = Dd1Install.PathOf(table);
            if (path == null || !File.Exists(path)) return;
            try
            {
                var english = false;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.IndexOf("<language ", StringComparison.Ordinal) >= 0)
                    {
                        if (english) break;
                        english = line.IndexOf("id=\"english\"", StringComparison.Ordinal) >= 0;
                        continue;
                    }
                    if (!english) continue;
                    if (line.IndexOf("</language>", StringComparison.Ordinal) >= 0) break;
                    if (!Wanted(line, prefixes)) continue;
                    var match = Entry.Match(line);
                    if (match.Success) found(match.Groups[1].Value, Markup.Replace(match.Groups[2].Value, "").Trim());
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 string table could not be read (" + table + "): " + e.Message); }
        }

        // "vestal|plague_doctor|highwayman|crusader" to the number of the party's name.
        private static Dictionary<string, string> ReadPartyLibrary()
        {
            var ids = new Dictionary<string, string>();
            try
            {
                var text = Dd1Install.ReadText(PartyLibrary);
                if (text == null) return ids;
                foreach (var entry in JObject.Parse(text)["party_names"] as JArray ?? new JArray())
                {
                    var classes = new List<string>();
                    foreach (var heroClass in entry["required_hero_class"] as JArray ?? new JArray()) classes.Add((string)heroClass);
                    var id = (string)entry["id"];
                    var key = string.Join("|", classes);
                    if (id != null && classes.Count > 0 && !ids.ContainsKey(key)) ids[key] = id;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 party names could not be read: " + e.Message); }
            return ids;
        }

        private static bool Wanted(string line, string[] prefixes)
        {
            foreach (var prefix in prefixes)
                if (line.IndexOf(prefix, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string Title(string id)
        {
            var words = (id ?? "").Split('_');
            for (var i = 0; i < words.Length; i++)
                if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }
    }

    /// <summary>
    /// DD1's named colours (colours/base.colours.darkest: `colour: .id "x" .rgba r g b a`, `.rgba #rrggbb` or
    /// `.shared_id "y"`), so that the map's texts and bars are tinted as in DD1.
    /// </summary>
    internal static class QuestMapColours
    {
        private const string Source = "colours/base.colours.darkest";
        private static readonly Regex Line = new Regex("\\.id\\s+\"?([\\w.]+)\"?\\s+\\.(rgba|shared_id)\\s+(.*)");
        private static readonly Regex FactorLine = new Regex("\\.id\\s+\"?([\\w.]+)\"?\\s+\\.hvec4\\s+([\\d.]+)\\s+([\\d.]+)\\s+([\\d.]+)");

        // FALLBACK, used only when the file cannot be read: DD1's values of the colours the map uses.
        private static readonly Dictionary<string, Color> Defaults = new Dictionary<string, Color>
        {
            { "neutral", Rgb(174, 172, 162) }, { "notable", Rgb(200, 180, 110) }, { "harmful", Rgb(177, 25, 0) },
            { "town_quest_select_xp_bar_gradient_left", Rgb(0x8e, 0x03, 0x00) },
            { "town_quest_select_xp_bar_gradient_right", Rgb(0xeb, 0x16, 0x00) },
            { "town_quest_select_party_name_default", Rgb(0x5d, 0x5a, 0x50) }
        };

        // What DD1 shares each of the map's colours with, should the file be missing.
        private static readonly Dictionary<string, string> DefaultShares = new Dictionary<string, string>
        {
            { "town_name", "notable" }, { "town_progression_forward", "harmful" }, { "town_quest_select_dungeon_name", "notable" },
            { "town_quest_select_dungeon_level", "neutral" }, { "town_quest_select_party_name", "notable" }, { "town_quest_name", "notable" },
            { "town_quest_description", "neutral" }, { "town_quest_specifics", "neutral" }, { "town_quest_camping", "notable" },
            { "town_quest_goals", "neutral" }, { "town_quest_goal_start_description", "neutral" }, { "town_quest_rewards", "notable" },
            { "inventory_amount", "notable" }
        };

        private static Dictionary<string, Color> _colours;

        public static Color Neutral => Get("neutral");
        public static Color Notable => Get("notable");
        public static Color Harmful => Get("harmful");
        public static Color Faded => Get("town_quest_select_party_name_default");

        public static Color Get(string id)
        {
            return TryGet(id, out var colour) ? colour : UI.UiKit.Parchment;
        }

        /// <summary>
        /// A colour DD1 multiplies a picture by (`colour: .id "button_highlight" .hvec4 1.5 1.5 1.3 1.0`): its
        /// channels may pass 1, which draws the picture brighter than its art.
        /// </summary>
        public static Color Factor(string id, Color fallback)
        {
            var text = Dd1Install.ReadText(Source);
            if (text == null) return fallback;
            foreach (Match match in FactorLine.Matches(text))
            {
                if (match.Groups[1].Value != id) continue;
                if (float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                    float.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                    float.TryParse(match.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return new Color(r, g, b, 1f);
            }
            return fallback;
        }

        /// <summary>False when DD1's table has no colour of that name.</summary>
        public static bool TryGet(string id, out Color colour)
        {
            if (_colours == null) _colours = Read();
            return _colours.TryGetValue(id, out colour);
        }

        private static Dictionary<string, Color> Read()
        {
            var colours = new Dictionary<string, Color>(Defaults);
            var shares = new Dictionary<string, string>(DefaultShares);
            var text = Dd1Install.ReadText(Source);
            if (text != null)
            {
                foreach (var raw in text.Split('\n'))
                {
                    var match = Line.Match(raw);
                    if (!match.Success) continue;
                    var id = match.Groups[1].Value;
                    var value = match.Groups[3].Value.Trim();
                    if (match.Groups[2].Value == "shared_id")
                    {
                        shares[id] = value.Split(' ', '\t')[0].Trim('"');
                        colours.Remove(id);
                    }
                    else if (TryColour(value, out var colour))
                    {
                        colours[id] = colour;
                        shares.Remove(id);
                    }
                }
            }
            // a shared colour may itself be shared: a few passes settle every chain DD1 has
            for (var pass = 0; pass < 4; pass++)
                foreach (var share in shares)
                    if (!colours.ContainsKey(share.Key) && colours.TryGetValue(share.Value, out var target)) colours[share.Key] = target;
            return colours;
        }

        private static bool TryColour(string value, out Color colour)
        {
            colour = Color.white;
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = value.Substring(1).Split(' ', '\t', '\r')[0];
                if (hex.Length == 3) hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
                if (hex.Length < 6) return false;
                if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
                    !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
                    !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
                colour = Rgb(r, g, b);
                return true;
            }
            var parts = value.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            var channels = new int[3];
            for (var i = 0; i < 3; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[i])) return false;
            colour = Rgb(channels[0], channels[1], channels[2]);
            return true;
        }

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
}
