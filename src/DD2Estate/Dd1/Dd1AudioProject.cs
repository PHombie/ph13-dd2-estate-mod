using System;
using System.Collections.Generic;
using System.IO;

namespace DD2Estate.Dd1
{
    /// <summary>The four volume sliders of DD2 a DD1 sound can belong to (ambience shares the effects' slider but is paused and faded on its own).</summary>
    internal enum Dd1Category { Music, Voice, Ambience, Sfx }

    /// <summary>
    /// The sound banks of a DD1 install as one project: the names (event path to id), the mixer of the master
    /// bank, and the other banks, read when an event in them is first asked for.
    ///
    /// The mapping rule. DD1's code and data name sounds by event path ("/vo/good/camp_04" in
    /// audio/narration.json is "event:/vo/good/camp_04"). The strings bank gives the path's id; the bank that
    /// holds an event with that id says what it plays: a timeline of instruments, each ending in wave assets,
    /// each wave asset the n-th sample of one of the bank's FSB5s. Sample names are not used: they follow the
    /// event names only loosely ("vo_narr_town_rec_occ").
    ///
    /// No Unity type is used here (see <see cref="Dd1AudioBank"/>).
    /// </summary>
    internal sealed class Dd1AudioProject
    {
        private const string StringsBank = "master_bank.strings";
        private const string MasterBank = "master_bank";

        // Where an event is looked for first, by the start of its path; any other bank is tried after these.
        // Names only: the same names audio/*.load_order.json list.
        private static readonly KeyValuePair<string, string[]>[] Homes =
        {
            new KeyValuePair<string, string[]>("event:/vo/", new[] { "voiceover", "voiceover_shared_courtyard" }),
            new KeyValuePair<string, string[]>("event:/music/", new[] { "music" }),
            new KeyValuePair<string, string[]>("event:/ambience/", new[] { "ambience" }),
            new KeyValuePair<string, string[]>("event:/town/", new[] { "town" }),
            new KeyValuePair<string, string[]>("event:/ui/", new[] { "ui_town", "ui_shared", "ui_dungeon" }),
            new KeyValuePair<string, string[]>("event:/general/", new[] { "general", "raid_screen" }),
            new KeyValuePair<string, string[]>("snapshot:/", new[] { MasterBank })
        };

        private readonly Action<string> _log;
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, Dd1AudioBank> _banks = new Dictionary<string, Dd1AudioBank>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, Dd1AudioBank> _eventHomes = new Dictionary<Guid, Dd1AudioBank>();
        private readonly Dictionary<Guid, Dd1Category> _categories = new Dictionary<Guid, Dd1Category>();

        public string Root { get; }
        public readonly Dictionary<Guid, string> Names;
        public readonly Dictionary<string, Guid> Ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        /// <summary>The mixer: group and return buses of the master bank, by id.</summary>
        public readonly Dictionary<Guid, Dd1AudioBank.Bus> Mixer = new Dictionary<Guid, Dd1AudioBank.Bus>();
        public Dd1AudioBank Master { get; }

        /// <summary>Throws when <paramref name="dd1Root"/> has no readable DD1 audio.</summary>
        public Dd1AudioProject(string dd1Root, Action<string> log)
        {
            Root = dd1Root;
            _log = log ?? (text => { });
            var audio = Path.Combine(dd1Root, "audio");
            AddFolder(Path.Combine(audio, "master_banks"));
            AddFolder(Path.Combine(audio, "secondary_banks"));
            // The add-ons bring their own banks (and some bring a hero's bank again): the first found wins.
            var dlc = Path.Combine(dd1Root, "dlc");
            if (Directory.Exists(dlc))
            {
                foreach (var pack in Sorted(Directory.GetDirectories(dlc)))
                {
                    AddFolder(Path.Combine(pack, "audio", "secondary_banks"));
                    var features = Path.Combine(pack, "features");
                    if (!Directory.Exists(features)) continue;
                    foreach (var feature in Sorted(Directory.GetDirectories(features)))
                        AddFolder(Path.Combine(feature, "audio", "secondary_banks"));
                }
            }
            if (!_files.TryGetValue(StringsBank, out var strings)) throw new FileNotFoundException("DD1's strings bank is missing", StringsBank + ".bank");
            Names = Dd1AudioNames.Read(strings);
            foreach (var pair in Names) Ids[pair.Value] = pair.Key;
            Master = Load(MasterBank);
            if (Master != null)
                foreach (var bus in Master.Buses.Values) Mixer[bus.Id] = bus;
        }

        private static string[] Sorted(string[] paths)
        {
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private void AddFolder(string folder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Sorted(Directory.GetFiles(folder, "*.bank")))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (_files.ContainsKey(name)) continue;
                _files[name] = file;
                if (name != StringsBank) _order.Add(name);
            }
        }

        public IEnumerable<string> BankNames => _order;

        public IEnumerable<Dd1AudioBank> LoadedBanks => _banks.Values;

        public string FileOf(string bank) => _files.TryGetValue(bank, out var file) ? file : null;

        /// <summary>A bank's objects, read on first use; null when the file is missing or unreadable (said once).</summary>
        public Dd1AudioBank Load(string name)
        {
            if (_banks.TryGetValue(name, out var bank)) return bank;
            bank = null;
            if (_files.TryGetValue(name, out var file))
            {
                try
                {
                    bank = Dd1AudioBank.Read(file);
                    if (bank.Errors.Count > 0) _log("DD1 bank " + name + ": " + bank.Errors.Count + " chunks not understood (first: " + bank.Errors[0] + ")");
                }
                catch (Exception e)
                {
                    _log("DD1 bank " + name + " could not be read: " + e.Message);
                    bank = null;
                }
            }
            else _log("DD1 bank " + name + " is not in the install");
            _banks[name] = bank;
            return bank;
        }

        /// <summary>"/vo/good/camp_04", "vo/good/camp_04" or "event:/vo/good/camp_04" as the strings bank spells it.</summary>
        public static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.IndexOf(":/", StringComparison.Ordinal) >= 0 ? path : "event:/" + path.TrimStart('/');
        }

        public bool Knows(string path) => Ids.ContainsKey(FullPath(path));

        /// <summary>The event behind a path and the bank that holds it; false when DD1 has no such event.</summary>
        public bool TryFind(string path, out Dd1AudioBank bank, out Dd1AudioBank.Event found)
        {
            bank = null;
            found = null;
            var full = FullPath(path);
            return Ids.TryGetValue(full, out var id) && TryFind(id, full, null, out bank, out found);
        }

        /// <summary>The event with this id: in <paramref name="near"/> if it is there (an event used inside another), else wherever it lives.</summary>
        public bool TryFind(Guid id, string path, Dd1AudioBank near, out Dd1AudioBank bank, out Dd1AudioBank.Event found)
        {
            if (near != null && near.Events.TryGetValue(id, out found))
            {
                bank = near;
                return true;
            }
            if (_eventHomes.TryGetValue(id, out bank))
            {
                found = null;
                return bank != null && bank.Events.TryGetValue(id, out found);
            }
            if (path == null) Names.TryGetValue(id, out path);
            foreach (var name in Candidates(path))
            {
                var candidate = Load(name);
                if (candidate == null || !candidate.Events.TryGetValue(id, out found)) continue;
                _eventHomes[id] = bank = candidate;
                return true;
            }
            _eventHomes[id] = bank = null;
            found = null;
            return false;
        }

        private IEnumerable<string> Candidates(string path)
        {
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (path != null)
            {
                foreach (var home in Homes)
                {
                    if (!path.StartsWith(home.Key, StringComparison.Ordinal)) continue;
                    foreach (var name in home.Value)
                        if (tried.Add(name)) yield return name;
                }
            }
            // Banks already read cost nothing to look into; the rest are read one by one until the event turns up.
            foreach (var name in _order)
                if (_banks.ContainsKey(name) && tried.Add(name)) yield return name;
            foreach (var name in _order)
                if (tried.Add(name)) yield return name;
        }

        /// <summary>A bus of an event (in its own bank) or of the mixer.</summary>
        public Dd1AudioBank.Bus Bus(Dd1AudioBank bank, Guid id)
        {
            if (id == Guid.Empty) return null;
            if (bank != null && bank.Buses.TryGetValue(id, out var bus)) return bus;
            return Mixer.TryGetValue(id, out bus) ? bus : null;
        }

        /// <summary>The buses between an event and the master bus, the event's own master track first.</summary>
        public List<Dd1AudioBank.Bus> Route(Dd1AudioBank bank, Dd1AudioBank.Event e)
        {
            var route = new List<Dd1AudioBank.Bus>();
            var id = e.Master;
            for (var steps = 0; steps < 32 && id != Guid.Empty; steps++)
            {
                var bus = Bus(bank, id);
                if (bus == null || route.Contains(bus)) break;
                route.Add(bus);
                id = bus.Output != bus.Id ? bus.Output : Guid.Empty;
                // The master track ends in the event's input bus, which carries on into the mixer.
                if (bus.Kind == 'M' && id == Guid.Empty && e.Input != bus.Id) id = e.Input;
            }
            return route;
        }

        /// <summary>Which of DD2's sliders an event answers to, by the mixer group DD1 routes it through.</summary>
        public Dd1Category Category(Dd1AudioBank bank, Dd1AudioBank.Event e)
        {
            if (_categories.TryGetValue(e.Id, out var known)) return known;
            var category = Dd1Category.Sfx;
            foreach (var bus in Route(bank, e))
            {
                if (!Names.TryGetValue(bus.Id, out var name)) continue;
                if (name.StartsWith("bus:/all_sum/music", StringComparison.Ordinal)) category = Dd1Category.Music;
                else if (name.StartsWith("bus:/all_sum/vo", StringComparison.Ordinal)) category = Dd1Category.Voice;
                else if (name.StartsWith("bus:/all_sum/sfx/ambience", StringComparison.Ordinal)) category = Dd1Category.Ambience;
                else continue;
                break;
            }
            _categories[e.Id] = category;
            return category;
        }

        /// <summary>What a snapshot sets, wherever its definition is stored (the master bank, as a rule).</summary>
        public Dd1AudioBank.SnapshotValue[] Snapshot(Dd1AudioBank bank, Guid id)
        {
            if (bank != null && bank.Snapshots.TryGetValue(id, out var values)) return values;
            if (Master != null && Master.Snapshots.TryGetValue(id, out values)) return values;
            foreach (var loaded in _banks.Values)
                if (loaded != null && loaded.Snapshots.TryGetValue(id, out values)) return values;
            return new Dd1AudioBank.SnapshotValue[0];
        }
    }
}
