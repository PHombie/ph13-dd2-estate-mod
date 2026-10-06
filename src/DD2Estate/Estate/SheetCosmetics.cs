using System;
using System.Collections;
using System.Collections.Generic;
using Assets.Code.Actor;
using Assets.Code.DLC;
using Assets.Code.Game;
using Assets.Code.Library;
using Assets.Code.Profile;
using Assets.Code.UI;
using Assets.Code.UI.Managers;
using Assets.Code.UI.Widgets;
using Assets.Code.Utils;
using BepInEx.Configuration;
using DD2Estate.Dd2;
using DD2Estate.Dev;
using HarmonyLib;

namespace DD2Estate.Estate
{
    /// <summary>
    /// A hero's look is chosen on DD2's character sheet in the hamlet: the sheet's own Cosmetics tab (palettes,
    /// weapon kits, the class's other skin), with everything the installed game has for the class open at once.
    ///
    /// Why nothing could be chosen before: the tab is shown only when the profile has some cosmetic of the class
    /// unlocked (CharacterSheetUiBhv.SetTabsActive asks ResourceActorCosmetic.GetIsUnlocked, which asks the
    /// current profile, and the Estate's own profile has unlocked nothing), and it can be pressed only in DD2's
    /// inn and hero-select modes (CharacterSheetCosmeticsBhv.IsTabAvailable), which the Estate never is in.
    ///
    /// So, during an Estate session only: a cosmetic counts as unlocked whatever the profile says (the answer is
    /// given, nothing is written into any profile), and the tab is open while the hamlet is on screen; on an
    /// expedition it is DD2's own "only at an inn". What belongs to a DLC the player does not own is left as the
    /// game has it: DD2 loads no assets of such a DLC, and its own ownership questions are still asked.
    ///
    /// The choice is the hero's own: DD2 keeps it on the ActorInstance (m_ActorSkinId, m_ActorWeaponKitId,
    /// m_ActorPaletteId), which the estate's snapshot writes with the hero (actors.json), and every model of the
    /// hero is made from it (ActorCreateGameObjectBhv: the sheet's preview, the corridor, a fight, the camp).
    /// The estate is written when the sheet closes after a change. DD2 has one portrait a class, whatever the
    /// hero wears: the hamlet's roster portraits stay the class's.
    /// </summary>
    [EstateModule]
    internal static class SheetCosmetics
    {
        private static ConfigEntry<bool> _enabled;
        private static bool _changed;

        private static void Register()
        {
            _enabled = Plugin.Settings.Bind("Sheet", "Cosmetics", true,
                "In the Estate every palette, weapon kit and skin the installed game has for a hero's class can be chosen on the hero's sheet in the hamlet (nothing is unlocked in any profile). Off: only what the Estate's own profile has unlocked, as before.");
            SheetCosmeticsDev.Register();
        }

        /// <summary>DD2's words on the Cosmetics tab where it cannot be pressed: "Only available at the Inn".</summary>
        public const string InnOnlyKey = "character_sheet_cosmetics_inn_limit_tooltip_label";

        /// <summary>The Estate's words in their place (the string table is answered for in SheetColours.cs).</summary>
        public static readonly string[] HamletOnly = { "Only available in the Hamlet" };

        /// <summary>Everything the installed game has is open: an Estate session with the setting on.</summary>
        public static bool Opens => EstateSession.Active && _enabled != null && _enabled.Value;

        /// <summary>The look can be changed now: in the hamlet, not on an expedition (DD2: only at an inn).</summary>
        public static bool CanChange => Opens && EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet;

        /// <summary>A hero's look was changed on the sheet: the estate is written when the sheet closes.</summary>
        public static void Touched(uint guid)
        {
            if (!EstateSession.Active) return;
            _changed = true;
            var actor = SingletonMonoBehaviour<Library<uint, ActorInstance>>.Instance.GetLibraryElement(guid);
            if (actor != null)
                Plugin.Log.LogInfo("Sheet: " + actor.ActorName + " wears skin " + (actor.ActorSkinId ?? "-") + ", kit " + (actor.ActorWeaponKitId ?? "-") + ", palette " + (actor.ActorPaletteId ?? "-"));
        }

        public static void SheetClosed()
        {
            if (!_changed) return;
            _changed = false;
            if (EstateSession.InHub) EstatePersistence.SaveNow("cosmetics");
        }

        /// <summary>Is this cosmetic of a DLC the player owns (or of none)? DD2's own question for kits and palettes.</summary>
        public static bool Owned(ResourceActorCosmetic cosmetic)
        {
            try { return DLCManager.Instance == null || DLCManager.Instance.IsActorCosmeticDLCOwned(cosmetic.name); }
            catch (Exception) { return true; }
        }
    }

    // ---- everything open, in the Estate ------------------------------------------------------------------

    /// <summary>Palettes and weapon kits: unlocked in the Estate whatever the profile holds.</summary>
    [HarmonyPatch(typeof(ResourceActorCosmetic), nameof(ResourceActorCosmetic.GetIsUnlocked))]
    internal static class EstateOpensCosmetics
    {
        private static void Postfix(ResourceActorCosmetic __instance, ref bool __result)
        {
            if (__result || !SheetCosmetics.Opens) return;
            __result = SheetCosmetics.Owned(__instance);
        }
    }

    /// <summary>Skins: the same, less the profile's unlock; the DLC a skin needs is still asked for (DD2's own check).</summary>
    [HarmonyPatch(typeof(ResourceActorSkin), nameof(ResourceActorSkin.GetIsUnlocked))]
    internal static class EstateOpensSkins
    {
        private static void Postfix(ResourceActorSkin __instance, ref bool __result)
        {
            if (__result || !SheetCosmetics.Opens) return;
            try { __result = __instance.IsRequiredDLCOwned(); }
            catch (Exception) { }
        }
    }

    /// <summary>The Cosmetics tab can be pressed in the hamlet (DD2: at an inn and at hero select).</summary>
    [HarmonyPatch(typeof(CharacterSheetCosmeticsBhv), nameof(CharacterSheetCosmeticsBhv.IsTabAvailable))]
    internal static class EstateHamletIsAnInnForCosmetics
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && SheetCosmetics.CanChange) __result = true;
        }
    }

    // Nothing is "new" in the Estate: with everything open at once the red dots of DD2 (a cosmetic the profile
    // has not looked at yet) would sit on every button of every hero.
    [HarmonyPatch(typeof(ProfileInstance), nameof(ProfileInstance.ViewedWeaponKit))]
    internal static class EstateKitsAreNotNew
    {
        private static void Postfix(ref bool __result)
        {
            if (SheetCosmetics.Opens) __result = true;
        }
    }

    [HarmonyPatch(typeof(ProfileInstance), nameof(ProfileInstance.ViewedHeroPalette))]
    internal static class EstatePalettesAreNotNew
    {
        private static void Postfix(ref bool __result)
        {
            if (SheetCosmetics.Opens) __result = true;
        }
    }

    [HarmonyPatch(typeof(ProfileInstance), nameof(ProfileInstance.ViewedHeroSkin))]
    internal static class EstateSkinsAreNotNew
    {
        private static void Postfix(ref bool __result)
        {
            if (SheetCosmetics.Opens) __result = true;
        }
    }

    // ---- the choice is kept ------------------------------------------------------------------------------

    [HarmonyPatch(typeof(CharacterSheetCosmeticsBhv), nameof(CharacterSheetCosmeticsBhv.OnKitButtonPressed))]
    internal static class EstateKeepsWeaponKit
    {
        private static void Postfix(uint ___m_curActorGuid) => SheetCosmetics.Touched(___m_curActorGuid);
    }

    [HarmonyPatch(typeof(CharacterSheetCosmeticsBhv), nameof(CharacterSheetCosmeticsBhv.OnPaletteButtonPressed))]
    internal static class EstateKeepsPalette
    {
        private static void Postfix(uint ___m_curActorGuid) => SheetCosmetics.Touched(___m_curActorGuid);
    }

    [HarmonyPatch(typeof(CharacterSheetCosmeticsBhv), nameof(CharacterSheetCosmeticsBhv.OnHeroSkinButtonPressed))]
    internal static class EstateKeepsSkin
    {
        private static void Postfix(uint ___m_curActorGuid) => SheetCosmetics.Touched(___m_curActorGuid);
    }

    [HarmonyPatch(typeof(CharacterSheetUiBhv), nameof(CharacterSheetUiBhv.OnScreenCloseCompleted))]
    internal static class EstateWritesCosmeticsAsTheSheetCloses
    {
        private static void Postfix() => SheetCosmetics.SheetClosed();
    }

    // ---- dev bridge --------------------------------------------------------------------------------------

    /// <summary>
    /// sheet.state: the sheet as it stands (hero, tab, the tabs, the hero's look and every cosmetic of the class).
    /// sheet.tab {"tab":"Cosmetic"}: the tab is pressed. sheet.hero {"guid":3}: the sheet shows another hero.
    /// sheet.cosmetic {"kind":"palette|kit|skin","index":0}: the button is pressed (index -1 is the default of
    /// palettes and kits), as a click would. sheet.look {"guid":3}: a hero's look without the sheet.
    /// </summary>
    internal static class SheetCosmeticsDev
    {
        public static void Register()
        {
            AgentBridge.Register("sheet.state", o => State());
            AgentBridge.Register("sheet.look", o => Look(UpgradeUi.Hero((uint)o["guid"])));
            AgentBridge.Register("sheet.tab", o =>
            {
                var sheet = Sheet();
                if (sheet == null) return "the sheet is not open";
                if (!Enum.TryParse<CharacterSheetUiBhv.Tab>((string)o["tab"], true, out var tab)) return "no such tab";
                sheet.SetPanelActive(sheet.GetTabPanel(tab));
                sheet.SelectTab(tab);
                return State();
            });
            AgentBridge.Register("sheet.hero", o =>
            {
                var sheet = Sheet();
                if (sheet == null) return "the sheet is not open";
                var guid = (uint)o["guid"];
                if (UpgradeUi.Hero(guid) == null) return "no such hero";
                sheet.ActorGuid = guid;
                return State();
            });
            AgentBridge.Register("sheet.cosmetic", o =>
            {
                var sheet = Sheet();
                if (sheet == null) return "the sheet is not open";
                var panel = Panel(sheet);
                if (panel == null) return "no cosmetics panel";
                if (!panel.IsTabAvailable()) return "the cosmetics tab is shut here";
                var index = (int?)o["index"] ?? -1;
                var kind = (string)o["kind"] ?? "palette";
                var buttons = Buttons(panel, kind == "kit" ? "m_kitButtonsAdded" : kind == "skin" ? "m_skinButtonsAdded" : "m_paletteButtonsAdded");
                foreach (var button in buttons)
                {
                    if (button.Index != index) continue;
                    button.OnSubmit(null);
                    return State();
                }
                return "no such button (" + buttons.Count + " " + kind + " buttons)";
            });
        }

        public static CharacterSheetUiBhv Sheet()
        {
            if (!SingletonMonoBehaviour<CommonUiBhv>.HasInstance()) return null;
            var screen = SingletonMonoBehaviour<CommonUiBhv>.Instance.GetCharacterSheetInstance();
            return screen != null ? screen.GetWidget<CharacterSheetUiBhv>() : null;
        }

        private static CharacterSheetCosmeticsBhv Panel(CharacterSheetUiBhv sheet)
        {
            return AccessTools.Field(typeof(CharacterSheetUiBhv), "m_cosmeticPanelBhv")?.GetValue(sheet) as CharacterSheetCosmeticsBhv;
        }

        private static List<CharacterSheetCosmeticButtonBhv> Buttons(CharacterSheetCosmeticsBhv panel, string field)
        {
            return AccessTools.Field(typeof(CharacterSheetCosmeticsBhv), field)?.GetValue(panel) as List<CharacterSheetCosmeticButtonBhv> ?? new List<CharacterSheetCosmeticButtonBhv>();
        }

        private static object Look(ActorInstance actor)
        {
            if (actor == null) return "no such hero";
            var resource = Singleton<ResourceDatabaseActors>.Instance.GetResource(actor.ActorDataId).GetTopMostParent();
            return new
            {
                guid = actor.ActorGuid, name = actor.ActorName, cls = actor.ActorDataId,
                skin = actor.ActorSkinId, kit = actor.ActorWeaponKitId, palette = actor.ActorPaletteId,
                palettes = List(Singleton<ResourceDatabaseActorPalettes>.Instance.GetCosmeticsForActor(resource)),
                kits = List(Singleton<ResourceDatabaseActorWeaponKits>.Instance.GetCosmeticsForActor(resource)),
                skins = List(Singleton<ResourceDatabaseActorSkins>.Instance.GetCosmeticsForActor(resource))
            };
        }

        private static List<object> List(IEnumerable cosmetics)
        {
            var list = new List<object>();
            foreach (var entry in cosmetics)
            {
                if (!(entry is ResourceActorCosmetic cosmetic)) continue;
                list.Add(new { id = cosmetic.name, order = cosmetic.SortOrder, open = cosmetic.GetIsUnlocked(), owned = SheetCosmetics.Owned(cosmetic) });
            }
            return list;
        }

        private static object State()
        {
            var sheet = Sheet();
            if (sheet == null) return new { open = false, opens = SheetCosmetics.Opens, canChange = SheetCosmetics.CanChange };
            var panel = Panel(sheet);
            var tabs = new List<object>();
            var bar = AccessTools.Field(typeof(CharacterSheetUiBhv), "m_characterSheetTopBarBhv")?.GetValue(sheet) as CharacterSheetTopBarUiBhv;
            if (bar != null)
                for (var i = 0; i < bar.TabCount; i++)
                {
                    var tab = bar.GetTab(i);
                    tabs.Add(new { tab = tab.Tab.ToString(), shown = tab.gameObject.activeSelf, pressable = tab.Button.interactable, panel = tab.CharacterSheetPanel.activeSelf });
                }
            var buttons = new List<object>();
            if (panel != null)
                foreach (var kind in new[] { "palette", "kit", "skin" })
                    foreach (var button in Buttons(panel, kind == "kit" ? "m_kitButtonsAdded" : kind == "skin" ? "m_skinButtonsAdded" : "m_paletteButtonsAdded"))
                        buttons.Add(new { kind, index = button.Index, on = button.IsToggleOn, pressable = button.interactable });
            return new
            {
                open = true, opens = SheetCosmetics.Opens, canChange = SheetCosmetics.CanChange,
                mode = GameModeMgr.CurrentMode?.GetName(),
                skillsEditable = sheet.IsSkillsEditable, inventoryEditable = sheet.IsInventoryEditable,
                tab = sheet.ActiveTab.ToString(), tabs,
                cosmeticsTab = panel != null && panel.IsTabAvailable(),
                buttons,
                look = Look(UpgradeUi.Hero(sheet.ActorGuid))
            };
        }
    }
}
