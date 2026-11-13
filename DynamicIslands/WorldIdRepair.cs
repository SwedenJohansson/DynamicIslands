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
	/// loads under the id already. The world's next save has the id.
	///
	/// Players who joined such a world keep their no-id record on their own PC. When the host had to make a NEW id (case 2),
	/// it keeps a note (worlds/&lt;id&gt;.newid) and tells each joiner (kind WorldNewId, for NewIdDays after the repair). A joiner
	/// whom Raft gave a fresh start in the world (no record under the id when it joined: Raft's RestoreUser runs before any
	/// mod message comes) gets the inventory of its own no-id record put back, once per world (worlds/&lt;id&gt;.newid-copied),
	/// and only while its bag still holds nothing but Raft's starting items: a real record is never overwritten and nothing
	/// is doubled.
	/// </summary>
	internal static class WorldIdRepair
	{
		static readonly Regex GuidText = new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
		const int MaxBackups = 30;

		/// <summary>Tests: the last repair ("restored &lt;id&gt; from &lt;folder&gt;", "new &lt;id&gt;"), or null.</summary>
		internal static string Last;
		/// <summary>Tests: a repair copied the no-id inventory to the world's id.</summary>
		internal static bool LastCopiedPlayer;
		/// <summary>Tests: no repair (a world loads without its id, as before 3.4).</summary>
		internal static bool TestOff;

		/// <summary>Raft read the world's id from its save (RGD_World.RestoreWorld, inside RestoreRGDGame): before LoadComplete
		/// and before the player's inventory is looked up.</summary>
		internal static void AfterWorldRead()
		{
			if (TestOff || SaveAndLoad.WorldGuid != Guid.Empty || !Raft_Network.IsHost) return;
			string world = WorldCopy.RaftWorldFolder;
			if (world == null || string.IsNullOrEmpty(SaveAndLoad.CurrentGameFileName)) return;
			string oldKey = IslandWorldState.WorldKey;
			string from;
			Guid id = IdFromBackups(world, out from);
			bool restored = id != Guid.Empty;
			if (!restored) id = Guid.NewGuid();
			LastCopiedPlayer = CopyPlayer(id);
			SaveAndLoad.WorldGuid = id;
			if (!restored) MarkNewId(id);
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
			// (folder names are dates, yyyy.MM.dd-H.mm, the newest with "-Latest")
			foreach (string d in dirs.OrderByDescending(DayRepair.SaveOrderKey, StringComparer.Ordinal).Take(MaxBackups))
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

		/// <summary>Days after a repair with a new id in which joiners are told (their fresh start is put right).</summary>
		internal const int NewIdDays = 60;
		static string NewIdPath(Guid id) { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), id + ".newid"); }

		/// <summary>Host: the world got a new id at this load - noted so joiners get their inventory back (OnNewIdFromHost).</summary>
		internal static void MarkNewId(Guid id)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(NewIdPath(id)));
				File.WriteAllText(NewIdPath(id), DateTime.UtcNow.ToString("o"));
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair, the note for joiners: " + e.Message); }
		}

		/// <summary>Host -> a joiner: this world got a new id in the last NewIdDays days (null: it didn't).</summary>
		internal static IslandNetMessage NewIdMessage()
		{
			if (!Raft_Network.IsHost || SaveAndLoad.WorldGuid == Guid.Empty) return null;
			string path = NewIdPath(SaveAndLoad.WorldGuid);
			if (!File.Exists(path)) return null;
			DateTime when;
			string first = (File.ReadAllText(path) ?? "").Trim();
			if (!DateTime.TryParse(first, null, System.Globalization.DateTimeStyles.RoundtripKind, out when)) when = File.GetLastWriteTimeUtc(path);
			if ((DateTime.UtcNow - when.ToUniversalTime()).TotalDays > NewIdDays) return null;
			return new IslandNetMessage { Kind = IslandNetMessage.WorldNewId, Name = SaveAndLoad.WorldGuid.ToString() };
		}

		// (client: what Raft found when this player joined - RestoreUser runs before the mod's messages)
		static Guid joinGuid;
		static bool joinHadRecord;
		/// <summary>Tests: the last joiner copy ("copied N", "skipped: why"), or null.</summary>
		internal static string LastClient;

		/// <summary>Client: Raft asked whether this player has a record in the world it joins (SaveAndLoad.PlayerSaveExistsInWorld).</summary>
		internal static void AfterRecordLookup(bool had)
		{
			if (Raft_Network.IsHost) return;
			// (only the first lookup of a join counts: a later one would see the fresh record Raft saved)
			if (joinGuid == SaveAndLoad.WorldGuid) return;
			joinGuid = SaveAndLoad.WorldGuid;
			joinHadRecord = had;
		}

		/// <summary>Client: left the world - the next join's lookup counts again.</summary>
		internal static void ForgetJoin() { joinGuid = Guid.Empty; }

		static readonly HashSet<string> StartingItems = new HashSet<string> { "Hook_Plastic", "Hammer", "Axe", "PaintBrush", "DevHat", "Backpack_Advanced", "ZiplineTool_Electric" };

		/// <summary>Client: the host says this world got a new id. A fresh start here gets the no-id record's inventory, once.</summary>
		internal static void OnNewIdFromHost(string name)
		{
			Guid id;
			if (Raft_Network.IsHost || !Guid.TryParse(name ?? "", out id) || id == Guid.Empty || id != SaveAndLoad.WorldGuid) return;
			string why = ClientCopy(id);
			LastClient = why;
			Debug.Log("[CUSTOM ISLANDS] World id repair (joiner): " + why);
		}

		static string ClientCopy(Guid id)
		{
			if (joinGuid != id) return "skipped: no join lookup for this world";
			if (joinHadRecord) return "skipped: this player had a record in the world";
			string done = NewIdPath(id) + "-copied";
			if (File.Exists(done)) return "skipped: copied before";
			try
			{
				RGD_Users users = AccessTools.Field(typeof(SaveAndLoad), "rgd_users").GetValue(null) as RGD_Users;
				if (users == null || users.users == null) return "skipped: no records";
				ulong steam = Steamworks.SteamUser.GetSteamID().m_SteamID;
				RGD_User empty = users.GetUser(Guid.Empty, steam);
				RGD_PlayerInventory inv = empty != null && empty.rgdPlayer != null ? empty.rgdPlayer.rgdInventory : null;
				int items = inv == null ? 0 : Filled(inv).Count();
				if (items == 0) return "skipped: no no-id inventory";
				// (the record Raft made at the fresh start, and the bag now: Raft's starting items only)
				RGD_User here = users.GetUser(id, steam);
				RGD_PlayerInventory hereInv = here != null && here.rgdPlayer != null ? here.rgdPlayer.rgdInventory : null;
				if (hereInv != null && !OnlyStarting(Filled(hereInv).Select(s => { Item_Base b = ItemManager.GetItemByIndex(s.itemIndex); return b == null ? "?" : b.UniqueName; })))
					return "skipped: the record in the world has more than the starting items";
				Network_Player player = ComponentManager<Network_Player>.Value;
				if (player == null || player.Inventory == null) return "skipped: no player";
				if (!OnlyStarting(player.Inventory.allSlots.Where(s => s != null && !s.IsEmpty && s.itemInstance != null).Select(s => s.itemInstance.UniqueName)))
					return "skipped: the bag has more than the starting items";
				inv.RestoreInventory(player.Inventory);
				File.WriteAllText(done, DateTime.UtcNow.ToString("o"));
				// (saved under the world's id now: a quit before the next save keeps it)
				try { AccessTools.Method(typeof(SaveAndLoad), "SaveUser").Invoke(ComponentManager<SaveAndLoad>.Value, null); }
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair (joiner), saving: " + e.Message); }
				return "copied " + items;
			}
			catch (Exception e) { return "skipped: " + e.Message; }
		}

		static IEnumerable<RGD_Slot> Filled(RGD_PlayerInventory inv)
		{
			return (inv.slots ?? new RGD_Slot[0]).Concat(inv.equipSlots ?? new RGD_Slot[0]).Where(s => s != null && s.HasItem);
		}

		static bool OnlyStarting(IEnumerable<string> names) { return names.All(n => StartingItems.Contains(n)); }

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

	[HarmonyPatch(typeof(SaveAndLoad), "PlayerSaveExistsInWorld")]
	static class WorldIdRepairJoinPatch
	{
		static void Postfix(bool __result)
		{
			try { WorldIdRepair.AfterRecordLookup(__result); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair (join): " + e.Message); }
		}
	}

	[HarmonyPatch(typeof(RGD_World), "RestoreWorld")]
	static class WorldIdRepairPatch
	{
		static void Postfix()
		{
			try { WorldIdRepair.AfterWorldRead(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] World id repair: " + e.Message); }
			try { DayRepair.AfterWorldRead(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Day repair: " + e.Message); }
		}
	}
}
