using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Each crew member's share of a quest's Raft-item reward (ROADMAP LM8): a player who wasn't at the island when its
	/// quest was done (or joined the world later) gets the reward when they come to the island - once. This machine's own
	/// record, per world: Mods\DynamicIslands\worlds\&lt;world&gt;.rewards ("rewarded &lt;island&gt;" / "owed &lt;island&gt;").
	/// </summary>
	public static class QuestRewards
	{
		static Guid loadedFor;
		static readonly HashSet<string> rewarded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		static readonly HashSet<string> owed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		static string FilePath { get { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), SaveAndLoad.WorldGuid + ".rewards"); } }

		static void Load()
		{
			if (loadedFor == SaveAndLoad.WorldGuid) return;
			loadedFor = SaveAndLoad.WorldGuid;
			rewarded.Clear(); owed.Clear();
			try
			{
				if (!File.Exists(FilePath)) return;
				foreach (string line in File.ReadAllLines(FilePath))
				{
					if (line.StartsWith("rewarded ")) rewarded.Add(line.Substring(9).Trim());
					else if (line.StartsWith("owed ")) owed.Add(line.Substring(5).Trim());
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Quest rewards file: " + e.Message); }
		}

		/// <summary>Read the file again when next asked (an island was renamed in it).</summary>
		internal static void Forget() { loadedFor = Guid.Empty; }

		static void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
				File.WriteAllLines(FilePath, rewarded.Select(r => "rewarded " + r).Concat(owed.Where(o => !rewarded.Contains(o)).Select(o => "owed " + o)).ToArray());
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
			if (rewarded.Contains(island)) return false;
			if (near) { give(); rewarded.Add(island); owed.Remove(island); }
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
			rewarded.Add(island); owed.Remove(island);
			Save();
		}
	}
}
