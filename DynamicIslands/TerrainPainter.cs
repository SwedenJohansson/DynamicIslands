using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>Which island style a terrain is textured with (see TerrainPainter.Styles).</summary>
	public class IslandStyleTag : MonoBehaviour
	{
		public int Style;
	}

	/// <summary>
	/// Terrain texturing. Four layers ("slots"): seabed, shore, ground and steep. By default every pixel is painted
	/// automatically from height and slope; pixels painted by hand in the editor are marked in a paint mask
	/// (1 = hand-painted) and are left alone by the automatic painter. Weights and mask are saved in the
	/// .island file (format 2); older files fall back to automatic texturing.
	///
	/// Island styles (roadmap 1.6) decide which of Raft's ground textures fill the four slots: tropical (Raft's
	/// ordinary islands), snowy (Temperance), desert (Caravan Island), forest (Balboa) and volcanic (darkened
	/// desert and Temperance rock). The textures are borrowed from those islands' terrains while the object
	/// catalog has their scenes loaded; a style whose textures are missing falls back to tropical.
	/// </summary>
	public static class TerrainPainter
	{
		public const int Seabed = 0, Sand = 1, Grass = 2, Rock = 3;
		public const int LayerCount = 4;
		public static readonly string[] LayerNames = { "Seabed", "Sand", "Grass", "Rock" };
		const int AlphamapResolution = 512;

		#region Styles

		public class Style
		{
			public string Name;
			/// <summary>Diffuse texture names for the seabed / shore / ground / steep slots.</summary>
			public string[] Textures;
			/// <summary>Button labels for the slots in the editor.</summary>
			public string[] Labels;
			/// <summary>Colour multiplied into each slot's texture (white = unchanged).</summary>
			public Color[] Tints;
			public float Tile = 8f;
		}

		public const int Tropical = 0, Snowy = 1, Desert = 2, Forest = 3, Volcanic = 4;

		static readonly Color W = Color.white;
		public static readonly Style[] Styles =
		{
			new Style { Name = "Tropical", Textures = new[] { "Dirt_basecolor", "Clean_Sand_basecolor", "Grass_basecolor", "Rock_Stylized_basecolor" },
				Labels = new[] { "Seabed", "Sand", "Grass", "Rock" }, Tints = new[] { W, W, W, W } },
			new Style { Name = "Snowy", Textures = new[] { "TP_Rock_basecolor", "TP_Snow_w_Rocks_basecolor", "TP_Snow_basecolor", "TP_Rock_basecolor" },
				Labels = new[] { "Seabed", "Rocky", "Snow", "Rock" }, Tints = new[] { new Color(0.7f, 0.72f, 0.75f), W, W, W }, Tile = 10f },
			new Style { Name = "Desert", Textures = new[] { "CaravanIsland_GravelGrayGroundTex_basecolor", "CaravanIsland_SandGroundTex_basecolor", "CaravanIsland_GravelGroundTex_basecolor", "Red_Rock_Side_basecolor" },
				Labels = new[] { "Gravel", "Sand", "Ground", "Red rock" }, Tints = new[] { W, W, W, W }, Tile = 10f },
			new Style { Name = "Forest", Textures = new[] { "Balboa_Dirt_basecolor", "Clean_Sand_basecolor", "Balboa_Grass_basecolor", "Rock_Stylized_basecolor" },
				Labels = new[] { "Seabed", "Sand", "Grass", "Rock" }, Tints = new[] { W, W, W, W } },
			new Style { Name = "Volcanic", Textures = new[] { "CaravanIsland_GravelGrayGroundTex_basecolor", "CaravanIsland_GravelGrayGroundTex_basecolor", "CaravanIsland_RockGroundTex_basecolor", "TP_Rock_basecolor" },
				Labels = new[] { "Seabed", "Cinders", "Ash", "Basalt" },
				Tints = new[] { new Color(0.42f, 0.4f, 0.38f), new Color(0.3f, 0.29f, 0.28f), new Color(0.38f, 0.35f, 0.33f), new Color(0.45f, 0.3f, 0.26f) }, Tile = 10f },
		};

		public static int StyleIndex(string name)
		{
			if (string.IsNullOrEmpty(name)) return Tropical;
			int i = Array.FindIndex(Styles, s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			return i < 0 ? Tropical : i;
		}

		public static string StyleName(int style) { return Styles[Mathf.Clamp(style, 0, Styles.Length - 1)].Name; }

		/// <summary>Editor button label for a paint slot in this style ("Snow", "Red rock"...).</summary>
		public static string SlotLabel(int style, int slot) { return Styles[Mathf.Clamp(style, 0, Styles.Length - 1)].Labels[slot]; }

		public static int StyleOf(Terrain terrain)
		{
			IslandStyleTag tag = terrain != null ? terrain.GetComponent<IslandStyleTag>() : null;
			return tag != null ? tag.Style : Tropical;
		}

		/// <summary>Sets the terrain's style and swaps its texture layers (paint weights stay as they are).</summary>
		public static void SetStyle(Terrain terrain, int style)
		{
			IslandStyleTag tag = terrain.GetComponent<IslandStyleTag>() ?? terrain.gameObject.AddComponent<IslandStyleTag>();
			tag.Style = Mathf.Clamp(style, 0, Styles.Length - 1);
			RefreshLayers(terrain);
		}

		/// <summary>True if Raft's textures for this style were found (otherwise it falls back to tropical).</summary>
		public static bool HasStyle(int style) { return BuildStyle(style) != null; }

		#endregion

		#region Raft's textures

		static TerrainLayer[] layers; // procedural fallback
		/// <summary>Every ground texture layer seen on Raft's islands, by diffuse texture name, with its footstep type.</summary>
		static readonly Dictionary<string, KeyValuePair<TerrainLayer, SO_TerrainType>> raftTextures = new Dictionary<string, KeyValuePair<TerrainLayer, SO_TerrainType>>();
		static readonly Dictionary<int, TerrainLayer[]> styleLayers = new Dictionary<int, TerrainLayer[]>();
		static readonly Dictionary<int, SO_TerrainTypeGroup> styleTypes = new Dictionary<int, SO_TerrainTypeGroup>();
		static SO_TerrainTypeGroup defaultTypes;
		static Material terrainMaterial;

		/// <summary>True once Raft's own tropical ground textures were borrowed from one of its islands (see PlaceableCatalog).</summary>
		public static bool HasRaftTextures { get { return BuildStyle(Tropical) != null; } }

		/// <summary>
		/// Remembers the texture layers of a Raft island terrain (and their footstep types) for the styles. Returns true
		/// if anything new was found.
		/// </summary>
		public static bool UseRaftTextures(TerrainLayer[] source, TerrainIdentifier identifier = null)
		{
			if (source == null) return false;
			int added = 0;
			for (int i = 0; i < source.Length; i++)
			{
				TerrainLayer l = source[i];
				if (l == null || l.diffuseTexture == null || raftTextures.ContainsKey(l.diffuseTexture.name)) continue;
				SO_TerrainType type = null;
				try { if (identifier != null && identifier.terrainTypeGroup != null) type = identifier.terrainTypeGroup.GetTerrainType(i); } catch { }
				raftTextures[l.diffuseTexture.name] = new KeyValuePair<TerrainLayer, SO_TerrainType>(l, type);
				added++;
			}
			if (added > 0)
			{
				// Styles may be complete now
				styleLayers.Clear();
				Debug.Log("[CUSTOM ISLANDS] Borrowed " + added + " of Raft's ground textures (styles available: " +
					string.Join(", ", Enumerable.Range(0, Styles.Length).Where(HasStyle).Select(StyleName).ToArray()) + ")");
			}
			return added > 0;
		}

		/// <summary>The four layers for a style, or null if Raft's textures for it haven't been seen.</summary>
		static TerrainLayer[] BuildStyle(int style)
		{
			TerrainLayer[] cached;
			if (styleLayers.TryGetValue(style, out cached)) return cached;
			Style s = Styles[style];
			if (s.Textures.Any(t => !raftTextures.ContainsKey(t))) return null;
			var result = new TerrainLayer[LayerCount];
			var types = new List<SO_TerrainType>();
			for (int slot = 0; slot < LayerCount; slot++)
			{
				var src = raftTextures[s.Textures[slot]];
				float tile = slot == Rock ? s.Tile * 1.25f : s.Tile;
				result[slot] = Copy("CI_" + s.Name + "_" + slot, src.Key, tile, s.Tints[slot]);
				types.Add(src.Value ?? ScriptableObject.CreateInstance<SO_TerrainType>());
			}
			var group = ScriptableObject.CreateInstance<SO_TerrainTypeGroup>();
			group.name = "CI_TerrainTypes_" + s.Name;
			group.groupName = "Custom Islands";
			group.terrainTypes = types;
			styleTypes[style] = group;
			styleLayers[style] = result;
			return result;
		}

		static TerrainLayer Copy(string name, TerrainLayer from, float tile, Color tint)
		{
			return new TerrainLayer
			{
				name = name,
				diffuseTexture = tint == Color.white ? from.diffuseTexture : Tinted(from.diffuseTexture, tint),
				normalMapTexture = from.normalMapTexture,
				normalScale = 1f,
				tileSize = new Vector2(tile, tile),
				smoothness = 0f,
				metallic = 0f,
			};
		}

		static Material tintMaterial;

		/// <summary>
		/// A darkened / tinted copy of a (non-readable) texture, made on the GPU: blit through a tinting material into a
		/// render texture and read that back. Used for the volcanic style, which Raft has no textures of its own for.
		/// </summary>
		static Texture2D Tinted(Texture2D source, Color tint)
		{
			try
			{
				if (tintMaterial == null) tintMaterial = new Material(Shader.Find("Sprites/Default"));
				tintMaterial.color = tint;
				var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
				Graphics.Blit(source, rt, tintMaterial);
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = rt;
				var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true) { name = source.name + "_tinted", wrapMode = TextureWrapMode.Repeat };
				copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
				copy.Apply(true, true); // mipmaps, then free the CPU copy
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(rt);
				return copy;
			}
			catch (Exception e)
			{
				Debug.LogWarning("[CUSTOM ISLANDS] Could not tint " + source.name + ": " + e.Message);
				return source;
			}
		}

		/// <summary>Swaps the layers of an already painted terrain to its style's (e.g. when Raft's textures arrive after the editor opened).</summary>
		public static void RefreshLayers(Terrain terrain)
		{
			if (terrain == null) return;
			TerrainData data = terrain.terrainData;
			if (data.terrainLayers != null && data.terrainLayers.Length == LayerCount) data.terrainLayers = LayersFor(StyleOf(terrain));
			ApplyMaterial(terrain);
		}

		/// <summary>
		/// Raft's player looks up a TerrainIdentifier on any terrain it walks on (footstep sounds, friction) and throws
		/// every frame without one. Uses the style's footstep types from Raft when borrowed, otherwise neutral defaults.
		/// </summary>
		public static void EnsureIdentifier(Terrain terrain)
		{
			int style = StyleOf(terrain);
			SO_TerrainTypeGroup group;
			if (BuildStyle(style) == null || !styleTypes.TryGetValue(style, out group))
			{
				if (BuildStyle(Tropical) == null || !styleTypes.TryGetValue(Tropical, out group))
				{
					if (defaultTypes == null)
					{
						defaultTypes = ScriptableObject.CreateInstance<SO_TerrainTypeGroup>();
						defaultTypes.name = "CI_TerrainTypes_Default";
						defaultTypes.groupName = "Custom Islands";
						defaultTypes.terrainTypes = new List<SO_TerrainType>();
						for (int i = 0; i < LayerCount; i++) defaultTypes.terrainTypes.Add(ScriptableObject.CreateInstance<SO_TerrainType>());
					}
					group = defaultTypes;
				}
			}
			TerrainIdentifier id = terrain.GetComponent<TerrainIdentifier>() ?? terrain.gameObject.AddComponent<TerrainIdentifier>();
			id.terrainTypeGroup = group;
		}

		/// <summary>Raft's textures come with normal maps; use the standard terrain shader when Raft includes it.</summary>
		static void ApplyMaterial(Terrain terrain)
		{
			EnsureIdentifier(terrain);
			if (!HasRaftTextures) return;
			if (terrainMaterial == null)
			{
				Shader s = Shader.Find("Nature/Terrain/Standard");
				if (s == null) return;
				terrainMaterial = new Material(s) { name = "CI_Terrain" };
			}
			terrain.materialTemplate = terrainMaterial;
		}

		/// <summary>The tropical layers (or procedural ones when Raft's textures aren't available).</summary>
		public static TerrainLayer[] Layers { get { return LayersFor(Tropical); } }

		/// <summary>The layers for a style: Raft's textures, else tropical, else procedural ones.</summary>
		public static TerrainLayer[] LayersFor(int style)
		{
			TerrainLayer[] l = BuildStyle(style) ?? BuildStyle(Tropical);
			if (l != null) return l;
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

		#endregion

		/// <summary>Assigns the layers and paints the whole terrain automatically (pixels with mask > 0.5 are kept).</summary>
		public static void Setup(Terrain terrain, float waterLevelWorldY, float[,] mask = null)
		{
			TerrainData data = terrain.terrainData;
			EnsureLayers(terrain, AlphamapResolution);
			ApplyMaterial(terrain);
			Paint(terrain, waterLevelWorldY, new RectInt(0, 0, data.alphamapWidth, data.alphamapHeight), mask);
		}

		/// <summary>Applies a block of saved paint (from IslandFile.GetAlphamapBlock) to the whole terrain.</summary>
		public static void ApplySaved(Terrain terrain, float[,,] maps)
		{
			TerrainData data = terrain.terrainData;
			EnsureLayers(terrain, maps.GetLength(0));
			ApplyMaterial(terrain);
			data.SetAlphamaps(0, 0, maps);
		}

		static void EnsureLayers(Terrain terrain, int resolution)
		{
			TerrainData data = terrain.terrainData;
			TerrainLayer[] wanted = LayersFor(StyleOf(terrain));
			if (data.alphamapResolution != resolution) data.alphamapResolution = resolution;
			if (data.terrainLayers == null || data.terrainLayers.Length != LayerCount || data.terrainLayers[0] != wanted[0]) data.terrainLayers = wanted;
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
