using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	// Scenario rows that need a raft built for them: SC10 (a 12 x 12 raft - islands, plan rules and near-rules keep clear of
	// all its blocks, AU68) and SC9 (foundations built into an island's ground, then sailing away).
	public static partial class DevTests
	{
		/// <summary>Host: foundations added to the raft on these grid cells (relative to its first foundation); the made ones.</summary>
		static List<Block> ScGrow(Network_Player me, IEnumerable<Vector2Int> cells)
		{
			var made = new List<Block>();
			List<Block> start = StormSeas.Foundations();
			Item_Base foundation = ItemManager.GetItemByName("Block_Foundation");
			if (start.Count == 0 || foundation == null) return made;
			Vector3 o = start[0].transform.localPosition;
			var taken = new HashSet<Vector2Int>(start.Select(b => StormSeas.CellOf(b.transform.localPosition, o)));
			foreach (Vector2Int c in cells)
			{
				if (!taken.Add(c)) continue;
				Block b = null;
				try { b = me.BlockCreator.CreateBlockCheat(foundation, o + new Vector3(c.x * HeadStart.Cell, 0f, c.y * HeadStart.Cell), Vector3.zero, DPS.Default, 0); }
				catch (Exception e) { Log("  (placing a foundation failed: " + e.Message + ")"); }
				if (b != null) made.Add(b);
			}
			return made;
		}

		static void ScTakeDown(List<Block> made)
		{
			foreach (Block b in made) { try { if (b != null) BlockCreator.RemoveBlockNetwork(b, null, true); } catch { } }
			made.Clear();
		}

		/// <summary>Raft blocks with land (above the sea) under them on this island.</summary>
		static int ScBlocksOverLand(IslandWorldState.Entry e, Raft raft, out float highest)
		{
			highest = float.NegativeInfinity;
			int n = 0;
			if (e == null || e.Root == null || raft == null) return 0;
			Terrain[] ts = e.Root.GetComponentsInChildren<Terrain>(true);
			foreach (Block b in BlockCreator.GetPlacedBlocks())
			{
				if (b == null || !b.transform.IsChildOf(raft.transform)) continue;
				Vector3 p = b.transform.position;
				foreach (Terrain t in ts)
				{
					if (t == null || t.terrainData == null) continue;
					Vector3 tp = t.transform.position, size = t.terrainData.size;
					if (p.x < tp.x || p.z < tp.z || p.x > tp.x + size.x || p.z > tp.z + size.z) continue;
					float h = t.SampleHeight(p) + tp.y;
					highest = Mathf.Max(highest, h);
					if (h > -0.5f) { n++; break; }
				}
			}
			return n;
		}

		#region SC10 - a 12 x 12 raft

		[ConsoleCommand(name: "CIScBigRaft", docs: "Dev, world (host, a test world 'CI ...'): SC10/AU68 - the raft grown to 12 x 12 foundations; its reach (RaftRadius) follows; a plan rule 'ahead:20', a host island and a near-rule pointed at the raft, the random islands' spot ahead and the tests' clear spot all keep the island's land + 60 m clear of the furthest block, and no block has land under it. Blocks, islands and plan put back after")]
		public static void ScBigRaftCommand() { StartTest(ScBigRaftRoutine()); }

		static IEnumerator ScBigRaftRoutine()
		{
			Network_Player me = RAPI.GetLocalPlayer();
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (me == null || raft == null || !CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("big raft: run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const string big = "ciscbig", host = "ciscbighost", near = "ciscbignear";
			string[] files = { big, host, near };
			var grown = new List<Block>();
			var made = new List<IslandWorldState.Entry>();
			WorldPlan keep = WorldDirector.Plan;
			try
			{
				for (int i = 0; i < files.Length; i++)
				{
					var s = new IslandGenSettings { Seed = 9310 + i, Radius = 40f, Height = 6f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
					IslandGenerator.CreateFile(s, files[i]).Save(IslandSpawner.PathFor(files[i]));
					IslandWorldState.Remove(files[i]);
				}
				IslandCache.Forget();

				// (the raft: 12 x 12 around the foundations it has)
				float before = CustomIslandSpawner.RaftRadius;
				List<Block> start = StormSeas.Foundations();
				if (start.Count == 0) { Fail("big raft: the raft has no foundations"); yield break; }
				Vector3 o = start[0].transform.localPosition;
				var cells = start.Select(b => StormSeas.CellOf(b.transform.localPosition, o)).ToList();
				int cx = Mathf.RoundToInt((float)cells.Average(c => c.x)), cz = Mathf.RoundToInt((float)cells.Average(c => c.y));
				var want = new List<Vector2Int>();
				for (int i = -5; i <= 6; i++) for (int j = -5; j <= 6; j++) want.Add(new Vector2Int(cx + i, cz + j));
				grown = ScGrow(me, want);
				yield return new WaitForSecondsRealtime(11f); // (RaftRadius looks every 10 s)
				int foundations = StormSeas.Foundations().Count;
				float reach = CustomIslandSpawner.RaftRadius;
				Check(ref ok, foundations >= 144, "the raft is 12 x 12: " + foundations + " foundations (" + grown.Count + " added to " + start.Count + ")");
				Check(ref ok, reach >= 10f && reach > before + 5f, "its reach follows: " + Gf(reach, "F1") + " m from its middle (was " + Gf(before, "F1") + " m)");
				float need = CustomIslandSpawner.LandRadius(big) + CustomIslandSpawner.Clearance + reach;

				// (a plan rule that asks for less room than the raft needs: ahead:20)
				IntroRule ahead = IntroRule.Parse("big_ahead | island:" + big + " | start | ahead:20 | | Big Ahead");
				IntroRule hostRule = IntroRule.Parse("big_host | island:" + host + " | start | ahead:150 | | Big Host");
				WorldDirector.Plan = new WorldPlan { Name = "CI big raft", Random = true, Rules = { ahead, hostRule } };
				Vector3 raftAt = CustomIslandSpawner.RaftPosition.Value;
				string why = WorldDirector.Bring(ahead, null, null);
				IslandWorldState.Entry a = null;
				for (float t = 0f; t < 30f && (a == null || a.Root == null); t += 0.5f) { a = IslandWorldState.Islands.LastOrDefault(x => x.HostName == big && x.Rule == "big_ahead"); yield return new WaitForSeconds(0.5f); }
				if (a != null) made.Add(a);
				Check(ref ok, a != null && a.Root != null, "the 'ahead:20' rule brings its island (" + (why ?? "on its way") + ")");
				float highest;
				if (a != null && a.Root != null)
				{
					float d = ScFlat(a.Position, raftAt);
					Check(ref ok, d >= need - 1f, "it comes clear of the furthest block: " + Gf(d, "F0") + " m from the raft's middle, needs " + Gf(need, "F0") + " m (land " + Gf(CustomIslandSpawner.LandRadius(big), "F0") + " + 60 + raft " + Gf(reach, "F0") + ")");
					int over = ScBlocksOverLand(a, raft, out highest);
					Check(ref ok, over == 0, "no raft block has its land under it (" + over + "; highest ground under a block " + (float.IsNegativeInfinity(highest) ? "none: off its terrain" : Gf(highest, "F1") + " m") + ")");
				}

				// (a host island, then a near-rule pointed straight at the raft: its first spot would be on the blocks)
				why = WorldDirector.Bring(hostRule, null, null);
				IslandWorldState.Entry h = null;
				for (float t = 0f; t < 30f && (h == null || h.Root == null); t += 0.5f) { h = IslandWorldState.Islands.LastOrDefault(x => x.HostName == host && x.Rule == "big_host"); yield return new WaitForSeconds(0.5f); }
				if (h != null) made.Add(h);
				Check(ref ok, h != null && h.Root != null, "the host island comes (" + (why ?? "on its way") + ")");
				if (h != null && h.Root != null)
				{
					raftAt = CustomIslandSpawner.RaftPosition.Value;
					Vector3 toRaft = raftAt - h.Position;
					float bearing = Mathf.Atan2(toRaft.x, toRaft.z) * Mathf.Rad2Deg;
					IntroRule nearRule = IntroRule.Parse("big_near | island:" + near + " | start | near:big_host:10:" + IntroRule.DirectionName(bearing) + " | | Big Near");
					Check(ref ok, nearRule != null && nearRule.Where == "near", "the near-rule reads: " + (nearRule != null ? nearRule.ToLine() : "null"));
					if (nearRule != null)
					{
						WorldDirector.Plan.Rules.Add(nearRule);
						why = WorldDirector.Bring(nearRule, null, null);
						IslandWorldState.Entry n = null;
						for (float t = 0f; t < 30f && (n == null || n.Root == null); t += 0.5f) { n = IslandWorldState.Islands.LastOrDefault(x => x.HostName == near && x.Rule == "big_near"); yield return new WaitForSeconds(0.5f); }
						if (n != null) made.Add(n);
						Check(ref ok, n != null && n.Root != null, "the near-rule (" + IntroRule.DirectionName(bearing) + " of the host, towards the raft) brings its island (" + (why ?? "on its way") + ")");
						if (n != null && n.Root != null)
						{
							float needN = CustomIslandSpawner.LandRadius(near) + CustomIslandSpawner.Clearance + reach;
							float d = ScFlat(n.Position, raftAt);
							Check(ref ok, d >= needN - 1f, "it keeps clear of the raft's blocks: " + Gf(d, "F0") + " m from the raft's middle, needs " + Gf(needN, "F0") + " m; " + Gf(ScFlat(n.Position, h.Position), "F0") + " m from the host");
							int over = ScBlocksOverLand(n, raft, out highest);
							Check(ref ok, over == 0, "no raft block has the near island's land under it (" + over + ")");
						}
					}
				}

				// (the random islands' spot ahead, and the tests' clear spot)
				raftAt = CustomIslandSpawner.RaftPosition.Value;
				float r = CustomIslandSpawner.LandRadius(big);
				int spots = 0, close = 0;
				float nearest = float.MaxValue;
				for (int i = 0; i < 30; i++)
				{
					Vector3? s = CustomIslandSpawner.SpotAhead(raftAt, r, 0f, null);
					if (!s.HasValue) continue;
					spots++;
					float d = ScFlat(s.Value, raftAt);
					nearest = Mathf.Min(nearest, d);
					if (d < r + CustomIslandSpawner.Clearance + reach - 1f) close++;
				}
				Check(ref ok, spots > 0 && close == 0, "random islands' spot ahead: " + spots + "/30 found, " + close + " too close; nearest " + (spots > 0 ? Gf(nearest, "F0") : "-") + " m, needs " + Gf(r + CustomIslandSpawner.Clearance + reach, "F0") + " m");
				Vector3? clear = CustomIslandSpawner.FindClearSpot(raftAt, r, 30f);
				Check(ref ok, clear.HasValue && ScFlat(clear.Value, raftAt) >= r + CustomIslandSpawner.Clearance + reach - 1f, "the tests' clear spot keeps the same room: " + (clear.HasValue ? Gf(ScFlat(clear.Value, raftAt), "F0") + " m" : "none"));
			}
			finally
			{
				WorldDirector.Plan = keep;
				ScRemove(made, files);
				ScTakeDown(grown);
			}
			if (ok) Log("PASS: big raft (SC10)"); else Fail("big raft (SC10): see the lines above");
		}

		#endregion

		#region SC9 - foundations built into an island's ground

		[ConsoleCommand(name: "CIScGrounded", docs: "Dev, world (host, a test world 'CI ...'): SC9 - the raft pushed up to an island, foundations built out from its bow into the island's ground (beach and land under them), then the raft pushed away at 6 m/s: it frees itself and sails off, stays at the sea and level, and the player stays on it. Blocks and island put back after")]
		public static void ScGroundedCommand() { StartTest(ScGroundedRoutine()); }

		static IEnumerator ScGroundedRoutine()
		{
			Network_Player me = RAPI.GetLocalPlayer();
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (me == null || raft == null || raft.body == null || !CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("grounded: run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const string isl = "ciscground";
			var made = new List<IslandWorldState.Entry>();
			var grown = new List<Block>();
			var r = new List<float>();
			try
			{
				try { ScIsland(isl, "Ground Rock").Save(IslandSpawner.PathFor(isl)); } catch (Exception ex) { Fail("grounded: no sample island (" + ex.Message + ")"); yield break; }
				Vector3? spot = ScSpot(isl, 320f);
				if (!spot.HasValue) { Fail("grounded: no open sea near the raft"); yield break; }
				yield return ScBring(isl, spot.Value, made);
				IslandWorldState.Entry e = made.Count > 0 ? made[0] : null;
				if (e == null || e.Root == null) { Fail("grounded: the island did not come"); yield break; }
				OnRaftCommand();
				yield return new WaitForSeconds(1.5f);
				Vector3 dir = Flat(e.Position - raft.body.position).normalized;
				yield return ScPush(dir, 6f, 45f, 1000f, r);
				Check(ref ok, r[1] > 0f, "the raft runs up to the island and stops (" + Gf(r[0], "F0") + " m; " + Gf(ScFlat(e.Position, raft.body.position), "F0") + " m from its middle)");

				// (foundations from the bow towards the island, until some stand on land: up to 12 rows)
				Vector3 local = raft.transform.InverseTransformDirection(dir);
				Vector2Int step = Mathf.Abs(local.x) > Mathf.Abs(local.z) ? new Vector2Int(local.x > 0 ? 1 : -1, 0) : new Vector2Int(0, local.z > 0 ? 1 : -1);
				List<Block> start = StormSeas.Foundations();
				Vector3 o = start[0].transform.localPosition;
				var cells = start.Select(b => StormSeas.CellOf(b.transform.localPosition, o)).ToList();
				int front = cells.Max(c => c.x * step.x + c.y * step.y);
				var row = cells.Where(c => c.x * step.x + c.y * step.y == front).ToList();
				float highest = float.NegativeInfinity;
				int over = 0, rows = 0;
				float y0 = raft.body.position.y;
				Quaternion q0 = raft.body.rotation;
				for (int k = 1; k <= 12 && over < 2; k++)
				{
					grown.AddRange(ScGrow(me, row.Select(c => c + step * k)));
					rows = k;
					yield return new WaitForSeconds(0.3f);
					over = ScBlocksOverLand(e, raft, out highest);
				}
				Check(ref ok, over >= 2, "foundations built " + rows + " rows out into the island: " + over + " stand over its ground (highest ground " + (float.IsNegativeInfinity(highest) ? "none" : Gf(highest, "F1") + " m") + ", " + grown.Count + " added)");
				RaftColliderGuard.Heal(true);
				yield return new WaitForSeconds(3f);
				float rose = raft.body.position.y - y0, tilt = Quaternion.Angle(q0, raft.body.rotation);
				Check(ref ok, rose < 1.2f && tilt < 15f, "built into the ground, the raft stays at the sea and level (rose " + Gf(rose, "F2") + " m, tilted " + Gf(tilt, "F1") + " degrees)");
				Check(ref ok, ScOnRaft(me, raft.body), "the player is still on the raft");

				yield return ScPush(-dir, 6f, 20f, 60f, r);
				Check(ref ok, r[0] > 20f, "pushed away, the raft frees itself and sails off (" + Gf(r[0], "F0") + " m" + (r[1] > 0f ? ", stuck" : "") + ")");
				Check(ref ok, r[2] < 1.2f && r[3] < 15f, "sailing off it stays at the sea and level (rose " + Gf(r[2], "F2") + " m, tilted " + Gf(r[3], "F1") + " degrees)");
				Check(ref ok, ScOnRaft(me, raft.body), "the player sails off with it");
			}
			finally
			{
				ScTakeDown(grown);
				ScRemove(made, isl);
			}
			if (ok) Log("PASS: grounded (SC9)"); else Fail("grounded (SC9): see the lines above");
		}

		#endregion
	}
}
