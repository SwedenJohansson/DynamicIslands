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
		/// <summary>Island style (TerrainPainter.Styles): ground textures and which objects are scattered. Volcanic islands get a cone with a crater.</summary>
		public int Style = TerrainPainter.Tropical;
		/// <summary>The land's layout (IslandShapes): one round island, a ring around a lagoon, several islets...</summary>
		public int Shape = IslandShapes.Round;

		// The land can reach about 1.9 x Radius with a ragged coast; 250 keeps it inside the 1000 m build area
		public const float MinRadius = 40f, MaxRadius = 250f, MinHeight = 5f, MaxHeight = 120f;
		public const int MaxPeaks = 5;

		public void Clamp()
		{
			// (map types may go smaller than the editor's sliders: a sandbar, a low atoll)
			Radius = Mathf.Clamp(Radius, 12f, MaxRadius);
			Height = Mathf.Clamp(Height, 2f, MaxHeight);
			Shape = Mathf.Clamp(Shape, 0, IslandShapes.Names.Length - 1);
			Roughness = Mathf.Clamp01(Roughness);
			Peaks = Mathf.Clamp(Peaks, 1, MaxPeaks);
			ObjectDensity = Mathf.Clamp01(ObjectDensity);
			Style = Mathf.Clamp(Style, 0, TerrainPainter.Styles.Length - 1);
		}
	}

	/// <summary>Layouts of generated land.</summary>
	public static class IslandShapes
	{
		/// <summary>Round: one island with peaks. Atoll: a ring of low land around a shallow lagoon. Archipelago: several
		/// islets on a shallow shelf. Stacks: steep rock pillars rising from shallow water. Plateau: a flat-topped mesa
		/// with cliffs and one ramp up. Marsh: low, bumpy land with pools of water.</summary>
		public const int Round = 0, Atoll = 1, Archipelago = 2, Stacks = 3, Plateau = 4, Marsh = 5;
		public static readonly string[] Names = { "Round", "Atoll", "Archipelago", "Sea stacks", "Plateau", "Marsh" };
		public static readonly string[] Hints =
		{
			"One island with hills and peaks", "A ring of low land around a shallow lagoon", "Several islets on a shallow shelf",
			"Steep rock pillars rising from shallow water", "A flat-topped mesa with cliffs and one ramp up", "Low, bumpy land with pools of water",
		};
	}

	/// <summary>Undo step for changing the island style (part of generating an island).</summary>
	public class StyleCommand : ICommand
	{
		readonly int before, after;
		public StyleCommand(int before, int after) { this.before = before; this.after = after; }
		public void Execute() { DynamicIslands.SetEditorStyle(after); }
		public void UnExecute() { DynamicIslands.SetEditorStyle(before); }
	}

	/// <summary>
	/// Procedural islands for the editor (Discord roadmap 1.5): a noisy coastline around the middle of the build area,
	/// a sandy shelf just above sea level, one or more peaks with rolling hills, and a slope down to the flat seabed
	/// (which stays exactly flat further out, so spawning crops the terrain to the island). Textures are painted
	/// automatically, and objects can be scattered by zone: palms along the shore, bushes and fruit trees inland,
	/// rocks on steep or high ground, corals under water. Generating replaces the island and is one undo step.
	/// Each island style (TerrainPainter.Styles) scatters its own objects; volcanic islands get a cone with a crater.
	/// </summary>
	public static class IslandGenerator
	{
		public static IslandGenSettings Last = new IslandGenSettings();

		/// <summary>Beach shelf height above sea level (m) that the land profile rises to before the hills start.</summary>
		const float ShelfHeight = 2.5f;

		/// <summary>Which catalog objects a style scatters in each zone (names from PlaceableCatalog; missing ones are skipped).</summary>
		class ZoneObjects
		{
			public Regex Shore, Inland, Rocks, Beach, Underwater;
		}

		// Corals, plus now and then a sunken barrel or container, and scrap to dive for (roadmap 1.6 "enhancing the ocean floor")
		static readonly Regex Corals = new Regex(@"^(Coral\d+|LeafCoral_\d+|TableCoral_\d+|CauliCoral|CylinderCoral_\d+|SpineCoral_\d+|SeaVine3|seavine_tongue|Reef_Barrel\d+|Reef_Container|Pickup_Landmark_Scrap \d+_OceanBottom)$");

		/// <summary>Corals and reef things of the object list (map types dress sunken islands with them).</summary>
		internal static string[] CoralNames() { return PlaceableCatalog.CoreNames.Where(n => Corals.IsMatch(n) && !n.StartsWith("Pickup_")).ToArray(); }

		// Indexed like TerrainPainter.Styles. (BigRock_Low*_Sand is left out: those are cliff-sized formations that swamp a generated island)
		static readonly ZoneObjects[] StyleObjects =
		{
			new ZoneObjects // Tropical
			{
				Shore = new Regex(@"^(Pickup_Landmark_Tree_Palm \d+|BigPalm\d+)$"),
				Inland = new Regex(@"^(Pickup_Landmark_MangoTree|Pickup_Landmark_BerryBush|Bush2?|Monstera_\d+|Banana_Bush_\d+|Bamboo_\d+|BigPalm\d+|Pickup_Landmark_Tree_Palm \d+)$"),
				Rocks = new Regex(@"^(Pickup_Landmark_Rock \d+|BigBoulder\d+_Low|SmallBoulder\d+)$"),
				Beach = new Regex(@"^(Log|Pickup_Landmark_Rock \d+|SmallBoulder\d+)$"),
				Underwater = Corals,
			},
			new ZoneObjects // Snowy
			{
				Shore = new Regex(@"^(TP_SnowDrift0\d|TP_SmallRock0\d|TP_IceShore_Small\d)$"),
				Inland = new Regex(@"^(TP_PineTreeSnowy|TP_SnowDrift0\d|TP_PineTreeSnowy)$"),
				Rocks = new Regex(@"^(TP_BigRock0[2-4]|TP_SmallRock0\d|TP_StalagmiteCluster0\d_Snow|Pickup_Landmark_(Rock|Iron) \d+)$"),
				Beach = new Regex(@"^(TP_SmallRock0\d|TP_SnowDrift0\d|TP_Moontown_Barrel0\d)$"),
				Underwater = new Regex(@"^(TP_SmallRock0\d|SeaVine3)$"),
			},
			new ZoneObjects // Desert
			{
				Shore = new Regex(@"^(DesertFern_\d+|SmallBush_\d+|Cactus\w+)$"),
				Inland = new Regex(@"^(Cactus\w+|SmallBushyTree_\d+|BigBush_\d+|DesertFern_\d+|Pickup_Landmark_PineappleLandmark|CaravanIsland_(Yellow|Brown)Grass)$"),
				Rocks = new Regex(@"^(BigSharpRock_\d+|CaravanIsland_SmallRock_\d+|Pickup_Landmark_(Rock|Copper) \d+)$"),
				Beach = new Regex(@"^(CaravanIsland_SmallRock_\d+|Pickup_Landmark_Sand_Caravan)$"),
				Underwater = Corals,
			},
			new ZoneObjects // Forest
			{
				Shore = new Regex(@"^(Balboa_Bush_\d+ Variant|SmallRock_\d+|BirchTree_Small\d)$"),
				Inland = new Regex(@"^(BirchTree_\w+|PineTree_\w+|Pickup_Landmark_Tree_(Pine|Birch)|Balboa_(Big)?Bush_\d+ Variant|TreeLog_\d+|Tree_Stump)$"),
				Rocks = new Regex(@"^(BigRock_\d+|SmallRock_\d+|Pickup_Landmark_(Rock|Iron|Copper|Clay) \d+)$"),
				Beach = new Regex(@"^(Pickup_Landmark_Sand|SmallRock_\d+|TreeLog_\d+)$"),
				Underwater = Corals,
			},
			new ZoneObjects // Volcanic
			{
				// (no Temperance rocks anywhere: they have snow on them)
				Shore = new Regex(@"^(CaravanIsland_SmallRock_\d+|Pickup_Landmark_Rock \d+)$"),
				Inland = new Regex(@"^(DesertFern_\d+|SmallBush_\d+|BigSharpRock_\d+|Pickup_Landmark_(Iron|Copper) \d+)$"),
				Rocks = new Regex(@"^(BigSharpRock_\d+|CaravanIsland_SmallRock_\d+|Pickup_Landmark_(Iron|Copper|Rock) \d+)$"),
				Beach = new Regex(@"^(CaravanIsland_SmallRock_\d+)$"),
				Underwater = new Regex(@"^(TableCoral_\d+|SeaVine3|Reef_Barrel\d+|Pickup_Landmark_Scrap \d+_OceanBottom)$"),
			},
		};

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

			int styleBefore = DynamicIslands.currentStyle;
			DynamicIslands.SetEditorStyle(s.Style); // before painting, so the style's textures go on
			data.SetHeights(0, 0, Heights(s, data.size, hres));
			// A new island starts with automatic texturing everywhere
			if (terraineditor.paintMask == null || terraineditor.paintMask.GetLength(0) != ares) terraineditor.paintMask = new float[ares, ares];
			else Array.Clear(terraineditor.paintMask, 0, terraineditor.paintMask.Length);
			TerrainPainter.Setup(terrain, IslandFile.DefaultWaterLevel, terraineditor.paintMask);

			var group = new CommandGroup();
			group.Add(new StyleCommand(styleBefore, s.Style)); // first in, so undo restores the old style last
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
			Debug.Log(string.Format("[CUSTOM ISLANDS] Generated island: seed {0}, radius {1:F0} m, height {2:F0} m, roughness {3:F2}, {4} peak(s), {5} style, {6} objects",
				s.Seed, s.Radius, s.Height, s.Roughness, s.Peaks, TerrainPainter.StyleName(s.Style), scattered.Count));
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
			switch (s.Shape)
			{
				case IslandShapes.Atoll: return Grid(size, res, Atoll(s));
				case IslandShapes.Archipelago: return Grid(size, res, Archipelago(s));
				case IslandShapes.Stacks: return Grid(size, res, Stacks(s));
				case IslandShapes.Plateau: return Grid(size, res, Plateau(s));
			}
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
			bool volcanic = s.Style == TerrainPainter.Volcanic, marsh = s.Shape == IslandShapes.Marsh;
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
						for (int i = 0; i < peaks.Count; i++)
						{
							Vector4 pk = peaks[i];
							float dx = p.x - pk.x, dz = p.y - pk.y;
							float q = (dx * dx + dz * dz) / (pk.z * pk.z);
							float v;
							if (volcanic)
							{
								// A straight-sided cone, and a crater in the main one
								float r = Mathf.Sqrt(q);
								v = pk.w * Mathf.Pow(Mathf.Max(0f, 1f - r * 0.85f), 1.4f);
								if (i == 0 && r < 0.2f) v -= pk.w * 0.45f * (1f - r / 0.2f);
							}
							else v = pk.w * Mathf.Exp(-q * 1.6f);
							mountain = Mathf.Max(mountain, v);
						}
						float hills = Fbm(p * hillScale + hillOffset, 4); // -1..1
						float bump = mountain * (1f + 0.35f * s.Roughness * hills) + 0.18f * s.Roughness * (hills * 0.5f + 0.5f);
						elevation += inland * Mathf.Max(0f, bump) * (s.Height - ShelfHeight);
						// A marsh has pools of shallow water in its low land
						if (marsh) elevation = Mathf.Max(sea - 1.5f, elevation - inland * Mathf.Max(0f, Fbm(p / 22f + warpOffset * 1.7f, 3) - 0.05f) * 14f);
					}
					heights[z, x] = Mathf.Clamp01(elevation / size.y);
				}
			return heights;
		}

		#region Shapes (IslandShapes)

		const float Sea = IslandFile.DefaultWaterLevel;

		/// <summary>A heightmap from a function giving metres above the terrain's base for a point relative to the middle.</summary>
		static float[,] Grid(Vector3 size, int res, Func<Vector2, float> elevationAt)
		{
			float step = size.x / (res - 1);
			Vector2 centre = new Vector2(size.x / 2f, size.z / 2f);
			var heights = new float[res, res];
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					heights[z, x] = Mathf.Clamp01(elevationAt(new Vector2(x * step, z * step) - centre) / size.y);
			return heights;
		}

		/// <summary>Smooth 0..1 as x goes from a to b (either way round).</summary>
		static float SS(float a, float b, float x) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x)); }

		/// <summary>A ring of low land (dunes up to the height setting) around a shallow lagoon, with a channel or two into it.</summary>
		static Func<Vector2, float> Atoll(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed);
			Vector2 coastOff = RandomOffset(rnd), hillOff = RandomOffset(rnd), floorOff = RandomOffset(rnd);
			float ring = s.Radius * 0.75f, width = s.Radius * (0.16f + 0.1f * s.Roughness);
			float coastScale = 1f / Mathf.Max(20f, s.Radius * 0.5f);
			return p =>
			{
				float dist = p.magnitude;
				float coast = Fbm(p * coastScale + coastOff, 3);
				float d = Mathf.Abs(dist - ring) / Mathf.Max(4f, width * (1f + 0.6f * coast));
				float top = (1f - SS(0.55f, 1.25f, d)) * SS(-0.5f, -0.3f, coast);
				float dunes = top * Mathf.Max(0f, Fbm(p / 30f + hillOff, 3) * 0.5f + 0.5f) * Mathf.Max(0f, s.Height - ShelfHeight);
				float floor = dist < ring ? Sea - 2f - 2.5f * (Fbm(p / 40f + floorOff, 2) * 0.5f + 0.5f) : Mathf.Lerp(0f, Sea - 2f, 1f - SS(1f, 3.2f, d));
				return Mathf.Lerp(floor, Sea + ShelfHeight, top) + dunes;
			};
		}

		/// <summary>Archipelago islets: x, z (relative to the middle), radius, height. The first is the biggest, near the middle.</summary>
		public static List<Vector4> Islets(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed * 101 + 3);
			int n = 3 + rnd.Next(4);
			var list = new List<Vector4>();
			for (int i = 0; i < n * 12 && list.Count < n; i++)
			{
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
				float d = list.Count == 0 ? (float)rnd.NextDouble() * s.Radius * 0.2f : s.Radius * (0.4f + 0.45f * (float)rnd.NextDouble());
				float r = s.Radius * (list.Count == 0 ? 0.28f : 0.14f + 0.12f * (float)rnd.NextDouble());
				var c = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
				if (list.Any(o => (new Vector2(o.x, o.y) - c).magnitude < (o.z + r) * 1.2f)) continue; // water between them
				float h = Mathf.Max(ShelfHeight + 2f, s.Height * (list.Count == 0 ? 1f : 0.35f + 0.5f * (float)rnd.NextDouble()));
				list.Add(new Vector4(c.x, c.y, r, h));
			}
			return list;
		}

		/// <summary>Several islets (Islets) on a shallow shelf that joins them under water.</summary>
		static Func<Vector2, float> Archipelago(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed);
			Vector2 coastOff = RandomOffset(rnd), hillOff = RandomOffset(rnd);
			List<Vector4> islets = Islets(s);
			return p =>
			{
				float shelf = 1f - SS(0.95f, 1.4f, p.magnitude / s.Radius);
				float floor = Mathf.Lerp(0f, Sea - 2.5f, shelf), best = floor;
				float coast = Fbm(p / Mathf.Max(15f, s.Radius * 0.25f) + coastOff, 3);
				foreach (Vector4 o in islets)
				{
					float q = (p - new Vector2(o.x, o.y)).magnitude / (o.z * (1f + 0.35f * s.Roughness * coast));
					float land = 1f - SS(0.7f, 1.25f, q);
					if (land <= 0f) continue;
					float hill = Mathf.Exp(-q * q * 2.2f) * (1f + 0.3f * s.Roughness * Fbm(p / 25f + hillOff, 3));
					best = Mathf.Max(best, Mathf.Lerp(floor, Sea + ShelfHeight, land) + land * Mathf.Max(0f, hill) * (o.w - ShelfHeight));
				}
				return best;
			};
		}

		/// <summary>Sea stacks: x, z (relative to the middle), radius, height above the sea. The first is the tallest.</summary>
		public static List<Vector4> StackSpots(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed * 97 + 5);
			int n = 3 + rnd.Next(5);
			var list = new List<Vector4>();
			for (int i = 0; i < n * 12 && list.Count < n; i++)
			{
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
				float d = list.Count == 0 ? (float)rnd.NextDouble() * s.Radius * 0.25f : s.Radius * (0.2f + 0.55f * (float)rnd.NextDouble());
				float r = Mathf.Clamp(s.Radius * (0.09f + 0.09f * (float)rnd.NextDouble()), 6f, 26f);
				var c = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
				if (list.Any(o => (new Vector2(o.x, o.y) - c).magnitude < (o.z + r) * 1.4f)) continue;
				list.Add(new Vector4(c.x, c.y, r, s.Height * (list.Count == 0 ? 1f : 0.45f + 0.5f * (float)rnd.NextDouble())));
			}
			return list;
		}

		/// <summary>Steep rock pillars with flat tops (StackSpots), a rocky beach at their feet, on a shallow shelf.</summary>
		static Func<Vector2, float> Stacks(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed);
			Vector2 edgeOff = RandomOffset(rnd), capOff = RandomOffset(rnd);
			List<Vector4> stacks = StackSpots(s);
			return p =>
			{
				float plate = 1f - SS(0.8f, 1.25f, p.magnitude / s.Radius);
				float floor = Mathf.Lerp(0f, Sea - 2.5f, plate), e = floor;
				float edge = Fbm(p / 12f + edgeOff, 3);
				foreach (Vector4 o in stacks)
				{
					float q = (p - new Vector2(o.x, o.y)).magnitude / (o.z * (1f + 0.25f * s.Roughness * edge));
					if (q > 2f) continue;
					float side = 1f - SS(0.8f, 1f, q);
					float cap = o.w * (1f + 0.04f * Fbm(p / 6f + capOff, 2));
					e = Mathf.Max(e, Mathf.Lerp(floor, Sea + cap, side));
					e = Mathf.Max(e, Mathf.Lerp(floor, Sea + 0.8f, 1f - SS(1f, 1.9f, q))); // the rocky beach at its foot
				}
				return e;
			};
		}

		/// <summary>The plateau's ramp: the direction (radians, from +x towards +z) it goes up from the shore.</summary>
		public static float RampAngle(IslandGenSettings s) { return (float)new System.Random(s.Seed * 53 + 1).NextDouble() * Mathf.PI * 2f; }

		/// <summary>A flat-topped mesa (the height setting) with cliffs, a beach around it, and one ramp up from the shore.</summary>
		static Func<Vector2, float> Plateau(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed);
			Vector2 coastOff = RandomOffset(rnd), topOff = RandomOffset(rnd);
			float ramp = RampAngle(s);
			Vector2 rampDir = new Vector2(Mathf.Cos(ramp), Mathf.Sin(ramp));
			float coastScale = 1f / Mathf.Max(20f, s.Radius * 0.6f);
			return p =>
			{
				float coast = Fbm(p * coastScale + coastOff, 4);
				float rr = p.magnitude / (s.Radius * (1f + 0.3f * s.Roughness * coast));
				float land = 1f - SS(0.75f, 1.3f, rr);
				if (land <= 0f) return 0f;
				float baseLevel = Mathf.Lerp(0f, Sea + ShelfHeight, land);
				float rise = s.Height - ShelfHeight;
				float cliff = 1f - SS(0.5f, 0.62f, rr);
				float e = baseLevel + cliff * (rise + 0.4f * Fbm(p / 10f + topOff, 2));
				float along = Vector2.Dot(p, rampDir), across = Mathf.Abs(p.x * rampDir.y - p.y * rampDir.x);
				if (along > 0f)
					e = Mathf.Max(e, baseLevel + rise * SS(1.05f, 0.45f, rr) * (1f - SS(5f, 9f, across)));
				return e;
			};
		}

		#endregion

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

		/// <summary>One object the scatter wants to place (position relative to the terrain's corner, y = ground height).</summary>
		struct Placement
		{
			public string Name;
			public Vector3 Position;
			public float Yaw, Scale;
		}

		/// <summary>
		/// Where objects go, by zone. heightAt / slopeAt take terrain-local x, z (metres) and return the ground height
		/// (metres above the terrain's base) and the slope in degrees. Deterministic for a seed.
		/// </summary>
		static List<Placement> PlanObjects(IslandGenSettings s, Vector3 size, Func<float, float, float> heightAt, Func<float, float, float> slopeAt)
		{
			var result = new List<Placement>();
			if (!PlaceableCatalog.IsBuilt || s.ObjectDensity <= 0f) return result;
			string[] names = PlaceableCatalog.CoreNames.ToArray(); // the same set every time, so a seed always gives the same island
			ZoneObjects zones = StyleObjects[s.Style];
			string[] shore = names.Where(n => zones.Shore.IsMatch(n)).ToArray();
			string[] inland = names.Where(n => zones.Inland.IsMatch(n)).ToArray();
			string[] rocks = names.Where(n => zones.Rocks.IsMatch(n)).ToArray();
			string[] beach = names.Where(n => zones.Beach.IsMatch(n)).ToArray();
			string[] underwater = names.Where(n => zones.Underwater.IsMatch(n)).ToArray();

			var rnd = new System.Random(s.Seed * 7919 + 13);
			Vector3 centre = new Vector3(size.x / 2f, 0, size.z / 2f);
			float sea = IslandFile.DefaultWaterLevel;

			// About one object per 300 m² of island at full density, capped so the editor stays quick
			int target = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * s.Radius * s.Radius / 300f * s.ObjectDensity), 5, 400);
			var positions = new List<Vector3>();
			for (int attempt = 0; attempt < target * 6 && result.Count < target; attempt++)
			{
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
				float d = Mathf.Sqrt((float)rnd.NextDouble()) * s.Radius * 1.35f;
				Vector3 p = centre + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
				if (p.x < 0 || p.x > size.x || p.z < 0 || p.z > size.z) continue;
				float h = heightAt(p.x, p.z);
				float slope = slopeAt(p.x, p.z);
				float above = h - sea;

				string[] pool; float spacing, maxSize; // maxSize: bigger objects are scaled down (some of Raft's rocks are cliff-sized)
				if (above < -8f || h < 0.5f) continue;                                       // deep water / open seabed
				else if (above < -1.5f) { pool = underwater; spacing = 5f; maxSize = 6f; }                  // shallow water: corals
				else if (above < 0.8f) { if (rnd.NextDouble() < 0.7) continue; pool = beach; spacing = 6f; maxSize = 5f; } // wet sand
				else if (slope > 32f || above > s.Height * 0.8f) { pool = rocks; spacing = 7f; maxSize = 10f; } // steep or high ground
				else if (above < 7f) { pool = rnd.NextDouble() < 0.75 ? shore : beach; spacing = 7f; maxSize = 18f; } // along the shore
				else { pool = rnd.NextDouble() < 0.8 ? inland : rocks; spacing = 6f; maxSize = 22f; }
				if (pool.Length == 0) continue;
				if (positions.Any(q => (q - p).sqrMagnitude < spacing * spacing)) continue;

				string kind = pool[rnd.Next(pool.Length)];
				float yaw = (float)rnd.NextDouble() * 360f;
				float scale = 0.85f + 0.3f * (float)rnd.NextDouble();
				float objectSize = PlaceableCatalog.ApproxSize(kind);
				if (objectSize * scale > maxSize) scale = Mathf.Max(0.15f, maxSize / objectSize);
				p.y = h;
				positions.Add(p);
				result.Add(new Placement { Name = kind, Position = p, Yaw = yaw, Scale = scale });
			}
			return result;
		}

		/// <summary>Places objects by zone as editor objects (saved with the island).</summary>
		static List<GameObject> Scatter(IslandGenSettings s, Terrain terrain, Transform placed)
		{
			var result = new List<GameObject>();
			if (!PlaceableCatalog.IsBuilt) { Debug.LogWarning("[CUSTOM ISLANDS] Objects are still loading; generated the terrain without objects"); return result; }
			TerrainData data = terrain.terrainData;
			Vector3 origin = terrain.transform.position;
			foreach (Placement pl in PlanObjects(s, data.size,
				(x, z) => terrain.SampleHeight(origin + new Vector3(x, 0, z)),
				(x, z) => data.GetSteepness(x / data.size.x, z / data.size.z)))
			{
				GameObject go = PlaceableCatalog.Spawn(pl.Name, placed);
				if (go == null) continue;
				go.transform.position = origin + pl.Position;
				go.transform.rotation = Quaternion.Euler(0, pl.Yaw, 0);
				go.transform.localScale = go.transform.localScale * pl.Scale;
				go.AddComponent<EditorGameObject>().GameObjectName = pl.Name;
				result.Add(go);
			}
			return result;
		}

		#endregion

		#region Islands generated while sailing

		/// <summary>The editor's build area, which generated island files use too.</summary>
		internal static readonly Vector3 BuildArea = new Vector3(1000f, 600f, 1000f);
		internal const int BuildResolution = 513;

		/// <summary>
		/// A complete island file from settings, without the editor: heights, objects (the object catalog must be
		/// built) and style; textures are painted automatically when it spawns.
		/// </summary>
		public static IslandFile CreateFile(IslandGenSettings s, string name)
		{
			s.Clamp();
			float[,] heights = Heights(s, BuildArea, BuildResolution);
			float step = BuildArea.x / (BuildResolution - 1);
			Func<float, float, float> heightAt = (x, z) => SampleHeights(heights, BuildResolution, step, x, z) * BuildArea.y;
			Func<float, float, float> slopeAt = (x, z) =>
			{
				float dx = (heightAt(x + step, z) - heightAt(x - step, z)) / (2f * step);
				float dz = (heightAt(x, z + step) - heightAt(x, z - step)) / (2f * step);
				return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
			};
			var file = new IslandFile
			{
				Name = name,
				TerrainSize = BuildArea,
				HeightmapResolution = BuildResolution,
				Heights = heights,
				Style = s.Style == TerrainPainter.Tropical ? "" : TerrainPainter.StyleName(s.Style),
			};
			foreach (Placement pl in PlanObjects(s, BuildArea, heightAt, slopeAt))
			{
				GameObject proto = PlaceableCatalog.Get(pl.Name);
				Vector3 baseScale = proto != null ? proto.transform.localScale : Vector3.one;
				file.Objects.Add(new IslandObject { Name = pl.Name, Position = pl.Position, EulerRotation = new Vector3(0, pl.Yaw, 0), Scale = baseScale * pl.Scale });
			}
			return file;
		}

		/// <summary>Bilinear height (0..1) at terrain-local x, z.</summary>
		internal static float SampleHeights(float[,] h, int res, float step, float x, float z)
		{
			float fx = Mathf.Clamp(x / step, 0, res - 1.001f), fz = Mathf.Clamp(z / step, 0, res - 1.001f);
			int x0 = (int)fx, z0 = (int)fz;
			float tx = fx - x0, tz = fz - z0;
			return Mathf.Lerp(Mathf.Lerp(h[z0, x0], h[z0, x0 + 1], tx), Mathf.Lerp(h[z0 + 1, x0], h[z0 + 1, x0 + 1], tx), tz);
		}

		/// <summary>Random settings for an island generated while sailing, in one of the allowed styles.</summary>
		public static IslandGenSettings RandomSettings(System.Random rnd, int[] styles)
		{
			return new IslandGenSettings
			{
				Seed = rnd.Next(1, 999999),
				Radius = 60f + (float)rnd.NextDouble() * 110f, // fits between Raft's own islands more often
				Height = 15f + (float)rnd.NextDouble() * 55f,
				Roughness = 0.3f + (float)rnd.NextDouble() * 0.6f,
				Peaks = 1 + rnd.Next(3),
				ObjectDensity = 0.4f + (float)rnd.NextDouble() * 0.4f,
				Style = styles != null && styles.Length > 0 ? styles[rnd.Next(styles.Length)] : TerrainPainter.Tropical,
			};
		}

		#endregion
	}
}
