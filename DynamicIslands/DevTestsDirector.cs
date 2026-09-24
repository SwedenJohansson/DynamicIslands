using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>Tests for the world director: rules, world plans, map types.</summary>
	public static partial class DevTests
	{
		#region Rules (anywhere)

		[ConsoleCommand(name: "CIRuleTest", docs: "Dev: world director rules - reading and writing rule lines and plans, directions, the quest editor's 'bring an island' (in the editor also opens it)")]
		public static void RuleTest()
		{
			DynamicIslands.instance.StartCoroutine(RuleTestRoutine());
		}

		static IEnumerator RuleTestRoutine()
		{
			bool ok = true;
			string[] lines =
			{
				"start | island:Old camp | start | ahead:250 | You see smoke on the horizon. | Camp",
				"map | type:tropical | quest:start | near:start:600:north | The diary points north... | Treasure",
				"third | oneof:Skull Rock, Old camp | step:map:2 | near:map:800:any |  | ",
				"z | pool | zone:start:cave | ahead:300 | A zone fired |",
				"v | type:random | visit:map | near:self:700:135 | |",
				"k | island:Old camp | km:3 | ahead:400 | |",
				"d | island:Old camp | day:5 | ahead:400 | |",
				"r | island:Old camp | rule:z | near:z:500:west | |",
			};
			foreach (string line in lines)
			{
				IntroRule r = IntroRule.Parse(line);
				Check(ref ok, r != null, "parses: " + line);
				if (r == null) continue;
				string again = IntroRule.Parse(r.ToLine()).ToLine();
				Check(ref ok, again == r.ToLine(), "  written and read back the same: " + r.ToLine());
				Log("  " + r.Describe());
			}
			IntroRule map = IntroRule.Parse(lines[1]);
			Check(ref ok, map.What == "type" && map.WhatArg == "tropical" && map.When == "quest" && map.WhenRef == "start" && map.Where == "near" && map.WhereRef == "start" && map.Distance == 600f && map.Direction == "north" && map.Label == "Treasure", "the fields of a rule");
			IntroRule third = IntroRule.Parse(lines[2]);
			Check(ref ok, third.WhatArg == "Skull Rock, Old camp" && third.WhenArg == "2", "one-of lists and step numbers");
			Check(ref ok, IntroRule.Parse("x | nonsense:1 | start | ahead:10") == null && IntroRule.Parse("only | two") == null, "bad lines are refused");
			Check(ref ok, IntroRule.Parse("x | pool | start | ahead:1").Distance == 50f, "distances are kept in range");
			Check(ref ok, IntroRule.DirectionAngle("north-east") == 45f && float.IsNaN(IntroRule.DirectionAngle("any")) && IntroRule.DirectionAngle("135") == 135f, "direction angles");
			Check(ref ok, IntroRule.DirectionName(44f) == "north-east" && IntroRule.DirectionName(350f) == "north" && IntroRule.DirectionName(-90f) == "west", "direction names");

			WorldPlan plan = WorldPlan.Parse("test", "description = A test\nrandom = off\nrule = " + lines[0] + "\nrule = " + lines[1] + "\nrule = " + lines[0] + "\nrule = | island:X | start | ahead:300 | |\nrule = broken line");
			Check(ref ok, plan.Description == "A test" && !plan.Random && plan.Rules.Count == 4, "a plan: description, random off, 4 rules (a broken one left out)");
			Check(ref ok, plan.Rules.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4 && plan.Rules[3].Id == "rule4", "rule ids are made distinct (" + string.Join(", ", plan.Rules.Select(r => r.Id).ToArray()) + ")");
			WorldPlan back = WorldPlan.Parse("test", plan.ToText());
			Check(ref ok, back.ToText() == plan.ToText(), "a plan written and read back is the same");
			Check(ref ok, WorldPlan.Load(WorldPlan.RandomName).Random && !WorldPlan.Load(WorldPlan.NoneName).Random && WorldPlan.Load("no such plan") == null, "built-in plans");

			// The quest editor's rule: set, found, other rules kept, removed
			var props = new Dictionary<string, string> { { WorldDirector.IslandRulesKey, lines[3] } };
			var bring = new IntroRule { Id = QuestEditorWindow.BringRuleId, What = "island", WhatArg = "Old camp", When = "quest", WhenRef = IntroRule.Self, Where = "near", WhereRef = IntroRule.Self, Distance = 700f, Direction = "east", Message = "Go east", Label = "Camp" };
			QuestEditorWindow.SetQuestBringRule(props, bring);
			IntroRule found = QuestEditorWindow.QuestBringRule(props);
			Check(ref ok, found != null && found.ToLine() == bring.ToLine() && IntroRule.ParseLines(props[WorldDirector.IslandRulesKey]).Count == 2, "the quest's 'bring' rule is stored with the island's other rules");
			bring.Direction = "west";
			QuestEditorWindow.SetQuestBringRule(props, bring);
			Check(ref ok, IntroRule.ParseLines(props[WorldDirector.IslandRulesKey]).Count == 2 && QuestEditorWindow.QuestBringRule(props).Direction == "west", "changing it replaces it");
			QuestEditorWindow.SetQuestBringRule(props, null);
			Check(ref ok, QuestEditorWindow.QuestBringRule(props) == null && props[WorldDirector.IslandRulesKey] == IntroRule.Parse(lines[3]).ToLine(), "removing it keeps the other rule");

			if (DynamicIslands.InEditor())
			{
				yield return WaitForEditor(false);
				var saved = new Dictionary<string, string>(DynamicIslands.currentIslandProps);
				DynamicIslands.currentIslandProps.Clear();
				TestQuest().To(DynamicIslands.currentIslandProps);
				bring.Direction = "north";
				QuestEditorWindow.SetQuestBringRule(DynamicIslands.currentIslandProps, bring);
				QuestEditorWindow.Open();
				yield return new WaitForSecondsRealtime(0.5f);
				Check(ref ok, QuestEditorWindow.IsOpen, "the quest editor opens with the rule");
				Screenshot(new[] { "quest_bring" });
				yield return new WaitForSecondsRealtime(0.5f);
				QuestEditorWindow.Close();
				DynamicIslands.currentIslandProps.Clear();
				foreach (var kv in saved) DynamicIslands.currentIslandProps[kv.Key] = kv.Value;
			}
			if (ok) Log("PASS: world director rules"); else Fail("world director rules");
		}

		#endregion

		#region A quest brings an island (in a world)

		/// <summary>A copy of the sample island with a zone "camp" in the middle, a one-step quest ("go to camp") and these rules.</summary>
		static IslandFile RuleIsland(string name, string title, params IntroRule[] rules)
		{
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor("generated_sample"));
			f.Name = name;
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 10f);
			f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = new Vector3(c.x, f.Heights[Mathf.RoundToInt(c.y / step), Mathf.RoundToInt(c.x / step)] * f.TerrainSize.y, c.y),
				Props = new Dictionary<string, string> { { ObjectProps.ZoneId, "camp" }, { ObjectProps.ZoneRadius, "5" } } });
			new IslandQuest { Title = "Find the camp", Steps = { new IslandQuest.Step { Type = "reach", Target = "camp" } } }.To(f.Props);
			f.Props[IslandProps.Title] = title;
			if (rules.Length > 0) f.Props[WorldDirector.IslandRulesKey] = IntroRule.ToLines(rules);
			return f;
		}

		static float FlatDistance(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }

		[ConsoleCommand(name: "CIQuestSpawnTest", docs: "Dev, in game (host): an island's quest is done -> the next island appears 600 m north of it, a zone brings a generated one; announcements, Receiver labels, saved state, Raft keeps its islands clear")]
		public static void QuestSpawnTest()
		{
			DynamicIslands.instance.StartCoroutine(QuestSpawnTestRoutine());
		}

		static IEnumerator QuestSpawnTestRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			var created = new List<string> { "ciqsrc", "ciqnext" };

			// The next island (a plain copy), and the first one with two rules: its quest brings "ciqnext" 600 m north,
			// its zone "camp" brings a new tropical island somewhere around it
			IslandFile next = RuleIsland("ciqnext", "Next Isle");
			next.Save(IslandSpawner.PathFor("ciqnext"));
			var questRule = new IntroRule { Id = QuestEditorWindow.BringRuleId, What = "island", WhatArg = "ciqnext", When = "quest", WhenRef = IntroRule.Self, Where = "near", WhereRef = IntroRule.Self, Distance = 600f, Direction = "north", Message = "The diary points north.", Label = "Next" };
			var zoneRule = new IntroRule { Id = "zone-gen", What = "type", WhatArg = "tropical", When = "zone", WhenRef = IntroRule.Self, WhenArg = "camp", Where = "near", WhereRef = IntroRule.Self, Distance = 700f, Direction = "any" };
			RuleIsland("ciqsrc", "Source Isle", questRule, zoneRule).Save(IslandSpawner.PathFor("ciqsrc"));

			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius("ciqsrc"), 390f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile("ciqsrc", spot.Value, true);
			IslandWorldState.Entry src = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (src == null || src.Root == null) { Fail("the first island did not spawn"); yield break; }
			yield return StandRoutine(src.Root);
			yield return new WaitForSeconds(1f);
			Check(ref ok, WorldDirector.RulesOf(src).Count == 2, "the island carries its 2 rules");
			WorldDirector.Evaluate();
			Check(ref ok, IslandWorldState.Islands.Count == before + 1, "nothing is brought before the quest or the zone");

			var sent = new List<IslandNetMessage>();
			var brought = new List<KeyValuePair<IslandWorldState.Entry, IntroRule>>();
			Action<IslandWorldState.Entry, IntroRule> onBrought = (e, r) => brought.Add(new KeyValuePair<IslandWorldState.Entry, IntroRule>(e, r));
			WorldDirector.Brought += onBrought;
			IslandNetwork.Loopback = m => sent.Add(m);
			try
			{
				// Walking into "camp": the zone fires and the quest (one step) is done
				src.Root.GetComponentInChildren<TriggerZone>().Enter();
				Check(ref ok, QuestTracker.StepOf(src) == 1, "the quest is done");
				WorldDirector.Evaluate();
			}
			finally { IslandNetwork.Loopback = null; WorldDirector.Brought -= onBrought; }

			KeyValuePair<IslandWorldState.Entry, IntroRule> byQuest = brought.FirstOrDefault(b => b.Value.Id == QuestEditorWindow.BringRuleId);
			KeyValuePair<IslandWorldState.Entry, IntroRule> byZone = brought.FirstOrDefault(b => b.Value.Id == "zone-gen");
			Check(ref ok, brought.Count == 2 && byQuest.Key != null && byZone.Key != null, brought.Count + " island(s) brought (quest and zone)");
			if (byQuest.Key != null)
			{
				IslandWorldState.Entry e = byQuest.Key;
				Vector3 d = e.Position - src.Position;
				float angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
				Check(ref ok, e.HostName == "ciqnext" && e.Label == "Next" && e.Rule == QuestEditorWindow.BringRuleId, "'ciqnext' with its Receiver label 'Next'");
				Check(ref ok, FlatDistance(e.Position, src.Position) >= 590f && FlatDistance(e.Position, src.Position) < 1500f && Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 50f,
					FlatDistance(e.Position, src.Position).ToString("F0") + " m from the first island, " + angle.ToString("F0") + " degrees (north = 0)");
				Check(ref ok, CustomIslandSpawner.OverlapsRaftIsland(e.Position, CustomIslandSpawner.LandRadius(e.Name)) == null, "clear of Raft's islands");
				Check(ref ok, sent.Any(m => m.Kind == IslandNetMessage.Islands && m.Ids.Contains(e.Id) && m.Labels != null && m.Labels.Contains("Next")), "clients are told (with the label)");
				Check(ref ok, sent.Any(m => m.Kind == IslandNetMessage.Announce && m.Name == "Next" && m.Data == "The diary points north."), "clients get the announcement");
				Check(ref ok, WorldDirector.LastAnnouncement != null && WorldDirector.LastAnnouncement.Contains("m to the"), "the banner says where: " + WorldDirector.LastAnnouncement);
				Screenshot(new[] { "quest_spawn_banner" });

				// Raft's own islands keep clear of it from now on
				ChunkManager cm = ComponentManager<ChunkManager>.Value;
				ChunkPoint any = cm != null ? cm.GetAllChunkPointsList().FirstOrDefault(c => c.rule != null && c.rule.name.IndexOf("FloatingRaft", StringComparison.OrdinalIgnoreCase) < 0) : null;
				if (any != null)
				{
					var test = new ChunkPoint { worldPosition = e.Position, rule = any.rule };
					MethodInfo fits = typeof(ChunkManager).GetMethod("DoesPointFit", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(ChunkPoint) }, null);
					bool fit = fits != null && (bool)fits.Invoke(cm, new object[] { test });
					Check(ref ok, fits != null && !fit, "Raft can't put its '" + any.rule.name + "' where the new island is");
				}
				else Log("  (no chunk point to test Raft's placement with)");
			}
			if (byZone.Key != null)
			{
				IslandWorldState.Entry g = byZone.Key;
				created.Add(g.HostName);
				Check(ref ok, g.HostName.StartsWith("gen-tropical-") && FlatDistance(g.Position, src.Position) >= 690f, "the zone brought a generated tropical island ('" + g.HostName + "', " + FlatDistance(g.Position, src.Position).ToString("F0") + " m away)");
				float t0 = Time.realtimeSinceStartup;
				while (!File.Exists(IslandSpawner.PathFor(g.HostName)) && Time.realtimeSinceStartup - t0 < 20f) yield return new WaitForSeconds(0.5f);
				Check(ref ok, File.Exists(IslandSpawner.PathFor(g.HostName)), "its file was generated");
			}

			// Each rule fires once; the state says so, and it's saved with the world
			int count = IslandWorldState.Islands.Count;
			WorldDirector.Evaluate();
			Check(ref ok, IslandWorldState.Islands.Count == count, "rules don't fire twice");
			Check(ref ok, src.State.ContainsKey(WorldDirector.RuleKeyBase) && src.State.ContainsKey(WorldDirector.RuleKeyBase + 1), "both rules are marked done in the island's state");
			Check(ref ok, src.State.ContainsKey(WorldDirector.VisitKey), "the island is marked visited");
			IslandWorldState.Save();
			string worldFile = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), SaveAndLoad.WorldGuid + ".txt");
			string text = File.Exists(worldFile) ? File.ReadAllText(worldFile) : "";
			Check(ref ok, text.Contains("|" + QuestEditorWindow.BringRuleId + "|Next") && text.Contains("@plan="), "the world file keeps the rule, label and plan");
			Log(string.Join("\n", WorldDirector.Describe().Split('\n').Take(6).ToArray()));

			// Clean up
			yield return new WaitForSeconds(1f);
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(before).Select(e => e.Id).ToList(), true);
			foreach (string n in created) if (File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n));
			IslandWorldState.Save();
			if (ok) Log("PASS: a quest brings an island"); else Fail("a quest brings an island");
		}

		#endregion

		#region World plans

		const string TestPlan = "ci plan";

		[ConsoleCommand(name: "CIPlanTest", docs: "Dev, editor: the world plan window - samples, a test plan's rule cards, Check, the map, the island's own rules; screenshots")]
		public static void PlanTest()
		{
			DynamicIslands.instance.StartCoroutine(PlanTestRoutine());
		}

		static IEnumerator PlanTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			WorldPlanWindow.EnsureSamples();
			List<string> plans = WorldPlan.All();
			Check(ref ok, plans.Contains("Island hopping") && plans.Contains("Growing sea") && plans[0] == WorldPlan.RandomName, "plans: " + string.Join(", ", plans.ToArray()));
			WorldPlan hopping = WorldPlan.Load("Island hopping");
			Check(ref ok, hopping != null && hopping.Rules.Count == 4 && hopping.Random, "the sample 'Island hopping' has 4 rules");

			WorldPlan p = WorldPlan.Parse(TestPlan, "description = Test plan\nrandom = off\n" +
				"rule = start | island:generated_sample | start | ahead:300 | Hello | Start\n" +
				"rule = next | type:snowy | quest:start | near:start:700:east | East | Snow\n" +
				"rule = broken | island:no such island | zone:start:nowhere | near:nobody:500:any | |\n");
			p.Save();
			WorldPlanWindow.Open(TestPlan);
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, WorldPlanWindow.IsOpen, "the window opens on the test plan");
			Transform window = EditorUI.Canvas.transform.Find("WorldPlanWindow");
			int cards = window != null ? window.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Rule") : 0;
			Check(ref ok, cards == 3, cards + " rule cards");
			// Check lists the broken rule's problems
			window.SendMessage("Check", SendMessageOptions.DontRequireReceiver);
			yield return null;
			Text problems = window.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.text.Contains("is placed near"));
			Check(ref ok, problems != null && problems.text.Contains("nobody") && !problems.text.Contains("1. "), "Check finds the broken rule's problems only: " + (problems != null ? problems.text.Replace("\n", " / ") : "none"));
			int dots = window.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Dot");
			Check(ref ok, dots == 4, "the map shows the raft and 3 islands (" + dots + " dots)");
			Screenshot(new[] { "world_plans" });
			yield return new WaitForSecondsRealtime(0.5f);
			WorldPlanWindow.Close();

			// The island's own rules in the same window
			var saved = new Dictionary<string, string>(DynamicIslands.currentIslandProps);
			DynamicIslands.currentIslandProps.Clear();
			TestQuest().To(DynamicIslands.currentIslandProps);
			WorldDirector.SetRulesInProps(DynamicIslands.currentIslandProps, new[] { IntroRule.Parse("reward | type:random | quest:self | near:self:600:north | Well done | Reward") });
			WorldPlanWindow.OpenIsland();
			yield return new WaitForSecondsRealtime(0.5f);
			cards = window.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Rule");
			Check(ref ok, cards == 1, "island rules: 1 card");
			Screenshot(new[] { "island_rules" });
			yield return new WaitForSecondsRealtime(0.5f);
			WorldPlanWindow.Close();
			DynamicIslands.currentIslandProps.Clear();
			foreach (var kv in saved) DynamicIslands.currentIslandProps[kv.Key] = kv.Value;
			File.Delete(WorldPlan.PathFor(TestPlan));
			if (ok) Log("PASS: world plan window"); else Fail("world plan window");
		}

		[ConsoleCommand(name: "CIPlanWorld", docs: "Dev, in game (host): plays a test world plan - start island, a quest brings a snowy island east of it, km sailed, a visit, one-of; saved state; multiplayer messages")]
		public static void PlanWorld()
		{
			DynamicIslands.instance.StartCoroutine(PlanWorldRoutine());
		}

		static IEnumerator PlanWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			string planBefore = WorldDirector.PlanName;
			bool autoBefore = CustomIslandSpawner.Enabled;
			float sailedBefore = WorldDirector.Sailed;
			var doneBefore = WorldDirector.Done.ToList();
			int before = IslandWorldState.Islands.Count;
			var created = new List<string> { "ciplan1" };

			RuleIsland("ciplan1", "Plan Isle").Save(IslandSpawner.PathFor("ciplan1"));
			WorldPlan.Parse(TestPlan, "random = off\n" +
				"rule = start | island:ciplan1 | start | ahead:300 | Welcome to the test plan | Start\n" +
				"rule = second | type:snowy | quest:start | near:start:700:east | Go east | Snow\n" +
				"rule = third | type:desert | km:5 | ahead:400 | Hot | Desert\n" +
				"rule = fourth | oneof:no such island, ciplan1 | visit:second | near:second:600:south | | Again\n").Save();
			Check(ref ok, WorldDirector.SetPlan(TestPlan, true) && !CustomIslandSpawner.Enabled, "the world gets the plan (random islands off, as the plan says)");

			var sent = new List<IslandNetMessage>();
			IslandNetwork.Loopback = m => sent.Add(m);
			try
			{
				WorldDirector.Evaluate();
				IslandWorldState.Entry start = WorldDirector.Refs("start", null).FirstOrDefault();
				Check(ref ok, start != null && start.HostName == "ciplan1" && WorldDirector.Done.Contains("start") && IslandWorldState.Islands.Count == before + 1, "'start' brings ciplan1 at once, and only it");
				if (start == null) yield break;
				Check(ref ok, FlatDistance(start.Position, raftPos.Value) > 250f && FlatDistance(start.Position, raftPos.Value) < 700f, "ahead of the raft (" + FlatDistance(start.Position, raftPos.Value).ToString("F0") + " m)");
				float t0 = Time.realtimeSinceStartup;
				while (start.Root == null && Time.realtimeSinceStartup - t0 < 30f) yield return new WaitForSeconds(0.5f);
				if (start.Root == null) { Fail("the start island did not load"); yield break; }
				yield return StandRoutine(start.Root);

				start.Root.GetComponentInChildren<TriggerZone>().Enter();
				WorldDirector.Evaluate();
				IslandWorldState.Entry second = WorldDirector.Refs("second", null).FirstOrDefault();
				if (second != null) created.Add(second.HostName);
				float angle = second != null ? Mathf.Atan2(second.Position.x - start.Position.x, second.Position.z - start.Position.z) * Mathf.Rad2Deg : 0f;
				Check(ref ok, second != null && second.HostName.StartsWith("gen-snowy-") && Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < 45f && second.Label == "Snow",
					"the quest brings a snowy island east of the start (" + angle.ToString("F0") + " degrees)");
				Check(ref ok, WorldDirector.Refs("third", null).Count == 0, "'third' waits for 5 km");

				WorldDirector.Sailed = 5001f;
				WorldDirector.Evaluate();
				IslandWorldState.Entry third = WorldDirector.Refs("third", null).FirstOrDefault();
				if (third != null) created.Add(third.HostName);
				Check(ref ok, third != null && third.HostName.StartsWith("gen-desert-"), "5 km sailed bring a desert island");

				if (second != null) second.State[WorldDirector.VisitKey] = new ObjectState { Active = true };
				WorldDirector.Evaluate();
				IslandWorldState.Entry fourth = WorldDirector.Refs("fourth", null).FirstOrDefault();
				Check(ref ok, fourth != null && fourth.HostName == "ciplan1" && second != null && FlatDistance(fourth.Position, second.Position) >= 590f, "visiting the snowy island brings one of the list (the one that exists) south of it");
				Check(ref ok, WorldDirector.Done.SetEquals(new[] { "start", "second", "third", "fourth" }), "all 4 rules done: " + string.Join(", ", WorldDirector.Done.ToArray()));

				// Generated islands reach clients once their files exist
				t0 = Time.realtimeSinceStartup;
				while (created.Skip(1).Any(n => !File.Exists(IslandSpawner.PathFor(n))) && Time.realtimeSinceStartup - t0 < 30f) yield return new WaitForSeconds(0.5f);
				yield return new WaitForSeconds(0.5f);
				var told = new HashSet<int>(sent.Where(m => m.Kind == IslandNetMessage.Islands).SelectMany(m => m.Ids));
				Check(ref ok, IslandWorldState.Islands.Skip(before).All(e => told.Contains(e.Id)), "clients are told about all 4 islands");
				Check(ref ok, sent.Count(m => m.Kind == IslandNetMessage.Announce) == 4, sent.Count(m => m.Kind == IslandNetMessage.Announce) + " announcements sent");
				Screenshot(new[] { "plan_world" });
			}
			finally { IslandNetwork.Loopback = null; }

			// Saved with the world, and read back
			IslandWorldState.Save();
			string worldFile = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), SaveAndLoad.WorldGuid + ".txt");
			string[] lines = File.Exists(worldFile) ? File.ReadAllLines(worldFile) : new string[0];
			Check(ref ok, lines.Contains("@plan=" + TestPlan) && lines.Contains("@auto=off") && lines.Any(l => l.StartsWith("@done=") && l.Contains("fourth")), "the world file keeps the plan, random off and the done rules");
			WorldDirector.Reset();
			foreach (string l in lines.Where(l => l.StartsWith("@")))
			{
				int eq = l.IndexOf('=');
				WorldDirector.ReadLine(l.Substring(1, eq - 1), l.Substring(eq + 1));
			}
			Check(ref ok, WorldDirector.PlanName == TestPlan && WorldDirector.Done.Count == 4 && WorldDirector.Sailed >= 5000f, "read back: plan, 4 done rules, km sailed");
			Log(WorldDirector.Describe());

			// Clean up: the world's own plan and state come back
			yield return new WaitForSeconds(1f);
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(before).Select(e => e.Id).ToList(), true);
			foreach (string n in created) if (File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n));
			File.Delete(WorldPlan.PathFor(TestPlan));
			WorldDirector.SetPlan(planBefore, false);
			CustomIslandSpawner.Enabled = autoBefore;
			WorldDirector.Sailed = sailedBefore;
			WorldDirector.Done.Clear();
			foreach (string d in doneBefore) WorldDirector.Done.Add(d);
			IslandWorldState.Save();
			if (ok) Log("PASS: a world plan in a world"); else Fail("a world plan in a world");
		}

		#region Map types

		[ConsoleCommand(name: "CIMapTypeTest", docs: "Dev, editor: every map type from a fixed seed - land inside the build area (above or under the sea as it should be), same seed = same island, content and quest targets present; opens a few and takes screenshots")]
		public static void MapTypeTest()
		{
			DynamicIslands.instance.StartCoroutine(MapTypeTestRoutine());
		}

		static string Fingerprint(IslandFile f)
		{
			double sum = 0;
			for (int z = 0; z < f.HeightmapResolution; z += 7)
				for (int x = 0; x < f.HeightmapResolution; x += 7) sum += f.Heights[z, x] * (1 + (x * 31 + z) % 97);
			return sum.ToString("F4") + "/" + f.Objects.Count + "/" + string.Join(",", f.Objects.Where(o => o.Props != null).Select(o => o.Name + "@" + o.Position.ToString("F1")).ToArray()) + "/" + IslandObjectState.Encode(new Dictionary<int, ObjectState>()) + string.Join(";", f.Props.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value).ToArray());
		}

		static IEnumerator MapTypeTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			foreach (MapType type in MapTypes.All)
			{
				float t0 = Time.realtimeSinceStartup;
				float elevation, elevation2;
				IslandGenSettings s = MapTypes.Roll(type, new System.Random(4242), out elevation);
				IslandFile f = MapTypes.Create(type, s, elevation, "citype");
				float ms = (Time.realtimeSinceStartup - t0) * 1000f;
				IslandGenSettings s2 = MapTypes.Roll(type, new System.Random(4242), out elevation2);
				IslandFile f2 = MapTypes.Create(type, s2, elevation2, "citype");
				string name = type.Name.PadRight(11);
				Check(ref ok, Fingerprint(f) == Fingerprint(f2) && elevation == elevation2, name + " the same seed gives the same island");

				int res = f.HeightmapResolution;
				float water = f.WaterLevel / f.TerrainSize.y, top = 0f;
				bool edgesFlat = true;
				for (int i = 0; i < res; i++)
				{
					if (f.Heights[0, i] > 0.001f || f.Heights[res - 1, i] > 0.001f || f.Heights[i, 0] > 0.001f || f.Heights[i, res - 1] > 0.001f) edgesFlat = false;
					for (int j = 0; j < res; j++) top = Mathf.Max(top, f.Heights[i, j]);
				}
				float topAboveSea = (top - water) * f.TerrainSize.y + elevation;
				float radius = IslandSpawner.LandRadius(f);
				if (type.Build != null) Check(ref ok, top <= 0f && f.Objects.Count >= 8, name + " no land, " + f.Objects.Count + " objects on the water");
				else
				{
					Check(ref ok, edgesFlat && radius > 10f && radius < 480f, name + " land inside the build area (land radius " + radius.ToString("F0") + " m)");
					if (type.SunkenDepth > 0f) Check(ref ok, topAboveSea < -5f, name + " stays under water (top " + topAboveSea.ToString("F1") + " m)");
					else Check(ref ok, topAboveSea > 1f, name + " land above the sea (top " + topAboveSea.ToString("F1") + " m" + (elevation > 0f ? ", flying " + elevation.ToString("F0") + " m up" : "") + ")");
				}

				// Quest steps point at things that are on the island
				IslandQuest q = IslandQuest.From(f.Props);
				var zones = f.Objects.Where(o => o.Name == ContentCatalog.TriggerZone).Select(o => ObjectProps.Get(o.Props, ObjectProps.ZoneId)).ToList();
				var notes = f.Objects.Where(o => o.Props != null && ObjectProps.IsNote(o.Name, o.Props)).Select(o => ObjectProps.Get(o.Props, ObjectProps.NoteTitle)).ToList();
				var chests = f.Objects.Where(o => o.Props != null && o.Props.ContainsKey(ObjectProps.LootItems) && o.Name != ContentCatalog.TriggerZone).Select(o => ObjectProps.Get(o.Props, ObjectProps.NoteTitle)).ToList();
				var creatures = f.Objects.Select(o => ContentCatalog.CreatureOf(o.Name)).Where(k => k != null).Select(k => k.Label).ToList();
				foreach (IslandQuest.Step st in q.Steps)
				{
					bool found = st.Type == "reach" ? zones.Contains(st.Target) : st.Type == "read" ? notes.Contains(st.Target) : st.Type == "open" ? chests.Count(c => c == st.Target) >= st.Count :
						st.Type == "kill" ? creatures.Contains(st.Target) : true;
					Check(ref ok, found, name + " quest step '" + st.Describe() + "' has its target");
				}
				Log("  " + name + " " + ms.ToString("F0") + " ms: " + TerrainPainter.StyleName(s.Style) + " " + IslandShapes.Names[s.Shape] + ", " + f.Objects.Count + " objects, " + chests.Count + " chest(s), " + notes.Count + " note(s), " +
					zones.Count + " zone(s), " + creatures.Count + " creature spot(s)" + (q.Exists ? ", quest '" + q.ShownTitle + "' (" + q.Steps.Count + " steps)" : "") +
					(f.Props.ContainsKey(IslandProps.Title) ? ", title '" + f.Props[IslandProps.Title] + "'" : ""));
				yield return null;
			}

			// A look at some of them in the editor
			foreach (string show in new[] { "atoll", "archipelago", "stacks", "boss", "camp" })
			{
				string file = GeneratorWindow.MakeType(MapTypes.Get(show), 4242);
				Check(ref ok, file != null && DynamicIslands.LoadIsland(file), "opens a " + show + " in the editor");
				float t0 = Time.realtimeSinceStartup;
				while (DynamicIslands.currentIslandName != file && Time.realtimeSinceStartup - t0 < 30f) yield return new WaitForSecondsRealtime(0.25f);
				yield return new WaitForSecondsRealtime(1.5f);
				IslandGenSettings s = MapTypes.Roll(MapTypes.Get(show), new System.Random(4242), out float _);
				Terrain terrain = terraineditor.terrain;
				Vector3 c = terrain.transform.position + new Vector3(500f, IslandFile.DefaultWaterLevel, 500f);
				Transform cam = Camera.main.transform;
				cam.position = c + new Vector3(0, s.Radius * 1.3f + s.Height, -s.Radius * 1.5f);
				cam.LookAt(c + Vector3.up * (s.Height * 0.2f));
				yield return new WaitForSecondsRealtime(0.5f);
				Screenshot(new[] { "maptype_" + show });
				yield return new WaitForSecondsRealtime(0.5f);
				File.Delete(IslandSpawner.PathFor(file));
			}
			DynamicIslands.NewIsland();
			if (ok) Log("PASS: map types"); else Fail("map types");
		}

		[ConsoleCommand(name: "CIMapTypeWorld", docs: "Dev, in game (host): brings a treasure island, a wreck, a sunken island and a sky island by rules; checks their content in the world and plays the treasure hunt")]
		public static void MapTypeWorld()
		{
			DynamicIslands.instance.StartCoroutine(MapTypeWorldRoutine());
		}

		static IEnumerator MapTypeWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			string planBefore = WorldDirector.PlanName;
			bool autoBefore = CustomIslandSpawner.Enabled;
			var doneBefore = WorldDirector.Done.ToList();
			int before = IslandWorldState.Islands.Count;
			float unloadBefore = CustomIslandSpawner.UnloadDistance;
			CustomIslandSpawner.UnloadDistance = 2500f; // near Raft's own islands the four may end up 1 km away: keep them loaded
			WorldPlan.Parse(TestPlan, "random = off\n" +
				"rule = treasure | type:treasure | start | ahead:350 | | \n" +
				"rule = wreck | type:wreck | start | near:treasure:450:any | | \n" +
				"rule = sunken | type:sunken | start | near:treasure:450:any | | \n" +
				"rule = sky | type:sky | start | near:treasure:450:any | | \n").Save();
			WorldDirector.SetPlan(TestPlan, true);
			WorldDirector.Evaluate(); // "treasure" first: the others are placed near it
			WorldDirector.Evaluate();
			var entries = new[] { "treasure", "wreck", "sunken", "sky" }.Select(id => WorldDirector.Refs(id, null).FirstOrDefault()).ToList();
			Check(ref ok, entries.All(e => e != null), "all 4 rules brought their island (" + string.Join(", ", entries.Select(e => e != null ? e.HostName : "none").ToArray()) + ")");
			float t0 = Time.realtimeSinceStartup;
			while (entries.Any(e => e != null && e.Root == null) && Time.realtimeSinceStartup - t0 < 60f) yield return new WaitForSeconds(1f);
			foreach (IslandWorldState.Entry e in entries.Where(e => e != null))
			{
				GameObject r = e.Root;
				if (r == null) { Check(ref ok, false, e.HostName + " loaded"); continue; }
				Log("  " + e.HostName + ": elevation " + e.Position.y.ToString("F0") + " m, " + r.GetComponentsInChildren<LootCrate>(true).Length + " chest(s), " + r.GetComponentsInChildren<CustomNote>(true).Length + " note(s), " +
					r.GetComponentsInChildren<TriggerZone>(true).Length + " zone(s), " + r.GetComponentsInChildren<CreatureSpawnPoint>(true).Length + " creature spot(s), terrain " + (r.GetComponentInChildren<Terrain>() != null));
			}
			IslandWorldState.Entry treasure = entries[0], wreck = entries[1], sunken = entries[2], sky = entries[3];
			if (wreck != null && wreck.Root != null) Check(ref ok, wreck.Root.GetComponentInChildren<Terrain>() == null && wreck.Root.GetComponentsInChildren<LootCrate>(true).Length == 2, "the wreck floats without land, with 2 containers");
			if (sunken != null) Check(ref ok, sunken.Position.y < -15f && sunken.Root != null && sunken.Root.GetComponentsInChildren<LootCrate>(true).Length >= 1, "the sunken island is under water (" + sunken.Position.y.ToString("F0") + " m) with barrels");
			if (sky != null) Check(ref ok, sky.Position.y >= 45f, "the sky island floats " + sky.Position.y.ToString("F0") + " m up");

			// The treasure hunt: read the map, reach the X, open the treasure
			if (treasure != null && treasure.Root != null)
			{
				Check(ref ok, QuestTracker.QuestOf(treasure).Steps.Count == 3, "the treasure island has its quest");
				yield return StandRoutine(treasure.Root);
				CustomNote map = treasure.Root.GetComponentsInChildren<CustomNote>(true).FirstOrDefault(n => n.Title == "Treasure map");
				if (map != null) { NoteReader.Open(map); NoteReader.Close(); }
				TriggerZone x = treasure.Root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.Id == "x");
				if (x != null) x.Enter();
				LootCrate chest = treasure.Root.GetComponentsInChildren<LootCrate>(true).FirstOrDefault(c => c.GetComponent<CustomNote>() != null && c.GetComponent<CustomNote>().Title == "Treasure");
				if (chest != null) { chest.Open(); NoteReader.Close(); }
				Check(ref ok, map != null && x != null && chest != null && QuestTracker.StepOf(treasure) == 3, "the treasure hunt can be played to the end (step " + QuestTracker.StepOf(treasure) + ")");
				Screenshot(new[] { "maptype_world" });
			}

			yield return new WaitForSeconds(1f);
			var files = IslandWorldState.Islands.Skip(before).Select(e => e.HostName).ToList();
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(before).Select(e => e.Id).ToList(), true);
			foreach (string n in files) if (File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n));
			File.Delete(WorldPlan.PathFor(TestPlan));
			CustomIslandSpawner.UnloadDistance = unloadBefore;
			WorldDirector.SetPlan(planBefore, false);
			CustomIslandSpawner.Enabled = autoBefore;
			WorldDirector.Done.Clear();
			foreach (string d in doneBefore) WorldDirector.Done.Add(d);
			IslandWorldState.Save();
			if (ok) Log("PASS: map types in a world"); else Fail("map types in a world");
		}

		#endregion

		[ConsoleCommand(name: "CIPlanBox", docs: "Dev, main menu: opens Raft's New Game box, checks the Custom Islands plan choice and cycles it; screenshot; CIPlanBox <plan> leaves that plan chosen")]
		public static void PlanBox(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(PlanBoxRoutine(args != null && args.Length > 0 ? string.Join(" ", args) : null));
		}

		static IEnumerator PlanBoxRoutine(string choose)
		{
			bool ok = true;
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(x => x.gameObject.scene.IsValid());
			if (box == null) { Fail("no New Game box (go to the main menu first)"); yield break; }
			box.gameObject.SetActive(true);
			box.Open();
			yield return new WaitForSecondsRealtime(0.5f);
			Transform row = box.transform.Find("CustomIslands_Plan");
			Check(ref ok, row != null && row.gameObject.activeInHierarchy, "the box has the plan choice");
			Button b = row != null ? row.GetComponentInChildren<Button>() : null;
			string first = NewWorldOptions.Selected;
			if (b != null) b.onClick.Invoke();
			string second = NewWorldOptions.Selected;
			Check(ref ok, first != second, "clicking it picks the next plan (" + first + " -> " + second + ")");
			Screenshot(new[] { "new_game_plan" });
			yield return new WaitForSecondsRealtime(0.5f);
			WorldDirector.PendingPlan = choose;
			if (choose == null) box.Button_Close();
			else Log("Plan for the next new world: " + NewWorldOptions.Selected);
			if (ok) Log("PASS: plan choice in the New Game box"); else Fail("plan choice in the New Game box");
		}

		[ConsoleCommand(name: "CIPlanCheck", docs: "Dev, in game: the world's plan and its islands, after creating a world with a plan: CIPlanCheck <expected plan>")]
		public static void PlanCheck(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(PlanCheckRoutine(args != null ? string.Join(" ", args) : ""));
		}

		static IEnumerator PlanCheckRoutine(string expected)
		{
			bool ok = true;
			float t0 = Time.realtimeSinceStartup;
			while (WorldDirector.Plan != null && WorldDirector.Plan.Rules.Any(r => r.When == "start" && !WorldDirector.Done.Contains(r.Id)) && Time.realtimeSinceStartup - t0 < 30f) yield return new WaitForSeconds(0.5f);
			yield return new WaitForSeconds(2f);
			Check(ref ok, WorldDirector.PlanName.Equals(expected, StringComparison.OrdinalIgnoreCase), "the world's plan is '" + WorldDirector.PlanName + "'");
			WorldPlan p = WorldDirector.Plan;
			if (p != null)
				foreach (IntroRule r in p.Rules.Where(r => r.When == "start"))
				{
					IslandWorldState.Entry e = WorldDirector.Refs(r.Id, null).FirstOrDefault();
					Check(ref ok, e != null, "start rule '" + r.Id + "' brought " + (e != null ? "'" + e.HostName + "' (" + e.Label + ")" : "nothing"));
				}
			Check(ref ok, CustomIslandSpawner.Enabled == (p == null || p.Random), "random islands " + (CustomIslandSpawner.Enabled ? "on" : "off") + " as the plan says");
			Screenshot(new[] { "plan_check" });
			Log(WorldDirector.Describe());
			if (ok) Log("PASS: the new world follows its plan"); else Fail("the new world follows its plan");
		}

		#endregion
	}
}
