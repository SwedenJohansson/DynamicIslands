using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A kind of island the generator can make on its own: the ranges its settings are rolled from (style, layout,
	/// size), how high it floats, and the content it gets (chests, notes, zones, creatures, atmosphere, a quest).
	/// World plans ("type:&lt;name&gt;"), the quest editor's "bring a new island" and the editor's Generate window use them.
	/// </summary>
	public class MapType
	{
		public string Name, Label, Description;
		/// <summary>Shown to players on arrival (the island's info.title), or "" for none.</summary>
		public string Title = "";
		/// <summary>Settings for one island of this type (the seed comes from rnd too).</summary>
		public Func<System.Random, IslandGenSettings> Settings;
		/// <summary>Chance of floating in the air, and how high then (m above sea level).</summary>
		public float FlyingChance;
		public float FlyingMin = 40f, FlyingMax = 90f;
		/// <summary>Under water: its top this many metres below the surface (0 = not sunken).</summary>
		public float SunkenDepth;
		/// <summary>What is placed on the generated land (optional).</summary>
		public Action<MapKit, IslandGenSettings> Content;
		/// <summary>Makes the whole file instead of the generator (objects-only places such as a wreck).</summary>
		public Func<IslandGenSettings, string, IslandFile> Build;
	}

	/// <summary>
	/// Places content on a generated island file: heights come from the file, positions are terrain-local (as in
	/// IslandFile), and content pushes aside scattered nature around it. Deterministic for a seed.
	/// </summary>
	public class MapKit
	{
		public readonly IslandFile File;
		public readonly System.Random Rnd;
		readonly float step;
		readonly List<Vector2> placed = new List<Vector2>();
		/// <summary>Terrain height of the sea (the file's water level: 20 m on a shallow seabed, 160 m on a deep sea floor).</summary>
		public float Sea { get { return File.WaterLevel; } }

		public MapKit(IslandFile file, int seed)
		{
			File = file;
			Rnd = new System.Random(seed * 31 + 7);
			step = file.TerrainSize.x / (file.HeightmapResolution - 1);
		}

		/// <summary>The middle of the build area (generated land is centred on it).</summary>
		public Vector2 Mid { get { return new Vector2(File.TerrainSize.x / 2f, File.TerrainSize.z / 2f); } }

		/// <summary>Ground height (m above the terrain's base; the sea is at Sea).</summary>
		public float Ground(Vector2 p) { return IslandGenerator.SampleHeights(File.Heights, File.HeightmapResolution, step, p.x, p.y) * File.TerrainSize.y; }

		public float Slope(Vector2 p)
		{
			float dx = (Ground(p + new Vector2(step, 0)) - Ground(p - new Vector2(step, 0))) / (2f * step);
			float dz = (Ground(p + new Vector2(0, step)) - Ground(p - new Vector2(0, step))) / (2f * step);
			return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
		}

		public Vector3 At(Vector2 p, float lift = 0f) { return new Vector3(p.x, Ground(p) + lift, p.y); }

		/// <summary>The highest ground within radius of a point.</summary>
		public Vector2 Highest(Vector2 around, float radius)
		{
			Vector2 best = around;
			float h = float.MinValue;
			for (float x = -radius; x <= radius; x += 2f)
				for (float z = -radius; z <= radius; z += 2f)
				{
					if (x * x + z * z > radius * radius) continue;
					Vector2 p = around + new Vector2(x, z);
					float g = Ground(p);
					if (g > h) { h = g; best = p; }
				}
			return best;
		}

		/// <summary>A random spot within radius where ok(height above the sea, slope) holds and nothing else was put, or null.</summary>
		public Vector2? Find(Vector2 around, float radius, Func<float, float, bool> ok, float keepApart = 8f)
		{
			for (int i = 0; i < 600; i++)
			{
				float a = (float)Rnd.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)Rnd.NextDouble()) * radius;
				Vector2 p = around + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
				if (placed.Any(q => (q - p).sqrMagnitude < keepApart * keepApart)) continue;
				if (ok(Ground(p) - Sea, Slope(p))) return p;
			}
			return null;
		}

		/// <summary>Removes scattered nature (objects without settings) around a spot, so content isn't inside a tree.</summary>
		public void Clear(Vector2 p, float radius)
		{
			File.Objects.RemoveAll(o => (o.Props == null || o.Props.Count == 0) && new Vector2(o.Position.x - p.x, o.Position.z - p.y).sqrMagnitude < radius * radius);
		}

		public IslandObject Add(string name, Vector3 position, float yaw = 0f, Dictionary<string, string> props = null, float clear = 3f)
		{
			var p2 = new Vector2(position.x, position.z);
			if (clear > 0f) Clear(p2, clear);
			GameObject proto = PlaceableCatalog.Get(name);
			var o = new IslandObject { Name = name, Position = position, EulerRotation = new Vector3(0, yaw, 0), Scale = proto != null ? proto.transform.localScale : Vector3.one, Props = props };
			File.Objects.Add(o);
			placed.Add(p2);
			return o;
		}

		float Yaw() { return (float)Rnd.NextDouble() * 360f; }

		/// <summary>Loot of one of the chest presets (Basics, Metal, Food, Treasure), with the items this Raft has.</summary>
		public static string Loot(string preset)
		{
			var p = ContentCatalog.LootPresets.FirstOrDefault(x => x.Key == preset);
			return p.Value != null ? ContentCatalog.PresetLoot(p.Value) : ContentCatalog.DefaultLoot();
		}

		/// <summary>A container with loot; the title is what quests and players call it.</summary>
		public IslandObject Chest(string kind, Vector2 p, string title, string loot, string note = "", float lift = 0f)
		{
			var props = new Dictionary<string, string> { { ObjectProps.LootItems, loot } };
			if (title.Length > 0) props[ObjectProps.NoteTitle] = title;
			if (note.Length > 0) props[ObjectProps.NoteText] = note;
			return Add(kind, At(p, lift), Yaw(), props, 4f);
		}

		public IslandObject Note(string kind, Vector2 p, string title, string text)
		{
			return Add(kind, At(p), Yaw(), new Dictionary<string, string> { { ObjectProps.NoteTitle, title }, { ObjectProps.NoteText, text } });
		}

		public IslandObject Zone(Vector2 p, string id, float radius, string message = "")
		{
			var props = new Dictionary<string, string> { { ObjectProps.ZoneId, id }, { ObjectProps.ZoneRadius, radius.ToString("0.#", CultureInfo.InvariantCulture) } };
			if (message.Length > 0) props[ObjectProps.ZoneMessage] = message;
			return Add(ContentCatalog.TriggerZone, At(p), 0f, props, 0f);
		}

		/// <summary>Creatures at a spot (type as in Creature_&lt;type&gt;), with a difficulty preset (ObjectProps.Presets).</summary>
		public IslandObject Creature(string type, Vector2 p, int count, string preset = "Normal", float size = 1f, string zone = null, bool respawn = true, float lift = 0f)
		{
			var props = new Dictionary<string, string> { { ObjectProps.CreatureCount, Mathf.Clamp(count, 1, ObjectProps.MaxCount).ToString() } };
			float[] m = ObjectProps.Presets.FirstOrDefault(x => x.Key == preset).Value;
			if (m != null && preset != "Normal")
			{
				props[ObjectProps.CreatureHealth] = m[0].ToString("0.##", CultureInfo.InvariantCulture);
				props[ObjectProps.CreatureDamage] = m[1].ToString("0.##", CultureInfo.InvariantCulture);
				props[ObjectProps.CreatureSpeed] = m[2].ToString("0.##", CultureInfo.InvariantCulture);
			}
			if (size != 1f) props[ObjectProps.CreatureSize] = size.ToString("0.##", CultureInfo.InvariantCulture);
			if (zone != null) props[ObjectProps.CreatureZone] = zone;
			if (!respawn) props[ObjectProps.CreatureRespawn] = "0";
			return Add("Creature_" + type, At(p, lift), Yaw(), props, 2f);
		}

		/// <summary>An atmosphere zone: fog and light colours (#RRGGBB) and amounts, particles (AtmosphereZone.ParticleKinds).</summary>
		public IslandObject Atmosphere(Vector2 p, float radius, string fog, float fogAmount, string light, float lightAmount, string particles)
		{
			var props = new Dictionary<string, string>
			{
				{ ObjectProps.ZoneRadius, radius.ToString("0.#", CultureInfo.InvariantCulture) },
				{ ObjectProps.AtmoFog, fog }, { ObjectProps.AtmoFogAmount, fogAmount.ToString("0.##", CultureInfo.InvariantCulture) },
				{ ObjectProps.AtmoLight, light }, { ObjectProps.AtmoLightAmount, lightAmount.ToString("0.##", CultureInfo.InvariantCulture) },
				{ ObjectProps.AtmoParticles, particles },
			};
			return Add(ContentCatalog.AtmosphereZoneName, At(p), 0f, props, 0f);
		}

		/// <summary>The island's quest; steps as "type|target|count|text".</summary>
		public void Quest(string title, string intro, string done, string reward, params string[] steps)
		{
			var q = new IslandQuest { Title = title, Intro = intro, Done = done, Reward = reward };
			foreach (string line in steps)
			{
				string[] p = line.Split('|');
				int n;
				q.Steps.Add(new IslandQuest.Step { Type = p[0], Target = p.Length > 1 ? p[1] : "", Count = p.Length > 2 && int.TryParse(p[2], out n) ? n : 1, Text = p.Length > 3 ? p[3] : "" });
			}
			q.To(File.Props);
		}

		/// <summary>Terrain-local position of a point given relative to the middle (generator islets and stacks).</summary>
		public Vector2 FromMid(Vector4 o) { return Mid + new Vector2(o.x, o.y); }

		public static bool Dry(float above, float slope) { return above > 1.2f && slope < 18f; }
		public static bool Beach(float above, float slope) { return above > 0.6f && above < 3.5f && slope < 15f; }
	}

	/// <summary>The map types (built in). Plans and the spawner refer to them by name.</summary>
	public static class MapTypes
	{
		static readonly int[] AllStyles = { TerrainPainter.Tropical, TerrainPainter.Snowy, TerrainPainter.Desert, TerrainPainter.Forest, TerrainPainter.Volcanic };

		static float R(System.Random rnd, float min, float max) { return min + (float)rnd.NextDouble() * (max - min); }

		static IslandGenSettings Gen(System.Random rnd, int style, int shape, float rMin, float rMax, float hMin, float hMax, float rough, float density, int peaksMax = 3)
		{
			return new IslandGenSettings
			{
				Seed = rnd.Next(1, 999999), Style = style, Shape = shape, Radius = R(rnd, rMin, rMax), Height = R(rnd, hMin, hMax),
				Roughness = Mathf.Clamp01(rough + R(rnd, -0.15f, 0.15f)), Peaks = 1 + rnd.Next(peaksMax), ObjectDensity = density,
			};
		}

		static int Pick(System.Random rnd, params int[] styles) { return styles[rnd.Next(styles.Length)]; }

		public static readonly List<MapType> All = new List<MapType>
		{
			new MapType { Name = "random", Label = "Random island", Description = "Any style and size; now and then a flying one", FlyingChance = 0.1f,
				Settings = rnd => IslandGenerator.RandomSettings(rnd, AllStyles) },
			Styled("tropical", "Tropical island", "Palms, bushes and fruit trees", TerrainPainter.Tropical),
			Styled("snowy", "Snowy island", "Pines and snow drifts (Temperance)", TerrainPainter.Snowy),
			Styled("desert", "Desert island", "Cacti and dry grass (Caravan)", TerrainPainter.Desert),
			Styled("forest", "Forest island", "Birches and pines (Balboa)", TerrainPainter.Forest),
			Styled("volcanic", "Volcanic island", "A cone with a crater", TerrainPainter.Volcanic),

			new MapType { Name = "sandbar", Label = "Sandbar", Description = "A tiny island with a few palms and a small chest: a rest stop between islands",
				Settings = rnd => Gen(rnd, TerrainPainter.Tropical, IslandShapes.Round, 12f, 23f, 3f, 4f, 0.25f, 0.9f, 1),
				Content = (k, s) => k.Chest("Loot_ChestSmall", k.Highest(k.Mid, s.Radius), "Driftwood cache", MapKit.Loot("Basics")) },

			new MapType { Name = "atoll", Label = "Atoll", Title = "Atoll", Description = "A ring of low land around a shallow lagoon, with turtles and a sunken barrel",
				Settings = rnd => Gen(rnd, TerrainPainter.Tropical, IslandShapes.Atoll, 110f, 170f, 4f, 8f, 0.6f, 0.6f),
				Content = (k, s) =>
				{
					k.Chest("Loot_SunkenBarrel", k.Mid, "Sunken barrel", MapKit.Loot("Metal"));
					Vector2? lagoon = k.Find(k.Mid, s.Radius * 0.4f, (above, slope) => above < -1.5f);
					if (lagoon.HasValue) k.Creature("Turtle", lagoon.Value, 2);
				} },

			new MapType { Name = "archipelago", Label = "Archipelago", Title = "Archipelago", Description = "Several islets on a shallow shelf; a castaway hid three caches on them (quest)",
				Settings = rnd => Gen(rnd, Pick(rnd, TerrainPainter.Tropical, TerrainPainter.Forest), IslandShapes.Archipelago, 130f, 200f, 12f, 30f, 0.5f, 0.55f),
				Content = Archipelago },

			new MapType { Name = "stacks", Label = "Sea stacks", Title = "Sea stacks", Description = "Steep rock pillars; a chest waits on top of the tallest (quest: build your way up)",
				Settings = rnd => Gen(rnd, Pick(rnd, TerrainPainter.Tropical, TerrainPainter.Desert, TerrainPainter.Snowy), IslandShapes.Stacks, 90f, 150f, 25f, 55f, 0.6f, 0.45f),
				Content = Stacks },

			new MapType { Name = "boss", Label = "Boss island", Title = "The plateau", Description = "A flat-topped mesa with a ramp; walking into the arena wakes a huge beast (quest, big reward)",
				Settings = rnd => Gen(rnd, Pick(rnd, TerrainPainter.Forest, TerrainPainter.Volcanic, TerrainPainter.Snowy), IslandShapes.Plateau, 78f, 113f, 12f, 20f, 0.45f, 0.35f),
				Content = Boss },

			new MapType { Name = "volcano", Label = "Volcano", Title = "Volcano", Description = "A tall volcano; embers, red light and dark smoke near the crater",
				Settings = rnd => Gen(rnd, TerrainPainter.Volcanic, IslandShapes.Round, 95f, 148f, 60f, 100f, 0.65f, 0.5f, 2),
				Content = (k, s) => k.Atmosphere(k.Highest(k.Mid, s.Radius * 0.45f), 50f, "#3A1E14", 0.45f, "#FF7043", 0.5f, "embers") },

			new MapType { Name = "swamp", Label = "Swamp", Title = "Swamp", Description = "Low land with pools, green mist and fireflies; rats guard a stash (quest)",
				Settings = rnd => Gen(rnd, TerrainPainter.Forest, IslandShapes.Marsh, 87f, 139f, 5f, 9f, 0.9f, 0.9f),
				Content = Swamp },

			new MapType { Name = "spire", Label = "Frozen spire", Title = "Frozen spire", Description = "A snowy island with one very tall peak, falling snow and a polar bear",
				Settings = rnd => { var s = Gen(rnd, TerrainPainter.Snowy, IslandShapes.Round, 78f, 104f, 95f, 120f, 0.5f, 0.5f, 1); s.Peaks = 1; return s; },
				Content = (k, s) =>
				{
					k.Atmosphere(k.Mid, 50f, "#DDE6F0", 0.35f, "#CFE0FF", 0.25f, "snow");
					k.Chest("Loot_Chest", k.Highest(k.Mid, s.Radius * 0.5f), "Climber's cache", MapKit.Loot("Treasure"));
					Vector2? den = k.Find(k.Mid, s.Radius, MapKit.Dry);
					if (den.HasValue) k.Creature("PolarBear", den.Value, 1, "Hard");
				} },

			new MapType { Name = "treasure", Label = "Treasure island", Title = "Treasure island", Description = "A map in a bottle on the beach leads to a buried treasure (quest)",
				Settings = rnd => Gen(rnd, TerrainPainter.Tropical, IslandShapes.Round, 78f, 122f, 20f, 40f, 0.5f, 0.55f),
				Content = Treasure },

			new MapType { Name = "camp", Label = "Old camp", Title = "Old camp", Description = "An abandoned camp with a notice board and supplies (quest); good as the start of a story",
				Settings = rnd => Gen(rnd, Pick(rnd, TerrainPainter.Tropical, TerrainPainter.Forest), IslandShapes.Round, 78f, 113f, 15f, 30f, 0.45f, 0.5f),
				Content = Camp },

			new MapType { Name = "sunken", Label = "Sunken island", Title = "Sunken island", Description = "An island under water: corals, sunken barrels and puffer fish, for divers", SunkenDepth = 12f,
				Settings = rnd => Gen(rnd, TerrainPainter.Tropical, IslandShapes.Round, 52f, 87f, 9f, 13f, 0.6f, 0f),
				Content = Sunken },

			new MapType { Name = "sky", Label = "Sky island", Title = "Sky island", Description = "A small island floating high in the air, with a cache", FlyingChance = 1f, FlyingMin = 45f, FlyingMax = 90f,
				Settings = rnd => Gen(rnd, Pick(rnd, TerrainPainter.Tropical, TerrainPainter.Forest), IslandShapes.Round, 39f, 70f, 12f, 25f, 0.5f, 0.6f, 2),
				Content = (k, s) => k.Chest("Loot_ChestSmall", k.Highest(k.Mid, s.Radius * 0.6f), "Sky cache", MapKit.Loot("Metal")) },

			new MapType { Name = "wreck", Label = "Wreck", Title = "Wreck", Description = "No land: an abandoned raft of Raft's blocks with barrels to loot",
				Settings = rnd => new IslandGenSettings { Seed = rnd.Next(1, 999999), Radius = 16f, Height = 2f },
				Build = Wreck },
		};

		static MapType Styled(string name, string label, string description, int style)
		{
			return new MapType { Name = name, Label = label, Description = description, Settings = rnd => IslandGenerator.RandomSettings(rnd, new[] { style }) };
		}

		#region Content

		static void Archipelago(MapKit k, IslandGenSettings s)
		{
			List<Vector4> islets = IslandGenerator.Islets(s);
			if (islets.Count == 0) return;
			Vector2 main = k.Highest(k.FromMid(islets[0]), islets[0].z * 0.5f);
			Vector2? noteSpot = k.Find(main, islets[0].z * 0.6f, MapKit.Dry);
			k.Note("Note_Paper", noteSpot ?? main, "Castaway's note", "I hid what I could save on the small islands around this one. Three caches in all. Whoever finds this: they're yours.");
			string[] loot = { "Basics", "Food", "Metal" };
			int caches = 0;
			foreach (Vector4 o in islets.Skip(1).Take(3))
			{
				k.Chest("Loot_ChestSmall", k.Highest(k.FromMid(o), o.z * 0.4f), "Cache", MapKit.Loot(loot[caches % loot.Length]));
				caches++;
			}
			if (caches > 0)
				k.Quest("The castaway's caches", "Someone lived here. Look for a note on the biggest islet.", "You found every cache the castaway hid.", "",
					"read|Castaway's note|1|", "open|Cache|" + caches + "|Find the " + caches + " caches on the islets");
		}

		static void Stacks(MapKit k, IslandGenSettings s)
		{
			List<Vector4> stacks = IslandGenerator.StackSpots(s);
			if (stacks.Count == 0) return;
			Vector4 tall = stacks[0];
			Vector2 top = k.Highest(k.FromMid(tall), tall.z * 0.5f);
			k.Chest("Loot_Chest", top, "Eagle's nest", MapKit.Loot("Treasure"));
			// A sign at the foot of the tallest stack
			Vector2? foot = k.Find(k.FromMid(tall), tall.z * 2f, MapKit.Beach, 3f);
			k.Note("Note_Sign", foot ?? k.FromMid(tall) + new Vector2(tall.z * 1.3f, 0f), "Warning", "Something glints on top of the tallest rock. You'll have to build your way up.");
			if (stacks.Count > 1) k.Creature("StoneBird", k.Highest(k.FromMid(stacks[1]), stacks[1].z * 0.5f), 1, "Normal", 1f, null, true, 2f);
			k.Quest("The eagle's nest", "", "Nobody has been up here for a long time.", "", "read|Warning|1|", "open|Eagle's nest|1|Reach the top of the tallest rock");
		}

		static void Boss(MapKit k, IslandGenSettings s)
		{
			Vector2 c = k.Mid;
			k.Clear(c, 24f);
			k.Zone(c, "arena", 18f, "The ground shakes... something big is coming!");
			string beast = s.Style == TerrainPainter.Snowy ? "PolarBear" : "Bear";
			k.Creature(beast, c + new Vector2(8f, 0f), 1, "Boss", 1.6f, "arena", false);
			k.Chest("Loot_ChestLarge", c - new Vector2(4f, 0f), "Spoils", MapKit.Loot("Treasure"));
			k.Quest("The beast of the plateau", "Climb the ramp to the top of the plateau.", "The beast is defeated. Its spoils are yours.", "",
				"reach|arena|1|Climb to the top of the plateau", "kill|" + (beast == "Bear" ? "Bear" : "Polar bear") + "|1|Defeat the beast", "open|Spoils|1|");
		}

		static void Swamp(MapKit k, IslandGenSettings s)
		{
			k.Atmosphere(k.Mid, 50f, "#4B5A3C", 0.55f, "#A8C890", 0.35f, "fireflies");
			Vector2? stash = k.Find(k.Mid, s.Radius * 0.6f, MapKit.Dry);
			if (!stash.HasValue) return;
			k.Chest("Loot_Crate", stash.Value, "Swamp stash", MapKit.Loot("Food"));
			for (int i = 0; i < 2; i++)
			{
				Vector2? nest = k.Find(stash.Value, 25f, MapKit.Dry, 6f);
				if (nest.HasValue) k.Creature("Rat", nest.Value, 3);
			}
			k.Quest("Rats in the reeds", "Something scurries between the pools.", "The swamp is quiet now.", "", "kill|Rat|4|Chase off the rats", "open|Swamp stash|1|");
		}

		static void Treasure(MapKit k, IslandGenSettings s)
		{
			Vector2 x = k.Highest(k.Mid, s.Radius * 0.7f);
			k.Zone(x, "x", 6f, "X marks the spot!");
			k.Chest("Loot_Chest", x, "Treasure", MapKit.Loot("Treasure"));
			Vector2? beach = k.Find(k.Mid, s.Radius * 1.3f, MapKit.Beach);
			k.Note("Note_Bottle", beach ?? k.Mid, "Treasure map", "X marks the spot. Climb to where this island is highest, and dig.");
			k.Quest("Treasure hunt", "A bottle glints on the beach.", "The treasure is yours!", "", "read|Treasure map|1|Find the map on the beach", "reach|x|1|Find the X", "open|Treasure|1|");
		}

		static void Camp(MapKit k, IslandGenSettings s)
		{
			Vector2? spot = k.Find(k.Mid, s.Radius, (above, slope) => above > 2f && above < 10f && slope < 10f, 1f);
			Vector2 c = spot ?? k.Highest(k.Mid, 10f);
			k.Clear(c, 9f);
			k.Add("Placeable_Lantern_FireBasket", k.At(c), 0f);
			k.Add("Placeable_Bed_Hammock", k.At(c + new Vector2(3.5f, 1f)), 90f);
			k.Add("Placeable_Flag_02", k.At(c + new Vector2(-3f, 3f)), 0f);
			k.Add("Placeable_CookingStand_Food_One", k.At(c + new Vector2(-2.5f, -1.5f)), 30f);
			k.Add("TP_Moontown_TarpCrate01", k.At(c + new Vector2(1f, -4f)), 15f);
			k.Note("Note_Board", c + new Vector2(-1f, 4f), "Camp notice", "We have moved on to find more land. Take what you need from the crate, and follow the birds.");
			k.Chest("Loot_Crate", c + new Vector2(3f, -3f), "Camp supplies", MapKit.Loot("Food") + ";" + MapKit.Loot("Basics").Split(';').FirstOrDefault());
			k.Quest("The empty camp", "Someone camped here not long ago.", "Where did they go?", "", "read|Camp notice|1|Read the notice board", "open|Camp supplies|1|");
		}

		static void Sunken(MapKit k, IslandGenSettings s)
		{
			string[] corals = IslandGenerator.CoralNames().Where(n => !n.StartsWith("Reef_")).ToArray();
			int count = Mathf.Clamp(Mathf.RoundToInt(s.Radius * s.Radius / 120f), 20, 120);
			for (int i = 0; i < count && corals.Length > 0; i++)
			{
				Vector2? p = k.Find(k.Mid, s.Radius * 1.2f, (above, slope) => above > -4f && slope < 35f, 3f);
				if (p.HasValue) k.Add(corals[k.Rnd.Next(corals.Length)], k.At(p.Value), (float)k.Rnd.NextDouble() * 360f, null, 0f);
			}
			for (int i = 0; i < 3; i++)
			{
				Vector2? p = k.Find(k.Mid, s.Radius, (above, slope) => above > 0f && slope < 25f, 10f);
				if (p.HasValue) k.Chest("Loot_SunkenBarrel", p.Value, "Sunken barrel", MapKit.Loot(i == 0 ? "Treasure" : "Metal"));
			}
			k.Creature("PufferFish", k.Mid, 2, "Normal", 1f, null, true, 4f);
			k.Creature("Turtle", k.Mid + new Vector2(s.Radius * 0.5f, 0f), 1, "Normal", 1f, null, true, 3f);
		}

		/// <summary>An abandoned raft of Raft's blocks on open water, with barrels (no land).</summary>
		static IslandFile Wreck(IslandGenSettings s, string name)
		{
			var f = new IslandFile
			{
				Name = name, TerrainSize = IslandGenerator.BuildArea, HeightmapResolution = IslandGenerator.BuildResolution,
				Heights = new float[IslandGenerator.BuildResolution, IslandGenerator.BuildResolution],
			};
			var k = new MapKit(f, s.Seed);
			var rnd = new System.Random(s.Seed);
			float g = PlacementOptions.GridSize, sea = k.Sea;
			Vector3 o = new Vector3(k.Mid.x, 0f, k.Mid.y);
			float floatY = sea - PlacementOptions.FloatDepth, deck = floatY + 0.35f;
			int w = 3 + rnd.Next(3), d = 2 + rnd.Next(3);
			for (int x = 0; x < w; x++)
				for (int z = 0; z < d; z++)
					if (rnd.NextDouble() > 0.12 || (x == 0 && z == 0)) // a few foundations are gone
						k.Add("Block_Foundation", o + new Vector3(x * g, floatY, z * g), 0f, null, 0f);
			k.Add("Block_Pillar_Wood", o + new Vector3(-g / 2, deck, -g / 2), 0f, null, 0f);
			k.Add("Block_Pillar_Wood", o + new Vector3(g * 1.5f, deck, -g / 2), 0f, null, 0f);
			k.Add("Block_Wall_Thatch", o + new Vector3(0, deck, -g / 2), 0f, null, 0f);
			if (rnd.NextDouble() < 0.6) k.Add("Block_Roof_Straight_Thatch", o + new Vector3(0, deck + 2.4f, 0), 0f, null, 0f);
			k.Add("Block_Ladder", o + new Vector3(g * w, deck, g), 90f, null, 0f);
			var loot = new Dictionary<string, string> { { ObjectProps.LootItems, MapKit.Loot("Basics") }, { ObjectProps.NoteTitle, "Wreckage" } };
			k.Add("Loot_Barrel", o + new Vector3(g, deck, g * (d - 1)), 0f, loot, 0f);
			k.Add("Loot_Box", o + new Vector3(g * (w - 1), deck, 0f), 20f, new Dictionary<string, string> { { ObjectProps.LootItems, MapKit.Loot("Metal") }, { ObjectProps.NoteTitle, "Wreckage" } }, 0f);
			return f;
		}

		#endregion

		public static MapType Get(string name)
		{
			return All.FirstOrDefault(t => t.Name.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Settings and elevation for a new island of this type.</summary>
		public static IslandGenSettings Roll(MapType type, System.Random rnd, out float elevation)
		{
			IslandGenSettings s = type.Settings(rnd);
			s.Clamp();
			elevation = 0f;
			if (type.SunkenDepth > 0f) elevation = -(s.Height + type.SunkenDepth + (float)rnd.NextDouble() * 4f);
			else if (type.FlyingChance > 0f && rnd.NextDouble() < type.FlyingChance)
				elevation = type.FlyingMin + (float)rnd.NextDouble() * (type.FlyingMax - type.FlyingMin);
			return s;
		}

		/// <summary>The island file of a rolled island (the object catalog must be built).</summary>
		public static IslandFile Create(MapType type, IslandGenSettings s, float elevation, string fileName)
		{
			IslandFile file = type.Build != null ? type.Build(s, fileName) : IslandGenerator.CreateFile(s, fileName);
			file.Elevation = elevation;
			if (type.Content != null)
			{
				try { type.Content(new MapKit(file, s.Seed), s); }
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Content of a '" + type.Name + "' island: " + e); }
			}
			if (type.Title.Length > 0 && !file.Props.ContainsKey(IslandProps.Title)) file.Props[IslandProps.Title] = type.Title;
			return file;
		}

		/// <summary>File name for a generated island of this type: gen-&lt;type&gt;-&lt;seed&gt;.</summary>
		public static string FileName(MapType type, IslandGenSettings s)
		{
			string kind = type.Name == "random" ? TerrainPainter.StyleName(s.Style).ToLowerInvariant() : type.Name;
			return CustomIslandSpawner.GeneratedPrefix + kind + "-" + s.Seed;
		}

		/// <summary>About how far the land of these settings reaches (for placing it before the file exists).</summary>
		public static float EstimatedRadius(IslandGenSettings s) { return Mathf.Max(20f, IslandGenerator.Reach(s)); }
	}
}
