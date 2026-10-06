using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Code.Actor;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    [EstateModule]
    internal static class BuildingPanels
    {
        private static void Register()
        {
            foreach (var id in ActivityRules.BuildingIds)
            {
                var building = id;
                Buildings.Register(building, () => BuildingPanel.Open(building));
            }
        }
    }

    /// <summary>
    /// The Tavern and Abbey screens, laid out like DD1's building screen with the art of the player's DD1
    /// install (<see cref="RosterWindow"/> frames it): one row per activity on the shelves drawn in the
    /// backdrop: its name between the two gold rules, DD1's line about it, and its slots; a hero standing in one
    /// unpaid has the price above them. As in DD1 a hero is dragged from the roster column into a slot (and gets there in no other
    /// way), the check mark under the slot pays, and until the week ends the red cross under a paid slot
    /// takes the hero out again. The slot the Caretaker has taken for the week shows his face, as in DD1, and
    /// takes nobody (<see cref="ActivityLedger.CaretakerAt"/>). Last week's results stand on the window's line
    /// of text.
    ///
    /// Every position is DD1's: campaign/town/buildings/building.layout.darkest (the list of activities starts
    /// at body_base_pos + base_pos, a row every activity_spacing; name, description and slots inside a row;
    /// price, overlays and buttons of a slot) and campaign/town/hero_slot/hero_slot.layout.darkest (the hero's
    /// name over a slot). tools/preview_windows.py draws the same layout offline: change one, change the other.
    /// </summary>
    internal class BuildingPanel : MonoBehaviour
    {
        private const string BuildingsDir = UpgradeUi.BuildingsDir;
        private const float NameWidth = 236f;           // between the gold rules drawn in the backdrop (x 673..915)
        private static readonly Vector2 ConfirmSize = new Vector2(64f, 32f);    // hero_activity.confirm_button.png
        private static readonly Vector2 CancelSize = new Vector2(70f, 38f);     // hero_activity.cancel_button.png

        private class SlotView
        {
            public int Index;
            public Vector2 At;
            public UpgradeUi.Slot Slot;
            public HeroSlots.HandFrames Hand;
            public Image Resident, Overlay, Boards, EventArt, Confirm, Cancel;
            public RectTransform Cost;
            public string CostKey;
            public TextMeshProUGUI Hero, Free, Note;
            public uint Shown;      // the hero whose face the slot shows; 0 for nobody
        }

        private class RowView
        {
            public ActivityRules.Activity Activity;
            public TextMeshProUGUI Description;
            public readonly List<SlotView> Slots = new List<SlotView>();
        }

        private static BuildingPanel _instance;

        private readonly List<RowView> _rows = new List<RowView>();
        // Heroes standing in a slot whose price has not been paid yet: activity id and slot to hero.
        private readonly Dictionary<string, uint> _pending = new Dictionary<string, uint>();
        private RosterWindow _window;
        private string _building;
        private Transform _costs, _leaving;
        private Vector2 _costOffset, _freeOffset;

        public static bool IsOpen => _instance != null && RosterWindow.Current != null && RosterWindow.Current == _instance._window;

        public static void Open(string building)
        {
            if (ActivityRules.For(building).Count == 0)
            {
                Plugin.Log.LogWarning("Hamlet: the " + building + " has no activities to show (DD1 data not read)");
                return;
            }
            var window = RosterWindow.Open(building, ActivityText.Building(building), BuildingsDir + building + "/" + building);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<BuildingPanel>();
            _instance = panel;
            panel._window = window;
            panel._building = building;
            panel.Build(window.Frame);
            window.Refresh = panel.Refresh;
            window.HeroPicked = panel.OnHeroPicked;
            window.BackOut = panel.StepBack;
            panel.Refresh();
        }

        public static void Close()
        {
            if (IsOpen) RosterWindow.Close();
        }

        /// <summary>Opens or closes the upgrade pane of the building on show; false if there is none.</summary>
        public static bool ShowUpgrades(bool open)
        {
            if (!IsOpen || _instance._window.Upgrades == null) return false;
            _instance._window.Upgrades.SetOpen(open);
            return true;
        }

        private void OnEnable()
        {
            ActivityLedger.Changed += Refresh;
            UpgradeRules.Changed += Refresh;     // a built step changes prices, relief and slots at once
            HeroDrag.InHandChanged += ShowHand;
        }

        private void OnDisable()
        {
            ActivityLedger.Changed -= Refresh;
            UpgradeRules.Changed -= Refresh;
            HeroDrag.InHandChanged -= ShowHand;
        }

        // While a hero is in the player's hand every free slot says whether it would take them (HeroSlots.HandFrames).
        private void ShowHand()
        {
            var guid = HeroDrag.InHand;
            foreach (var row in _rows)
            {
                var open = ActivityRules.Slots(row.Activity);
                var refused = guid != 0u && ActivityLedger.Refusal(guid, row.Activity) != null;
                foreach (var slot in row.Slots)
                    slot.Hand?.Show(slot.Index < open && ActivityLedger.At(row.Activity, slot.Index) == null && !ActivityLedger.CaretakerAt(row.Activity, slot.Index), refused);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- construction --------------------------------------------------------------------------------

        private void Build(RectTransform art)
        {
            var dir = BuildingsDir + _building + "/" + _building;
            // FALLBACK numbers: DD1's own values of these entries, used where a layout file cannot be read.
            var layout = Dd1Ui.Layout(BuildingsDir + "building.layout.darkest");
            var slotLayout = Dd1Ui.Layout(UpgradeUi.SlotDir + "hero_slot.layout.darkest");
            const string list = "building_activity_list_layout", row = "building_activity_layout", slot = "building_activity_slot_layout";
            var origin = _window.Body + Dd1Ui.Offset(layout, list, "base_pos", 70f, 50f);
            var spacing = Dd1Ui.Offset(layout, list, "activity_spacing", 0f, 230f);
            var nameOffset = Dd1Ui.Offset(layout, row, "name_offset", 170f, 36f);
            var descriptionOffset = Dd1Ui.Offset(layout, row, "description_offset", 170f, 90f);
            var descriptionWidth = Dd1Ui.Number(layout, row, "description_width", 250f);
            var slotList = Dd1Ui.Offset(layout, row, "slot_list_pos", 440f, 119f);
            var slotSpacing = Dd1Ui.Offset(layout, row, "slot_spacing", 135f, 0f);
            var overlayOffset = Dd1Ui.Offset(layout, slot, "overlay_offset", -26f, -70f);
            var boardsOffset = Dd1Ui.Offset(layout, slot, "locked_overlay_offset", -42f, -70f);
            var eventOffset = Dd1Ui.Offset(layout, slot, "free_overlay_offset", -42f, -70f);
            _costOffset = Dd1Ui.Offset(layout, slot, "cost_offset", 38f, -20f);
            _freeOffset = Dd1Ui.Offset(layout, slot, "free_offset", 42f, 0f);
            var confirmOffset = Dd1Ui.Offset(layout, slot, "confirm_button_offset", 42f, 115f);
            var cancelOffset = Dd1Ui.Offset(layout, slot, "cancel_button_offset", 7f, 112f);
            var confirmTip = Dd1Ui.Offset(layout, slot, "confirm_button_tooltip_offset", 0f, 38f);
            var tipWidth = Dd1Ui.Number(layout, slot, "confirm_button_tooltip_text_width", 200f);
            var heroName = Dd1Ui.Offset(slotLayout, "town_hero_slot_layout", "name_offset", 45f, -70f);

            var activities = ActivityRules.For(_building);
            for (var i = 0; i < activities.Count; i++)
            {
                var activity = activities[i];
                var view = new RowView { Activity = activity };
                _rows.Add(view);
                var at = origin + spacing * i;

                Dd1Ui.Line("Name." + activity.Id, art, "town_activity_name", at + nameOffset, new Vector2(NameWidth, 40f)).text = ActivityText.Activity(activity.Id);
                view.Description = Dd1Ui.Block("Description." + activity.Id, art, "town_activity_description", at + descriptionOffset, new Vector2(descriptionWidth, 100f));

                for (var j = 0; j < activity.MaxSlots; j++)
                {
                    var place = at + slotList + slotSpacing * j;
                    var s = new SlotView { Index = j, At = place };
                    view.Slots.Add(s);
                    var id = activity.Id + j;

                    s.Slot = UpgradeUi.BuildSlot("Slot." + id, art, place);
                    // A slot takes a dragged hero and nothing else: a click on it does nothing, as in DD1.
                    UpgradeUi.Pointer(s.Slot.Back, null, () => OnSlotRightClicked(view, s),
                        () =>
                        {
                            if (Occupant(view, s) != 0u) return;
                            var words = ActivityLedger.CaretakerAt(activity, s.Index) ? CaretakerLine : WindowText.Plain("str_empty_hero_slot_" + activity.Id) ?? "Drag a hero here.";
                            _window.Tip(s, true, place + new Vector2(0f, UpgradeUi.SlotSize + 4f), null, words, tipWidth);
                        },
                        () => _window.Tip(s, false, Vector2.zero, null, null));
                    _window.AddDrop(s.Slot.Rect, guid => Stand(view, s, guid));
                    s.Hand = HeroSlots.BuildHandFrames("Hand." + id, art, place);
                    // The Caretaker's face fills the slot he has taken (DD1's portrait is the slot's own size).
                    s.Resident = Dd1Ui.Art("Resident." + id, art, UpgradeUi.SlotDir + "caretaker_portrait.png", place, new Vector2(UpgradeUi.SlotSize, UpgradeUi.SlotSize));

                    s.Overlay = Dd1Ui.Art("Overlay." + id, art, dir + "." + activity.Id + ".hero_slot_overlay.png", place + overlayOffset);
                    s.Boards = Dd1Ui.Art("Boards." + id, art, dir + ".locked_hero_slot_overlay.png", place + boardsOffset);
                    s.EventArt = UiKit.Image("Event." + id, art, null);
                    ((RectTransform)s.EventArt.transform).PlaceTopLeft(place + eventOffset, Dd1Ui.TopLeft, Vector2.zero);

                    s.Hero = Dd1Ui.Line("Hero." + id, art, "hero_slot_name", place + heroName, new Vector2(slotSpacing.x - 4f, 40f), TextAlignmentOptions.Top);
                    s.Free = Dd1Ui.Line("Free." + id, art, "town_free", place + _freeOffset, new Vector2(80f, 26f), TextAlignmentOptions.Top);
                    s.Free.text = WindowText.Plain("town_free") ?? "Free";

                    var confirmAt = place + new Vector2(confirmOffset.x - ConfirmSize.x * 0.5f, confirmOffset.y);
                    s.Confirm = Dd1Ui.Art("Confirm." + id, art, BuildingsDir + "hero_activity/hero_activity.confirm_button.png", confirmAt, ConfirmSize, new Color(0.25f, 0.3f, 0.1f), true);
                    UpgradeUi.Pointer(s.Confirm, () => Confirm(view, s), () => OnSlotRightClicked(view, s),
                        () => _window.Tip(s.Confirm, true, confirmAt + confirmTip, null, WindowText.Plain("str_hero_slot_unlocked_" + activity.Id) ?? "Confirm Treatment", tipWidth),
                        () => _window.Tip(s.Confirm, false, Vector2.zero, null, null));
                    var cancelAt = place + cancelOffset;
                    s.Cancel = Dd1Ui.Art("Cancel." + id, art, BuildingsDir + "hero_activity/hero_activity.cancel_button.png", cancelAt, CancelSize, new Color(0.35f, 0.05f, 0.04f), true);
                    UpgradeUi.Pointer(s.Cancel, () => TakeOut(view, s), null,
                        () => _window.Tip(s.Cancel, true, cancelAt + confirmTip, null, WindowText.Plain("str_hero_slot_locked_" + activity.Id) ?? "Cancel Treatment", tipWidth),
                        () => _window.Tip(s.Cancel, false, Vector2.zero, null, null));
                    s.Note = Dd1Ui.Line("Note." + id, art, "tooltip", place + new Vector2(UpgradeUi.SlotSize * 0.5f, cancelOffset.y + 4f), new Vector2(slotSpacing.x - 6f, 24f), TextAlignmentOptions.Top, UiKit.Harmful);
                }
            }
            _costs = UiKit.Stretch(UiKit.Rect("Costs", art));
            _leaving = UiKit.Stretch(UiKit.Rect("Leaving", art));
            // The pane was attached with the window, before the rows: it lies over the keeper's side again.
            if (_window.Upgrades != null) _window.Upgrades.transform.SetAsLastSibling();
        }

        private static string Key(RowView row, SlotView slot) => row.Activity.Id + "#" + slot.Index;

        // DD1's words for a slot the Caretaker sits in. FALLBACK: its own English, should the table be missing.
        private static string CaretakerLine => WindowText.Plain("str_cant_place_hero_here_caretaker") ?? "The Caretaker is currently enjoying this activity...";

        // Who stands in a slot: the hero staying there, or the one waiting for the check mark; 0 for nobody.
        private uint Occupant(RowView row, SlotView slot)
        {
            var stay = ActivityLedger.At(row.Activity, slot.Index);
            if (stay != null) return stay.Guid;
            return _pending.TryGetValue(Key(row, slot), out var guid) ? guid : 0u;
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void Refresh()
        {
            if (_window == null || _rows.Count == 0) return;
            var gold = EstateState.Gold;

            foreach (var row in _rows)
            {
                var activity = row.Activity;
                var open = ActivityRules.Slots(activity);
                var closed = TownEventHooks.ActivityClosed(activity.Id);
                // DD1's line about the activity and nothing more: what it relieves is left to be found out.
                row.Description.text = WindowText.Plain("town_activity_description_" + activity.Id) ?? "";

                foreach (var slot in row.Slots)
                {
                    var key = Key(row, slot);
                    var boarded = slot.Index >= open;
                    var stay = boarded ? null : ActivityLedger.At(activity, slot.Index);
                    var caretaker = !boarded && stay == null && ActivityLedger.CaretakerAt(activity, slot.Index);
                    if (boarded || stay != null || caretaker) _pending.Remove(key);
                    _pending.TryGetValue(key, out var waiting);
                    var hero = UpgradeUi.Hero(stay != null ? stay.Guid : waiting);
                    if (stay == null && waiting != 0u && (hero == null || ActivityLedger.Refusal(waiting, activity) != null))
                    {
                        // The hero left, died or was taken elsewhere behind the screen's back.
                        _pending.Remove(key);
                        waiting = 0u;
                        hero = null;
                    }
                    var price = waiting != 0u ? ActivityRules.CostFor(activity, waiting) : ActivityRules.Cost(activity, 0);
                    var free = !boarded && stay == null && price <= 0;

                    slot.Boards.gameObject.SetActive(boarded && !closed);
                    slot.Slot.Back.gameObject.SetActive(!boarded);
                    // A hero who leaves a slot (taken out, sent back, replaced by another) slides out of it, as in DD1.
                    var shown = hero != null ? hero.ActorGuid : 0u;
                    if (slot.Shown != 0u && slot.Shown != shown) HeroSlots.SlideOut(slot.Slot, _leaving);
                    slot.Shown = shown;
                    slot.Slot.Show(hero != null ? HeroNames.Portrait(hero) : null);
                    slot.Resident.gameObject.SetActive(caretaker);
                    slot.Overlay.gameObject.SetActive(stay != null);
                    // DD1's art of a week's event on a slot: the boards and chains of a closed activity, the lanterns of a free one.
                    var eventArt = closed && boarded ? Dd1Ui.Sprite(BuildingsDir + _building + "/" + _building + "." + activity.Id + ".locked_event.png")
                        : free ? Dd1Ui.Sprite(BuildingsDir + _building + "/" + _building + "." + activity.Id + ".free_event.png") : null;
                    slot.EventArt.gameObject.SetActive(eventArt != null);
                    if (eventArt != null)
                    {
                        slot.EventArt.sprite = eventArt;
                        ((RectTransform)slot.EventArt.transform).sizeDelta = eventArt.rect.size;
                    }
                    slot.Free.gameObject.SetActive(free && !caretaker);
                    slot.Hero.gameObject.SetActive(hero != null);
                    if (hero != null) slot.Hero.text = ActivityLedger.HeroName(hero);
                    slot.Confirm.gameObject.SetActive(stay == null && waiting != 0u);
                    slot.Confirm.color = new Color(1f, 1f, 1f, price <= gold ? 1f : 0.35f);
                    slot.Cancel.gameObject.SetActive(stay != null && !stay.Locked);
                    slot.Note.gameObject.SetActive(stay != null && stay.Locked);
                    if (stay != null && stay.Locked) slot.Note.text = "won't leave";
                    // DD1 names a price only over a hero who stands in the slot unpaid: a free slot is bare
                    ShowCost(slot, key, !boarded && stay == null && !free && !caretaker && waiting != 0u ? price : -1, price > gold);
                }
            }
            ShowHand();
            _window.Hint(Results());
        }

        // The price over an empty slot (and over a hero who has not been paid for), as DD1 writes it.
        private void ShowCost(SlotView slot, string name, int price, bool tooDear)
        {
            var key = price + (tooDear ? "!" : "");
            if (key == slot.CostKey) return;
            slot.CostKey = key;
            if (slot.Cost != null) Destroy(slot.Cost.gameObject);
            // cost_offset is the middle of the price: read so, it stands clear above the slot (and, in the
            // Sanitarium, above the check mark, as DD1's own screen shows it).
            slot.Cost = price > 0 ? UpgradeUi.BuildGoldPrice("Cost." + name, _costs, slot.At + _costOffset, price, tooDear) : null;
        }

        private string Results()
        {
            var lines = ActivityLedger.ResultsFor(_building);
            // DD1 writes no how-to on a building's screen: the line is for what happened, and for messages
            return lines.Count == 0 ? null : Dd1Ui.Tint("Last week:", "notable") + " " + string.Join("  ", lines);
        }

        // ---- input ---------------------------------------------------------------------------------------

        // DD1: a right click leaves. First the hero who has not been paid for, then the building.
        private void OnSlotRightClicked(RowView row, SlotView slot)
        {
            var stay = ActivityLedger.At(row.Activity, slot.Index);
            if (stay != null) UpgradeUi.ShowSheet(stay.Guid);
            else if (_pending.Remove(Key(row, slot))) _window.Say(null);
            else
            {
                RosterWindow.Close();
                return;
            }
            Refresh();
        }

        // A right click on the backdrop: the heroes who have not been paid for step out.
        private bool StepBack()
        {
            if (_pending.Count == 0) return false;
            _pending.Clear();
            _window.Say(null);
            Refresh();
            return true;
        }

        // A click on a roster row, or a hero let go beside every slot: nothing, as in DD1. The window asks for
        // the roster's heroes all the same, because that is what lets their rows be dragged.
        private void OnHeroPicked(uint guid)
        {
        }

        // The hero stands in the slot; nothing is paid until the check mark is clicked (DD1).
        private void Stand(RowView row, SlotView slot, uint guid)
        {
            var activity = row.Activity;
            var hero = UpgradeUi.Hero(guid);
            if (hero == null) return;
            string trouble = null;
            if (slot.Index >= ActivityRules.Slots(activity)) trouble = "That slot is not open.";
            else if (ActivityLedger.At(activity, slot.Index) != null) trouble = "That slot is taken.";
            else if (ActivityLedger.CaretakerAt(activity, slot.Index)) trouble = CaretakerLine;
            else
            {
                trouble = ActivityLedger.Refusal(guid, activity);
                if (trouble != null) trouble += ".";
            }
            if (trouble != null)
            {
                _window.Say(trouble, true);
                return;
            }
            // A hero waits in one slot at a time.
            var elsewhere = new List<string>();
            foreach (var pair in _pending)
                if (pair.Value == guid) elsewhere.Add(pair.Key);
            foreach (var key in elsewhere) _pending.Remove(key);
            _pending[Key(row, slot)] = guid;
            var price = ActivityRules.CostFor(activity, guid);
            _window.Say(ActivityLedger.HeroName(hero) + " waits at the " + ActivityText.Activity(activity.Id) + ": the check mark pays " + (price > 0 ? UpgradeUi.Amount(price) + " gold." : "nothing this week."), price > EstateState.Gold);
            Refresh();
        }

        private void Confirm(RowView row, SlotView slot)
        {
            if (!_pending.TryGetValue(Key(row, slot), out var guid)) return;
            var hero = UpgradeUi.Hero(guid);
            var error = ActivityLedger.Place(guid, row.Activity, slot.Index);
            if (error != null) _window.Say(error + ".", true);
            else
            {
                _pending.Remove(Key(row, slot));
                _window.Say((hero != null ? ActivityLedger.HeroName(hero) : "The hero") + " will spend the week at the " + ActivityText.Activity(row.Activity.Id) + ".");
            }
            _window.Tooltip.Hide(null);
            Refresh();
        }

        // The red cross under a paid slot. DD1 asks first: the price is not given back.
        private void TakeOut(RowView row, SlotView slot)
        {
            if (ActivityLedger.At(row.Activity, slot.Index) == null) return;
            _window.Tooltip.Hide(null);
            HeroSlots.AskToCancel(_building, () =>
            {
                // The week may have turned, or the window closed, while the question stood.
                var stay = ActivityLedger.At(row.Activity, slot.Index);
                if (stay == null) return;
                var error = ActivityLedger.Cancel(stay);
                if (this == null || _window == null) return;
                if (error != null) _window.Say(error + ".", true);
                else _window.Say(null);
                Refresh();
            });
        }

        /// <summary>
        /// For the test bridge: stands a hero in a slot of the open screen as a drop from the roster does.
        /// Nothing is paid. Returns what went wrong, or null.
        /// </summary>
        public static string StandForTest(string activityId, int slotIndex, uint guid)
        {
            if (!IsOpen) return "the screen is not open";
            var row = _instance._rows.Find(view => view.Activity.Id == activityId);
            if (row == null) return "no such activity in the " + _instance._building;
            if (slotIndex < 0 || slotIndex >= row.Slots.Count) return "no such slot";
            if (UpgradeUi.Hero(guid) == null) return "no such hero";
            _instance.Stand(row, row.Slots[slotIndex], guid);
            return _instance.Occupant(row, row.Slots[slotIndex]) == guid ? null : _instance._window.Said;
        }

        /// <summary>For tests: what the screen shows.</summary>
        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            var waiting = new List<object>();
            foreach (var pair in _instance._pending) waiting.Add(new { slot = pair.Key, guid = pair.Value });
            return new { open = true, building = _instance._building, waiting, window = _instance._window.Snapshot() };
        }
    }

    /// <summary>What the hero slots of the Tavern, the Abbey and the Sanitarium do alike.</summary>
    internal static class HeroSlots
    {
        /// <summary>
        /// DD1's question before a hero is taken out of a slot that has been paid for
        /// (str_hero_slot_locked_cancel_confirm: the cost is not refunded, the hero is free for quests at once),
        /// with its "Yes" and "No", in DD1's own dialog. <paramref name="yes"/> runs on "Yes".
        /// </summary>
        public static void AskToCancel(string building, Action yes)
        {
            // FALLBACK words: DD1's own English, used when its string table cannot be read.
            var question = WindowText.Plain("str_hero_slot_locked_cancel_confirm")
                           ?? "You will not be refunded the cost, but the hero will be immediately available for use on quests. Are you sure?";
            TownConfirm.Ask(question, WindowText.Plain("str_hero_slot_locked_cancel_confirm_yes") ?? "Yes", WindowText.Plain("str_hero_slot_locked_cancel_confirm_no") ?? "No", yes);
        }

        /// <summary>
        /// DD1's two frames on a slot while a hero is in the player's hand (<see cref="HeroDrag.InHand"/>):
        /// hero_slot.positive_frame.png, the gold frame of a free slot that would take the hero, and
        /// hero_slot.locked_for_hero.png, the red frame with a cross of a free slot whose activity is not for
        /// this hero. GUESS: which of the two shows when is read from their names; DD1's files load both (and
        /// hero_slot.negative_frame.png, which the Estate Map shows on a hero who refuses a quest) and say no more.
        /// </summary>
        public class HandFrames
        {
            public Image Accepts, Barred;

            /// <param name="free">The slot is open and empty.</param>
            /// <param name="refused">Its activity does not take the hero in hand.</param>
            public void Show(bool free, bool refused)
            {
                var hand = HeroDrag.InHand != 0u && free;
                if (Accepts.gameObject.activeSelf != (hand && !refused)) Accepts.gameObject.SetActive(hand && !refused);
                if (Barred.gameObject.activeSelf != (hand && refused)) Barred.gameObject.SetActive(hand && refused);
            }
        }

        /// <summary>The two frames over a slot whose corner is at <paramref name="slotAt"/>, hidden until a hero is in hand.</summary>
        public static HandFrames BuildHandFrames(string name, Transform parent, Vector2 slotAt)
        {
            var size = new Vector2(UpgradeUi.SlotSize, UpgradeUi.SlotSize);
            var frames = new HandFrames
            {
                Accepts = Dd1Ui.Art(name + ".Accepts", parent, UpgradeUi.SlotDir + "hero_slot.positive_frame.png", slotAt, size),
                Barred = Dd1Ui.Art(name + ".Barred", parent, UpgradeUi.SlotDir + "hero_slot.locked_for_hero.png", slotAt, size)
            };
            frames.Accepts.gameObject.SetActive(false);
            frames.Barred.gameObject.SetActive(false);
            return frames;
        }

        /// <summary>
        /// The face a slot shows leaves it the way DD1 plays it (<see cref="SlotSlideOut"/>). To be called
        /// before the slot is given its next face; <paramref name="layer"/> is where the leaving face is drawn
        /// meanwhile, over the slots.
        /// </summary>
        public static void SlideOut(UpgradeUi.Slot slot, Transform layer)
        {
            var portrait = slot.Portrait;
            if (portrait == null || !portrait.enabled || portrait.sprite == null || !portrait.gameObject.activeInHierarchy) return;
            // The slot's inside, which cuts the portrait off at the frame: a copy of it stays behind and slides.
            var leaving = UnityEngine.Object.Instantiate(portrait.transform.parent.gameObject, layer, true);
            leaving.name = "LeavingHero";
            leaving.AddComponent<SlotSlideOut>();
        }
    }

    /// <summary>
    /// A hero's face on its way out of a slot, after DD1's campaign/town/hero_slot/hero_slot.layout.darkest
    /// (replace_slide_out_anim): it moves by `offset` in `time` seconds on `easing_function` and is gone. `fade`
    /// is read as what is left of it at the end of the way (0.5: half seen).
    /// </summary>
    internal class SlotSlideOut : MonoBehaviour
    {
        private const string Anim = "replace_slide_out_anim";

        private static bool _read;
        private static float _time, _fade;
        private static Vector2 _offset;
        private static string _easing;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector2 _from;
        private float _started;

        private void Awake()
        {
            if (!_read)
            {
                _read = true;
                // FALLBACK numbers: DD1's own values of these entries, used when the file cannot be read.
                var layout = Dd1Ui.Layout(UpgradeUi.SlotDir + "hero_slot.layout.darkest");
                _time = Dd1Ui.Number(layout, Anim, "time", 0.2f);
                _offset = Dd1Ui.Offset(layout, Anim, "offset", 0f, -50f);
                _fade = Mathf.Clamp01(Dd1Ui.Number(layout, Anim, "fade", 0.5f));
                _easing = layout?.Find(Anim)?.String("easing_function") ?? "easeInOutSine";
            }
            _rect = (RectTransform)transform;
            _from = _rect.anchoredPosition;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _started = Time.unscaledTime;
        }

        private void Update()
        {
            var t = _time > 0f ? Mathf.Clamp01((Time.unscaledTime - _started) / _time) : 1f;
            var eased = Ease(t);
            // DD1's y runs down the screen.
            _rect.anchoredPosition = _from + new Vector2(_offset.x, -_offset.y) * eased;
            _group.alpha = Mathf.Lerp(1f, _fade, eased);
            if (t >= 1f) Destroy(gameObject);
        }

        // The easing functions DD1's town animations name.
        private static float Ease(float t)
        {
            switch (_easing)
            {
                case "easeInOutSine": return 0.5f * (1f - Mathf.Cos(Mathf.PI * t));
                case "easeInSine": return 1f - Mathf.Cos(Mathf.PI * 0.5f * t);
                case "easeOutSine": return Mathf.Sin(Mathf.PI * 0.5f * t);
                case "easeInQuad": return t * t;
                case "easeOutQuad": return t * (2f - t);
                case "easeInOutQuad": return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
                default: return t;
            }
        }
    }
}
