using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's cave tool (ROADMAP PF-D2). The ground is one height map and can't hold a cave, so a cave is made of Raft's
	/// own cave pieces, as the story islands make theirs (drowned_labyrinth): drag from where the mouth is to be, into a hill
	/// or across flat ground, and a tunnel of Temperance's cave tunnels is laid end to end along the drag - level, or
	/// sloping down or up - with Balboa's bear den as a room at its far end (or an open far end: a tunnel through a hill). In
	/// the same step the ground is raised over the pieces into a hill (never lowered there), filled up under them so none
	/// hangs in the air, and an approach is levelled to the mouth, which is left open; one Ctrl+Z takes it all back. The
	/// ground in the rooms is cut away by CaveGround, and in a world CaveMountain covers the rock - as for any cave piece.
	/// The pieces are ordinary placed objects: they save, load, go into Test and multiplayer with the island.
	/// </summary>
	public static class CaveTool
	{
		public const string Tunnel = "TP_UnderwaterCaveTunnel_Straight", Room = "BalboaCave_Bear";

		// TP_UnderwaterCaveTunnel_Straight (raft_props.txt): its passage runs along local z at x 3.002, floor at -0.01; its
		// box ends (where two meet, as the labyrinth's do) at z 1.673 and -8.726; its rock 4.43 under the floor to 8.52 over
		// the pivot, 5.48 to each side of the passage.
		public const float Step = 10.399f, FaceMin = -8.726f, PassX = 3.002f, Floor = -0.01f;
		public const float ShellTop = 8.53f, ShellHalfWidth = 5.48f;
		// (its floor is a shallow trough: at -0.01 mid-piece, about 0.9 lower at its ends - measured 10-26. The pieces sit that
		// much higher than the line, so the floor at the mouth and at the den meets the ground and the den's floor level)
		public const float EndDip = 0.9f;
		// (the den's floor at its mouth is about 0.6 under the line it was measured on: it sits that much higher too)
		public const float RoomLift = 0.6f;
		// (and that much into the tunnel's end: the bear's front wall bows back from its mouth, so flush it left a slot each
		// side where the tunnel's end met it - a look sideways down from the doorway went out to the sky, 11-02)
		public const float RoomIn = 0.6f;
		// BalboaCave_Bear: its mouth at +z (box end 15.99), the passage there at x -1.072 with its floor 4.05 under the pivot
		// (the labyrinth: the bear's pivot 4.05 m over the tunnels' - their faces meet flush); its box -10.43..11.65 high,
		// x -15.85..13.71, z -15.79..15.99.
		static readonly Vector3 RoomMouth = new Vector3(-1.072f, -4.05f, 15.99f);
		static readonly Vector3 RoomMin = new Vector3(-15.85f, -10.43f, -15.79f), RoomMax = new Vector3(13.71f, 11.65f, 15.99f);

		public const float MaxSlope = 12f; // (CaveMountain keeps tunnels pitched up to about that)
		public const int MaxTunnels = 12;
		const float Cover = 1.2f;     // ground over the rock (m)
		const float SideSlope = 1.1f; // the hill's sides: metres down per metre out
		const float ApronLength = 8f, ApronHalf = 3.5f; // the level ground before the mouth
		const float CutSlope = 1.4f;  // the walls of the cut leading to the mouth: metres up per metre out

		/// <summary>The tool's settings (the Caves panel; a recipe's cave step sets its own).</summary>
		public static float Slope; // degrees: below 0 down into the ground, above 0 up
		/// <summary>The tunnel's size over Raft's own (PF-D2b): the pieces are laid scaled by it, and every measure of the
		/// layout, the hill, the cut and the den's wall with them. MinSize: the passage still about 3.6 m wide and 4 m high.</summary>
		public static float Size = 1f;
		public const float MinSize = 0.7f, MaxSize = 1.5f;
		public static bool EndRoom = true, Hill = true;

		public struct Piece
		{
			public string Name;
			public float Scale; // (over the piece's own: the layout's Size)
			public Vector3 Position;
			public Quaternion Rotation;
		}

		/// <summary>A cave laid out: its pieces, its mouth (the floor where it opens), its far end, its way in on the ground.</summary>
		public class Layout
		{
			public List<Piece> Pieces = new List<Piece>();
			public Vector3 Mouth, End, Dir, Flat;
			public float Slope, Length;
			public float Size = 1f;
			public int Tunnels;
			public bool Room;
			public Vector3 RoomPivot;
			public Quaternion RoomRotation;

			/// <summary>The floor of the passage this far along it (m, flat), on the tunnel's line.</summary>
			public float FloorAt(float s) { return Mouth.y + Mathf.Tan(Slope * Mathf.Deg2Rad) * Mathf.Clamp(s, 0f, Length); }

			/// <summary>The tunnel's rock to each side of the passage, and over its floor (m, at this size); the den's box and mouth.</summary>
			public float HalfWidth { get { return ShellHalfWidth * Size; } }
			public float Top { get { return (ShellTop - Floor + EndDip) * Size; } }
			public Vector3 RoomMin { get { return CaveTool.RoomMin * Size; } }
			public Vector3 RoomMax { get { return CaveTool.RoomMax * Size; } }
			public Vector3 RoomMouth { get { return CaveTool.RoomMouth * Size; } }
		}

		/// <summary>
		/// Lays a cave out: the mouth's floor at mouth, the tunnel towards toward (only its direction and flat distance count),
		/// as many tunnel pieces as fit (one at least - a den alone had no mouth of its own, 11-01 - MaxTunnels at most),
		/// sloping slope degrees, the bear's den at the far end when room; size times Raft's own size.
		/// </summary>
		public static Layout Plan(Vector3 mouth, Vector3 toward, float slope, bool room, float size = 1f)
		{
			var l = new Layout { Mouth = mouth, Room = room, Slope = Mathf.Clamp(slope, -MaxSlope, MaxSlope), Size = Mathf.Clamp(size, MinSize, MaxSize) };
			float k = l.Size, step = Step * k;
			Vector3 flat = toward - mouth; flat.y = 0f;
			float dist = flat.magnitude;
			l.Flat = dist > 0.01f ? flat / dist : Vector3.forward;
			float a = l.Slope * Mathf.Deg2Rad;
			l.Dir = (l.Flat * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)).normalized;
			int n = Mathf.RoundToInt(dist / (step * Mathf.Cos(a)));
			l.Tunnels = Mathf.Clamp(n, 1, MaxTunnels);
			Quaternion r = Quaternion.LookRotation(l.Dir, Vector3.up);
			Vector3 entry = new Vector3(PassX, Floor - EndDip, FaceMin) * k;
			for (int i = 0; i < l.Tunnels; i++)
				l.Pieces.Add(new Piece { Name = Tunnel, Scale = k, Rotation = r, Position = mouth + l.Dir * (i * step) - r * entry });
			l.End = mouth + l.Dir * (l.Tunnels * step);
			l.Length = l.Tunnels * step * Mathf.Cos(a);
			if (room)
			{
				// (the den's mouth over the tunnel's far end, its floor level with it; it faces back up the tunnel)
				l.RoomRotation = Quaternion.LookRotation(-l.Flat, Vector3.up);
				l.RoomPivot = l.End - l.RoomRotation * l.RoomMouth + Vector3.up * (RoomLift * k) - l.Flat * (RoomIn * k);
				l.Pieces.Add(new Piece { Name = Room, Scale = k, Rotation = l.RoomRotation, Position = l.RoomPivot });
			}
			return l;
		}

		#region The ground

		/// <summary>
		/// How high the ground is to be at a point (world), or NaN to leave it: over the rock the hill's top, falling away at
		/// SideSlope beside it; nothing before the mouth (its approach is levelled by Apron) or past an open far end.
		/// </summary>
		public static float HillAt(Layout l, Vector3 p)
		{
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat));
			float best = float.NaN;
			if (l.Tunnels > 0)
			{
				// (before the mouth, and past an open end, the tunnel's face is the hillside: the hill only beside it)
				bool beforeMouth = s < 0f, pastEnd = !l.Room && s > l.Length;
				if (!((beforeMouth || pastEnd) && Mathf.Abs(q) < l.HalfWidth + 0.5f))
				{
					float ds = s < 0f ? -s : s > l.Length ? s - l.Length : 0f, dq = Mathf.Max(0f, Mathf.Abs(q) - l.HalfWidth);
					float d = Mathf.Sqrt(ds * ds + dq * dq);
					float top = l.FloorAt(s) + l.Top + Cover;
					best = top - d * SideSlope;
					// (beside the way in, the cut's walls slope up from it: not straight up - they stood as two flat slabs)
					if (beforeMouth) best = Mathf.Min(best, l.Mouth.y + (Mathf.Abs(q) - l.HalfWidth - 0.5f) * CutSlope);
					else if (pastEnd) best = Mathf.Min(best, l.End.y + (Mathf.Abs(q) - l.HalfWidth - 0.5f) * CutSlope);
				}
			}
			if (l.Room)
			{
				Vector3 lp = Quaternion.Inverse(l.RoomRotation) * (p - l.RoomPivot);
				Vector3 rmin = l.RoomMin, rmax = l.RoomMax;
				float dx = Mathf.Max(0f, rmin.x - lp.x, lp.x - rmax.x), dz = Mathf.Max(0f, rmin.z - lp.z, lp.z - rmax.z);
				// (the den's own front - at its mouth - is under the tunnel's hill, or, with no tunnel, the face it opens in)
				bool front = l.Tunnels == 0 && lp.z > rmax.z - 0.5f && Mathf.Abs(lp.x - l.RoomMouth.x) < 7f * l.Size;
				if (!front)
				{
					float top = l.RoomPivot.y + rmax.y + Cover - Mathf.Sqrt(dx * dx + dz * dz) * SideSlope;
					if (float.IsNaN(best) || top > best) best = top;
				}
			}
			return best;
		}

		/// <summary>The approach to the mouth: the ground before it as high as its floor at the mouth, as wide as the threshold's
		/// rock and past its front so that sinks into it on every side, its outline wandering and its edges easing into the
		/// ground around over 5 m, rising gently further out (cut down where higher, filled where lower near the mouth), or NaN
		/// out of it. weight: how much of it to take. A straight-edged 7 m strip narrower than the threshold left that a slab
		/// standing on a hump with steep sides until 11-02.</summary>
		public static float ApronAt(Layout l, Vector3 p, out float weight)
		{
			weight = 0f;
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			float len = ApronLength * l.Size, half = ApronHalf * l.Size, fall = 5f;
			Portal d = PortalOf(l.Size, l.Slope);
			float wide = Mathf.Max(half, d.Half + 1.5f), longest = Mathf.Max(0.8f * len, 4f - d.SillFrom);
			if (s > 0.5f || s < -longest - 1.3f - fall || q > wide + 0.7f + fall) return float.NaN;
			float n = Mathf.PerlinNoise(p.x * 0.21f + 17.3f, p.z * 0.21f + 5.9f), n2 = Mathf.PerlinNoise(p.x * 0.09f + 3.1f, p.z * 0.09f + 29.7f);
			float flatHalf = wide + 1.4f * (n - 0.5f), flatLen = longest + 2.5f * (n2 - 0.5f);
			Func<float, float> smooth01 = x => { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); };
			// (narrow by the rock's face, so the hill stays beside it: cut back that wide, the rock stood alone like a boulder)
			float t = smooth01((d.SillFrom + 1f - s) / 4f), halfHere = Mathf.Lerp(half, flatHalf, t), fallHere = Mathf.Lerp(3f, fall, t);
			weight = smooth01((halfHere + fallHere - q) / fallHere) * smooth01((flatLen + fall + s) / fall);
			return l.Mouth.y - 0.05f + Mathf.Max(0f, -s - flatLen) * 0.2f + Mathf.Max(0f, q - halfHere) * 0.12f;
		}

		/// <summary>
		/// Shapes the ground for a cave laid out: the hill over it (hill), the ground under every piece filled up to its rock's
		/// underside so nothing hangs in the air, the mouth's approach. Returns the area changed (world min/max).
		/// </summary>
		public static void ShapeGround(Terrain terrain, Layout l, bool hill, out Vector3 min, out Vector3 max)
		{
			TerrainData data = terrain.terrainData;
			Vector3 o = terrain.transform.position, size = data.size;
			int res = data.heightmapResolution;
			Bounds b = Footprint(l);
			b.Expand(new Vector3(40f, 0f, 40f)); // (the hill's sides)
			min = b.min; max = b.max;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - o.x) / size.x * (res - 1)), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((b.max.x - o.x) / size.x * (res - 1)), 0, res - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - o.z) / size.z * (res - 1)), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((b.max.z - o.z) / size.z * (res - 1)), 0, res - 1);
			int w = x1 - x0 + 1, h = z1 - z0 + 1;
			if (w <= 0 || h <= 0) return;
			float[,] heights = data.GetHeights(x0, z0, w, h);
			Portal at = PortalOf(l.Size, l.Slope);
			Layout far = !l.Room && l.Tunnels > 0 ? FarEnd(l) : null;
			Portal farAt = PortalOf(l.Size, -l.Slope);
			for (int z = 0; z < h; z++)
				for (int x = 0; x < w; x++)
				{
					Vector3 p = new Vector3(o.x + (x0 + x) / (float)(res - 1) * size.x, 0f, o.z + (z0 + z) / (float)(res - 1) * size.z);
					float y = heights[z, x] * size.y + o.y, ny = y;
					float target = hill ? HillAt(l, p) : UnderAt(l, p);
					if (!float.IsNaN(target) && target > ny) ny = target;
					ny = Approach(l, at, p, ny);
					if (far != null) ny = Approach(far, farAt, p, ny);
					if (ny != y) heights[z, x] = Mathf.Clamp01((ny - o.y) / size.y);
				}
			data.SetHeights(x0, z0, heights);
		}

		/// <summary>The ground at a mouth (l: its way in): its approach levelled (Apron), a doorstep's length of it level with
		/// the floor before the frame, and none higher than the frame's top over it - the ground under the frame is taken out
		/// (CaveGround), and the frame's top is where the hill meets it.</summary>
		static float Approach(Layout l, Portal d, Vector3 p, float ny)
		{
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			float wgt, apron = ApronAt(l, p, out wgt);
			if (!float.IsNaN(apron))
			{
				if (q < d.Half + 0.5f && s >= d.SillFrom - 1f && s <= Mathf.Max(d.Wall, 0f) + 0.3f) wgt = 1f; // (under all the doorstep)
				bool near = s > -ApronLength * l.Size;
				if (wgt > 0f && (ny > apron || near)) ny = Mathf.Lerp(ny, apron, wgt);
			}
			if (q < d.Half + 0.3f && s > d.Wall && s <= d.CapTo + 0.3f) ny = Mathf.Min(ny, l.Mouth.y + d.CapAt(s) - 0.1f);
			return ny;
		}

		/// <summary>The ground about a mouth worn bare: sand and rock showing through the grass around the threshold, thinning
		/// out raggedly over about 3 m, so the threshold's rock doesn't end in a line on green (11-02). Marked painted by hand
		/// (mask), so the automatic paint leaves it.</summary>
		static void WearMouthGround(Terrain terrain, Layout l, float[,] mask)
		{
			if (l.Tunnels <= 0) return;
			WearAt(terrain, l, PortalOf(l.Size, l.Slope), mask);
			if (!l.Room) WearAt(terrain, FarEnd(l), PortalOf(l.Size, -l.Slope), mask);
		}

		static void WearAt(Terrain terrain, Layout l, Portal d, float[,] mask)
		{
			TerrainData data = terrain.terrainData;
			int layers = data.alphamapLayers, res = data.alphamapWidth;
			if (layers < TerrainPainter.LayerCount) return;
			Vector3 across = Vector3.Cross(Vector3.up, l.Flat), o = terrain.transform.position;
			float half = d.Half + 0.5f, front = d.SillFrom - 1.8f, reach = 4.5f;
			Vector3 min = l.Mouth, max = l.Mouth;
			foreach (float s in new[] { front - reach, d.Wall + 0.5f })
				foreach (float q in new[] { -half - reach, half + reach })
				{
					Vector3 c = l.Mouth + l.Flat * s + across * q;
					min = Vector3.Min(min, c); max = Vector3.Max(max, c);
				}
			RectInt area;
			if (!TerrainPainter.WorldToAlphamapRect(terrain, min, max, out area)) return;
			float[,,] maps = data.GetAlphamaps(area.x, area.y, area.width, area.height);
			Func<float, float> smooth01 = x => { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); };
			for (int z = 0; z < area.height; z++)
				for (int x = 0; x < area.width; x++)
				{
					Vector3 p = new Vector3(o.x + (area.x + x + 0.5f) / res * data.size.x, 0f, o.z + (area.y + z + 0.5f) / res * data.size.z);
					Vector3 rel = p - l.Mouth; rel.y = 0f;
					float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, across));
					if (s > d.Wall + 0.5f) continue;
					// (how far outside the threshold, its outline wandering)
					float n = Mathf.PerlinNoise(p.x * 0.35f + 7.7f, p.z * 0.35f + 1.3f), n2 = Mathf.PerlinNoise(p.x * 1.1f + 19f, p.z * 1.1f + 43f);
					float outside = Mathf.Max(q - half, front - s) - 2.4f * (n - 0.5f);
					float wear = 0.8f * (1f - smooth01(outside / 3.2f)) * (0.6f + 0.4f * n2);
					if (wear < 0.02f) continue;
					for (int k = 0; k < layers; k++) maps[z, x, k] *= 1f - wear;
					maps[z, x, TerrainPainter.Sand] += wear * 0.55f;
					maps[z, x, TerrainPainter.Rock] += wear * 0.45f;
					if (mask != null && wear > 0.1f && area.y + z < mask.GetLength(0) && area.x + x < mask.GetLength(1)) mask[area.y + z, area.x + x] = 1f;
				}
			data.SetAlphamaps(area.x, area.y, maps);
		}

		/// <summary>Without a hill: the ground under the rock up to its underside (NaN: not under it), so no piece hangs in the air.</summary>
		static float UnderAt(Layout l, Vector3 p)
		{
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			float best = float.NaN;
			if (l.Tunnels > 0 && s >= 0f && s <= l.Length && q < l.HalfWidth - 0.5f) best = l.FloorAt(s) - 1.5f * l.Size;
			if (l.Room)
			{
				Vector3 lp = Quaternion.Inverse(l.RoomRotation) * (p - l.RoomPivot);
				Vector3 rmin = l.RoomMin, rmax = l.RoomMax;
				if (lp.x > rmin.x + 1f && lp.x < rmax.x - 1f && lp.z > rmin.z + 1f && lp.z < rmax.z - 1f)
				{
					float t = l.RoomPivot.y + l.RoomMouth.y - 2f * l.Size;
					if (float.IsNaN(best) || t > best) best = t;
				}
			}
			return best;
		}

		/// <summary>The ground a cave laid out takes up (world, flat; y: its floor to its top).</summary>
		public static Bounds Footprint(Layout l)
		{
			var b = new Bounds(l.Mouth, Vector3.zero);
			Vector3 side = Vector3.Cross(Vector3.up, l.Flat) * l.HalfWidth;
			Vector3 rmin = l.RoomMin, rmax = l.RoomMax;
			foreach (Vector3 c in new[] { l.Mouth, l.End })
			{
				b.Encapsulate(c + side + Vector3.up * l.Top); b.Encapsulate(c - side - Vector3.up * 4.5f * l.Size);
			}
			if (l.Room)
				for (int i = 0; i < 8; i++)
					b.Encapsulate(l.RoomPivot + l.RoomRotation * new Vector3((i & 1) == 0 ? rmin.x : rmax.x, (i & 2) == 0 ? rmin.y : rmax.y, (i & 4) == 0 ? rmin.z : rmax.z));
			return b;
		}

		#endregion

		#region The den's front wall

		/// <summary>Name of the wall a den gets around the tunnel's end (a child of the den).</summary>
		public const string PlugName = "CavePlug";
		// (the wall's rock texture over this many metres: at 6 m its fine cracks read as a flat paved wall - 11-03)
		const float PlugTile = 20f;
		/// <summary>How far a lone den's wall (PlugOpening) bulges in or out at most, in metres: rock relief, not a flat board.</summary>
		public const float PlugRelief = 0.7f;
		/// <summary>The rock a lone den's wall is drawn with (the den's own texture is an atlas of colour patches).</summary>
		public const string WallRock = "BigRock_1";
		const float PlugIn = 0.8f, PlugBelow = 3f, HoleHalf = 2.6f, HoleBelow = 1f, HoleUp = 5.6f;

		/// <summary>
		/// The bear den's mouth opens far wider than a tunnel: in Balboa its cliff closes the rest, here the hill does from
		/// outside only (the ground is not seen from below), so from inside the den you looked out past the tunnel's end
		/// through the hill. A den laid at a tunnel's end (ObjectProps.CavePlug) gets a rock wall across its mouth, just inside
		/// the tunnel's last piece, with a hole the size of the passage (its edges hidden in the tunnel's rock). In the editor
		/// and in a world; made again when called again. size: the den's size over Raft's own (the cave tool's Size, kept as
		/// its ObjectProps.CavePlug).
		/// </summary>
		public static void Plug(GameObject den, float size = 1f)
		{
			if (den == null) return;
			Transform old = den.transform.Find(PlugName);
			if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
			Renderer look = den.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterial != null)
				.OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).FirstOrDefault();
			if (look == null) return;
			var go = new GameObject(PlugName);
			go.layer = look.gameObject.layer;
			go.transform.SetParent(den.transform, false);
			Vector3 ls = den.transform.lossyScale;
			go.transform.localScale = new Vector3(size / Mathf.Max(0.001f, ls.x), size / Mathf.Max(0.001f, ls.y), size / Mathf.Max(0.001f, ls.z));
			// (in the den's frame at the size the layout uses: its mouth at +z, the tunnel past it)
			float z = RoomMax.z + PlugIn, mx = RoomMouth.x, my = RoomMouth.y;
			// (across the den's whole mouth and up past its roof: a metre under it the sky showed over the wall - 11-03)
			float x0 = RoomMin.x - 0.5f, x1 = RoomMax.x + 0.5f;
			float y0 = my - PlugBelow, y1 = RoomMax.y + 0.5f;
			float hx0 = mx - HoleHalf, hx1 = mx + HoleHalf, hy0 = my - HoleBelow, hy1 = my + HoleUp;
			var verts = new List<Vector3>();
			var tris = new List<int>();
			Action<float, float, float, float> quad = (ax, ay, bx, by) =>
			{
				// (each side its own corners: on shared ones the two sides' normals cancelled out and the wall drew black - 11-03)
				int i = verts.Count;
				verts.Add(new Vector3(ax, ay, z)); verts.Add(new Vector3(bx, ay, z)); verts.Add(new Vector3(bx, by, z)); verts.Add(new Vector3(ax, by, z));
				verts.Add(new Vector3(ax, ay, z)); verts.Add(new Vector3(bx, ay, z)); verts.Add(new Vector3(bx, by, z)); verts.Add(new Vector3(ax, by, z));
				tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 }); // (facing -z: into the den)
				tris.AddRange(new[] { i + 4, i + 5, i + 6, i + 4, i + 6, i + 7 }); // (and the other way)
			};
			quad(x0, y0, x1, hy0);  // under the hole
			quad(x0, hy1, x1, y1);  // over it
			quad(x0, hy0, hx0, hy1); // beside it
			quad(hx1, hy0, x1, hy1);
			var mesh = new Mesh { name = PlugName };
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.SetUVs(0, verts.Select(v => new Vector2(v.x / PlugTile, v.y / PlugTile)).ToList());
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = look.sharedMaterial;
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
		}

		/// <summary>
		/// Plug's wall is a rectangle up past the den's roof and out past its sides: a small den under a low hill (size 0.7, no
		/// hill - Shelter Atoll's sea stack) had it standing out of the stack's top as a dark board (11-05). In a world, once the
		/// island and its hill are up (their rock answers rays only then), the wall is cut to the rock: every 0.5 m cell with
		/// rock - the den, the tunnel, the hill, the ground - over it, under it and on both sides, and every cell of the den's own
		/// opening (its mouth's outline, 2 m in), each with one cell more round it for an edge in the rock; nothing else - no
		/// board out past the den's shell into the sky or the sea.
		/// </summary>
		/// <summary>The mesh name of a den's front wall cut by PlugClip (CICaveInside checks only those).</summary>
		public const string ClippedMesh = "CavePlugClipped";

		public static IEnumerator PlugClipLater(GameObject den)
		{
			for (float t = 0f; den != null && !den.activeInHierarchy && t < 60f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			if (den == null || !den.activeInHierarchy) yield break;
			// (the hill over the caves is built a second after the island: wait for it, it is what covers the wall's top)
			CaveMountain m = den.transform.parent != null ? den.transform.parent.GetComponent<CaveMountain>() : null;
			for (float t = 0f; m != null && m.transform.Find(CaveMountain.SkinName) == null && t < 15f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			yield return null;
			if (den == null) yield break;
			try { PlugClip(den); }
			catch (Exception x) { Debug.LogWarning("[CUSTOM ISLANDS] Cutting a den's front wall to its rock failed: " + x.Message); }
		}

		/// <summary>The rock round a den that may cover its front wall: cave pieces, dug tunnels, the hill over them, the ground.</summary>
		static Collider[] PlugRock(Transform den, Bounds near)
		{
			Transform island = den.parent != null ? den.parent : den;
			return island.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger && c.name != PlugName && c.bounds.Intersects(near)
				&& (c is TerrainCollider || c.gameObject.layer == IslandSpawner.TerrainLayer || c.name == CaveMountain.SkinName || c.GetComponentInParent<DigTunnel>() != null
					|| c.GetComponentsInParent<Transform>(true).Any(t => { PropInfo p = RaftProps.Get(t.name.Replace("(Clone)", "").Trim()); return p != null && p.IsCave; }))).ToArray();
		}

		static bool PlugRockHit(Collider[] rock, Vector3 from, Vector3 dir, float reach)
		{
			var ray = new Ray(from, dir);
			RaycastHit h;
			foreach (Collider c in rock) if (c.Raycast(ray, out h, reach)) return true;
			return false;
		}

		static Collider[] PlugDenRock(GameObject den)
		{
			return den.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger && c.name != PlugName).ToArray();
		}

		/// <summary>A point of the wall (its own x, y) in the den's opening: 2 m inside its mouth, the den's rock over it, under it
		/// and on both sides (as PlugOpening's cells).</summary>
		static bool PlugInOpening(Transform plug, Collider[] own, float x, float y, float reach)
		{
			Vector3 w = plug.TransformPoint(new Vector3(x, y, RoomMax.z - 2f));
			return PlugRockHit(own, w, plug.up, reach) && PlugRockHit(own, w, -plug.up, reach) && PlugRockHit(own, w, plug.right, reach) && PlugRockHit(own, w, -plug.right, reach);
		}

		/// <summary>Cuts a den's front wall (Plug) to the rock round it - see PlugClipLater. Returns the cells kept, -1: no wall.</summary>
		public static int PlugClip(GameObject den)
		{
			Transform plug = den != null ? den.transform.Find(PlugName) : null;
			MeshFilter mf = plug != null ? plug.GetComponent<MeshFilter>() : null;
			if (mf == null || mf.sharedMesh == null) return -1;
			float size = Mathf.Max(0.1f, plug.lossyScale.x), cell = 0.5f / size, reach = 40f * size;
			Bounds near = mf.GetComponent<Renderer>() != null ? mf.GetComponent<Renderer>().bounds : new Bounds(plug.position, Vector3.one * 20f);
			near.Expand(2f * reach);
			Physics.SyncTransforms();
			Collider[] rock = PlugRock(den.transform, near);
			float z = RoomMax.z + PlugIn, mx = RoomMouth.x, my = RoomMouth.y;
			float x0 = RoomMin.x - 0.5f, x1 = RoomMax.x + 0.5f, y0 = my - PlugBelow, y1 = RoomMax.y + 0.5f;
			float hx0 = mx - HoleHalf, hx1 = mx + HoleHalf, hy0 = my - HoleBelow, hy1 = my + HoleUp;
			int nx = Mathf.CeilToInt((x1 - x0) / cell), ny = Mathf.CeilToInt((y1 - y0) / cell);
			Vector3 up = plug.up, right = plug.right;
			var inside = new bool[nx, ny];
			var roofed = new bool[nx, ny];
			var opening = new bool[nx, ny];
			Collider[] own = PlugDenRock(den);
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < ny; k++)
					{
						Vector3 w = plug.TransformPoint(new Vector3(x0 + (i + 0.5f) * cell, y0 + (k + 0.5f) * cell, z));
						roofed[i, k] = PlugRockHit(rock, w, up, reach);
						inside[i, k] = roofed[i, k] && PlugRockHit(rock, w, -up, reach) && PlugRockHit(rock, w, right, reach) && PlugRockHit(rock, w, -right, reach);
						opening[i, k] = PlugInOpening(plug, own, x0 + (i + 0.5f) * cell, y0 + (k + 0.5f) * cell, reach);
					}
			}
			finally { Physics.queriesHitBackfaces = backs; }
			var verts = new List<Vector3>();
			var tris = new List<int>();
			int kept = 0;
			for (int k = 0; k < ny; k++)
			{
				float ya = y0 + k * cell, yb = Mathf.Min(y1, ya + cell);
				int run = -1;
				for (int i = 0; i <= nx; i++)
				{
					bool on = false;
					if (i < nx)
					{
						// (the den's own opening - its mouth's outline, where the wall closes it - and the rock round the tunnel's end
						// under cover; each with one cell more round it, its edge in the rock)
						for (int di = -1; di <= 1 && !on; di++)
							for (int dk = -1; dk <= 1 && !on; dk++)
							{
								int a = i + di, b = k + dk;
								if (a >= 0 && b >= 0 && a < nx && b < ny && (opening[a, b] || roofed[i, k] && inside[a, b])) on = true;
							}
						float xa = x0 + i * cell, xb = xa + cell;
						if (on && xb > hx0 && xa < hx1 && yb > hy0 && ya < hy1) on = false; // (the passage's hole)
					}
					if (on) kept++;
					if (on && run < 0) run = i;
					if (!on && run >= 0)
					{
						float ax = x0 + run * cell, bx = Mathf.Min(x1, x0 + i * cell);
						int n = verts.Count;
						// (each side its own corners, as Plug's: shared ones drew the wall black - 11-03)
						verts.Add(new Vector3(ax, ya, z)); verts.Add(new Vector3(bx, ya, z)); verts.Add(new Vector3(bx, yb, z)); verts.Add(new Vector3(ax, yb, z));
						verts.Add(new Vector3(ax, ya, z)); verts.Add(new Vector3(bx, ya, z)); verts.Add(new Vector3(bx, yb, z)); verts.Add(new Vector3(ax, yb, z));
						tris.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2, n + 4, n + 5, n + 6, n + 4, n + 6, n + 7 });
						run = -1;
					}
				}
			}
			var mesh = new Mesh { name = ClippedMesh };
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.SetUVs(0, verts.Select(v => new Vector2(v.x / PlugTile, v.y / PlugTile)).ToList());
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			mf.sharedMesh = mesh;
			MeshCollider mc = plug.GetComponent<MeshCollider>();
			if (mc != null) mc.sharedMesh = tris.Count > 0 ? mesh : null;
			plug.gameObject.SetActive(tris.Count > 0);
			Debug.Log("[CUSTOM ISLANDS] Den's front wall cut to its rock: " + kept + " of " + (nx * ny) + " cells kept");
			return kept;
		}

		/// <summary>Tests: points of a den's front wall with open sky over them and outside the den's opening (none: the wall is in
		/// the rock; -1: no wall).</summary>
		public static int PlugInOpen(Transform den)
		{
			Transform plug = den != null ? den.Find(PlugName) : null;
			MeshFilter mf = plug != null ? plug.GetComponent<MeshFilter>() : null;
			if (mf == null || mf.sharedMesh == null || !plug.gameObject.activeInHierarchy
				|| mf.sharedMesh.name != ClippedMesh) return -1;
			// (only a wall PlugClip cut: a lone den's wall with its passage hole - PlugOpening - is built to stand in the den's open
			// mouth, and its mesh is laid out otherwise - 11-05)
			float size = Mathf.Max(0.1f, plug.lossyScale.x), reach = 40f * size;
			Bounds near = plug.GetComponent<Renderer>().bounds;
			near.Expand(2f * reach);
			Physics.SyncTransforms();
			Collider[] rock = PlugRock(den, near), own = PlugDenRock(den.gameObject);
			float step = 0.5f / size;
			Vector3[] v = mf.sharedMesh.vertices;
			int open = 0;
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				// (each strip's points every 0.5 m along its middle)
				for (int s = 0; s + 3 < v.Length; s += 8)
				{
					Vector3 a = v[s], b = v[s + 2];
					float y = (a.y + b.y) * 0.5f;
					int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(b.x - a.x) * size / 0.5f));
					for (int j = 0; j < steps; j++)
					{
						float x = Mathf.Lerp(a.x, b.x, (j + 0.5f) / steps);
						if (PlugRockHit(rock, plug.TransformPoint(new Vector3(x, y, a.z)), plug.up, reach)) continue;
						bool mouth = false;
						for (int di = -1; di <= 1 && !mouth; di++)
							for (int dk = -1; dk <= 1 && !mouth; dk++) mouth = PlugInOpening(plug, own, x + di * step, y + dk * step, reach);
						if (!mouth) open++;
					}
				}
			}
			finally { Physics.queriesHitBackfaces = backs; }
			return open;
		}

		/// <summary>
		/// A bear den on its own (ar_cave: no tunnel at its mouth) - RK15. Its mouth is open its full width, and the hill over it
		/// (CaveMountain) stops at the open end, so from outside over the hill you looked into the den. Plug's rectangle stood out
		/// past the den's shell there as a dark board, so this wall is cut to the shell's own opening: 2 m in from the mouth,
		/// every 0.5 m cell with the den's rock over it, under it and on both sides of it (one cell more all round, its edge in
		/// the rock), with the same passage-sized hole as Plug's. World only (IslandSpawner, before the tint, so it is tinted).
		/// </summary>
		/// <summary>PlugOpening once the den is active and in place (an island is built inactive), tinted as the den.</summary>
		public static IEnumerator PlugOpeningLater(GameObject den, IDictionary<string, string> props)
		{
			for (float t = 0f; den != null && !den.activeInHierarchy && t < 60f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			yield return null;
			if (den == null || !den.activeInHierarchy) yield break;
			try
			{
				PlugOpening(den, CaveMountain.OverMeasured(den.transform, Room).x);
				TintWall(den, props);
			}
			catch (Exception x) { Debug.LogWarning("[CUSTOM ISLANDS] Walling a den's mouth failed: " + x.Message); }
		}

		/// <summary>
		/// Colours a lone den's wall (PlugOpening) as the den's shell looks: Raft's big rock read as a grey-blue patch beside the
		/// khaki shell of The Abyss's lava tube (11-05), though the two textures' mean colours are alike - the two shaders light
		/// them differently. So both are drawn lit, as the game draws them (with the den's tint, when the den has one), and the
		/// wall's rock texture is multiplied by the shell's lit colour over the wall's (LitColour). Every den. The factor used,
		/// or white when the colours could not be read.
		/// </summary>
		public static Color TintWall(GameObject den, IDictionary<string, string> props)
		{
			Transform plug = den != null ? den.transform.Find(PlugName) : null;
			MeshRenderer wr = plug != null ? plug.GetComponent<MeshRenderer>() : null;
			if (wr == null) return Color.white;
			Color factor = Color.white, shell, wall = Color.white, after = Color.white;
			bool matched = LitColour(DenShell(den), out shell) && LitColour(new Renderer[] { wr }, out wall);
			if (matched)
			{
				Func<float, float, float> ratio = (s, r) => Mathf.Clamp(s / Mathf.Max(0.02f, r), 0.1f, 4f);
				factor = new Color(ratio(shell.r, wall.r), ratio(shell.g, wall.g), ratio(shell.b, wall.b), 1f);
			}
			if (factor != Color.white) CaveMountain.TintRockTextures(wr, factor);
			bool reread = matched && LitColour(new Renderer[] { wr }, out after);
			Debug.Log("[CUSTOM ISLANDS] Den's wall coloured as its shell: " + (matched ? "shell " + shell.ToString("0.00") + ", wall " + wall.ToString("0.00") : "colours not read")
				+ ", factor " + factor.ToString("0.00") + (reread ? ", wall now " + after.ToString("0.00") : ""));
			return factor;
		}

		/// <summary>A den's own renderers: all but its wall (PlugName).</summary>
		public static Renderer[] DenShell(GameObject den)
		{
			return den == null ? new Renderer[0] : den.GetComponentsInChildren<MeshRenderer>(true)
				.Where(r => r.sharedMaterial != null && r.enabled && !r.transform.name.StartsWith(PlugName)).Cast<Renderer>().ToArray();
		}

		static readonly Color Key = new Color(1f, 0f, 1f, 1f);

		/// <summary>A material's colour texture: _MainTex, else its shader's first texture that is not a normal, mask or such map.</summary>
		internal static Texture ColourTexture(Material m, out string prop)
		{
			prop = null;
			if (m == null) return null;
			if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) { prop = "_MainTex"; return m.GetTexture(prop); }
			Shader sh = m.shader;
			for (int i = 0; i < sh.GetPropertyCount(); i++)
			{
				if (sh.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
				string tp = sh.GetPropertyName(i), low = tp.ToLowerInvariant();
				if (low.Contains("normal") || low.Contains("bump") || low.Contains("mask") || low.Contains("noise") || low.Contains("height")
					|| low.Contains("occlusion") || low.Contains("metal") || low.Contains("smooth") || low.Contains("emission")) continue;
				if (m.GetTexture(tp) != null) { prop = tp; return m.GetTexture(tp); }
			}
			return null;
		}

		/// <summary>
		/// The colour some renderers show as the game draws them: lit by the scene's lights, with their own shaders, property
		/// blocks (a tint) and textures. They alone are drawn - moved for a moment to a layer nothing else uses, for a camera
		/// that sees only that layer - from four sides and above into a small picture without fog, whose drawn pixels are
		/// averaged. The den's atlas texture and mesh can't be read in the game, so drawing is how its look is measured.
		/// </summary>
		public static bool LitColour(Renderer[] rs, out Color colour)
		{
			colour = Color.white;
			rs = rs != null ? rs.Where(r => r != null).ToArray() : new Renderer[0];
			if (rs.Length == 0) return false;
			int layer = -1;
			for (int i = 31; i >= 8 && layer < 0; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) layer = i;
			if (layer < 0) { Debug.Log("[CUSTOM ISLANDS] Lit colour: no free layer"); return false; }
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			float ext = Mathf.Max(0.5f, b.extents.magnitude);
			const int size = 96;
			RenderTexture rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			RenderTexture prev = RenderTexture.active;
			var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
			var layers = rs.Select(r => r.gameObject.layer).ToArray();
			bool fog = RenderSettings.fog;
			var go = new GameObject("CI lit colour");
			try
			{
				Camera cam = go.AddComponent<Camera>();
				cam.enabled = false;
				cam.orthographic = true;
				cam.orthographicSize = ext;
				cam.nearClipPlane = 0.01f;
				cam.farClipPlane = ext * 4f;
				cam.clearFlags = CameraClearFlags.SolidColor;
				cam.backgroundColor = Key;
				cam.cullingMask = 1 << layer;
				cam.allowHDR = false;
				cam.allowMSAA = false;
				cam.targetTexture = rt;
				foreach (Renderer r in rs) r.gameObject.layer = layer;
				RenderSettings.fog = false;
				Color sum = Color.clear;
				int n = 0;
				foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.down })
				{
					go.transform.SetPositionAndRotation(b.center - dir * ext * 2f, Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up));
					cam.Render();
					RenderTexture.active = rt;
					read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
					read.Apply();
					foreach (Color c in read.GetPixels())
					{
						if (c.r > 0.9f && c.g < 0.1f && c.b > 0.9f) continue; // (the key: nothing drawn there)
						sum += c; n++;
					}
				}
				if (n < 50) { Debug.Log("[CUSTOM ISLANDS] Lit colour: only " + n + " pixels drawn"); return false; }
				colour = sum / n;
				colour.a = 1f;
				if (colour.maxColorComponent < 0.01f) { Debug.Log("[CUSTOM ISLANDS] Lit colour: drawn black"); colour = Color.white; return false; }
				return true;
			}
			catch (Exception x) { Debug.LogWarning("[CUSTOM ISLANDS] Lit colour could not be read: " + x.Message); return false; }
			finally
			{
				for (int i = 0; i < rs.Length; i++) if (rs[i] != null) rs[i].gameObject.layer = layers[i];
				RenderSettings.fog = fog;
				RenderTexture.active = prev;
				RenderTexture.ReleaseTemporary(rt);
				UnityEngine.Object.Destroy(read);
				UnityEngine.Object.Destroy(go);
			}
		}

		public static void PlugOpening(GameObject den, float size = 1f)
		{
			if (den == null) return;
			Transform old = den.transform.Find(PlugName);
			if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
			Renderer look = den.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterial != null)
				.OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).FirstOrDefault();
			Collider[] rock = den.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToArray();
			if (look == null || rock.Length == 0) return;
			var go = new GameObject(PlugName);
			go.layer = look.gameObject.layer;
			go.transform.SetParent(den.transform, false);
			Vector3 ls = den.transform.lossyScale;
			go.transform.localScale = new Vector3(size / Mathf.Max(0.001f, ls.x), size / Mathf.Max(0.001f, ls.y), size / Mathf.Max(0.001f, ls.z));
			Physics.SyncTransforms();
			const float cell = 0.5f, inFrom = 2f;
			float z = RoomMax.z - inFrom, mx = RoomMouth.x, my = RoomMouth.y;
			float x0 = RoomMin.x, y0 = my - PlugBelow;
			int nx = Mathf.CeilToInt((RoomMax.x - x0) / cell), ny = Mathf.CeilToInt((RoomMax.y - y0) / cell);
			float reach = 40f * Mathf.Max(0.1f, size);
			Vector3 up = den.transform.up, right = den.transform.right;
			Func<Vector3, Vector3, bool> hits = (from, dir) =>
			{
				var ray = new Ray(from, dir);
				RaycastHit h;
				foreach (Collider c in rock) if (c.Raycast(ray, out h, reach)) return true;
				return false;
			};
			var inside = new bool[nx, ny];
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < ny; k++)
					{
						Vector3 w = go.transform.TransformPoint(new Vector3(x0 + (i + 0.5f) * cell, y0 + (k + 0.5f) * cell, z));
						inside[i, k] = hits(w, up) && hits(w, -up) && hits(w, right) && hits(w, -right);
					}
			}
			finally { Physics.queriesHitBackfaces = backs; }
			// (one cell more all round: the wall's edge goes into the rock, no slit of sky along it)
			var keep = new bool[nx, ny];
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < ny; k++)
					for (int di = -1; di <= 1 && !keep[i, k]; di++)
						for (int dk = -1; dk <= 1 && !keep[i, k]; dk++)
						{
							int a = i + di, b = k + dk;
							if (a >= 0 && b >= 0 && a < nx && b < ny && inside[a, b]) keep[i, k] = true;
						}
			float hx0 = mx - HoleHalf, hx1 = mx + HoleHalf, hy0 = my - HoleBelow, hy1 = my + HoleUp;
			// (rock relief: a flat board with the den's texture at 20 m drew as a smooth marbled blue-white sheet beside
			// The Abyss's acid path, 11-05. Each corner is pushed in or out by coarse noise and every triangle has its own
			// corners, so the wall reads as faceted rock like the den's own walls)
			Func<float, float, float> depth = (px, py) => PlugRelief * (2f * Mathf.PerlinNoise(px / 1.6f + 17.3f, py / 1.6f + 5.1f) - 1f)
				+ 0.35f * PlugRelief * (2f * Mathf.PerlinNoise(px / 0.7f + 3.7f, py / 0.7f + 41.9f) - 1f);
			var verts = new List<Vector3>();
			var norms = new List<Vector3>();
			var tris = new List<int>();
			// (each side's corners shared by the cells that meet there, with the relief's own smooth normal: every triangle with
			// its own corners and flat normal drew the wall in 45-degree halves of orange and black - the rock shader (Mountains)
			// blends its rock and sand by the normal - Shelter Atoll's ammo cave from the sea, 11-06)
			var corner = new Dictionary<int, int>[] { new Dictionary<int, int>(), new Dictionary<int, int>() };
			Func<int, int, int, int> at = (side, ci, ck) =>
			{
				int key = ci * (ny + 2) + ck, n;
				if (corner[side].TryGetValue(key, out n)) return n;
				float px = x0 + ci * cell, py = y0 + ck * cell, e = cell;
				float fx = (depth(px + e, py) - depth(px - e, py)) / (2f * e), fy = (depth(px, py + e) - depth(px, py - e)) / (2f * e);
				n = verts.Count;
				verts.Add(new Vector3(px, py, z + depth(px, py)));
				norms.Add(side == 0 ? new Vector3(fx, fy, -1f).normalized : new Vector3(-fx, -fy, 1f).normalized); // (0: facing -z, into the den)
				corner[side][key] = n;
				return n;
			};
			int cells = 0;
			for (int k = 0; k < ny; k++)
			{
				float ya = y0 + k * cell, yb = ya + cell;
				for (int i = 0; i < nx; i++)
				{
					if (!keep[i, k]) continue;
					float xa = x0 + i * cell, xb = xa + cell;
					if (xb > hx0 && xa < hx1 && yb > hy0 && ya < hy1) continue; // (the passage's hole)
					cells++;
					// (both sides, each with its own corners: on shared ones the two sides' normals cancelled out and the wall
					// drew as a flat black board - seen from inside The Abyss's lava tube, 11-05; Plug had the same fault, TN1)
					tris.AddRange(new[] { at(0, i, k), at(0, i + 1, k + 1), at(0, i + 1, k), at(0, i, k), at(0, i, k + 1), at(0, i + 1, k + 1) });
					tris.AddRange(new[] { at(1, i, k), at(1, i + 1, k), at(1, i + 1, k + 1), at(1, i, k), at(1, i + 1, k + 1), at(1, i, k + 1) });
				}
			}
			if (tris.Count == 0) { UnityEngine.Object.DestroyImmediate(go); return; }
			var mesh = new Mesh { name = PlugName };
			if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			// (the den's texture is an atlas of colour patches: the wall's own UVs at 20 m showed all of it - blue, white and
			// orange - beside The Abyss's acid path, 11-05. The wall takes the den's colour from round its mouth instead)
			// The den's mesh is not readable in the game, so it is Raft's big rock instead (as the cave mountain's skin), at 14 m
			// like the skin; only without it, one patch of the den's own texture (readable mesh) or the old 20 m tiling.
			GameObject bigRock = PlaceableCatalog.Get(WallRock);
			Renderer rockLook = bigRock != null ? bigRock.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial != null) : null;
			Vector2 swatch = Vector2.zero;
			bool atlas = rockLook == null && MouthSwatch(look, go.transform.TransformPoint(new Vector3(mx, my + HoleUp + 1f, RoomMax.z)), out swatch);
			mesh.SetUVs(0, verts.Select(v => rockLook != null ? new Vector2(v.x, v.y) / 14f : atlas ? swatch + new Vector2(v.x, v.y) * (0.004f / PlugTile)
				: new Vector2(v.x / PlugTile, v.y / PlugTile)).ToList());
			mesh.SetNormals(norms);
			mesh.RecalculateBounds();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer wr = go.AddComponent<MeshRenderer>();
			wr.sharedMaterial = rockLook != null ? rockLook.sharedMaterial : look.sharedMaterial;
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
			Debug.Log("[CUSTOM ISLANDS] Den's mouth walled: " + cells + " cells, rock relief, smooth, " + wr.sharedMaterial.name + (atlas ? " at uv " + swatch.ToString("0.000") : ""));
		}

		/// <summary>The UV of the den mesh's corner nearest a point by its mouth (its rock colour), when the mesh is readable.</summary>
		public static bool MouthSwatch(Renderer look, Vector3 near, out Vector2 uv)
		{
			uv = Vector2.zero;
			MeshFilter mf = look != null ? look.GetComponent<MeshFilter>() : null;
			Mesh m = mf != null ? mf.sharedMesh : null;
			if (m == null || !m.isReadable) return false;
			Vector3[] vs = m.vertices;
			var uvs = new List<Vector2>();
			m.GetUVs(0, uvs);
			if (vs.Length == 0 || uvs.Count != vs.Length) return false;
			Vector3 at = look.transform.InverseTransformPoint(near);
			int best = 0; float bd = float.MaxValue;
			for (int i = 0; i < vs.Length; i++) { float d = (vs[i] - at).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
			uv = uvs[best];
			return true;
		}

		#endregion

		#region The mouth's frame

		/// <summary>Name of the rock frame a cave tool tunnel gets at its mouth, and at an open far end (children of the piece).</summary>
		public const string MouthName = "CaveMouth";

		/// <summary>A mouth's frame's own space (its child): level, facing the way in, at its floor - the frame itself turns
		/// with its piece, as a child turned otherwise under the stretched piece is skewed (its doorstep's front 0.16 m
		/// up going down, down under the ground going up; 11-01).</summary>
		public const string FrameSpaceName = "CaveMouthSpace";
		/// <summary>A mouth frame's footprint (a child of its FrameSpaceName: its position the middle, its scale the size),
		/// where CaveGround cuts the ground under it.</summary>
		public const string FootName = "CaveMouthFoot";

		/// <summary>
		/// A mouth's frame's measures (metres, from the mouth's floor, s along the way in, at size k, the floor rising slope
		/// degrees going in): the wall across the hillside at Wall, Half to each side, Height high, with the passage's hole in it;
		/// a doorstep from SillFrom to the wall; its top from the wall to CapTo, level up to the mouth and then with the tunnel.
		/// The ground is one height map: it could not turn from the level way in to the hill over the rock at the mouth, so its
		/// slope ran through the opening - a narrow way in, rock spikes in it, a slope that pushed you up (11-01).
		/// </summary>
		public struct Portal
		{
			public float Half, Height, Wall, CapTo, SillFrom, Rise;
			public float CapAt(float s) { return Height + Rise * Mathf.Max(0f, s) + 0.05f; }
		}

		public static Portal PortalOf(float size, float slope)
		{
			float k = Mathf.Clamp(size, 0.1f, 10f), a = Mathf.Clamp(slope, -60f, 60f) * Mathf.Deg2Rad, top = (ShellTop - Floor + EndDip) * k;
			var d = new Portal { Half = Mathf.Max(ShellHalfWidth * k + 0.5f, 1.6f * k + 3.2f), Height = top + Cover, Rise = Mathf.Tan(a) };
			// (the piece's end leans out over the mouth going up, back going down: the wall stands at its front)
			d.Wall = -Mathf.Max(0f, Mathf.Sin(a)) * top - 0.05f;
			d.CapTo = Mathf.Max(d.Wall + 3f, Mathf.Max(0f, -Mathf.Sin(a)) * top + 1f);
			d.SillFrom = d.Wall - 3f;
			return d;
		}

		/// <summary>A point of a tunnel piece's own measures (raft_props': metres at the piece's own scale, which stretches it
		/// 1.18 along) in the world: its transform's own point was that far again out - the frame stood 1.5 m before the rock,
		/// higher than the way in going down (11-01).</summary>
		static Vector3 AtMeasure(Transform t, Vector3 m)
		{
			return t.position + t.rotation * Vector3.Scale(m, CaveMountain.OverMeasured(t, Tunnel));
		}

		/// <summary>The far end of a cave laid out seen as a mouth (an open far end gets one too).</summary>
		public static Layout FarEnd(Layout l)
		{
			return new Layout { Mouth = l.End, End = l.Mouth, Flat = -l.Flat, Dir = -l.Dir, Slope = -l.Slope, Length = l.Length, Size = l.Size, Tunnels = l.Tunnels, Room = false };
		}

		/// <summary>Takes the frames off a piece (ObjectProps sets them again).</summary>
		public static void ClearMouths(GameObject piece)
		{
			if (piece == null) return;
			var old = new List<GameObject>();
			foreach (Transform c in piece.transform) if (c.name == MouthName || c.name == JoinName) old.Add(c.gameObject);
			foreach (GameObject go in old) UnityEngine.Object.DestroyImmediate(go);
		}

		/// <summary>Name of the floor plate over the join to the next tunnel or the den (a child of the piece; ObjectProps.CaveJoin).</summary>
		public const string JoinName = "CaveJoin";

		/// <summary>
		/// Lays a plate of the rock over the groove where a cave tool tunnel meets the next (ObjectProps.CaveJoin): the piece's
		/// floor dips at its ends, and two leave a groove across the way - at 1.5x a metre and a half wide and as deep, a step
		/// the player could not walk out of (11-01). Level with the line the floor ends meet on, so it hides under them either
		/// side. size: the tunnel's size over Raft's own. Made again when called again (ClearMouths takes it away).
		/// </summary>
		public static void Join(GameObject piece, float size)
		{
			if (piece == null || size <= 0f) return;
			Transform t = piece.transform;
			Renderer look = piece.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterial != null && r.name != MouthName && r.name != JoinName)
				.OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).FirstOrDefault();
			if (look == null) return;
			var go = new GameObject(JoinName);
			go.layer = look.gameObject.layer;
			Transform j = go.transform;
			j.SetParent(t, false);
			// (turned with the piece, its scale undone: metres along the line, which runs along the piece)
			j.position = AtMeasure(t, new Vector3(PassX, Floor - EndDip, FaceMin + Step));
			j.localRotation = Quaternion.identity;
			Vector3 ls = t.lossyScale;
			j.localScale = new Vector3(1f / Mathf.Max(0.001f, Mathf.Abs(ls.x)), 1f / Mathf.Max(0.001f, Mathf.Abs(ls.y)), 1f / Mathf.Max(0.001f, Mathf.Abs(ls.z)));
			float k = Mathf.Max(0.1f, size), half = JoinHalfWidth * k, len = JoinHalfLength * k, y = 0.03f;
			var verts = new List<Vector3> { new Vector3(-half, y, -len), new Vector3(half, y, -len), new Vector3(half, y, len), new Vector3(-half, y, len) };
			verts.AddRange(verts.ToArray()); // (both sides, each its own corners)
			var mesh = new Mesh { name = JoinName };
			mesh.SetVertices(verts);
			mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 }, 0);
			mesh.SetUVs(0, verts.Select(v => new Vector2(v.x / 6f, v.z / 6f)).ToList());
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = look.sharedMaterial;
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
		}

		// (the join's plate: across the passage into its walls, and along over the groove onto the floor either side)
		const float JoinHalfWidth = 2.4f, JoinHalfLength = 1.3f;

		// (the passage's outline at a piece's end, its own measures: distance from its middle at each of Rays angles - per mesh)
		const int Rays = 64;
		const float RimY = 1.8f;
		static readonly Dictionary<string, float[]> rims = new Dictionary<string, float[]>();

		/// <summary>
		/// The passage's outline at a tunnel piece's end (far: its far end) in its own measures, as distances from (PassX,
		/// Floor + RimY) at Rays angles: the nearest of its rock a little way in from the end, so the frame's hole is never
		/// wider than the passage (the shell's dark outside showed past it). Measured from its collider's mesh once.
		/// </summary>
		static float[] Rim(Transform piece, bool far)
		{
			var meshes = new List<KeyValuePair<Mesh, Matrix4x4>>();
			string key = far ? "far" : "mouth";
			foreach (MeshCollider mc in piece.GetComponentsInChildren<MeshCollider>(true))
			{
				if (mc.sharedMesh == null || mc.name == MouthName || mc.name == PlugName || !mc.sharedMesh.isReadable) continue;
				meshes.Add(new KeyValuePair<Mesh, Matrix4x4>(mc.sharedMesh, Matrix4x4.TRS(piece.position, piece.rotation, CaveMountain.OverMeasured(piece, Tunnel)).inverse * mc.transform.localToWorldMatrix));
				key += "|" + mc.sharedMesh.GetInstanceID();
			}
			float[] found;
			if (rims.TryGetValue(key, out found)) return found;
			found = new float[Rays];
			for (int i = 0; i < Rays; i++) found[i] = float.PositiveInfinity;
			float face = far ? FaceMin + Step : FaceMin, inward = far ? -1f : 1f;
			var segs = new List<Vector4>();
			foreach (float depth in new[] { 0.15f, 0.5f, 0.9f })
			{
				float zc = face + inward * depth;
				segs.Clear();
				foreach (KeyValuePair<Mesh, Matrix4x4> m in meshes)
				{
					Vector3[] v = m.Key.vertices;
					int[] tri = m.Key.triangles;
					var w = new Vector3[v.Length];
					for (int i = 0; i < v.Length; i++) w[i] = m.Value.MultiplyPoint3x4(v[i]);
					for (int i = 0; i + 2 < tri.Length; i += 3)
					{
						Vector3 a = w[tri[i]], b = w[tri[i + 1]], c = w[tri[i + 2]];
						float da = a.z - zc, db = b.z - zc, dc = c.z - zc;
						if (da >= 0f && db >= 0f && dc >= 0f || da < 0f && db < 0f && dc < 0f) continue;
						var cut = new List<Vector2>(2);
						if (da >= 0f != db >= 0f) cut.Add(Vector2.Lerp(a, b, da / (da - db)));
						if (db >= 0f != dc >= 0f) cut.Add(Vector2.Lerp(b, c, db / (db - dc)));
						if (dc >= 0f != da >= 0f) cut.Add(Vector2.Lerp(c, a, dc / (dc - da)));
						if (cut.Count == 2) segs.Add(new Vector4(cut[0].x, cut[0].y, cut[1].x, cut[1].y));
					}
				}
				var o = new Vector2(PassX, Floor + RimY);
				for (int r = 0; r < Rays; r++)
				{
					float ang = r * Mathf.PI * 2f / Rays;
					var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
					foreach (Vector4 s in segs)
					{
						Vector2 p = new Vector2(s.x, s.y), e = new Vector2(s.z - s.x, s.w - s.y), op = p - o;
						float den = dir.x * e.y - dir.y * e.x;
						if (Mathf.Abs(den) < 1e-7f) continue;
						float t = (op.x * e.y - op.y * e.x) / den, u = (op.x * dir.y - op.y * dir.x) / den;
						if (t > 0.05f && u >= 0f && u <= 1f && t < found[r]) found[r] = t;
					}
				}
			}
			// (no rock found that way: the passage's own size - 3.2 m wide, its floor 2.7 under the middle, its roof 2.2 over it)
			for (int i = 0; i < Rays; i++)
				if (float.IsInfinity(found[i]) || found[i] > 4f)
				{
					float ang = i * Mathf.PI * 2f / Rays, cx = Mathf.Cos(ang) / 1.5f, cy = Mathf.Sin(ang) / (Mathf.Sin(ang) < 0f ? RimY + EndDip : 2.2f);
					found[i] = 1f / Mathf.Sqrt(cx * cx + cy * cy);
				}
			rims[key] = found;
			return found;
		}

		/// <summary>
		/// Puts the rock frame at a cave tool tunnel's mouth (far: its open far end - ObjectProps.CaveMouth / CaveEnd): rough
		/// rock across the hillside with the passage's own outline through it, its sides lined back to the tunnel, a
		/// threshold level with the floor before it and a top that falls away into the ground behind and beside it. The ground under it is taken out (CaveGround), so
		/// the way in is the passage's full size, flush with the rock. size: the tunnel's size over Raft's own. In the editor
		/// and in a world; made again when called again.
		/// </summary>
		public static void Mouth(GameObject piece, float size, bool far)
		{
			if (piece == null || size <= 0f) return;
			Transform t = piece.transform;
			Vector3 fwd = t.forward, flat = new Vector3(fwd.x, 0f, fwd.z);
			if (flat.sqrMagnitude < 1e-6f) return;
			flat.Normalize();
			float slope = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
			if (far) { flat = -flat; slope = -slope; }
			Renderer look = piece.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterial != null && r.name != MouthName && r.name != PlugName)
				.OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).FirstOrDefault();
			if (look == null) return;
			float[] rim = Rim(t, far);
			Portal d = PortalOf(size, slope);

			var go = new GameObject(MouthName);
			go.layer = look.gameObject.layer;
			Transform f = go.transform;
			f.SetParent(t, false);
			f.position = AtMeasure(t, new Vector3(PassX, Floor - EndDip, far ? FaceMin + Step : FaceMin));
			// (turned with the piece, so its scale is undone exactly; the mesh is turned level in it - see FrameSpaceName)
			f.localRotation = Quaternion.identity;
			Vector3 ls = t.lossyScale;
			f.localScale = new Vector3(1f / Mathf.Max(0.001f, Mathf.Abs(ls.x)), 1f / Mathf.Max(0.001f, Mathf.Abs(ls.y)), 1f / Mathf.Max(0.001f, Mathf.Abs(ls.z)));
			Quaternion level = Quaternion.LookRotation(flat, Vector3.up), toFrame = Quaternion.Inverse(f.rotation) * level;
			var space = new GameObject(FrameSpaceName).transform;
			space.SetParent(f, false);
			space.rotation = level;
			Func<Vector3, Vector3> inFrame = w => Quaternion.Inverse(level) * (w - f.position);

			// (the outline at the piece's end in the frame's metres: a little inside the rock, its floor part level with the floor)
			float face = far ? FaceMin + Step : FaceMin, k = Mathf.Max(0.1f, size);
			Vector3 mid = inFrame(AtMeasure(t, new Vector3(PassX, Floor + RimY, face)));
			var outline = new List<Vector3>();
			for (int i = 0; i < Rays; i++)
			{
				float ang = i * Mathf.PI * 2f / Rays, r = Mathf.Max(0.3f, rim[i] - 0.12f / k);
				Vector3 p = inFrame(AtMeasure(t, new Vector3(PassX + Mathf.Cos(ang) * r, Floor + RimY + Mathf.Sin(ang) * r, face)));
				if (p.y < 0.35f) p.y = -0.02f;
				p.x = Mathf.Clamp(p.x, -d.Half + 0.3f, d.Half - 0.3f);
				p.y = Mathf.Min(p.y, d.Height - 0.3f);
				outline.Add(p);
			}
			outline.Sort((a, b) => Mathf.Atan2(a.y - mid.y, a.x - mid.x).CompareTo(Mathf.Atan2(b.y - mid.y, b.x - mid.x)));

			// The rock (a field over a grid in the frame's level metres, below 0 rock, made a surface by RockNets): a rough
			// outcrop across the hillside, its face uneven and leaning back near its top, its top falling away into the
			// hill on both sides and behind; a threshold low before it whose front edge goes down under the ground; the
			// passage's outline through it, flaring out toward the front; and room left for the tunnel's own rock behind the
			// piece's end. It was a square wall with a flat doorstep plate and straight edges until 11-02.
			float h = Mathf.Clamp(0.32f * k, 0.25f, 0.7f), wz = d.Wall, capF = d.CapAt(0f);
			float zBack = d.CapTo + 0.2f + (Mathf.Max(capF, d.CapAt(d.CapTo)) + 2.2f) / 3f;
			float topMax = Mathf.Max(capF, d.CapAt(zBack)) + 0.6f;
			float v = topMax + 2.2f, dxMax = v <= 4.5f ? v : 4.5f + (v - 4.5f) / 2.5f;
			float x0 = -(d.Half + dxMax) - 2f * h, y0 = -1.6f - 2f * h, z0 = d.SillFrom - 2.6f - 2f * h;
			int nx = Mathf.CeilToInt((-2f * x0) / h) + 1, ny = Mathf.CeilToInt((topMax + 2f * h - y0) / h) + 1, nz = Mathf.CeilToInt((zBack + 2f * h - z0) / h) + 1;
			Vector3 o = new Vector3(x0, y0, z0);
			// (its own roughness, the same each time for the same piece)
			Vector3 lp = t.localPosition;
			float sx = Mathf.Repeat(lp.x * 0.137f + lp.z * 0.071f + (far ? 37f : 0f), 200f), sz = Mathf.Repeat(lp.z * 0.113f - lp.x * 0.059f + (far ? 53f : 0f), 200f);
			Func<float, float, float> noise = (a, b) => Mathf.PerlinNoise(a + sx, b + sz);
			Func<float, float> smooth01 = s => { s = Mathf.Clamp01(s); return s * s * (3f - 2f * s); };
			var hole = new List<Vector2>(outline.Select(p => new Vector2(p.x, p.y)));
			float shellHalf = ShellHalfWidth * k - 0.25f, shellUp = (ShellTop - Floor + EndDip) * k * 1.02f - 0.3f;
			// (what depends on x and y alone, and on x and z alone)
			var dPoly = new float[nx * ny];
			var faceN = new float[nx * ny];
			var flareN = new float[nx * ny];
			for (int y = 0; y < ny; y++)
				for (int x = 0; x < nx; x++)
				{
					float px = x0 + x * h, py = y0 + y * h;
					dPoly[x + y * nx] = RockNets.PolygonDistance(hole, new Vector2(px, py));
					faceN[x + y * nx] = noise(px * 0.35f, py * 0.35f) * 0.55f + noise(px * 0.9f + 7.1f, py * 0.9f + 3.3f) * 0.3f + noise(px * 1.9f + 61f, py * 1.9f + 47f) * 0.15f;
					flareN[x + y * nx] = noise(px * 0.6f + 41f, py * 0.6f + 23f);
				}
			var topN = new float[nx * nz];
			var rubN = new float[nx * nz];
			for (int z = 0; z < nz; z++)
				for (int x = 0; x < nx; x++)
				{
					float px = x0 + x * h, pz = z0 + z * h;
					topN[x + z * nx] = noise(px * 0.3f + 11f, pz * 0.3f + 5f) * 0.65f + noise(px * 0.8f + 2f, pz * 0.8f + 17f) * 0.35f;
					rubN[x + z * nx] = noise(px * 0.7f + 53f, pz * 0.7f + 29f);
				}
			var frontN = new float[nx];
			for (int x = 0; x < nx; x++) frontN[x] = noise((x0 + x * h) * 0.5f + 31f, 9.7f);
			var sideN = new float[nz];
			for (int z = 0; z < nz; z++) sideN[z] = noise(71f, (z0 + z * h) * 0.6f + 13f);
			// (the passage's floor at the outline: as wide as the threshold before it)
			float floorHalf = 1f;
			foreach (Vector2 p in hole) if (p.y < 0.35f) floorHalf = Mathf.Max(floorHalf, Mathf.Abs(p.x));

			var field = new float[nx * ny * nz];
			for (int z = 0; z < nz; z++)
				for (int y = 0; y < ny; y++)
					for (int x = 0; x < nx; x++)
					{
						float px = x0 + x * h, py = y0 + y * h, pz = z0 + z * h, ax = Mathf.Abs(px);
						int xy = x + y * nx, xz = x + z * nx;
						float dx = Mathf.Max(0f, ax - d.Half);
						// the outcrop
						float top = d.CapAt(pz) + 0.35f + 0.8f * topN[xz] - dx - 1.5f * Mathf.Max(0f, dx - 4.5f) - 3f * Mathf.Max(0f, pz - (d.CapTo + 0.2f));
						float zf = wz - 0.15f * k - 0.9f * k * faceN[xy] + 0.6f * Mathf.Max(0f, py - (top - 2.5f)) + 0.35f * dx;
						float body = Mathf.Max(Mathf.Max(zf - pz, py - top), Mathf.Max(-1.6f - py, Mathf.Max(ax - (d.Half + dxMax), pz - zBack)));
						// its flanks: rock coming out of the cut's walls beside the way in (CutSlope), in patches further out and
						// rising to the face, so the walls don't meet it in a straight edge
						float wallY = (ax - d.Half) * CutSlope;
						float bump = -0.3f + (rubN[xz] - 0.35f) * 1.4f * k * smooth01((pz - (zf - 3f * k)) / (0.8f * k)) + 2.5f * k * smooth01((pz - (zf - 2f * k)) / (2f * k));
						float flank = Mathf.Max(Mathf.Max(d.Half - 0.3f - 0.8f * topN[xz] - ax, py - (wallY + bump)), Mathf.Max(zf - 3f * k - pz, Mathf.Max(-1.6f - py, ax - (d.Half + dxMax))));
						// the threshold: rock level with the floor (just over the ground beside it), its ragged edges going down
						// under the ground (no plate edge); over every ground cell CaveGround may cut before the mouth (all of the
						// footprint, x within Half, z from SillFrom) and 0.45 m past, so no cut cell's square edge shows
						float zFront = d.SillFrom - 1.8f + 0.6f * (2f * frontN[x] - 1f);
						float edge = Mathf.Min(pz - zFront, Mathf.Max(floorHalf + 0.5f, d.Half + 0.5f) + 1f * sideN[z] - ax);
						float thrTop = -0.02f - 1f * (1f - smooth01(edge / 1.5f));
						float thr = Mathf.Max(Mathf.Max(-edge, py - thrTop), -1.6f - py);
						float solid = Mathf.Min(Mathf.Min(body, flank), thr);
						// the way in: the passage's outline, flared toward the front; and the tunnel's own rock behind its end
						float flare = 0.75f * k * (0.6f + 0.8f * flareN[xy]) * (1f - smooth01((pz - zf) / 1.6f));
						float holeAir = Mathf.Max(dPoly[xy] - flare, -0.02f - py);
						float boxAir = Mathf.Max(Mathf.Max(ax - shellHalf, py - (shellUp + d.Rise * pz)), Mathf.Max(-d.Rise * py + 0.05f - pz, Mathf.Min(-0.02f, d.Rise * pz - 0.05f) - py));
						field[x + y * nx + z * nx * ny] = Mathf.Max(solid, -Mathf.Min(holeAir, boxAir));
					}

			int triangles;
			float flatY = 0.6f * h;
			Mesh mesh = RockNets.Build(MouthName, field, nx, ny, nz, o, h, (air, rock) => 0, 1, 2,
				(before, after) => Mathf.Abs(before.y + 0.02f) < flatY && Mathf.Abs(after.y + 0.02f) < flatY ? new Vector3(after.x, -0.02f, after.z) : after, 1f / 6f, out triangles);
			field = null;
			// (into the frame's own space: the mesh was made level)
			Vector3[] mv = mesh.vertices, mn = mesh.normals;
			for (int i = 0; i < mv.Length; i++) { mv[i] = toFrame * mv[i]; mn[i] = toFrame * mn[i]; }
			mesh.vertices = mv;
			mesh.normals = mn;
			mesh.RecalculateBounds();
			try { mesh.RecalculateTangents(); } catch { }
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = look.sharedMaterial;
			// (less shine: the smoothed rock caught wet-looking highlights the pieces' own rock doesn't)
			if (mr.sharedMaterial.HasProperty("_Smoothness_Multiplyer"))
			{
				var block = new MaterialPropertyBlock();
				block.SetFloat("_Smoothness_Multiplyer", 0.45f);
				mr.SetPropertyBlock(block);
			}
			go.AddComponent<MeshCollider>().sharedMesh = mesh;

			// (the footprint the ground is cut under, CaveGround: the frame's as it was, not the rock's rough outline)
			var foot = new GameObject(FootName).transform;
			foot.SetParent(space, false);
			float yTop = Mathf.Max(capF, d.CapAt(d.CapTo)), yBot = -0.6f;
			foot.localPosition = new Vector3(0f, (yTop + yBot) * 0.5f, (d.SillFrom + d.CapTo) * 0.5f);
			foot.localRotation = Quaternion.identity;
			foot.localScale = new Vector3(2f * d.Half, yTop - yBot, d.CapTo - d.SillFrom);
		}

		#endregion

		/// <summary>Whether a point is in a cave's rock: its tunnel's shell or its den's box, or a mouth's frame.</summary>
		public static bool InRock(Layout l, Vector3 p)
		{
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			if (l.Tunnels > 0 && s > -0.3f && s < l.Length + 0.3f && q < l.HalfWidth && p.y > l.FloorAt(s) - 2f * l.Size && p.y < l.FloorAt(s) + l.Top + 0.5f) return true;
			if (l.Tunnels > 0 && InFrame(l, PortalOf(l.Size, l.Slope), p)) return true;
			if (l.Tunnels > 0 && !l.Room && InFrame(FarEnd(l), PortalOf(l.Size, -l.Slope), p)) return true;
			if (l.Room)
			{
				Vector3 lp = Quaternion.Inverse(l.RoomRotation) * (p - l.RoomPivot);
				Vector3 rmin = l.RoomMin, rmax = l.RoomMax;
				if (lp.x > rmin.x && lp.x < rmax.x && lp.y > rmin.y && lp.y < rmax.y && lp.z > rmin.z && lp.z < rmax.z) return true;
			}
			return false;
		}

		static bool InFrame(Layout l, Portal d, Vector3 p)
		{
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			return q < d.Half && s > d.Wall && s < d.CapTo && p.y > l.Mouth.y - 0.6f && p.y < l.Mouth.y + d.CapAt(s) + 0.5f;
		}

		public struct Move { public Transform T; public Vector3 From, To; }

		/// <summary>Undo step for objects the ground moved (laying a cave).</summary>
		public class MoveCommand : CommandUndoRedo.ICommand
		{
			readonly List<Move> moves;
			public MoveCommand(List<Move> moves) { this.moves = moves; }
			public void Execute() { foreach (Move m in moves) if (m.T != null) m.T.position = m.To; }
			public void UnExecute() { foreach (Move m in moves) if (m.T != null) m.T.position = m.From; }
		}

		/// <summary>The ground was shaped for a cave (its heights before: before, [z, x]): the plants and rocks in its rock
		/// hidden (cleared), the objects that stood on the ground in the area moved up or down with it (moved; done already).</summary>
		static void SettleObjects(GameObject placedRoot, Terrain terrain, float[,] before, Layout l, Vector3 min, Vector3 max, out List<GameObject> cleared, out List<Move> moved)
		{
			cleared = new List<GameObject>();
			moved = new List<Move>();
			TerrainData data = terrain.terrainData;
			Vector3 o = terrain.transform.position, size = data.size;
			int res = data.heightmapResolution;
			Func<Vector3, float> oldGround = p =>
			{
				float fx = Mathf.Clamp((p.x - o.x) / size.x * (res - 1), 0f, res - 1.001f), fz = Mathf.Clamp((p.z - o.z) / size.z * (res - 1), 0f, res - 1.001f);
				int ix = (int)fx, iz = (int)fz;
				float tx = fx - ix, tz = fz - iz;
				float h = Mathf.Lerp(Mathf.Lerp(before[iz, ix], before[iz, ix + 1], tx), Mathf.Lerp(before[iz + 1, ix], before[iz + 1, ix + 1], tx), tz);
				return o.y + h * size.y;
			};
			// (a boulder's middle can be off the doorstep while its rock lies on it - 11-02: what any collider puts there counts too)
			HashSet<EditorGameObject> onStep = OnDoorsteps(l);
			foreach (EditorGameObject e in placedRoot.GetComponentsInChildren<EditorGameObject>(false))
			{
				Vector3 p = e.transform.position;
				if (e.GameObjectName == Tunnel || e.GameObjectName == Room) continue;
				bool setUp = e.Props != null && e.Props.Count > 0;
				if (!setUp && onStep.Contains(e)) { cleared.Add(e.gameObject); continue; }
				if (p.x < min.x || p.x > max.x || p.z < min.z || p.z > max.z) continue;
				if (!setUp && (InRock(l, p) || OnWayIn(l, p))) { cleared.Add(e.gameObject); continue; }
				float was = oldGround(p), now = terrain.SampleHeight(p) + o.y;
				if (Mathf.Abs(p.y - was) > 1.5f || Mathf.Abs(now - was) < 0.05f) continue;
				Vector3 to = p + Vector3.up * (now - was);
				if (!setUp && InRock(l, to)) { cleared.Add(e.gameObject); continue; }
				moved.Add(new Move { T = e.transform, From = p, To = to });
			}
			foreach (GameObject go in cleared) go.SetActive(false);
			foreach (Move m in moved) m.T.position = m.To;
			// (and what the ground's move brought onto a doorstep)
			foreach (EditorGameObject e in OnDoorsteps(l))
				if (e.GameObjectName != Tunnel && e.GameObjectName != Room && (e.Props == null || e.Props.Count == 0) && !cleared.Contains(e.gameObject) && e.transform.IsChildOf(placedRoot.transform))
				{
					cleared.Add(e.gameObject);
					e.gameObject.SetActive(false);
				}
		}

		/// <summary>Whether a point is on a mouth's way in (its doorstep and a little before it, the frame's width; an open far
		/// end's too): a rock or plant there stood in the way in (a boulder before the doorstep, 11-01).</summary>
		static bool OnWayIn(Layout l, Vector3 p)
		{
			if (l.Tunnels <= 0) return false;
			foreach (Layout m in l.Room ? new[] { l } : new[] { l, FarEnd(l) })
			{
				Portal d = PortalOf(m.Size, m.Slope);
				Vector3 rel = p - m.Mouth; rel.y = 0f;
				float s = Vector3.Dot(rel, m.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, m.Flat)));
				if (q < d.Half + 0.5f && s > d.SillFrom - 2f && s <= d.Wall + 0.1f && p.y > m.Mouth.y - 3f && p.y < m.Mouth.y + d.Height) return true;
			}
			return false;
		}

		/// <summary>The objects with a collider on a mouth's doorstep or the metre and a half before it (an open far end's too), above its floor.</summary>
		static HashSet<EditorGameObject> OnDoorsteps(Layout l)
		{
			var found = new HashSet<EditorGameObject>();
			if (l.Tunnels <= 0) return found;
			Physics.SyncTransforms();
			foreach (Layout m in l.Room ? new[] { l } : new[] { l, FarEnd(l) })
			{
				Portal d = PortalOf(m.Size, m.Slope);
				float s0 = d.SillFrom - 1.5f, s1 = Mathf.Max(d.Wall + 0.1f, s0 + 0.5f), y0 = 0.3f, y1 = Mathf.Max(d.Height, 1f);
				Vector3 mid = m.Mouth + m.Flat * ((s0 + s1) / 2f) + Vector3.up * ((y0 + y1) / 2f);
				foreach (Collider c in Physics.OverlapBox(mid, new Vector3(d.Half + 0.3f, (y1 - y0) / 2f, (s1 - s0) / 2f), Quaternion.LookRotation(m.Flat, Vector3.up), ~0, QueryTriggerInteraction.Collide))
				{
					EditorGameObject e = c is TerrainCollider ? null : c.GetComponentInParent<EditorGameObject>();
					if (e != null) found.Add(e);
				}
			}
			return found;
		}

		#region Laying it in the editor

		/// <summary>Whether a cave is being laid (its pieces loading).</summary>
		public static bool Busy { get; private set; }

		/// <summary>The last cave laid (tests): its layout and its objects.</summary>
		public static Layout LastLayout;
		public static List<GameObject> LastPieces = new List<GameObject>();

		/// <summary>
		/// Lays a cave in the editor's island (the scenes its pieces come from loaded first): the pieces, the ground shaped
		/// around them, one undo step. Nothing is selected after: the gizmo stood on the cave (11-01). Says what it did, or
		/// why it can't. done gets the objects (none: refused).
		/// size: times Raft's own size (Size).
		/// </summary>
		public static IEnumerator Lay(Vector3 mouth, Vector3 toward, float slope, bool room, bool hill, float size, Action<List<GameObject>> done = null)
		{
			var made = new List<GameObject>();
			Terrain terrain = terraineditor.terrain;
			GameObject placedRoot = GameObject.Find("PlacedObjects");
			if (terrain == null || placedRoot == null || Busy) { if (done != null) done(made); yield break; }
			Layout l = Plan(mouth, toward, slope, room, size);
			if (!ObjectLimit.Allow(l.Pieces.Count)) { if (done != null) done(made); yield break; }
			Busy = true;
			try
			{
				string[] names = l.Pieces.Select(p => p.Name).Distinct().ToArray();
				if (names.Any(n => !PlaceableCatalog.IsLoaded(n)))
				{
					DynamicIslands.Notify("Loading the cave pieces from Temperance and Balboa...");
					yield return PlaceableCatalog.EnsureLoaded(names);
				}
				string missing = names.FirstOrDefault(n => !PlaceableCatalog.IsLoaded(n));
				if (missing != null) { DynamicIslands.Notify("Could not load " + PlaceableCatalog.DisplayName(missing) + " - see the console (F10)", true); if (done != null) done(made); yield break; }
				if (!DynamicIslands.InEditor() || terraineditor.terrain != terrain) { if (done != null) done(made); yield break; }

				// (the ground first, with its snapshot for undo)
				TerrainData data = terrain.terrainData;
				int hres = data.heightmapResolution, ares = data.alphamapResolution;
				float[,] heightsBefore = data.GetHeights(0, 0, hres, hres);
				float[,,] alphaBefore = data.GetAlphamaps(0, 0, ares, ares);
				float[,] maskBefore = terraineditor.paintMask != null ? (float[,])terraineditor.paintMask.Clone() : null;
				Vector3 min, max;
				ShapeGround(terrain, l, hill, out min, out max);
				TerrainPainter.PaintWorldArea(terrain, DynamicIslands.EditorWaterLevel, min, max, terraineditor.paintMask);
				WearMouthGround(terrain, l, terraineditor.paintMask);
				// (what stood in the rock goes - plants and rocks only, anything set up stays; what stood on the ground moves with it)
				List<GameObject> cleared;
				List<Move> moved;
				SettleObjects(placedRoot, terrain, heightsBefore, l, min, max, out cleared, out moved);

				var props = new Dictionary<string, string> { { ObjectProps.Skin, ObjectProps.SkinRock } };
				string sized = l.Size.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
				for (int i = 0; i < l.Pieces.Count; i++)
				{
					Piece p = l.Pieces[i];
					GameObject go = PlaceableCatalog.Spawn(p.Name, placedRoot.transform);
					if (go == null) continue;
					go.transform.position = p.Position;
					go.transform.rotation = p.Rotation;
					if (p.Scale > 0f && Mathf.Abs(p.Scale - 1f) > 0.001f) go.transform.localScale *= p.Scale;
					foreach (Collider c in go.GetComponentsInChildren<Collider>()) c.enabled = true;
					// (the tunnels' icy rock takes the den's brown island rock; the den keeps its own. The first tunnel gets the
					// mouth's frame, the last an open far end's)
					Dictionary<string, string> own;
					if (p.Name == Tunnel)
					{
						own = new Dictionary<string, string>(props);
						if (i == 0) own[ObjectProps.CaveMouth] = sized;
						if (i == l.Tunnels - 1 && !l.Room) own[ObjectProps.CaveEnd] = sized;
						if (i < l.Tunnels - 1 || l.Room) own[ObjectProps.CaveJoin] = sized; // (and into the den)
					}
					else own = new Dictionary<string, string> { { ObjectProps.CavePlug, sized } };
					EditorGameObject.Attach(go, p.Name, own);
					made.Add(go);
				}

				var group = new CommandUndoRedo.CommandGroup();
				group.Add(new ObjectVisibilityCommand(made, true));
				if (cleared.Count > 0) group.Add(new ObjectVisibilityCommand(cleared, false));
				if (moved.Count > 0) group.Add(new MoveCommand(moved));
				Vector3 o = terrain.transform.position;
				float spacing = data.size.x / (hres - 1);
				int hx0 = Mathf.Clamp(Mathf.FloorToInt((min.x - o.x) / spacing) - 1, 0, hres - 1), hz0 = Mathf.Clamp(Mathf.FloorToInt((min.z - o.z) / spacing) - 1, 0, hres - 1);
				int hx1 = Mathf.Clamp(Mathf.CeilToInt((max.x - o.x) / spacing) + 1, 0, hres - 1), hz1 = Mathf.Clamp(Mathf.CeilToInt((max.z - o.z) / spacing) + 1, 0, hres - 1);
				var heightRect = new RectInt(hx0, hz0, hx1 - hx0 + 1, hz1 - hz0 + 1);
				RectInt alphaRect;
				if (TerrainPainter.WorldToAlphamapRect(terrain, min - Vector3.one * 2f, max + Vector3.one * 2f, out alphaRect))
					group.Add(new TerrainStrokeCommand(data, heightRect, TerrainStrokeCommand.Crop(heightsBefore, heightRect),
						alphaRect, TerrainStrokeCommand.Crop(alphaBefore, alphaRect), maskBefore != null ? TerrainStrokeCommand.Crop(maskBefore, alphaRect) : null, terraineditor.paintMask));
				CommandUndoRedo.UndoRedoManager.Insert(group);

				LastLayout = l;
				LastPieces = made;
				float sea = DynamicIslands.EditorWaterLevel;
				float lowest = Mathf.Min(l.Mouth.y, l.End.y);
				string what = (Mathf.Abs(l.Size - 1f) > 0.01f ? "size " + l.Size.ToString("0.0") + "x, " : "") + (l.Tunnels > 0 ? l.Tunnels + " tunnel piece" + (l.Tunnels == 1 ? "" : "s") + " (" + Mathf.RoundToInt(l.Length) + " m" + (Mathf.Abs(l.Slope) > 0.5f ? ", " + (l.Slope < 0 ? "down " : "up ") + Mathf.RoundToInt(Mathf.Abs(l.Slope)) + "°" : "") + ")" : "") +
					(l.Room ? (l.Tunnels > 0 ? " and " : "") + "a cave room" : ", open at the far end");
				if (lowest < sea + 0.3f) DynamicIslands.Notify("Cave laid: " + what + " - part of its floor is under the sea, so it fills with water. Ctrl+Z takes it back", true, 7);
				else DynamicIslands.Notify("Cave laid: " + what + ". Ctrl+Z takes it back", false, 5);
			}
			finally { Busy = false; }
			if (done != null) done(made);
		}

		#endregion

		#region The drag (terraineditor's Cave action)

		static Vector3? dragFrom;
		static LineRenderer line, ring;

		/// <summary>The mouse in the editor while the Cave tool is chosen: press where the mouth goes, drag the way the tunnel
		/// runs, let go to lay it; Esc or the right button lets it go. Before the press the mouth's outline (its size: Size)
		/// stands at the cursor.</summary>
		public static void Drag(bool overTerrain, Vector3 point, bool canUse)
		{
			if (dragFrom.HasValue && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1) || !canUse && !Input.GetMouseButton(0))) { Cancel(); return; }
			if (!dragFrom.HasValue)
			{
				if (canUse && overTerrain && Input.GetMouseButtonDown(0) && !EditorCamera.UsingMouse && !Busy) dragFrom = point;
				// (the mouth's outline at the cursor, across the view)
				Camera cam = Camera.main;
				Vector3 look = cam != null ? cam.transform.forward : Vector3.forward;
				look.y = 0f;
				if (canUse && overTerrain && !Busy) ShowMouth(point, look.sqrMagnitude > 1e-4f ? look.normalized : Vector3.forward, Size);
				else if (ring != null) ring.enabled = false;
				return;
			}
			if (Input.GetMouseButton(0))
			{
				if (overTerrain) Show(Plan(dragFrom.Value, point, Slope, EndRoom, Size), point);
				return;
			}
			// (let go)
			Vector3 from = dragFrom.Value, to = overTerrain ? point : lastTo;
			Cancel();
			Vector3 flat = to - from; flat.y = 0f;
			if (flat.magnitude < 2f) { DynamicIslands.Notify("Cave: press where the mouth is to be and drag the way the tunnel goes, into a hill", true); return; }
			DynamicIslands.instance.StartCoroutine(Lay(from, to, Slope, EndRoom, Hill, Size));
		}

		static Vector3 lastTo;

		static LineRenderer NewLine(string name, float width, Color a, Color b)
		{
			var go = new GameObject(name);
			LineRenderer lr = go.AddComponent<LineRenderer>();
			lr.material = new Material(Shader.Find("Sprites/Default"));
			lr.startWidth = lr.endWidth = width;
			lr.startColor = a; lr.endColor = b;
			lr.useWorldSpace = true;
			return lr;
		}

		/// <summary>The mouth's outline at a size (world, its floor at floor, the tunnel running along dir): the rock's width
		/// along the ground and the passage's opening rising from it - the way in is about that wide and high.</summary>
		public static List<Vector3> MouthOutline(Vector3 floor, Vector3 dir, float size)
		{
			Vector3 across = Vector3.Cross(Vector3.up, dir).normalized;
			float k = Mathf.Clamp(size, MinSize, MaxSize), rock = PortalOf(k, 0f).Half, half = 1.6f * k, up = 4.9f * k;
			Vector3 b = floor + Vector3.up * 0.15f;
			var pts = new List<Vector3> { b - across * rock, b - across * half };
			// (the opening: straight sides to two thirds of its height, a half-ellipse over them)
			float wall = up * 0.62f;
			pts.Add(b - across * half + Vector3.up * wall);
			for (int i = 1; i < 12; i++)
			{
				float a = Mathf.PI * i / 12f;
				pts.Add(b - across * (half * Mathf.Cos(a)) + Vector3.up * (wall + (up - wall) * Mathf.Sin(a)));
			}
			pts.Add(b + across * half + Vector3.up * wall);
			pts.Add(b + across * half);
			pts.Add(b + across * rock);
			return pts;
		}

		static void ShowMouth(Vector3 floor, Vector3 dir, float size)
		{
			if (ring == null) ring = NewLine("CaveToolMouth", 0.25f, new Color(1f, 0.85f, 0.35f), new Color(1f, 0.85f, 0.35f));
			ring.enabled = true;
			List<Vector3> pts = MouthOutline(floor, dir, size);
			ring.positionCount = pts.Count;
			ring.SetPositions(pts.ToArray());
		}

		static void Show(Layout l, Vector3 to)
		{
			lastTo = to;
			if (line == null) line = NewLine("CaveToolPreview", 0.6f, new Color(1f, 0.8f, 0.3f), new Color(1f, 0.5f, 0.2f));
			line.enabled = true;
			var pts = new List<Vector3> { l.Mouth + Vector3.up * 0.3f, l.End + Vector3.up * 0.3f };
			if (l.Room) pts.Add(l.RoomPivot + l.RoomRotation * new Vector3(l.RoomMouth.x, l.RoomMouth.y, 0f) + Vector3.up * 0.3f);
			line.positionCount = pts.Count;
			line.SetPositions(pts.ToArray());
			ShowMouth(l.Mouth, l.Flat, l.Size);
			string text = "Cave: " + (l.Tunnels > 0 ? l.Tunnels + " tunnel piece(s), " + Mathf.RoundToInt(l.Length) + " m" : "no tunnel") + (l.Room ? " + a cave room" : ", open far end") + (Mathf.Abs(l.Size - 1f) > 0.01f ? ", size " + l.Size.ToString("0.0") + "x" : "") + " - let go to lay it, Esc to cancel";
			if (text != shownText) { shownText = text; DynamicIslands.Notify(text, false, 2); }
		}

		static string shownText;

		public static void Cancel()
		{
			dragFrom = null;
			shownText = null;
			if (line != null) line.enabled = false;
			if (ring != null) ring.enabled = false;
		}

		#endregion
	}

	/// <summary>
	/// The cave tool's pieces (tunnels with a mouth, join or end, and dens with a plug) lit in a world as in the editor: Raft
	/// lights a world with a strong blue sky (ambient intensity ~8), so the pieces came out pale blue-white, where the editor's
	/// warm light shows them brown-grey. For the camera about to draw (AmbienceZones.PreRender) each piece's renderers get
	/// their own light probe - the sky's light made grey and warm, and dimmed - and less of the sky's shine, through their
	/// property blocks (the tint and rock skin in them kept). The editor's light is left alone (11-03).
	/// </summary>
	public class CaveLit : MonoBehaviour
	{
		static readonly List<CaveLit> live = new List<CaveLit>();
		[NonSerialized] Renderer[] rends;
		static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		static readonly SphericalHarmonicsL2[] one = new SphericalHarmonicsL2[1];
		/// <summary>How much of the rock's shine is kept (as the dug tunnel's lining: its blue reflections washed the walls out).</summary>
		const float Shine = 0.45f;
		/// <summary>Dev probes: leave the pieces' light alone.</summary>
		public static bool Off;
		static bool told;

		public static bool AnyLive { get { return live.Count > 0; } }

		/// <summary>Marks a cave tool piece (again after its parts changed: its renderers are looked up anew).</summary>
		public static void Mark(GameObject piece)
		{
			if (piece == null) return;
			CaveLit c = piece.GetComponent<CaveLit>();
			if (c == null) c = piece.AddComponent<CaveLit>();
			c.rends = null;
		}

		void OnEnable() { if (!live.Contains(this)) live.Add(this); }
		void OnDisable() { live.Remove(this); }

		public static void LightAll(SphericalHarmonicsL2 sh)
		{
			if (live.Count == 0 || Off || DynamicIslands.InEditor()) return;
			one[0] = sh;
			for (int i = live.Count - 1; i >= 0; i--)
			{
				CaveLit c = live[i];
				if (c == null) { live.RemoveAt(i); continue; }
				try { c.Light(); }
				catch (Exception e) { if (!told) Debug.LogWarning("[CUSTOM ISLANDS] Cave piece's light: " + e.Message); told = true; }
			}
		}

		void Light()
		{
			if (rends == null || rends.Any(r => r == null))
				rends = GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer)).ToArray();
			foreach (Renderer r in rends)
			{
				if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
				if (r.lightProbeUsage != LightProbeUsage.CustomProvided) r.lightProbeUsage = LightProbeUsage.CustomProvided;
				Material[] mats = r.sharedMaterials;
				if (mats.Length != 1)
					for (int i = 0; i < mats.Length; i++) { r.GetPropertyBlock(block, i); Set(mats[i]); r.SetPropertyBlock(block, i); }
				// (the renderer's own block too: a part drawn without its own block would be lit by nothing)
				r.GetPropertyBlock(block);
				Set(mats.Length == 1 ? mats[0] : null);
				r.SetPropertyBlock(block);
			}
		}

		static void Set(Material m)
		{
			block.CopySHCoefficientArraysFrom(one);
			if (m != null && m.HasProperty("_Smoothness_Multiplyer")) block.SetFloat("_Smoothness_Multiplyer", m.GetFloat("_Smoothness_Multiplyer") * Shine);
		}

		/// <summary>The sky's light made grey (each colour's share by its brightness) and multiplied by warm.</summary>
		public static SphericalHarmonicsL2 GreyWarm(SphericalHarmonicsL2 sh, Color warm)
		{
			for (int c = 0; c < 9; c++)
			{
				float y = 0.3f * sh[0, c] + 0.59f * sh[1, c] + 0.11f * sh[2, c];
				sh[0, c] = y * warm.r; sh[1, c] = y * warm.g; sh[2, c] = y * warm.b;
			}
			return sh;
		}
	}
}
