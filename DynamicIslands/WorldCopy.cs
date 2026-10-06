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
	/// When a world loads, the newest of the copies (by the "@savecount" counter in it) is read. Island files come along with
	/// joining already (a player keeps them as &lt;name&gt;_&lt;hash&gt;.island); the list says each island's hash, so a new host
	/// finds its copy and the island keeps its name (rules, quests, journal pages refer to it).
	///
	/// Older saves: Raft keeps the last 8 saves of a world (World\&lt;name&gt;\&lt;date&gt;, the newest "-Latest"), and its
	/// Load Game box can load any of them. So that the mod's state goes back with Raft's (a chest looted after that save
	/// is full again, a quest is where it was), the copy is also written into the save's own folder with Raft's stamp of
	/// that save ("@raftsave=", RGD_Game.lastPlayedDateTicks); loading a save reads the copy with the same stamp.
	///
	/// Writes between Raft's saves (a story step, a Receiver unlock, a setting changed) say so ("@between=" the stamp of
	/// Raft's save they follow, instead of "@raftsave="). After a crash or Alt+F4 such a copy is newer than Raft's save
	/// (the players' inventories): loading that save then takes the islands' state - chests, quests, story - from the
	/// copy written with it, and only the settings (SettingKeys) from the newer copy, so no loot or reward is lost - AU3.
	/// "Newest" goes by the world's save counter ("@savecount=", one more on every write, carried in every copy and sent
	/// with it), the PC's clock ("@savedat=") only breaks a tie: two PCs' clocks needn't agree - AU4.
	/// </summary>
	public static class WorldCopy
	{
		public const string FileName = "CustomIslands.txt";
		const int ChunkChars = 3000;

		/// <summary>The copy's stamp line: "@savedat=&lt;UTC ticks&gt;".</summary>
		public static string StampLine() { return "@savedat=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture); }

		/// <summary>Raft's stamp of the save being written (CreateRGDGame) and of the save being loaded (RestoreRGDGame); 0 = none.</summary>
		public static long SavingStamp, LoadingStamp;
		/// <summary>True while the world file is written because Raft saves the world (not for a setting changed in between).</summary>
		public static bool InRaftSave;

		/// <summary>The line naming Raft's save this state belongs to (null outside Raft's own save).</summary>
		public static string RaftSaveLine() { return InRaftSave && SavingStamp != 0 ? "@raftsave=" + SavingStamp.ToString(CultureInfo.InvariantCulture) : null; }

		/// <summary>The world's save counter: the highest of its copies when it loaded, one more on every write (AU4).</summary>
		public static long SaveCount;
		/// <summary>Raft's save the world's state follows: the one loaded, then each one Raft writes (0 = not known).</summary>
		static long followsSave;

		/// <summary>The stamp lines of a write of the world file: the clock, the counter (one more), and Raft's save it is
		/// written with ("@raftsave=") or, between Raft's saves, the one it follows ("@between=" - AU3).</summary>
		public static string[] StampLines()
		{
			SaveCount++;
			string raft = RaftSaveLine();
			if (raft != null) followsSave = SavingStamp;
			return new[] { StampLine(), "@savecount=" + SaveCount.ToString(CultureInfo.InvariantCulture), raft ?? "@between=" + followsSave.ToString(CultureInfo.InvariantCulture) };
		}

		public static bool ReadLine(string key, string value)
		{
			// (which PC saved the world: the plan's owner for a world that names none - AU25)
			if (key.Equals("savedby", StringComparison.OrdinalIgnoreCase)) { WorldDirector.ReadSavedBy(value); return true; }
			return key.Equals("savedat", StringComparison.OrdinalIgnoreCase) || key.Equals("raftsave", StringComparison.OrdinalIgnoreCase) ||
				key.Equals("savecount", StringComparison.OrdinalIgnoreCase) || key.Equals("between", StringComparison.OrdinalIgnoreCase);
		}

		static long StampOf(string[] lines) { return LongLine(lines, "@savedat="); }
		static long RaftSaveOf(string[] lines) { return LongLine(lines, "@raftsave="); }
		static long CountOf(string[] lines) { return LongLine(lines, "@savecount="); }

		/// <summary>Copy a is newer than b: by the save counter, the clock only when the counters are equal (AU4).</summary>
		static bool Newer(string[] a, string[] b)
		{
			long ca = CountOf(a), cb = CountOf(b);
			return ca != cb ? ca > cb : StampOf(a) > StampOf(b);
		}

		/// <summary>Written between Raft's saves: no "@raftsave=" line (older versions wrote none there either - AU3).</summary>
		static bool IsBetween(string[] lines) { return RaftSaveOf(lines) == 0; }

		/// <summary>The world's settings (the rules, options, levels on/off, randomizer, islands left out...): a crash doesn't
		/// take them back to Raft's save - they aren't loot or progress (AU3).</summary>
		static readonly string[] SettingKeys = { "auto", "monsters", "buildcost", "regrow", "options", "optionsused", "levels", "randomizer", "islandsoff", "raftgap" };

		static bool IsSetting(string line)
		{
			int eq = line.IndexOf('=');
			return line.StartsWith("@") && eq > 1 && SettingKeys.Contains(line.Substring(1, eq - 1).Trim().ToLowerInvariant());
		}

		/// <summary>The state of one copy with the settings of another (AU3).</summary>
		static string[] Merge(string[] state, string[] settings)
		{
			return state.TakeWhile(l => l.StartsWith("#")).Concat(settings.Where(IsSetting)).Concat(state.SkipWhile(l => l.StartsWith("#")).Where(l => !IsSetting(l))).ToArray();
		}

		static bool futureTold;

		/// <summary>A copy stamped more than a day ahead of this PC's clock (a PC's clock set wrong): logged, and the host is
		/// told once - the save counter decides which copy is newest, but the clock still breaks ties (AU4).</summary>
		static void WarnFuture(string[] lines, string where)
		{
			if (lines == null) return;
			bool ahead = StampOf(lines) > DateTime.UtcNow.Ticks + TimeSpan.TicksPerDay ||
				Math.Max(RaftSaveOf(lines), LongLine(lines, "@between=")) > DateTime.Now.Ticks + TimeSpan.TicksPerDay;
			if (!ahead) return;
			Debug.LogWarning("[CUSTOM ISLANDS] The world's copy " + where + " is stamped more than a day in the future: a PC's clock is set wrong?");
			if (Raft_Network.IsHost && !futureTold)
			{
				futureTold = true;
				DynamicIslands.Notify("A copy of this world's custom islands is dated more than a day in the future: is a PC's clock set wrong? (see the log, F10)", true);
			}
		}

		static long LongLine(string[] lines, string prefix)
		{
			foreach (string l in lines)
				if (l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					long t;
					if (long.TryParse(l.Substring(prefix.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out t)) return t;
				}
			return 0L;
		}

		/// <summary>The folder of Raft's newest save of the world being played (World\&lt;name&gt;\&lt;date&gt;-Latest), or null.</summary>
		static string LatestSaveFolder(string worldFolder)
		{
			try { return Directory.GetDirectories(worldFolder).FirstOrDefault(d => d.EndsWith("-Latest", StringComparison.OrdinalIgnoreCase)); }
			catch { return null; }
		}

		/// <summary>The last load read an older save's copy (tests): "" or the save folder's name.</summary>
		public static string LastOlderSave { get; private set; }

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

		/// <summary>A file's lines (null when it isn't there); tried a few times while it is in use, then the error is thrown.</summary>
		static string[] ReadThere(string file)
		{
			for (int i = 0; ; i++)
			{
				try { return File.Exists(file) ? File.ReadAllLines(file) : null; }
				catch (IOException e)
				{
					if (i >= 4) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + file + ": " + e.Message); throw; }
					System.Threading.Thread.Sleep(100);
				}
			}
		}

		/// <summary>
		/// Host, loading a world: the newest of the mod's own file and the copy in Raft's world folder (null if neither).
		/// </summary>
		public static string[] Choose(string modFile)
		{
			// (a file that is there but can't be read - locked by an antivirus or a sync - throws: the world then loads as
			// "couldn't be read" and isn't saved, instead of as "no file", which the next save deleted - review 2026-10-06)
			string[] mine = ReadThere(modFile);
			string folder = RaftWorldFolder;
			string copy = folder != null ? Path.Combine(folder, FileName) : null;
			string[] travelled = copy != null ? ReadThere(copy) : null;
			LastOlderSave = "";
			long loading = LoadingStamp;
			LoadingStamp = 0; // (used once: a new world made afterwards has no save of its own yet)
			followsSave = loading;
			var saves = new List<KeyValuePair<string, string[]>>();
			if (loading != 0 && folder != null)
				try
				{
					foreach (string dir in Directory.GetDirectories(folder))
					{
						string f = Path.Combine(dir, FileName);
						if (File.Exists(f)) saves.Add(new KeyValuePair<string, string[]>(dir, File.ReadAllLines(f)));
					}
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Looking through the world's saves: " + e.Message); }
			List<string[]> all = new[] { mine, travelled }.Concat(saves.Select(s => s.Value)).Where(l => l != null).ToList();
			// (the counter goes on from the highest of every copy: the next write is the newest, whatever the clocks say - AU4)
			SaveCount = all.Select(CountOf).DefaultIfEmpty(0L).Max();
			WarnFuture(mine, "in the mod's folder");
			WarnFuture(travelled, "in Raft's world folder");
			// (Raft is loading an OLDER save than the world's newest - one the player picked in Raft's Load Game box: the
			// state written with that save, so the world fits together. The newest save is read as always: the newest
			// copy, which also has what changed after it - a setting, or the copy another host sent.)
			if (loading != 0)
			{
				long newest = all.Select(RaftSaveOf).DefaultIfEmpty(0L).Max();
				KeyValuePair<string, string[]> match = saves.Where(s => RaftSaveOf(s.Value) == loading).Aggregate(default(KeyValuePair<string, string[]>), (b, s) => b.Value == null || Newer(s.Value, b.Value) ? s : b);
				// (older: another Raft save's copy was written after this one's - by the counter, not the clock - AU4)
				bool older = match.Value != null ? all.Any(l => RaftSaveOf(l) != 0 && RaftSaveOf(l) != loading && Newer(l, match.Value)) : loading < newest;
				// (an older save without a copy of its own - the first save of a new world, or one made before saves had
				// copies: its state isn't known; the newest is used, and the player is told - AU27)
				if (older && match.Value == null)
				{
					Debug.LogWarning("[CUSTOM ISLANDS] The save being loaded is older than the world's newest and has no copy of the custom islands' state: the newest is used");
					DynamicIslands.Notify("This older save has no record of its custom islands: their chests, quests and story are as in the world's newest save.", true);
				}
				if (older && match.Value != null)
				{
					LastSource = "Raft's save " + Path.GetFileName(match.Key);
					LastOlderSave = Path.GetFileName(match.Key);
					Debug.Log("[CUSTOM ISLANDS] An older save of the world: its custom islands, used objects, quests and the rest go back to that save too (" + match.Key + ")");
					DynamicIslands.Notify("An older save of this world: its custom islands, chests, quests and story are as they were then too.");
					return match.Value;
				}
			}
			if (mine == null && travelled == null) { LastSource = "none"; return null; }
			string[] chosen = mine;
			LastSource = "mod folder";
			if (travelled != null && (mine == null || Newer(travelled, mine)))
			{
				chosen = travelled;
				LastSource = "Raft's world folder";
				Debug.Log("[CUSTOM ISLANDS] The world's custom islands come from the copy in Raft's world folder" + (mine != null ? " (newer than the mod's own)" : "") + ": " + copy);
			}
			// (the newest copy was written after Raft's save being loaded - a story step, a chest, a setting, then a crash or
			// Alt+F4 before Raft saved again: the players' inventories are Raft's save's, so the islands' state is the copy
			// written with that save, and only the settings come from the newer copy - AU3)
			if (loading != 0 && IsBetween(chosen))
			{
				string[] saved = all.Where(l => !IsBetween(l) && RaftSaveOf(l) == loading).Aggregate((string[])null, (b, l) => b == null || Newer(l, b) ? l : b);
				if (saved != null && Newer(chosen, saved))
				{
					LastSource += ", islands' state from Raft's save";
					Debug.LogWarning("[CUSTOM ISLANDS] The world's newest copy was written after the Raft save being loaded (a crash?): its islands, chests and quests are as in that save, its settings as in the newest copy");
					return Merge(saved, chosen);
				}
			}
			return chosen;
		}

		/// <summary>Host, after writing the world file: the copy in Raft's world folder, and the copy for every player.</summary>
		public static void AfterSave(string[] lines)
		{
			string folder = RaftWorldFolder;
			if (folder != null)
				try { SafeFile.WriteAllLines(Path.Combine(folder, FileName), lines); }
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write the world's copy into Raft's world folder: " + e.Message); }
			WriteIntoSave(folder, lines);
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
			last = new[] { "# (nothing of Custom Islands in this world)" }.Concat(StampLines()).ToArray();
			// (the save still gets its note: loading it later means "nothing of the mod", not an older save's state)
			if (RaftSaveLine() != null) WriteIntoSave(folder, last);
			lastKey = SaveAndLoad.WorldGuid.ToString();
			Send(null);
		}

		/// <summary>The copy in the folder of the save Raft just wrote (it moves with it when Raft makes it a backup).</summary>
		static void WriteIntoSave(string worldFolder, string[] lines)
		{
			if (worldFolder == null || RaftSaveLine() == null) return;
			string save = LatestSaveFolder(worldFolder);
			if (save == null) return;
			try { SafeFile.WriteAllLines(Path.Combine(save, FileName), lines); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write the world's copy into Raft's save folder: " + e.Message); }
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

		/// <summary>A player: the host's world file as it last came (null until it has, and in a new world) - the host's plan,
		/// its progress and the islands left out, for the player's WorldPlan, WorldIslands and world window (they showed
		/// the player's own, empty, state).</summary>
		public static string[] HostLines { get; private set; }
		internal static void ForgetHostLines() { HostLines = null; }

		/// <summary>A value of the host's copy ("@key=value"), or null.</summary>
		public static string HostValue(string key)
		{
			if (HostLines == null) return null;
			string prefix = "@" + key + "=";
			string line = HostLines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
			return line != null ? line.Substring(prefix.Length).Trim() : null;
		}

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
			WarnFuture(lines, "the host sent");
			// (newer by the world's save counter, not the PCs' clocks - AU4)
			if (HostLines == null || !Newer(HostLines, lines)) HostLines = lines;
			string path = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), guid + ".txt");
			try
			{
				// (never over a newer copy: a late message from an older save)
				if (File.Exists(path) && Newer(File.ReadAllLines(path), lines)) return;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				SafeFile.WriteAllLines(path, lines);
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
