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
		/// <summary>How far out along its +z a bear den's mouth lies from its origin, at size 1 (m).</summary>
		public static float RoomMouthZ { get { return RoomMax.z; } }
		/// <summary>A lone den's arch at its widest, half across, and its top over the way in's floor, at size 1 (m) - with the
		/// edge noise's most (PlugOpening's ha, hb, archNoise).</summary>
		public static float ArchHalf { get { return HoleHalf * 1.2f * 1.13f; } }
		public static float ArchUp { get { return (HoleUp - HoleBelow) / 2f + (HoleUp + HoleBelow) / 2f * 1.15f * 1.13f; } }

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
		/// <summary>A lone den's wall's outside faces (a child of its wall): drawn as the hill over the den when there is one.</summary>
		public const string PlugOutName = "CavePlugOut";
		/// <summary>How far a lone den's way in is set back into the den at its edge: the wall leans in round it as a funnel.</summary>
		public const float PlugFunnel = 2.2f;
		/// <summary>How far a lone den's wall's outer edge leans out to the mouth: no straight edge of a wall set back behind the hill.</summary>
		public const float PlugLean = 1.6f;
		/// <summary>How much further out than the cells' plane (2 m in from the den's mouth) a lone den's wall is drawn.</summary>
		public const float PlugForward = 1.8f;
		/// <summary>How far behind the hill's skin a lone den's wall's outside is set where the skin is just in front of it (m).</summary>
		public const float SkinCover = 0.35f;
		/// <summary>How far a lone den's wall may stand out of the hill's skin and still be set back under it (WallLikeHill).</summary>
		const float WallProud = 1.2f;
		/// <summary>How far a corner of a lone den's wall that stands out of the hill may still be set back under its skin when the
		/// rest of its triangle already is (WallLikeHill: the torn spikes over Gravewell Shaft's den, 11-21 cv38).</summary>
		const float SpikeProud = 4f;
		/// <summary>How far a lone den's wall's outside faces are turned from straight out toward the hill's own slope at most,
		/// in degrees (WallLikeHill): a wall facing level out drew the hill's dark rock layer and the light of a cliff (cv38).</summary>
		public const float WallTilt = 65f;
		const float FlakeArea = 6f; // (r84: a loose bit of the wall's outside smaller than this, m2, is dropped - WallLikeHill)
		/// <summary>A lone den's way in in shade: its wall's outside round the arch drawn darker, in bands out to the arch's size times
		/// these (the first band darkest, by PlugShade), the rest as the hill. Each band's edge is a smooth, uneven line (the
		/// triangles cut along it): whole 0.5 m cells drew it as a staircase, 11-06 review.</summary>
		public static readonly float[] PlugShadeOut = { 1.45f };
		// (11-08 user: the first band at 0.28 read as a black, notched frame round every den's arch - then a soft fall-off in three
		// bands; 11-09 user: those read as 2-3 stepped rings on the arch's rim, a stair - now one band)
		// (11-21 cv40: not drawn on a hill any more - WallLikeHill draws every band as the hill, a dark panel otherwise)
		public static readonly float[] PlugShade = { 0.86f };
		/// <summary>The name of a lone den's way-in sleeve's holder (CaveVeil): a short rock tunnel, seen from outside only.</summary>
		public const string PlugVeilName = "CavePlugVeil";

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
				// (each cell its own quad on the shared grid, not a run of cells: a run's long edge met the shorter runs over and
				// under it at points along it, and the wall drew thin sky-blue cracks along those rows - Weatherglass Deep's right
				// cliff face from outside, cave round 4 11-20)
				float ya = y0 + k * cell, yb = Mathf.Min(y1, y0 + (k + 1) * cell);
				for (int i = 0; i < nx; i++)
				{
					bool on = false;
					// (the den's own opening - its mouth's outline, where the wall closes it - and the rock round the tunnel's end
					// under cover; each with one cell more round it, its edge in the rock)
					for (int di = -1; di <= 1 && !on; di++)
						for (int dk = -1; dk <= 1 && !on; dk++)
						{
							int a = i + di, b = k + dk;
							if (a >= 0 && b >= 0 && a < nx && b < ny && (opening[a, b] || roofed[i, k] && inside[a, b])) on = true;
						}
					float ax = x0 + i * cell, bx = Mathf.Min(x1, x0 + (i + 1) * cell);
					if (on && bx > hx0 && ax < hx1 && yb > hy0 && ya < hy1) on = false; // (the passage's hole)
					if (!on) continue;
					kept++;
					int n = verts.Count;
					// (each side its own corners, as Plug's: shared ones drew the wall black - 11-03)
					verts.Add(new Vector3(ax, ya, z)); verts.Add(new Vector3(bx, ya, z)); verts.Add(new Vector3(bx, yb, z)); verts.Add(new Vector3(ax, yb, z));
					verts.Add(new Vector3(ax, ya, z)); verts.Add(new Vector3(bx, ya, z)); verts.Add(new Vector3(bx, yb, z)); verts.Add(new Vector3(ax, yb, z));
					tris.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2, n + 4, n + 5, n + 6, n + 4, n + 6, n + 7 });
				}
			}
			var mesh = new Mesh { name = ClippedMesh };
			if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // (a large den's cells, one quad each)
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
			// (and once the hill over it is built: the wall leaves out the cells in the open air, found by what rock is round
			// them - walled before the hill, all the cells by the hill passed as open air, and The Abyss's wall came out
			// different from one load to the next - fix1107)
			CaveMountain over = den != null ? den.GetComponentInParent<CaveMountain>() : null;
			for (float t = 0f; over != null && over.Pending && t < 60f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			if (den == null || !den.activeInHierarchy) yield break;
			try
			{
				PlugOpening(den, CaveMountain.OverMeasured(den.transform, Room).x);
				TintWall(den, props);
				// (its outside as the hill over the den, when the hill is already there; else the hill does it once built)
				CaveMountain hill = den.GetComponentInParent<CaveMountain>();
				if (hill != null && hill.Skin != null) WallLikeHill(den, hill.Skin);
				// (lit as the den round it, the wall's parts with it: CaveLit looked the den's renderers up before the wall came, or the
				// den had none yet, so the wall drew by Raft's blue sky alone - abyss-oldmine, 11-06)
				if (den.transform.Find(PlugName) != null) CaveLit.Mark(den);
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
			if (factor != Color.white)
			{
				CaveMountain.TintRockTextures(wr, factor);
				Transform outT = plug.Find(PlugOutName);
				MeshRenderer or = outT != null ? outT.GetComponent<MeshRenderer>() : null;
				if (or != null && or.GetComponent<HillWall>() == null) CaveMountain.TintRockTextures(or, factor);
			}
			bool reread = matched && LitColour(new Renderer[] { wr }, out after);
			Debug.Log("[CUSTOM ISLANDS] Den's wall coloured as its shell: " + (matched ? "shell " + shell.ToString("0.00") + ", wall " + wall.ToString("0.00") : "colours not read")
				+ ", factor " + factor.ToString("0.00") + (reread ? ", wall now " + after.ToString("0.00") : ""));
			return factor;
		}

		/// <summary>How far the last WallLikeHill turned its wall's outside faces toward the hill's slope, degrees (log, tests).</summary>
		public static float WallTiltSeen;
		/// <summary>How many of the last WallLikeHill's outside points in the hill's opening were brought flush with the skin (log, tests).</summary>
		public static int WallFlushed;
		/// <summary>How many of the last WallLikeHill's outside faces took the texture mapping of the skin behind them, and how many of its corners the skin's own normal (log, tests).</summary>
		public static int WallSkinMapped, WallSkinLit;
		/// <summary>Of WallSkinMapped, the last WallLikeHill's faces in the hill's opening (no skin behind them) mapped as the skin at
		/// the nearest outside point that has it; and its corners there lit by the skin's normals round them (log, tests).</summary>
		public static int WallSkinBorrowed, WallSkinLitRound;
		/// <summary>How many of the last WallLikeHill's outside corners took their texture from the skin's surface straight out from them.</summary>
		public static int WallUvOnSkin;
		/// <summary>The farthest (m, along outward) a den wall's corner's texture is looked up on the skin's surface from it (WallLikeHill).</summary>
		const float WallUvReach = 6f;
		/// <summary>Den wall outside faces left mapped by the world axis they face (no skin anywhere near), all islands since Raft started.</summary>
		public static int WallFacesPlain;
		/// <summary>How far behind a den wall's outside face (m) the hill's skin is looked for to map and light it as the skin (WallLikeHill).</summary>
		const float WallSkinBehind = 2.5f;
		/// <summary>How near the skin (m, either side) a den wall's outside corner takes the skin's own normal (WallLikeHill).</summary>
		const float WallSkinLitNear = 0.75f;

		/// <summary>The skin's texture side for a triangle of it (CaveMountain.Quad: 0 maps (z, y), 1 (x, z), 2 (x, y), in its build
		/// space, over 14 m) found from its corners and their UVs, and the UV offset of that build space; -1 when none fits.</summary>
		static int SkinSide(Vector3 v0, Vector3 v1, Vector3 v2, Vector2 u0, Vector2 u1, Vector2 u2, out Vector2 off)
		{
			int best = -1; float bestErr = float.MaxValue;
			for (int k = 0; k < 3; k++)
			{
				float err = ((SkinSideUv(k, v1 - v0) - (u1 - u0)).magnitude + (SkinSideUv(k, v2 - v0) - (u2 - u0)).magnitude);
				if (err < bestErr) { bestErr = err; best = k; }
			}
			off = best >= 0 ? u0 - SkinSideUv(best, v0) : Vector2.zero;
			return bestErr < 0.02f ? best : -1;
		}

		static Vector2 SkinSideUv(int k, Vector3 p) => (k == 1 ? new Vector2(p.x, p.z) : k == 0 ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y)) / 14f;
		/// <summary>Over how far from the arch's rim (m, across the wall) the wall's points come onto the skin's surface (WallLikeHill).</summary>
		const float WallFlushFade = 1.2f;
		/// <summary>How far from the arch's rim (m) the wall's outside stays exactly where it was, to meet the way in's sleeve (WallLikeHill).</summary>
		const float WallRimKeep = 0.3f;
		/// <summary>How widely (m, a Gaussian's spread) the skin's points round a wall point count in the plane fitted to find the skin's depth there (WallLikeHill).</summary>
		const float WallFitSpread = 2.5f;
		/// <summary>At most this far (m) a wall point is moved onto the skin's fitted surface (WallLikeHill).</summary>
		const float WallFlushMax = 2f;

		/// <summary>
		/// A lone den's wall's outside drawn as the hill over the den (CaveMountain's skin): the same rock, tint and light, its
		/// texture mapped as the skin's (world metres / 14, from the side it faces). Raft's big rock tinted as the den's shell
		/// still read as an orange and black panel in grey rock, framing a lighter square - Shelter Atoll's ammo cave from the
		/// sea, 11-06 review. The inside stays as the den. False when there is no outside or hill.
		/// </summary>
		public static bool WallLikeHill(GameObject den, MeshRenderer skin)
		{
			Transform plug = den != null ? den.transform.Find(PlugName) : null;
			Transform outT = plug != null ? plug.Find(PlugOutName) : null;
			MeshRenderer r = outT != null ? outT.GetComponent<MeshRenderer>() : null;
			MeshFilter mf = outT != null ? outT.GetComponent<MeshFilter>() : null;
			if (r == null || mf == null || mf.sharedMesh == null || skin == null || skin.sharedMaterial == null) return false;
			r.sharedMaterials = Enumerable.Repeat(skin.sharedMaterial, mf.sharedMesh.subMeshCount).ToArray();
			r.shadowCastingMode = ShadowCastingMode.Off; // (r82: no shadow - see PlugOpening)
			r.receiveShadows = skin.receiveShadows;
			r.lightProbeUsage = skin.lightProbeUsage;
			r.reflectionProbeUsage = skin.reflectionProbeUsage;
			var block = new MaterialPropertyBlock();
			skin.GetPropertyBlock(block);
			r.SetPropertyBlock(block);
			// (11-21 cv40: every band as the hill - the darker band round the arch, with the faces' light, drew the whole wall seen
			// through the hill's opening as a dark panel with two spikes over Gravewell Shaft's den; the way in's shade is its sleeve's)
			for (int i = 0; i < r.sharedMaterials.Length; i++) r.SetPropertyBlock(block, i);
			// (where the hill's skin is just in front of the wall or the wall's relief pokes through it, the wall is set back
			// SkinCover m behind the skin: the wall's darker bands showed through the hill's rock in square patches on the arch's
			// pillars, Shelter Atoll and Balboa Remade, 11-08 r26 user)
			MeshCollider skinCol = skin.GetComponent<MeshCollider>();
			Vector3 outward = plug.forward;
			bool nearSkin = false;
			// (11-21 cv36: the ray now reaches as far behind the wall as the wall may stand proud - it stopped SkinCover + 0.2 m
			// behind it, so a wall standing out of the hill by more than 0.55 m was never set back and kept its darker shade
			// band: a dark panel with torn shards over Gravewell Shaft's den mouth)
			// (11-21 cv38: a corner standing out more than WallProud whose triangle's other corners went under the skin is set back
			// too, up to SpikeProud: those triangles stood up out of the hill as torn spikes over Gravewell Shaft's den panel)
			bool moved = false;
			Func<Vector3, float, Vector3> UnderSkin = (w, proud) =>
			{
				RaycastHit hit;
				moved = false;
				if (skinCol == null || !skinCol.Raycast(new Ray(w + outward * 3f, -outward), out hit, 3f + proud + 0.05f)) return w;
				float d = Vector3.Dot(hit.point - w, outward);
				if (!(d > -proud && d < SkinCover)) return w;
				nearSkin = true; moved = true;
				return w + outward * (d - SkinCover);
			};
			Mesh m = mf.sharedMesh;
			// (11-21 cv38: the outside's faces turned from straight out toward the hill's slope round the wall - the mean of the
			// skin's normals behind it, at most WallTilt degrees: the Mountains shader lays its rock layer on steep faces and its
			// sand on flat ones, so a wall facing level out drew as a dark rock panel in the hill's sand round Gravewell Shaft's
			// den, and was lit as a cliff)
			Vector3 skinUp = Vector3.zero;
			if (skinCol != null)
				foreach (Vector3 p in m.vertices)
				{
					RaycastHit sh; Vector3 w = outT.TransformPoint(p);
					if (skinCol.Raycast(new Ray(w + outward * 6f, -outward), out sh, 12f) && sh.normal.y > 0f) skinUp += sh.normal;
				}
			Vector3 faceTo = skinUp.sqrMagnitude > 1e-6f ? Vector3.RotateTowards(outward, skinUp.normalized, WallTilt * Mathf.Deg2Rad, 0f) : outward;
			// (11-21 cv40: every corner lit as that slope, not the wall's own normals turned by it: the funnel round the arch leans
			// in, so its upper faces still faced 26 deg down after the turn and drew darker than the hill round them)
			Vector3 faceLocal = outT.InverseTransformDirection(faceTo).normalized;
			WallTiltSeen = Vector3.Angle(outward, faceTo);
			// (each triangle its own corners, its texture laid along the world axis it faces most: one flat projection for the
			// whole wall stretched the funnel's sides into stripes - the den's "zebra" frame, 11-08 user)
			Vector3[] mv = m.vertices, mn = m.normals;
			var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
			// (11-21 cv44: each face mapped as the skin's triangle behind it maps, in the skin's own space, and each corner at the
			// skin lit by the skin's own normal there: mapped by the side the wall's face looked at most, in world metres, and lit as
			// one slope, it still drew as a panel of smaller, skewed texture with hard edges round Gravewell Shaft's den opening)
			Mesh skm = skinCol != null ? skinCol.sharedMesh : null;
			Vector3[] skv = null, skn = null; Vector2[] sku = null; int[] skt = null;
			if (skm != null && skm.isReadable) { skv = skm.vertices; skn = skm.normals; sku = skm.uv; skt = skm.triangles; }
			bool skinMap = skv != null && sku != null && skn != null && sku.Length == skv.Length && skn.Length == skv.Length && skt != null;
			Transform skT = skinCol != null ? skinCol.transform : null;
			WallSkinMapped = 0; WallSkinLit = 0; WallSkinBorrowed = 0; WallSkinLitRound = 0; WallUvOnSkin = 0;
			// (the skin's normal at a point near it, in the outside's space; null when the skin is not within WallSkinLitNear)
			Func<Vector3, Vector3?> SkinNormal = w =>
			{
				RaycastHit sh;
				if (!skinMap || !skinCol.Raycast(new Ray(w + outward * 3f, -outward), out sh, 3f + WallSkinBehind)) return null;
				if (Mathf.Abs(Vector3.Dot(sh.point - w, outward)) > WallSkinLitNear) return null;
				int ti = sh.triangleIndex * 3;
				if (ti < 0 || ti + 2 >= skt.Length) return null;
				Vector3 bc = sh.barycentricCoordinate;
				Vector3 ln = skn[skt[ti]] * bc.x + skn[skt[ti + 1]] * bc.y + skn[skt[ti + 2]] * bc.z;
				if (ln.sqrMagnitude < 1e-8f) return null;
				Vector3 wn = skT.TransformDirection(ln).normalized;
				if (Vector3.Dot(wn, outward) < 0f) wn = -wn;
				return outT.InverseTransformDirection(wn).normalized;
			};
			// (r84: bits of the outside not joined to the rest - its corners welded by place - and under FlakeArea m2 dropped:
			// they hung in the air off the hill's side as rock flakes, Balboa Remade mouth 1, 11-11 user)
			var weld = new Dictionary<Vector3Int, int>();
			var vid = new int[mv.Length];
			for (int v = 0; v < mv.Length; v++)
			{
				Vector3Int key = Vector3Int.RoundToInt(mv[v] * 50f);
				int id;
				if (!weld.TryGetValue(key, out id)) { id = weld.Count; weld[key] = id; }
				vid[v] = id;
			}
			var root = Enumerable.Range(0, weld.Count).ToArray();
			Func<int, int> find = null;
			find = x => { while (root[x] != x) { root[x] = root[root[x]]; x = root[x]; } return x; };
			for (int s = 0; s < m.subMeshCount; s++)
			{
				int[] st = m.GetTriangles(s);
				for (int t = 0; t + 2 < st.Length; t += 3) { root[find(vid[st[t + 1]])] = find(vid[st[t]]); root[find(vid[st[t + 2]])] = find(vid[st[t]]); }
			}
			var area = new Dictionary<int, float>();
			for (int s = 0; s < m.subMeshCount; s++)
			{
				int[] st = m.GetTriangles(s);
				for (int t = 0; t + 2 < st.Length; t += 3)
				{
					int g = find(vid[st[t]]);
					float ar = 0.5f * Vector3.Cross(outT.TransformVector(mv[st[t + 1]] - mv[st[t]]), outT.TransformVector(mv[st[t + 2]] - mv[st[t]])).magnitude;
					float had; area.TryGetValue(g, out had); area[g] = had + ar;
				}
			}
			float biggest = area.Count > 0 ? area.Values.Max() : 0f;
			var flake = new HashSet<int>(area.Where(kv => kv.Value < FlakeArea && kv.Value < biggest).Select(kv => kv.Key));
			Debug.Log("[CUSTOM ISLANDS] Plug " + den.name + ": outside in " + area.Count + " piece(s), " + flake.Count + " flake(s) dropped (" + string.Join(" ", area.Values.OrderByDescending(x => x).Take(8).Select(x => x.ToString("F1")).ToArray()) + " m2)");
			// (11-21 cv42: the wall's points in the hill's opening, where no skin is in front or behind, brought flush with the skin
			// round them - by the skin's offset at the nearest points that have it, fading out over WallFlushFade into the opening:
			// those round it went SkinCover behind the skin and these stayed, so the opening's edge stood as a rectangular step
			// round Gravewell Shaft's den arch, its sides' texture laid sideways)
			var wv0 = new Vector3[mv.Length]; var skinD = new float[mv.Length];
			var hitIdx = new List<int>();
			// (11-21 cv45: and the skin's texture side and normal where each of those points meets it - for the faces in the
			// opening, which have no skin behind them: mapped by the world axis they faced and lit as one slope, they drew as a
			// lighter panel of stretched ripples with straight seams and faceted shards round Gravewell Shaft's den arch)
			var hitSide = new int[mv.Length]; var hitOff = new Vector2[mv.Length]; var hitN = new Vector3[mv.Length];
			for (int v = 0; v < mv.Length; v++)
			{
				wv0[v] = outT.TransformPoint(mv[v]); skinD[v] = float.NaN;
				RaycastHit sh;
				if (skinCol != null && skinCol.Raycast(new Ray(wv0[v] + outward * 3f, -outward), out sh, 3f + SpikeProud + 0.05f))
				{
					float d = Vector3.Dot(sh.point - wv0[v], outward);
					if (d > -SpikeProud && d < SkinCover + WallProud)
					{
						skinD[v] = d; hitIdx.Add(v); hitSide[v] = -1;
						int ti = sh.triangleIndex * 3;
						if (skinMap && ti >= 0 && ti + 2 < skt.Length)
						{
							hitSide[v] = SkinSide(skv[skt[ti]], skv[skt[ti + 1]], skv[skt[ti + 2]], sku[skt[ti]], sku[skt[ti + 1]], sku[skt[ti + 2]], out hitOff[v]);
							Vector3 bc = sh.barycentricCoordinate;
							Vector3 ln = skn[skt[ti]] * bc.x + skn[skt[ti + 1]] * bc.y + skn[skt[ti + 2]] * bc.z;
							if (ln.sqrMagnitude > 1e-8f)
							{
								Vector3 wn = skT.TransformDirection(ln).normalized;
								if (Vector3.Dot(wn, outward) < 0f) wn = -wn;
								hitN[v] = outT.InverseTransformDirection(wn).normalized;
							}
						}
					}
				}
			}
			// (11-21 cv43: onto the skin's surface itself, not moved by its offset: shifted as a whole the wall kept its own relief, and
			// the pillars either side of Gravewell Shaft's arch still stood as a 0.5-0.9 m step - CavePlugOut faces turned sideways.
			// The skin's depth at each point from a plane fitted to the skin points round it (WallFitSpread); only the arch's own
			// rim - the outside's open edge in the opening - stays where it meets the way in's sleeve, blending over WallFlushFade)
			var flush = new float[mv.Length];
			// (11-21 cv47: how far along outward each point in the opening is from the fitted skin, before the rim keeps it and the
			// clamp - its texture is laid as at that place on the skin, below)
			var fitD = new float[mv.Length];
			for (int v = 0; v < mv.Length; v++) fitD[v] = float.NaN;
			int flushed = 0; float flushMax = 0f;
			Vector3 right = Vector3.Cross(Vector3.up, outward);
			if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(Vector3.forward, outward);
			right.Normalize();
			Vector3 upIn = Vector3.Cross(outward, right).normalized;
			var edgeUse = new Dictionary<long, int>();
			for (int s = 0; s < m.subMeshCount; s++)
			{
				int[] st = m.GetTriangles(s);
				for (int t = 0; t + 2 < st.Length; t += 3)
				{
					if (flake.Contains(find(vid[st[t]]))) continue;
					for (int k = 0; k < 3; k++)
					{
						int p0 = vid[st[t + k]], p1 = vid[st[t + (k + 1) % 3]];
						if (p0 == p1) continue;
						long key = p0 < p1 ? ((long)p0 << 32) | (uint)p1 : ((long)p1 << 32) | (uint)p0;
						int had; edgeUse.TryGetValue(key, out had); edgeUse[key] = had + 1;
					}
				}
			}
			var openEdge = new HashSet<int>();
			foreach (var kv in edgeUse) if (kv.Value == 1) { openEdge.Add((int)(kv.Key >> 32)); openEdge.Add((int)(kv.Key & 0xffffffffL)); }
			var rimIdx = new List<int>();
			for (int v = 0; v < mv.Length; v++) if (float.IsNaN(skinD[v]) && openEdge.Contains(vid[v])) rimIdx.Add(v);
			if (hitIdx.Count >= 3)
				for (int v = 0; v < mv.Length; v++)
				{
					if (!float.IsNaN(skinD[v])) continue;
					float pu = Vector3.Dot(wv0[v], right), pw = Vector3.Dot(wv0[v], upIn);
					double s0 = 0, su = 0, sw = 0, suu = 0, suw = 0, sww = 0, sd = 0, sud = 0, swd = 0;
					foreach (int hv in hitIdx)
					{
						float du = Vector3.Dot(wv0[hv], right) - pu, dw = Vector3.Dot(wv0[hv], upIn) - pw;
						double wt = System.Math.Exp(-(du * du + dw * dw) / (2.0 * WallFitSpread * WallFitSpread));
						if (wt < 1e-6) continue;
						double dep = Vector3.Dot(wv0[hv], outward) + skinD[hv];
						s0 += wt; su += wt * du; sw += wt * dw; suu += wt * du * du; suw += wt * du * dw; sww += wt * dw * dw;
						sd += wt * dep; sud += wt * du * dep; swd += wt * dw * dep;
					}
					if (s0 < 1e-4) continue;
					// (a + b du + c dw through the weighted points: the plane's height at this point is a)
					double det = s0 * (suu * sww - suw * suw) - su * (su * sww - suw * sw) + sw * (su * suw - suu * sw);
					double at = sd / s0;
					if (System.Math.Abs(det) > 1e-6 * s0 * s0 * s0)
						at = (sd * (suu * sww - suw * suw) - su * (sud * sww - suw * swd) + sw * (sud * suw - suu * swd)) / det;
					float rim = float.MaxValue;
					foreach (int rv in rimIdx) rim = Mathf.Min(rim, (Vector3.ProjectOnPlane(wv0[rv] - wv0[v], outward)).magnitude);
					float keep = rimIdx.Count == 0 ? 1f : Mathf.Clamp01((rim - WallRimKeep) / WallFlushFade);
					fitD[v] = (float)at - Vector3.Dot(wv0[v], outward);
					flush[v] = Mathf.Clamp(fitD[v], -WallFlushMax, WallFlushMax) * keep;
					if (Mathf.Abs(flush[v]) > 0.02f) { flushed++; flushMax = Mathf.Max(flushMax, Mathf.Abs(flush[v])); }
				}
			WallFlushed = flushed;
			Debug.Log("[CUSTOM ISLANDS] Plug " + den.name + ": " + hitIdx.Count + " of " + mv.Length + " outside point(s) with the hill's skin in line, skin offset "
				+ (hitIdx.Count > 0 ? hitIdx.Min(v => skinD[v]).ToString("0.00") + ".." + hitIdx.Max(v => skinD[v]).ToString("0.00") : "-") + " m; " + flushed + " in its opening brought onto the skin's surface (up to " + flushMax.ToString("0.00") + " m), arch rim " + rimIdx.Count + " point(s)");
			// (the outside point nearest a world point, across the wall, that met the skin with a texture side; -1 when none)
			var sideIdx = hitIdx.Where(v => hitSide[v] >= 0).ToList();
			Func<Vector3, int> NearestSide = w =>
			{
				int best = -1; float bd = float.MaxValue;
				foreach (int hv in sideIdx)
				{
					float dd = Vector3.ProjectOnPlane(wv0[hv] - w, outward).sqrMagnitude;
					if (dd < bd) { bd = dd; best = hv; }
				}
				return best;
			};
			// (the skin's normals at the outside points round a world point, weighted by WallFitSpread across the wall; null when none)
			var normIdx = hitIdx.Where(v => hitN[v] != Vector3.zero).ToList();
			Func<Vector3, Vector3?> NormalRound = w =>
			{
				Vector3 sum = Vector3.zero; double wsum = 0;
				foreach (int hv in normIdx)
				{
					float dd = Vector3.ProjectOnPlane(wv0[hv] - w, outward).sqrMagnitude;
					double wt = System.Math.Exp(-dd / (2.0 * WallFitSpread * WallFitSpread));
					if (wt < 1e-6) continue;
					sum += hitN[hv] * (float)wt; wsum += wt;
				}
				if (wsum < 1e-4 || sum.sqrMagnitude < 1e-8f) return null;
				return sum.normalized;
			};
			var subs = new List<int>[m.subMeshCount];
			for (int s = 0; s < subs.Length; s++)
			{
				int[] st = m.GetTriangles(s);
				subs[s] = new List<int>(st.Length);
				for (int t = 0; t + 2 < st.Length; t += 3)
				{
					if (flake.Contains(find(vid[st[t]]))) continue;
					nearSkin = false;
					Vector3 a0 = wv0[st[t]] + outward * flush[st[t]], b0 = wv0[st[t + 1]] + outward * flush[st[t + 1]], c0 = wv0[st[t + 2]] + outward * flush[st[t + 2]];
					Vector3 a = UnderSkin(a0, WallProud); bool am = moved;
					Vector3 b = UnderSkin(b0, WallProud); bool bm = moved;
					Vector3 c = UnderSkin(c0, WallProud); bool cm = moved;
					if ((am || bm || cm) && !(am && bm && cm))
					{
						if (!am) a = UnderSkin(a0, SpikeProud);
						if (!bm) b = UnderSkin(b0, SpikeProud);
						if (!cm) c = UnderSkin(c0, SpikeProud);
					}
					Vector3 fn = Vector3.Cross(b - a, c - a);
					float fx = Mathf.Abs(fn.x), fy = Mathf.Abs(fn.y), fz = Mathf.Abs(fn.z);
					int ax = fy >= fx && fy >= fz ? 1 : fx >= fz ? 0 : 2;
					// (a triangle at the skin drawn plain, as the hill: where it still pokes through between its corners its
					// shade band showed as a square patch, r30)
					int into = nearSkin ? 0 : s;
					int side = -1; Vector2 sideOff = Vector2.zero;
					if (skinMap)
					{
						RaycastHit gh; Vector3 g = (a + b + c) / 3f;
						if (skinCol.Raycast(new Ray(g + outward * 3f, -outward), out gh, 3f + WallSkinBehind) && gh.triangleIndex >= 0 && gh.triangleIndex * 3 + 2 < skt.Length)
						{
							int ti = gh.triangleIndex * 3;
							side = SkinSide(skv[skt[ti]], skv[skt[ti + 1]], skv[skt[ti + 2]], sku[skt[ti]], sku[skt[ti + 1]], sku[skt[ti + 2]], out sideOff);
						}
					}
					if (side < 0 && skinMap)
					{
						int hv = NearestSide((a + b + c) / 3f);
						if (hv >= 0) { side = hitSide[hv]; sideOff = hitOff[hv]; WallSkinBorrowed++; }
					}
					if (side >= 0) WallSkinMapped++;
					else WallFacesPlain++;
					foreach (int k in new[] { 0, 1, 2 })
					{
						int vi = st[t + k];
						Vector3 w = k == 0 ? a : k == 1 ? b : c;
						subs[into].Add(nv.Count);
						nv.Add(outT.InverseTransformPoint(w));
						Vector3? sn = SkinNormal(w);
						if (sn.HasValue) WallSkinLit++;
						else { sn = NormalRound(w); if (sn.HasValue) WallSkinLitRound++; }
						nn.Add(sn ?? faceLocal);
						// (11-21 cv47: the texture laid as at the skin's surface straight out from the corner, not at the corner
						// itself: the funnel round the arch, kept where it meets the way in's sleeve, leans in, and the skin's flat
						// projection laid on it drew a lighter panel of stretched ripples with straight seams either side of Gravewell
						// Shaft's den arch - CIDenWallLook seams 42/48 px, cv46)
						Vector3 uw = !float.IsNaN(fitD[vi]) ? wv0[vi] + outward * Mathf.Clamp(fitD[vi], -WallUvReach, WallUvReach)
							: !float.IsNaN(skinD[vi]) ? wv0[vi] + outward * skinD[vi] : w;
						if (uw != w) WallUvOnSkin++;
						nu.Add(side >= 0 ? SkinSideUv(side, skT.InverseTransformPoint(uw)) + sideOff
							: (ax == 0 ? new Vector2(w.z, w.y) : ax == 1 ? new Vector2(w.x, w.z) : new Vector2(w.x, w.y)) / 14f);
					}
				}
			}
			m.Clear();
			if (nv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
			m.SetVertices(nv);
			m.SetNormals(nn);
			m.SetUVs(0, nu);
			Debug.Log("[CUSTOM ISLANDS] Plug " + den.name + ": " + WallSkinMapped + " outside face(s) mapped as the skin behind them (" + WallSkinBorrowed + " in the opening as the skin nearest them), " + WallSkinLit + " corner(s) lit by the skin's normal, " + WallSkinLitRound + " by the skin's normals round them; " + WallUvOnSkin + " corner(s) textured as the skin straight out from them");
			m.subMeshCount = subs.Length;
			for (int s = 0; s < subs.Length; s++) m.SetTriangles(subs[s], s);
			m.RecalculateBounds();
			if (r.GetComponent<HillWall>() == null) r.gameObject.AddComponent<HillWall>();
			// (and the way in's sleeve in the hill's rock too)
			Transform vt = plug.Find(PlugVeilName);
			SleeveShade ss = vt != null ? vt.GetComponentInChildren<SleeveShade>(true) : null;
			MeshRenderer sr = ss != null ? ss.GetComponent<MeshRenderer>() : null;
			if (sr != null)
			{
				sr.sharedMaterials = Enumerable.Repeat(skin.sharedMaterial, sr.sharedMaterials.Length).ToArray();
				sr.receiveShadows = skin.receiveShadows;
				sr.reflectionProbeUsage = skin.reflectionProbeUsage;
				sr.SetPropertyBlock(block);
				// (its rock darker the further in, as the wall's shade bands: the light alone left it a lit, closed door, r30)
				for (int i = 0; i < sr.sharedMaterials.Length; i++)
					sr.SetPropertyBlock(i < CaveVeil.Dark.Length ? Darker(sr, skin.sharedMaterial, CaveVeil.Dark[i]) : block, i);
			}
			CaveLit.Mark(den);
			Debug.Log("[CUSTOM ISLANDS] Den's wall outside drawn as the hill: " + skin.sharedMaterial.name + ", its faces turned " + WallTiltSeen.ToString("0") + " deg up the hill's slope");
			return true;
		}

		/// <summary>
		/// A tunnel's mouth frames (MouthName) drawn as the hill over it (CaveMountain's skin): its rock, tint and light, the
		/// texture laid as the skin's (world metres / 14, along the axis each triangle faces most). In the tunnel's own pale ravine
		/// rock the frame's doorstep and top stood out of the hill's grey face as a white slab and steps, and as a pale panel
		/// beside the tunnel - Old Mine Islet and Gilded Skull Cove (11-19 cave1119 FO-G4). The collider keeps its mesh.
		/// Returns how many frames were redrawn.
		/// </summary>
		public static int FramesLikeHill(GameObject piece, MeshRenderer skin)
		{
			if (piece == null || skin == null || skin.sharedMaterial == null) return 0;
			int done = 0;
			liftLand = null; liftSolid = null; // (the ground's holes read fresh: a dig may have cut it since)
			foreach (Transform c in piece.transform)
			{
				if (c.name != MouthName) continue;
				MeshRenderer r = c.GetComponent<MeshRenderer>();
				MeshFilter mf = c.GetComponent<MeshFilter>();
				if (r == null || mf == null || mf.sharedMesh == null || r.GetComponent<HillWall>() != null) continue;
				Mesh src = mf.sharedMesh;
				Vector3[] mv = src.vertices, mn = src.normals;
				int[] st = src.triangles;
				// (round 4: a frame sunk 0-0.5 m under the ground at the mouth is lifted onto it: the cut ground over it showed through in
				// patches, and where the ground was cut away it stood as a sheet a hand under the cutting's floor - Old Mine Islet,
				// Sinkmarket Floor)
				Vector3[] wv = new Vector3[mv.Length], gn = new Vector3[mv.Length];
				bool hasN = mn != null && mn.Length == mv.Length;
				for (int i = 0; i < mv.Length; i++) wv[i] = c.TransformPoint(mv[i]);
				// (11-21 cv42: each point's faces' own normals, by their winding as drawn, beside the mesh's normals - FrameCovered
				// looks along both; and the points next to one laid out, looked for further, so the faces between don't dive
				// under the cutting wall just inside the ring - Sinkmarket Floor)
				var wn = new Vector3[mv.Length];
				for (int t = 0; t + 2 < st.Length; t += 3)
				{
					Vector3 fw = Vector3.Cross(wv[st[t + 1]] - wv[st[t]], wv[st[t + 2]] - wv[st[t]]);
					for (int k = 0; k < 3; k++) wn[st[t + k]] += fw;
				}
				// (11-21 cv49: the points of the faces turned up - the faces that lie along the ground and can show through it. A
				// point only on faces turned down or aside - the sill's underside, 0.6 m under its top - never shows: putting the
				// ground back over those, then lifting them onto it, laid the ground flat in Sinkmarket Floor's frame faces, 590
				// rays, CIFrameRing, and squares of it stood up through the mouth's floor)
				bool[] up = FrameUpPoints(wv, st);
				var laid = new bool[mv.Length];
				for (int i = 0; i < mv.Length; i++)
				{
					Vector3 was = wv[i];
					wv[i] = OnGround(was, hasN ? c.TransformDirection(mn[i]) : Vector3.zero, wn[i], FrameCover, out gn[i]);
					laid[i] = gn[i] != Vector3.zero;
				}
				for (int pass = 0; pass < 3; pass++)
				{
					var next = new bool[mv.Length];
					for (int t = 0; t + 2 < st.Length; t += 3)
						if (laid[st[t]] || laid[st[t + 1]] || laid[st[t + 2]])
							for (int k = 0; k < 3; k++) if (!laid[st[t + k]]) next[st[t + k]] = true;
					int more = 0;
					for (int i = 0; i < mv.Length; i++)
					{
						if (!next[i]) continue;
						Vector3 back = c.TransformPoint(mv[i]);
						Vector3 moved = OnGround(back, hasN ? c.TransformDirection(mn[i]) : Vector3.zero, wn[i], FrameCoverNext, out gn[i]);
						if (gn[i] == Vector3.zero) continue;
						wv[i] = moved; laid[i] = true; more++;
					}
					if (more == 0) break;
				}
				// (11-21 cv45: and the ground under its faces let down off them - Sinkmarket's cutting walls 1 cm behind its mouth's
				// side walls showed through as a dark sawtooth)
				GroundSunkUnderFrames += SinkGroundUnder(wv, st, up);
				// (11-21 cv47: and any point still just under the ground after that lifted onto it - 12 of Sinkmarket Floor's showed
				// through its frame, CIFrameSunk)
				for (int i = 0; i < mv.Length; i++)
				{
					float g; Vector3 n;
					if (!up[i] || !FrameLiftable(wv[i], out g, out n)) continue;
					wv[i] = OnGround(wv[i], Vector3.zero, Vector3.zero, 0f, out gn[i]);
					FrameRelifted++;
				}
				// (11-21 cv52: and the frame's rim tucked under the ground - where a face turned aside goes on down into the ground
				// from points laid just over it, it stood 3-10 cm out of Overpass Shoal's channel floor all along its mouth's apron,
				// black dashes where it faced away from the light)
				// (11-21 cv54: which points were tucked kept on the frame - CIFrameSunk and CIFrameRim read them, not guessed again from
				// the finished mesh: the tuck lowers the rim, and the guess lost 897 of Overpass Shoal's, 1104 of Sinkmarket Floor's)
				bool[] tucked = TuckRim(wv, st);
				var rimOut = new List<bool>(st.Length);
				var nv =new List<Vector3>(st.Length); var nn = new List<Vector3>(st.Length); var nu = new List<Vector2>(st.Length); var tri = new List<int>(st.Length);
				for (int t = 0; t + 2 < st.Length; t += 3)
				{
					Vector3 a = wv[st[t]], b = wv[st[t + 1]], d = wv[st[t + 2]];
					// (11-21 cv54: and a face lying flat a hand or less over flat ground left out - the ground drawn there is the floor:
					// Sinkmarket Floor's mouth floor plate 4 cm over its sand stood as a flat golden plate in front of the camera, and
					// 584 rays of the ground fighting through faces lying along it, CIFrameRing)
					if (FlatOnGround(a, b, d)) { FrameFlatLeft++; continue; }
					Vector3 fn = Vector3.Cross(b - a, d - a);
					float fx = Mathf.Abs(fn.x), fy = Mathf.Abs(fn.y), fz = Mathf.Abs(fn.z);
					int ax = fy >= fx && fy >= fz ? 1 : fx >= fz ? 0 : 2;
					for (int k = 0; k < 3; k++)
					{
						int vi = st[t + k];
						Vector3 w = k == 0 ? a : k == 1 ? b : d;
						tri.Add(nv.Count);
						nv.Add(c.InverseTransformPoint(w));
						rimOut.Add(tucked[vi]);
						// (11-21 cv47: never a normal turned against its face as now drawn - a point moved onto the ground keeps its
						// mesh normal, which could face away from the light: black triangles in Sinkmarket Floor's frame)
						Vector3 vn = gn[vi] != Vector3.zero ? c.InverseTransformDirection(gn[vi]) : hasN ? mn[vi] : Vector3.zero;
						Vector3 fl = c.InverseTransformDirection(fn).normalized;
						if (vn.sqrMagnitude < 1e-6f || Vector3.Dot(vn, fl) < 0.2f) { vn = fl; FrameNormalsFlipped++; }
						nn.Add(vn);
						nu.Add((ax == 0 ? new Vector2(w.z, w.y) : ax == 1 ? new Vector2(w.x, w.z) : new Vector2(w.x, w.y)) / 14f);
					}
				}
				var m = new Mesh { name = MouthName + " (as the hill)" };
				if (nv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
				m.SetVertices(nv);
				m.SetNormals(nn);
				m.SetUVs(0, nu);
				m.SetTriangles(tri, 0);
				m.RecalculateBounds();
				try { m.RecalculateTangents(); } catch { }
				mf.sharedMesh = m;
				r.sharedMaterials = new[] { skin.sharedMaterial };
				r.receiveShadows = skin.receiveShadows;
				r.lightProbeUsage = skin.lightProbeUsage;
				r.reflectionProbeUsage = skin.reflectionProbeUsage;
				var block = new MaterialPropertyBlock();
				skin.GetPropertyBlock(block);
				// (11-21 cv47: and its main normal map flat: Balboa's baked one belongs to its own rocks' uvs - laid on ours, each
				// triangle along its own axis, it tipped patches away from the light, black triangles all over Sinkmarket Floor's
				// frame; the hill's skin turned up hides it, a frame's faces turned to the side don't)
				FlatMainNormal(block, skin.sharedMaterial);
				r.SetPropertyBlock(block);
				r.SetPropertyBlock(block, 0);
				// (round 4: and its rock layer on every face: Balboa's sand layer, drawn on faces turned up, laid the frame's steep
				// cutting walls pale - Old Mine Islet, Sinkmarket Floor; the hill's own steep faces are its rock layer - one rock)
				DigTunnel.SideRockOnly(r, 0);
				r.gameObject.AddComponent<HillWall>().Rim = rimOut.ToArray();
				done++;
			}
			// (and the tunnel itself, inside and its back, in the hill's rock: its pale blue ice-ravine rock seen through the mouth
			// in the hill's grey face filled the lower half of the opening as a pale bluish shape - Old Mine Islet 11-19 cave1119 r2.
			// Its mesh can't be read, so the rock is laid by the tunnel's own texture coordinates)
			// (a tunnel piece with no frame - the middle of a passage, a swim-through placed on its own - in the hill's rock too where
			// it is in the pale ice-ravine rock: through the mouth its pale blue ring framed the opening, Sinkmarket Floor, round 3)
			bool ice = done == 0 && piece.name.StartsWith("TP_UnderwaterCaveTunnel");
			if (done > 0 || ice)
			{
				var body = new List<MeshRenderer>();
				MeshRenderer own = piece.GetComponent<MeshRenderer>();
				if (own != null) body.Add(own);
				foreach (Transform c in piece.transform)
					// (and its join plates, the floor strips where it meets the next piece: left in the ice-ravine rock, pale strips
					// across the floor of a tunnel in the hill's rock - Sinkmarket Floor, round 3)
					if (c.name == CaveBacks.Name || c.name == JoinName) { MeshRenderer br = c.GetComponent<MeshRenderer>(); if (br != null) body.Add(br); }
				foreach (MeshRenderer r in body)
				{
					if (r.GetComponent<HillWall>() != null || r.sharedMaterial == skin.sharedMaterial) continue;
					if (ice && !r.sharedMaterials.Any(x => x != null && x.name.Contains(IceRock))) continue;
					r.sharedMaterials = Enumerable.Repeat(skin.sharedMaterial, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
					r.receiveShadows = skin.receiveShadows;
					r.reflectionProbeUsage = skin.reflectionProbeUsage;
					var block = new MaterialPropertyBlock();
					skin.GetPropertyBlock(block);
					FlatMainNormal(block, skin.sharedMaterial);
					for (int i = 0; i < r.sharedMaterials.Length; i++) r.SetPropertyBlock(block, i);
					r.SetPropertyBlock(block);
					// (11-21 cv47: and the rock layer on its faces turned up too, as the frame's: the join plates and the tunnel's floor
					// wore Balboa's sand layer, a golden band in Sinkmarket Floor's mouth and a golden plate)
					for (int i = 0; i < r.sharedMaterials.Length; i++) DigTunnel.SideRockOnly(r, i);
					r.gameObject.AddComponent<HillWall>();
					TunnelsLikeHill++;
				}
				CaveLit.Mark(piece);
			}
			return done;
		}
		/// <summary>Mouth frame points lifted onto the ground after SinkGroundUnder (FramesLikeHill), all islands since Raft started.</summary>
		public static int FrameRelifted;
		/// <summary>Mouth frame corners whose normal was turned against their face as drawn, given the face's (FramesLikeHill).</summary>
		public static int FrameNormalsFlipped;

		/// <summary>A block's main normal map (Balboa's Mountains, _MainNormal) set flat, when the material has one.</summary>
		public static void FlatMainNormal(MaterialPropertyBlock b, Material m)
		{
			if (b != null && m != null && m.HasProperty(MainNormal)) b.SetTexture(MainNormal, Texture2D.normalTexture);
		}

		/// <summary>Balboa's Mountains shader's baked main normal map (FlatMainNormal; CIFrameRing checks the frames have it flat).</summary>
		public const string MainNormal = "_MainNormal";

		/// <summary>How far under a mouth frame's face turned up (m, across it) FramesLikeHill keeps the island ground standing within
		/// FrameSinkBand of it: the heightmap's triangles bulged between the frame's laid points and stood 1 cm off its faces on
		/// Sinkmarket Floor's 54 degree cutting walls, a dark sawtooth on both side walls of its mouth (CIFrameRing, 11-21 cv45).</summary>
		public const float FrameGroundUnder = 0.1f;
		/// <summary>A frame face turned up at least this much (its normal's y) lies along the ground: SinkGroundUnder lets the ground
		/// down under it, and its points are the ones that can show through the ground (FrameUpPoints).</summary>
		public const float FrameUpFace = 0.2f;

		/// <summary>Which of a mouth frame's points (world points w, triangles st as drawn) are on a face turned up (FrameUpFace):
		/// the ones the ground can show through. FramesLikeHill and CIFrameSunk.</summary>
		public static bool[] FrameUpPoints(Vector3[] w, int[] st)
		{
			var up = new bool[w.Length];
			for (int t = 0; t + 2 < st.Length; t += 3)
			{
				Vector3 n = Vector3.Cross(w[st[t + 1]] - w[st[t]], w[st[t + 2]] - w[st[t]]);
				if (n.sqrMagnitude < 1e-10f || n.normalized.y < FrameUpFace) continue;
				up[st[t]] = up[st[t + 1]] = up[st[t + 2]] = true;
			}
			return up;
		}

		/// <summary>A frame point standing up to this (m, straight up) over the ground, on a face turned aside that goes on down into
		/// the ground, is the frame's rim: TuckRim lays it under the ground. CIFrameRim fails on rim faces still standing out this
		/// little.</summary>
		public const float FrameRimShow = 0.15f;
		/// <summary>How far under the ground (m, straight down) TuckRim lays a rim point.</summary>
		public const float FrameRimUnder = 0.05f;
		/// <summary>Mouth frame rim points laid under the ground (TuckRim), all islands since Raft started.</summary>
		public static int FrameRimTucked;

		/// <summary>A frame face crossing the ground at a steeper angle than this (the dot of its normal and the ground's; 0.5 = 60
		/// degrees) is a side wall for the rim (FrameAside): Overpass Shoal's apron's side wall faces stood at 0.13-0.20 on its flat
		/// channel floor. (11-21 cv54: by the ground's own slope, not straight up - by the face's steepness alone the faces lying
		/// along the steep cutting walls counted as side walls, 1121 of Overpass Shoal's points and 1312 of Sinkmarket Floor's
		/// were tucked under the ground, and CIFrameRim failed on their 3-5 cm.)</summary>
		public const float FrameRimCross = 0.5f;

		/// <summary>Whether a face (world points a b c) is turned aside: crossing the ground (FrameRimCross) where the ground lies
		/// no steeper than FrameCoverSteep, not turned down into it. False where there is no island ground under it.</summary>
		public static bool FrameAside(Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 n = Vector3.Cross(b - a, c - a);
			if (n.sqrMagnitude < 1e-10f) return false;
			Vector3 gn;
			if (!GroundNormalAt((a + b + c) / 3f, out gn) || gn.y < FrameCoverSteep) return false;
			float d = Vector3.Dot(n.normalized, gn);
			return d < FrameRimCross && d >= -0.3f;
		}

		/// <summary>The island ground's normal under a world point, where there is island ground.</summary>
		public static bool GroundNormalAt(Vector3 w, out Vector3 gn)
		{
			gn = Vector3.up;
			Terrain t = LiftTerrain(w);
			if (t == null) return false;
			TerrainData d = t.terrainData;
			Vector3 o = t.transform.position;
			gn = d.GetInterpolatedNormal((w.x - o.x) / d.size.x, (w.z - o.z) / d.size.z);
			return true;
		}

		/// <summary>
		/// A mouth frame's rim (world points wv, triangles st as drawn) laid FrameRimUnder under the ground: each point standing
		/// 0-FrameRimShow over solid ground on a face turned aside (FrameAside) whose lowest point lies under solid ground. The
		/// frame's points are laid FrameOver over the ground and the ground under its faces let down; where a side wall goes on
		/// down into the ground from there, that strip of it stood out of the ground along the frame's edge. Returns which points
		/// were laid under (and counts them in FrameRimTucked).
		/// </summary>
		static bool[] TuckRim(Vector3[] wv, int[] st)
		{
			var tuck = new bool[wv.Length];
			var to = new float[wv.Length];
			var h = new float[3];
			for (int t = 0; t + 2 < st.Length; t += 3)
			{
				if (!FrameAside(wv[st[t]], wv[st[t + 1]], wv[st[t + 2]])) continue;
				bool solid = true; float low = float.MaxValue;
				for (int k = 0; k < 3; k++)
				{
					float g;
					if (!SolidGroundAt(wv[st[t + k]], out g)) { solid = false; break; }
					h[k] = wv[st[t + k]].y - g;
					low = Mathf.Min(low, h[k]);
				}
				if (!solid || low > -FrameRimIn) continue;
				for (int k = 0; k < 3; k++)
				{
					int v = st[t + k];
					if (h[k] < -0.01f || h[k] > FrameRimShow) continue;
					tuck[v] = true; to[v] = wv[v].y - h[k] - FrameRimUnder;
				}
			}
			for (int v = 0; v < wv.Length; v++) if (tuck[v]) { wv[v].y = to[v]; FrameRimTucked++; }
			return tuck;
		}

		/// <summary>A rim face's lowest point lies at least this far (m, straight down) under the ground: it goes on down into it.</summary>
		public const float FrameRimIn = 0.02f;

		/// <summary>The island ground's height under a world point, where the ground is there (not a hole cut in it).</summary>
		public static bool SolidGroundAt(Vector3 w, out float g)
		{
			g = 0f;
			Terrain t = LiftTerrain(w);
			if (t == null) return false;
			g = t.SampleHeight(w) + t.transform.position.y;
			return SolidAt(t, w);
		}

		/// <summary>The terrain under a world point (liftLand), its holes read into liftSolid.</summary>
		static Terrain LiftTerrain(Vector3 w)
		{
			Terrain t = liftLand;
			if (t != null && Over(t, w)) return t;
			t = null; liftSolid = null;
			foreach (Terrain x in Terrain.activeTerrains) if (x != null && x.terrainData != null && Over(x, w)) { t = x; break; }
			liftLand = t;
			if (t == null) return null;
			int hr = t.terrainData.holesResolution;
			liftSolid = t.terrainData.GetHoles(0, 0, hr, hr);
			return t;
		}

		/// <summary>Whether the LiftTerrain t has ground at w (no hole cut there).</summary>
		static bool SolidAt(Terrain t, Vector3 w)
		{
			TerrainData d = t.terrainData;
			Vector3 o = t.transform.position;
			int res = liftSolid.GetLength(0);
			int cx = Mathf.Clamp((int)((w.x - o.x) / d.size.x * res), 0, res - 1), cz = Mathf.Clamp((int)((w.z - o.z) / d.size.z * res), 0, res - 1);
			return liftSolid[cz, cx];
		}

		/// <summary>Which of a finished mouth frame's points (its mesh's points, n of them) TuckRim laid under the ground as its rim,
		/// as FramesLikeHill kept them on the frame (HillWall.Rim). CIFrameSunk leaves these; CIFrameRim counts them.</summary>
		public static bool[] FrameRimPoints(HillWall h, int n)
		{
			return h != null && h.Rim != null && h.Rim.Length == n ? h.Rim : new bool[n];
		}

		/// <summary>A frame face lying along flat ground no more than this (m, straight up) over it all over is left out of the
		/// frame (FlatOnGround): the ground there is the floor.</summary>
		public const float FrameFlatOn = 0.06f;
		/// <summary>The ground under a face FlatOnGround leaves out lies at least this flat (its normal's y; 0.85 = 32 degrees).</summary>
		public const float FrameFlatGround = 0.85f;
		/// <summary>Mouth frame faces left out as lying flat on the ground (FlatOnGround), all islands since Raft started.</summary>
		public static int FrameFlatLeft;

		/// <summary>Whether a frame face (world points a b c) lies along flat solid ground (FrameFlatGround), its normal within 25
		/// degrees of the ground's, its corners and middle 1 cm under to FrameFlatOn over it: the ground drawn there already is the
		/// floor, and the face only fights it or stands on it as a plate of the frame's own texture.</summary>
		public static bool FlatOnGround(Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 n = Vector3.Cross(b - a, c - a);
			if (n.sqrMagnitude < 1e-10f) return false;
			n.Normalize();
			if (n.y < FrameFlatGround) return false;
			Vector3 m = (a + b + c) / 3f, gn;
			if (!GroundNormalAt(m, out gn) || gn.y < FrameFlatGround || Vector3.Dot(n, gn) < 0.9f) return false;
			foreach (Vector3 p in new[] { a, b, c, m })
			{
				float g;
				if (!SolidGroundAt(p, out g) || p.y < g - 0.01f || p.y > g + FrameFlatOn) return false;
			}
			return true;
		}
		/// <summary>Ground over a frame face by more than this (m, across it) is the frame under the hill on purpose: left.</summary>
		public const float FrameSinkBand = 0.25f;
		/// <summary>The most a heightmap point is let down under a frame face (m, straight down).</summary>
		public const float FrameSinkMost = 1f;
		/// <summary>Heightmap points let down under mouth frames (SinkGroundUnder), all islands since Raft started.</summary>
		public static int GroundSunkUnderFrames;
		/// <summary>Heightmap points SinkGroundUnder put back because letting them down laid bare a frame point buried on purpose.</summary>
		public static int FrameSinkKeptBack;

		/// <summary>
		/// The island ground under a mouth frame's faces turned up (world triangles wv/st, as drawn) let down to FrameGroundUnder
		/// under each face, measured across it, where it stands within FrameSinkBand over it or less than FrameGroundUnder under
		/// it - the frame's laid points sit on the ground, but between them the ground's own triangles bulged into its faces. By
		/// the frame's mesh and the terrain only (not the hill's grid). Returns how many heightmap points were let down.
		/// </summary>
		static int SinkGroundUnder(Vector3[] wv, int[] st, bool[] up)
		{
			int sunk = 0;
			foreach (Terrain t in Terrain.activeTerrains)
			{
				if (t == null || t.terrainData == null) continue;
				TerrainData d = t.terrainData;
				Vector3 o = t.transform.position, size = d.size;
				int res = d.heightmapResolution;
				float sx = size.x / (res - 1), sz = size.z / (res - 1);
				// (the faces turned up over this terrain, and the heightmap block under them all)
				var faces = new List<int>();
				float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
				for (int k = 0; k + 2 < st.Length; k += 3)
				{
					Vector3 a = wv[st[k]], b = wv[st[k + 1]], c = wv[st[k + 2]];
					Vector3 n = Vector3.Cross(b - a, c - a);
					if (n.sqrMagnitude < 1e-10f || n.normalized.y < FrameUpFace) continue;
					float lx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), hx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
					float lz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), hz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
					if (hx < o.x || lx > o.x + size.x || hz < o.z || lz > o.z + size.z) continue;
					faces.Add(k);
					minX = Mathf.Min(minX, lx); maxX = Mathf.Max(maxX, hx); minZ = Mathf.Min(minZ, lz); maxZ = Mathf.Max(maxZ, hz);
				}
				if (faces.Count == 0) continue;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - o.x) / sx) - 1, 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - o.x) / sx) + 1, 0, res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - o.z) / sz) - 1, 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - o.z) / sz) + 1, 0, res - 1);
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				if (w <= 0 || h <= 0) continue;
				float[,] heights = d.GetHeights(x0, z0, w, h);
				var target = new float[h, w];
				for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) target[j, i] = float.MaxValue;
				foreach (int k in faces)
				{
					Vector3 a = wv[st[k]], b = wv[st[k + 1]], c = wv[st[k + 2]];
					Vector3 n = Vector3.Cross(b - a, c - a).normalized;
					// (the face's plane over the ground's points under it, and half a step past its edges: the ground's triangles
					// between a point under the face and one just outside it bulge into the face too)
					float e1x = b.x - a.x, e1z = b.z - a.z, e2x = c.x - a.x, e2z = c.z - a.z;
					float det = e1x * e2z - e2x * e1z;
					if (Mathf.Abs(det) < 1e-6f) continue;
					float lx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - sx * 0.5f, hx = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + sx * 0.5f;
					float lz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - sz * 0.5f, hz = Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + sz * 0.5f;
					int i0 = Mathf.Max(x0, Mathf.CeilToInt((lx - o.x) / sx)), i1 = Mathf.Min(x1, Mathf.FloorToInt((hx - o.x) / sx));
					int j0 = Mathf.Max(z0, Mathf.CeilToInt((lz - o.z) / sz)), j1 = Mathf.Min(z1, Mathf.FloorToInt((hz - o.z) / sz));
					float edge = 0.5f * Mathf.Max(sx, sz) / Mathf.Max(0.01f, Mathf.Sqrt(Mathf.Abs(det)));
					for (int jj = j0; jj <= j1; jj++)
						for (int ii = i0; ii <= i1; ii++)
						{
							float px = o.x + ii * sx - a.x, pz = o.z + jj * sz - a.z;
							float bu = (px * e2z - e2x * pz) / det, bv = (e1x * pz - px * e1z) / det;
							if (bu < -edge || bv < -edge || bu + bv > 1f + edge) continue;
							float fy = a.y + bu * (b.y - a.y) + bv * (c.y - a.y);
							float gy = heights[jj - z0, ii - x0] * size.y + o.y;
							float gap = (fy - gy) * n.y; // (across the face: frame over the ground)
							if (gap >= FrameGroundUnder || gap < -FrameSinkBand) continue;
							float want = fy - FrameGroundUnder / n.y;
							if (want < target[jj - z0, ii - x0]) target[jj - z0, ii - x0] = want;
						}
				}
				bool any = false;
				float[,] was = (float[,])heights.Clone();
				// (11-21 cv46: the frame's points already just under the ground, before any is let down - a point that comes into
				// that band only because the ground under it went down is the frame under the hill on purpose laid bare: 3563 of
				// Sinkmarket Floor's frame points showed through it, CIFrameSunk)
				// (11-21 cv49: only points of faces turned up - a point on the sill's underside never shows)
				var near = new List<int>();
				for (int v = 0; v < wv.Length; v++)
				{
					if (!up[v]) continue;
					Vector3 p = wv[v];
					if (p.x < o.x + x0 * sx - sx || p.x > o.x + x1 * sx + sx || p.z < o.z + z0 * sz - sz || p.z > o.z + z1 * sz + sz) continue;
					float g; Vector3 gn;
					if (!FrameLiftable(p, out g, out gn) && p.y < g) near.Add(v);
				}
				for (int j = 0; j < h; j++)
					for (int i = 0; i < w; i++)
					{
						if (target[j, i] == float.MaxValue) continue;
						float gy = heights[j, i] * size.y + o.y;
						float to = Mathf.Max(target[j, i], gy - FrameSinkMost);
						if (to >= gy - 1e-4f) continue;
						heights[j, i] = Mathf.Clamp01((to - o.y) / size.y);
						sunk++; any = true;
					}
				if (!any) continue;
				d.SetHeights(x0, z0, heights);
				// (any buried point now in the lift band: the heightmap points of its cell raised back only as far as keeps it just
				// under the band - FrameSunk and a little across the slope - never over where they were. 11-21 cv47: putting them
				// all the way back undid 47 of 49: the sill's underside 0.6 m under its top came into the band wherever the ground
				// under the top went down 0.1 m, and 573 rays met the ground lying in Sinkmarket Floor's frame faces again)
				for (int round = 0; round < 4; round++)
				{
					bool back = false;
					foreach (int v in near)
					{
						float g; Vector3 gn;
						if (!FrameLiftable(wv[v], out g, out gn)) continue;
						float need = wv[v].y + (FrameSunk + 0.02f) / Mathf.Max(gn.y, 0.2f);
						float needH = Mathf.Clamp01((need - o.y) / size.y);
						int ci = Mathf.FloorToInt((wv[v].x - o.x) / sx) - x0, cj = Mathf.FloorToInt((wv[v].z - o.z) / sz) - z0;
						for (int dj = 0; dj <= 1; dj++)
							for (int di = 0; di <= 1; di++)
							{
								int ii = ci + di, jj = cj + dj;
								if (ii < 0 || jj < 0 || ii >= w || jj >= h || heights[jj, ii] >= was[jj, ii]) continue;
								float to = Mathf.Min(was[jj, ii], Mathf.Max(heights[jj, ii], needH));
								if (to <= heights[jj, ii]) continue;
								heights[jj, ii] = to; back = true; FrameSinkKeptBack++;
								if (to >= was[jj, ii]) sunk--;
							}
					}
					if (!back) break;
					d.SetHeights(x0, z0, heights);
				}
			}
			return sunk;
		}

		/// <summary>How far under the ground (m) a mouth frame's point is lifted onto it (FramesLikeHill), measured across the ground's
		/// slope; deeper is under the hill on purpose.</summary>
		public const float FrameSunk = 0.5f;
		/// <summary>How far straight under a steep ground a mouth frame's point may lie and still be lifted onto it (m).</summary>
		public const float FrameSunkDown = 1.5f;
		/// <summary>How far over the ground (m) a lifted frame point is laid, so the ground doesn't flicker through it.</summary>
		public const float FrameOver = 0.03f;
		/// <summary>How far over a steep ground (m, across it) a frame point lifted or laid out onto it is laid: the ground's own
		/// triangles bulge between the frame's points, and at FrameOver they stood 2-4 cm in front of Sinkmarket Floor's frame
		/// faces as pale sheets at the picture's edge (CIFrameRing 5 rays, 11-21 cv44).</summary>
		public const float FrameOverSteep = 0.12f;
		/// <summary>Mouth frame points lifted onto the ground, all islands since Raft started.</summary>
		public static int FramePointsLifted;
		/// <summary>How far in front of a mouth frame's face, along its own normal, the island ground may stand and the face is still
		/// laid out onto it (FrameCovered, m); further is the frame under the hill on purpose.</summary>
		public const float FrameCover = 1f;
		/// <summary>FrameCovered only lays a frame onto ground steeper than this: its normal's height under it (0.5 = 60 degrees).</summary>
		public const float FrameCoverSteep = 0.5f;
		/// <summary>Of FramePointsLifted, those laid out along their face onto the ground in front of it (FrameCovered).</summary>
		public static int FramePointsCovered;
		static Terrain liftLand; static bool[,] liftSolid;

		/// <summary>
		/// Whether a mouth frame's world point w, its face's world normal n, has the island ground in front of its face within
		/// FrameCover along n - the ground's own surface, steeper than FrameCoverSteep, turned the same way, not a hole cut in it
		/// (the collider has none). hit is
		/// where n meets the ground. For tests too (CIFrameRing).
		/// </summary>
		public static bool FrameCovered(Vector3 w, Vector3 n, out RaycastHit hit) { return FrameCovered(w, n, FrameCover, out hit); }

		/// <summary>FrameCovered with the ground looked for up to reach in front of the face.</summary>
		public static bool FrameCovered(Vector3 w, Vector3 n, float reach, out RaycastHit hit)
		{
			hit = default(RaycastHit);
			if (n.sqrMagnitude < 1e-6f) { FrameCoverMiss[0]++; return false; }
			n = n.normalized;
			if (n.y < -0.3f) { FrameCoverMiss[0]++; return false; } // (a face turned down: the ground over it is its roof's, not in front of it)
			foreach (Terrain t in Terrain.activeTerrains)
			{
				if (t == null || t.terrainData == null || !Over(t, w)) continue;
				TerrainCollider tc = t.GetComponent<TerrainCollider>();
				if (tc == null || !tc.enabled) return false;
				// (11-21 cv42: no longer only for a point under the ground's height where it stands - Sinkmarket's frame still
				// showed the cutting walls inside its ring, 14 rays of CIFrameRing; a ray from in front of the face back to it
				// meets the ground's face only where the ground stands in front of it)
				// (11-21 cv43: and by the heightmap itself where the collider's ray met nothing - 32025 tries met no ground in front
				// while CIFrameRing's rays saw it 0.56-0.92 m in front of Sinkmarket's frame faces)
				if (!tc.Raycast(new Ray(w + n * reach, -n), out hit, reach) && !HeightRay(t, w + n * reach, -n, reach, out hit)) { FrameCoverMiss[1]++; return false; }
				// (only a steep cutting wall: under a gentle hillside the frame's outside is under the hill on purpose)
				if (hit.normal.y >= FrameCoverSteep) { FrameCoverMiss[2]++; return false; }
				if (Vector3.Dot(hit.normal, n) <= 0.3f) { FrameCoverMiss[3]++; return false; }
				return true;
			}
			return false;
		}

		/// <summary>A ray against a terrain's heights (not its collider): the first point along it under the ground, found in
		/// 5 cm steps and halved down to 5 mm, with the ground's normal from its heights a heightmap step either side.</summary>
		static bool HeightRay(Terrain t, Vector3 from, Vector3 dir, float len, out RaycastHit hit)
		{
			hit = default(RaycastHit);
			float ty = t.transform.position.y;
			System.Func<Vector3, float> under = q => t.SampleHeight(q) + ty - q.y;
			if (under(from) > 0f) return false; // (starts under the ground: nothing in front)
			float step = 0.05f, prev = 0f;
			for (float d = step; d <= len + 1e-4f; d += step)
			{
				if (under(from + dir * d) <= 0f) { prev = d; continue; }
				float lo = prev, hi = d;
				while (hi - lo > 0.005f) { float mid = 0.5f * (lo + hi); if (under(from + dir * mid) > 0f) hi = mid; else lo = mid; }
				Vector3 p = from + dir * hi;
				float e = Mathf.Max(t.terrainData.heightmapScale.x, 0.05f), f = Mathf.Max(t.terrainData.heightmapScale.z, 0.05f);
				float hx = t.SampleHeight(p + Vector3.right * e) - t.SampleHeight(p - Vector3.right * e);
				float hz = t.SampleHeight(p + Vector3.forward * f) - t.SampleHeight(p - Vector3.forward * f);
				hit.point = new Vector3(p.x, t.SampleHeight(p) + ty, p.z);
				hit.normal = new Vector3(-hx / (2f * e), 1f, -hz / (2f * f)).normalized;
				hit.distance = hi;
				FrameCoverByHeights++;
				return true;
			}
			return false;
		}

		/// <summary>FrameCovered's ground found by the heightmap where the terrain's collider met nothing (log, tests).</summary>
		public static int FrameCoverByHeights;

		/// <summary>Why FrameCovered left frame points, all islands since Raft started: no normal or turned down, no ground in
		/// front within reach, the ground there not steep, the ground turned away from the face (log, tests).</summary>
		public static readonly int[] FrameCoverMiss = new int[4];
		/// <summary>How far in front of a frame point next to one laid out (FrameCovered) the ground is looked for (m): the faces
		/// between a point laid out and one left under it dived under the cutting wall within FrameCover.</summary>
		public const float FrameCoverNext = 2.5f;

		/// <summary>
		/// A world point 0-FrameSunk under the island ground (not over a hole cut in it) laid FrameOver over it, its normal then the
		/// ground's (gn; zero when not lifted).
		/// </summary>
		static Vector3 OnGround(Vector3 w, Vector3 fn, Vector3 wn, float reach, out Vector3 gn)
		{
			float g;
			if (!FrameLiftable(w, out g, out gn))
			{
				// (11-21 cv40: and a point whose own face has the ground just in front of it, laid out onto the ground along it -
				// Sinkmarket Floor's cutting walls ran 0.4-0.65 m in front of its frame's inner faces, 1.3 m and more straight
				// down on a 74 degree wall, where the heightmap's smoothed normal said 44: pale walls stood inside the frame ring)
				RaycastHit hit;
				if (!FrameCovered(w, fn, reach, out hit) && !FrameCovered(w, wn, reach, out hit)) { gn = Vector3.zero; return w; }
				FramePointsLifted++; FramePointsCovered++;
				gn = hit.normal;
				return hit.point + hit.normal * FrameOverSteep;
			}
			FramePointsLifted++;
			// (FrameOver measured across the slope, not straight up: on a steep wall 3 cm up is a hair off the ground and flickers)
			return new Vector3(w.x, g + (gn.y < FrameCoverSteep ? FrameOverSteep : FrameOver) / Mathf.Max(gn.y, 0.2f), w.z);
		}

		/// <summary>
		/// Whether a world point is a mouth frame's point FramesLikeHill lifts onto the island ground: under it by 0-FrameSunk
		/// across its slope (at most FrameSunkDown straight down), not over a hole cut in it. g the ground's height there, n its
		/// normal (zero when not). For tests too (CIFrameSunk).
		/// </summary>
		public static bool FrameLiftable(Vector3 w, out float g, out Vector3 n)
		{
			g = 0f; n = Vector3.zero;
			Terrain t = LiftTerrain(w);
			if (t == null) return false;
			g = t.SampleHeight(w) + t.transform.position.y;
			if (w.y >= g || w.y < g - FrameSunkDown) return false;
			TerrainData d = t.terrainData;
			Vector3 o = t.transform.position;
			Vector3 gn = d.GetInterpolatedNormal((w.x - o.x) / d.size.x, (w.z - o.z) / d.size.z);
			// (11-21 cv29: how deep measured across the ground's slope, not straight down - on Sinkmarket Floor's 52-54 degree cutting
			// walls a frame 0.3 m under the ground lay 0.85 m straight under it, was left, and the cut ground poked through the
			// frame's sides as walls in its mouth)
			if ((g - w.y) * Mathf.Max(gn.y, 0.2f) > FrameSunk) return false;
			// (round 4: lifting the frame's outer top onto steep cutting walls from up to 3-8 m under them, where the ground is drawn
			// pale, tore its rim into shards against the sky and stood a pale slab in Sinkmarket's mouth: only 0-FrameSunk is lifted)
			if (!SolidAt(t, w)) return false; // (a hole: no ground here to lie on)
			n = gn;
			return true;
		}

		static bool Over(Terrain t, Vector3 w)
		{
			Vector3 o = t.transform.position, s = t.terrainData.size;
			return w.x >= o.x && w.x <= o.x + s.x && w.z >= o.z && w.z <= o.z + s.z;
		}

		/// <summary>The tunnel pieces' own pale blue rock (TP_IceRavine).</summary>
		public const string IceRock = "IceRavine";
		/// <summary>How many tunnel bodies (and backs) FramesLikeHill drew in the hill's rock, all islands since Raft started.</summary>
		public static int TunnelsLikeHill;

		/// <summary>
		/// Takes away a join's floor plate (Join) that lies on a floor all over: at a tunnel's end into a den the den's floor
		/// fills the groove the plate is for, and the plate stood over it as a pale flat sheet in the tunnel's ravine rock -
		/// Upfall Orchard's root cave (11-19 cave1119 uf_cave_in). Kept wherever a ray down it finds no floor JoinOnFloor or
		/// less under it (the groove between two tunnels). Turned off, not destroyed. Returns how many were taken away.
		/// </summary>
		public static int JoinsOnFloor(GameObject piece)
		{
			if (piece == null) return 0;
			int done = 0;
			Physics.SyncTransforms();
			foreach (Transform c in piece.transform)
			{
				if (c.name != JoinName || !c.gameObject.activeSelf || !JoinLiesOnFloor(c)) continue;
				c.gameObject.SetActive(false);
				done++;
			}
			return done;
		}

		/// <summary>True when every ray down a join plate's middle part (3 across, 5 along) meets an upward face of something
		/// else from JoinRayUp over the plate to JoinOnFloor under it (JoinsOnFloor; CICaveGapsLook checks it).</summary>
		public static bool JoinLiesOnFloor(Transform join)
		{
			MeshFilter mf = join == null ? null : join.GetComponent<MeshFilter>();
			if (mf == null || mf.sharedMesh == null) return false;
			Collider own = join.GetComponent<Collider>();
			Bounds b = mf.sharedMesh.bounds;
			for (int ix = -1; ix <= 1; ix++)
				for (int iz = -2; iz <= 2; iz++)
				{
					// (its scale is undone: metres; a third of the way across, not out at its ends inside the walls)
					Vector3 top = join.TransformPoint(new Vector3(b.center.x + ix * b.extents.x * 0.35f, b.max.y, b.center.z + iz * b.extents.z * 0.4f));
					bool hit = false;
					foreach (RaycastHit h in Physics.RaycastAll(top + Vector3.up * JoinRayUp, Vector3.down, JoinRayUp + JoinOnFloor, ~0, QueryTriggerInteraction.Ignore))
						if (h.collider != own && h.normal.y > 0.5f && h.collider.name != MouthName && h.collider.name != JoinName) { hit = true; break; }
					if (!hit) return false;
				}
			return true;
		}

		// (a join plate with a floor this far under it all over is not needed - the step left is one to walk; the rays start this high)
		const float JoinOnFloor = 0.4f, JoinRayUp = 1f;

		/// <summary>The renderer's own block (copied) with every colour texture of the material - the block's own, else the
		/// material's - darker by a factor.</summary>
		static MaterialPropertyBlock Darker(Renderer r, Material m, float by)
		{
			var b = new MaterialPropertyBlock();
			r.GetPropertyBlock(b);
			Shader sh = m.shader;
			for (int i = 0; i < sh.GetPropertyCount(); i++)
			{
				if (sh.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
				string tp = sh.GetPropertyName(i), low = tp.ToLowerInvariant();
				if (low.Contains("normal") || low.Contains("bump") || low.Contains("mask") || low.Contains("noise") || low.Contains("height")
					|| low.Contains("occlusion") || low.Contains("metal") || low.Contains("smooth") || low.Contains("emission")) continue;
				Texture src = b.HasTexture(tp) ? b.GetTexture(tp) : m.GetTexture(tp);
				Texture2D dark = src != null ? DigTunnel.Scaled(src, new Color(by, by, by, 1f), " (den shade " + by.ToString("0.00") + ")") : null;
				if (dark != null) b.SetTexture(tp, dark);
			}
			return b;
		}

		/// <summary>Marks a lone den's wall's outside drawn as the hill: TintWall leaves it as the hill. CaveLit lights it as it lights
		/// the hill (CaveMountain marks its skin), so the two stay one rock.</summary>
		public class HillWall : MonoBehaviour
		{
			/// <summary>A mouth frame's points (by its mesh's points) TuckRim laid under the ground as its rim; null on other walls.</summary>
			[NonSerialized] public bool[] Rim;
		}

		/// <summary>A den's way-in sleeve's parts' shade, from its mouth in (CaveLit lights each part with the den's light times it).</summary>
		public class SleeveShade : MonoBehaviour { [NonSerialized] public float[] Shade; }

		/// <summary>
		/// A lone den's way in seen from outside: a short rock tunnel. A sleeve in the hill's own rock (the wall outside's material,
		/// lit as the den by CaveLit), the arch's shape just inside its edge, open at the floor and closed 3 m in, darker the
		/// further in. Drawn only for cameras out in front of the arch (per camera, onPreCull); nearer, the den itself.
		/// 11-08 user (r26): the den room seen through the arch read blue - Raft's depth fog (Azure) lies over its dark walls 20-40 m
		/// off, dimming it made that worse - and the earlier sleeve, black and reaching under the floor, a black horseshoe with two
		/// points in the sand. This one starts at the arch's inner edge (the funnel round it is the hill's rock), stays above the
		/// den floor and is lit like the rock.
		/// </summary>
		public class CaveVeil : MonoBehaviour
		{
			[NonSerialized] float front;
			[NonSerialized] Renderer sleeve;
			/// <summary>The sleeve: from SleeveLead out from the arch's inner edge to SleeveLength in, SleeveSize of the arch; shown
			/// for a camera ShowFrom out from that edge.</summary>
			const float SleeveLead = 0.05f, SleeveLength = 3f, SleeveSize = 0.96f, ShowFrom = 0.8f;
			/// <summary>Its rings' depths in (0 at the arch) and the shade of each part between them, then the end's.</summary>
			/// (11-09: Rings rings, each a tiny step darker - five, then nine, read as stepped bands just inside the arch's rim, r41)
			const int Rings = 24;
			static readonly float[] RingIn = Steps(0f, 3f);
			static readonly float[] Shade = Steps(0.86f, 0.3f);
			/// <summary>Its parts' rock darker by these (on the hill: CaveTool.WallLikeHill): fast at first, then slower.</summary>
			internal static readonly float[] Dark = Steps(0f, 1f).Select(u => 0.15f + 0.65f * (1f - u) * (1f - u * 0.55f)).ToArray();
			static float[] Steps(float from, float to) { var a = new float[Rings]; for (int n = 0; n < Rings; n++) a[n] = from + (to - from) * n / (Rings - 1f); return a; }
			const int Seg = 40;

			public static GameObject Build(GameObject plug, float cx, float cy, float ha, float hb, float hp, Func<float, float, float> noise, float front,
				float floorY, Material rock)
			{
				if (plug == null) return null;
				var go = new GameObject(PlugVeilName);
				go.layer = plug.layer;
				go.transform.SetParent(plug.transform, false);
				var veil = go.AddComponent<CaveVeil>();
				veil.front = front;
				if (rock == null) return go;
				// (the rings: the arch's superellipse with its noise, never under the den floor - the sides end on it, level)
				float fy = floorY - 0.05f;
				var rings = new Vector3[RingIn.Length][];
				for (int ri = 0; ri < RingIn.Length; ri++)
				{
					float z = front + SleeveLead - RingIn[ri] * SleeveLength / RingIn[RingIn.Length - 1];
					rings[ri] = new Vector3[Seg];
					for (int sg = 0; sg < Seg; sg++)
					{
						float ang = 2f * Mathf.PI * sg / Seg, ux = Mathf.Cos(ang), uy = Mathf.Sin(ang), sc = noise(ux, uy);
						float t = SleeveSize / Mathf.Pow(Mathf.Pow(Mathf.Abs(ux) / (ha * sc), hp) + Mathf.Pow(Mathf.Abs(uy) / (hb * sc), hp), 1f / hp);
						rings[ri][sg] = new Vector3(cx + ux * t, Mathf.Max(cy + uy * t, fy), z);
					}
				}
				var vv = new List<Vector3>();
				var vn = new List<Vector3>();
				var vu = new List<Vector2>();
				var parts = new List<int>[RingIn.Length];
				for (int pi = 0; pi < parts.Length; pi++) parts[pi] = new List<int>();
				// (a triangle both sides, mapped along its own plane in metres, as the hill's skin: CaveMountain.Quad)
				Action<List<int>, Vector3, Vector3, Vector3> tri = (into, a, b, c) =>
				{
					Vector3 pn = Vector3.Cross(b - a, c - a);
					if (pn.sqrMagnitude < 1e-8f) return;
					Vector3 n = pn.normalized;
					float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
					for (int side = 0; side < 2; side++)
					{
						int b0 = vv.Count;
						foreach (Vector3 v in new[] { a, b, c })
						{
							vv.Add(v);
							vn.Add(side == 0 ? n : -n);
							vu.Add(ay >= ax && ay >= az ? new Vector2(v.x, v.z) / 14f : ax >= az ? new Vector2(v.z, v.y) / 14f : new Vector2(v.x, v.y) / 14f);
						}
						if (side == 0) into.AddRange(new[] { b0, b0 + 1, b0 + 2 }); else into.AddRange(new[] { b0, b0 + 2, b0 + 1 });
					}
				};
				for (int sg = 0; sg < Seg; sg++)
				{
					int sn = (sg + 1) % Seg;
					for (int ri = 0; ri + 1 < RingIn.Length; ri++)
					{
						Vector3 a0 = rings[ri][sg], b0 = rings[ri][sn], a1 = rings[ri + 1][sg], b1 = rings[ri + 1][sn];
						// (no strip along the floor: both its ends on the floor line)
						if (a0.y <= fy + 1e-4f && b0.y <= fy + 1e-4f) continue;
						tri(parts[ri], a0, b0, b1);
						tri(parts[ri], a0, b1, a1);
					}
					// (the end: a fan from the middle)
					Vector3[] end = rings[RingIn.Length - 1];
					Vector3 mid = new Vector3(cx, Mathf.Max(cy, fy), end[0].z);
					tri(parts[RingIn.Length - 1], mid, end[sg], end[sn]);
				}
				var mesh = new Mesh { name = PlugVeilName + "Sleeve", subMeshCount = parts.Length };
				mesh.SetVertices(vv);
				mesh.SetNormals(vn);
				mesh.SetUVs(0, vu);
				for (int pi = 0; pi < parts.Length; pi++) mesh.SetTriangles(parts[pi], pi);
				mesh.RecalculateBounds();
				var sgo = new GameObject(PlugVeilName + "Sleeve");
				sgo.layer = go.layer;
				sgo.transform.SetParent(go.transform, false);
				sgo.AddComponent<MeshFilter>().sharedMesh = mesh;
				var r = sgo.AddComponent<MeshRenderer>();
				r.sharedMaterials = Enumerable.Repeat(rock, parts.Length).ToArray();
				// (casting: the camera's depth picture is drawn with the shadow pass, and Raft's fog reads it)
				r.shadowCastingMode = ShadowCastingMode.On;
				r.enabled = false;
				sgo.AddComponent<SleeveShade>().Shade = (float[])Shade.Clone();
				veil.sleeve = r;
				return go;
			}

			/// <summary>How far a camera here is out in front of the arch's inner edge, metres (negative inside).</summary>
			public float OutFrom(Vector3 cam) { return transform.InverseTransformPoint(cam).z - front; }

			void OnEnable() { Camera.onPreCull += SleeveFor; }
			void OnDisable() { Camera.onPreCull -= SleeveFor; if (sleeve != null) sleeve.enabled = false; }

			void SleeveFor(Camera c)
			{
				if (sleeve == null || c == null) return;
				sleeve.enabled = OutFrom(c.transform.position) > ShowFrom && Vector3.Distance(c.transform.position, transform.position) < 200f;
			}
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
		/// averaged (from: the directions to look in instead). The den's atlas texture and mesh can't be read in the game, so drawing is how its look is measured.
		/// </summary>
		public static bool LitColour(Renderer[] rs, out Color colour, bool sidesOnly = false, Vector3[] from = null)
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
				foreach (Vector3 dir in from != null ? from : sidesOnly ? new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right } : new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.down })
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
			// (the way in: a rounded, uneven arch round the passage's rectangle, not a square window - the rectangle cut in a
			// flat panel read as a texture board stuck on the hill, Shelter Atoll's ammo cave from the sea, 11-06 review. The
			// arch is a superellipse a bit wider than the passage, its edge pushed out by noise round it, so the passage stays clear)
			// (11-08 user: Raft's own notched den rim showed round the arch - the arch now tighter, so the wall's rock covers it)
			float hcx = mx, hcy = (hy0 + hy1) * 0.5f, ha = HoleHalf * 1.2f, hb = (hy1 - hy0) * 0.5f * 1.15f;
			const float hp = 2.2f;
			Func<float, float, float> archNoise = (dx, dy) =>
			{
				float ang = Mathf.Atan2(dy, dx);
				// (and a finer, ragged edge on it, so its sides aren't smooth curves - the oldmine wall's ragged arch, 11-06 merge; round
				// the angle's circle, no seam at its left)
				return 1f + 0.1f * Mathf.PerlinNoise(1.3f * Mathf.Cos(ang) + 7.7f, 1.3f * Mathf.Sin(ang) + 2.9f)
					+ 0.03f * Mathf.PerlinNoise(2.7f * Mathf.Cos(ang) + 23.1f, 2.7f * Mathf.Sin(ang) + 14.3f);
			};
			// (how far out of the arch a point is: 1 on its edge, under 1 inside)
			Func<float, float, float> archQ = (px, py) =>
			{
				float dx = px - hcx, dy = py - hcy, s = archNoise(dx, dy);
				return Mathf.Pow(Mathf.Pow(Mathf.Abs(dx) / (ha * s), hp) + Mathf.Pow(Mathf.Abs(dy) / (hb * s), hp), 1f / hp);
			};
			// (rock relief: a flat board with the den's texture at 20 m drew as a smooth marbled blue-white sheet beside
			// The Abyss's acid path, 11-05. Each corner is pushed in or out by coarse noise, so the wall reads as rock. And
			// the wall leans back into the den round the way in (PlugFunnel): a funnel of rock, as a dug tunnel's mouth, its
			// edge in shade, not a flat panel with a hole)
			// (11-08 user: the relief on the funnel round the arch broke it into dark shards - a ragged frame; it fades in from 0.8 out of the arch)
			Func<float, float, float> calm = (px, py) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((archQ(px, py) - 1f) / 0.8f));
			Func<float, float, float> depth = (px, py) => calm(px, py) * (PlugRelief * (2f * Mathf.PerlinNoise(px / 1.6f + 17.3f, py / 1.6f + 5.1f) - 1f)
				+ 0.35f * PlugRelief * (2f * Mathf.PerlinNoise(px / 0.7f + 3.7f, py / 0.7f + 41.9f) - 1f))
				- PlugFunnel * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(archQ(px, py) - 1f)))
				// (and leans out at its outer edge, over the hill's own edge at the mouth: the wall's straight side, 2 m behind it, showed)
				+ PlugLean * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((archQ(px, py) - 1.6f) / 0.8f));
			// (each cell: 0 none, 1 wall, 2 the way in)
			var state = new int[nx, ny];
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < ny; k++)
				{
					if (!keep[i, k]) continue;
					float xa = x0 + i * cell, xb = xa + cell, ya = y0 + k * cell, yb = ya + cell;
					// (the arch only, not the passage's rectangle too: its top corners stood out over the arch's shoulders as two
					// pointed ears, 11-09 r46; the arch holds the passage but for those corners, over 4.6 m up)
					state[i, k] = archQ(xa + cell * 0.5f, ya + cell * 0.5f) < 1f ? 2 : 1;
				}
			Func<int, int, int> stateAt = (i, k) => i >= 0 && k >= 0 && i < nx && k < ny ? state[i, k] : 0;
			// (a corner on the way in's edge lies on the arch; one on the wall's outer edge is pushed out a little by noise, so
			// neither edge is a straight row of 0.5 m cells)
			Func<int, int, Vector2> place = (ci, ck) =>
			{
				float px = x0 + ci * cell, py = y0 + ck * cell;
				bool wall = false, hole = false, none = false;
				for (int di = -1; di <= 0; di++)
					for (int dk = -1; dk <= 0; dk++)
					{
						int s = stateAt(ci + di, ck + dk);
						if (s == 1) wall = true; else if (s == 2) hole = true; else none = true;
					}
				float dx = px - hcx, dy = py - hcy, len = Mathf.Sqrt(dx * dx + dy * dy);
				if (len < 1e-3f) return new Vector2(px, py);
				if (wall && hole)
				{
					float sc = archNoise(dx, dy), ux = dx / len, uy = dy / len;
					float t = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(ux) / (ha * sc), hp) + Mathf.Pow(Mathf.Abs(uy) / (hb * sc), hp), 1f / hp);
					return new Vector2(hcx + ux * t, hcy + uy * t);
				}
				if (wall && none)
				{
					float push = 0.5f * Mathf.PerlinNoise(px * 0.9f + 31.1f, py * 0.9f + 12.7f);
					return new Vector2(px + dx / len * push, py + dy / len * push);
				}
				return new Vector2(px, py);
			};
			var verts = new List<Vector3>();
			var norms = new List<Vector3>();
			var tris = new List<int>();
			// (each side's corners shared by the cells that meet there, with the relief's own smooth normal: every triangle with
			// its own corners and flat normal drew the wall in 45-degree halves of orange and black - the rock shader (Mountains)
			// blends its rock and sand by the normal - Shelter Atoll's ammo cave from the sea, 11-06)
			var corner = new Dictionary<int, int>[] { new Dictionary<int, int>(), new Dictionary<int, int>() };
			// (a point on the wall from its place in the wall's plane, and its smooth normal; 0: facing -z, into the den)
			// (11-08 user: the den's own notched rim stood in front of the wall, 2 m of it, as a dark ragged frame round every den's
			// arch - the wall is drawn PlugForward further out, at the den's mouth, so its rock hides that rim; its cells are still
			// found 2 m in, where the den's rock is all round)
			float zf = z + PlugForward;
			Func<float, float, Vector3> pos3 = (px, py) => new Vector3(px, py, zf + depth(px, py));
			Func<float, float, int, Vector3> normAt = (px, py, side) =>
			{
				float e = cell, fx = (depth(px + e, py) - depth(px - e, py)) / (2f * e), fy = (depth(px, py + e) - depth(px, py - e)) / (2f * e);
				return side == 0 ? new Vector3(fx, fy, -1f).normalized : new Vector3(-fx, -fy, 1f).normalized;
			};
			Func<int, int, int, int> at = (side, ci, ck) =>
			{
				int key = ci * (ny + 2) + ck, n;
				if (corner[side].TryGetValue(key, out n)) return n;
				Vector2 pp = place(ci, ck);
				n = verts.Count;
				verts.Add(pos3(pp.x, pp.y));
				norms.Add(normAt(pp.x, pp.y, side));
				corner[side][key] = n;
				return n;
			};
			// (a cell out in the open air is left out, both sides: the wall's plane, its relief and its lean took its outer cells
			// out of the den's rock and the hill, in front of the hill's face, where its outside - shaded dark round the way in -
			// stood as black blades over The Abyss's acid path - fix1107. Such a cell closes nothing: the den's hollow isn't
			// there (the den's own rock near on all four sides, met from inside the hollow) and it isn't in the hill (up, both
			// sides and out toward the mouth, the first thing met other than the den is rock seen from inside - a back face). The
			// den's rock doesn't count for that: its shell is one surface, so from outside the den it too is met from behind, and
			// the cells round the den in the open air passed as in rock. A cell beside the hollow stays, so the wall's edge still
			// goes into the rock round it)
			Vector3 outward = go.transform.TransformDirection(Vector3.forward).normalized;
			float nearRock = 8f * Mathf.Max(0.1f, size);
			var denRock = new HashSet<Collider>(rock);
			// (the first thing met but the den: 1 rock from inside, -1 rock from outside near by, 0 nothing or far)
			Func<Vector3, Vector3, int> firstMet = (w, dir) =>
			{
				RaycastHit best = default(RaycastHit);
				float near = float.MaxValue;
				foreach (RaycastHit h in Physics.RaycastAll(w, dir, reach, ~0, QueryTriggerInteraction.Ignore))
					if (h.distance < near && !denRock.Contains(h.collider)) { near = h.distance; best = h; }
				if (near == float.MaxValue) return 0;
				return Vector3.Dot(best.normal, dir) > 0f ? 1 : near <= nearRock ? -1 : 0;
			};
			// (the same, the den's own rock only: an open pocket of air under the hill's overhang beside the den, by the den's
			// rock, met rock from outside all round too and passed as the hollow - the last blades on The Abyss's path)
			Func<Vector3, Vector3, int> denMet = (w, dir) =>
			{
				var ray = new Ray(w, dir);
				RaycastHit h, best = default(RaycastHit);
				float near = float.MaxValue;
				foreach (Collider c in rock)
					if (c.Raycast(ray, out h, reach) && h.distance < near) { near = h.distance; best = h; }
				if (near == float.MaxValue) return 0;
				// (the den's rock from inside at any distance: the bear den's room is 14 m up and 10 m to a side from the wall's cells,
				// so at nearRock (8 m) no cell but the way in's passed as its hollow and the wall was only a ring round the arch - the den's
				// room beside and over it seen from outside, a box cut in the hill, 11-09 user. From outside, the den's shell is met
				// from behind, so a pocket by the den in the open still fails)
				return Vector3.Dot(best.normal, dir) > 0f ? 1 : -1;
			};
			var hollow = new bool[nx, ny];
			var open = new bool[nx, ny];
			var sky = new bool[nx, ny];
			var free = new bool[nx, ny];
			int outNotByHollow = 0, outSky = 0, outFree = 0;
			bool backsOpen = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < ny; k++)
					{
						if (state[i, k] != 1) { hollow[i, k] = state[i, k] == 2; continue; }
						float cx = x0 + (i + 0.5f) * cell, cy = y0 + (k + 0.5f) * cell;
						// (the hill asked where the cell is drawn, PlugForward out: asked 1.8 m further in, the cells by a thin bit of
						// the hill - Signal Rock's cleft - passed as in it and stood out of it as rust shards and a flat slab, w15Z;
						// the den's hollow still where its cells are found)
						Vector3 w = go.transform.TransformPoint(new Vector3(cx, cy, z + depth(cx, cy))), wd = go.transform.TransformPoint(pos3(cx, cy));
						int u = firstMet(wd, up), r = firstMet(wd, right), l = firstMet(wd, -right);
						hollow[i, k] = denMet(w, up) == -1 && denMet(w, right) == -1 && denMet(w, -right) == -1 && denMet(w, -up) == -1;
						open[i, k] = !hollow[i, k] && !(u == 1 && r == 1 && l == 1 && firstMet(wd, outward) == 1);
						free[i, k] = u == 0 && r == 0 && l == 0 && firstMet(wd, outward) == 0 && firstMet(wd, -up) == 0;
						sky[i, k] = u == 0;
					}
			}
			finally { Physics.queriesHitBackfaces = backsOpen; }
			int leftOut = 0;
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < ny; k++)
				{
					if (!open[i, k]) continue;
					// (an open cell with nothing over it, the sky, is the wall's rim standing out over the hill - seen from the side it
					// was a dark blade. It stays only right beside the way in, so the way in keeps its frame - and not even there when
					// nothing of the hill is met any way round it: then the den's own shell stands out of the hill and frames the way in)
					bool byHollow = false, byWayIn = false;
					for (int di = -1; di <= 1; di++)
						for (int dk = -1; dk <= 1; dk++)
						{
							int a = i + di, b = k + dk;
							if (a < 0 || b < 0 || a >= nx || b >= ny) continue;
							if (hollow[a, b]) byHollow = true;
							if (state[a, b] == 2) byWayIn = true;
						}
					if (!byHollow || (sky[i, k] && !byWayIn) || free[i, k])
					{
						state[i, k] = 0; leftOut++;
						if (!byHollow) outNotByHollow++; else if (free[i, k]) outFree++; else outSky++;
					}
				}
			int cells = 0;
			var trisOut = new List<int>();
			// (the outside in bands by how near the way in: shade round it - the den beyond, lit evenly, showed as a light window.
			// Each band's edge an uneven line by noise, the triangles cut along it, so it is no staircase of cells)
			Func<float, float, float> shadeQ = (px, py) => archQ(px, py) + 0.3f * (Mathf.PerlinNoise(px * 0.55f + 51.3f, py * 0.55f + 8.9f) - 0.5f)
				+ 0.12f * (Mathf.PerlinNoise(px * 1.7f + 3.1f, py * 1.7f + 77.7f) - 0.5f);
			var bands = new List<int>[PlugShadeOut.Length + 1];
			for (int b = 0; b < bands.Length; b++) bands[b] = new List<int>();
			var outV = new List<Vector3>();
			var outN = new List<Vector3>();
			var outQ = new List<float>();
			var outOf = new Dictionary<int, int>();
			var cutOf = new Dictionary<long, int>();
			Func<int, int> outAt = src =>
			{
				int n;
				if (outOf.TryGetValue(src, out n)) return n;
				n = outV.Count;
				outV.Add(verts[src]);
				outN.Add(norms[src]);
				outQ.Add(shadeQ(verts[src].x, verts[src].y));
				outOf[src] = n;
				return n;
			};
			// (the point where an edge crosses a band's edge, shared by the two triangles on that edge: no crack)
			Func<int, int, int, int> cutAt = (a, b, ti) =>
			{
				int lo = Math.Min(a, b), hi = Math.Max(a, b), n;
				long key = ((long)lo * 1000003L + hi) * 8L + ti;
				if (cutOf.TryGetValue(key, out n)) return n;
				float th = PlugShadeOut[ti], s = Mathf.Clamp01((th - outQ[lo]) / (outQ[hi] - outQ[lo]));
				float px = Mathf.Lerp(outV[lo].x, outV[hi].x, s), py = Mathf.Lerp(outV[lo].y, outV[hi].y, s);
				n = outV.Count;
				outV.Add(pos3(px, py));
				outN.Add(normAt(px, py, 1));
				outQ.Add(th);
				cutOf[key] = n;
				return n;
			};
			Action<List<int>, int> emit = (poly, band) =>
			{
				for (int j = 1; j + 1 < poly.Count; j++) { bands[band].Add(poly[0]); bands[band].Add(poly[j]); bands[band].Add(poly[j + 1]); }
			};
			Action<int, int, int> shadeTri = (a, b, c) =>
			{
				var poly = new List<int> { outAt(a), outAt(b), outAt(c) };
				for (int ti = 0; ti < PlugShadeOut.Length && poly.Count >= 3; ti++)
				{
					var below = new List<int>();
					var above = new List<int>();
					for (int j = 0; j < poly.Count; j++)
					{
						int p = poly[j], q = poly[(j + 1) % poly.Count];
						bool pb = outQ[p] < PlugShadeOut[ti], qb = outQ[q] < PlugShadeOut[ti];
						(pb ? below : above).Add(p);
						if (pb != qb) { int x = cutAt(p, q, ti); below.Add(x); above.Add(x); }
					}
					if (below.Count >= 3) emit(below, ti);
					poly = above;
				}
				if (poly.Count >= 3) emit(poly, PlugShadeOut.Length);
			};
			for (int k = 0; k < ny; k++)
				for (int i = 0; i < nx; i++)
				{
					if (state[i, k] != 1) continue; // (none, or the way in)
					cells++;
					// (both sides, each with its own corners: on shared ones the two sides' normals cancelled out and the wall
					// drew as a flat black board - seen from inside The Abyss's lava tube, 11-05; Plug had the same fault, TN1)
					tris.AddRange(new[] { at(0, i, k), at(0, i + 1, k + 1), at(0, i + 1, k), at(0, i, k), at(0, i, k + 1), at(0, i + 1, k + 1) });
					var outCell = new[] { at(1, i, k), at(1, i + 1, k), at(1, i + 1, k + 1), at(1, i, k), at(1, i + 1, k + 1), at(1, i, k + 1) };
					trisOut.AddRange(outCell);
					shadeTri(outCell[0], outCell[1], outCell[2]);
					shadeTri(outCell[3], outCell[4], outCell[5]);
				}
			if (tris.Count == 0) { UnityEngine.Object.DestroyImmediate(go); return; }
			// (the wall itself draws its inside, lit as the den; its outside is a child of its own (PlugOutName), so it can be
			// drawn and lit as the hill over the den - WallLikeHill. The collider has both)
			var mesh = new Mesh { name = PlugName };
			if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			var both = new Mesh { name = PlugName + " collider" };
			if (verts.Count > 65000) both.indexFormat = IndexFormat.UInt32;
			both.SetVertices(verts);
			both.SetTriangles(tris.Concat(trisOut).ToList(), 0);
			both.RecalculateBounds();
			// (the den's texture is an atlas of colour patches: the wall's own UVs at 20 m showed all of it - blue, white and
			// orange - beside The Abyss's acid path, 11-05. The wall takes the den's colour from round its mouth instead)
			// The den's mesh is not readable in the game, so it is Raft's big rock instead (as the cave mountain's skin), at 14 m
			// like the skin; only without it, one patch of the den's own texture (readable mesh) or the old 20 m tiling.
			GameObject bigRock = PlaceableCatalog.Get(WallRock);
			Renderer rockLook = bigRock != null ? bigRock.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial != null) : null;
			Vector2 swatch = Vector2.zero;
			bool atlas = rockLook == null && MouthSwatch(look, go.transform.TransformPoint(new Vector3(mx, my + HoleUp + 1f, RoomMax.z)), out swatch);
			Func<Vector3, Vector2> uvOf = v => rockLook != null ? new Vector2(v.x, v.y) / 14f : atlas ? swatch + new Vector2(v.x, v.y) * (0.004f / PlugTile)
				: new Vector2(v.x / PlugTile, v.y / PlugTile);
			mesh.SetUVs(0, verts.Select(uvOf).ToList());
			mesh.SetNormals(norms);
			mesh.RecalculateBounds();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer wr = go.AddComponent<MeshRenderer>();
			wr.sharedMaterial = rockLook != null ? rockLook.sharedMaterial : look.sharedMaterial;
			go.AddComponent<MeshCollider>().sharedMesh = both;
			// (r82: no shadow of its own - the sun nearly overhead, its rim leaning out over the sand before the arch (PlugLean)
			// cast its 0.5 m cells' staircase outline there as a jagged teal patch, and a lone cell a dark square, Shelter Atoll r81)
			wr.shadowCastingMode = ShadowCastingMode.Off;
			var outGo = new GameObject(PlugOutName);
			outGo.layer = go.layer;
			outGo.transform.SetParent(go.transform, false);
			var outMesh = new Mesh { name = PlugOutName };
			if (outV.Count > 65000) outMesh.indexFormat = IndexFormat.UInt32;
			outMesh.SetVertices(outV);
			outMesh.SetNormals(outN);
			outMesh.SetUVs(0, outV.Select(uvOf).ToList());
			// (submesh 0 as the hill, then the shade bands from the way in out: PlugShade, WallLikeHill)
			outMesh.subMeshCount = bands.Length;
			outMesh.SetTriangles(bands[bands.Length - 1], 0);
			for (int b = 0; b < bands.Length - 1; b++) outMesh.SetTriangles(bands[b], b + 1);
			outMesh.RecalculateBounds();
			outGo.AddComponent<MeshFilter>().sharedMesh = outMesh;
			MeshRenderer outR = outGo.AddComponent<MeshRenderer>();
			outR.sharedMaterials = Enumerable.Repeat(wr.sharedMaterial, bands.Length).ToArray();
			outR.shadowCastingMode = ShadowCastingMode.Off;
			// (and a short rock tunnel in the way in, seen from outside only (CaveVeil): the den room through the arch read blue, 11-08 user)
			CaveVeil.Build(go, hcx, hcy, ha, hb, hp, archNoise, zf - PlugFunnel, my, wr.sharedMaterial);
			// (lit as the den, its own parts looked up again, the outside too)
			CaveLit.Mark(den);
			Debug.Log("[CUSTOM ISLANDS] Den's mouth walled: " + cells + " cells (" + leftOut + " out in the open left out: " + outNotByHollow + " by no hollow, " + outSky + " sky, " + outFree + " free), rock relief, smooth, arched way in, " + wr.sharedMaterial.name + (atlas ? " at uv " + swatch.ToString("0.000") : ""));
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
				// (not what lies under another cave's den: the hill rises through the den, its apron takes the ground in it back
				// down to the den's floor after - Weatherglass Deep's reef chest rode 7.4 m up with the glass room's hill, 11-20)
				if (UnderDen(p)) continue;
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

		/// <summary>Whether a point is under a cave den's (BalboaCave_Bear's) roof: a ray up from it meets the den's rock.</summary>
		static bool UnderDen(Vector3 p)
		{
			foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 0.2f, Vector3.up, 12f, ~0, QueryTriggerInteraction.Ignore))
			{
				if (h.collider is TerrainCollider) continue;
				EditorGameObject e = h.collider.GetComponentInParent<EditorGameObject>();
				if (e != null && e.GameObjectName == Room) return true;
			}
			return false;
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
		/// <summary>Per renderer (as rends): each material's shine to set, or null when it has one material (NaN: no shine property).</summary>
		[NonSerialized] float[][] subShine;
		/// <summary>Per renderer: the shine for its own block (its one material's), NaN for none.</summary>
		[NonSerialized] float[] ownShine;
		/// <summary>Lit with the light now set: false when newly marked or enabled, or a renderer was skipped (hidden) or gone.</summary>
		[NonSerialized] bool lit;
		/// <summary>Per renderer its parts' shade (a den's way-in sleeve, CaveTool.SleeveShade: darker the further in), else null.</summary>
		[NonSerialized] float[][] shades;
		static readonly SphericalHarmonicsL2[] shaded = new SphericalHarmonicsL2[1];
		static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		static readonly SphericalHarmonicsL2[] one = new SphericalHarmonicsL2[1];
		static readonly int SmoothId = Shader.PropertyToID("_Smoothness_Multiplyer");
		/// <summary>How much of the rock's shine is kept (as the dug tunnel's lining: its blue reflections washed the walls out).</summary>
		const float Shine = 0.45f;
		/// <summary>Dev probes: leave the pieces' light alone.</summary>
		public static bool Off;
		/// <summary>Dev (CIRockLook): the shine kept multiplied by this; the pieces lit by no light at all.</summary>
		internal static float TestShine = 1f;
		internal static bool TestDark;
		static bool told;
		/// <summary>The light last set, and whether it was set at all since a skip (Off, editor); some piece not lit with it yet.</summary>
		static SphericalHarmonicsL2 lastSh;
		static bool haveLast, pending, lastDark;
		static float lastTestShine = 1f;

		public static bool AnyLive { get { return live.Count > 0; } }

		/// <summary>The light the pieces and the cave hill are drawn with now, and the share of shine they keep - for a dug tunnel's
		/// rim walls dressed as the hill (DigTunnel.LightLining). False before any light was set.</summary>
		public static bool HillLight(out SphericalHarmonicsL2 sh, out float shine)
		{
			sh = TestDark ? new SphericalHarmonicsL2() : lastSh;
			shine = Shine * TestShine;
			return haveLast && !Off;
		}

		/// <summary>Marks a cave tool piece (again after its parts changed: its renderers are looked up anew).</summary>
		public static void Mark(GameObject piece)
		{
			if (piece == null) return;
			CaveLit c = piece.GetComponent<CaveLit>();
			if (c == null) c = piece.AddComponent<CaveLit>();
			c.rends = null;
			c.lit = false;
			pending = true;
		}

		// (the camera hook too: it was only set by an atmosphere zone or a dug tunnel, so on an island with neither, loaded
		// first after Raft started, nothing lit the pieces - they and the cave hill showed Raft's blue sky light; 11-06)
		void OnEnable() { if (!live.Contains(this)) live.Add(this); lit = false; pending = true; AtmosphereZone.Hook(); }
		void OnDisable() { live.Remove(this); }

		public static void LightAll(SphericalHarmonicsL2 sh)
		{
			if (live.Count == 0) return;
			if (Off || DynamicIslands.InEditor()) { haveLast = false; return; }
			// (the same light as last frame and every piece lit with it: nothing to do - the blocks keep it)
			bool all = !haveLast || sh != lastSh || TestDark != lastDark || TestShine != lastTestShine;
			if (!all && !pending) return;
			lastSh = sh; lastDark = TestDark; lastTestShine = TestShine;
			haveLast = true;
			pending = false;
			one[0] = TestDark ? new SphericalHarmonicsL2() : sh;
			for (int i = live.Count - 1; i >= 0; i--)
			{
				CaveLit c = live[i];
				if (c == null) { live.RemoveAt(i); continue; }
				if (!all && c.lit) continue;
				try { c.Light(); }
				catch (Exception e) { c.lit = true; if (!told) Debug.LogWarning("[CUSTOM ISLANDS] Cave piece's light: " + e.Message); told = true; }
				if (!c.lit) pending = true;
			}
		}

		void Find()
		{
			// (a lone den's wall's outside drawn as the hill (HillWall) is lit too: the hill's skin is CaveLit, so the two match -
			// left out, it drew by Raft's blue sky beside the warm hill once the skin was lit, 11-06 merge. Not the veil: see-through shade)
			rends = GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer)
				&& r.GetComponent<CaveTool.CaveVeil>() == null).ToArray();
			subShine = new float[rends.Length][];
			ownShine = new float[rends.Length];
			shades = rends.Select(r => { var v = r.GetComponent<CaveTool.SleeveShade>(); return v != null ? v.Shade : null; }).ToArray();
			for (int k = 0; k < rends.Length; k++)
			{
				Material[] mats = rends[k].sharedMaterials;
				ownShine[k] = mats.Length == 1 ? ShineOf(mats[0]) : float.NaN;
				if (mats.Length != 1)
				{
					subShine[k] = new float[mats.Length];
					for (int i = 0; i < mats.Length; i++) subShine[k][i] = ShineOf(mats[i]);
				}
			}
		}

		static float ShineOf(Material m)
		{
			return m != null && m.HasProperty(SmoothId) ? m.GetFloat(SmoothId) * Shine : float.NaN;
		}

		void Light()
		{
			if (rends == null) Find();
			lit = true;
			for (int k = 0; k < rends.Length; k++)
			{
				Renderer r = rends[k];
				// (a part gone: looked up anew next frame)
				if (r == null) { rends = null; lit = false; return; }
				// (a way-in sleeve is shown per camera (CaveVeil), so it is lit hidden too)
				if ((!r.enabled && shades[k] == null) || !r.gameObject.activeInHierarchy) { lit = false; continue; }
				if (r.lightProbeUsage != LightProbeUsage.CustomProvided) r.lightProbeUsage = LightProbeUsage.CustomProvided;
				float[] sub = subShine[k];
				float[] sh = shades[k];
				if (sub != null)
					for (int i = 0; i < sub.Length; i++)
					{
						r.GetPropertyBlock(block, i);
						if (sh != null && i < sh.Length) { shaded[0] = one[0] * sh[i]; Set(shaded, sub[i]); } else Set(one, sub[i]);
						r.SetPropertyBlock(block, i);
					}
				// (the renderer's own block too: a part drawn without its own block would be lit by nothing)
				r.GetPropertyBlock(block);
				Set(one, ownShine[k]);
				r.SetPropertyBlock(block);
			}
		}

		static void Set(SphericalHarmonicsL2[] sh, float shine)
		{
			block.CopySHCoefficientArraysFrom(sh);
			if (!float.IsNaN(shine)) block.SetFloat(SmoothId, shine * TestShine);
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

	/// <summary>Dev (CICaveTry): numbers a test sets by name to try a change in the cave tools in one run, before and after,
	/// without a new build. Unset, each is its default - what ships.</summary>
	internal static class CaveTry
	{
		static readonly Dictionary<string, float> set = new Dictionary<string, float>();
		public static float F(string name, float normal) { float v; return set.TryGetValue(name, out v) ? v : normal; }
		public static bool On(string name) { return F(name, 0f) > 0f; }
		public static void Set(string name, float v) { set[name] = v; }
		public static void Clear() { set.Clear(); }
		public static string All() { return set.Count == 0 ? "none" : string.Join(", ", set.Select(kv => kv.Key + "=" + kv.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).ToArray()); }
	}
}
