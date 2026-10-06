using System.Collections.Generic;
using DD2Estate.Dd1;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// Where DD1 puts the parts of its raid screen, read from the player's install. DD1 keeps these numbers in
    /// scripts/layout/*.darkest (the raid screen, its panels and overlays), shared/hero/hero.layout.darkest
    /// (what the hero panel holds), shared/inventory/inventory.layout.darkest (an item in a grid),
    /// shared/tooltip and shared/confirm_dialog. Everything is in pixels of DD1's 1920x1080 screen, y down.
    ///
    /// The value written beside each read is DD1's stock value and is used only when the file or the key
    /// cannot be read: a FALLBACK, not a second source. docs/recon/dd1-raid-ui.md lists every entry with what
    /// it places; tools/preview_raid_hud.py reads the same files with the same fallbacks.
    /// </summary>
    internal sealed class RaidLayout
    {
        private const string Dir = "scripts/layout/";

        private static RaidLayout _current, _stock;

        /// <summary>
        /// Read once the DD1 install is found. Before that the stock values answer, and a later call looks
        /// for the install again.
        /// </summary>
        public static RaidLayout Current
        {
            get
            {
                if (_current != null) return _current;
                if (Dd1Install.Found) return _current = new RaidLayout(true);
                return _stock ?? (_stock = new RaidLayout(false));
            }
        }

        // ---- screen.raid.darkest ---------------------------------------------------------------------
        /// <summary>screen_guide: the two 720 px panels lie between safe_left and safe_right, under panel_top.</summary>
        public readonly float XCentre, SafeLeft, SafeRight, PanelTop;
        /// <summary>panel_transition_bar.pos: panels/panel_transition.png over the seam of scene and panels.</summary>
        public readonly Vector2 Transition;
        /// <summary>overlays: where a hero of rank 1 stands and the step to the next rank.</summary>
        public readonly Vector2 HeroStart, HeroSpacing;

        /// <summary>torch_layout: overlays/torch.png hangs from pos_y, centred; the two gauges run out from its middle.</summary>
        public readonly float TorchY, TorchFade;
        public readonly Vector2 TorchGaugeOffset, TorchGaugeSize, TorchFlame;
        /// <summary>torch_info: the hot areas of the torch and of its strip, and the tooltip's offset.</summary>
        public readonly Vector2 TorchHot, TorchHotSize, TorchStripHot, TorchStripHotSize, TorchTooltip;

        /// <summary>basic_scroll (scrolls/event_scroll_basic.png): hunger and plain questions.</summary>
        public readonly Vector2 BasicPos, BasicInset, BasicButtonSize, BasicOk, BasicCancel;
        public readonly float BasicHeaderY, BasicBodyY, BasicBodyWidth, BasicButtonY;
        /// <summary>basic_scroll.ok_tooltip_width: how wide the text of the two bowls' tooltips may run.</summary>
        public readonly float BasicTooltipWidth;
        /// <summary>sidebar_scroll (scrolls/event_scroll_sidebar.png): curios and obstacles.</summary>
        public readonly Vector2 SidebarPos, SidebarBody, SidebarInvestigate, SidebarPass, SidebarItemSlot, SidebarItemSlotSize;
        public readonly float SidebarHeaderY, SidebarBodyWidth;
        /// <summary>sidebar_scroll: how wide the text of its three tooltips may run (the hand, the way past, the item slot).</summary>
        public readonly float SidebarInvestigateTooltipWidth, SidebarPassTooltipWidth, SidebarItemSlotTooltipWidth;
        /// <summary>
        /// choice_button.frame_offset: from a scroll's button position to the middle of the top edge of the
        /// button's picture (124x69: the picture and scrolls/choice_button_frame.png, its two rails, share a
        /// place). Measured in DD1's own frames (_lab/dd1_ref/raid: the hand of the curio scroll stands at
        /// 1174,430 for investigate_button_pos -152 240 of a scroll hung at 1348,200; the loot scroll's two
        /// buttons and the quest-complete choice's fit the same rule).
        /// </summary>
        public readonly Vector2 ChoiceFrame;
        /// <summary>meal_scroll and camp_layout: the camp's two scrolls.</summary>
        public readonly float MealHeaderY, MealButtonY, MealButtonOffset, MealTooltipWidth, RespiteDescriptionWidth;
        public readonly Vector2 MealTooltipOffset, RespiteScroll, RespiteTitle, RespitePoints, RespiteDescription, RespiteRest, RespiteRestText;

        /// <summary>quest_info: the quest's scroll in the top left corner, the retreat flag, the quest-complete choice.</summary>
        public readonly Vector2 QuestPos, QuestSize, QuestGlow, QuestButton, QuestName, QuestGoals, QuestGoalSpacing;
        public readonly Vector2 QuestCompleteButton, QuestCompleteText, QuestRetreat, QuestRetreatTooltip;
        public readonly Vector2 CompleteMid, CompleteFrame, CompleteReturn, CompleteContinue, CompleteTooltipOffset;
        public readonly float CompleteTooltipWidth;
        /// <summary>quest_info.complete_blur_time: the seconds over which the screen behind the choice blurs.</summary>
        public readonly float CompleteBlurTime;

        /// <summary>
        /// announcement: the four points DD1's banner (overlays/announcement_frame.png) is centred on, and where
        /// its words stand from there (the middle of their line, the top of its cell).
        /// </summary>
        public readonly Vector2 AnnounceLeft, AnnounceCentreBottom, AnnounceCentreTop, AnnounceRight, AnnounceText;
        // announcement_times: how long a banner of each kind is up, in seconds
        private readonly Dictionary<string, float> _announceTimes = new Dictionary<string, float>();

        // ---- screen.raid.status_bars.darkest -----------------------------------------------------------
        /// <summary>status_bars: the health bar and the stress pips under a hero's feet.</summary>
        public readonly float TrayCharX, TrayY, TrayHealthHeight, TrayHealthWidth, TrayStressSpacing;
        public readonly Vector2 TrayHealthOffset, TrayStressOffset;
        /// <summary>
        /// status_bars: the status icons over the health bar (overlays/tray_*.png). Two rows that part beside the
        /// tray's middle: one ends at tray_icon_left_offset and grows to the left, the other starts at
        /// tray_icon_right_offset and grows to the right, an icon's hot spot (tray_icon_hot_spot_size) a step.
        /// </summary>
        public readonly Vector2 TrayIconSize, TrayIconLeft, TrayIconRight;
        public readonly float TrayIconLeftSpacing, TrayIconRightSpacing;
        /// <summary>
        /// status_bars: where the pointer is told a hero's health and stress (status_bar_tooltip_hot_area_offset,
        /// status_bar_tooltip_hot_area_height: over the bars) and where the tooltip stands (status_bar_tooltip_offset),
        /// all from the tray's origin.
        /// </summary>
        public readonly Vector2 TrayBarsHot, TrayBarsTooltip;
        public readonly float TrayBarsHotHeight;
        /// <summary>status_bars.icon_world_y_offset: how far above the ground DD1 hangs what it shows on a hero.</summary>
        public readonly float HeroMiddle;
        /// <summary>status_bar_tray_icon_pulse (screen.raid.darkest): an icon swells and settles as it appears.</summary>
        public readonly float TrayIconPulseOut, TrayIconPulseIn, TrayIconPulseScale;

        // ---- panel.banner.darkest / panel.hero.darkest / shared/hero/hero.layout.darkest -----------------
        public readonly Vector2 BannerBackground, BannerPortrait, BannerName, BannerAbility;
        public readonly float BannerClassY, BannerAbilitySpacing;
        public readonly Color BannerNameColour, BannerClassColour;
        public readonly Vector2 HeroHealth, HeroStress, HeroStat, HeroEquipment, HeroTrinket;
        public readonly Color HeroHealthColour, HeroStressColour;
        public readonly Vector2 TrinketStart, TrinketOffset, WeaponPos, ArmourPos, EquipIcon, EquipLevel, EquipTooltip, StatSpacing, StatValue;
        /// <summary>hero_stats_layout.icon_offset: the arrow of a buffed or debuffed stat, from its row's corner.</summary>
        public readonly Vector2 StatIcon;

        // ---- panel.map.darkest / panel.tab.darkest / pannel.inventory.darkest ----------------------------
        public readonly float MapTile, MapScale, MapFollowDelay, MapZoomStep, MapMinZoom, MapMaxZoom;
        /// <summary>map_layout.clip: left, right, top, bottom of the map's window inside the panel.</summary>
        public readonly Rect MapClip;
        public readonly float IndicatorBounce, IndicatorUpTime, IndicatorDownTime, IndicatorMaxScale, IndicatorMinScale;
        public readonly Vector2 TabPos, TabSize, HomeButton, PartyOrderButton;
        /// <summary>
        /// Where the tooltips of the two buttons beside the map begin, counted from the panels' left corner
        /// (home_button_layout.tooltip_offset, reorder_party_layout.tooltip_offset), and how wide they may be.
        /// </summary>
        public readonly Vector2 HomeTooltip, PartyOrderTooltip;
        /// <summary>
        /// DD1's camp bonus icons beside the retreat flag, from quest_info.pos (quest_info.camp_bonus_icon_offset,
        /// _spacing, _scale, _tooltip_max_width).
        /// </summary>
        public readonly Vector2 CampBonusIcon, CampBonusSpacing;
        public readonly float CampBonusScale, CampBonusTooltipWidth;
        public readonly float SideButtonTooltipWidth;
        /// <summary>What is left of the party-order button's colour while the party stands in its own order (reorder_party_layout.desat_level).</summary>
        public readonly float PartyOrderDesaturation;
        public readonly int BagColumns;
        public readonly Vector2 BagStart, BagOffset;

        // ---- shared/inventory/inventory.layout.darkest -------------------------------------------------
        public readonly Vector2 ItemIconSize, ItemIconOffset, ItemAmount, ItemCost, ItemTooltipOffset;
        /// <summary>inventory_curio_tracker_layout.icon_offset: the curio tracker's mark on a card, from the cell's corner.</summary>
        public readonly Vector2 ItemTracker;
        public readonly float ItemTooltipWidth;

        // ---- overlay.loot.darkest -----------------------------------------------------------------------
        public readonly Vector2 LootTitle, LootDescription, LootTakeAll, LootClose;
        public readonly float LootDescriptionWidth, LootTilesY, LootTileOffset, LootBackdropY;
        public readonly int LootMaxItems, LootMinItems;

        // ---- user_information/user_information.darkest, shared/ui.layout.darkest -----------------------------
        /// <summary>user_information_popup: the line stands y_offset from the pointer and is up for time seconds.</summary>
        public readonly float UserInformationY, UserInformationTime;
        /// <summary>
        /// bark_layout: a hero's speech balloon (scrolls/bark_balloon.png). offset: from the hero's feet to where
        /// the balloon's tail points (x right, y up); tail: that point in the balloon's picture; text: the words'
        /// box in the picture (left, right, top, bottom); the words come a letter every char_delay seconds and
        /// the balloon stays lifetime_per_char a letter after them, lifetime_min at the least.
        /// </summary>
        public readonly Vector2 BarkOffset, BarkTail;
        public readonly Rect BarkText;
        public readonly float BarkCharDelay, BarkLifetimeMin, BarkLifetimePerChar;

        // ---- shared/tooltip, shared/confirm_dialog -------------------------------------------------------
        public readonly float TooltipBorder;
        public readonly Vector2 TooltipOffset, TooltipTextOffset;
        public readonly Vector2 ConfirmBase, ConfirmQuestion, ConfirmAnswersOffset, ConfirmAnswersStart, ConfirmAnswerSpacing, ConfirmButtonSize, ConfirmSelected;
        public readonly float ConfirmQuestionWidth;

        private RaidLayout(bool read)
        {
            var screen = read ? DarkestFile.Load(Dir + "screen.raid.darkest") : null;
            var bars = read ? DarkestFile.Load(Dir + "screen.raid.status_bars.darkest") : null;
            var banner = read ? DarkestFile.Load(Dir + "panel.banner.darkest") : null;
            var hero = read ? DarkestFile.Load(Dir + "panel.hero.darkest") : null;
            var map = read ? DarkestFile.Load(Dir + "panel.map.darkest") : null;
            var tab = read ? DarkestFile.Load(Dir + "panel.tab.darkest") : null;
            var bag = read ? DarkestFile.Load(Dir + "pannel.inventory.darkest") : null;      // DD1's own spelling
            var loot = read ? DarkestFile.Load(Dir + "overlay.loot.darkest") : null;
            var heroShared = read ? DarkestFile.Load("shared/hero/hero.layout.darkest") : null;
            var item = read ? DarkestFile.Load("shared/inventory/inventory.layout.darkest") : null;
            var tooltip = read ? DarkestFile.Load("shared/tooltip/tooltip.layout.darkest") : null;
            var confirm = read ? DarkestFile.Load("shared/confirm_dialog/confirm_dialog.layout.darkest") : null;

            XCentre = F(screen, "screen_guide", "x_centre", 960f);
            SafeLeft = F(screen, "screen_guide", "safe_left", 240f);
            SafeRight = F(screen, "screen_guide", "safe_right", 1680f);
            PanelTop = F(screen, "screen_guide", "panel_top", 720f);
            Transition = V(screen, "panel_transition_bar", "pos", 0f, 710f);
            HeroStart = V(screen, "overlays", "hero_start_pos", 788f, 680f);
            HeroSpacing = V(screen, "overlays", "hero_spacing", -168f, 0f);

            TorchY = F(screen, "torch_layout", "pos_y", 28f);
            TorchGaugeOffset = V(screen, "torch_layout", "gauge_offset", 26f, 89f);
            TorchGaugeSize = V(screen, "torch_layout", "gauge_size", 400f, 4f);
            TorchFade = F(screen, "torch_layout", "fade_amount", 0.05f);
            TorchFlame = V(screen, "torch_layout", "flamepos", 960f, 100f);
            TorchHot = V(screen, "torch_info", "mouseOverAreaOffset", 0f, 0f);
            TorchHotSize = V(screen, "torch_info", "mouseOverAreaSize", 200f, 130f);
            TorchStripHot = V(screen, "torch_info", "stripMouseOverAreaOffset", 0f, 70f);
            TorchStripHotSize = V(screen, "torch_info", "stripMouseOverAreaSize", 860f, 24f);
            TorchTooltip = V(screen, "torch_info", "tooltip_offset", 0f, -70f);

            BasicPos = V(screen, "basic_scroll", "pos", 1342f, 140f);
            BasicHeaderY = F(screen, "basic_scroll", "header_y", 48f);
            BasicInset = V(screen, "basic_scroll", "inset_offset", 0f, 145f);
            BasicBodyY = F(screen, "basic_scroll", "body_y", 308f);
            BasicBodyWidth = F(screen, "basic_scroll", "body_width", 345f);
            BasicButtonY = F(screen, "basic_scroll", "button_y", 456f);
            BasicButtonSize = V(screen, "basic_scroll", "button_size", 384f, 36f);
            BasicOk = V(screen, "basic_scroll", "ok_button_pos", -150f, 400f);
            BasicCancel = V(screen, "basic_scroll", "cancel_button_pos", 78f, 400f);
            BasicTooltipWidth = F(screen, "basic_scroll", "ok_tooltip_width", 200f);

            SidebarPos = V(screen, "sidebar_scroll", "pos", 1348f, 200f);
            SidebarHeaderY = F(screen, "sidebar_scroll", "header_y", 44f);
            SidebarBodyWidth = F(screen, "sidebar_scroll", "body_width", 330f);
            // the file writes "128d" for the body's y: a slip of the hand DD1's reader takes as 128
            SidebarBody = new Vector2(F(screen, "sidebar_scroll", "body", -152f), Number(screen?.Find("sidebar_scroll")?.String("body", 1), 128f));
            SidebarInvestigate = V(screen, "sidebar_scroll", "investigate_button_pos", -152f, 240f);
            SidebarPass = V(screen, "sidebar_scroll", "pass_button_pos", 75f, 240f);
            SidebarItemSlot = V(screen, "sidebar_scroll", "item_slot_pos", -34f, 230f);
            SidebarItemSlotSize = V(screen, "sidebar_scroll", "item_slot_size", 80f, 160f);
            SidebarInvestigateTooltipWidth = F(screen, "sidebar_scroll", "investigate_tooltip_text_width", 160f);
            SidebarPassTooltipWidth = F(screen, "sidebar_scroll", "pass_tooltip_text_width", 160f);
            SidebarItemSlotTooltipWidth = F(screen, "sidebar_scroll", "item_slot_tooltip_text_width", 160f);

            ChoiceFrame = V(screen, "choice_button", "frame_offset", 40f, -10f);

            MealHeaderY = F(screen, "meal_scroll", "headerY", 52f);
            MealButtonY = F(screen, "meal_scroll", "buttonY", 148f);
            MealButtonOffset = F(screen, "meal_scroll", "buttonOffset", 74f);
            MealTooltipOffset = V(screen, "meal_scroll", "tooltipOffset", 170f, -10f);
            MealTooltipWidth = F(screen, "meal_scroll", "tooltipWidth", 180f);
            RespiteScroll = V(screen, "camp_layout", "respite_scroll_pos", 732f, 60f);
            RespiteTitle = V(screen, "camp_layout", "respite_title_offset", 60f, 44f);
            RespitePoints = V(screen, "camp_layout", "respite_points_offset", 324f, 44f);
            RespiteDescription = V(screen, "camp_layout", "respite_description_offset", 56f, 130f);
            RespiteDescriptionWidth = F(screen, "camp_layout", "respite_description_width", 360f);
            RespiteRest = V(screen, "camp_layout", "respite_rest_offset", 100f, 200f);
            RespiteRestText = V(screen, "camp_layout", "respite_rest_text_offset", 128f, 17f);

            QuestPos = V(screen, "quest_info", "pos", 12f, 20f);
            QuestSize = V(screen, "quest_info", "size", 300f, 150f);
            QuestGlow = V(screen, "quest_info", "info_glow_offset", -14f, 0f);
            QuestButton = V(screen, "quest_info", "info_button_offset", 65f, 58f);
            QuestName = V(screen, "quest_info", "info_text_name_offset", 110f, 26f);
            QuestGoals = V(screen, "quest_info", "info_text_goals_start_offset", 110f, 58f);
            QuestGoalSpacing = V(screen, "quest_info", "info_text_goals_spacing", 0f, 30f);
            QuestCompleteButton = V(screen, "quest_info", "complete_button_offset", 0f, 30f);
            QuestCompleteText = V(screen, "quest_info", "complete_text_offset", 120f, 45f);
            QuestRetreat = V(screen, "quest_info", "retreat_button_offset", 68f, 120f);
            QuestRetreatTooltip = V(screen, "quest_info", "retreat_tooltip_offset", 148f, -18f);
            CampBonusIcon = V(screen, "quest_info", "camp_bonus_icon_offset", 140f, 110f);
            CampBonusSpacing = V(screen, "quest_info", "camp_bonus_icon_spacing", 50f, 0f);
            CampBonusScale = F(screen, "quest_info", "camp_bonus_icon_scale", 0.5f);
            CampBonusTooltipWidth = F(screen, "quest_info", "camp_bonus_icon_tooltip_max_width", 240f);
            CompleteMid = V(screen, "quest_info", "complete_mid_screen_pos", 948f, 590f);
            CompleteFrame = V(screen, "quest_info", "complete_choice_shared_frame_pos", 0f, 55f);
            CompleteReturn = V(screen, "quest_info", "complete_return_to_hamlet_pos", -170f, 98f);
            CompleteContinue = V(screen, "quest_info", "complete_continue_raid_pos", 90f, 98f);
            CompleteTooltipWidth = F(screen, "quest_info", "complete_tooltip_width", 200f);
            CompleteTooltipOffset = V(screen, "quest_info", "complete_tooltip_offset", 0f, 10f);
            CompleteBlurTime = F(screen, "quest_info", "complete_blur_time", 0.2f);

            AnnounceLeft = V(screen, "announcement", "frame_pos_left", 572f, 184f);
            AnnounceCentreBottom = V(screen, "announcement", "frame_pos_centre_bottom", 960f, 668f);
            AnnounceCentreTop = V(screen, "announcement", "frame_pos_centre_top", 960f, 210f);
            AnnounceRight = V(screen, "announcement", "frame_pos_right", 1348f, 184f);
            AnnounceText = V(screen, "announcement", "text_offset", 0f, -29f);
            foreach (var stock in StockAnnounceTimes) _announceTimes[stock.Key] = F(screen, "announcement_times", stock.Key, stock.Value);
            TrayIconPulseOut = F(screen, "status_bar_tray_icon_pulse", "pulse_out_duration", 0.25f);
            TrayIconPulseIn = F(screen, "status_bar_tray_icon_pulse", "pulse_in_duration", 0.125f);
            TrayIconPulseScale = F(screen, "status_bar_tray_icon_pulse", "pulse_scale", 1.25f);

            TrayCharX = F(bars, "status_bars", "char_x_offset", -50f);
            TrayY = F(bars, "status_bars", "y_pos", 698f);
            TrayHealthOffset = V(bars, "status_bars", "health_bar_offset", 50f, 0f);
            TrayHealthHeight = F(bars, "status_bars", "health_bar_height", 10f);
            // health_bar_widths lists one width per size of monster; a hero is of size one
            TrayHealthWidth = F(bars, "status_bars", "health_bar_widths", 100f);
            TrayStressOffset = V(bars, "status_bars", "stress_offset", -1f, 12f);
            TrayStressSpacing = F(bars, "status_bars", "stress_spacing", 10f);
            TrayIconSize = V(bars, "status_bars", "tray_icon_hot_spot_size", 20f, 24f);
            TrayIconLeft = V(bars, "status_bars", "tray_icon_left_offset", 58f, -38f);
            TrayIconLeftSpacing = F(bars, "status_bars", "tray_icon_left_spacing", 20f);
            TrayIconRight = V(bars, "status_bars", "tray_icon_right_offset", 62f, -38f);
            TrayIconRightSpacing = F(bars, "status_bars", "tray_icon_right_spacing", 20f);
            HeroMiddle = F(bars, "status_bars", "icon_world_y_offset", 149f);
            TrayBarsHot = V(bars, "status_bars", "status_bar_tooltip_hot_area_offset", 50f, -10f);
            TrayBarsHotHeight = F(bars, "status_bars", "status_bar_tooltip_hot_area_height", 35f);
            TrayBarsTooltip = V(bars, "status_bars", "status_bar_tooltip_offset", 50f, -12f);

            BannerBackground = V(banner, "background_layout", "pos", -33f, 0f);
            BannerPortrait = V(banner, "portrait_layout", "pos", 32f, 32f);
            BannerName = V(banner, "name_layout", "pos", 272f, 38f);
            BannerClassY = F(banner, "name_layout", "class_y", 76f);
            BannerNameColour = C(banner, "name_layout", "hero_name_colour", new Color32(177, 161, 108, 255));
            BannerClassColour = C(banner, "name_layout", "hero_class_colour", new Color32(154, 152, 143, 175));
            BannerAbility = V(banner, "ability_layout", "pos", 280f, 35f);
            BannerAbilitySpacing = F(banner, "ability_layout", "spacing", 76f);

            HeroHealth = V(hero, "health_layout", "pos", 130f, 11f);
            HeroHealthColour = C(hero, "health_layout", "colour", new Color32(0xc0, 0, 0, 255));
            HeroStress = V(hero, "stress_layout", "pos", 130f, 40f);
            HeroStressColour = C(hero, "stress_layout", "colour", new Color32(150, 150, 150, 255));
            HeroStat = V(hero, "stat_layout", "pos", 60f, 72f);
            HeroEquipment = V(hero, "hero_equipment", "pos", 238f, 0f);
            HeroTrinket = V(hero, "hero_trinket", "pos", 453f, 0f);
            TrinketStart = V(heroShared, "hero_trinket_grid_layout", "start_pos", 32f, 52f);
            TrinketOffset = V(heroShared, "hero_trinket_grid_layout", "offset", 92f, 160f);
            WeaponPos = V(heroShared, "hero_equipment_layout", "weapon_pos", 4f, 0f);
            ArmourPos = V(heroShared, "hero_equipment_layout", "armour_pos", 95f, 0f);
            EquipIcon = V(heroShared, "hero_equipment_layout", "icon_offset", 29f, 52f);
            EquipLevel = V(heroShared, "hero_equipment_layout", "level_offset", 90f, 12f);
            EquipTooltip = V(heroShared, "hero_equipment_layout", "tooltip_offset", 120f, 52f);
            StatSpacing = V(heroShared, "hero_stats_layout", "spacing", 160f, 20f);
            StatValue = V(heroShared, "hero_stats_layout", "value_offset", 112f, 0f);
            StatIcon = V(heroShared, "hero_stats_layout", "icon_offset", -26f, 2f);

            MapTile = F(map, "map_layout", "tilesize", 24f);
            MapScale = F(map, "map_layout", "scale", 1f);
            MapFollowDelay = F(map, "map_layout", "manual_to_follow_delay", 10f);
            MapZoomStep = F(map, "map_layout", "zoom_key_value", 0.25f);
            var clip = map?.Find("map_layout");
            MapClip = clip != null && clip.Values("clip").Count >= 4
                ? Rect.MinMaxRect(clip.Float("clip", 0), clip.Float("clip", 2), clip.Float("clip", 1), clip.Float("clip", 3))
                : Rect.MinMaxRect(16f, 19f, 665f, 340f);
            MapMinZoom = F(map, "input", "min_zoom_scale", 0.35f);
            MapMaxZoom = F(map, "input", "max_zoom_scale", 1.5f);
            IndicatorBounce = F(map, "indicator_layout", "bounce", 5f);
            IndicatorUpTime = F(map, "indicator_layout", "up_time", 0.5f);
            IndicatorDownTime = F(map, "indicator_layout", "down_time", 0.5f);
            IndicatorMaxScale = F(map, "indicator_layout", "max_scale", 1.05f);
            IndicatorMinScale = F(map, "indicator_layout", "min_scale", 0.95f);
            TabPos = V(map, "tab_placement", "pos", 672f, 252f);
            TabSize = V(map, "tab_placement", "size", 48f, 90f);
            HomeButton = V(map, "home_button_layout", "button_pos", 677f, 24f);
            HomeTooltip = V(map, "home_button_layout", "tooltip_offset", 1206f, 28f);
            SideButtonTooltipWidth = F(map, "home_button_layout", "tooltip_width", 200f);
            PartyOrderButton = V(tab, "reorder_party_layout", "button_pos", 678f, 90f);
            PartyOrderTooltip = V(tab, "reorder_party_layout", "tooltip_offset", 1206f, 90f);
            PartyOrderDesaturation = F(tab, "reorder_party_layout", "desat_level", 0.2f);
            BagColumns = Mathf.Max(1, Mathf.RoundToInt(F(bag, "raid_inventory_panel_grid_layout", "number_of_columns", 8f)));
            BagStart = V(bag, "raid_inventory_panel_grid_layout", "start_pos", 20f, 28f);
            BagOffset = V(bag, "raid_inventory_panel_grid_layout", "offset", 80f, 160f);

            ItemIconSize = V(item, "inventory_item_layout", "icon_size", 72f, 144f);
            ItemIconOffset = V(item, "inventory_item_layout", "icon_offset", 4f, 0f);
            ItemAmount = V(item, "inventory_item_layout", "amount_text_offset", 14f, 4f);
            ItemCost = V(item, "inventory_item_layout", "cost_offset", 37f, 157f);
            ItemTracker = V(item, "inventory_curio_tracker_layout", "icon_offset", 24f, 118f);
            ItemTooltipOffset = V(item, "inventory_item_tooltip_layout", "offset", 85f, 0f);
            ItemTooltipWidth = F(item, "inventory_item_tooltip_layout", "text_width", 200f);

            LootTitle = V(loot, "loot_title", "pos", 228f, 40f);
            LootDescription = V(loot, "loot_description", "pos", 228f, 136f);
            LootDescriptionWidth = F(loot, "loot_description", "width", 350f);
            LootTilesY = F(loot, "loot_tiles", "startPosY", 195f);
            LootTileOffset = F(loot, "loot_tiles", "offset", 74f);
            LootTakeAll = V(loot, "loot_buttons", "take_all_pos", 80f, 358f);
            LootClose = V(loot, "loot_buttons", "close_pos", 306f, 358f);
            LootBackdropY = F(loot, "loot_background", "dynamic_backdrop_y_offset", 10f);
            LootMaxItems = Mathf.RoundToInt(F(loot, "loot_background", "max_items", 9f));
            LootMinItems = Mathf.RoundToInt(F(loot, "loot_background", "min_items", 4f));

            var information = read ? DarkestFile.Load("user_information/user_information.darkest") : null;
            UserInformationY = F(information, "user_information_popup", "y_offset", -60f);
            UserInformationTime = F(information, "user_information_popup", "time", 4f);
            var ui = read ? DarkestFile.Load("shared/ui.layout.darkest") : null;
            BarkOffset = V(ui, "bark_layout", "offset", -25f, 300f);
            BarkTail = V(ui, "bark_layout", "tail", 62f, 180f);
            var bark = ui?.Find("bark_layout");
            BarkText = bark != null && bark.Values("text").Count >= 4
                ? Rect.MinMaxRect(bark.Float("text", 0), bark.Float("text", 2), bark.Float("text", 1), bark.Float("text", 3))
                : Rect.MinMaxRect(40f, 24f, 320f, 122f);
            BarkCharDelay = F(ui, "bark_layout", "char_delay", 0.03f);
            BarkLifetimeMin = F(ui, "bark_layout", "lifetime_min", 1f);
            BarkLifetimePerChar = F(ui, "bark_layout", "lifetime_per_char", 0.06f);

            TooltipBorder = F(tooltip, "tooltip_background_layout", "border_texture_threshold", 0.15f);
            TooltipOffset = V(tooltip, "tooltip_background_layout", "offset", 12f, -12f);
            TooltipTextOffset = V(tooltip, "tooltip_background_layout", "text_offset", 4f, 0f);
            ConfirmBase = V(confirm, "confirm_dialog_layout", "base_pos", 960f, 200f);
            ConfirmAnswersOffset = V(confirm, "confirm_dialog_layout", "answers_offset", -12f, -20f);
            ConfirmQuestion = V(confirm, "confirm_dialog_question_layout", "text_offset", -18f, 200f);
            ConfirmQuestionWidth = F(confirm, "confirm_dialog_question_layout", "text_width", 520f);
            ConfirmAnswersStart = V(confirm, "confirm_dialog_answers_layout", "start_offset", 0f, 390f);
            ConfirmAnswerSpacing = V(confirm, "confirm_dialog_answers_layout", "spacing", 0f, 55f);
            ConfirmButtonSize = V(confirm, "confirm_dialog_answer_layout", "button_size", 500f, 38f);
            ConfirmSelected = V(confirm, "confirm_dialog_answer_layout", "selected_overlay_offset", 0f, -24f);
        }

        // FALLBACK: announcement_times of DD1's stock screen.raid.darkest.
        private static readonly Dictionary<string, float> StockAnnounceTimes = new Dictionary<string, float>
        {
            { "new_quirk", 1.5f }, { "surprised", 1f }, { "deaths_door", 1.2f }, { "round", 0.5f }, { "trap", 1f }, { "disarm", 0.5f },
            { "curio", 0.5f }, { "curio_purge", 1.2f }
        };

        /// <summary>announcement_times: the seconds a banner of this kind is up; a kind DD1 does not list is up a second.</summary>
        public float AnnounceTime(string kind) => kind != null && _announceTimes.TryGetValue(kind, out var seconds) ? seconds : 1f;

        /// <summary>
        /// The corner of a scroll button's picture, <paramref name="width"/> wide, for a position of the layout
        /// (investigate_button_pos, take_all_pos, complete_return_to_hamlet_pos...): see <see cref="ChoiceFrame"/>.
        /// </summary>
        public Vector2 ChoiceCorner(Vector2 position, float width) => new Vector2(position.x + ChoiceFrame.x - width * 0.5f, position.y + ChoiceFrame.y);

        /// <summary>The top left corner of the left panel (the hero's banner and panel).</summary>
        public Vector2 LeftPanel => new Vector2(SafeLeft, PanelTop);

        /// <summary>The top left corner of the right panel (map or inventory): the two meet at the screen's middle.</summary>
        public Vector2 RightPanel => new Vector2(XCentre, PanelTop);

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

        // "#c00000" or "150 150 150 255"
        private static Color C(DarkestFile file, string block, string key, Color fallback)
        {
            var values = file?.Find(block)?.Values(key);
            if (values == null || values.Count == 0) return fallback;
            if (values[0].StartsWith("#")) return ColorUtility.TryParseHtmlString(values[0], out var parsed) ? parsed : fallback;
            if (values.Count < 3) return fallback;
            return new Color32((byte)Number(values[0], 0f), (byte)Number(values[1], 0f), (byte)Number(values[2], 0f), (byte)(values.Count > 3 ? Number(values[3], 255f) : 255f));
        }

        // The leading number of a token ("128d" is 128).
        private static float Number(string text, float fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            var end = 0;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '-' || text[end] == '.')) end++;
            return end > 0 && float.TryParse(text.Substring(0, end), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }
    }
}
