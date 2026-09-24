using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The Objects tab's inspector: when exactly one placed object is selected it shows that object's settings,
	/// so every editor sits in the same place as the objects themselves:
	///   Creature  - herd size, difficulty presets, health / damage / speed / size, respawning (the creature editor)
	///   Note      - title and a preview of the text, "Edit note..." opens the note editor; any object can be made readable
	///   Colour    - a tint for any object, creatures included (swatches, strength, custom RGB)
	/// Every change is an undo step (slider drags merge into one).
	/// </summary>
	public static class ObjectInspector
	{
		static RectTransform root;
		static EditorGameObject shown;
		static bool dirty, typing, showRgb;
		static InputField titleField;

		/// <summary>The inspector is visible (an object with settings is selected); the Objects tab hides its tips then.</summary>
		public static bool Visible { get { return root != null && root.gameObject.activeSelf; } }

		public static EditorGameObject Target { get { return shown; } }

		public static readonly Color[] Swatches =
		{
			new Color(1f, 0.35f, 0.3f), new Color(1f, 0.62f, 0.25f), new Color(1f, 0.9f, 0.35f), new Color(0.45f, 0.9f, 0.4f),
			new Color(0.3f, 0.85f, 0.85f), new Color(0.35f, 0.55f, 1f), new Color(0.7f, 0.45f, 1f), new Color(1f, 0.5f, 0.8f),
			new Color(0.35f, 0.35f, 0.38f),
		};

		public static void Build(Transform parent)
		{
			root = UIKit.Rect("Inspector", parent);
			UIKit.Vertical(root.gameObject, 10f, new RectOffset(0, 0, 0, 0));
			root.gameObject.SetActive(false);
			shown = null;
		}

		/// <summary>Shows the settings again (after an undo, or a change made elsewhere).</summary>
		public static void Refresh() { dirty = true; }

		public static void Tick()
		{
			if (root == null) return;
			var g = DynamicIslands.EditorGizmoHandler;
			EditorGameObject sel = null;
			if (g != null)
			{
				List<Transform> roots = g.SelectedRoots.Where(t => t != null).ToList();
				if (roots.Count == 1) sel = roots[0].GetComponent<EditorGameObject>();
			}
			// (a shown object that was destroyed, e.g. by loading an island, compares equal to null: check it too)
			bool gone = !ReferenceEquals(shown, null) && shown == null;
			if (sel != shown || dirty || gone) Rebuild(sel);

			bool nowTyping = titleField != null && titleField.isFocused;
			if (nowTyping) EditorInput.IsTyping = true;
			else if (typing && !NoteEditorWindow.IsOpen) EditorInput.IsTyping = false;
			typing = nowTyping;
		}

		static void Rebuild(EditorGameObject target)
		{
			dirty = false;
			if (target == null) target = null; // a destroyed object becomes a real null
			shown = target;
			titleField = null;
			foreach (Transform child in root) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
			root.gameObject.SetActive(target != null);
			if (target == null) return;
			if (target.Props == null) target.Props = new Dictionary<string, string>();

			ContentCatalog.CreatureKind kind = ContentCatalog.CreatureOf(target.GameObjectName);
			if (kind != null) CreatureGroup(target, kind);
			else NoteGroup(target);
			ColourGroup(target, kind != null);
		}

		#region Creature editor

		static void CreatureGroup(EditorGameObject target, ContentCatalog.CreatureKind kind)
		{
			Dictionary<string, string> p = target.Props;
			RectTransform g = UIKit.Group(root, "Creature: " + kind.Label);
			Text about = UIKit.Label(g, Capital(kind.Hint) + ".", 12, UIKit.TextMuted);
			about.lineSpacing = 1.05f;

			UIKit.Slider(g, "Animals here", 1, ObjectProps.MaxCount, ObjectProps.Count(p), v => v <= 1 ? "1" : v.ToString("F0") + " (a herd)",
				v => Set(target, ObjectProps.CreatureCount, ((int)v).ToString(), "1", false), "How many of them live at this spot", true);

			RectTransform presets = UIKit.Row(g, 26f, 4f, "Presets");
			string current = ContentCatalog.Summary(p);
			foreach (var preset in ObjectProps.Presets)
			{
				float[] v = preset.Value;
				Button b = UIKit.Button(presets, preset.Key, () =>
				{
					Dictionary<string, string> np = ObjectProps.With(target.Props, ObjectProps.CreatureHealth, ObjectProps.Format(v[0]), "1");
					np = ObjectProps.With(np, ObjectProps.CreatureDamage, ObjectProps.Format(v[1]), "1");
					np = ObjectProps.With(np, ObjectProps.CreatureSpeed, ObjectProps.Format(v[2]), "1");
					PropsCommand.Change(target, np);
					Refresh();
				}, preset.Key + ": health \u00D7" + ObjectProps.Format(v[0]) + ", damage \u00D7" + ObjectProps.Format(v[1]) + ", speed \u00D7" + ObjectProps.Format(v[2]), -1, 26f, 12);
				UIKit.SetActive(b, preset.Key == current || (preset.Key == "Normal" && current.Length == 0));
			}

			Multiplier(g, target, "Health", ObjectProps.CreatureHealth, ObjectProps.MinMultiplier, ObjectProps.MaxMultiplier, ObjectProps.Health(p), "How much damage it takes to kill (Raft's own health times this)");
			Multiplier(g, target, "Damage", ObjectProps.CreatureDamage, 0f, ObjectProps.MaxMultiplier, ObjectProps.Damage(p), "How hard it hits players (0 = harmless)");
			Multiplier(g, target, "Speed", ObjectProps.CreatureSpeed, ObjectProps.MinMultiplier, 2.5f, ObjectProps.Speed(p), "How fast it walks, runs and swims");
			Multiplier(g, target, "Size", ObjectProps.CreatureSize, ObjectProps.MinSize, ObjectProps.MaxSize, ObjectProps.Size(p), "How big it is (also shown on the marker)");

			RectTransform rrow = UIKit.Row(g, 26f, 4f, "Respawn");
			UIKit.Label(rrow, "Comes back after", 13, UIKit.TextMuted);
			bool respawns = ObjectProps.Respawns(p);
			Button on = UIKit.Button(rrow, (CustomIslandSpawner.RegrowDays > 0 ? CustomIslandSpawner.RegrowDays + " days" : "never (world setting)"), () => { Set(target, ObjectProps.CreatureRespawn, null); Refresh(); },
				"Killed or caught animals come back after the world's regrow time (spawnpool.txt: regrowDays), like trees", 110, 26f, 12);
			Button off = UIKit.Button(rrow, "Never", () => { Set(target, ObjectProps.CreatureRespawn, "0"); Refresh(); }, "Once killed or caught, gone for good in that world", 64, 26f, 12);
			UIKit.SetActive(on, respawns);
			UIKit.SetActive(off, !respawns);
		}

		static void Multiplier(Transform g, EditorGameObject target, string label, string key, float min, float max, float value, string hint)
		{
			UIKit.SliderRow s = UIKit.Slider(g, label, min, max, value, v => "\u00D7" + v.ToString("0.00"), null, hint);
			// Rounded to steps of 0.05 so values like 1.00 and 2.00 are easy to hit
			s.Slider.onValueChanged.AddListener(v =>
			{
				float r = Mathf.Round(v * 20f) / 20f;
				Set(target, key, ObjectProps.Format(r), "1", false);
			});
		}

		static string Capital(string s) { return string.IsNullOrEmpty(s) ? "" : char.ToUpper(s[0]) + s.Substring(1); }

		#endregion

		#region Notes

		static void NoteGroup(EditorGameObject target)
		{
			Dictionary<string, string> p = target.Props;
			bool note = ObjectProps.IsNote(target.GameObjectName, p);
			RectTransform g = UIKit.Group(root, note ? "Note" : PlaceableCatalog.DisplayName(target.GameObjectName));
			if (!note)
			{
				UIKit.Label(g, "Make this object readable: players look at it and press the interact key (E) to read your text.", 12, UIKit.TextMuted);
				UIKit.Button(g, "Add a note to it...", () =>
				{
					NoteEditorWindow.Apply(target, "Note", "");
					NoteEditorWindow.Open(target);
				}, "Write a note on this object (a sign, a book, a crate...)");
				return;
			}
			titleField = UIKit.Field(g, "Title", ObjectProps.Get(p, ObjectProps.NoteTitle, ""), 30f, "The note's title (Enter keeps it)");
			titleField.characterLimit = NoteEditorWindow.MaxTitle;
			titleField.onEndEdit.AddListener(v => Set(target, ObjectProps.NoteTitle, v.Trim(), null));
			string text = ObjectProps.Get(p, ObjectProps.NoteText, "");
			string preview = text.Length == 0 ? "<i>(no text yet)</i>" : Escape(text.Length > 140 ? text.Substring(0, 140).TrimEnd() + "\u2026" : text);
			Text pt = UIKit.Label(g, preview, 12, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Preview");
			pt.verticalOverflow = VerticalWrapMode.Truncate;
			UIKit.Size(pt.gameObject, -1, 48);
			RectTransform row = UIKit.Row(g, UIKit.RowHeight, 4f);
			Button edit = UIKit.Button(row, "Edit note...", () => NoteEditorWindow.Open(target), "Write the note's text in a big window, with a preview");
			UIKit.SetActive(edit, true);
			if (!ContentCatalog.IsNoteObject(target.GameObjectName))
				UIKit.Button(row, "Remove", () =>
				{
					Dictionary<string, string> np = ObjectProps.With(target.Props, ObjectProps.NoteTitle, null);
					PropsCommand.Change(target, ObjectProps.With(np, ObjectProps.NoteText, null));
					Refresh();
				}, "This object is no longer readable", 80);
		}

		static string Escape(string s) { return s.Replace("<", "\u2039").Replace(">", "\u203A"); }

		#endregion

		#region Colour

		static void ColourGroup(EditorGameObject target, bool creature)
		{
			Dictionary<string, string> p = target.Props;
			RectTransform g = UIKit.Group(root, "Colour");
			RectTransform row = UIKit.Row(g, 24f, 4f, "Swatches");
			Button none = UIKit.Button(row, "None", () => SetTint(target, null), "Its own colours", 46, 24f, 11);
			UIKit.SetActive(none, !ObjectProps.HasTint(p));
			foreach (Color c in Swatches)
			{
				Color col = c;
				UIKit.ColorButton(row, c, () => SetTint(target, col), "Tint it this colour" + (creature ? " (every animal of this spot)" : ""), 22f);
			}
			if (!ObjectProps.HasTint(p) && !showRgb)
			{
				UIKit.Button(g, "Custom colour...", () => { showRgb = true; Refresh(); }, "Mix your own tint", -1, 24f, 12);
				return;
			}
			UIKit.Slider(g, "Strength", 0f, 1f, ObjectProps.HasTint(p) ? ObjectProps.TintStrength(p) : 1f, v => (v * 100f).ToString("F0") + " %",
				v => { if (ObjectProps.HasTint(target.Props)) Set(target, ObjectProps.TintAmount, ObjectProps.Format(Mathf.Round(v * 100f) / 100f), "1", false); },
				"How strongly the colour shows");
			if (showRgb)
			{
				Color cur = ObjectProps.HasTint(p) ? ObjectProps.Tint(p) : Color.white;
				string[] names = { "Red", "Green", "Blue" };
				for (int i = 0; i < 3; i++)
				{
					int ch = i;
					UIKit.Slider(g, names[i], 0f, 1f, cur[ch], v => Mathf.RoundToInt(v * 255f).ToString(), v =>
					{
						Color c = ObjectProps.HasTint(target.Props) ? ObjectProps.Tint(target.Props) : Color.white;
						c[ch] = v;
						Set(target, ObjectProps.TintColor, ObjectProps.ColorText(c), null, false, "tint.rgb");
					}, "The tint's " + names[i].ToLowerInvariant() + " (the object's own colours are multiplied by it)");
				}
			}
			else UIKit.Button(g, "Custom colour...", () => { showRgb = true; Refresh(); }, "Mix your own tint", -1, 24f, 12);
		}

		static void SetTint(EditorGameObject target, Color? c)
		{
			Dictionary<string, string> np = ObjectProps.With(target.Props, ObjectProps.TintColor, c.HasValue ? ObjectProps.ColorText(c.Value) : null);
			if (!c.HasValue) np = ObjectProps.With(np, ObjectProps.TintAmount, null);
			PropsCommand.Change(target, np);
			Refresh();
		}

		#endregion

		/// <summary>
		/// Sets one setting as an undo step. rebuild = false while dragging a slider (rebuilding would drop the drag).
		/// mergeKey groups the changes of one slider drag into one step.
		/// </summary>
		static void Set(EditorGameObject target, string key, string value, string defaultValue = null, bool rebuild = true, string mergeKey = null)
		{
			if (target == null) return;
			PropsCommand.Change(target, ObjectProps.With(target.Props, key, value, defaultValue), rebuild ? null : (mergeKey ?? key));
			// The command refreshes the inspector; while dragging, keep the controls as they are
			if (!rebuild) dirty = false;
		}
	}
}
