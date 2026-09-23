using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Automatic terrain texturing: seabed below the water, sand at the shore, grass above, rock on steep slopes.
	/// Textures are generated in code (no asset bundle needed). The paint is derived purely from the heights,
	/// so it doesn't need to be saved: the editor and in-world spawning both repaint from the heightmap.
	/// </summary>
	public static class TerrainPainter
	{
		const int Seabed = 0, Sand = 1, Grass = 2, Rock = 3;
		const int AlphamapResolution = 512;

		static TerrainLayer[] layers;

		public static TerrainLayer[] Layers
		{
			get
			{
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

		/// <summary>Assigns the layers to the terrain and paints all of it.</summary>
		public static void Setup(Terrain terrain, float waterLevelWorldY)
		{
			TerrainData data = terrain.terrainData;
			data.alphamapResolution = AlphamapResolution;
			data.terrainLayers = Layers;
			Paint(terrain, waterLevelWorldY, new RectInt(0, 0, data.alphamapWidth, data.alphamapHeight));
		}

		/// <summary>Repaints the alphamap pixels covering the given world-space rectangle (x/z min and max).</summary>
		public static void PaintWorldArea(Terrain terrain, float waterLevelWorldY, Vector3 worldMin, Vector3 worldMax)
		{
			TerrainData data = terrain.terrainData;
			if (data.terrainLayers == null || data.terrainLayers.Length < 4) { Setup(terrain, waterLevelWorldY); return; }
			Vector3 origin = terrain.transform.position;
			int res = data.alphamapWidth;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((worldMin.x - origin.x) / data.size.x * res), 0, res - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt((worldMin.z - origin.z) / data.size.z * res), 0, res - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((worldMax.x - origin.x) / data.size.x * res), 0, res - 1);
			int z1 = Mathf.Clamp(Mathf.CeilToInt((worldMax.z - origin.z) / data.size.z * res), 0, res - 1);
			if (x1 < x0 || z1 < z0) return;
			Paint(terrain, waterLevelWorldY, new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1));
		}

		static void Paint(Terrain terrain, float waterLevelWorldY, RectInt area)
		{
			TerrainData data = terrain.terrainData;
			int res = data.alphamapWidth;
			float baseY = terrain.transform.position.y;
			var maps = new float[area.height, area.width, 4];
			for (int z = 0; z < area.height; z++)
			{
				float nz = (area.y + z + 0.5f) / res;
				for (int x = 0; x < area.width; x++)
				{
					float nx = (area.x + x + 0.5f) / res;
					float height = baseY + data.GetInterpolatedHeight(nx, nz) - waterLevelWorldY; // metres above the water
					float slope = data.GetSteepness(nx, nz); // degrees

					float seabed = 1f - Mathf.InverseLerp(-2.5f, -0.5f, height);
					float grass = Mathf.InverseLerp(2.5f, 5f, height);
					float sand = Mathf.Max(0f, 1f - seabed - grass);
					float rock = Mathf.InverseLerp(28f, 42f, slope);

					float keep = 1f - rock;
					maps[z, x, Seabed] = seabed * keep;
					maps[z, x, Sand] = sand * keep;
					maps[z, x, Grass] = grass * keep;
					maps[z, x, Rock] = rock;
				}
			}
			data.SetAlphamaps(area.x, area.y, maps);
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
