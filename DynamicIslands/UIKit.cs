using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A small UI toolkit for the editor, built entirely in code: a theme (colours, font), rounded and outlined
	/// sprites generated at runtime (so no Unity bundle is needed for the look), and helpers for panels, bordered
	/// groups, buttons, toggle buttons, sliders, text fields and layouts.
	/// Every control can carry a hint that the status bar shows while the mouse is over it.
	/// </summary>
	public static class UIKit
	{
		// Theme: dark slate panels, warm accent (Raft's sunny orange), light text
		public static readonly Color PanelBg = new Color(0.075f, 0.09f, 0.115f, 0.93f);
		public static readonly Color GroupBg = new Color(0.12f, 0.14f, 0.175f, 0.95f);
		public static readonly Color GroupBorder = new Color(0.30f, 0.35f, 0.42f, 1f);
		public static readonly Color ButtonBg = new Color(0.19f, 0.22f, 0.27f, 1f);
		public static readonly Color ButtonHover = new Color(0.26f, 0.30f, 0.37f, 1f);
		public static readonly Color ButtonPressed = new Color(0.14f, 0.16f, 0.2f, 1f);
		public static readonly Color ButtonBorder = new Color(0.36f, 0.41f, 0.49f, 1f);
		public static readonly Color Accent = new Color(0.98f, 0.66f, 0.22f, 1f);
		public static readonly Color AccentText = new Color(0.1f, 0.07f, 0.03f, 1f);
		public static readonly Color TextColor = new Color(0.91f, 0.93f, 0.96f, 1f);
		public static readonly Color TextMuted = new Color(0.62f, 0.67f, 0.74f, 1f);
		public static readonly Color FieldBg = new Color(0.06f, 0.07f, 0.09f, 1f);
		public static readonly Color Danger = new Color(0.86f, 0.33f, 0.3f, 1f);
		public static readonly Color Good = new Color(0.45f, 0.82f, 0.5f, 1f);

		public const int FontSize = 15;
		public const float RowHeight = 30f;

		static Font font;
		public static Font Font
		{
			get
			{
				if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
				return font;
			}
			set { font = value; }
		}

		#region Generated sprites

		static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

		/// <summary>A filled rounded rectangle (9-sliced, so it stretches to any size).</summary>
		public static Sprite Rounded(int radius) { return RoundedSprite(radius, 0f); }

		/// <summary>Only the outline of a rounded rectangle, <paramref name="thickness"/> pixels wide.</summary>
		public static Sprite Outline(int radius, float thickness = 1.5f) { return RoundedSprite(radius, thickness); }

		static Sprite RoundedSprite(int radius, float thickness)
		{
			string key = radius + "/" + thickness;
			Sprite s;
			if (sprites.TryGetValue(key, out s) && s != null) return s;
			int size = radius * 2 + 4;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CI_Rounded" + key };
			var pixels = new Color32[size * size];
			float half = size / 2f;
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
				{
					// Signed distance to the rounded box edge (negative inside)
					float px = Mathf.Abs(x + 0.5f - half) - (half - radius);
					float py = Mathf.Abs(y + 0.5f - half) - (half - radius);
					float outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude + Mathf.Min(Mathf.Max(px, py), 0);
					float d = outside - radius;
					float a = Mathf.Clamp01(0.5f - d);
					if (thickness > 0f) a *= Mathf.Clamp01(0.5f + d + thickness);
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
				}
			tex.SetPixels32(pixels);
			tex.Apply(false, true);
			float b = radius + 1;
			s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
			s.name = tex.name;
			sprites[key] = s;
			return s;
		}

		#endregion

		#region Layout basics

		/// <summary>The component, added if missing (Unity objects must not be null-coalesced with ??).</summary>
		public static T Ensure<T>(GameObject go) where T : Component
		{
			T c = go.GetComponent<T>();
			return c != null ? c : go.AddComponent<T>();
		}

		public static RectTransform Rect(string name, Transform parent)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.layer = 5; // UI
			go.transform.SetParent(parent, false);
			return (RectTransform)go.transform;
		}

		public static void Stretch(RectTransform r, float left = 0, float right = 0, float top = 0, float bottom = 0)
		{
			r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
			r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
		}

		/// <summary>Anchors a rect to a corner/edge: anchor and pivot (0..1 each), position relative to the anchor, size.</summary>
		public static void Anchor(RectTransform r, Vector2 anchor, Vector2 position, Vector2 size)
		{
			r.anchorMin = r.anchorMax = r.pivot = anchor;
			r.anchoredPosition = position;
			r.sizeDelta = size;
		}

		public static Image Background(GameObject go, Color color, int radius = 8)
		{
			Image img = Ensure<Image>(go);
			img.sprite = radius > 0 ? Rounded(radius) : null;
			img.type = Image.Type.Sliced;
			img.color = color;
			return img;
		}

		/// <summary>An outline drawn over a control (does not catch clicks).</summary>
		public static Image Border(Transform target, Color color, int radius = 8, float thickness = 1.5f)
		{
			RectTransform r = Rect("Border", target);
			Stretch(r);
			r.SetAsLastSibling();
			var img = r.gameObject.AddComponent<Image>();
			img.sprite = Outline(radius, thickness);
			img.type = Image.Type.Sliced;
			img.color = color;
			img.raycastTarget = false;
			r.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; // never takes part in a parent's layout
			return img;
		}

		public static VerticalLayoutGroup Vertical(GameObject go, float spacing, RectOffset padding, bool fitHeight = false)
		{
			var v = Ensure<VerticalLayoutGroup>(go);
			v.spacing = spacing; v.padding = padding ?? new RectOffset();
			v.childControlWidth = true; v.childControlHeight = true;
			v.childForceExpandWidth = true; v.childForceExpandHeight = false;
			if (fitHeight) (Ensure<ContentSizeFitter>(go)).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			return v;
		}

		public static HorizontalLayoutGroup Horizontal(GameObject go, float spacing, RectOffset padding = null)
		{
			var h = Ensure<HorizontalLayoutGroup>(go);
			h.spacing = spacing; h.padding = padding ?? new RectOffset();
			h.childControlWidth = true; h.childControlHeight = true;
			h.childForceExpandWidth = true; h.childForceExpandHeight = true;
			h.childAlignment = TextAnchor.MiddleLeft;
			return h;
		}

		/// <summary>
		/// Layout size of a control. A given width or height is fixed (it doesn't grow); without a width the control
		/// takes its share of the free space in a row.
		/// </summary>
		public static LayoutElement Size(GameObject go, float preferredWidth = -1, float preferredHeight = -1, float flexibleWidth = -1)
		{
			var le = Ensure<LayoutElement>(go);
			if (preferredWidth >= 0) { le.preferredWidth = preferredWidth; le.minWidth = preferredWidth; }
			if (preferredHeight >= 0) { le.preferredHeight = preferredHeight; le.minHeight = preferredHeight; le.flexibleHeight = 0; }
			le.flexibleWidth = flexibleWidth >= 0 ? flexibleWidth : preferredWidth >= 0 ? 0 : 1;
			return le;
		}

		/// <summary>A row that lays its children out side by side (equal widths unless they say otherwise).</summary>
		public static RectTransform Row(Transform parent, float height = RowHeight, float spacing = 6f, string name = "Row")
		{
			RectTransform r = Rect(name, parent);
			Horizontal(r.gameObject, spacing).childForceExpandWidth = false; // fixed-width controls stay fixed, the others share the rest
			Size(r.gameObject, -1, height);
			return r;
		}

		#endregion

		#region Controls

		public static Text Label(Transform parent, string text, int size = FontSize, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, string name = "Label")
		{
			RectTransform r = Rect(name, parent);
			var t = r.gameObject.AddComponent<Text>();
			t.font = Font; t.fontSize = size; t.color = color ?? TextColor; t.alignment = anchor; t.fontStyle = style;
			t.text = text; t.supportRichText = true; t.raycastTarget = false;
			t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
			Ensure<LayoutElement>(r.gameObject).flexibleWidth = 1; // shares a row's free space
			return t;
		}

		/// <summary>A panel: rounded dark background, vertical layout.</summary>
		public static RectTransform Panel(Transform parent, string name, RectOffset padding = null, float spacing = 8f, bool fitHeight = true)
		{
			RectTransform r = Rect(name, parent);
			Background(r.gameObject, PanelBg, 10);
			Border(r, new Color(1f, 1f, 1f, 0.07f), 10, 1f);
			Vertical(r.gameObject, spacing, padding ?? new RectOffset(10, 10, 10, 10), fitHeight);
			return r;
		}

		/// <summary>
		/// A bordered group of related controls with a small title on top, e.g. "SCULPT". Returns the content area
		/// (vertical layout) to add rows to.
		/// </summary>
		public static RectTransform Group(Transform parent, string title, string name = null)
		{
			RectTransform box = Rect(name ?? ("Group_" + title), parent);
			Background(box.gameObject, GroupBg, 8);
			Vertical(box.gameObject, 6f, new RectOffset(8, 8, 6, 8));
			if (!string.IsNullOrEmpty(title))
			{
				Text t = Label(box, title.ToUpperInvariant(), 12, TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
				Size(t.gameObject, -1, 16);
			}
			Border(box, GroupBorder, 8, 1.5f);
			return box;
		}

		/// <summary>A button: rounded, outlined, hover/press colours, optional hint for the status bar.</summary>
		public static Button Button(Transform parent, string label, Action onClick, string hint = null, float width = -1, float height = RowHeight, int fontSize = FontSize)
		{
			RectTransform r = Rect("Button_" + label, parent);
			Image img = Background(r.gameObject, Color.white, 6);
			var b = r.gameObject.AddComponent<Button>();
			b.targetGraphic = img;
			ColorBlock cb = b.colors;
			cb.normalColor = ButtonBg; cb.highlightedColor = ButtonHover; cb.pressedColor = ButtonPressed;
			cb.selectedColor = ButtonBg; cb.disabledColor = new Color(ButtonBg.r, ButtonBg.g, ButtonBg.b, 0.45f);
			cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
			b.colors = cb;
			// Keyboard focus would keep the "selected" look and steal Space/Enter; the editor is mouse-driven
			var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
			Text t = Label(r, label, fontSize, TextColor, TextAnchor.MiddleCenter, FontStyle.Normal, "Text");
			Stretch(t.rectTransform, 6, 6, 0, 0);
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			Border(r, ButtonBorder, 6, 1f);
			Size(r.gameObject, width, height);
			if (onClick != null) b.onClick.AddListener(() => onClick());
			if (hint != null) Hint(r.gameObject, hint);
			return b;
		}

		public static Text LabelOf(Button b) { return b.transform.Find("Text").GetComponent<Text>(); }

		/// <summary>Shows a button as the active choice (accent fill, dark text) or as a normal button.</summary>
		public static void SetActive(Button b, bool active)
		{
			if (b == null) return;
			ColorBlock cb = b.colors;
			cb.normalColor = active ? Accent : ButtonBg;
			cb.highlightedColor = active ? Color.Lerp(Accent, Color.white, 0.15f) : ButtonHover;
			cb.selectedColor = cb.normalColor;
			b.colors = cb;
			Text t = LabelOf(b);
			if (t != null) { t.color = active ? AccentText : TextColor; t.fontStyle = active ? FontStyle.Bold : FontStyle.Normal; }
		}

		/// <summary>A small coloured square at the left of a button (texture swatches).</summary>
		public static Image Swatch(Button b, Color color)
		{
			RectTransform r = Rect("Swatch", b.transform);
			Anchor(r, new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(14f, 14f));
			r.pivot = new Vector2(0f, 0.5f);
			var img = r.gameObject.AddComponent<Image>();
			img.sprite = Rounded(3); img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
			Text t = LabelOf(b);
			t.rectTransform.offsetMin = new Vector2(26f, t.rectTransform.offsetMin.y);
			t.alignment = TextAnchor.MiddleLeft;
			return img;
		}

		public class SliderRow
		{
			public Slider Slider;
			public Text Value;
		}

		/// <summary>"Label ........ value" on one line, the slider below it.</summary>
		public static SliderRow Slider(Transform parent, string label, float min, float max, float value, Func<float, string> format, Action<float> onChange, string hint = null, bool whole = false)
		{
			RectTransform box = Rect("Slider_" + label, parent);
			Vertical(box.gameObject, 2f, new RectOffset(0, 0, 0, 0));
			RectTransform top = Row(box, 18, 4, "Top");
			Label(top, label, 14, TextColor);
			Text v = Label(top, "", 14, Accent, TextAnchor.MiddleRight, FontStyle.Bold, "Value");

			RectTransform sr = Rect("Slider", box);
			Size(sr.gameObject, -1, 18);
			var slider = sr.gameObject.AddComponent<Slider>();
			RectTransform bg = Rect("Background", sr);
			bg.anchorMin = new Vector2(0, 0.5f); bg.anchorMax = new Vector2(1, 0.5f); bg.sizeDelta = new Vector2(0, 6); bg.anchoredPosition = Vector2.zero;
			Background(bg.gameObject, FieldBg, 3);
			RectTransform fillArea = Rect("Fill Area", sr);
			fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f); fillArea.sizeDelta = new Vector2(-12, 6); fillArea.anchoredPosition = new Vector2(-1, 0);
			RectTransform fill = Rect("Fill", fillArea);
			fill.sizeDelta = new Vector2(10, 0);
			Background(fill.gameObject, Accent, 3);
			RectTransform handleArea = Rect("Handle Slide Area", sr);
			Stretch(handleArea, 7, 7, 0, 0);
			RectTransform handle = Rect("Handle", handleArea);
			handle.sizeDelta = new Vector2(14, 0);
			Image hImg = Background(handle.gameObject, Color.white, 7);
			slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = hImg;
			slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
			var nav = slider.navigation; nav.mode = Navigation.Mode.None; slider.navigation = nav;
			slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = whole;
			slider.SetValueWithoutNotify(value);
			v.text = format(value);
			slider.onValueChanged.AddListener(x => { v.text = format(x); if (onChange != null) onChange(x); });
			if (hint != null) Hint(box.gameObject, hint);
			return new SliderRow { Slider = slider, Value = v };
		}

		public static InputField Field(Transform parent, string placeholder, string text = "", float height = RowHeight, string hint = null)
		{
			RectTransform r = Rect("Field", parent);
			Image img = Background(r.gameObject, FieldBg, 6);
			Border(r, ButtonBorder, 6, 1f);
			Size(r.gameObject, -1, height);
			var f = r.gameObject.AddComponent<InputField>();
			f.targetGraphic = img;
			Text ph = Label(r, placeholder, FontSize, TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "Placeholder");
			Stretch(ph.rectTransform, 10, 10, 0, 0);
			Text t = Label(r, "", FontSize, TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Text");
			Stretch(t.rectTransform, 10, 10, 0, 0);
			t.supportRichText = false;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			f.textComponent = t; f.placeholder = ph;
			f.caretColor = Accent; f.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
			var nav = f.navigation; nav.mode = Navigation.Mode.None; f.navigation = nav;
			f.text = text;
			if (hint != null) Hint(r.gameObject, hint);
			return f;
		}

		/// <summary>A text field for several lines (Enter = new line), text from the top left, wrapped.</summary>
		public static InputField TextArea(Transform parent, string placeholder, float height, string hint = null)
		{
			InputField f = Field(parent, placeholder, "", height, hint);
			f.lineType = InputField.LineType.MultiLineNewline;
			foreach (Text t in new[] { f.textComponent, (Text)f.placeholder })
			{
				t.alignment = TextAnchor.UpperLeft;
				t.horizontalOverflow = HorizontalWrapMode.Wrap;
				t.verticalOverflow = VerticalWrapMode.Truncate;
				Stretch(t.rectTransform, 10, 10, 8, 8);
			}
			return f;
		}

		/// <summary>A small square button filled with a colour (colour pickers).</summary>
		public static Button ColorButton(Transform parent, Color color, Action onClick, string hint = null, float size = 24f)
		{
			RectTransform r = Rect("Color", parent);
			Image img = Background(r.gameObject, color, 4);
			var b = r.gameObject.AddComponent<Button>();
			b.targetGraphic = img;
			ColorBlock cb = b.colors;
			cb.normalColor = Color.white; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.selectedColor = Color.white;
			b.colors = cb;
			var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
			Border(r, ButtonBorder, 4, 1f);
			Size(r.gameObject, size, size);
			if (onClick != null) b.onClick.AddListener(() => onClick());
			if (hint != null) Hint(r.gameObject, hint);
			return b;
		}

		/// <summary>A thin horizontal line between sections.</summary>
		public static void Separator(Transform parent)
		{
			RectTransform r = Rect("Separator", parent);
			var img = r.gameObject.AddComponent<Image>();
			img.color = new Color(1, 1, 1, 0.08f); img.raycastTarget = false;
			Size(r.gameObject, -1, 1);
		}

		/// <summary>A vertical scroll list; returns the content (vertical layout) to fill.</summary>
		public static RectTransform ScrollList(Transform parent, out ScrollRect scroll, float spacing = 4f)
		{
			RectTransform view = Rect("Scroll", parent);
			var bg = view.gameObject.AddComponent<Image>();
			bg.color = new Color(0, 0, 0, 0.001f); // catches the mouse wheel over gaps
			scroll = view.gameObject.AddComponent<ScrollRect>();
			scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
			scroll.inertia = false;

			RectTransform viewport = Rect("Viewport", view);
			Stretch(viewport, 0, 10, 0, 0);
			viewport.gameObject.AddComponent<RectMask2D>();
			RectTransform content = Rect("Content", viewport);
			content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
			content.offsetMin = content.offsetMax = Vector2.zero;
			Vertical(content.gameObject, spacing, new RectOffset(0, 2, 0, 4), true);

			RectTransform bar = Rect("Scrollbar", view);
			bar.anchorMin = new Vector2(1, 0); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(1, 0.5f);
			bar.sizeDelta = new Vector2(6, 0); bar.anchoredPosition = Vector2.zero;
			Background(bar.gameObject, new Color(0, 0, 0, 0.3f), 3);
			var sb = bar.gameObject.AddComponent<Scrollbar>();
			sb.direction = Scrollbar.Direction.BottomToTop;
			RectTransform slide = Rect("Sliding Area", bar);
			Stretch(slide);
			RectTransform handle = Rect("Handle", slide);
			Stretch(handle);
			sb.handleRect = handle;
			sb.targetGraphic = Background(handle.gameObject, new Color(1, 1, 1, 0.25f), 3);
			var nav = sb.navigation; nav.mode = Navigation.Mode.None; sb.navigation = nav;

			scroll.viewport = viewport; scroll.content = content;
			scroll.verticalScrollbar = sb;
			scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
			return content;
		}

		#endregion

		#region Hints (status bar text while hovering)

		/// <summary>Raised when the mouse enters (text) or leaves (null) a control with a hint.</summary>
		public static event Action<string> HintChanged;

		public static void Hint(GameObject go, string hint)
		{
			var h = Ensure<HintTarget>(go);
			h.Text = hint;
		}

		public class HintTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
		{
			public string Text;
			public void OnPointerEnter(PointerEventData e) { if (HintChanged != null) HintChanged(Text); }
			public void OnPointerExit(PointerEventData e) { if (HintChanged != null) HintChanged(null); }
			void OnDisable() { if (HintChanged != null) HintChanged(null); }
		}

		#endregion

		/// <summary>An overlay canvas that scales with the screen and always fits a 1400 x 800 layout.</summary>
		public static Canvas CreateCanvas(string name, int sortingOrder)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			go.layer = 5;
			var canvas = go.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = sortingOrder;
			canvas.pixelPerfect = false;
			var scaler = go.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1400, 800);
			scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the smaller of the two scales, so 1400 x 800 always fits
			scaler.referencePixelsPerUnit = 100;
			return canvas;
		}
	}
}
