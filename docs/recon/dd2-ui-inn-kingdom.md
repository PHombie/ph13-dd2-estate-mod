# DD2 recon — UI framework, Inn, Kingdoms, hero select, travel/arena presentation, narration, localization, runtime content

Game: Darkest Dungeon II `2.04.85095` (PlayerSettings `bundleVersion`, read from `globalgamemanagers`), Unity 2022.3, Mono, URP, linear colour space.

## 0. Conventions and method

* `IC/` = `D:\Mods\DD2\dd2-estate-mod\_ref\dd2-decomp\IronCrown\`. Folder = namespace. Citations are `IC/<folder>/<file>.cs:<line>`.
* "bundle-verified" = read directly (read-only) out of the shipped Addressables bundles in
  `E:\Steam\steamapps\common\Darkest Dungeon® II\Darkest Dungeon II_Data\StreamingAssets\aa\` with UnityPy 1.25.2; nothing in the game folder was modified.
  Helper scripts and a decoded catalog live in `_ref/_scratch/` (git-ignored), see Appendix C.
* "not verified" = inferred from reading code/assets, not observed in a running game. The game was never launched.
* Addressable keys quoted in `code font` were decoded from `aa/catalog.json` (`m_KeyDataString`/`m_BucketDataString`/`m_EntryDataString`).
  Every asset has a GUID key; only some also have their `Assets/...` path as a key, others have a shortened address. Always look the key up in
  `_ref/_scratch/catalog_map.json` before using it.

Core singletons used everywhere below:

| Access pattern | Meaning |
|---|---|
| `SingletonMonoBehaviour<T>.Instance` / `.HasInstance()` | scene/prefab-bound manager (e.g. `ScreenStackBhv`, `CommonUiBhv`, `InnPresentationBhv`) |
| `Singleton<T>.Instance` | plain C# system (e.g. `GameModeMgr`, `GameTypeMgr`, `Localization`, `InnBhv`) |
| `EventManager.AddListener<T>(Action<T> callback, bool oneShot = false, int priority = 0)` | global event bus, higher `priority` runs first — `IC/Assets.Code.Events/EventManager.cs:54`, ordering at `:93-101` |
| `Globals.RegisterPostInstallerCallback<T>(Action callback, ...)` | run code once an installer (system set) has finished loading — `IC/Globals.cs:81` |

Game modes are a closed `CustomEnum` with a private constructor (`IC/Assets.Code.Game/GameModeType.cs:137`); each maps to a scene:

| `GameModeType` | scene | input map | line |
|---|---|---|---|
| `DRIVING` | `MainScene` | `Driving` | `GameModeType.cs:41` |
| `COMBAT` | `combat` (+ additive arena scene) | `Combat` | `:43` |
| `HERO_SELECT` | `hero_select` | `Driving` | `:45` |
| `INN` | `inn` (or `camp` in Kingdoms) | `Driving` | `:47` |
| `ALTAR_OF_HOPE` | `altar_of_hope` | — | `:59` |
| `EMBARK` | `embark_<biome>` / `embark_camp_<biome>` | — | `:61` |
| `MAIN_MENU`, `CINEMATIC`, `RESULTS`, `HERO_STORY_INTRO`, `REALTIME_CINEMATIC`, `LOADING` | | | `:49-63` |

Switching: `Singleton<GameModeMgr>.Instance.SetMode(GameModeType mode, bool isLoad, SceneTransition transitionOverride = null, bool? showTransitionThrobberOverride = null, bool unloadEverything = false, bool isGameOverTransition = false)` — `IC/Assets.Code.Game/GameModeMgr.cs:256`.
Every mode change destroys all object pools and runs `Resources.UnloadUnusedAssets()` (`GameModeMgr.cs:407`, `:436`, `:451-461`) and clears the screen stack (`IC/Assets.Code.UI.Screens/ScreenStackBhv.cs:673-679`).
Game types: `GameType.EXPEDITION` (default mode `DRIVING`) and `GameType.KINGDOM` (default mode `INN`) — `IC/Assets.Code.Game/GameType.cs:10`, `:16`.

---

## 1. UI framework

### 1.1 It is uGUI + TextMeshPro (not UI Toolkit)

* All screens derive from `MonoBehaviour`s using `UnityEngine.UI` / `TMPro` (`Button`, `Image`, `TextMeshProUGUI`, `CanvasGroup`, `ScrollRect`), e.g. `IC/Assets.Code.UI.Screens/UiModalBhv.cs:5-7`, `:160`.
* Input is the new Input System (`UnityEngine.InputSystem.PlayerInput`) wrapped by `Assets.Code.Inputs.InputSystemBhv` — `IC/Assets.Code.Inputs/InputSystemBhv.cs:17`, `:88`.
* Bundle-verified canvases (all `ScreenSpaceOverlay`, `CanvasScaler` = ScaleWithScreenSize, reference 1920×1080):

| Root prefab (addressable key) | Canvas `sortingOrder` | Notes |
|---|---|---|
| `AddressableResources/Systems/screen_stack.prefab` | 10 | `ScreenStackBhv`, `DataContextBhv`, `RunValueDataContextBhv`; scaler match = 1.0, screenMatchMode = Expand; children `BlurCanvas` (Image with `uiblur.mat`), `layers`, `UIPool` |
| `AddressableResources/Systems/CommonUiBhv.prefab` | 30 | `CommonUiBhv` |
| `AddressableResources/Systems/DragCanvas.prefab` | 50 | `DragCanvasUiBhv` (drag & drop / slot select) |
| `Assets/Prefabs/UI/Canvases/TooltipCanvas.prefab` | 100 | `TooltipCanvasUiBhv` + pooled tooltip elements |
| `AddressableResources/Systems/ScreenFaderBhv.prefab` | 32766 | fades / wipes / throbber |

  Scene-owned canvases (also overlay): `Inn UI` + `Floating UI` (inn scene), `DrivingUI` (MainScene), `CombatUI` (combat scene), `hero_select_canvas`, `embark_ui_bhv`.
* Sorting layers: only `Default` and `Cinematic`. Layers of interest: `5 UI`, `10 Characters`, `11 ForUI`, `16 ClickableObject`, `19 WorldSpaceUI`, `24 Foreground`, `25 Foreground_UI`, `29 WorldSpacePopText` (TagManager, bundle-verified).

### 1.2 Screens: definition, opening, stacking, closing

* **Base classes**
  * `Assets.Code.UI.Elements.UiBaseBhv : MonoBehaviour` — lifecycle helpers `AddPostStartCallback(Action)`, `AddUpdateCallback(Action)`, `IsVisible` — `IC/Assets.Code.UI.Elements/UiBaseBhv.cs:9`, `:121`, `:133`.
  * `Assets.Code.UI.Screens.UiScreenBhv : UiBaseBhv, IPointerClickHandler` — a screen. State machine `UiScreenState { None, Opening, Open, Closing, Closed }`; optional `PlayableDirector` open/close/idle timelines; open/close FMOD events; `m_ScreenStackLayer`; `m_canCloseWithHotkey`; `m_firstSelectedGameObject` — `IC/Assets.Code.UI.Screens/UiScreenBhv.cs:21-74`.
    Key API: `virtual void OnScreenPushed(UiScreenPushParams pushParams = null)` `:223`, `virtual void OnScreenPopped()` `:239`, `T GetWidget<T>() where T : UiScreenWidgetBhv` `:265`, `virtual bool GoBack()` `:546`, `virtual void TryCloseScreen()` `:551`, `void ForceCloseScreen()` `:541`, `GameObject OpenScreen(UiScreenPushParams pushParams = null)` `:575`, `void SetFirstSelectedObject(GameObject)` `:597`, events `OnOpened` / `OnClosed` (`LightweightAction<UiScreenBhv>`) `:85-87`. Right mouse click on the screen root closes it (`OnPointerClick` `:589-595`).
  * `Assets.Code.UI.Widgets.UiScreenWidgetBhv : UiBaseBhv` — content widgets inside a screen; the screen calls `OnScreenOpenStart/OnScreenOpenCompleted/OnScreenUpdate/OnScreenCloseStart/OnScreenCloseCompleted` and honours `IsBlockingScreenClose` / `IsBlockingCloseAction` — `IC/Assets.Code.UI.Widgets/UiScreenWidgetBhv.cs:10-63`. Widgets declare the input focus they own via `ActiveFocus` (`:18`).
  * `UiScreenPushParams` — abstract parameter bag (`IC/Assets.Code.UI.Screens/UiScreenPushParams.cs`), subclassed per screen (`StoreUiPushParams`, `HospitalUiPushParams`, `InventoryUiPushParams`, `CharacterSheetUiPushParams`, `StoryScreenUiPushParams`, …).
* **There is no screen-id enum.** A screen is identified by its *prefab GameObject reference*. The stack is `Assets.Code.UI.Screens.ScreenStackBhv : SingletonMonoBehaviour<ScreenStackBhv>` (`IC/Assets.Code.UI.Screens/ScreenStackBhv.cs:13`):
  * `enum Layer { Undetermined, Inn, Altar, Story, Loot, Items, Actor, Modal, Pause, PauseModal, Loading, Map }` (`:15-29`). One child canvas per layer is created from `m_LayerPrefab` (`ScreenStackLayer.prefab`) in `Initialize()` (`:129-172`); a layer canvas is enabled only while it holds screens (`:426-449`). A blur canvas is switched on when a screen sits at/above `m_BlurCanvasLayer` or on `Pause`/`PauseModal` (`:434-447`).
  * `GameObject PushScreen(GameObject screen, Layer layer, UiScreenPushParams pushParams = null)` (`:217`): if `screen` is a prefab asset (`!screen.scene.IsValid()`), it is instantiated under the layer (or a preloaded instance is reused) and recorded in `m_ActivePrefabInstances` and in the ordered list via `AddScreenToOrder(ScreenOrderType.SCREEN, value)` (`:225-235`); if it is already a scene object it is only re-parented and activated (`:248-252`) — **it is not added to the order list**. Then `UiScreenBhv.OnScreenPushed` is invoked after `Start` (`:254-265`).
  * `void PopScreen(GameObject screen)` (`:275`): prefab instances are destroyed (or deactivated if preloaded), scene objects are `SetActive(false)` (`:326-340`).
  * `void Clear()` (`:344`), `FindInstance(prefab)` (`:571`), `PeekScreen<T>()` (`:595`), `GetTopMostCloseableWithHotkeyScreen()` (`:617`), `SelectFirstItemOnTopMostScreen()` (`:644`), public `AddScreenToOrder` / `RemoveScreenFromOrder` (`:378`, `:383`).
* **Façade:** `Assets.Code.UI.Managers.CommonUiBhv : SingletonMonoBehaviour<CommonUiBhv>` holds serialized prefab references for every shared screen (`IC/Assets.Code.UI.Managers/CommonUiBhv.cs:168-344`) and exposes `Show*/Hide*/Toggle*` methods. Helpers: `static T FindScreenInstance<T>(GameObject prefab)` (`:996`), `static T ShowOrPopulateScreen<T>(GameObject prefab, ScreenStackBhv.Layer layer, UiScreenPushParams pushParams, Action<T> onPopulate)` (`:1407`).
* **Prefab loading:** most prefabs are direct serialized references on `CommonUiBhv`; rarely used ones are `AssetReferenceGameObject` loaded lazily with the `SafeLoad` extension (e.g. `m_hospitalPrefab` `:198`, loaded in `ShowHospital` `:2331-2346`; `m_storyPrefab` `:270`, `ShowStory` `:2631-2641`). `SafeLoad` = `IC/Assets.Code.Utils/AddressableUtils.cs:113`.
* **Closing / back:** input action `"ExitMenu"` → `CommonUiBhv.HandleGoBack` → `GoBack(bool usedEscStartButton)` (`:931-932`, `:1024-1211`) asks the stack for the top-most hotkey-closeable screen and calls `GoBack()` / `TryCloseScreen()`; scene-root objects implementing `IGoBackButton` get a last chance (`:1200-1209`).
* **Sub-screen tabs** (the inn / altar bottom bar): `Assets.Code.UI.Widgets.SubScreenCollectionBhv` (`IC/Assets.Code.UI.Widgets/SubScreenCollectionBhv.cs:32`) owns a list of `AssetReferenceGameObject` sub-screens plus pre-spawned buttons; each sub-screen is a `SubScreenElementBhv : UiScreenBhv, ISubscreenContent` (`IC/Assets.Code.UI.Widgets/SubScreenElementBhv.cs:12`, interface `IC/Assets.Code.UI.Widgets/ISubscreenContent.cs:7-46`) with a tab sprite, tab colour and `m_ScreenNameLocString`. API used by owners: `FindElementByPrefab(prefab)`, `ToggleSubScreenElement(element[, pushParams])`, `HasAnySubScreensOpen()`, `CloseActiveSubScreen()` (call sites `IC/Assets.Code.ui/AltarOfHopeUiBhv.cs:501-512`, `IC/Assets.Code.Inn.Presentation/InnPresentationBhv.cs:1395-1424`).

### 1.3 Data binding (`DataContext`)

* `Assets.Code.Data.DataContextBhv : MonoBehaviour` — a keyed bag of `DataContextValue` (union of `Int, Float, String, GameObject, Sprite, Texture2D, Bool, Colour`, `IC/Assets.Code.Data/DataContextValue.cs:7-20`). Setters `SetStringValue/SetIntValue/SetFloatValue/SetBoolValue/SetSpriteValue/SetColorValue/SetTexture2DValue/SetGameObjectValue(string key, …)` (`IC/Assets.Code.Data/DataContextBhv.cs:247-285`), getters `:169-245`, `AddListener(string key, OnDataContextChanged callback)` `:295`. Dirty values are pushed to subscribers in `LateUpdate` (`:55-74`). Lookup falls back to the nearest parent `DataContextBhv` (`:105-117`, `:143-157`).
* View side: `Assets.Code.UI.DataContext.UiDisplayDataContextElement` (serialized `protected string m_Key`, `IC/Assets.Code.UI.DataContext/UiDisplayDataContextElement.cs:6-9`) with concrete binders `UiDisplayTextBhv` (TMP text; **auto-localizes the value as a loc key** when `m_Localize` is true — `UiDisplayTextBhv.cs:25`, `:71-74`; optional `m_Formatting`, substitution loc id), `UiDisplayImageBhv` (sprite → `Image.overrideSprite`), `UiDisplayColourBhv`, `UiDisplaySliderBhv`, `UiDisplayChildrenActiveBhv`, `UiDisplayChildrenActiveByIndexBhv`, `UiDisplayActiveByStringValueBhv`, `UIDisplayRawImage`, `UiDisplayActorPortraitBhv`, `RenderCharacterToTextureBhv` (all in `IC/Assets.Code.UI.DataContext/` and `IC/Assets.Code.UI.Rendering/`).
* Typical usage: code calls `m_dataContextBhv.SetStringValue("confirmation_title", title)` (`IC/Assets.Code.UI.Widgets/ConfirmationDialogBhv.cs:71-74`) and the prefab's `UiDisplayTextBhv` with `m_Key = "confirmation_title"` updates the label.

### 1.4 Tooltips

* `Assets.Code.UI.Tooltips.TooltipUiBhv : UiBaseBhv` (pointer enter/exit + select/deselect) is placed on the hovered element; it registers a pooled panel with `TooltipCanvasUiBhv` in `Awake` (unless `m_lazyInit`) and shows/positions it on hover — `IC/Assets.Code.UI.Tooltips/TooltipUiBhv.cs:13`, `:128-143`, `:191-222`, `:249-298`.
* Concrete types: `TextTooltipBhv.Populate(string text)` (`TextTooltipBhv.cs:20`), `LocalizedTextTooltipBhv` (`m_locKey`, `SetLocKey(string)`), `ItemTooltipBhv.Populate(IReadOnlyItemInstance item, bool includeRunStatModification)` (`ItemTooltipBhv.cs:64`), `SkillTooltipBhv.Populate(string skillId, uint actorGuid = 0u)` (`SkillTooltipBhv.cs:29`), `QuirkTooltipBhv.Populate(QuirkDefinition)` (`QuirkTooltipBhv.cs:33`), plus `BuffTooltipBhv`, `DotTooltipBhv`, `TokenTooltipBhv`, `StatusBarTooltipBhv`, `TorchTooltipBhv`, `UpgradeSkillTooltipBhv`, `EquippedTrinketTooltipBhv`.
* Canvas side: `Assets.Code.UI.Canvases.TooltipCanvasUiBhv` — `AddTooltipPanel(TooltipUiBhv)` (`TooltipCanvasUiBhv.cs:219`), `SetTooltipText(TooltipUiBhv, string)` (`:268`), `SetTooltipActive` (`:282`).
* Mod use: `go.AddComponent<TextTooltipBhv>().Populate("text")` on any raycast-target uGUI element needs no serialized references (not verified at runtime).

### 1.5 Modal dialogs / confirm popups

* Preferred: `CommonUiBhv.ShowConfirmationDialog(ConfirmationDialogType dialogType, string title, string desc, Action confirmBtnAction, string confirmBtnLabel, Action declineBtnAction = null, string declineBtnLabel = null, ScreenStackBhv.Layer layer = ScreenStackBhv.Layer.Modal, bool showCloseBtn = false)` — `CommonUiBhv.cs:2580`. Types `Default, DiscardItem, InnEmbark, AbandonRun, AbandonRunMainMenu, AbandonRunExitGame, MainMenuExitGame, StagecoachUnequippable, HotkeyCloseable` (`:73-84`); `HotkeyCloseable` treats Esc as "decline" (`:1213-1222`). Strings are passed already localized. Widget: `ConfirmationDialogBhv.Init(...)` `IC/Assets.Code.UI.Widgets/ConfirmationDialogBhv.cs:62`.
* Text input: `CommonUiBhv.ShowNameInputDialog(string title, string defaultString, int characterLimit, Action<string> confirmBtnAction, string confirmBtnLabel, Action<string> declineBtnAction = null, string declineBtnLabel = null, Layer layer = Modal)` — `:2872`.
* Lower-level generic modal: `UiModalBhv` + `UiModalPushParams.MakeOk/MakeYesNo/MakeYesNoCancel` (`IC/Assets.Code.UI.Screens/UiModalPushParams.cs:32-45`, `UiModalBhv.cs:11`).
* Others worth knowing: `ShowEnterNodeScreen(Action cmd, string buttonString, bool hasCandle)` `:2546` (the "Enter"/"Engage" prompt), `ShowDungeonConfirmationDialog(Action confirmCmd, Action declineCmd, int nextBattleIndex, IReadOnlyList<LootPreRoll> nextLootPreRolls)` `:2785` (press-on / retreat between lair fights), `ToastManager.ShowMessageToast(string toastLocKey, string tooltipLocKey, Action onClick)` `IC/Assets.Code.ui/ToastManager.cs:553`.

### 1.6 Buttons, hover/click SFX

* `Assets.Code.Audio.Sfx.ButtonAudioBhv` (`[RequireComponent(typeof(Button))]`) — click event (`m_eventRef` or a `ScriptableStringParameter` path), invalid-click, pointer-enter/exit events, spam delay — `IC/Assets.Code.Audio.Sfx/ButtonAudioBhv.cs:10-34`, `:113-126`. All references are serialized FMOD `EventReference`s, so it only works out-of-the-box on cloned native buttons.
* From code: `SingletonMonoBehaviour<AudioMgr>.Instance.Play(EventReference eventRef, int maxConcurrent = 8, AudioParamData paramData = null)` (`IC/Assets.Code.Audio/AudioMgr.cs:638`) with canned refs on `AudioPathsBhv`: `ClickConfirm` (`AudioPathsBhv.cs:671`), `ClickInvalid` (`:673`), `TabClick` (`:675`), `MinorClick` (`:779`), `MinorClickBack` (`:781`). Arbitrary event by path: `AudioEventUtils.MakeEventReference(string eventName)` (`IC/Assets.Code.Audio/AudioEventUtils.cs:125`).
* Visual hover helpers: `HighlightableButtonBhv` (highlight `CanvasGroup` + hover/submit `UnityEvent`s, `IC/Assets.Code.ui/HighlightableButtonBhv.cs:9-24`), `ButtonHoverBhv` (`AddOnHoverBeginEvent(UnityAction)` `IC/Assets.Code.UI.Input/ButtonHoverBhv.cs:70`), `UIPointerHoverBhv` (scale on hover, `IC/Assets.Code.ui/UIPointerHoverBhv.cs:7`), `HoldToActionBhv` (hold-to-confirm).

### 1.7 Controller / gamepad focus navigation

* Standard uGUI `Selectable` navigation driven by `EventSystem.current`; the device in use is `InputSystemBhv.ActiveInputDevice` (`MOUSE_AND_KEYBOARD | CONTROLLER | NONE`, `InputSystemBhv.cs:42-47`, `:105`).
* On open, a screen selects `m_firstSelectedGameObject` (or fires `m_selectFirstObjectEvent`) when the device is a controller — `UiScreenBhv.AttemptToSetFirstSelectedObject()` `UiScreenBhv.cs:377-390`.
* "Input focus" is a coarse context enum `InputSystemBhv.InputFocus { NO_FOCUS, DRIVING, PLAYER_INVENTORY, STORE_INVENTORY, CHARACTER_SHEET, INN_REST, STAGECOACH_SHEET, TOKEN_REFERENCE, TUTORIAL_ARCHIVE, INN_SUB_SCREEN, KINGDOM_MAP_EVENT_PANEL, INN_STORAGE, CREDITS, STORY_CHOICE }` (`InputSystemBhv.cs:24-40`), changed through `EventInputFocusChanged.Trigger(focus)` (e.g. `SelectableFocusTrigger`, `IC/Assets.Code.ui/SelectableFocusTrigger.cs:8-16`) and `EventOnUiScreenWidgetToggle` (`UiScreenWidgetBhv.cs:23`, `:36`). When focus returns to `NO_FOCUS` the top-most screen re-selects its first item (`CommonUiBhv.cs:964-970`).
* Helpers: `SelectOnEmptyFallbackBhv` (re-select something when nothing is selected, `IC/SelectOnEmptyFallbackBhv.cs:9-70`), `DeactivateNavigationOnInputDeviceBhv`, `DeactivateOnInputDeviceBhv`, `ButtonPromptBhv` (glyph for an input action per controller type, `IC/Assets.Code.UI.Input/ButtonPromptBhv.cs:14`, `:135`), `TabGroupBhv` (`IC/TabGroupBhv.cs`).
* Action listeners: `InputSystemBhv.AddListener(string actionName, InputActionDelegate callback)` / `RemoveListener` (`InputSystemBhv.cs:164`, `:183`); delegate `void (string actionName, InputActionDelegateValues values)` with `m_performed/m_canceled/m_started`. Action name constants are on `CommonUiBhv` (`"Submit"`, `"ExitMenu"`, `"PrevTab"`, `"NextTab"`, `"CycleLeftPanel"`, `"Embark"`, `"Navigate"`, `"MoreInfo"`, … `CommonUiBhv.cs:86-164`). Input maps per mode: `"Default"`, `"UI"`, `"Driving"`, `"Combat"`, `"Cinematic"`, `"Debug"` (`InputSystemBhv.cs:63-77`).

### 1.8 Reusable widgets (class → prefab key → how to feed it)

| Need | Class (file:line) | Prefab (addressable key, bundle-verified to exist) | Feed |
|---|---|---|---|
| Hero ribbon: portrait + HP + stress + tokens/DOTs (driving HUD) | `Assets.Code.UI.Banter.HeroRibbonBhv` `IC/Assets.Code.UI.Banter/HeroRibbonBhv.cs:41`; container `HeroRibbonContainerBhv` `:24` | `Assets/Prefabs/UI/Banter/hero_ribbon.prefab`, `Assets/Prefabs/UI/Banter/StressPip.prefab` | `SetActorData(ActorInstance actor, int slotIndex)` `HeroRibbonBhv.cs:177` |
| Overhead actor info (HP bar, stress, tokens) following a 3D actor | `Assets.Code.UI.ActorInfoUiBhv` `IC/Assets.Code.ui/ActorInfoUiBhv.cs` | `Assets/Prefabs/UI/actor_info_panel.prefab` | `Populate(uint actorGuid)` `:337` |
| HP bar / stress bar pieces | `StatusBarBhv.SetValue(float currentValue, float maxValue, float woundedPct)` `IC/Assets.Code.ui/StatusBarBhv.cs:189`; `StressBarBhv.Init(ActorInstance actor)` `IC/Assets.Code.ui/StressBarBhv.cs:48`; `ActorStatusUiBhv.Init(ActorInstance actor)` `IC/Assets.Code.ui/ActorStatusUiBhv.cs:145` | `Assets/Prefabs/UI/health_bar_panel.prefab`, `stress_bar_panel.prefab`, `status_bar_panel.prefab`, `StressBar.prefab` | as left |
| 2D portrait via data context | `UiDisplayActorPortraitBhv` `IC/Assets.Code.UI.DataContext/UiDisplayActorPortraitBhv.cs`; raw sprite: `ResourceActor.GetPortraitIconByType(ResourceActor.PortraitIconType.Color)` (used at `IC/Assets.Code.Campaign/HeroSelectBhv.cs:1988-1989`) | atlases `<class>_portraits`, `<class>_story_portraits` (e.g. `highwayman_portraits`) | data-context value |
| 3D hero rendered into a UI RawImage | `RenderCharacterToTextureBhv.TrySetActiveCharacter(uint actorGuid)` `IC/Assets.Code.UI.Rendering/RenderCharacterToTextureBhv.cs:52` (int data-context value = actor guid, `:107-119`) | `Assets/Prefabs/UI/Common/Character3DPortrait.prefab` | data-context key name on the prefab not verified |
| Hero sheet (skills, quirks/conditions, trinkets, relationships, hero story, cosmetics) | `CharacterSheetUiBhv` `IC/Assets.Code.UI.Widgets/CharacterSheetUiBhv.cs:39`, `enum Tab { Skills, Relationships, Conditions, HeroStory, Cosmetic }` `:41` | `Assets/Prefabs/UI/Screens/CharacterSheet/CharacterSheet.prefab` (+ `CharacterSheetMirrored.prefab` for controller) | `CommonUiBhv.ShowCharacterSheet(CharacterSheetUiBhv.Tab? tab, uint actorGuid, bool isSkillEditable, bool isInventoryEditable, bool autoselectTrinketSlot, bool heroSelectFilterParty)` `CommonUiBhv.cs:1492`; `ToggleCharacterSheet(...)` `:1630`; `HideCharacterSheet()` `:1599` |
| Skill loadout picker | `SkillLoadoutWidgetBhv` | `Assets/Prefabs/UI/Screens/screen_skill_loadout_selection.prefab` | `CommonUiBhv.ToggleSkillLoadoutScreen(uint actorGuid, Layer screenLayer = Actor)` `:2953` |
| Skill button / skill info | `SkillButtonBhv.Populate(uint actorGuid, string skillId, ResourceSkillBase skillResource, ResourceActor actorResource)` `IC/Assets.Code.ui/SkillButtonBhv.cs:233`; `UpgradeSkillButton`, `CharacterSheetSkillButtonBhv` | `Assets/Prefabs/UI/Skills/skill_button.prefab`, `character_sheet_skill_button.prefab`, `upgrade_skill_button.prefab`, `skill_info.prefab` | as left |
| Quirk entry | `CharacterSheetQuirkBhv.Populate(QuirkInstance quirkInst, ActorInstance actorInstance)` `IC/Assets.Code.ui/CharacterSheetQuirkBhv.cs:118` | `Assets/Prefabs/UI/Screens/CharacterSheet/Quirk.prefab` | as left |
| Item slot + inventory grid (drag/drop, filters, sell/discard) | `InventoryItemBhv` / `InventoryItemContainerBhv` (abstract, `IC/Assets.Code.UI.Items/InventoryItemContainerBhv.cs`, `Populate(DragElementContainerData)` `:278`); concrete `PlayerInventoryItemContainerBhv`, `StoreInventoryItemContainerBhv`, `TrinketInventoryItemContainerBhv`, `LootInventoryItemContainerBhv`; screen widget `InventoryUiBhv` `IC/Assets.Code.UI.Screens/InventoryUiBhv.cs:34` | `Assets/Prefabs/UI/Items/PlayerInventoryItem.prefab`, `StoreInventoryItem.prefab`, `TrinketInventoryItem.prefab`, `LootInventoryItem.prefab`, `UninteractableRewardItem.prefab`; screens `Assets/Prefabs/UI/Screens/Inventory/Inventory.prefab`, `screen_inn_player_inventory.prefab`, `screen_kingdom_inn_player_inventory.prefab` | `CommonUiBhv.TryShowPlayerInventory(bool closeButtonIsInteractable = true)` `:1880`; `TryShowPlayerInventory(SlotInteractivityPredicate, bool, Action onCloseCmd = null, Layer layer = Items)` `:1867`; `ShowInnPlayerInventory(bool forceInnInventory = false)` `:1885`; `HidePlayerInventory(...)` `:1929` |
| Currency display | `CurrencyContainerBhv.Refresh()` `IC/Assets.Code.ui/CurrencyContainerBhv.cs:57` (relics = item `gold`, mastery = `RunValueType.HERO_UPGRADE_POINTS`, baubles = 7 faction items, Kingdoms `materials`, `:59-98`); `InventoryItemSumBhv` `IC/Assets.Code.UI.Widgets/InventoryItemSumBhv.cs:11` | `Assets/Prefabs/UI/Screens/Inventory/InventoryCurrency.prefab`, `Assets/Prefabs/UI/Common/Currency/InventoryItemSum.prefab` | refresh on `EventUpdatePlayerCurrency` |
| Generic list / scroll | `InfiniteListBhv.Init(int totalItemCount, PopulateFunc populateFunc)` `IC/Assets.Code.ui/InfiniteListBhv.cs:65`; `PagedListWidgetBhv.SetTotalItemCount(int)` `IC/Assets.Code.UI.Widgets/PagedListWidgetBhv.cs:71`; `FlexibleGridLayout`; `ScrollToCenterBhv`; pooled rows via `GameObjectPoolBhv` (`IC/Assets.Code.CommonLogic.Pooling/GameObjectPoolBhv.cs:8`) | (components, used inside prefabs) | — |
| Buttons | — | `Assets/Prefabs/UI/Common/ui_button_basic.prefab` (Image + Button + TMP + `LocalizeTextBhv`), `CloseScreenButton.prefab`, `done_button.prefab`, `hold_to_action_button.prefab`, `Assets/Prefabs/UI/button_prefab.prefab`, `Assets/Prefabs/UI/ButtonPrompt.prefab`, `Assets/Prefabs/UI/Screens/Modal/ModalButton.prefab` | — |
| Party browser (prev/next hero in a panel) | `PartyBrowserBhv` `IC/Assets.Code.UI.Widgets/PartyBrowserBhv.cs` (used by hospital + mastery trainer) | part of those prefabs | `Initialize()` |
| Toasts, pop text, barks | `ToastManager`, `PopTextManager`, `BarkSpawnerSingleton.SpawnBark(uint actorGuid, string barkKey, BarkDisplayType, SourceType, string sourceId, EventReference overrideSpawnEventRef = default)` `IC/Assets.Code.Bark/BarkSpawnerSingleton.cs:83` (also overload following any `Transform`, `:94`) | `Assets/Prefabs/UI/Common/MessageToast.prefab`, `Assets/Prefabs/UI/PopText/*`, `Assets/Prefabs/UI/Combat/bark_prefab.prefab` | — |
| Fade / wipe | `ScreenFaderBhv.FadeToBlack(bool showThrobber, BasicCallback onFinished = null)` `IC/Assets.Code.ui/ScreenFaderBhv.cs:159`, `FadeOutOfBlack` `:164`, coroutine variants `:297-307` | system prefab | — |

Fonts (serialized on `CommonUiBhv`, `CommonUiBhv.cs:374-399`): `m_titleFont`, `m_descFont`, `m_barkFont` (`TMP_FontAsset`) with per-language fallback tables swapped in `LoadFallbackLanguages` (`:1242-1283`).
Font assets in the catalog: `Font/NDDunkelD-Bold SDF.asset` (title face; material presets `Font/NDDunkelD-Bold SDF DropShadow.mat`, `… BlackOutline.mat`, `… TitleCard_Large.mat`, …), `AlegreyaSans-Regular SDF` (body), `Cuprum-Italic SDF`, fallbacks under `Assets/Data/Font/Fallbacks/` (NotoSansSC/JP, SUIT-Variable, RobotoCondensed-Italic), colour gradient `Font/Gradients/color_gradient_title_golden.asset`.
Which of the three `CommonUiBhv` fields maps to which asset is not verified (private serialized references; read them at runtime).
UI art is addressable by path, e.g. `Assets/Art/UI/HeaderImages/ui_paper_panel.png`, `ui_inn_borders.png`, `panels_bookpage.png`, `bg_physician_tree.png`, `bg_trainer_tree.png`, `bg_provisions_tree.png`, `bg_wainwright_tree.png`, `art_button_travel.png`, `Assets/Art/UI/HUD/dd2_itemcontainer_border*.png`; icon atlases `Assets/Art/UI/Icons/NonTextAndTextIcons.spriteatlas`, TMP sprite asset `Sprite Assets/TextSpriteAtlas.asset`.

### 1.9 Least-effort way for a mod to show a native-looking full-screen screen

1. **Host on the game's stack, not a private canvas.** Build one root `GameObject` (RectTransform stretched, `CanvasGroup`, a raycast-target `Image` background) carrying a `UiScreenBhv` subclass and push it:
   `SingletonMonoBehaviour<ScreenStackBhv>.Instance.PushScreen(go, ScreenStackBhv.Layer.Inn /* or Altar */, pushParams)`. This gives correct ordering below `Items/Actor/Modal/Pause` screens, tooltips (order 100), the drag canvas (50) and the fader, plus automatic cleanup on mode change.
   * If the root is a **prefab asset from the mod's own AssetBundle**, `PushScreen` instantiates it and registers it for Esc/back handling (`ScreenStackBhv.cs:225-235`).
   * If the root is **constructed at runtime** (a scene object), also call `ScreenStackBhv.Instance.AddScreenToOrder(ScreenStackBhv.ScreenOrderType.SCREEN, go)` after pushing and `RemoveScreenFromOrder` when it closes, otherwise `CommonUiBhv.GoBack` and `SelectFirstItemOnTopMostScreen` will not see it (`:248-252` vs `:378-412`, `:617-642`). Closing a scene-object screen only deactivates it (`:337-340`), so it can be re-pushed.
2. **Reuse native panels by calling `CommonUiBhv`**, do not rebuild them: hero sheet, player inventory, store, field hospital, inn storage, confirmation / name dialogs, skill loadout, relationship matrix, token glossary (signatures in 1.5/1.8 and section 2).
3. **Reuse look:** take `TMP_FontAsset`s from `CommonUiBhv` (reflection on `m_titleFont`/`m_descFont`) or load `Font/NDDunkelD-Bold SDF.asset` / `AlegreyaSans-Regular SDF`; load panel/button sprites by the path keys above; instantiate small native prefabs (`ui_button_basic`, `CloseScreenButton`, `hero_ribbon`, `InventoryItemSum`, `Character3DPortrait`) through `Addressables.LoadAssetAsync<GameObject>(key)` + `Instantiate`.
4. **Tooltips:** `AddComponent<TextTooltipBhv>().Populate(text)`; item/skill/quirk tooltips by cloning a native slot prefab that already carries the tooltip component.
5. **Dialogs & SFX:** `CommonUiBhv.ShowConfirmationDialog(...)`; `AudioMgr.Instance.Play(AudioPathsBhv.ClickConfirm)`.

Gotchas (inferred from code, not verified in a running game):
* Components added with `AddComponent` at runtime do not have their `[SerializeField]` reference fields populated. `UiScreenBhv.m_selectFirstObjectEvent` (`UiScreenBhv.cs:73`) has no initializer and is dereferenced when a controller is active (`:381`); `DataContextBhv.m_Values` (`DataContextBhv.cs:13`) is dereferenced in `Initialize()` (`:135`). A mod subclass must assign these (reflection) in `Awake`, or avoid `DataContextBhv` on hand-built objects and bind text directly. A null `m_playableDirector` is tolerated (`UiScreenBhv.cs:245`, `:312`, `:435`, `:458` → `PlayableDirectorUtil.IsPlaying` null-checks at `IC/Assets.Code.Utils/PlayableDirectorUtil.cs:100-105`).
* `ScreenStackBhv.Clear()` runs on every game-mode exit (`ScreenStackBhv.cs:673-679`), so a hamlet screen must be re-pushed after returning from combat / a run.
* Many native screens branch on `GameModeMgr.CurrentMode` and `Singleton<GameTypeMgr>.Instance.CurrentGameType` (see section 2.4); they behave differently, or dereference null singletons, when opened in a mode they were not designed for.

**What the mod should reuse / hook**
* Reuse: `ScreenStackBhv` (layers `Inn`/`Altar` for the hamlet root, `Modal` for popups), `UiScreenBhv` as base class, `CommonUiBhv.Show*` façade, `TextTooltipBhv`, `ConfirmationDialogBhv`, `hero_ribbon`, `CharacterSheet`, inventory/store/hospital screens, `ScreenFaderBhv`, `AudioMgr` + `AudioPathsBhv`, game TMP fonts and `Assets/Art/UI/HeaderImages/*` sprites.
* Hook (Harmony): `CommonUiBhv.GoBack` only if custom back behaviour is needed; `ScreenStackBhv.OnGameModeExitComplete` / `GameModeMgr` enter-complete to re-open the hamlet; `EventLanguageFallbacksSet` to refresh own labels.

---

## 2. The Inn

### 2.1 Entry and state flow

* Logic singleton `Assets.Code.Inn.InnBhv : Singleton<InnBhv>, IGameModeEnterAsyncPreStart, IGameModeExitComplete` (`IC/Assets.Code.Inn/InnBhv.cs:27`). Scenes: `SCENE_INN = "inn"`, `SCENE_CAMP = "camp"` (`:31-33`).
* The inn is entered by switching mode: `Singleton<GameModeMgr>.Instance.SetMode(GameModeType.INN, isLoad: false)` — from the road node trigger `TriggerGameModeBhv.Execute()` (`IC/Assets.Code.Map.Triggers/TriggerGameModeBhv.cs:27-55`), after an inn-siege fallback (`TriggerCombatBhv.cs:156`), and in Kingdoms from `KingdomBhv` (`IC/Assets.Code.Kingdom/KingdomBhv.cs:249`, `:461`).
* `InnBhv.GameModeEnterAsyncPreStart` (`InnBhv.cs:69-168`) waits for roster/biome/kingdom-map readiness, creates `InnSystem` (`StartInn()` or `SaveUtils.TryLoadInn()`), picks the scene (`"inn"` for Expedition; in Kingdoms by `KingdomMapCellType.CAMP`/`INN`, `:115-130`), loads it additively (`RedHookSceneManagerBhv.LoadSceneAdditively(m_Scene, m_loadingObject, setActive: true)` `:133`) and preloads hire-able hero art (`:141-160`).
* `InnStateMachine` (`IC/Assets.Code.Inn/InnStateMachine.cs`) runs `PRELOAD → START → [SCORE → EFFECTS → ADD_QUIRKS] → LOOT_COLLECT_RESULT → WAIT_TO_CLOSE_RESULTS → LOOT_ADD_RESULT → SAVE → WAIT_ON_PLAYER → END` (`:38-78`; enum `IC/Assets.Code.Inn/InnState.cs`). `EFFECTS` applies every `IInnComponent.HeroEffects` to the party (`:139-160`); `ADD_QUIRKS` rolls inn quirks (`:161-196`); `SAVE` writes the save (`:201-210`). Each transition fires `EventInnStateChanged` (`:105`).
* Leaving: `InnPresentationBhv.OnEmbark()` shows the `InnEmbark` confirmation and then `InnBhv.NextState()` + `CompleteInn()` (`IC/Assets.Code.Inn.Presentation/InnPresentationBhv.cs:874-904`); `InnBhv.EndInn()` hands the chosen `BiomeChoice` to `EmbarkBhv.SetNextBiome` and sets mode `EMBARK` (Expedition `InnBhv.cs:332-338`; Kingdoms `:340-379`, including the camp-ambush combat at `:359-375`).

### 2.2 Scene and presentation (bundle-verified, `scenes_scenes_inn.bundle`)

```
Inn_Root (x = 2500)
  Inn Timeline            PlayableDirector, Animator, SetCameraOnTimelineBhv
    Inn Rendering
      Inn_ActorSpawnPositions   SpawnPositions          <- 4 hero transforms
      Blend Camera / Menu Camera (CinemachineVirtualCamera)
      Environment, Character_Properties, Fog (FogVolume), Lights, inn_interior_vfx_prefab
    Inn Presentation      InnPresentationBhv, PartyPresentationBhv, DataContextBhv
      Inn UI   (Canvas)   InnUiBhv
        inn_sub_screen_collection  SubScreenCollectionBhv  (tab buttons, EmbarkBtn, MouseEmbarkButton)
        WalletCurrencies (InventoryItemSumBhv x5), Header/InnLabel (InnTitleLabelBhv), StationedHeroesContainer
      Floating UI (Canvas)
        RestSlots   RestWidgetBhv
          HeroA..D  RestItemSlotBhv, ActorStatusUiBhv, UIFollowWorldTargetBhv, Selectable
        BountyHunterPosterUI  HunterPosterBhv
  FMOD (Inn): InnSfxBhv, InnNarrationBhv
```

* `InnPresentationBhv` (`[RequireComponent(typeof(PartyPresentationBhv))]`, `InnPresentationBhv.cs:55-56`) owns `m_innSubScreenCollectionBhv` (`:59`), `m_restWidgetBhv` (`:62`), `m_actorUiBhv` (`:101`) and prefab refs `m_biomeSelectPrefab`, `m_stageCoachPrefab`, `m_upgradeSkillsPrefab`, `m_biomeResultsPrefab` (`:105-114`). It spawns the four 3D heroes via `PartyPresentationBhv.SpawnParty(scene)` (`:1132-1139`).
* The heroes in the inn are real 3D `ActorBhv`s standing at `SpawnPositions` in animator state `inn_idle` (see section 5.3).

### 2.3 What the inn offers

| Feature | Sub-screen prefab (key) | Class | Notes |
|---|---|---|---|
| Rest / inn items (stress relief, heal, relationship items) and hero replacement | scene object `RestSlots`; also `Assets/Prefabs/UI/Inn/inn_sub_screen_rest.prefab` | `RestWidgetBhv` (`IC/Assets.Code.UI.Widgets/RestWidgetBhv.cs:37`), `RestItemSlotBhv` | Inn items are dragged from the player inventory onto a hero slot; applying triggers `EventRestItemApplied` / `EventRestItemBlocked` (`IC/Assets.Code.Inn.Events/`). An empty or dead slot opens the replacement screen (`RestItemSlotBhv.cs:470`, `:524`). |
| Hero swap / replacement | `Assets/Prefabs/UI/Screens/screen_inn_replacement_hero.prefab` | `InnReplacementScreenWidgetBhv` (`:27`) | `CommonUiBhv.ToggleInnReplacementScreen(uint innReplacementSourceActorGuid, int innPosition)` (`CommonUiBhv.cs:2902`); candidates from `RosterManager.GetReplacementActorGuids()` (`IC/Assets.Code.Roster/RosterManager.cs:1180`: `RESERVE` heroes in Expedition, heroes stationed in the current cell in Kingdoms); applied with `AddReplacementActorToParty` (`:1204-1230`). |
| Hire (Bounty Hunter poster) | `Assets/Prefabs/UI/Screens/screen_hunter_hire_dialog_panel.prefab` | `HunterHireScreenWidgetBhv`, `HunterPosterBhv` | `CommonUiBhv.ShowHunterHireScreen()` (`:2809`); `InnSystem.RollHireActors()` (`IC/Assets.Code.Inn/InnSystem.cs:157-170`); `RosterManager.Hire(uint hireActorGuid, uint replacingActorGuid)` (`RosterManager.cs:1170`). |
| Shop — "The Provisioner" | `Assets/Prefabs/UI/Inn/inn_sub_screen_item_store.prefab` | `InnStoreUiBhv : SubScreenElementBhv` (`IC/Assets.Code.UI.Screens/InnStoreUiBhv.cs:25`) wrapping `StoreUiBhv` | Stock = `InnInstance.StoreInventory` (`:54`), rolled by `InnInstance.RollStoreInventory()` (`IC/Assets.Code.Inn/InnInstance.cs:1198`). |
| Mastery / skill upgrades — "Mastery Trainer" (also hero path change) | `Assets/Prefabs/UI/Inn/inn_sub_screen_upgrade_skills.prefab` | `InnUpgradeSkillsBhv : SubScreenElementBhv` (`IC/Assets.Code.UI.Screens/InnUpgradeSkillsBhv.cs:41`) + `PartyBrowserBhv` | Spends `RunValueType.HERO_UPGRADE_POINTS`; `IsUnlocked()` requires `InnFeatureType.TRAINER` on the current `InnInstance` (`:199`). |
| Stagecoach — "The Wainwright" (equip/repair/skins) | `Assets/Prefabs/UI/Inn/stage_coach_sheet.prefab` (driving variant `stage_coach_sheet_driving.prefab`) | `StageCoachConfigUiBhv : SubScreenElementBhv` (`IC/Assets.Code.UI.Screens/StageCoachConfigUiBhv.cs:46`) | `InnPresentationBhv.ToggleStageCoachSheet(...)` (`:1395`); driving: `CommonUiBhv.ToggleStageCoachSheet(StageCoachScreenPushParams = null)` (`:2075`). |
| Route selection | `Assets/Prefabs/UI/Inn/inn_sub_screen_select_route.prefab` | `SubScreenBiomeChoiceBhv` | Expedition only. |
| Travelogue (run log, score) | `Assets/Prefabs/UI/Inn/inn_sub_screen_results.prefab` | `SubScreenBiomeResultsBhv` | closing it advances `WAIT_TO_CLOSE_RESULTS` (`InnPresentationBhv.cs:601-604`). |
| Inn upgrades tree (Kingdoms) | `Assets/Prefabs/UI/Inn/inn_sub_screen_upgrade.prefab`, `InnUpgradeTreePanel.prefab`, `inn_upgrade_button.prefab`, `inn_upgrade_category.prefab` | `SubScreenInnUpgradeBhv` (`:23`), `InnUpgradeCategoryWidgetBhv`, `InnUpgradeButtonBhv` | see section 3. |
| Physician (Kingdoms inn) / Field Hospital (road node) | key `screen_field_hospital_panel` (`Assets/Prefabs/UI/Screens/screen_field_hospital_panel.prefab`) | `HospitalScreenBhv : SubScreenElementBhv` (`IC/Assets.Code.UI.Screens/HospitalScreenBhv.cs`) | Tabs: Treatment (minor/full heal, cure disease), Therapy (lock positive quirk, remove quirk), Pharmacy (store). See 2.4. |
| Inn storage (Kingdoms) | `Assets/Prefabs/UI/Screens/Inn/InnStorage.prefab` | `InnStorageBhv` | `CommonUiBhv.OpenInnStorage(InnInstance innInstance = null)` (`:2359`). |
| Relationship matrix | `Assets/Prefabs/UI/Screens/Relationship/screen_relationships_matrix_panel.prefab` | `SubScreenRelationshipMatrixBhv` | — |

Localized names: `inn_screen_name_rest`, `inn_screen_name_store` ("The Provisioner"), `inn_screen_name_stage_coach` ("The Wainwright"), `inn_screen_name_upgrade_skills` ("Mastery Trainer"), `inn_screen_name_physician`, `inn_screen_name_biome_results` ("Travelogue") — `StreamingAssets/Localization/Sources/inn.txt`.

### 2.4 Which panels can be invoked standalone from a mod screen

Verified by reading each class's singleton dependencies:

| Panel | Call | Standalone? | Why |
|---|---|---|---|
| Hero sheet (skills / quirks / trinket equip) | `CommonUiBhv.ShowCharacterSheet(...)` `:1492` | **Yes**, any mode with a started game type and a valid actor guid | Only guards: `GameModeMgr.IsChangingState()` and combat intro (`:1500-1511`). `isInventoryEditable: true` enables trinket/combat-item equip; pairs with `TryShowPlayerInventory`. Uses layer `Actor` (or `Modal` in driving/combat, `:1564-1568`). `CharacterSheetUiBhv` touches `KingdomBhv.Instance.KingdomMapManager` (12 references) — check each is behind a `GameType.KINGDOM` test before using outside Kingdoms (not verified line by line). |
| Player inventory | `CommonUiBhv.TryShowPlayerInventory(...)` `:1867/:1880` | **Yes** except `ALTAR_OF_HOPE` and `COMBAT` modes | `GetCanShowPlayerInventory()` `:1848-1865`. |
| Store / Hoarder UI | `CommonUiBhv.ShowDrivingStore(IReadOnlyItemInstance[] itemsForSale, bool isValleyStore)` `:2303`; stock from `LibraryLoot.CollectItems(lootIds, RandomIdentifier.NODE_STORE, onInvalid)` (`IC/Assets.Code.Map.Triggers/TriggerStoreBhv.cs:46-57`) | **Yes** (designed for road nodes) | `StoreUiBhv` (`:28`) depends only on `GameTypeMgr` (stagecoach, torch, run values), `CommonUiBhv` and `InputSystemBhv`. Close with `HideDrivingStore()` `:2317`. The trigger additionally shows hero ribbons via `GameUIBhv` (MainScene only). |
| Field hospital = sanitarium analogue | `StartCoroutine(CommonUiBhv.ShowHospital(List<string> lootTables))` `:2331` | **Yes when `GameModeMgr.CurrentMode != INN`** | `HospitalScreenBhv.IsInn => GameModeMgr.CurrentMode == GameModeType.INN` (`HospitalScreenBhv.cs:239`); the non-inn branch needs only `HospitalUiPushParams.m_lootTables` (`:301-326`) and does not consult `InnBhv` feature flags (`:939-942`, `:1078-1082`). Costs are gold: `QuirkDefinition.GetRemoveCost()/GetLockCost()/GetCureCost()` × `RunStatType.STORE_COST_BUY_MULTIPLIER` (`:1174-1204`). In `INN` mode it dereferences `Singleton<InnBhv>.Instance.GetInnInstance()` (11 call sites) and so needs a live `InnInstance`. |
| Skill loadouts | `CommonUiBhv.ToggleSkillLoadoutScreen(actorGuid)` `:2953` | **Yes** | depends on `PlayerLoadoutMgr` + actor library only. |
| Confirmation / name input / token glossary | `:2580`, `:2872`, `:2663` | **Yes** | — |
| Mastery trainer `InnUpgradeSkillsBhv` | sub-screen of the inn collection | **No** (needs an inn) | `Singleton<InnBhv>.Instance.GetInnInstance()` at 10 call sites incl. `IsUnlocked()` `:199` and `GetActorUnlocks()` `:206`; `GoBack` special-case in `CommonUiBhv.cs:1143`. Usable only with a live `InnSystem` (i.e. inside `INN` mode) or with Harmony patches supplying an `InnInstance`. |
| Inn store `InnStoreUiBhv` | sub-screen | **No** | `InnPresentationBhv.Instance.ActiveInnInstance` (`:68`, `:101`), `InnBhv.Instance.*` (store viewed, camp). Use `ShowDrivingStore` instead. |
| Rest widget / hero replacement / hunter hire | scene-bound | **No** | `RestWidgetBhv`/`InnReplacementScreenWidgetBhv`/`HunterHireScreenWidgetBhv` call `InnPresentationBhv.Instance.*` (`RestWidgetBhv.cs:118`, etc.) and follow 3D actors in the inn scene. |
| Wainwright `StageCoachConfigUiBhv` | `CommonUiBhv.ToggleStageCoachSheet()` in driving | Partly | branches on `GameModeType.INN` and `InnBhv.GetInnInstance()` (6 sites) for repair/equip limits; the driving variant is read-mostly. |

**What the mod should reuse / hook**
* Reuse as-is from a hamlet screen: `ShowCharacterSheet` (trinket/skill management), `TryShowPlayerInventory`, `ShowDrivingStore` (trinket shop / provisioning), `ShowHospital` (sanitarium: quirks, diseases, healing), `ShowConfirmationDialog`.
* To reuse the **mastery trainer, inn items/rest, hero replacement and wainwright unchanged**, the cheapest route is to *be* an inn: run the hamlet inside `GameModeType.INN` with a real `InnInstance` whose `InnDefinition` / upgrades enable `InnFeatureType.TRAINER`, `PHYSICIAN`, `WAINWRIGHT`, `ITEM_SELLING`, … (`IC/Assets.Code.Inn/InnFeatureType.cs:9-31`), and add or re-skin tabs in `SubScreenCollectionBhv`.
* Hook: `InnBhv.EndInn` (`InnBhv.cs:328`) to redirect "embark" to the mod's quest/dungeon flow; `InnPresentationBhv.OnSubScreenPush/Pop` (`:524`, `:571`) for extra tabs; `InnStateMachine.PostCreate` (`:38`) to skip score/loot states for a hamlet visit; `EventInnStateChanged` for timing.

---

## 3. Kingdoms mode

### 3.1 Structure

* Root manager `Assets.Code.Kingdom.KingdomBhv : SingletonMonoBehaviour<KingdomBhv>`; `StartKingdom(GameModeType startGameModeType, KingdomDifficultyConfiguration difficultyConfiguration, KingdomMapDefinition mapDefinition, bool isLoad, bool loadModeByCell)` (`IC/Assets.Code.Kingdom/KingdomBhv.cs:423`). Sub-managers: `KingdomManager` (day counter, gang, statistics; `IC/Assets.Code.Kingdom/KingdomManager.cs:30-123`), `KingdomMapManager` (grid map, stagecoach coordinates, `NextBiomeChoice`, `ResolveTravel()` `:1441`, `ResolveEmbark()` `:1454`), `KingdomSiegeManager`, `KingdomEventManager`, `KingdomDifficulty`.
* The Kingdom game type boots into the **inn scene** (`GameType.KINGDOM` default mode `INN`, `GameType.cs:16`); the kingdom map is a *sub-screen of the inn* — `KingdomPresentationBhv : …, ISubscreenContent` (`IC/Assets.Code.Kingdom.Presentation/KingdomPresentationBhv.cs:40`), opened via `InnPresentationBhv.OpenKingdomMap()` (`InnPresentationBhv.cs:1415-1424`). Map scenes: `Assets/Scenes/Kingdoms/kingdom_map_01…07.unity`; UI prefab key `UI/Kingdom/kingdom_presentation.prefab`.
* Map cells: `KingdomMapCellType { INN, CAMP, BIOME, BOSS }` → `KingdomMapCellInn`, `KingdomMapCellCamp`, `KingdomMapCellBiome`, `KingdomMapCellBoss` (`IC/Assets.Code.Kingdom/KingdomMapCellType.cs:11-17`). Inn/camp cells derive from `KingdomMapCellInnContainer` and own an `InnInstance` (`IInnContainer`, `IC/Assets.Code.Inn/IInnContainer.cs`).

### 3.2 Inn data model and upgrades

* `Assets.Code.Inn.InnInstance` (`IC/Assets.Code.Inn/InnInstance.cs:32`): `m_InnDefinition`, `m_Name`, `m_PurchasedInnUpgrades`, `m_Hp` (siege damage), `StoreInventory` (`:126`), `StorageInventory` (`:128`), `InnComponents` (active `IInnComponent`s). Serialized with `ToJson()`/`FromJson()` (`:256-289`).
* `Assets.Code.Inn.InnUpgradeDefinition : SerializedLibraryElement<InnUpgradeDefinition>, IInnComponent` (`IC/Assets.Code.Inn/InnUpgradeDefinition.cs:19`): `m_InnLevel`, `m_InnUpgradeCategory`, `m_InnUpgradeType`, `m_CostId`/`CostDefinition`, prerequisite lists, hero effects, store/bonus loot tables, `m_OnPurchaseStoreLootTableIds`, actor unlocks, `m_InnFeatureTypes`, inn stats, siege defense, biome upgrades (`:21-73`).
* Categories (`IC/Assets.Code.Inn/InnUpgradeCategory.cs:9-25`): `DEFENSE, PROVISIONER, WAINWRIGHT, TRAINER, PHYSICIAN, KINGDOM_INN, KINGDOM_CAMP, STAGE_COACH_INN, STAGE_COACH_CAMP`. Types (`InnUpgradeType.cs:9-17`): `MINOR, MAJOR, ULTIMATE` (purchasable), `KINGDOM, STAGE_COACH` (not).
* Features unlocked by upgrades (`InnFeatureType.cs:9-31`): `ACTOR_PATH_CHANGE, STAGE_COACH_REPAIR, STAGE_COACH_CHANGE_SKIN, FAST_TRAVEL, TRAINER, WAINWRIGHT, REMOVE_POSITIVE_QUIRK, LOCK_POSITIVE_QUIRK, REMOVE_NEGATIVE_QUIRK, REMOVE_DISEASE, PHYSICIAN, ITEM_SELLING`; query `InnInstance.GetIsInnFeatureEnabled(InnFeatureType)` (`InnInstance.cs:888`).
* Inn stats (`InnStatType.cs:10-34`): `HEALTH_MAX, DEFENSE, REPAIR_PERCENT, UPGRADE_SKILL_LIMIT, UNLOCK_SKILL_LIMIT, STAGE_COACH_ITEM_SLOT_EQUIP_LIMIT, SIEGE_*, PHYSICIAN_WOUND_HEAL_PERCENTAGE, KINGDOM_WOUND_HEAL_PERCENTAGE, STORAGE_INVENTORY_MAX_SLOTS, INFECTION_ADJACENT_CURE_PERCENTAGE`; `InnInstance.GetStatValue(InnStatType, string subStatKey = null)` (`:873`).
* Purchase: `InnInstance.TryPurchaseUpgrade(InnUpgradeDefinition)` → `CostCalculation.CanAffordCost` / `AttemptSpendCost(cost, SourceType.INN)` (`InnInstance.cs:431-450`); queries `GetPurchasableUpgrades(category, type, checkCanAffordCost)` `:558`, `GetCanPurchaseUpgrade` `:602`, `GetLevel(category)` `:730`, `GetVisualLevel()` `:756`.
* Data is CSV: `StreamingAssets/Excel/kingdom_inn_upgrade_data_export.Group.csv` (144 `element_start` blocks; categories defense 13, physician 13, provisioner 14, trainer 9, wainwright 10; costs in item `materials`), `kingdom_inn_data_export*.csv`, `kingdom_rules_data_export.Group.csv` (`m_InnBaseId,kingdom_inn`, `m_CampBaseId,kingdom_camp`, `m_InnInitialFreeUpgradeCounts,2,2,1,1`), `kingdom_cost_data_export.Group.csv`.

### 3.3 UI

* Inn panel on the kingdom map: `Assets.Code.Kingdom.UI.ScreenKingdomMapInnPanel : ScreenKingdomPopupBase<KingdomMapCellInnContainer>` (`IC/Assets.Code.Kingdom.UI/ScreenKingdomMapInnPanel.cs:36`), prefab key `UI/Kingdom/screen_kingdom_map_inn_panel.prefab` (bundle `kingdoms_assets_all.bundle`). Contains travel / fast-travel / engage-siege buttons (`:40-54`), a pool of stationed-hero cards `KingdomInnPanelActorBhv` with drag-reorder (`:57-69`, `:169-171`), upgrade trees `List<InnUpgradeCategoryWidgetBhv> m_upgradeCategories` (`:72`) switched by tabs `m_defenseTab/m_provisionerTab/m_physicianTab/m_trainerTab/m_wainwrightTab` through `TabGroupBhv` (`:121-136`), an inn-storage button (`:114`, opens `OpenInnStorage(base.SelectedCell.InnInstance)` `:821`), treasure/siege displays.
* Roster header on the map: `KingdomMapActorHeaderBhv` (party container + reserve container, pooled `KingdomMapActorHeaderButtonBhv`, cycle-hero input `"CycleHeroRight"/"CycleHeroLeft"`) — `IC/Assets.Code.UI.Kingdom/KingdomMapActorHeaderBhv.cs:19-52`; hero buttons `KingdomActorButtonBhv.Init(uint actorGuid)` (`IC/Assets.Code.UI.Kingdom/KingdomActorButtonBhv.cs:64`), prefabs `Assets/Prefabs/UI/Kingdom/kingdom_actor_button.prefab`, `actor_portraits.prefab`. Top-level HUD `KingdomUiBhv` (`IC/Assets.Code.UI.Kingdom/KingdomUiBhv.cs`), timeline `KingdomMapTimelineBhv`, events sidebar `KingdomMapEventsSidebarBhv`.
* Other panels: `screen_kingdom_map_biome_panel`, `screen_kingdom_map_event_panel`, `screen_kingdom_siege_results_panel`, save-select `screen_kingdom_save_select_panel`; creation flow `KingdomCreationFlowBhv` (main menu).

### 3.4 Roster (more than four heroes) and recruitment

* `Assets.Code.Roster.RosterManager` (`IC/Assets.Code.Roster/RosterManager.cs:35`): `FULL_PARTY_SIZE = 4` (`:37`), `List<RosterEntry> m_Entries` (`:39`), each `RosterEntry(string actorClassId, uint actorGuid)` with a `RosterStatusType` (`IC/Assets.Code.Roster/RosterEntry.cs:10-41`).
* Statuses (`IC/Assets.Code.Roster/RosterStatusType.cs:9-25`): `IDLE, PARTY, RESERVE, DEAD, LOAD, CAPTURED, HIRE, HIRE_REPLACED, KINGDOM`. In Kingdoms all non-party heroes are `KINGDOM` and physically live in map cells (`KingdomMapCellBase.AddActor(uint)` `IC/Assets.Code.Kingdom/KingdomMapCellBase.cs:110`, capacity = free slots in `m_ActorGuids`, `:84-95`); `RosterManager.GetKingdomActors()` `:1034`, `GetPartyActors()` `:1016`, `GetIdleActors()` `:1048`, `GetActorGuids(RosterStatusType)` `:1391`.
* **The roster holds at most one hero per class.** `AddMissingHeroesToRoster` iterates `ActorDataClass` library elements and skips any class already present (`!GetIsActorInRoster(item.Id)`, `RosterManager.cs:436-460`, test at `:451`); entries are looked up by class id (`GetRosterEntryByActorClassId` `:1505`). "Recruitment" in Kingdoms is refilling: `RefillKingdomMap()` respawns dead heroes or adds missing classes and distributes them to inns (`:539-590`), governed by `RosterReplacementType { NONE, RESPAWN, REFILL }` and `m_ActiveEntryLimit` (`:77-81`, `:486-529`). There is no DD1-style stagecoach with random recruits.
* Party confirmation: `EventRosterConfirmParty.Trigger(IReadOnlyList<uint> partyActorGuids, IReadOnlyList<uint> preferedKingdomActorGuids)` sets entries to `PARTY` and then `FillKingdom`/`FillReserve` (`RosterManager.cs:242-258`, `:615-643`).

### 3.5 Resources / currencies

* Items in `Singleton<GameTypeMgr>.Instance.PlayerInventory` (`PlayerItemInventory`): `gold` ("relics"), `materials` (Kingdoms upgrade currency, `ScreenKingdomMapInnPanel.cs:38`), faction baubles `cave_dirt, forest_medals, farm_spoons, city_books, coast_starfish, valley_baubles, tundra_baubles` (`IC/Assets.Code.ui/CurrencyContainerBhv.cs:22-34`, `:66-92`).
* Run values (`IC/Assets.Code.Run/RunValueType.cs:24-34`): `TORCH`, `HERO_UPGRADE_POINTS` (mastery), `DOOM`, `STAGE_COACH_ARMOR`, `STAGE_COACH_WHEELS`, `ESCALATION` — via `Singleton<GameTypeMgr>.Instance.RunValues`.
* Profile value `ProfileValueType.CANDLES` (Altar of Hope currency, `IC/Assets.Code.Profile/ProfileValueType.cs:9`).
* Generic prices: `CostDefinition` (`m_ItemId`/`m_ItemQty`, `RunValueType`, `ItemTag`) with `CostCalculation.CanAffordCost` / `AttemptSpendCost` (`IC/Assets.Code.Cost/`).

### 3.6 Turn / day loop

* `KingdomBhv.Update()` drives `KingdomDaySystem.Update(this)` while `m_ShouldUpdateDay` (`KingdomBhv.cs:798-816`); `KingdomDaySystem` wraps `KingdomDayStateMachine` and runs registered `IKingdomDayPresentation.DisplayDayPresentation(state)` coroutines per state (`IC/Assets.Code.Kingdom/KingdomDaySystem.cs:63-95`, `:144-164`).
* States, in enum order (`IC/Assets.Code.Kingdom/KingdomDayState.cs:9-29`): `START → DAY_CHANGED → EVENT_SPAWN → CURSE_ACTIVITY → SIEGE_RESOLVE → SIEGE_SELECT* → SIEGE_SPAWN → MAP_UPDATE → WAIT_ON_PLAYER* → RESOLVE_TRAVEL → END` (`*` = waits for the player; `m_IncrementOnUpdate` false). Transitions fire `EventKingdomDayStateChanged(from, to, day)` and autosave on `WAIT_ON_PLAYER`/siege select (`IC/Assets.Code.Kingdom/KingdomDayStateMachine.cs:47`, `:50-65`). Player input advances with `KingdomDaySystem.UserInputIncrementState()` (`KingdomDaySystem.cs:134`); extra days via `AddAdditionalDays(int)` (`:139`).
* A "day" = one inn visit + one travel/expedition leg; loss conditions `KingdomManager.LossDay`, `LossNumberOfInns` (`KingdomManager.cs:91-95`).

### 3.7 Closest analogue to DD1 hamlet buildings

| DD1 building | DD2 Kingdoms analogue | Reuse verdict |
|---|---|---|
| Stagecoach (recruit) | roster refill `RefillKingdomMap`, hunter hire | **Build new** — one-hero-per-class roster; no recruit pool UI. Hero cards can reuse `kingdom_actor_button` / `HeroSelectActorUIBhv`. |
| Tavern / Abbey (stress relief) | inn rest items (`RestWidgetBhv`), inn `HeroEffects` / `KingdomHeroEffects` applied per day to stationed heroes (`InnInstance.ApplyKingdomHeroEffects` `InnInstance.cs:974`, `KingdomMapCellInnContainer.DoKingdomHeroEffects` `:86`) | **Build new UI**, reuse the effect pipeline (`EffectApply.Apply(new AppliedEffects.Input<…>(effects, actor))`, pattern at `InnStateMachine.cs:147-155`). |
| Sanitarium | Physician / `HospitalScreenBhv` | **Reuse** (`ShowHospital`, section 2.4). |
| Guild / Blacksmith | Mastery Trainer `InnUpgradeSkillsBhv`; trainer & physician upgrade trees | **Reuse inside INN mode**, otherwise build new. |
| Trinket shop / provisioning | Provisioner (`InnStoreUiBhv`) / Hoarder (`StoreUiBhv`) | **Reuse** `ShowDrivingStore` with own loot tables. |
| Building upgrades | `InnUpgradeDefinition` trees + `InnUpgradeCategoryWidgetBhv` / `InnUpgradeTreePanel` | **Reuse data model + widgets** (CSV-driven), ideally with a persistent `InnInstance` representing the hamlet. |
| Roster of 10–20 heroes | Kingdom roster (≤ one per class, stationed in cells) | **Partial**; multiple heroes of one class needs new roster code. |
| Week tick | `KingdomDaySystem` | **Pattern only**; tightly coupled to the kingdom map/siege. |

**What the mod should reuse / hook**
* Reuse: `InnInstance` + `InnUpgradeDefinition` (+ CSV `InnUpgrade`/`Cost` blocks) as the hamlet-building data model; `InnUpgradeCategoryWidgetBhv`/`InnUpgradeButtonBhv`/`TabGroupBhv` for upgrade trees; `kingdom_actor_button` and `KingdomMapActorHeaderBhv` layout ideas for a >4 roster strip; `CostDefinition`/`CostCalculation`; `PlayerItemInventory` currencies (`gold`, `materials`, baubles).
* Hook: `RosterManager.AddMissingHeroesToRoster` / `GetIsActorInRoster` (`:424`, `:1545`) if duplicate classes are wanted; `EventRosterEntryStatusChanged`; `EventKingdomDayStateChanged` only if piggy-backing on Kingdoms; `InnInstance.GetIsInnFeatureEnabled` to gate services.
* Build new: recruit UI, tavern/abbey stress-relief UI, quest board, hamlet save data.

---

## 4. Altar of Hope / Crossroads (hero selection) / Embark

### 4.1 How the flow is wired

* New expedition: main menu calls `SetMode(GameModeType.DRIVING, …)` (`IC/Assets.Code.UI.Screens/MainMenuUiScreenBhv.cs:611`). `MapMgrBhv.BeginNewCampaign()` creates the run starting at node type `NodeType.ALTAR_OF_HOPE` when the profile has candles, else `NodeType.BOSS_SELECT` (`IC/Assets.Code.Map/MapMgrBhv.cs:987-997`).
* The Altar and the Crossroads are **road nodes**: their tile carries a `TriggerGameModeBhv` which shows the "enter node" prompt and then calls `SetMode(m_SetGameModeType, isLoad: false, null, true)` for `HERO_SELECT` / `ALTAR_OF_HOPE` (`IC/Assets.Code.Map.Triggers/TriggerGameModeBhv.cs:33-50`). Both scenes are loaded automatically on mode entry (`m_isSceneLoadedAutomatically`, `GameModeType.cs:45`, `:59`, `:223-272`). Leaving either sets mode back to `DRIVING` (`AltarOfHopeBhv.EndAltarOfHope()` `IC/Assets.Code.AltarOfHope/AltarOfHopeBhv.cs:103-108`; `HeroSelectBhv.ConfirmRosterSelection()` `IC/Assets.Code.Campaign/HeroSelectBhv.cs:1938`).

### 4.2 Altar of Hope = a clickable diorama (closest thing to a DD1 hamlet screen)

Bundle-verified `scenes_scenes_altar_of_hope.bundle` (root at x = 3500):

```
Altar Of Hope Manager        AltarOfHopeBhv
altar_of_hope_kingdom
  layers
    shared (background plane, vfx)
    blend_camera (BlendActiveCameraBhv), camera_altar_of_hope_default (CinemachineVirtualCamera)
    layer_terrain_close / _mid / _far      VariableAppearanceBhv, children upgrade_level_1..4
    layer_sluice / layer_sprawl / layer_mountain / layer_foeter / layer_forest / layer_shroud ...   (layer 16 "ClickableObject")
        UIPointerHoverBhv, VariableAppearanceBhv, InjectScriptableGameObjectOnClickBhv, AltarRegionTag, Selectable
        hitbox (PolygonCollider2D), upgrade_level_1..4 visuals, camera_<region> (CinemachineVirtualCamera), vfx_root
```

* `Assets.Code.AltarOfHope.AltarRegionTag : MonoBehaviour, IPointerClickHandler, ISubmitHandler` — `m_regionKey`, `m_subScreenPrefab`, `UnityEvent<GameObject> m_onClickCmd`, `m_buildingObjects` (per-level visuals), `PlayUpgradeParticles()`, `SetSelectable(bool selectable, string lockedTooltipStr = "")` — `IC/Assets.Code.AltarOfHope/AltarRegionTag.cs:14-119`. Click → `AltarOfHopeUiBhv.OnRegionClick(GameObject subScreenPrefab)` → `SubScreenCollectionBhv.ToggleSubScreenElement(...)` (`IC/Assets.Code.ui/AltarOfHopeUiBhv.cs:501-512`); hover → `OnSelectionHoverChange` sets the title `"altar_region_" + regionKey + "_name"` and plays ambience (`:482-499`).
* Upgrade-level visuals are switched by `VariableAppearanceBhv` (`IC/Assets.Code.Utils.Behaviors/VariableAppearanceBhv.cs:7`), i.e. the same "building grows as it is upgraded" presentation DD1 uses.
* Sub-screens: `Assets/Prefabs/UI/AltarOfHope/altar_sub_screen_class_panel.prefab`, `_item_panel`, `_memory_panel`, `_general_panel`, `_cosmetic_panel`, `_collection_panel`, `_options_panel` (classes `AltarClassSubScreenBhv`, `AltarItemSubScreenBhv`, `AltarMemorySubScreenBhv`, `AltarGeneralSubScreenBhv`, `AltarCosmeticSubScreenBhv` in `IC/Assets.Code.UI.Screens/`).
* Logic: `AltarOfHopeBhv` / `AltarOfHopeSystem` / `AltarOfHopeRules` (`IC/Assets.Code.AltarOfHope/`), embark button → `AltarOfHopeUiBhv.OnEmbark()` → `ExitAltar()` (`AltarOfHopeUiBhv.cs:441-480`).

### 4.3 Crossroads hero select

* `Assets.Code.Campaign.HeroSelectBhv : MonoBehaviour, IGameModeEnterAsyncPreStart, IGameModeEnterStart, IGameModeExitComplete, IActorSpawnSource` (`IC/Assets.Code.Campaign/HeroSelectBhv.cs:60`), scene `hero_select` (root offset x = 3000; `hero_select_canvas`, `3d_root`, FMOD narration/sfx objects — bundle-verified).
* State: `List<uint> m_SelectedActorGuids` (4 slots), hero cards `HeroSelectActorUIBhv` (drag element: `Init(uint actorGuid, ResourceActor resourceActor, HeroSelectBhv screenBhv)` `IC/Assets.Code.UI.HeroSelect/HeroSelectActorUIBhv.cs:120`, `Populate(uint actorGuid, ActorInstance actorInstance, Sprite actorSprite)` `:230`), roster slots `m_HeroSelectRosterObjects` (`HeroSelectBhv.cs:88-89`).
* Picking: `OnHeroSelectSubmit(HeroSelectActorUIBhv)` `:977`, `OnRosterSlotSubmit(rosterSlot, swapped)` `:991`, `AddToFirstPossibleSlot(uint actorGuid)` `:1069`, `SetHeroSelection(uint replaceActorGuid, int index, bool playNarration)` `:1966`, `SwapActorGuidsAtRosterIndexes` `:1994`, `OnRandomCompositionButton()` `:2230`, party loadouts `SetPartyLoadout(IReadOnlyList<uint> loadoutGuids)` `:855`, path selection `TogglePathSelectionPanel()` `:724` / `SetSelectedActorPath()` `:1826`, rename `RollNewActorName()` `:1838`.
* The selected hero is shown as a rotatable 3D model: `SpawnActor(uint actorGuid)` → `ActorCreateGameObjectBhv.Instance.CreateActorGameObject(actorGuid, m_SpawnPosition, m_SpawnPosition.gameObject.layer, m_StartingAnimatorState /* "idle_neutral" */, loadSubclasses: false)` (`:2132-2138`, default state `:70`), with `DragRotatingModelBhv`.
* Confirm: `ConfirmRosterSelection()` → `EventRosterConfirmParty.Trigger(m_SelectedActorGuids, m_kingdomPreferredActorGuids)` then `SetMode(GameModeType.DRIVING, isLoad: false)` (`:1927-1955`). `RosterManager.HandleEventRosterConfirmParty` marks the four entries `PARTY` (`RosterManager.cs:242-258`).
* Candidates are whatever is `IDLE`/hero-select-valid in the roster: on entering `HERO_SELECT` the roster is refilled and `PARTY`/`RESERVE` reset to `IDLE` (`RosterManager.cs:371-380`).

### 4.4 Embark

* `Assets.Code.Embark.EmbarkBhv : SingletonMonoBehaviour<EmbarkBhv>` (`IC/Assets.Code.Embark/EmbarkBhv.cs:27`): scenes `"embark_" + biome` / `"embark_camp_" + biome` (`:32-34`), plus the additive `relationship_test` scene (`:48`). `SetNextBiome(BiomeChoice, bool startLoad)`, `EndEmbark()` → `SetMode(DRIVING)` (`:252-260`).
* Scene controller `EmbarkControllerBhv` (`[RequireComponent(typeof(PartyPresentationBhv), typeof(PlayableDirector))]`, `IC/Assets.Code.Embark/EmbarkControllerBhv.cs:15`): spawns the party (`SpawnParty` `:47-54`), plays intro/outro timelines, UI `EmbarkUiBhv` (`IC/Assets.Code.ui/EmbarkUiBhv.cs`, relationship buttons `EmbarkRelationshipBtnBhv`). Bundle-verified layout (`biome_city_scenes_all.bundle`): `EmbarkController` with `embark_ui_bhv` canvas, `Party`, `vcam_embark`, `Inn_Foreground`/`Camp_Foreground`, `Embark_City_Objects` (models, vfx, lights), fog + post-processing volume.
* This is the pre-departure scene where the four heroes stand in front of the inn in animator state `embark` and relationships are rolled.

### 4.5 Reusing hero select as "embark party" in the hamlet

* **Core is one event.** A mod party picker only needs to produce four actor guids and call `EventRosterConfirmParty.Trigger(guids, null-or-empty)`; all party bookkeeping (roster statuses, preloading art via `ActorCreateGameObjectBhv.Handle_EventRosterConfirmParty`, `IC/Assets.Code.Actor/ActorCreateGameObjectBhv.cs:138-154`) follows from it.
* **Reusing the Crossroads scene itself** (`SetMode(GameModeType.HERO_SELECT)`) is possible — it is also reachable from the debug pause menu (`IC/Assets.Code.UI.Controllers/PauseMenuUiControllerBhv.cs:641`) — but `HeroSelectBhv` is hard-wired to leave into `DRIVING` (`:1938`), resets the whole roster on entry (`RosterManager.cs:371-380`), and carries path/loadout/boss-emblem UI. Use Harmony on `ConfirmRosterSelection` (postfix or replace the mode switch) if the native screen is wanted.
* **Reusing widgets only** (recommended for a hamlet overlay): instantiate hero cards from the `hero_select` scene's `m_HeroSelectTopActorPrefab` (private serialized ref; prefab asset name not verified) or use `kingdom_actor_button` / portrait sprites, plus `Character3DPortrait` for a 3D preview and `CommonUiBhv.ShowCharacterSheet(..., heroSelectFilterParty: true)` for inspection.

**What the mod should reuse / hook**
* Reuse: Altar pattern (`AltarRegionTag` + `VariableAppearanceBhv` + `UIPointerHoverBhv` + `PolygonCollider2D` hitboxes on layer `ClickableObject` + per-region vcam + `SubScreenCollectionBhv`) as the blueprint — or even the host scene — for the hamlet; `EventRosterConfirmParty`; `HeroSelectActorUIBhv`/portrait atlases; embark scenes as a departure cut-scene.
* Hook: `TriggerGameModeBhv.Execute`, `MapMgrBhv.BeginNewCampaign` (`:987`) to change the run's first node; `HeroSelectBhv.ConfirmRosterSelection`; `AltarOfHopeBhv.EndAltarOfHope` / `AltarOfHopeUiBhv.OnEmbark`; `EmbarkBhv.EndEmbark`.

---

## 5. Travel presentation, heroes outside combat, combat arenas

### 5.1 Cameras (bundle-verified, `GameInstaller` prefab in `addressable_resources_assets_addressableresources.bundle`)

* One persistent `GameInstaller/Main Camera` (tag `MainCamera`, perspective FOV 40, `CinemachineBrain`, `PhysicsRaycaster` + `Physics2DRaycaster`, `BaseCameraBhv`, FMOD `StudioListener`). Culling mask = `Default, TransparentFX, Ignore Raycast, Water, UI, Deferred, Skybox, CharacterInteraction, CameraCollision, Stagecoach, PostCopyDepthPreFog, ClickableObject, WorldSpaceUI, WorldSpaceUINoDepth, Carriage, RoadInteractions, Decor, MaxLightsWorkaround(2)` — **not** `Characters`, **not** `Foreground`.
* Child `Character Camera` (`OverlayCamera`, `CopyCameraFrustumBhv`, FOV 35, clear = depth only) renders layers `Characters (10)` and `WorldSpacePopText (29)` on top. A further child camera renders `Characters/WorldSpaceUINoDepth/Carriage` to a texture (`RenderPipelineToTexture`).
* Scenes do not own cameras; they own `CinemachineVirtualCamera`s (`Menu Camera` in the inn, `camera_<region>` in the altar, `vcam_embark`, `DrivingMainCamera`, combat vcams) and `BlendActiveCameraBhv`. The inn additionally enables the `Foreground` layer on `Camera.main` while active (`InnPresentationBhv.cs:282`, `:322`); the combat scene has its own `FG Camera` (`OverlayCamera`) on layer 24.

### 5.2 Stagecoach driving scene (`MainScene`, bundle-verified)

```
GameMgr                 MapMgrBhv, MinimapMgrBhv, DrivingSimulationBhv, ParametersBhv, StoryBhv, StoryPresentationBhv, NodeBarkBhv
DrivingMainCamera       MapCurvatureBhv, CinemachineVirtualCamera, CinemachineCarriageBhv, CinemachineCeilingCollider, CinemachineRoadLimiter
FMOD (Driving)          DrivingAmbienceBhv, DrivingNarrationBhv, DrivingSfxBhv, StagecoachSfxBhv, RoadMaterialSfxBhv, ...
Driving_Global_PostProcessingStack (Volume), DrivingLighting (EnvironmentEffectsStackBhv)
CombatSupport (AdditivelyLoadScene), HeroSelectSupport (SkyboxSupportBhv)
DrivingUI (Canvas)      DrivingUiBhv
  Game UI               GameUIBhv: HeroRibbons (HeroRibbonContainerBhv), Minimap, StageCoachTorch, RoadIndicators, ToastContainer, ...
```

* `Assets.Code.Map.MapMgrBhv` (`IC/Assets.Code.Map/MapMgrBhv.cs:47`) generates a procedural `Map` of biome rows/lanes from tile prefabs and spawns the stagecoach (`m_stageCoachPrefab` `:84`, `GetStageCoachGameObject()` `:1413`, `GetVehicleControl()` `:1418`). Road data: `Road`, `RoadSegment`, `RoadNodeBhv` (`IC/Assets.Code.Map/`), rows/lanes/intersections in `IC/Assets.Code.Map.Generation.Row/`.
* Road pieces are addressable tile prefabs per biome: `Assets/Data/Biome/<Biome>/Map/NodeTiles/<biome>_node_*.prefab` (e.g. `city_node_dungeon`, `city_node_field_hospital`, `city_node_creature_den`, `city_node_story_assist_*`, `city_node_kingdom_inn`), `…/DecorTiles/*.prefab`, `…/BaseTilePrefabs/*.prefab`; approach cameras `Assets/Prefabs/Driving/intro_cam_trigger/cam_trigger_*_prefab.prefab`.
* Vehicle: `StageCoachVehicleControlBhv` / `AVehicleControl`, `WagonBhv`, `HorseBhv` (animator floats `"Speed"`, `"TurnSpeed"`, `IC/Assets.Code.Game.StageCoach/HorseBhv.cs:11-13`), camera rig `CinemachineCarriageBhv`.
* **Heroes are never shown walking or riding in the driving scene** — they exist only as HUD ribbons (`HeroRibbonContainerBhv`).
* Nodes are interacted with through `TriggerBhv` subclasses on the node tile (`Execute()` / `IsExecuting()` / `OnExecuteComplete()`): `TriggerCombatBhv`, `TriggerDungeonBhv`, `TriggerStoryBhv`, `TriggerStoreBhv`, `TriggerHospitalBhv`, `TriggerItemBhv`, `TriggerCacheBhv`, `TriggerWatchtowerBhv`, `TriggerScouting`, `TriggerGameModeBhv`, `TriggerNarrationBhv`, … (`IC/Assets.Code.Map.Triggers/`). This is DD2's equivalent of "rooms and curios".
* Minimap: `MinimapMgrBhv` (`IC/Assets.Code.Map.Minimap/MinimapMgrBhv.cs`, `Init(Map mapRef)` `:309`, `Reset(Map)` `:333`) with `MinimapRow/MinimapIcon/MinimapLink`; it is bound to the driving `Map` (rows × lanes). Art is separately addressable: atlas key `Minimap`, prefabs `Assets/Prefabs/UI/Minimap/node_known_icon.prefab`, `node_unknown_icon.prefab`, `line_0…5.prefab`, `line_dangerous_0…2.prefab`, `wagon.prefab`, `pulse.prefab`, `reveal_effect.prefab`, `RouteTypes/*`.

### 5.3 Where heroes are shown outside combat

Heroes are full 3D skinned models with a Unity `Animator` (bundle-verified: `hero_highwayman_assets_basegame.bundle` holds 149 `SkinnedMeshRenderer`s, 98 `AnimationClip`s, one `AnimatorController`). They are created the same way everywhere:

`SingletonMonoBehaviour<ActorCreateGameObjectBhv>.Instance.CreateActorGameObject(uint actorGuid, Transform parent, int characterRendingLayer, string characterAnimationState, bool loadSubclasses)` → `ActorBhv` — `IC/Assets.Code.Actor/ActorCreateGameObjectBhv.cs:165` (overloads by `IResourceActorAccessor` `:205` and by `string actorDataId` `:210`). The generic actor prefab is instantiated under `parent`, art loads asynchronously (`ActorBhv.IsLoading`, `OnSpawned`, `WaitForActorsToLoad`), then `SetRendererLayers` and `SetAnimatorState(characterAnimationState, randomOffset)` (`:451-500`).

| Place | Code | Animator state |
|---|---|---|
| Inn (4 heroes standing/sitting) | `PartyPresentationBhv.SpawnParty(Scene, string animatorStateOverride = null)` → `SpawnActorByActorGuid(uint partyGuid, string animatorState, int? index)` at `SpawnPositions` transforms — `IC/Assets.Code.Presentation/PartyPresentationBhv.cs:151-197`, `:199-234`; `SpawnPositions.GetSpawnPositions()` `IC/Assets.Code.Presentation/SpawnPositions.cs` | `m_DefaultAnimatorState` default `"idle_neutral"` (`:37`); inn uses `inn_idle` / `inn_hire` (`IC/Assets.Code.Combat.Animation/CommonActorAnimatorStates.cs:9-11`, `IC/Assets.Code.ui/InnReplacementActorBhv.cs:237-239`) |
| Embark scene | `EmbarkControllerBhv` + `PartyPresentationBhv` (`EmbarkControllerBhv.cs:47-54`) | `embark` (state exists in controller; assignment not verified) |
| Crossroads | `HeroSelectBhv.SpawnActor` (`HeroSelectBhv.cs:2132`) | `idle_neutral` / `hero` (the latter not verified) |
| UI 3D portrait | `RenderCharacterToTextureBhv.TrySetActiveCharacter` (`RenderCharacterToTextureBhv.cs:52-74`) | `idle_neutral` (`:33`) |
| Hunter hire | `HunterHireScreenWidgetBhv` (`:181`) | `inn_hire` |
| Relationship scene | `relationship_test` scene | `relationship_test_positive` / `relationship_test_negative` |
| Combat | `CombatPresentationBhv` + `TeamLayout` / `ActorSpacingLayout` (`IC/Assets.Code.Combat.Presentation/TeamLayout.cs:11-37`) | combat idles |

`ActorBhv` animation API (`IC/Assets.Code.Actor/ActorBhv.cs`): `SetAnimatorState(string stateName, float normalizedTime = 0f)` `:1590`, `SetIdleAnimatorState(float crossfadeTime = -1f)` `:1613`, `AttemptAnimatorTrigger(string triggerName, bool evaluateImmediately = false)` `:1464`, `AttemptAnimatorSetBool` `:1483`, `AttemptAnimatorSetInt` `:1501`, `HasAnimatorState(string)` `:1707`, `IsInAnimatorState(string)` `:1730`, `PlayTimelineAsset(TimelineAsset, DirectorWrapMode, …)` `:863`, `SetRendererLayers(int layer)` `:909`, `SetShadowEnable(bool)` `:943`, `GetCurrentAnimator()` `:1883`.

### 5.4 Hero animator states (bundle-verified for the Highwayman controller)

`highwayman_animation_controller`, single layer `Base Layer`, 46 states:

`idle_neutral`, `inn_idle`, `inn_item_antic`, `inn_item_idle`, `inn_item_recover`, `embark`, `hero`, `victory`, `Death`, `resolute`, `meltdown`, `bark_listen`, `bark_positive`, `bark_negative`, `actout_caster`, `relationship_test_positive`, `relationship_test_negative`, `deaths_door_antic`, `deaths_door_idle`, `deaths_door_exit`, `move_forward`, `move_forward 0`, `move_backward`, `move_backward 0`, `impact_small`, `impact_recover`, `impact_dodge`, `impact_backstab`, `impact_is_guarding`, `impact_was_guarded`, `Friendly_buff`, `Friendly_buff 0`, and per-skill `antic_*` / `idle_*` pairs (`antic_advance`, `idle_advance`, `antic_attack1_point_blank_shot`, `idle_attack1_point_blank_shot`, `antic_attack1_wickedSlice 0`, `idle_attack1_wickedSlice 0`, `antic_attack1_double_cross`, `idle_attack1_double_cross`, `antic_attack_grapeshot_blast`, `idle_attack_grapeshot_blast`, `antic_hipshot`, `idle_hipshot`, `antic_take_aim`, `idle_take_aim`).

Parameters (triggers unless noted): `spawn`, `move_forward`, `move_backward`, `move_complete`, `use_skill`, `miss`, `hit_reaction`, `hit_big`, `hit_kill`, `hit_friendly`, `hit_was_guarded`, `hit_is_guarding`, `hit_backstab`, `hit_recovery`, `impact_death`, `impactA`, `impactB`, `bark_positive`, `bark_negative`, `bark_listen`, `bark_exit`, `bark_meltdown`, `meltdown`, `resolute`, `actout_caster`, `battle_state_performer_start_turn`, `select_skill_<skill>` (one per skill), `status_deaths_door` (int), `can_enter_deaths_door` (bool), `target_selection_enemy` (bool), `inn_rollover_item` (bool), `Blend` (float). Code-side constants: `IC/Assets.Code.Combat.Animation/CommonActorAnimationTriggers.cs:5-35` (`slide_forward/backward/complete` exist as constants but are not parameters of this controller).

**There is no walk or run cycle.** Clips of interest: `hwm_idle_neutral_anm` (7.97 s loop), `hwm_inn_idle_A_anm` (2.97 s loop), `hwm_embark` (4.8 s loop), `hwm_hero` (4.0 s loop), `hwm_camping_idle_A_anm`, `hwm_move_forward_anm` (**0.47 s, non-looping** step) + `hwm_move_forward_recover_anm` (1.0 s), `hwm_victory_A_anm`. A catalog-wide search for `walk|run|stride|march|locomot` in animation assets finds only `man_at_arms@run_in_slowwalk_anm.fbx`. Other heroes follow the same naming (`<hero>_embark`, `<hero>_hero`, `<hero>_inn_idle*`, `<hero>_move_forward`); their controllers were not dumped individually (not verified per hero).

### 5.5 Combat arenas

* Combat = scene `combat` + one additive background scene. `CombatScenarioData.Load()` loads `"combat"` then the arena named by `m_BackgroundSceneName` (`IC/Assets.Code.Combat/CombatScenarioData.cs:313-330`); the arena comes from the constructor argument, `BattleConfigurationDefinition.m_BackgroundSceneOverride`, or biome data (`:260-274`; `BiomeData.GetCampAmbushCombatArena()` used at `InnBhv.cs:372`).
* Arena scenes are addressable by short name: `combat_arena_<biome>_<kind>` with biome ∈ `caves, city, coast, farm, forest, tundra, valley, mountain, catacombs(DLC)` and kind ∈ `faction, cultist, gaunt, gaunt_chirurgeon, creature_den, pillager, military, resist, story_cultist, urgent_repairs, inn_defense, dungeon_exterior, dungeon_interior, boss_*, barricade_gang_*`; plus `combat_arena_kingdom_camp_ambush`, `combat_arena_stressworld`, `combat_arena_hero_story_*`. (147 scenes under `Assets/Scenes/Combat`.)
* **Arenas are real 3D environments, not layered sprites.** Bundle-verified `combat_arena_city_dungeon_interior`: 167 GameObjects, 142 `MeshRenderer`+`MeshFilter` props (bookcases, pillars, banners) spread over x ≈ −15…13, z ≈ 3…28, point lights, particle systems / `VisualEffect`s, plus `background_properties_*` (material properties, post-processing `Volume`, `FogVolume`) and an FMOD ambience object. No camera, no actors.
* The `combat` scene supplies everything else (bundle-verified): `Arena` root (`ArenaBhv`, `CombatPresentationBhv`, `PlayableDirector`), `Shared/TeamPositions` (`DepthSorterBhv`) → `Team 0` / `Team 1` (`TeamLayout`, `TweenGroup`) → `Line Spacing` / `Zoom In Spacing` (`ActorSpacingLayout` with `ActorSpacingElement`s) on layer `Characters`, `FG Character Layer/FG Camera`, post-processing volumes, `CombatUI` canvas (`CombatUiBhv`).
* Launching a fight from anywhere (pattern at `InnBhv.cs:367-374` and `TriggerCombatBhv.cs:114-183`): `new CombatScenarioData(string battleConfigurationId, string backgroundScene, CombatSource loadedFrom[, IReadOnlyList<uint> startingPartyActorGuids])` (`CombatScenarioData.cs:210`, `:215`) → `Singleton<GameTypeMgr>.Instance.SetCombatScenario(data, isLoad: true)` (`IC/Assets.Code.Game/GameTypeMgr.cs:428`) → `SetMode(GameModeType.COMBAT, isLoad: false)`. Multi-fight "lair" sequences with a retreat prompt already exist (`BattleConfigurations` list, `CommonUiBhv.ShowDungeonConfirmationDialog` `:2785`).

### 5.6 Could a corridor be assembled from an existing arena?

Yes in principle (not verified in a running game):

* Load an arena additively by name: `RedHookSceneManagerBhv.LoadSceneAdditively(string sceneName, UnityEngine.Object source, bool setActive = false)` (`IC/Assets.Code.Loading/RedHookSceneManagerBhv.cs:305`) or `LoadSceneAsync(string address, LoadSceneMode = Additive)` (`:709`); unload with `UnloadAdditiveScene` / `UnloadAdditiveSceneByForce` (`:347`, `:352`). The `dungeon_interior` / `dungeon_exterior` arenas per biome are natural "room"/"corridor" sets; an arena is ~30 units wide, so a corridor is either one arena with the party translated / camera dollied across it, or several arena scenes instanced at x-offsets (other scenes already live at x = 2000/2500/3000/3500).
* Spawn the party with `ActorCreateGameObjectBhv.CreateActorGameObject(guid, parentTransform, LayerMask.NameToLayer("Characters"), "idle_neutral", false)` under mod-owned transforms (or reuse `PartyPresentationBhv` + a `SpawnPositions` component the mod adds to its own scene root); add a mod `CinemachineVirtualCamera` with high priority for the side-on view. The character overlay camera will draw them.
* Walking: no cycle exists, so the options are (a) repeat the `move_forward` trigger (a 0.47 s hop + 1 s recover) while translating the party, (b) keep `idle_neutral` and scroll the environment/camera, (c) author a walk clip per hero and inject it with an `AnimatorOverrideController` (17+ heroes, real animation work), (d) drive bones procedurally.
* Arena lighting, fog and post-processing assume the combat camera framing; `FogVolume`/`Volume` objects in the arena are box-collider triggered (`Fog_Combat_City` has a `BoxCollider`), so a moving camera may leave the tuned volume.

**What the mod should reuse / hook**
* Reuse: `ActorCreateGameObjectBhv.CreateActorGameObject` + `ActorBhv` animation API; `PartyPresentationBhv`/`SpawnPositions`; arena scenes via `RedHookSceneManagerBhv`; the persistent Cinemachine main camera + character overlay camera; `CombatScenarioData` + `GameTypeMgr.SetCombatScenario` for fights; node `Trigger*Bhv` semantics, `ShowEnterNodeScreen`, `ShowDungeonConfirmationDialog`; minimap icon/line prefabs and the `Minimap` atlas (with a mod-owned layout); `HeroRibbonContainerBhv` if the corridor runs in `DRIVING` mode.
* Hook: `GameModeMgr` enter/exit of `COMBAT` to return to the corridor; `MapMgrBhv` only if the corridor is implemented as a disguised driving map.
* Build new: corridor controller (movement, room graph, curio/trap spawns), walk presentation, dungeon minimap layout.

---

## 6. Narration & story

### 6.1 Narrator barks

* `Assets.Code.Audio.Narration.NarrationMgr : SingletonMonoBehaviour<NarrationMgr>` (`IC/Assets.Code.Audio.Narration/NarrationMgr.cs`):
  * `void Queue(NarrationType narrationType, IReadOnlyList<string> queueTags = null)` (`:427`) — picks a weighted random `NarrationEntryDefinition` of that type honouring type chance, game-type exclusions, occurrence limits, tag filters (`m_allTags/m_anyTags/m_avoidTags`), cooldown (`m_timeLimitSeconds`, default 180) and `GuaranteeType { None, Always, ProfileFirst }` (`GenerateEntryBag` `:353-425`).
  * `void Queue(EventReference eventRef, int secondsToCooldown = 180)` (`:483`) — queue a specific FMOD event.
  * `void QueueEntryImmediate(NarrationEntryDefinition def)` (`:515`) — clear queue, stop current, play this entry.
  * `ClearQueue()` `:523`, `Stop()` `:528`, `bool HasNarration(NarrationType, tags)` `:541`.
  * Playback: `PlayQueueElement` plays through `AudioMgr.Play(eventRef, null, ref m_currentEventInst, 8, paramData)` and starts subtitles `m_subtitleState.Start(queueElement.m_subtitlesLocKeyBase)` (`:581-607`); FMOD timeline marker `"Next"` advances the subtitle segment (`:567-579`).
* Types: `NarrationType` custom enum (`IC/Assets.Code.Audio.Narration/NarrationType.cs`) — e.g. `INN_AVAILABLE_RECRUIT, INN_MASTERY, INN_ROUTE_SELECT, INN_STORE, INN_TRAVELOGUE, INN_WAINWRIGHT, INN_WAINWRIGHT_REPAIR, INN_RUN_END, HOARDER_BUY_ITEM, HOSPITAL_BUY_ITEM, HOSPITAL_BUY_SERVICE, RECRUIT_HERO, ARRIVE_NODE, BIOME_START, COMBAT_VICTORY, HERO_DEATH, MONSTER_KILL*, LOOT_OPEN, ALTAR_OF_HOPE_*, KINGDOM_*`, …
* Data: CSV `StreamingAssets/Excel/narration_entry_data_export_<group>.Group.csv` and `narration_type_data_export_<group>.Group.csv` (groups: affinity, altar_of_hope, combat, driving, hero_select, hero_story, inn, kingdom, loot, run, store). Entry format:
  ```
  element_start,inn_mastery_01,NarrationEntry
  m_type,inn_mastery,
  m_audioEventId,event:/vo/inn/trainer_open_general_01,
  m_occurrenceTypes,inn,
  m_maxOccurrences,1,
  element_end
  ```
  parsed by `NarrationEntryDefinition(string id, string csvText)` (`IC/Assets.Code.Audio.Narration/NarrationEntryDefinition.cs:72-124`); library `LibraryNarrationEntry` with `GetEntriesOfType(NarrationType)` and `GetEntryByEventPath(string path)` (`IC/Assets.Code.Audio.Narration/LibraryNarrationEntry.cs:31-59`).
* Scene-side triggers: `PlayNarrationBhv.OnPlaySignal()` (timeline signal → `Queue(type, tags)`, `IC/Assets.Code.Audio.Narration/PlayNarrationBhv.cs:20-33`), per-mode listeners `InnNarrationBhv`, `DrivingNarrationBhv`, `CombatNarrationBhv`, `HeroSelectNarrationBhv`, `AltarOfHopeNarrationBhv`, `StoreNarrationBhv`.

### 6.2 Subtitles

* Loc key convention: `NarrationUtils.GetSubtitlesLocKeyBase(string audioEventPath)` strips `event:/` and replaces `/` with `_` (`IC/Assets.Code.Audio.Narration/NarrationUtils.cs:106-113`); segment keys are `<base>_<n>` (`SubtitleState.LocKey`, `IC/Assets.Code.Subtitles/SubtitleState.cs:15-28`). Example (file-verified): event `event:/vo/inn/trainer_open_general_01` → `vo_inn_trainer_open_general_01_0=Learn what can be taught, …` in `Localization/Sources/subtitles.txt:384`, and `msgctxt "vo_inn_trainer_open_general_01_0"` in `Poedit/ru.po`.
* `SubtitleState.Start(string locKeyBase)` / `NextSegment()` / `Clear()` raise `EventSubtitleStateChanged.Trigger(bool isActive, string locKey)` (`SubtitleState.cs:36-58`; event class `IC/Assets.Code.Subtitles.Events/EventSubtitleStateChanged.cs:5-20`).
* `Assets.Code.Subtitles.SubtitlesMgr` listens (`IC/Assets.Code.Subtitles/SubtitlesMgr.cs:112`, `:144-154`) and calls `SubtitlesUtils.TryUpdateDisplay(...)`, which writes `Localization.GetString(locKey)` into data-context key `"subtitles_text"` and plays the fade-in/out timelines, respecting the `SUBTITLES_ENABLED` option (`IC/Assets.Code.Subtitles/SubtitlesUtils.cs:14-42`). Position is per game mode with overrides: `AddTransformOverride(UnityEngine.Object source, StandardSubtitlePosition position, string subId = null)` / `RemoveTransformOverride(source, subId)` (`SubtitlesMgr.cs:263`, `:281`). Prefab `Assets/Prefabs/UI/Subtitles/SubtitlesMgr.prefab`.
* Cinematic (video) subtitles are a separate timed system: `CinematicSubtitlesMgr` + `cinematic_subtitles_data_export.Group.csv` (`IC/Assets.Code.Subtitles/CinematicSubtitlesMgr.cs`).

### 6.3 Story / choice screens

* `Assets.Code.Story.StoryBhv` (on `GameMgr` in `MainScene`, so **only available while the driving scene is loaded**) listens for `EventStoryTriggered` and opens the story screen (`IC/Assets.Code.Story/StoryBhv.cs:47-76`).
* Trigger: `EventStoryTriggered.Trigger(StoryType storyType, IReadOnlyList<string> drawTags, IReadOnlyList<string> anyTags, string backgroundScene, string battleConfigurationTableId, uint biomeKillContractGuid, string nodeSubType, Sprite texture, string titleLoc, StoryStateMachine.ShowResultDelegate showResultDelegate, Color tintColor)` (`IC/Assets.Code.Story.Events/EventStoryTriggered.cs:46`).
* Story types `ASSISTANCE, RESISTANCE, COSMIC, HERO, CULTIST, CREATURE_DEN, OASIS, GAUNT_CHIRURGEON` (`IC/Assets.Code.Story/StoryType.cs:10-24`); state machine `INACTIVE → START → CHOOSE → ALIGNMENT → EFFECT → SHOW_RESULT → APPLY_RESULT → APPLY_COST → END` (`StoryState.cs`). Each party hero offers a `StoryChoiceDefinition` (CSV `story_choice_export_*.Group.csv`), with outcomes including loot, effects and combat (`StoryCalculation.cs:433` sets `COMBAT`).
* UI: `StoryScreenBhv` (`IC/Assets.Code.UI.Screens/StoryScreenBhv.cs`), `StoryChoiceButtonBhv`, `StoryChoicePreviewBarBhv`; prefab `Assets/Prefabs/UI/Screens/Story/Story_Prefab.prefab` (own bundle `ui_assets_story_prefab.bundle`), shown via `CommonUiBhv.ShowStory(StoryScreenUiPushParams)` (`:2631`) on layer `Story`. Art `Assets/Art/UI/StoryArt/*`.
* Hero speech bubbles: `BarkSpawnerSingleton.SpawnBark(...)` with a loc key (`BarkSpawnerSingleton.cs:83`, `:94`); bark key cascade helper `LocalizationCascadingLookup.GetLocKey(...)` (`IC/Assets.Code.Locale/LocalizationCascadingLookup.cs:114-171`).

### 6.4 How a mod can play an existing line or show custom subtitle text

* Existing line by entry id: `var def = SingletonMonoBehaviour<Library<string, NarrationEntryDefinition>>.Instance.GetLibraryElement("inn_mastery_01"); SingletonMonoBehaviour<NarrationMgr>.Instance.QueueEntryImmediate(def);` (library accessor pattern as in `NarrationMgr.cs:439`).
* Existing line by FMOD path: `NarrationMgr.Instance.Queue(AudioEventUtils.MakeEventReference("event:/vo/inn/trainer_open_general_01"), secondsToCooldown: 0)` — subtitles follow automatically from the event path.
* By type with tags: `NarrationMgr.Instance.Queue(NarrationType.INN_STORE, new[] { "tag" })` (subject to the type's chance / occurrence limits).
* Custom text without audio: add a loc key (section 7) and call `EventSubtitleStateChanged.Trigger(true, "estate_my_line_0")`, later `EventSubtitleStateChanged.Trigger(false, null)`. The key must have exactly one value (`SubtitleState.ValidateLocKey`, `SubtitleState.cs:60-66`). Showing arbitrary non-key text needs a Harmony prefix on `SubtitlesUtils.TryUpdateDisplay` or direct access to `SubtitlesMgr.m_dataContextBhv`.
* New voiced lines need an FMOD bank; the official mod pipeline loads mod banks (`ModMgr.LoadModAudio` → `AudioBankMgr.PopulateModBankList(path, names)`, `IC/Assets.Code.Mod/ModMgr.cs:205-211`). Not investigated further.

**What the mod should reuse / hook**
* Reuse: `NarrationMgr.Queue*`, `EventSubtitleStateChanged`, `SubtitlesMgr.AddTransformOverride`, `BarkSpawnerSingleton.SpawnBark`, the Story screen for curio-style choices (when `MainScene` is loaded), `NarrationEntry` CSV blocks for new narration ids pointing at existing events.
* Hook: `SubtitlesUtils.TryUpdateDisplay` (free text), `NarrationMgr.IsNarrationTypeAllowed` (`:151`) if lines are blocked by game state.

---

## 7. Localization

### 7.1 Lookup

* `Assets.Code.Locale.Localization : Singleton<Localization>` (`IC/Assets.Code.Locale/Localization.cs:20`):
  * `string GetString(string key, bool showMissingLocWarning = true)` (`:276`) — missing keys render as `<color=#00FFFF>key</color>` (`:210-217`, `:258-261`).
  * `string TryGetString(string key, bool showMissingLocWarning = false)` (`:267`) — returns `null` if missing.
  * `IReadOnlyList<string> GetLocalizedStrings(string key)` (`:160`) — a key may have several values; `GetString` picks one at random (`:229-242`). A controller-specific variant `gamepad_<key>` is preferred when a gamepad is active (`:176-189`).
  * `string GetSubstitutedText(string sourceText, params string[] substitutions)` (`:390`) — pairs of `"${token}", value`; also expands `#{otherKey}` (nested loc key, used for colours such as `<color=#{notable}>`), `@{InputAction}` (binding glyph/name), `\n`, `{q}` (double quote), `{faction}`, `{version}` (`:312-388`).
  * `string GetPluralizationKey(string locKey, int qty)` (`:395`) → language-specific plural variants (Russian handled, `IC/Assets.Code.Locale/LanguageDefinition.cs:401-420`).
  * `void AddLoadPath(string loadPath, int priority)` (`:150`), `void UnloadStringsAtPath(string loadPath)` (`:155`).
* Components: `LocalizeTextBhv` (public field `locKey`, refreshes on `EventLanguageFallbacksSet`, `IC/Assets.Code.Locale/LocalizeTextBhv.cs:11-22`), `UiDisplayTextBhv` (section 1.3), `LocalizedTextTooltipBhv`, `LocalizeDropdownBhv`.

### 7.2 File format and location

* Root `Application.streamingAssetsPath + "/Localization/"` (`IC/Assets.Code.Locale/LocalizationUtils.cs:30`).
* **English**: every `*.txt` directly inside a load path (`Directory.GetFiles(path, "*.txt", SearchOption.TopDirectoryOnly)`, `IC/Assets.Code.Locale.Sources/TextFileSource.cs:64`), UTF-8 (`Localization.DefaultEncoding`, `Localization.cs:22`), one `key=value` per line, `#` comment lines, split at the **first** `=` (`IC/Assets.Code.Locale/LocalizationSourceParser.cs:9-11`, `:32-44`). Repeating a key adds a random variant. Base path `Localization/Sources/` (`LocalizationUtils.cs:38`) with sub-folders `dlc_catacombs`, `dlc_dul_cru`, `game_type_override_expedition`, `game_type_override_kingdom`.
* **Other languages**: gettext `<iso>.po` inside a load path (`ForeignLocalizationData.LoadStringsAtPath`: `Path.Combine(loadPath.m_path, m_iso + ".po")`, `IC/Assets.Code.Locale/ForeignLocalizationData.cs:57-58`); **`msgctxt` is the loc key, `msgstr` the text** (`:64-66`). Base path `Localization/Poedit/` (`LocalizationUtils.cs:40`); shipped: `cs, de_DE, es, es_lat, fr, it, ja, ko, pl, pt_BR, ru, tw_CN, uk, zh_CN`. Parser rules (`IC/Assets.Code.Locale/PoFile.cs:66-171`): an entry must start with a `msgctxt "…"` line (header entry without `msgctxt` is ignored), entries are separated by a blank line, continuation lines start with `"`, entries with empty `msgstr` are dropped (`:114`), a suffix after `@` in `msgctxt` is trimmed so duplicates become variants (`:117-118`).
* Available languages = `*.po` files found in `Poedit/` (`LocalizationUtils.GetValidForeignLanguages`, `:191-219`) + English; a language is valid if its `.mo` (in `Localization/Binary/`) or `.po` parses (`LanguageDefinition.HasValidTranslation`, `LanguageDefinition.cs:252-266`).
* Priorities (`LocalizationUtils.cs:52-58`): `PRIORITY_BASE_GAME = 0`, `PRIORITY_DLC = 1`, `PRIORITY_GAME_TYPE_OVERRIDE = 2`, `PRIORITY_MOD = 3`. For a key present in several paths the highest priority wins (`LoadedStrings.GetActiveEntryForLocKey`, `IC/Assets.Code.Locale/LoadedStrings.cs:76-92`). Game-type override folders are added/removed on `EventGameTypeStarted/Ended` (`Localization.cs:65-75`).
* **There is no English fallback for foreign languages**: `ForeignLocalizationData.GetLocalizedStrings` returns only `.po` data (`ForeignLocalizationData.cs:102-109`), so a key missing from `ru.po` shows as the cyan key.

### 7.3 How a mod adds keys for English + Russian

Two supported routes:

1. **Native mod folder** (no code): the game's own `ModMgr` scans `StreamingAssets/mods/<mod>/` (`ModMgr.nexusModPath`, `IC/Assets.Code.Mod/ModMgr.cs:31`) and Workshop mods added through `AddMod(ModInfo)` (`:405`). A sub-folder `localization` with files is registered with `Singleton<Localization>.Instance.AddLoadPath(dir.FullName, 3)` (`:289`, `:306-310`). Put `estate_strings.txt` (English) and `ru.po` (Russian) side by side in that folder. A shipped sample exists at `StreamingAssets/mods/test_token_creation_export/` (`Localization/dd2_mod_strings_token.txt`, `manifest.json` with `title/description/version/game_mode`, `Assets/catalog.json` + bundle, `*.Group.csv`).
2. **From the BepInEx plugin**: call `Singleton<Localization>.Instance.AddLoadPath(<plugin dir>/localization, 3)` once localization is initialized, with the same two files.

Caveat for both (code-verified): the load path is stored on the *current* `ILocalizationData` object only (`Localization.AddLoadPath` → `m_loadedLocData.AddLoadPath`, `Localization.cs:150-153`), and `SetLanguage` replaces that object with a fresh one from `languageDef.LoadTranslation()` (`:138-145`; `LanguageDefinition.cs:423-434`). After a language switch the extra path is gone until re-added. The plugin should re-add it on `EventLanguageChanged` with a listener priority below 1000 (Localization's own handler is registered with 1000, `Localization.cs:46`; `CommonUiBhv` at 999 starts the async font-fallback load and then fires `EventLanguageFallbacksSet`, `CommonUiBhv.cs:925`, `:1242-1273`), or postfix `Localization.SetLanguage`.

Russian rendering: Russian is not in `IsFallbackReliant` (`LanguageDefinition.cs:228-238`), so it uses the default title/body fonts with `m_DefaultFallbackList`; Cyrillic coverage of those TMP assets is implied by the shipped `ru.po` but glyph tables were not inspected (not verified).

**What the mod should reuse / hook**
* Reuse: `Localization.GetString/TryGetString/GetSubstitutedText`, `LocalizeTextBhv`, `UiDisplayTextBhv` auto-localization, `#{colour}` substitutions already defined in `Sources/colors.txt`, native `localization` mod folder format.
* Hook: `EventLanguageChanged` (or postfix `Localization.SetLanguage`) to re-register the mod load path; optionally postfix `Localization.GetLocalizedStrings` to fall back to English for keys missing in other languages.
* Ship: `localization/estate_*.txt` (English, `key=value`) and `localization/ru.po` (`msgctxt`/`msgid`/`msgstr` blocks, UTF-8, blank-line separated). Prefix keys (e.g. `estate_`) to avoid collisions.

---

## 8. Loading custom content at runtime

### 8.1 How the game loads sprites / prefabs

* Everything ships as Addressables (`StreamingAssets/aa/catalog.json`: 32 079 internal ids, 810 bundles, 31 269 assets; labels `BaseGame`, `dlc_catacombs`, `dlc_dul_cru`, `dlc_origin_skins`, `dlc_supporter`, biome labels). Providers are the stock `AssetBundleProvider` / `BundledAssetProvider` — no custom provider or encryption (catalog `m_ProviderIds`).
* `Assets.Code.Resource.ResourceDatabaseAddressable<ResourceDatabaseType, ResourceType> : Singleton<…>` (`IC/Assets.Code.Resource/ResourceDatabaseAddressable.cs:19`): at start-up it loads all resource *locations* of `ResourceType` for owned DLC labels into `m_ResourceLocationDictionary` keyed by asset name (`:35-55`); `GetResource(string resourceId, bool isErrorValid = true, object reference = null)` loads synchronously (`LoadAssetAsync(location)` + `WaitForCompletion()`, `:114-125`); `CleanAssetReference`, `UnloadLoadedAssets`. Concrete databases: `ResourceDatabaseActors`, `ResourceDatabaseItem`, `ResourceDatabaseSkills`, `ResourceDatabaseInns`, `ResourceDatabaseInnUpgrade`, `ResourceDatabaseActorPalettes`, … Item icons, hero portraits etc. hang off these ScriptableObjects (`ResourceItem`, `ResourceActor`).
* `Assets.Code.Resource.AddressableReferencesManager : Singleton<…>` (`IC/Assets.Code.Resource/AddressableReferencesManager.cs`) — reference counting / handle registry: `AddPreload<T>(object runtimeKey)` `:139`, `RemovePreload(object runtimeKey)` `:157`, `AddReference`/`ReleaseReference` `:186-211`, `IsLoading(AssetReference)` `:242`, `GetHandle<TObject>(string assetPath)` `:265`.
* `AssetReferenceT<T>` helpers: `IEnumerator SafeLoad<AssetType>(this AssetReferenceT<AssetType>, Action<bool, AssetType> onLoaded)` (`IC/Assets.Code.Utils/AddressableUtils.cs:113`), `SafeLoadHandle` (`:143`), `SafeRelease` (`:170`).
* Text data: `ResourceDatabaseText` / `ResourceGroupCsvDatabase` read `StreamingAssets/Excel/*.Group.csv` (`element_start,<id>,<Type>` … `element_end` blocks).
* Scenes: `RedHookSceneManagerBhv.LoadSceneAdditively` / `LoadSceneAsync` by addressable scene name (`RedHookSceneManagerBhv.cs:305-320`, `:709`).
* Official mod content path (useful as proof that external bundles work): `ModMgr.LoadModAddressables` calls `Addressables.LoadContentCatalogAsync(mod.modCatalogPath, autoReleaseHandle: true)` on the mod's own `catalog.json` and merges every location labelled `"Mod"` into the matching `ResourceDatabaseAddressable` (`IC/Assets.Code.Mod/ModMgr.cs:148-203`, `:331-349`), calling `AssignModdedOverrides(resourceId, locator)` when the database defines it (`:187-199`; base no-op at `ResourceDatabaseAddressable.cs:308`). `PopTextManager.AddModPrefabToken(GameObject prefab, string name)` exists for mod token pop-text (`IC/Assets.Code.ui/PopTextManager.cs:294`). Mods are (re)loaded on entering `MAIN_MENU` (`ModMgr.cs:379-390`).

### 8.2 Can a plugin put its own AssetBundle / PNG on a native-looking canvas?

Nothing found that prevents it:

* **Canvas**: all native UI roots are `ScreenSpaceOverlay` canvases (table in 1.1), which need no camera, no URP renderer feature and no special sorting layer. A plugin canvas, or better a child of a `ScreenStackBhv` layer, composites exactly like native UI. Suggested overlay order if a private canvas is used: > 10 (screen stack) and < 30 (`CommonUiBhv`), or parent under a stack layer.
* **Shaders / materials**: `UI/Default` and `Sprites/Default` are in the player's Always-Included list (bundle-verified from `globalgamemanagers`), so `Image`, `RawImage` and `SpriteRenderer` with default materials work for runtime-created textures. Native UI images use the default material (`mat=0` on dumped `Image`s); special effects use optional materials (`uiblur.mat` on the blur canvas, `Red Hook/UI/UIBlurrable`, `Red Hook/UI/UIInkDissolve` via `DissolveManager`, Coffee `UIEffect`/`UIShiny`/`UITransitionEffect`/`UIParticle` components) — none are required.
* **Text**: TMP shaders are *not* always-included (they ship in the fonts bundle: `Assets/Includes/TextMesh Pro/Shaders/TMP_SDF.shader` etc.), so a plugin should not rely on `Shader.Find("TextMeshPro/Distance Field")`; assign a game `TMP_FontAsset` (its material carries the shader) from `CommonUiBhv` or load `Font/NDDunkelD-Bold SDF.asset` / `AlegreyaSans-Regular SDF`. `TMP Settings.asset` and the default style sheet are addressable (`Assets/Includes/TextMesh Pro/Resources/TMP Settings.asset`).
* **PNG → Sprite**: `new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)` + `ImageConversion.LoadImage(tex, bytes)` + `Sprite.Create(...)`. Project colour space is Linear (PlayerSettings `m_ActiveColorSpace = 1`), so leave the texture sRGB (the default) for UI art. Keep a strong (static) reference: every game-mode change runs `Resources.UnloadUnusedAssets()` (`GameModeMgr.cs:451-461`) and destroys object pools (`:407`).
* **Own AssetBundle**: `AssetBundle.LoadFromFile` is unaffected by Addressables. Constraints: build with Unity 2022.3 for StandaloneWindows64; do not include game scripts' `MonoScript`s unless the assembly/namespace/class names match (`IronCrown`, `Assembly-CSharp`); materials in the bundle must use shaders included in the bundle or always-included ones (game shaders such as `Red Hook/Lit/Environment`, `Red Hook/Lit/Hero` are addressable assets, not globally findable — reference them at runtime by loading the addressable shader/material, not by name at build time). Alternatively ship an Addressables catalog and load it like `ModMgr` does.
* **World-space / 3D content**: the main camera does not render `Characters (10)` or `Foreground (24)`; those are drawn by overlay cameras (section 5.1). Put world objects on `Default`/`Decor`, heroes on `Characters`. URP custom renderer features exist (`IC/Assets.Code.Rendering.RendererFeatures/`: `DeferredRenderFeature`, `TransparentRenderObjectsFeature`, `GPUCullFeature`, outline, fog, LUT, bloom, blur) and post-processing volumes are per scene; a plain unlit sprite/mesh renders, but matching the game's lit/outlined look requires the game's materials (not verified in-game).
* **Clickable world objects**: `PhysicsRaycaster` and `Physics2DRaycaster` are on the main camera, so `IPointerClickHandler` on a collider works (the altar uses `PolygonCollider2D` hitboxes on layer `ClickableObject`).
* **Input**: uGUI events come from the game's `EventSystem`; a plugin should not add a second `EventSystem`/input module. `ReplayInputModule` exists but disables itself (`IC/Assets.Code.Inputs/ReplayInputModule.cs:37-39`).

**What the mod should reuse / hook**
* Reuse: `Addressables.LoadAssetAsync<T>(key)` with keys from `_ref/_scratch/catalog_map.json` for native sprites/prefabs/fonts/scenes; `ResourceDatabase*` singletons for item/actor/skill art; `RedHookSceneManagerBhv` for scenes; `ScreenStackBhv` layers as parents; native mod-folder catalog loading as an optional delivery path for big art.
* Hook: nothing mandatory. Optional: postfix `ModMgr.LoadModInfo` only if piggy-backing on the native mod list.
* Build: a tiny asset cache in the plugin (static dictionary of `Texture2D`/`Sprite`) to survive `UnloadUnusedAssets`.

---

## Appendix A — Open unknowns and risks

1. **No hero walk cycle** (5.4). Corridor "walking" needs a presentation compromise or new animation clips for every hero class.
2. **Runtime `AddComponent` of native UI classes** leaves serialized reference fields null (`UiScreenBhv.m_selectFirstObjectEvent`, `DataContextBhv.m_Values`); must be patched via reflection or avoided by cloning native prefabs. Not verified in a running game.
3. **Mode coupling.** Mastery trainer, inn store, rest/inn items, hero replacement and wainwright require a live `InnSystem`/`InnPresentationBhv` (INN mode). Story screens require `MainScene` (DRIVING). A hamlet as a pure overlay gets only the standalone set (hero sheet, inventory, store, hospital, dialogs).
4. **Roster is one hero per class** (3.4); DD1-style duplicates and a recruit pool need new roster code and save data.
5. **Mode changes are destructive**: screen stack cleared, pools destroyed, unused assets unloaded; a persistent hamlet/corridor must survive `COMBAT` round-trips and re-create its UI.
6. **Arena reuse**: lighting/fog/post volumes are tuned for the static combat camera; multi-arena corridors and moving cameras are untested. Arena scenes contain no walkable metadata.
7. **Localization load paths are lost on language switch** (7.3); no English fallback for foreign languages.
8. **Font field mapping** on `CommonUiBhv` and Cyrillic glyph coverage were not inspected.
9. `CharacterSheetUiBhv` and `InventoryUiBhv` reference `KingdomBhv`/`InnPresentationBhv` in several branches; each needs checking when opened from a new context.
10. Only the Highwayman animator controller was dumped; other heroes inferred from clip file names.
11. Where a brand-new FMOD narration bank would be registered for a BepInEx plugin (vs. the native mod folder) was not investigated.

## Appendix B — Selected addressable keys (decoded from `aa/catalog.json`)

* Scenes (key = short name): `inn`, `camp`, `altar_of_hope`, `hero_select`, `MainScene`, `combat`, `combat_results`, `cinematic`, `relationship_test`, `main_menu`, `main_menu_kingdom`, `embark_<biome>`, `embark_camp_<biome>`, `embark_kingdom_<biome>`, `combat_arena_<biome>_<kind>`; kingdom maps use the full path `Assets/Scenes/Kingdoms/kingdom_map_0N.unity`.
* System prefabs: `AddressableResources/Systems/screen_stack.prefab`, `…/CommonUiBhv.prefab`, `…/DragCanvas.prefab`, `…/ScreenFaderBhv.prefab`, `…/ActorCreateGameObjectBhv.prefab`, `…/GameMgr.prefab`, `AddressableResources/GameInstaller.prefab`, `AddressableResources/InnSystemsInstaller.prefab`.
* UI prefabs with path keys: everything under `Assets/Prefabs/UI/…` listed in sections 1–4 **except** Kingdom prefabs (keys `UI/Kingdom/<name>.prefab`) and `screen_field_hospital_panel` (key without path/extension).
* Fonts: `Font/NDDunkelD-Bold SDF.asset`, `AlegreyaSans-Regular SDF`, `Cuprum-Italic SDF`, `Font/Hahmlet-ExtraBold SDF.asset`, `Font/ReggaeOne-Regular SDF.asset`.
* Atlases: `Minimap`, `<class>_portraits`, `<class>_story_portraits`, `<class>_skill_icons`, `Assets/Art/UI/Icons/NonTextAndTextIcons.spriteatlas`.
* Bundles holding UI prefabs: `aa/ui_assets_assets/prefabs/ui/{screens,common,canvases,screenstack,inn,items,skills,banter,combat,driving,minimap,poptext,widgets,altarofhope,mainmenu,optionsmenu,subtitles,tutorial}.bundle`; kingdom UI in `kingdoms_assets_all.bundle`.

## Appendix C — Tooling left in `_ref/_scratch/` (git-ignored)

* `catalog_map.json` — internal asset path → `{ keys: [...], type }` for all 31 269 addressable assets.
* `dump_scene.py <bundle> [depth] [maxChildren]` — prints the GameObject hierarchy of a scene bundle with component and script class names.
* `dump_node.py <bundle> <GameObjectName> [depth] [maxChildren]` — same, for one subtree.
* `dump_prefab.py <bundle> <rootName|*ROOTS*> [depth]` — prefab bundles, with `Canvas` / `CanvasScaler` / `Image` / `TextMeshProUGUI` details.

All three open bundles read-only with UnityPy plus `c54ec5a455bda9e73475662a238974c8_monoscripts.bundle` for script names.
