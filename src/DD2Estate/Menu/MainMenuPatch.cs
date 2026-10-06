using System.Collections.Generic;
using Assets.Code.Locale;
using Assets.Code.UI.Screens;
using Assets.Code.UI.Tooltips;
using DD2Estate.Dd2;
using DD2Estate.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Menu
{
    /// <summary>Adds the "Estate" entry under Confessions and Kingdoms on the main menu.</summary>
    [HarmonyPatch(typeof(MainMenuUiScreenBhv), nameof(MainMenuUiScreenBhv.Show))]
    internal static class MainMenuPatch
    {
        private const string ContainerName = "EstateHorizontalContainer";

        /// <summary>
        /// The entry's tooltip, as the game's own entries have one (Kingdoms: "Defend a vulnerable realm from
        /// merciless aggressors. Kingdoms is a stand-alone strategic campaign that remixes the core mechanics of
        /// the base game."). The game's tooltip asks its string table for a key; this key is the mod's own and
        /// the table's answer to it is given by the mod (SheetWritesHarmInRed, the one patch on the table).
        /// The warning is the owner's: the Estate keeps its own profile, but a mod that rewrites saves is to be
        /// played on a profile that can be lost.
        /// </summary>
        public const string TooltipKey = "dd2estate_main_menu_estate_tooltip";
        public static readonly string[] TooltipWords =
        {
            "Estate plays like the original Darkest Dungeon. It requires Darkest Dungeon to be installed.\n"
            + "<color=#B11900FF>WARNING: Use a fresh profile only, or keep backups of your saves.</color>"
        };

        private static void Postfix(MainMenuUiScreenBhv __instance)
        {
            try
            {
                Ensure(__instance);
                Follow(__instance);
                if (!EstateSession.Active && !EstateSession.Starting) EstateProfile.RecoverIfNeeded();
            }
            catch (System.Exception e) { Plugin.Log.LogError("main menu injection failed: " + e); }
        }

        private static void Ensure(MainMenuUiScreenBhv screen)
        {
            var light = screen.transform.Find("UI/MenuButtons/LightSideMenuButtons") as RectTransform;
            if (light == null || light.Find(ContainerName) != null) return;
            var kingdom = light.Find("KingdomHorizontalContainer") as RectTransform;
            var confession = light.Find("InitialExpeditionContainer") as RectTransform;
            if (kingdom == null || confession == null)
            {
                Plugin.Log.LogWarning("main menu layout changed: Kingdoms/Confessions containers not found");
                return;
            }

            var step = confession.anchoredPosition.y - kingdom.anchoredPosition.y;
            var container = (RectTransform)Object.Instantiate(kingdom.gameObject, light).transform;
            container.name = ContainerName;
            container.anchoredPosition = kingdom.anchoredPosition - new Vector2(0f, step);
            // The new row takes the old Kingdoms spot; the native rows move up one step so the Mods toggle stays clear.
            light.anchoredPosition += new Vector2(0f, step);

            var buttonGo = container.GetChild(0).gameObject;
            buttonGo.name = "EstateButton";
            foreach (var loc in buttonGo.GetComponentsInChildren<LocalizeTextBhv>(true)) Object.DestroyImmediate(loc);
            // the copy keeps Kingdoms' tooltip behaviour and is given the Estate's words to ask for
            var key = AccessTools.Field(typeof(LocalizedTextTooltipBhv), "m_locKey");
            foreach (var tip in buttonGo.GetComponentsInChildren<LocalizedTextTooltipBhv>(true))
            {
                if (key != null) key.SetValue(tip, TooltipKey);
                else Object.DestroyImmediate(tip);
            }

            var label = buttonGo.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "Estate";
                if (UiKit.Font == null) UiKit.Font = label.font;
            }

            var button = buttonGo.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(EstateMenu.Clicked);
            // The menu enables and disables its entries through this list (disclaimer, cinematics, options).
            var selectables = AccessTools.Field(typeof(MainMenuUiScreenBhv), "m_mainMenuSelectables")?.GetValue(screen) as List<Selectable>;
            var kingdomButton = kingdom.GetComponentInChildren<Button>(true);
            if (selectables != null && !selectables.Contains(button)) selectables.Add(button);
            if (kingdomButton != null) button.interactable = kingdomButton.interactable;
            try { SetIcon(screen, buttonGo, confession); }
            catch (System.Exception e) { Plugin.Log.LogWarning("main menu: the Estate entry keeps the Kingdoms crest: " + e.Message); }
            Plugin.Log.LogInfo("Estate entry added to the main menu");
        }

        /// <summary>
        /// The entry is on screen when Kingdoms' is. A press on Confessions puts Kingdoms' row away and shows
        /// "Continue Confession" and "New Confession" (MainMenuUiScreenBhv.OnConfessionButtonPressed switches the
        /// row's object off, OnConfessionBackPressed on again); the Estate's row is a row the game does not know
        /// of and stayed there as a third line under the two (the owner's picture, 2026-10-06).
        /// </summary>
        public static void Follow(MainMenuUiScreenBhv screen)
        {
            if (screen == null) return;
            var light = screen.transform.Find("UI/MenuButtons/LightSideMenuButtons");
            var estate = light != null ? light.Find(ContainerName) : null;
            var kingdom = light != null ? light.Find("KingdomHorizontalContainer") : null;
            if (estate == null || kingdom == null) return;
            if (estate.gameObject.activeSelf != kingdom.gameObject.activeSelf) estate.gameObject.SetActive(kingdom.gameObject.activeSelf);
        }

        private static readonly string[] BookWords = { "book", "journal", "codex", "tome", "glossary", "compendium", "grimoire", "memoir", "lore", "academic" };
        private static readonly Color Bronze = new Color(0.74f, 0.5f, 0.25f);
        private const float BookScale = 0.85f;      // the user: "make the book 15% smaller"

        // The entry was made from the Kingdoms entry and had its crest. It gets the book of the menu's bottom row
        // instead (the user's choice), painted as the game's own two pictures are: their colours from top to
        // foot are in their sprites, and the book gets a sprite of its own with the same (EstateMenuIcon).
        // FALLBACK when the game's pictures cannot be read: the book in one colour, the crest's mean.
        private static void SetIcon(MainMenuUiScreenBhv screen, GameObject entry, Transform confession)
        {
            var logo = entry.transform.Find("Logo") != null ? entry.transform.Find("Logo").GetComponent<Image>() : null;
            var group = AccessTools.Field(typeof(MainMenuUiScreenBhv), "m_bottomLeftButtonGroup")?.GetValue(screen) as CanvasGroup;
            if (logo == null || group == null)
            {
                Plugin.Log.LogWarning("main menu: no " + (logo == null ? "Logo on the entry" : "bottom row") + "; the Estate entry keeps the Kingdoms crest");
                return;
            }
            Image book = null;
            var seen = new List<string>();
            foreach (var image in group.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null) continue;
                var words = (image.sprite.name + " " + image.name + " " + (image.transform.parent != null ? image.transform.parent.name : "")).ToLowerInvariant();
                seen.Add(image.name + "=" + image.sprite.name);
                if (book != null) continue;
                foreach (var word in BookWords)
                    if (words.Contains(word)) { book = image; break; }
            }
            if (book == null)
            {
                // by place: the button before the film reel's
                var cinematic = AccessTools.Field(typeof(MainMenuUiScreenBhv), "m_cinematicButton")?.GetValue(screen) as GameObject;
                var at = cinematic != null ? cinematic.transform : null;
                while (at != null && at.parent != null && at.parent != group.transform && at.parent.GetComponentsInChildren<Button>(true).Length < 2) at = at.parent;
                if (at != null && at.parent != null)
                {
                    for (var i = at.GetSiblingIndex() - 1; i >= 0 && book == null; i--)
                    {
                        var sibling = at.parent.GetChild(i);
                        var itsButton = sibling.GetComponentInChildren<Button>(true);
                        if (itsButton == null) continue;
                        var images = itsButton.GetComponentsInChildren<Image>(true);
                        for (var k = images.Length - 1; k >= 0 && book == null; k--)
                            if (images[k].sprite != null) book = images[k];
                    }
                }
            }
            Plugin.Log.LogInfo("main menu: bottom row " + string.Join(", ", seen) + "; the Estate entry takes " + (book != null ? book.sprite.name : "nothing"));
            if (book == null) return;
            Sprite torch = null;
            if (confession != null)
                foreach (var image in confession.GetComponentsInChildren<Image>(true))
                    if (image.name == "Logo" && image.sprite != null) torch = image.sprite;
            var painted = EstateMenuIcon.Make(book.sprite, logo.sprite, torch);
            if (painted != null)
            {
                // drawn white, as the game draws its own two
                logo.sprite = painted;
                logo.color = new Color(1f, 1f, 1f, logo.color.a);
            }
            else
            {
                var crest = Mean(logo.sprite, Bronze);
                var page = Mean(book.sprite, Color.white);
                // the book's own picture is grey or white: the tint makes up for a grey one as far as a tint can
                var lift = 1f / Mathf.Clamp(Mathf.Max(page.r, Mathf.Max(page.g, page.b)), 0.45f, 1f);
                logo.sprite = book.sprite;
                logo.color = new Color(Mathf.Clamp01(crest.r * lift), Mathf.Clamp01(crest.g * lift), Mathf.Clamp01(crest.b * lift), logo.color.a);
            }
            logo.preserveAspect = true;
            // the book fills its picture more than the crest did: a little smaller, about its own middle
            var rect = logo.rectTransform;
            var middle = Vector2.Scale(new Vector2(0.5f, 0.5f) - rect.pivot, rect.rect.size);
            rect.localScale *= BookScale;
            rect.anchoredPosition += middle * (1f - BookScale);
        }

        /// <summary>The mean colour of a sprite's lit pixels (not its outlines, not what is clear).</summary>
        private static Color Mean(Sprite sprite, Color fallback)
        {
            if (sprite == null || sprite.texture == null) return fallback;
            RenderTexture target = null;
            Texture2D copy = null;
            var before = RenderTexture.active;
            try
            {
                var texture = sprite.texture;
                var area = sprite.textureRect;
                target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                copy = new Texture2D(Mathf.Max(1, (int)area.width), Mathf.Max(1, (int)area.height), TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(area.x, area.y, copy.width, copy.height), 0, 0, false);
                float r = 0f, g = 0f, b = 0f;
                var n = 0;
                foreach (var p in copy.GetPixels32())
                {
                    if (p.a < 128 || Mathf.Max(p.r, Mathf.Max(p.g, p.b)) < 90) continue;
                    r += p.r; g += p.g; b += p.b;
                    n++;
                }
                return n > 0 ? new Color(r / n / 255f, g / n / 255f, b / n / 255f) : fallback;
            }
            catch (System.Exception) { return fallback; }
            finally
            {
                RenderTexture.active = before;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (copy != null) Object.Destroy(copy);
            }
        }
    }

    /// <summary>Confessions is pressed: the Estate's entry leaves with Kingdoms'.</summary>
    [HarmonyPatch(typeof(MainMenuUiScreenBhv), nameof(MainMenuUiScreenBhv.OnConfessionButtonPressed))]
    internal static class EstateEntryLeavesWithKingdoms
    {
        private static void Postfix(MainMenuUiScreenBhv __instance) => MainMenuPatch.Follow(__instance);
    }

    /// <summary>Back from Confessions' two lines: the Estate's entry is back with Kingdoms'.</summary>
    [HarmonyPatch(typeof(MainMenuUiScreenBhv), nameof(MainMenuUiScreenBhv.OnConfessionBackPressed))]
    internal static class EstateEntryIsBackWithKingdoms
    {
        private static void Postfix(MainMenuUiScreenBhv __instance) => MainMenuPatch.Follow(__instance);
    }
}
