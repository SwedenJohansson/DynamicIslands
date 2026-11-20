using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests for the settings fixes of 11-20: the settings a crash keeps (AU3 merge) and the build cost after the mod is unloaded.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CISettingsMerge", docs: "Dev, anywhere: after a crash the world's settings come from the newer copy (AU3) - rare animal looks and the options' seed too, also when the newer copy has none (back to Normal)")]
		public static void SettingsMerge()
		{
			bool ok = true;
			string[] state = { "#head", "@rarelooks=1", "@options=fogbanks", "@optionseed=111", "@lookscaught=5", "@place=a" };
			string[] newer = { "#head2", "@rarelooks=2", "@options=fogbanks,kraken", "@optionseed=222", "@lookscaught=9", "@place=b" };
			string[] m = WorldCopy.Merge(state, newer);
			string all = string.Join(" ", m);
			Check(ref ok, m.Contains("@rarelooks=2") && !m.Contains("@rarelooks=1"), "rare animal looks from the newer copy: " + all);
			Check(ref ok, m.Contains("@optionseed=222") && !m.Contains("@optionseed=111") && m.Contains("@options=fogbanks,kraken"), "the options and their seed from the newer copy: " + all);
			Check(ref ok, m.Contains("@lookscaught=5") && m.Contains("@place=a") && !m.Contains("@place=b"), "progress (looks caught, places) from Raft's save's copy: " + all);
			string[] back = WorldCopy.Merge(state, new[] { "#head2", "@options=fogbanks", "@optionseed=111" });
			Check(ref ok, !back.Any(l => l.StartsWith("@rarelooks=")), "rare looks set back to Normal in the newer copy (no line) stay Normal: " + string.Join(" ", back));
			if (ok) Log("PASS: settings merge"); else Fail("settings merge");
		}

		[ConsoleCommand(name: "CIBuildCostUnload", docs: "Dev, in game (host): the mod's unload step for the build cost - Raft's own amounts come back and stay (the keeper put the world's percent back on 4 times a second), the keeper object is gone; then the build cost works again")]
		public static void BuildCostUnload()
		{
			StartTest(BuildCostUnloadRoutine());
		}

		static IEnumerator BuildCostUnloadRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			int before = BuildCost.Current;
			try
			{
				BuildCost.Set(50);
				for (float t0 = Time.realtimeSinceStartup; BuildCost.Applied != 50 && Time.realtimeSinceStartup - t0 < 10f; ) yield return new WaitForSeconds(0.25f);
				Check(ref ok, BuildCost.Applied == 50 && WrongAmounts(50).Count == 0 && GameObject.Find("CustomIslands_BuildCost") != null, "the world's 50 % is on Raft's numbers, the keeper is there (" + BuildCost.Entries(BuildCost.Items).Count + " amounts)");
				BuildCost.Unload();
				yield return new WaitForSeconds(2f);
				List<string> wrong = WrongAmounts(0);
				Check(ref ok, BuildCost.Applied == 0 && wrong.Count == 0, "2 s after the unload step every amount is still Raft's own" + (wrong.Count > 0 ? " - WRONG " + string.Join(", ", wrong.Take(5).ToArray()) : "") + " (applied " + BuildCost.Applied + ")");
				Check(ref ok, GameObject.Find("CustomIslands_BuildCost") == null, "the keeper object is gone");
				BuildCost.Refresh();
				Check(ref ok, BuildCost.Applied == 0, "a refresh after the unload changes nothing");
			}
			finally
			{
				BuildCost.unloaded = false;
				BuildCost.Set(before);
			}
			for (float t0 = Time.realtimeSinceStartup; BuildCost.Applied != before && Time.realtimeSinceStartup - t0 < 10f; ) yield return new WaitForSeconds(0.25f);
			Check(ref ok, BuildCost.Applied == before && GameObject.Find("CustomIslands_BuildCost") != null, "back to the world's " + BuildCost.Describe(before) + " with a new keeper");
			if (ok) Log("PASS: build cost unload"); else Fail("build cost unload");
		}
	}
}
