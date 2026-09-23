using System;
using System.Collections;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Automated smoke tests, run from the RML console. Results are logged with a [CITEST] prefix
	/// (visible in the F10 console and in Player.log).
	///   CITest       - main menu or editor: opens the editor, sculpts, places objects, saves, wipes, loads, verifies.
	///   CITestWorld  - in a world (host): spawns the island saved by CITest in front of the raft and verifies it.
	/// </summary>
	public static class DevTests
	{
		public const string TestIsland = "citest";
		const int ObjectsToPlace = 3;

		static void Log(string msg) { Debug.Log("[CITEST] " + msg); }
		static void Fail(string msg) { Debug.LogError("[CITEST] FAIL: " + msg); }

		[ConsoleCommand(name: "CITest", docs: "Dev: automated editor test (sculpt, place, save, load, verify)")]
		public static void RunEditorTest()
		{
			DynamicIslands.instance.StartCoroutine(EditorTest());
		}

		[ConsoleCommand(name: "CITestWorld", docs: "Dev: spawns the island saved by CITest in front of the raft and verifies it")]
		public static void RunWorldTest()
		{
			DynamicIslands.instance.StartCoroutine(WorldTest());
		}

		[ConsoleCommand(name: "CILook", docs: "Dev: renders spawned custom islands from a temporary camera to Mods\\DynamicIslands\\view_<island>.png")]
		public static void RenderIslandViews()
		{
			DynamicIslands.instance.StartCoroutine(RenderViews());
		}

		static IEnumerator RenderViews()
		{
			yield return new WaitForEndOfFrame();
			Camera main = Camera.main;
			if (main != null)
				Log("Main camera: far clip " + main.farClipPlane + ", renders layer " + IslandSpawner.TerrainLayer + ": " + ((main.cullingMask & (1 << IslandSpawner.TerrainLayer)) != 0));

			foreach (GameObject root in UnityEngine.Object.FindObjectsOfType<GameObject>().Where(g => g.transform.parent == null && g.name.StartsWith("CustomIsland_")))
			{
				Terrain terrain = root.GetComponentInChildren<Terrain>();
				if (terrain == null) continue;
				Vector3 centre = HighestPoint(terrain);
				Log(root.name + ": terrain enabled=" + terrain.enabled + ", material=" + (terrain.materialTemplate != null ? terrain.materialTemplate.shader.name : "(default)") + ", hill top y=" + centre.y.ToString("F1"));

				var camGO = new GameObject("CITest_ViewCamera");
				Camera cam = camGO.AddComponent<Camera>();
				if (main != null) cam.CopyFrom(main);
				cam.farClipPlane = 3000f;
				cam.transform.position = centre + new Vector3(0, 40f, -180f);
				cam.transform.LookAt(centre);
				var rt = new RenderTexture(1280, 720, 24);
				cam.targetTexture = rt;
				cam.Render();

				RenderTexture.active = rt;
				var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
				tex.Apply();
				RenderTexture.active = null;

				string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "view_" + root.name.Substring("CustomIsland_".Length) + ".png"));
				File.WriteAllBytes(file, tex.EncodeToPNG());
				Log("Rendered view to " + file);

				cam.targetTexture = null;
				UnityEngine.Object.Destroy(camGO);
				UnityEngine.Object.Destroy(rt);
				UnityEngine.Object.Destroy(tex);
			}
		}

		/// <summary>World position of the terrain's highest heightmap sample.</summary>
		static Vector3 HighestPoint(Terrain terrain)
		{
			TerrainData d = terrain.terrainData;
			int res = d.heightmapResolution;
			float[,] h = d.GetHeights(0, 0, res, res);
			int bx = 0, bz = 0;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					if (h[z, x] > h[bz, bx]) { bx = x; bz = z; }
			return terrain.transform.position + new Vector3(bx / (float)(res - 1) * d.size.x, h[bz, bx] * d.size.y, bz / (float)(res - 1) * d.size.z);
		}

		static IEnumerator EditorTest()
		{
			Log("Editor test started");
			if (!DynamicIslands.InEditor())
			{
				Log("Opening editor");
				DynamicIslands.LoadEditor(new string[0]);
			}

			float timeout = Time.realtimeSinceStartup + 90f;
			while (!(DynamicIslands.InEditor() && PlaceableCatalog.IsBuilt && DynamicIslands.EditorGizmoHandler != null))
			{
				if (Time.realtimeSinceStartup > timeout)
				{
					Fail("editor/catalog not ready after 90s (InEditor=" + DynamicIslands.InEditor() + ", catalog=" + PlaceableCatalog.IsBuilt + ")");
					yield break;
				}
				yield return new WaitForSeconds(0.5f);
			}
			Log("Editor ready. Catalog has " + PlaceableCatalog.Names.Count() + " objects: " + string.Join(", ", PlaceableCatalog.Names.Take(15).ToArray()) + (PlaceableCatalog.Names.Count() > 15 ? ", ..." : ""));

			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			Log("Terrain size " + data.size + ", resolution " + data.heightmapResolution);

			// 1. Sculpt a round hill in the middle that rises 15 m above the water level
			int res = data.heightmapResolution;
			float[,] heights = data.GetHeights(0, 0, res, res);
			float peak = (IslandFile.DefaultWaterLevel + 15f) / data.size.y;
			int cx = res / 2, cy = res / 2, radius = res / 8;
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
				{
					float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
					if (d < 1f) heights[y, x] = Mathf.Max(heights[y, x], peak * Mathf.SmoothStep(1f, 0f, d));
				}
			data.SetHeights(0, 0, heights);
			TerrainPainter.Setup(terrain, IslandFile.DefaultWaterLevel);
			yield return null;

			// 2. Place objects on top of the hill
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;

			string[] names = PlaceableCatalog.Names.Take(ObjectsToPlace).ToArray();
			if (names.Length == 0) { Fail("catalog is empty"); yield break; }
			Vector3 centre = terrain.transform.position + new Vector3(data.size.x / 2f, 0, data.size.z / 2f);
			for (int i = 0; i < names.Length; i++)
			{
				Vector3 pos = centre + new Vector3((i - 1) * 8f, 0, 0);
				pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
				GameObject go = PlaceableCatalog.Spawn(names[i], placed);
				go.transform.position = pos;
				go.transform.rotation = Quaternion.Euler(0, i * 45f, 0);
				go.AddComponent<EditorGameObject>().GameObjectName = names[i];
			}
			Log("Placed " + names.Length + " objects: " + string.Join(", ", names));

			// Point the camera at the hill for a screenshot
			try
			{
				Transform cam = Camera.main.transform;
				cam.position = centre + new Vector3(0, IslandFile.DefaultWaterLevel + 40f, -70f);
				cam.LookAt(centre + new Vector3(0, IslandFile.DefaultWaterLevel, 0));
			}
			catch (Exception e) { Log("Could not move camera: " + e.Message); }
			yield return new WaitForSeconds(1f);

			float[,] savedHeights = data.GetHeights(0, 0, res, res);
			IslandFile expected = IslandFile.Capture(TestIsland, terrain, placed);

			// 3. Save
			string path = IslandSpawner.PathFor(TestIsland);
			if (File.Exists(path)) File.Delete(path);
			DynamicIslands.SaveIsland(TestIsland);
			if (!File.Exists(path)) { Fail("save did not create " + path); yield break; }
			Log("Saved " + Path.GetFullPath(path) + " (" + new FileInfo(path).Length + " bytes)");

			// 4. Wipe: flatten terrain and remove objects
			data.SetHeights(0, 0, new float[res, res]);
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			Log("Wiped terrain and objects (" + placed.GetComponentsInChildren<EditorGameObject>().Length + " objects left)");

			// 5. Load and compare
			DynamicIslands.LoadIsland(TestIsland);
			yield return null;
			yield return null;

			float[,] loaded = data.GetHeights(0, 0, res, res);
			float maxDiff = 0;
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
					maxDiff = Mathf.Max(maxDiff, Mathf.Abs(loaded[y, x] - savedHeights[y, x]));
			bool heightsOk = maxDiff < 0.0005f;
			Log("Heights: max difference after load = " + maxDiff.ToString("F6") + " (" + (heightsOk ? "ok" : "TOO LARGE") + ")");

			IslandFile actual = IslandFile.Capture(TestIsland, terrain, placed);
			bool countOk = actual.Objects.Count == expected.Objects.Count;
			float maxPosDiff = 0;
			if (countOk)
			{
				foreach (IslandObject e in expected.Objects)
				{
					IslandObject match = actual.Objects.Where(a => a.Name == e.Name).OrderBy(a => (a.Position - e.Position).sqrMagnitude).FirstOrDefault();
					maxPosDiff = Mathf.Max(maxPosDiff, match == null ? float.MaxValue : (match.Position - e.Position).magnitude);
				}
			}
			bool objectsOk = countOk && maxPosDiff < 0.01f;
			Log("Objects: " + actual.Objects.Count + "/" + expected.Objects.Count + " restored, max position error " + maxPosDiff.ToString("F4") + " m (" + (objectsOk ? "ok" : "WRONG") + ")");

			if (heightsOk && objectsOk) Log("PASS: editor save/load round trip");
			else Fail("editor save/load round trip");
		}

		static IEnumerator WorldTest()
		{
			Log("World test started");
			if (!LoadSceneManager.IsGameSceneLoaded) { Fail("not in a game world"); yield break; }
			if (!File.Exists(IslandSpawner.PathFor(TestIsland))) { Fail("run CITest in the editor first (no " + TestIsland + IslandFile.Extension + ")"); yield break; }

			DynamicIslands.SpawnIslandCommand(new[] { TestIsland });

			GameObject root = null;
			float timeout = Time.realtimeSinceStartup + 90f;
			while (root == null && Time.realtimeSinceStartup < timeout)
			{
				yield return new WaitForSeconds(0.5f);
				root = GameObject.Find("CustomIsland_" + TestIsland);
			}
			if (root == null) { Fail("island root did not appear within 90s"); yield break; }

			Terrain terrain = root.GetComponentInChildren<Terrain>();
			int objects = root.transform.Find("Objects") != null ? root.transform.Find("Objects").childCount : 0;
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			Vector3 top = HighestPoint(terrain);
			float hillTop = top.y;

			Log("Island root at " + root.transform.position + ", terrain " + terrain.terrainData.size + " (" + terrain.terrainData.heightmapResolution + " samples) at " + terrain.transform.position +
				", highest point " + top + ", raft at " + (raft != null ? raft.transform.position.ToString() : "?"));
			Log("Terrain layer " + terrain.gameObject.layer + ", hill top at world Y " + hillTop.ToString("F1") + " (expected about 15 above sea level)");
			Log("Objects spawned: " + objects);

			bool ok = terrain != null && objects > 0 && hillTop > 5f && hillTop < 30f;
			if (ok) Log("PASS: island spawned in world");
			else Fail("island spawned but looks wrong (objects=" + objects + ", hillTop=" + hillTop + ")");
		}
	}
}
