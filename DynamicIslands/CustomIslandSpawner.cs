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
		const float Clearance = 60f;

		// Settings (spawnpool.txt)
		public static float ChancePerKm = 0.25f;
		public static float MinSpacing = 800f;
		public static float SpawnDistanceMin = 250f;
		public static float SpawnDistanceMax = 350f;
		public static float UnloadDistance = 800f;
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
			if (!Enabled || ChancePerKm <= 0f) return;

			// Chance of at least one island over this stretch, for a given chance per km
			float chance = 1f - Mathf.Pow(1f - Mathf.Clamp01(ChancePerKm), sailed / 1000f);
			if (UnityEngine.Random.value < chance) TrySpawn(pos.Value, false);
		}

		#region Streaming

		/// <summary>Unloads far-away islands (keeping their entry) and loads them again when the raft comes back.</summary>
		static void StreamIslands(Vector3 raftPos)
		{
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.ToList())
			{
				float d = Flat(e.Position - raftPos).magnitude;
				if (e.Root != null && d > UnloadDistance)
				{
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
			float radius = LandRadius(name);
			if (radius < 0) return Skip("could not read island '" + name + "'");

			Vector3 dir = SailDirection();
			var reasons = new List<string>();
			for (int attempt = 0; attempt < 8; attempt++)
			{
				// Off-centre so it's reachable but not always dead ahead; later attempts spread wider
				float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
				float angle = side * UnityEngine.Random.Range(10f, 35f + attempt * 10f);
				float distance = Mathf.Max(UnityEngine.Random.Range(SpawnDistanceMin, SpawnDistanceMax), radius + Clearance);
				Vector3 candidate = raftPos + Quaternion.Euler(0, angle, 0) * dir * distance;
				candidate.y = 0; // sea level

				string why = Rejects(candidate, radius, raftPos);
				if (why != null) { reasons.Add(why); continue; }

				sailedSinceSpawn = 0f;
				Debug.Log("[CUSTOM ISLANDS] Auto spawn: island '" + name + "' (land radius " + radius.ToString("F0") + " m) at " + candidate +
					", " + distance.ToString("F0") + " m from the raft at " + angle.ToString("F0") + " degrees");
				// In the list straight away, so spacing checks see it while it loads
				IslandWorldState.Entry entry = IslandWorldState.Add(name, candidate, null);
				entry.Loading = true;
				DynamicIslands.instance.StartCoroutine(DynamicIslands.instance.SpawnIslandFile(name, candidate, true, entry));
				return "Spawning '" + name + "' " + distance.ToString("F0") + " m ahead";
			}
			return Skip("no free spot ahead of the raft for '" + name + "' (" + string.Join("; ", reasons.Distinct().Take(3).ToArray()) + ")");
		}

		static string Skip(string why)
		{
			Debug.Log("[CUSTOM ISLANDS] Auto spawn skipped: " + why);
			return "Skipped: " + why;
		}

		/// <summary>Why an island of this land radius can't go at candidate, or null if it can.</summary>
		static string Rejects(Vector3 candidate, float radius, Vector3 raftPos)
		{
			if (Flat(candidate - raftPos).magnitude < radius + Clearance) return "too close to the raft";
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				float d = Flat(candidate - e.Position).magnitude;
				if (d < Mathf.Max(MinSpacing, radius + LandRadius(e.Name) + Clearance)) return "custom island '" + e.Name + "' " + d.ToString("F0") + " m away";
			}
			ChunkManager cm = ComponentManager<ChunkManager>.Value;
			if (cm != null)
			{
				foreach (ChunkPoint cp in cm.GetAllChunkPointsList())
				{
					float overlap = cp.rule != null ? cp.rule.collisionOverlapRadius : 150f;
					float d = Flat(candidate - cp.worldPosition).magnitude;
					if (d < overlap + radius + Clearance) return "Raft's " + (cp.rule != null ? cp.rule.name : "island") + " " + d.ToString("F0") + " m away";
				}
				if (cm.DoesLineIntersectWithChunkPoints(raftPos, candidate)) return "one of Raft's islands is in the way";
			}
			return null;
		}

		static Vector3 SailDirection()
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
			try { r = IslandSpawner.LandRadius(IslandFile.Load(IslandSpawner.PathFor(name))); }
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read island '" + name + "': " + ex.Message); r = -1; }
			radiusCache[name] = r;
			return r;
		}

		#endregion

		#region Pool (spawnpool.txt)

		/// <summary>The islands taking part and their weights, with "*" expanded to every saved island not listed.</summary>
		public static List<KeyValuePair<string, float>> Pool()
		{
			var result = new List<KeyValuePair<string, float>>();
			var saved = IslandSpawner.ListSavedIslands().ToList();
			var listed = new HashSet<string>(poolLines.Where(p => p.Key != "*").Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
			foreach (var p in poolLines)
			{
				if (p.Key == "*")
				{
					foreach (string s in saved)
						if (!listed.Contains(s) && !result.Any(x => x.Key.Equals(s, StringComparison.OrdinalIgnoreCase))) result.Add(new KeyValuePair<string, float>(s, p.Value));
				}
				else if (saved.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
					result.Add(p);
			}
			return result.Where(p => p.Value > 0f).ToList();
		}

		static string PickFromPool()
		{
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

# Islands taking part, one per line: <island name> <weight>
# A higher weight makes an island more likely. Weight 0 leaves it out.
# ""*"" stands for every saved island not listed by name.
* 1
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

				poolLines.Clear();
				foreach (string raw in File.ReadAllLines(PoolPath))
				{
					string line = raw.Trim();
					if (line.Length == 0 || line.StartsWith("#")) continue;
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						float v;
						if (!float.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { BadLine(line); continue; }
						switch (line.Substring(0, eq).Trim().ToLowerInvariant())
						{
							case "chanceperkm": ChancePerKm = Mathf.Clamp01(v); break;
							case "minspacing": MinSpacing = Mathf.Max(0f, v); break;
							case "spawndistancemin": SpawnDistanceMin = Mathf.Max(20f, v); break;
							case "spawndistancemax": SpawnDistanceMax = Mathf.Max(20f, v); break;
							case "unloaddistance": UnloadDistance = Mathf.Max(300f, v); break;
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
				string.Format(CultureInfo.InvariantCulture, "Chance per km sailed: {0:P0} (about one island every {1:F1} km), min spacing {2:F0} m, appear {3:F0}-{4:F0} m ahead, unload beyond {5:F0} m",
					ChancePerKm, ChancePerKm > 0 ? 1f / ChancePerKm : float.PositiveInfinity, MinSpacing, SpawnDistanceMin, SpawnDistanceMax, UnloadDistance),
				"Sailed since the last automatic island: " + sailedSinceSpawn.ToString("F0") + " m",
				"Pool (" + PoolPath + "): " + (pool.Count == 0 ? "empty" : "")
			};
			foreach (var p in pool)
				lines.Add(string.Format(CultureInfo.InvariantCulture, "  {0}: weight {1}, {2:P0} of spawns", p.Key, p.Value, p.Value / total));
			return string.Join("\n", lines.ToArray());
		}

		#endregion

		static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0, v.z); }
	}
}
