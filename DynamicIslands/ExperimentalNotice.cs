using System;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The main menu's "EXPERIMENTAL RELEASE" box: this is the mod's first release, so new players are told that things are
	/// likely to change, some systems might be unstable and progress isn't guaranteed to be saved. It sits at the top right
	/// of Raft's main menu, clear of the menu buttons and the New Game box. "Got it" folds it into a slim bar (click the bar
	/// to open it again); that is remembered for this version of the mod only (Mods\DynamicIslands\notice.txt), so a new
	/// version shows the whole box again.
	/// </summary>
	public static class ExperimentalNotice
	{
		public const string PanelName = "CustomIslands_ExperimentalNotice";
		public const string Header = "EXPERIMENTAL RELEASE";
		public static readonly string[] Points =
		{
			"Things are likely to change.",
			"Some systems might be unstable.",
			"We do not guarantee that progress is always saved.",
		};
		const string FileName = "notice.txt";

		static RectTransform panel, body;
		static Button toggle;

		public static RectTransform Panel { get { return panel; } }
		public static bool Folded { get { return body != null && !body.gameObject.activeSelf; } }
		public static Button Toggle { get { return toggle; } }

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

		/// <summary>Whether "Got it" was pressed for this version.</summary>
		public static bool SeenThisVersion
		{
			get
			{
				try { return File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "seen=" + Version; }
				catch { return false; }
			}
		}

		static void Remember(bool seen)
		{
			try
			{
				Directory.CreateDirectory(DynamicIslands.assetpath);
				if (seen) File.WriteAllText(FilePath, "seen=" + Version + "\n");
				else if (File.Exists(FilePath)) File.Delete(FilePath);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write " + FilePath + ": " + e.Message); }
		}

		/// <summary>The main menu appeared (DynamicIslands.HookUI): the box on its canvas, once.</summary>
		public static void Show(Transform mainMenuCanvas)
		{
			if (mainMenuCanvas == null || mainMenuCanvas.Find(PanelName) != null) return;
			try { Build(mainMenuCanvas); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] The experimental release box: " + e); }
		}

		static void Build(Transform canvas)
		{
			panel = UIKit.Panel(canvas, PanelName, new RectOffset(14, 14, 10, 12), 6f);
			// (top right, under Raft's "built by" line)
			panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
			panel.pivot = new Vector2(1f, 1f);
			panel.anchoredPosition = new Vector2(-24f, -70f);
			panel.sizeDelta = new Vector2(380f, 0f);

			// (the header alone on its line, as large as fits: Raft's title font is wide)
			Text title = UIKit.Label(panel, Header, 24, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			UIKit.UseTitleFont(title);
			title.horizontalOverflow = HorizontalWrapMode.Wrap;
			title.resizeTextForBestFit = true;
			title.resizeTextMinSize = 12;
			title.resizeTextMaxSize = title.fontSize;
			UIKit.Size(title.gameObject, -1, 30);

			body = UIKit.Rect("Body", panel);
			UIKit.Vertical(body.gameObject, 4f, new RectOffset(0, 0, 0, 0));
			Text intro = UIKit.Label(body, "This is the first release of Custom Islands (version " + Version + "). Please keep in mind:", 13, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Intro");
			intro.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(intro.gameObject, -1, 36); // (two lines)
			for (int i = 0; i < Points.Length; i++)
			{
				Text t = UIKit.Label(body, "•  " + Points[i], 14, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Point" + i);
				t.horizontalOverflow = HorizontalWrapMode.Wrap;
				UIKit.Size(t.gameObject, -1, 20);
			}
			Text advice = UIKit.Label(body, "Back up the worlds you care about (Raft keeps them in AppData\\LocalLow\\Redbeet Interactive\\Raft\\User). Found a problem? Tell us - with what you did and the log.", 11, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Italic, "Advice");
			advice.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(advice.gameObject, -1, 44);

			RectTransform buttons = UIKit.Row(panel, 28f, 6f, "Buttons");
			UIKit.Label(buttons, "", 12);
			toggle = UIKit.Button(buttons, "", Flip, null, 120, 28f, 13);
			toggle.name = "Toggle";
			SetFolded(SeenThisVersion);
			Debug.Log("[CUSTOM ISLANDS] Experimental release box shown" + (Folded ? " (folded: seen for " + Version + ")" : ""));
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
		}
	}
}
