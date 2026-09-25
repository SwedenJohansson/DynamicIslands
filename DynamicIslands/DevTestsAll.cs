using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using RuntimeGizmos;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// The "everything" test (tools\alltests.ps1): every button of the editor and its windows, the in-world windows,
	/// the editor's state for the keyboard test, save/load round trips of every saved island, and snapshots of a
	/// world to compare after saving, loading, leaving, joining again and restarting Raft.
	/// </summary>
	public static partial class DevTests
	{
		#region Every button

		/// <summary>Buttons the walk leaves alone: leaving the editor is tested on its own at the end.</summary>
		static readonly HashSet<string> ButtonSkip = new HashSet<string> { "Main menu" };
		/// <summary>A list with more buttons than this (object tiles, items, sounds, plan cards) is sampled: its first three.</summary>
		const int ListSample = 3, ListThreshold = 8;

		class ButtonRun
		{
			public int Pressed, Windows;
			public readonly HashSet<Button> Done = new HashSet<Button>();
			public readonly HashSet<string> Keys = new HashSet<string>();
			public readonly List<string> Errors = new List<string>(), Unclosed = new List<string>(), Sampled = new List<string>();
			public readonly Dictionary<string, int> PerScreen = new Dictionary<string, int>();
			public string Current = "";
		}

		static ButtonRun buttonRun;

		[ConsoleCommand(name: "CIButtons", docs: "Dev, main menu or editor: presses every button of the editor (each tab, each kind of selected object) and of every window they open, and reports errors, windows that don't close, and buttons never reached")]
		public static void ButtonsCommand()
		{
			DynamicIslands.instance.StartCoroutine(ButtonsRoutine());
		}

		static IEnumerator ButtonsRoutine()
		{
			buttonRun = new ButtonRun();
			ButtonRun run = buttonRun;
			Application.LogCallback watch = (msg, trace, type) =>
			{
				if ((type == LogType.Exception || type == LogType.Error) && !msg.StartsWith("[CITEST]"))
					run.Errors.Add(run.Current + ": " + msg.Split('\n')[0] + (type == LogType.Exception ? " @ " + (trace ?? "").Split('\n').FirstOrDefault() : ""));
			};
			Application.logMessageReceived += watch;
			try
			{
				// The main menu's EDITOR button (the mod's own) opens the editor
				if (!DynamicIslands.InEditor())
				{
					GameObject menu = GameObject.Find("MainMenuCanvas");
					Transform ed = menu != null ? menu.transform.Find("MenuButtons/EDITOR") : null;
					Button eb = ed != null ? ed.GetComponent<Button>() : null;
					if (eb == null) { Fail("no EDITOR button in the main menu"); yield break; }
					run.Current = "Main menu > EDITOR";
					eb.onClick.Invoke();
					run.Pressed++;
					Log("Pressed the main menu's EDITOR button");
					// (its own load: WaitForEditor would start a second one)
					float t0 = Time.realtimeSinceStartup;
					while (!(DynamicIslands.InEditor() && PlaceableCatalog.IsBuilt && EditorUI.Canvas != null) && Time.realtimeSinceStartup - t0 < 120f) yield return new WaitForSecondsRealtime(0.5f);
					if (!DynamicIslands.InEditor()) { Fail("the EDITOR button did not open the editor in 120 s"); yield break; }
				}
				yield return WaitForEditor(false);
				if (!DynamicIslands.InEditor()) { Fail("the editor did not open"); yield break; }
				yield return new WaitForSecondsRealtime(1f);

				// A test island with one object of each kind that has its own inspector
				DynamicIslands.currentIslandName = "cibuttons";
				Transform placed = GameObject.Find("PlacedObjects").transform;
				Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, IslandFile.DefaultWaterLevel + 2f, 500f);
				var kinds = new List<KeyValuePair<string, string>>
				{
					new KeyValuePair<string, string>("plain object", "Log"),
					new KeyValuePair<string, string>("creature", "Creature_Boar"),
					new KeyValuePair<string, string>("note", "Note_Paper"),
					new KeyValuePair<string, string>("chest", "Loot_Chest"),
					new KeyValuePair<string, string>("trigger zone", ContentCatalog.TriggerZone),
					new KeyValuePair<string, string>("atmosphere zone", ContentCatalog.AtmosphereZoneName),
					new KeyValuePair<string, string>("sound zone", ContentCatalog.SoundZoneName),
					new KeyValuePair<string, string>("invisible wall", ContentCatalog.HelperWall),
					new KeyValuePair<string, string>("raft block", "Block_Foundation"),
				};
				var objs = new List<KeyValuePair<string, EditorGameObject>>();
				int i = 0;
				foreach (var k in kinds)
				{
					EditorGameObject eo = PlaceForTest(k.Value, c0 + new Vector3(i++ * 6f, 0f, 0f), placed);
					if (eo == null) Log("  (no " + k.Value + " in the catalog)");
					else objs.Add(new KeyValuePair<string, EditorGameObject>(k.Key, eo));
				}
				yield return null;

				// The screens: each tab, and the Objects tab with each kind of selection
				yield return ButtonScreen(run, "Terrain tab", () => { DynamicIslands.EditorGizmoHandler.ClearTargets(false); EditorUI.SetTab(TAB.TerrainEdit); });
				yield return ButtonScreen(run, "Island tab", () => { DynamicIslands.EditorGizmoHandler.ClearTargets(false); EditorUI.SetTab(TAB.Island); });
				yield return ButtonScreen(run, "Objects tab, nothing selected", () => { EditorUI.SetTab(TAB.ObjectPlace); DynamicIslands.EditorGizmoHandler.ClearTargets(false); });
				foreach (var o in objs)
				{
					EditorGameObject target = o.Value;
					if (target == null) continue;
					yield return ButtonScreen(run, "Objects tab, a " + o.Key + " selected", () => { EditorUI.SetTab(TAB.ObjectPlace); TransformGizmoSelect(target.transform); });
				}
				if (objs.Count >= 2 && objs[0].Value != null && objs[1].Value != null)
					yield return ButtonScreen(run, "Objects tab, two objects selected", () =>
					{
						EditorUI.SetTab(TAB.ObjectPlace);
						DynamicIslands.EditorGizmoHandler.ClearTargets(false);
						DynamicIslands.EditorGizmoHandler.AddTarget(objs[0].Value.transform, false);
						DynamicIslands.EditorGizmoHandler.AddTarget(objs[1].Value.transform, false);
					});
			}
			finally { Application.logMessageReceived -= watch; }

			// Coverage: every button the mod made that still exists (and isn't a sampled list entry or skipped)
			UIKit.AllButtons.RemoveAll(b => b == null);
			List<Button> missed = UIKit.AllButtons.Where(b => !run.Done.Contains(b) && !ButtonSkip.Contains(LabelOfButton(b)) && !InSampledList(b) && !IsWorldOnly(b)).ToList();
			foreach (var s in run.PerScreen) Log("  " + s.Key + ": " + s.Value + " button(s)");
			Log("Pressed " + run.Pressed + " buttons (" + run.Keys.Count + " different), " + run.Windows + " window(s) opened; lists sampled: " + run.Sampled.Distinct().Count());
			foreach (string e in run.Errors.Distinct().Take(30)) Log("  ERROR " + e);
			foreach (string u in run.Unclosed.Distinct()) Log("  NOT CLOSED " + u);
			foreach (var g in missed.GroupBy(b => OwnerOf(b) + " > " + LabelOfButton(b)).Take(40)) Log("  NEVER PRESSED " + g.Key + (g.Count() > 1 ? " (x" + g.Count() + ")" : ""));
			bool ok = run.Errors.Count == 0 && run.Unclosed.Count == 0;
			Check(ref ok, run.Keys.Count >= 150, "at least 150 different buttons pressed (" + run.Keys.Count + ")");
			Log("Buttons never pressed: " + missed.Count + " (of " + UIKit.AllButtons.Count + " the mod made)");
			if (ok) Log("PASS: every button"); else Fail("every button (" + run.Errors.Count + " errors, " + run.Unclosed.Count + " windows not closed)");
		}

		static IEnumerator ButtonScreen(ButtonRun run, string screen, Action setup)
		{
			CloseAllWindows();
			try { setup(); } catch (Exception e) { run.Errors.Add(screen + " (setting up): " + e.Message); }
			yield return null; yield return null;
			int before = run.Pressed;
			// (a snapshot of this screen's buttons: pressing one can rebuild the panel, the loop re-checks each)
			List<Button> list = SampleList(run, MainButtons());
			foreach (Button b in list)
			{
				if (b == null || !b.gameObject.activeInHierarchy || !b.interactable) continue;
				if (!DynamicIslands.InEditor()) { run.Errors.Add(screen + ": left the editor"); yield break; }
				yield return Press(run, b, screen, 0);
				CloseAllWindows();
				// (the screen may have changed: selection lost, tab switched - set it up again)
				try { setup(); } catch { }
				yield return null;
			}
			run.PerScreen[screen] = run.Pressed - before;
		}

		static IEnumerator Press(ButtonRun run, Button b, string context, int depth)
		{
			string label = LabelOfButton(b);
			if (ButtonSkip.Contains(label)) yield break;
			string key = OwnerOf(b) + " > " + PathOf(b.transform, b.transform.root) + " '" + label + "'";
			List<MonoBehaviour> before = OpenWindows();
			run.Current = context + " > " + label;
			try { b.onClick.Invoke(); }
			catch (Exception e) { run.Errors.Add(run.Current + ": " + (e.InnerException ?? e).Message); }
			run.Pressed++;
			run.Done.Add(b);
			run.Keys.Add(key);
			yield return null; yield return null;
			if (depth >= 3) yield break;
			foreach (MonoBehaviour w in OpenWindows().Where(x => !before.Contains(x)).ToList())
			{
				run.Windows++;
				yield return TestWindow(run, w, b, run.Current, depth + 1);
			}
			// Whatever this press opened is closed again
			foreach (MonoBehaviour w in OpenWindows().Where(x => !before.Contains(x)).ToList())
			{
				if (!CloseWindow(w)) run.Unclosed.Add(w.GetType().Name + " (opened by " + context + " > " + label + ")");
			}
		}

		/// <summary>Presses every button of a window; when a press closes the window, it's opened again with the same button.</summary>
		static IEnumerator TestWindow(ButtonRun run, MonoBehaviour w, Button opener, string context, int depth)
		{
			Type type = w.GetType();
			var done = new HashSet<string>();
			for (int guard = 0; guard < 120; guard++)
			{
				if (w == null || !w.gameObject.activeInHierarchy)
				{
					if (opener == null || !opener.gameObject.activeInHierarchy) break;
					try { opener.onClick.Invoke(); } catch { break; }
					yield return null; yield return null;
					w = OpenWindows().FirstOrDefault(x => x.GetType() == type);
					if (w == null) break;
				}
				MonoBehaviour win = w;
				Button next = SampleList(run, win.GetComponentsInChildren<Button>(false).Where(x => x.interactable && WindowOf(x) == win).ToList())
					.FirstOrDefault(x => !done.Contains(PathOf(x.transform, win.transform) + "|" + LabelOfButton(x)) && !ButtonSkip.Contains(LabelOfButton(x)));
				if (next == null) break;
				done.Add(PathOf(next.transform, win.transform) + "|" + LabelOfButton(next));
				yield return Press(run, next, context + " [" + type.Name + "]", depth);
			}
			if (w != null && w.gameObject.activeInHierarchy && !CloseWindow(w)) run.Unclosed.Add(type.Name + " (" + context + ")");
		}

		/// <summary>The buttons of the editor screen itself (not in a window).</summary>
		static List<Button> MainButtons()
		{
			if (EditorUI.Canvas == null) return new List<Button>();
			return EditorUI.Canvas.GetComponentsInChildren<Button>(false).Where(b => b.interactable && WindowOf(b) == null).ToList();
		}

		/// <summary>Long lists (tiles, items, sounds, cards) keep their first few buttons.</summary>
		static List<Button> SampleList(ButtonRun run, List<Button> list)
		{
			var result = new List<Button>();
			foreach (var g in list.GroupBy(b => b.transform.parent))
			{
				if (g.Count() > ListThreshold) { result.AddRange(g.Take(ListSample)); run.Sampled.Add(PathOf(g.Key, g.Key.root)); }
				else result.AddRange(g);
			}
			return result;
		}

		static bool InSampledList(Button b) { return b.transform.parent != null && b.transform.parent.GetComponentsInChildren<Button>(true).Count(x => x.transform.parent == b.transform.parent) > ListThreshold; }

		/// <summary>The journal and the note reader belong to the world (CIWorldButtons).</summary>
		static bool IsWorldOnly(Button b) { MonoBehaviour w = WindowOf(b); return w is JournalWindow || w is NoteReader; }

		static List<MonoBehaviour> OpenWindows()
		{
			return UnityEngine.Object.FindObjectsOfType<MonoBehaviour>().Where(m => m != null && m.gameObject.activeInHierarchy && IsWindowType(m.GetType())).ToList();
		}

		static bool IsWindowType(Type t) { return t.Name.EndsWith("Window") || t == typeof(NoteReader); }

		static MonoBehaviour WindowOf(Button b)
		{
			for (Transform t = b.transform; t != null; t = t.parent)
				foreach (MonoBehaviour m in t.GetComponents<MonoBehaviour>())
					if (m != null && IsWindowType(m.GetType())) return m;
			return null;
		}

		static string OwnerOf(Button b)
		{
			MonoBehaviour w = WindowOf(b);
			if (w != null) return w.GetType().Name;
			for (Transform t = b.transform; t != null; t = t.parent)
				if (t.name == "ToolPanel" || t.name == "ObjectBrowser" || t.name == "TopBar" || t.name == "StatusBar") return t.name;
			return b.transform.root.name;
		}

		static string LabelOfButton(Button b)
		{
			if (b == null) return "";
			Text t = UIKit.LabelOf(b) ?? b.GetComponentInChildren<Text>(true);
			string s = t != null ? t.text.Replace("\n", " ").Trim() : "";
			return s.Length > 0 ? (s.Length > 30 ? s.Substring(0, 30) : s) : b.name;
		}

		static bool CloseWindow(MonoBehaviour w)
		{
			if (w == null || !w.gameObject.activeInHierarchy) return true;
			try
			{
				var close = w.GetType().GetMethod("Close", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);
				if (close != null) close.Invoke(null, null);
			}
			catch (Exception e) { Debug.LogWarning("[CITEST] closing " + w.GetType().Name + ": " + (e.InnerException ?? e).Message); }
			return w == null || !w.gameObject.activeInHierarchy;
		}

		static void CloseAllWindows() { foreach (MonoBehaviour w in OpenWindows()) CloseWindow(w); }

		/// <summary>The in-world windows: the journal (J) and the note reader, every button.</summary>
		[ConsoleCommand(name: "CIWorldButtons", docs: "Dev, in game: presses every button of the journal and the note reader")]
		public static void WorldButtonsCommand()
		{
			DynamicIslands.instance.StartCoroutine(WorldButtonsRoutine());
		}

		static IEnumerator WorldButtonsRoutine()
		{
			var run = new ButtonRun();
			Application.LogCallback watch = (msg, trace, type) => { if ((type == LogType.Exception || type == LogType.Error) && !msg.StartsWith("[CITEST]")) run.Errors.Add(run.Current + ": " + msg.Split('\n')[0]); };
			Application.logMessageReceived += watch;
			bool ok = true;
			try
			{
				StoryBook.Give("ci-button-test", 1);
				StoryBook.AddPage("ci:buttons", "A test page", "Written by the button test.", "");
				JournalWindow.Open();
				yield return null; yield return null;
				MonoBehaviour j = OpenWindows().FirstOrDefault(w => w is JournalWindow);
				Check(ref ok, j != null, "the journal opens");
				if (j != null) { run.Windows++; yield return TestWindow(run, j, null, "Journal", 1); }
				Check(ref ok, !JournalWindow.IsOpen, "the journal closes");
				var go = new GameObject("CI_TestNote");
				CustomNote n = go.AddComponent<CustomNote>();
				n.Title = "Button test"; n.Text = "A note for the button test.";
				NoteReader.Open(n);
				yield return null; yield return null;
				MonoBehaviour r = OpenWindows().FirstOrDefault(w => w is NoteReader);
				Check(ref ok, r != null, "the note reader opens");
				if (r != null) { run.Windows++; yield return TestWindow(run, r, null, "Note reader", 1); }
				Check(ref ok, !NoteReader.IsOpen, "the note reader closes");
				UnityEngine.Object.Destroy(go);
			}
			finally { Application.logMessageReceived -= watch; }
			foreach (string e in run.Errors.Distinct()) Log("  ERROR " + e);
			ok &= run.Errors.Count == 0 && run.Unclosed.Count == 0;
			Log("Pressed " + run.Pressed + " buttons in the journal and the note reader");
			if (ok) Log("PASS: in-world windows"); else Fail("in-world windows");
		}

		#endregion

		#region Editor state (keyboard test)

		/// <summary>One line with what the keyboard shortcuts change, for tools\alltests.ps1's keyboard phase.</summary>
		[ConsoleCommand(name: "CIEditorState", docs: "Dev, editor: logs the tab, brush, gizmo, placement options, selection, undo steps, open windows, stamp turn and the placer's turn and size")]
		public static void EditorStateCommand()
		{
			if (!DynamicIslands.InEditor()) { Fail("not in the editor"); return; }
			var g = DynamicIslands.EditorGizmoHandler;
			ObjectPlacer placer = UnityEngine.Object.FindObjectOfType<ObjectPlacer>();
			Transform placed = GameObject.Find("PlacedObjects") != null ? GameObject.Find("PlacedObjects").transform : null;
			Log("Editor state: tab " + EditorUI.CurrentTab + ", brush " + terraineditor.modificationAction + ", gizmo " + (g != null ? g.transformType.ToString() : "?") +
				", random " + PlacementOptions.RandomTurnAndSize + ", slope " + PlacementOptions.AlignToSlope + ", grid " + PlacementOptions.SnapToGrid +
				", selected " + (g != null ? g.SelectedRoots.Count : 0) + ", objects " + (placed != null ? placed.childCount : 0) +
				", undo " + CommandUndoRedo.UndoRedoManager.UndoCount + ", windows [" + string.Join(",", OpenWindows().Select(w => w.GetType().Name).ToArray()) + "]" +
				", stamp turn " + TerrainStamps.Rotation.ToString("F0") + ", placer " + (placer != null ? placer.name + " yaw " + placer.Yaw.ToString("F0") + " scale " + placer.ScaleFactor.ToString("F2") : "none") +
				", island '" + DynamicIslands.currentIslandName + "'");
		}

		[ConsoleCommand(name: "CIStartPlacing", docs: "Dev, editor (Objects tab): starts placing an object as clicking its tile does: CIStartPlacing <object name>")]
		public static void StartPlacingCommand(string[] args)
		{
			string name = args != null && args.Length > 0 ? args[0] : "Log";
			EditorUI.SetTab(TAB.ObjectPlace);
			ObjectBrowser.StartPlacing(name);
			Log("Placing '" + name + "'");
		}

		[ConsoleCommand(name: "CIStamp", docs: "Dev, editor: picks a terrain stamp as its button does: CIStamp <index>")]
		public static void StampCommand(string[] args)
		{
			int i;
			if (args == null || args.Length == 0 || !int.TryParse(args[0], out i)) i = 0;
			EditorUI.SetTab(TAB.TerrainEdit);
			EditorUI.SetStamp(i);
			Log("Stamp " + i + " picked (" + (TerrainStamps.Current != null ? TerrainStamps.Current.Name : "none") + ")");
		}

		[ConsoleCommand(name: "CISelect", docs: "Dev, editor (Objects tab): selects the first placed object (or the one named) as a click does")]
		public static void SelectCommand(string[] args)
		{
			Transform placed = GameObject.Find("PlacedObjects") != null ? GameObject.Find("PlacedObjects").transform : null;
			if (placed == null || placed.childCount == 0) { Fail("nothing placed to select"); return; }
			string name = args != null && args.Length > 0 ? args[0] : null;
			Transform t = placed.Cast<Transform>().FirstOrDefault(c => name == null || c.name.StartsWith(name));
			if (t == null) { Fail("no placed '" + name + "'"); return; }
			EditorUI.SetTab(TAB.ObjectPlace);
			TransformGizmoSelect(t);
			Log("Selected " + t.name);
		}

		[ConsoleCommand(name: "CIJournalState", docs: "Dev, in game: whether the journal and the note reader are open (the keyboard test presses J and Esc)")]
		public static void JournalStateCommand()
		{
			Log("Journal " + (JournalWindow.IsOpen ? "open" : "closed") + ", note reader " + (NoteReader.IsOpen ? "open" : "closed"));
		}

		#endregion

		#region Round trip of every saved island

		/// <summary>Loads each saved island in the editor, saves it under a test name and compares the two files (terrain, paint, objects with settings, island settings).</summary>
		[ConsoleCommand(name: "CIRoundTrip", docs: "Dev, editor: loads every saved island (or the one named), saves it again under a test name and compares: nothing may change")]
		public static void RoundTripCommand(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(RoundTripRoutine(args != null && args.Length > 0 ? string.Join(" ", args) : null));
		}

		const string RoundTripName = "ci_roundtrip";

		static IEnumerator RoundTripRoutine(string only)
		{
			yield return WaitForEditor(false);
			List<string> names = IslandSpawner.ListSavedIslands().Where(n => n != RoundTripName && (only == null || n == only)).ToList();
			int passed = 0;
			var failed = new List<string>();
			foreach (string name in names)
			{
				IslandFile a;
				try { a = IslandFile.Load(IslandSpawner.PathFor(name)); }
				catch (Exception e) { failed.Add(name + ": can't read (" + e.Message + ")"); continue; }
				if (a == null) { failed.Add(name + ": can't read"); continue; }
				bool loaded = false;
				try { loaded = DynamicIslands.LoadIsland(name); } catch (Exception e) { failed.Add(name + ": loading failed: " + e.Message); continue; }
				if (!loaded) { failed.Add(name + ": the editor didn't load it"); continue; }
				yield return null; yield return null;
				DynamicIslands.SaveIsland(RoundTripName);
				yield return null;
				IslandFile b = IslandFile.Load(IslandSpawner.PathFor(RoundTripName));
				string diff = CompareIslands(a, b);
				if (diff == null) passed++; else failed.Add(name + ": " + diff);
			}
			if (File.Exists(IslandSpawner.PathFor(RoundTripName))) File.Delete(IslandSpawner.PathFor(RoundTripName));
			foreach (string f in failed) Log("  ROUND TRIP " + f);
			Log("Round trip: " + passed + " of " + names.Count + " islands unchanged");
			if (failed.Count == 0 && names.Count > 0) Log("PASS: every saved island survives loading and saving"); else Fail("island round trip (" + failed.Count + " changed)");
		}

		/// <summary>What differs between two island files (null: the same, within float precision).</summary>
		static string CompareIslands(IslandFile a, IslandFile b)
		{
			if (b == null) return "the saved copy can't be read";
			if (a.HeightmapResolution != b.HeightmapResolution || (a.TerrainSize - b.TerrainSize).magnitude > 0.01f) return "terrain size " + a.TerrainSize + "/" + a.HeightmapResolution + " -> " + b.TerrainSize + "/" + b.HeightmapResolution;
			float maxH = 0f;
			for (int z = 0; z < a.HeightmapResolution; z++)
				for (int x = 0; x < a.HeightmapResolution; x++)
					maxH = Mathf.Max(maxH, Mathf.Abs(a.Heights[z, x] - b.Heights[z, x]));
			if (maxH * a.TerrainSize.y > 0.05f) return "heights differ by up to " + (maxH * a.TerrainSize.y).ToString("F2") + " m";
			if (a.HasPaint)
			{
				if (!b.HasPaint || a.Alphamaps.Length != b.Alphamaps.Length) return "ground paint lost or resized";
				int maxP = 0;
				for (int i = 0; i < a.Alphamaps.Length; i++) maxP = Math.Max(maxP, Math.Abs(a.Alphamaps[i] - b.Alphamaps[i]));
				if (maxP > 2) return "ground paint differs (up to " + maxP + "/255)";
			}
			if (Mathf.Abs(a.Elevation - b.Elevation) > 0.01f) return "height in the world " + a.Elevation + " -> " + b.Elevation;
			if ((a.Style ?? "") != (b.Style ?? "")) return "style '" + a.Style + "' -> '" + b.Style + "'";
			string pd = CompareProps(a.Props, b.Props);
			if (pd != null) return "island settings: " + pd;
			if (a.Objects.Count != b.Objects.Count) return "objects " + a.Objects.Count + " -> " + b.Objects.Count;
			// (the editor may save objects in another order: match each to the nearest of the same name)
			var left = new List<IslandObject>(b.Objects);
			foreach (IslandObject o in a.Objects)
			{
				IslandObject m = left.Where(x => x.Name == o.Name).OrderBy(x => (x.Position - o.Position).sqrMagnitude).FirstOrDefault();
				if (m == null) return "object " + o.Name + " missing";
				if ((m.Position - o.Position).magnitude > 0.02f) return "object " + o.Name + " moved by " + (m.Position - o.Position).magnitude.ToString("F3") + " m";
				if (Quaternion.Angle(Quaternion.Euler(m.EulerRotation), Quaternion.Euler(o.EulerRotation)) > 0.5f) return "object " + o.Name + " turned";
				if ((m.Scale - o.Scale).magnitude > 0.01f) return "object " + o.Name + " resized " + o.Scale + " -> " + m.Scale;
				string od = CompareProps(o.Props, m.Props);
				if (od != null) return "object " + o.Name + " settings: " + od;
				left.Remove(m);
			}
			return null;
		}

		static string CompareProps(Dictionary<string, string> a, Dictionary<string, string> b)
		{
			a = a ?? new Dictionary<string, string>(); b = b ?? new Dictionary<string, string>();
			foreach (var kv in a) { string v; if (!b.TryGetValue(kv.Key, out v)) return "'" + kv.Key + "' lost"; if (v != kv.Value) return "'" + kv.Key + "' " + Short(kv.Value) + " -> " + Short(v); }
			foreach (var kv in b) if (!a.ContainsKey(kv.Key)) return "'" + kv.Key + "' added";
			return null;
		}

		#endregion

		#region World snapshots (saving, loading, leaving, joining, restarting)

		static string SnapshotPath(string name) { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "snapshots"), name + (Raft_Network.IsHost ? "_host" : "_client") + ".txt"); }

		/// <summary>
		/// The world as this player has it, as lines "key = value": positions relative to the named reference island
		/// (the raft drifts and Raft shifts the world), the raft's too, the player's position and items, every island
		/// (label, height, used objects), quests, the story book, and the world plan's state (host).
		/// </summary>
		static List<KeyValuePair<string, string>> Snapshot(string reference)
		{
			var lines = new List<KeyValuePair<string, string>>();
			Action<string, string> add = (k, v) => lines.Add(new KeyValuePair<string, string>(k, v));
			IslandWorldState.Entry refIsland = IslandWorldState.Islands.FirstOrDefault(e => e.HostName == reference);
			Vector3 origin = refIsland != null ? refIsland.Position : Vector3.zero;
			Func<Vector3, string> rel = p => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1},{1:F1},{2:F1}", p.x - origin.x, p.y - origin.y, p.z - origin.z);
			add("reference", refIsland != null ? reference : "(none)");
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.OrderBy(x => x.HostName).ThenBy(x => x.Position.x))
			{
				if (e.Root != null) IslandObjectState.Capture(e);
				string id = "island " + e.HostName + "@" + rel(e.Position);
				add(id + " label", e.Label);
				// (a client leaves out the host's own bookkeeping - creature spots 1xxxx, the director's 5xxxx: it gets
				// them only when joining, so they would show up as changes after joining again)
				add(id + " state", string.Join(" ", e.State.Where(kv => Raft_Network.IsHost || !((kv.Key >= 0x10000 && kv.Key < 0x20000) || (kv.Key >= 0x50000 && kv.Key < 0x60000))).OrderBy(kv => kv.Key).Select(kv => kv.Key.ToString("X") + "=" + (kv.Value.Active ? 1 : 0) + "/" + kv.Value.Yield).ToArray()));
				IslandQuest q = QuestTracker.QuestOf(e);
				if (q.Steps.Count > 0) add(id + " quest", QuestTracker.StepOf(e) + "/" + q.Steps.Count);
			}
			add("story items", string.Join(", ", StoryBook.Items.Select(h => h.Def.Id + " x" + h.Count).ToArray()));
			add("story pages", string.Join(", ", StoryBook.Pages.Select(p => p.Key).OrderBy(k => k).ToArray()));
			if (Raft_Network.IsHost) add("world plan", WorldDirector.PlanName + "; done " + string.Join(",", WorldDirector.Done.OrderBy(d => d).ToArray()));
			Network_Player player = RAPI.GetLocalPlayer();
			if (player != null)
			{
				add("player position", rel(player.transform.position));
				PlayerInventory inv = player.Inventory;
				if (inv != null) add("player items", string.Join(", ", new[] { "Plank", "Stone", "Rope", "Nail", "Scrap", "Plastic", "Palm Leaf" }.Select(i => i + " x" + inv.GetItemCount(i)).ToArray()));
			}
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (raft.HasValue) add("raft position", rel(raft.Value));
			return lines;
		}

		[ConsoleCommand(name: "CISnapshot", docs: "Dev, in game (either player): writes a snapshot of the world as this player has it (positions relative to an island): CISnapshot <name> <reference island>")]
		public static void SnapshotCommand(string[] args)
		{
			if (args == null || args.Length < 2) { Fail("usage: CISnapshot <name> <reference island>"); return; }
			List<KeyValuePair<string, string>> lines = Snapshot(args[1]);
			string path = SnapshotPath(args[0]);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllLines(path, lines.Select(kv => kv.Key + " = " + kv.Value).ToArray());
			Log("Snapshot '" + args[0] + "': " + lines.Count + " lines -> " + path);
			foreach (var kv in lines) Log("  " + kv.Key + " = " + kv.Value);
		}

		/// <summary>Compares the world now with a snapshot. Positions: islands 2 m, the player 3 m; the raft only reported (it drifts).</summary>
		[ConsoleCommand(name: "CISnapCheck", docs: "Dev, in game (either player): compares the world now with a snapshot: CISnapCheck <name> <reference island> [what to leave out, e.g. raft]")]
		public static void SnapCheckCommand(string[] args)
		{
			if (args == null || args.Length < 2) { Fail("usage: CISnapCheck <name> <reference island>"); return; }
			string path = SnapshotPath(args[0]);
			if (!File.Exists(path)) { Fail("no snapshot " + path); return; }
			var then = File.ReadAllLines(path).Select(l => { int i = l.IndexOf(" = "); return new KeyValuePair<string, string>(l.Substring(0, i), l.Substring(i + 3)); }).ToList();
			List<KeyValuePair<string, string>> now = Snapshot(args[1]);
			var ignore = new HashSet<string>(args.Skip(2));
			int diffs = 0;
			Func<string, string> baseKey = k => k.StartsWith("island ") ? System.Text.RegularExpressions.Regex.Replace(k, "@[^ ]+", "") : k;
			var nowLeft = new List<KeyValuePair<string, string>>(now);
			foreach (var t in then)
			{
				if (ignore.Any(x => t.Key.Contains(x))) continue;
				// Islands: the same island (name) nearest to where it was
				KeyValuePair<string, string> m = nowLeft.Where(n => baseKey(n.Key) == baseKey(t.Key)).OrderBy(n => IslandOffsetDiff(t.Key, n.Key)).FirstOrDefault();
				if (m.Key == null) { Log("  DIFF " + t.Key + ": gone (was " + Short(t.Value) + ")"); diffs++; continue; }
				nowLeft.Remove(m);
				if (t.Key.StartsWith("island ") && IslandOffsetDiff(t.Key, m.Key) > 2f) { Log("  DIFF " + baseKey(t.Key) + " moved: " + t.Key + " -> " + m.Key); diffs++; }
				if (t.Key == "player position" || t.Key == "raft position")
				{
					float d = (ParseVec(t.Value) - ParseVec(m.Value)).magnitude;
					bool bad = t.Key == "player position" && d > 3f;
					Log("  " + (bad ? "DIFF " : "") + t.Key + ": " + t.Value + " -> " + m.Value + " (" + d.ToString("F1") + " m)");
					if (bad) diffs++;
					continue;
				}
				if (t.Value != m.Value) { Log("  DIFF " + baseKey(t.Key) + ": " + t.Value + " -> " + m.Value); diffs++; }
			}
			foreach (var n in nowLeft) if (!ignore.Any(x => n.Key.Contains(x))) { Log("  DIFF new: " + n.Key + " = " + Short(n.Value)); diffs++; }
			if (diffs == 0) Log("PASS: the world matches snapshot '" + args[0] + "' (" + then.Count + " lines)");
			else Fail("the world differs from snapshot '" + args[0] + "' in " + diffs + " place(s)");
		}

		static Vector3 ParseVec(string s)
		{
			string[] p = (s ?? "").Split(',');
			float x = 0, y = 0, z = 0;
			if (p.Length == 3)
			{
				float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x);
				float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y);
				float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
			}
			return new Vector3(x, y, z);
		}

		static float IslandOffsetDiff(string a, string b)
		{
			var ma = System.Text.RegularExpressions.Regex.Match(a, "@([^ ]+)");
			var mb = System.Text.RegularExpressions.Regex.Match(b, "@([^ ]+)");
			if (!ma.Success || !mb.Success) return 0f;
			return (ParseVec(ma.Groups[1].Value) - ParseVec(mb.Groups[1].Value)).magnitude;
		}

		#endregion
	}
}
