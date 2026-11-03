# killspare.py - sweep tool T4 (SWEEP_PLAN.md 3.2 Q2): kill and catch steps have a spare animal.
#
# The user 2026-11-03: "If it has to do with for example killing things, then perhaps include an extra monster incase
# it despawns or something." Rule: an island places at least (animals its kill/catch steps need) + 1 of each kind.
#
# Offline, read only, over the shipped .island files (library islands/*/ and plans/*/). Per island and creature kind:
#   need    = the sum of the counts of every kill/catch step of that kind in all the island's quests (a kill made early
#             counts for a later step, QuestTracker.Remember, and a dead animal stays dead until the regrow days)
#   placed  = creature spots of that kind (Creature_<Type>), each creature.count (1..8, default 1)
#   hidden  = of those, spots that start hidden (beh.hidden): they spawn only when the island shows them
#   summons = bosses that call that kind (boss.summonKind; counted as 0 spare, they come only in the fight)
# Verdict: OK (placed >= need + 1), NOSPARE (placed == need), SHORT (placed < need: the quest can't be finished from
# what the island places), HIDDEN (enough only when the hidden spots count), BOSS (no spare, but the animals needed are
# boss spots (boss.* settings): a spare boss is a design call, SWEEP_PLAN decision D3). Same island in several plan copies with the
# same counts = one row, "+n copies".
# The in-game part (live animals at the step's start) is CIQuestNeeds (T5): it counts the spawned, alive ones.
#
#   python tools/killspare.py [--deploy DIR] [--out FILE] [--all]      (--all also lists OK rows)
# Exit 0 = no SHORT/NOSPARE, 1 = some.
import argparse, glob, os, re, sys
from collections import OrderedDict, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import islandfile
from questlint import parse_steps, TYPE_LABEL, recipe_lines, locate, DEPLOY, RECIPES

ORDER = {"SHORT": 0, "NOSPARE": 1, "HIDDEN": 2, "BOSS": 3, "OK": 4}


def label_of(type_name):
    t = type_name.lower()
    return TYPE_LABEL.get(t, t)


def check(path):
    f = islandfile.load(path, heights=False)
    need, steps = defaultdict(int), defaultdict(list)
    for n in range(9):
        k = "quest" if n == 0 else "quest%d" % (n + 1)
        for i, s in enumerate(parse_steps(f.props.get(k + ".steps", ""))):
            if s["type"] in ("kill", "catch") and s["target"]:
                lab = s["target"].lower()
                need[lab] += s["count"]
                steps[lab].append("q%d s%d %s %d \"%s\"" % (n + 1, i + 1, s["type"], s["count"], s["text"]))
    placed, hidden, summons, spots, bosses = defaultdict(int), defaultdict(int), defaultdict(int), defaultdict(int), defaultdict(int)
    for o in f.objects:
        p = o.props
        if o.name.startswith("Creature_"):
            lab = label_of(o.name[len("Creature_"):])
            try: c = max(1, min(8, int(p.get("creature.count", "1"))))
            except ValueError: c = 1
            placed[lab] += c; spots[lab] += 1
            if p.get("beh.hidden", "").lower() in ("true", "1", "yes"): hidden[lab] += c
            if any(k.startswith("boss.") for k in p): bosses[lab] += c
        sk = p.get("boss.summonKind", "")
        if sk:
            try: summons[label_of(sk.replace("Creature_", ""))] += int(p.get("boss.summon", "1"))
            except ValueError: summons[label_of(sk.replace("Creature_", ""))] += 1
    rows = []
    for lab, nd in need.items():
        pl, hd = placed.get(lab, 0), hidden.get(lab, 0)
        vis = pl - hd
        if vis >= nd + 1: v = "OK"
        elif pl >= nd + 1: v = "HIDDEN"
        elif pl >= nd and bosses.get(lab, 0) >= nd: v = "BOSS"
        elif pl >= nd: v = "NOSPARE"
        else: v = "SHORT"
        rows.append({"island": f.name, "path": path, "kind": lab, "need": nd, "placed": pl, "hidden": hd, "spots": spots.get(lab, 0),
                     "summons": summons.get(lab, 0), "verdict": v, "steps": steps[lab], "copies": 0})
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--deploy", default=DEPLOY)
    ap.add_argument("--recipes", default=RECIPES)
    ap.add_argument("--out")
    ap.add_argument("--all", action="store_true")
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(a.deploy, "islands", "*", "*.island")) + glob.glob(os.path.join(a.deploy, "plans", "*", "*.island")))
    merged, failed = OrderedDict(), []
    for p in files:
        try: rows = check(p)
        except Exception as e: failed.append((p, str(e))); continue
        for r in rows:
            k = (r["island"].lower(), r["kind"], r["need"], r["placed"], r["hidden"])
            if k in merged: merged[k]["copies"] += 1
            else: merged[k] = r
    rows = sorted(merged.values(), key=lambda r: (ORDER[r["verdict"]], r["placed"] - r["need"], r["island"].lower()))
    tot = defaultdict(int)
    for r in rows: tot[r["verdict"]] += 1
    rl, cache = recipe_lines(a.recipes), {}
    out = ["killspare: %d shipped .island files (%d unreadable); %d kill/catch kinds on islands: %s" % (
        len(files), len(failed), len(rows), ", ".join("%s %d" % (v, tot[v]) for v in ("SHORT", "NOSPARE", "HIDDEN", "BOSS", "OK")))]
    out.append("rule: placed >= need + 1 (need = all kill/catch steps of the kind added up). Columns: verdict | island | where | kind | need | placed (hidden) spots | steps")
    for p, e in failed: out.append("UNREADABLE %s: %s" % (p, e))
    for r in rows:
        if r["verdict"] == "OK" and not a.all: continue
        where = None
        for s in r["steps"]:
            m = re.search(r'"(.+)"$', s)
            if m and m.group(1): where = locate(m.group(1), rl, cache)
            if where: break
        out.append("%s | %s | %s | %s | need %d | placed %d%s in %d spot(s)%s | %s%s" % (
            r["verdict"], r["island"], where or os.path.relpath(r["path"], a.deploy), r["kind"], r["need"], r["placed"],
            " (%d hidden)" % r["hidden"] if r["hidden"] else "", r["spots"], ", boss summons %d" % r["summons"] if r["summons"] else "",
            "; ".join(r["steps"]), " (+%d copies)" % r["copies"] if r["copies"] else ""))
    text = "\n".join(out) + "\n"
    if a.out:
        with open(a.out, "w", encoding="utf-8") as fh: fh.write(text)
        print(out[0]); print("written", a.out)
    else: sys.stdout.write(text)
    return 1 if tot["SHORT"] or tot["NOSPARE"] else 0


if __name__ == "__main__":
    sys.exit(main())
