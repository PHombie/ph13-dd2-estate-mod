using System;
using System.Collections.Generic;
using Assets.Code.Events;
using Assets.Code.Presentation.Events;
using DD2Estate.Dd1;
using DD2Estate.Dungeon;
using DD2Estate.Estate;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace DD2Estate.Dd2
{
    /// <summary>
    /// What of DD1's sound the estate plays, and how it sits next to DD2's own audio.
    ///
    ///   where the estate is            DD1 plays (events of the player's own DD1 banks, see Dd1/Dd1Audio)
    ///   the hamlet                     music/mus_town, ambience/town/general; with a building's window open
    ///                                  the music's own "inside" parameter (DD1's snapshot takes it down 5 dB)
    ///                                  and the building's ambience/town/&lt;building&gt; in place of the town's
    ///   a dungeon's corridors          ambience/dungeon/&lt;dungeon&gt; (the Darkest Dungeon: quest/plot_darkest_dungeon_n)
    ///                                  and music/mus_exploration, both following the torch through "darkness"
    ///   a camp                         music/mus_camp and ambience/local/campfire over the dungeon's ambience
    ///   a fight, a loading screen      nothing of DD1's: it fades, DD2 plays its own, and it begins again after
    ///   the Ancestor speaks            the line's /vo/... event; music and ambience step back under his voice
    ///   clicks in the hamlet           DD1's town and ui_town sounds where the moment can be seen from outside
    ///                                  (see <see cref="Moments"/>)
    ///
    /// DD2's side. Its volume sliders are VCAs (vca:/Master, Music, SFX, VO; Assets.Code.Audio.AudioVolumeUtils)
    /// and are read back every frame for the mod's four channel groups. The master bus's mute (the game mutes
    /// it when its window loses focus) and pause are copied; the game's pause (EventGamePauseChanged) holds the
    /// voice and the effects and takes music and ambience down. DD2's music needs no silencing in the hub: its
    /// MusicMgrBhv stops what it plays when a mode it does not know is entered (and logs a warning each time);
    /// <see cref="Dd2MusicStaysOutOfTheEstate"/> does the same without the warning and leaves fights to it.
    ///
    /// Nothing here can be heard by whoever wrote it: docs/recon/dd1-audio.md says what is proven on paper.
    /// </summary>
    [EstateModule]
    internal static class EstateAudio
    {
        /// <summary>The whole of DD1's audio on or off (for a config entry: see docs/recon/dd1-audio.md).</summary>
        public static bool Enabled = true;
        /// <summary>DD1's sound against DD2's, 0..1, on top of DD2's own sliders.</summary>
        public static float Level = 1f;
        /// <summary>DD1's exploration music in the corridors, and its camp music at a camp.</summary>
        public static bool DungeonMusic = true, CampMusic = true;

        // DESIGN CALLS (nothing in DD1's files gives these; DD1's own are a compressor and its pause snapshot).
        private const float UnderVoice = 0.56f;         // music and ambience while the Ancestor speaks: -5 dB
        private const float UnderPause = 0.5f;          // music and ambience while the game is paused
        private const float DuckSpeed = 2.5f;           // of full scale a second
        private const float SceneFade = 1.5f;           // music and ambience when the place changes
        private const float DoorFade = 1f;              // the town's ambience when a building opens or closes
        private const float VoiceTail = 0.4f;           // the words stay this long after the voice
        private const float HushFade = 0.25f;

        private enum Place { None, Hamlet, Dungeon }

        private static Place _place = Place.None;
        private static string _dungeon;
        private static bool _camp;
        private static Dd1Playing _music, _ambience, _inside, _campMusic, _campFire, _voice;
        private static string _insideOf;
        private static float _voiceOverAt, _duck = 1f, _pauseDuck = 1f, _nextComplaint;
        private static bool _gamePaused, _listening, _bandSeen;
        private static VCA _master, _musicSlider, _sfxSlider, _voSlider;
        private static Bus _masterBus;
        private static readonly System.Random Random = new System.Random();

        private static void Register()
        {
            // The same kind of object the plugin runs on: the game destroys the loader's own (MODLOG, gotcha 1).
            var go = new GameObject("DD2Estate.Audio") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>();
            Moments.Register();
        }

        private class Driver : MonoBehaviour
        {
            private void Update() => Tick();

            private void OnApplicationQuit() => Dd1Audio.Shutdown();
        }

        private static void Tick()
        {
            try
            {
                var wanted = Enabled && EstateSession.Active && EstateSession.InHub ? (EstateSession.View == EstateSession.Screen.Dungeon ? Place.Dungeon : Place.Hamlet) : Place.None;
                if (wanted == Place.Dungeon && DungeonRun.Current == null) wanted = Place.None;
                // Outside a session nothing is started, but what the bridge plays is still kept going.
                if (wanted != Place.None && !Dd1Audio.Start()) wanted = Place.None;
                if (!Dd1Audio.Ready) return;

                Listen();
                Move(wanted);
                if (_place == Place.Hamlet) Hamlet();
                else if (_place == Place.Dungeon) Corridor();
                if (_place != Place.None) Moments.Watch();
                Voice();
                Levels();
                Dd1Audio.Tick(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                if (Time.unscaledTime < _nextComplaint) return;
                _nextComplaint = Time.unscaledTime + 10f;
                Plugin.Log.LogError("EstateAudio: " + e);
            }
        }

        // ---- where the estate is ----

        private static void Move(Place wanted)
        {
            var dungeon = wanted == Place.Dungeon ? DungeonEvent() : null;
            if (wanted == _place && dungeon == _dungeon) return;
            var setOff = _place == Place.Hamlet && wanted == Place.Dungeon;
            Leave();
            _place = wanted;
            _dungeon = dungeon;
            Moments.Reset();
            if (setOff) Ui("ui/town/set_off_button");
            if (wanted == Place.Hamlet)
            {
                // INFERRED: DD1 has a parameter that swaps the order of the town theme's two halves and its code sets
                // it somewhere the files do not show; a coin is tossed per visit.
                _music = Dd1Audio.Play("music/mus_town", new Dictionary<string, float> { { "play_variance", Random.Next(2) } });
                _ambience = Dd1Audio.Play("ambience/town/general");
            }
            else if (wanted == Place.Dungeon && dungeon != null)
            {
                var dark = new Dictionary<string, float> { { "darkness", Darkness() } };
                _ambience = Dd1Audio.Play(dungeon, dark);
                // DD1 turns the exploration music off in the last descent (snapshot:/darkest_4_explore_music_off).
                if (DungeonMusic && !dungeon.EndsWith("plot_darkest_dungeon_4", StringComparison.Ordinal)) _music = Dd1Audio.Play("music/mus_exploration", dark);
            }
        }

        private static void Leave()
        {
            Stop(ref _music, SceneFade);
            Stop(ref _ambience, SceneFade);
            Stop(ref _inside, SceneFade);
            Stop(ref _campMusic, SceneFade);
            Stop(ref _campFire, SceneFade);
            _insideOf = null;
            _camp = false;
            // The Ancestor does not talk over a fight or into the menu.
            if (_place != Place.None) Hush();
        }

        private static void Stop(ref Dd1Playing playing, float fade)
        {
            try { playing?.Stop(fade); }
            catch (Exception e) { Plugin.Log.LogWarning("EstateAudio: stop failed: " + e.Message); }
            playing = null;
        }

        private static string DungeonEvent()
        {
            var id = DungeonRun.Current?.Exploration?.Map?.DungeonId;
            if (string.IsNullOrEmpty(id)) return null;
            // an expedition that is nothing but its fight (the Darkest Dungeon's stand-ins) has no corridor to sound
            if (DungeonRun.Current.IsBossQuest) return null;
            // DD1 keeps the Darkest Dungeon's ambience per descent, as it keeps its art.
            if (id == "darkestdungeon") return "ambience/dungeon/quest/plot_darkest_dungeon_" + Mathf.Clamp(Dd1Install.DarkestQuestArt, 1, 4);
            var path = "ambience/dungeon/" + id;
            // A place DD1 has no ambience of (the mod's own dungeons): the Ruins' stone.
            return Dd1Audio.Project != null && Dd1Audio.Project.Knows(path) ? path : "ambience/dungeon/crypts";
        }

        // INFERRED: DD1's ambience and exploration music take a "darkness" of 0..10 with their layers changing
        // at 2, 4..5, 7..8 and 10: the torch (100..0) in tenths, turned round, puts DD1's four light bands on them.
        private static float Darkness()
        {
            var run = DungeonRun.Current;
            return run == null ? 0f : Mathf.Clamp((100f - (float)run.Exploration.Light) / 10f, 0f, 10f);
        }

        private static void Hamlet()
        {
            // A building's window: DD1 sets the music's "inside" (its snapshot takes the music down) and plays
            // the building's own ambience. INFERRED: the town's ambience steps back meanwhile.
            var open = RosterWindow.IsOpen ? RosterWindow.Current.Id : null;
            _music?.SetParameter("inside", open != null ? 1f : 0f);
            if (open == _insideOf) return;
            _insideOf = open;
            Stop(ref _inside, DoorFade);
            var path = open != null ? "ambience/town/" + open : null;
            if (path != null && Dd1Audio.Project.Knows(path)) _inside = Dd1Audio.Play(path);
            // DD1 has an event for every building, but not a sound in every one (the Survivalist's is empty).
            if (_inside != null && _inside.Finished) _inside = null;
            if (_inside != null) _inside.FadeTo(0f, 0f);
            _inside?.FadeTo(1f, DoorFade);
            _ambience?.FadeTo(_inside != null ? 0f : 1f, DoorFade);
        }

        private static void Corridor()
        {
            var darkness = Darkness();
            _ambience?.SetParameter("darkness", darkness);
            _music?.SetParameter("darkness", darkness);

            var camp = CampMusic && CampScreen.IsOpen;
            if (camp == _camp) return;
            _camp = camp;
            if (camp)
            {
                Stop(ref _music, SceneFade);
                _campMusic = Dd1Audio.Play("music/mus_camp", new Dictionary<string, float> { { "play_variance", Random.Next(2) } });
                _campFire = Dd1Audio.Play("ambience/local/campfire");
            }
            else
            {
                Stop(ref _campMusic, SceneFade);
                Stop(ref _campFire, SceneFade);
                if (DungeonMusic && _dungeon != null && !_dungeon.EndsWith("plot_darkest_dungeon_4", StringComparison.Ordinal))
                    _music = Dd1Audio.Play("music/mus_exploration", new Dictionary<string, float> { { "darkness", darkness } });
            }
        }

        // ---- the Ancestor's voice ----

        /// <summary>
        /// Speaks a line of the narration. <paramref name="source"/> is what Narration keeps with a line: for
        /// one of DD1's it ends in the line's audio event ("camp /vo/good/camp_04"); the mod's own story has
        /// none and stays text. True when a voice was started.
        /// </summary>
        public static bool Speak(string source)
        {
            try
            {
                Hush();
                var path = VoicePath(source);
                // Asked of the session, not of the place this class has got to: the first line of a visit may be
                // shown in the very frame the hamlet comes up, before this class has looked.
                if (path == null || !Enabled || !EstateSession.Active || !EstateSession.InHub || !Dd1Audio.Start()) return false;
                _voice = Dd1Audio.Play(path);
                _voiceOverAt = 0f;
                _bandSeen = NarrationBox.IsOpen;
                return _voice != null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("EstateAudio: " + source + ": " + e.Message);
                return false;
            }
        }

        private static string VoicePath(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            var at = source.LastIndexOf(' ');
            var last = at >= 0 ? source.Substring(at + 1) : source;
            return last.StartsWith("/vo/", StringComparison.Ordinal) || last.StartsWith("event:/vo/", StringComparison.Ordinal) ? last : null;
        }

        /// <summary>True while a line's voice is being opened or is heard.</summary>
        public static bool Speaking => _voice != null && !_voice.Finished && _voice.IsSounding;

        /// <summary>
        /// Until when (Time.unscaledTime) the words on the screen belong to the voice: while he speaks, the time
        /// his line ends; 0 when no voice is speaking. A click does not send a spoken line away before this.
        /// </summary>
        public static float SpokenUntil
        {
            get
            {
                if (!Speaking) return _voiceOverAt;
                var left = Math.Max(0.0, _voice.LengthSeconds - _voice.Seconds);
                return Time.unscaledTime + (float)left + VoiceTail;
            }
        }

        /// <summary>
        /// Whether a line shown with <see cref="Speak"/> may leave the screen: a spoken one when its voice is
        /// over (DD1 times its subtitles by the clip), a silent one at <paramref name="readingDeadline"/>.
        /// </summary>
        public static bool LineIsOver(float readingDeadline)
        {
            if (Speaking) return false;
            return _voiceOverAt > 0f ? Time.unscaledTime >= _voiceOverAt : Time.unscaledTime >= readingDeadline;
        }

        /// <summary>Silences the voice (the line was taken off the screen).</summary>
        public static void Hush()
        {
            if (_voice != null) Stop(ref _voice, HushFade);
            _voiceOverAt = 0f;
        }

        private static void Voice()
        {
            if (_voice == null) return;
            if (!_voice.Finished && _voice.IsSounding)
            {
                // The words are gone (a fight began, an expedition set out, the bridge hushed him): the voice goes
                // with them. A voice started without words on the screen (the bridge's audio.speak) is left to finish.
                if (NarrationBox.IsOpen) _bandSeen = true;
                else if (_bandSeen) Hush();
                return;
            }
            // Over. If it never sounded (the file would not open) the line keeps its reading time.
            _voiceOverAt = _voice.Seconds >= _voice.LengthSeconds * 0.5 ? Time.unscaledTime + VoiceTail : 0f;
            _voice = null;
        }

        // ---- DD2's side ----

        private static void Listen()
        {
            if (_listening) return;
            _listening = true;
            try { EventManager.AddListener<EventGamePauseChanged>(OnGamePause); }
            catch (Exception e) { Plugin.Log.LogWarning("EstateAudio: the game's pause is not followed: " + e.Message); }
        }

        private static void OnGamePause(EventGamePauseChanged evt) => _gamePaused = evt.m_isPaused;

        private static float Slider(ref VCA vca, string path)
        {
            if (!vca.isValid() && RuntimeManager.StudioSystem.getVCA(path, out vca) != RESULT.OK) return 1f;
            return vca.getVolume(out var volume) == RESULT.OK ? Mathf.Clamp01(volume) : 1f;
        }

        private static void Levels()
        {
            var master = Slider(ref _master, "vca:/Master") * Mathf.Clamp01(Level);
            var music = Slider(ref _musicSlider, "vca:/Music");
            var sfx = Slider(ref _sfxSlider, "vca:/SFX");
            var vo = Slider(ref _voSlider, "vca:/VO");

            var step = DuckSpeed * Time.unscaledDeltaTime;
            _duck = Mathf.MoveTowards(_duck, Speaking ? UnderVoice : 1f, step);
            _pauseDuck = Mathf.MoveTowards(_pauseDuck, _gamePaused ? UnderPause : 1f, step);
            Dd1Audio.SetVolume(Dd1Category.Music, master * music * _duck * _pauseDuck);
            Dd1Audio.SetVolume(Dd1Category.Ambience, master * sfx * _duck * _pauseDuck);
            Dd1Audio.SetVolume(Dd1Category.Voice, master * vo);
            Dd1Audio.SetVolume(Dd1Category.Sfx, master * sfx);

            // What the game does to its own master bus (mute without focus, pause when the application is paused) holds for DD1's sound too.
            var muted = false;
            var paused = false;
            if (_masterBus.isValid() || RuntimeManager.StudioSystem.getBus("bus:/", out _masterBus) == RESULT.OK)
            {
                _masterBus.getMute(out muted);
                _masterBus.getPaused(out paused);
            }
            Dd1Audio.SetMuted(muted);
            Dd1Audio.SetPaused(Dd1Category.Music, paused);
            Dd1Audio.SetPaused(Dd1Category.Ambience, paused);
            Dd1Audio.SetPaused(Dd1Category.Voice, paused || _gamePaused);
            Dd1Audio.SetPaused(Dd1Category.Sfx, paused || _gamePaused);
        }

        /// <summary>One of DD1's sounds, once ("ui/town/button_click"). For the hamlet's screens: safe to call at any time, silent when there is no DD1 audio.</summary>
        public static void Ui(string path)
        {
            if (!Enabled || _place == Place.None) return;
            Dd1Audio.Play(path);
        }

        /// <summary>For the test bridge.</summary>
        public static object Describe()
        {
            return new
            {
                enabled = Enabled,
                level = Level,
                place = _place.ToString(),
                dungeon = _dungeon,
                camp = _camp,
                inside = _insideOf,
                music = _music?.Path,
                musicAt = _music?.Seconds ?? 0.0,
                ambience = _ambience?.Path,
                insideAmbience = _inside?.Path,
                voice = _voice?.Path,
                speaking = Speaking,
                spokenUntil = SpokenUntil,
                now = Time.unscaledTime,
                gamePaused = _gamePaused,
                duck = _duck,
                darkness = _place == Place.Dungeon ? Darkness() : 0f,
                dd2 = new { master = Slider(ref _master, "vca:/Master"), music = Slider(ref _musicSlider, "vca:/Music"), sfx = Slider(ref _sfxSlider, "vca:/SFX"), vo = Slider(ref _voSlider, "vca:/VO") }
            };
        }

        /// <summary>For the test bridge: a parameter of what the place plays ("darkness", "inside", "town_event", "play_variance").</summary>
        public static bool SetParameter(string name, float value)
        {
            var found = false;
            foreach (var playing in new[] { _music, _ambience, _inside, _campMusic })
                if (playing != null && playing.SetParameter(name, value)) found = true;
            return found;
        }

        /// <summary>
        /// DD1's town sounds for moments of the hamlet that can be seen from outside the screens that own them:
        /// events those screens raise, and public state watched once a frame. What needs a call from inside a
        /// screen (a button's hover and click, a purchase's own sound) is listed in docs/recon/dd1-audio.md.
        /// </summary>
        private static class Moments
        {
            private static readonly Dictionary<string, string> Doors = new Dictionary<string, string>
            {
                { "stage_coach", "town/enter_coach" }, { "abbey", "town/enter_abbey" }, { "blacksmith", "town/enter_blacksmith" },
                { "graveyard", "town/enter_graveyard" }, { "guild", "town/enter_guild" }, { "sanitarium", "town/enter_sanitarium" },
                { "statue", "town/enter_statue" }, { "tavern", "town/enter_tavern" }
            };

            private const float Every = 0.05f;         // the roster and the purse are not asked every frame
            private static bool _window, _map, _provisions, _notice, _primed;
            private static int _party, _roster, _gold, _week;
            private static float _next;

            public static void Register()
            {
                HamletScene.BuildingClicked += id =>
                {
                    if (Doors.TryGetValue(id ?? "", out var path)) Ui(path);
                };
                UpgradeRules.Built += tree =>
                {
                    var own = "town/building_upgrade_" + tree?.Building;
                    Ui(Dd1Audio.Project != null && Dd1Audio.Project.Knows(own) ? own : "town/building_upgrade");
                };
            }

            /// <summary>The place changed: what is seen now is where the watching starts from.</summary>
            public static void Reset() => _primed = false;

            public static void Watch()
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + Every;
                var hamlet = _place == Place.Hamlet;
                var window = hamlet && RosterWindow.IsOpen;
                var map = hamlet && QuestPanel.IsOpen;
                var provisions = hamlet && ProvisionScreen.IsOpen;
                var notice = hamlet && TownEventPanel.IsOpen;
                var party = hamlet ? EstateSession.PartySize : 0;
                var roster = hamlet ? RosterLifecycle.Count : 0;
                var gold = hamlet ? EstateState.Gold : 0;
                var week = EstateState.Current.Week;
                if (_primed && hamlet && week == _week)
                {
                    if (!window && _window) Ui("ui/town/building_zoomout");
                    if (map && !_map) Ui("ui/town/embark_button");
                    if (provisions && !_provisions) Ui("ui/town/provision_button");
                    if (notice && !_notice) Ui("town/town_event_display_" + (TownEvents.Current?.Def?.Tone ?? "neutral"));
                    if (party > _party) Ui("ui/town/character_add");
                    else if (party < _party && roster >= _roster) Ui("ui/town/character_remove");
                    // A hero more on the roster while the coach is open: the coach's own sound.
                    if (roster > _roster && RosterWindow.Shows(StageCoach.BuildingId)) Ui("town/stage_coach_purchase");
                    // Gold gone while a building's screen is open: DD1's purchase (the coins ring with it). The shop of the
                    // provision screen rings its own purchases, and a trinket's sale is the Trinket Inventory's to sound
                    // (RealmInventoryPanel: /ui/town/sell): neither is heard twice from here.
                    if (gold < _gold && window) Ui("ui/town/buy");
                }
                _window = window;
                _map = map;
                _provisions = provisions;
                _notice = notice;
                _party = party;
                _roster = roster;
                _gold = gold;
                _week = week;
                _primed = true;
            }
        }
    }

    /// <summary>
    /// DD2's music manager, entering the estate's own mode. Left alone it does what is wanted, by accident: a
    /// mode it has no case for makes it log "Unhandled GameModeType" and stop its music
    /// (MusicMgrBhv.OnGameModeEnterStart, the last else, then HandleMusicChange(null) -> Stop). Here it is told
    /// to do exactly that, so that the silence does not hang on a warning path. Fights are not touched: on
    /// COMBAT it starts its own music as in any Kingdoms fight.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Assets.Code.Audio.Music.MusicMgrBhv), nameof(Assets.Code.Audio.Music.MusicMgrBhv.OnGameModeEnterStart))]
    internal static class Dd2MusicStaysOutOfTheEstate
    {
        private static bool Prefix(Assets.Code.Audio.Music.MusicMgrBhv __instance, Assets.Code.Game.GameModeType enteringGameMode)
        {
            if (enteringGameMode != EstateMode.Hub) return true;
            __instance.Stop(saveTimelinePosition: false);
            return false;
        }
    }
}
