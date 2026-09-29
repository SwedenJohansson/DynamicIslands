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

		[ConsoleCommand(name: "CIMenuLibrary", docs: "Dev, main menu: the ISLAND LIBRARY button shows its words (all of them drawn, on the screen) and opens the library; the library's Submit yours... shows how to submit (export, post on the Discord, approved first) with its buttons (Discord, exports folder, the guide's section); pictures shot_menu_library, shot_library_submit")]
		public static void MenuLibraryCommand() { DynamicIslands.instance.StartCoroutine(MenuLibraryRoutine()); }

		static IEnumerator MenuLibraryRoutine()
		{
			bool ok = true;
			GameObject button = GameObject.Find("MainMenuCanvas/MenuButtons/LIBRARY");
			Text text = button != null ? button.GetComponentInChildren<Text>() : null;
			if (text == null) { Fail("menu library: no ISLAND LIBRARY button on the main menu"); yield break; }
			Canvas.ForceUpdateCanvases();
			int drawn = text.cachedTextGenerator.characterCountVisible;
			Check(ref ok, text.text == "ISLAND LIBRARY" && drawn >= "ISLAND LIBRARY".Length - 1, "the button shows ISLAND LIBRARY (" + drawn + " letters drawn, size " + text.fontSize + ")");
			Text editor = GameObject.Find("MainMenuCanvas/MenuButtons/EDITOR") != null ? GameObject.Find("MainMenuCanvas/MenuButtons/EDITOR").GetComponentInChildren<Text>() : null;
			int size = text.cachedTextGenerator.fontSizeUsedForBestFit, editorSize = editor != null ? editor.cachedTextGenerator.fontSizeUsedForBestFit : 0;
			Check(ref ok, editorSize > 0 && size <= editorSize && size >= editorSize / 2, "its words fit the button, no larger than EDITOR's (" + size + " / " + editorSize + ")");
			var corners = new Vector3[4];
			text.rectTransform.GetWorldCorners(corners);
			float right = corners[0].x + text.preferredWidth * text.canvas.scaleFactor;
			Check(ref ok, corners[0].x >= 0f && right <= Screen.width * 0.5f, "its words are on the screen, on the left (" + corners[0].x.ToString("F0") + " to " + right.ToString("F0") + " px)");
			Screenshot(new[] { "menu_library" });
			yield return new WaitForSecondsRealtime(0.6f);
			HelpLinks.TestMode = true;
			try
			{
				button.GetComponent<Button>().onClick.Invoke();
				yield return new WaitForSecondsRealtime(1f);
				Check(ref ok, LibraryWindow.IsOpen, "the button opens the library");
				Button submit = LibraryWindow.Root != null ? LibraryWindow.Root.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == "Button_Submit") : null;
				Check(ref ok, submit != null, "the library has Submit yours...");
				if (submit != null)
				{
					submit.onClick.Invoke();
					yield return new WaitForSecondsRealtime(0.4f);
					Check(ref ok, InfoWindow.IsOpen && InfoWindow.Title == "SUBMIT YOUR ISLAND OR PLAN" && InfoWindow.Body.Contains("Export") && InfoWindow.Body.Contains("Discord") && InfoWindow.Body.Contains("approved"),
						"it explains: export, post on the Discord, approved first");
					Screenshot(new[] { "library_submit" });
					yield return new WaitForSecondsRealtime(0.6f);
					InfoWindow.ButtonNamed("Open the Discord").onClick.Invoke();
					Check(ref ok, HelpLinks.LastOpened == HelpLinks.Discord, "Open the Discord: " + HelpLinks.LastOpened);
					InfoWindow.ButtonNamed("Open exports folder").onClick.Invoke();
					Check(ref ok, HelpLinks.LastOpened == LibraryPack.ExportFolder && Directory.Exists(LibraryPack.ExportFolder), "Open exports folder: " + HelpLinks.LastOpened);
					InfoWindow.ButtonNamed("Guide: sharing").onClick.Invoke();
					Check(ref ok, HelpLinks.LastOpened == HelpLinks.GuideOnline + "#47-saving-and-sharing", "Guide: sharing opens section 4.7");
					InfoWindow.ButtonNamed("Close").onClick.Invoke();
					yield return null;
					Check(ref ok, !InfoWindow.IsOpen && LibraryWindow.IsOpen, "Close goes back to the library");
				}
			}
			finally { HelpLinks.TestMode = false; InfoWindow.Close(); LibraryWindow.Close(); }
			if (ok) Log("PASS: menu library"); else Fail("menu library");
		}

		[ConsoleCommand(name: "CIEditorUpDown", docs: "Dev, editor: the camera goes straight up and down (Space / C) without turning or sliding sideways, whichever way it looks; the keys are written in the camera help and the Terrain tab's tips")]
		public static void EditorUpDownCommand() { DynamicIslands.instance.StartCoroutine(EditorUpDownRoutine()); }

		static IEnumerator EditorUpDownRoutine()
		{
			EditorCamera cam = EditorCamera.Instance;
			if (!DynamicIslands.InEditor() || cam == null) { Fail("editor up/down: open the editor first"); yield break; }
			bool ok = true;
			foreach (float dir in new[] { 1f, -1f })
			{
				Vector3 start = cam.transform.position;
				Quaternion look = cam.transform.rotation;
				float until = Time.unscaledTime + 1f;
				while (Time.unscaledTime < until)
				{
					cam.Move(new Vector3(0f, dir, 0f), false, false, Mathf.Min(Time.unscaledDeltaTime, 0.1f));
					yield return null;
				}
				yield return new WaitForSecondsRealtime(0.3f);
				Vector3 moved = cam.transform.position - start;
				Check(ref ok, moved.y * dir > 2f && new Vector2(moved.x, moved.z).magnitude < 0.05f * Mathf.Abs(moved.y) + 0.05f && Quaternion.Angle(look, cam.transform.rotation) < 0.5f,
					(dir > 0 ? "Space" : "C") + ": straight " + (dir > 0 ? "up" : "down") + " " + moved.y.ToString("F1") + " m (sideways " + new Vector2(moved.x, moved.z).magnitude.ToString("F2") + " m)");
			}
			Text tips = EditorUI.Canvas.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Tips" && t.text.StartsWith("Left mouse: use the brush"));
			Check(ref ok, EditorCamera.Help.Contains("Space / C") && tips != null && tips.text.Contains("Space / C: straight up / down"), "the keys are written in the help and the Terrain tab's tips");
			Screenshot(new[] { "editor_updown" });
			yield return new WaitForSecondsRealtime(0.6f);
			if (ok) Log("PASS: editor up down"); else Fail("editor up down");
		}

		/// <summary>The guide's PDF came out of the .rmod and was opened as a file.</summary>
		static void CheckGuideOpened(ref bool ok)
		{
			string path = Path.Combine(DynamicIslands.assetpath, HelpLinks.PdfName);
			bool file = HelpLinks.LastOpened != null && HelpLinks.LastOpened.StartsWith("file:") && File.Exists(path) && new FileInfo(path).Length > 1000000;
			Check(ref ok, file, "the guide opens as the PDF from the mod: " + HelpLinks.LastOpened + (File.Exists(path) ? " (" + new FileInfo(path).Length / 1024 + " KB)" : " (no file)"));
		}

		[ConsoleCommand(name: "CINoticeShot", docs: "Dev, main menu: the guide's picture of the main menu with the alpha box open (shot_main_menu.png); a folded box is opened for the picture and folded again after, the player's choice (notice.txt) untouched")]
		public static void NoticeShotCommand() { DynamicIslands.instance.StartCoroutine(NoticeShotRoutine()); }

		static IEnumerator NoticeShotRoutine()
		{
			bool folded = ExperimentalNotice.Folded;
			var fold = HarmonyLib.Traverse.Create(typeof(ExperimentalNotice)).Method("SetFolded", new[] { typeof(bool) });
			if (folded) fold.GetValue(false);
			yield return new WaitForSecondsRealtime(0.5f);
			Screenshot(new[] { "main_menu" });
			yield return new WaitForSecondsRealtime(1.5f);
			if (folded) fold.GetValue(true);
			Log("PASS: notice shot");
		}

		/// <summary>The alpha box is never shown over another window: Raft's New Game and Load World boxes and the mod's info box each hide it, closing them brings it back.</summary>
		static IEnumerator NoticeCoverRoutine(Action<bool, string> check)
		{
			yield return new WaitForSecondsRealtime(1.2f);
			check(!ExperimentalNotice.Hidden && ExperimentalNotice.OtherWindow() == null, "on the plain main menu the box shows (nothing else open: " + (ExperimentalNotice.OtherWindow() ?? "none") + ")");
			foreach (Type t in new[] { typeof(NewGameBox), typeof(LoadGameBox) })
			{
				MenuBox box = Resources.FindObjectsOfTypeAll(t).OfType<MenuBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
				if (box == null) { check(false, t.Name + " not found on the main menu"); continue; }
				try { box.Close(); } catch { } box.Open(); // (Raft's Open subscribes to input changes each time, Close unsubscribes: never open twice)
				yield return new WaitForSecondsRealtime(1.5f);
				check(box.IsOpen && ExperimentalNotice.Hidden, t.Name + " open: the alpha box is hidden (" + (ExperimentalNotice.OtherWindow() ?? "nothing seen") + ")");
				Screenshot(new[] { "notice_under_" + t.Name });
				yield return new WaitForSecondsRealtime(0.6f);
				box.Close();
				yield return new WaitForSecondsRealtime(1.5f);
				check(!box.IsOpen && !ExperimentalNotice.Hidden, t.Name + " closed: the box is back");
			}
			InfoWindow.OpenReport();
			yield return null;
			yield return null;
			check(ExperimentalNotice.Hidden, "Report a problem's box open: the alpha box is hidden");
			InfoWindow.Close();
			yield return null;
			yield return null;
			check(!ExperimentalNotice.Hidden, "... and back when it closes");
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
				check(notice.Contains("place="), "the place is remembered (notice.txt)");
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
