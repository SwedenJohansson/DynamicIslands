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
								// (its outside a child of its own, to be drawn as the hill: an orange panel round a light square from the sea,
								// Shelter Atoll 11-06)
								Transform po = pl.Find(CaveTool.PlugOutName);
								Mesh om = po != null && po.GetComponent<MeshFilter>() != null ? po.GetComponent<MeshFilter>().sharedMesh : null;
								int inT = m.triangles.Length, outT = om != null ? om.triangles.Length : 0;
								// (its shade bands cut along smooth lines: more triangles than the inside, one part per band)
								Check(ref ok, om != null && outT >= inT && inT > 0 && om.subMeshCount == CaveTool.PlugShadeOut.Length + 1,
									"a lone den's wall has its outside as a child of its own (" + outT + " to " + inT + " triangle corners, "
									+ (om != null ? om.subMeshCount : 0) + " parts, the shade bands' edges cut smooth)");
								// (a short rock tunnel in the way in, the hill's rock, seen from outside only: the den room read blue, 11-08 user)
								Transform pv = pl.Find(CaveTool.PlugVeilName);
								CaveTool.CaveVeil cv = pv != null ? pv.GetComponent<CaveTool.CaveVeil>() : null;
								MeshFilter sv = pv != null ? pv.GetComponentInChildren<MeshFilter>(true) : null;
								MeshRenderer svr = sv != null ? sv.GetComponent<MeshRenderer>() : null;
								Check(ref ok, cv != null && sv != null && sv.sharedMesh != null && sv.sharedMesh.vertexCount > 0 && svr != null
									&& sv.GetComponent<CaveTool.SleeveShade>() != null && pv.GetComponentInChildren<Collider>(true) == null
									&& svr.sharedMaterial != null && svr.sharedMaterial.shader == pl.GetComponent<MeshRenderer>().sharedMaterial.shader
									&& cv.OutFrom(pv.TransformPoint(0f, 0f, 400f)) > 1f,
									"a lone den's way in has a rock sleeve in the wall's own rock, seen from outside (" + (sv != null && sv.sharedMesh != null ? sv.sharedMesh.vertexCount : 0) + " corners, no collider)");
							// (the den's colour as drawn, not the big rock's grey-blue: a grey wall beside the lava tube's khaki shell, 11-05;
							// the textures' means were alike, the shaders' light was not - so both are compared lit, after TintWall)
							Color before, shell, wallLit;
							MeshRenderer wr = pl.GetComponent<MeshRenderer>();
							bool readBefore = CaveTool.LitColour(new Renderer[] { wr }, out before);
							Color factor = CaveTool.TintWall(probe, null);
							bool read = readBefore & CaveTool.LitColour(CaveTool.DenShell(probe), out shell) & CaveTool.LitColour(new Renderer[] { wr }, out wallLit);
							float off = Mathf.Max(Mathf.Abs(wallLit.r - shell.r), Mathf.Abs(wallLit.g - shell.g), Mathf.Abs(wallLit.b - shell.b));
							Check(ref ok, read && off < 0.08f, "a lone den's wall looks as the den: den " + shell.ToString("0.00") + ", wall " + before.ToString("0.00")
								+ " before, " + wallLit.ToString("0.00") + " after (factor " + factor.ToString("0.00") + ", off " + off.ToString("0.00") + ", under 0.08)");
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

		[ConsoleCommand(name: "CIDenWalls", docs: "Dev, in a world: the wall across each lone bear den's mouth (ar_cave, CaveTool.PlugOpening) on a loaded island - every face's normal set (zero ones draw flat black: the dark slabs round its mouth, 11-06), faces both into the den and out of its mouth, and lit as the den (CaveLit). CIDenWalls <island>")]
		public static void DenWallsCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args ?? new string[0]) });
			if (e == null) return;
			var walls = e.Root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name == CaveTool.PlugName && f.sharedMesh != null).ToList();
			if (walls.Count == 0) { Fail("no den wall (" + CaveTool.PlugName + ") on " + e.HostName); return; }
			int bad = 0;
			foreach (MeshFilter f in walls)
			{
				// (the wall's inside is its own mesh, its outside a child (CavePlugOut, drawn as the hill when there is one): both
				// sides counted over the two, each lit as the cave - 11-06 merge)
				Transform outT = f.transform.Find(CaveTool.PlugOutName);
				MeshFilter of = outT != null ? outT.GetComponent<MeshFilter>() : null;
				Vector3[] n = f.sharedMesh.normals.Concat(of != null && of.sharedMesh != null ? of.sharedMesh.normals : new Vector3[0]).ToArray();
				int zero = n.Count(v => v.sqrMagnitude < 0.25f), inn = n.Count(v => v.z < -0.3f), outn = n.Count(v => v.z > 0.3f); // (a rock face now, not flat: 11-06)
				Renderer r = f.GetComponent<Renderer>(), ro = of != null ? of.GetComponent<Renderer>() : null;
				bool lit = r != null && r.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.CustomProvided
					&& (ro == null || ro.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.CustomProvided);
				bool ok = n.Length > 0 && zero == 0 && inn > 0 && outn > 0 && lit;
				if (!ok) bad++;
				Log("  " + f.transform.parent.name + " at " + (f.transform.parent.position - e.Position).ToString("F1") + ": " + n.Length + " points, normals zero " + zero + ", into the den " + inn + ", out " + outn + ", lit as the den " + lit + (ok ? "" : " - BAD"));
			}
			if (bad > 0) Fail(bad + " of " + walls.Count + " den walls drawn wrong");
			else Log("PASS: den walls " + walls.Count + ", each face lit both ways");
		}

		[ConsoleCommand(name: "CIDenWallSlope", docs: "Dev, in a world: each lone den wall's outside drawn as the hill (CaveTool.WallLikeHill) on a loaded island faces the hill's slope round it, not straight out - the mean of its faces' normals within 25 degrees of the skin's mean normal behind it (turned at most CaveTool.WallTilt from straight out): a wall facing level out drew as a dark rock panel in the hill's sand, Gravewell Shaft 11-21 cv38. CIDenWallSlope <island>")]
		public static void DenWallSlopeCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args ?? new string[0]) });
			if (e == null) return;
			var skins = e.Root.GetComponentsInChildren<MeshCollider>(true).Where(c => c.name == CaveMountain.SkinName).ToList();
			var outs = e.Root.GetComponentsInChildren<CaveTool.HillWall>(true).Where(h => h.name == CaveTool.PlugOutName).ToList();
			if (outs.Count == 0) { Fail("no den wall outside drawn as the hill (" + CaveTool.PlugOutName + ") on " + e.HostName); return; }
			int bad = 0;
			foreach (CaveTool.HillWall h in outs)
			{
				string den = h.transform.parent != null && h.transform.parent.parent != null ? h.transform.parent.parent.name : h.name;
				MeshFilter mf = h.GetComponent<MeshFilter>();
				Mesh m = mf != null ? mf.sharedMesh : null;
				if (m == null || m.vertexCount == 0) { bad++; Log("  " + den + ": no mesh - BAD"); continue; }
				Vector3 outward = h.transform.parent.forward, wallN = Vector3.zero, skinN = Vector3.zero;
				Vector3[] v = m.vertices, n = m.normals;
				int level = 0;
				for (int i = 0; i < v.Length; i++)
				{
					Vector3 wn = h.transform.TransformDirection(n[i]).normalized;
					wallN += wn;
					if (wn.y < 0.2f) level++;
					Vector3 w = h.transform.TransformPoint(v[i]);
					RaycastHit hit; float best = float.MaxValue; Vector3 hn = Vector3.zero;
					foreach (MeshCollider c in skins)
						if (c.Raycast(new Ray(w + outward * 6f, -outward), out hit, 12f) && hit.distance < best && hit.normal.y > 0f) { best = hit.distance; hn = hit.normal; }
					skinN += hn;
				}
				if (skinN.sqrMagnitude < 1e-6f) { Log("  " + den + ": no skin behind it - skipped"); continue; }
				Vector3 want = Vector3.RotateTowards(outward, skinN.normalized, CaveTool.WallTilt * Mathf.Deg2Rad, 0f);
				float off = Vector3.Angle(wallN, want);
				bool ok = off < 25f;
				if (!ok) bad++;
				Log("  " + den + ": " + v.Length + " points, faces " + Vector3.Angle(wallN, outward).ToString("0") + " deg up from straight out, the skin's slope "
					+ Vector3.Angle(skinN, outward).ToString("0") + " deg, off it by " + off.ToString("0") + ", facing level " + (100f * level / v.Length).ToString("0") + "%" + (ok ? "" : " - BAD"));
			}
			if (bad > 0) Fail(bad + " of " + outs.Count + " den walls facing away from the hill's slope");
			else Log("PASS: den walls " + outs.Count + " facing the hill's slope");
		}

		[ConsoleCommand(name: "CIFrameSunk", docs: "Dev, in a world: tunnel mouth frames drawn as the hill (CaveTool.FramesLikeHill) on a loaded island - no frame point left under the ground within the lift band (CaveTool.FrameLiftable: 0-FrameSunk across the ground's slope): the cut ground poked through Sinkmarket Floor's mouth frame as walls, 11-21 cv29. Logs how many points lie deeper on purpose. CIFrameSunk <island>")]
		public static void FrameSunkCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args ?? new string[0]) });
			if (e == null) return;
			var frames = e.Root.GetComponentsInChildren<CaveTool.HillWall>(true).Where(h => h.name == CaveTool.MouthName).ToList();
			if (frames.Count == 0) { Fail("no mouth frame drawn as the hill (" + CaveTool.MouthName + ") on " + e.HostName); return; }
			int left = 0, deeper = 0, all = 0, tucked = 0;
			foreach (CaveTool.HillWall h in frames)
			{
				MeshFilter mf = h.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null) continue;
				int mine = 0;
				// (11-21 cv49: the points of faces turned up only, as FramesLikeHill - a point on the sill's underside never shows)
				Vector3[] ws = mf.sharedMesh.vertices.Select(p => h.transform.TransformPoint(p)).ToArray();
				bool[] up = CaveTool.FrameUpPoints(ws, mf.sharedMesh.triangles);
				// (11-21 cv52: and the rim laid under the ground on purpose, CaveTool.TuckRim)
				bool[] rim = CaveTool.FrameRimPoints(h, ws.Length);
				for (int v = 0; v < ws.Length; v++)
				{
					if (!up[v]) continue;
					Vector3 w = ws[v]; float g; Vector3 gn;
					all++;
					if (rim[v]) { tucked++; continue; }
					if (CaveTool.FrameLiftable(w, out g, out gn)) { left++; mine++; }
					else if (w.y < g - 0.01f) deeper++;
				}
				if (mine > 0) Log("  " + (h.transform.parent != null ? h.transform.parent.name : h.name) + " at " + (h.transform.position - e.Position).ToString("F1") + ": " + mine + " point(s) still just under the ground - BAD");
			}
			Log("  " + frames.Count + " frame(s), " + all + " points on faces turned up, " + left + " just under the ground, " + deeper + " deeper (under the hill on purpose), " + tucked + " rim point(s) laid under it (TuckRim)");
			if (left > 0) Fail(left + " frame point(s) left just under the ground: it shows through the frame");
			else Log("PASS: mouth frames " + frames.Count + ", none just under the ground");
		}

		[ConsoleCommand(name: "CIFrameRim", docs: "Dev, in a world: tunnel mouth frames drawn as the hill (CaveTool.FramesLikeHill) on a loaded island - no long stretch of a frame face turned aside standing a strip of at most CaveTool.FrameRimShow out of the ground while it goes on down into it (CaveTool.TuckRim lays the rim under the ground): Overpass Shoal's mouth apron stood 3 cm over the channel floor, its side wall a row of black dashes, 11-21 cv50. FAIL at 0.5 m of such strips on one frame. Logs the rim points laid under the ground and the faces turned up lying within 5 cm over it. CIFrameRim <island>")]
		public static void FrameRimCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args ?? new string[0]) });
			if (e == null) return;
			var frames = e.Root.GetComponentsInChildren<CaveTool.HillWall>(true).Where(h => h.name == CaveTool.MouthName).ToList();
			if (frames.Count == 0) { Fail("no mouth frame drawn as the hill (" + CaveTool.MouthName + ") on " + e.HostName); return; }
			int badFrames = 0, tuckedAll = 0, flatAll = 0; float worstLen = 0f;
			foreach (CaveTool.HillWall h in frames)
			{
				MeshFilter mf = h.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
				Vector3[] w = mf.sharedMesh.vertices.Select(p => h.transform.TransformPoint(p)).ToArray();
				int[] st = mf.sharedMesh.triangles;
				int tucked = CaveTool.FrameRimPoints(h, w.Length).Count(x => x);
				float len = 0f; int strips = 0, flat = 0;
				var worst = new List<KeyValuePair<float, string>>();
				for (int t = 0; t + 2 < st.Length; t += 3)
				{
					Vector3 a = w[st[t]], b = w[st[t + 1]], c = w[st[t + 2]];
					float ga, gb, gc;
					if (!CaveTool.SolidGroundAt(a, out ga) || !CaveTool.SolidGroundAt(b, out gb) || !CaveTool.SolidGroundAt(c, out gc)) continue;
					float ha = a.y - ga, hb = b.y - gb, hc = c.y - gc;
					float top = Mathf.Max(ha, Mathf.Max(hb, hc)), low = Mathf.Min(ha, Mathf.Min(hb, hc));
					if (!CaveTool.FrameAside(a, b, c))
					{
						// (a face turned up lying within 5 cm over the ground all over: the ground may fight through it - for the log)
						Vector3 n = Vector3.Cross(b - a, c - a);
						if (n.sqrMagnitude > 1e-10f && n.normalized.y >= CaveTool.FrameRimCross && low >= 0f && top < 0.05f) flat++;
						continue;
					}
					// (a strip out of the ground at most FrameRimShow high, the face going on down into the ground)
					if (top <= 0.005f || top > CaveTool.FrameRimShow || low > -CaveTool.FrameRimIn) continue;
					// (how long it runs: across the face's points standing out of the ground, or half the face's width where one does)
					var outs = new List<Vector3>();
					if (ha > -CaveTool.FrameRimIn) outs.Add(a);
					if (hb > -CaveTool.FrameRimIn) outs.Add(b);
					if (hc > -CaveTool.FrameRimIn) outs.Add(c);
					float l = 0f;
					for (int i = 0; i < outs.Count; i++) for (int j = i + 1; j < outs.Count; j++) l = Mathf.Max(l, Vector2.Distance(new Vector2(outs[i].x, outs[i].z), new Vector2(outs[j].x, outs[j].z)));
					if (l < 0.01f) l = Mathf.Max(Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)), Mathf.Max(Vector2.Distance(new Vector2(b.x, b.z), new Vector2(c.x, c.z)), Vector2.Distance(new Vector2(a.x, a.z), new Vector2(c.x, c.z)))) * 0.5f;
					len += l; strips++;
					Vector3 m = (a + b + c) / 3f;
					worst.Add(new KeyValuePair<float, string>(l, "strip " + (top * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " cm out of the ground, " + l.ToString("0.00", CultureInfo.InvariantCulture) + " m long, at " + (m - e.Position).ToString("F2")));
				}
				string where = (h.transform.parent != null ? h.transform.parent.name : h.name) + " at " + (h.transform.position - e.Position).ToString("F1");
				Log("  " + where + ": " + strips + " face(s) turned aside standing a strip out of the ground, " + len.ToString("0.00", CultureInfo.InvariantCulture) + " m in all; " + tucked + " rim point(s) laid under the ground; " + flat + " face(s) turned up lying within 5 cm over it");
				bool bad = len >= FrameRimLong;
				foreach (var kv in worst.OrderByDescending(k => k.Key).Take(bad ? 8 : 3)) Log("    " + kv.Value + (bad ? " - BAD" : ""));
				if (bad) badFrames++;
				worstLen = Mathf.Max(worstLen, len); tuckedAll += tucked; flatAll += flat;
			}
			Log("  " + frames.Count + " frame(s), most strip out of the ground on one " + worstLen.ToString("0.00", CultureInfo.InvariantCulture) + " m, " + tuckedAll + " rim point(s) laid under the ground, " + flatAll + " face(s) turned up within 5 cm over it");
			if (badFrames > 0) Fail(badFrames + " mouth frame(s) with " + FrameRimLong.ToString("0.0", CultureInfo.InvariantCulture) + " m or more of side faces standing a strip of at most " + (CaveTool.FrameRimShow * 100f).ToString("0", CultureInfo.InvariantCulture) + " cm out of the ground (dashes along the frame's edge)");
			else Log("PASS: mouth frames " + frames.Count + ", no long strip of a side face standing just out of the ground");
		}

		/// <summary>CIFrameRim: this much (m) of side-face strips just out of the ground on one frame fails.</summary>
		const float FrameRimLong = 0.5f;

		[ConsoleCommand(name: "CIFrameRing", docs: "Dev, in a world, after a CIPlay on the island: rays over the last play picture (64x36) at the tunnel mouth frames drawn as the hill (CaveTool.FramesLikeHill) - none may meet steep island ground (CaveTool.FrameCoverSteep) standing within CaveTool.FrameCover in front of a frame face, along that face's normal: Sinkmarket Floor's cutting walls stood 0.4-0.65 m in front of its frame's inner faces as pale walls inside the ring while CIFrameSunk passed, 11-21 cv40 - and none may meet island ground lying along a frame face (normals within 45 degrees) within 0.05 m of it, in front or behind: the heightmap's triangles 1 cm behind Sinkmarket's mouth side walls poked through as a dark sawtooth, 11-21 cv45. FAIL at 3 rays of either. CIFrameRing <island>")]
		public static void FrameRingCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args ?? new string[0]) });
			if (e == null) return;
			StartTest(FrameRingRoutine(e));
		}

		/// <summary>CIFrameRing: ground lying along a frame face within this (m, across it) fights through it.</summary>
		const float FightNear = 0.05f;

		static IEnumerator FrameRingRoutine(IslandWorldState.Entry e)
		{
			var frames = e.Root.GetComponentsInChildren<CaveTool.HillWall>(true).Where(h => h.name == CaveTool.MouthName).ToList();
			if (frames.Count == 0) { Fail("no mouth frame drawn as the hill (" + CaveTool.MouthName + ") on " + e.HostName); yield break; }
			string pi = LastPictureIsland ?? "";
			bool ours = pi.Length > 0 && ((e.Name ?? "").IndexOf(pi, System.StringComparison.OrdinalIgnoreCase) >= 0 || (e.HostName ?? "").IndexOf(pi, System.StringComparison.OrdinalIgnoreCase) >= 0);
			if (!LastPictureFrom.HasValue || !ours) { Fail("frame ring: no play picture of " + e.HostName + " (run CIPlay on it first)"); yield break; }
			Vector3 from = e.Position + e.Root.transform.rotation * LastPictureFrom.Value, to = e.Position + e.Root.transform.rotation * LastPictureLook.Value;
			Quaternion look = Quaternion.LookRotation(to - from);
			float half = Mathf.Tan((Camera.main != null ? Camera.main.fieldOfView : 65f) * 0.5f * Mathf.Deg2Rad);
			// (the frames' drawn faces, as world triangles: the collider keeps the frame's mesh before it was laid on the ground)
			var ta = new List<Vector3>(); var te1 = new List<Vector3>(); var te2 = new List<Vector3>(); var tn = new List<Vector3>();
			var trim = new List<bool>(); // (11-21 cv52: a face with a rim point laid under the ground - it goes into the ground there on purpose)
			var pieces = new List<Transform>();
			foreach (CaveTool.HillWall h in frames)
			{
				MeshFilter mf = h.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
				if (h.transform.parent != null) pieces.Add(h.transform.parent);
				Vector3[] v = mf.sharedMesh.vertices; int[] t = mf.sharedMesh.triangles;
				bool[] rim = CaveTool.FrameRimPoints(h, v.Length);
				for (int i = 0; i + 2 < t.Length; i += 3)
				{
					Vector3 a = h.transform.TransformPoint(v[t[i]]), b = h.transform.TransformPoint(v[t[i + 1]]), c = h.transform.TransformPoint(v[t[i + 2]]);
					Vector3 n = Vector3.Cross(b - a, c - a);
					if (n.sqrMagnitude < 1e-12f) continue;
					ta.Add(a); te1.Add(b - a); te2.Add(c - a); tn.Add(n.normalized);
					trim.Add(rim[t[i]] || rim[t[i + 1]] || rim[t[i + 2]]);
				}
			}
			const int GX = 64, GY = 36;
			int metFrame = 0, bad = 0, deeper = 0, fight = 0, rimFight = 0;
			var worst = new List<KeyValuePair<float, string>>(); var fights = new List<KeyValuePair<float, string>>();
			for (int y = 0; y < GY; y++)
			{
				for (int x = 0; x < GX; x++)
				{
					float u = (x + 0.5f) / GX, vv = (y + 0.5f) / GY;
					Vector3 dir = (look * new Vector3((2f * u - 1f) * half * 16f / 9f, (1f - 2f * vv) * half, 1f)).normalized;
					float tF = 60f; int fi = -1;
					for (int i = 0; i < ta.Count; i++)
					{
						if (Vector3.Dot(tn[i], dir) >= 0f) continue; // (seen from behind: culled, as drawn)
						Vector3 p = Vector3.Cross(dir, te2[i]);
						float det = Vector3.Dot(te1[i], p);
						if (Mathf.Abs(det) < 1e-9f) continue;
						float inv = 1f / det; Vector3 s = from - ta[i];
						float bu = Vector3.Dot(s, p) * inv;
						if (bu < 0f || bu > 1f) continue;
						Vector3 q = Vector3.Cross(s, te1[i]);
						float bv = Vector3.Dot(dir, q) * inv;
						if (bv < 0f || bu + bv > 1f) continue;
						float d = Vector3.Dot(te2[i], q) * inv;
						if (d > 0.05f && d < tF) { tF = d; fi = i; }
					}
					if (fi < 0) continue;
					metFrame++;
					float cosF = Mathf.Abs(Vector3.Dot(dir, tn[fi]));
					float past = Mathf.Min(FightNear / Mathf.Max(cosF, 0.2f) + 0.01f, 0.5f);
					RaycastHit[] hits = Physics.RaycastAll(new Ray(from, dir), tF + past, ~0, QueryTriggerInteraction.Ignore).OrderBy(hh => hh.distance).ToArray();
					RaycastHit? land = null;
					foreach (RaycastHit hh in hits)
					{
						if (hh.collider is TerrainCollider) { land = hh; break; }
						if (hh.collider.GetComponentInParent<Network_Player>() != null || pieces.Any(pc => hh.collider.transform.IsChildOf(pc))) continue;
						break; // (something else in front: the ground there isn't what is seen)
					}
					// (11-21 cv45: and the ground lying along the face within FightNear of it, in front or just behind - Sinkmarket's
					// cutting walls 1 cm behind its mouth's side walls, the heightmap's triangles poking through them as a dark sawtooth;
					// not ground crossing the face at a slant, where a frame goes into the hill)
					if (land.HasValue && Mathf.Abs(land.Value.distance - tF) * cosF < FightNear && Vector3.Dot(land.Value.normal, tn[fi]) > 0.7f)
					{
						if (trim[fi]) { rimFight++; continue; }
						fight++;
						fights.Add(new KeyValuePair<float, string>(-Mathf.Abs(land.Value.distance - tF) * cosF, "at " + u.ToString("0.00", CultureInfo.InvariantCulture) + "," + vv.ToString("0.00", CultureInfo.InvariantCulture) + " of the picture: ground at "
							+ (land.Value.point - e.Position).ToString("F2") + " (normal " + land.Value.normal.ToString("F2") + ") " + ((land.Value.distance - tF) * cosF).ToString("0.000", CultureInfo.InvariantCulture) + " m behind the frame's face (normal " + tn[fi].ToString("F2") + ")"));
						continue;
					}
					if (!land.HasValue || land.Value.distance > tF - 0.02f || land.Value.normal.y >= CaveTool.FrameCoverSteep) continue;
					float across = (tF - land.Value.distance) * cosF;
					if (across > CaveTool.FrameCover) { deeper++; continue; }
					bad++;
					worst.Add(new KeyValuePair<float, string>(across, "at " + u.ToString("0.00", CultureInfo.InvariantCulture) + "," + vv.ToString("0.00", CultureInfo.InvariantCulture) + " of the picture: ground at "
						+ (land.Value.point - e.Position).ToString("F2") + " (normal " + land.Value.normal.ToString("F2") + ") " + across.ToString("0.00", CultureInfo.InvariantCulture) + " m in front of the frame's face"));
				}
				yield return null;
			}
			foreach (var kv in worst.OrderByDescending(k => k.Key).Take(10)) Log("  " + kv.Value + " - BAD");
			foreach (var kv in fights.OrderByDescending(k => k.Key).Take(10)) Log("  " + kv.Value + " - FIGHT");
			Log("  " + frames.Count + " frame(s), " + ta.Count + " faces, " + metFrame + " of " + (GX * GY) + " rays meet a frame face, " + bad + " see steep ground just in front of it, " + fight + " ground within " + FightNear.ToString("0.00", CultureInfo.InvariantCulture) + " m of it along it (and " + rimFight + " where its rim goes into the ground, TuckRim), " + deeper + " ground further in front (frame under the hill); " + CaveTool.GroundSunkUnderFrames + " ground point(s) let down under frame faces since start");
			// (11-21 cv47: and every renderer of the frames' pieces in the hill's rock with its main normal map flat and its rock layer
			// on faces turned up: Balboa's baked normal on our uvs drew black triangles all over Sinkmarket Floor's frame, and its
			// sand layer a golden band in its mouth)
			int looks = 0, lookBad = 0;
			foreach (Transform pc in pieces.Distinct())
				foreach (MeshRenderer r in pc.GetComponentsInChildren<MeshRenderer>(false))
				{
					if (r.GetComponent<CaveTool.HillWall>() == null) continue;
					Material[] mats = r.sharedMaterials;
					for (int i = 0; i < mats.Length; i++)
					{
						Material m = mats[i];
						if (m == null || !m.HasProperty(CaveTool.MainNormal)) continue;
						looks++;
						var b = new MaterialPropertyBlock();
						r.GetPropertyBlock(b, i);
						Texture n = b.isEmpty ? null : b.GetTexture(CaveTool.MainNormal);
						if (n == null) { r.GetPropertyBlock(b); n = b.isEmpty ? null : b.GetTexture(CaveTool.MainNormal); r.GetPropertyBlock(b, i); }
						Texture top = b.isEmpty || !m.HasProperty("_GrassDiffuse") ? null : b.GetTexture("_GrassDiffuse");
						Texture side = b.isEmpty || !m.HasProperty("_RockDiffuse") ? null : b.GetTexture("_RockDiffuse");
						if (side == null && m.HasProperty("_RockDiffuse")) side = m.GetTexture("_RockDiffuse");
						bool flat = n == Texture2D.normalTexture, rock = !m.HasProperty("_GrassDiffuse") || top != null && top == side;
						if (flat && rock) continue;
						lookBad++;
						Log("  " + (r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name + " part " + i + ": " + (flat ? "" : "main normal map not flat (" + (n != null ? n.name : m.GetTexture(CaveTool.MainNormal) != null ? m.GetTexture(CaveTool.MainNormal).name : "none") + ") ") + (rock ? "" : "sand layer on faces turned up") + " - BAD");
					}
				}
			Log("  " + looks + " part(s) of the frames' pieces in the hill's rock, " + lookBad + " not flat or not rock all over");
			if (lookBad > 0) Fail(lookBad + " part(s) of a mouth frame's piece in the hill's rock with Balboa's baked normal map or its sand layer (black or golden patches)");
			if (bad >= 3 || fight >= 3) Fail((bad >= 3 ? bad + " rays see steep ground standing just in front of a mouth frame's face (inside the ring)" : "") + (bad >= 3 && fight >= 3 ? "; " : "") + (fight >= 3 ? fight + " rays meet the ground within " + FightNear.ToString("0.00", CultureInfo.InvariantCulture) + " m of a mouth frame's face, lying along it (it fights through the frame)" : ""));
			else if (lookBad == 0) Log("PASS: mouth frames " + frames.Count + ", no ground standing inside the ring (" + bad + " ray(s)), their pieces' rock flat and rock all over");
		}

		[ConsoleCommand(name: "CIDenWallLook", docs: "Dev, in a world: each lone den wall's outside drawn as the hill (CaveTool.WallLikeHill) on a loaded island, pictured from 8 m or more out in front of the den (shot_denlook_<n>.png): the pixels it draws (seen changing when it is hidden) against the hill's skin round them (rays meet the skin first, within 8 px) - mean brightness within 14 (of 255) and at most 30% of its pixels darker than the skin's darkest tenth: a dark panel with two spikes round Gravewell Shaft's den mouth while CIDenWalls and CIDenWallSlope passed, 11-21 cv40. CIDenWallLook <island>")]
		public static void DenWallLookCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args ?? new string[0]) });
			if (e == null) return;
			StartTest(DenWallLookRoutine(e));
		}

		static int denLookN;
		/// <summary>CIDenWallLook: a den wall's grain (its brightness slopes' direction-and-strength, 0..2) may differ from the hill's round it by this much.</summary>
		const float DenGrainOff = 0.25f;
		/// <summary>CIDenWallLook: a brightness step (of 255) across a column or row that counts toward a straight seam.</summary>
		const float DenSeamStep = 12f;
		/// <summary>CIDenWallLook: a straight seam this long (px of 360) or a fifth of the wall's box on the picture is BAD.</summary>
		const int DenSeamRun = 20;

		static IEnumerator DenWallLookRoutine(IslandWorldState.Entry e)
		{
			var outs = e.Root.GetComponentsInChildren<CaveTool.HillWall>(true).Where(h => h.name == CaveTool.PlugOutName).ToList();
			if (outs.Count == 0) { Fail("no den wall outside drawn as the hill (" + CaveTool.PlugOutName + ") on " + e.HostName); yield break; }
			Camera cam = Camera.main;
			if (cam == null) { Fail("den wall look: no camera"); yield break; }
			const int W = 640, H = 360, R = 8;
			int bad = 0, looked = 0;
			foreach (CaveTool.HillWall h in outs)
			{
				MeshRenderer pr = h.GetComponent<MeshRenderer>();
				Transform plug = h.transform.parent;
				if (pr == null || plug == null) continue;
				string den = plug.parent != null ? plug.parent.name : plug.name;
				Bounds b = pr.bounds;
				Vector3 from = b.center + plug.forward * Mathf.Max(b.extents.magnitude * 2.2f, 8f) + Vector3.up * 1.5f;
				Quaternion rot = Quaternion.LookRotation(b.center - from);
				float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
				yield return new WaitForEndOfFrame();
				denLookN++;
				Vector3 oldPos = cam.transform.position; Quaternion oldRot = cam.transform.rotation; RenderTexture oldRt = cam.targetTexture;
				var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
				var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
				Color32[] with, with2, without;
				try
				{
					cam.transform.position = from; cam.transform.rotation = rot; cam.targetTexture = rt;
					System.Func<Color32[]> grab = () =>
					{
						cam.Render();
						RenderTexture was = RenderTexture.active; RenderTexture.active = rt;
						tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
						RenderTexture.active = was;
						return tex.GetPixels32();
					};
					// (11-21 cv44: a first picture to settle the camera's effects, then two to know what changes on its own: the mask
					// took nearly the whole picture, so only 3 rays round it were left to find the skin and the look was BAD unchecked)
					grab();
					with = grab();
					with2 = grab();
					SaveRt(rt, Path.Combine(DynamicIslands.assetpath, "shot_denlook_" + denLookN + ".png"));
					pr.forceRenderingOff = true;
					try { without = grab(); } finally { pr.forceRenderingOff = false; }
				}
				finally
				{
					cam.transform.position = oldPos; cam.transform.rotation = oldRot; cam.targetTexture = oldRt;
					rt.Release(); Object.Destroy(rt); Object.Destroy(tex);
				}
				System.Func<Color32, float> luma = c => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
				// (only within the wall's own box on the picture, and not where the picture changes by itself)
				int bx0 = W, by0 = H, bx1 = -1, by1 = -1;
				Matrix4x4 toCam = Matrix4x4.TRS(from, rot, Vector3.one).inverse;
				for (int ci = 0; ci < 8; ci++)
				{
					Vector3 cw = new Vector3((ci & 1) == 0 ? b.min.x : b.max.x, (ci & 2) == 0 ? b.min.y : b.max.y, (ci & 4) == 0 ? b.min.z : b.max.z);
					Vector3 cc = toCam.MultiplyPoint3x4(cw);
					if (cc.z < 0.1f) { bx0 = 0; by0 = 0; bx1 = W - 1; by1 = H - 1; break; }
					int px = Mathf.RoundToInt((cc.x / cc.z / (half * W / H) + 1f) * 0.5f * W), py = Mathf.RoundToInt((cc.y / cc.z / half + 1f) * 0.5f * H);
					bx0 = Mathf.Min(bx0, px); bx1 = Mathf.Max(bx1, px); by0 = Mathf.Min(by0, py); by1 = Mathf.Max(by1, py);
				}
				var mask = new bool[W * H];
				int noisy = 0;
				for (int i = 0; i < mask.Length; i++)
				{
					int x = i % W, y = i / W;
					if (x < bx0 || x > bx1 || y < by0 || y > by1) continue;
					if (Mathf.Abs(with[i].r - with2[i].r) + Mathf.Abs(with[i].g - with2[i].g) + Mathf.Abs(with[i].b - with2[i].b) > 12) { noisy++; continue; }
					mask[i] = Mathf.Abs(with[i].r - without[i].r) + Mathf.Abs(with[i].g - without[i].g) + Mathf.Abs(with[i].b - without[i].b) > 24;
				}
				// (within R px of the wall's pixels: a box dilation, rows then columns)
				var rowNear = new bool[W * H]; var near = new bool[W * H];
				for (int y = 0; y < H; y++)
				{
					int last = -100000;
					for (int x = 0; x < W; x++) { if (mask[y * W + x]) last = x; if (x - last <= R) rowNear[y * W + x] = true; }
					last = 100000;
					for (int x = W - 1; x >= 0; x--) { if (mask[y * W + x]) last = x; if (last - x <= R) rowNear[y * W + x] = true; }
				}
				for (int x = 0; x < W; x++)
				{
					int last = -100000;
					for (int y = 0; y < H; y++) { if (rowNear[y * W + x]) last = y; if (y - last <= R) near[y * W + x] = true; }
					last = 100000;
					for (int y = H - 1; y >= 0; y--) { if (rowNear[y * W + x]) last = y; if (last - y <= R) near[y * W + x] = true; }
				}
				var wall = new List<float>(); var skin = new List<float>(); var metFirst = new Dictionary<string, int>();
				var cls = new byte[W * H]; // (1 the wall, 2 the skin, 0 anything else)
				// (nor the den's own rock: cv43 still found 4 px of skin round Gravewell's bear den, the picture all skin there -
				// the den's colliders lie just over the skin the hill drapes on them)
				Transform denT = plug.parent != null ? plug.parent : plug;
				for (int y = 0; y < H; y++)
					for (int x = 0; x < W; x++)
					{
						int i = y * W + x;
						if (mask[i]) { wall.Add(luma(with[i])); cls[i] = 1; continue; }
						if (!near[i] || ((x + y) & 1) != 0) continue;
						Vector3 dir = rot * new Vector3((2f * (x + 0.5f) / W - 1f) * half * W / H, (2f * (y + 0.5f) / H - 1f) * half, 1f);
						// (the first thing met that is not the den's own plug: cv42 met the plug's colliders, still where the wall stood
						// before WallLikeHill set it back under the skin, so 8 px of skin were found and the look was skipped as a PASS)
						RaycastHit[] hits = Physics.RaycastAll(from, dir, 80f, ~0, QueryTriggerInteraction.Ignore);
						float nearest = float.MaxValue; Collider first = null;
						foreach (RaycastHit hh in hits)
							if (hh.distance < nearest && !hh.collider.transform.IsChildOf(denT)) { nearest = hh.distance; first = hh.collider; }
						if (first != null && first.name == CaveMountain.SkinName) { skin.Add(luma(with[i])); cls[i] = 2; }
						string fk = first == null ? "(sky)" : first.name;
						metFirst[fk] = metFirst.TryGetValue(fk, out int fc) ? fc + 1 : 1;
					}
				looked++;
				Log("  " + den + ": its box on the picture " + bx0 + ".." + bx1 + " x " + by0 + ".." + by1 + ", " + noisy + " px changing by themselves left out");
				if (wall.Count < 30) { Log("  " + den + ": its outside not seen past the hill (" + wall.Count + " px)"); continue; }
				if (skin.Count < 30) { bad++; Log("  " + den + ": too little of the hill's skin round it seen (" + skin.Count + " px) - not checked, BAD; met first: " + string.Join(", ", metFirst.OrderByDescending(kv => kv.Value).Take(4).Select(kv => kv.Key + " " + kv.Value).ToArray())); continue; }
				skin.Sort();
				float p10 = skin[skin.Count / 10], mw = wall.Average(), ms = skin.Average();
				float dark = (float)wall.Count(l => l < p10) / wall.Count;
				// (11-21 cv45: and its texture's grain and straight seams - Gravewell Shaft's panel passed on brightness, 26 against 22,
				// but still read as a lighter rectangle of stretched ripples running another way, straight seams left and right of
				// its arch. The skipped pixels of the skin's checkerboard take their left neighbour's kind)
				for (int y = 0; y < H; y++)
					for (int x = 1; x < W; x++) { int i = y * W + x; if (cls[i] == 0 && near[i] && !mask[i] && ((x + y) & 1) != 0) cls[i] = cls[i - 1]; }
				System.Func<int, int, float> L = (x, y) => luma(with[y * W + x]);
				// (the grain: each side's structure tensor of the brightness slopes, as a direction-and-strength vector at twice the angle)
				double wxx = 0, wyy = 0, wxy = 0, sxx = 0, syy = 0, sxy = 0, wg = 0, sg = 0; int wn = 0, sn = 0;
				for (int y = 1; y < H - 1; y++)
					for (int x = 1; x < W - 1; x++)
					{
						int i = y * W + x;
						byte k = cls[i];
						if (k == 0 || cls[i - 1] != k || cls[i + 1] != k || cls[i - W] != k || cls[i + W] != k) continue;
						double gx = L(x + 1, y) - L(x - 1, y), gy = L(x, y + 1) - L(x, y - 1);
						if (k == 1) { wxx += gx * gx; wyy += gy * gy; wxy += gx * gy; wg += System.Math.Sqrt(gx * gx + gy * gy); wn++; }
						else { sxx += gx * gx; syy += gy * gy; sxy += gx * gy; sg += System.Math.Sqrt(gx * gx + gy * gy); sn++; }
					}
				float grainOff = 0f, wAng = 0f, sAng = 0f, wCoh = 0f, sCoh = 0f, slope = 1f;
				bool grainSeen = wn >= 30 && sn >= 30 && wxx + wyy > 1e-3 && sxx + syy > 1e-3;
				if (grainSeen)
				{
					double wtr = wxx + wyy, str = sxx + syy;
					Vector2 wv = new Vector2((float)((wxx - wyy) / wtr), (float)(2 * wxy / wtr)), sv = new Vector2((float)((sxx - syy) / str), (float)(2 * sxy / str));
					grainOff = (wv - sv).magnitude;
					wCoh = wv.magnitude; sCoh = sv.magnitude;
					wAng = 0.5f * Mathf.Atan2(wv.y, wv.x) * Mathf.Rad2Deg; sAng = 0.5f * Mathf.Atan2(sv.y, sv.x) * Mathf.Rad2Deg;
					slope = (float)((wg / wn) / System.Math.Max(sg / sn, 1e-3));
				}
				// (straight seams: the longest run, down a column or along a row (a pixel either way), of the same brightness step
				// across it over DenSeamStep, where the pixels across are the wall's or the skin's, at least one the wall's)
				System.Func<bool, int> seamRun = vertical =>
				{
					int longest = 0;
					int outerN = vertical ? W : H, innerN = vertical ? H : W;
					var prev = new int[outerN]; var cur = new int[outerN];
					for (int a = 0; a < innerN; a++)
					{
						for (int o = 2; o < outerN - 2; o++)
						{
							cur[o] = 0;
							int x = vertical ? o : a, y = vertical ? a : o;
							bool anyWall = false, okc = true;
							for (int d = -2; d <= 2 && okc; d++)
							{
								byte k = cls[vertical ? y * W + x + d : (y + d) * W + x];
								if (k == 0) okc = false; else if (k == 1) anyWall = true;
							}
							if (!okc || !anyWall) continue;
							float step = vertical ? (L(x + 1, y) + L(x + 2, y) - L(x - 1, y) - L(x - 2, y)) * 0.5f : (L(x, y + 1) + L(x, y + 2) - L(x, y - 1) - L(x, y - 2)) * 0.5f;
							if (Mathf.Abs(step) < DenSeamStep) continue;
							int sgn = step > 0 ? 1 : -1, run = 0;
							for (int q = o - 1; q <= o + 1; q++) if (prev[q] * sgn > run) run = prev[q] * sgn;
							cur[o] = sgn * (run + 1);
							if (run + 1 > longest) longest = run + 1;
						}
						var sw = prev; prev = cur; cur = sw;
					}
					return longest;
				};
				int vSeam = seamRun(true), hSeam = seamRun(false);
				int seamMost = Mathf.Max(DenSeamRun, Mathf.RoundToInt(0.2f * Mathf.Min(by1 - by0, bx1 - bx0)));
				bool grainOk = !grainSeen || (grainOff <= DenGrainOff && slope >= 0.65f && slope <= 1.5f);
				bool seamOk = vSeam < seamMost && hSeam < seamMost;
				bool ok = Mathf.Abs(mw - ms) <= 14f && dark <= 0.3f && grainOk && seamOk;
				if (!ok) bad++;
				Log("  " + den + " (shot_denlook_" + denLookN + "): its outside " + wall.Count + " px, brightness " + mw.ToString("0") + " against the hill's " + ms.ToString("0") + " (" + skin.Count + " px round it), "
					+ (100f * dark).ToString("0") + "% darker than the hill's darkest tenth; grain "
					+ (grainSeen ? wAng.ToString("0") + " deg x" + wCoh.ToString("0.00") + " against the hill's " + sAng.ToString("0") + " deg x" + sCoh.ToString("0.00") + ", off " + grainOff.ToString("0.00") + " (most " + DenGrainOff.ToString("0.00") + "), slopes " + slope.ToString("0.00") + " of the hill's" + (grainOk ? "" : " - BAD") : "not seen (" + wn + "/" + sn + " px)")
					+ "; straight seams " + vSeam + " px down, " + hSeam + " px across (most " + seamMost + ")" + (seamOk ? "" : " - BAD")
					+ "; outside faces mapped by world axis since start: " + CaveTool.WallFacesPlain + (ok ? "" : " - BAD"));
			}
			if (looked == 0) Fail("den wall look: no den wall pictured");
			else if (bad > 0) Fail(bad + " of " + looked + " den walls drawn darker than the hill round them");
			else Log("PASS: den walls " + looked + " drawn as the hill round them");
		}

		[ConsoleCommand(name: "CILootGround", docs: "Dev, in a world: the loot pieces (Pickup_Landmark_*, as caveloot lays them) on a loaded island's cave floors (not Raft's own sea-floor pickups) stand on the floor - the floor under each one's corners no more than <gap> m (0.12) under its bottom: on a sloping floor the low side of a piece set by its middle stood in the air, Rootcellar Deep's ramp stone 11-21 cv38. CILootGround [gap=<m>] <island>")]
		public static void LootGroundCommand(string[] args)
		{
			float gap = 0.12f;
			var rest = new List<string>();
			foreach (string a in args ?? new string[0])
				if (a.StartsWith("gap=")) float.TryParse(a.Substring(4), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out gap); else rest.Add(a);
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", rest.ToArray()) });
			if (e == null) return;
			// (only the pieces caveloot lays: cv42 counted Raft's own sea vines, Pickup_Landmark_SeavineKlump, under the hill's skin)
			var kit = CaveKitObjects.Where(k => k.StartsWith("Pickup_Landmark_")).ToList();
			var loot = e.Root.GetComponentsInChildren<Transform>(true).Where(t => kit.Any(k => t.name.StartsWith(k, System.StringComparison.Ordinal)) && t.parent != null && !t.parent.name.StartsWith("Pickup_Landmark_")).ToList();
			if (loot.Count == 0) { Fail("no loot piece (Pickup_Landmark_*) on " + e.HostName); return; }
			int bad = 0, seen = 0;
			foreach (Transform t in loot)
			{
				Renderer[] rs = t.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
				if (rs.Length == 0) continue;
				Bounds b = rs[0].bounds;
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				float worst = 0f; bool any = false;
				foreach (Vector2 c in new[] { Vector2.zero, new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) })
				{
					Vector3 from = new Vector3(b.center.x + c.x * b.extents.x * 0.8f, b.max.y + 0.3f, b.center.z + c.y * b.extents.z * 0.8f);
					RaycastHit[] under = Physics.RaycastAll(from, Vector3.down, b.size.y + 2.3f, ~0, QueryTriggerInteraction.Ignore)
						.Where(h => !h.collider.transform.IsChildOf(t) && h.collider.GetComponentInParent<Network_Player>() == null).OrderBy(h => h.distance).ToArray();
					if (under.Length == 0) continue;
					// (only the caves' loot: on a cave's floor under its middle - cv40 counted Raft's own pickups on the sea floor, 165 pieces)
					if (c == Vector2.zero && !CaveFloor(under[0].collider.transform)) { any = false; break; }
					any = true;
					worst = Mathf.Max(worst, b.min.y - under[0].point.y);
				}
				if (!any) continue;
				seen++;
				if (worst > gap) { bad++; Log("  " + t.name + " at " + (t.position - e.Position).ToString("F1") + ": its low side " + worst.ToString("0.00") + " m over the floor - BAD"); }
			}
			if (bad > 0) Fail(bad + " of " + seen + " loot pieces standing in the air");
			else Log("PASS: loot pieces " + seen + " on the floor (within " + gap.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m)");
		}

		/// <summary>Whether a collider is a cave's floor: a dug tunnel's rock, or a cave piece's (BalboaCave*, Cave*).</summary>
		static bool CaveFloor(Transform t)
		{
			if (t.GetComponentInParent<DigTunnel>() != null) return true;
			if (t.name == CaveMountain.SkinName) return false; // (the hill's outside, not a cave's floor)
			for (; t != null; t = t.parent) if (t.name.StartsWith("BalboaCave", System.StringComparison.Ordinal) || t.name.StartsWith("Cave", System.StringComparison.Ordinal)) return true;
			return false;
		}

		[ConsoleCommand(name: "CIRayLook", docs: "Dev, in a world: what is seen from a point toward another on a loaded island (points from the island's land centre: a play's picture point minus the play's offset; or 'picture' in their place: the last play picture's own camera - w15Z: points given with the offset added looked out to sea) - a fan of 9x9 rays over 30 degrees, each ray's first renderer it sees the face of (its mesh's triangles, colliders or not; a face seen from behind is culled, as drawn - w15Z: an inside-out CaveBack round the eye was reported for the wall in front); fan=<degrees> narrows or widens the fan (30); at=<u>,<v> points the fan at that spot of a play picture taken from the first point at the second (0..1 across and down, the camera's own field of view, 16:9) - for asking what one odd bit in a picture is; logs a 9x9 map of what each ray met (top row up, left column left) and each thing seen with its share of the rays, path, material, shader and textures. For finding what draws an odd look (the blue rock, 11-06). CIRayLook [fan=<degrees>] [at=<u>,<v>] <island> <x> <y> <z> <tx> <ty> <tz> | picture")]
		public static void RayLookCommand(string[] args)
		{
			float fan = 30f; Vector2? at = null; bool who = false;
			while (args != null && args.Length > 0 && (args[0].StartsWith("fan=") || args[0].StartsWith("at=") || args[0] == "who"))
			{
				if (args[0] == "who") who = true;
				else if (args[0].StartsWith("fan=")) fan = float.Parse(args[0].Substring(4), CultureInfo.InvariantCulture);
				else { string[] uv = args[0].Substring(3).Split(','); at = new Vector2(float.Parse(uv[0], CultureInfo.InvariantCulture), float.Parse(uv[1], CultureInfo.InvariantCulture)); }
				args = args.Skip(1).ToArray();
			}
			if (args != null && args.Length == 1 && args[0] == "picture")
			{
				if (!LastPictureFrom.HasValue) { Fail("ray look: no play picture taken yet"); return; }
				Vector3 pf = LastPictureFrom.Value, pl = LastPictureLook.Value;
				Log("  the last play picture's camera, on " + LastPictureIsland + ": " + pf.ToString("F2") + " toward " + pl.ToString("F2"));
				StartTest(RayLookRoutine(LastPictureIsland, new[] { pf.x, pf.y, pf.z, pl.x, pl.y, pl.z }, fan, at, who));
				return;
			}
			if (args == null || args.Length < 7) { Fail("CIRayLook [fan=<degrees>] [at=<u>,<v>] <island> <x> <y> <z> <tx> <ty> <tz>, or CIRayLook [fan=..] [at=..] picture"); return; }
			float[] v = args.Skip(args.Length - 6).Select(a => float.Parse(a, CultureInfo.InvariantCulture)).ToArray();
			StartTest(RayLookRoutine(string.Join(" ", args.Take(args.Length - 6).ToArray()).Trim().Trim('"', '\'').Trim(), v, fan, at, who));
		}

		/// <summary>CIRayLook's and CIPictureSeeThrough's island: the one loaded here, or (host) brought next to the raft as
		/// CICaveGapsLook brings one, its hill built; got[0] null (and FAIL logged) when there is none.</summary>
		static IEnumerator LookIsland(string name, string what, IslandWorldState.Entry[] got)
		{
			got[0] = null;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Root != null && ((x.Name ?? "").IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0
				|| (x.HostName ?? "").IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0));
			if (e == null)
			{
				if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || CaveGapsLookIslandMissing(name)) { Fail(what + ": no loaded island called '" + name + "' here, and none to bring"); yield break; }
				float elev = 0f;
				try { elev = IslandFile.Load(IslandSpawner.PathFor(name)).Elevation; } catch { }
				Vector3? spot = ScSpot(name, 400f, elev);
				if (!spot.HasValue) { Fail(what + ": no open sea near the raft"); yield break; }
				var made = new List<IslandWorldState.Entry>();
				yield return ScBring(name, spot.Value, made);
				e = made.LastOrDefault();
				if (e == null || e.Root == null) { Fail(what + ": " + name + " didn't come"); yield break; }
				for (float w = 0f; w < 20f; w += 0.5f)
				{
					CaveMountain cm = e.Root.GetComponentInChildren<CaveMountain>();
					if (cm == null || !cm.Pending) break;
					yield return new WaitForSeconds(0.5f);
				}
				yield return new WaitForSeconds(2f);
			}
			got[0] = e;
		}

		/// <summary>CIRayLook's work: the island loaded here, or (host) brought next to the raft as CICaveGapsLook brings one.
		/// A test, so a run sees IDLE after it: run after a CIPlay, whose island is gone by then, the command only logged FAIL
		/// and a job waited out its hour for IDLE (w15Y, 11-18).</summary>
		static IEnumerator RayLookRoutine(string name, float[] v, float fan, Vector2? at, bool who = false)
		{
			var got = new IslandWorldState.Entry[1];
			yield return LookIsland(name, "ray look", got);
			IslandWorldState.Entry e = got[0];
			if (e == null) yield break;
			Vector3 from = e.Position + e.Root.transform.rotation * new Vector3(v[0], v[1], v[2]),
				to = e.Position + e.Root.transform.rotation * new Vector3(v[3], v[4], v[5]);
			Quaternion look = Quaternion.LookRotation(to - from);
			if (at.HasValue)
			{
				float half = Mathf.Tan((Camera.main != null ? Camera.main.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad);
				look = Quaternion.LookRotation(look * new Vector3((2f * at.Value.x - 1f) * half * 16f / 9f, (1f - 2f * at.Value.y) * half, 1f));
				Log("  aimed at " + at.Value.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + at.Value.y.ToString("0.00", CultureInfo.InvariantCulture) + " of the picture (field of view " + (Camera.main != null ? Camera.main.fieldOfView : 60f).ToString("0") + ")");
			}
			var rends = Object.FindObjectsOfType<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && r.GetComponentInParent<Network_Player>() == null && r.bounds.SqrDistance(from) < 80f * 80f).ToList();
			Log("  from " + from.ToString("F1") + " (island at " + e.Position.ToString("F1") + ")" + ": " + rends.Count + " renderers within 80 m");
			var seen = new Dictionary<Renderer, int>();
			var nearHit = new Dictionary<Renderer, float>(); // (w15Z: how far each was met, the nearest ray's)
			int rays = 0;
			var grid = new Renderer[9, 9]; // [row (k, up first), column (i, left first)]
			for (int i = -4; i <= 4; i++)
				for (int k = -4; k <= 4; k++)
				{
					Ray ray = new Ray(from, look * Quaternion.Euler(k * fan / 8f, i * fan / 8f, 0f) * Vector3.forward);
					rays++;
					Renderer best = null; float bd = 80f;
					foreach (Renderer r in rends)
					{
						float bdist;
						if (!r.bounds.IntersectRay(ray, out bdist) || bdist > bd) continue;
						float d = RayMeshDistance(r, ray);
						if (d < bd) { bd = d; best = r; }
					}
					grid[k + 4, i + 4] = best;
					if (best != null) { seen[best] = (seen.ContainsKey(best) ? seen[best] : 0) + 1; if (!nearHit.ContainsKey(best) || bd < nearHit[best]) nearHit[best] = bd; }
				}
			var order = seen.OrderByDescending(p => p.Value).Select(p => p.Key).ToList();
			for (int k = 0; k < 9; k++)
			{
				var row = new System.Text.StringBuilder("  map ");
				for (int i = 0; i < 9; i++) { int idx = grid[k, i] == null ? -1 : order.IndexOf(grid[k, i]); row.Append(idx < 0 ? '.' : (char)(idx < 26 ? 'A' + idx : 'a' + idx - 26)); }
				Log(row.ToString());
			}
			foreach (var kv in seen.OrderByDescending(p => p.Value))
			{
				Renderer r = kv.Key;
				string path = r.name; for (Transform t = r.transform.parent; t != null && t != e.Root.transform.parent; t = t.parent) path = t.name + "/" + path;
				string mats = string.Join("; ", r.sharedMaterials.Where(m => m != null).Select(m => m.name + " (" + m.shader.name + ": " + string.Join(" ", m.GetTexturePropertyNames().Where(t => m.GetTexture(t) != null).Select(t => t + "=" + m.GetTexture(t).name).ToArray()) + ")").ToArray());
				var b = new MaterialPropertyBlock(); r.GetPropertyBlock(b);
				// (11-21 cv46: and each part's own block's textures - a dug tunnel's lining wears Raft's rock and darker copies by
				// them, which the material's names don't show: Pendulum Isle's pale walls)
				Material[] sm = r.sharedMaterials;
				var partTex = new List<string>();
				for (int pi = 0; pi < sm.Length && sm.Length > 1; pi++)
				{
					if (sm[pi] == null) continue;
					var pb = new MaterialPropertyBlock(); r.GetPropertyBlock(pb, pi);
					if (pb.isEmpty) continue;
					string[] over = sm[pi].GetTexturePropertyNames().Where(t => pb.GetTexture(t) != null).Select(t => t + "=" + pb.GetTexture(t).name).ToArray();
					if (over.Length > 0) partTex.Add("part " + pi + ": " + string.Join(" ", over));
				}
				if (partTex.Count > 0) mats += " {blocks " + string.Join("; ", partTex.ToArray()) + "}";
				int at2 = order.IndexOf(r);
				Log("  " + (at2 < 26 ? (char)('A' + at2) : (char)('a' + at2 - 26)) + " " + kv.Value + "/" + rays + " " + path + " met " + nearHit[r].ToString("0.0") + " m off (bounds " + (r.bounds.min - e.Position).ToString("F1") + ".." + (r.bounds.max - e.Position).ToString("F1") + ") at " + (r.transform.position - e.Position).ToString("F1") + " [" + r.GetType().Name + (b.isEmpty ? "" : ", property block") + ", probes " + r.lightProbeUsage + "]: " + mats);
			}
			// (the middle ray: every renderer it crosses, faces from behind too, and those whose mesh couldn't be read - a flap
			// the fan never met (w15Z's Coral den) is drawn from both sides, or not readable)
			Ray mid = new Ray(from, look * Vector3.forward);
			var through = new List<KeyValuePair<float, string>>();
			foreach (Renderer r in rends)
			{
				float bdist;
				if (!r.bounds.IntersectRay(mid, out bdist) || bdist > 40f) continue;
				string path = r.name; for (Transform t = r.transform.parent; t != null && t != e.Root.transform.parent; t = t.parent) path = t.name + "/" + path;
				MeshFilter mf = r.GetComponent<MeshFilter>();
				Vector3[] mp; int[] mt;
				if (mf == null || mf.sharedMesh == null || !ReadMesh(mf.sharedMesh, out mp, out mt)) { through.Add(new KeyValuePair<float, string>(bdist, "its bounds " + bdist.ToString("0.0") + " m (" + (mf == null || mf.sharedMesh == null ? r.GetType().Name + ", no mesh" : "mesh " + mf.sharedMesh.name + " unread") + ") " + path)); continue; }
				float f = RayMeshDistance(r, mid);
				int fTri = RayHitTri; Vector3 fAt = RayHitPoint;
				float bk = RayMeshDistance(r, mid, true);
				if (f < 40f) through.Add(new KeyValuePair<float, string>(f, f.ToString("0.0") + " m its front " + path + HitFace(r, mp, mt, fTri, fAt, e.Position)));
				if (bk < 40f) through.Add(new KeyValuePair<float, string>(bk, bk.ToString("0.0") + " m its back " + path + HitFace(r, mp, mt, RayHitTri, RayHitPoint, e.Position)));
					if (f >= 40f && bk >= 40f)
					{
						// (its box crossed, no face met: a den's shell round the eye never showed in the fan, w15AA - its points as
						// read against the mesh's own box tell a misread buffer from a real miss)
						Bounds pb = new Bounds(mp.Length > 0 ? mp[0] : Vector3.zero, Vector3.zero);
						foreach (Vector3 q in mp) pb.Encapsulate(q);
						through.Add(new KeyValuePair<float, string>(bdist + 0.001f, "its bounds " + bdist.ToString("0.0") + " m, no face met (" + mf.sharedMesh.name + (mf.sharedMesh.isReadable ? "" : ", from the GPU") + ": " + mp.Length + " points " + mt.Length / 3 + " faces, read box " + pb.min.ToString("F1") + ".." + pb.max.ToString("F1") + ", mesh box " + mf.sharedMesh.bounds.min.ToString("F1") + ".." + mf.sharedMesh.bounds.max.ToString("F1") + ") " + path));
					}
			}
			// (and the terrain it meets: its paint there - a pale mound before Gravewell's diver den, 11-20, drew no renderer)
			foreach (Terrain land in Terrain.activeTerrains)
			{
				TerrainCollider tc = land == null ? null : land.GetComponent<TerrainCollider>();
				RaycastHit th;
				if (tc == null || !tc.Raycast(mid, out th, 40f)) continue;
				TerrainData td = land.terrainData;
				float nx = (th.point.x - land.transform.position.x) / td.size.x, nz = (th.point.z - land.transform.position.z) / td.size.z;
				int ar = td.alphamapWidth, ax = Mathf.Clamp((int)(nx * ar), 0, ar - 1), az = Mathf.Clamp((int)(nz * ar), 0, ar - 1);
				float[,,] am = td.GetAlphamaps(ax, az, 1, 1);
				var lw = new System.Text.StringBuilder();
				for (int l = 0; l < td.alphamapLayers; l++) lw.Append((l > 0 ? " " : "") + (td.terrainLayers != null && l < td.terrainLayers.Length && td.terrainLayers[l] != null ? td.terrainLayers[l].name : "layer" + l) + "=" + am[0, 0, l].ToString("0.00"));
				through.Add(new KeyValuePair<float, string>(th.distance, th.distance.ToString("0.0") + " m terrain " + land.name + " at " + (th.point - e.Position).ToString("F2") + " steep " + td.GetSteepness(nx, nz).ToString("0") + " deg, normal " + th.normal.ToString("F2") + ", paint " + lw));
			}
			foreach (var kv in through.OrderBy(x => x.Key).Take(24)) Log("  middle ray: " + kv.Value);
			if (who) yield return WhoDraws(e, from, to, at ?? new Vector2(0.5f, 0.5f), mid);
			Log("PASS: ray look " + seen.Count + " things");
		}

		/// <summary>CIRayLook who (w15AB): who draws the aimed spot of the picture, by the picture itself - the view drawn again
		/// with each renderer the spot's ray crosses (by its box, so unreadable meshes count) hidden in turn, and the spot's colour
		/// compared. A ray tool needs the mesh; this needs only the renderer (Raft's den shells read back as zeros).</summary>
		static int whoN;
		static IEnumerator WhoDraws(IslandWorldState.Entry e, Vector3 from, Vector3 to, Vector2 at, Ray mid)
		{
			Camera cam = Camera.main;
			if (cam == null) { Log("  who: no camera"); yield break; }
			yield return new WaitForEndOfFrame();
			whoN++;
			const int W = 1280, H = 720, P = 4;
			int px = Mathf.Clamp(Mathf.RoundToInt(at.x * W), P, W - P - 1), py = Mathf.Clamp(Mathf.RoundToInt((1f - at.y) * H), P, H - P - 1);
			var cands = Object.FindObjectsOfType<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && !(r is ParticleSystemRenderer)
				&& r.GetComponentInParent<Network_Player>() == null && r.bounds.IntersectRay(mid)).ToList();
			Vector3 oldPos = cam.transform.position; Quaternion oldRot = cam.transform.rotation; RenderTexture oldRt = cam.targetTexture;
			var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
			var tex = new Texture2D(2 * P + 1, 2 * P + 1, TextureFormat.RGB24, false);
			var results = new List<KeyValuePair<float, string>>();
			Renderer top = null; float topD = 0f;
			try
			{
				cam.transform.position = from; cam.transform.rotation = Quaternion.LookRotation(to - from);
				cam.targetTexture = rt;
				System.Func<Color> spot = () =>
				{
					cam.Render();
					RenderTexture was = RenderTexture.active; RenderTexture.active = rt;
					tex.ReadPixels(new Rect(px - P, py - P, 2 * P + 1, 2 * P + 1), 0, 0); tex.Apply();
					RenderTexture.active = was;
					Color sum = Color.clear; foreach (Color c in tex.GetPixels()) sum += c;
					return sum / ((2 * P + 1) * (2 * P + 1));
				};
				Color base0 = spot();
				SaveRt(rt, System.IO.Path.Combine(DynamicIslands.assetpath, "shot_who_" + whoN + "_base.png"));
				Log("  who: the spot (" + px + "," + (H - py) + " from the top) is " + ColorUtility.ToHtmlStringRGB(base0) + "; " + cands.Count + " renderer(s) on its ray");
				foreach (Renderer r in cands)
				{
					r.forceRenderingOff = true;
					Color c;
					try { c = spot(); } finally { r.forceRenderingOff = false; }
					float d = Mathf.Abs(c.r - base0.r) + Mathf.Abs(c.g - base0.g) + Mathf.Abs(c.b - base0.b);
					string path = r.name; for (Transform t = r.transform.parent; t != null && e != null && e.Root != null && t != e.Root.transform.parent; t = t.parent) path = t.name + "/" + path;
					MeshFilter mf = r.GetComponent<MeshFilter>();
					results.Add(new KeyValuePair<float, string>(d, "hidden, the spot " + ColorUtility.ToHtmlStringRGB(c) + " (change " + d.ToString("0.00", CultureInfo.InvariantCulture) + "): " + path
						+ (mf != null && mf.sharedMesh != null ? " mesh " + mf.sharedMesh.name + (mf.sharedMesh.isReadable ? "" : " (unreadable)") : "") + " material " + (r.sharedMaterial != null ? r.sharedMaterial.name : "none")));
					if (d > topD) { topD = d; top = r; }
				}
				if (top != null && topD > 0.05f)
				{
					top.forceRenderingOff = true;
					try { spot(); SaveRt(rt, System.IO.Path.Combine(DynamicIslands.assetpath, "shot_who_" + whoN + "_hidden.png")); } finally { top.forceRenderingOff = false; }
				}
			}
			finally
			{
				cam.targetTexture = oldRt; cam.transform.position = oldPos; cam.transform.rotation = oldRot;
				rt.Release(); Object.Destroy(tex);
			}
			foreach (var kv in results.OrderByDescending(x => x.Key).Take(12)) Log("  who: " + kv.Value);
			Log("  who: " + (top != null && topD > 0.05f ? "the spot is drawn by " + top.name + " (shot_who_" + whoN + "_hidden.png without it)" : "no single renderer on the ray changes the spot - something without a box on it (terrain, sky, an effect)"));
		}

		static readonly Dictionary<Mesh, KeyValuePair<Vector3[], int[]>> rayMeshes = new Dictionary<Mesh, KeyValuePair<Vector3[], int[]>>();

		// A mesh's points and triangles, readable or not (an unreadable one from its GPU buffers: Float32 or Float16 positions).
		static bool ReadMesh(Mesh m, out Vector3[] p, out int[] tri)
		{
			KeyValuePair<Vector3[], int[]> got;
			if (!rayMeshes.TryGetValue(m, out got))
			{
				got = new KeyValuePair<Vector3[], int[]>(null, null);
				try
				{
					if (m.isReadable) got = new KeyValuePair<Vector3[], int[]>(m.vertices, m.triangles);
					else if (m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position) == UnityEngine.Rendering.VertexAttributeFormat.Float32
						|| m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position) == UnityEngine.Rendering.VertexAttributeFormat.Float16)
					{
						bool f16 = m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position) == UnityEngine.Rendering.VertexAttributeFormat.Float16;
						int st = m.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position), off = m.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position), stride = m.GetVertexBufferStride(st);
						byte[] vb, ib;
						using (GraphicsBuffer g = m.GetVertexBuffer(st)) { vb = new byte[g.count * g.stride]; g.GetData(vb); }
						using (GraphicsBuffer g = m.GetIndexBuffer()) { ib = new byte[g.count * g.stride]; g.GetData(ib); }
						var pts = new Vector3[m.vertexCount];
						for (int v = 0; v < pts.Length; v++) { int a = v * stride + off; pts[v] = f16 ? new Vector3(Mathf.HalfToFloat(System.BitConverter.ToUInt16(vb, a)), Mathf.HalfToFloat(System.BitConverter.ToUInt16(vb, a + 2)), Mathf.HalfToFloat(System.BitConverter.ToUInt16(vb, a + 4)))
							: new Vector3(System.BitConverter.ToSingle(vb, a), System.BitConverter.ToSingle(vb, a + 4), System.BitConverter.ToSingle(vb, a + 8)); }
						bool wide = m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32;
						var ts = new List<int>();
						for (int sm = 0; sm < m.subMeshCount; sm++)
						{
							UnityEngine.Rendering.SubMeshDescriptor d = m.GetSubMesh(sm);
							if (d.topology != MeshTopology.Triangles) continue;
							for (int i = d.indexStart; i < d.indexStart + d.indexCount; i++) ts.Add(d.baseVertex + (wide ? System.BitConverter.ToInt32(ib, i * 4) : System.BitConverter.ToUInt16(ib, i * 2)));
						}
						got = new KeyValuePair<Vector3[], int[]>(pts, ts.ToArray());
					}
				}
				catch (System.Exception) { }
				rayMeshes[m] = got;
			}
			p = got.Key; tri = got.Value;
			return p != null && tri != null;
		}

		// The distance along the ray to the renderer's mesh's nearest face the ray meets from the front (as drawn: back faces culled), or infinity (a skinned or unreadable mesh: its bounds).
		/// <summary>CIRayLook's middle ray: the face it met - where (from the island), its corners, its facing and its corners' normals
		/// (round 4: the Utopia fin, a face of the hill's skin, both sides, that no trial moved).</summary>
		static string HitFace(Renderer r, Vector3[] p, int[] tri, int i, Vector3 at, Vector3 isle)
		{
			if (i < 0 || i + 2 >= tri.Length) return "";
			Transform t = r.transform;
			Vector3 a = t.TransformPoint(p[tri[i]]), b = t.TransformPoint(p[tri[i + 1]]), c = t.TransformPoint(p[tri[i + 2]]);
			Vector3 n = Vector3.Cross(b - a, c - a).normalized;
			MeshFilter mf = r.GetComponent<MeshFilter>();
			string vn = "";
			if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
			{
				Vector3[] ns = mf.sharedMesh.normals;
				if (ns != null && ns.Length > tri[i + 2]) vn = ", corner normals " + t.TransformDirection(ns[tri[i]]).ToString("F2") + " " + t.TransformDirection(ns[tri[i + 1]]).ToString("F2") + " " + t.TransformDirection(ns[tri[i + 2]]).ToString("F2");
				// (11-21 cv46: and which part - material - the face is drawn in)
				Mesh sm = mf.sharedMesh;
				if (sm.subMeshCount > 1 && tri.Length == sm.triangles.Length)
					for (int s = 0; s < sm.subMeshCount; s++)
					{
						var d = sm.GetSubMesh(s);
						if (i >= d.indexStart && i < d.indexStart + d.indexCount) { vn += ", part " + s; break; }
					}
			}
			return " (face " + i / 3 + " at " + (at - isle).ToString("F2") + ", corners " + (a - isle).ToString("F2") + " " + (b - isle).ToString("F2") + " " + (c - isle).ToString("F2") + ", facing " + n.ToString("F2") + vn + ")";
		}
		static int RayHitTri; static Vector3 RayHitPoint; // (RayMeshDistance: the face its nearest hit was on, and where)
		static float RayMeshDistance(Renderer r, Ray ray, bool backs = false)
		{
			RayHitTri = -1;
			MeshFilter f = r.GetComponent<MeshFilter>();
			Mesh m = f != null ? f.sharedMesh : null;
			float bd;
			Vector3[] p; int[] tri;
			// (Raft's own rock and the cave backs (uploaded) can't be read on the CPU: read from the GPU as CaveBacks does - without
			// it the ray met their bounds' box, and a den's deco rock's box in front of the eye was reported as the odd look, w15Z)
			if (m == null || !ReadMesh(m, out p, out tri)) return !r.bounds.Contains(ray.origin) && r.bounds.IntersectRay(ray, out bd) ? bd : float.PositiveInfinity; // (around the eye: can't tell)
			Transform t = r.transform;
			Vector3 o = t.InverseTransformPoint(ray.origin), dir = t.InverseTransformDirection(ray.direction);
			Vector3 ls = t.lossyScale; bool flip = ls.x * ls.y * ls.z < 0f; // (a mirrored transform turns the winding)
			float best = float.PositiveInfinity;
			for (int i = 0; i < tri.Length; i += 3)
			{
				Vector3 a = p[tri[i]], e1 = p[tri[i + 1]] - a, e2 = p[tri[i + 2]] - a;
				Vector3 h = Vector3.Cross(dir, e2); float det = Vector3.Dot(e1, h);
				if (Mathf.Abs(det) < 1e-7f || ((det < 0f) != flip) != backs) continue; // (seen from behind: culled; backs: only those)
				float inv = 1f / det; Vector3 s = o - a; float u = Vector3.Dot(s, h) * inv;
				if (u < 0f || u > 1f) continue;
				Vector3 q = Vector3.Cross(s, e1); float w = Vector3.Dot(dir, q) * inv;
				if (w < 0f || u + w > 1f) continue;
				float d = Vector3.Dot(e2, q) * inv;
				if (d > 0f && d < best) { best = d; RayHitTri = i; }
			}
			if (best != float.PositiveInfinity) RayHitPoint = t.TransformPoint(o + dir * best);
			return best == float.PositiveInfinity ? best : Vector3.Distance(ray.origin, t.TransformPoint(o + dir * best));
		}

		/// <summary>How many of a picture's 64 x 36 rays may see through the island (PictureSeeThrough) - about 1 %: a seam's
		/// pinhole; CIPlay's picture step fails over it.</summary>
		const int PictureSeeThroughAllowed = 23;
		/// <summary>PictureSeeThrough: how far under the ground (m) a ray must be to count as in it, and how far over it to count
		/// as out again.</summary>
		const float PictureGroundDip = 0.3f;
		/// <summary>PictureSeeThrough: a camera under the ground whose ray leaves it this near (m) is in the ground's skin - a
		/// steep bank's edge, seen out of as if from its face (cv48: Overpass Shoal's op_cave_out, 1.1 m under the bank's
		/// height at the water, every ray out of it 0.9 m on) - not inside the hill.</summary>
		const float PictureEyeSkin = 1.5f;

		[ConsoleCommand(name: "CIPictureSeeThrough", docs: "Dev, in a world: a picture's rays (64 x 36, as CIPlay's picture step checks every picture) that see through the island - past the back of its rock (cave pieces, the hill's skin, a dug tunnel's rock) or up through its ground from below - with a map of where. Points from the island's land centre as CIRayLook's (a play's picture point minus the play's offset), or 'picture': the last play picture's camera. With bad: PASS only when it does see through (the check is not blind: a camera inside the hill). CIPictureSeeThrough [bad] <island> <x> <y> <z> <tx> <ty> <tz> | [bad] picture")]
		public static void PictureSeeThroughCommand(string[] args)
		{
			var rest = (args ?? new string[0]).ToList();
			bool bad = rest.Remove("bad");
			if (rest.Count == 1 && rest[0] == "picture")
			{
				if (!LastPictureFrom.HasValue) { Fail("picture see-through: no play picture taken yet"); return; }
				Vector3 pf = LastPictureFrom.Value, pl = LastPictureLook.Value;
				StartTest(PictureSeeThroughRoutine(LastPictureIsland, new[] { pf.x, pf.y, pf.z, pl.x, pl.y, pl.z }, bad));
				return;
			}
			float[] v = new float[6];
			bool ok = rest.Count >= 7;
			for (int i = 0; ok && i < 6; i++) ok = float.TryParse(rest[rest.Count - 6 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
			if (!ok) { Fail("CIPictureSeeThrough [bad] <island> <x> <y> <z> <tx> <ty> <tz>, or CIPictureSeeThrough [bad] picture"); return; }
			StartTest(PictureSeeThroughRoutine(string.Join(" ", rest.Take(rest.Count - 6).ToArray()).Trim().Trim('"', '\'').Trim(), v, bad));
		}

		static IEnumerator PictureSeeThroughRoutine(string name, float[] v, bool bad)
		{
			var got = new IslandWorldState.Entry[1];
			yield return LookIsland(name, "picture see-through", got);
			IslandWorldState.Entry e = got[0];
			if (e == null) yield break;
			Vector3 from = e.Position + e.Root.transform.rotation * new Vector3(v[0], v[1], v[2]),
				to = e.Position + e.Root.transform.rotation * new Vector3(v[3], v[4], v[5]);
			var lines = new List<string>();
			int n = PictureSeeThrough(e, from, to, lines);
			foreach (string l in lines) Log("  " + l);
			if (bad)
			{
				if (n > PictureSeeThroughAllowed) Log("PASS: picture see-through bad (self-check: the check is not blind) - " + n + " of 64x36 rays see through from a camera that should see through");
				else Fail("picture see-through bad (self-check): blind - only " + n + " of 64x36 rays see through from a camera that should see through");
			}
			else if (n > PictureSeeThroughAllowed) Fail("picture see-through: " + n + " of 64x36 rays see through the island (" + PictureSeeThroughAllowed + " allowed)");
			else Log("PASS: picture see-through - " + n + " of 64x36 rays (" + PictureSeeThroughAllowed + " allowed)");
		}

		/// <summary>A picture's rays (64 x 36 over the frame, the main camera's field of view at 16:9) that see through the island
		/// as drawn: past the back of its rock (cave pieces, the hill's skin, a dug tunnel's rock and other cave or rock meshes,
		/// drawn one-sided) before any face drawn toward the eye, or up through its ground from below (the terrain is drawn from
		/// above only; its holes are open), or into the ground and on to nothing. Colliders can't stand in: they are not what
		/// is drawn - except for a mesh it can't read (11-21 cv48: Raft's den meshes read from the GPU as zeros, so a den's walls
		/// were missing and every picture inside one failed): its piece's own solid colliders round it stand in, met from the
		/// front. A camera in the ground's skin (PictureEyeSkin) is not under it. cv48: Overpass Shoal's op_bank picture stood 5 m inside the hill beside the culvert's room - the room seen
		/// through its outside, the sky through the ground - and its play passed (4 checks). lines: the count by kind, the
		/// camera's ground, the first bad rays and a 16 x 9 map. Returns the bad rays.</summary>
		static int PictureSeeThrough(IslandWorldState.Entry e, Vector3 from, Vector3 to, List<string> lines)
		{
			const int GX = 64, GY = 36; const float Far = 80f;
			var made = new List<Object>();
			bool backWas = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = false;
			int bad = 0, backs = 0, upGround = 0, lost = 0, sky = 0;
			try
			{
				// (the island's drawn faces near the eye, in world space: fronts (both ways where the material draws both), and the
				// backs of its rock - what a camera behind it sees through)
				var fr = new List<Vector3>(); var bk = new List<Vector3>();
				var bkWho = new List<KeyValuePair<int, string>>();
				// (meshes that can't be read - none, or every point the same: the GPU read of Raft's den meshes - and the colliders
				// of their pieces that stand in for them)
				var blind = new List<string>(); var stand = new List<Collider>(); var standSeen = new HashSet<Collider>();
				foreach (Renderer r in e.Root.GetComponentsInChildren<Renderer>(false))
				{
					if (!r.enabled || !(r is MeshRenderer) || r.bounds.SqrDistance(from) > Far * Far) continue;
					Material[] ms = r.sharedMaterials;
					if (ms.Length == 0 || ms.All(m => m == null || m.renderQueue >= 2450)) continue; // (water, glass, leaves: see-through anyway)
					if (r.GetComponentInParent<Network_Player>() != null || r.GetComponentInParent<AI_NetworkBehaviour>() != null) continue;
					MeshFilter mf = r.GetComponent<MeshFilter>();
					Vector3[] p; int[] tri;
					if (mf == null || mf.sharedMesh == null) continue;
					if (!ReadMesh(mf.sharedMesh, out p, out tri) || PictureBlank(p, mf.sharedMesh))
					{
						if (!blind.Contains(r.name)) blind.Add(r.name);
						// (the piece it belongs to: the island's object it is part of)
						Transform top = r.transform;
						while (top.parent != null && top.parent != e.Root.transform && top.parent.parent != e.Root.transform) top = top.parent;
						foreach (Collider c in top.GetComponentsInChildren<Collider>(false))
							if (c.enabled && !c.isTrigger && !(c is TerrainCollider) && c.bounds.Intersects(r.bounds) && standSeen.Add(c)) stand.Add(c);
						continue;
					}
					bool both = ms.Any(m => m != null && m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f);
					bool rock = !both && PictureRock(r);
					Transform t = r.transform;
					Vector3 ls = t.lossyScale; bool flip = ls.x * ls.y * ls.z < 0f;
					var w = new Vector3[p.Length];
					for (int i = 0; i < p.Length; i++) w[i] = t.TransformPoint(p[i]);
					if (rock) bkWho.Add(new KeyValuePair<int, string>(bk.Count / 3, r.name + (mf.sharedMesh.name.Length > 0 && mf.sharedMesh.name != r.name ? " (" + mf.sharedMesh.name + ")" : "")));
					for (int i = 0; i + 2 < tri.Length; i += 3)
					{
						if (tri[i] >= w.Length || tri[i + 1] >= w.Length || tri[i + 2] >= w.Length) continue;
						Vector3 a = w[tri[i]], b = flip ? w[tri[i + 2]] : w[tri[i + 1]], c = flip ? w[tri[i + 1]] : w[tri[i + 2]];
						if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-10f) continue;
						fr.Add(a); fr.Add(b); fr.Add(c);
						if (both) { fr.Add(a); fr.Add(c); fr.Add(b); }
						else if (rock) { bk.Add(a); bk.Add(c); bk.Add(b); }
					}
				}
				MeshCollider colF = SeeThroughFaces(fr, made), colB = SeeThroughFaces(bk, made);
				// (the ground: its height and its holes, read once)
				var lands = e.Root.GetComponentsInChildren<Terrain>(false).Where(l => l.terrainData != null).ToList();
				var solid = lands.Select(l => l.terrainData.GetHoles(0, 0, l.terrainData.holesResolution, l.terrainData.holesResolution)).ToList();
				// (the ground's height at a point, NaN off it; open[0]: a hole there)
				System.Func<Vector3, bool[], float> ground = (q, open) =>
				{
					open[0] = false;
					for (int k = 0; k < lands.Count; k++)
					{
						TerrainData td = lands[k].terrainData; Vector3 lo = lands[k].transform.position;
						float nx = (q.x - lo.x) / td.size.x, nz = (q.z - lo.z) / td.size.z;
						if (nx < 0f || nx >= 1f || nz < 0f || nz >= 1f) continue;
						int hr = td.holesResolution;
						open[0] = !solid[k][Mathf.Clamp((int)(nz * hr), 0, hr - 1), Mathf.Clamp((int)(nx * hr), 0, hr - 1)];
						return lands[k].SampleHeight(q) + lo.y;
					}
					return float.NaN;
				};
				var hole = new bool[1];
				float g0 = ground(from, hole);
				bool eyeUnder = !float.IsNaN(g0) && !hole[0] && from.y < g0 - PictureGroundDip;
				lines.Add("camera at " + (from - e.Position).ToString("F1") + " from the island's middle: " + (float.IsNaN(g0) ? "off its ground" : eyeUnder ? (g0 - from.y).ToString("0.0") + " m under its ground (a cave, or inside the hill)" : hole[0] ? "over a hole in its ground" : (from.y - g0).ToString("0.0") + " m over its ground")
					+ "; " + fr.Count / 3 + " drawn faces, " + bk.Count / 3 + " rock backs within " + Far.ToString("0") + " m"
					+ (blind.Count > 0 ? "; " + blind.Count + " meshes not readable (" + string.Join(", ", blind.Take(4).ToArray()) + (blind.Count > 4 ? ", ..." : "") + "), " + stand.Count + " of their pieces' colliders stand in" : ""));
				Quaternion look = Quaternion.LookRotation(to - from);
				float half = Mathf.Tan((Camera.main != null ? Camera.main.fieldOfView : 65f) * 0.5f * Mathf.Deg2Rad);
				var map = new int[9, 16];
				var shown = new List<string>();
				int skin = 0;
				for (int y = 0; y < GY; y++)
					for (int x = 0; x < GX; x++)
					{
						Ray ray = new Ray(from, (look * new Vector3((2f * (x + 0.5f) / GX - 1f) * half * 16f / 9f, (1f - 2f * (y + 0.5f) / GY) * half, 1f)).normalized);
						RaycastHit h;
						float fD = Far, bD = float.PositiveInfinity; int bTri = -1;
						if (colF != null && colF.Raycast(ray, out h, Far)) fD = h.distance;
						if (colB != null && colB.Raycast(ray, out h, Far)) { bD = h.distance; bTri = h.triangleIndex; }
						foreach (Collider c in stand) if (c != null && c.Raycast(ray, out h, fD)) fD = h.distance;
						// (the ground along it up to the first face met: met from above, it stops the ray; crossed from below, or
						// gone into and never left, it is seen through)
						bool under = eyeUnder, metGround = false;
						float upAt = float.NaN, stop = Mathf.Min(fD, bD);
						for (float s = 0.2f; s < stop; s += 0.35f)
						{
							Vector3 q = ray.GetPoint(s);
							float g = ground(q, hole);
							if (float.IsNaN(g)) continue;
							if (hole[0]) { under = false; continue; }
							if (!under && q.y < g) { metGround = true; break; }
							if (under && q.y > g + PictureGroundDip)
							{
								// (out of the ground this near the eye: the camera is in the ground's skin, not inside the hill)
								if (s < PictureEyeSkin) { under = false; skin++; continue; }
								upAt = s; break;
							}
						}
						string why = null;
						if (!metGround && bD < fD - 0.05f && (float.IsNaN(upAt) || bD < upAt))
						{
							backs++;
							string who = "?";
							foreach (var kv in bkWho) { if (kv.Key > bTri) break; who = kv.Value; }
							why = "past the back of " + who + " " + bD.ToString("0.0") + " m out, then " + (fD < Far ? "a face at " + fD.ToString("0.0") + " m" : "the sky");
						}
						else if (!float.IsNaN(upAt)) { upGround++; why = "up through the ground from below " + upAt.ToString("0.0") + " m out"; }
						else if (!metGround && under && fD >= Far) { lost++; why = "into the ground and on to nothing"; }
						else if (!metGround && fD >= Far && ray.direction.y > 0f) sky++;
						if (why == null) continue;
						bad++;
						map[y * 9 / GY, x * 16 / GX]++;
						if (shown.Count < 8) shown.Add("BAD ray at " + ((x + 0.5f) / GX).ToString("0.00", CultureInfo.InvariantCulture) + "," + ((y + 0.5f) / GY).ToString("0.00", CultureInfo.InvariantCulture) + " of the picture: " + why);
					}
				lines.Add(bad + " of " + (GX * GY) + " rays see through: " + backs + " past a rock's back, " + upGround + " up through the ground, " + lost + " into the ground; " + sky + " to the open sky (fine)"
					+ (skin > 0 ? "; " + skin + " out of the ground within " + PictureEyeSkin.ToString("0.0") + " m of the eye (the camera in the ground's skin, fine)" : ""));
				lines.AddRange(shown);
				if (bad > 0)
					for (int k = 0; k < 9; k++)
					{
						var row = new System.Text.StringBuilder("map ");
						for (int i = 0; i < 16; i++) row.Append(map[k, i] == 0 ? '.' : map[k, i] < 10 ? (char)('0' + map[k, i]) : '#');
						lines.Add(row.ToString());
					}
			}
			finally
			{
				Physics.queriesHitBackfaces = backWas;
				foreach (Object o in made) if (o != null) Object.Destroy(o);
			}
			return bad;
		}

		/// <summary>PictureSeeThrough: a mesh read as nothing - no points, or all at one point while the mesh's bounds are not (the
		/// GPU read of Raft's den meshes, cv48).</summary>
		static bool PictureBlank(Vector3[] p, Mesh m)
		{
			if (p == null || p.Length == 0) return true;
			if (m.bounds.size.sqrMagnitude < 1e-4f) return false;
			for (int i = 1; i < p.Length; i++) if ((p[i] - p[0]).sqrMagnitude > 1e-8f) return false;
			return true;
		}

		/// <summary>PictureSeeThrough: rock drawn one-sided, its back never meant to be seen - the hill's skin, a cave piece and its
		/// backs, a dug tunnel's rock, a den's wall, any cave, rock, cliff or mountain mesh.</summary>
		static bool PictureRock(Renderer r)
		{
			if (r.GetComponentInParent<CaveMountain>() != null || r.GetComponentInParent<DigTunnel>() != null) return true;
			MeshFilter mf = r.GetComponent<MeshFilter>();
			string n = (r.name + " " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "")).ToLowerInvariant();
			return n.Contains("cave") || n.Contains("rock") || n.Contains("cliff") || n.Contains("mountain");
		}

		[ConsoleCommand(name: "CIMeshRead", docs: "Dev, in a world: how an unreadable mesh's points read back from the GPU (CaveBacks' source, w15AA): for the first n unreadable cave meshes (or with back, their backs), the read box by GetData, by AsyncGPUReadback and by a copy into a raw buffer, against the mesh's own box; with info, the camera's path and the first den pieces' shader, passes and colliders. CIMeshRead [n] [back] [info]")]
		public static void MeshReadCommand(string[] args)
		{
			int n = args != null && args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 4;
			bool backs = args != null && args.Contains("back");
			if (args != null && args.Contains("info"))
			{
				// (what else a den piece offers for its backs: its colliders' meshes, its shader's cull switch, the camera's path)
				Camera cam = Camera.main;
				Log("  camera " + (cam == null ? "none" : cam.name + " " + cam.actualRenderingPath));
				int k = 0;
				foreach (MeshFilter mf in Object.FindObjectsOfType<MeshFilter>())
				{
					if (mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith("BalboaCave") || mf.name == CaveBacks.Name || k++ >= 6) continue;
					Transform piece = mf.transform;
					while (piece.parent != null && !piece.parent.name.StartsWith("Objects")) piece = piece.parent;
					var cols = piece.GetComponentsInChildren<Collider>(true).Select(c =>
					{
						MeshCollider mc = c as MeshCollider;
						Mesh cm = mc != null ? mc.sharedMesh : null;
						return c.GetType().Name + (cm == null ? "" : " " + cm.name + " readable " + cm.isReadable + " " + cm.vertexCount + " pts uv " + cm.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0) + " same " + (cm == mf.sharedMesh));
					}).ToArray();
					MeshRenderer r = mf.GetComponent<MeshRenderer>();
					Material mat = r != null ? r.sharedMaterial : null;
					string cull = "";
					if (mat != null && mat.shader != null)
						for (int i = 0; i < mat.shader.GetPropertyCount(); i++)
						{
							string pn = mat.shader.GetPropertyName(i);
							if (pn.IndexOf("Cull", System.StringComparison.OrdinalIgnoreCase) >= 0 || pn.IndexOf("Side", System.StringComparison.OrdinalIgnoreCase) >= 0) cull += " " + pn + "=" + mat.GetFloat(pn);
						}
					Log("  " + mf.name + " in " + piece.name + ": mesh " + mf.sharedMesh.name + ", shader " + (mat == null ? "none" : mat.shader.name + " passes " + mat.passCount + " cull:" + cull) + "; colliders " + string.Join("; ", cols));
				}
				Log("PASS: mesh info " + k);
				return;
			}
			var seen = new HashSet<Mesh>();
			int shown = 0, good = 0;
			// (every mesh loaded, not just those in the scene: after a play the island has gone, the backs stay made)
			foreach (Mesh m in Resources.FindObjectsOfTypeAll<Mesh>().Where(q => q.name.EndsWith("(back)") == backs))
			{
				if (m == null || m.isReadable || !seen.Add(m) || !m.name.StartsWith("BalboaCave")) continue;
				if (shown++ >= n) break;
				var ways = new List<string>();
				int st = m.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position), off = m.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position), stride = m.GetVertexBufferStride(st);
				bool f16 = m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position) == UnityEngine.Rendering.VertexAttributeFormat.Float16;
				System.Func<byte[], string> box = vb =>
				{
					if (vb == null) return "none";
					Bounds bb = new Bounds();
					for (int v = 0; v < m.vertexCount && v * stride + off + 12 <= vb.Length; v++)
					{
						int a = v * stride + off;
						Vector3 q = f16 ? new Vector3(Mathf.HalfToFloat(System.BitConverter.ToUInt16(vb, a)), Mathf.HalfToFloat(System.BitConverter.ToUInt16(vb, a + 2)), Mathf.HalfToFloat(System.BitConverter.ToUInt16(vb, a + 4)))
							: new Vector3(System.BitConverter.ToSingle(vb, a), System.BitConverter.ToSingle(vb, a + 4), System.BitConverter.ToSingle(vb, a + 8));
						if (v == 0) bb = new Bounds(q, Vector3.zero); else bb.Encapsulate(q);
					}
					return bb.min.ToString("F1") + ".." + bb.max.ToString("F1");
				};
				try
				{
					using (GraphicsBuffer g = m.GetVertexBuffer(st))
					{
						ways.Add("target " + m.vertexBufferTarget + ", buffer " + g.count + "x" + g.stride);
						var a = new byte[g.count * g.stride];
						try { g.GetData(a); ways.Add("GetData " + box(a)); } catch (System.Exception e) { ways.Add("GetData failed: " + e.Message); }
						try
						{
							var req = UnityEngine.Rendering.AsyncGPUReadback.Request(g);
							req.WaitForCompletion();
							ways.Add("async " + (req.hasError ? "error" : box(req.GetData<byte>().ToArray())));
						}
						catch (System.Exception e) { ways.Add("async failed: " + e.Message); }
						try
						{
							using (var raw = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination, g.count * g.stride / 4, 4))
							{
								Graphics.CopyBuffer(g, raw);
								var c = new byte[g.count * g.stride];
								raw.GetData(c);
								ways.Add("copied " + box(c));
							}
						}
						catch (System.Exception e) { ways.Add("copy failed: " + e.Message); }
					}
				}
				catch (System.Exception e) { ways.Add("buffer failed: " + e.Message); }
				if (ways.Any(w => (w.StartsWith("GetData") || w.StartsWith("async") || w.StartsWith("copied")) && !w.Contains("(0.0, 0.0, 0.0)..(0.0, 0.0, 0.0)") && !w.Contains("failed") && !w.Contains("error"))) good++;
				Log("  " + m.name + " (" + m.vertexCount + " points, " + (f16 ? "half" : "float") + ", mesh box " + m.bounds.min.ToString("F1") + ".." + m.bounds.max.ToString("F1") + "): " + string.Join("; ", ways));
			}
			Log((good > 0 ? "PASS" : "FAIL") + ": mesh read " + good + "/" + System.Math.Min(shown, n) + " read by some way");
		}

		[ConsoleCommand(name: "CIBackLook", docs: "Dev, in a world: the cave pieces' backs (CaveBacks: their shells seen from outside) for finding the blue rock (11-06). off / on: every back hidden or shown; full / plain / normals: every back made again with normals and tangents' handedness flipped (as made), faces turned only, or normals only. CIBackLook <off|on|full|plain|normals>")]
		public static void BackLookCommand(string[] args)
		{
			string mode = args != null && args.Length > 0 ? args[0] : "";
			var backs = Object.FindObjectsOfType<MeshRenderer>().Where(r => r.name == CaveBacks.Name).ToList();
			if (mode == "off" || mode == "on") foreach (MeshRenderer r in backs) r.enabled = mode == "on";
			else if (mode == "full" || mode == "plain" || mode == "normals")
			{
				CaveBacks.TestFlip = mode == "full" ? 0 : mode == "plain" ? 1 : 2;
				Log("  backs made again: " + CaveBacks.Remake());
			}
			else { Fail("CIBackLook <off|on|full|plain|normals>"); return; }
			Log("PASS: backs " + mode + " (" + backs.Count + ")");
		}

		[ConsoleCommand(name: "CIRockLook", docs: "Dev, in a world: the island rock (Raft's Mountains shader) for finding the blue rock (11-06). props: each Mountains material's settings and the scene's light; vanilla / lit: the cave light (CaveLit) off and Raft's probes back, or on again; dark: the cave pieces lit by nothing; shine <k>: their shine times k; noprobe / probe: reflection probes off or on for every Mountains renderer; sand: Balboa_BigRocks_Dirt swapped for the hill's sand rock; noterrain / terrain: the island ground hidden or shown; rocktint <r g b>: the ground's steep layer tinted; nosun / sun: the sun off or on; norefl / refl: reflections off (intensity 0, probes off) or back; flatnormal / flatall / normal: the backs' and hill skins' (or every Mountains renderer's) main normal map flat, or back; info: how the backs, the hill skin and the rest are lit (probe use, block, cave light); noamb / amb: the scene's ambient light off or back; nofog / fog: the scene's fog off or back (logs its settings); blockdark / blockoff: the backs and skin given a black probe straight through their blocks (cave light off), or their blocks cleared. CIRockLook <mode> [k]")]
		public static void RockLookCommand(string[] args)
		{
			string mode = args != null && args.Length > 0 ? args[0] : "";
			var rends = Object.FindObjectsOfType<MeshRenderer>().Where(r => r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name.Contains("Mountains"))).ToList();
			if (mode == "props")
			{
				Log("  ambient " + RenderSettings.ambientMode + " x" + RenderSettings.ambientIntensity + " sky " + RenderSettings.ambientSkyColor + ", reflections " + RenderSettings.defaultReflectionMode + " x" + RenderSettings.reflectionIntensity + " bounces " + RenderSettings.reflectionBounces + ", probes in scene " + Object.FindObjectsOfType<ReflectionProbe>().Length + ", sun " + (RenderSettings.sun != null ? RenderSettings.sun.color + " x" + RenderSettings.sun.intensity : "none"));
				foreach (ReflectionProbe rp in Object.FindObjectsOfType<ReflectionProbe>().Take(6)) Log("  probe " + rp.name + " " + rp.mode + " at " + rp.transform.position + " size " + rp.size + " x" + rp.intensity + " importance " + rp.importance);
				foreach (Material m in rends.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader.name.Contains("Mountains")).Distinct())
				{
					var sb = new System.Text.StringBuilder("  " + m.name + " (" + m.shader.name + ", keywords " + string.Join(" ", m.shaderKeywords) + "):");
					for (int i = 0; i < m.shader.GetPropertyCount(); i++)
					{
						string n = m.shader.GetPropertyName(i);
						switch (m.shader.GetPropertyType(i))
						{
							case UnityEngine.Rendering.ShaderPropertyType.Float: case UnityEngine.Rendering.ShaderPropertyType.Range: sb.Append(" " + n + "=" + m.GetFloat(n).ToString("0.###")); break;
							case UnityEngine.Rendering.ShaderPropertyType.Color: sb.Append(" " + n + "=" + m.GetColor(n)); break;
							case UnityEngine.Rendering.ShaderPropertyType.Vector: sb.Append(" " + n + "=" + m.GetVector(n)); break;
							case UnityEngine.Rendering.ShaderPropertyType.Texture: Texture t = m.GetTexture(n); sb.Append(" " + n + "=" + (t != null ? t.name : "-")); break;
						}
					}
					Log(sb.ToString());
				}
				foreach (var g in rends.GroupBy(r => r.lightProbeUsage + "/" + r.reflectionProbeUsage + "/" + (r.probeAnchor != null ? "anchor" : "-"))) Log("  renderers " + g.Key + ": " + g.Count());
			}
			else if (mode == "vanilla" || mode == "lit")
			{
				CaveLit.Off = mode == "vanilla";
				if (CaveLit.Off) foreach (MeshRenderer r in rends) { r.SetPropertyBlock(null); for (int i = 0; i < r.sharedMaterials.Length; i++) r.SetPropertyBlock(null, i); r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes; }
			}
			else if (mode == "nofog" || mode == "fog")
			{
				Log("  fog " + RenderSettings.fog + " " + RenderSettings.fogMode + " colour " + RenderSettings.fogColor + " density " + RenderSettings.fogDensity
					+ " " + RenderSettings.fogStartDistance + "-" + RenderSettings.fogEndDistance + " ambient " + RenderSettings.ambientMode + " " + RenderSettings.ambientLight);
				RenderSettings.fog = mode == "fog";
			}
			else if (mode == "dark") CaveLit.TestDark = true;
			else if (mode == "light") CaveLit.TestDark = false;
			else if (mode == "shine") CaveLit.TestShine = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
			else if (mode == "noprobe" || mode == "probe") foreach (MeshRenderer r in rends) r.reflectionProbeUsage = mode == "probe" ? UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes : UnityEngine.Rendering.ReflectionProbeUsage.Off;
			else if (mode == "sand")
			{
				Material sand = rends.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.name.StartsWith("Balboa_BigRocks_Sand"));
				if (sand == null) { Fail("rock look: no sand rock"); return; }
				foreach (MeshRenderer r in rends) r.sharedMaterials = r.sharedMaterials.Select(m => m != null && m.name.StartsWith("Balboa_BigRocks_Dirt") ? sand : m).ToArray();
			}
			else if (mode == "noterrain" || mode == "terrain") foreach (Terrain t in Terrain.activeTerrains) t.drawHeightmap = mode == "terrain";
			else if (mode == "rocktint" && args.Length >= 4)
			{
				var c = new Color(float.Parse(args[1], CultureInfo.InvariantCulture), float.Parse(args[2], CultureInfo.InvariantCulture), float.Parse(args[3], CultureInfo.InvariantCulture));
				int n = 0;
				foreach (Terrain t in Terrain.activeTerrains)
				{
					TerrainLayer[] ls = t.terrainData != null ? t.terrainData.terrainLayers : null;
					if (ls == null) continue;
					for (int i = 0; i < ls.Length; i++)
					{
						if (ls[i] == null || ls[i].diffuseTexture == null || !ls[i].name.StartsWith("CI_") || !(ls[i].name.EndsWith("_3") || ls[i].name.EndsWith("_7"))) continue;
						Texture2D orig;
						if (!rockOrig.TryGetValue(ls[i].name, out orig)) rockOrig[ls[i].name] = orig = ls[i].diffuseTexture;
						ls[i].diffuseTexture = c == Color.white ? orig : TerrainPainter.Tinted(orig, c);
						n++;
					}
					t.terrainData.terrainLayers = ls;
				}
				Log("  steep layers tinted " + c + ": " + n);
			}
			else if (mode == "nosun" || mode == "sun")
			{
				int n = 0;
				foreach (Light l in Object.FindObjectsOfType<Light>(true))
					if (l.type == LightType.Directional) { l.enabled = mode == "sun"; n++; }
				Log("  directional lights " + (mode == "sun" ? "on" : "off") + ": " + n);
			}
			else if (mode == "norefl" || mode == "refl")
			{
				if (mode == "norefl") { reflWas = RenderSettings.reflectionIntensity; RenderSettings.reflectionIntensity = 0f; }
				else if (reflWas >= 0f) RenderSettings.reflectionIntensity = reflWas;
				int n = 0;
				foreach (ReflectionProbe rp in Object.FindObjectsOfType<ReflectionProbe>(true)) { rp.enabled = mode == "refl"; n++; }
				Log("  reflections x" + RenderSettings.reflectionIntensity + ", probes " + (mode == "refl" ? "on" : "off") + ": " + n);
			}
			else if (mode == "flatnormal" || mode == "flatall" || mode == "normal")
			{
				// (the backs and the hill skin: their main normal map flat - or every Mountains renderer's, flatall)
				int n = 0;
				foreach (MeshRenderer r in rends)
				{
					bool back = r.name == global::DynamicIslands.Editor.CaveBacks.Name || r.name == CaveMountain.SkinName;
					if (mode == "flatnormal" && !back) continue;
					Material[] mats = r.sharedMaterials;
					for (int i = 0; i < mats.Length; i++)
					{
						Material m = mats[i], o;
						if (m == null) continue;
						if (flatOrig.TryGetValue(m, out o)) { if (mode == "normal") mats[i] = o; continue; }
						if (mode == "normal" || !m.HasProperty("_MainNormal")) continue;
						var f = new Material(m) { name = m.name };
						f.SetTexture("_MainNormal", Texture2D.normalTexture);
						flatOrig[f] = m;
						mats[i] = f;
					}
					r.sharedMaterials = mats;
					n++;
				}
				Log("  main normal " + mode + ": " + n + " renderers");
			}
			else if (mode == "info")
			{
				// (the backs and the hill skin, and the pieces they belong to: how each is lit)
				Log("  ambient " + RenderSettings.ambientMode + " x" + RenderSettings.ambientIntensity + " sky " + RenderSettings.ambientSkyColor + " equator " + RenderSettings.ambientEquatorColor + " ground " + RenderSettings.ambientGroundColor + " probe dc " + RenderSettings.ambientProbe[0, 0].ToString("0.##") + "/" + RenderSettings.ambientProbe[1, 0].ToString("0.##") + "/" + RenderSettings.ambientProbe[2, 0].ToString("0.##") + ", cave lit live " + CaveLit.AnyLive);
				foreach (var g in rends.GroupBy(r => (r.name == global::DynamicIslands.Editor.CaveBacks.Name ? "back" : r.name == CaveMountain.SkinName ? "skin" : "other") + " " + r.lightProbeUsage + " block " + r.HasPropertyBlock() + " cavelit " + (r.GetComponentInParent<CaveLit>() != null) + " " + string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.shader.name).Distinct().ToArray())))
					Log("  " + g.Key + ": " + g.Count() + " e.g. " + string.Join(", ", g.Take(3).Select(r => (r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name).ToArray()));
			}
			else if (mode == "noamb" || mode == "amb")
			{
				if (mode == "noamb")
				{
					if (ambWas == null) ambWas = new object[] { RenderSettings.ambientIntensity, RenderSettings.ambientProbe, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.ambientLight };
					RenderSettings.ambientIntensity = 0f; RenderSettings.ambientProbe = new UnityEngine.Rendering.SphericalHarmonicsL2();
					RenderSettings.ambientSkyColor = RenderSettings.ambientEquatorColor = RenderSettings.ambientGroundColor = RenderSettings.ambientLight = Color.black;
				}
				else if (ambWas != null)
				{
					RenderSettings.ambientIntensity = (float)ambWas[0]; RenderSettings.ambientProbe = (UnityEngine.Rendering.SphericalHarmonicsL2)ambWas[1];
					RenderSettings.ambientSkyColor = (Color)ambWas[2]; RenderSettings.ambientEquatorColor = (Color)ambWas[3]; RenderSettings.ambientGroundColor = (Color)ambWas[4]; RenderSettings.ambientLight = (Color)ambWas[5];
					ambWas = null;
				}
				Log("  scene ambient " + (mode == "noamb" ? "off" : "back"));
			}
			else if (mode == "blockdark" || mode == "blockoff")
			{
				// (the backs and the skin given a black light probe straight through their blocks, the cave light kept off them)
				CaveLit.Off = mode == "blockdark";
				var mpb = new MaterialPropertyBlock();
				var sh = new UnityEngine.Rendering.SphericalHarmonicsL2[] { new UnityEngine.Rendering.SphericalHarmonicsL2() };
				int n = 0;
				foreach (MeshRenderer r in rends)
				{
					if (r.name != global::DynamicIslands.Editor.CaveBacks.Name && r.name != CaveMountain.SkinName) continue;
					if (mode == "blockoff") { r.SetPropertyBlock(null); continue; }
					r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.CustomProvided;
					r.GetPropertyBlock(mpb); mpb.CopySHCoefficientArraysFrom(sh); r.SetPropertyBlock(mpb);
					for (int i = 0; i < r.sharedMaterials.Length; i++) { r.GetPropertyBlock(mpb, i); mpb.CopySHCoefficientArraysFrom(sh); r.SetPropertyBlock(mpb, i); }
					n++;
				}
				Log("  backs and skin black probe: " + n);
			}
			else { Fail("CIRockLook <props|vanilla|lit|dark|light|shine k|noprobe|probe|sand|noterrain|terrain|rocktint r g b|nosun|sun|norefl|refl|flatnormal|flatall|normal|info|noamb|amb|blockdark|blockoff>"); return; }
			Log("PASS: rock look " + mode + " (" + rends.Count + " renderers)");
		}

		static readonly Dictionary<string, Texture2D> rockOrig = new Dictionary<string, Texture2D>();
		static readonly Dictionary<Material, Material> flatOrig = new Dictionary<Material, Material>();
		static float reflWas = -1f;
		static object[] ambWas;

		[ConsoleCommand(name: "CIDownRays", docs: "Dev, in a world: what lies over and under points of a loaded island (island frame) - a ray down from 80 m at each, every collider it meets with its height, and the terrain's height there: is a cave's shell covered by ground, or out in the open over the sea (11-06). CIDownRays <island> <x> <z> [<x> <z> ...]")]
		public static void DownRaysCommand(string[] args)
		{
			int nums = 0;
			float f;
			while (args != null && nums < args.Length && float.TryParse(args[args.Length - 1 - nums], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) nums++;
			nums -= nums % 2;
			if (nums < 2) { Fail("CIDownRays <island> <x> <z> [<x> <z> ...]"); return; }
			float[] v = args.Skip(args.Length - nums).Select(a => float.Parse(a, CultureInfo.InvariantCulture)).ToArray();
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args.Take(args.Length - nums).ToArray()) });
			if (e == null) return;
			Quaternion rot = e.Root.transform.rotation;
			for (int i = 0; i + 1 < v.Length; i += 2)
			{
				Vector3 top = e.Position + rot * new Vector3(v[i], 80f, v[i + 1]);
				RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, 140f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
				Terrain ter = Terrain.activeTerrains.FirstOrDefault(t => { Vector3 p = t.transform.position, s = t.terrainData.size; return top.x >= p.x && top.x <= p.x + s.x && top.z >= p.z && top.z <= p.z + s.z; });
				string ground = ter != null ? (ter.SampleHeight(top) + ter.transform.position.y - e.Position.y).ToString("0.0") : "none";
				Log("  (" + v[i] + ", " + v[i + 1] + ") terrain " + ground + ": " + string.Join(", ", hits.Take(8).Select(h => h.collider.name + " " + (h.point.y - e.Position.y).ToString("0.0")).ToArray()));
			}
			Log("PASS: down rays " + v.Length / 2);
		}

		[ConsoleCommand(name: "CICaveLit", docs: "Dev, in a world: every Balboa cave piece placed on a loaded island (a mine's dead-end mound, an ar_cave den, the cave tool's tunnels) lit as a cave - its renderers on the cave light (CaveLit), not Raft's blue sky light that drew its faces bright blue (11-06). CICaveLit [island]")]
		public static void CaveLitCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(new[] { string.Join(" ", args ?? new string[0]) });
			if (e == null) return;
			var pieces = e.Root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("BalboaCave", System.StringComparison.Ordinal)
				&& (t.parent == null || !t.parent.name.StartsWith("BalboaCave", System.StringComparison.Ordinal))).ToList();
			if (pieces.Count == 0) { Fail("no Balboa cave piece on " + e.HostName); return; }
			int bad = 0;
			foreach (Transform t in pieces)
			{
				Renderer[] rs = t.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
				int off = rs.Count(r => r.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.CustomProvided);
				bool marked = t.GetComponentInParent<CaveLit>() != null;
				if (!marked || off > 0) bad++;
				Log("  " + t.name + " at " + (t.position - e.Position).ToString("F1") + ": " + rs.Length + " renderers, " + (marked ? "cave light" : "NOT marked") + ", on the sky's light " + off);
			}
			if (bad > 0) Fail(bad + " of " + pieces.Count + " Balboa cave pieces lit by the sky");
			else Log("PASS: Balboa cave pieces " + pieces.Count + ", all on the cave light");
		}
	}
}
