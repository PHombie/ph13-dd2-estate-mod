using System;
using Assets.Code.Actor;
using Assets.Code.Audio;
using Assets.Code.Combat.Presentation;
using Assets.Code.Game;
using Assets.Code.UI;
using Assets.Code.UI.Managers;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Core;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using HarmonyLib;
using UnityEngine;

namespace DD2Estate.Dungeon
{
    /// <summary>
    /// The party's bag in a fight (the owner, 2026-10-06: "in a fight add a button that switches between the
    /// skills and the inventory"). DD2's fight screen has a bar of the acting hero's skills; a button beside it
    /// puts the expedition's bag in the bar's place (DD1's raid inventory: eight cards by two) and the skills
    /// back. A fight starts on the skills, and so does every turn: the bag goes away with the bar (a skill is
    /// played, the turn is over, an enemy acts) and the next hero's bar is the skills again; choosing a skill
    /// with its key brings the skills back too. Using an item is no action of the turn (DD1), so the bag
    /// stays up while the hero uses what they need. TAB, DD1's key for map and inventory, does what the button
    /// does (DD2 binds that key only to its driving map: seen in the running game's input actions).
    ///
    /// DD2's bar is not touched: the bag is a canvas of the mod's own over it, there only while the bar is
    /// (<see cref="BarShown"/>: a hero's turn waits for the player, the bar's buttons are up, no menu, sheet or
    /// question of DD2's is open), with the bar's own alpha. While the skills show, nothing of the mod's lies
    /// over them but the button beside them.
    ///
    /// The rules are DD1's (<see cref="FightItemRules"/>), carried out by <see cref="DungeonRun.UseItemInFight"/>.
    /// Bridge: fightbag.state, fightbag.show, fightbag.use, fightbag.click, fightbag.hover (FightBagDev.cs).
    /// </summary>
    [EstateModule]
    internal static class FightBag
    {
        internal static ConfigEntry<bool> Enabled, OnTab, StandIns;

        /// <summary>
        /// The canvas' sorting order: over DD2's fight HUD (its CombatUI canvas has 1), under everything else DD2
        /// lays over a fight (barks 6, its screens 10, toasts 15, pop texts 25, menus 30, tooltips 100, the fader)
        /// and under the mod's own screens and curtains.
        /// </summary>
        internal static int Order = 2;

        private static FightBagView _view;
        private static bool _fight, _open, _failed;
        private static uint _openFor;
        private static float _openedAt, _nextFill;
        private static FightBagLayout _applied;
        private const float FillEvery = 0.1f;       // seconds between looks at what the acting hero could use

        // DD2's fight HUD (found when a fight begins; the objects live as long as the game does)
        private static CombatUiBhv _ui;
        private static CombatInterfaceBarUiBhv _bar;
        private static SkillSelectionBhv _skills;
        private static SkillButtonBhv _move, _pass;
        private static CombatPresentationBhv _presentation;
        private static CanvasGroup[] _groups = new CanvasGroup[0];
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>Dev bridge: numbers that take the place of the computed ones (NaN: computed).</summary>
        internal static float PlaceX = float.NaN, PlaceY = float.NaN, PlaceScale = float.NaN, PlaceButtonX = float.NaN, PlaceButtonY = float.NaN, PlaceButtonSize = float.NaN;
        internal static string PlaceIcon;

        /// <summary>DD2's skill bar is up for a hero whose turn waits for the player.</summary>
        public static bool BarShown { get; private set; }
        /// <summary>The bar's place shows the bag.</summary>
        public static bool Open => _open;
        public static bool InFight => _fight;
        internal static float BarAlpha { get; private set; }
        /// <summary>The bar's buttons on the 1920x1080 screen (y down), as last measured.</summary>
        internal static Rect BarRect { get; private set; }
        /// <summary>The side of one button's place in DD2's bar (100 on a 1920 screen), as last measured.</summary>
        internal static float BarPlace { get; private set; } = 100f;
        internal static FightBagView View => _view;
        internal static SkillSelectionBhv Dd2Skills => _skills;
        internal static CombatInterfaceBarUiBhv Dd2Bar => _bar;
        internal static CombatPresentationBhv Dd2Presentation => _presentation;

        /// <summary>The fight waits for the acting hero's choice: the one moment an item can be used.</summary>
        public static bool TurnIsOpen
        {
            get
            {
                if (_presentation == null) FindHud();
                return _presentation == null || _presentation.CurrentPresentationState == CombatPresentationState.IDLE;
            }
        }

        private static void Register()
        {
            Enabled = Plugin.Settings.Bind("Fight", "Bag", true,
                "In a fight of an expedition a button beside the skill bar shows the party's bag in the bar's place, and the skills again. The hero whose turn it is can use supplies and food from it, by Darkest Dungeon (1)'s rules.");
            OnTab = Plugin.Settings.Bind("Fight", "BagOnTab", true,
                "TAB (Darkest Dungeon (1)'s key for map and inventory) switches between the skills and the bag in a fight, like the button.");
            StandIns = Plugin.Settings.Bind("Fight", "BagStandIns", true,
                "In a fight a bandage, antivenom, herbs, holy water and laudanum also do what they do on a hero between fights (a share of health, a point of stress), beside what Darkest Dungeon (1) has them do in a fight (stop bleeding, blight, debuffs, horror; resistances). Off: Darkest Dungeon (1)'s effect alone.");
            FightItemRules.StandInsInAFight = StandIns.Value;
            StandIns.SettingChanged += (s, e) => FightItemRules.StandInsInAFight = StandIns.Value;

            var go = new GameObject("DD2Estate.FightBag.Watcher") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();
            FightBagDev.Register();
        }

        private sealed class Watcher : MonoBehaviour
        {
            private void Update()
            {
                try { Tick(); }
                catch (Exception e)
                {
                    // one line, not one a frame
                    if (!_failed) Plugin.Log.LogError("Fight bag: " + e);
                    _failed = true;
                }
            }
        }

        private static void Tick()
        {
            var run = DungeonRun.Current;
            var fight = Enabled.Value && EstateSession.Active && run != null && run.Fighting && GameModeMgr.CurrentMode == GameModeType.COMBAT;
            if (!fight)
            {
                if (_fight || BarShown) EndFight();
                return;
            }
            if (!_fight)
            {
                _fight = true;
                _open = false;
                _failed = false;
                FindHud();
            }
            if (_skills == null && !FindHud()) return;

            var hero = DungeonRun.ActingHero();
            BarShown = hero != null && MeasureBar();
            // the bag is one hero's, for the stretch their bar is up: a skill chosen or played, a turn ended,
            // another hero: the skills again
            if (_open && (hero == null || hero.ActorGuid != _openFor || (Time.unscaledTime > _openedAt + 0.25f && SkillChosen()))) _open = false;

            if (BarShown && OnTab.Value)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) Show(!_open);
            }

            if (!BarShown)
            {
                _view?.Hide();
                return;
            }
            if (_view == null) Build();
            var layout = ComputeLayout();
            if (!Same(layout, _applied))
            {
                _applied = layout;
                _view.Apply(layout);
            }
            var shown = _view.ShowsBag;
            _view.Show(true, _open, BarAlpha);
            if (_open && (!shown || Time.unscaledTime >= _nextFill)) Fill(run, hero);
        }

        private static bool Same(FightBagLayout a, FightBagLayout b)
        {
            return a.Grid == b.Grid && a.Button == b.Button && a.Cover == b.Cover && Mathf.Approximately(a.Scale, b.Scale) && Mathf.Approximately(a.ButtonSize, b.ButtonSize);
        }

        // The hero has chosen a skill (a target is being picked) or the skill is being played: the bar is the
        // skills' again. A menu or a sheet of DD2's over the fight is not that: the bag is back when it closes.
        private static bool SkillChosen()
        {
            if (_skills != null && _skills.CurrentInputState == SkillSelectionBhv.InputState.ACTOR_SELECT) return true;
            if (_presentation == null) return false;
            var state = _presentation.CurrentPresentationState;
            return state != CombatPresentationState.IDLE && state != CombatPresentationState.START_TURN;
        }

        private static void EndFight()
        {
            _fight = false;
            _open = false;
            BarShown = false;
            _view?.Hide();
            // nothing of a fight's stays on a hero
            FightBagBuffs.Clear();
        }

        private static bool FindHud()
        {
            try
            {
                _ui = SingletonMonoBehaviour<CombatUiBhv>.HasInstance() ? SingletonMonoBehaviour<CombatUiBhv>.Instance : null;
                if (_ui == null) return false;
                _bar = Traverse.Create(_ui).Field("m_combatInterfaceBarBhv").GetValue<CombatInterfaceBarUiBhv>();
                _skills = _bar != null ? Traverse.Create(_bar).Field("m_skillSelectionBhv").GetValue<SkillSelectionBhv>() : null;
                if (_skills != null)
                {
                    _move = Traverse.Create(_skills).Field("m_moveButtonBhv").GetValue<SkillButtonBhv>();
                    _pass = Traverse.Create(_skills).Field("m_passButtonBhv").GetValue<SkillButtonBhv>();
                }
                _presentation = _ui.GetComponentInParent<CombatPresentationBhv>();
                // the groups whose alpha fades the bar in and out, from the buttons' own panel up
                _groups = _skills != null ? _skills.GetComponentsInParent<CanvasGroup>(true) : new CanvasGroup[0];
                return _skills != null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Fight bag: DD2's fight HUD could not be found: " + e.Message);
                return false;
            }
        }

        // DD2's bar as it stands this frame: whether any of its buttons is up, where they are and how much of
        // them shows (the bar is faded in and out by the alpha of the groups it stands in).
        private static bool MeasureBar()
        {
            if (_skills == null || !_skills.gameObject.activeInHierarchy) return false;
            if (_presentation != null)
            {
                var state = _presentation.CurrentPresentationState;
                if (state != CombatPresentationState.IDLE && state != CombatPresentationState.START_TURN) return false;
            }
            var common = SingletonMonoBehaviour<CommonUiBhv>.HasInstance() ? SingletonMonoBehaviour<CommonUiBhv>.Instance : null;
            if (common != null && (common.IsPauseOrOptionsMenuActive() || common.IsCharacterSheetActive || common.IsConfirmationDialogActive || common.IsTokenReferenceViewActive)) return false;
            if (_ui != null && (_ui.MoreInfoActive || _ui.IsAcademicViewActive)) return false;

            var any = false;
            float left = float.MaxValue, right = float.MinValue, top = float.MaxValue, bottom = float.MinValue;
            for (var i = 0; i < _skills.SkillButtonCount + 2; i++)
            {
                var button = i < _skills.SkillButtonCount ? _skills.GetSkillButton(i) : i == _skills.SkillButtonCount ? _move : _pass;
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                var rect = ScreenRect((RectTransform)button.transform);
                if (rect.width <= 0f) continue;
                if (!any) BarPlace = rect.height;
                any = true;
                left = Mathf.Min(left, rect.xMin);
                right = Mathf.Max(right, rect.xMax);
                top = Mathf.Min(top, rect.yMin);
                bottom = Mathf.Max(bottom, rect.yMax);
            }
            if (!any) return false;
            BarRect = Rect.MinMaxRect(left, top, right, bottom);

            var alpha = 1f;
            foreach (var group in _groups)
            {
                if (group == null) continue;
                alpha *= group.alpha;
                if (group.ignoreParentGroups) break;
            }
            BarAlpha = alpha;
            return alpha > 0.02f;
        }

        /// <summary>A rectangle of DD2's UI on the mod's 1920x1080 screen (y down), whatever canvas it is on.</summary>
        internal static Rect ScreenRect(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas : null;
            var camera = root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            rt.GetWorldCorners(Corners);
            float left = float.MaxValue, right = float.MinValue, low = float.MaxValue, high = float.MinValue;
            foreach (var corner in Corners)
            {
                var pixel = RectTransformUtility.WorldToScreenPoint(camera, corner);
                left = Mathf.Min(left, pixel.x);
                right = Mathf.Max(right, pixel.x);
                low = Mathf.Min(low, pixel.y);
                high = Mathf.Max(high, pixel.y);
            }
            // the mod's screen: 1920x1080 scaled to the display's height, centred, standing on its foot
            var scale = Screen.height / 1080f;
            var x0 = (Screen.width - 1920f * scale) * 0.5f;
            return Rect.MinMaxRect((left - x0) / scale, 1080f - high / scale, (right - x0) / scale, 1080f - low / scale);
        }

        private static void Build()
        {
            _view = new FightBagView(Order)
            {
                Toggled = () => Show(!_open),
                Clicked = OnClicked,
                TooltipOf = TooltipOf,
                ButtonTooltip = () => (_open ? "Combat Skills" : "Inventory") + (OnTab.Value ? " [TAB]" : "")
            };
            _view.SetSkin(FightBagArt.Skin(_move, PlaceIcon));
        }

        /// <summary>Dev bridge: another sign on the button ("dd1": DD1's sack; a DD2 sprite's name; null: the stock one).</summary>
        internal static void SetIcon(string name)
        {
            PlaceIcon = name;
            _view?.SetSkin(FightBagArt.Skin(_move, PlaceIcon));
        }

        /// <summary>Dev bridge: the view is made again (another picture for the button, another order).</summary>
        internal static void Rebuild()
        {
            _view?.Destroy();
            _view = null;
            _applied = default(FightBagLayout);
        }

        /// <summary>
        /// DD1's panel at this share of its size: the owner's picture of the fight's bag has the cells 72 px
        /// apart on a 1920 screen where DD1's own are 80.
        /// </summary>
        internal const float BagScale = 0.9f;
        /// <summary>The second row's cards end this far over the screen's foot (the same picture).</summary>
        internal const float BagFoot = 9f;

        // Where the bag stands, by the owner's picture: in the middle of the screen's width, where DD2's bar is,
        // its two rows ending just over the screen's foot, which puts its first row beside the hero's name plate.
        // The button is one more place of DD2's bar, before its first skill button: a diamond at the bar's left
        // end as "move" and "pass" are at its right end. It stays there under the bag.
        private static FightBagLayout ComputeLayout()
        {
            var l = RaidLayout.Current;
            var layout = new FightBagLayout { Scale = !float.IsNaN(PlaceScale) ? PlaceScale : BagScale };
            var width = l.BagOffset.x * l.BagColumns * layout.Scale;
            var rows = (InventoryContent.Rules.RaidSlots + l.BagColumns - 1) / l.BagColumns;
            var height = (l.BagOffset.y * (rows - 1) + l.ItemIconSize.y) * layout.Scale;
            layout.Grid = new Vector2(!float.IsNaN(PlaceX) ? PlaceX : Mathf.Round(960f - width * 0.5f), !float.IsNaN(PlaceY) ? PlaceY : Mathf.Round(1080f - BagFoot - height));
            layout.ButtonSize = !float.IsNaN(PlaceButtonSize) ? PlaceButtonSize : BarPlace;
            layout.Button = new Vector2(
                !float.IsNaN(PlaceButtonX) ? PlaceButtonX : Mathf.Round(BarRect.xMin - layout.ButtonSize),
                !float.IsNaN(PlaceButtonY) ? PlaceButtonY : Mathf.Round(BarRect.yMin));
            // the black ground: everything of DD2's bar from the cells' top down, and the cells
            var top = layout.Grid.y - (l.BagStart.y - FightBagView.CellsTop) * layout.Scale;
            var left = Mathf.Min(layout.Grid.x - 8f, BarRect.xMin - 2f);
            var right = Mathf.Max(layout.Grid.x + width + 8f, BarRect.xMax + 6f);
            layout.Cover = Rect.MinMaxRect(left, Mathf.Min(top, BarRect.yMin - 6f), right, 1080f + 400f);
            return layout;
        }

        /// <summary>Shows the bag (true) or the skills (false); the bag only while the bar is up.</summary>
        public static bool Show(bool bag)
        {
            if (bag && !BarShown) return false;
            var hero = DungeonRun.ActingHero();
            if (bag && hero == null) return false;
            // a skill waiting for its target is put down first, the way DD2 itself does when a menu comes up
            if (bag && _skills != null && _skills.CurrentInputState == SkillSelectionBhv.InputState.ACTOR_SELECT)
            {
                try { Assets.Code.Combat.Events.EventActorTryResetSelection.Trigger(); }
                catch (Exception e) { Plugin.Log.LogWarning("Fight bag: the skill being aimed could not be put down: " + e.Message); }
            }
            if (_open != bag) Sound(() => AudioPathsBhv.TabClick);
            _open = bag;
            _openFor = bag ? hero.ActorGuid : 0u;
            _openedAt = Time.unscaledTime;
            return true;
        }

        private static void Fill(DungeonRun run, ActorInstance hero)
        {
            _nextFill = Time.unscaledTime + FillEvery;
            var rules = run.FightItems;
            var state = run.FightStateOf(hero);
            var scene = run.FightSceneNow();
            // DD1 greys what is used and cannot be used now; what is only carried (gold, gems, heirlooms, a
            // trinket) keeps its colours
            _view.Fill(run.Bag, (slot, stack) => FightItemRules.IsUsedItem(stack.Item) && !rules.Plan(stack.Item, state, scene).Possible);
        }

        private static string TooltipOf(int slot)
        {
            var run = DungeonRun.Current;
            var stack = run?.Bag.Slot(slot);
            if (stack == null) return null;
            var item = stack.Item;
            // DD1's words for the item, as in the corridor. Its line on what a supply gives a hero between fights
            // ("On a hero: +10% health.") is true here only while those stand-ins count in a fight.
            var words = item.Type == ItemTypes.Supply && !FightItemRules.StandInsInAFight
                ? InventoryText.Description(item) ?? InventoryText.Hint(item)
                : InventoryText.Tooltip(item, false);
            var reason = Reason(run, slot);
            if (reason != null)
                words += "\n<color=" + RaidText.Hex(Dd1Fonts.Colour("inventory_shift_click_remove_item", DD2Estate.UI.UiKit.Harmful)) + ">" + reason + "</color>";
            return RaidTooltip.Titled(InventoryText.Name(item), words);
        }

        /// <summary>Why the stack of a slot cannot be used now; null when it can, and for what is only carried.</summary>
        internal static string Reason(DungeonRun run, int slot)
        {
            var stack = run?.Bag.Slot(slot);
            if (stack == null) return null;
            if (stack.Item.Type == ItemTypes.Trinket) return TrinketRefusal;
            if (!FightItemRules.IsUsedItem(stack.Item)) return null;
            var hero = DungeonRun.ActingHero();
            var use = run.PlanFightUse(slot, hero);
            return use == null || use.Possible ? null : run.FightRefusalText(stack.Item, use, hero);
        }

        // DD1 says it of a worn one (str_user_information_cant_unequip_trinket_in_battle); the mod's own words for one in the bag
        private const string TrinketRefusal = "Trinkets cannot be changed during a battle.";
        private const string NoDiscard = "Nothing is thrown away during a battle.";

        // DD1: the right button uses an item of the inventory; the corridor's bag reads a plain click as the
        // same, and so does this one. Nothing is thrown away in a fight (GUESS: DD1's files do not say whether
        // its shift-click works during a battle; a stack lost to a slip of the hand in a fight is the worse
        // mistake), and no card is carried to another cell.
        private static void OnClicked(int slot, bool right)
        {
            var run = DungeonRun.Current;
            var stack = run?.Bag.Slot(slot);
            if (stack == null) return;
            if (!right && RaidUi.ShiftHeld)
            {
                Refuse(NoDiscard);
                return;
            }
            if (stack.Item.Type == ItemTypes.Trinket)
            {
                Refuse(TrinketRefusal);
                return;
            }
            var plan = run.PlanFightUse(slot, DungeonRun.ActingHero());
            if (run.UseItemInFight(slot, out var message))
            {
                Sound(() => AudioPathsBhv.MinorClick);
                // DD2's fight shows health given back and what is taken off a hero by itself; a buff has no
                // mark there, so it is said in a line
                if (plan != null && plan.Buffs.Count > 0) _view.Notice(message, true);
            }
            else Refuse(message);
            Fill(run, DungeonRun.ActingHero());
            _view.ShowTooltip();
        }

        private static void Refuse(string why)
        {
            _view.Notice(why);
            Sound(() => AudioPathsBhv.ClickInvalid);
        }

        // DD2's own sounds for its buttons: the bag is a part of its fight screen.
        private static void Sound(Func<FMODUnity.EventReference> which)
        {
            try { SingletonMonoBehaviour<AudioMgr>.Instance.Play(which()); }
            catch (Exception e) { Plugin.Log.LogInfo("Fight bag: no sound (" + e.Message + ")"); }
        }

        /// <summary>Dev bridge: a click on a card, as the pointer makes it.</summary>
        internal static void DevClick(int slot, bool right)
        {
            if (_view != null) OnClicked(slot, right);
        }
    }

    /// <summary>
    /// The button's pictures. Its sign is DD2's own picture for an inventory (the satchel "icon_inventory" of its
    /// UI icons, the one its HUD's inventory button wears), so that it says "bag" at a glance in DD2's hand; the
    /// plate under it and the mark it wears are those of DD2's "move" button, read from the button itself. DD1's
    /// sack (the raid panel's inventory tab) stands in when DD2's satchel is not loaded.
    /// </summary>
    internal static class FightBagArt
    {
        /// <summary>Names of DD2 sprites tried in turn for the sign; the first one loaded is taken.</summary>
        internal static readonly string[] Dd2Signs = { "icon_inventory", "icon_inventory_all", "icon_inventory_items" };

        public static FightBagSkin Skin(SkillButtonBhv move, string wanted)
        {
            var skin = new FightBagSkin();
            try
            {
                if (move != null)
                {
                    var plate = Picture(move.transform, "SkillIcon");
                    if (plate != null) skin.Plate = plate.sprite;
                    var sign = Picture(move.transform, "SkillIcon/Image");
                    if (sign != null) skin.SignColour = sign.color;
                    var hovered = Picture(move.transform, "overlay_hovered_image");
                    if (hovered != null)
                    {
                        skin.Mark = hovered.sprite;
                        skin.Hovered = hovered.color;
                    }
                    var chosen = Picture(move.transform, "overlay_selected_image");
                    if (chosen != null) skin.Chosen = chosen.color;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Fight bag: the look of DD2's move button could not be read: " + e.Message); }
            if (skin.Plate == null) skin.Plate = Loaded("ui_container_item_blue");
            if (skin.Mark == null) skin.Mark = Loaded("ui_highlight_move");

            if (wanted != "dd1")
            {
                if (!string.IsNullOrEmpty(wanted)) skin.Sign = Loaded(wanted);
                for (var i = 0; skin.Sign == null && i < Dd2Signs.Length; i++) skin.Sign = Loaded(Dd2Signs[i]);
                skin.SignIsDd2 = skin.Sign != null;
            }
            if (skin.Sign == null)
            {
                skin.Sign = FightBagView.Dd1TabIcon();
                if (wanted != "dd1") Plugin.Log.LogInfo("Fight bag: DD2's inventory picture is not loaded; the button wears DD1's sack");
            }
            return skin;
        }

        private static UnityEngine.UI.Image Picture(Transform root, string path)
        {
            var child = root.Find(path);
            return child != null ? child.GetComponent<UnityEngine.UI.Image>() : null;
        }

        /// <summary>A sprite the game has loaded, by its name; null when it has none of that name.</summary>
        public static Sprite Loaded(string name)
        {
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && sprite.name == name) return sprite;
            return null;
        }
    }
}
