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
	public class landmarkBundle
	{
		public string name;
		public string path;
		public AssetBundle bundle;
	}

	public static class MyIslands
	{
		public const ChunkPointType Landmark_TestIsland = (ChunkPointType)100;
	}


	public static class ChunkPointTypeExtensions
	{
		public static ChunkPointType AddValue(this ChunkPointType type, string value)
		{
			return (ChunkPointType)Enum.Parse(typeof(ChunkPointType), value, true);
		}
	}

	[System.Serializable]
	public class IslandMessage : Message
	{
		public string[] Islandtoload;
		// Set for .island files (new editor format); empty for legacy .assets landmark bundles
		public float[] Position;
	}

	public class DynamicIslands : Mod
	{
		public static List<landmarkBundle> landmarkBundles = new List<landmarkBundle>();
		public static readonly string assetpath = @"Mods\DynamicIslands\";

		/// <summary>Name used by the editor's Save/Load menu entries; set by LoadIsland/SaveIsland commands.</summary>
		public static string currentIslandName = "myisland";
		public static LoadSceneManager loadSceneManagerinstance;

		public AssetBundle mainbundle;
		public AssetBundle helperbundle;


		public static List<GameObject> GlobalPrefabList = new List<GameObject>();

		public static List<Shader> _shaders = new List<Shader>();
		public static TransformGizmo EditorGizmoHandler;
		//ChunkPointType lol = MyIslands.TheBestIslandOfAllTime;

		public static DynamicIslands instance;



		#region IslandObjectDefinition

		// Placeable objects now live in PlaceableCatalog (built from Raft's Vasagatan scene)



		



		#endregion


		public void Start()
		{
			//Pushing notification for mod loading
			HNotification DynamicIslandsLoad = FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.spinning, "Loading Custom Islands...");

			//ChunkPointType newValue = MyIslands.Landmark_TestIsland.AddValue("Landmark_NewValue");

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

			//Legacy .assets islands for SpawnCustomLandmark
			RefreshLandmarkBundles(new string[0]);

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
			

			if (!Raft_Network.IsHost)
			{
				// Choose a unique ID for the channel id to not interfer with other mods.
				NetworkMessage netMessage = RAPI.ListenForNetworkMessagesOnChannel(6969);
				if (netMessage != null)
				{
					CSteamID id = netMessage.steamid;
					Message message = netMessage.message;
					// Do your stuff with the message now that you know 
					// its yours and its the wanted type.
					IslandMessage msg = message as IslandMessage;
					if (msg == null || msg.Islandtoload == null || msg.Islandtoload.Length == 0) return;
					Debug.Log("[CUSTOM ISLANDS] Host asked to spawn island: " + msg.Islandtoload[0]);
					if (msg.Position != null && msg.Position.Length == 3)
						StartCoroutine(SpawnIslandFile(msg.Islandtoload[0], new Vector3(msg.Position[0], msg.Position[1], msg.Position[2]), false));
					else
						ForceSpawnNewLandmark(msg.Islandtoload);
				}
			}
		}

		public void OnModUnload()
		{
			//The mod will not be able to be unloaded, therefore this will be unused
			Debug.Log("Mod Custom Islands has been unloaded!");
		}

		#region Legacy landmark bundles (.assets, restored from v1.1.1)

		public static async Task readBundles()
		{
			List<landmarkBundle> bundles = new List<landmarkBundle>();

			foreach (string asset in Directory.EnumerateFiles(assetpath, "*.assets"))
			{
				try
				{
					landmarkBundle bundle = new landmarkBundle();
					bundle.path = asset;
					bundle.name = Path.GetFileNameWithoutExtension(asset);
					AssetBundleCreateRequest request = AssetBundle.LoadFromMemoryAsync(File.ReadAllBytes(asset));
					await request;
					bundle.bundle = request.assetBundle;
					if (bundle.bundle == null)
					{
						Debug.LogWarning("[CUSTOM ISLANDS] Could not load island bundle " + asset + " (built with an incompatible Unity version?)");
						continue;
					}
					bundles.Add(bundle);
					Debug.Log("[CUSTOM ISLANDS] Loaded island bundle " + asset);
				}
				catch (Exception e)
				{
					Debug.LogWarning("[CUSTOM ISLANDS] Could not load island bundle " + asset + ": " + e);
				}
			}

			landmarkBundles = bundles;
		}

		[ConsoleCommand(name: "RefreshLandmarkBundles", docs: "Reloads the .assets island bundles from Mods\\DynamicIslands")]
		public static async void RefreshLandmarkBundles(string[] args)
		{
			HNotification notification = FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.spinning, "Loading custom island bundles...");
			try
			{
				foreach (landmarkBundle bundle in landmarkBundles)
				{
					if (bundle.bundle != null) bundle.bundle.Unload(true);
				}
				landmarkBundles.Clear();
				await readBundles();
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] RefreshLandmarkBundles failed: " + e);
			}
			finally
			{
				notification.Close();
			}
		}

		#endregion



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
			Debug.Log("Loading landmark from scene " + scenePath[0]);



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

				EditorNavbar.transform.Find("Button").GetComponent<Button>().onClick.AddListener(() =>
				{
					Debug.Log("Loading Main Menu");
				});
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
				new Dropdown.OptionData("Save island"),
				new Dropdown.OptionData("Load island"),
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
						SaveIsland(currentIslandName);
						break;
					case 3:
						LoadIsland(currentIslandName);
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

			try
			{
				//Add gameobjects to the gameobject list in the editor
				GameObject ContentGO = GameObject.Find("ToolList").transform.Find("ObjectTool/Scroll View/Viewport/Content").gameObject;
				GameObject ButtonTemplate = ContentGO.transform.Find("Button").gameObject;

				foreach (string objectName in PlaceableCatalog.NamesByDisplayName)
				{
					string nameCopy = objectName;
					GameObject newButton = Instantiate(ButtonTemplate, ContentGO.transform);
					newButton.GetComponentInChildren<Text>().text = PlaceableCatalog.DisplayName(nameCopy);
					newButton.GetComponent<Button>().onClick.AddListener(() => { EditorGizmoHandler.placingObject = true; PlaceObject(nameCopy); });
				}
				ButtonTemplate.SetActive(false);
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Could not fill the object list: " + e);
			}

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

		[ConsoleCommand(name: "ListIslands", docs: "Lists saved islands (.island) and island bundles (.assets)")]
		public static void ListIslandsCommand()
		{
			Debug.Log("[CUSTOM ISLANDS] Saved islands: " + string.Join(", ", IslandSpawner.ListSavedIslands().ToArray()));
			Debug.Log("[CUSTOM ISLANDS] Island bundles (SpawnCustomLandmark): " + string.Join(", ", landmarkBundles.Select(b => b.name).ToArray()));
		}

		public static void SaveIsland(string name)
		{
			if (!InEditor()) { Notify("SaveIsland only works inside the editor", true); return; }
			if (!IsValidIslandName(name)) { Notify("Invalid island name: '" + name + "'", true); return; }
			try
			{
				IslandFile island = IslandFile.Capture(name, terraineditor.terrain, GameObject.Find("PlacedObjects").transform);
				island.Save(IslandSpawner.PathFor(name));
				currentIslandName = name;
				Notify("Saved island '" + name + "' (" + island.Objects.Count + " objects)");
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Saving failed: " + e);
				Notify("Saving '" + name + "' failed - see console (F10)", true);
			}
		}

		public static void LoadIsland(string name)
		{
			if (!InEditor()) { Notify("LoadIsland only works inside the editor", true); return; }
			string path = IslandSpawner.PathFor(name);
			if (!File.Exists(path)) { Notify("No saved island named '" + name + "'", true); return; }
			if (!PlaceableCatalog.IsBuilt) { Notify("Objects are still loading, try again in a moment", true); return; }
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
				TerrainPainter.Setup(terrain, island.WaterLevel);

				Transform placed = GameObject.Find("PlacedObjects").transform;
				foreach (Transform child in placed) Destroy(child.gameObject);
				// PlacedObjects may not sit at the terrain origin; place relative to the terrain
				var holder = new GameObject("LoadedObjects");
				holder.transform.SetParent(placed, false);
				holder.transform.position = terrain.transform.position;
				int missing = IslandSpawner.SpawnObjects(island, holder.transform, true);

				currentIslandName = name;
				Notify("Loaded island '" + name + "'" + (missing > 0 ? " (" + missing + " objects missing)" : ""), missing > 0);
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Loading failed: " + e);
				Notify("Loading '" + name + "' failed - see console (F10)", true);
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

		[ConsoleCommand(name: "SpawnPrefabTest", docs: "Refreshes the Bundle cache")]
		public static async void SpawnPrefabTest(string[] args)
		{


			Instantiate(GlobalPrefabList[System.Convert.ToInt32(args[0])]);




		}

		/*ItemManager.GetAllItems().ForEach(i =>
		{
			try
			{
				//GameObject go = i.settings_buildable.GetBlockPrefab(0).gameObject;
				Debug.Log("got gameobject" + i.GetUniqueName() + i.GetUniqueIndex());
				GlobalPrefabList.Add(i);
			}
			catch { }
		}
		);*/



		[ConsoleCommand(name: "SpawnCustomLandmark", docs: "Spawns a custom landmark")]
		public static void SpawnNewLandmark(string[] args)
		{
			if (Raft_Network.IsHost && LoadSceneManager.IsGameSceneLoaded)
			{
				IEnumerator coroutine = instance.customlandmarkienum(args);
				instance.StartCoroutine(coroutine);
			}
			else
			{
				Debug.LogWarning("You're not the host or you're not ingame");
			}
		}

		public static void ForceSpawnNewLandmark(string[] args)
		{
			if (LoadSceneManager.IsGameSceneLoaded)
			{
				IEnumerator coroutine = instance.customlandmarkienum(args);
				instance.StartCoroutine(coroutine);
			}
			else
			{
				Debug.LogWarning("You're not ingame");
			}
		}

		//csrun
		[ConsoleCommand(name: "spawnlandmarkcheat", docs: "Toggle the itemspawner menu.")]
		public void SpawnLandmark(string[] args)
		{
			string landmark = args[0];
			ChunkPointType cpt = ChunkPointType.None;

			switch (landmark)
			{
				case "balboa":
					cpt = ChunkPointType.Landmark_Balboa;
					break;
				default:
					cpt = ChunkPointType.None;
					break;
			}

			if (!Raft_Network.IsHost)
			{
				FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.normal, "You are not the host!", 3, HNotify.ErrorSprite);
				return;
			}
			SO_ChunkSpawnRuleAsset sO_ChunkSpawnRuleAsset = new SO_ChunkSpawnRuleAsset();


			SO_ChunkSpawnRuleAsset ruleFromPointType = ComponentManager<ChunkManager>.Value.GetRuleFromPointType(cpt);
			if (ruleFromPointType)
			{
				int value = 200;
				switch (cpt)
				{
					case ChunkPointType.Landmark_Balboa:
						value = 400;
						break;
				}

				ComponentManager<ChunkManager>.Value.AddChunkPointCheat(cpt, Raft.direction * value);
				FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.normal, "Landmark successfully spawned!", 3, HNotify.CheckSprite);
			}
			else
			{
				FindObjectOfType<HNotify>().AddNotification(HNotify.NotificationType.normal, "This island is in the game but isn't fully implemented currently!", 3, HNotify.ErrorSprite);
			}
		}



		public IEnumerator customlandmarkienum(string[] args)
		{
			Debug.Log("Loading custom landmark");

			landmarkBundle bundletoload = new landmarkBundle();

			if (args[0].IsNullOrEmpty())
			{
				Debug.LogWarning("Invalid Landmark! Check if you spelled the name correctly!");
				yield break;
			}

			foreach (landmarkBundle bundle1 in landmarkBundles)
			{
				if (bundle1.name == args[0])
				{
					bundletoload.name = bundle1.name;
					bundletoload.path = bundle1.path;
					bundletoload.bundle = bundle1.bundle;
					break;
				}
			}


			if (bundletoload.path == null)
			{
				Debug.LogWarning("Invalid Landmark! Check if you spelled the name correctly or if the file really exists!");
				yield break;
			}
			//Debug.Log("got data preparing scene load");
			//AssetBundle bundle = AssetBundle.LoadFromMemory(File.ReadAllBytes(bundletoload.path));
			AssetBundle bundle = bundletoload.bundle;

			if (bundle == null)
			{
				Debug.LogWarning("Invalid AssetBundle! The file might be broken!");
				yield break;
			}

			Debug.Log("Loading scene");

			string[] scenePath = bundle.GetAllScenePaths();
			Debug.Log("Loading landmark from scene " + scenePath[0]);
			SceneManager.LoadScene(scenePath[0], LoadSceneMode.Additive);

			var scene = SceneManager.GetSceneByName(Utils.SceneNameFromPath(scenePath[0]));

			//Debug.Log("check if scene is loaded");

			while (!scene.isLoaded)
			{
				//Debug.Log("scene not loaded, waiting");
				yield return new WaitForSeconds(.1f);
			}
			//Debug.Log("scene loaded");

			GameObject[] rootgoisland = SceneManager.GetSceneByName(Utils.SceneNameFromPath(scenePath[0])).GetRootGameObjects();


			//bundle.Unload(true);

			Vector3 spawnOffset = Raft.direction * 200;
			Debug.Log("spawn offset" + spawnOffset);
			foreach (GameObject go in rootgoisland)
			{
				//Debug.Log("go" + go.name);
			}
			//Debug.Log(bundletoload.name + "CustomLandmark");
			GameObject CustomLandmark = rootgoisland[0];
			//Debug.Log("found landmark" + CustomLandmark.name);
			try
			{
				Vector3 spawnpos = FindObjectOfType<Raft>().gameObject.transform.position + spawnOffset;
				Debug.Log(spawnpos);
				CustomLandmark.transform.position = spawnpos;
			}
			catch (NullReferenceException e)
			{
				Debug.LogWarning(e);
			}
			// Islands built from meshes (like demoisland1) have no Terrain component
			foreach (Terrain t in CustomLandmark.GetComponentsInChildren<Terrain>(true))
				t.gameObject.layer = IslandSpawner.TerrainLayer;
			//Debug.Log("Layer is on " + CustomLandmark.GetComponentInChildren<Terrain>().gameObject.layer.ToString());
			Debug.Log("Landmark spawned successfully");

			if (Raft_Network.IsHost)
			{
				Debug.Log("Sending to spawn request to other players");

				// This will send your network message to all players.
				IslandMessage islandMessage = new IslandMessage();
				islandMessage.Islandtoload = args;

				RAPI.SendNetworkMessage(islandMessage, 6969, EP2PSend.k_EP2PSendReliable);
			}

			//REAPPLY SHADERS
			CustomLandmark.AddComponent<ReApplyShaders>();
			//Debug.Log(CustomLandmark.GetComponentInChildren<Terrain>().gameObject.name + "thats my name XD");



			//spawn snowmobiles if there are any
			if (CustomLandmark.GetComponentsInChildren<SnowmobileShed>().Length > 0)
			{

				SnowmobileShed prefabClass = new SnowmobileShed();

				if (GameManager.GameMode == GameMode.Creative)
				{
					Debug.Log("We're in creative. Load temperance");

					SceneManager.LoadScene("55#Landmark_Temperance#", LoadSceneMode.Additive);

					var sceneTemperance = SceneManager.GetSceneByName("55#Landmark_Temperance#");
					if (!sceneTemperance.isLoaded)
					{
						Debug.Log("scene not loaded, waiting");
						yield return new WaitForSeconds(.1f);
					}
					prefabClass = sceneTemperance.GetRootGameObjects()[0].GetComponentsInChildren<SnowmobileShed>()[0];

					Destroy(sceneTemperance.GetRootGameObjects()[0]);
				}
				else
				{

					var sceneTemperance = SceneManager.GetSceneByName("55#Landmark_Temperance#");
					if (!sceneTemperance.isLoaded)
					{
						Debug.Log("scene not loaded, waiting");
						yield return new WaitForSeconds(.1f);
					}
					prefabClass = sceneTemperance.GetRootGameObjects()[0].GetComponentsInChildren<SnowmobileShed>()[0];
				}


				foreach (SnowmobileShed shed in CustomLandmark.GetComponentsInChildren<SnowmobileShed>())
				{
					Debug.Log(shed.gameObject.name);
					try
					{
						Debug.Log("tempfix");
						//shed.gameObject.transform.GetChild(1).transform.position = new Vector3(0, 1, 0);
						//shed.gameObject.transform.GetChild(1).transform.localPosition = new Vector3(0, 1, 0);
					}
					catch { };
					shed.snowmobilePrefab = prefabClass.snowmobilePrefab;
					if (Raft_Network.IsHost)
					{
						shed.SpawnSnowmobileNetwork();
					}
				}
			}

			//RAPI.GetLocalPlayer().transform.position = CustomLandmark.GetComponentInChildren<Transform>().position;
			//We just need the first. Keep for later if we want to load multiple
			/*foreach (string scene in scenePath)
			{
				Debug.Log("scene" + scene);
				SceneManager.LoadScene(scene, LoadSceneMode.Additive);
			}*/
		}


		#region Spawning editor islands (.island) in a world

		/// <summary>Distance in front of the raft where SpawnIsland places the island's land. Raft's camera only renders to 400 m.</summary>
		const float SpawnDistance = 250f;

		[ConsoleCommand(name: "SpawnIsland", docs: "Host, in game: spawns a saved editor island in front of the raft. Usage: SpawnIsland <name>")]
		public static void SpawnIslandCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Notify("Usage: SpawnIsland <name>   (ListIslands shows saved islands)", true); return; }
			if (!LoadSceneManager.IsGameSceneLoaded) { Notify("You need to be in a game to spawn an island", true); return; }
			if (!Raft_Network.IsHost) { Notify("Only the host can spawn islands", true); return; }

			Raft raft = FindObjectOfType<Raft>();
			Vector3 origin = raft != null ? raft.transform.position : Vector3.zero;
			Vector3 dir = Raft.direction.sqrMagnitude > 0.01f ? Raft.direction.normalized : Vector3.forward;
			Vector3 position = origin + new Vector3(dir.x, 0, dir.z).normalized * SpawnDistance;
			position.y = 0; // sea level

			instance.StartCoroutine(instance.SpawnIslandFile(string.Join(" ", args), position, true));
		}

		public IEnumerator SpawnIslandFile(string name, Vector3 position, bool broadcast)
		{
			string path = IslandSpawner.PathFor(name);
			if (!File.Exists(path)) { Notify("No saved island named '" + name + "' in " + assetpath, true); yield break; }

			IslandFile island;
			try { island = IslandFile.Load(path); }
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Could not read " + path + ": " + e);
				Notify("Could not read island '" + name + "' - see console (F10)", true);
				yield break;
			}

			yield return PlaceableCatalog.EnsureBuilt();

			try
			{
				GameObject root = IslandSpawner.SpawnInWorld(island, position);
				root.AddComponent<ReApplyShaders>();
				Notify("Spawned island '" + name + "'");
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Spawning '" + name + "' failed: " + e);
				Notify("Spawning '" + name + "' failed - see console (F10)", true);
				yield break;
			}

			if (broadcast && Raft_Network.IsHost)
			{
				IslandMessage msg = new IslandMessage();
				msg.Islandtoload = new[] { name };
				msg.Position = new[] { position.x, position.y, position.z };
				RAPI.SendNetworkMessage(msg, 6969, EP2PSend.k_EP2PSendReliable);
			}
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

	// RML publicizes Assembly-CSharp, so private members are accessed directly
	[HarmonyPatch(typeof(Snowmobile), "Start")]
	class snowmoobilenosound
	{
		static void Postfix(ref Snowmobile __instance)
		{
			if (__instance.emitter_engine == null)
			{
				Debug.Log("EMMITER ENGINE IS NULL");
			}
			if (__instance.emitter_impact == null)
			{
				Debug.Log("EMMITER impact IS NULL");
			}
		}
	}

	//SNOWMOBILES ANYWHERE!

	/*[HarmonyPatch(typeof(Snowmobile), "Update")]
	static class Patch_Snowmobile_Update
	{
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var code = instructions.ToList();
			code.Insert(code.FindLastIndex(code.FindIndex(x => x.opcode == OpCodes.Call && (x.operand as MethodInfo).Name == "Raycast"), x => x.opcode == OpCodes.Ldsfld && (x.operand as FieldInfo).Name == "MASK_Obstruction") + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_Snowmobile_Update), nameof(EditMask))));
			return code;
		}
		public static LayerMask EditMask(LayerMask original) => original | (LayerMask)1;
		/*static void Postfix(Snowmobile __instance, Transform ___groundCheckPoint, Rigidbody ___body)
		{
			var flag = Physics.Raycast(___groundCheckPoint.position, Vector3.down, out var hit, 100, (LayerMask)16) && hit.collider.transform.IsChildOf(SingletonGeneric<GameManager>.Singleton.lockedPivot);
			if (___body.transform.ParentedToRaft() != flag)
				___body.transform.SetParent(flag ? SingletonGeneric<GameManager>.Singleton.lockedPivot : null, true);
		}*/
	//}




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


	public class UnityAssetBundleRequestAwaiter : INotifyCompletion
	{
		private AssetBundleCreateRequest asyncOp;
		private Action continuation;

		public UnityAssetBundleRequestAwaiter(AssetBundleCreateRequest asyncOp)
		{
			this.asyncOp = asyncOp;
			asyncOp.completed += OnRequestCompleted;
		}

		public bool IsCompleted { get { return asyncOp.isDone; } }

		public void GetResult() { }

		public void OnCompleted(Action continuation)
		{
			this.continuation = continuation;
		}

		private void OnRequestCompleted(AsyncOperation obj)
		{
			continuation();
		}
	}


	public static class ExtensionMethods
	{

		public static UnityAssetBundleRequestAwaiter GetAwaiter(this AssetBundleCreateRequest asyncOp)
		{
			return new UnityAssetBundleRequestAwaiter(asyncOp);
		}
	}



	#endregion

}
