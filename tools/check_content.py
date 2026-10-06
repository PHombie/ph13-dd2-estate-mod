#!/usr/bin/env python3
"""Content check for the DD2 Estate data files.

Loads  src/DD2Estate/Data/dungeons.json  and  src/DD2Estate/Data/plot_quests.json
and proves that every Darkest Dungeon II id they mention exists in the installed game:

  * element ids   -> an `element_start,<id>,<Type>` row somewhere under
                     StreamingAssets/Excel/**/*.csv   (Type = BattleConfiguration,
                     BattleConfigurationTable, ActorDataClass, BossModifier, BattleModifier,
                     DataExternalBuffs)
  * arena scenes  -> a `combat_arena_*.unity` / `combat_intro_*.unity` entry in the
                     addressables catalog (StreamingAssets/aa/catalog.json)

It also checks the promises the data makes to the code:

  * mandatory content only uses ids that are loaded without DLC under the mod's host game type
    (root Excel folder + Excel/kingdom); ids that live only in Excel/expedition or in a DLC
    folder are errors unless the entry is flagged with that DLC
  * hallway/room pools: tables are condition-free all the way down, no fight chains another
    wave (m_NextBattleConfiguration*), the only m_AdditionalBattleConfigurationTableId allowed
    is the vanilla Death hook, and no fight carries its own m_BackgroundSceneOverride
  * bosses: the listed actors are the ones in the fight, the boss/biome_boss/gang_boss/end_boss
    tags match the game's ActorDataClass rows, the arena matches the config's own
    m_BackgroundSceneOverride when it has one, declared chains match the data, a BossModifier is
    valid for at least one of the boss's actors (same rule as the game's GetIsValidForActorClass)
  * escort tables are condition-free and chain into the boss exactly when the data says they do
  * plot quests: ordered, unique, reference existing dungeons/bosses/tiers, cover every boss
    tier exactly once, texts are at most 60 words; dungeon unlocks name real quests

  * tier scaling (tier_rules.<tier>.scaling): one block per fight kind with known keys and sane
    numbers, an escalation of 1..3 per tier, a `run_boss` on a boss names a real Boss row

Read-only. Exit code 0 = everything checks out, 1 = problems found, 2 = game data not found.

usage:  python tools/check_content.py [--game <DD2 install dir>] [--quiet]
        (the install dir can also come from the DD2_GAME_DIR environment variable)

        python tools/check_content.py --difficulty [--dd1 <DD1 install dir>]
        prints where the tier scaling numbers come from: how much DD1's monsters, fights and heroes
        grow from level 1 to 3 and 5 (read from the DD1 install), how much the DD2 fight pools of
        dungeons.json grow by composition alone (read from the DD2 tables), and the stat shares
        that put the two together, next to the ones stored in dungeons.json
        (docs/recon/difficulty.md, section 3, explains the formulas)
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import defaultdict
from pathlib import Path

DEFAULT_GAME_DIR = r"E:\Steam\steamapps\common\Darkest Dungeon® II"
DEFAULT_DD1_DIR = r"E:\Steam\steamapps\common\DarkestDungeon"
EXCEL_REL = Path("Darkest Dungeon II_Data") / "StreamingAssets" / "Excel"
CATALOG_REL = Path("Darkest Dungeon II_Data") / "StreamingAssets" / "aa" / "catalog.json"

REPO = Path(__file__).resolve().parent.parent
DUNGEONS_JSON = REPO / "src" / "DD2Estate" / "Data" / "dungeons.json"
QUESTS_JSON = REPO / "src" / "DD2Estate" / "Data" / "plot_quests.json"

# The mod hosts its session as GameType.KINGDOM: the root Excel folder plus Excel/kingdom are
# loaded, Excel/expedition is not, DLC folders only when the DLC is owned.
HOST_SCOPES = {"", "kingdom"}
DEATH_HOOK = "death_mashes"            # vanilla "Death may join" table on most road fights
BOSS_TAGS = ("boss", "biome_boss", "gang_boss", "end_boss")
TIERS = ("apprentice", "veteran", "champion", "darkest")
ACTS = ("brain", "lungs", "eyes", "arms", "final")   # prefixes of the BossModifier rows per act
LENGTHS = ("short", "medium", "long")
MAX_WORDS = 60
KEPT_CLASSES = {"BattleConfiguration", "BattleConfigurationTable", "ActorDataClass",
                "BossModifier", "BattleModifier", "ActorDataStats"}
FIGHT_KINDS = ("hallway", "room", "boss")
# tier_rules.<tier>.scaling.<kind>: shares added to every enemy of the fight (0.5 = +50%), except
# speed (points) and crit / resist (percentage points as a share, 0.05 = +5%)
SCALING_LIMITS = {"hp": (0, 3), "dmg": (0, 3), "dot": (0, 3), "speed": (0, 4), "crit": (0, 0.25), "resist": (0, 0.6)}
SCENE_RE = re.compile(r"(?:^|/)(combat_(?:arena|intro)_[a-z0-9_]+)\.unity$")
DLC_RE = re.compile(r"^Assets/(DLC_[A-Za-z0-9_]+)/")


# --------------------------------------------------------------------------- game data

class GameData:
    def __init__(self, game_dir: Path):
        self.excel = game_dir / EXCEL_REL
        self.catalog = game_dir / CATALOG_REL
        self.scopes: dict[tuple[str, str], set[str]] = defaultdict(set)   # (id, class) -> scopes
        self.rows: dict[tuple[str, str], list[tuple[str, list]]] = defaultdict(list)  # -> [(scope, rows)]
        self.by_file: dict[str, list[tuple[str, str, list]]] = defaultdict(list)       # file -> [(id, class, rows)]
        self.file_of: dict[tuple[str, str, str], str] = {}                              # (id, class, scope) -> file
        self.arenas: dict[str, str | None] = {}                           # scene -> dlc or None
        self.files = 0
        self.elements = 0

    def load(self) -> None:
        for path in sorted(self.excel.rglob("*.csv")):
            rel = path.relative_to(self.excel)
            scope = rel.parts[0] if len(rel.parts) > 1 else ""
            self.files += 1
            cur = None
            with open(path, encoding="utf-8", errors="replace") as fh:
                for line in fh:
                    cells = line.rstrip("\r\n").split(",")
                    head = cells[0]
                    if head == "element_start" and len(cells) >= 3:
                        eid, cls = cells[1], cells[2]
                        self.scopes[(eid, cls)].add(scope)
                        self.elements += 1
                        cur = [] if cls in KEPT_CLASSES else None
                        if cur is not None:
                            self.rows[(eid, cls)].append((scope, cur))
                            if cls in ("ActorDataClass", "ActorDataStats"):
                                self.by_file[str(rel)].append((eid, cls, cur))
                                self.file_of[(eid, cls, scope)] = str(rel)
                    elif head == "element_end":
                        cur = None
                    elif cur is not None and head:
                        vals = cells[1:]
                        if vals and vals[-1] == "":
                            vals = vals[:-1]          # trailing comma of the Excel export
                        cur.append((head, vals))
        # Scene entries are project paths such as
        #   Assets/Scenes/Combat/caves/combat_arena_caves_faction.unity
        #   Assets/DLC_catacombs/Scenes/Combat/catacombs/combat_arena_catacombs_faction.unity
        catalog = json.loads(self.catalog.read_text(encoding="utf-8", errors="replace"))
        for internal_id in catalog.get("m_InternalIds", []):
            m = SCENE_RE.search(internal_id)
            if m:
                d = DLC_RE.search(internal_id)
                self.arenas[m.group(1)] = d.group(1).lower() if d else None

    # -- lookups

    @staticmethod
    def allowed(dlc: str | None) -> set[str]:
        return HOST_SCOPES | ({dlc} if dlc else set())

    def availability(self, eid: str, cls: str, dlc: str | None) -> str | None:
        """None when usable, else a human-readable reason."""
        scopes = self.scopes.get((eid, cls))
        if not scopes:
            others = sorted(c for (i, c) in self.scopes if i == eid)
            hint = f" (id exists as {', '.join(others)})" if others else ""
            return f"no `element_start,{eid},{cls}` row in any CSV{hint}"
        if scopes & self.allowed(dlc):
            return None
        where = ", ".join(sorted("Excel/" + s for s in scopes))
        if scopes <= {"expedition"}:
            return f"only defined in {where}, which is not loaded under the kingdom host game type"
        return f"only defined in {where}: DLC content used outside an entry flagged with that DLC"

    def element(self, eid: str, cls: str, dlc: str | None) -> dict[str, list]:
        """Rows of one element as {key: values}; the host game type copy overrides the root one."""
        best = None
        for scope, rows in self.rows.get((eid, cls), []):
            if scope not in self.allowed(dlc):
                continue
            if best is None or scope == "kingdom":
                best = rows
        return {k: v for k, v in (best or [])}

    def table_options(self, tid: str, dlc: str | None, every_scope: bool = False):
        """Merged option rows of a table (same-id tables append their rows)."""
        out = []
        for scope, rows in self.rows.get((tid, "BattleConfigurationTable"), []):
            if not every_scope and scope not in self.allowed(dlc):
                continue
            d = {k: v for k, v in rows}
            ids, types = d.get("m_ids", []), d.get("m_types", [])
            conds, chances = d.get("m_conditions", []), d.get("m_chances", [])
            for i, oid in enumerate(ids):
                out.append({
                    "id": oid,
                    "type": types[i] if i < len(types) else "battle_config",
                    "condition": conds[i] if i < len(conds) else "",
                    "chance": chances[i] if i < len(chances) else "1",
                    "scope": scope,
                })
        return out

    def leaf_configs(self, tid: str, dlc: str | None, _seen=None) -> list[str]:
        seen = _seen if _seen is not None else set()
        if tid in seen:
            return []
        seen.add(tid)
        out = []
        for opt in self.table_options(tid, dlc):
            if opt["type"] == "sub_table":
                out += self.leaf_configs(opt["id"], dlc, seen)
            elif opt["type"] == "battle_config":
                out.append(opt["id"])
        return list(dict.fromkeys(out))

    def table_conditions(self, tid: str, _seen=None) -> list[str]:
        """Every condition found in the table or its sub tables, in any scope (strict)."""
        seen = _seen if _seen is not None else set()
        if tid in seen:
            return []
        seen.add(tid)
        out = []
        for opt in self.table_options(tid, None, every_scope=True):
            if opt["condition"]:
                out.append(f"{tid}: {opt['id']} needs {opt['condition']}")
            if opt["type"] == "sub_table":
                out += self.table_conditions(opt["id"], seen)
        return out

    def actor_tags(self, aid: str, dlc: str | None) -> list[str]:
        return self.element(aid, "ActorDataClass", dlc).get("m_Tags", [])


# --------------------------------------------------------------------------- checker

class Checker:
    def __init__(self, game: GameData):
        self.g = game
        self.errors: list[str] = []
        self.notes: list[str] = []
        self.checked: set[tuple[str, str]] = set()
        self.arenas_checked: set[str] = set()
        self.summary: list[str] = []

    def err(self, where: str, msg: str) -> None:
        self.errors.append(f"{where}: {msg}")

    def note(self, where: str, msg: str) -> None:
        self.notes.append(f"{where}: {msg}")

    # -- primitive references

    def ref(self, where: str, eid, cls: str, dlc: str | None) -> bool:
        if not isinstance(eid, str) or not eid:
            self.err(where, f"expected a {cls} id, got {eid!r}")
            return False
        self.checked.add((eid, cls))
        reason = self.g.availability(eid, cls, dlc)
        if reason:
            self.err(where, f"{cls} `{eid}`: {reason}")
            return False
        return True

    def arena(self, where: str, name, dlc: str | None) -> bool:
        if not isinstance(name, str) or not name:
            self.err(where, f"expected an arena scene name, got {name!r}")
            return False
        self.arenas_checked.add(name)
        if name not in self.g.arenas:
            self.err(where, f"arena `{name}` is not a scene in the addressables catalog")
            return False
        owner = self.g.arenas[name]
        if owner and owner != dlc:
            self.err(where, f"arena `{name}` belongs to {owner} but the entry is not flagged with it")
            return False
        return True

    # -- fights

    def fight_leaves(self, where: str, block: dict, dlc: str | None) -> list[str]:
        """Resolve config / config_table / configs of a boss-like tier block to config ids."""
        leaves: list[str] = []
        if block.get("config") is not None:
            if self.ref(where + ".config", block["config"], "BattleConfiguration", dlc):
                leaves.append(block["config"])
        for i, cid in enumerate(block.get("configs") or []):
            if self.ref(f"{where}.configs[{i}]", cid, "BattleConfiguration", dlc):
                leaves.append(cid)
        if block.get("config_table") is not None:
            tid = block["config_table"]
            if self.ref(where + ".config_table", tid, "BattleConfigurationTable", dlc):
                for cid in self.g.leaf_configs(tid, dlc):
                    if self.ref(f"{where}.config_table[{tid}]", cid, "BattleConfiguration", dlc):
                        leaves.append(cid)
        if not leaves:
            self.err(where, "no fight: give one of config / configs / config_table")
        return leaves

    def chains_of(self, cfg: dict[str, list]) -> set[str]:
        out = set()
        for key in ("m_NextBattleConfigurationId", "m_NextBattleConfigurationTableId",
                    "m_AdditionalBattleConfigurationTableId"):
            for v in cfg.get(key, []):
                if v != DEATH_HOOK:
                    out.add(v)
        return out

    def check_actors_exist(self, where: str, cid: str, dlc: str | None) -> list[str]:
        actors = self.g.element(cid, "BattleConfiguration", dlc).get("m_EnemyActors", [])
        for a in actors:
            self.ref(f"{where} -> {cid}.m_EnemyActors", a, "ActorDataClass", dlc)
        return actors

    def check_pool(self, where: str, pool: dict, dlc: str | None) -> str:
        """Returns a one-line description of the pool for the summary."""
        configs = pool.get("configs") or []
        tables = pool.get("tables") or []
        if not configs and not tables:
            self.err(where, "empty pool (no configs and no tables)")
        leaves: list[str] = []
        for i, cid in enumerate(configs):
            if self.ref(f"{where}.configs[{i}]", cid, "BattleConfiguration", dlc):
                leaves.append(cid)
        for i, tid in enumerate(tables):
            if not self.ref(f"{where}.tables[{i}]", tid, "BattleConfigurationTable", dlc):
                continue
            for c in self.g.table_conditions(tid):
                self.err(f"{where}.tables[{i}]", f"table is not condition-free ({c})")
            sub = self.g.leaf_configs(tid, dlc)
            if not sub:
                self.err(f"{where}.tables[{i}]", f"table `{tid}` resolves to no fights")
            for cid in sub:
                if self.ref(f"{where}.tables[{i}] -> {cid}", cid, "BattleConfiguration", dlc):
                    leaves.append(cid)
        weights = pool.get("weights") or {}
        for key, val in weights.items():
            if key not in configs and key not in tables:
                self.err(where + ".weights", f"`{key}` is not in this pool")
            if not isinstance(val, (int, float)) or val <= 0:
                self.err(where + ".weights", f"`{key}` needs a positive weight")
        death = locked = forced = 0
        unique = list(dict.fromkeys(leaves))
        for cid in unique:
            cfg = self.g.element(cid, "BattleConfiguration", dlc)
            self.check_actors_exist(where, cid, dlc)
            for key in ("m_NextBattleConfigurationId", "m_NextBattleConfigurationTableId"):
                if cfg.get(key):
                    self.err(where, f"`{cid}` chains another wave ({key}={cfg[key][0]})")
            for v in cfg.get("m_AdditionalBattleConfigurationTableId", []):
                if v == DEATH_HOOK:
                    death += 1
                else:
                    self.err(where, f"`{cid}` rolls an additional wave from `{v}`")
            if cfg.get("m_BackgroundSceneOverride"):
                self.err(where, f"`{cid}` forces its own arena {cfg['m_BackgroundSceneOverride'][0]}")
            locked += cfg.get("m_RunDataStatsId", [""])[0] == "no_retreat"
            forced += bool(cfg.get("m_BattleModifierOverrideId"))
        extra = "".join(f", {n} {label}" for n, label in
                        ((death, "with Death hook"), (locked, "no_retreat"), (forced, "forced modifier")) if n)
        return f"{len(configs)} configs + {len(tables)} tables = {len(unique)} fights{extra}"

    def check_tier_pools(self, where: str, tier: dict, dlc: str | None, label: str) -> None:
        self.summary.append(f"  {label}")
        for slot in ("hallway", "room"):
            if slot not in tier:
                if dlc is None:
                    self.err(where, f"missing `{slot}` pool")
                continue
            self.summary.append(f"    {slot:<8} " + self.check_pool(f"{where}.{slot}", tier[slot], dlc))

    def check_boss_header(self, where: str, boss: dict, dlc: str | None) -> None:
        """actor_ids / escort_actor_ids exist and the declared boss tags equal the game's."""
        actors = boss.get("actor_ids") or []
        if not actors:
            self.err(where, "no actor_ids")
        found: set[str] = set()
        for i, a in enumerate(actors):
            if self.ref(f"{where}.actor_ids[{i}]", a, "ActorDataClass", dlc):
                found |= set(self.g.actor_tags(a, dlc)) & set(BOSS_TAGS)
        for i, a in enumerate(boss.get("escort_actor_ids") or []):
            self.ref(f"{where}.escort_actor_ids[{i}]", a, "ActorDataClass", dlc)
        if boss.get("run_boss") is not None:
            self.ref(where + ".run_boss", boss["run_boss"], "Boss", dlc)
        declared = set(boss.get("tags") or [])
        if declared != found:
            self.err(where + ".tags", f"declared {sorted(declared)} but the game's actors carry {sorted(found)}")
        for kind in ("gang_boss", "end_boss"):
            if kind in found and f"{kind}_tag" not in (boss.get("guards") or []):
                self.err(where + ".guards", f"actors carry `{kind}` but guards does not list `{kind}_tag`")

    def check_boss(self, where: str, boss: dict, dungeon_tiers, dlc_default: str | None) -> None:
        dlc = boss.get("dlc") or dlc_default
        self.check_boss_header(where, boss, dlc)
        tiers = boss.get("tiers") or {}
        if not tiers:
            self.err(where, "no tiers")
        for tname, block in tiers.items():
            w = f"{where}.tiers.{tname}"
            if tname not in TIERS:
                self.err(w, "unknown tier name")
            if dungeon_tiers is not None and tname not in dungeon_tiers:
                self.err(w, "tier is not defined for this dungeon")
            self.check_boss_tier(w, block, boss, dlc)

    def chain_leaves(self, cid: str, dlc: str | None) -> list[str]:
        if (cid, "BattleConfigurationTable") in self.g.scopes:
            return self.g.leaf_configs(cid, dlc)
        return [cid]

    def check_boss_tier(self, w: str, block: dict, boss: dict, dlc: str | None) -> None:
        actors = boss.get("actor_ids") or []
        known = set(actors) | set(boss.get("escort_actor_ids") or [])
        leaves = self.fight_leaves(w, block, dlc)
        declared_chain = set(block.get("chains_to") or [])
        real_chain: set[str] = set()
        overrides: set[str] = set()
        fielded: set[str] = set()
        for cid in leaves:
            cfg = self.g.element(cid, "BattleConfiguration", dlc)
            enemies = self.check_actors_exist(w, cid, dlc)
            stray = [a for a in enemies if a not in known]
            if stray:
                self.err(w, f"`{cid}` fields actors missing from actor_ids/escort_actor_ids: {stray}")
            fielded |= set(enemies)
            real_chain |= self.chains_of(cfg)
            overrides |= set(cfg.get("m_BackgroundSceneOverride", [])) or {""}
        if real_chain != declared_chain:
            self.err(w + ".chains_to", f"declared {sorted(declared_chain)} but the fight chains to {sorted(real_chain)}")
        for cid in declared_chain:
            cls = "BattleConfigurationTable" if (cid, "BattleConfigurationTable") in self.g.scopes else "BattleConfiguration"
            if not self.ref(w + ".chains_to", cid, cls, dlc):
                continue
            for leaf in self.chain_leaves(cid, dlc):
                if self.ref(f"{w}.chains_to -> {leaf}", leaf, "BattleConfiguration", dlc):
                    fielded |= set(self.check_actors_exist(w + ".chains_to", leaf, dlc))
        if leaves and actors and not fielded & set(actors):
            self.err(w, "none of the boss's actor_ids is fielded by the fight or the fights it chains to")

        source = block.get("arena_source")
        arena = block.get("arena")
        if source == "config_override":
            self.arena(w + ".arena", arena, dlc)
            if overrides != {arena}:
                self.err(w + ".arena", f"says config_override `{arena}` but the configs override with {sorted(overrides)}")
        elif source == "mod":
            self.arena(w + ".arena", arena, dlc)
            if overrides != {""}:
                self.err(w + ".arena", f"arena_source is `mod` but the configs force {sorted(o for o in overrides if o)}")
        elif source == "dungeon":
            if arena is not None:
                self.err(w + ".arena", "arena_source `dungeon` means arena must be null")
            if overrides != {""}:
                self.err(w + ".arena", f"arena_source is `dungeon` but the configs force {sorted(o for o in overrides if o)}")
        else:
            self.err(w + ".arena_source", f"must be config_override, mod or dungeon (got {source!r})")

        escorts = block.get("escort_tables") or []
        escort_overrides: set[str] = set()
        for i, tid in enumerate(escorts):
            we = f"{w}.escort_tables[{i}]"
            if not self.ref(we, tid, "BattleConfigurationTable", dlc):
                continue
            for c in self.g.table_conditions(tid):
                self.err(we, f"table is not condition-free ({c})")
            last = i == len(escorts) - 1
            targets = {block.get("config"), block.get("config_table")} - {None}
            for cid in self.g.leaf_configs(tid, dlc):
                if not self.ref(f"{we} -> {cid}", cid, "BattleConfiguration", dlc):
                    continue
                self.check_actors_exist(we, cid, dlc)
                cfg = self.g.element(cid, "BattleConfiguration", dlc)
                escort_overrides |= set(cfg.get("m_BackgroundSceneOverride", [])) or {""}
                nxt = set(cfg.get("m_NextBattleConfigurationId", [])) | set(cfg.get("m_NextBattleConfigurationTableId", []))
                if block.get("escort_native_chain") and last:
                    if not nxt or not nxt <= targets:
                        self.err(we, f"`{cid}` should chain into {sorted(targets)} but chains to {sorted(nxt)}")
                elif nxt:
                    self.err(we, f"`{cid}` chains to {sorted(nxt)} but escort_native_chain is false")
        if block.get("escort_native_chain") and not escorts:
            self.err(w, "escort_native_chain without escort_tables")
        escort_arena = block.get("escort_arena")
        if escort_arena is not None:
            self.arena(w + ".escort_arena", escort_arena, dlc)
        if escort_overrides - {""} and escort_overrides != {escort_arena}:
            self.err(w + ".escort_arena", f"escort fights force {sorted(o for o in escort_overrides if o)} but escort_arena is {escort_arena!r}")
        if escort_arena is not None and escort_overrides == {""}:
            self.note(w, "escort fights have no arena of their own; escort_arena is the mod's choice")

        if block.get("battle_modifier") is not None:
            self.ref(w + ".battle_modifier", block["battle_modifier"], "BattleModifier", dlc)
        if block.get("external_buffs") is not None:
            self.ref(w + ".external_buffs", block["external_buffs"], "DataExternalBuffs", dlc)
        bm = block.get("boss_modifier")
        if bm is not None and self.ref(w + ".boss_modifier", bm, "BossModifier", dlc):
            row = self.g.element(bm, "BossModifier", dlc)
            want = set(row.get("m_ActorDataTags", []))
            if not any(want & set(self.g.actor_tags(a, dlc)) for a in actors):
                self.err(w + ".boss_modifier", f"`{bm}` targets tags {sorted(want)}; none of the boss's actors has one")
            buffs = (row.get("m_DataExternalBuffsId") or [bm])[0]
            if block.get("external_buffs") != buffs:
                self.err(w + ".external_buffs", f"`{bm}` delivers `{buffs}`, the entry says {block.get('external_buffs')!r}")

    def check_ordain(self, where: str, ordain, dlc: str | None) -> None:
        if ordain is None:
            return
        bm = ordain.get("boss_modifier")
        if self.ref(where + ".boss_modifier", bm, "BossModifier", dlc):
            native = self.g.element(bm, "BossModifier", dlc).get("m_Chance", ["?"])[0]
            if str(ordain.get("chance")) != native and float(ordain.get("chance", -1)) != float(native):
                self.note(where, f"chance {ordain.get('chance')} differs from the game's own {native} for `{bm}`")
        if not isinstance(ordain.get("chance"), (int, float)) or not 0 <= ordain["chance"] <= 1:
            self.err(where + ".chance", "must be a number in 0..1")

    def check_tier_rule(self, where: str, rule: dict) -> None:
        if rule.get("escalation") not in (1, 2, 3):
            self.err(where + ".escalation", "must be 1, 2 or 3 (the game clamps the escalation run value to that range)")
        scaling = rule.get("scaling")
        if not isinstance(scaling, dict):
            self.err(where + ".scaling", "missing: one block per fight kind (hallway, room, boss)")
            return
        for kind in FIGHT_KINDS:
            block = scaling.get(kind)
            if not isinstance(block, dict):
                self.err(f"{where}.scaling.{kind}", "missing block")
                continue
            for key, val in block.items():
                if key not in SCALING_LIMITS:
                    self.err(f"{where}.scaling.{kind}", f"unknown key `{key}` (known: {', '.join(SCALING_LIMITS)})")
                    continue
                lo, hi = SCALING_LIMITS[key]
                if not isinstance(val, (int, float)) or isinstance(val, bool) or not lo <= val <= hi:
                    self.err(f"{where}.scaling.{kind}.{key}", f"must be a number in {lo}..{hi}")
        for kind in scaling:
            if kind not in FIGHT_KINDS:
                self.err(where + ".scaling", f"unknown fight kind `{kind}`")

    # -- files

    def check_dungeons(self, data: dict) -> dict:
        index: dict[str, dict] = {}
        if data.get("format") != 1:
            self.err("dungeons.json", "format must be 1")
        rules = data.get("tier_rules") or {}
        for t in TIERS:
            if t not in rules:
                self.err("dungeons.json.tier_rules", f"missing tier `{t}`")
                continue
            self.check_tier_rule(f"dungeons.json.tier_rules.{t}", rules[t])
        for di, d in enumerate(data.get("dungeons") or []):
            did = d.get("id", f"#{di}")
            where = f"dungeons[{did}]"
            if did in index:
                self.err(where, "duplicate dungeon id")
            index[did] = {"tiers": list((d.get("tiers") or {}).keys()), "bosses": {},
                          "requires": list((d.get("unlock") or {}).get("requires") or [])}
            for key in ("name", "dd1_art", "unlock", "factions", "arenas", "tiers", "bosses"):
                if key not in d:
                    self.err(where, f"missing key `{key}`")
            act = (d.get("confession") or {}).get("act")
            if act not in ACTS:
                self.err(where + ".confession.act", f"must be one of {ACTS} (BossModifier id prefix)")
            for slot in ("hallway", "room"):
                names = (d.get("arenas") or {}).get(slot) or []
                if not names:
                    self.err(f"{where}.arenas.{slot}", "no arena")
                for i, a in enumerate(names):
                    self.arena(f"{where}.arenas.{slot}[{i}]", a, None)
            for f in d.get("factions") or []:
                if not f.get("id") or not isinstance(f.get("weight"), (int, float)) or f["weight"] <= 0:
                    self.err(where + ".factions", f"bad faction entry {f!r}")
            self.summary.append(f"{d.get('name', did)} ({did})")
            for tname, tier in (d.get("tiers") or {}).items():
                if tname not in TIERS:
                    self.err(f"{where}.tiers.{tname}", "unknown tier name")
                self.check_ordain(f"{where}.tiers.{tname}.ordain", tier.get("ordain"), None)
                self.check_tier_pools(f"{where}.tiers.{tname}", tier, None, tname)
            for b in d.get("bosses") or []:
                bid = b.get("id", "?")
                if bid in index[did]["bosses"]:
                    self.err(f"{where}.bosses[{bid}]", "duplicate boss id")
                # a boss whose quest is DD1's own (`dd1_quest`: a town event's quest, read from the DD1 install)
                # has no story quest in plot_quests.json to be covered by
                index[did]["bosses"][bid] = [] if b.get("dd1_quest") else list((b.get("tiers") or {}).keys())
                self.check_boss(f"{where}.bosses[{bid}]", b, d.get("tiers") or {}, None)
                kinds = sorted(set(b.get("tags") or []) - {"boss"})
                self.summary.append(f"  boss {bid:<28} tiers {'/'.join((b.get('tiers') or {}).keys())}  tags {','.join(kinds) or '-'}")
            for xi, x in enumerate(d.get("dlc_extras") or []):
                dlc = x.get("dlc")
                wx = f"{where}.dlc_extras[{xi}:{dlc}]"
                if not dlc or not any(s == dlc for scopes in self.g.scopes.values() for s in scopes):
                    self.err(wx, f"unknown dlc `{dlc}`")
                    continue
                for slot in ("hallway", "room"):
                    for i, a in enumerate((x.get("arenas") or {}).get(slot) or []):
                        self.arena(f"{wx}.arenas.{slot}[{i}]", a, dlc)
                for tname, tier in (x.get("tiers") or {}).items():
                    if tname not in (d.get("tiers") or {}):
                        self.err(f"{wx}.tiers.{tname}", "tier is not defined for this dungeon")
                    self.check_tier_pools(f"{wx}.tiers.{tname}", tier, dlc, f"{tname} [+{dlc}]")
        for wi, wb in enumerate(data.get("wandering") or []):
            wid = wb.get("id", f"#{wi}")
            where = f"wandering[{wid}]"
            dlc = wb.get("dlc")
            for dn in wb.get("dungeons") or []:
                if dn not in index:
                    self.err(where + ".dungeons", f"unknown dungeon `{dn}`")
            if dlc is not None and not any(dlc in scopes for scopes in self.g.scopes.values()):
                self.err(where + ".dlc", f"unknown dlc `{dlc}`")
            self.check_boss_header(where, wb, dlc)
            blocks: list[tuple[str, dict]] = []
            for name, block in (wb.get("tiers") or {}).items():
                if name not in TIERS:
                    self.err(f"{where}.tiers.{name}", "unknown tier name")
                blocks.append((f"{where}.tiers.{name}", block))
            for dn, block in (wb.get("per_dungeon") or {}).items():
                if dn not in index:
                    self.err(where + ".per_dungeon", f"unknown dungeon `{dn}`")
                blocks.append((f"{where}.per_dungeon.{dn}", block))
            for w, block in blocks:
                self.check_boss_tier(w, block, wb, dlc)
            if not blocks:
                self.err(where, "no tiers / per_dungeon fights")
            self.summary.append(f"wandering {wid:<22} in {','.join(wb.get('dungeons') or [])}  dlc {dlc or '-'}")
        return index

    def check_quests(self, data: dict, index: dict) -> None:
        if data.get("format") != 1:
            self.err("plot_quests.json", "format must be 1")
        seen: list[str] = []
        covered: dict[tuple[str, str, str], str] = {}
        words_max = 0
        for qi, q in enumerate(data.get("quests") or []):
            qid = q.get("id", f"#{qi}")
            where = f"quests[{qid}]"
            if qid in seen:
                self.err(where, "duplicate quest id")
            dn, boss, tier = q.get("dungeon"), q.get("boss"), q.get("tier")
            if dn not in index:
                self.err(where + ".dungeon", f"unknown dungeon `{dn}`")
            else:
                if tier not in index[dn]["tiers"]:
                    self.err(where + ".tier", f"dungeon `{dn}` has no tier `{tier}`")
                if boss is None:
                    if q.get("type") == "kill_boss":
                        self.err(where, "kill_boss quest without a boss")
                elif boss not in index[dn]["bosses"]:
                    self.err(where + ".boss", f"dungeon `{dn}` has no boss `{boss}`")
                elif tier not in index[dn]["bosses"][boss]:
                    self.err(where + ".tier", f"boss `{boss}` has no tier `{tier}`")
                else:
                    key = (dn, boss, tier)
                    if key in covered:
                        self.err(where, f"boss tier already covered by `{covered[key]}`")
                    covered[key] = qid
            if q.get("length") not in LENGTHS:
                self.err(where + ".length", f"must be one of {LENGTHS}")
            unlock = q.get("unlock") or {}
            for r in unlock.get("requires") or []:
                if r not in seen:
                    self.err(where + ".unlock.requires", f"`{r}` is not an earlier quest")
            lvl = unlock.get("dungeon_level")
            if not isinstance(lvl, int) or not 0 <= lvl <= 7:
                self.err(where + ".unlock.dungeon_level", "must be an integer 0..7")
            for key in ("name", "goal_text", "intro_narration", "victory_narration"):
                text = q.get(key)
                if not isinstance(text, str) or not text.strip():
                    self.err(f"{where}.{key}", "missing text")
                    continue
                n = len(text.split())
                words_max = max(words_max, n)
                if n > MAX_WORDS:
                    self.err(f"{where}.{key}", f"{n} words (limit {MAX_WORDS})")
            seen.append(qid)
        for dn, info in index.items():
            for r in info.get("requires", []):
                if r not in seen:
                    self.err(f"dungeons[{dn}].unlock.requires", f"`{r}` is not a plot quest")
            for boss, tiers in info["bosses"].items():
                for tier in tiers:
                    if (dn, boss, tier) not in covered:
                        self.err("plot_quests.json", f"no quest for boss `{boss}` ({dn}) at tier `{tier}`")
        self.summary.append(f"plot quests: {len(seen)} (boss tiers covered {len(covered)}), longest text {words_max} words")


# --------------------------------------------------------------------------- difficulty report

def _mean(xs):
    xs = list(xs)
    return sum(xs) / len(xs) if xs else 0.0


def _stats(rows) -> dict[str, float]:
    """add-stats of an ActorDataStats element as {stat: value}."""
    out: dict[str, float] = {}
    keys: list[str] = []
    for head, vals in rows:
        try:
            if head == "key_map":
                keys = vals
            elif head == "add_stats":
                for k, v in zip(keys, vals):
                    out[k] = float(v)
            elif head == "add_stat":
                out[vals[0]] = float(vals[1])
        except (ValueError, IndexError):
            pass
    return out


class Dd2Strength:
    """Size of the DD2 fights behind a pool: enemies, their summed health and summed damage per round.

    Damage of an actor = mean of the damaging skills written in the same CSV file (health_damage +
    half of health_damage_range) times its turns per round. Which skills an actor really carries is
    in its prefab, so this is the same rough measure the DD1 side uses (mean skill damage)."""

    def __init__(self, game: GameData):
        self.g = game
        self.file_damage: dict[str, float] = {}
        for name, elements in game.by_file.items():
            actors = {eid for eid, cls, _ in elements if cls == "ActorDataClass"}
            hits = []
            for eid, cls, rows in elements:
                if cls != "ActorDataStats" or eid in actors:
                    continue
                st = _stats(rows)
                if st.get("health_damage", 0) > 0 or st.get("health_damage_range", 0) > 0:
                    hits.append(st.get("health_damage", 0) + st.get("health_damage_range", 0) / 2)
            self.file_damage[name] = _mean(hits)

    def _scope(self, eid: str, cls: str, dlc: str | None) -> str | None:
        best = None
        for scope, _ in self.g.rows.get((eid, cls), []):
            if scope in self.g.allowed(dlc) and (best is None or scope == "kingdom"):
                best = scope
        return best

    def actor(self, aid: str, dlc: str | None):
        if self._scope(aid, "ActorDataStats", dlc) is None:
            return None
        st = _stats(self.g.element(aid, "ActorDataStats", dlc).items())
        cls_scope = self._scope(aid, "ActorDataClass", dlc)
        damage = self.file_damage.get(self.g.file_of.get((aid, "ActorDataClass", cls_scope), ""), 0.0)
        return st.get("health_max", 0.0), damage * (st.get("speed_number_of_turns", 1) or 1)

    def fight(self, cid: str, dlc: str | None):
        cfg = self.g.element(cid, "BattleConfiguration", dlc)
        actors = [a for a in (self.actor(x, dlc) for x in cfg.get("m_EnemyActors", [])) if a]
        return len(actors), sum(a[0] for a in actors), sum(a[1] for a in actors)

    def leaves(self, tid: str, dlc: str | None, weight: float = 1.0, seen: frozenset = frozenset()):
        if tid in seen:
            return []
        options = self.g.table_options(tid, dlc)
        total = sum(float(o["chance"] or 0) for o in options) or 1.0
        out = []
        for o in options:
            w = weight * float(o["chance"] or 0) / total
            if o["type"] == "sub_table":
                out += self.leaves(o["id"], dlc, w, seen | {tid})
            elif o["type"] == "battle_config":
                out.append((o["id"], w))
        return out

    def pool(self, pool: dict, dlc: str | None = None):
        weights = pool.get("weights") or {}
        entries = [(c, weights.get(c, 1), False) for c in pool.get("configs") or []]
        entries += [(t, weights.get(t, 1), True) for t in pool.get("tables") or []]
        total = sum(w for _, w, _ in entries) or 1.0
        n = hp = dmg = 0.0
        for eid, w, is_table in entries:
            for cid, lw in (self.leaves(eid, dlc) if is_table else [(eid, 1.0)]):
                f = self.fight(cid, dlc)
                share = w / total * lw
                n, hp, dmg = n + share * f[0], hp + share * f[1], dmg + share * f[2]
        return n, hp, dmg


class Dd1Numbers:
    """What DD1's own files say about levels 1, 3 and 5 (monster variants _A/_B/_C, mash tables, hero gear)."""

    DUNGEONS = ("crypts", "weald", "warrens", "cove")
    LEVELS = (1, 3, 5)

    def __init__(self, root: Path):
        self.root = root
        self.monsters: dict[str, dict] = {}
        for path in (root / "monsters").rglob("*.info.darkest"):
            self.monsters[path.name[:-len(".info.darkest")]] = self._monster(path)
        self.heroes = {}
        for path in sorted((root / "heroes").glob("*/*.info.darkest")):
            if path.name == path.parent.name + ".info.darkest":
                self.heroes[path.parent.name] = self._hero(path)

    @staticmethod
    def _monster(path: Path) -> dict:
        m = {"hp": 0.0, "def": 0.0, "spd": 0.0, "dmg": [], "atk": [], "crit": [], "res": {}}
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            line = line.strip()
            if line.startswith("stats:"):
                for key, pat in (("hp", r"\.hp (\S+)"), ("def", r"\.def (\S+)%"), ("spd", r"\.spd (\S+)")):
                    got = re.search(pat, line)
                    if got:
                        m[key] = float(got.group(1))
                for key in ("stun", "poison", "bleed", "debuff", "move"):
                    got = re.search(r"\." + key + r"_resist (\S+)%", line)
                    if got:
                        m["res"][key] = float(got.group(1))
            elif line.startswith("skill:"):
                dmg = re.search(r"\.dmg (\S+) (\S+)", line)
                if dmg and float(dmg.group(2)) > 0:
                    m["dmg"].append((float(dmg.group(1)) + float(dmg.group(2))) / 2)
                    atk, crit = re.search(r"\.atk (\S+)%", line), re.search(r"\.crit (\S+)%", line)
                    if atk:
                        m["atk"].append(float(atk.group(1)))
                        m["crit"].append(float(crit.group(1)) if crit else 0.0)
        for key in ("dmg", "atk", "crit"):
            m[key] = _mean(m[key]) if m[key] else None
        return m

    @staticmethod
    def _hero(path: Path) -> dict:
        h = {"dmg": [], "crit": [], "spd": [], "hp": [], "dodge": [], "acc": defaultdict(list)}
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("weapon:"):
                got = re.search(r"\.dmg (\S+) (\S+) \.crit (\S+)% \.spd (\S+)", line)
                h["dmg"].append((float(got.group(1)) + float(got.group(2))) / 2)
                h["crit"].append(float(got.group(3)))
                h["spd"].append(float(got.group(4)))
            elif line.startswith("armour:"):
                got = re.search(r"\.def (\S+)% \.prot (\S+) \.hp (\S+)", line)
                h["dodge"].append(float(got.group(1)))
                h["hp"].append(float(got.group(3)))
            elif line.startswith("combat_skill:"):
                got = re.search(r"\.level (\d) .*?\.atk (\S+)%", line)
                if got and float(got.group(2)) > 0:
                    h["acc"][int(got.group(1))].append(float(got.group(2)))
        return h

    # -- monsters by variant

    def variant_growth(self) -> dict:
        """Mean over the monsters that have _A, _B and _C: ratio (hp, dmg) or difference (the rest) to _A."""
        out = {}
        bases = sorted(n[:-2] for n in self.monsters
                       if n.endswith("_A") and n[:-2] + "_B" in self.monsters and n[:-2] + "_C" in self.monsters)
        for level, suffix in ((3, "_B"), (5, "_C")):
            hp, dmg, spd, atk, crit, dfn, res = [], [], [], [], [], [], []
            for b in bases:
                a, v = self.monsters[b + "_A"], self.monsters[b + suffix]
                if a["hp"]:
                    hp.append(v["hp"] / a["hp"])
                if a["dmg"] and v["dmg"]:
                    dmg.append(v["dmg"] / a["dmg"])
                    atk.append(v["atk"] - a["atk"])
                    crit.append(v["crit"] - a["crit"])
                spd.append(v["spd"] - a["spd"])
                dfn.append(v["def"] - a["def"])
                res += [v["res"][k] - a["res"][k] for k in a["res"] if k in v["res"]]
            out[level] = {"n": len(bases), "hp": _mean(hp), "dmg": _mean(dmg), "spd": _mean(spd), "atk": _mean(atk),
                          "crit": _mean(crit), "def": _mean(dfn), "res": _mean(res)}
        return out

    # -- fights by level

    def mash(self, dungeon: str, level: int) -> dict[str, list]:
        kinds: dict[str, list] = defaultdict(list)
        path = self.root / "dungeons" / dungeon / f"{dungeon}.{level}.mash.darkest"
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            got = re.match(r"(\w+): (?:\.name (\S+) )?\.chance (\S+) \.types (.*)", line.strip())
            if not got:
                continue
            types = [t for t in got.group(4).split() if t in self.monsters]
            if types:
                kinds[got.group(1)].append((float(got.group(3)), types, got.group(2) or ""))
        return kinds

    def _size(self, rows):
        total = sum(r[0] for r in rows) or 1.0
        n = sum(r[0] * len(r[1]) for r in rows) / total
        hp = sum(r[0] * sum(self.monsters[x]["hp"] for x in r[1]) for r in rows) / total
        dmg = sum(r[0] * sum(self.monsters[x]["dmg"] or 0 for x in r[1]) for r in rows) / total
        return n, hp, dmg

    def fights(self, dungeon: str, level: int, kind: str):
        """Weighted mean (monsters, summed hp, summed mean skill damage) of one kind of a mash table."""
        return self._size(self.mash(dungeon, level).get(kind, []))

    def darkest_fights(self):
        """The Darkest Dungeon's ordinary fights (named mashes that are not bosses or mini-bosses)."""
        return self._size([r for r in self.mash("darkestdungeon", 6).get("named", []) if "_mash_" in r[2]])

    def monster_aim(self, level: int):
        """Appearance-weighted mean skill accuracy, dodge and crit of the monsters in halls and rooms."""
        atk, dfn, crit = [], [], []
        for d in self.DUNGEONS:
            mash = self.mash(d, level)
            for kind in ("hall", "room"):
                for w, types, _ in mash.get(kind, []):
                    for t in types:
                        m = self.monsters[t]
                        dfn.append((m["def"], w))
                        if m["atk"] is not None:
                            atk.append((m["atk"], w))
                            crit.append((m["crit"], w))
        wm = lambda xs: sum(a * b for a, b in xs) / (sum(b for _, b in xs) or 1.0)
        return wm(atk), wm(dfn), wm(crit)

    def encounter_growth(self) -> dict:
        """{level: {kind: (hp ratio, dmg ratio)}} against level 1 of the same kind, mean of the four dungeons."""
        out: dict = {}
        for level in (3, 5):
            out[level] = {}
            for kind in ("hall", "room", "boss"):
                hp, dmg = [], []
                for d in self.DUNGEONS:
                    base, now = self.fights(d, 1, kind), self.fights(d, level, kind)
                    hp.append(now[1] / base[1])
                    dmg.append(now[2] / base[2])
                out[level][kind] = (_mean(hp), _mean(dmg))
        return out

    # -- heroes by gear level (index 0 = level 1)

    def gear(self) -> list[dict]:
        full = [h for h in self.heroes.values() if len(h["dmg"]) == 5 and len(h["hp"]) == 5]
        out = []
        for i in range(5):
            out.append({
                "dmg": _mean(h["dmg"][i] / h["dmg"][0] for h in full),
                "hp": _mean(h["hp"][i] / h["hp"][0] for h in full),
                "crit": _mean(h["crit"][i] - h["crit"][0] for h in full),
                "spd": _mean(h["spd"][i] - h["spd"][0] for h in full),
                "dodge": _mean(h["dodge"][i] for h in full),
                "acc": _mean(_mean(h["acc"][i]) for h in full if h["acc"].get(i)),
                "n": len(full),
            })
        return out


def mastery_gain(game: GameData) -> tuple[float, int]:
    """Mean damage of a mastered DD2 hero skill (<id>_u) against its plain version."""
    stats = {}
    for name, elements in game.by_file.items():
        if not Path(name).name.startswith("hero_"):
            continue
        for eid, cls, rows in elements:
            if cls == "ActorDataStats":
                stats[eid] = _stats(rows)
    ratios = []
    for eid, up in stats.items():
        plain = stats.get(eid[:-2]) if eid.endswith("_u") else None
        if plain and "health_damage" in plain and "health_damage" in up:
            low = plain["health_damage"] + plain.get("health_damage_range", 0) / 2
            if low > 0:
                ratios.append((up["health_damage"] + up.get("health_damage_range", 0) / 2) / low)
    return _mean(ratios), len(ratios)


# The estate's rules that enter the derivation (docs/recon/difficulty.md, section 3).
GEAR_AT = {"veteran": 4, "champion": 5}               # DD1 weapon/armour level a hero may wear at the tier (resolve 3 / 5)
MASTERED_SHARE = {"veteran": 3 / 5, "champion": 1.0}  # share of a hero's five skills the guild lets them master by then
DD1_LEVEL = {"veteran": 3, "champion": 5}
TOLERANCE = 0.08


def difficulty_report(game: GameData, data: dict, dd1_dir: Path) -> int:
    strength = Dd2Strength(game)
    print("DD2 fight pools of dungeons.json (enemies, summed health, summed damage per round)")
    pools: dict = {}
    for d in data["dungeons"]:
        for tname, tier in d["tiers"].items():
            for kind in ("hallway", "room"):
                n, hp, dmg = pools[(d["id"], tname, kind)] = strength.pool(tier[kind])
                base = pools.get((d["id"], "apprentice", kind))
                rel = f"   x{hp / base[1]:.2f} hp x{dmg / base[2]:.2f} dmg vs apprentice" if base and tname != "apprentice" else ""
                print(f"  {d['id']:<14} {tname:<10} {kind:<7} {n:4.2f} enemies {hp:6.1f} hp {dmg:5.1f} dmg{rel}")
    domains = [d["id"] for d in data["dungeons"] if "apprentice" in d["tiers"]]
    comp: dict = {}
    for tname in ("veteran", "champion"):
        for kind in ("hallway", "room"):
            comp[(tname, kind)] = (
                _mean(pools[(d, tname, kind)][1] / pools[(d, "apprentice", kind)][1] for d in domains),
                _mean(pools[(d, tname, kind)][2] / pools[(d, "apprentice", kind)][2] for d in domains))
    print("\n  composition alone, mean of the four domains (x hp, x dmg against the apprentice pool of the same kind):")
    for key, val in comp.items():
        print(f"    {key[0]:<9} {key[1]:<7} x{val[0]:.2f} x{val[1]:.2f}")
    gain, pairs = mastery_gain(game)
    print(f"\n  a mastered hero skill deals x{gain:.3f} the damage of the plain one (mean of {pairs} skills)")

    if not (dd1_dir / "monsters").is_dir():
        print(f"\nDD1 install not found at {dd1_dir}: pass --dd1 <dir> for the DD1 half and the derived numbers")
        return 0
    dd1 = Dd1Numbers(dd1_dir)
    growth, enc, gear = dd1.variant_growth(), dd1.encounter_growth(), dd1.gear()
    print(f"\nDD1 monsters, level 3 and 5 against level 1 ({growth[3]['n']} monsters with _A/_B/_C variants)")
    for level in (3, 5):
        g = growth[level]
        print(f"  level {level}: x{g['hp']:.2f} hp  x{g['dmg']:.2f} dmg  {g['spd']:+.1f} spd  {g['atk']:+.1f} acc  {g['crit']:+.1f} crit"
              f"  {g['def']:+.1f} dodge  {g['res']:+.1f} each resistance")
    print("DD1 fights (mash tables of crypts, weald, warrens, cove; x hp, x dmg against level 1 of the same kind)")
    for level in (3, 5):
        print(f"  level {level}: " + "   ".join(f"{k} x{v[0]:.2f} x{v[1]:.2f}" for k, v in enc[level].items()))
    halls = [dd1.fights(d, 1, "hall") for d in dd1.DUNGEONS]
    rooms = [dd1.fights(d, 1, "room") for d in dd1.DUNGEONS]
    print(f"  level 1 hall {_mean(h[0] for h in halls):.2f} monsters {_mean(h[1] for h in halls):.1f} hp,"
          f" room {_mean(r[0] for r in rooms):.2f} monsters {_mean(r[1] for r in rooms):.1f} hp"
          f" (room/hall x{_mean(r[1] for r in rooms) / _mean(h[1] for h in halls):.2f})")
    dd = dd1.darkest_fights()
    rooms5 = [dd1.fights(d, 5, "room") for d in dd1.DUNGEONS]
    print(f"  Darkest Dungeon fights: {dd[0]:.2f} monsters {dd[1]:.1f} hp {dd[2]:.1f} dmg"
          f" (level 5 rooms: {_mean(r[1] for r in rooms5):.1f} hp {_mean(r[2] for r in rooms5):.1f} dmg)")
    print(f"DD1 heroes by weapon/armour level ({gear[0]['n']} classes)")
    for i, g in enumerate(gear):
        print(f"  level {i + 1}: x{g['dmg']:.2f} dmg  x{g['hp']:.2f} hp  {g['crit']:+.1f} crit  {g['spd']:+.1f} spd"
              f"  dodge {g['dodge']:.1f}  skill acc {g['acc']:.1f}")
    aim = {level: dd1.monster_aim(level) for level in dd1.LEVELS}
    for level in dd1.LEVELS:
        print(f"  monsters met at level {level}: skill acc {aim[level][0]:.1f}  dodge {aim[level][1]:.1f}  crit {aim[level][2]:.1f}")

    print("\nDerived shares (multiplier - 1) per tier and kind, next to what dungeons.json stores")
    derived: dict = {}
    hero_hit0 = gear[0]["acc"] - aim[1][1]
    monster_hit0 = aim[1][0] - gear[0]["dodge"]

    def hits(level: int, gear_level: int):
        g = gear[gear_level - 1]
        return ((g["acc"] - aim[level][1]) / hero_hit0,            # DD1 heroes: skill levels against monster dodge
                (aim[level][0] - g["dodge"]) / monster_hit0,       # DD1 monsters: accuracy against armour dodge
                (g["dodge"] - gear[0]["dodge"]) / 100)             # the smith's stand-in for that dodge: less damage taken

    for tname in ("veteran", "champion"):
        level = DD1_LEVEL[tname]
        hero_hit, monster_hit, cut = hits(level, GEAR_AT[tname])
        mastery = 1 + (gain - 1) * MASTERED_SHARE[tname]           # the guild's stand-in for DD1 skill levels
        print(f"  {tname}: DD1 hero hit chance x{hero_hit:.3f}, DD1 monster hit chance x{monster_hit:.3f},"
              f" mastery x{mastery:.3f}, armour takes -{cut:.0%} damage")
        for kind, dd1_kind in (("hallway", "hall"), ("room", "room"), ("boss", "boss")):
            c = comp.get((tname, kind), (1.0, 1.0))
            derived[(tname, kind)] = {
                "hp": max(enc[level][dd1_kind][0] * mastery / hero_hit / c[0] - 1, 0),
                "dmg": max(enc[level][dd1_kind][1] * monster_hit / (1 - cut) / c[1] - 1, 0),
                "speed": growth[level]["spd"], "crit": (aim[level][2] - aim[1][2]) / 100}
    # The Darkest Dungeon: every fight is worth a champion room (DD1's own fights there are), its bosses
    # are champion bosses without the mastery term (DD2's act bosses already expect mastered heroes).
    room = derived[("champion", "room")]
    target = (_mean(pools[(d, "apprentice", "room")][1] for d in domains) * comp[("champion", "room")][0] * (1 + room["hp"]),
              _mean(pools[(d, "apprentice", "room")][2] for d in domains) * comp[("champion", "room")][1] * (1 + room["dmg"]))
    extras = {"speed": growth[5]["spd"], "crit": (aim[5][2] - aim[1][2]) / 100}
    for d in data["dungeons"]:
        if "darkest" not in d["tiers"]:
            continue
        for kind in ("hallway", "room"):
            n, hp, dmg = pools[(d["id"], "darkest", kind)]
            derived[("darkest", kind)] = dict(extras, hp=max(target[0] / hp - 1, 0), dmg=max(target[1] / dmg - 1, 0))
    hero_hit, monster_hit, cut = hits(5, 5)
    derived[("darkest", "boss")] = dict(extras, hp=enc[5]["boss"][0] / hero_hit - 1,
                                        dmg=enc[5]["boss"][1] * monster_hit / (1 - cut) - 1)
    worst = 0.0
    rules = data.get("tier_rules") or {}
    for (tname, kind), want in derived.items():
        have = ((rules.get(tname) or {}).get("scaling") or {}).get(kind) or {}
        line = f"  {tname:<9} {kind:<7}"
        for key in ("hp", "dmg", "speed", "crit"):
            line += f"  {key} {want[key]:+.2f} (stored {have.get(key, 0):+.2f})"
            if key in ("hp", "dmg"):
                worst = max(worst, abs(want[key] - have.get(key, 0)))
        print(line)
    ok = worst <= TOLERANCE
    print(f"\n  largest difference between derived and stored hp/dmg shares: {worst:.2f}"
          + ("" if ok else "  <-- dungeons.json no longer matches the data: re-derive tier_rules.<tier>.scaling"))
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--game", default=os.environ.get("DD2_GAME_DIR", DEFAULT_GAME_DIR),
                    help="Darkest Dungeon II install directory")
    ap.add_argument("--quiet", action="store_true", help="only print problems")
    ap.add_argument("--difficulty", action="store_true",
                    help="print where the tier scaling numbers come from instead of checking ids")
    ap.add_argument("--dd1", default=os.environ.get("DD1_GAME_DIR", DEFAULT_DD1_DIR),
                    help="Darkest Dungeon (1) install directory, for --difficulty")
    args = ap.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    game = GameData(Path(args.game))
    if not game.excel.is_dir() or not game.catalog.is_file():
        print(f"check_content: game data not found under {args.game}\n"
              f"  expected {game.excel}\n  and      {game.catalog}\n"
              f"  pass --game <dir> or set DD2_GAME_DIR", file=sys.stderr)
        return 2
    game.load()

    chk = Checker(game)
    try:
        dungeons = json.loads(DUNGEONS_JSON.read_text(encoding="utf-8"))
        quests = json.loads(QUESTS_JSON.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        print(f"check_content: cannot load data files: {e}", file=sys.stderr)
        return 1
    if args.difficulty:
        return difficulty_report(game, dungeons, Path(args.dd1))
    index = chk.check_dungeons(dungeons)
    chk.check_quests(quests, index)

    if not args.quiet:
        print(f"game data: {game.files} CSV files, {game.elements} elements, {len(game.arenas)} arena scenes")
        print(f"checked:   {len(chk.checked)} element ids, {len(chk.arenas_checked)} arena names")
        by_cls: dict[str, int] = defaultdict(int)
        for _, cls in chk.checked:
            by_cls[cls] += 1
        print("           " + ", ".join(f"{n} {c}" for c, n in sorted(by_cls.items())))
        print()
        for line in chk.summary:
            print(line)
        if chk.notes:
            print()
            for n in chk.notes:
                print("note: " + n)
    if chk.errors:
        print(f"\n{len(chk.errors)} problem(s):", file=sys.stderr)
        for e in chk.errors:
            print("  " + e, file=sys.stderr)
        return 1
    if not args.quiet:
        print("\nOK: every id exists in the game data and all structural checks pass.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
