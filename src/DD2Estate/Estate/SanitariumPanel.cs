using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    internal static class SanitariumScreen
    {
        private static void Register()
        {
            Buildings.Register(SanitariumRules.Building, SanitariumPanel.Open);
        }
    }

    /// <summary>
    /// The Sanitarium, laid out like DD1's screen with the art of the player's DD1 install
    /// (<see cref="RosterWindow"/> frames it): the Treatment Ward and the Medical Ward as two rows (name
    /// between the gold rules, DD1's line about the ward, the cells on the shelves drawn in the backdrop). As
    /// in DD1 a hero is dragged from the roster column into a cell (and gets there in no other way): under the
    /// shelves the hero's good quirks are listed on the left and the bad ones on the right (or the diseases,
    /// in the Medical Ward) with DD2's names in DD1's colours; pointing at one shows DD2's description in
    /// DD1's tooltip, clicking picks it, the check mark in the frame admits the hero for the price shown above
    /// it, the only place DD1 names a price ("Free" in a week that makes the ward free). The red cross under
    /// a filled cell takes the hero out again, after DD1's question.
    ///
    /// DD1's marks beside an entry: the gold padlock (shared/character/lockquirk.png) on the good quirk picked
    /// to be locked in, as DD1's own picture of the screen has it (tutorial_popup.locking_pos_quirks.png), the
    /// grey one (sanitarium/lockedquirk.png) on one that is locked in already and greyed out with it; the red
    /// cross (remove_quirk_negative.png) on what is picked to go, and DD1's skull
    /// (shared/character/seriousquirk.png, tutorial_popup.permanent_neg_quirks.png) on a bad quirk that has set.
    ///
    /// tools/preview_windows.py draws the same layout offline: change one, change the other. All positions
    /// are DD1's: buildings/building.layout.darkest (the list of wards starts at body_base_pos + base_pos, a
    /// row every activity_spacing; overlays and buttons of a cell), hero_slot/hero_slot.layout.darkest (the
    /// name over a cell) and buildings/sanitarium/sanitarium.layout.darkest (offsets inside a row; the
    /// header, backdrop, lists, price and confirm button of the choice). That last file does not say what its
    /// `quirk_treatment` positions count from. Here the header and the backdrop count from the start of the
    /// list of wards and the lists, the price and the button from the first cell of the first ward, all
    /// anchored at their top centre: read that way the confirm button falls exactly into the frame drawn at
    /// the bottom of the backdrop, and the two lists and their tooltips mirror each other around its middle.
    /// </summary>
    internal class SanitariumPanel : MonoBehaviour
    {
        // Reference pixels, y down, relative to the backdrop.
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);
        private const float SlotSize = UpgradeUi.SlotSize;
        private const float NameWidth = 236f;           // between the gold rules drawn in the backdrop
        private const float EntryHeight = 28f;          // a list entry is as high as its pitch
        private const float EntryWidth = 190f;
        private const float IconSize = 32f;             // lockquirk.png, lockedquirk.png, seriousquirk.png, remove_quirk_negative.png
        private static readonly Vector2 BackdropSize = new Vector2(420f, 250f);
        private static readonly Vector2 HeaderSize = new Vector2(450f, 51f);
        private static readonly Vector2 HighlightSize = new Vector2(221f, 29f);
        private static readonly Vector2 ConfirmSize = new Vector2(64f, 32f);
        private static readonly Vector2 CancelSize = new Vector2(70f, 38f);

        private const string BuildingsDir = UpgradeUi.BuildingsDir;
        private const string Dir = BuildingsDir + SanitariumRules.Building + "/";
        private const string SheetDir = "shared/character/";
        private const string ColourTable = "colours/base.colours.darkest";

        private class SlotView
        {
            public int Index;
            public UpgradeUi.Slot Slot;
            public HeroSlots.HandFrames Hand;
            public Image Overlay, Bars, EventArt, Cancel;
            public TextMeshProUGUI Hero;
            public uint Shown;      // the hero whose face the cell shows; 0 for nobody
        }

        private class WardView
        {
            public SanitariumRules.Ward Ward;
            public readonly List<SlotView> Slots = new List<SlotView>();
        }

        // Where DD1 puts one of its three lists and what belongs to an entry of it.
        private class ListLayout
        {
            public Vector2 Position, IconOffset, HighlightOffset, TooltipOffset;
            public bool RightJustified;
            public string Highlight;
        }

        private class EntryView
        {
            public SanitariumRules.Option Option;
            public ListLayout List;
            public Vector2 Position;
            public Image Highlight, Icon;
            public TextMeshProUGUI Name;
        }

        private static SanitariumPanel _instance;
        private static readonly Dictionary<string, float> Shades = new Dictionary<string, float>();

        private readonly List<WardView> _wards = new List<WardView>();
        private readonly List<EntryView> _entries = new List<EntryView>();
        private readonly Dictionary<SanitariumRules.Treatment, ListLayout> _lists = new Dictionary<SanitariumRules.Treatment, ListLayout>();
        private RosterWindow _window;
        private RectTransform _art;
        private Transform _leaving;
        private Sprite _lockIcon, _lockedIcon, _severeIcon, _removeIcon;

        // The choice of a treatment under the shelves.
        private GameObject _choice;
        private Image _choiceBackdrop, _choiceHeader, _confirm;
        private RectTransform _entryRoot, _costRow;
        private TextMeshProUGUI _title, _action, _free;
        private Vector2 _lockTextPos, _removeTextPos, _costPos, _confirmTip, _hotSpot;
        private float _choicePitch, _tooltipWidth, _confirmTipWidth;
        private string _entriesShown, _costShown;

        private WardView _ward;             // the ward of the cell a hero is being admitted to
        private int _slot = -1;             // that cell
        private uint _patient;              // the hero standing at it, not yet paid for; 0 = nobody
        private string _chosen;             // id of the quirk picked for the patient
        private EntryView _hover;

        public static bool IsOpen => _instance != null && RosterWindow.Current != null && RosterWindow.Current == _instance._window;

        public static void Open()
        {
            if (SanitariumRules.Wards.Count == 0)
            {
                // Without DD1's rules there is no ward to run. DD2's own field hospital used to stand in: a shop
                // that sells DD2's items for relics into DD2's inventory, which the estate does not keep.
                Plugin.Log.LogWarning("Hamlet: the Sanitarium's wards could not be read from DD1; the Sanitarium stays shut");
                Narration.Say("The Sanitarium's wards could not be read from Darkest Dungeon's files; it stays shut.", Narration.Scope.Hamlet, "sanitarium");
                return;
            }
            var window = RosterWindow.Open(SanitariumRules.Building, ActivityText.Building(SanitariumRules.Building), Dir + SanitariumRules.Building);
            if (window == null) return;
            var panel = window.gameObject.AddComponent<SanitariumPanel>();
            _instance = panel;
            panel._window = window;
            panel._art = window.Frame;
            panel.BuildArt();
            window.Refresh = panel.Refresh;
            window.HeroPicked = panel.OnHeroClicked;
            window.BackOut = panel.BackOut;
            panel.Refresh();
        }

        public static void Close()
        {
            if (IsOpen) RosterWindow.Close();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- construction --------------------------------------------------------------------------------

        private void BuildArt()
        {
            var stem = Dir + SanitariumRules.Building;

            // Stock DD1 values, used only where a layout file cannot be read.
            var shared = Dd1Ui.Layout(BuildingsDir + "building.layout.darkest");
            var own = Dd1Ui.Layout(stem + ".layout.darkest");
            var slotLayout = Dd1Ui.Layout(UpgradeUi.SlotDir + "hero_slot.layout.darkest");
            const string row = "sanitarium_activity_layout", cell = "building_activity_slot_layout";
            var listOrigin = _window.Body + Dd1Ui.Offset(shared, "building_activity_list_layout", "base_pos", 70f, 50f);
            var spacing = Dd1Ui.Offset(shared, "building_activity_list_layout", "activity_spacing", 0f, 230f);
            var nameOffset = Dd1Ui.Offset(own, row, "name_offset", 170f, 42f);
            var descriptionOffset = Dd1Ui.Offset(own, row, "description_offset", 170f, 90f);
            var descriptionWidth = Dd1Ui.Number(own, row, "description_width", 250f);
            var slotList = Dd1Ui.Offset(own, row, "slot_list_pos", 440f, 119f);
            var slotSpacing = Dd1Ui.Offset(own, row, "slot_spacing", 135f, 0f);
            var overlayOffset = Dd1Ui.Offset(shared, cell, "overlay_offset", -26f, -70f);
            var barsOffset = Dd1Ui.Offset(shared, cell, "locked_overlay_offset", -42f, -70f);
            var eventOffset = Dd1Ui.Offset(shared, cell, "free_overlay_offset", -42f, -70f);
            var cancelOffset = Dd1Ui.Offset(shared, cell, "cancel_button_offset", 7f, 112f);
            var cancelTip = Dd1Ui.Offset(shared, cell, "confirm_button_tooltip_offset", 0f, 38f);
            var cancelTipWidth = Dd1Ui.Number(shared, cell, "confirm_button_tooltip_text_width", 200f);
            var heroName = Dd1Ui.Offset(slotLayout, "town_hero_slot_layout", "name_offset", 45f, -70f);

            _lockIcon = Dd1Ui.Sprite(SheetDir + "lockquirk.png");
            _lockedIcon = Dd1Ui.Sprite(Dir + "lockedquirk.png");
            _severeIcon = Dd1Ui.Sprite(SheetDir + "seriousquirk.png");
            _removeIcon = Dd1Ui.Sprite(Dir + "remove_quirk_negative.png");

            var wards = SanitariumRules.Wards;
            for (var i = 0; i < wards.Count; i++)
            {
                var ward = wards[i];
                var view = new WardView { Ward = ward };
                _wards.Add(view);
                var origin = listOrigin + spacing * i;

                Dd1Ui.Line("Name." + ward.Id, _art, "town_activity_name", origin + nameOffset, new Vector2(NameWidth, 40f)).text = ActivityText.Activity(ward.Id);
                // DD1's line about the ward and nothing more: it names a price only once a quirk is chosen.
                var about = Dd1Ui.Block("Description." + ward.Id, _art, "town_activity_description", origin + descriptionOffset, new Vector2(descriptionWidth, 100f));
                about.text = WindowText.Plain("town_activity_description_" + ward.Id) ?? "";

                for (var j = 0; j < ward.MaxSlots; j++)
                {
                    var at = origin + slotList + slotSpacing * j;
                    var slot = new SlotView { Index = j };
                    view.Slots.Add(slot);
                    var id = ward.Id + j;

                    slot.Slot = UpgradeUi.BuildSlot("Slot." + id, _art, at);
                    // A cell takes a dragged hero and nothing else: a click on it does nothing, as in DD1.
                    UpgradeUi.Pointer(slot.Slot.Back, null, () => { if (!BackOut()) RosterWindow.Close(); },
                        () => { if (SanitariumLedger.At(ward, slot.Index) == null && _patient == 0u) _window.Tip(slot, true, at + new Vector2(0f, SlotSize + 4f), null, WindowText.Plain("str_empty_hero_slot_treatment") ?? "Drag a hero here.", cancelTipWidth); },
                        () => _window.Tip(slot, false, Vector2.zero, null, null));
                    _window.AddDrop(slot.Slot.Rect, guid => OnHeroDropped(view, slot, guid));
                    slot.Hand = HeroSlots.BuildHandFrames("Hand." + id, _art, at);
                    slot.Overlay = Dd1Ui.Art("Overlay." + id, _art, stem + "." + ward.Id + ".hero_slot_overlay.png", at + overlayOffset);
                    slot.Bars = Dd1Ui.Art("Bars." + id, _art, stem + ".locked_hero_slot_overlay.png", at + barsOffset);
                    // DD1's art of a week that makes the ward free: the nurses beside the cell.
                    slot.EventArt = Dd1Ui.Art("Event." + id, _art, stem + "." + ward.Id + ".free_event.png", at + eventOffset);

                    slot.Hero = Dd1Ui.Line("SlotHero." + id, _art, "hero_slot_name", at + heroName, new Vector2(slotSpacing.x - 4f, 40f), TextAlignmentOptions.Top);

                    var cancelAt = at + cancelOffset;
                    slot.Cancel = Dd1Ui.Art("Cancel." + id, _art, BuildingsDir + "hero_activity/hero_activity.cancel_button.png", cancelAt, CancelSize, new Color(0.35f, 0.05f, 0.04f), true);
                    UpgradeUi.Pointer(slot.Cancel, () => TakeOut(view, slot), null,
                        () => _window.Tip(slot.Cancel, true, cancelAt + cancelTip, null, WindowText.Plain("str_hero_slot_locked_" + ward.Id) ?? "Cancel Treatment", cancelTipWidth),
                        () => _window.Tip(slot.Cancel, false, Vector2.zero, null, null));
                }
            }

            BuildChoice(own, listOrigin, listOrigin + slotList);
            _leaving = UiKit.Stretch(UiKit.Rect("Leaving", _art));
            // The pane was attached with the window, before the wards: it lies over the nurse's side again.
            if (_window.Upgrades != null) _window.Upgrades.transform.SetAsLastSibling();
        }

        // The header bar, the backdrop with its frame, the lists and what the frame holds. `listOrigin` is where
        // the list of wards starts, `firstCell` the first cell of the first ward.
        private void BuildChoice(DarkestFile own, Vector2 listOrigin, Vector2 firstCell)
        {
            const string treatment = "quirk_treatment", row = "sanitarium_activity_layout", cell = "sanitarium_activity_slot_layout";
            var backdropTop = listOrigin + Dd1Ui.Offset(own, treatment, "backdrop_centre_position", 500f, 500f);
            var headerTop = listOrigin + Dd1Ui.Offset(own, treatment, "header_backdrop_centre_position", 480f, 470f);
            var titleTop = listOrigin + Dd1Ui.Offset(own, treatment, "title_centre_position", 500f, 475f);
            // In the frame at the bottom of the backdrop: the price over the confirm button. shared_cost_pos is
            // the middle of the price: read so, it stands clear above the check mark, as DD1's own screen shows it.
            _costPos = firstCell + Dd1Ui.Offset(own, row, "shared_cost_pos", 62f, 570f);
            var freeTop = firstCell + Dd1Ui.Offset(own, row, "shared_free_pos", 62f, 554f);
            var confirmTop = firstCell + Dd1Ui.Offset(own, row, "shared_confirm_pos", 62f, 588f);
            _confirmTip = confirmTop + Dd1Ui.Offset(own, row, "shared_confirm_tooltip_offset", 0f, 46f);
            _confirmTipWidth = Dd1Ui.Number(own, row, "shared_confirm_tooltip_text_width", 350f);
            _choicePitch = Dd1Ui.Offset(own, cell, "choice_spacing", 0f, 28f).y;
            _hotSpot = Dd1Ui.Offset(own, cell, "choice_hot_spot_size", 120f, 20f);
            _tooltipWidth = Dd1Ui.Number(own, cell, "choice_tooltip_text_width", 200f);
            var tipLeft = Dd1Ui.Offset(own, cell, "choice_tooltip_left_justified_offset", 350f, 0f);
            var tipRight = Dd1Ui.Offset(own, cell, "choice_tooltip_right_justified_offset", -330f, 0f);
            // "Lock <quirk>" / "Remove <quirk>": DD1 writes what the check mark will do beside the mask, over the backdrop's rule.
            _lockTextPos = backdropTop + Dd1Ui.Offset(own, cell, "choice_remove_quirk_right_text_pos", -115f, 156f);
            _removeTextPos = backdropTop + Dd1Ui.Offset(own, cell, "choice_remove_quirk_left_text_pos", 130f, 156f);

            _lists.Clear();
            _lists[SanitariumRules.Treatment.Lock] = new ListLayout
            {
                Position = firstCell + Dd1Ui.Offset(own, treatment, "positive_list_position", -230f, 400f),
                IconOffset = Dd1Ui.Offset(own, treatment, "positive_icon_offset", -37f, -2f),
                HighlightOffset = Dd1Ui.Offset(own, treatment, "positive_selection_backdrop_offset", -50f, 0f),
                TooltipOffset = tipLeft, Highlight = Dir + "posquirk_highlight.png"
            };
            _lists[SanitariumRules.Treatment.Remove] = new ListLayout
            {
                Position = firstCell + Dd1Ui.Offset(own, treatment, "negative_list_position", 330f, 400f),
                IconOffset = Dd1Ui.Offset(own, treatment, "negative_icon_offset", 5f, -2f),
                HighlightOffset = Dd1Ui.Offset(own, treatment, "negative_selection_backdrop_offset", -170f, 0f),
                TooltipOffset = tipRight, Highlight = Dir + "negquirk_highlight.png", RightJustified = true
            };
            _lists[SanitariumRules.Treatment.Cure] = new ListLayout
            {
                Position = firstCell + Dd1Ui.Offset(own, treatment, "disease_list_position", -90f, 410f),
                IconOffset = Dd1Ui.Offset(own, treatment, "disease_icon_offset", -37f, -2f),
                HighlightOffset = Dd1Ui.Offset(own, treatment, "disease_selection_backdrop_offset", -50f, 0f),
                TooltipOffset = tipLeft, Highlight = Dir + "disease_highlight.png"
            };

            var root = UiKit.Stretch(UiKit.Rect("Choice", _art));
            _choice = root.gameObject;
            _choiceBackdrop = UiKit.Image("Backdrop", root, null);
            ((RectTransform)_choiceBackdrop.transform).PlaceTopLeft(backdropTop, TopCentre, BackdropSize);
            _choiceHeader = UiKit.Image("Header", root, null);
            ((RectTransform)_choiceHeader.transform).PlaceTopLeft(headerTop, TopCentre, HeaderSize);
            _title = Dd1Ui.Line("Title", root, "town_activity_name", new Vector2(titleTop.x, headerTop.y + (HeaderSize.y - 40f) * 0.5f), new Vector2(420f, 40f), TextAlignmentOptions.Top,
                Dd1Fonts.Colour("sanitarium_treatment_header", UiKit.Notable));

            _entryRoot = UiKit.Stretch(UiKit.Rect("Entries", root));
            _action = Dd1Ui.Line("Action", root, "town_choice_activity", _lockTextPos, new Vector2(EntryWidth + 20f, 26f), TextAlignmentOptions.TopLeft, Dd1Fonts.Colour("town_choice_activity_choice", UiKit.Notable));

            // What stands over the check mark: the price of what was chosen (built when it is), or DD1's "Free".
            _free = Dd1Ui.Line("Free", root, "town_free", freeTop, new Vector2(80f, 26f), TextAlignmentOptions.Top);
            _free.text = WindowText.Plain("town_free") ?? "Free";
            var confirmSprite = Dd1Ui.Sprite(BuildingsDir + "hero_activity/hero_activity.confirm_button.png");
            _confirm = UiKit.Image("Confirm", root, confirmSprite, confirmSprite != null ? Color.white : new Color(0.25f, 0.3f, 0.1f));
            ((RectTransform)_confirm.transform).PlaceTopLeft(confirmTop, TopCentre, ConfirmSize);
            UpgradeUi.Pointer(_confirm, OnConfirm, () => BackOut(),
                () => _window.Tip(_confirm, true, _confirmTip - new Vector2(ConfirmSize.x * 0.5f, 0f), null, WindowText.Plain("str_hero_slot_unlocked_treatment") ?? "Commit hero for treatment.", _confirmTipWidth),
                () => _window.Tip(_confirm, false, Vector2.zero, null, null));
            _choice.SetActive(false);
        }

        /// <summary>Opens or closes the upgrade pane while the screen is up; false if there is none.</summary>
        public static bool ShowUpgrades(bool open)
        {
            if (!IsOpen || _instance._window.Upgrades == null) return false;
            _instance._window.Upgrades.SetOpen(open);
            return true;
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void OnEnable()
        {
            SanitariumLedger.Changed += Refresh;
            UpgradeRules.Changed += Refresh;     // a built step changes prices, chances and cells at once
            HeroDrag.InHandChanged += ShowHand;
        }

        private void OnDisable()
        {
            SanitariumLedger.Changed -= Refresh;
            UpgradeRules.Changed -= Refresh;
            HeroDrag.InHandChanged -= ShowHand;
        }

        // While a hero is in the player's hand every free cell says whether it would take them (HeroSlots.HandFrames).
        private void ShowHand()
        {
            var guid = HeroDrag.InHand;
            foreach (var row in _wards)
            {
                var open = SanitariumRules.Slots(row.Ward);
                var refused = guid != 0u && SanitariumLedger.Refusal(guid, row.Ward) != null;
                foreach (var slot in row.Slots)
                    slot.Hand?.Show(slot.Index < open && SanitariumLedger.At(row.Ward, slot.Index) == null && !(row == _ward && slot.Index == _slot && _patient != 0u), refused);
            }
        }

        private void Refresh()
        {
            if (_window == null || _choice == null) return;
            var gold = EstateState.Gold;

            // The cell may have been closed or filled, the patient taken elsewhere, behind the panel's back.
            if (_ward != null && (_slot >= SanitariumRules.Slots(_ward.Ward) || SanitariumLedger.At(_ward.Ward, _slot) != null)) DropPatient();
            if (_patient != 0u && SanitariumLedger.Refusal(_patient, _ward.Ward) != null) DropPatient();
            var patient = _patient != 0u ? SanitariumLedger.Actor(_patient) : null;

            foreach (var row in _wards)
            {
                var open = SanitariumRules.Slots(row.Ward);
                var free = TownEventHooks.ActivityPriceFactor(row.Ward.Id) <= 0f;

                foreach (var slot in row.Slots)
                {
                    var barred = slot.Index >= open;
                    var stay = barred ? null : SanitariumLedger.At(row.Ward, slot.Index);
                    var admitting = !barred && stay == null && row == _ward && slot.Index == _slot;
                    var actor = stay != null ? SanitariumLedger.Actor(stay.Guid) : admitting ? patient : null;

                    slot.Bars.gameObject.SetActive(barred);
                    slot.Slot.Back.gameObject.SetActive(!barred);
                    // A hero who leaves a cell (taken out, sent back, treated) slides out of it, as in DD1.
                    var shown = actor != null ? actor.ActorGuid : 0u;
                    if (slot.Shown != 0u && slot.Shown != shown) HeroSlots.SlideOut(slot.Slot, _leaving);
                    slot.Shown = shown;
                    slot.Slot.Show(actor != null ? HeroNames.Portrait(actor) : null, admitting);
                    slot.Overlay.gameObject.SetActive(stay != null);
                    slot.EventArt.gameObject.SetActive(free && !barred && stay == null);
                    slot.Hero.gameObject.SetActive(actor != null);
                    // While a hero is being admitted the header bar lies where the second ward's buttons are.
                    slot.Cancel.gameObject.SetActive(stay != null && patient == null);
                    if (actor != null) slot.Hero.text = ActivityLedger.HeroName(actor);
                }
            }

            ShowHand();
            RefreshChoice(patient, gold);
            _window.Hint(Results());
        }

        private static string Results()
        {
            var lines = SanitariumLedger.LastResults();
            // DD1 writes no how-to on a building's screen: the line is for what happened, and for messages
            return lines.Count == 0 ? null : Dd1Ui.Tint("Last week:", "notable") + " " + string.Join("  ", lines);
        }

        private void RefreshChoice(ActorInstance patient, int gold)
        {
            var choosing = patient != null;
            if (_choice.activeSelf != choosing) _choice.SetActive(choosing);
            if (!choosing)
            {
                _hover = null;
                return;
            }

            var ward = _ward.Ward;
            var diseases = ward.TreatsDiseases && !ward.TreatsQuirks;
            var options = SanitariumRules.Options(patient, ward);

            // The lists follow the hero's quirks; rebuilt only when something about them changed.
            var shown = new StringBuilder().Append(_patient).Append('@').Append(ward.Id);
            foreach (var option in options) shown.Append('|').Append(option.Id).Append(option.Quirk.IsLocked() ? "!" : "").Append(option.Cost).Append(option.Blocked);
            if (shown.ToString() != _entriesShown)
            {
                _entriesShown = shown.ToString();
                RebuildEntries(patient, options);
            }

            var backdrop = Dd1Ui.Sprite(Dir + (diseases ? "disease_treatment_backdrop.png" : "quirk_treatment_backdrop.png"));
            _choiceBackdrop.sprite = backdrop;
            _choiceBackdrop.color = backdrop != null ? Color.white : new Color(0.03f, 0.03f, 0.03f, 0.9f);
            var header = Dd1Ui.Sprite(Dir + (diseases ? "diseaseheader.png" : "quirkheader.png"));
            _choiceHeader.sprite = header;
            _choiceHeader.color = header != null ? Color.white : new Color(0.1f, 0.1f, 0.1f);

            EntryView chosen = null;
            foreach (var entry in _entries)
            {
                var selected = entry.Option.Id == _chosen;
                if (selected) chosen = entry;
                entry.Highlight.gameObject.SetActive(selected || entry == _hover);
                if (entry.Highlight.sprite != null) entry.Highlight.color = new Color(1f, 1f, 1f, selected ? 1f : 0.55f);
                entry.Name.color = EntryColour(entry.Option, selected);
                var mark = Mark(entry.Option, selected);
                entry.Icon.sprite = mark;
                entry.Icon.gameObject.SetActive(mark != null);
            }
            if (chosen == null) _chosen = null;

            // DD1 heads the choice "Quirks" (or "Diseases").
            _title.text = WindowText.Plain(diseases ? "str_diseases" : "str_quirks") ?? (diseases ? "Diseases" : "Quirks");

            var affordable = chosen != null && chosen.Option.Cost <= gold;
            ShowCost(chosen != null ? chosen.Option.Cost : -1, !affordable);
            _action.gameObject.SetActive(chosen != null);
            _confirm.color = new Color(1f, 1f, 1f, affordable ? 1f : 0.35f);
            if (chosen == null) return;
            // "Lock Tough" to the left of the mask, "Remove Known Cheat" to its right, in DD1's words.
            var locking = chosen.Option.Kind == SanitariumRules.Treatment.Lock;
            var quirk = SanitariumRules.PlainQuirkName(chosen.Option.Quirk.Definition, patient);
            _action.text = WindowText.Format(locking ? "str_sanitarium_lock_quirk_format" : "str_sanitarium_remove_quirk_format", locking ? "Lock %s" : "Remove %s", quirk);
            var size = ((RectTransform)_action.transform).sizeDelta;
            _action.alignment = locking ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            ((RectTransform)_action.transform).PlaceTopLeft(locking ? _lockTextPos - new Vector2(size.x, 0f) : _removeTextPos, TopLeft, size);
        }

        // Over the check mark: the price of what is chosen, DD1's "Free" in a week that asks nothing; -1: nothing chosen.
        private void ShowCost(int price, bool tooDear)
        {
            _free.gameObject.SetActive(price == 0);
            var key = price + (tooDear ? "!" : "");
            if (key == _costShown) return;
            _costShown = key;
            if (_costRow != null) Destroy(_costRow.gameObject);
            _costRow = price > 0 ? UpgradeUi.BuildGoldPrice("Cost", _choice.transform, _costPos, price, tooDear) : null;
        }

        // DD1's mark beside an entry (see the class comment); null for none.
        private Sprite Mark(SanitariumRules.Option option, bool selected)
        {
            var set = option.Quirk.IsLocked();
            switch (option.Kind)
            {
                case SanitariumRules.Treatment.Lock: return selected ? _lockIcon : set ? _lockedIcon : null;
                case SanitariumRules.Treatment.Remove: return selected ? _removeIcon : set ? _severeIcon : null;
                default: return selected ? _removeIcon : null;
            }
        }

        // DD1's colours of an entry (colours/base.colours.darkest: sanitarium_treatment_*): one per list, a
        // brighter green for the disease that is picked, and grey for a good quirk that cannot be locked in
        // (locked in already, or as many locked in as a hero may have).
        private static Color EntryColour(SanitariumRules.Option option, bool selected)
        {
            switch (option.Kind)
            {
                case SanitariumRules.Treatment.Lock:
                    if (option.Blocked != null) return Shade(option.Quirk.IsLocked() ? "sanitarium_treatment_positive_entry_locked" : "sanitarium_treatment_max_locked");
                    return Dd1Fonts.Colour(selected ? "sanitarium_treatment_positive_entry_selected" : "sanitarium_treatment_positive_entry", UiKit.Notable);
                case SanitariumRules.Treatment.Remove:
                    return Dd1Fonts.Colour(selected ? "sanitarium_treatment_negative_entry_selected" : option.Quirk.IsLocked() ? "sanitarium_treatment_negative_entry_locked" : "sanitarium_treatment_negative_entry", UiKit.Harmful);
                default:
                    // FALLBACK: DD1's two greens, should the table be missing.
                    return selected ? Dd1Fonts.Colour("sanitarium_treatment_disease_entry_selected", new Color32(173, 201, 98, 255)) : Dd1Fonts.Colour("sanitarium_treatment_disease_entry", new Color32(121, 141, 69, 255));
            }
        }

        // A DD1 colour that is a `.darkness` and nothing else: white text darkened to that share.
        private static Color Shade(string id)
        {
            if (!Shades.TryGetValue(id, out var share))
            {
                share = 0.4f;       // FALLBACK: DD1's stock value of both entries used here
                var table = Dd1Install.Found ? Dd1Install.ReadText(ColourTable) : null;
                var match = table != null ? Regex.Match(table, "\\.id\\s+\"" + Regex.Escape(id) + "\"\\s+\\.darkness\\s+([0-9.]+)") : Match.Empty;
                if (match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var read)) share = Mathf.Clamp01(read);
                Shades[id] = share;
            }
            return new Color(share, share, share);
        }

        private void RebuildEntries(ActorInstance patient, List<SanitariumRules.Option> options)
        {
            UpgradeUi.Clear(_entryRoot);
            _entries.Clear();
            _hover = null;
            _window.Tooltip.Hide(null);
            var counts = new Dictionary<ListLayout, int>();
            foreach (var option in options)
            {
                var list = _lists[option.Kind];
                counts.TryGetValue(list, out var index);
                counts[list] = index + 1;
                var entry = new EntryView { Option = option, List = list, Position = list.Position + new Vector2(0f, _choicePitch * index) };
                _entries.Add(entry);

                entry.Highlight = Dd1Ui.Art("Highlight." + option.Id, _entryRoot, list.Highlight, entry.Position + list.HighlightOffset, HighlightSize, new Color(0.3f, 0.26f, 0.18f, 0.6f));
                entry.Icon = UiKit.Image("Icon." + option.Id, _entryRoot, null);
                ((RectTransform)entry.Icon.transform).PlaceTopLeft(entry.Position + list.IconOffset, TopLeft, new Vector2(IconSize, IconSize));
                // The name carries no colour of the game's: DD1's colour of the list and the entry's state is the label's.
                entry.Name = Dd1Ui.Line("Name." + option.Id, _entryRoot, "town_choice_activity", entry.Position + new Vector2(0f, 1f), new Vector2(EntryWidth, EntryHeight),
                    list.RightJustified ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft, EntryColour(option, false));
                entry.Name.text = SanitariumRules.PlainQuirkName(option.Quirk.Definition, patient);

                // DD1's hot spot of an entry (choice_hot_spot_size), from where the name begins (the right-hand
                // list's names end at their place: its spots end there).
                var spot = UiKit.Image("Spot." + option.Id, _entryRoot, null, new Color(0f, 0f, 0f, 0f));
                ((RectTransform)spot.transform).PlaceTopLeft(list.RightJustified ? entry.Position - new Vector2(_hotSpot.x, 0f) : entry.Position, TopLeft, _hotSpot);
                UpgradeUi.Pointer(spot, () => OnEntryClicked(entry), () => BackOut(), () => OnEntryHover(entry, true), () => OnEntryHover(entry, false));
            }
        }

        // ---- input ---------------------------------------------------------------------------------------

        // The red cross under a cell with a patient. DD1 asks first: the price is not given back.
        private void TakeOut(WardView row, SlotView slot)
        {
            if (SanitariumLedger.At(row.Ward, slot.Index) == null) return;
            _window.Tooltip.Hide(null);
            HeroSlots.AskToCancel(SanitariumRules.Building, () =>
            {
                // The week may have turned, or the window closed, while the question stood.
                var stay = SanitariumLedger.At(row.Ward, slot.Index);
                if (stay == null) return;
                var error = SanitariumLedger.Cancel(stay);
                if (this == null || _window == null) return;
                if (error != null) _window.Say(error + ".", true);
                else _window.Say(null);
                Refresh();
            });
        }

        // A click on a roster row, or a hero let go beside every cell: nothing, as in DD1. The window asks for
        // the roster's heroes all the same, because that is what lets their rows be dragged.
        private void OnHeroClicked(uint guid)
        {
        }

        // A hero dragged from the roster onto a cell.
        private void OnHeroDropped(WardView row, SlotView slot, uint guid)
        {
            var trouble = slot.Index >= SanitariumRules.Slots(row.Ward) || SanitariumLedger.At(row.Ward, slot.Index) != null ? "That cell is not free" : SanitariumLedger.Refusal(guid, row.Ward);
            if (trouble != null) _window.Say(trouble + ".", true);
            else Admit(row, slot.Index, guid);
            _window.Tooltip.Hide(null);
            Refresh();
        }

        // The hero stands at the cell; nothing is paid until a treatment is confirmed.
        private void Admit(WardView row, int slot, uint guid)
        {
            DropPatient();
            _ward = row;
            _slot = slot;
            _patient = guid;
            _window.Say(null);
            // With a single thing to treat there is nothing to choose (the usual case in the Medical Ward).
            var options = SanitariumRules.Options(SanitariumLedger.Actor(guid), row.Ward);
            if (options.Count == 1 && options[0].Blocked == null) _chosen = options[0].Id;
        }

        private void OnEntryClicked(EntryView entry)
        {
            if (entry.Option.Blocked != null) _window.Say(entry.Option.Blocked + ".", true);
            else
            {
                _chosen = entry.Option.Id == _chosen ? null : entry.Option.Id;
                _window.Say(null);
            }
            Refresh();
        }

        private void OnEntryHover(EntryView entry, bool on)
        {
            if (on) _hover = entry;
            else if (_hover == entry) _hover = null;
            ShowTooltip(_hover);
            Refresh();
        }

        private void OnConfirm()
        {
            if (_patient == 0u || _ward == null) return;
            if (_chosen == null) _window.Say("Choose what to treat first.", true);
            else
            {
                // The ledger's own notice of the admission refreshes this screen, which lets the patient go: kept here.
                var ward = _ward.Ward;
                var guid = _patient;
                var actor = SanitariumLedger.Actor(guid);
                var error = SanitariumLedger.Place(guid, ward, _slot, _chosen);
                if (error != null) _window.Say(error + ".", true);
                else
                {
                    var stay = SanitariumLedger.StayOf(guid);
                    DropPatient();
                    _window.Say((actor != null ? ActivityLedger.HeroName(actor) : "The hero") + " is admitted to the " + ActivityText.Activity(ward.Id)
                                + (stay != null ? (stay.Paid > 0 ? " for " + UpgradeUi.Amount(stay.Paid) + " gold. " : ". ") + SanitariumLedger.TreatmentText(stay) + ", when the week ends." : "."));
                }
            }
            _window.Tooltip.Hide(null);
            Refresh();
        }

        // DD1: a right click leaves. First the hero being admitted, then the building (false: nobody was left
        // to step back).
        private bool BackOut()
        {
            if (_patient == 0u) return false;
            DropPatient();
            _window.Say(null);
            _window.Tooltip.Hide(null);
            Refresh();
            return true;
        }

        private void DropPatient()
        {
            _ward = null;
            _slot = -1;
            _patient = 0u;
            _chosen = null;
            _hover = null;
        }

        // DD1's tooltip of an entry is what the quirk does and nothing else, on the far side of the entry: over
        // the other list, where it hides nothing of this one.
        private void ShowTooltip(EntryView entry)
        {
            var actor = entry != null && _patient != 0u ? SanitariumLedger.Actor(_patient) : null;
            var text = actor != null ? SanitariumRules.QuirkText(entry.Option.Quirk.Definition, actor) : null;
            if (string.IsNullOrEmpty(text))
            {
                _window.Tooltip.Hide(null);
                return;
            }
            var at = entry.Position + entry.List.TooltipOffset;
            // the box is as wide as its words (DD2's): of one that stands left of its place only the right edge is known
            if (entry.List.RightJustified) _window.Tooltip.ShowLeftOf(entry, at, null, text, _tooltipWidth);
            else _window.Tooltip.ShowAt(entry, at, null, text, _tooltipWidth);
        }

        // ---- for the test bridge -------------------------------------------------------------------------

        /// <summary>
        /// Does with the open screen what the mouse does: stands a hero at a cell as a drag from the roster
        /// does, then picks a quirk. Nothing is paid. A quirk left out (null) stops the walk before it.
        /// Returns what went wrong, or null.
        /// </summary>
        public static string Pick(string wardId, int slotIndex, uint guid, string quirkId)
        {
            if (!IsOpen) return "the screen is not open";
            var panel = _instance;
            var row = panel._wards.Find(view => view.Ward.Id == wardId);
            if (row == null) return "no such ward";
            if (slotIndex < 0 || slotIndex >= SanitariumRules.Slots(row.Ward)) return "That cell is not open";
            if (SanitariumLedger.At(row.Ward, slotIndex) != null) return "That cell is taken";
            panel.DropPatient();
            if (guid == 0u)
            {
                panel.Refresh();
                return "name a hero: a cell takes one by a drag, it is not picked by itself";
            }
            var error = SanitariumLedger.Refusal(guid, row.Ward);
            if (error == null)
            {
                panel.Admit(row, slotIndex, guid);
                if (quirkId != null)
                {
                    var option = SanitariumRules.OptionFor(SanitariumLedger.Actor(guid), row.Ward, quirkId);
                    error = option == null ? "no such quirk to treat there" : option.Blocked;
                    if (error == null) panel._chosen = quirkId;
                }
            }
            panel.Refresh();
            return error;
        }

        /// <summary>For tests: what the screen shows.</summary>
        public static object Snapshot()
        {
            if (!IsOpen) return new { open = false };
            var panel = _instance;
            var entries = new List<object>();
            foreach (var entry in panel._entries)
                entries.Add(new { quirk = entry.Option.Id, treatment = entry.Option.Kind.ToString().ToLowerInvariant(), cost = entry.Option.Cost, blocked = entry.Option.Blocked });
            return new
            {
                open = true,
                ward = panel._ward?.Ward.Id,
                slot = panel._slot,
                patient = panel._patient,
                chosen = panel._chosen,
                entries,
                said = panel._window.Said,
                upgrades = panel._window.Upgrades != null && panel._window.Upgrades.IsOpen
            };
        }
    }
}
