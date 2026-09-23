using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's Save / Load window: a name field, a list of saved islands (click = pick, double-click = load)
	/// and Save / Load / Close buttons. Built at runtime (the UI bundle has no such screen); the main buttons are
	/// clones of the object list's button so they match the editor's style.
	/// Open with the navbar "Islands" button, the Menu dropdown, or Ctrl+O; Ctrl+S saves the current island.
	/// </summary>
	public class IslandFilesWindow : MonoBehaviour
	{
		static IslandFilesWindow instance;

		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		InputField nameField;
		RectTransform listContent;
		Text status;
		Font font;
		GameObject buttonTemplate;
		string pendingOverwrite;
		string lastClicked;
		float lastClickTime;

		static readonly Color PanelColor = new Color(0.16f, 0.11f, 0.07f, 0.96f);
		static readonly Color EntryColor = new Color(0.93f, 0.87f, 0.72f);
		static readonly Color EntrySelectedColor = new Color(1f, 0.8f, 0.35f);
		static readonly Color TextDark = new Color(0.15f, 0.1f, 0.05f);
		static readonly Color TextLight = new Color(0.98f, 0.93f, 0.8f);

		/// <summary>Builds the (hidden) window on the editor canvas. Call once when the editor opens.</summary>
		public static void Create(Transform canvas, GameObject buttonTemplate)
		{
			var blocker = new GameObject("IslandFilesWindow", typeof(RectTransform), typeof(Image));
			blocker.transform.SetParent(canvas, false);
			Stretch(blocker.GetComponent<RectTransform>());
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f); // dims the editor and swallows clicks (modal)
			instance = blocker.AddComponent<IslandFilesWindow>();
			instance.buttonTemplate = buttonTemplate;
			Text anyText = canvas.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.font != null && t.name == "CamPos")
				?? canvas.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.font != null);
			instance.font = anyText != null ? anyText.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
			instance.Build();
			blocker.SetActive(false);
		}

		public static void Open()
		{
			if (instance == null) return;
			instance.gameObject.SetActive(true);
			instance.transform.SetAsLastSibling();
			instance.nameField.text = DynamicIslands.currentIslandName;
			instance.pendingOverwrite = null;
			instance.SetStatus("Type a name or pick an island. Double-click an island to load it.", false);
			instance.Refresh();
			instance.nameField.ActivateInputField();
		}

		public static void Close()
		{
			if (instance == null) return;
			instance.gameObject.SetActive(false);
			EditorInput.IsTyping = false;
		}

		/// <summary>Ctrl+S: save straight away if the island already has a file, otherwise ask for a name.</summary>
		public static void QuickSave()
		{
			if (File.Exists(IslandSpawner.PathFor(DynamicIslands.currentIslandName))) DynamicIslands.SaveIsland(DynamicIslands.currentIslandName);
			else Open();
		}

		void Update()
		{
			EditorInput.IsTyping = nameField != null && nameField.isFocused;
			if (Input.GetKeyDown(KeyCode.Escape)) Close();
			else if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && nameField.text.Trim().Length > 0) OnSave();
		}

		void OnDisable()
		{
			EditorInput.IsTyping = false;
		}

		void Build()
		{
			var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			panel.transform.SetParent(transform, false);
			RectTransform pr = panel.GetComponent<RectTransform>();
			pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
			pr.sizeDelta = new Vector2(440, 400);
			panel.GetComponent<Image>().color = PanelColor;

			MakeText(panel.transform, "Title", "ISLANDS", 24, TextLight, new Vector2(0, 170), new Vector2(400, 36), TextAnchor.MiddleCenter, FontStyle.Bold);
			MakeText(panel.transform, "NameLabel", "Island name", 14, TextLight, new Vector2(0, 138), new Vector2(400, 20), TextAnchor.MiddleLeft, FontStyle.Normal);

			// Name field
			GameObject field = DefaultControls.CreateInputField(new DefaultControls.Resources());
			field.name = "NameField";
			field.transform.SetParent(panel.transform, false);
			Place(field.GetComponent<RectTransform>(), new Vector2(0, 112), new Vector2(400, 32));
			field.GetComponent<Image>().color = EntryColor;
			nameField = field.GetComponent<InputField>();
			nameField.characterLimit = 64;
			foreach (Text t in field.GetComponentsInChildren<Text>(true)) { t.font = font; t.fontSize = 16; t.color = TextDark; }
			nameField.placeholder.GetComponent<Text>().text = "e.g. myisland";
			nameField.placeholder.GetComponent<Text>().color = new Color(0.4f, 0.35f, 0.3f, 0.8f);
			nameField.onValueChanged.AddListener(v => { pendingOverwrite = null; Highlight(); });

			MakeText(panel.transform, "ListLabel", "Saved islands", 14, TextLight, new Vector2(0, 82), new Vector2(400, 20), TextAnchor.MiddleLeft, FontStyle.Normal);

			// List of saved islands
			GameObject scroll = DefaultControls.CreateScrollView(new DefaultControls.Resources());
			scroll.name = "IslandList";
			scroll.transform.SetParent(panel.transform, false);
			Place(scroll.GetComponent<RectTransform>(), new Vector2(0, -20), new Vector2(400, 180));
			scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f);
			ScrollRect sr = scroll.GetComponent<ScrollRect>();
			sr.horizontal = false;
			if (sr.horizontalScrollbar != null) { Destroy(sr.horizontalScrollbar.gameObject); sr.horizontalScrollbar = null; }
			sr.scrollSensitivity = 25f;
			listContent = sr.content;
			var layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
			layout.spacing = 3; layout.padding = new RectOffset(4, 4, 4, 4);
			layout.childControlWidth = true; layout.childControlHeight = true;
			layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
			listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			status = MakeText(panel.transform, "Status", "", 13, TextLight, new Vector2(0, -130), new Vector2(400, 36), TextAnchor.MiddleCenter, FontStyle.Italic);

			MakeButton(panel.transform, "Save", new Vector2(-135, -172), OnSave);
			MakeButton(panel.transform, "Load", new Vector2(0, -172), OnLoad);
			MakeButton(panel.transform, "Close", new Vector2(135, -172), Close);
		}

		void Refresh()
		{
			foreach (Transform child in listContent) Destroy(child.gameObject);
			string[] names = IslandSpawner.ListSavedIslands().ToArray();
			if (names.Length == 0)
			{
				MakeText(listContent, "Empty", "No saved islands yet.", 14, TextLight, Vector2.zero, new Vector2(380, 28), TextAnchor.MiddleCenter, FontStyle.Italic)
					.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
				return;
			}
			foreach (string n in names)
			{
				string islandName = n;
				var info = new FileInfo(IslandSpawner.PathFor(n));
				GameObject entry = DefaultControls.CreateButton(new DefaultControls.Resources());
				entry.name = "Island_" + n;
				entry.transform.SetParent(listContent, false);
				entry.AddComponent<LayoutElement>().preferredHeight = 28;
				Text label = entry.GetComponentInChildren<Text>();
				label.font = font; label.fontSize = 15; label.color = TextDark; label.alignment = TextAnchor.MiddleLeft;
				label.rectTransform.offsetMin = new Vector2(10, 0);
				label.text = n + "   <size=11>" + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + ", " + Math.Max(1, info.Length / 1024) + " KB</size>";
				label.supportRichText = true;
				entry.GetComponent<Button>().onClick.AddListener(() => OnEntryClicked(islandName));
			}
			Highlight();
		}

		void Highlight()
		{
			string current = nameField != null ? nameField.text.Trim() : "";
			foreach (Transform child in listContent)
			{
				Image img = child.GetComponent<Image>();
				if (img != null) img.color = child.name == "Island_" + current ? EntrySelectedColor : EntryColor;
			}
		}

		void OnEntryClicked(string islandName)
		{
			bool doubleClick = lastClicked == islandName && Time.unscaledTime - lastClickTime < 0.4f;
			lastClicked = islandName; lastClickTime = Time.unscaledTime;
			nameField.text = islandName;
			if (doubleClick) OnLoad();
		}

		void OnSave()
		{
			string n = nameField.text.Trim();
			if (n.Length == 0) { SetStatus("Type a name first.", true); return; }
			if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { SetStatus("A name can't contain \\ / : * ? \" < > |", true); return; }
			bool exists = File.Exists(IslandSpawner.PathFor(n));
			if (exists && n != DynamicIslands.currentIslandName && pendingOverwrite != n)
			{
				pendingOverwrite = n;
				SetStatus("'" + n + "' already exists. Press Save again to overwrite it.", true);
				return;
			}
			if (DynamicIslands.SaveIsland(n)) Close();
			else SetStatus("Saving failed - see the console (F10).", true);
		}

		void OnLoad()
		{
			string n = nameField.text.Trim();
			if (n.Length == 0) { SetStatus("Pick an island to load.", true); return; }
			if (!File.Exists(IslandSpawner.PathFor(n))) { SetStatus("There is no saved island called '" + n + "'.", true); return; }
			if (DynamicIslands.LoadIsland(n)) Close();
			else SetStatus("Loading failed - see the console (F10).", true);
		}

		void SetStatus(string message, bool warning)
		{
			status.text = message;
			status.color = warning ? new Color(1f, 0.7f, 0.4f) : TextLight;
		}

		void MakeButton(Transform parent, string label, Vector2 pos, Action onClick)
		{
			GameObject b;
			if (buttonTemplate != null)
			{
				b = Instantiate(buttonTemplate, parent);
				b.SetActive(true);
				Button button = b.GetComponent<Button>();
				button.onClick = new Button.ButtonClickedEvent();
			}
			else
			{
				b = DefaultControls.CreateButton(new DefaultControls.Resources());
				b.transform.SetParent(parent, false);
			}
			b.name = label + "Button";
			Place(b.GetComponent<RectTransform>(), pos, new Vector2(120, 36));
			Text t = b.GetComponentInChildren<Text>(true);
			if (t != null) { t.text = label; if (buttonTemplate == null) { t.font = font; t.fontSize = 16; } }
			b.GetComponent<Button>().onClick.AddListener(() => onClick());
		}

		Text MakeText(Transform parent, string name, string text, int size, Color color, Vector2 pos, Vector2 box, TextAnchor anchor, FontStyle style)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Text));
			go.transform.SetParent(parent, false);
			Place(go.GetComponent<RectTransform>(), pos, box);
			Text t = go.GetComponent<Text>();
			t.font = font; t.fontSize = size; t.color = color; t.alignment = anchor; t.fontStyle = style; t.text = text;
			t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
			t.raycastTarget = false;
			return t;
		}

		static void Place(RectTransform r, Vector2 pos, Vector2 size)
		{
			r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
			r.anchoredPosition = pos;
			r.sizeDelta = size;
		}

		static void Stretch(RectTransform r)
		{
			r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
			r.offsetMin = r.offsetMax = Vector2.zero;
		}
	}
}
