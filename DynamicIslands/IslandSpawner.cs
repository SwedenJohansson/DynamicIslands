using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>Turns an IslandFile into GameObjects, in the editor or in a Raft world.</summary>
	public static class IslandSpawner
	{
		/// <summary>Raft's layer for walkable static geometry; the original landmark code used it for island terrain.</summary>
		public const int TerrainLayer = 16;

		/// <summary>Every island spawned in the current world (host and clients), so world shifts can move them.</summary>
		public static readonly List<GameObject> SpawnedRoots = new List<GameObject>();

		/// <summary>
		/// Raft finds a tree or pickup that a remote player harvests or picks up by its ObjectIndex in NetworkIDManager
		/// (Message_AxeHit, Message_PickupObjectManager_RemoveItem). Every machine spawns the same island objects in
		/// the same order, so indexes built from the island's id (shared by host and clients) plus the object's place
		/// in the hierarchy match everywhere. The high base keeps them clear of Raft's own counter-based indexes.
		/// </summary>
		public static void RegisterNetworkIds(GameObject root, int islandId)
		{
			uint baseIndex = 0xC0000000u | ((uint)(islandId & 0x3FFF) << 16);
			uint n = 0;
			foreach (PickupItem_Networked pn in root.GetComponentsInChildren<PickupItem_Networked>(true))
			{
				pn.ObjectIndex = baseIndex + (++n);
				// Picked up = disabled rather than destroyed, so the island's objects keep their order and state can be recorded
				pn.spawnType = ObjectSpawnType.GameObject;
				NetworkIDManager.AddNetworkID(pn, typeof(PickupItem_Networked));
			}
		}

		/// <summary>Removes a spawned island from the world (and its objects from Raft's network registry).</summary>
		public static void Despawn(GameObject root)
		{
			if (root == null) return;
			try { CreatureSpawner.OnIslandDespawned(root); }
			catch (System.Exception e) { Debug.LogError("[CUSTOM ISLANDS] Removing the island's creatures: " + e); }
			foreach (PickupItem_Networked pn in root.GetComponentsInChildren<PickupItem_Networked>(true))
			{
				try { NetworkIDManager.RemoveNetworkID(pn, typeof(PickupItem_Networked)); }
				catch (System.Exception) { } // never registered
			}
			SpawnedRoots.Remove(root);
			Object.Destroy(root);
		}

		#region Elevation (flying and underwater islands)

		public const float MinElevation = -100f, MaxElevation = 250f;
		/// <summary>Islands raised more than this above the sea are built as flying islands (holes + rocky underside).</summary>
		public const float FlyingThreshold = 3f;
		/// <summary>Clearance kept between a flying island's underside and the sea, so a raft can pass beneath.</summary>
		const float UndersideClearance = 8f;

		public static string DescribeElevation(float e)
		{
			if (e > FlyingThreshold) return "flying " + e.ToString("F0") + " m above the sea";
			if (e < -0.5f) return "under water, top of the land " + (-e).ToString("F0") + " m deeper than normal";
			return "normal (at sea level)";
		}

		/// <summary>The elevation saved in an island file (0 if it can't be read).</summary>
		public static float ElevationOf(string islandName)
		{
			try { return IslandFile.Load(PathFor(islandName)).Elevation; }
			catch { return 0f; }
		}

		/// <summary>
		/// Flying island: cuts holes in the terrain wherever it lies below the editor's sea level (so no flat square of
		/// seabed floats in the sky) and hangs a rocky underside below the remaining land, from the coastline down to a
		/// point, with a collider so a low island still stops a raft.
		/// </summary>
		static void MakeFlying(TerrainData data, Transform terrainTransform, float waterLevel, float elevation)
		{
			int res = data.heightmapResolution, cells = res - 1;
			float[,] h = data.GetHeights(0, 0, res, res);
			float spacing = data.size.x / cells, height = data.size.y;
			float water = waterLevel / height;

			// Land samples, and each land sample's distance (in samples) to the nearest non-land sample
			var land = new bool[res, res];
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) land[z, x] = h[z, x] > water;
			int[,] dist = DistanceToEdge(land, res);

			// Holes: keep only cells whose four corners are land
			var solid = new bool[cells, cells];
			int kept = 0;
			for (int z = 0; z < cells; z++)
				for (int x = 0; x < cells; x++)
				{
					solid[z, x] = land[z, x] && land[z, x + 1] && land[z + 1, x] && land[z + 1, x + 1];
					if (solid[z, x]) kept++;
				}
			if (kept == 0) return;
			data.SetHoles(0, 0, solid);

			// Underside: same grid as the kept cells; the rim follows the surface, the inside hangs down like a cone
			// that is deepest under the middle of the land (never reaching down to the sea)
			float maxDepth = Mathf.Max(2f, elevation - UndersideClearance);
			int dMax = 1;
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) if (dist[z, x] > dMax) dMax = dist[z, x];
			float coneDepth = Mathf.Min(maxDepth, dMax * spacing * 0.9f);
			var index = new int[res, res];
			var verts = new List<Vector3>();
			var uvs = new List<Vector2>();
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					index[z, x] = -1;
					if (!land[z, x]) continue;
					float surface = h[z, x] * height;
					int d = dist[z, x] - 1; // 0 on the coastline
					float noise = Mathf.PerlinNoise(x * 0.07f, z * 0.07f);
					// Relative to sea level (terrain y = waterLevel); rounded near the rim, ragged with noise
					float depth = Mathf.Min(coneDepth * Mathf.Pow(d / (float)dMax, 0.75f) * (0.8f + 0.4f * noise), maxDepth);
					float y = d == 0 ? surface : Mathf.Min(surface, waterLevel) - depth;
					index[z, x] = verts.Count;
					verts.Add(new Vector3(x * spacing, y, z * spacing));
					uvs.Add(new Vector2((x * spacing + y) / 12f, (z * spacing + y) / 12f));
				}
			var tris = new List<int>();
			for (int z = 0; z < cells; z++)
				for (int x = 0; x < cells; x++)
				{
					if (!solid[z, x]) continue;
					int a = index[z, x], b = index[z, x + 1], c = index[z + 1, x], e = index[z + 1, x + 1];
					// Wound to face downwards
					tris.Add(a); tris.Add(b); tris.Add(c);
					tris.Add(b); tris.Add(e); tris.Add(c);
				}

			var mesh = new Mesh { name = "CI_FlyingUnderside" };
			mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			mesh.SetVertices(verts);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			var go = new GameObject("Underside");
			go.layer = TerrainLayer;
			go.transform.SetParent(terrainTransform, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = UndersideMaterial(TerrainPainter.StyleOf(terrainTransform.GetComponent<Terrain>()));
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
		}

		/// <summary>Chamfer distance (in samples) from each true cell to the nearest false cell.</summary>
		static int[,] DistanceToEdge(bool[,] inside, int res)
		{
			const int Far = 1 << 20;
			var d = new int[res, res];
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) d[z, x] = inside[z, x] ? Far : 0;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					if (d[z, x] == 0) continue;
					int v = d[z, x];
					if (x > 0) v = Mathf.Min(v, d[z, x - 1] + 1); else v = 1;
					if (z > 0) v = Mathf.Min(v, d[z - 1, x] + 1); else v = 1;
					d[z, x] = v;
				}
			for (int z = res - 1; z >= 0; z--)
				for (int x = res - 1; x >= 0; x--)
				{
					if (d[z, x] == 0) continue;
					int v = d[z, x];
					if (x < res - 1) v = Mathf.Min(v, d[z, x + 1] + 1); else v = 1;
					if (z < res - 1) v = Mathf.Min(v, d[z + 1, x] + 1); else v = 1;
					d[z, x] = v;
				}
			return d;
		}

		static readonly Dictionary<int, Material> undersideMaterials = new Dictionary<int, Material>();

		/// <summary>Rock-textured material for the underside (the style's steep-ground layer when Raft's textures are in use).</summary>
		static Material UndersideMaterial(int style)
		{
			Material undersideMaterial;
			if (undersideMaterials.TryGetValue(style, out undersideMaterial) && undersideMaterial != null) return undersideMaterial;
			Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
			undersideMaterial = new Material(shader) { name = "CI_FlyingUnderside_" + TerrainPainter.StyleName(style) };
			undersideMaterials[style] = undersideMaterial;
			TerrainLayer rock = TerrainPainter.LayersFor(style)[TerrainPainter.Rock];
			if (rock != null && rock.diffuseTexture != null)
			{
				undersideMaterial.mainTexture = rock.diffuseTexture;
				if (rock.normalMapTexture != null && undersideMaterial.HasProperty("_BumpMap"))
				{
					undersideMaterial.SetTexture("_BumpMap", rock.normalMapTexture);
					undersideMaterial.EnableKeyword("_NORMALMAP");
				}
			}
			else undersideMaterial.color = new Color(0.45f, 0.42f, 0.38f);
			if (undersideMaterial.HasProperty("_Glossiness")) undersideMaterial.SetFloat("_Glossiness", 0.05f);
			return undersideMaterial;
		}

		#endregion

		public static string PathFor(string islandName)
		{
			return Path.Combine(DynamicIslands.assetpath, islandName + IslandFile.Extension);
		}

		public static IEnumerable<string> ListSavedIslands()
		{
			if (!Directory.Exists(DynamicIslands.assetpath)) return Enumerable.Empty<string>();
			return Directory.GetFiles(DynamicIslands.assetpath, "*" + IslandFile.Extension)
				.Select(Path.GetFileNameWithoutExtension)
				.OrderBy(n => n);
		}

		public static TerrainData CreateTerrainData(Vector3 size, int heightmapResolution)
		{
			var data = new TerrainData();
			// Resolution must be set before size (setting resolution rescales size)
			data.heightmapResolution = heightmapResolution;
			data.size = size;
			return data;
		}

		/// <summary>
		/// Instantiates the island's objects under parent. Positions in the file are relative to the terrain origin,
		/// so parent should sit at the terrain origin. Returns the number of objects that could not be found.
		/// </summary>
		public static int SpawnObjects(IslandFile island, Transform parent, bool editable, bool skipUnderwater = false)
		{
			int missing = 0, creature = 0;
			foreach (IslandObject o in island.Objects)
			{
				// Creatures: in a world only their spawn point exists (the host brings the live animals, CreatureSpawner).
				// They are numbered in file order, which is the same on every machine.
				if (!editable && ContentCatalog.IsCreature(o.Name))
				{
					CreatureSpawnPoint.Create(parent, o, creature++);
					continue;
				}
				// A flying island has no sea around it: corals and the like would hang in the air
				if (skipUnderwater && o.Position.y < island.WaterLevel - 0.5f) continue;
				GameObject go = PlaceableCatalog.Spawn(o.Name, parent, !editable); // gameplay scripts only in a world
				if (go == null)
				{
					missing++;
					Debug.LogWarning("[CUSTOM ISLANDS] Object '" + o.Name + "' is not in the object catalog - skipped");
					continue;
				}
				go.transform.position = parent.position + o.Position;
				go.transform.rotation = Quaternion.Euler(o.EulerRotation);
				go.transform.localScale = o.Scale;
				foreach (Collider c in go.GetComponentsInChildren<Collider>()) c.enabled = true;

				if (editable)
					EditorGameObject.Attach(go, o.Name, o.Props ?? new Dictionary<string, string>());
				else
				{
					ObjectProps.ApplyTint(go, o.Props);
					if (ObjectProps.IsNote(o.Name, o.Props)) CustomNote.Attach(go, o.Props);
				}
			}
			return missing;
		}

		/// <summary>
		/// Centre (terrain-local x/z) of the parts of the island above the water level, so the land
		/// rather than the middle of the whole terrain square is what ends up where we spawn.
		/// Falls back to the terrain centre when nothing is above water.
		/// </summary>
		public static Vector2 LandCentre(IslandFile island)
		{
			int res = island.HeightmapResolution;
			float water = island.WaterLevel / island.TerrainSize.y;
			double sx = 0, sz = 0; long n = 0;
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
					if (island.Heights[y, x] > water) { sx += x; sz += y; n++; }
			if (n == 0)
			{
				// No land (e.g. an abandoned raft built from Raft blocks on the water): centre on the objects
				if (island.Objects.Count > 0)
					return new Vector2(island.Objects.Average(o => o.Position.x), island.Objects.Average(o => o.Position.z));
				return new Vector2(island.TerrainSize.x / 2f, island.TerrainSize.z / 2f);
			}
			float step = 1f / (res - 1);
			return new Vector2((float)(sx / n) * step * island.TerrainSize.x, (float)(sz / n) * step * island.TerrainSize.z);
		}

		/// <summary>
		/// How far (m) the shaped part of the island reaches from its land centre (see LandCentre), i.e. the radius
		/// of the circle around the spawn position that the island covers. 0 if nothing was shaped.
		/// </summary>
		public static float LandRadius(IslandFile island)
		{
			int res = island.HeightmapResolution;
			float threshold = ShapedThresholdMetres / island.TerrainSize.y;
			float step = 1f / (res - 1);
			Vector2 centre = LandCentre(island);
			float maxSq = 0f;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					if (island.Heights[z, x] > threshold)
					{
						float dx = x * step * island.TerrainSize.x - centre.x, dz = z * step * island.TerrainSize.z - centre.y;
						float sq = dx * dx + dz * dz;
						if (sq > maxSq) maxSq = sq;
					}
			if (maxSq == 0f && island.Objects.Count > 0) // no land: how far the objects reach
				foreach (IslandObject o in island.Objects)
					maxSq = Mathf.Max(maxSq, (o.Position.x - centre.x) * (o.Position.x - centre.x) + (o.Position.z - centre.y) * (o.Position.z - centre.y));
			return Mathf.Sqrt(maxSq);
		}

		/// <summary>True if anything of the terrain was raised above the flat seabed (islands of only objects have no land).</summary>
		public static bool HasLand(IslandFile island)
		{
			float threshold = ShapedThresholdMetres / island.TerrainSize.y;
			foreach (float h in island.Heights) if (h > threshold) return true;
			return false;
		}

		/// <summary>Anything raised more than this above the flat seabed (height 0) counts as part of the island.</summary>
		const float ShapedThresholdMetres = 1f;
		/// <summary>Extra heightmap samples kept around the shaped area so slopes don't end in a cliff.</summary>
		const int CropMarginSamples = 16;

		/// <summary>
		/// Square block of the heightmap (size 2^n + 1, as Unity terrains require) covering everything that was
		/// raised above the seabed plus a margin. Falls back to the whole heightmap if nothing was shaped.
		/// </summary>
		public static void GetCropArea(IslandFile island, out int x0, out int z0, out int size)
		{
			int res = island.HeightmapResolution;
			float threshold = ShapedThresholdMetres / island.TerrainSize.y;
			int minX = res, minZ = res, maxX = -1, maxZ = -1;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					if (island.Heights[z, x] > threshold)
					{
						if (x < minX) minX = x; if (x > maxX) maxX = x;
						if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
					}
			if (maxX < 0) { x0 = 0; z0 = 0; size = res; return; }

			int needed = Mathf.Max(maxX - minX, maxZ - minZ) + 1 + 2 * CropMarginSamples;
			size = 33;
			while (size < needed && size < res) size = (size - 1) * 2 + 1;
			size = Mathf.Min(size, res);

			// Centre the block on the shaped area, then keep it inside the heightmap
			x0 = Mathf.Clamp((minX + maxX) / 2 - size / 2, 0, res - size);
			z0 = Mathf.Clamp((minZ + maxZ) / 2 - size / 2, 0, res - size);
		}

		/// <summary>
		/// Builds the island in the current (game) scene with its sea level at worldPosition.y and the
		/// centre of its land at worldPosition horizontally. The catalog must already be built.
		/// </summary>
		public static GameObject SpawnInWorld(IslandFile island, Vector3 worldPosition)
		{
			var root = new GameObject("CustomIsland_" + island.Name);
			SpawnedRoots.Add(root);
			IslandInfo.Tag(root, island);
			// Terrain origin is its corner; shift so the land centre lands on worldPosition,
			// and down so the editor's water level lines up with the sea
			Vector2 land = LandCentre(island);
			root.transform.position = worldPosition - new Vector3(land.x, island.WaterLevel, land.y);

			bool flying = worldPosition.y > FlyingThreshold;
			if (HasLand(island)) // an island of only objects (e.g. an abandoned raft of Raft blocks) gets no terrain
			{
				// Only bring the part of the heightmap that has been shaped, not the whole flat 1000 x 1000 m seabed
				int cropX, cropZ, cropSize;
				GetCropArea(island, out cropX, out cropZ, out cropSize);
				float spacing = island.TerrainSize.x / (island.HeightmapResolution - 1);
				var cropped = new float[cropSize, cropSize];
				for (int z = 0; z < cropSize; z++)
					for (int x = 0; x < cropSize; x++)
						cropped[z, x] = island.Heights[cropZ + z, cropX + x];

				TerrainData data = CreateTerrainData(new Vector3(spacing * (cropSize - 1), island.TerrainSize.y, spacing * (cropSize - 1)), cropSize);
				data.SetHeights(0, 0, cropped);
				GameObject terrainGO = Terrain.CreateTerrainGameObject(data);
				terrainGO.name = "Terrain";
				terrainGO.layer = TerrainLayer;
				terrainGO.transform.SetParent(root.transform, false);
				// Object positions in the file are relative to the full terrain's corner, which the root still represents
				terrainGO.transform.localPosition = new Vector3(cropX * spacing, 0, cropZ * spacing);
				Terrain spawnedTerrain = terrainGO.GetComponent<Terrain>();
				TerrainPainter.SetStyle(spawnedTerrain, TerrainPainter.StyleIndex(island.Style));
				// Saved paint covers the full terrain; take the block matching the heightmap crop
				// (alphamap pixels line up with heightmap cells: resolution = heightmap resolution - 1)
				int cells = island.HeightmapResolution - 1;
				if (island.HasPaint && island.AlphamapResolution % cells == 0)
				{
					int scale = island.AlphamapResolution / cells;
					TerrainPainter.ApplySaved(spawnedTerrain, island.GetAlphamapBlock(cropX * scale, cropZ * scale, (cropSize - 1) * scale));
				}
				else
				{
					TerrainPainter.Setup(spawnedTerrain, worldPosition.y);
				}

				if (flying) MakeFlying(data, terrainGO.transform, island.WaterLevel, worldPosition.y);
			}

			var objects = new GameObject("Objects");
			objects.transform.SetParent(root.transform, false);
			int missing = SpawnObjects(island, objects.transform, false, flying);
			int wanted = flying ? island.Objects.Count(o => o.Position.y >= island.WaterLevel - 0.5f) : island.Objects.Count;

			Debug.Log("[CUSTOM ISLANDS] Spawned island '" + island.Name + "' at " + worldPosition + " with " +
				(wanted - missing) + "/" + wanted + " objects" + (worldPosition.y != 0f ? ", " + DescribeElevation(worldPosition.y) : "") +
				(wanted < island.Objects.Count ? " (" + (island.Objects.Count - wanted) + " under-water objects left out)" : ""));
			return root;
		}
	}
}
