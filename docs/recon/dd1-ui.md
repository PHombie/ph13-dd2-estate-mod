# DD1 UI: fonts, text styles and the hamlet's chrome

What DD1's files say about how its town screen is framed and set, and how the mod reproduces it. Everything is
read from the player's DD1 install at runtime; the numbers below are DD1's stock values (the fallbacks in the
code) with the file each comes from. Screen pixels of DD1's 1920x1080 layout, y down, art at native size.

Code: `Dd1/Dd1Fonts.cs`, `Dd1/Tga.cs`, `UI/UiKit.cs`, `Estate/HamletScreen.cs`, `Estate/RosterPanel.cs`,
`Estate/EstateSummary.cs`, `Estate/NarrationBox.cs`. Offline preview: `python tools/preview_hamlet.py`
→ `_lab/preview/hamlet_chrome.png` (`--heroes 11` scrolls, `--narration` adds the subtitle band);
`tools/dd1_bmfont.py` is the shared BMFont renderer for the other previews.

## 1. Fonts

### Files
`fonts/fonts.darkest`:
- `font: .id "<font id>" .file "fonts/<name>.fnt"`: the fonts. Four matter for English text:

| font id | file | face | line / base | capitals | native TMP size |
|---|---|---|---|---|---|
| `dwarven_axe_large` | `dwarvenaxe-l.fnt` | DwarvenAxe BB | 63 / 48 | 34 px | 53 |
| `dwarven_axe_medium` | `dwarvenaxe-m.fnt` | DwarvenAxe BB | 40 / 30 | 22 px | 34 |
| `ubuntu_medium` | `ubuntu_m.fnt` | Ubuntu (medium weight) | 28 / 23 | 17 px | 24 |
| `ubuntu_small` | `ubuntu.fnt` | Ubuntu | 25 / 21 | 16 px | 23 |

  (`popup` = DwarvenAxe with a 4 px outline for combat pop-ups, `pips` = skill pips; `dwarvenaxe-xl.fnt` is on
  disk but no `font:` entry uses it; `font_language_override:` swaps files for other languages.)
- `font_ref: .id "<style>" .font "<font id>"`: about 250 text styles. The layout files and the game code name
  styles, never fonts. There are **no sizes**: a font is drawn 1:1.
- `colours/base.colours.darkest` gives the same style ids their colour: `.rgba r g b a`, `.rgba #rrggbb` or
  `.shared_id "<other>"`. Three colours carry nearly everything: `neutral` 174 172 162 (running text),
  `notable` 200 180 110 (names, numbers), `harmful` 177 25 0 (warnings, the forward button).

Styles the hamlet uses: `town_estate_title` (large, #5d5a50), `town_roster_name` (medium, notable),
`town_roster_number` (ubuntu medium, neutral), `resolve_number` (ubuntu medium, black), `roster_full` (medium,
notable), `town_currency_amount` (ubuntu small, notable), `town_large_currency_amount` (large; no colour of its
own, drawn in `town_currency_amount`'s), `town_progression_forward` (large, harmful),
`town_activity_log_week_count` (medium, neutral), `subtitle_context_town` (large, notable). For the building
screens: `town_name` (large, notable: a screen's title), `town_activity_name` (medium, notable),
`town_activity_description`, `town_building_info`, `tooltip` (ubuntu small), `town_character_class`,
`town_upgrade_tree_title` (ubuntu medium), `town_quest_name` (medium), `town_quest_description` (ubuntu small),
`inventory_amount` (medium, notable).

### BMFont format, as shipped
AngelCode BMFont text: `info`, `common lineHeight= base= scaleW= scaleH= pages=`, `page id= file=`,
`char id= x= y= width= height= xoffset= yoffset= xadvance= page=`, `kerning first= second= amount=`.
The cursor is the top-left corner of the line cell; a glyph is drawn at cursor + (xoffset, yoffset).
- **`scaleW`/`scaleH` are wrong for the two DwarvenAxe fonts** (512x256 and 1024x512 declared, pages are
  256x256 and 512x512). Glyph rectangles are pixels of the page as stored; nothing is to be rescaled.
- Pages are TGA: type 2 (raw colour, 32 bit) for most, **type 10 (run-length) for `dwarvenaxe-l_0.tga`**, type 3
  (8-bit grey) for the small extension pages of `ubuntu.fnt`. Row order differs per file (descriptor bit 5).
  The Ubuntu pages declare 0 alpha bits and still keep the glyphs in the fourth byte.
- Coverage is the alpha channel (the grey value of an 8-bit page). RGB is white (Ubuntu, xl) or a copy of the
  coverage (DwarvenAxe m/l); only `popup` has real colour (white glyph on a black outline).
- DwarvenAxe m/l carry 158/162 kerning pairs, Ubuntu none. All four have Latin-1, curly quotes, dashes and the
  ellipsis; DwarvenAxe (241 characters) a part of Latin Extended-A, Ubuntu (853+) all of it, Greek and Cyrillic.

### In the plugin (`Dd1Fonts`)
`Dd1Fonts.Font(fontId)` / `Dd1Fonts.Style(styleId)` build a `TMP_FontAsset` on first use:
1. parse the .fnt, decode the TGA pages (`Tga.TryRead`);
2. copy every glyph into a new RGBA32 atlas: white, coverage in alpha, a 6 px clear gutter (TMP widens a bitmap
   glyph's quad by 1 texel, 5 with "extra padding"); shelves, tallest first, power-of-two size;
3. `ScriptableObject.CreateInstance<TMP_FontAsset>()`, then by hand: `m_Version = "1.1.0"` (an asset without a
   version gets "upgraded" from legacy tables), `m_FaceInfo`, `atlasPopulationMode = Static`, `atlasTextures`,
   `m_AtlasWidth/Height/Padding`, `m_AtlasRenderMode = SMOOTH` (a raster mode without the colour bit, so TMP
   tints glyphs with the label's colour), `glyphTable`, `characterTable`, kerning into
   `fontFeatureTable.glyphPairAdjustmentRecords`, `material`, `fallbackFontAssetTable`, `ReadFontAssetDefinition()`.
   Internal members are set through reflection (TMP 3.2 as shipped with the game).

Metrics: glyph index = running number from 1; `GlyphMetrics(width, height, bearingX = xoffset,
bearingY = base - yoffset, advance = xadvance)`; `GlyphRect` in the new atlas, origin bottom-left. Face:
`ascentLine = base`, `descentLine = base - lineHeight`, `lineHeight`, `baseline = 0`, `capLine` / `meanLine`
from H / x, `scale = 1`. With the ascent line at BMFont's `base`, a top-aligned TMP label puts every glyph
where DD1 does when its rect's top-left corner is DD1's text position.

Size: `pointSize = max(round(cap height / 0.705), ceil(line height / 1.2))`. 0.705 and 1.2 are cap line and
line height over point size of DD2's own font (NDDunkel: 62 and 105.6 at 88). At a given TMP size a DD1 font's
capitals and its line (ascent to descent) are therefore no taller than DD2's: a label made for DD2's font keeps
its size and its box. (The line matters: TMP's Truncate/Ellipsis overflow drops a line that is taller than
its rect, and DwarvenAxe's line is 1.29 of its cap-based size.) A DD1 font's **native size**
(`Dd1Font.NativeSize`, the table above) draws it pixel for pixel on the 1920x1080 canvas. Average advance per
size unit: NDDunkel 0.42, DwarvenAxe 0.27-0.30, Ubuntu 0.43-0.46, so text that fitted in DD2's font still fits.

Material: Unity's own **UI/Default** shader (`Canvas.GetDefaultCanvasMaterial().shader`): texture x vertex
colour, rect clipping and stencil masking like every other UI element. It is in every build. TMP's bitmap
shaders exist in the game too (`TextMeshPro/Bitmap`, `TextMeshPro/Mobile/Bitmap`, `TextMeshPro/Sprite` in the
addressable bundle `ui_assets_assets/includes/textmeshpro/shaders.bundle`) but `Shader.Find` only sees a
shader once something has loaded it; they are the second choice. TMP itself draws colour bitmap fonts and
sprite assets the same way (RGBA atlas, `TextMeshPro/Sprite`, a copy of UI/Default, texture taken from the
material).

Fallback: `Dd1Fonts.Fallback(font, UiKit.Font)` puts DD2's font first in the asset's fallback list (done by
UiKit every time it hands a font to a label); after it TMP searches its own settings' fallbacks. If DD1, a
file or the asset fails, `Font()` returns null (one warning in the log) and UiKit uses DD2's font.

When: a font is built synchronously, in one call, and enters the table whole or not at all; there is no
loading state. `Dd1Fonts.Preload()` builds the four text fonts at once; UiKit calls it before the first label
of a session (so `ubuntu_medium` is not made in the middle of a narration line). `UiKit.SetFont` never assigns
null: DD1 font if whole, else DD2's (`UiKit.Font`), else the default TMP gave the label. Asked before the DD1
install is found, nothing is cached (not the style table, not the colours, no "failed" mark): the next call
looks again.

Checked outside the game (the real `Dd1Fonts.cs` and `Tga.cs` compiled against stand-ins for Unity/TMP and run
on the DD1 install): every font page decodes to the pixels Pillow reads; the four fonts build (241/241/863/853
characters, atlases 512x256, 1024x512, 1024x512, 1024x512); text laid out by TMP's rules from the built glyph
table and atlas is pixel for pixel what `tools/dd1_bmfont.py` draws from the .fnt; no glyph touches another's
gutter. Not checked: TMP and the shader themselves, see section 4.

### UiKit
```csharp
UiKit.Text(name, parent, text, 30f, colour, align)                 // unchanged signature; font by size:
                                                                   //   >= 40 DwarvenAxe large, >= 24 DwarvenAxe medium, below Ubuntu small
UiKit.Text(name, parent, text, Dd1Text.Header, colour, align)      // by part, at native size; optional last argument: size
UiKit.Text(name, parent, text, "town_roster_name", null, align)    // by DD1 style: its font at native size, its colour unless one is passed
UiKit.Style(label, Dd1Text.Small, 16f); UiKit.Style(label, "town_activity_name");   // restyle an existing label
UiKit.NativeSize(Dd1Text.Title)                                    // 53
UiKit.Neutral / UiKit.Notable / UiKit.Harmful                      // DD1's three text colours
UiKit.Art(name, parent, "shared/estate/currency.gold.icon.png", topLeft)            // DD1 art at its own size
UiKit.Hover(gameObject, inside => ...)                             // pointer enter / leave
UiKit.UseDd1Fonts = false                                          // everything back in DD2's font
```
`Dd1Text`: `Title` = dwarven_axe_large, `Header` = dwarven_axe_medium, `Body` = ubuntu_medium, `Small` =
ubuntu_small, `Numbers` = ubuntu_medium (levels and counts on badges: `town_roster_number`, `resolve_number`).
`UiKit.Parchment` / `Gold` are unchanged (the building panels dim text against Parchment).

## 2. The town screen's chrome in DD1's files

`campaign/town/town.layout.darkest`
| entry | value | meaning |
|---|---|---|
| `town_estate_title_layout.pos` | 0 0 | `estate_title/estate_nameplate.png` (893x281): crest, then a brush-stroke band, y 72..122, x 240..~790 |
| `town_estate_title_layout.text_offset` | 286 70 | the estate's name on the band: `town_estate_title`; string `estate_title_format` "The %s Estate", default name `estate_title_default_data` "Darkest" |
| `town_screen_layout.roster_list_pos` | 1550 0 | origin of the roster column (370 px wide on screen) |
| `town_screen_layout.estate_summary_pos` | 0 975 | origin of the estate summary |
| `town_screen_layout.embark_party_pos` | 754 871 | party tray (estate map; `embark_party/`), not on the plain town screen |
| `town_background_layout.area_pos` / `character_pos` | 144 132 / 132 240 | a building screen's backdrop and keeper |

`campaign/town/roster/roster.layout.darkest`, `town_roster_list_layout` (from the roster origin)
| entry | value | meaning |
|---|---|---|
| `element_pos`, `element_spacing`, `element_max` | 0 132, 0 97, 8 | rows of `rosterelement.background.png` (395x104, overlapping 7 px); 8 show |
| `top_frame_offset` | 20 -50 | `roster_topframe.png` (383x60) from the first row: screen y 82. Its smoky band starts at x 60 |
| `bottom_frame_offset` | 20 -10 | `roster_bottomframe.png` from the end of the list (132 + 8 x 97 = 908): y 898..958, down to the bar |
| `roster_message_offset` | 60 78 | where DD1 writes "Full" (`str_roster_list_full`, style `roster_full`) |
| `roster_sort_start_position`, `roster_sort_spacing` | 148 80, 6 0 | four 32 px buttons `roster_sort_{building,class,level,stress}.png` |
| `roster_sort_current_{ascending,descending}_overlay_offset` | -8 -8 / -8 24 | `roster_sort_current_overlay.png` (49x16) over / under the button in use |
| `scroll_up_button_offset`, `scroll_down_button_offset` | 0 -38 / 0 -12 | `roster_uparrow.png` / `roster_downarrow.png` (62x49); taken from the first row's corner and from the end of the list |
| `roster_bggrad.png` | | 373x1080 gradient down the column |

`town_roster_element_layout` (from a row's corner)
| entry | value | meaning |
|---|---|---|
| `portrait_icon_offset` | 21 9 | 85x85 portrait |
| `non_building_icon_offset` | 14 10 | `party.icon_roster.png` (wax seal), `missing.icon_roster.png` |
| `building_icon_offset` | 20 10 | `buildings/<id>/<id>.icon_roster.png` (abbey, sanitarium, tavern; 64x64) |
| `name_offset` | 116 4 | `town_roster_name` |
| `stress_offset`, `stress_spacing` | 116 43, 10 0 | ten pips: `overlays/stress_pip_empty.png` (8x12), `stress_pip_full.png` (9x10) |
| `weapon_level_offset`, `armour_level_offset` | 156 65 / 228 65 | numbers beside the sword and armour drawn into the row art (`town_roster_number`) |
| `resolve_level_bar_offset` | 258 4 | resolve widget: see below |
| `rosterelement_res1..6.png` | | the row's edge in the colour of the resolve level |
| `roster.layout.anim.darkest` `town_roster_mouseover_element_anim` | 30 px, 0.2 s | the row under the pointer slides left |

`shared/resolve_level_bar/resolve_level_bar.layout.darkest`: `resolve_level_number.background_offset -3 0`
(`resolve_level_bar_number_background_lvl0..6.png`, 64x64), `.number_offset 30 31` (centre of the number,
`resolve_number`); `resolve_level.bar_pos 12 28` + `resolve_level_bar.gradient_offset 10 16`, `gradient_size
16 40`, mask 37x59: the experience bar hanging under the badge (not drawn by the mod, see 3).

`campaign/town/estate_summary/estate_summary.layout.darkest` + `shared/estate/estate.layout.darkest`
| entry | value | meaning |
|---|---|---|
| `estate_summary_layout.pos_offset` | 0 -17 | with `estate_summary_pos`: the bar's corner is 0, 958. `shared/progression/progression_bar.png` (1920x138) there puts its lit band (art rows 22..83) at y 980..1041, centred on the icons, the numbers and the forward button; its black foot hangs 16 px below the screen |
| `currency_pos` | 200 42 | first currency: 200, 1000 |
| `estate_large_currency_layout.icon_offset` / `number_offset` | 0 -50 / 90 -16 | gold: `shared/estate/currency.gold.large_icon.png` (88x88) at 200, 950; count at 290, 984 (`town_large_currency_amount`) |
| `large_currency_spacing` | 200 0 | heirlooms start at 400, 1000 |
| `estate_currency_heirloom_layout.icon_offset` / `number_offset` | 0 -10 / 38 -4 | `currency.<id>.icon.png` (40x40), count (`town_currency_amount`) |
| `currency_spacing` | 74 0 | step between heirlooms |
| order | `campaign/estate/estate.json` `currencies` | gold, bust, portrait, deed, crest |
| `navigation_button_start_pos`, `navigation_button_offset` | 1800 45 / -110 0 | DD1's 113 px buttons at the bar's right end (`activity_log.icon.png`, `realm_inventory.icon.png`, `glossary.icon.png`); the numbers only fit as centres. Unused by the mod so far |
| `heirloom_exchange_offset` | 700 51 | DD1's "trade heirlooms" button. Unused |

`shared/progression/progression.layout.darkest`
| entry | value | meaning |
|---|---|---|
| `forward_pos` | 801 984 | `progression_forward.png` (312x52): the town's Embark, the map's Provision, ... in the middle of the bar |
| `forward_text_offset` | 160 -2 | middle of the label's top edge (`town_progression_forward`; string `town_progression_forward_embark`) |
| `forward_selected_overlay_offset` | 0 -13 | `progression_forward_selected_overlay.png` (311x24) under the pointer |
| `back_pos` | 228 82 | `progression_back.png` (32x33): the red arrows at the head of the name plate's band |

`shared/app.darkest`: `s_TownSubtitleContextTunables.screen_position 960 990`, `.max_width 1280`,
`s_SubtitleGlobalTunables.background_y_offset 100`: the Ancestor's subtitle, centred on that point, over
`shared/subtitles/subtitle_bg.png` (1920x240) whose top is 100 px above it.

DD1 does **not** show the week on the town screen (it is in the activity log and on the save slot), has no
"Roster n/m" header (the message spot is empty until the barracks are full) and no class line on a roster row.

## 3. What the mod adds or changes (its own numbers)

| what | where | why |
|---|---|---|
| estate name | "The Darkest Estate" from DD1's two strings | the estate has no name of its own yet |
| "Week n" (`str_week`) | on the plate's band, right aligned at x 772, cell top y 77, `town_activity_log_week_count` | the mod has no activity log |
| Return to Menu | DD1's back arrows at `back_pos`; "Return to Menu" appears under them on hover | DD1 leaves a screen by these arrows; from the hamlet the way back is the menu |
| "Roster" + "n/m" | "Roster" at `roster_message_offset`, the count right aligned at x 364 past the sort buttons (red when full) | asked for; 1:1 DwarvenAxe does not fit both before the buttons |
| class, path / block reason | two right-aligned Ubuntu lines (size 15, shrinking to 11) in the box 262, 66, 104x34 under the resolve badge; class and path each in `<nobr>`, a reason wraps | several heroes of one class differ by path. It takes the place of DD1's experience bar (not drawn). Longest class "Plague Doctor" is 89 px: clear of the armour number, which ends at 258 |
| health | bar 100x3 at `stress_offset` + (0, -5), mid tone of DD1's `tray_health_bar_default_current_*` | DD1's row has no health (the mod heals at week's end too) |
| dismiss | `shared/character/icon_dismiss.png` at 330, 8, while the pointer is on the row of a hero who may go | DD1 dismisses from the character sheet |
| portrait | DD2's portrait drawn 98 px, cut off by a `RectMask2D` window inset 3 px in `hero_slot.background.png`; `hero_slot.backgroundhightlight.png` for the party; DD2's grey portrait at 0.55 for a hero who cannot embark | DD1's portraits are faces filling the 85 px square; DD2's are heads with wide clear margins (at 73 px a head came out 32-43 px wide) |
| other block reasons | `hero_slot.negative_frame.png` over the portrait | DD1 has icons only for abbey, sanitarium, tavern and "missing" |
| sorting | buttons in the order building, class, level, stress; click = ascending, again = descending, again = order of arrival | DD1's order of the buttons is not in a file |
| narration | DD1's seat (centred on 960, 990: on the bar) with the band art laid four deep (64% → 98% black) so the bar's gold and Embark do not read through; a text of more than two lines keeps 24 px from the screen's foot and pushes the band up; stays until clicked or hidden; `Show` moves the band to the canvas it is given | DD1's single wash lets the bar show through the words; DD1's subtitles time out |

Screen widths other than 16:9: the plate stays at the left edge, the roster column at the right edge, the bar
spans the width, Embark and the narration keep the middle. `RosterPanel.Width` is 370 (was 372).

## 4. Verified in game, and not
Seen in game (2560x1440, `_lab/shots/hamlet_new.png`, `wagon_ingame.png`, `quest_map_many.png`): TMP takes the
hand-built assets and draws them with UI/Default, tinted, sharp, no boxes and no bleed; the plate, the roster
rows, the currency strip and Embark stand as in the offline preview; the panels that ask by size get the DD1
fonts; Ellipsis works.

Not yet seen in game (changed after that check, or not exercised by it):
- the deepened narration band over the hamlet, a building window, the estate map and the dungeon HUD;
- native sizes 34 / 53 for DwarvenAxe (text asked by size comes out about 8% smaller than in that check) and
  Ubuntu for sizes 22-23;
- DD2's portrait at 98 px in the clipped window, and DD2's greyscale portrait for a blocked hero;
- a glyph missing from a DD1 font falling back to DD2's font (a sub-mesh object appears under the label);
- the row's slide and the dismiss control under the pointer, Embark's glow, the "Return to Menu" hint,
  the wheel and the arrows with more than eight heroes, the sort buttons;
- screens that are not 16:9.
Unknown from the files: where DD1 really hangs the scroll arrows (taken from the first row's corner and the
end of the list), and the order of its sort buttons.

## 5. Read off DD1's own screens (2026-10-05, frames in `_lab/dd1_ref/town/`, pairs in `_lab/mod_now/pairs/`)

Where a section above and this one disagree, this one holds: it was measured on 1920x1080 frames of the real
game by looking for DD1's own pictures and its own font on them (pixel for pixel; the measuring scripts are not
in the repo).

- **The screen-name widget** (`UI/Dd1ScreenName.cs`, `shared/name/name.layout.darkest`): the icon's corner is
  `name_pos + icon_offset` on every screen (Stage Coach 164,152; Estate Map 164,148; Provision 164,152; town
  event 164,152; Glossary 158,150; Activity Log 158,146 and 854,146). The name's line cell begins at
  `text_offset.x` and ends on `text_offset.y` (its foot; the line is 63 px). The "+" (`more_info_icon.png`) has
  its corner at 200,238 on seven buildings: its middle is `name_pos + icon_offset + sub_icon_offset`, the icon's
  bottom middle, and the cross of an open pane stands in the same place. The exclamation mark's middle is
  `name_pos + alert_offset` (219,217), on the icon of the building on show; that building's button of the quick
  navigation carries none.
- **The quick navigation's marks** are DD1's exclamation mark at its full size (24x60 in its 74x77 glow) with
  the middle at `exclamation_point_offset` 28 32 from the button's corner: they cover the 56 px buttons in DD1 too.
- **The bar's row**: candles 1800, book 1690, chest 1580, scroll 1470 and, in a week with a town event, the
  crier's bell at 1360; a button that is away leaves no gap. Under the pointer a button (the exchange's icon
  too) gets the two rules of `estate_summary.selected_overlay.png` and no tooltip. On the Estate Map and the
  provision screen the row is drawn at full strength; only the exchange's icon is dark (9.8 of 35.6 of light).
  While a building's screen, the Activity Log or the crier's notice is up the forward button is its picture
  without colour at 0.4 and has no word; with the Trinket Inventory, the Glossary or the exchange open it is as
  ever. Seen and not built: while the crier's notice is up DD1 shows the Activity Log's scroll open as well.
- **Tooltips**: an heirloom's box begins at its place + `tooltip_offset` (the portraits' at 475,952);
  "Sort by Level" stands centred over its sort button, its foot `roster_sort_tooltip_offset` from the button's
  corner (`Dd1Tooltip.ShowOver`).
- **The roster**: a hero of the party has a dimmed face under the party's seal, in the hamlet and on the map.
- **DD1's scroll bar** is one widget (`Dd1ScrollBar` in `UI/UiKitWindows.cs`, made by `Dd1Ui.ScrollList`):
  rail as tall as the list's window, `scrollbar_offset` right of it; the bottom cap begins where the window
  ends; the pip's middle is on the rail's top end with the list at its top; the arrows are centred on the rail,
  the upper one's picture 32 px above the window, the lower one's 5 px under it. It is drawn whether or not
  the list scrolls (the Graveyard without a grave has all of it). Alike on the Activity Log's two lists, the
  Glossary, the Graveyard and the Memoirs (whose `scrollbar_offset 20 -50` moves nothing that can be seen).
- **One player of DD1's Spine sprites on a canvas**: `UI/SpineView.cs` (`Dd1Spine`, `SpineUi`, `Dd1Fx` are gone).
- **One confirm dialog on the town's side**: `Estate/TownConfirm.cs` (DD1's dialog as the raid builds it,
  `RaidConfirm`, over the hamlet's canvas). **One price in gold**: `UpgradeUi.BuildGoldPrice`.
- Bridge: `confirm.state / answer / ask / close`, `ui.scrollbars`, `ui.scroll`, `bar.state {"click":id}`,
  `roster.hold {"guid"}`, `guild.equip`.
