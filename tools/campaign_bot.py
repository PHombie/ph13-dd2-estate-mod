#!/usr/bin/env python3
"""Plays the estate for a number of weeks through the dev bridge, the way a player's week goes: hire, send the
worn-out to the tavern or abbey, learn a skill, build what the heirlooms pay for, pick a quest the party will take,
provision, play the dungeon (fights are forced to a win: this is a robustness run, not a balance one), come home.
After every week it prints the estate's books and any error the game's log has gained, and has
tools/ledger_check.py look at them: DD2's own inventory must be empty, the purse must have moved by its journal
alone and not at all while the party was out, and the expedition's record must agree with the purse and with
the results screen (lines starting "books:"; a fault is a line starting "BOOKS:").

An expedition ends on DD1's results screens: the bot presses their button ("Next", "Return to Town") until the
hamlet is back, as a player does (come_home).

    python -u tools/campaign_bot.py [weeks]      (the Estate must be open in its hamlet)
"""
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ledger_check  # noqa: E402
import play_dungeon as pd  # noqa: E402

LOG = r"E:\Steam\steamapps\common\Darkest Dungeon® II\BepInEx\LogOutput.log"
WALK_SPEED = 3.0


def invoke(expr, method, *args):
    return pd.call({"cmd": "invoke", "expr": expr, "method": method, "args": list(args)})


def get(expr):
    return pd.call({"cmd": "get", "expr": expr})


def errors():
    try:
        with open(LOG, encoding="utf-8", errors="ignore") as f:
            return [line.strip()[:200] for line in f if "[Error" in line or "Exception" in line or "given up" in line]
    except OSError:
        return []


def heroes():
    return pd.run("roster.state")["heroes"]


def stress_of():
    return {h["guid"]: h["stress"] for h in pd.run("estate.state")["heroes"]}


def town(rng):
    """The week's errands."""
    invoke("DD2Estate.Estate.NarrationBox", "Hide")
    done = []
    # hire while the barracks have room
    for _ in range(4):
        coach = pd.run("roster.state")["stageCoach"]
        if coach["roster"] >= coach["rosterSize"] or not coach["offer"]:
            break
        r = pd.run("roster.hire", index=0)
        if not str(r).startswith("hired"):
            break
        done.append(str(r))
    # the stressed go to the tavern or the abbey, if a slot is open and the purse allows
    stress = stress_of()
    rules = pd.run("activity.rules")
    activities = [(a["building"], a["activity"]) for a in rules] if isinstance(rules, list) else []
    rng.shuffle(activities)
    for h in sorted(heroes(), key=lambda h: -stress.get(h["guid"], 0)):
        if stress.get(h["guid"], 0) < 5 or h.get("dismissBlocked") not in (None, "In the party"):
            continue
        if h["status"] == "party":
            invoke("DD2Estate.Dd2.EstateSession", "ToggleParty", h["guid"])
        for building, activity in activities:
            r = pd.run("activity.place", guid=h["guid"], building=building, activity=activity)
            if str(r) == "placed":
                done.append("%s to the %s (%s)" % (h["name"], building, activity))
                break
    # a skill at the guild
    gold = get("DD2Estate.Estate.EstateState.Gold")
    if gold > 4500:
        pick = rng.choice(heroes())
        state = pd.run("guild.state", guid=pick["guid"])
        locked = [k["id"] for k in state.get("skills", []) if not k["known"]] if isinstance(state, dict) else []
        if locked:
            r = pd.run("guild.learn", guid=pick["guid"], skill=locked[0])
            done.append("%s learns %s: %s" % (pick["name"], locked[0], r))
    # whatever the heirlooms pay for
    for building in ("stage_coach", "guild", "blacksmith", "tavern", "abbey", "sanitarium", "nomad_wagon", "camping_trainer"):
        listing = pd.run("upgrade.list", building=building)
        for tree in listing.get("trees", []) if isinstance(listing, dict) else []:
            if tree.get("blocked") is None and tree["level"] < tree["of"]:
                r = pd.run("upgrade.buy", tree=tree["tree"])
                if str(r).startswith("built"):
                    done.append(str(r))
    return done


def pick_party_and_quest(rng):
    """Four free heroes, the least stressed first, and a quest all of them will take."""
    stress = stress_of()
    hs = heroes()
    for h in hs:
        if h["status"] == "party":
            invoke("DD2Estate.Dd2.EstateSession", "ToggleParty", h["guid"])
    free = [h for h in heroes() if not h.get("dismissBlocked")]
    free.sort(key=lambda h: stress.get(h["guid"], 0))
    for h in free[:4]:
        invoke("DD2Estate.Dd2.EstateSession", "ToggleParty", h["guid"])
    quests = pd.run("quests.list")
    order = list(range(len(quests)))
    rng.shuffle(order)
    # story quests first: they move the estate on
    order.sort(key=lambda i: 0 if quests[i].get("Plot") else 1)
    pd.run("quests.open")
    for i in order:
        m = pd.run("quests.select", index=i)
        if isinstance(m, dict) and m.get("ready"):
            return quests[i], m
        # a hero refuses: swap the refusers for others and look again
        if isinstance(m, dict):
            refusers = [p["guid"] for p in m.get("party", []) if p.get("refuses")]
            if refusers and len(refusers) < 4:
                spare = [h for h in free[4:] if h["guid"] not in refusers]
                for guid in refusers:
                    invoke("DD2Estate.Dd2.EstateSession", "ToggleParty", guid)
                    if spare:
                        invoke("DD2Estate.Dd2.EstateSession", "ToggleParty", spare.pop(0)["guid"])
                m = pd.run("quests.select", index=i)
                if isinstance(m, dict) and m.get("ready"):
                    return quests[i], m
    return None, None


def week(rng):
    notes = town(rng)
    quest, _ = pick_party_and_quest(rng)
    if quest is None:
        pd.run("quests.close")
        notes.append("nobody would take any quest: the week passes in town")
        pd.run("roster.week")
        return notes
    for _ in range(2):          # a party under four is asked twice
        pd.run("quests.forward")
        time.sleep(2.0)
        p = pd.run("provision.state")
        if isinstance(p, dict) and p.get("open"):
            break
    pd.run("provision.kit")
    pd.no_key_at_loading()
    pd.run("provision.setout")
    time.sleep(12)
    if pd.run("dungeon.state").get("view") != "Dungeon":
        notes.append("the expedition did not start")
        return notes
    invoke("DD2Estate.Estate.NarrationBox", "Hide")
    pd.call({"cmd": "set", "expr": "DD2Estate.Dungeon.CorridorView", "member": "WalkSpeedScale", "value": WALK_SPEED})
    notes.append("quest: %s (%s, %s, %s, %s)" % (quest["Name"], quest["Dungeon"], quest["Type"], quest["Tier"], quest["LengthName"]))
    try:
        code = pd.main(1500)
    except Exception as e:
        notes.append("the dungeon run stopped: " + str(e)[:160])
        code = 1
    if code != 0 and pd.run("dungeon.state").get("view") == "Dungeon":
        pd.run("dungeon.leave")
        time.sleep(3)
        s = pd.run("dungeon.state")
        if s.get("prompt"):
            pd.run("dungeon.answer", option=0)
        notes.append("abandoned")
    notes.extend(come_home())
    return notes


def come_home():
    """DD1's results pages stand between the expedition and the hamlet: the red button is pressed until they are
    gone (it finishes a reveal, turns the page, opens the masks, returns to town), then the town's loading
    picture is waited out."""
    notes = []
    time.sleep(2)
    for _ in range(12):
        try:
            r = pd.run("results.state")
        except RuntimeError:
            break               # a plugin from before the results pages
        if not (isinstance(r, dict) and r.get("open")):
            break
        if not notes:
            notes.append("results: %s, %s" % (r.get("outcome"), r.get("quest")))
        pd.run("results.next")
        time.sleep(1.2)
    else:
        notes.append("the results pages did not close")
    t0 = time.time()
    while time.time() - t0 < 40:
        try:
            up = pd.run("loading.state").get("up")
        except RuntimeError:
            up = False
        s = pd.run("dungeon.state")
        if not up and s.get("view") == "Hamlet":
            break
        time.sleep(0.5)
    else:
        notes.append("the hamlet did not come up after the expedition")
    time.sleep(2)
    invoke("DD2Estate.Estate.NarrationBox", "Hide")
    return notes


def main(weeks):
    rng = random.Random(7)
    seen = len(errors())
    for n in range(weeks):
        before = pd.run("roster.state")["stageCoach"]["week"]
        try:
            books = ledger_check.mark()
        except (RuntimeError, OSError, KeyError, TypeError) as e:
            books = None
            print("   books: no mark (%s)" % str(e)[:120], flush=True)
        notes = week(rng)
        if books is not None:
            notes.extend(ledger_check.week(books))
        state = pd.run("roster.state")
        gold = get("DD2Estate.Estate.EstateState.Gold")
        held = pd.run("heirlooms.state")["exchange"]["held"]
        errs = errors()
        print("WEEK %d -> %d | heroes %d | gold %s | %s | fallen %d" % (before, state["stageCoach"]["week"], len(state["heroes"]), gold, held, len(state["fallen"])), flush=True)
        for note in notes:
            print("   " + note, flush=True)
        for e in errs[seen:]:
            print("   LOG: " + e, flush=True)
        seen = len(errs)
        if state["stageCoach"]["week"] == before:
            print("   the week did not advance: stopping", flush=True)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 10))
