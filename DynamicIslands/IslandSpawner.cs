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
				GameObject go = PlaceableCatalog.Spawn(o.Name, parent);
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
		/// Builds the island in the current (game) scene with its sea level at worldPosition.y and the
		/// centre of its land at worldPosition horizontally. The catalog must already be built.
		/// </summary>
		public static GameObject SpawnInWorld(IslandFile island, Vector3 worldPosition)
		{
			var root = new GameObject("CustomIsland_" + island.Name);
			// Terrain origin is its corner; shift so the land centre lands on worldPosition,
			// and down so the editor's water level lines up with the sea
			Vector2 land = LandCentre(island);
			root.transform.position = worldPosition - new Vector3(land.x, island.WaterLevel, land.y);

			TerrainData data = CreateTerrainData(island.TerrainSize, island.HeightmapResolution);
			data.SetHeights(0, 0, island.Heights);
			GameObject terrainGO = Terrain.CreateTerrainGameObject(data);
			terrainGO.name = "Terrain";
			terrainGO.layer = TerrainLayer;
			terrainGO.transform.SetParent(root.transform, false);

			var objects = new GameObject("Objects");
			objects.transform.SetParent(root.transform, false);
			int missing = SpawnObjects(island, objects.transform, false);

			Debug.Log("[CUSTOM ISLANDS] Spawned island '" + island.Name + "' at " + worldPosition + " with " +
				(island.Objects.Count - missing) + "/" + island.Objects.Count + " objects");
			return root;
		}
	}
}
