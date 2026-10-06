#!/usr/bin/env python3
"""Compose the expedition's screens the way DD1 lays out its raid screen, from the player's own Darkest
Dungeon (1) install.

Reference for the layout in src/DD2Estate/Dungeon/DungeonHud.cs, RaidHeroPanel.cs, RaidOverlays.cs,
RaidScrolls.cs, Minimap.cs, InventoryGrid.cs, CampScreen.cs and ProvisionScreen.cs, and for the numbers
RaidLayout.cs reads: change one, change the other. Nothing from DD1 is copied into the repo; output goes to
_lab/ (gitignored).

    python tools/preview_raid_hud.py [--dd1 <install>] [--out <dir>] [--light 0..100] [--dungeon crypts]

Writes (1920x1080):
    raid_hud.png            the corridor with DD1's raid panel: banner and hero panel, the map, the torch, the
                            quest info, the status bars under the heroes
    raid_hud_inventory.png  the same with the inventory tab and an item's tooltip
    raid_curio.png          a curio: DD1's sidebar scroll (heading, text, the hand, the item slot, the way past)
    raid_obstacle.png       an obstacle in the same scroll (no way past)
    raid_hunger.png         hunger: DD1's basic scroll with its picture, Eat and Starve
    raid_plain.png          a plain question (the bag is full) in the basic scroll
    raid_loot.png           DD1's loot scroll: cards, Take All, Close
    raid_loot_battle.png    the same after a won fight ("Victory!" / "You have found:"), as DD1's own frame has it
    raid_loot_fallen.png    the same for what a hero who died was wearing ("Reclaimed:"): trinket cards
    raid_loot_wide.png      the same with more cards than the scroll's own strip holds
    raid_complete.png       the quest complete: the seal in the corner and DD1's choice in mid screen
    raid_retreat.png        DD1's confirm dialog over the screen (the retreat flag was clicked)
    raid_camp_meal.png      the camp: the Repast scroll, camping skills in the banner
    raid_camp_respite.png   the camp: the Respite scroll
    raid_provision.png      the provision screen
    raid_feedback.png       DD1's banner low and at both sides ("TRAP!", "Surprised!") and pop texts over the party,
                            each with its last place drawn faint
    raid_feedback_icons.png the banner high and low ("... is at Death's Door!", "New Quirk: ..."), the status icons
                            over the health bars and the tooltip of one

What is real and what stands in: every picture, font, colour and number is DD1's, read from the install the
way the plugin reads it. DD2 supplies the heroes in the game: their models in the scene (grey figures
here), their portraits and skill icons (DD1's own portraits and ability icons of the same classes here)
and their trinkets (DD1 trinket cards cut square here).

Two things the game draws with DD2's look since 2026-10-06 and this preview still draws with DD1's (what the
game shows with [Look] Torch = dd1 and [Look] HeroBars = dd1): the torch (Dungeon/Dd2Torch.cs) and the bars
under the heroes (Dungeon/Dd2Bars.cs). They are copies of the widgets of DD2's fights, taken from the running
game, and cannot be drawn offline.

DD1's raid screen (scripts/layout/*.darkest; every number below is read from there, the stock value beside
it in LAYOUT is a FALLBACK):
  * screen.raid.darkest, screen_guide: the scene is the top 720 px (panel_top); under it two 720 px panels
    between safe_left 240 and safe_right 1680, panels/side_decor.png left and (mirrored) right of them.
  * Left panel: panels/panel_banner.png (754x136) at background_layout.pos -33,0, panels/panel_hero.png
    (720x224) under it. panel.banner.darkest: portrait 32,32 (85 px), the name's and the class's right edge at
    272 (y 38 and 76), skill icons from 280,35 every 76 px over the numbers 1..5 drawn into the banner.
    panel.hero.darkest + shared/hero/hero.layout.darkest: health and stress numbers ("33 / 33") begin at 130,11 and
    130,40; DD1's six stat rows from 60,72 every 20 px, values 112 px in, a row a quirk or trinket has moved in
    gold, the arrow of what a camp gave at -26,2 of its row; weapon and armour cards at 238+4+29 and 238+95+29, y 52, their levels at +90,12
    in the level's colour (equipment_level_0..4); trinkets at 453+32+4 and one 92 px further, y 52.
  * Right panel: panels/panel_map.png or panel_inventory.png (720x360), each drawn with its own tab lit.
    panel.map.darkest: the map's window is clip 16..665 x 19..340, a tile is 24 px, a room icon 64 px; the
    home button at 677,24; tab_placement 672,252 (48x90) is the lower tab, the map's the one above.
    pannel.inventory.darkest: 8 columns from 20,28, cells 80x160; shared/inventory/inventory.layout.darkest:
    the 72x144 card 4 px into its cell, the amount at 14,4.
  * screen.raid.status_bars.darkest: under each hero a health bar (100x10) at y 698 and ten stress pips
    12 px lower, 10 px apart, from 51 px left of the hero. Over the bar the status icons (overlays/tray_*.png,
    24 px on a 20x24 hot spot): one row ends at tray_icon_left_offset 58,-38 and grows to the left, the other
    starts at tray_icon_right_offset 62,-38 and grows to the right, 20 px a step.
  * announcement: overlays/announcement_frame.png (619x136) centred on frame_pos_left 572,184, frame_pos_right
    1348,184, frame_pos_centre_top 960,210 or frame_pos_centre_bottom 960,668; the words (banner_header) hang
    from text_offset 0,-29, which puts their capitals in the middle of the frame's band.
  * base.popup_text.layout.darkest: a pop text of a kind starts at start_offset and travels pop_y_offset in
    pop_time seconds, counted upwards from the hero's middle (status_bars.icon_world_y_offset 149 above the
    ground: the mod's reading); fonts/popup.fnt in the kind's two colours (pop_text_<kind>,
    pop_text_outline_<kind>), overlays/poptext_<kind>.png left of the word where DD1 has one.
  * torch_layout: overlays/torch.png (900x188) centred, top at 28; the gauges 400x4 at 26,89 of the picture
    and mirrored, coloured torch_centre to torch_ends (colours/base.colours.darkest).
  * quest_info at 12,20: overlays/quest_log.png centred on 65,58, the name at 110,26, the goal at 110,58,
    panels/retreat_button.png centred on 68,120; complete: overlays/quest_complete.png at 0,30, the words at
    120,45, the choice's frame hanging from 948,590 + 0,55 and its two buttons at -170,98 and 90,98.
  * sidebar_scroll: scrolls/event_scroll_sidebar.png hanging from 1348,200; header y 44; body -152,128 (330
    wide); the hand -152,240, the item slot -34,230 (80x160), the way past 75,240. No words under the hand
    and the way past: DD1 places those for a controller only and tells the pointer in tooltips.
  * basic_scroll: scrolls/event_scroll_basic.png hanging from 1342,140; header y 48; inset 0,145; body y 308
    (345 wide); Eat -150,400, Starve 78,400; text answers 384x36 at y 456.
  * overlay.loot.darkest: scrolls/event_scroll_loot.png, hung where the sidebar scroll hangs (DD1's own frame
    of the window, _lab/dd1_ref/raid/raid_loot_window_after_fight.png, has its picture at 1120,200); title
    228,40; description 228,136 (350 wide); cards from y 195, 74 apart, centred; Take All 80,358, Close 306,358,
    each picture 22 px left and 10 px up of that (the same frame: byhand.png at 58,348, pass.png at 284,347) and
    without words under it (DD1 writes them for a controller only).
"""
import argparse
import glob
import io
import os
import re

from PIL import Image, ImageDraw, ImageEnhance

from dd1_bmfont import Dd1Text
from preview_hamlet import floats, parse_darkest

W, H = 1920, 1080
DIR = "scripts/layout/"

# (file, block, key): FALLBACK = DD1's stock value. Keep in step with RaidLayout.cs.
LAYOUT = {
    "x_centre": ("screen.raid.darkest", "screen_guide", "x_centre", (960,)),
    "safe_left": ("screen.raid.darkest", "screen_guide", "safe_left", (240,)),
    "safe_right": ("screen.raid.darkest", "screen_guide", "safe_right", (1680,)),
    "panel_top": ("screen.raid.darkest", "screen_guide", "panel_top", (720,)),
    "transition": ("screen.raid.darkest", "panel_transition_bar", "pos", (0, 710)),
    "hero_start": ("screen.raid.darkest", "overlays", "hero_start_pos", (788, 680)),
    "hero_spacing": ("screen.raid.darkest", "overlays", "hero_spacing", (-168, 0)),
    "torch_y": ("screen.raid.darkest", "torch_layout", "pos_y", (28,)),
    "torch_gauge": ("screen.raid.darkest", "torch_layout", "gauge_offset", (26, 89)),
    "torch_gauge_size": ("screen.raid.darkest", "torch_layout", "gauge_size", (400, 4)),
    "torch_fade": ("screen.raid.darkest", "torch_layout", "fade_amount", (0.05,)),
    "torch_flame": ("screen.raid.darkest", "torch_layout", "flamepos", (960, 100)),
    "basic_pos": ("screen.raid.darkest", "basic_scroll", "pos", (1342, 140)),
    "basic_header_y": ("screen.raid.darkest", "basic_scroll", "header_y", (48,)),
    "basic_inset": ("screen.raid.darkest", "basic_scroll", "inset_offset", (0, 145)),
    "basic_body_y": ("screen.raid.darkest", "basic_scroll", "body_y", (308,)),
    "basic_body_width": ("screen.raid.darkest", "basic_scroll", "body_width", (345,)),
    "basic_button_y": ("screen.raid.darkest", "basic_scroll", "button_y", (456,)),
    "basic_button_size": ("screen.raid.darkest", "basic_scroll", "button_size", (384, 36)),
    "basic_ok": ("screen.raid.darkest", "basic_scroll", "ok_button_pos", (-150, 400)),
    "basic_cancel": ("screen.raid.darkest", "basic_scroll", "cancel_button_pos", (78, 400)),
    "sidebar_pos": ("screen.raid.darkest", "sidebar_scroll", "pos", (1348, 200)),
    "sidebar_header_y": ("screen.raid.darkest", "sidebar_scroll", "header_y", (44,)),
    "sidebar_body_width": ("screen.raid.darkest", "sidebar_scroll", "body_width", (330,)),
    "sidebar_body": ("screen.raid.darkest", "sidebar_scroll", "body", (-152, 128)),
    "sidebar_investigate": ("screen.raid.darkest", "sidebar_scroll", "investigate_button_pos", (-152, 240)),
    "sidebar_pass": ("screen.raid.darkest", "sidebar_scroll", "pass_button_pos", (75, 240)),
    "sidebar_slot": ("screen.raid.darkest", "sidebar_scroll", "item_slot_pos", (-34, 230)),
    "meal_header_y": ("screen.raid.darkest", "meal_scroll", "headerY", (52,)),
    "meal_button_y": ("screen.raid.darkest", "meal_scroll", "buttonY", (148,)),
    "meal_button_offset": ("screen.raid.darkest", "meal_scroll", "buttonOffset", (74,)),
    "respite_title": ("screen.raid.darkest", "camp_layout", "respite_title_offset", (60, 44)),
    "respite_points": ("screen.raid.darkest", "camp_layout", "respite_points_offset", (324, 44)),
    "respite_description": ("screen.raid.darkest", "camp_layout", "respite_description_offset", (56, 130)),
    "respite_description_width": ("screen.raid.darkest", "camp_layout", "respite_description_width", (360,)),
    "respite_rest": ("screen.raid.darkest", "camp_layout", "respite_rest_offset", (100, 200)),
    "respite_rest_text": ("screen.raid.darkest", "camp_layout", "respite_rest_text_offset", (128, 17)),
    "quest_pos": ("screen.raid.darkest", "quest_info", "pos", (12, 20)),
    "quest_glow": ("screen.raid.darkest", "quest_info", "info_glow_offset", (-14, 0)),
    "quest_button": ("screen.raid.darkest", "quest_info", "info_button_offset", (65, 58)),
    "quest_name": ("screen.raid.darkest", "quest_info", "info_text_name_offset", (110, 26)),
    "quest_goals": ("screen.raid.darkest", "quest_info", "info_text_goals_start_offset", (110, 58)),
    "quest_complete_button": ("screen.raid.darkest", "quest_info", "complete_button_offset", (0, 30)),
    "quest_complete_text": ("screen.raid.darkest", "quest_info", "complete_text_offset", (120, 45)),
    "quest_retreat": ("screen.raid.darkest", "quest_info", "retreat_button_offset", (68, 120)),
    "complete_mid": ("screen.raid.darkest", "quest_info", "complete_mid_screen_pos", (948, 590)),
    "complete_frame": ("screen.raid.darkest", "quest_info", "complete_choice_shared_frame_pos", (0, 55)),
    "complete_return": ("screen.raid.darkest", "quest_info", "complete_return_to_hamlet_pos", (-170, 98)),
    "complete_continue": ("screen.raid.darkest", "quest_info", "complete_continue_raid_pos", (90, 98)),
    "complete_tooltip_offset": ("screen.raid.darkest", "quest_info", "complete_tooltip_offset", (0, 10)),
    "tray_char_x": ("screen.raid.status_bars.darkest", "status_bars", "char_x_offset", (-50,)),
    "tray_y": ("screen.raid.status_bars.darkest", "status_bars", "y_pos", (698,)),
    "tray_health": ("screen.raid.status_bars.darkest", "status_bars", "health_bar_offset", (50, 0)),
    "tray_health_height": ("screen.raid.status_bars.darkest", "status_bars", "health_bar_height", (10,)),
    "tray_health_width": ("screen.raid.status_bars.darkest", "status_bars", "health_bar_widths", (100,)),
    "tray_stress": ("screen.raid.status_bars.darkest", "status_bars", "stress_offset", (-1, 12)),
    "tray_stress_spacing": ("screen.raid.status_bars.darkest", "status_bars", "stress_spacing", (10,)),
    "tray_icon_size": ("screen.raid.status_bars.darkest", "status_bars", "tray_icon_hot_spot_size", (20, 24)),
    "tray_icon_left": ("screen.raid.status_bars.darkest", "status_bars", "tray_icon_left_offset", (58, -38)),
    "tray_icon_left_spacing": ("screen.raid.status_bars.darkest", "status_bars", "tray_icon_left_spacing", (20,)),
    "tray_icon_right": ("screen.raid.status_bars.darkest", "status_bars", "tray_icon_right_offset", (62, -38)),
    "tray_icon_right_spacing": ("screen.raid.status_bars.darkest", "status_bars", "tray_icon_right_spacing", (20,)),
    "hero_middle": ("screen.raid.status_bars.darkest", "status_bars", "icon_world_y_offset", (149,)),
    "announce_left": ("screen.raid.darkest", "announcement", "frame_pos_left", (572, 184)),
    "announce_right": ("screen.raid.darkest", "announcement", "frame_pos_right", (1348, 184)),
    "announce_top": ("screen.raid.darkest", "announcement", "frame_pos_centre_top", (960, 210)),
    "announce_bottom": ("screen.raid.darkest", "announcement", "frame_pos_centre_bottom", (960, 668)),
    "announce_text": ("screen.raid.darkest", "announcement", "text_offset", (0, -29)),
    "pop_icon_offset": ("base.popup_text.layout.darkest", "pop_text_shared", "icon_offset", (5, 0)),
    "banner_background": ("panel.banner.darkest", "background_layout", "pos", (-33, 0)),
    "banner_portrait": ("panel.banner.darkest", "portrait_layout", "pos", (32, 32)),
    "banner_name": ("panel.banner.darkest", "name_layout", "pos", (272, 38)),
    "banner_class_y": ("panel.banner.darkest", "name_layout", "class_y", (76,)),
    "banner_name_colour": ("panel.banner.darkest", "name_layout", "hero_name_colour", (177, 161, 108, 255)),
    "banner_class_colour": ("panel.banner.darkest", "name_layout", "hero_class_colour", (154, 152, 143, 175)),
    "banner_ability": ("panel.banner.darkest", "ability_layout", "pos", (280, 35)),
    "banner_ability_spacing": ("panel.banner.darkest", "ability_layout", "spacing", (76,)),
    "hero_health": ("panel.hero.darkest", "health_layout", "pos", (130, 11)),
    "hero_stress": ("panel.hero.darkest", "stress_layout", "pos", (130, 40)),
    "hero_stress_colour": ("panel.hero.darkest", "stress_layout", "colour", (150, 150, 150, 255)),
    "hero_stat": ("panel.hero.darkest", "stat_layout", "pos", (60, 72)),
    "hero_equipment": ("panel.hero.darkest", "hero_equipment", "pos", (238, 0)),
    "hero_trinket": ("panel.hero.darkest", "hero_trinket", "pos", (453, 0)),
    "trinket_start": ("../../shared/hero/hero.layout.darkest", "hero_trinket_grid_layout", "start_pos", (32, 52)),
    "trinket_offset": ("../../shared/hero/hero.layout.darkest", "hero_trinket_grid_layout", "offset", (92, 160)),
    "weapon_pos": ("../../shared/hero/hero.layout.darkest", "hero_equipment_layout", "weapon_pos", (4, 0)),
    "armour_pos": ("../../shared/hero/hero.layout.darkest", "hero_equipment_layout", "armour_pos", (95, 0)),
    "equip_icon": ("../../shared/hero/hero.layout.darkest", "hero_equipment_layout", "icon_offset", (29, 52)),
    "equip_level": ("../../shared/hero/hero.layout.darkest", "hero_equipment_layout", "level_offset", (90, 12)),
    "stat_spacing": ("../../shared/hero/hero.layout.darkest", "hero_stats_layout", "spacing", (160, 20)),
    "stat_value": ("../../shared/hero/hero.layout.darkest", "hero_stats_layout", "value_offset", (112, 0)),
    "stat_icon": ("../../shared/hero/hero.layout.darkest", "hero_stats_layout", "icon_offset", (-26, 2)),
    "map_tile": ("panel.map.darkest", "map_layout", "tilesize", (24,)),
    "map_clip": ("panel.map.darkest", "map_layout", "clip", (16, 665, 19, 340)),
    "tab_pos": ("panel.map.darkest", "tab_placement", "pos", (672, 252)),
    "tab_size": ("panel.map.darkest", "tab_placement", "size", (48, 90)),
    "home_button": ("panel.map.darkest", "home_button_layout", "button_pos", (677, 24)),
    "party_order_button": ("panel.tab.darkest", "reorder_party_layout", "button_pos", (678, 90)),
    "bag_columns": ("pannel.inventory.darkest", "raid_inventory_panel_grid_layout", "number_of_columns", (8,)),
    "bag_start": ("pannel.inventory.darkest", "raid_inventory_panel_grid_layout", "start_pos", (20, 28)),
    "bag_offset": ("pannel.inventory.darkest", "raid_inventory_panel_grid_layout", "offset", (80, 160)),
    "item_icon_offset": ("../../shared/inventory/inventory.layout.darkest", "inventory_item_layout", "icon_offset", (4, 0)),
    "item_amount": ("../../shared/inventory/inventory.layout.darkest", "inventory_item_layout", "amount_text_offset", (14, 4)),
    "item_cost": ("../../shared/inventory/inventory.layout.darkest", "inventory_item_layout", "cost_offset", (37, 157)),
    "item_tooltip_offset": ("../../shared/inventory/inventory.layout.darkest", "inventory_item_tooltip_layout", "offset", (85, 0)),
    "item_tooltip_width": ("../../shared/inventory/inventory.layout.darkest", "inventory_item_tooltip_layout", "text_width", (200,)),
    "loot_title": ("overlay.loot.darkest", "loot_title", "pos", (228, 40)),
    "loot_description": ("overlay.loot.darkest", "loot_description", "pos", (228, 136)),
    "loot_description_width": ("overlay.loot.darkest", "loot_description", "width", (350,)),
    "loot_tiles_y": ("overlay.loot.darkest", "loot_tiles", "startPosY", (195,)),
    "loot_tile_offset": ("overlay.loot.darkest", "loot_tiles", "offset", (74,)),
    "loot_take_all": ("overlay.loot.darkest", "loot_buttons", "take_all_pos", (80, 358)),
    "loot_close": ("overlay.loot.darkest", "loot_buttons", "close_pos", (306, 358)),
    "loot_backdrop_y": ("overlay.loot.darkest", "loot_background", "dynamic_backdrop_y_offset", (10,)),
    "loot_min_items": ("overlay.loot.darkest", "loot_background", "min_items", (4,)),
    "tooltip_border": ("../../shared/tooltip/tooltip.layout.darkest", "tooltip_background_layout", "border_texture_threshold", (0.15,)),
    "tooltip_text_offset": ("../../shared/tooltip/tooltip.layout.darkest", "tooltip_background_layout", "text_offset", (4, 0)),
    "tooltip_offset": ("../../shared/tooltip/tooltip.layout.darkest", "tooltip_background_layout", "offset", (12, -12)),
    "confirm_base": ("../../shared/confirm_dialog/confirm_dialog.layout.darkest", "confirm_dialog_layout", "base_pos", (960, 200)),
    "confirm_answers_offset": ("../../shared/confirm_dialog/confirm_dialog.layout.darkest", "confirm_dialog_layout", "answers_offset", (-12, -20)),
    "confirm_question": ("../../shared/confirm_dialog/confirm_dialog.layout.darkest", "confirm_dialog_question_layout", "text_offset", (-18, 200)),
    "confirm_question_width": ("../../shared/confirm_dialog/confirm_dialog.layout.darkest", "confirm_dialog_question_layout", "text_width", (520,)),
    "confirm_answers_start": ("../../shared/confirm_dialog/confirm_dialog.layout.darkest", "confirm_dialog_answers_layout", "start_offset", (0, 390)),
    "confirm_answer_spacing": ("../../shared/confirm_dialog/confirm_dialog.layout.darkest", "confirm_dialog_answers_layout", "spacing", (0, 55)),
    "confirm_button_size": ("../../shared/confirm_dialog/confirm_dialog.layout.darkest", "confirm_dialog_answer_layout", "button_size", (500, 38)),
}

# ---- the mod's own numbers: keep in step with the C# constants named beside them ---------------------------
BANNER_HEIGHT = 136                  # RaidHeroPanel.BannerHeight (panel_banner.png)
PORTRAIT = 85                        # RaidHeroPanel.PortraitSize
NAME_LEFT = 122                      # RaidHeroPanel.NameLeft
PATH_GAP = 21                        # RaidHeroPanel.PathGap
ABILITY = 72
BRACKET_LINE, BRACKET_GAP = 172, 3   # RaidTrays.BracketLine / BracketGap
BRACKET_ART = (175, 206)
LOG_POS = (24, 196)                  # DungeonHud.LogPos
NOTICE_RISE = (16, 8)                # DungeonHud.NoticeRise
HEADER_WIDTH = 370                   # RaidScrolls.HeaderWidth
CAPTION_RISE = 9                     # RaidScrolls.CaptionRise
CAPTION_WIDTH = 116                  # RaidScrolls.CaptionWidth
BUTTON_ART = (124, 69)
LOOT_BUTTON_NUDGE = (-22, -10)       # RaidScrolls.LootButtonNudge
PLAIN_BODY_Y = 162                   # RaidScrolls.PlainBody
ROW_GAP = 4                          # RaidScrolls.RowGap
QUEST_NAME_WIDTH = 384               # RaidQuestInfo.NameWidth
CHOICE_SHADE = 0.45                  # RaidQuestInfo.ChoiceShade
DOOR_TILES = 1                       # Minimap.DoorTiles
MAP_SCALE = 0.5                      # Minimap.FrameScale: DD1's frames show the map's art at half its size
FLAME_SIZE = 76                      # RaidTorch.FlameSize
CAMP_SCROLL_POS = (1420, 40)         # CampScreen.ScrollPos
CAMP_NOTE_GAP, CAMP_NOTE_PAD = 44, 14

STRINGS = ["localization/miscellaneous.string_table.xml", "localization/curios.string_table.xml", "localization/heroes.string_table.xml"]


class Dd1:
    """The install: art, layout numbers, strings, text."""

    def __init__(self, root):
        self.root = root
        self.text = Dd1Text(root)
        self._art = {}
        self._files = {}
        self._strings = None
        self.used_fallback = []

    def path(self, rel):
        return os.path.join(self.root, rel)

    def art(self, rel):
        if rel not in self._art:
            p = self.path(rel)
            self._art[rel] = Image.open(p).convert("RGBA") if os.path.isfile(p) else None
        return self._art[rel]

    def _blocks(self, rel):
        if rel not in self._files:
            p = os.path.normpath(self.path(DIR + rel))
            self._files[rel] = parse_darkest(io.open(p, encoding="utf-8-sig", errors="replace").read()) if os.path.isfile(p) else None
        return self._files[rel]

    def get(self, name):
        """A layout entry as a tuple of numbers (DD1's value, or the marked fallback)."""
        rel, block, key, fallback = LAYOUT[name]
        blocks = self._blocks(rel)
        tokens = (blocks or {}).get(block, {}).get(key)
        if not tokens:
            self.used_fallback.append(name)
            return fallback
        if tokens[0].startswith("#"):
            h = tokens[0][1:]
            return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)
        out = []
        for i, default in enumerate(fallback):
            m = re.match(r"-?\d+(\.\d+)?", tokens[i]) if i < len(tokens) else None      # "128d" is 128
            value = float(m.group(0)) if m else default
            out.append(int(value) if float(value).is_integer() else value)
        return tuple(out)

    def n(self, name):
        return self.get(name)[0]

    def string(self, key, fallback=None):
        if self._strings is None:
            self._strings = {}
            for rel in STRINGS:
                p = self.path(rel)
                if not os.path.isfile(p):
                    continue
                english = False
                for line in io.open(p, encoding="utf-8", errors="replace"):
                    if "<language " in line:
                        if english:
                            break
                        english = 'id="english"' in line
                        continue
                    if not english:
                        continue
                    m = re.search(r'<entry id="([^"]+)"><!\[CDATA\[(.*?)\]\]></entry>', line)
                    if m and m.group(1) not in self._strings:
                        self._strings[m.group(1)] = m.group(2)
        return self._strings.get(key) or fallback


# ---------------------------------------------------------------- drawing

def put(canvas, img, pos, alpha=1.0, tint=None):
    if img is None:
        return
    if tint is not None:
        r, g, b, a = img.split()
        # a tint above 1 brightens and stops at white (DD1's button_highlight under the pointer)
        img = Image.merge("RGBA", (r.point(lambda v: min(255, int(v * tint[0]))), g.point(lambda v: min(255, int(v * tint[1]))), b.point(lambda v: min(255, int(v * tint[2]))), a))
    if alpha < 1.0:
        img = img.copy()
        img.putalpha(img.getchannel("A").point(lambda v: int(v * alpha)))
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    layer.paste(img, (int(round(pos[0])), int(round(pos[1]))))
    canvas.alpha_composite(layer)


def fill(canvas, box, colour):
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rectangle([int(box[0]), int(box[1]), int(box[2]) - 1, int(box[3]) - 1], fill=tuple(int(c) for c in colour))
    canvas.alpha_composite(layer)


def fitted(dd1, text, style, width, size=None):
    """TextMeshPro auto-size for one line: None (native) unless the text is wider than its box."""
    full = dd1.text.width(text, style, size)
    font = dd1.text.font(style)
    return size if full <= width else (size or font.native_size) * width / full


def narrow_move(dd1):
    """RaidMoveArt.Tile: "move" at 20x72. The frame and the ground of DD1's move square (its left 10 and its right
    10 pixels) with DD1's small sign of a move (overlays/tray_move.png) on it twice, as it is and turned round."""
    move = dd1.art("panels/icons_ability/ability_move.png")
    if move is None:
        return None
    tile = Image.new("RGBA", (20, move.height))
    tile.paste(move.crop((0, 0, 10, move.height)), (0, 0))
    tile.paste(move.crop((move.width - 10, 0, move.width, move.height)), (10, 0))
    sign = dd1.art("overlays/tray_move.png")
    if sign is not None:
        small = sign.crop((2, 2, 22, 22)).resize((14, 14), Image.LANCZOS)
        tile.alpha_composite(small, (3, 20))
        tile.alpha_composite(small.transpose(Image.FLIP_LEFT_RIGHT), (3, 38))
    return tile


def drained(img, saturation, light):
    """A picture with part of its colour and light, as DD1 draws what cannot be used (RaidUi.Desaturated and a tint)."""
    if img is None:
        return None
    r, g, b, a = img.convert("RGBA").split()
    grey = Image.merge("RGB", (r, g, b)).convert("L")
    mixed = [Image.blend(grey, band, saturation).point(lambda v: int(v * light)) for band in (r, g, b)]
    return Image.merge("RGBA", (mixed[0], mixed[1], mixed[2], a))


def line(canvas, dd1, pos, text, style, colour=None, anchor="l", width=None, shadow=False, alpha=1.0):
    """One line; width: the box it must stay inside (it shrinks, as RaidUi.Fit does)."""
    if not text:
        return
    size = fitted(dd1, text, style, width) if width else None
    if shadow:
        dd1.text.draw(canvas, (pos[0] + 1.5, pos[1] + 1.5), text, style, (0, 0, 0), anchor, size)
    dd1.text.draw(canvas, pos, text, style, colour, anchor, size, alpha=alpha)


def paragraph(canvas, dd1, pos, text, style, width, colour=None, anchor="l", height=None):
    """Wrapped lines from pos down; returns the height used. Shrinks to fit the height, as RaidUi.Paragraph."""
    font = dd1.text.font(style)
    size = None
    for _ in range(12):
        lines = []
        for part in text.split("\n"):
            lines += dd1.text.wrap(part, style, width, size) or [""]
        step = dd1.text.line_height(style, size)
        if height is None or len(lines) * step <= height or (size or font.native_size) <= 13:
            break
        size = (size or font.native_size) - 1
    for i, words in enumerate(lines):
        dd1.text.draw(canvas, (pos[0], pos[1] + i * step), words, style, colour, anchor, size)
    return len(lines) * step


def tooltip_box(canvas, dd1, box):
    """shared/tooltip/tooltip_background.png cut in nine around a box."""
    art = dd1.art("shared/tooltip/tooltip_background.png")
    x0, y0, x1, y1 = [int(round(v)) for v in box]
    if art is None:
        fill(canvas, box, (0, 0, 0, 222))
        return
    b = int(round(art.width * dd1.n("tooltip_border")))
    w, h = x1 - x0, y1 - y0
    xs = [(0, b, 0, b), (b, art.width - b, b, w - b), (art.width - b, art.width, w - b, w)]
    ys = [(0, b, 0, b), (b, art.height - b, b, h - b), (art.height - b, art.height, h - b, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if dx1 <= dx0 or dy1 <= dy0:
                continue
            put(canvas, art.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.BILINEAR), (x0 + dx0, y0 + dy0))


def tooltip(canvas, dd1, at, title, body, width, above=False, centred=False, title_colour="notable", body_colour="neutral"):
    """RaidTooltip.Show: a titled text in DD1's tooltip box; returns the box."""
    style = "tooltip"
    step = dd1.text.line_height(style)
    lines = [(title, title_colour)] if title else []
    for part in (body or "").split("\n"):
        if part:
            lines += [(w, body_colour) for w in dd1.text.wrap(part, style, width)]
    text_w = min(width, max(dd1.text.width(w, style) for w, _ in lines) + 1)
    text_h = len(lines) * step
    pad = round(128 * dd1.n("tooltip_border")) * 0.5 + dd1.get("tooltip_text_offset")[0]
    bw, bh = text_w + 2 * pad, text_h + 2 * pad
    x = at[0] - bw / 2 if centred else at[0]
    y = at[1] - bh if above else at[1]
    x = max(8, min(W - 8 - bw, x))
    y = max(8, min(H - 8 - bh, y))
    tooltip_box(canvas, dd1, (x, y, x + bw, y + bh))
    for i, (words, colour) in enumerate(lines):
        dd1.text.draw(canvas, (x + pad, y + pad + i * step), words, style, colour)
    return x, y, x + bw, y + bh


# ---------------------------------------------------------------- the scene (stand-in for the game's corridor)

def scene(dd1, dungeon):
    """The top 720 px as CorridorView frames a hallway: far layer, mid layer, wall tiles; grey figures."""
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    prefix = "dungeons/%s/%s." % (dungeon, dungeon)
    bg, mid = dd1.art(prefix + "corridor_bg.png"), dd1.art(prefix + "corridor_mid.png")
    walls = sorted(glob.glob(dd1.path(prefix + "corridor_wall.*.png")))
    for i in range(-1, 4):
        x = -120 + i * 720
        put(canvas, bg, (x, 0))
        put(canvas, mid, (x, 0))
        if walls:
            put(canvas, Image.open(walls[(i + 2) % len(walls)]).convert("RGBA"), (x, 0))
    fill(canvas, (0, 720, W, H), (0, 0, 0, 255))
    return canvas


def figures(canvas, dd1, xs):
    """Grey stand-ins for the DD2 hero models, feet on DD1's ground line."""
    ground = dd1.n("tray_y") - 8
    for x in xs:
        layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        d.ellipse([x - 26, ground - 300, x + 26, ground - 248], fill=(42, 44, 48, 235))
        d.polygon([(x - 46, ground - 246), (x + 46, ground - 246), (x + 60, ground), (x - 60, ground)], fill=(42, 44, 48, 235))
        canvas.alpha_composite(layer)


# ---------------------------------------------------------------- sample state (what the game supplies)

HEROES = [
    {"name": "Reynauld", "cls": "crusader", "title": "Crusader", "path": "Wanderer", "hp": (31, 33), "stress": 3, "weapon": 2, "armour": 1,
     "stats": ["+25%", "+7%", "+0%", "1"], "tints": {0: 1, 3: -1}, "marks": {1: 1}, "trinkets": ["inv_trinket+berserk_charm.png", "inv_trinket+sun_ring.png"]},
    {"name": "Dismas", "cls": "highwayman", "title": "Highwayman", "path": "Sharpshot", "hp": (14, 23), "stress": 6, "weapon": 1, "armour": 1,
     "stats": ["+0%", "+0%", "+0%", "5"], "trinkets": []},
    {"name": "Paracelsus", "cls": "plague_doctor", "title": "Plague Doctor", "path": "Wanderer", "hp": (22, 22), "stress": 1, "weapon": 1, "armour": 2,
     "stats": ["+0%", "+0%", "+10%", "7"], "trinkets": []},
    {"name": "Junia", "cls": "vestal", "title": "Vestal", "path": "Confessor", "hp": (19, 24), "stress": 8, "weapon": 1, "armour": 1,
     "stats": ["+0%", "+0%", "+0%", "4"], "trinkets": []},
]
HERO_X = [788, 569, 350, 131]        # where CorridorView's party stands on screen in a hallway (lead first)
# RaidHeroPanel.Stats: four rows, what the hero brings to a fight: by how much their blows and their maximum health
# are multiplied, what they add to a skill's chance of a critical hit, their speed ("HP" is the mod's own word)
STAT_NAMES = [("str_ui_DMG", "DMG"), ("str_ui_CRIT", "CRIT"), (None, "HP"), ("str_ui_SPD", "SPD")]
ABILITY_FILES = ["one", "two", "three", "four", "five"]
# a trinket found on the way lies in the bag like anything else: ("trinket", "<DD1 rarity>:<DD1 picture standing in>", 1)
BAG = [("supply", "torch", 6), ("provision", "", 10), ("supply", "shovel", 1), ("supply", "skeleton_key", 2), ("supply", "bandage", 2),
       ("supply", "medicinal_herbs", 1), ("gold", "", 1250), ("gem", "ruby", 1), ("heirloom", "crest", 3), ("supply", "firewood", 1),
       ("trinket", "rare:sun_ring", 1)]
ITEM_NAMES = {("supply", "torch"): "Torch", ("provision", ""): "Food", ("supply", "shovel"): "Shovel", ("supply", "skeleton_key"): "Skeleton Key",
              ("supply", "bandage"): "Bandage", ("supply", "medicinal_herbs"): "Medicinal Herbs", ("gold", ""): "Gold", ("gem", "ruby"): "Ruby",
              ("heirloom", "crest"): "Crest", ("supply", "firewood"): "Firewood", ("supply", "holy_water"): "Holy Water",
              ("supply", "antivenom"): "Antivenom", ("supply", "laudanum"): "Laudanum", ("gem", "emerald"): "Emerald", ("heirloom", "deed"): "Deed",
              ("heirloom", "bust"): "Bust", ("gem", "jade"): "Jade"}
STACK_LIMIT = {"provision": 12, "gold": 1750}


def item_icon(dd1, kind, ident, amount):
    """ItemDef.IconPath: food and gold have a picture per fullness of the stack. A trinket's card
    (InventoryContent.Icon, InventoryGrid): DD1's card of its rarity with DD2's square picture of the trinket in
    its middle. DD2's art cannot be had offline: a DD1 trinket's picture, cut square, stands in for it."""
    if kind == "trinket":
        rarity, _, stand_in = ident.partition(":")
        back = dd1.art("panels/icons_equip/trinket/rarity_%s.png" % rarity)
        art = dd1.art("panels/icons_equip/trinket/inv_trinket+%s.png" % stand_in)
        if back is None or art is None:
            return back or art
        out = back.convert("RGBA").copy()
        out.alpha_composite(art.convert("RGBA").crop((0, 36, 72, 108)), (0, 36))
        return out
    name = "panels/icons_equip/%s/inv_%s+%s" % (kind, kind, ident)
    variants = len(glob.glob(dd1.path(name + "_[0-9].png")))
    if variants:
        index = min(variants - 1, max(0, amount) * variants // (STACK_LIMIT.get(kind, 12) + 1))
        return dd1.art("%s_%d.png" % (name, index))
    return dd1.art(name + ".png")


def amount_text(kind, amount):
    if kind == "trinket":
        return ""                    # InventoryText.Amount: no number on a trinket's card, as in DD1
    return str(amount)


def card(canvas, dd1, cell, kind, ident, amount, dim=False, hover=False):
    """InventoryGrid: the card icon_offset into its cell, the amount at amount_text_offset of the cell."""
    off = dd1.get("item_icon_offset")
    icon = item_icon(dd1, kind, ident, amount)
    put(canvas, icon, (cell[0] + off[0], cell[1] + off[1]), tint=(0.4, 0.4, 0.4) if dim else None)
    if hover:
        frame = dd1.art("overlays/eqp_mouseover.png")
        put(canvas, frame, (cell[0] + off[0] + 36 - frame.width / 2, cell[1] + off[1] + 72 - frame.height / 2))
    at = dd1.get("item_amount")
    line(canvas, dd1, (cell[0] + at[0], cell[1] + at[1]), amount_text(kind, amount), "inventory_amount",
         (0x5d, 0x5a, 0x50) if dim else None, shadow=True)


# ---------------------------------------------------------------- the HUD

def draw_panels(canvas, dd1, hero, inventory=False, light=75, camp_skills=None, selected=0, complete=False, dim_bag=None, hover_slot=None, log=None, targets=()):
    top = dd1.n("panel_top")
    left = (dd1.n("safe_left"), top)
    right = (dd1.n("x_centre"), top)
    decor = dd1.art("panels/side_decor.png")
    put(canvas, decor, (0, top))
    put(canvas, decor.transpose(Image.FLIP_LEFT_RIGHT), (W - decor.width, top))

    # ---- right panel ----
    put(canvas, dd1.art("panels/panel_inventory.png" if inventory else "panels/panel_map.png"), right)
    if inventory:
        draw_bag(canvas, dd1, right, dim_bag, hover_slot)
    else:
        draw_map(canvas, dd1, right)
    home = dd1.get("home_button")
    put(canvas, dd1.art("panels/focuscam_button.png"), (right[0] + home[0], right[1] + home[1]))
    # DD1's party-order button under it, with a fifth of its colour while the party stands in its own order
    order = dd1.get("party_order_button")
    put(canvas, drained(dd1.art("panels/party_order_button.png"), 0.2, 1.0), (right[0] + order[0], right[1] + order[1]))

    # ---- left panel ----
    put(canvas, dd1.art("panels/panel_hero.png"), (left[0], left[1] + BANNER_HEIGHT))
    bb = dd1.get("banner_background")
    put(canvas, dd1.art("panels/panel_banner.png"), (left[0] + bb[0], left[1] + bb[1]))
    bp = dd1.get("banner_portrait")
    portrait = dd1.art("heroes/%s/%s_A/%s_portrait_roster.png" % (hero["cls"], hero["cls"], hero["cls"]))
    put(canvas, portrait, (left[0] + bp[0], left[1] + bp[1]))
    bn = dd1.get("banner_name")
    name_w = bn[0] - NAME_LEFT
    line(canvas, dd1, (left[0] + bn[0], left[1] + bn[1]), hero["name"], "banner_hero_name", dd1.get("banner_name_colour")[:3], "r", name_w)
    cc = dd1.get("banner_class_colour")
    cy = dd1.n("banner_class_y")
    line(canvas, dd1, (left[0] + bn[0], left[1] + cy), hero["title"], "banner_hero_class", cc[:3], "r", name_w, alpha=cc[3] / 255)
    line(canvas, dd1, (left[0] + bn[0], left[1] + cy + PATH_GAP), hero["path"], "banner_hero_class", cc[:3], "r", name_w, alpha=cc[3] / 255)

    ba = dd1.get("banner_ability")
    step = dd1.n("banner_ability_spacing")
    if camp_skills is None:
        # outside a fight: the hero's five combat skills in their own colours, and "move" in the narrow place
        # beside them where DD1 has "pass" (RaidMoveArt)
        for i, word in enumerate(ABILITY_FILES[:5]):
            icon = dd1.art("heroes/%s/%s.ability.%s.png" % (hero["cls"], hero["cls"], word))
            put(canvas, icon, (left[0] + ba[0] + i * step, left[1] + ba[1]))
        put(canvas, narrow_move(dd1), (left[0] + ba[0] + 5 * step, left[1] + ba[1]))
    else:
        for i, (skill, cost, usable, chosen) in enumerate(camp_skills):
            at = (left[0] + ba[0] + i * step, left[1] + ba[1])
            put(canvas, dd1.art("raid/camping/skill_icons/camp_skill_%s.png" % skill), at, tint=None if usable else (0.32, 0.32, 0.32))
            if chosen:
                frame = dd1.art("panels/icons_ability/selected_ability.png")
                put(canvas, frame, (at[0] - (frame.width - ABILITY) / 2, at[1] - (frame.height - ABILITY) / 2))
            # the cost is in the skill's tooltip ("Time Cost: n"), as in DD1: no number on the icon

    panel = (left[0], left[1] + BANNER_HEIGHT)
    hh, hs = dd1.get("hero_health"), dd1.get("hero_stress")
    line(canvas, dd1, (panel[0] + hh[0], panel[1] + hh[1]), "%d / %d" % hero["hp"], "stat", (0xc0, 0, 0))
    line(canvas, dd1, (panel[0] + hs[0], panel[1] + hs[1]), "%d / 10" % hero["stress"], "stat", dd1.get("hero_stress_colour")[:3])
    st, sp, sv = dd1.get("hero_stat"), dd1.get("stat_spacing"), dd1.get("stat_value")
    si = dd1.get("stat_icon")
    for i, (key, fallback) in enumerate(STAT_NAMES):
        y = panel[1] + st[1] + i * sp[1]
        # a row a quirk, a disease or a trinket has moved is gold (worse: red), name and number
        tint = hero.get("tints", {}).get(i, 0)
        colour = "notable" if tint > 0 else "harmful" if tint < 0 else "neutral"
        line(canvas, dd1, (panel[0] + st[0], y), dd1.string(key, fallback) if key else fallback, "stat", colour)
        line(canvas, dd1, (panel[0] + st[0] + sv[0], y), hero["stats"][i], "stat", colour)
        mark = hero.get("marks", {}).get(i, 0)
        if mark:
            put(canvas, dd1.art("shared/hero/icon_stat_buff.png" if mark > 0 else "shared/hero/icon_stat_debuff.png"), (panel[0] + st[0] + si[0], y + si[1]))
    eq, ei, el = dd1.get("hero_equipment"), dd1.get("equip_icon"), dd1.get("equip_level")
    for kind, pos, level in (("weapon", dd1.get("weapon_pos"), hero["weapon"]), ("armour", dd1.get("armour_pos"), hero["armour"])):
        put(canvas, dd1.art("heroes/%s/icons_equip/eqp_%s_%d.png" % (hero["cls"], kind, level - 1)), (panel[0] + eq[0] + pos[0] + ei[0], panel[1] + eq[1] + pos[1] + ei[1]))
        line(canvas, dd1, (panel[0] + eq[0] + pos[0] + el[0], panel[1] + eq[1] + pos[1] + el[1]), str(level), "equipment_level", "equipment_level_%d" % (level - 1))
    tr, ts, to, io_ = dd1.get("hero_trinket"), dd1.get("trinket_start"), dd1.get("trinket_offset"), dd1.get("item_icon_offset")
    for i, name in enumerate(hero["trinkets"][:2]):
        art = dd1.art("panels/icons_equip/trinket/" + name)
        if art is None:
            continue
        # DD2's trinket icons are square: 72x72 in the middle of DD1's tall slot
        square = art.crop((0, 36, 72, 108))
        put(canvas, square, (panel[0] + tr[0] + ts[0] + i * to[0] + io_[0], panel[1] + tr[1] + ts[1] + io_[1] + 36))

    # the seam, then what stands on it
    tp = dd1.get("transition")
    put(canvas, dd1.art("panels/panel_transition.png"), tp)
    draw_trays(canvas, dd1, selected, targets)
    draw_torch(canvas, dd1, light)
    draw_quest(canvas, dd1, complete)
    if log:
        for i, words in enumerate(log):
            line(canvas, dd1, (LOG_POS[0], LOG_POS[1] + i * dd1.text.line_height("raid_quest_info_goals")), words, "raid_quest_info_goals", "neutral", alpha=0.9)


def draw_trays(canvas, dd1, selected, targets=()):
    y = dd1.n("tray_y")
    cx, ho, hw, hh = dd1.n("tray_char_x"), dd1.get("tray_health"), dd1.n("tray_health_width"), dd1.n("tray_health_height")
    so, ss = dd1.get("tray_stress"), dd1.n("tray_stress_spacing")
    top, bottom = dd1.text.colour("tray_health_bar_default_current_top"), dd1.text.colour("tray_health_bar_default_current_bottom")
    btop, bbottom = dd1.text.colour("tray_health_bar_background_top"), dd1.text.colour("tray_health_bar_background_bottom")
    for i, hero in enumerate(HEROES):
        x = HERO_X[i]
        for name, on in (("overlays/target_h_1.png", i in targets), ("overlays/selected_1.png", i == selected)):
            if on:
                put(canvas, dd1.art(name), (x - BRACKET_ART[0] / 2, y - BRACKET_GAP - BRACKET_LINE))
        bar_x = x + cx + ho[0] - hw / 2
        share = hero["hp"][0] / hero["hp"][1]
        for row in range(int(hh)):
            t = row / max(1, hh - 1)
            fill(canvas, (bar_x, y + ho[1] + row, bar_x + hw, y + ho[1] + row + 1), tuple(btop[c] + (bbottom[c] - btop[c]) * t for c in range(3)) + (255,))
            fill(canvas, (bar_x, y + ho[1] + row, bar_x + hw * share, y + ho[1] + row + 1), tuple(top[c] + (bottom[c] - top[c]) * t for c in range(3)) + (255,))
        for p in range(10):
            full = p < hero["stress"]
            art = dd1.art("overlays/stress_pip_full.png" if full else "overlays/stress_pip_empty.png")
            put(canvas, art, (x + cx + so[0] + p * ss, y + so[1] + (1 if full else 0)))


def draw_torch(canvas, dd1, light):
    art = dd1.art("overlays/torch.png")
    at = (dd1.n("x_centre") - art.width / 2, dd1.n("torch_y"))
    put(canvas, art, at)
    go, gs, fade = dd1.get("torch_gauge"), dd1.get("torch_gauge_size"), dd1.n("torch_fade")
    centre, ends = dd1.text.colour("torch_centre"), dd1.text.colour("torch_ends")
    share = max(0.0, min(1.0, light / 100.0))
    length = int(round(gs[0] * share))
    soft = max(1.0, fade * gs[0])
    for px in range(length):
        t = px / gs[0]
        colour = tuple(int(centre[c] + (ends[c] - centre[c]) * t) for c in range(3)) + (int(255 * max(0.0, min(1.0, (length - px) / soft))),)
        fill(canvas, (at[0] + art.width - go[0] - gs[0] + px, at[1] + go[1], at[0] + art.width - go[0] - gs[0] + px + 1, at[1] + go[1] + gs[1]), colour)
        fill(canvas, (at[0] + go[0] + gs[0] - px - 1, at[1] + go[1], at[0] + go[0] + gs[0] - px, at[1] + go[1] + gs[1]), colour)
    flame = dd1.art("overlays/torch_flame.png")
    if flame is not None and share > 0:
        # white on black, made for additive blending: brightness is coverage (RaidTorch.FlameSprite)
        cover = flame.convert("L")
        scale = 0.35 + 0.65 * share
        size = (int(FLAME_SIZE * scale), int(FLAME_SIZE * scale * 1.15))
        tint = Image.new("RGBA", size, tuple(int(ends[c] + (centre[c] - ends[c]) * (0.5 + 0.5 * share)) for c in range(3)) + (255,))
        tint.putalpha(cover.resize(size, Image.BILINEAR).point(lambda v: int(v * (0.55 + 0.4 * share))))
        fp = dd1.get("torch_flame")
        put(canvas, tint, (fp[0] - size[0] / 2, fp[1] - size[1] * 0.75))


def draw_quest(canvas, dd1, complete):
    pos = dd1.get("quest_pos")
    glow = dd1.get("quest_glow")
    if complete:
        cb, ct = dd1.get("quest_complete_button"), dd1.get("quest_complete_text")
        put(canvas, dd1.art("overlays/quest_log_glow.png"), (pos[0] + glow[0], pos[1] + glow[1] + cb[1]))
        put(canvas, dd1.art("overlays/quest_complete.png"), (pos[0] + cb[0], pos[1] + cb[1]))
        line(canvas, dd1, (pos[0] + ct[0], pos[1] + ct[1]), dd1.string("raid_quest_complete", "Quest Complete!"), "raid_quest_info_complete")
        return
    put(canvas, dd1.art("overlays/quest_log_glow.png"), (pos[0] + glow[0], pos[1] + glow[1]))
    qb = dd1.get("quest_button")
    log = dd1.art("overlays/quest_log.png")
    put(canvas, log, (pos[0] + qb[0] - log.width / 2, pos[1] + qb[1] - log.height / 2))
    qn, qg = dd1.get("quest_name"), dd1.get("quest_goals")
    line(canvas, dd1, (pos[0] + qn[0], pos[1] + qn[1]), "The Keeper of the Catalogue", "raid_quest_info_name", None, "l", QUEST_NAME_WIDTH)
    # DungeonRun.GoalLine: DD1's sentence for the goal, no count
    goal = dd1.string("town_quest_goal_start_plural_explore_room", "Explore %.0f%% of rooms.").replace("%.0f", "90").replace("%%", "%")
    line(canvas, dd1, (pos[0] + qg[0], pos[1] + qg[1]), goal, "raid_quest_info_goals")
    qr = dd1.get("quest_retreat")
    flag = dd1.art("panels/retreat_button.png")
    put(canvas, flag, (pos[0] + qr[0] - flag.width / 2, pos[1] + qr[1] - flag.height / 2))


# rooms on the generator's grid: (x, y, icon, known, visited); hallways: (a, b, [tile states], {tile: marker})
MAP_ROOMS = [(0, 1, "entrance", True, True), (1, 1, "battle", True, True), (2, 1, "curio", True, False), (3, 1, "unknown", False, False),
             (1, 2, "treasure", True, False), (1, 0, "empty", True, True), (2, 0, "unknown", False, False), (2, 2, "boss", True, False)]
MAP_HALLS = [(0, 1, "cccc", {}), (1, 2, "ccdd", {3: "curio"}), (2, 3, "kkkk", {}), (1, 4, "dddd", {1: "trap", 2: "battle"}), (1, 5, "cccc", {}),
             (5, 6, "kkkk", {}), (4, 7, "dkkk", {0: "obstacle"}), (2, 7, "kkkk", {})]
MAP_PARTY = ("hall", 1, 1)           # the party stands on tile 1 of hallway 1


def draw_map(canvas, dd1, right):
    clip = dd1.get("map_clip")       # left, right, top, bottom
    window = (right[0] + clip[0], right[1] + clip[2], right[0] + clip[1], right[1] + clip[3])
    tile = dd1.n("map_tile") * MAP_SCALE
    segments = 4
    step = (segments + 2 * DOOR_TILES + 1) * tile
    icons = "panels/icons_map/"
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))

    def room_at(i):
        return MAP_ROOMS[i][0] * step, -MAP_ROOMS[i][1] * step        # the map's y runs up

    def tile_at(a, b, i, count):
        t = (i + 1 + DOOR_TILES) / (count + 2 * DOOR_TILES + 1)
        ax, ay = room_at(a)
        bx, by = room_at(b)
        return ax + (bx - ax) * t, ay + (by - ay) * t

    kind, hall, index = MAP_PARTY
    focus = tile_at(MAP_HALLS[hall][0], MAP_HALLS[hall][1], index, segments)
    # DD1 holds the party left of and above the window's middle by the window's own corner (Minimap.Apply)
    cx, cy = (window[0] + window[2]) / 2 - clip[0], (window[1] + window[3]) / 2 - clip[2]

    def half(art):
        return art.resize((max(1, int(art.width * MAP_SCALE)), max(1, int(art.height * MAP_SCALE))), Image.LANCZOS) if art is not None else None

    def screen(p):
        return cx + p[0] - focus[0], cy + p[1] - focus[1]

    for a, b, states, markers in MAP_HALLS:
        for i, state in enumerate(states):
            p = screen(tile_at(a, b, i, len(states)))
            art = half(dd1.art(icons + {"c": "hall_clear.png", "d": "hall_dim.png", "k": "hall_dark.png"}[state]))
            put(layer, art, (p[0] - art.width / 2, p[1] - art.height / 2))
            if i in markers:
                m = half(dd1.art(icons + "marker_%s.png" % markers[i]))
                put(layer, m, (p[0] - m.width / 2, p[1] - m.height / 2))
    for i, (x, y, icon, known, visited) in enumerate(MAP_ROOMS):
        p = screen(room_at(i))
        art = half(dd1.art(icons + "room_%s.png" % (icon if known else "unknown")))
        put(layer, art, (p[0] - art.width / 2, p[1] - art.height / 2))
        if visited and icon != "entrance":
            v = half(dd1.art(icons + "marker_room_visited.png"))
            put(layer, v, (p[0] - v.width / 2, p[1] - v.height / 2))
    ind = half(dd1.art(icons + "indicator.png"))
    p = screen(focus)
    put(layer, ind, (p[0] - ind.width / 2, p[1] - ind.height / 2 - 1))
    mask = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(mask).rectangle([int(window[0]), int(window[1]), int(window[2]) - 1, int(window[3]) - 1], fill=255)
    layer.putalpha(Image.composite(layer.getchannel("A"), Image.new("L", canvas.size, 0), mask))
    canvas.alpha_composite(layer)


def bag_cell(dd1, right, slot):
    start, pitch, columns = dd1.get("bag_start"), dd1.get("bag_offset"), int(dd1.n("bag_columns"))
    return right[0] + start[0] + (slot % columns) * pitch[0], right[1] + start[1] + (slot // columns) * pitch[1]


def draw_bag(canvas, dd1, right, usable=None, hover=None):
    for slot, (kind, ident, amount) in enumerate(BAG):
        dim = usable is not None and (kind, ident) not in usable
        card(canvas, dd1, bag_cell(dd1, right, slot), kind, ident, amount, dim, hover == slot)


# ---------------------------------------------------------------- the scrolls

def scroll(canvas, dd1, art, top_centre):
    img = dd1.art("scrolls/" + art)
    origin = (top_centre[0] - img.width / 2, top_centre[1])
    put(canvas, img, origin)
    return origin, img.size


def header(canvas, dd1, origin, x, y, text):
    line(canvas, dd1, (origin[0] + x - HEADER_WIDTH / 2 + HEADER_WIDTH / 2, origin[1] + y), text, "scroll_header", "notable", "m", HEADER_WIDTH)


def choice(canvas, dd1, origin, pos, art, caption, style, colour="notable", dim=False):
    """A picture button as it rests (its art as it is); caption None: no words under it."""
    put(canvas, dd1.art("scrolls/" + art), (origin[0] + pos[0], origin[1] + pos[1]), tint=(0.3, 0.3, 0.3) if dim else None)
    line(canvas, dd1, (origin[0] + pos[0] + BUTTON_ART[0] / 2, origin[1] + pos[1] + BUTTON_ART[1] - CAPTION_RISE), caption, style, colour, "m", CAPTION_WIDTH)


def draw_sidebar(canvas, dd1, title, body, hand, way_past, item=None, slot=True):
    origin, size = scroll(canvas, dd1, "event_scroll_sidebar.png", dd1.get("sidebar_pos"))
    centre = size[0] / 2
    header(canvas, dd1, origin, centre, dd1.n("sidebar_header_y"), title)
    sb = dd1.get("sidebar_body")
    inv = dd1.get("sidebar_investigate")
    paragraph(canvas, dd1, (origin[0] + centre + sb[0], origin[1] + sb[1]), body, "scroll_body", dd1.n("sidebar_body_width"), "neutral", height=inv[1] - sb[1] - 6)
    # RaidScrolls.BuildSidebar: a curio's and an obstacle's pictures stand without words (hand: None, way_past: True)
    choice(canvas, dd1, origin, (centre + inv[0], inv[1]), "byhand.png", hand[0] if hand else None, hand[1] if hand else "curio_investigate_text", hand[2] if hand else "notable")
    if way_past:
        sp = dd1.get("sidebar_pass")
        choice(canvas, dd1, origin, (centre + sp[0], sp[1]), "pass.png", way_past if isinstance(way_past, str) else None, "curio_pass_text", "neutral")
    if slot:
        ss, io_ = dd1.get("sidebar_slot"), dd1.get("item_icon_offset")
        at = (origin[0] + centre + ss[0] + io_[0], origin[1] + ss[1] + io_[1])
        put(canvas, dd1.art("scrolls/use_inventory_active.png" if item else "scrolls/use_inventory.png"), at, tint=None if item else (0.6, 0.6, 0.6))
        if item:
            put(canvas, item_icon(dd1, item[0], item[1], item[2]), at)
            am = dd1.get("item_amount")
            line(canvas, dd1, (at[0] - io_[0] + am[0], at[1] - io_[1] + am[1]), str(item[2]), "inventory_amount", shadow=True)
    return origin


def draw_basic(canvas, dd1, title, body, hunger=None, options=None):
    origin, size = scroll(canvas, dd1, "event_scroll_basic.png", dd1.get("basic_pos"))
    centre = size[0] / 2
    if title:
        header(canvas, dd1, origin, centre, dd1.n("basic_header_y"), title)
    ok, cancel = dd1.get("basic_ok"), dd1.get("basic_cancel")
    if hunger is not None:
        inset = dd1.art("scrolls/inset_hunger.png")
        bi = dd1.get("basic_inset")
        put(canvas, inset, (origin[0] + centre + bi[0] - inset.width / 2, origin[1] + bi[1]))
        by = dd1.n("basic_body_y")
        paragraph(canvas, dd1, (origin[0] + centre, origin[1] + by), body, "scroll_body", dd1.n("basic_body_width"), "neutral", "m", ok[1] - by - 4)
        choice(canvas, dd1, origin, (centre + ok[0], ok[1]), "eat.png", dd1.string("str_ui_eat", "Eat") if hunger else dd1.string("str_meal_not_enough_provisions", "(Not Enough Food)"),
               "event_hunger_eat_text" if hunger else "event_hunger_eat_tooltip", "notable" if hunger else "harmful", dim=not hunger)
        choice(canvas, dd1, origin, (centre + cancel[0], cancel[1]), "starve.png", dd1.string("str_ui_starve", "Starve"), "event_hunger_starve_text")
        return origin
    bs = dd1.get("basic_button_size")
    rows = dd1.n("basic_button_y") - (len(options) - 1) * (bs[1] + ROW_GAP)
    paragraph(canvas, dd1, (origin[0] + centre, origin[1] + PLAIN_BODY_Y), body, "scroll_body", dd1.n("basic_body_width"), "neutral", "m", min(240, rows - PLAIN_BODY_Y - 8))
    plate = dd1.art("scrolls/choice_button_frame.png")
    for i, words in enumerate(options):
        at = (origin[0] + centre - bs[0] / 2, origin[1] + rows + i * (bs[1] + ROW_GAP))
        put(canvas, plate.resize((int(bs[0]), int(bs[1])), Image.BILINEAR), at, alpha=0.9)
        line(canvas, dd1, (at[0] + bs[0] / 2, at[1] + (bs[1] - 28) / 2), words, "confirm_dialog_answer", "notable" if i == 0 else "neutral", "m", bs[0] - 24)
    return origin


def draw_loot(canvas, dd1, title, description, stacks):
    origin, size = scroll(canvas, dd1, "event_scroll_loot.png", dd1.get("sidebar_pos"))
    lt, ld = dd1.get("loot_title"), dd1.get("loot_description")
    header(canvas, dd1, origin, lt[0], lt[1], title)
    y = dd1.n("loot_tiles_y")
    paragraph(canvas, dd1, (origin[0] + ld[0], origin[1] + ld[1]), description, "scroll_body", dd1.n("loot_description_width"), "neutral", "m", y - ld[1] - 8)
    pitch = dd1.n("loot_tile_offset")
    count = len(stacks)
    width = (count - 1) * pitch + 72
    left = lt[0] - width / 2
    if count > dd1.n("loot_min_items"):
        sy = origin[1] + y - dd1.n("loot_backdrop_y")
        edge_l, mid, edge_r = (dd1.art("scrolls/event_scroll_loot_%s.png" % n) for n in ("left_edge", "mid_section", "right_edge"))
        put(canvas, edge_l, (origin[0] + left - edge_l.width, sy))
        for i in range(count):
            piece = mid if i == count - 1 else mid.resize((int(pitch), mid.height), Image.BILINEAR)
            put(canvas, piece, (origin[0] + left + i * pitch, sy))
        put(canvas, edge_r, (origin[0] + left + width, sy))
    io_ = dd1.get("item_icon_offset")
    for i, (kind, ident, amount) in enumerate(stacks):
        card(canvas, dd1, (origin[0] + left + i * pitch - io_[0], origin[1] + y - io_[1]), kind, ident, amount)
    # RaidScrolls.LootButtonNudge: where DD1's own frame has the two pictures; no words under them
    ta, cl = dd1.get("loot_take_all"), dd1.get("loot_close")
    choice(canvas, dd1, origin, (ta[0] + LOOT_BUTTON_NUDGE[0], ta[1] + LOOT_BUTTON_NUDGE[1]), "byhand.png", None, "loot_take_all_text")
    choice(canvas, dd1, origin, (cl[0] + LOOT_BUTTON_NUDGE[0], cl[1] + LOOT_BUTTON_NUDGE[1]), "pass.png", None, "loot_close_text")
    return origin


def draw_complete_choice(canvas, dd1):
    fill(canvas, (0, 0, W, H), (0, 0, 0, int(255 * CHOICE_SHADE)))
    mid = dd1.get("complete_mid")
    frame = dd1.art("panels/quest_complete_choice_shared_frame.png")
    cf = dd1.get("complete_frame")
    put(canvas, frame, (mid[0] + cf[0] - frame.width / 2, mid[1] + cf[1]))
    seal = dd1.art("overlays/quest_complete.png")
    put(canvas, seal, (mid[0] - seal.width / 2, mid[1] - seal.height / 2))
    to = dd1.get("complete_tooltip_offset")
    for key, art, words in (("complete_return", "panels/quest_return_to_hamlet.png", dd1.string("raid_results_progression_return_to_town", "Return to Town")),
                            ("complete_continue", "panels/quest_continue_raid.png", dd1.string("str_continue_raid_tooltip", "Continue Adventuring"))):
        at = dd1.get(key)
        lit = key == "complete_return"       # the pointer rests on the way home: its tooltip hangs under it
        # RaidHighlight: under the pointer the art times button_highlight (FALLBACK numbers: DD1's stock 1.5 1.5 1.3)
        put(canvas, dd1.art(art), (mid[0] + at[0], mid[1] + at[1]), tint=(1.5, 1.5, 1.3) if lit else None)
        if lit:
            tooltip(canvas, dd1, (mid[0] + at[0] + BUTTON_ART[0] / 2, mid[1] + at[1] + BUTTON_ART[1] + to[1]), words, None, 200, centred=True)


def draw_confirm(canvas, dd1, question, answers, lit=0):
    fill(canvas, (0, 0, W, H), (0, 0, 0, 128))
    base = dd1.get("confirm_base")
    art = dd1.art("shared/confirm_dialog/confirm_dialog.background.png")
    put(canvas, art, (base[0] - art.width / 2, base[1]))
    q = dd1.get("confirm_question")
    paragraph(canvas, dd1, (base[0] + q[0], base[1] + q[1]), question, "confirm_dialog_question", dd1.n("confirm_question_width"), "neutral", "m")
    ao, start, spacing, size = dd1.get("confirm_answers_offset"), dd1.get("confirm_answers_start"), dd1.get("confirm_answer_spacing"), dd1.get("confirm_button_size")
    glow = dd1.art("shared/confirm_dialog/confirm_dialog.answer_text_selected_overlay.png")
    for i, words in enumerate(answers):
        cx, cy = base[0] + ao[0] + start[0] + spacing[0] * i, base[1] + ao[1] + start[1] + spacing[1] * i
        if i == lit:
            put(canvas, glow, (cx - glow.width / 2, cy - 24))
        line(canvas, dd1, (cx, cy + (size[1] - 28) / 2), words, "confirm_dialog_answer", "neutral", "m")


def notice(canvas, dd1, text):
    """DungeonHud.ShowNotice: the HUD's line over the inventory panel."""
    pad = round(128 * dd1.n("tooltip_border")) * 0.5 + dd1.get("tooltip_text_offset")[0]
    w = dd1.text.width(text, "tooltip") + 1
    h = dd1.text.line_height("tooltip")
    x, y = dd1.n("x_centre") + NOTICE_RISE[0], dd1.n("panel_top") - NOTICE_RISE[1] - h - 2 * pad
    tooltip_box(canvas, dd1, (x, y, x + w + 2 * pad, y + h + 2 * pad))
    dd1.text.draw(canvas, (x + pad, y + pad), text, "tooltip", "notable")


# ---------------------------------------------------------------- the camp

def draw_camp(canvas, dd1, meal):
    x, y = CAMP_SCROLL_POS
    if meal:
        art = dd1.art("scrolls/meal_scroll.png")
        put(canvas, art, (x, y))
        line(canvas, dd1, (x + art.width / 2, y + dd1.n("meal_header_y")), dd1.string("str_ui_meal_title", "Repast"), "scroll_header", "notable", "m", HEADER_WIDTH)
        pitch, by = dd1.n("meal_button_offset"), dd1.n("meal_button_y")
        left = (art.width - (3 * pitch + 72)) / 2
        am, io_ = dd1.get("item_amount"), dd1.get("item_icon_offset")
        for i, food in enumerate((0, 2, 4, 8)):
            at = (x + left + i * pitch, y + by)
            icon = item_icon(dd1, "provision", "", 12 * i // 3)
            put(canvas, icon, at, tint=(0.45, 0.45, 0.45) if food == 0 else None)
            line(canvas, dd1, (at[0] + am[0] - io_[0], at[1] + am[1] - io_[1]), str(food), "inventory_amount", shadow=True)
            line(canvas, dd1, (at[0] + 36, at[1] + 144 - 26), dd1.string("str_meal_title_%d" % i, "Meal"), "tooltip", "neutral", "m", 72)
        height = art.height
    else:
        art = dd1.art("scrolls/event_scroll_campingrespite.png")
        put(canvas, art, (x, y))
        rt, rp, rd = dd1.get("respite_title"), dd1.get("respite_points"), dd1.get("respite_description")
        line(canvas, dd1, (x + rt[0], y + rt[1]), dd1.string("camping_respite_title", "Respite"), "scroll_header", "notable", "l", rp[0] - rt[0] - 70)
        line(canvas, dd1, (x + rp[0], y + rp[1]), "9", "camping_points", "neutral", "r")
        rr = dd1.get("respite_rest")
        paragraph(canvas, dd1, (x + rd[0], y + rd[1]), dd1.string("camping_respite_description", "Use Camping skills."), "scroll_body", dd1.n("respite_description_width"), "neutral", height=rr[1] - rd[1] - 4)
        plate = dd1.art("overlays/announcement_rest.png")
        put(canvas, plate, (x + rr[0], y + rr[1]))
        rw = dd1.get("respite_rest_text")
        line(canvas, dd1, (x + rr[0] + rw[0], y + rr[1] + rw[1]), dd1.string("camping_respite_rest", "REST"), "rest", "notable", "m")
        height = art.height
    note = ("The party eats first: choose a meal on the scroll. 10 food is in the bag." if meal
            else "Click a hero to see their camping skills in the banner, a skill to use it. 33% chance of an ambush in the night.")
    log = ["The fire is lit. First, the meal."] if meal else ["The party eats 4 food (full rations): Dismas +2 health.", "Reynauld: Encourage for Junia: Junia -1 stress."]
    step = dd1.text.line_height("tooltip")
    lines = dd1.text.wrap(note, "tooltip", 456 - 2 * CAMP_NOTE_PAD) + [""] + sum((dd1.text.wrap(w, "tooltip", 456 - 2 * CAMP_NOTE_PAD) for w in log), [])
    ny = y + height + CAMP_NOTE_GAP
    tooltip_box(canvas, dd1, (x, ny, x + 456, ny + len(lines) * step + 2 * CAMP_NOTE_PAD))
    first = len(dd1.text.wrap(note, "tooltip", 456 - 2 * CAMP_NOTE_PAD))
    for i, words in enumerate(lines):
        dd1.text.draw(canvas, (x + CAMP_NOTE_PAD, ny + CAMP_NOTE_PAD + i * step), words, "tooltip", "neutral" if i < first else (113, 112, 105))


# ---------------------------------------------------------------- the provision screen

SHELVES = [("provision", "", 12, 75), ("supply", "shovel", 2, 250), ("supply", "antivenom", 6, 150), ("supply", "bandage", 6, 150), ("supply", "medicinal_herbs", 6, 200),
           ("supply", "skeleton_key", 6, 200), ("supply", "holy_water", 6, 150), ("supply", "laudanum", 6, 100), ("supply", "torch", 12, 75)]
PROVISION_BAG = [("supply", "holy_water", 1), ("provision", "", 8), ("supply", "torch", 8), ("supply", "shovel", 1), ("supply", "skeleton_key", 1)]


def town_layout(dd1, rel, block, key, fallback):
    p = dd1.path(rel)
    blocks = parse_darkest(io.open(p, encoding="utf-8-sig", errors="replace").read()) if os.path.isfile(p) else {}
    return tuple(floats(blocks.get(block), key, len(fallback))) if blocks.get(block, {}).get(key) else fallback


def draw_provision(dd1):
    canvas = Image.new("RGBA", (W, H), (10, 9, 8, 255))
    d = "campaign/town/provision/"
    town, lay = "campaign/town/town.layout.darkest", d + "provision.layout.darkest"
    art_pos = town_layout(dd1, town, "town_background_layout", "area_pos", (144, 132))
    char_pos = town_layout(dd1, town, "town_background_layout", "character_pos", (132, 240))
    name_pos = town_layout(dd1, lay, "provision_layout", "name_pos", (104, 126))
    sell_pos = town_layout(dd1, lay, "provision_layout", "provision_sell_back_info_pos", (1164, 510))
    quest_pos = town_layout(dd1, lay, "provision_layout", "quest_info_pos", (1300, 96))
    specs = town_layout(dd1, lay, "provision_layout", "quest_specs_offset", (20, -5))
    store_pos = town_layout(dd1, lay, "provision_store_background_layout", "pos", (814, 144))
    store_start = town_layout(dd1, lay, "provision_store_grid_layout", "start_pos", (120, 20))
    store_pitch = town_layout(dd1, lay, "provision_store_grid_layout", "offset", (80, 170))
    store_cols = int(town_layout(dd1, lay, "provision_store_grid_layout", "number_of_columns", (7,))[0])
    party_pos = town_layout(dd1, lay, "provision_party_background_layout", "pos", (800, 532))
    party_start = town_layout(dd1, lay, "provision_party_grid_layout", "start_pos", (60, 28))
    party_pitch = town_layout(dd1, lay, "provision_party_grid_layout", "offset", (80, 160))
    party_cols = int(town_layout(dd1, lay, "provision_party_grid_layout", "number_of_columns", (8,))[0])

    put(canvas, dd1.art(d + "provision.background.png"), (0, 0))
    put(canvas, dd1.art(d + "provision.character_background.png"), art_pos)
    put(canvas, dd1.art(d + "provision.character.png"), char_pos)
    put(canvas, dd1.art(d + "inventory_grid_background_store.png"), store_pos)
    cost = dd1.get("item_cost")
    for i, (kind, ident, left, price) in enumerate(SHELVES):
        cell = (store_pos[0] + store_start[0] + (i % store_cols) * store_pitch[0], store_pos[1] + store_start[1] + (i // store_cols) * store_pitch[1])
        card(canvas, dd1, cell, kind, ident, left)
        # cost_offset is the middle of the price, in the gap under the card
        line(canvas, dd1, (cell[0] + cost[0], cell[1] + cost[1] - dd1.text.line_height("town_currency_amount") / 2), amount_text("gold", price), "town_currency_amount", None, "m")
    put(canvas, dd1.art(d + "inventory_grid_background_party.png"), party_pos)
    for i, (kind, ident, amount) in enumerate(PROVISION_BAG):
        cell = (party_pos[0] + party_start[0] + (i % party_cols) * party_pitch[0], party_pos[1] + party_start[1] + (i // party_cols) * party_pitch[1])
        card(canvas, dd1, cell, kind, ident, amount, hover=i == 1)
    # its position is its middle
    line(canvas, dd1, (sell_pos[0], sell_pos[1] - dd1.text.line_height("provision_sell_back_info") / 2), dd1.string("provision_sell_back_info", "[CLICK] inventory to sell back"),
         "provision_sell_back_info", None, "m")
    # As measured on DD1's own screen: the particulars END at quest_info_pos, their line's top on it; the dungeon's
    # name ends quest_specs_offset.x before them, its line that offset's y higher; "Scouting (25.0%)" from
    # scouting_stat_pos in the particulars' style.
    words = dd1.string("town_quest_length_1", "Short") + " | " + dd1.string("town_quest_difficulty_1", "Apprentice (Lvl 1)")
    line(canvas, dd1, quest_pos, words, "provision_quest_info_specs", None, "r")
    line(canvas, dd1, (quest_pos[0] - dd1.text.width(words, "provision_quest_info_specs") - specs[0], quest_pos[1] + specs[1]), dd1.string("dungeon_name_crypts", "Ruins"),
         "provision_quest_info_dungeon_name", None, "r")
    scouting = town_layout(dd1, d + "provision.layout.darkest", "provision_layout", "scouting_stat_pos", (1380, 96))
    line(canvas, dd1, scouting, dd1.string("str_scouting", "Scouting") + " (25.0%)", "provision_quest_info_specs")
    # DD1's screen-name widget (UI/Dd1ScreenName.cs): the icon's corner at name_pos + 60 26, the name's line from
    # name_pos + 192 with its foot on name_pos.y + 118 (shared/name/name.layout.darkest)
    icon = dd1.art(d + "provision.icon.png")
    put(canvas, icon, (name_pos[0] + 60, name_pos[1] + 26))
    line(canvas, dd1, (name_pos[0] + 192, name_pos[1] + 118 - 63), dd1.string("town_name_provision", "Provision"), "town_name")

    # the bar: the gold as the hamlet's bar shows it, and the way forward ("Embark" on DD1's own provision screen)
    summary = town_layout(dd1, town, "town_screen_layout", "estate_summary_pos", (0, 975))
    offset = town_layout(dd1, "campaign/town/estate_summary/estate_summary.layout.darkest", "estate_summary_layout", "pos_offset", (0, -17))
    origin = (summary[0] + offset[0], summary[1] + offset[1])
    put(canvas, dd1.art("shared/progression/progression_bar.png"), origin)
    cur = town_layout(dd1, "campaign/town/estate_summary/estate_summary.layout.darkest", "estate_summary_layout", "currency_pos", (200, 42))
    put(canvas, dd1.art("shared/estate/currency.gold.large_icon.png"), (origin[0] + cur[0], origin[1] + cur[1] - 50))
    line(canvas, dd1, (origin[0] + cur[0] + 90, origin[1] + cur[1] - 16), "96", "town_large_currency_amount", dd1.text.colour("town_currency_amount"))
    prog = "shared/progression/progression.layout.darkest"
    fwd = town_layout(dd1, prog, "progression_layout", "forward_pos", (801, 984))
    ftx = town_layout(dd1, prog, "progression_layout", "forward_text_offset", (160, -2))
    put(canvas, dd1.art("shared/progression/progression_forward.png"), fwd)
    line(canvas, dd1, (fwd[0] + ftx[0], fwd[1] + ftx[1]), dd1.string("town_progression_forward_embark", "Embark"), "town_progression_forward", None, "m")

    # the tray and the way back
    tray = town_layout(dd1, town, "town_screen_layout", "embark_party_pos", (754, 871))
    party = "campaign/town/embark_party/embark_party.layout.darkest"
    tb = town_layout(dd1, party, "embark_party_layout", "background_offset", (-1, 0))
    ts = town_layout(dd1, party, "embark_party_layout", "hero_slot_start_offset", (22, 16))
    tp = town_layout(dd1, party, "embark_party_layout", "hero_slot_spacing", (93, 0))
    put(canvas, dd1.art("campaign/town/embark_party/embark_party.background.png"), (tray[0] + tb[0], tray[1] + tb[1]))
    for i, hero in enumerate(HEROES):
        at = (tray[0] + ts[0] + i * tp[0], tray[1] + ts[1])
        put(canvas, dd1.art("campaign/town/hero_slot/hero_slot.background.png"), at)
        put(canvas, dd1.art("heroes/%s/%s_A/%s_portrait_roster.png" % (hero["cls"], hero["cls"], hero["cls"])).resize((79, 79), Image.BILINEAR), (at[0] + 3, at[1] + 3))
    back = town_layout(dd1, prog, "progression_layout", "back_pos", (228, 82))
    put(canvas, dd1.art("shared/progression/progression_back.png"), back)
    dd1.text.draw(canvas, (back[0] + 32 + 12, back[1] + 4), "Return to the Estate Map", "town_quest_description", "neutral")

    # the tooltip of the card under the pointer (inventory_item_tooltip_layout)
    cell = (party_pos[0] + party_start[0] + party_pitch[0], party_pos[1] + party_start[1])
    to = dd1.get("item_tooltip_offset")
    tooltip(canvas, dd1, (cell[0] + to[0], cell[1] + to[1]), "Food", "A meal when the party grows hungry (1 a hero). On a hero: +5% health, 4 at most between meals.", dd1.n("item_tooltip_width"))
    return canvas


# ---------------------------------------------------------------- DD1's feedback over the scene

# RaidPop: kind -> (start_offset y, pop_y_offset, pop_time, fill, outline): FALLBACKS, DD1's stock values.
POPS = {
    "damage": (20, 100, 1.5, (0xe3, 0x09, 0x00), (0x39, 0x00, 0x00)),
    "hero_heal": (20, 100, 1.5, (0x4f, 0xcb, 0x4f), (0x2a, 0x44, 0x16)),
    "stress_damage": (120, -80, 1.0, (0x00, 0x00, 0x00), (0xc2, 0xc2, 0xc2)),
    "stress_reduce": (40, 80, 1.0, (0xfd, 0xf8, 0xc7), (0x7c, 0x66, 0x3f)),
    "full": (80, 100, 0.8, (0x00, 0x00, 0x00), (0x9a, 0x98, 0x8f)),
    "cured": (80, 100, 0.8, (0xfd, 0xf8, 0xc7), (0x7c, 0x66, 0x3f)),
    "buff": (80, 100, 0.8, (0x79, 0xf8, 0xff), (0x12, 0x72, 0x98)),
    "debuff": (80, 100, 0.8, (0xe2, 0x68, 0x26), (0x7f, 0x29, 0x0b)),
    "death_avoided": (80, 100, 1.5, (0x92, 0x04, 0x00), (0x1e, 0x04, 0x00)),
    "deathblow": (80, 100, 1.5, (0xff, 0x71, 0x00), (0x48, 0x08, 0x00)),
}
FRAME_ART = (619, 136)               # RaidAnnouncement.FrameArt (overlays/announcement_frame.png)
POP_ICON = 64                        # RaidPopText.IconSize (overlays/poptext_*.png)
POP_EDGE = 8                         # RaidPopText.EdgeMargin
TRAY_ICON_ART = 24                   # RaidTrays.IconArt (overlays/tray_*.png)
TRAY_TOOLTIP_RISE, TRAY_TOOLTIP_WIDTH = 44, 260      # RaidTrays.TooltipRise / TooltipWidth


def draw_announcement(canvas, dd1, place, words, colour, scale=1.0):
    """RaidAnnouncement: the frame centred on one of announcement's four points, the words' line cell hanging
    from text_offset. words: a text, or (lead, lead colour, rest) for "New Quirk:" and a name."""
    at, offset = dd1.get("announce_" + place), dd1.get("announce_text")
    art = dd1.art("overlays/announcement_frame.png")
    if art is not None and scale != 1.0:
        art = art.resize((int(art.width * scale), int(art.height * scale)), Image.BILINEAR)
    if art is not None:
        put(canvas, art, (at[0] - art.width / 2, at[1] - art.height / 2))
    else:
        fill(canvas, (at[0] - FRAME_ART[0] / 2, at[1] - 46, at[0] + FRAME_ART[0] / 2, at[1] + 40), (10, 10, 12, 230))
    style = "banner_header"
    if isinstance(words, tuple):
        lead, lead_colour, rest = words
        whole = lead + " " + rest
        size = fitted(dd1, whole, style, FRAME_ART[0])
        left = at[0] + offset[0] - dd1.text.width(whole, style, size) / 2
        dd1.text.draw(canvas, (left, at[1] + offset[1]), lead + " ", style, lead_colour, "l", size)
        dd1.text.draw(canvas, (left + dd1.text.width(lead + " ", style, size), at[1] + offset[1]), rest, style, colour, "l", size)
    else:
        line(canvas, dd1, (at[0] + offset[0], at[1] + offset[1]), words, style, colour, "m", width=FRAME_ART[0])


def pop_layout(dd1, kind):
    """(start_offset y, pop_y_offset, pop_time) of pop_text_layout_<kind>, DD1's or the fallback."""
    stock = POPS[kind]
    blocks = dd1._blocks("base.popup_text.layout.darkest") or {}
    block = blocks.get("pop_text_layout_" + kind)
    if block is None:
        dd1.used_fallback.append("pop_text_layout_" + kind)
    return floats(block, "start_offset", 2, stock[0])[1], floats(block, "pop_y_offset", 1, stock[1])[0], floats(block, "pop_time", 1, stock[2])[0]


def draw_pop(canvas, dd1, x, kind, text, share=0.0, ghost=True):
    """RaidPopText: a text of a kind over the hero standing at x, `share` of its way through its travel. The
    offsets are counted upwards from the hero's middle (status_bars.icon_world_y_offset above the ground) to
    the middle of the text; the kind's picture stands left of the word. ghost: its last place as well, faint."""
    start, rise, _ = pop_layout(dd1, kind)
    ground = dd1.n("tray_y") - 8                     # the stand-in figures' ground line
    middle = ground - dd1.n("hero_middle")
    font = dd1.text.font("pop_text")
    icon = dd1.art("overlays/poptext_%s.png" % kind)
    gap = dd1.get("pop_icon_offset")
    lead = (POP_ICON + gap[0]) if icon is not None else 0
    width = lead + font.width(text)
    for where, alpha in (((1.0, 0.28),) if ghost else ()) + ((share, 1.0 if share <= 0.75 else (1 - share) / 0.25),):
        y = middle - start - rise * where - font.line_height / 2
        left = max(POP_EDGE, min(W - POP_EDGE - width, x - width / 2))
        if icon is not None:
            put(canvas, icon, (left, y + (font.line_height - POP_ICON) / 2 + gap[1]), alpha=alpha)
        dd1.text.draw(canvas, (left + lead, y), text, "pop_text", dd1.text.colours.get("pop_text_" + kind, POPS[kind][3]), "l", alpha=alpha,
                      outline=dd1.text.colours.get("pop_text_outline_" + kind, POPS[kind][4]))


def draw_tray_icons(canvas, dd1, x, left=(), right=()):
    """RaidTrays' status icons over the hero at x: the left row ends at tray_icon_left_offset and grows to the
    left, the right row starts at tray_icon_right_offset; a 24 px picture is centred on its 20 px hot spot.
    Returns the hot spots' corners by icon name."""
    y, cx = dd1.n("tray_y"), dd1.n("tray_char_x")
    size = dd1.get("tray_icon_size")
    lo, ls, ro, rs = dd1.get("tray_icon_left"), dd1.n("tray_icon_left_spacing"), dd1.get("tray_icon_right"), dd1.n("tray_icon_right_spacing")
    places = {}
    for i, name in enumerate(left):
        places[name] = (x + cx + lo[0] - i * ls - size[0], y + lo[1])
    for i, name in enumerate(right):
        places[name] = (x + cx + ro[0] + i * rs, y + ro[1])
    for name, corner in places.items():
        art = dd1.art("overlays/tray_%s.png" % name)
        if art is None:
            fill(canvas, (corner[0], corner[1], corner[0] + size[0], corner[1] + size[1]), (177, 25, 0, 255))
        else:
            put(canvas, art, (corner[0] + (size[0] - TRAY_ICON_ART) / 2, corner[1] + (size[1] - TRAY_ICON_ART) / 2))
    return places


def buff_words(dd1, stat, amount, battles):
    """TrayIcons.BuffWords and the note of how long it lasts, in DD1's own formats."""
    text = dd1.string("buff_stat_tooltip_" + stat, "%+d " + stat)
    text = re.sub(r"\{[^}]*\}", "", text).replace("%+d", "%+d" % amount).replace("%%", "%")
    return dd1.string("tray_icon_tooltip_buff_duration_combat_end_format", "%s (%d Battles)").replace("%s", text).replace("%d", str(battles))


def draw_feedback(dd1, dungeon, light):
    """Two pictures. The banner low ("TRAP!") and at the two sides ("Surprised!") with pop texts over the
    party, each with its last place faint above or below it; the banner high ("... is at Death's Door!") with
    the trays' status icons and the tooltip of one of them. (The banner is in one place at a time in the game.)"""
    plain = lambda key, fallback: re.sub(r"\{[^}]*\}", "", dd1.string(key, fallback))
    first = base(dd1, dungeon, light=light)
    draw_tray_icons(first, dd1, HERO_X[0], right=("buff_plus",))
    draw_pop(first, dd1, HERO_X[0], "buff", plain("str_ui_buff", "Buff!"), 0.45)
    draw_pop(first, dd1, HERO_X[1], "damage", "7", 0.3)
    draw_pop(first, dd1, HERO_X[1], "stress_damage", "2", 0.3)
    draw_pop(first, dd1, HERO_X[2], "hero_heal", "5", 0.5)
    draw_pop(first, dd1, HERO_X[3], "cured", plain("str_ui_disease_cured", "%s Cured!").replace("%s", "Rabies"), 0.2)
    draw_announcement(first, dd1, "bottom", plain("str_its_a_trap", "TRAP!"), "trap_announcement")
    draw_announcement(first, dd1, "left", plain("surprise_announcement", "Surprised!"), "surprised")
    draw_announcement(first, dd1, "right", plain("surprise_announcement", "Surprised!"), "surprised")

    second = base(dd1, dungeon, hero=1, selected=1, light=light)
    draw_tray_icons(second, dd1, HERO_X[2], left=("deathsdoor", "deathsdoor_effects", "disease"), right=("buff_plus", "town_event"))
    draw_tray_icons(second, dd1, HERO_X[1], left=("deathsdoor",))
    draw_tray_icons(second, dd1, HERO_X[3], right=("buff_plus",))
    draw_pop(second, dd1, HERO_X[3], "stress_reduce", "3", 0.4)
    draw_pop(second, dd1, HERO_X[0], "full", plain("str_full", "Full!"), 0.3)
    draw_announcement(second, dd1, "top", plain("str_ui_deathdoor", "%s is at Death's Door!").replace("%s", HEROES[1]["name"]), "deathdoor")
    draw_announcement(second, dd1, "bottom", (plain("str_new_quirk_colon", "New Quirk:"), "neutral", "Hard Noggin"), "quirk_positive")
    # an icon's tooltip stands where the bars' does: over the tray, centred on the hero
    words = "\n".join([buff_words(dd1, "combat_stat_multiply_damage_low", 15, 4), buff_words(dd1, "combat_stat_add_speed_rating", 2, 4)])
    tooltip(second, dd1, (HERO_X[2], dd1.n("tray_y") - TRAY_TOOLTIP_RISE), None, words, TRAY_TOOLTIP_WIDTH, above=True, centred=True, body_colour="tray_icon_tooltip_positive")
    return first, second


# ---------------------------------------------------------------- main

def base(dd1, dungeon, **kw):
    canvas = scene(dd1, dungeon)
    figures(canvas, dd1, HERO_X)
    draw_panels(canvas, dd1, HEROES[kw.pop("hero", 0)], **kw)
    return canvas


def camp_scene(dd1):
    """Stand-in for DD2's rest stop: night, a fire, the party seated left of centre."""
    canvas = Image.new("RGBA", (W, H), (9, 11, 20, 255))
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for r, a in ((260, 30), (180, 50), (110, 80), (60, 130)):
        d.ellipse([760 - r, 600 - r // 2, 760 + r, 600 + r // 2], fill=(255, 120, 40, a))
    canvas.alpha_composite(layer)
    fill(canvas, (0, 720, W, H), (0, 0, 0, 255))
    return canvas


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=r"E:\Steam\steamapps\common\DarkestDungeon")
    ap.add_argument("--out", default=os.path.join(here, "..", "_lab", "preview"))
    ap.add_argument("--light", type=float, default=62)
    ap.add_argument("--dungeon", default="crypts")
    args = ap.parse_args()
    if not os.path.isfile(os.path.join(args.dd1, "campaign", "town", "town_bg.png")):
        raise SystemExit("Darkest Dungeon (1) not found at " + args.dd1 + " (pass --dd1)")
    os.makedirs(args.out, exist_ok=True)
    dd1 = Dd1(args.dd1)
    right = (dd1.n("x_centre"), dd1.n("panel_top"))
    log = ["Dismas grows uneasy in the dark.", "The darkness presses closer."]

    def save(canvas, name):
        path = os.path.join(args.out, name)
        canvas.convert("RGB").save(path)
        print("wrote", os.path.normpath(path))

    save(base(dd1, args.dungeon, light=args.light, log=log), "raid_hud.png")

    canvas = base(dd1, args.dungeon, hero=1, selected=1, inventory=True, light=args.light, hover_slot=1)
    cell = bag_cell(dd1, right, 1)
    to = dd1.get("item_tooltip_offset")
    # InventoryText.Tooltip: DD1's description, then DD1's discard line
    tooltip(canvas, dd1, (cell[0] + to[0], cell[1] + to[1]), "Food", dd1.string("str_inventory_description_provision", "Eat to restore health and stave off hunger.") + "\n" +
            dd1.string("str_discard_item_instructions", "[SHIFT-CLICK] on this item to discard it."), dd1.n("item_tooltip_width"))
    save(canvas, "raid_hud_inventory.png")

    canvas = base(dd1, args.dungeon, inventory=True, light=args.light, dim_bag={("supply", "skeleton_key")})
    draw_sidebar(canvas, dd1, dd1.string("str_curio_title_locked_sarcophagus", "Locked Sarcophagus"), dd1.string("str_curio_content_locked_sarcophagus", "An ornate sarcophagus. It's locked."),
                 None, True, ("supply", "skeleton_key", 2))
    save(canvas, "raid_curio.png")

    canvas = base(dd1, args.dungeon, inventory=True, light=args.light, dim_bag={("supply", "shovel")})
    draw_sidebar(canvas, dd1, dd1.string("str_obstacle_rubble_title", "Rubble"), dd1.string("str_obstacle_rubble_description", "A crude jumble of stones and debris blocks the path."),
                 None, None, ("supply", "shovel", 1))
    save(canvas, "raid_obstacle.png")

    canvas = base(dd1, args.dungeon, light=args.light)
    draw_basic(canvas, dd1, dd1.string("str_ui_hunger_title", "Hunger"),
               dd1.string("str_ui_hunger_content", "The exertions of adventuring have produced a growing hunger amongst the party."), hunger=True)
    save(canvas, "raid_hunger.png")

    canvas = base(dd1, args.dungeon, inventory=True, light=args.light)
    draw_basic(canvas, dd1, dd1.string("not_enough_room", "Not enough room!"), "The bag is full: no room for 2 Emerald, 1 Deed.", options=["Leave it behind", "Drop something for it"])
    save(canvas, "raid_plain.png")

    canvas = base(dd1, args.dungeon, inventory=True, light=args.light)
    draw_loot(canvas, dd1, dd1.string("str_overlay_loot_chest_title", "Treasure!"), dd1.string("str_overlay_loot_chest_description", "Yours for the taking..."),
              [("gold", "", 900), ("gem", "emerald", 2), ("heirloom", "deed", 1)])
    save(canvas, "raid_loot.png")

    canvas = base(dd1, args.dungeon, inventory=True, light=args.light)
    draw_loot(canvas, dd1, dd1.string("str_overlay_loot_battle_title", "Victory!"), dd1.string("str_overlay_loot_battle_description", "You have found:"),
              [("gold", "", 50)])
    save(canvas, "raid_loot_battle.png")

    canvas = base(dd1, args.dungeon, inventory=True, light=args.light)
    draw_loot(canvas, dd1, dd1.string("str_overlay_loot_hero_death_title", "Reclaimed:"), dd1.string("str_overlay_loot_hero_death_description", "From the fallen hero..."),
              [("trinket", "uncommon:berserk_charm", 1), ("trinket", "very_rare:sun_ring", 1)])
    save(canvas, "raid_loot_fallen.png")

    canvas = base(dd1, args.dungeon, inventory=True, light=args.light)
    draw_loot(canvas, dd1, dd1.string("str_overlay_loot_chest_title", "Treasure!"), dd1.string("str_overlay_loot_chest_description", "Yours for the taking..."),
              [("gold", "", 900), ("gem", "emerald", 2), ("heirloom", "deed", 1), ("supply", "torch", 2), ("provision", "", 3), ("gem", "jade", 1)])
    notice(canvas, dd1, dd1.string("not_enough_room", "Not enough room!") + " " + dd1.string("str_discard_item_instructions", "[SHIFT-CLICK] on this item to discard it."))
    save(canvas, "raid_loot_wide.png")

    canvas = base(dd1, args.dungeon, light=args.light, complete=True)
    draw_complete_choice(canvas, dd1)
    save(canvas, "raid_complete.png")

    canvas = base(dd1, args.dungeon, light=args.light)
    draw_confirm(canvas, dd1, dd1.string("retreat_confirm_raid_question", "Are you sure you want to retreat?"),
                 [dd1.string("retreat_confirm_raid_answer_yes", "Yes"), dd1.string("retreat_confirm_raid_answer_no", "No")], lit=1)
    save(canvas, "raid_retreat.png")

    for meal in (True, False):
        canvas = camp_scene(dd1)
        figures(canvas, dd1, [1100, 880, 560, 330])
        skills = [("encourage", 2, not meal, False), ("zealous_speech", 5, not meal, False), ("stand_tall", 4, not meal, not meal), ("unshakeable_leader", 4, not meal, False)]
        global HERO_X
        keep, HERO_X = HERO_X, [1100, 880, 560, 330]
        draw_panels(canvas, dd1, HEROES[0], light=100, camp_skills=skills, selected=0, targets=() if meal else (1, 2, 3))
        HERO_X = keep
        draw_camp(canvas, dd1, meal)
        save(canvas, "raid_camp_meal.png" if meal else "raid_camp_respite.png")

    save(draw_provision(dd1), "raid_provision.png")

    low, high = draw_feedback(dd1, args.dungeon, args.light)
    save(low, "raid_feedback.png")
    save(high, "raid_feedback_icons.png")
    if dd1.used_fallback:
        print("FALLBACK values used for:", ", ".join(sorted(set(dd1.used_fallback))))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
