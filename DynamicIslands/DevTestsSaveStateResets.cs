using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>Review 10-30: a quest reward is tied to the Raft save it was given after. Given, then the world reloaded
		/// from the same save (a crash, a quit without saving): the quest done again pays again. Given before a later Raft
		/// save: refused when that save loads, and the record is still "rewarded" (no second pay from the Tick fallback).
		/// An old .rewards line with no stamp is refused as before. Also: the world-load resets clear the set counts, the
		/// barrels given and the quest count, and the world file keeps the feature lines when no option is on.</summary>
		[ConsoleCommand(name: "CISaveStateResets", docs: "Dev, in game (host): review 10-30 - quest rewards follow the Raft save the world loaded from; world-load resets clear statics; feature lines written with no options")]
		public static void SaveStateResetsCommand(string[] args) { StartTest(SaveStateResetsRoutine()); }

		static IEnumerator SaveStateResetsRoutine()
		{
			if (RAPI.GetLocalPlayer() == null || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			long loaded = QuestRewards.LoadedSave, last = QuestRewards.LastSave;
			const string key = "cisavestate1030";
			int paid = 0;
			Action give = () => paid++;
			QuestRewards.ForgetIsland(key);
			try
			{
				QuestRewards.OnWorldLoaded(1000L);
				Check(ref ok, QuestRewards.OnCompleted(key, true, give) && paid == 1, "first completion pays");
				Check(ref ok, !QuestRewards.OnCompleted(key, true, give) && paid == 1, "a second completion in the same session is refused");
				QuestRewards.Forget();
				QuestRewards.OnWorldLoaded(1000L);
				Check(ref ok, QuestRewards.Rewarded(key), "the record is kept in the file");
				Check(ref ok, QuestRewards.OnCompleted(key, true, give) && paid == 2, "reloaded from the save before the reward (crash): pays again");
				QuestRewards.AfterRaftSave(2000L);
				QuestRewards.OnWorldLoaded(2000L);
				Check(ref ok, !QuestRewards.OnCompleted(key, true, give) && paid == 2, "loaded from a save made after the reward: refused");
				QuestRewards.OnWorldLoaded(1000L);
				Check(ref ok, QuestRewards.Rewarded(key) && !QuestRewards.Owed(key), "older save loaded: still rewarded (no Tick double pay)");
				Check(ref ok, QuestRewards.OnCompleted(key, true, give) && paid == 3, "older save loaded, quest done again: pays again");
				Check(ref ok, !QuestRewards.OnCompleted(key, true, give) && paid == 3, "and only once");

				// (an old file's line, no stamp: refused as before)
				string file = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), QuestRewards.WhereKept);
				List<string> lines = File.Exists(file) ? File.ReadAllLines(file).Where(l => !l.EndsWith(" " + key, StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
				lines.Add("rewarded " + key);
				File.WriteAllLines(file, lines.ToArray());
				QuestRewards.Forget();
				QuestRewards.OnWorldLoaded(1000L);
				Check(ref ok, QuestRewards.Rewarded(key) && !QuestRewards.OnCompleted(key, true, give) && paid == 3, "an old record with no stamp is refused");

				// (a client: the loaded save is the first host copy's; a host ignores host copies)
				QuestRewards.OnWorldLoaded(1000L);
				QuestRewards.OnHostCopy(SaveAndLoad.WorldGuid, 5000L, 4000L);
				Check(ref ok, QuestRewards.LoadedSave == 1000L, "the host ignores host copies");
			}
			finally
			{
				QuestRewards.ForgetIsland(key);
				QuestRewards.OnWorldLoaded(loaded);
				QuestRewards.LastSave = last;
			}

			Collectibles.Reset();
			Check(ref ok, Collectibles.SeenCount == 0, "Collectibles.Reset clears the counts seen");
			GoldenBarrels.Reset();
			Check(ref ok, GoldenBarrels.GivenCount == 0 && !GoldenBarrels.TestKind.HasValue, "GoldenBarrels.Reset clears the barrels given");
			QuestCount.Reset();

			var options = new HashSet<string>(WorldOptions.Current);
			int seed = WorldOptions.Seed;
			try
			{
				WorldOptions.Current.Clear();
				WorldOptions.Seed = 0;
				var written = new HashSet<string>(WorldOptions.WriteLines());
				IEnumerable<string> features = PrivateStorage.WriteLines().Concat(GhostRafts.WriteLines()).Concat(DailyQuest.WriteLines()).Concat(RogueShark.WriteLines())
					.Concat(TraderRaft.WriteLines()).Concat(UpgradeTable.WriteLines()).Concat(StormSeas.WriteLines());
				List<string> missing = features.Where(l => !written.Contains(l)).ToList();
				Check(ref ok, missing.Count == 0, "no options, seed 0: the feature lines are still written" + (missing.Count == 0 ? "" : " (missing " + missing.First() + ")"));
				Check(ref ok, !written.Any(l => l.StartsWith("@options")), "no options, seed 0: no @options lines");
			}
			finally
			{
				WorldOptions.Current.Clear();
				WorldOptions.Current.UnionWith(options);
				WorldOptions.Seed = seed;
			}
			yield return null;
			if (ok) Log("PASS: save state resets");
			else Fail("save state resets");
		}
	}
}
