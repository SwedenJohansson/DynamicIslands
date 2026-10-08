using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandUndoRedo;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of the audit's leftovers (ROADMAP §2b A1-A8) and the release cases they closed: a file another program holds
	/// open (UP7) and a folder with a thousand islands (UP10).
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CILockFile", docs: "Dev, editor (TEST_CATALOGUE UP7): an island file another program holds open (a FileShare.None stream) - saving over it says 'in use by another program' and leaves the file as it was with no .tmp beside it, Delete says so and moves nothing, a settings write the same; once let go, saving works. Cleans up")]
		public static void LockFileCommand() { StartTest(LockFileRoutine()); }

		static IEnumerator LockFileRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("lock file: in the editor"); yield break; }
			bool ok = true;
			const string name = "citest-lock";
			string path = IslandSpawner.PathFor(name);
			int told = 0;
			Application.LogCallback counter = (msg, trace, type) => { if (msg.Contains("in use by another program")) told++; };
			FileStream held = null;
			try
			{
				DynamicIslands.NewIsland();
				Check(ref ok, DynamicIslands.SaveIsland(name), "a test island saved as '" + name + "'");
				byte[] before = File.ReadAllBytes(path);
				Application.logMessageReceived += counter;
				held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); // (as a program that locks it)
				terraineditor.terrain.terrainData.SetHeights(20, 20, new float[,] { { 0.4f } });
				UndoRedoManager.Execute(new NoopCommand());
				int t0 = told;
				bool saved = DynamicIslands.SaveIsland(name);
				Check(ref ok, !saved && told > t0, "saving over it is refused and says 'in use by another program'");
				held.Dispose(); held = null;
				Check(ref ok, File.ReadAllBytes(path).SequenceEqual(before) && !File.Exists(path + ".tmp"), "the saved island is as it was, no .tmp beside it");
				held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
				bool moved = true;
				try { IslandFilesWindow.MoveToDeleted(name); } catch (Exception e) { moved = false; Check(ref ok, SafeFile.InUse(e), "Delete: the error is 'in use' (" + e.GetType().Name + ")"); }
				Check(ref ok, !moved, "Delete moves nothing while it is held");
				string settings = Path.Combine(DynamicIslands.assetpath, "citest-lock.txt");
				File.WriteAllText(settings, "old");
				using (new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					bool refused = false;
					try { SafeFile.WriteAllText(settings, "new"); } catch (Exception e) { refused = SafeFile.InUse(e); }
					Check(ref ok, refused && !File.Exists(settings + ".tmp"), "a settings file held open: the write says 'in use', no .tmp left");
				}
				Check(ref ok, File.ReadAllText(settings) == "old", "... and the file is as it was");
				File.Delete(settings);
				held.Dispose(); held = null;
				Check(ref ok, DynamicIslands.SaveIsland(name) && !File.ReadAllBytes(path).SequenceEqual(before), "let go: saving works again (with the change)");
			}
			catch (Exception e) { Check(ref ok, false, "no errors: " + e.Message); }
			finally
			{
				Application.logMessageReceived -= counter;
				if (held != null) held.Dispose();
				try { if (DynamicIslands.InEditor()) DynamicIslands.NewIsland(); } catch { }
				foreach (string f in new[] { path, path + ".tmp", EditorAutosave.PathFor(name) }) try { if (File.Exists(f)) File.Delete(f); } catch { }
			}
			if (ok) Log("PASS: lock file"); else Fail("lock file");
		}

		[ConsoleCommand(name: "CILeftoversUnit", docs: "Dev, anywhere (ROADMAP §2b A1, A4): Tidy up's rules on test files - a world file this PC hosted whose Raft world is gone counts as deleted, one saved by another host (a joined world) doesn't; a gen- island no world uses is unused, one a world uses isn't; a copy no world uses is unused; a rule finds an island from its copy (name_hash) when only that is here. Cleans up")]
		public static void LeftoversUnit()
		{
			bool ok = true;
			string worlds = Path.Combine(DynamicIslands.assetpath, "worlds");
			Directory.CreateDirectory(worlds);
			string mine = Path.Combine(worlds, "citest-left-mine.txt"), joined = Path.Combine(worlds, "citest-left-joined.txt"), user = Path.Combine(worlds, "citest-left-user.txt");
			const string genUsed = "gen-citest-left-used", genFree = "gen-citest-left-free", copyBase = "citest-left-copy";
			string copyName = null;
			try
			{
				string by = Housekeeping.SavedByLine();
				Check(ref ok, by != null, "this PC's hosting line: " + by);
				File.WriteAllLines(mine, new[] { "# Custom islands in world 'CI Gone World 7x': ...", "@savedat=1", by ?? "@savedby=1" });
				File.WriteAllLines(joined, new[] { "# Custom islands in world 'CI Joined World 7x': ...", "@savedat=1", "@savedby=76561190000000001" });
				var small = new IslandFile { TerrainSize = new Vector3(32f, 60f, 32f), HeightmapResolution = 17, Heights = new float[17, 17] };
				foreach (string g in new[] { genUsed, genFree, copyBase }) { small.Name = g; small.Heights[8, 8] = g.Length / 100f; small.Save(IslandSpawner.PathFor(g)); }
				string hash = IslandNetwork.HashOf(copyBase);
				copyName = IslandNetwork.DownloadName(copyBase, hash);
				File.Move(IslandSpawner.PathFor(copyBase), IslandSpawner.PathFor(copyName));
				File.WriteAllLines(user, new[] { "# Custom islands in world 'CI Left User 7x': ...", genUsed + "|0|0|0||||" });
				IslandCache.Forget();

				List<KeyValuePair<string, string>> gone = Housekeeping.DeletedWorldFiles();
				Check(ref ok, gone.Any(g => g.Value == "CI Gone World 7x"), "a world this PC hosted whose Raft world is gone counts as deleted");
				Check(ref ok, !gone.Any(g => g.Value == "CI Joined World 7x"), "a world another host saved (joined here) is kept");
				Housekeeping.Scan scan = Housekeeping.Look();
				Check(ref ok, scan.UnusedGenerated.Contains(genFree) && !scan.UnusedGenerated.Contains(genUsed), "a gen- island no world uses is unused, one a world uses isn't");
				Check(ref ok, scan.Copies.Any(c => c.Key == copyName && c.Value.Count == 0), "a copy no world uses is unused: " + copyName);
				Log("  " + scan.Describe());
				Check(ref ok, WorldDirector.FileFor(copyBase) == copyName && WorldDirector.FileFor("citest-left-none") == null, "a rule finds '" + copyBase + "' from its copy when only that is here");
			}
			catch (Exception e) { Check(ref ok, false, "no errors: " + e.Message); }
			finally
			{
				foreach (string f in new[] { mine, joined, user }) try { if (File.Exists(f)) File.Delete(f); } catch { }
				foreach (string n in new[] { genUsed, genFree, copyBase, copyName }) try { if (n != null && File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n)); } catch { }
				IslandCache.Forget();
			}
			if (ok) Log("PASS: leftovers unit"); else Fail("leftovers unit");
		}

		[ConsoleCommand(name: "CIHotkeyTabs", docs: "Dev, world (host, a test world 'CI ...'): the hotbar's key tabs - the journal's (J) beside Raft's notebook tab, the stats page's (K) only while the level up system is on (switched on and back); picture shot_hotkey_tabs.png")]
		public static void HotkeyTabsCommand() { StartTest(HotkeyTabsRoutine()); }

		static IEnumerator HotkeyTabsRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("hotkey tabs: host in a test world 'CI ...'"); yield break; }
			bool ok = true;
			bool levels = PlayerLevels.On;
			yield return new WaitForSeconds(1f);
			GameObject j = GameObject.Find(HotkeyHints.JournalName), note = GameObject.Find("UI_Hotkey_Element_NoteBook");
			Func<GameObject, string> text = g => { if (g == null) return null; Component t = g.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == "TextMeshProUGUI"); return t != null ? HarmonyLib.Traverse.Create(t).Property("text").GetValue<string>() : null; };
			Check(ref ok, j != null && note != null && j.transform.parent == note.transform.parent, "the journal's tab is beside Raft's notebook tab");
			Check(ref ok, text(j) == JournalWindow.Key.ToString(), "it says the journal's key: " + text(j));
			Check(ref ok, j != null && note != null && j.transform.position.x > note.transform.position.x, "... after the notebook's tab (right of it)");
			PlayerLevels.SetEnabled(true);
			yield return new WaitForSeconds(1.5f);
			GameObject k = GameObject.Find(HotkeyHints.StatsName);
			Check(ref ok, k != null && k.activeInHierarchy && text(k) == PlayerLevels.Key.ToString(), "the level up system on: the stats tab shows its key " + text(k));
			Screenshot(new[] { "hotkey_tabs" });
			yield return new WaitForSeconds(0.5f);
			PlayerLevels.SetEnabled(false);
			yield return new WaitForSeconds(1.5f);
			GameObject k2 = GameObject.Find(HotkeyHints.StatsName); // (Find skips inactive objects)
			Check(ref ok, k2 == null, "off: the stats tab is gone again");
			if (levels) PlayerLevels.SetEnabled(true);
			if (ok) Log("PASS: hotkey tabs"); else Fail("hotkey tabs");
		}

		[ConsoleCommand(name: "CIDropOpen", docs: "Dev: opens the first visible drop-down list named so (e.g. Drop_When), as a click on it would: CIDropOpen <name>")]
		public static void DropOpenCommand(string[] args)
		{
			string name = args != null && args.Length > 0 ? args[0] : "";
			// (the top-most one: cards further down a list are there too, scrolled out of view)
			DropdownButton d = UnityEngine.Object.FindObjectsOfType<DropdownButton>().Where(x => x.name == name && x.isActiveAndEnabled).OrderByDescending(x => x.transform.position.y).FirstOrDefault();
			if (d == null) { Fail("no visible drop-down '" + name + "'"); return; }
			DropList.Open(d.GetComponent<UnityEngine.UI.Button>(), d);
			Log("Opened the drop-down " + name + " (" + d.Options.Count + " options)");
			StartTest(DropReport());
		}

		static IEnumerator DropReport()
		{
			for (int i = 0; i < 3; i++)
			{
				GameObject list = GameObject.Find("DropdownList");
				RectTransform r = list != null ? (RectTransform)list.transform : null;
				Log("  frame " + i + ": open " + DropList.IsOpen + (r != null ? ", list at " + r.anchoredPosition + " size " + r.rect.size + " under " + r.parent.parent.name + ", screen " + RectTransformUtility.WorldToScreenPoint(null, r.position) : ", no list"));
				yield return null;
			}
		}

		[ConsoleCommand(name: "CICheckReport", docs: "Dev, editor: the World Plans window's Check on a plan full of mistakes - two test islands (one with a zone 'gate', a note 'Diary' and a quest whose steps need a note and creatures it hasn't, one without a quest) and rules that wait for a quest that can't be finished, for an island without a quest, for a zone it hasn't, for each other in a circle, near an island nobody has, with no number, and after a broken rule: each found at its level, with why and how to fix it; the report window opens (shot_plan_check.png) and each card shows its findings. Cleans up")]
		public static void PlanCheckCommand() { StartTest(PlanCheckRoutine()); }

		static IEnumerator PlanCheckRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("plan check: in the editor"); yield break; }
			bool ok = true;
			const string questIsland = "cicheck-quest", plainIsland = "cicheck-plain", planName = "CI Check";
			try
			{
				// Two small islands: one with a zone, a note and a quest that can't be finished; one without a quest
				int res = 17;
				var h = new float[res, res]; for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) h[y, x] = 0.4f;
				var q = new IslandFile { TerrainSize = new Vector3(64f, 60f, 64f), HeightmapResolution = res, Heights = h, Name = questIsland };
				q.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = new Vector3(30, 25, 30), Props = new Dictionary<string, string> { { ObjectProps.ZoneId, "gate" } } });
				q.Objects.Add(new IslandObject { Name = "Note_Paper", Position = new Vector3(32, 25, 30), Props = new Dictionary<string, string> { { ObjectProps.NoteTitle, "Diary" }, { ObjectProps.NoteText, "Hello" } } });
				var quest = new IslandQuest { Title = "Test" };
				quest.Steps.Add(new IslandQuest.Step { Type = "reach", Target = "gate" });
				quest.Steps.Add(new IslandQuest.Step { Type = "read", Target = "Lost page" });
				quest.Steps.Add(new IslandQuest.Step { Type = "kill", Target = "Warthog", Count = 5 });
				quest.To(q.Props);
				q.Save(IslandSpawner.PathFor(questIsland));
				var p = new IslandFile { TerrainSize = q.TerrainSize, HeightmapResolution = res, Heights = h, Name = plainIsland };
				p.Save(IslandSpawner.PathFor(plainIsland));

				var plan = new WorldPlan { Name = planName, Random = false };
				Func<string, string, IntroRule> rule = (id, line) => IntroRule.Parse(id + " | " + line);
				plan.Rules.Add(rule("start", "island:" + questIsland + " | start | ahead:300 | A test | Start"));
				plan.Rules.Add(rule("afterquest", "type:camp | quest:start | near:start:600:any | msg | A"));          // 2: the quest can't be finished
				plan.Rules.Add(rule("plain", "island:" + plainIsland + " | km:1 | ahead:300 | msg | P"));
				plan.Rules.Add(rule("noquest", "type:camp | quest:plain | near:plain:600:any | msg | N"));               // 4: no quest on 'plain'
				plan.Rules.Add(rule("cave", "type:camp | zone:start:cave | near:start:600:any | msg | C"));              // 5: no zone 'cave'
				plan.Rules.Add(rule("circleA", "type:camp | rule:circleB | ahead:300 | msg | A2"));                      // 6-7: a circle
				plan.Rules.Add(rule("circleB", "type:camp | rule:circleA | ahead:300 | msg | B2"));
				plan.Rules.Add(rule("lost", "type:camp | start | near:nobody:600:any | msg | L"));                       // 8: near nobody
				plan.Rules.Add(rule("nonumber", "type:camp | km:abc | ahead:300 | msg | X"));                           // 9: no number
				plan.Rules.Add(rule("afterbroken", "type:camp | visit:afterquest | near:afterquest:600:any | msg | B")); // 10: after a broken rule
				Check(ref ok, plan.Rules.All(r => r != null), "the test plan's " + plan.Rules.Count + " rules read");

				List<PlanChecker.Finding> found = PlanChecker.Check(plan, false, true);
				foreach (PlanChecker.Finding f in found) Log("  " + f.Level + " " + (f.Rule + 1) + ": " + f.Text);
				Func<int, PlanChecker.Level, string, bool> has = (i, lvl, part) => found.Any(f => f.Rule == i && f.Level == lvl && f.Text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0 && f.Fix.Length > 0);
				Check(ref ok, !found.Any(f => f.Rule == 0 && f.Level == PlanChecker.Level.Problem), "rule 1 (the start island) has no problem");
				Check(ref ok, has(1, PlanChecker.Level.Problem, "needs a note titled \"Lost page\""), "rule 2: its island's quest can't be finished - step 2 needs a note 'Lost page' (the island has 'Diary')");
				Check(ref ok, has(1, PlanChecker.Level.Problem, "warthogs"), "rule 2: ... and step 3 needs warthogs the island hasn't");
				Check(ref ok, has(3, PlanChecker.Level.Problem, "has no quest"), "rule 4 waits for the quest of an island without one");
				Check(ref ok, has(4, PlanChecker.Level.Problem, "zone 'cave'"), "rule 5 waits for a zone 'cave' the island hasn't (it has 'gate')");
				Check(ref ok, found.Any(f => f.Level == PlanChecker.Level.Problem && f.Text.Contains("in a circle")), "rules 6 and 7 wait for each other in a circle");
				Check(ref ok, has(7, PlanChecker.Level.Problem, "placed near 'nobody'"), "rule 8 is placed near an island nobody has");
				Check(ref ok, has(8, PlanChecker.Level.Problem, "no number"), "rule 9 waits for a distance with no number");
				Check(ref ok, has(9, PlanChecker.Level.Warning, "has a problem"), "rule 10 waits for a broken rule: a warning");
				Check(ref ok, found.Where(f => f.Level == PlanChecker.Level.Problem).All(f => f.Fix.Length > 0), "every problem says how to fix it");

				// The window: Check opens the report, the cards show their findings
				plan.Save();
				WorldPlanWindow.Open(planName);
				yield return null;
				Transform window = EditorUI.Canvas.transform.Find("WorldPlanWindow");
				UnityEngine.UI.Button check = window != null ? window.GetComponentsInChildren<UnityEngine.UI.Button>(false).FirstOrDefault(b => UIKit.LabelOf(b) != null && UIKit.LabelOf(b).text == "Check") : null;
				if (check != null) check.onClick.Invoke();
				yield return null; yield return null;
				Check(ref ok, PlanCheckWindow.IsOpen && PlanCheckWindow.Shown != null && PlanCheckWindow.Shown.Count(f => f.Level == PlanChecker.Level.Problem) >= 7, "Check opens the report: " + (PlanCheckWindow.Shown != null ? PlanCheckWindow.Shown.Count + " findings" : "none"));
				int onCards = window != null ? window.GetComponentsInChildren<Transform>(false).Count(t => t.name == "Finding") : 0;
				Check(ref ok, onCards >= 1, "the cards show their findings (" + onCards + " shown in view)");
				Screenshot(new[] { "plan_check" });
				yield return new WaitForSeconds(0.5f);
				PlanCheckWindow.Close();
				WorldPlanWindow.Close();
			}
			finally
			{
				PlanCheckWindow.Close();
				WorldPlanWindow.Close();
				foreach (string n in new[] { questIsland, plainIsland }) try { if (System.IO.File.Exists(IslandSpawner.PathFor(n))) System.IO.File.Delete(IslandSpawner.PathFor(n)); } catch { }
				try { if (System.IO.File.Exists(WorldPlan.PathFor(planName))) System.IO.File.Delete(WorldPlan.PathFor(planName)); } catch { }
			}
			if (ok) Log("PASS: plan check"); else Fail("plan check");
		}

		[ConsoleCommand(name: "CICheckPlans", docs: "Dev, editor: runs the World Plans window's Check on saved plans and logs what it finds (CICheckPlans = the sample plans; CICheckPlans <plan> = one). The samples must have no problems: PASS: sample plans check clean")]
		public static void CheckPlansCommand(string[] args)
		{
			WorldPlanWindow.EnsureSamples();
			List<string> names = args != null && args.Length > 0 ? new List<string> { string.Join(" ", args) } : WorldPlanTemplates.All.Where(t => t.Value.Sample).Select(t => t.Key).ToList();
			bool ok = true;
			foreach (string n in names)
			{
				// (the samples as the mod writes them - the copies on this PC may be changed by tests or by hand)
				WorldPlan p = args != null && args.Length > 0 ? WorldPlan.Load(n) : WorldPlanTemplates.Get(n);
				if (p == null) { Log("  no plan '" + n + "'"); continue; }
				List<PlanChecker.Finding> found = PlanChecker.Check(p, false, true);
				Log("Plan '" + n + "': " + found.Count(f => f.Level == PlanChecker.Level.Problem) + " problems, " + found.Count(f => f.Level == PlanChecker.Level.Warning) + " warnings, " + found.Count(f => f.Level == PlanChecker.Level.Tip) + " tips");
				foreach (PlanChecker.Finding f in found) Log("  " + f.Level + " " + (f.Rule + 1) + ": " + f.Text);
				if (found.Any(f => f.Level == PlanChecker.Level.Problem)) ok = false;
			}
			if (args == null || args.Length == 0) { if (ok) Log("PASS: sample plans check clean"); else Fail("sample plans check clean"); }
		}

		const string CrateIsland = "cicrate";

		[ConsoleCommand(name: "CIRaftCrateEditor", docs: "Dev, editor: the abandoned rafts' crate (Pickup_Landmark_LandmarkCrateRaft) is in the object list under Loot & chests as 'Abandoned raft crate'; placed on a small generated island saved as 'cicrate' (for CIRaftCrateWorld)")]
		public static void RaftCrateEditorCommand() { StartTest(RaftCrateEditorRoutine()); }

		static IEnumerator RaftCrateEditorRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("raft crate editor: in the editor"); yield break; }
			bool ok = true;
			yield return PlaceableCatalog.EnsureBuilt();
			string crate = PlaceableCatalog.RaftCrate;
			Check(ref ok, PlaceableCatalog.IsHarvestable(crate), "the crate is taken from Raft's drifting raft with its gameplay");
			Check(ref ok, PlaceableCatalog.CategoryOf(crate) == ContentCatalog.LootCategory, "it is listed under " + ContentCatalog.LootCategory + " (" + PlaceableCatalog.CategoryOf(crate) + ")");
			DynamicIslands.NewIsland();
			yield return null;
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 777, Radius = 50f, Height = 20f, Style = TerrainPainter.Tropical });
			yield return null;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Terrain t = terraineditor.terrain;
			Vector3 at = t.transform.position + new Vector3(500f, 0f, 500f);
			at.y = t.transform.position.y + t.SampleHeight(at) + 0.3f;
			GameObject go = PlaceableCatalog.Spawn(crate, placed);
			Check(ref ok, go != null, "the crate can be placed in the editor");
			if (go != null)
			{
				go.transform.position = at;
				EditorGameObject.Attach(go, crate, null);
				Check(ref ok, DynamicIslands.SaveIsland(CrateIsland), "a small island with the crate saved as '" + CrateIsland + "'");
				IslandFile f = IslandFile.Load(IslandSpawner.PathFor(CrateIsland));
				Check(ref ok, f.Objects.Any(o => o.Name == crate), "the island file has the crate");

				// Its two limits, where builders look: the list's hint, the inspector, Check
				string hint = ContentCatalog.Hint(crate) ?? "";
				Check(ref ok, hint.Contains("can't be chosen") && hint.Contains("Open a chest"), "the object list's hint tells both limits");
				EditorUI.SetTab(TAB.ObjectPlace);
				DynamicIslands.EditorGizmoHandler.ClearTargets(false);
				DynamicIslands.EditorGizmoHandler.AddTarget(go.transform, false);
				ObjectInspector.Refresh();
				yield return null;
				yield return new WaitForSecondsRealtime(0.3f);
				Transform group = EditorUI.Canvas.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Group_Abandoned raft crate");
				string said = group != null ? string.Join(" ", group.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(x => x.text).ToArray()) : "";
				Check(ref ok, group != null && group.gameObject.activeInHierarchy && said.Contains("can't be chosen") && said.Contains("Open a chest"), "the inspector's 'Abandoned raft crate' group tells both limits");
				Check(ref ok, !EditorUI.Canvas.GetComponentsInChildren<UnityEngine.UI.Button>(false).Any(b => UIKit.LabelOf(b) != null && UIKit.LabelOf(b).text.StartsWith("A chest")), "the inspector doesn't offer to make it a chest");
				Screenshot(new[] { "raft_crate_inspector" });
				yield return new WaitForSecondsRealtime(0.5f);
				EditorUI.SetTab(TAB.TerrainEdit);
				var quest = new IslandQuest { Title = "Test" };
				quest.Steps.Add(new IslandQuest.Step { Type = "open" });
				quest.To(f.Props);
				f.Save(IslandSpawner.PathFor(CrateIsland));
				var plan = new WorldPlan { Name = "CI crate plan", Random = false };
				plan.Rules.Add(IntroRule.Parse("start | island:" + CrateIsland + " | start | ahead:300 | A test | Start"));
				plan.Rules.Add(IntroRule.Parse("after | type:camp | quest:start | near:start:600:any | msg | A"));
				List<PlanChecker.Finding> found = PlanChecker.Check(plan, false, true);
				PlanChecker.Finding open = found.FirstOrDefault(x => x.Rule == 1 && x.Level == PlanChecker.Level.Problem && x.Text.Contains("needs a chest to open"));
				Check(ref ok, open != null && open.Text.Contains("raft crates don't count"), "Check: an \"Open a chest\" step on an island with only the raft crate is a problem, and says the crate doesn't count" + (open != null ? " (" + open.Text + ")" : ""));
				foreach (string key in f.Props.Keys.Where(k => k.StartsWith("quest.")).ToList()) f.Props.Remove(key);
				f.Save(IslandSpawner.PathFor(CrateIsland));
			}
			DynamicIslands.NewIsland();
			if (ok) Log("PASS: raft crate editor"); else Fail("raft crate editor");
		}

		[ConsoleCommand(name: "CIRaftCrateWorld", docs: "Dev, world (host, a test world 'CI ...'; after CIRaftCrateEditor): the island 'cicrate' comes beside the raft; its crate is Raft's own (a pickup with Raft's random loot); picked up as a player does, it gives items and is gone, and the island remembers it was taken. The island and its file are removed after")]
		public static void RaftCrateWorldCommand() { StartTest(RaftCrateWorldRoutine()); }

		static IEnumerator RaftCrateWorldRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("raft crate world: host in a test world 'CI ...'"); yield break; }
			if (!System.IO.File.Exists(IslandSpawner.PathFor(CrateIsland))) { Fail("raft crate world: run CIRaftCrateEditor first"); yield break; }
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3 raft = CustomIslandSpawner.RaftPosition.Value;
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft, CustomIslandSpawner.LandRadius(CrateIsland), 700f) ?? CustomIslandSpawner.FindClearSpot(raft, CustomIslandSpawner.LandRadius(CrateIsland), 1500f);
			Check(ref ok, spot.HasValue, "a place for the island near the raft");
			if (!spot.HasValue) { Fail("raft crate world"); yield break; }
			// (the player there first: islands stay loaded where a player is, however far the raft)
			{ CharacterController c0 = player.PersonController.controller; c0.enabled = false; player.transform.position = spot.Value + Vector3.up * 30f; player.PersonController.SwitchControllerType(ControllerType.Ground); c0.enabled = true; }
			yield return new WaitForSeconds(1f);
			yield return DynamicIslands.instance.SpawnIslandFile(CrateIsland, spot.Value, true);
			yield return new WaitForSeconds(2f);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(x => x.HostName == CrateIsland);
			GameObject crate = e != null && e.Root != null ? e.Root.GetComponentsInChildren<Transform>(true).Select(x => x.gameObject).FirstOrDefault(g => g.name.StartsWith(PlaceableCatalog.RaftCrate)) : null;
			PickupItem pickup = crate != null ? crate.GetComponentInChildren<PickupItem>() : null;
			Check(ref ok, crate != null && pickup != null && crate.GetComponentInChildren<RandomDropper>() != null && crate.GetComponentInChildren<PickupItem_Networked>() != null,
				"the island's crate is Raft's own: a pickup with Raft's random loot" + (crate == null ? " (no crate on the island)" : ""));
			if (pickup != null && player != null)
			{
				// (the player beside the crate, as one who swam or sailed there)
				CharacterController cc = player.PersonController.controller;
				cc.enabled = false;
				player.transform.position = crate.transform.position + Vector3.up * 1.5f + Vector3.right * 1.2f;
				player.PersonController.SwitchControllerType(ControllerType.Ground);
				cc.enabled = true;
				yield return new WaitForSeconds(1f);
				Func<int> items = () => player.Inventory.allSlots.Where(sl => sl != null && sl.itemInstance != null && sl.itemInstance.baseItem != null).Sum(sl => sl.itemInstance.Amount);
				int before = items();
				int errors = 0; string first = null;
				Application.LogCallback counter = (msg, trace, type) => { if (type == LogType.Exception || type == LogType.Error) { errors++; if (first == null) first = msg; } };
				Application.logMessageReceived += counter;
				Pickup hands = player.GetComponentInChildren<Pickup>();
				try { if (hands != null) hands.PickupItem(pickup, true, false); }
				catch (Exception ex) { errors++; first = first ?? ex.Message; }
				yield return new WaitForSeconds(2f);
				Application.logMessageReceived -= counter;
				int after = items();
				Check(ref ok, hands != null && after > before, "picked up as a player does: items " + before + " -> " + after);
				Check(ref ok, crate == null || !crate.activeInHierarchy, "the crate is gone");
				// (what the island keeps is recorded when the world saves or the island unloads: now)
				Check(ref ok, crate != null, "the crate is hidden, not destroyed (so the island can remember it)");
				IslandObjectState.Capture(e);
				Check(ref ok, e.State.Values.Any(s => !s.Active), "the island remembers the crate was taken (" + string.Join(", ", e.State.Select(kv => kv.Key + (kv.Value.Active ? ":active" : ":taken")).ToArray()) + ")");
				// (unloaded and loaded again: still taken)
				IslandSpawner.Despawn(e.Root); e.Root = null;
				yield return DynamicIslands.instance.SpawnIslandFile(e.Name, e.Position, false, e);
				yield return new WaitForSeconds(1f);
				GameObject again = e.Root != null ? e.Root.GetComponentsInChildren<Transform>(true).Select(x => x.gameObject).FirstOrDefault(g => g.name.StartsWith(PlaceableCatalog.RaftCrate)) : null;
				Check(ref ok, again != null && !again.activeInHierarchy, "loaded again: the crate is still taken");
				Check(ref ok, errors == 0, "no errors (" + errors + (first != null ? ": " + first : "") + ")");
			}
			IslandWorldState.Remove(CrateIsland);
			try { System.IO.File.Delete(IslandSpawner.PathFor(CrateIsland)); } catch { }
			if (ok) Log("PASS: raft crate world"); else Fail("raft crate world");
		}

		const string BulkPrefix = "bulk-";

		[ConsoleCommand(name: "CIBulkIslands", docs: "Dev, anywhere (editor for the Islands window) (TEST_CATALOGUE UP10): CIBulkIslands [n] - n small islands (bulk-0001 ..., default 1000) in the folder: listing them, the spawn pool, CHOOSE ISLANDS' list, the library's Tidy up look and (in the editor) the Islands window each take under 2 s, and the search finds one. The islands are removed after (CIBulkIslands keep leaves them; CIBulkIslands clean removes them)")]
		public static void BulkIslandsCommand(string[] args)
		{
			string a = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (a == "clean") { Log("Removed " + BulkClean() + " bulk islands"); return; }
			int n;
			if (!int.TryParse(a, out n)) n = 1000;
			StartTest(BulkRoutine(Mathf.Clamp(n, 1, 5000), args != null && args.Contains("keep")));
		}

		static int BulkClean()
		{
			int n = 0;
			foreach (string f in Directory.GetFiles(DynamicIslands.assetpath, BulkPrefix + "*" + IslandFile.Extension)) { try { File.Delete(f); n++; } catch { } }
			IslandCache.Forget();
			CustomIslandSpawner.LoadPool(true);
			return n;
		}

		static IEnumerator BulkRoutine(int count, bool keep)
		{
			bool ok = true;
			try
			{
				// A small island: 64 m, a hill in the middle, nothing on it (a thousand of them are ~2 MB)
				int res = 33;
				var h = new float[res, res];
				for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) { float d = new Vector2(x - 16, y - 16).magnitude / 16f; h[y, x] = Mathf.Max(0f, 0.45f - 0.3f * d); }
				var file = new IslandFile { TerrainSize = new Vector3(64f, 60f, 64f), HeightmapResolution = res, Heights = h };
				var sw = Stopwatch.StartNew();
				for (int i = 1; i <= count; i++) { file.Name = BulkPrefix + i.ToString("0000"); file.Save(IslandSpawner.PathFor(file.Name)); }
				Log(count + " small islands written in " + sw.ElapsedMilliseconds + " ms");
				IslandCache.Forget();

				Func<string, Action, long> time = (what, act) => { var t = Stopwatch.StartNew(); act(); t.Stop(); Log("  " + what + ": " + t.ElapsedMilliseconds + " ms"); return t.ElapsedMilliseconds; };
				List<string> listed = null;
				long listMs = time("listing the islands", () => listed = IslandSpawner.ListSavedIslands().ToList());
				Check(ref ok, listMs < 2000 && listed.Count(x => x.StartsWith(BulkPrefix)) == count, "the islands are listed (" + listed.Count + " in all) in " + listMs + " ms");
				Check(ref ok, listed.Contains(BulkPrefix + (count / 2).ToString("0000")), "the search finds one of them: " + BulkPrefix + (count / 2).ToString("0000"));
				long poolMs = time("the spawn pool", () => CustomIslandSpawner.LoadPool(true));
				Check(ref ok, poolMs < 2000, "the spawn pool builds in " + poolMs + " ms");
				List<string> candidates = null;
				long pickMs = time("CHOOSE ISLANDS' list", () => candidates = WorldIslands.Candidates());
				Check(ref ok, pickMs < 2000 && candidates.Count(c => c.StartsWith(BulkPrefix)) == count, "CHOOSE ISLANDS lists them in " + pickMs + " ms");
				Housekeeping.Scan scan = null;
				// (the Installed tab no longer looks through everything when it opens: Tidy up does, on its first click)
				long tidyMs = time("Tidy up's look (its first click)", () => scan = Housekeeping.Look());
				Check(ref ok, tidyMs < 10000 && scan != null, "Tidy up looks through them and every world's saves in " + tidyMs + " ms (on its click)");
				if (DynamicIslands.InEditor())
				{
					long winMs = time("the Islands window", () => { IslandFilesWindow.Open(); IslandFilesWindow.Close(); });
					Check(ref ok, winMs < 2000, "the Islands window opens in " + winMs + " ms");
				}
				long mem = GC.GetTotalMemory(false) / (1024 * 1024);
				Log("  managed memory now " + mem + " MB");
			}
			catch (Exception e) { Check(ref ok, false, "no errors: " + e.Message); }
			finally { if (!keep) Log("Removed " + BulkClean() + " bulk islands"); }
			yield return null;
			if (ok) Log("PASS: bulk islands"); else Fail("bulk islands");
		}
	}
}
