using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using DD2Estate.Dungeon;
using DD2Estate.Estate;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// The loading screen between the hamlet and a dungeon, and on the way into the Estate: DD1's picture of the
    /// place the party is going to, the place's name and one of DD1's tips for it. It is drawn in one of two
    /// looks (config `[Look] LoadingScreen`):
    ///
    /// - dd2 (the stock; the owner, 2026-10-06: "make them as in DD2 ... DD2's layout, DD1's art"): DD2's own
    ///   loading screen with DD1's three things in it. The game's fader closes with its ink, its sign turns in
    ///   its corner, the picture, the name and the tip come out of the black, and the screen leaves by itself
    ///   when what it covers is ready: no key is asked for. How it is put together: <see cref="Dd2LoadingLook"/>.
    /// - dd1: DD1's loading screen, drawn with the art, the words and the numbers of the player's DD1 install: a
    ///   picture over the whole screen, the place's name on a plate at the top, a tip in a box under the
    ///   picture, a burning torch in the box's ring and, on the way into a dungeon, the line that asks for a key.
    ///
    /// Which picture, which name and which tip is DD1's rule in both looks.
    ///
    /// What DD1 does (its exe is _windows/win32/Darkest.exe; the addresses are of this install's build; the
    /// layout was laid over two frames of the real game, _lab/dd1_ref/raid/loading_screen_*.png, and stands on
    /// the same pixels: tools/preview_loading_screen.py):
    ///
    /// - Into a dungeon [0xb72520]: the picture is the quest's own for a plot quest (loading_screen.&lt;quest&gt;.png),
    ///   else one of the region's (loading_screen.&lt;dungeon&gt;_&lt;n&gt;.png, each as likely as another [0xb72030,
    ///   0xb73a60]; the base game has one a region); the name is the DUNGEON's (dungeon_name_&lt;dungeon&gt;:
    ///   "Ruins"), not the quest's; the tip is str_&lt;quest&gt;_tip for a plot quest, else str_&lt;dungeon&gt;_tip, an id
    ///   the string table holds several times over: one of them is shown.
    /// - Home [0xb72330]: loading_screen.town_visit.png, "str_town_title" (Hamlet), str_town_tip.
    /// - Out of a save [0x7b8876]: the screen that was up last, which the save keeps
    ///   (persist.loading_screen.json: picture, title, tip): the dungeon's for a save on an expedition, the
    ///   town's otherwise.
    /// - It is drawn alone and whole, with no fade of its own: it is there from one frame to the next and gone
    ///   the same way [0x7bd610: the screen's "fade" is set to 1 and nothing else is drawn while it is up].
    /// - How it is left [0xb72bf0, 0xb72df0]: the town's leaves by itself as soon as the town is loaded. A
    ///   dungeon's waits for the player: once the dungeon is loaded (and the Ancestor has done speaking, on a
    ///   boss quest) "Press [SPACE] or [CLICK] to Continue" (str_loading_screen_continue) comes up at the foot
    ///   and any key, any button of a controller or a click goes on. The line swings between its colour and
    ///   loading_screen_continue_pulse_to (the same gold with no alpha), pulse_time (2 s) each way, eased in and
    ///   out by a cube, starting from nothing. (min_loading_time_to_continue, 10 s, is the Butcher's Circus'.)
    ///
    /// What the mod does with it, in the dd1 look: the screen is a canvas of its own over everything the mod
    /// draws and under the game's fader. <see cref="Show"/> puts it up, runs the caller's work behind it (the
    /// expedition is made, the hamlet is opened) and it takes itself down when what it covers stands: the
    /// dungeon after the player's key, with the first hallway coming up out of black as it does without the
    /// screen; the hamlet at once, coming up out of black over DD1's global_fade_transition_time. That time
    /// (shared/app.darkest, 0.5 s, DD1's fade between the town and black) is also how long black takes to close
    /// over the screen the player leaves before the picture is there.
    ///
    /// In the dd2 look the course is the same with the game's fader in the black sheet's place: its wipe closes
    /// over the screen the player leaves (its sign opening in the corner), the picture and the words come out of
    /// the black, the work is done behind them, they are seen for <see cref="LeastSeen"/> at the least, go back
    /// into the black, and the game's wipe lifts off the hamlet, or off the first hallway as its light comes up.
    /// No key is asked for. On the way into the Estate the game's fader is black already (the main menu went
    /// under it) and the Estate's mode is entered with the fade left black (<see cref="HubEnterTransition"/>),
    /// so that it is this screen that lifts it. This canvas then only keeps the clicks from what is under it.
    ///
    /// In both looks: while it is up nothing under it takes a click (the canvas), a key (CorridorView,
    /// DungeonHud ask <see cref="IsUp"/>) or speaks (Narration). It cannot stay for ever: see
    /// <see cref="Patience"/>, <see cref="KeyPatience"/> and <see cref="FaderPatience"/>; and whatever ends it,
    /// black of the game's fader that was closed for it is lifted (Release).
    /// </summary>
    internal class LoadingScreen : MonoBehaviour
    {
        public enum Kind { Raid, Town }

        /// <summary>How the screen is drawn and how it is left: as DD2 lays out a loading screen, or as DD1 does.</summary>
        public enum Look { Dd2, Dd1 }

        // Fading: the dd2 look's picture goes back into the black before the game's fader lifts
        // Handed: the dd1 look's picture stays while the fight it has handed the party to is on its way (the game's
        // own black closes over it), for an expedition that is nothing but its fight
        private enum Phase { Off, Closing, Loading, Asking, Leaving, Fading, Lifting, Handed }

        /// <summary>`[Look] LoadingScreen` of the config (bound by <see cref="LoadingScreenDev"/>): "dd2" or "dd1".</summary>
        internal static BepInEx.Configuration.ConfigEntry<string> Setting;

        /// <summary>A look asked for from the dev bridge (`loading.show look=dd1`), in place of the config's until the game is closed.</summary>
        public static Look? Forced;

        /// <summary>The look the next screen is put up in.</summary>
        public static Look Chosen => Forced ?? (Setting != null && string.Equals((Setting.Value ?? "").Trim(), "dd1", StringComparison.OrdinalIgnoreCase) ? Look.Dd1 : Look.Dd2);

        private const string Dir = "loading_screen/";
        private const string LayoutFile = Dir + "loading_screen.layout.darkest";
        private const string AppFile = "shared/app.darkest";
        private const string ColourFile = "colours/base.colours.darkest";
        private const string StringFile = "localization/miscellaneous.string_table.xml";
        private const string TorchSprite = "fx/torch_load/torch_load.sprite";
        private const string TorchAnimation = "loading";
        private const string TitleStyle = "raid_loading_screen_title", TipStyle = "raid_loading_screen_tip", AskStyle = "loading_screen_continue";
        private const string PulseColour = "loading_screen_continue_pulse_to";

        // DD1's art sizes (pixels).
        private static readonly Vector2 ScreenSize = new Vector2(1920f, 1080f);
        private static readonly Vector2 TipArt = new Vector2(638f, 227f);         // loading_screen.tipoverlay.png
        private static readonly Vector2 TitleArt = new Vector2(418f, 101f);       // loading_screen.titleoverlay.png
        // FALLBACK line heights of the two fonts (fonts/dwarvenaxe-l.fnt, ubuntu.fnt), for when they cannot be had.
        private const float TitleLine = 63f, TextLine = 25f;

        // ---- the mod's own numbers (DD1 has none for these) ------------------------------------------------

        /// <summary>The canvas' place among the canvases: over the mod's own screens and under the game's fader (32766), as EstateCurtain's.</summary>
        public static int Order = 31900;
        /// <summary>GUESS: the longest the screen waits for what it covers to stand. After that it is taken down without asking for a key, with a line in the log.</summary>
        public static float Patience = 45f;
        /// <summary>GUESS: the longest the screen waits for the player's key (DD1 waits for ever). After that it goes by itself, with a line in the log.</summary>
        public static float KeyPatience = 300f;
        /// <summary>
        /// GUESS (the mod's own; DD1's loading simply takes longer): the least time the picture is really seen (dd1:
        /// the game's own black wipe off it; dd2: whole out of the black) before it leaves by itself. Without it
        /// the hamlet's picture is a flash of a quarter second between the game's black and the hamlet (filmed),
        /// which is worse than no picture.
        /// </summary>
        public static float LeastSeen = 1.6f;
        /// <summary>The dd1 look only. False: a dungeon's screen leaves by itself like the town's (test scripts, which press nothing).</summary>
        public static bool WaitForKey = true;
        /// <summary>GUESS, the dd2 look: the longest the screen waits for the game's fader to have closed, or to have lifted, before it goes on without it.</summary>
        public static float FaderPatience = 5f;
        /// <summary>
        /// GUESS (the mod's judgement, not DD1's): a tip that speaks of one of these is not shown, for the mod's
        /// dungeons hold DD2's creatures. Where that would leave a region without any tip, DD1's own are shown.
        /// </summary>
        public static readonly List<string> NotInTheMod = new List<string> { "undead", "skeleton", "fishfolk", "thrall", "tentacled lurker", "fungal" };
        private const int ShownFrames = 2;          // frames the picture is on screen before the work behind it starts
        private const int SettleFrames = 3;         // frames in a row what is covered has to stand
        private const float LongFrame = 0.25f;      // a frame as long as this is still loading something
        private const float LongestStep = 1f / 30f; // a long frame is not a long step of a fade (as EstateCurtain's)

        /// <summary>DD1's numbers, read once; the stock values are DD1's own.</summary>
        private class Numbers
        {
            public Vector2 Origin = new Vector2(960f, 120f);        // loading_screen_dungeon_tip_frame.posn
            public Vector2 Title = new Vector2(0f, 64f);            // loading_screen_dungeon_title.offset
            public Vector2 Torch = new Vector2(954f, 930f);         // loading_screen_dungeon_title.load_anim_position (a screen position)
            public Vector2 Tip = new Vector2(0f, 635f);             // loading_screen_dungeon_tip.offset
            public Vector2 TipSize = new Vector2(600f, 150f);       // .size
            public Vector2 TipGround = new Vector2(0f, 615f);       // .background_offset
            public Vector2 Ask = new Vector2(960f, 1025f);          // loading_screen_continue.posn (a screen position)
            public float Pulse = 2f;                                // .pulse_time
            public float Fade = 0.5f;                               // shared/app.darkest m_GlobalTunables.global_fade_transition_time
            public float PulseAlpha;                                // the fourth number of loading_screen_continue_pulse_to (0)
            public float TitleLine = LoadingScreen.TitleLine, TextLine = LoadingScreen.TextLine;

            // Where each part stands on DD1's 1920x1080 screen (top left corner, y down).
            public Vector2 TipGroundAt => new Vector2(Origin.x + TipGround.x - Mathf.Floor(TipArt.x * 0.5f), Origin.y + TipGround.y);
            // DD1 draws the pixel below a half: the plate is 101 high and its top is at 133 in the real frames
            public Vector2 TitleGroundAt => new Vector2(Origin.x + Title.x - Mathf.Floor(TitleArt.x * 0.5f), Mathf.Floor(Origin.y + Title.y - TitleArt.y * 0.5f));
            /// <summary>The middle of the top of the title's line cell: the cell lies with its middle on the plate's.</summary>
            public Vector2 TitleAt => new Vector2(Origin.x + Title.x, Origin.y + Title.y - Mathf.Floor(TitleLine * 0.5f));
            /// <summary>The middle of the top of the tip's first line.</summary>
            public Vector2 TipAt => Origin + Tip;
        }

        private static LoadingScreen _instance;
        private static Numbers _numbers;
        private static Dictionary<string, List<string>> _tips;
        private static string _note = "";

        private Canvas _canvas;
        private RectTransform _content, _screen;
        private Image _sheet;
        private TextMeshProUGUI _title, _tip, _ask;
        private SpineView _torch;
        private Numbers _n;
        private Phase _phase = Phase.Off;
        private Kind _kind;
        private string _dungeon, _quest, _picture, _titleText, _tipText;
        private Action _work;
        private bool _hold, _seenHub, _pressed;
        private int _frames, _settled;
        private float _since, _askTime, _sheetAlpha, _seen;
        private Color _askColour, _askPulse;
        private Look _look = Look.Dd1;
        private Dd2LoadingLook _dd2;
        // the dd2 look: the game's fader is black for this screen's sake and is this screen's to lift
        private bool _mine;

        /// <summary>The screen is up, from the first frame of the black that closes before it to the last of the black that lifts after it.</summary>
        public static bool IsUp => _instance != null && _instance._phase != Phase.Off;

        // ---- putting it up and taking it down ------------------------------------------------------------------

        /// <summary>
        /// Puts the screen up. <paramref name="dungeon"/>: DD1's dungeon id ("crypts") of a <see cref="Kind.Raid"/>.
        /// <paramref name="quest"/>: a DD1 plot quest whose own picture and tip are shown in place of the
        /// region's ("plot_tutorial_crypts"; see <see cref="Dd1Quest"/>), or null.
        /// <paramref name="work"/> is run once behind the picture: what makes the place the screen stands for.
        /// <paramref name="covered"/>: the screen under it cannot be seen (the game's fader is black): the
        /// picture is there at once, without the black closing first.
        /// <paramref name="hold"/>: put up to be looked at (the dev bridge): nothing is waited for, a dungeon's
        /// asks for its key at once, the town's stays until <see cref="Hide"/> (the dd2 look: either stays
        /// until <see cref="Hide"/> or <see cref="Continue"/>).
        /// False, with nothing shown and nothing run, when the screen cannot be drawn (no DD1 install, no such
        /// picture): the caller goes on as it did before there was a loading screen.
        /// </summary>
        public static bool Show(Kind kind, string dungeon = null, string quest = null, Action work = null, bool covered = false, bool hold = false, int tip = -1)
        {
            try
            {
                if (!Dd1Install.Found) return false;
                // a screen that has yet to do its work is not put aside for another (its expedition would never begin)
                if (_instance != null && _instance._phase != Phase.Off && _instance._work != null)
                {
                    Plugin.Log.LogWarning("Loading screen: asked for again (" + kind + ") while it still has its work to do: not shown anew");
                    return false;
                }
                var picture = PictureOf(kind, dungeon, ref quest);
                var sprite = picture != null ? RaidUi.Sprite(picture) : null;
                if (sprite == null)
                {
                    Plugin.Log.LogWarning("Loading screen: DD1 has no picture for " + kind + " " + (dungeon ?? "") + ": not shown");
                    return false;
                }
                if (_instance == null)
                {
                    var canvas = UiKit.Canvas("DD2Estate.Loading", Order);
                    _instance = canvas.gameObject.AddComponent<LoadingScreen>();
                    _instance._canvas = canvas;
                }
                _instance.Open(kind, dungeon, quest, picture, sprite, work, covered || hold, hold, tip);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Loading screen: could not be built: " + e);
                if (_instance != null) _instance.Off("it could not be built");
                return false;
            }
        }

        /// <summary>
        /// DD1 on the way into a save: the screen that was up last. For an estate whose save is on an expedition
        /// that is the dungeon's (and in the dd1 look it waits for the key), otherwise the town's. The game's
        /// fader is black over it when this is called (EstateSession.Enter).
        /// </summary>
        public static bool ShowForSave()
        {
            var run = DungeonRun.Current;
            return run != null
                ? Show(Kind.Raid, run.Exploration.Map.DungeonId, Dd1Quest(run.Title), covered: true)
                : Show(Kind.Town, covered: true);
        }

        /// <summary>
        /// The DD1 plot quest whose own loading picture and tip stand for one of the mod's quests (by its id or
        /// name); null: the region's. DD1's plot pictures are the Ancestor's pages on DD1's own bosses, and the
        /// mod's bosses are DD2's with a story of their own: only a story quest without a boss takes DD1's.
        /// </summary>
        public static string Dd1Quest(string questIdOrName)
        {
            // DD1's quests of a town event and of the Shrieker are DD1's own, picture and tip with them
            var own = PlotQuests.Dd1IdOf(questIdOrName);
            if (own != null) return own;
            var plot = Memoirs.PlotQuest(questIdOrName);
            if (plot == null || !string.IsNullOrEmpty((string)plot["boss"])) return null;
            return (string)plot["dd1_quest_id"];
        }

        /// <summary>Takes the screen down at once, wherever it is in its course. Its work, if not yet run, is not run.</summary>
        public static void Hide()
        {
            if (_instance != null && _instance._phase != Phase.Off) _instance.Off("hidden", atOnce: true);
        }

        /// <summary>
        /// The player's key, from outside (the dev bridge): goes on if the screen is asking for it. A screen of
        /// the dd2 look asks for none: one that was put up to be looked at leaves by its own course at this.
        /// </summary>
        public static bool Continue()
        {
            var s = _instance;
            if (s == null) return false;
            if (s._phase != Phase.Asking && !(s._look == Look.Dd2 && s._hold && s._phase == Phase.Loading)) return false;
            s._pressed = true;
            return true;
        }

        /// <summary>
        /// The transition the Estate's own mode is to be entered with while this screen stands over the way in
        /// (EstateSession.Enter): in the dd2 look the game leaves its fade black when the mode is in (DD2's
        /// MANUAL, as its own modes that load behind the black have it) and the screen lifts it when the hamlet
        /// stands. Null: as the mode has it (the game lifts its fade itself).
        /// </summary>
        public static Assets.Code.UI.Transitions.SceneTransition HubEnterTransition()
        {
            var s = _instance;
            if (s == null || s._phase == Phase.Off || s._look != Look.Dd2) return null;
            s._mine = true;
            return new Assets.Code.UI.Transitions.SceneTransition(Assets.Code.UI.Transitions.TransitionType.FADE, Assets.Code.UI.Transitions.TransitionType.MANUAL);
        }

        private void Open(Kind kind, string dungeon, string quest, string picture, Sprite sprite, Action work, bool covered, bool hold, int tip)
        {
            _n = Read();
            _kind = kind;
            _dungeon = dungeon;
            _quest = quest;
            _picture = picture;
            _work = work;
            _hold = hold;
            _seenHub = _pressed = false;
            _frames = _settled = 0;
            _askTime = 0f;
            _since = Time.unscaledTime;
            _canvas.sortingOrder = Order;
            var fader = Dd2Fader.Fader;
            _look = fader != null ? Chosen : Look.Dd1;
            Build(sprite, tip);
            if (_look == Look.Dd2 && _dd2 == null) _look = Look.Dd1;      // the game's fader could not be built into
            if (_look == Look.Dd1)
            {
                Release(true);
                BuildDd1(sprite);
            }
            _canvas.enabled = true;
            gameObject.SetActive(true);
            if (_look == Look.Dd2)
            {
                // The game's own black closes over what the player leaves, with its ink and its sign; the picture comes
                // out of it when it has closed. A fader that is black already (the way in from the main menu) stays so.
                if (!fader.IsBlack && !fader.FadingToBlack) _mine = true;
                Dd2Fader.Close(Dd2LoadingLook.CloseType(kind));
                _sheetAlpha = 0f;
                _phase = Phase.Closing;
            }
            else if (covered) Uncover();
            else
            {
                // black closes over what the player leaves; the picture is there when it has closed
                _content.gameObject.SetActive(false);
                _sheetAlpha = 0f;
                _phase = Phase.Closing;
            }
            Sheet();
            Note("up: " + kind + " " + picture + " (" + _look.ToString().ToLowerInvariant() + (hold ? ", held)" : ")"));
        }

        // Everything of the last showing goes; DD1's name and tip for the place are picked, and in the dd2 look the
        // screen is built into the game's fader. This canvas then only keeps the clicks from what is under it.
        private void Build(Sprite picture, int tip)
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var old = transform.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            _dd2?.Destroy();
            _dd2 = null;
            _content = _screen = null;
            _sheet = null;
            _title = _tip = _ask = null;
            _torch = null;

            _titleText = _kind == Kind.Town ? Dd1Strings.Get("str_town_title") ?? "Hamlet" : RaidText.Get("dungeon_name_" + _dungeon, _dungeon ?? "");
            _tipText = PickTip(TipKey(_kind, _dungeon, _quest), tip);
            if (_look != Look.Dd2) return;
            _dd2 = Dd2LoadingLook.Build(picture, _titleText, _tipText, darkEdges: _kind == Kind.Town);
            if (_dd2 == null) return;
            var veil = UiKit.Image("Veil", transform, null, new Color(0f, 0f, 0f, 0f), true);
            ((RectTransform)veil.transform).Stretch();
        }

        // DD1's screen, built from DD1's files.
        private void BuildDd1(Sprite picture)
        {
            _content = UiKit.Rect("Content", transform).Stretch();
            // Nothing under the screen takes a click; a display of another shape shows black beside DD1's.
            var backdrop = UiKit.Image("Backdrop", _content, null, Color.black, true);
            ((RectTransform)backdrop.transform).Stretch();
            _screen = RaidUi.Screen("Screen", _content);
            var back = UiKit.Image("Picture", _screen, picture);
            ((RectTransform)back.transform).PlaceTopLeft(Vector2.zero, RaidUi.TopLeft, ScreenSize);

            // DD1's order [0xb72bf0, 0xb73300]: the line at the foot, the tip's ground, the plate, the title, the
            // tip; the torch is drawn in a later layer (its sparks cross the ring of the tip's ground in the real frames).
            _askColour = Dd1Fonts.Colour(AskStyle, UiKit.Neutral);
            var gold = Dd1Fonts.Colour(PulseColour, UiKit.Notable);
            _askPulse = new Color(gold.r, gold.g, gold.b, _n.PulseAlpha);
            _ask = RaidUi.Label("Continue", _screen, AskStyle, _n.Ask, new Vector2(1600f, _n.TextLine + 6f), TextAlignmentOptions.Top, _askColour);
            _ask.richText = true;
            _ask.text = RaidText.Marks(Dd1Strings.Get("str_loading_screen_continue") ?? "Press [SPACE] or [CLICK] to Continue");
            _ask.gameObject.SetActive(false);

            RaidUi.Art("TipGround", _screen, Dir + "loading_screen.tipoverlay.png", _n.TipGroundAt, TipArt);
            // DD1 draws the plate only for a screen that has a title [0xb7342c]
            if (!string.IsNullOrEmpty(_titleText)) RaidUi.Art("TitleGround", _screen, Dir + "loading_screen.titleoverlay.png", _n.TitleGroundAt, TitleArt);
            _title = RaidUi.Label("Title", _screen, TitleStyle, _n.TitleAt, new Vector2(1600f, _n.TitleLine + 4f), TextAlignmentOptions.Top);
            _title.text = _titleText ?? "";

            _tip = RaidUi.Paragraph("Tip", _screen, TipStyle, _n.TipAt, _n.TipSize, TextAlignmentOptions.Top);
            _tip.richText = true;
            _tip.text = RaidText.Marks(_tipText ?? "");

            // The torch's sheet is drawn as DD1 draws it (SpineAtlas.Load, straight); its root stands on the position.
            _torch = SpineView.Create("Torch", _screen, TorchSprite, true);
            if (_torch != null)
            {
                _torch.Rect.PlaceTopLeft(_n.Torch, RaidUi.Middle, Vector2.zero);
                _torch.Play(TorchAnimation);
            }

            // The black that closes before the picture and lifts after it (the town's).
            _sheet = UiKit.Image("Sheet", transform, null, Color.black, true);
            ((RectTransform)_sheet.transform).Stretch();
        }

        // The picture is there: from one frame to the next, as DD1's. (The dd2 look: it begins to come out of the black.)
        private void Uncover()
        {
            if (_content != null) _content.gameObject.SetActive(true);
            _sheetAlpha = 0f;
            _phase = Phase.Loading;
            _frames = _settled = 0;
            _seen = 0f;
            _since = Time.unscaledTime;
        }

        private void Sheet()
        {
            if (_sheet == null) return;
            _sheet.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_sheetAlpha));
            // while it closes or lifts it also takes the clicks meant for what is under it; behind the picture it is away
            _sheet.gameObject.SetActive(_phase == Phase.Closing || _phase == Phase.Lifting);
        }

        private void Off(string why, bool atOnce = false)
        {
            _phase = Phase.Off;
            _work = null;
            if (_canvas != null) _canvas.enabled = false;
            gameObject.SetActive(false);
            _dd2?.Destroy();
            _dd2 = null;
            Release(atOnce);
            Note("down: " + why);
        }

        // The dd2 look: black of the game's fader that was closed (or kept closed) for this screen and has not been
        // lifted by its course is lifted, so that no end of the screen leaves the game black. While the game is
        // changing its mode the fader is the game's: the lift waits for the change to be over.
        private void Release(bool atOnce)
        {
            if (!_mine) return;
            _mine = false;
            var fader = Dd2Fader.Fader;
            if (fader == null || fader.IsClear || fader.FadingOutOfBlack) return;
            if (!Assets.Code.Utils.Singleton<Assets.Code.Game.GameModeMgr>.Instance.IsChangingState()) Dd2Fader.Lift(Assets.Code.UI.Transitions.TransitionType.FADE, atOnce);
            else if (Plugin.Host != null) Plugin.Host.StartCoroutine(LiftWhenSettled());
        }

        private static System.Collections.IEnumerator LiftWhenSettled()
        {
            var until = Time.unscaledTime + 30f;
            while (Assets.Code.Utils.Singleton<Assets.Code.Game.GameModeMgr>.Instance.IsChangingState() && Time.unscaledTime < until) yield return null;
            // another screen that is up by now has the black for its own
            if (!IsUp) Dd2Fader.Lift(Assets.Code.UI.Transitions.TransitionType.FADE);
        }

        private static void Note(string text)
        {
            _note = text;
            Plugin.Log.LogInfo("Loading screen " + text);
        }

        // ---- its course ----------------------------------------------------------------------------------------

        private void Update()
        {
            if (_phase == Phase.Off) return;
            var now = Time.unscaledTime;
            var step = Mathf.Min(Time.unscaledDeltaTime, LongestStep);
            if (!_hold)
            {
                // It belongs to an estate session: without one (the way in failed, the main menu) it goes. So it does
                // when the game leaves the hub under it (a fight out of a save), which no picture may cover.
                if (!EstateSession.Active && !EstateSession.Starting)
                {
                    Off("the session is over");
                    return;
                }
                if (EstateSession.HubMode) _seenHub = true;
                else if (_seenHub)
                {
                    Off("the game left the Estate's own mode under it");
                    return;
                }
            }

            if (_look == Look.Dd2)
            {
                CourseDd2(now, step);
                return;
            }

            switch (_phase)
            {
                case Phase.Closing:
                    _sheetAlpha += step / Mathf.Max(0.01f, _n.Fade);
                    if (_sheetAlpha >= 1f) Uncover();
                    Sheet();
                    break;

                case Phase.Loading:
                    _frames++;
                    if (_work != null)
                    {
                        // the picture has been on the screen: now what is long may be done behind it
                        if (_frames > ShownFrames) Work();
                        break;
                    }
                    var patience = _hold ? KeyPatience : Patience;
                    // seen: the game's own fader is off the screen
                    var fader = Assets.Code.Utils.SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.HasInstance() ? Assets.Code.Utils.SingletonMonoBehaviour<Assets.Code.UI.ScreenFaderBhv>.Instance : null;
                    if (fader == null || fader.IsClear) _seen += Time.unscaledDeltaTime;
                    var stands = Stands() && (_hold || _seen >= LeastSeen);
                    if (!stands && now - _since > patience)
                    {
                        Plugin.Log.LogWarning("Loading screen: " + (_hold ? "held for " : "what it covers did not stand after ") + patience.ToString("0", CultureInfo.InvariantCulture)
                                              + " s (" + _kind + ", " + Under() + "): taken down");
                        stands = true;
                    }
                    if (!stands) break;
                    if (_kind == Kind.Raid && (WaitForKey || _hold) && now - _since <= patience)
                    {
                        _phase = Phase.Asking;
                        _askTime = 0f;
                        _since = now;
                        _pressed = false;
                        _ask.gameObject.SetActive(true);
                        Pulse();
                    }
                    else _phase = Phase.Leaving;
                    break;

                case Phase.Asking:
                    // the first frame of the line is not a frame of the key: the click that brought the screen is long gone,
                    // but a key held down since then must not go straight on
                    if (_askTime > 0f && Pressed()) _pressed = true;
                    _askTime += Time.unscaledDeltaTime;
                    Pulse();
                    if (!_pressed && now - _since > KeyPatience)
                    {
                        Plugin.Log.LogWarning("Loading screen: no key in " + KeyPatience.ToString("0", CultureInfo.InvariantCulture) + " s: it goes on by itself");
                        _pressed = true;
                    }
                    // gone in the frame after the key: what is under the screen must not take the same key
                    if (_pressed) _phase = Phase.Leaving;
                    break;

                case Phase.Leaving:
                    Leave();
                    break;

                case Phase.Lifting:
                    _sheetAlpha -= step / Mathf.Max(0.01f, _n.Fade);
                    Sheet();
                    if (_sheetAlpha <= 0f) Off("the hamlet is up");
                    break;

                case Phase.Handed:
                    // (it goes when the game leaves the hub under it, above; this is for a fight that never came)
                    if (now - _since > 2f * FaderPatience) Off("the fight it handed the party to did not begin");
                    break;
            }
        }

        // An expedition that is nothing but its fight (the Darkest Dungeon's stand-ins, DungeonRunBoss.cs) goes
        // from under this screen straight into that fight: no room is lifted onto.
        private bool HandsOverToAFight()
        {
            var run = _hold ? null : DungeonRun.Current;
            return run != null && run.FightFromLoading();
        }

        // The dd2 look's course: the game's black closes, the picture comes out of it, what is long is done behind
        // it, the picture goes back into the black and the game's black lifts. No key is asked for.
        private void CourseDd2(float now, float step)
        {
            var fader = Dd2Fader.Fader;
            if (_phase != Phase.Lifting && (_dd2 == null || !_dd2.Whole))
            {
                // the game took its fader apart under the screen (it reinstalls its systems on some changes)
                Off("the game's fader is gone");
                return;
            }
            switch (_phase)
            {
                case Phase.Closing:
                    if (fader == null || fader.IsBlack || now - _since > FaderPatience) Uncover();
                    else if (!fader.FadingToBlack)
                    {
                        // something lifted it meanwhile: black again, and this screen's to lift
                        _mine = true;
                        Dd2Fader.Close(Dd2LoadingLook.CloseType(_kind), atOnce: true);
                    }
                    break;

                case Phase.Loading:
                    _frames++;
                    if (_dd2.Alpha < 1f)
                    {
                        _dd2.Alpha += step / Mathf.Max(0.01f, Dd2LoadingLook.FadeIn);
                        break;
                    }
                    if (_work != null)
                    {
                        // the picture is on the screen: now what is long may be done behind it
                        Work();
                        break;
                    }
                    var patience = _hold ? KeyPatience : Patience;
                    _seen += Time.unscaledDeltaTime;
                    var stands = Stands() && (_hold || _seen >= LeastSeen);
                    if (!stands && now - _since > patience)
                    {
                        Plugin.Log.LogWarning("Loading screen: " + (_hold ? "held for " : "what it covers did not stand after ") + patience.ToString("0", CultureInfo.InvariantCulture)
                                              + " s (" + _kind + ", " + Under() + "): taken down");
                        stands = true;
                    }
                    if (stands) _phase = Phase.Fading;
                    break;

                case Phase.Fading:
                    _dd2.Alpha -= step / Mathf.Max(0.01f, Dd2LoadingLook.FadeOut);
                    if (_dd2.Alpha <= 0f) LeaveDd2();
                    break;

                case Phase.Lifting:
                    if (fader == null || fader.IsClear || now - _since > FaderPatience) Off("the hamlet is up");
                    break;

                default:
                    _phase = Phase.Fading;      // asking for a key is the dd1 look's
                    break;
            }
        }

        // The picture is back in the black: the game's fader lifts as it does when DD2 has loaded.
        private void LeaveDd2()
        {
            _mine = false;
            var lift = Dd2LoadingLook.LiftType(_kind);
            if (_kind == Kind.Raid)
            {
                // the fight begins behind the black the picture went back into; the game lifts its black off the arena
                if (HandsOverToAFight())
                {
                    Off("into the fight");
                    return;
                }
                // the first hallway's light comes up as it does without the screen, under the ink as it draws back
                var view = CorridorView.Instance;
                var dungeon = !_hold || DungeonRun.Current != null;
                Dd2Fader.Lift(lift);
                Off("the dungeon is up");
                if (dungeon && view != null && view.isActiveAndEnabled) view.FadeInAgain();
                return;
            }
            // the hamlet stays out of reach until the ink has drawn back
            Dd2Fader.Lift(lift);
            _dd2.Destroy();
            _since = Time.unscaledTime;
            _phase = Phase.Lifting;
        }

        private void Work()
        {
            var work = _work;
            _work = null;
            try { work(); }
            catch (Exception e)
            {
                Plugin.Log.LogError("Loading screen: what it stood for could not be made: " + e);
                Off("its work failed");
                return;
            }
            _frames = _settled = 0;
            _since = Time.unscaledTime;
            // no expedition began (the hamlet is still there): there is nothing to ask a key for
            if (_kind == Kind.Raid && DungeonRun.Current == null)
            {
                Plugin.Log.LogWarning("Loading screen: no expedition began behind it");
                _kind = Kind.Town;
            }
        }

        // What the screen covers stands, and has stood for a few short frames (the frame a hamlet or a party is
        // built in is a long one).
        private bool Stands()
        {
            // put up to be looked at: a dungeon's dd1 screen asks for its key at once; a dd2 screen stays until it is told to go
            if (_hold) return _look == Look.Dd2 ? _pressed : _kind == Kind.Raid;
            bool up;
            if (_kind == Kind.Raid)
            {
                var run = DungeonRun.Current;
                up = EstateSession.InHub && EstateSession.View == EstateSession.Screen.Dungeon && run != null && !run.AwaitsResume;
            }
            else up = EstateSession.InHub && EstateSession.View == EstateSession.Screen.Hamlet && HamletScreen.IsOpen;
            _settled = up && Time.unscaledDeltaTime < LongFrame ? _settled + 1 : 0;
            return _settled >= SettleFrames;
        }

        private static string Under()
        {
            return "hub " + EstateSession.InHub + ", view " + EstateSession.View + ", hamlet " + HamletScreen.IsOpen + ", expedition " + (DungeonRun.Current != null);
        }

        private void Leave()
        {
            if (_kind == Kind.Raid)
            {
                // the picture stays until the game's own black has closed over it on the way into the fight
                if (HandsOverToAFight())
                {
                    if (_ask != null) _ask.gameObject.SetActive(false);
                    _since = Time.unscaledTime;
                    _phase = Phase.Handed;
                    Note("handed over: into the fight");
                    return;
                }
                // DD1: the picture is gone from one frame to the next and the dungeon comes up out of black
                var view = CorridorView.Instance;
                var dungeon = !_hold || DungeonRun.Current != null;
                Off("the dungeon is up");
                if (dungeon && view != null && view.isActiveAndEnabled) view.FadeInAgain();
                return;
            }
            // the hamlet comes up out of black
            _content.gameObject.SetActive(false);
            _sheetAlpha = 1f;
            _phase = Phase.Lifting;
            Sheet();
        }

        // DD1 [0xb72df0]: any key of the keyboard, any button of a controller, a mouse button. Escape is the game's
        // own (its menu opens under this screen and is seen when the screen has gone).
        private static bool Pressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame && !keyboard.escapeKey.wasPressedThisFrame) return true;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
            var pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame
                                   || pad.buttonNorth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        }

        /// <summary>
        /// DD1's swing of the line at the foot [0xb72df0]: how much of the pulse colour is in it after
        /// <paramref name="seconds"/>. 1 at the start (the pulse colour has no alpha: nothing is seen), down to 0
        /// (the line's own colour) over <paramref name="half"/> seconds and up again over as many, eased in and
        /// out by a cube.
        /// </summary>
        public static float PulseShare(float seconds, float half)
        {
            if (half <= 0f) return 0f;
            var whole = Mathf.Repeat(seconds, 2f * half);
            var x = Mathf.Repeat(whole, half) / half;
            var eased = x < 0.5f ? 0.5f * Mathf.Pow(2f * x, 3f) : 0.5f + 0.5f * (1f - Mathf.Pow(2f - 2f * x, 3f));
            return whole <= half ? 1f - eased : eased;
        }

        // GUESS: the words DD1 colours within the line ([SPACE], [CLICK]) keep their gold and go with the line's
        // alpha; whether DD1 pulses them too was not found.
        private void Pulse()
        {
            if (_ask != null) _ask.color = Color.Lerp(_askColour, _askPulse, PulseShare(_askTime, _n.Pulse));
        }

        // ---- DD1's files ---------------------------------------------------------------------------------------

        private static Numbers Read()
        {
            if (_numbers != null) return _numbers;
            var n = new Numbers();
            try
            {
                var layout = Dd1Ui.Layout(LayoutFile);
                n.Origin = Pair(layout, "loading_screen_dungeon_tip_frame", "posn", n.Origin);
                n.Title = Pair(layout, "loading_screen_dungeon_title", "offset", n.Title);
                n.Torch = Pair(layout, "loading_screen_dungeon_title", "load_anim_position", n.Torch);
                n.Tip = Pair(layout, "loading_screen_dungeon_tip", "offset", n.Tip);
                n.TipSize = Pair(layout, "loading_screen_dungeon_tip", "size", n.TipSize);
                n.TipGround = Pair(layout, "loading_screen_dungeon_tip", "background_offset", n.TipGround);
                n.Ask = Pair(layout, "loading_screen_continue", "posn", n.Ask);
                n.Pulse = Dd1Ui.Number(layout, "loading_screen_continue", "pulse_time", n.Pulse);
                n.Fade = Dd1Ui.Number(Dd1Ui.Layout(AppFile), "m_GlobalTunables", "global_fade_transition_time", n.Fade);
                var title = Dd1Fonts.Style(TitleStyle);
                if (title != null && title.LineHeight > 0) n.TitleLine = title.LineHeight;
                var text = Dd1Fonts.Style(AskStyle);
                if (text != null && text.LineHeight > 0) n.TextLine = text.LineHeight;
                // `colour: .id "loading_screen_continue_pulse_to" .rgba 200 180 110 0`: the table's reader keeps three numbers
                var colours = Dd1Install.ReadText(ColourFile);
                var alpha = colours != null ? Regex.Match(colours, "\"" + PulseColour + "\"\\s+\\.rgba\\s+\\d+\\s+\\d+\\s+\\d+\\s+(\\d+)") : null;
                if (alpha != null && alpha.Success) n.PulseAlpha = int.Parse(alpha.Groups[1].Value, CultureInfo.InvariantCulture) / 255f;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Loading screen: DD1's numbers could not be read, its stock values stand: " + e.Message); }
            // not kept before there is an install to read
            if (Dd1Install.Found) _numbers = n;
            return n;
        }

        // The layout file writes some of its pairs with a comma between the two (`.posn 960.0, 120.0`).
        private static Vector2 Pair(DarkestFile file, string block, string key, Vector2 stock)
        {
            var values = file?.Find(block)?.Values(key);
            if (values == null || values.Count < 2) return stock;
            return Number(values[0], out var x) && Number(values[1], out var y) ? new Vector2(x, y) : stock;
        }

        private static bool Number(string text, out float value)
        {
            return float.TryParse(text.TrimEnd(','), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // DD1 [0xb72520]: a plot quest's own picture; else one of the region's, each as likely as another.
        private static string PictureOf(Kind kind, string dungeon, ref string quest)
        {
            if (kind == Kind.Town) return Dir + "loading_screen.town_visit.png";
            if (!string.IsNullOrEmpty(quest) && Dd1Install.Exists(Dir + "loading_screen." + quest + ".png")) return Dir + "loading_screen." + quest + ".png";
            quest = null;
            if (string.IsNullOrEmpty(dungeon)) return null;
            var count = 0;
            while (count < 16 && Dd1Install.Exists(Dir + "loading_screen." + dungeon + "_" + count.ToString(CultureInfo.InvariantCulture) + ".png")) count++;
            return count > 0 ? Dir + "loading_screen." + dungeon + "_" + UnityEngine.Random.Range(0, count).ToString(CultureInfo.InvariantCulture) + ".png" : null;
        }

        private static string TipKey(Kind kind, string dungeon, string quest)
        {
            return kind == Kind.Town ? "town" : !string.IsNullOrEmpty(quest) ? quest : dungeon;
        }

        /// <summary>
        /// Every text DD1 has under str_&lt;key&gt;_tip, in the file's order (the table holds the id once for each;
        /// some texts twice). Dd1Strings keeps the first of an id only.
        /// </summary>
        public static IReadOnlyList<string> Tips(string key)
        {
            if (_tips == null)
            {
                var tips = new Dictionary<string, List<string>>();
                var path = Dd1Install.PathOf(StringFile);
                if (path == null || !File.Exists(path)) return new List<string>();
                try
                {
                    var entry = new Regex("<entry id=\"str_([^\"]+)_tip\"><!\\[CDATA\\[(.*?)\\]\\]>");
                    var english = false;
                    foreach (var line in File.ReadLines(path))
                    {
                        if (line.IndexOf("<language ", StringComparison.Ordinal) >= 0)
                        {
                            if (english) break;
                            english = line.IndexOf("id=\"english\"", StringComparison.Ordinal) >= 0;
                            continue;
                        }
                        if (!english || line.IndexOf("_tip\"", StringComparison.Ordinal) < 0) continue;
                        var match = entry.Match(line);
                        if (!match.Success) continue;
                        if (!tips.TryGetValue(match.Groups[1].Value, out var list)) tips[match.Groups[1].Value] = list = new List<string>();
                        list.Add(match.Groups[2].Value);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Loading screen: DD1's tips could not be read: " + e.Message); }
                _tips = tips;
            }
            return key != null && _tips.TryGetValue(key, out var found) ? found : new List<string>();
        }

        /// <summary>True for a tip the mod does not show (<see cref="NotInTheMod"/>).</summary>
        public static bool LeftOut(string tip)
        {
            foreach (var word in NotInTheMod)
                if (tip.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // One of the texts, each as likely as another (INFERRED: DD1's choice among the texts of one id was not
        // read; the repeats in the table shift the odds, which is kept). `index`: that one, of all of them.
        private static string PickTip(string key, int index)
        {
            var all = Tips(key);
            if (all.Count == 0) return null;
            if (index >= 0) return all[index % all.Count];
            var kept = new List<string>();
            foreach (var tip in all)
                if (!LeftOut(tip)) kept.Add(tip);
            if (kept.Count == 0) kept.AddRange(all);
            return kept[UnityEngine.Random.Range(0, kept.Count)];
        }

        // ---- dev bridge ----------------------------------------------------------------------------------------

        public static object Describe()
        {
            var s = _instance;
            var up = IsUp;
            var fader = Dd2Fader.Fader;
            return new
            {
                up,
                // the look the screen is up in, or the one the next will be put up in
                look = (up ? s._look : Chosen).ToString().ToLowerInvariant(),
                phase = s != null ? s._phase.ToString() : "Off",
                // the dd2 look: how much of the picture and the words is out of the black (0..1), the game's fader, and
                // whether its black is this screen's to lift
                shown = up && s._dd2 != null ? s._dd2.Alpha : up && s._look == Look.Dd1 && s._content != null && s._content.gameObject.activeSelf ? 1f : 0f,
                fader = fader == null ? "none" : fader.IsBlack ? "black" : fader.IsClear ? "clear" : fader.FadingToBlack ? "to black" : "to clear",
                faderMine = up && s._mine,
                kind = up ? s._kind.ToString() : null,
                held = up && s._hold,
                picture = up ? s._picture : null,
                title = up ? s._titleText : null,
                tip = up ? s._tipText : null,
                asking = up && s._phase == Phase.Asking,
                // how much of the line at the foot is seen (0..1), and for how long it has been asking
                askAlpha = up && s._ask != null && s._phase == Phase.Asking ? s._ask.color.a : 0f,
                askSeconds = up ? s._askTime : 0f,
                black = up ? s._sheetAlpha : 0f,
                seconds = up ? Time.unscaledTime - s._since : 0f,
                torch = up && s._torch != null,
                waitForKey = WaitForKey,
                order = Order,
                under = Under(),
                last = _note
            };
        }

        /// <summary>
        /// Where every part stands on DD1's 1920x1080 screen (x, y of the top left corner, y down) by DD1's
        /// numbers, and, while the screen is up, where the part really stands on the canvas.
        /// </summary>
        public static object Layout()
        {
            var n = Read();
            var s = IsUp ? _instance : null;
            if ((s != null ? s._look : Chosen) == Look.Dd2)
                return new
                {
                    look = "dd2",
                    screen = "1920x1080, x and y of the top left corner, y down; built into the game's fader (" + (Dd2Fader.Fader != null ? Dd2Fader.PathOf(Dd2Fader.Fader.transform) : "none yet") + ") under its sign",
                    parts = s != null && s._dd2 != null ? s._dd2.Parts() : "put a screen up to see its parts as built (loading.show)",
                    numbers = Dd2LoadingLook.Numbers(),
                    what = new[]
                    {
                        "Picture: DD1's loading_screen.<dungeon>_<n>.png / .<quest>.png / .town_visit.png over the whole screen",
                        "Title: DD1's name of the place in DD2's title font (a copy of the subtitle's words, larger)",
                        "Tip: DD1's tip in a copy of DD2's subtitle line (its band, font, size, colour and place)",
                        "Sign: the game's own (the fader's throbber), in its own corner",
                        "Fades: the game's own transitions (raidClose/raidLift, townClose/townLift); the picture and the words come out of the black over fadeIn and go back over fadeOut"
                    }
                };
            var parts = new List<object>
            {
                Part("Picture", "loading_screen.<dungeon>_<n>.png / .<quest>.png / .town_visit.png, the whole screen", new Rect(0f, 0f, ScreenSize.x, ScreenSize.y), s),
                Part("TitleGround", "loading_screen.titleoverlay.png, its middle on tip_frame.posn + title.offset", new Rect(n.TitleGroundAt, TitleArt), s),
                Part("Title", TitleStyle + " (DwarvenAxe large, notable): one centred line, its cell's middle on the plate's", new Rect(n.TitleAt.x - 800f, n.TitleAt.y, 1600f, n.TitleLine), s),
                Part("TipGround", "loading_screen.tipoverlay.png, hung by the middle of its top from tip_frame.posn + tip.background_offset", new Rect(n.TipGroundAt, TipArt), s),
                Part("Tip", TipStyle + " (Ubuntu small, neutral): centred lines from tip_frame.posn + tip.offset, wrapped at tip.size", new Rect(n.TipAt.x - n.TipSize.x * 0.5f, n.TipAt.y, n.TipSize.x, n.TipSize.y), s),
                Part("Torch", TorchSprite + " '" + TorchAnimation + "': its root on title.load_anim_position (flames above, handle 80 below)", new Rect(n.Torch, Vector2.zero), s),
                Part("Continue", AskStyle + " (Ubuntu small, neutral pulsing): one centred line hung from loading_screen_continue.posn; a dungeon's screen only", new Rect(n.Ask.x - 800f, n.Ask.y, 1600f, n.TextLine), s)
            };
            return new
            {
                look = "dd1",
                screen = "1920x1080, x and y of the top left corner, y down",
                parts,
                numbers = new
                {
                    tip_frame_posn = Pt(n.Origin), title_offset = Pt(n.Title), load_anim_position = Pt(n.Torch), tip_offset = Pt(n.Tip), tip_size = Pt(n.TipSize),
                    tip_background_offset = Pt(n.TipGround), continue_posn = Pt(n.Ask), pulse_time = n.Pulse, pulse_to_alpha = n.PulseAlpha,
                    global_fade_transition_time = n.Fade, title_line = n.TitleLine, text_line = n.TextLine
                },
                files = new[] { LayoutFile, AppFile, ColourFile, "fonts/fonts.darkest", StringFile }
            };
        }

        private static object Pt(Vector2 v) => new { x = v.x, y = v.y };

        private static object Part(string name, string what, Rect dd1, LoadingScreen up)
        {
            object built = null;
            var live = up != null && up._screen != null ? up._screen.Find(name) as RectTransform : null;
            if (live != null)
            {
                // PlaceTopLeft: anchored at the screen's top left corner, y up
                var size = live.rect.size;
                var x = live.anchoredPosition.x - live.pivot.x * size.x;
                var y = -live.anchoredPosition.y - (1f - live.pivot.y) * size.y;
                built = new { x, y, w = size.x, h = size.y, shown = live.gameObject.activeInHierarchy };
            }
            return new { name, x = dd1.x, y = dd1.y, w = dd1.width, h = dd1.height, what, built };
        }
    }
}
