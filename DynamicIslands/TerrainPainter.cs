using System;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Terrain texturing. Four procedural layers (seabed, sand, grass, rock). By default every pixel is painted
	/// automatically from height and slope; pixels painted by hand in the editor are marked in a paint mask
	/// (1 = hand-painted) and are left alone by the automatic painter. Weights and mask are saved in the
	/// .island file (format 2); older files fall back to automatic texturing.
	/// </summary>
	public static class TerrainPainter
	{
		public const int Seabed = 0, Sand = 1, Grass = 2, Rock = 3;
		public const int LayerCount = 4;
		public static readonly string[] LayerNames = { "Seabed", "Sand", "Grass", "Rock" };
		const int AlphamapResolution = 512;

		static TerrainLayer[] layers;
		static TerrainLayer[] raftLayers;
		static Material terrainMaterial;

		/// <summary>True once Raft's own ground textures were borrowed from one of its islands (see PlaceableCatalog).</summary>
		public static bool HasRaftTextures { get { return raftLayers != null && raftLayers[0] != null && raftLayers[0].diffuseTexture != null; } }

		/// <summary>
		/// Builds our four layers from the texture layers of a Raft island terrain (Grass, Clean_Sand, Rock_Stylized,
		/// Dirt for the seabed). Returns false if any of them is missing; the procedural textures are used then.
		/// </summary>
		public static bool UseRaftTextures(TerrainLayer[] source)
		{
			if (source == null) return false;
			Func<string, TerrainLayer> find = key => source.FirstOrDefault(l => l != null && l.diffuseTexture != null && l.diffuseTexture.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
			TerrainLayer dirt = find("Dirt"), sand = find("Sand"), grass = find("Grass"), rock = find("Rock");
			if (dirt == null || sand == null || grass == null || rock == null) return false;
			raftLayers = new[] { Copy("CI_Seabed", dirt, 8f), Copy("CI_Sand", sand, 8f), Copy("CI_Grass", grass, 8f), Copy("CI_Rock", rock, 10f) };
			Debug.Log("[CUSTOM ISLANDS] Using Raft's terrain textures: " + string.Join(", ", raftLayers.Select(l => l.diffuseTexture.name).ToArray()));
			return true;
		}

		static TerrainLayer Copy(string name, TerrainLayer from, float tile)
		{
			return new TerrainLayer
			{
				name = name,
				diffuseTexture = from.diffuseTexture,
				normalMapTexture = from.normalMapTexture,
				normalScale = 1f,
				tileSize = new Vector2(tile, tile),
				smoothness = 0f,
				metallic = 0f,
			};
		}

		/// <summary>Swaps the layers of an already painted terrain (e.g. when Raft's textures arrive after the editor opened).</summary>
		public static void RefreshLayers(Terrain terrain)
		{
			if (terrain == null) return;
			TerrainData data = terrain.terrainData;
			if (data.terrainLayers != null && data.terrainLayers.Length == LayerCount) data.terrainLayers = Layers;
			ApplyMaterial(terrain);
		}

		/// <summary>Raft's textures come with normal maps; use the standard terrain shader when Raft includes it.</summary>
		static void ApplyMaterial(Terrain terrain)
		{
			if (!HasRaftTextures) return;
			if (terrainMaterial == null)
			{
				Shader s = Shader.Find("Nature/Terrain/Standard");
				if (s == null) return;
				terrainMaterial = new Material(s) { name = "CI_Terrain" };
			}
			terrain.materialTemplate = terrainMaterial;
		}

		public static TerrainLayer[] Layers
		{
			get
			{
				if (HasRaftTextures) return raftLayers;
				if (layers == null || layers[0] == null)
				{
					layers = new[]
					{
						MakeLayer("CI_Seabed", new Color(0.55f, 0.52f, 0.40f), new Color(0.42f, 0.44f, 0.36f), 6f),
						MakeLayer("CI_Sand",   new Color(0.86f, 0.79f, 0.60f), new Color(0.76f, 0.68f, 0.50f), 5f),
						MakeLayer("CI_Grass",  new Color(0.36f, 0.55f, 0.22f), new Color(0.24f, 0.42f, 0.15f), 7f),
						MakeLayer("CI_Rock",   new Color(0.52f, 0.50f, 0.47f), new Color(0.36f, 0.34f, 0.32f), 9f),
					};
				}
				return layers;
			}
		}

		/// <summary>Assigns the layers and paints the whole terrain automatically (pixels with mask > 0.5 are kept).</summary>
		public static void Setup(Terrain terrain, float waterLevelWorldY, float[,] mask = null)
		{
			TerrainData data = terrain.terrainData;
			EnsureLayers(data, AlphamapResolution);
			ApplyMaterial(terrain);
			Paint(terrain, waterLevelWorldY, new RectInt(0, 0, data.alphamapWidth, data.alphamapHeight), mask);
		}

		/// <summary>Applies a block of saved paint (from IslandFile.GetAlphamapBlock) to the whole terrain.</summary>
		public static void ApplySaved(Terrain terrain, float[,,] maps)
		{
			TerrainData data = terrain.terrainData;
			EnsureLayers(data, maps.GetLength(0));
			ApplyMaterial(terrain);
			data.SetAlphamaps(0, 0, maps);
		}

		static void EnsureLayers(TerrainData data, int resolution)
		{
			if (data.alphamapResolution != resolution) data.alphamapResolution = resolution;
			if (data.terrainLayers == null || data.terrainLayers.Length != LayerCount || data.terrainLayers[0] != Layers[0]) data.terrainLayers = Layers;
		}

		/// <summary>Repaints the alphamap pixels covering the given world-space rectangle (x/z min and max).</summary>
		public static void PaintWorldArea(Terrain terrain, float waterLevelWorldY, Vector3 worldMin, Vector3 worldMax, float[,] mask = null)
		{
			TerrainData data = terrain.terrainData;
			if (data.terrainLayers == null || data.terrainLayers.Length < LayerCount) { Setup(terrain, waterLevelWorldY, mask); return; }
			RectInt area;
			if (WorldToAlphamapRect(terrain, worldMin, worldMax, out area))
				Paint(terrain, waterLevelWorldY, area, mask);
		}

		public static bool WorldToAlphamapRect(Terrain terrain, Vector3 worldMin, Vector3 worldMax, out RectInt area)
		{
			TerrainData data = terrain.terrainData;
			Vector3 origin = terrain.transform.position;
			int res = data.alphamapWidth;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((worldMin.x - origin.x) / data.size.x * res), 0, res - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt((worldMin.z - origin.z) / data.size.z * res), 0, res - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((worldMax.x - origin.x) / data.size.x * res), 0, res - 1);
			int z1 = Mathf.Clamp(Mathf.CeilToInt((worldMax.z - origin.z) / data.size.z * res), 0, res - 1);
			area = new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
			return x1 >= x0 && z1 >= z0;
		}

		static void Paint(Terrain terrain, float waterLevelWorldY, RectInt area, float[,] mask)
		{
			TerrainData data = terrain.terrainData;
			float[,,] maps = mask != null ? data.GetAlphamaps(area.x, area.y, area.width, area.height)
				: new float[area.height, area.width, LayerCount];
			var w = new float[LayerCount];
			for (int z = 0; z < area.height; z++)
				for (int x = 0; x < area.width; x++)
				{
					if (mask != null && mask[area.y + z, area.x + x] > 0.5f) continue; // painted by hand
					AutoWeights(terrain, waterLevelWorldY, area.x + x, area.y + z, w);
					for (int l = 0; l < LayerCount; l++) maps[z, x, l] = w[l];
				}
			data.SetAlphamaps(area.x, area.y, maps);
		}

		/// <summary>Automatic layer weights for one alphamap pixel.</summary>
		public static void AutoWeights(Terrain terrain, float waterLevelWorldY, int px, int pz, float[] result)
		{
			TerrainData data = terrain.terrainData;
			int res = data.alphamapWidth;
			float nx = (px + 0.5f) / res, nz = (pz + 0.5f) / res;
			float height = terrain.transform.position.y + data.GetInterpolatedHeight(nx, nz) - waterLevelWorldY; // metres above water
			float slope = data.GetSteepness(nx, nz); // degrees

			float seabed = 1f - Mathf.InverseLerp(-2.5f, -0.5f, height);
			float grass = Mathf.InverseLerp(2.5f, 5f, height);
			float sand = Mathf.Max(0f, 1f - seabed - grass);
			float rock = Mathf.InverseLerp(28f, 42f, slope);

			float keep = 1f - rock;
			result[Seabed] = seabed * keep;
			result[Sand] = sand * keep;
			result[Grass] = grass * keep;
			result[Rock] = rock;
		}

		static TerrainLayer MakeLayer(string name, Color light, Color dark, float tileSize)
		{
			const int size = 128;
			var tex = new Texture2D(size, size, TextureFormat.RGB24, true) { name = name, wrapMode = TextureWrapMode.Repeat };
			var pixels = new Color[size * size];
			float seed = name.GetHashCode() % 1000;
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
				{
					// Two octaves of tileable noise (sampled on a torus so the texture repeats seamlessly)
					float n = 0.65f * TileableNoise(x, y, size, 4f, seed) + 0.35f * TileableNoise(x, y, size, 16f, seed + 37f);
					pixels[y * size + x] = Color.Lerp(dark, light, n);
				}
			tex.SetPixels(pixels);
			tex.Apply(true);

			return new TerrainLayer
			{
				name = name,
				diffuseTexture = tex,
				tileSize = new Vector2(tileSize, tileSize),
			};
		}

		static float TileableNoise(int x, int y, int size, float frequency, float seed)
		{
			float ax = x / (float)size * Mathf.PI * 2f, ay = y / (float)size * Mathf.PI * 2f;
			float r = frequency / (Mathf.PI * 2f);
			// Map the 2D torus to 2D noise space via its angles; Perlin is smooth enough for ground textures
			float nx = Mathf.Cos(ax) * r + Mathf.Sin(ay) * r * 0.37f + seed;
			float ny = Mathf.Sin(ax) * r + Mathf.Cos(ay) * r + seed * 0.5f;
			return Mathf.PerlinNoise(nx, ny);
		}
	}
}
