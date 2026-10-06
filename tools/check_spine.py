#!/usr/bin/env python3
"""Check the plugin's Spine reader against the Python reference, on the player's own DD1 art.

src/DD2Estate/Dd1/SpineSkeleton.cs cannot be watched at work outside the game, so its numbers are checked instead:
tests/SpineCheck builds that very file against stand-ins for Unity and writes every pose of every skeleton it
finds (the setup pose, and each animation at its start, two moments between and its end); this script computes
the same poses with the reader of tools/preview_corridor.py, whose drawings can be looked at, and compares
slot by slot, colour by colour, vertex by vertex. The setup pose's region parts are also compared with the
hamlet's own reference reader (tools/preview_hamlet.py), which the town view was tuned against.

    python tools/check_spine.py [--dd1 <install>] [--folders props fx] [--out <file>]

The plugin reader's poses are written to --out (default _lab/spine_poses.txt, gitignored). Exit code: the number
of differences (0 when the two readers agree).
"""
import argparse
import os
import subprocess
import sys

import preview_corridor as corridor
import preview_hamlet as hamlet

VERTEX_TOLERANCE = 0.05          # pixels: the two readers differ by float rounding only
COLOUR_TOLERANCE = 0.005


def dump(dd1, folders, path):
    here = os.path.dirname(os.path.abspath(__file__))
    project = os.path.join(here, "..", "tests", "SpineCheck")
    run = subprocess.run(["dotnet", "run", "--project", project, "-c", "Release", "--", dd1, path] + folders, capture_output=True, text=True)
    sys.stdout.write(run.stdout)
    if not os.path.isfile(path):
        sys.stderr.write(run.stderr)
        raise SystemExit("tests/SpineCheck did not run")


def compare(dd1, path):
    with open(path, encoding="utf8") as f:
        lines = f.read().splitlines()
    problems = []
    worst = 0.0
    poses = pieces_checked = parts_checked = 0
    skel = regions = rel = None
    i = 0
    while i < len(lines):
        line = lines[i]
        i += 1
        if line.startswith("FAILED"):
            problems.append("the plugin's reader cannot read " + line.split(" ", 1)[1])
        elif line.startswith("SKEL "):
            rel = line.split(" ")[1]
            base = os.path.join(dd1, rel.replace("/", os.sep))
            with open(base, "rb") as f:
                data = f.read()
            try:
                skel = corridor.read_skeleton(data)
            except Exception as e:
                problems.append("the reference cannot read %s: %r" % (rel, e))
                skel = None
            atlas = base[:-len(".skel")] + ".atlas"
            regions = None
            if os.path.isfile(atlas):
                with open(atlas, encoding="utf-8-sig") as f:
                    regions = corridor.parse_atlas(f.read())[1]
            count = int(line.split(" ")[3])
            try:
                old = hamlet.setup_pose(hamlet.read_skeleton(data))
            except ValueError:          # the hamlet's reader stops at a skinned mesh (no town building has one)
                old = None
            if old is not None:
                if count != len(old):
                    problems.append("%s: %d setup parts, the hamlet's reader has %d" % (rel, count, len(old)))
                for k in range(min(count, len(old))):
                    f, o = lines[i + k].split(" "), old[k]
                    got = [float(v) for v in f[3:9]]
                    want = [o["cx"], o["cy"], o["w"], o["h"], o["rot"], o["alpha"]]
                    if f[1] != o["slot"] or f[2] != o["region"] or max(abs(a - b) for a, b in zip(got, want)) > 0.01:
                        problems.append("%s setup part %d: %s against %s" % (rel, k, " ".join(f[1:]), o))
                    parts_checked += 1
            i += count
        elif line.startswith("POSE "):
            _, name, share, count = line.split(" ")
            count = int(count)
            if skel is None:
                i += count
                continue
            animation = None if name == "-" else name
            duration = skel["animations"][animation]["duration"] if animation else 0.0
            want = corridor.pose(skel, animation, duration * float(share), regions)
            poses += 1
            where = "%s %s at %s" % (rel, name, share)
            if len(want) != count:
                problems.append("%s: %d pieces, the reference has %d" % (where, count, len(want)))
                i += count
                continue
            for k in range(count):
                f, p = lines[i + k].split(" "), want[k]
                if f[0] != p["slot"] or f[1] != p["region"] or int(f[2]) != len(p["triangles"]):
                    problems.append("%s piece %d: %s against %s/%s" % (where, k, " ".join(f[:3]), p["slot"], p["region"]))
                    continue
                colour = max(abs(a - b) for a, b in zip([float(v) for v in f[3:7]], p["colour"]))
                got, flat = [float(v) for v in f[7:]], [c for v in p["vertices"] for c in v]
                if len(got) != len(flat):
                    problems.append("%s piece %d (%s): %d numbers against %d" % (where, k, p["slot"], len(got), len(flat)))
                    continue
                off = max(abs(a - b) for a, b in zip(got, flat)) if got else 0.0
                worst = max(worst, off)
                if colour > COLOUR_TOLERANCE or off > VERTEX_TOLERANCE:
                    problems.append("%s piece %d (%s): colour off by %.3f, vertices by %.3f" % (where, k, p["slot"], colour, off))
                pieces_checked += 1
            i += count
    print("%d poses, %d pieces, %d setup parts compared; largest vertex difference %.4f px" % (poses, pieces_checked, parts_checked, worst))
    return problems


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--dd1", default=os.environ.get("DD1_ROOT", r"E:\Steam\steamapps\common\DarkestDungeon"))
    ap.add_argument("--folders", nargs="+", default=["props", "fx"], help="DD1 folders whose skeletons are read")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "_lab", "spine_poses.txt"),
                    help="where the plugin reader's poses are written")
    args = ap.parse_args()
    path = os.path.abspath(args.out)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if os.path.isfile(path):
        os.remove(path)
    dump(args.dd1, args.folders, path)
    problems = compare(args.dd1, path)
    print("%d differences" % len(problems))
    for problem in problems[:40]:
        print("  " + problem)
    return len(problems)


if __name__ == "__main__":
    sys.exit(main())
