using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The world plan editor (top bar "World plans..."): a plan's description, whether random islands also appear, and
	/// its rules as cards: when -> bring what -> where, with the message players see. The same window edits the rules
	/// of the island being edited ("Island rules..." on the Island tab), which travel with the island file.
	/// Check lists what can't work (missing islands, names that point nowhere); the map shows roughly where islands go.
	/// </summary>
	public class WorldPlanWindow : MonoBehaviour
	{
		static WorldPlanWindow instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		WorldPlan plan;
		/// <summary>Editing the island's own rules instead of a plan.</summary>
		bool islandMode;
		Text titleText, problemsText, planNameText;
		Button randomButton, planButton, newButton, copyButton, deleteButton, exportButton, importButton, storyButton, previewButton;
		readonly Dictionary<string, Button> storyIslandButtons = new Dictionary<string, Button>();
		InputField descriptionField;
		Text randomNote, rulesIntro;
		Button helpButton, shareHelp;
		RectTransform planRow, settingsRow, storyRow, rulesList, map;
		readonly List<InputField> fields = new List<InputField>();

		public static void Create(Transform canvas)
		{
			var blocker = new GameObject("WorldPlanWindow", typeof(RectTransform), typeof(Image));
			blocker.layer = 5;
			blocker.transform.SetParent(canvas, false);
			UIKit.Stretch((RectTransform)blocker.transform);
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);
			instance = blocker.AddComponent<WorldPlanWindow>();
			instance.Build();
			blocker.SetActive(false);
		}

		/// <summary>Opens the plan editor on a plan (the last one edited, or the first saved one, or a new one).</summary>
		public static void Open(string name = null)
		{
			if (instance == null) return;
			instance.islandMode = false;
			string pick = name ?? lastPlan ?? WorldPlan.All().FirstOrDefault(n => !WorldPlan.IsBuiltIn(n));
			WorldPlan p = pick != null ? WorldPlan.Load(pick) : null;
			if (p == null || p.BuiltIn) { EnsureSamples(); p = WorldPlan.Load(WorldPlan.All().FirstOrDefault(n => !WorldPlan.IsBuiltIn(n)) ?? "") ?? NewPlan("My plan"); }
			instance.Show(p);
		}

		/// <summary>Opens the plan editor on this plan as it is (back from Preview notebook: unsaved changes kept).</summary>
		public static void OpenWith(WorldPlan p)
		{
			if (instance == null || p == null) return;
			instance.islandMode = false;
			instance.Show(p);
		}

		/// <summary>Raft's tab colours for the helper's lists.</summary>
		public static IList<DropList.Option> TabColourChoices { get { return TabColourOptions; } }

		/// <summary>The "New main story..." helper's islands as rule cards (after the plan's own), then Check.</summary>
		public static void AddFromHelper(bool raftStory, List<IntroRule> rules)
		{
			if (instance == null || instance.plan == null) return;
			instance.Keep();
			instance.plan.RaftStory = raftStory;
			instance.plan.Rules.AddRange(rules);
			instance.ShowRandom();
			instance.ShowRules();
			instance.Check();
			instance.problemsText.text = rules.Count + " main story card(s) made. Look them over (NOTEBOOK: tab title and intro), then Preview notebook. " + instance.problemsText.text;
		}

		Button testButton;

		/// <summary>Test this plan (ROADMAP T2b): the plan saved, then a new test world with it.</summary>
		void TestPlan()
		{
			if (islandMode) return;
			Keep();
			try { plan.Save(); }
			catch (Exception e) { DynamicIslands.Notify("Could not save the plan: " + e.Message, true); return; }
			string name = plan.Name;
			Close();
			IslandTest.StartPlan(name);
		}

		/// <summary>
		/// A name for a new rule that no rule of the plan has had (ROADMAP AU24: "rule" + the count came back after a delete -
		/// "rule3" again, already done in running worlds, so the new rule never came): the next number with a letter of its
		/// own, e.g. "rule4k".
		/// </summary>
		internal static string NewRuleId(WorldPlan p)
		{
			var used = new HashSet<string>(p.Rules.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
			const string letters = "abcdefghjkmnpqrstuvwxyz";
			var rnd = new System.Random();
			for (int tries = 0; tries < 100; tries++)
			{
				// (three letters: a deleted rule's name comes back about once in 12 000, not once in 23 - review 2026-10-06)
				string id = "rule" + (p.Rules.Count + 1) + letters[rnd.Next(letters.Length)] + letters[rnd.Next(letters.Length)] + letters[rnd.Next(letters.Length)];
				if (!used.Contains(id)) return id;
			}
			return "rule" + DateTime.Now.Ticks.ToString("x");
		}

		/// <summary>Preview notebook: the plan as it is now, in Raft's notebook in the test world.</summary>
		void PreviewNotebook()
		{
			Keep();
			if (!plan.Rules.Any(r => r.MainStory)) { problemsText.text = "Preview notebook: no island is in the main story yet - choose a place in the story (STORY) for at least one island."; return; }
			WorldPlan p = WorldPlan.Parse(plan.Name, plan.ToText());
			p.Description = plan.Description;
			Close();
			IslandTest.StartPreview(p);
		}

		/// <summary>Opens the window on the rules of the island being edited.</summary>
		public static void OpenIsland()
		{
			if (instance == null) return;
			instance.islandMode = true;
			var p = new WorldPlan { Name = DynamicIslands.currentIslandName, Rules = WorldDirector.RulesFromProps(DynamicIslands.currentIslandProps) };
			instance.Show(p);
		}

		public static void Close()
		{
			if (instance == null) return;
			DropList.Close();
			// (its Check report belongs to it: left open, it stayed on the screen after World Plans closed)
			PlanCheckWindow.Close();
			instance.gameObject.SetActive(false);
			EditorInput.IsTyping = false;
		}

		static string lastPlan;

		void Show(WorldPlan p)
		{
			plan = p;
			if (!islandMode) lastPlan = p.Name;
			gameObject.SetActive(true);
			transform.SetAsLastSibling();
			titleText.text = islandMode ? "ISLAND RULES" : "WORLD PLANS";
			planRow.gameObject.SetActive(!islandMode);
			settingsRow.gameObject.SetActive(!islandMode);
			storyRow.gameObject.SetActive(!islandMode);
			descriptionField.transform.parent.gameObject.SetActive(!islandMode);
			if (exportButton != null) { exportButton.gameObject.SetActive(!islandMode); importButton.gameObject.SetActive(!islandMode); shareHelp.gameObject.SetActive(!islandMode); }
			if (previewButton != null) previewButton.gameObject.SetActive(!islandMode);
			planNameText.text = islandMode ? "Islands that '" + p.Name + "' brings into a world (saved with the island; \"self\" = this island)" : "";
			UIKit.LabelOf(planButton).text = "Plan: " + p.Name + "  \u25BC";
			descriptionField.text = p.Description;
			ShowRandom();
			ShowRules();
			problemsText.text = "";
			findings = null;
			ownFacts = null;
		}

		void ShowRandom()
		{
			UIKit.LabelOf(randomButton).text = "Random islands while sailing: " + (plan.Random ? "on" : "off");
			if (randomNote != null) randomNote.text = plan.Random ? "Islands from your spawn pool also turn up by chance, between the plan's islands." : "Only the islands the rules bring come - best for a story.";
			UIKit.SetActive(randomButton, plan.Random);
			UIKit.LabelOf(storyButton).text = "Raft's story islands: " + (plan.RaftStory ? "on" : "off");
			UIKit.SetActive(storyButton, plan.RaftStory);
			foreach (var kv in storyIslandButtons)
			{
				// (one of the plan's islands in its place: out, and says so)
				bool replaced = plan.Rules.Any(r => r.StoryPlace.Equals("instead:" + kv.Key, StringComparison.OrdinalIgnoreCase));
				bool on = plan.RaftStory && !plan.LeaveOut.Contains(kv.Key) && !replaced;
				UIKit.SetActive(kv.Value, on);
				kv.Value.interactable = plan.RaftStory;
				Text l = UIKit.LabelOf(kv.Value);
				l.text = StoryOrder.Name(StoryOrder.Parse(kv.Key)) + (replaced ? " (yours)" : "");
				l.color = on ? UIKit.TextColor : UIKit.TextMuted;
			}
		}

		/// <summary>Raft's story on or off (off: only the plan's own islands - a new adventure).</summary>
		public void FlipStory() { plan.RaftStory = !plan.RaftStory; ShowRandom(); ShowRules(); }

		/// <summary>One of Raft's story islands in or out of this plan's story.</summary>
		public void FlipStoryIsland(string key)
		{
			if (!plan.LeaveOut.Remove(key)) plan.LeaveOut.Add(key);
			ShowRandom();
			ShowRules();
		}

		#region The recipe player (the library's plans made in this window, step by step)

		/// <summary>A new, empty plan in the window, as New... makes it (a plan saved under that name before is replaced).</summary>
		public static void RecipeNew(string name)
		{
			if (instance == null) return;
			instance.islandMode = false;
			instance.Show(NewPlan(name));
		}

		/// <summary>The description typed into its field.</summary>
		public static void RecipeDescription(string text)
		{
			if (instance == null) return;
			instance.descriptionField.text = text;
			instance.descriptionField.onEndEdit.Invoke(text);
			instance.plan.Description = text;
		}

		/// <summary>"Random islands while sailing" clicked until it says on / off.</summary>
		public static void RecipeRandom(bool on) { if (instance != null && instance.plan.Random != on) instance.randomButton.onClick.Invoke(); }

		/// <summary>"Raft's story islands" clicked until it says on / off.</summary>
		public static void RecipeStory(bool on) { if (instance != null && instance.plan.RaftStory != on) instance.storyButton.onClick.Invoke(); }

		/// <summary>One of Raft's story islands clicked out of the plan's story (false: no such island in the story row).</summary>
		public static bool RecipeLeaveOut(string key)
		{
			Button b = StoryIslandButton(key);
			if (b == null) return false;
			if (!instance.plan.LeaveOut.Contains(key)) b.onClick.Invoke();
			return instance.plan.LeaveOut.Contains(key);
		}

		/// <summary>A rule as its card's fields are filled in (Add rule, then each field): one with the same id is replaced.</summary>
		public static void RecipeRule(IntroRule r)
		{
			if (instance == null) return;
			instance.Keep();
			int at = instance.plan.Rules.FindIndex(x => x.Id.Equals(r.Id, StringComparison.OrdinalIgnoreCase));
			if (at >= 0) instance.plan.Rules[at] = r; else instance.plan.Rules.Add(r);
			instance.ShowRandom();
			instance.ShowRules();
		}

		/// <summary>Save clicked (Check runs): the problems Check found, empty when there are none.</summary>
		public static List<string> RecipeSave()
		{
			if (instance == null) return new List<string> { "the World plans window isn't there" };
			instance.Save();
			PlanCheckWindow.Close();
			return (instance.findings ?? new List<PlanChecker.Finding>()).Where(f => f.Level == PlanChecker.Level.Problem).Select(f => f.Text).ToList();
		}

		/// <summary>The plan's Check report as text (warnings and tips too), for the recipe's log.</summary>
		public static List<string> RecipeFindings()
		{
			return instance != null && instance.findings != null ? instance.findings.Select(f => f.Level + ": " + f.Text).ToList() : new List<string>();
		}

		#endregion

		/// <summary>What Check showed last (tests read it).</summary>
		public static string LastCheck { get; private set; }

		public static WorldPlan Plan { get { return instance != null ? instance.plan : null; } }
		public static Button StoryButton { get { return instance != null ? instance.storyButton : null; } }
		public static Button StoryIslandButton(string key) { Button b; return instance != null && instance.storyIslandButtons.TryGetValue(key, out b) ? b : null; }

		void Update()
		{
			if (ChoiceWindow.IsOpen || TextPromptWindow.IsOpen || InfoWindow.IsOpen || DropList.Busy || PlanCheckWindow.IsOpen || MainStoryHelper.IsOpen || EditorInput.SubWindowJustClosed) return;
			EditorInput.IsTyping = fields.Any(f => f != null && f.isFocused);
			if (Input.GetKeyDown(KeyCode.Escape)) Close();
		}

		void OnDisable() { EditorInput.IsTyping = false; }

		#region Building

		void Build()
		{
			RectTransform panel = UIKit.Panel(transform, "Panel", new RectOffset(16, 16, 14, 16), 8f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 0));
			RectTransform head = UIKit.Row(panel, 28f, 6f, "Head");
			titleText = UIKit.Label(head, "WORLD PLANS", 18, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			UIKit.Label(head, "Which islands a world gets, when and where. Choose a plan when creating a world (New Game) or with WorldPlan <name>", 12, UIKit.TextMuted, TextAnchor.MiddleRight);
			helpButton = UIKit.Button(head, "Help", ShowHelp, "How this window works: the steps to make a plan, and the guide", 80, 26f, 13);
			helpButton.name = "HelpButton";
			UIKit.Primary(helpButton);
			planNameText = UIKit.Label(panel, "", 13, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "IslandNote");

			planRow = UIKit.Row(panel, 30f, 6f, "PlanRow");
			planButton = UIKit.Button(planRow, "Plan", PickPlan, "Choose the plan to edit", 320, 30f, 13);
			newButton = UIKit.Button(planRow, "New...", () => AskName("New plan", "A new, empty plan", "", n => Show(NewPlan(n))), "Start a new plan", 90, 30f, 12);
			copyButton = UIKit.Button(planRow, "Copy...", () => AskName("Copy plan", "A copy of '" + plan.Name + "'", plan.Name + " copy", n => { Keep(); var c = WorldPlan.Parse(n, plan.ToText()); c.Save(); Show(c); }), "Save a copy under another name", 90, 30f, 12);
			deleteButton = UIKit.Button(planRow, "Delete", DeletePlan, "Move this plan to the deleted\\plans folder (worlds that use it keep their own copy)", 90, 30f, 12);
			UIKit.DangerButton(deleteButton);
			HelpMark(planRow, HelpPlans);
			UIKit.Size(UIKit.Label(planRow, "", 12, UIKit.TextMuted).gameObject, -1, -1, 1);
			UIKit.Button(planRow, "Templates...", PickTemplate, "Add a ready-made set of rules (story chain, treasure hunt...) to this plan", 120, 30f, 12);
			UIKit.Button(planRow, "New main story...", () => { Keep(); MainStoryHelper.Open(); }, "Make a main story step by step: Raft's story on or off, then your islands in the order players find them on the Receiver - each gets a tab in Raft's notebook", 150, 30f, 12).name = "Button_NewMainStory";
			HelpMark(planRow, HelpTemplates);

			settingsRow = UIKit.Row(panel, 28f, 6f, "Settings");
			randomButton = UIKit.Button(settingsRow, "", () => { plan.Random = !plan.Random; ShowRandom(); }, "Also let islands from the spawn pool (spawnpool.txt) appear by chance while sailing, as in the \"Random islands\" plan", 330, 28f, 12);
			HelpMark(settingsRow, HelpRandom);
			randomNote = UIKit.Label(settingsRow, "", 12, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "RandomNote");
			storyRow = UIKit.Row(panel, 28f, 4f, "Story");
			storyButton = UIKit.Button(storyRow, "", () => { Keep(); FlipStory(); }, "Raft's own story islands (Radio Tower ... Utopia, found with the Receiver) come in worlds with this plan. Off: only this plan's own islands - a completely new adventure", 210, 28f, 12);
			storyButton.name = "StoryToggle";
			storyIslandButtons.Clear();
			foreach (ChunkPointType t in StoryOrder.Chain)
			{
				string key = StoryOrder.Key(t);
				Button b = UIKit.Button(storyRow, StoryOrder.Name(t), () => { Keep(); FlipStoryIsland(key); }, "Click to leave " + StoryOrder.Name(t) + " out of the story (the note before it leads to the one after), or put it back. " +
					"To put one of your islands in its place, give that island's rule the story place \"in place of " + StoryOrder.Name(t) + "\"", -1, 28f, 11);
				b.name = "Story_" + key;
				storyIslandButtons[key] = b;
			}
			HelpMark(storyRow, HelpStory);
			RectTransform descriptionRow = UIKit.Row(panel, 28f, 6f, "Description");
			descriptionField = UIKit.Field(descriptionRow, "Description, shown when choosing the plan (e.g. A story across five islands)", "", 28f, "Shown in the New Game box");
			descriptionField.characterLimit = 400; // (120 cut the library plans' descriptions short)
			fields.Add(descriptionField);
			HelpMark(descriptionRow, HelpDescription);
			// (how to read a card, once, above them)
			rulesIntro = UIKit.Label(panel, "Each card is one rule and brings one island: <b>WHEN</b> something happens → <b>BRING</b> an island → <b>WHERE</b> it goes → what to <b>TELL</b> the players. " +
				"Choose from the ▼ lists; the line in italics under each part says what the choice does.", 12, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "RulesIntro");
			rulesIntro.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(rulesIntro.gameObject, -1, 32);

			RectTransform body = UIKit.Row(panel, 440f, 10f, "Body");
			RectTransform rulesBox = UIKit.Rect("Rules", body);
			UIKit.Size(rulesBox.gameObject, -1, 440, 1);
			ScrollRect scroll;
			rulesList = UIKit.ScrollList(rulesBox, out scroll, 6f);
			UIKit.Stretch((RectTransform)scroll.transform);
			RectTransform side = UIKit.Rect("Side", body);
			UIKit.Size(side.gameObject, 250, 440);
			UIKit.Vertical(side.gameObject, 6f, new RectOffset(0, 0, 0, 0));
			RectTransform mapHead = UIKit.Row(side, 18f, 4f, "MapHead");
			UIKit.Label(mapHead, "WHERE ISLANDS GO (ROUGHLY)", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
			HelpMark(mapHead, HelpMap);
			map = UIKit.Rect("Map", side);
			UIKit.Size(map.gameObject, 250, 250);
			UIKit.Background(map.gameObject, new Color(0.08f, 0.2f, 0.3f, 1f), 6);
			problemsText = UIKit.Label(side, "", 12, UIKit.TextColor, TextAnchor.UpperLeft);
			UIKit.Size(problemsText.gameObject, 250, 160);
			problemsText.verticalOverflow = VerticalWrapMode.Truncate;

			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			UIKit.Button(buttons, "+ Add a rule", AddRule, "Another rule: when something happens, bring an island", 140, 34f, 13);
			UIKit.Button(buttons, "Check", () => { Keep(); Check(); }, "Look for rules that can't work (missing islands, names that point nowhere) and draw the map", 110, 34f, 13);
			HelpMark(buttons, HelpCheck);
			previewButton = UIKit.Button(buttons, "Preview notebook", PreviewNotebook, "See the main story in Raft's own notebook, in the test world: every tab and page, and step by step as players will find them (the plan needn't be saved)", 150, 34f, 13);
			previewButton.name = "Button_PreviewNotebook";
			testButton = UIKit.Button(buttons, "Test this plan", TestPlan, "Play the plan in a new test world (saved first): its islands come as in any world; Esc > Custom Islands > Back to the editor brings you back here", 130, 34f, 13);
			testButton.name = "Button_TestPlan";
			exportButton = UIKit.Button(buttons, "Export...", ExportPlan, "Share this plan: a pack (.zip) with every island it needs, to send or to put in the island library (saves it first)", 110, 34f, 13);
			importButton = UIKit.Button(buttons, "Import...", () => { Close(); LibraryImportWindow.Open(); }, "Install plans and islands from a pack (.zip) someone made, or remove what you installed", 110, 34f, 13);
			shareHelp = HelpMark(buttons, HelpShare);
			UIKit.Size(UIKit.Label(buttons, "", 12, UIKit.TextMuted).gameObject, -1, -1, 1);
			Button save = UIKit.Button(buttons, "Save", Save, "Keep the changes", 110, 34);
			UIKit.Primary(save);
			UIKit.Button(buttons, "Close", Close, "Close without saving", 110, 34);
		}

		// The choices of a rule's parts: the words in the drop-down and the line that explains the chosen one
		static readonly DropList.Option[] WhenOptions =
		{
			new DropList.Option("start", "When the world starts", "At once, as soon as a new world begins"),
			new DropList.Option("km", "After sailing a distance", "When the raft has sailed this many km in the world"),
			new DropList.Option("day", "On a day", "On this day of the world (day 1 is the first)"),
			new DropList.Option("quest", "When a quest is done", "When the quest of an island is finished (the island needs a quest)"),
			new DropList.Option("step", "When quest steps are done", "When this many steps of an island's quest are done"),
			new DropList.Option("zone", "When a trigger zone fires", "When a player walks into a trigger zone on an island"),
			new DropList.Option("visit", "When players reach an island", "When a player first comes to an island"),
			new DropList.Option("rule", "Right after another rule", "Straight after another rule's island has appeared"),
			new DropList.Option("signal", "When a signal is sent", "When an object on an island sends a signal (a behaviour's \"send a signal\")"),
		};
		static readonly DropList.Option[] WhatOptions =
		{
			new DropList.Option("type", "A new island of a map type", "The mod makes a new island of this kind in each world (camp, volcano, wreck...): nothing to share, it always works"),
			new DropList.Option("island", "One of my saved islands", "An island you built in the editor, exactly as you saved it"),
			new DropList.Option("oneof", "One island from a list", "One of several saved islands, picked at random (ones not in the world yet first)"),
			new DropList.Option("pool", "A random island (spawn pool)", "Any island of your spawn pool (spawnpool.txt), as random islands are"),
		};
		static readonly DropList.Option[] WhereOptions =
		{
			new DropList.Option("ahead", "Ahead of the raft", "This many metres ahead of the raft, the way it sails"),
			new DropList.Option("near", "Near an island", "This many metres from an island (centre to centre), in a direction"),
			new DropList.Option("receiver", "On the Receiver", "It gets its own frequency on Raft's Receiver and comes when a player tunes to it"),
			new DropList.Option("sailing", "By chance while sailing", "It turns up ahead of the raft some time later, while you sail"),
		};
		static readonly DropList.Option[] DoneOptions =
		{
			new DropList.Option("", "Its quest is done (or reached)", "Its quest is finished; an island without a quest counts when players reach it"),
			new DropList.Option("quest", "Its quest is done", "The island's quest is finished"),
			new DropList.Option("visit", "Players reach it", "A player comes to the island"),
			new DropList.Option("step", "Quest steps are done", "This many steps of its quest are done"),
			new DropList.Option("zone", "Its trigger zone fires", "A player walks into this trigger zone on it"),
			new DropList.Option("signal", "Its signal is sent", "An object on it sends this signal"),
			new DropList.Option("note", "Its note is read", "A player reads this note on it (by the note's number)"),
		};
		/// <summary>Raft's ten tab colours (the sprites NoteBook_Thumbnail_1..10), and "by its place".</summary>
		static readonly DropList.Option[] TabColourOptions =
		{
			new DropList.Option("0", "Automatic colour", "A colour by its place in the story"),
			// (Raft has nine: there is no NoteBook_Thumbnail_3)
			new DropList.Option("1", "Pink", "Raft's tab colour 1 (Utopia's)"), new DropList.Option("2", "Sand", "Raft's tab colour 2 (First page's)"),
			new DropList.Option("4", "Purple", "Raft's tab colour 4 (Caravan Town's)"), new DropList.Option("5", "Teal", "Raft's tab colour 5 (Varuna Point's)"),
			new DropList.Option("6", "Olive", "Raft's tab colour 6 (Tangaroa's)"), new DropList.Option("7", "Red", "Raft's tab colour 7 (Radio Tower's)"),
			new DropList.Option("8", "Green", "Raft's tab colour 8 (Vasagatan's)"), new DropList.Option("9", "Blue", "Raft's tab colour 9 (Balboa's)"),
			new DropList.Option("10", "Light blue", "Raft's tab colour 10 (Temperance's)"),
		};
		static DropList.Option[] DirectionOptions
		{
			get { return IntroRule.Directions.Select(d => new DropList.Option(d, d == "any" ? "Any way" : Capital(d), d == "any" ? "Wherever there is room" : "To the " + d + " of the island")).ToArray(); }
		}

		const float SectionLabel = 72f; // (the width of the WHEN / BRING / WHERE... column)

		void ShowRules()
		{
			DropList.Close();
			if (findings != null && !inCheck)
			{
				inCheck = true;
				try { findings = PlanChecker.Check(plan, islandMode, false, ownFacts); ShowFindings(); }
				finally { inCheck = false; }
			}
			foreach (Transform child in rulesList) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
			fields.RemoveAll(f => f == null || f.transform.IsChildOf(rulesList));
			for (int i = 0; i < plan.Rules.Count; i++) RuleCard(i);
			if (plan.Rules.Count == 0) UIKit.Label(rulesList, "<i>No rules yet. \"+ Add a rule\", or Templates... for a ready-made set.</i>", 13, UIKit.TextMuted);
			if (!islandMode && plan.Rules.Any(r => r.MainStory)) EndingCard();
			DrawMap();
			ShowRandom(); // (the story row follows the rules: an island of the plan in a story island's place)
		}

		/// <summary>
		/// A rule as a card, top to bottom: its number, name and a sentence saying what it does; then one section per
		/// question - WHEN it comes, what it BRINGS, WHERE it goes, what to TELL the players, and (world plans) its place in
		/// Raft's story. Each section: a drop-down with the choices, the fields that choice needs (with their names), and a
		/// line explaining the choice.
		/// </summary>
		void RuleCard(int index)
		{
			IntroRule r = plan.Rules[index];
			RectTransform card = UIKit.Group(rulesList, "", "Rule");
			float height = 16f; // (the group's padding)

			// The rule's number and name, what it does in a sentence, and the arrows
			RectTransform head = UIKit.Row(card, 28f, 6f, "Head");
			Text number = UIKit.Label(head, "RULE " + (index + 1), 14, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			UIKit.Size(number.gameObject, SectionLabel - 6f);
			UIKit.Size(UIKit.Label(head, "Name", 12, UIKit.TextMuted, TextAnchor.MiddleRight).gameObject, 40);
			SmallField(head, "id", r.Id, 130, "The rule's name: other rules refer to the island it brings by it (e.g. camp)", v => r.Id = v.Replace("|", "").Replace(":", "").Replace(",", "").Replace(";", "").Trim());
			HelpMark(head, islandMode ? HelpIdIsland : HelpId);
			UIKit.Size(UIKit.Label(head, "", 12).gameObject, -1, -1, 1);
			UIKit.Button(head, "▲", () => { if (index > 0) { Keep(); plan.Rules.Reverse(index - 1, 2); ShowRules(); } }, "Move this rule up (the order only matters for reading: each rule waits for its own WHEN)", 28, 26f, 11);
			UIKit.Button(head, "▼", () => { if (index < plan.Rules.Count - 1) { Keep(); plan.Rules.Reverse(index, 2); ShowRules(); } }, "Move this rule down", 28, 26f, 11);
			Button del = UIKit.Button(head, "Remove", () => { Keep(); plan.Rules.RemoveAt(index); ShowRules(); }, "Remove this rule", 80, 26f, 11);
			UIKit.DangerButton(del);
			height += 28f + 6f;
			Text describe = UIKit.Label(card, r.Describe(), 13, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Italic, "Describe");
			describe.horizontalOverflow = HorizontalWrapMode.Wrap;
			UIKit.Size(describe.gameObject, -1, 34);
			height += 34f + 6f;
			CardFindings(card, index, ref height);

			// WHEN
			RectTransform when = Section(card, "WHEN", ref height);
			Button whenDrop = DropList.Make(when, "Drop_When", WhenOptions.Select(o => new DropList.Option(o.Value, o.Label, WhenHint(o.Value))).ToList(), r.When, v =>
			{
				Keep();
				r.When = v;
				if ((v == "km" || v == "day") && !float.TryParse(r.WhenArg, NumberStyles.Float, CultureInfo.InvariantCulture, out float _)) r.WhenArg = v == "km" ? "1" : "2";
				if (v == "step" && !int.TryParse(r.WhenArg, out int _)) r.WhenArg = "1";
				if (v == "zone" || v == "signal") r.WhenArg = "";
				if (v == "quest" && !int.TryParse(r.WhenArg, out int _)) r.WhenArg = "";
				if (islandMode && r.WhenRef.Length == 0 && v != "rule") r.WhenRef = IntroRule.Self;
				ShowRules();
			}, 230, "What the rule waits for: choose from the list");
			bool needsRef = r.When == "quest" || r.When == "step" || r.When == "zone" || r.When == "visit" || r.When == "rule" || r.When == "signal";
			bool needsArg = r.When == "km" || r.When == "day" || r.When == "step" || r.When == "zone" || r.When == "signal";
			if (r.When == "day") Tag(when, "day");
			if (needsRef)
			{
				Tag(when, r.When == "rule" ? "rule" : "island");
				SmallField(when, r.When == "rule" ? "rule id" : islandMode ? "self" : "rule id / island", r.WhenRef, 130,
					r.When == "rule" ? "The id of the rule to wait for" : "Which island: the id of the rule that brought it, or an island name" + (islandMode ? " (self = this island)" : ""), v => r.WhenRef = v.Trim());
				Pick(when, "Pick_WhenRef", r.When == "rule" ? "Choose the rule to wait for" : "Choose the island: a rule of this plan, or one of your saved islands", () => WhenRefChoices(r),
					r.When == "rule" ? "Rule to wait for" : "Island to wait for", v => r.WhenRef = v, () => r.When == "rule" ? "There are no other rules yet: + Add a rule first." : "No rules or saved islands yet.");
			}
			if (needsArg)
			{
				if (r.When == "step" || r.When == "zone" || r.When == "signal") Tag(when, r.When == "step" ? "steps" : r.When);
				SmallField(when, r.When == "signal" ? "signal name" : r.When == "zone" ? "zone name" : r.When == "step" ? "steps" : r.When, r.WhenArg, r.When == "zone" || r.When == "signal" ? 120 : 56,
					r.When == "zone" ? "The trigger zone's name on that island" : r.When == "step" ? "How many steps of the quest are done" : r.When == "km" ? "Km sailed in this world" : "In-game day", v => r.WhenArg = v.Trim());
				if (r.When == "zone" || r.When == "signal" || r.When == "step")
					Pick(when, "Pick_WhenArg", "Choose from the " + ThingsOf(r.When) + " of that island", () => ThingChoices(IslandOf(r.WhenRef), r.When),
						Capital(ThingsOf(r.When)) + " of " + RefLabel(r.WhenRef), v => r.WhenArg = v, () => NoThings(r.WhenRef, IslandOf(r.WhenRef), r.When));
				if (r.When == "km") Tag(when, "km sailed", false);
			}
			if (r.When == "quest")
			{
				// (which of the island's quests: empty = the main quest; ROADMAP CB6)
				Tag(when, "quest no.");
				SmallField(when, "main", r.WhenArg, 56, "Which of the island's quests: empty or 1 = the main quest, 2 = its Quest 2, and so on",
					v => { v = v.Trim(); r.WhenArg = v == "1" ? "" : v; });
			}
			Fill(when);
			HelpMark(when, islandMode ? HelpWhenIsland : HelpWhen);
			Explain(card, WhenHint(r.When), ref height);

			// BRING
			RectTransform bring = Section(card, "BRING", ref height);
			DropList.Make(bring, "Drop_What", WhatOptions, r.What, v =>
			{
				Keep();
				r.What = v;
				r.WhatArg = v == "type" ? "random" : "";
				ShowRules();
			}, 230, "Which island comes: choose from the list");
			if (r.What != "pool")
			{
				Tag(bring, r.What == "type" ? "map type" : r.What == "oneof" ? "islands" : "island");
				SmallField(bring, r.What == "oneof" ? "island, island, ..." : r.What == "type" ? "map type" : "island name", r.WhatArg, -1,
					r.What == "oneof" ? "Island names, separated by commas: one is picked (ones not in the world yet first)" : "Which one (▾ to choose)", v => r.WhatArg = v.Trim());
				Button choose = UIKit.Button(bring, "▾", () =>
				{
					Keep();
					Action<string> set = v => { r.WhatArg = r.What == "oneof" && r.WhatArg.Trim().Length > 0 ? r.WhatArg.Trim() + ", " + v : v; ShowRules(); };
					if (r.What == "type") ChoiceWindow.Open("Map type", ChoiceWindow.Types(), set);
					else ChoiceWindow.Open(r.What == "oneof" ? "Add an island to the list" : "Island", ChoiceWindow.Islands(), set);
				}, r.What == "oneof" ? "Add one of your saved islands to the list" : "Choose from a list", 24, 26f, 12);
				choose.name = "Pick_What";
			}
			else Fill(bring);
			HelpMark(bring, HelpWhat);
			Explain(card, WhatHint(r), ref height);

			// WHERE
			RectTransform where = Section(card, "WHERE", ref height);
			List<DropList.Option> whereOptions = (islandMode ? WhereOptions.Where(o => o.Value == "ahead" || o.Value == "near") :
				// (main story: only the Receiver - its coordinates are what players follow, and its notebook tab shows them)
				r.MainStory ? WhereOptions.Where(o => o.Value == "receiver") : WhereOptions).ToList();
			DropList.Make(where, "Drop_Where", whereOptions, r.Where, v =>
			{
				Keep();
				r.Where = v;
				if (v == "near") { r.WhereRef = islandMode ? IntroRule.Self : ""; r.Distance = Mathf.Max(r.Distance, 600f); }
				if (v == "receiver") r.Distance = Mathf.Max(r.Distance, 600f);
				ShowRules();
			}, 230, "Where the island comes: choose from the list");
			SmallField(where, "300", r.Distance.ToString("0"), 60, "Metres (from the raft, or centre to centre from the island; at least clear of both)", v =>
			{
				float d;
				if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Distance = Mathf.Clamp(d, 50f, 5000f);
			}).contentType = InputField.ContentType.IntegerNumber;
			Tag(where, r.Where == "near" ? "metres" : r.Where == "ahead" ? "metres ahead" : "metres away", false);
			if (r.Where == "near")
			{
				DropList.Make(where, "Drop_Direction", DirectionOptions, r.Direction, v => { Keep(); r.Direction = v; ShowRules(); }, 110, "Which way from the island");
				Tag(where, "of");
				SmallField(where, islandMode ? "self" : "where it happened", r.WhereRef == IntroRule.Self && !islandMode ? "" : r.WhereRef, 130,
					"Which island: the id of the rule that brought it, or an island name. Empty = the island where the rule's event happened" + (islandMode ? "; self = this island" : ""), v => r.WhereRef = v.Trim());
				Pick(where, "Pick_WhereRef", "Choose the island to put it near", () => WhereRefChoices(r), "Put it near", v => r.WhereRef = v, () => "No rules or saved islands yet.");
			}
			Fill(where);
			HelpMark(where, islandMode ? HelpWhereIsland : HelpWhere);
			Explain(card, r.MainStory && !islandMode ? "MAIN STORY: found by its coordinates - its own frequency on the Receiver (shown on its notebook tab); it comes about this far away when a player tunes to it" : WhereHint(r), ref height);

			// TELL
			RectTransform tell = Section(card, "TELL", ref height);
			SmallField(tell, "Message to every player (optional)", r.Message, -1, "Shown when the island appears, with how far and which way it is", v => r.Message = v.Trim()).characterLimit = 160;
			Tag(tell, "on the Receiver");
			SmallField(tell, "Receiver name", r.Label, 140, "The island's name on Raft's Receiver (optional)", v => r.Label = v.Trim()).characterLimit = 18;
			HelpMark(tell, HelpTell);
			Explain(card, "The message pops up for every player when the island appears (with how far and which way); the name is its dot's name on Raft's Receiver. Both optional.", ref height);

			if (!islandMode) StoryRow(card, r, ref height);
			UIKit.Size(card.gameObject, -1, height);
		}

		/// <summary>A section of a card: its name in the left column, then the controls (a row).</summary>
		RectTransform Section(RectTransform card, string title, ref float height)
		{
			RectTransform row = UIKit.Row(card, 28f, 6f, "Section_" + title);
			Text t = UIKit.Label(row, title, 13, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "SectionTitle");
			UIKit.Size(t.gameObject, SectionLabel);
			height += 28f + 6f;
			return row;
		}

		/// <summary>The line under a section explaining the choice, lined up with the controls.</summary>
		void Explain(RectTransform card, string text, ref float height)
		{
			RectTransform row = UIKit.Row(card, 16f, 6f, "Explain");
			UIKit.Size(UIKit.Label(row, "", 11).gameObject, SectionLabel);
			Text t = UIKit.Label(row, text, 11, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Italic, "Explanation");
			UIKit.Fit(t, 8);
			height += 16f + 6f;
		}

		/// <summary>A small name before a field ("island", "zone"...), or a unit after it.</summary>
		static void Tag(Transform row, string text, bool before = true)
		{
			Text t = UIKit.Label(row, text, 12, UIKit.TextMuted, before ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
			UIKit.Size(t.gameObject, Mathf.Max(18f, text.Length * 7f + 6f));
		}

		/// <summary>Empty room at the end of a row, so the controls keep their widths.</summary>
		static void Fill(Transform row) { UIKit.Size(UIKit.Label(row, "", 12).gameObject, -1, -1, 1); }

		string WhenHint(string when)
		{
			string hint = WhenOptions.First(o => o.Value == when).Hint;
			if (!islandMode) return hint;
			return hint.Replace("an island", "an island (self = this island)");
		}

		static string WhatHint(IntroRule r)
		{
			string hint = WhatOptions.First(o => o.Value == r.What).Hint;
			if (r.What == "type")
			{
				MapType t = MapTypes.Get(r.WhatArg);
				if (t != null && !string.IsNullOrEmpty(t.Description)) hint = t.Label + ": " + t.Description;
			}
			return hint;
		}

		string WhereHint(IntroRule r)
		{
			switch (r.Where)
			{
				case "near": return "This far from " + (r.WhereRef.Length == 0 ? "the island where the WHEN happened" : r.WhereRef == IntroRule.Self ? "this island" : "'" + r.WhereRef + "'") + " (centre to centre), " + (r.Direction == "any" ? "wherever there is room" : "to the " + r.Direction);
				case "receiver": return "It gets its own frequency on Raft's Receiver and comes when a player tunes to it, about this far away";
				case "sailing": return "It turns up ahead of the raft some time later (a few hundred metres to 2 km more), about this far ahead";
			}
			return "This far ahead of the raft, the way it sails";
		}

		/// <summary>The rule's place in Raft's story (the Receiver chain) and when it counts as done there.</summary>
		void StoryRow(RectTransform card, IntroRule r, ref float height)
		{
			RectTransform c = Section(card, "STORY", ref height);
			Button place = DropList.Make(c, "StoryPlace", StoryPlaces(r), r.StoryPlace, v =>
			{
				Keep();
				r.StoryPlace = IntroRule.NormalPlace(v);
				// (a main story island is found by its coordinates: on the Receiver)
				if (r.MainStory && r.Where != "receiver") { r.Where = "receiver"; r.Distance = Mathf.Max(r.Distance, 600f); }
				ShowRules();
			}, 260,
				"Put this island into Raft's story chain: first, after one of Raft's story islands (or another of your islands in the story), or in place of one");
			if (r.InStory)
			{
				Tag(c, "next coordinates when");
				string kind = r.StoryDone.Split(':')[0], arg = r.StoryDone.Contains(":") ? r.StoryDone.Substring(r.StoryDone.IndexOf(':') + 1) : "";
				DropList.Make(c, "Drop_Done", DoneOptions, kind, v =>
				{
					Keep();
					r.StoryDone = v == "step" ? "step:1" : v == "zone" || v == "signal" || v == "note" ? v + ":" : v;
					ShowRules();
				}, 200, "When the players find the NEXT island's coordinates: when this island counts as done in the story (then the next island of the story is unlocked)");
				if (kind == "step" || kind == "zone" || kind == "signal" || kind == "note")
				{
					SmallField(c, kind == "step" ? "steps" : kind == "note" ? "note #" : kind + " name", arg, kind == "step" || kind == "note" ? 50 : 110,
						kind == "step" ? "How many steps of its quest" : kind == "note" ? "The note's number on the island (▾ lists them)" : "The " + kind + "'s name on the island", v => r.StoryDone = kind + ":" + v.Trim());
					string own = r.What == "island" ? r.WhatArg : null;
					Pick(c, "Pick_DoneArg", "Choose from the " + ThingsOf(kind) + " of the island this rule brings", () => ThingChoices(own, kind),
						Capital(ThingsOf(kind)) + " of " + (own ?? "its island"), v => r.StoryDone = kind + ":" + v, () => NoThings(r.Id, own, kind));
				}
			}
			Fill(c);
			HelpMark(c, HelpStoryPlace);
			Explain(card, r.Beside ? "MAIN STORY beside Raft's story: it goes into Raft's NOTEBOOK (after the chain's islands). Its own WHEN brings it - e.g. when the quest of the island before is done."
				: !r.InStory ? "SIDE QUEST: it goes into the JOURNAL (J). Its own WHEN decides when it comes. Choose a place in the story to make it MAIN STORY (Raft's notebook)."
				: "MAIN STORY: it goes into Raft's NOTEBOOK. " + r.DescribeStory().Replace("; done when ", "; the next coordinates come when ") + (r.Where == "receiver" ? " - players tune the Receiver to its frequency" : ""), ref height);
			if (r.MainStory) NotebookRow(card, r, ref height);
		}

		/// <summary>After the rules (a plan with a main story): the last page of the story in Raft's notebook.</summary>
		void EndingCard()
		{
			RectTransform card = UIKit.Group(rulesList, "", "Ending");
			float height = 16f;
			RectTransform c = Section(card, "THE END", ref height);
			InputField f = SmallField(c, "The last page of the main story in Raft's notebook (optional)", plan.StoryEnding, -1,
				"Shown in Raft's notebook, with a banner, when every main story island is done. Enter = a new line", v => plan.StoryEnding = v.Trim());
			f.lineType = InputField.LineType.MultiLineNewline;
			f.characterLimit = 600;
			f.name = "Field_StoryEnding";
			HelpMark(c, HelpNotebook);
			Explain(card, "When the last main story island is done, this page appears at the end of the notebook and every player gets a banner. The world goes on.", ref height);
			UIKit.Size(card.gameObject, -1, height);
		}

		/// <summary>Recipes and tests: the ending page typed in.</summary>
		public static void RecipeEnding(string text)
		{
			if (instance == null || instance.plan == null) return;
			instance.plan.StoryEnding = (text ?? "").Trim();
			instance.ShowRules();
		}

		/// <summary>A main story island's tab in Raft's notebook: its title, colour and the intro on its first page.</summary>
		void NotebookRow(RectTransform card, IntroRule r, ref float height)
		{
			RectTransform c = Section(card, "NOTEBOOK", ref height);
			Tag(c, "tab title");
			SmallField(c, r.TabName, r.TabTitle, 150, "The tab's title in Raft's notebook (empty: the Receiver name, else the island's name)", v => r.TabTitle = v.Replace("|", "/").Trim()).characterLimit = 24;
			DropList.Make(c, "Drop_TabColour", TabColourOptions, r.TabColour.ToString(CultureInfo.InvariantCulture), v => { Keep(); int n; r.TabColour = int.TryParse(v, out n) ? n : 0; ShowRules(); }, 170,
				"The tab's colour: one of Raft's nine notebook tab colours");
			Fill(c);
			HelpMark(c, HelpNotebook);
			RectTransform intro = Section(card, "", ref height);
			Tag(intro, "first page");
			InputField f = SmallField(intro, "The intro on the tab's first page (empty: \"A new frequency: #1234...\")", r.TabIntro, -1,
				"Shown on the tab's first page when the island's coordinates are found, before any note is read. Enter = a new line", v => r.TabIntro = v.Trim());
			f.lineType = InputField.LineType.MultiLineNewline;
			f.characterLimit = 400;
			Explain(card, "Its tab: the title" + (r.Where == "receiver" ? ", its Receiver #digits" : "") + " and this intro; then a page with its quest's steps (ticked off as players do them) and the notes read on it. Preview notebook shows it.", ref height);
		}

		const string HelpNotebook = "MAIN STORY islands get a tab in Raft's own notebook, in story order (between Raft's own islands when Raft's story is on).\n\n" +
			"The tab shows its title and, for an island on the Receiver, its frequency. Its pages: the intro you write here (shown when its coordinates are found), " +
			"its quest's steps (the steps done are crossed out), and the notes players read on the island - in Raft's paper and handwriting. Its story items show under Raft's 'Found items'.\n\n" +
			"Side quests (islands not in the story) stay in the journal (J).";

		/// <summary>The places an island can have in Raft's story, for the drop-down.</summary>
		List<DropList.Option> StoryPlaces(IntroRule r)
		{
			var list = new List<DropList.Option> { new DropList.Option("", "Side quest (not in the story)", "A side quest: the journal (J). Its own WHEN decides when it comes, as any rule"), new DropList.Option("first", "Main story: first", "Main story (Raft's notebook): unlocked from the start of the world, before Raft's first island"),
				new DropList.Option("beside", "Main story, beside Raft's", "Main story (Raft's notebook), but not in the Receiver chain: its own WHEN brings it, beside Raft's story (an expedition alongside it)") };
			foreach (ChunkPointType t in StoryOrder.Chain)
			{
				// (Raft's Utopia ends the story: it never counts as done, so nothing after it could come - offered only when
				// Utopia is left out or another island takes its place)
				if (!StoryChain.EndsStory(plan, t))
					list.Add(new DropList.Option("after:" + StoryOrder.Key(t), "After " + StoryOrder.Name(t), "Unlocked when " + StoryOrder.Name(t) + "'s note is found (or when it is done, if it is left out)"));
				list.Add(new DropList.Option("instead:" + StoryOrder.Key(t), "In place of " + StoryOrder.Name(t), StoryOrder.Name(t) + " is left out; the note before it leads here, and this island leads on"));
			}
			foreach (IntroRule o in plan.Rules.Where(o => o != r && o.InStory))
				list.Add(new DropList.Option("after:" + o.Id, "After your island '" + o.Id + "'", "Unlocked when that island is done"));
			// (a place the list doesn't have - an island of the plan that was renamed: shown as it is)
			if (r.StoryPlace.Length > 0 && !list.Any(o => o.Value == r.StoryPlace)) list.Add(new DropList.Option(r.StoryPlace, r.DescribeStory().Split(';')[0], "As the plan file says"));
			return list;
		}

		/// <summary>Tests: a rule's story place as a player picks it from the list.</summary>
		public static void SetStoryPlace(int rule, string place)
		{
			if (instance == null || instance.plan == null || rule < 0 || rule >= instance.plan.Rules.Count) return;
			instance.plan.Rules[rule].StoryPlace = IntroRule.NormalPlace(place);
			instance.ShowRules();
		}

		InputField SmallField(Transform row, string placeholder, string text, float width, string hint, Action<string> set)
		{
			InputField f = UIKit.Field(row, placeholder, text, 26f, hint);
			if (width > 0) UIKit.Size(f.gameObject, width, 26);
			((Text)f.placeholder).fontSize = 12;
			f.textComponent.fontSize = 12;
			f.onEndEdit.AddListener(v => set(v));
			fields.Add(f);
			return f;
		}

		#endregion

		#region Lists for the fields (combo boxes: type, or pick what exists with ▼)

		/// <summary>A ▼ after a field: the list of what exists (the rules of the plan, saved islands, an island's zones,
		/// signals or quest steps); picking one fills the field. Typing still works.</summary>
		void Pick(Transform row, string name, string hint, Func<List<ChoiceWindow.Choice>> choices, string title, Action<string> set, Func<string> empty)
		{
			Button b = UIKit.Button(row, "▾", () =>
			{
				Keep();
				ChoiceWindow.Open(title, choices(), v => { set(v); ShowRules(); }, empty());
			}, hint, 24, 26f, 12);
			b.name = name;
		}

		/// <summary>The plan's rules (except one) that bring an island, with what they bring.</summary>
		List<ChoiceWindow.Choice> RuleChoices(IntroRule except)
		{
			var list = new List<ChoiceWindow.Choice>();
			for (int i = 0; i < plan.Rules.Count; i++)
			{
				IntroRule o = plan.Rules[i];
				if (o == except || o.Id.Length == 0) continue;
				list.Add(new ChoiceWindow.Choice(o.Id, o.Id, "rule " + (i + 1) + ": " + o.DescribeWhat() + QuestNote(IslandOf(o.Id))));
			}
			return list;
		}

		/// <summary>Your saved islands, with whether they have a quest (quest rules need one).</summary>
		static List<ChoiceWindow.Choice> IslandChoices()
		{
			return ChoiceWindow.Islands().Select(c => new ChoiceWindow.Choice(c.Value, c.Label, "saved island" + (c.Detail.Length > 0 ? " \"" + c.Detail + "\"" : "") + QuestNote(c.Value))).ToList();
		}

		static string QuestNote(string island)
		{
			if (string.IsNullOrEmpty(island)) return "";
			IslandQuest q = IslandCache.QuestOf(island);
			return q.Exists ? " - quest, " + q.Steps.Count + " step" + (q.Steps.Count == 1 ? "" : "s") : " - no quest";
		}

		List<ChoiceWindow.Choice> WhenRefChoices(IntroRule r)
		{
			var list = new List<ChoiceWindow.Choice>();
			if (islandMode && r.When != "rule") list.Add(new ChoiceWindow.Choice(IntroRule.Self, "self", "this island" + QuestNote(DynamicIslands.currentIslandName)));
			list.AddRange(RuleChoices(r));
			if (r.When != "rule") list.AddRange(IslandChoices());
			return list;
		}

		List<ChoiceWindow.Choice> WhereRefChoices(IntroRule r)
		{
			var list = new List<ChoiceWindow.Choice>();
			if (islandMode) list.Add(new ChoiceWindow.Choice(IntroRule.Self, "self", "this island"));
			else list.Add(new ChoiceWindow.Choice("", "where it happened", "the island where the rule's event happened (only when \"When\" names an island)"));
			list.AddRange(RuleChoices(r));
			list.AddRange(IslandChoices());
			return list;
		}

		/// <summary>The saved island a rule id or island name stands for (null: a new map-type island, or unknown).</summary>
		string IslandOf(string reference)
		{
			if (string.IsNullOrEmpty(reference)) return null;
			if (reference.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase)) return islandMode ? DynamicIslands.currentIslandName : null;
			IntroRule by = plan.Rules.FirstOrDefault(x => x.Id.Equals(reference, StringComparison.OrdinalIgnoreCase));
			if (by != null) return by.What == "island" && by.WhatArg.Length > 0 ? by.WhatArg : null;
			return IslandSpawner.ListSavedIslands().FirstOrDefault(n => n.Equals(reference, StringComparison.OrdinalIgnoreCase));
		}

		static string ThingsOf(string kind) { return kind == "zone" ? "trigger zones" : kind == "signal" ? "signals" : kind == "note" ? "notes" : "quest steps"; }
		static string Capital(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
		static string RefLabel(string reference) { return string.IsNullOrEmpty(reference) ? "the island" : "'" + reference + "'"; }

		/// <summary>An island's zones, signals or quest steps (for steps: "how many are done", with each step's text).</summary>
		static List<ChoiceWindow.Choice> ThingChoices(string island, string kind)
		{
			var list = new List<ChoiceWindow.Choice>();
			if (string.IsNullOrEmpty(island)) return list;
			if (kind == "zone") list.AddRange(IslandCache.ZonesOf(island).Select(z => new ChoiceWindow.Choice(z, z, "trigger zone on " + island)));
			else if (kind == "signal") list.AddRange(IslandCache.SignalsOf(island).Select(s => new ChoiceWindow.Choice(s, s, "sent on " + island)));
			else if (kind == "note") list.AddRange(IslandCache.NotesOf(island).Select(n => new ChoiceWindow.Choice(n.ToString(CultureInfo.InvariantCulture), "Note #" + n, "a note with a text on " + island)));
			else
			{
				List<IslandQuest.Step> steps = IslandCache.QuestOf(island).Steps;
				for (int i = 0; i < steps.Count; i++)
					list.Add(new ChoiceWindow.Choice((i + 1).ToString(), (i + 1) + " step" + (i == 0 ? "" : "s") + " done", "the last: " + steps[i].Describe()));
			}
			return list;
		}

		/// <summary>Why the list is empty.</summary>
		string NoThings(string reference, string island, string kind)
		{
			if (string.IsNullOrEmpty(island))
				return string.IsNullOrEmpty(reference) ? "Choose the island first (▾ before this field)." :
					RefLabel(reference) + " is a new island made in the world (a map type) or not a saved island, so its " + ThingsOf(kind) + " aren't known here: type the name.";
			if (islandMode && reference == IntroRule.Self) return "'" + island + "' has no " + ThingsOf(kind) + " in its saved file. Save the island (Ctrl+S) after adding them.";
			return "'" + island + "' has no " + ThingsOf(kind) + (kind == "step" ? " (no quest)" : "") + ".";
		}

		/// <summary>Tests: what the ▼ of a rule's field lists (field: whenref, whenarg, whereref, donearg).</summary>
		public static List<ChoiceWindow.Choice> FieldChoices(int rule, string field)
		{
			if (instance == null || instance.plan == null || rule < 0 || rule >= instance.plan.Rules.Count) return new List<ChoiceWindow.Choice>();
			IntroRule r = instance.plan.Rules[rule];
			switch (field)
			{
				case "whenref": return instance.WhenRefChoices(r);
				case "whenarg": return ThingChoices(instance.IslandOf(r.WhenRef), r.When);
				case "whereref": return instance.WhereRefChoices(r);
				case "donearg": return ThingChoices(r.What == "island" ? r.WhatArg : null, r.StoryDone.Split(':')[0]);
			}
			return new List<ChoiceWindow.Choice>();
		}

		/// <summary>Tests: the plan as the window has it now.</summary>
		public static WorldPlan Current { get { return instance != null ? instance.plan : null; } }

		#endregion

		#region Help

		/// <summary>A "?" at the end of a row: hovering it explains that part.</summary>
		static Button HelpMark(Transform row, string text) { return UIKit.Help(row, text, 18f); }

		const string HelpPlans = "A world plan is a list of rules: which custom islands a world gets, when they come and where. Each rule brings one island.\n\n" +
			"Plan ▼: the plan to edit. New...: an empty plan. Copy...: this plan under a new name (a good way to start from a sample plan). " +
			"Delete: moves the plan file to Mods\\DynamicIslands\\deleted\\plans, where you can get it back - worlds that use it keep their own copy.";
		const string HelpTemplates = "Adds a ready-made set of rules to this plan: an island-hopping trail, a story chain of your islands, flying islands, a quest reward, " +
			"your island in Raft's story... They are added to the rules you have; change them afterwards like any rule.";
		const string HelpRandom = "On: islands from your spawn pool (spawnpool.txt) also turn up by chance while sailing, between the plan's islands.\n\n" +
			"Off: the world gets only the islands its rules bring. Best for a story.";
		const string HelpStory = "Raft's own story: Radio Tower, Vasagatan ... Utopia, each found by tuning the Receiver to the frequency on the island before.\n\n" +
			"On: it stays in worlds with this plan, next to your islands. Off: only your plan's islands - a new adventure.\n\n" +
			"Click a story island's name to leave it out: the note before it then leads to the one after. A rule's STORY part can put one of your islands into this chain.";
		const string HelpDescription = "One line about the plan. Players see it in the New Game box when they choose the plan (Custom Islands plan).";
		const string HelpMap = "A rough sketch from above of where the rules put their islands: the raft starts in the middle and sails up (north). " +
			"In a world the places depend on where the raft is when a rule fires.\n\nUnder it, Check lists what can't work (red) and tips about Raft's story (blue).";
		const string HelpCheck = "Check goes through the plan the way a world will play it, and looks inside the islands: can each island's quest be finished " +
			"(is there a zone, note, chest or creatures for every step)? Do the zones and signals the rules wait for exist? Do rules wait for each other in a circle, " +
			"or for an island no rule brings? A report opens: problems (the rule can't work), warnings (it may not work as you mean) and tips, each with how to fix it. " +
			"Afterwards each card shows its own problems as you edit. Save checks too.\n\n" +
			"Check can't play the quests: create a world with the plan and try it (F10 → WorldPlan shows which rules have fired).";
		const string HelpShare = "Export...: a pack (.zip) of this plan with every island it needs, to send to friends or to share in the island library (it saves the plan first).\n\n" +
			"Import...: installs a pack someone made (plans and islands), or removes what you installed.";
		const string HelpId = "The rule's name, e.g. camp. Other rules use it to mean the island this rule brought: \"quest done at camp\", \"near camp\". " +
			"Each rule needs its own id (no | or :).";
		const string HelpIdIsland = "The rule's name. Other rules of this island can wait for it (\"after rule\") or put an island near the one it brought.";
		const string HelpWhen = "WHEN the island comes - choose from the list:\n" +
			"• When the world starts\n• After sailing a distance / On a day: type the number\n" +
			"• When a quest is done: an island's quest is finished (the island must have a quest)\n• When quest steps are done: that many steps of it\n" +
			"• When a trigger zone fires: a player walks into a zone (its name) on an island\n• When players reach an island: a player first comes to it\n" +
			"• Right after another rule: straight after that rule's island came\n• When a signal is sent: an object's \"send a signal\" action on an island\n\n" +
			"\"island\": the id of the rule that brought it, or the island's name. The ▾ after a field lists them: the plan's rules, your saved islands (with their quests), and an island's zones, signals or quest steps.";
		const string HelpWhenIsland = "WHEN the island comes - choose from the list: when this island's quest is done, when some of its quest steps are done, when one of its zones fires, " +
			"when players first reach it, when it sends a signal... \"self\" means this island.";
		const string HelpWhat = "BRING: which island comes - choose from the list:\n" +
			"• A new island of a map type: the mod makes it new for each world - a camp, volcano, wreck, sky island... (▾ lists them). No file needed, so it always works when shared\n" +
			"• One of my saved islands: an island you built (▾ lists them)\n" +
			"• One island from a list: island names with commas between them; one is picked (▾ adds one)\n• A random island (spawn pool): any island of your spawn pool";
		const string HelpWhere = "WHERE it comes - choose from the list:\n" +
			"• Ahead of the raft: that many metres ahead\n" +
			"• Near an island: that far from it (centre to centre), in a direction (Any way = wherever there's room), \"of\" which island - empty means the island where the WHEN happened\n" +
			"• On the Receiver: it gets its own frequency, and comes when a player tunes Raft's Receiver to it\n" +
			"• By chance while sailing: it comes up ahead some time later\n\nThe ▾ after \"of\" lists the plan's rules and your saved islands.";
		const string HelpWhereIsland = "WHERE it comes: that many metres ahead of the raft, or near an island (\"self\" = this island) in a direction - Any way means wherever there's room.";
		const string HelpTell = "Message: shown to every player when the island appears, with how far and which way it is (e.g. \"Smoke rises from a small island ahead.\").\n\n" +
			"Receiver name: the island's name on its dot on Raft's Receiver. Both are optional.";
		const string HelpStoryPlace = "Optional: makes this island part of the main story (Raft's notebook): first, beside Raft's story, after a story island (or one of your story islands), or in place of one. A side quest goes in the journal (J) instead.\n\n" +
			"\"done when\" says when it counts as done, which unlocks the next island of the story. Leave it on \"Side quest (not in the story)\" for an ordinary rule. The ▾ after its field lists the zones, signals or quest steps of the island it brings.";

		/// <summary>The Help button: how to approach the window, step by step, and the guide.</summary>
		void ShowHelp()
		{
			Keep();
			if (islandMode)
			{
				InfoWindow.Open("Island rules",
					"An island's own rules bring more islands into any world this island turns up in: \"when my quest is done, bring a treasure island 600 m north of me\". " +
					"A chain of shared islands becomes a story on its own.\n\n" +
					"1.  <b>+ Add a rule</b>. It starts as \"when this island's quest is done, bring a new random island 600 m from it\".\n" +
					"2.  Choose <b>WHEN</b>, <b>BRING</b> (a saved island or a map type; ▾ lists them) and <b>WHERE</b> (how far, which way) from their ▼ lists. \"self\" means this island.\n" +
					"3.  Write a message players see when the island appears, and its name on the Receiver.\n" +
					"4.  <b>Check</b>, then <b>Save</b>, then save the island (Ctrl+S): the rules are kept in the island file.\n\n" +
					"Hover any <b>?</b> for help with that part. To decide a whole world's story in one place, use World plans instead (the guide, section 7).",
					new InfoWindow.Choice("Guide: islands that bring islands", () => HelpLinks.OpenGuideSection("64-islands-that-bring-islands"), "Section 6.4 of the guide, online"),
					new InfoWindow.Choice("Open the guide (PDF)", HelpLinks.OpenGuide, "The whole illustrated guide"),
					new InfoWindow.Choice("Close", InfoWindow.Close, "Back to the rules (Esc)", true));
				return;
			}
			InfoWindow.Open("How to make a world plan",
				"A world plan decides which custom islands a world gets, <b>when</b> they come and <b>where</b>. Each card on the left is one rule, and each rule brings one island:\n" +
				"<i>When something happens → bring this island → put it there → tell the players.</i>\n\n" +
				"1.  <b>New...</b> and give the plan a name - or pick a sample plan (Adventure, Island hopping...) and <b>Copy...</b> it to start from it.\n" +
				"2.  Write a <b>description</b>: players see it when they choose the plan.\n" +
				"3.  <b>Random islands while sailing</b>: off for a story (only your islands), on to have islands by chance as well.\n" +
				"4.  <b>+ Add a rule</b> for the first island. Give it an <b>id</b> (e.g. camp), leave <b>WHEN</b> on \"When the world starts\", <b>BRING</b> \"A new island of a map type\" and pick one with ▾ (e.g. Old camp), " +
				"<b>WHERE</b> \"Ahead of the raft\" 350 m, and write a message under <b>TELL</b>.\n" +
				"5.  <b>+ Add a rule</b> again for the next island. A new rule already says \"When a quest is done\" at the rule before, \"Near an island\" - that one: that makes a story from island to island. " +
				"Pick the island and the direction.\n" +
				"6.  Read the sentence at the top of each card: it says in plain words what the rule will do.\n" +
				"7.  <b>Check</b> (√ Every rule can work, or what to fix), then <b>Save</b>.\n" +
				"8.  Main menu → <b>NEW WORLD</b> → click <b>Custom Islands plan</b> until it shows your plan → Create. Play it: F10 → <i>WorldPlan</i> shows which rules have fired.\n\n" +
				"Hover any <b>?</b> in this window for help with that part. The guide walks through a whole plan with pictures (section 7.2), and lists every choice a rule has.",
				new InfoWindow.Choice("Guide: world plans step by step", () => HelpLinks.OpenGuideSection("72-your-first-world-plan-step-by-step"), "Section 7.2 of the guide, online"),
				new InfoWindow.Choice("Open the guide (PDF)", HelpLinks.OpenGuide, "The whole illustrated guide"),
				new InfoWindow.Choice("Close", InfoWindow.Close, "Back to the plan (Esc)", true));
		}

		#endregion

		#region Actions

		/// <summary>Makes sure typed text is in the plan (fields also apply on end edit; this catches the one still focused).</summary>
		void Keep()
		{
			plan.Description = descriptionField.text.Trim();
			foreach (InputField f in fields.Where(f => f != null && f.isFocused).ToList()) f.onEndEdit.Invoke(f.text);
		}

		void AddRule()
		{
			Keep();
			var r = new IntroRule { Id = NewRuleId(plan), What = "type", WhatArg = "random" };
			if (islandMode) { r.When = "quest"; r.WhenRef = IntroRule.Self; r.Where = "near"; r.WhereRef = IntroRule.Self; r.Distance = 600f; }
			else if (plan.Rules.Count > 0) { r.When = "quest"; r.WhenRef = plan.Rules[plan.Rules.Count - 1].Id; r.Where = "near"; r.WhereRef = ""; r.Distance = 600f; }
			while (plan.Rules.Any(x => x.Id.Equals(r.Id, StringComparison.OrdinalIgnoreCase))) r.Id += "b";
			plan.Rules.Add(r);
			ShowRules();
		}

		void Save()
		{
			Keep();
			List<PlanChecker.Finding> found = PlanChecker.Check(plan, islandMode, false, ownFacts);
			string problems = found.Any(f => f.Level == PlanChecker.Level.Problem) ? "problems" : "";
			if (islandMode)
			{
				IslandSettingsUndo.Change(() => WorldDirector.SetRulesInProps(DynamicIslands.currentIslandProps, plan.Rules));
				EditorUI.RefreshIsland();
				DynamicIslands.Notify("The island's " + plan.Rules.Count + " rule(s) are kept with it (save the island, Ctrl+S)" + (problems.Length > 0 ? " - Check found problems" : ""), problems.Length > 0);
			}
			else
			{
				try { plan.Save(); }
				catch (Exception e) { DynamicIslands.Notify("Could not save the plan: " + e.Message, true); return; }
				DynamicIslands.Notify("Saved plan '" + plan.Name + "' (" + plan.Rules.Count + " rules)" + (problems.Length > 0 ? " - Check found problems" : ""), problems.Length > 0);
			}
			RunCheck(true, problems.Length > 0);
		}

		/// <summary>Export: the plan saved first (what's shared is what's saved), then the Share window.</summary>
		void ExportPlan()
		{
			if (islandMode) return;
			Keep();
			try { plan.Save(); }
			catch (Exception e) { DynamicIslands.Notify("Could not save the plan: " + e.Message, true); return; }
			string name = plan.Name;
			Close();
			LibraryExportWindow.OpenPlan(name);
		}

		void PickPlan()
		{
			Keep();
			ChoiceWindow.Open("World plan", WorldPlan.All().Where(n => !WorldPlan.IsBuiltIn(n)).Select(n => { WorldPlan p = WorldPlan.Load(n); return new ChoiceWindow.Choice(n, n, p != null ? p.Rules.Count + " rules. " + p.Description : ""); }),
				n => { WorldPlan p = WorldPlan.Load(n); if (p != null) Show(p); });
		}

		void DeletePlan()
		{
			string path = WorldPlan.PathFor(plan.Name);
			// (moved to deleted\plans, where it can be got back, like islands, groups and stamps - it was deleted for good, AU41)
			try { if (File.Exists(path)) PiecesFiles.MoveToDeleted(path, "plans"); }
			catch (Exception e) { DynamicIslands.Notify(SafeFile.InUse(e) ? "The plan '" + plan.Name + "' is in use by another program - close it there and try again" : "Could not delete the plan '" + plan.Name + "': " + e.Message, true); return; }
			DynamicIslands.Notify("Deleted plan '" + plan.Name + "' (kept in " + IslandFilesWindow.DeletedFolderName + " until you remove it there)");
			lastPlan = null;
			Open();
		}

		static WorldPlan NewPlan(string name)
		{
			var p = new WorldPlan { Name = name, Description = "", Random = true };
			p.Save();
			return p;
		}

		static void AskName(string title, string message, string text, Action<string> ok)
		{
			TextPromptWindow.Open(title, message, text, n =>
			{
				if (WorldPlan.IsBuiltIn(n) || File.Exists(WorldPlan.PathFor(n))) { DynamicIslands.Notify("There is a plan called '" + n + "' already", true); return; }
				ok(n);
			});
		}

		void PickTemplate()
		{
			Keep();
			ChoiceWindow.Open("Add rules from a template", WorldPlanTemplates.All.Where(x => !islandMode || !WorldPlanTemplates.Get(x.Key).HasStory).Select(t => new ChoiceWindow.Choice(t.Key, t.Key, t.Value.Description)), key =>
			{
				WorldPlan t = WorldPlanTemplates.Get(key);
				if (t == null) return;
				// Ids already used get a suffix, and references inside the template follow them
				var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (IntroRule r in t.Rules)
				{
					string id = r.Id;
					while (plan.Rules.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) id += "b";
					renamed[r.Id] = id;
				}
				foreach (IntroRule r in t.Rules)
				{
					IntroRule c = r.Clone();
					c.Id = renamed[r.Id];
					string to;
					if (renamed.TryGetValue(c.WhenRef, out to)) c.WhenRef = to;
					if (renamed.TryGetValue(c.WhereRef, out to)) c.WhereRef = to;
					if (islandMode && c.When == "start") { c.When = "quest"; c.WhenRef = IntroRule.Self; }
					plan.Rules.Add(c);
				}
				if (!islandMode && plan.Description.Length == 0) { plan.Description = t.Description; descriptionField.text = t.Description; }
				// (a template's story: Raft's story off, islands left out; story places follow renamed ids)
				if (!islandMode)
				{
					if (!t.RaftStory) plan.RaftStory = false;
					plan.LeaveOut.UnionWith(t.LeaveOut);
					foreach (IntroRule r in plan.Rules.Skip(plan.Rules.Count - t.Rules.Count))
					{
						string to;
						if (r.StoryPlace.StartsWith("after:") && renamed.TryGetValue(r.StoryPlace.Substring(6), out to)) r.StoryPlace = "after:" + to;
					}
					ShowRandom();
				}
				ShowRules();
			});
		}

		#endregion

		#region Check and map

		/// <summary>What the last check found (null until Check was clicked; then kept up to date as the plan changes).</summary>
		List<PlanChecker.Finding> findings;
		bool inCheck;
		/// <summary>Island rules: what the island being edited has (read when Check is clicked).</summary>
		PlanChecker.Facts ownFacts;

		/// <summary>
		/// Check: goes through the plan the way a world will play it - the rules, and inside the islands they name (their
		/// quests step by step, their zones and signals; a sample of each map type) - and shows the report: problems,
		/// warnings, tips, each with why and how to fix it. showReport: open the report (Check always; Save when there are
		/// problems).
		/// </summary>
		void Check() { RunCheck(true, true); }

		void RunCheck(bool deep, bool showReport)
		{
			Keep();
			if (deep && islandMode)
			{
				try { ownFacts = PlanChecker.FromFile(DynamicIslands.CaptureIsland(DynamicIslands.currentIslandName), DynamicIslands.currentIslandName, false); }
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Check: reading the island in the editor: " + e.Message); }
			}
			findings = PlanChecker.Check(plan, islandMode, deep, ownFacts);
			ShowFindings();
			inCheck = true; // (the cards show these findings: no quick check again right after)
			try { ShowRules(); } finally { inCheck = false; }
			if (showReport) PlanCheckWindow.Open(plan.Name, findings, () => RunCheck(true, true), ShowRule);
		}

		/// <summary>The summary beside the map (and LastCheck for tests).</summary>
		void ShowFindings()
		{
			int problems = findings.Count(f => f.Level == PlanChecker.Level.Problem), warnings = findings.Count(f => f.Level == PlanChecker.Level.Warning), tips = findings.Count(f => f.Level == PlanChecker.Level.Tip);
			var lines = new List<string>();
			if (problems == 0 && warnings == 0) lines.Add("<color=#8fdc8f>\u221A Every rule can work.</color>" + (tips > 0 ? " " + tips + " tip" + (tips == 1 ? "" : "s") + "." : ""));
			else lines.Add("<color=#ffb4aa>" + problems + " problem" + (problems == 1 ? "" : "s") + "</color>, <color=#ffd98a>" + warnings + " warning" + (warnings == 1 ? "" : "s") + "</color>, " + tips + " tip" + (tips == 1 ? "" : "s") + ":");
			foreach (PlanChecker.Finding f in findings.Where(f => f.Level != PlanChecker.Level.Tip).Take(2))
				lines.Add("<color=#" + ColorUtility.ToHtmlStringRGB(PlanCheckWindow.ColorOf(f.Level)) + ">\u2022 " + Short(f.Text, 90) + "</color>");
			lines.Add("<i>Check shows the whole report.</i>");
			problemsText.text = string.Join("\n", lines.ToArray());
			LastCheck = problemsText.text + "\n" + string.Join("\n", findings.Select(f => f.Text).ToArray());
		}

		static string Short(string s, int max) { return s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "\u2026"; }

		/// <summary>The report's "Show rule n": scrolls the list to that rule's card.</summary>
		void ShowRule(int index)
		{
			Canvas.ForceUpdateCanvases();
			Transform card = rulesList.Cast<Transform>().Where(t => t.name == "Rule").ElementAtOrDefault(index);
			ScrollRect scroll = rulesList.GetComponentInParent<ScrollRect>();
			if (card == null || scroll == null) return;
			float content = rulesList.rect.height, view = scroll.viewport.rect.height;
			if (content <= view) return;
			float top = -((RectTransform)card).anchoredPosition.y - ((RectTransform)card).rect.height * (1f - ((RectTransform)card).pivot.y);
			scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - top / (content - view));
		}

		/// <summary>A card's findings, under its sentence (after the first Check).</summary>
		void CardFindings(RectTransform card, int index, ref float height)
		{
			if (findings == null) return;
			List<PlanChecker.Finding> mine = findings.Where(f => f.Rule == index && f.Level != PlanChecker.Level.Tip).ToList();
			int tips = findings.Count(f => f.Rule == index && f.Level == PlanChecker.Level.Tip);
			if (mine.Count == 0 && tips == 0) return;
			foreach (PlanChecker.Finding f in mine.Take(3))
			{
				Text t = UIKit.Label(card, (f.Level == PlanChecker.Level.Problem ? "\u2716 " : "\u26A0 ") + Short(f.Text.Replace(RuleLabel(index) + " ", ""), 170), 12, PlanCheckWindow.ColorOf(f.Level), TextAnchor.MiddleLeft, FontStyle.Normal, "Finding");
				t.horizontalOverflow = HorizontalWrapMode.Wrap;
				UIKit.Size(t.gameObject, -1, 32);
				height += 32f + 6f;
			}
			if (mine.Count > 3 || tips > 0)
			{
				Text more = UIKit.Label(card, (mine.Count > 3 ? "+ " + (mine.Count - 3) + " more" : "") + (mine.Count > 3 && tips > 0 ? ", " : "") + (tips > 0 ? tips + " tip" + (tips == 1 ? "" : "s") : "") + " - Check shows them", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "FindingMore");
				UIKit.Size(more.gameObject, -1, 16);
				height += 16f + 6f;
			}
		}

		string RuleLabel(int i) { IntroRule r = plan.Rules[i]; return "Rule " + (i + 1) + (r.Id.Length > 0 ? " '" + r.Id + "'" : ""); }

		/// <summary>A rough top-down sketch: the raft in the middle, islands where their rules would put them.</summary>
		void DrawMap()
		{
			foreach (Transform child in map) Destroy(child.gameObject);
			labelled.Clear();
			var spots = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
			var dots = new List<KeyValuePair<string, Vector2>>();
			Vector2 raft = Vector2.zero, sail = Vector2.up; // the raft sails north in the sketch
			float along = 0f;
			foreach (IntroRule r in plan.Rules)
			{
				Vector2 at;
				if (r.Where == "near")
				{
					Vector2 from;
					string refId = r.WhereRef.Length == 0 || r.WhereRef == IntroRule.Self ? r.WhenRef : r.WhereRef;
					if (!spots.TryGetValue(refId ?? "", out from)) from = islandMode ? Vector2.zero : raft + sail * along;
					float a = IntroRule.DirectionAngle(r.Direction);
					if (float.IsNaN(a)) a = 60f + dots.Count * 97f;
					at = from + new Vector2(Mathf.Sin(a * Mathf.Deg2Rad), Mathf.Cos(a * Mathf.Deg2Rad)) * r.Distance;
				}
				else
				{
					if (r.When == "km") along = Mathf.Max(along, ParseKm(r.WhenArg));
					at = raft + sail * (along + r.Distance);
					along += 200f;
				}
				spots[r.Id] = at;
				dots.Add(new KeyValuePair<string, Vector2>(r.Label.Length > 0 ? r.Label : r.Id, at));
			}
			float extent = Mathf.Max(800f, dots.Count == 0 ? 0f : dots.Max(d => Mathf.Max(Mathf.Abs(d.Value.x), Mathf.Abs(d.Value.y)))) * 1.15f;
			float half = 125f;
			Dot(islandMode ? "this island" : "raft", Vector2.zero, islandMode ? UIKit.Good : Color.white, half, extent);
			foreach (var d in dots) Dot(d.Key, d.Value, UIKit.Accent, half, extent);
		}

		static float ParseKm(string s)
		{
			float f;
			return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f * 1000f : 0f;
		}

		// Map spots that have a name beside them: a dot close to one of them goes without (names written over each other)
		readonly List<Vector2> labelled = new List<Vector2>();

		void Dot(string label, Vector2 world, Color color, float half, float extent)
		{
			Vector2 p = world / extent * half;
			RectTransform r = UIKit.Rect("Dot", map);
			UIKit.Anchor(r, new Vector2(0.5f, 0.5f), p, new Vector2(10, 10));
			Image img = r.gameObject.AddComponent<Image>();
			img.sprite = UIKit.Rounded(5); img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
			r.SetAsFirstSibling(); // dots under every name
			if (labelled.Any(q => Mathf.Abs(q.x - p.x) < 80f && Mathf.Abs(q.y - p.y) < 13f)) return;
			labelled.Add(p);
			Text t = UIKit.Label(map, label, 10, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Name");
			UIKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), p + new Vector2(52f, 0f), new Vector2(90, 14));
			UIKit.Fit(t, 7);
		}

		#endregion

		/// <summary>
		/// Writes each sample plan once (listed in plans\samples.txt): they can be changed or deleted, and deleted ones
		/// don't come back; samples added by a later version of the mod are written then.
		/// </summary>
		public static void EnsureSamples()
		{
			try
			{
				Directory.CreateDirectory(WorldPlan.Folder);
				string list = Path.Combine(WorldPlan.Folder, "samples.txt");
				var written = new HashSet<string>(File.Exists(list) ? File.ReadAllLines(list).Select(l => l.Trim()) : new string[0], StringComparer.OrdinalIgnoreCase);
				bool changed = false;
				foreach (var t in WorldPlanTemplates.All.Where(t => t.Value.Sample && !written.Contains(t.Key)))
				{
					if (!File.Exists(WorldPlan.PathFor(t.Key)))
					{
						WorldPlan p = WorldPlanTemplates.Get(t.Key);
						p.Name = t.Key;
						p.Save();
					}
					written.Add(t.Key);
					changed = true;
				}
				if (changed) SafeFile.WriteAllLines(list, new[] { "# Sample world plans the mod has written once (delete a line to get that sample back)" }.Concat(written.OrderBy(n => n)).ToArray());
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not write the sample plans: " + e.Message); }
		}
	}

	/// <summary>Ready-made sets of rules ("Templates..." in the plan editor; the samples are also written as plans).</summary>
	public static class WorldPlanTemplates
	{
		public class Template
		{
			public string Description, Text;
			/// <summary>Also written as a plan file the first time.</summary>
			public bool Sample;
		}

		public static readonly List<KeyValuePair<string, Template>> All = new List<KeyValuePair<string, Template>>
		{
			new KeyValuePair<string, Template>("Island hopping", new Template { Sample = true,
				Description = "An old camp near the start; each island you reach shows the way to the next, further out",
				Text = "random = on\n" +
					"rule = camp | type:camp | start | ahead:350 | Smoke rises from a small island ahead. | Old camp\n" +
					"rule = treasure | type:treasure | visit:camp | near:camp:900:any | Birds fly off towards another island... | Treasure\n" +
					"rule = islets | type:archipelago | visit:treasure | near:treasure:1100:any | From the top you can see a group of islets. | Islets\n" +
					"rule = volcano | type:volcano | visit:islets | near:islets:1400:any | Smoke rises far away. | Volcano\n" }),
			new KeyValuePair<string, Template>("Adventure", new Template { Sample = true,
				Description = "A story: each quest leads to the next island (camp, islets, a beast, a treasure), with wrecks and a sunken island on the way",
				Text = "random = off\n" +
					"rule = camp | type:camp | start | ahead:350 | Smoke rises from a small island ahead. | Old camp\n" +
					"rule = islets | type:archipelago | quest:camp | near:camp:900:north-east | The notice mentioned islets to the north-east. | Islets\n" +
					"rule = beast | type:boss | quest:islets | near:islets:1000:any | The castaway's note warns of a beast on a plateau... | Plateau\n" +
					"rule = treasure | type:treasure | quest:beast | near:beast:900:any | Among the spoils: a map to a treasure island! | Treasure\n" +
					"rule = wreck | type:wreck | km:2 | ahead:300 | Something floats ahead: a wrecked raft. | Wreck\n" +
					"rule = sunken | type:sunken | km:5 | ahead:350 | The water below looks strangely shallow. Dive! | Sunken\n" }),
			new KeyValuePair<string, Template>("Growing sea", new Template { Sample = true,
				Description = "Random islands while sailing, plus a special island every few km and days",
				Text = "random = on\n" +
					"rule = sandbar | type:sandbar | km:1 | ahead:300 | A tiny island with a chest. | Sandbar\n" +
					"rule = atoll | type:atoll | km:4 | ahead:450 | A ring of land around a lagoon. | Atoll\n" +
					"rule = stacks | type:stacks | day:3 | ahead:400 | Tall rocks rise from the sea. | Sea stacks\n" +
					"rule = swamp | type:swamp | day:6 | ahead:400 | A green mist hangs over the water. | Swamp\n" +
					"rule = spire | type:spire | km:8 | ahead:450 | It's getting colder. | Frozen spire\n" +
					"rule = volcano | type:volcano | day:10 | ahead:450 | The sea smells of sulphur. | Volcano\n" }),
			new KeyValuePair<string, Template>("Receiver adventure", new Template { Sample = true,
				Description = "A new adventure instead of Raft's story: each island is found with the Receiver, and its quest gives the next frequency",
				Text = "random = off\n" +
					"story = off\n" +
					"storyending = The treasure is yours, and the last frequency falls silent. Every island of the adventure is in this book.\\n\\nThe end.\n" +
					"rule = camp | type:camp | start | receiver:600 | A faint signal crackles on the Receiver... | Old camp | first | quest\n" +
					"rule = islets | type:archipelago | start | receiver:800 | The camp's radio log names another frequency. | Islets | after:camp | quest\n" +
					"rule = beast | type:boss | start | receiver:900 | A distress call from a plateau... | Plateau | after:islets | quest\n" +
					"rule = treasure | type:treasure | start | receiver:800 | Among the spoils: the frequency of a treasure island! | Treasure | after:beast | quest\n" }),
			new KeyValuePair<string, Template>("Detour in Raft's story", new Template {
				Description = "Raft's story as usual, with one of your islands after Vasagatan: its frequency comes with Vasagatan's note, and its quest gives Balboa's",
				Text = "rule = detour | type:camp | start | receiver:700 | A second signal hides under Vasagatan's... | Old camp | after:Vasagatan | quest\n" }),
			new KeyValuePair<string, Template>("Balboa replaced", new Template {
				Description = "Raft's story with an island of yours in Balboa's place: Vasagatan's note leads to it, and reaching it gives Caravan Town's frequency",
				Text = "rule = forest | type:forest | start | receiver:800 | A new signal, from a forest island. | Forest island | instead:Balboa | visit\n" }),
			new KeyValuePair<string, Template>("Sky chain", new Template {
				Description = "Flying islands, each appearing when players reach the one before",
				Text = "rule = sky1 | type:sky | start | ahead:300 | An island floats in the sky! | Sky 1\n" +
					"rule = sky2 | type:sky | visit:sky1 | near:sky1:350:any | Another one, higher up. | Sky 2\n" +
					"rule = sky3 | type:sky | visit:sky2 | near:sky2:350:any | And another... | Sky 3\n" }),
			new KeyValuePair<string, Template>("Story chain", new Template {
				Description = "Each island's quest brings the next one (replace the islands with your own; each needs a quest)",
				Text = "random = off\n" +
					"rule = chapter1 | island:myisland | start | ahead:300 | A strange island appears. | Chapter 1\n" +
					"rule = chapter2 | island:myisland | quest:chapter1 | near:chapter1:700:north | The story goes on to the north... | Chapter 2\n" +
					"rule = chapter3 | island:myisland | quest:chapter2 | near:chapter2:800:east | One more island to the east. | Chapter 3\n" }),
			new KeyValuePair<string, Template>("Quest reward island", new Template {
				Description = "Finishing an island's quest brings a new island near it",
				Text = "rule = reward | type:random | quest:self | near:self:600:any | Your reward: a new island! | Reward\n" }),
		};

		public static WorldPlan Get(string name)
		{
			Template t = All.Where(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).FirstOrDefault();
			if (t == null) return null;
			WorldPlan p = WorldPlan.Parse(name, t.Text);
			p.Description = t.Description;
			return p;
		}
	}
}
