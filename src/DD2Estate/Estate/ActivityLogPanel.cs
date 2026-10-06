using System.Collections.Generic;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// DD1's Activity Log screen, on DD1's layout (campaign/town/activity_log/activity_log.layout.darkest, its
    /// backdrop beside it, the entries' art in activity_log/): the caretaker's desk where a building's backdrop
    /// would stand (town.layout.darkest activity_log_pos), the log down its left half and the caretaker's
    /// goals over his head on the right. The log reads from the present week back: DD1's title bar for a week,
    /// under it what happened in it, each kind of entry in DD1's frame for it (a hero in a building with the
    /// building's sign and the hero's face, a resolve level, a building's upgrade, a region's level, news of
    /// the Darkest Dungeon), a quest's end under DD1's banner for a success, a defeat or a retreat.
    ///
    /// Which number of the layout means what is argued in docs/recon/dd1-town-panels.md.
    /// tools/preview_town_panels.py draws the same screen offline: change one, change the other.
    /// </summary>
    internal class ActivityLogPanel : MonoBehaviour
    {
        private const string Dir = "campaign/town/activity_log/";
        private const string EntryDir = "activity_log/";
        private const string LayoutFile = Dir + "activity_log.layout.darkest";
        private const string TownLayout = "campaign/town/town.layout.darkest";

        // Sizes of DD1's art.
        private static readonly Vector2 FrameSize = new Vector2(1395f, 776f);    // activitylog_bg.png
        private static readonly Vector2 CloseSize = new Vector2(32f, 32f);       // progression_close.png
        private static readonly Vector2 WeekBar = new Vector2(619f, 136f);       // week_title_bar.png
        private static readonly Vector2 Banner = new Vector2(619f, 58f);         // raid_*_banner.png
        private static readonly Vector2 Box = new Vector2(600f, 118f);           // *_entry_backdrop.png
        private const float Sign = 64f;                                          // <building>.icon_roster.png
        private const float DarkestIcon = 85f;                                   // darkest_dungeon_log_icon.png

        // Measured from week_title_bar.png: the bar's art begins 20 rows down, its inner field is centred on 322, 67.
        // Measured in DD1's own frame (_lab/dd1_ref/town/estate_bar_2_activity_log.png, the picture matched pixel by
        // pixel): the bar's picture stands with its top on the list's top (189, 292 on screen) and the week's first
        // entry begins 146 under it: the picture's whole 136 rows and 10 more. DD1 lays the picture in as it is.
        private const float WeekBarLead = 10f;
        private static readonly Vector2 WeekText = new Vector2(322f, 67f);
        private const float WeekBarStep = 136f;
        // The mod's own: how an entry's parts stand inside DD1's frame for it (hero_building_log_entry_layout
        // gives margins, not places: see the doc).
        private const float BoxInset = 10f;                 // the frame drawn into a backdrop
        private const float LineHeight = 25f;               // Ubuntu small
        private const float ScrollStep = 150f;
        // Measured on DD1's own screen (1920x1080 frames of the log in weeks 1 and 2): both lists stand 20 px
        // right of their text_pos. The week's bar and the entries' frames begin at 189 = 144 + 25 + 20 and the
        // log's rail at 809, right after a window of panel_size 620; the goals' rail stands at 1484 = 144 + 720
        // + 20 + 600. No file has the 20: the frames beat the reading of text_pos as the window's corner.
        private const float ListShift = 20f;
        // A year of entries is some thousand objects: the newest weeks are built at once, older ones as the
        // reader comes to the foot of the list.
        private const int FirstWeeks = 8;
        private const int MoreWeeks = 8;

        private static ActivityLogPanel _instance;

        private TownPanel _panel;
        private RectTransform _frame, _list, _goals;
        private ScrollRect _scroll;
        private Vector2 _start;
        private float _gap, _lineGap, _textWidth, _boxLeft, _goalGap, _goalIndent, _goalWidth;
        private readonly List<KeyValuePair<UpgradeUi.Slot, string>> _faces = new List<KeyValuePair<UpgradeUi.Slot, string>>();
        private List<int> _weeks = new List<int>();
        private int _nextWeek;
        private float _y;
        private bool _stale = true, _adding;
        private int _week = -1;
        private int _goalsDone = -1;

        public static bool IsOpen => _instance != null && TownPanel.Current != null && TownPanel.Current == _instance._panel;

        public static void Toggle()
        {
            if (IsOpen) TownPanel.Close();
            else Open();
        }

        public static bool Open()
        {
            var panel = TownPanel.Open(ActivityLog.Id, true);
            if (panel == null) return false;
            var view = panel.gameObject.AddComponent<ActivityLogPanel>();
            _instance = view;
            view._panel = panel;
            view.Build();
            panel.Refresh = view.Refresh;
            view.Refresh();
            return true;
        }

        public static void Close()
        {
            if (IsOpen) TownPanel.Close();
        }

        private void OnEnable() => ActivityLog.Changed += MarkStale;

        private void OnDisable() => ActivityLog.Changed -= MarkStale;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void MarkStale() => _stale = true;

        // ---- construction --------------------------------------------------------------------------------

        private void Build()
        {
            // FALLBACK numbers: DD1's own values of these entries, used when a file cannot be read.
            var town = Dd1Ui.Layout(TownLayout);
            var layout = Dd1Ui.Layout(LayoutFile);
            const string log = "activity_log_layout", entry = "activity_log_entry_layout", goals = "caretaker_goal_layout";
            var at = Dd1Ui.Offset(town, "town_screen_layout", "activity_log_pos", 144f, 132f);
            _frame = _panel.AddPart("Frame", at, FrameSize, TownPanel.Side.Centre);

            var back = Dd1Ui.Art("Backdrop", _frame, Dir + "activitylog_bg.png", Vector2.zero, FrameSize, new Color(0.03f, 0.03f, 0.035f, 0.97f), true);
            TownPanel.RightClickCloses(back);

            // DD1's name of a town screen, twice: the log's and the goals', each with its own icon
            Dd1ScreenName.Add(_frame, Dd1Ui.Offset(layout, log, "name_pos", -46f, -12f), Dir + "activity_log.icon.png", ActivityLog.ScreenName);
            Dd1ScreenName.Add(_frame, Dd1Ui.Offset(layout, goals, "name_pos", 650f, -12f), Dir + "caretaker_goals.icon.png", WindowText.Plain("str_caretaker_goals_heading") ?? "Caretaker Goals");
            Dd1Ui.ArtButton("Close", _frame, "shared/progression/progression_close.png", Dd1Ui.Offset(layout, log, "close_pos", 1352f, 12f), CloseSize, TownPanel.Close, "X");

            // The log: a window on a list that scrolls, DD1's scroll bar beside it (the rail with its end caps,
            // the pip and the two arrows: Dd1ScrollBar, which stands as DD1's own screen shows it).
            var panelPos = Dd1Ui.Offset(layout, entry, "text_pos", 25f, 160f) + new Vector2(ListShift, 0f);
            var panelSize = Dd1Ui.Offset(layout, log, "panel_size", 620f, 550f);
            var barOffset = Dd1Ui.Number(layout, log, "scrollbar_offset", 0f);
            _start = Dd1Ui.Offset(layout, log, "entry_start_pos", 0f, 10f);
            // entry_spacing: the step of one line of the log; what it leaves over a line of text parts two lines
            _lineGap = Mathf.Max(0f, Dd1Ui.Offset(layout, log, "entry_spacing", 0f, 30f).y - LineHeight);
            _gap = Dd1Ui.Number(layout, entry, "vertical_spacing", 20f);
            _textWidth = panelSize.x - 2f * BoxInset;
            // an entry's frame is 600 wide in a window of 620: DD1 has it at the window's left edge, as the week's bar
            _boxLeft = 0f;
            _list = Dd1Ui.ScrollList("Log", _frame, panelPos, panelSize, barOffset, out _scroll);
            _scroll.onValueChanged.AddListener(at =>
            {
                if (at.y <= 0.02f) More(MoreWeeks);
            });
            Dd1ScrollBar.Of(_scroll).Step = ScrollStep;

            // The caretaker's goals, over his head.
            var goalPos = Dd1Ui.Offset(layout, goals, "text_pos", 720f, 160f) + new Vector2(ListShift, 0f);
            var goalSize = Dd1Ui.Offset(layout, goals, "panel_size", 600f, 240f);
            var goalSpacing = Dd1Ui.Offset(layout, goals, "entry_spacing", 10f, 10f);
            _goalIndent = goalSpacing.x;
            _goalGap = goalSpacing.y;
            _goalWidth = goalSize.x;
            _goals = Dd1Ui.ScrollList("Goals", _frame, goalPos, goalSize, Dd1Ui.Number(layout, goals, "scrollbar_offset", 0f), out _);
        }

        private void Scroll(int direction)
        {
            Dd1ScrollBar.Of(_scroll)?.MoveBy(direction * ScrollStep);
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void Refresh()
        {
            if (_frame == null) return;
            var week = EstateState.Current.Week;
            if (_stale || week != _week)
            {
                _stale = false;
                _week = week;
                FillLog();
                _goalsDone = -1;
            }
            var done = 0;
            var questGoals = ActivityLog.QuestGoals();
            var rosterGoals = ActivityLog.RosterGoals();
            foreach (var goal in questGoals) done += goal.Done ? 1 : 0;
            foreach (var goal in rosterGoals) done += goal.Done ? 1 : 0;
            if (done != _goalsDone)
            {
                _goalsDone = done;
                FillGoals(questGoals, rosterGoals);
            }

            // The game drops portrait art it thinks nobody uses: a face that is gone is fetched again.
            foreach (var face in _faces)
                if (face.Key.Portrait != null && face.Key.Portrait.sprite == null) face.Key.Show(HeroNames.Portrait(face.Value));
        }

        private void FillLog()
        {
            UpgradeUi.Clear(_list);
            _faces.Clear();
            _weeks = ActivityLog.Weeks();
            _nextWeek = 0;
            _y = _start.y;
            _list.sizeDelta = Vector2.zero;
            More(FirstWeeks);
            _scroll.verticalNormalizedPosition = 1f;
        }

        // Builds the next weeks under what is there, and keeps what is on screen where it is.
        private void More(int weeks)
        {
            if (_adding || _nextWeek >= _weeks.Count) return;
            _adding = true;
            var window = ((RectTransform)_scroll.transform).rect.height;
            var down = (1f - _scroll.verticalNormalizedPosition) * Mathf.Max(0f, _list.rect.height - window);
            for (var i = 0; i < weeks && _nextWeek < _weeks.Count; i++) _y = AddWeek(_weeks[_nextWeek++], _y);
            _list.sizeDelta = new Vector2(0f, _y);
            var room = _y - window;
            if (room > 0f) _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(down / room);
            _adding = false;
        }

        private float AddWeek(int week, float y)
        {
            Dd1Ui.Art("Week" + week, _list, EntryDir + "week_title_bar.png", new Vector2(_start.x, y - WeekBarLead), WeekBar, new Color(0.1f, 0.08f, 0.11f, 0.9f));
            var title = Dd1Ui.Line("WeekTitle" + week, _list, "town_activity_log_week_title", new Vector2(_start.x + WeekText.x, y - WeekBarLead + WeekText.y - 32f), new Vector2(236f, 64f),
                TextAlignmentOptions.Center);
            title.text = WindowText.Format("str_week", "Week %d", week);
            y += WeekBarStep;

            // a week nothing has happened in yet is its bar alone: DD1 has no words for it
            var entries = ActivityLog.Of(week);
            var number = 0;
            ActivityLog.Entry before = null;
            foreach (var entry in entries)
            {
                var name = "Entry" + week + "." + number++;
                // lines of text follow each other closely; a frame or a banner keeps its distance
                var plain = entry.Kind == ActivityLog.Kinds.Note;
                if (before != null) y += plain && before.Kind == ActivityLog.Kinds.Note ? _lineGap : _gap;
                y = Add(name, entry, y);
                before = entry;
            }
            return y + _gap;
        }

        private float Add(string name, ActivityLog.Entry entry, float y)
        {
            switch (entry.Kind)
            {
                case ActivityLog.Kinds.Success:
                case ActivityLog.Kinds.Failure:
                case ActivityLog.Kinds.Abandon:
                    Dd1Ui.Art(name + ".Banner", _list, EntryDir + "raid_" + entry.Kind + "_banner.png", new Vector2(_start.x, y), Banner, new Color(0.25f, 0.1f, 0.05f, 0.9f));
                    y += Banner.y;
                    return string.IsNullOrEmpty(entry.Text) ? y : Note(name, y + _lineGap, entry.Text);
                case ActivityLog.Kinds.Hero:
                    return Boxed(name, entry, y, "hero_activity_entry_backdrop.png");
                case ActivityLog.Kinds.Level:
                    return Boxed(name, entry, y, "hero_level_up_entry_backdrop.png");
                case ActivityLog.Kinds.Building:
                    return Boxed(name, entry, y, "building_upgrade_entry_backdrop.png");
                case ActivityLog.Kinds.Region:
                    return Boxed(name, entry, y, "dungeon_unlocked_entry_backdrop.png");
                case ActivityLog.Kinds.Darkest:
                    return Boxed(name, entry, y, "darkest_dungeon_entry_backdrop.png");
                default:
                    return Note(name, y, entry.Text);
            }
        }

        // A line of the log: as wide as the frames' insides, as tall as its words.
        private float Note(string name, float y, string text)
        {
            var label = Dd1Ui.Block(name, _list, "town_activity_log_entry", new Vector2(_start.x + BoxInset + 2f, y), new Vector2(_textWidth, LineHeight));
            label.text = text;
            label.enableAutoSizing = false;
            var height = Mathf.Max(LineHeight, Mathf.Ceil(label.GetPreferredValues(text, _textWidth, 0f).y));
            ((RectTransform)label.transform).sizeDelta = new Vector2(_textWidth, height);
            return y + height;
        }

        // DD1's frame of an entry: the building's sign and the hero's face at its left as far as the entry has
        // them, the words in what is left (DD1's text box is 440 wide beside both).
        private float Boxed(string name, ActivityLog.Entry entry, float y, string backdrop)
        {
            var corner = new Vector2(_start.x + _boxLeft, y);
            Dd1Ui.Art(name, _list, EntryDir + backdrop, corner, Box, new Color(0.13f, 0.12f, 0.08f, 0.95f));
            var x = corner.x + BoxInset;
            var darkest = entry.Kind == ActivityLog.Kinds.Darkest;
            if (darkest)
            {
                // DD1 draws its art on whole pixels: 85 in 118 leaves an odd one, which goes below
                Dd1Ui.Art(name + ".Sign", _list, EntryDir + "darkest_dungeon_log_icon.png", new Vector2(x + 6f, y + Mathf.Floor((Box.y - DarkestIcon) * 0.5f)), new Vector2(DarkestIcon, DarkestIcon));
                x += 6f + DarkestIcon + 10f;
            }
            else if (!string.IsNullOrEmpty(entry.Building) && entry.Kind != ActivityLog.Kinds.Region)
            {
                // DD1 has a roster sign for the buildings that keep a hero; the others lend their icon.
                var sign = UpgradeUi.BuildingsDir + entry.Building + "/" + entry.Building + ".icon_roster.png";
                var small = Dd1Ui.Sprite(sign) != null;
                if (!small) sign = UpgradeUi.BuildingsDir + entry.Building + "/" + entry.Building + ".icon.png";
                if (Dd1Ui.Sprite(sign) != null)
                {
                    Dd1Ui.Art(name + ".Sign", _list, sign, new Vector2(x, y + (small ? 22f : Mathf.Floor((Box.y - Sign) * 0.5f))), new Vector2(Sign, Sign));
                    // the roster signs are drawn into the left 49 of their 64 pixels
                    x += (small ? 49f : Sign) + 6f;
                }
            }
            if (!string.IsNullOrEmpty(entry.Hero))
            {
                var slot = UpgradeUi.BuildSlot(name + ".Face", _list, new Vector2(x + 10f, y + Mathf.Floor((Box.y - UpgradeUi.SlotSize) * 0.5f)));
                slot.Back.raycastTarget = false;
                slot.Show(HeroNames.Portrait(entry.Hero));
                _faces.Add(new KeyValuePair<UpgradeUi.Slot, string>(slot, entry.Hero));
                x += 10f + UpgradeUi.SlotSize + 10f;
            }
            else x += 8f;

            var width = corner.x + Box.x - BoxInset - 6f - x;
            var label = Dd1Ui.Block(name + ".Text", _list, "town_activity_log_entry", new Vector2(x, y + BoxInset), new Vector2(width, Box.y - 2f * BoxInset), TextAlignmentOptions.Left, null, 13f);
            label.text = entry.Text;
            return y + Box.y;
        }

        // The goals as measured on DD1's own screen (a frame of week 1; the list's window at 884, 292): the
        // heading's line begins 12 px down the window, at its left edge; every goal is a row with DD1's check box
        // (shared/menu/menu.check_box.png, 32 px) at the window's left edge and its words entry_spacing.x after
        // the box, their line's top on the box's; a row follows the last entry_spacing.y under it (boxes at 377,
        // 419, 461: 42 apart; the first one 10 under the heading's 63 px line).
        // GUESS: a goal that is met wears shared/menu/menu.check_mark.png in its box (the box's twin in DD1's
        // files; no frame shows a goal met), and the second heading follows the first list as a row would.
        private const float GoalsTop = 12f, GoalHeading = 63f, GoalBox = 32f;
        private const string BoxArt = "shared/menu/menu.check_box.png", MarkArt = "shared/menu/menu.check_mark.png";

        private void FillGoals(List<ActivityLog.Goal> quests, List<ActivityLog.Goal> roster)
        {
            UpgradeUi.Clear(_goals);
            var y = GoalsTop;
            y = GoalList("Quests", WindowText.Plain("str_caretaker_goals_quest_goals_heading") ?? "Quest Goals", quests, y);
            y = GoalList("Roster", WindowText.Plain("str_caretaker_goals_roster_goals_heading") ?? "Roster Goals", roster, y);
            _goals.sizeDelta = new Vector2(0f, y);
        }

        private float GoalList(string name, string heading, List<ActivityLog.Goal> goals, float y)
        {
            if (goals.Count == 0) return y;
            var title = Dd1Ui.Line(name, _goals, "caretaker_goal_type_heading", new Vector2(0f, y), new Vector2(_goalWidth, GoalHeading + 1f));
            title.text = heading;
            y += GoalHeading + _goalGap;
            var textLeft = GoalBox + _goalIndent;
            var textWidth = _goalWidth - textLeft;
            var box = new Vector2(GoalBox, GoalBox);
            for (var i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                Dd1Ui.Art(name + i + ".Box", _goals, BoxArt, new Vector2(0f, y), box);
                if (goal.Done) Dd1Ui.Art(name + i + ".Mark", _goals, MarkArt, new Vector2(0f, y), box);
                var label = Dd1Ui.Block(name + i, _goals, "caretaker_goal_entry", new Vector2(textLeft, y), new Vector2(textWidth, LineHeight), TextAlignmentOptions.TopLeft,
                    Dd1Fonts.Colour(goal.Done ? "caretaker_goal_completed" : "caretaker_goal_not_completed", goal.Done ? UiKit.Notable : UiKit.Neutral));
                label.text = goal.Text;
                label.enableAutoSizing = false;
                var height = Mathf.Max(GoalBox, Mathf.Ceil(label.GetPreferredValues(goal.Text, textWidth, 0f).y));
                ((RectTransform)label.transform).sizeDelta = new Vector2(textWidth, height);
                y += height + _goalGap;
            }
            return y;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            var view = _instance;
            return new
            {
                open = true, week = view._week, weeks = view._weeks.Count, weeksBuilt = view._nextWeek, rows = view._list.childCount, listHeight = view._list.rect.height,
                window = ((RectTransform)view._scroll.transform).rect.height, at = view._scroll.verticalNormalizedPosition, faces = view._faces.Count,
                goals = view._goals.childCount
            };
        }

        /// <summary>For tests: what the scroll arrows do; lines above 0 go down the log.</summary>
        public static bool ScrollBy(int steps)
        {
            if (!IsOpen) return false;
            for (var i = 0; i < Mathf.Abs(steps); i++) _instance.Scroll(steps > 0 ? 1 : -1);
            return true;
        }
    }
}
