using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's "Generate island" window: seed, style, shape (size, height, roughness, peaks) and object
	/// density, and a Generate button that replaces the current island (one Ctrl+Z brings the old one back).
	/// Built in code with UIKit. Opened from the top bar's Generate button or the Island tab.
	/// </summary>
	public class GeneratorWindow : MonoBehaviour
	{
		static GeneratorWindow instance;

		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		InputField seedField;
		Text status, styleText, shapeText;
		UIKit.SliderRow radius, height, roughness, peaks, density;
		int style, shape;
		int mapType = 6;
		Text mapTypeText;
		float confirmUntil;

		public static void Create(Transform canvas, GameObject unusedTemplate)
		{
			var blocker = new GameObject("GeneratorWindow", typeof(RectTransform), typeof(Image));
			blocker.layer = 5;
			blocker.transform.SetParent(canvas, false);
			UIKit.Stretch((RectTransform)blocker.transform);
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f); // modal: swallows clicks on the editor
			instance = blocker.AddComponent<GeneratorWindow>();
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
			RectTransform panel = UIKit.Panel(transform, "Panel", new RectOffset(16, 16, 14, 16), 10f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 0));

			UIKit.Label(panel, "GENERATE ISLAND", 18, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);

			// Seed and style: what island it is
			RectTransform what = UIKit.Group(panel, "Seed and style");
			RectTransform seedRow = UIKit.Row(what, 30f, 6f, "Seed");
			UIKit.Size(UIKit.Label(seedRow, "Seed", 14, UIKit.TextMuted).gameObject, 60);
			seedField = UIKit.Field(seedRow, "any number", "", 30f, "The same seed always gives the same island");
			seedField.contentType = InputField.ContentType.IntegerNumber;
			seedField.characterLimit = 9;
			UIKit.Button(seedRow, "Random", () => seedField.text = UnityEngine.Random.Range(1, 999999).ToString(CultureInfo.InvariantCulture), "A new random seed", 86);
			RectTransform styleRow = UIKit.Row(what, 30f, 4f, "Style");
			UIKit.Size(UIKit.Label(styleRow, "Style", 14, UIKit.TextMuted).gameObject, 60);
			UIKit.Button(styleRow, "<", () => StepStyle(-1), "Previous style", 32);
			Button sb = UIKit.Button(styleRow, "", () => StepStyle(1), "Ground textures and objects: palms, snowy pines, cacti, birches, or a volcano");
			styleText = UIKit.LabelOf(sb);
			UIKit.Size(sb.gameObject, -1, -1, 1);
			UIKit.Button(styleRow, ">", () => StepStyle(1), "Next style", 32);
			RectTransform shapeRow = UIKit.Row(what, 30f, 4f, "ShapeRow");
			UIKit.Size(UIKit.Label(shapeRow, "Layout", 14, UIKit.TextMuted).gameObject, 60);
			UIKit.Button(shapeRow, "<", () => StepShape(-1), "Previous layout", 32);
			Button shb = UIKit.Button(shapeRow, "", () => StepShape(1), "One round island, an atoll, an archipelago, sea stacks, a plateau or a marsh");
			shapeText = UIKit.LabelOf(shb);
			UIKit.Size(shb.gameObject, -1, -1, 1);
			UIKit.Button(shapeRow, ">", () => StepShape(1), "Next layout", 32);

			// Shape
			RectTransform shape = UIKit.Group(panel, "Shape");
			radius = UIKit.Slider(shape, "Size", IslandGenSettings.MinRadius, IslandGenSettings.MaxRadius, 120, v => (v * 2f).ToString("F0") + " m across", null, "How wide the island is");
			height = UIKit.Slider(shape, "Highest point", IslandGenSettings.MinHeight, IslandGenSettings.MaxHeight, 35, v => v.ToString("F0") + " m", null, "Height of the tallest peak above the sea");
			roughness = UIKit.Slider(shape, "Roughness", 0f, 1f, 0.5f, v => v < 0.2f ? "smooth" : v < 0.5f ? "gentle" : v < 0.8f ? "rugged" : "wild", null, "Smooth hills or rugged cliffs");
			peaks = UIKit.Slider(shape, "Peaks", 1, IslandGenSettings.MaxPeaks, 2, v => v.ToString("F0"), null, "How many hills", true);

			// Objects
			RectTransform objects = UIKit.Group(panel, "Objects");
			density = UIKit.Slider(objects, "Trees, rocks and corals", 0f, 1f, 0.5f, v => v <= 0.01f ? "none" : v < 0.35f ? "few" : v < 0.7f ? "some" : "many", null, "How much nature the generator scatters");

			// A whole map type: layout, style and content (chests, notes, a quest...), made as a new island file to edit
			RectTransform types = UIKit.Group(panel, "Or a map type (with content)");
			RectTransform typeRow = UIKit.Row(types, 30f, 4f, "Type");
			UIKit.Button(typeRow, "<", () => StepType(-1), "Previous map type", 32);
			Button tb = UIKit.Button(typeRow, "", () => StepType(1), "Map types: sandbar, atoll, archipelago, sea stacks, boss island, volcano, swamp, frozen spire, treasure island, old camp, sunken island, sky island, wreck...");
			mapTypeText = UIKit.LabelOf(tb);
			UIKit.Size(tb.gameObject, -1, -1, 1);
			UIKit.Button(typeRow, ">", () => StepType(1), "Next map type", 32);
			UIKit.Button(typeRow, "Make", OnMakeType, "Make an island of this type from the seed, save it as gen-<type>-<seed> and open it (the current island is closed)", 70);

			status = UIKit.Label(panel, "", 14, UIKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Italic, "Status");
			UIKit.Size(status.gameObject, -1, 34);

			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			Button gen = UIKit.Button(buttons, "Generate", OnGenerate, "Replace the current island with a generated one (Enter; Ctrl+Z undoes)", -1, 34, 15);
			UIKit.Primary(gen);
			UIKit.Button(buttons, "Close", Close, "Close (Esc)", -1, 34, 15);
		}

		void StepStyle(int step)
		{
			int n = TerrainPainter.Styles.Length;
			style = ((style + step) % n + n) % n;
			ShowStyle();
			SetStatus(TerrainPainter.StyleName(style) + " island" + (TerrainPainter.HasStyle(style) ? "" : " (its textures aren't loaded; it will look tropical)") + ".");
		}

		void StepShape(int step)
		{
			int n = IslandShapes.Names.Length;
			shape = ((shape + step) % n + n) % n;
			ShowShape();
			SetStatus(IslandShapes.Hints[shape] + ".");
		}

		void ShowShape() { if (shapeText != null) shapeText.text = IslandShapes.Names[shape]; }

		void StepType(int step)
		{
			int n = MapTypes.All.Count;
			mapType = ((mapType + step) % n + n) % n;
			ShowType();
			SetStatus(MapTypes.All[mapType].Description + ".");
		}

		void ShowType() { if (mapTypeText != null) mapTypeText.text = MapTypes.All[mapType].Label; }

		void OnMakeType()
		{
			if (Time.unscaledTime > confirmUntil)
			{
				confirmUntil = Time.unscaledTime + 6f;
				SetStatus("This opens a new island: save the current one first. Click Make again to go ahead.");
				return;
			}
			confirmUntil = 0f;
			int seed;
			if (!int.TryParse(seedField.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) seed = UnityEngine.Random.Range(1, 999999);
			string name = MakeType(MapTypes.All[mapType], seed);
			if (name == null) { SetStatus("Making the island failed - see the console (F10)."); return; }
			if (DynamicIslands.LoadIsland(name)) { Close(); DynamicIslands.Notify("Made a " + MapTypes.All[mapType].Label.ToLowerInvariant() + ": '" + name + "'. Change it as you like and save it."); }
		}

		/// <summary>Makes an island of a map type from a seed and saves it; returns its name (null if it failed).</summary>
		public static string MakeType(MapType type, int seed)
		{
			try
			{
				float elevation;
				IslandGenSettings s = MapTypes.Roll(type, new System.Random(seed), out elevation);
				string name = MapTypes.FileName(type, s);
				MapTypes.Create(type, s, elevation, name).Save(IslandSpawner.PathFor(name));
				return name;
			}
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Making a '" + type.Name + "' island failed: " + e); return null; }
		}

		void ShowStyle() { if (styleText != null) styleText.text = TerrainPainter.StyleName(style); }

		void Show(IslandGenSettings s)
		{
			seedField.text = s.Seed.ToString(CultureInfo.InvariantCulture);
			radius.Slider.value = s.Radius; height.Slider.value = s.Height; roughness.Slider.value = s.Roughness; peaks.Slider.value = s.Peaks; density.Slider.value = s.ObjectDensity;
			style = DynamicIslands.currentStyle; // start from the island's current style
			ShowStyle();
			shape = s.Shape;
			ShowShape();
			ShowType();
		}

		void OnGenerate()
		{
			int seed;
			if (!int.TryParse(seedField.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) { seed = UnityEngine.Random.Range(1, 999999); seedField.text = seed.ToString(CultureInfo.InvariantCulture); }
			var s = new IslandGenSettings
			{
				Seed = seed, Radius = radius.Slider.value, Height = height.Slider.value, Roughness = roughness.Slider.value,
				Peaks = Mathf.RoundToInt(peaks.Slider.value), ObjectDensity = density.Slider.value, Style = style, Shape = shape
			};
			try
			{
				int n = IslandGenerator.GenerateInEditor(s);
				IslandGenerator.FrameCamera(s);
				EditorUI.RefreshIsland();
				SetStatus("Island " + seed + " generated with " + n + " objects. Ctrl+Z undoes it.");
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Generating failed: " + e);
				SetStatus("Generating failed - see the console (F10).");
			}
		}

		void SetStatus(string message) { status.text = message; }
	}
}
