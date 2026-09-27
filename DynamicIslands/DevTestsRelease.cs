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


		#region A story plan made from the player's own islands, played in a new world

		const string StoryPlan = "CI Story";
		static readonly string[] StoryIslands = { "cistory-home", "cistory-bay" };

		/// <summary>A button of a window by its label (UIKit names them Button_&lt;label&gt;), clicked as a player would.</summary>
		static bool Click(GameObject root, string label)
		{
			Button b = root.GetComponentsInChildren<Button>(false).FirstOrDefault(x => x.name == "Button_" + label || (UIKit.LabelOf(x) != null && UIKit.LabelOf(x).text == label));
			if (b == null || !b.interactable) return false;
			b.onClick.Invoke();
			return true;
		}

		/// <summary>Types into a window's field found by its placeholder text, as a player would (the field applies it when left).</summary>
		static bool TypeInto(GameObject root, string placeholder, string text, int nth = 0)
		{
			InputField f = root.GetComponentsInChildren<InputField>(false).Where(x => x.placeholder is Text && ((Text)x.placeholder).text == placeholder).Skip(nth).FirstOrDefault();
			if (f == null) return false;
			f.text = text;
			f.onEndEdit.Invoke(text);
			return true;
		}

		/// <summary>Clicks a cycling button (its label changes each click) until it says one of the wanted labels.</summary>
		static IEnumerator CycleTo(Func<GameObject> card, string[] all, string want)
		{
			for (int i = 0; i < all.Length + 1; i++)
			{
				GameObject c = card();
				if (c == null) yield break;
				Button b = c.GetComponentsInChildren<Button>(false).FirstOrDefault(x => UIKit.LabelOf(x) != null && all.Contains(UIKit.LabelOf(x).text));
				if (b == null || UIKit.LabelOf(b).text == want) yield break;
				b.onClick.Invoke();
				yield return null; yield return null;
			}
		}

		[ConsoleCommand(name: "CIStoryPlanMake", docs: "Dev, editor: a player's story plan made in the World Plans window from their own islands: two islands saved (a home island with a gate zone and a quest, a bay with a chest and a quest); a new plan 'CI Story' through New... and its name prompt; four rules added with + Add a rule and filled in through each card's fields and cycling buttons (start: home ahead; home's gate zone fires: the bay north of home; the bay's quest done: a treasure island east of the bay; after 2 km: an oddity ahead); a description; Check finds no problem; Save; closed, opened again: the same plan. Then Copy..., Delete, Templates..., the rule arrows and remove, the random islands switch on a scratch plan")]
		public static void StoryPlanMakeCommand() { DynamicIslands.instance.StartCoroutine(StoryPlanMakeRoutine()); }

		static IEnumerator StoryPlanMakeRoutine()
		{
			yield return WaitForEditor(false);
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			// Two islands of the player's own, as the generator and Save as make them
			for (int i = 0; i < StoryIslands.Length; i++)
			{
				var s = new IslandGenSettings { Seed = 3100 + i, Style = TerrainPainter.Tropical, Shape = IslandShapes.Round, Radius = 55f, Height = 14f, ObjectDensity = 0.6f };
				IslandFile f = IslandGenerator.CreateFile(s, StoryIslands[i]);
				var k = new MapKit(f, 7 + i);
				Vector2? spot = k.Find(k.Mid, 25f, MapKit.Dry, 6f);
				Vector2 at = spot ?? k.Mid;
				f.Props[IslandProps.Title] = i == 0 ? "Home Island" : "The Bay";
				if (i == 0)
				{
					k.Zone(at, "gate", 5f, "The gate opens: something appears to the north");
					k.Quest("Find the gate", "Look for the gate on this island.", "The gate is found!", "Plank*3", "reach|gate|1|Walk to the gate");
				}
				else
				{
					k.Chest("Loot_Chest", at, "Bay chest", "Rope*2");
					k.Quest("The bay's chest", "A chest lies in the bay.", "You found the bay's chest", "Nail*4", "open|Bay chest|1|Open the bay's chest");
				}
				f.Save(IslandSpawner.PathFor(StoryIslands[i]));
			}
			Check(ref ok, StoryIslands.All(n => System.IO.File.Exists(IslandSpawner.PathFor(n))), "two islands saved: " + string.Join(", ", StoryIslands));
			if (System.IO.File.Exists(WorldPlan.PathFor(StoryPlan))) System.IO.File.Delete(WorldPlan.PathFor(StoryPlan));
			if (System.IO.File.Exists(WorldPlan.PathFor(StoryPlan + " copy"))) System.IO.File.Delete(WorldPlan.PathFor(StoryPlan + " copy"));

			// The World Plans window, as the player uses it
			WorldPlanWindow.Open();
			yield return null; yield return null;
			GameObject win = WindowObject(typeof(WorldPlanWindow));
			Check(ref ok, win != null, "World Plans opens");
			if (win == null) { Fail("story plan made"); yield break; }
			Check(ref ok, Click(win, "New..."), "New... clicked");
			yield return null; yield return null;
			GameObject prompt = WindowObject(typeof(TextPromptWindow));
			Check(ref ok, prompt != null && TypeInto(prompt, "Name", StoryPlan) && Click(prompt, "OK"), "the name prompt: '" + StoryPlan + "', OK");
			yield return null; yield return null;
			Func<List<GameObject>> cards = () => win.GetComponentsInChildren<RectTransform>(false).Where(r => r.name == "Rule").Select(r => r.gameObject).ToList();
			string[] whenLabels = { "the world starts", "after sailing (km)", "on day", "quest done at", "quest step done at", "zone fires at", "players reach", "after rule", "signal sent at" };
			string[] whatLabels = { "saved island", "new map type", "from spawn pool", "one of these" };
			string[] whereLabels = { "ahead of the raft", "near an island" };
			string[] dirs = IntroRule.Directions.Select(d => d == "any" ? "any way" : d).ToArray();
			// (rule: id, when, when's island, when's arg, what, which, where, metres, direction, near which, message, Receiver name)
			var rules = new[]
			{
				new[] { "home", "the world starts", "", "", "saved island", StoryIslands[0], "ahead of the raft", "300", "", "", "A story begins", "Home" },
				new[] { "bay", "zone fires at", "home", "gate", "saved island", StoryIslands[1], "near an island", "600", "north", "home", "Something to the north", "Bay" },
				new[] { "treasure", "quest done at", "bay", "", "new map type", "treasure", "near an island", "600", "east", "bay", "", "Treasure" },
				new[] { "far", "after sailing (km)", "", "2", "new map type", "oddity", "ahead of the raft", "400", "", "", "", "" },
			};
			for (int i = 0; i < rules.Length; i++)
			{
				string[] r = rules[i];
				Check(ref ok, Click(win, "+ Add a rule"), "rule " + (i + 1) + ": + Add a rule");
				yield return null; yield return null;
				int idx = i;
				Func<GameObject> card = () => { var c = cards(); return idx < c.Count ? c[idx] : null; };
				yield return CycleTo(card, whenLabels, r[1]);
				yield return CycleTo(card, whatLabels, r[4]);
				yield return CycleTo(card, whereLabels, r[6]);
				if (r[8].Length > 0) yield return CycleTo(card, dirs, r[8]);
				GameObject cd = card();
				if (cd == null) { Check(ref ok, false, "rule " + (i + 1) + ": no card"); continue; }
				bool typed = TypeInto(cd, "id", r[0]);
				if (r[2].Length > 0) typed &= TypeInto(cd, "rule id / island", r[2]);
				if (r[3].Length > 0) typed &= TypeInto(cd, r[1] == "zone fires at" ? "zone name" : "km", r[3]);
				typed &= TypeInto(cd, r[4] == "new map type" ? "map type" : "island name", r[5]);
				typed &= TypeInto(cd, "300", r[7]);
				if (r[9].Length > 0) typed &= TypeInto(cd, "where it happened", r[9]);
				if (r[10].Length > 0) typed &= TypeInto(cd, "Message to every player (optional)", r[10]);
				if (r[11].Length > 0) typed &= TypeInto(cd, "Receiver name", r[11]);
				Check(ref ok, typed, "rule " + (i + 1) + " '" + r[0] + "': filled in through its card");
			}
			Check(ref ok, TypeInto(win, "Description, shown when choosing the plan (e.g. A story across five islands)", "A test story across four islands"), "the description typed");
			Check(ref ok, Click(win, "Check"), "Check clicked");
			yield return null;
			// (what Check found: the window's problems text)
			FieldInfo pt = typeof(WorldPlanWindow).GetField("problemsText", BindingFlags.Instance | BindingFlags.NonPublic);
			Text ptext = pt != null ? pt.GetValue(win.GetComponent<WorldPlanWindow>()) as Text : null;
			string problems = ptext != null ? ptext.text : "";
			Log("  Check says: " + problems.Replace("\n", " / "));
			Check(ref ok, Click(win, "Save"), "Save clicked");
			yield return null;
			WorldPlan saved = WorldPlan.Load(StoryPlan);
			Check(ref ok, saved != null && saved.Rules.Count == 4, "the plan saved with " + (saved != null ? saved.Rules.Count : 0) + " rules (4)" + (problems.Length > 0 ? "; Check says: " + problems.Replace("\n", " / ") : ""));
			if (saved != null)
			{
				for (int i = 0; i < rules.Length && i < saved.Rules.Count; i++)
				{
					IntroRule s = saved.Rules[i];
					string[] r = rules[i];
					string want = r[0] + "|" + r[5] + "|" + r[7];
					string got = s.Id + "|" + s.WhatArg + "|" + s.Distance.ToString("0");
					Check(ref ok, want == got && (r[8].Length == 0 || s.Direction == r[8]) && (r[3].Length == 0 || s.WhenArg == r[3]) && (r[2].Length == 0 || s.WhenRef == r[2]), "rule " + (i + 1) + " as typed: " + s.Describe());
				}
				Check(ref ok, saved.Description == "A test story across four islands", "the description saved");
			}
			Check(ref ok, Click(win, "Close"), "Close clicked");
			yield return null;
			WorldPlanWindow.Open(StoryPlan);
			yield return null; yield return null;
			Check(ref ok, cards().Count == 4, "opened again: its 4 rules (" + cards().Count + ")");

			// Copy..., then the copy: the random switch, a template, the arrows and remove, Delete
			Check(ref ok, Click(win, "Copy..."), "Copy... clicked");
			yield return null; yield return null;
			prompt = WindowObject(typeof(TextPromptWindow));
			if (prompt != null) Click(prompt, "OK"); // (the name it suggests: "CI Story copy")
			yield return null; yield return null;
			Check(ref ok, System.IO.File.Exists(WorldPlan.PathFor(StoryPlan + " copy")), "the copy saved as '" + StoryPlan + " copy'");
			int before = cards().Count;
			Button random = win.GetComponentsInChildren<Button>(false).FirstOrDefault(b => UIKit.LabelOf(b) != null && UIKit.LabelOf(b).text.StartsWith("Random islands while sailing"));
			string r0 = random != null ? UIKit.LabelOf(random).text : "";
			if (random != null) random.onClick.Invoke();
			yield return null;
			Check(ref ok, random != null && UIKit.LabelOf(random).text != r0, "the random islands switch: '" + r0 + "' -> '" + (random != null ? UIKit.LabelOf(random).text : "") + "'");
			Check(ref ok, Click(win, "Templates..."), "Templates... clicked");
			yield return null; yield return null;
			GameObject choice = WindowObject(typeof(ChoiceWindow));
			Button firstTemplate = choice != null ? choice.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name.StartsWith("Button_") && UIKit.LabelOf(b) != null && UIKit.LabelOf(b).text.Length > 0 && b.name != "Button_Close" && b.name != "Button_Cancel") : null;
			if (firstTemplate != null) firstTemplate.onClick.Invoke();
			yield return null; yield return null;
			int withTemplate = cards().Count;
			Check(ref ok, withTemplate > before, "a template adds its rules: " + before + " -> " + withTemplate);
			GameObject last = cards().LastOrDefault();
			string lastId = last != null ? last.GetComponentsInChildren<InputField>(false).Select(f => f.text).FirstOrDefault() : "";
			if (last != null) Click(last, "▲");
			yield return null; yield return null;
			var afterUp = cards();
			string movedId = afterUp.Count >= 2 ? afterUp[afterUp.Count - 2].GetComponentsInChildren<InputField>(false).Select(f => f.text).FirstOrDefault() : "";
			Check(ref ok, movedId == lastId, "the up arrow moves the last rule up one ('" + lastId + "')");
			GameObject first = cards().FirstOrDefault();
			if (first != null) Click(first, "×");
			yield return null; yield return null;
			Check(ref ok, cards().Count == withTemplate - 1, "the remove button takes a rule out: " + withTemplate + " -> " + cards().Count);
			Check(ref ok, Click(win, "Delete"), "Delete clicked");
			yield return null; yield return null;
			Check(ref ok, !System.IO.File.Exists(WorldPlan.PathFor(StoryPlan + " copy")) && System.IO.File.Exists(WorldPlan.PathFor(StoryPlan)), "the copy deleted, the story plan kept");
			WorldPlanWindow.Close();
			if (ok) Log("PASS: story plan made"); else Fail("story plan made");
		}

		[ConsoleCommand(name: "CIStoryPlay", docs: "Dev, in game (host, a new world made with the plan 'CI Story': CIPlanBox \"CI Story\" then CINewWorld): the plan played through. The home island comes ahead of the raft with its Receiver name and message, loaded exactly as saved (the same ground and every object of its file); its gate zone brings the bay 600 m north of it; the bay's quest (open its chest) brings a treasure island east of the bay; 2 km sailed bring an oddity. Each once, never twice. Logs STORY lines for CIStoryCheck")]
		public static void StoryPlayCommand() { DynamicIslands.instance.StartCoroutine(StoryPlayRoutine()); }

		/// <summary>An island in the world against its file: the same ground heights and every object of the file there.</summary>
		static string LoadedAsSaved(IslandWorldState.Entry e)
		{
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor(e.Name));
			if (f == null || e.Root == null) return "no file or not loaded";
			int spawned = e.Root.GetComponentsInChildren<IslandObjectRef>(true).Length;
			Transform objs = e.Root.transform.Find("Objects");
			int children = objs != null ? objs.childCount : spawned;
			Terrain t = e.Root.GetComponentInChildren<Terrain>();
			float fileTop = 0f, worldTop = 0f;
			foreach (float h in f.Heights) fileTop = Mathf.Max(fileTop, h * f.TerrainSize.y);
			if (t != null) foreach (float h in t.terrainData.GetHeights(0, 0, t.terrainData.heightmapResolution, t.terrainData.heightmapResolution)) worldTop = Mathf.Max(worldTop, h * t.terrainData.size.y);
			bool same = children == f.Objects.Count && (t == null || Mathf.Abs(fileTop - worldTop) < 0.05f);
			return (same ? "" : "DIFFERENT: ") + "objects " + children + " of the file's " + f.Objects.Count + ", highest ground " + worldTop.ToString("F2") + " m (file " + fileTop.ToString("F2") + ")";
		}

		static IEnumerator StoryPlayRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("story played: host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Check(ref ok, WorldDirector.Plan != null && WorldDirector.Plan.Name == StoryPlan, "the world's plan: '" + (WorldDirector.Plan != null ? WorldDirector.Plan.Name : "none") + "'");
			Func<string, IslandWorldState.Entry> byName = n => IslandWorldState.Islands.FirstOrDefault(e => e.HostName == n || e.Name == n);
			Func<string, IEnumerator> waitLoaded = n => WaitFor(() => byName(n) != null && byName(n).Root != null, 90f);
			// 1. The home island, at the start
			yield return waitLoaded(StoryIslands[0]);
			IslandWorldState.Entry home = byName(StoryIslands[0]);
			Check(ref ok, home != null && home.Root != null, "the world starts with '" + StoryIslands[0] + "'" + (home != null ? " (Receiver: '" + home.Label + "')" : ""));
			if (home == null || home.Root == null) { Fail("story played"); yield break; }
			Check(ref ok, home.Label == "Home", "its Receiver name 'Home' (" + home.Label + ")");
			string h1 = LoadedAsSaved(home);
			Check(ref ok, !h1.StartsWith("DIFFERENT"), "home loaded as saved: " + h1);
			// 2. Its gate zone brings the bay north of it
			TriggerZone gate = home.Root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.Id == "gate");
			Check(ref ok, gate != null, "home's gate zone");
			if (gate != null) { yield return PutPlayer(RAPI.GetLocalPlayer(), gate.transform.position + Vector3.up * 1.5f, false); yield return new WaitForSeconds(2f); }
			yield return waitLoaded(StoryIslands[1]);
			IslandWorldState.Entry bay = byName(StoryIslands[1]);
			if (bay != null)
			{
				Vector3 d = bay.Position - home.Position;
				float bearing = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
				Check(ref ok, bearing < 30f || bearing > 330f, "the gate brought the bay north of home: " + new Vector2(d.x, d.z).magnitude.ToString("F0") + " m at " + bearing.ToString("F0") + "°");
				if (bay.Root != null) Check(ref ok, !LoadedAsSaved(bay).StartsWith("DIFFERENT"), "the bay loaded as saved: " + LoadedAsSaved(bay));
				Check(ref ok, QuestTracker.StepOf(home) >= QuestTracker.QuestOf(home).Steps.Count, "home's quest done by reaching the gate");
			}
			else Check(ref ok, false, "the gate zone brought no bay");
			// 3. The bay's quest: open its chest; a treasure island east of the bay
			if (bay != null && bay.Root != null)
			{
				LootCrate chest = bay.Root.GetComponentsInChildren<LootCrate>(true).FirstOrDefault();
				if (chest != null) { PutPlayerNear(chest.transform); chest.Open(); NoteReader.Close(); }
				IslandWorldState.Entry treasure = null;
				for (float t = 0; t < 90f && treasure == null; t += 1f) { treasure = IslandWorldState.Islands.FirstOrDefault(e => e.Label == "Treasure"); if (treasure == null) yield return new WaitForSeconds(1f); }
				if (treasure != null)
				{
					Vector3 d = treasure.Position - bay.Position;
					float bearing = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
					Check(ref ok, bearing > 60f && bearing < 120f, "the bay's quest brought the treasure island east of it: " + bearing.ToString("F0") + "° ('" + treasure.Name + "')");
				}
				else Check(ref ok, false, "the bay's quest brought no treasure island");
			}
			// 4. 2 km sailed: an oddity ahead
			Raft raftObj = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftObj != null) yield return PutPlayer(RAPI.GetLocalPlayer(), raftObj.transform.position + Vector3.up * 2f, false);
			int before = IslandWorldState.Islands.Count;
			yield return SailRoutine(100f, 25f);
			yield return new WaitForSeconds(5f);
			bool far = IslandWorldState.Islands.Count > before && IslandWorldState.Islands.Skip(before).Any(e => (e.Name ?? "").StartsWith("gen-"));
			Check(ref ok, far, "2.5 km sailed: the 'far' rule brought an island (" + string.Join(", ", IslandWorldState.Islands.Skip(before).Select(e => e.Name).ToArray()) + ")");
			// Each rule once
			var names = IslandWorldState.Islands.Select(e => e.HostName ?? e.Name).ToList();
			Check(ref ok, names.Count(n => n == StoryIslands[0]) == 1 && names.Count(n => n == StoryIslands[1]) == 1 && IslandWorldState.Islands.Count(e => e.Label == "Treasure") == 1, "each island of the plan once");
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands) Log("STORY " + (e.HostName ?? e.Name) + " label=" + e.Label);
			Log("STORY done " + string.Join(",", WorldDirector.Done.OrderBy(x => x).ToArray()));
			if (ok) Log("PASS: story played"); else Fail("story played");
		}

		static IEnumerator WaitFor(Func<bool> done, float seconds)
		{
			for (float t = 0; t < seconds && !done(); t += 0.5f) yield return new WaitForSeconds(0.5f);
		}

		[ConsoleCommand(name: "CIStoryCheck", docs: "Dev, in game (host): after the story world was saved and loaded again: the plan still the world's, its islands the same (each once), the rules that fired don't fire again; logs STORY lines to compare with CIStoryPlay's")]
		public static void StoryCheckCommand() { DynamicIslands.instance.StartCoroutine(StoryCheckRoutine()); }

		static IEnumerator StoryCheckRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded) { Fail("story check: in a world"); yield break; }
			yield return new WaitForSeconds(8f);
			bool ok = true;
			Check(ref ok, WorldDirector.Plan != null && WorldDirector.Plan.Name == StoryPlan, "loaded again: the world's plan '" + (WorldDirector.Plan != null ? WorldDirector.Plan.Name : "none") + "'");
			var names = IslandWorldState.Islands.Select(e => e.HostName ?? e.Name).ToList();
			Check(ref ok, names.Count(n => n == StoryIslands[0]) == 1 && names.Count(n => n == StoryIslands[1]) == 1, "loaded again: home and the bay once each");
			Check(ref ok, WorldDirector.Done.Contains("home") && WorldDirector.Done.Contains("bay") && WorldDirector.Done.Contains("treasure"), "loaded again: the rules that fired are remembered (" + string.Join(",", WorldDirector.Done.OrderBy(x => x).ToArray()) + ")");
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands) Log("STORY " + (e.HostName ?? e.Name) + " label=" + e.Label);
			Log("STORY done " + string.Join(",", WorldDirector.Done.OrderBy(x => x).ToArray()));
			if (ok) Log("PASS: story check"); else Fail("story check");
		}

		#endregion

		[ConsoleCommand(name: "CIBoxClicks", docs: "Dev, main menu: Raft's New Game box clicked as a player does: the plan button goes round every plan (built-in and the player's own) and back to the first, each shows its name and description; the randomizer level button goes Off, Light, Normal, Wild and round; each part button switches its part off and on (greyed when Off). Leaves the plan <plan> (default CI Story) chosen and the randomizer Off: CIBoxClicks [plan]")]
		public static void BoxClicksCommand(string[] args) { DynamicIslands.instance.StartCoroutine(BoxClicksRoutine(args != null && args.Length > 0 ? string.Join(" ", args) : StoryPlan)); }

		static IEnumerator BoxClicksRoutine(string wantPlan)
		{
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("New Game box clicks: no New Game box (main menu?)"); yield break; }
			bool ok = true;
			box.gameObject.SetActive(true);
			box.Open();
			yield return new WaitForSecondsRealtime(1f);
			Transform planRow = box.transform.Find("CustomIslands_Plan"), randRow = box.transform.Find("CustomIslands_Randomizer");
			Button plan = planRow != null ? planRow.GetComponentsInChildren<Button>(true).FirstOrDefault() : null;
			Text detail = planRow != null ? planRow.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Detail") : null;
			if (plan == null) { Fail("New Game box clicks: no plan button"); yield break; }
			// The plan button: round every plan
			List<string> plans = WorldPlan.All();
			string first = UIKit.LabelOf(plan).text;
			var seen = new List<string>();
			var wrongDetail = new List<string>();
			for (int i = 0; i < plans.Count; i++)
			{
				plan.onClick.Invoke();
				yield return null;
				string label = UIKit.LabelOf(plan).text.Replace("►", "").Trim();
				seen.Add(label);
				WorldPlan p = WorldPlan.Load(label);
				if (p == null || detail == null || !detail.text.StartsWith(p.Description.Length > 0 ? p.Description : p.Rules.Count + " rule(s)")) wrongDetail.Add(label);
			}
			Check(ref ok, seen.Distinct().Count() == plans.Count && UIKit.LabelOf(plan).text == first, "the plan button goes round all " + plans.Count + " plans and back (" + string.Join(", ", seen.ToArray()) + ")");
			Check(ref ok, wrongDetail.Count == 0, "each plan shows its description" + (wrongDetail.Count > 0 ? " - not: " + string.Join(", ", wrongDetail.ToArray()) : ""));
			Check(ref ok, plans.Contains(wantPlan), "the player's plan '" + wantPlan + "' is among them");
			for (int i = 0; i < plans.Count && NewWorldOptions.Selected != wantPlan; i++) { plan.onClick.Invoke(); yield return null; }
			Check(ref ok, NewWorldOptions.Selected == wantPlan && UIKit.LabelOf(plan).text.StartsWith(wantPlan), "clicked to '" + wantPlan + "': chosen (" + NewWorldOptions.Selected + ")");
			// The randomizer: level round, then each part
			Button[] rb = randRow != null ? randRow.GetComponentsInChildren<Button>(true) : new Button[0];
			Button level = rb.FirstOrDefault();
			if (level == null) { Check(ref ok, false, "no randomizer buttons"); }
			else
			{
				var levels = new List<string>();
				for (int i = 0; i < RandomizerSettings.LevelNames.Length; i++) { level.onClick.Invoke(); yield return null; levels.Add(NewWorldOptions.Randomizer.LevelName); }
				Check(ref ok, levels.Distinct().Count() == RandomizerSettings.LevelNames.Length, "the level button goes round: " + string.Join(" > ", levels.ToArray()));
				for (int i = 0; i < 4 && NewWorldOptions.Randomizer.Level != RandomizerSettings.Wild; i++) { level.onClick.Invoke(); yield return null; }
				var parts = rb.Skip(1).ToArray();
				var badParts = new List<string>();
				for (int i = 0; i < parts.Length && i < RandomizerSettings.Features.Length; i++)
				{
					string f = RandomizerSettings.Features[i];
					parts[i].onClick.Invoke(); yield return null;
					bool off = NewWorldOptions.Randomizer.Disabled.Contains(f);
					parts[i].onClick.Invoke(); yield return null;
					bool on = !NewWorldOptions.Randomizer.Disabled.Contains(f);
					if (!off || !on) badParts.Add(f);
				}
				Check(ref ok, parts.Length == RandomizerSettings.Features.Length && badParts.Count == 0, parts.Length + " part buttons, each switches its part off and on" + (badParts.Count > 0 ? " - not: " + string.Join(", ", badParts.ToArray()) : ""));
				for (int i = 0; i < 4 && NewWorldOptions.Randomizer.Level != RandomizerSettings.Off; i++) { level.onClick.Invoke(); yield return null; }
				Check(ref ok, parts.All(b => !b.interactable), "Off: the part buttons greyed out");
			}
			Screenshot(new[] { "newgame_clicked" });
			yield return new WaitForSecondsRealtime(0.8f);
			box.gameObject.SetActive(false);
			if (ok) Log("PASS: New Game box clicks"); else Fail("New Game box clicks");
		}
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
