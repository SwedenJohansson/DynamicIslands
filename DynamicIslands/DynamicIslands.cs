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
			// The await helpers normally self-initialise at game startup, which never happens for a mod
			Redcode.Awaiting.Engine.ContextHelper.SaveContext();
			Redcode.Awaiting.Engine.RoutineHelper.CreateInstance();
			loadSceneManagerinstance = FindObjectOfType<LoadSceneManager>();
			var harmony = new Harmony("com.franzfischer.customislands");
			harmony.PatchAll();

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
			// Custom islands saved with a world come back when it loads
			SaveAndLoad.LoadComplete += IslandWorldState.OnWorldLoaded;
			// ...and follow Raft's floating-origin world shifts
			WorldShiftManager.OnWorldShift += IslandWorldState.OnWorldShift;


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

		private void Update()
		{
			try { CustomIslandSpawner.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Island spawner: " + e); }
			try { IslandNetwork.Tick(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Island network: " + e); }
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
			if (instance.mainbundle == null)
			{
				Debug.Log("Mainbundle is null");

			}
			string[] scenePath = instance.mainbundle.GetAllScenePaths();
			if (scenePath.Length == 0)
			{
				Debug.Log("scenepath is null");

			}

			var scene = new Scene();
			foreach (string sceneName in scenePath)
			{
				if(Utils.SceneNameFromPath(sceneName) == "Editor")
				{
					SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
					scene  = SceneManager.GetSceneByName(Utils.SceneNameFromPath(sceneName));
				}
			}

			Debug.Log(scenePath[0]);




			try
			{
				//Debug.Log("check if scene is loaded");

				while (!scene.isLoaded)
				{
					//Debug.Log("scene not loaded, waiting");
					await new WaitForSeconds(.1f);
				}
				Debug.Log("scene loaded");
				await new WaitForSeconds(1f);

				GameObject[] rootgoeditor = SceneManager.GetSceneByName(Utils.SceneNameFromPath(scenePath[0])).GetRootGameObjects();
				GameObject Canvas = rootgoeditor[0].gameObject.transform.Find("Canvas").gameObject;
				GameObject EditorNavbar = Canvas.gameObject.transform.Find("EditorNavbar").gameObject;
				GameObject Toolbar = Canvas.gameObject.transform.Find("Toolbar").gameObject;

			}
			catch { }
			await Task.Delay(1000);
			//need to get all gameobjects
			//process these and add them as buttons
			/*	Debug.Log("processing gameobjects");


				foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)) as GameObject[])
				{
					GlobalPrefabList.Add(go);
					Debug.Log(go.name);
				}*/

			Debug.Log("Adding cam move");
			RAPI.ToggleCursor(true);
			// Get a reference to the main camera
			
			Camera mainCamera = Camera.main;
			terraineditor terrainEditor = mainCamera.gameObject.AddComponent<terraineditor>();
			RTSCamera cam = mainCamera.gameObject.AddComponent<RTSCamera>();
			// Check if the main camera has a TerrainEditor component
			Debug.Log("Added Cam");


			//Name should be changed when further working with the hierarchy
			// A Dropdown only fires when the value changes, so entry 0 is a neutral "Menu" we reset to after every action
			Dropdown menuDropdown = GameObject.Find("DropdownMenu").GetComponent<Dropdown>();
			menuDropdown.options = new List<Dropdown.OptionData> {
				new Dropdown.OptionData("Menu"),
				new Dropdown.OptionData("Main menu"),
				new Dropdown.OptionData("Save island..."),
				new Dropdown.OptionData("Load island..."),
			};
			menuDropdown.SetValueWithoutNotify(0);
			menuDropdown.onValueChanged.AddListener((int index) =>
			{
				menuDropdown.SetValueWithoutNotify(0);
				switch (index)
				{
					case 1:
						SceneManager.LoadScene("MainMenuScene", LoadSceneMode.Single);
						break;
					case 2:
					case 3:
						IslandFilesWindow.Open();
						break;
				}
			});

			try
			{

				TabSelector tabbSelector = GameObject.Find("TabSelector").AddComponent<TabSelector>();
				tabbSelector.SelectedTab = TAB.TerrainEdit; // building an island starts with shaping land
				tabbSelector.ToolList = GameObject.Find("ToolList");
				EditorUI.Setup(GameObject.Find("Toolbar").transform.parent, tabbSelector);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not set up editor UI: " + e); }

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

			// The gizmo must exist before any palette button can be clicked
			EditorGizmoHandler = Camera.main.gameObject.AddComponent<TransformGizmo>();

			CreateWaterLevelPlane();

			// Start above the middle of the (1000 x 1000) build area, looking down at it, rather than at the corner under water
			Vector3 buildCentre = new Vector3(500f, IslandFile.DefaultWaterLevel, 500f);
			Camera.main.transform.position = buildCentre + new Vector3(0f, 60f, -120f);
			Camera.main.transform.rotation = Quaternion.Euler(28f, 0f, 0f);

			HNotification catalogNote = FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.spinning, "Loading placeable objects...");
			await PlaceableCatalog.EnsureBuilt();
			catalogNote.Close();
			// Raft's own ground textures are borrowed while the catalog loads its islands
			TerrainPainter.RefreshLayers(terraineditor.terrain);

			GameObject listButtonTemplate = null;
			try
			{
				//Add gameobjects to the gameobject list in the editor
				GameObject ContentGO = GameObject.Find("ToolList").transform.Find("ObjectTool/Scroll View/Viewport/Content").gameObject;
				GameObject ButtonTemplate = ContentGO.transform.Find("Button").gameObject;

				foreach (var category in PlaceableCatalog.ByCategory())
				{
					// Section header: a disabled copy of the button
					GameObject header = Instantiate(ButtonTemplate, ContentGO.transform);
					header.name = "Header_" + category.Key;
					header.GetComponentInChildren<Text>().text = "- " + category.Key.ToUpper() + " -";
					header.GetComponent<Button>().interactable = false;
					foreach (string objectName in category.Value)
					{
						string nameCopy = objectName;
						GameObject newButton = Instantiate(ButtonTemplate, ContentGO.transform);
						newButton.GetComponentInChildren<Text>().text = PlaceableCatalog.DisplayName(nameCopy);
						newButton.GetComponent<Button>().onClick.AddListener(() => { EditorGizmoHandler.placingObject = true; PlaceObject(nameCopy); });
					}
				}
				ButtonTemplate.SetActive(false);
				listButtonTemplate = ButtonTemplate;
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Could not fill the object list: " + e);
			}

			// Save / Load window (uses the object list's button style when available)
			try { IslandFilesWindow.Create(GameObject.Find("Toolbar").transform.parent, listButtonTemplate); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Could not create the islands window: " + e); }

			Debug.Log("[CUSTOM ISLANDS] Editor ready. Console: SaveIsland <name>, LoadIsland <name>, ListIslands");




			//Test load go to list
			//SceneManager.LoadSceneAsync()

		}

		#region Save / load (.island files in Mods\DynamicIslands)

		static bool IsValidIslandName(string name)
		{
			return !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
		}

		static void Notify(string text, bool error = false)
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
				island.Save(IslandSpawner.PathFor(name));
				currentIslandName = name;
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

				Terrain terrain = terraineditor.terrain;
				if (terrain.terrainData.heightmapResolution != island.HeightmapResolution || terrain.terrainData.size != island.TerrainSize)
				{
					terrain.terrainData.heightmapResolution = island.HeightmapResolution;
					terrain.terrainData.size = island.TerrainSize;
				}
				terrain.terrainData.SetHeights(0, 0, island.Heights);
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
				// Undo steps refer to the terrain/objects that were just replaced
				CommandUndoRedo.UndoRedoManager.Clear();
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

		/// <summary>Semi-transparent plane showing where the sea will be when the island is spawned in game.</summary>
		static void CreateWaterLevelPlane()
		{
			try
			{
				GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
				plane.name = "WaterLevel";
				Destroy(plane.GetComponent<Collider>()); // must not block terrain raycasts
				Vector3 size = terraineditor.terrain != null ? terraineditor.terrain.terrainData.size : new Vector3(1000, 600, 1000);
				plane.transform.position = new Vector3(size.x / 2f, IslandFile.DefaultWaterLevel, size.z / 2f);
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

		[ConsoleCommand(name: "SpawnIsland", docs: "Host, in game: spawns a saved editor island in front of the raft. Usage: SpawnIsland <name> [distance in m, default 250]")]
		public static void SpawnIslandCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Notify("Usage: SpawnIsland <name> [distance]   (ListIslands shows saved islands)", true); return; }
			float distance = SpawnDistance;
			float parsed;
			if (args.Length > 1 && float.TryParse(args[args.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
			{
				distance = Mathf.Clamp(parsed, 20f, 390f);
				args = args.Take(args.Length - 1).ToArray();
			}
			if (!LoadSceneManager.IsGameSceneLoaded) { Notify("You need to be in a game to spawn an island", true); return; }
			if (!Raft_Network.IsHost) { Notify("Only the host can spawn islands", true); return; }

			Raft raft = FindObjectOfType<Raft>();
			Vector3 origin = raft != null ? raft.transform.position : Vector3.zero;
			Vector3 dir = Raft.direction.sqrMagnitude > 0.01f ? Raft.direction.normalized : Vector3.forward;
			Vector3 position = origin + new Vector3(dir.x, 0, dir.z).normalized * distance;
			position.y = 0; // sea level

			instance.StartCoroutine(instance.SpawnIslandFile(string.Join(" ", args), position, true));
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

			yield return PlaceableCatalog.EnsureBuilt();

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
