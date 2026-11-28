# islandfile.py - read a Custom Islands .island file offline (IslandFile.cs: Read). Used by questlint.py and killspare.py.
#
#   from islandfile import load
#   isl = load(path)   # isl.name, isl.props (island settings), isl.objects [Obj(name, pos, rot, scale, props)],
#                      # isl.heights (list of rows, 0..1), isl.res, isl.size (x, y, z), isl.water, isl.elevation
# Format: "CISL", int32 version, then a zlib (deflate) body; strings are .NET BinaryReader strings (7-bit length, UTF-8).
import struct, zlib

MAGIC = 0x4C534943


class Obj(object):
    __slots__ = ("index", "name", "pos", "rot", "scale", "props")

    def __init__(self, index, name, pos, rot, scale):
        self.index, self.name, self.pos, self.rot, self.scale, self.props = index, name, pos, rot, scale, {}


class Island(object):
    pass


class _R(object):
    def __init__(self, data):
        self.d, self.p = data, 0

    def take(self, n):
        if self.p + n > len(self.d): raise EOFError("end of island body")
        b = self.d[self.p:self.p + n]; self.p += n
        return b

    def i32(self): return struct.unpack("<i", self.take(4))[0]
    def f32(self): return struct.unpack("<f", self.take(4))[0]
    def b(self): return self.take(1)[0] != 0
    def vec(self): return struct.unpack("<3f", self.take(12))

    def s(self):
        n, shift = 0, 0
        while True:
            c = self.take(1)[0]
            n |= (c & 0x7F) << shift
            if not c & 0x80: break
            shift += 7
        return self.take(n).decode("utf-8", "replace")

    def props(self):
        n = self.i32()
        if n < 0 or n > 10000: raise ValueError("bad property count %d" % n)
        out = {}
        for _ in range(n):
            k = self.s(); out[k] = self.s()
        return out


def load(path, heights=True):
    with open(path, "rb") as f:
        raw = f.read()
    magic, version = struct.unpack("<Ii", raw[:8])
    if magic != MAGIC: raise ValueError(path + " is not a .island file")
    body = zlib.decompressobj().decompress(raw[8:])
    r = _R(body)
    isl = Island()
    isl.path, isl.version = path, version
    isl.name = r.s()
    isl.water = r.f32()
    isl.size = r.vec()
    res = r.i32()
    isl.res = res
    if heights:
        hs = struct.unpack("<%dH" % (res * res), r.take(2 * res * res))
        isl.heights = [[hs[y * res + x] / 65535.0 for x in range(res)] for y in range(res)]
    else:
        r.take(2 * res * res); isl.heights = None
    count = r.i32()
    isl.objects = [Obj(i, r.s(), r.vec(), r.vec(), r.vec()) for i in range(count)]
    isl.props, isl.elevation, isl.style = {}, 0.0, ""
    if version >= 2 and r.b():
        ares, layers = r.i32(), r.i32()
        r.take(layers * ares * ares)
        if r.b(): r.take(ares * ares)
    if version >= 3:
        isl.elevation = r.f32()
        try: isl.style = r.s()
        except EOFError: pass
    if version >= 4:
        isl.props = r.props()
        n = r.i32()
        for _ in range(n):
            i = r.i32(); p = r.props()
            if 0 <= i < count: isl.objects[i].props = p
    return isl


def height_at(isl, x, z):
    """Terrain height (editor y) at island-local x, z (terrain corner at 0,0 like the editor), or None off the map."""
    if not isl.heights: return None
    sx, sy, sz = isl.size
    u, v = x / sx * (isl.res - 1), z / sz * (isl.res - 1)
    if u < 0 or v < 0 or u > isl.res - 1 or v > isl.res - 1: return None
    i, j = int(u), int(v)
    i1, j1 = min(i + 1, isl.res - 1), min(j + 1, isl.res - 1)
    fu, fv = u - i, v - j
    h = isl.heights
    a = h[j][i] * (1 - fu) + h[j][i1] * fu
    b = h[j1][i] * (1 - fu) + h[j1][i1] * fu
    return (a * (1 - fv) + b * fv) * sy


if __name__ == "__main__":
    import sys
    for p in sys.argv[1:]:
        isl = load(p, heights=False)
        print(isl.name, "v%d" % isl.version, len(isl.objects), "objects", len(isl.props), "props")
        for k in sorted(isl.props):
            if k.startswith("quest"): print("  ", k, "=", isl.props[k].replace("\n", " / ")[:300])
