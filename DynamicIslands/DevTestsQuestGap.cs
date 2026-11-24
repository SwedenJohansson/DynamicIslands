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
			var facts = new QuestSteps.PlanFacts();
			List<string> fish = QuestSteps.FishNames();
			Check(ref ok, fish.Contains("Raw_Pomfret") && fish.Count >= 4, "the rod's fish: " + string.Join(", ", fish.ToArray()));
			Check(ref ok, new IslandQuest.Step { Type = QuestSteps.Fish, Target = "Raw_Pomfret", Count = 2 }.Describe().StartsWith("Catch 2") && new IslandQuest.Step { Type = QuestSteps.Fish, Count = 3 }.Describe() == "Catch 3 fish with the rod"
				&& new IslandQuest.Step { Type = QuestSteps.Gather, Target = "Plank", Count = 3 }.Describe().StartsWith("Gather 3"),
				"the words: " + new IslandQuest.Step { Type = QuestSteps.Fish, Target = "Raw_Pomfret", Count = 2 }.Describe() + " / " + new IslandQuest.Step { Type = QuestSteps.Gather, Target = "Plank", Count = 3 }.Describe());
			bool w1, w2, w3, w4, w5, w6;
			string pFish = QuestSteps.StepProblem(facts, QuestSteps.Fish, "Raw_Pomfret", 1, out w1), pAny = QuestSteps.StepProblem(facts, QuestSteps.Fish, "", 1, out w2),
				notFish = QuestSteps.StepProblem(facts, QuestSteps.Fish, "Plank", 1, out w3), pNone = QuestSteps.StepProblem(facts, QuestSteps.Fish, "Unobtainium", 1, out w4),
				gPlank = QuestSteps.StepProblem(facts, QuestSteps.Gather, "Plank", 3, out w5), gNone = QuestSteps.StepProblem(facts, QuestSteps.Gather, "Unobtainium", 3, out w6);
			Check(ref ok, pFish == null && pAny == null && notFish != null && w3 && pNone != null && !w4 && gPlank == null && gNone != null,
				"the plan check: a fish is fine, a plank to fish a warning, unknown items problems (pomfret " + (pFish ?? "ok") + "; any " + (pAny ?? "ok") + "; plank " + (notFish ?? "ok") + " warn " + w3
				+ "; unknown " + (pNone ?? "ok") + " warn " + w4 + "; gather plank " + (gPlank ?? "ok") + "; gather unknown " + (gNone ?? "ok") + ")");
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
				flower = add("Pickup_Landmark_Flower_Red", 2f, 3f, new Dictionary<string, string> { { BehaviourProps.Name, "ci-flower" } });
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
					QuestSteps.Caught("Placeable_GlassCandle", 1);
					yield return new WaitForSeconds(0.5f);
					Check(ref ok, QuestTracker.StepOf(e) == 1 && QuestTracker.ProgressOf(e, 0) == 0, "the rod's glass candle is no fish: step 2 still waits");
					int fished = QuestSteps.Fished;
					AccessTools.Method(typeof(QuestStepsFish), "Prefix").Invoke(null, new object[0]);
					Item_Base got = null;
					for (int i = 0; i < 200 && (got == null || !QuestSteps.IsFish(got.UniqueName)); i++) got = bait.GetRandomItemFromCurrentBaitPool(false);
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
				QuestStepsGather.Leave(); // (the finalizer, as Harmony calls it)
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, !QuestSteps.GatherWaiting() && QuestTracker.StepOf(e) == 2 && QuestTracker.ProgressOf(e, 0) == 0, "planks picked up far off the island don't count");

				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1f);
				QuestStepsGather.Before(me);
				inv.AddItem("Plank", 2);
				inv.AddItem("Nail", 4);
				QuestStepsGather.After(me);
				QuestStepsGather.Leave(); // (the finalizer, as Harmony calls it)
				yield return new WaitForSeconds(0.3f);
				Check(ref ok, QuestTracker.StepOf(e) == 2 && QuestTracker.ProgressOf(e, 0) == 2, "2 planks (and nails) picked up on the island: 2 of 3 (" + QuestTracker.ProgressOf(e, 0) + ")");
				QuestStepsGather.Before(me);
				QuestStepsGather.Before(me); // (a harvest through a pickup: the outer one counts, once)
				inv.AddItem("Plank", 1);
				QuestStepsGather.After(me);
				QuestStepsGather.Leave(); // (the finalizer, as Harmony calls it)
				QuestStepsGather.After(me);
				QuestStepsGather.Leave(); // (the finalizer, as Harmony calls it)
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

		const int QuestTestFakeId = 987651104;

		[ConsoleCommand(name: "CIQuestTestCommands", docs: "Dev, in a world (host): gap idea 9 - QuestTest on an island with 2 quests: the list, a signal sent, a jump forward and back (the players told \"set\"), quest 2 done, a story item given, the island reset (made again: its flower back, quests at step 1, signals gone; the players told \"reset\", a player's copy takes it); bad arguments refused")]
		public static void QuestTestCommandsTest() { StartTest(QuestTestCommandsRoutine()); }

		static IEnumerator QuestTestCommandsRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			int flower = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				flower = add("Pickup_Landmark_Flower_Red", 2f, 3f, new Dictionary<string, string> { { BehaviourProps.Name, "ci-flower" } });
				file.Props[IslandQuest.KeyTitle] = "CI bells";
				file.Props[IslandQuest.KeySteps] = "signal|ci-bell|1|\nsignal|ci-gong|1|\nsignal|ci-horn|1|";
				file.Props[IslandQuest.Key(IslandQuest.KeyTitle, 1)] = "CI drums";
				file.Props[IslandQuest.Key(IslandQuest.KeySteps, 1)] = "signal|ci-drum|1|\nsignal|ci-flute|1|";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			Network_Player me = RAPI.GetLocalPlayer();
			var sent = new List<IslandNetMessage>();
			var nt = Traverse.Create(typeof(IslandNetwork));
			bool worldWas = nt.Field("worldReceived").GetValue<bool>();
			const string gem = "ci_questtest_gem";
			int gemsBefore = StoryBook.Count(gem);
			try
			{
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, QuestTestCommands.Nearest(true) == e, "the island nearest the player is the one QuestTest works on");

				// the list
				QuestTestCommands.QuestTestCommand(new string[0]);
				string list = QuestTestCommands.Describe(e);
				Check(ref ok, list.Contains("Quest 1 'CI bells': step 1 of 3") && list.Contains("Quest 2 'CI drums': step 1 of 2") && list.Contains(" > 1. "), "the list: both quests, step 1 marked (" + list.Replace("\n", " / ") + ")");

				// a signal: the main quest's step 1 is done by it
				QuestTestCommands.QuestTestCommand(new[] { "signal", "ci-bell" });
				yield return WaitStep(e, 1, 3f);
				Check(ref ok, Behaviours.HasSignal(e, "ci-bell") && QuestTracker.StepOf(e) == 1, "QuestTest signal ci-bell: the signal is had, step 1 done");

				// forward, then back (the players told "set")
				QuestTestCommands.QuestTestCommand(new[] { "step", "3" });
				Check(ref ok, QuestTracker.StepOf(e) == 2 && QuestTracker.LastMessage.Contains("ci-horn"), "QuestTest step 3: at step 3 (" + QuestTracker.LastMessage + ")");
				IslandNetwork.Loopback = x => sent.Add(x);
				QuestTestCommands.QuestTestCommand(new[] { "step", "1" });
				IslandNetwork.Loopback = null;
				IslandNetMessage set = sent.FirstOrDefault(x => x.Kind == IslandNetMessage.QuestStep && x.Name == "set");
				Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.ProgressOf(e, 0) == 0 && set != null && set.Ids[0] == e.Id && set.Index == 0,
					"QuestTest step 1: back at step 1, the players told \"set\" step 1 (" + sent.Count + " message(s))");
				QuestTestCommands.QuestTestCommand(new[] { "step", "done", "2" });
				Check(ref ok, QuestTracker.StepOf(e, 1) == 2 && QuestTracker.StepOf(e) == 0, "QuestTest step done 2: quest 2 done, the main quest left at step 1");
				QuestTestCommands.QuestTestCommand(new[] { "step", "9" });
				QuestTestCommands.QuestTestCommand(new[] { "step", "1", "3" });
				QuestTestCommands.QuestTestCommand(new[] { "jump" });
				Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.StepOf(e, 1) == 2, "bad arguments (step 9, quest 3, jump) change nothing");

				// a story item
				QuestTestCommands.QuestTestCommand(new[] { "give", gem, "2" });
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, StoryBook.Count(gem) == gemsBefore + 2, "QuestTest give " + gem + " 2: the crew has " + StoryBook.Count(gem));

				// reset: the flower picked first, then the island as new
				IslandObjectRef r = e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(x => x.Index == flower);
				PickupItem pick = r != null ? r.GetComponentInChildren<PickupItem>(true) : null;
				if (pick != null) me.PickupScript.PickupItem(pick, true, false);
				yield return new WaitForSeconds(1f);
				IslandObjectState.Capture(e);
				Check(ref ok, pick != null && e.State.Count > 3, "the flower picked: the island has " + e.State.Count + " things in its state");
				sent.Clear();
				IslandNetwork.Loopback = x => sent.Add(x);
				QuestTestCommands.QuestTestCommand(new[] { "reset" });
				IslandNetwork.Loopback = null;
				IslandNetMessage reset = sent.FirstOrDefault(x => x.Kind == IslandNetMessage.Islands && x.Name == "reset");
				Check(ref ok, e.Root == null && e.State.Count == 0 && reset != null && reset.Ids.Length == 1 && reset.Ids[0] == e.Id && reset.States[0] == "" && !reset.FullList,
					"QuestTest reset: unloaded, its state empty, the players told \"reset\"");
				for (float t = 0; t < 40f && (e.Root == null || e.Loading); t += 0.5f) yield return new WaitForSeconds(0.5f);
				yield return new WaitForSeconds(2f);
				r = e.Root != null ? e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(x => x.Index == flower) : null;
				pick = r != null ? r.GetComponentInChildren<PickupItem>(true) : null;
				Check(ref ok, e.Root != null && pick != null && pick.gameObject.activeInHierarchy, "... made again by streaming: the flower is back");
				yield return new WaitForSeconds(1f);
				Check(ref ok, me.transform.position.y > 1f, "... the player is put back on its ground, not left in the sea under it (y " + me.transform.position.y.ToString("0.0") + ")");
				Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.StepOf(e, 1) == 0 && !Behaviours.HasSignal(e, "ci-bell"), "... both quests at step 1, the signal gone");
				Screenshot(new[] { "qtest_reset" });

				// a player's copy takes the "reset" (the client side, on this machine): a known island not loaded there
				IslandWorldState.RemoveIds(new[] { QuestTestFakeId }, false);
				IslandWorldState.Entry fake = IslandWorldState.AddRemote(QuestTestFakeId, "ciquesttestfake", "", CustomIslandSpawner.RaftPosition.Value + new Vector3(40000f, 0f, 40000f));
				fake.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 2, Day = 1 };
				fake.State[0x123] = new ObjectState { Active = false, Yield = -1, Day = 1 };
				nt.Field("worldReceived").SetValue(true);
				var msg = new IslandNetMessage { Kind = IslandNetMessage.Islands, Name = "reset", Ids = new[] { QuestTestFakeId }, Names = new[] { "ciquesttestfake" }, Hashes = new[] { "" }, States = new[] { "" }, Labels = new[] { "" }, Rules = new[] { "" }, Offsets = new[] { 40000f, 0f, 40000f } };
				string threw = null;
				try { nt.Method("ReceiveIslands", new[] { typeof(IslandNetMessage) }).GetValue(msg); }
				catch (Exception ex) { threw = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message; }
				Check(ref ok, threw == null && fake.State.Count == 0 && IslandWorldState.Contains(fake), "a player's copy takes the host's reset: its state empty" + (threw != null ? " (" + threw + ")" : ""));
				yield return new WaitForSeconds(0.6f);
			}
			finally
			{
				IslandNetwork.Loopback = null;
				nt.Field("worldReceived").SetValue(worldWas);
				IslandWorldState.RemoveIds(new[] { QuestTestFakeId }, false);
				int extra = StoryBook.Count(gem) - gemsBefore;
				if (extra > 0) StoryBook.Take(gem, extra);
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: QuestTest commands"); else Fail("QuestTest commands");
		}
	}
}
