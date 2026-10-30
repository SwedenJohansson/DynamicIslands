using System.Collections;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>Player 10-30: things done before their step go to the step that names them. A quest "read the diary, open
		/// any chest, open the Vault": the Vault opened first is kept for its own step, not the "any chest" step (before, the
		/// "any" step took it and "open the Vault" could never be done); then another chest does the "any" step and the Vault's
		/// step is done at once. A quest "any warthog x2, then a warthog": three early warthogs fill the named step and the
		/// rest goes to the "any" step.</summary>
		[ConsoleCommand(name: "CIQuestOrder1030", docs: "Dev, in game (host): player 10-30 - work done early goes to the later step that names it before a step that takes any; more of the same then fills the 'any' step")]
		public static void QuestOrder1030Command(string[] args) { StartTest(QuestOrder1030Routine()); }

		static IEnumerator QuestOrder1030Routine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (player == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			const string name = "ciquestorder1030";
			QuestRewards.ForgetIsland(name);
			var s = new IslandGenSettings { Seed = 1030, Radius = 30f, Height = 6f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, name);
			new IslandQuest { Title = "Vault", Steps = { new IslandQuest.Step { Type = "read", Target = "Diary" }, new IslandQuest.Step { Type = "open", Target = "" },
				new IslandQuest.Step { Type = "open", Target = "Vault" } } }.To(f.Props, 0);
			new IslandQuest { Title = "Hogs", Steps = { new IslandQuest.Step { Type = "reach", Target = "pen" }, new IslandQuest.Step { Type = "kill", Target = "", Count = 2 },
				new IslandQuest.Step { Type = "kill", Target = "Warthog", Count = 1 } } }.To(f.Props, 1);
			IslandWorldState.Remove(name);
			f.Save(IslandSpawner.PathFor(name));
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(name), 450f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			yield return DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name);
			if (e == null || e.Root == null) { Fail(name + " did not spawn"); yield break; }
			player.transform.position = LandSpot(e) + Vector3.up * 1.5f;
			yield return new WaitForSeconds(1.5f);
			QuestTracker.Event(e, "open", "Vault");
			QuestTracker.Event(e, "read", "Diary");
			Check(ref ok, QuestTracker.StepOf(e, 0) == 1, "the Vault opened first: the 'any chest' step still waits (step " + (QuestTracker.StepOf(e, 0) + 1) + ")");
			QuestTracker.Event(e, "open", "Crate");
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, QuestTracker.IsDone(e, 0), "another chest: the 'any' step done, and the Vault's step with it (step " + (QuestTracker.StepOf(e, 0) + 1) + ")");
			for (int i = 0; i < 3; i++) QuestTracker.Event(e, "kill", "Warthog");
			QuestTracker.Event(e, "reach", "pen");
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, QuestTracker.IsDone(e, 1), "three warthogs early: the named step got one, the 'any x2' step the rest - done (step " + (QuestTracker.StepOf(e, 1) + 1) + ")");
			OnRaftCommand();
			IslandWorldState.Remove(name);
			if (ok) Log("PASS: quest order 1030"); else Fail("quest order 1030");
		}
	}
}
