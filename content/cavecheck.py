#!/usr/bin/env python3
"""cavecheck - an offline check of where a recipe's caves go (G7, 11-04; no Raft needed).

Run: tools\\content.ps1 cavecheck <recipe> [-repo <worktree>]   or   python content/cavecheck.py <recipe|path> [--machete]
     python content/cavecheck.py --selftest   (the cave tool's layout against CaveTool.Plan's numbers)
Expands the recipe as the mod does (set, $vars, {arithmetic}, for/next, macro/call, include; DevTestsRecipe.ExpandLines),
follows origin/push/pop frames, and collects per island (new ... save):
  caves   - `cave from .. to ..` (the cave tool: passage half width 3 m x size, a room of 6 m x size at end=room) and `dig`
            strokes (the Dig brush: their radius); a mouth is a cave's first point, or a stroke's first point that is not
            in a passage dug before it. Kits from lib_caves expand to these.
  objects - place/scatter/beside/store/vending/line; kinds: quest (a step's target by alias, obj.name, zone.id or note
            title, or an object with if./on./else. settings), islander (talk. settings, Story_Islander), sign, chest,
            ladder, spawn (creatures, creature.* settings), den (BalboaCave_*, bears).
It fails (exit 1) when
  1 a cave/dig volume or mouth is < 6 m (across the ground) from such an object;
  2 a cave/dig passes over or under a quest object (the object's ground point inside the volume);
  3 a mouth sits on the main walk line (the line through the quest steps' targets in order, outside caves; 3 m);
  4 a gated cave (cavegate, or ChoppableVines / BalboaCave_Vines within 6 m of its mouth) holds a step's target before
    the machete step (a step on Machete / Blueprint_Machete, or opening the chest whose loot has a Machete); --machete or a
    `# cavecheck machete` line: the plan gave the machete on an earlier island.
Objects meant to be in a cave (a chest in the end room): a `# cavecheck inside <alias> ...` line in the recipe; they are
then checked to be inside a cave instead. Heights are not known offline: distances are across the ground (x z), and
`origin` alone (the land centre) counts as (500, 500), so mixing it with `origin x z` positions is approximate.
"""
import math, os, random, re, sys

ROOT = os.path.dirname(os.path.abspath(__file__))
CLEAR = 6.0          # metres a cave keeps from people and things
WALK = 3.0           # half width of the main walk line
ST_HALF = 3.0        # cave tool passage half width (CaveTool.PassX 3.0) x size
# The cave tool's layout (CaveTool.Plan): tunnels of Step x size up the slope, as many as fit the distance (rounded,
# 1..MaxTunnels); the den beyond the last one, from RoomIn back over the tunnel's end to its far wall, RoomMin/RoomMax
# across (its mouth, RoomMouth.x, on the tunnel's line). Before 11-04 the room was a 6 m disc at the `to` point: the
# real den (BalboaCave_Bear, about 30 x 32 m x size) reaches some 30 m x size past it (Shelter Atoll's lagoon cave).
ST_STEP, ST_MAXSLOPE, ST_MAXTUNNELS, ST_MINSIZE, ST_MAXSIZE = 10.399, 12.0, 12, 0.7, 1.5
ST_ROOM_BACK = 0.6                       # RoomIn: the den reaches this far back over the tunnel's end
ST_ROOM_LEN = 15.99 + 15.79              # RoomMax.z - RoomMin.z
ST_ROOM_HALF = 15.85 - 1.072             # RoomMin.x - RoomMouth.x (= RoomMax.x - RoomMouth.x, 14.78)
LAND_CENTRE = (500.0, 500.0)


class RecipeError(Exception):
    pass


# ---------------------------------------------------------------- preprocessor (DevTestsRecipe.cs ExpandLines)

class Expr:
    def __init__(self, s, rnd):
        self.s, self.i, self.rnd = s.strip(), 0, rnd

    def skip(self):
        while self.i < len(self.s) and self.s[self.i] == ' ':
            self.i += 1

    def eat(self, c):
        self.skip()
        if self.i < len(self.s) and self.s[self.i] == c:
            self.i += 1
            return True
        return False

    def expr(self):
        v = self.term()
        while True:
            if self.eat('+'): v += self.term()
            elif self.eat('-'): v -= self.term()
            else: return v

    def term(self):
        v = self.power()
        while True:
            if self.eat('*'): v *= self.power()
            elif self.eat('/'):
                d = self.power()
                v = v / d if d != 0 else (math.inf if v >= 0 else -math.inf)
            elif self.eat('%'): v = math.fmod(v, self.power())
            else: return v

    def power(self):
        v = self.unary()
        return v ** self.power() if self.eat('^') else v

    def unary(self):
        if self.eat('-'): return -self.unary()
        if self.eat('+'): return self.unary()
        return self.primary()

    def primary(self):
        self.skip()
        if self.eat('('):
            v = self.expr()
            if not self.eat(')'): raise RecipeError("a ')' missing")
            return v
        s, st = self.s, self.i
        if self.i < len(s) and (s[self.i].isdigit() or s[self.i] == '.'):
            while self.i < len(s) and (s[self.i].isdigit() or s[self.i] in '.eE'):
                self.i += 1
            try:
                return float(s[st:self.i])
            except ValueError:
                raise RecipeError("can't read number '%s'" % s[st:self.i])
        while self.i < len(s) and (s[self.i].isalpha() or (self.i > st and s[self.i].isdigit())):
            self.i += 1
        name = s[st:self.i].lower()
        if not name: raise RecipeError("a number missing at '%s'" % s[st:])
        if name == 'pi': return math.pi
        if not self.eat('('): raise RecipeError("unknown '%s'" % name)
        args = []
        if not self.eat(')'):
            args.append(self.expr())
            while self.eat(','):
                args.append(self.expr())
            if not self.eat(')'): raise RecipeError("a ')' missing")
        a = lambda k: args[k] if k < len(args) else 0.0
        D = math.pi / 180
        f = {'sin': lambda: math.sin(a(0) * D), 'cos': lambda: math.cos(a(0) * D), 'tan': lambda: math.tan(a(0) * D),
             'atan2': lambda: math.atan2(a(0), a(1)) / D, 'sqrt': lambda: math.sqrt(a(0)), 'abs': lambda: abs(a(0)),
             'min': lambda: min(args), 'max': lambda: max(args), 'floor': lambda: float(math.floor(a(0))),
             'ceil': lambda: float(math.ceil(a(0))), 'round': lambda: float(round(a(0))),
             'rnd': lambda: a(0) + self.rnd.random() * (a(1) - a(0))}.get(name)
        if f is None: raise RecipeError("unknown function '%s'" % name)
        return f()


def num(v):
    v = round(v, 4)
    s = ('%.4f' % v).rstrip('0').rstrip('.')
    return '0' if s in ('-0', '') else s


def tokens(line):
    out, cur, q = [], [], False
    for c in line:
        if c == '"': q = not q; continue
        if c == ' ' and not q:
            if cur: out.append(''.join(cur)); cur = []
            continue
        cur.append(c)
    if cur: out.append(''.join(cur))
    return out or ['']


def rest(line, words):
    i = 0
    for _ in range(words):
        while i < len(line) and line[i] == ' ': i += 1
        while i < len(line) and line[i] != ' ': i += 1
    return line[i:].lstrip(' ')


VAR = re.compile(r'\$([A-Za-z_][A-Za-z0-9_]*)')
BRACES = re.compile(r'\{([^{}]*)\}')


class Expander:
    def __init__(self, path, folders):
        self.folders = folders
        seed = 17
        for c in os.path.splitext(os.path.basename(path))[0]:
            seed = (seed * 31 + ord(c)) & 0xFFFFFFFF
        self.rnd = random.Random(seed)
        self.markers = []   # (kind, args, where) from '# cavecheck ...' lines

    def eval(self, s):
        p = Expr(s, self.rnd)
        v = p.expr()
        p.skip()
        if p.i < len(p.s): raise RecipeError("can't work out '%s'" % s)
        return v

    def subst(self, line, vars):
        s = VAR.sub(lambda m: vars.get(m.group(1).lower(), m.group(0)), line)
        for _ in range(20):
            if not BRACES.search(s): break
            s = BRACES.sub(lambda m: num(self.eval(m.group(1))), s)
        return s

    def find(self, name):
        for f in self.folders:
            p = os.path.join(f, name)
            if os.path.isfile(p): return p
        raise RecipeError("no file " + name)

    def read(self, path):
        name = os.path.basename(path)
        with open(path, encoding='utf-8-sig', errors='replace') as fh:
            lines = fh.read().split('\n')
        out = []
        for i, l in enumerate(lines):
            l = l.rstrip('\r')
            m = re.match(r'\s*#\s*cavecheck\s+(\w+)\s*(.*)', l)
            if m: self.markers.append((m.group(1).lower(), m.group(2).split(), name + ':' + str(i + 1)))
            out.append((l, name + ':' + str(i + 1)))
        return out

    @staticmethod
    def block_end(src, start, op, cl):
        depth = 0
        for i in range(start, len(src)):
            w = src[i][0].strip().split(' ')[0].lower()
            if w == op: depth += 1
            elif w == cl:
                depth -= 1
                if depth == 0: return i
        raise RecipeError("%s: '%s' without its '%s'" % (src[start][1], op, cl))

    def expand(self, src, vars, macros, out, depth, top=None):
        if depth > 40: raise RecipeError("macros or loops nested too deep")
        i = 0
        while i < len(src):
            raw, where = src[i][0].strip(), src[i][1]
            here = top or where
            if not raw or raw.startswith('#'):
                i += 1; continue
            first = raw.split(' ')[0].lower()
            try:
                if first == 'macro':
                    t = tokens(raw)
                    end = self.block_end(src, i, 'macro', 'end')
                    macros[t[1].lower()] = (t[2:], src[i + 1:end])
                    i = end + 1; continue
                if first == 'for':
                    t = tokens(self.subst(raw, vars))
                    end = self.block_end(src, i, 'for', 'next')
                    body = src[i + 1:end]
                    a, b = self.eval(t[2]), self.eval(t[3])
                    step = self.eval(t[4]) if len(t) > 4 else 1.0
                    if step == 0 or (b - a) / step > 5000: raise RecipeError("a loop of too many rounds")
                    v = a
                    while (v <= b + 1e-9) if step > 0 else (v >= b - 1e-9):
                        inner = dict(vars); inner[t[1].lower()] = num(v)
                        self.expand(body, inner, macros, out, depth + 1, here)
                        v += step
                    i = end + 1; continue
                line = self.subst(raw, vars)
                if first == 'set':
                    t = tokens(line)
                    value = rest(line, 2)
                    try: vars[t[1].lower()] = num(self.eval(value))
                    except Exception: vars[t[1].lower()] = value
                elif first == 'include':
                    f = rest(line, 1).strip()
                    if not f.endswith('.recipe') and not f.endswith('.play'): f += '.recipe'
                    self.expand(self.read(self.find(f)), vars, macros, out, depth + 1, here)
                elif first == 'call':
                    t = tokens(line)
                    m = macros.get(t[1].lower()) if len(t) > 1 else None
                    if m is None: raise RecipeError("no macro '%s'" % (t[1] if len(t) > 1 else ''))
                    params, body = m
                    names = [p.split('=')[0].lower() for p in params]
                    given, pos = {}, 0
                    for arg in t[2:]:
                        eq = arg.find('=')
                        if eq > 0 and arg[:eq].lower() in names: given[arg[:eq].lower()] = arg[eq + 1:]
                        elif pos < len(names): given[names[pos]] = arg; pos += 1
                        else: raise RecipeError("macro %s: one argument too many (%s)" % (t[1], arg))
                    inner = dict(vars)
                    for p in params:
                        eq = p.find('=')
                        pn = (p[:eq] if eq > 0 else p).lower()
                        if pn in given: inner[pn] = given[pn]
                        elif eq > 0: inner[pn] = self.subst(p[eq + 1:], inner)
                        else: raise RecipeError("macro %s needs %s" % (t[1], pn))
                    self.expand(body, inner, macros, out, depth + 1, here)
                else:
                    out.append((line, where, here))
            except RecipeError as e:
                if '.recipe:' in str(e): raise
                raise RecipeError("%s (%s): %s" % (where, raw, e))
            i += 1


# ---------------------------------------------------------------- geometry

def seg_dist(p, a, b):
    ax, az = a; bx, bz = b; px, pz = p
    dx, dz = bx - ax, bz - az
    l2 = dx * dx + dz * dz
    t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / l2))
    return math.hypot(px - ax - t * dx, pz - az - t * dz)


def box_dist(p, s, u, length, half):
    """How far p is outside a rectangle from s along unit u (length) and half wide across (below 0: inside)."""
    dx, dz = p[0] - s[0], p[1] - s[1]
    along, across = dx * u[0] + dz * u[1], abs(-dx * u[1] + dz * u[0])
    ox, oz = max(-along, along - length, 0.0), max(across - half, 0.0)
    if ox > 0 or oz > 0: return math.hypot(ox, oz)
    return -min(along, length - along, half - across)


def st_layout(a, b, slope, size):
    """The cave tool's plan of `cave from a to b` (CaveTool.Plan): the tunnels' far end and the den's rectangle."""
    k = max(ST_MINSIZE, min(ST_MAXSIZE, size))
    sl = math.radians(max(-ST_MAXSLOPE, min(ST_MAXSLOPE, slope)))
    dx, dz = b[0] - a[0], b[1] - a[1]
    d = math.hypot(dx, dz)
    u = (dx / d, dz / d) if d > 0.01 else (0.0, 1.0)
    n = max(1, min(ST_MAXTUNNELS, int(math.floor(d / (ST_STEP * k * math.cos(sl)) + 0.5))))
    ln = n * ST_STEP * k * math.cos(sl)
    end = (a[0] + u[0] * ln, a[1] + u[1] * ln)
    room = ((end[0] - u[0] * ST_ROOM_BACK * k, end[1] - u[1] * ST_ROOM_BACK * k), u, ST_ROOM_LEN * k, ST_ROOM_HALF * k)
    return k, end, room


class Cave:
    """A cave's ground plan: segments (a, b, half width) and its mouths."""
    def __init__(self, kind, group, where):
        self.kind, self.group, self.where = kind, group, where
        self.segs, self.mouths, self.gated = [], [], False
        self.boxes = []   # (start, unit direction, length, half width): a rectangle along the direction

    def dist(self, p):
        """How far p is outside the volume (below 0: inside)."""
        return min([seg_dist(p, a, b) - r for a, b, r in self.segs] + [box_dist(p, *bx) for bx in self.boxes])

    def mouth_dist(self, p):
        return min(math.hypot(p[0] - m[0], p[1] - m[1]) for m in self.mouths) if self.mouths else math.inf


class Obj:
    def __init__(self, name, alias, x, z, r, where):
        self.name, self.alias, self.x, self.z, self.r, self.where = name, alias, x, z, r, where
        self.kinds = set()

    @property
    def p(self): return (self.x, self.z)


# ---------------------------------------------------------------- the check

KIND_RULES = [
    ('islander', re.compile(r'Islander', re.I)),
    ('sign', re.compile(r'Sign|Note_Board', re.I)),
    ('chest', re.compile(r'Chest|Locker', re.I)),
    ('ladder', re.compile(r'Ladder', re.I)),
    ('den', re.compile(r'^BalboaCave_(?!Vines)|Bear', re.I)),
    ('spawn', re.compile(r'^Creature_|Spawner', re.I)),
]
VINES = re.compile(r'^(ChoppableVines|BalboaCave_Vines)$', re.I)


class Island:
    def __init__(self, where):
        self.where = where
        self.objs, self.caves, self.steps = [], [], []
        self.props = {}           # alias -> {key: value}
        self.notes = {}           # alias -> title
        self.loot = {}            # alias -> loot text
        self.gates = set()        # cave groups with cavegate
        self.name = ''


def opts(toks):
    o = {}
    for t in toks:
        eq = t.find('=')
        if eq > 0: o[t[:eq].lower()] = t[eq + 1:]
        else: o[t.lower()] = ''
    return o


def f(s):
    try: return float(s)
    except ValueError: raise RecipeError("'%s' is no number" % s)


def read_islands(lines):
    islands, isl = [], None
    frames = [((LAND_CENTRE[0], LAND_CENTRE[1]), 0.0)]

    def world(x, z):
        (ox, oz), yaw = frames[-1]
        c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        return (ox + x * c + z * s, oz - x * s + z * c)

    def cur(where):
        nonlocal isl
        if isl is None:
            isl = Island(where); islands.append(isl)
        return isl

    for line, where, top in lines:
        t = tokens(line)
        verb = t[0].lower()
        try:
            if verb == 'new':
                isl = Island(top); islands.append(isl)
                frames[:] = [(LAND_CENTRE, 0.0)]
            elif verb in ('generate', 'randomize'):
                frames[:] = [(LAND_CENTRE, 0.0)]
            elif verb == 'origin':
                frames[:] = [((f(t[1]), f(t[2])) if len(t) > 2 else LAND_CENTRE, 0.0)]
            elif verb == 'push':
                at = t.index('at')
                o = opts(t[at + 3:])
                frames.append((world(f(t[at + 1]), f(t[at + 2])), frames[-1][1] + (f(o['yaw']) if 'yaw' in o else 0.0)))
            elif verb == 'pop':
                if len(frames) > 1: frames.pop()
            elif verb == 'save':
                if isl is not None and not isl.name: isl.name = t[1] if len(t) > 1 else ''
                isl = None
            elif verb == 'info' and len(t) > 1 and t[1].lower().startswith('title='):
                cur(top).name = rest(line, 1)[6:]
            elif verb == 'cave':
                cf, ct = t.index('from'), t.index('to')
                o = opts(t[ct + 3:])
                a, b = world(f(t[cf + 1]), f(t[cf + 2])), world(f(t[ct + 1]), f(t[ct + 2]))
                k, end, room = st_layout(a, b, f(o['slope']) if 'slope' in o else 0.0, f(o['size']) if 'size' in o else 1.0)
                c = Cave('cave', o.get('as', ''), top)
                c.segs.append((a, end, ST_HALF * k))
                if o.get('end', 'room') != 'open':
                    c.boxes.append(room)
                c.mouths.append(a)
                if o.get('end', 'room') == 'open': c.mouths.append(end)
                cur(top).caves.append(c)
            elif verb == 'dig':
                o = opts([x for x in t[1:] if '=' in x or x in ('fill', 'shaft')])
                r0 = f(o['r']) if 'r' in o else 1.75
                strokes, s = [], []
                for tok in t[1:]:
                    if tok == '|':
                        if s: strokes.append(s)
                        s = []
                    elif '=' not in tok and tok not in ('fill', 'shaft'):
                        s.append(tok)
                if s: strokes.append(s)
                I = cur(top)
                group = o.get('as', '')
                for st in strokes:
                    pts = []
                    for p in st:
                        v = p.split(',')
                        if len(v) < 2 or len(v) > 4: raise RecipeError("'%s' is no point" % p)
                        x, z = f(v[0]), f(v[1] if len(v) == 2 else v[2])
                        pts.append((world(x, z), f(v[3]) if len(v) == 4 else r0))
                    if 'fill' in o: continue
                    first = pts[0][0]
                    # (a stroke from inside a dug passage branches from that tunnel system; else it is a new mouth)
                    inside = next((cc for cc in I.caves if cc.kind == 'dig' and cc.segs and cc.dist(first) < 0), None)
                    c = next((cc for cc in I.caves if cc.kind == 'dig' and group and cc.group == group), None) or inside
                    if c is None:
                        c = Cave('dig', group, top); I.caves.append(c)
                    if inside is None: c.mouths.append(first)
                    elif inside is not c:   # (two systems joined: one cave from here on)
                        c.segs += inside.segs; c.mouths += inside.mouths; c.group = c.group or inside.group
                        I.caves.remove(inside)
                    if len(pts) == 1: pts.append(pts[0])
                    for (a, ra), (b, rb) in zip(pts, pts[1:]):
                        c.segs.append((a, b, max(ra, rb)))
                    # (11-08: a shaft - straight up out of the passage - opens on the ground at its last point)
                    if 'shaft' in o: c.mouths.append(pts[-1][0])
            elif verb == 'cavegate':
                cur(top).gates.add(t[1])
            elif verb in ('place', 'store', 'vending', 'beside', 'scatter', 'line'):
                I = cur(top)
                o = opts(t)
                if verb in ('store', 'vending'):
                    name, alias, at = ('TradingPost' if verb == 'store' else 'VendingMachine'), t[1], t.index('at')
                    pts, r = [world(f(t[at + 1]), f(t[at + 2]))], 0.0
                elif verb == 'place':
                    at = t.index('at')
                    name, alias = t[1], t[2]
                    pts, r = [world(f(t[at + 1]), f(t[at + 2]))], 0.0
                elif verb == 'scatter':
                    at = t.index('at')
                    name, alias = t[1], o.get('as', '-')
                    pts, r = [world(f(t[at + 1]), f(t[at + 2]))], f(o.get('r', '0'))
                elif verb == 'line':
                    fr, to = t.index('from'), t.index('to')
                    name, alias = t[1], '-'
                    pts, r = [world(f(t[fr + 1]), f(t[fr + 2])), world(f(t[to + 1]), f(t[to + 2]))], 0.0
                else:   # beside <Object> <alias> <of-alias> [d=]
                    name, alias = t[1], t[2]
                    of = [ob for ob in I.objs if ob.alias == t[3]]
                    if not of: continue
                    pts, r = [of[0].p], f(o.get('d', '2'))
                if name.lower() == 'none': continue
                for p in pts:
                    I.objs.append(Obj(name, alias if alias != '-' else '', p[0], p[1], r, top))
            elif verb in ('prop', 'beh') and len(t) > 2:
                kv = rest(line, 2)
                eq = kv.find('=')
                if eq > 0: cur(top).props.setdefault(t[1], {})[kv[:eq].lower()] = kv[eq + 1:]
            elif verb == 'note' and len(t) > 2:
                cur(top).notes[t[1]] = rest(line, 2).split('|')[0].strip()
            elif verb == 'loot' and len(t) > 2:
                cur(top).loot[t[1]] = rest(line, 2)
            elif re.match(r'^step\d?$', verb):
                parts = rest(line, 1).split('|')
                cur(top).steps.append((parts[0].strip().lower(), parts[1].strip() if len(parts) > 1 else '',
                                       parts[3].strip() if len(parts) > 3 else '', top))
        except (ValueError, IndexError):
            raise RecipeError("%s (%s): can't read it" % (where, line))
        except RecipeError as e:
            raise RecipeError("%s (%s): %s" % (where, line, e))
    return islands


def check_island(I, markers, machete_before):
    issues = []
    inside_marked = set(a for k, args, w in markers if k == 'inside' for a in args)
    names = {}   # a step target -> objects
    for ob in I.objs:
        if not ob.alias: continue
        pr = I.props.get(ob.alias, {})
        keys = [ob.alias, pr.get('obj.name', ''), pr.get('zone.id', ''), I.notes.get(ob.alias, ''), pr.get('note.title', ''),
                pr.get('name', '')]
        for k in keys:
            if k: names.setdefault(k.lower(), []).append(ob)
    for ob in I.objs:
        pr = I.props.get(ob.alias, {}) if ob.alias else {}
        for kind, rx in KIND_RULES:
            if rx.search(ob.name): ob.kinds.add(kind)
        if any(k.startswith('talk.') for k in pr): ob.kinds.add('islander')
        if any(k.startswith('creature.') for k in pr): ob.kinds.add('spawn')
        if any(k.split('.')[0] in ('if', 'on', 'else') for k in pr): ob.kinds.add('quest')
        if VINES.match(ob.name) or ob.name.lower().startswith('cavevine'): ob.kinds.clear()
    # the steps' targets, in order
    walk, machete_at = [], (-1 if machete_before else None)
    for n, (typ, target, text, where) in enumerate(I.steps):
        obs = names.get(target.lower(), []) if target else []
        for ob in obs: ob.kinds.add('quest')
        if machete_at is None:
            if 'machete' in target.lower(): machete_at = n
            elif typ == 'open' and any('machete' in I.loot.get(ob.alias, '').lower() for ob in obs): machete_at = n
        if obs: walk.append((n, obs[0], where))
    # gated caves: a cavegate, or machete vines at a mouth
    vines = [ob for ob in I.objs if VINES.match(ob.name)]
    for c in I.caves:
        if c.group and c.group in I.gates: c.gated = True
        if any(c.mouth_dist(v.p) < CLEAR for v in vines): c.gated = True

    # (11-09 w03: a placed den - BalboaCave_Bear, ~30 x 32 m, the hill caves' sk_cave - is a cave too: a rat marked inside
    # one, 10 m or less from its middle, is in it)
    dens = [ob for ob in I.objs if re.match(r'^BalboaCave_(?!Vines)', ob.name, re.I)]
    def in_cave(p):
        return any(c.dist(p) < 0 for c in I.caves) or any(math.hypot(p[0] - d.p[0], p[1] - d.p[1]) < 10.0 for d in dens)

    for c in I.caves:
        label = '%s %s (%s)' % (c.kind, c.group or '-', c.where)
        for ob in I.objs:
            if not ob.kinds: continue
            if ob.alias and ob.alias in inside_marked: continue
            d = c.dist(ob.p) - ob.r
            dm = c.mouth_dist(ob.p) - ob.r
            what = '%s %s %s (%s)' % ('/'.join(sorted(ob.kinds)), ob.name, ob.alias or '-', ob.where)
            if 'quest' in ob.kinds and d < 0:
                issues.append(('over/under', '%s passes over or under %s' % (label, what)))
            elif min(d, dm) < CLEAR:
                issues.append(('clearance', '%s is %.1f m from %s (keep %g m)' % (label, max(0.0, min(d, dm)), what, CLEAR)))
    for ob in I.objs:
        if ob.alias in inside_marked and not in_cave(ob.p):
            issues.append(('inside', '%s %s (%s) is marked inside a cave but is in none' % (ob.name, ob.alias, ob.where)))
    # 3: mouths on the walk line (its legs outside caves)
    legs = []
    for (n1, a, w1), (n2, b, w2) in zip(walk, walk[1:]):
        if a.p == b.p or in_cave(a.p) or in_cave(b.p) or a.alias in inside_marked or b.alias in inside_marked: continue
        legs.append((a, b, n2))
    for c in I.caves:
        for m in c.mouths:
            for a, b, n2 in legs:
                if seg_dist(m, a.p, b.p) < WALK:
                    issues.append(('walk line', '%s %s (%s): a mouth on the walk line from %s to %s (step %d)' % (
                        c.kind, c.group or '-', c.where, a.alias, b.alias, n2 + 1)))
                    break
    # 4: a gated cave holding a step's target before the machete step
    for c in (c for c in I.caves if c.gated):
        for n, ob, where in walk:
            if machete_at is not None and n > machete_at: continue
            if c.dist(ob.p) < 0 or c.mouth_dist(ob.p) < CLEAR:
                issues.append(('vine gate', '%s %s (%s) is gated with vines but step %d (%s, %s) is in it %s' % (
                    c.kind, c.group or '-', c.where, n + 1, ob.alias, where,
                    'before the machete step %d' % (machete_at + 1) if machete_at is not None and machete_at >= 0
                    else 'and no machete is given (--machete if an earlier island gives it)')))
    return issues


def selftest():
    """--selftest: the cave tool's plan against CaveTool.Plan's numbers (size 1: 3 tunnels for 26 m, the den 31.78 m
    long and 29.56 m wide from 0.6 m back over their end; size 0.7 at 12 degrees: 5 tunnels for 35.6 m)."""
    ok = True
    def chk(name, got, want, tol=0.02):
        nonlocal ok
        if abs(got - want) > tol: ok = False; print('  FAIL selftest %s: %.3f, not %.3f' % (name, got, want))
    k, end, room = st_layout((0.0, 0.0), (0.0, 26.0), 0.0, 1.0)
    chk('end z', end[1], 3 * ST_STEP); chk('room start', room[0][1], 3 * ST_STEP - 0.6); chk('room length', room[2], 31.78)
    chk('room half width', room[3], 14.778)
    chk('inside the den', box_dist((5.0, 50.0), *room), -9.778)
    chk('past the den', box_dist((0.0, 31.2 - 0.6 + 31.78 + 4.0), *room), 4.0)
    chk('beside the den', box_dist((14.778 + 3.0, 40.0), *room), 3.0)
    k, end, room = st_layout((0.0, 0.0), (0.0, -35.6), 12.0, 0.7)
    chk('uw end z', end[1], -5 * ST_STEP * 0.7 * math.cos(math.radians(12)))
    chk('uw den far wall', room[0][1] + room[1][1] * room[2], end[1] + 0.42 - 31.78 * 0.7)
    # (11-08) a dig shaft: one cave with the passage's mouth and the shaft's top as its two mouths
    isl = read_islands([(l, 'selftest', 'selftest') for l in
                        ('new', 'dig 0,0 10,0 slope=-10 as=g', 'dig 10,0 10,0.3,0 shaft r=1.6 as=g')])[0]
    chk('shaft: caves', len(isl.caves), 1); chk('shaft: mouths', len(isl.caves[0].mouths), 2)
    chk('shaft: top x', isl.caves[0].mouths[-1][0] - LAND_CENTRE[0], 10.0)
    # (11-09) a rat marked inside a placed den (BalboaCave_Bear) is in a cave; one 20 m off is not
    isl = read_islands([(l, 'selftest', 'selftest') for l in
                        ('new', 'place BalboaCave_Bear den at 0 0 h=5', 'place Creature_Rat rin at 2 3 h=1', 'place Creature_Rat rout at 20 0 h=1')])[0]
    ins = [t for r, t in check_island(isl, [('inside', ['rin', 'rout'], 'selftest')], True) if r == 'inside']
    chk('den: rats not in a cave', len(ins), 1); ok = ok and all('rout' in t for t in ins)
    print('cavecheck selftest: %s' % ('PASS' if ok else 'FAIL'))
    return 0 if ok else 1


def main(argv):
    if '--selftest' in argv: return selftest()
    machete = '--machete' in argv
    args = [a for a in argv if not a.startswith('--')]
    if not args:
        print(__doc__.split('\n\n')[0]); return 2
    path = args[0]
    recipes = os.path.join(ROOT, 'recipes')
    if not os.path.isfile(path):
        cand = os.path.join(recipes, path if path.endswith('.recipe') else path + '.recipe')
        if os.path.isfile(cand): path = cand
        else: print('cavecheck: no recipe ' + path); return 2
    ex = Expander(path, [os.path.dirname(os.path.abspath(path)), recipes])
    lines = []
    try:
        ex.expand(ex.read(path), {}, {}, lines, 0)
        islands = read_islands(lines)
    except RecipeError as e:
        print('cavecheck %s: ERROR %s' % (os.path.basename(path), e)); return 2
    machete = machete or any(k == 'machete' for k, a, w in ex.markers)
    bad, ncaves = 0, 0
    for I in islands:
        ncaves += len(I.caves)
        for rule, text in check_island(I, ex.markers, machete):
            bad += 1
            print('  FAIL %s: %s%s' % (rule, text, ' [' + I.name + ']' if I.name else ''))
    nobj = sum(len(I.objs) for I in islands)
    print('cavecheck %s: %s (%d island(s), %d cave(s), %d object(s)%s)' % (
        os.path.basename(path), 'FAIL %d' % bad if bad else 'PASS', len(islands), ncaves, nobj, ', machete given' if machete else ''))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
