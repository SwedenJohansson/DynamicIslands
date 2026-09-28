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
	/// Above it, "World randomizer": how much the world is randomized (off, light, normal, wild) and which parts
	/// (WorldRandomizer.Pending; the last choice is remembered for the next world).
	/// </summary>
	[HarmonyPatch(typeof(NewGameBox), "Open")]
	static class NewWorldOptions
	{
		static RectTransform row, randRow;
		static Button planButton, levelButton;
		static Text detailText, randText;
		static readonly List<Button> partButtons = new List<Button>();

		/// <summary>The plan shown in the box.</summary>
		public static string Selected { get { return WorldDirector.PendingPlan ?? WorldDirector.DefaultPlan; } }

		/// <summary>The randomizer settings shown in the box.</summary>
		public static RandomizerSettings Randomizer { get { if (WorldRandomizer.Pending == null) WorldRandomizer.Pending = WorldRandomizer.Defaults; return WorldRandomizer.Pending; } }

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

		/// <summary>
		/// "World randomizer", in the World settings window (WorldSettingsWindow): how much is randomized (off, light,
		/// normal, wild) and which parts, with a line on what that means. (It sat in the New Game box before; the box keeps
		/// only the plan now, like Raft's own.)
		/// </summary>
		internal static RectTransform BuildRandomizer(Transform parent)
		{
			randRow = UIKit.Group(parent, "World randomizer", "CustomIslands_Randomizer");
			UIKit.Hint(randRow.gameObject, "Makes a normal Raft world different every time: animal colours, alphas, more animals, moved and extra loot, finds, oddity islands and boss lairs. Raft's story is never changed.");
			levelButton = UIKit.Button(randRow, "", CycleLevel, "How much is randomized: off, light, normal or wild", -1, 28f, 14);
			partButtons.Clear();
			for (int r = 0; r < 2; r++)
			{
				RectTransform line = UIKit.Row(randRow, 24f, 4f, "Parts" + r);
				for (int i = r * 4; i < Math.Min(RandomizerSettings.Features.Length, r * 4 + 4); i++)
				{
					int index = i;
					Button b = UIKit.Button(line, RandomizerSettings.FeatureLabels[i], () => TogglePart(index), RandomizerSettings.FeatureHints[i], -1, 24f, 11);
					partButtons.Add(b);
				}
			}
			randText = UIKit.Label(randRow, "", 11, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Italic, "Detail");
			UIKit.Size(randText.gameObject, -1, 28);
			randText.horizontalOverflow = HorizontalWrapMode.Wrap;
			randText.verticalOverflow = VerticalWrapMode.Truncate;
			Show();
			return randRow;
		}

		/// <summary>The randomizer's controls (tests click them as a player would): the level button and the parts.</summary>
		public static Button LevelButton { get { return levelButton; } }
		public static IList<Button> PartButtons { get { return partButtons; } }
		/// <summary>Anything the mod shows in the box changed a setting (the World settings window's summary follows).</summary>
		internal static event Action Shown;
		/// <summary>The box and the window show the current choice again.</summary>
		internal static void Refresh() { Show(); }

		/// <summary>
		/// Raft shows "name can't be empty" and the like right of the name field, where the randomizer now is: it goes on
		/// the line of the "World name" heading instead, right-aligned above the field.
		/// </summary>
		static void MoveNameFeedback(NewGameBox box)
		{
			Text feedback = Traverse.Create(box).Field("text_nameFeedback").GetValue<Text>();
			InputField name = Traverse.Create(box).Field("inputfield_GameName").GetValue<InputField>();
			if (feedback == null || name == null) return;
			RectTransform f = feedback.rectTransform, n = (RectTransform)name.transform;
			var corners = new Vector3[4];
			n.GetWorldCorners(corners);
			f.SetParent(n.parent, true);
			f.pivot = new Vector2(1f, 0f);
			f.anchorMin = f.anchorMax = n.anchorMin;
			f.position = corners[2] + n.up * 4f * n.lossyScale.y;
			f.sizeDelta = new Vector2(n.rect.width * 0.62f, 22f);
			feedback.alignment = TextAnchor.LowerRight;
			feedback.horizontalOverflow = HorizontalWrapMode.Wrap;
			feedback.resizeTextMaxSize = feedback.fontSize;
			feedback.resizeTextMinSize = 9;
			feedback.resizeTextForBestFit = true;
			f.SetAsLastSibling();
		}

		static void Cycle()
		{
			List<string> plans = WorldPlan.All();
			int i = plans.FindIndex(p => p.Equals(Selected, StringComparison.OrdinalIgnoreCase));
			WorldDirector.PendingPlan = plans[(i + 1) % plans.Count];
			Show();
		}

		static void CycleLevel()
		{
			RandomizerSettings s = Randomizer;
			s.Level = (s.Level + 1) % RandomizerSettings.LevelNames.Length;
			Show();
		}

		static void TogglePart(int i)
		{
			RandomizerSettings s = Randomizer;
			string f = RandomizerSettings.Features[i];
			if (!s.Disabled.Remove(f)) s.Disabled.Add(f);
			Show();
		}

		static void Show()
		{
			if (planButton != null)
			{
				WorldPlan p = WorldPlan.Load(Selected);
				if (p == null) { WorldDirector.PendingPlan = WorldPlan.RandomName; p = WorldPlan.Load(WorldPlan.RandomName); }
				UIKit.LabelOf(planButton).text = p.Name + "   ►";
				detailText.text = (p.Description.Length > 0 ? p.Description : p.Rules.Count + " rule(s)") + (p.BuiltIn ? "" : " · random islands " + (p.Random ? "too" : "off"));
			}
			if (levelButton != null)
			{
				RandomizerSettings s = Randomizer;
				UIKit.LabelOf(levelButton).text = s.LevelName + "   ►";
				for (int i = 0; i < partButtons.Count; i++)
				{
					bool on = s.On && !s.Disabled.Contains(RandomizerSettings.Features[i]);
					UIKit.SetActive(partButtons[i], on);
					partButtons[i].interactable = s.On;
				}
				randText.text = !s.On ? "A normal Raft world. Pick Light, Normal or Wild to make this one different." :
					s.Level == RandomizerSettings.Light ? "Now and then something is different." :
					s.Level == RandomizerSettings.Normal ? "A good share of the world is different: colours, animals, loot and odd islands." :
					"Lots of surprises: many colours, alphas, loot and odd islands.";
			}
			if (Shown != null) try { Shown(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] " + e.Message); }
		}
	}

	/// <summary>Create pressed: the new world gets the plan and the randomizer settings shown in the box.</summary>
	[HarmonyPatch(typeof(NewGameBox), "Button_CreateNewGame")]
	static class NewWorldPlanChoice
	{
		static void Prefix()
		{
			WorldDirector.PendingPlan = NewWorldOptions.Selected;
			RandomizerSettings r = NewWorldOptions.Randomizer;
			WorldRandomizer.SaveDefaults(r);
			Debug.Log("[CUSTOM ISLANDS] Creating a world with the plan '" + WorldDirector.PendingPlan + "', world randomizer " + r.Describe());
		}
	}
}
