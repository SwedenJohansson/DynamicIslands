using System;
using System.Collections;
using System.Linq;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Inventory bug 11-09 (two worlds with one inventory; SaveGuard in SaveSoon.cs). TEST_CATALOGUE INV rows.
	/// CIInvFix show|give|exit|guard drive the in-game repro (exit = the pause menu's "Quit game", which quits Raft);
	/// CIInvFixGuard is the quick check without a quit.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIInvFix", docs: "Dev, inventory bug 11-09: CIInvFix show (logs the world id and the player's items) | give <item unique name> <n> | exit save|nosave (the pause menu's Quit game: Raft quits) | guard on|off (off = the old behaviour)")]
		public static void InvFixCommand(string[] args)
		{
			string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "show";
			Network_Player p = RAPI.GetLocalPlayer();
			if (what == "guard")
			{
				SaveGuard.TestOff = args.Length > 1 && args[1].ToLowerInvariant() == "off";
				Log("INVFIX guard " + (SaveGuard.TestOff ? "off" : "on"));
				Log("PASS: invfix guard");
				return;
			}
			if (p == null || p.Inventory == null) { Fail("invfix: not in a world"); return; }
			if (what == "give" && args.Length > 1)
			{
				int n = 1;
				if (args.Length > 2) int.TryParse(args[2], out n);
				p.Inventory.AddItem(args[1], n);
				Log("INVFIX gave " + n + " " + args[1]);
			}
			if (what == "exit")
			{
				bool save = args.Length < 2 || args[1].ToLowerInvariant() != "nosave";
				Log("INVFIX exit " + (save ? "save" : "nosave") + " guard " + (SaveGuard.TestOff ? "off" : "on"));
				ComponentManager<Raft_Network>.Value.LeaveGame((DisconnectReason)2, (SceneName)3, save, false);
				return;
			}
			var items = Items(p);
			Log("INVFIX inv world=\"" + SaveAndLoad.CurrentGameFileName + "\" id=" + SaveAndLoad.WorldGuid + " items " + items.Count + ": "
				+ string.Join(", ", items.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value).ToArray()));
			Log("PASS: invfix " + what);
		}

		[ConsoleCommand(name: "CIInvFixGuard", docs: "Dev, in game (host, a 'CI ...' world): inventory bug 11-09 - with Raft's world id cleared as on leaving, no save goes through (SaveGuard, SaveSoon); with the id back, saves work again")]
		public static void InvFixGuardCommand() { StartTest(InvFixGuardRoutine()); }

		static IEnumerator InvFixGuardRoutine()
		{
			Guid id = SaveAndLoad.WorldGuid;
			SaveAndLoad sl = ComponentManager<SaveAndLoad>.Value;
			if (!Raft_Network.IsHost || sl == null || id == Guid.Empty || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("invfix guard: needs the host in a 'CI ...' world with a world id"); yield break; }
			bool ok = true;
			SaveGuard.TestOff = false;
			sl.SaveGame(true);
			int refused = SaveGuard.Refused;
			Check(ref ok, SaveSoon.CanSave(), "SaveSoon may save in the world");
			SaveAndLoad.WorldGuid = Guid.Empty;
			try
			{
				Check(ref ok, SaveGuard.IdLost, "id cleared: seen as lost");
				Check(ref ok, !SaveSoon.CanSave(), "id cleared: SaveSoon does not save");
				sl.SaveGame(true);
				Check(ref ok, SaveGuard.Refused == refused + 1, "id cleared: Raft's SaveGame refused (" + (SaveGuard.Refused - refused) + ")");
			}
			finally { SaveAndLoad.WorldGuid = id; }
			Check(ref ok, !SaveGuard.IdLost && SaveSoon.CanSave(), "id back: saves allowed");
			sl.SaveGame(true);
			Check(ref ok, SaveGuard.Refused == refused + 1, "id back: Raft's SaveGame went through");
			yield return null;
			if (ok) Log("PASS: invfix guard"); else Fail("invfix guard");
		}
	}
}
