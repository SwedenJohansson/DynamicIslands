using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	// SC17 (AU65), the part one machine can do: with scrambled blueprints on, a blueprint of Raft's story island picked up in
	// the first frames after the island appears - before the 1.5 s look has given its pickups their partners - is the partner.
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIScFirstPickup", docs: "Dev, world (host, a test world 'CI ...' - it brings one of Raft's story islands): SC17/AU65 on one machine - scrambled blueprints on, the story island with the most movable blueprints brought and sailed to; the frame it appears, before the 1.5 s look, a movable blueprint is picked up through Raft's own Pickup.PickupItem: the player gets its partner, not Raft's own, and the island's other pickups have theirs too. The player-2 half (options arriving after the join) needs host + box")]
		public static void ScFirstPickupCommand() { StartTest(ScFirstPickupRoutine()); }

		static IEnumerator ScFirstPickupRoutine()
		{
			Network_Player me = RAPI.GetLocalPlayer();
			if (me == null || !LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("first pickup: host, in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			IDictionary originals = HarmonyLib.Traverse.Create(typeof(ScrambledBlueprints)).Field("originals").GetValue<IDictionary>();
			try
			{
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.Blueprints });
				List<string> movable = ScrambledBlueprints.Movable;
				string island = StoryTypes.Keys.OrderByDescending(k => movable.Count(b => ScrambledBlueprints.OnIslands[b].Contains(k))).First();
				var own = movable.Where(b => ScrambledBlueprints.OnIslands[b].Contains(island) && ScrambledBlueprints.Map(b) != b).ToList();
				Check(ref ok, own.Count > 0 && originals != null, "scrambled blueprints on; " + island + " holds " + own.Count + " that move (" + string.Join(", ", own.Select(b => b.Replace("Blueprint_", "") + ">" + ScrambledBlueprints.Map(b).Replace("Blueprint_", "")).ToArray()) + ")");
				if (own.Count == 0 || originals == null) yield break;
				ChunkManager chunks = ComponentManager<ChunkManager>.Value ?? UnityEngine.Object.FindObjectOfType<ChunkManager>();
				ChunkPoint point = chunks != null ? chunks.AddChunkPointForcibly(StoryTypes[island]) : null;
				Check(ref ok, point != null, "Raft brings " + island + (point != null ? " " + Flat(point.worldPosition - CustomIslandSpawner.RaftPosition.Value).magnitude.ToString("F0") + " m away" : ""));
				if (point == null) yield break;

				// (sailed towards it; every frame looked at, so the pickup comes the frame the island is there)
				Func<Landmark> find = () => WorldManager.AllLandmarks.FirstOrDefault(x => x != null && x.isSpawned && x.name.IndexOf(island, StringComparison.OrdinalIgnoreCase) >= 0);
				Func<Landmark, PickupItem> fresh = l => l == null ? null : l.GetComponentsInChildren<PickupItem>(true).FirstOrDefault(p => p != null && !originals.Contains(p)
					&& p.itemInstance != null && p.itemInstance.baseItem != null && own.Contains(p.itemInstance.baseItem.UniqueName));
				Landmark land = null;
				PickupItem pick = null;
				Vector3 p0 = point.worldPosition, shifted = Vector3.zero;
				Action<Vector3> onShift = s => shifted += s;
				WorldShiftManager.OnWorldShift += onShift;
				Func<Vector3> target = () => point.worldPosition != p0 ? point.worldPosition : p0 + shifted;
				float seen = -1f;
				// (the 1.5 s look held off while the island comes: which frame it falls on is chance, and when it comes first there
				// is no unswapped pickup left to take - SC17 run 11-26. The pickup hook is what this row is about.)
				HarmonyLib.Traverse tick = HarmonyLib.Traverse.Create(typeof(ScrambledBlueprints)).Field("nextTick");
				Action hold = () => tick.SetValue(Time.unscaledTime + 5f);
				try
				{
					for (int leg = 0; leg < 8 && land == null; leg++)
					{
						IEnumerator sail = SailTo(() => target() + (CustomIslandSpawner.RaftPosition.Value - target()).normalized * 200f, 60f);
						hold();
						while (land == null && sail.MoveNext()) { hold(); yield return sail.Current; hold(); land = find(); }
						for (float t = 0f; land == null && t < 20f; t += Time.deltaTime) { hold(); yield return null; hold(); land = find(); }
					}
					if (land != null)
					{
						seen = Time.time;
						// (the pickups may come a few frames after the landmark: still well inside the 1.5 s look)
						for (int f = 0; f < 30 && (pick = fresh(land)) == null; f++) { hold(); yield return null; hold(); }
					}
				}
				finally { WorldShiftManager.OnWorldShift -= onShift; if (pick == null) tick.SetValue(0f); }
				Check(ref ok, land != null, island + " appeared");
				if (land == null) yield break;
				if (pick == null) { Log("  (the 1.5 s look got to all its blueprints first: no unswapped pickup to take - run again)"); Check(ref ok, false, "a blueprint picked up before the look"); yield break; }

				string was = pick.itemInstance.baseItem.UniqueName, partner = ScrambledBlueprints.Map(was);
				int hadWas = me.Inventory.GetItemCount(was), hadPartner = me.Inventory.GetItemCount(partner);
				float after = Time.time - seen;
				me.PickupScript.PickupItem(pick, true, false);
				tick.SetValue(0f); // (the look runs again)
				yield return new WaitForSeconds(0.5f);
				int gotWas = me.Inventory.GetItemCount(was) - hadWas, gotPartner = me.Inventory.GetItemCount(partner) - hadPartner;
				Check(ref ok, gotPartner == 1 && gotWas == 0, "picked up " + Gf(after, "F2") + " s after the island appeared (Raft's own there: " + was.Replace("Blueprint_", "") + "): the player got " + partner.Replace("Blueprint_", "") + " x" + gotPartner + ", " + was.Replace("Blueprint_", "") + " x" + gotWas);
				// (every pickup of the island that held a blueprint that moves: given its partner at the same time)
				var left = land.GetComponentsInChildren<PickupItem>(true).Where(p => p != null && p != pick && (originals.Contains(p) || p.itemInstance != null && p.itemInstance.baseItem != null && own.Contains(p.itemInstance.baseItem.UniqueName))).ToList();
				int swapped = left.Count(p => originals.Contains(p));
				Check(ref ok, swapped == left.Count, "the island's other moving blueprint pickups were given their partners at the same time: " + swapped + "/" + left.Count);
				me.Inventory.RemoveItem(partner, gotPartner);
			}
			finally { WorldOptions.Set(optionsBefore); }
			if (ok) Log("PASS: first pickup (SC17 on one machine)"); else Fail("first pickup (SC17): see the lines above");
		}
	}
}
