using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>RK2: loose items dropped on a custom island stay on it while it unloads and while it loads after a save (ItemHold.cs).</summary>
	public static partial class DevTests
	{
		const string DropHoldIsland = "cidrophold";
		const string DropHoldItem = "Plank";
		static string DropHoldFile { get { return Path.Combine(DynamicIslands.assetpath, "cidrophold.txt"); } }

		[ConsoleCommand(name: "CIDropHold", docs: "Dev, world (host, a test world 'CI ...'): RK2 - an item dropped on a custom island stays there. CIDropHold sail: the island unloads and loads again. CIDropHold save: sets up an island with a dropped item for a reload (then CISave, CIMainMenu, CILoadWorld, CIDropHold load). CIDropHold load: the item is still on the island after the reload. CIDropHold clean: removes the island")]
		public static void DropHoldCommand(string[] args)
		{
			string mode = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "sail";
			StartTest(DropHoldRoutine(mode));
		}

		static IEnumerator DropHoldRoutine(string mode)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("drop hold: run in a world, as the host"); yield break; }
			bool ok = true;
			if (mode == "clean")
			{
				ScRemove(IslandWorldState.Islands.Where(x => x.Name == DropHoldIsland).ToList(), DropHoldIsland);
				try { File.Delete(DropHoldFile); } catch { }
				Log("PASS: drop hold clean");
				yield break;
			}
			if (mode == "load") { yield return DropHoldLoad(); yield break; }

			yield return EnsureAlive();
			var made = new List<IslandWorldState.Entry>();
			ScRemove(IslandWorldState.Islands.Where(x => x.Name == DropHoldIsland).ToList());
			try { ScIsland(DropHoldIsland, "Drop Rock").Save(IslandSpawner.PathFor(DropHoldIsland)); } catch (Exception ex) { Fail("drop hold: no sample island (" + ex.Message + ")"); yield break; }
			Vector3? spot = ScSpot(DropHoldIsland, 320f);
			if (!spot.HasValue) { ScRemove(made, DropHoldIsland); Fail("drop hold: no open sea near the raft"); yield break; }
			yield return ScBring(DropHoldIsland, spot.Value, made);
			IslandWorldState.Entry e = made[0];
			if (e.Root == null) { ScRemove(made, DropHoldIsland); Fail("drop hold: the island did not come"); yield break; }
			yield return new WaitForSeconds(1f);

			// Dropped on dry ground of the island, as a player drops it
			Vector3? ground = DropHoldGround(e);
			if (!ground.HasValue) { ScRemove(made, DropHoldIsland); Fail("drop hold: no dry ground on the island"); yield break; }
			Item_Base plank = ItemManager.GetItemByName(DropHoldItem);
			var before = new HashSet<int>(UnityEngine.Object.FindObjectsOfType<PickupItem>().Select(p => p.GetInstanceID()));
			Helper.DropItem(new ItemInstance(plank, 1, plank.MaxUses), ground.Value + Vector3.up * 1.5f, Vector3.zero, false);
			yield return new WaitForSeconds(4f);
			PickupItem item = UnityEngine.Object.FindObjectsOfType<PickupItem>().FirstOrDefault(p => p != null && p.isDropped && !before.Contains(p.GetInstanceID()));
			if (item == null) { ScRemove(made, DropHoldIsland); Fail("drop hold: the dropped item is not there"); yield break; }
			Vector3 rest = item.transform.position - e.Position;
			Check(ref ok, Mathf.Abs(item.transform.position.y - ground.Value.y) < 1.5f, "the item lies on the island's ground (" + (item.transform.position.y - ground.Value.y).ToString("F2") + " m above the spot)");

			if (mode == "save")
			{
				File.WriteAllText(DropHoldFile, rest.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + rest.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + rest.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				Log("rest offset from the island " + rest + " written; now CISave, CIMainMenu, CILoadWorld, CIDropHold load");
				if (ok) Log("PASS: drop hold set up"); else Fail("drop hold set up");
				yield break;
			}

			// 1. The island unloads (as with the raft far away): the item waits where it lay
			float unloadWas = CustomIslandSpawner.UnloadDistance;
			try
			{
				CustomIslandSpawner.UnloadDistance = 1f;
				for (float t = 0; e.Root != null && t < 15f; t += 0.25f) yield return new WaitForSeconds(0.25f);
				Check(ref ok, e.Root == null, "the island unloads");
				Check(ref ok, ItemHold.IsHeld(item), "the dropped item is held while its island is away");
				yield return new WaitForSeconds(5f);
				Vector3 away = item != null ? item.transform.position - e.Position : Vector3.zero;
				Check(ref ok, item != null && (away - rest).magnitude < 0.2f, "it stays where it lay while the island is away (moved " + (away - rest).magnitude.ToString("F2") + " m)");
			}
			finally { CustomIslandSpawner.UnloadDistance = unloadWas; }

			// 2. Loads again: the item is let go onto its ground and lies where it was
			for (float t = 0; e.Root == null && t < 30f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			Check(ref ok, e.Root != null, "the island loads again");
			for (float t = 0; ItemHold.IsHeld(item) && t < 10f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			Check(ref ok, !ItemHold.IsHeld(item), "the item is let go once the island stands");
			yield return new WaitForSeconds(4f);
			Vector3 back = item != null ? item.transform.position - e.Position : Vector3.zero;
			Check(ref ok, item != null && (back - rest).magnitude < 0.6f, "the item lies where it was dropped after sailing back (" + (back - rest).magnitude.ToString("F2") + " m off)");
			Check(ref ok, item != null && item.GetComponent<Rigidbody>() != null && !item.GetComponent<Rigidbody>().isKinematic, "the item is a free item again");

			if (item != null) PickupObjectManager.RemovePickupItemNetwork(item.GetComponent<PickupItem_Networked>());
			ScRemove(made, DropHoldIsland);
			if (ok) Log("PASS: drop hold sail"); else Fail("drop hold sail");
		}

		static IEnumerator DropHoldLoad()
		{
			bool ok = true;
			Vector3 rest;
			try
			{
				string[] p = File.ReadAllText(DropHoldFile).Split(' ');
				rest = new Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
			}
			catch (Exception ex) { Fail("drop hold load: run CIDropHold save first (" + ex.Message + ")"); yield break; }
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Name == DropHoldIsland);
			if (e == null) { Fail("drop hold load: the island is not in the loaded world"); yield break; }
			Log("dropped items read from the world file: " + ItemHold.SavedRead + " (placed " + ItemHold.SavedPlaced + ", waiting " + ItemHold.SavedWaiting + ")");
			Check(ref ok, ItemHold.SavedRead > 0, "the save kept the dropped item (Raft's own save keeps none)");
			for (float t = 0; e.Root == null && t < 60f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			Check(ref ok, e.Root != null, "the island loads");
			for (float t = 0; ItemHold.SavedWaiting > 0 && t < 15f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			Check(ref ok, ItemHold.SavedPlaced > 0, "it is dropped again once the island stands");
			yield return new WaitForSeconds(4f);
			PickupItem item = UnityEngine.Object.FindObjectsOfType<PickupItem>()
				.Where(x => x != null && x.isDropped && x.itemInstance != null && x.itemInstance.UniqueName == DropHoldItem)
				.OrderBy(x => (x.transform.position - e.Position - rest).sqrMagnitude).FirstOrDefault();
			float off = item != null ? (item.transform.position - e.Position - rest).magnitude : float.PositiveInfinity;
			Check(ref ok, item != null && off < 0.6f, "the dropped item lies on the island after loading the save (" + off.ToString("F2") + " m off" + (item != null ? ", at height " + item.transform.position.y.ToString("F1") : "") + ")");
			if (item != null) PickupObjectManager.RemovePickupItemNetwork(item.GetComponent<PickupItem_Networked>());
			ScRemove(IslandWorldState.Islands.Where(x => x.Name == DropHoldIsland).ToList(), DropHoldIsland);
			try { File.Delete(DropHoldFile); } catch { }
			if (ok) Log("PASS: drop hold load"); else Fail("drop hold load");
		}

		/// <summary>A spot of the island's own terrain above the sea near its land centre.</summary>
		static Vector3? DropHoldGround(IslandWorldState.Entry e)
		{
			for (int ring = 0; ring < 6; ring++)
				for (int a = 0; a < 8; a++)
				{
					Vector3 from = e.Position + new Vector3(Mathf.Cos(a * Mathf.PI / 4f), 0f, Mathf.Sin(a * Mathf.PI / 4f)) * (ring * 5f) + Vector3.up * 150f;
					RaycastHit hit;
					if (!Physics.Raycast(from, Vector3.down, out hit, 300f, ~0, QueryTriggerInteraction.Ignore)) continue;
					if (!(hit.collider is TerrainCollider) || !hit.collider.transform.IsChildOf(e.Root.transform)) continue;
					if (hit.point.y < 1.5f || Vector3.Angle(hit.normal, Vector3.up) > 20f) continue;
					return hit.point;
				}
			return null;
		}
	}
}
