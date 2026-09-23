using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's "Generate island" window: seed, size, height, roughness, peaks and object density, and a
	/// Generate button that replaces the current island (one Ctrl+Z brings the old one back). Built at runtime
	/// like IslandFilesWindow. Opened from the Terrain tab's Generate button or Menu > Generate island...
	/// </summary>
	public class GeneratorWindow : MonoBehaviour
	{
		static GeneratorWindow instance;

		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		Font font;
		GameObject buttonTemplate;
		InputField seedField;
		Text status;
		Slider radius, height, roughness, peaks, density;

		static readonly Color PanelColor = new Color(0.16f, 0.11f, 0.07f, 0.96f);
		static readonly Color FieldColor = new Color(0.93f, 0.87f, 0.72f);
		static readonly Color TextDark = new Color(0.15f, 0.1f, 0.05f);
		static readonly Color TextLight = new Color(0.98f, 0.93f, 0.8f);

		public static void Create(Transform canvas, GameObject buttonTemplate)
		{
			var blocker = new GameObject("GeneratorWindow", typeof(RectTransform), typeof(Image));
			blocker.transform.SetParent(canvas, false);
			RectTransform br = blocker.GetComponent<RectTransform>();
			br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = br.offsetMax = Vector2.zero;
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.35f); // modal: swallows clicks on the editor
			instance = blocker.AddComponent<GeneratorWindow>();
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
			IslandFilesWindow.Close();
			instance.gameObject.SetActive(true);
			instance.transform.SetAsLastSibling();
			instance.Show(IslandGenerator.Last);
			instance.SetStatus("Generating replaces the current island. Ctrl+Z brings it back.");
		}

		public static void Close()
		{
			if (instance == null) return;
			instance.gameObject.SetActive(false);
			EditorInput.IsTyping = false;
		}

		void Update()
		{
			EditorInput.IsTyping = seedField != null && seedField.isFocused;
			if (Input.GetKeyDown(KeyCode.Escape)) Close();
			else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnGenerate();
		}

		void OnDisable() { EditorInput.IsTyping = false; }

		void Build()
		{
			var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			panel.transform.SetParent(transform, false);
			Place(panel.GetComponent<RectTransform>(), Vector2.zero, new Vector2(440, 430));
			panel.GetComponent<Image>().color = PanelColor;
			Transform p = panel.transform;

			MakeText(p, "Title", "GENERATE ISLAND", 24, TextLight, new Vector2(0, 185), new Vector2(400, 36), TextAnchor.MiddleCenter, FontStyle.Bold);

			// Seed: a number field and a dice button
			MakeText(p, "SeedLabel", "Seed", 15, TextLight, new Vector2(-150, 145), new Vector2(100, 28), TextAnchor.MiddleLeft, FontStyle.Normal);
			GameObject field = DefaultControls.CreateInputField(new DefaultControls.Resources());
			field.name = "SeedField";
			field.transform.SetParent(p, false);
			Place(field.GetComponent<RectTransform>(), new Vector2(10, 145), new Vector2(170, 32));
			field.GetComponent<Image>().color = FieldColor;
			seedField = field.GetComponent<InputField>();
			seedField.contentType = InputField.ContentType.IntegerNumber;
			seedField.characterLimit = 9;
			// Overflow: a line that doesn't fit the text box exactly would otherwise not be drawn at all
			foreach (Text t in field.GetComponentsInChildren<Text>(true)) { t.font = font; t.fontSize = 16; t.color = TextDark; t.verticalOverflow = VerticalWrapMode.Overflow; }
			seedField.placeholder.GetComponent<Text>().text = "any number";
			MakeButton(p, "Random", new Vector2(150, 145), new Vector2(90, 30), () => seedField.text = UnityEngine.Random.Range(1, 999999).ToString(CultureInfo.InvariantCulture));

			radius = MakeSlider(p, "Size", 100, IslandGenSettings.MinRadius, IslandGenSettings.MaxRadius, false, v => "Size: " + (v * 2f).ToString("F0") + " m across");
			height = MakeSlider(p, "Height", 60, IslandGenSettings.MinHeight, IslandGenSettings.MaxHeight, false, v => "Highest point: " + v.ToString("F0") + " m");
			roughness = MakeSlider(p, "Roughness", 20, 0f, 1f, false, v => "Roughness: " + (v < 0.2f ? "smooth" : v < 0.5f ? "gentle" : v < 0.8f ? "rugged" : "wild"));
			peaks = MakeSlider(p, "Peaks", -20, 1, IslandGenSettings.MaxPeaks, true, v => "Peaks: " + v.ToString("F0"));
			density = MakeSlider(p, "Objects", -60, 0f, 1f, false, v => "Trees, rocks and corals: " + (v <= 0.01f ? "none" : v < 0.35f ? "few" : v < 0.7f ? "some" : "many"));

			status = MakeText(p, "Status", "", 13, TextLight, new Vector2(0, -115), new Vector2(400, 36), TextAnchor.MiddleCenter, FontStyle.Italic);
			// Style: each click moves to the next one
			styleText = MakeButton(p, "Style", new Vector2(-140, -175), new Vector2(130, 36), () =>
			{
				style = (style + 1) % TerrainPainter.Styles.Length;
				ShowStyle();
				SetStatus(TerrainPainter.StyleName(style) + " island" + (TerrainPainter.HasStyle(style) ? "" : " (its textures aren't loaded; it will look tropical)") + ". Click the style to change it.");
			});
			MakeButton(p, "Generate", new Vector2(0, -175), new Vector2(130, 36), OnGenerate);
			MakeButton(p, "Close", new Vector2(140, -175), new Vector2(130, 36), Close);
		}

		int style;
		Text styleText;

		void ShowStyle() { if (styleText != null) styleText.text = TerrainPainter.StyleName(style); }

		void Show(IslandGenSettings s)
		{
			seedField.text = s.Seed.ToString(CultureInfo.InvariantCulture);
			radius.value = s.Radius; height.value = s.Height; roughness.value = s.Roughness; peaks.value = s.Peaks; density.value = s.ObjectDensity;
			style = DynamicIslands.currentStyle; // start from the island's current style
			ShowStyle();
		}

		void OnGenerate()
		{
			int seed;
			if (!int.TryParse(seedField.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) { seed = UnityEngine.Random.Range(1, 999999); seedField.text = seed.ToString(CultureInfo.InvariantCulture); }
			var s = new IslandGenSettings
			{
				Seed = seed, Radius = radius.value, Height = height.value, Roughness = roughness.value,
				Peaks = Mathf.RoundToInt(peaks.value), ObjectDensity = density.value, Style = style
			};
			try
			{
				int n = IslandGenerator.GenerateInEditor(s);
				IslandGenerator.FrameCamera(s);
				SetStatus("Island " + seed + " generated with " + n + " objects. Ctrl+Z undoes it.");
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Generating failed: " + e);
				SetStatus("Generating failed - see the console (F10).");
			}
		}

		void SetStatus(string message) { status.text = message; }

		Slider MakeSlider(Transform parent, string name, float y, float min, float max, bool whole, Func<float, string> label)
		{
			Text text = MakeText(parent, name + "Label", "", 15, TextLight, new Vector2(0, y + 14), new Vector2(400, 22), TextAnchor.MiddleLeft, FontStyle.Normal);
			GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
			go.name = name + "Slider";
			go.transform.SetParent(parent, false);
			Place(go.GetComponent<RectTransform>(), new Vector2(0, y - 8), new Vector2(400, 20));
			Slider slider = go.GetComponent<Slider>();
			slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = whole;
			foreach (Image img in go.GetComponentsInChildren<Image>(true))
				img.color = img.name == "Handle" ? new Color(1f, 0.8f, 0.35f) : img.name == "Fill" ? new Color(0.85f, 0.65f, 0.3f) : new Color(0.45f, 0.38f, 0.3f);
			slider.onValueChanged.AddListener(v => text.text = label(v));
			text.text = label(slider.value);
			return slider;
		}

		Text MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Action onClick)
		{
			GameObject b;
			if (buttonTemplate != null)
			{
				b = Instantiate(buttonTemplate, parent);
				b.SetActive(true);
				b.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
			}
			else
			{
				b = DefaultControls.CreateButton(new DefaultControls.Resources());
				b.transform.SetParent(parent, false);
			}
			b.name = label + "Button";
			Place(b.GetComponent<RectTransform>(), pos, size);
			Text t = b.GetComponentInChildren<Text>(true);
			if (t != null) { t.text = label; if (buttonTemplate == null) { t.font = font; t.fontSize = 16; } }
			b.GetComponent<Button>().onClick.AddListener(() => onClick());
			return t;
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
	}
}
