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
		Button randomButton, planButton, newButton, copyButton, deleteButton;
		InputField descriptionField;
		RectTransform planRow, settingsRow, rulesList, map;
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
			descriptionField.gameObject.SetActive(!islandMode);
			planNameText.text = islandMode ? "Islands that '" + p.Name + "' brings into a world (saved with the island; \"self\" = this island)" : "";
			UIKit.LabelOf(planButton).text = "Plan: " + p.Name + "  \u25BC";
			descriptionField.text = p.Description;
			ShowRandom();
			ShowRules();
			problemsText.text = "";
		}

		void ShowRandom()
		{
			UIKit.LabelOf(randomButton).text = "Random islands while sailing: " + (plan.Random ? "on" : "off");
			UIKit.SetActive(randomButton, plan.Random);
		}

		void Update()
		{
			if (ChoiceWindow.IsOpen || TextPromptWindow.IsOpen) return;
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
			planNameText = UIKit.Label(panel, "", 13, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "IslandNote");

			planRow = UIKit.Row(panel, 30f, 6f, "PlanRow");
			planButton = UIKit.Button(planRow, "Plan", PickPlan, "Choose the plan to edit", 320, 30f, 13);
			newButton = UIKit.Button(planRow, "New...", () => AskName("New plan", "A new, empty plan", "", n => Show(NewPlan(n))), "Start a new plan", 90, 30f, 12);
			copyButton = UIKit.Button(planRow, "Copy...", () => AskName("Copy plan", "A copy of '" + plan.Name + "'", plan.Name + " copy", n => { Keep(); var c = WorldPlan.Parse(n, plan.ToText()); c.Save(); Show(c); }), "Save a copy under another name", 90, 30f, 12);
			deleteButton = UIKit.Button(planRow, "Delete", DeletePlan, "Delete this plan (worlds that use it keep their islands; their rules stop)", 90, 30f, 12);
			UIKit.LabelOf(deleteButton).color = new Color(1f, 0.6f, 0.55f);
			UIKit.Size(UIKit.Label(planRow, "", 12, UIKit.TextMuted).gameObject, -1, -1, 1);
			UIKit.Button(planRow, "Templates...", PickTemplate, "Add a ready-made set of rules (story chain, treasure hunt...) to this plan", 120, 30f, 12);

			settingsRow = UIKit.Row(panel, 28f, 6f, "Settings");
			randomButton = UIKit.Button(settingsRow, "", () => { plan.Random = !plan.Random; ShowRandom(); }, "Also let islands from the spawn pool (spawnpool.txt) appear by chance while sailing, as in the \"Random islands\" plan", 330, 28f, 12);
			descriptionField = UIKit.Field(panel, "Description, shown when choosing the plan (e.g. A story across five islands)", "", 28f, "Shown in the New Game box");
			descriptionField.characterLimit = 120;
			fields.Add(descriptionField);

			RectTransform body = UIKit.Row(panel, 470f, 10f, "Body");
			RectTransform rulesBox = UIKit.Rect("Rules", body);
			UIKit.Size(rulesBox.gameObject, -1, 470, 1);
			ScrollRect scroll;
			rulesList = UIKit.ScrollList(rulesBox, out scroll, 6f);
			UIKit.Stretch((RectTransform)scroll.transform);
			RectTransform side = UIKit.Rect("Side", body);
			UIKit.Size(side.gameObject, 250, 470);
			UIKit.Vertical(side.gameObject, 6f, new RectOffset(0, 0, 0, 0));
			UIKit.Label(side, "WHERE ISLANDS GO (ROUGHLY)", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
			map = UIKit.Rect("Map", side);
			UIKit.Size(map.gameObject, 250, 250);
			UIKit.Background(map.gameObject, new Color(0.08f, 0.2f, 0.3f, 1f), 6);
			problemsText = UIKit.Label(side, "", 12, UIKit.TextColor, TextAnchor.UpperLeft);
			UIKit.Size(problemsText.gameObject, 250, 190);
			problemsText.verticalOverflow = VerticalWrapMode.Truncate;

			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			UIKit.Button(buttons, "+ Add a rule", AddRule, "Another rule: when something happens, bring an island", 140, 34f, 13);
			UIKit.Button(buttons, "Check", () => { Keep(); Check(); }, "Look for rules that can't work (missing islands, names that point nowhere) and draw the map", 110, 34f, 13);
			UIKit.Size(UIKit.Label(buttons, "", 12, UIKit.TextMuted).gameObject, -1, -1, 1);
			Button save = UIKit.Button(buttons, "Save", Save, "Keep the changes", 110, 34);
			UIKit.SetActive(save, true);
			UIKit.Button(buttons, "Close", Close, "Close without saving", 110, 34);
		}

		static readonly Dictionary<string, string> WhenLabels = new Dictionary<string, string>
		{
			{ "start", "the world starts" }, { "km", "after sailing (km)" }, { "day", "on day" }, { "quest", "quest done at" }, { "step", "quest step done at" },
			{ "zone", "zone fires at" }, { "visit", "players reach" }, { "rule", "after rule" }, { "signal", "signal sent at" },
		};
		static readonly Dictionary<string, string> WhatLabels = new Dictionary<string, string>
		{
			{ "island", "saved island" }, { "type", "new map type" }, { "pool", "from spawn pool" }, { "oneof", "one of these" },
		};

		void ShowRules()
		{
			foreach (Transform child in rulesList) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
			fields.RemoveAll(f => f == null || f.transform.IsChildOf(rulesList));
			for (int i = 0; i < plan.Rules.Count; i++) RuleCard(i);
			if (plan.Rules.Count == 0) UIKit.Label(rulesList, "<i>No rules yet. \"+ Add a rule\", or Templates... for a ready-made set.</i>", 13, UIKit.TextMuted);
			DrawMap();
		}

		void RuleCard(int index)
		{
			IntroRule r = plan.Rules[index];
			RectTransform card = UIKit.Group(rulesList, "", "Rule");
			UIKit.Size(card.gameObject, -1, 102);

			// When ... bring ...
			RectTransform a = UIKit.Row(card, 26f, 4f, "When");
			UIKit.Size(UIKit.Label(a, (index + 1) + ".", 13, UIKit.TextMuted, TextAnchor.MiddleRight).gameObject, 20);
			InputField id = SmallField(a, "id", r.Id, 80, "The rule's name: other rules refer to the island it brings by it (e.g. camp)", v => r.Id = v.Replace("|", "").Replace(":", "").Trim());
			UIKit.Size(UIKit.Label(a, "When", 12, UIKit.TextMuted, TextAnchor.MiddleRight).gameObject, 38);
			Cycle(a, WhenLabels[r.When], 130, "Click to change what the rule waits for", () => { r.When = Next(IntroRule.WhenKinds, r.When); ShowRules(); });
			bool needsRef = r.When == "quest" || r.When == "step" || r.When == "zone" || r.When == "visit" || r.When == "rule" || r.When == "signal";
			bool needsArg = r.When == "km" || r.When == "day" || r.When == "step" || r.When == "zone" || r.When == "signal";
			if (needsRef) SmallField(a, r.When == "rule" ? "rule id" : islandMode ? "self" : "rule id / island", r.WhenRef, 110,
				r.When == "rule" ? "The id of the rule to wait for" : "Which island: the id of the rule that brought it, or an island name" + (islandMode ? " (self = this island)" : ""), v => r.WhenRef = v.Trim());
			if (needsArg) SmallField(a, r.When == "signal" ? "signal name" : r.When == "zone" ? "zone name" : r.When == "step" ? "steps" : r.When, r.WhenArg, r.When == "zone" ? 100 : 50,
				r.When == "zone" ? "The trigger zone's name on that island" : r.When == "step" ? "How many steps of the quest are done" : r.When == "km" ? "Km sailed in this world" : "In-game day", v => r.WhenArg = v.Trim());
			UIKit.Size(UIKit.Label(a, "bring", 12, UIKit.TextMuted, TextAnchor.MiddleRight).gameObject, 36);
			Cycle(a, WhatLabels[r.What], 120, "Click to change: a saved island, a new island of a map type, one from the spawn pool, or one of a list", () =>
			{
				r.What = Next(IntroRule.WhatKinds, r.What);
				r.WhatArg = r.What == "type" ? "random" : "";
				ShowRules();
			});
			if (r.What != "pool")
			{
				InputField what = SmallField(a, r.What == "oneof" ? "island, island, ..." : r.What == "type" ? "map type" : "island name", r.WhatArg, -1,
					r.What == "oneof" ? "Island names, separated by commas: one is picked (ones not in the world yet first)" : "Which one (\u2026 to choose)", v => r.WhatArg = v.Trim());
				UIKit.Button(a, "\u2026", () =>
				{
					Keep();
					Action<string> set = v => { r.WhatArg = r.What == "oneof" && r.WhatArg.Trim().Length > 0 ? r.WhatArg.Trim() + ", " + v : v; ShowRules(); };
					if (r.What == "type") ChoiceWindow.Open("Map type", ChoiceWindow.Types(), set);
					else ChoiceWindow.Open("Island", ChoiceWindow.Islands(), set);
				}, "Choose from a list", 28, 26f, 12);
			}
			else UIKit.Label(a, "(spawnpool.txt)", 12, UIKit.TextMuted);

			// ... where, message, label
			RectTransform b = UIKit.Row(card, 26f, 4f, "Where");
			UIKit.Size(UIKit.Label(b, "", 12).gameObject, 20);
			Cycle(b, r.Where == "ahead" ? "ahead of the raft" : "near an island", 130, "Click to change: ahead of the raft, or near an island (at a distance and direction)", () =>
			{
				r.Where = r.Where == "ahead" ? "near" : "ahead";
				if (r.Where == "near") { r.WhereRef = islandMode ? IntroRule.Self : ""; r.Distance = Mathf.Max(r.Distance, 600f); }
				ShowRules();
			});
			SmallField(b, "300", r.Distance.ToString("0"), 56, "Metres (from the raft, or centre to centre from the island; at least clear of both)", v =>
			{
				float d;
				if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Distance = Mathf.Clamp(d, 50f, 5000f);
			}).contentType = InputField.ContentType.IntegerNumber;
			UIKit.Size(UIKit.Label(b, "m", 12, UIKit.TextMuted).gameObject, 14);
			if (r.Where == "near")
			{
				Cycle(b, r.Direction == "any" ? "any way" : r.Direction, 90, "Which way from the island (any = wherever there's room)", () => { r.Direction = Next(IntroRule.Directions, r.Direction); ShowRules(); });
				UIKit.Size(UIKit.Label(b, "of", 12, UIKit.TextMuted, TextAnchor.MiddleCenter).gameObject, 18);
				SmallField(b, islandMode ? "self" : "where it happened", r.WhereRef == IntroRule.Self && !islandMode ? "" : r.WhereRef, 110,
					"Which island: the id of the rule that brought it, or an island name. Empty = the island where the rule's event happened" + (islandMode ? "; self = this island" : ""), v => r.WhereRef = v.Trim());
			}
			SmallField(b, "Message to every player (optional)", r.Message, -1, "Shown when the island appears, with how far and which way it is", v => r.Message = v.Trim()).characterLimit = 160;
			SmallField(b, "Receiver name", r.Label, 110, "The island's name on Raft's Receiver (optional)", v => r.Label = v.Trim()).characterLimit = 18;
			UIKit.Button(b, "\u25B2", () => { if (index > 0) { Keep(); plan.Rules.Reverse(index - 1, 2); ShowRules(); } }, "Earlier", 26, 26f, 11);
			UIKit.Button(b, "\u25BC", () => { if (index < plan.Rules.Count - 1) { Keep(); plan.Rules.Reverse(index, 2); ShowRules(); } }, "Later", 26, 26f, 11);
			Button del = UIKit.Button(b, "\u00D7", () => { Keep(); plan.Rules.RemoveAt(index); ShowRules(); }, "Remove this rule", 26, 26f, 12);
			UIKit.LabelOf(del).color = new Color(1f, 0.6f, 0.55f);

			Text describe = UIKit.Label(card, r.Describe(), 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "Describe");
			UIKit.Size(describe.gameObject, -1, 16);
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

		void Cycle(Transform row, string label, float width, string hint, Action click)
		{
			UIKit.Button(row, label, () => { Keep(); click(); }, hint, width, 26f, 12);
		}

		static string Next(string[] list, string current)
		{
			return list[(Array.IndexOf(list, current) + 1) % list.Length];
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
			var r = new IntroRule { Id = "rule" + (plan.Rules.Count + 1), What = "type", WhatArg = "random" };
			if (islandMode) { r.When = "quest"; r.WhenRef = IntroRule.Self; r.Where = "near"; r.WhereRef = IntroRule.Self; r.Distance = 600f; }
			else if (plan.Rules.Count > 0) { r.When = "quest"; r.WhenRef = plan.Rules[plan.Rules.Count - 1].Id; r.Where = "near"; r.WhereRef = ""; r.Distance = 600f; }
			while (plan.Rules.Any(x => x.Id.Equals(r.Id, StringComparison.OrdinalIgnoreCase))) r.Id += "b";
			plan.Rules.Add(r);
			ShowRules();
		}

		void Save()
		{
			Keep();
			string problems = Problems();
			if (islandMode)
			{
				WorldDirector.SetRulesInProps(DynamicIslands.currentIslandProps, plan.Rules);
				EditorUI.RefreshIsland();
				DynamicIslands.Notify("The island's " + plan.Rules.Count + " rule(s) are kept with it (save the island, Ctrl+S)" + (problems.Length > 0 ? " - Check lists problems" : ""), problems.Length > 0);
			}
			else
			{
				try { plan.Save(); }
				catch (Exception e) { DynamicIslands.Notify("Could not save the plan: " + e.Message, true); return; }
				DynamicIslands.Notify("Saved plan '" + plan.Name + "' (" + plan.Rules.Count + " rules)" + (problems.Length > 0 ? " - Check lists problems" : ""), problems.Length > 0);
			}
			Check();
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
			if (File.Exists(path)) File.Delete(path);
			DynamicIslands.Notify("Deleted plan '" + plan.Name + "'");
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
			ChoiceWindow.Open("Add rules from a template", WorldPlanTemplates.All.Select(t => new ChoiceWindow.Choice(t.Key, t.Key, t.Value.Description)), key =>
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
				ShowRules();
			});
		}

		#endregion

		#region Check and map

		/// <summary>What can't work in the plan, one line each ("" if nothing).</summary>
		string Problems()
		{
			var problems = new List<string>();
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var saved = new HashSet<string>(IslandSpawner.ListSavedIslands(), StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < plan.Rules.Count; i++)
			{
				IntroRule r = plan.Rules[i];
				string n = (i + 1) + ". ";
				if (r.Id.Length == 0) problems.Add(n + "has no id");
				else if (!ids.Add(r.Id)) problems.Add(n + "id '" + r.Id + "' is used twice");
				if (r.What == "island" && !saved.Contains(r.WhatArg)) problems.Add(n + (r.WhatArg.Length == 0 ? "no island chosen" : "there's no saved island '" + r.WhatArg + "'"));
				if (r.What == "type" && MapTypes.Get(r.WhatArg) == null) problems.Add(n + "there's no map type '" + r.WhatArg + "'");
				if (r.What == "oneof" && !r.WhatArg.Split(',').Any(x => saved.Contains(x.Trim()))) problems.Add(n + "none of '" + r.WhatArg + "' are saved islands");
				if ((r.When == "km" || r.When == "day") && !float.TryParse(r.WhenArg, NumberStyles.Float, CultureInfo.InvariantCulture, out float _)) problems.Add(n + "needs a number");
				if (r.When == "rule" && !plan.Rules.Any(x => x.Id.Equals(r.WhenRef, StringComparison.OrdinalIgnoreCase))) problems.Add(n + "waits for rule '" + r.WhenRef + "', which isn't in the plan");
				if (r.When == "quest" || r.When == "step" || r.When == "zone" || r.When == "visit") problems.AddRange(RefProblems(n, r.WhenRef, r, saved));
				if (r.Where == "near" && r.WhereRef.Length > 0 && !r.WhereRef.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase) && RefIsland(r.WhereRef) == null && !saved.Contains(r.WhereRef))
					problems.Add(n + "is placed near '" + r.WhereRef + "': no rule or island has that name");
				if (!islandMode && r.Where == "near" && (r.WhereRef.Length == 0 || r.WhereRef == IntroRule.Self) && (r.When == "start" || r.When == "km" || r.When == "day" || r.When == "rule"))
					problems.Add(n + "is placed near 'where it happened', but " + r.DescribeWhen().ToLowerInvariant() + " happens at no island: name one");
			}
			return string.Join("\n", problems.ToArray());
		}

		/// <summary>The island a ref names in this plan (the island rule's island), or null.</summary>
		string RefIsland(string reference)
		{
			IntroRule by = plan.Rules.FirstOrDefault(x => x.Id.Equals(reference, StringComparison.OrdinalIgnoreCase));
			return by == null ? null : by.What == "island" ? by.WhatArg : "";
		}

		IEnumerable<string> RefProblems(string n, string reference, IntroRule r, HashSet<string> saved)
		{
			if (reference.Length == 0 || reference.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase))
			{
				if (!islandMode) yield return n + "needs the island it waits for (a rule id or island name)";
				yield break;
			}
			string island = RefIsland(reference);
			if (island == null && !saved.Contains(reference)) { yield return n + "waits for '" + reference + "': no rule or island has that name"; yield break; }
			if (island == null) island = reference;
			if (island.Length == 0) yield break; // a generated island: can't look inside
			Dictionary<string, string> props = IslandCache.Props(island);
			if ((r.When == "quest" || r.When == "step") && !IslandQuest.From(props).Exists) yield return n + "island '" + island + "' has no quest";
			if (r.When == "zone" && IslandCache.ZoneOrdinal(island, r.WhenArg) < 0) yield return n + "island '" + island + "' has no zone '" + r.WhenArg + "'";
		}

		void Check()
		{
			string p = Problems();
			problemsText.text = p.Length == 0 ? "<color=#8fdc8f>\u221A Every rule can work.</color>" : "<color=#ffb4aa>" + p + "</color>";
			DrawMap();
		}

		/// <summary>A rough top-down sketch: the raft in the middle, islands where their rules would put them.</summary>
		void DrawMap()
		{
			foreach (Transform child in map) Destroy(child.gameObject);
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

		void Dot(string label, Vector2 world, Color color, float half, float extent)
		{
			Vector2 p = world / extent * half;
			RectTransform r = UIKit.Rect("Dot", map);
			UIKit.Anchor(r, new Vector2(0.5f, 0.5f), p, new Vector2(10, 10));
			Image img = r.gameObject.AddComponent<Image>();
			img.sprite = UIKit.Rounded(5); img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
			Text t = UIKit.Label(map, label, 10, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Name");
			UIKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), p + new Vector2(52f, 0f), new Vector2(90, 14));
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
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
				if (changed) File.WriteAllLines(list, new[] { "# Sample world plans the mod has written once (delete a line to get that sample back)" }.Concat(written.OrderBy(n => n)).ToArray());
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
