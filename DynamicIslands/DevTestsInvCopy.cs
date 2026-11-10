using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using DynamicIslands.Editor;

namespace DynamicIslands
{
	/// <summary>
	/// A joiner's inventory in a world that got a NEW id (WorldIdRepair, joiner part). Two players, run as
	/// (driver D:\Temp\scratch_8e762ddb\final34\invcopy.ps1):
	///   both in world A (host made it, player 2 joined): P2 CIInvCopy seed  (7 Plank + 3 Rope in the bag, the bag saved as
	///   player 2's no-id record); host CIMainMenu; CINewWorld CI &lt;B&gt;; CIInvCopy hostmark (B is noted as re-id'd);
	///   P2 joins B (fresh start there): P2 CIInvCopy check (the no-id bag is in, once); P2 CIMainMenu, joins again:
	///   P2 CIInvCopy again (no second copy, still 7 Plank); P2 CIInvCopy restore (player 2's old no-id record back).
	/// </summary>
	public static partial class DevTests
	{
		static RGD_User invCopyOld;
		static bool invCopySeeded;

		static RGD_Users InvCopyUsers() { return AccessTools.Field(typeof(SaveAndLoad), "rgd_users").GetValue(null) as RGD_Users; }
		static void InvCopySave() { AccessTools.Method(typeof(SaveAndLoad), "SaveUser").Invoke(ComponentManager<SaveAndLoad>.Value, null); }
		static RGD_User InvCopyClone(RGD_User u)
		{
			if (u == null) return null;
			var bf = new BinaryFormatter();
			using (var ms = new MemoryStream()) { bf.Serialize(ms, u); ms.Position = 0; return (RGD_User)bf.Deserialize(ms); }
		}

		[ConsoleCommand(name: "CIInvCopy", docs: "Dev, two players: a joiner's no-id inventory comes into a world that got a new id. seed (player 2) | hostmark (host, new world) | check (player 2 after joining it) | again (player 2 after joining once more) | restore (player 2)")]
		public static void InvCopyCommand(string[] args)
		{
			string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (!LoadSceneManager.IsGameSceneLoaded) { Fail("CIInvCopy: run in a world"); return; }
			bool ok = true;
			Network_Player player = ComponentManager<Network_Player>.Value;
			ulong steam = Steamworks.SteamUser.GetSteamID().m_SteamID;
			if (what == "seed")
			{
				if (Raft_Network.IsHost) { Fail("CIInvCopy seed: run on player 2"); return; }
				RGD_Users users = InvCopyUsers();
				Check(ref ok, users != null, "player 2's records are loaded");
				if (!ok) { Fail("inv copy seed"); return; }
				if (!invCopySeeded) { invCopyOld = InvCopyClone(users.GetUser(Guid.Empty, steam)); invCopySeeded = true; }
				player.Inventory.RemoveItem("Plank", player.Inventory.GetItemCount("Plank"));
				player.Inventory.RemoveItem("Rope", player.Inventory.GetItemCount("Rope"));
				player.Inventory.AddItem("Plank", 7);
				player.Inventory.AddItem("Rope", 3);
				users.AddUser(Guid.Empty, player);
				InvCopySave();
				RGD_User e = users.GetUser(Guid.Empty, steam);
				Check(ref ok, e != null && e.rgdPlayer != null && e.rgdPlayer.rgdInventory != null, "player 2 has a no-id record now");
				Log("  (player 2's old no-id record: " + (invCopyOld == null ? "none" : "kept to put back") + ")");
				if (ok) Log("PASS: inv copy seed"); else Fail("inv copy seed");
				return;
			}
			if (what == "hostmark")
			{
				if (!Raft_Network.IsHost) { Fail("CIInvCopy hostmark: run on the host"); return; }
				Check(ref ok, WorldIdRepair.NewIdMessage() == null, "a world that kept its id tells joiners nothing");
				WorldIdRepair.MarkNewId(SaveAndLoad.WorldGuid);
				IslandNetMessage m = WorldIdRepair.NewIdMessage();
				Check(ref ok, m != null && m.Kind == IslandNetMessage.WorldNewId && m.Name == SaveAndLoad.WorldGuid.ToString(), "a re-id'd world tells joiners its id");
				if (ok) Log("PASS: inv copy hostmark"); else Fail("inv copy hostmark");
				return;
			}
			if (what == "check" || what == "again")
			{
				if (Raft_Network.IsHost) { Fail("CIInvCopy " + what + ": run on player 2"); return; }
				DynamicIslands.instance.StartCoroutine(InvCopyCheck(what == "again"));
				return;
			}
			if (what == "restore")
			{
				RGD_Users users = InvCopyUsers();
				if (users == null || !invCopySeeded) { Log("PASS: inv copy restore (nothing to put back)"); return; }
				users.users.RemoveAll(u => u != null && u.worldGuid == Guid.Empty.ToString() && u.steamID == steam);
				if (invCopyOld != null) users.users.Add(invCopyOld);
				InvCopySave();
				invCopySeeded = false;
				Log("PASS: inv copy restore (" + (invCopyOld == null ? "the test record removed" : "the old record back") + ")");
				return;
			}
			Fail("CIInvCopy seed|hostmark|check|again|restore");
		}

		static IEnumerator InvCopyCheck(bool again)
		{
			string name = again ? "inv copy again" : "inv copy check";
			float until = Time.realtimeSinceStartup + 40f;
			// (the host's note comes in the join reply)
			while ((WorldIdRepair.LastClient == null) && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.5f);
			bool ok = true;
			Network_Player player = ComponentManager<Network_Player>.Value;
			string last = WorldIdRepair.LastClient;
			int planks = player.Inventory.GetItemCount("Plank"), ropes = player.Inventory.GetItemCount("Rope");
			if (!again)
			{
				Check(ref ok, last != null && last.StartsWith("copied"), "the host's note came and the no-id bag was copied (" + last + ")");
				Check(ref ok, planks == 7 && ropes == 3, "the bag has the no-id record's 7 Plank and 3 Rope (" + planks + ", " + ropes + ")");
				RGD_User here = InvCopyUsers().GetUser(SaveAndLoad.WorldGuid, Steamworks.SteamUser.GetSteamID().m_SteamID);
				Check(ref ok, here != null && here.rgdPlayer != null && here.rgdPlayer.rgdInventory != null, "it is saved under the world's id");
			}
			else
			{
				Check(ref ok, last != null && last.StartsWith("skipped"), "a second join copies nothing (" + last + ")");
				Check(ref ok, planks == 7 && ropes == 3, "still 7 Plank and 3 Rope, nothing doubled (" + planks + ", " + ropes + ")");
			}
			WorldIdRepair.LastClient = null;
			if (ok) Log("PASS: " + name); else Fail(name);
		}
	}
}
