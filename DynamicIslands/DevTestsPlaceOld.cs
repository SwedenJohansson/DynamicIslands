using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;
using DynamicIslands.Editor;

namespace DynamicIslands
{
	/// <summary>CF placement in an old world (player report 11-12: on 3.4 the upgrade research table showed no ghost in an
	/// old main world until a fresh world had been made and the old one loaded again). Run in a Raft just started, in a
	/// world made on 3.3 or older, with no new world made first; and again after CIMainMenu and CILoadWorld.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIPlaceOld", docs: "Dev, world (host; a fresh Raft start, an old world loaded, no new world first), CF: the upgrades' placement check line, every buildable upgrade and cosmetic reward still Raft's item by index and name, in the mod's lookup, with all its block prefabs; then each held and placed by Raft's own aim, ghost and click (as CIPlaceReal): a surface at the cursor, the ghost shown, the placed block the right item; then taken down")]
		public static void PlaceOldCommand() { StartTest(PlaceOldRoutine()); }

		static IEnumerator PlaceOldRoutine()
		{
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !Raft_Network.IsHost) { Fail("place old: host, in a world"); yield break; }
			// (by name: the build before the fix has no placement check)
			string line = null;
			try { line = Traverse.Create(typeof(ExtraUpgrades)).Field("LastPlacementCheck").GetValue<string>(); } catch { }
			Log("place old: " + (line ?? "no placement check line in this build"));
			if (line != null) Check(ref ok, line.Contains(", 0 wrong"), "the world load's placement check found nothing it could not mend (old world)");
			List<ExtraUpgrades.Upgrade> ups = ExtraUpgrades.Registered.Concat(ExtraUpgrades.CosmeticItems)
				.Where(u => u.Base != null && u.Base.settings_buildable != null && u.Base.settings_buildable.HasBuildablePrefabs).ToList();
			Check(ref ok, ups.Count > 0, ups.Count + " buildable upgrades and cosmetics made (old world)");
			var names = new List<string>();
			foreach (ExtraUpgrades.Upgrade u in ups)
			{
				Item_Base it = u.Item;
				Block[] prefabs = it != null && it.settings_buildable != null ? it.settings_buildable.GetBlockPrefabs() : null;
				bool fine = it != null && ItemManager.GetItemByIndex(u.Index) == it && ItemManager.GetItemByName(u.Name) == it && ExtraUpgrades.FindMade(it) == u
					&& prefabs != null && prefabs.Length > 0 && prefabs.All(p => p != null && p.buildableItem == it);
				Check(ref ok, fine, u.Name + ": Raft's item by index and name, in the lookup, " + (prefabs == null ? "no" : prefabs.Count(p => p != null) + "/" + prefabs.Length) + " block prefabs");
				names.Add(u.Name);
			}
			// (the old world's raft may be small: grown to 6 x 6 like CIPlaceGhosts, taken off after)
			BlockCreator bc = player.BlockCreator;
			var grown = new List<Block>();
			Item_Base fItem = ItemManager.GetItemByName("Block_Foundation");
			List<Block> fs = UnityEngine.Object.FindObjectsOfType<Block>().Where(b => b != null && b.buildableItem != null && b.buildableItem.UniqueName == "Block_Foundation").ToList();
			if (fItem != null && fs.Count > 0)
			{
				Vector3 o = fs[0].transform.localPosition;
				const float g = 1.5f;
				for (int x = -2; x <= 3; x++)
					for (int z = -2; z <= 3; z++)
					{
						Vector3 p = o + new Vector3(x * g, 0f, z * g);
						if (fs.Any(b => (b.transform.localPosition - p).sqrMagnitude < 0.04f)) continue;
						try { Block made = bc.CreateBlockCheat(fItem, p, fs[0].transform.localEulerAngles, DPS.Default, -1); if (made != null) grown.Add(made); }
						catch (Exception e) { Log("place old: a foundation: " + e.Message); break; }
					}
				yield return new WaitForSecondsRealtime(0.5f);
			}
			var placed = new List<string>();
			bool[] placeOk = { true };
			yield return PlaceRealCore(names.ToArray(), "place old", placeOk, placed);
			// an item with no green spot is tried again as Raft's own base item: when that finds none either, this old raft has
			// no room for the kind (a net needs the raft's edge, an engine a free foundation) - not a fault of the mod's item
			List<string> missed = names.Except(placed).ToList();
			if (missed.Count > 0)
			{
				var baseOf = ups.Where(u => missed.Contains(u.Name) && u.Base != null).ToDictionary(u => u.Name, u => u.Base.UniqueName);
				var basePlaced = new List<string>();
				yield return PlaceRealCore(baseOf.Values.Distinct().ToArray(), "place old (Raft's own, as a control)", new[] { true }, basePlaced);
				placeOk[0] = true;
				foreach (string n in missed)
				{
					string b; bool noRoom = baseOf.TryGetValue(n, out b) && !basePlaced.Contains(b);
					if (noRoom) Log("  " + n + ": no room on this raft - Raft's own " + b + " found no green spot either");
					else { placeOk[0] = false; Log("FAIL: " + n + " not placed, while Raft's own " + (b ?? "?") + " was"); }
				}
			}
			foreach (Block b in grown) if (b != null) BlockCreator.RemoveBlockNetwork(b, null, true);
			if (!placeOk[0]) ok = false;
			Log("place old: placed " + placed.Count + "/" + names.Count + (placed.Count < names.Count ? "; not: " + string.Join(", ", names.Except(placed).ToArray()) : ""));
			if (ok) Log("PASS: place old"); else Fail("place old");
		}
	}
}
