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
		/// Builds the island in the current (game) scene with its sea level at worldPosition.y.
		/// The terrain is centred on worldPosition horizontally. The catalog must already be built.
		/// </summary>
		public static GameObject SpawnInWorld(IslandFile island, Vector3 worldPosition)
		{
			var root = new GameObject("CustomIsland_" + island.Name);
			// Terrain origin is its corner; shift so the island's centre lands on worldPosition,
			// and down so the editor's water level lines up with the sea
			root.transform.position = worldPosition - new Vector3(island.TerrainSize.x / 2f, island.WaterLevel, island.TerrainSize.z / 2f);

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
