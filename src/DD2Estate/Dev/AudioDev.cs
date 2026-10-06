using System;
using System.Collections.Generic;
using System.Globalization;
using DD2Estate.Dd1;
using DD2Estate.Dd2;
using DD2Estate.Estate;
using Newtonsoft.Json.Linq;

namespace DD2Estate.Dev
{
    /// <summary>
    /// Test commands of DD1's audio for the dev bridge ({"cmd":"run","name":"audio.play","path":"/vo/good/camp_04"}).
    /// They work from the main menu on: only the hook-ups (place, narration, town sounds) need an estate session.
    ///   audio.state                              what the estate plays, DD2's sliders, every voice and clock, what went wrong
    ///   audio.banks                              the banks of the DD1 install; which are read, their FSB5s
    ///   audio.samples bank=.. [filter=..]        a bank's samples: index, name, rate, channels, seconds
    ///   audio.events [prefix=vo/town] [limit=N]  event paths the strings bank knows
    ///   audio.event path=..                      one event: its bank, category, parameters, what stands on its timeline
    ///   audio.play path=.. [params=a=1,b=0]      plays an event of DD1's ("music/mus_town", "/vo/good/camp_04")
    ///   audio.play sample=.. [bank=..] [category=Voice] [volume=1]   plays one sample by its name, outside any event
    ///   audio.stop [path=..] [fade=1]            stops what the bridge started (all of it, or one event)
    ///   audio.param name=.. value=..             sets a parameter on what the estate and the bridge play ("darkness", "inside")
    ///   audio.volume [level=0..1] [enabled=..] [dungeonMusic=..] [campMusic=..]   the mod's own switches; shows DD2's sliders
    ///   audio.speak path=/vo/..                  the Ancestor's voice the way a narration line starts it (needs the hub)
    ///   audio.hush                               silences that voice
    /// </summary>
    [EstateModule]
    internal static class AudioDev
    {
        private static readonly List<Dd1Playing> Started = new List<Dd1Playing>();
        private static readonly List<int> Samples = new List<int>();

        private static void Register()
        {
            AgentBridge.Register("audio.state", o => new { estate = EstateAudio.Describe(), dd1 = Dd1Audio.Describe() });
            AgentBridge.Register("audio.banks", o =>
            {
                if (!Dd1Audio.Start()) return Dd1Audio.Status;
                var loaded = new HashSet<Dd1AudioBank>(Dd1Audio.Project.LoadedBanks);
                var list = new List<object>();
                foreach (var name in Dd1Audio.Project.BankNames)
                {
                    Dd1AudioBank bank = null;
                    foreach (var candidate in loaded)
                        if (candidate != null && candidate.Name == name) bank = candidate;
                    if ((bool?)o["read"] == true) bank = Dd1Audio.Project.Load(name);
                    list.Add(new { name, file = Dd1Audio.Project.FileOf(name), read = bank != null, format = bank?.Format, events = bank?.Events.Count, fsbs = bank?.Fsbs.Count, errors = bank?.Errors });
                }
                return list;
            });
            AgentBridge.Register("audio.samples", o =>
            {
                if (!Dd1Audio.Start()) return Dd1Audio.Status;
                var bank = Dd1Audio.Project.Load((string)o["bank"] ?? "voiceover");
                if (bank == null) return "no such bank (audio.banks lists them)";
                var filter = (string)o["filter"];
                var fsbs = new List<object>();
                for (var fsb = 0; fsb < bank.Fsbs.Count; fsb++)
                {
                    var samples = new List<object>();
                    var all = bank.Samples(fsb, out var codec);
                    foreach (var sample in all)
                        if (filter == null || sample.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                            samples.Add(new { index = sample.Index, name = sample.Name, rate = sample.Rate, channels = sample.Channels, seconds = Math.Round(sample.Seconds, 2) });
                    fsbs.Add(new { fsb, offset = bank.Fsbs[fsb].Offset, length = bank.Fsbs[fsb].Length, codec, count = all.Count, samples });
                }
                return fsbs;
            });
            AgentBridge.Register("audio.events", o =>
            {
                if (!Dd1Audio.Start()) return Dd1Audio.Status;
                var prefix = Dd1AudioProject.FullPath((string)o["prefix"] ?? "");
                if (prefix.Length == 0) prefix = "event:/";
                var paths = new List<string>();
                foreach (var path in Dd1Audio.Project.Ids.Keys)
                    if (path.StartsWith(prefix, StringComparison.Ordinal)) paths.Add(path);
                paths.Sort(StringComparer.Ordinal);
                var limit = (int?)o["limit"] ?? 300;
                return new { count = paths.Count, paths = paths.Count > limit ? paths.GetRange(0, limit) : paths };
            });
            AgentBridge.Register("audio.event", o =>
            {
                if (!Dd1Audio.Start()) return Dd1Audio.Status;
                var path = (string)o["path"];
                if (!Dd1Audio.Project.TryFind(path, out var bank, out var e)) return "DD1 has no such event (audio.events lists them)";
                return Event(bank, e, 0);
            });
            AgentBridge.Register("audio.play", o =>
            {
                if (!Dd1Audio.Start()) return Dd1Audio.Status;
                var sample = (string)o["sample"];
                if (sample != null) return PlaySample(sample, (string)o["bank"], (string)o["category"], (float?)o["volume"] ?? 1f);
                var path = (string)o["path"];
                var playing = Dd1Audio.Play(path, Parameters((string)o["params"]));
                if (playing == null) return "DD1 has no such event, or nothing in it to play (audio.events lists them)";
                Started.Add(playing);
                return new { playing = playing.Path, category = playing.Category.ToString(), bank = playing.Bank.Name, seconds = Math.Round(playing.LengthSeconds, 2), parameters = new List<string>(playing.ParameterNames) };
            });
            AgentBridge.Register("audio.stop", o =>
            {
                var path = (string)o["path"];
                var full = path != null ? Dd1AudioProject.FullPath(path) : null;
                var fade = (float?)o["fade"] ?? 0.5f;
                var stopped = 0;
                foreach (var playing in Started)
                {
                    if (playing.Finished || (full != null && playing.Path != full)) continue;
                    playing.Stop(fade);
                    stopped++;
                }
                Started.RemoveAll(p => p.Finished || p.Stopping);
                if (full == null)
                {
                    foreach (var voice in Samples) Dd1Audio.StopSample(voice);
                    Samples.Clear();
                }
                return new { stopped };
            });
            AgentBridge.Register("audio.param", o =>
            {
                var name = (string)o["name"];
                var value = (float?)o["value"] ?? 0f;
                var found = EstateAudio.SetParameter(name, value);
                foreach (var playing in Started)
                    if (!playing.Finished && playing.SetParameter(name, value)) found = true;
                return found ? "set" : "nothing that plays has that parameter";
            });
            AgentBridge.Register("audio.volume", o =>
            {
                if (o["level"] != null) EstateAudio.Level = Math.Max(0f, Math.Min(1f, (float)o["level"]));
                if (o["enabled"] != null) EstateAudio.Enabled = (bool)o["enabled"];
                if (o["dungeonMusic"] != null) EstateAudio.DungeonMusic = (bool)o["dungeonMusic"];
                if (o["campMusic"] != null) EstateAudio.CampMusic = (bool)o["campMusic"];
                return EstateAudio.Describe();
            });
            AgentBridge.Register("audio.speak", o =>
            {
                var started = EstateAudio.Speak("dev " + (string)o["path"]);
                return new { started, spokenUntil = EstateAudio.SpokenUntil, status = Dd1Audio.Status };
            });
            AgentBridge.Register("audio.hush", o =>
            {
                EstateAudio.Hush();
                return "silent";
            });
        }

        private static Dictionary<string, float> Parameters(string text)
        {
            var parameters = new Dictionary<string, float>();
            foreach (var pair in (text ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var at = pair.IndexOf('=');
                if (at > 0 && float.TryParse(pair.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) parameters[pair.Substring(0, at)] = value;
            }
            return parameters;
        }

        private static object PlaySample(string name, string bankName, string categoryName, float volume)
        {
            if (!Enum.TryParse(categoryName, true, out Dd1Category category)) category = Dd1Category.Sfx;
            var banks = new List<string>();
            if (bankName != null) banks.Add(bankName);
            else banks.AddRange(new[] { "voiceover", "music", "ambience", "town", "ui_town", "ui_shared", "general" });
            foreach (var candidate in banks)
            {
                var bank = Dd1Audio.Project.Load(candidate);
                if (bank == null) continue;
                for (var fsb = 0; fsb < bank.Fsbs.Count; fsb++)
                {
                    foreach (var sample in bank.Samples(fsb, out var codec))
                    {
                        if (!string.Equals(sample.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                        var voice = Dd1Audio.PlaySample(bank.Name, fsb, sample.Index, category, volume);
                        if (voice != 0) Samples.Add(voice);
                        return new { voice, bank = bank.Name, fsb, index = sample.Index, codec, seconds = Math.Round(sample.Seconds, 2), category = category.ToString() };
                    }
                }
            }
            return "no sample of that name (audio.samples lists them)";
        }

        // What an event is made of, as the plugin's own reader sees it (tools/dd1_audio.py prints the same from outside the game).
        private static object Event(Dd1AudioBank bank, Dd1AudioBank.Event e, int depth)
        {
            var project = Dd1Audio.Project;
            bank.Timelines.TryGetValue(e.Timeline, out var line);
            var parameters = new List<object>();
            foreach (var id in e.Layouts)
                if (bank.Layouts.TryGetValue(id, out var layout) && bank.Params.TryGetValue(layout.Param, out var param))
                    parameters.Add(new { name = param.Name, min = param.Min, max = param.Max, seek = param.Seek, automates = layout.Controllers.Count });
            var route = new List<object>();
            foreach (var bus in project.Route(bank, e))
                route.Add(new { bus = project.Names.TryGetValue(bus.Id, out var name) ? name : bus.Kind == 'M' ? "master track" : "input", db = bus.Volume });
            var boxes = new List<object>();
            var transitions = new List<object>();
            if (line != null)
            {
                foreach (var box in line.Async) boxes.Add(Box(bank, box, false, depth));
                foreach (var box in line.Locked) boxes.Add(Box(bank, box, true, depth));
                foreach (var t in line.Transitions)
                {
                    double to = -1;
                    foreach (var marker in line.Markers)
                        if (marker.Id == t.Destination) to = marker.Position / (double)Dd1AudioBank.TimelineRate;
                    transitions.Add(new { at = Math.Round(t.Start / (double)Dd1AudioBank.TimelineRate, 2), to = Math.Round(to, 2), chance = t.Chance, priority = t.Priority, conditions = t.Conditions.Count });
                }
            }
            return new
            {
                path = project.Names.TryGetValue(e.Id, out var path) ? path : e.Id.ToString("B"),
                bank = bank.Name,
                category = project.Category(bank, e).ToString(),
                snapshot = e.Snapshot != Guid.Empty,
                maxInstances = e.MaxInstances,
                route,
                parameters,
                boxes,
                transitions
            };
        }

        private static object Box(Dd1AudioBank bank, Dd1AudioBank.Box box, bool locked, int depth)
        {
            return new
            {
                start = Math.Round(box.Start / (double)Dd1AudioBank.TimelineRate, 2),
                seconds = Math.Round(box.Length / (double)Dd1AudioBank.TimelineRate, 2),
                locked,
                plays = Instrument(bank, box.Instrument, depth)
            };
        }

        private static object Instrument(Dd1AudioBank bank, Guid id, int depth)
        {
            if (!bank.Instruments.TryGetValue(id, out var instrument)) return "missing";
            switch (instrument.Kind)
            {
                case Dd1AudioBank.InstrumentKind.Wave:
                {
                    if (!bank.Waves.TryGetValue(instrument.Target, out var wave)) return "wave without sample";
                    var name = "";
                    try
                    {
                        var samples = bank.Samples(wave.Fsb, out _);
                        if (wave.Index < samples.Count) name = samples[wave.Index].Name;
                    }
                    catch (Exception e) { name = e.Message; }
                    return new { wave = name, fsb = wave.Fsb, index = wave.Index, streamed = wave.Streamed, db = instrument.Volume, pitch = instrument.Pitch, loop = instrument.Loop };
                }
                case Dd1AudioBank.InstrumentKind.Event:
                {
                    if (depth > 3 || !Dd1Audio.Project.TryFind(instrument.Target, null, bank, out var other, out var e)) return new { missingEvent = instrument.Target.ToString("B") };
                    return new { loop = instrument.Loop, plays = Event(other, e, depth + 1) };
                }
                default:
                {
                    var entries = new List<object>();
                    foreach (var entry in instrument.Entries)
                        if (entries.Count < 40) entries.Add(new { weight = entry.Weight, plays = depth > 3 ? null : Instrument(bank, entry.Instrument, depth + 1) });
                    if (instrument.Kind == Dd1AudioBank.InstrumentKind.Scatter)
                        return new { scatterer = instrument.Entries.Count, every = new[] { instrument.IntervalMin, instrument.IntervalMax }, polyphony = instrument.Polyphony, spawns = instrument.SpawnTotal, db = instrument.Volume, pitch = instrument.Pitch, entries };
                    return new { pool = instrument.Entries.Count, mode = instrument.Mode, db = instrument.Volume, pitch = instrument.Pitch, loop = instrument.Loop, entries };
                }
            }
        }
    }
}
