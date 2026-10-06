using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A modal list to pick one thing from (a saved island, a map type, a world plan): a search field and one row per
	/// choice with a short description. Clicking a row picks it and closes the window.
	/// </summary>
	public class ChoiceWindow : MonoBehaviour
	{
		public class Choice
		{
			public string Value, Label, Detail;
			public Choice(string value, string label, string detail = "") { Value = value; Label = label; Detail = detail ?? ""; }
		}

		static ChoiceWindow instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		Text titleText, countText;
		InputField search;
		RectTransform list;
		Action<string> onPick;
		readonly List<KeyValuePair<string, GameObject>> rows = new List<KeyValuePair<string, GameObject>>();
		List<Choice> shown = new List<Choice>();
		string emptyText;

		/// <summary>Tests: the title and the values listed now.</summary>
		public static string Title { get { return instance != null ? instance.titleText.text : ""; } }
		public static List<string> Values { get { return instance != null && IsOpen ? instance.shown.Select(c => c.Value).ToList() : new List<string>(); } }
		public static string EmptyText { get { return instance != null && IsOpen && instance.shown.Count == 0 ? instance.emptyText : null; } }
		public static List<Choice> Shown { get { return instance != null && IsOpen ? instance.shown.ToList() : new List<Choice>(); } }

		/// <summary>Tests: picks a value as a click on its row does (false if it isn't listed).</summary>
		public static bool PickValue(string value)
		{
			if (!IsOpen || !instance.shown.Any(c => c.Value == value)) return false;
			Action<string> pick = instance.onPick;
			Close();
			if (pick != null) pick(value);
			return true;
		}

		public static void Create(Transform canvas)
		{
			var blocker = new GameObject("ChoiceWindow", typeof(RectTransform), typeof(Image));
			blocker.layer = 5;
			blocker.transform.SetParent(canvas, false);
			UIKit.Stretch((RectTransform)blocker.transform);
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
			instance = blocker.AddComponent<ChoiceWindow>();
			instance.Build();
			blocker.SetActive(false);
		}

		/// <summary>A list to choose from; empty (optional) says why there is nothing in it.</summary>
		public static void Open(string title, IEnumerable<Choice> choices, Action<string> pick, string empty = null)
		{
			if (instance == null) return;
			instance.emptyText = empty;
			instance.titleText.text = title.ToUpperInvariant();
			instance.onPick = pick;
			instance.Fill(choices.ToList());
			instance.search.text = "";
			instance.gameObject.SetActive(true);
			instance.transform.SetAsLastSibling();
		}

		public static void Close()
		{
			if (instance == null) return;
			if (instance.gameObject.activeSelf) EditorInput.SubWindowClosedFrame = Time.frameCount;
			instance.gameObject.SetActive(false);
			instance.onPick = null;
			EditorInput.IsTyping = false;
		}

		void Update()
		{
			EditorInput.IsTyping = search.isFocused;
			if (Input.GetKeyDown(KeyCode.Escape)) Close();
		}

		void OnDisable() { EditorInput.IsTyping = false; }

		void Build()
		{
			RectTransform panel = UIKit.Panel(transform, "Panel", new RectOffset(16, 16, 14, 16), 10f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 0));
			RectTransform head = UIKit.Row(panel, 28f, 6f, "Head");
			titleText = UIKit.Label(head, "", 18, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			countText = UIKit.Label(head, "", 12, UIKit.TextMuted, TextAnchor.MiddleRight);
			search = UIKit.Field(panel, "Search...", "", 30f, "Type part of a name");
			search.onValueChanged.AddListener(v => Filter());
			RectTransform box = UIKit.Rect("List", panel);
			UIKit.Size(box.gameObject, -1, 380);
			ScrollRect scroll;
			list = UIKit.ScrollList(box, out scroll, 4f);
			UIKit.Stretch((RectTransform)scroll.transform);
			RectTransform buttons = UIKit.Row(panel, 32f, 8f, "Buttons");
			UIKit.Size(UIKit.Label(buttons, "", 12, UIKit.TextMuted).gameObject, -1, -1, 1);
			UIKit.Button(buttons, "Cancel", Close, "Close without choosing (Esc)", 100, 32);
		}

		void Fill(List<Choice> choices)
		{
			foreach (Transform child in list) Destroy(child.gameObject);
			rows.Clear();
			shown = choices;
			foreach (Choice c in choices)
			{
				Choice choice = c;
				Button b = UIKit.Button(list, "", () => { Action<string> pick = onPick; Close(); if (pick != null) pick(choice.Value); }, choice.Detail.Length > 0 ? choice.Detail : null, -1, 34f, 13);
				Text t = UIKit.LabelOf(b);
				t.alignment = TextAnchor.MiddleLeft;
				t.text = choice.Label + (choice.Detail.Length > 0 ? "   <color=#b89e70><size=11>" + choice.Detail + "</size></color>" : "");
				rows.Add(new KeyValuePair<string, GameObject>((choice.Label + " " + choice.Value + " " + choice.Detail).ToLowerInvariant(), b.gameObject));
			}
			if (choices.Count == 0)
			{
				Text none = UIKit.Label(list, "<i>" + (string.IsNullOrEmpty(emptyText) ? "Nothing to choose from yet." : emptyText) + "</i>", 13, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Normal, "Empty");
				none.horizontalOverflow = HorizontalWrapMode.Wrap;
			}
			Filter();
		}

		void Filter()
		{
			string q = search.text.Trim().ToLowerInvariant();
			int shown = 0;
			foreach (var r in rows)
			{
				bool on = q.Length == 0 || r.Key.Contains(q);
				r.Value.SetActive(on);
				if (on) shown++;
			}
			countText.text = shown + " of " + rows.Count;
		}

		/// <summary>Saved islands to choose from (not downloaded copies or islands generated while sailing).</summary>
		public static IEnumerable<Choice> Islands()
		{
			return IslandSpawner.ListSavedIslands().Where(n => !IslandNetwork.IsDownloadName(n) && !n.StartsWith(CustomIslandSpawner.GeneratedPrefix, StringComparison.OrdinalIgnoreCase))
				.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
				.Select(n => new Choice(n, n, ObjectProps.Get(IslandCache.Props(n), IslandProps.Title)));
		}

		public static IEnumerable<Choice> Types()
		{
			return MapTypes.All.Select(t => new Choice(t.Name, t.Label, t.Description));
		}
	}
}
