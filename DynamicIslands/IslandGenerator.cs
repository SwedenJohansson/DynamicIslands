using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CommandUndoRedo;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>Settings for one generated island. The same settings (including the seed) always give the same island.</summary>
	public class IslandGenSettings
	{
		public int Seed = 1;
		/// <summary>Rough radius of the land in metres.</summary>
		public float Radius = 120f;
		/// <summary>Height of the highest peak above sea level, in metres.</summary>
		public float Height = 35f;
		/// <summary>0 = smooth round island, 1 = ragged coast and bumpy hills.</summary>
		public float Roughness = 0.5f;
		public int Peaks = 2;
		/// <summary>0 = no objects, 1 = dense vegetation.</summary>
		public float ObjectDensity = 0.5f;

		// The land can reach about 1.9 x Radius with a ragged coast; 250 keeps it inside the 1000 m build area
		public const float MinRadius = 40f, MaxRadius = 250f, MinHeight = 5f, MaxHeight = 120f;
		public const int MaxPeaks = 5;

		public void Clamp()
		{
			Radius = Mathf.Clamp(Radius, MinRadius, MaxRadius);
			Height = Mathf.Clamp(Height, MinHeight, MaxHeight);
			Roughness = Mathf.Clamp01(Roughness);
			Peaks = Mathf.Clamp(Peaks, 1, MaxPeaks);
			ObjectDensity = Mathf.Clamp01(ObjectDensity);
		}
	}

	/// <summary>
	/// Procedural islands for the editor (Discord roadmap 1.5): a noisy coastline around the middle of the build area,
	/// a sandy shelf just above sea level, one or more peaks with rolling hills, and a slope down to the flat seabed
	/// (which stays exactly flat further out, so spawning crops the terrain to the island). Textures are painted
	/// automatically, and objects can be scattered by zone: palms along the shore, bushes and fruit trees inland,
	/// rocks on steep or high ground, corals under water. Generating replaces the island and is one undo step.
	/// </summary>
	public static class IslandGenerator
	{
		public static IslandGenSettings Last = new IslandGenSettings();

		/// <summary>Beach shelf height above sea level (m) that the land profile rises to before the hills start.</summary>
		const float ShelfHeight = 2.5f;

		// Object groups by zone (names from PlaceableCatalog; missing ones are skipped)
		static readonly Regex ShoreObjects = new Regex(@"^(Pickup_Landmark_Tree_Palm \d+|BigPalm\d+)$");
		static readonly Regex InlandObjects = new Regex(@"^(Pickup_Landmark_MangoTree|Pickup_Landmark_BerryBush|Bush2?|Monstera_\d+|Banana_Bush_\d+|Bamboo_\d+|BigPalm\d+|Pickup_Landmark_Tree_Palm \d+)$");
		// (BigRock_Low*_Sand is left out: those are cliff-sized formations that swamp a generated island)
		static readonly Regex RockObjects = new Regex(@"^(Pickup_Landmark_Rock \d+|BigBoulder\d+_Low|SmallBoulder\d+)$");
		static readonly Regex BeachObjects = new Regex(@"^(Log|Pickup_Landmark_Rock \d+|SmallBoulder\d+)$");
		static readonly Regex UnderwaterObjects = new Regex(@"^(Coral\d+|LeafCoral_\d+|TableCoral_\d+|CauliCoral|CylinderCoral_\d+|SpineCoral_\d+|SeaVine3|seavine_tongue)$");

		/// <summary>Generates into the editor terrain (and scatters objects) as one undoable step. Returns how many objects were placed.</summary>
		public static int GenerateInEditor(IslandGenSettings s)
		{
			s.Clamp();
			Last = s;
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			int hres = data.heightmapResolution, ares = data.alphamapResolution;

			// Before-snapshots for undo
			float[,] heightsBefore = data.GetHeights(0, 0, hres, hres);
			float[,,] alphaBefore = data.GetAlphamaps(0, 0, ares, ares);
			float[,] maskBefore = terraineditor.paintMask != null ? (float[,])terraineditor.paintMask.Clone() : null;

			data.SetHeights(0, 0, Heights(s, data.size, hres));
			// A new island starts with automatic texturing everywhere
			if (terraineditor.paintMask == null || terraineditor.paintMask.GetLength(0) != ares) terraineditor.paintMask = new float[ares, ares];
			else Array.Clear(terraineditor.paintMask, 0, terraineditor.paintMask.Length);
			TerrainPainter.Setup(terrain, IslandFile.DefaultWaterLevel, terraineditor.paintMask);

			var group = new CommandGroup();
			group.Add(new TerrainStrokeCommand(data, new RectInt(0, 0, hres, hres), heightsBefore, new RectInt(0, 0, ares, ares), alphaBefore, maskBefore, terraineditor.paintMask));

			// The old objects go (hidden, so undo brings them back), the new ones come
			Transform placed = GameObject.Find("PlacedObjects") != null ? GameObject.Find("PlacedObjects").transform : null;
			if (placed != null)
			{
				if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.ClearTargets(false);
				List<GameObject> old = placed.GetComponentsInChildren<EditorGameObject>(false).Select(e => e.gameObject).ToList();
				foreach (GameObject go in old) go.SetActive(false);
				if (old.Count > 0) group.Add(new ObjectVisibilityCommand(old, false));
			}
			List<GameObject> scattered = placed != null && s.ObjectDensity > 0f ? Scatter(s, terrain, placed) : new List<GameObject>();
			if (scattered.Count > 0) group.Add(new ObjectVisibilityCommand(scattered, true));

			UndoRedoManager.Insert(group);
			Debug.Log(string.Format("[CUSTOM ISLANDS] Generated island: seed {0}, radius {1:F0} m, height {2:F0} m, roughness {3:F2}, {4} peak(s), {5} objects",
				s.Seed, s.Radius, s.Height, s.Roughness, s.Peaks, scattered.Count));
			return scattered.Count;
		}

		/// <summary>Points the editor camera at the middle of the build area from a distance that fits the island.</summary>
		public static void FrameCamera(IslandGenSettings s)
		{
			Terrain terrain = terraineditor.terrain;
			if (terrain == null || Camera.main == null) return;
			Vector3 c = terrain.transform.position + new Vector3(terrain.terrainData.size.x / 2f, IslandFile.DefaultWaterLevel, terrain.terrainData.size.z / 2f);
			Transform cam = Camera.main.transform;
			cam.position = c + new Vector3(0, s.Height + s.Radius * 0.7f, -s.Radius * 1.6f);
			cam.LookAt(c + Vector3.up * (s.Height * 0.3f));
		}

		#region Heights

		/// <summary>Normalised heightmap (0..1 of the terrain height) for a terrain of this size and resolution.</summary>
		public static float[,] Heights(IslandGenSettings s, Vector3 size, int res)
		{
			var rnd = new System.Random(s.Seed);
			// Noise offsets make each seed a different part of the noise field
			Vector2 coastOffset = RandomOffset(rnd), hillOffset = RandomOffset(rnd), warpOffset = RandomOffset(rnd);

			// Peaks somewhere inside the inner half of the island
			var peaks = new List<Vector4>(); // x, z, radius, height
			for (int i = 0; i < s.Peaks; i++)
			{
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
				float d = i == 0 ? (float)rnd.NextDouble() * s.Radius * 0.25f : (0.2f + 0.3f * (float)rnd.NextDouble()) * s.Radius;
				float r = s.Radius * (0.35f + 0.25f * (float)rnd.NextDouble());
				float h = i == 0 ? 1f : 0.45f + 0.45f * (float)rnd.NextDouble(); // the first peak is the highest
				peaks.Add(new Vector4(Mathf.Cos(a) * d, Mathf.Sin(a) * d, r, h));
			}

			float sea = IslandFile.DefaultWaterLevel;
			float step = size.x / (res - 1);
			Vector2 centre = new Vector2(size.x / 2f, size.z / 2f);
			var heights = new float[res, res];
			float coastScale = 1f / Mathf.Max(20f, s.Radius * 0.6f);
			float hillScale = 1f / 45f;

			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					Vector2 p = new Vector2(x * step, z * step) - centre;
					float dist = p.magnitude;
					// Ragged coastline: the radius varies with low-frequency noise (domain-warped a little)
					Vector2 warp = new Vector2(Fbm(p * coastScale + warpOffset, 2), Fbm(p * coastScale + warpOffset + new Vector2(17.3f, 5.1f), 2));
					float coast = Fbm((p + warp * s.Radius * 0.25f * s.Roughness) * coastScale + coastOffset, 4);
					float rr = dist / (s.Radius * (1f + 0.45f * s.Roughness * coast));

					// Land mask: 1 on the island, falling to exactly 0 on the open seabed
					float land = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1.3f, rr));
					if (land <= 0f) { heights[z, x] = 0f; continue; }

					// Profile: seabed (0) up to a shelf just above sea level
					float elevation = Mathf.Lerp(0f, sea + ShelfHeight, land);

					// Peaks and hills, kept back from the shore
					float inland = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.95f, rr));
					if (inland > 0f)
					{
						float mountain = 0f;
						foreach (Vector4 pk in peaks)
						{
							float dx = p.x - pk.x, dz = p.y - pk.y;
							float q = (dx * dx + dz * dz) / (pk.z * pk.z);
							mountain = Mathf.Max(mountain, pk.w * Mathf.Exp(-q * 1.6f));
						}
						float hills = Fbm(p * hillScale + hillOffset, 4); // -1..1
						float bump = mountain * (1f + 0.35f * s.Roughness * hills) + 0.18f * s.Roughness * (hills * 0.5f + 0.5f);
						elevation += inland * Mathf.Max(0f, bump) * (s.Height - ShelfHeight);
					}
					heights[z, x] = Mathf.Clamp01(elevation / size.y);
				}
			return heights;
		}

		static Vector2 RandomOffset(System.Random rnd)
		{
			return new Vector2((float)rnd.NextDouble() * 1000f, (float)rnd.NextDouble() * 1000f);
		}

		/// <summary>Fractal Perlin noise in roughly -1..1.</summary>
		static float Fbm(Vector2 p, int octaves)
		{
			float sum = 0f, amp = 0.5f, norm = 0f;
			for (int i = 0; i < octaves; i++)
			{
				sum += amp * (Mathf.PerlinNoise(p.x, p.y) * 2f - 1f);
				norm += amp;
				p *= 2.03f;
				amp *= 0.5f;
			}
			return sum / norm;
		}

		#endregion

		#region Objects

		/// <summary>Places objects by zone as editor objects (saved with the island). Deterministic for a seed.</summary>
		static List<GameObject> Scatter(IslandGenSettings s, Terrain terrain, Transform placed)
		{
			var result = new List<GameObject>();
			if (!PlaceableCatalog.IsBuilt) { Debug.LogWarning("[CUSTOM ISLANDS] Objects are still loading; generated the terrain without objects"); return result; }
			string[] names = PlaceableCatalog.Names.ToArray();
			string[] shore = names.Where(n => ShoreObjects.IsMatch(n)).ToArray();
			string[] inland = names.Where(n => InlandObjects.IsMatch(n)).ToArray();
			string[] rocks = names.Where(n => RockObjects.IsMatch(n)).ToArray();
			string[] beach = names.Where(n => BeachObjects.IsMatch(n)).ToArray();
			string[] underwater = names.Where(n => UnderwaterObjects.IsMatch(n)).ToArray();

			var rnd = new System.Random(s.Seed * 7919 + 13);
			TerrainData data = terrain.terrainData;
			Vector3 origin = terrain.transform.position;
			Vector3 centre = origin + new Vector3(data.size.x / 2f, 0, data.size.z / 2f);
			float sea = IslandFile.DefaultWaterLevel;

			// About one object per 300 m² of island at full density, capped so the editor stays quick
			int target = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * s.Radius * s.Radius / 300f * s.ObjectDensity), 5, 400);
			var positions = new List<Vector3>();
			for (int attempt = 0; attempt < target * 6 && result.Count < target; attempt++)
			{
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
				float d = Mathf.Sqrt((float)rnd.NextDouble()) * s.Radius * 1.35f;
				Vector3 p = centre + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
				float h = terrain.SampleHeight(p);
				float nx = (p.x - origin.x) / data.size.x, nz = (p.z - origin.z) / data.size.z;
				if (nx < 0 || nx > 1 || nz < 0 || nz > 1) continue;
				float slope = data.GetSteepness(nx, nz);
				float above = h - sea;

				string[] pool; float spacing;
				if (above < -8f || h < 0.5f) continue;                                       // deep water / open seabed
				else if (above < -1.5f) { pool = underwater; spacing = 5f; }                  // shallow water: corals
				else if (above < 0.8f) { if (rnd.NextDouble() < 0.7) continue; pool = beach; spacing = 6f; } // wet sand
				else if (slope > 32f || above > s.Height * 0.8f) { pool = rocks; spacing = 7f; } // steep or high ground
				else if (above < 7f) { pool = rnd.NextDouble() < 0.75 ? shore : beach; spacing = 7f; } // along the shore
				else { pool = rnd.NextDouble() < 0.8 ? inland : rocks; spacing = 6f; }
				if (pool.Length == 0) continue;
				if (positions.Any(q => (q - p).sqrMagnitude < spacing * spacing)) continue;

				string kind = pool[rnd.Next(pool.Length)];
				GameObject go = PlaceableCatalog.Spawn(kind, placed);
				if (go == null) continue;
				p.y = origin.y + h;
				go.transform.position = p;
				go.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
				float scale = 0.85f + 0.3f * (float)rnd.NextDouble();
				go.transform.localScale = go.transform.localScale * scale;
				go.AddComponent<EditorGameObject>().GameObjectName = kind;
				positions.Add(p);
				result.Add(go);
			}
			return result;
		}

		#endregion
	}
}
