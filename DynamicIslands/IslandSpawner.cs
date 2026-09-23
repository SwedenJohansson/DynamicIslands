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
				NetworkIDManager.AddNetworkID(pn, typeof(PickupItem_Networked));
			}
		}

		/// <summary>Removes a spawned island from the world (and its objects from Raft's network registry).</summary>
		public static void Despawn(GameObject root)
		{
			if (root == null) return;
			foreach (PickupItem_Networked pn in root.GetComponentsInChildren<PickupItem_Networked>(true))
			{
				try { NetworkIDManager.RemoveNetworkID(pn, typeof(PickupItem_Networked)); }
				catch (System.Exception) { } // never registered
			}
			SpawnedRoots.Remove(root);
			Object.Destroy(root);
		}

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
		public static int SpawnObjects(IslandFile island, Transform parent, bool editable)
		{
			int missing = 0;
			foreach (IslandObject o in island.Objects)
			{
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
					go.AddComponent<EditorGameObject>().GameObjectName = o.Name;
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
			if (n == 0) return new Vector2(island.TerrainSize.x / 2f, island.TerrainSize.z / 2f);
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
			return Mathf.Sqrt(maxSq);
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
			// Terrain origin is its corner; shift so the land centre lands on worldPosition,
			// and down so the editor's water level lines up with the sea
			Vector2 land = LandCentre(island);
			root.transform.position = worldPosition - new Vector3(land.x, island.WaterLevel, land.y);

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

			var objects = new GameObject("Objects");
			objects.transform.SetParent(root.transform, false);
			int missing = SpawnObjects(island, objects.transform, false);

			Debug.Log("[CUSTOM ISLANDS] Spawned island '" + island.Name + "' at " + worldPosition + " with " +
				(island.Objects.Count - missing) + "/" + island.Objects.Count + " objects");
			return root;
		}
	}
}
