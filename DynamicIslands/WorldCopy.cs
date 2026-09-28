using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Lets any player host a world later ("host swap"). Raft keeps a world only on the PC of the player who hosts it;
	/// to host it another time, the group copies its folder (Raft's World\&lt;name&gt;) to the next host's PC. The mod's own
	/// state of the world (Mods\DynamicIslands\worlds\&lt;world id&gt;.txt: custom islands, what was used, quests, the
	/// journal, levels, world rules, randomizer, options...) lives beside it on the host's PC, so it would stay behind.
	/// Two copies make it travel:
	///  - the host writes the world file into Raft's world folder too (CustomIslands.txt), so it moves with the folder;
	///  - the host sends it to every player each time Raft saves (and to a player who joins), and they keep it as their
	///    own worlds\&lt;world id&gt;.txt, so the world can be hosted by any of them with its latest state.
	/// When a world loads, the newest of the copies (by the "@savedat" stamp in it) is read. Island files come along with
	/// joining already (a player keeps them as &lt;name&gt;_&lt;hash&gt;.island); the list says each island's hash, so a new host
	/// finds its copy and the island keeps its name (rules, quests, journal pages refer to it).
	/// </summary>
	public static class WorldCopy
	{
		public const string FileName = "CustomIslands.txt";
		const int ChunkChars = 3000;

		/// <summary>The copy's stamp line: "@savedat=&lt;UTC ticks&gt;".</summary>
		public static string StampLine() { return "@savedat=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture); }

		public static bool ReadLine(string key, string value) { return key.Equals("savedat", StringComparison.OrdinalIgnoreCase); }

		static long StampOf(string[] lines)
		{
			foreach (string l in lines)
				if (l.StartsWith("@savedat=", StringComparison.OrdinalIgnoreCase))
				{
					long t;
					if (long.TryParse(l.Substring(9).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out t)) return t;
				}
			return 0L;
		}

		/// <summary>Raft's folder of the world being played (World\&lt;name&gt;), or null.</summary>
		public static string RaftWorldFolder
		{
			get
			{
				try
				{
					if (string.IsNullOrEmpty(SaveAndLoad.WorldPath) || string.IsNullOrEmpty(SaveAndLoad.CurrentGameFileName)) return null;
					string dir = Path.Combine(SaveAndLoad.WorldPath, SaveAndLoad.CurrentGameFileName);
					return Directory.Exists(dir) ? dir : null;
				}
				catch { return null; }
			}
		}

		/// <summary>The last copy's source when a world was read (tests): "mod folder", "Raft's world folder", or "none".</summary>
		public static string LastSource { get; private set; }

		/// <summary>
		/// Host, loading a world: the newest of the mod's own file and the copy in Raft's world folder (null if neither).
		/// </summary>
		public static string[] Choose(string modFile)
		{
			string[] mine = null, travelled = null;
			try { if (File.Exists(modFile)) mine = File.ReadAllLines(modFile); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + modFile + ": " + e.Message); }
			string folder = RaftWorldFolder;
			string copy = folder != null ? Path.Combine(folder, FileName) : null;
			try { if (copy != null && File.Exists(copy)) travelled = File.ReadAllLines(copy); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + copy + ": " + e.Message); }
			if (mine == null && travelled == null) { LastSource = "none"; return null; }
			if (travelled != null && (mine == null || StampOf(travelled) > StampOf(mine)))
			{
				LastSource = "Raft's world folder";
				Debug.Log("[CUSTOM ISLANDS] The world's custom islands come from the copy in Raft's world folder" + (mine != null ? " (newer than the mod's own)" : "") + ": " + copy);
				return travelled;
			}
			LastSource = "mod folder";
			return mine;
		}

		/// <summary>Host, after writing the world file: the copy in Raft's world folder, and the copy for every player.</summary>
		public static void AfterSave(string[] lines)
		{
			string folder = RaftWorldFolder;
			if (folder != null)
				try { File.WriteAllLines(Path.Combine(folder, FileName), lines); }
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write the world's copy into Raft's world folder: " + e.Message); }
			last = lines;
			lastKey = SaveAndLoad.WorldGuid.ToString();
			Send(null);
		}

		/// <summary>Host: the world file forgotten (a world without anything of the mod): the copies go too.</summary>
		public static void AfterDelete()
		{
			string folder = RaftWorldFolder;
			if (folder != null)
				try { string f = Path.Combine(folder, FileName); if (File.Exists(f)) File.Delete(f); } catch { }
			last = new[] { "# (nothing of Custom Islands in this world)", StampLine() };
			lastKey = SaveAndLoad.WorldGuid.ToString();
			Send(null);
		}

		static string[] last;
		static string lastKey;

		/// <summary>Host: sends the world file (as last saved; read from disk if not saved this session) to one player or everyone.</summary>
		public static void Send(Network_UserId? to)
		{
			if (!Raft_Network.IsHost) return;
			string key = SaveAndLoad.WorldGuid.ToString();
			if (last == null || lastKey != key)
			{
				string file = IslandWorldState.WorldFilePath;
				last = File.Exists(file) ? File.ReadAllLines(file) : null;
				lastKey = key;
			}
			if (last == null || SaveAndLoad.WorldGuid == Guid.Empty) return;
			string text = string.Join("\n", last);
			string id = StampOf(last).ToString(CultureInfo.InvariantCulture);
			int count = Mathf.Max(1, (text.Length + ChunkChars - 1) / ChunkChars);
			for (int i = 0; i < count; i++)
			{
				var msg = new IslandNetMessage { Name = key, Hash = id, Index = i, Count = count, Data = text.Substring(i * ChunkChars, Mathf.Min(ChunkChars, text.Length - i * ChunkChars)) };
				IslandNetwork.SendWorldCopy(msg, to);
			}
			Debug.Log("[CUSTOM ISLANDS] [net] Sent the world's copy (" + text.Length + " characters, " + count + " part(s)) to " + (to.HasValue ? to.Value.ToString() : "every player"));
		}

		static readonly Dictionary<string, string[]> incoming = new Dictionary<string, string[]>();

		/// <summary>The last copy a player kept (tests): "&lt;world id&gt; &lt;lines&gt; lines".</summary>
		public static string LastKept { get; private set; }

		/// <summary>A player: a part of the host's world file; when all have come, it is kept as worlds\&lt;world id&gt;.txt.</summary>
		public static void OnMessage(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost || string.IsNullOrEmpty(msg.Name) || msg.Count <= 0 || msg.Index < 0 || msg.Index >= msg.Count) return;
			Guid guid;
			if (!Guid.TryParse(msg.Name, out guid) || guid == Guid.Empty) return;
			string key = msg.Name + "|" + msg.Hash;
			string[] parts;
			if (!incoming.TryGetValue(key, out parts) || parts.Length != msg.Count) incoming[key] = parts = new string[msg.Count];
			parts[msg.Index] = msg.Data ?? "";
			if (parts.Any(p => p == null)) return;
			incoming.Remove(key);
			string[] lines = string.Concat(parts).Split('\n');
			string path = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), guid + ".txt");
			try
			{
				// (never over a newer copy: a late message from an older save)
				if (File.Exists(path) && StampOf(File.ReadAllLines(path)) > StampOf(lines)) return;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllLines(path, lines);
				LastKept = guid + " " + lines.Length + " lines";
				Debug.Log("[CUSTOM ISLANDS] Kept the host's copy of this world (" + lines.Length + " lines) to host it later: " + path);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not keep the world's copy: " + e.Message); }
		}

		/// <summary>
		/// Host, reading an island line of the world file: the local file for an island saved by another host. Its own file
		/// when that has the recorded hash (or no hash was recorded), else a copy downloaded while joining (name_hash).
		/// </summary>
		public static string LocalFileFor(string name, string hash)
		{
			if (string.IsNullOrEmpty(hash)) return name;
			string own = IslandNetwork.HashOf(name);
			if (own == hash) return name;
			string downloaded = IslandNetwork.DownloadName(name, hash);
			if (File.Exists(IslandSpawner.PathFor(downloaded))) { Debug.Log("[CUSTOM ISLANDS] '" + name + "' is played from the copy downloaded from its first host: " + downloaded); return downloaded; }
			if (own != null) Debug.LogWarning("[CUSTOM ISLANDS] '" + name + "' differs from the one this world was saved with (" + own + ", not " + hash + "): playing this PC's own");
			return name;
		}
	}
}
