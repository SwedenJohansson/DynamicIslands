using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// 3.5: the mod's journal (J) and stats page (K) keys as two rows at the bottom of Raft's own Settings > Controls list, in
	/// the main menu's settings and the pause menu's. Each row is a copy of Raft's last keybind row (name, main key, alt key)
	/// without its KeybindInterface, so Raft never saves it in its own settings or puts it in MyInput.Keybinds: ModKeys
	/// (world_rules.txt) stays the one place the keys are kept, and the Defaults window's Keys page shows the same keys.
	/// Clicking the main key waits for a key as Raft's rows do (Esc keeps the old one); the alt key column shows "Not set"
	/// and can't be set (the mod's keys have one key each). Raft's "reset to default" puts them back to J and K.
	/// </summary>
	public static class RaftControlsKeys
	{
		public const string JournalRow = "CI_JournalKeyRow", StatsRow = "CI_StatsKeyRow";
		public const string JournalLabel = "Islands journal", StatsLabel = "Islands stats page";

		static readonly AccessTools.FieldRef<KeybindInterface, KeyConnection> MainKeyOf = AccessTools.FieldRefAccess<KeybindInterface, KeyConnection>("mainKey");
		static readonly AccessTools.FieldRef<KeybindInterface, KeyConnection> AltKeyOf = AccessTools.FieldRefAccess<KeybindInterface, KeyConnection>("altKey");
		static readonly AccessTools.FieldRef<ControlsSettingsBox, ColorBlock> ColorsOf = AccessTools.FieldRefAccess<ControlsSettingsBox, ColorBlock>("colorblock");
		static readonly AccessTools.FieldRef<ControlsSettingsBox, RectTransform> ContentOf = AccessTools.FieldRefAccess<ControlsSettingsBox, RectTransform>("contentBox");

		/// <summary>Height the two rows added to each Controls box's list (the list is made that much taller in SetFocus).</summary>
		static readonly Dictionary<ControlsSettingsBox, float> added = new Dictionary<ControlsSettingsBox, float>();

		/// <summary>The rows of a Controls box are there (made once per box: one in the main menu, one in a world).</summary>
		public static void Ensure(ControlsSettingsBox box)
		{
			if (box == null || FindRow(box, JournalRow) != null) return;
			// (the main menu's box goes with its scene)
			foreach (ControlsSettingsBox gone in new List<ControlsSettingsBox>(added.Keys)) if (gone == null) added.Remove(gone);
			KeybindInterface[] rows = box.GetComponentsInChildren<KeybindInterface>(true);
			if (rows.Length == 0) { Debug.LogWarning("[CUSTOM ISLANDS] Raft's Controls settings have no key rows: the journal and stats keys are set in Defaults... > Keys only"); return; }
			KeybindInterface last = rows[rows.Length - 1];
			RectTransform lastRect = last.transform as RectTransform;
			Transform parent = last.transform.parent;
			// one row's step down the list: from the row before the last (same parent) to the last, else the row's height
			float step = lastRect.rect.height;
			for (int i = rows.Length - 2; i >= 0; i--)
			{
				if (rows[i].transform.parent != parent) continue;
				float d = ((RectTransform)rows[i].transform).anchoredPosition.y - lastRect.anchoredPosition.y;
				if (d > 1f) step = d;
				break;
			}
			bool laidOut = parent.GetComponent<LayoutGroup>() != null;
			float lastY = lastRect.anchoredPosition.y;
			if (!laidOut)
				// anything under the last row in its list (a reset button) moves down to make room
				foreach (Transform c in parent)
				{
					RectTransform r = c as RectTransform;
					if (r != null && r != lastRect && r.anchoredPosition.y < lastY - 1f) r.anchoredPosition -= new Vector2(0f, step * 2f);
				}
			ColorBlock colors = ColorsOf(box);
			int at = last.transform.GetSiblingIndex();
			Make(last, colors, true, JournalRow, JournalLabel, laidOut ? at + 1 : -1, lastY - step);
			Make(last, colors, false, StatsRow, StatsLabel, laidOut ? at + 2 : -1, lastY - step * 2f);
			added[box] = laidOut ? 0f : step * 2f;
			if (laidOut && parent is RectTransform) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)parent);
			Debug.Log("[CUSTOM ISLANDS] Journal and stats key rows added to Raft's Controls settings (" + rows.Length + " Raft rows, step " + step.ToString("F0") + (laidOut ? ", laid out" : "") + ")");
		}

		static Transform FindRow(ControlsSettingsBox box, string name)
		{
			foreach (RaftControlsKeyRow r in box.GetComponentsInChildren<RaftControlsKeyRow>(true)) if (r.name == name) return r.transform;
			return null;
		}

		static void Make(KeybindInterface from, ColorBlock colors, bool forJournal, string name, string label, int sibling, float y)
		{
			GameObject go = UnityEngine.Object.Instantiate(from.gameObject, from.transform.parent, false);
			go.name = name;
			KeybindInterface copy = go.GetComponent<KeybindInterface>();
			KeyConnection main = MainKeyOf(copy), alt = AltKeyOf(copy);
			// (Raft finds its rows by KeybindInterface: without it this row is never saved in Raft's settings or MyInput)
			UnityEngine.Object.DestroyImmediate(copy);
			// (I2 Localize would put Raft's own row name back)
			foreach (Component c in go.GetComponentsInChildren<Component>(true))
				if (c != null && !(c is Transform) && c.GetType().Name.IndexOf("Localiz", StringComparison.OrdinalIgnoreCase) >= 0) UnityEngine.Object.DestroyImmediate(c);
			foreach (Text t in go.GetComponentsInChildren<Text>(true))
				if ((main == null || t != main.text) && (alt == null || t != alt.text)) { t.text = label; break; }
			RaftControlsKeyRow row = go.AddComponent<RaftControlsKeyRow>();
			row.Setup(forJournal, main, alt, colors);
			RectTransform rt = (RectTransform)go.transform;
			if (sibling >= 0) go.transform.SetSiblingIndex(sibling);
			else rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
			go.SetActive(true);
		}

		/// <summary>SetFocus sets the list's height to Raft's own fixed number (keyboard), which since Raft's later key rows
		/// already ends above its last rows (Hotbar 8-10): the list is made as tall as its rows, ours included.</summary>
		public static void Grow(ControlsSettingsBox box, bool gamepad)
		{
			if (gamepad || box == null) return;
			RectTransform content = ContentOf(box);
			if (content == null) return;
			float h;
			if (added.TryGetValue(box, out h) && h > 0f) { Vector2 s0 = content.sizeDelta; s0.y += h; content.sizeDelta = s0; }
			LayoutRebuilder.ForceRebuildLayoutImmediate(content);
			float bottom = 0f;
			Vector3[] corners = new Vector3[4];
			foreach (RaftControlsKeyRow r in box.GetComponentsInChildren<RaftControlsKeyRow>(true))
			{
				((RectTransform)r.transform).GetWorldCorners(corners);
				bottom = Mathf.Max(bottom, -content.InverseTransformPoint(corners[0]).y);
			}
			foreach (KeybindInterface k in box.GetComponentsInChildren<KeybindInterface>(false))
			{
				RectTransform kr = (RectTransform)k.transform;
				if (kr.rect.height < 1f || kr.rect.height > 200f) continue;
				kr.GetWorldCorners(corners);
				bottom = Mathf.Max(bottom, -content.InverseTransformPoint(corners[0]).y);
			}
			// (content's pivot is its top: local y below it is negative; the rect starts at its top edge)
			bottom += content.rect.yMax + 20f;
			if (bottom > content.rect.height + 1f) { Vector2 s = content.sizeDelta; s.y += bottom - content.rect.height; content.sizeDelta = s; }
		}

		/// <summary>The key a row shows, as Raft writes keys in its own rows.</summary>
		public static string Shown(KeyCode k) { return k.ToShortString(); }
	}

	/// <summary>One of the mod's rows in Raft's Controls list: its main key button waits for a key as Raft's do.</summary>
	public class RaftControlsKeyRow : MonoBehaviour
	{
		public bool ForJournal;
		public Text MainText, AltText;
		public Button MainButton, AltButton;
		ColorBlock colors;
		bool waiting;
		/// <summary>Why the last pressed key was refused (for tests), or null.</summary>
		public string LastRefusal;

		public bool Waiting { get { return waiting; } }
		KeyCode Bound { get { return ForJournal ? ModKeys.Journal : ModKeys.Stats; } }

		internal void Setup(bool forJournal, KeyConnection main, KeyConnection alt, ColorBlock colors)
		{
			ForJournal = forJournal;
			this.colors = colors;
			MainText = main != null ? main.text : null;
			AltText = alt != null ? alt.text : null;
			MainButton = main != null ? main.button : null;
			AltButton = alt != null ? alt.button : null;
			if (MainButton != null)
			{
				MainButton.onClick = new Button.ButtonClickedEvent();
				MainButton.onClick.AddListener(StartAssign);
				Hover(MainButton, MainText);
			}
			if (AltButton != null)
			{
				AltButton.onClick = new Button.ButtonClickedEvent();
				AltButton.interactable = false;
				EventTrigger et = AltButton.GetComponent<EventTrigger>();
				if (et != null) et.triggers.Clear();
			}
			if (MainText != null) MainText.color = colors.normalColor;
			if (AltText != null) { AltText.text = RaftControlsKeys.Shown(KeyCode.None); AltText.color = colors.normalColor * new Color(1f, 1f, 1f, 0.45f); }
			Refresh();
		}

		void Hover(Button b, Text t)
		{
			EventTrigger et = b.GetComponent<EventTrigger>() ?? b.gameObject.AddComponent<EventTrigger>();
			et.triggers.Clear();
			var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
			enter.callback.AddListener(_ => { if (!waiting && !KeybindInterface.AssigningKey && t != null) t.color = colors.highlightedColor; });
			et.triggers.Add(enter);
			var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
			exit.callback.AddListener(_ => { if (!waiting && !KeybindInterface.AssigningKey && t != null) t.color = colors.normalColor; });
			et.triggers.Add(exit);
		}

		/// <summary>Waits for the new key (the main key button's click).</summary>
		public void StartAssign()
		{
			if (waiting || KeybindInterface.AssigningKey) return;
			waiting = true;
			LastRefusal = null;
			KeybindInterface.AssigningKey = true;
			ModKeys.Listen(true);
			if (MainText != null) { MainText.text = "-"; MainText.color = colors.highlightedColor; }
		}

		/// <summary>A key pressed while waiting (Update passes the real one; tests call it). Esc keeps the old key, a key
		/// that can never be one of the mod's keys is passed over (Raft passes over mouse buttons the same way).</summary>
		public void KeyPressed(KeyCode k)
		{
			if (!waiting || k == KeyCode.None) return;
			if (k != KeyCode.Escape)
			{
				string why = ModKeys.SetFromControls(ForJournal, k);
				if (why != null) { LastRefusal = why; return; }
			}
			Stop();
		}

		void Stop()
		{
			waiting = false;
			KeybindInterface.AssigningKey = false;
			ModKeys.Listen(false);
			if (MainText != null) MainText.color = colors.normalColor;
			Refresh();
		}

		void Refresh()
		{
			if (MainText != null && !waiting) { string s = RaftControlsKeys.Shown(Bound); if (MainText.text != s) MainText.text = s; }
		}

		void Update()
		{
			if (waiting) KeyPressed(ModKeys.Pressed());
			else Refresh();
		}

		void OnDisable() { if (waiting) Stop(); }
	}

	[HarmonyPatch(typeof(ControlsSettingsBox), "Load")]
	static class ControlsKeysLoadPatch
	{
		static void Postfix(ControlsSettingsBox __instance)
		{
			try { RaftControlsKeys.Ensure(__instance); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Journal/stats key rows in Raft's Controls settings: " + e); }
		}
	}

	[HarmonyPatch(typeof(ControlsSettingsBox), "SetFocus")]
	static class ControlsKeysFocusPatch
	{
		static void Postfix(ControlsSettingsBox __instance, bool gamepad)
		{
			try { RaftControlsKeys.Grow(__instance, gamepad); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Journal/stats key rows (list height): " + e.Message); }
		}
	}

	[HarmonyPatch(typeof(ControlsSettingsBox), "SetKeybindsToDefaultValue")]
	static class ControlsKeysResetPatch
	{
		static void Postfix()
		{
			try { ModKeys.ResetMain(); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Journal/stats keys reset: " + e.Message); }
		}
	}
}
