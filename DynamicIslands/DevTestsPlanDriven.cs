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
	// X6 (11-25): the plan-driven part of E6. CIPlanWorld/CIPlanCheck set the plan and the sailed distance by hand; this one
	// gives a world a plan through the New Game box or the console WorldPlan step, sails the raft for real (so the mod's own
	// distance counter and sail direction drive the rules), and checks what the director brings survives a save + reload.
	public static partial class DevTests
	{
		const string PdPlan = "CI PlanDriven";
		static readonly string[] PdIslands = { "cipd-a", "cipd-b", "cipd-c", "cipd-d" };
		static readonly string[] PdRules = { "pd_start", "pd_km1", "pd_km2" };
		static readonly float[] PdKm = { 0f, 1f, 2f };
		const string PdFar = "pd_far";

		static string PdPlanText()
		{
			return "description = X6 test plan (CIPlanDriven)\nrandom = off\n" +
				"rule = pd_start | island:cipd-a | start | ahead:450 | PD message start | PD Start\n" +
				"rule = pd_km1 | island:cipd-b | km:1 | ahead:450 | PD message one | PD One\n" +
				"rule = pd_km2 | island:cipd-c | km:2 | ahead:450 | PD message two | PD Two\n" +
				"rule = pd_far | island:cipd-d | km:60 | ahead:450 | PD message far | PD Far\n";
		}

		[ConsoleCommand(name: "CIPlanDriven", docs: "Dev (X6): a world plan run by the mod. setup (main menu: islands cipd-a..d + plan 'CI PlanDriven'); run (in a world, host: the plan is the world's - New Game box or WorldPlan - then sails for real until its start/km:1/km:2 rules bring their islands ahead); check (after a reload: plan and done rules kept, nothing brought twice); clean")]
		public static void PlanDrivenCommand(string[] args)
		{
			string a = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (a == "setup") PlanDrivenSetup();
			else if (a == "run") StartTest(PlanDrivenRun());
			else if (a == "check") StartTest(PlanDrivenCheck());
			else if (a == "clean") StartTest(PlanDrivenClean());
			else Fail("CIPlanDriven setup|run|check|clean");
		}

		static void PlanDrivenSetup()
		{
			bool ok = true;
			for (int i = 0; i < PdIslands.Length; i++) RuleIsland(PdIslands[i], "PD Isle " + i).Save(IslandSpawner.PathFor(PdIslands[i]));
			WorldPlan plan = WorldPlan.Parse(PdPlan, PdPlanText());
			plan.Save();
			Check(ref ok, PdIslands.All(n => File.Exists(IslandSpawner.PathFor(n))), "plan-driven setup: the 4 islands are saved");
			WorldPlan back = WorldPlan.Load(PdPlan);
			Check(ref ok, back != null && back.Rules.Count == 4 && !back.Random, "plan-driven setup: plan '" + PdPlan + "' saved and reads back (4 rules, random off)");
			if (ok) Log("PASS: plan-driven setup"); else Fail("plan-driven setup");
		}

		static IEnumerator PlanDrivenClean()
		{
			if (LoadSceneManager.IsGameSceneLoaded && Raft_Network.IsHost)
				foreach (string n in PdIslands) IslandWorldState.Remove(n);
			yield return DeleteIslandFiles(PdIslands.ToList());
			try { if (File.Exists(WorldPlan.PathFor(PdPlan))) File.Delete(WorldPlan.PathFor(PdPlan)); } catch (Exception e) { Log("  (plan file: " + e.Message + ")"); }
			Log("PASS: plan-driven clean");
		}

		class PdBring { public IslandWorldState.Entry Entry; public IntroRule Rule; public float Sailed, Distance, AngleToSail, AngleToMove, At; public string Announcement; public bool Loaded; }

		static IEnumerator PlanDrivenRun()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("plan-driven run: run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Log("Plan-driven run: plan '" + WorldDirector.PlanName + "', sailed " + WorldDirector.Sailed.ToString("F0") + " m, done [" + string.Join(",", WorldDirector.Done.ToArray()) + "]");
			Check(ref ok, string.Equals(WorldDirector.PlanName, PdPlan, StringComparison.OrdinalIgnoreCase), "the world follows the plan '" + PdPlan + "' (New Game box / WorldPlan step)");
			if (!ok) { Fail("plan-driven run: the world has plan '" + WorldDirector.PlanName + "'"); yield break; }
			Check(ref ok, WorldDirector.Plan != null && WorldDirector.Plan.Rules.Count == 4, "the world's plan has its 4 rules");
			Check(ref ok, !CustomIslandSpawner.Enabled, "random islands are off (the plan says random = off)");

			var brought = new Dictionary<string, PdBring>(StringComparer.OrdinalIgnoreCase);
			var extra = new List<string>();
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raft == null || raft.body == null) { Fail("no raft"); yield break; }
			Rigidbody body = raft.body;
			Vector3 dir = CustomIslandSpawner.SailDirection();
			dir = Flat(dir).sqrMagnitude > 0.01f ? Flat(dir).normalized : Vector3.forward;
			Action<IslandWorldState.Entry, IntroRule> onBrought = (e, r) =>
			{
				Vector3 rp = CustomIslandSpawner.RaftPosition ?? body.position;
				Vector3 off = Flat(e.Position - rp);
				var b = new PdBring
				{
					Entry = e, Rule = r, Sailed = WorldDirector.Sailed, Distance = off.magnitude, At = Time.realtimeSinceStartup,
					AngleToSail = Vector3.Angle(off, Flat(CustomIslandSpawner.SailDirection())), AngleToMove = Vector3.Angle(off, dir),
					Announcement = WorldDirector.LastAnnouncement ?? ""
				};
				if (r == null || brought.ContainsKey(r.Id)) extra.Add((r != null ? r.Id : "?") + ":" + e.Name);
				else brought[r.Id] = b;
				Log(string.Format("  brought rule '{0}' -> '{1}' at sailed {2:F0} m, {3:F0} m from the raft, {4:F0} deg off the sail direction ({5:F0} off the test's heading); banner: {6}",
					r != null ? r.Id : "?", e.Name, b.Sailed, b.Distance, b.AngleToSail, b.AngleToMove, b.Announcement));
			};
			WorldDirector.Brought += onBrought;
			// (this world may have met the rules already: a run after the plan's start rule)
			foreach (string id in PdRules) if (WorldDirector.Done.Contains(id)) Log("  (rule '" + id + "' was done before the run)");

			Network_Player player = RAPI.GetLocalPlayer();
			if (player != null) player.transform.position = body.position + Vector3.up * 3f;
			const float speed = 15f, maxSeconds = 600f;
			float t = 0f, nextLog = 15f, stallT = 0f, stallSailed = WorldDirector.Sailed;

			try
			{
				while (t < maxSeconds && PdRules.Any(id => !brought.ContainsKey(id) && !WorldDirector.Done.Contains(id)))
				{
					yield return new WaitForFixedUpdate();
					t += Time.fixedDeltaTime;
					KeepAlive(player);
					// Keep clear of the islands just brought (and any other custom island) - the raft sails around them
					Vector3 pos = body.position;
					foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
					{
						Vector3 to = Flat(e.Position - pos);
						if (to.magnitude < 350f && Vector3.Angle(to, dir) < 45f)
						{
							float side = Vector3.Cross(dir, to).y > 0 ? -1f : 1f;
							dir = Quaternion.Euler(0f, 50f * side, 0f) * dir;
							Log("  steering 50 deg around '" + e.Name + "' (" + to.magnitude.ToString("F0") + " m ahead)");
							break;
						}
					}
					body.MovePosition(pos + dir * speed * Time.fixedDeltaTime);
					foreach (PdBring b in brought.Values) if (!b.Loaded && b.Entry.Root != null) { b.Loaded = true; Log("  '" + b.Entry.Name + "' loaded " + (Time.realtimeSinceStartup - b.At).ToString("F0") + " s after it was brought"); }
					// Held by land or a rock: turn
					stallT += Time.fixedDeltaTime;
					if (stallT >= 8f)
					{
						if (WorldDirector.Sailed - stallSailed < 30f) { dir = Quaternion.Euler(0f, 70f, 0f) * dir; Log("  the raft is held (" + (WorldDirector.Sailed - stallSailed).ToString("F0") + " m in 8 s) - turning 70 deg"); }
						stallT = 0f; stallSailed = WorldDirector.Sailed;
					}
					if (t >= nextLog)
					{
						nextLog += 15f;
						Log(string.Format("  t={0:F0}s the mod counts {1:F0} m sailed; brought {2}/3; custom islands {3}", t, WorldDirector.Sailed, brought.Count, IslandWorldState.Islands.Count));
					}
				}
				// The islands load in (up to 90 s after their bring; the raft waits)
				float waitT = 0f;
				while (waitT < 90f && brought.Values.Any(b => !b.Loaded))
				{
					yield return new WaitForSecondsRealtime(1f);
					waitT += 1f;
					KeepAlive(player);
					foreach (PdBring b in brought.Values) if (!b.Loaded && b.Entry.Root != null) { b.Loaded = true; Log("  '" + b.Entry.Name + "' loaded " + (Time.realtimeSinceStartup - b.At).ToString("F0") + " s after it was brought"); }
				}
			}
			finally { WorldDirector.Brought -= onBrought; body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }

			for (int i = 0; i < PdRules.Length; i++)
			{
				string id = PdRules[i], island = PdIslands[i];
				PdBring b;
				if (!brought.TryGetValue(id, out b))
				{
					Check(ref ok, false, "rule '" + id + "' brought its island within " + maxSeconds + " s (sailed " + WorldDirector.Sailed.ToString("F0") + " m" + (WorldDirector.Done.Contains(id) ? "; it was done before the run" : "") + ")");
					continue;
				}
				Check(ref ok, string.Equals(b.Entry.Name, island, StringComparison.OrdinalIgnoreCase), "rule '" + id + "' brought '" + island + "' (got '" + b.Entry.Name + "')");
				Check(ref ok, b.Sailed + 1f >= PdKm[i] * 1000f, "rule '" + id + "' waited for " + PdKm[i] + " km sailed (" + b.Sailed.ToString("F0") + " m)");
				if (PdKm[i] > 0f) Check(ref ok, b.Sailed <= PdKm[i] * 1000f + 400f, "rule '" + id + "' came soon after " + PdKm[i] + " km (" + b.Sailed.ToString("F0") + " m)");
				Check(ref ok, b.Distance >= 250f && b.Distance <= 1300f, "rule '" + id + "': island " + b.Distance.ToString("F0") + " m from the raft (ahead:450, room found up to x2.4)");
				Check(ref ok, b.AngleToSail <= 100f, "rule '" + id + "': island ahead of the raft (" + b.AngleToSail.ToString("F0") + " deg off the sail direction)");
				Check(ref ok, b.Announcement.Contains(b.Rule.Label) && b.Announcement.Contains(b.Rule.Message), "rule '" + id + "': banner shows label + message (" + b.Announcement + ")");
				IntroRule by = ReturningIslands.RuleThatBrought(b.Entry);
				Check(ref ok, by != null && by.Id == id, "the Receiver knows rule '" + id + "' brought '" + island + "' (" + (by != null ? by.Id : "none") + ")");
				Check(ref ok, WorldDirector.Done.Contains(id), "rule '" + id + "' is done");
				Check(ref ok, b.Loaded, "'" + island + "' loaded after its bring");
				Check(ref ok, IslandWorldState.Islands.Count(e => string.Equals(e.Name, island, StringComparison.OrdinalIgnoreCase)) == 1, "'" + island + "' is in the world once");
			}
			Check(ref ok, extra.Count == 0, "no rule brought a second island (" + string.Join(", ", extra.ToArray()) + ")");
			Check(ref ok, !WorldDirector.Done.Contains(PdFar) && !IslandWorldState.Islands.Any(e => e.Name.Equals("cipd-d", StringComparison.OrdinalIgnoreCase)), "the km:60 rule waits");
			IslandWorldState.Save();
			Log("Plan-driven run: sailed " + WorldDirector.Sailed.ToString("F0") + " m in " + t.ToString("F0") + " s");
			if (ok) Log("PASS: plan driven run"); else Fail("plan driven run");
		}

		static IEnumerator PlanDrivenCheck()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("plan-driven check: run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Check(ref ok, string.Equals(WorldDirector.PlanName, PdPlan, StringComparison.OrdinalIgnoreCase), "after the reload the world still follows '" + PdPlan + "' (has '" + WorldDirector.PlanName + "')");
			Check(ref ok, !CustomIslandSpawner.Enabled, "after the reload random islands stay off");
			Check(ref ok, WorldDirector.Sailed >= 2000f, "after the reload the sailed distance is kept (" + WorldDirector.Sailed.ToString("F0") + " m)");
			foreach (string id in PdRules) Check(ref ok, WorldDirector.Done.Contains(id), "after the reload rule '" + id + "' is still done");
			for (int i = 0; i < PdRules.Length; i++)
			{
				string island = PdIslands[i];
				List<IslandWorldState.Entry> es = IslandWorldState.Islands.Where(e => string.Equals(e.Name, island, StringComparison.OrdinalIgnoreCase)).ToList();
				Check(ref ok, es.Count == 1, "after the reload '" + island + "' is in the world once (" + es.Count + ")");
				IntroRule by = es.Count == 1 ? ReturningIslands.RuleThatBrought(es[0]) : null;
				Check(ref ok, by != null && by.Id == PdRules[i], "after the reload the Receiver knows rule '" + PdRules[i] + "' brought it (" + (by != null ? by.Id : "none") + ")");
			}
			// Sail on a little: nothing is brought again
			var again = new List<string>();
			Action<IslandWorldState.Entry, IntroRule> onBrought = (e, r) => again.Add((r != null ? r.Id : "?") + ":" + e.Name);
			WorldDirector.Brought += onBrought;
			try { yield return SailRoutine(45f, 15f); }
			finally { WorldDirector.Brought -= onBrought; }
			Check(ref ok, again.Count == 0, "sailing on after the reload brings nothing again (" + string.Join(", ", again.ToArray()) + ")");
			Check(ref ok, !WorldDirector.Done.Contains(PdFar), "the km:60 rule still waits");
			if (ok) Log("PASS: plan driven check"); else Fail("plan driven check");
		}
	}
}
