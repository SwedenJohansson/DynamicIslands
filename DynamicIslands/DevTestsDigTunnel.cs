using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	// The dig brush (DigTunnel): a winding tunnel dug into a generated island's hill, as the brush digs it, then walked and looked
	// round in from a world.
	public static partial class DevTests
	{
		const string DigIsland = "citest-digtunnel";

		[ConsoleCommand(name: "CIRimWalls", docs: "Dev, in a world: every dug tunnel on the loaded custom islands wears the cave hill skin's rock and tint on its rim walls (part 1, out in the open round the mouth) - the skin's material and colour where the island has a hill, else the rock the hill would wear (11-05: Raft's island rock went blue-grey in the shade beside the sand-brown hill)")]
		public static void RimWallsCommand() { StartTest(RimWallsRoutine()); }

		static IEnumerator RimWallsRoutine()
		{
			bool ok = true;
			int n = 0;
			// (an island just spawned builds its tunnels and hill over a few frames - wait for them, at most a minute)
			for (float w = 0f; w < 60f; w += 0.5f)
			{
				List<IslandWorldState.Entry> up = IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading).ToList();
				if (up.Count > 0 && up.All(x => { CaveMountain m = x.Root.GetComponentInChildren<CaveMountain>(); return m == null || !m.Pending; })
					&& up.Any(x => x.Root.GetComponentsInChildren<DigTunnel>(true).Any(t => t.Triangles > 0))) break;
				yield return new WaitForSeconds(0.5f);
			}
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading).ToList())
				foreach (DigTunnel t in e.Root.GetComponentsInChildren<DigTunnel>(true))
				{
					Transform r = t.transform.Find(DigTunnel.RockName);
					MeshRenderer mr = r != null ? r.GetComponent<MeshRenderer>() : null;
					n++;
					if (mr == null || mr.sharedMaterials.Length < 2) { Check(ref ok, false, "'" + e.Name + "' " + t.name + ": " + (mr == null ? "no rock renderer" : mr.sharedMaterials.Length + " materials on its rock")); continue; }
					Material walls = mr.sharedMaterials[1];
					CaveMountain hill = mr.GetComponentInParent<CaveMountain>();
					MeshRenderer skin = hill != null ? hill.SkinRenderer : null;
					string what = "'" + e.Name + "' " + t.name + " at " + (t.transform.position - e.Position).ToString("0") + ": rim walls " + (walls != null ? walls.name : "none");
					if (skin != null)
					{
						var a = new MaterialPropertyBlock(); var b = new MaterialPropertyBlock();
						skin.GetPropertyBlock(a); mr.GetPropertyBlock(b, 1);
						Texture ta = a.isEmpty ? null : a.GetTexture("_MainTex"), tb = b.isEmpty ? null : b.GetTexture("_MainTex");
						Check(ref ok, walls == skin.sharedMaterial && ta == tb, what + ", the hill's skin " + skin.sharedMaterial.name + (ta != null ? " (tinted " + ta.name + ")" : ""));
					}
					else Check(ref ok, walls != null && t.RimWalls == walls.name, what + " (no hill on the island: the rock it would wear, " + (t.RimWalls == "" ? "not dressed" : t.RimWalls) + ")");
				}
			Check(ref ok, n > 0, n + " dug tunnels on the loaded islands");
			if (ok) Log("PASS: rim walls wear the hill's rock"); else Fail("rim walls wear the hill's rock");
		}

		[ConsoleCommand(name: "CIDigTunnelEditor", docs: "Dev, editor: the Dig brush on a generated island - a tunnel dug into a hill from its foot, on 24 m and round a bend: its rock lining made, the ground cut only at its mouth, walkable end to end (a floor every step, steps under 0.6 m, 2 m of room), no look from inside out through the hill, a rim of rock over every cut cell round the mouth (none sunken); one Ctrl+Z per stroke (the tunnel, a branch, a Shift fill), the ground's holes back as they were; saved and loaded with the island; then a tunnel dug into a raised cliff's foot (its rim, no leaks; pictures of both mouths). Leaves the island citest-digtunnel for CIDigTunnelWorld")]
		public static void DigTunnelEditorCommand(string[] args) { StartTest(DigTunnelEditorRoutine()); }

		[ConsoleCommand(name: "CIDigTunnelWorld", docs: "Dev, in a world (host): the island CIDigTunnelEditor left spawned ahead of the raft - the ground cut over its tunnel's mouth, the lining there with its collider, the player walked from the mouth to the tunnel's end, no look from inside to the sky or the ground from below; pictures of the mouth and inside. The island and its file are taken away after (keep: kept)")]
		public static void DigTunnelWorldCommand(string[] args) { args0Keep = args.Contains("keep"); StartTest(DigTunnelWorldRoutine()); }

		[ConsoleCommand(name: "CIDigTunnelWalk", docs: "Dev, in a world (either player; the two-player test): a dug tunnel already in the world (CIDigTunnelWorld keep on the host) - its rock lining and the ground cut over its mouth on this PC, then this player walks in from its mouth to its end with Raft's controller and looks round inside for gaps to the sky")]
		public static void DigTunnelWalkCommand(string[] args) { StartTest(DigTunnelWalkRoutine()); }

		static int OpenCells(TerrainData data)
		{
			int cells = data.holesResolution, n = 0;
			bool[,] solid = data.GetHoles(0, 0, cells, cells);
			for (int z = 0; z < cells; z++)
				for (int x = 0; x < cells; x++)
					if (!solid[z, x]) n++;
			return n;
		}

		static bool IsDigRock(Collider c) { return c != null && c.GetComponentInParent<DigTunnel>() != null; }

		/// <summary>The floor under a point of the passage (a ray down from its middle): the dug rock or the ground (at the mouth).</summary>
		static bool DigFloor(Vector3 centre, out RaycastHit hit)
		{
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = false;
			try
			{
				hit = default(RaycastHit);
				RaycastHit[] all = Physics.RaycastAll(centre, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
				foreach (RaycastHit h in all)
				{
					if (h.collider.GetComponentInParent<Network_Player>() != null) continue;
					hit = h;
					return true;
				}
				return false;
			}
			finally { Physics.queriesHitBackfaces = backs; }
		}

		/// <summary>
		/// Looks from eyes in the passage: one that meets nothing or anything but the dug rock (or the ground from above outside) leaks
		/// (a thing met before the ray leaves the passage stands in the cave and is no leak),
		/// unless it has left by the passage into the open air first (out of the mouth) - the ray followed in 0.25 m steps up to what
		/// it meets: still in the passage until it is over the ground, never through the lining. A joined system (G3, 11-04):
		/// in the passage of any of joined counts as in it, and alsoRock's rock (a cave piece's) met from the front is the cave's own.
		/// </summary>
		static int DigLeaks(DigTunnel t, Terrain land, List<Vector3> eyes, List<string> seen, out int under, IList<DigTunnel> joined = null, System.Func<Collider, bool> alsoRock = null, List<Ray> rays = null)
		{
			under = 0;
			int leaks = 0;
			var looks = new List<Vector3>();
			foreach (float pitch in new[] { -20f, 0f, 15f, 35f, 60f })
				for (int i = 0; i < 16; i++) looks.Add(Quaternion.Euler(-pitch, i * 22.5f, 0f) * Vector3.forward);
			looks.Add(Vector3.up);
			float o = land.transform.position.y;
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.SyncTransforms();
			try
			{
				foreach (Vector3 eye in eyes)
					foreach (Vector3 d in looks)
					{
						Physics.queriesHitBackfaces = true;
						RaycastHit[] all = Physics.RaycastAll(eye, d, 150f, ~0, QueryTriggerInteraction.Ignore)
							.Where(h => h.collider.GetComponentInParent<Network_Player>() == null && h.collider.GetComponentInParent<AI_NetworkBehaviour>() == null)
							.OrderBy(h => h.distance).ToArray();
						Physics.queriesHitBackfaces = false;
						float first = all.Length > 0 ? all[0].distance : 150f;
						bool outside = false;
						float left = -1f;
						for (float s = 0.25f; s < Mathf.Min(first, 60f); s += 0.25f)
						{
							Vector3 p = eye + d * s;
							if (p.y > land.SampleHeight(p) + o + 0.05f) { outside = true; break; }
							if (t.Distance(t.transform.InverseTransformPoint(p)) > 0.4f && (joined == null || joined.All(j => j == null || j == t || j.Distance(j.transform.InverseTransformPoint(p)) > 0.4f)))
							{
								// (out of the passage at the lining's top, under the cut ground, with nothing met: out by the open mouth; lower: through the lining)
								left = s;
								if (p.y > land.SampleHeight(p) + o - DigTunnel.Under - 0.1f) outside = true;
								break;
							}
						}
						if (outside) continue;
						Vector3 lp = eye + d * Mathf.Max(left, 0f);
						string where = (eye - eyes[0]).magnitude.ToString("0") + " m in, " + d.ToString("F1") + " (left the passage " + left.ToString("0.0") + " m on, "
							+ (lp.y - land.SampleHeight(lp) - o).ToString("0.0") + " m from the ground there"
							+ (all.Length > 0 ? ", hit at " + (all[0].point.y - land.SampleHeight(all[0].point) - o).ToString("0.0") + " m from the ground, normal " + all[0].normal.ToString("F1") : "") + ")";
						int was = leaks + under;
						if (all.Length == 0) { leaks++; seen.Add("sky " + where); }
						else if (all[0].collider is TerrainCollider) { if (all[0].normal.y < 0f) { under++; seen.Add("the ground from below " + where); } else { leaks++; seen.Add("the ground " + all[0].distance.ToString("0.0") + " m off " + where); } }
						// (a thing met while the ray is still in the passage stands in the cave - a sign, a chest, a plank: no leak;
						// 11-04, Ironreef's drift sign 4 m in front of an eye failed the check though the ray never left the passage)
						else if (left < 0f && first < 60f) { }
						else if (!IsDigRock(all[0].collider) && !(alsoRock != null && alsoRock(all[0].collider) && Vector3.Dot(all[0].normal, d) < 0f)) { leaks++; seen.Add("out to " + all[0].collider.name + " " + all[0].distance.ToString("0") + " m, " + where); }
						if (rays != null && leaks + under > was) rays.Add(new Ray(eye, d));
					}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			return leaks;
		}

		/// <summary>The passage's middle line in the world: stroke 0's floor points, raised to the middle of the passage.</summary>
		static List<Vector3> DigLine(DigTunnel t, int stroke = 0)
		{
			return t.Strokes[stroke].Select(p => t.transform.TransformPoint(p.At + Vector3.up * (DigTunnel.CentreUp * p.R))).ToList();
		}

		/// <summary>
		/// The mouth's rim over the cut ground: looked at from straight over each cut cell, the first thing met is the dug rock -
		/// over the ground there (the rim), or down in the passage (the open mouth); a cell whose rock top is under the ground
		/// (a sunken square of the cut) is counted in sunken.
		/// </summary>
		static string RimCover(DigTunnel t, Terrain land, IEnumerable<Vector2Int> cut, out int sunken)
		{
			TerrainData data = land.terrainData;
			Vector3 o = land.transform.position;
			float cw = data.size.x / data.holesResolution, ch = data.size.z / data.holesResolution;
			int over = 0, open = 0, other = 0;
			string where = "";
			sunken = 0;
			Physics.SyncTransforms();
			foreach (Vector2Int q in cut)
			{
				var w = new Vector3(o.x + (q.x + 0.5f) * cw, 0f, o.z + (q.y + 0.5f) * ch);
				float g = land.SampleHeight(w) + o.y;
				RaycastHit hit;
				if (!Physics.Raycast(new Vector3(w.x, g + 30f, w.z), Vector3.down, out hit, 60f, ~0, QueryTriggerInteraction.Ignore) || !IsDigRock(hit.collider)) { other++; continue; }
				if (hit.point.y >= g - 0.05f) over++;
				else if (t.Distance(t.transform.InverseTransformPoint(hit.point + Vector3.up * 0.3f)) < 0f) open++;
				else { sunken++; if (where.Length < 200) where += " (" + q.x + "," + q.y + ": " + (hit.point.y - g).ToString("0.00") + " m, passage " + t.Distance(t.transform.InverseTransformPoint(hit.point)).ToString("0.00") + ")"; }
			}
			return over + " under the rim, " + open + " the open mouth, " + sunken + " sunken" + where + ", " + other + " under something else; cells " + cw.ToString("0.00") + " m";
		}

		/// <summary>
		/// The mouth looked at from outside: points of the ground's surface over the cut cells (5 x 5 a cell, the mouth's own
		/// opening left out) looked at from each eye, or from straight over them (eye null): a look that meets nothing, or meets
		/// something 0.6 m or more past the point, sees through the hill (the sky or the hill's inside).
		/// </summary>
		static int SeeThrough(DigTunnel t, Terrain land, IEnumerable<Vector2Int> cut, IList<Vector3> eyes, List<string> seen, out int looked)
		{
			TerrainData data = land.terrainData;
			Vector3 o = land.transform.position;
			float cw = data.size.x / data.holesResolution, ch = data.size.z / data.holesResolution;
			int through = 0;
			looked = 0;
			Physics.SyncTransforms();
			foreach (Vector2Int q in cut)
				for (int a = 0; a < 5; a++)
					for (int b = 0; b < 5; b++)
					{
						var p = new Vector3(o.x + (q.x + 0.1f + a * 0.2f) * cw, 0f, o.z + (q.y + 0.1f + b * 0.2f) * ch);
						p.y = land.SampleHeight(p) + o.y;
						if (t.Distance(t.transform.InverseTransformPoint(p)) < 0.3f) continue; // (the opening)
						// (and its lip: where a shallow passage runs under the ground with less than the mesh's step (0.35-0.6 m) of roof, the
						// lining can't be thinner than a step and the mouth reaches that far - looking in there is looking into the mouth)
						bool lip = false;
						for (float k = 0.15f; k <= 0.61f && !lip; k += 0.15f) lip = t.Distance(t.transform.InverseTransformPoint(p - Vector3.up * k)) < 0f;
						if (lip) continue;
						IEnumerable<Vector3> from = eyes != null ? eyes : (IEnumerable<Vector3>)new[] { p + Vector3.up * 25f };
						foreach (Vector3 eye in from)
						{
							Vector3 d = p - eye;
							float dist = d.magnitude;
							if (dist < 1f) continue;
							d /= dist;
							looked++;
							RaycastHit hit;
							bool any = Physics.Raycast(eye, d, out hit, dist + 40f, ~0, QueryTriggerInteraction.Ignore);
							while (any && (hit.collider.GetComponentInParent<Network_Player>() != null || hit.collider.GetComponentInParent<AI_NetworkBehaviour>() != null))
								any = Physics.Raycast(hit.point + d * 0.05f, d, out hit, dist + 40f, ~0, QueryTriggerInteraction.Ignore);
							float past = any ? Vector3.Dot(hit.point - p, d) : 999f;
							if (past < 0.6f) continue;
							through++;
							if (seen.Count < 8) seen.Add("(" + q.x + "," + q.y + ") " + (p.y - o.y).ToString("0.0") + " m high: " + (any ? hit.collider.name + " " + past.ToString("0.0") + " m past it" : "the sky"));
						}
					}
			return through;
		}

		/// <summary>The rim's look: its columns, the ground texture on its top, the rock's shader and what it takes.</summary>
		static string RimLook(DigTunnel t)
		{
			Transform rock = t.transform.Find(DigTunnel.RockName);
			MeshRenderer mr = rock != null ? rock.GetComponent<MeshRenderer>() : null;
			Material m = mr != null && mr.sharedMaterials.Length > 1 ? mr.sharedMaterials[1] : null;
			MeshFilter mf = rock != null ? rock.GetComponent<MeshFilter>() : null;
			int top = mf != null && mf.sharedMesh != null && mf.sharedMesh.subMeshCount > 1 ? mf.sharedMesh.GetTriangles(1).Length / 3 : 0;
			string tex = "";
			if (mr != null)
			{
				Material[] ms = mr.sharedMaterials;
				var pb = new MaterialPropertyBlock();
				mr.GetPropertyBlock(pb, 0);
				Texture side = pb.GetTexture("_SideDiffuse");
				tex = "; " + ms.Length + " materials, the lining's rock " + (side != null ? side.name : "(no block: " + (ms.Length > 0 && ms[0] != null && ms[0].HasProperty("_SideDiffuse") && ms[0].GetTexture("_SideDiffuse") != null ? ms[0].GetTexture("_SideDiffuse").name : "none") + ")");
				if (ms.Length > 2 && ms[2] != null)
				{
					// (the terrain shaders don't declare their splat maps: asking the material says "none" - the tunnel keeps them)
					Terrain land = t.SkinLand;
					tex += ", the top (" + ms[2].shader.name + ", ground's " + (land != null && land.materialTemplate != null ? land.materialTemplate.shader.name : "built-in") + ") " + t.TopTextures;
				}
			}
			return t.RimColumns + " rim columns, " + top + " rim triangles, its top " + (t.RimTop == "" ? "rock" : t.RimTop)
				+ (m != null ? "; material " + m.name + ", shader " + m.shader.name + " (" + string.Join(", ", m.GetTexturePropertyNames()) + ")" : "; no second material") + tex;
		}

		/// <summary>Where a dug mouth's rim has rock shards on the ground (DigTunnel.RimShards), the first few.</summary>
		internal static string RimShardsAt(DigTunnel t) { string w; t.RimShards(out w); return w; }

		static IEnumerator DigTunnelEditorRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("dig tunnel: in the editor"); yield break; }
			bool ok = true;
			float radius0 = DigTunnel.Radius, slope0 = DigTunnel.Slope;
			DigTunnel.Radius = 1.75f; DigTunnel.Slope = 0f;
			DynamicIslands.NewIsland();
			yield return null;
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4242, Radius = 130f, Height = 30f, Style = TerrainPainter.Tropical });
			yield return null;
			yield return new WaitForSecondsRealtime(0.6f);
			Terrain t = terraineditor.terrain;
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorWaterLevel;
			System.Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };
			int cells = data.holesResolution;
			bool[,] holes0 = data.GetHoles(0, 0, cells, cells);
			int open0 = OpenCells(data);

			// A hill's foot: on land, the ground 10 m on 3 m higher and 24 m on (and round the bend) 5.5 m higher - the steepest
			// such place, from each side in turn
			Vector3 mouth = Vector3.zero, dir = Vector3.zero, side = Vector3.zero;
			bool found = false;
			float bestScore = 0f;
			foreach (Vector3 d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
				for (float s = 90f; s > 10f; s -= 2f)
				{
					Vector3 p = ground(c - d * s);
					if (p.y < sea + 1.5f) continue;
					foreach (Vector3 sd in new[] { Vector3.Cross(Vector3.up, d), -Vector3.Cross(Vector3.up, d) })
					{
						float score = Mathf.Min(Mathf.Min(ground(p + d * 10f).y - p.y - 3f, ground(p + d * 24f).y - p.y - 5.5f),
							Mathf.Min(ground(p + d * 24f + sd * 10f).y - p.y - 5.5f, ground(p + d * 16f + sd * 8f).y - p.y - 5f));
						if (score >= bestScore) { bestScore = score; mouth = p; dir = d; side = sd; found = true; }
					}
				}
			if (!found) { Fail("dig tunnel: no hillside found on the generated island"); DigTunnel.Radius = radius0; DigTunnel.Slope = slope0; yield break; }
			Log("  the mouth " + (mouth.y - sea).ToString("0.0") + " m above the sea, dug " + dir + " then " + side);

			// The stroke: pressed at the hill's foot, the mouse 24 m on into the hill and then 10 m round the bend
			var pts = new List<Vector3> { mouth, mouth + dir * 24f, mouth + dir * 24f + side * 10f };
			DigTunnel tun = DigTunnel.TestStroke(t, pts);
			yield return null;
			Check(ref ok, tun != null, "the stroke dug a tunnel");
			if (tun == null) { Fail("dig tunnel editor"); DigTunnel.Radius = radius0; DigTunnel.Slope = slope0; yield break; }
			Transform rock = tun.transform.Find(DigTunnel.RockName);
			MeshCollider mc = rock != null ? rock.GetComponent<MeshCollider>() : null;
			int n0 = tun.PointCount;
			Check(ref ok, n0 >= 30 && tun.Strokes.Count == 1, "the passage: " + n0 + " points in " + tun.Strokes.Count + " stroke(s)");
			Check(ref ok, tun.Triangles > 1000 && mc != null && mc.sharedMesh != null && mc.enabled && rock.gameObject.layer == IslandSpawner.TerrainLayer, "its rock lining: " + tun.Triangles + " triangles, collider " + (mc != null && mc.sharedMesh != null));
			EditorGameObject ego = tun.GetComponent<EditorGameObject>();
			Check(ref ok, ego != null && ego.Props != null && ego.Props.ContainsKey(DigTunnel.PathKey) && ego.Props[DigTunnel.PathKey].Length > 20, "the path kept in its props (" + (ego != null && ego.Props != null && ego.Props.ContainsKey(DigTunnel.PathKey) ? ego.Props[DigTunnel.PathKey].Length : 0) + " characters)");

			// The ground cut at the mouth, not over the deep end
			DigTunnel.EditorNow(t);
			HashSet<Vector2Int> cut = DigTunnel.EditorCutCells;
			bool[,] holes1 = data.GetHoles(0, 0, cells, cells);
			int notOpen = cut.Count(q => holes1[q.y, q.x]);
			Vector3 end = DigLine(tun).Last();
			float cellSize = data.size.x / cells;
			int overEnd = cut.Count(q => new Vector2(o.x + (q.x + 0.5f) * cellSize - end.x, o.z + (q.y + 0.5f) * cellSize - end.z).magnitude < 6f);
			float nearMouth = cut.Count == 0 ? 0f : cut.Max(q => new Vector2(o.x + (q.x + 0.5f) * cellSize - mouth.x, o.z + (q.y + 0.5f) * cellSize - mouth.z).magnitude);
			Check(ref ok, cut.Count > 0 && notOpen == 0 && overEnd == 0, "the ground cut over its mouth: " + cut.Count + " cells (the farthest " + nearMouth.ToString("0.0") + " m from the mouth; " + notOpen + " not cut, " + overEnd + " over the deep end)");

			// Walkable end to end: a floor under every point of the passage, steps under 0.6 m, 2 m of room over it
			List<Vector3> line = DigLine(tun);
			int noFloor = 0, low = 0;
			float bigStep = 0f, lastFloor = float.NaN;
			var eyes = new List<Vector3>();
			var bad = new List<string>();
			for (int i = 0; i < line.Count; i++)
			{
				RaycastHit fh;
				if (!DigFloor(line[i], out fh)) { noFloor++; bad.Add("no floor at " + i); continue; }
				if (!IsDigRock(fh.collider) && !(fh.collider is TerrainCollider)) { noFloor++; bad.Add(fh.collider.name + " at " + i); continue; }
				if (!float.IsNaN(lastFloor)) bigStep = Mathf.Max(bigStep, Mathf.Abs(fh.point.y - lastFloor));
				lastFloor = fh.point.y;
				RaycastHit up;
				Physics.queriesHitBackfaces = true;
				bool roof = Physics.Raycast(fh.point + Vector3.up * 0.1f, Vector3.up, out up, 10f, ~0, QueryTriggerInteraction.Ignore);
				Physics.queriesHitBackfaces = false;
				if (roof && up.distance + 0.1f < 2f) { low++; bad.Add("room " + (up.distance + 0.1f).ToString("0.0") + " m at " + i); }
				if (i % 4 == 2) eyes.Add(fh.point + Vector3.up * 1.6f);
			}
			Check(ref ok, noFloor == 0 && low == 0 && bigStep < 0.6f, "walkable: " + line.Count + " points, " + noFloor + " without a floor, " + low + " too low, the biggest step " + bigStep.ToString("0.00") + " m" + (bad.Count > 0 ? ": " + string.Join(", ", bad.Take(6).ToArray()) : ""));

			// From inside: no look out through the hill
			{
				var seen = new List<string>();
				int under;
				int leaks = DigLeaks(tun, t, eyes, seen, out under);
				Check(ref ok, eyes.Count >= 6 && leaks == 0 && under == 0, "from inside (" + eyes.Count + " places, 81 looks each): " + leaks + " looks out through the hill, " + under + " to the ground from below" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));
			}
			// The mouth: a rim of rock round it, over the cut ground (none of the cut's squares sunken), its top the ground's look
			{
				int sunken;
				string cover = RimCover(tun, t, cut, out sunken);
				int shards = tun.RimTop == "" ? 0 : tun.RimShards(out _);
				Check(ref ok, tun.RimColumns > 0 && sunken == 0 && shards == 0, "the mouth's rim: " + RimLook(tun) + "; the cut cells: " + cover + "; " + shards + " rock shards on the ground" + (shards > 0 ? " (" + RimShardsAt(tun) + ")" : ""));
			}
			// From outside, in front and from over it: no look through the hill at the mouth (11-02)
			{
				var seen = new List<string>();
				int looked, looked2;
				int front = SeeThrough(tun, t, cut, new[] { mouth - dir * 12f + Vector3.up * 5f, mouth - dir * 6f + side * 7f + Vector3.up * 2.5f, mouth - dir * 6f - side * 7f + Vector3.up * 2.5f }, seen, out looked);
				int above = SeeThrough(tun, t, cut, null, seen, out looked2);
				Check(ref ok, front == 0 && above == 0 && looked > 0, "the mouth from outside: " + front + " of " + looked + " looks from in front and " + above + " of " + looked2 + " from over it see through the hill" + (seen.Count > 0 ? ": " + string.Join("; ", seen.ToArray()) : ""));
			}
			// No ground cut over a buried part: every cut cell has the passage within 1.5 m of its ground somewhere (11-02)
			{
				int buried = 0;
				foreach (Vector2Int q in cut)
				{
					float bestD = float.MaxValue;
					for (int a = 0; a <= 2; a++)
						for (int b2 = 0; b2 <= 2; b2++)
						{
							Vector3 w = ground(new Vector3(o.x + (q.x + a * 0.5f) * cellSize, 0f, o.z + (q.y + b2 * 0.5f) * cellSize));
							bestD = Mathf.Min(bestD, tun.Distance(tun.transform.InverseTransformPoint(w)));
						}
					if (bestD > 1.5f) buried++;
				}
				Check(ref ok, buried == 0, "no ground cut over a buried part: " + buried + " of " + cut.Count + " cut cells have the passage deeper than 1.5 m under them");
			}
			// (the editor camera flown into the mouth, and God mode: CIDigTunnelCamera)
			yield return CameraShot(mouth - dir * 12f + Vector3.up * 5f, mouth + Vector3.up * 1.5f, "digtunnel_mouth");
			yield return CameraShot(mouth - dir * 6f + side * 7f + Vector3.up * 2.5f, mouth + dir * 1f + Vector3.up * 1.2f, "digtunnel_mouth_side");
			yield return CameraShot(line[3] + Vector3.down * 0.4f, line[Mathf.Min(line.Count - 1, 24)], "digtunnel_inside");

			// One undo takes the tunnel and its holes back; redo brings them
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			DigTunnel.EditorNow(t);
			bool[,] undone = data.GetHoles(0, 0, cells, cells);
			int differ = 0;
			for (int z = 0; z < cells; z++) for (int x = 0; x < cells; x++) if (undone[z, x] != holes0[z, x]) differ++;
			Check(ref ok, !tun.gameObject.activeSelf && differ == 0, "undo: the tunnel gone and the ground's holes as they were (" + differ + " cells differ)");
			CommandUndoRedo.UndoRedoManager.Redo();
			yield return null;
			DigTunnel.EditorNow(t);
			Check(ref ok, tun.gameObject.activeSelf && tun.PointCount == n0 && DigTunnel.EditorCutCells.Count == cut.Count && OpenCells(data) == open0 + cut.Count, "redo: back again (" + DigTunnel.EditorCutCells.Count + " cells cut)");

			// A press on the passage digs a branch from it (one undo); Shift fills it back (one undo)
			Vector3 from = line[line.Count / 3];
			Vector3 dir2 = (from - mouth); dir2.y = 0f; dir2 = Vector3.Cross(Vector3.up, dir2.normalized) * (Vector3.Dot(Vector3.Cross(Vector3.up, dir2.normalized), side) > 0f ? -1f : 1f);
			DigTunnel b = DigTunnel.TestStroke(t, new List<Vector3> { from, from + dir2 * 8f });
			yield return null;
			int n1 = tun.PointCount;
			Check(ref ok, b == tun && tun.Strokes.Count == 2 && n1 > n0 + 5, "a branch dug from the passage: " + (b == tun ? "the same tunnel" : "a new tunnel") + ", " + tun.Strokes.Count + " strokes, " + n0 + " -> " + n1 + " points");
			Vector3 branchEnd = DigLine(tun, 1).Last();
			DigTunnel f = DigTunnel.TestStroke(t, new List<Vector3> { branchEnd, branchEnd }, true);
			yield return null;
			int n2 = tun.PointCount;
			Check(ref ok, f == tun && n2 < n1, "Shift: the branch's end filled in (" + n1 + " -> " + n2 + " points)");
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			Check(ref ok, tun.PointCount == n1, "undo the fill (" + tun.PointCount + " points)");
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			DigTunnel.EditorNow(t);
			Check(ref ok, tun.PointCount == n0 && tun.Strokes.Count == 1 && tun.gameObject.activeSelf, "undo the branch (" + tun.PointCount + " points, " + tun.Strokes.Count + " stroke)");

			// Saved and loaded with the island; a new island leaves no holes behind
			int tri0 = tun.Triangles;
			Vector3 at0 = tun.transform.position;
			Check(ref ok, DynamicIslands.SaveIsland(DigIsland), "saved as " + DigIsland);
			DynamicIslands.NewIsland();
			yield return null;
			yield return new WaitForSecondsRealtime(0.7f);
			int left = terraineditor.terrain != null ? OpenCells(terraineditor.terrain.terrainData) : -1;
			Check(ref ok, left == 0, "a new island: no holes left in the ground (" + left + ")");
			DynamicIslands.LoadIsland(DigIsland);
			yield return null;
			yield return null;
			yield return new WaitForSecondsRealtime(0.7f);
			// (11-09: a Raft just started and busy loaded slower than 0.7 s - "none" and no island; wait up to 10 s for it)
			DigTunnel back = null;
			for (float w = 0f; w < 10f; w += 0.25f)
			{
				GameObject po = GameObject.Find("PlacedObjects");
				back = po != null ? po.GetComponentsInChildren<DigTunnel>(false).FirstOrDefault() : null;
				if (back != null && back.Triangles > 0) break;
				yield return new WaitForSecondsRealtime(0.25f);
			}
			t = terraineditor.terrain;
			int cutBack = DigTunnel.EditorNow(t);
			Check(ref ok, back != null && back.PointCount == n0 && back.Triangles > 0 && Mathf.Abs(back.Triangles - tri0) < tri0 / 20 + 10 && (back.transform.position - at0).magnitude < 0.05f && cutBack == cut.Count,
				"loaded again: the tunnel where it was (" + (back != null ? back.PointCount + " points, " + back.Triangles + " triangles (" + tri0 + " before), " : "none, ") + cutBack + " cells cut, " + cut.Count + " before)");
			Check(ref ok, back == null || back.transform.Find(DigTunnel.RockName) != null && back.GetComponent<EditorGameObject>() != null, "loaded: an editor object with its lining");

			// A cliff: flat ground raised 10 m behind a steep face (painted by slope, as the islands are), a tunnel dug into its foot
			{
				data = t.terrainData;
				o = t.transform.position;
				Vector3 cliff = Vector3.zero;
				bool flat = false;
				for (float r = 20f; r < 110f && !flat; r += 6f)
					for (int k = 0; k < 16 && !flat; k++)
					{
						float an = k * Mathf.PI / 8f;
						Vector3 p = ground(c + new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an)) * r);
						if (p.y < sea + 2f || (p - mouth).magnitude < 45f || (p - end).magnitude < 30f) continue;
						float lo = float.MaxValue, hi = float.MinValue;
						for (int gx = -1; gx <= 4; gx++)
							for (int gz = -2; gz <= 2; gz++)
							{
								float y = ground(p + new Vector3(gx * 6f, 0f, gz * 6f)).y;
								lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
							}
						if (hi - lo < 2.5f && lo > sea + 1.5f) { cliff = p; flat = true; }
					}
				Check(ref ok, flat, "a flat place for a cliff" + (flat ? " " + (cliff.y - sea).ToString("0.0") + " m above the sea" : ""));
				if (flat)
				{
					int res = data.heightmapResolution;
					Vector3 size = data.size;
					int hx0 = Mathf.Clamp(Mathf.FloorToInt((cliff.x - 3f - o.x) / size.x * (res - 1)), 0, res - 1), hx1 = Mathf.Clamp(Mathf.CeilToInt((cliff.x + 26f - o.x) / size.x * (res - 1)), 0, res - 1);
					int hz0 = Mathf.Clamp(Mathf.FloorToInt((cliff.z - 16f - o.z) / size.z * (res - 1)), 0, res - 1), hz1 = Mathf.Clamp(Mathf.CeilToInt((cliff.z + 16f - o.z) / size.z * (res - 1)), 0, res - 1);
					float[,] h0 = data.GetHeights(hx0, hz0, hx1 - hx0 + 1, hz1 - hz0 + 1);
					var h1 = (float[,])h0.Clone();
					for (int z = 0; z <= hz1 - hz0; z++)
						for (int x = 0; x <= hx1 - hx0; x++)
						{
							float wx = o.x + (hx0 + x) * size.x / (res - 1), wz = o.z + (hz0 + z) * size.z / (res - 1);
							float d = wx - cliff.x, lat = Mathf.Abs(wz - cliff.z);
							float k = Mathf.SmoothStep(0f, 1f, d / 1.5f) * Mathf.SmoothStep(0f, 1f, (14f - lat) / 1.5f) * Mathf.SmoothStep(0f, 1f, (24f - d) / 6f);
							h1[z, x] += 10f * k / size.y;
						}
					data.SetHeights(hx0, hz0, h1);
					TerrainPainter.PaintWorldArea(t, sea, cliff + new Vector3(-4f, 0f, -17f), cliff + new Vector3(27f, 0f, 17f));
					yield return null;
					Vector3 foot = ground(cliff + Vector3.left * 1f);
					DigTunnel ct = DigTunnel.TestStroke(t, new List<Vector3> { foot, foot + Vector3.right * 16f });
					yield return null;
					DigTunnel.EditorNow(t);
					HashSet<Vector2Int> cliffCut = new HashSet<Vector2Int>();
					if (ct != null) ct.CellsToCut(t, cliffCut);
					Check(ref ok, ct != null && ct != back && ct.PointCount > 10 && cliffCut.Count > 0, "a tunnel dug into the cliff's face: " + (ct != null ? ct.PointCount + " points, " + cliffCut.Count + " cells cut" : "none"));
					if (ct != null)
					{
						int sunken;
						string cover = RimCover(ct, t, cliffCut, out sunken);
						int cshards = ct.RimTop == "" ? 0 : ct.RimShards(out _);
						Check(ref ok, ct.RimColumns > 0 && sunken == 0 && cshards == 0, "the cliff mouth's rim: " + RimLook(ct) + "; the cut cells: " + cover + "; " + cshards + " rock shards on the ground" + (cshards > 0 ? " (" + RimShardsAt(ct) + ")" : ""));
						// (11-11 w03: on the cliff's steep face the rim wears the ground's look too - the cliff is the ground's own; the
						// hill's rock there was a dark or tan patch with a toothed outline round the mouth)
						string steepAt;
						int steep = ct.RimSteepTop(out steepAt);
						Check(ref ok, steep > 0, "the cliff mouth's rim on the steep face: " + steep + " faces wearing the ground's look" + (steep > 0 ? " (" + steepAt + ")" : ""));
						// (11-09 w03: and the rim walls draw no sandy top texture on their up-turned faces - SideRockOnly)
						{
							Transform crt = ct.transform.Find(DigTunnel.RockName);
							MeshRenderer cmr = crt != null ? crt.GetComponent<MeshRenderer>() : null;
							Material[] cms = cmr != null ? cmr.sharedMaterials : new Material[0];
							string topLook = "no rim walls";
							bool sideOnly = false;
							if (cms.Length > 1 && cms[1] != null)
							{
								if (!cms[1].HasProperty("_TopDiffuse")) { sideOnly = true; topLook = "a material without a top texture (" + cms[1].shader.name + ")"; }
								else
								{
									var cb = new MaterialPropertyBlock();
									cmr.GetPropertyBlock(cb, 1);
									Texture top = cb.isEmpty ? null : cb.GetTexture("_TopDiffuse"), sideTex = cb.isEmpty ? null : cb.GetTexture("_SideDiffuse");
									if (sideTex == null) sideTex = cms[1].GetTexture("_SideDiffuse");
									sideOnly = top != null && top == sideTex;
									topLook = "top " + (top != null ? top.name : "the material's " + (cms[1].GetTexture("_TopDiffuse") != null ? cms[1].GetTexture("_TopDiffuse").name : "none")) + ", side " + (sideTex != null ? sideTex.name : "none");
								}
							}
							Check(ref ok, sideOnly, "the cliff mouth's rim walls wear the side rock on top too: " + topLook);
						}
						{
							var cseen = new List<string>();
							int looked, looked2;
							int front = SeeThrough(ct, t, cliffCut, new[] { foot + Vector3.left * 11f + Vector3.up * 3f, foot + Vector3.left * 5f + Vector3.forward * 7f + Vector3.up * 2f, foot + Vector3.left * 5f + Vector3.back * 7f + Vector3.up * 2f, foot + Vector3.left * 8f + Vector3.up * 9f }, cseen, out looked);
							int above = SeeThrough(ct, t, cliffCut, null, cseen, out looked2);
							Check(ref ok, front == 0 && above == 0 && looked > 0, "the cliff mouth from outside: " + front + " of " + looked + " looks from in front and " + above + " of " + looked2 + " from over it see through the cliff" + (cseen.Count > 0 ? ": " + string.Join("; ", cseen.ToArray()) : ""));
						}
						List<Vector3> cl = DigLine(ct);
						var ceyes = new List<Vector3>();
						for (int i = 2; i < cl.Count; i += 4) ceyes.Add(cl[i] + Vector3.down * (DigTunnel.CentreUp * DigTunnel.Radius) + Vector3.up * 1.6f);
						var seen = new List<string>();
						int under;
						int leaks = DigLeaks(ct, t, ceyes, seen, out under);
						Check(ref ok, leaks == 0 && under == 0, "the cliff tunnel from inside (" + ceyes.Count + " places): " + leaks + " looks out through the rock, " + under + " to the ground from below" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));
						yield return CameraShot(foot + Vector3.left * 11f + Vector3.up * 3f, foot + Vector3.up * 2f, "digtunnel_cliff_mouth");
						yield return CameraShot(foot + Vector3.left * 5f + Vector3.forward * 7f + Vector3.up * 2f, foot + Vector3.up * 1.5f, "digtunnel_cliff_side");
						CommandUndoRedo.UndoRedoManager.Undo();
						yield return null;
					}
					data.SetHeights(hx0, hz0, h0);
					TerrainPainter.PaintWorldArea(t, sea, cliff + new Vector3(-4f, 0f, -17f), cliff + new Vector3(27f, 0f, 17f));
					DigTunnel.EditorNow(t);
				}
			}
			// Dug down: a level stroke over flat ground goes down into it on a walkable ramp until it has rock over it, then on
			// level; the ground cut only at the ramp's top (11-02)
			{
				data = t.terrainData;
				o = t.transform.position;
				Vector3 flatAt = Vector3.zero;
				bool flat = false;
				for (float r = 24f; r < 110f && !flat; r += 6f)
					for (int k = 0; k < 16 && !flat; k++)
					{
						float an = (k + 0.5f) * Mathf.PI / 8f;
						Vector3 p = ground(c + new Vector3(Mathf.Cos(an), 0f, Mathf.Sin(an)) * r);
						if (p.y < sea + 2.5f || (p - mouth).magnitude < 45f || (p - end).magnitude < 30f) continue;
						float lo = float.MaxValue, hi = float.MinValue;
						for (int gx = -1; gx <= 4; gx++)
							for (int gz = -1; gz <= 1; gz++)
							{
								float y = ground(p + new Vector3(gx * 5f, 0f, gz * 5f)).y;
								lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
							}
						if (hi - lo < 1.5f && lo > sea + 2f) { flatAt = p; flat = true; }
					}
				Check(ref ok, flat, "a flat place to dig down" + (flat ? " " + (flatAt.y - sea).ToString("0.0") + " m above the sea" : ""));
				if (flat)
				{
					DigTunnel.Slope = 0f;
					DigTunnel dt = DigTunnel.TestStroke(t, new List<Vector3> { flatAt, ground(flatAt + Vector3.right * 18f) });
					yield return null;
					DigTunnel.EditorNow(t);
					HashSet<Vector2Int> downCut = new HashSet<Vector2Int>();
					if (dt != null) dt.CellsToCut(t, downCut);
					Check(ref ok, dt != null && dt.PointCount > 10 && downCut.Count > 0, "dug down: " + (dt != null ? dt.PointCount + " points, " + downCut.Count + " cells cut" : "none"));
					if (dt != null && dt.PointCount > 10)
					{
						List<DigTunnel.Point> s0 = dt.Strokes[0];
						Vector3 endAt = dt.transform.TransformPoint(s0[s0.Count - 1].At);
						float roofCover = ground(endAt).y - DigTunnel.Under - (endAt.y + (DigTunnel.CentreUp + 1f) * s0[s0.Count - 1].R);
						float steepest = 0f;
						for (int i = 1; i < s0.Count; i++)
						{
							Vector3 a = dt.transform.TransformPoint(s0[i - 1].At), b2 = dt.transform.TransformPoint(s0[i].At);
							float run = new Vector2(b2.x - a.x, b2.z - a.z).magnitude;
							if (run > 0.01f) steepest = Mathf.Max(steepest, Mathf.Atan2(Mathf.Abs(b2.y - a.y), run) * Mathf.Rad2Deg);
						}
						Check(ref ok, roofCover > DigTunnel.DownCover - 0.1f && ground(endAt).y - endAt.y > 3.5f && steepest <= DigTunnel.MaxSlope + 0.5f,
							"down into the ground: the end's floor " + (ground(endAt).y - endAt.y).ToString("0.0") + " m under the ground, " + roofCover.ToString("0.0") + " m of rock over its roof, the ramp at most " + steepest.ToString("0") + " degrees");
						// walkable both ways (the way out is the ramp)
						List<Vector3> dl = DigLine(dt);
						int dNoFloor = 0, dLow = 0;
						float dStep = 0f, dLast = float.NaN;
						var deyes = new List<Vector3>();
						var dBad = new List<string>();
						for (int i = 0; i < dl.Count; i++)
						{
							RaycastHit fh;
							if (!DigFloor(dl[i], out fh)) { dNoFloor++; dBad.Add("no floor at " + i); continue; }
							if (!IsDigRock(fh.collider) && !(fh.collider is TerrainCollider)) { dNoFloor++; dBad.Add(fh.collider.name + " at " + i); continue; }
							if (!float.IsNaN(dLast)) dStep = Mathf.Max(dStep, Mathf.Abs(fh.point.y - dLast));
							dLast = fh.point.y;
							RaycastHit up;
							Physics.queriesHitBackfaces = true;
							bool roof = Physics.Raycast(fh.point + Vector3.up * 0.1f, Vector3.up, out up, 10f, ~0, QueryTriggerInteraction.Ignore);
							Physics.queriesHitBackfaces = false;
							if (roof && up.distance + 0.1f < 2f) { dLow++; dBad.Add("room " + (up.distance + 0.1f).ToString("0.0") + " m at " + i); }
							if (i % 4 == 2) deyes.Add(fh.point + Vector3.up * 1.6f);
						}
						Check(ref ok, dNoFloor == 0 && dLow == 0 && dStep < 0.6f, "the ramp down walkable: " + dl.Count + " points, " + dNoFloor + " without a floor, " + dLow + " too low, the biggest step " + dStep.ToString("0.00") + " m" + (dBad.Count > 0 ? ": " + string.Join(", ", dBad.Take(6).ToArray()) : ""));
						// the cut: only where the ramp meets the ground, none over its covered part
						int buried = 0;
						float farCut = 0f;
						Vector3 top = dt.transform.TransformPoint(s0[0].At);
						foreach (Vector2Int q in downCut)
						{
							float bestD = float.MaxValue;
							for (int a = 0; a <= 2; a++)
								for (int b2 = 0; b2 <= 2; b2++)
								{
									Vector3 w = ground(new Vector3(o.x + (q.x + a * 0.5f) * cellSize, 0f, o.z + (q.y + b2 * 0.5f) * cellSize));
									bestD = Mathf.Min(bestD, dt.Distance(dt.transform.InverseTransformPoint(w)));
								}
							if (bestD > 1.5f) buried++;
							farCut = Mathf.Max(farCut, new Vector2(o.x + (q.x + 0.5f) * cellSize - top.x, o.z + (q.y + 0.5f) * cellSize - top.z).magnitude);
						}
						float rampRun = new Vector2(endAt.x - top.x, endAt.z - top.z).magnitude;
						Check(ref ok, buried == 0 && farCut < rampRun - 2f, "the ground cut at the ramp's top only: " + downCut.Count + " cells, the farthest " + farCut.ToString("0.0") + " m on (the passage " + rampRun.ToString("0.0") + " m long), " + buried + " over a buried part");
						int sunken;
						string cover = RimCover(dt, t, downCut, out sunken);
						Check(ref ok, sunken == 0, "the ramp's rim: the cut cells: " + cover);
						{
							var rseen = new List<string>();
							int looked, looked2;
							int front = SeeThrough(dt, t, downCut, new[] { top + Vector3.left * 9f + Vector3.up * 4f, top + Vector3.left * 4f + Vector3.forward * 7f + Vector3.up * 3f, top + Vector3.right * 10f + Vector3.up * 6f }, rseen, out looked);
							int above = SeeThrough(dt, t, downCut, null, rseen, out looked2);
							Check(ref ok, front == 0 && above == 0 && looked > 0, "the ramp from outside: " + front + " of " + looked + " looks from round it and " + above + " of " + looked2 + " from over it see through the ground" + (rseen.Count > 0 ? ": " + string.Join("; ", rseen.ToArray()) : ""));
						}
						var seen = new List<string>();
						int under;
						int leaks = DigLeaks(dt, t, deyes, seen, out under);
						Check(ref ok, leaks == 0 && under == 0, "the ramp from inside (" + deyes.Count + " places): " + leaks + " looks out through the rock, " + under + " to the ground from below" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));
						yield return CameraShot(top + Vector3.left * 9f + Vector3.forward * 4f + Vector3.up * 5f, top + Vector3.right * 4f, "digtunnel_down_top");
						yield return CameraShot(dl[dl.Count - 1] + Vector3.down * 0.3f, dl[0], "digtunnel_down_inside");
					}
					if (dt != null) { CommandUndoRedo.UndoRedoManager.Undo(); yield return null; }
					DigTunnel.EditorNow(t);
				}
			}
			DigTunnel.Radius = radius0; DigTunnel.Slope = slope0;
			if (ok) Log("PASS: dig tunnel editor"); else Fail("dig tunnel editor");
		}

		class DigWalk { public bool Ok; public string Text = ""; }

		/// <summary>Walks the local player with Raft's controller from a dug tunnel's mouth along its first stroke to its end.</summary>
		static IEnumerator WalkDig(Network_Player player, DigTunnel tun, DigWalk res)
		{
			List<Vector3> line = DigLine(tun);
			var route = new List<Vector3>();
			for (int i = 2; i < line.Count; i += 3) route.Add(tun.transform.InverseTransformPoint(line[i]));
			route.Add(tun.transform.InverseTransformPoint(line[line.Count - 1]));
			RaycastHit fh;
			Vector3 start = line[0];
			if (DigFloor(line[0] + Vector3.up * 1f, out fh)) start = fh.point;
			PlayerMove.To(player, start + Vector3.up * 1.1f); // (as the cave tool's walk: 0.3 m up, the player dropped through the floor at once, 11-02)
			yield return new WaitForSeconds(1f);
			PersonController pc = player.PersonController;
			CharacterController cc = pc.controller;
			pc.SwitchControllerType(ControllerType.Ground);
			string stoppedAt = null, fell = null;
			int reached = 0;
			Vector3 lastGood = player.transform.position;
			foreach (Vector3 rel in route)
			{
				float stuck = 0f, total = 0f;
				Vector3 last = player.transform.position;
				while (ScFlat(player.transform.position, tun.transform.TransformPoint(rel)) > 0.7f)
				{
					KeepAlive(player);
					Vector3 d = tun.transform.TransformPoint(rel) - player.transform.position; d.y = 0f;
					Vector3 dirW = d.normalized;
					Look(player, Quaternion.LookRotation(dirW).eulerAngles.y, 0f);
					cc.Move(dirW * pc.normalSpeed * Time.deltaTime);
					Vector3 now = player.transform.position;
					stuck = ScFlat(now, last) < pc.normalSpeed * Time.deltaTime * 0.3f ? stuck + Time.deltaTime : 0f;
					last = now;
					total += Time.deltaTime;
					if (fell == null) fell = FallNote(player, tun, line, ref lastGood);
					if (stuck > 2.5f || total > 20f)
					{
						Collider bl = WhatBlocks(player, dirW);
						stoppedAt = "stopped " + ScFlat(now, tun.transform.TransformPoint(rel)).ToString("0.0") + " m short of point " + (reached + 1) + " (" + (bl != null ? bl.name : "nothing ahead") + ", grounded " + cc.isGrounded + ")";
						break;
					}
					yield return null;
				}
				if (stoppedAt != null) break;
				reached++;
			}
			if (fell != null) fell += " (the walk began " + (start.y - line[0].y).ToString("0.0") + " m from the line's start)";
			Vector3 endAt = tun.transform.TransformPoint(route.Last());
			float drop = endAt.y - DigTunnel.CentreUp * tun.Strokes[0][tun.Strokes[0].Count - 1].R - player.transform.position.y;
			res.Ok = stoppedAt == null && Mathf.Abs(drop) < 1.5f;
			res.Text = reached + " of " + route.Count + " points" + (stoppedAt != null ? ", " + stoppedAt : "") + (fell != null ? ", " + fell : "") + " (" + drop.ToString("0.0") + " m under the passage's floor at the end)";
		}

		/// <summary>A fall through the passage's floor while walking: where it began (the last place on the floor, how far
		/// along the line), what is under the player now and under the line there, and whether the ground there is cut.
		/// Null while on the floor.</summary>
		static string FallNote(Network_Player player, DigTunnel tun, List<Vector3> line, ref Vector3 lastGood)
		{
			Vector3 pp = player.transform.position;
			int near = 0;
			for (int i = 1; i < line.Count; i++) if (ScFlat(line[i], pp) < ScFlat(line[near], pp)) near = i;
			float floorY = line[near].y - DigTunnel.CentreUp * tun.Strokes[0][Mathf.Min(near, tun.Strokes[0].Count - 1)].R;
			if (pp.y > floorY - 0.6f) { lastGood = pp; return null; }
			if (pp.y > floorY - 1.5f) return null;
			System.Func<Vector3, string> under = from =>
			{
				RaycastHit[] all = Physics.RaycastAll(from, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
				foreach (RaycastHit h in all)
					if (h.collider.GetComponentInParent<Network_Player>() == null) return h.collider.name + " " + h.distance.ToString("0.0") + " m under";
				return "nothing";
			};
			string hole = "";
			Terrain land = tun.Land();
			if (land != null)
			{
				TerrainData td = land.terrainData;
				int res = td.holesResolution;
				Vector3 lp = lastGood - land.transform.position;
				int hx = Mathf.Clamp(Mathf.FloorToInt(lp.x / td.size.x * res), 0, res - 1), hz = Mathf.Clamp(Mathf.FloorToInt(lp.z / td.size.z * res), 0, res - 1);
				hole = ", the ground there " + (td.GetHoles(hx, hz, 1, 1)[0, 0] ? "whole" : "cut") + " (ground " + (land.SampleHeight(lastGood) + land.transform.position.y - floorY).ToString("0.0") + " m over the floor)";
			}
			int nearGood = 0;
			for (int i = 1; i < line.Count; i++) if (ScFlat(line[i], lastGood) < ScFlat(line[nearGood], lastGood)) nearGood = i;
			return "fell " + (floorY - pp.y).ToString("0.0") + " m near line point " + nearGood + " of " + line.Count + " (" + ScFlat(line[nearGood], lastGood).ToString("0.0") + " m off it, last on the floor at "
				+ (lastGood.y - floorY).ToString("0.0") + " m; under the player now: " + under(pp + Vector3.up * 0.5f) + "; under the line there: " + under(line[nearGood]) + hole + ")";
		}

		static IEnumerator DigTunnelWalkRoutine()
		{
			if (DynamicIslands.InEditor() || RAPI.GetLocalPlayer() == null) { Fail("dig tunnel walk: in a world"); yield break; }
			bool ok = true;
			DigTunnel tun = null;
			for (float w = 0f; w < 30f && tun == null; w += 1f)
			{
				tun = UnityEngine.Object.FindObjectsOfType<DigTunnel>().FirstOrDefault(t => t.PointCount >= 30 && t.gameObject.activeInHierarchy && t.Land() != null);
				if (tun == null) yield return new WaitForSeconds(1f);
			}
			if (tun == null) { Fail("dig tunnel walk: no dug tunnel in this world (CIDigTunnelWorld keep on the host first)"); yield break; }
			Terrain land = tun.Land();
			Transform rock = tun.transform.Find(DigTunnel.RockName);
			MeshCollider mc = rock != null ? rock.GetComponent<MeshCollider>() : null;
			MeshRenderer mr = rock != null ? rock.GetComponent<MeshRenderer>() : null;
			Check(ref ok, tun.Triangles > 1000 && mc != null && mc.sharedMesh != null && mc.enabled && mr != null && mr.enabled, "the tunnel is here (" + (Raft_Network.IsHost ? "host" : "player 2") + "): " + tun.PointCount + " points, " + tun.Triangles + " triangles, collider and renderer on");
			{
				// (the tunnel's own cells open, not LastWorldCut: a later cut on this PC - a player joining - finds them open and counts 0)
				var need = new HashSet<Vector2Int>();
				tun.CellsToCut(land, need);
				int res = land.terrainData.holesResolution;
				bool[,] solid = land.terrainData.GetHoles(0, 0, res, res);
				int open = need.Count(c => !solid[c.y, c.x]);
				Check(ref ok, need.Count > 0 && open == need.Count, "the ground cut over its mouth on this PC: " + open + " of " + need.Count + " cells");
			}
			Log("  the mouth's rim: " + RimLook(tun));
			Network_Player player = RAPI.GetLocalPlayer();
			var walk = new DigWalk();
			yield return WalkDig(player, tun, walk);
			Check(ref ok, walk.Ok, "walked from the mouth to the end: " + walk.Text);
			List<Vector3> line = DigLine(tun);
			RaycastHit fh;
			var eyes = new List<Vector3>();
			for (int i = 2; i < line.Count; i += 4)
				if (DigFloor(line[i], out fh) && IsDigRock(fh.collider)) eyes.Add(fh.point + Vector3.up * 1.6f);
			var seen = new List<string>();
			int under;
			Vector3 back = line[0] - (line[Mathf.Min(6, line.Count - 1)] - line[0]).normalized * 4f;
			if (Physics.Raycast(back + Vector3.up * 30f, Vector3.down, out fh, 60f, ~0, QueryTriggerInteraction.Ignore)) back = fh.point;
			PlayerMove.To(player, back + Vector3.up * 0.5f); // (out of the looks' way, before the mouth)
			int leaks = DigLeaks(tun, land, eyes, seen, out under);
			Check(ref ok, eyes.Count >= 6 && leaks == 0 && under == 0, "from inside (" + eyes.Count + " places): " + leaks + " looks out through the hill, " + under + " to the ground from below" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));
			if (ok) Log("PASS: dig tunnel walk"); else Fail("dig tunnel walk");
		}

		static IEnumerator DigTunnelWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || DynamicIslands.InEditor()) { Fail("dig tunnel world: in a world"); yield break; }
			string path = IslandSpawner.PathFor(DigIsland);
			if (!File.Exists(path)) { Fail("dig tunnel world: run CIDigTunnelEditor first (no " + DigIsland + ")"); yield break; }
			bool ok = true;
			Vector3 pos = raftPos.Value + Vector3.forward * 350f; pos.y = 0f;
			int before = IslandWorldState.Islands.Count;
			DigTunnel.LastWorldCut = 0;
			yield return DynamicIslands.instance.SpawnIslandFile(DigIsland, pos, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("dig tunnel world: the island did not spawn"); yield break; }
			yield return new WaitForSeconds(2f);
			DigTunnel tun = e.Root.GetComponentsInChildren<DigTunnel>(true).FirstOrDefault();
			Terrain land = tun != null ? tun.Land() : null;
			Check(ref ok, tun != null && tun.PointCount >= 30 && land != null, "the tunnel spawned with the island (" + (tun != null ? tun.PointCount + " points" : "none") + (land == null ? ", no ground found" : "") + ")");
			if (tun == null || land == null) { Fail("dig tunnel world"); yield break; }
			Transform rock = tun.transform.Find(DigTunnel.RockName);
			MeshCollider mc = rock != null ? rock.GetComponent<MeshCollider>() : null;
			MeshRenderer mr = rock != null ? rock.GetComponent<MeshRenderer>() : null;
			Check(ref ok, tun.Triangles > 1000 && mc != null && mc.sharedMesh != null && mc.enabled && mr != null && mr.enabled && tun.gameObject.activeInHierarchy, "its rock lining: " + tun.Triangles + " triangles, collider and renderer on (" + (mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name : "no material") + ")");
			Check(ref ok, DigTunnel.LastWorldCut > 0 && OpenCells(land.terrainData) >= DigTunnel.LastWorldCut, "the ground cut over its mouth: " + DigTunnel.LastWorldCut + " cells");

			Log("  the mouth's rim: " + RimLook(tun));

			// Walked from the mouth to its end with Raft's controller
			Network_Player player = RAPI.GetLocalPlayer();
			List<Vector3> line = DigLine(tun);
			Vector3 rel0 = line[0] - e.Position;
			RaycastHit fh;
			var walk = new DigWalk();
			yield return WalkDig(player, tun, walk);
			Check(ref ok, walk.Ok, "walked from the mouth to the end: " + walk.Text);

			// From inside: no look to the sky or the ground from below
			var eyes = new List<Vector3>();
			for (int i = 2; i < line.Count; i += 4)
				if (DigFloor(line[i], out fh) && IsDigRock(fh.collider)) eyes.Add(fh.point + Vector3.up * 1.6f);
			var seen = new List<string>();
			int under;
			PlayerMove.To(player, e.Position + rel0 + Vector3.up * 0.3f); // (out of the looks' way)
			int leaks = DigLeaks(tun, land, eyes, seen, out under);
			Check(ref ok, eyes.Count >= 6 && leaks == 0 && under == 0, "from inside (" + eyes.Count + " places, 81 looks each): " + leaks + " looks out through the hill, " + under + " to the ground from below" + (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(6).ToArray()) : ""));

			Vector3 out0 = line[0] - (line[Mathf.Min(6, line.Count - 1)] - line[0]).normalized * 12f; out0.y = line[0].y + 4f;
			yield return CameraShot(out0, line[0], "digtunnel_world_mouth");
			{
				Light sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsOfType<Light>().Where(l => l.type == LightType.Directional && l.enabled).OrderByDescending(l => l.intensity).FirstOrDefault();
				Log("  (light: ambient " + RenderSettings.ambientMode + " " + RenderSettings.ambientSkyColor + ", intensity " + RenderSettings.ambientIntensity.ToString("0.00") + ", reflections " + RenderSettings.reflectionIntensity.ToString("0.00") + "; sun " + (sun != null ? sun.name + " " + sun.color + " x" + sun.intensity.ToString("0.00") + ", shadows " + sun.shadows + ", lights the rock's layer " + ((sun.cullingMask & (1 << rock.gameObject.layer)) != 0) + ", the ground's " + ((sun.cullingMask & (1 << land.gameObject.layer)) != 0) : "none") + "; rock layer " + LayerMask.LayerToName(rock.gameObject.layer) + ", ground layer " + LayerMask.LayerToName(land.gameObject.layer) + "; the rock casts " + mr.shadowCastingMode + ", receives " + mr.receiveShadows + ", probes " + mr.lightProbeUsage + "/" + mr.reflectionProbeUsage + "; the ground probes " + land.reflectionProbeUsage + "; camera " + (Camera.main != null ? Camera.main.actualRenderingPath.ToString() : "none") + "; QualitySettings shadows " + QualitySettings.shadows + ")");
			}
			yield return CameraShot(line[2] + Vector3.down * 0.3f, line[Mathf.Min(line.Count - 1, 22)], "digtunnel_world_inside");
			yield return CameraShot(line[line.Count - 1] + Vector3.down * 0.3f, line[Mathf.Max(0, line.Count - 14)], "digtunnel_world_back");

			if (args0Keep) Log("  (island kept: " + DigIsland + ")");
			else
			{
				IslandWorldState.RemoveIds(new[] { e.Id }, true);
				yield return null;
				try { File.Delete(path); } catch { }
			}
			if (ok) Log("PASS: dig tunnel world"); else Fail("dig tunnel world");
		}

		[ConsoleCommand(name: "CIDigLookProbe", docs: "Dev, editor or world: the first dug tunnel's lining tried with a light trick, pictures before and after (CIDigLookProbe vc|sh|none): vc vertex colours black, sh its own black ambient (a custom light probe); then put back")]
		public static void DigLookProbeCommand(string[] args) { StartTest(DigLookProbeRoutine(args.Length > 0 ? args[0] : "none")); }

		static IEnumerator DigLookProbeRoutine(string mode)
		{
			DigTunnel tun = Object.FindObjectsOfType<DigTunnel>().FirstOrDefault(x => x.PointCount > 10);
			if (tun == null) { Fail("dig look probe: no tunnel"); yield break; }
			List<Vector3> line = DigLine(tun);
			MeshRenderer mr = tun.transform.Find(DigTunnel.RockName).GetComponent<MeshRenderer>();
			Mesh mesh = mr.GetComponent<MeshFilter>().sharedMesh;
			Vector3 from = line[2] + Vector3.down * 0.3f, to = line[Mathf.Min(line.Count - 1, 22)];
			Vector3 out0 = line[0] - (line[Mathf.Min(6, line.Count - 1)] - line[0]).normalized * 6f; out0.y = line[0].y + 1.5f;
			yield return CameraShot(from, to, "digprobe_" + mode + "_0");
			yield return CameraShot(out0, line[Mathf.Min(line.Count - 1, 10)], "digprobe_" + mode + "_out0");
			var cols = new List<Color>(); mesh.GetColors(cols);
			if (mode == "vc") { var c = new Color[mesh.vertexCount]; for (int i = 0; i < c.Length; i++) c[i] = (i / 64) % 2 == 0 ? Color.black : Color.white; mesh.colors = c; }
			UnityEngine.Rendering.LightProbeUsage lpu = mr.lightProbeUsage;
			var saved = new MaterialPropertyBlock(); mr.GetPropertyBlock(saved);
			DigTunnel.LiningLightOff = true;
			if (mode == "amb" || mode == "gi" || mode == "none")
			{
				// (what the lining's light is when left to Unity, and the two places a light probe could be taken from)
				for (int i = 0; i < 4; i++) mr.SetPropertyBlock(null, i);
				mr.SetPropertyBlock(null);
				mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
				yield return null;
				yield return CameraShot(from, to, "digprobe_" + mode + "_base");
				UnityEngine.Rendering.SphericalHarmonicsL2 amb = RenderSettings.ambientProbe, gi;
				LightProbes.GetInterpolatedProbe(mr.bounds.center, mr, out gi);
				System.Func<UnityEngine.Rendering.SphericalHarmonicsL2, string> dc = h => h[0, 0].ToString("0.00") + "," + h[1, 0].ToString("0.00") + "," + h[2, 0].ToString("0.00");
				Log("  ambient mode " + RenderSettings.ambientMode + " intensity " + RenderSettings.ambientIntensity + " sky " + RenderSettings.ambientSkyColor + " eq " + RenderSettings.ambientEquatorColor + " ground " + RenderSettings.ambientGroundColor
					+ "; probe dc " + dc(amb) + "; interpolated dc " + dc(gi) + "; probes " + (LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : -1)
					+ "; SHAr " + Shader.GetGlobalVector("unity_SHAr") + " SHAb " + Shader.GetGlobalVector("unity_SHAb"));
				Shader sd = mr.sharedMaterial.shader; var props = new List<string>();
				for (int i = 0; i < sd.GetPropertyCount(); i++) if (sd.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color || sd.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Float || sd.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Range) props.Add(sd.GetPropertyName(i));
				Log("  lining props " + string.Join(" ", props));
				if (mode != "none")
				{
					mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.CustomProvided;
					var mpb = new MaterialPropertyBlock(); mr.GetPropertyBlock(mpb);
					mpb.CopySHCoefficientArraysFrom(new[] { mode == "amb" ? amb : gi });
					mr.SetPropertyBlock(mpb);
				}
			}
			if (mode == "sh")
			{
				mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.CustomProvided;
				var mpb = new MaterialPropertyBlock();
				mr.GetPropertyBlock(mpb);
				mpb.CopySHCoefficientArraysFrom(new[] { new UnityEngine.Rendering.SphericalHarmonicsL2() });
				mr.SetPropertyBlock(mpb);
			}
			yield return null;
			yield return CameraShot(from, to, "digprobe_" + mode + "_1");
			yield return CameraShot(out0, line[Mathf.Min(line.Count - 1, 10)], "digprobe_" + mode + "_out1");
			Log("  shader " + mr.sharedMaterial.shader.name + ", colours before " + cols.Count + ", probes " + lpu);
			if (mode == "vc") mesh.SetColors(cols);
			if (mode == "sh" || mode == "amb" || mode == "gi") { mr.lightProbeUsage = lpu; mr.SetPropertyBlock(saved); }
			DigTunnel.LiningLightOff = false;
			Log("PASS: dig look probe " + mode);
		}

		[ConsoleCommand(name: "CIDigRimProbe", docs: "Dev, world or editor: every dug tunnel's mouths within 2000 m - what each part of the lining wears (shader, texture, block), the rim top's baked texture (mean colour, share of the plain grey a bake leaves where no layer weighs), the ground's splat weights there; pictures of each mouth as drawn, with the parts in flat colours (lining green, rim walls blue, rim top magenta) and with the ground hidden (dig_rimprobe_<n>_<a|b|c>)")]
		public static void DigRimProbeCommand(string[] args) { StartTest(DigRimProbeRoutine()); }

		static IEnumerator DigRimProbeRoutine()
		{
			Vector3 me = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
			var tuns = Object.FindObjectsOfType<DigTunnel>().Where(x => x.PointCount > 0 && x.Openings.Count > 0 && x.transform.Find(DigTunnel.RockName) != null).ToList();
			var all = Resources.FindObjectsOfTypeAll<DigTunnel>().Where(x => x.gameObject.scene.IsValid()).ToList();
			Log("  " + all.Count + " dug tunnels in the scene (" + all.Count(x => x.gameObject.activeInHierarchy) + " active), " + tuns.Count + " with mouths and a lining; camera at " + me.ToString("F0"));
			foreach (DigTunnel x in all.Take(12)) Log("    " + (x.transform.parent != null ? x.transform.parent.name + "/" : "") + x.name + " at " + x.transform.position.ToString("F0") + ": points " + x.PointCount + ", mouths " + x.Openings.Count + ", lining " + (x.transform.Find(DigTunnel.RockName) != null) + ", active " + x.gameObject.activeInHierarchy);
			int n = 0;
			foreach (DigTunnel t in tuns)
			{
				MeshRenderer mr = t.transform.Find(DigTunnel.RockName).GetComponent<MeshRenderer>();
				Mesh mesh = mr.GetComponent<MeshFilter>().sharedMesh;
				Terrain land = t.SkinLand;
				if (mesh == null || land == null) continue;
				Vector3 lo = land.transform.position, size = land.terrainData.size;
				// (the mouths: the cut cells in clusters 6 m apart)
				var groups = new List<List<Vector2>>();
				foreach (Vector2 q in t.Openings)
				{
					List<Vector2> g = groups.FirstOrDefault(gr => gr.Any(p => (p - q).sqrMagnitude < 36f));
					if (g == null) groups.Add(g = new List<Vector2>());
					g.Add(q);
				}
				var parts = new List<string>();
				Material[] ms = mr.sharedMaterials;
				for (int i = 0; i < mesh.subMeshCount; i++)
				{
					Material m = i < ms.Length ? ms[i] : null;
					var b = new MaterialPropertyBlock(); mr.GetPropertyBlock(b, i);
					Texture bt = b.isEmpty ? null : b.GetTexture("_MainTex");
					parts.Add("part " + i + ": " + mesh.GetTriangles(i).Length / 3 + " faces, " + (m == null ? "none" : m.shader.name + " main " + (m.HasProperty("_MainTex") && m.mainTexture != null ? m.mainTexture.name : "-") + " colour " + (m.HasProperty("_Color") ? m.color.ToString() : "-")
						+ (bt != null ? " block main " + bt.name : "") + (m.HasProperty("_TopDiffuse") ? " top " + (b.isEmpty || b.GetTexture("_TopDiffuse") == null ? (m.GetTexture("_TopDiffuse") != null ? m.GetTexture("_TopDiffuse").name : "-") : b.GetTexture("_TopDiffuse").name) : "")));
				}
				Log("  tunnel " + t.transform.parent.name + "/" + t.name + " at " + t.transform.position.ToString("F1") + ": " + groups.Count + " mouths, " + t.Openings.Count + " cells cut, " + t.RimColumns + " rim columns, top " + (t.RimTop == "" ? "rock" : t.RimTop) + "; " + string.Join("; ", parts.ToArray()));
				if (ms.Length > 2 && ms[2] != null && ms[2].HasProperty("_MainTex") && ms[2].mainTexture != null)
				{
					Texture2D c = DigTunnel.Readable(ms[2].mainTexture, 64);
					if (c != null)
					{
						Color32[] px = c.GetPixels32();
						float r = 0f, g = 0f, bl = 0f; int grey = 0;
						foreach (Color32 p in px) { r += p.r; g += p.g; bl += p.b; if (Mathf.Abs(p.r - 128) < 3 && Mathf.Abs(p.g - 128) < 3 && Mathf.Abs(p.b - 128) < 3) grey++; }
						Log("    rim top's baked texture " + ms[2].mainTexture.width + "x" + ms[2].mainTexture.height + ": mean " + (r / px.Length).ToString("0") + "," + (g / px.Length).ToString("0") + "," + (bl / px.Length).ToString("0") + ", plain grey " + (100f * grey / px.Length).ToString("0") + "%, scale " + ms[2].mainTextureScale + " offset " + ms[2].mainTextureOffset);
					}
				}
				if (ms.Length > 2 && ms[2] != null && ms[2].mainTexture != null)
				{
					// (the baked texture itself, as a file; the rim top's block; how steep its faces are against the ground under them)
					Texture2D full = DigTunnel.Readable(ms[2].mainTexture, Mathf.Min(2048, ms[2].mainTexture.width));
					if (full != null) System.IO.File.WriteAllBytes(System.IO.Path.Combine(DynamicIslands.assetpath, "shot_dig_rimprobe_bake_" + tuns.IndexOf(t) + ".png"), full.EncodeToPNG());
					var blk = new MaterialPropertyBlock(); mr.GetPropertyBlock(blk, 2);
					Log("    rim top's block: " + (blk.isEmpty ? "empty" : "colour " + blk.GetColor("_Color") + ", main " + (blk.GetTexture("_MainTex") != null ? blk.GetTexture("_MainTex").name : "-")) + "; terrain size " + size);
					var tv = new List<Vector3>(); mesh.GetVertices(tv);
					int[] tt = mesh.GetTriangles(2);
					float area = 0f, steepF = 0f, steepG = 0f, over = 0f;
					for (int k = 0; k + 2 < tt.Length; k += 3)
					{
						Vector3 wa = mr.transform.TransformPoint(tv[tt[k]]), wb = mr.transform.TransformPoint(tv[tt[k + 1]]), wc = mr.transform.TransformPoint(tv[tt[k + 2]]);
						Vector3 cr = Vector3.Cross(wb - wa, wc - wa); float ar2 = cr.magnitude * 0.5f; if (ar2 < 1e-6f) continue;
						Vector3 mid = (wa + wb + wc) / 3f;
						Vector3 gn = land.terrainData.GetInterpolatedNormal((mid.x - lo.x) / size.x, (mid.z - lo.z) / size.z);
						area += ar2; steepF += ar2 * Vector3.Angle(cr, Vector3.up); steepG += ar2 * Vector3.Angle(gn, Vector3.up);
						over += ar2 * (mid.y - land.SampleHeight(mid) - lo.y);
					}
					if (area > 0f) Log("    rim top: " + area.ToString("0.0") + " m2, its faces " + (steepF / area).ToString("0") + " deg steep against the ground's " + (steepG / area).ToString("0") + " deg under them, " + (over / area).ToString("0.00") + " m over it");
				}
				TerrainLayer[] layers = land.terrainData.terrainLayers;
				Log("    ground " + land.name + " shader " + (land.materialTemplate != null ? land.materialTemplate.shader.name : "built-in") + ", layers " + string.Join(", ", layers.Select(l => l == null ? "null" : (l.diffuseTexture != null ? l.diffuseTexture.name + " " + l.diffuseTexture.width : "NO TEXTURE") + " tile " + l.tileSize.x).ToArray())
					+ ", pixel error " + land.heightmapPixelError + ", basemap " + land.basemapDistance);
				foreach (List<Vector2> g in groups)
				{
					Vector2 c2 = Vector2.zero; foreach (Vector2 q in g) c2 += q; c2 /= g.Count;
					var at = new Vector3(c2.x, 0f, c2.y); at.y = land.SampleHeight(at) + lo.y;
					if ((at - me).magnitude > 2000f) continue;
					int ar = land.terrainData.alphamapResolution;
					int ax = Mathf.Clamp(Mathf.FloorToInt((c2.x - 3f - lo.x) / size.x * ar), 0, ar - 1), az = Mathf.Clamp(Mathf.FloorToInt((c2.y - 3f - lo.z) / size.z * ar), 0, ar - 1);
					int aw = Mathf.Clamp(Mathf.CeilToInt(6f / size.x * ar), 1, ar - ax), ah = Mathf.Clamp(Mathf.CeilToInt(6f / size.z * ar), 1, ar - az);
					float[,,] w = land.terrainData.GetAlphamaps(ax, az, aw, ah);
					var mean = new float[w.GetLength(2)];
					for (int z = 0; z < ah; z++) for (int x = 0; x < aw; x++) for (int l = 0; l < mean.Length; l++) mean[l] += w[z, x, l] / (aw * ah);
					Vector3 nrm = land.terrainData.GetInterpolatedNormal((c2.x - lo.x) / size.x, (c2.y - lo.z) / size.z);
					Vector3 outw = new Vector3(nrm.x, 0f, nrm.z);
					if (outw.magnitude < 0.2f) { List<Vector3> line = DigLine(t); Vector3 mid = line[line.Count / 2]; outw = at - mid; outw.y = 0f; }
					outw.Normalize();
					Vector3 eye = at + outw * 9f + Vector3.up * 3f; eye.y = Mathf.Max(eye.y, land.SampleHeight(eye) + lo.y + 1.5f);
					Log("    mouth " + n + " at " + at.ToString("F1") + ": " + g.Count + " cells, slope " + Vector3.Angle(nrm, Vector3.up).ToString("0") + " deg, splat " + string.Join(" ", mean.Select(v => v.ToString("0.00")).ToArray()) + "; eye " + eye.ToString("F1"));
					Vector3 look = at + Vector3.up * 1f;
					yield return CameraShot(eye, look, "dig_rimprobe_" + n + "_a");
					var flat = new Material[ms.Length];
					Color[] cols = { Color.green, Color.blue, Color.magenta };
					Shader unlit = Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/Internal-Colored");
					if (unlit == null) { Log("    (no flat shader in this build: no b/c pictures)"); }
					else
					{
						for (int i = 0; i < ms.Length; i++) { flat[i] = new Material(unlit); flat[i].color = cols[Mathf.Min(i, 2)]; }
						mr.sharedMaterials = flat;
						yield return CameraShot(eye, look, "dig_rimprobe_" + n + "_b");
						land.drawHeightmap = false;
						yield return CameraShot(eye, look, "dig_rimprobe_" + n + "_c");
						land.drawHeightmap = true;
						mr.sharedMaterials = ms;
						foreach (Material f in flat) if (f != null) Object.Destroy(f);
					}
					// (d: the rim top's albedo black - what is left is the Standard shader's own shine, the sky's reflection and
					// the sun's highlight; e: the rim top on the legacy diffuse shader the ground wears, its baked texture kept)
					if (ms.Length > 2 && ms[2] != null)
					{
						var dark = new Material(ms[2]); dark.color = Color.black;
						Material[] m2 = (Material[])ms.Clone(); m2[2] = dark; mr.sharedMaterials = m2;
						yield return CameraShot(eye, look, "dig_rimprobe_" + n + "_d");
						Object.Destroy(dark);
						Shader leg = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Nature/Terrain/Diffuse");
						Log("    legacy diffuse shader: " + (leg != null ? leg.name : "none in this build"));
						if (leg != null)
						{
							var ld = new Material(leg); ld.mainTexture = ms[2].mainTexture; ld.mainTextureScale = ms[2].mainTextureScale; ld.mainTextureOffset = ms[2].mainTextureOffset;
							m2[2] = ld; mr.sharedMaterials = m2;
							yield return CameraShot(eye, look, "dig_rimprobe_" + n + "_e");
							Object.Destroy(ld);
						}
						// (f: the rim top not drawn at all - what is in front of it and behind it)
						mr.sharedMaterials = new[] { ms[0], ms[1] };
						yield return CameraShot(eye, look, "dig_rimprobe_" + n + "_f");
						mr.sharedMaterials = ms;
					}
					// (every other renderer whose bounds hold the mouth: anything drawn over the rim)
					var bnd = new Bounds(at, new Vector3(8f, 8f, 8f));
					Log("    renderers at the mouth: " + string.Join("; ", Object.FindObjectsOfType<Renderer>().Where(x => x.enabled && x.gameObject.activeInHierarchy && x != mr && x.bounds.Intersects(bnd) && x.bounds.size.magnitude < 400f)
						.Take(20).Select(x => x.name + " (" + (x.sharedMaterial != null ? x.sharedMaterial.shader.name + " q" + x.sharedMaterial.renderQueue : "-") + ")").ToArray()));
					n++;
				}
			}
			if (n == 0) Fail("dig rim probe: no dug tunnel's mouth within 2000 m");
			else Log("PASS: dig rim probe - " + n + " mouths of " + tuns.Count + " tunnels");
		}

		[ConsoleCommand(name: "CIDigTunnelCamera", docs: "Dev, editor: the editor camera and a dug tunnel - a tunnel dug into a generated island's hill (24 m in, round a bend), the camera flown (its own controls, normal speed, steered at the passage's points) from in front of the mouth to the end: every step in the passage, under the hill's surface, near the path, not too fast; into the wall it stays inside, up into the roof it stops; God mode on: on up through the rock, and into the hillside away from the mouth (off: kept over the ground). Pictures from inside")]
		public static void DigTunnelCameraCommand(string[] args) { StartTest(DigTunnelCameraRoutine()); }

		/// <summary>The editor camera flown with its own controls (EditorCamera.TestKeys) for some seconds, facing aim each frame (null: as it is).</summary>
		static IEnumerator FlyCamera(EditorCamera ec, Vector3 keys, float seconds, System.Func<Vector3> aim)
		{
			float t0 = Time.unscaledTime;
			while (Time.unscaledTime < t0 + seconds)
			{
				if (aim != null) ec.Face(aim());
				EditorCamera.TestKeys = keys;
				yield return null;
			}
			EditorCamera.TestKeys = null;
			for (int i = 0; i < 15; i++) yield return null; // (it slows down)
		}

		static IEnumerator DigTunnelCameraRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("dig tunnel camera: in the editor"); yield break; }
			EditorCamera ec = EditorCamera.Instance;
			if (ec == null) { Fail("dig tunnel camera: no editor camera"); yield break; }
			bool ok = true;
			float radius0 = DigTunnel.Radius, slope0 = DigTunnel.Slope, speed0 = EditorCamera.SpeedFactor;
			bool god0 = EditorCamera.GodMode;
			DigTunnel.Radius = 1.75f; DigTunnel.Slope = 0f;
			EditorCamera.SpeedFactor = 1f; EditorCamera.GodMode = false;
			DynamicIslands.NewIsland();
			yield return null;
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4242, Radius = 130f, Height = 30f, Style = TerrainPainter.Tropical });
			yield return null;
			yield return new WaitForSecondsRealtime(0.6f);
			Terrain t = terraineditor.terrain;
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorWaterLevel;
			System.Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };

			// A hill's foot (as CIDigTunnelEditor): the ground 10 m on 3 m higher, 24 m on and round the bend 5.5 m higher
			Vector3 mouth = Vector3.zero, dir = Vector3.zero, side = Vector3.zero;
			bool found = false;
			float bestScore = 0f;
			foreach (Vector3 d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
				for (float s = 90f; s > 10f; s -= 2f)
				{
					Vector3 p = ground(c - d * s);
					if (p.y < sea + 1.5f) continue;
					foreach (Vector3 sd in new[] { Vector3.Cross(Vector3.up, d), -Vector3.Cross(Vector3.up, d) })
					{
						float score = Mathf.Min(Mathf.Min(ground(p + d * 10f).y - p.y - 3f, ground(p + d * 24f).y - p.y - 5.5f),
							Mathf.Min(ground(p + d * 24f + sd * 10f).y - p.y - 5.5f, ground(p + d * 16f + sd * 8f).y - p.y - 5f));
						if (score >= bestScore) { bestScore = score; mouth = p; dir = d; side = sd; found = true; }
					}
				}
			DigTunnel tun = found ? DigTunnel.TestStroke(t, new List<Vector3> { mouth, mouth + dir * 24f, mouth + dir * 24f + side * 10f }) : null;
			yield return null;
			DigTunnel.EditorNow(t);
			yield return null;
			if (tun == null) { Fail("dig tunnel camera: no tunnel dug (hillside found " + found + ")"); DigTunnel.Radius = radius0; DigTunnel.Slope = slope0; EditorCamera.SpeedFactor = speed0; EditorCamera.GodMode = god0; yield break; }
			List<Vector3> line = DigLine(tun);
			System.Func<Vector3, float> inside = p => tun.Distance(tun.transform.InverseTransformPoint(p));
			System.Func<Vector3, float> fromPath = p =>
			{
				float best = float.MaxValue;
				for (int i = 0; i + 1 < line.Count; i++)
				{
					Vector3 a = line[i], ab = line[i + 1] - a;
					float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					best = Mathf.Min(best, (a + ab * k - p).magnitude);
				}
				return best;
			};
			Log("  the tunnel: " + line.Count + " points, the mouth " + (mouth.y - sea).ToString("0.0") + " m above the sea, dug " + dir + " then " + side);

			// 1. Flown from 6 m in front of the mouth along the passage's points to its end, steered at the next point
			Vector3 start = line[0] - dir * 6f;
			start.y = Mathf.Max(line[0].y, ground(start).y + 1.2f);
			ec.transform.SetPositionAndRotation(start, Quaternion.LookRotation(line[0] - start));
			ec.Sync();
			yield return null;
			List<Vector3> way = line;
			int w = 0, frames = 0, notIn = 0, notUnder = 0, offPath = 0, deepFrames = 0;
			float maxStep = 0f, maxSpeed = 0f, maxOff = 0f, maxUp = float.MinValue, t0 = Time.unscaledTime;
			bool shotMid = false;
			var bad = new List<string>();
			Vector3 last = ec.transform.position;
			while (w < way.Count && Time.unscaledTime < t0 + 40f)
			{
				Vector3 cp = ec.transform.position;
				while (w < way.Count && (way[w] - cp).magnitude < (w == way.Count - 1 ? 0.6f : 1.2f)) w++;
				if (w >= way.Count) break;
				ec.Face(way[w] - cp);
				EditorCamera.TestKeys = new Vector3(0f, 0f, 1f);
				yield return null;
				frames++;
				cp = ec.transform.position;
				float step = (cp - last).magnitude;
				last = cp;
				maxStep = Mathf.Max(maxStep, step);
				maxSpeed = Mathf.Max(maxSpeed, step / Mathf.Max(1e-4f, Time.unscaledDeltaTime));
				// (past the mouth by 3 m: in the passage, under the hill's surface, near the path)
				if (Vector3.Dot(cp - line[0], dir) < 3f) continue;
				deepFrames++;
				float din = inside(cp), dg = cp.y - ground(cp).y, dp = fromPath(cp);
				maxUp = Mathf.Max(maxUp, dg); maxOff = Mathf.Max(maxOff, dp);
				if (din >= 0f) { notIn++; if (bad.Count < 6) bad.Add("out of the passage " + din.ToString("0.00") + " m at point " + w); }
				if (dg >= 0f) { notUnder++; if (bad.Count < 6) bad.Add(dg.ToString("0.0") + " m over the ground at point " + w); }
				if (dp > 1.5f) { offPath++; if (bad.Count < 6) bad.Add(dp.ToString("0.0") + " m off the path at point " + w); }
				if (!shotMid && w >= way.Count / 2)
				{
					shotMid = true;
					EditorCamera.TestKeys = null;
					yield return CameraShot(cp, cp + ec.transform.forward * 10f, "digtunnel_camera_mid");
					last = ec.transform.position;
				}
			}
			EditorCamera.TestKeys = null;
			for (int i = 0; i < 15; i++) yield return null;
			Vector3 endP = ec.transform.position;
			bool reached = w >= way.Count;
			Check(ref ok, reached && deepFrames > 20 && notIn == 0 && notUnder == 0 && offPath == 0 && maxSpeed < 7f,
				"the camera flown in at the mouth and along the passage: " + (reached ? "reached its end" : "stuck at point " + w + " of " + way.Count) + " in " + (Time.unscaledTime - t0).ToString("0.0") + " s, " + frames + " frames (" + deepFrames + " past the mouth): "
				+ notIn + " out of the passage, " + notUnder + " over the ground (highest " + maxUp.ToString("0.0") + " m), " + offPath + " off the path (farthest " + maxOff.ToString("0.0") + " m); the fastest " + maxSpeed.ToString("0.0") + " m/s, " + maxStep.ToString("0.00") + " m a frame" + (bad.Count > 0 ? ": " + string.Join(", ", bad.ToArray()) : ""));
			yield return CameraShot(endP, line[Mathf.Max(0, line.Count - 14)], "digtunnel_camera_back");

			// 2. Into the wall: it stays in; up into the roof: it stops there; God mode: on up through the rock
			Vector3 segDir = (line[line.Count - 1] - line[Mathf.Max(0, line.Count - 4)]).normalized;
			Vector3 wallDir = (Vector3.Cross(Vector3.up, segDir) + segDir * 0.3f).normalized;
			yield return FlyCamera(ec, new Vector3(0f, 0f, 1f), 1.5f, () => wallDir);
			Vector3 cw = ec.transform.position;
			yield return FlyCamera(ec, new Vector3(0f, 1f, 0f), 1.5f, null);
			Vector3 cr = ec.transform.position;
			Check(ref ok, inside(cw) < 0f && inside(cr) < 0f && cr.y < ground(cr).y, "God mode off: flown into the wall the camera stays in the passage (" + inside(cw).ToString("0.00") + " m), up into the roof too (" + inside(cr).ToString("0.00") + " m, " + (ground(cr).y - cr.y).ToString("0.0") + " m under the ground)");
			EditorCamera.GodMode = true;
			yield return FlyCamera(ec, new Vector3(0f, 1f, 0f), 2f, null);
			Vector3 cg = ec.transform.position;
			Check(ref ok, inside(cg) > 0.2f && cg.y > cr.y + 2f, "God mode on: up on through the rock (" + (cg.y - cr.y).ToString("0.0") + " m up, " + inside(cg).ToString("0.0") + " m out of the passage, " + (ground(cg).y - cg.y).ToString("0.0") + " m under the ground)");

			// 3. Into the hillside 12 m beside the mouth: off, over the ground; on, into it
			Vector3 h0 = ground(mouth - side * 12f - dir * 4f) + Vector3.up * 1.2f;
			EditorCamera.GodMode = false;
			ec.transform.SetPositionAndRotation(h0, Quaternion.LookRotation(dir)); ec.Sync(); yield return null;
			yield return FlyCamera(ec, new Vector3(0f, 0f, 1f), 3f, () => dir);
			Vector3 hOff = ec.transform.position;
			EditorCamera.GodMode = true;
			ec.transform.SetPositionAndRotation(h0, Quaternion.LookRotation(dir)); ec.Sync(); yield return null;
			yield return FlyCamera(ec, new Vector3(0f, 0f, 1f), 3f, () => dir);
			Vector3 hOn = ec.transform.position;
			Check(ref ok, hOff.y >= ground(hOff).y + 0.5f && hOn.y < ground(hOn).y - 1f && Vector3.Dot(hOn - h0, dir) > 6f,
				"into the hillside: God mode off keeps over the ground (" + (hOff.y - ground(hOff).y).ToString("0.0") + " m over it), on flies in (" + (ground(hOn).y - hOn.y).ToString("0.0") + " m under it, " + Vector3.Dot(hOn - h0, dir).ToString("0.0") + " m on)");

			EditorCamera.TestKeys = null;
			EditorCamera.GodMode = god0; EditorCamera.SpeedFactor = speed0;
			DigTunnel.Radius = radius0; DigTunnel.Slope = slope0;
			if (ok) Log("PASS: dig tunnel camera"); else Fail("dig tunnel camera");
		}

		[ConsoleCommand(name: "CILookProbe", docs: "Dev, editor or world: logs what the look fixes of 11-03 need (CILookProbe ground|den|mountain|light): ground - the first dug tunnel's ground material, layers and rim top; den - every cave piece's renderers (shader, colours, probes); mountain - every cave mountain's skin and its caves' tint; light - the sun, shadows and ambient")]
		public static void LookProbeCommand(string[] args) { StartTest(LookProbeRoutine(args.Length > 0 ? args[0] : "light")); }

		static string MatInfo(Material m)
		{
			if (m == null) return "null";
			Shader s = m.shader;
			var parts = new List<string>();
			for (int i = 0; i < s.GetPropertyCount(); i++)
			{
				string n = s.GetPropertyName(i);
				switch (s.GetPropertyType(i))
				{
					case UnityEngine.Rendering.ShaderPropertyType.Color: parts.Add(n + "=" + m.GetColor(n)); break;
					case UnityEngine.Rendering.ShaderPropertyType.Float:
					case UnityEngine.Rendering.ShaderPropertyType.Range: parts.Add(n + "=" + m.GetFloat(n).ToString("0.###")); break;
					case UnityEngine.Rendering.ShaderPropertyType.Vector: parts.Add(n + "=" + m.GetVector(n)); break;
					case UnityEngine.Rendering.ShaderPropertyType.Texture: { Texture t = m.GetTexture(n); parts.Add(n + "=" + (t != null ? t.name + "(" + t.width + "x" + t.height + ")" : "-") + " st" + m.GetTextureScale(n) + m.GetTextureOffset(n)); break; }
				}
			}
			return m.name + " [" + s.name + "] kw:" + string.Join(",", m.shaderKeywords) + " | " + string.Join("; ", parts.ToArray());
		}

		static IEnumerator LookProbeRoutine(string what)
		{
			yield return null;
			Light sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsOfType<Light>().Where(l => l.type == LightType.Directional && l.enabled).OrderByDescending(l => l.intensity).FirstOrDefault();
			if (what == "light")
			{
				Log("  sun " + (sun != null ? sun.name + " " + sun.color + " x" + sun.intensity + " shadows " + sun.shadows + " str " + sun.shadowStrength + " mask " + sun.cullingMask + " bias " + sun.shadowBias + "/" + sun.shadowNormalBias : "none")
					+ "; quality " + QualitySettings.GetQualityLevel() + " shadows " + QualitySettings.shadows + " dist " + QualitySettings.shadowDistance + " cascades " + QualitySettings.shadowCascades + " res " + QualitySettings.shadowResolution
					+ "; ambient " + RenderSettings.ambientMode + " sky " + RenderSettings.ambientSkyColor + " eq " + RenderSettings.ambientEquatorColor + " gr " + RenderSettings.ambientGroundColor + " x" + RenderSettings.ambientIntensity
					+ "; probe dc " + RenderSettings.ambientProbe[0, 0].ToString("0.00") + "," + RenderSettings.ambientProbe[1, 0].ToString("0.00") + "," + RenderSettings.ambientProbe[2, 0].ToString("0.00")
					+ "; camera " + (Camera.main != null ? Camera.main.actualRenderingPath + " far " + Camera.main.farClipPlane : "none"));
				bool shot = GfxShotBegin();
				Log("  at the pictures' quality (" + shot + "): " + QualitySettings.GetQualityLevel() + " shadows " + QualitySettings.shadows + " dist " + QualitySettings.shadowDistance + " cascades " + QualitySettings.shadowCascades
					+ " res " + QualitySettings.shadowResolution + "; sun shadows " + (sun != null ? sun.shadows.ToString() : "-"));
				GfxShotEnd(shot);
			}
			if (what == "ground")
			{
				DigTunnel tun = Object.FindObjectsOfType<DigTunnel>().FirstOrDefault(x => x.PointCount > 10);
				Terrain land = tun != null ? tun.Land() : Terrain.activeTerrain;
				if (land != null)
				{
					Log("  terrain " + land.name + " instanced " + land.drawInstanced + " basemap " + land.basemapDistance + " shadows " + land.shadowCastingMode + " detail " + land.detailObjectDistance + "/" + land.terrainData.detailPrototypes.Length + " layer " + LayerMask.LayerToName(land.gameObject.layer));
					Log("  ground material " + MatInfo(land.materialTemplate));
					foreach (TerrainLayer l in land.terrainData.terrainLayers)
						if (l != null) Log("  layer " + l.name + " diffuse " + (l.diffuseTexture != null ? l.diffuseTexture.name + " " + l.diffuseTexture.width : "-") + " normal " + (l.normalMapTexture != null ? l.normalMapTexture.name + " " + l.normalMapTexture.width + " " + l.normalMapTexture.graphicsFormat : "-") + " mask " + (l.maskMapTexture != null ? l.maskMapTexture.name : "-") + " tile " + l.tileSize + " off " + l.tileOffset + " smooth " + l.smoothness + " metal " + l.metallic + " nscale " + l.normalScale + " spec " + l.specular + " remap " + l.diffuseRemapMin + "-" + l.diffuseRemapMax);
				}
				if (tun != null)
				{
					MeshRenderer mr = tun.transform.Find(DigTunnel.RockName).GetComponent<MeshRenderer>();
					Mesh mesh = mr.GetComponent<MeshFilter>().sharedMesh;
					var tg = new List<Vector4>(); mesh.GetTangents(tg);
					Log("  rim top " + tun.RimTop + "; mesh parts " + mesh.subMeshCount + " verts " + mesh.vertexCount + " tangents " + tg.Count);
					Material[] ms = mr.sharedMaterials;
					for (int i = 0; i < ms.Length; i++) Log("  rock part " + i + ": " + MatInfo(ms[i]));
				}
			}
			if (what == "den")
			{
				foreach (Transform t in Object.FindObjectsOfType<Transform>().Where(x => x.parent != null && (x.name.StartsWith(CaveTool.Room) || x.name.StartsWith(CaveTool.Tunnel) || x.name.ToLowerInvariant().Contains("cave"))).Take(30))
				{
					foreach (Renderer r in t.GetComponentsInChildren<MeshRenderer>().Take(6))
					{
						var b = new MaterialPropertyBlock(); r.GetPropertyBlock(b);
						Log("  " + t.name + "/" + r.name + " at " + r.bounds.center + " probes " + r.lightProbeUsage + " block " + (b.isEmpty ? "empty" : "set") + " shadows " + r.shadowCastingMode + " layer " + LayerMask.LayerToName(r.gameObject.layer));
						foreach (Material m in r.sharedMaterials.Take(3)) Log("    " + MatInfo(m));
					}
				}
			}
			if (what == "mountain")
			{
				foreach (CaveMountain cm in Object.FindObjectsOfType<CaveMountain>())
				{
					Transform sk = cm.transform.Find(CaveMountain.SkinName);
					MeshRenderer mr = sk != null ? sk.GetComponent<MeshRenderer>() : null;
					Log("  mountain on " + cm.name + " at " + cm.transform.position + ": " + (mr != null ? MatInfo(mr.sharedMaterial) : "no skin"));
					if (mr != null) { var b = new MaterialPropertyBlock(); mr.GetPropertyBlock(b); Log("    block " + (b.isEmpty ? "empty" : "set") + " top " + (b.GetTexture("_TopDiffuse") != null ? b.GetTexture("_TopDiffuse").name : "-")); }
				}
			}
			Log("PASS: look probe " + what);
		}
	}
}
