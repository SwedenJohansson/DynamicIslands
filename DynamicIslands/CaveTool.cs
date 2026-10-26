using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
		public static bool EndRoom = true, Hill = true;

		public struct Piece
		{
			public string Name;
			public Vector3 Position;
			public Quaternion Rotation;
		}

		/// <summary>A cave laid out: its pieces, its mouth (the floor where it opens), its far end, its way in on the ground.</summary>
		public class Layout
		{
			public List<Piece> Pieces = new List<Piece>();
			public Vector3 Mouth, End, Dir, Flat;
			public float Slope, Length;
			public int Tunnels;
			public bool Room;
			public Vector3 RoomPivot;
			public Quaternion RoomRotation;

			/// <summary>The floor of the passage this far along it (m, flat), on the tunnel's line.</summary>
			public float FloorAt(float s) { return Mouth.y + Mathf.Tan(Slope * Mathf.Deg2Rad) * Mathf.Clamp(s, 0f, Length); }
		}

		/// <summary>
		/// Lays a cave out: the mouth's floor at mouth, the tunnel towards toward (only its direction and flat distance count),
		/// as many tunnel pieces as fit (one at least, MaxTunnels at most; none when only a room is wanted and the drag is
		/// shorter than one), sloping slope degrees, the bear's den at the far end when room.
		/// </summary>
		public static Layout Plan(Vector3 mouth, Vector3 toward, float slope, bool room)
		{
			var l = new Layout { Mouth = mouth, Room = room, Slope = Mathf.Clamp(slope, -MaxSlope, MaxSlope) };
			Vector3 flat = toward - mouth; flat.y = 0f;
			float dist = flat.magnitude;
			l.Flat = dist > 0.01f ? flat / dist : Vector3.forward;
			float a = l.Slope * Mathf.Deg2Rad;
			l.Dir = (l.Flat * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)).normalized;
			int n = Mathf.RoundToInt(dist / (Step * Mathf.Cos(a)));
			l.Tunnels = Mathf.Clamp(n, room ? 0 : 1, MaxTunnels);
			Quaternion r = Quaternion.LookRotation(l.Dir, Vector3.up);
			Vector3 entry = new Vector3(PassX, Floor - EndDip, FaceMin);
			for (int i = 0; i < l.Tunnels; i++)
				l.Pieces.Add(new Piece { Name = Tunnel, Rotation = r, Position = mouth + l.Dir * (i * Step) - r * entry });
			l.End = mouth + l.Dir * (l.Tunnels * Step);
			l.Length = l.Tunnels * Step * Mathf.Cos(a);
			if (room)
			{
				// (the den's mouth meets the tunnel's far end flush, its floor level with it; it faces back up the tunnel)
				l.RoomRotation = Quaternion.LookRotation(-l.Flat, Vector3.up);
				l.RoomPivot = l.End - l.RoomRotation * RoomMouth + Vector3.up * RoomLift;
				l.Pieces.Add(new Piece { Name = Room, Rotation = l.RoomRotation, Position = l.RoomPivot });
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
				if (!((beforeMouth || pastEnd) && Mathf.Abs(q) < ShellHalfWidth + 0.5f))
				{
					float ds = s < 0f ? -s : s > l.Length ? s - l.Length : 0f, dq = Mathf.Max(0f, Mathf.Abs(q) - ShellHalfWidth);
					float d = Mathf.Sqrt(ds * ds + dq * dq);
					float top = l.FloorAt(s) + ShellTop - Floor + EndDip + Cover;
					best = top - d * SideSlope;
					// (beside the way in, the cut's walls slope up from it: not straight up - they stood as two flat slabs)
					if (beforeMouth) best = Mathf.Min(best, l.Mouth.y + (Mathf.Abs(q) - ShellHalfWidth - 0.5f) * CutSlope);
				}
			}
			if (l.Room)
			{
				Vector3 lp = Quaternion.Inverse(l.RoomRotation) * (p - l.RoomPivot);
				float dx = Mathf.Max(0f, RoomMin.x - lp.x, lp.x - RoomMax.x), dz = Mathf.Max(0f, RoomMin.z - lp.z, lp.z - RoomMax.z);
				// (the den's own front - at its mouth - is under the tunnel's hill, or, with no tunnel, the face it opens in)
				bool front = l.Tunnels == 0 && lp.z > RoomMax.z - 0.5f && Mathf.Abs(lp.x - RoomMouth.x) < 7f;
				if (!front)
				{
					float top = l.RoomPivot.y + RoomMax.y + Cover - Mathf.Sqrt(dx * dx + dz * dz) * SideSlope;
					if (float.IsNaN(best) || top > best) best = top;
				}
			}
			return best;
		}

		/// <summary>The approach to the mouth: the ground before it as high as its floor at the mouth, rising gently further
		/// out (cut down where higher, filled where lower near the mouth), or NaN out of it. weight: how much of it to take.</summary>
		public static float ApronAt(Layout l, Vector3 p, out float weight)
		{
			weight = 0f;
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			if (s > 0.5f || s < -ApronLength - 6f || q > ApronHalf + 3f) return float.NaN;
			weight = Mathf.Clamp01((ApronHalf + 3f - q) / 3f) * Mathf.Clamp01((ApronLength + 6f + s) / 6f);
			return l.Mouth.y - 0.05f + Mathf.Max(0f, -s - ApronLength) * 0.25f; // (level up to the mouth: it rose into a hump)
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
			for (int z = 0; z < h; z++)
				for (int x = 0; x < w; x++)
				{
					Vector3 p = new Vector3(o.x + (x0 + x) / (float)(res - 1) * size.x, 0f, o.z + (z0 + z) / (float)(res - 1) * size.z);
					float y = heights[z, x] * size.y + o.y, ny = y;
					float target = hill ? HillAt(l, p) : UnderAt(l, p);
					if (!float.IsNaN(target) && target > ny) ny = target;
					float wgt, apron = ApronAt(l, p, out wgt);
					if (!float.IsNaN(apron) && wgt > 0f)
					{
						Vector3 rel = p - l.Mouth; rel.y = 0f;
						bool near = Vector3.Dot(rel, l.Flat) > -ApronLength;
						if (ny > apron || near) ny = Mathf.Lerp(ny, apron, wgt);
					}
					if (ny != y) heights[z, x] = Mathf.Clamp01((ny - o.y) / size.y);
				}
			data.SetHeights(x0, z0, heights);
		}

		/// <summary>Without a hill: the ground under the rock up to its underside (NaN: not under it), so no piece hangs in the air.</summary>
		static float UnderAt(Layout l, Vector3 p)
		{
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			float best = float.NaN;
			if (l.Tunnels > 0 && s >= 0f && s <= l.Length && q < ShellHalfWidth - 0.5f) best = l.FloorAt(s) - 1.5f;
			if (l.Room)
			{
				Vector3 lp = Quaternion.Inverse(l.RoomRotation) * (p - l.RoomPivot);
				if (lp.x > RoomMin.x + 1f && lp.x < RoomMax.x - 1f && lp.z > RoomMin.z + 1f && lp.z < RoomMax.z - 1f)
				{
					float t = l.RoomPivot.y + RoomMouth.y - 2f;
					if (float.IsNaN(best) || t > best) best = t;
				}
			}
			return best;
		}

		/// <summary>The ground a cave laid out takes up (world, flat; y: its floor to its top).</summary>
		public static Bounds Footprint(Layout l)
		{
			var b = new Bounds(l.Mouth, Vector3.zero);
			Vector3 side = Vector3.Cross(Vector3.up, l.Flat) * ShellHalfWidth;
			foreach (Vector3 c in new[] { l.Mouth, l.End })
			{
				b.Encapsulate(c + side + Vector3.up * (ShellTop + EndDip)); b.Encapsulate(c - side - Vector3.up * 4.5f);
			}
			if (l.Room)
				for (int i = 0; i < 8; i++)
					b.Encapsulate(l.RoomPivot + l.RoomRotation * new Vector3((i & 1) == 0 ? RoomMin.x : RoomMax.x, (i & 2) == 0 ? RoomMin.y : RoomMax.y, (i & 4) == 0 ? RoomMin.z : RoomMax.z));
			return b;
		}

		#endregion

		#region The den's front wall

		/// <summary>Name of the wall a den gets around the tunnel's end (a child of the den).</summary>
		public const string PlugName = "CavePlug";
		const float PlugIn = 0.8f, PlugHalf = 12f, PlugBelow = 3f, HoleHalf = 2.6f, HoleBelow = 1f, HoleUp = 5.6f;

		/// <summary>
		/// The bear den's mouth opens far wider than a tunnel: in Balboa its cliff closes the rest, here the hill does from
		/// outside only (the ground is not seen from below), so from inside the den you looked out past the tunnel's end
		/// through the hill. A den laid at a tunnel's end (ObjectProps.CavePlug) gets a rock wall across its mouth, just inside
		/// the tunnel's last piece, with a hole the size of the passage (its edges hidden in the tunnel's rock). In the editor
		/// and in a world; made again when called again.
		/// </summary>
		public static void Plug(GameObject den)
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
			go.transform.localScale = new Vector3(1f / Mathf.Max(0.001f, ls.x), 1f / Mathf.Max(0.001f, ls.y), 1f / Mathf.Max(0.001f, ls.z));
			// (in the den's frame at the size the layout uses: its mouth at +z, the tunnel past it)
			float z = RoomMax.z + PlugIn, mx = RoomMouth.x, my = RoomMouth.y;
			float x0 = Mathf.Max(RoomMin.x + 0.5f, mx - PlugHalf), x1 = Mathf.Min(RoomMax.x - 0.5f, mx + PlugHalf);
			float y0 = my - PlugBelow, y1 = RoomMax.y - 1f;
			float hx0 = mx - HoleHalf, hx1 = mx + HoleHalf, hy0 = my - HoleBelow, hy1 = my + HoleUp;
			var verts = new List<Vector3>();
			var tris = new List<int>();
			Action<float, float, float, float> quad = (ax, ay, bx, by) =>
			{
				int i = verts.Count;
				verts.Add(new Vector3(ax, ay, z)); verts.Add(new Vector3(bx, ay, z)); verts.Add(new Vector3(bx, by, z)); verts.Add(new Vector3(ax, by, z));
				tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 }); // (facing -z: into the den)
				tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); // (and the other way)
			};
			quad(x0, y0, x1, hy0);  // under the hole
			quad(x0, hy1, x1, y1);  // over it
			quad(x0, hy0, hx0, hy1); // beside it
			quad(hx1, hy0, x1, hy1);
			var mesh = new Mesh { name = PlugName };
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.SetUVs(0, verts.Select(v => new Vector2(v.x / 6f, v.y / 6f)).ToList());
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = look.sharedMaterial;
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
		}

		#endregion

		/// <summary>Whether a point is in a cave's rock: its tunnel's shell or its den's box.</summary>
		public static bool InRock(Layout l, Vector3 p)
		{
			Vector3 rel = p - l.Mouth; rel.y = 0f;
			float s = Vector3.Dot(rel, l.Flat), q = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat)));
			if (l.Tunnels > 0 && s > -0.3f && s < l.Length + 0.3f && q < ShellHalfWidth && p.y > l.FloorAt(s) - 2f && p.y < l.FloorAt(s) + ShellTop + EndDip + 0.5f) return true;
			if (l.Room)
			{
				Vector3 lp = Quaternion.Inverse(l.RoomRotation) * (p - l.RoomPivot);
				if (lp.x > RoomMin.x && lp.x < RoomMax.x && lp.y > RoomMin.y && lp.y < RoomMax.y && lp.z > RoomMin.z && lp.z < RoomMax.z) return true;
			}
			return false;
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
			foreach (EditorGameObject e in placedRoot.GetComponentsInChildren<EditorGameObject>(false))
			{
				Vector3 p = e.transform.position;
				if (p.x < min.x || p.x > max.x || p.z < min.z || p.z > max.z) continue;
				if (e.GameObjectName == Tunnel || e.GameObjectName == Room) continue;
				bool setUp = e.Props != null && e.Props.Count > 0;
				if (!setUp && InRock(l, p)) { cleared.Add(e.gameObject); continue; }
				float was = oldGround(p), now = terrain.SampleHeight(p) + o.y;
				if (Mathf.Abs(p.y - was) > 1.5f || Mathf.Abs(now - was) < 0.05f) continue;
				Vector3 to = p + Vector3.up * (now - was);
				if (!setUp && InRock(l, to)) { cleared.Add(e.gameObject); continue; }
				moved.Add(new Move { T = e.transform, From = p, To = to });
			}
			foreach (GameObject go in cleared) go.SetActive(false);
			foreach (Move m in moved) m.T.position = m.To;
		}

		#region Laying it in the editor

		/// <summary>Whether a cave is being laid (its pieces loading).</summary>
		public static bool Busy { get; private set; }

		/// <summary>The last cave laid (tests): its layout and its objects.</summary>
		public static Layout LastLayout;
		public static List<GameObject> LastPieces = new List<GameObject>();

		/// <summary>
		/// Lays a cave in the editor's island (the scenes its pieces come from loaded first): the pieces, the ground shaped
		/// around them, one undo step; the pieces selected after. Says what it did. done gets the objects (none: refused).
		/// </summary>
		public static IEnumerator Lay(Vector3 mouth, Vector3 toward, float slope, bool room, bool hill, Action<List<GameObject>> done = null)
		{
			var made = new List<GameObject>();
			Terrain terrain = terraineditor.terrain;
			GameObject placedRoot = GameObject.Find("PlacedObjects");
			if (terrain == null || placedRoot == null || Busy) { if (done != null) done(made); yield break; }
			Layout l = Plan(mouth, toward, slope, room);
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
				// (what stood in the rock goes - plants and rocks only, anything set up stays; what stood on the ground moves with it)
				List<GameObject> cleared;
				List<Move> moved;
				SettleObjects(placedRoot, terrain, heightsBefore, l, min, max, out cleared, out moved);

				var props = new Dictionary<string, string> { { ObjectProps.Skin, ObjectProps.SkinRock } };
				foreach (Piece p in l.Pieces)
				{
					GameObject go = PlaceableCatalog.Spawn(p.Name, placedRoot.transform);
					if (go == null) continue;
					go.transform.position = p.Position;
					go.transform.rotation = p.Rotation;
					foreach (Collider c in go.GetComponentsInChildren<Collider>()) c.enabled = true;
					// (the tunnels' icy rock takes the den's brown island rock; the den keeps its own)
					EditorGameObject.Attach(go, p.Name, p.Name == Tunnel ? new Dictionary<string, string>(props)
						: l.Tunnels > 0 ? new Dictionary<string, string> { { ObjectProps.CavePlug, "1" } } : null);
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

				if (DynamicIslands.EditorGizmoHandler != null)
				{
					DynamicIslands.EditorGizmoHandler.ClearTargets(false);
					foreach (GameObject go in made) DynamicIslands.EditorGizmoHandler.AddTarget(go.transform, false);
				}
				LastLayout = l;
				LastPieces = made;
				float sea = DynamicIslands.EditorWaterLevel;
				float lowest = Mathf.Min(l.Mouth.y, l.End.y);
				string what = (l.Tunnels > 0 ? l.Tunnels + " tunnel piece" + (l.Tunnels == 1 ? "" : "s") + " (" + Mathf.RoundToInt(l.Length) + " m" + (Mathf.Abs(l.Slope) > 0.5f ? ", " + (l.Slope < 0 ? "down " : "up ") + Mathf.RoundToInt(Mathf.Abs(l.Slope)) + "°" : "") + ")" : "") +
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
		static LineRenderer line;

		/// <summary>The mouse in the editor while the Cave tool is chosen: press where the mouth goes, drag the way the tunnel
		/// runs, let go to lay it; Esc or the right button lets it go.</summary>
		public static void Drag(bool overTerrain, Vector3 point, bool canUse)
		{
			if (dragFrom.HasValue && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1) || !canUse && !Input.GetMouseButton(0))) { Cancel(); return; }
			if (!dragFrom.HasValue)
			{
				if (canUse && overTerrain && Input.GetMouseButtonDown(0) && !EditorCamera.UsingMouse && !Busy) dragFrom = point;
				return;
			}
			if (Input.GetMouseButton(0))
			{
				if (overTerrain) Show(Plan(dragFrom.Value, point, Slope, EndRoom), point);
				return;
			}
			// (let go)
			Vector3 from = dragFrom.Value, to = overTerrain ? point : lastTo;
			Cancel();
			Vector3 flat = to - from; flat.y = 0f;
			if (flat.magnitude < 2f) { DynamicIslands.Notify("Cave: press where the mouth is to be and drag the way the tunnel goes, into a hill", true); return; }
			DynamicIslands.instance.StartCoroutine(Lay(from, to, Slope, EndRoom, Hill));
		}

		static Vector3 lastTo;

		static void Show(Layout l, Vector3 to)
		{
			lastTo = to;
			if (line == null)
			{
				var go = new GameObject("CaveToolPreview");
				line = go.AddComponent<LineRenderer>();
				line.material = new Material(Shader.Find("Sprites/Default"));
				line.startWidth = line.endWidth = 0.6f;
				line.startColor = new Color(1f, 0.8f, 0.3f); line.endColor = new Color(1f, 0.5f, 0.2f);
				line.useWorldSpace = true;
			}
			line.enabled = true;
			var pts = new List<Vector3> { l.Mouth + Vector3.up * 0.3f, l.End + Vector3.up * 0.3f };
			if (l.Room) pts.Add(l.RoomPivot + l.RoomRotation * new Vector3(RoomMouth.x, RoomMouth.y, 0f) + Vector3.up * 0.3f);
			line.positionCount = pts.Count;
			line.SetPositions(pts.ToArray());
			string text = "Cave: " + (l.Tunnels > 0 ? l.Tunnels + " tunnel piece(s), " + Mathf.RoundToInt(l.Length) + " m" : "no tunnel") + (l.Room ? " + a cave room" : ", open far end") + " - let go to lay it, Esc to cancel";
			if (text != shownText) { shownText = text; DynamicIslands.Notify(text, false, 2); }
		}

		static string shownText;

		public static void Cancel()
		{
			dragFrom = null;
			shownText = null;
			if (line != null) line.enabled = false;
		}

		#endregion
	}
}
