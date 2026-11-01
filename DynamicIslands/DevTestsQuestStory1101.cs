using System;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Quest deep dive 2026-11-01: main story islands on the Receiver.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIQsStoryTuned", docs: "Dev, world (host, 'CI ...'): questdive 11-01 - a main story island far behind comes back whenever the Receiver is set to its frequency (left tuned, step done, finished, past MaxReturns; no return counted; once per 20 s), a finished side Receiver island doesn't, and a story island whose step is done keeps its dot until its quest is")]
		public static void QsStoryTunedCommand()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("qs story tuned: host, in a world"); return; }
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("qs story tuned: only in a test world 'CI ...'"); return; }
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (!raft.HasValue) { Fail("qs story tuned: no raft position"); return; }
			bool ok = true;
			List<string> linesBefore = StoryChain.WriteLines().ToList();
			string planBefore = WorldDirector.PlanName;
			var doneBefore = WorldDirector.Done.ToList();
			const string storyName = "ciqsstorygone", sideName = "ciqssidegone";
			IslandWorldState.Entry story = null, side = null;
			try
			{
				StoryChain.FromPlan(WorldPlan.Parse("CI qs story tuned",
					"story = on\n" +
					"rule = s | island:" + storyName + " | start | receiver:400 | | Story | first | step:1\n" +
					"rule = o | island:" + sideName + " | start | receiver:400 | | Side | | visit\n"));
				int typeS = StoryChain.TypeOfRule("s"), typeO = StoryChain.TypeOfRule("o");
				Check(ref ok, typeS >= StoryChain.ModTypeBase && typeO >= StoryChain.ModTypeBase && typeS != typeO, "both rules have their own frequency (" + typeS + ", " + typeO + ")");
				Check(ref ok, StoryChain.RuleOf("s") != null && StoryChain.RuleOf("s").MainStory && !StoryChain.RuleOf("o").MainStory, "'s' is main story, 'o' a side quest");
				Check(ref ok, StoryChain.Steps.Contains(StoryChain.RuleKey("s"), StringComparer.OrdinalIgnoreCase), "'s' is a step of the chain");
				foreach (string id in new[] { "s", "o" }) { StoryChain.Fired.Add(id); StoryChain.Brought.Add(id); StoryChain.ForgetTunes(id); }
				Vector3 at = raft.Value + new Vector3(6000f, 0f, 0f);
				story = new IslandWorldState.Entry { Id = IslandNetwork.NewId(), Name = storyName, HostName = storyName, Rule = "s", Label = "", Position = at };
				side = new IslandWorldState.Entry { Id = IslandNetwork.NewId(), Name = sideName, HostName = sideName, Rule = "o", Label = "", Position = at + new Vector3(0f, 0f, 2000f) };
				IslandWorldState.Islands.Add(story);
				IslandWorldState.Islands.Add(side);

				// (1) the dot: its step done before its quest, the quest still open - needed however far; finished - not by StillOpen
				StoryChain.Done.Add(StoryChain.RuleKey("s"));
				Check(ref ok, IslandRadar.StillOpen(story), "step done, quest open: still open (its Receiver dot stays however far)");
				story.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Day = WorldDirector.Today };
				Check(ref ok, !IslandRadar.StillOpen(story), "step done, finished: not open any more");

				// (2) its step done, finished, the Receiver left tuned (no fresh tune): it comes back, no return counted
				Func<IslandWorldState.Entry, Vector3, bool> moved = (e, from) => (e.Position - from).sqrMagnitude > 1f;
				StoryChain.TuneIsFresh("s", Time.unscaledTime, Time.frameCount); // (the Receiver was tuned already)
				StoryChain.OnTuned(typeS);
				if (!moved(story, at)) Log("qs story tuned: no free spot ahead of the raft - the story return isn't checked");
				else
				{
					Check(ref ok, ReturningIslands.Returns(story) == 0, "finished, step done, Receiver left tuned: it comes back, no return counted (" + ReturningIslands.Returns(story) + ")");
					// (3) left far behind again within 20 s: not pulled back every frame
					story.Position = at;
					StoryChain.OnTuned(typeS);
					Check(ref ok, !moved(story, at), "again within 20 s: it stays (once per 20 s)");
					// (4) past MaxReturns: still comes back, the count unchanged
					story.State[ReturningIslands.ReturnsKey] = new ObjectState { Active = true, Yield = ReturningIslands.MaxReturns, Day = WorldDirector.Today };
					StoryChain.ForgetTunes("s");
					StoryChain.TuneIsFresh("s", Time.unscaledTime, Time.frameCount);
					StoryChain.OnTuned(typeS);
					Check(ref ok, moved(story, at) && ReturningIslands.Returns(story) == ReturningIslands.MaxReturns, "past its returns: it still comes back, the count unchanged (" + ReturningIslands.Returns(story) + ")");
				}
				story.Position = at;

				// (5) a side quest Receiver island, finished: a fresh tune doesn't bring it
				Vector3 sideAt = side.Position;
				side.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Day = WorldDirector.Today };
				StoryChain.ForgetTunes("o");
				StoryChain.OnTuned(typeO);
				Check(ref ok, !moved(side, sideAt) && ReturningIslands.Returns(side) == 0, "a finished side quest island: a new tune leaves it where it is");
				// (6) unfinished, the Receiver left tuned (no fresh tune): it stays too - only a new tune brings it (CIMgTunedFar)
				side.State.Remove(WorldDirector.VisitKey);
				StoryChain.ForgetTunes("o");
				StoryChain.TuneIsFresh("o", Time.unscaledTime, Time.frameCount);
				StoryChain.OnTuned(typeO);
				Check(ref ok, !moved(side, sideAt), "an unfinished side quest island, the Receiver left tuned: it stays");
			}
			catch (Exception e) { ok = false; Fail("qs story tuned: " + e); }
			finally
			{
				if (story != null) IslandWorldState.Islands.Remove(story);
				if (side != null) IslandWorldState.Islands.Remove(side);
				StoryChain.ForgetTunes("s");
				StoryChain.ForgetTunes("o");
				StoryChain.Reset();
				foreach (string l in linesBefore) { int eq = l.IndexOf('='); StoryChain.ReadLine(l.Substring(1, eq - 1), l.Substring(eq + 1)); }
				WorldDirector.Done.Clear();
				foreach (string d in doneBefore) WorldDirector.Done.Add(d);
				if (!WorldDirector.SetPlan(planBefore, false)) WorldDirector.SetPlan(WorldPlan.RandomName, false);
				StoryChain.OnWorldRead();
				IslandWorldState.Save();
			}
			if (ok) Log("PASS: qs story tuned"); else Fail("qs story tuned");
		}
	}
}
