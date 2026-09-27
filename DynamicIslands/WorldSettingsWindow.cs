using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "World settings..." in Raft's New Game box: a button under the Custom Islands plan opens a window with the world's
	/// extra options (WorldOptions), each switched on and off with its own button and explained under it, and a summary of
	/// the box's other choices of the mod (the plan, the randomizer, the monsters and the build cost). The choice is kept
	/// for the world being made (WorldOptions.Pending) and remembered for the next one when Create is pressed.
	/// </summary>
	[HarmonyPatch(typeof(NewGameBox), "Open")]
	public static class WorldSettingsWindow
	{
		public const string ButtonName = "CustomIslands_WorldSettings", WindowName = "CustomIslands_WorldSettingsWindow";

		static Button openButton;
		static RectTransform window;
		static readonly Dictionary<string, Button> toggles = new Dictionary<string, Button>();
		static Text summary;

		/// <summary>The options shown in the box (the last choice until one is made).</summary>
		public static HashSet<string> Chosen { get { if (WorldOptions.Pending == null) WorldOptions.Pending = WorldOptions.Defaults; return WorldOptions.Pending; } }

		public static bool IsOpen { get { return window != null && window.gameObject.activeSelf; } }
		public static Button OpenButton { get { return openButton; } }
		public static Button Toggle(string option) { Button b; return toggles.TryGetValue(option, out b) ? b : null; }
		public static RectTransform Window { get { return window; } }

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
				openButton = UIKit.Button(holder, "", Open, "More options for this world: scrambled blueprints, story islands in a new order, ghost rafts, private storages", -1, 34f, 14);
			}
			if (window == null) Build(box);
			window.gameObject.SetActive(false);
			Show();
		}

		static void Build(NewGameBox box)
		{
			window = UIKit.Rect(WindowName, box.transform);
			UIKit.Stretch(window);
			window.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f); // (the box behind it dimmed, its clicks taken)
			RectTransform panel = UIKit.Panel(window, "Panel", new RectOffset(16, 16, 12, 14), 6f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 0f));
			RectTransform head = UIKit.Row(panel, 26f, 6f, "Head");
			UIKit.Label(head, "WORLD SETTINGS", 18, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			UIKit.Label(head, "for the world you create", 12, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Italic);
			toggles.Clear();
			for (int i = 0; i < WorldOptions.All.Length; i++)
			{
				string option = WorldOptions.All[i];
				RectTransform group = UIKit.Group(panel, null, "Option_" + option);
				Button b = UIKit.Button(group, "", () => Flip(option), WorldOptions.Hints[i], -1, 30f, 14);
				b.name = "Toggle_" + option;
				toggles[option] = b;
				Text d = UIKit.Label(group, WorldOptions.Hints[i], 11, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Italic, "Detail");
				d.horizontalOverflow = HorizontalWrapMode.Wrap;
				UIKit.Size(d.gameObject, -1, 44);
			}
			summary = UIKit.Label(panel, "", 11, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Normal, "Summary");
			summary.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(summary.gameObject, -1, 46);
			RectTransform buttons = UIKit.Row(panel, 32f, 8f, "Buttons");
			UIKit.Button(buttons, "All off", () => { Chosen.Clear(); Show(); }, "Switch every option off (a world like the others)", 120, 32f, 13);
			UIKit.Label(buttons, "", 12);
			Button done = UIKit.Button(buttons, "Done", Close, "Keep these settings for the world you create", 140, 32f, 14);
			UIKit.Primary(done);
		}

		public static void Open()
		{
			if (window == null) return;
			window.gameObject.SetActive(true);
			window.SetAsLastSibling();
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

		static void Show()
		{
			HashSet<string> on = Chosen;
			foreach (var kv in toggles)
			{
				if (kv.Value == null) continue;
				bool active = on.Contains(kv.Key);
				UIKit.LabelOf(kv.Value).text = WorldOptions.Label(kv.Key) + ":  " + (active ? "ON" : "off");
				UIKit.SetActive(kv.Value, active);
			}
			if (openButton != null) UIKit.LabelOf(openButton).text = "WORLD SETTINGS...   " + (on.Count == 0 ? "none" : on.Count + " on");
			if (summary != null)
				summary.text = "Also in this box: the plan '" + NewWorldOptions.Selected + "', the world randomizer " + NewWorldOptions.Randomizer.Describe() +
					", monsters " + MonsterDifficulty.Describe(NewWorldRulesBox.MonsterLevel) + ", build cost " + BuildCost.Describe(NewWorldRulesBox.BuildPercent) + ". Every player in the world gets these settings from the host.";
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
