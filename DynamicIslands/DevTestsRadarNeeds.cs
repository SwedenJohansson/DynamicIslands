using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIRadarNeeds", docs: "Dev, in game (host, a fresh world without the engine): SL9 - a plan rule's 'needs=engine' WHERE part: the line kept; without the engine blueprint its island comes straight ahead (even a 'near' rule) and its Receiver dot is hidden; with it the dot shows and a 'near' rule places to the side")]
		public static void RadarNeedsCommand(string[] args) { StartTest(RadarNeedsRoutine()); }

		static IEnumerator RadarNeedsRoutine()
		{
			Network_Player me = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (me == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			const string bp = "Blueprint_MotorWheel";
			if (BlueprintSteps.Has(bp)) { Fail("this player has the engine blueprint already: use a fresh world"); yield break; }
			bool ok = true;
			// (the line: written with a short name, read back to the blueprint; old lines read as before)
			IntroRule a = IntroRule.Parse("gated | island:ciradarneeds | start | near:nowhere:900:west:needs=engine | | Gated");
			IntroRule b = IntroRule.Parse("plain | island:ciradarneeds | start | ahead:400 | | ");
			Check(ref ok, a != null && a.Needs == bp && a.Where == "near" && a.WhereRef == "nowhere" && a.Distance == 900f && a.Direction == "west", "needs=engine read: " + (a != null ? a.Needs + ", " + a.Where + ":" + a.WhereRef + ":" + a.Distance + ":" + a.Direction : "null"));
			Check(ref ok, a != null && a.ToLine().Contains("near:nowhere:900:west:needs=engine") && IntroRule.Parse(a.ToLine()).Needs == bp, "written back: " + (a != null ? a.ToLine() : ""));
			Check(ref ok, b != null && b.Needs == "" && !b.ToLine().Contains("needs="), "a rule without it: no needs part");
			Check(ref ok, a != null && a.DescribeWhere().Contains("Motor Wheel"), "described: " + (a != null ? a.DescribeWhere() : ""));
			yield return EnsureAlive();
			const string name = "ciradarneeds";
			var s = new IslandGenSettings { Seed = 8994, Radius = 25f, Height = 5f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandGenerator.CreateFile(s, name).Save(IslandSpawner.PathFor(name));
			IslandWorldState.Remove(name);
			WorldPlan keep = WorldDirector.Plan;
			IntroRule side = IntroRule.Parse("sided | island:ciradarneeds | start | near:gated:700:east:needs=engine | | Sided");
			WorldDirector.Plan = new WorldPlan { Name = "CI radar needs", Random = true, Rules = { a, side } };
			GameObject recv = null;
			try
			{
				// (without the engine: straight ahead of the raft, not west of an island that isn't there)
				Vector3 sail = CustomIslandSpawner.SailDirection();
				string why = WorldDirector.Bring(a, null, null);
				Check(ref ok, why == null, "the gated 'near' rule brings its island without the engine (" + (why ?? "on its way") + ")");
				IslandWorldState.Entry e = null;
				for (float t = 0f; t < 30f && (e == null || e.Root == null); t += 0.5f) { e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name && i.Rule == "gated"); yield return new WaitForSeconds(0.5f); }
				if (e == null) { Fail("the gated island did not come"); yield break; }
				Vector3 to = e.Position - raft.Value;
				float off = Vector2.Angle(new Vector2(sail.x, sail.z), new Vector2(to.x, to.z));
				Check(ref ok, off <= 20f, "it came straight ahead: " + Gf(off, "F0") + " deg off the way the raft sails, " + Gf(new Vector2(to.x, to.z).magnitude, "F0") + " m");
				Check(ref ok, ReturningIslands.NeedsOf(e) == bp, "its rule is found: needs " + ReturningIslands.NeedsOf(e));
				// (the Receiver: no dot until the player has the engine blueprint)
				Reciever r = MakeReceiver(me, out recv);
				if (r == null) { Fail("could not find Raft's receiver block"); yield break; }
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				Check(ref ok, !IslandRadar.Shown.Contains(e), "without the engine: no dot (" + IslandRadar.Shown.Count + " dot(s) for other islands)");
				me.Inventory.AddItem(bp, 1);
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				int at = IslandRadar.Shown.IndexOf(e);
				List<Reciever_Dot> dots = IslandRadar.DotsOf(r);
				Check(ref ok, at >= 0 && at < dots.Count && dots[at] != null && dots[at].gameObject.activeSelf && dots[at].dotNumberText != null && dots[at].dotNumberText.text.StartsWith("Gated"),
					"with the engine blueprint: its dot shows (" + (at >= 0 && at < dots.Count && dots[at] != null && dots[at].dotNumberText != null ? dots[at].dotNumberText.text : "none") + ")");
				// (with the engine: a 'near' rule goes to the side, as WHERE says)
				why = WorldDirector.Bring(side, null, null);
				IslandWorldState.Entry f = null;
				for (float t = 0f; t < 30f && (f == null || f.Root == null); t += 0.5f) { f = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name && i.Rule == "sided"); yield return new WaitForSeconds(0.5f); }
				if (f == null) Check(ref ok, false, "the sided island did not come (" + (why ?? "") + ")");
				else
				{
					Vector3 d = f.Position - e.Position;
					float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
					Check(ref ok, Mathf.Abs(Mathf.DeltaAngle(bearing, 90f)) <= 50f, "with the engine: placed east of the first island (bearing " + Gf(bearing, "F0") + ", " + Gf(new Vector2(d.x, d.z).magnitude, "F0") + " m)");
				}
			}
			finally
			{
				WorldDirector.Plan = keep;
				if (recv != null) UnityEngine.Object.Destroy(recv);
				me.Inventory.RemoveItem(bp, 1);
				IslandWorldState.Remove(name);
			}
			if (ok) Log("PASS: radar needs"); else Fail("radar needs");
		}

		[ConsoleCommand(name: "CIRadarFarReceiver", docs: "Dev, in game (host, a fresh world): an island on its own Receiver frequency, reached and its quest begun, then left beyond receiverDistance - it doesn't come back by itself, so its dot still shows however far; its quest done, the dot goes beyond the range (a player lost one, 2026-10-30)")]
		public static void RadarFarReceiverCommand(string[] args) { StartTest(RadarFarReceiverRoutine()); }

		static IEnumerator RadarFarReceiverRoutine()
		{
			Network_Player me = RAPI.GetLocalPlayer();
			if (me == null || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			if (!WorldRules.ShowOnReceiver) { Fail("custom islands are hidden on the Receiver (showOnReceiver = 0)"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			const string name = "ciradarfar";
			var s = new IslandGenSettings { Seed = 8995, Radius = 25f, Height = 5f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile file = IslandGenerator.CreateFile(s, name);
			var k = new MapKit(file, 5);
			Vector2 at = k.Find(k.Mid, 15f, MapKit.Dry, 4f) ?? k.Mid;
			k.Zone(at, "landing", 4f, "");
			k.Zone(at + new Vector2(6f, 0f), "lookout", 4f, "");
			k.Quest("Far signal", "Find the signal's source.", "Found it.", "", "reach|landing|1|Walk to the landing", "reach|lookout|1|Walk to the lookout");
			file.Save(IslandSpawner.PathFor(name));
			IslandWorldState.Remove(name);
			WorldPlan keep = WorldDirector.Plan;
			float keepRange = CustomIslandSpawner.ReceiverDistance;
			// (brought ahead so the test needn't tune the Receiver; then the plan's rule is the Receiver one, as in a world whose plan gives its frequency)
			IntroRule ahead = IntroRule.Parse("cifar | island:" + name + " | start | ahead:300 | | Far");
			IntroRule onReceiver = IntroRule.Parse("cifar | island:" + name + " | start | receiver:300 | | Far");
			// (a second frequency in the same game, its island also begun and left: both dots show, each until its own quest is done)
			IntroRule ahead2 = IntroRule.Parse("cifar2 | island:" + name + " | start | ahead:600 | | Far two");
			IntroRule onReceiver2 = IntroRule.Parse("cifar2 | island:" + name + " | start | receiver:600 | | Far two");
			WorldDirector.Plan = new WorldPlan { Name = "CI radar far", Random = true, Rules = { ahead, ahead2 } };
			GameObject recv = null;
			try
			{
				string why = WorldDirector.Bring(ahead, null, null);
				IslandWorldState.Entry e = null;
				for (float t = 0f; t < 30f && (e == null || e.Root == null); t += 0.5f) { e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name && i.Rule == "cifar"); yield return new WaitForSeconds(0.5f); }
				if (e == null) { Fail("the island did not come (" + (why ?? "") + ")"); yield break; }
				string why2 = WorldDirector.Bring(ahead2, null, null);
				IslandWorldState.Entry e2 = null;
				for (float t = 0f; t < 30f && (e2 == null || e2.Root == null); t += 0.5f) { e2 = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name && i.Rule == "cifar2"); yield return new WaitForSeconds(0.5f); }
				if (e2 == null) { Fail("the second island did not come (" + (why2 ?? "") + ")"); yield break; }
				WorldDirector.Plan = new WorldPlan { Name = "CI radar far", Random = true, Rules = { onReceiver, onReceiver2 } };
				// (reached, first step done, the second not: as the player who sailed off - from both)
				foreach (IslandWorldState.Entry x in new[] { e, e2 })
				{
					x.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0 };
					x.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 1 };
				}
				Check(ref ok, ReturningIslands.RuleThatBrought(e) == onReceiver && !ReturningIslands.Finished(e), "on its own Receiver frequency, quest begun and not done (step " + QuestTracker.StepOf(e) + ")");
				Check(ref ok, ReturningIslands.Why(e) == null, "it doesn't come back by itself (the Receiver shows the way): " + (ReturningIslands.Why(e) ?? "no return"));
				Check(ref ok, ReturningIslands.RuleThatBrought(e2) == onReceiver2 && ReturningIslands.Why(e2) == null && !ReturningIslands.Finished(e2), "the second frequency's island: begun, not done, no return by itself");
				float dist = Vector2.Distance(new Vector2(e.Position.x, e.Position.z), new Vector2(me.transform.position.x, me.transform.position.z));
				float dist2 = Vector2.Distance(new Vector2(e2.Position.x, e2.Position.z), new Vector2(me.transform.position.x, me.transform.position.z));
				CustomIslandSpawner.ReceiverDistance = Mathf.Max(20f, Mathf.Min(dist, dist2) * 0.25f); // (both islands now far beyond the range)
				Reciever r = MakeReceiver(me, out recv);
				if (r == null) { Fail("could not find Raft's receiver block"); yield break; }
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				int idx = IslandRadar.Shown.IndexOf(e);
				List<Reciever_Dot> dots = IslandRadar.DotsOf(r);
				Check(ref ok, idx >= 0 && idx < dots.Count && dots[idx] != null && dots[idx].gameObject.activeSelf,
					"beyond the Receiver's range (" + Gf(dist, "F0") + " m, range " + Gf(WorldRules.ReceiverDistance, "F0") + " m): its dot still shows (" + (idx >= 0 && idx < dots.Count && dots[idx] != null && dots[idx].dotNumberText != null ? dots[idx].dotNumberText.text : "none") + ")");
				int idx2 = IslandRadar.Shown.IndexOf(e2);
				Check(ref ok, idx2 >= 0 && idx2 < dots.Count && dots[idx2] != null && dots[idx2].gameObject.activeSelf, "the second frequency's island (" + Gf(dist2, "F0") + " m): its dot shows too");
				// (its quest done: an island nobody needs, so beyond the range no dot - the other frequency's island keeps its own)
				e.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 2 };
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				Check(ref ok, ReturningIslands.Finished(e) && !IslandRadar.Shown.Contains(e), "its quest done: no dot beyond the range (" + IslandRadar.Shown.Count + " dot(s))");
				Check(ref ok, IslandRadar.Shown.Contains(e2), "the second frequency's island, still unfinished: its dot still shows");
				// (an island of the pool - no rule - its quest begun and back the most times it comes: no return, but its dot stays)
				e2.Rule = "";
				e2.State[ReturningIslands.ReturnsKey] = new ObjectState { Active = true, Yield = ReturningIslands.MaxReturns };
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				Check(ref ok, ReturningIslands.Why(e2) == null && IslandRadar.Shown.Contains(e2), "a pool island past its returns, quest unfinished: no return (" + (ReturningIslands.Why(e2) ?? "none") + "), its dot still shows");
				CustomIslandSpawner.ReceiverDistance = 0f;
				yield return new WaitForSeconds(1.2f);
				IslandRadar.Draw(r);
				Check(ref ok, IslandRadar.Shown.Contains(e), "range 0 (all): its dot shows again");
			}
			finally
			{
				WorldDirector.Plan = keep;
				CustomIslandSpawner.ReceiverDistance = keepRange;
				if (recv != null) UnityEngine.Object.Destroy(recv);
				IslandWorldState.Remove(name);
			}
			if (ok) Log("PASS: radar far receiver"); else Fail("radar far receiver");
		}

		[ConsoleCommand(name: "CISpawnStoryAgain", docs: "Dev, in game (host, a fresh world): SpawnIsland <name> of a story island (one with a rule) this world has, quest unfinished: that entry comes back (quest kept) instead of a fresh rule-less copy; loaded already, nothing is added; a rule-less same-name copy whose quest is done counts the story step as done (a player's SpawnIsland copy, 2026-10-30)")]
		public static void SpawnStoryAgainCommand(string[] args) { StartTest(SpawnStoryAgainRoutine()); }

		static IEnumerator SpawnStoryAgainRoutine()
		{
			Network_Player me = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (me == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			const string name = "cispawnstory";
			const string typed = "CiSpawnStory"; // (the player's spelling: any case finds it)
			var s = new IslandGenSettings { Seed = 8996, Radius = 25f, Height = 5f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile file = IslandGenerator.CreateFile(s, name);
			var k = new MapKit(file, 5);
			Vector2 at = k.Find(k.Mid, 15f, MapKit.Dry, 4f) ?? k.Mid;
			k.Zone(at, "landing", 4f, "");
			k.Zone(at + new Vector2(6f, 0f), "lookout", 4f, "");
			k.Quest("Spawn again", "Find the lookout.", "Found it.", "", "reach|landing|1|Walk to the landing", "reach|lookout|1|Walk to the lookout");
			file.Save(IslandSpawner.PathFor(name));
			IslandWorldState.Remove(name);
			WorldPlan keep = WorldDirector.Plan;
			IntroRule ahead = IntroRule.Parse("cispawn | island:" + name + " | start | ahead:300 | | Spawn again");
			WorldDirector.Plan = new WorldPlan { Name = "CI spawn story", Random = true, Rules = { ahead } };
			Func<int> copies = () => IslandWorldState.Islands.Count(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			try
			{
				string why = WorldDirector.Bring(ahead, null, null);
				IslandWorldState.Entry e = null;
				for (float t = 0f; t < 30f && (e == null || e.Root == null); t += 0.5f) { e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name && i.Rule == "cispawn"); yield return new WaitForSeconds(0.5f); }
				if (e == null || e.Root == null) { Fail("the story island did not come (" + (why ?? "") + ")"); yield break; }
				// (reached, first step done, the second not; then left far behind and unloaded, as after the player sailed off)
				e.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0 };
				e.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 1 };
				Vector3 sail = CustomIslandSpawner.SailDirection();
				e.Position = raft.Value + new Vector3(sail.x, 0f, sail.z).normalized * 4000f;
				IslandObjectState.Capture(e);
				IslandSpawner.Despawn(e.Root);
				e.Root = null;
				Check(ref ok, !ReturningIslands.Finished(e) && e.Rule == "cispawn" && QuestTracker.StepOf(e) == 1 && copies() == 1, "set up: a story island, quest begun and not done, unloaded far away (" + copies() + " entry)");

				// a) SpawnIsland of its name: that entry comes back
				float returnsBefore = ReturningIslands.Returns(e);
				DynamicIslands.SpawnIslandCommand(new[] { typed });
				yield return new WaitForSeconds(0.5f);
				float away = new Vector2(e.Position.x - raft.Value.x, e.Position.z - raft.Value.z).magnitude;
				Check(ref ok, copies() == 1 && IslandWorldState.Islands.Contains(e), "SpawnIsland " + typed + ": no second entry (" + copies() + " with that name), the story's own is still there");
				Check(ref ok, e.Rule == "cispawn" && QuestTracker.StepOf(e) == 1 && !ReturningIslands.Finished(e), "it is still the story's: rule '" + e.Rule + "', quest step " + QuestTracker.StepOf(e) + " kept");
				Check(ref ok, away < 600f && ReturningIslands.Returns(e) == returnsBefore + 1, "it came back ahead of the raft (" + Gf(away, "F0") + " m from it, came back " + ReturningIslands.Returns(e) + "x)");
				for (float t = 0f; t < 40f && e.Root == null; t += 0.5f) yield return new WaitForSeconds(0.5f);
				Check(ref ok, e.Root != null && copies() == 1, "it loads there: " + (e.Root != null ? "loaded" : "not loaded after 40 s"));

				// b) loaded already: nothing is added or moved
				Vector3 posBefore = e.Position;
				int returnsAfter = ReturningIslands.Returns(e);
				int total = IslandWorldState.Islands.Count;
				DynamicIslands.SpawnIslandCommand(new[] { typed.ToLowerInvariant() });
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, IslandWorldState.Islands.Count == total && copies() == 1 && e.Position == posBefore && ReturningIslands.Returns(e) == returnsAfter && QuestTracker.StepOf(e) == 1,
					"loaded already: SpawnIsland adds nothing, moves nothing (" + IslandWorldState.Islands.Count + " islands, " + copies() + " with that name)");

				// c) a rule-less same-name copy (spawned by hand before the fix) whose quest is done: the rule's step counts as done
				Check(ref ok, !StoryChain.IsDone(ahead), "the story island's own quest is not done: the step is not done");
				IslandWorldState.Entry x = IslandWorldState.Add(name, raft.Value + new Vector3(sail.x, 0f, sail.z).normalized * 5000f, null, false);
				x.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 1 };
				Check(ref ok, x.Rule.Length == 0 && !StoryChain.IsDone(ahead), "a rule-less copy with its quest not done: the step is not done");
				x.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 2 };
				Check(ref ok, StoryChain.IsDone(ahead), "a rule-less copy with its quest done: the step counts as done");
				x.Name = "cispawnother";
				Check(ref ok, !StoryChain.IsDone(ahead), "a rule-less entry of another name does not count");
			}
			finally
			{
				WorldDirector.Plan = keep;
				IslandWorldState.Remove(name);
			}
			if (ok) Log("PASS: spawn story again"); else Fail("spawn story again");
		}

		/// <summary>A Receiver (Raft's block) above the player with its radar on.</summary>
		static Reciever MakeReceiver(Network_Player me, out GameObject go)
		{
			go = null;
			Reciever prefab = null;
			foreach (Item_Base item in ItemManager.GetAllItems())
			{
				try
				{
					if (item == null || item.settings_buildable == null || !item.settings_buildable.Placeable) continue;
					Block[] blocks = item.settings_buildable.GetBlockPrefabs();
					prefab = blocks != null ? blocks.Where(x => x != null).Select(x => x.GetComponentInChildren<Reciever>(true)).FirstOrDefault(x => x != null) : null;
					if (prefab != null) break;
				}
				catch { }
			}
			if (prefab == null) return null;
			go = UnityEngine.Object.Instantiate(prefab.transform.root.gameObject, me.transform.position + Vector3.up * 3f, Quaternion.identity);
			Reciever r = go.GetComponentInChildren<Reciever>(true);
			if (r != null && r.radarSection != null) r.radarSection.SetActive(true);
			return r;
		}
	}
}
