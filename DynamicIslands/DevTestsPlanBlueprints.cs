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
			}
			var bare = new WorldPlan { Name = "CI no story", Random = true, RaftStory = false };
			PlanChecker.Finding none = PlanChecker.Check(bare, false, false).FirstOrDefault(x => x.Text.StartsWith("Raft's blueprints never given"));
			Check(ref ok, none != null && ScrambledBlueprints.OnIslands.Keys.All(b => none.Text.Contains(b.Replace("Blueprint_", "").Replace("_", " "))),
				"a plan with Raft's story off and no islands is told about all " + all + " blueprints");
			if (ok) Log("PASS: plan blueprints"); else Fail("plan blueprints");
		}
	}
}
