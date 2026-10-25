using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests of the story features players meet (PlayerFeatures, 2026-10-25): islanders and their dialogue.</summary>
	public static partial class DevTests
	{
		const string PlayerIsland = "ciplayer";

		/// <summary>Every verb, check and quest step has its label and hint in the editors (the behaviour and quest windows).</summary>
		static void CheckPlayerTables(ref bool ok)
		{
			var labels = Traverse.Create(typeof(BehaviourWindow)).Field("VerbLabels").GetValue<Dictionary<string, string>>();
			var hints = Traverse.Create(typeof(BehaviourWindow)).Field("VerbHints").GetValue<Dictionary<string, string>>();
			string[] missing = BehaviourProps.Verbs.Where(v => labels == null || hints == null || !labels.ContainsKey(v) || !hints.ContainsKey(v)).ToArray();
			Check(ref ok, missing.Length == 0, "every verb has a label and a hint in the behaviour window" + (missing.Length > 0 ? " (missing " + string.Join(", ", missing) + ")" : ""));
			var clabels = Traverse.Create(typeof(BehaviourWindow)).Field("CheckLabels").GetValue<Dictionary<string, string>>();
			var chints = Traverse.Create(typeof(BehaviourWindow)).Field("CheckHints").GetValue<Dictionary<string, string>>();
			missing = BehaviourProps.CheckKinds.Where(k => clabels == null || chints == null || !clabels.ContainsKey(k) || !chints.ContainsKey(k)).ToArray();
			Check(ref ok, missing.Length == 0, "every check has a label and a hint" + (missing.Length > 0 ? " (missing " + string.Join(", ", missing) + ")" : ""));
			string[] tables = { "TypeLabels", "TypeHints", "TargetHints" };
			foreach (string t in tables)
			{
				var d = Traverse.Create(typeof(QuestEditorWindow)).Field(t).GetValue<Dictionary<string, string>>();
				missing = IslandQuest.Types.Where(x => d == null || !d.ContainsKey(x)).ToArray();
				Check(ref ok, missing.Length == 0, "every quest step is in the quest editor's " + t + (missing.Length > 0 ? " (missing " + string.Join(", ", missing) + ")" : ""));
			}
		}

		[ConsoleCommand(name: "CIIslanderParse", docs: "Dev, anywhere: islanders' dialogues read right (pages, answers, poses), the dialogue verb, the talked check and the Talk to step, and the editors' lists")]
		public static void IslanderParseCommand()
		{
			bool ok = true;
			List<Islanders.Page> pages = Islanders.Parse("Hello.\n---\nWill you help?\nIt's dark.\n> Yes\n> No\n> Maybe\n> Too many");
			Check(ref ok, pages.Count == 2 && pages[0].Text == "Hello." && pages[1].Text == "Will you help?\nIt's dark." && pages[1].Choices.Count == 3 && pages[1].Choices[0] == "Yes",
				"two pages, three answers at most (" + pages.Count + " pages)");
			pages = Islanders.Parse("One\\n---\\nTwo\\n> Ok");
			Check(ref ok, pages.Count == 2 && pages[1].Text == "Two" && pages[1].Choices.Count == 1, "an action's one line: \\n breaks lines, --- pages");
			Check(ref ok, Islanders.Parse("").Count == 0 && Islanders.Parse("---\n---").Count == 0, "an empty dialogue has no pages");
			Check(ref ok, Islanders.Choices("A\n> x\n---\nB\n> y").SequenceEqual(new[] { "x", "y" }), "answers count on over pages (choice1, choice2)");
			Check(ref ok, Islanders.PoseOf(P(Islanders.PoseProp, "Fish")) == "fish" && Islanders.PoseOf(P(Islanders.PoseProp, "dance")) == "stand" && Islanders.PoseOf(P()) == "stand", "poses: fish; unknown = stand");
			Check(ref ok, Islanders.NameOf(P(BehaviourProps.Name, "Old Finn")) == "Old Finn" && Islanders.NameOf(P()) == "Islander", "an islander's name (default Islander)");
			Dictionary<string, string> d = ObjectProps.Defaults(Islanders.ObjectName);
			Check(ref ok, ObjectProps.Get(d, Islanders.TextProp).Length > 0 && ObjectProps.Get(d, Islanders.PoseProp) == "stand", "a placed islander starts with a dialogue and a pose");

			ObjAction a = ObjAction.Parse("dialogue|Old Finn|Hi\\n---\\nBye");
			Check(ref ok, a != null && a.Target == "Old Finn" && a.Arg == "Hi\\n---\\nBye" && !a.Shared && ObjAction.HasTarget("dialogue") && ObjAction.HasArg("dialogue"), "dialogue|Old Finn|text parses; it is the player's own");
			Check(ref ok, a != null && a.Describe().Contains("dialogue") && a.Describe().Contains("Old Finn"), "described (" + (a != null ? a.Describe() : "") + ")");
			Check(ref ok, a != null && ObjAction.Parse(ObjAction.ToLines(new[] { a })).Arg == a.Arg, "it saves and reads back");
			ObjCheck c = ObjCheck.Parse("talked|Old Finn");
			Check(ref ok, c != null && c.Kind == "talked" && c.Target == "Old Finn" && c.Describe().Contains("talked to"), "talked|Old Finn parses (" + (c != null ? c.Describe() : "null") + ")");
			List<string> evs = BehaviourProps.EventsFor(Islanders.ObjectName, P(Islanders.TextProp, "Hi\n> Yes")).Select(x => x.Key).ToList();
			Check(ref ok, evs.Contains("talk") && evs.Contains("choice1") && evs.Contains("choice3") && !evs.Contains("use"), "an islander's events: talk, choice1..3, not use (" + string.Join(", ", evs.ToArray()) + ")");
			Check(ref ok, PlayerFeatures.IsEvent("choice2") && PlayerFeatures.IsEvent("talk") && !PlayerFeatures.IsEvent("choice"), "the events are known");

			var q = new IslandQuest.Step { Type = "talk", Target = "Old Finn", Count = 1 };
			Check(ref ok, IslandQuest.Types.Contains("talk") && !IslandQuest.Counted("talk") && q.Describe() == "Talk to Old Finn", "the Talk to step (" + q.Describe() + ")");
			var facts = new PlayerFeatures.PlanFacts();
			PlayerFeatures.AddFacts(facts, Islanders.ObjectName, P(BehaviourProps.Name, "Old Finn"));
			Check(ref ok, PlayerFeatures.StepProblem(facts, "talk", "Old Finn", 1) == null && PlayerFeatures.StepProblem(facts, "talk", "Nobody", 1) != null
				&& PlayerFeatures.StepProblem(new PlayerFeatures.PlanFacts(), "talk", "", 1) != null, "the plan check: a missing islander is a problem");
			Check(ref ok, ContentCatalog.Hint(Islanders.ObjectName).Contains("talk"), "the catalog hint");
			Check(ref ok, Islanders.TalkedBase > BossPhases.KeyBase + 0xFFFF && Islanders.TalkedBase + 0xFFFF < Behaviours.PendingBase, "the talked keys meet no other keys");
			CheckPlayerTables(ref ok);
			if (ok) Log("PASS: islanders' settings"); else Fail("islanders' settings");
		}

		/// <summary>The test island: SampleIsland with the objects add puts on it near the middle (cleared 25 m around).</summary>
		static IslandFile PlayerTestIsland(Action<IslandFile, Func<string, float, float, Dictionary<string, string>, int>> build)
		{
			IslandFile f = SampleIsland();
			f.Name = PlayerIsland;
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Func<float, float, Vector3> ground = (x, z) => new Vector3(x, f.Heights[Mathf.Clamp(Mathf.RoundToInt(z / step), 0, res - 1), Mathf.Clamp(Mathf.RoundToInt(x / step), 0, res - 1)] * f.TerrainSize.y, z);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 25f);
			build(f, (name, dx, dz, props) => { f.Objects.Add(new IslandObject { Name = name, Position = ground(c.x + dx, c.y + dz), Props = props }); return f.Objects.Count - 1; });
			return f;
		}

		static IEnumerator SpawnPlayerIsland(IslandFile f, Action<IslandWorldState.Entry> got)
		{
			f.Save(IslandSpawner.PathFor(PlayerIsland));
			IslandWorldState.Entry old = IslandWorldState.Islands.FirstOrDefault(x => x.HostName == PlayerIsland);
			if (old != null) IslandWorldState.RemoveIds(new[] { old.Id }, true);
			Vector3? spot = CustomIslandSpawner.FindClearSpot(CustomIslandSpawner.RaftPosition.Value, CustomIslandSpawner.LandRadius(PlayerIsland), 300f);
			if (!spot.HasValue) { got(null); yield break; }
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(PlayerIsland, spot.Value, true);
			got(IslandWorldState.Islands.Skip(before).FirstOrDefault());
		}

		static void RemovePlayerIsland(IslandWorldState.Entry e)
		{
			if (e != null) IslandWorldState.RemoveIds(new[] { e.Id }, true);
			if (File.Exists(IslandSpawner.PathFor(PlayerIsland))) File.Delete(IslandSpawner.PathFor(PlayerIsland));
		}

		[ConsoleCommand(name: "CIIslanderRun", docs: "Dev, in a world (host): spawns 'ciplayer' with an islander (sitting): talks to it, pages on, picks an answer (its choice1 signal), the talked check and the Talk to step, a dialogue action")]
		public static void IslanderRunCommand() { StartTest(IslanderRunRoutine()); }

		static IEnumerator IslanderRunRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			int finn = -1, gate = -1, sign = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				finn = add(Islanders.ObjectName, 0f, 4f, P(BehaviourProps.Name, "Old Finn", Islanders.PoseProp, "sit", Islanders.CharacterProp, "1",
					Islanders.TextProp, "Ahoy.\n---\nWill you light the beacon?\n> I will\n> Not now", "on.choice1", "signal||finn-yes", "on.talk", "message||Finn nods"));
				gate = add("Log", 3f, 4f, P(BehaviourProps.Use, "Open", "if.use", "talked|Old Finn", "on.use", "signal||finn-gate"));
				sign = add("Log", -3f, 4f, P(BehaviourProps.Use, "Ring", "on.use", "dialogue|Old Finn|You rang?"));
				file.Props[IslandQuest.KeyTitle] = "CI islander";
				file.Props[IslandQuest.KeySteps] = "talk|Old Finn|1|\nreach|nowhere|1|";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			Func<int, IslandObjectRef> obj = i => e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			try
			{
				yield return new WaitForSeconds(1f);
				IslandObjectRef fr = obj(finn);
				Islander isl = fr != null ? fr.GetComponentInChildren<Islander>(true) : null;
				Check(ref ok, isl != null, "the islander has its talk");
				Check(ref ok, isl != null && isl.HasModel, "Raft's character stands in for the marker figure");
				if (isl != null)
				{
					Animator an = fr.GetComponentsInChildren<Animator>(true).FirstOrDefault();
					Log("islander animator: " + (an != null && an.runtimeAnimatorController != null ? string.Join(", ", an.parameters.Select(p => p.name + "=" + (p.type == AnimatorControllerParameterType.Int ? an.GetInteger(p.name).ToString() : p.type == AnimatorControllerParameterType.Bool ? an.GetBool(p.name).ToString() : p.type.ToString())).ToArray()) : "none"));
					Screenshot(new[] { "islander" });
					PutPlayerNear(fr.transform, 2f);
					yield return new WaitForSeconds(0.5f);
					// The gate's check fails before anyone talked
					Behaviours.Fire(e, gate, "use", true);
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, !Behaviours.HasSignal(e, "finn-gate"), "the talked check fails before talking");
					isl.Talk();
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, DialoguePanel.IsOpen && DialoguePanel.ShownSpeaker == "Old Finn" && DialoguePanel.ShownText == "Ahoy.", "the dialogue opens on page 1 (" + DialoguePanel.ShownText + ")");
					DialoguePanel.Next();
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, DialoguePanel.ShownPage == 1 && DialoguePanel.ShownText.StartsWith("Will you"), "Next: page 2 with the answers");
					DialoguePanel.Next();
					Check(ref ok, DialoguePanel.IsOpen, "Next doesn't skip the answers");
					DialoguePanel.Pick(1);
					yield return new WaitForSeconds(0.5f);
					Check(ref ok, !DialoguePanel.IsOpen, "an answer closes it");
					Check(ref ok, Behaviours.HasSignal(e, "finn-yes"), "answer 1 fired choice1 (its signal)");
					Check(ref ok, Behaviours.LastMessage == "Finn nods", "closing fired talk (its message: " + Behaviours.LastMessage + ")");
					Check(ref ok, ContentState.IsUsed(e, Islanders.TalkedBase + finn), "talked to is remembered for the world");
					Check(ref ok, QuestTracker.StepOf(e) >= 1, "the Talk to step is done (step " + QuestTracker.StepOf(e) + ")");
					Behaviours.Fire(e, gate, "use", true);
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, Behaviours.HasSignal(e, "finn-gate"), "the talked check passes after talking");
					Behaviours.Fire(e, sign, "use", true);
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, DialoguePanel.IsOpen && DialoguePanel.ShownSpeaker == "Old Finn" && DialoguePanel.ShownText == "You rang?", "a dialogue action, said by Old Finn");
					DialoguePanel.Close();
				}
			}
			finally
			{
				DialoguePanel.Close();
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: islanders in a world"); else Fail("islanders in a world");
		}
	}
}
