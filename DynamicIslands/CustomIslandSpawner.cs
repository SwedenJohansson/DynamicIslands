using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Makes saved islands appear on their own while the raft sails (host only), and streams the world's custom
	/// islands in and out by distance so long worlds don't keep every island in memory (host and clients).
	/// Settings and the island pool live in Mods\DynamicIslands\spawnpool.txt; whether it is on is stored per world.
	/// </summary>
	public static class CustomIslandSpawner
	{
		public const string PoolFileName = "spawnpool.txt";
		static string PoolPath { get { return Path.Combine(DynamicIslands.assetpath, PoolFileName); } }

		/// <summary>How often the spawner and the streaming check run, in seconds.</summary>
		const float TickInterval = 2f;
		/// <summary>An island that was unloaded is loaded again this much closer than the unload distance, so it doesn't flicker.</summary>
		const float ReloadHysteresis = 200f;
		/// <summary>Space kept between an island's land and the raft, or Raft's own islands, when placing it.</summary>
		public const float Clearance = 60f;

		// Settings (spawnpool.txt)
		public static float ChancePerKm = 0.25f;
		public static float MinSpacing = 800f;
		public static float SpawnDistanceMin = 250f;
		public static float SpawnDistanceMax = 350f;
		public static float UnloadDistance = 800f;
		/// <summary>Harvested trees and picked-up items on custom islands grow back after this many in-game days (0 = never).</summary>
		public static int RegrowDays = 3;
		/// <summary>Custom islands appear as green dots on Raft's Receiver (IslandRadar).</summary>
		public static bool ShowOnReceiver = true;
		/// <summary>Weight of brand-new, randomly generated islands in the pool (0 = never).</summary>
		public static float GeneratedWeight = 1f;
		/// <summary>Styles generated islands can have.</summary>
		public static int[] GeneratedStyles = { TerrainPainter.Tropical, TerrainPainter.Snowy, TerrainPainter.Desert, TerrainPainter.Forest, TerrainPainter.Volcanic };
		/// <summary>Chance that a generated island is a flying one.</summary>
		public static float GeneratedFlyingChance = 0.1f;

		/// <summary>Pool entry standing for "generate a new island".</summary>
		public const string GeneratedEntry = "<generated>";
		/// <summary>Files of islands generated while sailing are named gen-&lt;style&gt;-&lt;seed&gt;.</summary>
		public const string GeneratedPrefix = "gen-";
		/// <summary>Pool entries "type:&lt;map type&gt;" stand for a new island of that map type (MapTypes).</summary>
		public const string TypePrefix = "type:";
		static readonly List<KeyValuePair<string, float>> poolLines = new List<KeyValuePair<string, float>>();
		static DateTime poolFileTime;

		/// <summary>Automatic spawning in the current world (stored in the world's island list).</summary>
		public static bool Enabled = true;

		static float nextTick;
		static Vector3? lastRaftPosition;
		static float sailedSinceSpawn;
		static readonly Dictionary<string, float> radiusCache = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

		static Raft raft;
		static Raft CurrentRaft
		{
			get
			{
				if (raft == null) raft = UnityEngine.Object.FindObjectOfType<Raft>();
				return raft;
			}
		}

		public static Vector3? RaftPosition
		{
			get
			{
				Raft r = CurrentRaft;
				if (r == null) return null;
				return r.body != null ? r.body.position : r.transform.position;
			}
		}

		/// <summary>Called when a world finished loading.</summary>
		public static void OnWorldLoaded()
		{
			raft = null;
			lastRaftPosition = null;
			sailedSinceSpawn = 0f;
			nextTick = 0f; // (stream the islands in at once: a player may be standing on one - PlayerHold)
			LoadPool(true);
		}

		public static void OnWorldShift(Vector3 shift)
		{
			if (lastRaftPosition.HasValue) lastRaftPosition = lastRaftPosition.Value - shift;
		}

		/// <summary>Called every frame by the mod.</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextTick) return;
			nextTick = Time.unscaledTime + TickInterval;
			if (!LoadSceneManager.IsGameSceneLoaded) { lastRaftPosition = null; return; }
			Vector3? pos = RaftPosition;
			if (!pos.HasValue) return;

			// Every machine streams its own copy of the island list; only the host adds islands
			StreamIslands(pos.Value);
			if (!Raft_Network.IsHost) { lastRaftPosition = null; return; }
			LoadPool(false);

			float sailed = lastRaftPosition.HasValue ? Flat(pos.Value - lastRaftPosition.Value).magnitude : 0f;
			lastRaftPosition = pos.Value;
			// A teleport or a very long frame is not sailing
			if (sailed <= 0.01f || sailed > 200f) return;
			sailedSinceSpawn += sailed;
			WorldDirector.Sailed += sailed;
			if (!Enabled || ChancePerKm <= 0f) return;

			// Chance of at least one island over this stretch, for a given chance per km
			float chance = 1f - Mathf.Pow(1f - Mathf.Clamp01(ChancePerKm), sailed / 1000f);
			if (UnityEngine.Random.value < chance) TrySpawn(pos.Value, false);
		}

		#region Streaming

		/// <summary>Unloads far-away islands (keeping their entry) and loads them again when the raft comes back.</summary>
		static void StreamIslands(Vector3 raftPos)
		{
			// (by the raft, and by this player: an island someone stands on stays, however far the raft drifts, and loads
			// for a player who comes back to a world standing on it - PlayerHold)
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? playerPos = player != null ? player.transform.position : (Vector3?)null;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.ToList())
			{
				float d = Flat(e.Position - raftPos).magnitude;
				if (playerPos.HasValue) d = Mathf.Min(d, Flat(e.Position - playerPos.Value).magnitude);
				if (e.Root != null && d > UnloadDistance)
				{
					IslandObjectState.Capture(e);
					IslandSpawner.Despawn(e.Root);
					e.Root = null;
					Debug.Log("[CUSTOM ISLANDS] Unloaded island '" + e.Name + "' (" + d.ToString("F0") + " m away)");
				}
				else if (e.Root == null && !e.Loading && !e.Failed && !e.WaitingForFile && d < Mathf.Max(UnloadDistance - ReloadHysteresis, SpawnDistanceMax + 50f))
				{
					e.Loading = true;
					DynamicIslands.instance.StartCoroutine(DynamicIslands.instance.SpawnIslandFile(e.Name, e.Position, false, e));
				}
			}
		}

		#endregion

		#region Spawning

		/// <summary>
		/// Tries to place an island from the pool ahead of the raft. Returns a message saying what happened.
		/// force: ignore "raft is inside one of Raft's islands" (dev/testing).
		/// </summary>
		public static string TrySpawn(Vector3 raftPos, bool force)
		{
			if (!force && ChunkManager.RaftIsInsideChunkPoint) return Skip("the raft is at one of Raft's islands");
			string name = PickFromPool();
			if (name == null) return Skip("the spawn pool is empty");

			// A brand-new island: random settings now, the island itself is generated once a spot is found
			IslandGenSettings generate = null;
			float elevation = 0f;
			MapType mapType = null;
			if (name.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase))
			{
				// A new island of a map type (type:<name> in the pool)
				mapType = MapTypes.Get(name.Substring(TypePrefix.Length));
				if (mapType == null) return Skip("there is no map type '" + name.Substring(TypePrefix.Length) + "'");
				generate = MapTypes.Roll(mapType, new System.Random(), out elevation);
				name = MapTypes.FileName(mapType, generate);
				radiusCache[name] = MapTypes.EstimatedRadius(generate);
				elevationCache[name] = elevation;
			}
			else if (name == GeneratedEntry)
			{
				var rnd = new System.Random();
				generate = IslandGenerator.RandomSettings(rnd, GeneratedStyles);
				if (rnd.NextDouble() < GeneratedFlyingChance) elevation = 40f + (float)rnd.NextDouble() * 50f;
				name = GeneratedPrefix + TerrainPainter.StyleName(generate.Style).ToLowerInvariant() + "-" + generate.Seed;
				// (used for placing until the real file exists)
				radiusCache[name] = MapTypes.EstimatedRadius(generate);
				elevationCache[name] = elevation;
			}
			float radius = LandRadius(name);
			if (radius < 0) return Skip("could not read island '" + name + "'");

			Vector3 dir = SailDirection();
			var reasons = new List<string>();
			for (int attempt = 0; attempt < 8; attempt++)
			{
				// Off-centre so it's reachable but not always dead ahead; later attempts spread wider
				float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
				float angle = side * UnityEngine.Random.Range(10f, 35f + attempt * 10f);
				// Later attempts also look a little further out
				float distance = Mathf.Max(UnityEngine.Random.Range(SpawnDistanceMin, SpawnDistanceMax + attempt * 20f), radius + Clearance);
				Vector3 candidate = raftPos + Quaternion.Euler(0, angle, 0) * dir * distance;
				candidate.y = Elevation(name); // 0 = sea level; flying / underwater islands keep their height

				string why = Rejects(candidate, radius, raftPos);
				if (why != null) { reasons.Add(why); continue; }

				sailedSinceSpawn = 0f;
				Debug.Log("[CUSTOM ISLANDS] Auto spawn: island '" + name + "' (land radius " + radius.ToString("F0") + " m) at " + candidate +
					", " + distance.ToString("F0") + " m from the raft at " + angle.ToString("F0") + " degrees");
				// In the list straight away, so spacing checks see it while it loads (clients hear about a generated
				// island only once its file exists, so they can fetch it)
				IslandWorldState.Entry entry = IslandWorldState.Add(name, candidate, null, generate == null);
				entry.Loading = true;
				if (generate != null)
				{
					IslandGenSettings s = generate; float el = elevation; string n = name;
					MapType t = mapType;
					DynamicIslands.instance.StartCoroutine(GenerateAndSpawn(() => { if (t != null) return MapTypes.Create(t, s, el, n); IslandFile f = IslandGenerator.CreateFile(s, n); f.Elevation = el; return f; }, name, entry));
				}
				else DynamicIslands.instance.StartCoroutine(DynamicIslands.instance.SpawnIslandFile(name, candidate, true, entry));
				return "Spawning '" + name + "' " + distance.ToString("F0") + " m ahead";
			}
			return Skip("no free spot ahead of the raft for '" + name + "' (" + string.Join("; ", reasons.Distinct().Take(3).ToArray()) + ")");
		}

		/// <summary>Generates a new island file (roadmap 1.5; map types), saves it next to the others, tells clients, and spawns it.</summary>
		internal static System.Collections.IEnumerator GenerateAndSpawn(Func<IslandFile> create, string name, IslandWorldState.Entry entry)
		{
			yield return PlaceableCatalog.EnsureBuilt();
			try
			{
				IslandFile file = create();
				file.Save(IslandSpawner.PathFor(name));
				radiusCache[name] = IslandSpawner.LandRadius(file);
				elevationCache[name] = file.Elevation;
				Debug.Log("[CUSTOM ISLANDS] Generated island '" + name + "': " + (string.IsNullOrEmpty(file.Style) ? "Tropical" : file.Style) + ", land radius " + radiusCache[name].ToString("F0") + " m, " +
					file.Objects.Count + " objects" + (file.Elevation > 0f ? ", flying " + file.Elevation.ToString("F0") + " m up" : file.Elevation < 0f ? ", " + (-file.Elevation).ToString("F0") + " m under water" : ""));
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Generating island '" + name + "' failed: " + e);
				IslandWorldState.RemoveIds(new[] { entry.Id }, false);
				yield break;
			}
			if (!IslandWorldState.Contains(entry)) yield break; // removed meanwhile
			IslandNetwork.BroadcastAdded(entry);
			yield return DynamicIslands.instance.SpawnIslandFile(name, entry.Position, true, entry);
		}

		static string Skip(string why)
		{
			Debug.Log("[CUSTOM ISLANDS] Auto spawn skipped: " + why);
			return "Skipped: " + why;
		}

		/// <summary>
		/// A place around the raft where an island of this land radius fits clear of the raft, the other custom islands
		/// and Raft's own islands (nearest first, up to maxDistance), or null. Used by tests and to warn SpawnIsland.
		/// </summary>
		public static Vector3? FindClearSpot(Vector3 raftPos, float radius, float maxDistance)
		{
			for (float d = radius + Clearance + 10f; d <= Mathf.Max(maxDistance, radius + Clearance + 10f); d += 25f)
				for (int i = 0; i < 24; i++)
				{
					float a = i * 15f * Mathf.Deg2Rad;
					Vector3 c = raftPos + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * d;
					c.y = 0f;
					if (Rejects(c, radius, raftPos, false) == null) return c; // (the way there may cross an island: tests teleport)
				}
			return null;
		}

		/// <summary>Why an island of this land radius shouldn't go at candidate because one of Raft's islands is there, or null.</summary>
		public static string OverlapsRaftIsland(Vector3 candidate, float radius)
		{
			string why = Rejects(candidate, radius, candidate + Vector3.one * 100000f);
			return why != null && why.StartsWith("Raft's") ? why : null;
		}

		/// <summary>
		/// Why an island of this land radius can't go at candidate, or null if it can. minSpacing: centre-to-centre
		/// distance kept from other custom islands (-1 = the minSpacing setting; 0 = just clear of their land).
		/// </summary>
		internal static string Rejects(Vector3 candidate, float radius, Vector3 raftPos, bool checkPath = true, float minSpacing = -1f)
		{
			if (Flat(candidate - raftPos).magnitude < radius + Clearance) return "too close to the raft";
			float spacing = minSpacing < 0f ? MinSpacing : minSpacing;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				float d = Flat(candidate - e.Position).magnitude;
				if (d < Mathf.Max(spacing, radius + LandRadius(e.Name) + Clearance)) return "custom island '" + e.Name + "' " + d.ToString("F0") + " m away";
			}
			ChunkManager cm = ComponentManager<ChunkManager>.Value;
			if (cm != null)
			{
				foreach (ChunkPoint cp in cm.GetAllChunkPointsList())
				{
					// The rule's overlap radius is the footprint Raft keeps free around its island; floating rafts are
					// small drifting wrecks, so they only need a little room (Raft packs its points densely: about a
					// dozen within 1 km, so being stricter leaves hardly any open sea)
					bool raftWreck = cp.rule != null && cp.rule.name.IndexOf("FloatingRaft", StringComparison.OrdinalIgnoreCase) >= 0;
					float overlap = cp.rule != null ? cp.rule.collisionOverlapRadius : 150f;
					float d = Flat(candidate - cp.worldPosition).magnitude;
					if (d < (raftWreck ? 20f : overlap) + radius) return "Raft's " + (cp.rule != null ? cp.rule.name : "island") + " " + d.ToString("F0") + " m away";
				}
				if (checkPath && cm.DoesLineIntersectWithChunkPoints(raftPos, candidate)) return "one of Raft's islands is in the way";
			}
			return null;
		}

		internal static Vector3 SailDirection()
		{
			Raft r = CurrentRaft;
			if (r != null && r.body != null)
			{
				Vector3 v = Flat(r.body.velocity);
				if (v.sqrMagnitude > 0.04f) return v.normalized;
			}
			Vector3 d = Flat(Raft.direction);
			return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
		}

		/// <summary>How far (m) the island's land reaches from its land centre; cached. -1 if the file can't be read.</summary>
		public static float LandRadius(string name)
		{
			float r;
			if (radiusCache.TryGetValue(name, out r)) return r;
			try
			{
				IslandFile file = IslandFile.Load(IslandSpawner.PathFor(name));
				r = IslandSpawner.LandRadius(file);
				elevationCache[name] = file.Elevation;
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read island '" + name + "': " + ex.Message); r = -1; }
			radiusCache[name] = r;
			return r;
		}

		static readonly Dictionary<string, float> elevationCache = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Placing an island whose file doesn't exist yet (being generated): its size until then.</summary>
		internal static void CacheSize(string name, float radius, float elevation)
		{
			radiusCache[name] = radius;
			elevationCache[name] = elevation;
		}

		/// <summary>The island's saved elevation above sea level (flying / underwater), cached with its radius.</summary>
		internal static float Elevation(string name)
		{
			float e;
			LandRadius(name);
			return elevationCache.TryGetValue(name, out e) ? e : 0f;
		}

		#endregion

		#region Pool (spawnpool.txt)

		/// <summary>The islands taking part and their weights, with "*" expanded to every saved island not listed.</summary>
		public static List<KeyValuePair<string, float>> Pool()
		{
			var result = new List<KeyValuePair<string, float>>();
			// Copies downloaded from a multiplayer host (<name>_<hash>) and islands generated while sailing (gen-...,
			// which live on in their worlds) only join the pool when listed by name
			var saved = IslandSpawner.ListSavedIslands().Where(n => !IslandNetwork.IsDownloadName(n) && !n.StartsWith(GeneratedPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
			var listed = new HashSet<string>(poolLines.Where(p => p.Key != "*").Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
			foreach (var p in poolLines)
			{
				if (p.Key == "*")
				{
					foreach (string s in saved)
						if (!listed.Contains(s) && !result.Any(x => x.Key.Equals(s, StringComparison.OrdinalIgnoreCase))) result.Add(new KeyValuePair<string, float>(s, p.Value));
				}
				else if (IslandSpawner.ListSavedIslands().Contains(p.Key, StringComparer.OrdinalIgnoreCase))
					result.Add(p);
				else if (p.Key.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase) && MapTypes.Get(p.Key.Substring(TypePrefix.Length)) != null)
					result.Add(p);
			}
			if (GeneratedWeight > 0f) result.Add(new KeyValuePair<string, float>(GeneratedEntry, GeneratedWeight));
			return result.Where(p => p.Value > 0f).ToList();
		}

		/// <summary>Dev tests: the next pick, instead of a random one.</summary>
		internal static string ForceNextPick;

		internal static string PickFromPool()
		{
			if (ForceNextPick != null) { string forced = ForceNextPick; ForceNextPick = null; return forced; }
			var pool = Pool();
			float total = pool.Sum(p => p.Value);
			if (total <= 0f) return null;
			float roll = UnityEngine.Random.value * total;
			foreach (var p in pool)
			{
				roll -= p.Value;
				if (roll <= 0f) return p.Key;
			}
			return pool[pool.Count - 1].Key;
		}

		const string DefaultPool =
@"# Custom Islands: islands that appear on their own while sailing (host only).
# Changes are picked up while the game runs.

# Chance that an island appears for each km the raft sails (0 to 1)
chancePerKm = 0.25
# Metres kept between custom islands (centre to centre)
minSpacing = 800
# How far ahead of the raft an island appears (m). Raft's camera renders to about 400 m.
spawnDistanceMin = 250
spawnDistanceMax = 350
# Islands further than this from the raft are unloaded (and come back when the raft returns)
unloadDistance = 800
# Harvested trees and picked-up items grow back after this many in-game days (0 = never). Checked when an island loads.
regrowDays = 3
# Show custom islands as green dots on Raft's Receiver (1 = yes, 0 = no)
showOnReceiver = 1
# The world plan new worlds get when none is chosen in the New Game box (plans are in Mods\DynamicIslands\plans)
defaultPlan = Random islands

# Brand-new random islands join the pool with this weight (0 = never). Each is saved as gen-<style>-<seed>.island.
generated = 1
# Styles they can have (Tropical, Snowy, Desert, Forest, Volcanic), and the chance that one is a flying island
generatedStyles = Tropical, Snowy, Desert, Forest, Volcanic
generatedFlyingChance = 0.1

# Islands taking part, one per line: <island name> <weight>
# A higher weight makes an island more likely. Weight 0 leaves it out.
# ""*"" stands for every saved island not listed by name.
* 1

# Map types join with type:<name> <weight>: a new island of that kind each time (sandbar, atoll, archipelago,
# stacks, boss, volcano, swamp, spire, treasure, camp, sunken, sky, wreck, or tropical, snowy, desert, forest, volcanic)
type:sandbar 0.4
type:wreck 0.4
type:atoll 0.3
type:sunken 0.2
";

		/// <summary>Reads spawnpool.txt (creating it with defaults if missing) when it changed, or always when force is set.</summary>
		public static void LoadPool(bool force)
		{
			try
			{
				if (!File.Exists(PoolPath))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(PoolPath));
					File.WriteAllText(PoolPath, DefaultPool);
				}
				DateTime t = File.GetLastWriteTimeUtc(PoolPath);
				if (!force && t == poolFileTime) return;
				poolFileTime = t;
				radiusCache.Clear(); // islands may have been re-saved too
				elevationCache.Clear();

				poolLines.Clear();
				foreach (string raw in File.ReadAllLines(PoolPath))
				{
					string line = raw.Trim();
					if (line.Length == 0 || line.StartsWith("#")) continue;
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						string key = line.Substring(0, eq).Trim().ToLowerInvariant(), value = line.Substring(eq + 1).Trim();
						if (key == "generatedstyles")
						{
							// A list of style names, e.g. "Tropical, Snowy"
							GeneratedStyles = value.Split(',', ' ').Where(x => x.Trim().Length > 0)
								.Select(x => Array.FindIndex(TerrainPainter.Styles, st => st.Name.Equals(x.Trim(), StringComparison.OrdinalIgnoreCase)))
								.Where(i => i >= 0).Distinct().ToArray();
							if (GeneratedStyles.Length == 0) { BadLine(line); GeneratedStyles = new[] { TerrainPainter.Tropical }; }
							continue;
						}
						if (key == "defaultplan") { WorldDirector.DefaultPlan = value.Length > 0 ? value : WorldPlan.RandomName; continue; }
						float v;
						if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { BadLine(line); continue; }
						switch (line.Substring(0, eq).Trim().ToLowerInvariant())
						{
							case "chanceperkm": ChancePerKm = Mathf.Clamp01(v); break;
							case "minspacing": MinSpacing = Mathf.Max(0f, v); break;
							case "spawndistancemin": SpawnDistanceMin = Mathf.Max(20f, v); break;
							case "spawndistancemax": SpawnDistanceMax = Mathf.Max(20f, v); break;
							case "unloaddistance": UnloadDistance = Mathf.Max(300f, v); break;
							case "regrowdays": RegrowDays = Mathf.Max(0, Mathf.RoundToInt(v)); break;
							case "showonreceiver": ShowOnReceiver = v != 0f; break;
							case "generated": GeneratedWeight = Mathf.Max(0f, v); break;
							case "generatedflyingchance": GeneratedFlyingChance = Mathf.Clamp01(v); break;
							default: BadLine(line); break;
						}
						continue;
					}
					// "<name> <weight>" (names may contain spaces), or just "<name>" for weight 1
					int sp = line.LastIndexOf(' ');
					float w;
					if (sp > 0 && float.TryParse(line.Substring(sp + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out w))
						poolLines.Add(new KeyValuePair<string, float>(line.Substring(0, sp).Trim(), w));
					else
						poolLines.Add(new KeyValuePair<string, float>(line, 1f));
				}
				if (SpawnDistanceMax < SpawnDistanceMin) SpawnDistanceMax = SpawnDistanceMin;
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + PoolPath + ": " + ex.Message); }
		}

		static void BadLine(string line) { Debug.LogWarning("[CUSTOM ISLANDS] Ignoring unknown line in " + PoolFileName + ": " + line); }

		public static string Describe()
		{
			LoadPool(true);
			var pool = Pool();
			float total = pool.Sum(p => p.Value);
			var lines = new List<string>
			{
				"Automatic islands in this world: " + (Enabled ? "on" : "off") + " (CustomIslandsAuto on|off)",
				string.Format(CultureInfo.InvariantCulture, "Chance per km sailed: {0:P0} (about one island every {1:F1} km), min spacing {2:F0} m, appear {3:F0}-{4:F0} m ahead, unload beyond {5:F0} m, regrow after {6} day(s)",
					ChancePerKm, ChancePerKm > 0 ? 1f / ChancePerKm : float.PositiveInfinity, MinSpacing, SpawnDistanceMin, SpawnDistanceMax, UnloadDistance, RegrowDays > 0 ? RegrowDays.ToString() : "never"),
				"Sailed since the last automatic island: " + sailedSinceSpawn.ToString("F0") + " m",
				"Pool (" + PoolPath + "): " + (pool.Count == 0 ? "empty" : "")
			};
			foreach (var p in pool)
				lines.Add(string.Format(CultureInfo.InvariantCulture, "  {0}: weight {1}, {2:P0} of spawns", p.Key == GeneratedEntry ? "a new generated island" : p.Key, p.Value, p.Value / total));
			if (GeneratedWeight > 0f)
				lines.Add("Generated islands: " + string.Join(", ", GeneratedStyles.Select(TerrainPainter.StyleName).ToArray()) + ", " +
					GeneratedFlyingChance.ToString("P0", CultureInfo.InvariantCulture) + " of them flying");
			lines.Add("Custom islands on the Receiver: " + (ShowOnReceiver ? "shown" : "hidden"));
			return string.Join("\n", lines.ToArray());
		}

		#endregion

		static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0, v.z); }
	}
}
