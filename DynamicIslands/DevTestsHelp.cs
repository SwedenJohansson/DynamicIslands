using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>The help of the World plans window (? marks, Help) and the alpha box's help buttons.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIPlanHelp", docs: "Dev, editor: the World plans window's help - a ? on every part (the plan row, templates, random islands, Raft's story, description, map, Check, Export/Import, and 6 on each rule card), each inside the window with its text; hovering shows the popup; Help opens the steps with the guide's buttons (online section, PDF); the island's rules have their own help and no Export ?. Pictures shot_planhelp_*")]
		public static void PlanHelpCommand() { DynamicIslands.instance.StartCoroutine(PlanHelpRoutine()); }

		static IEnumerator PlanHelpRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("plan help: open the editor first"); yield break; }
			bool ok = true;
			HelpLinks.TestMode = true;
			try
			{
				WorldPlanWindow.EnsureSamples();
				WorldPlanWindow.Open("Adventure");
				yield return new WaitForSecondsRealtime(0.5f);
				Transform window = EditorUI.Canvas.transform.Find("WorldPlanWindow");
				RectTransform panel = window != null ? window.Find("Panel") as RectTransform : null;
				Check(ref ok, WorldPlanWindow.IsOpen && panel != null, "the window opens on Adventure");
				if (panel == null) yield break;
				Canvas.ForceUpdateCanvases();
				List<UIKit.HelpMark> marks = window.GetComponentsInChildren<UIKit.HelpMark>(false).ToList();
				int cards = window.GetComponentsInChildren<Transform>(false).Count(t => t.name == "Rule");
				int onCards = marks.Count(m => m.GetComponentsInParent<Transform>(true).Any(t => t.name == "Rule"));
				Check(ref ok, cards > 0 && onCards == cards * 6, onCards + " ? on " + cards + " rule cards (6 each)");
				Check(ref ok, marks.Count - onCards == 8, (marks.Count - onCards) + " ? for the window's parts (plan row, templates, random islands, Raft's story, description, map, Check, Export/Import)");
				Check(ref ok, marks.All(m => m.Text != null && m.Text.Length > 60), "every ? has a real explanation");
				Rect area = ScreenRect(panel);
				// (a ? on a card further down the list is scrolled out of view: only the visible ones must be inside the window)
				RectTransform list = window.GetComponentsInChildren<ScrollRect>(false).Select(s => s.viewport).FirstOrDefault();
				Rect listArea = list != null ? ScreenRect(list) : area;
				var outside = marks.Where(m =>
				{
					Rect r = ScreenRect((RectTransform)m.transform);
					bool onCard = m.GetComponentsInParent<Transform>(true).Any(t => t.name == "Rule");
					if (onCard && !listArea.Contains(r.center)) return false; // (scrolled out of view)
					return r.width < 10f || !area.Contains(r.center);
				}).ToList();
				Check(ref ok, outside.Count == 0, "every ? has its size and sits in the window" + (outside.Count > 0 ? " - not: " + outside.Count : ""));
				// Hovering shows the popup
				UIKit.HelpMark when = marks.FirstOrDefault(m => m.Text.StartsWith("WHEN"));
				if (when != null)
				{
					when.OnPointerEnter(null);
					yield return null;
					Check(ref ok, UIKit.ShownHelp == when.Text, "hovering the When ? shows its help");
					Screenshot(new[] { "planhelp_when" });
					yield return new WaitForSecondsRealtime(0.6f);
					when.OnPointerExit(null);
					yield return null;
					Check(ref ok, UIKit.ShownHelp == null, "moving off it closes the popup");
				}
				else Check(ref ok, false, "a When ? on the first card");
				// Help
				Button help = panel.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == "HelpButton");
				Check(ref ok, help != null, "a Help button in the window's head");
				if (help != null)
				{
					help.onClick.Invoke();
					yield return new WaitForSecondsRealtime(0.4f);
					Check(ref ok, InfoWindow.IsOpen && InfoWindow.Title == "HOW TO MAKE A WORLD PLAN" && InfoWindow.Body.Contains("+ Add a rule") && InfoWindow.Body.Contains("NEW WORLD"),
						"Help shows the steps: " + InfoWindow.Title);
					Screenshot(new[] { "planhelp_steps" });
					yield return new WaitForSecondsRealtime(0.6f);
					Button online = InfoWindow.ButtonNamed("Guide: world plans step by step");
					if (online != null) online.onClick.Invoke();
					Check(ref ok, HelpLinks.LastOpened == HelpLinks.GuideOnline + "#72-your-first-world-plan-step-by-step", "its guide button opens section 7.2 online: " + HelpLinks.LastOpened);
					Button pdf = InfoWindow.ButtonNamed("Open the guide (PDF)");
					HelpLinks.LastOpened = null;
					if (pdf != null) pdf.onClick.Invoke();
					CheckGuideOpened(ref ok);
					InfoWindow.Close();
					yield return null;
					Check(ref ok, !InfoWindow.IsOpen && WorldPlanWindow.IsOpen, "Close goes back to the plan");
				}
				WorldPlanWindow.Close();

				// The island's own rules
				var saved = new Dictionary<string, string>(DynamicIslands.currentIslandProps);
				try
				{
					DynamicIslands.currentIslandProps.Clear();
					WorldDirector.SetRulesInProps(DynamicIslands.currentIslandProps, new[] { IntroRule.Parse("reward | type:random | quest:self | near:self:600:north | Well done | Reward") });
					WorldPlanWindow.OpenIsland();
				}
				catch (Exception e) { Check(ref ok, false, "island rules: " + e.Message); }
				yield return new WaitForSecondsRealtime(0.5f);
				marks = window.GetComponentsInChildren<UIKit.HelpMark>(false).ToList();
				Check(ref ok, marks.Count == 5 + 2 && !marks.Any(m => m.Text.StartsWith("Export")), "island rules: " + marks.Count + " ? (the card's 5 - no story row on an island - plus map and Check), no Export ?");
				Button islandHelp = panel.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == "HelpButton");
				if (islandHelp != null) islandHelp.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, InfoWindow.IsOpen && InfoWindow.Title == "ISLAND RULES" && InfoWindow.ButtonLabels.Contains("Guide: islands that bring islands"), "the island rules' Help: " + InfoWindow.Title);
				InfoWindow.Close();
				WorldPlanWindow.Close();
				DynamicIslands.currentIslandProps.Clear();
				foreach (var kv in saved) DynamicIslands.currentIslandProps[kv.Key] = kv.Value;
			}
			finally { HelpLinks.TestMode = false; InfoWindow.Close(); }
			if (ok) Log("PASS: plan help"); else Fail("plan help");
		}

		/// <summary>The guide's PDF came out of the .rmod and was opened as a file.</summary>
		static void CheckGuideOpened(ref bool ok)
		{
			string path = Path.Combine(DynamicIslands.assetpath, HelpLinks.PdfName);
			bool file = HelpLinks.LastOpened != null && HelpLinks.LastOpened.StartsWith("file:") && File.Exists(path) && new FileInfo(path).Length > 1000000;
			Check(ref ok, file, "the guide opens as the PDF from the mod: " + HelpLinks.LastOpened + (File.Exists(path) ? " (" + new FileInfo(path).Length / 1024 + " KB)" : " (no file)"));
		}

		/// <summary>The alpha box's help: Discord, Guide, Report a problem (its box and buttons), and moving the box.</summary>
		static IEnumerator NoticeHelpRoutine(Action<bool, string> check)
		{
			HelpLinks.TestMode = true;
			try
			{
				check(ExperimentalNotice.DiscordButton != null && ExperimentalNotice.GuideButton != null && ExperimentalNotice.ReportButton != null
					&& ExperimentalNotice.DiscordButton.gameObject.activeInHierarchy && ExperimentalNotice.ReportButton.gameObject.activeInHierarchy, "Discord, Guide (PDF) and Report a problem on the box");
				ExperimentalNotice.DiscordButton.onClick.Invoke();
				check(HelpLinks.LastOpened == HelpLinks.Discord, "Discord opens " + HelpLinks.LastOpened);
				HelpLinks.LastOpened = null;
				ExperimentalNotice.GuideButton.onClick.Invoke();
				bool g = true; CheckGuideOpened(ref g); check(g, "Guide (PDF) opens the PDF");
				ExperimentalNotice.ReportButton.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.4f);
				check(InfoWindow.IsOpen && InfoWindow.Title == "REPORT A PROBLEM" && InfoWindow.Body.Contains("Player.log") && InfoWindow.Body.Contains("What you expected") && InfoWindow.Body.Contains(ExperimentalNotice.Version),
					"Report a problem opens its box: what to include, the logs, the versions");
				var want = new[] { "Copy report form", "Open log folder", "Report on GitHub", "Post on Discord", "Close" };
				check(want.All(w => InfoWindow.ButtonLabels.Contains(w)), "its buttons: " + string.Join(", ", InfoWindow.ButtonLabels.ToArray()));
				check(InfoWindow.ButtonsOutside().Count == 0, "every button inside the box" + (InfoWindow.ButtonsOutside().Count > 0 ? " - sticking out: " + string.Join(", ", InfoWindow.ButtonsOutside().ToArray()) : ""));
				Screenshot(new[] { "notice_report" });
				yield return new WaitForSecondsRealtime(0.6f);
				InfoWindow.ButtonNamed("Report on GitHub").onClick.Invoke();
				check(HelpLinks.LastOpened != null && HelpLinks.LastOpened.StartsWith(HelpLinks.Issues + "/new?body=") && Uri.UnescapeDataString(HelpLinks.LastOpened).Contains("Custom Islands " + ExperimentalNotice.Version),
					"Report on GitHub opens a new issue with the form and the versions");
				InfoWindow.ButtonNamed("Post on Discord").onClick.Invoke();
				check(HelpLinks.LastOpened == HelpLinks.Discord, "Post on Discord opens the server");
				InfoWindow.ButtonNamed("Open log folder").onClick.Invoke();
				check(HelpLinks.LastOpened == HelpLinks.LogFolder && File.Exists(Path.Combine(HelpLinks.LogFolder, "Player.log")), "Open log folder: " + HelpLinks.LogFolder + " (Player.log there)");
				string clip = GUIUtility.systemCopyBuffer;
				InfoWindow.ButtonNamed("Copy report form").onClick.Invoke();
				check(GUIUtility.systemCopyBuffer.Contains("What happened:") && GUIUtility.systemCopyBuffer.Contains("Custom Islands " + ExperimentalNotice.Version) && InfoWindow.Status.StartsWith("Copied"),
					"Copy report form puts the form on the clipboard (" + HelpLinks.Versions + ")");
				GUIUtility.systemCopyBuffer = clip;
				InfoWindow.ButtonNamed("Close").onClick.Invoke();
				yield return null;
				check(!InfoWindow.IsOpen, "Close closes it");

				// Moving the box: a drag moves it, never off the screen, and the place is remembered
				RectTransform panel = ExperimentalNotice.Panel;
				Vector2 start = panel.anchoredPosition;
				ExperimentalNotice.MoveBy(new Vector2(-250f, -150f));
				yield return null;
				Rect r = ScreenRect(panel);
				check((panel.anchoredPosition - start).magnitude > 100f && r.xMin >= -1f && r.yMin >= -1f && r.xMax <= Screen.width + 1f && r.yMax <= Screen.height + 1f, "dragging moves it (" + start + " > " + panel.anchoredPosition + ")");
				string notice = File.ReadAllText(Path.Combine(DynamicIslands.assetpath, "notice.txt"));
				check(notice.Contains("pos="), "the place is remembered (notice.txt)");
				Screenshot(new[] { "notice_moved" });
				yield return new WaitForSecondsRealtime(0.6f);
				ExperimentalNotice.MoveBy(new Vector2(5000f, 5000f));
				yield return null;
				r = ScreenRect(panel);
				check(r.xMin >= -1f && r.yMin >= -1f && r.xMax <= Screen.width + 1f && r.yMax <= Screen.height + 1f, "dragged too far, it stays on the screen (" + r + ")");
				ExperimentalNotice.MoveBy(new Vector2(-5000f, -5000f));
				yield return null;
				r = ScreenRect(panel);
				check(r.xMin >= -1f && r.yMin >= -1f && r.xMax <= Screen.width + 1f && r.yMax <= Screen.height + 1f, "and the other way (" + r + ")");
				ExperimentalNotice.ResetPlace();
				yield return null;
				check(panel.anchoredPosition == start || (panel.anchoredPosition - start).magnitude < 1f, "back where it starts");
			}
			finally { HelpLinks.TestMode = false; InfoWindow.Close(); }
		}
	}
}
