using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "Islands while sailing" in the New Game box's World settings window: a button ("CHOOSE ISLANDS...  12 of 14") that
	/// opens a list of everything that can turn up by chance while sailing - the saved islands, the map types and brand-new
	/// generated islands of spawnpool.txt - each with a tick box. Untick what the world being created shouldn't have
	/// (WorldIslands.Chosen); a search field narrows the list, and "Tick shown" / "Untick shown" do the rows shown. The
	/// choice is the world's when Create is pressed, and the next world starts from it.
	/// </summary>
	public static class IslandPickerWindow
	{
		public const string WindowName = "CustomIslands_IslandPicker", EntryName = "CustomIslands_Islands", RowPrefix = "Pick_";

		static RectTransform window, list;
		static Button entryButton;
		static Text entryText, countText, planNote;
		static InputField search;
		static readonly Dictionary<string, Button> rows = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

		public static bool IsOpen { get { return window != null && window.gameObject.activeSelf; } }
		public static RectTransform Window { get { return window; } }
		public static Button EntryButton { get { return entryButton; } }
		public static InputField Search { get { return search; } }
		public static Text Count { get { return countText; } }
		/// <summary>The row of a pool entry (tests click it as a player would), or null.</summary>
		public static Button Row(string entry) { Button b; return rows.TryGetValue(entry, out b) ? b : null; }
		/// <summary>The entries whose rows are shown (not hidden by the search).</summary>
		public static List<string> Shown() { return rows.Where(kv => kv.Value != null && kv.Value.gameObject.activeSelf).Select(kv => kv.Key).ToList(); }

		/// <summary>The group in the World settings window (parent), and the list's window over the whole menu (screen).</summary>
		internal static void BuildEntry(Transform parent, Transform screen)
		{
			RectTransform group = UIKit.Group(parent, "Islands while sailing", EntryName);
			UIKit.Hint(group.gameObject, "Which of your islands - and which kinds of new ones - turn up by chance while you sail in this world. Untick the ones it shouldn't have.");
			entryButton = UIKit.Button(group, "", Open, "Choose the islands that turn up by chance while sailing in this world", -1, 28f, 14);
			entryText = UIKit.Label(group, "", 11, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Italic, "Detail");
			entryText.horizontalOverflow = HorizontalWrapMode.Wrap;
			entryText.verticalOverflow = VerticalWrapMode.Truncate;
			UIKit.Size(entryText.gameObject, -1, 28);
			// How often: one random custom island after every <min>-<max> of Raft's own islands (default 3-6; min 2-20, max 4-50)
			const string gapHint = "How often an island from this list turns up: one after every so many of Raft's own islands you meet - a number between these two each time. 3-6 by default; the lowest 2-4, the highest 20-50. However many islands are ticked, they never crowd the sea.";
			RectTransform gapRow = UIKit.Row(group, 26f, 4f, "Gap");
			Text lead = UIKit.Label(gapRow, "One after every", 12, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "GapLead");
			UIKit.Size(lead.gameObject, 104);
			UIKit.Button(gapRow, "-", () => StepGap(-1, 0), gapHint, 24, 24f, 14).name = "GapMinLess";
			gapMinText = UIKit.Label(gapRow, "", 13, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, "GapMin");
			UIKit.Size(gapMinText.gameObject, 26);
			UIKit.Button(gapRow, "+", () => StepGap(1, 0), gapHint, 24, 24f, 14).name = "GapMinMore";
			Text to = UIKit.Label(gapRow, "to", 12, UIKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Normal, "GapTo");
			UIKit.Size(to.gameObject, 20);
			UIKit.Button(gapRow, "-", () => StepGap(0, -1), gapHint, 24, 24f, 14).name = "GapMaxLess";
			gapMaxText = UIKit.Label(gapRow, "", 13, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, "GapMax");
			UIKit.Size(gapMaxText.gameObject, 26);
			UIKit.Button(gapRow, "+", () => StepGap(0, 1), gapHint, 24, 24f, 14).name = "GapMaxMore";
			UIKit.Label(gapRow, "of Raft's islands", 12, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "GapTail");
			UIKit.Hint(gapRow.gameObject, gapHint);
			Build(screen);
			ShowEntry();
		}

		static Text gapMinText, gapMaxText;

		/// <summary>The World settings window's - and + by the span (tests click them as a player would).</summary>
		internal static void StepGap(int min, int max)
		{
			int[] g = WorldIslands.ChosenGap;
			int lo = g[0] + min, hi = g[1];
			// (the top goes in ones up to 10, then in fives: 10, 15, 20 ... 50)
			if (max > 0) hi = hi < 10 ? hi + 1 : (hi / 5 + 1) * 5;
			else if (max < 0) hi = hi <= 10 ? hi - 1 : ((hi - 1) / 5) * 5;
			if (lo > hi) hi = lo;
			WorldIslands.SetChosenGap(lo, hi);
			ShowEntry();
		}

		static void Build(Transform screen)
		{
			if (window != null) UnityEngine.Object.Destroy(window.gameObject);
			window = UIKit.Rect(WindowName, screen);
			UIKit.Stretch(window);
			window.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f); // (what is behind it dimmed, its clicks taken)
			RectTransform panel = UIKit.Panel(window, "Panel", new RectOffset(18, 18, 12, 14), 8f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 0f));
			RectTransform head = UIKit.Row(panel, 30f, 6f, "Head");
			Text title = UIKit.Label(head, "ISLANDS WHILE SAILING", 22, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			UIKit.UseTitleFont(title);
			UIKit.Label(head, "for the world you create", 12, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Italic);
			Text intro = UIKit.Label(panel, "These turn up by chance while you sail. Untick the ones this world shouldn't have. Islands you make or download later join in, unless you untick them for a later world. \"Brand-new generated islands\": the mod makes up a random island now and then (saved as gen-... in this world); unticked, only made or downloaded islands and map types come. (In a world, the host can change it with the console command WorldIslands.)",
				12, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Intro");
			intro.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(intro.gameObject, -1, 46);
			planNote = UIKit.Label(panel, "", 12, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Italic, "PlanNote");
			planNote.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(planNote.gameObject, -1, 30);

			RectTransform tools = UIKit.Row(panel, 30f, 6f, "Tools");
			search = UIKit.Field(tools, "Search...", "", 30f, "Show only the islands whose name holds this");
			search.onValueChanged.AddListener(Filter);
			UIKit.Button(tools, "Tick shown", () => SetShown(true), "Tick every island shown (all of them, or the ones the search found)", 130, 30f, 13);
			UIKit.Button(tools, "Untick shown", () => SetShown(false), "Untick every island shown (all of them, or the ones the search found)", 130, 30f, 13);

			RectTransform listBox = UIKit.Rect("ListBox", panel);
			UIKit.Size(listBox.gameObject, -1, 360);
			UIKit.Background(listBox.gameObject, UIKit.FieldBg, 6);
			ScrollRect scroll;
			list = UIKit.ScrollList(listBox, out scroll, 3f);
			UIKit.Stretch((RectTransform)scroll.transform, 4, 4, 4, 4);

			countText = UIKit.Label(panel, "", 13, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Count");
			UIKit.Size(countText.gameObject, -1, 22);
			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			UIKit.Label(buttons, "", 12);
			Button done = UIKit.Button(buttons, "Done", Close, "Keep this choice for the world you create", 150, 34f, 15);
			UIKit.Primary(done);
			window.gameObject.SetActive(false);
		}

		public static void Open()
		{
			if (window == null) return;
			window.gameObject.SetActive(true);
			window.SetAsLastSibling();
			if (search != null) search.text = "";
			Refresh();
		}

		public static void Close()
		{
			if (window != null) window.gameObject.SetActive(false);
			ShowEntry();
		}

		/// <summary>One row per pool entry: first the kinds of new islands, then the saved islands by name.</summary>
		static void Refresh()
		{
			foreach (Transform child in list) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
			rows.Clear();
			List<string> all = WorldIslands.Candidates();
			List<string> kinds = all.Where(e => !WorldIslands.IsIsland(e)).ToList();
			List<string> islands = all.Where(WorldIslands.IsIsland).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
			if (all.Count == 0) Header("Nothing can turn up while sailing: every island is left out (Defaults... in World settings).");
			if (kinds.Count > 0) { Header("NEW EACH TIME"); foreach (string e in kinds) AddRow(e); }
			if (islands.Count > 0) { Header("YOUR ISLANDS (" + islands.Count + ")"); foreach (string e in islands) AddRow(e); }
			ShowEntry();
		}

		static void Header(string text)
		{
			Text h = UIKit.Label(list, text, 12, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Header");
			UIKit.Size(h.gameObject, -1, 22);
		}

		static void AddRow(string entry)
		{
			Button b = UIKit.Button(list, WorldIslands.Label(entry), () => Flip(entry), WorldIslands.Hint(entry), -1, 28f, 13);
			b.name = RowPrefix + entry;
			UIKit.Flat(b);
			Text label = UIKit.LabelOf(b);
			label.alignment = TextAnchor.MiddleLeft;
			label.rectTransform.offsetMin = new Vector2(38, 0);
			label.rectTransform.offsetMax = new Vector2(-230, 0);
			UIKit.Fit(label, 9);
			// The tick box: a frame with a golden square in it while the island takes part
			RectTransform box = UIKit.Rect("Box", b.transform);
			box.anchorMin = box.anchorMax = new Vector2(0f, 0.5f);
			box.pivot = new Vector2(0f, 0.5f);
			box.anchoredPosition = new Vector2(10f, 0f);
			box.sizeDelta = new Vector2(18f, 18f);
			UIKit.Background(box.gameObject, UIKit.FieldBg, 3);
			UIKit.Border(box, UIKit.Tan, 3, 1.5f);
			RectTransform tick = UIKit.Rect("Tick", box);
			UIKit.Stretch(tick, 4, 4, 4, 4);
			UIKit.Background(tick.gameObject, UIKit.Accent, 2);
			Text meta = UIKit.Label(b.transform, WorldIslands.Detail(entry), 11, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Normal, "Meta");
			UIKit.Stretch(meta.rectTransform, 10, 10, 0, 0);
			rows[entry] = b;
			Look(entry);
		}

		/// <summary>Whether an entry's row shows it taking part (its tick box ticked).</summary>
		public static bool IsTicked(string entry)
		{
			Button b = Row(entry);
			Transform tick = b != null ? b.transform.Find("Box/Tick") : null;
			return tick != null && tick.gameObject.activeSelf;
		}

		static void Look(string entry)
		{
			Button b = Row(entry);
			if (b == null) return;
			bool on = !WorldIslands.Chosen.Contains(entry);
			Transform tick = b.transform.Find("Box/Tick");
			if (tick != null) tick.gameObject.SetActive(on);
			UIKit.LabelOf(b).color = on ? UIKit.TextColor : UIKit.TextMuted;
		}

		public static void Flip(string entry)
		{
			if (!WorldIslands.Chosen.Remove(entry)) WorldIslands.Chosen.Add(entry);
			Look(entry);
			ShowEntry();
		}

		static void SetShown(bool on)
		{
			foreach (string entry in Shown())
			{
				if (on) WorldIslands.Chosen.Remove(entry); else WorldIslands.Chosen.Add(entry);
				Look(entry);
			}
			ShowEntry();
		}

		static void Filter(string text)
		{
			string t = (text ?? "").Trim();
			foreach (var kv in rows)
				if (kv.Value != null) kv.Value.gameObject.SetActive(t.Length == 0 || WorldIslands.Label(kv.Key).IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		/// <summary>The counts on the World settings button and in the list, and what the chosen plan means for them.</summary>
		internal static void ShowEntry()
		{
			List<string> all = WorldIslands.Candidates();
			HashSet<string> off = WorldIslands.Chosen;
			int taking = all.Count(e => !off.Contains(e));
			if (entryButton != null) UIKit.LabelOf(entryButton).text = "CHOOSE ISLANDS...   " + (taking == all.Count ? "all " + all.Count : taking == 0 ? "none" : taking + " of " + all.Count);
			WorldPlan plan = WorldPlan.Load(NewWorldOptions.Selected);
			bool random = plan == null || plan.Random;
			string note = random ? "" : "The plan '" + plan.Name + "' has no random islands, so this list only counts with a plan that has them (like Random islands).";
			if (entryText != null) entryText.text = random ? (taking == all.Count ? "Every island can turn up while sailing." : taking == 0 ? "No island turns up by chance while sailing (a world plan's own islands still come)." : (all.Count - taking) + " left out of this world.") + (off.Contains(CustomIslandSpawner.GeneratedEntry) ? " No made-up islands." : "") : note;
			if (planNote != null) planNote.text = random ? "Plan: '" + (plan != null ? plan.Name : WorldPlan.RandomName) + "' - islands from this list turn up by chance while sailing (a world plan's own islands only come in their plan)." : note;
			if (countText != null) countText.text = taking + " of " + all.Count + " take part" + (taking < all.Count ? ",  " + (all.Count - taking) + " left out" : "");
			int[] gap = WorldIslands.ChosenGap;
			if (gapMinText != null) gapMinText.text = gap[0].ToString();
			if (gapMaxText != null) gapMaxText.text = gap[1].ToString();
		}
	}
}
