using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of TODO 5b "Islands and world plans apart": the main menu's EDITOR opens a start window (an island, a world plan,
	/// the guide), and world plans are made on the main menu with no island loaded.
	/// </summary>
	public static partial class DevTests
	{
		static bool OnRaftMenu() { return !DynamicIslands.InEditor() && !LoadSceneManager.IsGameSceneLoaded && GameObject.Find("MainMenuCanvas") != null; }

		/// <summary>Errors and exceptions logged while a test runs (not the tests' own lines).</summary>
		static Application.LogCallback WatchErrors(List<string> into)
		{
			Application.LogCallback watch = (msg, trace, type) =>
			{
				if ((type == LogType.Exception || type == LogType.Error) && !msg.StartsWith("[CITEST]")) into.Add(msg.Split('\n')[0]);
			};
			Application.logMessageReceived += watch;
			return watch;
		}

		static void StartShot(string name) { Screenshot(new[] { "split_" + name }); }

		static string ReadStartFile()
		{
			string p = Path.Combine(DynamicIslands.assetpath, "editor_start.txt");
			try { return File.Exists(p) ? File.ReadAllText(p) : null; } catch { return null; }
		}

		static void PutStartFileBack(string text)
		{
			string p = Path.Combine(DynamicIslands.assetpath, "editor_start.txt");
			try { if (text != null) File.WriteAllText(p, text); else if (File.Exists(p)) PiecesFiles.MoveToDeleted(p, "ci"); } catch { }
		}

		static void PlanAway(string name)
		{
			try { string p = WorldPlan.PathFor(name); if (File.Exists(p)) PiecesFiles.MoveToDeleted(p, "plans"); } catch { }
		}

		[ConsoleCommand(name: "CICreateWindow", docs: "Dev, main menu: TODO 5b - the main menu's EDITOR opens the start window over the menu (no editor loaded): three cards (an island, a world plan, the guide) with their buttons, fitting 1024 x 768; the guide button opens the guide; Open an island... lists the islands; New world plan asks how to start (empty, Main story line, templates, copies), then the name; the plan opens on the main menu with the template's rules; closed, the start window offers Continue on it and shows its Main story line; Continue opens it again. The test plan goes to the deleted folder after. CICreateWindow shot: also pictures (shot_split_*.png)")]
		public static void CreateWindowCommand(string[] args) { StartTest(CreateWindowRoutine(args != null && args.Contains("shot"))); }

		static IEnumerator CreateWindowRoutine(bool shots)
		{
			if (!OnRaftMenu()) { Fail("start window: run it on the main menu"); yield break; }
			bool ok = true;
			var errors = new List<string>();
			Application.LogCallback watch = WatchErrors(errors);
			HelpLinks.TestMode = true;
			string startFile = ReadStartFile();
			string name = "CI Start " + DateTime.Now.ToString("HHmmss");
			try
			{
				Transform ed = GameObject.Find("MainMenuCanvas").transform.Find("MenuButtons/EDITOR");
				Check(ref ok, ed != null, "the main menu has EDITOR");
				if (ed != null) ed.GetComponent<Button>().onClick.Invoke();
				yield return null; yield return null;
				Check(ref ok, CreateWindow.IsOpen && !DynamicIslands.InEditor(), "EDITOR opens the start window over the main menu, no editor loaded");
				GameObject win = WindowObject(typeof(CreateWindow));
				if (win == null) { Fail("start window"); yield break; }
				foreach (string card in new[] { "Card_Island", "Card_Plan", "Card_Guide" })
					Check(ref ok, win.GetComponentsInChildren<RectTransform>(false).Any(r => r.name == card), "the card " + card);
				foreach (string b in new[] { "New island", "Open an island...", "New world plan", "Open a world plan...", "Guide" })
					Check(ref ok, win.GetComponentsInChildren<Button>(false).Any(x => x.name == "Button_" + b), "the button " + b);
				List<string> over = UIKit.RowOverflow((RectTransform)win.transform.Find("Panel"), new Vector2(1024, 768));
				Check(ref ok, over.Count == 0, "it fits 1024 x 768" + (over.Count > 0 ? ": " + string.Join("; ", over.ToArray()) : ""));
				if (shots) { StartShot("start_window"); yield return new WaitForSecondsRealtime(1f); }

				HelpLinks.LastOpened = null;
				Check(ref ok, Click(win, "Guide") && !string.IsNullOrEmpty(HelpLinks.LastOpened), "Open the guide opens the guide (" + HelpLinks.LastOpened + ")");

				Check(ref ok, Click(win, "Open an island..."), "Open an island... clicked");
				yield return null;
				for (float t = 0; t < 30f && ChoiceWindow.Loading; t += 0.2f) yield return new WaitForSecondsRealtime(0.2f);
				Check(ref ok, ChoiceWindow.IsOpen && ChoiceWindow.Title == "OPEN AN ISLAND", "Open an island... lists the saved islands (" + ChoiceWindow.Values.Count + ")");
				ChoiceWindow.Close();
				yield return null;

				Check(ref ok, Click(win, "New world plan"), "New world plan clicked");
				yield return null; yield return null;
				List<string> how = ChoiceWindow.Values;
				Check(ref ok, ChoiceWindow.IsOpen && how.Contains(WorldPlanWindow.NewEmpty) && how.Contains(WorldPlanWindow.NewStory) && how.Any(v => v.StartsWith(WorldPlanWindow.NewTemplatePrefix)),
					"New world plan asks how to start: " + how.Count + " choices (empty, Main story line, templates, copies)");
				if (shots) { StartShot("new_plan_ask"); yield return new WaitForSecondsRealtime(1f); }
				string template = WorldPlanTemplates.All.Select(x => x.Key).FirstOrDefault(k => WorldPlanTemplates.Get(k).Rules.Any(r => r.InStory)) ?? WorldPlanTemplates.All[0].Key;
				int templateRules = WorldPlanTemplates.Get(template).Rules.Count;
				Check(ref ok, ChoiceWindow.PickValue(WorldPlanWindow.NewTemplatePrefix + template), "from the template '" + template + "'");
				yield return null; yield return null;
				GameObject prompt = WindowObject(typeof(TextPromptWindow));
				Check(ref ok, prompt != null && TypeInto(prompt, "Name", name) && Click(prompt, "OK"), "the name prompt: '" + name + "', OK");
				yield return null; yield return null;
				GameObject plan = WindowObject(typeof(WorldPlanWindow));
				Check(ref ok, plan != null && MenuWindows.Root != null && plan.transform.IsChildOf(MenuWindows.Root) && !DynamicIslands.InEditor(), "the plan opens on the main menu");
				Check(ref ok, WorldPlanWindow.Plan != null && WorldPlanWindow.Plan.Name == name && File.Exists(WorldPlan.PathFor(name)), "the new plan '" + name + "' is shown and saved");
				Check(ref ok, WorldPlanWindow.Plan != null && WorldPlanWindow.Plan.Rules.Count == templateRules, "it has the template's " + templateRules + " rules (" + (WorldPlanWindow.Plan != null ? WorldPlanWindow.Plan.Rules.Count : -1) + ")");
				Check(ref ok, EditorEntry.LastPlan == name && EditorEntry.Last == EditorEntry.Plan, "it is remembered as the last plan");
				if (shots) { StartShot("plan_on_menu"); yield return new WaitForSecondsRealtime(1f); }
				if (plan != null) Check(ref ok, Click(plan, "Close"), "the plan's Close");
				yield return null; yield return null;
				Check(ref ok, !WorldPlanWindow.IsOpen && CreateWindow.IsOpen, "closing the plan shows the start window again");
				Button cont = win.GetComponentsInChildren<Button>(false).FirstOrDefault(x => x.name == "Button_ContinuePlan");
				Check(ref ok, cont != null && UIKit.LabelOf(cont).text.Contains(name), "the plan card offers Continue '" + name + "'");
				Text story = win.GetComponentsInChildren<Text>(false).FirstOrDefault(x => x.name == "PlanStory");
				Check(ref ok, story != null && story.text.Contains("Main story line") && story.text.Contains("1. "), "the plan card shows its Main story line: " + (story != null ? story.text.Replace("\n", " / ") : "none"));
				if (shots) { StartShot("start_window_continue"); yield return new WaitForSecondsRealtime(1f); }
				if (cont != null) cont.onClick.Invoke();
				yield return null;
				Check(ref ok, WorldPlanWindow.IsOpen && WorldPlanWindow.Plan != null && WorldPlanWindow.Plan.Name == name, "Continue opens the plan again");
				WorldPlanWindow.Close();
				CreateWindow.Close();
				Check(ref ok, errors.Count == 0, "no errors (" + string.Join("; ", errors.Take(3).ToArray()) + ")");
			}
			finally
			{
				Application.logMessageReceived -= watch;
				HelpLinks.TestMode = false;
				PlanAway(name);
				PutStartFileBack(startFile);
			}
			if (ok) Log("PASS: start window"); else Fail("start window");
		}

		[ConsoleCommand(name: "CIPlanFromMenu", docs: "Dev, main menu: TODO 5b - a world plan on the main menu with no island loaded: Check runs there; Test this plan makes a new world 'Plan test <time>' with it, its island comes, the world window offers Back to the world plan, and back goes to the main menu with the start window and the plan open again (not the editor). Several minutes; the test plan goes to the deleted folder after")]
		public static void PlanFromMenuCommand(string[] args) { StartTest(PlanFromMenuRoutine()); }

		static IEnumerator PlanFromMenuRoutine()
		{
			if (!OnRaftMenu()) { Fail("plan from the menu: run it on the main menu"); yield break; }
			bool ok = true;
			var errors = new List<string>();
			Application.LogCallback watch = WatchErrors(errors);
			string startFile = ReadStartFile();
			string island = IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == "Crowfield Farm") ?? IslandSpawner.ListSavedIslands().FirstOrDefault(n => !n.StartsWith("gen-") && !n.StartsWith("ci"));
			var wp = new WorldPlan { Name = "CI Menu Plan", Random = false, Description = "A plan tested from the main menu (CIPlanFromMenu)" };
			wp.Rules.Add(IntroRule.Parse("first | island:" + island + " | start | ahead:300 | A first island | First"));
			wp.Save();
			try
			{
				CreateWindow.Open();
				WorldPlanWindow.Open(wp.Name);
				yield return null; yield return null;
				GameObject pw = WindowObject(typeof(WorldPlanWindow));
				Check(ref ok, pw != null && pw.transform.IsChildOf(MenuWindows.Root) && WorldPlanWindow.Plan.Name == wp.Name, "the plan opens on the main menu");
				if (pw == null) { Fail("plan from the menu"); yield break; }
				Check(ref ok, Click(pw, "Check"), "Check clicked");
				for (float t = 0; t < 20f && !PlanCheckWindow.IsOpen; t += 0.2f) yield return new WaitForSecondsRealtime(0.2f);
				Check(ref ok, PlanCheckWindow.IsOpen, "Check's report opens on the main menu");
				PlanCheckWindow.Close();
				yield return null;
				Check(ref ok, Click(pw, "Test this plan"), "Test this plan clicked");
				yield return null;
				Check(ref ok, IslandTest.Busy && IslandTest.PlanToTest == wp.Name && IslandTest.FromMenu, "Test this plan: on its way from the main menu");
				for (float t = 0; t < 300f && !IslandTest.Testing; t += 1f) yield return new WaitForSeconds(1f);
				for (float t = 0; t < 40f && !IslandWorldState.Islands.Any(x => x.HostName.Equals(island, StringComparison.OrdinalIgnoreCase)); t += 1f) yield return new WaitForSeconds(1f);
				string world = SaveAndLoad.CurrentGameFileName ?? "";
				Check(ref ok, IslandTest.Testing && world.StartsWith("Plan test"), "in a new world '" + world + "' (" + IslandTest.LastStep + ")");
				Check(ref ok, IslandWorldState.Islands.Any(x => x.HostName.Equals(island, StringComparison.OrdinalIgnoreCase)), "the plan's first island '" + island + "' came");
				WorldWindow.Open();
				yield return null;
				Button back = WorldWindow.ButtonNamed("BackToEditor");
				Check(ref ok, back != null && back.gameObject.activeInHierarchy && UIKit.LabelOf(back).text == "Back to the world plan", "the world window offers Back to the world plan (" + (back != null ? UIKit.LabelOf(back).text : "none") + ")");
				if (back != null) back.onClick.Invoke();
				for (float t = 0; t < 240f && (IslandTest.Busy || !WorldPlanWindow.IsOpen); t += 1f) yield return new WaitForSeconds(1f);
				yield return new WaitForSeconds(1f);
				Check(ref ok, OnRaftMenu() && WorldPlanWindow.IsOpen && WorldPlanWindow.Plan != null && WorldPlanWindow.Plan.Name == wp.Name, "back on the main menu with the plan open");
				Check(ref ok, CreateWindow.IsOpen, "the start window is under it");
				WorldPlanWindow.Close();
				CreateWindow.Close();
				Check(ref ok, errors.Count == 0, "no errors (" + string.Join("; ", errors.Take(3).ToArray()) + ")");
			}
			finally
			{
				Application.logMessageReceived -= watch;
				PlanAway(wp.Name);
				PutStartFileBack(startFile);
			}
			if (ok) Log("PASS: plan from the menu"); else Fail("plan from the menu");
		}

		static GameObject TopBar() { RectTransform t = UnityEngine.Object.FindObjectsOfType<RectTransform>().FirstOrDefault(x => x.name == "TopBar"); return t != null ? t.gameObject : null; }

		static string SomeSavedIsland() { return IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == "Crowfield Farm") ?? IslandSpawner.ListSavedIslands().FirstOrDefault(n => !n.StartsWith("gen-") && !n.StartsWith("ci")); }

		static Button TopBarButton(GameObject bar, string label) { return bar == null ? null : bar.GetComponentsInChildren<Button>(true).FirstOrDefault(b => UIKit.LabelOf(b) != null && UIKit.LabelOf(b).text == label); }

		[ConsoleCommand(name: "CIUseInPlan", docs: "Dev, editor: TODO 5b - the island editor's Use in a plan...: a saved island opened; Use in a plan... lists A new world plan... and the plans; a new plan 'CI Use <time>' with the island next in its Main story line (the plan opens on it; the file has it in the story, the last island before Raft's ending); again into the same plan as another island (not in the story); the rule card's Edit island opens the island, the top bar shows Back to the plan in place of Use in a plan... and still fits 1024 x 768; Back to the plan opens the plan again in the editor. The test plan goes to the deleted folder after")]
		public static void UseInPlanCommand(string[] args) { StartTest(UseInPlanRoutine()); }

		static IEnumerator UseInPlanRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("use in a plan: in the editor"); yield break; }
			bool ok = true;
			var errors = new List<string>();
			Application.LogCallback watch = WatchErrors(errors);
			string startFile = ReadStartFile();
			string island = SomeSavedIsland();
			string name = "CI Use " + DateTime.Now.ToString("HHmmss");
			try
			{
				Check(ref ok, island != null && DynamicIslands.LoadIsland(island), "the island '" + island + "' opened");
				yield return null; yield return null;
				GameObject bar = TopBar();
				Check(ref ok, bar != null && Click(bar, "Use in a plan..."), "the top bar's Use in a plan...");
				yield return null;
				Check(ref ok, ChoiceWindow.IsOpen && ChoiceWindow.Values.Contains(EditorEntry.UseNewPlan), "it lists A new world plan... and " + (ChoiceWindow.Values.Count - 1) + " plan(s)");
				Check(ref ok, ChoiceWindow.PickValue(EditorEntry.UseNewPlan), "A new world plan...");
				yield return null; yield return null;
				GameObject prompt = WindowObject(typeof(TextPromptWindow));
				Check(ref ok, prompt != null && TypeInto(prompt, "Name", name) && Click(prompt, "OK"), "named '" + name + "'");
				yield return null; yield return null;
				Check(ref ok, ChoiceWindow.IsOpen && ChoiceWindow.PickValue(EditorEntry.UseInStory), "as next in the Main story line");
				yield return null; yield return null;
				WorldPlan p = File.Exists(WorldPlan.PathFor(name)) ? WorldPlan.Load(name) : null;
				IntroRule story = p != null ? p.Rules.FirstOrDefault(r => r.WhatArg == island) : null;
				Check(ref ok, story != null && story.InStory && story.Where == "receiver", "the plan file has it in the Main story line (" + (story != null ? story.StoryPlace : "none") + ")");
				List<string> steps = p != null ? StoryChain.BuildSteps(p.RaftStory, p.LeaveOut, p.Rules) : new List<string>();
				int at = story != null ? steps.FindIndex(x => !StoryChain.IsRaft(x) && StoryChain.RuleIdOf(x) == story.Id) : -1;
				Check(ref ok, at >= 0 && (at == steps.Count - 1 || (at == steps.Count - 2 && StoryChain.TypeOfStep(steps[steps.Count - 1]) == ChunkPointType.Landmark_Utopia)), "it is the story's last island before Raft's ending (step " + (at + 1) + " of " + steps.Count + ")");
				Check(ref ok, WorldPlanWindow.IsOpen && WorldPlanWindow.Plan != null && WorldPlanWindow.Plan.Name == name, "the plan opens in the editor");
				WorldPlanWindow.Close();
				yield return null;

				Check(ref ok, Click(bar, "Use in a plan..."), "Use in a plan... again");
				yield return null;
				Check(ref ok, ChoiceWindow.IsOpen && ChoiceWindow.PickValue(name), "the same plan from the list");
				yield return null; yield return null;
				Check(ref ok, ChoiceWindow.IsOpen && ChoiceWindow.PickValue(EditorEntry.UseOther), "as another island");
				yield return null; yield return null;
				p = WorldPlan.Load(name);
				Check(ref ok, p != null && p.Rules.Count == 2 && p.Rules.Count(r => r.WhatArg == island && !r.MainStory && r.When == "start") == 1, "the plan has a second rule, not in the story (" + (p != null ? p.Rules.Count : 0) + " rules)");

				GameObject plan = WindowObject(typeof(WorldPlanWindow));
				Button edit = plan != null ? plan.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == "Button_EditIsland") : null;
				Check(ref ok, edit != null, "the rule card has Edit island");
				if (edit != null) edit.onClick.Invoke();
				for (float t = 0; t < 20f && WorldPlanWindow.IsOpen; t += 0.2f) yield return new WaitForSecondsRealtime(0.2f);
				yield return null;
				Check(ref ok, !WorldPlanWindow.IsOpen && DynamicIslands.InEditor() && DynamicIslands.currentIslandName == island, "Edit island: the island in the editor");
				Button back = TopBarButton(bar, "Back to the plan"), use = TopBarButton(bar, "Use in a plan...");
				Check(ref ok, back != null && back.gameObject.activeInHierarchy && use != null && !use.gameObject.activeInHierarchy, "the top bar shows Back to the plan in place of Use in a plan...");
				List<string> over = UIKit.RowOverflow((RectTransform)bar.transform, new Vector2(1024, 768));
				Check(ref ok, over.Count == 0, "the top bar fits 1024 x 768" + (over.Count > 0 ? ": " + string.Join("; ", over.ToArray()) : ""));
				if (back != null) back.onClick.Invoke();
				yield return null; yield return null;
				Check(ref ok, WorldPlanWindow.IsOpen && WorldPlanWindow.Plan != null && WorldPlanWindow.Plan.Name == name, "Back to the plan: the plan open again");
				Check(ref ok, use != null && use.gameObject.activeInHierarchy && back != null && !back.gameObject.activeInHierarchy, "the top bar shows Use in a plan... again");
				WorldPlanWindow.Close();
				Check(ref ok, errors.Count == 0, "no errors (" + string.Join("; ", errors.Take(3).ToArray()) + ")");
			}
			finally
			{
				Application.logMessageReceived -= watch;
				PlanAway(name);
				PutStartFileBack(startFile);
			}
			if (ok) Log("PASS: use in a plan"); else Fail("use in a plan");
		}

		[ConsoleCommand(name: "CIEditIslandFromMenu", docs: "Dev, main menu: TODO 5b - a plan on the main menu, its rule card's Edit island: the editor opens on that island with Back to the plan in its top bar; Back to the plan goes to the main menu with the start window and the plan open again. The test plan goes to the deleted folder after")]
		public static void EditIslandFromMenuCommand(string[] args) { StartTest(EditIslandFromMenuRoutine(args != null && args.Contains("shot"))); }

		static IEnumerator EditIslandFromMenuRoutine(bool shots)
		{
			if (!OnRaftMenu()) { Fail("edit island from the menu: run it on the main menu"); yield break; }
			bool ok = true;
			string startFile = ReadStartFile();
			string island = SomeSavedIsland();
			var wp = new WorldPlan { Name = "CI Edit Jump", Random = false, Description = "Edit island from the main menu (CIEditIslandFromMenu)" };
			wp.Rules.Add(IntroRule.Parse("first | island:" + island + " | start | ahead:300 | A first island | First"));
			wp.Save();
			try
			{
				CreateWindow.Open();
				WorldPlanWindow.Open(wp.Name);
				yield return null; yield return null;
				GameObject pw = WindowObject(typeof(WorldPlanWindow));
				Button edit = pw != null ? pw.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == "Button_EditIsland") : null;
				Check(ref ok, edit != null, "the rule card has Edit island");
				if (edit == null) { Fail("edit island from the menu"); yield break; }
				edit.onClick.Invoke();
				for (float t = 0; t < 180f && !(DynamicIslands.InEditor() && DynamicIslands.currentIslandName == island); t += 1f) yield return new WaitForSeconds(1f);
				yield return new WaitForSeconds(2f);
				Check(ref ok, DynamicIslands.InEditor() && DynamicIslands.currentIslandName == island, "the editor opens on '" + island + "' (" + DynamicIslands.currentIslandName + ")");
				Button back = TopBarButton(TopBar(), "Back to the plan");
				Check(ref ok, back != null && back.gameObject.activeInHierarchy, "the top bar has Back to the plan");
				if (shots) { StartShot("editor_back_to_plan"); yield return new WaitForSecondsRealtime(1f); }
				if (back != null) back.onClick.Invoke();
				for (float t = 0; t < 180f && !(OnRaftMenu() && WorldPlanWindow.IsOpen); t += 1f) yield return new WaitForSeconds(1f);
				yield return new WaitForSeconds(1f);
				Check(ref ok, OnRaftMenu() && WorldPlanWindow.IsOpen && WorldPlanWindow.Plan != null && WorldPlanWindow.Plan.Name == wp.Name && CreateWindow.IsOpen, "back on the main menu with the start window and the plan open");
				WorldPlanWindow.Close();
				CreateWindow.Close();
			}
			finally
			{
				PlanAway(wp.Name);
				PutStartFileBack(startFile);
			}
			if (ok) Log("PASS: edit island from the menu"); else Fail("edit island from the menu");
		}
	}
}
