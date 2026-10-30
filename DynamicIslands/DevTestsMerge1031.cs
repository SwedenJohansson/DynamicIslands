using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Merge review 2026-10-31: the fixes made while merging the 10-30 worktrees.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIMgTuneFresh", docs: "Dev: merge 10-31 - a Receiver tune counts as new only after a gap (Raft asks every frame), a resumed event gives its story items only when a part before the wait didn't, and an old world file's fired zones keep their story items given")]
		public static void MgTuneFreshCommand()
		{
			bool ok = true;
			const string id = "cimgtune";
			try
			{
				// (1) Raft asks every frame while the Receiver stays tuned: only the first ask is a new tune
				StoryChain.ForgetTunes(id);
				float t = 100f; int f = 1000;
				Check(ref ok, StoryChain.TuneIsFresh(id, t, f), "the first ask is a new tune");
				int fresh = 0;
				for (int i = 0; i < 600; i++) { t += 1f / 60f; f++; if (StoryChain.TuneIsFresh(id, t, f)) fresh++; }
				Check(ref ok, fresh == 0, "ten seconds of asks, one per frame: no new tune (" + fresh + ")");
				fresh = 0;
				for (int i = 0; i < 40; i++) { t += 0.5f; f++; if (StoryChain.TuneIsFresh(id, t, f)) fresh++; }
				Check(ref ok, fresh == 0, "slow frames (2 a second): no new tune (" + fresh + ")");
				t += 5f; f += 1;
				Check(ref ok, !StoryChain.TuneIsFresh(id, t, f), "one long frame (a hitch, 5 s): not a new tune");
				t += 2.5f; f += 40;
				Check(ref ok, StoryChain.TuneIsFresh(id, t, f), "tuned again after a gap (2.5 s, 40 frames): a new tune");
				StoryChain.ForgetTunes(id);
				Check(ref ok, StoryChain.TuneIsFresh(id, t + 0.01f, f + 1), "forgotten: a new tune");

				// (2) a resumed event (after its firstPart-th wait): a part before it gave story items - the key may be its own
				Func<string, List<ObjAction>> acts = text => ObjAction.ParseLines(text);
				Check(ref ok, Behaviours.StoryGivenInPartsBefore(acts("give||story:cimga\nwait||5\ngive||story:cimgb"), 1), "a story give before the wait: found");
				Check(ref ok, !Behaviours.StoryGivenInPartsBefore(acts("message||hi\nwait||5\ngive||story:cimga"), 1), "only after the wait: not found (the key is an earlier firing's)");
				Check(ref ok, !Behaviours.StoryGivenInPartsBefore(acts("give||story:cimga\nwait||5\ngive||story:cimgb"), 0), "not resumed: nothing before");
				Check(ref ok, !Behaviours.StoryGivenInPartsBefore(acts("give||Plank\nwait||5\nwait||5\ngive||story:cimga"), 2), "a give of plain items before: not a story give");
				Check(ref ok, Behaviours.StoryGivenInPartsBefore(acts("wait||5\ngive||story:cimga\nwait||5\ngive||story:cimgb"), 2) &&
					!Behaviours.StoryGivenInPartsBefore(acts("wait||5\ngive||story:cimga\nwait||5\ngive||story:cimgb"), 1), "counted by waits");

				// (3) a world file from before the story-loot keys: zones that fired keep their story items given
				Func<bool, int, ObjectState> st = (active, day) => new ObjectState { Active = active, Yield = 0, Day = day };
				var e1 = new IslandWorldState.Entry { Name = "cimgzone1", HostName = "cimgzone1" };
				e1.State[TriggerZone.KeyBase + 3] = st(false, 4);    // fired
				e1.State[TriggerZone.KeyBase + 4] = st(true, 4);     // ready again
				e1.State[ContentState.LootKeyBase + 3] = st(false, 4); // a looted crate: not a zone
				var e2 = new IslandWorldState.Entry { Name = "cimgzone2", HostName = "cimgzone2" };
				e2.State[TriggerZone.KeyBase + 5] = st(false, 2);
				e2.State[TriggerZone.StoryLootBase + 5] = st(false, 9); // (already there: kept)
				TriggerZone.ForgetStoryLootLine();
				int set = TriggerZone.MarkOldStoryLoot(new[] { e1, e2 });
				ObjectState s;
				Check(ref ok, set == 1 && e1.State.TryGetValue(TriggerZone.StoryLootBase + 3, out s) && !s.Active && s.Day == 4, "old file: the fired zone's story-loot key is set (" + set + ")");
				Check(ref ok, !e1.State.ContainsKey(TriggerZone.StoryLootBase + 4), "a zone ready again: no key");
				Check(ref ok, e1.State.Count == 4 && e2.State[TriggerZone.StoryLootBase + 5].Day == 9, "nothing else changed");
				Check(ref ok, TriggerZone.StoryLootLine == "@storyloot=1" && TriggerZone.ReadLine("storyloot", "1") && !TriggerZone.ReadLine("storylootx", "1"), "the file's line");
				var e3 = new IslandWorldState.Entry { Name = "cimgzone3", HostName = "cimgzone3" };
				e3.State[TriggerZone.KeyBase + 1] = st(false, 1);
				Check(ref ok, TriggerZone.MarkOldStoryLoot(new[] { e3 }) == 0 && !e3.State.ContainsKey(TriggerZone.StoryLootBase + 1), "a file with the line: nothing migrated");
			}
			catch (Exception e) { ok = false; Fail("mg tune fresh: " + e); }
			finally { StoryChain.ForgetTunes(id); }
			if (ok) Log("PASS: mg tune fresh"); else Fail("mg tune fresh");
		}

		[ConsoleCommand(name: "CIMgTunedFar", docs: "Dev, world (host, 'CI ...'): merge 10-31 - the Receiver staying tuned to a brought island's frequency (Raft asks every frame) doesn't pull it back and use up its returns; only a new tune does. Also: the missing-islands box replaced by another box plays on without them")]
		public static void MgTunedFarCommand() { StartTest(MgTunedFarRoutine()); }

		static IEnumerator MgTunedFarRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("mg tuned far: host, in a world"); yield break; }
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("mg tuned far: only in a test world 'CI ...'"); yield break; }
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (!raft.HasValue) { Fail("mg tuned far: no raft position"); yield break; }
			bool ok = true;
			List<string> linesBefore = StoryChain.WriteLines().ToList();
			string planBefore = WorldDirector.PlanName;
			var doneBefore = WorldDirector.Done.ToList();
			const string gone = "cimgtunedgone";
			IslandWorldState.Entry far = null;
			try
			{
				StoryChain.FromPlan(WorldPlan.Parse("CI mg tuned", "story = on\nrule = t | island:" + gone + " | start | receiver:400 | | Harbor | | visit\n"));
				int type = StoryChain.TypeOfRule("t");
				Check(ref ok, type >= StoryChain.ModTypeBase, "the rule has its own frequency (" + type + ")");
				StoryChain.Fired.Add("t");
				StoryChain.Brought.Add("t");
				Vector3 at = raft.Value + new Vector3(6000f, 0f, 0f);
				far = new IslandWorldState.Entry { Id = IslandNetwork.NewId(), Name = gone, HostName = gone, Rule = "t", Label = "", Position = at };
				IslandWorldState.Islands.Add(far);
				// The player tuned in earlier (the island came, was left behind and unloaded); the Receiver is still tuned
				StoryChain.TuneIsFresh("t", Time.unscaledTime, Time.frameCount);
				for (int i = 0; i < 150; i++)
				{
					StoryChain.OnTuned(type);
					yield return null;
				}
				Check(ref ok, ReturningIslands.Returns(far) == 0 && (far.Position - at).sqrMagnitude < 1f,
					"150 frames still tuned, the island far behind: not pulled back, no return used (returns " + ReturningIslands.Returns(far) + ")");
				// A new tune (after a gap): it comes back, one return
				StoryChain.ForgetTunes("t");
				StoryChain.OnTuned(type);
				bool moved = (far.Position - at).sqrMagnitude > 1f;
				IslandWorldState.Islands.Remove(far); // (before the spawner sees it near: there is no such file)
				if (moved) Check(ref ok, ReturningIslands.Returns(far) == 1, "a new tune: it comes back, one return (" + ReturningIslands.Returns(far) + ")");
				else Log("mg tuned far: a new tune found no free spot ahead of the raft - not checked");

				// The missing-islands box replaced by another box (InfoWindow.Open doesn't call its Closed): the world plays on
				// without them, as when it is closed (merge review 10-31)
				var lastBefore = MissingIslandsCheck.Last.ToList();
				bool withoutBefore = StoryChain.PlayingWithoutMissing;
				try
				{
					StoryChain.PlayingWithoutMissing = false;
					MissingIslandsCheck.Last.Clear();
					MissingIslandsCheck.Last.Add(new MissingIslandsCheck.Missing { Name = gone, ByPlan = true });
					MissingIslandsCheck.Show();
					Check(ref ok, MissingIslandsCheck.Asking, "the missing-islands box asks");
					InfoWindow.Open("CI merge", "Another box");
					MissingIslandsCheck.Tick();
					Check(ref ok, !MissingIslandsCheck.Asking && StoryChain.PlayingWithoutMissing, "another box in its place: no longer asking, playing on without them");
					Check(ref ok, InfoWindow.IsOpen, "the other box stays open");
					InfoWindow.Close();
					MissingIslandsCheck.Tick();
				}
				finally
				{
					if (InfoWindow.IsOpen) InfoWindow.Close();
					MissingIslandsCheck.Forget();
					MissingIslandsCheck.Last.Clear(); MissingIslandsCheck.Last.AddRange(lastBefore);
					StoryChain.PlayingWithoutMissing = withoutBefore;
				}
			}
			finally
			{
				if (far != null) IslandWorldState.Islands.Remove(far);
				StoryChain.ForgetTunes("t");
				StoryChain.Reset();
				foreach (string l in linesBefore) { int eq = l.IndexOf('='); StoryChain.ReadLine(l.Substring(1, eq - 1), l.Substring(eq + 1)); }
				WorldDirector.Done.Clear();
				foreach (string d in doneBefore) WorldDirector.Done.Add(d);
				if (!WorldDirector.SetPlan(planBefore, false)) WorldDirector.SetPlan(WorldPlan.RandomName, false);
				StoryChain.OnWorldRead();
				IslandWorldState.Save();
			}
			if (ok) Log("PASS: mg tuned far"); else Fail("mg tuned far");
		}
	}
}
