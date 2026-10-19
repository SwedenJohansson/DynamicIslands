using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// The "Islands while sailing" list's rows (IslandPickerWindow): picture, description, QUEST / no quest badge, A to Z,
	/// Tick all / Untick all, the search, and the choice kept after Done.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIIslandPickerRows", docs: "Dev, main menu: World settings > CHOOSE ISLANDS... - the rows show a picture, the description and the right quest badge (Rookery Cliffs: no quest; an island with a quest: QUEST), islands A to Z, the window smaller than the island library, search + a row's tick + Tick shown, Tick all / Untick all, and Done keeps the choice (reopened, it shows). Screenshots picker_top, picker_quest, picker_search, picker_alloff. Leaves every island ticked")]
		public static void IslandPickerRowsCommand(string[] args) { StartTest(IslandPickerRowsRoutine()); }

		static IEnumerator IslandPickerRowsRoutine()
		{
			const string noQuest = "Rookery Cliffs";
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("picker rows: no New Game box (main menu?)"); yield break; }
			bool ok = true;
			box.gameObject.SetActive(true);
			try { box.Close(); } catch { } box.Open();
			yield return new WaitForSecondsRealtime(1f);
			WorldSettingsWindow.Open();
			yield return null;
			Button entry = IslandPickerWindow.EntryButton;
			if (entry == null) { Fail("picker rows: no CHOOSE ISLANDS button"); yield break; }
			List<string> all = WorldIslands.Candidates();
			Check(ref ok, all.Contains(noQuest, StringComparer.OrdinalIgnoreCase), "'" + noQuest + "' is in the list (library island, installed)");
			// The quest island: the first in the list whose file has a quest (read here, not from the cache)
			string quest = null;
			foreach (string e in all.Where(WorldIslands.IsIsland).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).Take(80))
			{
				try { if (IslandQuest.HasQuest(IslandFile.Load(IslandSpawner.PathFor(e)).Props)) { quest = e; break; } } catch { }
			}
			Check(ref ok, quest != null, "an island with a quest is in the list: " + quest);
			bool rookeryHasQuest = true;
			string rookeryDesc = "";
			try { var props = IslandFile.Load(IslandSpawner.PathFor(noQuest)).Props; rookeryHasQuest = IslandQuest.HasQuest(props); rookeryDesc = ObjectProps.Get(props, IslandProps.Description).Trim(); } catch { }
			Check(ref ok, !rookeryHasQuest, "'" + noQuest + "' has no quest in its file");

			WorldIslands.Chosen.Clear();
			entry.onClick.Invoke();
			yield return null;
			Check(ref ok, IslandPickerWindow.IsOpen, "the button opens the list");
			float until = Time.realtimeSinceStartup + 90f;
			while (!IslandPickerWindow.Loaded && Time.realtimeSinceStartup < until) yield return null;
			Check(ref ok, IslandPickerWindow.Loaded, "the descriptions and badges are filled in");

			// Compact: smaller than the island library (1240 x 720)
			Canvas.ForceUpdateCanvases();
			RectTransform panel = (RectTransform)IslandPickerWindow.Window.Find("Panel");
			Check(ref ok, panel != null && panel.rect.width <= 720f && panel.rect.height <= 680f, "the window is compact: " + (panel != null ? panel.rect.width.ToString("0") + " x " + panel.rect.height.ToString("0") : "no panel"));
			var off = OffScreen(IslandPickerWindow.Window.gameObject, Screen.width, Screen.height);
			Check(ref ok, off.Count == 0, "the list fits the screen" + (off.Count > 0 ? " - off: " + string.Join(", ", off.Take(4).ToArray()) : ""));

			// A to Z: the island rows in the list's order
			List<string> islandOrder = all.Where(WorldIslands.IsIsland).Select(e => new { e, b = IslandPickerWindow.Row(e) }).Where(x => x.b != null)
				.OrderBy(x => x.b.transform.GetSiblingIndex()).Select(x => x.e).ToList();
			List<string> sorted = islandOrder.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
			Check(ref ok, islandOrder.Count > 0 && islandOrder.SequenceEqual(sorted), "the islands are A to Z (" + string.Join(", ", islandOrder.Take(4).ToArray()) + " ...)");
			List<string> kinds = all.Where(e => !WorldIslands.IsIsland(e)).ToList();

			// Badges, descriptions
			Check(ref ok, IslandPickerWindow.BadgeOf(noQuest) == "no quest", "'" + noQuest + "' shows 'no quest' (" + IslandPickerWindow.BadgeOf(noQuest) + ")");
			if (quest != null) Check(ref ok, IslandPickerWindow.BadgeOf(quest) == "QUEST", "'" + quest + "' shows QUEST (" + IslandPickerWindow.BadgeOf(quest) + ")");
			string shownDesc = IslandPickerWindow.DescriptionOf(noQuest) ?? "";
			Check(ref ok, rookeryDesc.Length == 0 ? shownDesc == "(no description)" : shownDesc.StartsWith(rookeryDesc.Replace("\n", " ").Substring(0, Math.Min(30, rookeryDesc.Length))),
				"'" + noQuest + "' shows its description: '" + (shownDesc.Length > 60 ? shownDesc.Substring(0, 60) + "..." : shownDesc) + "'");
			Check(ref ok, kinds.All(k => IslandPickerWindow.BadgeOf(k) == "NEW EACH TIME" && (IslandPickerWindow.DescriptionOf(k) ?? "").Length > 0), "the map-type and generated rows say NEW EACH TIME and what they are");
			string small = IslandPickerWindow.SmallPrintOf(noQuest) ?? "";
			Check(ref ok, (small.StartsWith("library island") || small.StartsWith("your island")) && small.Contains(" KB") && small.Contains(" - "), "'" + noQuest + "' small print: '" + small + "'");

			// Pictures (maps made once in the background, cached after)
			until = Time.realtimeSinceStartup + 120f;
			while ((IslandPickerWindow.PictureOf(noQuest) == null || (quest != null && IslandPickerWindow.PictureOf(quest) == null)) && Time.realtimeSinceStartup < until) yield return null;
			Check(ref ok, IslandPickerWindow.PictureOf(noQuest) != null && (quest == null || IslandPickerWindow.PictureOf(quest) != null), "the islands' rows have a picture" + (IslandPickerWindow.HasLibraryPicture(noQuest) ? " (" + noQuest + ": the library's)" : ""));
			Check(ref ok, kinds.All(k => IslandPickerWindow.PictureOf(k) != null) || RaftIslands.All.Count == 0, "the map-type rows have one of Raft's islands' pictures");
			Check(ref ok, System.IO.File.Exists(IslandThumbs.PngPath(noQuest)), "the map is kept in thumbcache");
			yield return new WaitForSecondsRealtime(1.5f);
			Screenshot(new[] { "picker_top" });
			yield return new WaitForSecondsRealtime(0.5f);

			// The search, a row's tick, Tick shown
			if (quest != null)
			{
				IslandPickerWindow.Search.text = quest; yield return new WaitForSecondsRealtime(0.5f);
				Screenshot(new[] { "picker_quest" });
				yield return new WaitForSecondsRealtime(0.5f);
			}
			IslandPickerWindow.Search.text = "Rookery"; yield return null;
			List<string> shown = IslandPickerWindow.Shown();
			Check(ref ok, shown.Contains(noQuest, StringComparer.OrdinalIgnoreCase) && shown.All(e => WorldIslands.Label(e).IndexOf("Rookery", StringComparison.OrdinalIgnoreCase) >= 0),
				"searching 'Rookery' shows only its rows (" + shown.Count + ")");
			IslandPickerWindow.Row(noQuest).onClick.Invoke(); yield return null;
			Check(ref ok, !IslandPickerWindow.IsTicked(noQuest) && WorldIslands.Chosen.Contains(noQuest), "a click on its row unticks it");
			yield return new WaitForSecondsRealtime(0.5f);
			Screenshot(new[] { "picker_search" });
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, Click(IslandPickerWindow.Window.gameObject, "Tick shown"), "Tick shown clicked");
			yield return null;
			Check(ref ok, IslandPickerWindow.IsTicked(noQuest) && WorldIslands.Chosen.Count == 0, "Tick shown ticks it again");
			IslandPickerWindow.Search.text = ""; yield return null;

			// Tick all / Untick all (one button, whatever the search shows)
			Button allB = IslandPickerWindow.AllButton;
			Check(ref ok, allB != null && UIKit.LabelOf(allB).text == "Untick all", "with every row ticked the button says Untick all");
			IslandPickerWindow.Search.text = "Rookery"; yield return null;
			allB.onClick.Invoke(); yield return null;
			IslandPickerWindow.Search.text = ""; yield return null;
			Check(ref ok, all.All(e => !IslandPickerWindow.IsTicked(e)) && WorldIslands.Chosen.IsSupersetOf(all) && UIKit.LabelOf(allB).text == "Tick all" && UIKit.LabelOf(entry).text.Contains("none"),
				"Untick all unticks every row, not only those shown (button: '" + UIKit.LabelOf(entry).text + "')");
			yield return new WaitForSecondsRealtime(0.5f);
			Screenshot(new[] { "picker_alloff" });
			yield return new WaitForSecondsRealtime(0.5f);
			allB.onClick.Invoke(); yield return null;
			Check(ref ok, all.All(IslandPickerWindow.IsTicked) && WorldIslands.Chosen.Count == 0 && UIKit.LabelOf(allB).text == "Untick all", "Tick all ticks them all again");

			// Done keeps the choice; reopened, the list shows it
			IslandPickerWindow.Row(noQuest).onClick.Invoke(); yield return null;
			Check(ref ok, Click(IslandPickerWindow.Window.gameObject, "Done"), "Done clicked");
			yield return null;
			Check(ref ok, !IslandPickerWindow.IsOpen && WorldIslands.Pending != null && WorldIslands.Pending.Count == 1 && WorldIslands.Pending.Contains(noQuest),
				"Done keeps '" + noQuest + "' left out for the next world (button: '" + UIKit.LabelOf(entry).text + "')");
			entry.onClick.Invoke();
			yield return null;
			Check(ref ok, IslandPickerWindow.IsOpen && !IslandPickerWindow.IsTicked(noQuest) && quest != null && IslandPickerWindow.IsTicked(quest), "reopened, '" + noQuest + "' is still unticked");
			WorldIslands.Chosen.Clear();
			IslandPickerWindow.Close();
			WorldSettingsWindow.Close();
			box.gameObject.SetActive(false);
			if (ok) Log("PASS: island picker rows"); else Fail("island picker rows");
		}
	}
}
