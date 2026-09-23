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
					back.Hashes.SequenceEqual(list.Hashes) && back.Offsets.SequenceEqual(list.Offsets);
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
