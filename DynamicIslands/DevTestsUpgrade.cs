using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIUpgradeHeal", docs: "Dev, in game (host, a test world 'CI ...'): a world saved by a build before 10-31 heals on load - a Receiver rule whose island is gone and a hand-spawned (SpawnIsland) copy is there adopts the copy (no new island); one with no island is brought again; a done story step is left; a quest stuck at 'open <the Vault>' with the Vault already open goes on (and stops at a chest not opened)")]
		public static void UpgradeHealCommand(string[] args) { StartTest(UpgradeHealRoutine()); }

		static IEnumerator UpgradeHealRoutine()
		{
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (!raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("only in a test world 'CI ...' (it changes the story chain for a moment)"); yield break; }
			yield return EnsureAlive();
			bool ok = true;

			// Part 1: the story chain's state as an old build left it (no frame in between: the chain's tick never sees it)
			const string nameA = "ciupgrade-a", nameB = "ciupgrade-b", nameC = "ciupgrade-c", nameD = "ciupgrade-d", nameE = "ciupgrade-e";
			IslandFile f = SampleIsland();
			f.Props[IslandQuest.KeyTitle] = "Upgrade";
			f.Props[IslandQuest.KeySteps] = "reach|cizone|1|\nread|CI note|1|";
			foreach (string n in new[] { nameA, nameB, nameC, nameD, nameE }) { f.Name = n; f.Save(IslandSpawner.PathFor(n)); }
			var rules = new List<IntroRule>
			{
				new IntroRule { Id = "ciupa", What = "island", WhatArg = nameA, When = "start", Where = "receiver" },          // gone, copies spawned by hand
				new IntroRule { Id = "ciupb", What = "island", WhatArg = nameB, When = "start", Where = "receiver" },          // gone, nothing left
				new IntroRule { Id = "ciupc", What = "island", WhatArg = nameC, When = "start", Where = "receiver", StoryPlace = "after:ciupx" }, // story step done
				new IntroRule { Id = "ciupd", What = "island", WhatArg = nameD, When = "start", Where = "receiver", StoryPlace = "after:ciupx" }, // story step open
				new IntroRule { Id = "ciupe", What = "island", WhatArg = nameE, When = "start", Where = "receiver" },          // fired, not tuned yet
			};
			var made = new List<IslandWorldState.Entry>();
			Vector3 far = raft.Value + new Vector3(40000f, 0f, 40000f);
			far.y = 0f;
			try
			{
				foreach (IntroRule r in rules) { StoryChain.Rules.Add(r); StoryChain.Fired.Add(r.Id); StoryChain.Brought.Add(r.Id); }
				StoryChain.Done.Add(StoryChain.RuleKey("ciupc"));
				StoryChain.Brought.Remove("ciupe");
				IslandWorldState.Entry e1 = IslandWorldState.Add(nameE, far + new Vector3(0f, 0f, 2000f), null, false); // (never visited)
				made.Add(e1);
				// (two copies of A spawned by hand, rule-less: the second one played to step 1)
				IslandWorldState.Entry a1 = IslandWorldState.Add(nameA, far, null, false), a2 = IslandWorldState.Add(nameA, far + new Vector3(2000f, 0f, 0f), null, false);
				made.Add(a1); made.Add(a2);
				a2.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
				QuestTracker.Set(a2, 1, 0, false);
				int before = IslandWorldState.Islands.Count;

				StoryUpgrade.Run();
				Log("  repair: " + string.Join("; ", StoryUpgrade.Report.Where(l => l.Contains("ciup")).ToArray()));
				Check(ref ok, a2.Rule == "ciupa" && a1.Rule == "", "the copy played furthest is the rule's island now (" + (a1.Rule == "" ? "-" : a1.Rule) + " / " + (a2.Rule == "" ? "-" : a2.Rule) + ")");
				Check(ref ok, StoryChain.Brought.Contains("ciupa") && QuestTracker.StepOf(a2) == 1, "it stays brought, its quest as played (step " + QuestTracker.StepOf(a2) + ")");
				Check(ref ok, IslandWorldState.Islands.Count == before, "no island was added (" + before + " -> " + IslandWorldState.Islands.Count + ")");
				Check(ref ok, !StoryChain.Brought.Contains("ciupb") && StoryChain.Fired.Contains("ciupb"), "a Receiver rule with no island left is brought again when tuned (not brought, still fired)");
				Check(ref ok, StoryChain.Brought.Contains("ciupc"), "a done story step whose island is gone is left as it is (nothing comes twice)");
				Check(ref ok, !StoryChain.Brought.Contains("ciupd"), "an open story step whose island is gone is brought again");
				Check(ref ok, e1.Rule == "" && !StoryChain.Brought.Contains("ciupe"), "a rule not tuned yet leaves an unvisited copy alone (it comes as usual)");
				Check(ref ok, WorldDirector.Refs("ciupa", null).Count == 1 && WorldDirector.Refs("ciupa", null)[0] == a2, "the rule finds its island (the journal and notebook read it)");
				StoryUpgrade.Run();
				Check(ref ok, !StoryUpgrade.Report.Any(l => l.Contains("ciup")), "run again it finds nothing to do");
			}
			finally
			{
				foreach (IntroRule r in rules)
				{
					StoryChain.Rules.Remove(r); StoryChain.Fired.Remove(r.Id); StoryChain.Brought.Remove(r.Id); StoryChain.Done.Remove(StoryChain.RuleKey(r.Id));
				}
				if (made.Count > 0) IslandWorldState.RemoveIds(made.Select(e => e.Id).ToArray(), false);
				foreach (string n in new[] { nameA, nameB, nameC, nameD, nameE }) try { System.IO.File.Delete(IslandSpawner.PathFor(n)); } catch { }
			}

			// Part 2: a quest stuck at "open the Vault" with the Vault open already (an old build gave it to an "any chest" step)
			IslandFile v = PlayerTestIsland((file, add) =>
			{
				add("Loot_Chest", 6f, 0f, new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*1" }, { ObjectProps.NoteTitle, "Vault" }, { ObjectProps.NoteText, "The vault." } });
				add("Loot_Chest", -6f, 0f, new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*1" }, { ObjectProps.NoteTitle, "Box" }, { ObjectProps.NoteText, "A box." } });
				add("Loot_Chest", 0f, 6f, new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*1" }, { ObjectProps.NoteTitle, "Crate" }, { ObjectProps.NoteText, "A crate." } });
				file.Props[IslandQuest.KeyTitle] = "Vault order";
				file.Props[IslandQuest.KeySteps] = "open||1|Open any chest\nopen|Vault|1|Open the vault\nopen|Box|1|Open the box";
			});
			IslandWorldState.Entry e2 = null;
			yield return SpawnPlayerIsland(v, x => e2 = x);
			try
			{
				for (float t = 0f; t < 20f && (e2 == null || e2.Root == null); t += 0.5f) yield return new WaitForSeconds(0.5f);
				if (e2 == null || e2.Root == null) { Fail("the vault island did not come"); yield break; }
				Func<string, LootCrate> crate = title => e2.Root.GetComponentsInChildren<LootCrate>(true).FirstOrDefault(c => c.GetComponent<CustomNote>() != null && c.GetComponent<CustomNote>().Title == title);
				LootCrate vault = crate("Vault"), box = crate("Box");
				if (vault == null || box == null || crate("Crate") == null) { Fail("the chests aren't there with their titles"); yield break; }
				Check(ref ok, QuestTracker.StepOf(e2) == 0, "the quest starts at 'open any chest' (step " + QuestTracker.StepOf(e2) + ")");
				// (the old save: the Vault opened already, its loot had)
				e2.State[vault.StateKey] = new ObjectState { Active = false, Yield = 0, Day = 0 };
				QuestTracker.Event(e2, "open", "Crate");
				Check(ref ok, QuestTracker.StepOf(e2) == 2, "another chest opened: the step 'open the Vault', its chest open already, goes on at once - and it waits at the box not opened (step " + QuestTracker.StepOf(e2) + " of 3)");
				QuestTracker.HealSpentChests(e2);
				Check(ref ok, QuestTracker.StepOf(e2) == 2, "on load again: still at the box (step " + QuestTracker.StepOf(e2) + ")");
				// (a save stuck at the box with it open: loading the island heals it)
				e2.State[box.StateKey] = new ObjectState { Active = false, Yield = 0, Day = 0 };
				QuestTracker.HealSpentChests(e2);
				Check(ref ok, QuestTracker.StepOf(e2) == 3, "loaded with the box open already: the quest is done (step " + QuestTracker.StepOf(e2) + " of 3)");
			}
			finally { RemovePlayerIsland(e2); }
			if (ok) Log("PASS: upgrade heal"); else Fail("upgrade heal");
		}
	}
}
