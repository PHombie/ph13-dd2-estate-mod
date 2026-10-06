#!/usr/bin/env python3
"""Every DD2 combat arena as the game shows it, with no HUD and nobody standing in it.

    python tools/arena_shots.py                  all arenas of the game's catalog
    python tools/arena_shots.py forest caves     only the arenas whose name has one of these words
    python tools/arena_shots.py --restore        put the fight back the way it was (HUD, actors, camera)
    python tools/arena_shots.py --sheets         contact sheets of what was taken, one a biome (no game needed)

A fight must be on (the Estate in COMBAT mode). The fight's own scene stays, with its cameras, lights and post
processing; the HUD's canvases are switched off, the two teams are hidden, and the arena scene under the fight
is exchanged for each `combat_arena_*` scene of StreamingAssets/aa/catalog.json in turn, made the active scene
the way GameTypeMgr does it (the arena's sky, fog and volumes come with that). The colour grade is the LUT
asset that lists the arena (LUTManager): it is set by hand, since the Estate's game type is not the one every
arena belongs to and the manager would leave the last arena's grade on.

The camera is the fight's wide centred one ("InLine Cam - Default"), held still: the fight's own camera leans
to the side of whoever acts.

Pictures go to _lab/shots/arenas/<arena>.png at the game's resolution, and `index.json` there says which LUT
each was taken with. The fight does not go on while its teams are hidden: leave it with --restore, or close
the game.
"""
import json
import os
import re
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import bridge  # noqa: E402

OUT = os.path.join(ROOT, "_lab", "shots", "arenas")
GAME = r"E:\Steam\steamapps\common\Darkest Dungeon® II"
CATALOG = os.path.join(GAME, "Darkest Dungeon II_Data", "StreamingAssets", "aa", "catalog.json")
SCENES = "Assets.Code.Loading.RedHookSceneManagerBhv"
LUTS = "Assets.Code.Rendering.LUTManager.Instance"
CAMERA = "@GameInstaller(Clone)/Main Camera"
DEFAULT_CAM = "@Arena/Combat_Cameras/InLine Cam/InLine Cam - Default:CinemachineVirtualCamera"
CANVASES = ["Arena/CombatUI", "GameInstaller(Clone)/screen_stack", "GameInstaller(Clone)/TooltipCanvas",
            "GameInstaller(Clone)/DragCanvas", "GameInstaller(Clone)/ToastMgr",
            "GameInstaller(Clone)/SubtitlesMgr/SubtitlesCanvas"]
TEAMS = "@Arena/Shared/TeamPositions:Transform"
SETTLE = 5.0


def call(req):
    r = bridge.send(req)
    if not r.get("ok"):
        raise RuntimeError(str(r.get("error"))[:400])
    return r.get("result")


def get(expr, depth=1):
    return call({"cmd": "get", "expr": expr, "depth": depth})


def invoke(expr, method, *args):
    return call({"cmd": "invoke", "expr": expr, "method": method, "args": list(args)})


def put(expr, member, value):
    return call({"cmd": "set", "expr": expr, "member": member, "value": value})


def catalog_arenas():
    with open(CATALOG, encoding="utf-8", errors="ignore") as f:
        text = f.read()
    paths = sorted(set(re.findall(r'Assets/[^"]*combat_arena[^"]*\.unity', text)))
    return [p.rsplit("/", 1)[1][:-len(".unity")] for p in paths]


def loaded_arenas():
    return [s for s in call({"cmd": "observe"})["scenes"] if s.startswith("combat_arena_")]


def lut_table():
    """arena -> index of the first LUT asset (the manager's list is in priority order) that lists it."""
    table = {}
    names = []
    for i in range(get(LUTS + ".m_LUTAssetList.Count")):
        names.append(str(get("%s.m_LUTAssetList[%d]" % (LUTS, i))).split(":")[-1])
        for arena in get("%s.m_LUTAssetList[%d].m_combatArenas" % (LUTS, i), 2) or []:
            table.setdefault(arena, i)
    return table, names


def stage(hidden):
    """The HUD and the teams off (or back), the camera on the fight's wide centred view (or back with the fight)."""
    for path in CANVASES:
        try:
            put("@%s:Canvas" % path, "enabled", not hidden)
        except RuntimeError as e:
            print("  canvas", path, "-", e)
    invoke(TEAMS + ".gameObject", "SetActive", not hidden)
    put(CAMERA + ":CinemachineBrain", "enabled", not hidden)
    if hidden:
        time.sleep(0.3)
        position = get(DEFAULT_CAM + ".transform.position")
        angles = get(DEFAULT_CAM + ".transform.eulerAngles")
        xyz = lambda v: {"x": v["x"], "y": v["y"], "z": v["z"]}
        put(CAMERA + ":Camera.transform", "position", xyz(position))
        put(CAMERA + ":Camera.transform", "eulerAngles", xyz(angles))
        put(CAMERA + ":Camera", "fieldOfView", get(DEFAULT_CAM + ".m_Lens.FieldOfView"))


def swap(arena, timeout=90):
    for old in loaded_arenas():
        if old == arena:
            return True
        invoke(SCENES, "UnloadAdditiveSceneByForce", old)
    deadline = time.time() + timeout
    while loaded_arenas() or get(SCENES + ".Instance.UnloadOperationsInFlight()"):
        if time.time() > deadline:
            return False
        time.sleep(0.2)
    invoke(SCENES, "LoadSceneAdditively", arena, None, True)
    while arena not in loaded_arenas() or get("UnityEngine.SceneManagement.SceneManager.GetActiveScene().name") != arena:
        if time.time() > deadline:
            return False
        time.sleep(0.2)
    return True


def shoot(path):
    if os.path.exists(path):
        os.remove(path)
    call({"cmd": "shot", "path": path})
    for _ in range(100):
        if os.path.exists(path) and os.path.getsize(path) > 0:
            time.sleep(0.3)
            return True
        time.sleep(0.1)
    return False


def group(arena):
    """The biome an arena belongs to, by its name; the Shrine of Reflection's arenas are one group."""
    rest = arena[len("combat_arena_"):]
    if rest.startswith("hero_story"):
        return "hero_story"
    if rest.startswith("kingdom_camp") or rest == "stressworld":
        return "other"
    return rest.split("_", 1)[0]


def sheets(columns=3, width=853):
    from PIL import Image, ImageDraw
    folder = os.path.join(OUT, "_sheets")
    os.makedirs(folder, exist_ok=True)
    groups = {}
    for name in sorted(f[:-4] for f in os.listdir(OUT) if f.endswith(".png")):
        groups.setdefault(group(name), []).append(name)
    for biome, names in sorted(groups.items()):
        height = width * 9 // 16
        rows = -(-len(names) // columns)
        sheet = Image.new("RGB", (columns * width, rows * height), (12, 12, 12))
        for i, name in enumerate(names):
            tile = Image.open(os.path.join(OUT, name + ".png")).convert("RGB").resize((width, height), Image.LANCZOS)
            draw = ImageDraw.Draw(tile)
            label = name[len("combat_arena_"):]
            draw.rectangle((0, 0, 12 + 7 * len(label), 18), fill=(0, 0, 0))
            draw.text((6, 3), label, fill=(255, 220, 90))
            sheet.paste(tile, ((i % columns) * width, (i // columns) * height))
        path = os.path.join(folder, biome + ".jpg")
        sheet.save(path, quality=88)
        print(path, len(names))


def main(argv):
    if "--sheets" in argv:
        sheets()
        return 0
    if "--restore" in argv:
        stage(False)
        print("restored")
        return 0
    words = [a for a in argv if not a.startswith("-")]
    arenas = [a for a in catalog_arenas() if not words or any(w in a for w in words)]
    if get("Assets.Code.Game.GameModeMgr.CurrentMode.m_name") != "COMBAT" and "--any-mode" not in argv:
        print("no fight is on (mode %s)" % get("Assets.Code.Game.GameModeMgr.CurrentMode.m_name"))
        return 1
    os.makedirs(OUT, exist_ok=True)
    index_path = os.path.join(OUT, "index.json")
    index = json.load(open(index_path)) if os.path.exists(index_path) else {}
    luts, lut_names = lut_table()
    stage(True)
    failed = []
    for n, arena in enumerate(arenas, 1):
        start = time.time()
        try:
            if not swap(arena):
                raise RuntimeError("the scene did not load")
            lut = luts.get(arena)
            if lut is not None:
                invoke(LUTS, "SetLUT", "=%s.m_LUTAssetList[%d]" % (LUTS, lut), False, None)
            time.sleep(SETTLE)
            used = str(get(LUTS + ".m_currentLUTAsset")).split(":")[-1]
            if not shoot(os.path.join(OUT, arena + ".png")):
                raise RuntimeError("no picture was written")
            index[arena] = {"lut": used, "listed_lut": lut_names[lut] if lut is not None else None}
            print("%3d/%d %-55s %-32s %.1fs" % (n, len(arenas), arena, used, time.time() - start), flush=True)
        except (RuntimeError, OSError) as e:
            failed.append(arena)
            print("%3d/%d %-55s FAILED: %s" % (n, len(arenas), arena, e), flush=True)
        json.dump(index, open(index_path, "w"), indent=1, sort_keys=True)
    print("done: %d taken, %d failed %s" % (len(arenas) - len(failed), len(failed), failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
