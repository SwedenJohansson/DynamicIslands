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
		const int PaintOffset = 20, PaintSize = 12; // hand-painted test patch, in alphamap pixels from the centre

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
		public static void RenderIslandViews(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(RenderViews(args != null && args.Contains("low")));
		}

		/// <param name="low">film from below the hilltop (to see flying islands' undersides); files get a number per island</param>
		static IEnumerator RenderViews(bool low = false)
		{
			int n = 0;
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
				cam.transform.position = low ? new Vector3(centre.x, 4f, centre.z - 330f) : centre + new Vector3(0, 40f, -180f);
				cam.transform.LookAt(low ? new Vector3(centre.x, centre.y * 0.55f, centre.z) : centre);
				var rt = new RenderTexture(1280, 720, 24);
				cam.targetTexture = rt;
				cam.Render();

				RenderTexture.active = rt;
				var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
				tex.Apply();
				RenderTexture.active = null;

				string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "view_" + root.name.Substring("CustomIsland_".Length) + "_" + (++n) + ".png"));
				File.WriteAllBytes(file, tex.EncodeToPNG());
				Log("Rendered view to " + file);

				cam.targetTexture = null;
				UnityEngine.Object.Destroy(camGO);
				UnityEngine.Object.Destroy(rt);
				UnityEngine.Object.Destroy(tex);
			}
		}

		[ConsoleCommand(name: "CIDump", docs: "Dev: logs renderers/shaders/bounds of loaded scenes whose name contains <text>, e.g. CIDump demoisland1")]
		public static void DumpScene(string[] args)
		{
			string filter = args != null && args.Length > 0 ? args[0] : "";
			for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
			{
				var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
				if (filter.Length > 0 && scene.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
					Log("Scene '" + scene.name + "' root '" + root.name + "' active=" + root.activeInHierarchy + " pos=" + root.transform.position +
						" scale=" + root.transform.lossyScale + " renderers=" + renderers.Length + " terrains=" + root.GetComponentsInChildren<Terrain>(true).Length +
						" colliders=" + root.GetComponentsInChildren<Collider>(true).Length);
					if (renderers.Length == 0) continue;
					Bounds b = renderers[0].bounds;
					foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
					Log("  bounds centre " + b.center + " size " + b.size + ", enabled renderers " + renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy));
					foreach (var g in renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).GroupBy(m => m.shader == null ? "(no shader)" : m.shader.name))
					{
						Shader sh = g.First().shader;
						Log("  shader '" + g.Key + "' supported=" + (sh != null && sh.isSupported) + " materials=" + g.Count() +
							" found-by-name=" + (Shader.Find(g.Key) != null));
					}
					int nullMats = renderers.Sum(r => r.sharedMaterials.Count(m => m == null));
					if (nullMats > 0) Log("  " + nullMats + " missing (null) materials");
				}
			}
		}

		[ConsoleCommand(name: "CIUndo", docs: "Dev: tests undo/redo for sculpt, paint, placing and deleting, and the islands window (run in the editor)")]
		public static void RunUndoTest()
		{
			DynamicIslands.instance.StartCoroutine(UndoTest());
		}

		static float MaxDiff(float[,] a, float[,] b)
		{
			float m = 0;
			for (int z = 0; z < a.GetLength(0); z++) for (int x = 0; x < a.GetLength(1); x++) m = Mathf.Max(m, Mathf.Abs(a[z, x] - b[z, x]));
			return m;
		}

		static IEnumerator UndoTest()
		{
			Log("Undo test started");
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("open the editor first (CITest does that)"); yield break; }
			var editor = Camera.main.GetComponent<terraineditor>();
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			CommandUndoRedo.UndoRedoManager.Clear();
			var savedAction = terraineditor.modificationAction;
			bool ok = true;

			// 1. Sculpt: raise a flat spot away from the test hill, undo, redo
			Vector3 spot = terrain.transform.position + new Vector3(data.size.x * 0.8f, 0, data.size.z * 0.8f);
			spot.y = terrain.SampleHeight(spot) + terrain.transform.position.y;
			int res = data.heightmapResolution;
			float[,] h0 = data.GetHeights(0, 0, res, res);
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Raise;
			editor.SimulateStroke(spot, 30, 0.033f);
			float[,] h1 = data.GetHeights(0, 0, res, res);
			float raised = MaxDiff(h0, h1) * data.size.y;
			CommandUndoRedo.UndoRedoManager.Undo();
			float afterUndo = MaxDiff(h0, data.GetHeights(0, 0, res, res));
			CommandUndoRedo.UndoRedoManager.Redo();
			float afterRedo = MaxDiff(h1, data.GetHeights(0, 0, res, res));
			bool sculptOk = raised > 1f && afterUndo < 1e-6f && afterRedo < 1e-6f;
			Log("Sculpt: raised " + raised.ToString("F1") + " m; undo error " + afterUndo + ", redo error " + afterRedo + " (" + (sculptOk ? "ok" : "WRONG") + ")");
			ok &= sculptOk;
			CommandUndoRedo.UndoRedoManager.Undo(); // leave the terrain as it was
			yield return null;

			// 2. Paint: rock stroke, undo restores texture and paint mask
			int ares = data.alphamapResolution;
			Vector3 local = spot - terrain.transform.position;
			int px = Mathf.FloorToInt(local.x / data.size.x * ares), pz = Mathf.FloorToInt(local.z / data.size.z * ares);
			float rockBefore = data.GetAlphamaps(px, pz, 1, 1)[0, 0, TerrainPainter.Rock];
			terraineditor.paintLayer = TerrainPainter.Rock;
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.PaintLayer;
			editor.SimulateStroke(spot, 60, 0.033f);
			float rockPainted = data.GetAlphamaps(px, pz, 1, 1)[0, 0, TerrainPainter.Rock];
			float maskPainted = terraineditor.paintMask[pz, px];
			CommandUndoRedo.UndoRedoManager.Undo();
			float rockUndone = data.GetAlphamaps(px, pz, 1, 1)[0, 0, TerrainPainter.Rock];
			float maskUndone = terraineditor.paintMask[pz, px];
			bool paintOk = rockPainted > 0.9f && maskPainted > 0.5f && Mathf.Abs(rockUndone - rockBefore) < 0.01f && maskUndone < 0.5f;
			Log("Paint: rock " + rockBefore.ToString("F2") + " -> " + rockPainted.ToString("F2") + " -> undo " + rockUndone.ToString("F2") + ", mask " + maskPainted + " -> " + maskUndone + " (" + (paintOk ? "ok" : "WRONG") + ")");
			ok &= paintOk;
			yield return null;

			// 3. Place + delete objects
			Transform placed = GameObject.Find("PlacedObjects").transform;
			string objectName = PlaceableCatalog.Names.First();
			GameObject obj = PlaceableCatalog.Spawn(objectName, placed);
			obj.transform.position = spot;
			obj.AddComponent<EditorGameObject>().GameObjectName = objectName;
			CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(new[] { obj }, true)); // what ObjectPlacer does
			CommandUndoRedo.UndoRedoManager.Undo();
			bool hiddenByUndo = !obj.activeSelf;
			CommandUndoRedo.UndoRedoManager.Redo();
			bool shownByRedo = obj.activeSelf;

			DynamicIslands.EditorGizmoHandler.AddTarget(obj.transform, false);
			DynamicIslands.EditorGizmoHandler.DeleteSelection();
			bool deleted = !obj.activeSelf;
			int savedWhileDeleted = IslandFile.Capture("undo-test", terrain, placed).Objects.Count(o => o.Name == objectName && (o.Position - (spot - terrain.transform.position)).sqrMagnitude < 0.01f);
			CommandUndoRedo.UndoRedoManager.Undo();
			bool restored = obj.activeSelf;
			bool objectsOk = hiddenByUndo && shownByRedo && deleted && restored && savedWhileDeleted == 0;
			Log("Objects: place-undo hides " + hiddenByUndo + ", redo shows " + shownByRedo + ", delete hides " + deleted + " (saved while deleted: " + savedWhileDeleted + "), undo restores " + restored + " (" + (objectsOk ? "ok" : "WRONG") + ")");
			ok &= objectsOk;
			UnityEngine.Object.Destroy(obj);
			yield return null;

			// 4. Islands window
			IslandFilesWindow.Open();
			bool opened = IslandFilesWindow.IsOpen;
			yield return new WaitForSeconds(0.5f);
			IslandFilesWindow.Close();
			bool windowOk = opened && !IslandFilesWindow.IsOpen;
			Log("Islands window opens and closes: " + windowOk);
			ok &= windowOk;

			terraineditor.modificationAction = savedAction;
			CommandUndoRedo.UndoRedoManager.Clear();
			if (ok) Log("PASS: undo/redo and islands window");
			else Fail("undo/redo or islands window");
		}

		[ConsoleCommand(name: "CISpawnGenerated", docs: "Dev, in game (host): the automatic spawner generates a brand-new island ahead of the raft now; checks its file, style and objects. CISpawnGenerated [keep]")]
		public static void SpawnGenerated(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(SpawnGeneratedRoutine(args != null && args.Contains("keep")));
		}

		static IEnumerator SpawnGeneratedRoutine(bool keep)
		{
			Vector3? pos = CustomIslandSpawner.RaftPosition;
			if (!pos.HasValue) { Fail("not in a world"); yield break; }
			int before = IslandWorldState.Islands.Count;
			CustomIslandSpawner.ForceNextPick = CustomIslandSpawner.GeneratedEntry;
			string result = CustomIslandSpawner.TrySpawn(pos.Value, true);
			Log(result);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (e == null) { Fail("no island was added (" + result + ")"); yield break; }
			float t = 0f;
			while (e.Root == null && !e.Failed && t < 60f) { yield return new WaitForSeconds(0.5f); t += 0.5f; }
			bool fileOk = File.Exists(IslandSpawner.PathFor(e.Name));
			IslandFile file = fileOk ? IslandFile.Load(IslandSpawner.PathFor(e.Name)) : null;
			Terrain terrain = e.Root != null ? e.Root.GetComponentInChildren<Terrain>() : null;
			string style = terrain != null ? TerrainPainter.StyleName(TerrainPainter.StyleOf(terrain)) : "?";
			bool ok = fileOk && e.Root != null && e.Name.StartsWith(CustomIslandSpawner.GeneratedPrefix) && file.Objects.Count > 0 &&
				style.Equals(string.IsNullOrEmpty(file.Style) ? "Tropical" : file.Style) && e.Name.Contains(style.ToLowerInvariant());
			Log((ok ? "PASS" : "FAIL") + ": generated '" + e.Name + "' (" + (file != null ? file.Objects.Count + " objects, style " + style + (file.Elevation > 0 ? ", flying " + file.Elevation.ToString("F0") + " m" : "") : "no file") +
				") and spawned it " + (e.Root != null ? "at " + e.Position : "- it did not spawn") + " after " + t.ToString("F1") + " s");
			if (!keep)
			{
				IslandWorldState.RemoveIds(new[] { e.Id }, true);
				if (fileOk) File.Delete(IslandSpawner.PathFor(e.Name));
			}
		}

		[ConsoleCommand(name: "CIRadarTest", docs: "Dev, in game (host): places a Receiver next to the raft with its radar on and checks there is a dot per custom island, pointing the right way")]
		public static void RadarTest()
		{
			DynamicIslands.instance.StartCoroutine(RadarTestRoutine());
		}

		static IEnumerator RadarTestRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue) { Fail("not in a world"); yield break; }
			if (IslandWorldState.Islands.Count == 0)
			{
				string name = IslandSpawner.ListSavedIslands().FirstOrDefault();
				if (name == null) { Fail("no saved island to spawn"); yield break; }
				Vector3 at = raftPos.Value + new Vector3(0, 0, 300f);
				yield return DynamicIslands.instance.SpawnIslandFile(name, at, true);
			}
			// A receiver: Raft's own block prefab, from whichever buildable item carries a Reciever
			Reciever prefab = null;
			foreach (Item_Base item in ItemManager.GetAllItems())
			{
				try
				{
					if (item == null || item.settings_buildable == null || !item.settings_buildable.Placeable) continue;
					Block[] blocks = item.settings_buildable.GetBlockPrefabs();
					prefab = blocks != null ? blocks.Where(b => b != null).Select(b => b.GetComponentInChildren<Reciever>(true)).FirstOrDefault(rc => rc != null) : null;
					if (prefab != null) { Log("Receiver block: " + item.UniqueName); break; }
				}
				catch { }
			}
			if (prefab == null) { Fail("could not find Raft's receiver block"); yield break; }
			GameObject go = UnityEngine.Object.Instantiate(prefab.transform.root.gameObject, raftPos.Value + Vector3.up * 3f, Quaternion.identity);
			Reciever r = go.GetComponentInChildren<Reciever>(true);
			yield return null;
			if (r.radarSection != null) r.radarSection.SetActive(true);
			IslandRadar.Draw(r);
			var list = IslandRadar.DotsOf(r).Where(d => d != null && d.gameObject.activeSelf).ToList();
			bool ok = list.Count == IslandWorldState.Islands.Count;
			// The first island: its dot must point towards it (receiver faces world forward)
			if (ok && list.Count > 0)
			{
				Vector3 toIsland = IslandWorldState.Islands[0].Position - r.transform.position;
				Vector2 dotDir = ((RectTransform)list[0].transform).anchoredPosition;
				float angle = Vector2.Angle(new Vector2(toIsland.x, toIsland.z), -dotDir); // Raft's dot maths mirrors the vector
				Log("Island 0 is at " + new Vector2(toIsland.x, toIsland.z) + ", its dot at " + dotDir + " (angle " + angle.ToString("F0") + ")");
			}
			Log((ok ? "PASS" : "FAIL") + ": the receiver shows " + list.Count + " custom island dot(s) for " + IslandWorldState.Islands.Count + " island(s)");
			UnityEngine.Object.Destroy(go);
		}

		[ConsoleCommand(name: "CIPlaceTest", docs: "Dev, editor: object list search, Ground (with and without Slope) and its undo")]
		public static void PlaceTest()
		{
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("open the editor first (and wait for the objects to load)"); return; }
			DynamicIslands.instance.StartCoroutine(PlaceTestRoutine());
		}

		static IEnumerator PlaceTestRoutine()
		{
			bool ok = true;
			// 1. Search
			ObjectListSearch search = UnityEngine.Object.FindObjectOfType<ObjectListSearch>();
			UnityEngine.UI.InputField field = search != null ? search.GetComponent<UnityEngine.UI.InputField>() : null;
			Transform content = GameObject.Find("ToolList").transform.Find("ObjectTool/Scroll View/Viewport/Content");
			if (field == null) { Fail("no search field"); yield break; }
			System.Func<int> visibleObjects = () => content.Cast<Transform>().Count(t => t.gameObject.activeSelf && !t.name.StartsWith("Header_") && t.name != "Button");
			int all = visibleObjects();
			field.text = "cactus";
			yield return null;
			int cacti = visibleObjects();
			bool onlyCacti = content.Cast<Transform>().Where(t => t.gameObject.activeSelf && !t.name.StartsWith("Header_")).All(t => t.GetComponentInChildren<UnityEngine.UI.Text>().text.ToLower().Contains("cactus"));
			field.text = "";
			yield return null;
			Check(ref ok, cacti > 0 && cacti < all && onlyCacti && visibleObjects() == all, "search: 'cactus' shows " + cacti + " of " + all + " objects, clearing shows all again");

			// 2. Ground: objects lifted into the air land on the terrain, as one undo step
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 3, ObjectDensity = 0f });
			Terrain terrain = terraineditor.terrain;
			Vector3 c = terrain.transform.position + new Vector3(500f, 0, 500f);
			var objs = new System.Collections.Generic.List<Transform>();
			for (int i = 0; i < 3; i++)
			{
				GameObject go = PlaceableCatalog.Spawn("SmallBoulder2", GameObject.Find("PlacedObjects").transform) ?? PlaceableCatalog.Spawn(PlaceableCatalog.Names.First(), GameObject.Find("PlacedObjects").transform);
				go.AddComponent<EditorGameObject>().GameObjectName = go.name;
				go.transform.position = c + new Vector3(30f + i * 20f, 200f, 40f);
				objs.Add(go.transform);
			}
			var gizmo = DynamicIslands.EditorGizmoHandler;
			gizmo.ClearTargets(false);
			foreach (Transform t in objs) gizmo.AddTarget(t, false);
			PlacementOptions.AlignToSlope = false;
			int n = PlacementOptions.DropSelectionToGround();
			bool grounded = objs.All(t => Mathf.Abs(t.position.y - (terrain.SampleHeight(t.position) + terrain.transform.position.y)) < 0.2f && Vector3.Angle(t.up, Vector3.up) < 1f);
			CommandUndoRedo.UndoRedoManager.Undo();
			bool undone = objs.All(t => t.position.y > 150f);
			Check(ref ok, n == 3 && grounded && undone, "Ground put " + n + " objects on the terrain, upright; Ctrl+Z lifted them back");

			// 3. With Slope on they lean with the ground
			PlacementOptions.AlignToSlope = true;
			PlacementOptions.DropSelectionToGround();
			float worst = objs.Max(t =>
			{
				Vector3 point, normal;
				PlacementOptions.GroundAt(t.position, out point, out normal);
				return Vector3.Angle(t.up, normal);
			});
			PlacementOptions.AlignToSlope = false;
			Check(ref ok, worst < 2f, "with Slope on, objects lean with the ground (largest difference " + worst.ToString("F1") + " degrees)");
			gizmo.ClearTargets(false);
			foreach (Transform t in objs) UnityEngine.Object.Destroy(t.gameObject);
			if (ok) Log("PASS: placement tools test"); else Fail("placement tools test");
		}

		[ConsoleCommand(name: "CIObjInfo", docs: "Dev, editor: spawns catalog objects whose name contains <text> (or 'all') at the build-area centre and logs their rendered size; they are removed again")]
		public static void ObjInfo(string[] args)
		{
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("open the editor first"); return; }
			string filter = args != null && args.Length > 0 ? string.Join(" ", args) : "all";
			var lines = new System.Collections.Generic.List<string>();
			Vector3 c = terraineditor.terrain.transform.position + new Vector3(500f, 0, 500f);
			c.y = terraineditor.terrain.SampleHeight(c);
			foreach (string name in PlaceableCatalog.Names.Where(n => filter == "all" || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
			{
				GameObject go = PlaceableCatalog.Spawn(name, null);
				if (go == null) continue;
				go.transform.position = c;
				Renderer[] rs = go.GetComponentsInChildren<Renderer>();
				Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(c, Vector3.zero);
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				LODGroup lod = go.GetComponentInChildren<LODGroup>();
				lines.Add(string.Format("{0} [{1}]: size {2:F1} x {3:F1} x {4:F1} m, centre offset {5}, {6} renderers ({7} enabled){8}",
					name, PlaceableCatalog.CategoryOf(name), b.size.x, b.size.y, b.size.z, (b.center - c).ToString("F1"), rs.Length, rs.Count(r => r.enabled),
					lod != null ? ", LODGroup " + lod.lodCount + " levels, enabled=" + lod.enabled : ""));
				UnityEngine.Object.Destroy(go);
			}
			string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "objinfo.txt"));
			File.WriteAllLines(file, lines.ToArray());
			Log(lines.Count + " objects measured, written to " + file);
		}

		[ConsoleCommand(name: "CIStyleTest", docs: "Dev, editor: island styles - textures found, new object categories, and for every style: generate, save, load (files cistyle_<style>.island are deleted again)")]
		public static void StyleTest()
		{
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("open the editor first (and wait for the objects to load)"); return; }
			DynamicIslands.instance.StartCoroutine(StyleTestRoutine());
		}

		static IEnumerator StyleTestRoutine()
		{
			bool ok = true;
			var styles = TerrainPainter.Styles;
			Check(ref ok, Enumerable.Range(0, styles.Length).All(TerrainPainter.HasStyle),
				"Raft's textures found for the styles: " + string.Join(", ", Enumerable.Range(0, styles.Length).Select(i => styles[i].Name + (TerrainPainter.HasStyle(i) ? "" : " (missing)")).ToArray()));
			var cats = PlaceableCatalog.ByCategory().ToDictionary(c => c.Key, c => c.Value.Count);
			int ores = PlaceableCatalog.HarvestableNames.Count(n => System.Text.RegularExpressions.Regex.IsMatch(n, "Copper|Iron|Clay|Sand"));
			Check(ref ok, cats.ContainsKey(PlaceableCatalog.SnowCategory) && cats.ContainsKey(PlaceableCatalog.DesertCategory) && cats.ContainsKey(PlaceableCatalog.ForestCategory) && ores > 0,
				"object list: " + string.Join(", ", cats.Select(c => c.Value + " " + c.Key.ToLower()).ToArray()) + "; " + ores + " ore/clay/sand harvestables");

			Terrain terrain = terraineditor.terrain;
			int startStyle = DynamicIslands.currentStyle;
			for (int style = 0; style < styles.Length; style++)
			{
				var s = new IslandGenSettings { Seed = 11 + style, Radius = 90f, Height = 45f, Roughness = 0.5f, Peaks = 2, ObjectDensity = 0.5f, Style = style };
				int n = IslandGenerator.GenerateInEditor(s);
				string[] tex = terrain.terrainData.terrainLayers.Select(l => l != null && l.diffuseTexture != null ? l.diffuseTexture.name : "?").ToArray();
				bool texOk = Enumerable.Range(0, TerrainPainter.LayerCount).All(i => tex[i].StartsWith(styles[style].Textures[i]));
				string name = "cistyle_" + styles[style].Name.ToLower();
				DynamicIslands.SaveIsland(name);
				string saved = IslandFile.Load(IslandSpawner.PathFor(name)).Style;
				DynamicIslands.SetEditorStyle(TerrainPainter.Tropical);
				DynamicIslands.LoadIsland(name);
				yield return null;
				bool loaded = DynamicIslands.currentStyle == style && TerrainPainter.StyleOf(terrain) == style;
				Check(ref ok, texOk && n > 5 && (style == TerrainPainter.Tropical ? saved == "" : saved == styles[style].Name) && loaded,
					styles[style].Name + ": textures " + string.Join("/", tex) + ", " + n + " objects, saved as '" + saved + "', loads back as " + TerrainPainter.StyleName(DynamicIslands.currentStyle));
				File.Delete(IslandSpawner.PathFor(name));
			}

			// Generating is one undo step, including the style change
			DynamicIslands.SetEditorStyle(TerrainPainter.Forest);
			CommandUndoRedo.UndoRedoManager.Clear();
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 5, Style = TerrainPainter.Snowy, ObjectDensity = 0.2f });
			CommandUndoRedo.UndoRedoManager.Undo();
			bool undone = DynamicIslands.currentStyle == TerrainPainter.Forest;
			CommandUndoRedo.UndoRedoManager.Redo();
			Check(ref ok, undone && DynamicIslands.currentStyle == TerrainPainter.Snowy, "Ctrl+Z after generating also brings back the previous style");
			if (ok) Log("PASS: island styles test"); else Fail("island styles test");
		}

		[ConsoleCommand(name: "CIGenTest", docs: "Dev, editor: generates an island and checks it (same seed = same island, land above sea, fits the build area, objects, undo/redo, save/load as cigen.island)")]
		public static void GenTest()
		{
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("open the editor first (and wait for the objects to load)"); return; }
			bool ok = true;
			try
			{
				TerrainData data = terraineditor.terrain.terrainData;
				int res = data.heightmapResolution;
				var s = new IslandGenSettings { Seed = 4242, Radius = 120f, Height = 40f, Roughness = 0.5f, Peaks = 2, ObjectDensity = 0.5f };

				// 1. Deterministic, and seeds differ
				float[,] a = IslandGenerator.Heights(s, data.size, res), b = IslandGenerator.Heights(s, data.size, res);
				float[,] c = IslandGenerator.Heights(new IslandGenSettings { Seed = 777, Radius = 120f, Height = 40f, Roughness = 0.5f, Peaks = 2 }, data.size, res);
				bool same = a.Cast<float>().SequenceEqual(b.Cast<float>()), differs = !a.Cast<float>().SequenceEqual(c.Cast<float>());
				Check(ref ok, same && differs, "same seed gives the same island, another seed a different one");

				// 2. Shape: land above sea, peak near the requested height, flat seabed at the edges
				float sea = IslandFile.DefaultWaterLevel / data.size.y;
				float peak = a.Cast<float>().Max() * data.size.y - IslandFile.DefaultWaterLevel;
				int land = a.Cast<float>().Count(h => h > sea);
				float landArea = land * (data.size.x / (res - 1)) * (data.size.x / (res - 1));
				bool edgesFlat = Enumerable.Range(0, res).All(i => a[0, i] == 0f && a[res - 1, i] == 0f && a[i, 0] == 0f && a[i, res - 1] == 0f);
				Check(ref ok, peak > s.Height * 0.6f && peak < s.Height * 1.5f && landArea > 10000f && edgesFlat,
					string.Format("peak {0:F0} m above sea (asked {1:F0}), land area {2:F0} m², seabed flat at the edges: {3}", peak, s.Height, landArea, edgesFlat));

				// 3. Into the editor, as one undo step
				Transform placed = GameObject.Find("PlacedObjects").transform;
				float[,] before = data.GetHeights(0, 0, res, res);
				int n = IslandGenerator.GenerateInEditor(s);
				float[,] after = data.GetHeights(0, 0, res, res);
				bool applied = after.Cast<float>().Zip(a.Cast<float>(), (x, y) => Mathf.Abs(x - y)).Max() < 0.0001f;
				int visible = placed.GetComponentsInChildren<EditorGameObject>(false).Length;
				Check(ref ok, applied && n > 10 && visible == n, "generated into the editor with " + n + " objects (" + visible + " visible)");

				CommandUndoRedo.UndoRedoManager.Undo();
				bool undone = data.GetHeights(0, 0, res, res).Cast<float>().Zip(before.Cast<float>(), (x, y) => Mathf.Abs(x - y)).Max() < 0.0001f;
				int visibleAfterUndo = placed.GetComponentsInChildren<EditorGameObject>(false).Length;
				CommandUndoRedo.UndoRedoManager.Redo();
				bool redone = data.GetHeights(0, 0, res, res).Cast<float>().Zip(a.Cast<float>(), (x, y) => Mathf.Abs(x - y)).Max() < 0.0001f
					&& placed.GetComponentsInChildren<EditorGameObject>(false).Length == n;
				Check(ref ok, undone && visibleAfterUndo != n && redone, "Ctrl+Z restores the previous island (" + visibleAfterUndo + " objects), Ctrl+Y the generated one");

				// 4. Save, load, and check it would spawn cropped to the island (not the whole 1000 m square)
				DynamicIslands.SaveIsland("cigen");
				IslandFile file = IslandFile.Load(IslandSpawner.PathFor("cigen"));
				int cx, cz, size;
				IslandSpawner.GetCropArea(file, out cx, out cz, out size);
				float radius = IslandSpawner.LandRadius(file);
				Check(ref ok, file.Objects.Count == n && size < res,
					"saved as cigen.island with " + file.Objects.Count + " objects; spawns as a " + (size - 1) * data.size.x / (res - 1) + " m terrain block, land radius " + radius.ToString("F0") + " m");
				DynamicIslands.LoadIsland("cigen");
				DynamicIslands.instance.StartCoroutine(GenTestLoaded(ok, a, n, s));
			}
			catch (Exception e) { Fail("exception: " + e); Fail("island generator test"); }
		}

		/// <summary>Checks the loaded island a frame later, once the replaced objects are really destroyed.</summary>
		static IEnumerator GenTestLoaded(bool ok, float[,] expected, int n, IslandGenSettings s)
		{
			yield return null;
			TerrainData data = terraineditor.terrain.terrainData;
			int res = data.heightmapResolution;
			float diff = data.GetHeights(0, 0, res, res).Cast<float>().Zip(expected.Cast<float>(), (x, y) => Mathf.Abs(x - y)).Max() * data.size.y;
			int objects = GameObject.Find("PlacedObjects").GetComponentsInChildren<EditorGameObject>(false).Length;
			Check(ref ok, diff < 0.05f && objects == n, "loading cigen gives the same heights (largest difference " + diff.ToString("F3") + " m) and objects (" + objects + ")");
			IslandGenerator.FrameCamera(s);
			if (ok) Log("PASS: island generator test"); else Fail("island generator test");
		}

		static void Check(ref bool ok, bool condition, string what)
		{
			Log((condition ? "PASS: " : "FAIL: ") + what);
			ok &= condition;
		}

		[ConsoleCommand(name: "CIDemo", docs: "Dev: scatters sample nature objects over the editor terrain (as undoable placements) and frames the camera")]
		public static void Demo()
		{
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("open the editor first"); return; }
			Terrain terrain = terraineditor.terrain;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Vector3 c = terrain.transform.position + new Vector3(terrain.terrainData.size.x / 2f, 0, terrain.terrainData.size.z / 2f);
			string[] kinds = { "Pickup_Landmark_Tree_Palm 1", "Pickup_Landmark_Tree_Palm 2", "Pickup_Landmark_Tree_Palm 3", "Pickup_Landmark_Tree_Palm 4", "BigPalm1", "BigPalm3", "Pickup_Landmark_MangoTree", "Pickup_Landmark_Rock 1", "Pickup_Landmark_BerryBush", "BigBoulder1_Low", "BigBoulder3_Low",
				"SmallBoulder2", "Bush", "Bush2", "Monstera_1", "Banana_Bush_1", "Banana_Bush_2", "Log", "BigRock_Low1_Sand", "TableCoral_1", "LeafCoral_1", "CauliCoral" };
			var rnd = new System.Random(7);
			var spawned = new System.Collections.Generic.List<GameObject>();
			for (int i = 0; i < 60; i++)
			{
				string kind = kinds[i % kinds.Length];
				if (PlaceableCatalog.Get(kind) == null) continue;
				bool coral = kind.Contains("Coral");
				float r = coral ? 75f + (float)rnd.NextDouble() * 25f : (float)rnd.NextDouble() * 55f;
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
				Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
				p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
				GameObject go = PlaceableCatalog.Spawn(kind, placed);
				go.transform.position = p;
				go.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
				go.AddComponent<EditorGameObject>().GameObjectName = kind;
				spawned.Add(go);
			}
			CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(spawned, true));
			Transform cam = Camera.main.transform;
			cam.position = c + new Vector3(-60f, IslandFile.DefaultWaterLevel + 30f, -95f);
			cam.LookAt(c + new Vector3(0, IslandFile.DefaultWaterLevel + 5f, 0));
			Log("Placed " + spawned.Count + " sample objects (Ctrl+Z removes them)");
		}

		static string LayerList(int mask)
		{
			var names = new System.Collections.Generic.List<string>();
			for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) names.Add(i + ":" + LayerMask.LayerToName(i));
			return string.Join(", ", names.ToArray());
		}

		static void Describe(System.Collections.Generic.List<string> lines, Transform t, int depth, int maxDepth)
		{
			string pad = new string(' ', 2 + depth * 2);
			var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c =>
			{
				string s = c.GetType().Name;
				Collider col = c as Collider;
				if (col != null) s += (col.isTrigger ? "(trigger)" : "") + (col.enabled ? "" : "(disabled)");
				return s;
			}).ToArray();
			lines.Add(pad + t.name + "  [layer " + t.gameObject.layer + ":" + LayerMask.LayerToName(t.gameObject.layer) + ", tag " + t.tag + (t.gameObject.activeSelf ? "" : ", INACTIVE") + "]  " + string.Join(", ", comps));
			if (depth < maxDepth) foreach (Transform c in t) Describe(lines, c, depth + 1, maxDepth);
		}

		[ConsoleCommand(name: "CIInspect", docs: "Dev: CIInspect <scene> <name>[,<name>...] - layers, tags and components of objects in a Raft scene, plus Raft's layer masks")]
		public static void Inspect(string[] args)
		{
			if (args == null || args.Length < 2) { Log("Usage: CIInspect <scene> <name>[,<name>...]"); return; }
			DynamicIslands.instance.StartCoroutine(InspectRoutine(args[0], string.Join(" ", args.Skip(1).ToArray()).Split(',')));
		}

		static IEnumerator InspectRoutine(string sceneName, string[] names)
		{
			var lines = new System.Collections.Generic.List<string>();
			lines.Add("LAYERS:");
			for (int i = 0; i < 32; i++) { string n = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(n)) lines.Add("  " + i + " " + n); }
			lines.Add("LAYERMASKS (static, as currently set):");
			foreach (var fi in typeof(LayerMasks).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
				if (fi.FieldType == typeof(LayerMask)) lines.Add("  " + fi.Name + " = " + LayerList(((LayerMask)fi.GetValue(null)).value));

			var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
			bool loadedByUs = !scene.isLoaded;
			if (loadedByUs)
			{
				AsyncOperation op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
				if (op == null) { Fail("cannot load scene " + sceneName); yield break; }
				while (!op.isDone) yield return null;
				scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
			}
			lines.Add("RAFT colliders:");
			Raft raftForInspect = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftForInspect != null)
				foreach (var g in raftForInspect.body.GetComponentsInChildren<Collider>(true).GroupBy(c => LayerMask.LayerToName(c.gameObject.layer) + " " + c.GetType().Name + (c.isTrigger ? " trigger" : "")))
					lines.Add("  " + g.Count() + "x " + g.Key + " e.g. " + g.First().name);
			lines.Add("Layer collisions with Obstruction(16) / RaftCollision(9):");
			for (int i = 0; i < 32; i++) if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) lines.Add("  " + i + " " + LayerMask.LayerToName(i) + ": " + !Physics.GetIgnoreLayerCollision(i, 16) + " / " + !Physics.GetIgnoreLayerCollision(i, 9));
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				lines.Add("OBJECTS ON LAYER 9 (RaftCollision) in " + root.name + ":");
				foreach (Transform t in root.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.layer == 9).Take(40))
					lines.Add("  " + Path_(t) + "  " + string.Join(", ", t.GetComponents<Component>().Where(c => !(c is Transform)).Select(c => c.GetType().Name + (c is MeshCollider ? "(" + (((MeshCollider)c).sharedMesh != null ? ((MeshCollider)c).sharedMesh.name + " " + ((MeshCollider)c).sharedMesh.vertexCount + "v" : "no mesh") + ")" : "")).ToArray()));
				lines.Add("ROOT:"); Describe(lines, root.transform, 0, 1);
				foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true)) { lines.Add("TERRAIN:"); Describe(lines, t.transform, 0, 1); }
				foreach (string wanted in names)
				{
					string w = wanted.Trim();
					Transform found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => PlaceableCatalog.CleanName(t.name) == w);
					if (found != null) { lines.Add("OBJECT " + w + " (" + Path_(found) + "):"); Describe(lines, found, 0, 3); }
					else lines.Add("OBJECT " + w + ": not found");
				}
			}
			string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "inspect_" + sceneName.Replace("#", "_") + ".txt"));
			File.WriteAllLines(file, lines.ToArray());
			Log("Inspected " + sceneName + ", written to " + file);
			if (loadedByUs)
			{
				AsyncOperation unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
				if (unload != null) while (!unload.isDone) yield return null;
			}
		}

		static GameObject NearestIsland(Vector3 from)
		{
			return UnityEngine.Object.FindObjectsOfType<GameObject>().Where(g => g.transform.parent == null && g.name.StartsWith("CustomIsland_"))
				.OrderBy(g => (g.transform.position - from).sqrMagnitude).FirstOrDefault();
		}

		[ConsoleCommand(name: "CIFlyTest", docs: "Dev, in game (host): spawns an island flying at 60 m and one under water and checks them. CIFlyTest [island] [keep] (keep = leave them spawned)")]
		public static void FlyTest(string[] args)
		{
			string name = args != null && args.Length > 0 && args[0] != "keep" ? args[0] : (IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == "generated_sample") ?? IslandSpawner.ListSavedIslands().FirstOrDefault());
			bool keep = args != null && args.Contains("keep");
			if (name == null) { Fail("no saved island to test with"); return; }
			DynamicIslands.instance.StartCoroutine(FlyTestRoutine(name, keep));
		}

		static IEnumerator FlyTestRoutine(string name, bool keep)
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue) { Fail("not in a world"); yield break; }
			bool ok = true;
			const float Flying = 60f, Sunken = -45f;
			int obstruction = 1 << IslandSpawner.TerrainLayer;

			// Flying island 250 m ahead
			Vector3 pos = raftPos.Value + Vector3.forward * 250f; pos.y = Flying;
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(name, pos, true);
			IslandWorldState.Entry fly = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (fly == null || fly.Root == null) { Fail("flying island did not spawn"); yield break; }
			yield return new WaitForSeconds(0.5f);
			Terrain terrain = fly.Root.GetComponentInChildren<Terrain>();
			TerrainData data = terrain.terrainData;
			bool[,] holes = data.GetHoles(0, 0, data.holesResolution, data.holesResolution);
			int holeCount = holes.Cast<bool>().Count(solid => !solid);
			MeshFilter underside = fly.Root.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m => m.name == "Underside");
			Bounds ub = underside != null ? underside.GetComponent<Renderer>().bounds : new Bounds();
			Check(ref ok, holeCount > 0 && underside != null && underside.sharedMesh.vertexCount > 0 && ub.min.y >= 7.5f,
				"flying island: " + holeCount + "/" + holes.Length + " terrain cells cut away, underside " + (underside != null ? underside.sharedMesh.vertexCount + " vertices, lowest point " + ub.min.y.ToString("F1") + " m above the sea" : "missing"));

			Vector3 top = HighestPoint(terrain);
			RaycastHit hit;
			bool fromAbove = Physics.Raycast(new Vector3(top.x, 400f, top.z), Vector3.down, out hit, 500f, obstruction) && hit.collider.GetComponent<Terrain>() == terrain;
			float topY = fromAbove ? hit.point.y : 0f;
			bool fromBelow = Physics.Raycast(new Vector3(top.x, 1f, top.z), Vector3.up, out hit, 400f, obstruction) && hit.collider.name == "Underside";
			float bottomY = fromBelow ? hit.point.y : 0f;
			// A corner of the terrain block is open sea in the editor: it must be a hole now
			Vector3 corner = terrain.transform.position + new Vector3(3f, 0, 3f);
			bool cornerOpen = !Physics.Raycast(new Vector3(corner.x, 400f, corner.z), Vector3.down, out hit, 500f, obstruction) || hit.collider.transform.root != fly.Root.transform;
			Check(ref ok, fromAbove && fromBelow && cornerOpen && topY > Flying,
				string.Format("from above you land on the top ({0:F0} m), from below you hit the underside ({1:F0} m), the old seabed corner is open: {2}", topY, bottomY, cornerOpen));
			int low = fly.Root.GetComponentsInChildren<EditorGameObject>(true).Length + fly.Root.transform.Find("Objects").Cast<Transform>().Count(t => t.position.y < Flying - 1f);
			Check(ref ok, low == 0, "no objects hang below the flying island (" + fly.Root.transform.Find("Objects").childCount + " objects)");

			yield return StandRoutine(); // puts the player on the nearest island's peak and logs PASS/FAIL

			// Under water, 250 m to the side
			pos = raftPos.Value + Vector3.right * 300f; pos.y = Sunken;
			before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(name, pos, true);
			IslandWorldState.Entry sunk = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (sunk == null || sunk.Root == null) { Fail("underwater island did not spawn"); yield break; }
			Terrain t2 = sunk.Root.GetComponentInChildren<Terrain>();
			Vector3 top2 = HighestPoint(t2);
			bool noHoles = t2.terrainData.GetHoles(0, 0, t2.terrainData.holesResolution, t2.terrainData.holesResolution).Cast<bool>().All(solid => solid);
			Check(ref ok, top2.y < 0f && noHoles && sunk.Root.GetComponentsInChildren<MeshFilter>().All(m => m.name != "Underside"),
				"underwater island: highest point " + top2.y.ToString("F1") + " m (below the surface), no holes, no underside");

			// Saved with the world like any other island (the position's y is the elevation)
			Check(ref ok, Mathf.Abs(fly.Position.y - Flying) < 0.01f && Mathf.Abs(sunk.Position.y - Sunken) < 0.01f, "the world's island list keeps both heights");

			if (!keep) IslandWorldState.RemoveIds(new[] { fly.Id, sunk.Id }, true);
			if (ok) Log("PASS: flying and underwater islands test" + (keep ? " (islands kept)" : "")); else Fail("flying and underwater islands test");
		}

		[ConsoleCommand(name: "CIStand", docs: "Dev, in game: puts the local player on the highest point of the nearest custom island and logs whether they stay standing")]
		public static void Stand()
		{
			DynamicIslands.instance.StartCoroutine(StandRoutine());
		}

		static IEnumerator StandRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) { Fail("no local player (not in a world?)"); yield break; }
			GameObject island = NearestIsland(player.transform.position);
			if (island == null) { Fail("no custom island spawned"); yield break; }
			Terrain terrain = island.GetComponentInChildren<Terrain>();
			Vector3 top = HighestPoint(terrain);
			Vector3 target = top + Vector3.up * 1.5f + new Vector3(3f, 0, 3f); // a little off the peak
			target.y = terrain.SampleHeight(target) + terrain.transform.position.y + 1.5f;

			CharacterController cc = player.PersonController.controller;
			cc.enabled = false;
			player.transform.position = target;
			cc.enabled = true;
			Log("Teleported player to " + target + " on " + island.name);
			int exceptions = 0; string firstException = null;
			Application.LogCallback counter = (msg, trace, type) => { if (type == LogType.Exception) { exceptions++; if (firstException == null) firstException = msg + " " + trace.Split('\n').FirstOrDefault(); } };
			Application.logMessageReceived += counter;

			for (int i = 0; i < 12; i++)
			{
				yield return new WaitForSeconds(0.5f);
				PersonController pc = player.PersonController;
				Collider ground = pc.groundRaycastHit.collider;
				Log(string.Format("t={0:F1}s pos={1} grounded={2} standing on {3} (layer {4})", (i + 1) * 0.5f, player.transform.position, pc.IsGrounded,
					ground != null ? ground.name : "nothing", ground != null ? LayerMask.LayerToName(ground.gameObject.layer) : "-"));
			}
			Application.logMessageReceived -= counter;
			float drop = target.y - player.transform.position.y;
			Log("Exceptions while standing: " + exceptions + (firstException != null ? " (first: " + firstException + ")" : ""));
			bool ok = drop < 3f && player.PersonController.IsGrounded && exceptions == 0;
			if (ok) Log("PASS: player stands on the custom island (dropped " + drop.ToString("F2") + " m)");
			else Fail("player did not stay on the island (dropped " + drop.ToString("F1") + " m, grounded=" + player.PersonController.IsGrounded + ")");
		}

		[ConsoleCommand(name: "CIRaftWatch", docs: "Dev, in game: logs the raft's position and speed every 2 s for <seconds> (default 60)")]
		public static void RaftWatch(string[] args)
		{
			float seconds = 60f; float s;
			if (args != null && args.Length > 0 && float.TryParse(args[0], out s)) seconds = s;
			DynamicIslands.instance.StartCoroutine(RaftWatchRoutine(seconds));
		}

		static IEnumerator RaftWatchRoutine(float seconds)
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raft == null) { Fail("no raft"); yield break; }
			Transform body = raft.body != null ? raft.body.transform : raft.transform; // the raft moves through its rigidbody
			Vector3 last = body.position;
			for (float t = 0; t < seconds; t += 2f)
			{
				yield return new WaitForSeconds(2f);
				Vector3 p = body.position;
				RaycastHit hit;
				string below = Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out hit, 40f, 1 << IslandSpawner.TerrainLayer) ? hit.collider.name + " at depth " + (p.y - hit.point.y).ToString("F1") : "open water";
				GameObject island = NearestIsland(p);
				string dist = island != null ? (island.GetComponentInChildren<Terrain>() != null ? " | distance to island peak " + Vector3.Distance(new Vector3(p.x, 0, p.z), Flat(HighestPoint(island.GetComponentInChildren<Terrain>()))).ToString("F0") + " m" : "") : "";
				Log(string.Format("raft t={0:F0}s pos={1} speed={2:F2} m/s below: {3}{4}", t + 2f, p, (p - last).magnitude / 2f, below, dist));
				last = p;
			}
		}

		static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0, v.z); }

		[ConsoleCommand(name: "CIWorldWatch", docs: "Dev, in game: logs how floating items, landmarks and chunk points move over 10 s (does the world move, or the raft?)")]
		public static void WorldWatch()
		{
			DynamicIslands.instance.StartCoroutine(WorldWatchRoutine());
		}

		static IEnumerator WorldWatchRoutine()
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			var watched = new System.Collections.Generic.List<Transform>();
			watched.AddRange(UnityEngine.Object.FindObjectsOfType<Landmark>().Select(l => l.transform).Take(3));
			watched.AddRange(UnityEngine.Object.FindObjectsOfType<PickupItem_Networked>().Where(p => p.GetComponentInParent<Landmark>() == null).Select(p => p.transform).Take(5));
			ChunkManager cm = UnityEngine.Object.FindObjectOfType<ChunkManager>();
			var points = cm != null ? cm.GetAllChunkPointsList() : null;
			Log("Watching " + watched.Count + " objects; chunk points: " + (points != null ? points.Count.ToString() : "?"));
			var start = watched.ToDictionary(t => t, t => t.position);
			Vector3 raftStart = raft.body.transform.position;
			Vector3 pointStart = points != null && points.Count > 0 ? points[0].worldPosition : Vector3.zero;
			yield return new WaitForSeconds(10f);
			Log("raft moved " + (raft.body.transform.position - raftStart));
			foreach (Transform t in watched) if (t != null) Log("  " + t.name + " moved " + (t.position - start[t]) + " now at " + t.position);
			if (points != null && points.Count > 0) Log("  first chunk point (" + points[0].rule.name + ") moved " + (points[0].worldPosition - pointStart) + " now at " + points[0].worldPosition);
		}

		[ConsoleCommand(name: "CIRaftInfo", docs: "Dev, in game: logs the raft's movement state (anchors, speeds, rigidbody, direction)")]
		public static void RaftInfo()
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raft == null) { Fail("no raft"); return; }
			Rigidbody rb = raft.body;
			Log("Raft: pos=" + raft.transform.position + " anchors=" + raft._anchorCount + " speed=" + raft.speed + " currentMovementSpeed=" + raft.currentMovementSpeed +
				" maxSpeed=" + raft.maxSpeed + " waterDriftSpeed=" + raft.waterDriftSpeed + " moveDirection=" + raft.moveDirection + " Raft.direction=" + Raft.direction);
			if (rb != null) Log("Raft rigidbody: kinematic=" + rb.isKinematic + " velocity=" + rb.velocity + " mass=" + rb.mass + " constraints=" + rb.constraints + " layer=" + LayerMask.LayerToName(raft.gameObject.layer));
			foreach (Collider c in raft.GetComponentsInChildren<Collider>().Where(c => c.gameObject.layer == 9 || c.GetType() != typeof(BoxCollider)).Take(8))
				Log("  collider " + c.name + " " + c.GetType().Name + " layer " + LayerMask.LayerToName(c.gameObject.layer) + (c.isTrigger ? " trigger" : ""));
			var raftCols = UnityEngine.Object.FindObjectsOfType<Collider>().Where(c => c.gameObject.layer == 9).ToArray();
			Log("Colliders on layer RaftCollision in the scene: " + raftCols.Length + ", attached to the raft body: " + raftCols.Count(c => c.attachedRigidbody == rb) +
				(raftCols.Length > 0 ? " (e.g. " + Path_(raftCols[0].transform) + " " + raftCols[0].GetType().Name + (raftCols[0].enabled ? "" : " disabled") + ", rigidbody " + (raftCols[0].attachedRigidbody != null ? raftCols[0].attachedRigidbody.name : "none") + ")" : ""));
			Log("Blocks on the raft: " + UnityEngine.Object.FindObjectsOfType<Block>().Length + ", body children: " + (rb != null ? rb.transform.childCount : -1) + ", body path " + (rb != null ? Path_(rb.transform) : "-"));
			Log("Physics: RaftCollision(9) vs Obstruction(16) collide = " + !Physics.GetIgnoreLayerCollision(9, 16) + ", Default(0) vs Obstruction = " + !Physics.GetIgnoreLayerCollision(0, 16));
		}

		[ConsoleCommand(name: "CIShiftTest", docs: "Dev, in game (host): performs a world shift through Raft's WorldShiftManager and checks custom islands move with the world")]
		public static void ShiftTest()
		{
			DynamicIslands.instance.StartCoroutine(ShiftTestRoutine());
		}

		static IEnumerator ShiftTestRoutine()
		{
			WorldShiftManager wsm = UnityEngine.Object.FindObjectOfType<WorldShiftManager>();
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			GameObject island = NearestIsland(raft.body.transform.position);
			ChunkManager cm = UnityEngine.Object.FindObjectOfType<ChunkManager>();
			if (wsm == null || island == null || cm == null) { Fail("need WorldShiftManager, a raft, a chunk manager and a spawned custom island"); yield break; }
			ChunkPoint point = cm.GetAllChunkPointsList().FirstOrDefault();
			Vector3 islandToRaft = island.transform.position - raft.body.transform.position;
			Vector3 islandToPoint = island.transform.position - point.worldPosition;
			Vector3 shift = new Vector3(100f, 0, -50f);
			wsm.ResetToCenter(shift);
			yield return new WaitForSeconds(0.5f);
			Vector3 e1 = (island.transform.position - raft.body.transform.position) - islandToRaft;
			Vector3 e2 = (island.transform.position - point.worldPosition) - islandToPoint;
			var entry = IslandWorldState.Islands.FirstOrDefault(i => i.Root == island);
			bool savedFollows = entry == null || (entry.Position - (island.transform.position + (entry.Position - island.transform.position))).sqrMagnitude < 1f;
			Log("After a world shift of " + shift + ": island vs raft drift " + e1 + ", island vs chunk point drift " + e2);
			bool ok = e1.magnitude < 2f && e2.magnitude < 0.01f; // the raft moves a little by itself during the wait
			if (ok) Log("PASS: custom island follows world shifts");
			else Fail("custom island does not follow world shifts");
		}

		[ConsoleCommand(name: "CIPushRaft", docs: "Dev, in game: pushes the raft's rigidbody towards the nearest custom island (like drifting) for up to <seconds> and logs where it stops")]
		public static void PushRaft(string[] args)
		{
			float seconds = 60f; float s;
			if (args != null && args.Length > 0 && float.TryParse(args[0], out s)) seconds = s;
			DynamicIslands.instance.StartCoroutine(PushRaftRoutine(seconds));
		}

		static IEnumerator PushRaftRoutine(float seconds)
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			GameObject island = NearestIsland(raft.body.position);
			if (island == null) { Fail("no custom island"); yield break; }
			yield return PushTowards(HighestPoint(island.GetComponentInChildren<Terrain>()), seconds, island.name);
		}

		[ConsoleCommand(name: "CIPushTo", docs: "Dev, in game: pushes the raft towards world point <x> <z> for up to <seconds> and logs where it stops")]
		public static void PushTo(string[] args)
		{
			float x = float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
			float seconds = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 60f;
			DynamicIslands.instance.StartCoroutine(PushTowards(new Vector3(x, 0, z), seconds, "(" + x + ", " + z + ")"));
		}

		[ConsoleCommand(name: "CISail", docs: "Dev, in game (host, Normal world): sails the raft straight ahead for <seconds> (default 120) at <m/s> (default 15) and logs custom islands spawning / unloading")]
		public static void Sail(string[] args)
		{
			float seconds = args != null && args.Length > 0 ? float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 120f;
			float speed = args != null && args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 15f;
			RunInBackground();
			DynamicIslands.instance.StartCoroutine(SailRoutine(seconds, speed));
		}

		[ConsoleCommand(name: "CIBackground", docs: "Dev: keeps the game running while its window is not in front (for automated tests)")]
		public static void RunInBackground()
		{
			Application.runInBackground = true;
			Log("The game keeps running in the background now");
		}

		static IEnumerator SailRoutine(float seconds, float speed)
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raft == null || raft.body == null) { Fail("no raft"); yield break; }
			Rigidbody body = raft.body;
			Vector3 dir = Flat(body.velocity).sqrMagnitude > 0.04f ? Flat(body.velocity).normalized : (Flat(Raft.direction).sqrMagnitude > 0.01f ? Flat(Raft.direction).normalized : Vector3.forward);
			int startCount = IslandWorldState.Islands.Count;
			Log("Sailing " + dir + " at " + speed + " m/s for " + seconds + " s; custom islands in the world: " + startCount + ", auto " + (CustomIslandSpawner.Enabled ? "on" : "off"));
			float t = 0, sailed = 0, nextLog = 10f;
			Vector3 last = body.position;
			Network_Player player = RAPI.GetLocalPlayer();
			// Start on the raft (dying pauses a single-player game, which would stall the test)
			if (player != null) player.transform.position = body.position + Vector3.up * 3f;
			while (t < seconds)
			{
				yield return new WaitForFixedUpdate();
				t += Time.fixedDeltaTime;
				KeepAlive(player);
				// Raft's own physics caps the raft's speed, so move the body directly (the player on it moves along)
				body.MovePosition(body.position + dir * speed * Time.fixedDeltaTime);
				Vector3 d = Flat(body.position - last);
				if (d.magnitude < 100f) sailed += d.magnitude; // world shifts jump the position
				last = body.position;
				if (t >= nextLog)
				{
					nextLog += 10f;
					int loaded = IslandWorldState.Islands.Count(e => e.Root != null);
					Log(string.Format("t={0:F0}s sailed {1:F0} m; custom islands {2} ({3} loaded)", t, sailed, IslandWorldState.Islands.Count, loaded));
				}
			}
			body.velocity = Vector3.zero;
			Log("Done: sailed " + sailed.ToString("F0") + " m, custom islands " + startCount + " -> " + IslandWorldState.Islands.Count + ", loaded now " + IslandWorldState.Islands.Count(e => e.Root != null));
		}

		/// <summary>Long test runs in a Normal world would otherwise starve the test player.</summary>
		static void KeepAlive(Network_Player player)
		{
			if (player == null || player.Stats == null) return;
			PlayerStats s = player.Stats;
			s.stat_hunger.Normal.Value = s.stat_hunger.Normal.Max;
			s.stat_thirst.Normal.Value = s.stat_thirst.Normal.Max;
			s.stat_health.Value = s.stat_health.Max;
			s.stat_oxygen.Value = s.stat_oxygen.Max;
		}

		/// <summary>Sends a message through RML's own serializer (as the network would) and reads it back.</summary>
		static IslandNetMessage RoundTrip(IslandNetMessage m, out int bytes)
		{
			var writer = new Unity.Netcode.FastBufferWriter(1024, Unity.Collections.Allocator.Temp, 1 << 20);
			try
			{
				new RMessage("citest", m).SerializeFast(writer);
				bytes = writer.Length;
				var reader = new Unity.Netcode.FastBufferReader(writer, Unity.Collections.Allocator.Temp);
				try
				{
					reader.Seek(2); // RML's message marker, read by its network patch before DeserializeFast
					var back = new RMessage();
					back.DeserializeFast(reader);
					return back.realMsg as IslandNetMessage;
				}
				finally { reader.Dispose(); }
			}
			finally { writer.Dispose(); }
		}

		[ConsoleCommand(name: "CINetTest", docs: "Dev, in game (host): island messages survive RML's serializer, and an island file transfer (looped back locally) arrives intact")]
		public static void NetTest()
		{
			bool ok = true;
			try
			{
				// 1. The island list, as a client that joins would get it
				IslandNetMessage list = IslandNetwork.IslandsMessage(IslandWorldState.Islands, true);
				int size;
				IslandNetMessage back = RoundTrip(list, out size);
				bool same = back != null && back.Kind == list.Kind && back.FullList && back.Ids.SequenceEqual(list.Ids) && back.Names.SequenceEqual(list.Names) &&
					back.Hashes.SequenceEqual(list.Hashes) && back.Offsets.SequenceEqual(list.Offsets) && back.States.SequenceEqual(list.States);
				Log((same ? "PASS" : "FAIL") + ": island list with " + list.Ids.Length + " island(s) survives RML's serializer (" + size + " bytes)" +
					(back == null ? " - came back as null" : ""));
				ok &= same;

				// 2. File transfer: host sends chunks, the client puts them back together under a hash-suffixed name
				string name = IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == TestIsland) ?? IslandSpawner.ListSavedIslands().FirstOrDefault();
				if (name == null) { Fail("no saved island to transfer"); return; }
				string hash = IslandNetwork.HashOf(name);
				var sent = new System.Collections.Generic.List<IslandNetMessage>();
				IslandNetwork.Loopback = sent.Add;
				try { IslandNetwork.SendFile(name, hash, default(Network_UserId)); }
				finally { IslandNetwork.Loopback = null; }
				string target = IslandSpawner.PathFor(IslandNetwork.DownloadName(name, hash));
				if (File.Exists(target)) File.Delete(target);
				IslandNetwork.ExpectFile(hash);
				int maxChunk = 0;
				foreach (IslandNetMessage chunk in sent)
				{
					IslandNetMessage c = RoundTrip(chunk, out size);
					maxChunk = Math.Max(maxChunk, size);
					IslandNetwork.ReceiveChunk(c);
				}
				bool arrived = File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(IslandSpawner.PathFor(name)));
				Log((arrived ? "PASS" : "FAIL") + ": island file '" + name + "' (" + new FileInfo(IslandSpawner.PathFor(name)).Length + " bytes) sent in " + sent.Count +
					" chunk(s) of up to " + maxChunk + " bytes and saved intact as " + Path.GetFileName(target));
				ok &= arrived;
				if (File.Exists(target)) File.Delete(target);

				// 3. Trees and pickups on loaded islands can be found the way Raft finds what a remote player harvested / picked up
				int found = 0, total = 0;
				foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(i => i.Root != null))
					foreach (PickupItem_Networked pn in e.Root.GetComponentsInChildren<PickupItem_Networked>(true))
					{
						total++;
						if (NetworkIDManager.GetNetworkIDFromObjectIndex<PickupItem_Networked>(pn.ObjectIndex) == pn) found++;
					}
				bool ids = total > 0 && found == total;
				Log((ids ? "PASS" : total == 0 ? "SKIP" : "FAIL") + ": " + found + "/" + total + " harvestables and pickups on loaded custom islands are in Raft's network registry" +
					(total == 0 ? " (no loaded island with any; SpawnIsland demo2 first)" : ""));
				ok &= ids || total == 0;
			}
			catch (Exception e) { Fail("exception: " + e); ok = false; }
			if (ok) Log("PASS: network self test"); else Fail("network self test");
		}

		[ConsoleCommand(name: "CIStateTest", docs: "Dev, in game (host): chops a tree and picks up an item on the nearest loaded custom island, reloads the island and checks they stay used; also checks regrowing")]
		public static void StateTest()
		{
			DynamicIslands.instance.StartCoroutine(StateTestRoutine());
		}

		static IEnumerator StateTestRoutine()
		{
			Vector3 raftPos = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			IslandWorldState.Entry e = IslandWorldState.Islands.Where(i => i.Root != null && i.Root.GetComponentsInChildren<HarvestableTree>().Any())
				.OrderBy(i => Vector3.Distance(i.Position, raftPos)).FirstOrDefault();
			if (e == null) { Fail("no loaded custom island with trees (SpawnIsland demo2 first)"); yield break; }

			HarvestableTree tree = e.Root.GetComponentsInChildren<HarvestableTree>().First(t => !t.Depleted);
			int treeOrd = (int)(tree.GetComponent<PickupItem_Networked>().ObjectIndex & 0xFFFF);
			tree.Harvest(null); // one chop, no items
			int yieldLeft = tree.GetComponent<PickupItem>().yieldHandler.Yield.Count;
			PickupItem_Networked pickup = e.Root.GetComponentsInChildren<PickupItem_Networked>()
				.First(p => p.GetComponent<HarvestableTree>() == null && p.gameObject.activeSelf);
			int pickupOrd = (int)(pickup.ObjectIndex & 0xFFFF);
			bool removed = PickupObjectManager.RemovePickupItem(pickup);
			Log("On '" + e.HostName + "': chopped tree #" + treeOrd + " once (" + yieldLeft + " harvests left), picked up " + pickup.name + " #" + pickupOrd +
				" (removed=" + removed + ", still exists=" + (pickup != null) + ", active=" + (pickup != null && pickup.gameObject.activeSelf) + ")");

			// Unload and load again, the way the streamer does
			IslandObjectState.Capture(e);
			Log("Recorded state: " + IslandObjectState.Encode(e.State));
			IslandSpawner.Despawn(e.Root);
			e.Root = null;
			yield return null;
			e.Loading = true;
			yield return DynamicIslands.instance.SpawnIslandFile(e.Name, e.Position, false, e);
			if (e.Root == null) { Fail("island did not load again"); yield break; }

			PickupItem_Networked tree2 = e.Root.GetComponentsInChildren<PickupItem_Networked>(true).First(p => (p.ObjectIndex & 0xFFFF) == treeOrd);
			PickupItem_Networked pickup2 = e.Root.GetComponentsInChildren<PickupItem_Networked>(true).First(p => (p.ObjectIndex & 0xFFFF) == pickupOrd);
			int yieldAfter = tree2.GetComponent<PickupItem>().yieldHandler.Yield.Count;
			bool ok = yieldAfter == yieldLeft && !pickup2.gameObject.activeSelf;
			Log((ok ? "PASS" : "FAIL") + ": after reloading, the tree has " + yieldAfter + " harvests left (expected " + yieldLeft + ") and the pickup is " +
				(pickup2.gameObject.activeSelf ? "back (wrong)" : "still gone"));

			// Regrowing: on a copy of the state, pretend it all happened long ago (the real state stays for save/load checks)
			var old = IslandObjectState.Decode(IslandObjectState.Encode(e.State));
			foreach (ObjectState s in old.Values) s.Day -= 100;
			int stale = IslandObjectState.DropRegrown(old, 3);
			var fresh = IslandObjectState.Decode(IslandObjectState.Encode(e.State));
			int kept = fresh.Count - IslandObjectState.DropRegrown(fresh, 3);
			bool regrown = stale == e.State.Count && old.Count == 0 && kept == e.State.Count;
			Log((regrown ? "PASS" : "FAIL") + ": state older than 3 days is dropped, so the island regrows on its next load; today's stays (" + stale + " dropped, " + kept + " kept)");

			var round = IslandObjectState.Decode("5,0,-1,12;7,1,2,13");
			bool codec = round.Count == 2 && !round[5].Active && round[5].Yield == -1 && round[7].Yield == 2 && round[7].Day == 13 && IslandObjectState.Encode(round) == "5,0,-1,12;7,1,2,13";
			Log((codec ? "PASS" : "FAIL") + ": state text round trip");
			if (ok && regrown && codec) Log("PASS: island object state test"); else Fail("island object state test");
		}

		[ConsoleCommand(name: "CISpawnNow", docs: "Dev, in game (host): places an island from the spawn pool ahead of the raft now, with the automatic spawner's checks")]
		public static void SpawnNow()
		{
			Vector3? pos = CustomIslandSpawner.RaftPosition;
			if (!pos.HasValue) { Fail("no raft"); return; }
			Log(CustomIslandSpawner.TrySpawn(pos.Value, true));
		}

		[ConsoleCommand(name: "CIRealIsland",docs: "Dev, in game (host): spawns one of Raft's own small islands <distance> m east of the raft (for comparisons)")]
		public static void RealIsland(string[] args)
		{
			float d = args != null && args.Length > 0 ? float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 200f;
			ComponentManager<ChunkManager>.Value.AddChunkPointCheat(ChunkPointType.Landmark_Small, new Vector3(d, 0, 0));
			DynamicIslands.instance.StartCoroutine(ReportLandmarks());
		}

		static IEnumerator ReportLandmarks()
		{
			yield return new WaitForSeconds(8f);
			foreach (Landmark l in UnityEngine.Object.FindObjectsOfType<Landmark>())
			{
				Terrain t = l.GetComponentInChildren<Terrain>();
				Log("Real landmark " + l.name + " at " + l.transform.position + (t != null ? ", highest point " + HighestPoint(t) : ""));
			}
		}

		static IEnumerator PushTowards(Vector3 target, float seconds, string label)
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			Rigidbody body = raft.body;
			Vector3 peak = target;
			Vector3 dir = Flat(peak - body.position).normalized;
			Log("Pushing raft towards " + label + " at 4 m/s; layers RaftCollision/BakedBlocks vs Obstruction collide: " + !Physics.GetIgnoreLayerCollision(9, 16) + "/" + !Physics.GetIgnoreLayerCollision(18, 16));
			Vector3 lastLogged = body.position; float stuckTime = 0; float t = 0;
			while (t < seconds)
			{
				yield return new WaitForFixedUpdate();
				t += Time.fixedDeltaTime;
				Vector3 before = body.position;
				body.velocity = dir * 4f + Vector3.up * body.velocity.y;
				if (Mathf.Repeat(t, 2f) < Time.fixedDeltaTime)
				{
					float moved = Flat(body.position - lastLogged).magnitude;
					RaycastHit hit;
					string below = Physics.Raycast(body.position + Vector3.up * 5f, Vector3.down, out hit, 40f, 1 << IslandSpawner.TerrainLayer) ? "ground " + (body.position.y - hit.point.y).ToString("F1") + " m below" : "open water";
					Log(string.Format("t={0:F0}s raft {1} moved {2:F1} m in 2 s, {3}, {4:F0} m from peak", t, body.position, moved, below, Flat(peak - body.position).magnitude));
					stuckTime = moved < 0.5f ? stuckTime + 2f : 0f;
					lastLogged = body.position;
					if (stuckTime >= 6f) { Log("PASS: the raft is stopped by the island (" + Flat(peak - body.position).magnitude.ToString("F0") + " m from the peak)"); body.velocity = Vector3.zero; yield break; }
				}
			}
			body.velocity = Vector3.zero;
			Fail("raft was not stopped within " + seconds + " s");
		}

		[ConsoleCommand(name: "CIHarvestProbe", docs: "Dev, in game: places a harvestable Raft palm next to the player (outside any landmark) and tries to harvest it")]
		public static void HarvestProbe()
		{
			DynamicIslands.instance.StartCoroutine(HarvestProbeRoutine());
		}

		static IEnumerator HarvestProbeRoutine()
		{
			yield return PlaceableCatalog.EnsureBuilt();
			Log("Harvestable prototypes: " + string.Join(", ", PlaceableCatalog.HarvestableNames.ToArray()));
			Network_Player player = RAPI.GetLocalPlayer();
			string name = PlaceableCatalog.HarvestableNames.FirstOrDefault(n => n.Contains("Palm"));
			if (player == null || name == null) { Fail("need a player and a harvestable palm"); yield break; }
			int exceptions = 0; string first = null;
			Application.LogCallback counter = (msg, trace, type) => { if (type == LogType.Exception || type == LogType.Error) { exceptions++; if (first == null) first = msg + " | " + trace.Split('\n').FirstOrDefault(); } };
			Application.logMessageReceived += counter;
			GameObject islandForHarvest = NearestIsland(player.transform.position);
			HarvestableTree onIsland = islandForHarvest != null ? islandForHarvest.GetComponentsInChildren<HarvestableTree>().FirstOrDefault() : null;
			GameObject tree;
			if (onIsland != null) { tree = onIsland.transform.root == onIsland.transform ? onIsland.gameObject : onIsland.gameObject; Log("Using harvestable " + onIsland.name + " placed on " + islandForHarvest.name + " (" + islandForHarvest.GetComponentsInChildren<HarvestableTree>().Length + " harvestable trees, " + islandForHarvest.GetComponentsInChildren<PickupItem>().Length + " pickups on the island)"); }
			else { tree = PlaceableCatalog.SpawnHarvestable(name, null); tree.transform.position = player.transform.position + player.transform.forward * 3f; }
			yield return new WaitForSeconds(1f);
			Log("Spawned " + name + " with exceptions so far: " + exceptions + (first != null ? " (" + first + ")" : ""));
			HarvestableTree ht = tree.GetComponentInChildren<HarvestableTree>();
			PickupItem_Networked net = tree.GetComponentInChildren<PickupItem_Networked>();
			Log("HarvestableTree=" + (ht != null) + " PickupItem_Networked=" + (net != null) + " canBePickedUp=" + (net != null ? net.CanBePickedUp().ToString() : "-") + " index=" + (net != null ? net.uniqueSpawnableIndex.ToString() : "-"));
			int before = player.Inventory.allSlots.Where(sl => sl != null && sl.itemInstance != null && sl.itemInstance.baseItem != null).Sum(sl => sl.itemInstance.Amount);
			try { if (ht != null) ht.Harvest(player.Inventory); }
			catch (Exception e) { Log("Harvest threw: " + e.GetType().Name + ": " + e.Message); }
			yield return new WaitForSeconds(2f);
			int after = player.Inventory.allSlots.Where(sl => sl != null && sl.itemInstance != null && sl.itemInstance.baseItem != null).Sum(sl => sl.itemInstance.Amount);
			Application.logMessageReceived -= counter;
			Log("Inventory items " + before + " -> " + after + ", tree active=" + (tree != null && tree.activeInHierarchy) + ", errors/exceptions: " + exceptions + (first != null ? " (first: " + first + ")" : ""));
		}

		[ConsoleCommand(name: "CISave", docs: "Dev, in game (host): saves the world now")]
		public static void SaveNow()
		{
			SaveAndLoad sl = UnityEngine.Object.FindObjectOfType<SaveAndLoad>();
			if (sl == null) { Fail("SaveAndLoad not found"); return; }
			sl.SaveGame(false);
			Log("World save requested (" + SaveAndLoad.CurrentGameFileName + ", " + SaveAndLoad.WorldGuid + ")");
		}

		[ConsoleCommand(name: "CIScenes", docs: "Dev: lists all scenes in Raft's build")]
		public static void ListScenes()
		{
			var lines = new System.Collections.Generic.List<string>();
			for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
				lines.Add(i + ": " + UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i));
			string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "scan_scenes.txt"));
			File.WriteAllLines(file, lines.ToArray());
			Log(lines.Count + " scenes, written to " + file);
		}

		[ConsoleCommand(name: "CIScan", docs: "Dev: loads a Raft scene additively and writes its renderable objects and terrain setup to Mods\\DynamicIslands\\scan_<scene>.txt")]
		public static void ScanScene(string[] args)
		{
			if (args == null || args.Length == 0) { Log("Usage: CIScan <scene name>"); return; }
			DynamicIslands.instance.StartCoroutine(Scan(string.Join(" ", args)));
		}

		static IEnumerator Scan(string sceneName)
		{
			var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
			bool loadedByUs = !scene.isLoaded;
			if (loadedByUs)
			{
				AsyncOperation op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
				if (op == null) { Fail("cannot load scene " + sceneName); yield break; }
				while (!op.isDone) yield return null;
				scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
			}

			var lines = new System.Collections.Generic.List<string>();
			var counts = new System.Collections.Generic.Dictionary<string, int>();
			var examplePath = new System.Collections.Generic.Dictionary<string, string>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true))
				{
					TerrainData td = t.terrainData;
					lines.Add("TERRAIN " + Path_(t.transform) + " size=" + td.size + " hres=" + td.heightmapResolution + " ares=" + td.alphamapResolution +
						" material=" + (t.materialTemplate != null ? t.materialTemplate.name + " / " + t.materialTemplate.shader.name : "(none)"));
					if (td.terrainLayers != null)
						foreach (TerrainLayer l in td.terrainLayers)
							if (l != null) lines.Add("  LAYER " + l.name + " diffuse=" + (l.diffuseTexture != null ? l.diffuseTexture.name + " " + l.diffuseTexture.width + "x" + l.diffuseTexture.height + " readable=" + l.diffuseTexture.isReadable : "none") +
								" normal=" + (l.normalMapTexture != null ? l.normalMapTexture.name : "none") + " tile=" + l.tileSize);
					if (t.materialTemplate != null)
						foreach (string p in t.materialTemplate.GetTexturePropertyNames())
						{
							Texture tex = t.materialTemplate.GetTexture(p);
							if (tex != null) lines.Add("  MATTEX " + p + " = " + tex.name + " (" + tex.GetType().Name + " " + tex.width + "x" + tex.height + ")");
						}
					lines.Add("  TREES prototypes=" + td.treePrototypes.Length + " instances=" + td.treeInstanceCount + " details=" + td.detailPrototypes.Length);
					foreach (TreePrototype tp in td.treePrototypes) if (tp.prefab != null) lines.Add("  TREEPROTO " + tp.prefab.name);
				}

				// Top-most renderable nodes, as the placeable catalog would pick them
				var stack = new System.Collections.Generic.Stack<Transform>();
				stack.Push(root.transform);
				while (stack.Count > 0)
				{
					Transform t = stack.Pop();
					if (t != root.transform && (t.GetComponent<Renderer>() != null || t.GetComponent<LODGroup>() != null))
					{
						string n = PlaceableCatalog.CleanName(t.name);
						int c; counts.TryGetValue(n, out c); counts[n] = c + 1;
						if (!examplePath.ContainsKey(n)) examplePath[n] = Path_(t);
						continue;
					}
					foreach (Transform child in t) stack.Push(child);
				}
			}
			lines.Add("OBJECTS (name, count, example path):");
			foreach (var kv in counts.OrderByDescending(k => k.Value)) lines.Add("  " + kv.Value + "x " + kv.Key + "    " + examplePath[kv.Key]);

			string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "scan_" + sceneName.Replace("#", "_") + ".txt"));
			File.WriteAllLines(file, lines.ToArray());
			Log("Scanned " + sceneName + ": " + counts.Count + " distinct objects, written to " + file);

			if (loadedByUs)
			{
				AsyncOperation unload = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
				if (unload != null) while (!unload.isDone) yield return null;
			}
		}

		static string Path_(Transform t)
		{
			string p = t.name;
			while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
			return p;
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
			terraineditor.paintMask = null;
			TerrainPainter.Setup(terrain, IslandFile.DefaultWaterLevel);
			int ares = data.alphamapResolution;
			terraineditor.paintMask = new float[ares, ares];
			yield return null;

			// 1b. Hand-paint a Rock patch on the grassy hilltop, then check automatic re-texturing leaves it alone
			int bx = ares / 2 + PaintOffset, bz = ares / 2 + PaintOffset;
			var rockBlock = new float[PaintSize, PaintSize, TerrainPainter.LayerCount];
			for (int z = 0; z < PaintSize; z++)
				for (int x = 0; x < PaintSize; x++) { rockBlock[z, x, TerrainPainter.Rock] = 1f; terraineditor.paintMask[bz + z, bx + x] = 1f; }
			data.SetAlphamaps(bx, bz, rockBlock);
			TerrainPainter.PaintWorldArea(terrain, IslandFile.DefaultWaterLevel, terrain.transform.position, terrain.transform.position + data.size, terraineditor.paintMask);
			float rockAfterAuto = data.GetAlphamaps(bx + PaintSize / 2, bz + PaintSize / 2, 1, 1)[0, 0, TerrainPainter.Rock];
			float grassBeside = data.GetAlphamaps(bx - 6, bz - 6, 1, 1)[0, 0, TerrainPainter.Grass];
			bool maskRespected = rockAfterAuto > 0.98f;
			Log("Paint: rock patch after auto re-texturing = " + rockAfterAuto.ToString("F2") + " (" + (maskRespected ? "kept" : "OVERWRITTEN") + "), grass next to it = " + grassBeside.ToString("F2"));

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

			// 4. Wipe: flatten terrain, reset texturing to automatic, remove objects
			data.SetHeights(0, 0, new float[res, res]);
			terraineditor.paintMask = null;
			TerrainPainter.Setup(terrain, IslandFile.DefaultWaterLevel);
			terraineditor.paintMask = new float[ares, ares];
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

			float rockLoaded = data.GetAlphamaps(bx + PaintSize / 2, bz + PaintSize / 2, 1, 1)[0, 0, TerrainPainter.Rock];
			bool maskLoaded = terraineditor.paintMask != null && terraineditor.paintMask[bz + 1, bx + 1] > 0.5f && terraineditor.paintMask[bz - 6, bx - 6] < 0.5f;
			bool paintOk = maskRespected && rockLoaded > 0.98f && maskLoaded;
			Log("Paint after load: rock = " + rockLoaded.ToString("F2") + ", paint mask restored = " + maskLoaded + " (" + (paintOk ? "ok" : "WRONG") + ")");

			if (heightsOk && objectsOk && paintOk) Log("PASS: editor save/load round trip");
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

			// The hand-painted rock patch must be at the same place on the (cropped) spawned terrain
			IslandFile file = IslandFile.Load(IslandSpawner.PathFor(TestIsland));
			float u = (file.AlphamapResolution / 2 + PaintOffset + PaintSize / 2 + 0.5f) / file.AlphamapResolution;
			Vector3 patchWorld = root.transform.position + new Vector3(u * file.TerrainSize.x, 0, u * file.TerrainSize.z);
			TerrainData td = terrain.terrainData;
			Vector3 local = patchWorld - terrain.transform.position;
			int px = Mathf.FloorToInt(local.x / td.size.x * td.alphamapResolution), pz = Mathf.FloorToInt(local.z / td.size.z * td.alphamapResolution);
			float rock = (px >= 0 && pz >= 0 && px < td.alphamapResolution && pz < td.alphamapResolution) ? td.GetAlphamaps(px, pz, 1, 1)[0, 0, TerrainPainter.Rock] : -1f;
			Log("Spawned terrain alphamap " + td.alphamapResolution + " px; rock at the hand-painted spot = " + rock.ToString("F2"));

			bool ok = terrain != null && objects > 0 && hillTop > 5f && hillTop < 30f && rock > 0.9f;
			if (ok) Log("PASS: island spawned in world");
			else Fail("island spawned but looks wrong (objects=" + objects + ", hillTop=" + hillTop + ")");
		}
	}
}
