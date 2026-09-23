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
	/// The objects the editor can place. Raft's prefabs can't be loaded directly from its .assets,
	/// so we load one of Raft's own landmark scenes, clone the objects we want into an inactive
	/// DontDestroyOnLoad container (inactive = their scripts don't run), then unload the scene.
	/// The editor and in-world spawning both instantiate from these clones.
	///
	/// Which objects are included:
	///   - If Mods\DynamicIslands\placeables.txt exists: exactly the names listed there (one per line, # = comment).
	///   - Otherwise: every top-most object with a renderer under the source root, de-duplicated by name,
	///     and the generated list is written to placeables_generated.txt so it can be curated
	///     (copy it to placeables.txt and delete lines to remove objects).
	/// </summary>
	public static class PlaceableCatalog
	{
		const string SourceScene = "44#Landmark_Vasagatan";
		const string SourceRoot = "Boat related";

		public static string WhitelistPath { get { return Path.Combine(DynamicIslands.assetpath, "placeables.txt"); } }
		public static string GeneratedListPath { get { return Path.Combine(DynamicIslands.assetpath, "placeables_generated.txt"); } }

		static readonly Dictionary<string, GameObject> prototypes = new Dictionary<string, GameObject>();
		static GameObject container;
		static bool building;

		public static bool IsBuilt { get { return container != null && prototypes.Count > 0; } }
		public static IEnumerable<string> Names { get { return prototypes.Keys.OrderBy(n => n); } }

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
			Scene scene = SceneManager.GetSceneByName(SourceScene);
			// If the player is actually near Vasagatan the scene is already loaded; borrow it and leave it alone
			bool loadedByUs = !scene.isLoaded;
			if (loadedByUs)
			{
				AsyncOperation op = SceneManager.LoadSceneAsync(SourceScene, LoadSceneMode.Additive);
				if (op == null)
				{
					Debug.LogError("[CUSTOM ISLANDS] Could not load Raft scene " + SourceScene);
					yield break;
				}
				while (!op.isDone) yield return null;
				scene = SceneManager.GetSceneByName(SourceScene);
			}

			Transform root = FindInScene(scene, SourceRoot);
			if (root == null)
			{
				Debug.LogError("[CUSTOM ISLANDS] '" + SourceRoot + "' not found in " + SourceScene + " - Raft may have changed the scene.");
			}
			else
			{
				HashSet<string> whitelist = ReadWhitelist();
				List<Transform> picked = whitelist != null ? PickByWhitelist(root, whitelist) : PickAutomatically(root);

				container = new GameObject("CustomIslands_PlaceableCatalog");
				container.SetActive(false);
				UnityEngine.Object.DontDestroyOnLoad(container);

				int skipped = 0;
				foreach (Transform t in picked)
				{
					string name = CleanName(t.name);
					if (prototypes.ContainsKey(name)) continue;
					if (whitelist == null && excluded.IsMatch(name)) { skipped++; continue; }
					// Parent is inactive, so the clone's Awake/OnEnable don't run here
					GameObject clone = UnityEngine.Object.Instantiate(t.gameObject, container.transform);
					clone.name = name;
					clone.transform.localPosition = Vector3.zero;
					clone.transform.localRotation = t.rotation;
					clone.transform.localScale = t.lossyScale;
					prototypes.Add(name, clone);
				}

				if (whitelist == null)
				{
					try
					{
						Directory.CreateDirectory(DynamicIslands.assetpath);
						File.WriteAllLines(GeneratedListPath, new[] {
							"# Objects found automatically in " + SourceScene + "/" + SourceRoot + ".",
							"# To curate: copy this file to placeables.txt, delete lines you don't want, restart the editor.",
						}.Concat(prototypes.Keys.OrderBy(n => n)).ToArray());
					}
					catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write " + GeneratedListPath + ": " + e.Message); }
				}

				Debug.Log("[CUSTOM ISLANDS] Object catalog ready: " + prototypes.Count + " objects (" +
					(whitelist != null ? "from placeables.txt" : "auto-generated, " + skipped + " fragments/story items left out") + ")");
			}

			if (loadedByUs)
			{
				AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
				if (unload != null)
					while (!unload.isDone) yield return null;
			}
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

		static List<Transform> PickByWhitelist(Transform root, HashSet<string> names)
		{
			return root.GetComponentsInChildren<Transform>(true)
				.Where(t => t != root && (names.Contains(t.name) || names.Contains(CleanName(t.name))))
				.ToList();
		}

		// Top-most descendants that render something themselves; don't descend into them (their parts come along)
		static List<Transform> PickAutomatically(Transform root)
		{
			var result = new List<Transform>();
			var stack = new Stack<Transform>();
			foreach (Transform child in root) stack.Push(child);
			while (stack.Count > 0)
			{
				Transform t = stack.Pop();
				if (t.GetComponent<Renderer>() != null || t.GetComponent<LODGroup>() != null)
				{
					result.Add(t);
					continue;
				}
				foreach (Transform child in t) stack.Push(child);
			}
			return result;
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
		/// Objects the automatic list leaves out: model fragments ("BoatHull_low.003"), primitives, probes/effects,
		/// and story items/pickups whose scripts belong to Vasagatan's quest. A placeables.txt whitelist overrides this.
		/// </summary>
		static readonly Regex excluded = new Regex(
			@"\.\d+$|^(Plane|Cube|TextMeshPro|Particle.*|VG_EnvironmentProbeMesh.*|BoatHull.*|Window.*|Pennant_.*|Bolcutter.*|Boltcutter.*|" +
			@"Carlift_.*|QuestItemPickup_.*|Pickup_.*|NotePickup.*|.*Pickup|Bomb|DoorHandle.*|LockerDoor|Lock_Hatch|Padlock.*|Crowbar|Tools_Hammer|LOD\d+)$|^\s*$",
			RegexOptions.IgnoreCase);

		static readonly Regex displayPrefix = new Regex(@"^(VG_DecorationPrefabBase_|VG_|RT_)|\s*Variant.*$");

		/// <summary>Friendly label for the object list: "VG_DecorationPrefabBase_Sofa Variant" -> "Sofa". Saves keep the real name.</summary>
		public static string DisplayName(string name)
		{
			string s = displayPrefix.Replace(name, "").Replace('_', ' ').Trim();
			return s.Length > 0 ? s : name;
		}

		/// <summary>Catalog names ordered by their display label.</summary>
		public static IEnumerable<string> NamesByDisplayName { get { return prototypes.Keys.OrderBy(DisplayName); } }

		static readonly Regex duplicateSuffix = new Regex(@"\s*\(\d+\)$");

		/// <summary>"Crate (3)" -> "Crate"</summary>
		public static string CleanName(string name)
		{
			return duplicateSuffix.Replace(name, "");
		}
	}
}
