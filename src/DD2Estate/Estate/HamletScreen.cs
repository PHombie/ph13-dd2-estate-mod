using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    /// <summary>
    /// The hamlet: the estate's hub screen, drawn with the town art from the player's DD1 install and framed as
    /// DD1 frames its town screen. Top left the estate's name plate with the estate's name on its band (and nothing
    /// else, as in DD1: the week is the Activity Log's to tell, the way out is the menu behind the bar's candles).
    /// Right the roster column (<see cref="RosterPanel"/>), along the bottom the estate's
    /// bar with the currencies (<see cref="EstateSummary"/>) and, in its middle, DD1's way forward: Embark.
    ///
    /// Places come from town.layout.darkest (town_estate_title_layout) and shared/progression/
    /// progression.layout.darkest, texts from the English section of DD1's string table; the stock values stand
    /// in when a file cannot be read. tools/preview_hamlet.py draws the same layout offline: change one, change
    /// the other. Canvas "DD2Estate.Hamlet", sort order 5: building windows, the estate map and the narration
    /// are children of it, drawn in the order they are added.
    /// </summary>
    internal class HamletScreen : MonoBehaviour
    {
        private const string TownLayout = "campaign/town/town.layout.darkest";
        private const string ProgressionLayout = "shared/progression/progression.layout.darkest";
        private const string PlateArt = "campaign/town/estate_title/estate_nameplate.png";
        private const string ProgressionDir = "shared/progression/";
        private const string StringTable = "localization/miscellaneous.string_table.xml";

        // DD1's screen and art sizes.
        private const float ScreenWidth = 1920f, ScreenHeight = 1080f;
        private static readonly Vector2 PlateSize = new Vector2(893f, 281f);     // estate_nameplate.png
        private static readonly Vector2 ForwardSize = new Vector2(312f, 52f);    // progression_forward.png
        private static readonly Vector2 ForwardGlowSize = new Vector2(311f, 24f);

        // The mod's own number (plate pixels): the name ends here on the band, where the brush stroke frays.
        private const float NameRight = 772f;

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Regex Entry = new Regex("<entry id=\"([^\"]+)\"><!\\[CDATA\\[(.*?)\\]\\]></entry>");
        private static readonly string[] Wanted = { "\"estate_title_", "\"town_progression_forward_embark\"" };
        private static Dictionary<string, string> _strings;

        private static HamletScreen _instance;

        public static bool IsOpen => _instance != null && _instance.gameObject.activeSelf;

        public static void Open()
        {
            if (_instance == null) _instance = Build();
            _instance.gameObject.SetActive(true);
            Plugin.Log.LogInfo("Hamlet opened");
            if (QuestBoard.PendingNarration != null)
            {
                NarrationBox.Show(_instance.transform, QuestBoard.PendingNarration);
                QuestBoard.PendingNarration = null;
            }
        }

        public static void Close()
        {
            if (_instance != null) _instance.gameObject.SetActive(false);
        }

        private static HamletScreen Build()
        {
            var canvas = UiKit.Canvas("DD2Estate.Hamlet", 5);
            var screen = canvas.gameObject.AddComponent<HamletScreen>();
            var root = canvas.transform;

            var backdrop = UiKit.Image("Backdrop", root, null, UiKit.Ink, true);
            ((RectTransform)backdrop.transform).Stretch();

            if (!Dd1Install.Found)
            {
                // (Seen only on a way into the Estate that goes past the main menu's entry, which asks for the folder
                // before it lets anyone in. The lines are kept short: the roster's panel stands over the message's
                // right end, and the line that named the config file ran under it.)
                Message(root, "Dd1Missing",
                    "Darkest Dungeon (1) was not found.\nThe Estate reads its hamlet and dungeon art from your own DD1 install.\nName its folder in the options (the Estate tab) or at the main menu\n(the Estate entry asks for it), then restart the game.");
            }
            else if (!BuildTown((RectTransform)root))
            {
                Message(root, "TownFailed",
                    "The hamlet could not be drawn from your Darkest Dungeon (1) install.\nBepInEx/LogOutput.log has the details.");
            }

            // Back to front: the plate, the roster column, the bar over the column's foot, Embark on the bar.
            screen.BuildTitle(root);
            RosterPanel.Build(root);
            EstateSummary.Build(root);
            BuildForward(root);
            return screen;
        }

        // ---- the estate's name plate ---------------------------------------------------------------------

        private void BuildTitle(Transform root)
        {
            var town = Load(TownLayout);
            var platePos = Offset(town, "town_estate_title_layout", "pos", 0f, 0f);
            var nameAt = Offset(town, "town_estate_title_layout", "text_offset", 286f, 70f);

            var plate = UiKit.Rect("Title", root);
            plate.PlaceTopLeft(platePos, TopLeft, PlateSize);
            UiKit.Art("Plate", plate, PlateArt, Vector2.zero);

            // "The Darkest Estate": DD1's name of an estate nobody has named.
            var title = (Dd1String("estate_title_format") ?? "The %s Estate").Replace("%s", Dd1String("estate_title_default_data") ?? "Darkest");
            var name = UiKit.Text("Name", plate, title, "town_estate_title", null, TextAlignmentOptions.TopLeft);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)name.transform).PlaceTopLeft(nameAt, TopLeft, new Vector2(NameRight - nameAt.x, 64f));

            // DD1's band carries the estate's name and nothing else (a real frame of its hamlet): the week is the
            // Activity Log's to tell, and the way out is the menu behind the bar's candles.
            if (!Dd1Install.Found)
            {
                // No art, no bar buttons: a plain button keeps the way out.
                var plain = UiKit.Button("BackButton", root, "Return to Menu", 30, EstateSession.ExitToMenu);
                ((RectTransform)plain.transform).Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(320f, 64f));
            }
        }

        // ---- the way forward -----------------------------------------------------------------------------

        private static void BuildForward(Transform root)
        {
            var progression = Load(ProgressionLayout);
            var at = Offset(progression, "progression_layout", "forward_pos", 801f, 984f);
            var textAt = Offset(progression, "progression_layout", "forward_text_offset", 160f, -2f);
            var glowAt = Offset(progression, "progression_layout", "forward_selected_overlay_offset", 0f, -13f);
            var label = Dd1String("town_progression_forward_embark") ?? "Embark";

            var art = Dd1Install.Found ? Dd1Install.Sprite(ProgressionDir + "progression_forward.png") : null;
            if (art == null)
            {
                var plain = UiKit.Button("EmbarkButton", root, label, 34, () => QuestPanel.Open(root));
                ((RectTransform)plain.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(320f, 64f));
                return;
            }

            // DD1's forward button sits in the middle of the bar; it keeps the screen's middle on a wider screen.
            // Its click has one sound in DD1, ui/town/embark_button, which is played as the estate map comes up
            // (Dd2/EstateAudio): the button itself adds none.
            var picture = UiKit.Image("EmbarkButton", root, art, Color.white, true);
            var embark = picture.gameObject.AddComponent<Button>();
            embark.targetGraphic = picture;
            var colours = embark.colors;
            colours.highlightedColor = Color.white;
            colours.pressedColor = new Color(0.8f, 0.6f, 0.4f);
            colours.disabledColor = Color.white;     // a dead button has a picture of its own (Update)
            embark.colors = colours;
            embark.onClick.AddListener(() => QuestPanel.Open(root));
            var rect = (RectTransform)embark.transform;
            rect.Place(new Vector2(0.5f, 0f), TopLeft, new Vector2(at.x - ScreenWidth * 0.5f, ScreenHeight - at.y), ForwardSize);

            // Under the pointer: DD1's overlay, its sound, and the picture brighter by DD1's button_highlight
            // (colours/base.colours.darkest: 1.5 1.5 1.3), as the town's other picture buttons are.
            picture.gameObject.AddComponent<Dd1Highlight>().Button = embark;
            var glow = UiKit.Art("Glow", rect, ProgressionDir + "progression_forward_selected_overlay.png", glowAt, ForwardGlowSize);
            glow.gameObject.SetActive(false);
            UiKit.Hover(embark.gameObject, inside =>
            {
                var alive = embark != null && embark.interactable;
                glow.gameObject.SetActive(inside && alive);
                if (inside && alive) EstateAudio.Ui("ui/town/button_mouse_over_embark");
            });

            // forward_text_offset is the middle of the text's top edge.
            var text = UiKit.Text("Label", rect, label, "town_progression_forward", null, TextAlignmentOptions.Top);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)text.transform).PlaceTopLeft(textAt, new Vector2(0.5f, 1f), new Vector2(ForwardSize.x, 64f));

            _embark = embark;
            _embarkWord = text.gameObject;
            _embarkGlow = glow.gameObject;
            _embarkArt = art;
            // Measured on DD1's own screen of a building (the Tavern): the forward button's picture without its
            // colour at 0.4 of its light, and no word on it. No colour id of DD1's table is named for it; its
            // other "unselectable" pictures have the same two numbers (.darkness 0.4 .saturation 0.0).
            _embarkDead = Dd1Ui.Toned(ProgressionDir + "progression_forward.png", "town_progression_forward_unselectable", 0.4f, 0f) ?? art;
            _embarkIsDead = false;
        }

        private static Button _embark;
        private static GameObject _embarkWord, _embarkGlow;
        private static Sprite _embarkArt, _embarkDead;
        private static bool _embarkIsDead;

        // DD1 (its own screens): while a building's screen, the Activity Log or the town crier's notice is up there
        // is no way forward: the button's picture stands grey and wordless on the bar. With the Trinket
        // Inventory, the Glossary or the heirloom exchange open it stays as it is.
        private void Update()
        {
            if (_embark == null) return;
            var dead = RosterWindow.IsOpen || ActivityLogPanel.IsOpen || TownEventPanel.IsOpen;
            if (dead == _embarkIsDead) return;
            _embarkIsDead = dead;
            _embark.interactable = !dead;
            if (_embark.targetGraphic is Image picture) picture.sprite = dead ? _embarkDead : _embarkArt;
            _embarkWord.SetActive(!dead);
            if (dead) _embarkGlow.SetActive(false);
        }

        // ---- DD1's files ---------------------------------------------------------------------------------

        private static DarkestFile Load(string file) => Dd1Install.Found ? DarkestFile.Load(file) : null;

        /// <summary>An entry of a DD1 layout block (the stock value if the file cannot be read).</summary>
        internal static Vector2 Offset(DarkestFile layout, string block, string key, float x, float y)
        {
            var entry = layout?.Find(block);
            return entry != null && entry.Values(key).Count >= 2 ? entry.Vector2(key) : new Vector2(x, y);
        }

        private static string Dd1String(string key)
        {
            if (_strings == null) _strings = ReadStrings();
            return _strings.TryGetValue(key, out var text) ? text : null;
        }

        // The file holds every language (6 MB); English comes first, so reading stops at the end of its section.
        private static Dictionary<string, string> ReadStrings()
        {
            var strings = new Dictionary<string, string>();
            var path = Dd1Install.PathOf(StringTable);
            if (path == null || !File.Exists(path)) return strings;
            try
            {
                var english = false;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.IndexOf("<language ", StringComparison.Ordinal) >= 0)
                    {
                        if (english) break;
                        english = line.IndexOf("id=\"english\"", StringComparison.Ordinal) >= 0;
                        continue;
                    }
                    if (!english) continue;
                    if (line.IndexOf("</language>", StringComparison.Ordinal) >= 0) break;
                    var wanted = false;
                    foreach (var prefix in Wanted) wanted |= line.IndexOf(prefix, StringComparison.Ordinal) >= 0;
                    if (!wanted) continue;
                    var match = Entry.Match(line);
                    if (match.Success && !strings.ContainsKey(match.Groups[1].Value)) strings[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("DD1 string table could not be read: " + e.Message); }
            return strings;
        }

        private static bool BuildTown(RectTransform root)
        {
            if (HamletScene.Build(root) == null) return false;
            HamletScene.BuildingClicked -= OnBuildingClicked;
            HamletScene.BuildingClicked += OnBuildingClicked;
            return true;
        }

        // Placeholder until the building screens exist.
        private static void OnBuildingClicked(string id)
        {
            Plugin.Log.LogInfo("Hamlet: clicked " + id);
            Buildings.Open(id);
        }

        private static void Message(Transform root, string name, string text)
        {
            var label = UiKit.Text(name, root, text, 34, UiKit.Parchment);
            ((RectTransform)label.transform).Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 300));
        }
    }
}
