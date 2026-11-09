using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The island's ground where it runs through a cave piece's rooms - the hill a cave was set into, the ground beside a
	/// passage - over the shell's floor and under its roof, is taken out (X7): every point of it in a room is let down under
	/// the room's floor, and the cells wholly in a room are cut away (terrain holes), so the room is the rock's own floor,
	/// walls and roof. Raft draws the ground from above only: from inside a room it lay across, it was a window out. In a world
	/// once the island's objects are there; in the editor whenever the caves or the ground over them have changed - there the
	/// ground's own heights are kept (put back before each new cut, and saved as they were).
	/// </summary>
	public class CaveGround : MonoBehaviour
	{
		/// <summary>How far under a room's floor the ground in it is let down (m).</summary>
		const float Under = 0.6f;
		/// <summary>A cell steep enough that its ground can run through a room with none of its corners in it (m from its lowest corner to its highest), and how finely one is looked over.</summary>
		const float Steep = 2f;
		const int Looks = 8;

		readonly List<Transform> caves = new List<Transform>();

		/// <summary>Tests: how many ground cells the last cut took out, and how many points of the ground it let down.</summary>
		public int LastCut, LastDug;

		/// <summary>A cave piece spawned in a world under this island's objects.</summary>
		public static void Add(Transform objects, GameObject piece, string name, IDictionary<string, string> props = null)
		{
			PropInfo p = RaftProps.Get(name ?? "");
			if (objects == null || piece == null || p == null || !p.IsCave) return;
			CaveGround g = objects.GetComponent<CaveGround>() ?? objects.gameObject.AddComponent<CaveGround>();
			g.caves.Add(piece.transform);
			names[piece.transform.GetInstanceID()] = name;
			if (CaveMountain.DryFootFor(props, name)) dryFoot.Add(piece.transform.GetInstanceID()); else dryFoot.Remove(piece.transform.GetInstanceID());
		}

		// (a world's cave pieces with cave.foot=dry; the editor's say so in their EditorGameObject's props)
		static readonly HashSet<int> dryFoot = new HashSet<int>();

		static bool DryFoot(Transform t)
		{
			EditorGameObject e = t.GetComponent<EditorGameObject>();
			string name;
			return e != null ? CaveMountain.DryFootFor(e.Props, names.TryGetValue(t.GetInstanceID(), out name) ? name : null) : dryFoot.Contains(t.GetInstanceID());
		}

		// (a world's cave pieces' names, for their measures: the editor's are on their EditorGameObject)
		static readonly Dictionary<int, string> names = new Dictionary<int, string>();

		/// <summary>The island's objects all there and switched on (their colliders can be hit): the ground in its caves' rooms taken out.</summary>
		public static void Cut(Transform objects)
		{
			CaveGround g = objects != null ? objects.GetComponent<CaveGround>() : null;
			if (g == null) return;
			try { g.CutNow(); }
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Cave ground: " + e.Message); }
		}

		/// <summary>Cuts it again now (tests).</summary>
		public void CutNow()
		{
			caves.RemoveAll(t => t == null);
			LastCut = LastDug = 0;
			if (caves.Count == 0) return;
			Transform island = transform.parent != null ? transform.parent : transform;
			float started = Time.realtimeSinceStartup;
			foreach (Terrain land in island.GetComponentsInChildren<Terrain>())
			{
				int dug;
				LastCut += CutIn(land, caves, null, null, out dug);
				LastDug += dug;
			}
			if (LastCut + LastDug > 0)
				Debug.Log("[CUSTOM ISLANDS] '" + island.name + "': the ground taken out of " + caves.Count + " cave piece(s)' rooms (" + LastDug + " points let down, " + LastCut +
					" cells cut) in " + ((Time.realtimeSinceStartup - started) * 1000f).ToString("F0") + " ms");
		}

		static float nextLook;
		static double lastLook = double.NaN;
		static bool changed;
		static Terrain lastLand;
		static readonly List<Vector2Int> editorCut = new List<Vector2Int>();
		// (the editor's ground let down: point (z * resolution + x) -> its own height, and the height it was let down to)
		static readonly Dictionary<int, Vector2> editorDug = new Dictionary<int, Vector2>();

		/// <summary>
		/// The editor: the same cut in the island being made, so its caves look there as they will in a world. The ground is
		/// sculpted and the caves moved at any time: once a second the caves' places and the ground over them are compared with
		/// the last look, and once they have stopped changing the cut is made again (the last one put back first). busy: a brush
		/// stroke under way.
		/// </summary>
		public static void EditorTick(Terrain land, bool busy)
		{
			if (land == null || busy || Time.realtimeSinceStartup < nextLook) return;
			nextLook = Time.realtimeSinceStartup + 1f;
			try
			{
				List<Transform> caves = EditorCaves();
				double look = Look(land, caves);
				if (land != lastLand || look != lastLook) { lastLook = look; changed = true; if (land != lastLand) { lastLand = land; editorCut.Clear(); editorDug.Clear(); } return; }
				if (!changed) return;
				changed = false;
				EditorCut(land, caves);
			}
			catch (System.Exception e) { changed = false; Debug.LogWarning("[CUSTOM ISLANDS] Cave ground (editor): " + e.Message); }
		}

		/// <summary>The editor's cut made now (tests). How many cells are cut; dug: how many points are let down.</summary>
		public static int EditorNow(Terrain land, out int dug)
		{
			dug = 0;
			if (land == null) return 0;
			if (land != lastLand) { lastLand = land; editorCut.Clear(); editorDug.Clear(); }
			changed = false;
			dug = EditorCut(land, EditorCaves());
			return editorCut.Count;
		}

		/// <summary>The editor's island was just replaced (a new one, one loaded): its heights are its own, the ones let down are forgotten; cut again soon.</summary>
		public static void EditorForget()
		{
			editorDug.Clear();
			lastLook = double.NaN;
		}

		/// <summary>The editor's island being saved: its heights (all of them, [z, x]) as they are without the cut - the ground's own.</summary>
		public static void EditorOwnHeights(Terrain land, float[,] heights)
		{
			if (land == null || land != lastLand || heights == null || editorDug.Count == 0) return;
			int res = heights.GetLength(0);
			foreach (KeyValuePair<int, Vector2> d in editorDug)
			{
				int z = d.Key / res, x = d.Key % res;
				if (z < res && x < res && Mathf.Abs(heights[z, x] - d.Value.y) < 1e-6f) heights[z, x] = d.Value.x;
			}
		}

		static List<Transform> EditorCaves()
		{
			var caves = new List<Transform>();
			GameObject placed = GameObject.Find("PlacedObjects");
			if (placed != null)
				foreach (EditorGameObject e in placed.GetComponentsInChildren<EditorGameObject>(false))
				{
					PropInfo p = RaftProps.Get(e.GameObjectName ?? "");
					if (p != null && p.IsCave) caves.Add(e.transform);
				}
			return caves;
		}

		/// <summary>The last cut put back (holes filled, the ground let down - and not sculpted since - at its own height), and made again. How many points are let down.</summary>
		static int EditorCut(Terrain land, List<Transform> caves)
		{
			TerrainData data = land.terrainData;
			int res = data.heightmapResolution, cells = res - 1;
			if (editorCut.Count > 0)
			{
				bool[,] solid = data.GetHoles(0, 0, cells, cells);
				foreach (Vector2Int c in editorCut)
					if (c.x < cells && c.y < cells) solid[c.y, c.x] = true;
				data.SetHoles(0, 0, solid);
				editorCut.Clear();
			}
			if (editorDug.Count > 0)
			{
				float[,] h = data.GetHeights(0, 0, res, res);
				int back = 0;
				foreach (KeyValuePair<int, Vector2> d in editorDug)
				{
					int z = d.Key / res, x = d.Key % res;
					if (z < res && x < res && Mathf.Abs(h[z, x] - d.Value.y) < 1e-6f) { h[z, x] = d.Value.x; back++; }
				}
				// (none of them still where they were let down: the island was replaced - they are another island's)
				if (back == 0) editorDug.Clear();
				else data.SetHeights(0, 0, h);
			}
			int dug = 0;
			if (caves.Count > 0) CutIn(land, caves, editorCut, editorDug, out dug);
			lastLook = Look(land, caves); // (as it is now, cut: the cut itself is no change)
			return dug;
		}

		/// <summary>The caves' places and the ground in their reach, as one number: changed when either has.</summary>
		static double Look(Terrain land, List<Transform> caves)
		{
			TerrainData data = land.terrainData;
			double look = caves.Count;
			for (int i = 0; i < caves.Count; i++)
			{
				Transform t = caves[i];
				Vector3 p = t.position, r = t.eulerAngles, s = t.lossyScale;
				look += (i + 1) * (p.x * 1.31 + p.y * 1.73 + p.z * 1.97 + r.x * 0.37 + r.y * 0.41 + r.z * 0.43 + s.x * 3.1 + s.y * 3.7 + s.z * 4.3);
				HashSet<Collider> own;
				Bounds b;
				int x0, x1, z0, z1;
				if (!Reach(land, t, out own, out b, out x0, out x1, out z0, out z1)) continue;
				float[,] h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
				double sum = 0;
				foreach (float v in h) sum += v;
				look += (i + 1) * sum * 7.0;
			}
			return look;
		}

		/// <summary>A cave piece's colliders, their bounds, and the ground points under them (false: none).</summary>
		static bool Reach(Terrain land, Transform cave, out HashSet<Collider> own, out Bounds b, out int x0, out int x1, out int z0, out int z1)
		{
			// (a cave tool mouth's frame is not the piece's room: CutTunnels takes the ground under it)
			own = new HashSet<Collider>(cave.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.name != CaveTool.MouthName && c.name != CaveTool.JoinName));
			b = new Bounds();
			x0 = x1 = z0 = z1 = 0;
			if (own.Count == 0) return false;
			b = own.First().bounds;
			foreach (Collider c in own) b.Encapsulate(c.bounds);
			TerrainData data = land.terrainData;
			int cells = data.heightmapResolution - 1;
			float sx = data.size.x / cells, sz = data.size.z / cells;
			Vector3 o = land.transform.position;
			x0 = Mathf.Max(0, Mathf.FloorToInt((b.min.x - o.x) / sx)); x1 = Mathf.Min(cells, Mathf.CeilToInt((b.max.x - o.x) / sx));
			z0 = Mathf.Max(0, Mathf.FloorToInt((b.min.z - o.z) / sz)); z1 = Mathf.Min(cells, Mathf.CeilToInt((b.max.z - o.z) / sz));
			return x0 < x1 && z0 < z1;
		}

		/// <summary>
		/// Takes the ground of land out of the caves' rooms: every point of it in a room let down under the room's floor, and the
		/// cells wholly in rooms cut. cut: gets each cell cut (x, z); dugTo: gets each point let down (its own height, and the new
		/// one). How many cells are cut; dug: how many points let down.
		/// </summary>
		static int CutIn(Terrain land, List<Transform> caves, List<Vector2Int> cut, Dictionary<int, Vector2> dugTo, out int dug)
		{
			dug = 0;
			TerrainData data = land.terrainData;
			int res = data.heightmapResolution, cells = res - 1;
			float sx = data.size.x / cells, sz = data.size.z / cells, sy = data.size.y;
			Vector3 o = land.transform.position;
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.SyncTransforms();
			// (each point of the ground: in one of the rooms - a piece's own roof over it and its floor under it, both seen
			// from inside - and the height of the floor under it)
			var inside = new bool[res, res];
			var floor = new Dictionary<int, float>();
			var aprons = new List<KeyValuePair<Transform, List<int>>>(); // (each piece's own room points: its mouth's apron)
			var apronBoth = new HashSet<Transform>();
			int lo = res, hi = -1, left = res, right = -1;
			try
			{
				foreach (Transform cave in caves)
				{
					HashSet<Collider> own;
					Bounds b;
					int x0, x1, z0, z1;
					if (cave == null || !Reach(land, cave, out own, out b, out x0, out x1, out z0, out z1)) continue;
					bool walled = DryFoot(cave);
					// (every cave's own room points: its mouths' aprons - a walled den's both ways, an open cave's only down, 11-08)
					List<int> mine = new List<int>();
					aprons.Add(new KeyValuePair<Transform, List<int>>(cave, mine));
					if (walled) apronBoth.Add(cave);
					float[,] h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
					for (int z = z0; z <= z1; z++)
						for (int x = x0; x <= x1; x++)
						{
							if (inside[z, x]) continue;
							Vector3 p = new Vector3(o.x + x * sx, o.y + h[z - z0, x - x0] * sy, o.z + z * sz);
							float under, over;
							if (p.y <= b.min.y || p.y >= b.max.y || !Inside(p, own, b, out under, out over) || (walled && !Walled(p, own))) continue;
							inside[z, x] = true;
							floor[z * res + x] = p.y - under;
							if (mine != null) mine.Add(z * res + x);
							lo = Mathf.Min(lo, z); hi = Mathf.Max(hi, z); left = Mathf.Min(left, x); right = Mathf.Max(right, x);
						}
				}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			if (hi >= 0)
			{
				// (each point in a room let down under its floor: the ground between it and one outside the room then runs down
				// into the rock, under the floor, seen from above - not across the room, seen from below - however few of a cell's
				// corners are in it)
				int w = right - left + 1, d = hi - lo + 1;
				float[,] heights = data.GetHeights(left, lo, w, d), own0 = dugTo != null ? (float[,])heights.Clone() : null;
				foreach (KeyValuePair<int, float> f in floor)
				{
					int z = f.Key / res, x = f.Key % res;
					float to = Mathf.Clamp01((f.Value - Under - o.y) / sy);
					if (to >= heights[z - lo, x - left]) continue;
					heights[z - lo, x - left] = to;
					dug++;
				}
				if (dug > 0)
				{
					data.SetHeights(left, lo, heights);
					if (dugTo != null)
					{
						float[,] now = data.GetHeights(left, lo, w, d); // (as kept: the heights are stored rounded)
						foreach (KeyValuePair<int, float> f in floor)
						{
							int z = f.Key / res, x = f.Key % res;
							if (now[z - lo, x - left] < own0[z - lo, x - left]) dugTo[f.Key] = new Vector2(own0[z - lo, x - left], now[z - lo, x - left]);
						}
					}
				}
			}
			foreach (KeyValuePair<Transform, List<int>> a in aprons) dug += Apron(land, a.Key, a.Value, inside, floor, dugTo, apronBoth.Contains(a.Key));
			// (and the cells whose ground still runs steeply through a room between their corners)
			var across = new List<Vector2Int>();
			var roomAt = new Dictionary<int, Vector2>();
			try { Across(land, caves, inside, across, roomAt); CutTunnels(land, caves, across); }
			finally { Physics.queriesHitBackfaces = backs0; }
			dug += LipDown(land, across, roomAt, inside, dugTo);
			if (hi < 0 && across.Count == 0) return 0;
			// (a cell goes when all four of its corners are in a room: none is cut out past a wall, where it would open a gap
			// beside the rock)
			bool[,] solid = data.GetHoles(0, 0, cells, cells);
			int n = 0;
			for (int z = lo; z < hi; z++)
				for (int x = left; x < right; x++)
					if (solid[z, x] && inside[z, x] && inside[z, x + 1] && inside[z + 1, x] && inside[z + 1, x + 1])
					{
						solid[z, x] = false;
						n++;
						if (cut != null) cut.Add(new Vector2Int(x, z));
					}
			foreach (Vector2Int c in across)
				if (solid[c.y, c.x])
				{
					solid[c.y, c.x] = false;
					n++;
					if (cut != null) cut.Add(c);
				}
			if (n > 0) data.SetHoles(0, 0, solid);
			return n;
		}

		const float ApronReach = 6f; // (4 m left a steep tongue, Caisson Yard r31)
		/// <summary>How far round to the side of a den's open end its apron reaches (the cosine to the way out where it fades out).</summary>
		const float ApronSide = -0.35f;
		/// <summary>The share of a den box's half width out in front of which its apron is full height (the rest rounds off).</summary>
		const float ApronCore = 0.35f;
		/// <summary>How far back from an open end of a piece's box the ground beside it is eased in too (m).</summary>
		const float ApronBeside = 3f;
		/// <summary>The band behind a den's open end whose room points give the floor its apron eases to (m), and how many times the moved points are rounded off.</summary>
		const float EndBand = 2f; const int ApronRound = 3;
		const float SlabDeep = 2.7f; // (how far in a den's slab is looked for)
		const float SlabFlat = 1.6f, SlabFlatBy = 0.15f; // (how far on in its flat top is read, and how far under it counts as its rounded edge)
		const float SlabIn = 0.3f, SlabLook = 1.5f, SlabSink = 0.05f, SlabUnder = 0.5f; // (SlabSink/Under: the sand under its top just past it and under it - at its top, the sand came up through the slab's dips in tongues, 11-08 r39)
		// // (Apron: how far inside a den's box its slab's top is read, and from how high over its floor)

		/// <summary>The highest top of these colliders straight under from (NegativeInfinity: none within 2 SlabLook).</summary>
		static float SlabTop(Collider[] cols, Vector3 from)
		{
			float top = float.NegativeInfinity;
			var ray = new Ray(from, Vector3.down);
			foreach (Collider c in cols)
			{
				if (c == null || !c.enabled || c.isTrigger) continue;
				RaycastHit hit;
				if (c.Raycast(ray, out hit, 2f * SlabLook)) top = Mathf.Max(top, hit.point.y);
			}
			return top;
		}
		/// <summary>How far under an open cave's floor its apron digs the ground (m: crossing the floor at a slant, not grazing it in a
		/// notched line, gen-large r36), and within how far of the end the eased ground is kept out of the rounding.</summary>
		const float OpenSink = 0.3f, ApronPin = 1.5f, OpenNose = 0.7f;
		/// <summary>How steeply a den's raised sand falls away to the sand round about (rise per metre), and how much further than
		/// ApronReach it may spread.</summary>
		const float ApronSlope = 0.3f, ApronSpread = 8f;

		/// <summary>
		/// The ground out in front of a dry-foot den's open end eased to its floor (11-08 user: before every den's mouth the sand
		/// stood as a flat slab with a square edge over the den's floor, or the floor's lip stood square on the sand): each point
		/// out past the room, within ApronReach of its nearest room point and ahead of it (towards the open end), goes towards
		/// that point's floor less 5 cm, all the way at the room and none at ApronReach, on a smooth curve - a rounded slope in.
		/// How many points it moved.
		/// </summary>
		static int Apron(Terrain land, Transform cave, List<int> pts, bool[,] inside, Dictionary<int, float> floor, Dictionary<int, Vector2> dugTo, bool raise = true)
		{
			string name;
			PropInfo info = cave != null && names.TryGetValue(cave.GetInstanceID(), out name) ? RaftProps.Get(name) : null;
			if (info == null || !info.IsCave || pts.Count == 0 || !(info.OpenPlus || info.OpenMinus)) return 0;
			Vector3 along = cave.rotation * (info.Axis == 0 ? Vector3.right : Vector3.forward);
			along.y = 0f;
			if (along.sqrMagnitude < 1e-4f) return 0;
			along.Normalize();
			TerrainData data = land.terrainData;
			int res = data.heightmapResolution;
			float sx = data.size.x / (res - 1), sz = data.size.z / (res - 1), sy = data.size.y;
			Vector3 o = land.transform.position;
			int rx = Mathf.CeilToInt((ApronReach + ApronSpread) / sx), rz = Mathf.CeilToInt((ApronReach + ApronSpread) / sz);
			int x0 = res, x1 = -1, z0 = res, z1 = -1;
			foreach (int k in pts) { int z = k / res, x = k % res; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z); }
			x0 = Mathf.Max(0, x0 - rx); x1 = Mathf.Min(res - 1, x1 + rx); z0 = Mathf.Max(0, z0 - rz); z1 = Mathf.Min(res - 1, z1 + rz);
			Vector3 sideDir = Vector3.Cross(Vector3.up, along).normalized;
			Vector3 mid = new Vector3(cave.position.x, 0f, cave.position.z);
			// (the piece's box at its scale over the measured one - by its whole scale a bear den's came out 89 m wide - for the
			// ground right across its floor's front: the den's floor slab is as wide as its rock, and beside a way in only as wide
			// as its room it stood on the sand on dark sides, Caisson Yard r35)
			int ax = info.Axis == 0 ? 0 : 2, sideAx = 2 - ax;
			Vector3 sc = CaveMountain.OverMeasured(cave, info.Name);
			Vector3 boxMid = cave.position + cave.rotation * Vector3.Scale(info.Centre, sc);
			float boxS = Vector3.Dot(new Vector3(boxMid.x, 0f, boxMid.z) - mid, along), boxL = Vector3.Dot(new Vector3(boxMid.x, 0f, boxMid.z) - mid, sideDir);
			float halfLen = Mathf.Abs(info.Size[ax] * sc[ax]) * 0.5f, halfW = Mathf.Abs(info.Size[sideAx] * sc[sideAx]) * 0.5f;
			// (and over the box's open ends with their reach round them)
			for (int e = 0; e < 2; e++)
			{
				if (!(e == 0 ? info.OpenPlus : info.OpenMinus)) continue;
				for (int c = -1; c <= 1; c += 2)
				{
					Vector3 w = boxMid + along * ((e == 0 ? 1f : -1f) * halfLen) + sideDir * (c * halfW);
					int gx = Mathf.RoundToInt((w.x - o.x) / sx), gz = Mathf.RoundToInt((w.z - o.z) / sz);
					x0 = Mathf.Max(0, Mathf.Min(x0, gx - rx)); x1 = Mathf.Min(res - 1, Mathf.Max(x1, gx + rx));
					z0 = Mathf.Max(0, Mathf.Min(z0, gz - rz)); z1 = Mathf.Min(res - 1, Mathf.Max(z1, gz + rz));
				}
			}
			float[,] h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
			// (ahead of an open end, the distance from the room's end - a straight end with rounded corners - not from the nearest
			// room point: those stand on the ground's grid, and the sand met the floor along their staircase, a notched lip,
			// gen-large and Caisson Yard's raised tongue with hard sides, 11-08 r26/r31 user; the floor it eases to from the room
			// points at the room's end)
			float[] endAt = { float.NegativeInfinity, float.NegativeInfinity }, latLo = { float.PositiveInfinity, float.PositiveInfinity },
				latHi = { float.NegativeInfinity, float.NegativeInfinity }, endFloor = { float.PositiveInfinity, float.PositiveInfinity };
			bool[] open = { info.OpenPlus, info.OpenMinus };
			float[] endSum = { 0f, 0f }; int[] endN = { 0, 0 };
			foreach (int k in pts)
			{
				Vector3 pw = new Vector3(o.x + (k % res) * sx, 0f, o.z + (k / res) * sz) - mid;
				float s = Vector3.Dot(pw, along);
				endAt[0] = Mathf.Max(endAt[0], s); endAt[1] = Mathf.Max(endAt[1], -s);
			}
			// (the floor at each open end - its lowest room point within EndBand m of the end: ahead of the end all the ground eases to
			// that one height, not each point to its nearest room point's - the floor's bumps left the sand's edge over it notched, gen-large 11-08 r33)
			foreach (int k in pts)
			{
				Vector3 pw = new Vector3(o.x + (k % res) * sx, 0f, o.z + (k / res) * sz) - mid;
				float s = Vector3.Dot(pw, along), l = Vector3.Dot(pw, sideDir);
				for (int e = 0; e < 2; e++)
				{
					if ((e == 0 ? s : -s) < endAt[e] - EndBand) continue;
					latLo[e] = Mathf.Min(latLo[e], l); latHi[e] = Mathf.Max(latHi[e], l); endFloor[e] = Mathf.Min(endFloor[e], floor[k]); endSum[e] += floor[k]; endN[e]++;
				}
			}
			// (a den: to its end's mean floor - its lowest left the floor slab's front edge standing on the sand, Caisson Yard r36)
			for (int e = 0; e < 2; e++) if (raise && endN[e] > 0) endFloor[e] = endSum[e] / endN[e];
			var movedAt = new List<int>();
			Collider[] cols = cave.GetComponentsInChildren<Collider>();
			Physics.SyncTransforms();
			var upAt = new HashSet<int>();
			var raisedAt = new List<int>();
			int slabHits = 0;
			int moved = 0, nearN = 0, offSide = 0, lower = 0, wet = 0;
			for (int z = z0; z <= z1; z++)
				for (int x = x0; x <= x1; x++)
				{
					if (inside[z, x]) continue;
					float best = ApronReach + 2f, to = 0f;
					bool byBox = false, slab = false, under = false, dipOnly = false;
					float sinkBy = SlabUnder;
					Vector3 away = Vector3.zero;
					foreach (int k in pts)
					{
						float dx = (x - k % res) * sx, dz = (z - k / res) * sz, d = Mathf.Sqrt(dx * dx + dz * dz);
						if (d < best) { best = d; to = floor[k]; away = new Vector3(dx, 0f, dz); }
					}
					{
						Vector3 pw = new Vector3(o.x + x * sx, 0f, o.z + z * sz) - mid;
						float s = Vector3.Dot(pw, along), l = Vector3.Dot(pw, sideDir);
						int e = !open[1] || (open[0] && s - endAt[0] >= -s - endAt[1]) ? 0 : 1;
						// (its sides measured from ApronCore of its half width: from the box's own sides, the sand out in front stood as a
						// flat tongue the box's width with straight sides, 11-09 r47 - now a rounded fan; a den's only)
						float past = (e == 0 ? s - boxS : boxS - s) - halfLen, lat = Mathf.Max(0f, Mathf.Abs(l - boxL) - halfW * (raise ? ApronCore : 1f));
						// (and beside its last ApronBeside m too: the floor stood out of the sand there on bare, dark sides, a raised
						// tongue between them - Caisson Yard r31)
						float box = Mathf.Sqrt(Mathf.Max(past, 0f) * Mathf.Max(past, 0f) + lat * lat);
						if (past > -ApronBeside && box < ApronReach && !float.IsInfinity(endFloor[e]) && (box < best || best >= ApronReach))
						{
							// (to the piece's own floor slab's top at the nearest point of its box's edge, both ways and round its sides
							// too: the room's floor stood lower than a den's slab, whose front stood out of the sand on a dark face -
							// Shelter Atoll's den (down only) and Caisson Yard, r37)
							// (read further in until it is met: a den's slab ends a little inside its box, so at SlabIn alone it was missed and
							// the sand went to the room's lower floor - the slab's front stood 0.7-1.1 m over it on a straight face, 11-09 r44)
							// (and on in, SlabFlat m past where it is met, to its flat top: its front edge is rounded down, and the sand went to
							// that lower edge - the flat top's front stood over it on a straight face, 11-09 r47)
							float top = float.NegativeInfinity, metAt = float.PositiveInfinity;
							for (float sinIn = SlabIn; sinIn <= Mathf.Min(SlabDeep, metAt + SlabFlat); sinIn += 0.4f)
							{
								float sIn = Mathf.Clamp(s, boxS - halfLen + sinIn, boxS + halfLen - sinIn), lIn = Mathf.Clamp(l, boxL - halfW + sinIn, boxL + halfW - sinIn);
								Vector3 at = mid + along * sIn + sideDir * lIn;
								float got = SlabTop(cols, new Vector3(at.x, endFloor[e] + SlabLook, at.z));
								if (float.IsNegativeInfinity(got) || Mathf.Abs(got - endFloor[e]) >= SlabLook) continue;
								if (float.IsInfinity(metAt)) metAt = sinIn;
								top = Mathf.Max(top, got);
							}
							if (!float.IsNegativeInfinity(top) && Mathf.Abs(top - endFloor[e]) < SlabLook && top > 0.5f) // (not out of the sea: see the wet ones below)
							{
								best = Mathf.Max(box, 0.01f); byBox = true; to = top; slab = true; slabHits++;
								// (under only where the slab is over the point itself: it ends inside the box, and the sand between its
								// front and the box's edge went SlabUnder under its top - a straight face with a dark seam under it, r46)
								// (and only where it is near its flat top: over its rounded front edge the sand covers it)
								under = past <= 0f && Mathf.Abs(l - boxL) <= halfW && SlabTop(cols, new Vector3(o.x + x * sx, endFloor[e] + SlabLook, o.z + z * sz)) > top - SlabFlatBy;
							}
							else if (raise || lat <= 0f) // (an open cave's sides not dug down: only across its way in)
							{ best = Mathf.Max(box, 0.01f); byBox = true; to = endFloor[e]; }
						}
						// (an open cave: a point under its own floor goes SlabUnder under it, whatever its box says - the floor's edge is
						// its own, not the box's, and the sand crossing the sloping floor along the terrain's grid stood over it as a
						// notched lip, gen-large 11-08 r26-r44 user; the floor's own edge is now where the sand meets it)
						// (all along it, not only its last ApronBeside m: along its open side the sand stood over the floor in a
						// notched line further in, gen-large r52)
						if (!raise && !float.IsInfinity(endFloor[e]))
						{
							Vector3 at = new Vector3(o.x + x * sx, 0f, o.z + z * sz);
							float here = SlabTop(cols, new Vector3(at.x, endFloor[e] + SlabLook, at.z));
							if (!float.IsNegativeInfinity(here) && Mathf.Abs(here - endFloor[e]) < SlabLook && here > 0.5f)
							{
								// (only down, and to just under the floor where it runs down into the sand: sunk SlabUnder under every
								// point of it, the sand crossed its rounded nose along the terrain's grid, a sawtooth line over a bare
								// dirt band, gen-large r26-r49 - now the sand meets it along its own smooth contour; SlabUnder kept
								// under its flat inner part, where the sand came up through its dips)
								bool nose = false;
								for (int dir = 0; dir < 4 && !nose; dir++)
								{
									Vector3 by = at + new Vector3(dir == 0 ? OpenNose : dir == 1 ? -OpenNose : 0f, 0f, dir == 2 ? OpenNose : dir == 3 ? -OpenNose : 0f);
									float there = SlabTop(cols, new Vector3(by.x, endFloor[e] + SlabLook, by.z));
									nose = float.IsNegativeInfinity(there) || there < here - 0.25f;
								}
								best = 0.01f; byBox = true; to = here; slab = true; under = true; dipOnly = true;
								sinkBy = nose ? SlabSink : SlabUnder;
							}
						}
					}
					if (best >= ApronReach || best < 1e-3f) continue;
					nearN++;
					// (an open cave: only its dry ways in, and only cut down - the sand's step lip over a Balboa cave's floor, 11-08)
					if (!raise && !slab && to < 0.5f) { wet++; continue; }
					float ahead = Vector3.Dot(away / best, along);
					// (eased in round the sides too, less the further to the side: a cone cut off at 60 degrees left the eased sand a raised
					// tongue with hard sides, Caisson Yard, and the sand beside it a notched lip over the den's floor, gen-large, 11-08 r26 user)
					float side = Mathf.Max(info.OpenPlus ? ahead : -1f, info.OpenMinus ? -ahead : -1f);
					float turn = byBox ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ApronSide, 0.6f, side));
					if (turn <= 0f) { offSide++; continue; }
					float own = h[z - z0, x - x0], target = Mathf.Clamp01((to - (slab ? (under ? sinkBy : SlabSink) : raise ? 0.05f : OpenSink) - o.y) / sy);
					// (sloping down from the start and flat only where it meets the sand: a smoothstep left a flat tongue before the
					// floor, 11-09 r47)
					float t = best / ApronReach, wgt = (1f - Mathf.Sin(t * Mathf.PI * 0.5f)) * turn;
					float now = Mathf.Lerp(own, target, wgt);
					if (Mathf.Abs(now - own) * sy < 0.02f || (!raise && (!slab || dipOnly) && now > own)) { lower++; continue; }
					h[z - z0, x - x0] = now;
					if (raise && now > own) raisedAt.Add((z - z0) * (x1 - x0 + 1) + (x - x0));
					if (best > ApronPin) { movedAt.Add((z - z0) * (x1 - x0 + 1) + (x - x0)); if (slab) upAt.Add((z - z0) * (x1 - x0 + 1) + (x - x0)); } // (the edge at the floor not rounded down off it: Caisson Yard r36)
					moved++;
					if (dugTo != null && !dugTo.ContainsKey(z * res + x)) dugTo[z * res + x] = new Vector2(own, -1f);
				}
			// (then rounded off a few times among the points it moved: the eased sand still met the floor along a jagged, notched
			// edge, gen-large; and a raised tongue's sides stood hard, Caisson Yard - 11-08 r33 user; down-only stays down only)
			int rw = x1 - x0 + 1;
			// (a den's raised sand then spread out sideways at no more than ApronSlope: eased to the floor only within its reach, it
			// stood as a raised tongue with steep, straight sides over the lower sand beside it, Caisson Yard, Shelter Atoll and
			// Canopy Stair 11-09 r47-r51 user - now a mound whose sides slope down to the sand round about)
			if (raise && raisedAt.Count > 0)
			{
				int rh = z1 - z0 + 1;
				var m = new float[rh, rw];
				for (int a = 0; a < rh; a++) for (int c = 0; c < rw; c++) m[a, c] = float.NegativeInfinity;
				foreach (int q in raisedAt) m[q / rw, q % rw] = h[q / rw, q % rw];
				float fx = ApronSlope * sx / sy, fz = ApronSlope * sz / sy, fd = ApronSlope * Mathf.Sqrt(sx * sx + sz * sz) / sy;
				for (int pass = 0; pass < 2; pass++)
					for (int dir = 0; dir < 2; dir++)
						for (int i = 0; i < rh; i++)
							for (int j = 0; j < rw; j++)
							{
								int a = dir == 0 ? i : rh - 1 - i, c = dir == 0 ? j : rw - 1 - j, st = dir == 0 ? -1 : 1;
								if (inside[a + z0, c + x0]) continue;
								float v = m[a, c];
								if (a + st >= 0 && a + st < rh)
								{
									v = Mathf.Max(v, m[a + st, c] - fz);
									if (c + st >= 0 && c + st < rw) v = Mathf.Max(v, m[a + st, c + st] - fd);
									if (c - st >= 0 && c - st < rw) v = Mathf.Max(v, m[a + st, c - st] - fd);
								}
								if (c + st >= 0 && c + st < rw) v = Mathf.Max(v, m[a, c + st] - fx);
								m[a, c] = v;
							}
				int spread = 0;
				for (int a = 0; a < rh; a++)
					for (int c = 0; c < rw; c++)
					{
						if (inside[a + z0, c + x0] || !(m[a, c] > h[a, c] + 0.02f / sy)) continue;
						if (dugTo != null && !dugTo.ContainsKey((a + z0) * res + c + x0)) dugTo[(a + z0) * res + c + x0] = new Vector2(h[a, c], -1f);
						h[a, c] = m[a, c]; movedAt.Add(a * rw + c); spread++; moved++;
					}
				Debug.Log("[CUSTOM ISLANDS] Apron " + cave.name + ": " + spread + " points raised round its sand");
			}
			var orig = (float[,])h.Clone();
			for (int pass = 0; pass < ApronRound; pass++)
				foreach (int q in movedAt)
				{
					int zz = q / rw, xx = q % rw;
					float sum = 0f; int n = 0;
					for (int a = zz - 1; a <= zz + 1; a++)
						for (int c = xx - 1; c <= xx + 1; c++)
							if (a >= 0 && c >= 0 && a <= z1 - z0 && c < rw && !inside[a + z0, c + x0]) { sum += h[a, c]; n++; }
					if (n < 5) continue;
					float avg = sum / n;
					h[zz, xx] = raise || upAt.Contains(q) ? avg : Mathf.Min(avg, orig[zz, xx]);
				}
			Debug.Log("[CUSTOM ISLANDS] Apron " + cave.name + (raise ? " (both ways)" : " (down only)") + ": " + pts.Count + " room points, " + nearN + " near, "
				+ moved + " moved, " + offSide + " to the side or behind, " + lower + " already there or lower, " + wet + " at the sea, " + slabHits + " to the slab's top");
			if (moved == 0) return 0;
			data.SetHeights(x0, z0, h);
			for (int e = 0; e < 2; e++) // DEBUGPROBE
			{
				if (!open[e]) continue;
				string row = "";
				for (float d = -3f; d <= 4f; d += 0.5f)
					for (float lw = 0f; lw <= 0f; lw += 1f)
					{
						Vector3 pp = boxMid + along * ((e == 0 ? 1f : -1f) * (halfLen + d)) + sideDir * (lw * (halfW - 1f));
						float top = SlabTop(cols, new Vector3(pp.x, endFloor[e] + SlabLook, pp.z));
						row += string.Format(" [{0:0},{1:0}] g{2:0.00} s{3:0.00}", d, lw, land.SampleHeight(pp) + o.y, top);
					}
				Debug.Log("[CUSTOM ISLANDS] ApronProbe " + cave.name + " end " + e + " floor " + endFloor[e].ToString("0.00") + row);
			}
			if (dugTo != null)
			{
				// (as kept: the heights are stored rounded, and the editor puts back only a point still at its kept height)
				float[,] kept = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
				for (int z = z0; z <= z1; z++)
					for (int x = x0; x <= x1; x++)
					{
						Vector2 v;
						if (dugTo.TryGetValue(z * res + x, out v) && v.y < 0f) dugTo[z * res + x] = new Vector2(v.x, kept[z - z0, x - x0]);
					}
			}
			return moved;
		}

		/// <summary>
		/// The cells whose ground runs steeply through a room between their corners, none of them in it - a slope from over a
		/// passage's roof down under its floor, where it ends in a hole in the ground (X7): from inside, seen from behind, a
		/// window out. Only where the room is under the cell's middle, so none opens a gap beside the rock.
		/// </summary>
		/// <summary>
		/// The corners of the cells cut across a room (Across) that stand in the room's height but outside it - out past its
		/// roof, at a way in - let down to just under its floor: they stood as the cut's edge over the floor, a notched sand lip
		/// across the way in (gen-large-265783, 11-08 r26-r32 user). How many points it moved.
		/// </summary>
		static int LipDown(Terrain land, List<Vector2Int> across, Dictionary<int, Vector2> roomAt, bool[,] inside, Dictionary<int, Vector2> dugTo)
		{
			TerrainData data = land.terrainData;
			int res = data.heightmapResolution, cells = res - 1;
			float sy = data.size.y, oy = land.transform.position.y;
			var to = new Dictionary<int, float>(); // (point: its new height, 0-1)
			foreach (Vector2Int c in across)
			{
				Vector2 fr;
				if (!roomAt.TryGetValue(c.y * cells + c.x, out fr)) continue;
				for (int dz = 0; dz <= 1; dz++)
					for (int dx = 0; dx <= 1; dx++)
					{
						int x = c.x + dx, z = c.y + dz;
						if (inside[z, x]) continue;
						float y = oy + data.GetHeight(x, z);
						if (!(y > fr.x - 0.05f && y < fr.y)) continue;
						float t = Mathf.Clamp01((fr.x - 0.05f - oy) / sy), was;
						if (!to.TryGetValue(z * res + x, out was) || t < was) to[z * res + x] = t;
					}
			}
			foreach (KeyValuePair<int, float> p in to)
			{
				int z = p.Key / res, x = p.Key % res;
				float own = data.GetHeight(x, z) / sy;
				data.SetHeights(x, z, new float[,] { { p.Value } });
				if (dugTo != null && !dugTo.ContainsKey(p.Key)) dugTo[p.Key] = new Vector2(own, data.GetHeights(x, z, 1, 1)[0, 0]);
			}
			return to.Count;
		}

		static void Across(Terrain land, List<Transform> caves, bool[,] inside, List<Vector2Int> found, Dictionary<int, Vector2> roomAt = null)
		{
			TerrainData data = land.terrainData;
			int cells = data.heightmapResolution - 1;
			float sx = data.size.x / cells, sz = data.size.z / cells, sy = data.size.y;
			Vector3 o = land.transform.position;
			var seen = new HashSet<int>();
			foreach (Transform cave in caves)
			{
				HashSet<Collider> own;
				Bounds b;
				int x0, x1, z0, z1;
				if (cave == null || !Reach(land, cave, out own, out b, out x0, out x1, out z0, out z1)) continue;
				float[,] h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
				for (int z = z0; z < z1; z++)
					for (int x = x0; x < x1; x++)
					{
						if (seen.Contains(z * cells + x) || (inside[z, x] && inside[z, x + 1] && inside[z + 1, x] && inside[z + 1, x + 1])) continue;
						float c00 = h[z - z0, x - x0], c10 = h[z - z0, x + 1 - x0], c01 = h[z + 1 - z0, x - x0], c11 = h[z + 1 - z0, x + 1 - x0];
						float top = o.y + Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c01, c11)) * sy, bottom = o.y + Mathf.Min(Mathf.Min(c00, c10), Mathf.Min(c01, c11)) * sy;
						if (top - bottom < Steep || top <= b.min.y || bottom >= b.max.y) continue;
						// (the room under the cell's middle, if any, between the heights its ground runs through - a room is
						// more than a metre high)
						Vector3 mid = new Vector3(o.x + (x + 0.5f) * sx, 0f, o.z + (z + 0.5f) * sz);
						float floorY = 0f, roofY = 0f, under, over;
						bool room = false;
						for (float y = Mathf.Max(bottom, b.min.y) + 0.05f; y < Mathf.Min(top, b.max.y) && !room; y += 1f)
						{
							mid.y = y;
							if (Inside(mid, own, b, out under, out over)) { room = true; floorY = y - under; roofY = y + over; }
						}
						if (!room) continue;
						// (and its ground in between its corners somewhere in that room)
						bool through = false;
						for (int i = 1; i < Looks && !through; i++)
							for (int j = 1; j < Looks && !through; j++)
							{
								Vector3 p = new Vector3(o.x + (x + (float)i / Looks) * sx, 0f, o.z + (z + (float)j / Looks) * sz);
								p.y = o.y + land.SampleHeight(p);
								through = p.y > floorY + 0.05f && p.y < roofY - 0.05f && Inside(p, own, b, out under, out over);
							}
						if (!through) continue;
						seen.Add(z * cells + x);
						found.Add(new Vector2Int(x, z));
						if (roomAt != null) roomAt[z * cells + x] = new Vector2(floorY, roofY);
					}
			}
		}

		/// <summary>A way through narrower than this (m, at the piece's size) is a tunnel's (CutTunnels): the cave tool's is 3.2 m.</summary>
		const float NarrowWidth = 6f;

		/// <summary>Dev (CICaveToolProbe): told why each cell near TraceAt is cut or not by CutTunnels' tunnel rule.</summary>
		internal static Action<string> Trace;
		internal static Vector3 TraceAt;

		/// <summary>A cave piece's measures (raft_props), or null.</summary>
		static PropInfo InfoOf(Transform t)
		{
			EditorGameObject e = t.GetComponent<EditorGameObject>();
			string name = e != null ? e.GameObjectName : null;
			if (name == null) names.TryGetValue(t.GetInstanceID(), out name);
			PropInfo p = RaftProps.Get(name ?? "");
			return p != null && p.IsCave ? p : null;
		}

		/// <summary>A point (world) in a cave piece's own measures (raft_props', at the size it was measured at).</summary>
		static Vector3 InMeasures(Transform t, PropInfo p, Vector3 world)
		{
			Vector3 sc = CaveMountain.OverMeasured(t, p.Name), l = Quaternion.Inverse(t.rotation) * (world - t.position);
			return new Vector3(l.x / Mathf.Max(1e-4f, Mathf.Abs(sc.x)), l.y / Mathf.Max(1e-4f, Mathf.Abs(sc.y)), l.z / Mathf.Max(1e-4f, Mathf.Abs(sc.z)));
		}

		/// <summary>A cave tool mouth's frames on a piece (CaveTool.Mouth) and their footprints (each frame's level space,
		/// CaveTool.FrameSpaceName: metres, z along the way in, y up from its floor).</summary>
		static List<KeyValuePair<Transform, Bounds>> Frames(Transform cave)
		{
			var found = new List<KeyValuePair<Transform, Bounds>>();
			foreach (Transform c in cave)
			{
				MeshFilter mf = c.name == CaveTool.MouthName ? c.GetComponent<MeshFilter>() : null;
				if (mf == null || mf.sharedMesh == null || !c.gameObject.activeInHierarchy) continue;
				Transform space = c.Find(CaveTool.FrameSpaceName) ?? c;
				// (the frame's footprint as it was before its rock was made rough and wider than it, 11-02)
				Transform foot = space.Find(CaveTool.FootName);
				if (foot != null) { found.Add(new KeyValuePair<Transform, Bounds>(space, new Bounds(foot.localPosition, foot.localScale))); continue; }
				Vector3[] v = mf.sharedMesh.vertices;
				if (v.Length == 0) continue;
				var b = new Bounds(space.InverseTransformPoint(c.TransformPoint(v[0])), Vector3.zero);
				foreach (Vector3 x in v) b.Encapsulate(space.InverseTransformPoint(c.TransformPoint(x)));
				found.Add(new KeyValuePair<Transform, Bounds>(space, b));
			}
			return found;
		}

		/// <summary>
		/// The cells whose ground runs anywhere through a tunnel's way (a piece narrower than NarrowWidth - the cave tool's,
		/// 3.2 m wide where the ground's cells are 1.95 m): each looked over finely, and cut where any of its ground is in the
		/// way, when all four of its corners are within the caves' rock (none opens a gap beside it) or under a cave tool
		/// mouth's frame (which covers it); and every cell under a frame. The rooms' rule (a cell wholly in one, or steep
		/// through it with the room under its middle) left wedges of ground across a tunnel: rock spikes up through its floor,
		/// a slope that pushed you up, its mouth closed (the cave tool, 11-01).
		/// </summary>
		static void CutTunnels(Terrain land, List<Transform> caves, List<Vector2Int> found)
		{
			var infos = new List<KeyValuePair<Transform, PropInfo>>();
			var frames = new List<KeyValuePair<Transform, Bounds>>();
			var joins = new List<KeyValuePair<Transform, Bounds>>();
			foreach (Transform t in caves)
			{
				if (t == null) continue;
				PropInfo p = InfoOf(t);
				if (p != null) infos.Add(new KeyValuePair<Transform, PropInfo>(t, p));
				else if (Trace != null) Trace(t.name + ": no measures");
				frames.AddRange(Frames(t));
				foreach (Transform c in t)
					if (c.name == CaveTool.JoinName && c.gameObject.activeInHierarchy && c.GetComponent<MeshFilter>() != null && c.GetComponent<MeshFilter>().sharedMesh != null)
						joins.Add(new KeyValuePair<Transform, Bounds>(c, c.GetComponent<MeshFilter>().sharedMesh.bounds));
			}
			TerrainData data = land.terrainData;
			int res = data.heightmapResolution, cells = res - 1;
			float sx = data.size.x / cells, sz = data.size.z / cells, sy = data.size.y;
			Vector3 o = land.transform.position;
			var seen = new HashSet<int>(found.Select(c => c.y * cells + c.x));
			Func<int, int, float, Vector3> at = (x, z, h) => new Vector3(o.x + x * sx, o.y + h * sy, o.z + z * sz);
			// (under a frame: the ground there is hidden by it - its sill, its front, its top - and the way in runs through it)
			Func<Vector3, bool> underFrame = c =>
			{
				foreach (KeyValuePair<Transform, Bounds> f in frames)
				{
					Vector3 l = f.Key.InverseTransformPoint(c);
					Bounds fb = f.Value;
					if (l.x > fb.min.x + 0.02f && l.x < fb.max.x - 0.02f && l.z > fb.min.z + 0.02f && l.z < fb.max.z - 0.02f && l.y > fb.min.y - 3f && l.y < fb.max.y + 0.5f) return true;
				}
				return false;
			};
			// (under its doorstep, before the passage: kept - a hole there showed the sea under the doorstep's edge)
			Func<Transform, Vector3, bool> underStep = (fs, c) => { Vector3 l = fs.InverseTransformPoint(c); return l.z < 0.1f && l.y < 0f; };
			Func<Vector3, bool> covered = c =>
			{
				foreach (KeyValuePair<Transform, PropInfo> i in infos)
				{
					Vector3 l = InMeasures(i.Key, i.Value, c), half = i.Value.Size * 0.5f + Vector3.one * 0.3f;
					if (Mathf.Abs(l.x - i.Value.Centre.x) < half.x && Mathf.Abs(l.z - i.Value.Centre.z) < half.z) return true;
				}
				return underFrame(c);
			};
			foreach (KeyValuePair<Transform, Bounds> f in frames)
			{
				Bounds w = new Bounds(f.Key.position, Vector3.zero);
				Bounds fb = f.Value;
				for (int i = 0; i < 4; i++) w.Encapsulate(f.Key.TransformPoint(new Vector3((i & 1) == 0 ? fb.min.x : fb.max.x, 0f, (i & 2) == 0 ? fb.min.z : fb.max.z)));
				int x0 = Mathf.Max(0, Mathf.FloorToInt((w.min.x - o.x) / sx)), x1 = Mathf.Min(cells, Mathf.CeilToInt((w.max.x - o.x) / sx));
				int z0 = Mathf.Max(0, Mathf.FloorToInt((w.min.z - o.z) / sz)), z1 = Mathf.Min(cells, Mathf.CeilToInt((w.max.z - o.z) / sz));
				if (x0 >= x1 || z0 >= z1) continue;
				float[,] h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
				for (int z = z0; z < z1; z++)
					for (int x = x0; x < x1; x++)
						if (!seen.Contains(z * cells + x) && underFrame(at(x, z, h[z - z0, x - x0])) && underFrame(at(x + 1, z, h[z - z0, x + 1 - x0]))
							&& underFrame(at(x, z + 1, h[z + 1 - z0, x - x0])) && underFrame(at(x + 1, z + 1, h[z + 1 - z0, x + 1 - x0]))
							&& !(underStep(f.Key, at(x, z, h[z - z0, x - x0])) && underStep(f.Key, at(x + 1, z, h[z - z0, x + 1 - x0]))
								&& underStep(f.Key, at(x, z + 1, h[z + 1 - z0, x - x0])) && underStep(f.Key, at(x + 1, z + 1, h[z + 1 - z0, x + 1 - x0]))))
						{
							seen.Add(z * cells + x);
							found.Add(new Vector2Int(x, z));
						}
			}
			// (over a join's plate: the floor dips where two tunnels meet, and the rule below finds no way through there - the
			// ground stood up through the plate, 11-02; a cell whose middle is on the plate's middle part and whose corners are all
			// low in the passage is cut: the plate is the floor over it, the tunnels' rock its sides)
			foreach (KeyValuePair<Transform, Bounds> j in joins)
			{
				Bounds jb = j.Value;
				float k = jb.extents.x / 2.4f, roof = 1.6f * k;
				Bounds w = new Bounds(j.Key.position, Vector3.zero);
				for (int i = 0; i < 4; i++) w.Encapsulate(j.Key.TransformPoint(new Vector3((i & 1) == 0 ? jb.min.x : jb.max.x, 0f, (i & 2) == 0 ? jb.min.z : jb.max.z)));
				int x0 = Mathf.Max(0, Mathf.FloorToInt((w.min.x - o.x) / sx) - 1), x1 = Mathf.Min(cells, Mathf.CeilToInt((w.max.x - o.x) / sx) + 1);
				int z0 = Mathf.Max(0, Mathf.FloorToInt((w.min.z - o.z) / sz) - 1), z1 = Mathf.Min(cells, Mathf.CeilToInt((w.max.z - o.z) / sz) + 1);
				if (x0 >= x1 || z0 >= z1) continue;
				float[,] h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
				Func<Vector3, bool> low = c => { Vector3 l = j.Key.InverseTransformPoint(c); return l.y > -3f && l.y < roof; };
				for (int z = z0; z < z1; z++)
					for (int x = x0; x < x1; x++)
					{
						if (seen.Contains(z * cells + x)) continue;
						Vector3 m = j.Key.InverseTransformPoint(new Vector3(o.x + (x + 0.5f) * sx, j.Key.position.y, o.z + (z + 0.5f) * sz));
						if (Mathf.Abs(m.x) > 1.2f * k || m.z < jb.min.z - 0.3f || m.z > jb.max.z + 0.3f) continue;
						if (!low(at(x, z, h[z - z0, x - x0])) || !low(at(x + 1, z, h[z - z0, x + 1 - x0])) || !low(at(x, z + 1, h[z + 1 - z0, x - x0])) || !low(at(x + 1, z + 1, h[z + 1 - z0, x + 1 - x0]))) continue;
						seen.Add(z * cells + x);
						found.Add(new Vector2Int(x, z));
					}
			}
			foreach (KeyValuePair<Transform, PropInfo> i in infos)
			{
				Transform t = i.Key;
				PropInfo p = i.Value;
				Vector3 sc = CaveMountain.OverMeasured(t, p.Name);
				if (Trace != null) Trace(t.name + ": width " + p.Width + " axis " + p.Axis + " scale " + sc.ToString("F2"));
				if (p.Width <= 0f || p.Width * Mathf.Abs(p.Axis == 0 ? sc.z : sc.x) >= NarrowWidth) continue;
				HashSet<Collider> own;
				Bounds b;
				int x0, x1, z0, z1;
				if (!Reach(land, t, out own, out b, out x0, out x1, out z0, out z1)) continue;
				if (Trace != null) Trace(t.name + ": cells " + x0 + ".." + x1 + ", " + z0 + ".." + z1 + " trace at " + TraceAt.ToString("F1"));
				Vector3 mid = p.Inside != Vector3.zero ? p.Inside : p.Centre;
				float across = p.Width * 0.5f + 1.2f, along = (p.Axis == 0 ? p.Size.x : p.Size.z) * 0.5f + 0.5f;
				float[,] h = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
				for (int z = z0; z < z1; z++)
					for (int x = x0; x < x1; x++)
					{
						bool tr = Trace != null && Mathf.Abs(o.x + (x + 0.5f) * sx - TraceAt.x) < 1.2f && Mathf.Abs(o.z + (z + 0.5f) * sz - TraceAt.z) < 1.2f;
						if (seen.Contains(z * cells + x)) { if (tr) Trace("cell " + x + "," + z + " " + t.name + ": seen"); continue; }
						float c00 = h[z - z0, x - x0], c10 = h[z - z0, x + 1 - x0], c01 = h[z + 1 - z0, x - x0], c11 = h[z + 1 - z0, x + 1 - x0];
						float top = o.y + Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c01, c11)) * sy, bottom = o.y + Mathf.Min(Mathf.Min(c00, c10), Mathf.Min(c01, c11)) * sy;
						if (top <= b.min.y || bottom >= b.max.y) { if (tr) Trace("cell " + x + "," + z + " " + t.name + ": out of bounds y"); continue; }
						if (!covered(at(x, z, c00)) || !covered(at(x + 1, z, c10)) || !covered(at(x, z + 1, c01)) || !covered(at(x + 1, z + 1, c11)))
						{
							if (tr) Trace("cell " + x + "," + z + " " + t.name + ": covered " + covered(at(x, z, c00)) + covered(at(x + 1, z, c10)) + covered(at(x, z + 1, c01)) + covered(at(x + 1, z + 1, c11)) + " local " + InMeasures(t, p, at(x, z, c00)).ToString("F2") + " " + InMeasures(t, p, at(x, z + 1, c01)).ToString("F2") + " centre " + p.Centre.ToString("F2") + " size " + p.Size.ToString("F2"));
							continue;
						}
						// (finer the steeper: a way 3.5 m high is crossed in under half a metre by a cell 9 m high)
						int n = Mathf.Clamp(Mathf.CeilToInt((top - bottom) / 1.5f) + 4, 6, 20);
						bool through = false;
						float under, over;
						int inRange = 0;
						for (int a = 0; a <= n && !through; a++)
							for (int c = 0; c <= n && !through; c++)
							{
								Vector3 q = new Vector3(o.x + (x + (float)a / n) * sx, 0f, o.z + (z + (float)c / n) * sz);
								q.y = o.y + land.SampleHeight(q);
								Vector3 l = InMeasures(t, p, q);
								float side = p.Axis == 0 ? l.z - mid.z : l.x - mid.x, run = p.Axis == 0 ? l.x - p.Centre.x : l.z - p.Centre.z;
								if (Mathf.Abs(side) > across || Mathf.Abs(run) > along || l.y < p.Floor - 2.5f || l.y > p.Floor + p.Headroom + 2.5f) continue;
								inRange++;
								through = Inside(q, own, b, out under, out over);
							}
						if (tr) Trace("cell " + x + "," + z + " " + t.name + ": " + inRange + " of " + ((n + 1) * (n + 1)) + " samples in range, through " + through);
						if (!through) continue;
						seen.Add(z * cells + x);
						found.Add(new Vector2Int(x, z));
					}
			}
		}

		/// <summary>A point of the ground in the piece's room: its nearest faces straight up and straight down both seen from their fronts (the shells face inwards). under, over: how far down the floor is and how far up the roof.</summary>
		internal static bool Inside(Vector3 p, HashSet<Collider> own, Bounds b, out float under, out float over)
		{
			Vector3 from = p + Vector3.up * 0.02f;
			under = over = 0f;
			if (!FrontFirst(from, Vector3.down, from.y - b.min.y + 1f, own, out under) || !FrontFirst(from, Vector3.up, b.max.y - from.y + 1f, own, out over)) return false;
			under -= 0.02f;
			over += 0.02f;
			return true;
		}

		/// <summary>
		/// Whether the piece's rock is round p on most sides (WalledSides of 8, out to WalledReach m): in a room it is, but
		/// under a wall leaning out over the ground outside, with the piece's foot buried under it, only on the rock's side.
		/// There the ground was let down too and left a pit by the rock, open to the sky (11-07 review: Shelter Atoll's den).
		/// Only for a piece with cave.foot=dry.
		/// </summary>
		static bool Walled(Vector3 p, HashSet<Collider> own)
		{
			Vector3 from = p + Vector3.up * 0.5f;
			int hits = 0;
			for (int i = 0; i < 8; i++)
			{
				float a = i * Mathf.PI / 4f;
				if (Nearest(from, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), WalledReach, own, true) < float.PositiveInfinity) hits++;
			}
			return hits >= WalledSides;
		}

		const int WalledSides = 5;
		const float WalledReach = 25f;

		static bool FrontFirst(Vector3 from, Vector3 dir, float len, HashSet<Collider> own, out float front)
		{
			front = Nearest(from, dir, len, own, false);
			return front < float.PositiveInfinity && front <= Nearest(from, dir, len, own, true) + 0.01f;
		}

		static float Nearest(Vector3 from, Vector3 dir, float len, HashSet<Collider> own, bool backs)
		{
			Physics.queriesHitBackfaces = backs;
			float best = float.PositiveInfinity;
			foreach (RaycastHit hit in Physics.RaycastAll(from, dir, len, ~0, QueryTriggerInteraction.Ignore))
				if (hit.distance < best && own.Contains(hit.collider)) best = hit.distance;
			return best;
		}
	}
}
