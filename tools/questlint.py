# questlint.py - sweep tool T3 (SWEEP_PLAN.md 3.3): quest texts are clear and say what to do.
#
# Offline, read only. Reads every shipped .island (the library folder: islands/*/ and plans/*/) and every
# content/recipes/*.recipe, and lists each text that breaks a rule, worst first, with where to fix it
# (the recipe file:line when the text is found in a recipe, else the island file + setting).
#
# Rules (the user 2026-11-03 + memory raft-quest-text-rules):
#   COMPASS     north/south/east/west (and -ern, -ward, north-east ...) in a step, intro, done text, a target name the
#               journal shows (ERROR); in a message, note title, islander line, story item text (WARN); in a note's
#               body (WARN when it reads like a direction: "go/head/walk ... north", "north of/side/end/shore")
#   KILLWORD    a kill step whose text does not say "kill" (ERROR; "defeat/slay/hunt" = WARN)
#   KILLWHAT    a kill / catch step whose text does not name the animal (ERROR)
#   COUNT       a kill / catch / pages / collect step for more than one whose text gives no number (WARN), or a number
#               that differs from the step's count (ERROR)
#   CATCHWORD   a catch step whose text does not say catch/net/capture (ERROR)
#   VAGUE       "free / clear / rid / purge / secure ... the area" style texts (kill steps: ERROR via KILLWORD)
#   CHEST       an open step whose text says neither chest/crate/box/... nor a noun of the chest's own name (WARN)
#   WASHED      an open step (the item is in a container) that says "washed up / ashore / lying on the beach" (ERROR)
#   TARGET      a read / open / reach / kill / catch step whose target the island has no note / chest / zone / animal
#               of (ERROR; recipes' save checks most of these, the shipped file may be older)
#   EMPTY       a step with no text (the journal shows "Go to <zone id>"), a quest with steps but no intro / done (WARN)
#   PLAIN       tags <...>, tabs or control characters in a shown text (ERROR)
#   FIRSTSTEP   the first step of the main quest names no landmark (by / at / near ... + a noun) (WARN)
#   LANDMARK    a step names no place at all (no by / near / next to / in / at / on / ... phrase) (CHECK: a reader
#               reads these next to the island's pictures)
# Severity order ERROR > WARN > CHECK. Same text in several shipped copies (plan copies) = one finding, "+n copies".
#
#   python tools/questlint.py [--deploy DIR] [--recipes DIR] [--out FILE] [--rules COMPASS,KILLWORD] [--max N]
# Exit 0 = no ERROR, 1 = ERRORs.
import argparse, glob, os, re, sys
from collections import OrderedDict, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import islandfile

HERE = os.path.dirname(os.path.abspath(__file__))
DEPLOY = r"C:\Users\Johan\Desktop\RAFT_DRIVE_DEPLOY"
RECIPES = os.path.join(HERE, "..", "content", "recipes")
SEV = {"ERROR": 0, "WARN": 1, "CHECK": 2}

COMPASS = re.compile(r"\b(north|south|east|west)(ern|erly|ward|wards|east|west|-east|-west)?\b", re.I)
# a note body's compass word that guides: "go/head/walk/swim ... north", "north of/side/end/shore/tip/beach/cliff"
COMPASS_GUIDE = re.compile(r"\b(go|head|walk|swim|climb|sail|turn|look|follow|dig|search|steps?|paces?|metres?|meters?)\b"
                           r"[^.!?]{0,30}\b(north|south|east|west)\w*\b|\b(north|south|east|west)\w*\s+"
                           r"(of|side|end|shore|tip|beach|cliffs?|coast|point|corner|edge|wall|bank|slope|face)\b", re.I)
KILLWORD = re.compile(r"\bkill(s|ed|ing)?\b", re.I)
KILLSOFT = re.compile(r"\b(defeat|slay|slain|hunt|shoot|put down|finish off|destroy|take down|fight)\w*\b", re.I)
CATCHWORD = re.compile(r"\b(catch|caught|net|nets|netted|capture|trap)\w*\b", re.I)
VAGUE = re.compile(r"\b(free|clear|rid|purge|cleanse|liberate|secure|drive)\b[^.!?]{0,40}\b(of|from|out|away|area|place|nest|den|the)\b", re.I)
CONTAINER = re.compile(r"\b(chests?|crates?|box(es)?|barrels?|cupboards?|lockers?|trunks?|cabinets?|drawers?|safes?|cases?|"
                       r"hives?|coffers?|strongbox(es)?|desks?|shelf|shelves|wardrobes?|footlockers?|bins?|sacks?|caches?|"
                       r"tins?|jars?|bags?|baskets?|kists?|lockbox(es)?|caskets?|chests'|tool ?box(es)?|containers?|stash|"
                       r"bedside table|cooler|fridge|freezer|cabinet|vault|pod|capsule|canister|tank|bucket)\b", re.I)
WASHED = re.compile(r"\b(washed (up|ashore)|lying on the (beach|sand)|on the beach)\b", re.I)
PLACE = re.compile(r"\b(by|near|next to|beside|behind|inside|in|on|at|under|beneath|below|above|over|across|past|through|"
                   r"along|around|atop|into|onto|up|down|between|among|toward|towards|to|from|out of|off|round|within|"
                   r"top of|foot of|end of|edge of|side of)\b\s+(the|a|an|its|his|her|their|this|that|those|these|each|"
                   r"every|both|[A-Z0-9])", re.I)
TAGS = re.compile(r"<[^>]{1,40}>")
CTRL = re.compile(r"[\x00-\x08\x0b-\x1f\x7f]")
NUMWORDS = {"one": 1, "a": 1, "an": 1, "single": 1, "two": 2, "both": 2, "pair": 2, "couple": 2, "three": 3, "four": 4,
            "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9, "ten": 10, "eleven": 11, "twelve": 12,
            "fifteen": 15, "twenty": 20, "dozen": 12}
NUMRE = re.compile(r"\b(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|dozen|both|pair)\b", re.I)

# creature label -> words a text may use for it (ContentCatalog.Creatures)
ANIMAL_WORDS = {
    "chicken": ["chicken", "hen", "rooster", "fowl"], "goat": ["goat"], "llama": ["llama"],
    "warthog": ["warthog", "boar", "hog", "pig"], "pig": ["pig", "hog", "boar"], "bear": ["bear"],
    "mama bear": ["bear", "mama", "mother"], "polar bear": ["polar bear", "bear"], "hyena": ["hyena"],
    "rat": ["rat"], "tangaroa rat": ["rat"], "roach": ["roach", "cockroach"], "bee swarm": ["bee", "swarm"],
    "screecher": ["screecher", "bird"], "caravan screecher": ["screecher", "bird"], "puffin": ["puffin"],
    "butler bot": ["bot", "butler", "robot"], "puffer fish": ["puffer", "pufferfish", "fish"],
    "angler fish": ["angler", "anglerfish", "fish"], "turtle": ["turtle"], "stingray": ["stingray", "ray"],
    "dolphin": ["dolphin"], "whale": ["whale"],
}
STOP = set("the a an of in on at to and or his her its their old my your our by for with from into".split())


class Finding(object):
    def __init__(self, sev, rule, island, where, field, text, why):
        self.sev, self.rule, self.island, self.where, self.field, self.text, self.why = sev, rule, island, where, field, text, why
        self.copies = 0


def number_in(text, count):
    """(numbers the text gives, True when one of them is the count)."""
    found = []
    for m in NUMRE.finditer(text):
        # (a count stands before a word: "three hyenas"; "stack 6", "Round two:" are names)
        if not re.match(r"\s+(of\s+)?[A-Za-z]", text[m.end():]): continue
        w = m.group(1).lower()
        found.append(int(w) if w.isdigit() else NUMWORDS.get(w, -1))
    return found, count in found or ("all" in text.lower().split() and count > 1 and not found)


def parse_steps(text):
    out = []
    for line in (text or "").split("\n"):
        if not line.strip(): continue
        p = line.split("|")
        p += [""] * (4 - len(p))
        try: c = int(p[2].strip() or "1")
        except ValueError: c = 1
        out.append({"type": p[0].strip().lower(), "target": p[1].strip(), "count": c, "text": "|".join(p[3:]).strip()})
    return out


class Island(object):
    """What questlint needs of one island: quests, names it has, texts it shows."""
    def __init__(self, name, path):
        self.name, self.path = name, path
        self.quests = []        # (n, title, intro, done, steps)
        self.notes, self.chests, self.zones, self.creatures = set(), {}, set(), defaultdict(int)
        self.texts = []         # (field, text, sev-for-compass)


def island_from_file(path):
    f = islandfile.load(path, heights=False)
    isl = Island(f.name, path)
    for n in range(9):
        k = "quest" if n == 0 else "quest%d" % (n + 1)
        steps = parse_steps(f.props.get(k + ".steps", ""))
        if steps or f.props.get(k + ".title"):
            isl.quests.append((n, f.props.get(k + ".title", ""), f.props.get(k + ".intro", ""), f.props.get(k + ".done", ""), steps))
    for key, v in f.props.items():
        if key.startswith(("on.", "else.")): isl.texts += [("island " + key, t, "WARN") for t in messages(v)]
        if key == "story.items":
            for line in v.split("\n"):
                p = line.split("|")
                if len(p) > 1: isl.texts.append(("story item " + p[0], p[1], "WARN"))
                if len(p) > 3: isl.texts.append(("story item " + p[0] + " text", p[3], "WARN"))
    for o in f.objects:
        pr = o.props
        title = pr.get("note.title", "").strip()
        if title:
            if o.name.startswith("Loot_") or "loot.items" in pr or o.name.startswith("Placeable_Storage"): isl.chests[title.lower()] = o.name
            else: isl.notes.add(title.lower())
            isl.texts.append(("note title", title, "WARN"))
        if pr.get("note.text"): isl.texts.append(("note text '" + title + "'", pr["note.text"], "NOTE"))
        if pr.get("zone.id"): isl.zones.add(pr["zone.id"].strip().lower())
        if pr.get("zone.message"): isl.texts.append(("zone message " + pr.get("zone.id", ""), pr["zone.message"], "WARN"))
        if pr.get("zone.hazardMsg"): isl.texts.append(("hazard message", pr["zone.hazardMsg"], "WARN"))
        if pr.get("talk.text"): isl.texts.append(("islander line", pr["talk.text"], "WARN"))
        if pr.get("survivor.line"): isl.texts.append(("survivor line", pr["survivor.line"], "WARN"))
        for key, v in pr.items():
            if key.startswith(("on.", "else.")): isl.texts += [("object " + key, t, "WARN") for t in messages(v)]
        if o.name.startswith("Creature_"):
            try: c = max(1, min(8, int(pr.get("creature.count", "1"))))
            except ValueError: c = 1
            isl.creatures[o.name[len("Creature_"):].lower()] += c
    return isl


def messages(v):
    out = []
    for line in v.split("\n"):
        p = line.split("|")
        if p[0] in ("message", "journal") and len(p) >= 3 and p[2].strip(): out.append("|".join(p[2:]))
    return out


# Creature_<Type> -> label (ContentCatalog: only the ones whose label differs from the type)
TYPE_LABEL = {"boar": "warthog", "mamabear": "mama bear", "polarbear": "polar bear", "rat_tangaroa": "tangaroa rat",
              "bugswarm_bee": "bee swarm", "stonebird": "screecher", "stonebird_caravan": "caravan screecher",
              "butlerbot": "butler bot", "pufferfish": "puffer fish", "anglerfish": "angler fish"}


def creature_count(isl, label):
    label = label.lower()
    return sum(c for t, c in isl.creatures.items() if TYPE_LABEL.get(t, t) == label or t == label.replace(" ", ""))


def lint_step(isl, n, i, s, first, out, check_targets=True):
    t, text, where = s["type"], s["text"], "quest %d step %d (%s|%s|%d)" % (n + 1, i + 1, s["type"], s["target"], s["count"])
    def add(sev, rule, why, field="step text", txt=None):
        out.append(Finding(sev, rule, isl.name, isl.path, where + " " + field, txt if txt is not None else text, why))
    if not text:
        add("WARN", "EMPTY", "no text: the journal shows the made-up line"); return
    m = COMPASS.search(text)
    if m:
        named = m.group(0)[0].isupper() and m.start() > 0 and not re.search(r"[.!?:]\s*$", text[:m.start()])
        add("WARN" if named else "ERROR", "COMPASS", "compass word '%s'%s" % (m.group(0), " (part of a name?)" if named else ""))
    if t in ("read", "open", "reach") and COMPASS.search(s["target"]):
        add("ERROR", "COMPASS", "compass word in the target name", "target", s["target"])
    if TAGS.search(text) or CTRL.search(text) or "\t" in text: add("ERROR", "PLAIN", "tag, tab or control character")
    low = text.lower()
    if t in ("kill", "catch"):
        words = ANIMAL_WORDS.get(s["target"].lower(), [s["target"].lower()])
        if s["target"] and not any(re.search(r"\b" + re.escape(w) + r"(s|es)?\b", low) for w in words):
            add("ERROR", "KILLWHAT", "does not name the %s" % s["target"].lower())
    if t == "kill":
        if not KILLWORD.search(text):
            soft = KILLSOFT.search(text)
            add("WARN" if soft else "ERROR", "KILLWORD", "says '%s', not 'kill'" % soft.group(0) if soft else "never says kill" +
                (" (vague: '%s')" % VAGUE.search(text).group(0) if VAGUE.search(text) else ""))
    elif VAGUE.search(text) and re.search(r"\b(area|of (the )?\w+s\b|nest|den|vermin|pests?)\b", low):
        add("WARN", "VAGUE", "vague '%s'" % VAGUE.search(text).group(0))
    if t == "catch" and not CATCHWORD.search(text): add("ERROR", "CATCHWORD", "never says catch / net")
    if t in ("kill", "catch", "pages", "collect") and s["count"] > 1:
        nums, ok = number_in(text, s["count"])
        real = [x for x in nums if x > 1]
        # (collect/pages counts are often running totals - "the last three" of 12 - so their mismatch is a WARN; a list
        # of numbers like "beds 4, 7 and 9" is names, not a count)
        if real and s["count"] not in nums and len(set(real)) < 3:
            add("ERROR" if t in ("kill", "catch") else "WARN", "COUNT", "text says %s, step needs %d" % ("/".join(map(str, real)), s["count"]))
        elif not ok and not real and t in ("kill", "catch"): add("WARN", "COUNT", "needs %d, the text gives no number" % s["count"])
    if t == "open":
        tgt = s["target"].lower()
        own = [w for w in re.findall(r"[a-z']{4,}", tgt) if w not in STOP]
        if not CONTAINER.search(text) and not any(re.search(r"\b" + re.escape(w), low) for w in own):
            add("WARN", "CHEST", "does not say it is in a chest/crate/box (target '%s')" % s["target"])
        if WASHED.search(text) and (WASHED.search(text).group(0).lower().startswith("washed") or not CONTAINER.search(text)): add("ERROR", "WASHED", "the item is in a container, text says '%s'" % WASHED.search(text).group(0))
    if check_targets:
        tl = s["target"].lower()
        if t == "read" and tl and tl not in isl.notes and tl not in isl.chests: add("ERROR", "TARGET", "no note titled '%s' on the island" % s["target"])
        if t == "open" and tl and tl not in isl.chests and tl not in isl.notes: add("ERROR", "TARGET", "no chest titled '%s' on the island" % s["target"])
        if t == "reach" and tl and tl not in isl.zones: add("ERROR", "TARGET", "no zone '%s' on the island" % s["target"])
        if t in ("kill", "catch") and tl and creature_count(isl, tl) == 0: add("ERROR", "TARGET", "no %s placed on the island" % s["target"])
    if not PLACE.search(text) and t not in ("pages", "blueprint"):
        if first: add("WARN", "FIRSTSTEP", "first step names no landmark to start from")
        else: add("CHECK", "LANDMARK", "names no place (by/near/in/at ... + a noun)")


def lint_island(isl, out):
    for n, title, intro, done, steps in isl.quests:
        for field, txt in (("title", title), ("intro", intro), ("done", done)):
            m = COMPASS.search(txt or "")
            if m: out.append(Finding("ERROR", "COMPASS", isl.name, isl.path, "quest %d %s" % (n + 1, field), txt, "compass word '%s'" % m.group(0)))
            if txt and (TAGS.search(txt) or CTRL.search(txt)): out.append(Finding("ERROR", "PLAIN", isl.name, isl.path, "quest %d %s" % (n + 1, field), txt, "tag or control character"))
        if steps and not (intro or "").strip(): out.append(Finding("WARN", "EMPTY", isl.name, isl.path, "quest %d intro" % (n + 1), "", "quest has steps but no intro"))
        if steps and not (done or "").strip(): out.append(Finding("WARN", "EMPTY", isl.name, isl.path, "quest %d done" % (n + 1), "", "quest has steps but no done text"))
        if (title or "").strip() and not steps: out.append(Finding("WARN", "EMPTY", isl.name, isl.path, "quest %d" % (n + 1), title, "a title and no steps"))
        for i, s in enumerate(steps): lint_step(isl, n, i, s, n == 0 and i == 0, out)
    for field, txt, sev in isl.texts:
        if sev == "NOTE":
            m = COMPASS_GUIDE.search(txt)
            if m: out.append(Finding("WARN", "COMPASS", isl.name, isl.path, field, txt, "note guides by compass: '%s'" % m.group(0)))
        else:
            m = COMPASS.search(txt)
            if m: out.append(Finding(sev, "COMPASS", isl.name, isl.path, field, txt, "compass word '%s'" % m.group(0)))
        if TAGS.search(txt) or CTRL.search(txt.replace("\n", "")):
            out.append(Finding("ERROR", "PLAIN", isl.name, isl.path, field, txt, "tag or control character"))


# ---------------------------------------------------------------- recipes: where a text lives, and unshipped islands

def recipe_lines(rdir):
    lines = []
    for p in sorted(glob.glob(os.path.join(rdir, "*.recipe"))):
        with open(p, encoding="utf-8-sig", errors="replace") as f:
            for i, l in enumerate(f, 1): lines.append((os.path.basename(p), i, norm(l)))
    return lines


def norm(s):
    return re.sub(r"\s+", " ", s.replace("\\n", " ").replace("\n", " ")).strip().lower()


def locate(text, rlines, cache):
    key = norm(text)[:60]
    if len(key) < 12: return None
    if key in cache: return cache[key]
    hit = None
    for f, i, l in rlines:
        if key in l: hit = "%s:%d" % (f, i); break
    cache[key] = hit
    return hit


def recipe_islands(rdir):
    """Recipes that save an island: name -> (file, [(line, steptext)]) of their own step lines (includes not followed)."""
    out = {}
    for p in sorted(glob.glob(os.path.join(rdir, "*.recipe"))):
        steps, saves, cur = [], [], None
        with open(p, encoding="utf-8-sig", errors="replace") as f:
            for i, l in enumerate(f, 1):
                m = re.match(r"^\s*(step[2-9]?)\s+(.+)$", l)
                if m and "|" in m.group(2): steps.append((i, m.group(1), m.group(2).rstrip()))
                m = re.match(r"^\s*save\s+(.+)$", l)
                if m: saves.append(m.group(1).strip())
        for s in saves: out[s] = (os.path.basename(p), steps)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--deploy", default=DEPLOY)
    ap.add_argument("--recipes", default=RECIPES)
    ap.add_argument("--out")
    ap.add_argument("--rules", default="")
    ap.add_argument("--max", type=int, default=0, help="list at most N findings per rule (0 = all)")
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(a.deploy, "islands", "*", "*.island")) + glob.glob(os.path.join(a.deploy, "plans", "*", "*.island")))
    findings, shipped, failed = [], set(), []
    for p in files:
        try: isl = island_from_file(p)
        except Exception as e: failed.append((p, str(e))); continue
        shipped.add(isl.name.lower())
        lint_island(isl, findings)
    # recipes of islands not in the shipped folder: lint their own step lines (no target check: includes not followed)
    unshipped = 0
    for name, (rf, steps) in recipe_islands(a.recipes).items():
        if name.lower() in shipped or not steps: continue
        unshipped += 1
        isl = Island(name, rf)
        bykey = defaultdict(list)
        for i, key, body in steps: bykey[key].append((i, body))
        for key, lst in bykey.items():
            n = 0 if key == "step" else int(key[4:]) - 1
            for j, (line, body) in enumerate(lst):
                s = parse_steps(body)[0]
                before = len(findings)
                lint_step(isl, n, j, s, n == 0 and j == 0, findings, check_targets=False)
                for fd in findings[before:]: fd.where = "%s:%d" % (rf, line)
    if a.rules:
        keep = set(a.rules.upper().split(","))
        findings = [f for f in findings if f.rule in keep]
    # one finding per (island, rule, field, text): plan copies of an island repeat it
    merged = OrderedDict()
    for f in findings:
        k = (f.island.lower(), f.rule, re.sub(r"^quest \d+ step \d+ ", "", f.field) if False else f.field, f.text)
        if k in merged: merged[k].copies += 1
        else: merged[k] = f
    findings = list(merged.values())
    rlines, cache = recipe_lines(a.recipes), {}
    for f in findings:
        if f.where.endswith(".island"):
            hit = locate(f.text, rlines, cache) if f.text else None
            f.where = hit or os.path.relpath(f.where, a.deploy)
    order = ["COMPASS", "KILLWORD", "KILLWHAT", "CATCHWORD", "TARGET", "WASHED", "COUNT", "PLAIN", "CHEST", "VAGUE", "FIRSTSTEP", "EMPTY", "LANDMARK"]
    findings.sort(key=lambda f: (SEV[f.sev], order.index(f.rule) if f.rule in order else 99, f.island.lower(), f.field))
    out = []
    tot = defaultdict(int)
    for f in findings: tot[(f.sev, f.rule)] += 1
    nsev = defaultdict(int)
    for f in findings: nsev[f.sev] += 1
    out.append("questlint: %d shipped .island files (%d unreadable), %d unshipped recipe islands; findings %s" % (
        len(files), len(failed), unshipped, ", ".join("%s %d" % (s, nsev[s]) for s in ("ERROR", "WARN", "CHECK"))))
    out.append("by rule: " + ", ".join("%s %s %d" % (s, r, n) for (s, r), n in sorted(tot.items(), key=lambda x: (SEV[x[0][0]], x[0][1]))))
    for p, e in failed: out.append("UNREADABLE %s: %s" % (p, e))
    shown = defaultdict(int)
    for f in findings:
        shown[f.rule] += 1
        if a.max and shown[f.rule] > a.max: continue
        txt = f.text.replace("\n", " / ")
        if len(txt) > 160: txt = txt[:157] + "..."
        out.append("%s %s | %s | %s | %s | %s%s | \"%s\"" % (f.sev, f.rule, f.island, f.where, f.field, f.why,
                   " (+%d copies)" % f.copies if f.copies else "", txt))
    text = "\n".join(out) + "\n"
    if a.out:
        with open(a.out, "w", encoding="utf-8") as fh: fh.write(text)
        print("\n".join(out[:2])); print("written", a.out)
    else:
        sys.stdout.write(text)
    return 1 if nsev["ERROR"] else 0


if __name__ == "__main__":
    sys.exit(main())
