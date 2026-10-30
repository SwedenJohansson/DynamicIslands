using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using RaftModLoader;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Each crew member's share of a quest's Raft-item reward (ROADMAP LM8): a player who wasn't at the island when its
	/// quest was done (or joined the world later) gets the reward when they come to the island - once. This machine's own
	/// record, per world: Mods\DynamicIslands\worlds\&lt;world&gt;.rewards ("rewarded &lt;island&gt;" / "owed &lt;island&gt;").
	///
	/// Raft's saves (review 10-30): the record is written at once, but the player's items and the quest only last with
	/// Raft's next save (the host's, for a player who joins). So each reward also notes the Raft save it was given after
	/// ("after &lt;stamp&gt; &lt;island&gt;", RGD_Game.lastPlayedDateTicks: the host's own, or for a player the host's from its world
	/// copies). A world loaded from that save or an older one - a crash, a quit without saving, an older save picked -
	/// doesn't have the items: a quest done again there pays again. Only a quest done again does: an island whose quest is
	/// still done in the loaded world (a player who left and came back, the host still playing) doesn't pay twice. Lines
	/// without a stamp (files from before) count as before.
	/// </summary>
	public static class QuestRewards
	{
		// (the world and the player it was read for: before the local player is there the id is "local" - review 2026-10-06)
		static string loadedFor;
		/// <summary>The record is there but couldn't be read (locked by an antivirus or a sync): not saved over until it is.</summary>
		static bool readFailed;
		static readonly HashSet<string> rewarded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		static readonly HashSet<string> owed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>The Raft save each reward was given after (0 or none = not known: a file from before, or a world not saved yet).</summary>
		static readonly Dictionary<string, long> givenAfter = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		/// <summary>The rewards given since the world loaded (or the host's arrived): never given again in that time.</summary>
		static readonly HashSet<string> givenNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Raft's save the world was loaded from (a player: the host's, from its first world copy); 0 = not known.</summary>
		internal static long LoadedSave;
		/// <summary>Raft's last save so far (a player: the host's): a reward given now is in the next one.</summary>
		internal static long LastSave;
		/// <summary>A player: the host's first world copy since joining came (LoadedSave is known).</summary>
		static bool hostCopyCame;

		/// <summary>Host: a world loads (Raft's stamp of the save it loads from, 0 for a new world).</summary>
		internal static void OnWorldLoaded(long raftSave) { LoadedSave = LastSave = raftSave; givenNow.Clear(); hostCopyCame = false; }

		/// <summary>A player: the host's world arrived; its save is known when its first world copy comes.</summary>
		internal static void OnWorldReceived() { LoadedSave = LastSave = 0L; givenNow.Clear(); hostCopyCame = false; }

		/// <summary>Host: Raft saved the world (its stamp of the save).</summary>
		internal static void AfterRaftSave(long raftSave) { if (raftSave != 0L) LastSave = raftSave; }

		/// <summary>A player: a copy of the host's world file came - "@raftsave=" (written with the host's Raft save) or
		/// "@between=" (the host's Raft save it follows). The first one since joining is the save the host's world is from.</summary>
		internal static void OnHostCopy(Guid world, long raftSave, long between)
		{
			if (Raft_Network.IsHost || world != SaveAndLoad.WorldGuid) return;
			long save = raftSave != 0L ? raftSave : between;
			if (save == 0L) return;
			if (!hostCopyCame) { hostCopyCame = true; LoadedSave = save; }
			LastSave = Math.Max(LastSave, save);
		}

		/// <summary>The reward was given after the Raft save the world was loaded from (and not since): that world doesn't have it.</summary>
		static bool NotInLoadedSave(string island)
		{
			long after;
			return LoadedSave != 0L && !givenNow.Contains(island) && givenAfter.TryGetValue(island, out after) && after != 0L && after >= LoadedSave;
		}

		static void MarkGiven(string island)
		{
			rewarded.Add(island);
			givenAfter[island] = LastSave;
			givenNow.Add(island);
		}

		// (per world and player: a player who joins uses the host's world id, and two players on one PC - or a host who
		// later joins its own world - each have their own record)
		static string FilePath { get { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), SaveAndLoad.WorldGuid + "-" + PlayerId + ".rewards"); } }
		/// <summary>The record before 2026-10-06, by world only: kept by the PC that hosted the world (it was that player's).</summary>
		static string OldFilePath { get { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), SaveAndLoad.WorldGuid + ".rewards"); } }

		static string PlayerId
		{
			get
			{
				try { Network_Player p = RAPI.GetLocalPlayer(); if (p != null) return p.steamID.Id.ToString(System.Globalization.CultureInfo.InvariantCulture); } catch { }
				return "local";
			}
		}

		static void Load()
		{
			string key = SaveAndLoad.WorldGuid + "-" + PlayerId;
			if (loadedFor == key) return;
			// (what happened while the player wasn't known yet is kept, and saved with the player's own record)
			bool merge = loadedFor == SaveAndLoad.WorldGuid + "-local";
			loadedFor = key;
			readFailed = false;
			if (!merge) { rewarded.Clear(); owed.Clear(); givenAfter.Clear(); }
			try
			{
				string path = FilePath;
				if (!File.Exists(path) && Raft_Network.IsHost && File.Exists(OldFilePath)) path = OldFilePath;
				if (File.Exists(path))
					foreach (string line in File.ReadAllLines(path))
					{
						if (line.StartsWith("rewarded ")) rewarded.Add(line.Substring(9).Trim());
						else if (line.StartsWith("owed ")) owed.Add(line.Substring(5).Trim());
						else if (line.StartsWith("after "))
						{
							// ("after <stamp> <island>": older versions skip the line)
							string rest = line.Substring(6).Trim();
							int sp = rest.IndexOf(' ');
							long at;
							if (sp > 0 && long.TryParse(rest.Substring(0, sp), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out at) && !givenAfter.ContainsKey(rest.Substring(sp + 1).Trim()))
								givenAfter[rest.Substring(sp + 1).Trim()] = at;
						}
					}
				if (merge && (rewarded.Count > 0 || owed.Count > 0)) Save();
			}
			// (what this session gives is still remembered; the file isn't written over with only that - every reward given
			// before came again)
			catch (Exception e) { readFailed = true; Debug.LogWarning("[CUSTOM ISLANDS] Quest rewards file: " + e.Message); }
		}

		/// <summary>Read the file again when next asked (an island was renamed in it).</summary>
		internal static void Forget() { loadedFor = null; }

		/// <summary>Tests: this player's rewards of an island's quests (key, key#questN) are forgotten, so a test island run again
		/// in the same world pays again.</summary>
		internal static void ForgetIsland(string island)
		{
			Load();
			Predicate<string> his = k => k.Equals(island, StringComparison.OrdinalIgnoreCase) || k.StartsWith(island + "#", StringComparison.OrdinalIgnoreCase);
			foreach (string k in givenAfter.Keys.Where(k => his(k)).ToList()) givenAfter.Remove(k);
			givenNow.RemoveWhere(his);
			if (rewarded.RemoveWhere(his) + owed.RemoveWhere(his) > 0) Save();
		}

		/// <summary>The file this player's rewards of the world are kept in (logs).</summary>
		internal static string WhereKept { get { return Path.GetFileName(FilePath); } }

		static void Save()
		{
			// (not before the player is known: "local"'s small record would be written over the player's own later)
			if (PlayerId == "local") { Debug.LogWarning("[CUSTOM ISLANDS] Quest rewards: not saved yet - the player isn't there yet"); return; }
			if (readFailed) { Debug.LogWarning("[CUSTOM ISLANDS] Quest rewards: not saved - the file couldn't be read, so it stays as it was"); return; }
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				// (in one step: a crash part way no longer leaves half a file, and the rewards given twice - AU41)
				SafeFile.WriteAllLines(FilePath, rewarded.Select(r => "rewarded " + r).Concat(owed.Where(o => !rewarded.Contains(o)).Select(o => "owed " + o))
					.Concat(rewarded.Where(r => givenAfter.ContainsKey(r) && givenAfter[r] != 0L).Select(r => "after " + givenAfter[r].ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + r)).ToArray());
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Quest rewards file: " + e.Message); }
		}

		/// <summary>Whether this machine's player got the island's quest reward already in this world.</summary>
		public static bool Rewarded(string island) { Load(); return rewarded.Contains(island); }

		/// <summary>Whether this machine's player still has the island's reward to come.</summary>
		public static bool Owed(string island) { Load(); return owed.Contains(island) && !rewarded.Contains(island); }

		/// <summary>The quest was done: give the reward now (near) or keep it for later. False if given before.</summary>
		public static bool OnCompleted(string island, bool near, Action give)
		{
			Load();
			if (rewarded.Contains(island))
			{
				if (!NotInLoadedSave(island)) return false;
				// (given after the Raft save this world was loaded from: a crash, a quit without saving or an older save took the
				// items back with the quest, which was done again - given again, not refused for good - review 10-30)
				Debug.Log("[CUSTOM ISLANDS] Quest reward of '" + island + "': given after the Raft save this world was loaded from - given again");
				rewarded.Remove(island);
				givenAfter.Remove(island);
			}
			if (near) { give(); MarkGiven(island); owed.Remove(island); }
			else owed.Add(island);
			Save();
			return true;
		}

		/// <summary>The player has come to an island whose reward is owed: give it.</summary>
		public static void Collect(string island, Action give)
		{
			Load();
			if (!owed.Contains(island) || rewarded.Contains(island)) return;
			give();
			MarkGiven(island); owed.Remove(island);
			Save();
		}
	}
}
