using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>The editor's cave tool (CaveTool.cs, ROADMAP PF-D2): its layout, laying one in the editor, and the cave in a world.</summary>
	public static partial class DevTests
	{
		const string CaveToolIsland = "citest-cavetool", CaveToolKey = "citest.cave";

		[ConsoleCommand(name: "CICaveToolUnit", docs: "Dev, anywhere: the cave tool's layout - tunnel pieces end to end from the mouth (their passages and floors meeting), the slope kept to 12 degrees and the pieces to 12, the den's mouth on the tunnel's end, a short drag only the den; the hill over it, none before the mouth or past an open end")]
		public static void CaveToolUnitCommand(string[] args)
		{
			bool ok = true;
			Vector3 a = new Vector3(500f, 20f, 500f);
			CaveTool.Layout l = CaveTool.Plan(a, a + new Vector3(40f, 7f, 0f), 0f, true);
			Check(ref ok, l.Tunnels == 4 && l.Pieces.Count == 5 && l.Pieces.Count(p => p.Name == CaveTool.Room) == 1, "a 40 m drag: 4 tunnels and the den (" + l.Tunnels + ", " + l.Pieces.Count + " pieces)");
			Check(ref ok, CaveJoins(l) < 0.01f, "level: the passages meet end to end (worst " + CaveJoins(l).ToString("0.000") + " m), the first opens at the mouth");
			Vector3 denMouth = l.RoomPivot + l.RoomRotation * new Vector3(-1.072f, -4.05f - CaveTool.RoomLift, 15.99f);
			Check(ref ok, (denMouth - l.End).magnitude < 0.01f && (l.End - (a + Vector3.right * 4f * CaveTool.Step)).magnitude < 0.01f, "the den's mouth on the tunnel's far end (" + (denMouth - l.End).magnitude.ToString("0.000") + " m off)");
			Check(ref ok, Vector3.Angle(l.RoomRotation * Vector3.forward, Vector3.left) < 0.5f, "the den faces back up the tunnel");

			CaveTool.Layout down = CaveTool.Plan(a, a + new Vector3(0f, 0f, -50f), -8f, false);
			Check(ref ok, CaveJoins(down) < 0.01f && down.Tunnels == 5 && !down.Room, "8 degrees down, open: 5 tunnels end to end (worst " + CaveJoins(down).ToString("0.000") + " m)");
			Check(ref ok, Mathf.Abs(down.End.y - (a.y - Mathf.Sin(8f * Mathf.Deg2Rad) * 5f * CaveTool.Step)) < 0.01f && Mathf.Abs(down.FloorAt(down.Length) - down.End.y) < 0.01f, "its floor goes down " + (a.y - down.End.y).ToString("0.0") + " m");
			Check(ref ok, Mathf.Abs(CaveTool.Plan(a, a + Vector3.forward * 40f, 30f, true).Slope - CaveTool.MaxSlope) < 0.01f, "a slope of 30 degrees is kept to " + CaveTool.MaxSlope);
			Check(ref ok, CaveTool.Plan(a, a + Vector3.forward * 900f, 0f, true).Tunnels == CaveTool.MaxTunnels, "a long drag: " + CaveTool.MaxTunnels + " tunnels at most");
			CaveTool.Layout den = CaveTool.Plan(a, a + Vector3.forward * 3f, 0f, true);
			Check(ref ok, den.Tunnels == 0 && den.Pieces.Count == 1 && (den.RoomPivot + den.RoomRotation * new Vector3(-1.072f, -4.05f - CaveTool.RoomLift, 15.99f) - a).magnitude < 0.01f, "a short drag with a room: only the den, its mouth at the press");
			Check(ref ok, CaveTool.Plan(a, a + Vector3.forward * 3f, 0f, false).Tunnels == 1, "a short drag without: one tunnel");

			// (PF-D2b: smaller and bigger - the pieces scaled, still end to end, the den on the end, the hill over the scaled rock)
			foreach (float k in new[] { CaveTool.MinSize, CaveTool.MaxSize })
			{
				CaveTool.Layout sl = CaveTool.Plan(a, a + new Vector3(40f * k, 0f, 0f), -6f, true, k);
				Vector3 dm = sl.RoomPivot + sl.RoomRotation * (new Vector3(-1.072f, -4.05f - CaveTool.RoomLift, 15.99f) * k);
				float hillTop = CaveTool.HillAt(sl, a + new Vector3(sl.Length / 2f, 0f, 0f));
				Check(ref ok, sl.Tunnels == 4 && sl.Pieces.All(p => Mathf.Abs(p.Scale - k) < 1e-4f) && CaveJoins(sl) < 0.01f && (dm - sl.End).magnitude < 0.01f
					&& Mathf.Abs(sl.Length - 4f * CaveTool.Step * k * Mathf.Cos(6f * Mathf.Deg2Rad)) < 0.01f && hillTop >= sl.FloorAt(sl.Length / 2f) + CaveTool.ShellTop * k,
					"size " + k + "x: 4 pieces at " + k + "x end to end (worst " + CaveJoins(sl).ToString("0.000") + " m), the den on the end (" + (dm - sl.End).magnitude.ToString("0.000") + " m off), " + sl.Length.ToString("0.0") + " m long, the hill over its rock");
			}
			Check(ref ok, Mathf.Abs(CaveTool.Plan(a, a + Vector3.forward * 40f, 0f, true, 0.2f).Size - CaveTool.MinSize) < 1e-4f && Mathf.Abs(CaveTool.Plan(a, a + Vector3.forward * 40f, 0f, true, 9f).Size - CaveTool.MaxSize) < 1e-4f, "a size is kept to " + CaveTool.MinSize + "-" + CaveTool.MaxSize);

			// (the hill: over the tunnel its roof and more, none before the mouth or past an open end on its line)
			float over = CaveTool.HillAt(down, a + new Vector3(0f, 0f, -20f));
			Check(ref ok, over >= down.FloorAt(20f) + CaveTool.ShellTop, "the hill over the tunnel is over its rock (" + over.ToString("0.0") + ")");
			Check(ref ok, float.IsNaN(CaveTool.HillAt(down, a + new Vector3(0f, 0f, 5f))) && float.IsNaN(CaveTool.HillAt(down, down.End + new Vector3(0f, 0f, -5f))), "no hill before the mouth or past the open end");
			float beside = CaveTool.HillAt(down, a + new Vector3(15f, 0f, -20f));
			Check(ref ok, beside < over - 5f, "the hill falls away beside it (" + beside.ToString("0.0") + ")");
			float w;
			Check(ref ok, Mathf.Abs(CaveTool.ApronAt(down, a + new Vector3(0f, 0f, 1f), out w) - (a.y - 0.05f)) < 0.01f && w > 0.99f, "the ground before the mouth at its floor");
			if (ok) Log("PASS: cave tool unit"); else Fail("cave tool unit");
		}

		/// <summary>How far apart the passages' floors are where one tunnel ends and the next starts (and the first from the mouth).</summary>
		static float CaveJoins(CaveTool.Layout l)
		{
			float worst = 0f;
			Vector3 prevEnd = l.Mouth;
			foreach (CaveTool.Piece p in l.Pieces.Where(x => x.Name == CaveTool.Tunnel))
			{
				Vector3 start = p.Position + p.Rotation * (new Vector3(CaveTool.PassX, CaveTool.Floor - CaveTool.EndDip, CaveTool.FaceMin) * l.Size);
				worst = Mathf.Max(worst, (start - prevEnd).magnitude);
				prevEnd = p.Position + p.Rotation * (new Vector3(CaveTool.PassX, CaveTool.Floor - CaveTool.EndDip, CaveTool.FaceMin + CaveTool.Step) * l.Size);
			}
			return Mathf.Max(worst, (prevEnd - l.End).magnitude);
		}

		[ConsoleCommand(name: "CICaveToolEditor", docs: "Dev, editor: the cave tool on a generated island - a tunnel 6 degrees down into the land with the den at its end, laid as the Tunnel button lays it (CICaveToolEditor [size], 0.7-1.5, default 1): walkable end to end (a floor every metre, steps under 0.6 m, 2 m of room over it), the hill over it, its sides buried, the way in level; one Ctrl+Z takes all back, redo brings it; saved and loaded with the island. Leaves the island citest-cavetool for CICaveToolWorld")]
		public static void CaveToolEditorCommand(string[] args) { StartTest(CaveToolEditorRoutine(args.Length > 0 ? float.Parse(args[0], CultureInfo.InvariantCulture) : 1f)); }

		const string RockName = "SmallBoulder2";

		static IEnumerator CaveToolEditorRoutine(float size)
		{
			if (!DynamicIslands.InEditor()) { Fail("cave tool: in the editor"); yield break; }
			bool ok = true;
			DynamicIslands.NewIsland();
			yield return null;
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4242, Radius = 130f, Height = 30f, Style = TerrainPainter.Tropical });
			yield return null;
			Terrain t = terraineditor.terrain;
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorWaterLevel;
			System.Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };
			// (the mouth on land west of the middle, the tunnel east into it)
			Vector3 mouth = ground(c + new Vector3(-45f, 0f, 0f));
			for (int i = 0; i < 6 && mouth.y < sea + 3f; i++) mouth = ground(mouth + new Vector3(8f, 0f, 0f));
			Check(ref ok, mouth.y > sea + 3f, "the mouth is on land (" + (mouth.y - sea).ToString("0.0") + " m above the sea)");
			int hres = data.heightmapResolution;
			float[,] before = data.GetHeights(0, 0, hres, hres);
			GameObject root = GameObject.Find("PlacedObjects");
			int objects = root.GetComponentsInChildren<EditorGameObject>(false).Length;

			// (a rock where the tunnel will run, and one well beside it)
			GameObject rockIn = null, rockOut = null;
			yield return PlaceableCatalog.EnsureLoaded(new[] { RockName });
			{
				CaveTool.Layout pl = CaveTool.Plan(mouth, mouth + Vector3.right * 42f * size, -6f, true, size);
				Vector3 inP = mouth + pl.Flat * 15f; inP.y = pl.FloorAt(15f) + 1f;
				Vector3 outP = ground(mouth + pl.Flat * 15f + Vector3.Cross(Vector3.up, pl.Flat) * 40f);
				foreach (Vector3 at in new[] { inP, outP })
				{
					GameObject g = PlaceableCatalog.Spawn(RockName, root.transform);
					if (g == null) break;
					g.transform.position = at;
					EditorGameObject.Attach(g, RockName);
					if (rockIn == null) rockIn = g; else rockOut = g;
				}
				objects = root.GetComponentsInChildren<EditorGameObject>(false).Length;
			}
			List<GameObject> laid = null;
			var shownBefore = root.GetComponentsInChildren<EditorGameObject>(false).Select(x => x.gameObject).ToList();
			yield return CaveTool.Lay(mouth, mouth + Vector3.right * 42f * size, -6f, true, true, size, m => laid = m);
			CaveTool.Layout l = CaveTool.LastLayout;
			if (laid == null || laid.Count == 0 || l == null) { Fail("cave tool: nothing was laid"); yield break; }
			int hid = shownBefore.Count(x => x != null && !x.activeSelf);
			Check(ref ok, l.Tunnels == 4 && laid.Count == 5 && hid >= 1 && root.GetComponentsInChildren<EditorGameObject>(false).Length == objects + 5 - hid, "4 tunnels and the den laid, what stood in their rock hidden (" + laid.Count + " pieces, " + hid + " hidden)");
			Check(ref ok, laid.Where(g => g.GetComponent<EditorGameObject>().GameObjectName == CaveTool.Tunnel).All(g => ObjectProps.Get(g.GetComponent<EditorGameObject>().Props, ObjectProps.Skin) == ObjectProps.SkinRock), "the tunnels take the den's brown rock");
			GameObject den = laid.FirstOrDefault(g => g.GetComponent<EditorGameObject>().GameObjectName == CaveTool.Room);
			Check(ref ok, den != null && den.transform.Find(CaveTool.PlugName) != null, "the den's wide mouth walled round the tunnel's end");
			{
				// (PF-D2b: every piece laid at the size, the den's wall with it)
				GameObject tp = PlaceableCatalog.Get(CaveTool.Tunnel), rp = PlaceableCatalog.Get(CaveTool.Room);
				bool scaled = tp != null && rp != null && laid.All(g => Mathf.Abs(g.transform.lossyScale.x - (g.GetComponent<EditorGameObject>().GameObjectName == CaveTool.Tunnel ? tp : rp).transform.localScale.x * size) < 0.001f);
				Transform plug = den != null ? den.transform.Find(CaveTool.PlugName) : null;
				Check(ref ok, Mathf.Abs(l.Size - size) < 0.001f && scaled && plug != null && Mathf.Abs(plug.lossyScale.x - size) < 0.001f && ObjectProps.Get(den.GetComponent<EditorGameObject>().Props, ObjectProps.CavePlug) == size.ToString("0.###", CultureInfo.InvariantCulture),
					"size " + size + "x: the pieces and the den's wall laid at it (wall " + (plug != null ? plug.lossyScale.x.ToString("0.00") : "none") + ")");
			}
			Check(ref ok, rockIn != null && !rockIn.activeSelf && rockOut != null && rockOut.activeSelf, "a rock in the tunnel's way cleared, one beside it kept");
			var selected = DynamicIslands.EditorGizmoHandler != null ? DynamicIslands.EditorGizmoHandler.SelectedRoots : null;
			Check(ref ok, selected == null || selected.Count == laid.Count, "the cave's pieces are selected after");
			Physics.SyncTransforms();

			// Walkable: from the mouth to 8 m into the den, a floor of the cave's own rock every metre, no step over 0.6 m, 2 m over it
			var pieces = new HashSet<Collider>(laid.SelectMany(g => g.GetComponentsInChildren<Collider>()));
			// (a ball the player's width rolled down from over the floor, every half metre. Where two pieces meet, their floors
			// leave a narrow groove - Raft's labyrinth joins them the same way: one sample in it, missed or lower than both its
			// neighbours, is the groove and counts as no step; two missed in a row are a gap)
			float worstStep = 0f, leastRoom = 99f;
			string gap = null, worstAt = "";
			var ss = new List<float>();
			var ys = new List<float?>();
			var profile = new List<string>();
			for (float s = 0.5f; s <= l.Length + 8f * size; s += 0.5f)
			{
				Vector3 p = mouth + l.Flat * s;
				float expect = s <= l.Length ? l.FloorAt(s) : l.End.y;
				RaycastHit? floor = null;
				foreach (RaycastHit h in Physics.SphereCastAll(new Vector3(p.x, expect + 1.8f, p.z), 0.4f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
					if (pieces.Contains(h.collider) && h.normal.y > 0.5f && h.distance > 0f && (floor == null || h.distance < floor.Value.distance)) floor = h;
				ss.Add(s);
				ys.Add(floor.HasValue ? floor.Value.point.y : (float?)null);
				profile.Add(s.ToString("0.0") + ":" + (floor.HasValue ? (floor.Value.point.y - expect).ToString("0.00") + " " + floor.Value.collider.name : "none"));
				if (!floor.HasValue) continue;
				RaycastHit roof;
				float room = Physics.Raycast(floor.Value.point + Vector3.up * 0.2f, Vector3.up, out roof, 30f, ~0, QueryTriggerInteraction.Ignore) ? roof.distance + 0.2f : 99f;
				leastRoom = Mathf.Min(leastRoom, room);
			}
			int grooves = 0;
			float? last = mouth.y;
			for (int k = 0; k < ys.Count; k++)
			{
				float? y = ys[k], next = null;
				// (the joins' grooves widen with the size: up to a metre and a half across at 1.5x, as Raft's own between its pieces)
				for (int j = k + 1; j < ys.Count && j <= k + Mathf.Max(1, Mathf.FloorToInt(size * 1.5f)); j++) if (ys[j].HasValue) { next = ys[j]; break; }
				bool groove = last.HasValue && next.HasValue && (!y.HasValue || (y.Value < last.Value - 0.25f && y.Value < next.Value - 0.25f));
				if (groove) { grooves++; continue; }
				if (!y.HasValue) { gap = gap ?? "no floor " + ss[k].ToString("0.0") + " m in"; last = null; continue; }
				if (last.HasValue)
				{
					float step = Mathf.Abs(y.Value - last.Value);
					if (step > worstStep) { worstStep = step; worstAt = " at " + ss[k].ToString("0.0") + " m"; }
				}
				last = y;
			}
			bool walk = gap == null && worstStep < 0.6f && leastRoom >= 2f;
			Check(ref ok, walk, "walkable end to end: " + (gap ?? "a floor every half metre (" + grooves + " joins' grooves)") + ", worst step " + worstStep.ToString("0.00") + " m" + worstAt + ", least room over it " + leastRoom.ToString("0.0") + " m");
			if (!walk) Log("  floor (m in: over the line's floor, what): " + string.Join("; ", profile.ToArray()));

			// The ground: a hill over the tunnel, its sides buried (nothing hangs in the air), the way in at the mouth's floor
			float mid = l.Length / 2f;
			Vector3 midP = mouth + l.Flat * mid, side = Vector3.Cross(Vector3.up, l.Flat) * (l.HalfWidth + 0.6f);
			float hillY = ground(midP + side * 2.5f).y;
			Check(ref ok, ground(midP + side).y > l.FloorAt(mid) && ground(midP - side).y > l.FloorAt(mid), "the tunnel's sides are buried (" + (ground(midP + side).y - l.FloorAt(mid)).ToString("0.0") + " m over its floor)");
			float dz = ground(l.RoomPivot + Vector3.up * 0f).y - l.RoomPivot.y;
			Check(ref ok, ground(midP + side * 1.2f).y > l.FloorAt(mid) + 3f, "the hill rises over it (" + (ground(midP + side * 1.2f).y - l.FloorAt(mid)).ToString("0.0") + " m beside its roof, " + hillY.ToString("0.0") + " further out)");
			float wayIn = ground(mouth - l.Flat * 3f).y;
			Check(ref ok, Mathf.Abs(wayIn - mouth.y) < 0.6f, "the way in, 3 m before the mouth, at its floor (" + (wayIn - mouth.y).ToString("0.00") + " m)");
			int dug;
			int cut = CaveGround.EditorNow(t, out dug);
			Log("  the ground in the cave's rooms: " + cut + " cells cut, " + dug + " points let down; den " + dz.ToString("0.0") + " m");
			{
				// (the cut fits the scaled rooms: looking down in the passage and over the den's floor, the ground is never met before
				// the cave's own floor - nothing of it stands where the player walks)
				Physics.SyncTransforms();
				int looked = 0, inWay = 0;
				var seenAt = new List<string>();
				System.Action<Vector3, float> lookDown = (p, from) =>
				{
					RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, 2.5f * size, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
					RaycastHit first = hits.FirstOrDefault(h => h.collider is TerrainCollider || pieces.Contains(h.collider));
					if (first.collider == null) return;
					looked++;
					if (first.collider is TerrainCollider) { inWay++; seenAt.Add(p.ToString("F0")); }
				};
				for (float s = 1f; s <= l.Length; s += 1f) lookDown(mouth + l.Flat * s, l.FloorAt(s) + 1.8f * size);
				float denFloor = l.RoomPivot.y + l.RoomMouth.y;
				Vector3 rmin = l.RoomMin, rmax = l.RoomMax;
				for (float x = rmin.x + 3f; x < rmax.x - 3f; x += 3f * size)
					for (float z = rmin.z + 3f; z < rmax.z - 3f; z += 3f * size)
						lookDown(l.RoomPivot + l.RoomRotation * new Vector3(x, 0f, z), denFloor + 1.8f * size);
				Check(ref ok, looked > 20 && inWay == 0, "the ground cut out of the " + size + "x tunnel and den: " + inWay + " of " + looked + " looks down meet the ground before the cave's floor" + (seenAt.Count > 0 ? ": " + string.Join(", ", seenAt.Take(5).ToArray()) : ""));
			}

			// One undo takes the pieces and the ground back; redo brings them
			float[,] after = data.GetHeights(0, 0, hres, hres);
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			float[,] undone = data.GetHeights(0, 0, hres, hres);
			Check(ref ok, laid.All(g => !g.activeSelf) && SameHeights(before, undone, 0.0005f), "undo: the pieces gone and the ground as it was (" + Differ(before, undone) + " points differ)");
			CommandUndoRedo.UndoRedoManager.Redo();
			yield return null;
			Check(ref ok, laid.All(g => g.activeSelf) && SameHeights(after, data.GetHeights(0, 0, hres, hres), 0.0005f), "redo: back again");

			// Saved and loaded with the island; the cave's place kept for CICaveToolWorld (m from the middle, above the sea)
			Vector3 m0 = mouth - c, e0 = l.End - c;
			DynamicIslands.currentIslandProps[CaveToolKey] = string.Join(",", new[] { m0.x, mouth.y - sea, m0.z, e0.x, l.End.y - sea, e0.z, l.Length, size }.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)).ToArray());
			Vector3 firstAt = laid[0].transform.position; float firstScale = laid[0].transform.lossyScale.x;
			Check(ref ok, DynamicIslands.SaveIsland(CaveToolIsland), "saved as " + CaveToolIsland);
			DynamicIslands.NewIsland();
			yield return null;
			DynamicIslands.LoadIsland(CaveToolIsland);
			yield return null;
			yield return null;
			var back = GameObject.Find("PlacedObjects").GetComponentsInChildren<EditorGameObject>(false).Where(e => e.GameObjectName == CaveTool.Tunnel || e.GameObjectName == CaveTool.Room).ToList();
			Check(ref ok, back.Count == 5 && back.Any(e => (e.transform.position - firstAt).magnitude < 0.05f && Mathf.Abs(e.transform.lossyScale.x - firstScale) < 0.001f) && back.Where(e => e.GameObjectName == CaveTool.Tunnel).All(e => ObjectProps.Get(e.Props, ObjectProps.Skin) == ObjectProps.SkinRock) && back.Any(e => e.transform.Find(CaveTool.PlugName) != null), "loaded again: the 5 pieces where they were at their size, brown, the den walled (" + back.Count + ")");

			// Pictures: the mouth from outside, and down the tunnel from inside it
			Vector3 eye = mouth - l.Flat * 14f + Vector3.up * 4f;
			string tag = Mathf.Abs(size - 1f) < 0.01f ? "" : "_" + size.ToString("0.0", CultureInfo.InvariantCulture);
			yield return CameraShot(eye, mouth + Vector3.up * 2f, "cavetool_mouth" + tag);
			yield return CameraShot(mouth + l.Flat * 2f + Vector3.up * 1.6f, mouth + l.Flat * 12f + Vector3.up * 0.6f, "cavetool_inside" + tag);
			if (ok) Log("PASS: cave tool editor"); else Fail("cave tool editor");
		}

		static bool SameHeights(float[,] a, float[,] b, float tol) { return Differ(a, b, tol) == 0; }

		static int Differ(float[,] a, float[,] b, float tol = 0.0005f)
		{
			int n = 0;
			for (int z = 0; z < a.GetLength(0); z++)
				for (int x = 0; x < a.GetLength(1); x++)
					if (Mathf.Abs(a[z, x] - b[z, x]) > tol) n++;
			return n;
		}

		[ConsoleCommand(name: "CICaveToolWorld", docs: "Dev, in a world (host): the island CICaveToolEditor left (its cave tool cave) spawned ahead of the raft - the rock mountain over it leaves its mouth clear, from inside the tunnel and the den no look meets the sky or the ground from below; pictures of the mouth and inside. The island and its file are taken away after")]
		public static void CaveToolWorldCommand(string[] args) { args0Keep = args.Contains("keep"); StartTest(CaveToolWorldRoutine()); }

		static IEnumerator CaveToolWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || DynamicIslands.InEditor()) { Fail("cave tool world: in a world"); yield break; }
			string path = IslandSpawner.PathFor(CaveToolIsland);
			IslandFile f = File.Exists(path) ? IslandFile.Load(path) : null;
			string spec;
			if (f == null || !f.Props.TryGetValue(CaveToolKey, out spec)) { Fail("cave tool world: run CICaveToolEditor first (no " + CaveToolIsland + " with its cave)"); yield break; }
			float[] v = spec.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
			float size = v.Length > 7 ? v[7] : 1f;
			string tag = Mathf.Abs(size - 1f) < 0.01f ? "" : "_" + size.ToString("0.0", CultureInfo.InvariantCulture);
			bool ok = true;
			Vector3 pos = raftPos.Value + Vector3.forward * 350f; pos.y = 0f;
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(CaveToolIsland, pos, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("cave tool world: the island did not spawn"); yield break; }
			CaveMountain mountain = null;
			for (float w = 0f; w < 20f; w += 0.5f)
			{
				mountain = e.Root.GetComponentInChildren<CaveMountain>();
				if (mountain != null && !mountain.Pending) break;
				yield return new WaitForSeconds(0.5f);
			}
			yield return new WaitForSeconds(1f);
			Quaternion turn = Quaternion.Euler(0f, e.Root.transform.eulerAngles.y, 0f);
			System.Func<float, float, float, Vector3> at = (x, y, z) => { Vector3 p = e.Position + turn * new Vector3(x, 0f, z); p.y = e.Position.y + y; return p; };
			Vector3 mouth = at(v[0], v[1], v[2]), end = at(v[3], v[4], v[5]);
			Vector3 flat = end - mouth; flat.y = 0f; flat.Normalize();
			// (the island may stand a little off where its file's middle says: take the mouth from the first tunnel piece, as Plan laid it)
			{
				Transform first = e.Root.GetComponentsInChildren<Transform>(true).Where(x => x.name == CaveTool.Tunnel).OrderBy(x => Vector3.Dot(x.position - mouth, flat)).FirstOrDefault();
				if (first != null)
				{
					Vector3 shift = first.position + first.rotation * (new Vector3(CaveTool.PassX, CaveTool.Floor - CaveTool.EndDip, CaveTool.FaceMin) * size) - mouth;
					if (shift.magnitude > 0.05f) Log("  (the mouth taken from the first piece: " + shift.magnitude.ToString("0.00") + " m from the file's)");
					mouth += shift; end += shift;
				}
			}
			List<string> blocked = mountain != null ? mountain.BlockedWays() : new List<string>();
			Check(ref ok, mountain != null && blocked.Count == 0, "the rock over it leaves the way in clear" + (blocked.Count > 0 ? ": " + string.Join("; ", blocked.Take(4).ToArray()) : mountain == null ? " (no mountain built)" : ""));

			// The tunnels wear the island's brown rock, not their ice
			{
				int pieces = 0, skinned = 0;
				string seenTex = "";
				foreach (Transform tr in e.Root.GetComponentsInChildren<Transform>(true).Where(x => x.name == CaveTool.Tunnel))
				{
					pieces++;
					bool rock = false;
					foreach (Renderer r in tr.GetComponentsInChildren<Renderer>(true))
					{
						if (r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_SideDiffuse")) continue;
						var block = new MaterialPropertyBlock();
						r.GetPropertyBlock(block);
						Texture side = block.GetTexture("_SideDiffuse");
						if (seenTex == "") seenTex = r.sharedMaterial.name + ": " + string.Join(", ", r.sharedMaterial.GetTexturePropertyNames().Select(n => n + "=" + (block.GetTexture(n) != null ? block.GetTexture(n).name + "*" : r.sharedMaterial.GetTexture(n) != null ? r.sharedMaterial.GetTexture(n).name : "-")).ToArray());
						if (side != null && side.name == "Rock_Stylized_basecolor") rock = true;
					}
					if (rock) skinned++;
				}
				Check(ref ok, pieces > 0 && skinned == pieces, "the tunnels wear brown rock (" + skinned + " of " + pieces + "; " + seenTex + ")");
			}

			// From inside: eyes every 4 m down the tunnel and in the den, 81 looks each - none to the sky or the ground's underside
			System.Func<Collider, bool> counts = col => col != null && !col.isTrigger && col.GetComponentInParent<AI_NetworkBehaviour>() == null && col.GetComponentInParent<Network_Player>() == null;
			System.Func<Collider, bool> caveRock = col =>
			{
				for (Transform tr = col.transform; tr != null && tr != e.Root.transform; tr = tr.parent)
					if (tr.name == CaveTool.Tunnel || tr.name == CaveTool.Room || tr.name == CaveMountain.SkinName || tr.name == CaveTool.PlugName) return true;
				return false;
			};
			var looks = new List<Vector3>();
			foreach (float pitch in new[] { -20f, 0f, 15f, 35f, 60f })
				for (int i = 0; i < 16; i++) looks.Add(Quaternion.Euler(-pitch, i * 22.5f, 0f) * Vector3.forward);
			looks.Add(Vector3.up);
			int eyes = 0, sky = 0, under = 0, noFloor = 0;
			var seen = new List<string>();
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.SyncTransforms();
			try
			{
				for (float s = 1.5f; s <= v[6] + 14f * size; s += 4f * Mathf.Min(1f, size))
				{
					float y = s <= v[6] ? Mathf.Lerp(mouth.y, end.y, s / Mathf.Max(0.1f, v[6])) : end.y;
					Vector3 p = mouth + flat * s; p.y = y;
					Physics.queriesHitBackfaces = false;
					RaycastHit fh;
					if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out fh, 3f, ~0, QueryTriggerInteraction.Ignore) || fh.collider is TerrainCollider) { noFloor++; continue; }
					Vector3 eye = fh.point + Vector3.up * 1.6f;
					eyes++;
					foreach (Vector3 d in looks)
					{
						Physics.queriesHitBackfaces = true;
						RaycastHit[] all = Physics.RaycastAll(eye, d, 150f, ~0, QueryTriggerInteraction.Ignore).Where(h => counts(h.collider)).OrderBy(h => h.distance).ToArray();
						Physics.queriesHitBackfaces = false;
						// (the mouth's own opening looks out by design: a look that leaves through it before meeting anything)
						bool outOfMouth = false;
						float back = Vector3.Dot(d, -flat);
						if (back > 0.05f)
						{
							float t = Vector3.Dot(eye - mouth, flat) / back;
							Vector3 cross = eye + d * t;
							Vector3 rel = cross - mouth; rel.y = 0f;
							outOfMouth = (all.Length == 0 || all[0].distance > t - 0.3f) && Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, flat))) < CaveTool.ShellHalfWidth * size && cross.y < mouth.y + (CaveTool.ShellTop - CaveTool.Floor + CaveTool.EndDip) * size;
						}
						// (the first thing met is the cave's rock - a piece, the den's wall, the mountain - or the ground from above;
						// anything else - a tree, a rock, the sky - is seen through the hill: the den's wide mouth showed it)
						if (outOfMouth) continue;
						if (all.Length == 0) { sky++; seen.Add("sky " + s.ToString("0") + " m in, " + d.ToString("F1")); }
						else if (all[0].collider is TerrainCollider) { if (all[0].normal.y < 0f) { under++; seen.Add("the ground from below " + s.ToString("0") + " m in, " + d.ToString("F1")); } }
						else if (!caveRock(all[0].collider)) { sky++; seen.Add("out to " + all[0].collider.name + " " + all[0].distance.ToString("0") + " m, " + s.ToString("0") + " m in, " + d.ToString("F1")); }
					}
				}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			Check(ref ok, eyes >= 5 && sky == 0 && under == 0, "size " + size + "x, from inside (" + eyes + " places, " + noFloor + " without a floor): " + sky + " looks to the sky, " + under + " to the ground from below" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));

			yield return CameraShot(mouth - flat * 14f * size + Vector3.up * 4f * size, mouth + Vector3.up * 2f * size, "cavetool_world_mouth" + tag);
			yield return CameraShot(mouth + flat * 3f * size + Vector3.up * 1.6f, mouth + flat * 14f * size + Vector3.up * 0.8f, "cavetool_world_tunnel" + tag);
			yield return CameraShot(end + flat * 6f * size + Vector3.up * 1.8f, end + flat * 20f * size + Vector3.up * 3f * size, "cavetool_world_den" + tag);
			yield return CameraShot(end + flat * 10f * size + Vector3.up * 1.8f, end - flat * 6f * size + Vector3.up * 3f * size, "cavetool_world_back" + tag);

			if (args0Keep) { Log("  (island kept: " + CaveToolIsland + ")"); }
			else
			{
				IslandWorldState.RemoveIds(new[] { e.Id }, true);
				yield return null;
				try { File.Delete(path); } catch { }
			}
			if (ok) Log("PASS: cave tool world"); else Fail("cave tool world");
		}

		static bool args0Keep;

		[ConsoleCommand(name: "CIProbeMaterials", docs: "Dev, anywhere: logs each named prefab's renderers - material, shader, colour properties, main texture. CIProbeMaterials <name> [name...]")]
		public static void ProbeMaterialsCommand(string[] args) { StartTest(ProbeMaterialsRoutine(args)); }

		static IEnumerator ProbeMaterialsRoutine(string[] args)
		{
			yield return PlaceableCatalog.EnsureIndex();
			yield return PlaceableCatalog.EnsureLoaded(args);
			foreach (string name in args)
			{
				GameObject go = PlaceableCatalog.Get(name);
				if (go == null) { Log("  " + name + ": no prefab"); continue; }
				foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
					foreach (Material m in r.sharedMaterials)
					{
						if (m == null) continue;
						string cols = string.Join(" ", new[] { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_AlbedoColor", "_EmissionColor" }.Where(m.HasProperty).Select(c => c + "=" + ColorUtility.ToHtmlStringRGBA(m.GetColor(c))).ToArray());
						string texs = string.Join(" ", m.GetTexturePropertyNames().Where(t => m.GetTexture(t) != null).Select(t => t + "=" + m.GetTexture(t).name).ToArray());
						Log("  " + name + " / " + r.name + " (" + r.GetType().Name + "): " + m.name + " shader " + m.shader.name + " | " + cols + " | " + texs);
					}
			}
			Log("PASS: materials probed");
		}
	}
}
