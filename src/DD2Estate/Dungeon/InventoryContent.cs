using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>The DD1 item data behind the party's bag, read once from the player's install.</summary>
    internal static class InventoryContent
    {
        private static ItemCatalog _items;
        private static InventoryRaidRules _rules;
        private static ProvisionRules _provision;

        public static ItemCatalog Items => _items ?? (_items = ItemCatalog.Load(new Dd1Files()));
        public static InventoryRaidRules Rules => _rules ?? (_rules = InventoryRaidRules.Load(new Dd1Files()));
        public static ProvisionRules Provision => _provision ?? (_provision = ProvisionRules.Load(new Dd1Files(), Items));

        public static Core.Inventory NewBag() => new Core.Inventory(Rules.RaidSlots, Rules.RaidStackLimits);

        /// <summary>
        /// The card of a stack. A trinket's is DD1's card of the rarity it was found under; DD2's own picture of
        /// the trinket lies on it (<see cref="InventoryGrid"/> puts it there), as in the Trinket Inventory.
        /// </summary>
        public static Sprite Icon(ItemDef item, int amount)
        {
            var path = item.Type == ItemTypes.Trinket ? Trinkets.RarityArt(Trinkets.Grade(item.Id)) : item.IconPath(amount);
            return Dd1Install.Exists(path) ? Dd1Install.Sprite(path) : null;
        }
    }

    /// <summary>
    /// Words for items. Names and descriptions come from the English section of the player's DD1 string table
    /// (str_inventory_title_&lt;type&gt;&lt;id&gt;, str_inventory_description_&lt;type&gt;&lt;id&gt;), so the mod carries no DD1
    /// text. The mod's own wording is kept for what DD1's sentences cannot say: what an item does to a hero
    /// in the Estate, and an item DD1 has no description of.
    /// </summary>
    internal static class InventoryText
    {
        private const string Table = "localization/miscellaneous.string_table.xml";
        private const string Prefix = "str_inventory_title_";
        private const string DescriptionPrefix = "str_inventory_description_";
        private static readonly Regex Entry = new Regex("<entry id=\"str_inventory_(title|description)_([^\"]+)\"><!\\[CDATA\\[(.*?)\\]\\]></entry>");
        private static Dictionary<string, string> _names, _descriptions;

        public static string Name(ItemDef item)
        {
            if (item == null) return "";
            // the trinkets are DD2's: its name, whatever DD1 calls a trinket of the same id
            if (item.Type == ItemTypes.Trinket) return Trinkets.Name(item.Id);
            if (_names == null) Read();
            if (_names.TryGetValue(item.Type + item.Id, out var name)) return name;
            return Title(item.Id.Length > 0 ? item.Id : item.Type == ItemTypes.Provision ? "food" : item.Type);
        }

        /// <summary>DD1's sentence about an item ("Increases the light level."); null when it has none.</summary>
        public static string Description(ItemDef item)
        {
            if (item == null) return null;
            if (item.Type == ItemTypes.Trinket) return null;
            if (_descriptions == null) Read();
            return _descriptions.TryGetValue(item.Type + item.Id, out var text) && text.Length > 0 ? text : null;
        }

        /// <summary>
        /// What DD1's tooltip says of an item under its name: the description, for a gem what one is worth
        /// (str_inventory_gold_value_format: "[Value: 1,250 Gold Each]", in DD1's colour for the line), and with <paramref name="discard"/> DD1's line on how to throw it away. One line
        /// between them is the mod's: what the item does to a hero in the Estate, where DD1's sentence speaks
        /// of a thing a DD2 hero does not carry out of a fight (bleeding, blight, a debuff). TextMeshPro rich text.
        /// </summary>
        public static string Tooltip(ItemDef item, bool discard)
        {
            if (item != null && item.Type == ItemTypes.Trinket) return TrinketTooltip(item, discard);
            var text = Description(item);
            if (text == null) text = Hint(item);
            else
            {
                var use = InventoryContent.Rules.UseOf(item);
                if (use.Kind == ItemUseKind.Heal) text += "\nOn a hero: +" + Mathf.RoundToInt((float)(use.Amount * 100)) + "% health.";
                else if (use.Kind == ItemUseKind.StressHeal) text += "\nOn a hero: -" + use.Amount.ToString("0.#", CultureInfo.InvariantCulture) + " stress.";
            }
            // DD1 prices its gems: what the others sell for is not told in the dungeon
            if (item.Type == ItemTypes.Gem && item.SellGold > 0)
                text += "\n<color=" + RaidText.Hex(Dd1Fonts.Colour("inventory_tooltip_gold_value", DD2Estate.UI.UiKit.Notable)) + ">"
                        + RaidText.Format(RaidText.Get("str_inventory_gold_value_format", "[Value: %s Gold Each]"), Number(item.SellGold)) + "</color>";
            if (discard)
                text += "\n<color=" + RaidText.Hex(Dd1Fonts.Colour("inventory_shift_click_remove_item", DD2Estate.UI.UiKit.Harmful)) + ">"
                        + RaidText.Get("str_discard_item_instructions", "[SHIFT-CLICK] on this item to discard it.") + "</color>";
            return text;
        }

        // A trinket's card says what the Trinket Inventory says of it: DD1's name of its rarity, what DD2 says
        // the item does, and how it is put on here.
        private static string TrinketTooltip(ItemDef item, bool discard)
        {
            var text = "<color=" + RaidText.Hex(DD2Estate.UI.UiKit.Notable) + ">" + QuestMapText.TrinketRarity(Trinkets.Grade(item.Id)) + "</color>";
            var does = Trinkets.Description(item.Id);
            if (!string.IsNullOrEmpty(does)) text += "\n" + does;
            if (discard)
                text += "\n" + Hint(item) + "\n<color=" + RaidText.Hex(Dd1Fonts.Colour("inventory_shift_click_remove_item", DD2Estate.UI.UiKit.Harmful)) + ">"
                        + RaidText.Get("str_discard_item_instructions", "[SHIFT-CLICK] on this item to discard it.") + "</color>";
            return text;
        }

        /// <summary>A stack's size as the player reads it; a trinket's card carries no number (DD1 writes none on one).</summary>
        public static string Amount(ItemDef item, int amount)
        {
            return item.Type == ItemTypes.Trinket ? "" : Number(amount);
        }

        /// <summary>
        /// A number as DD1 writes one on a card, a price tag or in a tooltip: the thousands set apart ("75",
        /// "1,750"; its own frames have "3,000" on a quest's gold card and "1,090" for a treasure's total).
        /// </summary>
        public static string Number(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>"2 Emerald", "5 gold".</summary>
        public static string Counted(ItemDef item, int amount)
        {
            if (item.Type == ItemTypes.Trinket) return "the trinket " + Name(item);
            return item.Type == ItemTypes.Gold ? Amount(item, amount) + " gold" : amount + " " + Name(item);
        }

        public static string Lower(ItemDef item) => Name(item).ToLowerInvariant();

        /// <summary>What the item is for in the Estate, in the mod's words.</summary>
        public static string Hint(ItemDef item)
        {
            var rules = InventoryContent.Rules;
            var use = rules.UseOf(item);
            var percent = Mathf.RoundToInt((float)(use.Amount * 100));
            switch (use.Kind)
            {
                case ItemUseKind.Light: return "Lit with a click: the torchlight rises.";
                case ItemUseKind.Camp: return "Makes camp in a cleared room: a meal, respite, and a night that may not be quiet.";
                case ItemUseKind.Food:
                    return "A meal when the party grows hungry (" + rules.FoodPerHero + " a hero). On a hero: +" + percent + "% health, " + rules.FoodBeforeFull + " at most between meals.";
                case ItemUseKind.Heal: return "On a hero: +" + percent + "% health. Some curios call for it.";
                case ItemUseKind.StressHeal: return "On a hero: -" + use.Amount.ToString("0.#", CultureInfo.InvariantCulture) + " stress. Some curios call for it.";
            }
            switch (item.Type)
            {
                case ItemTypes.Gold: return "Joins the estate's purse on the way home.";
                case ItemTypes.Gem: return "Sold for " + Number(item.SellGold) + " gold on the way home.";
                case ItemTypes.Heirloom: return "Goes to the estate on the way home.";
                case ItemTypes.QuestItem: return "Needed for this quest.";
                case ItemTypes.Trinket: return "A click puts it on the selected hero. Unworn, it goes to the estate's trinkets on the way home.";
            }
            if (item.Id == "shovel") return "Clears an obstacle without the toll of digging by hand; pries some curios open.";
            if (item.Id == "skeleton_key") return "Opens locked curios safely.";
            if (item.Id == "firewood") return "For a camp.";
            return "Offered when a curio has a use for it.";
        }

        // The file holds every language (6 MB); English comes first, so reading stops at the end of its section.
        private static void Read()
        {
            _names = new Dictionary<string, string>();
            _descriptions = new Dictionary<string, string>();
            var path = Dd1Install.PathOf(Table);
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
                    if (line.IndexOf(Prefix, StringComparison.Ordinal) < 0 && line.IndexOf(DescriptionPrefix, StringComparison.Ordinal) < 0) continue;
                    var match = Entry.Match(line);
                    if (!match.Success) continue;
                    var into = match.Groups[1].Value == "title" ? _names : _descriptions;
                    if (!into.ContainsKey(match.Groups[2].Value)) into[match.Groups[2].Value] = match.Groups[3].Value;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 string table could not be read: " + e.Message); }
        }

        private static string Title(string id)
        {
            var text = (id ?? "").Replace('_', ' ');
            return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
        }
    }
}
