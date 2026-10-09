using System;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>Raft's progression in the plans (the user, 2026-10-05): the plan Check names every blueprint of Raft's
		/// story islands a plan never gives. The library's plans give them all (their own islands or the story islands they
		/// keep); a plan with Raft's story off and no islands is told about every one.</summary>
		[ConsoleCommand(name: "CIPlanBlueprints", docs: "Dev, anywhere: the plan Check's blueprint findings for the library's plans (installed), and for a plan with Raft's story off and no islands")]
		public static void PlanBlueprints(string[] args)
		{
			bool ok = true;
			ScrambledBlueprints.Read();
			int all = ScrambledBlueprints.OnIslands.Count;
			Check(ref ok, all >= 20, "Raft's story islands' blueprints known: " + all);
			foreach (string name in new[] { "Raft 2 - The Drowned Frontier", "Raft Remade", "The Long Voyage", "Silver Screen Seas", "The Abyss Expedition" })
			{
				WorldPlan p = WorldPlan.Load(name);
				if (p == null) { Log("  (plan '" + name + "' isn't installed - skipped)"); continue; }
				List<PlanChecker.Finding> f = PlanChecker.Check(p, false, false);
				PlanChecker.Finding miss = f.FirstOrDefault(x => x.Text.StartsWith("Raft's blueprints never given"));
				PlanChecker.Finding tip = f.FirstOrDefault(x => x.Text.StartsWith("Raft's blueprints given by the plan's islands"));
				Check(ref ok, miss == null, "'" + name + "' gives every one of Raft's blueprints" + (miss != null ? " - " + miss.Text : ""));
				if (tip != null) Log("  " + tip.Text);
				// (the plan editor's Progression panel shows the same, rule by rule)
				PlanChecker.Progress pr = PlanChecker.Progression(p, false);
				Check(ref ok, pr.Missing.Count == 0 && pr.Kept.Count + pr.Steps.Sum(s => s.Blueprints.Count) >= all,
					"'" + name + "' progression: " + pr.Kept.Count + " kept, " + pr.Steps.Count(s => s.Blueprints.Count > 0) + " rules give blueprints, " + pr.Missing.Count + " never given, " +
					pr.Steps.Sum(s => s.NeedsLate.Count) + " locks before their item");
				PlanProgressWindow.Open(p, null);
				if (PlanProgressWindow.IsOpen)
				{
					Check(ref ok, PlanProgressWindow.Rows.Count == pr.Steps.Count + (pr.Kept.Count > 0 ? 1 : 0) && !PlanProgressWindow.Rows.Any(r => r.StartsWith("NEVER GIVEN")),
						"the Progression panel shows " + PlanProgressWindow.Rows.Count + " rows: " + string.Join(" / ", PlanProgressWindow.Rows.Take(3).Select(r => r.Length > 90 ? r.Substring(0, 90) + "..." : r).ToArray()));
					PlanProgressWindow.Close();
				}
			}
			var bare = new WorldPlan { Name = "CI no story", Random = true, RaftStory = false };
			PlanChecker.Finding none = PlanChecker.Check(bare, false, false).FirstOrDefault(x => x.Text.StartsWith("Raft's blueprints never given"));
			Check(ref ok, none != null && ScrambledBlueprints.OnIslands.Keys.All(b => none.Text.Contains(b.Replace("Blueprint_", "").Replace("_", " "))),
				"a plan with Raft's story off and no islands is told about all " + all + " blueprints");
			PlanChecker.Progress bp = PlanChecker.Progression(bare, false);
			Check(ref ok, bp.Missing.Count == all && bp.Kept.Count == 0 && bp.Steps.Count == 0, "its progression: " + bp.Missing.Count + " never given, " + bp.Kept.Count + " kept");
			PlanProgressWindow.Open(bare, null);
			if (PlanProgressWindow.IsOpen)
			{
				Check(ref ok, PlanProgressWindow.Rows.Count == 1 && PlanProgressWindow.Rows[0].StartsWith("NEVER GIVEN"), "the Progression panel shows it never given: " + string.Join(" / ", PlanProgressWindow.Rows.ToArray()).Substring(0, Math.Min(120, string.Join(" / ", PlanProgressWindow.Rows.ToArray()).Length)));
				PlanProgressWindow.Close();
			}
			else Log("  (the Progression panel isn't built here: run in the editor to check it too)");
			if (ok) Log("PASS: plan blueprints"); else Fail("plan blueprints");
		}
	}
}
