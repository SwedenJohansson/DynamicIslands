using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using UnityEngine;
using UnityEngine.UI;
using RaftModLoader;
using HMLLibrary;

namespace DynamicIslands
{
	/// <summary>Tests of the quest step kinds of 3.5 (QuestWaitSteps): "Be there at a time", "Weather is",
	/// "Counter reaches" and "Players in a zone".</summary>
	public static partial class DevTests
	{
		const string WaitSteps = "time|night|1|\nweather|rain,storm|1|\ncounter|ci-bells|3|\nplayers|ciwait|2|";

		[ConsoleCommand(name: "CIQuestWaitEditor", docs: "Dev, editor: 3.5 - the quest editor's step kinds time, weather, counter and players: their names, ▾ lists (times, weathers, the island's counters and zones), no count for time/weather, the words, the plan check")]
		public static void QuestWaitEditor() { StartTest(QuestWaitEditorRoutine()); }

		static IEnumerator QuestWaitEditorRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			DynamicIslands.currentIslandProps.Clear();
			yield return null;
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 1f, 500f);
			EditorGameObject bell = PlaceForTest("Note_Sign", c0, placed);
			PropsCommand.Change(bell, P(BehaviourProps.Name, "Bell", BehaviourProps.Use, "Ring", BehaviourProps.EventPrefix + "use", "count|ci-bells|+1"));
			EditorGameObject zone = PlaceForTest(ContentCatalog.TriggerZone, c0 + new Vector3(6, 0, 0), placed);
			PropsCommand.Change(zone, P(BehaviourProps.Name, "Plate", ObjectProps.ZoneId, "ciwait", ObjectProps.ZoneRadius, "4"));

			var q = new IslandQuest { Title = "CI wait", Intro = "Wait for the world.", Done = "Done." };
			foreach (string line in WaitSteps.Split('\n'))
			{
				string[] p = line.Split('|');
				q.Steps.Add(new IslandQuest.Step { Type = p[0], Target = p[1], Count = int.Parse(p[2]) });
			}
			QuestEditorWindow.Apply(q);
			IslandQuest back = IslandQuest.From(DynamicIslands.currentIslandProps);
			string words = string.Join(" / ", back.Steps.Select(s => s.Describe()).ToArray());
			Check(ref ok, back.Steps.Count == 4 && back.Steps[0].Type == QuestWaitSteps.Time && back.Steps[3].Type == QuestWaitSteps.Players && back.Steps[2].Count == 3,
				"the 4 steps are kept in the island's props");
			Check(ref ok, back.Steps[0].Describe() == "Be here at night" && back.Steps[1].Describe() == "Be here in rain or storm" && back.Steps[2].Describe() == "Get ci-bells to 3"
				&& back.Steps[3].Describe() == "Stand in ciwait with 2 players at once", "the steps' words: " + words);
			Check(ref ok, QuestWaitSteps.Kinds.All(k => IslandQuest.Types.Contains(k) && IslandQuest.Counted(k)), "4 new kinds, counted by the host");

			EditorUI.SetTab(TAB.Island);
			yield return null;
			QuestEditorWindow.Open();
			yield return new WaitForSecondsRealtime(0.5f);
			Transform qw = EditorUI.Canvas.transform.Find("QuestEditorWindow");
			Check(ref ok, qw != null, "the quest editor opens");
			if (qw == null) { Fail("quest wait editor"); yield break; }
			ScrollRect scroll = qw.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault(s => s.transform.parent != null && s.transform.parent.name == "StepsBox") ?? qw.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault();
			List<Button> kinds = qw.GetComponentsInChildren<Button>(true).Where(b => b.name == "Drop_StepType").ToList();
			string[] labels = kinds.Select(b => UIKit.LabelOf(b).text).ToArray();
			Check(ref ok, labels.Length == 4 && labels[0] == "Be there at a time" && labels[1] == "Weather is" && labels[2] == "Counter reaches" && labels[3] == "Players in a zone",
				"the kinds' names: " + string.Join(", ", labels));
			// each row's own ▾ list and count field
			Func<int, Transform> row = i => kinds.Count > i ? kinds[i].transform.parent : null;
			Func<int, string> listed = i =>
			{
				Button pick = row(i) != null ? row(i).GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Pick_Target") : null;
				return pick != null ? string.Join(",", DropList.Shown(pick).Select(o => o.Key).ToArray()) : "";
			};
			Func<int, int> fieldsIn = i => row(i) != null ? row(i).GetComponentsInChildren<InputField>(true).Length : 0;
			string times = listed(0), weathers = listed(1), counters = listed(2), zones = listed(3);
			Check(ref ok, times.Contains("night") && times.Contains("dawn") && times.Contains("20-6"), "time's list: " + times);
			Check(ref ok, weathers.Contains("rain") && weathers.Contains("storm") && !weathers.Contains("raft"), "weather's list: " + weathers);
			Check(ref ok, counters == "ci-bells", "counter's list: the island's count action (" + counters + ")");
			Check(ref ok, zones == "ciwait", "players' list: the island's trigger zone (" + zones + ")");
			// 3.5 feat35b: every step row also has the QE9 time-limit field (one more InputField per row)
			Check(ref ok, fieldsIn(0) == 3 && fieldsIn(1) == 3 && fieldsIn(2) == 4 && fieldsIn(3) == 4,
				"no count field for time and weather, one for counter and players (" + fieldsIn(0) + "," + fieldsIn(1) + "," + fieldsIn(2) + "," + fieldsIn(3) + ")");
			Screenshot(new[] { "qwait_editor" });
			yield return new WaitForSecondsRealtime(0.5f);
			if (kinds.Count > 0)
			{
				// the kind list open: the new kinds at its end
				kinds[0].onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.4f);
				Screenshot(new[] { "qwait_kinds" });
				yield return new WaitForSecondsRealtime(0.3f);
				DropList.Close();
				yield return null;
			}
			QuestEditorWindow.Close();

			// the plan check
			var f = new IslandFile { Name = "ciwait", Props = new Dictionary<string, string>(DynamicIslands.currentIslandProps) };
			foreach (EditorGameObject g in new[] { bell, zone }) f.Objects.Add(new IslandObject { Name = g.GameObjectName, Props = g.Props != null ? new Dictionary<string, string>(g.Props) : null });
			List<string> bad = PlanChecker.QuestFindings(f).Where(x => x.Level == PlanChecker.Level.Problem).Select(x => x.Text).ToList();
			Check(ref ok, bad.Count == 0, "the plan check finds no problem in the 4 steps" + (bad.Count > 0 ? ": " + string.Join(" / ", bad.ToArray()) : ""));
			f.Props[IslandQuest.KeySteps] = "time|noon-ish|1|\nweather|hail|1|\ncounter|ci-gongs|2|\nplayers|nozone|2|\nplayers|ciwait|12|";
			bad = PlanChecker.QuestFindings(f).Where(x => x.Level == PlanChecker.Level.Problem).Select(x => x.Text).ToList();
			string all = string.Join(" / ", bad.ToArray());
			Check(ref ok, bad.Count == 5 && all.Contains("noon-ish") && all.Contains("hail") && all.Contains("ci-gongs") && all.Contains("nozone") && all.Contains("at most 8"),
				"bad steps are problems (" + bad.Count + "): " + all);

			DynamicIslands.currentIslandProps.Clear();
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			if (ok) Log("PASS: quest wait steps editor"); else Fail("quest wait steps editor");
		}

		[ConsoleCommand(name: "CIQuestWaitWorld", docs: "Dev, in a world (host): 3.5 - an island whose quest waits for night, rain, a counter at 3 (a sign's count action) and 2 players in a zone (3 online, faked); QuestTest shows the state line")]
		public static void QuestWaitWorld() { StartTest(QuestWaitWorldRoutine()); }

		static IEnumerator QuestWaitWorldRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			int bell = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				bell = add("Note_Sign", -4f, 4f, P(BehaviourProps.Name, "Bell", BehaviourProps.Use, "Ring", BehaviourProps.EventPrefix + "use", "count|ci-bells|+1"));
				add(ContentCatalog.TriggerZone, 8f, 0f, P(BehaviourProps.Name, "Plate", ObjectProps.ZoneId, "ciwait", ObjectProps.ZoneRadius, "3"));
				file.Props[IslandQuest.KeyTitle] = "CI wait";
				file.Props[IslandQuest.KeySteps] = WaitSteps;
			});
			IslandWorldState.Entry e = null;
			GapBehaviours.TestHour = 12f;
			GapBehaviours.TestWeather = "clear";
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); GapBehaviours.TestHour = null; GapBehaviours.TestWeather = null; RemovePlayerIsland(e); yield break; }
			try
			{
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(2f);
				Check(ref ok, QuestTracker.StepOf(e) == 0, "at noon: step 1 (night) waits");
				string show = QuestTestCommands.Describe(e);
				Check(ref ok, show.Contains("(now: 12:00 day, weather clear)"), "QuestTest shows the time and weather: " + show.Replace("\n", " / "));
				Screenshot(new[] { "qwait_world" });

				GapBehaviours.TestHour = 23f;
				yield return WaitStep(e, 1, 6f);
				Check(ref ok, QuestTracker.StepOf(e) == 1, "night falls: step 1 done");
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, QuestTracker.StepOf(e) == 1, "clear weather: step 2 (rain or storm) waits");
				GapBehaviours.TestWeather = "storm";
				yield return WaitStep(e, 2, 6f);
				Check(ref ok, QuestTracker.StepOf(e) == 2, "a storm: step 2 done");

				// the counter: a real count action, three rings
				for (int i = 0; i < 2; i++) { Behaviours.Fire(e, bell, "use", true); yield return new WaitForSeconds(1.5f); }
				yield return new WaitForSeconds(1.5f);
				show = QuestTestCommands.Describe(e);
				Check(ref ok, QuestTracker.StepOf(e) == 2 && GapBehaviours.Counter(e, "ci-bells") == 2 && show.Contains("counter 'ci-bells' = 2"), "two rings: the counter at 2, step 3 waits (" + show.Replace("\n", " / ") + ")");
				Behaviours.Fire(e, bell, "use", true);
				yield return WaitStep(e, 3, 6f);
				Check(ref ok, QuestTracker.StepOf(e) == 3, "the third ring: step 3 done (counter " + GapBehaviours.Counter(e, "ci-bells") + ")");

				// players: 3 online (faked), one and then two in the zone
				GapWorld.FakeOnline = 3;
				GapWorld.FakeCount = z => z != null && z.Id == "ciwait" ? 1 : 0;
				yield return new WaitForSeconds(2f);
				show = QuestTestCommands.Describe(e);
				Check(ref ok, QuestTracker.StepOf(e) == 3 && show.Contains("1 of 2 players in 'ciwait'"), "one player of 3 in the zone: step 4 waits (" + show.Replace("\n", " / ") + ")");
				GapWorld.FakeCount = z => z != null && z.Id == "ciwait" ? 2 : 0;
				yield return WaitStep(e, 4, 6f);
				Check(ref ok, QuestTracker.StepOf(e) == 4, "two players in the zone: step 4 done, the quest done");
				// alone (1 online): one player is enough for a step of 2
				GapWorld.FakeOnline = 1;
				GapWorld.FakeCount = z => 1;
				var s2 = new IslandQuest.Step { Type = QuestWaitSteps.Players, Target = "ciwait", Count = 2 };
				Check(ref ok, QuestWaitSteps.Found(e, s2) == 2, "alone in the world: one player in the zone is enough");
				Screenshot(new[] { "qwait_done" });
				yield return new WaitForSeconds(0.5f);
			}
			finally
			{
				GapBehaviours.TestHour = null;
				GapBehaviours.TestWeather = null;
				GapWorld.FakeCount = null;
				GapWorld.FakeOnline = -1;
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: quest wait steps in a world"); else Fail("quest wait steps in a world");
		}
	}
}
