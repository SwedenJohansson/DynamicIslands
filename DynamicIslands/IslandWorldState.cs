using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Which custom islands exist in the current world, so they come back when the world is loaded again.
	/// Stored next to the island files as Mods\DynamicIslands\worlds\&lt;world guid&gt;.txt (one "name|x|y|z" per line,
	/// plus "@auto=on|off" for automatic spawning), written whenever Raft saves the world and read when a world
	/// finishes loading. The host's list is the real one; clients hold a copy sent by the host (IslandNetwork).
	/// An entry's GameObjects exist only while the raft is near it: CustomIslandSpawner unloads and reloads them.
	/// </summary>
	public static class IslandWorldState
	{
		public class Entry
		{
			/// <summary>Given by the host for this session; clients use the host's ids.</summary>
			public int Id;
			/// <summary>Island file (without extension) this machine spawns from.</summary>
			public string Name;
			/// <summary>Client: the island's name on the host, and its file's content hash.</summary>
			public string HostName;
			public string Hash;
			/// <summary>Client: the island file is still coming from the host.</summary>
			public bool WaitingForFile;
			public Vector3 Position;
			/// <summary>The spawned island, or null while it is unloaded (far away) or still loading.</summary>
			public GameObject Root;
			public bool Loading;
			/// <summary>The island file is missing or broken: don't keep trying to load it.</summary>
			public bool Failed;
		}

		static readonly List<Entry> islands = new List<Entry>();
		static string loadedFor; // world guid the list belongs to

		public static IList<Entry> Islands { get { return islands; } }

		static string WorldKey { get { return SaveAndLoad.WorldGuid.ToString(); } }
		static string FilePath { get { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), WorldKey + ".txt"); } }

		/// <summary>Host: adds a new island to the world's list and tells clients about it.</summary>
		public static Entry Add(string name, Vector3 position, GameObject root)
		{
			EnsureCurrentWorld();
			var entry = new Entry { Id = IslandNetwork.NewId(), Name = name, HostName = name, Position = position, Root = root };
			islands.Add(entry);
			IslandNetwork.BroadcastAdded(entry);
			return entry;
		}

		/// <summary>Client: an island the host told us about.</summary>
		public static Entry AddRemote(int id, string hostName, string hash, Vector3 position)
		{
			var entry = new Entry { Id = id, Name = hostName, HostName = hostName, Hash = hash, Position = position };
			islands.Add(entry);
			return entry;
		}

		public static bool Contains(Entry entry) { return islands.Contains(entry); }

		/// <summary>Host: removes (and destroys) islands with this name, or all islands when name is null, and tells clients. Returns how many.</summary>
		public static int Remove(string name)
		{
			EnsureCurrentWorld();
			int[] ids = islands.Where(e => name == null || e.HostName.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(e => e.Id).ToArray();
			return RemoveIds(ids, true);
		}

		/// <summary>Removes (and destroys) the islands with these ids; the host also tells clients when broadcast is set.</summary>
		public static int RemoveIds(IList<int> ids, bool broadcast)
		{
			if (ids == null) return 0;
			List<Entry> gone = islands.Where(e => ids.Contains(e.Id)).ToList();
			foreach (Entry e in gone)
			{
				IslandSpawner.Despawn(e.Root);
				islands.Remove(e);
			}
			if (broadcast) IslandNetwork.BroadcastRemoved(gone.Select(e => e.Id));
			return gone.Count;
		}

		static void EnsureCurrentWorld()
		{
			if (loadedFor != WorldKey) { islands.Clear(); loadedFor = WorldKey; }
		}

		/// <summary>
		/// Raft keeps the raft near the origin: when it drifts more than a chunk away, the host shifts the whole
		/// world back (everything does position -= shift, clients get the same shift). Custom islands must follow,
		/// on every machine, and the saved positions follow too so they stay in Raft's coordinate frame.
		/// </summary>
		public static void OnWorldShift(Vector3 shift)
		{
			foreach (GameObject root in IslandSpawner.SpawnedRoots)
				if (root != null) root.transform.position -= shift;
			IslandSpawner.SpawnedRoots.RemoveAll(r => r == null);
			foreach (Entry e in islands) e.Position -= shift;
			CustomIslandSpawner.OnWorldShift(shift);
		}

		/// <summary>Writes the list for the current world (called after Raft saves the world).</summary>
		public static void Save()
		{
			if (!Raft_Network.IsHost || SaveAndLoad.WorldGuid == Guid.Empty) return;
			EnsureCurrentWorld();
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				if (islands.Count == 0 && CustomIslandSpawner.Enabled) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
				var lines = new List<string>
				{
					"# Custom islands in world '" + SaveAndLoad.CurrentGameFileName + "': name|x|y|z",
					"@auto=" + (CustomIslandSpawner.Enabled ? "on" : "off")
				};
				foreach (Entry e in islands)
					lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}", e.HostName,e.Position.x, e.Position.y, e.Position.z));
				File.WriteAllLines(FilePath, lines.ToArray());
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Could not save the world's island list: " + ex.Message); }
		}

		/// <summary>Reads the island list of the world that just finished loading; CustomIslandSpawner spawns the ones near the raft.</summary>
		public static void OnWorldLoaded()
		{
			islands.Clear();
			loadedFor = WorldKey;
			CustomIslandSpawner.Enabled = true;
			CustomIslandSpawner.OnWorldLoaded();
			IslandNetwork.OnWorldLoaded();
			if (!Raft_Network.IsHost || !File.Exists(FilePath)) return;
			foreach (string line in File.ReadAllLines(FilePath))
			{
				if (line.StartsWith("#") || line.Trim().Length == 0) continue;
				if (line.StartsWith("@auto=")) { CustomIslandSpawner.Enabled = !line.Substring(6).Trim().Equals("off", StringComparison.OrdinalIgnoreCase); continue; }
				string[] p = line.Split('|');
				float x, y, z;
				if (p.Length != 4 || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
					!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
				{
					Debug.LogWarning("[CUSTOM ISLANDS] Ignoring bad line in " + FilePath + ": " + line);
					continue;
				}
				islands.Add(new Entry { Id = IslandNetwork.NewId(), Name = p[0], HostName = p[0], Position = new Vector3(x, y, z) });
			}
			Debug.Log("[CUSTOM ISLANDS] World '" + SaveAndLoad.CurrentGameFileName + "' has " + islands.Count + " custom island(s); automatic islands " +
				(CustomIslandSpawner.Enabled ? "on" : "off"));
		}
	}

	/// <summary>Keeps the island list in step with Raft's own world saves.</summary>
	[HarmonyPatch(typeof(SaveAndLoad), "SaveWorld")]
	static class SaveWorldPatch
	{
		static void Postfix() { IslandWorldState.Save(); }
	}
}
