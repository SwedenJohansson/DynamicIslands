using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests of the quest puzzle tools (2026-11-03): QE6 order puzzles, QE5 ready puzzle story sets.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIOrderParse", docs: "Dev, anywhere (QE6): order puzzles read right (the order action, a piece's use line and wrong message, its events, the editor's step check, the state key)")]
		public static void OrderParseCommand()
		{
			bool ok = true;
			ObjAction a = ObjAction.Parse("order|Gate|2");
			Check(ref ok, a != null && a.Verb == OrderPuzzle.Verb && a.Shared && ObjAction.HasTarget(OrderPuzzle.Verb) && ObjAction.HasArg(OrderPuzzle.Verb), "order|Gate|2 parses; it is shared (host)");
			Check(ref ok, a != null && OrderPuzzle.GroupOf(a) == "Gate" && OrderPuzzle.StepOf(a) == 2 && a.Describe().Contains("Gate") && a.Describe().Contains("2"), "group Gate, step 2 (" + (a != null ? a.Describe() : "") + ")");
			ObjAction b = ObjAction.Parse("order||");
			Check(ref ok, b != null && OrderPuzzle.GroupOf(b) == OrderPuzzle.DefaultGroup && OrderPuzzle.StepOf(b) == 1, "empty: group Order, step 1");
			Check(ref ok, OrderPuzzle.SignalOf(" Gate ") == "order:gate", "the signal order:gate");
			int k = OrderPuzzle.KeyOf("Gate");
			Check(ref ok, k == OrderPuzzle.KeyOf(" gate ") && k >= OrderPuzzle.KeyBase && k <= OrderPuzzle.KeyBase + 0xFFFF && k != OrderPuzzle.KeyOf("Other"), "the state key (" + k.ToString("X") + ")");
			Dictionary<string, string> p = OrderPuzzle.MakePiece(P("on.use", "message||Click"), "Gate", 1);
			Check(ref ok, ObjectProps.Get(p, "on.use") == "message||Click\norder|Gate|1" && ObjectProps.Get(p, BehaviourProps.Use).Length > 0 && ObjectProps.Get(p, "on.wrong").Length > 0, "make piece: the order line last, a use label, a wrong message (" + ObjectProps.Get(p, "on.use") + ")");
			p = OrderPuzzle.MakePiece(p, "Gate", 3);
			Check(ref ok, ObjectProps.Get(p, "on.use") == "message||Click\norder|Gate|3" && OrderPuzzle.StepIn(p, "gate") == 3 && OrderPuzzle.InGroup(p, "GATE") && !OrderPuzzle.InGroup(p, "Order"), "made again: the line changed in place (step 3)");
			IDictionary<string, string> x = OrderPuzzle.Expand("Log", P("on.use", "order|Gate|1"));
			Check(ref ok, ObjectProps.Get(x, "on.wrong") == "message||" + OrderPuzzle.WrongMessage, "a piece without wrong lines tells the player");
			IDictionary<string, string> own = P("on.use", "order|Gate|1", "on.wrong", "signal||w");
			Check(ref ok, ReferenceEquals(OrderPuzzle.Expand("Log", own), own), "a piece's own wrong lines are kept");
			IDictionary<string, string> none = P("on.use", "message||x");
			Check(ref ok, ReferenceEquals(OrderPuzzle.Expand("Log", none), none) && !OrderPuzzle.IsPiece(none), "an object without an order line is no piece");
			List<string> evs = BehaviourProps.EventsFor("Log", P("on.use", "order|Gate|1")).Select(v => v.Key).ToList();
			Check(ref ok, evs.Contains("solved") && evs.Contains("wrong") && PlayerFeatures.IsEvent("solved") && PlayerFeatures.IsEvent("wrong"), "a piece's events (" + string.Join(", ", evs.ToArray()) + ")");
			Check(ref ok, !BehaviourProps.EventsFor("Log", none).Any(v => v.Key == "solved"), "no solved event on other objects");
			Dictionary<string, string> light = OrderPuzzle.MakePiece(ObjectProps.Defaults(Lights.ObjectName), "Gate", 1);
			IDictionary<string, string> lx = PlayerFeatures.Expand(Lights.ObjectName, light);
			Check(ref ok, ObjectProps.Get(lx, "on.use").StartsWith("switch|") && ObjectProps.Get(lx, "on.use").Contains("order|Gate|1"), "a puzzle light as a piece: switched, then counted (" + ObjectProps.Get(lx, "on.use").Replace("\n", " / ") + ")");
			Check(ref ok, OrderPuzzle.Problem("G", new List<int> { 1, 2, 3 }) == null && OrderPuzzle.Problem("G", new List<int> { 1, 3 }) != null
				&& OrderPuzzle.Problem("G", new List<int> { 1 }) != null && OrderPuzzle.Problem("G", new List<int> { 1, 2, 2 }) == null, "the editor's check: a missing step, only one step");
			CheckPlayerTables(ref ok);
			if (ok) Log("PASS: order puzzles' settings"); else Fail("order puzzles' settings");
		}

		[ConsoleCommand(name: "CIOrderRun", docs: "Dev, in a world (host, QE6): spawns 'ciplayer' with an order puzzle (lamp 1, a log 2, lamp 3): a wrong press starts it again (wrong event, lamps back off), the right order sends order:order and every piece's solved event; later presses change nothing")]
		public static void OrderRunCommand() { StartTest(OrderRunRoutine()); }

		static IEnumerator OrderRunRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			int[] piece = new int[3];
			int gate = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				Dictionary<string, string> l1 = OrderPuzzle.MakePiece(ObjectProps.Defaults(Lights.ObjectName), OrderPuzzle.DefaultGroup, 1);
				l1["on.solved"] = "signal||order-reward";
				piece[0] = add(Lights.ObjectName, -3f, 4f, l1);
				piece[1] = add("Log", 0f, 4f, OrderPuzzle.MakePiece(P(BehaviourProps.Use, "Pull"), OrderPuzzle.DefaultGroup, 2));
				Dictionary<string, string> l3 = OrderPuzzle.MakePiece(ObjectProps.Defaults(Lights.ObjectName), OrderPuzzle.DefaultGroup, 3);
				l3["on.wrong"] = "message||" + OrderPuzzle.WrongMessage + "\nsignal||order-wrong";
				piece[2] = add(Lights.ObjectName, 3f, 4f, l3);
				gate = add("Log", 0f, 8f, P(BehaviourProps.Use, "Open", "if.use", "signal|" + OrderPuzzle.SignalOf(OrderPuzzle.DefaultGroup) + "|", "on.use", "signal||order-gate"));
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, v => e = v);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			Func<int, IslandObjectRef> obj = i => e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			Func<int, bool> lit = i => { IslandObjectRef r = obj(i); return r != null && Lights.Lit(e, r); };
			Func<int, bool> press = i =>
			{
				IslandObjectRef r = obj(i);
				UseInteract u = r != null ? r.GetComponentInChildren<UseInteract>(true) : null;
				if (u == null) return false;
				Traverse.Create(u).Method("Use").GetValue();
				return true;
			};
			string g = OrderPuzzle.DefaultGroup;
			try
			{
				yield return new WaitForSeconds(1f);
				Check(ref ok, OrderPuzzle.Steps(e, g) == 3 && OrderPuzzle.Pieces(e, g).Count == 3 && OrderPuzzle.Progress(e, g) == 0, "three pieces, three steps, none done");
				if (obj(piece[1]) != null) { PutPlayerNear(obj(piece[1]).transform, 4f); Screenshot(new[] { "order" }); }
				Check(ref ok, press(piece[0]), "piece 1 can be pressed");
				yield return new WaitForSeconds(0.8f);
				Check(ref ok, OrderPuzzle.Progress(e, g) == 1 && lit(piece[0]), "step 1 done, lamp 1 on (progress " + OrderPuzzle.Progress(e, g) + ")");
				Check(ref ok, press(piece[2]), "piece 3 can be pressed");
				yield return new WaitForSeconds(0.2f);
				Check(ref ok, OrderPuzzle.Progress(e, g) == 0 && Behaviours.HasSignal(e, "order-wrong"), "3 after 1 is wrong: back to the start, its wrong event ran");
				yield return new WaitForSeconds(1.2f);
				Check(ref ok, !lit(piece[0]) && !lit(piece[2]), "the lamps went back off (" + lit(piece[0]) + "/" + lit(piece[2]) + ")");
				Check(ref ok, !OrderPuzzle.Solved(e, g), "not solved");
				Behaviours.Fire(e, gate, "use", true);
				yield return new WaitForSeconds(0.3f);
				Check(ref ok, !Behaviours.HasSignal(e, "order-gate"), "the gate's signal check fails before it is solved");
				for (int n = 0; n < 3; n++)
				{
					Check(ref ok, press(piece[n]), "piece " + (n + 1) + " pressed");
					yield return new WaitForSeconds(0.8f);
					Check(ref ok, OrderPuzzle.Progress(e, g) == n + 1, "progress " + OrderPuzzle.Progress(e, g) + " after piece " + (n + 1));
					if (n < 2) Check(ref ok, !OrderPuzzle.Solved(e, g), "not solved yet");
				}
				Check(ref ok, OrderPuzzle.Solved(e, g) && Behaviours.HasSignal(e, "order:order"), "solved: the signal order:order");
				Check(ref ok, Behaviours.HasSignal(e, "order-reward"), "piece 1's solved event ran");
				Check(ref ok, lit(piece[0]) && lit(piece[2]), "both lamps on");
				if (obj(piece[1]) != null) Screenshot(new[] { "order_solved" });
				Behaviours.Fire(e, gate, "use", true);
				yield return new WaitForSeconds(0.3f);
				Check(ref ok, Behaviours.HasSignal(e, "order-gate"), "the gate opens on the signal");
				yield return new WaitForSeconds(0.8f);
				press(piece[1]);
				yield return new WaitForSeconds(1.2f);
				Check(ref ok, OrderPuzzle.Solved(e, g) && lit(piece[0]) && lit(piece[2]), "a press after solving changes nothing (no reset)");
			}
			finally { RemovePlayerIsland(e); }
			if (ok) Log("PASS: order puzzles in a world"); else Fail("order puzzles in a world");
		}

		const string PuzzleIsland = "cipuzzlesets";

		[ConsoleCommand(name: "CIPuzzleSets", docs: "Dev, editor (QE5): a flat plateau, then the four puzzle story sets (levers in order, three wheels, generator and radio, laser and mirrors) placed on it: their pieces and behaviours, the laser's beam path checked (no warning), the window's button and undo; saves the island 'cipuzzlesets' for CIPuzzleSetsPlay")]
		public static void PuzzleSetsCommand() { StartTest(PuzzleSetsRoutine()); }

		static IEnumerator PuzzleSetsRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			yield return PlaceableCatalog.EnsureLoaded(StorySets.LeverNeeds.Concat(StorySets.WheelNeeds).Concat(StorySets.GeneratorNeeds).Concat(StorySets.LaserNeeds).Concat(new[] { "Loot_Chest", "Loot_ChestSmall", "Note_Paper" }));
			EditorUI.SetTab(TAB.Island);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();

			// A flat plateau 3 m above the water, 45 m round the middle (sloping down to 60 m)
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			int res = data.heightmapResolution;
			float[,] heights = data.GetHeights(0, 0, res, res);
			float top = (DynamicIslands.EditorWaterLevel + 3f) / data.size.y, cell = data.size.x / (res - 1);
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
				{
					float d = Mathf.Sqrt((x - res / 2) * (x - res / 2) + (y - res / 2) * (y - res / 2)) * cell;
					if (d < 60f) heights[y, x] = Mathf.Lerp(heights[y, x], top, d < 45f ? 1f : Mathf.SmoothStep(1f, 0f, (d - 45f) / 15f));
				}
			data.SetHeights(0, 0, heights);
			terraineditor.paintMask = null;
			TerrainPainter.Setup(terrain, DynamicIslands.EditorWaterLevel);
			terraineditor.paintMask = new float[data.alphamapResolution, data.alphamapResolution];
			yield return null;
			Vector3 c = terrain.transform.position + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			c.y = terrain.transform.position.y + terrain.SampleHeight(c);
			Func<string, EditorGameObject> named = n => placed.GetComponentsInChildren<EditorGameObject>(true).FirstOrDefault(x => string.Equals(ObjectProps.Get(x.Props, BehaviourProps.Name), n, StringComparison.OrdinalIgnoreCase));
			Func<string, string, string> prop = (n, k) => { EditorGameObject x = named(n); return x != null ? ObjectProps.Get(x.Props, k) : ""; };
			var none = new List<StoryItemDef>();

			// Levers in order
			List<GameObject> levers = StorySets.LeversInOrder(none, c + new Vector3(-20f, 0f, -20f));
			Dictionary<string, List<int>> steps = OrderPuzzle.EditorSteps(placed.GetComponentsInChildren<EditorGameObject>(true));
			Check(ref ok, levers.Count == 5 && steps.ContainsKey("Levers") && steps["Levers"].OrderBy(i => i).SequenceEqual(new[] { 1, 2, 3 }) && OrderPuzzle.Problem("Levers", steps["Levers"]) == null,
				"levers in order: 5 objects, the order puzzle Levers with steps 1-3 (" + levers.Count + ")");
			Check(ref ok, prop("lever-right", "on.use").EndsWith("order|Levers|1") && prop("lever-left", "on.use").EndsWith("order|Levers|2") && prop("lever-middle", "on.use").EndsWith("order|Levers|3"),
				"right 1, left 2, middle 3");
			Check(ref ok, prop("lever-right", "on.solved").StartsWith("show|lever-chest|") && prop("lever-chest", BehaviourProps.Hidden) == "1" && prop("lever-chest", ObjectProps.LootItems).Length > 0, "solved shows the hidden chest");

			// Three wheels
			List<GameObject> wheels = StorySets.ThreeWheels(none, c + new Vector3(0f, 0f, 25f));
			Check(ref ok, wheels.Count == 5 && prop("wheel-1", BehaviourProps.Turn) == "90" && prop("wheel-3", "on.use").Contains("switch|"), "three wheels: 5 objects, the wheels turn on use (" + wheels.Count + ")");
			Check(ref ok, prop("wheel-gate", "if.use") == "state|wheel-1|open\nstate|wheel-2|closed\nstate|wheel-3|open" && prop("wheel-gate", "else.use").Length > 0 && prop("wheel-gate", "on.use").Contains("signal||wheels"),
				"the gate checks outer turned, middle not; sends wheels");

			// Generator and radio
			List<GameObject> gen = StorySets.GeneratorRadio(none, c + new Vector3(20f, 0f, -20f));
			EditorGameObject g = named("generator");
			Check(ref ok, gen.Count == 4 && g != null && ReadyPieces.PartsOf(g.Props) == 1 && prop("radio", "on.use").Contains("journal|") && prop("radio", "if.use").Contains("signal|power"),
				"generator and radio: 4 objects, the generator wants 1 part, the radio needs the power and adds a journal page (" + gen.Count + ")");
			string part = g != null ? ReadyPieces.ItemOf(g.GameObjectName) : "?";
			Check(ref ok, gen.Any(go => ObjectProps.Get(go.GetComponent<EditorGameObject>().Props, ObjectProps.LootItems).StartsWith(StoryItems.Ref(part) + "*1")), "a chest holds the part (" + part + ")");

			// Laser and mirrors
			List<GameObject> laser = StorySets.LaserMirrors(none, c);
			string warn = StorySets.Warning;
			Check(ref ok, laser.Count == 7 && warn == null, "laser and mirrors: 7 objects, the beam's path is clear (" + laser.Count + ", " + (warn ?? "no warning") + ")");
			Check(ref ok, prop("mirror-fixed", LaserBeam.Prop) == LaserBeam.Mirror && prop("mirror-fixed", BehaviourProps.Turn) == "" && prop("mirror-turn", BehaviourProps.Turn) == "90",
				"one fixed mirror, one that turns a quarter");
			Check(ref ok, prop("laser-panel", "on.laser").Contains("show|laser-chest|") && prop("laser-chest", BehaviourProps.Hidden) == "1", "the panel shows the hidden chest");
			EditorGameObject em = named("laser");
			if (em != null)
			{
				Bounds b = StorySets.ModelBounds(em.gameObject);
				Log("  emitter forward " + em.transform.forward.ToString("F2") + ", model " + b.min.y.ToString("F2") + ".." + b.max.y.ToString("F2") + ", ground " + c.y.ToString("F2"));
				Check(ref ok, Mathf.Abs(b.min.y - c.y) < 0.3f && Mathf.Abs(em.transform.forward.y) < 0.05f, "the emitter stands level on the ground");
				foreach (string n in new[] { "mirror-fixed", "mirror-turn", "laser-panel" })
					if (named(n) != null) Log("  " + n + " at " + (named(n).transform.position - c).ToString("F1") + ", centre " + (StorySets.ModelCentre(named(n).gameObject) - c).ToString("F1"));
			}
			Camera.main.transform.position = c + new Vector3(0f, 35f, -45f);
			Camera.main.transform.LookAt(c + new Vector3(0f, 0f, 3f));
			yield return null;
			Screenshot(new[] { "puzzlesets" });
			yield return new WaitForSecondsRealtime(0.6f);
			if (em != null)
			{
				Vector3 f = em.transform.forward, r = Quaternion.Euler(0f, 90f, 0f) * new Vector3(f.x, 0f, f.z).normalized;
				Camera.main.transform.position = c - f * 6f - r * 4f + Vector3.up * 7f;
				Camera.main.transform.LookAt(c + f * 3.5f + r * 3.5f);
				yield return null;
				Screenshot(new[] { "puzzle_laser" });
				yield return new WaitForSecondsRealtime(0.6f);
			}

			// The window's button places a set too (loaded pieces: at once); one undo takes it away
			Camera.main.transform.position = c + new Vector3(-30f, 25f, 0f);
			Camera.main.transform.LookAt(c + new Vector3(-30f, 0f, 15f));
			int before = placed.GetComponentsInChildren<EditorGameObject>(true).Count(x => x.gameObject.activeSelf);
			StoryItemsWindow.Open();
			yield return new WaitForSecondsRealtime(0.3f);
			Click(new[] { "Levers in order" });
			yield return new WaitForSecondsRealtime(0.5f);
			int after = placed.GetComponentsInChildren<EditorGameObject>(true).Count(x => x.gameObject.activeSelf);
			Check(ref ok, after == before + 5 && named("lever-right-2") != null && ObjectProps.Get(named("lever-right-2").Props, "on.use").Contains("order|Levers-2|1"), "the window's button places a second set (group Levers-2; " + before + " -> " + after + ")");
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			Check(ref ok, placed.GetComponentsInChildren<EditorGameObject>(true).Count(x => x.gameObject.activeSelf) == before, "one undo takes it away");
			foreach (EditorGameObject x in placed.GetComponentsInChildren<EditorGameObject>(true).Where(x => !x.gameObject.activeSelf).ToList()) UnityEngine.Object.DestroyImmediate(x.gameObject);
			if (StoryItemsWindow.IsOpen) StoryItemsWindow.SaveNow();

			// Saved for the world test
			DynamicIslands.SaveIsland(PuzzleIsland);
			IslandFile file = IslandFile.Load(IslandSpawner.PathFor(PuzzleIsland));
			Check(ref ok, file != null && file.Objects.Count >= 21 && file.Objects.Count(o => OrderPuzzle.IsPiece(o.Props)) == 3, "saved as '" + PuzzleIsland + "' (" + (file != null ? file.Objects.Count : -1) + " objects)");
			if (ok) Log("PASS: puzzle story sets"); else Fail("puzzle story sets");
		}

		[ConsoleCommand(name: "CIPuzzleSetsPlay", docs: "Dev, in a world (host, QE5): spawns 'cipuzzlesets' (made by CIPuzzleSets) and plays its four sets: levers in the wrong then right order, the wheels set right opens the gate, the generator with its part powers the radio, the turned mirror sends the beam to the panel; removes it")]
		public static void PuzzleSetsPlayCommand() { StartTest(PuzzleSetsPlayRoutine()); }

		/// <summary>A picture of a placed set from above and to one side, looking at the middle of the named objects.</summary>
		static IEnumerator SetShot(IslandWorldState.Entry e, string[] names, float dist, string name)
		{
			List<Vector3> at = names.Select(n => ScObjOf(e, n)).Where(r => r != null).Select(r => r.transform.position).ToList();
			if (at.Count == 0) yield break;
			Vector3 mid = at.Aggregate(Vector3.zero, (a, b) => a + b) / at.Count + Vector3.up;
			yield return CameraShot(mid + new Vector3(-0.6f * dist, 0.7f * dist, -0.6f * dist), mid, name);
		}

		static IEnumerator PuzzleSetsPlayRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			if (!System.IO.File.Exists(IslandSpawner.PathFor(PuzzleIsland))) { Fail("no island '" + PuzzleIsland + "': run CIPuzzleSets in the editor first"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Vector3? spot = CustomIslandSpawner.FindClearSpot(CustomIslandSpawner.RaftPosition.Value, CustomIslandSpawner.LandRadius(PuzzleIsland), 300f);
			if (!spot.HasValue) { Fail("no free spot"); yield break; }
			int count = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(PuzzleIsland, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(count).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("the island did not spawn"); yield break; }
			string part = ReadyPieces.ItemOf("EmergencyGenerator") ?? "raft-Tangaroa_GeneratorPart", charger = "raft-Caravan_BatteryChargerPart";
			int hadPart = StoryBook.Count(part), hadCharger = StoryBook.Count(charger);
			try
			{
				yield return new WaitForSeconds(1.5f);
				// Levers: a wrong one first, then right, left, middle
				string g = "Levers";
				Check(ref ok, OrderPuzzle.Steps(e, g) == 3 && !GstShown(e, "lever-chest"), "three levers, the chest hidden");
				ScUse(e, "lever-left");
				yield return new WaitForSeconds(1.1f);
				Check(ref ok, OrderPuzzle.Progress(e, g) == 0, "the left lever first is wrong (progress 0)");
				foreach (string n in new[] { "lever-right", "lever-left", "lever-middle" })
				{
					ScUse(e, n);
					yield return new WaitForSeconds(1.1f);
				}
				Check(ref ok, OrderPuzzle.Solved(e, g) && GstShown(e, "lever-chest"), "right, left, middle: solved, the chest shows (progress " + OrderPuzzle.Progress(e, g) + ")");
				yield return SetShot(e, new[] { "lever-left", "lever-right", "lever-chest" }, 9f, "puzzle_levers");

				// Wheels: the gate refuses, then the outer two turned
				ScUse(e, "wheel-gate");
				yield return new WaitForSeconds(1.1f);
				Check(ref ok, !GstOpen(e, "wheel-gate") && !Behaviours.HasSignal(e, "wheels"), "the gate stays shut while the wheels are wrong");
				ScUse(e, "wheel-1");
				ScUse(e, "wheel-3");
				yield return new WaitForSeconds(2f);
				ScUse(e, "wheel-gate");
				yield return new WaitForSeconds(2f);
				Check(ref ok, GstOpen(e, "wheel-1") && !GstOpen(e, "wheel-2") && GstOpen(e, "wheel-3") && GstOpen(e, "wheel-gate") && Behaviours.HasSignal(e, "wheels"), "outer wheels turned: the gate opens, signal wheels");
				yield return SetShot(e, new[] { "wheel-1", "wheel-3", "wheel-gate" }, 16f, "puzzle_wheels");

				// Generator and radio: nothing without the part; with it, power, then the radio
				if (hadPart > 0) StoryBook.Take(part, hadPart);
				if (hadCharger > 0) StoryBook.Take(charger, hadCharger);
				ScUse(e, "radio");
				yield return new WaitForSeconds(1.1f);
				Check(ref ok, !Behaviours.HasSignal(e, ReadyPieces.RadioSignal), "the radio is dead without power");
				ScUse(e, "generator");
				yield return new WaitForSeconds(1.1f);
				Check(ref ok, !Behaviours.HasSignal(e, ReadyPieces.PowerSignal), "the generator won't start without its part");
				StoryBook.Give(part, 1);
				ScUse(e, "generator");
				yield return new WaitForSeconds(1.1f);
				Check(ref ok, Behaviours.HasSignal(e, ReadyPieces.PowerSignal) && StoryBook.Count(part) == 0, "with the part it starts (signal power, the part used up)");
				ScUse(e, "radio");
				yield return new WaitForSeconds(1.1f);
				Check(ref ok, Behaviours.HasSignal(e, ReadyPieces.RadioSignal), "the radio works with the power on (signal radio)");
				yield return SetShot(e, new[] { "generator", "radio" }, 8f, "puzzle_generator");

				// Laser: the placed turning mirror sends the beam past; turned, it reaches the panel
				IslandObjectRef emitter = ScObjOf(e, "laser");
				LaserBeam beam = emitter != null ? emitter.GetComponent<LaserBeam>() : null;
				Check(ref ok, beam != null && ScObjOf(e, "laser-panel") != null, "an emitter with a beam and the panel");
				if (beam != null)
				{
					beam.Trace();
					Check(ref ok, beam.Mirrors.Count == 2 && (beam.Hit == null || beam.Hit.Name != "laser-panel") && !GstShown(e, "laser-chest"), "placed: the beam goes over both mirrors and past the panel (" + LaserPath(beam) + ")");
					ScUse(e, "mirror-turn");
					yield return new WaitForSeconds(2f);
					beam.Trace();
					yield return new WaitForSeconds(0.6f);
					Check(ref ok, beam.Hit != null && beam.Hit.Name == "laser-panel", "the turned mirror sends it to the panel (" + LaserPath(beam) + ")");
					Check(ref ok, Behaviours.HasSignal(e, ReadyPieces.LaserSignal) && GstShown(e, "laser-chest"), "signal laser, the chest behind the wall shows");
					yield return SetShot(e, new[] { "laser", "mirror-fixed", "mirror-turn", "laser-panel" }, 11f, "puzzle_laser_world");
				}
			}
			finally
			{
				int now = StoryBook.Count(part);
				if (now < hadPart) StoryBook.Give(part, hadPart - now);
				if (hadCharger > 0) StoryBook.Give(charger, hadCharger);
				IslandWorldState.RemoveIds(new[] { e.Id }, true);
			}
			if (ok) Log("PASS: puzzle story sets in a world"); else Fail("puzzle story sets in a world");
		}
	}
}
