using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Progression (the World Plans window, TODO 4c step 3): what a plan gives of Raft's progression, in its order - which
	/// rule's islands give which of Raft's story-island blueprints and story items, what their locks want and whether an
	/// earlier island gave it, the blueprints the story islands it keeps give, and those never given.
	/// </summary>
	public class PlanProgressWindow : MonoBehaviour
	{
		static PlanProgressWindow instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		Text titleText, summaryText;
		RectTransform list;
		ScrollRect scroll;
		Action<int> showRule;

		/// <summary>What the panel shows now (tests): the progression and its rows' text.</summary>
		public static PlanChecker.Progress Shown { get; private set; }
		public static List<string> Rows { get; private set; }

		public static void Create(Transform canvas)
		{
			var blocker = new GameObject("PlanProgressWindow", typeof(RectTransform), typeof(Image));
			blocker.layer = 5;
			blocker.transform.SetParent(canvas, false);
			UIKit.Stretch((RectTransform)blocker.transform);
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
			instance = blocker.AddComponent<PlanProgressWindow>();
			instance.Build();
			blocker.SetActive(false);
		}

		/// <summary>Shows the progression of plan; showRule(i) = show rule i's card.</summary>
		public static void Open(WorldPlan plan, Action<int> showRule)
		{
			if (instance == null || plan == null) return;
			instance.showRule = showRule;
			instance.Fill(plan, PlanChecker.Progression(plan, true));
			instance.gameObject.SetActive(true);
			instance.transform.SetAsLastSibling();
		}

		public static void Close()
		{
			if (instance == null) return;
			if (instance.gameObject.activeSelf) EditorInput.SubWindowClosedFrame = Time.frameCount;
			instance.gameObject.SetActive(false);
		}

		void Update()
		{
			if (Input.GetKeyDown(KeyCode.Escape)) Close();
		}

		void Build()
		{
			RectTransform panel = UIKit.Panel(transform, "Panel", new RectOffset(16, 16, 14, 16), 8f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 0));
			RectTransform head = UIKit.Row(panel, 28f, 6f, "Head");
			titleText = UIKit.Label(head, "PROGRESSION", 18, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			summaryText = UIKit.Label(panel, "", 13, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Summary");
			summaryText.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(summaryText.gameObject, -1, 40);
			RectTransform box = UIKit.Rect("Steps", panel);
			UIKit.Size(box.gameObject, -1, 470);
			UIKit.Background(box.gameObject, UIKit.GroupBg, 6);
			list = UIKit.ScrollList(box, out scroll, 6f);
			UIKit.Stretch((RectTransform)scroll.transform, 6, 6, 6, 6);
			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			UIKit.Label(buttons, "In the plan's order. Blueprints: Raft's from its story islands. Story items: keys, keycards... that open locks.", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic);
			Button close = UIKit.Button(buttons, "Close", Close, "Back to the plan (Esc)", 110, 34f, 13);
			UIKit.Primary(close);
		}

		static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }
		static string Blueprints(IEnumerable<string> b) { return string.Join(", ", b.Select(PlanChecker.Pretty).ToArray()); }

		void Fill(WorldPlan plan, PlanChecker.Progress p)
		{
			Shown = p;
			Rows = new List<string>();
			foreach (Transform child in list) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
			titleText.text = "PROGRESSION: " + plan.Name.ToUpperInvariant();
			string warn = Hex(PlanCheckWindow.ColorOf(PlanChecker.Level.Warning));
			int byIslands = p.Steps.Sum(s => s.Blueprints.Count(b => !p.Kept.Contains(b)));
			summaryText.text = p.All == 0 ? "Raft's list of story-island blueprints couldn't be read."
				: "Of Raft's " + p.All + " story-island blueprints: " + p.Kept.Count + " on the story islands the plan keeps, " + byIslands + " more from its islands" +
					(p.Missing.Count > 0 ? ", <color=" + warn + ">" + p.Missing.Count + " never given</color>." : " - <color=#8fdc8f>every one is given.</color>");
			if (p.Kept.Count > 0)
				Item(-1, "RAFT'S STORY", "The story islands the plan keeps give: " + Blueprints(p.Kept) + ".", "");
			foreach (PlanChecker.Progress.Step s in p.Steps)
			{
				IntroRule r = plan.Rules[s.Rule];
				var lines = new List<string>();
				if (s.Blueprints.Count > 0) lines.Add("<b>Blueprints:</b> " + Blueprints(s.Blueprints));
				if (s.Gives.Count > 0) lines.Add("<b>Gives story items:</b> " + string.Join(", ", s.Gives.ToArray()));
				if (s.Needs.Count > 0) lines.Add("<b>Its locks want:</b> " + string.Join(", ", s.Needs.Select(n => s.NeedsLate.Contains(n) ? "<color=" + warn + ">" + n + " (no island before gives it)</color>" : n).ToArray()));
				Item(s.Rule, "RULE " + (s.Rule + 1), (r.Id.Length > 0 ? "'" + r.Id + "' - " : "") + (r.WhatArg.Length > 0 ? r.WhatArg : r.What) + "\n" + string.Join("\n", lines.ToArray()), "");
			}
			if (p.Missing.Count > 0)
				Item(-1, "NEVER GIVEN", "<color=" + warn + ">" + Blueprints(p.Missing) + "</color>",
					"Give each as a quest reward, in a chest's loot or from a note on one of the plan's islands - or keep the story islands that carry them.");
			if (p.Steps.Count == 0 && p.Kept.Count == 0 && p.Missing.Count == 0) UIKit.Label(list, "<i>Nothing to show.</i>", 13, UIKit.TextMuted);
			scroll.verticalNormalizedPosition = 1f;
		}

		void Item(int rule, string tagText, string body, string fixText)
		{
			Rows.Add(tagText + ": " + body.Replace("\n", " | ") + (fixText.Length > 0 ? " (" + fixText + ")" : ""));
			RectTransform item = UIKit.Rect("Step", list);
			UIKit.Background(item.gameObject, new Color(0.2f, 0.11f, 0.05f, 0.45f), 6);
			UIKit.Vertical(item.gameObject, 3f, new RectOffset(10, 10, 6, 8));
			RectTransform top = UIKit.Row(item, 22f, 8f, "Top");
			Text tag = UIKit.Label(top, tagText, 12, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Tag");
			UIKit.Size(tag.gameObject, 120);
			UIKit.Label(top, "", 12, UIKit.TextMuted);
			if (rule >= 0 && showRule != null)
				UIKit.Button(top, "Show rule " + (rule + 1), () => { Close(); showRule(rule); }, "Close the panel and scroll to this rule's card", 120, 22f, 11);
			Text text = UIKit.Label(item, body, 13, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Text");
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			if (fixText.Length > 0)
			{
				Text fix = UIKit.Label(item, "<b>How to fix:</b> " + fixText, 12, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Italic, "Fix");
				fix.horizontalOverflow = HorizontalWrapMode.Wrap;
			}
		}
	}
}
