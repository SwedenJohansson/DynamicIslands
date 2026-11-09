using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIReceiverOtherPlans", docs: "Dev, in game (host, a fresh world): receiver1109 - plans A and B on disk, plan C saved and deleted, islands of all three in the world (A and B share a rule id); with plan A only A's islands and unruled ones show on the Receiver, the others are marked ~id in the world file; an old save without the marks gets them on load; switching to B swaps them")]
		public static void ReceiverOtherPlansCommand(string[] args) { StartTest(ReceiverOtherPlansRoutine()); }

		static IEnumerator ReceiverOtherPlansRoutine()
		{
			Network_Player me = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (me == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			if (!WorldRules.ShowOnReceiver) { Fail("custom islands are hidden on the Receiver (showOnReceiver = 0)"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			string[] names = { "cirxa", "cirxb", "cirxb2", "cirxc", "cirxfree" };
			for (int i = 0; i < names.Length; i++)
			{
				var s = new IslandGenSettings { Seed = 9100 + i, Radius = 20f, Height = 4f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
				IslandGenerator.CreateFile(s, names[i]).Save(IslandSpawner.PathFor(names[i]));
				IslandWorldState.Remove(names[i]);
			}
			const string pa = "CI rx A", pb = "CI rx B", pc = "CI rx C";
			// (A and B both use the id "shelter" - library plans reuse ids too)
			new WorldPlan { Name = pa, Id = WorldPlan.NewId(), Random = true, Rules = { IntroRule.Parse("shelter | island:cirxa | start | ahead:300 | | A Shelter") } }.Save();
			new WorldPlan { Name = pb, Id = WorldPlan.NewId(), Random = true, Rules = { IntroRule.Parse("shelter | island:cirxb | start | ahead:300 | | B Shelter"), IntroRule.Parse("bonly | island:cirxb2 | km:5 | ahead:300 | | B Yard") } }.Save();
			new WorldPlan { Name = pc, Id = WorldPlan.NewId(), Random = true, Rules = { IntroRule.Parse("conly | island:cirxc | start | ahead:300 | | C Hall") } }.Save();
			File.Delete(WorldPlan.PathFor(pc));
			string keep = WorldDirector.PlanName;
			GameObject recv = null;
			var made = new List<IslandWorldState.Entry>();
			try
			{
				// (islands the three plans brought, as a world that played them all has; and a random one)
				float range = WorldRules.ReceiverDistance;
				float step = range > 0f ? Mathf.Min(150f, range / 8f) : 150f;
				Func<string, string, string, int, IslandWorldState.Entry> put = (n, rule, label, k) =>
				{
					IslandWorldState.Entry e = IslandWorldState.Add(n, raft.Value + new Vector3(step * (k + 1), 0f, -step * (k + 1)), null, false);
					e.Rule = rule; e.Label = label; made.Add(e);
					return e;
				};
				IslandWorldState.Entry a = put("cirxa", "shelter", "A Shelter", 0);
				IslandWorldState.Entry b = put("cirxb", "shelter", "B Shelter", 1);
				IslandWorldState.Entry b2 = put("cirxb2", "bonly", "B Yard", 2);
				IslandWorldState.Entry c = put("cirxc", "conly", "C Hall", 3);
				IslandWorldState.Entry free = put("cirxfree", "", "", 4);

				Reciever r = MakeReceiver(me, out recv);
				if (r == null) { Fail("could not find Raft's receiver block"); yield break; }
				Func<string> shown = () => string.Join(", ", IslandRadar.Shown.Where(made.Contains).Select(x => x.Name).ToArray());

				// (the plan chosen with the console, as the GUIDE says: B first, then A)
				DynamicIslands.WorldPlanCommand(new[] { pb });
				DynamicIslands.WorldPlanCommand(new[] { pa });
				Check(ref ok, WorldDirector.PlanName == pa, "the world's plan is A (" + WorldDirector.PlanName + ")");
				Check(ref ok, a.Rule == "shelter" && b.Rule == "~shelter" && b2.Rule == "~bonly" && c.Rule == "~conly" && free.Rule == "",
					"marks: " + string.Join(", ", made.Select(x => x.Name + "=" + x.Rule).ToArray()));
				Check(ref ok, b.Label == "B Shelter", "a marked island keeps its label");
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				Check(ref ok, IslandRadar.Shown.Contains(a) && IslandRadar.Shown.Contains(free) && !IslandRadar.Shown.Contains(b) && !IslandRadar.Shown.Contains(b2) && !IslandRadar.Shown.Contains(c),
					"plan A: only A's island and the random one on the Receiver (" + shown() + ")");
				int at = IslandRadar.Shown.IndexOf(a);
				List<Reciever_Dot> dots = IslandRadar.DotsOf(r);
				Check(ref ok, at >= 0 && at < dots.Count && dots[at] != null && dots[at].dotNumberText != null && dots[at].dotNumberText.text.StartsWith("A Shelter"),
					"A's dot keeps its label (" + (at >= 0 && at < dots.Count && dots[at] != null && dots[at].dotNumberText != null ? dots[at].dotNumberText.text : "none") + ")");

				// (saved: the mark is in the world file)
				IslandWorldState.Save();
				string[] lines = File.ReadAllLines(IslandWorldState.WorldFilePath);
				Check(ref ok, lines.Any(l => l.StartsWith("cirxb|") && l.Split('|').Length > 6 && l.Split('|')[5] == "~shelter" && l.Split('|')[6] == "B Shelter") && lines.Any(l => l.StartsWith("cirxa|") && l.Split('|')[5] == "shelter"),
					"saved: " + string.Join(" / ", lines.Where(l => l.StartsWith("cirx")).Select(l => string.Join("|", l.Split('|').Take(1).Concat(l.Split('|').Skip(5).Take(2)).ToArray())).ToArray()));
				Check(ref ok, WorldDirector.MarkOtherPlans().Count == 0, "marking again changes nothing");

				// (an old 3.1/3.2 save: the same islands without marks - what a world load does marks them)
				b.Rule = "shelter"; b2.Rule = "bonly"; c.Rule = "conly";
				List<IslandWorldState.Entry> changed = WorldDirector.MarkOtherPlans();
				Check(ref ok, changed.Count == 3 && b.Rule == "~shelter" && b2.Rule == "~bonly" && c.Rule == "~conly" && a.Rule == "shelter", "an old save's extra islands are marked on load (" + changed.Count + ")");

				// (back to plan B: B's islands come back on the Receiver, A's leave)
				DynamicIslands.WorldPlanCommand(new[] { pb });
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				Check(ref ok, a.Rule == "~shelter" && b.Rule == "shelter" && b2.Rule == "bonly" && c.Rule == "~conly", "plan B marks: " + string.Join(", ", made.Select(x => x.Name + "=" + x.Rule).ToArray()));
				Check(ref ok, !IslandRadar.Shown.Contains(a) && IslandRadar.Shown.Contains(b) && IslandRadar.Shown.Contains(b2) && !IslandRadar.Shown.Contains(c) && IslandRadar.Shown.Contains(free),
					"plan B: B's islands and the random one (" + shown() + ")");
			}
			finally
			{
				if (recv != null) UnityEngine.Object.Destroy(recv);
				if (!string.IsNullOrEmpty(keep) && WorldPlan.Load(keep) != null) { WorldDirector.SetPlan(keep, true); StoryChain.FromPlan(WorldDirector.Plan); }
				foreach (string n in names) IslandWorldState.Remove(n);
				foreach (string p in new[] { pa, pb, pc }) if (File.Exists(WorldPlan.PathFor(p))) File.Delete(WorldPlan.PathFor(p));
				IslandWorldState.Save();
			}
			if (ok) Log("PASS: receiver other plans"); else Fail("receiver other plans");
		}
	}
}
