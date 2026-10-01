using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// How much of the world's questline is done, for the journal (J): every quest of the custom islands in the world,
	/// and of the islands its plan (and the islands' own rules) will still bring, counted by steps. The host counts
	/// (it has the plan, every island file and the rules' state) and sends the numbers to everyone (IslandNetMessage.QuestLine),
	/// so every player sees the same; a player shows their own count only until the host's has come.
	/// Islands that come by chance while sailing, or from a rule that picks one at random ("a new ... island", "one of",
	/// the spawn pool), count once they are in the world: before that nobody knows their quest.
	/// </summary>
	public static class QuestLine
	{
		public class Totals
		{
			public int StepsDone, Steps, QuestsDone, Quests;

			/// <summary>Done, in per cent of all quest steps (100 only when every step is done).</summary>
			public int Percent { get { return Steps <= 0 ? 0 : StepsDone >= Steps ? 100 : Mathf.Min(99, (int)Math.Floor(100.0 * StepsDone / Steps)); } }

			public string Encode() { return string.Join(",", new[] { StepsDone, Steps, QuestsDone, Quests }.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()); }

			public static Totals Decode(string data)
			{
				int[] n = (data ?? "").Split(',').Select(x => { int v; return int.TryParse(x.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? Mathf.Max(0, v) : -1; }).ToArray();
				if (n.Length < 4 || n.Any(v => v < 0)) return null;
				return new Totals { StepsDone = Mathf.Min(n[0], n[1]), Steps = n[1], QuestsDone = Mathf.Min(n[2], n[3]), Quests = n[3] };
			}

			/// <summary>The journal's line: "42% of the questline done - 3 of 7 quests".</summary>
			public string Describe()
			{
				if (Quests == 0) return "No quests in this world yet";
				return Percent + "% of the questline done · " + QuestsDone + " of " + Quests + " quest" + (Quests == 1 ? "" : "s");
			}
		}

		const float TickInterval = 1f;
		/// <summary>How deep island rules are followed (an island brought by an island brought by ...): cycles stop anyway.</summary>
		const int MaxDepth = 8;

		const float FullCountSeconds = 30f;

		static Totals fromHost;
		static string lastSent, lastSignature;
		static float nextTick, nextFullCount;

		/// <summary>What the count depends on, cheaply (no files): the islands, the steps they reached, the rules that fired.</summary>
		static string Signature()
		{
			int steps = 0, rules = 0;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				steps = steps * 31 + e.Id * 7 + QuestTracker.StepOf(e) + (e.Root != null ? 1 : 0);
				rules += e.State.Keys.Count(k => k >= WorldDirector.RuleKeyBase && k < WorldDirector.RuleKeyBase + 0x100);
			}
			return IslandWorldState.Islands.Count + "/" + steps + "/" + rules + "/" + WorldDirector.Done.Count + "/" + StoryChain.Brought.Count + "/" +
				(WorldDirector.Plan != null ? WorldDirector.Plan.Rules.Count + WorldDirector.PlanName : "-") + "/" + (WorldCopy.HostLines != null ? WorldCopy.HostLines.Length : -1);
		}

		/// <summary>Raised on this machine when the shown numbers change (the journal listens).</summary>
		public static event Action Changed;

		/// <summary>What this machine shows: the host's count (a player: once it has come; until then their own).</summary>
		public static Totals Shown { get { return !Raft_Network.IsHost && fromHost != null ? fromHost : Compute(); } }

		/// <summary>A player: the host's numbers have come.</summary>
		public static bool HasHostCount { get { return fromHost != null; } }

		/// <summary>A world was left or loaded.</summary>
		public static void Reset()
		{
			fromHost = null;
			lastSent = null;
			lastSignature = null;
			Raise();
		}

		static void Raise() { if (Changed != null) try { Changed(); } catch { } }

		/// <summary>
		/// The questline here: the quests of the islands in the world (the step each has reached), plus the quests of
		/// the islands still to come by name (the plan's rules and the islands' own rules that haven't fired, followed
		/// through the islands they bring).
		/// </summary>
		public static Totals Compute()
		{
			var t = new Totals();
			if (!LoadSceneManager.IsGameSceneLoaded) return t;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.ToList())
			{
				IslandQuest q = QuestTracker.QuestOf(e);
				if (q.Exists) Add(t, q, QuestTracker.StepOf(e));
				// (the island's own rules that haven't brought their island yet)
				List<IntroRule> rules = WorldDirector.RulesOf(e);
				for (int i = 0; i < rules.Count; i++)
					if (!e.State.ContainsKey(WorldDirector.RuleKeyBase + i)) AddComing(t, rules[i], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { e.Name ?? "" }, 0);
			}
			foreach (IntroRule r in PlanRulesToCome()) AddComing(t, r, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
			return t;
		}

		static void Add(Totals t, IslandQuest q, int step)
		{
			t.Quests++;
			t.Steps += q.Steps.Count;
			int done = Mathf.Clamp(step, 0, q.Steps.Count);
			t.StepsDone += done;
			if (done >= q.Steps.Count) t.QuestsDone++;
		}

		/// <summary>An island a rule will still bring by name: its quest (none done), and the islands its own rules bring.</summary>
		static void AddComing(Totals t, IntroRule r, HashSet<string> seen, int depth)
		{
			if (r == null || r.What != "island" || depth > MaxDepth) return;
			string name = (r.WhatArg ?? "").Trim();
			if (name.Length == 0 || !seen.Add(name)) return;
			Dictionary<string, string> props = IslandCache.Props(name);
			IslandQuest q = IslandQuest.From(props);
			if (q.Exists) Add(t, q, 0);
			foreach (IntroRule own in WorldDirector.RulesFromProps(props)) AddComing(t, own, seen, depth + 1);
			seen.Remove(name);
		}

		/// <summary>The plan's rules that haven't brought their island yet (the host's plan: on a player's PC from the host's copy of the world).</summary>
		static IEnumerable<IntroRule> PlanRulesToCome()
		{
			if (Raft_Network.IsHost)
			{
				if (WorldDirector.Plan == null) return Enumerable.Empty<IntroRule>();
				return WorldDirector.Plan.Rules.Where(r => !WorldDirector.Done.Contains(r.Id) && !StoryChain.Brought.Contains(r.Id) &&
					!IslandWorldState.Islands.Any(e => e.Rule.Equals(r.Id, StringComparison.OrdinalIgnoreCase))).ToList();
			}
			if (WorldCopy.HostLines == null) return Enumerable.Empty<IntroRule>();
			var done = new HashSet<string>((WorldCopy.HostValue("done") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
			return WorldCopy.HostLines.Where(l => l.StartsWith("@planrule=")).Select(l => IntroRule.Parse(l.Substring(10)))
				.Where(r => r != null && !done.Contains(r.Id) && !IslandWorldState.Islands.Any(e => e.Rule.Equals(r.Id, StringComparison.OrdinalIgnoreCase))).ToList();
		}

		/// <summary>Every frame from the mod: the host counts once a second and tells everyone when the numbers change.</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextTick) return;
			nextTick = Time.unscaledTime + TickInterval;
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor()) { if (lastSent != null || fromHost != null) Reset(); return; }
			// (counting reads the islands' settings - files for islands far away: only when something it depends on changed,
			// and every half minute for island files changed on the disk)
			string sig = Signature();
			if (sig == lastSignature && Time.unscaledTime < nextFullCount) return;
			lastSignature = sig;
			nextFullCount = Time.unscaledTime + FullCountSeconds;
			string now = Raft_Network.IsHost || fromHost == null ? Compute().Encode() : null;
			if (!Raft_Network.IsHost)
			{
				// (until the host's count comes, the journal shows this player's own)
				if (now != null && now != lastSent) { lastSent = now; Raise(); }
				return;
			}
			if (now == lastSent) return;
			lastSent = now;
			IslandNetwork.SendToEveryone(Message());
			Debug.Log("[CUSTOM ISLANDS] Questline: " + now + " (" + Totals.Decode(now).Describe() + ")");
			Raise();
		}

		/// <summary>The host's numbers, for everyone (and a player who joins).</summary>
		public static IslandNetMessage Message()
		{
			return new IslandNetMessage { Kind = IslandNetMessage.QuestLine, Data = Compute().Encode() };
		}

		/// <summary>A player: the host's numbers.</summary>
		public static void OnMessage(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost) return;
			Totals t = Totals.Decode(msg.Data);
			if (t == null) return;
			bool changed = fromHost == null || fromHost.Encode() != t.Encode();
			fromHost = t;
			if (changed)
			{
				Debug.Log("[CUSTOM ISLANDS] Questline from the host: " + t.Encode() + " (" + t.Describe() + ")");
				Raise();
			}
		}

		/// <summary>The host and each quest counted (the QuestLine command, tests).</summary>
		public static string Report()
		{
			Totals t = Shown;
			var lines = new List<string> { "Questline: " + t.Describe() + " (" + t.StepsDone + " of " + t.Steps + " steps; " + t.Encode() + ")" +
				(Raft_Network.IsHost ? "" : HasHostCount ? " - the host's count" : " - this player's own count (the host's hasn't come)") };
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				IslandQuest q = QuestTracker.QuestOf(e);
				if (q.Exists) lines.Add("  here: " + e.HostName + " '" + q.ShownTitle + "' " + Mathf.Min(QuestTracker.StepOf(e), q.Steps.Count) + "/" + q.Steps.Count);
			}
			foreach (IntroRule r in PlanRulesToCome())
				lines.Add("  to come (plan rule " + r.Id + "): " + r.DescribeWhat() + (r.What == "island" && IslandCache.QuestOf(r.WhatArg.Trim()).Exists ? " - has a quest" : r.What == "island" ? "" : " - counts once it is here"));
			return string.Join("\n", lines.ToArray());
		}
	}
}
