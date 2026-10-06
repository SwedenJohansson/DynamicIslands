using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>ROADMAP E9: renaming a saved island, with the worlds, plans and rules that name it.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIRenameIsland", docs: "Dev, editor: ROADMAP E9 - the Islands window's Rename: test islands, a plan, a world file, quest rewards and a spawnpool line naming 'citest-rn-a'; Rename to an existing name and a bad name refused; Rename to 'citest-rn-c' moves the file and its kept copy and changes every place that names it (island:, oneof:, quest of, near), others left alone")]
		public static void RenameIslandCommand(string[] args) { DynamicIslands.instance.StartCoroutine(RenameIslandRoutine()); }

		static IEnumerator RenameIslandRoutine()
		{
			if (!DynamicIslands.InEditor() || IslandFilesWindow.Root == null) { Fail("rename: in the editor"); yield break; }
			bool ok = true;
			const string a = "citest-rn-a", b = "citest-rn-b", c = "citest-rn-c", planName = "CI Rename Plan";
			string worlds = Path.Combine(DynamicIslands.assetpath, "worlds");
			string worldFile = Path.Combine(worlds, "citest-rn-world.txt"), rewardsFile = Path.Combine(worlds, "citest-rn.rewards");
			string pool = Path.Combine(DynamicIslands.assetpath, CustomIslandSpawner.PoolFileName);
			string poolBefore = File.Exists(pool) ? File.ReadAllText(pool) : null;
			string source = IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == "Crowfield Farm") ?? IslandSpawner.ListSavedIslands().First(n => !n.StartsWith("ci") && !IslandNetwork.IsDownloadName(n));
			var made = new List<string>();
			try
			{
				foreach (string n in new[] { a, b, c }) if (File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n));
				File.Copy(IslandSpawner.PathFor(source), IslandSpawner.PathFor(a));
				string hash = IslandNetwork.HashOf(a);
				string copy = IslandNetwork.DownloadName(a, hash);
				File.Copy(IslandSpawner.PathFor(a), IslandSpawner.PathFor(copy), true);
				// b's rules: bring a, one of a, when a's quest is done, near a
				IslandFile fb = IslandFile.Load(IslandSpawner.PathFor(source));
				var rulesB = new List<IntroRule>
				{
					new IntroRule { Id = "rn1", What = "island", WhatArg = a, When = "visit", WhenRef = IntroRule.Self },
					new IntroRule { Id = "rn2", What = "oneof", WhatArg = "x, " + a, When = "quest", WhenRef = a, Where = "near", WhereRef = a },
					new IntroRule { Id = "rn3", What = "island", WhatArg = "citest-rn-other", When = "start" },
				};
				WorldDirector.SetRulesInProps(fb.Props, rulesB);
				fb.Save(IslandSpawner.PathFor(b));
				var plan = new WorldPlan { Name = planName, Random = false, Description = "CIRenameIsland" };
				plan.Rules.Add(new IntroRule { Id = "p1", What = "island", WhatArg = a, When = "start" });
				plan.Rules.Add(new IntroRule { Id = "p2", What = "island", WhatArg = "citest-rn-other", When = "step", WhenRef = a, WhenArg = "2" });
				plan.Save();
				Directory.CreateDirectory(worlds);
				File.WriteAllLines(worldFile, new[] { "# Custom islands in world 'CI Rename World': name|...", "@plan=" + planName, "@planrule=" + plan.Rules[0].ToLine(), a + "|1|2|3||p1||" + hash, "other|0|0|0||||" });
				File.WriteAllLines(rewardsFile, new[] { "rewarded " + a, "owed other" });
				File.WriteAllText(pool, (poolBefore ?? "") + (poolBefore != null && !poolBefore.EndsWith("\n") ? "\r\n" : "") + a + " 2\r\n");
				made.AddRange(new[] { a, b, copy });

				IslandFilesWindow.Open();
				GameObject root = IslandFilesWindow.Root;
				IslandFilesWindow.NameField.text = a;
				Check(ref ok, Click(root, "Rename") && IslandFilesWindow.StatusText.Contains("new name"), "Rename: asks for the new name (" + IslandFilesWindow.StatusText + ")");
				IslandFilesWindow.NameField.text = b;
				Click(root, "Rename");
				Check(ref ok, IslandFilesWindow.StatusText.Contains("already") && File.Exists(IslandSpawner.PathFor(a)), "a name in use is refused (" + IslandFilesWindow.StatusText + ")");
				IslandFilesWindow.NameField.text = "bad,name";
				Click(root, "Rename");
				Check(ref ok, File.Exists(IslandSpawner.PathFor(a)) && !File.Exists(IslandSpawner.PathFor("bad,name")), "a bad name is refused (" + IslandFilesWindow.StatusText + ")");
				IslandFilesWindow.NameField.text = c;
				Click(root, "Rename");
				made.Add(c); made.Add(IslandNetwork.DownloadName(c, hash));
				Check(ref ok, IslandFilesWindow.StatusText.StartsWith("Renamed"), "renamed: " + IslandFilesWindow.StatusText);
				Check(ref ok, !File.Exists(IslandSpawner.PathFor(a)) && File.Exists(IslandSpawner.PathFor(c)) && IslandNetwork.HashOf(c) == hash, "the file has the new name, its content (hash) unchanged");
				Check(ref ok, !File.Exists(IslandSpawner.PathFor(copy)) && File.Exists(IslandSpawner.PathFor(IslandNetwork.DownloadName(c, hash))), "the copy kept for saved worlds goes with it");
				List<IntroRule> rb = WorldDirector.RulesFromProps(IslandFile.Load(IslandSpawner.PathFor(b)).Props);
				Check(ref ok, rb.Count == 3 && rb[0].WhatArg == c && rb[0].WhenRef == IntroRule.Self && rb[1].WhatArg == "x, " + c && rb[1].WhenRef == c && rb[1].WhereRef == c && rb[2].WhatArg == "citest-rn-other",
					"another island's rules follow (island:, oneof:, quest of, near); the others left alone: " + string.Join(" / ", rb.Select(r => r.ToLine()).ToArray()));
				WorldPlan p2 = WorldPlan.Load(planName);
				Check(ref ok, p2 != null && p2.Rules[0].WhatArg == c && p2.Rules[1].WhenRef == c && p2.Rules[1].WhatArg == "citest-rn-other", "the plan follows (island:, a step of it)");
				string[] wl = File.ReadAllLines(worldFile);
				Check(ref ok, wl.Contains(c + "|1|2|3||p1||" + hash) && wl.Contains("other|0|0|0||||") && wl.Any(l => l.StartsWith("@planrule=") && IntroRule.Parse(l.Substring(10)).WhatArg == c),
					"the saved world follows: its island line (state and hash kept) and its kept plan");
				string[] rw = File.ReadAllLines(rewardsFile);
				Check(ref ok, rw.Contains("rewarded " + c) && rw.Contains("owed other"), "its quest reward follows");
				Check(ref ok, File.ReadAllLines(pool).Any(l => l.Trim() == c + " 2") && !File.ReadAllLines(pool).Any(l => l.Trim().StartsWith(a + " ")), "its spawnpool.txt line follows (with its weight)");
				Check(ref ok, IslandSpawner.ListSavedIslands().Contains(c) && !IslandSpawner.ListSavedIslands().Contains(a), "the list shows the new name");
				IslandFilesWindow.Close();
			}
			catch (Exception e) { Check(ref ok, false, "no errors: " + e); }
			finally
			{
				IslandFilesWindow.Close();
				foreach (string n in made) try { if (File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n)); } catch { }
				foreach (string f in new[] { worldFile, rewardsFile, WorldPlan.PathFor(planName) }) try { if (File.Exists(f)) File.Delete(f); } catch { }
				try { if (poolBefore != null) File.WriteAllText(pool, poolBefore); else if (File.Exists(pool)) File.Delete(pool); CustomIslandSpawner.LoadPool(true); } catch { }
			}
			yield return null;
			if (ok) Log("PASS: rename island"); else Fail("rename island");
		}
	}
}
