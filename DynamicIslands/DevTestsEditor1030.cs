using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandUndoRedo;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using RuntimeGizmos;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Editor review 10-30: saved numbers back on the objects, "|" in titles, ids and names, Save as over another
	/// island, the windows that hold the shortcuts back.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIEdPipeTargets", docs: "Dev: review 10-30 - a note's or chest's title, a zone's id or an islander's name with | in it: the plan checker's facts and the game's quest events compare it as the step keeps it (a/b), and a rename from such a title moves its steps")]
		public static void EdPipeTargetsTest()
		{
			bool ok = true;
			try
			{
				Check(ref ok, IslandQuest.Clean("Log | Day 3") == "Log / Day 3" && IslandQuest.AsTarget("Log | Day 3") == "Log / Day 3" && IslandQuest.AsTarget(" plain ") == " plain " && IslandQuest.AsTarget(null) == null,
					"Clean and AsTarget: | becomes /, anything else stays as it is");

				var f = new IslandFile { Name = "citest-pipes", TerrainSize = new Vector3(64, 40, 64), HeightmapResolution = 33, Heights = new float[33, 33], WaterLevel = 0 };
				Func<string, Dictionary<string, string>, IslandObject> obj = (n, p) => new IslandObject { Name = n, Position = Vector3.zero, EulerRotation = Vector3.zero, Scale = Vector3.one, Props = p };
				f.Objects.Add(obj("Loot_Chest", new Dictionary<string, string> { { ObjectProps.NoteTitle, "Chest | Old" } }));
				f.Objects.Add(obj(ContentCatalog.TriggerZone, new Dictionary<string, string> { { ObjectProps.ZoneId, "Cove|North" } }));
				f.Objects.Add(obj(Islanders.ObjectName, new Dictionary<string, string> { { BehaviourProps.Name, "A|B" } }));
				f.Objects.Add(obj("Loot_Chest", new Dictionary<string, string> { { ObjectProps.NoteTitle, "Log | Day 3" }, { ObjectProps.NoteText, "a page" } }));
				f.Objects.Add(obj("Loot_Chest", new Dictionary<string, string> { { ObjectProps.NoteTitle, "Plain" } }));
				PlanChecker.Facts x = PlanChecker.FromFile(f, f.Name, false);
				Check(ref ok, x.Notes.Contains("Log / Day 3") && !x.Notes.Contains("Log | Day 3"), "a note titled \"Log | Day 3\" is \"Log / Day 3\" in the facts (" + string.Join(", ", x.Notes.ToArray()) + ")");
				Check(ref ok, x.Chests.Contains("Chest / Old") && x.Chests.Contains("Plain"), "chests: the | title cleaned, a plain one as it is (" + string.Join(", ", x.Chests.ToArray()) + ")");
				Check(ref ok, x.Zones.Contains("Cove/North") && !x.Zones.Contains("Cove|North"), "a zone id \"Cove|North\" is \"Cove/North\" (" + string.Join(", ", x.Zones.ToArray()) + ")");
				Check(ref ok, x.Player.Islanders.Contains("A/B"), "an islander named \"A|B\" is \"A/B\" (" + string.Join(", ", x.Player.Islanders.ToArray()) + ")");

				MethodInfo matches = typeof(QuestTracker).GetMethod("Matches", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IslandQuest.Step), typeof(string), typeof(string) }, null);
				if (matches == null) { Fail("pipe targets: QuestTracker.Matches(Step, string, string) not found"); return; }
				Func<string, string, string, string, bool> m = (st, tt, type, target) => (bool)matches.Invoke(null, new object[] { new IslandQuest.Step { Type = st, Target = tt }, type, target });
				Check(ref ok, m("read", "Log / Day 3", "read", "Log | Day 3"), "the game: reading \"Log | Day 3\" finishes the step \"Log / Day 3\"");
				Check(ref ok, m("reach", "Cove/North", "reach", "Cove|North") && m("talk", "A/B", "talk", "a|b"), "reaching \"Cove|North\" and talking to \"a|b\" finish theirs");
				Check(ref ok, m("open", "Plain", "open", "plain") && !m("open", "Plain", "open", "Other") && !m("open", "Plain", "read", "Plain") && m("open", "", "open", "anything"),
					"plain titles as before: any case, not another title, not another kind, an empty target takes any");

				var props = new Dictionary<string, string> { { IslandQuest.KeySteps, "read|Log / Day 3|1|\nopen|Other|1|" } };
				Check(ref ok, EditorRenames.QuestTargets(props, new[] { "read" }, "Log | Day 3", "Log | Day 4") == 1 && props[IslandQuest.KeySteps] == "read|Log / Day 4|1|\nopen|Other|1|",
					"a rename from \"Log | Day 3\" moves its step to \"Log / Day 4\" (" + props[IslandQuest.KeySteps].Replace("\n", " \\n ") + ")");
			}
			catch (Exception e) { Fail("pipe targets: " + e); return; }
			if (ok) Log("PASS: pipe targets (review 10-30)"); else Fail("pipe targets (review 10-30)");
		}

		[ConsoleCommand(name: "CIEdUidsSaved", docs: "Dev, editor: review 10-30 - after a save the objects carry the numbers the file gave them (a second save keeps them), a duplicate and a paste start without one and get their own, and Save as over another island's file takes that file's numbers only for the objects it really had")]
		public static void EdUidsSavedCommand() { StartTest(EdUidsSavedRoutine()); }

		static IEnumerator EdUidsSavedRoutine()
		{
			bool ok = true;
			const string isl = "citest-ed1030", other = "citest-ed1030-other";
			var leftovers = new List<string> { IslandSpawner.PathFor(isl), IslandSpawner.PathFor(other) };
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("uids saved: in the editor"); yield break; }
			try
			{
				var f = new IslandFile { Name = isl, TerrainSize = new Vector3(64, 40, 64), HeightmapResolution = 33, Heights = new float[33, 33], WaterLevel = 0 };
				Func<float, IslandObject> chest = x => new IslandObject { Name = "Loot_Chest", Position = new Vector3(x, 3, 0), EulerRotation = Vector3.zero, Scale = Vector3.one };
				f.Objects.AddRange(new[] { chest(-3), chest(3) });
				f.Save(IslandSpawner.PathFor(isl));
			}
			catch (Exception e) { Fail("uids saved: setting up - " + e.Message); yield break; }

			Check(ref ok, DynamicIslands.LoadIsland(isl), "the island opens");
			yield return new WaitForSecondsRealtime(1f);
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Func<List<EditorGameObject>> egos = () => placed.GetComponentsInChildren<EditorGameObject>().ToList();
			Func<string> uids = () => string.Join(",", egos().Select(e => e.Uid.ToString()).ToArray());
			TransformGizmo gizmo = DynamicIslands.EditorGizmoHandler;

			// 1. A new object: its number from the save is on it, and a second save keeps it
			EditorGameObject added = PlaceForTest("Loot_Chest", egos()[0].transform.position + new Vector3(0f, 0f, 6f), placed);
			if (added == null) { Fail("uids saved: could not place a chest"); yield break; }
			UndoRedoManager.Execute(new ObjectVisibilityCommand(new[] { added.gameObject }, true));
			yield return null;
			Check(ref ok, added.Uid == 0, "a placed chest starts without a number");
			Check(ref ok, DynamicIslands.SaveIsland(isl), "saved");
			IslandFile back = IslandFile.Load(IslandSpawner.PathFor(isl));
			int first = added.Uid;
			Check(ref ok, first > 0 && egos().Select(e => e.Uid).OrderBy(u => u).SequenceEqual(back.Objects.Select(o => o.Uid).OrderBy(u => u)) && back.Objects.Count(o => o.Uid == first) == 1,
				"the objects carry the file's numbers (editor " + uids() + ", file " + string.Join(",", back.Objects.Select(o => o.Uid.ToString()).ToArray()) + ")");
			added.transform.position += new Vector3(1f, 0f, 0f);
			Check(ref ok, DynamicIslands.SaveIsland(isl), "moved 1 m, saved again");
			back = IslandFile.Load(IslandSpawner.PathFor(isl));
			Check(ref ok, added.Uid == first && back.Objects.Count(o => o.Uid == first) == 1 && back.Objects.Select(o => o.Uid).Distinct().Count() == back.Objects.Count,
				"the moved chest keeps its number " + first + " (editor " + uids() + ")");

			// 2. A duplicate and a paste start without a number, then get their own
			gizmo.ClearTargets(false);
			gizmo.AddTarget(added.transform, false);
			int made = PlacementOptions.DuplicateSelection();
			yield return null;
			gizmo.ClearTargets(false);
			gizmo.AddTarget(added.transform, false);
			PlacementOptions.CopySelection();
			int pasted = PlacementOptions.PasteClipboard(false);
			yield return null;
			gizmo.ClearTargets(false);
			Check(ref ok, made == 1 && pasted == 1 && egos().Count(e => e.Uid == 0) == 2 && egos().Count(e => e.Uid == first) == 1,
				"a duplicate and a paste have no number yet, the chest keeps its own (" + made + " made, " + pasted + " pasted; " + uids() + ")");
			Check(ref ok, DynamicIslands.SaveIsland(isl), "saved with the copies");
			back = IslandFile.Load(IslandSpawner.PathFor(isl));
			List<int> now = egos().Select(e => e.Uid).ToList();
			Check(ref ok, now.All(u => u > 0) && now.Distinct().Count() == now.Count && added.Uid == first && now.OrderBy(u => u).SequenceEqual(back.Objects.Select(o => o.Uid).OrderBy(u => u)),
				"each copy got its own number, the chest kept " + first + " (" + uids() + ")");

			// 3. Save as over another island: only an object that island really had (same kind, same place) takes its number
			IslandFile shot = DynamicIslands.CaptureIsland(other);
			IslandObject at = shot.Objects[0];
			int top = 0;
			try
			{
				var o = new IslandFile { Name = other, TerrainSize = new Vector3(64, 40, 64), HeightmapResolution = 33, Heights = new float[33, 33], WaterLevel = 0 };
				o.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = new Vector3(25, 3, 25), EulerRotation = Vector3.zero, Scale = Vector3.one, Uid = 1 });
				o.Objects.Add(new IslandObject { Name = at.Name, Position = at.Position, EulerRotation = at.EulerRotation, Scale = at.Scale, Uid = 40 });
				o.NextUid = 41;
				o.Save(IslandSpawner.PathFor(other));
				IslandFile check = IslandFile.Load(IslandSpawner.PathFor(other));
				top = check.Objects.Count == 2 && check.Objects[1].Uid == 40 ? 40 : 0;
			}
			catch (Exception e) { Fail("uids saved: the other island - " + e.Message); ok = false; }
			Check(ref ok, top == 40, "the other island's file keeps its numbers 1 and 40");
			Check(ref ok, DynamicIslands.SaveIsland(other), "saved as the other island");
			back = IslandFile.Load(IslandSpawner.PathFor(other));
			string got = string.Join(",", back.Objects.Select(x => x.Uid.ToString()).ToArray());
			Check(ref ok, back.Objects.Count == shot.Objects.Count && back.Objects[0].Uid == 40, "the object in the other island's place took its number 40 (" + got + ")");
			Check(ref ok, back.Objects.Skip(1).All(x => x.Uid > 40) && back.Objects.Select(x => x.Uid).Distinct().Count() == back.Objects.Count,
				"the others got new numbers above the other island's, none of this island's old ones (" + got + ")");
			Check(ref ok, egos().Select(e => e.Uid).OrderBy(u => u).SequenceEqual(back.Objects.Select(x => x.Uid).OrderBy(u => u)), "the editor carries the new numbers (" + uids() + ")");

			DynamicIslands.NewIsland();
			yield return null;
			foreach (string p in leftovers) try { if (File.Exists(p)) File.Delete(p); } catch { }
			if (ok) Log("PASS: uids saved (review 10-30)"); else Fail("uids saved (review 10-30)");
		}

		[ConsoleCommand(name: "CIEdWindowOpen", docs: "Dev, editor: review 10-30 - the box after a save (InfoWindow), the island and plan pickers, the world settings and the defaults count as open windows (EditorInput.WindowOpen): Ctrl+Z waits behind them")]
		public static void EdWindowOpenCommand() { StartTest(EdWindowOpenRoutine()); }

		static IEnumerator EdWindowOpenRoutine()
		{
			yield return WaitForEditor(false);
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 1f, 500f);
			EditorGameObject a = PlaceForTest("Log", c0, placed);
			if (a == null) { Fail("window open: could not place a log"); yield break; }
			UndoRedoManager.Execute(new ObjectVisibilityCommand(new[] { a.gameObject }, true));
			yield return null;
			Check(ref ok, !EditorInput.WindowOpen, "no window open at the start");

			var windows = new List<KeyValuePair<string, KeyValuePair<Action, Action>>>
			{
				new KeyValuePair<string, KeyValuePair<Action, Action>>("the box after a save", new KeyValuePair<Action, Action>(() => InfoWindow.Open("CI", "review 10-30"), InfoWindow.Close)),
				new KeyValuePair<string, KeyValuePair<Action, Action>>("the island picker", new KeyValuePair<Action, Action>(IslandPickerWindow.Open, IslandPickerWindow.Close)),
				new KeyValuePair<string, KeyValuePair<Action, Action>>("the plan picker", new KeyValuePair<Action, Action>(PlanPickerWindow.Open, PlanPickerWindow.Close)),
				new KeyValuePair<string, KeyValuePair<Action, Action>>("the world settings", new KeyValuePair<Action, Action>(WorldSettingsWindow.Open, WorldSettingsWindow.Close)),
				new KeyValuePair<string, KeyValuePair<Action, Action>>("the defaults", new KeyValuePair<Action, Action>(DefaultsWindow.Open, DefaultsWindow.Close)),
			};
			Func<bool> isOpen = () => InfoWindow.IsOpen || IslandPickerWindow.IsOpen || PlanPickerWindow.IsOpen || WorldSettingsWindow.IsOpen || DefaultsWindow.IsOpen;
			int tried = 0;
			foreach (var w in windows)
			{
				try { w.Value.Key(); } catch (Exception e) { Fail("window open: opening " + w.Key + " - " + e.Message); ok = false; continue; }
				yield return null;
				if (!isOpen()) { Log("(window open: " + w.Key + " does not open here - skipped)"); continue; }
				tried++;
				Check(ref ok, EditorInput.WindowOpen, w.Key + " counts as an open window");
				if (w.Key == windows[0].Key)
				{
					yield return CvPress(KeyCode.LeftControl, KeyCode.Z);
					Check(ref ok, a.gameObject.activeSelf, "Ctrl+Z behind " + w.Key + " undoes nothing");
				}
				try { w.Value.Value(); } catch (Exception e) { Fail("window open: closing " + w.Key + " - " + e.Message); ok = false; }
				yield return null;
				Check(ref ok, !EditorInput.WindowOpen, w.Key + " closed: free again");
			}
			Check(ref ok, tried >= 1 && InfoWindow.IsOpen == false, tried + " window(s) tried, the box among them");

			UndoRedoManager.Clear();
			if (a != null) UnityEngine.Object.Destroy(a.gameObject);
			yield return null;
			if (ok) Log("PASS: window open (review 10-30)"); else Fail("window open (review 10-30)");
		}
	}
}
