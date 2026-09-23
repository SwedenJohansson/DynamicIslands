using System;
using RuntimeGizmos;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Wires the editor UI from the Unity bundle (TerrainEdit_Canvas prefab) to the mod code.
	/// The bundle only contains layout; every button is found by its path in the hierarchy:
	///   Toolbar/TabSelector/Terrain Tool/Paint - Button              -> Terrain tab
	///   Toolbar/TabSelector/Terrain Tool/Paint - Button/Brush Tools   -> Raise / Lower / Flatten / Smooth
	///   Toolbar/TabSelector/Object Tool/Object - Button               -> Objects tab
	///   Toolbar/TabSelector/Object Tool/Object - Button/Object Tools  -> Move / Rotate / Scale / Delete
	///   Toolbar/ToolList/TerrainTool/BrushSizeSlider, BrushStrengthSlider
	/// </summary>
	public static class EditorUI
	{
		static readonly Color ActiveText = new Color(1f, 0.85f, 0.2f);
		static readonly Color NormalText = new Color(0.2f, 0.2f, 0.2f);

		static Text[] brushLabels;
		static readonly System.Collections.Generic.List<Action> sliderRefreshers = new System.Collections.Generic.List<Action>();
		static Text[] objectLabels;

		/// <summary>Updates the sliders after the brush was changed elsewhere (console commands).</summary>
		public static void RefreshSliders()
		{
			foreach (Action refresh in sliderRefreshers) refresh();
		}

		public static void Setup(Transform canvas, TabSelector tabs)
		{
			sliderRefreshers.Clear();
			Transform toolbar = canvas.Find("Toolbar");
			Transform terrainTab = toolbar.Find("TabSelector/Terrain Tool/Paint - Button");
			Transform objectTab = toolbar.Find("TabSelector/Object Tool/Object - Button");
			Transform brushTools = terrainTab != null ? terrainTab.Find("Brush Tools") : null;
			Transform objectTools = objectTab != null ? objectTab.Find("Object Tools") : null;

			// The camera position readout sits where the extra texture and Generate buttons go; move it below them
			Transform camPos = canvas.Find("CamPos");
			if (camPos != null) camPos.GetComponent<RectTransform>().anchoredPosition += new Vector2(0, -240f);

			// The navbar button (labelled "Return" in the bundle) opens the Save / Load window
			Transform islandsButton = canvas.Find("EditorNavbar/Menu - Button");
			if (islandsButton != null)
			{
				Text label = islandsButton.GetComponentInChildren<Text>(true);
				if (label != null) label.text = "Islands";
				Hook(islandsButton, IslandFilesWindow.Open);
			}

			// Tabs
			Hook(terrainTab, () => tabs.UpdateTabSelection((int)TAB.TerrainEdit));
			Hook(objectTab, () => tabs.UpdateTabSelection((int)TAB.ObjectPlace));
			tabs.TabChanged += tab =>
			{
				if (brushTools != null) brushTools.gameObject.SetActive(tab == TAB.TerrainEdit);
				if (objectTools != null) objectTools.gameObject.SetActive(tab == TAB.ObjectPlace);
				// Leaving the object tab drops the selection so the gizmo doesn't block sculpting
				if (tab == TAB.TerrainEdit && DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.ClearTargets(false);
			};

			// Terrain brush modes, then texture paint buttons (cloned from the last bundle button, stacked below it)
			if (brushTools != null)
			{
				CreatePaintButtons(brushTools, "Button (5)");
				brushLabels = new[]
				{
					SetupButton(brushTools, "RaiseButton", "Raise", () => SetBrush(terraineditor.TerrainModificationAction.Raise)),
					SetupButton(brushTools, "LowerButton", "Lower", () => SetBrush(terraineditor.TerrainModificationAction.Lower)),
					SetupButton(brushTools, "FlattenButton", "Flatten", () => SetBrush(terraineditor.TerrainModificationAction.Flatten)),
					SetupButton(brushTools, "Button (5)", "Smooth", () => SetBrush(terraineditor.TerrainModificationAction.Smooth)),
					SetupButton(brushTools, "PaintSand", "Sand", () => SetPaint(TerrainPainter.Sand)),
					SetupButton(brushTools, "PaintGrass", "Grass", () => SetPaint(TerrainPainter.Grass)),
					SetupButton(brushTools, "PaintRock", "Rock", () => SetPaint(TerrainPainter.Rock)),
					SetupButton(brushTools, "PaintSeabed", "Seabed", () => SetPaint(TerrainPainter.Seabed)),
					SetupButton(brushTools, "PaintAuto", "Auto", () => SetBrush(terraineditor.TerrainModificationAction.AutoPaint)),
				};
				SetBrush(terraineditor.modificationAction);
				Text generate = SetupButton(brushTools, GenerateButton, "Generate", GeneratorWindow.Open);
				if (generate != null) generate.color = NormalText;
				// Island style: each click moves to the next one (Tropical, Snowy, Desert, Forest, Volcanic)
				styleLabel = SetupButton(brushTools, StyleButton, "", () =>
				{
					DynamicIslands.SetEditorStyle((DynamicIslands.currentStyle + 1) % TerrainPainter.Styles.Length);
					ShowStyleNote();
				});
				if (styleLabel != null) styleLabel.color = NormalText;
				RefreshStyle();
			}

			// Object gizmo modes
			if (objectTools != null)
			{
				// Placement options below the gizmo buttons
				CloneBelow(objectTools, "Button (5)", "Button (4)", ObjectOptionButtons);
				randomLabel = SetupButton(objectTools, ObjectOptionButtons[0], "Random", () => { PlacementOptions.RandomTurnAndSize = !PlacementOptions.RandomTurnAndSize; RefreshOptions(); });
				slopeLabel = SetupButton(objectTools, ObjectOptionButtons[1], "Slope", () => { PlacementOptions.AlignToSlope = !PlacementOptions.AlignToSlope; RefreshOptions(); });
				Text ground = SetupButton(objectTools, ObjectOptionButtons[2], "Ground", () =>
				{
					int n = PlacementOptions.DropSelectionToGround();
					DynamicIslands.Notify(n > 0 ? "Put " + n + " object(s) on the ground" : "Select objects first (Ground puts them on the terrain)", n == 0);
				});
				if (ground != null) ground.color = NormalText;
				RefreshOptions();

				objectLabels = new[]
				{
					SetupButton(objectTools, "Button (2)", "Move", () => SetGizmo(TransformType.Move)),
					SetupButton(objectTools, "Button (3)", "Rotate", () => SetGizmo(TransformType.Rotate)),
					SetupButton(objectTools, "Button (4)", "Scale", () => SetGizmo(TransformType.Scale)),
					SetupButton(objectTools, "Button (5)", "Delete", () => { if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.DeleteSelection(); }),
				};
				SetGizmo(TransformType.Move);
			}

			// Brush sliders (0..1 in the bundle; mapped to metres and metres/second)
			Transform terrainPanel = toolbar.Find("ToolList/TerrainTool");
			if (terrainPanel != null)
			{
				SetupSlider(terrainPanel, "BrushSizeSlider", "BrushSizeLabel", terraineditor.MinRadius, terraineditor.MaxRadius,
					() => terraineditor.brushRadius, v => terraineditor.brushRadius = v, v => "Brush Size: " + (v * 2f).ToString("F0") + " m");
				SetupSlider(terrainPanel, "BrushStrengthSlider", "BrushStrengthLabel", terraineditor.MinStrength, terraineditor.MaxStrength,
					() => terraineditor.strength, v => terraineditor.strength = v, v => "Brush Strength: " + v.ToString("F1") + " m/s");

				// Texture painting is automatic for now; hide the placeholder button
				Transform paint = terrainPanel.Find("PaintTool");
				if (paint != null) paint.gameObject.SetActive(false);
			}
		}

		static readonly string[] ObjectOptionButtons = { "OptionRandom", "OptionSlope", "OptionGround" };
		static Text randomLabel, slopeLabel;

		/// <summary>Random / Slope light up while they're on.</summary>
		static void RefreshOptions()
		{
			if (randomLabel != null) randomLabel.color = PlacementOptions.RandomTurnAndSize ? ActiveText : NormalText;
			if (slopeLabel != null) slopeLabel.color = PlacementOptions.AlignToSlope ? ActiveText : NormalText;
		}

		/// <summary>Clones a bundle button once per name, stacked below it after a small gap (the bundle has no spare buttons).</summary>
		static void CloneBelow(Transform parent, string templateName, string aboveName, string[] names)
		{
			Transform template = parent.Find(templateName);
			Transform above = parent.Find(aboveName);
			if (template == null || parent.Find(names[0]) != null) return;
			RectTransform t = template.GetComponent<RectTransform>();
			float step = above != null ? above.GetComponent<RectTransform>().anchoredPosition.y - t.anchoredPosition.y : 35f;
			for (int i = 0; i < names.Length; i++)
			{
				GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
				clone.name = names[i];
				clone.GetComponent<RectTransform>().anchoredPosition = t.anchoredPosition - new Vector2(0, step * (i + 1) + step * 0.35f);
			}
		}

		static readonly string[] PaintButtons = { "PaintSand", "PaintGrass", "PaintRock", "PaintSeabed", "PaintAuto" };
		/// <summary>Terrain slot painted by each of the first four paint buttons.</summary>
		static readonly int[] PaintSlots = { TerrainPainter.Sand, TerrainPainter.Grass, TerrainPainter.Rock, TerrainPainter.Seabed };
		const string GenerateButton = "GenerateIsland";
		const string StyleButton = "IslandStyle";
		static Text styleLabel;

		/// <summary>Shows the current style on the Style button and names the paint buttons after its textures.</summary>
		public static void RefreshStyle()
		{
			int style = DynamicIslands.currentStyle;
			if (styleLabel != null) styleLabel.text = TerrainPainter.StyleName(style);
			if (brushLabels != null)
				for (int i = 0; i < PaintSlots.Length && 4 + i < brushLabels.Length; i++)
					if (brushLabels[4 + i] != null) brushLabels[4 + i].text = TerrainPainter.SlotLabel(style, PaintSlots[i]);
		}

		static void ShowStyleNote()
		{
			int style = DynamicIslands.currentStyle;
			string text = "Island style: " + TerrainPainter.StyleName(style) + (TerrainPainter.HasStyle(style) ? "" : " (textures not loaded; showing tropical)");
			DynamicIslands.Notify(text);
		}

		/// <summary>The bundle only has four brush buttons; clone the last one for the texture tools and the Generate button.</summary>
		static void CreatePaintButtons(Transform brushTools, string templateName)
		{
			Transform template = brushTools.Find(templateName);
			Transform above = brushTools.Find("FlattenButton");
			if (template == null || brushTools.Find(PaintButtons[0]) != null) return;
			RectTransform t = template.GetComponent<RectTransform>();
			float step = above != null ? above.GetComponent<RectTransform>().anchoredPosition.y - t.anchoredPosition.y : 35f;
			for (int i = 0; i < PaintButtons.Length; i++)
			{
				GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, brushTools);
				clone.name = PaintButtons[i];
				// A small gap separates the texture tools from the sculpt tools
				RectTransform cr = clone.GetComponent<RectTransform>();
				cr.anchoredPosition = t.anchoredPosition - new Vector2(0, step * (i + 1) + step * 0.35f);
				cr.sizeDelta = new Vector2(cr.sizeDelta.x * 1.3f, cr.sizeDelta.y); // room for style names like "Red rock"
			}
			// Generate sits below the texture tools, after another gap
			GameObject gen = UnityEngine.Object.Instantiate(template.gameObject, brushTools);
			gen.name = GenerateButton;
			RectTransform gr = gen.GetComponent<RectTransform>();
			gr.anchoredPosition = t.anchoredPosition - new Vector2(0, step * (PaintButtons.Length + 1) + step * 0.7f);
			gr.sizeDelta = new Vector2(gr.sizeDelta.x * 1.3f, gr.sizeDelta.y); // "Generate" is the longest label
			// ...and the island style right below it
			GameObject style = UnityEngine.Object.Instantiate(gen, brushTools);
			style.name = StyleButton;
			style.GetComponent<RectTransform>().anchoredPosition = gr.anchoredPosition - new Vector2(0, step);
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
				case terraineditor.TerrainModificationAction.PaintLayer:
					active = terraineditor.paintLayer == TerrainPainter.Sand ? 4 : terraineditor.paintLayer == TerrainPainter.Grass ? 5 :
						terraineditor.paintLayer == TerrainPainter.Rock ? 6 : 7;
					break;
				default: active = -1; break;
			}
			Highlight(brushLabels, active);
		}

		static TransformType shownGizmoType = TransformType.Move;

		/// <summary>Keeps the Move/Rotate/Scale highlight in sync when the type is changed with the 1/2/3/4 keys.</summary>
		public static void Tick()
		{
			TransformGizmo g = DynamicIslands.EditorGizmoHandler;
			if (g != null && g.transformType != shownGizmoType) SetGizmo(g.transformType);
		}

		static void SetGizmo(TransformType type)
		{
			shownGizmoType = type;
			if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.transformType = type;
			Highlight(objectLabels, type == TransformType.Move ? 0 : type == TransformType.Rotate ? 1 : type == TransformType.Scale ? 2 : -1);
		}

		static void Highlight(Text[] labels, int active)
		{
			if (labels == null) return;
			for (int i = 0; i < labels.Length; i++)
				if (labels[i] != null) labels[i].color = i == active ? ActiveText : NormalText;
		}

		static void Hook(Transform t, Action onClick)
		{
			if (t == null) { Debug.LogWarning("[CUSTOM ISLANDS] Editor UI: button not found"); return; }
			Button b = t.GetComponent<Button>();
			if (b == null) { Debug.LogWarning("[CUSTOM ISLANDS] Editor UI: " + t.name + " has no Button"); return; }
			b.onClick.AddListener(() => onClick());
		}

		static Text SetupButton(Transform parent, string child, string label, Action onClick)
		{
			Transform t = parent.Find(child);
			if (t == null) { Debug.LogWarning("[CUSTOM ISLANDS] Editor UI: " + parent.name + "/" + child + " not found"); return null; }
			t.gameObject.SetActive(true); // some buttons are disabled in the prefab
			Hook(t, onClick);
			Text text = t.GetComponentInChildren<Text>(true);
			if (text != null) text.text = label;
			return text;
		}

		static void SetupSlider(Transform panel, string sliderName, string labelName, float min, float max,
			Func<float> get, Action<float> set, Func<float, string> format)
		{
			Transform st = panel.Find(sliderName);
			Slider slider = st != null ? st.GetComponent<Slider>() : null;
			if (slider == null) { Debug.LogWarning("[CUSTOM ISLANDS] Editor UI: slider " + sliderName + " not found"); return; }
			Transform lt = panel.Find(labelName);
			Text label = lt != null ? lt.GetComponent<Text>() : null;

			slider.minValue = min;
			slider.maxValue = max;
			slider.SetValueWithoutNotify(get());
			if (label != null) label.text = format(get());
			sliderRefreshers.Add(() => { if (slider == null) return; slider.SetValueWithoutNotify(get()); if (label != null) label.text = format(get()); });
			slider.onValueChanged.AddListener(v =>
			{
				set(v);
				if (label != null) label.text = format(v);
			});
		}
	}
}
