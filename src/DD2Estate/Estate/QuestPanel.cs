using System;
using System.Collections.Generic;
using System.Text;
using Assets.Code.Actor;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Dungeon;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Estate
{
    [EstateModule]
    internal static class QuestMapModule
    {
        // DD1: a seasoned hero asked along on a quest beneath them says no. While a quest is chosen on the map
        // the roster column has that hero's answer on their row and does not put them in the party.
        private static void Register()
        {
            EstateSession.PartyBlockers.Add(QuestPanel.Refusal);
        }
    }

    /// <summary>
    /// DD1's quest select screen ("Estate Map"), opened by Embark: the map of the estate with a node per
    /// region (name, level, the bar towards the next level, a padlock while it is closed), this week's quests
    /// as markers at their region (type icon coloured by difficulty in a frame that tells length and story
    /// quests apart), the chosen quest's details on the left, and the party about to set out at the bottom
    /// under its name. "Provision" goes on to the provision screen, after DD1's questions where it has one (a
    /// party short of four, a quest without retreat, trinkets left in the stores); a hero who refuses the quest
    /// keeps the party at home. The hamlet's roster column lies over the map's right edge and its bar over the
    /// map's foot, as in DD1: the party is changed in the column (or by a click on a hero in the tray), and
    /// the estate's gold, heirlooms and bar buttons stay in sight.
    ///
    /// Everything is placed by DD1's own layout files (<see cref="QuestMapLayout"/>) over the art of the
    /// player's DD1 install; the few numbers below are the mod's own. tools/preview_quest_map.py draws the same
    /// layout offline: change one, change the other.
    /// </summary>
    internal class QuestPanel : MonoBehaviour
    {
        // Reference pixels, y down.
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 PanelSize = new Vector2(400f, 843f);     // quest_select.questverbose_bg.png
        private static readonly Vector2 Card = new Vector2(72f, 144f);           // DD1 item card
        private const float MapWidth = HamletScene.Width - RosterPanel.Width;    // the map up to the roster column
        private const float DescriptionBottom = 380f; // the red field of the panel art ends here
        private const float PartyNameWidth = 760f;    // room for the line above the party tray
        private const float SlotSize = 85f;           // campaign/town/hero_slot/hero_slot.background.png
        private const float PortraitInset = 3f, PortraitDrawn = 98f;    // DD2's portrait in DD1's slot, as in the roster and on the provision screen
        private const float MarkerSize = 96f;         // quest_select_length_*.png
        private const float MarkerHit = 70f;
        private const float NameSize = 30f, LevelSize = 22f, QuestNameSize = 30f, BodySize = 20f;
        private const int Slots = 4;
        private const float RefreshSeconds = 0.2f;
        // FALLBACK: DD1's own post_dd1_chance .. post_dd4_chance of shared/dd_effects.darkest, town_quest_select_dd_effect.
        private static readonly float[] EffectChances = { 0.10f, 0.15f, 0.20f, 0.25f };

        private const string Dir = QuestMapLayout.Dir;
        private const string SlotArt = "campaign/town/hero_slot/";
        private const string TrayArt = "campaign/town/embark_party/";
        private const string ProgressionArt = "shared/progression/";

        private class Marker
        {
            public Quest Quest;
            public RectTransform Root, Body;
            public GameObject Splash;
        }

        private class Slot
        {
            public uint Guid;
            public Image Portrait, Refuses;
        }

        private static QuestPanel _instance;
        private static string _lastQuestId;
        // What each region's bar showed when the map was last drawn (level, share of the way to the next): a
        // bar that has grown since fills up to where it stands now.
        private static readonly Dictionary<string, Vector2> BarsShown = new Dictionary<string, Vector2>();

        private bool _open, _embarkAway;
        private Transform _canvasRoot;
        private RectTransform _frame, _over, _overFrame;
        private QuestMapLayout _layout;
        private QuestMapTooltip _tooltip;
        private readonly List<Marker> _markers = new List<Marker>();
        private readonly List<Slot> _slots = new List<Slot>();
        // DD1's questions still to be asked before the party goes on, and the one that is up
        private readonly List<string> _questions = new List<string>();
        private string _asked;
        private Quest _selected;
        private string _partyKey;
        private float _nextRefresh;
        private TextMeshProUGUI _questName, _description, _camping, _specifics, _goals, _goal, _rewardsTitle, _noticeTitle, _noticeText;
        private Image _noticeIcon;
        private RectTransform _rewards;
        // the line above the tray: the words in sight, the words that will follow them, and where the fade stands
        private TextMeshProUGUI _partyName;
        private string _partyNameShown, _partyNameWanted;
        private bool _partyNamedShown, _partyNamedWanted;
        private float _partyNameAlpha;
        // reward trinkets whose DD2 icon is still loading: the picture is put in when it is there
        private readonly List<KeyValuePair<Image, string>> _iconsWanted = new List<KeyValuePair<Image, string>>();

        public static bool IsOpen => _instance != null && _instance._open;

        public static void Open(Transform canvasRoot)
        {
            if (canvasRoot == null) return;
            if (_instance == null) _instance = Create(canvasRoot);
            _instance.Show();
        }

        public static void Close()
        {
            if (_instance == null) return;
            _instance._open = false;
            _instance._tooltip?.Hide(null);
            TownConfirm.Close(_instance);
            _instance.gameObject.SetActive(false);
            if (_instance._over != null) _instance._over.gameObject.SetActive(false);
            if (_instance._embarkAway) TownChrome.ShowEmbark(_instance._canvasRoot, true);
            _instance._embarkAway = false;
        }

        /// <summary>
        /// What this hero says to the quest chosen on the map when they will not go (DD1's str_quest_too_easy);
        /// null when they will, or when no map is open.
        /// </summary>
        public static string Refusal(uint guid)
        {
            if (!IsOpen || _instance._selected == null) return null;
            // DD1's "Never Again": one who has finished a quest in the Darkest Dungeon says so in their own words
            var never = DarkestDungeon.Refusal(guid, _instance._selected);
            if (never != null) return never;
            return Resolve.RefusalReason(guid, _instance._selected.Difficulty) != null ? QuestMapText.TooEasy(guid, _instance._selected.Id) : null;
        }

        // ---- dev bridge ----------------------------------------------------------------------------------

        /// <summary>Dev bridge: select an offer by index and embark with the standard kit, skipping the provision screen.</summary>
        public static bool EmbarkOffer(int index, bool free = false)
        {
            var offers = QuestBoard.Current();
            if (index < 0 || index >= offers.Count || EstateSession.PartySize == 0) return false;
            Close();
            ProvisionScreen.Close();
            QuestBoard.Embark(offers[index], Provisioning.StandardBag(offers[index].Length, free));
            return true;
        }

        /// <summary>Dev bridge: open the provision screen for an offer, as the map's "Provision" does (without its party checks).</summary>
        public static bool ProvisionOffer(int index)
        {
            var offers = QuestBoard.Current();
            var hamlet = FindObjectOfType<HamletScreen>();
            if (hamlet == null || index < 0 || index >= offers.Count || EstateSession.PartySize == 0) return false;
            Close();
            ProvisionScreen.Open(hamlet.transform, offers[index]);
            return true;
        }

        /// <summary>Dev bridge: open the map as the hamlet's Embark button does.</summary>
        public static bool DevOpen()
        {
            if (RosterPanel.CanvasRoot == null || !HamletScreen.IsOpen) return false;
            Open(RosterPanel.CanvasRoot);
            return true;
        }

        /// <summary>Dev bridge: choose an offer on the open map by its index in <see cref="QuestBoard.Current"/>.</summary>
        public static bool SelectOffer(int index)
        {
            var offers = QuestBoard.Current();
            if (!IsOpen || index < 0 || index >= offers.Count) return false;
            _instance.Select(offers[index], true);
            return true;
        }

        /// <summary>
        /// Dev bridge: "provision" presses the way forward; while one of DD1's questions is up it answers that
        /// one and all that would follow with "Still Embark" (so a party that is asked something goes on with
        /// the second call). "back" leaves the map.
        /// </summary>
        public static bool DevPress(string button)
        {
            if (!IsOpen) return false;
            switch (button)
            {
                case "provision":
                    if (!TownConfirm.IsOpenFor(_instance)) return _instance.OnForward();
                    _instance._questions.Clear();
                    TownConfirm.Close(_instance);
                    _instance.Ask();
                    return true;
                case "back": Close(); return true;
                default: return false;
            }
        }

        public static object Describe()
        {
            var quests = new List<object>();
            var offers = QuestBoard.Current();
            for (var i = 0; i < offers.Count; i++)
            {
                var q = offers[i];
                var trinkets = new List<object>();
                foreach (var t in q.Trinkets) trinkets.Add(new { rarity = t.Rarity, item = t.Item, name = t.Item != null ? Trinkets.Name(t.Item) : null, dd2 = Trinkets.SubTypeOf(t.Item) });
                quests.Add(new
                {
                    index = i, q.Id, q.Name, shown = QuestMapText.QuestName(q), q.Dungeon, q.Type, q.Tier, q.LengthName, q.Plot, difficulty = q.Difficulty,
                    levelCap = Resolve.LevelCap(q.Difficulty), selected = IsOpen && _instance._selected == q,
                    gold = q.Gold, heirlooms = q.Heirlooms, trinkets, countsForRegion = q.DungeonXp, goal = q.Goal
                });
            }
            var regions = new List<object>();
            foreach (var dungeon in DungeonContent.Dungeons())
            {
                var id = (string)dungeon["id"];
                var progress = QuestBoard.Progress(id);
                regions.Add(new
                {
                    id, open = progress.Open, level = progress.Level, done = progress.Done, toNextLevel = progress.ToNextLevel, share = progress.Share,
                    locked = QuestBoard.LockReason(id), tooltip = QuestMapText.RegionTooltip(id, progress, BossOnOffer(offers, id)), townEvent = EventLine(id)
                });
            }
            if (!IsOpen) return new { open = false, quests, regions };
            var party = Provisioning.Party();
            var heroes = new List<object>();
            foreach (var hero in party)
                heroes.Add(new { guid = hero.ActorGuid, name = hero.ActorName, heroClass = hero.ActorDataId, resolve = Resolve.Level(hero.ActorGuid), refuses = Refusal(hero.ActorGuid) });
            var asking = TownConfirm.IsOpenFor(_instance);
            return new
            {
                open = true, selected = _instance._selected?.Id, ready = _instance.Ready(party), status = _instance._partyNameWanted, partyName = PartyNameOf(party),
                confirmSmallParty = asking, question = asking ? _instance._asked : null, party = heroes, quests, regions
            };
        }

        // ---- construction: the shell once, the map on every opening (regions open, offers change) ---------

        private static QuestPanel Create(Transform canvasRoot)
        {
            // The map itself: under the hamlet's roster column and under the estate's bar (see Show), which
            // lie over its right edge and its foot the way DD1's do. The ink covers the hamlet and takes its
            // clicks; on a screen wider than DD1's it is what shows beside the map.
            var root = UiKit.Rect("QuestPanel", canvasRoot);
            UiKit.Stretch(root);
            root.gameObject.SetActive(false);
            var panel = root.gameObject.AddComponent<QuestPanel>();
            panel._canvasRoot = canvasRoot;
            var ink = UiKit.Image("Ink", root, null, UiKit.Ink, true);
            UiKit.Stretch((RectTransform)ink.transform);
            ink.gameObject.AddComponent<QuestMapHit>().Right = Close;     // DD1: a right click leaves a town screen
            panel._frame = UiKit.Rect("Frame", root).Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(HamletScene.Width, HamletScene.Height));

            // What stands on the bar or over it: the party tray with its name, the way forward, tooltips and
            // DD1's questions. A layer of its own, over everything the hamlet draws.
            var over = UiKit.Rect("QuestPanelOver", canvasRoot);
            UiKit.Stretch(over);
            over.gameObject.SetActive(false);
            panel._over = over;
            return panel;
        }

        private void Show()
        {
            RosterWindow.Close();
            TownChrome.PutUnder(transform, "Roster", "EstateSummary");
            _over.SetAsLastSibling();
            // this screen's way on is "Provision", where the hamlet's Embark stands
            if (TownChrome.ShowEmbark(_canvasRoot, false)) _embarkAway = true;
            // active before it is filled: the labels measure themselves
            gameObject.SetActive(true);
            _over.gameObject.SetActive(true);
            _open = true;
            _nextRefresh = 0f;
            try
            {
                Rebuild();
            }
            catch (Exception e)
            {
                // a half-drawn map must not sit on top of the hamlet and swallow its clicks
                Plugin.Log.LogError("Hamlet: the estate map could not be drawn: " + e);
                Close();
                return;
            }
            Fit();
        }

        private void Rebuild()
        {
            Clear(_frame);
            Clear(_over);
            _overFrame = UiKit.Rect("Frame", _over).Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(HamletScene.Width, HamletScene.Height));
            _markers.Clear();
            _slots.Clear();
            _questions.Clear();
            _partyKey = null;
            _partyNameShown = null;
            _layout = new QuestMapLayout();

            var offers = QuestBoard.Current();
            BuildMap(offers);
            BuildChrome();
            BuildDetails();
            BuildParty();
            _tooltip = new QuestMapTooltip(_overFrame, MapWidth);

            Quest pick = null;
            foreach (var quest in offers)
                if (quest.Id == _lastQuestId) pick = quest;
            Select(pick ?? (offers.Count > 0 ? offers[0] : null), false);
        }

        private static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var old = parent.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }
        }

        private void BuildMap(IReadOnlyList<Quest> offers)
        {
            var background = Art("Background", _frame, Dir + "quest_select.background.png", Vector2.zero, new Vector2(HamletScene.Width, HamletScene.Height));
            background.raycastTarget = true;
            background.gameObject.AddComponent<QuestMapHit>().Right = Close;    // DD1: a right click leaves a town screen

            var regions = new List<string>();
            foreach (var dungeon in DungeonContent.Dungeons())
            {
                var id = (string)dungeon["id"];
                if (!_layout.Has(id)) continue;
                // DD1 (requires_quest_to_display): the hamlet's own streets are on the map only while a quest is there
                if (DungeonContent.NeedsQuest(id))
                {
                    var wanted = false;
                    foreach (var quest in offers) wanted |= quest.Dungeon == id;
                    if (!wanted) continue;
                }
                regions.Add(id);
            }

            // DD1 lets the thing under the manor show through the map once the Darkest Dungeon has been
            // entered: each region's picture of it flickers up now and then, the oftener the more descents are made.
            var descents = QuestBoard.Progress(QuestBoard.DarkestDungeon).Level;
            foreach (var id in regions)
                DdFlicker.Attach(Art("Effect." + id, _frame, Dir + "dd_effect_" + id + ".png", _layout.EffectPos(id)), "town_quest_select_dd_effect", descents, EffectChances);

            var nodes = UiKit.Rect("Regions", _frame).Stretch();
            var grew = false;
            foreach (var id in regions) grew |= BuildNode(nodes, id, BossOnOffer(offers, id));
            // DD1's sound of a region's bar moving on
            if (grew) EstateAudio.Ui("ui/town/dungeon_progress");

            var markers = UiKit.Rect("Quests", _frame).Stretch();
            _bossPictures.Clear();
            foreach (var id in regions)
            {
                // the Darkest Dungeon's five stand-ins stand in a row of their own: won, on offer, yet to come
                if (id == QuestBoard.DarkestDungeon && DarkestBosses.Holds)
                {
                    BuildBossRow(markers, offers);
                    continue;
                }
                var here = new List<Quest>();
                foreach (var quest in offers)
                    if (quest.Dungeon == id) here.Add(quest);
                var centres = _layout.MarkerCentres(id, here.Count);
                for (var i = 0; i < here.Count; i++) BuildMarker(markers, here[i], centres[i]);
            }

            // DD1 puts a picture of the week's town event behind the tray when the event is about the party
            // that sets out (campaign/town/embark_party/<event>.png: a rising sun, the crowd of the Helping
            // Hand). Its foot is cut straight: it stands behind the estate's bar, which hides the cut.
            var active = TownEvents.Current;
            if (active != null)
            {
                var picture = QuestMapArt.Sprite(TrayArt + active.Id + ".png");
                if (picture != null)
                {
                    var image = UiKit.Image("TownEvent", _frame, picture);
                    ((RectTransform)image.transform).PlaceTopLeft(_layout.TrayPos + _layout.TrayEvent, new Vector2(0.5f, 0f), picture.rect.size);
                    FadeIn.Attach(image, _layout.TrayEventSeconds);
                }
            }
        }

        private static bool BossOnOffer(IReadOnlyList<Quest> offers, string dungeonId)
        {
            foreach (var quest in offers)
                if (quest.Dungeon == dungeonId && quest.Plot && quest.Type == Core.QuestTypes.KillBoss) return true;
            return false;
        }

        /// <summary>What the week's town event does to those who set out for this region; null when nothing (DD1's embark_party_buff with a buff that holds in one dungeon).</summary>
        private static string EventLine(string dungeonId)
        {
            var active = TownEvents.Current;
            foreach (var effect in TownEvents.Effects(active, "embark_party_buff"))
            {
                var buff = TownEvents.Catalog.Buff(effect.Text);
                if (buff != null && buff.Rule == "in_dungeon" && buff.RuleText == dungeonId) return string.Join("\n", TownEventText.Info(active));
            }
            return null;
        }

        /// <summary>What the week's town event means for this quest, for the notice in the panel's foot; null when nothing.</summary>
        private static string EventNotice(Quest quest)
        {
            var active = TownEvents.Current;
            if (active == null || quest == null) return null;
            var about = false;
            foreach (var effect in TownEvents.Effects(active, "embark_party_buff"))
            {
                var buff = TownEvents.Catalog.Buff(effect.Text);
                about |= buff == null || buff.Rule != "in_dungeon" || buff.RuleText == quest.Dungeon;
            }
            foreach (var effect in TownEvents.Effects(active, "remove_quest_hero_level_restriction")) about |= effect != null;
            return about ? string.Join("\n", TownEventText.Info(active)) : null;
        }

        /// <summary>True when the region's bar has grown since the map was last drawn.</summary>
        private bool BuildNode(Transform parent, string id, bool bossOnOffer)
        {
            var progress = QuestBoard.Progress(id);
            var pos = _layout.MapPos(id);
            var grew = false;
            Art("Bar." + id, parent, Dir + (progress.Open ? "dungeon_progressionbar.png" : "dungeon_no_progressionbar.png"), pos + _layout.NodeBackground);

            // right aligned: the bar art keeps an ink smudge for it, ending at the skull
            var name = Label("Name." + id, parent, pos + _layout.NodeName, new Vector2(1f, 1f), new Vector2(320f, 40f), NameSize,
                progress.Open ? QuestMapColours.Get("town_quest_select_dungeon_name") : QuestMapColours.Neutral, TextAlignmentOptions.TopRight, false, "town_quest_select_dungeon_name");
            name.text = DungeonContent.DisplayName(id);

            if (progress.Open)
            {
                var back = UiKit.Image("FillBack." + id, parent, null, Color.black);
                ((RectTransform)back.transform).PlaceTopLeft(pos + _layout.NodeBar, TopLeft, _layout.NodeBarSize);
                var fill = UiKit.Image("Fill." + id, parent, QuestMapArt.Gradient(QuestMapColours.Get("town_quest_select_xp_bar_gradient_left"), QuestMapColours.Get("town_quest_select_xp_bar_gradient_right")));
                ((RectTransform)fill.transform).PlaceTopLeft(pos + _layout.NodeBar, TopLeft, _layout.NodeBarSize);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                var share = Mathf.Clamp01(progress.Share);
                fill.fillAmount = share;
                // A bar that has moved on since it was last seen fills up to its new place (DD1:
                // town_quest_select_xp_bar_anim), from where it stood, or from nothing after a new level.
                if (BarsShown.TryGetValue(id, out var shown))
                {
                    var sameBar = Mathf.Approximately(shown.x, progress.Level) || id == QuestBoard.DarkestDungeon;
                    grew = progress.Level > shown.x || share > shown.y + 0.0005f;
                    if (grew) QuestBarFill.Attach(fill, sameBar ? Mathf.Min(shown.y, share) : 0f, share, _layout.BarSeconds, pos + _layout.NodeBar + _layout.NodeBarFx, _layout.NodeBarSize.x);
                }
                BarsShown[id] = new Vector2(progress.Level, share);
                // in the skull's mouth
                var level = Label("Level." + id, parent, pos + _layout.NodeLevel, new Vector2(0.5f, 1f), new Vector2(48f, 30f), LevelSize,
                    QuestMapColours.Get("town_quest_select_dungeon_level"), TextAlignmentOptions.Top, false, "town_quest_select_dungeon_level");
                level.text = progress.Level.ToString();
            }
            else Art("Lock." + id, parent, Dir + "locked_dungeon.png", pos + _layout.NodeLock);

            // DD1's tooltip of a region's bar, its corner where the layout puts it (dungeon_xp_bar_tt_offset)
            var hot = UiKit.Image("Hot." + id, parent, null, Color.clear, true);
            ((RectTransform)hot.transform).PlaceTopLeft(pos + _layout.NodeHot, TopLeft, _layout.NodeHotSize);
            var hit = hot.gameObject.AddComponent<QuestMapHit>();
            hit.Right = Close;
            hit.Hover = inside =>
            {
                if (inside) _tooltip.ShowAt(hot, pos + _layout.NodeTooltip, null, QuestMapText.RegionTooltip(id, QuestBoard.Progress(id), bossOnOffer));
                else _tooltip.Hide(hot);
            };

            // DD1 marks a region the week's town event is about (quest_select_town_event.png) and says what it does there
            var eventLine = EventLine(id);
            if (eventLine != null)
            {
                Art("Event." + id, parent, Dir + "quest_select_town_event.png", pos + _layout.NodeEvent);
                var eventHot = UiKit.Image("EventHot." + id, parent, null, Color.clear, true);
                ((RectTransform)eventHot.transform).PlaceTopLeft(pos + _layout.NodeEventHot, TopLeft, _layout.NodeEventHotSize);
                var eventHit = eventHot.gameObject.AddComponent<QuestMapHit>();
                eventHit.Right = Close;
                eventHit.Hover = inside =>
                {
                    if (inside) _tooltip.ShowAt(eventHot, pos + _layout.NodeEvent + _layout.NodeEventTooltip, null, eventLine);
                    else _tooltip.Hide(eventHot);
                };
            }
            return grew;
        }

        private void BuildMarker(Transform parent, Quest quest, Vector2 centre)
        {
            var marker = new Marker { Quest = quest };
            marker.Root = UiKit.Rect("Quest." + quest.Id, parent).PlaceTopLeft(centre, Middle, new Vector2(MarkerSize, MarkerSize));
            var half = new Vector2(MarkerSize, MarkerSize) * 0.5f;
            marker.Splash = Art("Selected", marker.Root, Dir + "quest_select_selected.png", half + _layout.MarkerSelected, null, new Color(0.45f, 0.02f, 0.02f, 0.6f)).gameObject;
            marker.Splash.SetActive(false);

            marker.Body = UiKit.Rect("Body", marker.Root).Stretch();
            var frame = QuestMapArt.Sprite(Dir + "quest_select_length_" + (quest.Plot ? "plot_" : "generated_") + quest.Length + ".png");
            // DD1 has a marker of its own for some plot quests (quest_select_plot_crow_trinket.png: the Shrieker's)
            var icon = (PlotQuests.IsOurs(quest) ? QuestMapArt.Sprite(Dir + "quest_select_" + quest.Id + ".png") : null)
                       ?? QuestMapArt.Sprite(Dir + "quest_select_" + quest.Type + "_" + quest.Difficulty + ".png")
                       ?? QuestMapArt.Sprite(Dir + "quest_select_explore_" + quest.Difficulty + ".png");
            if (frame != null) ((RectTransform)UiKit.Image("Length", marker.Body, frame).transform).PlaceTopLeft(half, Middle, frame.rect.size);
            Image type = null;
            if (icon != null) ((RectTransform)(type = UiKit.Image("Type", marker.Body, icon)).transform).PlaceTopLeft(half, Middle, icon.rect.size);
            // one of the Darkest Dungeon's five stand-ins: the game's own picture of its Confession, where it is to be had
            if (DarkestBosses.IsOurs(quest)) WantBossPicture(marker.Body, type, quest.Id, half, Color.white);

            // without DD1's pictures the marker is still there to click: a dark disc with the tier's letter
            var hot = UiKit.Image("Hit", marker.Body, null, frame != null || icon != null ? Color.clear : new Color(0.1f, 0.09f, 0.08f, 0.95f), true);
            ((RectTransform)hot.transform).PlaceTopLeft(half, Middle, new Vector2(MarkerHit, MarkerHit));
            if (frame == null && icon == null)
                ((RectTransform)UiKit.Text("Letter", hot.transform, QuestBoard.TierLabel(quest).Substring(0, 1), 30f, quest.Plot ? QuestMapColours.Notable : QuestMapColours.Neutral).transform).Stretch();
            var hit = hot.gameObject.AddComponent<QuestMapHit>();
            hit.Left = () => Select(quest, true);
            hit.Right = Close;
            hit.Hover = inside =>
            {
                // DD1's sound of the pointer coming onto a quest. No box of words: the mod had one of its own with the
                // quest's name, particulars and pay, all of which the panel at the left says once the quest is
                // chosen; the owner had it taken off (2026-10-06).
                if (inside) EstateAudio.Ui("ui/town/button_mouse_over");
            };
            _markers.Add(marker);
        }

        // ---- the Darkest Dungeon's row of five (Estate/DarkestBosses.cs) -----------------------------------

        /// <summary>How dark a won place of the row is drawn (its marker's colours times this).</summary>
        public static Color BossWonTint = new Color(0.42f, 0.4f, 0.38f, 1f);
        /// <summary>
        /// The game's picture of a Confession in DD1's marker as it was until 2026-10-06: the side of the square
        /// its rectangle was fitted into, in DD1's pixels (DD1's own icon is 48). It stood small in the ring; it
        /// now fills the ring's inside (QuestBossPicture, which keeps this for a look at the old size).
        /// </summary>
        public static float BossPictureSize = 44f;
        /// <summary>The side of DD1's padlock on a place of the row that is not open yet (the art is 72).</summary>
        public static float BossLockSize = 64f;
        /// <summary>False: DD1's own mark of a boss quest stands in every place of the row, whatever the game has.</summary>
        public static bool BossGamePictures = true;

        private class BossPicture
        {
            public Transform Body;
            public Image Dd1Icon;
            public DarkestBosses.Confession Confession;
            public Vector2 Half;
            public Color Tint;
        }

        // places of the row whose picture of the game's is still on its way (its fight HUD is being loaded)
        private readonly List<BossPicture> _bossPictures = new List<BossPicture>();

        // While the five stand-ins hold the Darkest Dungeon: a row of five places in the game's order of its
        // Confessions. The one on offer is a quest's marker like any other; a won one is the same marker gone
        // dark; a later one is DD1's padlock. The pointer is told which is which.
        private void BuildBossRow(Transform parent, IReadOnlyList<Quest> offers)
        {
            var row = DarkestBosses.Row();
            var centres = _layout.RowCentres(QuestBoard.DarkestDungeon, row.Count, MapWidth - MarkerHit * 0.5f - 4f);
            for (var i = 0; i < row.Count; i++)
            {
                Quest quest = null;
                if (row[i].Place == Core.DarkestBossPlace.Open)
                    foreach (var offer in offers)
                        if (offer.Id == row[i].Confession.QuestId) quest = offer;
                if (quest != null) BuildMarker(parent, quest, centres[i]);
                else BuildBossPlace(parent, row[i], centres[i]);
            }
            // After the fifth the map says so in so many words, under the row (the pointer on the region's bar is
            // told the same). A black twin behind the line: the sky behind it is bright.
            var done = DarkestBosses.DoneLine;
            if (done == null || row.Count == 0) return;
            var under = new Vector2((centres[0].x + centres[row.Count - 1].x) * 0.5f, centres[0].y + MarkerHit * 0.5f + 2f);
            var size = new Vector2(_layout.MarkerSpacing.x * row.Count + 80f, 28f);
            Label("DarkestDone.Shadow", parent, under + new Vector2(1.5f, 1.5f), new Vector2(0.5f, 1f), size, BodySize, Color.black, TextAlignmentOptions.Top, false, "town_quest_specifics").text = done;
            Label("DarkestDone", parent, under, new Vector2(0.5f, 1f), size, BodySize, QuestMapColours.Get("town_quest_specifics"), TextAlignmentOptions.Top, false, "town_quest_specifics").text = done;
        }

        private void BuildBossPlace(Transform parent, DarkestBosses.RowPlace place, Vector2 centre)
        {
            var size = new Vector2(MarkerSize, MarkerSize);
            var half = size * 0.5f;
            var root = UiKit.Rect("Confession." + place.Confession.Act, parent).PlaceTopLeft(centre, Middle, size);
            var drawn = false;
            if (place.Place == Core.DarkestBossPlace.Won)
            {
                // the quest's own marker (a short plot quest against a boss of the darkest difficulty), gone dark
                var frame = QuestMapArt.Sprite(Dir + "quest_select_length_plot_1.png");
                var icon = QuestMapArt.Sprite(Dir + "quest_select_" + Core.QuestTypes.KillBoss + "_6.png");
                if (frame != null) ((RectTransform)UiKit.Image("Length", root, frame, BossWonTint).transform).PlaceTopLeft(half, Middle, frame.rect.size);
                Image type = null;
                if (icon != null) ((RectTransform)(type = UiKit.Image("Type", root, icon, BossWonTint)).transform).PlaceTopLeft(half, Middle, icon.rect.size);
                WantBossPicture(root, type, place.Confession.QuestId, half, BossWonTint);
                drawn = frame != null || icon != null;
            }
            else
            {
                var padlock = QuestMapArt.Sprite(Dir + "locked_dungeon.png");
                if (padlock != null) ((RectTransform)UiKit.Image("Lock", root, padlock).transform).PlaceTopLeft(half, Middle, new Vector2(BossLockSize, BossLockSize));
                drawn = padlock != null;
            }

            // without DD1's pictures the place is still there: a dark disc with its number
            var hot = UiKit.Image("Hit", root, null, drawn ? Color.clear : new Color(0.1f, 0.09f, 0.08f, 0.8f), true);
            ((RectTransform)hot.transform).PlaceTopLeft(half, Middle, new Vector2(MarkerHit, MarkerHit));
            if (!drawn) ((RectTransform)UiKit.Text("Number", hot.transform, place.Confession.Number.ToString(), 30f, QuestMapColours.Neutral).transform).Stretch();
            var hit = hot.gameObject.AddComponent<QuestMapHit>();
            hit.Right = Close;
            hit.Hover = inside =>
            {
                if (!inside)
                {
                    _tooltip.Hide(hot);
                    return;
                }
                DarkestBosses.PlaceWords(place, out var title, out var body);
                _tooltip.Show(hot, centre + new Vector2(-MarkerSize * 0.5f, -MarkerSize * 0.5f - 4f), title, body);
            };
        }

        // The game's own picture of a Confession stands in the marker where DD1's icon of a boss quest stands,
        // once the game has it (its fight HUD, which carries its boss pictures, is loaded in the background).
        private void WantBossPicture(Transform body, Image dd1Icon, string questId, Vector2 half, Color tint)
        {
            if (!BossGamePictures) return;
            var act = Core.DarkestBossRules.ActOf(questId);
            DarkestBosses.Confession confession = null;
            foreach (var c in DarkestBosses.Chain)
                if (c.Act == act) confession = c;
            if (confession == null) return;
            var wanted = new BossPicture { Body = body, Dd1Icon = dd1Icon, Confession = confession, Half = half, Tint = tint };
            if (!PutBossPicture(wanted, out var pending) && pending) _bossPictures.Add(wanted);
        }

        private static bool PutBossPicture(BossPicture wanted, out bool pending)
        {
            pending = false;
            if (wanted.Body == null) return true;
            var sprite = DarkestBosses.Picture(wanted.Confession, out pending);
            if (sprite == null) return false;
            // as large as the ring's inside takes what the picture shows, and about the ring's middle (QuestBossPicture)
            var fit = QuestBossPicture.Of(sprite);
            var image = UiKit.Image("Confession", wanted.Body, fit.Sprite, wanted.Tint);
            ((RectTransform)image.transform).PlaceTopLeft(wanted.Half + new Vector2(fit.Offset.x, -fit.Offset.y), Middle, fit.Size);
            if (wanted.Dd1Icon != null)
            {
                image.transform.SetSiblingIndex(wanted.Dd1Icon.transform.GetSiblingIndex() + 1);
                wanted.Dd1Icon.enabled = false;
            }
            return true;
        }

        private void LoadBossPictures()
        {
            for (var i = _bossPictures.Count - 1; i >= 0; i--)
                if (PutBossPicture(_bossPictures[i], out var pending) || !pending) _bossPictures.RemoveAt(i);
        }

        // DD1 leaves a town screen by the red arrows at the head of the name plate's band, and by a right click.
        private void BuildChrome()
        {
            // DD1 keeps the estate's crest and the band with its name over the map (the red arrows stand on the
            // band, left of the name): the hamlet's own plate, copied in, since the map lies over it.
            var plate = _canvasRoot != null ? _canvasRoot.Find("Title") : null;
            if (plate != null) Instantiate(plate.gameObject, _frame, false).name = "EstatePlate";

            var arrows = Art("BackArrows", _frame, ProgressionArt + "progression_back.png", _layout.Back);
            var arrowSize = arrows.sprite != null ? arrows.sprite.rect.size : new Vector2(32f, 33f);
            if (arrows.sprite == null)
            {
                ((RectTransform)arrows.transform).sizeDelta = arrowSize;
                arrows.color = UiKit.Blood;
            }
            var hot = UiKit.Image("Back", _frame, null, Color.clear, true);
            ((RectTransform)hot.transform).PlaceTopLeft(_layout.Back - new Vector2(6f, 6f), TopLeft, arrowSize + new Vector2(12f, 12f));
            var hit = hot.gameObject.AddComponent<QuestMapHit>();
            hit.Left = Close;
            hit.Right = Close;
            hit.Hover = inside => TownChrome.Highlight(arrows, inside);
        }

        private void BuildDetails()
        {
            var l = _layout;
            var sprite = QuestMapArt.Sprite(Dir + "quest_select.questverbose_bg.png");
            // takes the clicks: the panel is not the map
            var back = UiKit.Image("Details", _frame, sprite, sprite != null ? Color.white : new Color(0.02f, 0.02f, 0.02f, 0.96f), true);
            var panel = ((RectTransform)back.transform).PlaceTopLeft(l.PanelPos, TopLeft, sprite != null ? sprite.rect.size : PanelSize);
            var width = l.DescriptionWidth;

            _questName = Label("Name", panel, l.QuestName, new Vector2(0.5f, 1f), new Vector2(352f, 40f), QuestNameSize, QuestMapColours.Get("town_quest_name"), TextAlignmentOptions.Center, false, "town_quest_name");
            Shrink(_questName, 18f);
            _description = Label("Description", panel, l.Description, TopLeft, new Vector2(width, DescriptionBottom - l.Description.y), BodySize,
                QuestMapColours.Get("town_quest_description"), TextAlignmentOptions.TopLeft, true, "town_quest_description");
            Shrink(_description, 14f);

            // the campfire is drawn in the panel art; DD1 writes the number of camps next to it and says in a
            // tooltip beside it (camping_tt_offset) what the number means
            _camping = Label("Camping", panel, l.Camping, TopLeft, new Vector2(40f, 30f), 22f, QuestMapColours.Get("town_quest_camping"), TextAlignmentOptions.TopLeft, false, "town_quest_camping");
            var campfire = UiKit.Image("CampingHot", panel, null, Color.clear, true);
            ((RectTransform)campfire.transform).PlaceTopLeft(l.CampingHot, TopLeft, l.CampingHotSize);
            campfire.gameObject.AddComponent<QuestMapHit>().Hover = inside =>
            {
                if (inside && _selected != null) _tooltip.ShowAt(campfire, l.PanelPos + l.CampingHot + l.CampingTooltip, null, QuestMapText.Camps(Camps(_selected)));
                else _tooltip.Hide(campfire);
            };
            _specifics = Label("Specifics", panel, l.Specifics, new Vector2(0.5f, 1f), new Vector2(320f, 28f), BodySize, QuestMapColours.Get("town_quest_specifics"), TextAlignmentOptions.Top, false, "town_quest_specifics");
            Shrink(_specifics, 15f);

            _goals = Label("Goals", panel, l.Goals, TopLeft, new Vector2(width, 28f), BodySize, QuestMapColours.Get("town_quest_goals"), TextAlignmentOptions.TopLeft, false, "town_quest_goals");
            _goals.text = QuestMapText.Goals;
            _goal = Label("Goal", panel, l.GoalStart, TopLeft, new Vector2(l.Description.x + width - l.GoalStart.x, 56f), BodySize, QuestMapColours.Get("town_quest_goal_start_description"), TextAlignmentOptions.TopLeft, true, "town_quest_goal_start_description");
            Shrink(_goal, 15f);

            _rewardsTitle = Label("RewardsTitle", panel, l.RewardsTitle, new Vector2(0.5f, 1f), new Vector2(300f, 40f), QuestNameSize, QuestMapColours.Get("town_quest_rewards"), TextAlignmentOptions.Center, false, "town_quest_rewards");
            _rewardsTitle.text = QuestMapText.Rewards;
            // a card stands inventory_item_layout.icon_offset inside its cell
            var item = RaidLayout.Current;
            _rewards = UiKit.Rect("Rewards", panel).PlaceTopLeft(l.RewardsGrid + item.ItemIconOffset, TopLeft, new Vector2(l.RewardPitch.x * (l.RewardColumns - 1) + Card.x, Card.y));

            // The panel's foot is DD1's place for a notice about the quest: here the week's town event, when it
            // is about this quest (town_notification_*: the bell, "Town Event:", what the event does).
            _noticeIcon = Art("NoticeIcon", panel, Dir + "quest_select_town_event_notification.png", l.NoticeIcon);
            var noticeRoom = Mathf.Min(l.NoticeWidth, PanelSize.x - l.NoticeTitle.x - 12f);
            _noticeTitle = Label("NoticeTitle", panel, l.NoticeTitle, TopLeft, new Vector2(noticeRoom, 26f), 18f, QuestMapColours.Neutral, TextAlignmentOptions.TopLeft, false, "town_quest_select_town_event_notification");
            _noticeTitle.text = QuestMapText.TownEventTitle;
            _noticeText = Label("NoticeText", panel, l.NoticeText, TopLeft, new Vector2(noticeRoom, PanelSize.y - l.NoticeText.y - 10f), 18f, QuestMapColours.Neutral, TextAlignmentOptions.TopLeft, true, "town_quest_select_town_event_notification");
            Shrink(_noticeText, 13f);

            // DD1's name of a town screen, in the blank head of the panel art: the map's icon and "Estate Map"
            // (on DD1's own screen the icon lies at 164, 148 and the name begins at 296: name_pos 104 122).
            Dd1ScreenName.Add(_frame, l.NamePos, Dir + "quest_select.icon.png", QuestMapText.ScreenName, null,
                Mathf.Max(120f, l.PanelPos.x + PanelSize.x - Dd1ScreenName.NameLeft(l.NamePos) - 10f));
        }

        // The tray stands on the estate's bar, so it is drawn on the layer over the hamlet's own bar.
        private void BuildParty()
        {
            var l = _layout;
            var tray = Art("Tray", _overFrame, TrayArt + "embark_party.background.png", l.TrayPos + l.TrayBackground, null, new Color(0.03f, 0.03f, 0.035f, 0.92f));
            if (tray.sprite == null) ((RectTransform)tray.transform).sizeDelta = new Vector2(l.SlotStart.x * 2f + l.SlotSpacing.x * (Slots - 1) + SlotSize, l.SlotStart.y * 2f + SlotSize);
            tray.raycastTarget = true;

            for (var i = 0; i < Slots; i++)
            {
                var slot = new Slot();
                // DD1 shows the party as it will stand: rank 1, the first hero chosen, on the right
                var at = l.TrayPos + l.SlotStart + l.SlotSpacing * (Slots - 1 - i);
                var back = Art("Slot" + i, _overFrame, SlotArt + "hero_slot.background.png", at, new Vector2(SlotSize, SlotSize), new Color(0.07f, 0.07f, 0.07f));
                back.raycastTarget = true;
                // DD1's portrait fills the slot (icon_offset 0 0); DD2's is drawn as in the roster and on the provision screen's tray
                var window = UiKit.Rect("Window", back.transform);
                window.PlaceTopLeft(new Vector2(PortraitInset, PortraitInset), TopLeft, new Vector2(SlotSize - 2f * PortraitInset, SlotSize - 2f * PortraitInset));
                window.gameObject.AddComponent<RectMask2D>();
                slot.Portrait = UiKit.Image("Portrait", window, null);
                slot.Portrait.preserveAspect = true;
                ((RectTransform)slot.Portrait.transform).Place(Middle, Middle, Vector2.zero, new Vector2(PortraitDrawn, PortraitDrawn));
                slot.Portrait.gameObject.SetActive(false);
                slot.Refuses = Art("Refuses", back.transform, SlotArt + "hero_slot.negative_frame.png", Vector2.zero, new Vector2(SlotSize, SlotSize), new Color(0.7f, 0.1f, 0f, 0.35f));
                slot.Refuses.gameObject.SetActive(false);

                var hit = back.gameObject.AddComponent<QuestMapHit>();
                hit.Left = () =>
                {
                    if (slot.Guid == 0u) return;
                    EstateSession.ToggleParty(slot.Guid);    // back to the roster
                    _tooltip.Hide(back);
                    RefreshParty(false);
                };
                // The mod's own box: who stands there, and DD1's line of a hero who will not go. (What DD1 shows
                // on a slot of the tray, and its dragging of heroes between slots, are not settled from its files.)
                hit.Hover = inside =>
                {
                    var hero = inside && slot.Guid != 0u ? Find(slot.Guid) : null;
                    if (hero == null)
                    {
                        _tooltip.Hide(back);
                        return;
                    }
                    var refusal = Refusal(slot.Guid);
                    _tooltip.Show(back, at + new Vector2(0f, -6f), hero.ActorName,
                        HeroNames.ClassName(hero) + ", resolve level " + Resolve.Level(slot.Guid) + (refusal != null ? "\n" + Dd1Ui.Tint(refusal, "harmful") : "") + "\nClick to send back to the roster.");
                };
                _slots.Add(slot);
            }

            // The line above the tray is the party's name: DD1's name for these four in this order ("The Usual
            // Suspects"), or its word to a party it has no name for. It comes and goes by DD1's fades.
            _partyName = Label("PartyName", _overFrame, l.PartyName, new Vector2(0.5f, 1f), new Vector2(PartyNameWidth, 30f), 22f, QuestMapColours.Faded, TextAlignmentOptions.Top, false, "town_quest_select_party_name_default");
            Shrink(_partyName, 15f);

            var forward = TownChrome.Forward(_over, QuestMapText.Forward, () => OnForward(), out _);
            // under the tray, whose foot stands on it, and under the shade of a question
            forward.transform.SetAsFirstSibling();
        }

        // ---- state ---------------------------------------------------------------------------------------

        private void OnDisable()
        {
            // Still active itself: the hamlet was hidden around the screen. It must not come back with the hamlet.
            if (gameObject.activeSelf) _open = false;
        }

        private void Update()
        {
            if (!_open || !EstateSession.InHub || EstateSession.View != EstateSession.Screen.Hamlet)
            {
                Close();
                return;
            }
            Fit();
            Pulse();
            FadePartyName();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            LoadIcons();
            LoadBossPictures();
            RefreshParty(false);
        }

        // The canvas keeps 1080 units of height. Wider than 16:9 the frame sits in the middle like the hamlet;
        // narrower it shrinks until the map up to the roster column fits.
        private void Fit()
        {
            var width = ((RectTransform)transform).rect.width - RosterPanel.Width;
            if (width <= 0f) return;
            var fit = Mathf.Min(1f, width / MapWidth);
            var x = Mathf.Max(0f, (width + RosterPanel.Width - HamletScene.Width) * 0.5f);
            if (!Mathf.Approximately(fit, _frame.localScale.x)) _frame.localScale = new Vector3(fit, fit, 1f);
            if (!Mathf.Approximately(x, _frame.anchoredPosition.x)) _frame.anchoredPosition = new Vector2(x, 0f);
            if (_overFrame == null) return;
            _overFrame.localScale = _frame.localScale;
            _overFrame.anchoredPosition = _frame.anchoredPosition;
        }

        // DD1's town_quest_select_pulse_anim: the chosen marker swells and settles, eased like a sine.
        private void Pulse()
        {
            if (_selected == null) return;
            var phase = Mathf.PingPong(Time.unscaledTime / _layout.PulseSeconds, 1f);
            var scale = 1f + _layout.PulseScale * (0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI));
            foreach (var marker in _markers)
                if (marker.Quest == _selected) marker.Body.localScale = new Vector3(scale, scale, 1f);
        }

        /// <param name="byHand">The player chose it: DD1's sound of a quest picked.</param>
        private void Select(Quest quest, bool byHand)
        {
            if (byHand && quest != null && quest != _selected) EstateAudio.Ui("ui/town/dungeon_select");
            _selected = quest;
            if (quest != null) _lastQuestId = quest.Id;
            _questions.Clear();
            TownConfirm.Close(this);
            foreach (var marker in _markers)
            {
                var chosen = marker.Quest == quest;
                marker.Splash.SetActive(chosen);
                marker.Body.localScale = Vector3.one;
                if (chosen) marker.Root.SetAsLastSibling();
            }
            ShowDetails();
            RefreshParty(true);
        }

        private void ShowDetails()
        {
            Clear(_rewards);
            var quest = _selected;
            _goals.gameObject.SetActive(quest != null);
            _rewardsTitle.gameObject.SetActive(quest != null);
            var notice = EventNotice(quest);
            _noticeIcon.gameObject.SetActive(notice != null);
            _noticeTitle.gameObject.SetActive(notice != null);
            _noticeText.text = notice ?? "";
            if (quest == null)
            {
                _questName.text = "No quests this week";
                _description.text = _camping.text = _specifics.text = _goal.text = "";
                return;
            }
            _questName.text = QuestMapText.QuestName(quest);
            _description.text = QuestMapText.Description(quest);
            // DD1 writes the count as "x0", "x1" (its own screen of a short quest: "x0" beside the fire)
            _camping.text = "x" + Camps(quest);
            _specifics.text = QuestMapText.Specifics(quest);
            _goal.text = quest.Goal ?? "";

            // a centred row of DD1 item cards: the gold, the heirlooms in the purse's order, the trinkets
            var rewards = new List<KeyValuePair<string, int>>();
            if (quest.Gold > 0) rewards.Add(new KeyValuePair<string, int>("gold", quest.Gold));
            foreach (var id in EstateState.HeirloomIds)
                if (quest.Heirlooms.TryGetValue(id, out var amount) && amount > 0) rewards.Add(new KeyValuePair<string, int>(id, amount));
            foreach (var pair in quest.Heirlooms)
                if (pair.Value > 0 && Array.IndexOf(EstateState.HeirloomIds, pair.Key) < 0) rewards.Add(pair);
            _iconsWanted.Clear();
            var cards = rewards.Count + quest.Trinkets.Count;
            // DD1's grid has four columns, which is what its quests pay; more than that moves closer together
            var pitch = cards > _layout.RewardColumns ? _layout.RewardPitch.x * (_layout.RewardColumns - 1) / (cards - 1) : _layout.RewardPitch.x;
            var shift = Mathf.Max(0, _layout.RewardColumns - cards) * _layout.RewardPitch.x * 0.5f;
            for (var i = 0; i < rewards.Count; i++) BuildReward(rewards[i].Key, rewards[i].Value, new Vector2(shift + i * pitch, 0f));
            for (var i = 0; i < quest.Trinkets.Count; i++) BuildTrinket(quest.Trinkets[i], i, new Vector2(shift + (rewards.Count + i) * pitch, 0f));
            LoadIcons();
        }

        private void BuildReward(string id, int amount, Vector2 at)
        {
            var card = Art("Reward." + id, _rewards, RewardArt(id, amount), at, Card, new Color(0.08f, 0.07f, 0.06f, 0.95f));
            card.raycastTarget = true;
            // the count stands at inventory_item_layout.amount_text_offset of the cell, the card icon_offset inside it
            var item = RaidLayout.Current;
            var countAt = item.ItemAmount - item.ItemIconOffset;
            var size = new Vector2(Card.x - countAt.x, 40f);
            // some cards are bright in the corner (coins, a crest): the count gets a black twin behind it
            Label("CountShadow", card.transform, countAt + new Vector2(1.5f, 1.5f), TopLeft, size, 22f, Color.black, TextAlignmentOptions.TopLeft, false, "inventory_amount").text = UpgradeUi.Amount(amount);
            Label("Count", card.transform, countAt, TopLeft, size, 22f, QuestMapColours.Get("inventory_amount"), TextAlignmentOptions.TopLeft, false, "inventory_amount").text = UpgradeUi.Amount(amount);
            card.gameObject.AddComponent<QuestMapHit>().Hover = inside =>
            {
                if (inside) _tooltip.ShowAt(card, CardTooltip(at), QuestMapText.RewardName(id), QuestMapText.RewardDescription(id), item.ItemTooltipWidth);
                else _tooltip.Hide(card);
            };
        }

        // DD1 picks the pile of gold by the sum (shared/inventory/item.display.json: gold.icon_thresholds); the
        // sum is the quest's in DD1's own gold.
        private static string RewardArt(string id, int amount)
        {
            if (id != "gold") return "panels/icons_equip/heirloom/inv_heirloom+" + id + ".png";
            return "panels/icons_equip/gold/inv_gold+_" + QuestMapArt.GoldPile(amount) + ".png";
        }

        // DD1 hangs an item's tooltip beside its card (inventory_item_tooltip_layout: offset from the cell's corner).
        private Vector2 CardTooltip(Vector2 at)
        {
            return _layout.PanelPos + _layout.RewardsGrid + at + RaidLayout.Current.ItemTooltipOffset;
        }

        // DD1 draws a trinket's own card; the trinket here is DD2's, so its own icon sits on DD1's card of
        // the rarity and the tooltip says what DD2's item tooltip says.
        private void BuildTrinket(QuestTrinket trinket, int index, Vector2 at)
        {
            var card = Art("Reward.trinket" + index, _rewards, Trinkets.RarityArt(trinket.Rarity), at, Card, new Color(0.08f, 0.07f, 0.06f, 0.95f));
            card.raycastTarget = true;
            if (trinket.Item != null)
            {
                var icon = TrinketPicture.Add(card.transform);
                _iconsWanted.Add(new KeyValuePair<Image, string>(icon, trinket.Item));
            }
            else
            {
                // nothing of the rarity was left to show when the week was rolled: one is drawn when the quest is done
                ((RectTransform)UiKit.Text("Unknown", card.transform, "?", 40f, QuestMapColours.Notable).transform).Stretch();
            }
            card.gameObject.AddComponent<QuestMapHit>().Hover = inside =>
            {
                if (!inside)
                {
                    _tooltip.Hide(card);
                    return;
                }
                var line = QuestMapText.TrinketLine(trinket);
                var body = trinket.Item != null ? line + "\n" + Trinkets.Description(trinket.Item) : line;
                _tooltip.ShowAt(card, CardTooltip(at), trinket.Item != null ? Trinkets.Name(trinket.Item) : "Trinket", body);
            };
        }

        // DD2 loads item icons on demand: the cards ask until theirs has come.
        private void LoadIcons()
        {
            for (var i = _iconsWanted.Count - 1; i >= 0; i--)
            {
                var image = _iconsWanted[i].Key;
                if (image == null)
                {
                    _iconsWanted.RemoveAt(i);
                    continue;
                }
                var sprite = Trinkets.Icon(_iconsWanted[i].Value);
                if (sprite == null)
                {
                    if (Trinkets.HasNoIcon(_iconsWanted[i].Value)) _iconsWanted.RemoveAt(i);
                    continue;
                }
                image.sprite = sprite;
                image.color = Color.white;
                _iconsWanted.RemoveAt(i);
            }
        }

        // DD1 hands out firewood by quest length: one log, one camp.
        private static int Camps(Quest quest) => CampContent.Rules.Firewood(quest.Length);

        /// <summary>Whether this party may go on to the provision screen: a quest is chosen, somebody goes, and nobody of them refuses it.</summary>
        private bool Ready(List<ActorInstance> party)
        {
            if (_selected == null || party.Count == 0) return false;
            foreach (var hero in party)
                if (Resolve.RefusalReason(hero.ActorGuid, _selected) != null) return false;
            return true;
        }

        /// <summary>DD1's name of a party of four as the tray shows it (back rank first); null without one.</summary>
        private static string PartyNameOf(List<ActorInstance> party)
        {
            if (party.Count != Slots) return null;
            var classes = new List<string>();
            for (var i = party.Count - 1; i >= 0; i--) classes.Add(party[i].ActorDataId);
            return QuestMapText.PartyName(classes);
        }

        /// <param name="force">Redraw even if neither the quest nor the party changed.</param>
        private void RefreshParty(bool force)
        {
            var party = Provisioning.Party();
            var key = new StringBuilder(_selected != null ? _selected.Id : "");
            foreach (var hero in party) key.Append('|').Append(hero.ActorGuid).Append(':').Append(Resolve.Level(hero.ActorGuid));
            var signature = key.ToString();
            if (signature == _partyKey && !force) return;
            if (signature != _partyKey)
            {
                // DD1 asks again when the party changes
                _questions.Clear();
                TownConfirm.Close(this);
            }
            _partyKey = signature;

            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var hero = i < party.Count ? party[i] : null;
                slot.Guid = hero != null ? hero.ActorGuid : 0u;
                slot.Portrait.gameObject.SetActive(hero != null);
                if (hero != null)
                {
                    var portrait = HeroNames.Portrait(hero);
                    slot.Portrait.sprite = portrait;
                    slot.Portrait.color = portrait != null ? Color.white : new Color(0.24f, 0.23f, 0.2f);
                }
                slot.Refuses.gameObject.SetActive(hero != null && _selected != null && Resolve.RefusalReason(hero.ActorGuid, _selected) != null);
            }

            var name = PartyNameOf(party);
            _partyNameWanted = name ?? QuestMapText.PartyNameDefault;
            _partyNamedWanted = name != null;
            if (_partyNameShown == null) SetPartyName(1f);      // the map has just been drawn: the line is there
        }

        // quest_select.anim.darkest, town_quest_select_party_name_anim: the old words go in text_fade_out_time,
        // the new ones come in text_fade_in_time (the file's easeInOutSine). With a name DD1 knows comes its effect.
        private void FadePartyName()
        {
            if (_partyName == null || _partyNameWanted == null) return;
            var changing = _partyNameShown != _partyNameWanted;
            var seconds = changing ? _layout.PartyNameOut : _layout.PartyNameIn;
            var step = seconds > 0f ? Time.unscaledDeltaTime / seconds : 1f;
            if (changing)
            {
                _partyNameAlpha -= step;
                if (_partyNameAlpha > 0f) Tint();
                else
                {
                    SetPartyName(0f);
                    if (_partyNamedShown) PartyComboFx.Play(_overFrame, _layout.PartyCombo, _partyName.transform.GetSiblingIndex());
                }
            }
            else if (_partyNameAlpha < 1f)
            {
                _partyNameAlpha = Mathf.Min(1f, _partyNameAlpha + step);
                Tint();
            }
        }

        private void SetPartyName(float alpha)
        {
            _partyNameShown = _partyNameWanted;
            _partyNamedShown = _partyNamedWanted;
            _partyNameAlpha = alpha;
            _partyName.text = _partyNameShown;
            UiKit.Style(_partyName, _partyNamedShown ? "town_quest_select_party_name" : "town_quest_select_party_name_default");
            _partyName.fontSizeMax = _partyName.fontSize;
            Tint();
        }

        private void Tint()
        {
            var colour = _partyNamedShown ? QuestMapColours.Get("town_quest_select_party_name") : QuestMapColours.Faded;
            var share = Mathf.Clamp01(_partyNameAlpha);
            colour.a = 0.5f - 0.5f * Mathf.Cos(share * Mathf.PI);
            _partyName.color = colour;
        }

        // DD1 chains quest select, provision, embark: the party is checked here, provisioned next, and that
        // screen's "Embark" starts the expedition. Before the party goes on DD1 asks what it has to ask, each
        // question in its confirm dialog with "Still Embark" and "Cancel Embark".
        private bool OnForward()
        {
            if (TownConfirm.IsOpenFor(this)) return false;
            var party = Provisioning.Party();
            if (!Ready(party))
            {
                // DD1's sound of a way that is shut: nobody goes, or one of the party will not
                EstateAudio.Ui("ui/town/button_click_locked");
                return false;
            }
            EstateAudio.Ui("ui/town/button_click");
            _questions.Clear();
            if (party.Count < Slots) _questions.Add(QuestMapText.SmallParty);
            // DD1's questions in a week whose town event has a quest: for that quest ("Retreating will result in
            // the death of one of your heroes"), or for leaving it alone ("By ignoring the Brigand Incursion ...")
            var eventQuestions = PlotQuests.EmbarkQuestions(_selected);
            // DD1's rules of its plot quests (can_retreat, retreat_party_kill_count)
            if (!_selected.CanRetreat) _questions.Add(QuestMapText.CannotRetreat);
            else if (_selected.RetreatDeaths > 0 && eventQuestions.Count == 0) _questions.Add(QuestMapText.DarkestRetreat);
            _questions.AddRange(eventQuestions);
            if (TrinketsLeft(party)) _questions.Add(QuestMapText.NoTrinkets);
            Ask();
            return true;
        }

        // DD1 (town_provision_no_trinkets_equipped): "Your party is not fully outfitted with trinkets. Really
        // embark?" Its options count it among the warnings "when advancing from Quest Select to Provisioning"
        // (menu_options_element_tooltip_quest_select_warnings), so it is asked here. GUESS: asked when a hero of
        // the party has a trinket slot free and the stores hold a trinket that hero could wear; the files have
        // the words, not the test.
        private static bool TrinketsLeft(List<ActorInstance> party)
        {
            try
            {
                var stored = Trinkets.Stored();
                if (stored.Count == 0) return false;
                foreach (var hero in party)
                {
                    if (!RealmInventory.Worn(hero.ActorGuid).Contains(null)) continue;
                    foreach (var id in stored)
                        if (RealmInventory.WearRefusal(hero.ActorGuid, id) == null) return true;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Estate map: the party's trinkets could not be read: " + e.Message); }
            return false;
        }

        // The next of DD1's questions, or on to the provision screen when none is left.
        private void Ask()
        {
            if (_questions.Count == 0)
            {
                var quest = _selected;
                Close();
                ProvisionScreen.Open(_canvasRoot, quest);
                return;
            }
            _asked = _questions[0];
            _questions.RemoveAt(0);
            TownConfirm.Ask(this, _asked, new[] { QuestMapText.StillEmbark, QuestMapText.CancelEmbark }, answer =>
            {
                if (answer == 0) Ask();
                else _questions.Clear();
            });
        }

        // ---- helpers -------------------------------------------------------------------------------------

        private static ActorInstance Find(uint guid)
        {
            foreach (var hero in Provisioning.Party())
                if (hero.ActorGuid == guid) return hero;
            return null;
        }

        /// <summary>A DD1 image at its own size (or <paramref name="size"/>); a flat box, or nothing, when the file is missing.</summary>
        private static Image Art(string name, Transform parent, string dd1File, Vector2 topLeft, Vector2? size = null, Color? fallback = null)
        {
            var sprite = QuestMapArt.Sprite(dd1File);
            var image = UiKit.Image(name, parent, sprite, sprite != null ? Color.white : fallback ?? Color.clear);
            ((RectTransform)image.transform).PlaceTopLeft(topLeft, TopLeft, size ?? (sprite != null ? sprite.rect.size : Vector2.zero));
            return image;
        }

        /// <param name="pivot">The point of the label's box put at <paramref name="position"/> (parent pixels from its top left, y down).</param>
        private static TextMeshProUGUI Label(string name, Transform parent, Vector2 position, Vector2 pivot, Vector2 size, float fontSize, Color color, TextAlignmentOptions align, bool wrap = false, string style = null)
        {
            var label = UiKit.Text(name, parent, "", fontSize, color, align);
            // A DD1 text style is drawn 1:1 in its own font, as DD1 draws it; the size is for a label without one.
            if (style != null) UiKit.Style(label, style);
            ((RectTransform)label.transform).PlaceTopLeft(position, pivot, size);
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;                 // a text is never cut: it shrinks (Shrink), then runs over
            // One line shrinks to its box; it never ends in an ellipsis.
            if (!wrap) Dd1Ui.NoCut(label, label.fontSize * 0.55f);
            return label;
        }

        // Long names and narrations get smaller rather than cut off.
        private static void Shrink(TextMeshProUGUI label, float smallest)
        {
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = smallest;
        }
    }
}
