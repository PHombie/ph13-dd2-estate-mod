using System;
using DD2Estate.Dd1;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.UI
{
    /// <summary>
    /// DD1's name of a town screen (shared/name/name.layout.darkest, name_layout): the screen's 113 px icon, the
    /// screen's name right of it in the town_name style and, on a screen that has more to show, the round "+" on
    /// the icon's lower left (shared/progression: info_icon_frame, more_info_icon, less_info_icon) inside its
    /// breathing ring (fx/building_upgrade_pulse). A screen's own layout file gives the widget one point, its
    /// name_pos, and name_layout hangs everything from that point:
    ///
    ///   icon_offset      60  26   the icon's corner
    ///   text_offset     192 118   where the name begins, and the foot of its line
    ///   sub_icon_offset  56 106   the middle of the "+", counted from the ICON's corner (sub_icon_pulse_offset,
    ///                             the same, is its ring's)
    ///   alert_offset    115  90   the middle of the red exclamation mark (fx/estate_exclamation)
    ///
    /// No file says what point of a picture an offset is: the four are read off DD1's own screens (1920x1080
    /// frames of the Stage Coach, the Tavern, the Abbey, the Sanitarium, the Guild, the Blacksmith, the
    /// Survivalist, the Estate Map, the Provision screen, the town crier's notice, the Glossary and the Activity
    /// Log, on which DD1's own pictures were looked for). The icon lies with its corner at name_pos +
    /// icon_offset on every one of them, to the pixel. The name's line cell begins at text_offset.x and ends
    /// on text_offset.y: its foot, 63 px (the font's line) under its top. The "+" (more_info_icon.png, 39 px)
    /// has its corner at 200, 238 on all seven buildings, its middle at 220, 258: name_pos 104 126 + icon_offset
    /// + sub_icon_offset, the icon's bottom middle; open, DD1's cross stands in the same place. The exclamation
    /// mark's middle is at 219, 217: name_pos + alert_offset, on the icon over the "+".
    ///
    /// One call for any screen: the parent is the rect the screen's layout counts in (its top left corner is
    /// the layout's 0 0, y down) and <c>namePos</c> the screen's name_pos in those pixels.
    ///
    ///     var name = Dd1ScreenName.Add(frame, namePos, "campaign/town/provision/provision.icon.png", "Provision");
    ///     var name = Dd1ScreenName.Add(frame, namePos, icon, "Tavern", () => pane.SetOpen(!pane.IsOpen));    // with the "+"
    ///     name.SetOpen(true);                    // the "+" shows DD1's cross while what it opened is open
    ///     name.More.SetAsLastSibling();          // the "+" is a sibling of the name: lift it over what was laid on top
    ///
    /// Icon and name are drawn where the call stands among the parent's children; the "+" right above them.
    /// </summary>
    internal class Dd1ScreenName
    {
        public const string LayoutFile = "shared/name/name.layout.darkest";
        private const string Block = "name_layout";
        private const string ProgressionDir = "shared/progression/";
        private const string Style = "town_name";
        private const float MoreFrame = 59f;        // info_icon_frame.png
        private const float MoreIcon = 39f;         // more_info_icon.png, less_info_icon.png
        // FALLBACK: the line of DD1's dwarven_axe_large (fonts/dwarvenaxe-l.fnt: lineHeight), should the font not be had.
        private const float StockLine = 63f;
        /// <summary>The room a name has unless the screen says otherwise: DD1's longest ("Ancestor's Memoirs") is 304 px.</summary>
        public const float NameWidth = 320f;

        private readonly Vector2 _alertAt;
        private Image _moreIcon;
        private SpineView _alert;

        /// <summary>Icon and name: a rect over the whole parent, in whose pixels they stand.</summary>
        public RectTransform Root { get; private set; }

        public Image Icon { get; private set; }

        public TextMeshProUGUI Label { get; private set; }

        /// <summary>The "+" with its ring, a sibling of <see cref="Root"/>; null on a screen without one.</summary>
        public RectTransform More { get; private set; }

        private Dd1ScreenName(Vector2 alertAt)
        {
            _alertAt = alertAt;
        }

        /// <summary>
        /// Where the name of a screen begins (x in the pixels its name_pos is in): for a screen that has to
        /// tell the widget how much room the name has before something else begins.
        /// </summary>
        public static float NameLeft(Vector2 namePos)
        {
            return namePos.x + Dd1Ui.Offset(Dd1Ui.Layout(LayoutFile), Block, "text_offset", 192f, 118f).x;
        }

        /// <param name="parent">The rect the screen's layout file counts in.</param>
        /// <param name="namePos">The screen's name_pos in the parent's pixels (y down).</param>
        /// <param name="iconArt">DD1 file of the screen's icon, e.g. campaign/town/buildings/tavern/tavern.icon.png.</param>
        /// <param name="onMore">What a click on the "+" does; null: the screen has no "+".</param>
        /// <param name="nameWidth">A name wider than this shrinks to it.</param>
        public static Dd1ScreenName Add(Transform parent, Vector2 namePos, string iconArt, string name, Action onMore = null, float nameWidth = NameWidth)
        {
            // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            var iconAt = namePos + Dd1Ui.Offset(layout, Block, "icon_offset", 60f, 26f);
            var textAt = namePos + Dd1Ui.Offset(layout, Block, "text_offset", 192f, 118f);
            // the "+" hangs on the icon, not on the name's own point
            var moreAt = iconAt + Dd1Ui.Offset(layout, Block, "sub_icon_offset", 56f, 106f);
            var ringAt = iconAt + Dd1Ui.Offset(layout, Block, "sub_icon_pulse_offset", 56f, 106f);

            var widget = new Dd1ScreenName(namePos + Dd1Ui.Offset(layout, Block, "alert_offset", 115f, 90f));
            widget.Root = UiKit.Rect("ScreenName", parent).Stretch();
            widget.Icon = Dd1Ui.Art("Icon", widget.Root, iconArt, iconAt);

            // text_offset is the foot of the name's line: its cell begins one line above
            var font = Dd1Fonts.Style(Style);
            var line = font != null ? font.LineHeight : StockLine;
            widget.Label = Dd1Ui.Line("Name", widget.Root, Style, new Vector2(textAt.x, textAt.y - line), new Vector2(nameWidth, line + 1f));
            widget.Label.text = name;

            if (onMore != null) widget.BuildMore(parent, moreAt, ringAt, onMore);
            return widget;
        }

        // Whole pixels for the corner of a picture that has its middle at a point: an odd-sized one would blur.
        private static Vector2 Corner(Vector2 middle, float size) => new Vector2(Mathf.Floor(middle.x - size * 0.5f), Mathf.Floor(middle.y - size * 0.5f));

        private void BuildMore(Transform parent, Vector2 middle, Vector2 ringMiddle, Action onMore)
        {
            var corner = Corner(middle, MoreFrame);
            var frame = Dd1Ui.Art("ScreenName.More", parent, ProgressionDir + "info_icon_frame.png", corner, new Vector2(MoreFrame, MoreFrame), new Color(0f, 0f, 0f, 0.85f), true);
            More = (RectTransform)frame.transform;
            More.SetSiblingIndex(Root.GetSiblingIndex() + 1);
            var inset = Mathf.Floor((MoreFrame - MoreIcon) * 0.5f);
            _moreIcon = Dd1Ui.Art("Icon", More, ProgressionDir + "more_info_icon.png", new Vector2(inset, inset), new Vector2(MoreIcon, MoreIcon), UiKit.Gold);
            // the skeleton draws its ring last, over the "+"
            Pulse(More, ringMiddle - corner);

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.onClick.AddListener(() =>
            {
                DD2Estate.Dd2.EstateAudio.Ui("ui/town/button_click");
                onMore();
            });
        }

        /// <summary>DD1's cross (less_info_icon) while what the "+" opened is open, the "+" (more_info_icon) otherwise.</summary>
        public void SetOpen(bool open)
        {
            if (_moreIcon == null) return;
            var icon = Dd1Ui.Sprite(ProgressionDir + (open ? "less_info_icon.png" : "more_info_icon.png"));
            if (icon != null) _moreIcon.sprite = icon;
        }

        /// <summary>DD1's pulsing red exclamation mark on the name (name_layout alert_offset): the screen has news.</summary>
        public void SetAlert(bool on)
        {
            if (_alert == null)
            {
                if (!on || Root == null) return;
                _alert = Exclamation(Root, _alertAt);
            }
            if (_alert != null) _alert.gameObject.SetActive(on);
        }

        /// <summary>
        /// The ring that breathes round a screen's "+": fx/building_upgrade_pulse, animation "pulse" (1.07 s:
        /// the rim of the 73 px ring goes from 0.9 to 1.1 of its size and back, its alpha from 0.38 to 0.5), its
        /// origin at <paramref name="middle"/> of the parent (pixels from its top left corner, y down). The
        /// skeleton's other animation, "notify" (the ring and a flash of the sheet's bar), is not played: when
        /// DD1 plays it is in its code. Null when the install has no such effect.
        /// </summary>
        public static SpineView Pulse(Transform parent, Vector2 middle)
        {
            return SpineView.Loop("building_upgrade_pulse", parent, SpineView.Fx("building_upgrade_pulse"), middle, "pulse");
        }

        /// <summary>
        /// DD1's mark of news: fx/estate_exclamation, animation "alert" (1 s: the 24x60 exclamation mark swells
        /// to 1.1 and back, the 74x77 glow behind it to 1.15), its origin at <paramref name="middle"/>.
        /// </summary>
        public static SpineView Exclamation(Transform parent, Vector2 middle)
        {
            return SpineView.Loop("estate_exclamation", parent, SpineView.Fx("estate_exclamation"), middle, "alert");
        }
    }
}
