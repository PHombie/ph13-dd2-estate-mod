using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Assets.Code.Actor;
using Assets.Code.Source;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using DD2Estate.Estate;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>One of the five places of the banner: a combat skill of the hero, or at a camp a camping skill.</summary>
    internal sealed class RaidAbility
    {
        public string Id;
        public Sprite Icon;
        /// <summary>What the pointer is told (TextMeshPro rich text).</summary>
        public string Tooltip;
        /// <summary>
        /// Drawn as DD1 draws a skill that cannot be used now (colours/base.colours.darkest: skill_unselectable
        /// .darkness 0.4 .saturation 0.0): its picture in grey at four tenths of its light. A camping skill the
        /// hero cannot use now; never a combat skill (outside a fight they stand in their own colours, as DD2
        /// shows them).
        /// </summary>
        public bool Dim;
        /// <summary>Drawn inside DD1's selection frame (a camping skill waiting for a companion, "move" waiting for one).</summary>
        public bool Chosen;
        /// <summary>
        /// The picture has no frame of its own (DD2's skill icons are drawings on clear ground; DD1's are framed
        /// squares): DD1's empty place is drawn under it and the picture inside that frame.
        /// </summary>
        public bool Bare;
        /// <summary>
        /// Stands in the narrow place beside the fifth, where DD1 has "pass" (panels/icons_ability/ability_pass.png,
        /// 20x72; the place is 40 wide now): "move" (<see cref="RaidMoveArt"/>).
        /// </summary>
        public bool Narrow;
        /// <summary>A place no skill is in: its frame alone.</summary>
        public bool Empty;
        public Action Click;
    }

    /// <summary>
    /// The left half of DD1's raid panel, for the hero who is selected: the banner (panels/panel_banner.png:
    /// portrait, name and class, the row of skill icons over the numbers 1 to 5 drawn into it) over the hero
    /// panel (panels/panel_hero.png: health and stress, the stats, weapon and armour, the two trinkets).
    /// Places come from scripts/layout/panel.banner.darkest, panel.hero.darkest and
    /// shared/hero/hero.layout.darkest (<see cref="RaidLayout"/>).
    ///
    /// What DD2 puts in DD1's places: the hero's five equipped skills in the places 1 to 5, in their own colours,
    /// and "move" in the narrow place beside them where DD1 has "pass" (<see cref="CombatSkills"/>); four lines
    /// of what the hero brings to a fight (<see cref="Stats"/>: DMG, CRIT, HP, SPD), in DD1's gold where a
    /// quirk, a disease or a trinket has moved one; the blacksmith's levels as DD1's own pictures of the class's weapon and armour,
    /// the level's number in DD1's colour for it; DD2's trinkets in DD1's two slots. A worn trinket takes a click
    /// (DD1 drags it): into the bag, or to change places with one of the bag (the screen decides).
    /// </summary>
    internal class RaidHeroPanel
    {
        public const float BannerHeight = 136f;             // panels/panel_banner.png (754x136)
        private const float PortraitSize = 85f;             // DD1's <class>_portrait_roster.png, the hole drawn into the banner
        // DD1's portrait is one picture: a grey rim three pixels wide around a black field, the face on it,
        // filling it (the rim is the same in every class's picture, compared pixel by pixel). DD2's portraits are
        // heads on clear ground with wide margins: DD1's rim and field are drawn for them, and the head so
        // large that it stands in the field from top to bottom (measured in the mod's own frames: drawn at 106
        // the Crusader's head is 62 px tall; the field is 79), cut off at the rim.
        private const float PortraitRim = 3f;
        private const float PortraitDrawn = 136f;
        private const string PortraitRimArt = "heroes/crusader/crusader_A/crusader_portrait_roster.png";
        private const float AbilitySize = 72f;              // DD1's <class>.ability.*.png, panels/icons_ability
        private const float AbilityInset = 3f;              // the frame of DD1's skill pictures (panels/icons_ability/ability_none.png)
        private const float AbilityFrame = 110f;            // panels/icons_ability/selected_ability.png, focused_ability.png
        public const int Abilities = 5;                     // the numbers drawn into the banner
        public const int CombatPlaces = 5;                  // a DD2 hero's five equipped skills (DD1: four, and "move" in the fifth)
        private const float StatArrow = 24f;                // shared/hero/icon_stat_buff.png, icon_stat_debuff.png

        // The mod's own numbers (panel pixels).
        // Name, class and path begin beside the portrait and are set from the left (the owner, 2026-10-06; DD1
        // sets them from the right, against the skills): the portrait's rim ends at 117.
        private const float TextLeft = 127f;
        private const float NameHeight = 40f, ClassHeight = 25f;
        // A name is as long as the player makes it (HeroRename: sixteen letters): it shrinks until it fits.
        private const float NameSmallest = 9f, ClassSmallest = 10f;
        // DD1's "pass" (panels/icons_ability/ability_pass.png) is 20 wide and ends 26 px short of the frame's
        // inner line; "move" in its place is RaidMoveArt.Width wide. The whole row (skills, "move", and the two
        // rules and the numbers 1 to 5 that DD1 has drawn into the banner for it) stands that much further left,
        // so that the row ends where DD1's does: as far from the frame at its right as from the frame under it.
        private const float PassWidth = 20f;

        /// <summary>How far left of DD1's places the row of skills stands (panel pixels).</summary>
        public static float RowShift => Mathf.Max(0f, RaidMoveArt.Width - PassWidth);

        // MEASURED on panels/panel_banner.png (754x136, opaque inside its frame): the frame's inner line stands
        // at x 54..56 and 739..740, y 17..19 and 129..131. The row's two rules run from x 304 to 721, the upper
        // at y 28..31, the lower at y 109..114; the numbers 1 to 5 stand on the lower one down to y 122, the "5"
        // ending at x 659. Nothing else is painted from x 296 to 728 between y 24 and 126.
        private const float BannerArtWidth = 754f, BannerArtHeight = 136f;
        private const float FrameInnerRight = 739f;         // (the picture's pixels; the panel's are 33 less: 706)
        private static readonly Rect RowArt = Rect.MinMaxRect(296f, 24f, 728f, 126f);
        private const float RowArtSeam = 700f;              // in the rules' plain run right of the last number
        private const float RowArtNumbersEnd = 662f;
        private const float PathGap = 21f;                  // the hero's path, a line under the class (DD1 has no paths)
        private static readonly Vector2 BarText = new Vector2(180f, 26f);
        private const float LookEvery = 2f;                 // seconds between walks through the hero's data (what has moved a stat)

        // FALLBACK: DD1's stock colours of a weapon's or an armour's level, lowest first (colours/base.colours.darkest,
        // equipment_level_0..4; the last is DD1's "harmful").
        private static readonly Color[] LevelColours =
        {
            new Color32(215, 213, 205, 255), new Color32(215, 213, 205, 255), new Color32(105, 190, 75, 255), new Color32(62, 114, 212, 255), new Color32(177, 25, 0, 255)
        };

        private readonly RaidTooltip _tooltip;
        private readonly RectTransform _root;
        private readonly Image _portrait;
        private readonly TextMeshProUGUI _name, _class, _path, _health, _stress, _weaponLevel, _armourLevel;
        private readonly Image _weapon, _armour;
        private readonly Image[] _trinkets = new Image[2];
        private readonly string[] _trinketIds = new string[2];
        private readonly string[] _trinketRarities = new string[2];
        private readonly SpineView[] _trinketSparkles = new SpineView[2];
        private readonly TextMeshProUGUI[] _statNames, _statValues;
        private readonly Image[] _statArrows;
        // per row: the arrow (1 up, -1 down, 0 none) and the colour (1 raised, -1 lowered, 0 as it is)
        private readonly int[] _statMarks, _statTints;
        private uint _looked;
        private float _lookAgain;
        private readonly RectTransform _abilities;
        // what stands in the row now, for the dev bridge
        private readonly List<Dd2Skills.View> _dd2Skills = new List<Dd2Skills.View>();
        private readonly List<RectTransform> _moveParts = new List<RectTransform>();
        private string _rowLook = "none";
        private readonly Vector2 _origin;
        private string _abilityKey;
        private uint _shown;
        private bool _portraitGrey;

        private static readonly Dictionary<string, string> GearFolders = new Dictionary<string, string>();

        private struct Stat
        {
            /// <summary>DD1's string id of the row's name (null: the word is the mod's own), and the word should the install have none.</summary>
            public string Name, Stock;
            public Func<ActorInstance, string> Value;
            // What moves the row's number, for its colour and its arrow (Mark): the stat whose add_stat lines
            // are summed; the one whose multiply_stat lines are summed as shares (0.1: a tenth more); the one
            // whose multiply_stat values are factors (1: nothing).
            public ActorStatType Adds, Shares, Factors;
        }

        /// <summary>
        /// The four lines of the panel, in the owner's order: what the hero brings to a fight over the class's own
        /// skills, read from DD2's stats the way a fight reads them.
        /// DMG: by how much every blow the hero deals is multiplied (<see cref="DamageFactor"/>), "+20%".
        /// CRIT: what the hero adds to a skill's own chance of a critical hit (<see cref="CritBonus"/>), "+5%".
        /// HP: by how much the hero's maximum health is multiplied (<see cref="HealthShare"/>), "+10%".
        /// SPD: the hero's speed now, a number.
        /// DD1's words where it has them (str_ui_DMG, str_ui_CRIT, str_ui_SPD); its word for health is "MAX HP"
        /// (str_ui_MAXHP), the owner asked for "HP".
        /// </summary>
        private static readonly Stat[] Stats =
        {
            new Stat
            {
                Name = "str_ui_DMG", Stock = "DMG", Value = a => Percent(DamageFactor(a) - 1f, true),
                Adds = ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT, Factors = ActorStatType.HEALTH_DAMAGE_DEALT_MULT_PERCENT
            },
            new Stat { Name = "str_ui_CRIT", Stock = "CRIT", Value = a => Percent(CritBonus(a), true), Adds = ActorStatType.CRIT_CHANCE },
            new Stat { Stock = "HP", Value = a => Percent(HealthShare(a), true), Shares = ActorStatType.HEALTH_MAX },
            new Stat { Name = "str_ui_SPD", Stock = "SPD", Value = a => Mathf.RoundToInt(a.GetClampedStatValue(ActorStatType.SPEED)).ToString(), Adds = ActorStatType.SPEED, Shares = ActorStatType.SPEED }
        };

        /// <summary>
        /// What a fight multiplies every blow of the hero by (SkillCalculation.CalculateResultDamage): the stat's
        /// base of 1 and every add_stat of health_damage_dealt_percent the hero carries (the class and what is
        /// hung on it: the Blacksmith's weapon, a camp's buff; quirks, trinkets, diseases), times every
        /// multiply_stat of health_damage_dealt_mult_percent. What holds only for a blow of one kind (a trinket's
        /// "melee skills", a skill's own bonus) is a buff under a condition that nothing meets outside a fight,
        /// and is not in it.
        /// </summary>
        public static float DamageFactor(ActorInstance actor)
        {
            var factor = ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT.m_BaseValue;
            foreach (var pair in actor.GetAddStatValuesBySource(ActorStatType.HEALTH_DAMAGE_DEALT_PERCENT, false)) factor += pair.Value;
            foreach (var pair in actor.GetMultiplyStatValuesBySource(ActorStatType.HEALTH_DAMAGE_DEALT_MULT_PERCENT, false)) factor *= pair.Value;
            return factor;
        }

        /// <summary>
        /// The hero's own chance of a critical hit, which a fight adds to the chance of the skill used
        /// (SkillCalculation: the performer's crit_chance while the skill's stats are counted with the hero's).
        /// </summary>
        public static float CritBonus(ActorInstance actor) => actor.GetClampedStatValue(ActorStatType.CRIT_CHANCE);

        /// <summary>
        /// The share the hero's maximum health is raised by: DD2 counts health_max as (what is added) x (1 + what
        /// it is multiplied by), and this is the second (StatInstance.GetStatTotal).
        /// </summary>
        public static float HealthShare(ActorInstance actor)
        {
            var share = 0f;
            foreach (var pair in actor.GetMultiplyStatValuesBySource(ActorStatType.HEALTH_MAX, false)) share += pair.Value;
            return share;
        }

        /// <param name="trinket">A click on a worn trinket: the hero shown and the trinket's DD2 id.</param>
        public RaidHeroPanel(RectTransform screen, RaidTooltip tooltip, Action<uint> sheet, Action<uint, string> trinket = null)
        {
            _tooltip = tooltip;
            var l = RaidLayout.Current;
            _origin = l.LeftPanel;
            _root = UiKit.Rect("HeroPanel", screen);
            _root.PlaceTopLeft(_origin, RaidUi.TopLeft, new Vector2(720f, 360f));

            var panel = new Vector2(0f, BannerHeight);
            RaidUi.Art("Panel", _root, "panels/panel_hero.png", panel, new Vector2(720f, 224f), new Color(0.03f, 0.03f, 0.035f), true);
            var banner = RaidUi.Art("Banner", _root, "panels/panel_banner.png", l.BannerBackground, new Vector2(BannerArtWidth, BannerHeight), new Color(0.05f, 0.05f, 0.055f), true);
            ShiftRowArt(banner, l);

            // ---- the banner ----
            // DD1's rim: one of its portraits under a black field, of which the rim alone is left to see
            RaidUi.Art("PortraitRim", _root, PortraitRimArt, l.BannerPortrait, new Vector2(PortraitSize, PortraitSize), new Color32(60, 60, 60, 255));
            var inside = new Vector2(PortraitSize - 2f * PortraitRim, PortraitSize - 2f * PortraitRim);
            var field = UiKit.Image("PortraitField", _root, null, Color.black);
            ((RectTransform)field.transform).PlaceTopLeft(l.BannerPortrait + new Vector2(PortraitRim, PortraitRim), RaidUi.TopLeft, inside);
            var window = UiKit.Image("Portrait", _root, null, Color.clear, true);
            ((RectTransform)window.transform).PlaceTopLeft(l.BannerPortrait + new Vector2(PortraitRim, PortraitRim), RaidUi.TopLeft, inside);
            window.gameObject.AddComponent<RectMask2D>();
            _portrait = UiKit.Image("Art", window.transform, null, Color.clear);
            _portrait.preserveAspect = true;
            ((RectTransform)_portrait.transform).Place(RaidUi.Middle, RaidUi.Middle, Vector2.zero, new Vector2(PortraitDrawn, PortraitDrawn));
            // DD1: a right click on the hero opens the character sheet
            RaidUi.Pointer(window, null, () => { if (_shown != 0u) sheet?.Invoke(_shown); });

            // name, class and path begin beside the portrait; their box ends where DD1 ends it before the skills
            // (name_layout.pos is DD1's right edge of the lines, 8 px before its first skill), moved with the row
            var width = l.BannerName.x - RowShift - TextLeft;
            _name = RaidUi.Label("Name", _root, "banner_hero_name", new Vector2(TextLeft, l.BannerName.y), new Vector2(width, NameHeight), TextAlignmentOptions.TopLeft, l.BannerNameColour);
            RaidUi.Fit(_name, NameSmallest);
            _class = RaidUi.Label("Class", _root, "banner_hero_class", new Vector2(TextLeft, l.BannerClassY), new Vector2(width, ClassHeight), TextAlignmentOptions.TopLeft, l.BannerClassColour);
            RaidUi.Fit(_class, ClassSmallest);
            _path = RaidUi.Label("Path", _root, "banner_hero_class", new Vector2(TextLeft, l.BannerClassY + PathGap), new Vector2(width, ClassHeight), TextAlignmentOptions.TopLeft, l.BannerClassColour);
            RaidUi.Fit(_path, ClassSmallest);

            _abilities = UiKit.Rect("Abilities", _root).PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, Vector2.zero);

            // ---- the hero panel ----
            // health_layout.pos and stress_layout.pos are where the numbers begin (DD1's own frame: "33 / 33" and
            // "0 / 200" both start at x 370, the panel's 130)
            _health = RaidUi.Label("Health", _root, "stat", panel + l.HeroHealth, BarText, TextAlignmentOptions.TopLeft, l.HeroHealthColour);
            _stress = RaidUi.Label("Stress", _root, "stat", panel + l.HeroStress, BarText, TextAlignmentOptions.TopLeft, l.HeroStressColour);

            _statNames = new TextMeshProUGUI[Stats.Length];
            _statValues = new TextMeshProUGUI[Stats.Length];
            _statArrows = new Image[Stats.Length];
            _statMarks = new int[Stats.Length];
            _statTints = new int[Stats.Length];
            for (var i = 0; i < Stats.Length; i++)
            {
                var at = panel + l.HeroStat + new Vector2(0f, l.StatSpacing.y * i);
                _statNames[i] = RaidUi.Label("Stat" + i, _root, "stat", at, new Vector2(l.StatValue.x, 26f), TextAlignmentOptions.TopLeft, UiKit.Neutral);
                _statNames[i].text = Stats[i].Name != null ? RaidText.Get(Stats[i].Name, Stats[i].Stock) : Stats[i].Stock;
                _statValues[i] = RaidUi.Label("Value" + i, _root, "stat", at + l.StatValue, new Vector2(l.StatSpacing.x - l.StatValue.x, 26f), TextAlignmentOptions.TopLeft, UiKit.Neutral);
                // DD1's arrow left of a row whose stat is buffed or debuffed (hero_stats_layout.icon_offset)
                _statArrows[i] = UiKit.Image("Arrow" + i, _root, null, Color.clear);
                ((RectTransform)_statArrows[i].transform).PlaceTopLeft(at + l.StatIcon, RaidUi.TopLeft, new Vector2(StatArrow, StatArrow));
                _statArrows[i].gameObject.SetActive(false);
            }

            // DD1 hangs a card's tooltip beside it, level with its top: hero_equipment_layout.tooltip_offset
            // (120 52) from the card's own position, which is 19 px right of the picture
            var gear = panel + l.HeroEquipment;
            _weapon = Slot("Weapon", gear + l.WeaponPos + l.EquipIcon, l.ItemIconSize, gear + l.WeaponPos + l.EquipTooltip, () => GearWords(Blacksmith.Gear.Weapon));
            _armour = Slot("Armour", gear + l.ArmourPos + l.EquipIcon, l.ItemIconSize, gear + l.ArmourPos + l.EquipTooltip, () => GearWords(Blacksmith.Gear.Armour));
            _weaponLevel = RaidUi.Label("WeaponLevel", _root, "equipment_level", gear + l.WeaponPos + l.EquipLevel, new Vector2(40f, 30f));
            _armourLevel = RaidUi.Label("ArmourLevel", _root, "equipment_level", gear + l.ArmourPos + l.EquipLevel, new Vector2(40f, 30f));

            for (var i = 0; i < _trinkets.Length; i++)
            {
                var index = i;
                var corner = panel + l.HeroTrinket + l.TrinketStart + new Vector2(l.TrinketOffset.x * i, 0f);
                // DD1's card of the trinket's rarity in DD1's slot, DD2's picture of the trinket on it, as the
                // Trinket Inventory draws a card (ShowTrinket). A trinket is an item: its tooltip hangs where an
                // item's does (inventory_item_tooltip_layout.offset from the cell).
                // (the release that ends a carry is not a click on the slot it began in: the UI sends that click before the carry's end)
                _trinkets[i] = Slot("Trinket" + i, corner + l.ItemIconOffset, l.ItemIconSize, corner + l.ItemTooltipOffset, () => TrinketWords(index),
                    trinket == null ? (Action)null : () => { if (_trinketCarried < 0 && _shown != 0u && _trinketIds[index] != null) trinket(_shown, _trinketIds[index]); });
                // DD1 drags a trinket between the inventory and the two slots: a worn one is picked up with the left button
                var slot = _trinkets[i];
                var carry = slot.gameObject.AddComponent<RaidCarry>();
                carry.CanBegin = () => _trinketCarried < 0 && _shown != 0u && _trinketIds[index] != null && CanCarryTrinket != null && CanCarryTrinket(index);
                carry.Began = () =>
                {
                    _trinketCarried = index;
                    _tooltip.Hide(slot);
                    TrinketCarryBegan?.Invoke(index);
                };
                carry.Moved = at => TrinketCarryMoved?.Invoke(at);
                carry.Ended = at =>
                {
                    _trinketCarried = -1;
                    TrinketCarryEnded?.Invoke(index, at);
                };
            }
        }

        // ---- the banner's rules and numbers, moved with the row ------------------------------------------

        private static Texture _rowArtOf;
        private static Sprite _rowArtMoved, _rowArtEnd;

        // DD1 has drawn the row's two rules and the numbers 1 to 5 into the banner's picture, for a row that
        // begins RowShift further right than the mod's. Two pieces of that same picture are laid over it:
        // everything from before the rules' left end to behind the "5", RowShift further left (the numbers stand
        // under their skills again, the rules begin before the first); and the rules' right end where it is, so
        // that the rules still end where DD1 ends them, behind the last place. Together they cover what the
        // picture has there, and the picture is opaque: nothing of the old numbers shows.
        private void ShiftRowArt(Image banner, RaidLayout l)
        {
            var shift = RowShift;
            var art = banner != null && banner.sprite != null ? banner.sprite.texture : null;
            // (a picture of another size is not DD1's banner as measured: it stays as it is)
            if (art == null || shift < 0.5f || art.width != (int)BannerArtWidth || art.height != (int)BannerArtHeight) return;
            if (shift > RowArtSeam - RowArtNumbersEnd)
            {
                Plugin.Log.LogWarning("Dungeon HUD: the banner's numbers stay where DD1 has them (the row is moved by more than its rules can follow)");
                return;
            }
            if (_rowArtOf != art || _rowArtMoved == null || _rowArtEnd == null)
            {
                _rowArtOf = art;
                _rowArtMoved = CutOf(art, Rect.MinMaxRect(RowArt.xMin, RowArt.yMin, RowArtSeam, RowArt.yMax), "row");
                _rowArtEnd = CutOf(art, Rect.MinMaxRect(RowArtSeam - shift, RowArt.yMin, RowArt.xMax, RowArt.yMax), "row's end");
            }
            var moved = UiKit.Image("BannerRow", _root, _rowArtMoved);
            ((RectTransform)moved.transform).PlaceTopLeft(l.BannerBackground + new Vector2(RowArt.xMin - shift, RowArt.yMin), RaidUi.TopLeft, new Vector2(RowArtSeam - RowArt.xMin, RowArt.height));
            var end = UiKit.Image("BannerRowEnd", _root, _rowArtEnd);
            ((RectTransform)end.transform).PlaceTopLeft(l.BannerBackground + new Vector2(RowArtSeam - shift, RowArt.yMin), RaidUi.TopLeft, new Vector2(RowArt.xMax - RowArtSeam + shift, RowArt.height));
        }

        // A piece of a picture, given in the picture's own pixels from its top left corner (a texture's rows count from the bottom).
        private static Sprite CutOf(Texture2D art, Rect piece, string name)
        {
            var cut = Sprite.Create(art, new Rect(piece.xMin, art.height - piece.yMax, piece.width, piece.height), new Vector2(0f, 1f), 100f, 0, SpriteMeshType.FullRect);
            cut.name = art.name + " (" + name + ")";
            cut.hideFlags = HideFlags.HideAndDontSave;
            return cut;
        }

        // ---- a worn trinket carried ------------------------------------------------------------------------

        private int _trinketCarried = -1;

        /// <summary>Whether the trinket in a slot may be picked up right now (the screen decides: not in a fight, not one DD2 does not let go of).</summary>
        public Func<int, bool> CanCarryTrinket;
        /// <summary>A worn trinket is picked up: its slot. Then where the pointer carries it (screen pixels), and its slot and where it is let go.</summary>
        public Action<int> TrinketCarryBegan;
        public Action<Vector2> TrinketCarryMoved;
        public Action<int, Vector2> TrinketCarryEnded;

        /// <summary>The trinket shown in a slot (DD2's id); null for an empty one.</summary>
        public string TrinketIn(int index) => index >= 0 && index < _trinketIds.Length ? _trinketIds[index] : null;

        /// <summary>The trinket slot of the shown hero under a point of the screen: 0 or 1; -1 for none (also for a slot the hero does not have).</summary>
        public int TrinketSlotAt(Vector2 screen)
        {
            if (_shown == 0u) return -1;
            var slots = RealmInventory.Worn(_shown).Count;
            for (var i = 0; i < _trinkets.Length && i < slots; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_trinkets[i].transform, screen, null)) return i;
            return -1;
        }

        /// <param name="tip">Where the card's tooltip begins (its top left corner), in the panel's pixels.</param>
        private Image Slot(string name, Vector2 at, Vector2 size, Vector2 tip, Func<string> words, Action click = null)
        {
            var image = UiKit.Image(name, _root, null, Color.clear, true);
            ((RectTransform)image.transform).PlaceTopLeft(at, RaidUi.TopLeft, size);
            image.preserveAspect = true;
            RaidUi.Pointer(image, click, null, inside =>
            {
                var text = inside ? words() : null;
                if (text != null) _tooltip.Show(image, text, _origin + tip, RaidLayout.Current.ItemTooltipWidth);
                else _tooltip.Hide(image);
            });
            return image;
        }

        /// <summary>True for a point of the screen on the selected hero's portrait (a card of the bag let go there is for that hero).</summary>
        public bool PortraitAt(Vector2 screen)
        {
            return _shown != 0u && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_portrait.transform.parent, screen, null);
        }

        // ---- the hero ------------------------------------------------------------------------------------

        /// <summary>Shows a hero (null: nobody is left to show). Cheap enough to call a few times a second.</summary>
        public void Show(ActorInstance actor)
        {
            var guid = actor != null ? actor.ActorGuid : 0u;
            var alive = actor != null && actor.IsLiving;
            if (guid != _shown)
            {
                _shown = guid;
                _portrait.sprite = null;
                _name.text = actor != null ? actor.ActorName : "";
                var title = actor != null ? HeroNames.Title(actor) : "";
                var comma = title.IndexOf(", ", StringComparison.Ordinal);
                _class.text = comma >= 0 ? title.Substring(0, comma) : title;
                _path.text = comma >= 0 ? title.Substring(comma + 2) : "";
                MatchLines();
            }
            if (actor == null)
            {
                _portrait.color = Color.clear;
                _health.text = _stress.text = _weaponLevel.text = _armourLevel.text = "";
                foreach (var value in _statValues) value.text = "";
                foreach (var arrow in _statArrows) arrow.gameObject.SetActive(false);
                _weapon.color = _armour.color = Color.clear;
                for (var i = 0; i < _trinkets.Length; i++) ShowTrinket(i, null);
                return;
            }

            // The game drops portrait art it thinks nobody uses (a fight does it): fetch it again when it is gone.
            if (_portrait.sprite == null || _portraitGrey == alive)
            {
                _portraitGrey = !alive;
                _portrait.sprite = HeroNames.Portrait(actor.ActorDataId, !alive);
            }
            _portrait.color = _portrait.sprite == null ? Color.clear : alive ? Color.white : new Color(0.45f, 0.45f, 0.45f);

            // DD1 writes both with a space on either side of the stroke: "33 / 33", "0 / 200"
            var health = (alive ? Mathf.RoundToInt(actor.HpRounded) : 0) + " / " + Mathf.RoundToInt(Mathf.Max(1f, actor.CurrentHpMax));
            if (_health.text != health) _health.text = health;
            var stress = Mathf.RoundToInt(actor.Stress) + " / " + Mathf.RoundToInt(Mathf.Max(1f, actor.StressMax));
            if (_stress.text != stress) _stress.text = stress;
            Look(actor);
            for (var i = 0; i < Stats.Length; i++)
            {
                string value;
                try { value = Stats[i].Value(actor); }
                catch { value = "-"; }
                if (_statValues[i].text != value) _statValues[i].text = value;
                // DD1 writes a row whose number is not the class's own in gold, name and number (its own frame:
                // DMG 7-14 and DODGE 9 of a Crusader fresh from the hamlet, colour "notable"). GUESS: a row made
                // worse is written in DD1's "harmful"; no frame shows one.
                var colour = _statTints[i] > 0 ? UiKit.Notable : _statTints[i] < 0 ? UiKit.Harmful : UiKit.Neutral;
                if (_statNames[i].color != colour) _statNames[i].color = colour;
                if (_statValues[i].color != colour) _statValues[i].color = colour;
                var mark = _statMarks[i];
                var arrow = _statArrows[i];
                var sprite = mark == 0 ? null : RaidUi.Sprite(mark > 0 ? "shared/hero/icon_stat_buff.png" : "shared/hero/icon_stat_debuff.png");
                if (arrow.gameObject.activeSelf != (sprite != null)) arrow.gameObject.SetActive(sprite != null);
                if (sprite == null) continue;
                if (arrow.sprite != sprite) arrow.sprite = sprite;
                arrow.color = Color.white;
            }

            ShowGear(_weapon, _weaponLevel, actor, Blacksmith.Gear.Weapon);
            ShowGear(_armour, _armourLevel, actor, Blacksmith.Gear.Armour);

            // slot by slot, as the hero's sheet shows them (a trinket in the second slot stands in the second
            // place also while the first is empty: it can be carried from one to the other)
            for (var i = 0; i < _trinkets.Length; i++)
            {
                string worn = null;
                try
                {
                    var item = actor.GetTrinketInventory()?.GetItemOrDefault(i);
                    if (Assets.Code.Utils.ItemUtils.IsValid(item)) worn = item.GetItemDefinition().m_id;
                }
                catch { /* a hero without a trinket inventory shows two empty slots */ }
                ShowTrinket(i, worn);
            }
        }

        // Class and path are two lines of one block: where one of them has to shrink to fit ("Plague Doctor" is
        // wider than the box at the full size), both are set in the size that one fits at.
        private void MatchLines()
        {
            try
            {
                // each line finds the size it fits at ...
                _class.enableAutoSizing = _path.enableAutoSizing = true;
                _class.overflowMode = _path.overflowMode = TextOverflowModes.Truncate;
                _class.ForceMeshUpdate(true);
                _path.ForceMeshUpdate(true);
                var size = Mathf.Min(_class.fontSize, _path.fontSize);
                if (size < ClassSmallest - 0.01f || size > _class.fontSizeMax + 0.01f) return;
                // ... and both take the smaller one (set by hand, a line is never cut short: it would rather run on)
                _class.enableAutoSizing = _path.enableAutoSizing = false;
                _class.overflowMode = _path.overflowMode = TextOverflowModes.Overflow;
                _class.fontSize = _path.fontSize = size;
            }
            catch (Exception)
            {
                // (each line then fits itself, as before)
                _class.enableAutoSizing = _path.enableAutoSizing = true;
                _class.overflowMode = _path.overflowMode = TextOverflowModes.Truncate;
            }
        }

        // A worn trinket as the Trinket Inventory shows one: DD1's card of the rarity it came under
        // (panels/icons_equip/trinket/rarity_*.png), DD2's picture of it on the card (one size for all, cut off
        // at the card's edge: TrinketPicture; DD2 loads the picture on demand, the card is bare until it is
        // there) and DD1's sparkle round an ancestral trinket and a trophy. An empty slot is the panel's own.
        private void ShowTrinket(int index, string id)
        {
            _trinketIds[index] = id;
            var card = _trinkets[index];
            var rarity = id != null ? Trinkets.Grade(id) : null;
            var art = id != null ? RaidUi.Sprite(Trinkets.RarityArt(rarity)) : null;
            if (card.sprite != art) card.sprite = art;
            card.color = id == null ? Color.clear : art != null ? Color.white : new Color(0.1f, 0.09f, 0.08f, 0.95f);
            card.raycastTarget = id != null;
            TrinketPicture.Show(card, id);
            if (rarity == _trinketRarities[index]) return;
            _trinketRarities[index] = rarity;
            if (_trinketSparkles[index] != null) UnityEngine.Object.Destroy(_trinketSparkles[index].gameObject);
            _trinketSparkles[index] = TrinketPicture.Sparkle(card.transform, rarity);
        }

        private void ShowGear(Image image, TextMeshProUGUI level, ActorInstance actor, Blacksmith.Gear gear)
        {
            var rank = Mathf.Max(1, Blacksmith.Level(actor.ActorGuid, gear));
            var sprite = RaidUi.Sprite(GearArt(Survivalist.ClassOf(actor), gear == Blacksmith.Gear.Weapon ? "weapon" : "armour", rank));
            if (image.sprite != sprite) image.sprite = sprite;
            image.color = sprite != null ? Color.white : Color.clear;
            var text = rank.ToString();
            if (level.text != text) level.text = text;
            // DD1 colours the number by the level, as it numbers the level's picture: equipment_level_0 for the first
            var step = Mathf.Clamp(rank - 1, 0, LevelColours.Length - 1);
            var colour = Dd1Fonts.Colour("equipment_level_" + step, LevelColours[step]);
            if (level.color != colour) level.color = colour;
        }

        // ---- the stats that take a walk through the hero's data ------------------------------------------

        // Every couple of seconds, and when another hero is shown: which rows the hero's quirks, trinkets and
        // buffs have moved.
        private void Look(ActorInstance actor)
        {
            if (actor.ActorGuid == _looked && Time.unscaledTime < _lookAgain) return;
            _looked = actor.ActorGuid;
            _lookAgain = Time.unscaledTime + LookEvery;
            for (var i = 0; i < Stats.Length; i++)
            {
                try { Mark(actor, Stats[i], out _statTints[i], out _statMarks[i]); }
                catch { _statTints[i] = _statMarks[i] = 0; }
            }
        }

        // What has moved a row's number away from the class's own, in DD1's two ways of showing it.
        // The colour (tint: 1 better, -1 worse): what the hero carries for good, quirks, diseases, trinkets. DD1's
        // own frame of a hero fresh from the hamlet has such rows in gold and no arrow beside them.
        // The arrow (shared/hero/icon_stat_buff.png, icon_stat_debuff.png at hero_stats_layout.icon_offset;
        // arrow: 1 up, -1 down): GUESS, a buff that runs out, which outside a fight is what the camp gave (the
        // mod hangs that on the hero as a class's stats, beside the Blacksmith's gear, so it is read back from
        // the camp's own record). No frame of DD1's shows an arrow; its two colours "buff" and "debuff" are the
        // arrows' own.
        private static void Mark(ActorInstance actor, Stat stat, out int tint, out int arrow)
        {
            tint = arrow = 0;
            var kept = 0f;
            if (stat.Adds != null)
                foreach (var pair in actor.GetAddStatValuesBySource(stat.Adds, false))
                    if (Kept(pair.Key)) kept += pair.Value;
            if (stat.Shares != null)
                foreach (var pair in actor.GetMultiplyStatValuesBySource(stat.Shares, false))
                    if (Kept(pair.Key)) kept += pair.Value;
            if (stat.Factors != null)
                foreach (var pair in actor.GetMultiplyStatValuesBySource(stat.Factors, false))
                    if (Kept(pair.Key)) kept += pair.Value - 1f;
            var camp = 0f;
            string adds = stat.Adds?.GetName(), shares = stat.Shares?.GetName(), factors = stat.Factors?.GetName();
            foreach (var line in CampBuffs.TextOn(actor.ActorGuid).Split('\n'))
            {
                // "add_stat,crit_chance,0.05", "multiply_stat,health_max,0.1"
                var cells = line.Split(',');
                if (cells.Length != 3 || !float.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
                if (cells[0] == "add_stat" && cells[1] == adds) camp += value;
                else if (cells[0] == "multiply_stat" && cells[1] == shares) camp += value;
                else if (cells[0] == "multiply_stat" && cells[1] == factors) camp += value - 1f;
            }
            if (Mathf.Abs(kept) >= 0.0005f) tint = kept > 0f ? 1 : -1;
            if (Mathf.Abs(camp) >= 0.0005f) arrow = camp > 0f ? 1 : -1;
        }

        private static bool Kept(SourceType source)
        {
            return source == SourceType.QUIRK || source == SourceType.DISEASE || source == SourceType.CURSE || source == SourceType.TRINKET;
        }

        /// <summary>Dev bridge: the stat lines as they stand on the panel (tint and arrow: 1 better, -1 worse, 0 none).</summary>
        public object DescribeStats()
        {
            var rows = new List<object>();
            for (var i = 0; i < Stats.Length; i++)
            {
                var at = ((RectTransform)_statNames[i].transform).anchoredPosition;
                rows.Add(new { name = _statNames[i].text, value = _statValues[i].text, tint = _statTints[i], arrow = _statMarks[i], x = _origin.x + at.x, y = _origin.y - at.y });
            }
            return new { hero = _shown, name = _name.text, health = _health.text, rows };
        }

        private string GearWords(Blacksmith.Gear gear)
        {
            if (_shown == 0u) return null;
            var rank = Mathf.Max(1, Blacksmith.Level(_shown, gear));
            // the piece by DD1's own name for the class's gear at that level, as the Blacksmith names it
            return RaidTooltip.Titled(Blacksmith.PieceName(UpgradeUi.Hero(_shown), gear, rank), Blacksmith.Effect(gear, rank));
        }

        private string TrinketWords(int index)
        {
            var id = _trinketIds[index];
            if (id == null) return null;
            var words = Trinkets.Description(id);
            // what a click does, in the colour DD1 gives its own word on how to handle an item
            if (TrinketHint != null)
                words += (words.Length > 0 ? "\n" : "") + "<color=" + RaidText.Hex(Dd1Fonts.Colour("inventory_shift_click_remove_item", UiKit.Harmful)) + ">" + TrinketHint + "</color>";
            return RaidTooltip.Titled(Trinkets.Name(id), words);
        }

        /// <summary>What a click on a worn trinket does right now, for its tooltip; null: nothing to say.</summary>
        public string TrinketHint;

        // DD1 keeps a class's gear art in heroes/<class>/icons_equip, a DLC class's under that DLC's folder:
        // eqp_weapon_0..4.png for the blacksmith's five levels.
        private static string GearArt(string cls, string kind, int level)
        {
            if (string.IsNullOrEmpty(cls) || !Dd1Install.Found) return null;
            if (!GearFolders.TryGetValue(cls, out var folder))
            {
                folder = FindGearFolder(cls);
                GearFolders[cls] = folder;
            }
            return folder == null ? null : folder + "/eqp_" + kind + "_" + (level - 1) + ".png";
        }

        private static string FindGearFolder(string cls)
        {
            var tail = "heroes/" + cls + "/icons_equip";
            if (Directory.Exists(Dd1Install.PathOf(tail))) return tail;
            try
            {
                var dlc = Dd1Install.PathOf("dlc");
                if (dlc == null || !Directory.Exists(dlc)) return null;
                foreach (var pack in Directory.GetDirectories(dlc))
                {
                    var name = "dlc/" + Path.GetFileName(pack) + "/";
                    if (Directory.Exists(Path.Combine(pack, tail))) return name + tail;
                    var features = Path.Combine(pack, "features");
                    if (!Directory.Exists(features)) continue;
                    foreach (var feature in Directory.GetDirectories(features))
                        if (Directory.Exists(Path.Combine(feature, tail))) return name + "features/" + Path.GetFileName(feature) + "/" + tail;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeon HUD: DD1's gear art of " + cls + " was not found: " + e.Message); }
            return null;
        }

        private static string Percent(float share, bool signed)
        {
            var value = Mathf.RoundToInt(share * 100f);
            return (signed && value >= 0 ? "+" : "") + value + "%";
        }

        // ---- the row of skills ---------------------------------------------------------------------------

        private const string AbilityArt = "panels/icons_ability/";

        /// <summary>
        /// The banner's row outside a fight: the hero's five equipped combat skills in the places 1 to 5, drawn
        /// as a fight draws a skill that cannot be used now (<see cref="Dd2Skills.GreyOutsideAFight"/>; DD1 has
        /// four there, dark until a fight, and "move" in the fifth);
        /// "move" in the narrow place beside them, where DD1 has "pass": the one skill there is a use for in a
        /// hallway (chosen, a companion clicked gives up their place to the hero). DD1's "pass" has gone: outside
        /// a fight there is nothing to pass.
        /// </summary>
        public static List<RaidAbility> CombatSkills(ActorInstance actor, bool moving = false, Action move = null)
        {
            var list = new List<RaidAbility>();
            if (actor == null) return list;
            IReadOnlyList<string> equipped = null;
            try { equipped = actor.GetEquippedCharacterSheetCombatSkillIds(); }
            catch { /* a hero whose skills cannot be asked for shows five empty places */ }
            Dictionary<string, Guild.Skill> known = null;
            if (equipped != null)
                foreach (var id in equipped)
                {
                    if (list.Count >= CombatPlaces) break;
                    if (known == null)
                    {
                        known = new Dictionary<string, Guild.Skill>();
                        foreach (var skill in Guild.SkillsOf(actor)) known[skill.Id] = skill;
                    }
                    known.TryGetValue(id, out var info);
                    var name = info != null ? info.Name : HeroNames.ClassName(id);
                    list.Add(new RaidAbility
                    {
                        Id = id,
                        Icon = Guild.Icon(id),
                        Tooltip = info != null && info.Mastered ? RaidTooltip.Titled(name, "Mastered") : RaidTooltip.Titled(name, null),
                        Bare = true,
                        Dim = Dd2Skills.GreyOutsideAFight
                    });
                }
            // DD1's picture of a place no skill is in
            while (list.Count < CombatPlaces) list.Add(new RaidAbility { Id = "none" + list.Count, Icon = RaidUi.Sprite(AbilityArt + "ability_none.png"), Empty = true });
            list.Add(new RaidAbility
            {
                Id = "move",
                // every class's "move" has the one name in DD1's tables (combat_skill_name_<class>_move)
                Tooltip = RaidTooltip.Titled(RaidText.Get("combat_skill_name_crusader_move", "Move"), null),
                Chosen = moving,
                Narrow = true,
                Click = move
            });
            return list;
        }

        /// <summary>
        /// Puts skills in the banner's places; rebuilt only when what they show has changed. In DD2's look
        /// (<see cref="Dd2Skills"/>) every place is a copy of the fight's own skill button; in DD1's (the
        /// fallback) DD1's grey ability frame with the picture inside it.
        /// </summary>
        public void ShowAbilities(IReadOnlyList<RaidAbility> abilities)
        {
            var dd2 = Dd2Skills.Enabled && Dd2Skills.Ready;
            var hero = _shown != 0u ? SingletonMonoBehaviour<Assets.Code.Library.Library<uint, ActorInstance>>.Instance.GetLibraryElement(_shown) : null;
            // the ground DD2 draws under the skills of the hero's class
            var ground = dd2 ? Dd2Skills.GroundOf(hero) : null;
            var arrow = RaidMoveArt.Arrow;
            var key = new StringBuilder();
            key.Append(dd2 ? "dd2" : "dd1").Append(ground != null ? ground.name : "-").Append(RaidMoveArt.ArrowSource).Append(RowVersion).Append('|');
            foreach (var a in abilities)
                key.Append(a.Id).Append(a.Icon != null ? '+' : '-').Append(a.Dim ? 'd' : 'l').Append(a.Chosen ? 'c' : '.').Append(a.Bare ? 'b' : '.').Append(a.Narrow ? 'n' : '.').Append(a.Empty ? 'e' : '.').Append(a.Tooltip).Append('|');
            if (key.ToString() == _abilityKey) return;
            _abilityKey = key.ToString();
            _tooltip.Hide(_abilities);
            RaidUi.Clear(_abilities);
            _dd2Skills.Clear();
            _moveParts.Clear();
            _rowLook = dd2 ? "dd2" : "dd1";

            var l = RaidLayout.Current;
            var place = 0;
            for (var i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                if (ability.Narrow)
                {
                    ShowMove(ability, l, dd2 ? ground : null, arrow);
                    continue;
                }
                if (place >= Abilities) continue;
                var at = l.BannerAbility + new Vector2(l.BannerAbilitySpacing * place - RowShift, 0f);
                place++;
                if (!dd2 || !ShowDd2(ability, i, at, l, ground)) ShowDd1(ability, i, at);
            }
        }

        /// <summary>Dev bridge: bumped when a number of the row's look was changed, so that the row is built anew.</summary>
        public static int RowVersion;

        // What the pointer is told on a place, above it.
        private void Tell(bool inside, string tip, Vector2 at, float width)
        {
            if (inside && !string.IsNullOrEmpty(tip)) _tooltip.Show(_abilities, tip, _origin + at + new Vector2(width * 0.5f, -6f), 320f, above: true, centred: true);
            else _tooltip.Hide(_abilities);
        }

        // ---- DD2's look: the fight's own skill button ----

        private bool ShowDd2(RaidAbility ability, int index, Vector2 at, RaidLayout l, Sprite ground)
        {
            // the button's box is the row's step wide, its middle the middle of DD1's place: a fight's own proportions
            var view = Dd2Skills.Create(_abilities, l.BannerAbilitySpacing);
            if (view == null) return false;
            view.Root.name = "Skill" + index;
            view.Root.anchoredPosition = new Vector2(at.x + AbilitySize * 0.5f, -(at.y + AbilitySize * 0.5f));
            view.Ground(ground);
            if (ability.Empty || ability.Icon == null) view.Icon(null);
            else if (ability.Bare) view.Icon(ability.Icon);
            // a picture with DD1's frame drawn into it (a camping skill): inside DD2's frame, without its own
            else view.FramedIcon(ability.Icon, AbilityInset / AbilitySize);
            view.Dim = ability.Dim;
            view.Chosen = ability.Chosen;
            var tip = ability.Tooltip;
            if (view.Area != null)
                RaidUi.Pointer(view.Area, ability.Click, null, inside =>
                {
                    view.Pointed = inside;
                    Tell(inside, tip, at, AbilitySize);
                });
            _dd2Skills.Add(view);
            return true;
        }

        // ---- DD1's look: its grey frame, the picture inside ----

        private void ShowDd1(RaidAbility ability, int index, Vector2 at)
        {
            // DD1 (colours/base.colours.darkest, skill_unselectable): what is left of a dark skill's colour and light
            var saturation = RaidUi.ColourNumber("skill_unselectable", "saturation", 0f);
            var darkness = RaidUi.ColourNumber("skill_unselectable", "darkness", 0.4f);
            var dark = new Color(darkness, darkness, darkness);
            var size = new Vector2(AbilitySize, AbilitySize);

            Image ground = null;
            if (ability.Bare && ability.Icon != null)
            {
                ground = RaidUi.Art("Place" + index, _abilities, AbilityArt + "ability_none.png", at, size, null, true);
                if (ground.sprite == null) ground.raycastTarget = false;
            }
            var picture = ability.Icon;
            if (ability.Dim && picture != null) picture = RaidUi.Desaturated(picture, saturation) ?? picture;
            var icon = UiKit.Image("Skill" + index, _abilities, picture, picture == null ? new Color(0.12f, 0.12f, 0.12f) : ability.Dim ? dark : Color.white, true);
            var inset = ground != null && ground.sprite != null ? AbilityInset : 0f;
            ((RectTransform)icon.transform).PlaceTopLeft(at + new Vector2(inset, inset), RaidUi.TopLeft, size - new Vector2(2f * inset, 2f * inset));

            // DD1's frames are 110 px pictures around a 72 px icon
            var frameAt = at + size * 0.5f - new Vector2(AbilityFrame * 0.5f, AbilityFrame * 0.5f);
            if (ability.Chosen) RaidUi.Art("Chosen" + index, _abilities, AbilityArt + "selected_ability.png", frameAt, new Vector2(AbilityFrame, AbilityFrame));
            var focus = RaidUi.Art("Focus" + index, _abilities, AbilityArt + "focused_ability.png", frameAt, new Vector2(AbilityFrame, AbilityFrame));
            focus.gameObject.SetActive(false);

            var tip = ability.Tooltip;
            Action<bool> hover = inside =>
            {
                focus.gameObject.SetActive(inside);
                Tell(inside, tip, at, size.x);
            };
            RaidUi.Pointer(icon, ability.Click, null, hover);
            // the frame around a bare picture is part of the place: the pointer is told the same there
            if (ground != null && ground.sprite != null) RaidUi.Pointer(ground, ability.Click, null, hover);
        }

        // ---- "move": the narrow place beside the fifth skill ----

        /// <summary>
        /// How far the narrow place stands from where a sixth skill would begin (DD1's "pass" begins there, the
        /// whole row being <see cref="RowShift"/> left of DD1's), in the banner's pixels; the dev bridge can move it.
        /// </summary>
        public static float MoveShift = 0f;

        // The place is RaidMoveArt.Width wide and as high as a skill's frame. In DD2's look its ground and frame
        // are those of the skills beside it: the class's ground, of which the left and the right half-width
        // stand side by side, so that the frame line runs round the narrow place as it runs round a skill; the
        // marks of the chosen and the pointed-at place are the fight's own, brought to the narrow frame's
        // corners. In DD1's look it is DD1's empty place, narrowed, inside DD1's frames. The two arrows are the
        // same in both: one shape, painted (RaidMoveArt.Arrow).
        private void ShowMove(RaidAbility ability, RaidLayout l, Sprite ground, Sprite arrow)
        {
            var left = l.BannerAbility.x + l.BannerAbilitySpacing * Abilities + MoveShift - RowShift;
            var top = l.BannerAbility.y;
            var middle = new Vector2(left + RaidMoveArt.Width * 0.5f, top + AbilitySize * 0.5f);
            var at = new Vector2(left, top);
            GameObject[] pointed;
            Rect hot;

            if (ground != null)
            {
                var side = l.BannerAbilitySpacing;                      // the box of the skills' buttons
                var scale = side / Dd2Skills.Box;
                var inset = Dd2Skills.GroundInset(ground) * side;       // from the box's edge to the black ground
                var line = Dd2Skills.FrameInset(ground) * side;         // ... to the frame line
                var boxTop = middle.y - side * 0.5f;
                // the frame line stands on the place's edges; the ground reaches as far beyond it as a skill's does
                _moveParts.Add(Dd2Skills.Halves("MoveGround", _abilities, ground, Color.white, new Vector2(left - (line - inset), boxTop), new Vector2(side, side),
                    RaidMoveArt.Width + 2f * (line - inset), inset));
                if (ability.Chosen && Dd2Skills.SelectedSprite != null)
                {
                    // the two gold corners under a chosen skill, as far outside the frame line as the fight has them
                    var sprite = Dd2Skills.SelectedSprite;
                    var fit = Mathf.Min(Dd2Skills.SelectedSize.x / sprite.rect.width, Dd2Skills.SelectedSize.y / sprite.rect.height) * scale;
                    var drawn = new Vector2(sprite.rect.width * fit, sprite.rect.height * fit);
                    var beyond = (side - drawn.x) * 0.5f;               // the picture is as wide as the box, or a little less
                    _moveParts.Add(Dd2Skills.Halves("MoveChosen", _abilities, sprite, Dd2Skills.SelectedColour,
                        new Vector2(left - line + beyond, boxTop + side - Dd2Skills.SelectedRise * scale - drawn.y * 0.5f), drawn, RaidMoveArt.Width + 2f * (line - beyond), 0f));
                }
                var half = side * 0.5f - line;
                var first = Dd2Skills.Corner("MovePointed0", _abilities, new Vector2(left, boxTop + line), false, half, scale);
                var second = Dd2Skills.Corner("MovePointed1", _abilities, new Vector2(left + RaidMoveArt.Width, boxTop + side - line), true, half, scale);
                first.gameObject.SetActive(false);
                second.gameObject.SetActive(false);
                pointed = Dd2Skills.HoverSprite != null ? new[] { first.gameObject, second.gameObject } : new GameObject[0];
                hot = new Rect(left, boxTop + line, RaidMoveArt.Width, side - 2f * line);
            }
            else
            {
                var size = new Vector2(RaidMoveArt.Width, RaidMoveArt.Height);
                var place = UiKit.Image("MoveGround", _abilities, RaidMoveArt.Dd1Ground, RaidMoveArt.Dd1Ground != null ? Color.white : new Color(0.12f, 0.12f, 0.12f));
                ((RectTransform)place.transform).PlaceTopLeft(at, RaidUi.TopLeft, size);
                _moveParts.Add((RectTransform)place.transform);
                var frame = new Vector2(RaidMoveArt.FrameWidth, RaidMoveArt.FrameHeight);
                if (ability.Chosen && RaidMoveArt.Dd1Chosen != null)
                {
                    var chosen = UiKit.Image("MoveChosen", _abilities, RaidMoveArt.Dd1Chosen, Color.white);
                    ((RectTransform)chosen.transform).PlaceTopLeft(middle - frame * 0.5f, RaidUi.TopLeft, frame);
                    _moveParts.Add((RectTransform)chosen.transform);
                }
                var focus = UiKit.Image("MovePointed0", _abilities, RaidMoveArt.Dd1Focused, Color.white);
                focus.enabled = focus.sprite != null;
                ((RectTransform)focus.transform).PlaceTopLeft(middle - frame * 0.5f, RaidUi.TopLeft, frame);
                focus.gameObject.SetActive(false);
                pointed = new[] { focus.gameObject };
                hot = new Rect(left, top, size.x, size.y);
            }

            // back and forth: the one shape, once turned round, each at the size it had in the place of 20
            var colour = ability.Chosen ? RaidMoveArt.ArrowChosen : RaidMoveArt.ArrowColour;
            for (var n = 0; n < 2 && arrow != null; n++)
            {
                var sign = UiKit.Image(n == 0 ? "MoveBack" : "MoveForth", _abilities, arrow, colour);
                sign.preserveAspect = true;
                var centre = middle + new Vector2(0f, (n == 0 ? -1f : 1f) * (RaidMoveArt.ArrowGap + RaidMoveArt.ArrowSize) * 0.5f);
                var rect = (RectTransform)sign.transform;
                rect.Place(RaidUi.TopLeft, RaidUi.Middle, new Vector2(centre.x, -centre.y), new Vector2(RaidMoveArt.ArrowSize, RaidMoveArt.ArrowSize));
                if (n == 0) rect.localScale = new Vector3(-1f, 1f, 1f);
                _moveParts.Add(rect);
            }

            // what takes the pointer: the place inside its frame line, over everything of it
            var area = UiKit.Image("MoveArea", _abilities, null, Color.clear, true);
            ((RectTransform)area.transform).PlaceTopLeft(hot.position, RaidUi.TopLeft, hot.size);
            var tip = ability.Tooltip;
            RaidUi.Pointer(area, ability.Click, null, inside =>
            {
                foreach (var mark in pointed)
                    if (mark != null) mark.SetActive(inside);
                Tell(inside, tip, at, RaidMoveArt.Width);
            });
        }

        /// <summary>Dev bridge: what stands in the row and where (boxes on the display: left, right, middle, top, bottom).</summary>
        public object DescribeRow()
        {
            var skills = new List<object>();
            foreach (var view in _dd2Skills)
                if (view != null && view.Root != null) skills.Add(view.Describe());
            var move = new List<object>();
            foreach (var part in _moveParts)
                if (part != null) move.Add(new { name = part.name, shown = part.gameObject.activeInHierarchy, box = DungeonLookDev.Box(part) });
            // the banner's own pixels: where the lines of text and the row begin and end, and the frame's inner line
            var l = RaidLayout.Current;
            var lines = new List<object>();
            foreach (var label in new[] { _name, _class, _path })
            {
                var rect = (RectTransform)label.transform;
                label.ForceMeshUpdate();
                var letters = label.textBounds;
                lines.Add(new
                {
                    name = label.name, text = label.text, size = label.fontSize, largest = label.fontSizeMax, cut = label.isTextTruncated,
                    left = rect.anchoredPosition.x, right = rect.anchoredPosition.x + rect.rect.width,
                    lettersLeft = rect.anchoredPosition.x + letters.min.x, lettersRight = rect.anchoredPosition.x + letters.max.x,
                    box = DungeonLookDev.Box(rect)
                });
            }
            var rowLeft = l.BannerAbility.x - RowShift;
            var rowRight = l.BannerAbility.x + l.BannerAbilitySpacing * Abilities + MoveShift - RowShift + RaidMoveArt.Width;
            var frameRight = l.BannerBackground.x + FrameInnerRight;
            var rowArt = new List<object>();
            foreach (var name in new[] { "BannerRow", "BannerRowEnd" })
                if (_root.Find(name) is RectTransform piece) rowArt.Add(new { name, left = piece.anchoredPosition.x, right = piece.anchoredPosition.x + piece.rect.width, box = DungeonLookDev.Box(piece) });
            return new
            {
                look = _rowLook, wanted = Dd2Skills.Enabled ? "dd2" : "dd1", dd2 = Dd2Skills.Status, hero = _shown,
                arrow = RaidMoveArt.ArrowSource, arrowColour = "#" + ColorUtility.ToHtmlStringRGBA(RaidMoveArt.ArrowColour), arrowChosen = "#" + ColorUtility.ToHtmlStringRGBA(RaidMoveArt.ArrowChosen),
                moveWidth = RaidMoveArt.Width, moveShift = MoveShift, rowShift = RowShift,
                banner = new { portraitRight = l.BannerPortrait.x + PortraitSize, textLeft = TextLeft, rowLeft, rowRight, frameRight, rightGap = frameRight - rowRight, lines, rowArt },
                skills, move
            };
        }

        /// <summary>Forgets what the skill row shows: the next <see cref="ShowAbilities"/> builds it anew.</summary>
        public void Invalidate() => _abilityKey = null;
    }
}
