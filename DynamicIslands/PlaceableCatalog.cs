using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The objects the editor can place. Raft's prefabs can't be loaded directly from its .assets, so we load some
	/// of Raft's own island scenes, clone the objects we want into an inactive DontDestroyOnLoad container
	/// (inactive = their scripts don't run), then unload the scenes. The editor and in-world spawning both
	/// instantiate from these clones. While a nature island is loaded we also borrow its terrain textures.
	///
	/// Sources (in list order):
	///   Nature - Raft's ordinary islands: palms and trees, bushes, bamboo, rocks, corals, reef-hut pieces.
	///   Props  - Vasagatan (the cruise ship landmark): furniture, crates, signs, machines.
	/// If Mods\DynamicIslands\placeables.txt exists, only the names listed there are used (one per line, # = comment).
	/// The full automatic list is written to placeables_generated.txt so it can be copied and curated.
	/// </summary>
	public static class PlaceableCatalog
	{
		public const string NatureCategory = "Nature";
		public const string PropsCategory = "Props";

		class Source
		{
			public string Scene, Root, Category;
			public Regex Include; // null = everything not excluded
		}

		static readonly Regex NatureObjects = new Regex(
			@"^(Bush2?|Monstera_\d+|Banana_Bush_\d+|Bamboo_\d+|BigPalm\d+|Log|BigBoulder\d+_Low|SmallBoulder\d+|BigRock_Low\d+_Sand|" +
			@"Coral\d+|LeafCoral_\d+|TableCoral_\d+|CauliCoral|CylinderCoral_\d+|Pillar_\d+|SpineCoral_\d+|SeaVine3|seavine_tongue|" +
			@"ReefHuts_\w+|RopeFence_\w+|FL_\w+|Parabol_\d+)$");

		static readonly Source[] Sources =
		{
			new Source { Scene = "28#Landmark_Big#OG", Category = NatureCategory, Include = NatureObjects },
			new Source { Scene = "34#Landmark_Small#1", Category = NatureCategory, Include = NatureObjects },
			new Source { Scene = "44#Landmark_Vasagatan", Root = "Boat related", Category = PropsCategory },
		};

		public static string WhitelistPath { get { return Path.Combine(DynamicIslands.assetpath, "placeables.txt"); } }
		public static string GeneratedListPath { get { return Path.Combine(DynamicIslands.assetpath, "placeables_generated.txt"); } }

		static readonly Dictionary<string, GameObject> prototypes = new Dictionary<string, GameObject>();
		static readonly Dictionary<string, string> categories = new Dictionary<string, string>();
		static GameObject container;
		static bool building;

		public static bool IsBuilt { get { return container != null && prototypes.Count > 0; } }
		public static IEnumerable<string> Names { get { return prototypes.Keys.OrderBy(n => n); } }

		public static string CategoryOf(string name)
		{
			string c;
			return categories.TryGetValue(name, out c) ? c : PropsCategory;
		}

		/// <summary>Category names in list order, each with its objects sorted by display label.</summary>
		public static IEnumerable<KeyValuePair<string, List<string>>> ByCategory()
		{
			foreach (string cat in new[] { NatureCategory, PropsCategory })
			{
				List<string> names = prototypes.Keys.Where(n => CategoryOf(n) == cat).OrderBy(DisplayName).ToList();
				if (names.Count > 0) yield return new KeyValuePair<string, List<string>>(cat, names);
			}
		}

		public static GameObject Get(string name)
		{
			GameObject go;
			return prototypes.TryGetValue(name, out go) ? go : null;
		}

		/// <summary>Creates an active copy of a catalog object. Returns null if the name is unknown.</summary>
		public static GameObject Spawn(string name, Transform parent)
		{
			GameObject proto = Get(name);
			if (proto == null) return null;
			GameObject go = UnityEngine.Object.Instantiate(proto, parent);
			go.name = name;
			go.SetActive(true);
			return go;
		}

		public static IEnumerator EnsureBuilt()
		{
			while (building) yield return null;
			if (IsBuilt) yield break;

			building = true;
			try
			{
				IEnumerator build = Build();
				while (true)
				{
					object current;
					try
					{
						if (!build.MoveNext()) break;
						current = build.Current;
					}
					catch (Exception e)
					{
						Debug.LogError("[CUSTOM ISLANDS] Building the object catalog failed: " + e);
						break;
					}
					yield return current;
				}
			}
			finally
			{
				building = false;
			}
		}

		static IEnumerator Build()
		{
			HashSet<string> whitelist = ReadWhitelist();
			container = new GameObject("CustomIslands_PlaceableCatalog");
			container.SetActive(false);
			UnityEngine.Object.DontDestroyOnLoad(container);
			int skipped = 0;

			foreach (Source source in Sources)
			{
				Scene scene = SceneManager.GetSceneByName(source.Scene);
				// If the player is actually near this island the scene is already loaded; borrow it and leave it alone
				bool loadedByUs = !scene.isLoaded;
				if (loadedByUs)
				{
					AsyncOperation op = SceneManager.LoadSceneAsync(source.Scene, LoadSceneMode.Additive);
					if (op == null) { Debug.LogError("[CUSTOM ISLANDS] Could not load Raft scene " + source.Scene); continue; }
					while (!op.isDone) yield return null;
					scene = SceneManager.GetSceneByName(source.Scene);
				}

				int before = prototypes.Count;
				foreach (Transform root in Roots(scene, source))
				{
					foreach (KeyValuePair<string, Transform> pick in Pick(root))
					{
						string name = pick.Key;
						if (prototypes.ContainsKey(name)) continue;
						bool wanted = whitelist != null ? whitelist.Contains(name)
							: (source.Include == null ? !excluded.IsMatch(name) : source.Include.IsMatch(name) || IsTreeModel(pick.Value));
						if (!wanted) { skipped++; continue; }
						Add(name, pick.Value, source.Category);
					}
				}

				if (source.Category == NatureCategory && !TerrainPainter.HasRaftTextures)
					foreach (Terrain t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>(true)))
						if (TerrainPainter.UseRaftTextures(t.terrainData.terrainLayers)) break;

				Debug.Log("[CUSTOM ISLANDS] " + source.Scene + ": " + (prototypes.Count - before) + " objects");

				if (loadedByUs)
				{
					AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
					if (unload != null)
						while (!unload.isDone) yield return null;
				}
			}

			if (whitelist == null)
			{
				try
				{
					Directory.CreateDirectory(DynamicIslands.assetpath);
					var lines = new List<string> {
						"# Objects found automatically in Raft's island scenes.",
						"# To curate: copy this file to placeables.txt, delete lines you don't want, restart the editor.",
					};
					foreach (var cat in ByCategory()) { lines.Add("# --- " + cat.Key); lines.AddRange(cat.Value); }
					File.WriteAllLines(GeneratedListPath, lines.ToArray());
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write " + GeneratedListPath + ": " + e.Message); }
			}

			Debug.Log("[CUSTOM ISLANDS] Object catalog ready: " + prototypes.Count + " objects (" + string.Join(", ", ByCategory().Select(c => c.Value.Count + " " + c.Key.ToLower()).ToArray()) +
				(whitelist != null ? ", from placeables.txt" : ", " + skipped + " fragments/story items/pickups left out") + ")" +
				(TerrainPainter.HasRaftTextures ? ", using Raft's terrain textures" : ""));
		}

		static void Add(string name, Transform source, string category)
		{
			// Parent is inactive, so the clone's Awake/OnEnable don't run here
			GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, container.transform);
			clone.name = name;
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = source.rotation;
			clone.transform.localScale = source.lossyScale;
			prototypes.Add(name, clone);
			categories[name] = category;
		}

		static IEnumerable<Transform> Roots(Scene scene, Source source)
		{
			if (source.Root == null) return scene.GetRootGameObjects().Select(g => g.transform);
			Transform root = FindInScene(scene, source.Root);
			if (root == null) Debug.LogError("[CUSTOM ISLANDS] '" + source.Root + "' not found in " + source.Scene + " - Raft may have changed the scene.");
			return root != null ? new[] { root } : new Transform[0];
		}

		/// <summary>
		/// Top-most descendants that render something themselves (their parts come along), keyed by catalog name.
		/// Harvestable trees keep their visual under a child called "Model"; those are named after the tree instead.
		/// </summary>
		static IEnumerable<KeyValuePair<string, Transform>> Pick(Transform root)
		{
			var stack = new Stack<Transform>();
			foreach (Transform child in root) stack.Push(child);
			while (stack.Count > 0)
			{
				Transform t = stack.Pop();
				if (t.GetComponent<Renderer>() != null || t.GetComponent<LODGroup>() != null)
				{
					yield return new KeyValuePair<string, Transform>(IsTreeModel(t) ? TreeName(t.parent.name) : CleanName(t.name), t);
					continue;
				}
				foreach (Transform child in t) stack.Push(child);
			}
		}

		static readonly Regex treeParent = new Regex(@"^Pickup_Landmark_(Tree_(\w+) (\d+)|(\w+)Tree)$");

		static bool IsTreeModel(Transform t)
		{
			return t.parent != null && t.name.Equals("Model", StringComparison.OrdinalIgnoreCase) && treeParent.IsMatch(CleanName(t.parent.name));
		}

		/// <summary>"Pickup_Landmark_Tree_Palm 4" -> "Palm Tree 4", "Pickup_Landmark_MangoTree" -> "Mango Tree".</summary>
		static string TreeName(string parent)
		{
			Match m = treeParent.Match(CleanName(parent));
			return m.Groups[2].Success ? m.Groups[2].Value + " Tree " + m.Groups[3].Value : m.Groups[4].Value + " Tree";
		}

		static HashSet<string> ReadWhitelist()
		{
			if (!File.Exists(WhitelistPath)) return null;
			var names = new HashSet<string>();
			foreach (string line in File.ReadAllLines(WhitelistPath))
			{
				string l = line.Trim();
				if (l.Length > 0 && !l.StartsWith("#")) names.Add(l);
			}
			return names;
		}

		static Transform FindInScene(Scene scene, string name)
		{
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				if (go.name == name) return go.transform;
				Transform found = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
				if (found != null) return found;
			}
			return null;
		}

		/// <summary>
		/// Objects the automatic props list leaves out: model fragments ("BoatHull_low.003"), primitives, probes/effects,
		/// and story items/pickups whose scripts belong to the landmark's quest.
		/// </summary>
		static readonly Regex excluded = new Regex(
			@"\.\d+$|^(Plane|Cube|TextMeshPro|Particle.*|VG_EnvironmentProbeMesh.*|BoatHull.*|Window.*|Pennant_.*|Bolcutter.*|Boltcutter.*|" +
			@"Carlift_.*|QuestItemPickup_.*|Pickup_.*|NotePickup.*|.*Pickup|Bomb|DoorHandle.*|LockerDoor|Lock_Hatch|Padlock.*|Crowbar|Tools_Hammer|LOD\d+)$|^\s*$",
			RegexOptions.IgnoreCase);

		static readonly Regex displayPrefix = new Regex(@"^(VG_DecorationPrefabBase_|VG_|RT_)|\s*Variant.*$|_Low(?=\d|_|$)");
		static readonly Regex wordBreak = new Regex(@"(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Za-z])");

		/// <summary>Friendly label for the object list: "VG_DecorationPrefabBase_Sofa Variant" -> "Sofa", "BigBoulder1_Low" -> "Big Boulder 1". Saves keep the real name.</summary>
		public static string DisplayName(string name)
		{
			string s = displayPrefix.Replace(name, "").Replace('_', ' ').Trim();
			s = wordBreak.Replace(s, " ");
			s = Regex.Replace(s, @"\s+", " ").Trim();
			if (s.Length > 0) s = char.ToUpper(s[0]) + s.Substring(1);
			return s.Length > 0 ? s : name;
		}

		static readonly Regex duplicateSuffix = new Regex(@"\s*\(\d+\)$");
		static readonly Regex cloneSuffix = new Regex(@"\s*\(Clone\)");

		/// <summary>"Crate (3)" -> "Crate", "BigPalm3(Clone)" -> "BigPalm3"</summary>
		public static string CleanName(string name)
		{
			return duplicateSuffix.Replace(cloneSuffix.Replace(name, ""), "").Trim();
		}
	}
}
