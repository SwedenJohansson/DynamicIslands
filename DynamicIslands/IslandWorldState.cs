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
	/// Stored next to the island files as Mods\DynamicIslands\worlds\&lt;world guid&gt;.txt (one "name|x|y|z" per line),
	/// written whenever Raft saves the world and read when a world finishes loading. Host only: clients get the
	/// islands from the host.
	/// </summary>
	public static class IslandWorldState
	{
		public class Entry
		{
			public string Name;
			public Vector3 Position;
			public GameObject Root;
		}

		static readonly List<Entry> islands = new List<Entry>();
		static string loadedFor; // world guid the list belongs to

		public static IList<Entry> Islands { get { return islands; } }

		static string WorldKey { get { return SaveAndLoad.WorldGuid.ToString(); } }
		static string FilePath { get { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), WorldKey + ".txt"); } }

		public static void Add(string name, Vector3 position, GameObject root)
		{
			EnsureCurrentWorld();
			islands.Add(new Entry { Name = name, Position = position, Root = root });
		}

		/// <summary>Removes (and destroys) islands with this name, or all islands when name is null. Returns how many.</summary>
		public static int Remove(string name)
		{
			EnsureCurrentWorld();
			List<Entry> gone = islands.Where(e => name == null || e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
			foreach (Entry e in gone)
			{
				if (e.Root != null) UnityEngine.Object.Destroy(e.Root);
				islands.Remove(e);
			}
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
		}

		/// <summary>Writes the list for the current world (called after Raft saves the world).</summary>
		public static void Save()
		{
			if (!Raft_Network.IsHost || SaveAndLoad.WorldGuid == Guid.Empty) return;
			EnsureCurrentWorld();
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				if (islands.Count == 0) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
				var lines = new List<string> { "# Custom islands in world '" + SaveAndLoad.CurrentGameFileName + "': name|x|y|z" };
				foreach (Entry e in islands)
					lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}", e.Name, e.Position.x, e.Position.y, e.Position.z));
				File.WriteAllLines(FilePath, lines.ToArray());
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Could not save the world's island list: " + ex.Message); }
		}

		/// <summary>Spawns the islands saved for the world that just finished loading.</summary>
		public static void OnWorldLoaded()
		{
			islands.Clear();
			loadedFor = WorldKey;
			if (!Raft_Network.IsHost || !File.Exists(FilePath)) return;
			int n = 0;
			foreach (string line in File.ReadAllLines(FilePath))
			{
				if (line.StartsWith("#") || line.Trim().Length == 0) continue;
				string[] p = line.Split('|');
				float x, y, z;
				if (p.Length != 4 || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
					!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
				{
					Debug.LogWarning("[CUSTOM ISLANDS] Ignoring bad line in " + FilePath + ": " + line);
					continue;
				}
				DynamicIslands.instance.StartCoroutine(DynamicIslands.instance.SpawnIslandFile(p[0], new Vector3(x, y, z), false, true));
				n++;
			}
			Debug.Log("[CUSTOM ISLANDS] Restoring " + n + " custom island(s) for world '" + SaveAndLoad.CurrentGameFileName + "'");
		}
	}

	/// <summary>Keeps the island list in step with Raft's own world saves.</summary>
	[HarmonyPatch(typeof(SaveAndLoad), "SaveWorld")]
	static class SaveWorldPatch
	{
		static void Postfix() { IslandWorldState.Save(); }
	}
}
