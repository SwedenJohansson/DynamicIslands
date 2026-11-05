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

		[ConsoleCommand(name: "CICaveToolUnit", docs: "Dev, anywhere: the cave tool's layout - tunnel pieces end to end from the mouth (their passages and floors meeting), the slope kept to 12 degrees and the pieces to 12, the den's mouth on the tunnel's end, a short drag one tunnel and the den; the hill over it, none before the mouth or past an open end")]
		public static void CaveToolUnitCommand(string[] args)
		{
			StartTest(CaveToolUnitRoutine());
		}

		static IEnumerator CaveToolUnitRoutine()
		{
			bool ok = true;
			Vector3 a = new Vector3(500f, 20f, 500f);
			CaveTool.Layout l = CaveTool.Plan(a, a + new Vector3(40f, 7f, 0f), 0f, true);
			Check(ref ok, l.Tunnels == 4 && l.Pieces.Count == 5 && l.Pieces.Count(p => p.Name == CaveTool.Room) == 1, "a 40 m drag: 4 tunnels and the den (" + l.Tunnels + ", " + l.Pieces.Count + " pieces)");
			Check(ref ok, CaveJoins(l) < 0.01f, "level: the passages meet end to end (worst " + CaveJoins(l).ToString("0.000") + " m), the first opens at the mouth");
			Vector3 denMouth = l.RoomPivot + l.RoomRotation * new Vector3(-1.072f, -4.05f - CaveTool.RoomLift, 15.99f);
			Check(ref ok, (denMouth - (l.End - l.Flat * CaveTool.RoomIn)).magnitude < 0.01f && (l.End - (a + Vector3.right * 4f * CaveTool.Step)).magnitude < 0.01f, "the den's mouth over the tunnel's far end (" + (denMouth - (l.End - l.Flat * CaveTool.RoomIn)).magnitude.ToString("0.000") + " m off)");
			Check(ref ok, Vector3.Angle(l.RoomRotation * Vector3.forward, Vector3.left) < 0.5f, "the den faces back up the tunnel");

			CaveTool.Layout down = CaveTool.Plan(a, a + new Vector3(0f, 0f, -50f), -8f, false);
			Check(ref ok, CaveJoins(down) < 0.01f && down.Tunnels == 5 && !down.Room, "8 degrees down, open: 5 tunnels end to end (worst " + CaveJoins(down).ToString("0.000") + " m)");
			Check(ref ok, Mathf.Abs(down.End.y - (a.y - Mathf.Sin(8f * Mathf.Deg2Rad) * 5f * CaveTool.Step)) < 0.01f && Mathf.Abs(down.FloorAt(down.Length) - down.End.y) < 0.01f, "its floor goes down " + (a.y - down.End.y).ToString("0.0") + " m");
			Check(ref ok, Mathf.Abs(CaveTool.Plan(a, a + Vector3.forward * 40f, 30f, true).Slope - CaveTool.MaxSlope) < 0.01f, "a slope of 30 degrees is kept to " + CaveTool.MaxSlope);
			Check(ref ok, CaveTool.Plan(a, a + Vector3.forward * 900f, 0f, true).Tunnels == CaveTool.MaxTunnels, "a long drag: " + CaveTool.MaxTunnels + " tunnels at most");
			CaveTool.Layout den = CaveTool.Plan(a, a + Vector3.forward * 3f, 0f, true);
			Check(ref ok, den.Tunnels == 1 && den.Pieces.Count == 2 && CaveJoins(den) < 0.01f, "a short drag with a room: one tunnel and the den (a den alone had no mouth of its own)");
			// (the mouth's frame: wider than the rock, its wall before the mouth, its top over the rock; the far end's mirrored)
			foreach (float sl in new[] { -CaveTool.MaxSlope, 0f, CaveTool.MaxSlope })
				foreach (float k in new[] { CaveTool.MinSize, 1f, CaveTool.MaxSize })
				{
					CaveTool.Portal pd = CaveTool.PortalOf(k, sl);
					float a12 = sl * Mathf.Deg2Rad;
					bool fits = pd.Half > CaveTool.ShellHalfWidth * k && pd.Wall < 0f && pd.SillFrom < pd.Wall - 2.9f && pd.CapTo > pd.Wall + 2.9f
						&& pd.CapAt(0f) > (CaveTool.ShellTop - CaveTool.Floor + CaveTool.EndDip) * k && pd.Wall <= -Mathf.Max(0f, Mathf.Sin(a12)) * (CaveTool.ShellTop + 0.9f) * k;
					if (!fits) { Check(ref ok, false, "the mouth's frame at " + sl + " degrees, " + k + "x: half " + pd.Half.ToString("0.0") + ", wall " + pd.Wall.ToString("0.00") + ", top " + pd.CapAt(0f).ToString("0.0")); }
				}
			CaveTool.Layout fe = CaveTool.FarEnd(down);
			Check(ref ok, (fe.Mouth - down.End).magnitude < 0.01f && Vector3.Dot(fe.Flat, down.Flat) < -0.99f && Mathf.Abs(fe.FloorAt(fe.Length) - a.y) < 0.01f, "the open far end seen as a mouth: back up the tunnel to its mouth");
			Check(ref ok, CaveTool.Plan(a, a + Vector3.forward * 3f, 0f, false).Tunnels == 1, "a short drag without: one tunnel");

			// (PF-D2b: smaller and bigger - the pieces scaled, still end to end, the den on the end, the hill over the scaled rock)
			foreach (float k in new[] { CaveTool.MinSize, CaveTool.MaxSize })
			{
				CaveTool.Layout sl = CaveTool.Plan(a, a + new Vector3(40f * k, 0f, 0f), -6f, true, k);
				Vector3 dm = sl.RoomPivot + sl.RoomRotation * (new Vector3(-1.072f, -4.05f - CaveTool.RoomLift, 15.99f) * k);
				float hillTop = CaveTool.HillAt(sl, a + new Vector3(sl.Length / 2f, 0f, 0f));
				Check(ref ok, sl.Tunnels == 4 && sl.Pieces.All(p => Mathf.Abs(p.Scale - k) < 1e-4f) && CaveJoins(sl) < 0.01f && (dm - (sl.End - sl.Flat * (CaveTool.RoomIn * k))).magnitude < 0.01f
					&& Mathf.Abs(sl.Length - 4f * CaveTool.Step * k * Mathf.Cos(6f * Mathf.Deg2Rad)) < 0.01f && hillTop >= sl.FloorAt(sl.Length / 2f) + CaveTool.ShellTop * k,
					"size " + k + "x: 4 pieces at " + k + "x end to end (worst " + CaveJoins(sl).ToString("0.000") + " m), the den over the end (" + (dm - (sl.End - sl.Flat * (CaveTool.RoomIn * k))).magnitude.ToString("0.000") + " m off), " + sl.Length.ToString("0.0") + " m long, the hill over its rock");
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

			// (the den's walls - Plug at a tunnel's end, PlugOpening on a lone den - drawn both ways with real normals: shared
			// corners cancelled them and the wall drew as a flat black board, TN1 and The Abyss's lava tube 11-05)
			yield return PlaceableCatalog.EnsureLoaded(new[] { CaveTool.Room, CaveTool.WallRock });
			GameObject denProto = PlaceableCatalog.Get(CaveTool.Room);
			if (denProto == null) Check(ref ok, false, "the bear den could be loaded (to check its walls)");
			else
			{
				GameObject probe = UnityEngine.Object.Instantiate(denProto, new Vector3(-4000f, -300f, -4000f), Quaternion.identity);
				probe.SetActive(true);
				try
				{
					foreach (bool lone in new[] { false, true })
					{
						if (lone) CaveTool.PlugOpening(probe); else CaveTool.Plug(probe);
						Transform pl = probe.transform.Find(CaveTool.PlugName);
						Mesh m = pl != null ? pl.GetComponent<MeshFilter>().sharedMesh : null;
						Vector3[] nr = m != null ? m.normals : new Vector3[0];
						float face = lone ? 0.3f : 0.99f; // (a lone den's wall has rock relief: its facets lean, but each still faces out one way)
						int fwd = nr.Count(v => v.z > face), back = nr.Count(v => v.z < -face);
						Check(ref ok, m != null && nr.Length > 0 && fwd + back == nr.Length && fwd == back,
							(lone ? "a lone den's wall (PlugOpening)" : "a tunnel-end den's wall (Plug)") + ": every corner's normal faces one way out of the wall, as many each way (" + fwd + " + " + back + " of " + nr.Length + ")");
						if (lone && m != null && nr.Length > 0)
						{
							// (not a flat board: a smooth marbled sheet beside The Abyss's acid path, 11-05)
							Vector3[] vs = m.vertices;
							float spread = vs.Max(v => v.z) - vs.Min(v => v.z);
							int tilted = nr.Count(v => Mathf.Abs(v.z) < 0.97f);
							// (not the den's atlas texture at 20 m: blue, white and orange patches beside The Abyss's acid path, 11-05)
							Material denMat = probe.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.sharedMaterial != null && r.transform != pl)
								.OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).First().sharedMaterial;
							Material wm = pl.GetComponent<MeshRenderer>().sharedMaterial;
							Check(ref ok, wm != denMat, "a lone den's wall is drawn with Raft's rock (" + (wm != null ? wm.name : "none") + "), not the den's atlas (" + denMat.name + ")");
							Check(ref ok, spread > 0.5f && tilted > nr.Length / 4, "a lone den's wall has rock relief: " + spread.ToString("0.00") + " m deep (over 0.5), "
								+ tilted + " of " + nr.Length + " corners on tilted facets (over a quarter)");
						}
					}
				}
				finally { UnityEngine.Object.DestroyImmediate(probe); }
			}
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
			Check(ref ok, selected == null || !laid.Any(g => selected.Contains(g.transform)), "nothing of the cave is selected after (its gizmo stood on it)");
			Transform frame = laid[0].transform.Find(CaveTool.MouthName);
			Check(ref ok, frame != null && ObjectProps.Get(laid[0].GetComponent<EditorGameObject>().Props, ObjectProps.CaveMouth) == size.ToString("0.###", CultureInfo.InvariantCulture) && laid.Count(g => g.transform.Find(CaveTool.MouthName) != null) == 1, "the mouth's rock frame on the first tunnel, none elsewhere (a den at the end)");
			Check(ref ok, laid.Count(g => g.transform.Find(CaveTool.JoinName) != null) == 4, "a floor plate over each of the 4 joins (tunnels and den)");
			Physics.SyncTransforms();

			// Walkable: from the mouth to 8 m into the den, a floor of the cave's own rock every metre, no step over 0.6 m, 2 m over it
			var pieces = new HashSet<Collider>(laid.SelectMany(g => g.GetComponentsInChildren<Collider>()));
			string walked;
			Check(ref ok, Walkable(l, pieces, mouth, size, l.Length + 8f * size, out walked), walked);

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
			Check(ref ok, back.Count == 5 && back.Any(e => (e.transform.position - firstAt).magnitude < 0.05f && Mathf.Abs(e.transform.lossyScale.x - firstScale) < 0.001f) && back.Where(e => e.GameObjectName == CaveTool.Tunnel).All(e => ObjectProps.Get(e.Props, ObjectProps.Skin) == ObjectProps.SkinRock) && back.Any(e => e.transform.Find(CaveTool.PlugName) != null)
				&& back.Count(e => e.transform.Find(CaveTool.MouthName) != null) == 1 && back.Count(e => e.transform.Find(CaveTool.JoinName) != null) == 4, "loaded again: the 5 pieces where they were at their size, brown, the den walled, the mouth framed, the joins plated (" + back.Count + ")");

			// Pictures: the mouth from outside, and down the tunnel from inside it
			Vector3 eye = mouth - l.Flat * 14f + Vector3.up * 4f;
			string tag = Mathf.Abs(size - 1f) < 0.01f ? "" : "_" + size.ToString("0.0", CultureInfo.InvariantCulture);
			yield return CameraShot(eye, mouth + Vector3.up * 2f, "cavetool_mouth" + tag);
			yield return CameraShot(mouth + l.Flat * 2f + Vector3.up * 1.6f, mouth + l.Flat * 12f + Vector3.up * 0.6f, "cavetool_inside" + tag);
			if (ok) Log("PASS: cave tool editor"); else Fail("cave tool editor");
		}

		/// <summary>Walkable from the mouth to to (m along): a floor of the cave's own rock (pieces) every half metre, no step
		/// over 0.6 m, 2 m of room over it.</summary>
		static bool Walkable(CaveTool.Layout l, HashSet<Collider> pieces, Vector3 mouth, float size, float to, out string said)
		{
			// (a ball the player's width rolled down from over the floor, every half metre. Where two pieces meet, their floors
			// leave a narrow groove - Raft's labyrinth joins them the same way: one sample in it, missed or lower than both its
			// neighbours, is the groove and counts as no step; two missed in a row are a gap)
			float worstStep = 0f, leastRoom = 99f;
			string gap = null, worstAt = "";
			var ss = new List<float>();
			var ys = new List<float?>();
			var profile = new List<string>();
			for (float s = 0.5f; s <= to; s += 0.5f)
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
			said = "walkable end to end: " + (gap ?? "a floor every half metre (" + grooves + " joins' grooves)") + ", worst step " + worstStep.ToString("0.00") + " m" + worstAt + ", least room over it " + leastRoom.ToString("0.0") + " m";
			if (!walk) Log("  floor (m in: over the line's floor, what): " + string.Join("; ", profile.ToArray()));
			return walk;
		}

		[ConsoleCommand(name: "CICaveToolCases", docs: "Dev, editor: the cave tool in every case on a generated island - eight ways round, slopes -12/0/12, sizes 0.7/1/1.5, room on and off, hill on and off (CICaveToolCases [quick] [shots] [from N]): each has its mouth's frame (and an open end's), the way in its full size from before the mouth, no ground in the passage from the doorstep to the end, walkable mouth to end (and out of an open end), one Ctrl+Z takes it back")]
		public static void CaveToolCasesCommand(string[] args)
		{
			int from = 0;
			int at = System.Array.IndexOf(args, "from");
			if (at >= 0 && at + 1 < args.Length) int.TryParse(args[at + 1], out from);
			StartTest(CaveToolCasesRoutine(args.Contains("quick"), args.Contains("shots"), from));
		}

		static IEnumerator CaveToolCasesRoutine(bool quick, bool shots, int from)
		{
			if (!DynamicIslands.InEditor()) { Fail("cave tool cases: in the editor"); yield break; }
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
			int hres = data.heightmapResolution;
			float[,] before = data.GetHeights(0, 0, hres, hres);

			var cases = new List<KeyValuePair<string, float[]>>(); // (slope, size, room, hill)
			foreach (float sl in new[] { -CaveTool.MaxSlope, 0f, CaveTool.MaxSlope })
				foreach (float k in new[] { CaveTool.MinSize, 1f, CaveTool.MaxSize })
					foreach (bool room in new[] { true, false })
						foreach (bool hill in new[] { true, false })
							cases.Add(new KeyValuePair<string, float[]>("", new[] { sl, k, room ? 1f : 0f, hill ? 1f : 0f }));
			if (quick) cases = cases.Where((x, i) => i % 5 == 0).ToList();
			int passed = 0, run = 0;
			float started = Time.realtimeSinceStartup;
			for (int i = from; i < cases.Count; i++)
			{
				float sl = cases[i].Value[0], k = cases[i].Value[1];
				bool room = cases[i].Value[2] > 0f, hill = cases[i].Value[3] > 0f;
				float ang = 45f * (i % 8) + 22.5f * ((i / 8) % 2);
				Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
				string name = "case " + i + " (" + ang + "°, " + sl + "°, " + k + "x, " + (room ? "room" : "open") + ", " + (hill ? "hill" : "no hill") + ")";
				// (the mouth on land out from the middle, the tunnel in towards it)
				Vector3 mouth = ground(c - dir * 55f);
				for (int j = 0; j < 8 && mouth.y < sea + 3f; j++) mouth = ground(mouth + dir * 6f);
				List<GameObject> laid = null;
				float t0 = Time.realtimeSinceStartup;
				yield return CaveTool.Lay(mouth, mouth + dir * ((room ? 22f : 32f) * k), sl, room, hill, k, m => laid = m);
				CaveTool.Layout l = CaveTool.LastLayout;
				run++;
				var problems = new List<string>();
				if (laid == null || laid.Count == 0 || l == null) { Check(ref ok, false, name + ": nothing laid"); continue; }
				int dug;
				int cut = CaveGround.EditorNow(t, out dug);
				float took = Time.realtimeSinceStartup - t0;
				Physics.SyncTransforms();
				var pieces = new HashSet<Collider>(laid.SelectMany(g => g.GetComponentsInChildren<Collider>()));

				int frames = laid.Sum(g => g.transform.Cast<Transform>().Count(x => x.name == CaveTool.MouthName));
				if (frames != (room ? 1 : 2)) problems.Add(frames + " frames");
				if (DynamicIslands.EditorGizmoHandler != null && DynamicIslands.EditorGizmoHandler.SelectedRoots != null && laid.Any(g => DynamicIslands.EditorGizmoHandler.SelectedRoots.Contains(g.transform))) problems.Add("selected");

				// (the way in: looking along it from before the doorstep, nothing in the way to well inside - at three heights, three places across)
				CaveTool.Portal pd = CaveTool.PortalOf(l.Size, l.Slope);
				Vector3 across = Vector3.Cross(Vector3.up, l.Flat);
				float shortest = 99f;
				string blocked = null;
				foreach (float h in new[] { 0.6f, 1.5f, 2.4f })
					foreach (float q in new[] { -0.6f * k, 0f, 0.6f * k })
					{
						// (level to the mouth, then up or down with the floor)
						float s0 = pd.SillFrom - 0.5f;
						Vector3 eye = l.Mouth + l.Flat * s0 + across * q + Vector3.up * h, turn = l.Mouth + across * q + Vector3.up * h;
						RaycastHit hit;
						float reach = Physics.Raycast(eye, l.Flat, out hit, -s0, ~0, QueryTriggerInteraction.Ignore) ? hit.distance + s0
							: Physics.Raycast(turn, l.Dir, out hit, 12f * k, ~0, QueryTriggerInteraction.Ignore) ? hit.distance * Vector3.Dot(l.Dir, l.Flat) : 40f;
						if (reach < shortest) shortest = reach;
						if (reach < 1.5f && blocked == null) blocked = "way in blocked " + h + " m up, " + q.ToString("0.0") + " across, " + reach.ToString("0.0") + " m in, by " + hit.collider.name;
					}
				if (blocked != null) problems.Add(blocked);

				// (nothing but the cave on the doorstep: no rock or plant standing on it, in the way or not)
				{
					float s0 = pd.SillFrom, s1 = Mathf.Max(pd.Wall, s0 + 0.5f), y0 = 0.4f, y1 = Mathf.Max(pd.Height, 1f);
					Vector3 mid = l.Mouth + l.Flat * ((s0 + s1) / 2f) + Vector3.up * ((y0 + y1) / 2f);
					Collider on = Physics.OverlapBox(mid, new Vector3(pd.Half, (y1 - y0) / 2f, (s1 - s0) / 2f), Quaternion.LookRotation(l.Flat, Vector3.up), ~0, QueryTriggerInteraction.Ignore)
						.FirstOrDefault(x => !(x is TerrainCollider) && !pieces.Contains(x) && x.GetComponentInParent<EditorGameObject>() != null);
					if (on != null) { Vector3 r = on.bounds.center - l.Mouth; problems.Add("on the doorstep: " + on.GetComponentInParent<EditorGameObject>().GameObjectName + " (its middle " + Vector3.Dot(r, l.Flat).ToString("0.0") + " m in, " + Vector3.Dot(r, across).ToString("0.0") + " across, " + r.y.ToString("0.0") + " up, " + on.bounds.size.ToString("F1") + ")"); }
				}

				// (no ground in the passage: from the doorstep to the end, the first thing under a look down is the cave's own floor)
				int looked = 0, inWay = 0;
				string firstIn = null;
				for (float s = pd.SillFrom + 0.3f; s <= l.Length - 0.5f; s += 0.5f)
					foreach (float q in new[] { -0.8f * k, 0f, 0.8f * k })
					{
						float fy = s <= 0f ? l.Mouth.y : l.FloorAt(s);
						Vector3 p = l.Mouth + l.Flat * s + across * q;
						RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, fy + 1.8f * k, p.z), Vector3.down, 3f * k, ~0, QueryTriggerInteraction.Ignore).OrderBy(x => x.distance).ToArray();
						RaycastHit first = hits.FirstOrDefault(x => x.collider is TerrainCollider || pieces.Contains(x.collider));
						if (first.collider == null) continue;
						looked++;
						if (first.collider is TerrainCollider) { inWay++; if (firstIn == null) firstIn = s.ToString("0.0") + " m in, " + q.ToString("0.0") + " across"; }
					}
				if (inWay > 0 || looked < 10) problems.Add("ground in the passage " + inWay + " of " + looked + (firstIn != null ? " (first " + firstIn + ")" : ""));

				string walked;
				if (!Walkable(l, pieces, l.Mouth, l.Size, room ? l.Length + 6f * k : l.Length + 2f, out walked)) problems.Add(walked);

				if (shots && (i % 6 == 0 || problems.Count > 0))
				{
					yield return CameraShot(l.Mouth - l.Flat * 12f * k + Vector3.up * 3f * k, l.Mouth + Vector3.up * 2f * k, "cavecase_" + i + "_mouth");
					yield return CameraShot(l.Mouth - l.Flat * 1.5f + Vector3.up * 1.6f * k, l.Mouth + l.Flat * 10f + Vector3.up * 1.2f * k, "cavecase_" + i + "_in");
					if (!room) yield return CameraShot(l.End + l.Flat * 12f * k + Vector3.up * 3f * k, l.End + Vector3.up * 2f * k, "cavecase_" + i + "_end");
				}

				CommandUndoRedo.UndoRedoManager.Undo();
				yield return null;
				int differ = Differ(before, data.GetHeights(0, 0, hres, hres));
				if (differ > 0 || laid.Any(g => g != null && g.activeSelf)) problems.Add("undo left " + differ + " points, " + laid.Count(g => g != null && g.activeSelf) + " pieces");
				CaveGround.EditorNow(t, out dug);
				if (problems.Count == 0) passed++;
				Log("  " + name + ": " + (problems.Count == 0 ? "ok" : string.Join("; ", problems.ToArray())) + " - " + l.Tunnels + " tunnels, way in " + shortest.ToString("0.0") + " m clear, " + cut + " cells cut, " + took.ToString("0.0") + " s");
				if (problems.Count > 0) ok = false;
			}
			Log("  " + passed + " of " + run + " cases right, " + (Time.realtimeSinceStartup - started).ToString("0") + " s");
			if (ok) Log("PASS: cave tool cases"); else Fail("cave tool cases (" + (run - passed) + " of " + run + ")");
		}

		[ConsoleCommand(name: "CICaveToolInside", docs: "Dev, editor: the cave tool's tunnel seen from inside (TN1) - laid as the user laid it (CICaveToolInside [slope] [size] [room 1/0] [hill 1/0] [keep], default -2 0.8 1 1) on a generated island, and on three more ways round: from eye height along the passage and in the den, 81 looks each meet the cave's rock or the ground from above - none the sky, the ground from below or anything through the hill; the tunnel and den pieces each pass the X7 see-through check; pictures from the den back up the tunnel and from the passage out of the mouth")]
		public static void CaveToolInsideCommand(string[] args)
		{
			System.Func<int, float, float> arg = (i, d) => { float v; return args.Length > i && float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d; };
			StartTest(CaveToolInsideRoutine(arg(0, -2f), arg(1, 0.8f), arg(2, 1f) > 0f, arg(3, 1f) > 0f, args.Contains("keep")));
		}

		static IEnumerator CaveToolInsideRoutine(float slope, float size, bool room, bool hill, bool keep)
		{
			if (!DynamicIslands.InEditor()) { Fail("cave tool inside: in the editor"); yield break; }
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
			var looks = new List<Vector3>();
			foreach (float pitch in new[] { -20f, 0f, 15f, 35f, 60f })
				for (int i = 0; i < 16; i++) looks.Add(Quaternion.Euler(-pitch, i * 22.5f, 0f) * Vector3.forward);
			looks.Add(Vector3.up);
			System.Func<Collider, bool> counts = col => col != null && !col.isTrigger && col.GetComponentInParent<AI_NetworkBehaviour>() == null && col.GetComponentInParent<Network_Player>() == null;
			int ways = 0;
			foreach (float ang in new[] { 0f, 135f, 225f, 300f })
			{
				Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
				Vector3 mouth = ground(c - dir * 55f);
				for (int j = 0; j < 8 && mouth.y < sea + 3f; j++) mouth = ground(mouth + dir * 6f);
				List<GameObject> laid = null;
				yield return CaveTool.Lay(mouth, mouth + dir * ((room ? 22f : 32f) * size), slope, room, hill, size, m => laid = m);
				CaveTool.Layout l = CaveTool.LastLayout;
				string name = ang + "°, " + slope + "°, " + size + "x, " + (room ? "room" : "open") + ", " + (hill ? "hill" : "no hill");
				if (laid == null || laid.Count == 0 || l == null) { Check(ref ok, false, name + ": nothing laid"); continue; }
				ways++;
				int dug;
				CaveGround.EditorNow(t, out dug);
				yield return null;
				Physics.SyncTransforms();
				var pieces = new HashSet<Transform>(laid.Select(g => g.transform));
				System.Func<Collider, bool> caveRock = col =>
				{
					for (Transform tr = col.transform; tr != null; tr = tr.parent) if (pieces.Contains(tr)) return true;
					return false;
				};
				// (eyes: along the passage every 3 m, and round the den's floor)
				var eyes = new List<Vector3>();
				float far = l.Length + (room ? 12f * size : 0f);
				for (float s = 2f; s <= far; s += 3f * Mathf.Min(1f, size))
				{
					Vector3 p = l.Mouth + l.Flat * s; p.y = l.FloorAt(s) + 1.2f * size;
					foreach (float q in s > l.Length ? new[] { -4f * size, 0f, 4f * size } : new[] { 0f })
					{
						RaycastHit fh;
						Vector3 pq = p + Vector3.Cross(Vector3.up, l.Flat) * q;
						if (!Physics.Raycast(pq, Vector3.down, out fh, 4f * size, ~0, QueryTriggerInteraction.Ignore) || fh.collider is TerrainCollider || !caveRock(fh.collider)) continue;
						eyes.Add(fh.point + Vector3.up * 1.6f);
					}
				}
				int sky = 0, under = 0, other = 0;
				var seen = new List<string>();
				bool backs0 = Physics.queriesHitBackfaces;
				try
				{
					foreach (Vector3 eye in eyes)
						foreach (Vector3 d in looks)
						{
							Physics.queriesHitBackfaces = true;
							RaycastHit[] all = Physics.RaycastAll(eye, d, 150f, ~0, QueryTriggerInteraction.Ignore).Where(h => counts(h.collider)).OrderBy(h => h.distance).ToArray();
							Physics.queriesHitBackfaces = false;
							// (out by the mouth's own opening: a look that crosses the mouth's plane inside the passage before meeting anything)
							float back = Vector3.Dot(d, -l.Flat);
							if (back > 0.05f)
							{
								float tm = Vector3.Dot(eye - l.Mouth, l.Flat) / back;
								Vector3 cross = eye + d * tm, rel = cross - l.Mouth; rel.y = 0f;
								if ((all.Length == 0 || all[0].distance > tm - 0.3f) && Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, l.Flat))) < CaveTool.ShellHalfWidth * size && cross.y < l.Mouth.y + l.Top) continue;
							}
							if (!room && Vector3.Dot(d, l.Flat) > 0.05f)
							{
								float te = Vector3.Dot(l.End - eye, l.Flat) / Vector3.Dot(d, l.Flat);
								if (all.Length == 0 || all[0].distance > te - 0.3f) continue;
							}
							float inAt = Vector3.Dot(eye - l.Mouth, l.Flat);
							string where = inAt.ToString("0") + " m in, " + d.ToString("F1");
							if (all.Length == 0) { sky++; seen.Add("sky " + where); }
							else if (all[0].collider is TerrainCollider) { if (all[0].normal.y < 0f) { under++; seen.Add("the ground from below " + all[0].distance.ToString("0.0") + " m off, " + where); } }
							// (a thing deep in the hill - a boulder its ground was raised over - is seen by no one: the den's rock is
							// drawn round it, which the X7 check below makes sure of; only its collider is missing up there)
							else if (!caveRock(all[0].collider) && all[0].point.y > t.SampleHeight(all[0].point) + o.y - 0.3f) { other++; seen.Add("out to " + all[0].collider.name + " " + all[0].distance.ToString("0") + " m, " + where); }
						}
				}
				finally { Physics.queriesHitBackfaces = backs0; }
				Check(ref ok, eyes.Count >= 5 && sky + under + other == 0, name + ", from inside (" + eyes.Count + " places, 81 looks each): " + sky + " looks to the sky, " + under + " to the ground from below, " + other + " to other things" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));
				// (each piece by the X7 check)
				var badPieces = new List<string>();
				foreach (GameObject g in laid)
				{
					string detail; Vector3 e2, l2;
					string n = g.name.Replace("(Clone)", "").Trim();
					EditorGameObject ego = g.GetComponent<EditorGameObject>();
					if (ego != null) n = ego.GameObjectName;
					if (CaveSeeThrough(g.transform, RaftProps.Get(n), out detail, out e2, out l2) > 0) badPieces.Add(n + ": " + detail);
				}
				Check(ref ok, badPieces.Count == 0, name + ", each piece from inside: " + (laid.Count - badPieces.Count) + " of " + laid.Count + " not seen through" + (badPieces.Count > 0 ? ": " + string.Join("; ", badPieces.Take(3).ToArray()) : ""));
				if (ang == 0f || badPieces.Count > 0 || sky + under + other > 0)
				{
					string tag = "caveinside_" + (int)ang;
					if (room)
					{
						Vector3 den = l.End + l.Flat * 7f * size; den.y = l.FloorAt(l.Length) + 1.8f * size;
						yield return CameraShot(den, l.End + Vector3.up * 1.4f * size, tag + "_den");
						yield return CameraShot(l.End - l.Flat * 2f + Vector3.up * 1.6f * size, l.End + l.Flat * 10f + Vector3.up * 1.6f * size, tag + "_intoden");
					}
					Vector3 mid = l.Mouth + l.Flat * (l.Length * 0.6f); mid.y = l.FloorAt(l.Length * 0.6f) + 1.6f * size;
					yield return CameraShot(mid, l.Mouth + Vector3.up * 1.4f * size, tag + "_out");
					yield return CameraShot(l.Mouth + l.Flat * 1.5f + Vector3.up * 1.6f * size, l.Mouth - l.Flat * 6f + Vector3.up * 1.2f * size, tag + "_mouth");
					yield return CameraShot(l.Mouth - l.Flat * 12f * size + Vector3.up * 3f * size, l.Mouth + Vector3.up * 2f * size, tag + "_front");
				}
				if (!keep || ang != 300f)
				{
					CommandUndoRedo.UndoRedoManager.Undo();
					yield return null;
					CaveGround.EditorNow(t, out dug);
				}
			}
			if (ok && ways == 4) Log("PASS: cave tool inside"); else Fail("cave tool inside");
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

		[ConsoleCommand(name: "CICaveToolProbe", docs: "Dev, editor: lays one cave tool case on the cases' island (CICaveToolProbe <case>) and logs its frame against the layout, what a look in meets, and the ground's cells around the mouth (cut or not, under the frame or not); left laid")]
		public static void CaveToolProbeCommand(string[] args)
		{
			int i = 0;
			if (args.Length > 0) int.TryParse(args[0], out i);
			StartTest(CaveToolProbeRoutine(i));
		}

		static IEnumerator CaveToolProbeRoutine(int i)
		{
			if (!DynamicIslands.InEditor()) { Fail("cave tool probe: in the editor"); yield break; }
			DynamicIslands.NewIsland();
			yield return null;
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4242, Radius = 130f, Height = 30f, Style = TerrainPainter.Tropical });
			yield return null;
			Terrain t = terraineditor.terrain;
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorWaterLevel;
			System.Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };
			float[] sls = { -CaveTool.MaxSlope, 0f, CaveTool.MaxSlope }, ks = { CaveTool.MinSize, 1f, CaveTool.MaxSize };
			float sl = sls[i / 12 % 3], k = ks[i / 4 % 3];
			bool room = i / 2 % 2 == 0, hill = i % 2 == 0;
			float ang = 45f * (i % 8) + 22.5f * ((i / 8) % 2);
			Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
			Vector3 mouth = ground(c - dir * 55f);
			for (int j = 0; j < 8 && mouth.y < sea + 3f; j++) mouth = ground(mouth + dir * 6f);
			List<GameObject> laid = null;
			yield return CaveTool.Lay(mouth, mouth + dir * ((room ? 22f : 32f) * k), sl, room, hill, k, m => laid = m);
			CaveTool.Layout l = CaveTool.LastLayout;
			if (laid == null || laid.Count == 0) { Fail("probe: nothing laid"); yield break; }
			int dug;
			CaveGround.TraceAt = l.Mouth + new Vector3(dir.x, 0f, dir.z).normalized * 1.2f;
			CaveGround.Trace = m => Log("    trace " + m);
			CaveGround.EditorNow(t, out dug);
			CaveGround.Trace = null;
			Physics.SyncTransforms();
			Transform f = laid[0].transform.Find(CaveTool.MouthName);
			Transform fs = f != null ? f.Find(CaveTool.FrameSpaceName) ?? f : null;
			CaveTool.Portal pd = CaveTool.PortalOf(l.Size, l.Slope);
			Vector3 across = Vector3.Cross(Vector3.up, l.Flat);
			System.Func<Vector3, string> sqy = p => { Vector3 r = p - l.Mouth; return "s " + Vector3.Dot(r, l.Flat).ToString("0.00") + " q " + Vector3.Dot(r, across).ToString("0.00") + " y " + r.y.ToString("0.00"); };
			Bounds fb = new Bounds();
			if (f != null) { Vector3[] fv = f.GetComponent<MeshFilter>().sharedMesh.vertices; fb = new Bounds(fs.InverseTransformPoint(f.TransformPoint(fv[0])), Vector3.zero); foreach (Vector3 v in fv) fb.Encapsulate(fs.InverseTransformPoint(f.TransformPoint(v))); }
			GameObject proto = PlaceableCatalog.Get(CaveTool.Tunnel);
			Log("  probe: the tunnel's own scale " + (proto != null ? proto.transform.localScale.ToString("F3") : "?"));
			Log("  probe case " + i + " (" + ang + "°, " + sl + "°, " + k + "x, " + (room ? "room" : "open") + ", " + (hill ? "hill" : "no hill") + "): mouth " + l.Mouth.ToString("F2") + ", frame at " + (f != null ? sqy(f.position) + " fwd·flat " + Vector3.Dot(fs.forward, l.Flat).ToString("0.000") + " up " + fs.up.y.ToString("0.000") + " scale " + f.lossyScale.ToString("F3") : "none")
				+ "; frame bounds " + fb.min.ToString("F2") + ".." + fb.max.ToString("F2") + "; portal half " + pd.Half.ToString("0.00") + " wall " + pd.Wall.ToString("0.00") + " capTo " + pd.CapTo.ToString("0.00") + " sill " + pd.SillFrom.ToString("0.00") + " height " + pd.Height.ToString("0.00")
				+ "; piece 0 at " + sqy(laid[0].transform.position) + " scale " + laid[0].transform.lossyScale.ToString("F3") + " fwd " + laid[0].transform.forward.ToString("F3"));
			foreach (float h in new[] { 0.6f, 1.5f, 2.4f })
				foreach (float q in new[] { -0.6f * k, 0f, 0.6f * k })
				{
					Vector3 eye = l.Mouth + l.Flat * (pd.SillFrom - 0.5f) + across * q + Vector3.up * h;
					RaycastHit hit;
					if (Physics.Raycast(eye, l.Flat, out hit, 40f, ~0, QueryTriggerInteraction.Ignore))
						Log("    look in " + h + " up, " + q.ToString("0.0") + " across: " + hit.collider.name + " at " + sqy(hit.point) + (f != null && hit.collider.transform == f ? " (frame local " + fs.InverseTransformPoint(hit.point).ToString("F2") + ")" : ""));
				}
			// the passage as the cases look at it: where the ground comes first, why its cells stay (traced)
			var own = new HashSet<Collider>(laid.SelectMany(g => g.GetComponentsInChildren<Collider>()));
			Vector3? firstGround = null;
			for (float s = pd.SillFrom + 0.3f; s <= l.Length - 0.5f; s += 0.5f)
				foreach (float q in new[] { -0.8f * k, 0f, 0.8f * k })
				{
					float fy = s <= 0f ? l.Mouth.y : l.FloorAt(s);
					Vector3 at = l.Mouth + l.Flat * s + across * q;
					RaycastHit[] hits = Physics.RaycastAll(new Vector3(at.x, fy + 1.8f * k, at.z), Vector3.down, 3f * k, ~0, QueryTriggerInteraction.Ignore).OrderBy(x => x.distance).ToArray();
					RaycastHit first = hits.FirstOrDefault(x => x.collider is TerrainCollider || own.Contains(x.collider));
					if (!(first.collider is TerrainCollider)) continue;
					RaycastHit next = hits.FirstOrDefault(x => own.Contains(x.collider));
					Log("    ground first at " + sqy(first.point) + " (floor line " + (fy - l.Mouth.y).ToString("0.00") + "), the cave under it " + (next.collider != null ? next.collider.name + " at y " + (next.point.y - l.Mouth.y).ToString("0.00") : "none"));
					if (firstGround == null) firstGround = first.point;
				}
			if (firstGround != null)
			{
				CaveGround.TraceAt = firstGround.Value;
				CaveGround.Trace = m => Log("    trace " + m);
				CaveGround.EditorNow(t, out dug);
				CaveGround.Trace = null;
			}
			// the cells around the mouth
			int res = data.heightmapResolution, cells = res - 1;
			float sx = data.size.x / cells, sz = data.size.z / cells;
			bool[,] holes = data.GetHoles(0, 0, cells, cells); // (true: solid)
			var rows = new List<string>();
			Vector3 lo = l.Mouth + l.Flat * (pd.SillFrom - 2f), hi = l.Mouth + l.Flat * (pd.CapTo + 4f);
			int x0 = Mathf.FloorToInt((Mathf.Min(lo.x, hi.x) - 5f - o.x) / sx), x1 = Mathf.CeilToInt((Mathf.Max(lo.x, hi.x) + 5f - o.x) / sx);
			int z0 = Mathf.FloorToInt((Mathf.Min(lo.z, hi.z) - 5f - o.z) / sz), z1 = Mathf.CeilToInt((Mathf.Max(lo.z, hi.z) + 5f - o.z) / sz);
			for (int z = z0; z < z1; z++)
				for (int x = x0; x < x1; x++)
				{
					Vector3 mid = new Vector3(o.x + (x + 0.5f) * sx, 0f, o.z + (z + 0.5f) * sz);
					Vector3 r = mid - l.Mouth;
					float s = Vector3.Dot(new Vector3(r.x, 0f, r.z), l.Flat), q = Vector3.Dot(new Vector3(r.x, 0f, r.z), across);
					if (Mathf.Abs(q) > pd.Half + 4f || s < pd.SillFrom - 2f || s > pd.CapTo + 4f) continue;
					var ys = new List<string>();
					int under = 0;
					foreach (Vector2Int cn in new[] { new Vector2Int(x, z), new Vector2Int(x + 1, z), new Vector2Int(x, z + 1), new Vector2Int(x + 1, z + 1) })
					{
						Vector3 p = new Vector3(o.x + cn.x * sx, 0f, o.z + cn.y * sz);
						p.y = o.y + data.GetHeight(cn.x, cn.y);
						Vector3 fl = f != null ? fs.InverseTransformPoint(p) : Vector3.zero;
						bool u = f != null && fl.x > fb.min.x + 0.02f && fl.x < fb.max.x - 0.02f && fl.z > fb.min.z + 0.02f && fl.z < fb.max.z - 0.02f && fl.y > fb.min.y - 3f && fl.y < fb.max.y + 0.5f;
						if (u) under++;
						ys.Add("(" + fl.z.ToString("0.0") + "," + fl.y.ToString("0.0") + (u ? "" : "!") + ")");
					}
					rows.Add("s " + s.ToString("0.0") + " q " + q.ToString("0.0") + (holes[z, x] ? " SOLID" : " cut") + " corners(z,y) " + string.Join(" ", ys.ToArray()));
				}
			foreach (string row in rows) Log("    " + row);
			yield return CameraShot(l.Mouth - l.Flat * 12f * k + Vector3.up * 3f * k, l.Mouth + Vector3.up * 2f * k, "caveprobe_mouth");
			yield return CameraShot(l.Mouth - l.Flat * 6f * k + across * 6f * k + Vector3.up * 2f * k, l.Mouth + Vector3.up * 1f * k, "caveprobe_side");
			Log("PASS: cave tool probe");
		}

		[ConsoleCommand(name: "CICaveToolWorld", docs: "Dev, in a world (host): the island CICaveToolEditor left (its cave tool cave) spawned ahead of the raft - the rock mountain over it leaves its mouth clear, from inside the tunnel and the den no look meets the sky or the ground from below; walked in with Raft's controller from 4 m before the mouth into the den, never lifted off the floor; pictures of the mouth and inside. The island and its file are taken away after")]
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
					if (tr.name == CaveTool.Tunnel || tr.name == CaveTool.Room || tr.name == CaveMountain.SkinName || tr.name == CaveTool.PlugName || tr.name == CaveTool.MouthName) return true;
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
						if (all.Length == 0)
						{
							sky++;
							string why = "";
							// (where the look comes down to the floor's height: what stands there, and is the ground cut - for finding the gap)
							if (sky <= 4 && d.y < -0.05f)
							{
								Vector3 low = eye + d * (1.6f / -d.y), rel = low - mouth;
								Terrain tr = e.Root.GetComponentInChildren<Terrain>();
								string cut = "";
								if (tr != null)
								{
									TerrainData td = tr.terrainData; int cells = td.heightmapResolution - 1;
									int cx = Mathf.FloorToInt((low.x - tr.transform.position.x) / td.size.x * cells), cz = Mathf.FloorToInt((low.z - tr.transform.position.z) / td.size.z * cells);
									cut = cx >= 0 && cz >= 0 && cx < cells && cz < cells ? (td.GetHoles(cx, cz, 1, 1)[0, 0] ? " ground there" : " ground cut there") + " (height " + (tr.SampleHeight(low) + tr.transform.position.y - fh.point.y).ToString("0.0") + " m from the floor)" : " off the ground";
								}
								string below = string.Join(", ", Physics.RaycastAll(low + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Take(4).Select(h => h.collider.name + (h.collider.isTrigger ? "(trigger)" : "") + " " + (h.point.y - fh.point.y).ToString("0.0")).ToArray());
								why = " [eye " + (Vector3.Dot(eye - mouth, Vector3.Cross(Vector3.up, flat))).ToString("0.0") + " across; down at " + Vector3.Dot(rel, flat).ToString("0.0") + " m in, " + Vector3.Dot(rel, Vector3.Cross(Vector3.up, flat)).ToString("0.0") + " across:" + cut + "; down from 3 m over it: " + (below == "" ? "nothing" : below) + "]";
							}
							seen.Add("sky " + s.ToString("0") + " m in, " + d.ToString("F1") + why);
						}
						else if (all[0].collider is TerrainCollider) { if (all[0].normal.y < 0f) { under++; seen.Add("the ground from below " + s.ToString("0") + " m in, " + d.ToString("F1")); } }
						else if (!caveRock(all[0].collider)) { sky++; seen.Add("out to " + all[0].collider.name + " " + all[0].distance.ToString("0") + " m, " + s.ToString("0") + " m in, " + d.ToString("F1")); }
					}
				}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			Check(ref ok, eyes >= 5 && sky == 0 && under == 0, "size " + size + "x, from inside (" + eyes + " places, " + noFloor + " without a floor): " + sky + " looks to the sky, " + under + " to the ground from below" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));

			// Walked in as a player walks in from Test: from 4 m before the mouth down the tunnel into the den with Raft's
			// controller (the user, 2026-11-01: a narrow mouth he could not get into, no way in, walking in pushed him up or
			// put him somewhere above) - the whole way walked, never lifted off the passage's floor or jumped in a frame
			{
				Network_Player me = RAPI.GetLocalPlayer();
				var way = new List<Vector3>();
				System.Func<float, float> floorAt = s => s <= 0f ? mouth.y : s <= v[6] ? Mathf.Lerp(mouth.y, end.y, s / Mathf.Max(0.1f, v[6])) : end.y;
				for (float s = 0f; s < v[6] + 4f * size; s += 2f) { Vector3 q = mouth + flat * s; q.y = floorAt(s); way.Add(q); }
				{ Vector3 q = mouth + flat * (v[6] + 4f * size); q.y = end.y; way.Add(q); }
				Vector3 from = mouth - flat * 4f * size;
				RaycastHit gh;
				from.y = (Physics.Raycast(from + Vector3.up * 6f, Vector3.down, out gh, 12f, ~0, QueryTriggerInteraction.Ignore) ? gh.point.y : mouth.y) + 1.1f;
				PlayerMove.To(me, from);
				yield return new WaitForSeconds(1.5f);
				IslandWorldState.Entry entry0 = playEntry;
				playEntry = e;
				bool got = true;
				string where = "";
				var ups = new List<float>();
				float jump = 0f, deepest = -99f; string jumpAt = "";
				Vector3 prev = me.transform.position;
				IEnumerator walk = PlayWalk(me, way, (g, w) => { got = g; where = w; });
				try
				{
					while (walk.MoveNext())
					{
						Vector3 now = me.transform.position;
						float s = Vector3.Dot(now - mouth, flat);
						if (s > 0.5f) ups.Add(now.y - floorAt(s));
						deepest = Mathf.Max(deepest, s);
						if ((now - prev).magnitude > jump) { jump = (now - prev).magnitude; jumpAt = s.ToString("0.0") + " m in"; }
						prev = now;
						yield return walk.Current;
					}
				}
				finally { playEntry = entry0; }
				ups.Sort();
				float usual = ups.Count > 0 ? ups[ups.Count / 2] : 0f, highest = ups.Count > 0 ? ups[ups.Count - 1] : 0f;
				Check(ref ok, got && deepest >= v[6] + 2f * size && highest - usual < 0.8f && jump < 1.2f, "size " + size + "x, walked in from before the mouth to " + deepest.ToString("0.0") + " m in (the den from " + v[6].ToString("0.0") + " m): " + (got ? "got there" : "stuck: " + where) + ", over the floor " + usual.ToString("0.00") + " m as a rule, at most " + highest.ToString("0.00") + " m, biggest move in a frame " + jump.ToString("0.00") + " m (" + jumpAt + ")");
				Vector3? back = CustomIslandSpawner.RaftPosition;
				if (back.HasValue) PlayerMove.To(me, back.Value + Vector3.up * 2.5f);
				yield return new WaitForSeconds(1f);
			}

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
				if (name == "terrain")
				{
					foreach (Terrain land in Terrain.activeTerrains)
					{
						Material tm = land.materialTemplate;
						Log("  terrain " + land.name + " at " + land.transform.position.ToString("F1") + " size " + land.terrainData.size.ToString("F1") + " material " + (tm != null ? tm.name + " shader " + tm.shader.name : "built-in") + " holes " + land.terrainData.holesResolution);
						foreach (TerrainLayer tl in land.terrainData.terrainLayers)
							if (tl != null) Log("    layer " + tl.name + " " + (tl.diffuseTexture != null ? tl.diffuseTexture.name : "-") + " tile " + tl.tileSize.ToString("F2") + " off " + tl.tileOffset.ToString("F2") + " remap " + tl.diffuseRemapMin.ToString("F2") + ".." + tl.diffuseRemapMax.ToString("F2") + " smooth " + tl.smoothness.ToString("0.00") + " spec " + tl.specular);
					}
					continue;
				}
				GameObject go = PlaceableCatalog.Get(name);
				if (go == null) { Log("  " + name + ": no prefab"); continue; }
				foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
					foreach (Material m in r.sharedMaterials)
					{
						if (m == null) continue;
						string cols = string.Join(" ", new[] { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_AlbedoColor", "_EmissionColor" }.Where(m.HasProperty).Select(c => c + "=" + ColorUtility.ToHtmlStringRGBA(m.GetColor(c))).ToArray());
						string texs = string.Join(" ", m.GetTexturePropertyNames().Where(t => m.GetTexture(t) != null).Select(t => t + "=" + m.GetTexture(t).name + " x" + m.GetTextureScale(t).ToString("F3") + "+" + m.GetTextureOffset(t).ToString("F2")).ToArray());
						string floats = string.Join(" ", m.shader != null ? Enumerable.Range(0, m.shader.GetPropertyCount()).Where(p => m.shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Float || m.shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Range || m.shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Vector)
							.Select(p => m.shader.GetPropertyName(p) + "=" + (m.shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Vector ? m.GetVector(m.shader.GetPropertyName(p)).ToString("F2") : m.GetFloat(m.shader.GetPropertyName(p)).ToString("0.###"))).ToArray() : new string[0]);
						Log("  " + name + " / " + r.name + " (" + r.GetType().Name + "): " + m.name + " shader " + m.shader.name + " | " + cols + " | " + texs + " | " + floats + " | kw " + string.Join(",", m.shaderKeywords));
					}
			}
			Log("PASS: materials probed");
		}
	}
}
