using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The statue's screen, after DD1's Ancestor's Memoirs: the statue over its backdrop, the Ancestor's line
    /// above, and on the right the shelves: a title bar per category and under it the category's slabs,
    /// scrolling inside DD1's list area with DD1's scroll bar. A slab is built as DD1's layout file says
    /// (statue.layout.darkest, statue_entry_layout): inside its margins a picture, and right of it a line for
    /// each part: "Part n" in a box of part_textbox_width, the part's title in one of textbox_width, and the
    /// part's own button, DD1's arrow of a memoir that can be played or its lock. A part still shut is drawn
    /// in DD1's colour for it (town_statue_archive_locked_entry).
    ///
    /// DD1 plays a memoir when its arrow is clicked and prints the words where the line stands; the estate
    /// has the words only, so the click turns the shelves over to the memoir's page, and DD1's back arrows
    /// (or a right click) return to them. tools/preview_windows.py draws the same layout offline: change one,
    /// change the other.
    /// </summary>
    internal class MemoirsPanel : MonoBehaviour
    {
        private const string Art = Memoirs.ArtDir + "statue";
        private const string LayoutFile = Memoirs.ArtDir + "statue.layout.darkest";

        private static readonly Vector2 SlabSize = new Vector2(600f, 118f);     // media_entry_*_backdrop.png
        private static readonly Vector2 BarSize = new Vector2(619f, 136f);      // *_title_bar.png; the bar itself is rows 16-118
        private const float BarTop = 16f, BarBottom = 118f;
        private const float Picture = 85f;                                      // portrait_*.png
        private const float Mark = 32f;                                         // playmedia.png, lockedmedia.png
        private const float TextLine = 26f;                                     // Ubuntu small: town_statue_archive_entry
        private static readonly Vector2 BackSize = new Vector2(32f, 33f);       // shared/progression/progression_back.png
        private const float PageTextSize = 26f;                                 // a memoir's words: DD1's quote style, a size down

        private static MemoirsPanel _instance;

        private RosterWindow _window;
        private RectTransform _frame, _shelves, _shelfList, _page, _pageList;
        private TextMeshProUGUI _pageTitle, _pageSubtitle, _pageText;
        private Image _pageBar;
        private Vector2 _listPos, _listSize;
        // DD1 writes margins as left, right, top, bottom: here x, y, z, w.
        private Vector4 _content, _filmImage, _questImage, _button;
        private float _spacing, _textWidth, _partWidth, _textTop, _rowSpacing;
        private string _count = "";
        private string _shown;      // what the shelves were built from
        private string _reading;    // id of the memoir on the page; null on the shelves

        public static bool IsOpen => _instance != null && RosterWindow.Current != null && RosterWindow.Current == _instance._window;

        public static string Reading => IsOpen ? _instance._reading : null;

        public static void Open()
        {
            var window = RosterWindow.Open(Memoirs.BuildingId, ActivityText.Building(Memoirs.BuildingId), Art);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<MemoirsPanel>();
            _instance = panel;
            panel._window = window;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            // DD1: a right click steps back; from a memoir's page that is the shelves.
            window.BackOut = () =>
            {
                if (panel._reading == null) return false;
                panel.ShowShelves();
                return true;
            };
            panel.Refresh();
        }

        public static void Close()
        {
            if (IsOpen) RosterWindow.Close();
        }

        /// <summary>Opens a memoir's page (the dev bridge reads without the mouse). False for an entry that is not on the shelves.</summary>
        public static bool Read(string id)
        {
            if (!IsOpen) Open();
            if (!IsOpen) return false;
            var entry = Memoirs.Find(id);
            if (entry == null) return false;
            _instance.Show(entry);
            return true;
        }

        public static void Back()
        {
            if (IsOpen) _instance.ShowShelves();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- construction --------------------------------------------------------------------------------

        private void Build(RectTransform frame)
        {
            _frame = frame;
            var corner = new Vector2(0f, 1f);
            var top = new Vector2(0.5f, 1f);

            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var layout = Dd1Ui.Layout(LayoutFile);
            var body = _window.Body;
            const string main = "statue_main_layout", entry = "statue_entry_layout";
            _listPos = body + Dd1Ui.Offset(layout, main, "list_position", 270f, 170f);
            _listSize = Dd1Ui.Offset(layout, main, "list_area_size", 600f, 580f);
            _spacing = Dd1Ui.Number(layout, main, "entry_spacing", 10f);
            _content = Margins(layout, entry, "content_margins", 10f, 10f, 10f, 10f);
            _filmImage = Margins(layout, entry, "video_image_margins", 0f, 10f, 0f, 0f);
            _questImage = Margins(layout, entry, "plot_quest_image_margins", 0f, 10f, 0f, 0f);
            _button = Margins(layout, entry, "button_margins", 0f, 5f, 0f, 0f);
            _textWidth = Dd1Ui.Number(layout, entry, "textbox_width", 350f);
            _partWidth = Dd1Ui.Number(layout, entry, "part_textbox_width", 75f);
            _textTop = Dd1Ui.Number(layout, entry, "textbox_top_margin", 5f);
            _rowSpacing = Dd1Ui.Number(layout, entry, "textbox_entry_spacing", 0f);
            var quotePos = body + Dd1Ui.Offset(layout, main, "quote_position", 590f, 80f);
            var quoteWidth = Dd1Ui.Number(layout, main, "quote_width", 505f);
            var barOffset = Dd1Ui.Offset(layout, main, "scrollbar_offset", 20f, -50f).x;

            // DD1 prints the words of the memoir being played here; at rest the statue's own line stands there.
            var quote = Dd1Ui.Block("Quote", frame, "town_statue_quote", quotePos, new Vector2(quoteWidth, 84f), TextAlignmentOptions.Top);
            quote.text = Dd1Strings.Plain(Dd1Strings.Get("str_statue_ancestor_quote"));

            _shelves = UiKit.Rect("Shelves", frame).Stretch();
            _shelfList = Dd1Ui.ScrollList("Area", _shelves, _listPos, _listSize, barOffset, out _);

            // The page of one memoir, in the same place.
            _page = UiKit.Rect("Page", frame).PlaceTopLeft(_listPos, corner, _listSize);
            _pageBar = UiKit.Image("Bar", _page, null);
            ((RectTransform)_pageBar.transform).PlaceTopLeft(new Vector2(_listSize.x * 0.5f, -BarTop), top, BarSize);
            _pageTitle = Dd1Ui.Line("Title", _pageBar.transform, "town_statue_media_heading", new Vector2(BarSize.x * 0.5f, (BarTop + BarBottom - 40f) * 0.5f), new Vector2(BarSize.x - 230f, 40f), TextAlignmentOptions.Top);
            var barHeight = BarBottom - BarTop;
            _pageSubtitle = Dd1Ui.Line("Subtitle", _page, "town_statue_archive_entry", new Vector2(_listSize.x * 0.5f, barHeight + 2f), new Vector2(_listSize.x, 26f), TextAlignmentOptions.Top);
            var textTop = barHeight + 34f;
            _pageList = Dd1Ui.ScrollList("Text", _page, new Vector2(0f, textTop), new Vector2(_listSize.x, _listSize.y - textTop), barOffset, out _);
            _pageText = UiKit.Text("Words", _pageList, "", "town_statue_quote", UiKit.Neutral, TextAlignmentOptions.Top, PageTextSize);
            var words = (RectTransform)_pageText.transform;
            words.anchorMin = new Vector2(0f, 1f);
            words.anchorMax = new Vector2(1f, 1f);
            words.pivot = new Vector2(0.5f, 1f);
            words.offsetMin = new Vector2(18f, 0f);
            words.offsetMax = new Vector2(-18f, 0f);
            // DD1's way back: the red arrows, here at the head of the page's bar.
            var backAt = new Vector2(4f, (barHeight - BackSize.y) * 0.5f);
            var back = Dd1Ui.ArtButton("Back", _page, "shared/progression/progression_back.png", backAt, BackSize, ShowShelves, "<");
            UiKit.Hover(back.gameObject, inside => _window.Tip(back, inside, _listPos + backAt + new Vector2(0f, BackSize.y + 4f), null, "Back to the memoirs (or right click)"));

            _page.gameObject.SetActive(false);
        }

        // Four numbers of a layout block, which DD1 writes as left, right, top, bottom: content_margins
        // 10 10 10 10 leaves the 98 px that three 32 px lines with their buttons need, and the image
        // margins' second number (10) is the gap between the picture and the text.
        private static Vector4 Margins(DarkestFile layout, string block, string key, float left, float right, float top, float bottom)
        {
            var entry = layout?.Find(block);
            if (entry == null || entry.Values(key).Count < 4) return new Vector4(left, right, top, bottom);
            return new Vector4(entry.Float(key, 0, left), entry.Float(key, 1, right), entry.Float(key, 2, top), entry.Float(key, 3, bottom));
        }

        // ---- the shelves ---------------------------------------------------------------------------------

        private void Refresh()
        {
            var slabs = Memoirs.Shelves();
            var key = "";
            foreach (var slab in slabs)
                foreach (var part in slab.Parts) key += part.Id + (part.Unlocked ? "+" : "-") + ";";
            if (key == _shown) return;
            _shown = key;

            _window.Tooltip.Hide(null);
            UpgradeUi.Clear(_shelfList);
            var y = 0f;
            Memoirs.Category category = null;
            foreach (var slab in slabs)
            {
                if (slab.Category != category)
                {
                    category = slab.Category;
                    y = AddBar(category, y);
                }
                AddSlab(slab, y);
                y += SlabSize.y + _spacing;
            }
            _shelfList.sizeDelta = new Vector2(0f, y);
            _count = Memoirs.PlotDone + " of " + Memoirs.PlotCount + " chapters of the estate's story told";
        }

        // DD1's title bar art has a wide margin of empty rows around the bar, and DD1 lays the whole picture into the
        // list as an entry like any other (its own frame: 156 px from the foot of one shelf's last slab to the top of
        // the next shelf's first: the spacing after the slab and the picture's 136 rows; nothing after the picture).
        private float AddBar(Memoirs.Category category, float y)
        {
            var sprite = Dd1Ui.Sprite(category.TitleBar);
            var bar = UiKit.Image("Bar." + category.Id, _shelfList, sprite, sprite != null ? Color.white : new Color(0.1f, 0.08f, 0.1f, 0.9f));
            ((RectTransform)bar.transform).PlaceTopLeft(new Vector2(_listSize.x * 0.5f, y), new Vector2(0.5f, 1f), BarSize);
            Dd1Ui.Line("Label", bar.transform, "town_statue_media_heading", new Vector2(BarSize.x * 0.5f, (BarTop + BarBottom - 40f) * 0.5f), new Vector2(BarSize.x - 230f, 40f), TextAlignmentOptions.Top).text = category.Label;
            return y + BarSize.y;
        }

        private void AddSlab(Memoirs.Slab slab, float y)
        {
            var art = Dd1Ui.Sprite(slab.Category.Slab);
            var back = UiKit.Image("Slab." + slab.Parts[0].Id, _shelfList, art, art != null ? Color.white : new Color(0.1f, 0.1f, 0.11f, 0.9f));
            var on = (RectTransform)back.transform;
            on.PlaceTopLeft(new Vector2(0f, y), Dd1Ui.TopLeft, SlabSize);

            // The picture stands in the content's corner; a slab whose parts are all shut has it in DD1's
            // colour for a shut entry (darker and nearly without colour).
            var image = slab.Film ? _filmImage : _questImage;
            var shut = Dd1Shade.Of("town_statue_archive_locked_entry", 0.4f, 0.2f);
            var picture = UiKit.Image("Picture", on, null);
            picture.preserveAspect = true;
            var pictureAt = new Vector2(_content.x + image.x, _content.z + image.z);
            ((RectTransform)picture.transform).PlaceTopLeft(pictureAt, Dd1Ui.TopLeft, new Vector2(Picture, Picture));
            ShowPicture(picture, slab, shut);
            if (slab.About != null && slab.Unlocked)
            {
                picture.raycastTarget = true;
                UiKit.Hover(picture.gameObject, inside => _window.Tip(picture, inside, Place(picture) + new Vector2(0f, Picture + 4f), null, slab.About));
            }

            // A line per part, from the content's top: the button, and the text 5 px (textbox_top_margin) under its top.
            var left = pictureAt.x + Picture + image.y;
            var pitch = _button.z + Mark + _button.w + _rowSpacing;
            for (var i = 0; i < slab.Parts.Count; i++) AddPart(on, slab.Parts[i], left, _content.z + pitch * i, shut);
        }

        private void AddPart(RectTransform slab, Memoirs.Entry part, float left, float top, Dd1Shade shut)
        {
            // DD1 writes an entry in its archive style; one that is shut in its colour for that.
            var colour = Dd1Fonts.Colour("town_statue_archive_entry", UiKit.Neutral);
            if (!part.Unlocked) colour = shut.Text(colour);
            // DD1's own frame of this list (_lab/dd1_ref/town/town_statue_ancestors_memoirs.png) settles the order in
            // a line: the part's button first, right after the picture (slab +105), then "Part n" in its box (+141:
            // the button's 30 and its right margin of 5 on), then the title (+216). A memoir that stands alone
            // is "Part 1" as well ("Part 1   House of Ruin").
            var mark = Dd1Ui.Art("Mark." + part.Id, slab, Memoirs.ArtDir + (part.Unlocked ? "playmedia.png" : "lockedmedia.png"),
                new Vector2(left + _button.x, top + _button.z), new Vector2(Mark, Mark), null, true);
            var textAt = left + _button.x + Mark + _button.y + 1f;
            Dd1Ui.Line("Part." + part.Id, slab, "town_statue_archive_entry", new Vector2(textAt, top + _textTop), new Vector2(_partWidth, TextLine), TextAlignmentOptions.TopLeft, colour).text =
                (WindowText.Plain("str_part") ?? "Part") + " " + Mathf.Max(1, part.Part);
            Dd1Ui.Line("Title." + part.Id, slab, "town_statue_archive_entry", new Vector2(textAt + _partWidth, top + _textTop), new Vector2(_textWidth, TextLine), TextAlignmentOptions.TopLeft, colour).text = part.Title;

            if (part.Unlocked)
            {
                UpgradeUi.Pointer(mark, () =>
                {
                    DD2Estate.Dd2.EstateAudio.Ui("ui/town/button_click");
                    Show(part);
                });
                // DD1 draws a picture that is a button brighter under the pointer (button_highlight).
                mark.gameObject.AddComponent<DD2Estate.Dungeon.RaidHighlight>();
            }
            else if (!string.IsNullOrEmpty(part.LockHint))
            {
                // What opens it, where DD1 has words for that (str_media_epilog_locked, str_media_plot_darkest_dungeon_<n>).
                UiKit.Hover(mark.gameObject, inside => _window.Tip(mark, inside, Place(mark) + new Vector2(0f, Mark + 4f), null, part.LockHint));
            }
        }

        // The boss's own DD2 portrait when the game has it loaded, else the DD1 picture of the quest's slot.
        private static void ShowPicture(Image picture, Memoirs.Slab slab, Dd1Shade shut)
        {
            Sprite sprite = null;
            if (slab.Actor != null)
            {
                try { sprite = HeroNames.Portrait(slab.Actor); }
                catch (System.Exception) { sprite = null; }
            }
            if (sprite != null)
            {
                // A picture of DD2's can only be darkened here: the list is cut off by a rect mask, which the game's greyscale material may not know.
                picture.sprite = sprite;
                picture.color = slab.Unlocked ? Color.white : new Color(shut.Darkness, shut.Darkness, shut.Darkness);
                return;
            }
            picture.sprite = slab.Unlocked ? Dd1Ui.Sprite(slab.Icon) : shut.Art(slab.Icon);
            picture.color = picture.sprite != null ? Color.white : new Color(0.2f, 0.2f, 0.2f);
        }

        // Where something on the shelves is now, in the backdrop's pixels: the list scrolls under the pointer.
        private Vector2 Place(Graphic graphic)
        {
            var corners = new Vector3[4];
            graphic.rectTransform.GetWorldCorners(corners);
            var local = _frame.InverseTransformPoint(corners[1]);      // the top left corner
            var origin = new Vector2(-_frame.rect.width * _frame.pivot.x, _frame.rect.height * (1f - _frame.pivot.y));
            return new Vector2(local.x - origin.x, origin.y - local.y);
        }

        // ---- a memoir's page -----------------------------------------------------------------------------

        private void Show(Memoirs.Entry entry)
        {
            _reading = entry.Id;
            _window.Tooltip.Hide(null);
            var bar = Dd1Ui.Sprite(entry.Category.TitleBar);
            _pageBar.sprite = bar;
            _pageBar.color = bar != null ? Color.white : new Color(0.1f, 0.08f, 0.1f, 0.9f);
            _pageTitle.text = entry.Title;
            _pageSubtitle.text = entry.Subtitle ?? entry.Category.Label;
            _pageText.text = entry.Text;
            // The text decides the height of what scrolls.
            var width = _listSize.x - 36f;
            var height = _pageText.GetPreferredValues(_pageText.text, width, 0f).y + 12f;
            ((RectTransform)_pageText.transform).sizeDelta = new Vector2(((RectTransform)_pageText.transform).sizeDelta.x, height);
            _pageList.sizeDelta = new Vector2(0f, height);
            _pageList.anchoredPosition = Vector2.zero;
            _shelves.gameObject.SetActive(false);
            _page.gameObject.SetActive(true);
        }

        private void ShowShelves()
        {
            _reading = null;
            _window.Tooltip.Hide(null);
            _page.gameObject.SetActive(false);
            _shelves.gameObject.SetActive(true);
        }

        // ---- dev -----------------------------------------------------------------------------------------

        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            return new { open = true, reading = _instance._reading, title = _instance._reading != null ? _instance._pageTitle.text : null, count = _instance._count };
        }
    }
}
