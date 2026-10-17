using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>The editor's scatter (ROADMAP LM11): things to gather spread round a point - on land, kept off what was
		/// already there - and Raft's sea finds on the sea floor; one undo takes each scatter back.</summary>
		[ConsoleCommand(name: "CIScatterTool", docs: "Dev, editor with an island open: the scatter - pineapples on land kept 3 m off the island's objects, seaweed on the sea floor, each taken back by one undo")]
		public static void ScatterToolCommand(string[] args) { StartTest(ScatterToolRoutine()); }

		static IEnumerator ScatterToolRoutine()
		{
			if (!DynamicIslands.InEditor() || terraineditor.terrain == null) { Fail("CIScatterTool (in the editor, an island open)"); yield break; }
			bool ok = true;
			float sea = DynamicIslands.EditorWaterLevel;
			Terrain t = terraineditor.terrain;
			Vector3 mid = t.transform.position + new Vector3(t.terrainData.size.x / 2f, 0f, t.terrainData.size.z / 2f);
			// (the editor's new island is open sea since the 10-16 cleanup - no saved island opens with it: then the tests'
			// sample island is opened for the land)
			Vector3 g0, n0g;
			if (!PlacementOptions.GroundAt(new Vector3(mid.x, sea + 200f, mid.z), out g0, out n0g) || g0.y < sea + 0.3f)
			{
				bool opened = DynamicIslands.LoadIsland(SaveSampleIsland());
				yield return null;
				yield return null;
				RemoveSampleIsland();
				if (!opened || terraineditor.terrain == null) { Fail("scatter tool: the sample island didn't open"); yield break; }
				t = terraineditor.terrain;
				Vector2 c = IslandSpawner.LandCentre(SampleIsland());
				mid = t.transform.position + new Vector3(c.x, 0f, c.y);
				Log("The sample island opened (the island open had no land in the middle)");
			}
			GameObject root = GameObject.Find("PlacedObjects");
			Func<List<Vector3>> before = () => root.GetComponentsInChildren<EditorGameObject>(false).Select(e => e.transform.position).ToList();
			Func<int> count = () => root.GetComponentsInChildren<EditorGameObject>(false).Length;

			// On land: pineapples, 3 m off everything already placed
			List<Vector3> had = before();
			int n0 = count();
			List<GameObject> land = ScatterTool.Scatter("Pickup_Landmark_PineappleLandmark", mid, 8, Mathf.Min(60f, t.terrainData.size.x / 3f), 3f, false, 4242);
			Check(ref ok, land.Count >= 4, "pineapples scattered on land: " + land.Count + " of 8");
			Check(ref ok, land.All(g => g.transform.position.y > sea), "all on land, above the sea");
			float nearest = land.Count == 0 || had.Count == 0 ? 99f : land.Min(g => had.Min(p => Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(g.transform.position.x, 0, g.transform.position.z))));
			Check(ref ok, nearest > 2.9f, "kept off the island's own objects (nearest " + nearest.ToString("F1") + " m)");
			Check(ref ok, land.All(g => g.GetComponent<EditorGameObject>() != null), "each an editor object (saved with the island)");
			CommandUndoRedo.UndoRedoManager.Undo();
			Check(ref ok, count() == n0, "one undo takes them all back (" + count() + " objects, " + n0 + " before)");

			// Under water: seaweed on the sea floor round the island
			int n1 = count();
			List<GameObject> sea1 = ScatterTool.Scatter("SeaVine3_klump", mid, 6, t.terrainData.size.x / 2f, 1f, true, 4343);
			Check(ref ok, sea1.Count >= 3, "seaweed scattered under water: " + sea1.Count + " of 6");
			Check(ref ok, sea1.All(g => g.transform.position.y < sea - 0.3f), "all on the sea floor");
			Check(ref ok, ScatterTool.OnlyUnderWater("SeaVine3_klump") && ScatterTool.OnlyUnderWater("Pickup_Landmark_Scrap 1_OceanBottom") && !ScatterTool.OnlyUnderWater("Pickup_Landmark_PineappleLandmark"),
				"sea finds go under water by themselves");
			CommandUndoRedo.UndoRedoManager.Undo();
			Check(ref ok, count() == n1, "one undo takes the seaweed back");
			if (ok) Log("PASS: scatter tool"); else Fail("scatter tool");
		}
	}
}
