using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Multiplayer "Bring items" hand-overs the host didn't count (one machine, the host; messages caught by
	/// IslandNetwork.Loopback): two players handing over in the same moment (more than the step needed), a hand-over that
	/// came after the step was done, a refused one - the items go back to the player who sent them (QuestItemsBack), and a
	/// player puts what comes back in their bag.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIBringBack", docs: "Dev, in a world (host): multiplayer Bring items - two players' hand-overs in the same moment (6 planks wanted, 4 + 4 sent: 2 go back to the second), one after the step was done (all back), a refused one (back, at most the step's count), other step kinds and an unknown sender give nothing back, and a player's bag gets what the host gives back")]
		public static void BringBackCommand(string[] args) { StartTest(BringBackRoutine()); }

		static IEnumerator BringBackRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("CIBringBack: run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				add(Islanders.ObjectName, 0f, 4f, P(BehaviourProps.Name, "Mara", Islanders.TextProp, "Bring me planks."));
				file.Props[IslandQuest.KeyTitle] = "CI bring back";
				file.Props[IslandQuest.KeySteps] = "bring|Plank@Mara|6|\nbring|Rope@Mara|2|\nsignal|never|1|";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null) { Fail("CIBringBack: the island did not spawn"); RemovePlayerIsland(e); yield break; }
			var sent = new List<KeyValuePair<ulong, IslandNetMessage>>();
			const ulong A = 4242001UL, B = 4242002UL;
			Func<IEnumerable<IslandNetMessage>> back = () => sent.Where(x => x.Value.Kind == IslandNetMessage.QuestItemsBack).Select(x => x.Value);
			Func<ulong, int> backTo = who => sent.Where(x => x.Value.Kind == IslandNetMessage.QuestItemsBack && x.Key == who).Sum(x => x.Value.Count);
			Func<string> seen = () => string.Join(", ", back().Select(m => m.Count + " " + m.Data + " " + m.Name).ToArray());
			bool trustWas = PlayerTrust.ForTests;
			IslandNetwork.Loopback = m => sent.Add(new KeyValuePair<ulong, IslandNetMessage>(m.Kind == IslandNetMessage.QuestItemsBack ? IslandNetwork.LastSentTo : 0UL, m));
			PlayerInventory inv = RAPI.GetLocalPlayer().Inventory;
			try
			{
				Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.QuestOf(e).Steps.Count == 3, "the quest waits at step 1: " + QuestTracker.QuestOf(e).Steps[0].Describe());

				// two players, each with 4 planks, hand over in the same moment (both saw "6 wanted")
				QuestTracker.AddFromPlayer(e.Id, 0, 0, 4, A);
				Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.ProgressOf(e, 0) == 4 && !back().Any(), "player A's 4 planks: 4 of 6, nothing given back (" + seen() + ")");
				QuestTracker.AddFromPlayer(e.Id, 0, 0, 4, B);
				IslandNetMessage m1 = back().FirstOrDefault();
				Check(ref ok, QuestTracker.StepOf(e) == 1, "player B's 4 planks finish the step (step " + (QuestTracker.StepOf(e) + 1) + ")");
				Check(ref ok, back().Count() == 1 && backTo(B) == 2 && backTo(A) == 0 && m1.Data == "Plank" && m1.Name == "over" && m1.Ids[0] == e.Id && m1.Ids[1] == 0 && m1.Index == 0,
					"... the 2 planks over the count go back to player B only (" + seen() + ")");

				// a hand-over that comes after the step was done: all of it back
				sent.Clear();
				QuestTracker.AddFromPlayer(e.Id, 0, 0, 3, A);
				Check(ref ok, QuestTracker.StepOf(e) == 1 && QuestTracker.ProgressOf(e, 0) == 0 && backTo(A) == 3 && back().All(x => x.Name == "late" && x.Data == "Plank"), "a late hand-over (step already done): all 3 planks back to player A, the quest unchanged (" + seen() + ")");
				Check(ref ok, sent.Any(x => x.Value.Kind == IslandNetMessage.QuestStep && x.Value.Index == 1), "... and the quest's step is sent to put the player's view right");

				// the same through the network message ("add" from a player), and an exact hand-over: nothing back
				sent.Clear();
				PlayerTrust.ForTests = true;
				IslandNetwork.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.QuestStep, Ids = new[] { e.Id }, Index = 1, Count = 1, Name = "add" }, new Network_UserId(B));
				IslandNetwork.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.QuestStep, Ids = new[] { e.Id }, Index = 1, Count = 3, Name = "add" }, new Network_UserId(A));
				Check(ref ok, QuestTracker.StepOf(e) == 2 && backTo(A) == 2 && backTo(B) == 0 && back().All(x => x.Data == "Rope" && x.Name == "over"), "rope by message: B's 1 then A's 3 of 2 wanted - the step done, 2 rope back to A (" + seen() + ")");

				// other step kinds and an unknown sender: nothing back (as before)
				sent.Clear();
				QuestTracker.AddFromPlayer(e.Id, 0, 2, 5, A); // (a signal step's count over: no items in it)
				QuestTracker.AddFromPlayer(e.Id, 0, 0, 2);    // (no sender: an older call)
				Check(ref ok, !back().Any(), "a signal step's extra count and a hand-over with no sender give nothing back (" + seen() + ")");

				// refused by the host (PlayerTrust): back, never more than the step asks for
				sent.Clear();
				QuestTracker.GiveBack(e.Id, 0, 1, 50, B, "refused");
				Check(ref ok, backTo(B) == 2 && back().All(x => x.Name == "refused" && x.Data == "Rope"), "a refused hand-over: back to its player, at most the step's 2 rope (" + seen() + ")");

				// a player's side: what the host gives back goes in the bag, with a note
				int planks0 = inv.GetItemCount("Plank");
				QuestSteps.ItemsBack("Plank", 2, "over");
				Check(ref ok, inv.GetItemCount("Plank") == planks0 + 2 && (QuestSteps.LastMessage ?? "").Contains("given back"), "a player gets 2 planks back in the bag: \"" + QuestSteps.LastMessage + "\"");
				string said = QuestSteps.LastMessage;
				QuestSteps.ItemsBack("NoSuchItem_CI", 2, "late");
				QuestSteps.ItemsBack("Plank", 0, "late");
				Check(ref ok, inv.GetItemCount("Plank") == planks0 + 2 && QuestSteps.LastMessage == said, "an unknown item or none: nothing given, no note");
				if (planks0 + 2 > 0) inv.RemoveItem("Plank", 2);
			}
			finally
			{
				IslandNetwork.Loopback = null;
				PlayerTrust.ForTests = trustWas;
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: bring items given back"); else Fail("bring items given back");
		}
	}
}
