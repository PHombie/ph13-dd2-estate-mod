#!/usr/bin/env python3
"""Checks the estate's books through the dev bridge ("one inventory": docs/recon/inventory-unification.md).

    python tools/ledger_check.py                the books as they stand; exit code 1 if anything is wrong
    python tools/ledger_check.py since <N>      the same, with the purse's journal read from entry N on
    python tools/ledger_check.py mark           prints the purse and the journal's next entry number, to check from later

What is checked:
  1. DD2 holds nothing of its own (ledger.state: clean): its player inventory is empty, no hero carries a combat
     item, no hero upgrade points, nothing waits for DD2's loot window.
  2. The purse moves through its journal and by nothing else (ledger.purse): the gold now less the gold at the
     mark is what the entries since add up to.
  3. Nothing reaches or leaves the purse in the middle of an expedition: no entry is marked "expedition". What
     a party finds, a fight's loot included, lies in the bag until the party is home.
  4. The record of the expedition that ended last agrees (ledger.books): the purse moved by the quest's pay plus
     the haul; and the results screen's rows add up to the same (results.state).

tools/campaign_bot.py calls mark() before a week and week(mark) after it and prints what comes back: the week's
line (purse before and after, quest pay, haul, other income, spending) and the faults found, if any.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge  # noqa: E402

QUEST_PAY = ("QuestBoard",)             # the class that pays a quest's reward (the journal names the caller)
HAUL = ("haul",)                        # DungeonRun.BringHome's word for the bag's worth


def run(name, **kw):
    r = bridge.send(dict(cmd="run", name=name, **kw))
    if not r.get("ok"):
        raise RuntimeError(r.get("error"))
    return r.get("result")


def mark():
    """Where the purse and its journal stand now."""
    state = run("ledger.state")
    if not isinstance(state, dict):
        raise RuntimeError(str(state))
    estate = state["estate"]
    return {"gold": estate["gold"], "next": estate["purseJournalNext"], "week": estate.get("week")}


def check(since=None):
    """Returns (line, problems): a line on what moved the purse since the mark, and what is wrong (empty: nothing)."""
    problems = []
    state = run("ledger.state")
    if not isinstance(state, dict):
        return str(state), [str(state)]
    if not state.get("clean"):
        problems.append("DD2 holds things of its own: " + "; ".join(state.get("strays") or ["?"]))

    line = "purse %d" % state["estate"]["gold"]
    if since is not None:
        purse = run("ledger.purse", since=since["next"])
        entries = purse["entries"]
        if not purse.get("complete"):
            problems.append("the purse's journal no longer reaches back to entry %d: the sums below are short" % since["next"])
        moved = purse["gold"] - since["gold"]
        if moved != purse["sum"]:
            problems.append("the purse moved by %+d but its journal adds up to %+d: something changed it past the journal" % (moved, purse["sum"]))
        quest = sum(e["amount"] for e in entries if e["why"] in QUEST_PAY)
        haul = sum(e["amount"] for e in entries if e["why"] in HAUL)
        spent = -sum(e["amount"] for e in entries if e["amount"] < 0)
        other = sum(e["amount"] for e in entries if e["amount"] > 0 and e["why"] not in QUEST_PAY + HAUL)
        for e in entries:
            if e["where"] == "expedition":
                problems.append("%+d gold (%s) in the middle of an expedition, week %s: the purse is not to move before the party is home"
                                % (e["amount"], e["why"], e["week"]))
        line = "purse %d -> %d (%+d): quest pay %d, haul %d, other income %d, spending %d" % (since["gold"], purse["gold"], moved, quest, haul, other, spent)
        if other:
            line += " [other: " + ", ".join("%s %+d" % (k, v) for k, v in sorted(purse["byWhy"].items()) if k not in QUEST_PAY + HAUL and v > 0) + "]"

    books = run("ledger.books")
    if isinstance(books, dict):
        if not books.get("agrees"):
            problems.append("the last expedition's books do not agree: the purse went %d -> %d, the quest paid %d and the haul %d"
                            % (books["purseBefore"], books["purseAfter"], books["questGold"], books["haulGold"]))
        results = run("results.state")
        record = results.get("record") or results.get("last") if isinstance(results, dict) else None
        if isinstance(record, dict) and record.get("books") and not record.get("agreesWithThePurse", True):
            problems.append("the results screen's rows do not add up to what the purse got: treasure %s (DD1 gold), the haul paid %s"
                            % (record.get("treasureGold"), record["books"].get("haulGold")))
    return line, problems


def week(since):
    """For the campaign bot: the check since a mark, as lines to print."""
    try:
        line, problems = check(since)
    except (RuntimeError, OSError, KeyError, TypeError) as e:
        return ["books: could not be read (%s)" % str(e)[:160]]
    return ["books: " + line] + ["BOOKS: " + p for p in problems]


def main(argv):
    if argv and argv[0] == "mark":
        m = mark()
        print("gold %(gold)s, journal next %(next)s, week %(week)s" % m)
        return 0
    since = None
    if len(argv) >= 2 and argv[0] == "since":
        since = {"next": int(argv[1]), "gold": None, "week": None}
        # the purse as it stood before the first entry asked for: the entry's own "after" less its amount
        purse = run("ledger.purse", since=since["next"])
        first = purse["entries"][0] if purse["entries"] else None
        since["gold"] = first["after"] - first["amount"] if first else purse["gold"]
    line, problems = check(since)
    print(line)
    for p in problems:
        print("WRONG: " + p)
    if not problems:
        print("the books agree")
    return 1 if problems else 0


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    sys.exit(main(sys.argv[1:]))
