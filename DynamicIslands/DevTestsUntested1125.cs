using System;
using System.Collections;
using System.IO;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests for changes made 11-20..11-25 that had none (TEST_CATALOGUE UT rows): spawnpool.txt read while it is
	/// written or held open (TODO 6b), and a terrain brush leaving cave pieces sunk under the ground where they are (c3f26693).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CISpawnPoolLive", docs: "Dev, any screen: spawnpool.txt changed while Raft runs (TODO 6b) - a file saved under a second ago is read on a later tick, not half-way; a file another program holds open keeps the values read before (no empty pool) and is read once it is let go; the file is put back after")]
		public static void SpawnPoolLiveCommand()
		{
			StartTest(SpawnPoolLiveRoutine());
		}

		static IEnumerator SpawnPoolLiveRoutine()
		{
			bool ok = true;
			string path = Path.Combine(DynamicIslands.assetpath, CustomIslandSpawner.PoolFileName);
			CustomIslandSpawner.LoadPool(true);
			if (!File.Exists(path)) { Fail("no " + path); yield break; }
			byte[] backup = File.ReadAllBytes(path);
			string text = File.ReadAllText(path);
			const string Probe = "UT Pool Probe";
			float q0 = CustomIslandSpawner.QuietMinutes, qa = Mathf.Approximately(q0, 37f) ? 41f : 37f, qb = Mathf.Approximately(q0, 53f) ? 59f : 53f;
			bool listed;
			try
			{
				// 1. A file saved just now is not read yet (it may still be being written)
				File.WriteAllText(path, text + "\r\nquietMinutes = " + qa + "\r\n" + Probe + " 7\r\n");
				CustomIslandSpawner.LoadPool(false);
				float w = CustomIslandSpawner.PoolWeight(Probe, out listed);
				Check(ref ok, Mathf.Approximately(CustomIslandSpawner.QuietMinutes, q0) && !listed, "saved under a second ago: not read yet (quietMinutes " + CustomIslandSpawner.QuietMinutes + ", was " + q0 + "; probe listed " + listed + ")");

				// 2. A moment later it is read
				yield return new WaitForSecondsRealtime(1.4f);
				CustomIslandSpawner.LoadPool(false);
				w = CustomIslandSpawner.PoolWeight(Probe, out listed);
				Check(ref ok, Mathf.Approximately(CustomIslandSpawner.QuietMinutes, qa) && listed && Mathf.Approximately(w, 7f), "a second later: read (quietMinutes " + CustomIslandSpawner.QuietMinutes + ", probe weight " + w + ")");

				// 3. Changed again but held open by another program: the values read before stay, the pool is not emptied
				File.WriteAllText(path, text + "\r\nquietMinutes = " + qb + "\r\n" + Probe + " 9\r\n");
				File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(-10));
				using (FileStream hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					CustomIslandSpawner.LoadPool(false);
					w = CustomIslandSpawner.PoolWeight(Probe, out listed);
					Check(ref ok, Mathf.Approximately(CustomIslandSpawner.QuietMinutes, qa) && listed && Mathf.Approximately(w, 7f), "held open: the values read before stay (quietMinutes " + CustomIslandSpawner.QuietMinutes + ", probe weight " + w + ", listed " + listed + ")");
				}

				// 4. Let go: read on the next try (the failed read did not count as read)
				CustomIslandSpawner.LoadPool(false);
				w = CustomIslandSpawner.PoolWeight(Probe, out listed);
				Check(ref ok, Mathf.Approximately(CustomIslandSpawner.QuietMinutes, qb) && listed && Mathf.Approximately(w, 9f), "let go: read now (quietMinutes " + CustomIslandSpawner.QuietMinutes + ", probe weight " + w + ")");
			}
			finally
			{
				File.WriteAllBytes(path, backup);
				CustomIslandSpawner.LoadPool(true);
			}
			CustomIslandSpawner.PoolWeight(Probe, out listed);
			Check(ref ok, Mathf.Approximately(CustomIslandSpawner.QuietMinutes, q0) && !listed, "the file is put back (quietMinutes " + CustomIslandSpawner.QuietMinutes + ", probe gone " + !listed + ")");
			if (ok) Log("PASS: spawnpool.txt read late when just saved, values kept while held open, read once let go");
			else Fail("spawnpool.txt live read");
		}

		[ConsoleCommand(name: "CIBrushSunkCave", docs: "Dev, editor: a Raise and a Lower stroke over a cave piece sunk 4 m under the ground leave it where it is (c3f26693), while a log sunk as deep and a cave piece on the ground go up and down with the ground; undo puts all back")]
		public static void BrushSunkCaveCommand()
		{
			StartTest(BrushSunkCaveRoutine());
		}

		static IEnumerator BrushSunkCaveRoutine()
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("open the editor first"); yield break; }
			bool ok = true;
			EditorUI.SetTab(TAB.TerrainEdit);
			terraineditor editor = UnityEngine.Object.FindObjectOfType<terraineditor>();
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4242, Radius = 70f, Height = 40f, SeaFloor = IslandGenSettings.SeaFloorShallow, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f });
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();
			Terrain terrain = terraineditor.terrain;
			Vector3 o = terrain.transform.position;
			Func<Vector3, float> ground = p => terrain.SampleHeight(p) + o.y;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;

			float radius = terraineditor.brushRadius, strength = terraineditor.strength;
			terraineditor.TerrainModificationAction action = terraineditor.modificationAction;
			Check(ref ok, CaveMountain.Wants("BalboaCave") && !CaveMountain.Wants("Log"), "BalboaCave is a cave piece, Log is not");
			Vector3 at = o + new Vector3(500f, 0f, 500f); // (on the land: the sea floor may not go lower)
			Vector3 pc = at + new Vector3(-2f, 0f, 0f), pl = at + new Vector3(2f, 0f, 0f), pg = at + new Vector3(0f, 0f, 2f);
			pc.y = ground(pc) - 4f; pl.y = ground(pl) - 4f; pg.y = ground(pg);
			EditorGameObject cave = PlaceForTest("BalboaCave", pc, placed), log = PlaceForTest("Log", pl, placed), onGround = PlaceForTest("BalboaCave", pg, placed);
			if (cave == null || log == null || onGround == null) { Fail("could not place the test pieces"); yield break; }
			yield return null;
			cave.transform.position = pc; log.transform.position = pl; onGround.transform.position = pg;

			terraineditor.brushRadius = 20f; terraineditor.strength = 10f;
			foreach (terraineditor.TerrainModificationAction a in new[] { terraineditor.TerrainModificationAction.Raise, terraineditor.TerrainModificationAction.Lower })
			{
				terraineditor.modificationAction = a;
				float g0 = ground(at);
				editor.SimulateStroke(at, 10, 0.05f);
				yield return null;
				float d = ground(at) - g0, dc = cave.transform.position.y - pc.y, dl = log.transform.position.y - pl.y, dg = onGround.transform.position.y - pg.y;
				Check(ref ok, Mathf.Abs(d) > 2f, a + ": the ground moved " + d.ToString("F2") + " m");
				Check(ref ok, Mathf.Abs(dc) < 0.01f, a + ": the sunk cave piece stays (" + dc.ToString("F2") + " m)");
				Check(ref ok, Mathf.Abs(dl) > 1f && Mathf.Sign(dl) == Mathf.Sign(d), a + ": the sunk log follows the ground (" + dl.ToString("F2") + " m)");
				Check(ref ok, Mathf.Abs(dg) > 1f && Mathf.Sign(dg) == Mathf.Sign(d), a + ": the cave piece on the ground follows it (" + dg.ToString("F2") + " m)");
				CommandUndoRedo.UndoRedoManager.Undo();
				yield return null;
				float back = Mathf.Max(Mathf.Abs(ground(at) - g0), Mathf.Max((cave.transform.position - pc).magnitude, Mathf.Max((log.transform.position - pl).magnitude, (onGround.transform.position - pg).magnitude)));
				Check(ref ok, back < 0.02f, a + " undone: ground and pieces as before (" + back.ToString("F3") + " m off)");
			}
			terraineditor.brushRadius = radius; terraineditor.strength = strength; terraineditor.modificationAction = action;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			CommandUndoRedo.UndoRedoManager.Clear();
			if (ok) Log("PASS: a brush stroke leaves a sunk cave piece where it is; other pieces follow the ground; undo restores");
			else Fail("brush over a sunk cave piece");
		}
	}
}
