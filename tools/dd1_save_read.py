#!/usr/bin/env python3
"""Reads a Darkest Dungeon (1) save file and prints it as JSON-like text. READ-ONLY: it never writes into the
save folder (a dump goes to stdout, or to the file given with --out, which may not be under Steam's folders).

    python tools/dd1_save_read.py <persist.X.json>                 one file as text
    python tools/dd1_save_read.py <profile folder> --summary       what the estate is: name, week, mode, town, roster
    python tools/dd1_save_read.py <remote folder> --slots          one line per profile_N (add --full for everything)
    python tools/dd1_save_read.py <file> --names <DD1 install>     also turns name hashes back into names
    python tools/dd1_save_read.py <file> --raw                     also shows the header and the two tables

Where the saves are: with Steam, <Steam>\\userdata\\<account>\\262060\\remote\\profile_0 .. profile_8 (nine campaign
slots; profile_9 is the Butcher's Circus); without Steam (-disablesteam, or the exe in _windowsnosteam),
%USERPROFILE%\\Documents\\Darkest\\profile_N. The options (resolution, fullscreen) are plain JSON in
Documents\\Darkest\\persist.options.json either way.

A `persist.*.json` is one of two things, and the game loads both:
  * plain JSON text `{"version": N, "data": {...}}`: this is what scripts/starting_save holds, a new estate gets
    those files copied into its profile as they are, and the game keeps a text file until it next saves it;
  * a binary container ("01 B1 00 00"), little endian, four parts:

  header, 64 bytes
    +00 magic 01 B1 00 00        +04 revision            +08 header length (0x40)   +0C zero
    +10 object table size        +14 object count        +18 object table offset    +1C 16 zero bytes
    +2C field count              +30 field table offset  +34 zero
    +38 data length              +3C data offset
  object table, 16 bytes each
    parent object index (-1 for the root), index of the object's own field, direct children, all children
  field table, 12 bytes each, in the order the fields are written (a depth-first walk of the tree)
    hash of the name, offset of the field inside the data part, info:
      bit 0        the field is an object (it has children and no data of its own)
      bits 2..10   length of the name with its closing zero
      bits 11..30  for an object: its index in the object table
  data
    for each field: its name (zero terminated), then its value. A value longer than one byte starts at
    the next multiple of four (counted from the start of the data part). The length of a value is not
    stored: it is what is left before the next field's name. The TYPE is not stored either: the game
    knows it from the name. This reader guesses it from the size and the bytes:
      1 byte            bool (0/1) or a char ("requirement_code": "a")
      4 bytes           int, or a float when the name is a known float or the bits only make sense as one
      n = int, n bytes  a string of n bytes with its closing zero
      n = int, 4n bytes a list of n ints (often name hashes)
      n = int, then n strings each (int length, bytes, padded to four)   a list of strings
      n = int, then a whole save file ("01 B1 ...") of n bytes           an embedded file (each hero of
                        persist.roster.json is one: heroes.<id>.hero_file_data.raw_data), read the same way
      anything else     shown as ints or hex
  An object's children are the fields that follow it; the object table says how many are direct ones.

Hashes: the hash of a name is h = h * 53 + byte over its UTF-8 bytes, kept in 32 bits. The same hash is
stored as an int VALUE wherever the game saves an id (an upgrade tree, a quest, a tutorial event): with
--names the reader collects every word of the game's data files and shows the word beside the int.

Where the things of the summary live:
    persist.game.json          estatename, game_mode ("base" Darkest, "radiant", "bloodmoon" Stygian/Bloodmoon,
                               "new_game_plus"), inraid/raiddungeon, totalelapsed (seconds, float), date_time,
                               applied_ugcs_1_0 (the estate's mods: name = <Title> of a local mod's project.xml or a
                               Workshop id, source = mod_local_source | Steam), dlc
    persist.campaign_log.json  total_weeks
    persist.estate.json        wallet.<n>.{type, amount} (gold, bust, portrait, deed, crest, shard, memory, blueprint),
                               trinkets.items
    persist.upgrades.json      purchases.<n>.{tree_id (hash of "abbey.prayer", "crusader.smite" ...), requirement_code
                               (a char: "a".."f" for a building's levels, "0".."4" for a hero's), instance_number (0 for
                               the town, a hero's roster id for a hero's own trees), is_purchased}
    persist.roster.json        heroes.<id>.hero_file_data.raw_data = an embedded save: actor.name, heroClass, resolveXp,
                               m_Stress, weapon_rank, armour_rank, quirks.<id>, roster.status, roster.building_name
    persist.progression.json   dungeon.<id>.xp, total_quests_finished, total_successful_quests_finished
    persist.quest.json         quests.<n>.{id, dungeon, type, difficulty, length}: what the map offers this week
    persist.town.json          buildings.<id>.{activities, store}
    persist.tutorial.json      dispatched_events: hashes of the tutorial popups already shown
"""
import json
import os
import re
import struct
import sys

MAGIC = b"\x01\xb1\x00\x00"

# names the game stores as floats (the rest is told by the bits)
FLOAT_NAMES = {
    "current_hp", "m_Stress", "amount", "percent", "chance", "stress", "hp", "torchlight", "totalelapsed",
    "heirloom_gain_multiplier", "scoutChance_all", "scoutModifier_all", "start_elapsed_time",
}

# the town's upgrade trees of the base game (upgrades/building/*.upgrades.json) and how many levels each has
TOWN_TREES = {
    "abbey.meditation": 6, "abbey.prayer": 6, "abbey.flagellation": 6,
    "blacksmith.weapon": 4, "blacksmith.armour": 4, "blacksmith.cost": 5,
    "camping_trainer.cost": 5,
    "guild.skill_levels": 4, "guild.cost": 5,
    "nomad_wagon.numitems": 4, "nomad_wagon.cost": 5,
    "sanitarium.cost": 5, "sanitarium.disease_quirk_cost": 5, "sanitarium.slots": 4,
    "stage_coach.numrecruits": 5, "stage_coach.rostersize": 5, "stage_coach.upgraded_recruits": 3,
    "tavern.bar": 6, "tavern.gambling": 6, "tavern.brothel": 6,
}
DISEASES = {  # shared/quirk/quirk_library.json, "is_disease": true (base game)
    "bad_humours", "creeping_cough", "hemophilia", "the_runs", "wasting_sickness", "tetanus", "rabies", "syphilis",
    "stomach_cramp", "bulimic", "lethargy", "spotted_fever", "vertigo", "vampiric_spirits", "ennui", "the_ague",
    "hysterical_blindness", "scurvy", "the_worries", "the_fits", "tapeworm", "the_red_plague", "the_black_plague",
}
RESOLVE_XP = [0, 2, 8, 14, 24, 36, 48]            # campaign/roster/roster.variables.json (radiant: 0 2 7 13 21 29 40)
DUNGEON_XP = [0, 2, 6, 10, 16, 22, 32, 42]        # campaign/progression/progression.json


def name_hash(text):
    h = 0
    for b in text.encode("utf-8"):
        h = (h * 53 + b) & 0xFFFFFFFF
    return h


TOWN_TREE_BY_HASH = {name_hash(k): k for k in TOWN_TREES}


class Field(object):
    __slots__ = ("name", "hash", "offset", "info", "is_object", "object_index", "raw", "pad", "children", "parent")

    def __init__(self):
        self.children = []
        self.parent = None
        self.raw = b""
        self.pad = 0


class Save(object):
    """One parsed binary container. `root` is a list of top fields (normally one: base_root)."""

    def __init__(self, data, names=None):
        self.names = names if names is not None else {}
        if data[:4] != MAGIC:
            raise ValueError("not a DD1 binary save (no 01 B1 00 00 at the start)")
        if len(data) < 64:
            raise ValueError("shorter than a header")
        (self.revision, self.header_len, _z0, self.objects_size, self.objects_count, self.objects_offset,
         _z1, _z2, self.fields_count, self.fields_offset, _z3, self.data_len, self.data_offset) = struct.unpack_from(
            "<IIIIIIQQIIIII", data, 4)
        if self.header_len != 64 or self.objects_size != self.objects_count * 16:
            raise ValueError("unexpected header")
        if self.data_offset + self.data_len > len(data):
            raise ValueError("truncated: the data part ends past the end of the file")
        self.objects = [struct.unpack_from("<iiii", data, self.objects_offset + 16 * i) for i in range(self.objects_count)]
        blob = data[self.data_offset:self.data_offset + self.data_len]
        fields = []
        for i in range(self.fields_count):
            h, off, info = struct.unpack_from("<III", data, self.fields_offset + 12 * i)
            f = Field()
            f.hash, f.offset, f.info = h, off, info
            f.is_object = bool(info & 1)
            name_len = (info >> 2) & 0x1FF
            f.object_index = (info >> 11) & 0xFFFFF
            f.name = blob[off:off + name_len - 1].decode("utf-8", "replace")
            fields.append((f, name_len))
        # the value of a field runs to the next field's name (in offset order)
        order = sorted(range(len(fields)), key=lambda i: fields[i][0].offset)
        for n, i in enumerate(order):
            f, name_len = fields[i]
            start = f.offset + name_len
            end = fields[order[n + 1]][0].offset if n + 1 < len(order) else len(blob)
            raw = blob[start:end]
            if len(raw) > 1:  # aligned to four from the start of the data part
                f.pad = (-start) % 4
                raw = raw[f.pad:]
            f.raw = raw
        # the tree: fields are stored depth first; an object owns the next `direct children` subtrees
        self.fields = [f for f, _ in fields]
        self.root = []
        pos = [0]

        def take(parent):
            f = self.fields[pos[0]]
            pos[0] += 1
            f.parent = parent
            if f.is_object:
                if f.object_index >= len(self.objects):
                    raise ValueError("field %r points past the object table" % f.name)
                for _ in range(self.objects[f.object_index][2]):
                    if pos[0] >= len(self.fields):
                        break
                    f.children.append(take(f))
            return f

        while pos[0] < len(self.fields):
            self.root.append(take(None))

    # ---- values -----------------------------------------------------------------------------------------
    def value(self, f):
        """The best guess at a field's value as a Python value."""
        if f.is_object:
            return self.to_dict(f)
        raw = f.raw
        n = len(raw)
        if n == 0:
            return None
        if n == 1:
            return bool(raw[0]) if raw[0] < 2 else chr(raw[0])
        if n == 4:
            return self._number(f.name, raw)
        first = struct.unpack_from("<I", raw, 0)[0]
        if n >= 68 and raw[4:8] == MAGIC and first <= n - 4:
            try:
                return {"<embedded save>": Save(raw[4:4 + first], self.names).to_dict()}
            except (ValueError, struct.error):
                pass
        if first == n - 4 and first >= 1 and raw[-1] == 0:
            body = raw[4:-1]
            if b"\x00" not in body:
                try:
                    return body.decode("utf-8")
                except UnicodeDecodeError:
                    pass
        if n == 4 + 4 * first:
            return [self._number(None, raw[4 + 4 * i:8 + 4 * i], vector=True) for i in range(first)]
        strings = self._strings(raw, first)
        if strings is not None:
            return strings
        if n == 8:
            return {"<two ints>": list(struct.unpack("<ii", raw))}
        if n % 4 == 0 and n <= 64:
            return {"<ints>": list(struct.unpack("<%di" % (n // 4), raw))}
        return {"<%d bytes>" % n: raw[:48].hex() + ("..." if n > 48 else "")}

    def _strings(self, raw, count):
        if not 0 < count <= 4096:
            return None
        out, p = [], 4
        for _ in range(count):
            p += (-p) % 4
            if p + 4 > len(raw):
                return None
            ln = struct.unpack_from("<I", raw, p)[0]
            p += 4
            if ln < 1 or p + ln > len(raw) or raw[p + ln - 1] != 0:
                return None
            try:
                out.append(raw[p:p + ln - 1].decode("utf-8"))
            except UnicodeDecodeError:
                return None
            p += ln
        return out if p == len(raw) else None

    def _number(self, name, raw, vector=False):
        i = struct.unpack("<i", raw)[0]
        u = i & 0xFFFFFFFF
        if u == 0:
            return 0
        if name in FLOAT_NAMES and u > 0x00FFFFFF:              # a small int is an int whatever the name
            return round(struct.unpack("<f", raw)[0], 4)
        if u > 0xFFFF and u in self.names:
            return "%d <%s>" % (i, self.names[u])
        if not vector and not -10000000 <= i <= 100000000:
            # too large for a count, a week or gold: a float if its bits make a tidy one, else an unknown hash
            fl = struct.unpack("<f", raw)[0]
            if fl == fl and 1e-4 <= abs(fl) <= 1e7 and abs(fl * 1000 - round(fl * 1000)) < 1e-2:
                return round(fl, 4)
        return i

    def to_dict(self, f=None):
        kids = self.root if f is None else f.children
        out = {}
        for c in kids:
            key = c.name
            n = 2
            while key in out:  # the game does repeat a name inside one object now and then
                key = "%s#%d" % (c.name, n)
                n += 1
            out[key] = self.value(c)
        return out


def read_tree(path, names=None):
    """A save file as a dict {"base_root": {...}}, binary or text. Raises ValueError when it is neither."""
    with open(path, "rb") as fh:
        data = fh.read()
    if data[:4] == MAGIC:
        return Save(data, names).to_dict()
    try:
        text = json.loads(data.decode("utf-8-sig"))
    except (UnicodeDecodeError, ValueError) as e:
        raise ValueError("neither a binary save nor JSON text: %s" % e)
    if isinstance(text, dict) and isinstance(text.get("data"), dict):
        root = dict(text["data"])
        root.setdefault("version", text.get("version"))
        return {"base_root": root, "<plain text save>": True}
    return {"base_root": text, "<plain text save>": True}


def game_names(install):
    """Every word of the game's text data, by hash: what an int value may be the id of."""
    names = {}
    word = re.compile(rb"[A-Za-z_][A-Za-z0-9_.+\-]{2,63}")
    for base, _dirs, files in os.walk(install):
        low = base.replace("\\", "/").lower() + "/"
        if any(part in low for part in ("/audio/", "/video/", "/fonts/", "/shaders", "/localization/", "/_windows", "/fx/")):
            continue
        for fn in files:
            if not fn.lower().endswith((".json", ".darkest", ".txt")):
                continue
            full = os.path.join(base, fn)
            try:
                if os.path.getsize(full) > 4 * 1024 * 1024:
                    continue
                with open(full, "rb") as fh:
                    text = fh.read()
            except OSError:
                continue
            for m in word.findall(text):
                s = m.decode("ascii")
                names.setdefault(name_hash(s), s)
    return names


# ---- the summary of a profile ----------------------------------------------------------------------------
def _root(folder, name, names, problems):
    path = os.path.join(folder, name)
    if not os.path.exists(path):
        return {}
    try:
        return read_tree(path, names).get("base_root", {})
    except (ValueError, struct.error, OSError) as e:
        problems.append("%s: %s" % (name, e))
        return {}


def _level(xp, table):
    level = 0
    for i, need in enumerate(table):
        if isinstance(xp, (int, float)) and xp >= need:
            level = i
    return level


def summary(folder, names=None):
    """What the files of one profile_N say about the estate. Reads only."""
    names = names if names is not None else {}
    problems = []
    files = sorted(f for f in os.listdir(folder) if os.path.isfile(os.path.join(folder, f)))
    out = {"folder": folder, "file_count": len(files)}
    if not any(f.startswith("persist.") for f in files):
        out["state"] = "FREE (no persist files)"
        return out
    g = _root(folder, "persist.game.json", names, problems)
    if not g and "persist.circus_estate.json" in files:
        out["state"] = "Butcher's Circus (multiplayer) profile, not a campaign slot"
        return out
    out["estate_name"] = g.get("estatename")
    out["game_mode"] = g.get("game_mode", "(not written: the starting save, i.e. still on the Old Road)")
    out["in_raid"] = "%s (%s)" % (g.get("inraid"), g.get("raiddungeon"))
    out["last_saved"] = g.get("date_time")
    if isinstance(g.get("totalelapsed"), (int, float)):
        out["hours_played"] = round(g["totalelapsed"] / 3600.0, 1)
    mods = g.get("applied_ugcs_1_0") or {}
    out["mods"] = ["%s (%s)" % (m.get("name"), m.get("source")) for m in mods.values() if isinstance(m, dict)]
    out["dlc"] = [m.get("name") for m in (g.get("dlc") or {}).values() if isinstance(m, dict)]
    out["week"] = _root(folder, "persist.campaign_log.json", names, problems).get("total_weeks")

    e = _root(folder, "persist.estate.json", names, problems)
    out["wallet"] = {str(s.get("type")): s.get("amount") for s in (e.get("wallet") or {}).values() if isinstance(s, dict)}
    items = (e.get("trinkets") or {}).get("items")
    out["trinkets_in_stock"] = len(items) if isinstance(items, dict) else None

    u = _root(folder, "persist.upgrades.json", names, problems)
    town, heroes_bought = {}, 0
    for p in (u.get("purchases") or {}).values():
        if not isinstance(p, dict) or not p.get("is_purchased", True):
            continue
        tid = p.get("tree_id")
        if isinstance(tid, str):                      # "123 <name>" when --names resolved it
            tid = int(tid.split()[0])
        tree = TOWN_TREE_BY_HASH.get(tid & 0xFFFFFFFF) if isinstance(tid, int) else None
        if tree:
            town[tree] = town.get(tree, 0) + 1
        else:
            heroes_bought += 1
    out["town_upgrades"] = {t: "%d/%d" % (town.get(t, 0), TOWN_TREES[t]) for t in TOWN_TREES}
    out["town_upgrades_bought"] = "%d/%d" % (sum(town.values()), sum(TOWN_TREES.values()))
    out["hero_upgrades_bought"] = heroes_bought

    r = _root(folder, "persist.roster.json", names, problems)
    lines, levels, diseased, afflicted, stressed = [], {}, 0, 0, 0
    for hid, h in (r.get("heroes") or {}).items():
        d = h
        if isinstance(h, dict) and "hero_file_data" in h:
            d = h["hero_file_data"].get("raw_data", {})
            d = d.get("<embedded save>", d).get("base_root", {}) if isinstance(d, dict) else {}
        if not isinstance(d, dict):
            continue
        actor = d.get("actor") or {}
        stress = d.get("m_Stress", d.get("stress"))
        quirks = sorted((d.get("quirks") or {}).keys())
        level = _level(d.get("resolveXp"), RESOLVE_XP)
        levels[level] = levels.get(level, 0) + 1
        diseased += bool(set(quirks) & DISEASES)
        afflicted += bool(d.get("affliction_type_id"))
        stressed += isinstance(stress, (int, float)) and stress >= 50
        lines.append("%s %s (%s) L%d stress %s wpn %s arm %s quirks[%s]%s%s" % (
            hid, actor.get("name") or actor.get("name_id"), d.get("heroClass"), level,
            int(stress) if isinstance(stress, (int, float)) else stress, d.get("weapon_rank"), d.get("armour_rank"),
            ",".join(quirks),
            " afflicted:%s" % d["affliction_type_id"] if d.get("affliction_type_id") else "",
            " in:%s" % d["roster.building_name"] if d.get("roster.building_name") else ""))
    out["roster_size"] = len(lines)
    out["roster_levels"] = {"L%d" % k: levels[k] for k in sorted(levels)}
    out["roster_diseased"], out["roster_afflicted"], out["roster_stress_50_up"] = diseased, afflicted, int(stressed)
    out["roster"] = lines

    pr = _root(folder, "persist.progression.json", names, problems)
    out["quests_finished"] = "%s (%s won)" % (pr.get("total_quests_finished"), pr.get("total_successful_quests_finished"))
    out["dungeon_levels"] = {k: "L%d (xp %s)" % (_level(v.get("xp"), DUNGEON_XP), v.get("xp"))
                             for k, v in (pr.get("dungeon") or {}).items() if isinstance(v, dict)}
    q = _root(folder, "persist.quest.json", names, problems)
    out["quests_on_offer"] = ["%s: %s %s L%s len %s" % (v.get("id"), v.get("dungeon"), v.get("type"), v.get("difficulty"),
                                                      v.get("length"))
                              for v in (q.get("quests") or {}).values() if isinstance(v, dict)]
    t = _root(folder, "persist.town.json", names, problems)
    out["town_buildings_in_save"] = sorted((t.get("buildings") or {}).keys())
    if problems:
        out["problems"] = problems
    return out


def one_line(name, s):
    if "state" in s:
        return "%-10s %s" % (name, s["state"])
    return "%-10s %-26s week %-4s mode %-10s quests %-12s roster %-3s %-34s town %-6s gold %-8s mods %-3d in_raid %s" % (
        name, '"%s"' % s.get("estate_name"), s.get("week"), str(s.get("game_mode"))[:10], s.get("quests_finished"),
        s.get("roster_size"), " ".join("%s:%d" % kv for kv in (s.get("roster_levels") or {}).items()),
        s.get("town_upgrades_bought"), (s.get("wallet") or {}).get("gold"), len(s.get("mods") or []), s.get("in_raid"))


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 2
    target = argv[0]
    names = {}
    out_path = None
    if "--names" in argv:
        names = game_names(argv[argv.index("--names") + 1])
    if "--out" in argv:
        out_path = argv[argv.index("--out") + 1]
        low = os.path.abspath(out_path).replace("\\", "/").lower()
        if "/userdata/" in low or "/steamapps/" in low or "/documents/darkest" in low:
            print("refusing to write a dump into the game's or Steam's folders: %s" % out_path)
            return 2

    def dumps(obj):
        return json.dumps(obj, indent=1, ensure_ascii=False, default=str)

    if "--slots" in argv:
        found = {}
        for entry in sorted(os.listdir(target)):
            full = os.path.join(target, entry)
            if os.path.isdir(full) and re.match(r"profile_\d+$", entry):
                found[entry] = summary(full, names)
        for n in range(9):
            found.setdefault("profile_%d" % n, {"state": "FREE (no folder)"})
        text = dumps(found) if "--full" in argv else "\n".join(one_line(k, found[k]) for k in sorted(found))
    elif "--summary" in argv:
        text = dumps(summary(target, names))
    else:
        with open(target, "rb") as fh:
            data = fh.read()
        if data[:4] != MAGIC:
            try:
                shown = read_tree(target, names)
            except ValueError as e:
                print("%s: %s" % (target, e))
                return 1
        else:
            save = Save(data, names)
            shown = save.to_dict()
            if "--raw" in argv:
                shown = {
                    "<header>": {"revision": "0x%08x" % save.revision, "objects": save.objects_count,
                                 "objects_offset": save.objects_offset, "fields": save.fields_count,
                                 "fields_offset": save.fields_offset, "data_offset": save.data_offset,
                                 "data_length": save.data_len},
                    "<objects: parent, field, direct children, all children>": [list(o) for o in save.objects],
                    "<fields: name, hash, offset, info, value bytes>": [
                        [f.name, "0x%08x" % f.hash, f.offset, "0x%x" % f.info, len(f.raw)] for f in save.fields],
                    "<tree>": shown,
                }
        text = dumps(shown)
    if out_path:
        with open(out_path, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
        print(out_path)
    else:
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        except AttributeError:
            pass
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
