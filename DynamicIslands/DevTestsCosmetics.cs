using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CICosmetics", docs: "Dev, in game (host): Cosmetic rewards (PF7). Checks the hats and trophies are registered at their fixed indexes, never craftable or taught by the upgrade table, hats wearable on the head with an Equipment copy, trophies placeable tinted blocks, the chest preset and the reward/loot text, then gives one of each and takes them back.")]
		public static void CosmeticsTestCommand(string[] args) { StartTest(CosmeticsRoutine()); }

		static IEnumerator CosmeticsRoutine()
		{
			bool ok = true;
			List<ExtraUpgrades.Upgrade> all = Cosmetics.All.ToList();
			Check(ref ok, all.Count == 7 && Cosmetics.Hats.Count() == 4 && Cosmetics.Trophies.Count() == 3, all.Count + " cosmetics: 4 hats, 3 trophies");
			for (int i = 0; i < all.Count; i++)
			{
				ExtraUpgrades.Upgrade u = all[i];
				Item_Base byIndex = ItemManager.GetItemByIndex(u.Index);
				Check(ref ok, u.Cosmetic && u.Index == Cosmetics.FirstIndex + i && u.Item != null && byIndex != null && byIndex.UniqueName == u.Name && ItemManager.GetItemByName(u.Name) == byIndex,
					u.Display + ": registered as " + u.Name + " at " + u.Index + " from " + u.BaseName + (u.Item == null ? " - MISSING" : ""));
				if (u.Item == null) continue;
				Check(ref ok, ExtraUpgrades.Find(u.Index) == u && ExtraUpgrades.Find(u.Name) == u && !ExtraUpgrades.Registered.Contains(u) && !ExtraUpgrades.All.Contains(u)
					&& !UpgradeTable.Teaches.Contains(u) && !ExtraUpgrades.Craftable(u) && !u.Item.settings_recipe.Learned, u.Display + ": not an upgrade, never craftable or taught");
				Check(ref ok, ContentCatalog.ItemExists(u.Name) && ContentCatalog.ItemLabel(u.Name) == u.Display && ContentCatalog.ItemSprite(u.Name) != null, u.Display + ": the editor's item picker shows it with a picture");
				if (u.Worn != null)
				{
					var eq = u.Item.settings_equipment;
					Check(ref ok, eq != null && eq.EquipType == EquipSlotType.Head && u.Base.settings_equipment.EquipType == EquipSlotType.Head, u.Display + ": worn on the head");
				}
				else
				{
					Block[] prefabs = u.Item.settings_buildable != null ? u.Item.settings_buildable.GetBlockPrefabs() : null;
					Check(ref ok, prefabs != null && prefabs.Length > 0 && prefabs.All(p => p != null && p.buildableItem == u.Item && p.GetComponent<UpgradeBlock>() != null), u.Display + ": a placeable block of its own (" + (prefabs != null ? prefabs.Length : 0) + " prefabs), tinted");
				}
			}
			var preset = ContentCatalog.LootPresets.FirstOrDefault(p => p.Key == "Cosmetics");
			string presetLoot = preset.Value != null ? ContentCatalog.PresetLoot(preset.Value) : "";
			Check(ref ok, presetLoot.Contains(Cosmetics.AdmiralHat) && presetLoot.Contains(Cosmetics.GoldenShark), "chest preset Cosmetics: " + presetLoot);
			var reward = ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, Cosmetics.CorsairHat + "*1;" + Cosmetics.JadeTiki + "*2" } });
			Check(ref ok, reward.Count == 2 && reward[0].Key == Cosmetics.CorsairHat && reward[1].Value == 2 && !StoryItems.IsStory(reward[0].Key), "a quest reward / loot text names them like Raft's items");

			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("cosmetics: run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			Equipment[] have = player.GetComponentsInChildren<Equipment>(true);
			foreach (ExtraUpgrades.Upgrade h in Cosmetics.Hats.Where(x => x.Item != null))
				Check(ref ok, have.Count(e => e.equipableItem == h.Item) == 1, h.Display + ": the player has its Equipment (a copy of " + h.BaseName + "'s)");

			var before = all.Where(u => u.Item != null).ToDictionary(u => u, u => player.Inventory.GetItemCount(u.Name));
			try
			{
				List<string> given = TriggerZone.Give(before.Keys.Select(u => new KeyValuePair<string, int>(u.Name, 1)));
				yield return null;
				var missing = before.Keys.Where(u => player.Inventory.GetItemCount(u.Name) != before[u] + 1).Select(u => u.Display).ToList();
				Check(ref ok, missing.Count == 0, "given one of each (" + string.Join(", ", given.ToArray()) + ")" + (missing.Count > 0 ? ", not in the inventory: " + string.Join(", ", missing.ToArray()) : ""));
			}
			finally
			{
				foreach (var kv in before)
				{
					int extra = player.Inventory.GetItemCount(kv.Key.Name) - kv.Value;
					if (extra > 0) player.Inventory.RemoveItem(kv.Key.Name, extra);
				}
			}
			yield return null;
			Check(ref ok, before.All(kv => player.Inventory.GetItemCount(kv.Key.Name) == kv.Value), "taken back again");
			if (ok) Log("PASS: cosmetics"); else Fail("cosmetics");
		}
	}
}
