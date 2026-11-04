using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;
using HMLLibrary;

namespace DynamicIslands
{
	/// <summary>Tests of the quest step kinds of 11-04 (gap ideas 3 and 4): "Catch fish" and "Gather / harvest".</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIQuestFishGather", docs: "Dev, in a world (host): gap ideas 3+4 - an island whose quest wants 2 pomfret, any fish (Raft's rod hooks), 3 planks gathered and a flower picked; wrong fish, early and far work don't count")]
		public static void QuestFishGather() { StartTest(QuestFishGatherRoutine()); }

		static IEnumerator QuestFishGatherRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;

			// words, plan check, editor list (no world needed)
			bool warn;
			var facts = new QuestSteps.PlanFacts();
			List<string> fish = QuestSteps.FishNames();
			Check(ref ok, fish.Contains("Raw_Pomfret") && fish.Count >= 4, "the rod's fish: " + string.Join(", ", fish.ToArray()));
			Check(ref ok, new IslandQuest.Step { Type = QuestSteps.Fish, Target = "Raw_Pomfret", Count = 2 }.Describe().StartsWith("Catch 2") && new IslandQuest.Step { Type = QuestSteps.Fish, Count = 3 }.Describe() == "Catch 3 fish with the rod"
				&& new IslandQuest.Step { Type = QuestSteps.Gather, Target = "Plank", Count = 3 }.Describe().StartsWith("Gather 3"),
				"the words: " + new IslandQuest.Step { Type = QuestSteps.Fish, Target = "Raw_Pomfret", Count = 2 }.Describe() + " / " + new IslandQuest.Step { Type = QuestSteps.Gather, Target = "Plank", Count = 3 }.Describe());
			string notFish = QuestSteps.StepProblem(facts, QuestSteps.Fish, "Plank", 1, out warn);
			Check(ref ok, QuestSteps.StepProblem(facts, QuestSteps.Fish, "Raw_Pomfret", 1, out warn) == null && QuestSteps.StepProblem(facts, QuestSteps.Fish, "", 1, out warn) == null
				&& notFish != null && warn && QuestSteps.StepProblem(facts, QuestSteps.Fish, "Unobtainium", 1, out warn) != null && !warn
				&& QuestSteps.StepProblem(facts, QuestSteps.Gather, "Plank", 3, out warn) == null && QuestSteps.StepProblem(facts, QuestSteps.Gather, "Unobtainium", 3, out warn) != null,
				"the plan check: a fish is fine, a plank to fish a warning (" + notFish + "), unknown items problems");
			List<DropList.Option> fishList = QuestSteps.NamesFor(QuestSteps.Fish, "", new EditorGameObject[0]);
			Check(ref ok, fishList != null && fishList.Count == fish.Count + 1 && QuestSteps.HasCount(QuestSteps.Fish) && QuestSteps.HasCount(QuestSteps.Gather)
				&& IslandQuest.Types.Contains(QuestSteps.Fish) && IslandQuest.Types.Contains(QuestSteps.Gather), "the editor: both kinds, with a count; the fish list has 'any' + " + fish.Count);
			Func<Type, string, string, bool> hooked = (t, m, by) =>
			{
				Patches pi = Harmony.GetPatchInfo(AccessTools.Method(t, m));
				return pi != null && pi.Postfixes.Any(x => x.PatchMethod.DeclaringType.Name == by);
			};
			Check(ref ok, hooked(typeof(FishingRod), "PullItemsFromSea", "QuestStepsFish") && hooked(typeof(FishingBaitHandler), "GetRandomItemFromCurrentBaitPool", "QuestStepsFishPool")
				&& hooked(typeof(Pickup), "AddItemToInventory", "QuestStepsPickup") && hooked(typeof(PickupChanneling), "AddItemToInventory", "QuestStepsChannel") && hooked(typeof(HarvestableTree), "Harvest", "QuestStepsHarvest"),
				"the rod, the bait pool, pickups, channelled pickups and harvests are hooked");

			int flower = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				flower = add("Pickup_Landmark_Flower_Red", 2f, 3f, new Dictionary<string, string>());
				file.Props[IslandQuest.KeyTitle] = "CI fish and gather";
				file.Props[IslandQuest.KeySteps] = "fish|Raw_Pomfret|2|\nfish||1|\ngather|Plank|3|\ngather||1|";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			Network_Player me = RAPI.GetLocalPlayer();
			PlayerInventory inv = me.Inventory;
			try
			{
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, QuestTracker.QuestOf(e).Steps.Count == 4 && QuestTracker.StepOf(e) == 0, "the quest has 4 steps, at step 1: " + QuestTracker.QuestOf(e).Steps[0].Describe());
				Screenshot(new[] { "qgap_fish" });

				// early and wrong work
				Check(ref ok, !QuestSteps.GatherWaiting(), "no gather step waits yet (the pickup hooks stay idle)");
				QuestSteps.Gathered("Plank", 5);
				QuestSteps.Caught("Raw_Mackerel", 1);
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.ProgressOf(e, 0) == 0, "a mackerel and planks gathered early don't count");

				// 2 pomfret
				QuestSteps.Caught("Raw_Pomfret", 1);
				yield return new WaitForSeconds(0.3f);
				Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.ProgressOf(e, 0) == 1, "1 of 2 pomfret");
				QuestSteps.Caught("raw_pomfret", 1);
				yield return WaitStep(e, 1, 3f);
				Check(ref ok, QuestTracker.StepOf(e) == 1, "2 pomfret: step 1 done");

				// any fish, through Raft's rod: the hooks as Raft's PullItemsFromSea runs them
				FishingRod rod = me.GetComponentsInChildren<FishingRod>(true).FirstOrDefault();
				FishingBaitHandler bait = rod != null ? Traverse.Create(rod).Field("fishingBaitHandler").GetValue<FishingBaitHandler>() : null;
				Check(ref ok, rod != null && bait != null, "the player's rod and its bait handler");
				if (rod != null && bait != null)
				{
					int fished = QuestSteps.Fished;
					AccessTools.Method(typeof(QuestStepsFish), "Prefix").Invoke(null, new object[0]);
					Item_Base got = bait.GetRandomItemFromCurrentBaitPool(false);
					AccessTools.Method(typeof(QuestStepsFish), "Postfix").Invoke(null, new object[] { rod });
					yield return WaitStep(e, 2, 3f);
					Check(ref ok, got != null && QuestSteps.Fished == fished + 1 && QuestSteps.LastCaught == got.UniqueName && QuestTracker.StepOf(e) == 2,
						"the rod pulls " + (got != null ? got.UniqueName : "nothing") + ": any fish counts, step 2 done");
				}
				else QuestSteps.Caught("Raw_Herring", 1);
				yield return WaitStep(e, 2, 3f);

				// gather: far off doesn't count
				Check(ref ok, QuestSteps.GatherWaiting(), "the gather step waits: the pickup hooks count now");
				Vector3 home = me.transform.position;
				Vector3 mid = e.Position;
				Vector3 away = new Vector3(home.x - mid.x, 0f, home.z - mid.z).normalized;
				if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
				yield return PutPlayer(me, mid + away * (Mathf.Max(30f, CustomIslandSpawner.LandRadius(e.Name)) + QuestSteps.GatherReach + 60f), true);
				QuestStepsGather.Before(me);
				inv.AddItem("Plank", 3);
				QuestStepsGather.After(me);
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, !QuestSteps.GatherWaiting() && QuestTracker.StepOf(e) == 2 && QuestTracker.ProgressOf(e, 0) == 0, "planks picked up far off the island don't count");

				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1f);
				QuestStepsGather.Before(me);
				inv.AddItem("Plank", 2);
				inv.AddItem("Nail", 4);
				QuestStepsGather.After(me);
				yield return new WaitForSeconds(0.3f);
				Check(ref ok, QuestTracker.StepOf(e) == 2 && QuestTracker.ProgressOf(e, 0) == 2, "2 planks (and nails) picked up on the island: 2 of 3 (" + QuestTracker.ProgressOf(e, 0) + ")");
				QuestStepsGather.Before(me);
				QuestStepsGather.Before(me); // (a harvest through a pickup: the outer one counts, once)
				inv.AddItem("Plank", 1);
				QuestStepsGather.After(me);
				QuestStepsGather.After(me);
				yield return WaitStep(e, 3, 3f);
				Check(ref ok, QuestTracker.StepOf(e) == 3, "the 3rd plank: step 3 done (counted once in nested hooks)");

				// a real pickup: the flower picked as a player does
				IslandObjectRef r = e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(x => x.Index == flower);
				PickupItem pick = r != null ? r.GetComponentInChildren<PickupItem>(true) : null;
				Check(ref ok, pick != null, "the island's red flower is a Raft pickup");
				if (pick != null)
				{
					int gathered = QuestSteps.GatheredCount;
					me.PickupScript.PickupItem(pick, true, false);
					yield return WaitStep(e, 4, 4f);
					Check(ref ok, QuestSteps.GatheredCount > gathered && QuestTracker.StepOf(e) == 4, "picking the flower (" + QuestSteps.LastGathered + ") finishes the quest: " + QuestTracker.LastMessage);
				}
				Screenshot(new[] { "qgap_done" });
				yield return new WaitForSeconds(0.6f);
			}
			finally { RemovePlayerIsland(e); }
			if (ok) Log("PASS: fish and gather steps"); else Fail("fish and gather steps");
		}
	}
}
