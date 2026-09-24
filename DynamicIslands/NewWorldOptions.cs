using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "Custom Islands plan" in Raft's New Game box: a button under the box cycles through the world plans (Random
	/// islands, No custom islands, the saved plans) and shows the chosen plan's description. Pressing Create keeps the
	/// choice for the world being made (WorldDirector.PendingPlan), which gets the plan when it has loaded.
	/// </summary>
	[HarmonyPatch(typeof(NewGameBox), "Open")]
	static class NewWorldOptions
	{
		static RectTransform row;
		static Button planButton;
		static Text detailText;

		/// <summary>The plan shown in the box.</summary>
		public static string Selected { get { return WorldDirector.PendingPlan ?? WorldDirector.DefaultPlan; } }

		static void Postfix(NewGameBox __instance)
		{
			try { Ensure(__instance); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Adding the plan choice to the New Game box: " + e); }
		}

		static void Ensure(NewGameBox box)
		{
			WorldPlanWindow.EnsureSamples();
			if (row == null)
			{
				// In the empty space right of the name and password fields, above the bottom edge
				row = UIKit.Rect("CustomIslands_Plan", box.transform);
				row.anchorMin = row.anchorMax = new Vector2(1f, 0f);
				row.pivot = new Vector2(1f, 0f);
				row.anchoredPosition = new Vector2(-16f, 84f);
				row.sizeDelta = new Vector2(292f, 120f);
				UIKit.Background(row.gameObject, UIKit.GroupBg, 6);
				UIKit.Vertical(row.gameObject, 5f, new RectOffset(10, 10, 8, 8));
				Text title = UIKit.Label(row, "CUSTOM ISLANDS PLAN", 13, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
				UIKit.Size(title.gameObject, -1, 18);
				planButton = UIKit.Button(row, "", Cycle, null, -1, 32f, 15);
				detailText = UIKit.Label(row, "", 12, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Italic, "Detail");
				UIKit.Size(detailText.gameObject, -1, 42);
				detailText.verticalOverflow = VerticalWrapMode.Truncate;
			}
			Show();
		}

		static void Cycle()
		{
			List<string> plans = WorldPlan.All();
			int i = plans.FindIndex(p => p.Equals(Selected, StringComparison.OrdinalIgnoreCase));
			WorldDirector.PendingPlan = plans[(i + 1) % plans.Count];
			Show();
		}

		static void Show()
		{
			if (planButton == null) return;
			WorldPlan p = WorldPlan.Load(Selected);
			if (p == null) { WorldDirector.PendingPlan = WorldPlan.RandomName; p = WorldPlan.Load(WorldPlan.RandomName); }
			UIKit.LabelOf(planButton).text = p.Name + "   \u25BA";
			detailText.text = (p.Description.Length > 0 ? p.Description : p.Rules.Count + " rule(s)") + (p.BuiltIn ? "" : " \u00B7 random islands " + (p.Random ? "too" : "off"));
		}
	}

	/// <summary>Create pressed: the new world gets the plan shown in the box.</summary>
	[HarmonyPatch(typeof(NewGameBox), "Button_CreateNewGame")]
	static class NewWorldPlanChoice
	{
		static void Prefix()
		{
			WorldDirector.PendingPlan = NewWorldOptions.Selected;
			Debug.Log("[CUSTOM ISLANDS] Creating a world with the plan '" + WorldDirector.PendingPlan + "'");
		}
	}
}
