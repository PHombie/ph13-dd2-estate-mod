using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DD2Estate.Dd1
{
    /// <summary>
    /// One of DD1's sound banks (audio/secondary_banks/*.bank in the player's install), as far as the estate
    /// needs it: which events it holds, what an event plays and where the samples are. Only the project part
    /// of the file is read (a few hundred kilobytes); the sample data stays on disk for FMOD to stream.
    ///
    /// The file is an FMOD Studio 1.x bank (format 103; DD2's FMOD 2.02 does not load it as a bank): RIFF
    /// "FEV ", a FMT chunk, LIST "PROJ" with the objects, and a SND chunk per FSB5 of sample data. Objects
    /// are named by 16-byte GUIDs. Arrays of fixed-size elements are written as u16 (count * 2 + 1) and, when
    /// the count is not 0, the u16 element size; arrays of variable-size elements as u16 (count * 2) with a
    /// u16 size before each element. Timeline positions are samples at 48000 Hz. The chunks are listed in
    /// docs/recon/dd1-audio.md; tools/dd1_audio.py is the reference this file follows chunk by chunk.
    ///
    /// No Unity type is used here, so the reader can be run outside the game against the tool.
    /// </summary>
    internal sealed class Dd1AudioBank
    {
        public const int TimelineRate = 48000;

        public sealed class Event
        {
            public Guid Id, Snapshot, Timeline, Input, Master;
            public int MaxInstances;
            /// <summary>The parameter layouts of the event: one per parameter it has.</summary>
            public Guid[] Layouts = new Guid[0];
        }

        /// <summary>An instrument's place on a timeline.</summary>
        public struct Box
        {
            public Guid Instrument;
            public uint Start, Length;
            public double End => (double)Start + Length;
        }

        public struct Marker
        {
            public Guid Id;
            public uint Position;
            public string Name;
        }

        public struct Condition
        {
            public Guid Param;
            public float Min, Max;
        }

        /// <summary>At <see cref="Start"/> the cursor may go to the marker <see cref="Destination"/>. A loop region is a transition to a marker that carries the transition's own id.</summary>
        public sealed class Transition
        {
            public Guid Id, Destination;
            public uint Start, End, Priority;
            /// <summary>Percent.</summary>
            public float Chance = 100f;
            public readonly List<Condition> Conditions = new List<Condition>();
        }

        public sealed class Timeline
        {
            public Guid Id, Event;
            /// <summary>Instruments that play on their own clock while the cursor is in their box.</summary>
            public readonly List<Box> Async = new List<Box>();
            /// <summary>Instruments cut to the timeline: they start where the cursor is and stop when it leaves.</summary>
            public readonly List<Box> Locked = new List<Box>();
            public readonly List<Marker> Markers = new List<Marker>();
            public readonly List<Transition> Transitions = new List<Transition>();
            /// <summary>Controllers moved by the cursor's position.</summary>
            public readonly List<Guid> Controllers = new List<Guid>();
        }

        public enum InstrumentKind { Wave, Multi, Scatter, Event }

        public struct Entry
        {
            public Guid Instrument;
            public float Weight;
        }

        public sealed class Instrument
        {
            public InstrumentKind Kind;
            public Guid Id;
            /// <summary>The wave asset (Wave) or the event (Event).</summary>
            public Guid Target;
            /// <summary>The audio track it plays on: a group bus inside the bank. Empty for a pool's entry.</summary>
            public Guid Track;
            /// <summary>dB and semitones.</summary>
            public float Volume, Pitch;
            public bool Loop;
            /// <summary>Multi and Scatter: the pool. Mode 0 plays it in order, anything else at random by weight.</summary>
            public readonly List<Entry> Entries = new List<Entry>();
            public uint Mode;
            /// <summary>Scatter: voices at once, spawns in all (0: no limit), seconds between two spawns.</summary>
            public uint Polyphony, SpawnTotal;
            public float IntervalMin, IntervalMax;
        }

        public struct Wave
        {
            /// <summary>Which FSB5 of the bank, and which sample in it.</summary>
            public int Fsb, Index;
            public bool Streamed;
        }

        public sealed class Bus
        {
            /// <summary>'I' an event's input, 'M' its master track, 'G' a group (an audio track inside an event, or a mixer group in the master bank), 'R' a return.</summary>
            public char Kind;
            public Guid Id, Output;
            /// <summary>The fader, dB.</summary>
            public float Volume;
        }

        public sealed class Param
        {
            public Guid Id;
            public string Name = "";
            /// <summary>Seek: how far the value moves in a second towards what it was set to; 0 jumps.</summary>
            public float Min, Max, Seek;
        }

        public sealed class Layout
        {
            public Guid Id, Param, Event;
            /// <summary>Controllers moved by the parameter's value.</summary>
            public readonly List<Guid> Controllers = new List<Guid>();
        }

        /// <summary>Automation: <see cref="Property"/> of <see cref="Target"/> follows <see cref="Curve"/>. Property 0 is the volume in dB, 3 a snapshot's intensity (0..1).</summary>
        public sealed class Controller
        {
            public Guid Id, Target, Curve;
            public uint Property;
        }

        public struct Point
        {
            /// <summary>The same four bytes read as a parameter value and as a timeline position.</summary>
            public float X;
            public uint Position;
            public float Y;
        }

        public struct SnapshotValue
        {
            public Guid Target;
            public uint Property;
            public float Value;
        }

        public struct Fsb
        {
            public uint Offset, Length;
        }

        /// <summary>A sample as its FSB5 lists it.</summary>
        public struct Sample
        {
            public int Index, Rate, Channels;
            public string Name;
            public double Seconds;
        }

        public string File { get; private set; }
        public string Name { get; private set; }
        public int Format { get; private set; }
        public readonly List<Fsb> Fsbs = new List<Fsb>();
        public readonly Dictionary<Guid, Event> Events = new Dictionary<Guid, Event>();
        public readonly Dictionary<Guid, Timeline> Timelines = new Dictionary<Guid, Timeline>();
        public readonly Dictionary<Guid, Instrument> Instruments = new Dictionary<Guid, Instrument>();
        public readonly Dictionary<Guid, Wave> Waves = new Dictionary<Guid, Wave>();
        public readonly Dictionary<Guid, Bus> Buses = new Dictionary<Guid, Bus>();
        public readonly Dictionary<Guid, Param> Params = new Dictionary<Guid, Param>();
        public readonly Dictionary<Guid, Layout> Layouts = new Dictionary<Guid, Layout>();
        public readonly Dictionary<Guid, Controller> Controllers = new Dictionary<Guid, Controller>();
        public readonly Dictionary<Guid, Point[]> Curves = new Dictionary<Guid, Point[]>();
        public readonly Dictionary<Guid, SnapshotValue[]> Snapshots = new Dictionary<Guid, SnapshotValue[]>();
        /// <summary>Chunks that could not be read (a bank of another format version): the rest of the bank is still used.</summary>
        public readonly List<string> Errors = new List<string>();

        // What the chunks that follow belong to.
        private Instrument _instrument;
        private Bus _bus;
        private Timeline _timeline;
        private Layout _layout;

        private Dd1AudioBank()
        {
        }

        /// <summary>Reads a bank's objects. Throws on a file that is no FMOD bank at all.</summary>
        public static Dd1AudioBank Read(string file)
        {
            var bank = new Dd1AudioBank { File = file, Name = Path.GetFileNameWithoutExtension(file) };
            var lists = Lists(file, out var format);
            bank.Format = format;
            foreach (var list in lists) bank.Walk(list, 0, list.Length);
            return bank;
        }

        /// <summary>The bodies of the file's top-level lists (PROJ); the sample chunks are stepped over.</summary>
        internal static List<byte[]> Lists(string file, out int format)
        {
            var lists = new List<byte[]>();
            format = 0;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var head = new byte[12];
                if (stream.Read(head, 0, 12) != 12 || Tag(head, 0) != "RIFF" || Tag(head, 8) != "FEV ")
                    throw new InvalidDataException("not an FMOD bank: " + file);
                long end = Math.Min(stream.Length, 8L + BitConverter.ToUInt32(head, 4));
                long pos = 12;
                while (pos + 8 <= end)
                {
                    stream.Position = pos;
                    if (stream.Read(head, 0, 8) != 8) break;
                    var tag = Tag(head, 0);
                    long size = BitConverter.ToUInt32(head, 4);
                    if (tag == "LIST" && size >= 4 && size < 64 * 1024 * 1024)
                    {
                        var body = new byte[size];
                        if (stream.Read(body, 0, body.Length) != body.Length) break;
                        // Without its four-letter form: what is left is a run of chunks.
                        var chunks = new byte[size - 4];
                        Buffer.BlockCopy(body, 4, chunks, 0, chunks.Length);
                        lists.Add(chunks);
                    }
                    else if (tag == "FMT " && size >= 4)
                    {
                        var body = new byte[4];
                        if (stream.Read(body, 0, 4) == 4) format = BitConverter.ToInt32(body, 0);
                    }
                    pos += 8 + size + (size & 1);
                }
            }
            return lists;
        }

        private static string Tag(byte[] data, int at) => Encoding.ASCII.GetString(data, at, 4);

        private static readonly int[] SampleRates = { 4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000 };
        private static readonly int[] SampleChannels = { 1, 2, 6, 8 };
        private readonly Dictionary<int, List<Sample>> _samples = new Dictionary<int, List<Sample>>();

        /// <summary>FSB5 codec numbers DD1 uses: 15 Vorbis, 16 FADPCM.</summary>
        public static string CodecName(int mode) => mode == 15 ? "VORBIS" : mode == 16 ? "FADPCM" : mode == 2 ? "PCM16" : "codec " + mode;

        /// <summary>
        /// The sample table of one of the bank's FSB5s, read from its header: "FSB5", version, sample count,
        /// size of the sample headers, of the name table and of the data, codec; 60 header bytes in all (64 in
        /// version 0). A sample header is a 64-bit word (bit 0: more chunks follow, 4 bits rate, 2 bits
        /// channels, 27 bits data offset / 32, 30 bits length in samples) and its extra chunks (u32: bit 0
        /// more, 24 bits size, 7 bits kind; kind 1 channels, 2 rate). The names follow as offsets and strings.
        /// The plugin plays samples by index and needs none of this; it is for listing and for the test bridge.
        /// </summary>
        public List<Sample> Samples(int fsb, out string codec)
        {
            codec = "";
            if (fsb < 0 || fsb >= Fsbs.Count) return new List<Sample>();
            using (var stream = new FileStream(File, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var head = new byte[28];
                stream.Position = Fsbs[fsb].Offset;
                if (stream.Read(head, 0, 28) != 28 || Tag(head, 0) != "FSB5") throw new InvalidDataException("no FSB5 at " + Fsbs[fsb].Offset);
                codec = CodecName(BitConverter.ToInt32(head, 24));
                if (_samples.TryGetValue(fsb, out var known)) return known;
                var count = BitConverter.ToInt32(head, 8);
                var headers = new byte[BitConverter.ToInt32(head, 12)];
                var names = new byte[BitConverter.ToInt32(head, 16)];
                stream.Position = Fsbs[fsb].Offset + (BitConverter.ToInt32(head, 4) == 0 ? 64 : 60);
                if (stream.Read(headers, 0, headers.Length) != headers.Length || stream.Read(names, 0, names.Length) != names.Length)
                    throw new InvalidDataException("FSB5 header cut short");
                var list = new List<Sample>(count);
                var r = new Cursor(headers, 0, headers.Length);
                for (var i = 0; i < count; i++)
                {
                    var low = r.U32();
                    var high = r.U32();
                    var word = low | ((ulong)high << 32);
                    var sample = new Sample { Index = i, Name = "", Rate = SampleRates[Math.Min(10, (int)((word >> 1) & 15))], Channels = SampleChannels[(int)((word >> 5) & 3)] };
                    var length = (long)((word >> 34) & 0x3FFFFFFF);
                    var more = (word & 1) != 0;
                    while (more)
                    {
                        var chunk = r.U32();
                        more = (chunk & 1) != 0;
                        var size = (int)((chunk >> 1) & 0xFFFFFF);
                        var kind = (chunk >> 25) & 0x7F;
                        var body = new Cursor(headers, r.Position, size);
                        r.Skip(size);
                        if (kind == 1 && size >= 1) sample.Channels = body.U8();
                        else if (kind == 2 && size >= 4) sample.Rate = (int)body.U32();
                    }
                    sample.Seconds = sample.Rate > 0 ? (double)length / sample.Rate : 0.0;
                    if (names.Length >= 4 * count)
                    {
                        var start = BitConverter.ToInt32(names, 4 * i);
                        var end = start >= 0 && start < names.Length ? Array.IndexOf(names, (byte)0, start) : -1;
                        if (end >= start && start >= 0) sample.Name = Encoding.UTF8.GetString(names, start, end - start);
                    }
                    list.Add(sample);
                }
                _samples[fsb] = list;
                return list;
            }
        }

        private void Walk(byte[] data, int start, int end)
        {
            var pos = start;
            while (pos + 8 <= end)
            {
                var tag = Tag(data, pos);
                var size = (int)BitConverter.ToUInt32(data, pos + 4);
                if (size < 0 || pos + 8 + size > end) break;
                if (tag == "LIST")
                {
                    if (size >= 4) Walk(data, pos + 12, pos + 8 + size);
                }
                else
                {
                    try { Chunk(tag, new Cursor(data, pos + 8, size)); }
                    catch (Exception e) { if (Errors.Count < 32) Errors.Add(tag.Trim() + ": " + e.Message); }
                }
                pos += 8 + size + (size & 1);
            }
        }

        private void Chunk(string tag, Cursor r)
        {
            switch (tag)
            {
                case "SNDH":
                    foreach (var e in r.Fixed())
                        Fsbs.Add(new Fsb { Offset = e.U32(), Length = e.U32() });
                    break;
                case "EVTB":
                {
                    var e = new Event { Id = r.Guid(), Snapshot = r.Guid(), Timeline = r.Guid(), Input = r.Guid(), Master = r.Guid(), MaxInstances = r.I32() };
                    r.Skip(4 + 1 + 4);      // priority, two fields the estate has no use for
                    var layouts = r.Fixed();
                    e.Layouts = new Guid[layouts.Count];
                    for (var i = 0; i < layouts.Count; i++) e.Layouts[i] = layouts[i].Guid();
                    Events[e.Id] = e;
                    break;
                }
                case "TLNB":
                {
                    var line = new Timeline { Id = r.Guid(), Event = r.Guid() };
                    foreach (var e in r.Fixed()) line.Async.Add(new Box { Instrument = e.Guid(), Start = e.U32(), Length = e.U32() });
                    foreach (var e in r.Fixed()) line.Locked.Add(new Box { Instrument = e.Guid(), Start = e.U32(), Length = e.U32() });
                    r.Fixed();              // always empty in DD1's banks
                    foreach (var e in r.Variable()) line.Markers.Add(new Marker { Id = e.Guid(), Position = e.U32(), Name = e.Text() });
                    Timelines[line.Id] = line;
                    _timeline = line;
                    _layout = null;
                    break;
                }
                case "TRNB":
                {
                    var t = new Transition { Id = r.Guid(), Destination = r.Guid(), Start = r.U32(), End = r.U32() };
                    foreach (var e in r.Fixed()) t.Conditions.Add(new Condition { Param = e.Guid(), Min = e.F32(), Max = e.F32() });
                    r.Skip(8);
                    t.Chance = r.F32();
                    t.Priority = r.U32();
                    _timeline?.Transitions.Add(t);
                    break;
                }
                case "CTRO":
                    foreach (var e in r.Fixed())
                    {
                        if (_layout != null) _layout.Controllers.Add(e.Guid());
                        else _timeline?.Controllers.Add(e.Guid());
                    }
                    break;
                case "PMLB":
                {
                    var layout = new Layout { Id = r.Guid(), Param = r.Guid(), Event = r.Guid() };
                    Layouts[layout.Id] = layout;
                    _layout = layout;
                    break;
                }
                case "PRMB":
                {
                    var p = new Param { Id = r.Guid() };
                    r.Skip(5);
                    p.Name = r.Text();
                    p.Min = r.F32();
                    p.Max = r.F32();
                    r.Skip(8);
                    p.Seek = r.F32();
                    Params[p.Id] = p;
                    break;
                }
                case "CTRL":
                {
                    var c = new Controller { Id = r.Guid(), Target = r.Guid(), Curve = r.Guid(), Property = r.U32() };
                    Controllers[c.Id] = c;
                    break;
                }
                case "CURV":
                {
                    var id = r.Guid();
                    r.Guid();
                    var points = r.Fixed();
                    var curve = new Point[points.Count];
                    for (var i = 0; i < curve.Length; i++)
                    {
                        var e = points[i];
                        curve[i].Position = e.U32();
                        e.Back(4);
                        curve[i].X = e.F32();
                        curve[i].Y = e.F32();
                    }
                    Curves[id] = curve;
                    break;
                }
                case "WAIB":
                    Begin(new Instrument { Kind = InstrumentKind.Wave, Id = r.Guid(), Target = r.Guid() });
                    break;
                case "MUIB":
                    Begin(new Instrument { Kind = InstrumentKind.Multi, Id = r.Guid() });
                    break;
                case "SPIB":
                    Begin(new Instrument { Kind = InstrumentKind.Scatter, Id = r.Guid(), Polyphony = r.U32(), SpawnTotal = r.U32(), IntervalMin = r.F32(), IntervalMax = r.F32() });
                    break;
                case "EVIB":
                    Begin(new Instrument { Kind = InstrumentKind.Event, Id = r.Guid(), Target = r.Guid() });
                    break;
                case "PLST":
                    if (_instrument == null) break;
                    _instrument.Mode = r.U32();
                    r.Skip(4);
                    foreach (var e in r.Fixed()) _instrument.Entries.Add(new Entry { Instrument = e.Guid(), Weight = e.F32() });
                    break;
                case "INST":
                    if (_instrument == null) break;
                    r.Skip(16);             // the timeline it stands on
                    _instrument.Volume = r.F32();
                    _instrument.Pitch = r.F32();
                    r.Skip(4);
                    _instrument.Loop = r.U8() != 0;
                    r.Skip(52);
                    r.Skip(2);
                    _instrument.Track = r.Guid();
                    break;
                case "WAV ":
                {
                    var id = r.Guid();
                    r.Skip(2);
                    Waves[id] = new Wave { Fsb = (int)r.U32(), Index = (int)r.U32(), Streamed = (r.U32() & 2) != 0 };
                    break;
                }
                case "IBSB":
                case "MBSB":
                case "GBSB":
                case "RBSB":
                {
                    var bus = new Bus { Kind = tag[0], Id = r.Guid() };
                    r.Skip(2);
                    bus.Output = r.Guid();
                    Buses[bus.Id] = bus;
                    _bus = bus;
                    _instrument = null;
                    break;
                }
                case "BUS ":
                    if (_bus == null) break;
                    r.Skip(5);
                    r.Fixed();              // effects before the fader
                    r.Fixed();              // and after it
                    r.Skip(2);
                    _bus.Volume = r.F32();
                    _bus = null;
                    break;
                case "SNAB":
                {
                    var id = r.Guid();
                    r.Skip(4);
                    var values = r.Fixed();
                    var set = new SnapshotValue[values.Count];
                    for (var i = 0; i < set.Length; i++)
                    {
                        var e = values[i];
                        e.Skip(4);
                        set[i] = new SnapshotValue { Target = e.Guid(), Property = e.U32(), Value = e.F32() };
                    }
                    Snapshots[id] = set;
                    break;
                }
            }
        }

        private void Begin(Instrument instrument)
        {
            Instruments[instrument.Id] = instrument;
            _instrument = instrument;
            _bus = null;
        }

        /// <summary>A bounds-checked reader over one chunk (or one array element). A class: it is read from inside foreach loops.</summary>
        internal sealed class Cursor
        {
            private readonly byte[] _data;
            private readonly int _end;
            private int _pos;

            public Cursor(byte[] data, int start, int size)
            {
                _data = data;
                _pos = start;
                _end = start + size;
            }

            private int Take(int n)
            {
                if (n < 0 || _pos + n > _end) throw new InvalidDataException("chunk too short");
                var at = _pos;
                _pos += n;
                return at;
            }

            public void Skip(int n) => Take(n);
            public void Back(int n) => _pos -= n;
            public int Position => _pos;
            public int Left => _end - _pos;
            public byte U8() => _data[Take(1)];
            public ushort U16() => BitConverter.ToUInt16(_data, Take(2));
            public uint U32() => BitConverter.ToUInt32(_data, Take(4));
            public int I32() => BitConverter.ToInt32(_data, Take(4));
            public float F32() => BitConverter.ToSingle(_data, Take(4));

            public Guid Guid()
            {
                var at = Take(16);
                return new Guid(BitConverter.ToInt32(_data, at), BitConverter.ToInt16(_data, at + 4), BitConverter.ToInt16(_data, at + 6),
                    _data[at + 8], _data[at + 9], _data[at + 10], _data[at + 11], _data[at + 12], _data[at + 13], _data[at + 14], _data[at + 15]);
            }

            public string Text()
            {
                var length = U16();
                return Encoding.UTF8.GetString(_data, Take(length), length);
            }

            public byte[] Bytes(int n)
            {
                var copy = new byte[n];
                Buffer.BlockCopy(_data, Take(n), copy, 0, n);
                return copy;
            }

            /// <summary>An array of fixed-size elements.</summary>
            public List<Cursor> Fixed()
            {
                var list = new List<Cursor>();
                var head = U16();
                var count = head >> 1;
                if (count == 0) return list;
                if ((head & 1) == 0) throw new InvalidDataException("expected fixed-size elements");
                var size = U16();
                for (var i = 0; i < count; i++) list.Add(new Cursor(_data, Take(size), size));
                return list;
            }

            /// <summary>An array of variable-size elements.</summary>
            public List<Cursor> Variable()
            {
                var list = new List<Cursor>();
                var head = U16();
                if ((head & 1) != 0) throw new InvalidDataException("expected variable-size elements");
                for (var i = 0; i < head >> 1; i++)
                {
                    var size = U16();
                    list.Add(new Cursor(_data, Take(size), size));
                }
                return list;
            }
        }
    }

    /// <summary>
    /// The names of DD1's audio objects: audio/master_banks/master_bank.strings.bank. Its STDT chunk is a radix
    /// tree over the paths ("event:/vo/good/camp_04", "bus:/all_sum/music"): 8-byte nodes (u24 offset into a
    /// pool of zero-terminated strings, a key byte, u24 first child, u8 child count), the GUIDs, the pool, for
    /// each GUID its leaf node and for each node its parent. A path is the strings from the root to the leaf.
    /// </summary>
    internal static class Dd1AudioNames
    {
        public static Dictionary<Guid, string> Read(string file)
        {
            var names = new Dictionary<Guid, string>();
            foreach (var list in Dd1AudioBank.Lists(file, out _))
            {
                var pos = 0;
                while (pos + 8 <= list.Length)
                {
                    var tag = Encoding.ASCII.GetString(list, pos, 4);
                    var size = (int)BitConverter.ToUInt32(list, pos + 4);
                    if (size < 0 || pos + 8 + size > list.Length) break;
                    if (tag == "STDT" && size > 0) Table(new Dd1AudioBank.Cursor(list, pos + 8, size), names);
                    pos += 8 + size + (size & 1);
                }
            }
            return names;
        }

        private static void Table(Dd1AudioBank.Cursor r, Dictionary<Guid, string> names)
        {
            r.Skip(4);
            var nodes = r.Fixed();
            var guids = r.Fixed();
            var pool = r.Bytes(r.U16());
            var leaves = new int[r.U16()];
            for (var i = 0; i < leaves.Length; i++) leaves[i] = U24(r);
            var parents = new int[r.U16()];
            for (var i = 0; i < parents.Length; i++) parents[i] = U24(r);
            if (leaves.Length != guids.Count || parents.Length != nodes.Count) throw new InvalidDataException("the string table does not add up");

            var starts = new int[nodes.Count];
            for (var i = 0; i < starts.Length; i++) starts[i] = U24(nodes[i]);
            var parts = new List<string>();
            var text = new StringBuilder();
            for (var i = 0; i < guids.Count; i++)
            {
                parts.Clear();
                for (var node = leaves[i]; node != 0xFFFFFF && node < starts.Length && parts.Count < 64; node = parents[node])
                {
                    var start = starts[node];
                    if (start == 0xFFFFFF || start >= pool.Length) continue;
                    var end = Array.IndexOf(pool, (byte)0, start);
                    if (end < 0) end = pool.Length;
                    parts.Add(Encoding.UTF8.GetString(pool, start, end - start));
                }
                text.Length = 0;
                for (var p = parts.Count - 1; p >= 0; p--) text.Append(parts[p]);
                names[guids[i].Guid()] = text.ToString();
            }
        }

        private static int U24(Dd1AudioBank.Cursor r) => r.U8() | (r.U8() << 8) | (r.U8() << 16);
    }
}
