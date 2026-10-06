using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DD2Estate.Core
{
    /// <summary>DD1 curio result types, spelled as in <c>curios/curio_type_library.csv</c>.</summary>
    public static class CurioResultTypes
    {
        public const string Nothing = "Nothing";
        public const string Loot = "Loot";
        public const string Quirk = "Quirk";
        public const string Effect = "Effect";
        public const string Purge = "Purge";
        public const string Scouting = "Scouting";
        public const string Teleport = "Teleport";
        public const string Disease = "Disease";
        public const string Summon = "Summon";
        /// <summary>Not in the library: the result of DD1's quest curios (<c>props/prop_definitions.json</c>).</summary>
        public const string QuestItem = "quest_item";
    }

    /// <summary>
    /// DD1's ids for what the player has learnt an item does to a curio, as its art names them
    /// (<c>panels/icons_curio_tracker/&lt;id&gt;.curio_tracker.png</c>). The library's "CURIO TRACKER ID" column gives
    /// the one an item's interaction earns; the two below are the game's own: an item not tried yet, and one
    /// the curio turned out to have no use for.
    /// </summary>
    public static class TrackerIds
    {
        /// <summary>DD1's own spelling of the file.</summary>
        public const string Unknown = "unkown";
        public const string NoEffect = "no_effect";
    }

    /// <summary>
    /// What the player has learnt items do to curios, as DD1 keeps it (persist.curio_tracker.json): a hint id by
    /// curio type and item. An item that was never tried on a curio is <see cref="TrackerIds.Unknown"/>.
    /// </summary>
    public sealed class CurioTracker
    {
        private readonly Dictionary<string, string> _known = new Dictionary<string, string>();

        private static string Key(string curioType, string itemId) => curioType + "|" + itemId;

        public int Count => _known.Count;

        public string Get(string curioType, string itemId)
        {
            if (string.IsNullOrEmpty(curioType) || string.IsNullOrEmpty(itemId)) return TrackerIds.Unknown;
            return _known.TryGetValue(Key(curioType, itemId), out var id) ? id : TrackerIds.Unknown;
        }

        public void Learn(string curioType, string itemId, string trackerId)
        {
            if (string.IsNullOrEmpty(curioType) || string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(trackerId)) return;
            _known[Key(curioType, itemId)] = trackerId;
        }

        public void Clear() => _known.Clear();

        public Newtonsoft.Json.Linq.JObject ToJson()
        {
            var json = new Newtonsoft.Json.Linq.JObject();
            foreach (var pair in _known) json[pair.Key] = pair.Value;
            return json;
        }

        public void FromJson(Newtonsoft.Json.Linq.JToken json)
        {
            _known.Clear();
            if (!(json is Newtonsoft.Json.Linq.JObject known)) return;
            foreach (var p in known.Properties())
                if (p.Value.Type == Newtonsoft.Json.Linq.JTokenType.String) _known[p.Name] = (string)p.Value;
        }
    }

    /// <summary>
    /// One value of a result. Loot: Value is a DD1 loot table code and Amount the number of draws.
    /// Any other type: Value is the DD1 id (effect, quirk, disease, "N - target" scouting) and Amount its weight.
    /// </summary>
    public sealed class CurioValue
    {
        public string Value;
        public double Amount;
    }

    public sealed class CurioResult
    {
        public string Type;
        /// <summary>Weight of this result type among the curio's results; unused for an item interaction.</summary>
        public double Weight;
        public List<CurioValue> Values = new List<CurioValue>();
        /// <summary>Loot only: chance that the contents are lost and nothing is drawn (a strongbox smashed open with a shovel).</summary>
        public double NothingChance;
    }

    public sealed class CurioItemUse
    {
        public string ItemId;
        public CurioResult Result;
        /// <summary>DD1's hint icon for a known interaction (<c>panels/icons_curio_tracker</c>): loot, buff, purge_neg...</summary>
        public string TrackerId;
    }

    public sealed class CurioDef
    {
        public string Id, Name, Alignment, Region;
        /// <summary>
        /// The library's "FULL CURIO?": Yes for a curio DD1 asks about in its window (the hand, the item slot, the
        /// way past); No for one that is opened the moment it is turned to (discarded pack, sconce, crate, sack).
        /// </summary>
        public bool Full = true;
        public List<string> Tags = new List<string>();
        /// <summary>Results of investigating with bare hands, with their weights.</summary>
        public List<CurioResult> Results = new List<CurioResult>();
        public List<CurioItemUse> ItemUses = new List<CurioItemUse>();

        public CurioItemUse ItemUse(string itemId) => itemId == null ? null : ItemUses.FirstOrDefault(u => u.ItemId == itemId);
    }

    /// <summary>A row of <c>curios/curio_props.csv</c>: the prop placed in a dungeon, its art and the curio type behind it.</summary>
    public sealed class CurioProp
    {
        public string Id, Sprite, Type, UiString, Audio;
    }

    public sealed class LootDraw
    {
        public string Table;
        public int Draws;
    }

    /// <summary>One rolled interaction, in DD1 terms. Turning it into DD2 effects and items is the game layer's part.</summary>
    public sealed class CurioOutcome
    {
        public string CurioId;
        /// <summary>The item that changed the outcome; null when none was used or the curio has no use for it (keep the item then).</summary>
        public string ItemId;
        public string Type = CurioResultTypes.Nothing;
        /// <summary>Effect, quirk, disease, purge, scouting or summon id. Null for Nothing and Loot.</summary>
        public string Value;
        public List<LootDraw> Loot = new List<LootDraw>();
    }

    /// <summary>The curios of the player's DD1 install in a neutral form.</summary>
    public sealed class CurioCatalog
    {
        private readonly Dictionary<string, CurioDef> _types = new Dictionary<string, CurioDef>();
        private readonly Dictionary<string, CurioProp> _props = new Dictionary<string, CurioProp>();
        private readonly List<CurioDef> _all = new List<CurioDef>();

        public IReadOnlyList<CurioDef> All => _all;

        public static CurioCatalog Load(IDd1Files files)
        {
            var catalog = new CurioCatalog();
            catalog.ReadLibrary(Csv(files.ReadText("curios/curio_type_library.csv")));
            foreach (var row in Csv(files.ReadText("curios/curio_props.csv")).Skip(1))
            {
                if (row.Count < 3 || row[0].Trim().Length == 0) continue;
                catalog._props[row[0].Trim()] = new CurioProp
                {
                    Id = row[0].Trim(), Sprite = row[1].Trim(), Type = row[2].Trim(),
                    UiString = Cell(row, 3), Audio = Cell(row, 4)
                };
            }
            return catalog;
        }

        public CurioProp Prop(string propId) => propId != null && _props.TryGetValue(propId, out var prop) ? prop : null;

        /// <summary>Definition behind a prop placed in a dungeon. Null for quest curios, which DD1 handles outside the library.</summary>
        public CurioDef Get(string propId)
        {
            if (propId == null) return null;
            var prop = Prop(propId);
            if (prop != null && _types.TryGetValue(prop.Type, out var byType)) return byType;
            return _types.TryGetValue(propId, out var def) ? def : null;
        }

        /// <summary>Item ids a curio reacts to.</summary>
        public List<string> ItemsFor(string propId)
        {
            var def = Get(propId);
            return def == null ? new List<string>() : def.ItemUses.Select(u => u.ItemId).ToList();
        }

        public CurioOutcome Roll(string propId, string itemId, Rng rng)
        {
            var outcome = new CurioOutcome { CurioId = propId };
            var def = Get(propId);
            if (def == null) return outcome;

            CurioResult result;
            var use = def.ItemUse(itemId);
            if (use != null)
            {
                result = use.Result;
                outcome.ItemId = itemId;
            }
            else
            {
                var index = rng.PickWeighted(def.Results.Select(r => r.Weight).ToList());
                if (index < 0) return outcome;
                result = def.Results[index];
            }

            if (result.Type == CurioResultTypes.Nothing) return outcome;
            if (result.Type == CurioResultTypes.Loot)
            {
                if (result.NothingChance > 0 && rng.Chance(result.NothingChance)) return outcome;
                outcome.Type = result.Type;
                // every listed table is drawn, they are not alternatives
                foreach (var value in result.Values)
                    outcome.Loot.Add(new LootDraw { Table = value.Value, Draws = (int)value.Amount });
                return outcome;
            }

            outcome.Type = result.Type;
            var pick = rng.PickWeighted(result.Values.Select(v => v.Amount).ToList());
            if (pick >= 0) outcome.Value = result.Values[pick].Value;
            return outcome;
        }

        /// <summary>
        /// True for a curio DD1 opens its window for; false for one that is used the moment it is turned to
        /// (the library's "FULL CURIO?" is No). A quest curio, which the library does not hold, has the window.
        /// </summary>
        public bool HasWindow(string propId)
        {
            var def = Get(propId);
            return def == null || def.Full;
        }

        /// <summary>DD1's hint id for an item used on a curio ("CURIO TRACKER ID": loot, nothing, purge_neg...); null when the curio has no use for the item.</summary>
        public string TrackerFor(string propId, string itemId)
        {
            var use = Get(propId)?.ItemUse(itemId);
            if (use == null) return null;
            return string.IsNullOrEmpty(use.TrackerId) ? TrackerIds.Unknown : use.TrackerId;
        }

        // DD1 spells a result type in its string ids as it does in the library, in lower case; a map is a "scout".
        private static string ResultWord(string type)
        {
            switch (type)
            {
                case CurioResultTypes.Scouting: return "scout";
                case CurioResultTypes.QuestItem: return "loot";
                default: return (type ?? "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// The ids under which DD1's string table may hold the sentence for an interaction's outcome
        /// (<c>localization/curios.string_table.xml</c>), the most exact first:
        /// <c>str_curio_&lt;curio&gt;[_&lt;item&gt;]_&lt;result&gt;[_&lt;value&gt;]</c> ("str_curio_heirloom_chest_skeleton_key_loot",
        /// "str_curio_eerie_coral_effect_eerie_coral_stress"), then the few the table spells otherwise
        /// ("str_curio_eldritch_altar_holy_water", "str_curio_sacrificial_stone_provisions_purge"). The curio is
        /// named by its type in the library; a quest curio, which has none, by its prop.
        /// </summary>
        public List<string> ResultTextIds(CurioOutcome outcome)
        {
            var ids = new List<string>();
            if (outcome == null || string.IsNullOrEmpty(outcome.CurioId)) return ids;
            var names = new List<string>();
            void Name(string name)
            {
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            }
            Name(Get(outcome.CurioId)?.Id);
            Name(Prop(outcome.CurioId)?.UiString);
            Name(outcome.CurioId);
            var word = ResultWord(outcome.Type);
            var value = string.IsNullOrEmpty(outcome.Value) ? null : outcome.Value.Trim().ToLowerInvariant().Replace(' ', '_');
            var items = new List<string>();
            if (!string.IsNullOrEmpty(outcome.ItemId))
            {
                items.Add("_" + outcome.ItemId);
                // the table writes the food as "provisions"
                if (outcome.ItemId == "provision") items.Add("_provisions");
            }
            else items.Add("");
            foreach (var name in names)
                foreach (var item in items)
                {
                    if (value != null) ids.Add("str_curio_" + name + item + "_" + word + "_" + value);
                    ids.Add("str_curio_" + name + item + "_" + word);
                    if (item.Length > 0) ids.Add("str_curio_" + name + item);
                }
            return ids;
        }

        /// <summary>
        /// "N - target" of a Scouting result: N rooms around the party (0: the whole map) and what gets revealed
        /// (all, curios, traps, obstacles, hall_battles, room_battles).
        /// </summary>
        public static bool ParseScouting(string value, out int reach, out string target)
        {
            reach = 0;
            target = "all";
            if (value == null) return false;
            var dash = value.IndexOf('-');
            if (dash < 0 || !int.TryParse(value.Substring(0, dash).Trim(), out reach)) return false;
            target = value.Substring(dash + 1).Trim();
            return target.Length > 0;
        }

        // Library layout (a spreadsheet export): a block starts on the row whose second cell is the curio number.
        // Cell 2 runs down "ID STRING", id, "REGION FOUND", region, "FULL CURIO?", yes/no, "TAGS", tags...;
        // cell 4 names the result type of the row, 5 its weight, 7/8, 10/11, 13/14 are up to three values with
        // their weight (or number of draws for loot). After "Item Interactions": cell 4 item, 5 result type.
        private void ReadLibrary(List<List<string>> rows)
        {
            CurioDef def = null;
            var inItems = false;
            string label = null;
            foreach (var row in rows)
            {
                while (row.Count < 17) row.Add("");
                if (int.TryParse(row[1].Trim(), out _))
                {
                    def = new CurioDef { Name = row[2].Trim(), Alignment = row[4].Trim() };
                    inItems = false;
                    label = null;
                    continue;
                }
                if (def == null) continue;

                var c2 = row[2].Trim();
                if (c2 == "Item Interactions")
                {
                    inItems = true;
                    continue;
                }

                if (inItems)
                {
                    var item = row[4].Trim();
                    var type = row[5].Trim();
                    if (item.Length == 0 || type.Length == 0) continue;
                    var result = new CurioResult { Type = type, Weight = 1 };
                    ReadValues(row, result);
                    def.ItemUses.Add(new CurioItemUse { ItemId = item, Result = result, TrackerId = row[16].Trim() });
                    continue;
                }

                if (label == "ID STRING" && c2.Length > 0)
                {
                    def.Id = c2;
                    _types[c2] = def;
                    _all.Add(def);
                }
                else if (label == "REGION FOUND") def.Region = c2;
                else if (label == "FULL CURIO?") def.Full = !string.Equals(c2, "No", System.StringComparison.OrdinalIgnoreCase);
                else if (label == "TAGS")
                {
                    if (c2.Length > 0) def.Tags.Add(c2);
                    if (row[3].Trim().Length > 0) def.Tags.Add(row[3].Trim());
                }
                // a label applies to the row below it; TAGS runs to the end of the block
                if (label != "TAGS") label = c2 == "ID STRING" || c2 == "REGION FOUND" || c2 == "FULL CURIO?" || c2 == "TAGS" ? c2 : null;

                var resultType = row[4].Trim();
                if (resultType.Length == 0 || resultType == "RESULT TYPES") continue;
                var weight = ParseNumber(row[5]);
                if (weight <= 0) continue;
                var main = new CurioResult { Type = resultType, Weight = weight };
                ReadValues(row, main);
                def.Results.Add(main);
            }
        }

        private static void ReadValues(List<string> row, CurioResult result)
        {
            for (var cell = 7; cell <= 13; cell += 3)
            {
                var value = row[cell].Trim();
                if (value.Length == 0 || value == "N/A") continue;
                var amount = ParseNumber(row[cell + 1]);
                if (amount <= 0) amount = 1;
                if (result.Type == CurioResultTypes.Loot && value == CurioResultTypes.Nothing)
                {
                    // the only weighted alternative inside a loot row: its percent cell is the chance of an empty box
                    var percent = ParseNumber(row[cell + 2]);
                    result.NothingChance = percent > 0 ? percent / 100 : 0.5;
                    continue;
                }
                result.Values.Add(new CurioValue { Value = value, Amount = amount });
            }
        }

        private static double ParseNumber(string cell)
        {
            var text = cell.Trim().TrimEnd('%');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static string Cell(List<string> row, int index) => index < row.Count ? row[index].Trim() : "";

        private static List<List<string>> Csv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c != '"') cell.Append(c);
                    else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else if (c != '\r' && c != (char)0xFEFF) cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
