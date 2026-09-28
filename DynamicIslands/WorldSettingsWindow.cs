using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The mod's settings for a new world, in one place. Raft's New Game box stays as Raft made it, with the Custom Islands
	/// plan and, under it, "WORLD SETTINGS..." (saying how many settings differ from Raft's own). That opens a window over
	/// the menu with everything else, grouped:
	///   - World rules: the monster difficulty and the build cost (NewWorldRulesBox);
	///   - World randomizer: how much, and which parts (NewWorldOptions.BuildRandomizer);
	///   - Extra options: scrambled blueprints, story islands in a new order, ghost rafts, private storages (WorldOptions),
	///     each switched with its own button and explained under it.
	/// "Raft's own" puts every one of them back to plain Raft; "Done" closes it. The choices are kept for the world being
	/// made and remembered for the next one when Create is pressed.
	/// </summary>
	[HarmonyPatch(typeof(NewGameBox), "Open")]
	public static class WorldSettingsWindow
	{
		public const string ButtonName = "CustomIslands_WorldSettings", WindowName = "CustomIslands_WorldSettingsWindow";

		static Button openButton;
		static RectTransform window;
		static readonly Dictionary<string, Button> toggles = new Dictionary<string, Button>();
		static Text summary;
		static bool hooked;

		/// <summary>The options shown (the last choice until one is made).</summary>
		public static HashSet<string> Chosen { get { if (WorldOptions.Pending == null) WorldOptions.Pending = WorldOptions.Defaults; return WorldOptions.Pending; } }

		public static bool IsOpen { get { return window != null && window.gameObject.activeSelf; } }
		public static Button OpenButton { get { return openButton; } }
		public static Button Toggle(string option) { Button b; return toggles.TryGetValue(option, out b) ? b : null; }
		public static RectTransform Window { get { return window; } }

		/// <summary>How many of the settings differ from plain Raft (the box's button says so).</summary>
		public static int Changed
		{
			get
			{
				return Chosen.Count + (NewWorldOptions.Randomizer.On ? 1 : 0) + (NewWorldRulesBox.MonsterLevel != MonsterDifficulty.Normal ? 1 : 0) + (NewWorldRulesBox.BuildPercent > 0 ? 1 : 0);
			}
		}

		static void Postfix(NewGameBox __instance)
		{
			try { Ensure(__instance); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Adding World settings to the New Game box: " + e); }
		}

		static void Ensure(NewGameBox box)
		{
			if (openButton == null)
			{
				// Under the Custom Islands plan, right of Raft's Create button
				RectTransform holder = UIKit.Rect(ButtonName, box.transform);
				holder.anchorMin = holder.anchorMax = new Vector2(1f, 0f);
				holder.pivot = new Vector2(1f, 0f);
				holder.anchoredPosition = new Vector2(-16f, 44f);
				holder.sizeDelta = new Vector2(292f, 34f);
				UIKit.Horizontal(holder.gameObject, 0f, new RectOffset(0, 0, 0, 0));
				openButton = UIKit.Button(holder, "", Open, "The world's settings: monster difficulty, build cost, the world randomizer and the extra options", -1, 34f, 14);
				UIKit.Primary(openButton);
			}
			if (window == null) Build(box);
			window.gameObject.SetActive(false);
			if (!hooked) { NewWorldOptions.Shown += Show; hooked = true; }
			Show();
		}

		static void Build(NewGameBox box)
		{
			// Over the whole menu (not inside the box: it has room for Raft's own things only)
			Canvas canvas = box.GetComponentInParent<Canvas>();
			Transform screen = canvas != null ? canvas.rootCanvas.transform : box.transform;
			window = UIKit.Rect(WindowName, screen);
			UIKit.Stretch(window);
			window.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f); // (the menu behind it dimmed, its clicks taken)
			RectTransform panel = UIKit.Panel(window, "Panel", new RectOffset(18, 18, 12, 14), 8f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(930f, 0f));
			RectTransform head = UIKit.Row(panel, 30f, 6f, "Head");
			Text title = UIKit.Label(head, "WORLD SETTINGS", 22, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			UIKit.UseTitleFont(title);
			UIKit.Label(head, "for the world you create - every player in it gets them from the host", 12, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Italic);

			// (as tall as its taller column: no empty band under them)
			RectTransform columns = UIKit.Rect("Columns", panel);
			UIKit.Horizontal(columns.gameObject, 16f).childForceExpandWidth = false;
			columns.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
			// Left: the world's rules and the randomizer
			RectTransform left = UIKit.Rect("Left", columns);
			UIKit.Vertical(left.gameObject, 8f, new RectOffset(0, 0, 0, 0));
			UIKit.Size(left.gameObject, 430);
			Text rulesTitle = UIKit.Label(left, "WORLD RULES", 14, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "RulesTitle");
			UIKit.Size(rulesTitle.gameObject, -1, 18);
			NewWorldRulesBox.BuildInto(left);
			NewWorldOptions.BuildRandomizer(left);
			// Right: the extra options
			RectTransform right = UIKit.Rect("Right", columns);
			UIKit.Vertical(right.gameObject, 6f, new RectOffset(0, 0, 0, 0));
			UIKit.Size(right.gameObject, 450);
			Text optionsTitle = UIKit.Label(right, "EXTRA OPTIONS", 14, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "OptionsTitle");
			UIKit.Size(optionsTitle.gameObject, -1, 18);
			toggles.Clear();
			for (int i = 0; i < WorldOptions.All.Length; i++)
			{
				string option = WorldOptions.All[i];
				RectTransform group = UIKit.Group(right, null, "Option_" + option);
				Button b = UIKit.Button(group, "", () => Flip(option), WorldOptions.Hints[i], -1, 28f, 14);
				b.name = "Toggle_" + option;
				toggles[option] = b;
				Text d = UIKit.Label(group, WorldOptions.Hints[i], 11, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Italic, "Detail");
				d.horizontalOverflow = HorizontalWrapMode.Wrap;
				UIKit.Size(d.gameObject, -1, 44);
			}

			summary = UIKit.Label(panel, "", 12, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, "Summary");
			summary.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(summary.gameObject, -1, 30);
			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			UIKit.Button(buttons, "Raft's own", RaftsOwn, "Every setting back to plain Raft: Normal monsters, Raft's build cost, no randomizer, no extra options", 150, 34f, 14);
			UIKit.Label(buttons, "", 12);
			Button done = UIKit.Button(buttons, "Done", Close, "Keep these settings for the world you create", 150, 34f, 15);
			UIKit.Primary(done);
			window.gameObject.SetActive(false);
			Debug.Log("[CUSTOM ISLANDS] New Game box: World settings (world rules, randomizer, extra options) in their own window");
		}

		public static void Open()
		{
			if (window == null) return;
			window.gameObject.SetActive(true);
			window.SetAsLastSibling();
			NewWorldRulesBox.Refresh();
			Show();
		}

		public static void Close()
		{
			if (window != null) window.gameObject.SetActive(false);
			Show();
		}

		public static void Flip(string option)
		{
			if (!Chosen.Remove(option)) Chosen.Add(option);
			Show();
		}

		/// <summary>Everything back to plain Raft: no extra options, the randomizer off, Normal monsters, Raft's build cost.</summary>
		public static void RaftsOwn()
		{
			Chosen.Clear();
			NewWorldOptions.Randomizer.Level = RandomizerSettings.Off;
			NewWorldOptions.Randomizer.Disabled.Clear();
			NewWorldRulesBox.MonsterLevel = MonsterDifficulty.Normal;
			NewWorldRulesBox.BuildPercent = 0;
			NewWorldOptions.Refresh();
			Show();
		}

		internal static void Show()
		{
			HashSet<string> on = Chosen;
			foreach (var kv in toggles)
			{
				if (kv.Value == null) continue;
				bool active = on.Contains(kv.Key);
				UIKit.LabelOf(kv.Value).text = WorldOptions.Label(kv.Key) + ":  " + (active ? "ON" : "off");
				UIKit.SetActive(kv.Value, active);
			}
			int changed = Changed;
			if (openButton != null) UIKit.LabelOf(openButton).text = "WORLD SETTINGS...   " + (changed == 0 ? "Raft's own" : changed + " changed");
			if (summary != null)
				summary.text = "Now: monsters " + MonsterDifficulty.Describe(NewWorldRulesBox.MonsterLevel) + ", build cost " + BuildCost.Describe(NewWorldRulesBox.BuildPercent) +
					", the world randomizer " + NewWorldOptions.Randomizer.Describe() + ", extra options: " + WorldOptions.Describe(on).ToLowerInvariant() +
					". The Custom Islands plan ('" + NewWorldOptions.Selected + "') is chosen in the New Game box.";
		}
	}

	/// <summary>Create pressed: the world gets the options shown, and they are remembered for the next one.</summary>
	[HarmonyPatch(typeof(NewGameBox), "Button_CreateNewGame")]
	static class WorldSettingsChoice
	{
		static void Prefix()
		{
			HashSet<string> on = WorldSettingsWindow.Chosen;
			WorldOptions.SaveDefaults(on);
			Debug.Log("[CUSTOM ISLANDS] Creating a world with the options " + WorldOptions.Describe(on));
		}
	}
}
