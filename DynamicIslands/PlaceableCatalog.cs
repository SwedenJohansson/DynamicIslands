using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
	/// Two kinds of objects:
	///   Core (always loaded, EnsureBuilt): the curated nature, snow, desert, forest, underwater and harvestable
	///   objects the generator uses, Vasagatan's props, and every buildable item of Raft (blocks, furniture...).
	///   Everything else: every object of every one of Raft's island scenes (abandoned rafts, the radio tower,
	///   Tangaroa, Utopia...). Loading all of them takes minutes and a lot of memory, so the editor scans them
	///   once into catalog_index.txt (object name -> scene) and a scene is only loaded when one of its objects is
	///   wanted: when its category is opened in the editor, or when an island that uses it is loaded or spawned.
	///
	/// If Mods\DynamicIslands\placeables.txt exists, only the names listed there are used (one per line, # = comment).
	/// The full automatic core list is written to placeables_generated.txt so it can be copied and curated.
	/// </summary>
	public static class PlaceableCatalog
	{
		public const string NatureCategory = "Nature";
		public const string SnowCategory = "Snow";
		public const string DesertCategory = "Desert";
		public const string ForestCategory = "Forest";
		public const string UnderwaterCategory = "Underwater";
		public const string RaftBlocksCategory = "Raft blocks";
		public const string BuildablesCategory = "Raft buildables";
		public const string PropsCategory = "Props";
		public const string HarvestableCategory = "Harvestable";

		/// <summary>Order of the core categories in the editor's object list.</summary>
		static readonly string[] CategoryOrder = new[] { GroupLibrary.Category, NatureCategory, SnowCategory, DesertCategory, ForestCategory, UnderwaterCategory, HarvestableCategory }
			.Concat(ContentCatalog.Categories).Concat(new[] { RaftBlocksCategory, BuildablesCategory, PropsCategory }).ToArray();

		/// <summary>Categories of the on-demand island scenes, by scene name, in list order (the first match wins).</summary>
		static readonly KeyValuePair<Regex, string>[] SceneCategories =
		{
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_(Big|Small)\b"), "Islands"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Raft"), "Abandoned rafts"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Radar"), "Radio tower"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Pilot"), "Pilot island"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Boat"), "Boat"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Vasagatan"), "Vasagatan"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Balboa"), "Balboa"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Caravan"), "Caravan Town"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Tangaroa"), "Tangaroa"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Varuna"), "Varuna Point"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Temperance"), "Temperance"),
			new KeyValuePair<Regex, string>(new Regex(@"Landmark_Utopia"), "Utopia"),
		};
		const string OtherLandmarksCategory = "Other places";

		class Source
		{
			public string Scene, Root, Category;
			public Regex Include; // null = everything not excluded
			/// <summary>Also take this island's harvestable objects (trees, rocks, ores...), with their gameplay scripts.</summary>
			public bool Harvest;
			/// <summary>Optional second group from the same island, in another category (e.g. its underwater props).</summary>
			public Regex Include2;
			public string Category2;
		}

		/// <summary>Sunken props around Raft's islands: barrels, containers, buoys, sea vines (roadmap 1.6 "enhancing the ocean floor").</summary>
		static readonly Regex UnderwaterObjects = new Regex(@"^(Reef_Barrel\d+|Reef_Container|Reef_Buoy|SeaVine3_klump)$");

		static readonly Regex SnowObjects = new Regex(
			@"^(TP_PineTreeSnowy|TP_BigRock0\d|TP_SmallRock0\d|TP_SnowDrift0\d|TP_Icicles0\d|TP_StalagmiteCluster0\d_Snow|TP_IceShore_Small\d|" +
			@"TP_Moontown_Barrel0\d|TP_Moontown_TarpCrate0\d|TP_Moontown_SealedCrate0\d)$");
		static readonly Regex DesertObjects = new Regex(
			@"^(Cactus\w+|DesertFern_\d+|SmallBush_\d+|SmallBushyTree_\d+|BigBush_\d+|BigSharpRock_\d+|CaravanIsland_SmallRock_\d+|" +
			@"CaravanIsland_(Yellow|Green|Brown)Grass)$");
		static readonly Regex ForestObjects = new Regex(
			@"^(Balboa_(Big)?Bush_\d+ Variant|BirchTree_\w+|PineTree_\w+|TreeLog_\d+|Tree_Stump|SmallRock_\d+|BigRock_\d+)$");

		static readonly Regex NatureObjects = new Regex(
			@"^(Bush2?|Monstera_\d+|Banana_Bush_\d+|Bamboo_\d+|BigPalm\d+|Log|BigBoulder\d+_Low|SmallBoulder\d+|BigRock_Low\d+_Sand|" +
			@"Coral\d+|LeafCoral_\d+|TableCoral_\d+|CauliCoral|CylinderCoral_\d+|Pillar_\d+|SpineCoral_\d+|SeaVine3|seavine_tongue|" +
			@"ReefHuts_\w+|RopeFence_\w+|FL_\w+|Parabol_\d+)$");

		static readonly Source[] Sources =
		{
			new Source { Scene = "28#Landmark_Big#OG", Category = NatureCategory, Include = NatureObjects, Harvest = true },
			new Source { Scene = "34#Landmark_Small#1", Category = NatureCategory, Include = NatureObjects, Harvest = true },
			// Island styles (roadmap 1.6): snowy Temperance, desert Caravan Island, forest Balboa (objects and ground textures)
			new Source { Scene = "57#Landmark_TemperanceSmall#1", Category = SnowCategory, Include = SnowObjects, Harvest = true },
			new Source { Scene = "51#Landmark_CaravanSmall#1", Category = DesertCategory, Include = DesertObjects, Harvest = true, Include2 = UnderwaterObjects, Category2 = UnderwaterCategory },
			new Source { Scene = "46#Landmark_BalboaSmall#1", Category = ForestCategory, Include = ForestObjects, Harvest = true, Include2 = UnderwaterObjects, Category2 = UnderwaterCategory },
			new Source { Scene = "44#Landmark_Vasagatan", Root = "Boat related", Category = PropsCategory },
		};

		public static string WhitelistPath { get { return Path.Combine(DynamicIslands.assetpath, "placeables.txt"); } }
		public static string GeneratedListPath { get { return Path.Combine(DynamicIslands.assetpath, "placeables_generated.txt"); } }
		public static string IndexPath { get { return Path.Combine(DynamicIslands.assetpath, IndexFileName); } }
		public const string IndexFileName = "catalog_index.txt";

		static readonly Dictionary<string, GameObject> prototypes = new Dictionary<string, GameObject>();
		static readonly Dictionary<string, string> categories = new Dictionary<string, string>();
		/// <summary>Harvestable Raft objects kept with their gameplay scripts (the same objects as in prototypes).</summary>
		static readonly Dictionary<string, GameObject> harvestables = new Dictionary<string, GameObject>();
		static readonly Regex HarvestableObjects = new Regex(@"^Pickup_Landmark_(Tree_Palm \d+|Tree_Pine|Tree_Birch|MangoTree|Rock \d+|BerryBush|Clay \d+|Sand|Sand_Caravan|Copper \d+|Iron \d+|PineappleLandmark|Scrap \d+_OceanBottom|GiantClam|SilverAlgae)$");
		/// <summary>Labels for the list, where Raft has a real name (buildable items: "Simple Grill").</summary>
		static readonly Dictionary<string, string> labels = new Dictionary<string, string>();
		/// <summary>Names of the core objects (what EnsureBuilt loads; the island generator only uses these).</summary>
		static readonly HashSet<string> core = new HashSet<string>();

		static readonly Dictionary<string, float> sizes = new Dictionary<string, float>();

		/// <summary>Raised on the main thread when objects were added to the catalog or the index changed.</summary>
		public static event Action Changed;
		static void RaiseChanged() { if (Changed != null) try { Changed(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Catalog listener: " + e); } }

		/// <summary>
		/// Largest dimension (m) of a catalog object at scale 1, from its meshes' bounds (works on the inactive
		/// prototypes, whose renderer bounds are empty). 0 if it has no meshes.
		/// </summary>
		public static float ApproxSize(string name)
		{
			float size;
			if (sizes.TryGetValue(name, out size)) return size;
			Bounds b;
			size = LocalBounds(name, out b) ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 0f;
			sizes[name] = size;
			return size;
		}

		/// <summary>Bounds of a catalog object's meshes relative to its root at scale 1 (false if it has no meshes).</summary>
		public static bool LocalBounds(string name, out Bounds b)
		{
			b = new Bounds();
			GameObject proto = Get(name);
			if (proto == null) return false;
			Matrix4x4 toRoot = proto.transform.worldToLocalMatrix;
			bool any = false;
			foreach (MeshFilter mf in proto.GetComponentsInChildren<MeshFilter>(true))
			{
				if (mf.sharedMesh == null) continue;
				Encapsulate(ref b, ref any, toRoot * mf.transform.localToWorldMatrix, mf.sharedMesh.bounds);
			}
			foreach (SkinnedMeshRenderer sm in proto.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (sm.sharedMesh == null) continue;
				Encapsulate(ref b, ref any, toRoot * sm.transform.localToWorldMatrix, sm.sharedMesh.bounds);
			}
			return any;
		}

		static void Encapsulate(ref Bounds b, ref bool any, Matrix4x4 m, Bounds mb)
		{
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
				Vector3 p = m.MultiplyPoint3x4(corner);
				if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
			}
		}

		public static GameObject SpawnHarvestable(string name, Transform parent)
		{
			GameObject proto;
			if (!harvestables.TryGetValue(name, out proto)) return null;
			GameObject go = UnityEngine.Object.Instantiate(proto, parent);
			go.name = name;
			go.SetActive(true);
			return go;
		}

		public static IEnumerable<string> HarvestableNames { get { return harvestables.Keys; } }
		static GameObject container;
		static bool building;

		public static bool IsBuilt { get { return !building && container != null && prototypes.Count > 0; } }
		/// <summary>All loaded objects (core plus on-demand ones loaded so far).</summary>
		public static IEnumerable<string> Names { get { return prototypes.Keys.OrderBy(n => n); } }
		/// <summary>The core objects only: always the same set, whatever else was loaded (the generator relies on this).</summary>
		public static IEnumerable<string> CoreNames { get { return prototypes.Keys.Where(n => core.Contains(n)).OrderBy(n => n); } }

		public static string CategoryOf(string name)
		{
			string c;
			if (categories.TryGetValue(name, out c)) return c;
			IndexEntry e;
			return index.TryGetValue(name, out e) ? e.Category : PropsCategory;
		}

		/// <summary>Objects that can be spawned but aren't offered in the editor's list (see clutter).</summary>
		static readonly HashSet<string> hidden = new HashSet<string>();

		/// <summary>Loaded category names in list order, each with its objects (as offered in the editor) sorted by display label.</summary>
		public static IEnumerable<KeyValuePair<string, List<string>>> ByCategory()
		{
			foreach (string cat in CategoryOrder.Concat(ExtraCategoryOrder()))
			{
				List<string> names = prototypes.Keys.Where(n => CategoryOf(n) == cat && !hidden.Contains(n)).OrderBy(DisplayName).ToList();
				if (names.Count > 0) yield return new KeyValuePair<string, List<string>>(cat, names);
			}
		}

		static IEnumerable<string> ExtraCategoryOrder()
		{
			return SceneCategories.Select(p => p.Value).Distinct().Concat(new[] { OtherLandmarksCategory });
		}

		/// <summary>One object as the editor's object browser shows it.</summary>
		public class Entry
		{
			public string Name, Label, Category;
			/// <summary>Raft scene the object comes from when it is loaded on demand; null for core objects.</summary>
			public string Scene;
			public bool Loaded { get { return prototypes.ContainsKey(Name); } }
		}

		/// <summary>
		/// Everything the editor can offer, loaded or not, by category in list order: the core categories first, then
		/// one per group of Raft places (from the index).
		/// </summary>
		public static List<KeyValuePair<string, List<Entry>>> Browse()
		{
			var byCat = new Dictionary<string, List<Entry>>();
			Action<string, string, string> add = (name, cat, scene) =>
			{
				List<Entry> list;
				if (!byCat.TryGetValue(cat, out list)) byCat[cat] = list = new List<Entry>();
				list.Add(new Entry { Name = name, Label = DisplayName(name), Category = cat, Scene = scene });
			};
			foreach (var p in prototypes)
				if (!hidden.Contains(p.Key)) add(p.Key, CategoryOf(p.Key), core.Contains(p.Key) ? null : SceneOf(p.Key));
			HashSet<string> whitelist = cachedWhitelist;
			foreach (IndexEntry e in index.Values)
				if (!prototypes.ContainsKey(e.Name) && !e.Hidden && (whitelist == null || whitelist.Contains(e.Name))) add(e.Name, e.Category, e.Scene);
			var result = new List<KeyValuePair<string, List<Entry>>>();
			foreach (string cat in CategoryOrder.Concat(ExtraCategoryOrder()).Concat(byCat.Keys.OrderBy(k => k)).Distinct())
			{
				List<Entry> list;
				if (byCat.TryGetValue(cat, out list) && list.Count > 0)
					result.Add(new KeyValuePair<string, List<Entry>>(cat, list.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList()));
			}
			return result;
		}

		public static GameObject Get(string name)
		{
			GameObject go;
			return prototypes.TryGetValue(name, out go) ? go : null;
		}

		public static bool IsLoaded(string name) { return prototypes.ContainsKey(name); }
		public static bool IsHarvestable(string name) { return harvestables.ContainsKey(name); }

		/// <summary>
		/// Creates an active copy of a catalog object. Returns null if the name is unknown or not loaded yet.
		/// Harvestable objects keep their gameplay scripts only when <paramref name="withGameplay"/> is set (islands in a
		/// world); the editor gets a visual copy, because those scripts expect a running game world.
		/// </summary>
		public static GameObject Spawn(string name, Transform parent, bool withGameplay = false)
		{
			GameObject proto = Get(name);
			if (proto == null) return null;
			GameObject go;
			if (harvestables.ContainsKey(name) && !withGameplay)
			{
				// Instantiate under the inactive container so no script wakes up, strip the scripts, then move it out
				go = UnityEngine.Object.Instantiate(proto, container.transform);
				StripScripts(go);
				go.transform.SetParent(parent, false);
			}
			else go = UnityEngine.Object.Instantiate(proto, parent);
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
				yield return Guarded(Build(), "Building the object catalog");
			}
			finally
			{
				building = false;
			}
		}

		/// <summary>Runs a coroutine, logging (not throwing) an exception from it.</summary>
		static IEnumerator Guarded(IEnumerator routine, string what)
		{
			while (true)
			{
				object current;
				try
				{
					if (!routine.MoveNext()) yield break;
					current = routine.Current;
				}
				catch (Exception e)
				{
					Debug.LogError("[CUSTOM ISLANDS] " + what + " failed: " + e);
					yield break;
				}
				yield return current;
			}
		}

		static HashSet<string> cachedWhitelist;

		static IEnumerator Build()
		{
			HashSet<string> whitelist = cachedWhitelist = ReadWhitelist();
			container = new GameObject("CustomIslands_PlaceableCatalog");
			container.SetActive(false);
			UnityEngine.Object.DontDestroyOnLoad(container);
			int skipped = 0;

			foreach (Source source in Sources)
			{
				var opened = new OpenedScene();
				yield return OpenScene(source.Scene, opened);
				if (!opened.Scene.IsValid()) continue;
				Scene scene = opened.Scene;

				int before = prototypes.Count;
				foreach (Transform root in Roots(scene, source))
				{
					foreach (KeyValuePair<string, Transform> pick in Pick(root))
					{
						string name = pick.Key;
						if (prototypes.ContainsKey(name)) continue;
						bool second = source.Include2 != null && source.Include2.IsMatch(name);
						bool wanted = whitelist != null ? whitelist.Contains(name)
							: second || (source.Include == null ? !excluded.IsMatch(name) : source.Include.IsMatch(name)); // trees come in as harvestables
						if (!wanted) { skipped++; continue; }
						Add(name, pick.Value, second ? source.Category2 : source.Category, false);
						core.Add(name);
						// Clutter stays spawnable (islands saved with it still work) but is left out of the editor's list
						if (whitelist == null && clutter.IsMatch(name)) hidden.Add(name);
					}
				}

				if (source.Harvest)
					foreach (Transform t in AllTransforms(scene))
					{
						string hn = CleanName(t.name);
						if (!HarvestableObjects.IsMatch(hn) || harvestables.ContainsKey(hn)) continue;
						if (whitelist != null && !whitelist.Contains(hn)) continue;
						AddHarvestable(hn, t);
						core.Add(hn);
					}

				// Ground textures (and footstep sounds) for the island styles
				foreach (Terrain t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>(true)))
					if (t.terrainData != null) TerrainPainter.UseRaftTextures(t.terrainData.terrainLayers, t.GetComponent<TerrainIdentifier>());

				Debug.Log("[CUSTOM ISLANDS] " + source.Scene + ": " + (prototypes.Count - before) + " objects");
				yield return CloseScene(opened);
			}

			AddRaftBuildables(whitelist);

			if (whitelist == null)
			{
				try
				{
					Directory.CreateDirectory(DynamicIslands.assetpath);
					var lines = new List<string> {
						"# Objects found automatically in Raft's island scenes (the core list; the index of all other objects is catalog_index.txt).",
						"# To curate: copy this file to placeables.txt, delete lines you don't want, restart the editor.",
						"# Small indoor clutter is left out of the editor's list; it is listed at the end, commented out.",
					};
					foreach (var cat in ByCategory()) { lines.Add("# --- " + cat.Key); lines.AddRange(cat.Value); }
					lines.Add("# --- Left out (remove the # to use one in placeables.txt)");
					lines.AddRange(hidden.OrderBy(n => n).Select(n => "#" + n));
					File.WriteAllLines(GeneratedListPath, lines.ToArray());
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write " + GeneratedListPath + ": " + e.Message); }
			}

			ReadIndex();

			try { ContentCatalog.Register(); }
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Could not add creatures and notes: " + e); }

			Debug.Log("[CUSTOM ISLANDS] Object catalog ready: " + prototypes.Count + " objects (" + string.Join(", ", ByCategory().Select(c => c.Value.Count + " " + c.Key.ToLower()).ToArray()) +
				(whitelist != null ? ", from placeables.txt" : ", " + skipped + " fragments/story items/pickups left out") + ")" +
				(TerrainPainter.HasRaftTextures ? ", using Raft's terrain textures" : "") +
				"; " + index.Count + " more objects of Raft's islands load when needed" + (IndexIsCurrent ? "" : " (the object index needs a scan: open the editor)"));
			RaiseChanged();
		}

		static void Add(string name, Transform source, string category, bool stripScripts)
		{
			// Parent is inactive, so the clone's Awake/OnEnable don't run here
			GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, container.transform);
			clone.name = name;
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localRotation = source.rotation;
			clone.transform.localScale = source.lossyScale;
			// Island decoration from story places carries quest, AI and trigger scripts that expect their own island
			if (stripScripts) StripScripts(clone);
			KeepVisibleFarAway(clone);
			prototypes.Add(name, clone);
			categories[name] = category;
		}

		/// <summary>The inactive container the catalog's prototypes live in.</summary>
		internal static GameObject Container { get { return container; } }

		/// <summary>
		/// Adds (or replaces) an object made by the mod itself: a creature marker, or a readable note that shows one of
		/// Raft's objects under its own name. Not a core object (the generator never places it).
		/// </summary>
		internal static void AddCustom(string name, GameObject prototype, string category, string label)
		{
			prototypes[name] = prototype;
			categories[name] = category;
			labels[name] = label;
			sizes.Remove(name);
		}

		/// <summary>Takes a custom object out of the catalog (a deleted group).</summary>
		internal static void RemoveCustom(string name)
		{
			GameObject proto;
			if (prototypes.TryGetValue(name, out proto) && proto != null && proto.transform.parent == container.transform) UnityEngine.Object.Destroy(proto);
			prototypes.Remove(name);
			categories.Remove(name);
			labels.Remove(name);
			RaiseChanged();
		}

		/// <summary>Tells the object browser the list changed (a group was saved).</summary>
		internal static void NotifyChanged() { RaiseChanged(); }

		static void AddHarvestable(string name, Transform t)
		{
			GameObject clone = UnityEngine.Object.Instantiate(t.gameObject, container.transform);
			clone.name = name;
			clone.transform.localPosition = Vector3.zero;
			harvestables.Add(name, clone);
			prototypes[name] = clone;
			categories[name] = HarvestableCategory;
		}

		internal static void StripScripts(GameObject go)
		{
			// Some scripts need others ([RequireComponent]), and Unity refuses (with a warning) to remove a needed one
			// first: on each object, remove the scripts nothing else needs, round by round
			foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
			{
				List<MonoBehaviour> left = t.GetComponents<MonoBehaviour>().Where(m => m != null).ToList();
				for (int round = 0; left.Count > 0 && round < 8; round++)
				{
					List<MonoBehaviour> free = left.Where(m => !left.Any(o => o != m && Requires(o.GetType(), m.GetType()))).ToList();
					foreach (MonoBehaviour m in free.Count > 0 ? free : left)
						try { UnityEngine.Object.DestroyImmediate(m); } catch { }
					left = left.Where(m => m != null).ToList();
				}
			}
		}

		static readonly Dictionary<Type, Type[]> requirements = new Dictionary<Type, Type[]>();

		/// <summary>Whether a script of type <paramref name="owner"/> needs a component of type <paramref name="needed"/> on its object.</summary>
		static bool Requires(Type owner, Type needed)
		{
			Type[] req;
			if (!requirements.TryGetValue(owner, out req))
			{
				req = owner.GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
					.SelectMany(r => new[] { r.m_Type0, r.m_Type1, r.m_Type2 }).Where(x => x != null).ToArray();
				requirements[owner] = req;
			}
			foreach (Type r in req) if (r.IsAssignableFrom(needed)) return true;
			return false;
		}

		/// <summary>
		/// Every buildable item of Raft as decoration: its building blocks (foundations, floors, walls, pillars,
		/// stairs, roofs...) so islands can have huts and players can build their own abandoned rafts (Discord ideas),
		/// and everything else players place on a raft (storage, beds, grills, lights, decorations, sails...).
		/// Taken from the block prefabs of Raft's buildable items; their scripts are removed (Raft's block logic
		/// expects to be on the player's raft), the models and colliders stay so they can be walked on.
		/// </summary>
		static void AddRaftBuildables(HashSet<string> whitelist)
		{
			int before = prototypes.Count;
			try
			{
				foreach (Item_Base item in ItemManager.GetAllItems())
				{
					if (item == null || string.IsNullOrEmpty(item.UniqueName) || item.settings_buildable == null) continue;
					if (prototypes.ContainsKey(item.UniqueName) || (whitelist != null && !whitelist.Contains(item.UniqueName))) continue;
					Block[] blocks;
					try { blocks = item.settings_buildable.GetBlockPrefabs(); } catch { continue; }
					Block prefab = blocks != null ? blocks.FirstOrDefault(b => b != null) : null;
					if (prefab == null || prefab.GetComponentInChildren<Renderer>(true) == null) continue;

					GameObject clone = UnityEngine.Object.Instantiate(prefab.gameObject, container.transform);
					clone.name = item.UniqueName;
					clone.transform.localPosition = Vector3.zero;
					clone.transform.localRotation = Quaternion.identity;
					StripScripts(clone);
					// Raft's "Block" layer is only walkable as part of the player's raft; as island scenery they go on the
					// terrain's layer, so players walk on them, the editor can stack them, and a raft stops against them
					foreach (Transform t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IslandSpawner.TerrainLayer;
					KeepVisibleFarAway(clone);
					prototypes.Add(item.UniqueName, clone);
					core.Add(item.UniqueName);
					bool block = item.UniqueName.StartsWith("Block_");
					categories[item.UniqueName] = block ? RaftBlocksCategory : BuildablesCategory;
					try
					{
						string display = item.settings_Inventory != null ? item.settings_Inventory.DisplayName : null;
						if (!block && !string.IsNullOrEmpty(display) && !display.StartsWith("#")) labels[item.UniqueName] = display.Trim();
					}
					catch { }
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not add Raft's buildable items: " + e.Message); }
			Debug.Log("[CUSTOM ISLANDS] Raft buildable items: " + (prototypes.Count - before) + " objects (" +
				prototypes.Keys.Count(n => CategoryOf(n) == RaftBlocksCategory) + " building blocks, " + prototypes.Keys.Count(n => CategoryOf(n) == BuildablesCategory) + " other buildables)");
		}

		/// <summary>
		/// Some of Raft's objects (e.g. Balboa's trees) have LOD groups that cull them once they are a small part of the
		/// screen, tuned for walking around their own island; in the editor's overview and when sailing up to an island
		/// they would be missing. Keep the last level visible down to a much smaller size.
		/// </summary>
		static void KeepVisibleFarAway(GameObject clone)
		{
			foreach (LODGroup group in clone.GetComponentsInChildren<LODGroup>(true))
			{
				LOD[] lods = group.GetLODs();
				if (lods.Length == 0 || lods[lods.Length - 1].screenRelativeTransitionHeight <= 0.01f) continue;
				lods[lods.Length - 1].screenRelativeTransitionHeight = 0.005f;
				for (int i = lods.Length - 2; i >= 0; i--) // keep the heights strictly decreasing
					if (lods[i].screenRelativeTransitionHeight <= lods[i + 1].screenRelativeTransitionHeight)
						lods[i].screenRelativeTransitionHeight = lods[i + 1].screenRelativeTransitionHeight + 0.001f;
				group.SetLODs(lods);
			}
		}

		#region Loading Raft's scenes

		class OpenedScene
		{
			public Scene Scene;
			/// <summary>We loaded it (and unload it again); false = Raft had it loaded already (the player is near it).</summary>
			public bool Ours;
		}

		static IEnumerator OpenScene(string sceneName, OpenedScene result, bool hide = false)
		{
			Scene scene = SceneManager.GetSceneByName(sceneName);
			// If the player is actually near this island the scene is already loaded; borrow it and leave it alone.
			// Raft's scene loader may be busy with it (loaded but still empty), so then load our own copy.
			if (scene.isLoaded && scene.rootCount > 0) { result.Scene = scene; result.Ours = false; yield break; }

			// Remember the copies that exist already, so we find (and later unload) exactly the one we load,
			// never Raft's own copy of the island
			var existing = new HashSet<int>();
			for (int i = 0; i < SceneManager.sceneCount; i++)
				if (SceneManager.GetSceneAt(i).name == sceneName) existing.Add(SceneManager.GetSceneAt(i).handle);
			// Scenes only read for their objects are switched off the moment they arrive (in sceneLoaded, before any
			// Start or Update): story islands carry quest, AI, music and network scripts that must not run here
			UnityEngine.Events.UnityAction<Scene, LoadSceneMode> switchOff = (s, mode) =>
			{
				if (s.name != sceneName || existing.Contains(s.handle)) return;
				foreach (GameObject g in s.GetRootGameObjects()) g.SetActive(false);
			};
			if (hide) SceneManager.sceneLoaded += switchOff;
			AsyncOperation op;
			try { op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive); }
			catch (Exception e) { SceneManager.sceneLoaded -= switchOff; Debug.LogError("[CUSTOM ISLANDS] Could not load Raft scene " + sceneName + ": " + e.Message); yield break; }
			if (op == null) { SceneManager.sceneLoaded -= switchOff; Debug.LogError("[CUSTOM ISLANDS] Could not load Raft scene " + sceneName); yield break; }
			try { while (!op.isDone) yield return null; }
			finally { SceneManager.sceneLoaded -= switchOff; }
			for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
			{
				Scene s = SceneManager.GetSceneAt(i);
				if (s.name == sceneName && !existing.Contains(s.handle)) { result.Scene = s; result.Ours = true; break; }
			}
			if (!result.Scene.IsValid()) { Debug.LogError("[CUSTOM ISLANDS] Loaded Raft scene " + sceneName + " but could not find it"); yield break; }
			// Scenes only read for their objects are switched off at once, so they don't show up in the editor or the world
			if (hide) foreach (GameObject g in result.Scene.GetRootGameObjects()) g.SetActive(false);
		}

		static IEnumerator CloseScene(OpenedScene opened)
		{
			if (!opened.Ours || !opened.Scene.IsValid()) yield break;
			AsyncOperation unload = null;
			try { unload = SceneManager.UnloadSceneAsync(opened.Scene); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not unload " + opened.Scene.name + ": " + e.Message); }
			if (unload != null)
				while (!unload.isDone) yield return null;
		}

		static IEnumerable<Transform> AllTransforms(Scene scene)
		{
			return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));
		}

		/// <summary>
		/// Loads one of Raft's island scenes switched off (none of its scripts run), lets use() read it while it is
		/// loaded, then unloads it again (the dev command that measures Raft's islands).
		/// </summary>
		internal static IEnumerator VisitScene(string sceneName, Func<Scene, IEnumerator> use)
		{
			var opened = new OpenedScene();
			yield return OpenScene(sceneName, opened, true);
			if (!opened.Scene.IsValid()) yield break;
			yield return Guarded(use(opened.Scene), "Reading " + sceneName);
			yield return CloseScene(opened);
		}

		/// <summary>All of Raft's island scenes, in build order.</summary>
		internal static List<string> LandmarkSceneNames() { return LandmarkScenes(); }

		/// <summary>The placeable objects of a scene as the object list sees them (top-most things that render a mesh; no pickups).</summary>
		internal static IEnumerable<KeyValuePair<string, Transform>> PlaceablesOf(Scene scene) { return PickAll(scene); }

		/// <summary>Raft's harvestable things: trees, rocks, ores, clay, sand, scrap, fruit bushes ("Pickup_Landmark_...").</summary>
		internal static bool IsHarvestableName(string name) { return HarvestableObjects.IsMatch(name); }

		#endregion

		#region On-demand objects: the index of all of Raft's island scenes

		class IndexEntry
		{
			public string Name, Scene, Category;
			public bool Harvestable, Hidden;
		}

		const int IndexVersion = 2;
		static readonly Dictionary<string, IndexEntry> index = new Dictionary<string, IndexEntry>();
		/// <summary>Scenes whose on-demand objects are loaded.</summary>
		static readonly HashSet<string> loadedScenes = new HashSet<string>();
		static readonly HashSet<string> loadingScenes = new HashSet<string>();
		static string indexRaftVersion;
		static int indexFileVersion;

		/// <summary>The index matches this Raft version (else the editor scans Raft's islands again).</summary>
		public static bool IndexIsCurrent { get { return index.Count > 0 && indexFileVersion == IndexVersion && indexRaftVersion == Application.version; } }
		public static bool Indexing { get; private set; }
		/// <summary>What a running scan is doing, for the editor's status line ("Scanning Utopia (34/40)").</summary>
		public static string IndexProgress { get; private set; }
		/// <summary>Scenes currently being loaded for their objects (shown by the editor).</summary>
		public static IEnumerable<string> LoadingScenes { get { return loadingScenes; } }

		public static string SceneOf(string name)
		{
			IndexEntry e;
			return index.TryGetValue(name, out e) ? e.Scene : null;
		}

		/// <summary>"44#Landmark_Vasagatan" -> "Vasagatan", "36#Landmark_Small#3" -> "Small 3".</summary>
		public static string SceneLabel(string scene)
		{
			if (string.IsNullOrEmpty(scene)) return "";
			string s = Regex.Replace(scene, @"^\d+#Landmark_", "");
			s = s.Replace('#', ' ').Trim();
			return wordBreak.Replace(s, " ");
		}

		static string CategoryForScene(string scene)
		{
			foreach (var p in SceneCategories) if (p.Key.IsMatch(scene)) return p.Value;
			return OtherLandmarksCategory;
		}

		/// <summary>All of Raft's island scenes (their names contain "Landmark_"), in build order.</summary>
		static List<string> LandmarkScenes()
		{
			var result = new List<string>();
			for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
			{
				string name = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
				if (name.Contains("Landmark_") && !result.Contains(name)) result.Add(name);
			}
			return result;
		}

		static void ReadIndex()
		{
			index.Clear();
			string text = null;
			try { if (File.Exists(IndexPath)) text = File.ReadAllText(IndexPath); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + IndexPath + ": " + e.Message); }
			if (text == null)
			{
				// The mod ships an index made with the current Raft, so islands can be spawned before the editor was opened
				try
				{
					byte[] shipped = DynamicIslands.instance != null ? DynamicIslands.instance.GetEmbeddedFileBytes(IndexFileName) : null;
					if (shipped != null && shipped.Length > 0) text = Encoding.UTF8.GetString(shipped);
				}
				catch { }
			}
			if (text == null) return;
			indexFileVersion = 0; indexRaftVersion = null;
			foreach (string raw in text.Split('\n'))
			{
				string line = raw.TrimEnd('\r');
				if (line.StartsWith("#"))
				{
					Match m = Regex.Match(line, @"^# CustomIslands object index (\d+) raft=(.*)$");
					if (m.Success) { indexFileVersion = int.Parse(m.Groups[1].Value); indexRaftVersion = m.Groups[2].Value.Trim(); }
					continue;
				}
				string[] f = line.Split('\t');
				if (f.Length < 3 || prototypes.ContainsKey(f[0]) && core.Contains(f[0])) continue;
				index[f[0]] = new IndexEntry { Name = f[0], Scene = f[1], Category = f[2], Harvestable = f.Length > 3 && f[3].Contains("h"), Hidden = f.Length > 3 && f[3].Contains("x") };
			}
		}

		static void WriteIndex()
		{
			var sb = new StringBuilder();
			sb.Append("# CustomIslands object index ").Append(IndexVersion).Append(" raft=").Append(Application.version).Append('\n');
			sb.Append("# Every placeable object of Raft's island scenes that isn't in the core list: name, scene, category, flags (h = harvestable, x = left out of the list).\n");
			sb.Append("# Made by the editor; delete this file to scan Raft's islands again.\n");
			foreach (IndexEntry e in index.Values.OrderBy(e => e.Scene).ThenBy(e => e.Name))
				sb.Append(e.Name).Append('\t').Append(e.Scene).Append('\t').Append(e.Category).Append('\t').Append(e.Harvestable ? "h" : "").Append(e.Hidden ? "x" : "").Append('\n');
			try
			{
				Directory.CreateDirectory(DynamicIslands.assetpath);
				File.WriteAllText(IndexPath, sb.ToString());
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write " + IndexPath + ": " + e.Message); }
			indexFileVersion = IndexVersion; indexRaftVersion = Application.version;
		}

		/// <summary>
		/// Editor: makes sure the index of all of Raft's island objects is current, scanning every island scene once
		/// (a minute or two, in the background; the result is saved). Call after EnsureBuilt.
		/// </summary>
		public static IEnumerator EnsureIndex()
		{
			while (Indexing) yield return null;
			if (IndexIsCurrent || !IsBuilt) yield break;
			Indexing = true;
			try { yield return Guarded(ScanAll(), "Scanning Raft's islands for objects"); }
			finally { Indexing = false; IndexProgress = null; RaiseChanged(); }
		}

		static IEnumerator ScanAll()
		{
			float started = Time.realtimeSinceStartup;
			var found = new Dictionary<string, IndexEntry>();
			List<string> scenes = LandmarkScenes();
			for (int i = 0; i < scenes.Count; i++)
			{
				string sceneName = scenes[i];
				// Leaving the editor (a new scene replaces everything) stops the scan; it starts again next time
				if (!DynamicIslands.InEditor()) { Debug.Log("[CUSTOM ISLANDS] Object index: scan stopped (left the editor)"); yield break; }
				if (i > 0 && i % 8 == 0) yield return Resources.UnloadUnusedAssets(); // the scanned islands' textures and meshes
				IndexProgress = "Scanning Raft's islands for objects: " + SceneLabel(sceneName) + " (" + (i + 1) + "/" + scenes.Count + ")";
				var opened = new OpenedScene();
				yield return OpenScene(sceneName, opened, true);
				if (!opened.Scene.IsValid()) continue;
				string category = CategoryForScene(sceneName);
				int count = 0;
				try
				{
					foreach (KeyValuePair<string, Transform> pick in PickAll(opened.Scene))
					{
						string name = pick.Key;
						if (core.Contains(name) || found.ContainsKey(name)) continue;
						found[name] = new IndexEntry { Name = name, Scene = sceneName, Category = category, Hidden = clutter.IsMatch(name) };
						count++;
					}
					foreach (Transform t in AllTransforms(opened.Scene))
					{
						string hn = CleanName(t.name);
						if (!HarvestableObjects.IsMatch(hn) || core.Contains(hn) || found.ContainsKey(hn)) continue;
						found[hn] = new IndexEntry { Name = hn, Scene = sceneName, Category = HarvestableCategory, Harvestable = true };
						count++;
					}
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Scanning " + sceneName + ": " + e.Message); }
				Debug.Log("[CUSTOM ISLANDS] Object index: " + sceneName + " has " + count + " new objects");
				yield return CloseScene(opened);
			}
			index.Clear();
			foreach (var p in found) index[p.Key] = p.Value;
			WriteIndex();
			yield return Resources.UnloadUnusedAssets();
			Debug.Log("[CUSTOM ISLANDS] Object index ready: " + index.Count + " objects in " + scenes.Count + " of Raft's island scenes (" +
				(Time.realtimeSinceStartup - started).ToString("F0") + " s), written to " + IndexPath);
		}

		/// <summary>
		/// Loads the on-demand objects among these names (whole scenes at a time) so they can be spawned.
		/// Names that are core, already loaded or unknown are ignored.
		/// </summary>
		public static IEnumerator EnsureLoaded(IEnumerable<string> names)
		{
			yield return EnsureBuilt();
			var scenes = new List<string>();
			foreach (string n in names)
			{
				IndexEntry e;
				if (prototypes.ContainsKey(n) || !index.TryGetValue(n, out e) || scenes.Contains(e.Scene)) continue;
				scenes.Add(e.Scene);
			}
			foreach (string s in scenes) yield return LoadScene(s);
		}

		/// <summary>Scenes that must be loaded before these names can be spawned (empty = all ready).</summary>
		public static List<string> ScenesNeededFor(IEnumerable<string> names)
		{
			var scenes = new List<string>();
			foreach (string n in names)
			{
				IndexEntry e;
				if (!prototypes.ContainsKey(n) && index.TryGetValue(n, out e) && !scenes.Contains(e.Scene)) scenes.Add(e.Scene);
			}
			return scenes;
		}

		/// <summary>Loads every on-demand object of a category (all of its scenes).</summary>
		public static IEnumerator EnsureCategory(string category)
		{
			yield return EnsureBuilt();
			foreach (string s in index.Values.Where(e => e.Category == category).Select(e => e.Scene).Distinct().ToList())
				yield return LoadScene(s);
		}

		public static bool CategoryLoaded(string category)
		{
			return index.Values.Where(e => e.Category == category).All(e => prototypes.ContainsKey(e.Name) || loadedScenes.Contains(e.Scene));
		}

		static IEnumerator LoadScene(string sceneName)
		{
			while (loadingScenes.Contains(sceneName)) yield return null;
			if (loadedScenes.Contains(sceneName)) yield break;
			loadingScenes.Add(sceneName);
			try { yield return Guarded(LoadSceneObjects(sceneName), "Loading objects from " + sceneName); }
			finally
			{
				loadingScenes.Remove(sceneName);
				loadedScenes.Add(sceneName);
				RaiseChanged();
			}
		}

		static IEnumerator LoadSceneObjects(string sceneName)
		{
			var wanted = new HashSet<string>(index.Values.Where(e => e.Scene == sceneName && !prototypes.ContainsKey(e.Name)).Select(e => e.Name));
			if (wanted.Count == 0) yield break;
			if (LoadSceneManager.IsGameSceneLoaded)
				Debug.Log("[CUSTOM ISLANDS] Loading Raft's " + sceneName + " scene for a moment to take " + wanted.Count + " objects an island uses");
			var opened = new OpenedScene();
			yield return OpenScene(sceneName, opened, true);
			if (!opened.Scene.IsValid()) yield break;
			int before = prototypes.Count;
			foreach (KeyValuePair<string, Transform> pick in PickAll(opened.Scene))
			{
				if (!wanted.Contains(pick.Key) || prototypes.ContainsKey(pick.Key)) continue;
				Add(pick.Key, pick.Value, index[pick.Key].Category, true);
				// Picked from a switched-off scene: the clone of a scene root would stay switched off
				prototypes[pick.Key].SetActive(true);
				if (index[pick.Key].Hidden) hidden.Add(pick.Key);
			}
			foreach (Transform t in AllTransforms(opened.Scene))
			{
				string hn = CleanName(t.name);
				if (!wanted.Contains(hn) || prototypes.ContainsKey(hn) || !HarvestableObjects.IsMatch(hn)) continue;
				AddHarvestable(hn, t);
				harvestables[hn].SetActive(true);
			}
			Debug.Log("[CUSTOM ISLANDS] " + sceneName + ": loaded " + (prototypes.Count - before) + " of " + wanted.Count + " objects");
			yield return CloseScene(opened);
		}

		/// <summary>
		/// Every placeable object of a whole scene: top-most descendants that render a mesh, except fragments, story
		/// items and pickups (and the model parts inside them).
		/// </summary>
		static IEnumerable<KeyValuePair<string, Transform>> PickAll(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				// A scene root that renders something is an object itself
				if (root.GetComponent<Renderer>() != null || root.GetComponent<LODGroup>() != null)
				{
					string rn = CleanName(root.name);
					if (IsPlaceable(root.transform, rn)) yield return new KeyValuePair<string, Transform>(rn, root.transform);
					continue;
				}
				foreach (KeyValuePair<string, Transform> pick in Pick(root.transform))
					if (IsPlaceable(pick.Value, pick.Key)) yield return pick;
			}
		}

		static bool IsPlaceable(Transform t, string name)
		{
			if (excluded.IsMatch(name) || excludedExtra.IsMatch(name) || treeParent.IsMatch(name)) return false;
			// Inside a pickup (harvestable tree, quest item...): its parts aren't objects of their own
			for (Transform p = t; p != null; p = p.parent)
				if (pickupAncestor.IsMatch(p.name)) return false;
			// Must show a mesh (not only an effect, and not a hidden collider shape)
			foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
				if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) return true;
			return false;
		}

		static readonly Regex pickupAncestor = new Regex(@"^(Pickup_|QuestItem|NotePickup)", RegexOptions.IgnoreCase);

		#endregion

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
		/// Objects the automatic lists leave out: model fragments ("BoatHull_low.003"), primitives, probes/effects,
		/// and story items/pickups whose scripts belong to the landmark's quest.
		/// </summary>
		static readonly Regex excluded = new Regex(
			@"\.\d+$|^(Plane|Cube|Quad|Sphere|Cylinder|TextMeshPro|Particle.*|VG_EnvironmentProbeMesh.*|BoatHull.*|Window.*|Pennant_.*|Bolcutter.*|Boltcutter.*|" +
			@"Carlift_.*|QuestItemPickup_.*|Pickup_.*|NotePickup.*|.*Pickup|Bomb|DoorHandle.*|LockerDoor|Lock_Hatch|Padlock.*|Crowbar|Tools_Hammer|LOD\d+)$|^\s*$",
			RegexOptions.IgnoreCase);

		/// <summary>More that the whole-scene scan of Raft's other islands leaves out: helper shapes, water, decals.</summary>
		static readonly Regex excludedExtra = new Regex(
			@"^(Quad|Sphere|Cylinder|Capsule|.*Collider.*|.*Trigger.*|.*Occluder.*|.*Blocker.*|.*Water(Plane|Surface|Volume).*|Water|Ocean.*|.*Billboard.*|DefaultWeight|.*Decal.*|.*Shadow(Caster|Plane)?)$",
			RegexOptions.IgnoreCase);

		/// <summary>
		/// Vasagatan's small indoor clutter, which is lost on an island (pool balls, cutlery, bathroom bottles, pillows,
		/// ceiling cables and lamps...). Left out of the default list; a placeables.txt can still bring any of it back.
		/// </summary>
		static readonly Regex clutter = new Regex(
			@"^(VG_DecorationPrefabBase_)?(Pooltable_(Ball\d+|Cue|Triangle)|Spoon|Spatula|Knife|FryingPan|CuttingBoard|SoapBottle|SoapDispender|" +
			@"ShampooBottle|ToiletPaperHolder\d|ToiletPaperRoll|PaperrollHolder|Paper|Cables_\w+|CeilingLamp_Fancy2?|Kitchenfan|Flask_\d|" +
			@"Book_4|Book_Tall_2|Tools_Wrench|Pillow(_2|Decor_\d)?)( Variant)?$|^RT_ExitSignCeiling|^VG_SignStairsCeiling$",
			RegexOptions.IgnoreCase);

		static readonly Regex displayPrefix = new Regex(@"^(VG_DecorationPrefabBase_|VG_|RT_|TP_Moontown_|TP_|CaravanIsland_|Balboa_|Block_|Placeable_)|\s*Variant.*$|_Low(?=\d|_|$)|_LodGroup$");
		static readonly Regex wordBreak = new Regex(@"(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Za-z])");

		static readonly Regex harvestableLabel = new Regex(@"^Pickup_Landmark_(Tree_(\w+) (\d+)|(\w+)Tree|(.+))$");

		/// <summary>Friendly label for the object list: "VG_DecorationPrefabBase_Sofa Variant" -> "Sofa", "BigBoulder1_Low" -> "Big Boulder 1". Saves keep the real name.</summary>
		public static string DisplayName(string name)
		{
			string label;
			if (labels.TryGetValue(name, out label)) return label;
			Match hm = harvestableLabel.Match(name);
			if (hm.Success) name = hm.Groups[2].Success ? hm.Groups[2].Value + " Tree " + hm.Groups[3].Value : hm.Groups[4].Success ? hm.Groups[4].Value + " Tree" : hm.Groups[5].Value;
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
