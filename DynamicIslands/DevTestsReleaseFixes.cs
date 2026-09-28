using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandUndoRedo;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of what the user decided before the release (TEST_CATALOGUE UE1, UE2, UW1, UW7, UL8; tools\releasefix.ps1):
	/// the editor's autosave, the notice when saving an island saved worlds have, an edited plan in a saved world, and an
	/// older Raft save loading the mod's state of that save.
	/// </summary>
	public static partial class DevTests
	{
		const string AutosaveIsland = "ciautosave";

		class NoopCommand : ICommand { public void Execute() { } public void UnExecute() { } }

		[ConsoleCommand(name: "CIAutosave", docs: "Dev, editor: the autosave - nothing unsaved after Save; a change is autosaved after the interval (whole, with the change); it is offered (newer than the island), the offer window opens, picking it opens the unsaved work; Save removes it; leaving writes it at once; Throw away deletes it; saving an island saved worlds have says so once. Cleans up")]
		public static void AutosaveCommand() { DynamicIslands.instance.StartCoroutine(AutosaveRoutine()); }

		static IEnumerator AutosaveRoutine()
		{
			bool ok = true;
			string island = IslandSpawner.PathFor(AutosaveIsland), autosave = EditorAutosave.PathFor(AutosaveIsland);
			string worldFile = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), "ci-autosave-test.txt");
			float interval = EditorAutosave.IntervalSeconds;
			try
			{
				if (!DynamicIslands.InEditor()) { Fail("autosave: open the editor first"); yield break; }
				foreach (string f in new[] { island, autosave, worldFile }) if (File.Exists(f)) File.Delete(f);
				DynamicIslands.NewIsland();
				Check(ref ok, DynamicIslands.SaveIsland(AutosaveIsland) && !EditorAutosave.Unsaved, "saved: nothing unsaved");

				// A change (raised ground, one undo step): autosaved once the interval has passed
				TerrainData data = terraineditor.terrain.terrainData;
				data.SetHeights(10, 10, new float[,] { { 0.3f } });
				UndoRedoManager.Execute(new NoopCommand());
				Check(ref ok, EditorAutosave.Unsaved, "a change: unsaved");
				EditorAutosave.IntervalSeconds = 1f;
				for (float until = Time.unscaledTime + 10f; !File.Exists(autosave) && Time.unscaledTime < until; ) yield return new WaitForSecondsRealtime(0.5f);
				EditorAutosave.IntervalSeconds = interval;
				Check(ref ok, File.Exists(autosave) && !File.Exists(autosave + ".tmp"), "autosaved after the interval: " + autosave);
				IslandFile a = File.Exists(autosave) ? IslandFile.Load(autosave) : null;
				Check(ref ok, a != null && Mathf.Abs(a.Heights[10, 10] - 0.3f) < 0.01f && IslandFile.Load(island).Heights[10, 10] < 0.01f, "the autosave has the change, the island's file doesn't");
				Check(ref ok, EditorAutosave.Waiting().Contains(AutosaveIsland), "it is offered (newer than the island)");

				// The offer, and picking it: the unsaved work comes back (the ground raised), still unsaved
				DynamicIslands.LoadIsland(AutosaveIsland);
				yield return null;
				Check(ref ok, data.GetHeights(10, 10, 1, 1)[0, 0] < 0.01f && !EditorAutosave.Unsaved, "the island opened from its file: the change isn't there");
				yield return EditorAutosave.Offer();
				Check(ref ok, ChoiceWindow.IsOpen, "the editor offers the unsaved work in a window");
				ChoiceWindow.Close();
				EditorAutosave.Pick(AutosaveIsland);
				yield return null;
				Check(ref ok, Mathf.Abs(data.GetHeights(10, 10, 1, 1)[0, 0] - 0.3f) < 0.01f && EditorAutosave.Unsaved && DynamicIslands.currentIslandName == AutosaveIsland,
					"picking it opens the unsaved work under the island's name, still unsaved");

				// Save: the autosave goes
				Check(ref ok, DynamicIslands.SaveIsland(AutosaveIsland) && !File.Exists(autosave) && !EditorAutosave.Unsaved, "Save keeps it and removes the autosave");

				// Leaving with a change (the Main menu button): written at once; Throw them away deletes it
				UndoRedoManager.Execute(new NoopCommand());
				EditorAutosave.WriteNow();
				Check(ref ok, File.Exists(autosave), "leaving with unsaved changes writes the autosave at once");
				EditorAutosave.Pick(EditorAutosave.DiscardChoice);
				Check(ref ok, !File.Exists(autosave), "Throw them away deletes it");

				// Saving an island that saved worlds have: said once (not at every save)
				File.WriteAllLines(worldFile, new[] { "# Custom islands in world 'CI Told': name|x|y|z", "@auto=on", AutosaveIsland + "|0|0|0|" });
				int told = DynamicIslands.WorldsNoticeCount;
				DynamicIslands.SaveIsland(AutosaveIsland);
				DynamicIslands.SaveIsland(AutosaveIsland);
				Check(ref ok, DynamicIslands.WorldsNoticeCount == told + 1, "saving an island a saved world has: told once (" + (DynamicIslands.WorldsNoticeCount - told) + " time(s) for two saves)");
			}
			finally
			{
				EditorAutosave.IntervalSeconds = interval;
				foreach (string f in new[] { island, autosave, worldFile }) try { if (File.Exists(f)) File.Delete(f); } catch { }
				if (DynamicIslands.InEditor()) DynamicIslands.NewIsland();
			}
			if (ok) Log("PASS: autosave"); else Fail("autosave");
		}

		[ConsoleCommand(name: "CIPlanEdit", docs: "Dev, anywhere: changes the test plan 'CI Copy Plan' (CIPlanCopyPrep make) as a player would in World Plans: CIPlanEdit add = a 4th rule; CIPlanEdit more = a 5th")]
		public static void PlanEditCommand(string[] args)
		{
			string path = WorldPlan.PathFor(LibPlanCopy);
			if (!File.Exists(path)) { Fail("plan edit: no plan '" + LibPlanCopy + "'"); return; }
			string text = File.ReadAllText(path);
			if (!text.Contains("rule = added")) text += "rule = added | type:sandbar | km:900 | ahead:500 | | Added later\n";
			if (args != null && args.Contains("more") && !text.Contains("rule = more")) text += "rule = more | type:wreck | km:950 | ahead:500 | | More\n";
			File.WriteAllText(path, text);
			WorldPlan p = WorldPlan.Load(LibPlanCopy);
			Log("Plan edit: '" + LibPlanCopy + "' has " + (p != null ? p.Rules.Count : 0) + " rule(s)");
		}

		[ConsoleCommand(name: "CIPlanEditCheck", docs: "Dev, world (host): CIPlanEditCheck edited <rules> = the changed plan file plays (the player was told) with that many rules; CIPlanEditCheck copy <rules> = the world's own copy plays (another player made the world)")]
		public static void PlanEditCheckCommand(string[] args)
		{
			bool ok = true;
			string mode = args != null && args.Length > 0 ? args[0] : "edited";
			int rules = args != null && args.Length > 1 ? int.Parse(args[1]) : 4;
			int count = WorldDirector.Plan != null ? WorldDirector.Plan.Rules.Count : 0;
			Check(ref ok, WorldDirector.PlanName == LibPlanCopy && count == rules, "the world plays '" + WorldDirector.PlanName + "' with " + count + " rule(s) (expected " + rules + ")");
			if (mode == "edited") Check(ref ok, WorldDirector.PlanWasEdited && !WorldDirector.PlanFromWorld && WorldDirector.PlanOwner != 0, "the changed plan file plays (edited " + WorldDirector.PlanWasEdited + ", owner " + WorldDirector.PlanOwner + ")");
			else Check(ref ok, !WorldDirector.PlanWasEdited && WorldDirector.PlanFromWorld, "the world's own copy plays (edited " + WorldDirector.PlanWasEdited + ", from the world " + WorldDirector.PlanFromWorld + ", owner " + WorldDirector.PlanOwner + ")");
			if (ok) Log("PASS: plan edit check"); else Fail("plan edit check");
		}

		[ConsoleCommand(name: "CIPlanOwner", docs: "Dev, world (host): pretends another player made this world (CIPlanOwner <steam id>), saved with the next save")]
		public static void PlanOwnerCommand(string[] args)
		{
			ulong id;
			if (args == null || args.Length == 0 || !ulong.TryParse(args[0], out id)) { Fail("plan owner: CIPlanOwner <steam id>"); return; }
			WorldDirector.PlanOwner = id;
			Log("Plan owner: " + id);
		}

		[ConsoleCommand(name: "CIRollbackMark", docs: "Dev, world (host): leaves the made-up island <mark> out of this world (a change of the mod's state to find after loading): CIRollbackMark [mark]")]
		public static void RollbackMarkCommand(string[] args)
		{
			string mark = args != null && args.Length > 0 ? args[0] : "cirollback";
			WorldIslands.Set(mark, false);
			Log("Rollback mark: '" + mark + "' left out");
		}

		[ConsoleCommand(name: "CIRollbackCheck", docs: "Dev, world (host): CIRollbackCheck saved <mark> = Raft's newest save folder has the mod's copy with Raft's stamp and the island <mark> left out, and an older save's copy doesn't; CIRollbackCheck older <mark> = an older save was loaded: <mark> takes part again; CIRollbackCheck newest <mark> = the newest: <mark> left out")]
		public static void RollbackCheckCommand(string[] args)
		{
			bool ok = true;
			string mode = args != null && args.Length > 0 ? args[0] : "saved";
			string mark = args != null && args.Length > 1 ? args[1] : "cirollback";
			string folder = WorldCopy.RaftWorldFolder;
			if (mode == "saved")
			{
				string[] saves = folder != null ? Directory.GetDirectories(folder) : new string[0];
				string latest = saves.FirstOrDefault(d => d.EndsWith("-Latest"));
				string[] lines = latest != null && File.Exists(Path.Combine(latest, WorldCopy.FileName)) ? File.ReadAllLines(Path.Combine(latest, WorldCopy.FileName)) : new string[0];
				Check(ref ok, lines.Any(l => l.StartsWith("@raftsave=")) && lines.Any(l => l.StartsWith("@islandsoff=") && l.Contains(mark)), "the newest save's folder has the mod's copy with Raft's stamp and '" + mark + "' left out");
				var older = saves.Where(d => d != latest && File.Exists(Path.Combine(d, WorldCopy.FileName))).Select(d => File.ReadAllLines(Path.Combine(d, WorldCopy.FileName))).ToList();
				Check(ref ok, older.Any(o => o.Any(l => l.StartsWith("@raftsave=")) && !o.Any(l => l.StartsWith("@islandsoff=") && l.Contains(mark))), "an older save's folder has its own copy, without '" + mark + "' (" + older.Count + " older copies)");
			}
			else if (mode == "older")
				Check(ref ok, WorldIslands.TakesPart(mark) && WorldCopy.LastOlderSave.Length > 0, "an older save loaded: its state (" + WorldCopy.LastSource + "), '" + mark + "' takes part again");
			else
				Check(ref ok, !WorldIslands.TakesPart(mark) && WorldCopy.LastOlderSave.Length == 0, "the newest save loaded (" + WorldCopy.LastSource + "): '" + mark + "' left out");
			if (ok) Log("PASS: rollback check"); else Fail("rollback check");
		}
	}
}
