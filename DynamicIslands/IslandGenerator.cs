using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CommandUndoRedo;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Settings for one generated island. The same settings (including the seed) always give the same island.
	/// Every setting has a neutral default, so islands made from only the older settings (map types, random islands
	/// while sailing) still come out as before in spirit.
	/// </summary>
	public class IslandGenSettings
	{
		public int Seed = 1;
		/// <summary>Radius of the land (m): for the round layouts the coast runs about this far from the middle; the others fit their ring, islets or stacks into it.</summary>
		public float Radius = 90f;
		/// <summary>Height of the highest point above sea level (m): the tallest peak is exactly this high.</summary>
		public float Height = 45f;
		/// <summary>0 = smooth hills, 1 = bumpy, ridged hills.</summary>
		public float Roughness = 0.5f;
		public int Peaks = 2;
		/// <summary>0 = no objects, 1 = dense: what the object sliders left at -1 follow (map types, random islands).</summary>
		public float ObjectDensity = 0.5f;
		/// <summary>Island style (TerrainPainter.Styles): ground textures and which objects are scattered. Volcanic islands get a cone with a crater.</summary>
		public int Style = TerrainPainter.Tropical;
		/// <summary>The land's layout (IslandShapes): one round island, a ring around a lagoon, several islets...</summary>
		public int Shape = IslandShapes.Round;

		// Coast and outline (0..1 unless said)
		/// <summary>How ragged the coastline is; -1 = follow Roughness.</summary>
		public float Coast = -1f;
		/// <summary>Inlets cut into the coast.</summary>
		public float Bays;
		/// <summary>Width of the sandy band between the sea and the hills.</summary>
		public float BeachWidth = 0.5f;
		/// <summary>Share of the coast that drops steeply into the sea instead of a beach.</summary>
		public float Cliffs;
		/// <summary>1 = round, up to 3 = three times as long as wide (the area stays the same); along StretchAngle (degrees).</summary>
		public float Stretch = 1f, StretchAngle;

		// Land
		/// <summary>0 = full, rounded hills; 0.3 = as generated; 1 = sharp spires over flat lowland.</summary>
		public float PeakShape = 0.3f;
		public float Terraces, Lakes, Valleys, Erosion;

		// The sea around it
		/// <summary>SeaFloorDeep: the island rises from a sea floor 160 m down like Raft's own (a shallow shelf, then a
		/// drop-off into the deep); SeaFloorShallow: a flat seabed 20 m down all around (islands before the deep sea floor).</summary>
		public int SeaFloor;
		public const int SeaFloorDeep = 0, SeaFloorShallow = 1;
		/// <summary>Width of the shallow water around the island (round layouts).</summary>
		public float Shelf = 0.35f;
		/// <summary>Deep sea floor: 0 = a long, gentle slope down (Raft's big islands), 1 = a sheer wall (its small islands).</summary>
		public float DropOff = 0.5f;
		/// <summary>SeabedSand, SeabedRocky or SeabedReef.</summary>
		public int Seabed;
		public const int SeabedSand = 0, SeabedRocky = 1, SeabedReef = 2;

		// Objects, each 0..1 (-1 = follow ObjectDensity); Clusters: 0 = spread evenly, 1 = in groves and fields
		public float Trees = -1f, Bushes = -1f, Rocks = -1f, BeachThings = -1f, Harvest = -1f;
		public float Clusters = 0.35f;
		// Under water, each 0..1 where 0.5 = as dense as around Raft's islands (-1 = like Raft, or none if ObjectDensity is 0):
		// corals and sea plants, rocks, things to collect (stones, clay, sand, scrap, ores, giant clams), sunken barrels
		public float Water = -1f, SeaRocks = -1f, SeaFinds = -1f, Sunken = -1f;

		// Content: creature spots and loot boxes
		public int Hostiles, Friendly, SeaLife;
		/// <summary>Kinds of hostile creatures (AI types, comma separated), "" = the style's own.</summary>
		public string HostileKinds = "";
		/// <summary>A creature stat preset (ObjectProps.Presets): Easy, Normal, Hard, Boss.</summary>
		public string Difficulty = "Normal";
		public int Loot;
		/// <summary>Loot tiers the boxes are rolled between (1 = planks and plastic ... 5 = titanium and batteries).</summary>
		public int LootMin = 1, LootMax = 3;
		/// <summary>Loot boxes tucked in next to trees, bushes and rocks instead of out in the open.</summary>
		public bool LootHidden;

		// "Randomize existing": start from one of Raft's islands' own ground (RaftIslands) instead of a layout
		/// <summary>Scene of the Raft island whose ground is the start ("" = a layout).</summary>
		public string Source = "";
		/// <summary>Bumps added to the Raft island's land, and wobble of its coast (0..1).</summary>
		public float SourceRoughen, SourceWobble;
		public bool Mirror;

		// The land can reach well past Radius with a ragged coast and stretch; the edge of the 1000 m build area is always flat seabed
		public const float MinRadius = 8f, MaxRadius = 280f, MinHeight = 3f, MaxHeight = 160f, MaxStretch = 3f;
		public const int MaxPeaks = 5, MaxCreatureSpots = 24, MaxLoot = 40;

		public float CoastAmount { get { return Coast < 0f ? Roughness : Coast; } }

		public void Clamp()
		{
			// (map types may go smaller than the editor's sliders: a sandbar, a low atoll)
			Radius = Mathf.Clamp(Radius, MinRadius, MaxRadius);
			Height = Mathf.Clamp(Height, 2f, MaxHeight);
			Shape = Mathf.Clamp(Shape, 0, IslandShapes.Names.Length - 1);
			Roughness = Mathf.Clamp01(Roughness);
			Peaks = Mathf.Clamp(Peaks, 1, MaxPeaks);
			ObjectDensity = Mathf.Clamp01(ObjectDensity);
			Style = Mathf.Clamp(Style, 0, TerrainPainter.Styles.Length - 1);
			if (Coast >= 0f) Coast = Mathf.Clamp01(Coast);
			Bays = Mathf.Clamp01(Bays); BeachWidth = Mathf.Clamp01(BeachWidth); Cliffs = Mathf.Clamp01(Cliffs);
			Stretch = Mathf.Clamp(Stretch, 1f, MaxStretch); StretchAngle = Mathf.Repeat(StretchAngle, 180f);
			PeakShape = Mathf.Clamp01(PeakShape); Terraces = Mathf.Clamp01(Terraces); Lakes = Mathf.Clamp01(Lakes); Valleys = Mathf.Clamp01(Valleys); Erosion = Mathf.Clamp01(Erosion);
			Shelf = Mathf.Clamp01(Shelf); Seabed = Mathf.Clamp(Seabed, 0, 2);
			SeaFloor = Mathf.Clamp(SeaFloor, 0, 1); DropOff = Mathf.Clamp01(DropOff);
			Clusters = Mathf.Clamp01(Clusters);
			Hostiles = Mathf.Clamp(Hostiles, 0, MaxCreatureSpots); Friendly = Mathf.Clamp(Friendly, 0, MaxCreatureSpots); SeaLife = Mathf.Clamp(SeaLife, 0, MaxCreatureSpots);
			Loot = Mathf.Clamp(Loot, 0, MaxLoot);
			LootMin = Mathf.Clamp(LootMin, 1, 5); LootMax = Mathf.Clamp(LootMax, LootMin, 5);
			SourceRoughen = Mathf.Clamp01(SourceRoughen); SourceWobble = Mathf.Clamp01(SourceWobble);
			if (HostileKinds == null) HostileKinds = "";
			if (Difficulty == null || !ObjectProps.Presets.Any(p => p.Key == Difficulty)) Difficulty = "Normal";
			if (Source == null) Source = "";
		}

		public IslandGenSettings Copy() { return (IslandGenSettings)MemberwiseClone(); }

		/// <summary>An object slider's value: its own, or (at -1) one that follows ObjectDensity.</summary>
		public float Amount(float own) { return own >= 0f ? Mathf.Clamp01(own) : ObjectDensity * 0.45f; }

		/// <summary>An under-water slider's value: its own, or (at -1) as around Raft's islands (none on an island without objects).</summary>
		public float AmountSea(float own) { return own >= 0f ? Mathf.Clamp01(own) : ObjectDensity > 0.01f ? 0.5f : 0f; }

		/// <summary>Terrain height (m above its base) of the sea for these settings.</summary>
		public float WaterLevel { get { return SeaFloor == SeaFloorShallow ? IslandFile.DefaultWaterLevel : IslandFile.DeepWaterLevel; } }
		public bool Deep { get { return SeaFloor != SeaFloorShallow; } }

		static FieldInfo[] Fields { get { return typeof(IslandGenSettings).GetFields(BindingFlags.Public | BindingFlags.Instance); } }

		/// <summary>"name=value" lines (the generator's saved presets).</summary>
		public string ToText()
		{
			var sb = new StringBuilder();
			foreach (FieldInfo f in Fields)
			{
				object v = f.GetValue(this);
				sb.Append(f.Name).Append('=').Append(v is float ? ((float)v).ToString("0.###", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture)).Append('\n');
			}
			return sb.ToString();
		}

		/// <summary>Settings from ToText's lines; unknown or missing names keep their defaults.</summary>
		public static IslandGenSettings FromText(string text)
		{
			var s = new IslandGenSettings();
			foreach (string raw in (text ?? "").Split('\n'))
			{
				string line = raw.Trim();
				int eq = line.IndexOf('=');
				if (eq <= 0) continue;
				FieldInfo f = typeof(IslandGenSettings).GetField(line.Substring(0, eq), BindingFlags.Public | BindingFlags.Instance);
				if (f == null) continue;
				string v = line.Substring(eq + 1);
				try
				{
					if (f.FieldType == typeof(float)) f.SetValue(s, float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture));
					else if (f.FieldType == typeof(int)) f.SetValue(s, int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture));
					else if (f.FieldType == typeof(bool)) f.SetValue(s, bool.Parse(v));
					else if (f.FieldType == typeof(string)) f.SetValue(s, v);
				}
				catch { }
			}
			s.Clamp();
			return s;
		}
	}

	/// <summary>Layouts of generated land.</summary>
	public static class IslandShapes
	{
		/// <summary>Round: one island with peaks. Atoll: a ring of low land around a shallow lagoon. Archipelago: several
		/// islets on a shallow shelf. Stacks: steep rock pillars rising from shallow water. Plateau: a flat-topped mesa
		/// with cliffs and one ramp up. Marsh: low, bumpy land with pools of water. Crescent: a curved island around a
		/// sheltered bay (like Raft's "Cresent"). Twin peaks: two tall peaks with a saddle between (like Raft's).</summary>
		public const int Round = 0, Atoll = 1, Archipelago = 2, Stacks = 3, Plateau = 4, Marsh = 5, Crescent = 6, TwinPeaks = 7;
		public static readonly string[] Names = { "Round", "Atoll", "Archipelago", "Sea stacks", "Plateau", "Marsh", "Crescent", "Twin peaks" };
		public static readonly string[] Hints =
		{
			"One island with hills and peaks", "A ring of low land around a shallow lagoon", "Several islets on a shallow shelf",
			"Steep rock pillars rising from shallow water", "A flat-topped mesa with cliffs and one ramp up", "Low, bumpy land with pools of water",
			"A curved island around a sheltered bay", "Two tall peaks with a saddle between them",
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

	/// <summary>Undo step for changing the sea level of the island being edited (generating on a deep or shallow sea floor).</summary>
	public class WaterLevelCommand : ICommand
	{
		readonly float before, after;
		public WaterLevelCommand(float before, float after) { this.before = before; this.after = after; }
		public void Execute() { DynamicIslands.SetEditorWaterLevel(after); }
		public void UnExecute() { DynamicIslands.SetEditorWaterLevel(before); }
	}

	/// <summary>What one generation made (the window's status line and the tests).</summary>
	public class GenReport
	{
		public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
		public int Creatures, Chests;
		/// <summary>Objects the planner wanted before the cap (MaxObjects) scaled them down.</summary>
		public int Wanted;
		public float LandArea, Top, Seconds;
		public float LandLength, LandWidth;

		public int Nature { get { return Counts.Values.Sum(); } }
		public int Count(string key) { int n; return Counts.TryGetValue(key, out n) ? n : 0; }

		public string Describe()
		{
			var parts = IslandGenerator.Categories.Where(c => Count(c) > 0).Select(c => Count(c) + " " + IslandGenerator.CategoryLabel(c)).ToList();
			if (Creatures > 0) parts.Add(Creatures + " creature spot" + (Creatures == 1 ? "" : "s"));
			if (Chests > 0) parts.Add(Chests + " loot box" + (Chests == 1 ? "" : "es"));
			return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "no objects";
		}
	}

	/// <summary>
	/// Procedural islands for the editor (Discord roadmap 1.5). The land comes from a layout (a noisy coast with a
	/// beach, peaks and hills; a ring; islets; stacks; a mesa; a marsh; a crescent; twin peaks) or from one of Raft's
	/// own islands' ground, then passes shape it further: peak shape, valleys, height (the top is exactly the highest
	/// point asked for), terraces, erosion, lakes, and the seabed. Textures are painted automatically; objects are
	/// scattered by zone (under water, wet sand, beach, land, rocky ground) and kind (trees, bushes, rocks, beach
	/// things, harvestables, water plants), on a spatial grid so the densest settings make a jungle; creature spots
	/// and tiered loot boxes are placed last. Generating replaces the island and is one undo step. Deterministic.
	/// </summary>
	public static class IslandGenerator
	{
		public static IslandGenSettings Last = new IslandGenSettings();
		/// <summary>What the last generation made.</summary>
		public static GenReport LastReport = new GenReport();

		/// <summary>Beach shelf height above sea level (m) that the land profile rises to before the hills start.</summary>
		public const float ShelfHeight = 2.5f;
		/// <summary>Most objects one island gets (a denser island's objects are scaled down to this; see README for the measured costs).</summary>
		public const int MaxObjects = 12000;

		/// <summary>Terrain height (m above its base) of the sea for the island being generated: every entry point sets it from its settings (UseSea).</summary>
		static float Sea = IslandFile.DefaultWaterLevel;
		/// <summary>Deep sea floor (like Raft's): a shelf, then a drop-off to the floor at the terrain's base.</summary>
		static bool deep;
		/// <summary>Deep sea floor: metres over which the drop-off falls from the shelf's edge to the floor.</summary>
		static float dropWidth = 100f;
		static Vector2 seaOffA, seaOffB;

		static void UseSea(IslandGenSettings s)
		{
			Sea = s.WaterLevel;
			deep = s.Deep;
			// (bigger islands have longer slopes, as on Raft)
			dropWidth = DropWidthOf(s);
			var rnd = new System.Random(s.Seed * 211 + 29);
			seaOffA = RandomOffset(rnd); seaOffB = RandomOffset(rnd);
		}

		#region Objects of each style

		/// <summary>Which catalog objects a style scatters, by kind (names from PlaceableCatalog's core objects; missing ones are skipped).</summary>
		class StylePools
		{
			public Regex ShoreTrees, Trees, Bushes, Rocks, Beach, LandHarvest, SeaHarvest, Water;
		}

		static readonly Regex Corals = new Regex(@"^(Coral\d+|LeafCoral_\d+|TableCoral_\d+|CauliCoral|CylinderCoral_\d+|SpineCoral_\d+|SeaVine3|seavine_tongue|SeaVine3_klump)$");
		static readonly Regex SeaOres = new Regex(@"^Pickup_Landmark_(Iron \d+|Copper \d+|Scrap \d+_OceanBottom)$");
		static readonly Regex Nothing = new Regex(@"^$^");

		/// <summary>Corals and reef things of the object list (map types dress sunken islands with them).</summary>
		internal static string[] CoralNames() { return PlaceableCatalog.CoreNames.Where(n => (Corals.IsMatch(n) || Regex.IsMatch(n, @"^Reef_(Barrel\d+|Container)$")) && !n.StartsWith("Pickup_")).ToArray(); }

		// Indexed like TerrainPainter.Styles. (BigRock_Low*_Sand is left out: those are cliff-sized formations that swamp a generated island)
		static readonly StylePools[] Pools =
		{
			new StylePools // Tropical
			{
				ShoreTrees = new Regex(@"^(Pickup_Landmark_Tree_Palm \d+|BigPalm\d+)$"),
				Trees = new Regex(@"^(Pickup_Landmark_Tree_Palm \d+|BigPalm\d+|Pickup_Landmark_MangoTree|Bamboo_\d+)$"),
				Bushes = new Regex(@"^(Bush2?|Monstera_\d+|Banana_Bush_\d+|Bamboo_\d+)$"),
				Rocks = new Regex(@"^(BigBoulder\d+_Low|SmallBoulder\d+)$"),
				Beach = new Regex(@"^(Log|SmallBoulder\d+)$"),
				LandHarvest = new Regex(@"^Pickup_Landmark_(Rock \d+|BerryBush|Clay \d+|Sand)$"),
				SeaHarvest = SeaOres, Water = Corals,
			},
			new StylePools // Snowy
			{
				ShoreTrees = Nothing,
				Trees = new Regex(@"^TP_PineTreeSnowy$"),
				Bushes = new Regex(@"^TP_SnowDrift0\d$"),
				Rocks = new Regex(@"^(TP_BigRock0[2-4]|TP_SmallRock0\d|TP_StalagmiteCluster0\d_Snow)$"),
				Beach = new Regex(@"^(TP_SmallRock0\d|TP_IceShore_Small\d|TP_SnowDrift0\d)$"),
				LandHarvest = new Regex(@"^Pickup_Landmark_(Rock \d+|Clay \d+)$"),
				SeaHarvest = SeaOres,
				Water = new Regex(@"^(TP_SmallRock0\d|SeaVine3|SeaVine3_klump|seavine_tongue)$"),
			},
			new StylePools // Desert
			{
				ShoreTrees = new Regex(@"^SmallBushyTree_\d+$"),
				Trees = new Regex(@"^(Cactus\w+|SmallBushyTree_\d+)$"),
				Bushes = new Regex(@"^(BigBush_\d+|SmallBush_\d+|DesertFern_\d+|CaravanIsland_(Yellow|Green|Brown)Grass)$"),
				Rocks = new Regex(@"^(BigSharpRock_\d+|CaravanIsland_SmallRock_\d+)$"),
				Beach = new Regex(@"^(CaravanIsland_SmallRock_\d+|Log)$"),
				LandHarvest = new Regex(@"^Pickup_Landmark_(PineappleLandmark|Sand_Caravan|Rock \d+|Clay \d+)$"),
				SeaHarvest = SeaOres, Water = Corals,
			},
			new StylePools // Forest
			{
				ShoreTrees = new Regex(@"^(BirchTree_Small\d|PineTree_Small\d)$"),
				Trees = new Regex(@"^(BirchTree_\w+|PineTree_\w+|Pickup_Landmark_Tree_(Pine|Birch))$"),
				Bushes = new Regex(@"^(Balboa_(Big)?Bush_\d+ Variant|Tree_Stump|TreeLog_\d+)$"),
				Rocks = new Regex(@"^(BigRock_\d+|SmallRock_\d+)$"),
				Beach = new Regex(@"^(SmallRock_\d+|TreeLog_\d+|Log)$"),
				LandHarvest = new Regex(@"^Pickup_Landmark_(Rock \d+|Clay \d+|Sand|BerryBush)$"),
				SeaHarvest = SeaOres, Water = Corals,
			},
			new StylePools // Volcanic (no Temperance rocks anywhere: they have snow on them)
			{
				ShoreTrees = Nothing,
				Trees = new Regex(@"^SmallBushyTree_\d+$"),
				Bushes = new Regex(@"^(DesertFern_\d+|SmallBush_\d+|CaravanIsland_BrownGrass)$"),
				Rocks = new Regex(@"^(BigSharpRock_\d+|CaravanIsland_SmallRock_\d+)$"),
				Beach = new Regex(@"^CaravanIsland_SmallRock_\d+$"),
				LandHarvest = new Regex(@"^Pickup_Landmark_(Iron \d+|Copper \d+|Rock \d+)$"),
				SeaHarvest = new Regex(@"^Pickup_Landmark_(Scrap \d+_OceanBottom|Iron \d+)$"),
				Water = new Regex(@"^(TableCoral_\d+|SeaVine3|SeaVine3_klump)$"),
			},
		};

		/// <summary>The kinds of scattered objects, in the order they are placed (big things first, then what fills the gaps).</summary>
		public const string CatTrees = "trees", CatRocks = "rocks", CatBushes = "bushes", CatHarvest = "harvest", CatBeach = "beach";
		/// <summary>Under water (placed from Raft's measured islands, RaftUnderwater): corals and sea plants, rocks, things to collect, sunken barrels.</summary>
		public const string CatWater = "water", CatSeaRocks = "searocks", CatSeaFinds = "seafinds", CatSunken = "sunken";
		public static readonly string[] LandCategories = { CatTrees, CatRocks, CatBushes, CatHarvest, CatBeach };
		public static readonly string[] SeaCategories = { CatSeaRocks, CatSunken, CatSeaFinds, CatWater };
		public static readonly string[] Categories = { CatTrees, CatRocks, CatBushes, CatHarvest, CatBeach, CatWater, CatSeaRocks, CatSeaFinds, CatSunken };

		public static string CategoryLabel(string cat)
		{
			switch (cat)
			{
				case CatTrees: return "trees";
				case CatRocks: return "rocks";
				case CatBushes: return "bushes and plants";
				case CatHarvest: return "harvestables";
				case CatBeach: return "beach things";
				case CatSeaRocks: return "rocks under water";
				case CatSeaFinds: return "finds under water";
				case CatSunken: return "sunken barrels";
				default: return "corals and sea plants";
			}
		}

		/// <summary>Objects per 1000 m² of their zones at a slider's top (the slider squares its value: half way is a quarter of this).</summary>
		static float MaxDensity(string cat)
		{
			switch (cat)
			{
				case CatTrees: return 110f;   // about 3 m apart: a jungle you squeeze through
				case CatBushes: return 220f;
				case CatRocks: return 45f;
				case CatHarvest: return 25f;
				case CatBeach: return 30f;
				default: return 90f;         // a dense coral reef
			}
		}

		/// <summary>A category's slider in the settings.</summary>
		public static float AmountOf(IslandGenSettings s, string cat)
		{
			switch (cat)
			{
				case CatTrees: return s.Amount(s.Trees);
				case CatBushes: return s.Amount(s.Bushes);
				case CatRocks: return s.Amount(s.Rocks);
				case CatHarvest: return s.Amount(s.Harvest);
				case CatBeach: return s.Amount(s.BeachThings);
				case CatSeaRocks: return s.AmountSea(s.SeaRocks);
				case CatSeaFinds: return s.AmountSea(s.SeaFinds);
				case CatSunken: return s.AmountSea(s.Sunken);
				default: return s.AmountSea(s.Water);
			}
		}

		/// <summary>How many times as dense as around Raft's islands an under-water slider makes its kind (0.5 = as on Raft, 1 = three times).</summary>
		public static float SeaFactor(float amount) { return amount <= 0f ? 0f : Mathf.Pow(2f * amount, 1.6f); }

		/// <summary>The slider value that gives this many objects per 1000 m² (the inverse of the density curve).</summary>
		public static float AmountFor(string cat, float perThousand) { return Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0f, perThousand) / MaxDensity(cat))); }

		#endregion

		#region Generating in the editor

		/// <summary>Generates into the editor terrain (and scatters objects) as one undoable step. Returns how many objects were placed.</summary>
		public static int GenerateInEditor(IslandGenSettings settings)
		{
			float t0 = Time.realtimeSinceStartup;
			IslandGenSettings s = settings.Copy();
			s.Clamp();
			Last = s.Copy();
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			int hres = data.heightmapResolution, ares = data.alphamapResolution;

			// Before-snapshots for undo
			float[,] heightsBefore = data.GetHeights(0, 0, hres, hres);
			float[,,] alphaBefore = data.GetAlphamaps(0, 0, ares, ares);
			float[,] maskBefore = terraineditor.paintMask != null ? (float[,])terraineditor.paintMask.Clone() : null;

			int styleBefore = DynamicIslands.currentStyle;
			float levelBefore = DynamicIslands.EditorWaterLevel;
			DynamicIslands.SetEditorStyle(s.Style); // before painting, so the style's textures go on
			DynamicIslands.SetEditorWaterLevel(s.WaterLevel);
			float[,] metres = HeightsMetres(s, data.size, hres);
			UseSea(s);
			data.SetHeights(0, 0, Normalised(metres, data.size.y));
			// A new island starts with automatic texturing everywhere
			if (terraineditor.paintMask == null || terraineditor.paintMask.GetLength(0) != ares) terraineditor.paintMask = new float[ares, ares];
			else Array.Clear(terraineditor.paintMask, 0, terraineditor.paintMask.Length);
			TerrainPainter.Setup(terrain, terrain.transform.position.y + s.WaterLevel, terraineditor.paintMask);

			var group = new CommandGroup();
			group.Add(new StyleCommand(styleBefore, s.Style)); // first in, so undo restores the old style last
			group.Add(new WaterLevelCommand(levelBefore, s.WaterLevel));
			group.Add(new TerrainStrokeCommand(data, new RectInt(0, 0, hres, hres), heightsBefore, new RectInt(0, 0, ares, ares), alphaBefore, maskBefore, terraineditor.paintMask));

			// The old objects go (hidden, so undo brings them back), the new ones come
			var report = new GenReport();
			Transform placed = GameObject.Find("PlacedObjects") != null ? GameObject.Find("PlacedObjects").transform : null;
			var made = new List<GameObject>();
			if (placed != null)
			{
				if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.ClearTargets(false);
				List<GameObject> old = placed.GetComponentsInChildren<EditorGameObject>(false).Select(e => e.gameObject).ToList();
				foreach (GameObject go in old) go.SetActive(false);
				if (old.Count > 0) group.Add(new ObjectVisibilityCommand(old, false));
				PurgeHidden(placed);

				if (PlaceableCatalog.IsBuilt)
				{
					var file = new IslandFile { WaterLevel = s.WaterLevel, TerrainSize = data.size, HeightmapResolution = hres };
					file.Objects = PlanAll(s, metres, data.size, report);
					var holder = new GameObject("GeneratedObjects");
					holder.transform.SetParent(placed, false);
					holder.transform.position = terrain.transform.position;
					IslandSpawner.SpawnObjects(file, holder.transform, true);
					made = holder.GetComponentsInChildren<EditorGameObject>(true).Select(e => e.gameObject).ToList();
				}
				else Debug.LogWarning("[CUSTOM ISLANDS] Objects are still loading; generated the terrain without objects");
			}
			if (made.Count > 0) group.Add(new ObjectVisibilityCommand(made, true));

			UndoRedoManager.Insert(group);
			MeasureLand(metres, data.size.x / (hres - 1), report);
			report.Seconds = Time.realtimeSinceStartup - t0;
			LastReport = report;
			Debug.Log(string.Format(CultureInfo.InvariantCulture, "[CUSTOM ISLANDS] Generated island: seed {0}, {1} {2}, radius {3:F0} m, height {4:F0} m, land {5:F0} x {6:F0} m, {7} objects ({8}) in {9:F1} s",
				s.Seed, TerrainPainter.StyleName(s.Style), s.Source.Length > 0 ? "from " + s.Source : IslandShapes.Names[s.Shape], s.Radius, s.Height, report.LandLength, report.LandWidth, made.Count, report.Describe(), report.Seconds));
			return made.Count;
		}

		/// <summary>Hidden objects of earlier generations are kept for undo; past this many they are removed (and undo can't bring them back).</summary>
		const int MaxHiddenObjects = 25000;

		static void PurgeHidden(Transform placed)
		{
			List<EditorGameObject> hidden = placed.GetComponentsInChildren<EditorGameObject>(true).Where(e => !e.gameObject.activeSelf).ToList();
			if (hidden.Count <= MaxHiddenObjects) return;
			foreach (EditorGameObject e in hidden) UnityEngine.Object.Destroy(e.gameObject);
			foreach (Transform t in placed.Cast<Transform>().Where(t => t.name == "GeneratedObjects" && t.GetComponentsInChildren<EditorGameObject>(true).All(e => !e.gameObject.activeSelf)).ToList())
				UnityEngine.Object.Destroy(t.gameObject);
			Debug.Log("[CUSTOM ISLANDS] Removed " + hidden.Count + " hidden objects of earlier islands (they are kept for undo up to " + MaxHiddenObjects + ")");
		}

		/// <summary>Points the editor camera at the island from a distance that fits it.</summary>
		public static void FrameCamera(IslandGenSettings s)
		{
			Terrain terrain = terraineditor.terrain;
			if (terrain == null || Camera.main == null) return;
			Vector3 c = terrain.transform.position + new Vector3(terrain.terrainData.size.x / 2f, s.WaterLevel, terrain.terrainData.size.z / 2f);
			// (far enough back for the land and a tall peak)
			float r = Mathf.Max(30f, Mathf.Max(s.Radius * Mathf.Sqrt(Mathf.Max(1f, s.Stretch)), s.Height * 0.9f));
			Transform cam = Camera.main.transform;
			cam.position = c + new Vector3(0, s.Height * 0.8f + r * 0.9f, -r * 1.9f);
			cam.LookAt(c + Vector3.up * (s.Height * 0.3f));
		}

		/// <summary>Size of the land above the sea (along its long axis and across) and its highest point.</summary>
		static void MeasureLand(float[,] m, float step, GenReport report)
		{
			int res = m.GetLength(0), land = 0;
			double sx = 0, sz = 0;
			float top = 0f;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					if (m[z, x] > Sea) { land++; sx += x; sz += z; top = Mathf.Max(top, m[z, x] - Sea); }
			report.LandArea = land * step * step;
			report.Top = top;
			if (land < 3) return;
			double mx = sx / land, mz = sz / land, cxx = 0, czz = 0, cxz = 0;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					if (m[z, x] > Sea) { double dx = x - mx, dz = z - mz; cxx += dx * dx; czz += dz * dz; cxz += dx * dz; }
			cxx /= land; czz /= land; cxz /= land;
			double tr = cxx + czz, disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - (cxx * czz - cxz * cxz)));
			report.LandLength = (float)(4 * Math.Sqrt(tr / 2 + disc)) * step;
			report.LandWidth = (float)(4 * Math.Sqrt(Math.Max(0, tr / 2 - disc))) * step;
		}

		#endregion

		#region Heights

		/// <summary>Normalised heightmap (0..1 of the terrain height) for a terrain of this size and resolution.</summary>
		public static float[,] Heights(IslandGenSettings s, Vector3 size, int res) { return Normalised(HeightsMetres(s, size, res), size.y); }

		static float[,] Normalised(float[,] m, float height)
		{
			int res = m.GetLength(0);
			var h = new float[res, m.GetLength(1)];
			for (int z = 0; z < res; z++)
				for (int x = 0; x < h.GetLength(1); x++)
					h[z, x] = Mathf.Clamp01(m[z, x] / height);
			return h;
		}

		/// <summary>Ground heights in metres above the terrain's base (the sea is at the settings' WaterLevel), for the whole terrain.</summary>
		public static float[,] HeightsMetres(IslandGenSettings s, Vector3 size, int res) { return HeightsMetres(s, size, res, size.x); }

		/// <summary>
		/// Ground heights (m above the terrain's base) on a res x res grid covering span x span metres around the
		/// middle of a terrain of this size (the preview looks at a smaller square than the whole build area).
		/// </summary>
		public static float[,] HeightsMetres(IslandGenSettings settings, Vector3 size, int res, float span)
		{
			IslandGenSettings s = settings.Copy();
			s.Clamp();
			UseSea(s);
			float step = span / (res - 1);
			Func<Vector2, float> shape = ShapeOf(s);
			var m = new float[res, res];
			float a = s.StretchAngle * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a), k = Mathf.Sqrt(s.Stretch);
			float half = span / 2f;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					float px = x * step - half, pz = z * step - half;
					// In the island's own frame: its long axis along x, stretched (the area stays the same)
					m[z, x] = shape(new Vector2((px * ca + pz * sa) / k, (-px * sa + pz * ca) * k));
				}
			var g = new Grid { M = m, Res = res, Step = step, Half = half };
			ShapePeaks(g, s);
			CarveValleys(g, s);
			Normalise(g, s);
			Terrace(g, s);
			if (s.Erosion > 0f) Erode(g, s);
			if (s.Terraces > 0f || s.Erosion > 0f) Normalise(g, s); // (steps and rain lower the top a little)
			CarveLakes(g, s);
			ShapeSeabed(g, s);
			FlattenEdges(g, s, size);
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					m[z, x] = Mathf.Clamp(m[z, x], 0f, size.y);
			return m;
		}

		/// <summary>A height grid in metres; cell (x, z) is at (x * Step - Half, z * Step - Half) from the middle.</summary>
		class Grid
		{
			public float[,] M;
			public int Res;
			public float Step, Half;
			public Vector2 Pos(int x, int z) { return new Vector2(x * Step - Half, z * Step - Half); }
			public float Max() { float m = float.MinValue; foreach (float v in M) if (v > m) m = v; return m; }
			public float At(float px, float pz)
			{
				float fx = Mathf.Clamp((px + Half) / Step, 0f, Res - 1.001f), fz = Mathf.Clamp((pz + Half) / Step, 0f, Res - 1.001f);
				int x0 = (int)fx, z0 = (int)fz;
				float tx = fx - x0, tz = fz - z0;
				return Mathf.Lerp(Mathf.Lerp(M[z0, x0], M[z0, x0 + 1], tx), Mathf.Lerp(M[z0 + 1, x0], M[z0 + 1, x0 + 1], tx), tz);
			}
		}

		/// <summary>The layout's (or the Raft island's) ground: metres above the terrain's base at a point relative to the middle.</summary>
		static Func<Vector2, float> ShapeOf(IslandGenSettings s)
		{
			if (s.Source.Length > 0)
			{
				RaftIsland island = RaftIslands.Get(s.Source);
				HeightField field = island != null ? RaftIslands.Heights(island) : null;
				if (field != null) return FromRaftIsland(s, island, field);
				Debug.LogWarning("[CUSTOM ISLANDS] No ground heights for Raft island '" + s.Source + "': using the round layout");
			}
			switch (s.Shape)
			{
				case IslandShapes.Atoll: return Atoll(s);
				case IslandShapes.Archipelago: return Archipelago(s);
				case IslandShapes.Stacks: return Stacks(s);
				case IslandShapes.Plateau: return Plateau(s);
				case IslandShapes.Crescent: return Crescent(s);
				default: return Round(s);
			}
		}

		/// <summary>Smooth 0..1 as x goes from a to b (either way round).</summary>
		static float SS(float a, float b, float x) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x)); }

		/// <summary>A pattern around the island: a few soft bumps (0..1) at random directions.</summary>
		class Around
		{
			readonly float[] centres, halfWidths;

			public Around(System.Random rnd, int count, float minHalf, float maxHalf)
			{
				centres = new float[count]; halfWidths = new float[count];
				for (int i = 0; i < count; i++)
				{
					centres[i] = (float)rnd.NextDouble() * Mathf.PI * 2f;
					halfWidths[i] = minHalf + (float)rnd.NextDouble() * (maxHalf - minHalf);
				}
			}

			public float At(float angle)
			{
				float v = 0f;
				for (int i = 0; i < centres.Length; i++)
				{
					float d = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, centres[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
					if (d < halfWidths[i]) v = Mathf.Max(v, 1f - SS(halfWidths[i] * 0.35f, halfWidths[i], d));
				}
				return v;
			}
		}

		/// <summary>
		/// Which stretches of the coast are cliffs (0..1 by direction): a smooth wave around the island, cut so that just
		/// the share asked for is cliff (all of it at 100 %), in a few long stretches with short blends between them.
		/// </summary>
		class CliffMask
		{
			readonly float[] phase = new float[4], amp = new float[4];
			readonly float share, cut, blend;

			public CliffMask(System.Random rnd, float share)
			{
				this.share = share;
				for (int i = 0; i < 4; i++) { phase[i] = (float)rnd.NextDouble() * Mathf.PI * 2f; amp[i] = (0.4f + 0.6f * (float)rnd.NextDouble()) / (i + 1); }
				if (share <= 0.001f || share >= 0.999f) return;
				var v = new float[720];
				for (int i = 0; i < v.Length; i++) v[i] = Wave(i * Mathf.PI * 2f / v.Length);
				Array.Sort(v);
				cut = v[Mathf.Clamp(Mathf.RoundToInt((1f - share) * (v.Length - 1)), 0, v.Length - 1)];
				blend = (v[v.Length - 1] - v[0]) * 0.04f;
			}

			float Wave(float angle) { float w = 0f; for (int i = 0; i < 4; i++) w += amp[i] * Mathf.Sin((i + 1) * angle + phase[i]); return w; }

			public float At(float angle)
			{
				if (share <= 0.001f) return 0f;
				if (share >= 0.999f) return 1f;
				return SS(cut - blend, cut + blend, Wave(angle));
			}
		}

		/// <summary>Width (m) of the beach behind the waterline.</summary>
		static float BeachMetres(IslandGenSettings s) { return Mathf.Lerp(0.03f, 0.3f, s.BeachWidth) * s.Radius + Mathf.Lerp(1f, 6f, s.BeachWidth); }
		/// <summary>Width (m) of the shallow water (down to 8 m deep) off the coast, and of the slope down to the seabed after it.</summary>
		static float ShelfMetres(IslandGenSettings s) { return Mathf.Lerp(0.06f, 0.6f, s.Shelf) * s.Radius + Mathf.Lerp(4f, 30f, s.Shelf); }
		static float DropMetres(IslandGenSettings s) { return 10f + 0.12f * s.Radius; }

		/// <summary>
		/// Ground height d metres off the coast. Shallow seabed: shallow water, then the slope down to the seabed (the
		/// terrain's base, 20 m down). Deep sea floor (DeepDepth): the shelf, then the drop-off to the floor.
		/// </summary>
		static float OffCoast(Vector2 p, float d, float shelf, float drop)
		{
			if (deep) return Mathf.Max(0f, Sea - DeepDepth(p, d, shelf));
			float depth = 8f * SS(0f, shelf, d) + (Sea - 8f) * SS(shelf, shelf + drop, d);
			return Mathf.Max(0f, Sea - depth);
		}

		/// <summary>Depth of the shelf's edge on a deep sea floor (Raft's islands: 8-12 m about 20-30 m out).</summary>
		const float ShelfDepth = 10f;

		/// <summary>
		/// Deep sea floor, measured on Raft's islands (CIMeasureUnderwater): from the coast the shelf deepens evenly to
		/// about ShelfDepth, then the drop-off falls to the floor (Fall). The shelf's width and depth wander along the coast.
		/// </summary>
		static float DeepDepth(Vector2 p, float d, float shelf)
		{
			if (d <= 0f) return 0f;
			float n = Fbm(p / 45f + seaOffA, 3);
			float sh = Mathf.Max(3f, shelf * (1f + 0.45f * n)), edge = ShelfDepth * (1f + 0.2f * n);
			if (d <= sh) return edge * d / sh;
			return Fall(p, d - sh, edge);
		}

		/// <summary>
		/// Deep sea floor: the depth x metres beyond the edge of shallow water that is startDepth deep there. It falls
		/// over dropWidth to the floor (the terrain's base), steepest in the middle, with spurs and gullies running down.
		/// </summary>
		static float Fall(Vector2 p, float x, float startDepth)
		{
			if (x <= 0f) return startDepth;
			float u = x / dropWidth;
			float c = u >= 1f ? 1f : Mathf.Lerp(u, u * u * (3f - 2f * u), 0.5f);
			float depth = startDepth + (Sea - startDepth) * c;
			float ridge = 1f - 2f * Mathf.Abs(Fbm(p / 17f + seaOffB, 3)); // -1..1: spurs (up) and gullies (down)
			depth -= ridge * Mathf.Min(9f, 1f + depth * 0.1f) * SS(0f, 8f, x) * (1f - SS(0.85f, 1.1f, u));
			return Mathf.Clamp(depth, startDepth * 0.8f, Sea);
		}

		/// <summary>Deep sea floor beyond the edge of shallow water (startDepth deep there): a shelf deepening to ShelfDepth over shelfWidth, then the drop-off.</summary>
		static float ShelfThenFall(Vector2 p, float x, float startDepth, float shelfWidth)
		{
			if (x <= 0f) return startDepth;
			if (x <= shelfWidth) return Mathf.Lerp(startDepth, ShelfDepth, x / Mathf.Max(1f, shelfWidth));
			return Fall(p, x - shelfWidth, ShelfDepth);
		}

		static float DropWidthOf(IslandGenSettings s) { return Mathf.Lerp(170f, 35f, s.DropOff) * Mathf.Lerp(0.75f, 1.15f, Mathf.InverseLerp(10f, 120f, s.Radius)); }

		/// <summary>How far from the middle the island's terrain (land, shallow water, slope) can reach (m).</summary>
		public static float Reach(IslandGenSettings s)
		{
			float r;
			if (s.Source.Length > 0)
			{
				RaftIsland i = RaftIslands.Get(s.Source);
				r = i != null ? s.Radius * Mathf.Max(1.2f, i.Shelf / Mathf.Max(1f, i.Radius)) + 25f : s.Radius * 1.6f;
			}
			else
				switch (s.Shape)
				{
					case IslandShapes.Atoll: case IslandShapes.Archipelago: case IslandShapes.Stacks: case IslandShapes.Crescent: r = s.Radius * 1.45f + 10f; break;
					default: r = s.Radius * (1f + 0.3f * s.CoastAmount) + ShelfMetres(s) + (s.Deep ? 0.15f * DropWidthOf(s) : DropMetres(s)); break; // (the noise seldom reaches its full range)
				}
			return Mathf.Min(r * Mathf.Sqrt(Mathf.Max(1f, s.Stretch)), 500f);
		}

		/// <summary>The peaks of the round layouts: x, z (from the middle), radius, relative height (the first is the highest).</summary>
		static List<Vector4> PeakSpots(IslandGenSettings s, System.Random rnd)
		{
			var peaks = new List<Vector4>();
			if (s.Shape == IslandShapes.TwinPeaks)
			{
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = s.Radius * 0.34f;
				for (int i = 0; i < 2; i++)
				{
					float ai = a + i * Mathf.PI;
					peaks.Add(new Vector4(Mathf.Cos(ai) * d, Mathf.Sin(ai) * d, s.Radius * 0.36f, i == 0 ? 1f : 0.9f));
				}
				return peaks;
			}
			for (int i = 0; i < s.Peaks; i++)
			{
				float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
				float d = i == 0 ? (float)rnd.NextDouble() * s.Radius * 0.25f : (0.2f + 0.3f * (float)rnd.NextDouble()) * s.Radius;
				float r = s.Radius * (0.35f + 0.25f * (float)rnd.NextDouble());
				float h = i == 0 ? 1f : 0.45f + 0.45f * (float)rnd.NextDouble(); // the first peak is the highest
				peaks.Add(new Vector4(Mathf.Cos(a) * d, Mathf.Sin(a) * d, r, h));
			}
			return peaks;
		}

		/// <summary>One peak's shape at squared distance q (in peak radii): a rounded hill, blending into a spire as PeakShape grows.</summary>
		static float PeakProfile(float q, float peakShape)
		{
			float hill = Mathf.Exp(-q * 1.6f);
			if (peakShape <= 0.3f) return hill;
			float spire = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Sqrt(q) * 0.85f), 1.3f + 1.7f * peakShape);
			return Mathf.Lerp(hill, spire, SS(0.3f, 1f, peakShape));
		}

		/// <summary>
		/// One island (also the marsh and twin peaks): a noisy coast (with bays and cliffs), a beach, peaks and hills
		/// behind it; shallow water off the coast, then a slope to the seabed.
		/// </summary>
		static Func<Vector2, float> Round(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed);
			// Noise offsets make each seed a different part of the noise field
			Vector2 coastOffset = RandomOffset(rnd), hillOffset = RandomOffset(rnd), warpOffset = RandomOffset(rnd);
			List<Vector4> peaks = PeakSpots(s, rnd);
			var bays = new Around(new System.Random(s.Seed * 17 + 3), 1 + Mathf.RoundToInt(s.Bays * 4f), 0.14f, 0.3f);
			var cliffs = new CliffMask(new System.Random(s.Seed * 29 + 11), s.Cliffs);

			bool volcanic = s.Style == TerrainPainter.Volcanic, marsh = s.Shape == IslandShapes.Marsh;
			float coast = s.CoastAmount;
			float coastScale = 1f / Mathf.Max(20f, s.Radius * 0.6f);
			float hillScale = 1f / Mathf.Clamp(s.Radius * 0.45f, 12f, 45f);
			float beach = BeachMetres(s), shelf = ShelfMetres(s), drop = DropMetres(s);
			float cliffTop = Mathf.Clamp(s.Height * 0.4f, 3f, 30f);
			float rise = Mathf.Max(0f, s.Height - ShelfHeight);
			float ridged = s.Roughness * s.Roughness * 0.6f;

			return p =>
			{
				float dist = p.magnitude;
				// Ragged coastline: the radius varies with low-frequency noise (domain-warped a little)
				Vector2 warp = new Vector2(Fbm(p * coastScale + warpOffset, 2), Fbm(p * coastScale + warpOffset + new Vector2(17.3f, 5.1f), 2));
				float cn = Fbm((p + warp * s.Radius * 0.25f * coast) * coastScale + coastOffset, 4);
				float cf = 1f + 0.45f * coast * cn;
				float angle = Mathf.Atan2(p.y, p.x);
				if (s.Bays > 0f) cf *= 1f - 0.65f * s.Bays * bays.At(angle);
				float d = dist - s.Radius * cf; // metres beyond the coast (negative: inland)
				float cliff = s.Cliffs > 0f ? cliffs.At(angle) : 0f;
				if (d >= 0f) return OffCoast(p, d, Mathf.Lerp(shelf, 2f, cliff), drop);

				float u = -d;
				float ground = ShelfHeight * SS(0f, beach, u);
				if (cliff > 0f) ground = Mathf.Lerp(ground, cliffTop * SS(0f, 2f + 0.03f * s.Radius, u), cliff);
				float e = Sea + ground;

				// Peaks and hills, behind the beach
				float inland = SS(beach * 0.5f, beach + 0.4f * s.Radius, u);
				if (inland <= 0f) return e;
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
					else v = pk.w * PeakProfile(q, s.PeakShape);
					mountain = Mathf.Max(mountain, v);
				}
				float hills = Fbm(p * hillScale + hillOffset, 4); // -1..1
				if (ridged > 0f) hills = Mathf.Lerp(hills, 1f - 2f * Mathf.Abs(hills), ridged);
				float bump = mountain * (1f + 0.35f * s.Roughness * hills) + 0.18f * s.Roughness * (hills * 0.5f + 0.5f);
				e += inland * Mathf.Max(0f, bump) * rise;
				// A marsh has pools of shallow water in its low land
				if (marsh) e = Mathf.Max(Sea - 1.5f, e - inland * Mathf.Max(0f, Fbm(p / 22f + warpOffset * 1.7f, 3) - 0.05f) * 14f);
				return e;
			};
		}

		/// <summary>A ring of low land (dunes up to the height setting) around a shallow lagoon, with a channel or two into it.</summary>
		static Func<Vector2, float> Atoll(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed);
			Vector2 coastOff = RandomOffset(rnd), hillOff = RandomOffset(rnd), floorOff = RandomOffset(rnd);
			float ring = s.Radius * 0.75f, width = s.Radius * (0.16f + 0.1f * s.CoastAmount);
			float shelfW = ShelfMetres(s) * 0.6f;
			float coastScale = 1f / Mathf.Max(20f, s.Radius * 0.5f);
			return p =>
			{
				float dist = p.magnitude;
				float coast = Fbm(p * coastScale + coastOff, 3);
				float d = Mathf.Abs(dist - ring) / Mathf.Max(4f, width * (1f + 0.6f * coast));
				float top = (1f - SS(0.55f, 1.25f, d)) * SS(-0.5f, -0.3f, coast);
				float dunes = top * Mathf.Max(0f, Fbm(p / 30f + hillOff, 3) * 0.5f + 0.5f) * Mathf.Max(0f, s.Height - ShelfHeight);
				float floor = dist < ring ? Sea - 2f - 2.5f * (Fbm(p / 40f + floorOff, 2) * 0.5f + 0.5f) 
					: deep ? Sea - ShelfThenFall(p, dist - ring - Mathf.Max(4f, width * (1f + 0.6f * coast)), 2f, shelfW) : Mathf.Lerp(0f, Sea - 2f, 1f - SS(1f, 3.2f, d));
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
			float coastAmount = s.CoastAmount, shelfW = ShelfMetres(s) * 0.6f;
			return p =>
			{
				float shelf = 1f - SS(0.95f, 1.4f, p.magnitude / s.Radius);
				float floor = deep ? Sea - ShelfThenFall(p, p.magnitude - 0.95f * s.Radius, 2.5f, shelfW) : Mathf.Lerp(0f, Sea - 2.5f, shelf), best = floor;
				float coast = Fbm(p / Mathf.Max(15f, s.Radius * 0.25f) + coastOff, 3);
				foreach (Vector4 o in islets)
				{
					float q = (p - new Vector2(o.x, o.y)).magnitude / (o.z * (1f + 0.35f * coastAmount * coast));
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
			float coastAmount = s.CoastAmount, shelfW = ShelfMetres(s) * 0.6f;
			return p =>
			{
				float plate = 1f - SS(0.8f, 1.25f, p.magnitude / s.Radius);
				float floor = deep ? Sea - ShelfThenFall(p, p.magnitude - 0.8f * s.Radius, 2.5f, shelfW) : Mathf.Lerp(0f, Sea - 2.5f, plate), e = floor;
				float edge = Fbm(p / 12f + edgeOff, 3);
				foreach (Vector4 o in stacks)
				{
					float q = (p - new Vector2(o.x, o.y)).magnitude / (o.z * (1f + 0.25f * coastAmount * edge));
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
			float coastAmount = s.CoastAmount, shelf = ShelfMetres(s), drop = DropMetres(s);
			return p =>
			{
				float coast = Fbm(p * coastScale + coastOff, 4);
				float cr = s.Radius * (1f + 0.3f * coastAmount * coast);
				float d = p.magnitude - cr;
				if (d >= 0f) return OffCoast(p, d, shelf, drop);
				float rr = p.magnitude / cr;
				float baseLevel = Sea + ShelfHeight * SS(0f, 4f + 0.08f * s.Radius, -d);
				float rise = s.Height - ShelfHeight;
				float cliff = 1f - SS(0.55f, 0.7f, rr);
				float e = baseLevel + cliff * (rise + 0.4f * Fbm(p / 10f + topOff, 2));
				float along = Vector2.Dot(p, rampDir), across = Mathf.Abs(p.x * rampDir.y - p.y * rampDir.x);
				if (along > 0f)
					e = Mathf.Max(e, baseLevel + rise * SS(0.95f, 0.5f, rr) * (1f - SS(5f, 9f, across)));
				return e;
			};
		}

		/// <summary>A curved island (an arc of land with a ridge along it) around a sheltered, shallow bay.</summary>
		static Func<Vector2, float> Crescent(IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed);
			Vector2 coastOff = RandomOffset(rnd), hillOff = RandomOffset(rnd);
			float open = (float)rnd.NextDouble() * 360f;
			float ring = s.Radius * 0.62f, half = s.Radius * 0.33f;
			float coastAmount = s.CoastAmount, shelf = ShelfMetres(s) * 0.7f, drop = DropMetres(s);
			float beach = BeachMetres(s) * 0.6f, rise = Mathf.Max(0f, s.Height - ShelfHeight);
			return p =>
			{
				float dist = p.magnitude;
				float gap = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg, open)) / 180f; // 0 in the gap's middle, 1 opposite
				float arc = SS(0.28f, 0.55f, gap);                     // the land's width tapers to the tips
				float coast = Fbm(p / Mathf.Max(18f, s.Radius * 0.35f) + coastOff, 3);
				float width = half * arc * (1f + 0.4f * coastAmount * coast);
				float u = width - Mathf.Abs(dist - ring);              // metres inside the band (> 0 = land)
				if (u <= 0f)
				{
					float e = OffCoast(p, -u, shelf, drop);
					// The bay inside stays shallow
					if (dist < ring) e = Mathf.Max(e, Sea - 2f - 2f * SS(0f, ring * 0.8f, -u));
					return e;
				}
				float ground = Sea + ShelfHeight * SS(0f, beach, u);
				float ridge = SS(beach * 0.5f, Mathf.Max(beach + 1f, width * 0.9f), u) * SS(0.3f, 0.75f, gap);
				float hills = Fbm(p / Mathf.Clamp(s.Radius * 0.3f, 10f, 35f) + hillOff, 4);
				return ground + ridge * rise * Mathf.Max(0f, 0.75f + 0.25f * hills + 0.3f * s.Roughness * hills);
			};
		}

		/// <summary>One of Raft's islands' own ground (RaftIslands), scaled to the size asked for, mirrored, wobbled and roughened.</summary>
		static Func<Vector2, float> FromRaftIsland(IslandGenSettings s, RaftIsland island, HeightField field)
		{
			var rnd = new System.Random(s.Seed);
			Vector2 warpOff = RandomOffset(rnd), roughOff = RandomOffset(rnd);
			float scale = s.Radius / Mathf.Max(1f, island.Radius);
			float warpScale = 1f / Mathf.Max(12f, s.Radius * 0.45f);
			float wobble = s.SourceWobble * 0.2f * island.Radius, bumps = s.SourceRoughen * 0.15f * Mathf.Max(8f, island.Top);
			return p =>
			{
				Vector2 q = p / scale;
				if (s.Mirror) q.x = -q.x;
				if (wobble > 0f) q += new Vector2(Fbm(p * warpScale + warpOff, 3), Fbm(p * warpScale + warpOff + new Vector2(31.7f, 11.3f), 3)) * wobble;
				float h = field.At(q.x, q.y);
				// (Raft's deep water, down to 160 m, is the terrain's seabed here, 20 m down)
				float e = Sea + h;
				if (bumps > 0f && h > 0f) e += bumps * Fbm(p / 16f + roughOff, 4) * SS(0f, 4f, h);
				return e;
			};
		}

		/// <summary>Peak shape over the whole land: below 0.3 fuller, rounder hills; above it sharper tops over flatter lowland.</summary>
		static void ShapePeaks(Grid g, IslandGenSettings s)
		{
			if (Mathf.Abs(s.PeakShape - 0.3f) < 0.01f) return;
			float exp = s.PeakShape < 0.3f ? Mathf.Lerp(0.6f, 1f, s.PeakShape / 0.3f) : Mathf.Lerp(1f, 2.2f, (s.PeakShape - 0.3f) / 0.7f);
			float baseH = Sea + ShelfHeight, top = g.Max();
			if (top <= baseH + 0.5f) return;
			for (int z = 0; z < g.Res; z++)
				for (int x = 0; x < g.Res; x++)
				{
					float e = g.M[z, x];
					if (e <= baseH) continue;
					g.M[z, x] = baseH + Mathf.Pow((e - baseH) / (top - baseH), exp) * (top - baseH);
				}
		}

		/// <summary>Makes the highest point exactly the height asked for (the beach and the sea stay as they are).</summary>
		static void Normalise(Grid g, IslandGenSettings s)
		{
			float baseH = Sea + Mathf.Min(ShelfHeight, s.Height * 0.8f), top = g.Max();
			if (top <= baseH + 0.05f) return;
			float k = (Sea + s.Height - baseH) / (top - baseH);
			for (int z = 0; z < g.Res; z++)
				for (int x = 0; x < g.Res; x++)
				{
					float e = g.M[z, x];
					if (e > baseH) g.M[z, x] = baseH + (e - baseH) * k;
				}
		}

		/// <summary>Steps in the land: flat terraces with steep risers between them.</summary>
		static void Terrace(Grid g, IslandGenSettings s)
		{
			if (s.Terraces <= 0f) return;
			float baseH = Sea + ShelfHeight + 0.5f;
			float stepH = Mathf.Lerp(Mathf.Max(3f, s.Height / 3f), Mathf.Max(2.5f, s.Height / 10f), s.Terraces);
			float w = Mathf.Lerp(0.45f, 0.1f, s.Terraces), amount = Mathf.Min(1f, s.Terraces * 1.5f);
			for (int z = 0; z < g.Res; z++)
				for (int x = 0; x < g.Res; x++)
				{
					float e = g.M[z, x];
					if (e <= baseH) continue;
					float t = (e - baseH) / stepH, fl = Mathf.Floor(t);
					float stepped = baseH + stepH * (fl + SS(0.5f - w, 0.5f + w, t - fl));
					g.M[z, x] = Mathf.Lerp(e, stepped, amount);
				}
		}

		/// <summary>River valleys: from near the highest point, winding down to the sea, cut into the land.</summary>
		static void CarveValleys(Grid g, IslandGenSettings s)
		{
			if (s.Valleys <= 0f) return;
			var rnd = new System.Random(s.Seed * 61 + 17);
			// The highest cell
			int hx = 0, hz = 0; float top = float.MinValue;
			for (int z = 0; z < g.Res; z++) for (int x = 0; x < g.Res; x++) if (g.M[z, x] > top) { top = g.M[z, x]; hx = x; hz = z; }
			if (top < Sea + 3f) return;
			int n = 1 + Mathf.RoundToInt(s.Valleys * 4f);
			float width = Mathf.Lerp(5f, 14f, s.Valleys) + 0.05f * s.Radius;
			float reach = width * 2.5f;
			Vector2 peak = g.Pos(hx, hz);
			float start = rnd.NextDouble() < 0.5 ? 0f : (float)rnd.NextDouble() * Mathf.PI * 2f;
			for (int v = 0; v < n; v++)
			{
				float angle = start + v * Mathf.PI * 2f / n + ((float)rnd.NextDouble() - 0.5f) * 0.6f;
				Vector2 p = peak + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (width + 0.12f * s.Radius * (float)rnd.NextDouble());
				var path = new List<Vector2> { p };
				for (int i = 0; i < 400; i++)
				{
					angle += ((float)rnd.NextDouble() - 0.5f) * 0.45f;
					p += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 5f;
					path.Add(p);
					if (Mathf.Abs(p.x) > g.Half || Mathf.Abs(p.y) > g.Half || g.At(p.x, p.y) < Sea - 3f) break;
				}
				if (path.Count < 3) continue;
				float startH = g.At(path[0].x, path[0].y);
				// Cells near the path: lowered to a floor that falls from 70 % of the start to below the sea
				float minX = path.Min(q => q.x) - reach, maxX = path.Max(q => q.x) + reach, minZ = path.Min(q => q.y) - reach, maxZ = path.Max(q => q.y) + reach;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((minX + g.Half) / g.Step), 0, g.Res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((maxX + g.Half) / g.Step), 0, g.Res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ + g.Half) / g.Step), 0, g.Res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ + g.Half) / g.Step), 0, g.Res - 1);
				for (int z = z0; z <= z1; z++)
					for (int x = x0; x <= x1; x++)
					{
						float e = g.M[z, x];
						if (e < Sea - 2f) continue;
						Vector2 c = g.Pos(x, z);
						float best = float.MaxValue, at = 0f;
						for (int i = 0; i < path.Count - 1; i++)
						{
							Vector2 a = path[i], ab = path[i + 1] - a;
							float t = Mathf.Clamp01(Vector2.Dot(c - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
							float dd = (a + ab * t - c).sqrMagnitude;
							if (dd < best) { best = dd; at = (i + t) / (path.Count - 1); }
						}
						float d = Mathf.Sqrt(best);
						if (d >= reach) continue;
						float floor = Mathf.Lerp(startH * 0.7f + Sea * 0.3f, Sea - 1.5f, Mathf.Pow(at, 0.7f));
						float target = floor + (e - floor) * SS(0f, reach, d);
						if (target < e) g.M[z, x] = target;
					}
			}
		}

		/// <summary>Inland lakes: bowls in the lower land that go below sea level, so the sea fills them.</summary>
		static void CarveLakes(Grid g, IslandGenSettings s)
		{
			if (s.Lakes <= 0f) return;
			var rnd = new System.Random(s.Seed * 73 + 5);
			int wanted = 1 + Mathf.RoundToInt(s.Lakes * 3f);
			float top = g.Max();
			float maxH = Sea + Mathf.Max(3f, (top - Sea) * 0.45f);
			float size = Mathf.Clamp(s.Radius / 60f, 0.35f, 1.8f);
			var lakes = new List<Vector3>();
			for (int attempt = 0; attempt < 400 && lakes.Count < wanted; attempt++)
			{
				int x = rnd.Next(g.Res), z = rnd.Next(g.Res);
				float e = g.M[z, x];
				float r = Mathf.Lerp(6f, 22f, s.Lakes) * (0.7f + 0.6f * (float)rnd.NextDouble()) * size;
				if (e < Sea + 1.5f || e > maxH) continue;
				Vector2 c = g.Pos(x, z);
				if (lakes.Any(l => (new Vector2(l.x, l.y) - c).magnitude < l.z + r + 8f)) continue;
				// Land all around it (a lake, not a bay)
				bool inland = true;
				for (int k = 0; k < 12 && inland; k++)
				{
					float a = k * Mathf.PI / 6f;
					if (g.At(c.x + Mathf.Cos(a) * (r * 1.4f + 6f), c.y + Mathf.Sin(a) * (r * 1.4f + 6f)) < Sea + 1f) inland = false;
				}
				if (inland) lakes.Add(new Vector3(c.x, c.y, r));
			}
			Vector2 wob = RandomOffset(rnd);
			float bottom = Sea - 1f - 2.5f * s.Lakes;
			foreach (Vector3 l in lakes)
			{
				float reach = l.z * 1.8f;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((l.x - reach + g.Half) / g.Step), 0, g.Res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((l.x + reach + g.Half) / g.Step), 0, g.Res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((l.y - reach + g.Half) / g.Step), 0, g.Res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((l.y + reach + g.Half) / g.Step), 0, g.Res - 1);
				for (int z = z0; z <= z1; z++)
					for (int x = x0; x <= x1; x++)
					{
						Vector2 c = g.Pos(x, z);
						float t = (c - new Vector2(l.x, l.y)).magnitude / l.z * (1f + 0.2f * Fbm(c / 9f + wob, 2));
						float target = Mathf.Lerp(bottom, g.M[z, x], SS(0.6f, 1.5f, t));
						if (target < g.M[z, x]) g.M[z, x] = target;
					}
			}
		}

		/// <summary>The seabed near the island: sand (as it is), rocky (bumps and outcrops), or a reef ring just under the surface.</summary>
		static void ShapeSeabed(Grid g, IslandGenSettings s)
		{
			if (s.Seabed == IslandGenSettings.SeabedSand) return;
			var rnd = new System.Random(s.Seed * 43 + 9);
			Vector2 off = RandomOffset(rnd), gapOff = RandomOffset(rnd);
			for (int z = 0; z < g.Res; z++)
				for (int x = 0; x < g.Res; x++)
				{
					float e = g.M[z, x], depth = Sea - e;
					if (depth <= 0.5f || depth >= Sea - 1f) continue;
					Vector2 c = g.Pos(x, z);
					if (s.Seabed == IslandGenSettings.SeabedRocky)
					{
						// (fades out before the flat seabed, which must stay flat)
						float mask = SS(0.5f, 3f, depth) * (1f - SS(12f, 17f, depth));
						g.M[z, x] = Mathf.Min(Sea - 0.6f, e + mask * 2.6f * Fbm(c / 7f + off, 3));
					}
					else
					{
						float band = SS(3.5f, 5.5f, depth) * (1f - SS(9f, 12f, depth));
						float gaps = SS(-0.25f, 0.1f, Fbm(c / 40f + gapOff, 2));
						float reef = Sea - 0.8f - 0.9f * Mathf.Abs(Fbm(c / 6f + off, 2));
						g.M[z, x] = Mathf.Max(e, Mathf.Lerp(e, reef, band * gaps));
					}
				}
		}

		/// <summary>The edge of the build area is always flat seabed (a big, stretched island is cut off before it).</summary>
		static void FlattenEdges(Grid g, IslandGenSettings s, Vector3 size)
		{
			float hx = size.x / 2f, hz = size.z / 2f;
			for (int z = 0; z < g.Res; z++)
				for (int x = 0; x < g.Res; x++)
				{
					Vector2 c = g.Pos(x, z);
					float edge = Mathf.Min(hx - Mathf.Abs(c.x), hz - Mathf.Abs(c.y));
					if (edge < 70f) g.M[z, x] *= SS(8f, 70f, edge);
				}
		}

		/// <summary>Hydraulic erosion: raindrops run downhill, carry soil away from slopes and drop it in hollows (natural gullies).</summary>
		static void Erode(Grid g, IslandGenSettings s)
		{
			var rnd = new System.Random(s.Seed * 97 + 1);
			int res = g.Res;
			// In units of about the island's height, so the same settings erode big and small islands alike
			float unit = Mathf.Max(5f, s.Height);
			var h = new float[res * res];
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) h[z * res + x] = g.M[z, x] / unit;
			float land = (Sea + 0.5f) / unit;
			int drops = Mathf.RoundToInt(s.Erosion * res * res * 0.25f);
			const float Inertia = 0.05f, Capacity = 4f, MinCapacity = 0.01f, ErodeSpeed = 0.3f, DepositSpeed = 0.3f, Evaporate = 0.02f, Gravity = 4f;
			const int Life = 30, Radius = 2;
			// Brush: the cells a drop erodes around it, with weights
			var bx = new List<int>(); var bz = new List<int>(); var bw = new List<float>();
			float wsum = 0f;
			for (int dz = -Radius; dz <= Radius; dz++)
				for (int dx = -Radius; dx <= Radius; dx++)
				{
					float d = Mathf.Sqrt(dx * dx + dz * dz);
					if (d > Radius) continue;
					bx.Add(dx); bz.Add(dz); bw.Add(1f - d / Radius); wsum += 1f - d / Radius;
				}
			for (int i = 0; i < bw.Count; i++) bw[i] /= wsum;
			// (slopes per cell, not per metre: scale by the cell size so resolutions agree)
			float cellScale = g.Step / 2f;
			for (int n = 0; n < drops; n++)
			{
				float px = (float)rnd.NextDouble() * (res - 2), pz = (float)rnd.NextDouble() * (res - 2);
				if (h[(int)pz * res + (int)px] < land) continue;
				float dirX = 0f, dirZ = 0f, speed = 1f, water = 1f, sediment = 0f;
				for (int life = 0; life < Life; life++)
				{
					int cx = (int)px, cz = (int)pz;
					float ox = px - cx, oz = pz - cz;
					int i00 = cz * res + cx;
					float h00 = h[i00], h10 = h[i00 + 1], h01 = h[i00 + res], h11 = h[i00 + res + 1];
					float gx = ((h10 - h00) * (1 - oz) + (h11 - h01) * oz) / cellScale, gz = ((h01 - h00) * (1 - ox) + (h11 - h10) * ox) / cellScale;
					float height = h00 * (1 - ox) * (1 - oz) + h10 * ox * (1 - oz) + h01 * (1 - ox) * oz + h11 * ox * oz;
					dirX = dirX * Inertia - gx * (1 - Inertia); dirZ = dirZ * Inertia - gz * (1 - Inertia);
					float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
					if (len < 1e-6f) break;
					dirX /= len; dirZ /= len;
					px += dirX; pz += dirZ;
					if (px < 1 || pz < 1 || px >= res - 2 || pz >= res - 2) break;
					int nx = (int)px, nz = (int)pz;
					float nox = px - nx, noz = pz - nz;
					int j = nz * res + nx;
					float newHeight = h[j] * (1 - nox) * (1 - noz) + h[j + 1] * nox * (1 - noz) + h[j + res] * (1 - nox) * noz + h[j + res + 1] * nox * noz;
					float delta = newHeight - height;
					if (newHeight < land - 0.3f) { break; } // reached the sea: the soil goes with it
					float capacity = Mathf.Max(-delta * speed * water * Capacity, MinCapacity);
					if (sediment > capacity || delta > 0)
					{
						// Drop soil: fill the hollow (uphill) or what the drop can't carry
						float amount = delta > 0 ? Mathf.Min(delta, sediment) : (sediment - capacity) * DepositSpeed;
						sediment -= amount;
						h[i00] += amount * (1 - ox) * (1 - oz); h[i00 + 1] += amount * ox * (1 - oz);
						h[i00 + res] += amount * (1 - ox) * oz; h[i00 + res + 1] += amount * ox * oz;
					}
					else
					{
						float amount = Mathf.Min((capacity - sediment) * ErodeSpeed, -delta);
						for (int b = 0; b < bw.Count; b++)
						{
							int xx = cx + bx[b], zz = cz + bz[b];
							if (xx < 0 || zz < 0 || xx >= res || zz >= res) continue;
							int k = zz * res + xx;
							if (h[k] < land) continue;
							float take = Mathf.Min(amount * bw[b], h[k] - land * 0.98f);
							if (take <= 0f) continue;
							h[k] -= take;
							sediment += take;
						}
					}
					speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + delta * Gravity));
					water *= 1 - Evaporate;
				}
			}
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) g.M[z, x] = h[z * res + x] * unit;
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

		/// <summary>Zones of the ground, by height above the sea and slope.</summary>
		const int ZWater = 0, ZWet = 1, ZBeach = 2, ZLand = 3, ZRock = 4, ZoneCount = 5;

		/// <summary>How each kind uses the zones (weight per zone), and how big its things may be (m, larger ones are scaled down).</summary>
		static float[] ZoneWeights(string cat)
		{
			switch (cat)
			{
				case CatTrees: return new[] { 0f, 0f, 0.5f, 1f, 0.12f };
				case CatBushes: return new[] { 0f, 0f, 0.3f, 1f, 0.2f };
				case CatRocks: return new[] { 0f, 0.1f, 0.2f, 0.25f, 1f };
				case CatHarvest: return new[] { 0f, 0f, 0.4f, 1f, 0.4f }; // (the ores and scrap under water are "things to collect under water")
				case CatBeach: return new[] { 0f, 0.6f, 1f, 0f, 0f };
				default: return new[] { 0f, 0f, 0f, 0f, 0f };
			}
		}

		static float MaxSize(string cat, int zone)
		{
			switch (cat)
			{
				case CatTrees: return zone == ZBeach ? 18f : 24f;
				case CatBushes: return 8f;
				case CatRocks: return zone == ZRock ? 11f : 6f;
				case CatBeach: return 5f;
				default: return 6f;
			}
		}

		/// <summary>How close things of a kind may stand: a share of their size (trees' crowns overlap, rocks barely).</summary>
		static float Footprint(string cat, float size)
		{
			switch (cat)
			{
				case CatTrees: return Mathf.Clamp(size * 0.13f, 0.7f, 3f);
				case CatBushes: return Mathf.Clamp(size * 0.22f, 0.4f, 2f);
				case CatRocks: return Mathf.Clamp(size * 0.3f, 0.5f, 4f);
				case CatWater: return Mathf.Clamp(size * 0.25f, 0.4f, 2f);
				default: return Mathf.Clamp(size * 0.3f, 0.5f, 2.5f);
			}
		}

		/// <summary>Things already placed, on a grid of cells, for "is this spot free?" (and removing nature around content).</summary>
		class SpotGrid
		{
			const float Cell = 4f;
			readonly int n;
			readonly List<int>[] cells;
			public readonly List<Vector3> Spots = new List<Vector3>(); // x, z, footprint radius
			public readonly List<bool> Removed = new List<bool>();
			float biggest;

			public SpotGrid(float size) { n = Mathf.CeilToInt(size / Cell) + 1; cells = new List<int>[n * n]; }

			int CellOf(float v) { return Mathf.Clamp((int)(v / Cell), 0, n - 1); }

			public bool Free(float x, float z, float r)
			{
				int span = Mathf.CeilToInt((r + biggest) / Cell);
				int cx = CellOf(x), cz = CellOf(z);
				for (int j = Mathf.Max(0, cz - span); j <= Mathf.Min(n - 1, cz + span); j++)
					for (int i = Mathf.Max(0, cx - span); i <= Mathf.Min(n - 1, cx + span); i++)
					{
						List<int> list = cells[j * n + i];
						if (list == null) continue;
						foreach (int k in list)
						{
							if (Removed[k]) continue;
							Vector3 o = Spots[k];
							float dx = o.x - x, dz = o.y - z, rr = o.z + r;
							if (dx * dx + dz * dz < rr * rr) return false;
						}
					}
				return true;
			}

			public int Add(float x, float z, float r)
			{
				int k = Spots.Count;
				Spots.Add(new Vector3(x, z, r));
				Removed.Add(false);
				int c = CellOf(z) * n + CellOf(x);
				if (cells[c] == null) cells[c] = new List<int>();
				cells[c].Add(k);
				biggest = Mathf.Max(biggest, r);
				return k;
			}

			/// <summary>Removes the spots within radius of a point; returns their indices.</summary>
			public List<int> Clear(float x, float z, float radius)
			{
				var gone = new List<int>();
				int span = Mathf.CeilToInt((radius + biggest) / Cell);
				int cx = CellOf(x), cz = CellOf(z);
				for (int j = Mathf.Max(0, cz - span); j <= Mathf.Min(n - 1, cz + span); j++)
					for (int i = Mathf.Max(0, cx - span); i <= Mathf.Min(n - 1, cx + span); i++)
					{
						List<int> list = cells[j * n + i];
						if (list == null) continue;
						foreach (int k in list)
						{
							if (Removed[k]) continue;
							Vector3 o = Spots[k];
							if ((o.x - x) * (o.x - x) + (o.y - z) * (o.y - z) < radius * radius) { Removed[k] = true; gone.Add(k); }
						}
					}
				return gone;
			}
		}

		/// <summary>The planner's view of the ground: heights (m above the terrain's base) with zones per cell.</summary>
		class Ground
		{
			public float[,] M;
			public int Res;
			public float Step, Top;
			public readonly List<int>[] Zones = new List<int>[ZoneCount];
			public float ZoneArea(int z) { return Zones[z].Count * Step * Step; }

			public float At(float x, float z)
			{
				float fx = Mathf.Clamp(x / Step, 0, Res - 1.001f), fz = Mathf.Clamp(z / Step, 0, Res - 1.001f);
				int x0 = (int)fx, z0 = (int)fz;
				float tx = fx - x0, tz = fz - z0;
				return Mathf.Lerp(Mathf.Lerp(M[z0, x0], M[z0, x0 + 1], tx), Mathf.Lerp(M[z0 + 1, x0], M[z0 + 1, x0 + 1], tx), tz);
			}

			public float Slope(float x, float z)
			{
				float dx = (At(x + Step, z) - At(x - Step, z)) / (2f * Step);
				float dz = (At(x, z + Step) - At(x, z - Step)) / (2f * Step);
				return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
			}

			public int ZoneOf(float above, float slope)
			{
				if (above < -12f) return -1;
				if (above < -1.2f) return ZWater;
				if (above < 0.6f) return ZWet;
				if (slope > 32f || above > Mathf.Max(6f, Top * 0.82f)) return ZRock;
				return above < ShelfHeight + 1.2f ? ZBeach : ZLand;
			}
		}

		static Ground Survey(float[,] metres, float step)
		{
			int res = metres.GetLength(0);
			var g = new Ground { M = metres, Res = res, Step = step };
			for (int i = 0; i < ZoneCount; i++) g.Zones[i] = new List<int>();
			float top = 0f;
			foreach (float v in metres) top = Mathf.Max(top, v - Sea);
			g.Top = top;
			for (int z = 1; z < res - 1; z++)
				for (int x = 1; x < res - 1; x++)
				{
					float e = metres[z, x];
					if (e < Sea - 12f) continue;
					float dx = (metres[z, x + 1] - metres[z, x - 1]) / (2f * g.Step), dz = (metres[z + 1, x] - metres[z - 1, x]) / (2f * g.Step);
					int zone = g.ZoneOf(e - Sea, Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg);
					if (zone >= 0) g.Zones[zone].Add(z * res + x);
				}
			return g;
		}

		/// <summary>Everything the generator places for these heights (m above the terrain's base): nature, then creature spots and loot boxes. Deterministic for the settings.</summary>
		public static List<IslandObject> PlanAll(IslandGenSettings s, float[,] metres, Vector3 size, GenReport report = null)
		{
			var result = new List<IslandObject>();
			if (report == null) report = new GenReport();
			if (!PlaceableCatalog.IsBuilt) return result;
			s = s.Copy();
			s.Clamp();
			UseSea(s);
			float step = size.x / (metres.GetLength(0) - 1);
			Ground ground = Survey(metres, step);
			SeaGround sea = SurveySea(metres, step);
			var spots = new SpotGrid(size.x);
			var owners = new List<IslandObject>(); // the object of each spot (null for content's own spots)
			var cats = new List<string>();          // and its kind
			// How many of each kind; land and sea scaled down together past MaxObjects
			Dictionary<string, int> land = LandTargets(s, ground);
			Dictionary<string, float> seaWanted = SeaTargets(s, sea);
			float wanted = land.Values.Sum() + seaWanted.Values.Sum();
			report.Wanted = Mathf.RoundToInt(wanted);
			float k = wanted > MaxObjects ? MaxObjects / wanted : 1f;
			if (k < 1f) foreach (string cat in LandCategories) land[cat] = Mathf.FloorToInt(land[cat] * k);
			PlanNature(s, ground, spots, owners, cats, report, land);
			PlanSea(s, ground, sea, spots, owners, cats, report, k);
			PlanContent(s, ground, spots, owners, cats, report, result);
			for (int i = 0; i < owners.Count; i++)
				if (owners[i] != null && !spots.Removed[i]) result.Add(owners[i]);
			// (content first in the list: map types and tests find it quickly; nature follows)
			return result;
		}

		/// <summary>About how many objects of each kind the settings give (before content clears some), from heights on any grid (the preview's).</summary>
		public static Dictionary<string, int> Estimate(IslandGenSettings s, float[,] metres, float step)
		{
			s = s.Copy();
			s.Clamp();
			UseSea(s);
			Dictionary<string, int> land = LandTargets(s, Survey(metres, step));
			Dictionary<string, float> sea = SeaTargets(s, SurveySea(metres, step));
			float wanted = land.Values.Sum() + sea.Values.Sum();
			float k = wanted > MaxObjects ? MaxObjects / wanted : 1f;
			var targets = new Dictionary<string, int>();
			foreach (string cat in LandCategories) targets[cat] = Mathf.FloorToInt(land[cat] * k);
			foreach (string cat in SeaCategories) targets[cat] = Mathf.RoundToInt(sea[cat] * k);
			return targets;
		}

		/// <summary>How many objects of each land kind: density x the area of its zones.</summary>
		static Dictionary<string, int> LandTargets(IslandGenSettings s, Ground ground)
		{
			var targets = new Dictionary<string, int>();
			foreach (string cat in LandCategories)
			{
				float[] w = ZoneWeights(cat);
				float area = 0f;
				for (int z = 0; z < ZoneCount; z++) area += w[z] * ground.ZoneArea(z);
				float v = AmountOf(s, cat);
				targets[cat] = Mathf.RoundToInt(MaxDensity(cat) * v * v * area / 1000f);
			}
			return targets;
		}

		static void PlanNature(IslandGenSettings s, Ground ground, SpotGrid spots, List<IslandObject> owners, List<string> cats, GenReport report, Dictionary<string, int> targets)
		{
			string[] names = PlaceableCatalog.CoreNames.ToArray(); // the same set every time, so a seed always gives the same island
			StylePools pools = Pools[Mathf.Clamp(s.Style, 0, Pools.Length - 1)];
			Func<Regex, string[]> pick = r => names.Where(n => r.IsMatch(n)).ToArray();
			string[] trees = pick(pools.Trees), shoreTrees = pick(pools.ShoreTrees), bushes = pick(pools.Bushes), rocks = pick(pools.Rocks), beach = pick(pools.Beach);
			string[] landHarvest = pick(pools.LandHarvest), seaHarvest = pick(pools.SeaHarvest), water = pick(pools.Water);
			if (shoreTrees.Length == 0) shoreTrees = trees;

			// (targets: how many of each land kind, already scaled down past MaxObjects)
			for (int ci = 0; ci < LandCategories.Length; ci++)
			{
				string cat = LandCategories[ci];
				int target = targets[cat];
				if (target <= 0) continue;
				var rnd = new System.Random(s.Seed * 7919 + 13 + ci * 1013);
				Vector2 clusterOff = RandomOffset(rnd);
				float[] w = ZoneWeights(cat);
				float[] cum = new float[ZoneCount];
				float total = 0f;
				for (int z = 0; z < ZoneCount; z++) { total += w[z] * ground.Zones[z].Count; cum[z] = total; }
				if (total <= 0f) continue;
				int placed = 0;
				for (int attempt = 0; attempt < target * 5 + 50 && placed < target; attempt++)
				{
					float r = (float)rnd.NextDouble() * total;
					int zone = 0;
					while (zone < ZoneCount - 1 && r >= cum[zone]) zone++;
					List<int> cells = ground.Zones[zone];
					if (cells.Count == 0) continue;
					int cell = cells[rnd.Next(cells.Count)];
					float x = (cell % ground.Res + (float)rnd.NextDouble() - 0.5f) * ground.Step, z0 = (cell / ground.Res + (float)rnd.NextDouble() - 0.5f) * ground.Step;
					float h = ground.At(x, z0), above = h - Sea;
					// (the jitter may have moved it into the water, or out of it)
					if (zone == ZWater ? above > -1f : above < 0.3f && zone != ZWet) continue;
					if (s.Clusters > 0f)
					{
						float n = Fbm(new Vector2(x, z0) / 28f + clusterOff, 3);
						if ((float)rnd.NextDouble() > Mathf.Lerp(1f, SS(-0.1f, 0.35f, n), s.Clusters)) continue;
					}
					string[] pool;
					switch (cat)
					{
						case CatTrees: pool = zone == ZBeach ? shoreTrees : trees; break;
						case CatBushes: pool = bushes; break;
						case CatRocks: pool = rocks; break;
						case CatBeach: pool = beach; break;
						case CatHarvest: pool = zone == ZWater ? seaHarvest : landHarvest; break;
						default: pool = water; break;
					}
					if (pool.Length == 0) continue;
					string kind = pool[rnd.Next(pool.Length)];
					float yaw = (float)rnd.NextDouble() * 360f;
					float scale = 0.85f + 0.3f * (float)rnd.NextDouble();
					float objectSize = SpawnSize(kind); // (as spawned: the prototype carries Raft's own scale)
					float maxSize = MaxSize(cat, zone);
					if (objectSize * scale > maxSize) scale = Mathf.Max(0.15f, maxSize / objectSize);
					float foot = Footprint(cat, objectSize * scale);
					if (!spots.Free(x, z0, foot)) continue;
					spots.Add(x, z0, foot);
					GameObject proto = PlaceableCatalog.Get(kind);
					Vector3 baseScale = proto != null ? proto.transform.localScale : Vector3.one;
					owners.Add(new IslandObject { Name = kind, Position = new Vector3(x, h, z0), EulerRotation = new Vector3(0, yaw, 0), Scale = baseScale * scale });
					cats.Add(cat);
					placed++;
				}
				report.Counts[cat] = placed;
			}
		}

		#endregion

		#region Under water, like Raft's islands

		/// <summary>The sea around the land: cells by depth band (RaftUnderwater.Bands), with their distance from the land.</summary>
		class SeaGround
		{
			public int Res;
			public float Step;
			/// <summary>Metres from the nearest land, per cell (z * Res + x).</summary>
			public float[] Coast;
			public readonly List<int>[] Bands = new List<int>[RaftUnderwater.BandCount];
			public float Area(int band) { return Bands[band].Count * Step * Step; }
		}

		/// <summary>How far from the land the sea's objects go (Raft: most within 70 m of the coast, big rocks further out).</summary>
		const float SeaReach = 170f;
		/// <summary>The deepest ground dressed (m): an island on a deep sea floor keeps its ground in a world down to about 110 m (IslandSpawner).</summary>
		const float MaxSeaDepth = 105f;

		static SeaGround SurveySea(float[,] m, float step)
		{
			int res = m.GetLength(0);
			var g = new SeaGround { Res = res, Step = step, Coast = new float[res * res] };
			for (int b = 0; b < RaftUnderwater.BandCount; b++) g.Bands[b] = new List<int>();
			// Distance (in cells) from the land: two chamfer passes
			float[] d = g.Coast;
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) d[z * res + x] = m[z, x] > Sea ? 0f : 1e9f;
			for (int pass = 0; pass < 2; pass++)
			{
				for (int z = 0; z < res; z++)
					for (int x = 0; x < res; x++)
					{
						int i = z * res + x;
						float v = d[i];
						if (x > 0) v = Mathf.Min(v, d[i - 1] + 1f);
						if (z > 0) { v = Mathf.Min(v, d[i - res] + 1f); if (x > 0) v = Mathf.Min(v, d[i - res - 1] + 1.414f); if (x < res - 1) v = Mathf.Min(v, d[i - res + 1] + 1.414f); }
						d[i] = v;
					}
				for (int z = res - 1; z >= 0; z--)
					for (int x = res - 1; x >= 0; x--)
					{
						int i = z * res + x;
						float v = d[i];
						if (x < res - 1) v = Mathf.Min(v, d[i + 1] + 1f);
						if (z < res - 1) { v = Mathf.Min(v, d[i + res] + 1f); if (x < res - 1) v = Mathf.Min(v, d[i + res + 1] + 1.414f); if (x > 0) v = Mathf.Min(v, d[i + res - 1] + 1.414f); }
						d[i] = v;
					}
			}
			for (int z = 1; z < res - 1; z++)
				for (int x = 1; x < res - 1; x++)
				{
					int i = z * res + x;
					d[i] *= step;
					float e = m[z, x], depth = Sea - e;
					// (not the land, and not the flat floor at the terrain's base, as Raft's measurements leave out its deep floor)
					if (depth < 0.3f || e < 0.5f || depth > MaxSeaDepth || d[i] > SeaReach) continue;
					g.Bands[RaftUnderwater.BandOf(depth)].Add(i);
				}
			return g;
		}

		/// <summary>How many objects of each under-water kind (before the cap): Raft's density per depth band x this island's ground in that band x the slider.</summary>
		static Dictionary<string, float> SeaTargets(IslandGenSettings s, SeaGround sea)
		{
			var wanted = SeaCategories.ToDictionary(c => c, c => 0f);
			foreach (string cat in SeaCategories)
			{
				float f = SeaFactor(AmountOf(s, cat));
				if (f <= 0f) continue;
				foreach (SeaThing t in SeaThingsOf(s.Style, cat))
					for (int b = 0; b < RaftUnderwater.BandCount; b++) wanted[cat] += t.Density[b] * sea.Area(b) * f;
			}
			return wanted;
		}

		static HashSet<string> coreNames;
		static int coreNamesCount = -1;

		/// <summary>The measured kinds of a style that this editor has (the catalog's core objects, so a seed always gives the same island).</summary>
		static List<SeaThing> SeaThingsOf(int style, string cat)
		{
			if (!PlaceableCatalog.IsBuilt) return new List<SeaThing>();
			List<string> core = PlaceableCatalog.CoreNames.ToList();
			if (coreNames == null || core.Count != coreNamesCount) { coreNames = new HashSet<string>(core); coreNamesCount = core.Count; }
			List<SeaThing> list = RaftUnderwater.For(style).Of(cat).Where(t => coreNames.Contains(t.Name)).ToList();
			// (every sea has something to collect: Temperance's bare, icy rock borrows the tropical islands' finds)
			if (list.Count == 0 && cat == CatSeaFinds) list = RaftUnderwater.For(TerrainPainter.Tropical).Of(cat).Where(t => coreNames.Contains(t.Name)).ToList();
			return list;
		}

		/// <summary>Largest dimension (m) of a catalog object spawned at its prototype's own scale (the scale its Raft original had).</summary>
		static float SpawnSize(string name)
		{
			GameObject proto = PlaceableCatalog.Get(name);
			Vector3 ls = proto != null ? proto.transform.localScale : Vector3.one;
			return PlaceableCatalog.ApproxSize(name) * Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
		}

		/// <summary>How close things under water may stand.</summary>
		static float SeaFootprint(string cat, float size)
		{
			switch (cat)
			{
				case CatSeaRocks: return Mathf.Clamp(size * 0.2f, 0.6f, 5f);
				case CatSunken: return Mathf.Clamp(size * 0.3f, 0.6f, 3f);
				case CatSeaFinds: return 0.5f;
				default: return Mathf.Clamp(size * 0.25f, 0.35f, 1.8f);
			}
		}

		/// <summary>
		/// Dresses the sea like Raft's islands of the style (RaftUnderwater): per depth band, each measured kind as dense
		/// as there (times the slider), at the depths and distances from the coast it has there; ores and cliff rocks keep
		/// to steep slopes; big rock formations sink into the drop-off as deep as Raft's do; corals gather in reefs.
		/// </summary>
		static void PlanSea(IslandGenSettings s, Ground ground, SeaGround sea, SpotGrid spots, List<IslandObject> owners, List<string> cats, GenReport report, float cap)
		{
			for (int ci = 0; ci < SeaCategories.Length; ci++)
			{
				string cat = SeaCategories[ci];
				report.Counts[cat] = 0;
				float f = SeaFactor(AmountOf(s, cat)) * cap;
				if (f <= 0f) continue;
				var rnd = new System.Random(s.Seed * 4099 + 71 + ci * 577);
				Vector2 reefOff = RandomOffset(rnd);
				int placed = 0;
				foreach (SeaThing t in SeaThingsOf(s.Style, cat))
				{
					GameObject proto = PlaceableCatalog.Get(t.Name);
					if (proto == null) continue;
					bool pickup = t.Name.StartsWith("Pickup_");
					float protoSize = Mathf.Max(0.2f, SpawnSize(t.Name)); // (as spawned at the prototype's scale, like Raft's measured size)
					for (int b = 0; b < RaftUnderwater.BandCount; b++)
					{
						List<int> cells = sea.Bands[b];
						if (cells.Count == 0 || t.Density[b] <= 0f) continue;
						float expected = t.Density[b] * sea.Area(b) * f;
						int n = (int)expected + (rnd.NextDouble() < expected - (int)expected ? 1 : 0);
						for (int attempt = 0, got = 0; attempt < n * 8 + 4 && got < n; attempt++)
						{
							int cell = cells[rnd.Next(cells.Count)];
							float x = (cell % sea.Res + (float)rnd.NextDouble() - 0.5f) * sea.Step, z = (cell / sea.Res + (float)rnd.NextDouble() - 0.5f) * sea.Step;
							float h = ground.At(x, z), depth = Sea - h;
							if (depth < 0.3f || RaftUnderwater.BandOf(depth) != b) continue;
							if (sea.Coast[cell] > t.CoastHigh * 1.3f + 6f) continue;
							float slope = ground.Slope(x, z);
							if (t.Slope >= 40f) { if ((float)rnd.NextDouble() > SS(12f, t.Slope, slope) + 0.08f) continue; } // (ores and cliff rocks keep to the steep slopes)
							else if (slope > 62f) continue;
							if (cat == CatWater && s.Clusters > 0f)
							{
								// (Raft's corals grow in reefs with sand between them)
								float nz = Fbm(new Vector2(x, z) / 16f + reefOff, 3);
								if ((float)rnd.NextDouble() > Mathf.Lerp(1f, SS(-0.2f, 0.3f, nz), s.Clusters)) continue;
							}
							// As big as Raft's (a little either way); never much taller than the water is deep
							float scale = pickup ? 1f : Mathf.Clamp(t.Size / protoSize, 0.3f, 3f) * (0.8f + 0.4f * (float)rnd.NextDouble());
							float actual = protoSize * scale;
							// (only rocks right at the shore break the surface, as on Raft's islands; further out they stay under water)
							float room = sea.Coast[cell] < 8f ? Mathf.Max(4f, depth * 1.3f) : Mathf.Max(2.5f, depth * 0.8f);
							if (!pickup && actual > room) { scale *= room / actual; actual = protoSize * scale; }
							float foot = SeaFootprint(cat, actual);
							if (!spots.Free(x, z, foot)) continue;
							spots.Add(x, z, foot);
							float y = h;
							if (t.Above < -0.3f && t.Size > 0.5f) y += Mathf.Max(t.Above / t.Size, -1f) * actual; // (sunk into the slope as on Raft's islands)
							else if (pickup && slope > 30f) y -= 0.15f;
							float yaw = (float)rnd.NextDouble() * 360f;
							Vector3 euler = cat == CatSeaRocks ? new Vector3(((float)rnd.NextDouble() - 0.5f) * 16f, yaw, ((float)rnd.NextDouble() - 0.5f) * 16f) : new Vector3(0f, yaw, 0f);
							owners.Add(new IslandObject { Name = t.Name, Position = new Vector3(x, y, z), EulerRotation = euler, Scale = proto.transform.localScale * scale });
							cats.Add(cat);
							got++; placed++;
						}
					}
				}
				report.Counts[cat] = placed;
			}
		}

		#endregion

		#region Content: creatures and loot

		/// <summary>The hostile creatures of each style (AI types), when no kinds are chosen.</summary>
		static readonly string[][] StyleHostiles =
		{
			new[] { "Boar", "StoneBird" }, new[] { "PolarBear" }, new[] { "Hyena", "Rat" }, new[] { "Bear", "Boar", "BugSwarm_Bee" }, new[] { "Rat", "Roach", "StoneBird" },
		};
		static readonly string[][] StyleFriendly =
		{
			new[] { "Chicken", "Goat" }, new[] { "Goat" }, new[] { "Llama", "Chicken" }, new[] { "Goat", "Chicken" }, new[] { "Chicken" },
		};
		static readonly string[] SeaKinds = { "Turtle", "Stingray", "PufferFish", "Dolphin" };

		/// <summary>The hostile kinds used for these settings (the chosen ones that exist, else the style's).</summary>
		public static string[] HostileKindsOf(IslandGenSettings s)
		{
			string[] chosen = (s.HostileKinds ?? "").Split(',').Select(k => k.Trim()).Where(k => k.Length > 0 && ContentCatalog.IsCreature("Creature_" + k)).ToArray();
			return chosen.Length > 0 ? chosen : StyleHostiles[Mathf.Clamp(s.Style, 0, StyleHostiles.Length - 1)];
		}

		/// <summary>How many animals live at one spot of a kind.</summary>
		static int HerdSize(string kind, System.Random rnd)
		{
			switch (kind)
			{
				case "Rat": case "Rat_Tangaroa": case "Roach": case "Hyena": case "PufferFish": return 2 + rnd.Next(2);
				case "Chicken": return 2 + rnd.Next(3);
				case "Goat": case "Llama": case "Boar": case "Pig": case "Turtle": case "Stingray": case "Dolphin": return 1 + rnd.Next(2);
				default: return 1;
			}
		}

		/// <summary>The loot tiers: candidate stacks "Item*min-max" ("a|b" = one of them). Only items this Raft has are used.</summary>
		public static readonly string[][] LootTiers =
		{
			new[] { "Plank*8-15", "Plastic*6-12", "Thatch*6-10", "Rope*2-4" },
			new[] { "Nail*6-12", "Stone*4-8", "Scrap*3-6", "Coconut*2-3|Watermelon*1-2|Raw_Potato*2-4|Mango*2-3|Banana*2-3|Pineapple*1-2" },
			new[] { "MetalOre*3-6", "CopperOre*2-5", "Bolt*3-6", "Hinge*2-4" },
			new[] { "MetalIngot*3-6", "CopperIngot*2-5", "CircuitBoard*1-2" },
			new[] { "TitaniumIngot*1-3", "ExplosiveGoo*1-3", "Battery*1-1", "HealingSalve_Good*1-2" },
		};

		/// <summary>The box each tier comes in (1 barrel ... 5 large chest); under water always a sunken barrel.</summary>
		static readonly string[] TierBoxes = { "Loot_Barrel", "Loot_ChestSmall", "Loot_Crate", "Loot_Chest", "Loot_ChestLarge" };
		static readonly string[] TierTitles = { "Supplies", "Small chest", "Crate", "Chest", "Treasure chest" };

		/// <summary>A box's items for a tier (1..5): each of the tier's stacks, and now and then one from the tier below.</summary>
		public static string TierLoot(int tier, System.Random rnd)
		{
			tier = Mathf.Clamp(tier, 1, 5);
			var stacks = new List<KeyValuePair<string, int>>();
			Action<string> add = entry =>
			{
				string[] options = entry.Split('|');
				string[] p = options[rnd.Next(options.Length)].Split('*');
				string[] range = p.Length > 1 ? p[1].Split('-') : new[] { "1" };
				int min = int.Parse(range[0], CultureInfo.InvariantCulture), max = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : min;
				int amount = min + rnd.Next(max - min + 1);
				if (ContentCatalog.ItemExists(p[0]) && !stacks.Any(x => x.Key == p[0])) stacks.Add(new KeyValuePair<string, int>(p[0], amount));
			};
			foreach (string entry in LootTiers[tier - 1]) add(entry);
			if (tier > 1 && rnd.NextDouble() < 0.4) add(LootTiers[tier - 2][rnd.Next(LootTiers[tier - 2].Length)]);
			return ObjectProps.LootText(stacks);
		}

		static void PlanContent(IslandGenSettings s, Ground ground, SpotGrid spots, List<IslandObject> owners, List<string> cats, GenReport report, List<IslandObject> result)
		{
			var rnd = new System.Random(s.Seed * 31 + 7);
			float apart = Mathf.Clamp(s.Radius * 0.22f, 8f, 30f);
			var creatureSpots = new List<Vector2>();
			Func<int[], Func<float, float, bool>, float, Vector2?> find = (zones, ok, keepApart) =>
			{
				// (a spot in one of the zones where ok(height above the sea, slope) holds, away from the other creatures)
				if (zones.All(zz => ground.Zones[zz].Count == 0)) return null;
				for (int tries = 0; tries < 300; tries++)
				{
					List<int> cells = ground.Zones[zones[rnd.Next(zones.Length)]];
					if (cells.Count == 0) continue;
					int cell = cells[rnd.Next(cells.Count)];
					float x = (cell % ground.Res) * ground.Step, z = (cell / ground.Res) * ground.Step;
					float above = ground.At(x, z) - Sea;
					if (!ok(above, ground.Slope(x, z))) continue;
					float need = tries < 200 ? keepApart : keepApart * 0.4f;
					if (creatureSpots.Any(c => (c - new Vector2(x, z)).sqrMagnitude < need * need)) continue;
					return new Vector2(x, z);
				}
				return null;
			};
			float[] stats = ObjectProps.Presets.First(p => p.Key == s.Difficulty).Value;
			Action<string, Vector2, float> creature = (kind, p, lift) =>
			{
				spots.Clear(p.x, p.y, 2.5f); // (room for the animals: the nature there goes)
				var props = new Dictionary<string, string> { { ObjectProps.CreatureCount, HerdSize(kind, rnd).ToString(CultureInfo.InvariantCulture) } };
				if (s.Difficulty != "Normal" && ContentCatalog.CreatureOf("Creature_" + kind) != null && ContentCatalog.CreatureOf("Creature_" + kind).Category == ContentCatalog.HostileCategory)
				{
					props[ObjectProps.CreatureHealth] = ObjectProps.Format(stats[0]);
					props[ObjectProps.CreatureDamage] = ObjectProps.Format(stats[1]);
					props[ObjectProps.CreatureSpeed] = ObjectProps.Format(stats[2]);
				}
				GameObject proto = PlaceableCatalog.Get("Creature_" + kind);
				result.Add(new IslandObject { Name = "Creature_" + kind, Position = new Vector3(p.x, ground.At(p.x, p.y) + lift, p.y), EulerRotation = new Vector3(0, (float)rnd.NextDouble() * 360f, 0), Scale = proto != null ? proto.transform.localScale : Vector3.one, Props = props });
				spots.Add(p.x, p.y, 1.5f);
				owners.Add(null);
				cats.Add(null);
				creatureSpots.Add(p);
				report.Creatures++;
			};

			string[] hostiles = HostileKindsOf(s);
			for (int i = 0; i < s.Hostiles; i++)
			{
				string kind = hostiles[rnd.Next(hostiles.Length)];
				bool flies = kind == "StoneBird" || kind == "BugSwarm_Bee";
				Vector2? p = find(new[] { ZLand }, (above, slope) => above > 3f && slope < 22f, apart);
				if (p.HasValue) creature(kind, p.Value, flies ? 2f : 0f);
			}
			string[] friendly = StyleFriendly[Mathf.Clamp(s.Style, 0, StyleFriendly.Length - 1)];
			for (int i = 0; i < s.Friendly; i++)
			{
				Vector2? p = find(new[] { ZLand, ZBeach }, (above, slope) => above > 1.5f && slope < 18f, apart * 0.6f);
				if (p.HasValue) creature(friendly[rnd.Next(friendly.Length)], p.Value, 0f);
			}
			for (int i = 0; i < s.SeaLife; i++)
			{
				Vector2? p = find(new[] { ZWater }, (above, slope) => above < -3f, apart * 0.5f);
				if (p.HasValue) creature(SeaKinds[rnd.Next(SeaKinds.Length)], p.Value, 1.5f + (float)rnd.NextDouble() * 1.5f);
			}

			// Loot boxes: out in the open, or tucked in next to a tree, bush or rock; now and then sunken
			var hideBy = new List<int>();
			for (int i = 0; i < owners.Count; i++)
			{
				IslandObject o = owners[i];
				if (o != null && !spots.Removed[i] && o.Position.y > Sea + 0.6f && !o.Name.StartsWith("Pickup_")) hideBy.Add(i);
			}
			for (int i = 0; i < s.Loot; i++)
			{
				int tier = s.LootMin + rnd.Next(s.LootMax - s.LootMin + 1);
				bool sunken = ground.Zones[ZWater].Count > 0 && rnd.NextDouble() < 0.2;
				Vector2? spot = null;
				for (int tries = 0; tries < 200 && !spot.HasValue; tries++)
				{
					Vector2 p;
					if (sunken)
					{
						int cell = ground.Zones[ZWater][rnd.Next(ground.Zones[ZWater].Count)];
						p = new Vector2((cell % ground.Res) * ground.Step, (cell / ground.Res) * ground.Step);
						if (ground.At(p.x, p.y) - Sea > -2f) continue;
					}
					else if (s.LootHidden && hideBy.Count > 0 && tries < 150)
					{
						Vector3 by = spots.Spots[hideBy[rnd.Next(hideBy.Count)]];
						float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = by.z + 0.9f + (float)rnd.NextDouble() * 0.8f;
						p = new Vector2(by.x + Mathf.Cos(a) * d, by.y + Mathf.Sin(a) * d);
					}
					else
					{
						List<int> cells = rnd.NextDouble() < 0.7 ? ground.Zones[ZLand] : ground.Zones[ZBeach];
						if (cells.Count == 0) cells = ground.Zones[ZBeach];
						if (cells.Count == 0) break;
						int cell = cells[rnd.Next(cells.Count)];
						p = new Vector2((cell % ground.Res) * ground.Step, (cell / ground.Res) * ground.Step);
					}
					float above = ground.At(p.x, p.y) - Sea;
					if (!sunken && (above < 0.6f || ground.Slope(p.x, p.y) > 25f)) continue;
					if (!s.LootHidden && !sunken && !spots.Free(p.x, p.y, 0.8f)) continue;
					spot = p;
				}
				if (!spot.HasValue) continue;
				Vector2 at = spot.Value;
				spots.Clear(at.x, at.y, s.LootHidden ? 0.6f : 1.5f);
				string box = sunken ? "Loot_SunkenBarrel" : TierBoxes[tier - 1];
				if (tier == 1 && !sunken && rnd.NextDouble() < 0.5) box = "Loot_Box";
				var props = new Dictionary<string, string> { { ObjectProps.LootItems, TierLoot(tier, rnd) }, { ObjectProps.NoteTitle, (sunken ? "Sunken barrel" : TierTitles[tier - 1]) + " (tier " + tier + ")" } };
				GameObject proto = PlaceableCatalog.Get(box);
				result.Add(new IslandObject { Name = box, Position = new Vector3(at.x, ground.At(at.x, at.y), at.y), EulerRotation = new Vector3(0, (float)rnd.NextDouble() * 360f, 0), Scale = proto != null ? proto.transform.localScale : Vector3.one, Props = props });
				spots.Add(at.x, at.y, 0.8f);
				owners.Add(null);
				cats.Add(null);
				report.Chests++;
			}
			// (nature cleared away for content no longer counts)
			foreach (string cat in Categories)
				report.Counts[cat] = 0;
			for (int i = 0; i < owners.Count; i++)
				if (owners[i] != null && !spots.Removed[i]) { string c = cats[i]; report.Counts[c] = report.Count(c) + 1; }
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
		public static IslandFile CreateFile(IslandGenSettings settings, string name)
		{
			IslandGenSettings s = settings.Copy();
			s.Clamp();
			float[,] metres = HeightsMetres(s, BuildArea, BuildResolution);
			UseSea(s);
			var file = new IslandFile
			{
				Name = name,
				WaterLevel = s.WaterLevel,
				TerrainSize = BuildArea,
				HeightmapResolution = BuildResolution,
				Heights = Normalised(metres, BuildArea.y),
				Style = s.Style == TerrainPainter.Tropical ? "" : TerrainPainter.StyleName(s.Style),
			};
			file.Objects = PlanAll(s, metres, BuildArea);
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
			var s = new IslandGenSettings
			{
				Seed = rnd.Next(1, 999999),
				Radius = 52f + (float)rnd.NextDouble() * 96f, // fits between Raft's own islands more often
				Height = 15f + (float)rnd.NextDouble() * 55f,
				Roughness = 0.3f + (float)rnd.NextDouble() * 0.6f,
				Peaks = 1 + rnd.Next(3),
				ObjectDensity = 0.4f + (float)rnd.NextDouble() * 0.4f,
				Style = styles != null && styles.Length > 0 ? styles[rnd.Next(styles.Length)] : TerrainPainter.Tropical,
			};
			// Now and then an island that looks different: long, with bays, cliffs or a valley
			if (rnd.NextDouble() < 0.4) { s.Stretch = 1f + (float)rnd.NextDouble() * 0.9f; s.StretchAngle = (float)rnd.NextDouble() * 180f; }
			if (rnd.NextDouble() < 0.3) s.Bays = 0.3f + (float)rnd.NextDouble() * 0.5f;
			if (rnd.NextDouble() < 0.3) s.Cliffs = 0.2f + (float)rnd.NextDouble() * 0.5f;
			if (rnd.NextDouble() < 0.25) s.Valleys = 0.3f + (float)rnd.NextDouble() * 0.4f;
			// Under water: a gentle slope or a wall into the deep, as Raft's islands vary
			s.DropOff = 0.15f + (float)rnd.NextDouble() * 0.8f;
			return s;
		}

		#endregion

		#region Preview

		/// <summary>
		/// A top-down picture of heights (m above the terrain's base, from HeightsMetres): the sea by depth, the land
		/// in the style's colours by height and slope, lit from the north-west. Reuses the texture when it fits.
		/// </summary>
		public static Texture2D Preview(float[,] m, float step, int style, Texture2D reuse = null, float waterLevel = IslandFile.DefaultWaterLevel)
		{
			float sea = waterLevel;
			int res = m.GetLength(0);
			Texture2D tex = reuse != null && reuse.width == res && reuse.height == res ? reuse : new Texture2D(res, res, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CI_GeneratorPreview" };
			var px = new Color32[res * res];
			Color sand, grass, rock, high;
			switch (style)
			{
				case TerrainPainter.Snowy: sand = new Color(0.78f, 0.8f, 0.82f); grass = new Color(0.93f, 0.95f, 0.98f); rock = new Color(0.5f, 0.52f, 0.56f); high = Color.white; break;
				case TerrainPainter.Desert: sand = new Color(0.93f, 0.8f, 0.55f); grass = new Color(0.82f, 0.64f, 0.42f); rock = new Color(0.66f, 0.38f, 0.28f); high = new Color(0.75f, 0.5f, 0.35f); break;
				case TerrainPainter.Forest: sand = new Color(0.88f, 0.82f, 0.62f); grass = new Color(0.25f, 0.45f, 0.2f); rock = new Color(0.5f, 0.49f, 0.47f); high = new Color(0.42f, 0.44f, 0.4f); break;
				case TerrainPainter.Volcanic: sand = new Color(0.33f, 0.31f, 0.3f); grass = new Color(0.27f, 0.25f, 0.24f); rock = new Color(0.42f, 0.26f, 0.2f); high = new Color(0.2f, 0.18f, 0.18f); break;
				default: sand = new Color(0.94f, 0.86f, 0.62f); grass = new Color(0.36f, 0.62f, 0.27f); rock = new Color(0.55f, 0.53f, 0.5f); high = new Color(0.45f, 0.5f, 0.38f); break;
			}
			float top = 1f;
			foreach (float v in m) top = Mathf.Max(top, v - sea);
			Vector3 light = new Vector3(-0.55f, 0.75f, 0.4f).normalized;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					float e = m[z, x], above = e - sea;
					float gx = (m[z, Mathf.Min(res - 1, x + 1)] - m[z, Mathf.Max(0, x - 1)]) / (2f * step);
					float gz = (m[Mathf.Min(res - 1, z + 1), x] - m[Mathf.Max(0, z - 1), x]) / (2f * step);
					Color c;
					if (above < 0f)
					{
						float depth = -above;
						c = Color.Lerp(new Color(0.42f, 0.8f, 0.82f), new Color(0.1f, 0.36f, 0.58f), SS(0f, 12f, depth));
						c = Color.Lerp(c, new Color(0.06f, 0.24f, 0.44f), SS(12f, 20f, depth));
						c = Color.Lerp(c, new Color(0.02f, 0.1f, 0.22f), SS(25f, 110f, depth)); // (the drop-off into the deep)
					}
					else
					{
						float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
						c = Color.Lerp(sand, grass, SS(ShelfHeight, ShelfHeight + 2f, above));
						c = Color.Lerp(c, high, SS(0.55f, 0.95f, above / top) * 0.7f);
						c = Color.Lerp(c, rock, SS(28f, 40f, slope));
						Vector3 n = new Vector3(-gx, 1f, -gz).normalized;
						c *= Mathf.Clamp(0.55f + 0.6f * Vector3.Dot(n, light), 0.35f, 1.25f);
					}
					px[z * res + x] = c;
				}
			tex.SetPixels32(px);
			tex.Apply(false);
			return tex;
		}

		#endregion
	}
}
