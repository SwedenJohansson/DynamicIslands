using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Storyline progression (TODO 11-24, TEST_CATALOGUE SS1-SS9): a storyline made in the World Plans window with every kind
	/// of link from one story island to the next, then played step by step in a fresh world - each step's player action fires
	/// its trigger, the next island comes (and not before), is reachable, and its frequency / banner / journal page appears.
	/// Also CIStorySteps checks: one broken storyline per kind of fault the plan check finds - the report's text in plain
	/// words (the rule's name, label and island; how to fix it) and "Show rule" going to the field to fix.
	/// </summary>
	public static partial class DevTests
	{
		const string StepsPlan = "CI Story Steps", StepsCheckPlan = "CI Story Check", StepsNoQuest = "ciss-q";
		static readonly string[] StepsIslands = { "ciss-a", "ciss-b", "ciss-c", "ciss-d", "ciss-e", "ciss-f", "ciss-g", "ciss-h", "ciss-i", "ciss-j", "ciss-k", "ciss-l", "ciss-m" };
		/// <summary>The chain's state and frequencies after part1 (compared after the save and load).</summary>
		static string stepsSaved = "";
		static int stepsPass, stepsFail;
		static readonly List<string> stepsFailed = new List<string>();

		// (rule: id, island, story place, when, when's island/rule, when's arg, where, metres, near which, done when, Receiver name)
		static readonly string[][] StepsRules =
		{
			new[] { "s1", "ciss-a", "first", "start", "", "", "receiver", "600", "", "quest", "Step One" },
			new[] { "s2", "ciss-b", "after:s1", "km", "", "1", "receiver", "600", "", "visit", "Step Two" },
			new[] { "s3", "ciss-c", "after:s2", "day", "", "3", "receiver", "600", "", "step:1", "Step Three" },
			new[] { "s4", "ciss-d", "after:s3", "quest", "s3", "", "receiver", "600", "", "zone:camp", "Step Four" },
			new[] { "s5", "ciss-e", "after:s4", "zone", "s4", "gate", "receiver", "600", "", "signal:bell", "Step Five" },
			new[] { "s6", "ciss-f", "after:s5", "signal", "s5", "horn", "receiver", "600", "", "note:", "Step Six" },
			new[] { "s7", "ciss-g", "after:s6", "step", "s6", "1", "receiver", "600", "", "", "Step Seven" },
			new[] { "inst", "ciss-h", "instead:Vasagatan", "start", "", "", "receiver", "600", "", "quest", "Instead" },
			new[] { "aft", "ciss-i", "after:Balboa", "start", "", "", "receiver", "600", "", "quest", "After Balboa" },
			new[] { "bes", "ciss-j", "beside", "rule", "s2", "", "receiver", "600", "", "", "Beside" },
			new[] { "sv", "ciss-k", "", "visit", "s3", "", "near", "300", "s3", "", "" },
			new[] { "sq", "ciss-l", "", "quest", "s1", "", "ahead", "300", "", "", "" },
			new[] { "ss", "ciss-m", "", "rule", "sv", "", "sailing", "600", "", "", "" },
		};

		[ConsoleCommand(name: "CIStorySteps", docs: "Dev: storyline progression (SS rows). CIStorySteps make (editor) = the islands and the plan 'CI Story Steps' built in the World Plans window, every kind of link; checks (editor) = one broken storyline per kind of fault: the check report's text and Show rule; part1 (host, a fresh world 'CI ...' with the plan) = steps 1-3 played; part2 (after a save and load) = the rest to Caravan Town; all = part1 and part2 (two players); client (player 2) = every island of the chain seen; clean = the test's islands and plans removed")]
		public static void StoryStepsCommand(string[] args)
		{
			string part = args != null && args.Length > 0 ? args[0] : "make";
			StartTest(part == "checks" ? StepsChecks() : part == "part1" ? StepsPart1(true) : part == "part2" ? StepsPart2(true) : part == "all" ? StepsAll()
				: part == "client" ? StepsClient() : part == "clean" ? StepsClean() : StepsMake());
		}

		#region The islands

		/// <summary>A test island: zones camp and gate, a lever (signal bell), a horn (signal horn), a logbook note, a two-step quest
		/// (go to camp, ring the bell). ciss-i also has a radio ("frequency|Tangaroa|"); ciss-q has no quest.</summary>
		static void StepsMakeIslands()
		{
			foreach (string name in StepsIslands.Concat(new[] { StepsNoQuest }))
			{
				IslandFile f = RuleIsland(name, "Steps " + name.Substring(5).ToUpperInvariant());
				f.Objects.Add(ScObj(ContentCatalog.TriggerZone, ScDry(f, new Vector2(16, 0), 2), ObjectProps.ZoneId, "gate", ObjectProps.ZoneRadius, "4"));
				f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(0, 12), 3), BehaviourProps.Name, "lever", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "signal||bell"));
				f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(-12, 0), 4), BehaviourProps.Name, "horn", BehaviourProps.Use, "Blow", BehaviourProps.EventPrefix + "use", "signal||horn"));
				f.Objects.Add(ScObj("Note_Paper", ScDry(f, new Vector2(0, -12), 5), ObjectProps.NoteTitle, "Logbook", ObjectProps.NoteText, "The last entry: the bell, then the horn."));
				if (name == "ciss-i")
					f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(12, 12), 6), BehaviourProps.Name, "radio", BehaviourProps.Use, "Listen", BehaviourProps.EventPrefix + "use", ReceiverUnlock.Verb + "|Tangaroa|"));
				if (name == StepsNoQuest) { f.Props.Remove(IslandQuest.KeyTitle); f.Props.Remove(IslandQuest.KeySteps); }
				else new IslandQuest { Title = "Find the camp", Steps = { new IslandQuest.Step { Type = "reach", Target = "camp" }, new IslandQuest.Step { Type = QuestSteps.Signal, Target = "bell" } } }.To(f.Props);
				f.Save(IslandSpawner.PathFor(name));
			}
			IslandCache.Forget();
		}

		#endregion

		#region make - the storyline built in the World Plans window

		static IEnumerator StepsMake()
		{
			yield return WaitForEditor(false);
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			StepsMakeIslands();
			Check(ref ok, StepsIslands.All(n => System.IO.File.Exists(IslandSpawner.PathFor(n))), StepsIslands.Length + " test islands saved (ciss-a..m)");
			if (System.IO.File.Exists(WorldPlan.PathFor(StepsPlan))) System.IO.File.Delete(WorldPlan.PathFor(StepsPlan));

			WorldPlanWindow.Open();
			yield return null; yield return null;
			GameObject win = WindowObject(typeof(WorldPlanWindow));
			if (win == null) { Fail("story steps make: World Plans doesn't open"); yield break; }
			Check(ref ok, Click(win, "New..."), "New... clicked");
			yield return null; yield return null;
			Check(ref ok, ChoiceWindow.IsOpen && ChoiceWindow.PickValue(WorldPlanWindow.NewEmpty), "New...: an empty plan");
			yield return null; yield return null;
			GameObject prompt = WindowObject(typeof(TextPromptWindow));
			Check(ref ok, prompt != null && TypeInto(prompt, "Name", StepsPlan) && Click(prompt, "OK"), "the name prompt: '" + StepsPlan + "', OK");
			yield return null; yield return null;
			Check(ref ok, WorldPlanWindow.Current != null && WorldPlanWindow.Current.Name == StepsPlan && WorldPlanWindow.Current.Rules.Count == 0, "the new plan is open, empty");
			// (random islands off: only the storyline's islands come; Raft's story on - the Settings tab's switches)
			WorldPlanWindow.RecipeRandom(false);
			WorldPlanWindow.RecipeStory(true);
			yield return null;
			ClickTab(win, "Tab_Other");
			yield return null;

			for (int i = 0; i < StepsRules.Length; i++)
			{
				string[] s = StepsRules[i];
				int idx = i;
				Func<GameObject> card = () => { RectTransform c = WorldPlanWindow.CardFor(idx); return c != null ? c.gameObject : null; };
				// (+ Add a rule is on the Other islands tab only; the card of a story island showed the Story tab)
				ClickTab(win, "Tab_Other");
				yield return null;
				bool done = Click(win, "+ Add a rule");
				yield return null; yield return null;
				done &= WorldPlanWindow.Current.Rules.Count == i + 1 && card() != null;
				if (!done) { Check(ref ok, false, "rule " + (i + 1) + " '" + s[0] + "': + Add a rule gives no card"); continue; }
				done &= TypeInto(card(), "id", s[0]);
				done &= DropList.Pick(card(), "Drop_What", "island"); yield return null;
				done &= card() != null && TypeInto(card(), "island name", s[1]);
				if (s[2].Length > 0) { done &= card() != null && DropList.Pick(card(), "StoryPlace", s[2]); yield return null; yield return null; }
				done &= card() != null && DropList.Pick(card(), "Drop_When", s[3]); yield return null;
				string step = "id, island" + (s[2].Length > 0 ? ", place" : "") + ", when";
				// WHEN's island or rule: "right after a rule" from its list; an island with the ▾ (a rule of the plan or a saved island)
				if (s[4].Length > 0)
				{
					if (s[3] == "rule") done &= card() != null && DropList.Pick(card(), "Drop_WhenRef", s[4]);
					else
					{
						Button pick = card() != null ? card().GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == "Pick_WhenRef") : null;
						bool listed = false;
						if (pick != null)
						{
							pick.onClick.Invoke();
							yield return WaitFor(() => ChoiceWindow.IsOpen && ChoiceWindow.Values.Contains(s[4]), 10f);
							listed = ChoiceWindow.IsOpen && ChoiceWindow.PickValue(s[4]);
							if (ChoiceWindow.IsOpen) ChoiceWindow.Close();
						}
						done &= listed;
						if (!listed) Log("  (rule " + s[0] + ": '" + s[4] + "' not in the ▾ list: " + string.Join(", ", ChoiceWindow.Values.ToArray()) + ")");
					}
					yield return null; yield return null;
					step += ", its island";
				}
				// WHEN's arg: a number typed; a zone, signal or step count from the island's list
				if (s[5].Length > 0)
				{
					if (s[3] == "km" || s[3] == "day") done &= card() != null && TypeInto(card(), s[3], s[5]);
					else
					{
						DropdownButton d = card() != null ? card().GetComponentsInChildren<DropdownButton>(false).FirstOrDefault(x => x.name == "Drop_WhenArg") : null;
						Check(ref ok, d != null && d.Options.Any(o => o.Value == s[5]), "rule " + s[0] + ": WHEN's " + s[3] + " is a list of the island's (" + (d != null ? string.Join(", ", d.Options.Select(o => o.Value).ToArray()) : "no list") + ")");
						done &= d != null && DropList.Pick(card(), "Drop_WhenArg", s[5]);
					}
					yield return null;
					step += ", its " + s[3];
				}
				if (s[3] == "quest")
				{
					DropdownButton q = card() != null ? card().GetComponentsInChildren<DropdownButton>(false).FirstOrDefault(x => x.name == "Drop_WhenQuest") : null;
					Check(ref ok, q != null && q.Options.Any(o => o.Value == ""), "rule " + s[0] + ": which quest is a list (main quest first)");
					if (q != null && q.Value != "") { done &= DropList.Pick(card(), "Drop_WhenQuest", ""); yield return null; }
				}
				// WHERE (the main story's islands are on the Receiver by themselves)
				if (s[2].Length == 0)
				{
					done &= card() != null && DropList.Pick(card(), "Drop_Where", s[6]); yield return null;
					if (s[6] != "sailing") done &= card() != null && TypeInto(card(), "300", s[7]);
					if (s[8].Length > 0) done &= card() != null && TypeInto(card(), "where it happened", s[8]);
					step += ", where";
				}
				else if (WorldPlanWindow.Current.Rules[i].Where != "receiver") { done &= card() != null && DropList.Pick(card(), "Drop_Where", "receiver"); yield return null; }
				// STORY's "done when" (an island in the chain), and its arg from the island's list
				if (s[2].Length > 0 && s[2] != "beside")
				{
					string kind = s[9].Split(':')[0];
					done &= card() != null && DropList.Pick(card(), "Drop_Done", kind); yield return null;
					if (s[9].Contains(":"))
					{
						string arg = s[9].Substring(kind.Length + 1);
						if (kind == "note")
						{
							ChoiceWindow.Choice log = WorldPlanWindow.FieldChoices(i, "donearg").FirstOrDefault(c => (c.Label ?? "").Contains("Logbook"));
							arg = log != null ? log.Value : "";
							Check(ref ok, log != null, "rule " + s[0] + ": the island's notes listed for 'done when' (" + string.Join(", ", WorldPlanWindow.FieldChoices(i, "donearg").Select(c => c.Value + " " + c.Label).ToArray()) + ")");
						}
						done &= arg.Length > 0 && card() != null && DropList.Pick(card(), "Drop_DoneArg", arg); yield return null;
						StepsRules[i][9] = kind + ":" + arg;
					}
					step += ", done when";
				}
				if (s[10].Length > 0) done &= card() != null && TypeInto(card(), "Receiver name", s[10]);
				IntroRule r = WorldPlanWindow.Current.Rules[i];
				bool same = r.Id == s[0] && r.What == "island" && r.WhatArg == s[1] && r.When == s[3] && (s[4].Length == 0 && (s[3] == "km" || s[3] == "day" || s[3] == "start") || r.WhenRef == s[4]) && r.WhenArg == s[5] && r.Where == s[6] &&
					(s[2].Length == 0 || IntroRule.NormalPlace(r.StoryPlace) == IntroRule.NormalPlace(s[2])) && (s[2].Length == 0 || s[2] == "beside" || r.StoryDone == s[9]) &&
					(s[8].Length == 0 || r.WhereRef == s[8]) && (s[2].Length > 0 || s[6] == "sailing" || Mathf.Approximately(r.Distance, float.Parse(s[7]))) && r.Label == s[10];
				Check(ref ok, done && same, "rule " + (i + 1) + " '" + s[0] + "' made through its card (" + step + "): " + r.ToLine());
			}
			Check(ref ok, Click(win, "Check"), "Check clicked");
			yield return null; yield return null;
			List<PlanChecker.Finding> shown = PlanCheckWindow.Shown ?? new List<PlanChecker.Finding>();
			Check(ref ok, !shown.Any(f => f.Level == PlanChecker.Level.Problem), "Check finds no problem (" + string.Join(" | ", shown.Where(f => f.Level != PlanChecker.Level.Tip).Select(f => f.Level + ": " + f.Text).ToArray()) + ")");
			if (PlanCheckWindow.IsOpen) PlanCheckWindow.Close();
			yield return null;
			Check(ref ok, Click(win, "Save"), "Save clicked");
			yield return null;
			Check(ref ok, (WorldPlanWindow.LastSaveNotice ?? "").Length == 0, "Save says nothing about problems ('" + WorldPlanWindow.LastSaveNotice + "')");
			WorldPlan saved = WorldPlan.Load(StepsPlan);
			Check(ref ok, saved != null && saved.Rules.Count == StepsRules.Length && !saved.Random && saved.RaftStory, "saved: " + (saved != null ? saved.Rules.Count : 0) + " rules, random islands off, Raft's story on");
			if (saved != null)
			{
				string steps = Steps(StoryChain.BuildSteps(saved.RaftStory, saved.LeaveOut, saved.Rules));
				string want = RaftSteps("rule:s1", "rule:s2", "rule:s3", "rule:s4", "rule:s5", "rule:s6", "rule:s7", "RadioTower", "rule:inst", "Balboa", "rule:aft", "CaravanTown", "Tangaroa", "VarunaPoint", "Temperance", "Utopia");
				Check(ref ok, steps == want, "the saved storyline's chain: " + steps);
				Log("  saved plan:\n" + saved.ToText());
			}
			WorldPlanWindow.Close();
			yield return null;
			if (ok) Log("PASS: story steps make"); else Fail("story steps make");
		}

		#endregion

		#region part1 / part2 - the storyline played

		static IslandWorldState.Entry StepsEntry(string rule) { return IslandWorldState.Islands.FirstOrDefault(x => x.Rule.Equals(rule, StringComparison.OrdinalIgnoreCase)); }

		/// <summary>One step of the storyline: "STEP n kind: PASS/FAIL" (the runner counts them).</summary>
		static void StepResult(ref bool ok, string name, bool pass, string detail)
		{
			if (pass) { stepsPass++; Log("STEP " + name + ": PASS (" + detail + ")"); }
			else { stepsFail++; stepsFailed.Add(name); ok = false; Log("STEP " + name + ": FAIL (" + detail + ")"); }
		}

		/// <summary>Not before its trigger: the rule hasn't fired and its island isn't in the world.</summary>
		static bool NotYet(string rule) { StoryChain.Tick(); return !StoryChain.Fired.Contains(rule) && StepsEntry(rule) == null; }

		/// <summary>A rule on the Receiver fired: its banner gives its frequency and the journal keeps the page.</summary>
		static bool Announced(string rule)
		{
			string f = StoryChain.FrequencyOf(rule);
			return StoryChain.Fired.Contains(rule) && f != null && StoryChain.LastBanner != null && StoryChain.LastBanner.Contains(f) && StoryBook.Pages.Any(p => p.Key == "storyfreq:" + rule && p.Text.Contains(f));
		}

		/// <summary>The rule's island comes when the Receiver is tuned to it; a player walks onto it (reachable).</summary>
		static IEnumerator StepsTuneAndReach(string rule, Action<bool, string> result)
		{
			// (all round: the test's own islands crowd the spot ahead of the raft - "no free spot" is the spawner waiting
			// for room, which a player sailing on gets; the test doesn't sail)
			CustomIslandSpawner.TestAllRound = true;
			StoryChain.RetryNow(rule);
			try { yield return TuneTo(rule); }
			finally { CustomIslandSpawner.TestAllRound = false; }
			IslandWorldState.Entry e = StepsEntry(rule);
			if (e == null || e.Root == null) { result(false, "tuned to " + StoryChain.FrequencyOf(rule) + ": no island"); yield break; }
			yield return StepsReach(e, result);
		}

		static IEnumerator StepsReach(IslandWorldState.Entry e, Action<bool, string> result)
		{
			// (a dry, gentle spot clear of the island's trigger zones: landing in 'gate' set it off before its step - a
			// later "when zone" rule then came at once)
			Terrain t = e.Root.GetComponentInChildren<Terrain>();
			Vector3? spot = null;
			if (t != null)
			{
				TriggerZone[] zones = e.Root.GetComponentsInChildren<TriggerZone>(true);
				TerrainData td = t.terrainData;
				Vector3 mid = t.transform.position + new Vector3(td.size.x / 2f, 0f, td.size.z / 2f);
				float best = float.MaxValue;
				for (float dx = -60f; dx <= 60f; dx += 4f)
					for (float dz = -60f; dz <= 60f; dz += 4f)
					{
						Vector3 q = mid + new Vector3(dx, 0f, dz);
						Vector3 local = q - t.transform.position;
						float u = local.x / td.size.x, v = local.z / td.size.z;
						if (u < 0.05f || u > 0.95f || v < 0.05f || v > 0.95f || td.GetSteepness(u, v) > 25f) continue;
						q.y = t.transform.position.y + t.SampleHeight(q);
						if (q.y < 2f || zones.Any(z => new Vector2(z.transform.position.x - q.x, z.transform.position.z - q.z).magnitude < z.Radius + 8f)) continue;
						float far = dx * dx + dz * dz;
						if (far < best) { best = far; spot = q; }
					}
			}
			if (spot.HasValue) yield return StandAtRoutine(spot.Value + Vector3.up * 1.5f); else yield return StandRoutine(e.Root);
			yield return new WaitForSeconds(1f);
			Network_Player p = RAPI.GetLocalPlayer();
			bool on = p != null && t != null && p.transform.position.x >= t.transform.position.x && p.transform.position.z >= t.transform.position.z &&
				p.transform.position.x <= t.transform.position.x + t.terrainData.size.x && p.transform.position.z <= t.transform.position.z + t.terrainData.size.z &&
				p.transform.position.y > t.transform.position.y + t.SampleHeight(p.transform.position) - 2f;
			result(on, "'" + e.HostName + "' at " + e.Position.ToString("0") + (on ? ", a player stands on it" : ", the player isn't on it"));
		}

		static IEnumerator StepsAll()
		{
			yield return StepsPart1(false);
			yield return StepsPart2(false);
		}

		static IEnumerator StepsPart1(bool report)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("story steps part1: host, in a world"); yield break; }
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("story steps part1: only in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			bool ok = true, pass = false;
			string detail = "";
			Action<bool, string> got = (yes, why) => { pass = yes; detail = why; };
			stepsPass = 0; stepsFail = 0; stepsFailed.Clear();
			Check(ref ok, WorldDirector.Plan != null && WorldDirector.Plan.Name == StepsPlan && StoryChain.Active, "the fresh world has the plan '" + StepsPlan + "' and its story chain (" + (WorldDirector.Plan != null ? WorldDirector.Plan.Name : "none") + ")");
			Check(ref ok, Steps(StoryChain.Steps) == RaftSteps("rule:s1", "rule:s2", "rule:s3", "rule:s4", "rule:s5", "rule:s6", "rule:s7", "RadioTower", "rule:inst", "Balboa", "rule:aft", "CaravanTown", "Tangaroa", "VarunaPoint", "Temperance", "Utopia"),
				"the chain: " + Steps(StoryChain.Steps));
			if (!ok) { Fail("story steps part1: not the plan's world"); yield break; }
			WorldDirector.Sailed = 0f;
			SetDay(1);
			yield return new WaitForSeconds(2f);
			StoryChain.Tick();

			// 1. first, when the world starts: s1's frequency at once
			Check(ref ok, NotYet("s2") && NotYet("s3") && NotYet("s4") && NotYet("bes") && NotYet("sq"), "at the start: no later island yet");
			StepResult(ref ok, "1 first/start (receiver)", Announced("s1"), "banner: " + StoryChain.LastBanner);
			yield return StepsTuneAndReach("s1", got);
			StepResult(ref ok, "1 first/start island", pass, detail);

			// 2. s1 done when its quest is (camp, then the bell): s2 unlocked; a side island "when quest s1, ahead" comes
			Check(ref ok, !StoryChain.Done.Contains("rule:s1") && StepsEntry("sq") == null, "s1: reaching it isn't its quest");
			IslandWorldState.Entry a = StepsEntry("s1");
			if (a != null && a.Root != null) { ScEnterZone(a, "camp"); yield return new WaitForSeconds(1f); ScUse(a, "lever"); }
			yield return WaitFor(() => StoryChain.Done.Contains("rule:s1"), 20f);
			StepResult(ref ok, "2 done=quest", StoryChain.Done.Contains("rule:s1") && StoryChain.Unlocked.Contains("rule:s2"), "s1 done, s2 unlocked: " + ChainState());
			StepResult(ref ok, "2 km not before", NotYet("s2"), "s2 waits for 1 km (sailed " + WorldDirector.Sailed.ToString("0") + " m)");
			yield return WaitFor(() => StepsEntry("sq") != null && StepsEntry("sq").Root != null, 40f);
			IslandWorldState.Entry sq = StepsEntry("sq");
			StepResult(ref ok, "side when=quest where=ahead", sq != null && sq.Root != null, sq != null ? "'" + sq.HostName + "' came ahead" : "no island");

			// 3. s2 after 1 km sailed; the beside island after s2's island is there
			WorldDirector.Sailed = 1100f;
			StoryChain.Tick();
			yield return WaitFor(() => StoryChain.Fired.Contains("s2"), 20f);
			StepResult(ref ok, "3 when=km", Announced("s2"), "banner: " + StoryChain.LastBanner);
			StepResult(ref ok, "3 beside not before", NotYet("bes"), "'bes' waits for s2's island");
			yield return StepsTuneAndReach("s2", got);
			StepResult(ref ok, "3 km island", pass, detail);
			StoryChain.Tick();
			yield return WaitFor(() => StoryChain.Fired.Contains("bes"), 20f);
			StepResult(ref ok, "4 beside when=rule", Announced("bes"), "banner: " + StoryChain.LastBanner);
			yield return TuneTo("bes");
			IslandWorldState.Entry bes = StepsEntry("bes");
			StepResult(ref ok, "4 beside island", bes != null && bes.Root != null && StoryChain.BookRules.Any(r => r.Id == "bes"), bes != null ? "'" + bes.HostName + "' came, in the notebook" : "no island");
			// s2 done when players reach it
			IslandWorldState.Entry b = StepsEntry("s2");
			if (b != null && b.Root != null) yield return StandRoutine(b.Root);
			yield return WaitFor(() => StoryChain.Done.Contains("rule:s2"), 20f);
			StepResult(ref ok, "5 done=visit", StoryChain.Done.Contains("rule:s2") && StoryChain.Unlocked.Contains("rule:s3"), ChainState());
			StepResult(ref ok, "5 day not before", NotYet("s3"), "s3 waits for day 3 (day " + WorldManager.DayCounter + ")");

			// 4. s3 on day 3; a side island near s3 when players visit it, then one by chance while sailing after it
			SetDay(3);
			StoryChain.Tick();
			yield return WaitFor(() => StoryChain.Fired.Contains("s3"), 20f);
			StepResult(ref ok, "6 when=day", Announced("s3"), "banner: " + StoryChain.LastBanner);
			yield return TuneTo("s3");
			StepResult(ref ok, "6 visit not before", NotYet("sv"), "'sv' waits for a player on s3's island");
			IslandWorldState.Entry c = StepsEntry("s3");
			if (c != null && c.Root != null) yield return StepsReach(c, got); else got(false, "no island");
			StepResult(ref ok, "6 day island", pass, detail);
			yield return WaitFor(() => StepsEntry("sv") != null && StepsEntry("sv").Root != null, 40f);
			IslandWorldState.Entry sv = StepsEntry("sv");
			StepResult(ref ok, "7 side when=visit where=near", sv != null && sv.Root != null && c != null && FlatDistance(sv.Position, c.Position) < 1200f,
				sv != null ? "'" + sv.HostName + "' came " + (c != null ? FlatDistance(sv.Position, c.Position).ToString("0") + " m from s3's" : "") : "no island");
			StoryChain.Tick();
			StepResult(ref ok, "8 sailing not before", StepsEntry("ss") == null, "'ss' comes after some sailing");
			WorldDirector.Sailed += 2000f;
			StoryChain.Tick();
			yield return WaitFor(() => StepsEntry("ss") != null && StepsEntry("ss").Root != null, 40f);
			StepResult(ref ok, "8 side when=rule where=sailing", StepsEntry("ss") != null && StepsEntry("ss").Root != null, StepsEntry("ss") != null ? "'" + StepsEntry("ss").HostName + "' came" : "no island");
			// s3 done when its first quest step is (camp): s4 unlocked, but it waits for s3's whole quest
			if (c != null && c.Root != null) ScEnterZone(c, "camp");
			yield return WaitFor(() => StoryChain.Done.Contains("rule:s3"), 20f);
			StepResult(ref ok, "9 done=step", StoryChain.Done.Contains("rule:s3") && StoryChain.Unlocked.Contains("rule:s4"), ChainState());
			StepResult(ref ok, "9 quest not before", NotYet("s4"), "s4 waits for s3's quest");

			stepsSaved = ChainState() + "/" + string.Join(",", new[] { "s1", "s2", "s3", "bes" }.Select(StoryChain.FrequencyOf).ToArray());
			OnRaftCommand();
			IslandWorldState.Save();
			Log("STEPS state: " + stepsSaved);
			Log("STEPS part1: " + stepsPass + " PASS, " + stepsFail + " FAIL");
			if (stepsPass == 0) ok = false;
			if (!report) yield break;
			if (ok) Log("PASS: story steps part1"); else Fail("story steps part1");
		}

		static IEnumerator StepsPart2(bool report)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("story steps part2: host, in a world"); yield break; }
			yield return new WaitForSeconds(3f);
			yield return EnsureAlive();
			bool ok = true, pass = false;
			string detail = "";
			Action<bool, string> got = (yes, why) => { pass = yes; detail = why; };
			int before = stepsPass;
			// (after a save and a load: the same chain, steps done and frequencies)
			string now = ChainState() + "/" + string.Join(",", new[] { "s1", "s2", "s3", "bes" }.Select(StoryChain.FrequencyOf).ToArray());
			if (report) StepResult(ref ok, "10 save+load keeps progress", now == stepsSaved && stepsSaved.Length > 0, now);
			StepResult(ref ok, "10 quest still not before", NotYet("s4"), "s4 still waits for s3's quest");
			IslandWorldState.Entry c = StepsEntry("s3");
			// (part1 ends with the player back on the raft, over 1 km from s3's island: it is unloaded after the load until a
			// player goes back - in the water beside it)
			if (c != null && c.Root == null && RAPI.GetLocalPlayer() != null) yield return PutPlayer(RAPI.GetLocalPlayer(), new Vector3(c.Position.x, 0.5f, c.Position.z), true);
			yield return WaitFor(() => c != null && c.Root != null, 60f);
			if (report) StepResult(ref ok, "10 island loads again", c != null && c.Root != null, c != null ? "'" + c.HostName + "' at " + c.Position.ToString("0") + (c.Root != null ? " built when a player came back" : " not built") : "no entry for s3");
			if (c != null && c.Root != null) yield return StandRoutine(c.Root);
			if (c == null || c.Root == null) { Fail("story steps part2: s3's island isn't there (" + ChainState() + ")"); yield break; }

			// 5. s4 when s3's quest is done (the bell on s3's island)
			ScEnterZone(c, "camp");
			yield return new WaitForSeconds(1f);
			ScUse(c, "lever");
			yield return WaitFor(() => StoryChain.Fired.Contains("s4"), 20f);
			StepResult(ref ok, "11 when=quest", Announced("s4"), "banner: " + StoryChain.LastBanner);
			yield return StepsTuneAndReach("s4", got);
			StepResult(ref ok, "11 quest island", pass, detail);
			IslandWorldState.Entry d = StepsEntry("s4");
			// s4 done when its zone camp fires; s5 waits for its zone gate
			if (d != null && d.Root != null) ScEnterZone(d, "camp");
			yield return WaitFor(() => StoryChain.Done.Contains("rule:s4"), 20f);
			StepResult(ref ok, "12 done=zone", StoryChain.Done.Contains("rule:s4") && StoryChain.Unlocked.Contains("rule:s5"), ChainState());
			StepResult(ref ok, "12 zone not before", NotYet("s5"), "s5 waits for the zone 'gate'");

			// 6. s5 when s4's zone gate fires
			if (d != null && d.Root != null) ScEnterZone(d, "gate");
			yield return WaitFor(() => StoryChain.Fired.Contains("s5"), 20f);
			StepResult(ref ok, "13 when=zone", Announced("s5"), "banner: " + StoryChain.LastBanner);
			yield return StepsTuneAndReach("s5", got);
			StepResult(ref ok, "13 zone island", pass, detail);
			IslandWorldState.Entry e = StepsEntry("s5");
			// s5 done when its signal bell is sent (the lever); s6 waits for the horn
			if (e != null && e.Root != null) ScUse(e, "lever");
			yield return WaitFor(() => StoryChain.Done.Contains("rule:s5"), 20f);
			StepResult(ref ok, "14 done=signal", StoryChain.Done.Contains("rule:s5") && StoryChain.Unlocked.Contains("rule:s6"), ChainState());
			StepResult(ref ok, "14 signal not before", NotYet("s6"), "s6 waits for the signal 'horn'");

			// 7. s6 when s5's signal horn is sent
			if (e != null && e.Root != null) ScUse(e, "horn");
			yield return WaitFor(() => StoryChain.Fired.Contains("s6"), 20f);
			StepResult(ref ok, "15 when=signal", Announced("s6"), "banner: " + StoryChain.LastBanner);
			yield return StepsTuneAndReach("s6", got);
			StepResult(ref ok, "15 signal island", pass, detail);
			IslandWorldState.Entry f = StepsEntry("s6");
			// s6 done when its logbook is read; s7 waits for one quest step on s6's island
			if (f != null && f.Root != null) ScReadNote(f, "Logbook");
			yield return WaitFor(() => StoryChain.Done.Contains("rule:s6"), 20f);
			StepResult(ref ok, "16 done=note", StoryChain.Done.Contains("rule:s6") && StoryChain.Unlocked.Contains("rule:s7"), ChainState());
			StepResult(ref ok, "16 step not before", NotYet("s7"), "s7 waits for 1 quest step of s6's island");

			// 8. s7 when one step of s6's quest is done (camp)
			if (f != null && f.Root != null) ScEnterZone(f, "camp");
			yield return WaitFor(() => StoryChain.Fired.Contains("s7"), 20f);
			StepResult(ref ok, "17 when=step", Announced("s7"), "banner: " + StoryChain.LastBanner);
			yield return StepsTuneAndReach("s7", got);
			StepResult(ref ok, "17 step island", pass, detail);
			IslandWorldState.Entry g = StepsEntry("s7");
			// s7 done ("its quest is done or reached": it has a quest) -> Raft's Radio Tower, with a banner (no note told the players)
			StepResult(ref ok, "18 raft step not before", !ChainTypes().Contains(ChunkPointType.Landmark_RadioTower), "the Radio Tower waits for s7 (" + Names(ChainTypes()) + ")");
			if (g != null && g.Root != null) { ScEnterZone(g, "camp"); yield return new WaitForSeconds(1f); ScUse(g, "lever"); }
			yield return WaitFor(() => StoryChain.Done.Contains("rule:s7"), 20f);
			StepResult(ref ok, "18 done=default (quest) -> Raft's next", StoryChain.Done.Contains("rule:s7") && ChainTypes().Contains(ChunkPointType.Landmark_RadioTower) &&
				StoryChain.LastBanner != null && StoryChain.LastBanner.Contains("picks up a new frequency") && StoryBook.Pages.Any(p => p.Key == "storyfreq:raft:RadioTower"), Names(ChainTypes()) + "; " + StoryChain.LastBanner);

			// 9. instead of Vasagatan: the Radio Tower's note leads to the plan's island, never to Vasagatan
			StepResult(ref ok, "19 instead not before", NotYet("inst"), "'inst' waits for the Radio Tower's note");
			PlayNote(ChunkPointType.Landmark_Vasagatan);
			StoryChain.Tick();
			yield return WaitFor(() => StoryChain.Fired.Contains("inst"), 20f);
			StepResult(ref ok, "19 instead:Raft", Announced("inst") && !ChainTypes().Contains(ChunkPointType.Landmark_Vasagatan), "banner: " + StoryChain.LastBanner + " (" + Names(ChainTypes()) + ")");
			yield return StepsTuneAndReach("inst", got);
			StepResult(ref ok, "19 instead island", pass, detail);
			IslandWorldState.Entry h = StepsEntry("inst");
			if (h != null && h.Root != null) { ScEnterZone(h, "camp"); yield return new WaitForSeconds(1f); ScUse(h, "lever"); }
			yield return WaitFor(() => StoryChain.Done.Contains("rule:inst"), 20f);
			StepResult(ref ok, "20 instead done -> Balboa", StoryChain.Done.Contains("rule:inst") && ChainTypes().Contains(ChunkPointType.Landmark_Balboa) && StoryChain.LastBanner != null && StoryChain.LastBanner.Contains("new frequency"), Names(ChainTypes()));

			// 10. after Balboa: Balboa's note leads to the plan's island first, Caravan Town after it
			StepResult(ref ok, "21 after:Raft not before", NotYet("aft"), "'aft' waits for Balboa's note");
			PlayNote(ChunkPointType.Landmark_CaravanIsland);
			StoryChain.Tick();
			yield return WaitFor(() => StoryChain.Fired.Contains("aft"), 20f);
			StepResult(ref ok, "21 after:Raft", Announced("aft") && !ChainTypes().Contains(ChunkPointType.Landmark_CaravanIsland), "banner: " + StoryChain.LastBanner + " (" + Names(ChainTypes()) + ")");
			yield return StepsTuneAndReach("aft", got);
			StepResult(ref ok, "21 after:Raft island", pass, detail);
			IslandWorldState.Entry i = StepsEntry("aft");
			// its radio ("frequency|Tangaroa|"): Tangaroa's own step unlocks; Caravan Town still waits for the island's quest
			StepResult(ref ok, "22 frequency action not before", !ChainTypes().Contains(ChunkPointType.Landmark_Tangaroa), Names(ChainTypes()));
			if (i != null && i.Root != null) ScUse(i, "radio");
			yield return WaitFor(() => ChainTypes().Contains(ChunkPointType.Landmark_Tangaroa), 15f);
			StepResult(ref ok, "22 frequency|Tangaroa action", ChainTypes().Contains(ChunkPointType.Landmark_Tangaroa) && !ChainTypes().Contains(ChunkPointType.Landmark_CaravanIsland), Names(ChainTypes()));
			if (i != null && i.Root != null) { ScEnterZone(i, "camp"); yield return new WaitForSeconds(1f); ScUse(i, "lever"); }
			yield return WaitFor(() => StoryChain.Done.Contains("rule:aft"), 20f);
			StepResult(ref ok, "23 after:Raft done -> Caravan Town", StoryChain.Done.Contains("rule:aft") && ChainTypes().Contains(ChunkPointType.Landmark_CaravanIsland), Names(ChainTypes()));
			Check(ref ok, new[] { "s1", "s2", "s3", "s4", "s5", "s6", "s7", "inst", "aft", "bes" }.All(id => StoryChain.BookRules.Any(r => r.Id == id)), "the notebook has every main story island");

			OnRaftCommand();
			IslandWorldState.Save();
			Log("STEPS state: " + ChainState());
			Log("STEPS part2: " + (stepsPass - before) + " PASS, " + stepsFail + " FAIL (all parts)");
			if (stepsPass == 0) ok = false;
			if (ok) Log("PASS: story steps " + (report ? "part2" : "all (" + stepsPass + " steps)")); else Fail("story steps " + (report ? "part2" : "all") + ": " + stepsFail + " step(s) failed: " + string.Join("; ", stepsFailed.ToArray()));
		}

		/// <summary>Player 2: every island of the storyline is in their world and loads; the notebook has the main story's islands.</summary>
		static IEnumerator StepsClient()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || Raft_Network.IsHost) { Fail("story steps client: player 2, in the host's world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			foreach (string[] s in StepsRules)
			{
				string id = s[0];
				yield return WaitFor(() => StepsEntry(id) != null, 20f);
				IslandWorldState.Entry e = StepsEntry(id);
				if (e != null && e.Root == null)
				{
					// (far from the raft: it loads as a player comes near)
					Network_Player p = RAPI.GetLocalPlayer();
					if (p != null) { PlayerMove.To(p, e.Position + Vector3.up * 30f); KeepAlive(p); }
					yield return WaitFor(() => e.Root != null, 40f);
				}
				Check(ref ok, e != null && e.Root != null, "client: '" + id + "' (" + s[1] + ") " + (e == null ? "not in the world" : e.Root == null ? "known, doesn't load" : "in the world at " + e.Position.ToString("0")));
			}
			List<string> book = StoryChain.BookRules.Select(r => r.Id).ToList();
			Check(ref ok, new[] { "s1", "s2", "s3", "s4", "s5", "s6", "s7", "inst", "aft", "bes" }.All(book.Contains), "client: the notebook has the main story's islands (" + string.Join(",", book.ToArray()) + ")");
			Check(ref ok, ChainTypes().Contains(ChunkPointType.Landmark_CaravanIsland) && ChainTypes().Contains(ChunkPointType.Landmark_Tangaroa) && !ChainTypes().Contains(ChunkPointType.Landmark_Vasagatan),
				"client: the Receiver has the host's frequencies (" + Names(ChainTypes()) + ")");
			if (ok) Log("PASS: story steps client"); else Fail("story steps client");
		}

		static IEnumerator StepsClean()
		{
			if (LoadSceneManager.IsGameSceneLoaded && Raft_Network.IsHost)
			{
				IslandWorldState.RemoveIds(IslandWorldState.Islands.Where(x => x.HostName.StartsWith("ciss-")).Select(x => x.Id).ToList(), true);
				StoryChain.Reset();
				Dictionary<int, ChunkPointType> notes = StoryOrder.FrequencyNotes();
				NoteBook.unlockedNoteBookIndexes.RemoveAll(i => notes.ContainsKey(i));
				NoteBook.unlockedChunkPointType.RemoveAll(t => Chain.Contains(t) || (int)t >= StoryChain.ModTypeBase);
				WorldDirector.Done.Clear();
				WorldDirector.SetPlan(WorldPlan.RandomName, false);
				StoryChain.OnWorldRead();
				IslandWorldState.Save();
			}
			foreach (string f in StepsIslands.Concat(new[] { StepsNoQuest }))
				try { if (System.IO.File.Exists(IslandSpawner.PathFor(f))) System.IO.File.Delete(IslandSpawner.PathFor(f)); } catch { }
			foreach (string p in new[] { StepsPlan, StepsCheckPlan })
				try { if (System.IO.File.Exists(WorldPlan.PathFor(p))) System.IO.File.Delete(WorldPlan.PathFor(p)); } catch { }
			IslandCache.Forget();
			yield return null;
			Log("PASS: story steps clean");
		}

		#endregion

		#region checks - one broken storyline per kind of fault

		/// <summary>A broken storyline: its rule lines, the rule the fault is on, the field, the level, words the text has, the fix's start.</summary>
		class StepsCase
		{
			public string Name, Lines, Field, Phrase, FixStart, TypeId, TypePlace = "id";
			public int Rule;
			public PlanChecker.Level Level = PlanChecker.Level.Problem;
		}

		static StepsCase SC(string name, string lines, int rule, string field, string phrase, string fixStart, PlanChecker.Level level = PlanChecker.Level.Problem)
		{
			return new StepsCase { Name = name, Lines = lines, Rule = rule, Field = field, Phrase = phrase, FixStart = fixStart, Level = level };
		}

		static string[] StepsFieldControls(string field)
		{
			switch (field)
			{
				case "id": return new[] { "Field_Id" };
				case "whenref": return new[] { "Drop_WhenRef", "Field_WhenRef" };
				case "whenarg": return new[] { "Drop_WhenArg", "Field_WhenArg" };
				case "whenquest": return new[] { "Drop_WhenQuest", "Field_WhenQuest" };
				case "whatarg": return new[] { "Field_WhatArg", "Drop_What" };
				case "distance": return new[] { "Field_Distance" };
				case "place": return new[] { "StoryPlace" };
				case "donearg": return new[] { "Drop_DoneArg", "Field_DoneArg", "Drop_Done" };
			}
			return new string[0];
		}

		static IEnumerator StepsChecks()
		{
			yield return WaitForEditor(false);
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			if (!StepsIslands.All(n => System.IO.File.Exists(IslandSpawner.PathFor(n))) || !System.IO.File.Exists(IslandSpawner.PathFor(StepsNoQuest))) StepsMakeIslands();
			const string B = "rule = base | island:ciss-a | start | ahead:300 | | |\n";
			const string X = "rule = x | island:ciss-b | ";
			var cases = new List<StepsCase>
			{
				SC("rule: none chosen", B + X + "rule | ahead:300 | |\n", 1, "whenref", "waits for another rule but doesn't say which", "WHEN"),
				SC("rule: itself", B + X + "rule:x | ahead:300 | |\n", 1, "whenref", "waits for itself", "WHEN"),
				SC("rule: not in the plan", B + X + "rule:nope | ahead:300 | |\n", 1, "whenref", "waits for rule 'nope', which isn't in the plan", "WHEN"),
				// (a file can't leave it empty - "zone" reads as this island, "self" - so the field is cleared in the card)
				new StepsCase { Name = "island: none chosen (typed)", Lines = B + X + "zone:ciss-b:gate | ahead:300 | |\n", Rule = 1, Field = "whenref", Phrase = "doesn't say which island", FixStart = "WHEN", TypePlace = "rule id / island", TypeId = "" },
				SC("island: its own", B + X + "zone:x:camp | ahead:300 | |\n", 1, "whenref", "waits for its own island", "WHEN"),
				SC("island: self", B + X + "zone:self:camp | ahead:300 | |\n", 1, "whenref", "waits for \"self\"", "WHEN"),
				SC("island: unknown", B + X + "zone:nowhere:camp | ahead:300 | |\n", 1, "whenref", "no rule of this plan and no saved island has that name", "WHEN"),
				SC("island: saved, never brought", B + X + "zone:ciss-c:camp | ahead:300 | |\n", 1, "whenref", "but no rule of this plan brings it", "Add a rule that brings"),
				SC("steps: not a number", B + X + "step:base:abc | ahead:300 | |\n", 1, "whenarg", "isn't a number from 1 up", "WHEN"),
				SC("km: no number", B + X + "km:far | ahead:300 | |\n", 1, "whenarg", "has no number", "WHEN"),
				SC("quest: number too high", B + X + "quest:base:3 | ahead:300 | |\n", 1, "whenquest", "waits for quest 3 of", "WHEN"),
				SC("quest: island has none", B + "rule = q0 | island:" + StepsNoQuest + " | start | ahead:300 | |\n" + X + "quest:q0 | ahead:300 | |\n", 2, "whenref", "which has no quest", "WHEN"),
				SC("steps: more than the quest has", B + X + "step:base:5 | ahead:300 | |\n", 1, "whenarg", "but its quest has only 2", "WHEN"),
				SC("zone: none chosen", B + X + "zone:base: | ahead:300 | |\n", 1, "whenarg", "doesn't say which", "WHEN"),
				SC("zone: not on the island", B + X + "zone:base:nope | ahead:300 | |\n", 1, "whenarg", "waits for the zone 'nope'", "WHEN"),
				SC("signal: nothing sends it", B + X + "signal:base:nope | ahead:300 | |\n", 1, "whenarg", "but nothing there sends it", "WHEN"),
				SC("story done note: none chosen", B + X + "start | receiver:600 | | X | first | note:\n", 1, "donearg", "waits for a note of", "STORY > done when"),
				SC("story done note: no such title", B + X + "start | receiver:600 | | X | first | note:Nope\n", 1, "donearg", "which has no note with a text by that title", "STORY > done when"),
				SC("story done note: no such number", B + X + "start | receiver:600 | | X | first | note:99\n", 1, "donearg", "waits for note #99", "STORY > done when"),
				SC("story done zone: none chosen", B + X + "start | receiver:600 | | X | first | zone:\n", 1, "donearg", "waits for a trigger zone of", "STORY > done when"),
				SC("story place: can't be read", B + X + "start | receiver:600 | | X | middle | quest\n", 1, "place", "can't be read: the island is left out of the story", "STORY"),
				SC("story done: can't be read", B + X + "start | receiver:600 | | X | first | sometimes\n", 1, "donearg", "can't be read: the story counts the island's quest instead", "STORY", PlanChecker.Level.Warning),
				SC("distance: not a number", B + X + "start | ahead:far | |\n", 1, "distance", "isn't a number", "WHERE", PlanChecker.Level.Warning),
				SC("bring: no island", B + "rule = x | island: | start | ahead:300 | |\n", 1, "whatarg", "brings an island but doesn't say which", "BRING"),
				new StepsCase { Name = "name: none (typed)", Lines = B + X + "start | ahead:300 | |\n", Rule = 1, Field = "id", Phrase = "has no name", FixStart = "Rule 2", TypeId = "" },
				new StepsCase { Name = "name: twice (typed)", Lines = B + X + "start | ahead:300 | |\n", Rule = 1, Field = "id", Phrase = "has the same name as rule 1", FixStart = "Type another name", TypeId = "base" },
			};
			int passed = 0;
			foreach (StepsCase k in cases)
			{
				// (the file written as a builder or an old version wrote it: WorldPlan.Save would write back only what it could read)
				System.IO.Directory.CreateDirectory(WorldPlan.Folder);
				System.IO.File.WriteAllText(WorldPlan.PathFor(StepsCheckPlan), "description = A broken storyline (CIStorySteps checks)\nrandom = off\nstory = on\n" + k.Lines);
				WorldPlanWindow.Open(StepsCheckPlan);
				yield return null; yield return null;
				GameObject win = WindowObject(typeof(WorldPlanWindow));
				WorldPlan plan = WorldPlanWindow.Current;
				if (win == null || plan == null || plan.Name != StepsCheckPlan || plan.Rules.Count <= k.Rule) { Check(ref ok, false, "check '" + k.Name + "': the plan doesn't open with its rules (" + (plan != null ? plan.Rules.Count + " rules" : "none") + ")"); WorldPlanWindow.Close(); yield return null; continue; }
				if (k.TypeId != null)
				{
					// (the name typed into the card, as a builder types it)
					RectTransform card = WorldPlanWindow.CardFor(k.Rule);
					Check(ref ok, card != null && TypeInto(card.gameObject, k.TypePlace, k.TypeId), "check '" + k.Name + "': '" + k.TypeId + "' typed into rule " + (k.Rule + 1) + "'s " + k.TypePlace);
					yield return null;
				}
				Click(win, "Check");
				yield return null; yield return null;
				List<PlanChecker.Finding> shown = PlanCheckWindow.Shown ?? new List<PlanChecker.Finding>();
				string name = PlanChecker.RuleName(plan, k.Rule);
				PlanChecker.Finding f = shown.FirstOrDefault(x => x.Rule == k.Rule && x.Field == k.Field && x.Level == k.Level && x.Text.Contains(k.Phrase));
				bool text = f != null && f.Text.StartsWith(name) && f.Fix.StartsWith(k.FixStart);
				// the report's own row: its text, and Show rule going to the field to fix
				bool jumped = false;
				string field = "";
				GameObject report = PlanCheckWindow.IsOpen ? WindowObject(typeof(PlanCheckWindow)) : null;
				if (f != null && report != null)
				{
					RectTransform item = report.GetComponentsInChildren<RectTransform>(false).FirstOrDefault(r => r.name == "Finding" && r.GetComponentsInChildren<Text>(false).Any(t => t.name == "Text" && t.text == f.Text));
					Button show = item != null ? item.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == "Button_ShowRule" + (k.Rule + 1)) : null;
					if (show != null)
					{
						show.onClick.Invoke();
						yield return null; yield return null;
						field = WorldPlanWindow.LastShownField ?? "";
						jumped = !PlanCheckWindow.IsOpen && StepsFieldControls(k.Field).Contains(field);
					}
				}
				if (text && jumped) passed++;
				Check(ref ok, text && jumped, "check '" + k.Name + "': " + (f == null ? "no " + k.Level + " on " + k.Field + " with '" + k.Phrase + "' (" + string.Join(" | ", shown.Select(x => x.Level + "/" + x.Rule + "/" + x.Field + ": " + x.Text).ToArray()) + ")"
					: "\"" + f.Text + "\" - how to fix: \"" + f.Fix + "\"" + (text ? "" : " (not starting with \"" + name + "\" / \"" + k.FixStart + "\")") + "; Show rule -> " + (field.Length > 0 ? field : "nothing") + (jumped ? "" : " (want " + string.Join("/", StepsFieldControls(k.Field)) + ")")));
				if (k.TypeId == "base")
				{
					// Save with a problem: saved, and the notice says so (the report shows each one)
					Click(win, "Save");
					yield return null;
					Check(ref ok, (WorldPlanWindow.LastSaveNotice ?? "").Contains("problem"), "check: Save with a problem says so: '" + WorldPlanWindow.LastSaveNotice + "'");
				}
				if (PlanCheckWindow.IsOpen) PlanCheckWindow.Close();
				WorldPlanWindow.Close();
				yield return null;
			}
			if (System.IO.File.Exists(WorldPlan.PathFor(StepsCheckPlan))) System.IO.File.Delete(WorldPlan.PathFor(StepsCheckPlan));
			Log("STEPS checks: " + passed + "/" + cases.Count + " fault kinds shown in plain words, Show rule at the field");
			if (ok) Log("PASS: story steps checks"); else Fail("story steps checks");
		}

		#endregion
	}
}
