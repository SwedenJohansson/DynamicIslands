using System;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The main menu's "EXPERIMENTAL ALPHA RELEASE" box: this is the mod's first release, so new players are told that things are
	/// likely to change, some systems might be unstable and progress isn't guaranteed to be saved. It starts on the right
	/// half of Raft's main menu, clear of the menu buttons and the New Game box, and can be dragged anywhere (the place is
	/// remembered). Its buttons lead to help: the Discord server, the guide (PDF) and Report a problem. "Got it" folds it
	/// into a slim bar with those buttons (Show opens it again); that is remembered for this version of the mod only
	/// (Mods\DynamicIslands\notice.txt), so a new version shows the whole box again.
	/// </summary>
	public static class ExperimentalNotice
	{
		public const string PanelName = "CustomIslands_ExperimentalNotice";
		public const string Header = "EXPERIMENTAL ALPHA RELEASE";
		public static readonly string[] Points =
		{
			"Things are likely to change.",
			"Some systems might be unstable.",
			"We do not guarantee that progress is always saved.",
		};
		const string FileName = "notice.txt";
		/// <summary>Where the box starts: its top middle, as a part of the menu's width and height.</summary>
		static readonly Vector2 StartAnchor = new Vector2(0.72f, 0.88f);
		const float Width = 540f;

		static RectTransform panel, body;
		static Button toggle, discordButton, guideButton, reportButton;

		public static RectTransform Panel { get { return panel; } }
		public static bool Folded { get { return body != null && !body.gameObject.activeSelf; } }
		public static Button Toggle { get { return toggle; } }
		public static Button DiscordButton { get { return discordButton; } }
		public static Button GuideButton { get { return guideButton; } }
		public static Button ReportButton { get { return reportButton; } }

		/// <summary>The mod's version (modinfo.json), for the box's line and for remembering "Got it".</summary>
		public static string Version
		{
			get
			{
				// (the mod's own modinfo.json, shipped in the .rmod)
				try
				{
					byte[] b = RaftIslands.ModFile("modinfo.json");
					System.Text.RegularExpressions.Match m = b != null ? System.Text.RegularExpressions.Regex.Match(System.Text.Encoding.UTF8.GetString(b), "\"version\"\\s*:\\s*\"([^\"]+)\"") : null;
					return m != null && m.Success ? m.Groups[1].Value : "?";
				}
				catch { return "?"; }
			}
		}

		static string FilePath { get { return Path.Combine(DynamicIslands.assetpath, FileName); } }

		/// <summary>notice.txt's value for a key (seen = the version "Got it" was pressed for, pos = where the box was dragged).</summary>
		static string Read(string key)
		{
			try
			{
				if (!File.Exists(FilePath)) return null;
				string line = File.ReadAllLines(FilePath).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(key + "="));
				return line != null ? line.Substring(key.Length + 1) : null;
			}
			catch { return null; }
		}

		static void Write(string key, string value)
		{
			try
			{
				Directory.CreateDirectory(DynamicIslands.assetpath);
				var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath).Where(l => l.Trim().Length > 0 && !l.Trim().StartsWith(key + "=")).ToList() : new System.Collections.Generic.List<string>();
				if (value != null) lines.Add(key + "=" + value);
				if (lines.Count == 0) { if (File.Exists(FilePath)) File.Delete(FilePath); }
				else File.WriteAllText(FilePath, string.Join("\n", lines.ToArray()) + "\n");
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write " + FilePath + ": " + e.Message); }
		}

		/// <summary>Whether "Got it" was pressed for this version.</summary>
		public static bool SeenThisVersion { get { return Read("seen") == Version; } }

		static void Remember(bool seen) { Write("seen", seen ? Version : null); }

		/// <summary>The main menu appeared (DynamicIslands.HookUI): the box on its canvas, once.</summary>
		public static void Show(Transform mainMenuCanvas)
		{
			if (mainMenuCanvas == null || mainMenuCanvas.Find(PanelName) != null) return;
			try { Build(mainMenuCanvas); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] The experimental release box: " + e); }
		}

		static void Build(Transform canvas)
		{
			panel = UIKit.Panel(canvas, PanelName, new RectOffset(18, 18, 12, 14), 8f);
			panel.anchorMin = panel.anchorMax = StartAnchor;
			panel.pivot = new Vector2(0.5f, 1f);
			panel.anchoredPosition = Vector2.zero;
			panel.sizeDelta = new Vector2(Width, 0f);
			panel.gameObject.AddComponent<Dragger>();
			UIKit.Hint(panel.gameObject, "Drag the box to move it");

			// (the header alone on its line, as large as fits: Raft's title font is wide)
			Text title = UIKit.Label(panel, Header, 30, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			UIKit.UseTitleFont(title);
			title.horizontalOverflow = HorizontalWrapMode.Wrap;
			title.resizeTextForBestFit = true;
			title.resizeTextMinSize = 12;
			title.resizeTextMaxSize = title.fontSize;
			UIKit.Size(title.gameObject, -1, 36);

			body = UIKit.Rect("Body", panel);
			UIKit.Vertical(body.gameObject, 5f, new RectOffset(0, 0, 0, 0));
			Text intro = UIKit.Label(body, "This is the first release of Custom Islands (version " + Version + "), an early alpha. Please keep in mind:", 16, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Intro");
			intro.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(intro.gameObject, -1, 42); // (two lines)
			for (int i = 0; i < Points.Length; i++)
			{
				Text t = UIKit.Label(body, "•  " + Points[i], 17, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Point" + i);
				t.horizontalOverflow = HorizontalWrapMode.Wrap;
				UIKit.Size(t.gameObject, -1, 24);
			}
			Text advice = UIKit.Label(body, "Back up the worlds you care about (Raft keeps them in AppData\\LocalLow\\Redbeet Interactive\\Raft\\User). " +
				"New here? The guide shows you around. Found a problem, or have a question? Report it, or ask on the Custom Islands Discord.", 13, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Italic, "Advice");
			advice.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(advice.gameObject, -1, 54);

			// (the help buttons stay on the folded bar too)
			RectTransform buttons = UIKit.Row(panel, 32f, 6f, "Buttons");
			discordButton = UIKit.Button(buttons, "Discord", () => HelpLinks.Open(HelpLinks.Discord), "The Custom Islands Discord server: questions, islands, news", 104, 32f, 14);
			discordButton.name = "Discord";
			guideButton = UIKit.Button(buttons, "Guide (PDF)", HelpLinks.OpenGuide, "The illustrated guide: installing, sailing, the editor, world plans, playing together", 124, 32f, 14);
			guideButton.name = "Guide";
			reportButton = UIKit.Button(buttons, "Report a problem", InfoWindow.OpenReport, "How to report a problem, on GitHub or Discord", 164, 32f, 14);
			reportButton.name = "Report";
			UIKit.Label(buttons, "", 12);
			toggle = UIKit.Button(buttons, "", Flip, null, 92, 32f, 14);
			toggle.name = "Toggle";
			UIKit.Primary(toggle);
			SetFolded(SeenThisVersion);
			PlaceSaved();
			Debug.Log("[CUSTOM ISLANDS] Alpha notice shown" + (Folded ? " (folded: seen for " + Version + ")" : ""));
		}

		/// <summary>"Got it" folds the box and remembers it for this version; the folded bar's "Show" opens it again.</summary>
		public static void Flip()
		{
			bool fold = !Folded;
			SetFolded(fold);
			Remember(fold);
		}

		static void SetFolded(bool folded)
		{
			if (body == null || toggle == null) return;
			body.gameObject.SetActive(!folded);
			UIKit.LabelOf(toggle).text = folded ? "Show" : "Got it";
			LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
			KeepOnScreen();
		}

		#region Moving it

		/// <summary>Moves the box (a drag, or tests), kept on the screen, and remembers where.</summary>
		public static void MoveBy(Vector2 delta)
		{
			if (panel == null) return;
			panel.anchoredPosition += delta;
			KeepOnScreen();
			Vector2 p = panel.anchoredPosition;
			Write("pos", p.x.ToString("0", CultureInfo.InvariantCulture) + "," + p.y.ToString("0", CultureInfo.InvariantCulture));
		}

		/// <summary>Back where it starts (forgets the dragged place).</summary>
		public static void ResetPlace()
		{
			if (panel == null) return;
			panel.anchoredPosition = Vector2.zero;
			KeepOnScreen();
			Write("pos", null);
		}

		static void PlaceSaved()
		{
			string pos = Read("pos");
			if (pos == null) return;
			string[] xy = pos.Split(',');
			float x, y;
			if (xy.Length == 2 && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
			{
				panel.anchoredPosition = new Vector2(x, y);
				KeepOnScreen();
			}
		}

		/// <summary>The whole box inside the menu (another screen size, folding, a drag too far).</summary>
		public static void KeepOnScreen()
		{
			var area = panel != null ? panel.parent as RectTransform : null;
			if (area == null) return;
			Canvas.ForceUpdateCanvases();
			Rect a = area.rect, r = panel.rect;
			// (the panel's corners in the parent's space: anchor point + position + its rect)
			Vector2 anchorPoint = new Vector2(a.xMin + a.width * panel.anchorMin.x, a.yMin + a.height * panel.anchorMin.y);
			Vector2 p = panel.anchoredPosition;
			float left = anchorPoint.x + p.x + r.xMin, right = anchorPoint.x + p.x + r.xMax;
			float bottom = anchorPoint.y + p.y + r.yMin, top = anchorPoint.y + p.y + r.yMax;
			// (too far right or up: back in; then too far left or down wins, so the header and buttons are never cut off)
			if (right > a.xMax - 4f) p.x -= right - (a.xMax - 4f);
			if (left + p.x - panel.anchoredPosition.x < a.xMin + 4f) p.x = panel.anchoredPosition.x + a.xMin + 4f - left;
			if (top > a.yMax - 4f) p.y -= top - (a.yMax - 4f);
			if (bottom + p.y - panel.anchoredPosition.y < a.yMin + 4f && top + a.yMin + 4f - bottom <= a.yMax) p.y = panel.anchoredPosition.y + a.yMin + 4f - bottom;
			panel.anchoredPosition = p;
		}

		/// <summary>Drags the box by its surface (the buttons still click).</summary>
		class Dragger : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
		{
			public void OnBeginDrag(PointerEventData e) { }

			public void OnDrag(PointerEventData e)
			{
				Canvas c = GetComponentInParent<Canvas>();
				float scale = c != null && c.rootCanvas != null ? c.rootCanvas.scaleFactor : 1f;
				panel.anchoredPosition += e.delta / Mathf.Max(0.01f, scale);
			}

			public void OnEndDrag(PointerEventData e) { MoveBy(Vector2.zero); }
		}

		#endregion
	}
}
