using System;
using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.UI;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Where DD1 puts the parts of its quest select screen, read from the player's install:
    /// campaign/town/quest_select/quest_select.layout.darkest (regions, quest markers, details panel, the
    /// party's name) and quest_select.anim.darkest (the pulse of the chosen marker, the filling of a region's
    /// bar, the fades of the party's name), campaign/town/town.layout.darkest and embark_party/
    /// embark_party.layout.darkest (the party tray), shared/progression/progression.layout.darkest (the way
    /// back). All in screen pixels of a 1920x1080 frame, y down.
    ///
    /// FALLBACK: every number written out in this class is DD1's own value of that entry and is used only
    /// when the file or the entry cannot be read. tools/preview_quest_map.py keeps the same table.
    /// </summary>
    internal class QuestMapLayout
    {
        public const string Dir = "campaign/town/quest_select/";

        // FALLBACK (see the class text): quest_map_pos and dungeon_effect_overlay_pos of DD1's five regions.
        private static readonly Dictionary<string, Vector4> Regions = new Dictionary<string, Vector4>
        {
            { "crypts", new Vector4(940f, 220f, 1050f, 260f) },
            { "weald", new Vector4(1000f, 525f, 1000f, 560f) },
            { "warrens", new Vector4(815f, 400f, 720f, 460f) },
            { "cove", new Vector4(1260f, 400f, 1270f, 480f) },
            { "darkestdungeon", new Vector4(1260f, 100f, 1230f, 70f) }
        };
        private const int QuestsInRow = 4;

        private readonly DarkestFile _select;

        /// <summary>The screen's icon and name; the party's name above the tray (top centre of its line) and the origin of the effect a named party sets off.</summary>
        public readonly Vector2 NamePos, PartyName, PartyCombo;

        // A region's parts, relative to its place on the map.
        public readonly Vector2 NodeBackground, NodeName, NodeLevel, NodeBar, NodeBarSize, NodeBarFx, NodeTooltip, NodeHot, NodeHotSize, NodeLock;
        /// <summary>The week's town event on a region: its icon, the area that tells of it and where the telling goes.</summary>
        public readonly Vector2 NodeEvent, NodeEventTooltip, NodeEventHot, NodeEventHotSize;
        /// <summary>Centre of a region's first row of quest markers, and the step between markers and rows.</summary>
        public readonly Vector2 MarkerStart, MarkerSpacing;
        /// <summary>Corner of the "selected" splash, relative to the marker's centre.</summary>
        public readonly Vector2 MarkerSelected;

        // The details panel and what is on it, relative to the panel.
        public readonly Vector2 PanelPos, QuestName, Description, Specifics, Camping, CampingHot, CampingHotSize, CampingTooltip, Goals, GoalStart, RewardsTitle, RewardsGrid, RewardPitch;
        /// <summary>The week's town event in the panel's foot: its icon, "Town Event:" and what it does.</summary>
        public readonly Vector2 NoticeIcon, NoticeTitle, NoticeText;
        public readonly float DescriptionWidth, NoticeWidth;
        public readonly int RewardColumns;

        // The party tray and the way back (the way forward is TownChrome's: it stands where the hamlet's Embark stands).
        public readonly Vector2 TrayPos, TrayBackground, SlotStart, SlotSpacing, Back;
        /// <summary>The picture a town event puts behind the tray (the middle of its foot, from the tray's corner) and how long it takes to come up.</summary>
        public readonly Vector2 TrayEvent;
        public readonly float TrayEventSeconds;

        /// <summary>The chosen marker swells by <see cref="PulseScale"/> and back, <see cref="PulseSeconds"/> each way.</summary>
        public readonly float PulseSeconds, PulseScale;
        /// <summary>A region's bar takes this long to fill up to where it stands now; the party's name this long to come and to go.</summary>
        public readonly float BarSeconds, PartyNameIn, PartyNameOut;

        public QuestMapLayout()
        {
            _select = DarkestFile.Load(Dir + "quest_select.layout.darkest");
            var anim = DarkestFile.Load(Dir + "quest_select.anim.darkest");
            var town = DarkestFile.Load("campaign/town/town.layout.darkest");
            var party = DarkestFile.Load("campaign/town/embark_party/embark_party.layout.darkest");
            var progression = DarkestFile.Load("shared/progression/progression.layout.darkest");

            NamePos = Offset(_select, "town_quest_select_layout", "name_pos", 104f, 122f);
            var partyName = Offset(_select, "town_quest_select_layout", "party_name_pos", 756f, 834f);
            PartyName = partyName + Offset(_select, "town_quest_select_party_name_layout", "text_offset", 190f, 8f);
            PartyCombo = partyName + Offset(_select, "town_quest_select_party_name_layout", "change_animation_offset", 185f, 410f);

            const string node = "town_quest_select_dungeon_layout";
            NodeBackground = Offset(_select, node, "background_offset", -5f, -10f);
            NodeName = Offset(_select, node, "dungeon_name_offset", 190f, -8f);
            NodeLevel = Offset(_select, node, "dungeon_level_offset", 219f, 20f);
            NodeBar = Offset(_select, node, "dungeon_xp_bar_offset", 14f, 32f);
            NodeBarSize = Offset(_select, node, "dungeon_xp_bar_size", 194f, 8f);
            NodeBarFx = Offset(_select, node, "dungeon_xp_bar_fx_offset", 0f, 4f);
            NodeTooltip = Offset(_select, node, "dungeon_xp_bar_tt_offset", 0f, -88f);
            NodeHot = Offset(_select, node, "dungeon_xp_bar_tt_hot_area_offset", 14f, 0f);
            NodeHotSize = Offset(_select, node, "dungeon_xp_bar_tt_hot_area_size", 232f, 48f);
            NodeLock = Offset(_select, node, "locked_icon_offset", 184f, -10f);
            NodeEvent = Offset(_select, node, "town_event_icon_offset", -32f, -14f);
            NodeEventTooltip = Offset(_select, node, "town_event_icon_tooltip_offset", 0f, 0f);
            NodeEventHot = Offset(_select, node, "town_event_icon_tooltip_hot_area_offset", -32f, -14f);
            NodeEventHotSize = Offset(_select, node, "town_event_icon_tooltip_hot_area_size", 32f, 32f);
            MarkerStart = Offset(_select, node, "quest_button_start_offset", 160f, 92f);
            MarkerSpacing = Offset(_select, node, "quest_button_spacing", 76f, 80f);
            MarkerSelected = Offset(_select, "town_quest_select_quest_button_layout", "selected_background_offset", -94f, -94f);

            const string quest = "town_quest_select_quest_layout";
            PanelPos = Offset(_select, quest, "description_pos", 125f, 132f);
            QuestName = Offset(_select, quest, "name_text_offset", 200f, 140f);
            Description = Offset(_select, quest, "description_text_offset", 34f, 194f);
            DescriptionWidth = Number(_select, quest, "description_text_width", 340f);
            Specifics = Offset(_select, quest, "specifics_text_offset", 226f, 390f);
            Camping = Offset(_select, quest, "camping_text_offset", 58f, 388f);
            CampingHot = Offset(_select, quest, "camping_tt_pos", 10f, 375f);
            CampingHotSize = Offset(_select, quest, "camping_tt_size", 80f, 30f);
            CampingTooltip = Offset(_select, quest, "camping_tt_offset", 90f, 9f);
            Goals = Offset(_select, quest, "goals_text_offset", 34f, 430f);
            GoalStart = Offset(_select, quest, "goal_description_text_start_offset", 50f, 460f);
            RewardsTitle = Offset(_select, quest, "rewards_text_offset", 200f, 530f);
            const string grid = "town_quest_rewards_inventory_system_grid_layout";
            RewardsGrid = Offset(_select, quest, "rewards_inventory_system_offset", 22f, 562f) + Offset(_select, grid, "start_pos", 20f, 28f);
            RewardPitch = Offset(_select, grid, "offset", 80f, 160f);
            RewardColumns = Mathf.Max(1, Mathf.RoundToInt(Number(_select, grid, "number_of_columns", 4f)));
            NoticeIcon = Offset(_select, quest, "town_notification_icon", 60f, 750f);
            NoticeTitle = Offset(_select, quest, "town_notification_title_text", 110f, 750f);
            NoticeText = Offset(_select, quest, "town_notification_text", 110f, 774f);
            NoticeWidth = Number(_select, quest, "town_notification_text_width", 400f);

            TrayPos = Offset(town, "town_screen_layout", "embark_party_pos", 754f, 871f);
            TrayBackground = Offset(party, "embark_party_layout", "background_offset", -1f, 0f);
            SlotStart = Offset(party, "embark_party_layout", "hero_slot_start_offset", 22f, 16f);
            SlotSpacing = Offset(party, "embark_party_layout", "hero_slot_spacing", 93f, 0f);
            TrayEvent = Offset(party, "embark_party_layout", "town_event_overlay_offset", 206f, 124f);
            TrayEventSeconds = Mathf.Max(0f, Number(party, "embark_party_layout", "town_event_fade_time", 1f));
            Back = Offset(progression, "progression_layout", "back_pos", 228f, 82f);

            PulseSeconds = Mathf.Max(0.1f, Number(anim, "town_quest_select_pulse_anim", "selected_seconds_time", 0.6f));
            PulseScale = Number(anim, "town_quest_select_pulse_anim", "selected_scale", 0.2f);
            BarSeconds = Mathf.Max(0f, Number(anim, "town_quest_select_xp_bar_anim", "seconds_time", 2f));
            PartyNameIn = Mathf.Max(0f, Number(anim, "town_quest_select_party_name_anim", "text_fade_in_time", 1f));
            PartyNameOut = Mathf.Max(0f, Number(anim, "town_quest_select_party_name_anim", "text_fade_out_time", 1f));
        }

        /// <summary>Whether DD1's map has a place for this region.</summary>
        public bool Has(string dungeonId)
        {
            return Block(dungeonId) != null || Regions.ContainsKey(dungeonId);
        }

        public Vector2 MapPos(string dungeonId)
        {
            Regions.TryGetValue(dungeonId, out var stock);
            return Offset(_select, BlockName(dungeonId), "quest_map_pos", stock.x, stock.y);
        }

        /// <summary>Where dd_effect_&lt;dungeon&gt;.png lies on the map.</summary>
        public Vector2 EffectPos(string dungeonId)
        {
            Regions.TryGetValue(dungeonId, out var stock);
            return Offset(_select, BlockName(dungeonId), "dungeon_effect_overlay_pos", stock.z, stock.w);
        }

        public int InRow(string dungeonId)
        {
            return Mathf.Max(1, Mathf.RoundToInt(Number(_select, BlockName(dungeonId), "quest_number_of_quests_in_row", QuestsInRow)));
        }

        /// <summary>
        /// Centres of a region's quest markers: rows of <see cref="InRow"/>, each row centred on the start
        /// offset. (The file does not say how a row is aligned; centred is the one reading with which DD1's
        /// own rows of eleven, the `all_quest_*` entries, stay between the details panel and the roster.)
        /// A row of four at the Cove or the Darkest Dungeon ends a little under the roster column's edge, as
        /// the layout has it: the column lies over the map.
        /// </summary>
        public List<Vector2> MarkerCentres(string dungeonId, int count)
        {
            var origin = MapPos(dungeonId) + MarkerStart;
            var inRow = InRow(dungeonId);
            var centres = new List<Vector2>();
            for (var i = 0; i < count; i++)
            {
                int row = i / inRow, column = i % inRow;
                var inThisRow = Mathf.Min(inRow, count - row * inRow);
                var half = (inThisRow - 1) * 0.5f * MarkerSpacing.x;
                centres.Add(new Vector2(origin.x - half + column * MarkerSpacing.x, origin.y + row * MarkerSpacing.y));
            }
            return centres;
        }

        /// <summary>
        /// Centres of one row of <paramref name="count"/> places at a region, whatever DD1's number of quests in
        /// a row: the row the Darkest Dungeon's five stand-ins stand in (DD1 has no row of five there). It is
        /// centred on the region's start offset like DD1's rows, and moved left as far as it takes for its last
        /// place to stay left of <paramref name="rightEdge"/> (the roster column lies over the map's right edge).
        /// </summary>
        public List<Vector2> RowCentres(string dungeonId, int count, float rightEdge)
        {
            var origin = MapPos(dungeonId) + MarkerStart;
            var half = (count - 1) * 0.5f * MarkerSpacing.x;
            var first = origin.x - half - Mathf.Max(0f, origin.x + half - rightEdge);
            var centres = new List<Vector2>();
            for (var i = 0; i < count; i++) centres.Add(new Vector2(first + i * MarkerSpacing.x, origin.y));
            return centres;
        }

        private DarkestFile.Block Block(string dungeonId) => _select?.Find(BlockName(dungeonId));

        private static string BlockName(string dungeonId) => "quest_select_dungeon_layout_" + dungeonId;

        private static Vector2 Offset(DarkestFile layout, string block, string key, float x, float y)
        {
            var entry = layout?.Find(block);
            return entry != null && entry.Values(key).Count >= 2 ? entry.Vector2(key) : new Vector2(x, y);
        }

        private static float Number(DarkestFile layout, string block, string key, float fallback)
        {
            var entry = layout?.Find(block);
            return entry != null && entry.Has(key) ? entry.Float(key, 0, fallback) : fallback;
        }
    }

    /// <summary>DD1 pictures of the map, and the one the mod has to make itself.</summary>
    internal static class QuestMapArt
    {
        private static Sprite _gradient;
        private static Color _gradientLeft, _gradientRight;
        private static int[] _goldPiles;
        // FALLBACK: DD1's own gold.icon_thresholds.
        private static readonly int[] StockGoldPiles = { 250, 500, 750, 1000 };

        /// <summary>
        /// Which of DD1's pictures of gold (panels/icons_equip/gold/inv_gold+_N.png) stands for a sum of DD1
        /// gold: the last whose threshold the sum reaches (shared/inventory/item.display.json: gold.icon_thresholds).
        /// </summary>
        public static int GoldPile(int dd1Gold)
        {
            if (_goldPiles == null)
            {
                _goldPiles = StockGoldPiles;
                try
                {
                    var text = Dd1Install.ReadText("shared/inventory/item.display.json");
                    if (text != null && JObject.Parse(text)["gold"]?["icon_thresholds"] is JArray thresholds && thresholds.Count > 0)
                    {
                        _goldPiles = new int[thresholds.Count];
                        for (var i = 0; i < thresholds.Count; i++) _goldPiles[i] = (int)thresholds[i];
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("DD1 item display table could not be read: " + e.Message); }
            }
            var pile = 0;
            for (var i = 0; i < _goldPiles.Length; i++)
                if (dd1Gold >= _goldPiles[i]) pile = i;
            return pile;
        }

        /// <summary>A DD1 picture; null (and no warning in the log) when the install has no such file.</summary>
        public static Sprite Sprite(string dd1File)
        {
            return Dd1Install.Exists(dd1File) ? Dd1Install.Sprite(dd1File) : null;
        }

        /// <summary>A left-to-right blend for the fill of a region's level bar (DD1 names its two ends in base.colours.darkest).</summary>
        public static Sprite Gradient(Color left, Color right)
        {
            if (_gradient != null && _gradientLeft == left && _gradientRight == right) return _gradient;
            const int width = 64;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "quest map bar",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[width];
            for (var x = 0; x < width; x++) pixels[x] = Color.Lerp(left, right, x / (width - 1f));
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _gradient = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, width, 1f), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _gradient.name = texture.name;
            _gradient.hideFlags = HideFlags.HideAndDontSave;
            _gradientLeft = left;
            _gradientRight = right;
            return _gradient;
        }
    }

    /// <summary>Clicks and hovers of one element of the map.</summary>
    internal class QuestMapHit : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Left, Right;
        /// <summary>True when the pointer comes onto the element, false when it leaves.</summary>
        public Action<bool> Hover;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) Right?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Left) Left?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData) => Hover?.Invoke(true);

        public void OnPointerExit(PointerEventData eventData) => Hover?.Invoke(false);
    }

    /// <summary>
    /// The box that says what the pointer is on: a region's level or lock, a quest, a hero in the tray.
    /// It is DD1's tooltip (<see cref="Dd1Tooltip"/>: its box art and its text style).
    /// </summary>
    internal class QuestMapTooltip : Dd1Tooltip
    {
        /// <param name="rightEdge">The box stays left of this (the roster column).</param>
        public QuestMapTooltip(Transform frame, float rightEdge) : base(frame, rightEdge)
        {
        }
    }
}
