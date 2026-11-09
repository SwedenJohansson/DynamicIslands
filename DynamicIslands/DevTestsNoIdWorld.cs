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
	/// A world without Raft's world id: made before Raft gave worlds one, or saved by Esc > Quit game after Raft had cleared
	/// the id (bug 11-09). Its custom islands' state is kept under its name (noid-<name>), and on the next load the world gets
	/// an id back (WorldIdRepair) and that state moves to it. Run as:
	///   CINewWorld CI NoId; CINoIdWorld strip; CIMainMenu; CILoadWorld CI NoId; CINoIdWorld repaired;
	///   CIMainMenu; CILoadWorld CI NoId; CINoIdWorld check
	/// </summary>
	public static partial class DevTests
	{
		static string noIdStep, noIdRepaired;
		[ConsoleCommand(name: "CINoIdWorld", docs: "Dev, in a world: a world without Raft's world id gets one back and keeps its plan and story chain. CINoIdWorld strip = take the world's id away, give it the plan CI Receiver adventure, mark its first story step done, save (Raft's save has no id); repaired = (after loading it again) it has an id, with the plan and the step; check = (after loading it again) the same id, the plan and the step are still there")]
		public static void NoIdWorldCommand(string[] args)
		{
			string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CINoIdWorld: run in a world you host"); return; }
			bool ok = true;
			if (what == "strip")
			{
				SaveAndLoad.WorldGuid = Guid.Empty;
				Check(ref ok, IslandWorldState.WorldKey.StartsWith(IslandWorldState.NoIdPrefix), "its state is kept under its name (" + IslandWorldState.WorldKey + ")");
				WriteCIPlan(CIReceiver);
				DynamicIslands.WorldPlanCommand(new[] { CIReceiver });
				Check(ref ok, WorldDirector.PlanName == CIReceiver, "WorldPlan gave it the plan (" + WorldDirector.PlanName + ")");
				noIdStep = StoryChain.Steps.FirstOrDefault();
				if (noIdStep != null) StoryChain.MarkDone(noIdStep);
				Check(ref ok, noIdStep != null && StoryChain.Done.Contains(noIdStep), "its first step '" + noIdStep + "' is done");
				// (the save guard stops saves once the id is cleared; this one is the damaged save of before 3.4)
				SaveGuard.TestOff = true;
				try { UnityEngine.Object.FindObjectOfType<SaveAndLoad>().SaveGame(false); } catch (Exception ex) { Log("  (Raft's save: " + ex.Message + ")"); }
				finally { SaveGuard.TestOff = false; }
				string file = IslandWorldState.WorldFilePath;
				string[] lines = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
				Check(ref ok, lines.Any(l => l.StartsWith("@plan=") && l.Contains(CIReceiver)), "the no-id file has the plan (" + Path.GetFileName(file) + ")");
				WorldIdRepair.Last = null;
				if (ok) Log("PASS: no-id world strip"); else Fail("no-id world strip");
				return;
			}
			if (what == "repaired")
			{
				string step = noIdStep ?? "camp";
				Check(ref ok, WorldIdRepair.Last != null, "the world was repaired on load (" + WorldIdRepair.Last + ")");
				Check(ref ok, SaveAndLoad.WorldGuid != Guid.Empty, "it has a world id (" + SaveAndLoad.WorldGuid + ")");
				Check(ref ok, !IslandWorldState.WorldKey.StartsWith(IslandWorldState.NoIdPrefix), "its state is kept under the id");
				Check(ref ok, WorldDirector.PlanName == CIReceiver, "the plan came with it (" + WorldDirector.PlanName + ")");
				Check(ref ok, StoryChain.Done.Contains(step), "the done step '" + step + "' came with it");
				noIdRepaired = SaveAndLoad.WorldGuid.ToString();
				WorldIdRepair.Last = null;
				try { UnityEngine.Object.FindObjectOfType<SaveAndLoad>().SaveGame(false); } catch (Exception ex) { Log("  (Raft's save: " + ex.Message + ")"); }
				if (ok) Log("PASS: no-id world repaired"); else Fail("no-id world repaired");
				return;
			}
			if (what == "check")
			{
				string step = noIdStep ?? "camp";
				Check(ref ok, WorldIdRepair.Last == null, "no repair this time (" + WorldIdRepair.Last + ")");
				Check(ref ok, noIdRepaired == null || SaveAndLoad.WorldGuid.ToString() == noIdRepaired, "the same world id (" + SaveAndLoad.WorldGuid + ")");
				Check(ref ok, WorldDirector.PlanName == CIReceiver, "the plan is back after loading (" + WorldDirector.PlanName + ")");
				Check(ref ok, StoryChain.Active && StoryChain.Steps.Count > 1, "the story chain is back (" + StoryChain.Steps.Count + " steps)");
				Check(ref ok, StoryChain.Done.Contains(step), "the done step '" + step + "' is still done");
				RemoveCIPlan(CIReceiver);
				if (ok) Log("PASS: no-id world keeps its id, plan and story"); else Fail("no-id world keeps its id, plan and story");
				return;
			}
			Fail("CINoIdWorld strip|repaired|check");
		}
	}
}
