using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Tests before a release: every window at every screen size players use, Raft's settings the mod lives with.
	/// </summary>
	public static partial class DevTests
	{
		#region Every window at every screen size

		/// <summary>Screen sizes players use: small laptop, 4:3, HD, 16:10, full HD, 1440p, ultra-wide.</summary>
		static readonly Vector2Int[] ScreenSizes =
		{
			new Vector2Int(1024, 768), new Vector2Int(1280, 720), new Vector2Int(1366, 768), new Vector2Int(1600, 900),
			new Vector2Int(1920, 1080), new Vector2Int(1920, 1200), new Vector2Int(2560, 1440), new Vector2Int(3440, 1440),
		};

		/// <summary>The window classes of the editor (each a singleton with a private static instance, Open... and Close).</summary>
		static GameObject WindowObject(Type t)
		{
			FieldInfo f = t.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
			Component c = f != null ? f.GetValue(null) as Component : null;
			return c != null && c.gameObject.activeInHierarchy ? c.gameObject : null;
		}

		/// <summary>
		/// What of a window is off the screen: its panel and every control (buttons, fields, sliders, toggles) not inside a
		/// scrolling list (those scroll into view). Screen-space canvases: corners are pixels.
		/// </summary>
		static List<string> OffScreen(GameObject win, int w, int h)
		{
			var off = new List<string>();
			var parts = win.GetComponentsInChildren<RectTransform>(false).Where(r => r.name == "Panel" || r.GetComponent<Selectable>() != null);
			foreach (RectTransform r in parts)
			{
				if (r.GetComponentInParent<ScrollRect>() != null && r.GetComponent<ScrollRect>() == null && r.name != "Panel") continue;
				Vector3[] c = new Vector3[4];
				r.GetWorldCorners(c);
				if (c[0].x < -1f || c[0].y < -1f || c[2].x > w + 1f || c[2].y > h + 1f)
					off.Add(r.name + " (" + c[0].x.ToString("F0") + "," + c[0].y.ToString("F0") + " to " + c[2].x.ToString("F0") + "," + c[2].y.ToString("F0") + ")");
			}
			return off;
		}

		[ConsoleCommand(name: "CIScreens", docs: "Dev, editor: every window of the editor at 8 screen sizes (1024x768 to 3440x1440 ultra-wide): the editor's bars, the generator (each tab), world plans, island files, quests, story items, behaviours, the item, sound and note pickers, a text prompt and a choice - each panel and every control on the screen; pictures shot_screen_<w>x<h>_*. The screen size is put back after")]
		public static void ScreensCommand() { DynamicIslands.instance.StartCoroutine(ScreensRoutine()); }

		static IEnumerator ScreensRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			int w0 = Screen.width, h0 = Screen.height;
			FullScreenMode mode0 = Screen.fullScreenMode;
			GameObject root = GameObject.Find("PlacedObjects");
			Vector3 at = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 2f, 500f);
			EditorGameObject note = PlaceForTest("Note_Paper", at, root.transform);
			EditorGameObject chest = PlaceForTest("Loot_Chest", at + new Vector3(4f, 0f, 0f), root.transform);
			// (name, open it, its class)
			var windows = new List<KeyValuePair<string, KeyValuePair<Action, Type>>>
			{
				Win("generator", () => { GeneratorWindow.Open(); GeneratorWindow.ShowTab(0); }, typeof(GeneratorWindow)),
				Win("generator_2", () => { GeneratorWindow.Open(); GeneratorWindow.ShowTab(1); }, typeof(GeneratorWindow)),
				Win("generator_3", () => { GeneratorWindow.Open(); GeneratorWindow.ShowTab(2); }, typeof(GeneratorWindow)),
				Win("worldplans", () => WorldPlanWindow.Open(), typeof(WorldPlanWindow)),
				Win("islandfiles", IslandFilesWindow.Open, typeof(IslandFilesWindow)),
				Win("quest", QuestEditorWindow.Open, typeof(QuestEditorWindow)),
				Win("storyitems", StoryItemsWindow.Open, typeof(StoryItemsWindow)),
				Win("behaviours", BehaviourWindow.OpenIsland, typeof(BehaviourWindow)),
				Win("itempicker", () => ItemPickerWindow.Open(chest), typeof(ItemPickerWindow)),
				Win("soundpicker", () => SoundPickerWindow.OpenFor(s => { }), typeof(SoundPickerWindow)),
				Win("noteeditor", () => NoteEditorWindow.Open(note), typeof(NoteEditorWindow)),
				Win("prompt", () => TextPromptWindow.Open("A name", "A long question that a window like this asks, to see it wraps and fits", "some text", s => { }), typeof(TextPromptWindow)),
				Win("choice", () => ChoiceWindow.Open("Pick one", Enumerable.Range(1, 30).Select(i => new ChoiceWindow.Choice("c" + i, "Choice number " + i, "with a detail line")), s => { }), typeof(ChoiceWindow)),
			};
			try
			{
				foreach (Vector2Int size in ScreenSizes)
				{
					Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
					yield return new WaitForSecondsRealtime(1.2f);
					int w = Screen.width, h = Screen.height;
					string sz = w + "x" + h + (w != size.x || h != size.y ? " (asked " + size.x + "x" + size.y + ")" : "");
					// The editor itself: its top bar, tabs, the tool panel, the status bar
					var editorOff = OffScreen(EditorUI.Canvas.gameObject, w, h).Where(n => !n.StartsWith("Panel")).ToList();
					Check(ref ok, editorOff.Count == 0, sz + " the editor: " + (editorOff.Count == 0 ? "every control on the screen" : "off the screen: " + string.Join(", ", editorOff.Take(6).ToArray())));
					Screenshot(new[] { "screen_" + w + "x" + h + "_editor" });
					yield return new WaitForSecondsRealtime(0.6f);
					foreach (var win in windows)
					{
						try { win.Value.Key(); } catch (Exception e) { Check(ref ok, false, sz + " " + win.Key + ": opening it threw " + e.Message); continue; }
						yield return null; yield return null; yield return null;
						GameObject go = WindowObject(win.Value.Value);
						if (go == null) { Check(ref ok, false, sz + " " + win.Key + ": didn't open"); continue; }
						var off = OffScreen(go, w, h);
						Check(ref ok, off.Count == 0, sz + " " + win.Key + ": " + (off.Count == 0 ? "on the screen" : "off the screen: " + string.Join(", ", off.Take(5).ToArray())));
						if (size.x == 1280 || size.x == 3440 || off.Count > 0) { Screenshot(new[] { "screen_" + w + "x" + h + "_" + win.Key }); yield return new WaitForSecondsRealtime(0.6f); }
						MethodInfo close = win.Value.Value.GetMethod("Close", BindingFlags.Public | BindingFlags.Static);
						if (close != null) close.Invoke(null, null);
						yield return null;
					}
				}
			}
			finally { Screen.SetResolution(w0, h0, mode0); }
			yield return new WaitForSecondsRealtime(1f);
			foreach (EditorGameObject e in new[] { note, chest }) if (e != null) UnityEngine.Object.Destroy(e.gameObject);
			Check(ref ok, Screen.width == w0 && Screen.height == h0, "the screen size put back: " + Screen.width + "x" + Screen.height);
			if (ok) Log("PASS: every window at every screen size"); else Fail("every window at every screen size");
		}

		static KeyValuePair<string, KeyValuePair<Action, Type>> Win(string name, Action open, Type t) { return new KeyValuePair<string, KeyValuePair<Action, Type>>(name, new KeyValuePair<Action, Type>(open, t)); }

		[ConsoleCommand(name: "CIScreensMenu", docs: "Dev, main menu: Raft's New Game box with the mod's panels (world plan, randomizer, world rules) at 8 screen sizes: the box, every one of its buttons, sliders and fields - Raft's Create too - on the screen, and each of the mod's panels inside the box; pictures shot_screen_<w>x<h>_newgame. The screen size is put back after")]
		public static void ScreensMenuCommand() { DynamicIslands.instance.StartCoroutine(ScreensMenuRoutine()); }

		static IEnumerator ScreensMenuRoutine()
		{
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("every screen size in the New Game box: no New Game box (main menu?)"); yield break; }
			bool ok = true;
			int w0 = Screen.width, h0 = Screen.height;
			FullScreenMode mode0 = Screen.fullScreenMode;
			try
			{
				foreach (Vector2Int size in ScreenSizes)
				{
					Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
					yield return new WaitForSecondsRealtime(1.2f);
					int w = Screen.width, h = Screen.height;
					box.gameObject.SetActive(true);
					box.Open();
					yield return new WaitForSecondsRealtime(1f);
					var off = OffScreen(box.gameObject, w, h);
					// (the box's own rectangle, and each of the mod's panels inside it)
					RectTransform boxRect = (RectTransform)box.transform;
					Vector3[] b = new Vector3[4];
					boxRect.GetWorldCorners(b);
					if (b[0].x < -1f || b[0].y < -1f || b[2].x > w + 1f || b[2].y > h + 1f) off.Add("the box itself");
					var outside = new List<string>();
					foreach (Transform t in box.transform)
					{
						if (!t.name.StartsWith("CustomIslands_") || !t.gameObject.activeInHierarchy) continue;
						Vector3[] c = new Vector3[4];
						((RectTransform)t).GetWorldCorners(c);
						if (c[0].x < b[0].x - 1f || c[0].y < b[0].y - 1f || c[2].x > b[2].x + 1f || c[2].y > b[2].y + 1f) outside.Add(t.name);
					}
					Check(ref ok, off.Count == 0 && outside.Count == 0, w + "x" + h + " New Game box: " + (off.Count == 0 && outside.Count == 0 ? "every control on the screen, the mod's panels inside the box" :
						(off.Count > 0 ? "off the screen: " + string.Join(", ", off.Take(5).ToArray()) : "") + (outside.Count > 0 ? " sticking out of the box: " + string.Join(", ", outside.ToArray()) : "")));
					if (size.x == 1024 || size.x == 1280 || size.x == 3440 || off.Count > 0 || outside.Count > 0) { Screenshot(new[] { "screen_" + w + "x" + h + "_newgame" }); yield return new WaitForSecondsRealtime(0.6f); }
					box.gameObject.SetActive(false);
					yield return null;
				}
			}
			finally { Screen.SetResolution(w0, h0, mode0); box.gameObject.SetActive(false); }
			yield return new WaitForSecondsRealtime(1f);
			if (ok) Log("PASS: every screen size in the New Game box"); else Fail("every screen size in the New Game box");
		}

		#endregion

		#region Raft's own settings

		/// <summary>A type of Raft's by its full name, from whichever of the game's assemblies has it.</summary>
		static Type RaftType(string fullName)
		{
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type t = null;
				try { t = a.GetType(fullName, false); } catch { }
				if (t != null) return t;
			}
			return null;
		}

		[ConsoleCommand(name: "CIRaftSettings", docs: "Dev, world (host, a custom island loaded near the raft): Raft's own settings as a player sets them (the settings screen's dropdowns, toggles and sliders, so Raft applies them): every graphics quality, water, texture, shadow, reflection and FPS option, ambient occlusion and anti-aliasing, FOV at its ends; then every language of Raft's. After each: no error of the mod, the island still there with its ground drawn, the mod's item names, journal and quest panel still work. Everything put back after; pictures shot_settings_*")]
		public static void RaftSettingsCommand() { DynamicIslands.instance.StartCoroutine(RaftSettingsRoutine()); }

		static IEnumerator RaftSettingsRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded) { Fail("Raft's settings: run in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			IslandWorldState.Entry island = IslandWorldState.Islands.Where(e => e.Root != null && e.Root.GetComponentInChildren<Terrain>() != null).OrderBy(e => (e.Position - (CustomIslandSpawner.RaftPosition ?? Vector3.zero)).sqrMagnitude).FirstOrDefault();
			if (island == null) { Fail("Raft's settings: no custom island with land loaded (SpawnIsland citest first)"); yield break; }
			int objects = island.Root.GetComponentsInChildren<IslandObjectRef>(true).Length;
			var errors = new List<string>();
			Application.LogCallback watch = (text, trace, type) =>
			{
				if ((type == LogType.Error || type == LogType.Exception) && (text.Contains("CUSTOM ISLANDS") || (trace ?? "").Contains("DynamicIslands"))) errors.Add(text.Length > 160 ? text.Substring(0, 160) : text);
			};
			Func<string> islandState = () =>
			{
				if (island.Root == null) return "the island is gone";
				Terrain t = island.Root.GetComponentInChildren<Terrain>();
				int now = island.Root.GetComponentsInChildren<IslandObjectRef>(true).Length;
				if (t == null || !t.enabled || !t.drawHeightmap || t.materialTemplate == null) return "its ground isn't drawn";
				if (now != objects) return "objects " + objects + " -> " + now;
				return null;
			};
			Application.logMessageReceived += watch;
			try
			{
				// Graphics: every option of every dropdown, then the toggles and the field of view
				Type boxType = RaftType("GraphicsSettingsBox");
				Component box = boxType == null ? null : Resources.FindObjectsOfTypeAll(boxType).OfType<Component>().FirstOrDefault(c => c.gameObject.scene.IsValid());
				if (box == null) Check(ref ok, false, "Raft's graphics settings box not found");
				else
				{
					Type bt = box.GetType();
					var dropdowns = new[] { "qualitySettingsDropdown", "waterQualityDropdown", "textureQualityDropdown", "shadowTypeDropdown", "shadowCascadeDropdown", "shadowResolutionDropdown", "reflectionsDropdown", "fpsCapDropdown" };
					foreach (string name in dropdowns)
					{
						FieldInfo f = bt.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
						Dropdown d = f != null ? f.GetValue(box) as Dropdown : null;
						if (d == null) { Log("  (no " + name + " in this Raft)"); continue; }
						int was = d.value;
						var bad = new List<string>();
						for (int i = 0; i < d.options.Count; i++)
						{
							errors.Clear();
							d.value = i; // (Raft's own handler applies it)
							yield return new WaitForSecondsRealtime(0.8f);
							string s = islandState();
							if (errors.Count > 0 || s != null) bad.Add(d.options[i].text + ": " + (s ?? errors[0]));
							if (name == "qualitySettingsDropdown" && (i == 0 || i == d.options.Count - 1)) { Screenshot(new[] { "settings_quality_" + i }); yield return new WaitForSecondsRealtime(0.6f); }
						}
						d.value = was;
						yield return new WaitForSecondsRealtime(0.5f);
						Check(ref ok, bad.Count == 0, "graphics " + name.Replace("Dropdown", "") + ": " + d.options.Count + " options" + (bad.Count == 0 ? ", the island fine with each" : " - " + string.Join("; ", bad.Take(3).ToArray())));
					}
					foreach (string name in new[] { "aoToggle", "aaToggle", "vsyncToggle" })
					{
						FieldInfo f = bt.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
						Toggle tg = f != null ? f.GetValue(box) as Toggle : null;
						if (tg == null) continue;
						bool was = tg.isOn;
						errors.Clear();
						tg.isOn = !was; yield return new WaitForSecondsRealtime(0.5f);
						string s = islandState();
						tg.isOn = was; yield return new WaitForSecondsRealtime(0.3f);
						Check(ref ok, errors.Count == 0 && s == null, "graphics " + name.Replace("Toggle", "") + " switched: " + (s ?? (errors.Count > 0 ? errors[0] : "the island fine")));
					}
					FieldInfo ff = bt.GetField("FOVSlider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					Slider fov = ff != null ? ff.GetValue(box) as Slider : null;
					if (fov != null)
					{
						float was = fov.value;
						foreach (float v in new[] { fov.minValue, fov.maxValue })
						{
							errors.Clear();
							fov.value = v; yield return new WaitForSecondsRealtime(0.5f);
							Check(ref ok, errors.Count == 0 && islandState() == null, "field of view " + v.ToString("F0") + ": the island fine");
						}
						fov.value = was;
					}
				}

				// Languages: every one of Raft's; the mod's texts that come from Raft (item names) and its own windows
				Type lm = RaftType("I2.Loc.LocalizationManager");
				if (lm == null) Log("  (no I2 localization in this Raft: languages not checked)");
				else
				{
					PropertyInfo current = lm.GetProperty("CurrentLanguage", BindingFlags.Public | BindingFlags.Static);
					MethodInfo all = lm.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "GetAllLanguages");
					string was = current != null ? current.GetValue(null, null) as string : null;
					var langs = new List<string>();
					if (all != null)
					{
						object list = all.Invoke(null, all.GetParameters().Select(p => p.ParameterType == typeof(bool) ? (object)true : null).ToArray());
						if (list is IEnumerable) langs = ((IEnumerable)list).Cast<object>().Select(o => o.ToString()).ToList();
					}
					var bad = new List<string>();
					foreach (string lang in langs)
					{
						errors.Clear();
						try { current.SetValue(null, lang, null); } catch (Exception e) { bad.Add(lang + ": " + e.Message); continue; }
						yield return new WaitForSecondsRealtime(0.5f);
						string plank = ContentCatalog.ItemLabel("Plank"), rope = ContentCatalog.ItemLabel("Rope");
						try { JournalWindow.Open(); JournalWindow.Close(); IslandInfo.Show("Quest", "", "A test line"); } catch (Exception e) { bad.Add(lang + ": journal or quest panel: " + e.Message); }
						yield return null;
						if (string.IsNullOrEmpty(plank) || string.IsNullOrEmpty(rope) || errors.Count > 0) bad.Add(lang + ": items '" + plank + "', '" + rope + "'" + (errors.Count > 0 ? ", " + errors[0] : ""));
					}
					if (was != null && current != null) current.SetValue(null, was, null);
					Check(ref ok, langs.Count > 0 && bad.Count == 0, langs.Count + " languages (" + string.Join(", ", langs.Take(12).ToArray()) + ")" + (bad.Count == 0 ? ": item names, journal and quest panel fine in each" : " - " + string.Join("; ", bad.Take(4).ToArray())));
				}
			}
			finally { Application.logMessageReceived -= watch; }
			if (ok) Log("PASS: Raft's settings"); else Fail("Raft's settings");
		}

		#endregion
	}
}
