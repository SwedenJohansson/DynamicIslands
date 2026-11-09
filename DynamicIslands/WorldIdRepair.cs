using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using DynamicIslands.Editor;

namespace DynamicIslands
{
	/// <summary>
	/// Gives a world back its own id when it loads without one (bug 11-09). Esc > Quit game used to make one more save after
	/// Raft had already cleared the world's id (SaveGuard now stops it): that save had no id, and from then on the world was
	/// played without one. Raft keeps every player's inventory in one file for all worlds (RGD_Users.rgd), found by world id
	/// and Steam id, so all such worlds shared one inventory - the last one saved. The mod's quest rewards and story numbers
	/// were shared the same way.
	///
	/// Host, when a world loads without an id:
	/// 1. An older save of the world (its backup folders) has the world's real id: that id is used again. The player's
	///    inventory under it is the one from the world's last good save.
	/// 2. Otherwise (a world from before Raft gave worlds ids, or no older save is left) the world gets a new id, and the
	///    shared no-id inventory is copied to it, so the player keeps what they have now.
	/// Either way the world's custom island state kept under its name (noid-&lt;name&gt;) moves to the id when it is newer.
	/// Runs right after Raft read the world's id (RGD_World.RestoreWorld), before LoadComplete: the mod's world state then
	/// loads under the id already. The world's next save has the id. Players who joined such a world keep their no-id record on their own PC: Raft
	/// gives them a fresh start in it once.
	/// </summary>
	internal static class WorldIdRepair
	{
		static readonly Regex GuidText = new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
		const int MaxBackups = 30;

		/// <summary>Tests: the last repair ("restored &lt;id&gt; from &lt;folder&gt;", "new &lt;id&gt;"), or null.</summary>
		internal static string Last;
		/// <summary>Tests: a repair copied the no-id inventory to the world's id.</summary>
		internal static bool LastCopiedPlayer;

		/// <summary>Raft read the world's id from its save (RGD_World.RestoreWorld, inside RestoreRGDGame): before LoadComplete
		/// and before the player's inventory is looked up.</summary>
		internal static void AfterWorldRead()
		{
			if (SaveAndLoad.WorldGuid != Guid.Empty || !Raft_Network.IsHost) return;
			string world = WorldCopy.RaftWorldFolder;
			if (world == null || string.IsNullOrEmpty(SaveAndLoad.CurrentGameFileName)) return;
			string oldKey = IslandWorldState.WorldKey;
			string from;
			Guid id = IdFromBackups(world, out from);
			bool restored = id != Guid.Empty;
			if (!restored) id = Guid.NewGuid();
			LastCopiedPlayer = CopyPlayer(id);
			SaveAndLoad.WorldGuid = id;
			CarryState(oldKey, id.ToString());
			Last = restored ? "restored " + id + " from " + from : "new " + id;
			Debug.LogWarning("[CUSTOM ISLANDS] World '" + SaveAndLoad.CurrentGameFileName + "' had no world id (an Esc > Quit game save before 3.4): "
				+ (restored ? "its id " + id + " is back, from its save " + from : "it has a new id " + id)
				+ (LastCopiedPlayer ? "; the inventory is copied to it" : "") + ". Its next save keeps it.");
		}

		/// <summary>The id in the newest older save of the world that has exactly one, or Guid.Empty.</summary>
		static Guid IdFromBackups(string world, out string from)
		{
			from = null;
			string[] dirs;
			try { dirs = Directory.GetDirectories(world); } catch { return Guid.Empty; }
			// (folder names are dates, yyyy.MM.dd-HH.mm, the newest with "-Latest")
			foreach (string d in dirs.OrderByDescending(d => Path.GetFileName(d).Replace("-Latest", ""), StringComparer.Ordinal).Take(MaxBackups))
			{
				try
				{
					foreach (string rgd in Directory.GetFiles(d, "*.rgd"))
					{
						string text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(rgd));
						var ids = new HashSet<Guid>();
						foreach (Match m in GuidText.Matches(text)) { Guid g; if (Guid.TryParse(m.Value, out g) && g != Guid.Empty) ids.Add(g); }
						if (ids.Count == 1) { from = Path.GetFileName(d); return ids.First(); }
					}
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair, reading " + d + ": " + e.Message); }
			}
			return Guid.Empty;
		}

		/// <summary>The local player has no record under the id: the no-id one is copied to it. True when copied.</summary>
		static bool CopyPlayer(Guid id)
		{
			try
			{
				var field = AccessTools.Field(typeof(SaveAndLoad), "rgd_users");
				RGD_Users users = field.GetValue(null) as RGD_Users;
				if (users == null)
				{
					var load = AccessTools.Method(typeof(SaveAndLoad), "LoadRGDUsers");
					load.Invoke(load.IsStatic ? null : ComponentManager<SaveAndLoad>.Value, null);
					users = field.GetValue(null) as RGD_Users;
				}
				if (users == null || users.users == null) return false;
				ulong steam = Steamworks.SteamUser.GetSteamID().m_SteamID;
				if (users.GetUser(id, steam) != null) return false;
				RGD_User empty = users.GetUser(Guid.Empty, steam);
				if (empty == null) return false;
				RGD_User copy;
				var bf = new BinaryFormatter();
				using (var ms = new MemoryStream())
				{
					bf.Serialize(ms, empty);
					ms.Position = 0;
					copy = (RGD_User)bf.Deserialize(ms);
				}
				copy.worldGuid = id.ToString();
				users.users.Add(copy);
				return true;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair, the inventory: " + e.Message); return false; }
		}

		/// <summary>The world's files kept under its name move to the id's, when newer than what the id has.</summary>
		static void CarryState(string oldKey, string newKey)
		{
			if (!oldKey.StartsWith(IslandWorldState.NoIdPrefix)) return;
			string dir = Path.Combine(DynamicIslands.assetpath, "worlds");
			try
			{
				if (!Directory.Exists(dir)) return;
				foreach (string f in Directory.GetFiles(dir, oldKey + "*"))
				{
					string rest = Path.GetFileName(f).Substring(oldKey.Length);
					if (rest != ".txt" && !Regex.IsMatch(rest, @"^-[0-9a-z]+\.rewards$")) continue;
					string to = Path.Combine(dir, newKey + rest);
					if (File.Exists(to) && File.GetLastWriteTimeUtc(to) > File.GetLastWriteTimeUtc(f)) continue;
					File.Copy(f, to, true);
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair, the island state: " + e.Message); }
		}
	}

	[HarmonyPatch(typeof(RGD_World), "RestoreWorld")]
	static class WorldIdRepairPatch
	{
		static void Postfix()
		{
			try { WorldIdRepair.AfterWorldRead(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair: " + e.Message); }
		}
	}
}
