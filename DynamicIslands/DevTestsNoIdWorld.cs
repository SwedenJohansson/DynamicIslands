using System;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// A world without Raft's world id (made before Raft gave worlds one: SaveAndLoad.WorldGuid stays empty). Its custom
	/// islands' state was never saved, so a plan set with WorldPlan and the story chain's progress were gone on the next
	/// load (player log 11-02, world 'Dark Depths'). Run as:
	///   CINewWorld CI NoId; CINoIdWorld strip; CIMainMenu; CILoadWorld CI NoId; CINoIdWorld plan;
	///   CIMainMenu; CILoadWorld CI NoId; CINoIdWorld check
	/// </summary>
	public static partial class DevTests
	{
		static string noIdStep;

		[ConsoleCommand(name: "CINoIdWorld", docs: "Dev, in a world: a world without Raft's world id keeps its plan and story chain. CINoIdWorld strip = take the world's id away (Raft's next save has none); plan = (after loading it again) give it the plan CI Receiver adventure, mark its first story step done, save; check = (after loading it again) the plan and the step are still there")]
		public static void NoIdWorldCommand(string[] args)
		{
			string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CINoIdWorld: run in a world you host"); return; }
			bool ok = true;
			if (what == "strip")
			{
				SaveAndLoad.WorldGuid = Guid.Empty;
				Check(ref ok, SaveAndLoad.WorldGuid == Guid.Empty, "the world has no world id now (Raft saves it without one)");
				if (ok) Log("PASS: no-id world strip"); else Fail("no-id world strip");
				return;
			}
			if (what == "plan")
			{
				Check(ref ok, SaveAndLoad.WorldGuid == Guid.Empty, "the world loaded without a world id");
				Check(ref ok, IslandWorldState.WorldKey.StartsWith(IslandWorldState.NoIdPrefix), "its state is kept under its name (" + IslandWorldState.WorldKey + ")");
				Check(ref ok, IslandWorldState.ForThisWorld, "the island list is this world's");
				WriteCIPlan(CIReceiver);
				DynamicIslands.WorldPlanCommand(new[] { CIReceiver });
				Check(ref ok, WorldDirector.PlanName == CIReceiver, "WorldPlan gave it the plan (" + WorldDirector.PlanName + ")");
				Check(ref ok, StoryChain.Active && StoryChain.Steps.Count > 1, "the plan's story chain is on (" + StoryChain.Steps.Count + " steps)");
				noIdStep = StoryChain.Steps.FirstOrDefault();
				if (noIdStep != null) StoryChain.MarkDone(noIdStep);
				Check(ref ok, noIdStep != null && StoryChain.Done.Contains(noIdStep), "its first step '" + noIdStep + "' is done");
				try { UnityEngine.Object.FindObjectOfType<SaveAndLoad>().SaveGame(false); } catch (Exception ex) { Log("  (Raft's save: " + ex.Message + ")"); }
				string file = IslandWorldState.WorldFilePath;
				string[] lines = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
				Check(ref ok, lines.Any(l => l.StartsWith("@plan=") && l.Contains(CIReceiver)), "the world's file has the plan (" + Path.GetFileName(file) + ")");
				Check(ref ok, lines.Any(l => l.StartsWith("@storydone=") && l.IndexOf(noIdStep ?? "-", StringComparison.OrdinalIgnoreCase) >= 0), "the world's file has the done step");
				if (ok) Log("PASS: no-id world plan"); else Fail("no-id world plan");
				return;
			}
			if (what == "check")
			{
				string step = noIdStep ?? "camp";
				Check(ref ok, SaveAndLoad.WorldGuid == Guid.Empty, "the world loaded without a world id again");
				Check(ref ok, WorldDirector.PlanName == CIReceiver, "the plan is back after loading (" + WorldDirector.PlanName + ")");
				Check(ref ok, StoryChain.Active && StoryChain.Steps.Count > 1, "the story chain is back (" + StoryChain.Steps.Count + " steps)");
				Check(ref ok, StoryChain.Done.Contains(step), "the done step '" + step + "' is still done");
				RemoveCIPlan(CIReceiver);
				if (ok) Log("PASS: no-id world keeps its plan"); else Fail("no-id world keeps its plan");
				return;
			}
			Fail("CINoIdWorld strip|plan|check");
		}
	}
}
