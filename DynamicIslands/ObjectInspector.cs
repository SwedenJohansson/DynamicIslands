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

		/// <summary>Days until things come back on the island being edited: its rule (Island tab) or the world's setting.</summary>
		static int EditorRegrowDays
		{
			get
			{
				int d;
				return int.TryParse(ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.RegrowDays), out d) ? Mathf.Max(0, d) : CustomIslandSpawner.RegrowDays;
			}
		}

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

			bool nowTyping = (titleField != null && titleField.isFocused) || amountFields.Any(f => f != null && f.isFocused);
			if (nowTyping) EditorInput.IsTyping = true;
			else if (typing && !NoteEditorWindow.IsOpen && !ItemPickerWindow.IsOpen) EditorInput.IsTyping = false;
			typing = nowTyping;
		}

		static void Rebuild(EditorGameObject target)
		{
			dirty = false;
			if (target == null) target = null; // a destroyed object becomes a real null
			shown = target;
			titleField = null;
			amountFields.Clear();
			foreach (Transform child in root) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
			root.gameObject.SetActive(target != null);
			if (target == null) return;
			if (target.Props == null) target.Props = new Dictionary<string, string>();

			ContentCatalog.CreatureKind kind = ContentCatalog.CreatureOf(target.GameObjectName);
			if (ContentCatalog.IsZone(target.GameObjectName))
			{
				// (nothing to colour: zones are invisible in a world)
				if (target.GameObjectName == ContentCatalog.AtmosphereZoneName) AtmosphereGroup(target);
				else if (target.GameObjectName == ContentCatalog.SoundZoneName) SoundGroup(target);
				else { ZoneGroup(target); LootGroup(target, true); }
				BehaviourGroup(target);
				return;
			}
			if (ContentCatalog.IsHelper(target.GameObjectName)) { BehaviourGroup(target); return; }
			if (kind != null) CreatureGroup(target, kind);
			else
			{
				bool note = ObjectProps.IsNote(target.GameObjectName, target.Props), loot = ObjectProps.IsLoot(target.GameObjectName, target.Props);
				if (note) NoteGroup(target);
				if (loot) LootGroup(target, false);
				if (!note || !loot) AddFeatureGroup(target, note, loot);
			}
			BehaviourGroup(target);
			ColourGroup(target, kind != null);
		}

		/// <summary>What the object does in a world (name, movement, use, collision, events), and the button for the behaviour window.</summary>
		static void BehaviourGroup(EditorGameObject target)
		{
			RectTransform g = UIKit.Group(root, "Behaviour & events");
			Text t = UIKit.Label(g, BehaviourWindow.Summary(target.Props, target.GameObjectName), 12, UIKit.TextMuted);
			t.lineSpacing = 1.05f;
			Button b = UIKit.Button(g, "Behaviour & events...", () => BehaviourWindow.Open(target), "A name, movement (spin, bob, a door or lift), hidden at first, players can use it, collision, and what happens when...", -1, 26f, 13);
			if (BehaviourProps.Any(target.Props)) UIKit.SetActive(b, true);
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
			Button on = UIKit.Button(rrow, (EditorRegrowDays > 0 ? EditorRegrowDays + " days" : "never (island rule)"), () => { Set(target, ObjectProps.CreatureRespawn, null); Refresh(); },
				"Killed or caught animals come back after the world's regrow time (spawnpool.txt: regrowDays), like trees", 110, 26f, 12);
			Button off = UIKit.Button(rrow, "Never", () => { Set(target, ObjectProps.CreatureRespawn, "0"); Refresh(); }, "Once killed or caught, gone for good in that world", 64, 26f, 12);
			UIKit.SetActive(on, respawns);
			UIKit.SetActive(off, !respawns);

			// Ambush: wait for a trigger zone (the button steps through "at once" and the island's zones)
			string waits = ObjectProps.Get(p, ObjectProps.CreatureZone);
			List<string> choices = new[] { "" }.Concat(ContentCatalog.ZoneIdsInEditor()).ToList();
			if (waits.Length > 0 && !choices.Contains(waits)) choices.Add(waits);
			RectTransform wrow = UIKit.Row(g, 26f, 4f, "Wake");
			UIKit.Size(UIKit.Label(wrow, "Appears", 13, UIKit.TextMuted).gameObject, 64);
			Button wake = UIKit.Button(wrow, waits.Length == 0 ? "at once" : "when '" + waits + "' fires", () =>
			{
				string next = choices[(choices.IndexOf(waits) + 1) % choices.Count];
				Set(target, ObjectProps.CreatureZone, next.Length > 0 ? next : null);
				Refresh();
			}, choices.Count > 1 ? "Click to choose: at once, or when a player walks into one of the island's trigger zones (an ambush)" : "Place a trigger zone (Zones & triggers) to make this an ambush", -1, 26f, 12);
			UIKit.SetActive(wake, waits.Length > 0);
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
			RectTransform g = UIKit.Group(root, "Note");
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

		/// <summary>What players can do with this object in a world, for the features it doesn't have yet.</summary>
		static void AddFeatureGroup(EditorGameObject target, bool note, bool loot)
		{
			RectTransform g = UIKit.Group(root, note || loot ? "Also make it..." : PlaceableCatalog.DisplayName(target.GameObjectName));
			if (!note && !loot) UIKit.Label(g, "Players use it in a world with the interact key (E):", 12, UIKit.TextMuted);
			RectTransform row = UIKit.Row(g, UIKit.RowHeight, 4f);
			if (!note)
				UIKit.Button(row, "Readable...", () =>
				{
					NoteEditorWindow.Apply(target, "Note", "");
					NoteEditorWindow.Open(target);
				}, "Write a note on this object (a sign, a book, a crate...)");
			if (!loot)
				UIKit.Button(row, "A chest...", () =>
				{
					PropsCommand.Change(target, ObjectProps.With(target.Props, ObjectProps.LootItems, ""));
					Refresh();
					ItemPickerWindow.Open(target);
				}, "Put items in this object: players open it and take them");
		}

		#endregion

		#region Trigger zones

		static void ZoneGroup(EditorGameObject target)
		{
			Dictionary<string, string> p = target.Props;
			RectTransform g = UIKit.Group(root, "Trigger zone");
			RectTransform idRow = UIKit.Row(g, 28f, 4f, "Id");
			UIKit.Size(UIKit.Label(idRow, "Name", 13, UIKit.TextMuted).gameObject, 44);
			string id = ObjectProps.Get(p, ObjectProps.ZoneId);
			InputField idField = UIKit.Field(idRow, "zone name", id, 28f, "Creatures (and quests) link to the zone by this name");
			idField.characterLimit = 24;
			idField.onEndEdit.AddListener(v => RenameZone(target, id, v.Trim()));
			amountFields.Add(idField);

			UIKit.Slider(g, "Size (radius)", ObjectProps.MinZoneRadius, ObjectProps.MaxZoneRadius, ObjectProps.Radius(p), v => v.ToString("0") + " m",
				v => Set(target, ObjectProps.ZoneRadius, ObjectProps.Format(Mathf.Round(v)), "6", false), "How close players must come (the yellow sphere)", true);

			InputField msg = UIKit.TextArea(g, "Message shown to the player who walks in (optional)", 46f, "A line or two, shown at the top of the screen");
			msg.text = ObjectProps.Get(p, ObjectProps.ZoneMessage);
			msg.characterLimit = 200;
			msg.onEndEdit.AddListener(v => Set(target, ObjectProps.ZoneMessage, v.Trim().Length > 0 ? v.Trim() : null));
			amountFields.Add(msg);

			RectTransform fires = UIKit.Row(g, 26f, 4f, "Fires");
			UIKit.Size(UIKit.Label(fires, "Fires", 13, UIKit.TextMuted).gameObject, 44);
			bool repeats = ObjectProps.Repeats(p);
			Button once = UIKit.Button(fires, "Once", () => { Set(target, ObjectProps.ZoneRepeat, null); Refresh(); }, "Once per world, for the first player (again after the island's regrow time)", -1, 26f, 12);
			Button every = UIKit.Button(fires, "Every time", () => { Set(target, ObjectProps.ZoneRepeat, "1"); Refresh(); }, "Each time a player walks in (at most every half minute)", -1, 26f, 12);
			UIKit.SetActive(once, !repeats);
			UIKit.SetActive(every, repeats);

			GameObject placed = GameObject.Find("PlacedObjects");
			int linked = placed == null || id.Length == 0 ? 0 : placed.GetComponentsInChildren<EditorGameObject>().Count(e => ObjectProps.Get(e.Props, ObjectProps.CreatureZone) == id);
			UIKit.Label(g, linked > 0 ? linked + " creature spot(s) wait for this zone (an ambush)." : "<i>Creatures can wait for it: select one and set \"Appears\".</i>", 12, UIKit.TextMuted);
		}

		static readonly Color[] FogColors =
		{
			new Color(0.92f, 0.94f, 0.96f), new Color(0.55f, 0.58f, 0.62f), new Color(0.45f, 0.6f, 0.8f), new Color(0.45f, 0.6f, 0.35f),
			new Color(1f, 0.6f, 0.35f), new Color(0.6f, 0.4f, 0.75f), new Color(0.7f, 0.2f, 0.15f),
		};
		static readonly Color[] LightColors =
		{
			new Color(1f, 0.85f, 0.6f), new Color(0.6f, 0.75f, 1f), new Color(0.6f, 1f, 0.6f), new Color(0.8f, 0.55f, 1f),
			new Color(1f, 0.45f, 0.35f), new Color(0.35f, 0.35f, 0.45f),
		};

		static void RadiusSlider(Transform g, EditorGameObject target)
		{
			UIKit.Slider(g, "Size (radius)", ObjectProps.MinZoneRadius, ObjectProps.MaxZoneRadius, ObjectProps.Radius(target.Props), v => v.ToString("0") + " m",
				v => Set(target, ObjectProps.ZoneRadius, ObjectProps.Format(Mathf.Round(v)), "6", false), "How far it reaches (the sphere)", true);
		}

		/// <summary>A row of colour swatches (with None) setting a colour key; strength slider below when a colour is set.</summary>
		static void ColourChoice(Transform g, EditorGameObject target, string title, string key, string amountKey, float defaultAmount, Color[] colours, string hint)
		{
			UIKit.Size(UIKit.Label(g, title, 13, UIKit.TextMuted).gameObject, -1, 16);
			RectTransform row = UIKit.Row(g, 22f, 3f, title);
			bool has = ObjectProps.HasColor(target.Props, key);
			Button none = UIKit.Button(row, "None", () => { Set(target, key, null); Refresh(); }, "No " + title.ToLowerInvariant(), 42, 22f, 11);
			UIKit.SetActive(none, !has);
			foreach (Color c in colours)
			{
				Color col = c;
				UIKit.ColorButton(row, c, () => { Set(target, key, ObjectProps.ColorText(col)); Refresh(); }, hint, 22f);
			}
			if (has)
				UIKit.Slider(g, "Strength", 0.05f, 1f, Mathf.Clamp01(ObjectProps.GetFloat(target.Props, amountKey, defaultAmount)), v => (v * 100f).ToString("F0") + " %",
					v => Set(target, amountKey, ObjectProps.Format(Mathf.Round(v * 100f) / 100f), null, false), "How strong it is in the middle of the zone");
		}

		static void AtmosphereGroup(EditorGameObject target)
		{
			RectTransform g = UIKit.Group(root, "Atmosphere zone");
			UIKit.Label(g, "Fly the camera into the sphere to see it.", 12, UIKit.TextMuted);
			RadiusSlider(g, target);
			ColourChoice(g, target, "Fog", ObjectProps.AtmoFog, ObjectProps.AtmoFogAmount, 0.6f, FogColors, "Fog of this colour, thicker towards the middle");
			ColourChoice(g, target, "Light", ObjectProps.AtmoLight, ObjectProps.AtmoLightAmount, 0.5f, LightColors, "Tints the light (warm, cold, eerie...)");
			UIKit.Size(UIKit.Label(g, "Particles", 13, UIKit.TextMuted).gameObject, -1, 16);
			RectTransform row = UIKit.Row(g, 24f, 3f, "Particles");
			string current = ObjectProps.Get(target.Props, ObjectProps.AtmoParticles, "none");
			foreach (string kind in AtmosphereZone.ParticleKinds)
			{
				string k = kind;
				Button b = UIKit.Button(row, char.ToUpper(k[0]) + k.Substring(1), () => { Set(target, ObjectProps.AtmoParticles, k == "none" ? null : k); Refresh(); }, "Particles: " + k, -1, 24f, 10);
				UIKit.SetActive(b, k == current);
			}
		}

		static void SoundGroup(EditorGameObject target)
		{
			RectTransform g = UIKit.Group(root, "Sound zone");
			string ev = ObjectProps.Get(target.Props, ObjectProps.SoundEvent);
			Text name = UIKit.Label(g, ev.Length > 0 ? ev : "<i>No sound chosen yet</i>", 12, ev.Length > 0 ? UIKit.TextColor : UIKit.TextMuted);
			name.horizontalOverflow = HorizontalWrapMode.Wrap;
			RectTransform row = UIKit.Row(g, 26f, 4f, "Choose");
			Button choose = UIKit.Button(row, "Choose sound...", () => SoundPickerWindow.Open(target), "Pick one of Raft's sounds (birds, wind, waves, music...)", -1, 26f, 12);
			UIKit.SetActive(choose, ev.Length == 0);
			if (ev.Length > 0)
			{
				UIKit.Button(row, "\u25BA", () => SoundLibrary.Preview(ev), "Listen", 32, 26f, 12);
				UIKit.Button(row, "\u25A0", SoundLibrary.StopPreview, "Stop", 32, 26f, 12);
			}
			UIKit.Slider(g, "Volume", 0.05f, 1f, Mathf.Clamp01(ObjectProps.GetFloat(target.Props, ObjectProps.SoundVolume, 0.8f)), v => (v * 100f).ToString("F0") + " %",
				v => Set(target, ObjectProps.SoundVolume, ObjectProps.Format(Mathf.Round(v * 100f) / 100f), "0.8", false), "How loud (in the middle of the zone)");
			RectTransform mode = UIKit.Row(g, 24f, 4f, "Mode");
			bool once = ObjectProps.Get(target.Props, ObjectProps.SoundMode) == "enter";
			Button loop = UIKit.Button(mode, "While inside", () => { Set(target, ObjectProps.SoundMode, null); Refresh(); }, "Plays (loops) while a player is in the zone, louder towards the middle", -1, 24f, 11);
			Button enter = UIKit.Button(mode, "Once on entering", () => { Set(target, ObjectProps.SoundMode, "enter"); Refresh(); }, "Plays once each time a player walks in", -1, 24f, 11);
			UIKit.SetActive(loop, !once);
			UIKit.SetActive(enter, once);
			RadiusSlider(g, target);
		}

		/// <summary>Renames a zone; the creatures that waited for the old name follow (one undo step each).</summary>
		static void RenameZone(EditorGameObject target, string oldId, string newId)
		{
			if (newId.Length == 0 || newId == oldId) { Refresh(); return; }
			Set(target, ObjectProps.ZoneId, newId);
			GameObject placed = GameObject.Find("PlacedObjects");
			if (placed != null && oldId.Length > 0)
				foreach (EditorGameObject e in placed.GetComponentsInChildren<EditorGameObject>().Where(e => ObjectProps.Get(e.Props, ObjectProps.CreatureZone) == oldId).ToList())
					PropsCommand.Change(e, ObjectProps.With(e.Props, ObjectProps.CreatureZone, newId));
			Refresh();
		}

		#endregion

		#region Loot

		/// <param name="zone">a trigger zone's items (given on entering): no refill or "not a chest" rows</param>
		static void LootGroup(EditorGameObject target, bool zone)
		{
			List<KeyValuePair<string, int>> loot = ObjectProps.Loot(target.Props);
			RectTransform g = UIKit.Group(root, (zone ? "Gives on entering" : "Loot") + (loot.Count > 0 ? " (" + loot.Count + ")" : ""));
			Transform rows = g;
			if (loot.Count > 5)
			{
				// Many kinds of items: a short scrolling list, so the panel stays on the screen
				RectTransform box = UIKit.Rect("Items", g);
				UIKit.Size(box.gameObject, -1, 5 * 30f);
				ScrollRect scroll;
				rows = UIKit.ScrollList(box, out scroll, 4f);
				UIKit.Stretch((RectTransform)scroll.transform);
			}
			if (loot.Count == 0) UIKit.Label(g, zone ? "<i>Nothing: add items to give players who walk in.</i>" : "<i>Empty: add items, or pick a ready-made set.</i>", 12, UIKit.TextMuted);
			for (int i = 0; i < loot.Count; i++)
			{
				int index = i;
				string item = loot[i].Key;
				RectTransform row = UIKit.Row(rows, 26f, 3f, "Item");
				RectTransform pic = UIKit.Rect("Icon", row);
				UIKit.Size(pic.gameObject, 24, 24);
				Image icon = pic.gameObject.AddComponent<Image>();
				icon.sprite = ContentCatalog.ItemSprite(item); icon.preserveAspect = true; icon.raycastTarget = false;
				if (icon.sprite == null) icon.color = new Color(1, 1, 1, 0.1f);
				Text label = UIKit.Label(row, ContentCatalog.ItemLabel(item), 12, ContentCatalog.ItemExists(item) ? UIKit.TextColor : UIKit.Danger);
				label.horizontalOverflow = HorizontalWrapMode.Overflow;
				InputField amount = UIKit.Field(row, "1", loot[i].Value.ToString(), 24f, "How many");
				UIKit.Size(amount.gameObject, 46, 24);
				amount.contentType = InputField.ContentType.IntegerNumber;
				amount.characterLimit = 3;
				amount.onEndEdit.AddListener(v =>
				{
					int n;
					if (!int.TryParse(v, out n)) n = 1;
					SetLootAmount(target, index, Mathf.Clamp(n, 0, ObjectProps.MaxLootAmount));
				});
				amountFields.Add(amount);
				Button remove = UIKit.Button(row, "\u00D7", () => SetLootAmount(target, index, 0), "Take it out of the chest", 24, 24, 13);
				UIKit.LabelOf(remove).color = new Color(1f, 0.6f, 0.55f);
			}
			RectTransform add = UIKit.Row(g, 26f, 4f, "Add");
			Button more = UIKit.Button(add, "Add items...", () => ItemPickerWindow.Open(target), "Choose from all of Raft's items, with pictures", -1, 26f, 12);
			UIKit.SetActive(more, true);
			UIKit.Button(add, "Empty", () => { PropsCommand.Change(target, ObjectProps.With(target.Props, ObjectProps.LootItems, "")); Refresh(); }, "Take everything out", 60, 26f, 12);
			RectTransform presets = UIKit.Row(g, 24f, 3f, "Presets");
			foreach (var preset in ContentCatalog.LootPresets)
			{
				string[] items = preset.Value;
				UIKit.Button(presets, preset.Key, () =>
				{
					PropsCommand.Change(target, ObjectProps.With(target.Props, ObjectProps.LootItems, ContentCatalog.PresetLoot(items)));
					Refresh();
				}, "Fill it with a ready-made set: " + string.Join(", ", items.Select(x => x.Split('*')[0]).ToArray()), -1, 24f, 11);
			}
			if (zone) return;
			RectTransform refill = UIKit.Row(g, 24f, 4f, "Refill");
			UIKit.Label(refill, "Fills up again", 12, UIKit.TextMuted);
			bool refills = ObjectProps.LootRefills(target.Props);
			Button on = UIKit.Button(refill, EditorRegrowDays > 0 ? "after " + EditorRegrowDays + " days" : "never (rule)", () => { Set(target, ObjectProps.LootRefill, null); Refresh(); },
				"Once looted, it fills up again after the world's regrow time (spawnpool.txt: regrowDays)", 96, 24f, 11);
			Button off = UIKit.Button(refill, "Never", () => { Set(target, ObjectProps.LootRefill, "0"); Refresh(); }, "Once looted, it stays empty in that world", 56, 24f, 11);
			UIKit.SetActive(on, refills);
			UIKit.SetActive(off, !refills);
			if (!ContentCatalog.IsLootObject(target.GameObjectName))
				UIKit.Button(g, "Not a chest", () =>
				{
					PropsCommand.Change(target, ObjectProps.With(ObjectProps.With(target.Props, ObjectProps.LootItems, null), ObjectProps.LootRefill, null));
					Refresh();
				}, "This object no longer holds loot", -1, 24f, 11);
		}

		static readonly List<InputField> amountFields = new List<InputField>();

		static void SetLootAmount(EditorGameObject target, int index, int amount)
		{
			List<KeyValuePair<string, int>> loot = ObjectProps.Loot(target.Props);
			if (index < 0 || index >= loot.Count) return;
			if (amount <= 0) loot.RemoveAt(index);
			else loot[index] = new KeyValuePair<string, int>(loot[index].Key, amount);
			PropsCommand.Change(target, ObjectProps.With(target.Props, ObjectProps.LootItems, ObjectProps.LootText(loot)));
			Refresh();
		}

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
