using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using DynamicIslands.Editor;

namespace DynamicIslands
{
	/// <summary>
	/// Gives a world back its day (bug 11-11: "the calendar resets to day 0, the raft is still there").
	/// Before 3.4, Esc > Quit game made one more save after Raft had left the world (SaveSoon's save on quit; SaveGuard has
	/// stopped it since 3.4). Leaving, Raft had already set its day counter to 0 (WorldManager, SceneEvent.LeaveGame) and
	/// cleared the world id, so that last save had the raft but day 0 and no id. 3.4's WorldIdRepair gives such a world its
	/// id back from the older save Raft made a moment before (Raft_Network.LeaveGame saves first), but not its day.
	///
	/// Host, when a world loads (after WorldIdRepair): the world's backup folders hold a save with the world's id and day D
	/// followed by a save without an id (that quit save). If the loaded day is below D, the day becomes D + the loaded day
	/// (days played since the reset count on). Once fixed and saved, the day is D or more: it isn't done twice.
	/// </summary>
	internal static class DayRepair
	{
		static readonly Regex GuidText = new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
		const int MaxBackups = 30;

		/// <summary>Tests: the last repair ("day A -> B from <folder>"), or null.</summary>
		internal static string Last;
		/// <summary>Tests: no repair (a world loads with the day of its save, as in 3.4).</summary>
		internal static bool TestOff;

		/// <summary>Host, Raft read the world (RGD_World.RestoreWorld; the id repair has run).</summary>
		internal static void AfterWorldRead()
		{
			Last = null;
			if (TestOff || !Raft_Network.IsHost || SaveAndLoad.WorldGuid == Guid.Empty) return;
			string world = WorldCopy.RaftWorldFolder;
			if (world == null) return;
			int now = WorldManager.DayCounter;
			string from;
			int day = DayBeforeReset(world, SaveAndLoad.WorldGuid, out from);
			if (day <= now) return;
			int to = day + now;
			SetDay(to);
			Last = "day " + now + " -> " + to + " from " + from;
			Debug.LogWarning("[CUSTOM ISLANDS] World '" + SaveAndLoad.CurrentGameFileName + "' lost its day to an Esc > Quit game save before 3.4 (day 0, no world id):"
				+ " its day is back, " + now + " -> " + to + " (day " + day + " in its save " + from + (now > 0 ? ", plus " + now + " played since" : "") + ")");
		}

		/// <summary>
		/// The day in the newest save of the world with the id <paramref name="id"/> that is followed by a save without an id
		/// (the quit save that reset the day), or -1.
		/// </summary>
		internal static int DayBeforeReset(string world, Guid id, out string from)
		{
			from = null;
			string[] dirs;
			try { dirs = Directory.GetDirectories(world); } catch { return -1; }
			// (folder names are dates, yyyy.MM.dd-HH.mm, the newest with "-Latest"; oldest first here)
			List<string> order = dirs.OrderBy(d => Path.GetFileName(d).Replace("-Latest", ""), StringComparer.Ordinal).ToList();
			if (order.Count > MaxBackups) order = order.Skip(order.Count - MaxBackups).ToList();
			var files = new List<string>();
			var ids = new List<Guid?>(); // (null: none or several ids in it - not one of ours)
			foreach (string d in order)
			{
				try
				{
					string rgd = Directory.GetFiles(d, "*.rgd").FirstOrDefault();
					if (rgd == null) continue;
					var found = new HashSet<Guid>();
					string text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(rgd));
					foreach (Match m in GuidText.Matches(text)) { Guid g; if (Guid.TryParse(m.Value, out g)) found.Add(g); }
					files.Add(rgd);
					ids.Add(found.Count == 1 ? found.First() : (Guid?)null);
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Day repair, reading " + d + ": " + e.Message); }
			}
			for (int i = files.Count - 2; i >= 0; i--)
			{
				if (ids[i] != id || ids[i + 1] != Guid.Empty) continue;
				try
				{
					RGD_Game game = SaveAndLoad.Load<RGD_Game>(files[i]);
					if (game == null || game.world == null) return -1;
					from = Path.GetFileName(Path.GetDirectoryName(files[i]));
					return game.world.dayCounter;
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Day repair, reading " + files[i] + ": " + e.Message); return -1; }
			}
			return -1;
		}

		/// <summary>WorldManager.DayCounter's setter (not public in every Raft build).</summary>
		internal static void SetDay(int day)
		{
			System.Reflection.PropertyInfo p = typeof(WorldManager).GetProperty("DayCounter", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
			System.Reflection.MethodInfo set = p != null ? p.GetSetMethod(true) : null;
			if (set != null) set.Invoke(null, new object[] { day });
		}
	}
}
