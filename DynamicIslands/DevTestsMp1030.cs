using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Multiplayer review 10-30, one machine (the host, messages caught by IslandNetwork.Loopback): the host's corrections of a
	/// refused "used" / harvest, a Resync's list putting a known island's state right, a list without names, a player's event
	/// on an island the host hasn't loaded, a daily reward that came after the day turned, a ping's sender, the join size.
	/// </summary>
	public static partial class DevTests
	{
		const int Mp1030Id = 987650301;

		[ConsoleCommand(name: "CIMp1030", docs: "Dev, in game (host, one machine): review 10-30 multiplayer fixes - the host's answer to a refused used/harvest (the key as the host has it), a full list re-applying a known unloaded island's state and label, a list without names (left out, no error), a player's event on an island not loaded on the host (their \"otherwise\"), a claim still granted near, the day before's daily reward credited once, a ping's sender as the host saw it, the join size estimate")]
		public static void Mp1030Command(string[] args) { StartTest(Mp1030Routine()); }

		static IEnumerator Mp1030Routine()
		{
			yield return EnsureAlive();
			if (!Raft_Network.IsHost) { Fail("CIMp1030: run it on the host"); yield break; }
			Vector3? raftAt = CustomIslandSpawner.RaftPosition;
			if (!raftAt.HasValue) { Fail("CIMp1030: no raft"); yield break; }
			bool ok = true;
			var sent = new List<IslandNetMessage>();
			var player = new Network_UserId(4242UL);
			IslandWorldState.RemoveIds(new[] { Mp1030Id }, false);
			// (an island far off: not loaded, nothing streams it in)
			IslandWorldState.Entry e = IslandWorldState.AddRemote(Mp1030Id, "cimp1030", "", raftAt.Value + new Vector3(40000f, 0f, 40000f));
			var nt = Traverse.Create(typeof(IslandNetwork));
			bool worldWas = nt.Field("worldReceived").GetValue<bool>(), syncedWas = nt.Field("synced").GetValue<bool>();
			DailyQuest.Task dailyWas = DailyQuest.Current;
			IslandNetwork.Loopback = x => sent.Add(x);
			try
			{
				// #1: a refused "used" - the key as the host has it, to that player only
				IslandNetwork.SendUsedCorrection(player, Mp1030Id, 0x123);
				Check(ref ok, sent.Count == 1 && sent[0].Kind == IslandNetMessage.ObjectUsed && sent[0].Index == 0x123 && sent[0].Count == -1 && sent[0].Ids[0] == Mp1030Id, "a refused used of a key the host has unused: the player is told it is available (day -1)");
				sent.Clear();
				e.State[0x123] = new ObjectState { Active = false, Yield = -1, Day = 3 };
				IslandNetwork.SendUsedCorrection(player, Mp1030Id, 0x123);
				Check(ref ok, sent.Count == 0, "... used on the host too: nothing sent");
				e.State.Remove(0x123);

				// #1: a refused harvest - whole, or the host's state; nothing for a creature key
				sent.Clear();
				IslandNetwork.SendHarvestCorrection(player, Mp1030Id, 5);
				Check(ref ok, sent.Count == 1 && sent[0].Kind == IslandNetMessage.ObjectUsed && sent[0].Index == 5 && sent[0].Count == -1, "a refused harvest of a whole tree: the player is told it is whole");
				sent.Clear();
				e.State[5] = new ObjectState { Active = true, Yield = 2, Day = 7 };
				IslandNetwork.SendHarvestCorrection(player, Mp1030Id, 5);
				Check(ref ok, sent.Count == 1 && sent[0].Kind == IslandNetMessage.ObjectHarvest && sent[0].Index == 5 && sent[0].Count == 7 && sent[0].Ids.Length == 3 && sent[0].Ids[1] == 1 && sent[0].Ids[2] == 2, "... a harvested one: the host's state (active, yield 2, day 7)");
				sent.Clear();
				IslandNetwork.SendHarvestCorrection(player, Mp1030Id, CreatureSpawner.StateKeyBase + 1);
				Check(ref ok, sent.Count == 0, "... a creature's key: nothing");
				e.State.Remove(5);

				// #3: a full list (Resync) puts a known island's state and label right while it isn't loaded here
				nt.Field("worldReceived").SetValue(true);
				e.State[0x200] = new ObjectState { Active = false, Yield = -1, Day = 4 };
				e.Label = "old";
				var hostState = new Dictionary<int, ObjectState> { { 0x201, new ObjectState { Active = false, Yield = -1, Day = 9 } } };
				int[] ids = IslandWorldState.Islands.Select(x => x.Id).ToArray();
				int at = Array.IndexOf(ids, Mp1030Id);
				var states = new string[ids.Length]; var labels = new string[ids.Length];
				states[at] = IslandObjectState.Encode(hostState); labels[at] = "host label";
				// (no names or places: every island is known, none moves; one unknown id is left out - #6)
				int count = IslandWorldState.Islands.Count;
				var list = new IslandNetMessage { Kind = IslandNetMessage.Islands, FullList = true, Ids = ids.Concat(new[] { Mp1030Id + 1 }).ToArray(), States = states.Concat(new string[1]).ToArray(), Labels = labels.Concat(new string[1]).ToArray() };
				string threw = null;
				try { nt.Method("ReceiveIslands", new[] { typeof(IslandNetMessage) }).GetValue(list); }
				catch (Exception ex) { threw = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message; }
				Check(ref ok, threw == null, "a list without names: no error" + (threw != null ? " (" + threw + ")" : ""));
				Check(ref ok, IslandWorldState.Islands.Count == count && !IslandWorldState.Islands.Any(x => x.Id == Mp1030Id + 1), "... the island without a name is left out, the others kept (" + IslandWorldState.Islands.Count + " of " + count + ")");
				Check(ref ok, IslandWorldState.Contains(e) && !e.State.ContainsKey(0x200) && e.State.ContainsKey(0x201) && e.State[0x201].Day == 9, "a known island not loaded here: the host's state from the full list");
				Check(ref ok, e.Label == "host label", "... and its label");

				// #2: a player's event on an island the host hasn't loaded: one who waits gets the "otherwise", nothing runs
				sent.Clear();
				Behaviours.OnEventMessage(Mp1030Id, 3, "use", false, player, true);
				Check(ref ok, sent.Count == 1 && sent[0].Kind == IslandNetMessage.EventFired && sent[0].Name == "use!" && sent[0].Index == 3 && sent[0].Count == 1, "an event on an island not loaded on the host: the waiting player gets its \"otherwise\" (" + string.Join(",", sent.Select(x => x.Kind + ":" + x.Name).ToArray()) + ")");
				sent.Clear();
				Behaviours.OnEventMessage(Mp1030Id, 3, "use", false, player, false);
				Check(ref ok, sent.Count == 0, "... one who doesn't wait: nothing sent");

				// #1: a claim from a player the host lets mark it is still granted
				IslandNetMessage answer = null;
				Claims.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Claim, Ids = new[] { Mp1030Id, 77 }, Index = 0x300 }, 4242UL, x => answer = x);
				Check(ref ok, answer != null && answer.Count == 1, "a claim near the island: still granted");

				// #5: the day before's reward, its "got" after the day turned: credited once
				DailyQuest.Current = new DailyQuest.Task { Day = 5, Kind = DailyQuest.Gather, Target = "Plank", Need = 1, Have = 1, State = "done" };
				DailyQuest.Start(6);
				int xpBefore = PlayerLevels.RecordOf(4242UL) != null ? PlayerLevels.RecordOf(4242UL).Xp : 0;
				DailyQuest.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.DailyQuest, Name = "got", Index = 5 }, player);
				DailyQuest.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.DailyQuest, Name = "got", Index = 5 }, player);
				var prev = Traverse.Create(typeof(DailyQuest)).Field("previous").GetValue<DailyQuest.Task>();
				int xpAfter = PlayerLevels.RecordOf(4242UL) != null ? PlayerLevels.RecordOf(4242UL).Xp : 0;
				Check(ref ok, prev != null && prev.Day == 5 && prev.Rewarded.Contains(4242UL) && !DailyQuest.Current.Rewarded.Contains(4242UL), "the day before's \"got\": that day's reward is marked, not today's");
				Check(ref ok, !PlayerLevels.On || xpAfter - xpBefore == DailyQuest.Xp, "... its EXP credited once (" + (xpAfter - xpBefore) + ")");

				// cosmetic: a ping's sender is who the host got it from
				sent.Clear();
				IslandNetMessage ping = Minimap.MessageFor(raftAt.Value + new Vector3(3f, 0f, 3f), 777UL);
				ping.Data = "777";
				Minimap.OnMessage(ping, 12345UL);
				Check(ref ok, sent.Any(x => x.Kind == IslandNetMessage.Ping && x.Data == "12345"), "a ping that names another player: passed on as from its sender");

				// #4: the join size estimate
				long size = IslandNetwork.SizeOf(new IslandNetMessage { Data = new string('x', 4096), Names = new[] { "abc" } });
				Check(ref ok, size >= 4096 + 3 && size < 8192, "the join size estimate counts the text (" + size + ")");
			}
			finally
			{
				IslandNetwork.Loopback = null;
				nt.Field("worldReceived").SetValue(worldWas);
				nt.Field("synced").SetValue(syncedWas);
				DailyQuest.Current = dailyWas;
				Traverse.Create(typeof(DailyQuest)).Field("previous").SetValue(null);
				IslandWorldState.RemoveIds(new[] { Mp1030Id, Mp1030Id + 1 }, false);
			}
			if (ok) Log("PASS: mp review 10-30"); else Fail("mp review 10-30");
		}
	}
}
