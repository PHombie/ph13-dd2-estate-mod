#!/usr/bin/env python3
"""DD1's sound banks, read offline: what is in them and which samples an event plays.

Reference implementation of src/DD2Estate/Dd1/Dd1AudioBank.cs (the plugin reads the same chunks at runtime from
the player's own install): change one, change the other. Nothing of DD1's is copied into the repo; `map` writes
its table only where you tell it to.

    python tools/dd1_audio.py banks                      every bank: its FSB5s, codec, samples, objects
    python tools/dd1_audio.py samples voiceover [text]   the sample table of a bank (name, rate, channels, length)
    python tools/dd1_audio.py events [prefix]            event paths, the bank that holds each, its shape
    python tools/dd1_audio.py event /vo/good/camp_04     one event in full: timeline, pools, transitions, buses
    python tools/dd1_audio.py coverage                   audio/narration.json against the banks
    python tools/dd1_audio.py estate                     the events the estate plays (music, ambience, town, ui)
    python tools/dd1_audio.py map out.json               everything as JSON, to compare with the plugin's own parse
    [--dd1 <DD1 install dir>] or DD1_GAME_DIR

The format (FMOD Studio 1.x bank, format version 103 in the FMT chunk; docs/recon/dd1-audio.md has the long form):

  * A bank is RIFF: `RIFF <size> "FEV "`, a `FMT ` chunk, one `LIST "PROJ"` with the project's objects and one
    `SND ` chunk per FSB5 with the sample data. `SNDH` (inside PROJ) lists the FSB5s: file offset and length.
    Chunks are padded to even sizes; the FSB5 itself starts on a 32-byte boundary inside its SND chunk.
  * Objects are 16-byte GUIDs. Arrays are written two ways: of fixed-size elements as u16 (count*2+1), then
    u16 element size when the count is not 0; of variable-size elements as u16 (count*2), each element led by
    its u16 size.
  * master_bank.strings.bank holds the names: its STDT chunk is a radix tree (8-byte nodes: u24 offset into a
    string pool, a key byte, u24 first child, u8 child count), the GUIDs sorted, the pool, per GUID its leaf
    node and per node its parent. A path is the pool strings from the root down to the leaf.
  * event (EVNT/EVTB): guid, snapshot guid (zero for a sound event), timeline, input bus, master track bus,
    max instances, priority, the parameter layouts it uses.
  * timeline (TMLN/TLNB): guid, event, instruments that play on their own clock while the cursor is inside
    their box ("async": guid, start, length), instruments cut to the timeline ("locked"), markers (guid,
    position, name), tempo markers. Positions are samples at 48000 Hz. Its transitions (TRNS/TRAN/TRNB): guid,
    destination marker, start, end, parameter conditions, chance in percent, priority. A loop region is a
    transition whose destination is a marker carrying the transition's own guid.
  * instruments: WAIT/WAIB (one wave: guid, wave asset), MUIT/MUIB + PLST (a pool: entries with weights),
    SPIT/SPIB + PLST (a scatterer: a pool spawned again and again at random intervals), EVIT/EVIB (another
    event, or a snapshot). Each has an INST chunk: owner timeline, volume in dB, pitch in semitones, a loop
    flag, and the audio track (a group bus inside the bank) it plays on.
  * automation: a controller (CTRS/CTRL: guid, target object, curve, property; 0 is the volume in dB, 3 a
    snapshot's intensity 0..1) with its curve (CRVS/CURV: points x, y, shape). The CTRO chunk after a timeline
    or after a parameter layout (PMLO/PMLB: guid, parameter, event) lists the controllers that timeline
    position or that parameter drives. A parameter (PRMS/PARM/PRMB) has a name, a range and a seek speed.
  * wave asset (WAVS/WAV): guid, u16 12, FSB5 index in SNDH, sample index in that FSB5, flags (2: streamed).
  * buses (IBUS input, MBUS master track, GBUS group, RBUS return): guid, output bus; the BUS chunk next to
    it carries the fader in dB. Group buses live in master_bank.bank.
  * FSB5: "FSB5", version, sample count, sample header size, name table size, data size, codec (15 Vorbis,
    16 FADPCM), 32 more header bytes; per sample a 64-bit word (bit 0: extra chunks follow, 4 bits rate, 2 bits
    channels, 27 bits data offset / 32, 30 bits length in samples), then the name table.
"""
import argparse
import collections
import glob
import json
import os
import struct
import sys

DEFAULT_DD1_DIR = r"E:\Steam\steamapps\common\DarkestDungeon"
TIMELINE_RATE = 48000                # timeline positions are samples at this rate, whatever the waves are
ZERO = b"\0" * 16

FSB_RATES = {0: 4000, 1: 8000, 2: 11000, 3: 11025, 4: 16000, 5: 22050, 6: 24000, 7: 32000, 8: 44100, 9: 48000, 10: 96000}
FSB_CHANNELS = {0: 1, 1: 2, 2: 6, 3: 8}
FSB_CODECS = {1: "PCM8", 2: "PCM16", 3: "PCM24", 4: "PCM32", 5: "PCMFLOAT", 6: "GCADPCM", 7: "IMAADPCM", 8: "VAG",
              9: "HEVAG", 10: "XMA", 11: "MPEG", 12: "CELT", 13: "AT9", 14: "XWMA", 15: "VORBIS", 16: "FADPCM", 17: "OPUS"}


# ---- bytes ---------------------------------------------------------------------------------------------------------

class Reader:
    def __init__(self, data, pos=0):
        self.data = data
        self.pos = pos

    def take(self, n):
        if self.pos + n > len(self.data):
            raise ValueError("read past the end of a chunk")
        out = self.data[self.pos:self.pos + n]
        self.pos += n
        return out

    def guid(self): return self.take(16)
    def u8(self): return self.take(1)[0]
    def u16(self): return struct.unpack("<H", self.take(2))[0]
    def u32(self): return struct.unpack("<I", self.take(4))[0]
    def i32(self): return struct.unpack("<i", self.take(4))[0]
    def f32(self): return struct.unpack("<f", self.take(4))[0]
    def left(self): return len(self.data) - self.pos

    def fixed(self):
        """Array of fixed-size elements: u16 count*2+1, u16 element size (absent when the count is 0)."""
        head = self.u16()
        count = head >> 1
        if count == 0:
            return []
        if not head & 1:
            raise ValueError("expected an array of fixed-size elements")
        size = self.u16()
        return [self.take(size) for _ in range(count)]

    def variable(self):
        """Array of variable-size elements: u16 count*2, each element led by its u16 size."""
        head = self.u16()
        if head & 1:
            raise ValueError("expected an array of variable-size elements")
        return [self.take(self.u16()) for _ in range(head >> 1)]

    def text(self):
        return self.take(self.u16()).decode("utf-8", "replace")


def guid_text(guid):
    """The way FMOD prints a GUID: {d3 d2 d1 d0-d5 d4-d7 d6-d8 d9-d10..d15}."""
    a, b, c = struct.unpack_from("<IHH", guid)
    return "{%08x-%04x-%04x-%s-%s}" % (a, b, c, guid[8:10].hex(), guid[10:16].hex())


def riff_chunks(f, start, end, path=()):
    """Every leaf chunk as (path of list ids + chunk id, data offset, data size); lists are walked, not returned."""
    pos = start
    while pos + 8 <= end:
        f.seek(pos)
        tag, size = struct.unpack("<4sI", f.read(8))
        if tag in (b"RIFF", b"LIST"):
            form = f.read(4)
            yield from riff_chunks(f, pos + 12, min(end, pos + 8 + size), path + (form,))
        else:
            yield path + (tag,), pos + 8, size
        pos += 8 + size + (size & 1)


# ---- FSB5 ----------------------------------------------------------------------------------------------------------

def read_fsb5(f, offset):
    f.seek(offset)
    head = f.read(28)
    magic, version, count, headers_size, names_size, data_size, mode = struct.unpack("<4sIIIIII", head)
    if magic != b"FSB5":
        raise ValueError("no FSB5 at %d" % offset)
    base = 60 if version == 1 else 64
    f.seek(offset + base)
    headers = f.read(headers_size)
    names = f.read(names_size)
    r = Reader(headers)
    samples = []
    for index in range(count):
        word = struct.unpack("<Q", r.take(8))[0]
        more = word & 1
        rate = FSB_RATES.get((word >> 1) & 15, 0)
        channels = FSB_CHANNELS[(word >> 5) & 3]
        data_offset = ((word >> 7) & 0x7FFFFFF) << 5
        length = (word >> 34) & 0x3FFFFFFF
        loop = None
        while more:
            chunk = r.u32()
            more = chunk & 1
            size = (chunk >> 1) & 0xFFFFFF
            kind = (chunk >> 25) & 0x7F
            body = r.take(size)
            if kind == 1:
                channels = body[0]
            elif kind == 2:
                rate = struct.unpack_from("<I", body)[0]
            elif kind == 3:
                loop = struct.unpack_from("<II", body)
        samples.append({"index": index, "name": "", "rate": rate, "channels": channels, "samples": length,
                        "seconds": length / rate if rate else 0.0, "loop": loop, "data_offset": data_offset})
    if r.left():
        raise ValueError("FSB5 sample headers do not add up")
    if names_size:
        for index in range(count):
            start = struct.unpack_from("<I", names, 4 * index)[0]
            samples[index]["name"] = names[start:names.index(b"\0", start)].decode("utf-8", "replace")
    return {"offset": offset, "length": base + headers_size + names_size + data_size, "version": version,
            "codec": FSB_CODECS.get(mode, str(mode)), "mode": mode, "names_size": names_size,
            "headers_size": headers_size, "data_size": data_size, "samples": samples}


# ---- the strings bank ----------------------------------------------------------------------------------------------

def read_strings(path):
    """master_bank.strings.bank: guid (bytes) -> path ("event:/vo/good/camp_04")."""
    with open(path, "rb") as f:
        data = None
        for chunk, pos, size in riff_chunks(f, 0, os.path.getsize(path)):
            if chunk[-1] == b"STDT" and size:
                f.seek(pos)
                data = f.read(size)
    if data is None:
        raise ValueError("no STDT chunk in " + path)
    r = Reader(data)
    r.u32()                                             # 1 in DD1's file
    nodes = r.fixed()                                   # u24 pool offset, u8 key, u24 first child, u8 children
    guids = r.fixed()
    pool = r.take(r.u16())
    leaves = [int.from_bytes(r.take(3), "little") for _ in range(r.u16())]
    parents = [int.from_bytes(r.take(3), "little") for _ in range(r.u16())]
    if r.left() or len(leaves) != len(guids) or len(parents) != len(nodes):
        raise ValueError("the string table does not add up")

    def piece(node):
        start = int.from_bytes(nodes[node][0:3], "little")
        return "" if start == 0xFFFFFF else pool[start:pool.index(b"\0", start)].decode("utf-8", "replace")

    out = {}
    for index, guid in enumerate(guids):
        parts = []
        node = leaves[index]
        while node != 0xFFFFFF and len(parts) < 64:
            parts.append(piece(node))
            node = parents[node]
        out[guid] = "".join(reversed(parts))
    return out


# ---- a bank's objects ----------------------------------------------------------------------------------------------

class Bank:
    """The PROJ list of one bank file, parsed. The sample data is never read."""

    def __init__(self, path):
        self.path = path
        self.name = os.path.splitext(os.path.basename(path))[0]
        self.version = 0
        self.fsbs = []              # (offset, length)
        self.events = {}            # guid -> dict
        self.timelines = {}
        self.transitions = {}       # timeline guid -> [dict]
        self.instruments = {}       # guid -> dict (kind: wave, multi, scatter, event)
        self.waves = {}             # guid -> (fsb index, sample index, flags)
        self.buses = {}             # guid -> {"output": guid, "volume": dB}
        self.params = {}            # guid -> {"name", "min", "max"}
        self.layouts = {}           # guid -> {"param": guid, "event": guid, "instruments": [...]}
        self.snapshots = {}         # guid -> [(target guid, property, value)]
        self.controllers = {}       # guid -> {"target", "curve", "property"}
        self.curves = {}            # guid -> [(x as float, x as position, y)]
        self.driven = {}            # timeline or layout guid -> [controller guid]
        self.errors = []
        self._read()

    def _read(self):
        size = os.path.getsize(self.path)
        with open(self.path, "rb") as f:
            current = None          # the object whose sibling chunks (INST, PLST, BUS) are being read
            timeline = None         # the timeline or the parameter layout whose TRNB / CTRO chunks follow
            for chunk, pos, length in riff_chunks(f, 0, size):
                tag = chunk[-1]
                if tag == b"SND ":
                    continue        # sample data: SNDH says where the FSB5s are
                f.seek(pos)
                body = f.read(length)
                try:
                    current, timeline = self._chunk(tag, body, current, timeline)
                except (ValueError, struct.error, IndexError) as e:
                    self.errors.append("%s: %s" % (tag.decode("latin1").strip(), e))

    def _chunk(self, tag, body, current, timeline):
        r = Reader(body)
        if tag == b"FMT ":
            self.version = r.u32()
        elif tag == b"SNDH":
            self.fsbs = [struct.unpack("<II", e[:8]) for e in r.fixed()]
        elif tag == b"EVTB":
            guid = r.guid()
            event = {"guid": guid, "snapshot": r.guid(), "timeline": r.guid(), "input": r.guid(), "master": r.guid(),
                     "max_instances": r.i32(), "priority": r.u32()}
            r.u8()
            r.u32()
            event["layouts"] = r.fixed()
            self.events[guid] = event
        elif tag == b"TLNB":
            guid = r.guid()
            timeline = guid
            line = {"guid": guid, "event": r.guid(), "async": [], "locked": [], "markers": [], "tempo": []}
            for key in ("async", "locked"):
                for e in r.fixed():
                    start, length = struct.unpack_from("<II", e, 16)
                    line[key].append({"instrument": e[:16], "start": start, "length": length})
            r.fixed()               # always empty in DD1's banks
            for e in r.variable():
                m = Reader(e)
                line["markers"].append({"guid": m.guid(), "position": m.u32(), "name": m.text()})
            for e in r.fixed():
                line["tempo"].append({"position": struct.unpack_from("<I", e, 16)[0], "bpm": struct.unpack_from("<f", e, 28)[0],
                                      "signature": struct.unpack_from("<II", e, 16)})
            self.timelines[guid] = line
        elif tag == b"TRNB":
            guid = r.guid()
            tran = {"guid": guid, "destination": r.guid(), "start": r.u32(), "end": r.u32(), "conditions": []}
            for e in r.fixed():
                low, high = struct.unpack_from("<ff", e, 16)
                tran["conditions"].append({"param": e[:16], "min": low, "max": high})
            r.take(8)
            tran["chance"] = r.f32()
            tran["priority"] = r.u32()
            self.transitions.setdefault(timeline, []).append(tran)
        elif tag == b"WAIB":
            guid = r.guid()
            current = {"kind": "wave", "guid": guid, "wave": r.guid()}
            self.instruments[guid] = current
        elif tag == b"MUIB":
            guid = r.guid()
            current = {"kind": "multi", "guid": guid, "entries": []}
            self.instruments[guid] = current
        elif tag == b"SPIB":
            guid = r.guid()
            current = {"kind": "scatter", "guid": guid, "polyphony": r.u32(), "spawn_total": r.u32(),
                       "interval_min": r.f32(), "interval_max": r.f32(), "entries": []}
            self.instruments[guid] = current
        elif tag == b"EVIB":
            guid = r.guid()
            current = {"kind": "event", "guid": guid, "event": r.guid()}
            self.instruments[guid] = current
        elif tag == b"PLST" and current is not None and "entries" in current:
            current["mode"] = r.u32()
            r.u32()
            for e in r.fixed():
                current["entries"].append({"instrument": e[:16], "weight": struct.unpack_from("<f", e, 16)[0]})
        elif tag == b"INST" and current is not None and current.get("kind"):
            current["owner"] = r.guid()
            current["volume"] = r.f32()
            current["pitch"] = r.f32()
            r.i32()
            current["loop"] = r.u8() != 0
            r.take(52)
            r.u16()
            current["track"] = r.guid()
        elif tag == b"WAV ":
            guid = r.guid()
            r.u16()
            self.waves[guid] = (r.u32(), r.u32(), r.u32())
        elif tag in (b"IBSB", b"MBSB", b"GBSB", b"RBSB"):
            guid = r.guid()
            r.u16()
            current = {"bus": tag.decode()[0], "guid": guid, "output": r.guid(), "volume": 0.0}
            self.buses[guid] = current
        elif tag == b"BUS " and current is not None and "bus" in current:
            r.u8()
            r.u32()
            r.fixed()               # effects before the fader
            r.fixed()               # effects after it
            r.u16()
            current["volume"] = r.f32()
        elif tag == b"PRMB":
            guid = r.guid()
            r.take(5)
            param = {"name": r.text(), "min": r.f32(), "max": r.f32()}
            r.take(8)
            param["seek"] = r.f32()
            self.params[guid] = param
            current = None
        elif tag == b"PMLB":
            guid = r.guid()
            layout = {"param": r.guid(), "event": r.guid(), "instruments": []}
            for e in r.fixed():
                start, length = struct.unpack_from("<ff", e, 16)
                layout["instruments"].append({"instrument": e[:16], "start": start, "length": length})
            self.layouts[guid] = layout
            current = None
            timeline = guid
        elif tag == b"SNAB":
            guid = r.guid()
            r.u32()
            self.snapshots[guid] = [(e[4:20], struct.unpack_from("<I", e, 20)[0], struct.unpack_from("<f", e, 24)[0]) for e in r.fixed()]
            current = None
        elif tag == b"CTRO" and timeline is not None:
            self.driven.setdefault(timeline, []).extend(r.fixed())
        elif tag == b"CTRL":
            guid = r.guid()
            self.controllers[guid] = {"target": r.guid(), "curve": r.guid(), "property": r.u32()}
            current = None
        elif tag == b"CURV":
            guid = r.guid()
            r.guid()
            self.curves[guid] = [(struct.unpack_from("<f", e)[0], struct.unpack_from("<I", e)[0], struct.unpack_from("<f", e, 4)[0]) for e in r.fixed()]
            current = None
        elif tag in (b"BEFB", b"SEFB", b"SCEF", b"VCAB", b"MODB", b"MAP "):
            current = None          # effects, modulators, mappings: not followed
        return current, timeline


class Project:
    """Every bank of an install, with the names."""

    def __init__(self, dd1):
        self.dd1 = dd1
        audio = os.path.join(dd1, "audio")
        self.names = read_strings(os.path.join(audio, "master_banks", "master_bank.strings.bank"))
        self.by_path = {path: guid for guid, path in self.names.items()}
        files = sorted(glob.glob(os.path.join(audio, "master_banks", "*.bank")) + glob.glob(os.path.join(audio, "secondary_banks", "*.bank")))
        files += sorted(glob.glob(os.path.join(dd1, "dlc", "*", "audio", "secondary_banks", "*.bank")))
        files += sorted(glob.glob(os.path.join(dd1, "dlc", "*", "features", "*", "audio", "secondary_banks", "*.bank")))
        self.banks = []
        seen = set()
        for path in files:
            name = os.path.basename(path)
            if name.endswith(".strings.bank") or name in seen:
                continue            # a DLC ships its hero's bank again (musketeer, flagellant, shieldbreaker)
            seen.add(name)
            self.banks.append(Bank(path))
        self.buses = {}
        for bank in self.banks:
            for guid, bus in bank.buses.items():
                self.buses.setdefault(guid, bus)
        self._fsb_cache = {}

    def bank(self, name):
        name = name.lower().replace(".bank", "")
        for bank in self.banks:
            if bank.name == name:
                return bank
        return None

    def name(self, guid):
        return self.names.get(guid) or guid_text(guid)

    def find_event(self, path):
        """"/vo/good/camp_04", "vo/good/camp_04" or "event:/vo/good/camp_04" -> (bank, event); the first bank that holds it."""
        if ":/" not in path:
            path = "event:/" + path.lstrip("/")
        guid = self.by_path.get(path)
        return self.event_by_guid(guid) if guid else (None, None)

    def event_by_guid(self, guid, prefer=None):
        if prefer is not None and guid in prefer.events:
            return prefer, prefer.events[guid]
        for bank in self.banks:
            if guid in bank.events:
                return bank, bank.events[guid]
        return None, None

    def fsb(self, bank, index):
        key = (bank.path, index)
        if key not in self._fsb_cache:
            with open(bank.path, "rb") as f:
                self._fsb_cache[key] = read_fsb5(f, bank.fsbs[index][0])
        return self._fsb_cache[key]

    def sample(self, bank, wave):
        fsb_index, sample_index, flags = bank.waves[wave]
        sample = self.fsb(bank, fsb_index)["samples"][sample_index]
        return {"bank": bank.name, "fsb": fsb_index, "index": sample_index, "name": sample["name"], "seconds": sample["seconds"],
                "rate": sample["rate"], "channels": sample["channels"], "streamed": bool(flags & 2)}

    def route(self, bank, event):
        """The faders between an event and the master bus: [(bus name, dB)], the event's own track first."""
        out = []
        guid = event["master"]
        seen = set()
        while guid != ZERO and guid not in seen:
            seen.add(guid)
            bus = bank.buses.get(guid) or self.buses.get(guid)
            if bus is None:
                break
            label = self.names.get(guid) or {"M": "master track", "I": "input", "G": "group", "R": "return"}[bus["bus"]]
            out.append((label, bus["volume"]))
            guid = bus["output"] if bus["output"] != guid else ZERO
            if bus["bus"] == "M" and guid == ZERO and event["input"] != ZERO and event["input"] not in seen:
                guid = event["input"]
        return out

    # ---- what an event plays ----

    def describe(self, bank, event, depth=0, seen=None):
        """An event as plain data: timeline boxes with their instruments resolved down to samples."""
        seen = seen or set()
        line = bank.timelines.get(event["timeline"])
        out = {"path": self.names.get(event["guid"], ""), "guid": guid_text(event["guid"]), "bank": bank.name,
               "snapshot": event["snapshot"] != ZERO, "max_instances": event["max_instances"],
               "route": self.route(bank, event), "async": [], "locked": [], "markers": [], "transitions": [], "params": [], "layouts": [],
               "automation": []}
        if event["snapshot"] != ZERO:
            out["sets"] = [(self.name(target), prop, value) for target, prop, value in (bank.snapshots.get(event["snapshot"]) or self.snapshot(event["snapshot"]))]
        if line is None or event["guid"] in seen:
            return out
        seen = seen | {event["guid"]}
        for key in ("async", "locked"):
            for box in line[key]:
                out[key].append({"start": box["start"], "length": box["length"], "instrument": self.instrument(bank, box["instrument"], seen)})
        positions = {m["guid"]: m["position"] for m in line["markers"]}
        out["markers"] = [{"name": m["name"], "position": m["position"]} for m in line["markers"] if m["name"]]
        names = {m["guid"]: m["name"] for m in line["markers"]}
        for tran in sorted(bank.transitions.get(line["guid"], []), key=lambda t: (t["start"], t["priority"])):
            out["transitions"].append({"at": tran["start"], "end": tran["end"], "to": positions.get(tran["destination"]),
                                       "marker": names.get(tran["destination"], ""), "loop": tran["destination"] == tran["guid"],
                                       "chance": tran["chance"], "priority": tran["priority"],
                                       "conditions": [{"param": (bank.params.get(c["param"]) or {}).get("name", "?"), "min": c["min"], "max": c["max"]} for c in tran["conditions"]]})
        out["automation"] = self.automation(bank, "timeline", line["guid"], True)
        for guid in event["layouts"]:
            layout = bank.layouts.get(guid)
            if layout is None:
                continue
            param = bank.params.get(layout["param"]) or {"name": "?", "min": 0.0, "max": 0.0, "seek": 0.0}
            out["params"].append(param)
            out["automation"] += self.automation(bank, param["name"], guid, False)
            if layout["instruments"]:
                out["layouts"].append({"param": param["name"], "instruments": [
                    {"start": box["start"], "length": box["length"], "instrument": self.instrument(bank, box["instrument"], seen)} for box in layout["instruments"]]})
        return out

    def automation(self, bank, driver, owner, by_position):
        """The controllers a timeline or a parameter drives: what they move and along which curve."""
        out = []
        for guid in bank.driven.get(owner, []):
            ctrl = bank.controllers.get(guid)
            if ctrl is None:
                continue
            target = ctrl["target"]
            if target in bank.instruments:
                kind = bank.instruments[target]["kind"]
            elif target in bank.buses:
                kind = {"M": "master track", "I": "input", "G": "track", "R": "return"}[bank.buses[target]["bus"]]
            else:
                kind = "effect"
            points = [((x_pos / TIMELINE_RATE) if by_position else x, y) for x, x_pos, y in bank.curves.get(ctrl["curve"], [])]
            out.append({"driver": driver, "target": guid_text(target), "kind": kind, "property": ctrl["property"], "points": points})
        return out

    def track(self, bank, guid):
        """An instrument's audio track and the tracks above it, up to (not including) the event's master track."""
        out = []
        seen = set()
        while guid != ZERO and guid not in seen and guid in bank.buses and bank.buses[guid]["bus"] == "G":
            seen.add(guid)
            out.append({"guid": guid_text(guid), "volume": bank.buses[guid]["volume"]})
            guid = bank.buses[guid]["output"]
        return out

    def snapshot(self, guid):
        for bank in self.banks:
            if guid in bank.snapshots:
                return bank.snapshots[guid]
        return []

    def instrument(self, bank, guid, seen):
        inst = bank.instruments.get(guid)
        if inst is None:
            return {"kind": "missing", "guid": guid_text(guid)}
        out = {"kind": inst["kind"], "guid": guid_text(guid), "volume": inst.get("volume", 0.0), "pitch": inst.get("pitch", 0.0),
               "loop": inst.get("loop", False), "track": self.track(bank, inst.get("track", ZERO))}
        if inst["kind"] == "wave":
            out["sample"] = self.sample(bank, inst["wave"]) if inst["wave"] in bank.waves else None
        elif inst["kind"] in ("multi", "scatter"):
            out["mode"] = inst.get("mode", 0)
            out["entries"] = [{"weight": e["weight"], "instrument": self.instrument(bank, e["instrument"], seen)} for e in inst["entries"]]
            if inst["kind"] == "scatter":
                out.update(polyphony=inst["polyphony"], spawn_total=inst["spawn_total"], interval=(inst["interval_min"], inst["interval_max"]))
        elif inst["kind"] == "event":
            target_bank, target = self.event_by_guid(inst["event"], bank)
            out["event"] = self.describe(target_bank, target, 0, seen) if target else {"path": self.name(inst["event"]), "missing": True}
        return out


def samples_of(node, out=None):
    """Every sample an event description can reach."""
    out = [] if out is None else out
    if isinstance(node, dict):
        if node.get("kind") == "wave" and node.get("sample"):
            out.append(node["sample"])
        for value in node.values():
            samples_of(value, out)
    elif isinstance(node, (list, tuple)):
        for value in node:
            samples_of(value, out)
    return out


def shape(desc):
    """One word for how an event is built, and the count of samples behind it."""
    samples = samples_of(desc)
    if desc["snapshot"]:
        return "snapshot", 0
    boxes = desc["async"] + desc["locked"]
    kinds = [b["instrument"]["kind"] for b in boxes]
    if desc["layouts"] and not boxes:
        return "by parameter", len(samples)
    if not boxes:
        return "empty", 0
    if len(boxes) == 1 and kinds[0] == "wave":
        return ("loop" if boxes[0]["instrument"]["loop"] else "one sample"), len(samples)
    if len(boxes) == 1 and kinds[0] == "multi":
        return "pool", len(samples)
    if any(not t["loop"] for t in desc["transitions"]):
        return "sequenced", len(samples)
    if "scatter" in kinds or any(b["instrument"]["loop"] for b in boxes) or any(t["loop"] for t in desc["transitions"]):
        return "bed", len(samples)
    return "layered", len(samples)


# ---- printing ------------------------------------------------------------------------------------------------------

def seconds(position):
    return position / TIMELINE_RATE


def print_instrument(inst, indent, automated=None):
    pad = "  " * indent
    extra = ""
    if inst.get("volume"):
        extra += " %+.1f dB" % inst["volume"]
    if inst.get("pitch"):
        extra += " pitch %+.1f" % inst["pitch"]
    if inst.get("loop"):
        extra += " loop"
    track = sum(t["volume"] for t in inst.get("track", []))
    if track:
        extra += " track %+.1f dB" % track
    if automated and (inst.get("guid") in automated or any(t["guid"] in automated for t in inst.get("track", []))):
        extra += " (automated)"
    if inst["kind"] == "wave":
        s = inst["sample"]
        if s is None:
            print(pad + "wave (asset not in this bank)" + extra)
        else:
            print(pad + "wave %s  [%s fsb %d #%d, %.2f s, %d Hz, %d ch%s]%s" % (
                s["name"], s["bank"], s["fsb"], s["index"], s["seconds"], s["rate"], s["channels"], ", streamed" if s["streamed"] else "", extra))
    elif inst["kind"] in ("multi", "scatter"):
        head = "pool" if inst["kind"] == "multi" else "scatterer every %.2f..%.2f s, polyphony %d%s" % (
            inst["interval"][0], inst["interval"][1], inst["polyphony"], ", %d spawns" % inst["spawn_total"] if inst["spawn_total"] else "")
        print(pad + "%s of %d (mode %d)%s" % (head, len(inst["entries"]), inst["mode"], extra))
        total = sum(e["weight"] for e in inst["entries"]) or 1.0
        for e in inst["entries"]:
            print(pad + "  %4.1f%%" % (100.0 * e["weight"] / total))
            print_instrument(e["instrument"], indent + 2, automated)
    elif inst["kind"] == "event":
        print(pad + "event " + (inst["event"].get("path") or inst["event"].get("guid", "")) + extra)
        if not inst["event"].get("missing"):
            print_event(inst["event"], indent + 1, brief=True)
    else:
        print(pad + inst["kind"])


def print_event(desc, indent=0, brief=False):
    pad = "  " * indent
    if not brief:
        print(pad + "%s  %s  in %s.bank  (%s, %d samples)" % ((desc["path"], desc["guid"], desc["bank"]) + shape(desc)))
        print(pad + "  route: " + " -> ".join("%s %+.1f dB" % (name, db) for name, db in desc["route"]) +
              "   = %+.1f dB" % sum(db for _, db in desc["route"]))
        if desc["max_instances"] != 0x7FFFFFFF:
            print(pad + "  max instances: %d" % desc["max_instances"])
    if desc["snapshot"]:
        for target, prop, value in desc.get("sets", []):
            print(pad + "  sets %s property %d to %.1f" % (target, prop, value))
        return
    automated = set(a["target"] for a in desc["automation"])
    for param in desc["params"]:
        print(pad + "  parameter %s %.1f..%.1f, moves %.2f a second" % (param["name"], param["min"], param["max"], param["seek"]))
    for key in ("async", "locked"):
        for box in sorted(desc[key], key=lambda b: b["start"]):
            print(pad + "  %s %8.2f s .. %8.2f s" % (key, seconds(box["start"]), seconds(box["start"] + box["length"])))
            print_instrument(box["instrument"], indent + 2, automated)
    for layout in desc["layouts"]:
        for box in layout["instruments"]:
            print(pad + "  on %s %.2f .. %.2f" % (layout["param"], box["start"], box["start"] + box["length"]))
            print_instrument(box["instrument"], indent + 2, automated)
    for a in desc["automation"]:
        what = {0: "volume", 1: "pitch", 3: "intensity"}.get(a["property"], "property %d" % a["property"])
        print(pad + "  %s moves the %s of %s %s: %s" % (a["driver"], what, a["kind"], a["target"][1:9],
                                                    ", ".join("%.2f -> %.2f" % point for point in a["points"])))
    for marker in sorted(desc["markers"], key=lambda m: m["position"]):
        print(pad + "  marker %-10s at %8.2f s" % (marker["name"], seconds(marker["position"])))
    for t in desc["transitions"]:
        to = "?" if t["to"] is None else "%.2f s" % seconds(t["to"])
        cond = "".join(" if %s in %.1f..%.1f" % (c["param"], c["min"], c["max"]) for c in t["conditions"])
        print(pad + "  at %8.2f s %s %s%s, chance %.1f%%, priority %d" % (
            seconds(t["at"]), "loop to" if t["loop"] else "go to", (t["marker"] + " " if t["marker"] else "") + "(" + to + ")", cond, t["chance"], t["priority"]))


# ---- commands ------------------------------------------------------------------------------------------------------

def cmd_banks(project, args):
    print("%-28s %4s %6s %6s %6s %5s  FSB5s (offset, length, codec, samples)" % ("bank", "fmt", "events", "instr", "waves", "buses"))
    for bank in project.banks:
        fsbs = []
        for index in range(len(bank.fsbs)):
            fsb = project.fsb(bank, index)
            ok = "" if fsb["length"] == bank.fsbs[index][1] else " LENGTH MISMATCH"
            fsbs.append("@%d +%d %s x%d%s" % (fsb["offset"], fsb["length"], fsb["codec"], len(fsb["samples"]), ok))
        print("%-28s %4d %6d %6d %6d %5d  %s" % (bank.name, bank.version, len(bank.events), len(bank.instruments), len(bank.waves), len(bank.buses), "; ".join(fsbs)))
        for error in bank.errors[:5]:
            print("    ! " + error)
    print("%d banks, %d names in the strings bank" % (len(project.banks), len(project.names)))


def cmd_samples(project, args):
    bank = project.bank(args.bank)
    if bank is None:
        sys.exit("no such bank: " + args.bank)
    used = collections.defaultdict(list)
    for guid, event in bank.events.items():
        for s in samples_of(project.describe(bank, event)):
            if s["bank"] == bank.name:
                used[(s["fsb"], s["index"])].append(project.names.get(guid, guid_text(guid)))
    for index in range(len(bank.fsbs)):
        fsb = project.fsb(bank, index)
        print("FSB5 %d at %d, %d bytes, %s, %d samples, name table %d bytes" % (index, fsb["offset"], fsb["length"], fsb["codec"], len(fsb["samples"]), fsb["names_size"]))
        for s in fsb["samples"]:
            if args.filter and args.filter.lower() not in s["name"].lower():
                continue
            events = sorted(set(used.get((index, s["index"]), [])))
            print("  %4d %-46s %6d Hz %d ch %8.2f s  %s" % (s["index"], s["name"], s["rate"], s["channels"], s["seconds"],
                                                           ", ".join(e.replace("event:", "") for e in events[:3]) + (" ..." if len(events) > 3 else "")))


def cmd_events(project, args):
    prefix = args.prefix or ""
    if prefix and ":/" not in prefix:
        prefix = "event:/" + prefix.lstrip("/")
    counts = collections.Counter()
    for path in sorted(project.by_path):
        if not path.startswith(prefix or "event:/"):
            continue
        bank, event = project.event_by_guid(project.by_path[path])
        if event is None:
            print("%-64s (in no bank)" % path)
            counts["in no bank"] += 1
            continue
        kind, n = shape(project.describe(bank, event))
        counts[kind] += 1
        print("%-64s %-22s %-12s %d" % (path, bank.name, kind, n))
    print(", ".join("%d %s" % (n, kind) for kind, n in counts.most_common()))


def cmd_event(project, args):
    bank, event = project.find_event(args.path)
    if event is None:
        sys.exit("no such event: " + args.path)
    print_event(project.describe(bank, event))


def narration_files(dd1):
    files = [os.path.join(dd1, "audio", "narration.json")]
    files += sorted(glob.glob(os.path.join(dd1, "dlc", "*", "audio", "*.narration.json")))
    return [f for f in files if os.path.exists(f)]


def cmd_coverage(project, args):
    by_name = collections.Counter()
    for path in narration_files(project.dd1):
        with open(path, encoding="utf-8-sig") as f:
            table = json.load(f)
        lines = []
        for entry in table.get("entries", []):
            for clip in entry.get("audio_events", []):
                if clip.get("audio_event"):
                    lines.append((entry["id"], clip["audio_event"]))
        paths = sorted(set(p for _, p in lines))
        resolved, unnamed, unplayable, shapes = [], [], [], collections.Counter()
        for p in paths:
            bank, event = project.find_event(p)
            if event is None:
                unnamed.append(p)
                continue
            desc = project.describe(bank, event)
            found = samples_of(desc)
            kind, _ = shape(desc)
            shapes[kind] += 1
            if not found:
                unplayable.append(p)
                continue
            resolved.append(p)
            # how far the sample's name is from the event's: "/vo/good/camp_04" against "vo_narr_good_camp_04"
            plain = "vo_narr_" + p.strip("/").split("/", 1)[1].replace("/", "_") if p.startswith("/vo/") else ""
            by_name["same" if found[0]["name"] == plain else "different"] += 1
        print("%s" % os.path.relpath(path, project.dd1))
        print("  %d entries with a clip, %d distinct events: %d resolve to a sample, %d have no event in the banks, %d have an event without a sample"
              % (len(lines), len(paths), len(resolved), len(unnamed), len(unplayable)))
        print("  shapes: " + ", ".join("%d %s" % (n, k) for k, n in shapes.most_common()))
        for p in unnamed[:40]:
            print("    no event: " + p)
        for p in unplayable[:40]:
            print("    no sample: " + p)
    print("sample name = 'vo_narr_' + the event path with underscores: %d same, %d different (so names are not the rule; the bank's own graph is)"
          % (by_name["same"], by_name["different"]))


ESTATE = [
    "music/mus_town", "music/mus_exploration", "music/mus_camp",
    "ambience/town/general", "ambience/town/general_2", "ambience/town/abbey", "ambience/town/blacksmith", "ambience/town/camping_trainer",
    "ambience/town/graveyard", "ambience/town/guild", "ambience/town/sanitarium", "ambience/town/statue", "ambience/town/tavern",
    "ambience/dungeon/crypts", "ambience/dungeon/weald", "ambience/dungeon/warrens", "ambience/dungeon/cove",
    "ambience/dungeon/quest/plot_darkest_dungeon_1", "ambience/dungeon/quest/plot_darkest_dungeon_2",
    "ambience/dungeon/quest/plot_darkest_dungeon_3", "ambience/dungeon/quest/plot_darkest_dungeon_4", "ambience/local/campfire",
]


def cmd_estate(project, args):
    paths = list(ESTATE)
    paths += sorted(p[len("event:/"):] for p in project.by_path if p.startswith("event:/town/") or p.startswith("event:/ui/town/") and "/pit_" not in p or p.startswith("event:/ui/shared/"))
    for path in paths:
        bank, event = project.find_event(path)
        if event is None:
            print("%s: not in the banks\n" % path)
            continue
        if args.brief:
            desc = project.describe(bank, event)
            kind, n = shape(desc)
            names = sorted(set(s["name"] for s in samples_of(desc)))
            print("%-44s %-10s %-10s %3d  %+5.1f dB  %s" % (path, bank.name, kind, n, sum(db for _, db in desc["route"]), ", ".join(names[:4]) + (" ..." if len(names) > 4 else "")))
        else:
            print_event(project.describe(bank, event))
            print()


def cmd_map(project, args):
    out = {"banks": {}, "events": {}}
    for bank in project.banks:
        out["banks"][bank.name] = {"file": os.path.relpath(bank.path, project.dd1).replace("\\", "/"), "format": bank.version,
                                   "fsbs": [{"offset": o, "length": n} for o, n in bank.fsbs]}
    for path in sorted(project.by_path):
        if not path.startswith("event:/"):
            continue
        bank, event = project.event_by_guid(project.by_path[path])
        if event is not None:
            out["events"][path] = project.describe(bank, event)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)
    print("%d banks, %d events -> %s" % (len(out["banks"]), len(out["events"]), args.out))


def main():
    ap = argparse.ArgumentParser(description="DD1's sound banks: samples, events and what plays what.")
    ap.add_argument("--dd1", default=os.environ.get("DD1_GAME_DIR", DEFAULT_DD1_DIR), help="the Darkest Dungeon (1) install")
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("banks")
    p = sub.add_parser("samples")
    p.add_argument("bank")
    p.add_argument("filter", nargs="?")
    p = sub.add_parser("events")
    p.add_argument("prefix", nargs="?")
    p = sub.add_parser("event")
    p.add_argument("path")
    sub.add_parser("coverage")
    p = sub.add_parser("estate")
    p.add_argument("--brief", action="store_true")
    p = sub.add_parser("map")
    p.add_argument("out")
    args = ap.parse_args()
    if not os.path.exists(os.path.join(args.dd1, "audio", "master_banks", "master_bank.strings.bank")):
        sys.exit("no DD1 install at %s: pass --dd1 <dir> or set DD1_GAME_DIR" % args.dd1)
    project = Project(args.dd1)
    {"banks": cmd_banks, "samples": cmd_samples, "events": cmd_events, "event": cmd_event, "coverage": cmd_coverage,
     "estate": cmd_estate, "map": cmd_map}[args.cmd](project, args)


if __name__ == "__main__":
    main()
