using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's custom content next to Raft's objects: creatures (spawn points for Raft's own animals) and
	/// readable notes. Both are ordinary catalog objects, so the object list, placing, selecting, copying, undo and
	/// the island file all work for them as for any other object; what makes them special is their name and their
	/// settings (ObjectProps).
	///
	/// Creatures: Raft's animals only exist as prefabs inside a running world (Network_Host_Entities), not in the
	/// scenes the editor can borrow from. In the editor a creature is shown as a marker (a coloured body with its
	/// name over it), or as the real model once Raft's prefabs have been seen in a world this session. In a world
	/// the host spawns the real, networked animal there (CreatureSpawner).
	///
	/// Notes: "Notes &amp; signs" offers some of Raft's papers, signs, books and a bottle with a note already on them,
	/// but any placed object can be made readable in the inspector.
	/// </summary>
	public static class ContentCatalog
	{
		public const string CatchableCategory = "Animals: catchable";
		public const string HostileCategory = "Animals: hostile";
		public const string SeaCategory = "Sea creatures";
		public const string NotesCategory = "Notes & signs";
		public static readonly string[] Categories = { CatchableCategory, HostileCategory, SeaCategory, NotesCategory, "Loot & chests", "Zones & triggers" };

		/// <summary>Name of marker parts (ground ring, labels) that only help in the editor and are never tinted.</summary>
		public const string MarkerOnly = "CI_NoTint";
		const string CreaturePrefix = "Creature_";
		const string NotePrefix = "Note_";

		public enum Movement { Land, Water, Air }

		public class CreatureKind
		{
			public AI_NetworkBehaviourType Type;
			public string Label, Category, Hint;
			public float Length, Height;
			public Movement Moves;
			public string Name { get { return CreaturePrefix + Type; } }
		}

		static CreatureKind K(AI_NetworkBehaviourType type, string label, string category, float length, float height, string hint, Movement moves = Movement.Land)
		{
			return new CreatureKind { Type = type, Label = label, Category = category, Length = length, Height = height, Hint = hint, Moves = moves };
		}

		/// <summary>The creatures offered in the editor (Raft's own animals; bosses, NPCs and the shark are left out).</summary>
		public static readonly CreatureKind[] Creatures =
		{
			K(AI_NetworkBehaviourType.Chicken, "Chicken", CatchableCategory, 0.5f, 0.5f, "catch it with Raft's net launcher and keep it on the raft: lays eggs"),
			K(AI_NetworkBehaviourType.Goat, "Goat", CatchableCategory, 1.1f, 1f, "catch it with Raft's net launcher: gives milk on the raft"),
			K(AI_NetworkBehaviourType.Llama, "Llama", CatchableCategory, 1.4f, 1.9f, "catch it with Raft's net launcher: gives wool on the raft"),
			K(AI_NetworkBehaviourType.Boar, "Warthog", HostileCategory, 1.5f, 1f, "charges at players"),
			K(AI_NetworkBehaviourType.Pig, "Pig", HostileCategory, 1.4f, 0.9f, "charges at players"),
			K(AI_NetworkBehaviourType.Bear, "Bear", HostileCategory, 2.4f, 1.6f, "strong melee attacker"),
			K(AI_NetworkBehaviourType.MamaBear, "Mama bear", HostileCategory, 3.2f, 2.2f, "Balboa's huge bear"),
			K(AI_NetworkBehaviourType.PolarBear, "Polar bear", HostileCategory, 2.6f, 1.7f, "Temperance's bear"),
			K(AI_NetworkBehaviourType.Hyena, "Hyena", HostileCategory, 1.6f, 1f, "fast pack hunter"),
			K(AI_NetworkBehaviourType.Rat, "Rat", HostileCategory, 0.6f, 0.3f, "small and quick"),
			K(AI_NetworkBehaviourType.Rat_Tangaroa, "Tangaroa rat", HostileCategory, 0.6f, 0.3f, "small and quick"),
			K(AI_NetworkBehaviourType.Roach, "Roach", HostileCategory, 0.5f, 0.25f, "Tangaroa's roach"),
			K(AI_NetworkBehaviourType.BugSwarm_Bee, "Bee swarm", HostileCategory, 1f, 1f, "stings players nearby", Movement.Air),
			K(AI_NetworkBehaviourType.StoneBird, "Screecher", HostileCategory, 2f, 1.2f, "a bird that attacks players and drops stones on the raft", Movement.Air),
			K(AI_NetworkBehaviourType.PufferFish, "Puffer fish", SeaCategory, 0.6f, 0.6f, "swims near the island and explodes next to divers", Movement.Water),
			K(AI_NetworkBehaviourType.AnglerFish, "Angler fish", SeaCategory, 1.5f, 1f, "a deep-sea hunter", Movement.Water),
			K(AI_NetworkBehaviourType.Turtle, "Turtle", SeaCategory, 1f, 0.5f, "a friendly sea turtle", Movement.Water),
			K(AI_NetworkBehaviourType.Stingray, "Stingray", SeaCategory, 1.5f, 0.3f, "glides over the sea floor", Movement.Water),
			K(AI_NetworkBehaviourType.Dolphin, "Dolphin", SeaCategory, 2.2f, 0.8f, "swims around", Movement.Water),
			K(AI_NetworkBehaviourType.Whale, "Whale", SeaCategory, 12f, 4f, "a huge, harmless whale", Movement.Water),
		};

		static readonly Dictionary<string, CreatureKind> byName = Creatures.ToDictionary(c => c.Name);

		public static CreatureKind CreatureOf(string name)
		{
			CreatureKind k;
			return name != null && byName.TryGetValue(name, out k) ? k : null;
		}

		public static bool IsCreature(string name) { return CreatureOf(name) != null; }

		/// <summary>Readable objects of the list: our name, Raft's object it shows, label, the title it starts with.</summary>
		static readonly string[][] NoteObjects =
		{
			new[] { NotePrefix + "Paper", "VG_DecorationPrefabBase_Paper Variant", "Note (paper)", "Note" },
			new[] { NotePrefix + "Papers", "Placeable_PaperBundle", "Note (bundle of papers)", "Notes" },
			new[] { NotePrefix + "Book", "Placeable_OpenBook", "Open book", "Diary" },
			new[] { NotePrefix + "Sign", "Placeable_Sign", "Sign", "Sign" },
			new[] { NotePrefix + "Board", "VG_DecorationPrefabBase_BoardWithPapers Variant", "Notice board", "Notice" },
			new[] { NotePrefix + "Bottle", "Placeable_Utopia_Bottle", "Message in a bottle", "Message in a bottle" },
		};

		public const string LootCategory = "Loot & chests";
		const string LootPrefix = "Loot_";

		/// <summary>Containers of the list: our name, Raft's object it shows, label.</summary>
		static readonly string[][] LootObjects =
		{
			new[] { LootPrefix + "Chest", "Placeable_Storage_Medium", "Chest" },
			new[] { LootPrefix + "ChestSmall", "Placeable_Storage_Small", "Small chest" },
			new[] { LootPrefix + "ChestLarge", "Placeable_Storage_Large", "Large chest" },
			new[] { LootPrefix + "Crate", "TP_Moontown_SealedCrate01", "Sealed crate" },
			new[] { LootPrefix + "Box", "VG_DecorationPrefabBase_WoodenBoxes_ShortSquare Variant", "Wooden box" },
			new[] { LootPrefix + "Barrel", "TP_Moontown_Barrel01", "Barrel" },
			new[] { LootPrefix + "SunkenBarrel", "Reef_Barrel1", "Sunken barrel" },
		};

		public static bool IsNoteObject(string name) { return name != null && name.StartsWith(NotePrefix) && NoteObjects.Any(n => n[0] == name); }

		public static bool IsLootObject(string name) { return name != null && name.StartsWith(LootPrefix) && LootObjects.Any(n => n[0] == name); }

		public const string ZoneCategory = "Zones & triggers";
		const string ZonePrefix = "Zone_";
		public const string TriggerZone = ZonePrefix + "Trigger";

		/// <summary>Zones of the list: our name, label, marker colour, hint.</summary>
		static readonly KeyValuePair<string, string>[] Zones =
		{
			new KeyValuePair<string, string>(TriggerZone, "Trigger zone"),
		};

		public static bool IsZone(string name) { return name != null && name.StartsWith(ZonePrefix) && Zones.Any(z => z.Key == name); }

		public static string NewZoneId() { return "zone-" + UnityEngine.Random.Range(100, 1000); }

		/// <summary>Editor: the ids of the trigger zones placed on the island (creatures and quests link to them).</summary>
		public static List<string> ZoneIdsInEditor()
		{
			GameObject placed = GameObject.Find("PlacedObjects");
			if (placed == null) return new List<string>();
			return placed.GetComponentsInChildren<EditorGameObject>().Where(e => e.GameObjectName == TriggerZone)
				.Select(e => ObjectProps.Get(e.Props, ObjectProps.ZoneId)).Where(id => id.Length > 0).Distinct().OrderBy(id => id).ToList();
		}

		static readonly Color ZoneColor = new Color(1f, 0.85f, 0.3f);

		/// <summary>A zone in the editor: a pole with a flag to click, and a see-through sphere showing its radius.</summary>
		static GameObject BuildZoneMarker(string name, string label)
		{
			var root = new GameObject(name);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			Transform marker = new GameObject("Marker").transform;
			marker.SetParent(root.transform, false);
			Part(PrimitiveType.Cylinder, "Pole", marker, new Vector3(0, 1f, 0), Quaternion.identity, new Vector3(0.08f, 1f, 0.08f), MarkerMaterial(new Color(0.35f, 0.3f, 0.25f)));
			Part(PrimitiveType.Cube, "Flag", marker, new Vector3(0.3f, 1.75f, 0), Quaternion.identity, new Vector3(0.6f, 0.4f, 0.03f), MarkerMaterial(ZoneColor));
			// A faint sphere, and a clearer ring where it meets the ground (a child: it scales with the sphere)
			GameObject sphere = Part(PrimitiveType.Sphere, MarkerOnly, root.transform, Vector3.zero, Quaternion.identity, Vector3.one * 12f, MarkerMaterial(new Color(ZoneColor.r, ZoneColor.g, ZoneColor.b, 0.06f)));
			sphere.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			GameObject ring = Part(PrimitiveType.Cylinder, MarkerOnly, sphere.transform, new Vector3(0, 0.002f, 0), Quaternion.identity, new Vector3(1f, 0.0008f, 1f), MarkerMaterial(new Color(ZoneColor.r, ZoneColor.g, ZoneColor.b, 0.3f)));
			ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			AddLabel(root.transform, label, 2.4f);
			return root;
		}

		/// <summary>Ready-made loot for the loot editor: name, then candidate items (only those Raft has are used).</summary>
		public static readonly KeyValuePair<string, string[]>[] LootPresets =
		{
			new KeyValuePair<string, string[]>("Basics", new[] { "Plank*12", "Plastic*10", "Thatch*8", "Rope*4", "Nail*10", "Stone*6" }),
			new KeyValuePair<string, string[]>("Metal", new[] { "Scrap*6", "MetalIngot*4", "CopperIngot*3", "Bolt*6", "Hinge*4", "MetalOre*4" }),
			new KeyValuePair<string, string[]>("Food", new[] { "Watermelon*2", "Pineapple*2", "Coconut*3", "Mango*3", "Raw_Potato*4", "Egg*4", "Banana*3" }),
			new KeyValuePair<string, string[]>("Treasure", new[] { "TitaniumIngot*3", "ExplosiveGoo*2", "CircuitBoard*2", "Battery*1", "HealingSalve_Good*2", "Jar_Honey*1" }),
		};

		/// <summary>The loot of a preset: its candidates that exist in this version of Raft.</summary>
		public static string PresetLoot(string[] candidates)
		{
			var found = new List<KeyValuePair<string, int>>();
			foreach (var l in ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, string.Join(";", candidates) } }))
				if (ItemExists(l.Key)) found.Add(l);
			return ObjectProps.LootText(found);
		}

		public static string DefaultLoot() { return PresetLoot(LootPresets[0].Value); }

		public static bool ItemExists(string uniqueName)
		{
			try { return ItemManager.GetItemByName(uniqueName) != null; } catch { return false; }
		}

		/// <summary>An item's name as players see it ("Plank"), or its unique name.</summary>
		public static string ItemLabel(string uniqueName)
		{
			try
			{
				Item_Base item = ItemManager.GetItemByName(uniqueName);
				string d = item != null && item.settings_Inventory != null ? item.settings_Inventory.DisplayName : null;
				return !string.IsNullOrEmpty(d) && !d.StartsWith("#") ? d.Trim() : uniqueName.Replace('_', ' ');
			}
			catch { return uniqueName; }
		}

		public static Sprite ItemSprite(string uniqueName)
		{
			try
			{
				Item_Base item = ItemManager.GetItemByName(uniqueName);
				return item != null && item.settings_Inventory != null ? item.settings_Inventory.Sprite : null;
			}
			catch { return null; }
		}

		public static string DefaultNoteTitle(string name)
		{
			string[] n = NoteObjects.FirstOrDefault(x => x[0] == name);
			return n != null ? n[3] : "Note";
		}

		/// <summary>Extra text for the object list's hint (null for Raft's plain objects).</summary>
		public static string Hint(string name)
		{
			CreatureKind k = CreatureOf(name);
			if (k != null) return "a live " + k.Label.ToLowerInvariant() + " appears here in a world: " + k.Hint + ". Select it to change its stats and colour";
			if (IsNoteObject(name)) return "players read it in a world with the interact key (E). Select it to write the text";
			if (IsLootObject(name)) return "players open it in a world with the interact key (E) and get what's inside. Select it to choose the items";
			if (name == TriggerZone) return "invisible in a world: when a player walks in it shows a message, gives items or wakes up creatures linked to it. Select it to set it up";
			return null;
		}

		#region Registering in the object catalog

		/// <summary>Adds the creatures and notes to the object catalog (called when it is built, in the editor and in worlds).</summary>
		public static void Register()
		{
			int notes = 0;
			foreach (string[] n in NoteObjects)
			{
				GameObject source = PlaceableCatalog.Get(n[1]);
				if (source == null) { Debug.LogWarning("[CUSTOM ISLANDS] Note object '" + n[1] + "' not found in Raft's objects; '" + n[2] + "' is left out"); continue; }
				PlaceableCatalog.AddCustom(n[0], source, NotesCategory, n[2]);
				notes++;
			}
			int loot = 0;
			foreach (string[] n in LootObjects)
			{
				GameObject source = PlaceableCatalog.Get(n[1]);
				if (source == null) { Debug.LogWarning("[CUSTOM ISLANDS] Loot object '" + n[1] + "' not found in Raft's objects; '" + n[2] + "' is left out"); continue; }
				PlaceableCatalog.AddCustom(n[0], source, LootCategory, n[2]);
				loot++;
			}
			foreach (var z in Zones)
				PlaceableCatalog.AddCustom(z.Key, BuildZoneMarker(z.Key, z.Value), ZoneCategory, z.Value);
			foreach (CreatureKind k in Creatures)
				PlaceableCatalog.AddCustom(k.Name, BuildPrototype(k), k.Category, k.Label);
			Debug.Log("[CUSTOM ISLANDS] Custom content: " + Creatures.Length + " creatures (" + models.Count + " with Raft's models), " + notes + " note objects, " + loot + " loot containers");
		}

		static GameObject BuildPrototype(CreatureKind k)
		{
			GameObject model, proto;
			if (models.TryGetValue(k.Type, out model) && model != null)
			{
				// The model sits in a child so its size can be changed without touching its own scale
				proto = new GameObject(k.Name);
				proto.transform.SetParent(PlaceableCatalog.Container.transform, false);
				GameObject m = UnityEngine.Object.Instantiate(model, proto.transform);
				m.name = "Model";
				m.transform.localPosition = Vector3.zero;
				m.transform.localRotation = Quaternion.identity;
				m.SetActive(true);
			}
			else proto = BuildMarker(k);
			proto.name = k.Name;
			proto.transform.localPosition = Vector3.zero;
			AddLabel(proto.transform, k.Label, Mathf.Max(k.Height, 0.4f) + 0.5f);
			return proto;
		}

		static readonly Dictionary<AI_NetworkBehaviourType, GameObject> models = new Dictionary<AI_NetworkBehaviourType, GameObject>();
		static GameObject modelContainer;

		/// <summary>
		/// In a world: keeps a script-free copy of each creature's model (from Raft's prefab list), so the editor can
		/// show real animals instead of markers for the rest of the session.
		/// </summary>
		public static void CacheModels(IEnumerable<AI_NetworkBehaviour> prefabs)
		{
			if (prefabs == null) return;
			int added = 0;
			foreach (AI_NetworkBehaviour prefab in prefabs)
			{
				if (prefab == null || models.ContainsKey(prefab.behaviourType) || !Creatures.Any(c => c.Type == prefab.behaviourType)) continue;
				try
				{
					if (modelContainer == null)
					{
						modelContainer = new GameObject("CustomIslands_CreatureModels");
						modelContainer.SetActive(false);
						UnityEngine.Object.DontDestroyOnLoad(modelContainer);
					}
					GameObject copy = UnityEngine.Object.Instantiate(prefab.gameObject, modelContainer.transform);
					copy.name = prefab.behaviourType.ToString();
					PlaceableCatalog.StripScripts(copy);
					// Only the look: no physics, no navigation, no sounds of its own
					foreach (Component c in copy.GetComponentsInChildren<Component>(true).Where(c => c is Collider || c is Rigidbody || c is UnityEngine.AI.NavMeshAgent || c is CharacterController || c is AudioSource).ToList())
						UnityEngine.Object.DestroyImmediate(c);
					copy.transform.localPosition = Vector3.zero;
					copy.transform.localRotation = Quaternion.identity;
					models[prefab.behaviourType] = copy;
					added++;
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not copy the model of " + prefab.behaviourType + ": " + e.Message); }
			}
			if (added > 0) Debug.Log("[CUSTOM ISLANDS] Kept " + added + " creature models for the editor");
		}

		/// <summary>Editor: swaps markers for real models seen in a world since the catalog was built.</summary>
		public static void UpgradeMarkers()
		{
			foreach (CreatureKind k in Creatures)
			{
				GameObject current = PlaceableCatalog.Get(k.Name);
				if (current == null || !models.ContainsKey(k.Type) || current.transform.Find("Marker") == null) continue;
				PlaceableCatalog.AddCustom(k.Name, BuildPrototype(k), k.Category, k.Label);
				UnityEngine.Object.Destroy(current);
				ObjectThumbnails.Forget(k.Name);
			}
		}

		public static bool HasModel(AI_NetworkBehaviourType type) { return models.ContainsKey(type); }

		#endregion

		#region Markers and labels (editor)

		static readonly Color CatchableColor = new Color(0.55f, 0.9f, 0.55f), HostileColor = new Color(1f, 0.55f, 0.4f), SeaColor = new Color(0.45f, 0.75f, 1f);
		static readonly Dictionary<Color, Material> markerMaterials = new Dictionary<Color, Material>();

		static Material MarkerMaterial(Color c)
		{
			Material m;
			if (markerMaterials.TryGetValue(c, out m) && m != null) return m;
			Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
			m = new Material(shader) { name = "CI_CreatureMarker", color = c };
			if (c.a < 1f && m.HasProperty("_Mode"))
			{
				// Standard shader, transparent
				m.SetFloat("_Mode", 3); m.SetInt("_SrcBlend", 5); m.SetInt("_DstBlend", 10); m.SetInt("_ZWrite", 0);
				m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000;
			}
			markerMaterials[c] = m;
			return m;
		}

		/// <summary>A simple animal shape (body, head, ground ring) in the creature group's colour, sized like the animal.</summary>
		static GameObject BuildMarker(CreatureKind k)
		{
			var root = new GameObject(k.Name);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			var marker = new GameObject("Marker").transform;
			marker.SetParent(root.transform, false);
			Color c = k.Category == CatchableCategory ? CatchableColor : k.Category == SeaCategory ? SeaColor : HostileColor;
			float len = k.Length, h = k.Height;

			Part(PrimitiveType.Capsule, "Body", marker, new Vector3(0, h * 0.55f, 0), Quaternion.Euler(90, 0, 0), new Vector3(h * 0.7f, len * 0.5f, h * 0.7f), MarkerMaterial(c));
			Part(PrimitiveType.Sphere, "Head", marker, new Vector3(0, h * 0.8f, len * 0.5f), Quaternion.identity, Vector3.one * h * 0.5f, MarkerMaterial(c * 0.8f + Color.black * 0.2f));
			if (k.Moves == Movement.Land)
				foreach (float x in new[] { -1f, 1f })
					foreach (float z in new[] { -1f, 1f })
						Part(PrimitiveType.Cylinder, "Leg", marker, new Vector3(x * h * 0.2f, h * 0.2f, z * len * 0.3f), Quaternion.identity, new Vector3(h * 0.15f, h * 0.2f, h * 0.15f), MarkerMaterial(c * 0.7f + Color.black * 0.3f));
			GameObject ring = Part(PrimitiveType.Cylinder, MarkerOnly, marker, new Vector3(0, 0.02f, 0), Quaternion.identity, new Vector3(len * 1.3f, 0.01f, len * 1.3f), MarkerMaterial(new Color(c.r, c.g, c.b, 0.35f)));
			ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			return root;
		}

		static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
		{
			GameObject go = GameObject.CreatePrimitive(type);
			go.name = name;
			UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
			go.transform.SetParent(parent, false);
			go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
			go.GetComponent<Renderer>().sharedMaterial = mat;
			return go;
		}

		/// <summary>A name tag that floats over an object in the editor, always facing the camera.</summary>
		static TextMesh AddLabel(Transform parent, string text, float height)
		{
			var go = new GameObject(MarkerOnly);
			go.name = MarkerOnly;
			go.transform.SetParent(parent, false);
			go.transform.localPosition = new Vector3(0, height, 0);
			var tm = go.AddComponent<TextMesh>();
			tm.font = UIKit.Font;
			tm.fontSize = 48;
			tm.characterSize = 0.05f;
			tm.anchor = TextAnchor.LowerCenter;
			tm.alignment = TextAlignment.Center;
			tm.color = Color.white;
			tm.text = text;
			tm.richText = true;
			MeshRenderer mr = go.GetComponent<MeshRenderer>();
			mr.sharedMaterial = UIKit.Font.material;
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			go.AddComponent<FaceCamera>();
			return tm;
		}

		/// <summary>Editor: shows a creature's herd size, difficulty and size on its marker, and a note's title over a readable object.</summary>
		public static void ShowInEditor(GameObject go, IDictionary<string, string> props)
		{
			EditorGameObject ego = go.GetComponent<EditorGameObject>();
			string name = ego != null ? ego.GameObjectName : PlaceableCatalog.CleanName(go.name);
			if (IsZone(name))
			{
				float radius = ObjectProps.Radius(props);
				foreach (Transform child in go.transform)
					if (child.name == MarkerOnly && child.GetComponent<TextMesh>() == null) child.localScale = Vector3.one * radius * 2f;
				TextMesh zl = Label(go);
				if (zl != null)
				{
					int items = ObjectProps.Loot(props).Count;
					string msg = ObjectProps.Get(props, ObjectProps.ZoneMessage);
					zl.text = "Trigger: " + ObjectProps.Get(props, ObjectProps.ZoneId) + (ObjectProps.Repeats(props) ? " (every time)" : " (once)") +
						"\n<size=36>" + radius.ToString("0.#") + " m" + (msg.Length > 0 ? " \u00B7 message" : "") + (items > 0 ? " \u00B7 " + items + " item(s)" : "") + "</size>";
				}
				return;
			}
			CreatureKind k = CreatureOf(name);
			if (k != null)
			{
				Transform body = go.transform.Find("Marker");
				if (body == null) body = go.transform.Find("Model");
				float size = ObjectProps.Size(props);
				if (body != null) body.localScale = Vector3.one * size;
				TextMesh label = Label(go);
				if (label != null)
				{
					int count = ObjectProps.Count(props);
					string summary = Summary(props);
					string zone = ObjectProps.Get(props, ObjectProps.CreatureZone);
					if (zone.Length > 0) summary += (summary.Length > 0 ? ", " : "") + "waits for " + zone;
					label.text = k.Label + (count > 1 ? " \u00D7" + count : "") + (summary.Length > 0 ? "\n<size=36>" + summary + "</size>" : "");
					label.transform.localPosition = new Vector3(0, Mathf.Max(k.Height, 0.4f) * size + 0.5f, 0);
				}
				return;
			}
			bool note = ObjectProps.IsNote(name, props), loot = ObjectProps.IsLoot(name, props);
			TextMesh tag = Label(go);
			if (!note && !loot) { if (tag != null) UnityEngine.Object.Destroy(tag.gameObject); return; }
			if (tag == null)
			{
				Bounds b;
				float top = RendererBounds(go, out b) ? b.max.y - go.transform.position.y : 1f;
				tag = AddLabel(go.transform, "", top / Mathf.Max(0.01f, go.transform.lossyScale.y) + 0.3f);
			}
			string title = ObjectProps.Get(props, ObjectProps.NoteTitle, "");
			var lines = new List<string>();
			if (note) lines.Add("<color=#ffc766>\u2709</color> " + (title.Length > 0 ? title : "(no title)"));
			if (loot)
			{
				int stacks = ObjectProps.Loot(props).Count;
				lines.Add("<color=#8fdc8f>\u25a3</color> " + (stacks == 0 ? "empty" : stacks == 1 ? "1 item" : stacks + " items"));
			}
			tag.text = string.Join("\n", lines.ToArray());
		}

		static TextMesh Label(GameObject go)
		{
			foreach (Transform child in go.transform)
				if (child.name == MarkerOnly && child.GetComponent<TextMesh>() != null) return child.GetComponent<TextMesh>();
			return null;
		}

		/// <summary>"Hard", or "health \u00D72, speed \u00D70.5" - empty for a creature with Raft's own stats.</summary>
		public static string Summary(IDictionary<string, string> props)
		{
			float hp = ObjectProps.Health(props), dmg = ObjectProps.Damage(props), spd = ObjectProps.Speed(props);
			foreach (var p in ObjectProps.Presets)
				if (Mathf.Approximately(hp, p.Value[0]) && Mathf.Approximately(dmg, p.Value[1]) && Mathf.Approximately(spd, p.Value[2]))
					return p.Key == "Normal" ? "" : p.Key;
			var parts = new List<string>();
			if (!Mathf.Approximately(hp, 1f)) parts.Add("health \u00D7" + ObjectProps.Format(hp));
			if (!Mathf.Approximately(dmg, 1f)) parts.Add("damage \u00D7" + ObjectProps.Format(dmg));
			if (!Mathf.Approximately(spd, 1f)) parts.Add("speed \u00D7" + ObjectProps.Format(spd));
			return string.Join(", ", parts.ToArray());
		}

		static bool RendererBounds(GameObject go, out Bounds b)
		{
			b = new Bounds();
			bool any = false;
			foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
			{
				if (r.name == MarkerOnly || r is ParticleSystemRenderer) continue;
				if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
			}
			return any;
		}

		#endregion
	}

	/// <summary>Turns a label towards the editor camera and keeps it readable from far away.</summary>
	public class FaceCamera : MonoBehaviour
	{
		void LateUpdate()
		{
			Camera cam = Camera.main;
			if (cam == null) return;
			Vector3 d = transform.position - cam.transform.position;
			if (d.sqrMagnitude < 0.0001f) return;
			transform.rotation = Quaternion.LookRotation(d, cam.transform.up);
			// Grows with distance so it stays about the same size on screen (never smaller than up close)
			Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
			float k = Mathf.Clamp(d.magnitude / 25f, 1f, 12f);
			transform.localScale = new Vector3(k / Mathf.Max(0.01f, parentScale.x), k / Mathf.Max(0.01f, parentScale.y), k / Mathf.Max(0.01f, parentScale.z));
		}
	}
}
