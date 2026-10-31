using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Host, once per world load (before the chain rebuilds): puts right what builds before 2026-10-31 left in a world file
	/// when a story or Receiver island went missing (player 10-30: "the missing story island"):
	///  - a rule whose island was brought and is gone from the list, with a copy of that island spawned by hand (F10
	///    SpawnIsland, rule-less) in the world: the copy becomes the rule's island (its quest, as played, counts for the
	///    step, the journal and the notebook). Nothing is made, nothing is given.
	///  - such a rule with no island of that name at all (removed, or taken out after it failed to be made, which never told
	///    the chain): it is "not brought" again, so tuning the Receiver (or the chain) brings it once more - unless its step
	///    is done or the island was finished (its rewards were had), so nothing comes twice.
	/// It changes only the chain's sets and an entry's Rule; the world is saved as usual. Run again it finds nothing.
	/// </summary>
	public static class StoryUpgrade
	{
		/// <summary>What the last run did, a line per rule (tests read it).</summary>
		public static readonly List<string> Report = new List<string>();

		public static int Run()
		{
			Report.Clear();
			if (!Raft_Network.IsHost) return 0;
			int fixes = 0;
			foreach (IntroRule r in StoryChain.Rules.ToList())
			{
				try { if (Repair(r)) fixes++; }
				catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] [story upgrade] " + r.Id + ": " + ex.Message); }
			}
			foreach (string line in Report) Debug.Log("[CUSTOM ISLANDS] [story upgrade] " + line);
			return fixes;
		}

		static bool Repair(IntroRule r)
		{
			if (!StoryChain.Fired.Contains(r.Id)) return false;
			if (IslandWorldState.Islands.Any(e => r.Id.Equals(e.Rule, StringComparison.OrdinalIgnoreCase))) return false;
			string name = r.What == "island" ? (r.WhatArg ?? "").Trim() : "";
			bool stepDone = r.InStory && StoryChain.Done.Contains(StoryChain.RuleKey(r.Id));

			// 1. A hand-spawned copy of its island: that copy is the story's now
			List<IslandWorldState.Entry> copies = name.Length == 0 ? new List<IslandWorldState.Entry>() :
				IslandWorldState.Islands.Where(e => string.IsNullOrEmpty(e.Rule) && !e.Excluded && !e.Failed && !e.WaitingForFile &&
					name.Equals(e.HostName, StringComparison.OrdinalIgnoreCase) && !WorldRandomizer.IsExtras(e)).ToList();
			// (a rule not brought - re-armed after its island was removed, or not tuned yet - takes only a copy the players
			// played: else its island comes as usual when its moment comes, and an unvisited copy is left as it is)
			if (!StoryChain.Brought.Contains(r.Id))
				copies = copies.Where(e => e.State.ContainsKey(WorldDirector.VisitKey) || QuestTracker.StepOf(e) > 0).ToList();
			if (copies.Count > 0)
			{
				// (the one played furthest: finished first, then the most steps, then the last spawned)
				IslandWorldState.Entry pick = copies.Select((e, i) => new { e, i })
					.OrderByDescending(x => ReturningIslands.Finished(x.e) ? 1 : 0).ThenByDescending(x => QuestTracker.StepOf(x.e)).ThenByDescending(x => x.i)
					.First().e;
				pick.Rule = r.Id;
				StoryChain.Brought.Add(r.Id);
				Report.Add("adopt " + r.Id + " -> " + pick.HostName + " #" + pick.Id);
				return true;
			}

			// 2. No island of it at all: brought again when its moment comes - only if nothing of it was had
			if (!StoryChain.Brought.Contains(r.Id) || stepDone) return false;
			if (!r.InStory && name.Length > 0 && (QuestRewards.Rewarded(name) || QuestRewards.Owed(name) ||
				StoryBook.Items.Any(h => h.From.Keys.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase) || k.StartsWith(name + "|", StringComparison.OrdinalIgnoreCase)))))
			{
				Report.Add("keep " + r.Id + " (its island was had)");
				return false;
			}
			StoryChain.Brought.Remove(r.Id);
			Report.Add("rearm " + r.Id);
			return true;
		}
	}
}
