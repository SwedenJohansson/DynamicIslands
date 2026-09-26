using HarmonyLib;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using AsyncOperation = UnityEngine.AsyncOperation;
using UnityEngine.UI;
using Redcode.Awaiting;
using HMLLibrary;
using RaftModLoader;
using DynamicIslands.Editor;
using System.Reflection;
using RuntimeGizmos;

namespace DynamicIslands
{
	public class DynamicIslands : Mod
	{
		public static readonly string assetpath = @"Mods\DynamicIslands\";

		/// <summary>Name used by the editor's Save/Load menu entries; set by LoadIsland/SaveIsland commands.</summary>
		public static string currentIslandName = "myisland";
		/// <summary>Metres above sea level the island being edited floats at in game (negative = under water); saved with it.</summary>
		public static float currentElevation;
		/// <summary>Style of the island being edited (TerrainPainter.Styles); saved with it.</summary>
		public static int currentStyle = TerrainPainter.Tropical;
		/// <summary>Island-wide settings of the island being edited (IslandProps: name shown to players, author, description); saved with it.</summary>
		public static Dictionary<string, string> currentIslandProps = new Dictionary<string, string>();

		/// <summary>
		/// Editor Y of the sea for the island being edited: IslandFile.DefaultWaterLevel (20 m above the terrain's base)
		/// for islands on a shallow seabed, IslandFile.DeepWaterLevel for islands generated on Raft's deep sea floor.
		/// Saved with it.
		/// </summary>
		public static float EditorWaterLevel = IslandFile.DefaultWaterLevel;
		static GameObject waterPlane;

		/// <summary>Sets the sea level of the island being edited and moves the editor's blue sea plane to it.</summary>
		public static void SetEditorWaterLevel(float level)
		{
			EditorWaterLevel = Mathf.Clamp(level, 1f, IslandGenerator.BuildArea.y - 10f);
			if (waterPlane != null)
			{
				Vector3 p = waterPlane.transform.position;
				waterPlane.transform.position = new Vector3(p.x, (terraineditor.terrain != null ? terraineditor.terrain.transform.position.y : 0f) + EditorWaterLevel, p.z);
			}
		}

		/// <summary>Sets the style of the island being edited: re-skins the editor terrain and relabels the paint buttons.</summary>
		public static void SetEditorStyle(int style)
		{
			currentStyle = Mathf.Clamp(style, 0, TerrainPainter.Styles.Length - 1);
			if (terraineditor.terrain != null) TerrainPainter.SetStyle(terraineditor.terrain, currentStyle);
			EditorUI.RefreshStyle();
		}
		public static LoadSceneManager loadSceneManagerinstance;

		public AssetBundle mainbundle;
		public AssetBundle helperbundle;



		public static List<Shader> _shaders = new List<Shader>();
		public static TransformGizmo EditorGizmoHandler;

		public static DynamicIslands instance;



		#region IslandObjectDefinition

		// Placeable objects now live in PlaceableCatalog (built from Raft's Vasagatan scene)



		



		#endregion


		public void Start()
		{
			//Pushing notification for mod loading
			HNotification DynamicIslandsLoad = FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.spinning, "Loading Custom Islands...");


			instance = this;
			Editor.UIKit.CaptureRaftLook(); // Raft's menu sprites and fonts, while the main menu has them loaded
			// The await helpers normally self-initialise at game startup, which never happens for a mod
			Redcode.Awaiting.Engine.ContextHelper.SaveContext();
			Redcode.Awaiting.Engine.RoutineHelper.CreateInstance();
			loadSceneManagerinstance = FindObjectOfType<LoadSceneManager>();
			var harmony = new Harmony("com.franzfischer.customislands");
			harmony.PatchAll();
			CreatureSpawner.Patch(harmony);

			//INIT FOLDER
			if (!Directory.Exists(assetpath))
			{
				Directory.CreateDirectory(assetpath);
			}
			if (Directory.EnumerateFiles(assetpath).Count() == 0)
			{
				Debug.LogWarning("There are no custom Islands installed!");
			}

			if (GetEmbeddedFileBytes("editorsceneci.assets").Length == 0)
			{
				Debug.Log("embeddedfilebytes are null");
			}

			mainbundle = AssetBundle.LoadFromMemory(GetEmbeddedFileBytes("editorsceneci.assets"));
			helperbundle = AssetBundle.LoadFromMemory(GetEmbeddedFileBytes("maincustomislandsbundle.assets"));

			//Adding the Editor button to the main menu (again every time the main menu scene is reloaded)
			HookUI();
			SceneManager.sceneLoaded += OnSceneLoaded;
			// Sample world plans, the first time (Mods\DynamicIslands\plans)
			WorldPlanWindow.EnsureSamples();
			// Custom islands saved with a world come back when it loads
			SaveAndLoad.LoadComplete += IslandWorldState.OnWorldLoaded;
			SaveAndLoad.LoadComplete += CreatureSpawner.OnWorldLoaded;
			// Raft's world shifts and "world received" (for clients): hooked every frame by HookRaftEvents, since
			// Raft empties these events when a game is left
			HookRaftEvents();
			// An island's quest done: its "on.quest" actions
			QuestTracker.Advanced += Behaviours.OnQuestAdvanced;


			// Dev builds: the test commands can also be run from a file (release builds leave DevTests out)
			try
			{
				MethodInfo devInit = typeof(DynamicIslands).Assembly.GetType("DynamicIslands.DevTests")?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
				if (devInit != null) devInit.Invoke(null, null);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dev tests: " + e.Message); }

			DynamicIslandsLoad.Close();
			DynamicIslandsLoad = FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.normal, "Custom Islands has been loaded!", 5);
			Debug.Log("[CUSTOM ISLANDS] Mod Custom Islands has been loaded successfully!");
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (mode == LoadSceneMode.Single && GameObject.Find("MainMenuCanvas") != null)
				HookUI();
		}

		private void HookUI()
		{
			GameObject MainMenuParent = GameObject.Find("MainMenuCanvas");
			if (MainMenuParent == null) return;
			Transform existing = MainMenuParent.transform.Find("MenuButtons/EDITOR");
			if (existing != null) return;

			GameObject MenuButtonsParent = MainMenuParent.transform.Find("MenuButtons").gameObject;

			GameObject NewGamePanelParent = MainMenuParent.transform.Find("New Game Box").gameObject;
			GameObject LoadGamePanelParent = MainMenuParent.transform.Find("Load Game Box").gameObject;

			GameObject CreateGameButton = MainMenuParent.transform.Find("New Game Box").transform.Find("CreateGameButton").gameObject;

			Debug.Log("Hooking UI");

			try
			{
				//Hooking onto the main menu to add new buttons
				//Modpacks browser online
				GameObject ModpacksButton = Instantiate(MenuButtonsParent.transform.Find("New Game").gameObject, MenuButtonsParent.transform);
				ModpacksButton.name = "EDITOR";
				ModpacksButton.transform.SetSiblingIndex(3);
				Debug.Log("namebutton: " + ModpacksButton.name);
				ModpacksButton.GetComponentInChildren<Text>().text = "EDITOR";
				ModpacksButton.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();

				ModpacksButton.GetComponent<Button>().onClick.RemoveAllListeners();

				ModpacksButton.GetComponent<Button>().onClick.AddListener(() =>
				{
					Debug.Log("Editor");
					LaunchEditor();
				});


			}
			catch (Exception e)
			{
				Debug.Log("Error adding button to main menu raft ui: " + e);
			}




		}

		public void LaunchEditor()
		{
			string[] str = new string[] { };
			LoadEditor(str);
		}

		static readonly Action<Vector3> onWorldShift = IslandWorldState.OnWorldShift;
		static readonly Action onWorldReceived = IslandNetwork.OnWorldReceived;

		/// <summary>
		/// Keeps the mod on two of Raft's static events. Raft sets them to null when a game is left
		/// (WorldShiftManager and Raft_Network, SceneEventInterface.OnSceneEvent), which silently dropped the mod's
		/// handlers: after going back to the main menu once, custom islands stopped following world shifts, and a
		/// player joining again got no islands from the host. Found in the two-player test.
		/// </summary>
		static void HookRaftEvents()
		{
			// (the custom islands follow Raft's floating-origin world shifts)
			if (WorldShiftManager.OnWorldShift == null || Array.IndexOf(WorldShiftManager.OnWorldShift.GetInvocationList(), onWorldShift) < 0)
				WorldShiftManager.OnWorldShift += onWorldShift;
			// (a client asks for the host's islands once the host's world is here: the raft is where the host's is)
			if (Raft_Network.OnWorldReceivedLate == null || Array.IndexOf(Raft_Network.OnWorldReceivedLate.GetInvocationList(), onWorldReceived) < 0)
				Raft_Network.OnWorldReceivedLate += onWorldReceived;
		}

		private void Update()
		{
			try { HookRaftEvents(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Raft events: " + e); }
			try { CustomIslandSpawner.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Island spawner: " + e); }
			try { IslandNetwork.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Island network: " + e); }
			try { PlayerHold.Tick(); PlayerPlaces.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Player hold: " + e); }
			try { CreatureSpawner.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Creatures: " + e); }
			try { QuestTracker.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Quests: " + e); }
			try { IslandInfo.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Island banner: " + e); }
			try { Behaviours.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Behaviours: " + e); }
			try { WorldDirector.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] World director: " + e); }
			try { JournalWindow.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Journal: " + e); }
		}

		/// <summary>Messages sent with SendNetworkMessage arrive here (RML subscribes the mod to its own channel).</summary>
		public override bool OnNetworkMessage(object message, Network_UserId from, string modslug)
		{
			return IslandNetwork.OnMessage(message, from) || base.OnNetworkMessage(message, from, modslug);
		}

		public void OnModUnload()
		{
			//The mod will not be able to be unloaded, therefore this will be unused
			Debug.Log("Mod Custom Islands has been unloaded!");
		}



		[ConsoleCommand(name: "LoadEditor", docs: "Loads into the Editor via Command")]
		public static async void LoadEditor(string[] args)
		{
			if (instance.mainbundle == null) { Debug.LogError("[CUSTOM ISLANDS] The editor bundle is not loaded"); return; }
			string[] scenePath = instance.mainbundle.GetAllScenePaths();
			string editorScene = scenePath.FirstOrDefault(p => Utils.SceneNameFromPath(p) == "Editor");
			if (editorScene == null) { Debug.LogError("[CUSTOM ISLANDS] The editor bundle has no Editor scene"); return; }
			SceneManager.LoadScene(editorScene, LoadSceneMode.Single);
			Scene scene = SceneManager.GetSceneByName(Utils.SceneNameFromPath(editorScene));
			while (!scene.isLoaded) await new WaitForSeconds(.1f);
			await new WaitForSeconds(0.5f);

			RAPI.ToggleCursor(true);
			Camera mainCamera = Camera.main;
			mainCamera.gameObject.AddComponent<terraineditor>();
			mainCamera.gameObject.AddComponent<EditorCamera>(); // (Unity-style: right-drag look, WASD, middle-drag pan, Alt+drag orbit, wheel zooms to the cursor, F frames)

			// The editor's screen is built in code (EditorUI); the bundle's old canvas (toolbar, dropdown) is switched off
			try
			{
				// (every canvas of the bundle scene: toolbar, navbar, the old object list)
				foreach (Canvas old in FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.gameObject.scene.name == "Editor").ToList())
					old.gameObject.SetActive(false);
				var tabSelector = new GameObject("CustomIslandsTabs").AddComponent<TabSelector>();
				tabSelector.SelectedTab = TAB.TerrainEdit; // building an island starts with shaping land
				EditorUI.Setup(null, tabSelector);
			}
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Could not set up the editor UI: " + e); }

			//Load shaders (the transform gizmo needs them in Awake, so this must happen first)
			try
			{
				_shaders.Clear();
				foreach (UnityEngine.Object sh in instance.helperbundle.LoadAllAssets(typeof(Shader)))
				{
					_shaders.Add((Shader)sh);
				}
				Debug.Log("[CUSTOM ISLANDS] Editor shaders: " + string.Join(", ", _shaders.Select(s => s.name).ToArray()));
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not load editor shaders: " + e); }

			// The gizmo must exist before any object can be picked
			EditorGizmoHandler = Camera.main.gameObject.AddComponent<TransformGizmo>();

			CreateWaterLevelPlane();

			// Start above the middle of the (1000 x 1000) build area, looking down at it, rather than at the corner under water
			Vector3 buildCentre = new Vector3(500f, EditorWaterLevel, 500f);
			Camera.main.transform.position = buildCentre + new Vector3(0f, 60f, -120f);
			Camera.main.transform.rotation = Quaternion.Euler(28f, 0f, 0f);

			// Islands window and generator (built on the editor's canvas, in the same style)
			try { IslandFilesWindow.Create(EditorUI.Canvas.transform, null); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Could not create the islands window: " + e); }
			try { GeneratorWindow.Create(EditorUI.Canvas.transform, null); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Could not create the generator window: " + e); }
			try { NoteEditorWindow.Create(EditorUI.Canvas.transform); ItemPickerWindow.Create(EditorUI.Canvas.transform); TextPromptWindow.Create(EditorUI.Canvas.transform); SoundPickerWindow.Create(EditorUI.Canvas.transform); QuestEditorWindow.Create(EditorUI.Canvas.transform); ChoiceWindow.Create(EditorUI.Canvas.transform); WorldPlanWindow.Create(EditorUI.Canvas.transform); BehaviourWindow.Create(EditorUI.Canvas.transform); StoryItemsWindow.Create(EditorUI.Canvas.transform); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Could not create the note editor: " + e); }

			HNotification catalogNote = FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.spinning, "Loading placeable objects...");
			await PlaceableCatalog.EnsureBuilt();
			catalogNote.Close();
			// Creature models seen in a world since the catalog was built replace their markers
			try { ContentCatalog.UpgradeMarkers(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Creature models: " + e.Message); }
			// The builder's saved object groups ("My groups")
			instance.StartCoroutine(GroupLibrary.RegisterAll());
			currentIslandProps = new Dictionary<string, string>();
			// Raft's own ground textures are borrowed while the catalog loads its islands; a new island starts tropical,
			// at sea level
			currentElevation = 0f;
			SetEditorStyle(TerrainPainter.Tropical);
			EditorUI.RefreshIsland();

			Debug.Log("[CUSTOM ISLANDS] Editor ready. Console: SaveIsland <name>, LoadIsland <name>, ListIslands");

			// Once per Raft version: find every object of Raft's other islands (runs in the background)
			if (!PlaceableCatalog.IndexIsCurrent) instance.StartCoroutine(PlaceableCatalog.EnsureIndex());
		}

		/// <summary>Editor: starts an empty island (flat seabed, no objects, tropical, at sea level).</summary>
		public static void NewIsland()
		{
			if (!InEditor()) return;
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			if (EditorGizmoHandler != null) EditorGizmoHandler.ClearTargets(false);
			data.SetHeights(0, 0, new float[data.heightmapResolution, data.heightmapResolution]);
			foreach (Transform child in GameObject.Find("PlacedObjects").transform) Destroy(child.gameObject);
			currentIslandName = "myisland";
			currentElevation = 0f;
			currentIslandProps = new Dictionary<string, string>();
			SetEditorStyle(TerrainPainter.Tropical);
			terraineditor.paintMask = new float[data.alphamapResolution, data.alphamapResolution];
			SetEditorWaterLevel(IslandFile.DefaultWaterLevel);
			TerrainPainter.Setup(terrain, EditorWaterLevel);
			CommandUndoRedo.UndoRedoManager.Clear();
			EditorUI.RefreshIsland();
			Notify("New island: shape the land on the Terrain tab, then place objects");
		}

		#region Save / load (.island files in Mods\DynamicIslands)

		static bool IsValidIslandName(string name)
		{
			return !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
		}

		internal static void Notify(string text, bool error = false)
		{
			if (error) Debug.LogWarning("[CUSTOM ISLANDS] " + text); else Debug.Log("[CUSTOM ISLANDS] " + text);
			try
			{
				FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.normal, text, 4, error ? HNotify.ErrorSprite : HNotify.CheckSprite);
			}
			catch { }
		}

		public static bool InEditor()
		{
			return terraineditor.terrain != null && GameObject.Find("PlacedObjects") != null;
		}

		[ConsoleCommand(name: "SaveIsland", docs: "Editor: saves the island. Usage: SaveIsland <name>  (no name = current island)")]
		public static void SaveIslandCommand(string[] args)
		{
			SaveIsland(args != null && args.Length > 0 ? string.Join(" ", args) : currentIslandName);
		}

		[ConsoleCommand(name: "LoadIsland", docs: "Editor: loads a saved island. Usage: LoadIsland <name>")]
		public static void LoadIslandCommand(string[] args)
		{
			LoadIsland(args != null && args.Length > 0 ? string.Join(" ", args) : currentIslandName);
		}

		[ConsoleCommand(name: "DeleteGroup", docs: "Editor: deletes a saved object group (Mods\\DynamicIslands\\groups). Usage: DeleteGroup <name>")]
		public static void DeleteGroupCommand(string[] args)
		{
			string name = args != null ? string.Join(" ", args) : "";
			if (name.Length == 0) { Notify("Usage: DeleteGroup <name>   Groups: " + string.Join(", ", GroupLibrary.Saved().ToArray()), true); return; }
			bool deleted = GroupLibrary.Delete(name);
			Notify(deleted ? "Deleted group '" + name + "'" : "No group called '" + name + "'", !deleted);
		}

		[ConsoleCommand(name: "ListIslands", docs: "Lists saved islands (.island files in Mods\\DynamicIslands)")]
		public static void ListIslandsCommand()
		{
			Debug.Log("[CUSTOM ISLANDS] Saved islands: " + string.Join(", ", IslandSpawner.ListSavedIslands().ToArray()));
		}

		public static bool SaveIsland(string name)
		{
			if (!InEditor()) { Notify("SaveIsland only works inside the editor", true); return false; }
			if (!IsValidIslandName(name)) { Notify("Invalid island name: '" + name + "'", true); return false; }
			try
			{
				IslandFile island = IslandFile.Capture(name, terraineditor.terrain, GameObject.Find("PlacedObjects").transform, terraineditor.paintMask);
				island.Elevation = currentElevation;
				island.Style = currentStyle == TerrainPainter.Tropical ? "" : TerrainPainter.StyleName(currentStyle);
				island.Props = new Dictionary<string, string>(currentIslandProps);
				island.Save(IslandSpawner.PathFor(name));
				currentIslandName = name;
				EditorUI.RefreshIsland();
				Notify("Saved island '" + name + "' (" + island.Objects.Count + " objects)");
				return true;
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Saving failed: " + e);
				Notify("Saving '" + name + "' failed - see console (F10)", true);
				return false;
			}
		}

		public static bool LoadIsland(string name)
		{
			if (!InEditor()) { Notify("LoadIsland only works inside the editor", true); return false; }
			string path = IslandSpawner.PathFor(name);
			if (!File.Exists(path)) { Notify("No saved island named '" + name + "'", true); return false; }
			if (!PlaceableCatalog.IsBuilt) { Notify("Objects are still loading, try again in a moment", true); return false; }
			try
			{
				IslandFile island = IslandFile.Load(path);

				// Objects from Raft's other islands load first (their island scenes), then the island loads
				List<string> scenes = PlaceableCatalog.ScenesNeededFor(island.Objects.Select(o => o.Name));
				if (scenes.Count > 0)
				{
					Notify("Loading objects from " + string.Join(", ", scenes.Select(PlaceableCatalog.SceneLabel).ToArray()) + " for '" + name + "'...");
					instance.StartCoroutine(LoadAfter(PlaceableCatalog.EnsureLoaded(island.Objects.Select(o => o.Name).ToList()), name));
					return true;
				}

				Terrain terrain = terraineditor.terrain;
				if (terrain.terrainData.heightmapResolution != island.HeightmapResolution || terrain.terrainData.size != island.TerrainSize)
				{
					terrain.terrainData.heightmapResolution = island.HeightmapResolution;
					terrain.terrainData.size = island.TerrainSize;
				}
				terrain.terrainData.SetHeights(0, 0, island.Heights);
				SetEditorWaterLevel(island.WaterLevel);
				SetEditorStyle(TerrainPainter.StyleIndex(island.Style)); // before painting, so the right textures go on
				if (island.HasPaint)
				{
					TerrainPainter.ApplySaved(terrain, island.GetAlphamapBlock(0, 0, island.AlphamapResolution));
					terraineditor.paintMask = island.GetPaintMask() ?? new float[island.AlphamapResolution, island.AlphamapResolution];
				}
				else
				{
					// Format 1 files have no paint: texture automatically
					terraineditor.paintMask = new float[terrain.terrainData.alphamapResolution, terrain.terrainData.alphamapResolution];
					TerrainPainter.Setup(terrain, island.WaterLevel);
				}

				Transform placed = GameObject.Find("PlacedObjects").transform;
				foreach (Transform child in placed) Destroy(child.gameObject);
				// PlacedObjects may not sit at the terrain origin; place relative to the terrain
				var holder = new GameObject("LoadedObjects");
				holder.transform.SetParent(placed, false);
				holder.transform.position = terrain.transform.position;
				int missing = IslandSpawner.SpawnObjects(island, holder.transform, true);

				currentIslandName = name;
				currentElevation = island.Elevation;
				currentIslandProps = new Dictionary<string, string>(island.Props);
				// Undo steps refer to the terrain/objects that were just replaced
				CommandUndoRedo.UndoRedoManager.Clear();
				EditorUI.RefreshIsland();
				Notify("Loaded island '" + name + "'" + (missing > 0 ? " (" + missing + " objects missing)" : ""), missing > 0);
				return true;
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Loading failed: " + e);
				Notify("Loading '" + name + "' failed - see console (F10)", true);
				return false;
			}
		}

		static IEnumerator LoadAfter(IEnumerator loading, string name)
		{
			yield return loading;
			if (InEditor()) LoadIsland(name);
		}

		/// <summary>Semi-transparent plane showing where the sea will be when the island is spawned in game.</summary>
		static void CreateWaterLevelPlane()
		{
			try
			{
				GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
				plane.name = "WaterLevel";
				Destroy(plane.GetComponent<Collider>()); // must not block terrain raycasts
				Vector3 size = terraineditor.terrain != null ? terraineditor.terrain.terrainData.size : new Vector3(1000, 600, 1000);
				plane.transform.position = new Vector3(size.x / 2f, EditorWaterLevel, size.z / 2f);
				waterPlane = plane;
				plane.transform.localScale = new Vector3(size.x / 10f, 1, size.z / 10f); // Unity plane is 10x10
				Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
				if (shader != null)
				{
					plane.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.1f, 0.45f, 0.8f, 0.35f) };
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not create water level plane: " + e); }
		}

		#endregion

		public static void PlaceObject(string objectName)
		{
			//Only one object can be in "placing" mode at a time
			ObjectPlacer existing = FindObjectOfType<ObjectPlacer>();
			if (existing != null) Destroy(existing.gameObject);

			GameObject NewObjectToPlace = PlaceableCatalog.Spawn(objectName, null);
			if (NewObjectToPlace == null) { Debug.LogWarning("[CUSTOM ISLANDS] Unknown object " + objectName); return; }
			NewObjectToPlace.AddComponent<ObjectPlacer>().GameObjectName = objectName;
		}

		#region Spawning editor islands (.island) in a world

		/// <summary>Distance in front of the raft where SpawnIsland places the island's land. Raft's camera only renders to 400 m.</summary>
		const float SpawnDistance = 250f;

		[ConsoleCommand(name: "SpawnIsland", docs: "Host, in game: spawns a saved editor island in front of the raft. Usage: SpawnIsland <name> [distance in m, default 250] [height above sea in m, default the island's own elevation]")]
		public static void SpawnIslandCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Notify("Usage: SpawnIsland <name> [distance] [height]   (ListIslands shows saved islands)", true); return; }
			// Trailing numbers: distance, then height (island names may contain spaces)
			var numbers = new List<float>();
			float parsed;
			while (args.Length > 1 && numbers.Count < 2 && float.TryParse(args[args.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
			{
				numbers.Insert(0, parsed);
				args = args.Take(args.Length - 1).ToArray();
			}
			float distance = numbers.Count > 0 ? Mathf.Clamp(numbers[0], 20f, 390f) : SpawnDistance;
			float? height = numbers.Count > 1 ? Mathf.Clamp(numbers[1], IslandSpawner.MinElevation, IslandSpawner.MaxElevation) : (float?)null;
			if (!LoadSceneManager.IsGameSceneLoaded) { Notify("You need to be in a game to spawn an island", true); return; }
			if (!Raft_Network.IsHost) { Notify("Only the host can spawn islands", true); return; }

			Raft raft = FindObjectOfType<Raft>();
			Vector3 origin = raft != null ? raft.transform.position : Vector3.zero;
			Vector3 dir = Raft.direction.sqrMagnitude > 0.01f ? Raft.direction.normalized : Vector3.forward;
			Vector3 position = origin + new Vector3(dir.x, 0, dir.z).normalized * distance;
			string name = string.Join(" ", args);
			// The island's y is its elevation above sea level (0 = a normal island)
			position.y = height ?? IslandSpawner.ElevationOf(name);
			// On top of one of Raft's own islands, players can fall through the ground: say so (the automatic spawner avoids it)
			string overlap = CustomIslandSpawner.OverlapsRaftIsland(position, CustomIslandSpawner.LandRadius(name));
			if (overlap != null) Notify("Careful: " + overlap + " - the islands overlap; try another distance or direction", true);

			instance.StartCoroutine(instance.SpawnIslandFile(name, position, true));
		}

		/// <param name="broadcast">host spawning a new island: tell clients and remember it in the world's island list</param>
		/// <param name="entry">host (re)loading an island that is already in the world's island list (automatic
		/// spawns and streaming): its Root is set once spawned, and nothing is shown to the player</param>
		public IEnumerator SpawnIslandFile(string name, Vector3 position, bool broadcast, IslandWorldState.Entry entry = null)
		{
			bool quiet = entry != null;
			string path = IslandSpawner.PathFor(name);
			IslandFile island = null;
			try
			{
				if (File.Exists(path)) island = IslandFile.Load(path);
				else Notify("No saved island named '" + name + "' in " + assetpath, true);
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Could not read " + path + ": " + e);
				Notify("Could not read island '" + name + "' - see console (F10)", true);
			}
			if (island == null)
			{
				if (entry != null) { entry.Loading = false; entry.Failed = true; }
				yield break;
			}

			// The core objects, plus any from Raft's other islands this island uses
			yield return PlaceableCatalog.EnsureLoaded(island.Objects.Select(o => o.Name).ToList());

			if (entry != null)
			{
				entry.Loading = false;
				// Removed while loading, or the world changed
				if (!IslandWorldState.Contains(entry)) yield break;
				position = entry.Position; // follows world shifts that happened meanwhile
			}

			try
			{
				GameObject root = IslandSpawner.SpawnInWorld(island, position);
				root.AddComponent<ReApplyShaders>();
				if (entry == null && Raft_Network.IsHost && broadcast) entry = IslandWorldState.Add(name, position, root);
				if (entry != null)
				{
					entry.Root = root;
					IslandSpawner.RegisterNetworkIds(root, entry.Id);
					IslandObjectState.Apply(entry, IslandRules.RegrowDays(entry));
					Behaviours.OnIslandReady(entry); // objects shown or hidden, doors open or closed, as saved
					ContentState.OnIslandReady(entry); // first: chests refill and zones re-arm before the creatures look at them
					CreatureSpawner.OnIslandReady(entry);
				}
				if (!quiet) Notify("Spawned island '" + name + "'");
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Spawning '" + name + "' failed: " + e);
				if (entry != null) entry.Failed = true;
				Notify("Spawning '" + name + "' failed - see console (F10)", true);
			}
		}

		[ConsoleCommand(name: "RemoveIsland", docs: "Host, in game: removes spawned custom islands. Usage: RemoveIsland <name>   or   RemoveIsland all")]
		public static void RemoveIslandCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Notify("Usage: RemoveIsland <name> | all   (ListSpawned shows them)", true); return; }
			if (!Raft_Network.IsHost) { Notify("Only the host can remove islands", true); return; }
			string name = string.Join(" ", args);
			int n = IslandWorldState.Remove(name.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : name);
			Notify(n > 0 ? "Removed " + n + " island(s). They stay gone after the next save." : "No spawned island called '" + name + "'", n == 0);
		}

		[ConsoleCommand(name: "ListSpawned", docs: "Lists the custom islands spawned in this world (saved with the world)")]
		public static void ListSpawnedCommand()
		{
			if (IslandWorldState.Islands.Count == 0) { Debug.Log("[CUSTOM ISLANDS] No custom islands spawned in this world"); return; }
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			foreach (var e in IslandWorldState.Islands)
				Debug.Log("[CUSTOM ISLANDS] " + e.HostName + " at " + e.Position +
					(raftPos.HasValue ? ", " + Vector3.Distance(new Vector3(e.Position.x, 0, e.Position.z), new Vector3(raftPos.Value.x, 0, raftPos.Value.z)).ToString("F0") + " m from the raft" : "") +
					(e.Name != e.HostName ? " [file " + e.Name + "]" : "") +
					(e.Failed ? " (island file missing or broken)" : e.WaitingForFile ? " (waiting for the file from the host)" : e.Loading ? " (loading)" : e.Root == null ? " (unloaded: far away)" : ""));
		}

		[ConsoleCommand(name: "SpawnPool", docs: "Shows which islands appear on their own while sailing, and how often (edit Mods\\DynamicIslands\\spawnpool.txt to change)")]
		public static void SpawnPoolCommand()
		{
			foreach (string line in CustomIslandSpawner.Describe().Split('\n')) Debug.Log("[CUSTOM ISLANDS] " + line);
		}

		[ConsoleCommand(name: "WorldPlan", docs: "The world plan: which islands this world gets, when and where. WorldPlan = show it and its rules; WorldPlan <name> = give this world another plan (host)")]
		public static void WorldPlanCommand(string[] args)
		{
			string name = args != null ? string.Join(" ", args).Trim() : "";
			if (name.Length > 0)
			{
				if (!LoadSceneManager.IsGameSceneLoaded) { Notify("You need to be in a world (the plan is per world); for a new world, choose it in the New Game box", true); return; }
				if (!Raft_Network.IsHost) { Notify("Only the host can change the world plan", true); return; }
				if (!WorldDirector.SetPlan(name, true)) { Notify("No world plan '" + name + "'. Plans: " + string.Join(", ", WorldPlan.All().ToArray()), true); return; }
				Notify("This world now follows the plan '" + WorldDirector.PlanName + "' (kept when the world is saved)");
			}
			foreach (string line in WorldDirector.Describe().Split('\n')) Debug.Log("[CUSTOM ISLANDS] " + line);
		}

		[ConsoleCommand(name: "CustomIslandsAuto", docs: "Host: custom islands appear on their own while sailing in this world. Usage: CustomIslandsAuto on|off")]
		public static void CustomIslandsAutoCommand(string[] args)
		{
			if (args == null || args.Length == 0 || !(args[0].Equals("on", StringComparison.OrdinalIgnoreCase) || args[0].Equals("off", StringComparison.OrdinalIgnoreCase)))
			{
				Notify("Automatic islands are " + (CustomIslandSpawner.Enabled ? "on" : "off") + " in this world. Usage: CustomIslandsAuto on|off");
				return;
			}
			if (!LoadSceneManager.IsGameSceneLoaded) { Notify("You need to be in a game (the setting is per world)", true); return; }
			if (!Raft_Network.IsHost) { Notify("Only the host can change this", true); return; }
			CustomIslandSpawner.Enabled = args[0].Equals("on", StringComparison.OrdinalIgnoreCase);
			Notify("Automatic islands " + (CustomIslandSpawner.Enabled ? "on" : "off") + " in this world (kept when the world is saved)");
		}

		#endregion


		#region OnlineIslandDatabaseHandling
		//Logic to up or download custom islands from the custom islands server.

		[ConsoleCommand(name: "UploadFileTest", docs: "Upload Custom Island to the Server (left here for further development, is currently unused)")]
		public static void uploadIsland(string[] args)
		{
			instance.StartCoroutine(instance.UploadIsland("user", "password", "arandomisland"));
		}

		IEnumerator UploadIsland(string username, string password, string islandname)
		{


			WWWForm form = new WWWForm();
			form.AddBinaryData("islandfile", File.ReadAllBytes(@"Mods\demoisland1.assets"), "demoisland1.assets", "binary/octet-stream");
			form.AddField("username", username);
			form.AddField("password", System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password)));
			form.AddField("islandname", islandname);

			var uwr = new UnityWebRequest();
			uwr = UnityWebRequest.Post("http://localhost/CustomIslandsWebapp/upload.php", form);

			//uwr.uploadHandler = new UploadHandlerFile(@"Mods\hello.txt");
			yield return uwr.SendWebRequest();
			if (uwr.isNetworkError || uwr.isHttpError)
				Debug.LogError(uwr.error);
			else
			{
				// file data successfully sent
				Debug.Log("file uploaded" + uwr.downloadHandler.text + " with code " + uwr.responseCode);
			}

		}


		#endregion


		#region Commands

		[ConsoleCommand(name: "SetToRaise", docs: "Change Terrain Edit to raise the terrain")]
		public static void TerrainRaise()
		{
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Raise;
		}
		[ConsoleCommand(name: "SetToLower", docs: "Change Terrain Edit to lower the terrain")]
		public static void TerrainLower()
		{
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Lower;
		}
		[ConsoleCommand(name: "SetToFlatten", docs: "Change Terrain Edit to flatten the terrain")]
		public static void TerrainFlatten()
		{
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Flatten;
		}
		[ConsoleCommand(name: "SetToSample", docs: "Change Terrain Edit to sample the terrain")]
		public static void TerrainSample()
		{
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Sample;
		}
		[ConsoleCommand(name: "SetToSampleAverage", docs: "Change Terrain Edit to average sample the terrain")]
		public static void TerrainSampleAverage()
		{
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.SampleAverage;
		}
		[ConsoleCommand(name: "SetToSmooth", docs: "Change Terrain Edit to smooth the terrain")]
		public static void TerrainSmooth()
		{
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Smooth;
		}
		[ConsoleCommand(name: "PaintTexture", docs: "Terrain brush paints a texture by hand: PaintTexture sand|grass|rock|seabed")]
		public static void PaintTexture(string[] args)
		{
			int layer = args != null && args.Length > 0 ? Array.FindIndex(TerrainPainter.LayerNames, n => n.Equals(args[0], StringComparison.OrdinalIgnoreCase)) : -1;
			if (layer < 0) { Debug.LogWarning("Usage: PaintTexture " + string.Join("|", TerrainPainter.LayerNames).ToLower()); return; }
			terraineditor.paintLayer = layer;
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.PaintLayer;
			Debug.Log("[CUSTOM ISLANDS] Painting " + TerrainPainter.LayerNames[layer]);
		}
		[ConsoleCommand(name: "SetElevation", docs: "Editor: metres above sea level this island floats at in game (e.g. 60 = a flying island, -25 = under water, 0 = normal). Saved with the island.")]
		public static void SetElevationCommand(string[] args)
		{
			float v;
			if (args == null || args.Length == 0 || !float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
			{ Notify("Elevation is " + currentElevation + " m. Usage: SetElevation <metres>  (60 = flying, -25 = under water, 0 = normal)"); return; }
			currentElevation = Mathf.Clamp(v, IslandSpawner.MinElevation, IslandSpawner.MaxElevation);
			EditorUI.RefreshIsland();
			Notify("Island elevation: " + IslandSpawner.DescribeElevation(currentElevation) + " (saved with the island)");
		}

		[ConsoleCommand(name: "SetStyle", docs: "Editor: island style - Tropical, Snowy, Desert, Forest or Volcanic (ground textures; saved with the island)")]
		public static void SetStyleCommand(string[] args)
		{
			string names = string.Join(", ", TerrainPainter.Styles.Select(s => s.Name).ToArray());
			if (args == null || args.Length == 0) { Notify("Style is " + TerrainPainter.StyleName(currentStyle) + ". Usage: SetStyle " + names); return; }
			int i = Array.FindIndex(TerrainPainter.Styles, s => s.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
			if (i < 0) { Notify("Unknown style '" + args[0] + "'. Styles: " + names, true); return; }
			if (!InEditor()) { Notify("SetStyle only works inside the editor", true); return; }
			SetEditorStyle(i);
			Notify("Island style: " + TerrainPainter.StyleName(i) + (TerrainPainter.HasStyle(i) ? "" : " (its textures aren't loaded; showing tropical)"));
		}

		[ConsoleCommand(name: "GenerateIsland", docs: "Editor: generates a random island (replaces the current one; Ctrl+Z undoes). Usage: GenerateIsland [seed] [size in m] [height in m] [roughness 0-1] [peaks] [objects 0-1] [Tropical|Snowy|Desert|Forest|Volcanic]")]
		public static void GenerateIslandCommand(string[] args)
		{
			if (!InEditor()) { Notify("GenerateIsland only works inside the editor", true); return; }
			var s = new IslandGenSettings { Seed = UnityEngine.Random.Range(1, 999999) };
			var ci = System.Globalization.CultureInfo.InvariantCulture;
			float f; int i;
			if (args != null)
			{
				if (args.Length > 0 && int.TryParse(args[0], System.Globalization.NumberStyles.Integer, ci, out i)) s.Seed = i;
				if (args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, ci, out f)) s.Radius = f / 2f;
				if (args.Length > 2 && float.TryParse(args[2], System.Globalization.NumberStyles.Float, ci, out f)) s.Height = f;
				if (args.Length > 3 && float.TryParse(args[3], System.Globalization.NumberStyles.Float, ci, out f)) s.Roughness = f;
				if (args.Length > 4 && int.TryParse(args[4], System.Globalization.NumberStyles.Integer, ci, out i)) s.Peaks = i;
				if (args.Length > 5 && float.TryParse(args[5], System.Globalization.NumberStyles.Float, ci, out f)) s.ObjectDensity = f;
			}
			s.Style = args != null && args.Length > 6 ? TerrainPainter.StyleIndex(args[6]) : currentStyle;
			int n = IslandGenerator.GenerateInEditor(s);
			IslandGenerator.FrameCamera(s);
			Notify("Generated island " + s.Seed + " (" + n + " objects). Ctrl+Z undoes it.");
		}

		[ConsoleCommand(name: "SetToAutoPaint", docs: "Terrain brush returns painted areas to automatic texturing")]
		public static void TerrainAutoPaint()
		{
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.AutoPaint;
		}
		[ConsoleCommand(name: "ChangeHeight", docs: "Terrain brush diameter in metres (same as ChangeWidth; the brush is round)")]
		public static void TerrainHeight(string[] args)
		{
			TerrainWidth(args);
		}
		[ConsoleCommand(name: "ChangeWidth", docs: "Terrain brush diameter in metres, e.g. ChangeWidth 30")]
		public static void TerrainWidth(string[] args)
		{
			float value;
			if (args == null || args.Length == 0 || !float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) { Debug.LogWarning("Usage: ChangeWidth <metres>"); return; }
			terraineditor.brushRadius = Mathf.Clamp(value / 2f, terraineditor.MinRadius, terraineditor.MaxRadius);
			EditorUI.RefreshSliders();
			Debug.Log("[CUSTOM ISLANDS] Brush diameter: " + (terraineditor.brushRadius * 2f) + " m");
		}
		[ConsoleCommand(name: "ChangeStrength", docs: "Terrain brush speed in metres per second, e.g. ChangeStrength 4")]
		public static void TerrainStrength(string[] args)
		{
			float value;
			if (args == null || args.Length == 0 || !float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) { Debug.LogWarning("Usage: ChangeStrength <metres per second>"); return; }
			terraineditor.strength = Mathf.Clamp(value, terraineditor.MinStrength, terraineditor.MaxStrength);
			EditorUI.RefreshSliders();
			Debug.Log("[CUSTOM ISLANDS] Brush strength: " + terraineditor.strength + " m/s");
		}

		[ConsoleCommand(name: "EnableEditing", docs: "Enable the use of Terrain Edit")]
		public static void TerrainEnableEdit()
		{
			if (RAPI.IsCurrentSceneMainMenu()) { Debug.LogWarning($"x: Cant change value while in Main Menu"); return; }

			terraineditor.allowEditing = true;

			// GameObject canvasObj = canvasBundle.LoadAsset<GameObject>("TerrainEdit_Canvas");
			//
			// if (customCanvas == null)
			// {
			//     customCanvas = Instantiate(canvasObj);
			//     print($"{modName}: Couldn't find existing canvas, creating new one..");
			// }

		}

		[ConsoleCommand(name: "DisableEditing", docs: "Disable the use of Terrain Edit")]
		public static void TerrainDisableEdit()
		{
			if (RAPI.IsCurrentSceneMainMenu()) { Debug.LogWarning($"y: Cant change value while in Main Menu"); return; }

			terraineditor.allowEditing = false;

			// if(customCanvas == null){print("Issue trying to reference Custom Canvas..");}
			// else { customCanvas.SetActive(false); print($"{modName}:Custom Canvas now disabled!" + customCanvas); }

		}

		#endregion

	}

	//HARMONY PATCHES
	// (The old SceneLoader.LoadScenes postfix that auto-loaded every file in Mods\DynamicIslands as a JSON island
	//  was removed: islands are spawned with the SpawnIsland command until they join Raft's spawn pool.)

	#region MiscStuff

	public static class Utils
	{
		public static string SceneNameFromPath(string path)
		{
			string scenePathByBuildIndex = path;
			int num = scenePathByBuildIndex.LastIndexOf('/');
			string text = scenePathByBuildIndex.Substring(num + 1);
			int length = text.LastIndexOf('.');
			return text.Substring(0, length);
		}
	}

	//SHADER FIX 
	public class ReApplyShaders : MonoBehaviour
	{
		public Renderer[] renderers;
		public Material[] materials;
		public string[] shaders;

		void Awake()
		{
			Debug.Log("Getting renderers");
			renderers = GetComponentsInChildren<Renderer>();
		}

		void Start()
		{
			Debug.Log("FIXING SHADERS");
			foreach (var rend in renderers)
			{
				try
				{
					materials = rend.sharedMaterials;
					shaders = new string[materials.Length];

					for (int i = 0; i < materials.Length; i++)
					{
						try
						{
							shaders[i] = materials[i].shader.name;
						}
						catch { }
					}

					for (int i = 0; i < materials.Length; i++)
					{
						try
						{
							materials[i].shader = Shader.Find(shaders[i]);
						}
						catch { }
					}
				}
				catch
				{

				}
			}
		}
	}


	#endregion

}
