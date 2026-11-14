"""questcheck.py - static check of every shipped quest, no game run (questdeepB 11-14).

Loads the world plans (.plan) and islands (.island) the game would load and proves each quest can be finished:
  - every rule line, story place and done-when is readable (the game drops what it can't read, mostly without a word)
  - every island, zone, note, chest, creature, islander, set, light group, signal, counter, story item, Raft item and
    blueprint a step or rule names exists, and story items / blueprints are given in the world before they are needed
  - every step line is one the game reads (type, count, at most 99 steps) and every rule fires in play (no circles,
    no story step that never unlocks)
  - quest rewards name Raft items that exist (an unknown one is dropped silently)
  - text rules (Raft quest text rules, user 10-27): no compass directions; an item found in a chest or crate says so
  - each storyline has a day: rule (user 10-16)
  - the daily quest's items exist (DailyQuest.cs)

Sources (each optional; defaults are this PC's):
  --mods DIR      the installed mod folder: plans\\*.plan + *.island (built from content/recipes)
  --library DIR   the library deploy folder (index.json, plans\\<pack>, islands\\<entry>)
  --stage DIR     staged newer library files (searched recursively; a staged island/plan replaces the deploy copy;
                  several folders split by ';')
Run:  python -I content/questcheck.py [--json out.json] [--all] [--quiet]
Prints one line per finding (ERROR/WARN; INFO with --all) and a count summary. Exit code 1 when there is an ERROR.
"""
import argparse, glob, io, json, os, re, struct, sys, zlib
from collections import Counter, OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
RECIPES = os.path.join(HERE, 'recipes')
ITEMS_TXT = os.path.join(HERE, 'objects', 'items.txt')
RAFT_BP_TXT = os.path.join(REPO, 'DynamicIslands', 'raft_blueprints.txt')
DAILY_CS = os.path.join(REPO, 'DynamicIslands', 'DailyQuest.cs')
DEF_MODS = r'D:\Games\SteamLibrary\steamapps\common\Raft\Mods\DynamicIslands'
DEF_LIBRARY = r'C:\Users\Johan\Desktop\RAFT_DRIVE_DEPLOY'
DEF_STAGE = r'D:\Temp\scratch_8e762ddb\drive_stage'

# ---- the game's own lists (keep in step with the C#) ----
WHAT = {'island', 'type', 'pool', 'oneof'}                                           # IntroRule.WhatKinds
WHEN = {'start', 'km', 'day', 'quest', 'step', 'zone', 'visit', 'rule', 'signal'}    # IntroRule.WhenKinds
WHERE = {'ahead', 'near', 'receiver', 'sailing'}                                     # IntroRule.WhereKinds
DONE = {'quest', 'visit', 'step', 'zone', 'signal', 'note'}                          # IntroRule.DoneKinds
QTYPES = ['reach', 'read', 'open', 'kill', 'catch', 'collect', 'pages', 'blueprint', 'talk', 'set', 'lights', 'escort',
          'tame', 'signal', 'bring', 'craft', 'fish', 'gather', 'time', 'weather', 'counter', 'players']  # IslandQuest.Types
MAX_STEPS, MAX_QUESTS, MAX_PLAYERS, MAX_PEN = 99, 9, 8, 6
CHAIN = ['radiotower', 'vasagatan', 'balboa', 'caravantown', 'tangaroa', 'varunapoint', 'temperance', 'utopia']
ALIAS = {'radiotower': 'radiotower', 'radio': 'radiotower', 'vasagatan': 'vasagatan', 'balboa': 'balboa',
         'caravantown': 'caravantown', 'caravan': 'caravantown', 'caravanisland': 'caravantown', 'tangaroa': 'tangaroa',
         'varunapoint': 'varunapoint', 'varuna': 'varunapoint', 'temperance': 'temperance', 'utopia': 'utopia'}
CREATURES = {'Chicken': 'Chicken', 'Goat': 'Goat', 'Llama': 'Llama', 'Boar': 'Warthog', 'Pig': 'Pig', 'Bear': 'Bear',
             'MamaBear': 'Mama bear', 'PolarBear': 'Polar bear', 'Hyena': 'Hyena', 'Rat': 'Rat', 'Rat_Tangaroa': 'Tangaroa rat',
             'Roach': 'Roach', 'BugSwarm_Bee': 'Bee swarm', 'StoneBird': 'Screecher', 'StoneBird_Caravan': 'Caravan screecher',
             'Puffin': 'Puffin', 'ButlerBot': 'Butler bot', 'PufferFish': 'Puffer fish', 'AnglerFish': 'Angler fish',
             'Turtle': 'Turtle', 'Stingray': 'Stingray', 'Dolphin': 'Dolphin', 'Whale': 'Whale'}  # ContentCatalog.Creatures
CATCHABLE = {'chicken', 'goat', 'llama'}
VERBS = {'show', 'hide', 'toggle', 'open', 'close', 'switch', 'message', 'give', 'sound', 'teleport', 'signal', 'journal',
         'wait', 'character', 'playsound', 'stopsound', 'camera', 'suit', 'hit', 'carry', 'collapse', 'moveto', 'dialogue',
         'light', 'flythrough', 'order', 'weather', 'random', 'count', 'heal', 'frequency'}  # BehaviourProps.Verbs
TARGET_VERBS = {'show', 'hide', 'toggle', 'open', 'close', 'switch'}
STATES = {'open', 'closed', 'shown', 'hidden'}
WEATHERS = {'clear', 'calm', 'fog', 'waves', 'rain', 'storm', 'snow'}
FISH = {'Raw_Pomfret', 'Raw_Mackerel', 'Raw_Herring', 'Raw_Tilapia', 'Raw_Catfish', 'Raw_Salmon'}  # Raft's rod (FishNames)
NEED_RX = re.compile(r'^(?:has|take)\|story:([^|;*\s]+)')
GIVE_RX = re.compile(r'story:([^|;*\s]+)(?:\*(\d+))?')
COMPASS_RX = re.compile(r'\b(north|south|east|west)(ern|wards?|-?east|-?west)?\b|\b(NE|NW|SE|SW)\b')
CONTAINER_RX = re.compile(r'\b(chest|crate|box|barrel|locker|safe|trunk|cabinet|cupboard|case|drawer|stash|coffer|strongbox|hold|shed|cache|wardrobe|bin|basket|sack|bag|hive)s?\b', re.I)
# Raft items only some sources give (the resource check: gather/bring/craft needs them in the world)
SPECIAL = {'Egg': 'chicken', 'Feather': 'chicken', 'Bucket_Milk': 'goat', 'Wool': 'llama'}
TITANIUM = {'TitaniumOre', 'TitaniumIngot'}

F = []
LIBRARY_NAMES = set()   # every island name the library ships (a single island's own rule may bring another entry)
STATS = Counter()


def add(level, scope, where, text):
    F.append(OrderedDict(level=level, scope=scope, where=where, text=text))


def low(s):
    return (s or '').strip().lower()


def clean_id(s):
    for ch in '|:,;':
        s = s.replace(ch, '-')
    return s.strip()


def story_key(s):
    return ALIAS.get(re.sub(r'[\s_]', '', s or '').replace('Landmark', '').lower())


# ---------------- reading .island (IslandFile, format 1-4) ----------------
class _R:
    def __init__(s, b): s.b = b; s.p = 0
    def eof(s): return s.p >= len(s.b)
    def i32(s): v = struct.unpack_from('<i', s.b, s.p)[0]; s.p += 4; return v
    def f32(s): v = struct.unpack_from('<f', s.b, s.p)[0]; s.p += 4; return v
    def u8(s): v = s.b[s.p]; s.p += 1; return v
    def str(s):
        n = sh = 0
        while True:
            c = s.u8(); n |= (c & 0x7f) << sh; sh += 7
            if c < 0x80: break
        v = s.b[s.p:s.p + n].decode('utf-8', 'replace'); s.p += n; return v


def read_island(path):
    raw = open(path, 'rb').read()
    if raw[:4] != b'CISL': raise ValueError('not a Custom Islands file')
    ver = struct.unpack_from('<i', raw, 4)[0]
    r = _R(zlib.decompress(raw[8:]))
    isl = {'version': ver, 'name': r.str()}
    r.f32(); r.p += 12
    res = r.i32(); r.p += res * res * 2
    objs = []
    for _ in range(r.i32()):
        objs.append({'name': r.str(), 'props': {}}); r.p += 36
    if ver >= 2 and r.u8():
        ares = r.i32(); layers = r.i32(); r.p += layers * ares * ares
        if r.u8(): r.p += ares * ares
    if ver >= 3:
        r.f32(); r.str()
    props = {}
    if ver >= 4 and not r.eof():
        for _ in range(r.i32()):
            a = r.str(); props[a] = r.str()
        for _ in range(r.i32()):
            idx = r.i32(); d = {}
            for _ in range(r.i32()):
                a = r.str(); d[a] = r.str()
            if 0 <= idx < len(objs): objs[idx]['props'] = d
    isl['props'] = props; isl['objects'] = objs
    return isl


# ---------------- game data ----------------
ITEMS = {}       # lower -> exact
RAFT_BP = {}     # blueprint lower -> chain key of the Raft island it lies on
DAILY = []


def load_game_lists():
    for line in open(ITEMS_TXT, encoding='utf-8-sig'):
        n = line.split('|')[0].strip()
        if n: ITEMS[n.lower()] = n
    keymap = {'vasagatan': 'vasagatan', 'balboa': 'balboa', 'caravan': 'caravantown', 'tangaroa': 'tangaroa',
              'varuna': 'varunapoint', 'temperance': 'temperance', 'utopia': 'utopia', 'radiotower': 'radiotower'}
    for line in open(RAFT_BP_TXT, encoding='utf-8-sig'):
        if line.startswith('#') or '|' not in line: continue
        isl, bp = line.split('|')[:2]
        k = keymap.get(re.sub(r'\W', '', isl).lower())
        if k: RAFT_BP[bp.strip().lower()] = k
    if os.path.exists(DAILY_CS):
        src = open(DAILY_CS, encoding='utf-8-sig').read()
        for arr in ('GatherItems', 'RewardItems'):
            m = re.search(arr + r'\s*=\s*\{([^}]*)\}', src)
            if m: DAILY.extend((arr, x) for x in re.findall(r'"([^"]+)"', m.group(1)))


def loot(text):
    """ObjectProps.Loot: 'item*n;item' (a part whose amount isn't a number is dropped)."""
    out = []
    for part in (text or '').split(';'):
        if not part.strip(): continue
        star = part.rfind('*')
        item = (part[:star] if star > 0 else part).strip()
        n = 1
        if star > 0:
            try: n = int(part[star + 1:])
            except ValueError: out.append((item, None)); continue
        if item and n > 0: out.append((item, n))
    return out


# ---------------- island facts (PlanChecker.Facts.FromFile) ----------------
def actions(text):
    out, bad = [], []
    for line in (text or '').split('\n'):
        if not line.strip(): continue
        p = line.split('|')
        v = p[0].strip()
        if v not in VERBS:
            bad.append(line.strip()); continue
        a = {'verb': v, 'target': p[1].strip() if len(p) > 1 else '', 'arg': '|'.join(p[2:]).strip() if len(p) > 2 else ''}
        if v == 'wait' and not a['arg']: a['arg'] = a['target']; a['target'] = ''
        out.append(a)
    return out, bad


def quest_of(props, n):
    k = 'quest.steps' if n == 0 else 'quest%d.steps' % (n + 1)
    steps, dropped, badcount = [], [], []
    for line in (props.get(k) or '').split('\n'):
        if not line.strip(): continue
        p = line.split('|')
        if p[0] not in QTYPES:
            dropped.append(line); continue
        c = 1
        if len(p) > 2:
            try:
                c = int(p[2])
                if c < 1 or c > 99: badcount.append((line, 'clamped to %d' % max(1, min(99, c))))
                c = max(1, min(99, c))
            except ValueError:
                badcount.append((line, 'not a number: read as 1'))
        steps.append({'type': p[0], 'target': p[1] if len(p) > 1 else '', 'count': c, 'text': p[3] if len(p) > 3 else '', 'line': line})
    cut = steps[MAX_STEPS:]
    return steps[:MAX_STEPS], dropped, badcount, cut


def qkey(n, k):
    return 'quest.' + k if n == 0 else 'quest%d.%s' % (n + 1, k)


def facts(path):
    isl = read_island(path)
    P = isl['props']; O = isl['objects']
    x = {'path': path, 'name': isl['name'], 'file': os.path.splitext(os.path.basename(path))[0], 'props': P,
         'zones': set(), 'signals': set(), 'notes': set(), 'chests_full': set(), 'chests_empty': set(), 'notecount': 0,
         'pages': 0, 'chestcount': 0, 'emptychests': 0, 'creatures': {}, 'tamable': {}, 'needs': set(), 'gives': {},
         'loot_gives': set(), 'names': set(), 'islanders': set(), 'receivers': set(), 'receivercount': 0, 'sets': {},
         'lights': {}, 'counters': set(), 'note_idx': [], 'targets': [], 'storydefs': set(), 'alltext': '',
         'badverbs': [], 'raftitems': {}}
    for line in (P.get('story.items') or '').split('\n'):
        if line.strip(): x['storydefs'].add(low(line.split('|')[0]))
    text = [v for k, v in P.items() if k != 'story.items']
    owners = [{k: v for k, v in P.items() if k != 'story.items' and not re.match(r'^quest\d?\.steps$', k)}]
    for i, o in enumerate(O):
        p = o['props']; nm = o['name']
        owners.append(p)
        if p.get('obj.name', '').strip(): x['names'].add(low(p['obj.name']))
        if nm == 'Zone_Trigger' and p.get('zone.id', '').strip(): x['zones'].add(low(p['zone.id']))
        elif p.get('zone.id', '').strip(): x['zones'].add(low(p['zone.id']))
        if nm == 'Story_Islander':
            who = low(p.get('obj.name', '').strip() or 'Islander')
            x['islanders'].add(who); x['receivers'].add(who); x['receivercount'] += 1
        s = p.get('collect.set', '').strip()
        if s:
            x['sets'][low(s)] = x['sets'].get(low(s), 0) + 1; x['signals'].add('set:' + low(s))
        g = p.get('light.group', '').strip()
        if g:
            x['lights'][low(g)] = x['lights'].get(low(g), 0) + 1; x['signals'].add('lights:' + low(g))
        if nm.startswith('Note_') or 'note.title' in p or 'note.text' in p:
            # (ObjectProps.IsNote: a chest with a note title or text is a readable note too - CustomNote is attached)
            x['notecount'] += 1
            if p.get('note.title', '').strip(): x['notes'].add(low(p['note.title']))
            if p.get('note.text', '').strip():
                x['pages'] += 1; x['note_idx'].append(i)
        if not nm.startswith('Zone_') and (nm.startswith('Loot_') or 'loot.items' in p):
            x['chestcount'] += 1; x['receivercount'] += 1
            t = low(p.get('note.title', ''))
            items = loot(p.get('loot.items', ''))
            if t: x['receivers'].add(t)
            if not items:
                x['emptychests'] += 1
                if t: x['chests_empty'].add(t)
            elif t: x['chests_full'].add(t)
            for it, n in items:
                if it.lower().startswith('story:'): x['loot_gives'].add(low(it[6:]))
                else: x['raftitems'][it] = 'loot'
        if nm.startswith('Creature_') and nm[9:] in CREATURES:
            lab = low(CREATURES[nm[9:]])
            try: n = max(1, int(p.get('creature.count', '1')))
            except ValueError: n = 1
            x['creatures'][lab] = x['creatures'].get(lab, 0) + n
            if lab in CATCHABLE:
                x['tamable'][lab] = x['tamable'].get(lab, 0) + n
                sn = low(p.get('obj.name', ''))
                if sn and sn != lab: x['tamable'][sn] = x['tamable'].get(sn, 0) + n
        for k, v in p.items():
            text.append(v)
            if k.startswith('on.') or k.startswith('else.'):
                acts, bad = actions(v)
                x['badverbs'] += [(i, nm, k, b) for b in bad]
                for a in acts:
                    if a['verb'] == 'signal' and a['arg']: x['signals'].add(low(a['arg']))
                    if a['verb'] == 'journal': x['pages'] += 1
                    if a['verb'] == 'count' and a['target']: x['counters'].add(low(a['target']))
                    if a['verb'] in TARGET_VERBS and a['target'] and low(a['target']) != 'self':
                        x['targets'].append((i, nm, k, a['verb'], a['target']))
                    if a['verb'] == 'give':
                        for it, n in loot(a['arg'] or a['target']):
                            if not it.lower().startswith('story:'): x['raftitems'][it] = 'give'
            if k == 'zone.items' or k == 'trade.get':
                for it, n in loot(v):
                    if not it.lower().startswith('story:'): x['raftitems'][it] = k
        if nm.startswith('QuestItemPickup_'):
            x['gives'].setdefault('raft-' + low(nm[16:]), 1)
    for k, v in P.items():
        if k.startswith('on.') or k.startswith('else.'):
            acts, bad = actions(v)
            x['badverbs'] += [(-1, 'island', k, b) for b in bad]
            for a in acts:
                if a['verb'] == 'signal' and a['arg']: x['signals'].add(low(a['arg']))
                if a['verb'] == 'journal': x['pages'] += 1
                if a['verb'] == 'count' and a['target']: x['counters'].add(low(a['target']))
    x['alltext'] = '\n'.join(text)
    # story items needed (checks, trade.give) / given (everything else but the quests' steps and story.items)
    sends = []
    for p in owners:
        s = set()
        for k, v in p.items():
            if k.startswith('on.') or k.startswith('else.'):
                s |= {low(a['arg']) for a in actions(v)[0] if a['verb'] == 'signal' and a['arg']}
        sends.append(s)
    senders = Counter(g for s in sends for g in s)
    for i, p in enumerate(owners):
        for k, v in p.items():
            if k.startswith('if.'):
                lines = (v or '').split('\n')
                if lines[0].strip() == 'any' and any(
                        len(w) >= 2 and w[0] == 'signal' and w[1].strip() and senders[low(w[1])] - (1 if low(w[1]) in sends[i] else 0) > 0
                        for w in (l.strip().split('|') for l in lines[1:])):
                    continue
                for l in lines:
                    m = NEED_RX.match(l.strip())
                    if m: x['needs'].add(low(m.group(1)))
            elif k == 'trade.give':
                for m in GIVE_RX.finditer(v or ''): x['needs'].add(low(m.group(1)))
            else:
                for m in GIVE_RX.finditer(v or ''):
                    sid = low(m.group(1)); x['gives'][sid] = x['gives'].get(sid, 0) + int(m.group(2) or 1)
    x['quests'] = []; x['dropped'] = []; x['badcount'] = []; x['cut'] = []
    for n in range(MAX_QUESTS):
        st, dr, bc, cut = quest_of(P, n)
        x['quests'].append(st)
        x['dropped'] += [(n, d) for d in dr]; x['badcount'] += [(n, l, why) for l, why in bc]; x['cut'] += [(n, len(cut))] if cut else []
    while len(x['quests']) > 1 and not x['quests'][-1]: x['quests'].pop()
    return x


# ---------------- recipes (where to fix) ----------------
RECIPE_OF, RECIPE_LINES = {}, {}


def load_recipes():
    for f in glob.glob(os.path.join(RECIPES, '*.recipe')):
        lines = open(f, encoding='utf-8-sig', errors='replace').read().split('\n')
        RECIPE_LINES[f] = lines
        for i, l in enumerate(lines):
            if l.startswith('save '): RECIPE_OF[low(l[5:])] = (f, i + 1)


def rloc(island, needle=None):
    r = RECIPE_OF.get(low(island))
    if not r: return island + '.island'
    f, ln = r
    if needle:
        for i, l in enumerate(RECIPE_LINES[f]):
            if needle in l: return '%s:%d' % (os.path.basename(f), i + 1)
    return '%s:%d' % (os.path.basename(f), ln)


RECIPE_ITEMS_RX = re.compile(r'(?:^loot \S+ |reward=|items=|loot\.items=|zone\.items=|trade\.get=)"?([^\s"]+)')


def check_recipes():
    """Raft item names in the recipes' loot, rewards and item lists (also islands not built or shipped yet)."""
    n = 0
    for f, lines in sorted(RECIPE_LINES.items()):
        for i, l in enumerate(lines):
            for m in RECIPE_ITEMS_RX.finditer(l):
                v = m.group(1)
                if '$' in v or '{' in v or '%' in v: continue
                n += 1
                for it, k in loot(v):
                    if it.lower().startswith('story:') or not it: continue
                    if not item_ok(it): add('ERROR', 'recipe', '%s:%d' % (os.path.basename(f), i + 1), "item '%s': Raft has no such item (given as nothing)" % it)
                    elif ITEMS[it.lower()] != it: add('WARN', 'recipe', '%s:%d' % (os.path.basename(f), i + 1), "item '%s': Raft's name is '%s' (case)" % (it, ITEMS[it.lower()]))
    STATS['recipe item lists'] += n


# ---------------- plans (WorldPlan.Parse / IntroRule.Parse) ----------------
def parse_plan(path):
    plan = {'path': path, 'name': os.path.splitext(os.path.basename(path))[0], 'rules': [], 'random': False, 'story': True,
            'leaveout': set(), 'kept': [], 'title': ''}
    for ln, raw in enumerate(open(path, encoding='utf-8-sig', errors='replace').read().split('\n'), 1):
        line = raw.strip()
        if not line or line.startswith('#') or '=' not in line or line.index('=') <= 0: continue
        k, v = line.split('=', 1); k = k.strip().lower(); v = v.strip()
        if k == 'random': plan['random'] = low(v) in ('on', '1', 'yes', 'true')
        elif k == 'story': plan['story'] = low(v) not in ('off', '0', 'no', 'false')
        elif k == 'storyleaveout':
            for s in v.split(','):
                t = story_key(s)
                if t: plan['leaveout'].add(t)
        elif k == 'rule':
            r = parse_rule(v)
            if r is None: plan['kept'].append((ln, v))
            else:
                r['line'] = ln; r['raw'] = v; plan['rules'].append(r)
    return plan


def norm_place(s):
    s = (s or '').strip()
    if low(s) in ('first', 'beside'): return low(s)
    if ':' not in s or s.index(':') <= 0: return ''
    kind, what = s.split(':', 1); kind = low(kind); what = what.strip()
    if kind not in ('after', 'instead') or not what: return ''
    t = story_key(what)
    if t: return kind + ':' + t
    return '' if kind == 'instead' else kind + ':' + what


def norm_done(s):
    s = (s or '').strip()
    kind = low(s.split(':')[0])
    if kind not in DONE: return ''
    return kind if kind in ('quest', 'visit') else kind + ':' + (s[s.index(':') + 1:].strip() if ':' in s else '')


def parse_rule(line):
    p = [x.strip() for x in line.split('|')]
    if len(p) < 4: return None
    r = {'id': clean_id(p[0]), 'message': p[4] if len(p) > 4 else '', 'placeraw': p[6] if len(p) > 6 else '',
         'doneraw': p[7] if len(p) > 7 else '', 'tabintro': p[10] if len(p) > 10 else ''}
    r['place'] = norm_place(r['placeraw']); r['done'] = norm_done(r['doneraw'])
    w = p[1].split(':', 1)
    r['what'] = low(w[0]); r['whatarg'] = w[1].strip() if len(w) > 1 else ''
    if r['what'] not in WHAT: return None
    w = [x.strip() for x in p[2].split(':')]
    r['when'] = low(w[0]); r['whenref'] = r['whenarg'] = ''; r['again'] = False
    if r['when'] not in WHEN: return None
    if r['when'] in ('km', 'day'):
        r['whenarg'] = w[1] if len(w) > 1 else '0'; r['again'] = len(w) > 2 and low(w[2]) == 'again'
    elif r['when'] != 'start':
        r['whenref'] = w[1] if len(w) > 1 else ''; r['whenarg'] = ':'.join(w[2:]) if len(w) > 2 else ''
        if r['when'] == 'rule': r['whenref'] = clean_id(r['whenref'])
    w = [x.strip() for x in p[3].split(':')]
    needs = [x for x in w if low(x).startswith('needs=')]
    r['needs'] = needs[0][6:] if needs else ''
    w = [x for x in w if not low(x).startswith('needs=')]
    r['where'] = low(w[0]); r['whereref'] = ''; r['wherenum'] = ''
    if r['where'] not in WHERE: return None
    if r['where'] == 'near':
        r['whereref'] = w[1] if len(w) > 1 else 'self'; r['wherenum'] = w[2] if len(w) > 2 else ''
    else: r['wherenum'] = w[1] if len(w) > 1 else ''
    r['beside'] = r['place'] == 'beside'
    r['instory'] = bool(r['place']) and not r['beside']
    r['main'] = r['instory'] or r['beside']
    return r


def brings(r):
    if r['what'] == 'island': return [r['whatarg']] if r['whatarg'] else []
    if r['what'] == 'oneof': return [n.strip() for n in r['whatarg'].split(',') if n.strip()]
    return []


def build_steps(plan):
    story = [r for r in plan['rules'] if r['instory']]
    steps = ['rule:' + low(r['id']) for r in story if r['place'] == 'first']
    for key in CHAIN:
        inst = [r for r in story if r['place'] == 'instead:' + key]
        if inst: steps += ['rule:' + low(r['id']) for r in inst]
        elif plan['story'] and key not in plan['leaveout']: steps.append('raft:' + key)
        steps += ['rule:' + low(r['id']) for r in story if r['place'] == 'after:' + key]
    pending = [r for r in story if r['place'].startswith('after:') and not story_key(r['place'][6:])]
    placed, moved = {}, True
    while pending and moved:
        moved = False
        for r in list(pending):
            a = low(r['place'][6:])
            if 'rule:' + a not in steps: continue
            at = steps.index('rule:' + a); n = placed.get(a, 0)
            steps.insert(min(len(steps), at + 1 + n), 'rule:' + low(r['id']))
            placed[a] = n + 1; pending.remove(r); moved = True
    unplaced = [low(r['id']) for r in pending]
    return steps + ['rule:' + i for i in unplaced], unplaced


# ---------------- checks ----------------
class Plan:
    def __init__(s, plan, islands, scope):
        s.plan, s.islands, s.scope, s.cache = plan, islands, scope, {}
        s.P = os.path.basename(plan['path'])

    def fx(s, name):
        k = low(name)
        if k not in s.islands: return None
        if k not in s.cache:
            try: s.cache[k] = facts(s.islands[k])
            except Exception as e:
                add('ERROR', s.scope, s.islands[k], 'island file can not be read (%s): it never appears' % e); s.cache[k] = None
        return s.cache[k]

    def loc(s, r): return '%s:%d' % (s.P, r['line'])


def check_plan(plan, islands, scope, storyline=True):
    c = Plan(plan, islands, scope)
    STATS['rules'] += len(plan['rules'])
    rules = plan['rules']; loc = c.loc
    rid = lambda r: "rule '%s'" % r['id']
    for ln, v in plan['kept']:
        add('ERROR', scope, '%s:%d' % (c.P, ln), 'rule line the game can not read (unknown what/when/where or under 4 parts): dropped, its island never comes: ' + v[:100])
    ids = {}
    for r in rules:
        if not r['id']: add('ERROR', scope, loc(r), 'rule has no id: nothing can wait for it or come after it')
        elif low(r['id']) in ids: add('ERROR', scope, loc(r), "duplicate rule id '%s' (also line %d): the game renames this one '%sb', so waits and story places meant for it hit the first" % (r['id'], ids[low(r['id'])]['line'], r['id']))
        else: ids[low(r['id'])] = r
    if storyline and rules and not any(r['when'] == 'day' for r in rules):
        add('INFO', scope, c.P, 'storyline has no rule with a day: trigger (user 10-16: the storylines as a set must use one; not needed in each)')
    # what / where / names
    for r in rules:
        for n in brings(r):
            if low(n) not in islands:
                add('ERROR', scope, loc(r), "%s brings '%s', which is not shipped: the island never comes%s" % (rid(r), n, ' and the story stops here' if r['main'] else ''))
        if r['what'] in ('island', 'oneof') and not brings(r): add('ERROR', scope, loc(r), '%s brings no island' % rid(r))
        if r['wherenum'] and not re.match(r'^-?\d+(\.\d+)?$', r['wherenum']):
            add('WARN', scope, loc(r), "%s: distance '%s' in WHERE is no number (the game uses its default)" % (rid(r), r['wherenum']))
        if r['main'] and r['where'] != 'receiver':
            add('ERROR', scope, loc(r), '%s is main story but not on the Receiver: no coordinates for its notebook tab' % rid(r))
        if r['needs'] and not (low(r['needs']) in ('engine', 'steering', 'controls') or r['needs'].lower() in ITEMS and r['needs'].startswith('Blueprint_')):
            add('WARN', scope, loc(r), "%s needs '%s' for its dot: not engine/steering/controls or a Raft blueprint" % (rid(r), r['needs']))
        if r['placeraw'] and not r['place']:
            add('ERROR', scope, loc(r), "%s: story place '%s' can not be read: the island is silently left out of the story" % (rid(r), r['placeraw']))
        if r['doneraw'] and not r['done']:
            add('ERROR', scope, loc(r), "%s: done-when '%s' can not be read: silently falls back to its quest (or a visit)" % (rid(r), r['doneraw']))
        for t in (r['message'], r['tabintro']):
            m = COMPASS_RX.search(t or '')
            if m: add('WARN', scope, loc(r), "%s: its message says '%s' (a compass direction - point at landmarks)" % (rid(r), m.group(0)))
        if r['when'] in ('km', 'day'):
            try:
                if float(r['whenarg']) < 0: raise ValueError
            except ValueError: add('ERROR', scope, loc(r), '%s waits for %s with no number ("%s"): never comes' % (rid(r), r['when'], r['whenarg']))
        elif r['when'] == 'step':
            if not re.match(r'^\d+$', r['whenarg'] or '') or int(r['whenarg']) < 1: add('ERROR', scope, loc(r), '%s waits for quest steps with no number ("%s")' % (rid(r), r['whenarg']))
        if r['when'] == 'rule':
            if not r['whenref']: add('ERROR', scope, loc(r), '%s waits for another rule but names none' % rid(r))
            elif low(r['whenref']) == low(r['id']): add('ERROR', scope, loc(r), '%s waits for itself: never comes' % rid(r))
            elif low(r['whenref']) not in ids: add('ERROR', scope, loc(r), "%s waits for rule '%s', which isn't in the plan: never comes" % (rid(r), r['whenref']))
        elif r['when'] not in ('start', 'km', 'day'):
            ref = r['whenref']
            if not ref: add('ERROR', scope, loc(r), '%s waits for something on an island but names none' % rid(r)); continue
            rr = ids.get(low(ref))
            names = brings(rr) if rr else [ref]
            if not rr and low(ref) not in islands:
                add('ERROR', scope, loc(r), "%s waits for '%s': no rule and no shipped island has that name - never comes" % (rid(r), ref)); continue
            if not rr and not any(low(n) == low(ref) for x in rules for n in brings(x)):
                add('ERROR' if not plan['random'] else 'WARN', scope, loc(r), "%s waits for island '%s', which no rule brings" % (rid(r), ref))
            for n in names:
                f = c.fx(n)
                if f: check_island_for(r, r['when'], r['whenarg'], f, 'WHEN', loc(r), scope)
        if r['where'] == 'near' and low(r['whereref']) not in ('self', ''):
            if low(r['whereref']) not in ids and low(r['whereref']) not in islands:
                add('ERROR', scope, loc(r), "%s is placed near '%s': no rule or island of that name - never placed" % (rid(r), r['whereref']))
        elif r['where'] == 'near' and r['when'] in ('start', 'km', 'day'):
            add('ERROR', scope, loc(r), '%s is placed near "where it happened" but its WHEN happens at no island' % rid(r))
    # story
    steps, unplaced = build_steps(plan)
    utopia_ends = plan['story'] and 'utopia' not in plan['leaveout'] and not any(x['place'] == 'instead:utopia' for x in rules)
    for r in rules:
        if not r['instory']: continue
        if r['place'] == 'after:utopia' and utopia_ends:
            add('ERROR', scope, loc(r), '%s comes after Utopia, which never counts as done: never unlocks' % rid(r))
        if low(r['id']) in unplaced:
            a = r['place'][6:]
            circle = low(a) in ids and ids[low(a)]['instory']
            add('ERROR' if utopia_ends else 'WARN', scope, loc(r), "%s comes in the story after '%s', which %s: it goes to the end%s" % (
                rid(r), a, 'is in a circle of story places' if circle else 'is not in the story', ' (after Utopia: never unlocks)' if utopia_ends else ''))
        if r['when'] != 'start' and not (r['when'] == 'rule' and low(r['whenref']) in ids and ids[low(r['whenref'])]['instory']):
            add('INFO', scope, loc(r), "%s is in the story but its WHEN is '%s', not start: when its turn comes it still waits, without a word" % (rid(r), r['when']))
        kind = r['done'].split(':')[0] if r['done'] else ''
        arg = r['done'].split(':', 1)[1] if ':' in r['done'] else ''
        for n in brings(r):
            f = c.fx(n)
            if not f: continue
            if kind == '' and not f['quests'][0]: continue
            if kind in ('', 'quest'): check_island_for(r, 'quest', '', f, 'STORY', loc(r), scope)
            elif kind in ('step', 'zone', 'signal', 'note'): check_island_for(r, kind, arg, f, 'STORY', loc(r), scope)
    fired = simulate(c, ids, steps, utopia_ends)
    seen = set()
    for r in rules:
        for n in brings(r):
            f = c.fx(n)
            if f and low(n) not in seen:
                seen.add(low(n)); island_checks(f, scope, c, r, fired)
    return c, steps


def check_island_for(r, kind, arg, f, part, where, scope):
    who = "rule '%s'%s" % (r['id'], ' (STORY done-when)' if part == 'STORY' else '')
    eff = 'the story stops here' if part == 'STORY' else 'the rule never fires'
    nm = f['file']
    if kind == 'quest' and part != 'STORY' and arg.strip().isdigit() and int(arg) > 1:
        if int(arg) > len(f['quests']) or not f['quests'][int(arg) - 1]:
            add('ERROR', scope, where, "%s waits for quest %s of '%s', which has %d: %s" % (who, arg, nm, len(f['quests']), eff))
        return
    if kind in ('quest', 'step'):
        if not f['quests'][0]: add('ERROR', scope, where, "%s waits for the quest of '%s', which has none: %s" % (who, nm, eff)); return
        if kind == 'step' and arg.isdigit() and int(arg) > len(f['quests'][0]):
            add('ERROR', scope, where, "%s waits for %s steps of '%s', its quest has %d: %s" % (who, arg, nm, len(f['quests'][0]), eff))
    elif kind == 'zone':
        if low(arg) not in f['zones']: add('ERROR', scope, where, "%s waits for zone '%s' of '%s', which it doesn't have: %s" % (who, arg, nm, eff))
    elif kind == 'note':
        if not arg.strip().isdigit() or int(arg) not in f['note_idx']:
            add('ERROR', scope, where, "%s waits for note #%s of '%s', which is no note with a text: %s" % (who, arg, nm, eff))
    elif kind == 'signal':
        if low(arg) not in f['signals']: add('ERROR', scope, where, "%s waits for signal '%s' from '%s', which nothing there sends: %s" % (who, arg, nm, eff))


def simulate(c, ids, steps, utopia_ends):
    """Fire order: a rule fires when its WHEN is met (in the story: when the step before it is done too)."""
    rules = c.plan['rules']
    fired, raft_done, blocked = {}, set(), set()
    pos = {s: i for i, s in enumerate(steps)}
    for r in rules:
        if not r['instory']: continue
        kind = r['done'].split(':')[0] if r['done'] else ''
        arg = r['done'].split(':', 1)[1] if ':' in r['done'] else ''
        for n in brings(r) or ['']:
            f = c.fx(n) if n else None
            if not f: blocked.add(low(r['id'])); continue
            ok = True
            if kind == 'quest' or kind == '' and f['quests'][0]: ok = bool(f['quests'][0])
            elif kind == 'step': ok = arg.isdigit() and int(arg) <= len(f['quests'][0])
            elif kind == 'zone': ok = low(arg) in f['zones']
            elif kind == 'signal': ok = low(arg) in f['signals']
            elif kind == 'note': ok = arg.strip().isdigit() and int(arg) in f['note_idx']
            if not ok: blocked.add(low(r['id']))

    def step_done(s):
        return s in raft_done if s.startswith('raft:') else (s[5:] in fired and s[5:] not in blocked)

    def ref_rule(ref):
        rr = ids.get(low(ref))
        if rr is None:
            rr = next((x for x in rules if any(low(n) == low(ref) for n in brings(x))), None)
        return rr

    order, changed = 0, True
    while changed:
        changed = False
        for i, s in enumerate(steps):
            if s.startswith('raft:') and s not in raft_done and (i == 0 or step_done(steps[i - 1])) and not (s == 'raft:utopia' and utopia_ends):
                raft_done.add(s); changed = True
        for r in rules:
            k = low(r['id'])
            if k in fired: continue
            if r['when'] not in ('start', 'km', 'day'):
                rr = ref_rule(r['whenref'])
                if rr is None or low(rr['id']) not in fired: continue
            if r['where'] == 'near' and low(r['whereref']) not in ('self', ''):
                rr = ref_rule(r['whereref'])
                if rr is not None and low(rr['id']) not in fired: continue
            if r['instory']:
                i = pos.get('rule:' + k)
                if i is None or i > 0 and not step_done(steps[i - 1]): continue
            fired[k] = order; order += 1; changed = True
    for r in rules:
        if low(r['id']) not in fired:
            add('ERROR', c.scope, c.loc(r), "rule '%s' never fires in play (its WHEN, its place or the story step before it never comes): its island never comes" % r['id'])
    c.raft_done = raft_done
    return fired


def item_ok(name):
    return name.lower() in ITEMS


def island_checks(f, scope, c, rule, fired):
    """Every quest of the island (PlanChecker.CheckQuest and the step kinds' StepProblem), its texts, its reward."""
    nm = f['file']
    plan = c.plan if c else None
    for n, d in f['dropped']:
        add('ERROR', scope, rloc(nm, d.split('|')[0] + '|'), "'%s' %s: step line '%s' has a kind the game doesn't know: dropped silently" % (nm, qn(n), d[:70]))
    for n, l, why in f['badcount']:
        add('WARN', scope, rloc(nm, l[:30]), "'%s' %s: step '%s' count %s" % (nm, qn(n), l[:60], why))
    for n, k in f['cut']:
        add('ERROR', scope, rloc(nm), "'%s' %s has %d steps over %d: the game cuts them" % (nm, qn(n), k, MAX_STEPS))
    for i, onm, key, b in f['badverbs']:
        add('WARN', scope, rloc(nm, b[:25]), "'%s' %s %s: action line '%s' has a verb the game doesn't know: dropped silently" % (nm, onm, key, b[:60]))
    # a later island gives what this one needs? (blueprints / story items of the plan, by fire order)
    for qi, steps in enumerate(f['quests']):
        if not steps: continue
        STATS['quests'] += 1; STATS['steps'] += len(steps)
        title = f['props'].get(qkey(qi, 'title'), '')
        for s, st in enumerate(steps):
            check_step(f, scope, c, rule, fired, qi, s, st)
        for k in ('intro', 'done'):
            m = COMPASS_RX.search(f['props'].get(qkey(qi, k), ''))
            if m: add('WARN', scope, rloc(nm, qkey(qi, k)), "'%s' %s %s text says '%s' (compass direction: point at landmarks)" % (nm, qn(qi), k, m.group(0)))
        rew = f['props'].get(qkey(qi, 'reward'), '')
        for it, n in loot(rew):
            if n is None: add('ERROR', scope, rloc(nm, qkey(qi, 'reward')), "'%s' %s reward '%s' has no number after * : dropped" % (nm, qn(qi), it))
            elif it.lower().startswith('story:'):
                if low(it[6:]) not in f['storydefs']: add('INFO', scope, rloc(nm, qkey(qi, 'reward')), "'%s' %s reward story item '%s' is not in the island's Story items list" % (nm, qn(qi), it))
            elif not item_ok(it): add('ERROR', scope, rloc(nm, qkey(qi, 'reward')), "'%s' %s reward '%s': Raft has no such item - it is dropped (players get nothing for it)" % (nm, qn(qi), it))
            elif ITEMS[it.lower()] != it: add('WARN', scope, rloc(nm, qkey(qi, 'reward')), "'%s' %s reward '%s': Raft's name is '%s' (case)" % (nm, qn(qi), it, ITEMS[it.lower()]))
        if not title.strip() and qi > 0: add('INFO', scope, rloc(nm), "'%s' %s has steps but no title" % (nm, qn(qi)))
    # loot / zone / give items that don't exist
    for it, src in f['raftitems'].items():
        if not item_ok(it): add('ERROR', scope, rloc(nm, it + '*'), "'%s': %s names '%s', which Raft doesn't have (given as nothing)" % (nm, src, it))
    bad = sorted({t for (_, _, _, _, t) in f['targets'] if low(t) not in f['names']})
    if bad: add('WARN', scope, rloc(nm), "'%s': show/hide/open/close actions name objects that don't exist: %s" % (nm, bad[:6]))
    br = f['props'].get('bring.rules', '')
    for line in br.split('\n'):
        if not line.strip(): continue
        r = parse_rule(line)
        if r is None: add('ERROR', scope, rloc(nm), "'%s' own rule line can not be read: dropped silently: %s" % (nm, line[:90])); continue
        for n in brings(r):
            if low(n) in (c.islands if c else {}): continue
            if low(n) in LIBRARY_NAMES:
                add('INFO', scope, rloc(nm), "'%s' brings '%s' by its own rule '%s' only when that library island is installed too" % (nm, n, r['id']))
            else: add('ERROR', scope, rloc(nm), "'%s' brings '%s' by its own rule '%s', which is not shipped anywhere: it never comes" % (nm, n, r['id']))


def qn(n): return 'quest' if n == 0 else 'quest %d' % (n + 1)


def given_before(c, fired, rule, what, sid=None, blueprint=None):
    """Who gives it in this plan before this rule fires: 'here', 'before', 'later', 'raft' or None."""
    if c is None or rule is None: return None
    k = low(rule['id']); here = fired.get(k)
    best = None
    for r in c.plan['rules']:
        for n in brings(r):
            g = c.fx(n)
            if not g: continue
            has = (sid in g['gives'] or sid in g['loot_gives']) if sid else (blueprint.lower() + '*' in g['alltext'].lower())
            if not has: continue
            o = fired.get(low(r['id']))
            if low(r['id']) == k: return 'here'
            if o is not None and here is not None and o < here: best = 'before'
            elif best is None: best = 'later'
    if blueprint and RAFT_BP.get(blueprint.lower()) and 'raft:' + RAFT_BP[blueprint.lower()] in getattr(c, 'raft_done', ()):
        return best or 'raft'
    return best


def check_step(f, scope, c, rule, fired, qi, s, st):
    nm = f['file']; t = st['target'].strip(); lt = low(t); ty = st['type']; cnt = st['count']
    w = rloc(nm, 'step ' + ty + '|' + t + '|') if t else rloc(nm, 'step ' + ty + '|')
    head = "'%s' %s step %d (%s|%s)" % (nm, qn(qi), s + 1, ty, t)
    E = lambda msg: add('ERROR', scope, w, head + ': ' + msg + ' - the quest can not be finished')
    W = lambda msg: add('WARN', scope, w, head + ': ' + msg)
    text = st['text']
    m = COMPASS_RX.search(text)
    if m: W("its text says '%s' (compass direction: point at landmarks)" % m.group(0))
    if ty == 'reach':
        if t and lt not in f['zones']: E("no trigger zone '%s'" % t)
        elif not t and not f['zones']: E('the island has no trigger zone')
    elif ty == 'read':
        if t and lt not in f['notes']: E('no note titled "%s"' % t)
        elif not t and not f['notecount']: E('the island has no note')
    elif ty == 'open':
        if t and lt not in f['chests_full'] | f['chests_empty']: E('no chest titled "%s"' % t)
        elif t and lt in f['chests_empty'] and lt not in f['chests_full']: E('chest "%s" is empty: Raft can not open an empty chest' % t)
        elif not t and (not f['chestcount'] or f['emptychests'] == f['chestcount']): E('no chest that can be opened')
    elif ty in ('kill', 'catch'):
        if ty == 'catch' and t and lt not in CATCHABLE and lt in {low(v) for v in CREATURES.values()}: E("Raft's net launcher can't catch %s" % t)
        else:
            have = f['creatures'].get(lt, 0) if t else sum(v for k, v in f['creatures'].items() if ty == 'kill' or k in CATCHABLE)
            if have == 0: E('the island has no %s' % (t or 'creatures'))
            elif cnt > have: W('wants %d, the island has %d at a time (fine only if they come back)' % (cnt, have))
    elif ty == 'collect':
        if not t: return
        sid = low(t[6:] if lt.startswith('story:') else t)
        if sid.startswith('raft-'): return
        g = f['gives'].get(sid, 0) + (1 if sid in f['loot_gives'] else 0)
        if g == 0:
            where = given_before(c, fired, rule, 'story', sid=sid)
            if where in (None, 'later'): E("nothing %s gives story:%s" % ('in the world before it' if where == 'later' else 'in this world' if c else 'on the island', sid))
        elif sid in f['loot_gives'] and not CONTAINER_RX.search(text) and f['gives'].get(sid, 0) == 0:
            W("story:%s lies in a chest or crate, its text doesn't say so ('%s')" % (sid, text[:60]))
    elif ty == 'blueprint':
        if t and not item_ok(t): E("Raft has no blueprint '%s'" % t)
        elif t and (t + '*').lower() not in f['alltext'].lower():
            where = given_before(c, fired, rule, 'bp', blueprint=t)
            if where in (None, 'later'): E('nothing in the world before it gives %s' % t)
    elif ty == 'pages':
        if t != 'all' and f['pages'] < cnt: E('needs %d pages, the island gives %d' % (cnt, f['pages']))
    elif ty == 'talk':
        if not f['islanders']: E('no islander')
        elif t and lt not in f['islanders']: E("no islander '%s'" % t)
    elif ty == 'set':
        if not t or lt not in f['sets']: E("no piece of set '%s'" % t)
        elif cnt > f['sets'][lt]: E('wants %d, the set has %d' % (cnt, f['sets'][lt]))
    elif ty == 'lights':
        if not t or lt not in f['lights']: E("no light in group '%s'" % t)
        elif cnt > f['lights'][lt]: E('wants %d lights, the group has %d' % (cnt, f['lights'][lt]))
    elif ty == 'escort':
        who, _, zone = t.rpartition('@') if '@' in t else (t, '', '')
        if not who.strip() or not zone.strip(): E('needs Name@zone')
        elif low(who) not in f['islanders']: E("no islander '%s'" % who)
        elif low(zone) not in f['zones']: E("no trigger zone '%s'" % zone)
    elif ty == 'tame':
        who, _, pen = t.rpartition('@') if '@' in t else (t, '', '')
        if not who.strip() or not pen.strip(): E('needs Animal@zone')
        elif low(who) not in f['tamable']: E("no goat, llama or chicken spot '%s'" % who)
        elif low(pen) not in f['zones']: E("no trigger zone '%s'" % pen)
        elif cnt > MAX_PEN: E('a pen takes at most %d' % MAX_PEN)
        elif cnt > f['tamable'][low(who)]: E('wants %d, the island has %d' % (cnt, f['tamable'][low(who)]))
    elif ty == 'signal':
        if not t: E('names no signal')
        elif '=' in t and low(t.rsplit('=', 1)[1]) in STATES:
            if low(t.rsplit('=', 1)[0]) not in f['names']: E("no object named '%s'" % t.rsplit('=', 1)[0])
        elif '=' in t: E("'%s' is no state (open, closed, shown, hidden)" % t)
        elif lt not in f['signals']: W("nothing on the island sends '%s' (only works if another island or the plan does)" % t)
    elif ty == 'bring':
        item, _, to = t.partition('@')
        if not item.strip(): E('names no item')
        elif not item_ok(item.strip()): E("Raft has no item '%s'" % item.strip())
        elif not f['receivercount']: E('no islander or chest to hand it to')
        elif to.strip() and low(to) not in f['receivers']: E("no islander or chest '%s'" % to.strip())
        else: resource_check(f, scope, c, w, head, item.strip())
    elif ty in ('craft', 'gather', 'fish'):
        if t and not item_ok(t): E("Raft has no item '%s'" % t)
        elif ty == 'fish' and t and t not in FISH: W("Raft's rod doesn't catch '%s'" % t)
        elif t: resource_check(f, scope, c, w, head, t, craft=ty == 'craft', rule=rule, fired=fired)
    elif ty == 'time':
        for x in (t or '').replace(',', ' ').split():
            if x not in ('day', 'night', 'dawn', 'dusk') and not re.match(r'^\d{1,2}(\.\d+)?-\d{1,2}(\.\d+)?$', x): E("'%s' is no time" % x)
        if not t: E('names no time')
    elif ty == 'weather':
        for x in (t or '').replace(',', ' ').split():
            if x not in WEATHERS: E("'%s' is no weather" % x)
        if not t: E('names no weather')
    elif ty == 'counter':
        if not t: E('names no counter')
        elif lt not in f['counters']: E("no count action on the island changes '%s'" % t)
    elif ty == 'players':
        if not t or lt not in f['zones']: E("no trigger zone '%s'" % t)
        elif cnt > MAX_PLAYERS: E('wants %d players, Raft has at most %d' % (cnt, MAX_PLAYERS))


def resource_check(f, scope, c, w, head, item, craft=False, rule=None, fired=None):
    """Raft items only some sources give: in the world (this island, the plan's islands) before the step needs them."""
    texts = [f['alltext']] + ([g['alltext'] for g in c.cache.values() if g] if c else [])
    animal = SPECIAL.get(item)
    if animal:
        has = f['creatures'].get(animal, 0) or (c and any(g and g['creatures'].get(animal) for g in c.cache.values()))
        given = any(item.lower() + '*' in tx.lower() for tx in texts)
        if not has and not given: add('WARN', scope, w, head + ': %s comes only from a %s - no %s and no %s given in this world' % (item, animal, animal, item))
    if item in TITANIUM and not any(('titaniumore*' in tx.lower() or 'titaniumingot*' in tx.lower()) for tx in texts):
        add('WARN', scope, w, head + ": %s: no titanium is given in this world (only Raft's late islands' rocks)" % item)
    if craft:
        bp = 'blueprint_' + re.sub(r'^(placeable_)', '', item.lower())
        if bp in ITEMS:
            where = given_before(c, fired, rule, 'bp', blueprint=ITEMS[bp]) if c else None
            if (bp + '*') in f['alltext'].lower(): return
            if where in (None, 'later'): add('ERROR', scope, w, head + ': crafting it needs %s, which nothing in the world before it gives - the quest can not be finished' % ITEMS[bp])


# ---------------- run ----------------
def stage_files(stage):
    """stage: one folder or several split by ';' (the newest copy of a file wins)."""
    out = {}
    for d in (stage or '').split(';'):
        if not os.path.isdir(d): continue
        for p in glob.glob(os.path.join(d, '**', '*.island'), recursive=True) + glob.glob(os.path.join(d, '**', '*.plan'), recursive=True):
            k = low(os.path.basename(p))
            if k not in out or os.path.getmtime(p) > os.path.getmtime(out[k]): out[k] = p
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--mods', default=DEF_MODS); ap.add_argument('--library', default=DEF_LIBRARY); ap.add_argument('--stage', default=DEF_STAGE)
    ap.add_argument('--json'); ap.add_argument('--all', action='store_true'); ap.add_argument('--quiet', action='store_true')
    a = ap.parse_args()
    load_game_lists(); load_recipes(); check_recipes()
    counts = Counter()
    for kind, it in DAILY:
        if not item_ok(it): add('ERROR', 'daily', 'DailyQuest.cs ' + kind, "daily quest item '%s': Raft has no such item" % it)
    # --- the mod's own content (built from content/recipes)
    if a.mods and os.path.isdir(a.mods):
        isl = {low(os.path.splitext(os.path.basename(p))[0]): p for p in glob.glob(os.path.join(a.mods, '*.island'))}
        for pp in sorted(glob.glob(os.path.join(a.mods, 'plans', '*.plan'))):
            name = os.path.splitext(os.path.basename(pp))[0]
            # (test plans made by dev tests; "(Custom Islands)" = a library pack copied in by a test - checked as library)
            if name.startswith('CI ') or name.endswith('(Custom Islands)'): continue
            check_plan(parse_plan(pp), isl, 'mods:' + name); counts['mods plans'] += 1
    # --- the online library (deploy + staged newer files)
    if a.library and os.path.isdir(a.library):
        st = stage_files(a.stage)
        for p in glob.glob(os.path.join(a.library, '**', '*.island'), recursive=True): LIBRARY_NAMES.add(low(os.path.splitext(os.path.basename(p))[0]))
        for d in sorted(glob.glob(os.path.join(a.library, 'plans', '*'))):
            files = {low(os.path.basename(p)): p for p in glob.glob(os.path.join(d, '*'))}
            files.update({k: v for k, v in st.items() if k in files})
            isl = {k[:-7]: v for k, v in files.items() if k.endswith('.island')}
            for k, pp in files.items():
                if k.endswith('.plan'):
                    check_plan(parse_plan(pp), isl, 'library:' + os.path.basename(d)); counts['library plans'] += 1
        for d in sorted(glob.glob(os.path.join(a.library, 'islands', '*'))):
            for p in glob.glob(os.path.join(d, '*.island')):
                p = st.get(low(os.path.basename(p)), p)
                try: f = facts(p)
                except Exception as e: add('ERROR', 'library-island', p, 'can not be read: %s' % e); continue
                counts['library islands'] += 1
                island_checks(f, 'library-island:' + os.path.basename(d), None, None, {})
    order = {'ERROR': 0, 'WARN': 1, 'INFO': 2}
    # (one finding once: an island in two plans or packs is told for each scope - fold same text+where)
    seen, out = set(), []
    for x in sorted(F, key=lambda x: (order[x['level']], x['scope'], x['where'])):
        key = (x['level'], x['where'], x['text'])
        if key in seen: continue
        seen.add(key); out.append(x)
    if a.json: json.dump(out, open(a.json, 'w', encoding='utf-8'), indent=1)
    if not a.quiet:
        for x in out:
            if x['level'] != 'INFO' or a.all: print('%-5s %s | %s | %s' % (x['level'], x['scope'], x['where'], x['text']))
    print('checked: ' + ', '.join('%d %s' % (v, k) for k, v in list(counts.items()) + list(STATS.items())) + ' (an island in several scopes counts each time)')
    print('findings: ' + ', '.join('%s %d' % (k, v) for k, v in sorted(Counter(x['level'] for x in out).items(), key=lambda kv: order[kv[0]])))
    return 1 if any(x['level'] == 'ERROR' for x in out) else 0


if __name__ == '__main__':
    sys.exit(main())
