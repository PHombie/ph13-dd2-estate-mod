using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Glossary screen, on DD1's layout (campaign/town/glossary/glossary.layout.darkest; its backdrop,
    /// its scholar, its icon and its rule lie beside it): the scholar with his lantern where a building's keeper
    /// stands, the Ancestor's line over the list, and down the right half the terms: the term in gold, what it
    /// means beside it, a rule under each, scrolling in DD1's list area with DD1's scroll bar.
    ///
    /// The layout file gives the places. How the parts are put there is read out of DD1's exe
    /// (SharedUI::Glossary), no file says it:
    /// - the list is a column: an entry, then the rule (separator.png at its own size, from the column's left
    ///   edge), each keeping entry_spacing under it;
    /// - an entry is a row: the term with a colon after it in a box term_textbox_width wide, the definition in
    ///   what is left of the list's width;
    /// - the Ancestor's line is centred on quote_position and wraps at quote_width;
    /// - the screen's name is DD1's name widget (<see cref="Dd1ScreenName"/>), as on every screen of the town.
    ///   On this backdrop that is the icon over the head of the two rules painted there and the name between
    ///   them, its foot on the lower one.
    /// The four numbers of entry_content_margins and term_textbox_margins are all nought in DD1's file and
    /// which of them is which side is not known: they are not read.
    ///
    /// tools/preview_glossary.py draws the same screen offline: change one, change the other.
    /// </summary>
    internal class GlossaryPanel : MonoBehaviour
    {
        private const string Dir = "campaign/town/glossary/";
        private const string LayoutFile = Dir + "glossary.layout.darkest";

        // Sizes of DD1's art.
        private static readonly Vector2 FrameSize = new Vector2(1395f, 776f);    // glossary_background.png
        private static readonly Vector2 ScholarSize = new Vector2(811f, 757f);   // glossary_character.png
        private static readonly Vector2 Rule = new Vector2(584f, 7f);            // separator.png
        private static readonly Color RuleGrey = new Color32(51, 51, 51, 255);   // its one colour
        private static readonly Vector2 CloseSize = new Vector2(32f, 32f);       // progression_close.png
        // Measured from glossary_background.png: the two rules of the name end at x 406.
        private const float NameEnd = 406f;
        // FALLBACK line heights, for a font that cannot be had: DD1's dwarvenaxe-m.fnt, ubuntu.fnt.
        private const float TermLine = 40f, TextLine = 25f;
        // The mod's own: DD1's files give its scroll arrows no step. A click moves three lines of a definition.
        private const int ArrowLines = 3;

        private class Row
        {
            public Glossary.Term Term;
            public float Top, Bottom;
        }

        private static GlossaryPanel _instance;

        private TownPanel _panel;
        private RectTransform _frame, _list;
        private ScrollRect _scroll;
        private float _spacing, _termWidth, _textWidth, _termLine, _textLine;
        private readonly List<Row> _rows = new List<Row>();

        public static bool IsOpen => _instance != null && TownPanel.Current != null && TownPanel.Current == _instance._panel;

        public static void Toggle()
        {
            if (IsOpen) TownPanel.Close();
            else Open();
        }

        public static bool Open()
        {
            if (IsOpen) return true;
            var panel = TownPanel.Open(Glossary.Id, true);
            if (panel == null) return false;
            var view = panel.gameObject.AddComponent<GlossaryPanel>();
            _instance = view;
            view._panel = panel;
            view.Build();
            view.Fill();
            return true;
        }

        public static void Close()
        {
            if (IsOpen) TownPanel.Close();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- construction --------------------------------------------------------------------------------

        private void Build()
        {
            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            const string main = "glossary_main_layout";
            _frame = _panel.AddPart("Frame", Dd1Ui.Offset(layout, main, "pos", 144f, 132f), FrameSize, TownPanel.Side.Centre);

            var back = Dd1Ui.Art("Backdrop", _frame, Dir + "glossary_background.png", Vector2.zero, FrameSize, new Color(0.03f, 0.03f, 0.035f, 0.97f), true);
            TownPanel.RightClickCloses(back);
            // He hangs 81 px below the backdrop's foot and ends behind the estate's bar, as a building's keeper does.
            Dd1Ui.Art("Scholar", _frame, Dir + "glossary_character.png", Dd1Ui.Offset(layout, main, "character_position", -50f, 100f), ScholarSize);

            // DD1's name of a town screen: the icon over the head of the two rules painted here, the name between them
            var namePos = Dd1Ui.Offset(layout, main, "name_pos", -46f, -8f);
            Dd1ScreenName.Add(_frame, namePos, Dir + "glossary.icon.png", Glossary.ScreenName, null, Mathf.Max(120f, NameEnd - Dd1ScreenName.NameLeft(namePos)));

            _termLine = LineOf("glossary_term", TermLine);
            _textLine = LineOf("glossary_term_definition", TextLine);
            // one line in DD1 ("Let me share with you the terrible wonders I have come to know..."); room for two
            var quote = Dd1Ui.Block("Quote", _frame, "glossary_quote", Dd1Ui.Offset(layout, main, "quote_position", 1000f, 50f),
                new Vector2(Dd1Ui.Number(layout, main, "quote_width", 780f), 2f * LineOf("glossary_quote", TermLine)), TextAlignmentOptions.Top);
            quote.text = Glossary.Quote;

            Dd1Ui.ArtButton("Close", _frame, "shared/progression/progression_close.png", Dd1Ui.Offset(layout, main, "close_position", 1352f, 12f), CloseSize, TownPanel.Close, "X");

            // The terms: a window on a list that scrolls, DD1's scroll bar beside it (Dd1ScrollBar).
            var listPos = Dd1Ui.Offset(layout, main, "list_position", 680f, 140f);
            var listSize = Dd1Ui.Offset(layout, main, "list_area_size", 640f, 580f);
            var barOffset = Dd1Ui.Number(layout, main, "scrollbar_offset", 20f);
            _spacing = Dd1Ui.Number(layout, main, "entry_spacing", 10f);
            _termWidth = Mathf.Clamp(Dd1Ui.Number(layout, "glossary_entry_layout", "term_textbox_width", 190f), 0f, listSize.x - 1f);
            _textWidth = listSize.x - _termWidth;
            _list = Dd1Ui.ScrollList("Terms", _frame, listPos, listSize, barOffset, out _scroll);
            // DD1: a right click steps back, also from over the list
            TownPanel.RightClickCloses(_scroll.GetComponent<Image>());
            Dd1ScrollBar.Of(_scroll).Step = ArrowLines * _textLine;
        }

        // The height of a line of a DD1 text style: its font is drawn as it is stored.
        private static float LineOf(string style, float fallback)
        {
            var font = UiKit.UseDd1Fonts ? Dd1Fonts.Style(style) : null;
            return font != null && font.LineHeight > 0 ? font.LineHeight : fallback;
        }

        private void Fill()
        {
            UpgradeUi.Clear(_list);
            _rows.Clear();
            var y = 0f;
            foreach (var term in Glossary.Terms())
            {
                var row = new Row { Term = term, Top = y };
                // DD1 writes a colon after the term (its exe joins ":" to str_glossary_term_N)
                var name = Words("Term" + term.Number, "glossary_term", new Vector2(0f, y), _termWidth, _termLine, term.Name + ":");
                var meaning = Words("Text" + term.Number, "glossary_term_definition", new Vector2(_termWidth, y), _textWidth, _textLine, term.Definition);
                y += Mathf.Max(name, meaning) + _spacing;
                Dd1Ui.Art("Rule" + term.Number, _list, Dir + "separator.png", new Vector2(0f, y), Rule, RuleGrey);
                y += Rule.y + _spacing;
                row.Bottom = y;
                _rows.Add(row);
            }
            _list.sizeDelta = new Vector2(0f, y);
            _scroll.verticalNormalizedPosition = 1f;
        }

        // A text of an entry at DD1's size: as wide as its box, as tall as its words. Returns its height.
        private float Words(string name, string style, Vector2 at, float width, float line, string text)
        {
            var label = Dd1Ui.Block(name, _list, style, at, new Vector2(width, line));
            label.text = text;
            label.enableAutoSizing = false;
            var height = Mathf.Max(line, Mathf.Ceil(label.GetPreferredValues(text, width, 0f).y));
            ((RectTransform)label.transform).sizeDelta = new Vector2(width, height);
            return height;
        }

        // How far the list can move, and how far down it is (pixels of the list).
        private float Room => Mathf.Max(0f, _list.rect.height - ((RectTransform)_scroll.transform).rect.height);

        private float Down => (1f - _scroll.verticalNormalizedPosition) * Room;

        private void Scroll(int lines)
        {
            var room = Room;
            if (room <= 0f) return;
            // 1 = the top of the list
            _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01((Down + lines * _textLine) / room);
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            var view = _instance;
            var window = ((RectTransform)view._scroll.transform).rect.height;
            var down = view.Down;
            var inSight = new List<object>();
            foreach (var row in view._rows)
                if (row.Bottom > down && row.Top < down + window) inSight.Add(new { number = row.Term.Number, term = row.Term.Name });
            return new { open = true, terms = view._rows.Count, listHeight = view._list.rect.height, window, down, at = view._scroll.verticalNormalizedPosition, inSight };
        }

        /// <summary>For tests: moves the list by lines of a definition's text, down for a number above 0.</summary>
        public static bool ScrollBy(int lines)
        {
            if (!IsOpen) return false;
            _instance.Scroll(lines);
            return true;
        }
    }
}
