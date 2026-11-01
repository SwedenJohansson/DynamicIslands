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

		[ConsoleCommand(name: "CIDigTunnelEditor", docs: "Dev, editor: the Dig brush on a generated island - a tunnel dug into a hill from its foot, on 24 m and round a bend: its rock lining made, the ground cut only at its mouth, walkable end to end (a floor every step, steps under 0.6 m, 2 m of room), no look from inside out through the hill; one Ctrl+Z per stroke (the tunnel, a branch, a Shift fill), the ground's holes back as they were; saved and loaded with the island. Leaves the island citest-digtunnel for CIDigTunnelWorld")]
		public static void DigTunnelEditorCommand(string[] args) { StartTest(DigTunnelEditorRoutine()); }

		[ConsoleCommand(name: "CIDigTunnelWorld", docs: "Dev, in a world (host): the island CIDigTunnelEditor left spawned ahead of the raft - the ground cut over its tunnel's mouth, the lining there with its collider, the player walked from the mouth to the tunnel's end, no look from inside to the sky or the ground from below; pictures of the mouth and inside. The island and its file are taken away after (keep: kept)")]
		public static void DigTunnelWorldCommand(string[] args) { args0Keep = args.Contains("keep"); StartTest(DigTunnelWorldRoutine()); }

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
		/// Looks from eyes in the passage: one that meets nothing or anything but the dug rock (or the ground from above outside) leaks,
		/// unless it has left by the passage into the open air first (out of the mouth) - the ray followed in 0.25 m steps up to what
		/// it meets: still in the passage until it is over the ground, never through the lining.
		/// </summary>
		static int DigLeaks(DigTunnel t, Terrain land, List<Vector3> eyes, List<string> seen, out int under)
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
						for (float s = 0.25f; s < Mathf.Min(first, 60f); s += 0.25f)
						{
							Vector3 p = eye + d * s;
							if (p.y > land.SampleHeight(p) + o + 0.05f) { outside = true; break; }
							if (t.Distance(t.transform.InverseTransformPoint(p)) > 0.4f) break; // (out of the passage under the ground: through the lining)
						}
						if (outside) continue;
						string where = (eye - eyes[0]).magnitude.ToString("0") + " m in, " + d.ToString("F1");
						if (all.Length == 0) { leaks++; seen.Add("sky " + where); }
						else if (all[0].collider is TerrainCollider) { if (all[0].normal.y < 0f) { under++; seen.Add("the ground from below " + where); } else { leaks++; seen.Add("the ground " + all[0].distance.ToString("0.0") + " m off " + where); } }
						else if (!IsDigRock(all[0].collider)) { leaks++; seen.Add("out to " + all[0].collider.name + " " + all[0].distance.ToString("0") + " m, " + where); }
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
			yield return CameraShot(mouth - dir * 12f + Vector3.up * 5f, mouth + Vector3.up * 1.5f, "digtunnel_mouth");
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
			t = terraineditor.terrain;
			DigTunnel back = GameObject.Find("PlacedObjects").GetComponentsInChildren<DigTunnel>(false).FirstOrDefault();
			int cutBack = DigTunnel.EditorNow(t);
			Check(ref ok, back != null && back.PointCount == n0 && back.Triangles > 0 && Mathf.Abs(back.Triangles - tri0) < tri0 / 20 + 10 && (back.transform.position - at0).magnitude < 0.05f && cutBack == cut.Count,
				"loaded again: the tunnel where it was (" + (back != null ? back.PointCount + " points, " + back.Triangles + " triangles (" + tri0 + " before), " : "none, ") + cutBack + " cells cut, " + cut.Count + " before)");
			Check(ref ok, back == null || back.transform.Find(DigTunnel.RockName) != null && back.GetComponent<EditorGameObject>() != null, "loaded: an editor object with its lining");
			DigTunnel.Radius = radius0; DigTunnel.Slope = slope0;
			if (ok) Log("PASS: dig tunnel editor"); else Fail("dig tunnel editor");
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

			// Walked from the mouth to its end with Raft's controller
			Network_Player player = RAPI.GetLocalPlayer();
			List<Vector3> line = DigLine(tun);
			Vector3 rel0 = line[0] - e.Position;
			var route = new List<Vector3>();
			for (int i = 2; i < line.Count; i += 3) route.Add(line[i] - e.Position);
			route.Add(line[line.Count - 1] - e.Position);
			RaycastHit fh;
			Vector3 start = line[0];
			if (DigFloor(line[0] + Vector3.up * 1f, out fh)) start = fh.point;
			PlayerMove.To(player, start + Vector3.up * 0.3f);
			yield return new WaitForSeconds(1f);
			PersonController pc = player.PersonController;
			CharacterController cc = pc.controller;
			pc.SwitchControllerType(ControllerType.Ground);
			string stoppedAt = null;
			int reached = 0;
			foreach (Vector3 rel in route)
			{
				float stuck = 0f, total = 0f;
				Vector3 last = player.transform.position;
				while (ScFlat(player.transform.position, e.Position + rel) > 0.7f)
				{
					KeepAlive(player);
					Vector3 d = e.Position + rel - player.transform.position; d.y = 0f;
					Vector3 dirW = d.normalized;
					Look(player, Quaternion.LookRotation(dirW).eulerAngles.y, 0f);
					cc.Move(dirW * pc.normalSpeed * Time.deltaTime);
					Vector3 now = player.transform.position;
					stuck = ScFlat(now, last) < pc.normalSpeed * Time.deltaTime * 0.3f ? stuck + Time.deltaTime : 0f;
					last = now;
					total += Time.deltaTime;
					if (stuck > 2.5f || total > 20f)
					{
						Collider bl = WhatBlocks(player, dirW);
						stoppedAt = "stopped " + ScFlat(now, e.Position + rel).ToString("0.0") + " m short of point " + (reached + 1) + " (" + (bl != null ? bl.name : "nothing ahead") + ", grounded " + cc.isGrounded + ")";
						break;
					}
					yield return null;
				}
				if (stoppedAt != null) break;
				reached++;
			}
			Vector3 endAt = e.Position + route.Last();
			float drop = endAt.y - DigTunnel.CentreUp * tun.Strokes[0][tun.Strokes[0].Count - 1].R - player.transform.position.y;
			Check(ref ok, stoppedAt == null && Mathf.Abs(drop) < 1.5f, "walked from the mouth to the end: " + reached + " of " + route.Count + " points" + (stoppedAt != null ? ", " + stoppedAt : "") + " (" + drop.ToString("0.0") + " m under the passage's floor at the end)");

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
	}
}
