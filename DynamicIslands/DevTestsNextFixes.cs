using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Fixes after the 11-02 release (ROADMAP 3h/3i, branch next-fixes): one test per fix.</summary>
	public static partial class DevTests
	{

		[ConsoleCommand(name: "CIStandKeysWide", docs: "Dev: RK9 - trade stand keys past object 8191 are their own (no two stands share a key, none in another range), and an island edit carries them across the 8191 line")]
		public static void StandKeysWideCommand()
		{
			bool ok = true;
			string tmp = Path.Combine(DynamicIslands.assetpath, "ci_standwide.island");
			try
			{
				var seen = new HashSet<int>();
				int clash = 0, outside = 0, back = 0;
				for (int i = 0; i < 0x10000; i++)
					for (int u = 0; u < TradeStand.MaxStock; u++)
					{
						int k = TradeStand.KeyFor(i, u), bi, bu;
						if (!seen.Add(k)) clash++;
						if (!((k >= TradeStand.KeyBase && k < TradeStand.KeyBase + 0x10000) || (k >= TradeStand.WideKeyBase && k < TradeStand.WideKeyBase + 0x80000))) outside++;
						if (!TradeStand.FromKey(k, out bi, out bu) || bi != i || bu != u) back++;
					}
				Check(ref ok, clash == 0 && outside == 0 && back == 0, "65536 stands x " + TradeStand.MaxStock + " units: " + clash + " shared keys, " + outside + " outside the two ranges, " + back + " not read back");
				Check(ref ok, TradeStand.KeyFor(5, 1) == TradeStand.KeyBase + 5 * TradeStand.MaxStock + 1, "a stand below 8192 keeps its old key (saves made before stay right)");
				Check(ref ok, TradeStand.KeyFor(8192, 0) != TradeStand.KeyFor(0, 0), "stand 8192 and stand 0 differ (the old mask made them one)");

				if (!PlaceableCatalog.IsBuilt) { Check(ref ok, false, "the object catalog isn't loaded yet"); }
				else
				{
					var f1 = new IslandFile { Name = "ci_standwide", TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
					for (int i = 0; i < 8195; i++)
						f1.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = new Vector3(i % 100, 5, i / 100), EulerRotation = Vector3.zero, Scale = Vector3.one });
					f1.Save(tmp);
					IslandFile a = IslandFile.Load(tmp);
					IslandFile b = IslandFile.Load(tmp);
					b.Objects.Insert(0, new IslandObject { Name = "Loot_Chest", Position = new Vector3(-5, 5, -5), EulerRotation = Vector3.zero, Scale = Vector3.one });
					StableIds.Prepare(b, a);
					b.Save(tmp);
					IslandFile c = IslandFile.Load(tmp);
					Func<bool, ObjectState> st = active => new ObjectState { Active = active, Yield = -1, Day = 3 };
					var was = new Dictionary<int, ObjectState> { { TradeStand.KeyFor(8191, 2), st(false) }, { TradeStand.KeyFor(8193, 0), st(true) } };
					var expect = new Dictionary<int, ObjectState> { { TradeStand.KeyFor(8192, 2), was[TradeStand.KeyFor(8191, 2)] }, { TradeStand.KeyFor(8194, 0), was[TradeStand.KeyFor(8193, 0)] } };
					int dropped;
					string unsure;
					string moved = StableIds.Carry(a, c, IslandObjectState.Encode(was), false, out dropped, out unsure);
					Check(ref ok, moved != null && moved == IslandObjectState.Encode(expect) && dropped == 0,
						"stand units carried over the 8191 line and past it (dropped " + dropped + ")\n    got    " + moved + "\n    wanted " + IslandObjectState.Encode(expect));
				}
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { try { File.Delete(tmp); } catch { } }
			if (ok) Log("PASS: stand keys wide"); else Fail("stand keys wide");
		}

		[ConsoleCommand(name: "CIClaimRelease", docs: "Dev, in a world (host): RK6 - a granted claim let go (a trade that can no longer be paid) ends the host's hold at once, for that player only; an older host's question path is unchanged")]
		public static void ClaimReleaseCommand()
		{
			bool ok = true;
			if (!Raft_Network.IsHost) { Fail("claim release: needs a world this Raft hosts"); return; }
			try
			{
				var e = new IslandWorldState.Entry { Id = 990061, Name = "ciclaimrelease", HostName = "ciclaimrelease" };
				int key = TradeStand.KeyFor(4, 0);
				ulong a = 76561190000000011UL, b = 76561190000000012UL;
				Check(ref ok, Claims.HostGrant(e, key, a) && !Claims.HostGrant(e, key, b), "player A is granted the stand's unit, B is told no while A holds it");
				// (B's let go: not B's hold - nothing changes)
				Claims.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Claim, Ids = new[] { e.Id, 0 }, Index = key, Count = Claims.ReleaseMark }, b, x => { });
				Check(ref ok, Claims.IsHeld(e, key) && !Claims.HostGrant(e, key, b), "a let go from another player doesn't end A's hold");
				IslandNetMessage answer = null;
				Claims.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Claim, Ids = new[] { e.Id, 0 }, Index = key, Count = Claims.ReleaseMark }, a, x => answer = x);
				Check(ref ok, answer == null, "a let go isn't answered");
				Check(ref ok, !Claims.IsHeld(e, key) && Claims.HostGrant(e, key, b), "A lets go (could no longer pay): B gets it at once, not 6 s later");
				// (the host's own grant, let go the same way)
				Claims.Release(e, key);
				Check(ref ok, Claims.IsHeld(e, key), "the host's Release doesn't end B's hold");
				int key2 = TradeStand.KeyFor(4, 1);
				Check(ref ok, Claims.May(e, key2, y => { }) && Claims.IsHeld(e, key2), "the host itself is granted another unit");
				Claims.Release(e, key2);
				Check(ref ok, !Claims.IsHeld(e, key2) && Claims.HostGrant(e, key2, a), "... and its own Release ends that hold at once");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { Claims.Reset(); }
			if (ok) Log("PASS: claim release"); else Fail("claim release");
		}

		[ConsoleCommand(name: "CIRadarCopyDone", docs: "Dev, in a test world 'CI ...' (host): RK8 - a plan's island (ruled) whose quest was done on a rule-less copy (spawned by hand) has no radar dot left; before the copy is done it has")]
		public static void RadarCopyDoneCommand()
		{
			bool ok = true;
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (!raft.HasValue || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("radar copy done: run as the host in a test world 'CI ...'"); return; }
			const string name = "cirk8-radar";
			var made = new List<IslandWorldState.Entry>();
			try
			{
				IslandFile f = SampleIsland();
				f.Name = name;
				f.Props[IslandQuest.KeyTitle] = "Radar";
				f.Props[IslandQuest.KeySteps] = "reach|cizone|1|";
				f.Save(IslandSpawner.PathFor(name));
				Vector3 far = raft.Value + new Vector3(41000f, 0f, -41000f);
				far.y = 0f;
				IslandWorldState.Entry orig = IslandWorldState.Add(name, far, null, false), copy = IslandWorldState.Add(name, far + new Vector3(2000f, 0f, 0f), null, false);
				made.Add(orig); made.Add(copy);
				orig.Rule = "cirk8";
				orig.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
				Func<bool> open = () => Traverse.Create(typeof(IslandRadar)).Method("StillOpen", orig).GetValue<bool>();
				Check(ref ok, open(), "the plan's island, begun, its copy not done: it has a dot");
				copy.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
				QuestTracker.Set(copy, 1, 0, false);
				Check(ref ok, ReturningIslands.Finished(copy) && !open(), "the quest done on the rule-less copy: no dot for the plan's island");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally
			{
				if (made.Count > 0) IslandWorldState.RemoveIds(made.Select(e => e.Id).ToArray(), false);
				try { File.Delete(IslandSpawner.PathFor(name)); } catch { }
			}
			if (ok) Log("PASS: radar copy done"); else Fail("radar copy done");
		}
	}
}
