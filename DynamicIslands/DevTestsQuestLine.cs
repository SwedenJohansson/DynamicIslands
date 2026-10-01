using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The questline in the journal (QuestLine): how much of the world's quests is done. TEST_CATALOGUE "QL" rows,
	/// TEST_PROTOCOL 5k; two players: tools\mpquestline.ps1.
	/// </summary>
	public static partial class DevTests
	{
		const string QuestLinePlan = "ci questline";
		static readonly string[] QuestLineIslands = { "ciql1", "ciql2", "ciql3" };
		static readonly string[] QuestLineRules = { "qla", "qlb", "qlc" };

		[ConsoleCommand(name: "CIQuestLineUnit", docs: "Dev, anywhere: the questline's numbers - per cent (never 100 before the last step), the journal's words, what the host sends and a player reads")]
		public static void QuestLineUnit()
		{
			bool ok = true;
			Func<int, int, int, int, QuestLine.Totals> T = (sd, s, qd, q) => new QuestLine.Totals { StepsDone = sd, Steps = s, QuestsDone = qd, Quests = q };
			Check(ref ok, T(0, 0, 0, 0).Percent == 0 && T(0, 0, 0, 0).Describe() == "No quests in this world yet", "no quests: 0 % and \"" + T(0, 0, 0, 0).Describe() + "\"");
			Check(ref ok, T(1, 3, 0, 1).Percent == 33, "1 of 3 steps: 33 % (" + T(1, 3, 0, 1).Percent + ")");
			Check(ref ok, T(2, 3, 0, 1).Percent == 66, "2 of 3 steps: 66 %, rounded down (" + T(2, 3, 0, 1).Percent + ")");
			Check(ref ok, T(199, 200, 9, 10).Percent == 99, "199 of 200 steps: 99 %, not 100 (" + T(199, 200, 9, 10).Percent + ")");
			Check(ref ok, T(7, 7, 4, 4).Percent == 100, "every step: 100 %");
			Check(ref ok, T(1, 10, 0, 1).Percent == 10, "1 of 10: 10 %");
			Check(ref ok, T(3, 7, 2, 4).Describe() == "42% of the questline done · 2 of 4 quests", "the journal's words: " + T(3, 7, 2, 4).Describe());
			Check(ref ok, T(0, 2, 0, 1).Describe().EndsWith("0 of 1 quest"), "one quest: \"quest\", not \"quests\" (" + T(0, 2, 0, 1).Describe() + ")");
			QuestLine.Totals back = QuestLine.Totals.Decode(T(3, 7, 2, 4).Encode());
			Check(ref ok, back != null && back.Encode() == "3,7,2,4", "sent and read back: " + (back != null ? back.Encode() : "null"));
			Check(ref ok, QuestLine.Totals.Decode("") == null && QuestLine.Totals.Decode("1,2,3") == null && QuestLine.Totals.Decode("a,b,c,d") == null && QuestLine.Totals.Decode("1,-2,0,1") == null, "broken numbers are ignored");
			QuestLine.Totals odd = QuestLine.Totals.Decode("9,4,5,2");
			Check(ref ok, odd != null && odd.StepsDone == 4 && odd.QuestsDone == 2 && odd.Percent == 100, "more done than there are is cut to all (" + (odd != null ? odd.Encode() : "null") + ")");
			if (ok) Log("PASS: questline unit"); else Fail("questline unit");
		}

		/// <summary>The test plan's islands: ciql1 (2 steps), ciql2 (1 step; its own rule brings ciql3 when its quest is done),
		/// ciql3 (3 steps; its own rule would bring ciql2 again on day 9999 - a loop the count must not follow for ever).</summary>
		static void MakeQuestLineIslands()
		{
			IslandFile a = RuleIsland("ciql1", "Questline One");
			new IslandQuest { Title = "First", Steps = { new IslandQuest.Step { Type = "reach", Target = "camp" }, new IslandQuest.Step { Type = "read", Target = "a note" } } }.To(a.Props);
			a.Save(IslandSpawner.PathFor("ciql1"));
			IslandFile b = RuleIsland("ciql2", "Questline Two", IntroRule.Parse("r2 | island:ciql3 | quest:self | ahead:500 | The third island | Third"));
			b.Save(IslandSpawner.PathFor("ciql2"));
			IslandFile c = RuleIsland("ciql3", "Questline Three", IntroRule.Parse("r3 | island:ciql2 | day:9999 | ahead:500 | | Again"));
			new IslandQuest { Title = "Third", Steps = { new IslandQuest.Step { Type = "reach", Target = "camp" }, new IslandQuest.Step { Type = "kill", Target = "Warthog", Count = 2 }, new IslandQuest.Step { Type = "read", Target = "the end" } } }.To(c.Props);
			c.Save(IslandSpawner.PathFor("ciql3"));
			IslandCache.Forget();
		}

		static QuestLine.Totals Plus(QuestLine.Totals b, int stepsDone, int steps, int questsDone, int quests)
		{
			return new QuestLine.Totals { StepsDone = b.StepsDone + stepsDone, Steps = b.Steps + steps, QuestsDone = b.QuestsDone + questsDone, Quests = b.Quests + quests };
		}

		static IslandWorldState.Entry QuestLineEntry(string name) { return IslandWorldState.Islands.LastOrDefault(e => e.HostName.Equals(name, StringComparison.OrdinalIgnoreCase)); }

		/// <summary>The journal open: its questline row says what the count says, the bar as full as the per cent.</summary>
		static IEnumerator QuestLineJournal(Action<bool> result, QuestLine.Totals want, string label)
		{
			JournalWindow.Open();
			yield return null;
			yield return new WaitForSecondsRealtime(0.3f);
			string shown = JournalWindow.ShownQuestLine;
			float fill = JournalWindow.ShownQuestFill;
			bool ok = shown == want.Describe() && Mathf.Abs(fill - want.Percent / 100f) < 0.005f;
			Log((ok ? "  ok   " : "  FAIL ") + label + ": the journal says \"" + shown + "\", bar " + (fill * 100f).ToString("F0") + " % (expected \"" + want.Describe() + "\")");
			if (!ok) Debug.LogError("[CITEST] FAIL: " + label);
			result(ok);
		}

		[ConsoleCommand(name: "CIQuestLineWorld", docs: "Dev, in game (host): the questline in the journal - a test plan's quests counted before their islands come (by name; a random kind not), no double count when they come, steps and quests done, an island's own rules followed (a loop stops), 100 % at the end, sent to players, saved; CIQuestLineWorld keep = stop after the first step and keep the plan (mpquestline.ps1)")]
		public static void QuestLineWorld(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(QuestLineWorldRoutine(args != null && args.Length > 0 && args[0] == "keep"));
		}

		static IEnumerator QuestLineWorldRoutine(bool keep)
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true, jok = true;
			Action<bool> J = r => { jok &= r; };
			string planBefore = WorldDirector.PlanName;
			bool autoBefore = CustomIslandSpawner.Enabled;
			if (planBefore.Equals(QuestLinePlan, StringComparison.OrdinalIgnoreCase)) planBefore = WorldPlan.RandomName;
			foreach (string id in QuestLineRules) WorldDirector.Done.Remove(id);
			var doneBefore = WorldDirector.Done.ToList();
			int before = IslandWorldState.Islands.Count;

			// What the world counts without a plan (islands already here with quests): the test adds to it
			WorldDirector.SetPlan(WorldPlan.NoneName, false);
			QuestLine.Totals b = QuestLine.Compute();
			Log("Before the test plan: " + b.Encode() + " (" + b.Describe() + ")");

			MakeQuestLineIslands();
			WorldPlan.Parse(QuestLinePlan, "random = off\n" +
				"rule = qla | island:ciql1 | start | ahead:300 | The first island | One\n" +
				"rule = qlb | island:ciql2 | quest:qla | ahead:400 | The second island | Two\n" +
				"rule = qlc | type:snowy | day:9999 | ahead:600 | | Snow\n").Save();
			Check(ref ok, WorldDirector.SetPlan(QuestLinePlan, true), "the world gets the test plan");

			var sent = new List<IslandNetMessage>();
			// (kept for two players: the real player 2 must get the islands and counts, so nothing is caught here)
			if (!keep) IslandNetwork.Loopback = m => sent.Add(m);
			try
			{
				// Counted before any island came: ciql1 (2), ciql2 (1) and the ciql3 its rule brings (3); the snowy island
				// (a kind, not a named island) isn't known yet; the loop ciql3 -> ciql2 -> ciql3 is followed once
				QuestLine.Totals want = Plus(b, 0, 6, 0, 3);
				QuestLine.Totals now = QuestLine.Compute();
				Check(ref ok, now.Encode() == want.Encode(), "before the islands come: 3 quests, 6 steps to come (" + now.Encode() + ", expected " + want.Encode() + ")");
				Log(QuestLine.Report());

				WorldDirector.Evaluate();
				IslandWorldState.Entry one = QuestLineEntry("ciql1");
				Check(ref ok, one != null, "the plan brings ciql1");
				if (one == null) yield break;
				now = QuestLine.Compute();
				Check(ref ok, now.Encode() == want.Encode(), "ciql1 here: counted once, not again as still to come (" + now.Encode() + ")");
				yield return QuestLineJournal(J, want, "the journal at 0 %");

				// A step of ciql1
				QuestTracker.Set(one, 1, 0, true);
				want = Plus(b, 1, 6, 0, 3);
				now = QuestLine.Compute();
				Check(ref ok, now.Encode() == want.Encode(), "one step done: " + now.Describe());
				// (the mod counts once a second and tells everyone when it changes)
				float t0 = Time.realtimeSinceStartup;
				while (!keep && !sent.Any(m => m.Kind == IslandNetMessage.QuestLine && m.Data == want.Encode()) && Time.realtimeSinceStartup - t0 < 5f) yield return new WaitForSecondsRealtime(0.25f);
				if (!keep) Check(ref ok, sent.Any(m => m.Kind == IslandNetMessage.QuestLine && m.Data == want.Encode()), "players are sent the new count (" + want.Encode() + ")");
				yield return QuestLineJournal(J, want, "the journal after one step");
				Screenshot(new[] { "questline_journal" });
				yield return new WaitForSecondsRealtime(0.6f);
				JournalWindow.Close();
				if (keep)
				{
					IslandWorldState.Save();
					if (ok && jok) Log("PASS: questline world (kept for two players)"); else Fail("questline world (kept for two players)");
					yield break;
				}

				// ciql1 done: its quest brings ciql2
				QuestTracker.Set(one, 2, 0, true);
				WorldDirector.Evaluate();
				IslandWorldState.Entry two = QuestLineEntry("ciql2");
				want = Plus(b, 2, 6, 1, 3);
				now = QuestLine.Compute();
				Check(ref ok, two != null && now.Encode() == want.Encode(), "ciql1 done, ciql2 came: " + now.Describe() + " (" + now.Encode() + ", expected " + want.Encode() + ")");
				if (two == null) yield break;

				// ciql2 done: its own rule brings ciql3, whose own rule would bring ciql2 once more (counted: it would come)
				QuestTracker.Set(two, 1, 0, true);
				WorldDirector.Evaluate();
				IslandWorldState.Entry three = QuestLineEntry("ciql3");
				want = Plus(b, 3, 7, 2, 4);
				now = QuestLine.Compute();
				Check(ref ok, three != null && now.Encode() == want.Encode(), "ciql2 done, ciql3 came; its rule's island still to come: " + now.Describe() + " (" + now.Encode() + ", expected " + want.Encode() + ")");
				if (three == null) yield break;
				Log(QuestLine.Report());

				// ciql3's steps one by one; then its rule counts as fired: everything done
				QuestTracker.Set(three, 2, 0, true);
				now = QuestLine.Compute();
				Check(ref ok, now.Encode() == Plus(b, 5, 7, 2, 4).Encode(), "two steps of ciql3: " + now.Describe());
				QuestTracker.Set(three, 3, 0, true);
				three.State[WorldDirector.RuleKeyBase] = new ObjectState { Active = false, Day = 0 };
				want = Plus(b, 6, 6, 3, 3);
				now = QuestLine.Compute();
				Check(ref ok, now.Encode() == want.Encode(), "every quest done: " + now.Describe());
				if (b.Quests == 0) Check(ref ok, now.Percent == 100 && now.Describe().StartsWith("100%"), "100 % at the end");
				yield return QuestLineJournal(J, want, "the journal at the end");
				Screenshot(new[] { "questline_done" });
				yield return new WaitForSecondsRealtime(0.6f);
				JournalWindow.Close();

				// Saved with the world (the quest steps in the islands' state, the plan's done rules) and read back
				IslandWorldState.Save();
				string worldFile = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), SaveAndLoad.WorldGuid + ".txt");
				string[] lines = File.Exists(worldFile) ? File.ReadAllLines(worldFile) : new string[0];
				WorldDirector.Reset();
				foreach (string l in lines.Where(l => l.StartsWith("@")))
				{
					int eq = l.IndexOf('=');
					if (eq > 1) WorldDirector.ReadLine(l.Substring(1, eq - 1), l.Substring(eq + 1));
				}
				WorldDirector.SetPlan(QuestLinePlan, false);
				now = QuestLine.Compute();
				Check(ref ok, now.Encode() == want.Encode(), "the same after the world file is read back (" + now.Encode() + ")");
			}
			finally { IslandNetwork.Loopback = null; JournalWindow.Close(); }

			// Clean up: the world's own plan and state come back
			yield return new WaitForSeconds(1f);
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(before).Select(e => e.Id).ToList(), true);
			foreach (string n in QuestLineIslands) if (File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n));
			File.Delete(WorldPlan.PathFor(QuestLinePlan));
			IslandCache.Forget();
			WorldDirector.SetPlan(planBefore, false);
			CustomIslandSpawner.Enabled = autoBefore;
			WorldDirector.Done.Clear();
			foreach (string d in doneBefore) WorldDirector.Done.Add(d);
			IslandWorldState.Save();
			Check(ref ok, jok, "the journal showed every count");
			if (ok) Log("PASS: questline world"); else Fail("questline world");
		}

		[ConsoleCommand(name: "CIQuestLineStep", docs: "Dev, in game (either player): CIQuestLineStep <island> <step> - moves that island's quest to the step (a player's change goes through the host, like a real one)")]
		public static void QuestLineStep(string[] args)
		{
			int step;
			IslandWorldState.Entry e = args != null && args.Length >= 2 ? QuestLineEntry(args[0]) : null;
			if (e == null || !int.TryParse(args[1], out step)) { Fail("quest line step: no island '" + (args != null && args.Length > 0 ? args[0] : "") + "' here, or no step"); return; }
			QuestTracker.Set(e, step, 0, true);
			if (Raft_Network.IsHost) WorldDirector.Evaluate();
			Log("PASS: quest line step (" + e.HostName + " at step " + QuestTracker.StepOf(e) + ")");
		}

		[ConsoleCommand(name: "CIQuestLineCheck", docs: "Dev, in game (either player): logs QUESTLINE <steps done,steps,quests done,quests> <host|player 2: from the host or own>, opens the journal and checks its row; CIQuestLineCheck <numbers> also checks them")]
		public static void QuestLineCheck(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(QuestLineCheckRoutine(args != null && args.Length > 0 ? args[0] : null));
		}

		static IEnumerator QuestLineCheckRoutine(string want)
		{
			bool ok = true;
			QuestLine.Totals shown = QuestLine.Shown;
			Log("QUESTLINE " + shown.Encode() + " " + (Raft_Network.IsHost ? "host" : QuestLine.HasHostCount ? "from-host" : "own"));
			Log(QuestLine.Report());
			if (!Raft_Network.IsHost) Check(ref ok, QuestLine.HasHostCount, "a player shows the host's count");
			if (want != null) Check(ref ok, shown.Encode() == want, "the count is " + shown.Encode() + " (expected " + want + ")");
			bool jok = true;
			yield return QuestLineJournal(r => jok = r, shown, "the journal");
			JournalWindow.Close();
			Check(ref ok, jok, "the journal's questline row");
			if (ok) Log("PASS: questline check"); else Fail("questline check");
		}
	}
}
