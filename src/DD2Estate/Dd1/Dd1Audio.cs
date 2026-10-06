using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FMOD;
using FMODUnity;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// DD1's sounds through DD2's FMOD. DD2's FMOD (2.02) cannot load DD1's banks (Studio 1.x, format 103),
    /// but the sample data inside a bank is a plain FSB5, and FMOD's core API opens one straight out of the
    /// bank file when it is told where it starts and how long it is. So: <see cref="Dd1AudioBank"/> reads
    /// what an event plays, <see cref="Dd1AudioEngine"/> decides when, and this class makes the sound:
    ///
    ///   * a voice is one stream: System.createStream(bank file, CREATESTREAM | NONBLOCKING, exinfo with
    ///     fileoffset, length and initialsubsound), then Sound.getSubSound(index) and System.playSound. A
    ///     stream plays one of its samples at a time, so every voice opens the file for itself; nothing is
    ///     loaded whole (the voice bank is 174 MB).
    ///   * four channel groups of the mod's own (music, voice, ambience, effects) under one root on the
    ///     core's master group; DD2's volume sliders are applied to them by Dd2/EstateAudio.
    ///   * a sample that follows another is started on the sample with Channel.setDelay against its group's
    ///     DSP clock. Each group keeps a clock for the engine that runs with the DSP clock while that moves
    ///     and with real time while it does not (a group nothing plays in may stand still).
    ///
    /// Everything fails soft: without a DD1 install, with a bank missing, on any FMOD error the reason is
    /// logged once and the sound is simply not there. Nothing here throws into the game's update.
    /// </summary>
    internal static class Dd1Audio
    {
        /// <summary>Voices at once, all categories: beyond it a new one-shot is not played.</summary>
        private const int MaxVoices = 48;
        /// <summary>The DSP clock counts as standing still when it has not moved for this long.</summary>
        private const double StallSeconds = 0.1;

        private static readonly HashSet<string> Complaints = new HashSet<string>();
        private static bool _failed;
        private static FMOD.System _core;
        private static ChannelGroup _root;
        private static Voices _voices;
        private static int _rate = 48000;

        public static Dd1AudioProject Project { get; private set; }
        public static Dd1AudioEngine Engine { get; private set; }
        public static string Status { get; private set; } = "not started";
        public static bool Ready => Engine != null;

        private static void Say(string text)
        {
            if (Complaints.Count < 64 && Complaints.Add(text)) Plugin.Log.LogWarning("DD1 audio: " + text);
        }

        private static bool Ok(RESULT result, string what)
        {
            if (result == RESULT.OK) return true;
            Say(what + " failed: " + result);
            return false;
        }

        private const string NoInstall = "no DD1 install";

        /// <summary>
        /// A start that was given up for want of an install is tried again at the next ask (a folder has been
        /// taken into use: Dd1Install.Use). A start FMOD refused stays given up.
        /// </summary>
        public static void AskAgain()
        {
            if (Engine != null || !_failed || Status != NoInstall) return;
            _failed = false;
            Status = "not started";
        }

        /// <summary>Makes ready on first use. False while there is nothing to play with (and why is in <see cref="Status"/>).</summary>
        public static bool Start()
        {
            if (Engine != null) return true;
            if (_failed) return false;
            try
            {
                if (!Dd1Install.Found)
                {
                    Status = NoInstall;
                    _failed = true;
                    return false;
                }
                // The game's own audio comes up during its start: until then, later.
                if (!RuntimeManager.IsInitialized)
                {
                    Status = "waiting for the game's FMOD";
                    return false;
                }
                _core = RuntimeManager.CoreSystem;
                if (!_core.hasHandle())
                {
                    Status = "waiting for the game's FMOD";
                    return false;
                }
                var project = new Dd1AudioProject(Dd1Install.Root, text => Say(text));
                if (!Ok(_core.getSoftwareFormat(out _rate, out _, out _), "getSoftwareFormat") || _rate <= 0) _rate = 48000;
                if (!Ok(_core.createChannelGroup("DD2Estate DD1", out _root), "createChannelGroup") || !Ok(_core.getMasterChannelGroup(out var master), "getMasterChannelGroup") ||
                    !Ok(master.addGroup(_root), "addGroup"))
                {
                    Status = "FMOD refused the mod's channel groups";
                    _failed = true;
                    return false;
                }
                var voices = new Voices();
                foreach (Dd1Category category in Enum.GetValues(typeof(Dd1Category)))
                {
                    if (!Ok(_core.createChannelGroup("DD2Estate DD1 " + category, out var group), "createChannelGroup") || !Ok(_root.addGroup(group), "addGroup"))
                    {
                        Status = "FMOD refused the mod's channel groups";
                        _failed = true;
                        return false;
                    }
                    voices.Clocks[(int)category] = new Clock { Group = group };
                }
                _voices = voices;
                Project = project;
                Engine = new Dd1AudioEngine(project, voices, new Random(), text => Say(text));
                Status = "ready";
                Plugin.Log.LogInfo("DD1 audio: " + project.Ids.Count + " names read from " + project.Root + ", output " + _rate + " Hz");
                return true;
            }
            catch (Exception e)
            {
                Status = "failed: " + e.Message;
                _failed = true;
                Plugin.Log.LogWarning("DD1 audio is off: " + e.Message);
                return false;
            }
        }

        /// <summary>Plays one of DD1's events ("/vo/good/camp_04", "music/mus_town"); null when there is no such event or no audio.</summary>
        public static Dd1Playing Play(string path, IDictionary<string, float> parameters = null)
        {
            if (!Start()) return null;
            try
            {
                var playing = Engine.Play(path, parameters);
                if (playing == null) Say("no event " + path + " in the banks");
                return playing;
            }
            catch (Exception e)
            {
                Say(path + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Plays one sample of a bank by index, outside any event (the test bridge). Returns the voice, 0 for none.</summary>
        public static int PlaySample(string bankName, int fsb, int index, Dd1Category category, float volume)
        {
            if (!Start()) return 0;
            var bank = Project.Load(bankName);
            if (bank == null || fsb < 0 || fsb >= bank.Fsbs.Count) return 0;
            return _voices.Start(bank, new Dd1AudioBank.Wave { Fsb = fsb, Index = index }, category, _voices.Now(category), 0.0, false, false, volume, 0f);
        }

        public static void StopSample(int voice)
        {
            if (_voices != null && voice != 0) _voices.Stop(voice, _voices.Now(Dd1Category.Sfx), 0.1f);
        }

        /// <summary>Once a frame, in real seconds: clocks, timelines, voices.</summary>
        public static void Tick(float seconds)
        {
            if (Engine == null) return;
            try
            {
                if (!RuntimeManager.IsInitialized)
                {
                    // The game took its FMOD down (it does on quitting): everything of ours went with it.
                    Forget();
                    return;
                }
                _voices.Tick(seconds);
                Engine.Advance(seconds);
                _voices.Tend();
            }
            catch (Exception e)
            {
                Say("tick: " + e.Message);
            }
        }

        /// <summary>A category's volume, 0..1 (DD2's sliders, the mod's own ducking).</summary>
        public static void SetVolume(Dd1Category category, float volume)
        {
            var clock = _voices?.Clocks[(int)category];
            if (clock == null || Math.Abs(clock.Volume - volume) < 0.001f) return;
            clock.Volume = volume;
            Ok(clock.Group.setVolume(volume), "setVolume");
        }

        /// <summary>Holds a category where it is: its sounds and its clock (so its timelines) stand still.</summary>
        public static void SetPaused(Dd1Category category, bool paused)
        {
            var clock = _voices?.Clocks[(int)category];
            if (clock == null || clock.Paused == paused) return;
            clock.Paused = paused;
            // Coming back, the DSP clock is read afresh: whatever it did meanwhile is not time that passed for the timelines.
            if (!paused) clock.Known = false;
            Ok(clock.Group.setPaused(paused), "setPaused");
        }

        private static bool _muted;

        public static void SetMuted(bool muted)
        {
            if (_voices == null || _muted == muted) return;
            _muted = muted;
            Ok(_root.setMute(muted), "setMute");
        }

        public static void StopAll(float fade)
        {
            if (Engine == null) return;
            try { Engine.StopAll(fade); }
            catch (Exception e) { Say("stop: " + e.Message); }
        }

        /// <summary>Lets go of everything FMOD holds for the mod (the game is quitting).</summary>
        public static void Shutdown()
        {
            if (_voices == null) return;
            try
            {
                _voices.ReleaseAll();
                foreach (var clock in _voices.Clocks) clock?.Group.release();
                _root.release();
            }
            catch (Exception e) { Say("shutdown: " + e.Message); }
            Forget();
        }

        private static void Forget()
        {
            Engine = null;
            Project = null;
            _voices = null;
            _muted = false;
            Status = "stopped";
        }

        /// <summary>For the test bridge.</summary>
        public static object Describe()
        {
            var voices = new List<object>();
            var clocks = new List<object>();
            var playing = new List<object>();
            if (_voices != null)
            {
                foreach (var voice in _voices.All)
                    voices.Add(new { id = voice.Id, category = voice.Category.ToString(), bank = voice.Bank, sample = voice.Index, stage = voice.Stage.ToString(), volume = voice.Volume, loop = voice.Loop, seconds = voice.Seconds });
                for (var i = 0; i < _voices.Clocks.Length; i++)
                {
                    var clock = _voices.Clocks[i];
                    clocks.Add(new { category = ((Dd1Category)i).ToString(), time = clock.Time, dsp = clock.Dsp, running = clock.Running, paused = clock.Paused, volume = clock.Volume });
                }
            }
            if (Engine != null)
                foreach (var p in Engine.Playing)
                    playing.Add(new { path = p.Path, category = p.Category.ToString(), seconds = p.Seconds, voices = p.VoiceCount, stopping = p.Stopping, mixDb = p.OuterDb });
            return new { status = Status, ready = Ready, rate = _rate, muted = _muted, playing, voices, clocks, complaints = new List<string>(Complaints) };
        }

        // ---- the voices ----

        /// <summary>A category's channel group and the clock the engine reads for it.</summary>
        private sealed class Clock
        {
            public ChannelGroup Group;
            public double Time;
            public ulong Dsp;
            public bool Known, Running, Paused;
            public float Volume = 1f;
            private double _still, _owed;

            public void Tick(float seconds)
            {
                if (Paused) return;
                if (Group.getDSPClock(out var dsp, out _) != RESULT.OK)
                {
                    Running = false;
                    Time += seconds;
                    return;
                }
                if (!Known)
                {
                    Known = true;
                    Dsp = dsp;
                    Time += seconds;
                    return;
                }
                if (dsp > Dsp)
                {
                    // The mixer moved: its count is the time, and what was owed for the wait is in it. After a
                    // standstill the count is only taken up again: how far it went meanwhile says nothing (a
                    // group that wakes may come back with the mixer's time), and real time was counted for it.
                    Time += Running ? (dsp - Dsp) / (double)_rate : seconds;
                    Dsp = dsp;
                    Running = true;
                    _still = _owed = 0;
                    return;
                }
                // Not moved since the last frame: between two mixer blocks, or standing still.
                _still += seconds;
                _owed += seconds;
                if (_still < StallSeconds) return;
                Running = false;
                Time += _owed;
                _owed = 0;
            }

            /// <summary>The DSP clock value of an engine time; only meaningful while <see cref="Running"/>.</summary>
            public ulong At(double time)
            {
                // Rounded: the end of one sample and the start of the next are worked out in different frames
                // and must land on the same count.
                var ahead = Math.Round((time - Time) * _rate);
                return ahead <= 0 ? Dsp : Dsp + (ulong)ahead;
            }
        }

        private enum Stage { Parked, Opening, Seeking, Armed, Playing, Done }

        private sealed class Voice
        {
            public int Id, Index;
            public Dd1Category Category;
            public string Bank, File;
            public uint FileOffset, FileLength;
            public double When, Offset, StopAt = double.PositiveInfinity, Seconds;
            public bool KeepTime, Loop, StopSet;
            public float Volume, Pitch, StopFade;
            public Stage Stage;
            public Sound Parent, Sub;
            public Channel Channel;
            public ulong StartClock;
        }

        private sealed class Voices : IDd1Voices
        {
            public readonly Clock[] Clocks = new Clock[4];
            private readonly Dictionary<int, Voice> _byId = new Dictionary<int, Voice>();
            private readonly List<Voice> _list = new List<Voice>();
            private readonly List<Sound> _closing = new List<Sound>();
            private int _next = 1;

            public IEnumerable<Voice> All => _list;

            public double Now(Dd1Category category) => Clocks[(int)category].Time;

            public void Tick(float seconds)
            {
                foreach (var clock in Clocks) clock.Tick(seconds);
            }

            public int Start(Dd1AudioBank bank, Dd1AudioBank.Wave wave, Dd1Category category, double when, double offset, bool keepTime, bool loop, float volume, float pitch)
            {
                if (wave.Fsb < 0 || wave.Fsb >= bank.Fsbs.Count)
                {
                    Say(bank.Name + " has no sample data for one of its waves");
                    return 0;
                }
                if (_list.Count >= MaxVoices && !loop && !keepTime) return 0;
                var voice = new Voice
                {
                    Id = _next++, Index = wave.Index, Category = category, Bank = bank.Name, File = bank.File,
                    FileOffset = bank.Fsbs[wave.Fsb].Offset, FileLength = bank.Fsbs[wave.Fsb].Length,
                    When = when, Offset = offset, KeepTime = keepTime, Loop = loop, Volume = volume, Pitch = pitch,
                    Stage = Stage.Parked
                };
                _byId[voice.Id] = voice;
                _list.Add(voice);
                // What the mix has silent is not opened until it is turned up: a layer for a parameter the estate never sets costs nothing.
                if (volume > 0f) Open(voice);
                return voice.Id;
            }

            private void Open(Voice voice)
            {
                var info = new CREATESOUNDEXINFO
                {
                    cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO)),
                    fileoffset = voice.FileOffset,
                    length = voice.FileLength,
                    initialsubsound = voice.Index,
                    suggestedsoundtype = SOUND_TYPE.FSB
                };
                var mode = MODE.CREATESTREAM | MODE.NONBLOCKING | MODE._2D | MODE.IGNORETAGS | (voice.Loop ? MODE.LOOP_NORMAL : MODE.LOOP_OFF);
                var result = _core.createStream(voice.File, mode, ref info, out voice.Parent);
                if (result != RESULT.OK)
                {
                    Say("opening " + voice.Bank + " (" + result + ")");
                    voice.Stage = Stage.Done;
                    return;
                }
                voice.Stage = Stage.Opening;
            }

            public void Volume(int id, float volume)
            {
                if (!_byId.TryGetValue(id, out var voice) || voice.Stage == Stage.Done) return;
                voice.Volume = volume;
                if (voice.Stage == Stage.Parked)
                {
                    if (volume > 0f) Open(voice);
                }
                else if (voice.Stage == Stage.Playing) voice.Channel.setVolume(volume);
            }

            public void Stop(int id, double when, float fade)
            {
                if (!_byId.TryGetValue(id, out var voice) || voice.Stage == Stage.Done) return;
                if (when >= voice.StopAt) return;       // it ends sooner already
                voice.StopAt = when;
                voice.StopFade = fade;
                voice.StopSet = false;
            }

            public bool Alive(int id) => _byId.TryGetValue(id, out var voice) && voice.Stage != Stage.Done;

            /// <summary>Once a frame, after the engine: moves every voice along (opened, started, stopped, over).</summary>
            public void Tend()
            {
                for (var i = _list.Count - 1; i >= 0; i--)
                {
                    var voice = _list[i];
                    try { Step(voice); }
                    catch (Exception e)
                    {
                        Say("voice: " + e.Message);
                        voice.Stage = Stage.Done;
                    }
                    if (voice.Stage != Stage.Done) continue;
                    Close(voice);
                    _list.RemoveAt(i);
                    _byId.Remove(voice.Id);
                }
                // A sound that was still being opened when it was let go can only be released once it is open.
                for (var i = _closing.Count - 1; i >= 0; i--)
                {
                    var sound = _closing[i];
                    var state = sound.getOpenState(out var open, out _, out _, out _);
                    if (state == RESULT.OK && (open == OPENSTATE.LOADING || open == OPENSTATE.SEEKING || open == OPENSTATE.CONNECTING || open == OPENSTATE.BUFFERING)) continue;
                    sound.release();
                    _closing.RemoveAt(i);
                }
            }

            private void Step(Voice voice)
            {
                var clock = Clocks[(int)voice.Category];
                var now = clock.Time;
                // Told to end before it ever sounded.
                if (voice.Stage != Stage.Playing && now >= voice.StopAt)
                {
                    voice.Stage = Stage.Done;
                    return;
                }
                switch (voice.Stage)
                {
                    case Stage.Parked:
                        // A piece of the timeline that stayed silent to its end.
                        if (!voice.Loop && voice.KeepTime && voice.Seconds > 0 && now > voice.When + voice.Seconds) voice.Stage = Stage.Done;
                        break;
                    case Stage.Opening:
                    {
                        if (!Opened(voice, voice.Parent, "opening")) break;
                        if (!Check(voice, voice.Parent.getSubSound(voice.Index, out voice.Sub), "getSubSound")) break;
                        voice.Stage = Stage.Seeking;
                        break;
                    }
                    case Stage.Seeking:
                    {
                        if (!Opened(voice, voice.Sub, "seeking")) break;
                        if (voice.Sub.getLength(out var ms, TIMEUNIT.MS) == RESULT.OK) voice.Seconds = ms / 1000.0;
                        voice.Stage = Stage.Armed;
                        Arm(voice, clock, now);
                        break;
                    }
                    case Stage.Armed:
                        Arm(voice, clock, now);
                        break;
                    case Stage.Playing:
                    {
                        var result = voice.Channel.isPlaying(out var playing);
                        if (result != RESULT.OK || !playing)
                        {
                            // INVALID_HANDLE and CHANNEL_STOLEN are how FMOD says a channel has ended.
                            voice.Stage = Stage.Done;
                            break;
                        }
                        if (!voice.StopSet && !double.IsPositiveInfinity(voice.StopAt)) End(voice, clock, now);
                        break;
                    }
                }
            }

            // True when the sound is open. An error ends the voice (and is said once).
            private bool Opened(Voice voice, Sound sound, string what)
            {
                var result = sound.getOpenState(out var state, out _, out _, out _);
                if (result != RESULT.OK || state == OPENSTATE.ERROR)
                {
                    Say(what + " sample " + voice.Index + " of " + voice.Bank + " (" + (result != RESULT.OK ? result.ToString() : "open state ERROR: FMOD does not play this FSB5") + ")");
                    voice.Stage = Stage.Done;
                    return false;
                }
                return state == OPENSTATE.READY || state == OPENSTATE.PLAYING;
            }

            private bool Check(Voice voice, RESULT result, string what)
            {
                if (result == RESULT.OK) return true;
                Say(what + " on sample " + voice.Index + " of " + voice.Bank + " (" + result + ")");
                voice.Stage = Stage.Done;
                return false;
            }

            // The sound is open: start it now if its time has come, hand it to the mixer with its starting
            // time if the group's clock runs, or wait for the time when the clock stands still.
            private void Arm(Voice voice, Clock clock, double now)
            {
                var late = now - voice.When;
                var ahead = late < 0;
                if (ahead && !clock.Running) return;

                var position = voice.Offset;
                if (!ahead && voice.KeepTime) position += late;
                if (voice.Seconds > 0 && position >= voice.Seconds)
                {
                    if (!voice.Loop)
                    {
                        voice.Stage = Stage.Done;       // its place on the timeline has gone by
                        return;
                    }
                    position %= voice.Seconds;
                }
                if (!Check(voice, _core.playSound(voice.Sub, clock.Group, true, out voice.Channel), "playSound")) return;
                voice.Channel.setVolume(voice.Volume);
                if (voice.Pitch != 0f) voice.Channel.setPitch((float)Math.Pow(2.0, voice.Pitch / 12.0));
                if (position > 0.001) voice.Channel.setPosition((uint)(position * 1000.0), TIMEUNIT.MS);
                voice.StartClock = 0;
                if (ahead)
                {
                    voice.StartClock = clock.At(voice.When);
                    voice.Channel.setDelay(voice.StartClock, 0, false);
                }
                voice.Stage = Stage.Playing;
                if (!double.IsPositiveInfinity(voice.StopAt)) End(voice, clock, now);
                voice.Channel.setPaused(false);
            }

            // Gives the channel its end: on the sample when the clock runs, else by hand when the time is there.
            private void End(Voice voice, Clock clock, double now)
            {
                if (!clock.Running && !clock.Paused)
                {
                    if (now < voice.StopAt) return;
                    voice.Channel.stop();
                    voice.Stage = Stage.Done;
                    return;
                }
                var end = clock.At(voice.StopAt);
                voice.Channel.removeFadePoints(0, ulong.MaxValue);
                if (voice.StopFade > 0f)
                {
                    var fade = (ulong)(voice.StopFade * _rate);
                    var from = end > clock.Dsp + fade ? end - fade : clock.Dsp;
                    voice.Channel.addFadePoint(from, 1f);
                    voice.Channel.addFadePoint(Math.Max(end, from + 1), 0f);
                }
                voice.Channel.setDelay(voice.StartClock, Math.Max(end, clock.Dsp + 1), true);
                voice.StopSet = true;
            }

            private void Close(Voice voice)
            {
                if (voice.Channel.hasHandle()) voice.Channel.stop();
                voice.Channel.clearHandle();
                if (!voice.Parent.hasHandle()) return;
                // Releasing a sound that is still loading blocks until it is loaded: it is put aside instead.
                _closing.Add(voice.Parent);
                voice.Parent.clearHandle();
                voice.Sub.clearHandle();
            }

            public void ReleaseAll()
            {
                foreach (var voice in _list)
                {
                    if (voice.Channel.hasHandle()) voice.Channel.stop();
                    if (voice.Parent.hasHandle()) voice.Parent.release();
                }
                foreach (var sound in _closing) sound.release();
                _list.Clear();
                _byId.Clear();
                _closing.Clear();
            }
        }
    }
}
