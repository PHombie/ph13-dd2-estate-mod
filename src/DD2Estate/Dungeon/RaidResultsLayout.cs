using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Where DD1 puts the parts of its two results screens and how long it takes over them, read from the
    /// player's install: raid_results/raid_results.layout.darkest and raid_results.anim.darkest (the screens'
    /// own), shared/progression/progression.layout.darkest (the red button on the bar),
    /// shared/resolve_level_bar/resolve_level_bar.layout.darkest and shared/hero/hero.layout.darkest (the
    /// badge, its experience bar, the stress pips), shared/estate/estate.layout.darkest (a currency's icon and
    /// count) and campaign/estate/estate.json (the order of the heirlooms). Everything is in pixels of DD1's
    /// 1920x1080 screen, y down; the two pages count from state_pos.
    ///
    /// The value written beside each read is DD1's stock value and is used only when the file or the key
    /// cannot be read: a FALLBACK, not a second source. docs/recon/dd1-raid-results.md lists every entry with
    /// what it places and how the game draws it (which point of a picture a position names is in DD1's code,
    /// not in its files).
    /// </summary>
    internal sealed class RaidResultsLayout
    {
        public const string Dir = "raid_results/";

        /// <summary>One of DD1's inventory grids: rows of <see cref="Columns"/>, the first slot at <see cref="Start"/>, <see cref="Pitch"/> apart.</summary>
        public struct Grid
        {
            public int Columns;
            public Vector2 Start, Pitch;
            public bool Centred;
        }

        private static RaidResultsLayout _current, _stock;

        /// <summary>Read once the DD1 install is found; before that the stock values answer.</summary>
        public static RaidResultsLayout Current
        {
            get
            {
                if (_current != null) return _current;
                if (Dd1Install.Found) return _current = new RaidResultsLayout(true);
                return _stock ?? (_stock = new RaidResultsLayout(false));
            }
        }

        // ---- raid_results_screen_layout: what both pages share ------------------------------------------
        /// <summary>The region's picture (its corner), the outcome panel (the middle of its top edge), the two titles (the middle of their line cell's foot).</summary>
        public readonly Vector2 Background, Panel, QuestTitle, QuestResult;
        /// <summary>The origin both pages count from, and the bar's corner.</summary>
        public readonly Vector2 State, Bar;

        // ---- raid_results_items_state_layout: page 1 (from State) ---------------------------------------
        /// <summary>The frame and the ribbon's words hang from the middle of their top edge; the two other titles and the grids have their corner here; the totals are centred on theirs.</summary>
        public readonly Vector2 ItemsFrame, RewardsTitle, RewardsGrid, TreasureTitle, TreasureGrid, TreasureTotal, HeirloomTitle, HeirloomGrid, HeirloomTotal;
        public readonly Grid Rewards, Treasure, Heirlooms;

        // ---- raid_results_heroes_state_layout, raid_results_hero_layout: page 2 ---------------------------
        /// <summary>The frame (middle of its top edge), the first hero's origin and the step to the next: from State.</summary>
        public readonly Vector2 HeroesFrame, HeroesStart, HeroesSpacing;
        /// <summary>A hero's parts, from the hero's origin. The experience line ends at <see cref="XpText"/>.</summary>
        public readonly Vector2 Portrait, DeadPortrait, HeroName, Status, StatusTooltip, XpText, QuirksStart, QuirksSpacing, QuirksCentre;

        // ---- shared/hero/hero.layout.darkest, shared/resolve_level_bar/resolve_level_bar.layout.darkest ------
        /// <summary>DD1's "campaign status" widget, from its origin: the resolve bar, the row of stress pips, the affliction's name.</summary>
        public readonly Vector2 ResolveBar, Stress, StressSpacing, Affliction, DiseaseIcon;
        /// <summary>The resolve bar, from its own origin: the badge, the level's number (its middle), the bar's holder, the fill inside the holder, the pulse.</summary>
        public readonly Vector2 BadgeBack, BadgeAfflicted, BadgeNumber, BarHolder, BarFill, BarFillSize, BarMask, Pulse, TooltipHot, TooltipHotSize;
        public readonly float TooltipWidth;

        // ---- shared/estate/estate.layout.darkest ----------------------------------------------------------
        public readonly Vector2 LargeIcon, LargeNumber, SmallIcon, SmallNumber, SmallNext;
        /// <summary>The heirlooms in the order DD1 lists its currencies (campaign/estate/estate.json): bust, portrait, deed, crest.</summary>
        public readonly string[] HeirloomOrder;

        // ---- shared/progression/progression.layout.darkest ------------------------------------------------
        public readonly Vector2 Forward, ForwardText, ForwardGlow;

        // ---- raid_results.anim.darkest ---------------------------------------------------------------------
        /// <summary>Seconds between two cards of the bag; how far and how long a card's worth rises.</summary>
        public readonly float CardWait, PopupRise, PopupSeconds;
        /// <summary>A total swells to this and settles, this long each way.</summary>
        public readonly float GoldPulse, GoldPulseSeconds, HeirloomPulse, HeirloomPulseSeconds;
        /// <summary>The experience line slides in from <see cref="XpSlideFrom"/> to <see cref="XpSlideTo"/> of its place; the bar fills.</summary>
        public readonly float XpSlideFrom, XpSlideTo, XpSlideSeconds, XpBarSeconds;
        /// <summary>The masks' offset from the quirks' place; after a mask is opened the names wait, then fade in.</summary>
        public readonly Vector2 Masks;
        public readonly float QuirkWait, QuirkFade;

        private RaidResultsLayout(bool read)
        {
            var layout = read ? DarkestFile.Load(Dir + "raid_results.layout.darkest") : null;
            var anim = read ? DarkestFile.Load(Dir + "raid_results.anim.darkest") : null;
            var progression = read ? DarkestFile.Load("shared/progression/progression.layout.darkest") : null;
            var resolve = read ? DarkestFile.Load("shared/resolve_level_bar/resolve_level_bar.layout.darkest") : null;
            var hero = read ? DarkestFile.Load("shared/hero/hero.layout.darkest") : null;
            var estate = read ? DarkestFile.Load("shared/estate/estate.layout.darkest") : null;

            const string screen = "raid_results_screen_layout";
            Background = V(layout, screen, "level_background_pos", 0f, 0f);
            Panel = V(layout, screen, "completion_background_pos", 960f, 0f);
            QuestTitle = V(layout, screen, "quest_title_pos", 960f, 212f);
            QuestResult = V(layout, screen, "quest_result_pos", 960f, 150f);
            State = V(layout, screen, "state_pos", 510f, 0f);
            Bar = V(layout, screen, "progression_bar_pos", 0f, 958f);

            const string items = "raid_results_items_state_layout";
            ItemsFrame = V(layout, items, "frame_offset", 450f, 230f);
            RewardsTitle = V(layout, items, "quest_inventory_title", 450f, 242f);
            RewardsGrid = V(layout, items, "quest_inventory_grid_offset", 84f, 308f);
            TreasureTitle = V(layout, items, "party_gold_inventory_title", 200f, 490f);
            TreasureGrid = V(layout, items, "party_gold_inventory_grid_offset", 84f, 538f);
            TreasureTotal = V(layout, items, "party_gold_total_offset", 620f, 496f);
            HeirloomTitle = V(layout, items, "party_heirloom_inventory_title", 200f, 714f);
            HeirloomGrid = V(layout, items, "party_heirloom_inventory_grid_offset", 84f, 760f);
            HeirloomTotal = V(layout, items, "party_heirloom_total_offset", 620f, 720f);
            Rewards = G(layout, "raid_results_quest_inventory_system_grid_layout", 8, 40f);
            Treasure = G(layout, "raid_results_party_gold_inventory_system_grid_layout", 6, 100f);
            Heirlooms = G(layout, "raid_results_party_heirloom_inventory_system_grid_layout", 6, 100f);

            const string heroes = "raid_results_heroes_state_layout", row = "raid_results_hero_layout";
            HeroesFrame = V(layout, heroes, "frame_offset", 450f, 250f);
            HeroesStart = V(layout, heroes, "heroes_start_pos", 130f, 250f);
            HeroesSpacing = V(layout, heroes, "heroes_spacing", 0f, 168f);
            Portrait = V(layout, row, "portrait_icon_offset", 5f, 64f);
            DeadPortrait = V(layout, row, "portrait_dead_overlay_offset", 5f, 64f);
            HeroName = V(layout, row, "name_offset", 0f, 16f);
            Status = V(layout, row, "campaign_status_offset", 556f, 3f);
            StatusTooltip = V(layout, row, "campaign_status_resolve_level_bar_tooltip_offset", 100f, 8f);
            XpText = V(layout, row, "resolve_from_quest_offset", 520f, 20f);
            QuirksStart = V(layout, row, "new_quirks_from_quest_start_offset", 145f, 18f);
            QuirksSpacing = V(layout, row, "new_quirks_from_quest_spacing", 0f, 24f);
            QuirksCentre = V(layout, row, "center_of_quirks_upperleft_offset", 175f, 50f);

            const string status = "hero_campaign_status_layout";
            ResolveBar = V(hero, status, "resolve_level_bar_offset", 6f, 4f);
            Stress = V(hero, status, "stress_bar_offset", -14f, 100f);
            StressSpacing = V(hero, status, "stress_bar_spacing", 10f, 0f);
            Affliction = V(hero, status, "affliction_offset", 36f, 112f);
            DiseaseIcon = V(hero, "hero_portrait_icon_layout", "disease_icon_offset", 61f, 61f);
            BadgeBack = V(resolve, "resolve_level_number", "background_offset", -3f, 0f);
            BadgeAfflicted = V(resolve, "resolve_level_number", "afflicted_background_offset", -10f, -15f);
            BadgeNumber = V(resolve, "resolve_level_number", "number_offset", 30f, 31f);
            BarHolder = V(resolve, "resolve_level", "bar_pos", 12f, 28f);
            BarFill = V(resolve, "resolve_level_bar", "gradient_offset", 10f, 16f);
            BarFillSize = V(resolve, "resolve_level_bar", "gradient_size", 16f, 40f);
            BarMask = V(resolve, "resolve_level_bar", "mask_offset", 0f, 0f);
            Pulse = V(resolve, "resolve_level_pulse", "posn_offset", 30f, 30f);
            TooltipHot = V(resolve, "resolve_level_tooltip", "hot_spot_pos", 0f, 0f);
            TooltipHotSize = V(resolve, "resolve_level_tooltip", "hot_spot_size", 100f, 100f);
            TooltipWidth = F(resolve, "resolve_level_tooltip", "text_width", 200f);

            LargeIcon = V(estate, "estate_large_currency_layout", "icon_offset", 0f, -50f);
            LargeNumber = V(estate, "estate_large_currency_layout", "number_offset", 90f, -16f);
            SmallIcon = V(estate, "estate_currency_heirloom_layout", "icon_offset", 0f, -10f);
            SmallNumber = V(estate, "estate_currency_heirloom_layout", "number_offset", 38f, -4f);
            SmallNext = V(estate, "estate_currency_heirloom_layout", "next_currency_spacing", 2f, 0f);
            HeirloomOrder = ReadHeirloomOrder(read);

            Forward = V(progression, "progression_layout", "forward_pos", 801f, 984f);
            ForwardText = V(progression, "progression_layout", "forward_text_offset", 160f, -2f);
            ForwardGlow = V(progression, "progression_layout", "forward_selected_overlay_offset", 0f, -13f);

            CardWait = Mathf.Max(0.01f, F(anim, "treasure_item_wait", "time", 0.3f));
            PopupRise = F(anim, "treasure_item_value_popup_offset_y", "deltavalue", 60f);
            PopupSeconds = Mathf.Max(0.01f, F(anim, "treasure_item_value_popup_offset_y", "time", 1f));
            GoldPulse = F(anim, "treasure_gold_total_scale_pulse", "value", 1.1f);
            GoldPulseSeconds = Mathf.Max(0.01f, F(anim, "treasure_gold_total_scale_pulse", "time", 0.2f));
            HeirloomPulse = F(anim, "heirloom_total_scale_pulse", "value", 1.4f);
            HeirloomPulseSeconds = Mathf.Max(0.01f, F(anim, "heirloom_total_scale_pulse", "time", 0.2f));
            XpSlideFrom = F(anim, "raid_results_heroes_xptext_xoffset", "from_value", -175f);
            XpSlideTo = F(anim, "raid_results_heroes_xptext_xoffset", "to_value", 0f);
            XpSlideSeconds = Mathf.Max(0.01f, F(anim, "raid_results_heroes_xptext_xoffset", "time", 2f));
            XpBarSeconds = Mathf.Max(0.01f, F(anim, "raid_results_heroes_resolve_xp_bar", "time", 2f));
            Masks = V(anim, "raid_results_heroes_masks", "offset", -7f, 39f);
            QuirkWait = F(anim, "raid_results_heroes_quirk_text_wait", "time", 0.666f);
            QuirkFade = Mathf.Max(0.01f, F(anim, "raid_results_heroes_quirk_text_alpha", "time", 0.8f));
        }

        /// <summary>A hero's origin on the screen: rows of <see cref="HeroesSpacing"/> from <see cref="HeroesStart"/>.</summary>
        public Vector2 HeroOrigin(int index) => State + HeroesStart + HeroesSpacing * index;

        // FALLBACK, used only when campaign/estate/estate.json cannot be read: DD1's own order.
        private static readonly string[] StockOrder = { "bust", "portrait", "deed", "crest" };

        private static string[] ReadHeirloomOrder(bool read)
        {
            var order = new List<string>();
            try
            {
                var text = read ? Dd1Install.ReadText("campaign/estate/estate.json") : null;
                if (text != null)
                    foreach (var entry in (JArray)JObject.Parse(text.TrimStart((char)0xFEFF))["currencies"])
                    {
                        var id = (string)entry["id"];
                        if ((bool?)entry["is_heirloom"] == true && Array.IndexOf(EstateState.HeirloomIds, id) >= 0 && !order.Contains(id)) order.Add(id);
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 currency table could not be read: " + e.Message); }
            if (order.Count == 0) order.AddRange(StockOrder);
            // whatever DD1 does not list keeps the estate's own order
            foreach (var id in EstateState.HeirloomIds)
                if (!order.Contains(id)) order.Add(id);
            return order.ToArray();
        }

        private static Grid G(DarkestFile file, string block, int columns, float startX)
        {
            var entry = file?.Find(block);
            return new Grid
            {
                Columns = entry != null && entry.Has("number_of_columns") ? Mathf.Max(1, entry.Int("number_of_columns", 0, columns)) : columns,
                Start = V(file, block, "start_pos", startX, 0f),
                Pitch = V(file, block, "offset", 80f, 160f),
                Centred = entry == null || !entry.Has("is_centred") || entry.Int("is_centred", 0, 1) != 0
            };
        }

        private static Vector2 V(DarkestFile file, string block, string key, float x, float y)
        {
            var entry = file?.Find(block);
            return entry != null && entry.Values(key).Count >= 2 ? entry.Vector2(key) : new Vector2(x, y);
        }

        private static float F(DarkestFile file, string block, string key, float fallback)
        {
            var entry = file?.Find(block);
            return entry != null && entry.Has(key) ? entry.Float(key, 0, fallback) : fallback;
        }
    }
}
