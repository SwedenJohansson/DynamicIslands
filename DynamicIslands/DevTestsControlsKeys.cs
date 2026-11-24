using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HMLLibrary;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>3.5: the journal and stats keys as rows in Raft's Settings > Controls (RaftControlsKeys).</summary>
	public static partial class DevTests
	{
		static Settings FindSettings()
		{
			Settings s = UnityEngine.Object.FindObjectOfType<Settings>();
			if (s != null) return s;
			foreach (Settings x in Resources.FindObjectsOfTypeAll<Settings>()) if (x.gameObject.scene.isLoaded) return x;
			return null;
		}

		[ConsoleCommand(name: "CIControlsDump", docs: "Dev: writes Raft's Settings > Controls list (its rows: names, components, places, sizes, texts) to Mods\\DynamicIslands\\controls_dump.txt; opens the settings for it")]
		public static void ControlsDumpCommand() { StartTest(ControlsDumpRoutine()); }

		static IEnumerator ControlsDumpRoutine()
		{
			Settings s = FindSettings();
			if (s == null || s.controls == null) { Fail("controls dump: no Settings"); yield break; }
			s.Open();
			yield return null; yield return null;
			ControlsSettingsBox box = s.controls;
			var sb = new StringBuilder();
			RectTransform content = Traverse.Create(box).Field("contentBox").GetValue<RectTransform>();
			sb.AppendLine("box " + CkPath(box.transform) + " content " + (content != null ? CkPath(content) : "null"));
			for (Transform p = box.transform; p != null; p = p.parent) sb.AppendLine("  up " + Line(p));
			if (content != null) Dump(sb, content, 0, 3);
			File.WriteAllText(System.IO.Path.Combine(DynamicIslands.assetpath, "controls_dump.txt"), sb.ToString());
			s.Close();
			Log("PASS: controls dump written");
		}

		static string CkPath(Transform t) { string p = t.name; for (Transform x = t.parent; x != null; x = x.parent) p = x.name + "/" + p; return p; }

		static string Line(Transform t)
		{
			RectTransform r = t as RectTransform;
			string comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray());
			string s = t.name + (t.gameObject.activeSelf ? "" : " (off)") + " [" + comps + "]";
			if (r != null) s += " amin " + r.anchorMin + " amax " + r.anchorMax + " piv " + r.pivot + " pos " + r.anchoredPosition + " size " + r.sizeDelta + " rect " + r.rect.size;
			Text tx = t.GetComponent<Text>();
			if (tx != null) s += " text '" + tx.text + "' " + (tx.font != null ? tx.font.name : "") + " " + tx.fontSize + " " + tx.color + " " + tx.alignment;
			LayoutGroup lg = t.GetComponent<LayoutGroup>();
			if (lg is VerticalLayoutGroup) s += " vspacing " + ((VerticalLayoutGroup)lg).spacing;
			return s;
		}

		static void Dump(StringBuilder sb, Transform t, int depth, int max)
		{
			sb.AppendLine(new string(' ', depth * 2) + Line(t));
			if (depth >= max) return;
			bool isRow = t.GetComponent<KeybindInterface>() != null || t.GetComponent<RaftControlsKeyRow>() != null;
			foreach (Transform c in t) Dump(sb, c, depth + 1, isRow || depth < 2 ? max : depth + 1);
		}

		[ConsoleCommand(name: "CIControlsKeys", docs: "Dev, main menu or world: the journal and stats rows at the bottom of Raft's Settings > Controls - look like Raft's rows, not saved as Raft keybinds; rebinding through a row (Esc keeps, a typing key is passed over, a Raft key is taken like Raft does) updates ModKeys, world_rules.txt and the hints; Raft's reset to default gives J and K; picture shot_controls_keys_<menu|world>.png")]
		public static void ControlsKeysCommand() { StartTest(ControlsKeysRoutine()); }

		static IEnumerator ControlsKeysRoutine()
		{
			bool ok = true;
			bool inWorld = !Raft_Network.InMenuScene;
			Settings s = FindSettings();
			if (s == null || s.controls == null) { Fail("controls keys: no Settings"); yield break; }
			KeyCode journalWas = ModKeys.Journal, statsWas = ModKeys.Stats;
			PauseMenu pause = inWorld ? UnityEngine.Object.FindObjectOfType<PauseMenu>() : null;
			if (inWorld && pause != null) { CallPrivate(pause, "Pause"); yield return new WaitForSecondsRealtime(0.4f); }
			s.Open();
			ControlsSettingsBox box = s.controls;
			TabGroup tabs = Traverse.Create(s).Field("tabGroup").GetValue<TabGroup>();
			if (tabs != null)
				foreach (TabButton b in tabs.tabButtons)
					if (b != null && b.Tab != null && box.transform.IsChildOf(b.Tab.transform)) { tabs.OpenTab(b.Tab); break; }
			box.SetFocus(false);
			yield return null; yield return null;

			RaftControlsKeyRow[] rows = box.GetComponentsInChildren<RaftControlsKeyRow>(true);
			RaftControlsKeyRow jr = rows.FirstOrDefault(r => r.name == RaftControlsKeys.JournalRow), sr = rows.FirstOrDefault(r => r.name == RaftControlsKeys.StatsRow);
			Check(ref ok, rows.Length == 2 && jr != null && sr != null, "two rows in Raft's Controls list (" + rows.Length + ")");
			if (jr == null || sr == null) { s.Close(); if (pause != null) { CanvasHelper ch = ComponentManager<CanvasHelper>.Value ?? UnityEngine.Object.FindObjectOfType<CanvasHelper>(); if (ch != null) ch.CloseMenu(MenuType.PauseMenu); } Fail("controls keys"); yield break; }

			KeybindInterface[] raftRows = box.GetComponentsInChildren<KeybindInterface>(true);
			KeybindInterface lastRaft = raftRows[raftRows.Length - 1];
			RectTransform lr = (RectTransform)lastRaft.transform, jrt = (RectTransform)jr.transform, srt = (RectTransform)sr.transform;
			Check(ref ok, jr.GetComponent<KeybindInterface>() == null && !MyInput.Keybinds.ContainsKey(RaftControlsKeys.JournalRow) && raftRows.Length == MyInput.Keybinds.Count,
				"rows are not Raft keybinds (" + raftRows.Length + " Raft rows, " + MyInput.Keybinds.Count + " keybinds)");
			Check(ref ok, jrt.parent == lr.parent && jrt.position.y < lr.position.y - 1f && srt.position.y < jrt.position.y - 1f, "below Raft's last row (" + lastRaft.Identifier + "): y " + lr.position.y.ToString("F0") + " > " + jrt.position.y.ToString("F0") + " > " + srt.position.y.ToString("F0"));
			Check(ref ok, Mathf.Abs(jrt.rect.height - lr.rect.height) < 0.5f && Mathf.Abs(jrt.position.x - lr.position.x) < 0.5f, "same size and column as Raft's rows");
			Text jLabel = jr.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t != jr.MainText && t != jr.AltText);
			Check(ref ok, jLabel != null && jLabel.text == RaftControlsKeys.JournalLabel, "journal label: " + (jLabel != null ? jLabel.text : "none"));
			Check(ref ok, jLabel != null && jLabel.preferredWidth <= ((RectTransform)jLabel.transform).rect.width + 1f, "label fits (" + (jLabel != null ? jLabel.preferredWidth.ToString("F0") + "/" + ((RectTransform)jLabel.transform).rect.width.ToString("F0") : "") + ")");
			Text sLabel = sr.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t != sr.MainText && t != sr.AltText);
			Check(ref ok, sLabel != null && sLabel.preferredWidth <= ((RectTransform)sLabel.transform).rect.width + 1f, "stats label fits");
			Check(ref ok, jr.MainText != null && jr.MainText.text == ModKeys.Journal.ToShortString() && sr.MainText.text == ModKeys.Stats.ToShortString(), "main keys shown: " + (jr.MainText != null ? jr.MainText.text : "?") + " / " + (sr.MainText != null ? sr.MainText.text : "?"));
			Check(ref ok, jr.AltText != null && jr.AltText.text == KeyCode.None.ToShortString() && jr.AltButton != null && !jr.AltButton.interactable, "alt key: '" + (jr.AltText != null ? jr.AltText.text : "?") + "', not settable");

			// rebind the journal through its row
			KeyCode pick = new[] { KeyCode.L, KeyCode.P, KeyCode.Semicolon }.First(k => k != journalWas && k != statsWas);
			jr.MainButton.onClick.Invoke();
			Check(ref ok, jr.Waiting && KeybindInterface.AssigningKey && ModKeys.Listening && jr.MainText.text == "-", "click: waits for a key ('-')");
			jr.KeyPressed(KeyCode.Escape);
			Check(ref ok, !jr.Waiting && !KeybindInterface.AssigningKey && ModKeys.Journal == journalWas, "Esc keeps " + journalWas);
			jr.MainButton.onClick.Invoke();
			jr.KeyPressed(KeyCode.Return);
			Check(ref ok, jr.Waiting && jr.LastRefusal != null && ModKeys.Journal == journalWas, "Return passed over (" + jr.LastRefusal + ")");
			jr.KeyPressed(pick);
			yield return null;
			Check(ref ok, !jr.Waiting && ModKeys.Journal == pick && jr.MainText.text == pick.ToShortString(), "journal -> " + pick + " (row shows " + jr.MainText.text + ")");
			Check(ref ok, string.Equals(WorldRules.ReadDefault(ModKeys.JournalSetting), pick.ToString(), StringComparison.OrdinalIgnoreCase), "world_rules.txt journalkey=" + WorldRules.ReadDefault(ModKeys.JournalSetting));
			Check(ref ok, ModKeys.JournalName == ModKeys.Name(pick), "hint name: " + ModKeys.JournalName);
			if (inWorld && HotkeyHints.KeyShown(HotkeyHints.JournalName) != null)
				Check(ref ok, HotkeyHints.KeyShown(HotkeyHints.JournalName) == ModKeys.Name(pick), "hotbar tab: " + HotkeyHints.KeyShown(HotkeyHints.JournalName));

			// a key Raft uses is taken, as Raft's own rows take it
			KeyCode raftKey = KeyCode.None; string raftId = null;
			foreach (var kb in MyInput.Keybinds) if (kb.Value != null && ModKeys.HardProblem(kb.Value.MainKey) == null && kb.Value.MainKey != pick && kb.Value.MainKey != journalWas) { raftKey = kb.Value.MainKey; raftId = kb.Key; break; }
			if (raftKey != KeyCode.None)
			{
				sr.MainButton.onClick.Invoke();
				sr.KeyPressed(raftKey);
				yield return null;
				Check(ref ok, ModKeys.Stats == raftKey && sr.MainText.text == raftKey.ToShortString() && MyInput.Keybinds[raftId].MainKey == raftKey, "stats -> " + raftKey + " (Raft's " + raftId + " key too, kept)");
				Check(ref ok, string.Equals(WorldRules.ReadDefault(ModKeys.StatsSetting), raftKey.ToString(), StringComparison.OrdinalIgnoreCase), "world_rules.txt statskey=" + WorldRules.ReadDefault(ModKeys.StatsSetting));
			}

			// picture of the list's bottom (scrolled down)
			ScrollRect scroll = box.GetComponentInParent<ScrollRect>() ?? box.GetComponentInChildren<ScrollRect>(true);
			if (scroll != null) { scroll.verticalNormalizedPosition = 0f; Canvas.ForceUpdateCanvases(); }
			yield return new WaitForSecondsRealtime(0.3f);
			string shot = System.IO.Path.GetFullPath(System.IO.Path.Combine(DynamicIslands.assetpath, "shot_controls_keys_" + (inWorld ? "world" : "menu") + ".png"));
			ScreenCapture.CaptureScreenshot(shot);
			yield return new WaitForSecondsRealtime(0.5f);
			Log("controls picture: " + shot + " (" + Screen.width + "x" + Screen.height + ")");

			// Raft's reset to default: J and K (Raft's own keys put back after)
			var raftKeys = MyInput.Keybinds.ToDictionary(k => k.Key, k => new KeyCode[] { k.Value.MainKey, k.Value.AltKey });
			box.SetKeybindsToDefaultValue();
			yield return null;
			Check(ref ok, ModKeys.Journal == ModKeys.JournalDefault && ModKeys.Stats == ModKeys.StatsDefault && jr.MainText.text == "J" && sr.MainText.text == "K", "reset to default: " + ModKeys.Journal + " / " + ModKeys.Stats);
			Check(ref ok, WorldRules.ReadDefault(ModKeys.JournalSetting) == "J" && WorldRules.ReadDefault(ModKeys.StatsSetting) == "K", "world_rules.txt after reset: " + WorldRules.ReadDefault(ModKeys.JournalSetting) + "/" + WorldRules.ReadDefault(ModKeys.StatsSetting));
			foreach (KeybindInterface ki in raftRows)
			{
				KeyCode[] was;
				if (ki.Keybind != null && raftKeys.TryGetValue(ki.Identifier, out was)) { ki.Keybind.MainKey = was[0]; ki.Keybind.AltKey = was[1]; ki.Set(ki.Keybind); }
			}

			// the Defaults window's keys follow (one source): set there, the row shows it
			string why = ModKeys.Set(true, journalWas == KeyCode.J ? KeyCode.Semicolon : journalWas);
			yield return null;
			Check(ref ok, why == null && jr.MainText.text == ModKeys.Journal.ToShortString(), "set in Defaults -> row shows " + jr.MainText.text + (why != null ? " (" + why + ")" : ""));

			ModKeys.SetFromControls(true, journalWas);
			ModKeys.SetFromControls(false, statsWas);
			s.Close();
			if (pause != null) { CanvasHelper ch = ComponentManager<CanvasHelper>.Value ?? UnityEngine.Object.FindObjectOfType<CanvasHelper>(); if (ch != null) ch.CloseMenu(MenuType.PauseMenu); }
			yield return null;
			if (ok) Log("PASS: controls keys (" + (inWorld ? "world" : "main menu") + ")"); else Fail("controls keys");
		}
	}
}
