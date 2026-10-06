using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RuntimeGizmos;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's screen, built in code with UIKit (the UI bundle's old toolbar is hidden):
	///
	///   +- top bar -----------------------------------------------------------------------------------+
	///   | CUSTOM ISLANDS name | New Open Save Save as | Undo Redo | Terrain Objects Island | Generate Menu |
	///   +--------------+-----------------------------------------------------+-----------------+
	///   | tool panel   |                                                     | object browser  |
	///   | (per tab,    |                     3D view                         | (Objects tab)   |
	///   |  bordered    |                                                     |                 |
	///   |  groups)     |                                                     |                 |
	///   +--------------+-----------------------------------------------------+-----------------+
	///   | status bar: hint for the tool / the control under the mouse           camera position |
	///   +---------------------------------------------------------------------------------------+
	///
	/// Related buttons sit together in bordered groups (Sculpt, Paint, Brush / Transform, Selection, Placement /
	/// Island, Generate). The active choice of a group is shown in the accent colour.
	/// </summary>
	public static class EditorUI
	{
		public static Canvas Canvas { get; private set; }
		/// <summary>The left panel's frame (its content, "ToolPanel", scrolls inside it).</summary>
		public static RectTransform ToolFrame { get; private set; }
		static TabSelector tabs;

		static Button[] tabButtons;
		static Button[] brushButtons; // Raise, Lower, Flatten, Smooth, 4 paint slots, Auto
		static Image[] paintSwatches;
		static Button[] gizmoButtons; // Move, Rotate, Scale, All
		static Button randomButton, slopeButton, gridButton;
		static UIKit.SliderRow sizeSlider, strengthSlider;
		static Text islandNameText, styleText, selectionText, statsText, hintText, cameraText;
		static InputField elevationField;
		// (the height presets: the one that matches the island's height is lit)
		static Button presetSea, presetFlying, presetSunken;
		static DropdownButton styleDrop;
		static Text infoRegrowText;
		static RectTransform terrainTools, objectTools, islandTools, browserPanel;
		static string hoverHint;
		/// <summary>A short message in the status bar (Flash), shown until this time.</summary>
		static string flash;
		static float flashUntil;

		/// <summary>Shows a short message in the status bar for a moment (the camera's speed, framing).</summary>
		public static void Flash(string text, float seconds = 2f) { flash = text; flashUntil = Time.unscaledTime + seconds; }
		static bool hintHooked;

		/// <summary>Terrain slot painted by each of the four paint buttons.</summary>
		static readonly int[] PaintSlots = { TerrainPainter.Sand, TerrainPainter.Grass, TerrainPainter.Rock, TerrainPainter.Seabed };
		static readonly Color[] SlotColors =
		{
			new Color(0.55f, 0.45f, 0.3f), // seabed
			new Color(0.95f, 0.82f, 0.5f), // sand
			new Color(0.45f, 0.75f, 0.3f), // grass
			new Color(0.62f, 0.62f, 0.64f) // rock
		};

		const float TopBarHeight = 50f, StatusBarHeight = 26f, ToolPanelWidth = 284f, BrowserWidth = 340f, Margin = 10f;

		public static void RefreshSliders()
		{
			if (sizeSlider != null && sizeSlider.Slider != null) { sizeSlider.Slider.SetValueWithoutNotify(terraineditor.brushRadius); sizeSlider.Value.text = SizeText(terraineditor.brushRadius); }
			if (strengthSlider != null && strengthSlider.Slider != null) { strengthSlider.Slider.SetValueWithoutNotify(terraineditor.strength); strengthSlider.Value.text = StrengthText(terraineditor.strength); }
		}

		static string SizeText(float radius) { return (radius * 2f).ToString("F0") + " m"; }
		static string StrengthText(float s) { return s.ToString("F1") + " m/s"; }

		/// <summary>Builds the editor screen. The bundle's canvas (old toolbar) is switched off.</summary>
		public static void Setup(Transform bundleCanvas, TabSelector tabSelector)
		{
			tabs = tabSelector;
			if (bundleCanvas != null) bundleCanvas.gameObject.SetActive(false);

			Canvas = UIKit.CreateCanvas("CustomIslandsEditorUI", 20);
			if (tabs == null) tabs = Canvas.gameObject.AddComponent<TabSelector>();
			Transform root = Canvas.transform;
			if (!hintHooked) { UIKit.HintChanged += h => hoverHint = h; hintHooked = true; }
			hoverHint = null;
			lastSelectionCount = -1; shownPaintLayer = -1; elevationTyping = false;

			BuildTopBar(root);
			BuildStatusBar(root);

			// (as tall as the tab's tools, up to the status bar; a long inspector scrolls instead of running off the screen)
			RectTransform tools = UIKit.ScrollPanel(root, "ToolPanel", new RectOffset(10, 2, 10, 10), 10f, StatusBarHeight + Margin);
			ToolFrame = (RectTransform)tools.parent.parent;
			UIKit.Anchor(ToolFrame, new Vector2(0, 1), new Vector2(Margin, -(TopBarHeight + Margin)), new Vector2(ToolPanelWidth, 200));
			terrainTools = BuildTerrainTools(tools);
			objectTools = BuildObjectTools(tools);
			islandTools = BuildIslandTools(tools);

			browserPanel = UIKit.Panel(root, "ObjectBrowser", new RectOffset(10, 10, 10, 10), 8f, false); // fills the height
			browserPanel.anchorMin = new Vector2(1, 0); browserPanel.anchorMax = new Vector2(1, 1); browserPanel.pivot = new Vector2(1, 1);
			browserPanel.sizeDelta = new Vector2(BrowserWidth, -(TopBarHeight + StatusBarHeight + Margin * 2));
			browserPanel.anchoredPosition = new Vector2(-Margin, -(TopBarHeight + Margin));
			browserPanel.gameObject.AddComponent<ObjectBrowser>().Build(browserPanel);

			tabs.TabChanged += OnTabChanged;
			SetBrush(terraineditor.modificationAction);
			SetGizmo(TransformType.Move);
			RefreshOptions();
			RefreshStyle();
			RefreshIsland();
			OnTabChanged(tabs.SelectedTab);
		}

		#region Top bar and status bar

		static void BuildTopBar(Transform root)
		{
			RectTransform bar = UIKit.Rect("TopBar", root);
			bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
			bar.sizeDelta = new Vector2(0, TopBarHeight); bar.anchoredPosition = Vector2.zero;
			UIKit.Surface(bar, 0, false);
			var line = UIKit.Rect("Line", bar);
			line.anchorMin = new Vector2(0, 0); line.anchorMax = new Vector2(1, 0); line.sizeDelta = new Vector2(0, 2); line.pivot = new Vector2(0.5f, 0);
			line.gameObject.AddComponent<Image>().color = UIKit.PanelRim;
			HorizontalLayoutGroup h = UIKit.Horizontal(bar.gameObject, 10f, new RectOffset(12, 12, 8, 8));
			h.childForceExpandWidth = false;

			// Title and the island's name
			RectTransform brand = UIKit.Rect("Brand", bar);
			UIKit.Vertical(brand.gameObject, 0f, new RectOffset(0, 0, 0, 0), false);
			UIKit.Size(brand.gameObject, 190);
			UIKit.Size(UIKit.Label(brand, "CUSTOM ISLANDS", 12, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold).gameObject, -1, 14);
			islandNameText = UIKit.Label(brand, "", 16, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold, "IslandName");
			islandNameText.horizontalOverflow = HorizontalWrapMode.Overflow;
			UIKit.Size(islandNameText.gameObject, -1, 20);

			RectTransform file = ToolbarGroup(bar, "File");
			UIKit.Button(file, "New", () => ConfirmNew(), "New island: an empty sea to start from (asks first)", 58);
			UIKit.Button(file, "Open", IslandFilesWindow.Open, "Open a saved island (Ctrl+O)", 58);
			UIKit.Button(file, "Save", IslandFilesWindow.QuickSave, "Save the island (Ctrl+S)", 58);
			UIKit.Button(file, "Save as", IslandFilesWindow.Open, "Save the island under a new name", 70);

			RectTransform edit = ToolbarGroup(bar, "Edit");
			UIKit.Button(edit, "Undo", () => CommandUndoRedo.UndoRedoManager.Undo(), "Undo the last change (Ctrl+Z)", 58);
			UIKit.Button(edit, "Redo", () => CommandUndoRedo.UndoRedoManager.Redo(), "Redo (Ctrl+Y)", 58);

			RectTransform modes = ToolbarGroup(bar, "Tabs");
			tabButtons = new[]
			{
				UIKit.Button(modes, "Terrain", () => SetTab(TAB.TerrainEdit), "Shape the land and paint its ground (F1)", 84),
				UIKit.Button(modes, "Objects", () => SetTab(TAB.ObjectPlace), "Place, move, turn and scale objects (F2)", 84),
				UIKit.Button(modes, "Island", () => SetTab(TAB.Island), "Style, height in the world, and the island generator (F3)", 84),
			};

			RectTransform spacer = UIKit.Rect("Spacer", bar);
			UIKit.Size(spacer.gameObject, 0, -1, 1);

			RectTransform app = ToolbarGroup(bar, "App");
			UIKit.Button(app, "Generate", GeneratorWindow.Open, "Make a random island from a seed (replaces the current one; Ctrl+Z undoes)", 84);
			// (a list of the times of day - it cycled one click at a time)
			string[] lightHints = { "Low sun from the east", "Sun high up, short shadows", "Low warm sun from the west", "Moonlight: how it looks after dark", "Grey sky, soft light" };
			lightButton = DropList.Make(app, "Button_Light", Enumerable.Range(0, EditorLighting.Names.Length).Select(i => new DropList.Option(i.ToString(), EditorLighting.Names[i], lightHints[Mathf.Min(i, lightHints.Length - 1)])).ToList(), EditorLighting.Current.ToString(),
				v => { EditorLighting.Apply(int.Parse(v), true); RefreshLight(); }, 118, "The time of day in the editor: morning, noon, evening, night, overcast - how the island looks then (only in the editor)", 30f, 13);
			// (as wide as the button it was: wider, the top bar pushed Main menu off a 1024 x 768 screen - the words shrink to fit instead)
			Text lightText = UIKit.LabelOf(lightButton);
			lightText.resizeTextForBestFit = true; lightText.resizeTextMinSize = 9; lightText.resizeTextMaxSize = 13;
			UIKit.Stretch(lightText.rectTransform, 5, 22, 1, 1);
			UIKit.Button(app, "Test", IslandTest.Start, "Try the island in a world: it is saved, the test world '" + IslandTest.WorldName + "' opens with the island beside the raft and you on it. Back with Esc > Custom Islands > Back to the editor", 60);
			UIKit.Button(app, "World plans", () => WorldPlanWindow.Open(), "Plans for new worlds: which islands appear, when (start, km, days, quests, zones...) and where", 104);
			UIKit.Button(app, "Main menu", () => { EditorAutosave.WriteNow(); UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene", UnityEngine.SceneManagement.LoadSceneMode.Single); },
				"Back to Raft's main menu (save first! Unsaved changes are kept as an autosave, offered the next time the editor opens)", 92);
		}

		static Button lightButton;

		/// <summary>The Light button says the time of day shown.</summary>
		public static void RefreshLight()
		{
			if (lightButton == null) return;
			UIKit.LabelOf(lightButton).text = "Light: " + EditorLighting.Names[EditorLighting.Current];
			DropdownButton d = lightButton.GetComponent<DropdownButton>();
			if (d != null) d.Value = EditorLighting.Current.ToString();
		}

		/// <summary>A bordered strip of buttons in the top bar.</summary>
		static RectTransform ToolbarGroup(Transform bar, string name)
		{
			RectTransform g = UIKit.Rect("Group_" + name, bar);
			UIKit.Background(g.gameObject, UIKit.GroupBg, 8);
			HorizontalLayoutGroup h = UIKit.Horizontal(g.gameObject, 4f, new RectOffset(4, 4, 3, 3));
			h.childForceExpandWidth = false;
			UIKit.Border(g, UIKit.GroupBorder, 8, 1.5f);
			return g;
		}

		static void BuildStatusBar(Transform root)
		{
			RectTransform bar = UIKit.Rect("StatusBar", root);
			bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
			bar.sizeDelta = new Vector2(0, StatusBarHeight); bar.anchoredPosition = Vector2.zero;
			UIKit.Background(bar.gameObject, UIKit.PanelRim, 0);
			HorizontalLayoutGroup h = UIKit.Horizontal(bar.gameObject, 12f, new RectOffset(12, 12, 2, 2));
			h.childForceExpandWidth = false;
			hintText = UIKit.Label(bar, "", 14, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Hint");
			hintText.verticalOverflow = VerticalWrapMode.Truncate; // a long hint is cut rather than running into the camera readout
			UIKit.Size(hintText.gameObject, -1, -1, 1);
			cameraText = UIKit.Label(bar, "", 13, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Normal, "Camera");
			UIKit.Size(cameraText.gameObject, 260);
		}

		#endregion

		#region Tool panels

		static RectTransform ToolSection(Transform panel, string name)
		{
			RectTransform r = UIKit.Rect(name, panel);
			UIKit.Vertical(r.gameObject, 10f, new RectOffset(0, 0, 0, 0));
			return r;
		}

		static RectTransform BuildTerrainTools(Transform panel)
		{
			RectTransform s = ToolSection(panel, "TerrainTools");

			RectTransform sculpt = UIKit.Group(s, "Sculpt");
			RectTransform r1 = UIKit.Row(sculpt), r2 = UIKit.Row(sculpt);
			brushButtons = new Button[9];
			brushButtons[0] = UIKit.Button(r1, "Raise", () => SetBrush(terraineditor.TerrainModificationAction.Raise), "Raise: hold the left mouse button to build up land");
			brushButtons[1] = UIKit.Button(r1, "Lower", () => SetBrush(terraineditor.TerrainModificationAction.Lower), "Lower: dig down, make bays and lagoons");
			brushButtons[2] = UIKit.Button(r2, "Flatten", () => SetBrush(terraineditor.TerrainModificationAction.Flatten), "Flatten: levels the ground to the height where the stroke starts");
			brushButtons[3] = UIKit.Button(r2, "Smooth", () => SetBrush(terraineditor.TerrainModificationAction.Smooth), "Smooth: softens bumps and sharp edges");

			RectTransform paint = UIKit.Group(s, "Paint ground");
			RectTransform p1 = UIKit.Row(paint), p2 = UIKit.Row(paint), p3 = UIKit.Row(paint);
			paintSwatches = new Image[4];
			for (int i = 0; i < 4; i++)
			{
				int slot = PaintSlots[i];
				Button b = UIKit.Button(i < 2 ? p1 : p2, TerrainPainter.LayerNames[slot], () => SetPaint(slot), "Paint this ground texture by hand (painted ground keeps its texture when you sculpt)");
				paintSwatches[i] = UIKit.Swatch(b, SlotColors[slot]);
				brushButtons[4 + i] = b;
			}
			brushButtons[8] = UIKit.Button(p3, "Auto", () => SetBrush(terraineditor.TerrainModificationAction.AutoPaint), "Auto: the ground textures itself again by height and slope");

			RectTransform brush = UIKit.Group(s, "Brush");
			sizeSlider = UIKit.Slider(brush, "Size", terraineditor.MinRadius, terraineditor.MaxRadius, terraineditor.brushRadius, SizeText,
				v => terraineditor.brushRadius = v, "Brush diameter in metres (console: ChangeWidth)");
			strengthSlider = UIKit.Slider(brush, "Strength", terraineditor.MinStrength, terraineditor.MaxStrength, terraineditor.strength, StrengthText,
				v => terraineditor.strength = v, "How fast the brush works (console: ChangeStrength)");

			RectTransform stamps = UIKit.Group(s, "Stamps");
			stampButtonsRoot = UIKit.Rect("StampButtons", stamps);
			UIKit.Vertical(stampButtonsRoot.gameObject, 4f, new RectOffset(0, 0, 0, 0));
			RectTransform stampRow = UIKit.Row(stamps, 24f, 4f, "StampSave");
			UIKit.Button(stampRow, "Save stamp...", SaveStamp, "Keep the land under the brush (as big as the brush) as a stamp of your own", -1, 24f, 12);
			UIKit.Button(stampRow, "Manage...", () => PiecesWindow.Open(true), "Rename or delete your own stamps", 80, 24f, 12).name = "Button_ManageStamps";
			TerrainStamps.Load();
			RefreshStamps();

			Tips(s, "Left mouse: use the brush \u00B7 blue plane: the sea\nRight-drag: look (+WASD fly, Q/E, wheel: speed)\nMiddle-drag: pan \u00B7 Alt+drag: orbit \u00B7 WASD: move\nSpace / C: straight up / down \u00B7 F: frame \u00B7 Shift: faster\nWheel: zoom to the cursor");
			return s;
		}

		static RectTransform BuildObjectTools(Transform panel)
		{
			RectTransform s = ToolSection(panel, "ObjectTools");

			RectTransform transform = UIKit.Group(s, "Transform");
			RectTransform t1 = UIKit.Row(transform, UIKit.RowHeight, 4f);
			gizmoButtons = new[]
			{
				UIKit.Button(t1, "Move", () => SetGizmo(TransformType.Move), "Move the selection with the arrows (key 1)", -1, UIKit.RowHeight, 13),
				UIKit.Button(t1, "Turn", () => SetGizmo(TransformType.Rotate), "Turn the selection with the rings (key 2)", -1, UIKit.RowHeight, 13),
				UIKit.Button(t1, "Scale", () => SetGizmo(TransformType.Scale), "Resize the selection (key 3)", -1, UIKit.RowHeight, 13),
				UIKit.Button(t1, "All", () => SetGizmo(TransformType.All), "Move, turn and resize at once (key 4)", -1, UIKit.RowHeight, 13),
			};

			RectTransform sel = UIKit.Group(s, "Selection");
			selectionText = UIKit.Label(sel, "Nothing selected", 14, UIKit.TextMuted);
			UIKit.Size(selectionText.gameObject, -1, 18);
			RectTransform s1 = UIKit.Row(sel), s2 = UIKit.Row(sel);
			UIKit.Button(s1, "Ground", () =>
			{
				int n = PlacementOptions.DropSelectionToGround();
				DynamicIslands.Notify(n > 0 ? "Put " + n + " object(s) on the ground" : "Select objects first (Ground puts them on the terrain)", n == 0);
			}, "Put the selected objects down on the ground (with Slope on, they lean with it)");
			UIKit.Button(s1, "Duplicate", () =>
			{
				int n = PlacementOptions.DuplicateSelection();
				DynamicIslands.Notify(n > 0 ? "Duplicated " + n + " object(s)" : "Select objects first", n == 0);
			}, "Copy the selected objects next to them (Ctrl+D)");
			UIKit.Button(s2, "Deselect", () => { if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.ClearTargets(); }, "Clear the selection");
			Button del = UIKit.Button(s2, "Delete", () => { if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.DeleteSelection(); }, "Delete the selected objects (Delete key; Ctrl+Z brings them back)");
			UIKit.DangerButton(del);
			RectTransform s3 = UIKit.Row(sel, 26f);
			UIKit.Button(s3, "Save as group...", SaveGroup, "Keep the selected objects (a hut with its furniture, a camp...) as a group in \"My groups\", to place again on any island", -1, 26f, 13);
			UIKit.Button(s3, "Manage...", () => PiecesWindow.Open(false), "Rename or delete your saved groups", 86, 26f, 13).name = "Button_ManageGroups";

			// The selected object's settings (creature editor, note, colour) appear here, in place of Placing and the tips
			ObjectInspector.Build(s);

			RectTransform place = UIKit.Group(s, "Placing");
			placingGroup = place;
			RectTransform o1 = UIKit.Row(place);
			randomButton = UIKit.Button(o1, "Random", () => { PlacementOptions.RandomTurnAndSize = !PlacementOptions.RandomTurnAndSize; RefreshOptions(); }, "On: every placed object gets a random turn and size (natural-looking groups)");
			slopeButton = UIKit.Button(o1, "Slope", () => { PlacementOptions.AlignToSlope = !PlacementOptions.AlignToSlope; RefreshOptions(); }, "On: objects lean with the ground instead of standing straight up");
			gridButton = UIKit.Button(o1, "Grid", () => { PlacementOptions.SnapToGrid = !PlacementOptions.SnapToGrid; RefreshOptions(); }, "On: Raft's 1.5 m building grid and 90\u00B0 turns, for huts and rafts of Raft blocks");

			objectTips = Tips(s, "Pick an object on the right, click the ground\nShift+click: keep placing \u00B7 Q/E: turn \u00B7 [ ]: size \u00B7 Esc: cancel\nClick an object to select it \u00B7 Shift+click: add to the selection\nSelect one object to edit its settings here\nSpace / C: camera straight up / down").gameObject;
			return s;
		}

		static RectTransform BuildIslandTools(Transform panel)
		{
			RectTransform s = ToolSection(panel, "IslandTools");

			RectTransform island = UIKit.Group(s, "Island");
			RectTransform styleRow = UIKit.Row(island, UIKit.RowHeight, 4f);
			UIKit.Size(UIKit.Label(styleRow, "Style", 14, UIKit.TextMuted).gameObject, 62);
			UIKit.Button(styleRow, "\u25C4", () => StepStyle(-1), "Previous style", 32);
			Button style = DropList.Make(styleRow, "Drop_IslandStyle", Enumerable.Range(0, TerrainPainter.Styles.Length).Select(i => new DropList.Option(i.ToString(), TerrainPainter.StyleName(i))).ToList(), "0",
				v => { int to = int.Parse(v); IslandSettingsUndo.Change(() => DynamicIslands.SetEditorStyle(to)); DynamicIslands.Notify("Island style: " + TerrainPainter.StyleName(to)); },
				-1, "Island style: the ground textures and the generator's objects (saved with the island) - click for the list", UIKit.RowHeight, 14);
			styleText = UIKit.LabelOf(style);
			styleText.alignment = TextAnchor.MiddleCenter;
			styleDrop = style.GetComponent<DropdownButton>();
			UIKit.Size(style.gameObject, -1, -1, 1);
			UIKit.Button(styleRow, "\u25BA", () => StepStyle(1), "Next style", 32);

			RectTransform heightRow = UIKit.Row(island, UIKit.RowHeight, 4f);
			UIKit.Size(UIKit.Label(heightRow, "Height", 14, UIKit.TextMuted).gameObject, 62);
			elevationField = UIKit.Field(heightRow, "0", "0", UIKit.RowHeight, "Metres above the sea the island floats at in a world: 60 = flying, -30 = under water, 0 = normal");
			elevationField.contentType = InputField.ContentType.DecimalNumber;
			elevationField.characterLimit = 6;
			elevationField.onEndEdit.AddListener(v => SetElevation(v));
			UIKit.Size(UIKit.Label(heightRow, "m", 14, UIKit.TextMuted).gameObject, 18);
			RectTransform presets = UIKit.Row(island, 26f, 4f);
			presetSea = UIKit.Button(presets, "At sea", () => SetElevation("0"), "A normal island (height 0)", -1, 26, 12);
			presetFlying = UIKit.Button(presets, "Flying", () => SetElevation("60"), "Floats above the sea (60 m; type another height above)", -1, 26, 12);
			presetSunken = UIKit.Button(presets, "Sunken", () => SetElevation("-30"), "Lies under water (-30 m; type another depth above)", -1, 26, 12);

			RectTransform gen = UIKit.Group(s, "Generate");
			UIKit.Label(gen, "Make a whole island from a seed: size, height, peaks and objects. Ctrl+Z brings back what you had.", 13, UIKit.TextMuted);
			UIKit.Button(gen, "Open the generator...", GeneratorWindow.Open, "Opens the island generator");

			BuildInfoTools(s);

			RectTransform info = UIKit.Group(s, "About this island");
			statsText = UIKit.Label(info, "", 13, UIKit.TextColor);

			Tips(s, "The editor shows every island at sea level;\nthe height only applies in a world.");
			return s;
		}

		static Text Tips(Transform parent, string text)
		{
			Text t = UIKit.Label(parent, text, 13, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Italic, "Tips");
			t.lineSpacing = 1.1f;
			return t;
		}

		static RectTransform stampButtonsRoot;
		static readonly List<Button> stampButtons = new List<Button>();

		/// <summary>One button per stamp (built-in and saved), three to a row.</summary>
		public static void RefreshStamps()
		{
			if (stampButtonsRoot == null) return;
			foreach (Transform child in stampButtonsRoot) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
			stampButtons.Clear();
			RectTransform row = null;
			for (int i = 0; i < TerrainStamps.All.Count; i++)
			{
				if (i % 3 == 0) row = UIKit.Row(stampButtonsRoot, 24f, 4f);
				int index = i;
				TerrainStamps.Stamp st = TerrainStamps.All[i];
				stampButtons.Add(UIKit.Button(row, st.Name, () => SetStamp(index),
					(st.BuiltIn ? "Stamp a " + st.Name.ToLowerInvariant() : "Your stamp '" + st.Name + "'") + ": click the ground; the Size slider sets how big, Q/E turn it", -1, 24f, 12));
			}
			HighlightStamps();
		}

		internal static void SetStamp(int index)
		{
			TerrainStamps.Selected = index;
			SetBrush(terraineditor.TerrainModificationAction.Stamp);
		}

		static void HighlightStamps()
		{
			bool on = terraineditor.modificationAction == terraineditor.TerrainModificationAction.Stamp;
			for (int i = 0; i < stampButtons.Count; i++) UIKit.SetActive(stampButtons[i], on && i == TerrainStamps.Selected);
		}

		static void SaveStamp()
		{
			if (!terraineditor.LastPoint.HasValue) { DynamicIslands.Notify("Point the brush at the land to keep first, then click Save stamp", true); return; }
			Vector3 at = terraineditor.LastPoint.Value;
			float radius = terraineditor.brushRadius;
			TextPromptWindow.Open("Save stamp", "The land in the brush circle (" + (radius * 2f).ToString("F0") + " m across, at the last place the brush was) becomes a stamp to put down anywhere.", "my stamp", name =>
			{
				TerrainStamps.Save(TerrainStamps.Capture(terraineditor.terrain, at, radius, name));
				TerrainStamps.Load();
				RefreshStamps();
				SetStamp(TerrainStamps.All.FindIndex(s => s.Name == name && !s.BuiltIn));
				DynamicIslands.Notify("Saved stamp '" + name + "': click the ground to put it down");
			});
		}

		/// <summary>Selection's "Save as group...": asks a name, saves the selected objects as a group and adds it to "My groups".</summary>
		static void SaveGroup()
		{
			TransformGizmo g = DynamicIslands.EditorGizmoHandler;
			List<EditorGameObject> sel = g == null ? new List<EditorGameObject>() : g.SelectedRoots.Where(t => t != null).Select(t => t.GetComponent<EditorGameObject>()).Where(e => e != null).ToList();
			if (sel.Count == 0) { DynamicIslands.Notify("Select the objects for the group first (Shift+click adds to the selection)", true); return; }
			TextPromptWindow.Open("Save as group", sel.Count + " object(s) become a group in \"My groups\" on the object list, to place again on any island. Their settings (creatures, notes, loot, colours) come along.",
				"my group", name =>
				{
					bool exists = GroupLibrary.Saved().Contains(name, StringComparer.OrdinalIgnoreCase);
					int n = GroupLibrary.Save(name, sel);
					DynamicIslands.instance.StartCoroutine(GroupLibrary.Register(name));
					DynamicIslands.Notify((exists ? "Replaced" : "Saved") + " group '" + name + "' (" + n + " objects): find it under \"My groups\"");
				});
		}

		static RectTransform placingGroup;
		static GameObject objectTips;
		static InputField infoTitleField, infoAuthorField, infoTextField, infoRegrowField;
		static Text questText;
		static Button infoLevelsOff, infoLevelsOn;

		/// <summary>The Island tab's "Shown to players" group: the island's name, author and a short description (IslandProps).</summary>
		static void BuildInfoTools(Transform s)
		{
			RectTransform g = UIKit.Group(s, "Shown to players");
			infoTitleField = UIKit.Field(g, "Island name (e.g. Skull Rock)", "", 28f, "Shown as a banner when players arrive at the island in a world");
			infoTitleField.characterLimit = 48;
			infoAuthorField = UIKit.Field(g, "Made by (your name)", "", 28f, "Shown under the island's name");
			infoAuthorField.characterLimit = 40;
			infoTextField = UIKit.TextArea(g, "A short welcome or description...", 64f, "Shown with the banner (a line or two)");
			infoTextField.characterLimit = 240;
			RectTransform rules = UIKit.Group(s, "Rules");
			RectTransform regrow = UIKit.Row(rules, 28f, 4f);
			UIKit.Label(regrow, "Things come back after", 13, UIKit.TextMuted);
			infoRegrowField = UIKit.Field(regrow, "default", "", 28f, "In-game days until chopped trees, picked items, killed or caught animals and looted chests come back on this island. Empty = the world's setting (spawnpool.txt), 0 = never");
			UIKit.Size(infoRegrowField.gameObject, 58, 28);
			infoRegrowField.contentType = InputField.ContentType.IntegerNumber;
			infoRegrowField.characterLimit = 3;
			UIKit.Size(UIKit.Label(regrow, "days", 13, UIKit.TextMuted).gameObject, 34);
			infoRegrowField.onEndEdit.AddListener(v => { int d; SetInfo(IslandProps.RegrowDays, int.TryParse(v, out d) ? Mathf.Clamp(d, 0, 999).ToString() : ""); RefreshInfo(); });
			// (what the number means, under it: the empty field read "Things come back after world days")
			infoRegrowText = UIKit.Label(rules, "", 11, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Italic, "RegrowMeaning");
			infoRegrowText.horizontalOverflow = HorizontalWrapMode.Wrap;
			RectTransform levels = UIKit.Row(rules, 26f, 4f, "LevelUp");
			UIKit.Label(levels, "Level up system", 13, UIKit.TextMuted);
			infoLevelsOff = UIKit.Button(levels, "Off", () => { SetInfo(IslandProps.Levels, ""); RefreshInfo(); }, "No levels: Raft as usual", 50, 26f, 12);
			infoLevelsOn = UIKit.Button(levels, "On", () => { SetInfo(IslandProps.Levels, "on"); RefreshInfo(); }, "Players level up: hitting monsters gives EXP, every level gives stat points (K in a world)", 50, 26f, 12);
			UIKit.Help(levels, "The level up system: once this island is in a world, players there earn EXP by hitting monsters (anywhere in that world: the island's animals, Bruce, Raft's own), shown over the monster (+5). " +
				"Level 2 takes about 3 shark kills, level 3 about 5 more, level 4 about 10 more, then 5 more each level. Every level gives " + LevelRules.PointsPerLevel + " stat points to spend on the stats page (" + PlayerLevels.Key + "): walk, run and swim speed, jump height, damage, health, hunger, thirst and oxygen, +1% a point, at most " + LevelRules.MaxPoints + " points each. " +
				"The levels are kept with the world, for every player.");
			RectTransform quest = UIKit.Group(s, "Quest");
			questText = UIKit.Label(quest, "", 12, UIKit.TextMuted);
			UIKit.Button(quest, "Edit quest...", QuestEditorWindow.Open, "A quest for this island: steps (go to a zone, read a note, open a chest, defeat or catch animals) and a reward", -1, 26f, 13);
			UIKit.Button(quest, "Islands it brings...", WorldPlanWindow.OpenIsland, "Rules of this island: when its quest (or a step, or a zone) is done, a new island appears near it - in any world", -1, 26f, 13);
			UIKit.Button(quest, "Island events...", BehaviourWindow.OpenIsland, "What happens when players first come to the island, and when its quest is done (show or open things, messages, items, sounds, signals)", -1, 26f, 13);
			UIKit.Button(quest, "Story items...", StoryItemsWindow.Open, "Keys, map pieces, logs...: items of the story the crew keeps (Raft's notebook for a main story island, else the journal); chests and actions give them, events can ask for them. Also ready story sets (a locked door with its key, a trail of notes)", -1, 26f, 13);
			infoTitleField.onEndEdit.AddListener(v => SetInfo(IslandProps.Title, v));
			infoAuthorField.onEndEdit.AddListener(v => SetInfo(IslandProps.Author, v));
			infoTextField.onEndEdit.AddListener(v => SetInfo(IslandProps.Description, v));
		}

		static void SetInfo(string key, string value)
		{
			value = (value ?? "").Trim();
			// (an undo step, and an unsaved change for the autosave)
			IslandSettingsUndo.Change(() =>
			{
				if (value.Length == 0) DynamicIslands.currentIslandProps.Remove(key);
				else DynamicIslands.currentIslandProps[key] = value;
			});
		}

		static void RefreshInfo()
		{
			if (infoTitleField == null) return;
			infoTitleField.text = ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.Title);
			infoAuthorField.text = ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.Author);
			infoTextField.text = ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.Description);
			infoRegrowField.text = ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.RegrowDays);
			if (infoRegrowText != null)
			{
				string r = infoRegrowField.text;
				infoRegrowText.text = r.Length == 0 ? "Empty: as the world decides (" + CustomIslandSpawner.RegrowDays + " days unless its host changed it). Chopped trees, picked items, animals and looted chests come back." :
					r == "0" ? "0: never - what players take or kill here stays gone." :
					"Chopped trees, picked items, animals and looted chests come back after " + r + " in-game day" + (r == "1" ? "" : "s") + ". Empty = the world's setting, 0 = never.";
			}
			bool levelsOn = PlayerLevels.IsOn(DynamicIslands.currentIslandProps);
			if (infoLevelsOn != null) { UIKit.SetActive(infoLevelsOn, levelsOn); UIKit.SetActive(infoLevelsOff, !levelsOn); }
			IslandQuest q = IslandQuest.From(DynamicIslands.currentIslandProps);
			int brings = WorldDirector.RulesFromProps(DynamicIslands.currentIslandProps).Count;
			if (questText != null) questText.text = (q.Exists ? "<color=#eddeba>" + q.ShownTitle + "</color>: " + q.Steps.Count + " step(s)" + (q.Reward.Length > 0 ? ", with a reward" : "") : "<i>No quest yet.</i>") +
				(brings > 0 ? "\nBrings " + brings + " island(s) into a world" : "");
		}

		#endregion

		#region State

		public static TAB CurrentTab { get { return tabs != null ? tabs.SelectedTab : TAB.TerrainEdit; } }

		public static void SetTab(TAB tab)
		{
			if (tabs != null) tabs.UpdateTabSelection((int)tab);
		}

		static void OnTabChanged(TAB tab)
		{
			if (terrainTools != null) terrainTools.gameObject.SetActive(tab == TAB.TerrainEdit);
			if (objectTools != null) objectTools.gameObject.SetActive(tab == TAB.ObjectPlace);
			if (islandTools != null) islandTools.gameObject.SetActive(tab == TAB.Island);
			if (browserPanel != null) browserPanel.gameObject.SetActive(tab == TAB.ObjectPlace);
			if (tabButtons != null) for (int i = 0; i < tabButtons.Length; i++) UIKit.SetActive(tabButtons[i], i == (int)tab);
			// Leaving the object tab drops the selection so the gizmo doesn't block sculpting, and cancels placing
			if (tab != TAB.ObjectPlace)
			{
				if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.ClearTargets(false);
				ObjectPlacer placer = UnityEngine.Object.FindObjectOfType<ObjectPlacer>();
				if (placer != null) UnityEngine.Object.Destroy(placer.gameObject);
				if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.placingObject = false;
			}
			if (tab == TAB.Island) RefreshIsland();
		}

		static void RefreshOptions()
		{
			UIKit.SetActive(randomButton, PlacementOptions.RandomTurnAndSize);
			UIKit.SetActive(slopeButton, PlacementOptions.AlignToSlope);
			UIKit.SetActive(gridButton, PlacementOptions.SnapToGrid);
		}

		/// <summary>Shows the current style and names the paint buttons after its textures.</summary>
		public static void RefreshStyle()
		{
			int style = DynamicIslands.currentStyle;
			if (styleText != null) styleText.text = TerrainPainter.StyleName(style);
			if (styleDrop != null) styleDrop.Value = style.ToString();
			if (brushButtons != null)
				for (int i = 0; i < PaintSlots.Length; i++)
					if (brushButtons[4 + i] != null) UIKit.LabelOf(brushButtons[4 + i]).text = TerrainPainter.SlotLabel(style, PaintSlots[i]);
			if (paintSwatches != null)
			{
				TerrainPainter.Style st = TerrainPainter.Styles[Mathf.Clamp(style, 0, TerrainPainter.Styles.Length - 1)];
				for (int i = 0; i < PaintSlots.Length; i++)
				{
					Color c = SlotColors[PaintSlots[i]];
					if (st.Tints != null && PaintSlots[i] < st.Tints.Length) c *= st.Tints[PaintSlots[i]];
					if (paintSwatches[i] != null) paintSwatches[i].color = new Color(c.r, c.g, c.b, 1f);
				}
			}
		}

		/// <summary>Island name, elevation and statistics after loading, saving or a new island.</summary>
		public static void RefreshIsland()
		{
			if (islandNameText != null) islandNameText.text = DynamicIslands.currentIslandName + (File.Exists(IslandSpawner.PathFor(DynamicIslands.currentIslandName)) ? "" : "  <size=11><color=#b89e70>(not saved yet)</color></size>");
			if (elevationField != null && !elevationField.isFocused) elevationField.text = DynamicIslands.currentElevation.ToString(CultureInfo.InvariantCulture);
			RefreshPresets();
			RefreshInfo();
			RefreshStats();
		}

		static void RefreshStats()
		{
			if (statsText == null) return;
			GameObject placed = GameObject.Find("PlacedObjects");
			int objects = placed != null ? placed.GetComponentsInChildren<EditorGameObject>(false).Length : 0;
			int more = PlaceableCatalog.Browse().Sum(c => c.Value.Count(e => !e.Loaded));
			statsText.text = "Objects: " + objects + "\nIn a world: " + IslandSpawner.DescribeElevation(DynamicIslands.currentElevation) +
				"\nObject list: " + PlaceableCatalog.Names.Count() + " loaded" + (more > 0 ? ", " + more + " more from Raft's islands" : "");
		}

		/// <summary>The height preset matching the island's height is lit: at sea (0), flying (above), sunken (below).</summary>
		static void RefreshPresets()
		{
			if (presetSea == null) return;
			float h = DynamicIslands.currentElevation;
			UIKit.SetActive(presetSea, Mathf.Approximately(h, 0f));
			UIKit.SetActive(presetFlying, h > 0.01f);
			UIKit.SetActive(presetSunken, h < -0.01f);
		}

		static void StepStyle(int step)
		{
			int n = TerrainPainter.Styles.Length;
			IslandSettingsUndo.Change(() => DynamicIslands.SetEditorStyle(((DynamicIslands.currentStyle + step) % n + n) % n));
			int style = DynamicIslands.currentStyle;
			DynamicIslands.Notify("Island style: " + TerrainPainter.StyleName(style) + (TerrainPainter.HasStyle(style) ? "" : " (textures not loaded; showing tropical)"));
		}

		static void SetElevation(string text)
		{
			float e, before = DynamicIslands.currentElevation;
			float to = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out e) ? Mathf.Clamp(e, IslandSpawner.MinElevation, IslandSpawner.MaxElevation) : 0f;
			IslandSettingsUndo.Change(() => DynamicIslands.currentElevation = to);
			if (elevationField != null) elevationField.text = DynamicIslands.currentElevation.ToString(CultureInfo.InvariantCulture);
			RefreshPresets();
			RefreshStats();
			if (DynamicIslands.currentElevation != before) DynamicIslands.Notify("In a world this island will be " + IslandSpawner.DescribeElevation(DynamicIslands.currentElevation) + " (saved with the island)");
		}

		static float newClickedAt = -10f;

		static void ConfirmNew()
		{
			if (Time.unscaledTime - newClickedAt > 4f)
			{
				newClickedAt = Time.unscaledTime;
				DynamicIslands.Notify("Click New again to start an empty island (unsaved changes are lost)", true);
				return;
			}
			newClickedAt = -10f;
			DynamicIslands.NewIsland();
		}

		static void SetPaint(int layer)
		{
			terraineditor.paintLayer = layer;
			SetBrush(terraineditor.TerrainModificationAction.PaintLayer);
		}

		static void SetBrush(terraineditor.TerrainModificationAction action)
		{
			terraineditor.modificationAction = action;
			int active;
			switch (action)
			{
				case terraineditor.TerrainModificationAction.Raise: active = 0; break;
				case terraineditor.TerrainModificationAction.Lower: active = 1; break;
				case terraineditor.TerrainModificationAction.Flatten: active = 2; break;
				case terraineditor.TerrainModificationAction.Smooth: active = 3; break;
				case terraineditor.TerrainModificationAction.AutoPaint: active = 8; break;
				case terraineditor.TerrainModificationAction.PaintLayer: active = 4 + Array.IndexOf(PaintSlots, terraineditor.paintLayer); break;
				default: active = -1; break;
			}
			Highlight(brushButtons, active);
			HighlightStamps();
		}

		static TransformType shownGizmoType = TransformType.Move;
		static terraineditor.TerrainModificationAction shownAction;
		static int shownPaintLayer = -1;
		static int lastSelectionCount = -1;
		static Transform lastSelectionFirst;
		static bool elevationTyping;
		static float nextStats;

		/// <summary>Every frame: keeps highlights in sync with keys and console commands, the status bar, shortcuts.</summary>
		public static void Tick()
		{
			TransformGizmo g = DynamicIslands.EditorGizmoHandler;
			if (g != null && g.transformType != shownGizmoType) SetGizmo(g.transformType);
			if (terraineditor.modificationAction != shownAction || terraineditor.paintLayer != shownPaintLayer)
			{
				shownAction = terraineditor.modificationAction; shownPaintLayer = terraineditor.paintLayer;
				SetBrush(shownAction);
			}

			// Typing a height must not fly the camera (WASD) or trigger shortcuts
			ObjectInspector.Tick();
			bool inspecting = ObjectInspector.Visible;
			if (placingGroup != null && placingGroup.gameObject.activeSelf == inspecting) placingGroup.gameObject.SetActive(!inspecting);
			if (objectTips != null && objectTips.activeSelf == inspecting) objectTips.SetActive(!inspecting);

			if (elevationField != null)
			{
				bool typing = elevationField.isFocused || (infoTitleField != null && (infoTitleField.isFocused || infoAuthorField.isFocused || infoTextField.isFocused || infoRegrowField.isFocused));
				if (typing) EditorInput.IsTyping = true;
				else if (elevationTyping) EditorInput.IsTyping = false;
				elevationTyping = typing;
			}

			if (!EditorInput.IsTyping)
			{
				if (Input.GetKeyDown(KeyCode.F1)) SetTab(TAB.TerrainEdit);
				else if (Input.GetKeyDown(KeyCode.F2)) SetTab(TAB.ObjectPlace);
				else if (Input.GetKeyDown(KeyCode.F3)) SetTab(TAB.Island);
				if (EditorInput.Ctrl && Input.GetKeyDown(KeyCode.D) && tabs != null && tabs.SelectedTab == TAB.ObjectPlace) PlacementOptions.DuplicateSelection();
			}

			int selected = g != null ? g.SelectedRoots.Count(t => t != null) : 0;
			Transform first = selected > 0 ? g.SelectedRoots.First(t => t != null) : null;
			if ((selected != lastSelectionCount || first != lastSelectionFirst) && selectionText != null)
			{
				lastSelectionCount = selected;
				lastSelectionFirst = first; // (one object selected after another: the name must change too)
				EditorGameObject info = first != null ? first.GetComponent<EditorGameObject>() : null;
				selectionText.text = selected == 0 ? "Nothing selected" : selected == 1 && info != null ? PlaceableCatalog.DisplayName(info.GameObjectName) : selected + " objects selected";
				selectionText.color = selected == 0 ? UIKit.TextMuted : UIKit.Accent;
			}

			if (hintText != null) hintText.text = Time.unscaledTime < flashUntil ? flash : hoverHint ?? ToolHint();
			if (cameraText != null && Camera.main != null)
			{
				Vector3 c = Camera.main.transform.position;
				cameraText.text = "Camera  X " + c.x.ToString("F0") + "   Y " + (c.y - DynamicIslands.EditorWaterLevel).ToString("F0") + " above sea   Z " + c.z.ToString("F0");
			}
			if (Time.unscaledTime > nextStats && tabs != null && tabs.SelectedTab == TAB.Island) { nextStats = Time.unscaledTime + 1f; RefreshStats(); }
		}

		static string ToolHint()
		{
			if (tabs == null) return "";
			ObjectPlacer placer = tabs.SelectedTab == TAB.ObjectPlace ? UnityEngine.Object.FindObjectOfType<ObjectPlacer>() : null;
			if (placer != null) return "Placing " + PlaceableCatalog.DisplayName(placer.GameObjectName) + ": click the ground \u00B7 Shift+click keeps placing \u00B7 Q/E turn \u00B7 [ ] size \u00B7 Esc cancels";
			switch (tabs.SelectedTab)
			{
				case TAB.TerrainEdit:
					string tool = terraineditor.modificationAction == terraineditor.TerrainModificationAction.PaintLayer ? "Painting " + TerrainPainter.SlotLabel(DynamicIslands.currentStyle, terraineditor.paintLayer)
						: terraineditor.modificationAction == terraineditor.TerrainModificationAction.AutoPaint ? "Auto texturing" : terraineditor.modificationAction.ToString();
					if (terraineditor.modificationAction == terraineditor.TerrainModificationAction.Stamp && TerrainStamps.Current != null)
						return "Stamp " + TerrainStamps.Current.Name + ": click the ground \u00B7 Size = how big (" + (terraineditor.brushRadius * 2f).ToString("F0") + " m) \u00B7 Q/E turn it (" + TerrainStamps.Rotation.ToString("F0") + "\u00B0) \u00B7 Ctrl+Z undoes";
					return tool + ": hold the left mouse button on the ground \u00B7 Ctrl+Z undoes a stroke";
				case TAB.ObjectPlace: return "Pick an object from the list on the right, then click where it goes \u00B7 1-4 change the gizmo \u00B7 X world/own axes \u00B7 P turn around each object/the middle \u00B7 Ctrl while dragging snaps (0.25 m, 15\u00B0) \u00B7 Delete removes the selection";
				default: return "Island settings are saved with the island (Ctrl+S)";
			}
		}

		static void SetGizmo(TransformType type)
		{
			shownGizmoType = type;
			if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.transformType = type;
			Highlight(gizmoButtons, type == TransformType.Move ? 0 : type == TransformType.Rotate ? 1 : type == TransformType.Scale ? 2 : type == TransformType.All ? 3 : -1);
		}

		static void Highlight(Button[] buttons, int active)
		{
			if (buttons == null) return;
			for (int i = 0; i < buttons.Length; i++) UIKit.SetActive(buttons[i], i == active);
		}

		#endregion
	}
}
