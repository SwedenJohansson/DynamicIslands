using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>Tests of the world setting Head start raft (HeadStart): the World settings button, and the raft a new world starts on.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIHeadStartToggle", docs: "Dev, main menu: the World settings window's Head start raft button - it goes off -> level 1 -> ... -> 10 -> off, the label (level and size), the level's description and the summary say so; the choice is put back after")]
		public static void HeadStartToggleCommand() { StartTest(HeadStartToggleRoutine()); }

		static IEnumerator HeadStartToggleRoutine()
		{
			if (LoadSceneManager.IsGameSceneLoaded) { Fail("head start button: main menu only"); yield break; }
			Button b = WorldSettingsWindow.HeadStartToggle;
			if (b == null) { Fail("head start button: no World settings window (open the New Game box once)"); yield break; }
			int before = HeadStart.Chosen;
			bool ok = true;
			try
			{
				HeadStart.Chosen = 0;
				WorldSettingsWindow.Show();
				var seen = new List<string> { UIKit.LabelOf(b).text };
				bool round = seen[0].EndsWith("off"), detailed = WorldSettingsWindow.HeadStartDetail.text.StartsWith("Off");
				for (int i = 1; i <= HeadStart.Max + 1; i++)
				{
					b.onClick.Invoke();
					string t = UIKit.LabelOf(b).text;
					seen.Add(t + " = " + HeadStart.Chosen);
					int want = i % (HeadStart.Max + 1);
					round &= HeadStart.Chosen == want && (want == 0 ? t.EndsWith("off") : t.EndsWith("LEVEL " + want + "  (" + HeadStart.Width(want) + " x " + HeadStart.Length(want) + ")"));
					detailed &= WorldSettingsWindow.HeadStartDetail.text == HeadStart.Details(want);
				}
				Check(ref ok, round, "the middle button goes round 0-10: " + string.Join(" / ", seen.ToArray()));
				Check(ref ok, detailed, "each level's description shows under it");
				var sizes = Enumerable.Range(1, HeadStart.Max).Select(l => HeadStart.Describe(l)).ToArray();
				Check(ref ok, HeadStart.Describe(1) == "level 1 (7 x 10)" && HeadStart.Width(10) == 11 && HeadStart.Length(10) == 18 && HeadStart.Width(2) == 7 && HeadStart.Length(2) == 14, "the levels' sizes: " + string.Join(", ", sizes));
				Log("descriptions: " + string.Join(" | ", Enumerable.Range(0, HeadStart.Max + 1).Select(l => l + ": " + HeadStart.Details(l)).ToArray()));
			}
			finally { HeadStart.Chosen = before; WorldSettingsWindow.Show(); }
			yield return null;
			if (ok) Log("PASS: head start button"); else Fail("head start button");
		}

		[ConsoleCommand(name: "CIHeadStartCheck", docs: "Dev, world (host or joiner): the head start raft of a world made with Head start raft level N (1-10) is there once: CIHeadStartCheck <level> [client] - at least W x L foundations, each placeable of the level exactly once (a bed and research table on every level, the nets and storages per level), the storages hold all the supplies (none left out), the stations stand on the floor inside the bare edge and off the centre aisle, the nets in the water, nothing overlapping, the receiver (level 3 up) has its 3 antennas and a battery; the host's world file says built")]
		public static void HeadStartCheckCommand(string[] args) { StartTest(HeadStartCheckRoutine(args)); }

		static IEnumerator HeadStartCheckRoutine(string[] args)
		{
			int level = 0;
			if (args == null || args.Length == 0 || !int.TryParse(args[0], out level) || level < 1 || level > HeadStart.Max) { Fail("head start check: CIHeadStartCheck <1-" + HeadStart.Max + "> [client]"); yield break; }
			bool client = args.Length > 1 && args[1] == "client";
			// (the host builds a moment after the world is there: up to 30 s for it)
			float until = Time.realtimeSinceStartup + 30f;
			while (!client && !HeadStart.Done && Time.realtimeSinceStartup < until) yield return new WaitForSecondsRealtime(0.5f);
			yield return new WaitForSecondsRealtime(1f);
			bool ok = true;
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raft == null) { Fail("head start check: no raft"); yield break; }
			if (!client)
			{
				Check(ref ok, HeadStart.Level == level && HeadStart.Done && !HeadStart.LastBuild.StartsWith("skipped") && !HeadStart.LastBuild.Contains("failed") && !HeadStart.LastBuild.Contains("left out"), "the world's head start: " + HeadStart.Describe(HeadStart.Level) + (HeadStart.Done ? ", built" : ", not built") + " - " + HeadStart.LastBuild);
				Check(ref ok, HeadStart.WriteLines().SequenceEqual(new[] { "@headstart=" + level, "@headstartdone=1" }), "the world file's lines: " + string.Join(" ", HeadStart.WriteLines().ToArray()));
			}
			List<Block> blocks = raft.GetComponentsInChildren<Block>().Where(b => b != null && b.buildableItem != null).ToList();
			Func<string, int> count = n => blocks.Count(b => b.buildableItem.UniqueName == n);
			int w = HeadStart.Width(level), l = HeadStart.Length(level);
			int floors = HeadStart.Foundations(raft).Count;
			Check(ref ok, floors >= w * l && floors <= w * l + 12, floors + " foundations for " + w + " x " + l);
			// (the counts by hand for what must be there whatever the layout: a bed and research table on every level, the storages and nets)
			int[] storagesWant = { 0, 1, 3, 5, 6, 8, 10, 12, 14, 16, 19 }, netsWant = { 0, 0, 10, 12, 16, 20, 26, 32, 38, 44, 51 };
			var want = HeadStart.PieceCounts(level);
			Check(ref ok, want.ContainsKey("Placeable_Bed_Basic") && want.ContainsKey("Placeable_ResearchTable") && want.Where(kv => kv.Key.StartsWith("Placeable_Storage")).Sum(kv => kv.Value) == storagesWant[level]
				&& want.Where(kv => kv.Key.Contains("CollectionNet")).Sum(kv => kv.Value) == netsWant[level], "the level's plan: a bed, a research table, " + storagesWant[level] + " storages, " + netsWant[level] + " nets");
			Check(ref ok, want.All(kv => count(kv.Key) == kv.Value), "each placeable once (no second copy): " + string.Join(", ", want.Select(kv => kv.Key.Replace("Placeable_", "") + " " + count(kv.Key) + "/" + kv.Value).ToArray()));
			// (the storages' supplies: the first holds the planks)
			List<Storage_Small> storages = blocks.Where(b => b.buildableItem.UniqueName.StartsWith("Placeable_Storage")).Select(b => b.GetComponentInChildren<Storage_Small>(true)).Where(s => s != null).ToList();
			int planks = 0, total = 0, batteries = 0, flippers = 0;
			foreach (Storage_Small s in storages)
			{
				Inventory inv = Traverse.Create(s).Field("inventoryReference").GetValue<Inventory>();
				if (inv == null) continue;
				planks += inv.GetItemCount("Plank"); batteries += inv.GetItemCount("Battery"); flippers += inv.GetItemCount("Flipper");
				total += inv.allSlots.Where(x => x != null && !x.IsEmpty).Sum(x => x.itemInstance.Amount);
			}
			int wantTotal = HeadStart.SupplyTotal(level), wantPlanks = HeadStart.SupplyOf(level, "Plank"), wantBatteries = HeadStart.SupplyOf(level, "Battery"), wantFlippers = HeadStart.SupplyOf(level, "Flipper");
			Check(ref ok, planks == wantPlanks && total == wantTotal && batteries == wantBatteries && flippers == wantFlippers, "the storages hold " + total + " items (want " + wantTotal + "): " + planks + "/" + wantPlanks + " planks, " + batteries + "/" + wantBatteries + " batteries, " + flippers + "/" + wantFlippers + " flippers");
			// (where things are, in the raft's space: the stations inside the bare edge ring and off the aisle, the nets
			// past the edge in the water, no two pieces in one cell)
			Func<Transform, Vector3> local = t => raft.transform.InverseTransformPoint(t.position);
			List<Vector3> floorAt = HeadStart.Foundations(raft).Select(f => local(f.transform)).ToList();
			float minX = floorAt.Min(p => p.x), maxX = floorAt.Max(p => p.x), minZ = floorAt.Min(p => p.z), maxZ = floorAt.Max(p => p.z), aisle = (minX + maxX) / 2f, cell = HeadStart.Cell;
			var pieces = blocks.Where(x => x.buildableItem.UniqueName.StartsWith("Placeable_")).ToList();
			var wrong = new List<string>();
			foreach (Block p in pieces)
			{
				string n = p.buildableItem.UniqueName.Replace("Placeable_", "");
				Vector3 at = local(p.transform);
				bool inside = at.x > minX + 0.5f * cell && at.x < maxX - 0.5f * cell && at.z > minZ + 0.5f * cell && at.z < maxZ - 0.5f * cell;
				bool outside = at.x < minX - 0.5f * cell || at.x > maxX + 0.5f * cell || at.z < minZ - 0.5f * cell || at.z > maxZ + 0.5f * cell;
				if (n.Contains("CollectionNet")) { if (!outside) wrong.Add(n + " not in the water"); }
				else if (n == "Reciever_Antenna") { if (outside) wrong.Add(n + " off the raft"); }
				else if (!inside) wrong.Add(n + " on the edge");
				else if (n != "Sail" && Mathf.Abs(at.x - aisle) < 0.5f * cell) wrong.Add(n + " in the aisle");
			}
			for (int i = 0; i < pieces.Count; i++)
				for (int j = i + 1; j < pieces.Count; j++)
				{
					Vector3 d = local(pieces[i].transform) - local(pieces[j].transform);
					if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)) < 0.95f * cell) wrong.Add(pieces[i].buildableItem.UniqueName.Replace("Placeable_", "") + " on " + pieces[j].buildableItem.UniqueName.Replace("Placeable_", ""));
				}
			Check(ref ok, wrong.Count == 0, "the layout: stations in the middle, nets in the water, nothing overlapping" + (wrong.Count > 0 ? " - " + string.Join(", ", wrong.Take(12).ToArray()) : ""));
			// (on the floor: each placeable's solid bottom at the foundations' top, the nets in the water)
			Physics.SyncTransforms();
			Block floor = HeadStart.Foundations(raft).FirstOrDefault();
			// (a joiner's foundation blocks have no solid collider of their own - Raft's FoundationCollider stands in - so
			// their top fell back to 0 and every placeable read 0.5-0.8 m "above the floor": the block's own y, the top on the host)
			float top = floor != null ? floor.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger && c.enabled).Select(c => c.bounds.max.y).DefaultIfEmpty(floor.transform.position.y).Max() : 0f;
			var gaps = new List<string>();
			float worst = 0f;
			foreach (Block b in blocks.Where(x => x.buildableItem.UniqueName.StartsWith("Placeable_") && !x.buildableItem.UniqueName.Contains("CollectionNet")))
			{
				float bottom = b.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger && c.enabled).Select(c => c.bounds.min.y).DefaultIfEmpty(float.NaN).Min();
				if (float.IsNaN(bottom)) continue;
				float gap = bottom - top;
				gaps.Add(b.buildableItem.UniqueName.Replace("Placeable_", "") + " " + gap.ToString("F2"));
				if (Mathf.Abs(gap) > Mathf.Abs(worst)) worst = gap;
			}
			Log("bottoms above the floor: " + string.Join(", ", gaps.ToArray()));
			// (too far: in the raft's own space, each placeable's height over the floor block - the same on host and joiner if
			// they are placed alike, so a wrong floor reading shows here)
			if (floor != null && Mathf.Abs(worst) >= 0.35f)
			{
				Func<Transform, float> localY = t => raft.transform.InverseTransformPoint(t.position).y;
				Vector3 tilt = raft.transform.eulerAngles;
				Log("raft tilt x " + tilt.x.ToString("F1") + " z " + tilt.z.ToString("F1") + ", floor block " + floor.name + " top " + top.ToString("F2") + " (its own y " + floor.transform.position.y.ToString("F2") + "); heights in the raft's space: "
					+ string.Join(", ", blocks.Where(x => x.buildableItem.UniqueName.StartsWith("Placeable_") && !x.buildableItem.UniqueName.Contains("CollectionNet"))
						.Select(x => x.buildableItem.UniqueName.Replace("Placeable_", "") + " " + (localY(x.transform) - localY(floor.transform)).ToString("F2")).ToArray()));
			}
			Check(ref ok, Mathf.Abs(worst) < 0.35f, "the placeables stand on the floor (the farthest " + worst.ToString("F2") + " m)");
			if (level >= 3)
			{
				Reciever rec = blocks.Where(b => b.buildableItem.UniqueName == "Placeable_Reciever").Select(b => b.GetComponentInChildren<Reciever>(true)).FirstOrDefault();
				Battery slot = rec != null ? rec.GetComponentInChildren<Battery>(true) : null;
				int connected = 0;
				try { var list = Traverse.Create(rec).Field("antennas").GetValue() as System.Collections.ICollection; connected = list != null ? list.Count : 0; } catch { }
				Check(ref ok, rec != null && slot != null && !slot.BatterySlotIsEmpty && (client || connected == 3), "the receiver: " + connected + " antennas connected, battery " + (slot != null && !slot.BatterySlotIsEmpty ? "in (" + slot.BatteryUses + ")" : "missing"));
			}
			if (ok) Log("PASS: head start raft level " + level + (client ? " (joiner)" : "")); else Fail("head start raft level " + level);
		}
	}
}
