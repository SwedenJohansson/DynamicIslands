using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "In this order" puzzles (ROADMAP QE6, 2026-11-03): levers, lights, buttons - any object - pressed in a set order.
	/// A piece is an object with the action "order|group|n" (step n of the puzzle "group"), usually last on its "use"
	/// event. The host keeps how far the crew got (state key KeyBase + the group's hash: saved, kept by the host only);
	/// the right next piece moves it on, any other piece is a wrong press: the progress goes back to 0, the pressed piece
	/// runs its "wrong" event, and half a second later the group's pieces go back as placed (lights off, opened pieces
	/// closed). The last step sends the signal "order:group" and runs every piece's "solved" event, once for good.
	/// "wrong" and "solved" are not resumable events (PlayerFeatures.ResumableEvents is full): their actions after a wait
	/// run in this session only.
	/// </summary>
	public static class OrderPuzzle
	{
		public const string Verb = "order", SolvedEvent = "solved", WrongEvent = "wrong", DefaultGroup = "Order";
		public const string WrongMessage = "Wrong order! Everything clicks back.";
		/// <summary>Island state: KeyBase + (the group's hash &amp; 0xFFFF) = the steps done (Yield). Not an object's index.</summary>
		public const int KeyBase = 0x4100000;
		/// <summary>Seconds from a wrong press to the pieces going back (the player sees the press first).</summary>
		public const float ResetDelay = 0.6f;

		public static string GroupOf(ObjAction a) { string g = (a != null ? a.Target : "").Trim(); return g.Length > 0 ? g : DefaultGroup; }

		public static int StepOf(ObjAction a)
		{
			int n;
			return a != null && int.TryParse((a.Arg ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0 ? n : 1;
		}

		public static string SignalOf(string group) { return "order:" + (group ?? "").Trim().ToLowerInvariant(); }

		public static int KeyOf(string group)
		{
			int h = 17;
			foreach (char c in (group ?? "").Trim().ToLowerInvariant()) h = h * 31 + c;
			return KeyBase + (h & 0xFFFF);
		}

		/// <summary>An object's order actions (any event's lines).</summary>
		public static List<ObjAction> ActionsIn(IDictionary<string, string> p)
		{
			var list = new List<ObjAction>();
			if (p == null) return list;
			foreach (KeyValuePair<string, string> kv in p)
				if (kv.Key.StartsWith(BehaviourProps.EventPrefix) && kv.Value != null && kv.Value.Contains(Verb + "|"))
					list.AddRange(ObjAction.ParseLines(kv.Value).Where(a => a.Verb == Verb));
			return list;
		}

		public static bool IsPiece(IDictionary<string, string> p) { return ActionsIn(p).Count > 0; }

		/// <summary>Whether the object is a piece of this group.</summary>
		public static bool InGroup(IDictionary<string, string> p, string group)
		{
			return ActionsIn(p).Any(a => string.Equals(GroupOf(a), (group ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>A piece's step in a group (0 = not in it).</summary>
		public static int StepIn(IDictionary<string, string> p, string group)
		{
			ObjAction a = ActionsIn(p).FirstOrDefault(x => string.Equals(GroupOf(x), (group ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
			return a != null ? StepOf(a) : 0;
		}

		/// <summary>World: a piece without "wrong" lines of its own tells the player (as if the builder had written it).</summary>
		public static IDictionary<string, string> Expand(string objectName, IDictionary<string, string> props)
		{
			if (props == null || !IsPiece(props) || ObjectProps.Get(props, BehaviourProps.EventKey(WrongEvent)).Length > 0) return props;
			var d = new Dictionary<string, string>(props);
			d[BehaviourProps.EventKey(WrongEvent)] = "message||" + WrongMessage;
			return d;
		}

		/// <summary>The island's pieces of a group (loaded ones).</summary>
		public static List<IslandObjectRef> Pieces(IslandWorldState.Entry e, string group)
		{
			return Behaviours.RefsOf(e).Where(r => InGroup(r.Props, group)).ToList();
		}

		/// <summary>How many steps the group has (its highest step number).</summary>
		public static int Steps(IslandWorldState.Entry e, string group)
		{
			int most = 0;
			foreach (IslandObjectRef r in Pieces(e, group)) most = Math.Max(most, StepIn(r.Props, group));
			return most;
		}

		/// <summary>The steps done (host's count; 0 on a client).</summary>
		public static int Progress(IslandWorldState.Entry e, string group)
		{
			ObjectState s;
			return e != null && e.State.TryGetValue(KeyOf(group), out s) ? Math.Max(0, s.Yield) : 0;
		}

		public static bool Solved(IslandWorldState.Entry e, string group) { return Behaviours.HasSignal(e, SignalOf(group)); }

		static void SetProgress(IslandWorldState.Entry e, string group, int n)
		{
			e.State[KeyOf(group)] = new ObjectState { Active = true, Yield = n, Day = Behaviours.Today };
		}

		/// <summary>Host: the shared action "order|group|n" - a piece was pressed.</summary>
		internal static void HostRun(IslandWorldState.Entry e, int index, ObjAction a)
		{
			if (e == null || !Raft_Network.IsHost) return;
			string group = GroupOf(a);
			if (Solved(e, group)) return; // (solved for good: later presses are just presses)
			int n = StepOf(a), done = Progress(e, group), total = Steps(e, group);
			if (n == done + 1)
			{
				SetProgress(e, group, n);
				Debug.Log("[CUSTOM ISLANDS] Order '" + group + "' on '" + e.HostName + "': step " + n + " of " + total);
				if (n < total) return;
				Behaviours.SendSignal(e, SignalOf(group));
				foreach (IslandObjectRef r in Pieces(e, group)) Behaviours.FireFromHost(e, r.Index, SolvedEvent);
				return;
			}
			Debug.Log("[CUSTOM ISLANDS] Order '" + group + "' on '" + e.HostName + "': step " + n + " pressed after " + done + " - wrong, back to the start");
			SetProgress(e, group, 0);
			Behaviours.FireFromHost(e, index, WrongEvent);
			if (DynamicIslands.instance != null) DynamicIslands.instance.StartCoroutine(ResetLater(e, group));
			else ResetPieces(e, group);
		}

		static IEnumerator ResetLater(IslandWorldState.Entry e, string group)
		{
			yield return new WaitForSeconds(ResetDelay);
			if (IslandWorldState.Contains(e) && e.Root != null) ResetPieces(e, group);
		}

		/// <summary>Host: the group's pieces past the steps done go back as placed: a light off, an opened piece closed.</summary>
		internal static void ResetPieces(IslandWorldState.Entry e, string group)
		{
			if (Solved(e, group)) return;
			int done = Progress(e, group);
			foreach (IslandObjectRef r in Pieces(e, group))
			{
				if (StepIn(r.Props, group) <= done) continue; // (pressed again right after the wrong one: kept)
				bool visible, open;
				Behaviours.StateNow(e, r, out visible, out open);
				bool shut = Lights.IsLight(r.ObjectName, r.Props) ? Lights.StartsOn(r.Props) : false;
				if (open != shut) Behaviours.SetState(e, r.Index, visible, shut);
			}
		}

		/// <summary>An action's words for the builder.</summary>
		public static string Describe(ObjAction a) { return "count as step " + StepOf(a) + " of the order puzzle '" + GroupOf(a) + "' (wrong order: back to the start)"; }

		/// <summary>The steps the pieces placed in the editor have, by group (inspector, Check).</summary>
		public static Dictionary<string, List<int>> EditorSteps(IEnumerable<EditorGameObject> all)
		{
			var d = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
			foreach (EditorGameObject g in all)
				foreach (ObjAction a in ActionsIn(g.Props))
				{
					List<int> l;
					if (!d.TryGetValue(GroupOf(a), out l)) d[GroupOf(a)] = l = new List<int>();
					l.Add(StepOf(a));
				}
			return d;
		}

		/// <summary>What is wrong with a group's steps (a step no piece has: never solved), or null.</summary>
		public static string Problem(string group, List<int> steps)
		{
			if (steps == null || steps.Count == 0) return null;
			int most = steps.Max();
			List<int> missing = Enumerable.Range(1, most).Where(i => !steps.Contains(i)).ToList();
			if (missing.Count > 0) return "the order puzzle '" + group + "' has no piece for step " + string.Join(", ", missing.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray()) + ": it can never be solved";
			if (most < 2) return "the order puzzle '" + group + "' has only one step";
			return null;
		}

		/// <summary>A piece's props with its order line on "use" set to group/step (added last, or changed in place);
		/// a use label and the default "wrong" message when it has none.</summary>
		public static Dictionary<string, string> MakePiece(IDictionary<string, string> props, string group, int step)
		{
			var d = new Dictionary<string, string>(props ?? new Dictionary<string, string>());
			string key = BehaviourProps.EventKey("use");
			string line = Verb + "|" + group + "|" + step.ToString(CultureInfo.InvariantCulture);
			var lines = (ObjectProps.Get(d, key)).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
			int at = lines.FindIndex(l => { ObjAction x = ObjAction.Parse(l); return x != null && x.Verb == Verb; });
			if (at >= 0) lines[at] = line; else lines.Add(line);
			d[key] = string.Join("\n", lines.ToArray());
			if (ObjectProps.Get(d, BehaviourProps.Use).Length == 0) d[BehaviourProps.Use] = "Press it";
			if (ObjectProps.Get(d, BehaviourProps.EventKey(WrongEvent)).Length == 0) d[BehaviourProps.EventKey(WrongEvent)] = "message||" + WrongMessage;
			return d;
		}
	}
}
