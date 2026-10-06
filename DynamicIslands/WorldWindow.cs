using HMLLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The world's Custom Islands settings in a running world - a "CUSTOM ISLANDS" button in Raft's pause menu (Esc). The
	/// same groups as World settings in the New Game box: the world rules (monsters, build cost), the world randomizer,
	/// the extra options and the level up system, which islands turn up while sailing, and the world's plan and story. The
	/// host changes them here for every player (the same as the F10 commands Monsters, BuildCost, Randomizer, WorldOptions,
	/// Levels, WorldIslands); other players see the host's settings, read only. Before, a running world's settings could
	/// only be changed with F10 commands.
	/// </summary>
	public class WorldWindow : MonoBehaviour
	{
		public const string CanvasName = "CustomIslands_WorldWindow", ButtonName = "CustomIslands_PauseButton";
		static Canvas canvas;
		static WorldWindow instance;
		static Text hostText, planText, islandsText, hereText;
		static RectTransform islandList;
		static readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
		static float nextRefresh;

		public static bool IsOpen { get { return canvas != null && canvas.gameObject.activeSelf; } }
		/// <summary>Tests: a button of the window by its name ("Monsters_Savage", "Option_ghostrafts", "Levels"...).</summary>
		public static Button ButtonNamed(string name) { Button b; return buttons.TryGetValue(name, out b) ? b : null; }
		static bool Host { get { return Raft_Network.IsHost; } }

		static void Build()
		{
			canvas = UIKit.CreateCanvas(CanvasName, 870); // (over Raft's pause menu, under the info boxes and ? popups)
			DontDestroyOnLoad(canvas.gameObject);
			instance = canvas.gameObject.AddComponent<WorldWindow>();
			RectTransform root = (RectTransform)canvas.transform;
			RectTransform dim = UIKit.Rect("Dim", root);
			UIKit.Stretch(dim);
			dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
			RectTransform panel = UIKit.Panel(root, "Panel", new RectOffset(18, 18, 12, 14), 8f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 0f));
			RectTransform head = UIKit.Row(panel, 30f, 6f, "Head");
			Text title = UIKit.Label(head, "CUSTOM ISLANDS - THIS WORLD", 22, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			UIKit.UseTitleFont(title);
			hostText = UIKit.Label(head, "", 12, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Italic, "Who");

			RectTransform columns = UIKit.Rect("Columns", panel);
			UIKit.Horizontal(columns.gameObject, 16f).childForceExpandWidth = false;
			columns.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
			RectTransform left = UIKit.Rect("Left", columns);
			UIKit.Vertical(left.gameObject, 6f, new RectOffset(0, 0, 0, 0));
			UIKit.Size(left.gameObject, 450);
			RectTransform right = UIKit.Rect("Right", columns);
			UIKit.Vertical(right.gameObject, 6f, new RectOffset(0, 0, 0, 0));
			UIKit.Size(right.gameObject, 460);

			// World rules
			Heading(left, "WORLD RULES");
			RectTransform monsters = UIKit.Group(left, "Monster difficulty", "Monsters");
			RectTransform mrow = UIKit.Row(monsters, 28f, 4f, "Levels");
			for (int i = 0; i < MonsterDifficulty.Names.Length; i++)
			{
				int level = i;
				Add("Monsters_" + MonsterDifficulty.Names[i], UIKit.Button(mrow, MonsterDifficulty.Names[i], () => { MonsterDifficulty.Set(level); Refresh(); }, "Monsters' health and the damage they deal: " + MonsterDifficulty.Describe(level), -1, 28f, 12));
			}
			RectTransform cost = UIKit.Group(left, "Build cost", "BuildCost");
			RectTransform crow = UIKit.Row(cost, 28f, 4f, "Cost");
			Add("BuildCost_Less", UIKit.Button(crow, "- 5 %", () => { BuildCost.Set(Mathf.Max(0, BuildCost.Current - BuildCost.Step)); Refresh(); }, "The build menu costs 5 % less. Blocks built before keep giving back by the cost they were built at", 70, 28f, 12));
			Add("BuildCost_Value", UIKit.Button(crow, "", () => { }, "How many more materials the build menu costs than in Raft", -1, 28f, 13));
			Add("BuildCost_More", UIKit.Button(crow, "+ 5 %", () => { BuildCost.Set(Mathf.Min(BuildCost.Max, BuildCost.Current + BuildCost.Step)); Refresh(); }, "The build menu costs 5 % more. Blocks built before keep giving back by the cost they were built at", 70, 28f, 12));

			// World randomizer
			RectTransform rnd = UIKit.Group(left, "World randomizer (islands already looked at keep what they got)", "Randomizer");
			RectTransform rrow = UIKit.Row(rnd, 28f, 4f, "Level");
			for (int i = 0; i < RandomizerSettings.LevelNames.Length; i++)
			{
				int level = i;
				Add("Randomizer_" + RandomizerSettings.LevelNames[i], UIKit.Button(rrow, RandomizerSettings.LevelNames[i], () => SetRandomizer(s => s.Level = level), "How much of a normal Raft world is made different", -1, 28f, 12));
			}
			for (int row = 0; row < 2; row++)
			{
				RectTransform prow = UIKit.Row(rnd, 26f, 4f, "Parts" + row);
				for (int i = row * 4; i < Math.Min(RandomizerSettings.Features.Length, row * 4 + 4); i++)
				{
					string part = RandomizerSettings.Features[i];
					Add("Part_" + part, UIKit.Button(prow, RandomizerSettings.FeatureLabels[i], () => SetRandomizer(s => { if (!s.Disabled.Remove(part)) s.Disabled.Add(part); }), RandomizerSettings.FeatureHints[i], -1, 26f, 11));
				}
			}

			// Islands while sailing
			Heading(left, "ISLANDS WHILE SAILING");
			islandsText = UIKit.Label(left, "", 11, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Italic, "IslandsNote");
			islandsText.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(islandsText.gameObject, -1, 30);
			RectTransform listBox = UIKit.Rect("IslandList", left);
			UIKit.Size(listBox.gameObject, -1, 150);
			UIKit.Background(listBox.gameObject, new Color(0.231f, 0.129f, 0.059f, 0.3f), 6);
			ScrollRect scroll;
			islandList = UIKit.ScrollList(listBox, out scroll, 2f);
			UIKit.Stretch((RectTransform)scroll.transform, 6, 4, 4, 4);

			// Extra options and the level up system
			Heading(right, "EXTRA OPTIONS");
			for (int i = 0; i < WorldOptions.All.Length; i++)
			{
				string option = WorldOptions.All[i];
				Add("Option_" + option, UIKit.Button(right, "", () => { var on = new HashSet<string>(WorldOptions.Current); if (!on.Remove(option)) on.Add(option); WorldOptions.Set(on); Refresh(); },
					WorldOptions.Hints[i] + (option == WorldOptions.StoryOrder ? " Switched in a running world the Receiver's list is rebuilt: the island you were sailing to may move - best chosen when the world is made." : ""), -1, 28f, 13));
			}
			Add("Levels", UIKit.Button(right, "", () => { PlayerLevels.SetEnabled(!PlayerLevels.On); Refresh(); }, "Players earn EXP from monsters and spend stat points (K). Off keeps everyone's levels for when it is on again", -1, 28f, 13));

			// Plan and story
			Heading(right, "PLAN AND STORY");
			// (the host picks another plan here - it was only the F10 command WorldPlan <name>)
			RectTransform planRow = UIKit.Row(right, 28f, 6f, "PlanRow");
			UIKit.Size(UIKit.Label(planRow, "Plan", 13, UIKit.TextMuted).gameObject, 40);
			Add("WorldPlan", DropList.Make(planRow, "Drop_WorldPlan", NewWorldOptions.PlanOptions(), WorldDirector.PlanName, v => { DynamicIslands.WorldPlanCommand(new[] { v }); Refresh(); }, -1,
				"Give this world another plan (host): its islands come from now on; what is done or unlocked stays", 28f, 13));
			planText = UIKit.Label(right, "", 12, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Plan");
			planText.horizontalOverflow = HorizontalWrapMode.Wrap;
			planText.verticalOverflow = VerticalWrapMode.Truncate;
			UIKit.Size(planText.gameObject, -1, 170);

			// The islands in this world, nearest first (ROADMAP T1b)
			Heading(right, "ISLANDS IN THIS WORLD");
			hereText = UIKit.Label(right, "", 12, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Here");
			hereText.horizontalOverflow = HorizontalWrapMode.Wrap;
			hereText.verticalOverflow = VerticalWrapMode.Truncate;
			UIKit.Size(hereText.gameObject, -1, 130);

			RectTransform buttonsRow = UIKit.Row(panel, 34f, 8f, "Buttons");
			UIKit.Label(buttonsRow, "Changes are for every player and saved with the world. The same with F10: Monsters, BuildCost, Randomizer, WorldOptions, Levels, WorldIslands, WorldPlan.", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic);
			Add("BackToEditor", UIKit.Button(buttonsRow, "Back to the editor", IslandTest.Back, "Leave the test world without saving it and open the island in the editor again", 180, 34f, 14));
			Button close = UIKit.Button(buttonsRow, "Close", Close, "Back to the game menu (Esc)", 140, 34f, 15);
			UIKit.Primary(close);
			canvas.gameObject.SetActive(false);
		}

		static void Heading(Transform parent, string text)
		{
			Text t = UIKit.Label(parent, text, 14, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Heading");
			UIKit.Size(t.gameObject, -1, 18);
		}

		static void Add(string name, Button b) { b.name = name; buttons[name] = b; }

		static void SetRandomizer(Action<RandomizerSettings> change)
		{
			RandomizerSettings s = WorldRandomizer.Current.Copy();
			change(s);
			WorldRandomizer.Set(s);
			Refresh();
		}

		public static void Open()
		{
			if (!LoadSceneManager.IsGameSceneLoaded) return;
			if (canvas == null) Build();
			canvas.gameObject.SetActive(true);
			FillIslands();
			Refresh();
		}

		public static void Close()
		{
			if (canvas != null) canvas.gameObject.SetActive(false);
		}

		void Update()
		{
			if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
			if (!LoadSceneManager.IsGameSceneLoaded) { Close(); return; }
			// (a client sees the host's changes as they come)
			if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 1f; Refresh(); }
		}

		/// <summary>The island list: every entry of the spawn pool with a tick box (host); a player sees the host's choice isn't sent to them.</summary>
		static void FillIslands()
		{
			foreach (Transform c in islandList) Destroy(c.gameObject);
			foreach (string key in buttons.Keys.Where(k => k.StartsWith("Island_")).ToList()) buttons.Remove(key);
			if (!Host) return;
			foreach (string entry in WorldIslands.Candidates())
			{
				string e = entry;
				Button b = UIKit.Button(islandList, "", () => { WorldIslands.Set(e, !WorldIslands.TakesPart(e)); Refresh(); }, WorldIslands.Detail(e), -1, 24f, 12);
				Add("Island_" + e, b);
			}
		}

		public static void Refresh()
		{
			if (canvas == null || !canvas.gameObject.activeSelf) return;
			bool host = Host;
			hostText.text = host ? "you are the host: changes are for every player" : "the host's settings - only the host changes them";
			foreach (var kv in buttons) kv.Value.interactable = host && kv.Key != "BuildCost_Value";
			buttons["BackToEditor"].gameObject.SetActive(IslandTest.Testing);
			buttons["BackToEditor"].interactable = true;
			for (int i = 0; i < MonsterDifficulty.Names.Length; i++) UIKit.SetActive(buttons["Monsters_" + MonsterDifficulty.Names[i]], MonsterDifficulty.Current == i);
			UIKit.LabelOf(buttons["BuildCost_Value"]).text = BuildCost.Describe(BuildCost.Current);
			RandomizerSettings r = WorldRandomizer.Current;
			for (int i = 0; i < RandomizerSettings.LevelNames.Length; i++) UIKit.SetActive(buttons["Randomizer_" + RandomizerSettings.LevelNames[i]], r.Level == i);
			foreach (string part in RandomizerSettings.Features)
			{
				Button b = buttons["Part_" + part];
				UIKit.SetActive(b, r.Level > RandomizerSettings.Off && !r.Disabled.Contains(part));
				// (with the randomizer off its parts do nothing: they toggled with nothing to see)
				b.interactable = host && r.Level > RandomizerSettings.Off;
			}
			foreach (string o in WorldOptions.All)
			{
				Button b = buttons["Option_" + o];
				UIKit.LabelOf(b).text = WorldOptions.Label(o) + ":  " + (WorldOptions.On(o) ? "ON" : "off");
				UIKit.SetActive(b, WorldOptions.On(o));
			}
			UIKit.LabelOf(buttons["Levels"]).text = "Level up system:  " + (PlayerLevels.On ? "ON" : "off");
			Button planPick = buttons["WorldPlan"];
			DropdownButton pd = planPick.GetComponent<DropdownButton>();
			if (pd != null) { pd.Options = NewWorldOptions.PlanOptions(); pd.Value = WorldDirector.PlanName; }
			UIKit.LabelOf(planPick).text = WorldDirector.PlanName;
			UIKit.SetActive(buttons["Levels"], PlayerLevels.On);

			if (host)
			{
				List<string> all = WorldIslands.Candidates();
				int part = all.Count(WorldIslands.TakesPart);
				islandsText.text = (CustomIslandSpawner.Enabled ? "" : "Random islands are off in this world (its plan, or CustomIslandsAuto off) - the list counts when they are on. ") +
					part + " of " + all.Count + " take part: untick what this world shouldn't meet by chance (islands a plan or quest brings still come).";
				foreach (string e in all)
				{
					Button b;
					if (!buttons.TryGetValue("Island_" + e, out b)) continue;
					bool on = WorldIslands.TakesPart(e);
					UIKit.LabelOf(b).text = (on ? "☑  " : "☐  ") + WorldIslands.Label(e);
					UIKit.SetActive(b, on);
				}
			}
			else islandsText.text = WorldIslands.DescribeForPlayer();

			planText.text = host ? PlanSummary() : WorldDirector.DescribeForPlayer(false) + (StoryChain.Active ? "\n" + StorySummary() : "");
			hereText.text = IslandsHere();
		}

		/// <summary>The plan and its rules, and the story chain if the plan changed Raft's story (host).</summary>
		static string PlanSummary()
		{
			WorldPlan p = WorldDirector.Plan;
			var lines = new List<string> { "<b>Plan:</b> " + WorldDirector.PlanName + (p == null ? " (missing)" : "") + " - random islands while sailing " + (CustomIslandSpawner.Enabled ? "on" : "off") };
			if (p != null && p.Rules.Count > 0)
			{
				int done = p.Rules.Count(x => WorldDirector.Done.Contains(x.Id) || StoryChain.Brought.Contains(x.Id));
				lines.Add(done + " of " + p.Rules.Count + " island(s) of the plan are here; next: " +
					string.Join(", ", p.Rules.Where(x => !WorldDirector.Done.Contains(x.Id) && !StoryChain.Brought.Contains(x.Id)).Take(3).Select(x => (x.Label.Length > 0 ? x.Label : x.Id)).ToArray()));
			}
			if (StoryChain.Active) lines.Add(StorySummary());
			lines.Add("<i>Another plan: pick it in the list above - its islands come from now on, what is done or unlocked stays.</i>");
			return string.Join("\n", lines.ToArray());
		}

		/// <summary>The custom islands of this world, nearest first: their name, how far and which way, their quest's state.</summary>
		internal static string IslandsHere()
		{
			Vector3 from = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			Network_Player p = RAPI.GetLocalPlayer();
			if (p != null) from = p.transform.position;
			var list = IslandWorldState.Islands.Where(e => !e.Failed && !WorldRandomizer.IsExtras(e)).Select(e => new { E = e, D = new Vector2(e.Position.x - from.x, e.Position.z - from.z) })
				.OrderBy(x => x.D.magnitude).ToList();
			if (list.Count == 0) return "None yet.";
			var lines = new List<string>();
			foreach (var x in list.Take(8))
			{
				IslandQuest q = QuestTracker.QuestOf(x.E);
				int step = QuestTracker.StepOf(x.E);
				string state = !q.Exists ? "" : step >= q.Steps.Count ? "  <color=#8fdc8f>quest done</color>" : step > 0 ? "  quest " + step + "/" + q.Steps.Count : "  quest not begun";
				float angle = Mathf.Atan2(x.D.x, x.D.y) * Mathf.Rad2Deg;
				lines.Add(Behaviours.IslandTitle(x.E) + "  -  " + (Mathf.Round(x.D.magnitude / 10f) * 10f).ToString("F0") + " m " + IntroRule.DirectionName(angle) + state);
			}
			if (list.Count > 8) lines.Add("... and " + (list.Count - 8) + " more further away");
			return string.Join("\n", lines.ToArray());
		}

		static string StorySummary()
		{
			return "<b>Story:</b> " + string.Join(" > ", StoryChain.Steps.Select(s => StoryChain.StepName(s) + (StoryChain.Done.Contains(s) ? " ✓" : StoryChain.Unlocked.Contains(s) ? " (now)" : "")).ToArray());
		}
	}

	/// <summary>Raft's pause menu (Esc in a world) gets a CUSTOM ISLANDS button, a copy of its own buttons, before Exit.</summary>
	[HarmonyPatch(typeof(PauseMenu), "Start")]
	static class WorldWindowButton
	{
		static void Postfix(PauseMenu __instance)
		{
			try
			{
				GameObject holder = __instance.buttonHolderPanel;
				if (holder == null || holder.transform.Find(WorldWindow.ButtonName) != null) return;
				Button[] own = holder.GetComponentsInChildren<Button>(true).Where(b => b.transform.parent == holder.transform).ToArray();
				if (own.Length == 0) return;
				Button model = own[0];
				GameObject copy = UnityEngine.Object.Instantiate(model.gameObject, holder.transform);
				copy.name = WorldWindow.ButtonName;
				// (Raft's localisation would put the model's own word back)
				foreach (Component loc in copy.GetComponentsInChildren<Component>(true).Where(x => x != null && x.GetType().Name.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0).ToList())
					UnityEngine.Object.Destroy(loc);
				foreach (Text t in copy.GetComponentsInChildren<Text>(true)) t.text = "Custom Islands";
				foreach (Component tmp in copy.GetComponentsInChildren<Component>(true).Where(x => x != null && x.GetType().Name.StartsWith("TextMeshPro")))
					Traverse.Create(tmp).Property("text").SetValue("Custom Islands");
				Button button = copy.GetComponent<Button>();
				button.onClick = new Button.ButtonClickedEvent();
				button.onClick.AddListener(WorldWindow.Open);
				copy.transform.SetSiblingIndex(Mathf.Max(0, own[own.Length - 1].transform.GetSiblingIndex()));
				Debug.Log("[CUSTOM ISLANDS] Pause menu: a Custom Islands button (the world's settings)");
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] The pause menu's Custom Islands button: " + e.Message); }
		}
	}
}
