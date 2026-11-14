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
	}
}
