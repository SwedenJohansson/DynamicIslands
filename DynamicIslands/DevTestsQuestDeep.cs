using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIQuestDeep", docs: "Dev, in a world (host): questdeepA 11-14 - an unnamed islander escorted to two zones in a row (the second step used to stick at the first zone); taming: a goat of another spot of the step's kind is fed, a tamed animal whose player left goes free, the pen budget; a main story island sailed past unvisited comes back")]
		public static void QuestDeepCommand(string[] args) { StartTest(QuestDeepRoutine()); }

		static IEnumerator QuestDeepRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;

			// (a) the pen budget (no world needed): free animals less what the current step still needs
			int budget = 3 - 1, a1 = Taming.RePenTake(1, 0, ref budget), a2 = Taming.RePenTake(2, 0, ref budget), a3 = Taming.RePenTake(1, 0, ref budget);
			Check(ref ok, a1 == 1 && a2 == 1 && a3 == 0 && budget == 0, "the pen budget: 3 free, 1 still needed - 2 go back in the pen, not 4 (" + a1 + ", " + a2 + ", " + a3 + ")");
			budget = 1;
			Check(ref ok, Taming.RePenTake(2, 2, ref budget) == 0 && budget == 1, "a spot whose penned ones are all there takes none");

			// (b) a main story island sailed past before anyone set foot on it comes back; a plain one doesn't
			string[] names = { "ciqd-story", "ciqd-plain", "ciqd-storydone" };
			IslandFile qf = SampleIsland();
			qf.Props[IslandQuest.KeyTitle] = "Sailed past";
			qf.Props[IslandQuest.KeySteps] = "reach|cizone|1|walk in";
			foreach (string n in names) { qf.Name = n; qf.Save(IslandSpawner.PathFor(n)); }
			WorldPlan planBefore = WorldDirector.Plan;
			var made = new List<IslandWorldState.Entry>();
			try
			{
				Vector3 far = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				far.y = 0f;
				for (int i = 0; i < names.Length; i++) made.Add(IslandWorldState.Add(names[i], far + Vector3.right * 2000f * i, null, false));
				made[0].Rule = "qdstory"; made[1].Rule = "qdplain"; made[2].Rule = "qdstory2";
				made[2].State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
				QuestTracker.Set(made[2], 1, 0, false);
				var plan = new WorldPlan { Name = "CI questdeep", Random = false };
				plan.Rules.Add(IntroRule.Parse("qdstory | island:ciqd-story | start | ahead:300 | | | beside"));
				plan.Rules.Add(IntroRule.Parse("qdplain | island:ciqd-plain | start | ahead:300 | |"));
				plan.Rules.Add(IntroRule.Parse("qdstory2 | island:ciqd-storydone | start | ahead:300 | | | beside"));
				WorldDirector.Plan = plan;
				string ws = ReturningIslands.Why(made[0]), wp = ReturningIslands.Why(made[1]), wd = ReturningIslands.Why(made[2]);
				Check(ref ok, ws == "quest" && wp == null && wd == null, "never visited: the main story island comes back (" + (ws ?? "no") + "), a plain one doesn't (" + (wp ?? "no") + "), a done main story one doesn't (" + (wd ?? "no") + ")");
			}
			finally
			{
				WorldDirector.Plan = planBefore;
				IslandWorldState.RemoveIds(made.Select(e => e.Id).ToList(), false);
				foreach (string n in names) try { File.Delete(IslandSpawner.PathFor(n)); } catch { }
			}

			// (c) the test island: an unnamed islander (quest name "Islander") escorted to camp, then to the hut; goats on two spots
			int walker = -1, goatsA = -1, goatsB = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				walker = add(Islanders.ObjectName, -6f, 0f, P(Islanders.CharacterProp, "4"));
				add(ContentCatalog.TriggerZone, -6f, 14f, P(ObjectProps.ZoneId, "camp", ObjectProps.ZoneRadius, "3"));
				add(ContentCatalog.TriggerZone, 8f, 16f, P(ObjectProps.ZoneId, "hut", ObjectProps.ZoneRadius, "3"));
				goatsA = add("Creature_Goat", 6f, -6f, P(ObjectProps.CreatureCount, "1"));
				goatsB = add("Creature_Goat", 14f, -2f, P(ObjectProps.CreatureCount, "2"));
				add(ContentCatalog.TriggerZone, -14f, -10f, P(ObjectProps.ZoneId, "pen", ObjectProps.ZoneRadius, "3"));
				file.Props[IslandQuest.KeyTitle] = "CI deep";
				file.Props[IslandQuest.KeySteps] = "escort|Islander@camp|1|\nescort|Islander@hut|1|\nreach|nowhere|1|";
				file.Props["quest2.title"] = "CI deep goats";
				file.Props["quest2.steps"] = "tame|Goat@pen|2|\nreach|nowhere|1|";
			});
			IslandWorldState.Entry isl = null;
			yield return SpawnPlayerIsland(f, x => isl = x);
			if (isl == null || isl.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(isl); yield break; }
			Func<int, IslandObjectRef> obj = i => isl.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			Network_Player me = RAPI.GetLocalPlayer();
			try
			{
				yield return new WaitForSeconds(1f);
				IslandObjectRef mr = obj(walker);
				IslanderWalk mw = mr != null ? mr.GetComponent<IslanderWalk>() : null;
				TriggerZone camp = IslanderWalks.ZoneOf(isl, "camp"), hut = IslanderWalks.ZoneOf(isl, "hut");
				Check(ref ok, mw != null && camp != null && hut != null, "the islander's walk and both zones are there");
				if (mw == null || camp == null || hut == null) yield break;
				Check(ref ok, IslanderWalks.EscortZone(isl, IslanderWalks.QuestName(mr.Props)) == "camp", "the unnamed islander is the escort step's 'Islander' (" + IslanderWalks.QuestName(mr.Props) + ")");
				foreach (var leg in new[] { new KeyValuePair<TriggerZone, int>(camp, 0), new KeyValuePair<TriggerZone, int>(hut, 1) })
				{
					TriggerZone z = leg.Key;
					PutPlayerNear(mr.transform, 3f);
					yield return new WaitForSeconds(1.5f);
					for (int i = 0; i < 14 && QuestTracker.StepOf(isl) == leg.Value; i++)
					{
						Vector3 dir = z.transform.position - mr.transform.position; dir.y = 0f; dir.Normalize();
						Vector3 next = mr.transform.position + dir * 4f;
						if (Vector3.Distance(Flat(next), Flat(z.transform.position)) < 4f) next = z.transform.position + dir * 1f;
						MovePlayerTo(next);
						yield return new WaitForSeconds(2f);
					}
					yield return new WaitForSeconds(1f);
					Check(ref ok, QuestTracker.StepOf(isl) > leg.Value, "escort to " + z.Id + ": the step is done (step " + QuestTracker.StepOf(isl) + ", " + mw.State + ", " + Vector3.Distance(Flat(mr.transform.position), Flat(z.transform.position)).ToString("F1") + " m from it)");
					if (QuestTracker.StepOf(isl) <= leg.Value) break;
				}

				// (d) taming: a goat of spot B fed with spot A named (a client sends the first spot of the kind)
				IslandObjectRef ra = obj(goatsA), rb = obj(goatsB);
				CreatureSpawnPoint sa = ra != null ? ra.GetComponent<CreatureSpawnPoint>() : null, sb = rb != null ? rb.GetComponent<CreatureSpawnPoint>() : null;
				Check(ref ok, sa != null && sb != null, "both goat spots are there");
				if (sa == null || sb == null) yield break;
				PutPlayerNear(rb.transform, 4f);
				float t0 = Time.realtimeSinceStartup;
				while ((sa.Spawned.Count(a => a != null) < 1 || sb.Spawned.Count(a => a != null) < 2) && Time.realtimeSinceStartup - t0 < 30f) yield return new WaitForSeconds(0.5f);
				AI_NetworkBehaviour goatB = sb.Spawned.FirstOrDefault(a => a != null);
				Check(ref ok, goatB != null && sa.Spawned.Any(a => a != null), "the goats came (A " + sa.Spawned.Count(a => a != null) + ", B " + sb.Spawned.Count(a => a != null) + ")");
				if (goatB == null) yield break;
				PlayerMove.To(me, goatB.transform.position + Vector3.up * 1.2f + Vector3.right * 1.2f);
				KeepAlive(me);
				yield return new WaitForSeconds(0.5f);
				bool fed = Taming.HostFeed(isl, ra.Index, MechFeatures.LocalId, goatB.transform.position);
				AI_NetworkBehaviour follower = sb.Spawned.FirstOrDefault(Taming.IsTamed);
				Check(ref ok, fed && follower != null && !sa.Spawned.Any(Taming.IsTamed), "fed with spot A named, the goat of spot B next to the player follows (" + fed + ")");
				// (e) its player gone: it goes free
				int released = Taming.Released;
				var dict = (IDictionary)typeof(Taming).GetField("tamed", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
				foreach (object t in dict.Values.Cast<object>().ToList()) t.GetType().GetField("Owner").SetValue(t, 4242UL);
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, follower == null || (!Taming.IsTamed(follower) && Taming.Released > released), "its player left the game: the goat goes free (released " + (Taming.Released - released) + ")");
				// (f) a saved pen place with the step still open (a reload mid-step): a goat stands in, the others stay free to tame
				isl.State[Taming.KeyBase + ra.Index] = new ObjectState { Active = false, Yield = 0, Day = Behaviours.Today };
				Check(ref ok, Taming.Found(isl, "Goat@pen") == 1, "one goat counted in the pen (" + Taming.Found(isl, "Goat@pen") + ")");
				t0 = Time.realtimeSinceStartup;
				while (Taming.PennedNow == 0 && Time.realtimeSinceStartup - t0 < 6f) yield return new WaitForSeconds(0.5f);
				int freeLeft = sa.Spawned.Concat(sb.Spawned).Count(a => a != null && !Taming.IsTamed(a));
				Check(ref ok, Taming.PennedNow == 1 && freeLeft >= 1, "a goat back in the pen, " + freeLeft + " left to tame for the step's last one (penned " + Taming.PennedNow + ")");
			}
			finally
			{
				RemovePlayerIsland(isl);
			}
			if (ok) Log("PASS: CIQuestDeep - escort twice with an unnamed islander, taming across spots, owner gone, pen budget, unvisited main story returns");
			else Fail("CIQuestDeep: see the FAIL lines above");
		}

		[ConsoleCommand(name: "CIQuestDeep2", docs: "Dev, in a world (host): questdeepA round 2 - a player's 'arrive' on an island not loaded on the host, with a show of its objects, is kept until it loads (once); a player's zone and chest the host refused are ready again (also when their used mark comes after the refusal); the story chain: an island's 'frequency' action unlocks that island's own step (no earlier step counted done; a left-out island unlocks nothing); a story island put back in the plan after the chain passed its place unlocks. What the world had is put back after")]
		public static void QuestDeep2Command() { StartTest(QuestDeep2Routine()); }

		static IEnumerator QuestDeep2Routine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;

			// (a) A3: a player's island event (arrive) where the host hasn't the island loaded: kept as pending, once
			string far = "ciqd2-far", farPath = IslandSpawner.PathFor(far);
			IslandFile ff = SampleIsland();
			ff.Name = far;
			ff.Props[BehaviourProps.EventKey("arrive")] = "show|ciqd2-door";
			ff.Save(farPath);
			IslandCache.ForgetFile(farPath);
			IslandWorldState.Entry fe = null;
			try
			{
				Vector3 at = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				at.y = 0f;
				fe = IslandWorldState.Add(far, at, null, false);
				Check(ref ok, fe != null && fe.Root == null, "a far island, not loaded here");
				if (fe != null)
				{
					Func<int> pending = () => fe.State.Keys.Count(k => k >= Behaviours.PendingBase && k < Behaviours.PendingBase + 0x100000);
					int had = pending(), kept = Behaviours.KeptUnloaded;
					Behaviours.OnEventMessage(fe.Id, Behaviours.IslandIndex, "arrive", false, null, false);
					Check(ref ok, Behaviours.KeptUnloaded == kept + 1 && pending() == had + 1, "a player's arrive that shows something there is kept until the island loads (pending " + had + " -> " + pending() + ")");
					Behaviours.OnEventMessage(fe.Id, Behaviours.IslandIndex, "arrive", false, null, false);
					Check(ref ok, Behaviours.KeptUnloaded == kept + 1 && pending() == had + 1, "a second player's arrive keeps nothing more (once)");
				}
			}
			finally
			{
				if (fe != null) IslandWorldState.RemoveIds(new List<int> { fe.Id }, false);
				try { File.Delete(farPath); IslandCache.ForgetFile(farPath); } catch { }
			}

			// (b) A1: a player's zone and chest refused by the host are ready again
			int zi = -1, ci = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				zi = add(ContentCatalog.TriggerZone, -8f, 8f, P(ObjectProps.ZoneId, "qd2zone", ObjectProps.ZoneRadius, "2"));
				ci = add("Loot_Chest", 8f, -8f, P(ObjectProps.LootItems, "Plank:1"));
			});
			IslandWorldState.Entry isl = null;
			yield return SpawnPlayerIsland(f, x => isl = x);
			if (isl == null || isl.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(isl); yield break; }
			try
			{
				yield return new WaitForSeconds(1f);
				IslandObjectRef zr = isl.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == zi);
				IslandObjectRef cr = isl.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == ci);
				TriggerZone zone = zr != null ? zr.GetComponent<TriggerZone>() : null;
				LootCrate chest = cr != null ? cr.GetComponentInChildren<LootCrate>(true) : null;
				Check(ref ok, zone != null && !zone.Repeats && chest != null, "a once zone and a chest are there");
				if (zone != null && chest != null)
				{
					var used = new ObjectState { Active = false, Yield = 0, Day = Behaviours.Today };
					int freed = Behaviours.FreedOnRefusal;
					try
					{
						Behaviours.RefuseForTests = true;
						// (the player marked them used before asking: their used marks came first)
						isl.State[zone.StateKey] = used;
						isl.State[chest.StateKey] = used;
						Behaviours.OnEventMessage(isl.Id, zi, "enter", false, null, true);
						Behaviours.OnEventMessage(isl.Id, ci, "open", false, null, true);
						Check(ref ok, !ContentState.IsUsed(isl, zone.StateKey) && !ContentState.IsUsed(isl, chest.StateKey) && Behaviours.FreedOnRefusal == freed + 2,
							"refused: the zone and the chest are ready again (freed " + (Behaviours.FreedOnRefusal - freed) + ")");
						// (the used mark after the refusal: looked at again 2 s later)
						Behaviours.OnEventMessage(isl.Id, zi, "enter", false, null, true);
						isl.State[zone.StateKey] = used;
						yield return new WaitForSeconds(2.6f);
						Check(ref ok, !ContentState.IsUsed(isl, zone.StateKey) && Behaviours.FreedOnRefusal == freed + 3, "its used mark came after the refusal: ready again 2 s later");
						// (not refused: used stays)
						Behaviours.RefuseForTests = false;
						isl.State[zone.StateKey] = used;
						Behaviours.OnEventMessage(isl.Id, zi, "enter", false, null, true);
						yield return new WaitForSeconds(0.5f);
						Check(ref ok, ContentState.IsUsed(isl, zone.StateKey) && Behaviours.FreedOnRefusal == freed + 3, "a yes leaves it used");
					}
					finally { Behaviours.RefuseForTests = false; }
				}
			}
			finally { RemovePlayerIsland(isl); }

			// (c) S2 + S3: the story chain
			List<string> linesBefore = StoryChain.WriteLines().ToList();
			var unlockedBefore = NoteBook.unlockedChunkPointType.ToList();
			var indexesBefore = NoteBook.unlockedNoteBookIndexes.ToList();
			RecieverFrequency[] freqBefore = RecieverFrequency.AllFrequencies != null ? RecieverFrequency.AllFrequencies.ToArray() : null;
			try
			{
				Dictionary<int, ChunkPointType> notes = StoryOrder.FrequencyNotes();
				NoteBook.unlockedNoteBookIndexes.RemoveAll(i => notes.ContainsKey(i));
				NoteBook.unlockedChunkPointType.RemoveAll(t => Chain.Contains(t) || (int)t >= StoryChain.ModTypeBase);
				StoryChain.Reset();
				const string rule = "rule = detour | type:sandbar | start | receiver:400 | A test signal. | Detour | after:Vasagatan | visit\n";
				StoryChain.FromPlan(WorldPlan.Parse("CI chain", "storyleaveout = Balboa\n" + rule));
				NoteBook.UnlockFrequency(ChunkPointType.Landmark_RadioTower);
				NoteBook.UnlockFrequency(ChunkPointType.Landmark_Vasagatan);
				Check(ref ok, StoryChain.Active && Names(ChainTypes()) == "Radio Tower,Vasagatan" && !StoryChain.Done.Contains("raft:Vasagatan"), "the chain at Vasagatan: " + Names(ChainTypes()));
				Func<ChunkPointType, ObjAction> freq = t => new ObjAction { Verb = ReceiverUnlock.Verb, Target = StoryOrder.Key(t), Arg = "" };
				string done0 = Steps(StoryChain.Done.OrderBy(x => x));
				ReceiverUnlock.HostRun(null, freq(ChunkPointType.Landmark_CaravanIsland));
				Check(ref ok, StoryChain.Unlocked.Contains("raft:CaravanTown") && ChainTypes().Contains(ChunkPointType.Landmark_CaravanIsland) && Steps(StoryChain.Done.OrderBy(x => x)) == done0,
					"frequency|Caravan Town (the island before it left out): Caravan Town unlocks, nothing counted done (" + Names(ChainTypes()) + "; done " + Steps(StoryChain.Done.OrderBy(x => x)) + ")");
				ReceiverUnlock.HostRun(null, freq(ChunkPointType.Landmark_Tangaroa));
				Check(ref ok, StoryChain.Unlocked.Contains("raft:Tangaroa") && !StoryChain.Done.Contains("raft:CaravanTown"), "frequency|Tangaroa: Tangaroa unlocks, Caravan Town's step isn't counted done");
				int u = StoryChain.Unlocked.Count;
				ReceiverUnlock.HostRun(null, freq(ChunkPointType.Landmark_Balboa));
				Check(ref ok, StoryChain.Unlocked.Count == u && !ChainTypes().Contains(ChunkPointType.Landmark_Balboa), "frequency|Balboa (left out): nothing unlocks");

				// S3: Balboa put back in the plan after the chain passed its place
				StoryChain.MarkDone("raft:Vasagatan");
				StoryChain.MarkDone("rule:detour");
				StoryChain.MarkDone("raft:CaravanTown");
				StoryChain.FromPlan(WorldPlan.Parse("CI chain", rule));
				Check(ref ok, StoryChain.Steps.Contains("raft:Balboa") && StoryChain.Unlocked.Contains("raft:Balboa") && ChainTypes().Contains(ChunkPointType.Landmark_Balboa),
					"Balboa back in the plan, put in before the last step done: it unlocks (" + Steps(StoryChain.Steps) + "; " + Names(ChainTypes()) + ")");
				Check(ref ok, StoryChain.Done.Contains("raft:CaravanTown") && StoryChain.Unlocked.Contains("raft:Tangaroa"), "what was done and unlocked stays");
			}
			finally
			{
				StoryChain.Reset();
				foreach (string l in linesBefore) { int eq = l.IndexOf('='); StoryChain.ReadLine(l.Substring(1, eq - 1), l.Substring(eq + 1)); }
				NoteBook.unlockedNoteBookIndexes.Clear(); NoteBook.unlockedNoteBookIndexes.AddRange(indexesBefore);
				NoteBook.unlockedChunkPointType.Clear(); NoteBook.unlockedChunkPointType.AddRange(unlockedBefore);
				if (freqBefore != null) RecieverFrequency.AllFrequencies = freqBefore;
				StoryChain.OnWorldRead();
				IslandWorldState.Save();
			}

			if (ok) Log("PASS: CIQuestDeep2 - player's arrive kept for an unloaded island, refused zone and chest ready again, frequency action and plan edit in the story chain");
			else Fail("CIQuestDeep2: see the FAIL lines above");
		}

		const string Qd6Island = "ciqd6";

		[ConsoleCommand(name: "CIQD6", docs: "Dev, two players (mpqd6.ps1, questdeepA QD6/QD9): setup (host) = 'ciqd6' near the raft: goat spot A (1) and B (2), a pen zone, quest tame Goat@pen x2; a once zone that needs the crew's story key 'qd6-key' (the host has one) and sends signal qd6-zone. wait = until it is loaded here (the host: the goats too). feed (player) = feeds a goat of spot B, sending spot A as a player's game does: it must follow. check (host) = a goat of spot B is tamed. refuse on|off (host) = refuse every player's event that waits for an answer. zone (player) = walks into the zone with the host refusing: used at once, ready again after the refusal. zone2 (player) = again with the host saying yes: used, the host has the signal. drop (host)")]
		public static void Qd6Command(string[] args) { StartTest(Qd6Routine(args ?? new string[0])); }

		static IEnumerator Qd6Routine(string[] args)
		{
			string what = args.Length > 0 ? args[0].ToLowerInvariant() : "";
			string side = Raft_Network.IsHost ? "host" : "player";
			Func<IslandWorldState.Entry> entry = () => IslandWorldState.Islands.FirstOrDefault(i => i.Root != null && i.HostName == Qd6Island);
			if (what == "setup" || what == "drop" || what == "refuse" || what == "check")
			{
				if (!Raft_Network.IsHost) { Fail("QD6 " + what + ": as the host"); yield break; }
				if (what == "drop") { Behaviours.RefuseForTests = false; StoryBook.Take("qd6-key", StoryBook.Count("qd6-key")); GwDrop(new GwIsland { E = entry() }, Qd6Island); Log("PASS: QD6 drop"); yield break; }
				if (what == "refuse") { Behaviours.RefuseForTests = args.Length < 2 || args[1] != "off"; Log("PASS: QD6 refuse " + (Behaviours.RefuseForTests ? "on" : "off")); yield break; }
				if (what == "check")
				{
					IslandWorldState.Entry he = entry();
					CreatureSpawnPoint hb = he != null ? Qd6Spots(he).Select(x => x.Value).LastOrDefault() : null;
					AI_NetworkBehaviour tamedB = null;
					for (float t = 0f; t < 10f && tamedB == null; t += 0.5f) { tamedB = hb != null ? hb.Spawned.FirstOrDefault(Taming.IsTamed) : null; if (tamedB == null) yield return new WaitForSeconds(0.5f); }
					if (tamedB != null) Log("PASS: QD6 check - a goat of spot B is tamed");
					else Fail("QD6 check: no goat of spot B is tamed");
					yield break;
				}
				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(Qd6Island, out at);
				f.Objects.Add(new IslandObject { Name = "Creature_Goat", Position = at(-14f, -10f), Props = P(ObjectProps.CreatureCount, "1") });
				f.Objects.Add(new IslandObject { Name = "Creature_Goat", Position = at(14f, 8f), Props = P(ObjectProps.CreatureCount, "2") });
				f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = at(-14f, 12f), Props = P(ObjectProps.ZoneId, "pen", ObjectProps.ZoneRadius, "3") });
				f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = at(0f, -16f), Props = P(ObjectProps.ZoneId, "qd6zone", ObjectProps.ZoneRadius, "2", BehaviourProps.Name, "qd6-zone",
					BehaviourProps.CheckKey("enter"), "has|story:qd6-key|1", BehaviourProps.ElseKey("enter"), "message||Refused.", BehaviourProps.EventKey("enter"), "signal||qd6-zone") });
				f.Props[StoryItems.Key] = StoryItems.Text(new List<StoryItemDef> { GstDef("qd6-key", "Pen key", "Vasagatan_BlueKey", "A key for the zone.") });
				f.Props[IslandQuest.KeyTitle] = "CI qd6";
				f.Props[IslandQuest.KeySteps] = "tame|Goat@pen|2|\nreach|nowhere|1|";
				var g = new GwIsland();
				yield return GwSpawnFile(g, f, Qd6Island);
				if (g.E == null) { Fail("QD6 setup: the island did not spawn"); yield break; }
				StoryBook.Take("qd6-key", StoryBook.Count("qd6-key"));
				StoryBook.Give("qd6-key", 1);
				Log("PASS: QD6 setup - " + Qd6Island + " is island " + g.E.Id);
				yield break;
			}
			if (what == "wait")
			{
				for (float t = 0f; t < 90f && entry() == null; t += 1f) yield return new WaitForSeconds(1f);
				IslandWorldState.Entry we = entry();
				if (we == null) { Fail("QD6: '" + Qd6Island + "' did not come (" + side + ")"); yield break; }
				var spots = Qd6Spots(we);
				if (Raft_Network.IsHost && spots.Count == 2)
				{
					PutPlayerNear(spots[1].Key.transform, 8f);
					for (float t = 0f; t < 40f && (spots[0].Value.Spawned.Count(a => a != null) < 1 || spots[1].Value.Spawned.Count(a => a != null) < 2); t += 0.5f) yield return new WaitForSeconds(0.5f);
				}
				string goats = spots.Count == 2 ? spots[0].Value.Spawned.Count(a => a != null) + "+" + spots[1].Value.Spawned.Count(a => a != null) : "?";
				if (spots.Count == 2 && (!Raft_Network.IsHost || goats == "1+2")) Log("PASS: QD6 wait - the island is here (" + side + ", goats " + goats + ")");
				else Fail("QD6 wait: spots " + spots.Count + ", goats " + goats + " (" + side + ")");
				yield break;
			}
			IslandWorldState.Entry e = entry();
			Network_Player me = RAPI.GetLocalPlayer();
			if (e == null || me == null) { Fail("QD6 " + what + ": no '" + Qd6Island + "' loaded or no player here (" + side + ")"); yield break; }
			if (Raft_Network.IsHost) { Fail("QD6 " + what + ": as a player (the box)"); yield break; }
			if (what == "feed")
			{
				var spots = Qd6Spots(e);
				if (spots.Count != 2) { Fail("QD6 feed: " + spots.Count + " goat spots here"); yield break; }
				IslandObjectRef ra = spots[0].Key, rb = spots[1].Key;
				Func<AI_NetworkBehaviour> goatB = () => UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>()
					.Where(a => a != null && a.behaviourType == AI_NetworkBehaviourType.Goat && Vector3.Distance(a.transform.position, rb.transform.position) < 12f && Vector3.Distance(a.transform.position, ra.transform.position) > 15f)
					.OrderBy(a => Vector3.Distance(a.transform.position, rb.transform.position)).FirstOrDefault();
				AI_NetworkBehaviour goat = null;
				for (float t = 0f; t < 20f && (goat = goatB()) == null; t += 0.5f) yield return new WaitForSeconds(0.5f);
				if (goat == null) { Fail("QD6 feed: no goat of spot B seen here"); yield break; }
				PlayerMove.To(me, goat.transform.position + Vector3.up * 1.2f + Vector3.right * 1.2f);
				yield return new WaitForSeconds(1.5f); // (this player's place reaches the host)
				PlayerMove.To(me, goat.transform.position + Vector3.up * 1.2f + Vector3.right * 1.2f);
				yield return new WaitForSeconds(1f);
				Taming.LastAnswer = -1;
				// (a player's game sends the first spot of the step's kind, whichever goat it fed)
				Taming.SendFeed(e, ra.Index, goat.transform.position, Taming.FoodOf(ra.Props, AI_NetworkBehaviourType.Goat));
				for (float t = 0f; t < 10f && Taming.LastAnswer < 0; t += 0.25f) yield return new WaitForSeconds(0.25f);
				if (Taming.LastAnswer == 1) Log("PASS: QD6 feed - the host says the goat of spot B follows (spot A sent)");
				else Fail("QD6 feed: the host's answer " + Taming.LastAnswer + " (1 = follows, 0 = refused, -1 = none)");
				yield break;
			}
			if (what == "zone" || what == "zone2")
			{
				IslandObjectRef zr = ScObjOf(e, "qd6-zone");
				TriggerZone zone = zr != null ? zr.GetComponent<TriggerZone>() : null;
				if (zone == null) { Fail("QD6 " + what + ": the zone isn't here"); yield break; }
				if (ContentState.IsUsed(e, zone.StateKey)) { Fail("QD6 " + what + ": the zone is used already"); yield break; }
				PutPlayerNear(zr.transform, 6f);
				yield return new WaitForSeconds(1f);
				zone.Enter();
				bool usedNow = false;
				for (float t = 0f; t < 3f && !(usedNow = ContentState.IsUsed(e, zone.StateKey)); t += 0.1f) yield return new WaitForSeconds(0.1f);
				if (what == "zone")
				{
					bool freed = false;
					for (float t = 0f; t < 10f && !(freed = !ContentState.IsUsed(e, zone.StateKey)); t += 0.25f) yield return new WaitForSeconds(0.25f);
					if (usedNow && freed) Log("PASS: QD6 zone - used when walked in, ready again after the host's refusal");
					else Fail("QD6 zone: used at once " + usedNow + ", ready again after the refusal " + freed);
				}
				else
				{
					yield return new WaitForSeconds(4f);
					bool still = ContentState.IsUsed(e, zone.StateKey), signal = Behaviours.HasSignal(e, "qd6-zone");
					if (usedNow && still && signal) Log("PASS: QD6 zone2 - set off with the host's yes: used, the signal sent");
					else Fail("QD6 zone2: used " + usedNow + "/" + still + ", signal " + signal);
				}
				PutPlayerNear(zr.transform, 15f);
				yield break;
			}
			Fail("QD6: unknown part '" + what + "'");
		}

		static List<KeyValuePair<IslandObjectRef, CreatureSpawnPoint>> Qd6Spots(IslandWorldState.Entry e)
		{
			return Behaviours.RefsOf(e).Select(r => new KeyValuePair<IslandObjectRef, CreatureSpawnPoint>(r, r.GetComponent<CreatureSpawnPoint>()))
				.Where(x => x.Value != null && x.Value.Kind.Type == AI_NetworkBehaviourType.Goat).OrderBy(x => x.Key.Index).ToList();
		}
	}
}
