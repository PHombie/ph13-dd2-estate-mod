using System;
using System.Collections.Generic;

namespace DD2Estate.Dd1
{
    /// <summary>What the engine asks of whoever makes the sound (FMOD in the game, a list in the offline check).</summary>
    internal interface IDd1Voices
    {
        /// <summary>The clock of a category, seconds. It stands still while the category is paused.</summary>
        double Now(Dd1Category category);

        /// <summary>
        /// Plays a sample at <paramref name="when"/> on the category's clock (in the past: at once), starting
        /// <paramref name="offset"/> seconds in. <paramref name="keepTime"/>: a late start skips what was
        /// missed, so the sample stays where the timeline has it. Returns a handle, 0 when nothing will sound.
        /// </summary>
        int Start(Dd1AudioBank bank, Dd1AudioBank.Wave wave, Dd1Category category, double when, double offset, bool keepTime, bool loop, float volume, float pitch);

        void Volume(int voice, float volume);

        /// <summary>Ends a voice at <paramref name="when"/>, fading over the last <paramref name="fade"/> seconds before it (or from now, if that is later).</summary>
        void Stop(int voice, double when, float fade);

        bool Alive(int voice);
    }

    /// <summary>
    /// Plays DD1's events the way their banks describe them, without FMOD Studio: DD2's FMOD cannot load the
    /// banks, but it can play their samples, so the part of Studio the estate's sounds rely on is done here.
    ///
    ///   * A timeline with a cursor (48000 positions a second). Instruments cut to the timeline start where
    ///     the cursor enters their box and end where it leaves; the others start when it enters and play on
    ///     (a looping one until it leaves).
    ///   * Transitions: at their position, in priority order, the first whose parameter conditions hold and
    ///     whose chance comes up moves the cursor to its marker. The town's music is built of these.
    ///   * Pools (one entry by weight, or in order), scatterers (a pool spawned again after a random pause
    ///     while the cursor is in the box), events inside events, and snapshots (they move mixer faders).
    ///   * Volume: the dB of the instrument, its pool, its audio tracks, the event's master track and the
    ///     mixer groups down to the master bus, added up. Automation curves replace a fader's value (several
    ///     on one fader add up), driven by a parameter or by the cursor.
    ///
    /// Left out, because nothing the estate plays needs it or because it cannot be judged without hearing it:
    /// effects (reverbs, filters, compressors), sends, random volume and pitch modulators, automation of
    /// anything but volume and snapshot intensity, the shape of automation curves (taken as straight lines),
    /// instruments placed on a parameter instead of the timeline, tempo and quantised transitions.
    ///
    /// The timeline is acted on a little ahead of the clock (<see cref="Lookahead"/>): a sample that follows
    /// another is opened early and handed over with its exact starting time.
    ///
    /// No Unity type is used here (see <see cref="Dd1AudioBank"/>).
    /// </summary>
    internal sealed class Dd1AudioEngine
    {
        public const double Rate = Dd1AudioBank.TimelineRate;
        /// <summary>How far ahead of the clock the timeline is acted on, seconds.</summary>
        public const double Lookahead = 0.6;
        /// <summary>A scatterer's next sound is asked for this early, so that its file is open in time.</summary>
        public const double SpawnAhead = 0.15;
        /// <summary>A looping instrument left and entered again within this time (a loop region a few samples longer than its box) plays on.</summary>
        public const double Grace = 0.1;
        /// <summary>
        /// Fades, seconds. An instrument cut by the timeline ends on the sample, without one: the pieces of
        /// the town's music are cut from one take and follow each other at exactly that place. A looping bed
        /// that is left fades; an event that gives way to a newer one of its kind goes quickly.
        /// </summary>
        public const float CutFade = 0f, LeaveFade = 0.5f, StealFade = 0.05f;
        /// <summary>
        /// An event with pieces cut to its timeline begins this much after it is asked for: its first files are
        /// open by then and start on the sample, instead of late with their beginnings skipped.
        /// </summary>
        public const double Preroll = 0.15;
        /// <summary>A fader at or below this is silence (DD1's curves bottom out at -42).</summary>
        public const float SilenceDb = -41.9f;

        private readonly List<Dd1Playing> _playing = new List<Dd1Playing>();
        private readonly List<InForce> _snapshots = new List<InForce>();

        public Dd1AudioProject Project { get; }
        public IDd1Voices Voices { get; }
        public Random Random { get; }
        public Action<string> Log { get; }

        private sealed class InForce
        {
            public object Owner;
            public Dd1AudioBank.SnapshotValue[] Values;
            public float Intensity;
        }

        public Dd1AudioEngine(Dd1AudioProject project, IDd1Voices voices, Random random, Action<string> log)
        {
            Project = project;
            Voices = voices;
            Random = random ?? new Random();
            Log = log ?? (text => { });
        }

        public IReadOnlyList<Dd1Playing> Playing => _playing;

        /// <summary>Starts an event; null when DD1 has none of that path or it holds nothing to play.</summary>
        public Dd1Playing Play(string path, IDictionary<string, float> parameters = null)
        {
            if (!Project.TryFind(path, out var bank, out var e) || e.Snapshot != Guid.Empty) return null;
            if (!bank.Timelines.ContainsKey(e.Timeline)) return null;
            var category = Project.Category(bank, e);
            // DD1 caps how many of an event sound at once (a button's hover: 3); the oldest gives way.
            if (e.MaxInstances > 0 && e.MaxInstances < int.MaxValue)
            {
                var same = 0;
                Dd1Playing oldest = null;
                foreach (var other in _playing)
                {
                    if (other.Def != e || other.Stopping) continue;
                    same++;
                    if (oldest == null) oldest = other;
                }
                if (same >= e.MaxInstances) oldest?.Stop(StealFade);
            }
            var line = bank.Timelines[e.Timeline];
            var sequenced = line.Locked.Count > 0 && line.Locked.Count + line.Async.Count + line.Transitions.Count > 1;
            var playing = new Dd1Playing(this, bank, e, category, null, null, Voices.Now(category) + (sequenced ? Preroll : 0.0), Dd1AudioProject.FullPath(path));
            if (parameters != null)
                foreach (var pair in parameters) playing.SetParameter(pair.Key, pair.Value, true);
            _playing.Add(playing);
            playing.Advance(0f);
            return playing;
        }

        /// <summary>Once a frame: moves every timeline on and forgets the events that have ended.</summary>
        public void Advance(float seconds)
        {
            for (var i = 0; i < _playing.Count; i++)
            {
                try { _playing[i].Advance(seconds); }
                catch (Exception e)
                {
                    Log("DD1 audio: " + _playing[i].Path + " failed and is stopped: " + e.Message);
                    try { _playing[i].Stop(0f); } catch { }
                    _playing[i].Abandon();
                }
            }
            _playing.RemoveAll(p => p.Finished);
        }

        public void StopAll(float fade)
        {
            foreach (var playing in _playing) playing.Stop(fade);
        }

        // ---- the mixer ----

        internal void Snapshot(object owner, Dd1AudioBank.SnapshotValue[] values, float intensity)
        {
            foreach (var force in _snapshots)
            {
                if (force.Owner != owner) continue;
                force.Intensity = intensity;
                return;
            }
            _snapshots.Add(new InForce { Owner = owner, Values = values, Intensity = intensity });
        }

        internal void Release(object owner) => _snapshots.RemoveAll(force => force.Owner == owner);

        /// <summary>A mixer fader as the snapshots in force leave it, dB.</summary>
        public float FaderDb(Dd1AudioBank.Bus bus)
        {
            var db = bus.Volume;
            foreach (var force in _snapshots)
            {
                foreach (var value in force.Values)
                    if (value.Property == 0 && value.Target == bus.Id) db += (value.Value - db) * Clamp01(force.Intensity);
            }
            return db;
        }

        internal static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        public static float Linear(float db) => db <= SilenceDb ? 0f : (float)Math.Pow(10.0, db / 20.0);
    }

    /// <summary>An event that is playing: its cursor, what sounds, its parameters.</summary>
    internal sealed class Dd1Playing
    {
        private sealed class ParamState
        {
            public Dd1AudioBank.Param Def;
            public float Value, Target;
        }

        private struct Auto
        {
            public Dd1AudioBank.Point[] Curve;
            /// <summary>Null: the cursor drives it.</summary>
            public ParamState Driver;
        }

        private sealed class Sounding
        {
            public int Voice;
            /// <summary>The sample's instrument first, then the pools around it, the one standing on the timeline last.</summary>
            public Dd1AudioBank.Instrument[] Chain;
            public float Volume = -1f;
        }

        private sealed class BoxState
        {
            public Dd1AudioBank.Box Box;
            public Dd1AudioBank.Instrument Top;
            public bool Locked;
            public double EnteredAt;
            /// <summary>When the cursor left (or will leave) the box; infinity while it is inside.</summary>
            public double LeftAt = double.PositiveInfinity;
            public readonly List<Sounding> Voices = new List<Sounding>();
            public double NextSpawn;
            public int Spawned;
            public Dd1Playing Child;
            public Dd1AudioBank.SnapshotValue[] Snapshot;
            public bool Left => !double.IsPositiveInfinity(LeftAt);
        }

        /// <summary>From this time on the cursor is at Pos and moves on at the timeline's rate.</summary>
        private struct Segment
        {
            public double From, Pos;
        }

        private readonly Dd1AudioEngine _engine;
        private readonly Dd1Playing _parent;
        private readonly Dd1AudioBank.Instrument _parentInstrument;
        private readonly Dd1AudioBank.Timeline _line;
        private readonly List<Dd1AudioBank.Transition> _transitions;
        private readonly Dictionary<string, ParamState> _params = new Dictionary<string, ParamState>(StringComparer.Ordinal);
        private readonly Dictionary<Guid, List<Auto>> _volumes = new Dictionary<Guid, List<Auto>>();
        private readonly Dictionary<Guid, List<Auto>> _intensities = new Dictionary<Guid, List<Auto>>();
        private readonly Dictionary<Guid, int> _turns = new Dictionary<Guid, int>();
        private readonly List<Segment> _segments = new List<Segment>();
        private readonly List<BoxState> _boxes = new List<BoxState>();
        private readonly List<Sounding> _tails = new List<Sounding>();
        private readonly List<Dd1Playing> _released = new List<Dd1Playing>();
        private readonly double _lastEdge;
        private readonly bool _single;
        private double _scanPos;
        private double _judgedAt = -1;
        private double _releaseAt = double.PositiveInfinity;
        private bool _stopped;
        private float _outerDb;
        private float _level = 1f, _levelTarget = 1f, _levelSpeed;
        private int _jumps;

        public Dd1AudioBank Bank { get; }
        public Dd1AudioBank.Event Def { get; }
        public Dd1Category Category { get; }
        public string Path { get; }
        /// <summary>Stop was called, or the event's place in the event that holds it was left: nothing new begins.</summary>
        public bool Stopping => _stopped;
        public bool Finished { get; private set; }

        internal Dd1Playing(Dd1AudioEngine engine, Dd1AudioBank bank, Dd1AudioBank.Event def, Dd1Category category, Dd1Playing parent,
            Dd1AudioBank.Instrument parentInstrument, double startAt, string path)
        {
            _engine = engine;
            _parent = parent;
            _parentInstrument = parentInstrument;
            Bank = bank;
            Def = def;
            Category = category;
            Path = path ?? "";
            bank.Timelines.TryGetValue(def.Timeline, out _line);
            _line = _line ?? new Dd1AudioBank.Timeline();
            _segments.Add(new Segment { From = startAt, Pos = 0 });

            _transitions = new List<Dd1AudioBank.Transition>(_line.Transitions);
            _transitions.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Priority.CompareTo(b.Priority));
            foreach (var box in _line.Async) _lastEdge = Math.Max(_lastEdge, box.End);
            foreach (var box in _line.Locked) _lastEdge = Math.Max(_lastEdge, box.End);
            foreach (var t in _transitions) _lastEdge = Math.Max(_lastEdge, t.Start);

            // One sample and nothing else (a line of the Ancestor's, a click): it is played whole, from its
            // beginning, as soon as its file is open; there is no timeline to keep it in step with.
            _single = _transitions.Count == 0 && _line.Async.Count + _line.Locked.Count == 1;

            foreach (var id in _line.Controllers) Automate(id, null);
            foreach (var id in def.Layouts)
            {
                if (!bank.Layouts.TryGetValue(id, out var layout) || !bank.Params.TryGetValue(layout.Param, out var param)) continue;
                if (!_params.TryGetValue(param.Name, out var state))
                    _params[param.Name] = state = new ParamState { Def = param, Value = param.Min, Target = param.Min };
                foreach (var controller in layout.Controllers) Automate(controller, state);
            }
        }

        private void Automate(Guid controllerId, ParamState driver)
        {
            if (!Bank.Controllers.TryGetValue(controllerId, out var controller)) return;
            if (!Bank.Curves.TryGetValue(controller.Curve, out var curve) || curve.Length == 0) return;
            var table = controller.Property == 0 ? _volumes : controller.Property == 3 ? _intensities : null;
            if (table == null) return;
            if (!table.TryGetValue(controller.Target, out var list)) table[controller.Target] = list = new List<Auto>();
            list.Add(new Auto { Curve = curve, Driver = driver });
        }

        // ---- parameters ----

        /// <summary>Sets a parameter of the event (and of the events inside it). It moves there at DD1's own pace unless <paramref name="atOnce"/>.</summary>
        public bool SetParameter(string name, float value, bool atOnce = false)
        {
            var found = false;
            if (_params.TryGetValue(name, out var state))
            {
                state.Target = Math.Max(state.Def.Min, Math.Min(state.Def.Max, value));
                if (atOnce || state.Def.Seek <= 0f) state.Value = state.Target;
                found = true;
            }
            foreach (var box in _boxes)
                if (box.Child != null && box.Child.SetParameter(name, value, atOnce)) found = true;
            return found;
        }

        public float Parameter(string name) => _params.TryGetValue(name, out var state) ? state.Value : 0f;

        public IEnumerable<string> ParameterNames => _params.Keys;

        /// <summary>
        /// Turns the whole event up or down (1 is DD1's own level) over <paramref name="seconds"/>, without
        /// touching its cursor: the town's ambience steps back while a building is open and is there again,
        /// where it was, when the door closes.
        /// </summary>
        public void FadeTo(float level, float seconds)
        {
            _levelTarget = Dd1AudioEngine.Clamp01(level);
            _levelSpeed = seconds <= 0f ? float.MaxValue : 1f / seconds;
            if (seconds <= 0f) _level = _levelTarget;
        }

        // ---- time ----

        private double TimeAt(double pos)
        {
            var last = _segments[_segments.Count - 1];
            return last.From + (pos - last.Pos) / Dd1AudioEngine.Rate;
        }

        private double ScanPosAt(double time)
        {
            var last = _segments[_segments.Count - 1];
            return last.Pos + (time - last.From) * Dd1AudioEngine.Rate;
        }

        /// <summary>Where the cursor is at <paramref name="time"/>: the timeline may already have been acted on beyond a jump that is still to come.</summary>
        public double PositionAt(double time)
        {
            var at = _segments[0];
            for (var i = 1; i < _segments.Count && _segments[i].From <= time; i++) at = _segments[i];
            return at.Pos + Math.Max(0.0, time - at.From) * Dd1AudioEngine.Rate;
        }

        // ---- the frame ----

        internal void Advance(float seconds)
        {
            if (Finished) return;
            var now = _engine.Voices.Now(Category);
            foreach (var state in _params.Values)
            {
                if (state.Value == state.Target) continue;
                var step = state.Def.Seek * seconds;
                state.Value = state.Def.Seek <= 0f || Math.Abs(state.Target - state.Value) <= step ? state.Target : state.Value + Math.Sign(state.Target - state.Value) * step;
            }
            if (_level != _levelTarget)
            {
                var step = _levelSpeed * seconds;
                _level = Math.Abs(_levelTarget - _level) <= step ? _levelTarget : _level + Math.Sign(_levelTarget - _level) * step;
            }
            while (_segments.Count > 1 && _segments[1].From <= now) _segments.RemoveAt(0);

            _outerDb = Outer(now);
            if (!_stopped) Scan(Math.Min(now + Dd1AudioEngine.Lookahead, _releaseAt));
            if (!_stopped && now + Dd1AudioEngine.Lookahead >= _releaseAt) Release(_releaseAt);
            Service(now);
        }

        // Acts on the timeline up to a time: boxes entered and left, transitions judged.
        private void Scan(double horizon)
        {
            for (_jumps = 0; _jumps < 64;)
            {
                var horizonPos = ScanPosAt(horizon);
                if (horizonPos < _scanPos) return;
                EnterAt(_scanPos);

                // The next place where the cursor may be sent elsewhere.
                var next = double.PositiveInfinity;
                foreach (var t in _transitions)
                {
                    if (t.Start < _scanPos || (t.Start == _scanPos && _judgedAt == _scanPos)) continue;
                    next = t.Start;
                    break;
                }
                var to = Math.Min(horizonPos, next);
                foreach (var box in _line.Async) if (box.Start > _scanPos && box.Start < to) Enter(box, false, box.Start);
                foreach (var box in _line.Locked) if (box.Start > _scanPos && box.Start < to) Enter(box, true, box.Start);
                // A box that ends exactly where a transition stands is not left before the transition is judged:
                // a loop region as long as its boxes never lets the cursor out of them.
                foreach (var state in _boxes)
                    if (!state.Left && (state.Box.End < to || (state.Box.End == to && to != next))) Leave(state, TimeAt(state.Box.End));
                _scanPos = to;
                if (to != next) return;                 // the horizon: the rest is for a later frame

                _judgedAt = next;
                var destination = Judge(next);
                var when = TimeAt(next);
                if (destination < 0)
                {
                    foreach (var state in _boxes)
                        if (!state.Left && state.Box.End <= next) Leave(state, when);
                    continue;
                }
                // The jump: what the cursor is taken out of ends there; what it stays in plays on.
                foreach (var state in _boxes)
                {
                    if (state.Left) continue;
                    var stays = !state.Locked && state.Box.Start <= destination && destination < state.Box.End;
                    if (!stays) Leave(state, when);
                }
                _segments.Add(new Segment { From = when, Pos = destination });
                _scanPos = destination;
                _judgedAt = -1;
                _jumps++;
            }
        }

        // The first transition at this position that applies, in DD1's priority order; -1 for none.
        private double Judge(double position)
        {
            foreach (var t in _transitions)
            {
                if (t.Start != position) continue;
                var holds = true;
                foreach (var condition in t.Conditions)
                {
                    var value = Bank.Params.TryGetValue(condition.Param, out var param) ? Parameter(param.Name) : 0f;
                    if (value < condition.Min - 0.0001f || value > condition.Max + 0.0001f) holds = false;
                }
                if (!holds) continue;
                if (t.Chance < 100f && _engine.Random.NextDouble() * 100.0 >= t.Chance) continue;
                foreach (var marker in _line.Markers)
                    if (marker.Id == t.Destination) return marker.Position;
            }
            return -1;
        }

        private void EnterAt(double position)
        {
            foreach (var box in _line.Async) if (box.Start <= position && position < box.End) Enter(box, false, position);
            foreach (var box in _line.Locked) if (box.Start <= position && position < box.End) Enter(box, true, position);
        }

        private void Enter(Dd1AudioBank.Box box, bool locked, double position)
        {
            locked = locked && !_single;
            BoxState lingering = null;
            foreach (var state in _boxes)
            {
                if (state.Box.Instrument != box.Instrument || state.Box.Start != box.Start || state.Locked != locked) continue;
                if (!state.Left) return;                // the cursor is in it already
                lingering = state;
            }
            if (!Bank.Instruments.TryGetValue(box.Instrument, out var top)) return;
            var when = TimeAt(position);
            // A looping bed whose box ended a moment ago (its loop region is a few samples longer): it plays on.
            if (lingering != null && !locked && IsBed(top) && when - lingering.LeftAt <= Dd1AudioEngine.Grace && AnyAlive(lingering))
            {
                lingering.LeftAt = double.PositiveInfinity;
                return;
            }

            var entered = new BoxState { Box = box, Top = top, Locked = locked, EnteredAt = when, NextSpawn = when };
            _boxes.Add(entered);
            var offset = locked ? (position - box.Start) / Dd1AudioEngine.Rate : 0.0;
            switch (top.Kind)
            {
                case Dd1AudioBank.InstrumentKind.Wave:
                case Dd1AudioBank.InstrumentKind.Multi:
                    Sound(entered, when, offset);
                    break;
                case Dd1AudioBank.InstrumentKind.Event:
                    Begin(entered, when);
                    break;
            }
        }

        // One sample of the box's instrument: the wave itself, or the pool's pick.
        private void Sound(BoxState state, double when, double offset)
        {
            var chain = new List<Dd1AudioBank.Instrument>();
            var leaf = Pick(state.Top, chain, 0);
            if (leaf == null || !Bank.Waves.TryGetValue(leaf.Target, out var wave)) return;
            chain.Reverse();
            var sounding = new Sounding { Chain = chain.ToArray() };
            var pitch = 0f;
            foreach (var instrument in sounding.Chain) pitch += instrument.Pitch;
            var volume = Dd1AudioEngine.Linear(Db(sounding));
            var loop = leaf.Loop && state.Top.Kind == Dd1AudioBank.InstrumentKind.Wave;
            // A sound struck once is not struck at all where the mix has it silent (DD1 left whole scatterers in
            // its dungeons on muted tracks); a loop or a piece of the timeline is started all the same, it may come up.
            if (volume <= 0f && !loop && !state.Locked) return;
            sounding.Volume = volume;
            sounding.Voice = _engine.Voices.Start(Bank, wave, Category, when, offset, state.Locked, loop, volume, pitch);
            if (sounding.Voice == 0) return;
            state.Voices.Add(sounding);
            // A sample that was asked for after its box was already left (a scatterer's last) is on its own.
            if (state.Locked && state.Left) _engine.Voices.Stop(sounding.Voice, state.LeftAt, Dd1AudioEngine.CutFade);
        }

        // Follows pools down to a wave. chain: the instruments passed, the outermost first.
        private Dd1AudioBank.Instrument Pick(Dd1AudioBank.Instrument instrument, List<Dd1AudioBank.Instrument> chain, int depth)
        {
            chain.Add(instrument);
            if (instrument.Kind == Dd1AudioBank.InstrumentKind.Wave) return instrument;
            if (instrument.Kind == Dd1AudioBank.InstrumentKind.Event || instrument.Entries.Count == 0 || depth > 8) return null;
            Dd1AudioBank.Entry entry;
            if (instrument.Kind == Dd1AudioBank.InstrumentKind.Multi && instrument.Mode == 0)
            {
                // In order, one after another.
                _turns.TryGetValue(instrument.Id, out var turn);
                entry = instrument.Entries[turn % instrument.Entries.Count];
                _turns[instrument.Id] = turn + 1;
            }
            else
            {
                var total = 0f;
                foreach (var e in instrument.Entries) total += Math.Max(0f, e.Weight);
                var roll = _engine.Random.NextDouble() * total;
                entry = instrument.Entries[instrument.Entries.Count - 1];
                foreach (var e in instrument.Entries)
                {
                    roll -= Math.Max(0f, e.Weight);
                    if (roll >= 0) continue;
                    entry = e;
                    break;
                }
            }
            return Bank.Instruments.TryGetValue(entry.Instrument, out var next) ? Pick(next, chain, depth + 1) : null;
        }

        // An event inside this one: a snapshot takes hold of the mixer, a sound event plays as a child.
        private void Begin(BoxState state, double when)
        {
            if (!_engine.Project.TryFind(state.Top.Target, null, Bank, out var bank, out var def)) return;
            if (def.Snapshot != Guid.Empty)
            {
                state.Snapshot = _engine.Project.Snapshot(bank, def.Snapshot);
                return;
            }
            if (Depth() > 4 || !bank.Timelines.ContainsKey(def.Timeline)) return;
            state.Child = new Dd1Playing(_engine, bank, def, Category, this, state.Top, when, null);
            foreach (var param in _params)
                state.Child.SetParameter(param.Key, param.Value.Target, true);
        }

        private int Depth() => _parent == null ? 0 : _parent.Depth() + 1;

        // What plays for as long as the cursor is in its box: a looping sample, or a pool that goes round.
        private static bool IsBed(Dd1AudioBank.Instrument instrument)
        {
            return instrument.Loop && (instrument.Kind == Dd1AudioBank.InstrumentKind.Wave || instrument.Kind == Dd1AudioBank.InstrumentKind.Multi);
        }

        private void Leave(BoxState state, double when)
        {
            if (state.Left) return;
            state.LeftAt = when;
            if (state.Locked)
                foreach (var sounding in state.Voices) _engine.Voices.Stop(sounding.Voice, when, Dd1AudioEngine.CutFade);
            if (state.Child != null) state.Child._releaseAt = Math.Min(state.Child._releaseAt, when);
        }

        // The event's place in its parent was left: the cursor stops, loops fade, what was struck rings out.
        private void Release(double when)
        {
            if (_stopped) return;
            _stopped = true;
            foreach (var state in _boxes) Leave(state, Math.Min(when, state.LeftAt));
        }

        /// <summary>Ends the event: everything that sounds fades over <paramref name="fade"/> seconds.</summary>
        public void Stop(float fade)
        {
            var now = _engine.Voices.Now(Category);
            _stopped = true;
            foreach (var state in _boxes)
            {
                if (!state.Left) state.LeftAt = now;
                foreach (var sounding in state.Voices) _engine.Voices.Stop(sounding.Voice, now + fade, fade);
                // They have their end now: nothing that tidies the boxes up is to give them another.
                _tails.AddRange(state.Voices);
                state.Voices.Clear();
                state.Child?.Stop(fade);
                if (state.Snapshot != null) _engine.Release(state);
            }
            foreach (var sounding in _tails) _engine.Voices.Stop(sounding.Voice, now + fade, fade);
            foreach (var child in _released) child.Stop(fade);
        }

        internal void Abandon()
        {
            foreach (var state in _boxes) _engine.Release(state);
            Finished = true;
        }

        // What needs looking after every frame: scatterers, pools that go round, children, faders, the dead.
        private void Service(double now)
        {
            var scanned = TimeAt(_scanPos);
            for (var i = _boxes.Count - 1; i >= 0; i--)
            {
                var state = _boxes[i];
                var inside = now < state.LeftAt;

                if (state.Top.Kind == Dd1AudioBank.InstrumentKind.Scatter && !_stopped)
                {
                    var room = state.Top.Polyphony == 0 ? int.MaxValue : (int)state.Top.Polyphony;
                    var spawns = 0;
                    while (state.NextSpawn <= now + Dd1AudioEngine.SpawnAhead && state.NextSpawn < state.LeftAt && spawns++ < 8 &&
                           (state.Top.SpawnTotal == 0 || state.Spawned < state.Top.SpawnTotal))
                    {
                        if (CountAlive(state) < room) Sound(state, state.NextSpawn, 0.0);
                        state.Spawned++;
                        var pause = state.Top.IntervalMin + (state.Top.IntervalMax - state.Top.IntervalMin) * (float)_engine.Random.NextDouble();
                        state.NextSpawn += Math.Max(0.02f, pause);
                    }
                }
                else if (state.Top.Kind == Dd1AudioBank.InstrumentKind.Multi && state.Top.Loop && !state.Locked && inside && !_stopped && state.Voices.Count > 0 && !AnyAlive(state))
                {
                    // A pool set to loop: when its pick has run out, the next one.
                    state.Voices.Clear();
                    Sound(state, now, 0.0);
                }

                if (state.Snapshot != null)
                {
                    if (state.EnteredAt <= now && inside && !_stopped) _engine.Snapshot(state, state.Snapshot, Automated(_intensities, state.Top.Id, 1f, now));
                    else if (!inside || _stopped) _engine.Release(state);
                }
                if (state.Child != null)
                {
                    state.Child.Advance(0f);
                    foreach (var param in _params) state.Child.Follow(param.Key, param.Value.Value);
                    if (state.Child.Finished && state.Top.Loop && inside && !_stopped)
                    {
                        // An event set to loop inside its box starts over when it has run out.
                        state.Child = null;
                        Begin(state, now);
                    }
                }

                Tend(state.Voices, now, state.Top.Kind == Dd1AudioBank.InstrumentKind.Scatter);
                if (!state.Left) continue;

                // Left: a locked one was cut at that moment. A looping bed waits a little for the cursor to come back, then fades.
                var bed = !state.Locked && IsBed(state.Top);
                if (bed && !_stopped && scanned < state.LeftAt + Dd1AudioEngine.Grace && now < state.LeftAt + Dd1AudioEngine.Grace) continue;
                if (now < state.LeftAt && !_stopped) continue;
                foreach (var sounding in state.Voices)
                {
                    if (bed) _engine.Voices.Stop(sounding.Voice, Math.Max(now, state.LeftAt) + Dd1AudioEngine.LeaveFade, Dd1AudioEngine.LeaveFade);
                    _tails.Add(sounding);
                }
                if (state.Child != null) _released.Add(state.Child);
                if (state.Snapshot != null) _engine.Release(state);
                _boxes.RemoveAt(i);
            }

            Tend(_tails, now, true);
            for (var i = _released.Count - 1; i >= 0; i--)
            {
                _released[i].Advance(0f);
                if (_released[i].Finished) _released.RemoveAt(i);
            }

            // Over when nothing sounds and nothing more can begin.
            var ended = _stopped || (_scanPos > _lastEdge && PositionAt(now) >= _lastEdge);
            if (!ended || _tails.Count > 0 || _released.Count > 0) return;
            foreach (var state in _boxes)
            {
                if (AnyAlive(state) || (state.Child != null && !state.Child.Finished)) return;
                if (!_stopped && !state.Left) return;
            }
            foreach (var state in _boxes) _engine.Release(state);
            _boxes.Clear();
            Finished = true;
        }

        // The children hear their parent's parameters under the same names (DD1 sets them on the outer event only).
        private void Follow(string name, float value)
        {
            if (_params.TryGetValue(name, out var state)) state.Value = state.Target = Math.Max(state.Def.Min, Math.Min(state.Def.Max, value));
        }

        // Keeps the faders of what sounds where the mix has them, and drops what has ended.
        private void Tend(List<Sounding> voices, double now, bool dropDead)
        {
            for (var i = voices.Count - 1; i >= 0; i--)
            {
                var sounding = voices[i];
                if (!_engine.Voices.Alive(sounding.Voice))
                {
                    if (dropDead) voices.RemoveAt(i);
                    continue;
                }
                var volume = Dd1AudioEngine.Linear(Db(sounding, now));
                if (Math.Abs(volume - sounding.Volume) < 0.002f) continue;
                sounding.Volume = volume;
                _engine.Voices.Volume(sounding.Voice, volume);
            }
        }

        private bool AnyAlive(BoxState state) => CountAlive(state) > 0;

        private int CountAlive(BoxState state)
        {
            var alive = 0;
            foreach (var sounding in state.Voices)
                if (_engine.Voices.Alive(sounding.Voice)) alive++;
            return alive;
        }

        // ---- the mix ----

        private float Db(Sounding sounding) => Db(sounding, _engine.Voices.Now(Category));

        // A voice's place in the mix: its instruments, their audio tracks, then everything outside the event's tracks.
        private float Db(Sounding sounding, double now)
        {
            var db = 0f;
            foreach (var instrument in sounding.Chain) db += Automated(_volumes, instrument.Id, instrument.Volume, now);
            return db + Tracks(sounding.Chain[sounding.Chain.Length - 1].Track, now) + _outerDb;
        }

        private float Tracks(Guid track, double now)
        {
            var db = 0f;
            for (var steps = 0; steps < 16 && track != Guid.Empty; steps++)
            {
                if (!Bank.Buses.TryGetValue(track, out var bus) || bus.Kind != 'G') break;
                db += Automated(_volumes, bus.Id, bus.Volume, now);
                track = bus.Output;
            }
            return db;
        }

        // The master track, then either the event this one plays inside, or the mixer down to the master bus.
        private float Outer(double now)
        {
            var db = 0f;
            if (Bank.Buses.TryGetValue(Def.Master, out var master)) db += Automated(_volumes, master.Id, master.Volume, now);
            if (_parent != null)
                return db + _parent.Automated(_parent._volumes, _parentInstrument.Id, _parentInstrument.Volume, now) + _parent.Tracks(_parentInstrument.Track, now) + _parent._outerDb;
            if (_level < 0.999f) db += _level <= 0.001f ? -200f : 20f * (float)Math.Log10(_level);
            var id = Def.Input;
            for (var steps = 0; steps < 32 && id != Guid.Empty; steps++)
            {
                var bus = _engine.Project.Bus(Bank, id);
                if (bus == null) break;
                db += bus.Kind == 'I' ? bus.Volume : _engine.FaderDb(bus);
                id = bus.Output != bus.Id ? bus.Output : Guid.Empty;
            }
            return db;
        }

        /// <summary>The whole way from the event's master track to the output, dB: what every sample of it shares.</summary>
        public float OuterDb => _outerDb;

        // A fader's value: what its automation curves give (added up), or its own when nothing moves it.
        private float Automated(Dictionary<Guid, List<Auto>> table, Guid target, float own, double now)
        {
            if (!table.TryGetValue(target, out var list)) return own;
            var sum = 0f;
            foreach (var auto in list)
            {
                if (auto.Driver != null) sum += Curve(auto.Curve, auto.Driver.Value, false);
                else sum += Curve(auto.Curve, (float)PositionAt(now), true);
            }
            return sum;
        }

        // DD1's curves are bent between their points; here they are straight lines.
        private static float Curve(Dd1AudioBank.Point[] curve, float x, bool byPosition)
        {
            float X(int i) => byPosition ? curve[i].Position : curve[i].X;
            if (x <= X(0)) return curve[0].Y;
            for (var i = 1; i < curve.Length; i++)
            {
                if (x > X(i)) continue;
                var span = X(i) - X(i - 1);
                return span <= 0f ? curve[i].Y : curve[i - 1].Y + (curve[i].Y - curve[i - 1].Y) * (x - X(i - 1)) / span;
            }
            return curve[curve.Length - 1].Y;
        }

        // ---- for the bridge and the offline check ----

        public int VoiceCount
        {
            get
            {
                var count = _tails.Count;
                foreach (var state in _boxes) count += state.Voices.Count + (state.Child?.VoiceCount ?? 0);
                foreach (var child in _released) count += child.VoiceCount;
                return count;
            }
        }

        /// <summary>Seconds along the timeline at the category's clock.</summary>
        public double Seconds => PositionAt(_engine.Voices.Now(Category)) / Dd1AudioEngine.Rate;

        /// <summary>Where the timeline's last box or transition stands, seconds: how long an event without loops lasts.</summary>
        public double LengthSeconds => _lastEdge / Dd1AudioEngine.Rate;

        /// <summary>Whether anything of the event is to be heard, or still being opened.</summary>
        public bool IsSounding
        {
            get
            {
                foreach (var sounding in _tails)
                    if (_engine.Voices.Alive(sounding.Voice)) return true;
                foreach (var state in _boxes)
                    if (AnyAlive(state) || (state.Child != null && state.Child.IsSounding)) return true;
                foreach (var child in _released)
                    if (child.IsSounding) return true;
                return false;
            }
        }

        public int BoxCount => _boxes.Count;
    }
}
