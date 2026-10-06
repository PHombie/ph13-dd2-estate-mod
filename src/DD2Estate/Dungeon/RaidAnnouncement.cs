using System.Collections.Generic;
using System.Text.RegularExpressions;
using DD2Estate.Dd1;
using DD2Estate.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD2Estate.Dungeon
{
    /// <summary>The four points DD1's banner may be centred on (screen.raid.darkest, announcement: frame_pos_*).</summary>
    internal enum AnnouncePlace { Left, Right, CentreTop, CentreBottom }

    /// <summary>
    /// What DD1 announces in its banner while a party explores: the words (an id of the English string tables),
    /// their colour (colours/base.colours.darkest), the entry of announcement_times that says how long the banner
    /// is up, and the banner's place.
    ///
    /// From DD1's files: the words, the times, the colours of "TRAP!" and "Disarmed!" (trap_announcement,
    /// disarm_announcement). NOT in any file, and so the mod's reading (docs/recon/dd1-raid-ui.md, 9): which of
    /// the four places a banner takes, the time of "Ambush!" (announcement_times has none: it shares
    /// "surprised"), and the colours of the others, for which DD1's colours of the same words in its running
    /// text are taken (deathdoor, surprised, quirk_positive / quirk_negative).
    /// </summary>
    internal sealed class RaidAnnounce
    {
        /// <summary>The dev bridge's name for it.</summary>
        public readonly string Key;
        public readonly string TextId, TextFallback;
        /// <summary>The entry of announcement_times.</summary>
        public readonly string Time;
        public readonly string ColourId;
        /// <summary>FALLBACK colour: DD1's harmful red, or its notable gold.</summary>
        public readonly bool Harmful;
        public readonly AnnouncePlace Place;

        private RaidAnnounce(string key, string textId, string textFallback, string time, string colourId, bool harmful, AnnouncePlace place)
        {
            Key = key;
            TextId = textId;
            TextFallback = textFallback;
            Time = time;
            ColourId = colourId;
            Harmful = harmful;
            Place = place;
            Known.Add(this);
        }

        private static readonly List<RaidAnnounce> Known = new List<RaidAnnounce>();

        // DD1 hides its HUD and closes in on the hero for a trap, a disarming and a curio (trap.times,
        // disarm_intro.times, investigate_intro.times). Their banners stand at the high place, under the torch:
        // seen in DD1's own frames (_lab/dd1_ref/raid/raid_trap_sprung_failed_disarm.png: "TRAP!" with its frame's
        // middle at 960,210; raid_curio_result_banner_pack_contains_loot_kleptomaniac.png: a curio's sentence
        // there as well; raid_quest_complete_room.png: "Quest Complete!"). What a quirk or a purge of the same
        // close-up says is put there with them. A surprise stands on the side of whoever is surprised.
        public static readonly RaidAnnounce Trap = new RaidAnnounce("trap", "str_its_a_trap", "TRAP!", "trap", "trap_announcement", true, AnnouncePlace.CentreTop);
        public static readonly RaidAnnounce Disarm = new RaidAnnounce("disarm", "str_ui_disarm", "Disarmed!", "disarm", "disarm_announcement", false, AnnouncePlace.CentreTop);
        public static readonly RaidAnnounce Ambush = new RaidAnnounce("ambush", "str_ambush_title", "Ambush!", "surprised", "harmful", true, AnnouncePlace.CentreTop);
        /// <summary>The party is caught off guard: over the heroes' side.</summary>
        public static readonly RaidAnnounce Surprised = new RaidAnnounce("surprised", "surprise_announcement", "Surprised!", "surprised", "surprised", true, AnnouncePlace.Left);
        /// <summary>The party steals up on its enemies: over the side DD1's monsters stand on.</summary>
        public static readonly RaidAnnounce MonstersSurprised = new RaidAnnounce("monsters_surprised", "surprise_announcement", "Surprised!", "surprised", "surprised", false, AnnouncePlace.Right);
        public static readonly RaidAnnounce NewQuirk = new RaidAnnounce("new_quirk", "str_new_quirk_colon", "New Quirk:", "new_quirk", "quirk_positive", false, AnnouncePlace.CentreTop);
        public static readonly RaidAnnounce NewBadQuirk = new RaidAnnounce("new_quirk_negative", "str_new_quirk_colon", "New Quirk:", "new_quirk", "quirk_negative", true, AnnouncePlace.CentreTop);
        public static readonly RaidAnnounce QuirkRemoved = new RaidAnnounce("curio_purge", "curio_announcement_purge_format", "%s Quirk Removed!", "curio_purge", "notable", false, AnnouncePlace.CentreTop);
        public static readonly RaidAnnounce NothingToPurge = new RaidAnnounce("curio_no_purge", "str_curio_no_negative_quirks_to_remove", "No negative quirks to remove.", "curio", "neutral", false, AnnouncePlace.CentreTop);
        public static readonly RaidAnnounce DeathsDoor = new RaidAnnounce("deaths_door", "str_ui_deathdoor", "%s is at Death's Door!", "deaths_door", "deathdoor", true, AnnouncePlace.CentreTop);
        /// <summary>
        /// What an interaction with a curio did, in DD1's own sentence for it (the caller's words), for
        /// announcement_times.curio. GUESS: its colour, which no file names; the frame's words are DD1's gold.
        /// </summary>
        public static readonly RaidAnnounce Curio = new RaidAnnounce("curio", null, "", "curio", "notable", false, AnnouncePlace.CentreTop);
        /// <summary>The quest's goal is met (str_quest_complete_banner, in quest_complete_text); it stays for as long as the choice under it does.</summary>
        public static readonly RaidAnnounce QuestComplete = new RaidAnnounce("quest_complete", "str_quest_complete_banner", "Quest Complete!", "quest_complete", "quest_complete_text", false, AnnouncePlace.CentreTop);

        public static IReadOnlyList<RaidAnnounce> All => Known;

        public static RaidAnnounce Find(string key)
        {
            foreach (var known in Known)
                if (known.Key == key) return known;
            return null;
        }

        public Color Colour => Dd1Fonts.Colour(ColourId, Harmful ? UiKit.Harmful : UiKit.Notable);

        /// <summary>
        /// DD1's words with the subject in their place ("%s is at Death's Door!"), or after them in its own
        /// colour ("New Quirk:" and the quirk's name).
        /// </summary>
        public string Words(string subject)
        {
            var text = RaidText.Get(TextId ?? "", TextFallback) ?? "";
            if (string.IsNullOrEmpty(subject)) return RaidText.Format(text);
            if (text.IndexOf("%s", System.StringComparison.Ordinal) >= 0) return RaidText.Format(text, subject);
            if (text.Length == 0) return subject;
            return "<color=" + RaidText.Hex(UiKit.Neutral) + ">" + RaidText.Format(text) + "</color> " + subject;
        }
    }

    /// <summary>Easing curves by the names DD1's scripts and layouts give them.</summary>
    internal static class RaidEase
    {
        public static float Of(string name, float t)
        {
            t = Mathf.Clamp01(t);
            switch (name)
            {
                case "easeOutElastic":
                    if (t <= 0f || t >= 1f) return t;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
                case "easeOutSine": return Mathf.Sin(t * Mathf.PI * 0.5f);
                case "easeInSine": return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case "easeOutQuad": return 1f - (1f - t) * (1f - t);
                case "easeInOutCubic": return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                default: return t;
            }
        }
    }

    /// <summary>
    /// DD1's announcement banner: overlays/announcement_frame.png centred on one of the four points of
    /// screen.raid.darkest (announcement: frame_pos_left, frame_pos_right, frame_pos_centre_top,
    /// frame_pos_centre_bottom) with its words in the style banner_header at text_offset from that point. The
    /// offset is the top of the words' line cell: DwarvenAxe large's capitals then stand in the middle of the
    /// frame's band, which is how the reading was settled.
    ///
    /// It comes and goes as DD1's two scripts say (scripts/timescript/announcement_show.times: the banner grows
    /// from 0.8 of its size with an elastic ease while it fades in; announcement_hide.times: it fades out), and is
    /// up for the seconds announcement_times gives its kind. Banners asked for while one is up wait their turn.
    /// </summary>
    internal class RaidAnnouncement
    {
        private static readonly Vector2 FrameArt = new Vector2(619f, 136f);     // overlays/announcement_frame.png
        private const string Scale = "announcement_scale", Alpha = "announcement_text_alpha";

        // The mod's own numbers.
        private const float Smallest = 30f;                 // words wider than the frame's picture shrink no further than this
        private const int Waiting = 4;                      // banners that may wait their turn; older ones are dropped

        private class Entry
        {
            public string Key, Words;
            public Color Colour;
            public AnnouncePlace Place;
            public float Seconds;
        }

        private readonly RectTransform _root;
        private readonly Image _frame;
        private readonly Color _frameColour;
        private readonly TextMeshProUGUI _text;
        private readonly TimeScript _show, _hide;
        private readonly float _hideTime;
        private readonly Queue<Entry> _queue = new Queue<Entry>();
        private readonly Dictionary<string, float> _values = new Dictionary<string, float>();
        private Entry _shown;
        private float _since;

        public RaidAnnouncement(RectTransform screen)
        {
            var l = RaidLayout.Current;
            _root = UiKit.Rect("Announcement", screen);
            _root.PlaceTopLeft(l.AnnounceCentreTop, RaidUi.Middle, FrameArt);
            _frame = RaidUi.Art("Frame", _root, "overlays/announcement_frame.png", Vector2.zero, FrameArt, new Color(0.04f, 0.04f, 0.045f, 0.9f));
            _frameColour = _frame.color;
            // FALLBACK: fonts.darkest sets banner_header in DwarvenAxe large
            var style = Dd1Fonts.FontOf("banner_header") != null ? "banner_header" : Dd1Fonts.Large;
            _text = RaidUi.Label("Words", _root, style, FrameArt * 0.5f + l.AnnounceText, new Vector2(FrameArt.x, 72f), TextAlignmentOptions.Top, UiKit.Notable);
            _text.richText = true;
            RaidUi.Fit(_text, Smallest);
            _show = Script("announcement_show", true);
            _hide = Script("announcement_hide", false);
            _hideTime = Length(_hide);
            _root.gameObject.SetActive(false);
        }

        /// <summary>True while a banner is up or waits its turn.</summary>
        public bool Busy => _shown != null || _queue.Count > 0;

        /// <summary>
        /// Asks for a banner; it shows at once, or after the ones asked for before it. Returns the seconds
        /// from now until it has been up for its time (what a caller waits that wants it read).
        /// </summary>
        public float Show(RaidAnnounce what, string subject = null, AnnouncePlace? place = null, Color? colour = null)
        {
            return Show(what.Key, what.Words(subject), colour ?? what.Colour, place ?? what.Place, RaidLayout.Current.AnnounceTime(what.Time));
        }

        public float Show(string key, string words, Color colour, AnnouncePlace place, float seconds)
        {
            if (string.IsNullOrEmpty(words)) return 0f;
            while (_queue.Count >= Waiting) _queue.Dequeue();
            _queue.Enqueue(new Entry { Key = key, Words = words, Colour = colour, Place = place, Seconds = Mathf.Max(0.1f, seconds) });
            var wait = _shown != null ? Mathf.Max(0f, _shown.Seconds + _hideTime - _since) : 0f;
            foreach (var entry in _queue) wait += entry.Seconds + _hideTime;
            return wait - _hideTime;
        }

        /// <summary>
        /// A banner that stays until it is let go (<see cref="Release"/>): "Quest Complete!" stands over the choice
        /// of a completed quest for as long as the choice does. It takes the place of whatever is up.
        /// </summary>
        public void Hold(RaidAnnounce what)
        {
            Release(what.Key);
            _queue.Clear();
            _shown = null;
            _queue.Enqueue(new Entry { Key = what.Key, Words = what.Words(null), Colour = what.Colour, Place = what.Place, Seconds = float.PositiveInfinity });
        }

        /// <summary>Lets a held banner go: it leaves as DD1's hide script has it.</summary>
        public void Release(string key)
        {
            if (_shown != null && _shown.Key == key && float.IsPositiveInfinity(_shown.Seconds)) _shown.Seconds = _since;
            if (_queue.Count > 0 && _queue.Peek().Key == key && float.IsPositiveInfinity(_queue.Peek().Seconds)) _queue.Dequeue();
        }

        /// <summary>Takes the banner down and forgets the ones that wait (the screen is hidden, the expedition over).</summary>
        public void Clear()
        {
            _queue.Clear();
            _shown = null;
            if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        /// <summary>Every frame.</summary>
        public void Tick(float dt)
        {
            if (_shown == null)
            {
                if (_queue.Count == 0) return;
                _shown = _queue.Dequeue();
                _since = 0f;
                _root.anchoredPosition = Place(_shown.Place);
                _text.text = _shown.Words;
                _text.color = _shown.Colour;
                _root.gameObject.SetActive(true);
                _root.SetAsLastSibling();
            }
            else _since += dt;

            _values[Scale] = 1f;
            _values[Alpha] = 1f;
            Sample(_show, _since, _values);
            var over = _since - _shown.Seconds;
            if (over >= 0f)
            {
                if (over >= _hideTime)
                {
                    _shown = null;
                    _root.gameObject.SetActive(false);
                    return;
                }
                Sample(_hide, over, _values);
            }
            var scale = _values[Scale];
            _root.localScale = new Vector3(scale, scale, 1f);
            // announcement_text_alpha is the one value the scripts fade: it is given to the frame as well, which
            // would otherwise be gone from one frame to the next
            var alpha = Mathf.Clamp01(_values[Alpha]);
            _text.alpha = alpha;
            _frame.color = new Color(_frameColour.r, _frameColour.g, _frameColour.b, _frameColour.a * alpha);
        }

        private static Vector2 Place(AnnouncePlace place)
        {
            var l = RaidLayout.Current;
            var at = place == AnnouncePlace.Left ? l.AnnounceLeft : place == AnnouncePlace.Right ? l.AnnounceRight
                : place == AnnouncePlace.CentreBottom ? l.AnnounceCentreBottom : l.AnnounceCentreTop;
            return new Vector2(at.x, -at.y);
        }

        public object Describe()
        {
            return new
            {
                shown = _shown?.Key,
                words = _shown != null ? Regex.Replace(_shown.Words, "<[^>]+>", "") : null,
                place = _shown?.Place.ToString(),
                seconds = _shown?.Seconds,
                since = _shown != null ? _since : 0f,
                at = _shown != null ? new[] { _root.anchoredPosition.x, -_root.anchoredPosition.y } : null,
                scale = _root.localScale.x,
                alpha = _text.alpha,
                waiting = _queue.Count,
                showTime = Length(_show),
                hideTime = _hideTime
            };
        }

        // ---- DD1's two scripts -----------------------------------------------------------------------

        // The script as DD1 has it; its stock commands when the file cannot be read (FALLBACK).
        private static TimeScript Script(string name, bool show)
        {
            var script = TimeScript.Load(name);
            if (script.Commands.Count > 0) return script;
            script = new TimeScript();
            if (show)
            {
                script.Commands.Add(Tween(Scale, 0.8f, 0f, null));
                script.Commands.Add(Tween(Alpha, 0f, 0f, null));
                script.Commands.Add(new TimeScript.Command
                {
                    Kind = TimeScript.Kind.Group,
                    Children = new List<TimeScript.Command> { Tween(Scale, 1f, 0.7f, "easeOutElastic"), Tween(Alpha, 1f, 0.25f, null) }
                });
            }
            else script.Commands.Add(Tween(Alpha, 0f, 0.25f, null));
            return script;
        }

        private static TimeScript.Command Tween(string target, float value, float time, string easing)
        {
            return new TimeScript.Command { Kind = TimeScript.Kind.Tween, Target = target, HasValue = true, Value = value, Time = time, Easing = easing };
        }

        private static float Length(TimeScript script)
        {
            var length = 0f;
            foreach (var command in script.Commands) length += Span(command);
            return length;
        }

        private static float Span(TimeScript.Command command)
        {
            if (command.Kind != TimeScript.Kind.Group) return command.Kind == TimeScript.Kind.Tween || command.Kind == TimeScript.Kind.Wait ? command.Time : 0f;
            var longest = 0f;
            foreach (var child in command.Children) longest = Mathf.Max(longest, child.Time);
            return longest;
        }

        // What a script has made of the values after t seconds: its commands one after another, a group's
        // together (as CorridorView runs the scripts of the scene, here without a routine).
        private static void Sample(TimeScript script, float t, Dictionary<string, float> values)
        {
            var start = 0f;
            foreach (var command in script.Commands)
            {
                if (t < start) return;
                if (command.Kind == TimeScript.Kind.Tween) Step(command, t - start, values);
                else if (command.Kind == TimeScript.Kind.Group)
                    foreach (var child in command.Children)
                        if (child.Kind == TimeScript.Kind.Tween) Step(child, t - start, values);
                start += Span(command);
            }
        }

        private static void Step(TimeScript.Command tween, float t, Dictionary<string, float> values)
        {
            if (!tween.HasValue || !values.TryGetValue(tween.Target, out var from)) return;
            var share = tween.Time <= 0f ? 1f : Mathf.Clamp01(t / tween.Time);
            values[tween.Target] = from + (tween.Value - from) * RaidEase.Of(tween.Easing, share);
        }
    }
}
