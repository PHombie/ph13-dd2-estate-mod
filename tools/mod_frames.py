#!/usr/bin/env python3
"""mod_frames.py [name ...]: the mod's screens as they stand in game, photographed in the states DD1 was
photographed in (_lab/dd1_ref/town, docs/recon/dd1-reference-frames.md), saved at 1920x1080 into
_lab/mod_now/<DD1's frame name>.png, so the two can be held against each other pixel for pixel.

Needs the game running in the Estate's hamlet with the dev bridge (python tools/dev.py start, estate.enter).
Everything is done through bridge commands and `ui.hover`, so it works while another window has the focus.
It opens every building without asking (locks.state open=all) and changes nothing else that is kept: no hero is
placed for good, nothing is bought. With names: only those frames.

    python tools/mod_frames.py                     every frame
    python tools/mod_frames.py town_tavern town_guild_hero_in
    python tools/mod_frames.py --pairs             also writes _lab/mod_now/pairs/<name>.png: DD1 over the mod
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402
import dev  # noqa: E402
import play_dungeon as pd  # noqa: E402
from PIL import Image  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "_lab", "mod_now")
DD1 = os.path.join(ROOT, "_lab", "dd1_ref", "town")

CLOSE = ["upgrade.close", "activity.close", "sanitarium.close", "roster.close", "wagon.close", "inventory.close",
         "log.close", "glossary.close", "heirlooms.close", "events.close", "memoirs.close", "districts.close",
         "survivalist.close", "provision.back", "quests.close", "results.close"]

# name of DD1's frame -> the bridge commands that put the mod into the same state; ("hover", x, y) rests the pointer
# at a place given in DD1's 1920x1080 pixels from the top left.
HERO = 5        # a hero of the roster who is not in the party
FRAMES = [
    ("town_at_rest_week2_all_buildings", []),
    ("town_tavern", [("activity.open", {"building": "tavern"})]),
    ("town_tavern_hero_in", [("activity.open", {"building": "tavern"}), ("activity.stand", {"activity": "gambling", "slot": 0, "guid": HERO})]),
    ("town_tavern_upgrades", [("upgrade.open", {"building": "tavern"})]),
    ("town_abbey", [("activity.open", {"building": "abbey"})]),
    ("town_abbey_upgrades", [("upgrade.open", {"building": "abbey"})]),
    ("town_sanitarium", [("sanitarium.open", {})]),
    ("town_sanitarium_quirk_selected_cost", [("sanitarium.open", {}), ("sanitarium.pick", {"ward": "treatment", "slot": 0, "guid": HERO, "quirk": "first"})]),
    ("town_sanitarium_upgrades", [("upgrade.open", {"building": "sanitarium"})]),
    ("town_stage_coach", [("roster.open", {"building": "stage_coach"})]),
    ("town_stage_coach_recruit_tooltip", [("roster.open", {"building": "stage_coach"}), ("hover", 1100, 224)]),
    ("town_stage_coach_upgrades", [("upgrade.open", {"building": "stage_coach"})]),
    ("town_guild", [("guild.open", {})]),
    ("town_guild_hero_in", [("guild.open", {}), ("window.pick", {"guid": HERO})]),
    ("town_guild_upgrades", [("upgrade.open", {"building": "guild"})]),
    ("town_blacksmith", [("smith.open", {})]),
    ("town_blacksmith_hero_in", [("smith.open", {}), ("window.pick", {"guid": HERO})]),
    ("town_blacksmith_upgrades", [("upgrade.open", {"building": "blacksmith"})]),
    ("town_survivalist", [("survivalist.open", {})]),
    ("town_survivalist_hero_in", [("survivalist.open", {}), ("window.pick", {"guid": HERO})]),
    ("town_survivalist_upgrades", [("upgrade.open", {"building": "camping_trainer"})]),
    ("town_nomad_wagon", [("wagon.open", {})]),
    ("town_nomad_wagon_trinket_tooltip", [("wagon.open", {}), ("hover", 918, 310)]),
    ("town_nomad_wagon_upgrades", [("upgrade.open", {"building": "nomad_wagon"})]),
    ("town_graveyard", [("roster.open", {"building": "graveyard"})]),
    ("town_statue_ancestors_memoirs", [("memoirs.open", {})]),
    ("estate_bar_2_activity_log", [("log.open", {})]),
    ("estate_bar_3_trinket_inventory", [("inventory.open", {"guid": 1})]),
    ("estate_bar_4_glossary", [("glossary.open", {})]),
    ("estate_bar_heirloom_exchange", [("heirlooms.open", {})]),
    ("estate_bar_tooltip_gold", [("hover", 135, 1000)]),
    ("estate_bar_tooltip_heirlooms", [("hover", 369, 1009)]),
    ("estate_bar_tooltip_heirloom_exchange", [("hover", 700, 1009)]),
    ("estate_bar_tooltip_2", [("hover", 1470, 1003)]),
    ("estate_bar_tooltip_3", [("hover", 1580, 1003)]),
    ("estate_bar_tooltip_4", [("hover", 1690, 1003)]),
    ("estate_bar_tooltip_5", [("hover", 1800, 1003)]),
    ("town_embark_tooltip", [("hover", 957, 1010)]),
    ("estate_crest_tooltip", [("hover", 110, 110)]),
    ("roster_count_tooltip", [("hover", 1626, 96)]),
    ("roster_sort_tooltip_1", [("hover", 1714, 96)]),
    ("roster_sort_tooltip_2", [("hover", 1752, 96)]),
    ("roster_sort_tooltip_3", [("hover", 1790, 96)]),
    ("roster_sort_tooltip_4", [("hover", 1828, 96)]),
    ("roster_hero_tooltip", [("hover", 1720, 190)]),
    ("town_estate_map_quest_select", [("quests.open", {}), ("quests.select", {"index": 0})]),
    ("town_provision_screen", [("quests.open", {}), ("quests.select", {"index": 0}), ("quests.forward", {}), ("quests.forward", {})]),
]


def quiet(name, **kw):
    try:
        return pd.run(name, **kw)
    except Exception as e:   # a command another build does not have, a screen that is not open
        return str(e)


def shot(name):
    path = os.path.join(dev.SHOTS, "_mod_frame.png")
    if os.path.exists(path):
        os.remove(path)
    bridge.send({"cmd": "shot", "path": path})
    for _ in range(200):
        if os.path.exists(path) and os.path.getsize(path) > 0:
            break
        time.sleep(0.02)
    time.sleep(0.15)
    image = Image.open(path).convert("RGB")
    size = image.size
    image = image.resize((1920, 1080), Image.LANCZOS)
    image.save(os.path.join(OUT, name + ".png"))
    return image, size


def main(argv):
    pairs = "--pairs" in argv
    wanted = [a for a in argv if not a.startswith("--")]
    os.makedirs(OUT, exist_ok=True)
    if pairs:
        os.makedirs(os.path.join(OUT, "pairs"), exist_ok=True)
    if pd.mode() != "ESTATE":
        print("the game is not in the Estate")
        return 1
    quiet("locks.state", open="all")
    quiet("window.animate", on=False)
    quiet("narration.hush")
    done = 0
    for name, steps in FRAMES:
        if wanted and name not in wanted:
            continue
        quiet("ui.hover", x=-10, y=-10)
        for c in CLOSE:
            quiet(c)
        time.sleep(0.4)
        notes = []
        for step in steps:
            if step[0] == "hover":
                # the game's screen is 16:9; its size comes with a picture
                _, size = shot("_probe")
                result = quiet("ui.hover", x=step[1] * size[0] / 1920.0, y=(1080 - step[2]) * size[1] / 1080.0)
                notes.append("hover " + (str(result.get("hit")) if isinstance(result, dict) else str(result))[-70:])
            else:
                kw = dict(step[1])
                if kw.get("quirk") == "first":
                    options = quiet("sanitarium.options", guid=kw["guid"])
                    kw.pop("quirk")
                    try:
                        # the first quirk the treatment ward would REMOVE (DD1's frame has a negative quirk chosen)
                        ward = next(w for w in options["wards"] if w["ward"] == "treatment")
                        pick = next((o for o in ward["options"] if o.get("treatment") == "remove"), None) or ward["options"][0]
                        kw["quirk"] = pick["quirk"]
                    except Exception:
                        pass
                result = quiet(step[0], **kw)
                if isinstance(result, str) and ("unknown dev command" in result or "Exception" in result):
                    notes.append(step[0] + ": " + result[:80])
                time.sleep(0.5)
        time.sleep(1.0)
        quiet("narration.hush")
        image, _ = shot(name)
        done += 1
        print(name, "; ".join(notes))
        twin = os.path.join(DD1, name + ".png")
        if pairs and os.path.exists(twin):
            dd1 = Image.open(twin).convert("RGB")
            both = Image.new("RGB", (1920, 2160))
            both.paste(dd1, (0, 0))
            both.paste(image, (0, 1080))
            both.save(os.path.join(OUT, "pairs", name + ".png"))
    quiet("ui.hover", x=-10, y=-10)
    for c in CLOSE:
        quiet(c)
    probe = os.path.join(OUT, "_probe.png")
    if os.path.exists(probe):
        os.remove(probe)
    print(done, "frames in", OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
