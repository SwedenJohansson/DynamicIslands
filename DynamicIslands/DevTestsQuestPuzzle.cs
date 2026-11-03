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
	/// <summary>Tests of the quest puzzle tools (2026-11-03): QE6 order puzzles.</summary>
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
	}
}
