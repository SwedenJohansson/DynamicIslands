using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The quest editor: the island's quest with its steps (go to a zone, read a note, open a chest, defeat or catch
	/// animals), a reward and messages. Steps name things placed on the island; the window lists those names.
	/// Save writes the quest into the island's settings (saved with the island).
	/// </summary>
	public class QuestEditorWindow : MonoBehaviour
	{
		const int MaxSteps = 10;
		static QuestEditorWindow instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		IslandQuest quest = new IslandQuest();
		InputField titleField, introField, doneField;
		RectTransform stepsRoot;
		Text rewardText, namesText;
		readonly List<InputField> fields = new List<InputField>();
		bool pickingReward;

		public static void Create(Transform canvas)
		{
			var blocker = new GameObject("QuestEditorWindow", typeof(RectTransform), typeof(Image));
			blocker.layer = 5;
			blocker.transform.SetParent(canvas, false);
			UIKit.Stretch((RectTransform)blocker.transform);
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);
			instance = blocker.AddComponent<QuestEditorWindow>();
			instance.Build();
			blocker.SetActive(false);
		}

		public static void Open()
		{
			if (instance == null) return;
			instance.quest = IslandQuest.From(DynamicIslands.currentIslandProps);
			if (!instance.quest.Exists) instance.quest.Steps.Add(new IslandQuest.Step());
			instance.gameObject.SetActive(true);
			instance.transform.SetAsLastSibling();
			instance.titleField.text = instance.quest.Title;
			instance.introField.text = instance.quest.Intro;
			instance.doneField.text = instance.quest.Done;
			instance.ShowSteps();
			instance.ShowReward();
			instance.ShowNames();
		}

		public static void Close()
		{
			if (instance == null) return;
			instance.gameObject.SetActive(false);
			EditorInput.IsTyping = false;
		}

		/// <summary>Writes a quest into the island being edited (the tests use it too).</summary>
		public static void Apply(IslandQuest q)
		{
			q.To(DynamicIslands.currentIslandProps);
			EditorUI.RefreshIsland();
		}

		void Save()
		{
			quest.Title = titleField.text;
			quest.Intro = introField.text;
			quest.Done = doneField.text;
			quest.Steps.RemoveAll(s => s.Type == "reach" && s.Target.Trim().Length == 0 && s.Text.Trim().Length == 0); // ("go to" nowhere)
			Apply(quest);
			DynamicIslands.Notify(quest.Exists ? "Quest \"" + quest.ShownTitle + "\" with " + quest.Steps.Count + " step(s) saved with the island (Ctrl+S)" : "The island has no quest now");
			Close();
		}

		void Update()
		{
			if (ItemPickerWindow.IsOpen) return;
			if (pickingReward) { pickingReward = false; ShowReward(); }
			EditorInput.IsTyping = fields.Any(f => f != null && f.isFocused);
			if (Input.GetKeyDown(KeyCode.Escape)) Close();
		}

		void OnDisable() { EditorInput.IsTyping = false; }

		void Build()
		{
			RectTransform panel = UIKit.Panel(transform, "Panel", new RectOffset(16, 16, 14, 16), 8f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960, 0));
			RectTransform head = UIKit.Row(panel, 28f, 6f, "Head");
			UIKit.Label(head, "QUEST EDITOR", 18, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			UIKit.Label(head, "Players see the quest when they come to the island; steps are done in order", 12, UIKit.TextMuted, TextAnchor.MiddleRight);

			RectTransform top = UIKit.Row(panel, 30f, 8f, "Top");
			titleField = UIKit.Field(top, "Quest title (e.g. The lost captain)", "", 30f, "Shown at the top of the quest");
			titleField.characterLimit = 48;
			fields.Add(titleField);
			introField = UIKit.Field(panel, "Introduction, shown when players arrive (optional)", "", 30f, "A line or two to start the story");
			introField.characterLimit = 200;
			fields.Add(introField);

			RectTransform stepsGroup = UIKit.Group(panel, "Steps");
			stepsRoot = UIKit.Rect("Steps", stepsGroup);
			UIKit.Vertical(stepsRoot.gameObject, 4f, new RectOffset(0, 0, 0, 0));
			RectTransform addRow = UIKit.Row(stepsGroup, 26f, 6f, "Add");
			UIKit.Button(addRow, "+ Add a step", () => { if (quest.Steps.Count < MaxSteps) { Keep(); quest.Steps.Add(new IslandQuest.Step()); ShowSteps(); } }, "Another step (up to " + MaxSteps + ")", 140, 26f, 12);
			namesText = UIKit.Label(addRow, "", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "Names");
			namesText.verticalOverflow = VerticalWrapMode.Truncate;

			RectTransform reward = UIKit.Group(panel, "Reward and ending");
			RectTransform rewardRow = UIKit.Row(reward, 26f, 6f, "Reward");
			rewardText = UIKit.Label(rewardRow, "", 12, UIKit.TextColor);
			UIKit.Button(rewardRow, "Choose reward...", () =>
			{
				pickingReward = true;
				ItemPickerWindow.OpenFor(() => quest.Reward, v => quest.Reward = v);
			}, "Items every player near the island gets when the quest is done", 140, 26f, 12);
			UIKit.Button(rewardRow, "None", () => { quest.Reward = ""; ShowReward(); }, "No reward", 60, 26f, 12);
			doneField = UIKit.Field(reward, "Message when the quest is done (optional)", "", 30f, "Shown with \"Quest complete\"");
			doneField.characterLimit = 200;
			fields.Add(doneField);

			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			UIKit.Button(buttons, "Remove quest", () => { quest.Steps.Clear(); Apply(quest); DynamicIslands.Notify("The island has no quest now"); Close(); }, "Delete the quest from the island", 130, 34);
			UIKit.Size(UIKit.Label(buttons, "", 12, UIKit.TextMuted).gameObject, -1, -1, 1);
			Button save = UIKit.Button(buttons, "Save", Save, "Keep the quest (save the island to keep it for good)", 110, 34);
			UIKit.SetActive(save, true);
			UIKit.Button(buttons, "Cancel", Close, "Close without changing the quest", 110, 34);
		}

		static readonly Dictionary<string, string> TypeLabels = new Dictionary<string, string>
		{
			{ "reach", "Go to" }, { "read", "Read" }, { "open", "Open" }, { "kill", "Defeat" }, { "catch", "Catch" },
		};

		static readonly Dictionary<string, string> TargetHints = new Dictionary<string, string>
		{
			{ "reach", "trigger zone name" }, { "read", "note title" }, { "open", "chest's note title (empty = any chest)" },
			{ "kill", "creature (e.g. Warthog; empty = any)" }, { "catch", "animal (e.g. Llama; empty = any)" },
		};

		/// <summary>Takes what was typed into the step rows before they are rebuilt.</summary>
		void Keep()
		{
			quest.Title = titleField.text; quest.Intro = introField.text; quest.Done = doneField.text;
		}

		void ShowSteps()
		{
			foreach (Transform child in stepsRoot) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
			fields.RemoveAll(f => f == null || f.transform.IsChildOf(stepsRoot));
			for (int i = 0; i < quest.Steps.Count; i++)
			{
				int index = i;
				IslandQuest.Step s = quest.Steps[i];
				RectTransform row = UIKit.Row(stepsRoot, 28f, 4f, "Step");
				UIKit.Size(UIKit.Label(row, (i + 1) + ".", 13, UIKit.TextMuted, TextAnchor.MiddleRight).gameObject, 22);
				UIKit.Button(row, TypeLabels[s.Type], () =>
				{
					s.Type = IslandQuest.Types[(Array.IndexOf(IslandQuest.Types, s.Type) + 1) % IslandQuest.Types.Length];
					ShowSteps();
				}, "Click to change what the player must do: go to, read, open, defeat, catch", 70, 28f, 12);
				InputField target = UIKit.Field(row, TargetHints[s.Type], s.Target, 28f, "Must match a name on the island exactly (see the names below)");
				UIKit.Size(target.gameObject, 220, 28);
				target.onEndEdit.AddListener(v => s.Target = v.Trim());
				fields.Add(target);
				if (s.Type == "kill" || s.Type == "catch")
				{
					InputField count = UIKit.Field(row, "1", s.Count.ToString(), 28f, "How many");
					UIKit.Size(count.gameObject, 44, 28);
					count.contentType = InputField.ContentType.IntegerNumber;
					count.characterLimit = 2;
					count.onEndEdit.AddListener(v => { int n; s.Count = int.TryParse(v, out n) ? Mathf.Clamp(n, 1, 99) : 1; });
					fields.Add(count);
				}
				InputField text = UIKit.Field(row, s.Describe(), s.Text, 28f, "What players read for this step (empty: made from the step)");
				text.characterLimit = 80;
				text.onEndEdit.AddListener(v => s.Text = v.Trim());
				fields.Add(text);
				UIKit.Button(row, "\u25B2", () => { if (index > 0) { Keep(); quest.Steps.Reverse(index - 1, 2); ShowSteps(); } }, "Earlier", 26, 28f, 11);
				UIKit.Button(row, "\u25BC", () => { if (index < quest.Steps.Count - 1) { Keep(); quest.Steps.Reverse(index, 2); ShowSteps(); } }, "Later", 26, 28f, 11);
				Button del = UIKit.Button(row, "\u00D7", () => { Keep(); quest.Steps.RemoveAt(index); ShowSteps(); }, "Remove this step", 26, 28f, 12);
				UIKit.LabelOf(del).color = new Color(1f, 0.6f, 0.55f);
			}
		}

		void ShowReward()
		{
			List<KeyValuePair<string, int>> items = ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, quest.Reward } });
			rewardText.text = items.Count == 0 ? "<i>No reward</i>" : "Reward: " + string.Join(", ", items.Select(l => ContentCatalog.ItemLabel(l.Key) + " \u00D7" + l.Value).ToArray());
		}

		/// <summary>The names on the island that steps can point at.</summary>
		void ShowNames()
		{
			GameObject placed = GameObject.Find("PlacedObjects");
			List<EditorGameObject> all = placed != null ? placed.GetComponentsInChildren<EditorGameObject>().ToList() : new List<EditorGameObject>();
			var zones = ContentCatalog.ZoneIdsInEditor();
			var notes = all.Where(e => ObjectProps.IsNote(e.GameObjectName, e.Props)).Select(e => ObjectProps.Get(e.Props, ObjectProps.NoteTitle)).Where(t => t.Length > 0).Distinct().ToList();
			var creatures = all.Select(e => ContentCatalog.CreatureOf(e.GameObjectName)).Where(k => k != null).Select(k => k.Label).Distinct().ToList();
			var parts = new List<string>();
			if (zones.Count > 0) parts.Add("Zones: " + string.Join(", ", zones.ToArray()));
			if (notes.Count > 0) parts.Add("Notes: " + string.Join(", ", notes.Take(6).Select(n => "\"" + n + "\"").ToArray()));
			if (creatures.Count > 0) parts.Add("Creatures: " + string.Join(", ", creatures.ToArray()));
			namesText.text = parts.Count > 0 ? string.Join("  \u00B7  ", parts.ToArray()) : "Place trigger zones, notes, chests or creatures first: steps point at them by name.";
		}
	}
}
