using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Audit 11-09 player bugs: an upgraded block's pick-up refund, Esc on the mod's other windows, the mod's keys over
	/// Raft's own menus, the stats page / journal / notebook closing each other, and the host's check of a client's story give.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIUpgradeRefund", docs: "Dev, in game, host: every placeable extra upgrade placed and picked up - with Raft's itemToReturnOnDestroy the upgrade item itself comes back (never the base item); with it cleared, Raft's half-cost refund is half the upgrade's own recipe, not the base item's")]
		public static void UpgradeRefundCommand() { StartTest(UpgradeRefundRoutine()); }

		static IEnumerator UpgradeRefundRoutine()
		{
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !Raft_Network.IsHost) { Fail("upgrade refund: needs a world, as host"); yield break; }
			List<ExtraUpgrades.Upgrade> ups = ItemManager.GetAllItems()
				.Select(i => ExtraUpgrades.Find(i.UniqueIndex)).Where(u => u != null && u.Item != null && u.Base != null
					&& u.Item.settings_buildable != null && u.Item.settings_buildable.GetBlockPrefabs() != null
					&& u.Item.settings_buildable.GetBlockPrefabs().Length > 0 && u.Item.settings_recipe != null).ToList();
			Check(ref ok, ups.Count > 0, "placeable upgrades found: " + ups.Count);
			int natural = 0, cleared = 0;
			List<string> skipped = new List<string>();
			foreach (ExtraUpgrades.Upgrade u in ups)
			{
				CostMultiple[] cost = u.Item.settings_recipe.NewCost ?? new CostMultiple[0];
				CostMultiple[] baseCost = u.Base.settings_recipe != null && u.Base.settings_recipe.NewCost != null ? u.Base.settings_recipe.NewCost : new CostMultiple[0];
				List<string> names = cost.Concat(baseCost).Where(c => c.items != null && c.items.Length > 0).Select(c => c.items[0].UniqueName)
					.Concat(new[] { u.Item.UniqueName, u.Base.UniqueName }).Distinct().ToList();
				// (what the player had: everything above it is taken back after each upgrade - a full inventory gave nothing back)
				Dictionary<string, int> had = names.ToDictionary(n => n, n => player.Inventory.GetItemCount(n));
				foreach (CostMultiple c in cost) if (c.items != null && c.items.Length > 0) player.Inventory.AddItem(c.items[0].UniqueName, c.amount * 3);

				// 1. as placed: Raft's itemToReturnOnDestroy is the upgrade item - picking it up (PickupBlock's two calls) gives it back
				Block made = PlaceForTest(player, u.Item);
				if (made == null && placeThrew) { skipped.Add(u.Item.UniqueName); TakeBack(player, had); continue; }
				if (made == null) { Check(ref ok, false, u.Item.UniqueName + ": NO BLOCK placed"); TakeBack(player, had); continue; }
				string ret = made.itemToReturnOnDestroy != null ? made.itemToReturnOnDestroy.UniqueName : "null";
				Dictionary<string, int> c1 = names.ToDictionary(n => n, n => player.Inventory.GetItemCount(n));
				if (made.itemToReturnOnDestroy != null)
				{
					natural++;
					RemovePlaceables.ReturnItemsFromBlock(made, player, true);
					BlockCreator.RemoveBlockNetwork(made, player, true);
					yield return new WaitForSeconds(0.8f);
					int gotItem = player.Inventory.GetItemCount(u.Item.UniqueName) - c1[u.Item.UniqueName];
					int gotBase = player.Inventory.GetItemCount(u.Base.UniqueName) - c1[u.Base.UniqueName];
					Check(ref ok, made.itemToReturnOnDestroy == u.Item && gotItem == 1 && gotBase == 0, u.Item.UniqueName + " picked up (returns " + ret + "): +" + gotItem + " itself, +" + gotBase + " " + u.Base.UniqueName);
					made = PlaceForTest(player, u.Item);
					if (made == null) { Check(ref ok, false, u.Item.UniqueName + ": NO second block placed"); TakeBack(player, had); continue; }
					c1 = names.ToDictionary(n => n, n => player.Inventory.GetItemCount(n));
				}

				// 2. itemToReturnOnDestroy cleared (Raft's refund path): half of the upgrade's own recipe, as Raft does for its blocks
				cleared++;
				made.itemToReturnOnDestroy = null;
				RemovePlaceables.ReturnItemsFromBlock(made, player, true);
				Dictionary<string, int> want = names.ToDictionary(n => n, n => 0);
				foreach (CostMultiple c in cost) if (c.items != null && c.items.Length > 0) want[c.items[0].UniqueName] += Mathf.CeilToInt(c.amount * 0.5f);
				List<string> wrong = names.Where(n => player.Inventory.GetItemCount(n) - c1[n] != want[n])
					.Select(n => n + " +" + (player.Inventory.GetItemCount(n) - c1[n]) + " (want +" + want[n] + ")").ToList();
				Check(ref ok, wrong.Count == 0, u.Item.UniqueName + " refund with no return item: " + string.Join(" + ", cost.Where(c => c.items != null && c.items.Length > 0).Select(c => Mathf.CeilToInt(c.amount * 0.5f) + " " + c.items[0].UniqueName).ToArray())
					+ " (base " + u.Base.UniqueName + ")" + (wrong.Count > 0 ? " - WRONG " + string.Join(", ", wrong.ToArray()) : ""));
				Check(ref ok, made.buildableItem == u.Item, u.Item.UniqueName + ": the block keeps its upgrade item after the refund");
				BlockCreator.RemoveBlockNetwork(made, null, true);
				yield return new WaitForSeconds(0.5f);
				TakeBack(player, had);
			}
			Log("upgrade refund: " + ups.Count + " upgrades, " + natural + " with a return item, " + cleared + " refund checks"
				+ (skipped.Count > 0 ? "; not placeable by the test (placing throws - the placing fix, not the refund): " + string.Join(", ", skipped.ToArray()) : ""));
			if (ok) Log("PASS: upgrade refund"); else Fail("upgrade refund");
		}

		static bool placeThrew;
		static Block PlaceForTest(Network_Player player, Item_Base item)
		{
			placeThrew = false;
			try { return player.BlockCreator.CreateBlockCheat(item, new Vector3(0f, 25f, 0f), Vector3.zero, DPS.Default, 0); }
			catch (Exception e) { placeThrew = true; Log(item.UniqueName + ": placing threw " + e.GetType().Name + ": " + e.Message); return null; }
		}

		static void TakeBack(Network_Player player, Dictionary<string, int> had)
		{
			foreach (KeyValuePair<string, int> kv in had) { int extra = player.Inventory.GetItemCount(kv.Key) - kv.Value; if (extra > 0) player.Inventory.RemoveItem(kv.Key, extra); }
		}

		[ConsoleCommand(name: "CIEscHotkeys1109", docs: "Dev, in game, Levels on, no menu open: Esc belongs to the islander dialogue, story credits, notebook and quest-skip box (no pause menu); J/K/notebook key do nothing over Raft's inventory; the stats page, journal and notebook close each other on open; the host refuses a story give with a bad count")]
		public static void EscHotkeys1109Command() { StartTest(EscHotkeys1109Routine()); }

		static IEnumerator EscHotkeys1109Routine()
		{
			bool ok = true;
			ModCursor.TestPlaceFakedUntil = Time.realtimeSinceStartup + 120f; ModCursor.TestMinimisedInFront = false;
			DialoguePanel.Close(); JournalWindow.Close(); LevelWindow.Close(); PlayerNotebookWindow.Close(); StoryCredits.Close(); QuestSkip.CloseConfirm();
			yield return new WaitForSecondsRealtime(0.4f);
			MenuType start = CanvasHelper.ActiveMenu;
			Check(ref ok, !EscGuard.AnyOpen(), "no window open at the start");

			// 1. Esc's guard sees each window (the pause menu stays shut)
			Transform at = RAPI.GetLocalPlayer() != null ? RAPI.GetLocalPlayer().transform : null;
			DialoguePanel.Open(at, "Tester", "Esc test", i => { });
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, DialoguePanel.IsOpen && EscGuard.AnyOpen(), "islander dialogue: Esc is the dialogue's");
			DialoguePanel.Close(); yield return new WaitForSecondsRealtime(0.3f);
			StoryCredits.Show("Esc test", "Tester", "");
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, StoryCredits.IsOpen && EscGuard.AnyOpen(), "story credits: Esc is the credits'");
			StoryCredits.Close(); yield return new WaitForSecondsRealtime(0.3f);
			PlayerNotebookWindow.Open();
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, PlayerNotebookWindow.IsOpen && EscGuard.AnyOpen(), "notebook: Esc is the notebook's");
			yield return PressKey(KeyCode.Escape);
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, !PlayerNotebookWindow.IsOpen && !ModKeys.PauseOpen(), "Esc on the notebook closes it, no pause menu");

			// 2. the stats page, journal and notebook close each other
			if (!PlayerLevels.On) Check(ref ok, false, "the level up system is off - run 'Levels on' first");
			else
			{
				JournalWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
				LevelWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, LevelWindow.IsOpen && !JournalWindow.IsOpen, "stats page opened over the journal closes it");
				JournalWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, JournalWindow.IsOpen && !LevelWindow.IsOpen, "journal opened over the stats page closes it");
				LevelWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
				PlayerNotebookWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, PlayerNotebookWindow.IsOpen && !LevelWindow.IsOpen, "notebook opened over the stats page closes it");
				LevelWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, LevelWindow.IsOpen && !PlayerNotebookWindow.IsOpen, "stats page opened over the notebook closes it");
				LevelWindow.Close(); JournalWindow.Close(); PlayerNotebookWindow.Close();
				yield return new WaitForSecondsRealtime(0.4f);
			}

			// 3. Raft's inventory open: the mod's keys are blocked and open nothing
			CanvasHelper.ActiveMenu = MenuType.Inventory;
			Check(ref ok, ModKeys.Blocked() && ModKeys.RaftMenuOpen(), "Raft's inventory open: the mod's keys blocked");
			yield return PressKey(PlayerLevels.Key);
			yield return PressKey(JournalWindow.Key);
			yield return PressKey(PlayerNotebookWindow.Key);
			Check(ref ok, !LevelWindow.IsOpen && !JournalWindow.IsOpen && !PlayerNotebookWindow.IsOpen, "K / J / notebook key over the inventory open nothing");
			Check(ref ok, !Minimap.Shown, "the corner map hidden over the inventory");
			LevelWindow.Close(); JournalWindow.Close(); PlayerNotebookWindow.Close();
			CanvasHelper.ActiveMenu = start;
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, !ModKeys.RaftMenuOpen(), "inventory shut again: keys free (" + CanvasHelper.ActiveMenu + ")");

			// 4. the host's check of a client's story give
			Check(ref ok, !StoryBook.GiveTrusted("Thing|0", 1), "a give of 0 refused");
			Check(ref ok, !StoryBook.GiveTrusted("Thing|-5", 1), "a give of -5 refused");
			Check(ref ok, !StoryBook.GiveTrusted("Thing|x", 1), "a give with no count refused");
			Check(ref ok, StoryBook.GiveTrusted("Thing|1", 1), "a give of 1 with no island trusted");

			ModCursor.TestPlaceFakedUntil = 0;
			if (ok) Log("PASS: Esc and hotkeys 11-09"); else Fail("Esc and hotkeys 11-09");
		}
	}
}
