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

		[ConsoleCommand(name: "CICollectParse", docs: "Dev, anywhere: collectible sets read right (a piece's defaults and use lines, the set check, the Find set step, the set event, plan check, keys, the editors' lists)")]
		public static void CollectParseCommand()
		{
			bool ok = true;
			Dictionary<string, string> d = ObjectProps.Defaults(Collectibles.ObjectName);
			Check(ref ok, Collectibles.SetOf(d) == "Relics" && ObjectProps.Get(d, "on.use") == "hide|" && ObjectProps.Get(d, BehaviourProps.Use).Length > 0, "a placed collectible is a piece of Relics, picked up when used");
			IDictionary<string, string> x = Collectibles.Expand("Log", P(Collectibles.SetProp, "Shells", "on.use", "message||Got it"));
			Check(ref ok, ObjectProps.Get(x, "on.use") == "hide|\nmessage||Got it" && ObjectProps.Get(x, BehaviourProps.Use) == "Pick up", "any object with a set: hidden when used, its own lines kept (" + ObjectProps.Get(x, "on.use").Replace("\n", " / ") + ")");
			IDictionary<string, string> own = P(Collectibles.SetProp, "Shells", "on.use", "hide|", BehaviourProps.Use, "Take the shell");
			Check(ref ok, ReferenceEquals(Collectibles.Expand("Log", own), own), "a piece with its own hide and label is kept as it is");
			IDictionary<string, string> none = P("on.use", "message||x");
			Check(ref ok, ReferenceEquals(Collectibles.Expand("Log", none), none) && !Collectibles.IsPiece(none), "an object without a set is no piece");
			Check(ref ok, Collectibles.SignalOf(" Relics ") == "set:relics", "the set's signal (set:relics)");
			ObjCheck c = ObjCheck.Parse("set|Relics|3");
			Check(ref ok, c != null && c.Kind == "set" && c.Target == "Relics" && c.Arg == "3" && c.Describe().Contains("Relics"), "set|Relics|3 parses (" + (c != null ? c.Describe() : "null") + ")");
			Check(ref ok, PlayerFeatures.CheckArgPlaceholder("set") != null && PlayerFeatures.CheckPlaceholder("set") != null, "the behaviour window shows the set's name and how many");
			List<string> evs = BehaviourProps.EventsFor("Log", P(Collectibles.SetProp, "Relics")).Select(v => v.Key).ToList();
			Check(ref ok, evs.Contains("set") && evs.Contains("use") && PlayerFeatures.IsEvent("set"), "a piece has the set event (" + string.Join(", ", evs.ToArray()) + ")");
			var q = new IslandQuest.Step { Type = "set", Target = "Relics", Count = 4 };
			Check(ref ok, IslandQuest.Types.Contains("set") && IslandQuest.Counted("set") && q.Describe() == "Find 4 of the Relics", "the Find set step, counted (" + q.Describe() + ")");
			var facts = new PlayerFeatures.PlanFacts();
			for (int i = 0; i < 3; i++) PlayerFeatures.AddFacts(facts, Collectibles.ObjectName, P(Collectibles.SetProp, "Relics"));
			Check(ref ok, PlayerFeatures.StepProblem(facts, "set", "relics", 3) == null && PlayerFeatures.StepProblem(facts, "set", "Relics", 4) != null
				&& PlayerFeatures.StepProblem(facts, "set", "Coins", 1) != null && PlayerFeatures.StepProblem(facts, "set", "", 1) != null, "the plan check: an unknown set, too few pieces");
			Check(ref ok, ContentCatalog.Hint(Collectibles.ObjectName).Contains("set"), "the catalog hint");
			Check(ref ok, Collectibles.KeyBase > Islanders.TalkedBase + 0xFFFF && Collectibles.KeyBase + 0xFFFF < Behaviours.PendingBase, "the piece keys meet no other keys");
			CheckPlayerTables(ref ok);
			if (ok) Log("PASS: collectible sets' settings"); else Fail("collectible sets' settings");
		}

		[ConsoleCommand(name: "CICollectRun", docs: "Dev, in a world (host): spawns 'ciplayer' with three Relics: picks them up one by one (the HUD count, hidden, kept), the set check and the Find set step, the whole set's signal and set event")]
		public static void CollectRunCommand() { StartTest(CollectRunRoutine()); }

		static IEnumerator CollectRunRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			var piece = new List<int>();
			int gate = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				for (int i = 0; i < 3; i++)
				{
					Dictionary<string, string> p = ObjectProps.Defaults(Collectibles.ObjectName);
					if (i == 0) p["on.set"] = "signal||relics-reward\nmessage||All relics!";
					piece.Add(add(Collectibles.ObjectName, -3f + i * 3f, 4f, p));
				}
				gate = add("Log", 0f, 7f, P(BehaviourProps.Use, "Open", "if.use", "set|Relics|2", "on.use", "signal||relic-gate"));
				file.Props[IslandQuest.KeyTitle] = "CI relics";
				file.Props[IslandQuest.KeySteps] = "set|Relics|3|\nreach|nowhere|1|";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, v => e = v);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			Func<int, IslandObjectRef> obj = i => e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			try
			{
				yield return new WaitForSeconds(1f);
				Check(ref ok, Collectibles.Total(e, "Relics") == 3 && Collectibles.Found(e, "relics") == 0, "three pieces, none found (" + Collectibles.Total(e, "Relics") + ")");
				IslandObjectRef first = obj(piece[0]);
				Check(ref ok, first != null && first.GetComponentsInChildren<TextMesh>(false).Length == 0, "the editor name tag is gone in the world");
				if (first != null) { PutPlayerNear(first.transform, 2f); Screenshot(new[] { "collectible" }); }
				yield return new WaitForSeconds(0.5f);
				for (int i = 0; i < 3; i++)
				{
					IslandObjectRef r = obj(piece[i]);
					UseInteract u = r != null ? r.GetComponentInChildren<UseInteract>(true) : null;
					Check(ref ok, u != null, "piece " + (i + 1) + " can be picked up");
					if (u == null) continue;
					Traverse.Create(u).Method("Use").GetValue();
					yield return new WaitForSeconds(0.6f);
					Check(ref ok, Collectibles.LastShown != null && Collectibles.LastShown.StartsWith("Relics: " + (i + 1) + " of 3"), "the HUD counts (" + Collectibles.LastShown + ")");
					Check(ref ok, ContentState.IsUsed(e, Collectibles.KeyBase + piece[i]) && !r.gameObject.activeSelf, "piece " + (i + 1) + " is gone and kept found");
					if (i == 0)
					{
						Behaviours.Fire(e, gate, "use", true);
						yield return new WaitForSeconds(0.3f);
						Check(ref ok, !Behaviours.HasSignal(e, "relic-gate"), "set|Relics|2 fails with one found");
						Check(ref ok, !Behaviours.HasSignal(e, Collectibles.SignalOf("Relics")), "no set signal yet");
					}
					if (i == 1)
					{
						Behaviours.Fire(e, gate, "use", true);
						yield return new WaitForSeconds(0.3f);
						Check(ref ok, Behaviours.HasSignal(e, "relic-gate"), "set|Relics|2 passes with two found");
						Check(ref ok, QuestTracker.StepOf(e) == 0, "the Find 3 step waits");
					}
				}
				yield return new WaitForSeconds(1.2f);
				Check(ref ok, Behaviours.HasSignal(e, Collectibles.SignalOf("Relics")), "the whole set sent set:relics");
				Check(ref ok, Behaviours.HasSignal(e, "relics-reward"), "the whole set ran the pieces' set event (its reward)");
				Check(ref ok, QuestTracker.StepOf(e) >= 1, "the Find set step is done (step " + QuestTracker.StepOf(e) + ")");
			}
			finally { RemovePlayerIsland(e); }
			if (ok) Log("PASS: collectible sets in a world"); else Fail("collectible sets in a world");
		}

		[ConsoleCommand(name: "CILightParse", docs: "Dev, anywhere: light puzzles read right (a light's defaults and use lines, the light action, the light and lights checks, the events, the Light up step, plan check, the editors' lists)")]
		public static void LightParseCommand()
		{
			bool ok = true;
			Dictionary<string, string> d = ObjectProps.Defaults(Lights.ObjectName);
			Check(ref ok, Lights.GroupOf(d) == "Lamps" && !Lights.StartsOn(d) && ObjectProps.Get(d, BehaviourProps.Use).Length > 0, "a placed light is in Lamps, starts off, has a use label");
			IDictionary<string, string> x = Lights.Expand(Lights.ObjectName, d);
			Check(ref ok, ObjectProps.Get(x, "on.use") == "switch|", "the use key switches it (" + ObjectProps.Get(x, "on.use") + ")");
			x = Lights.Expand("Log", P(Lights.GroupProp, "Torches", "on.use", "message||Click"));
			Check(ref ok, ObjectProps.Get(x, "on.use") == "switch|\nmessage||Click" && ObjectProps.Get(x, BehaviourProps.Use).Length > 0, "any object with a group is a light, its own lines kept");
			IDictionary<string, string> own = P(Lights.GroupProp, "T", "on.use", "light||on", BehaviourProps.Use, "Light it");
			Check(ref ok, ReferenceEquals(Lights.Expand("Log", own), own), "a light that switches itself is kept as it is");
			IDictionary<string, string> none = P("on.use", "message||x");
			Check(ref ok, ReferenceEquals(Lights.Expand("Log", none), none) && !Lights.IsLight("Log", none), "an object without a group is no light");
			Check(ref ok, Lights.StartsOn(P(Lights.OnProp, "1")) && Lights.StartsOn(P(Lights.OnProp, "on")) && !Lights.StartsOn(P(Lights.OnProp, "0")), "starts on: 1/on");
			Check(ref ok, Lights.SignalOf(" Lamps ") == "lights:lamps", "the group's signal (lights:lamps)");
			ObjAction a = ObjAction.Parse("light|group:Lamps|off");
			Check(ref ok, a != null && a.Verb == "light" && a.Target == "group:Lamps" && a.Arg == "off" && a.Shared && ObjAction.HasTarget("light") && ObjAction.HasArg("light"), "light|group:Lamps|off parses; it is shared (host)");
			Check(ref ok, a != null && a.Describe().Contains("Lamps"), "described (" + (a != null ? a.Describe() : "") + ")");
			ObjCheck c = ObjCheck.Parse("lights|Lamps|on");
			Check(ref ok, c != null && c.Kind == "lights" && c.Target == "Lamps" && c.Describe().Contains("Lamps"), "lights|Lamps|on parses (" + (c != null ? c.Describe() : "null") + ")");
			c = ObjCheck.Parse("light||off");
			Check(ref ok, c != null && c.Kind == "light" && c.Describe().Contains("off"), "light||off parses (" + (c != null ? c.Describe() : "null") + ")");
			Check(ref ok, PlayerFeatures.CheckArgPlaceholder("light") != null && PlayerFeatures.CheckArgPlaceholder("lights") != null, "the behaviour window shows on/off for both checks");
			List<string> evs = BehaviourProps.EventsFor(Lights.ObjectName, d).Select(v => v.Key).ToList();
			Check(ref ok, evs.Contains("lit") && evs.Contains("unlit") && evs.Contains("alllit") && evs.Contains("use") && PlayerFeatures.IsEvent("alllit"), "a light's events (" + string.Join(", ", evs.ToArray()) + ")");
			var q = new IslandQuest.Step { Type = "lights", Target = "Lamps", Count = 3 };
			Check(ref ok, IslandQuest.Types.Contains("lights") && IslandQuest.Counted("lights") && q.Describe().Contains("Lamps"), "the Light up step, counted (" + q.Describe() + ")");
			var facts = new PlayerFeatures.PlanFacts();
			for (int i = 0; i < 3; i++) PlayerFeatures.AddFacts(facts, Lights.ObjectName, P(Lights.GroupProp, "Lamps"));
			Check(ref ok, PlayerFeatures.StepProblem(facts, "lights", "lamps", 3) == null && PlayerFeatures.StepProblem(facts, "lights", "Lamps", 4) != null
				&& PlayerFeatures.StepProblem(facts, "lights", "Torches", 1) != null && PlayerFeatures.StepProblem(facts, "lights", "", 1) != null, "the plan check: an unknown group, too few lights");
			Check(ref ok, ContentCatalog.Hint(Lights.ObjectName).Contains("light"), "the catalog hint");
			CheckPlayerTables(ref ok);
			if (ok) Log("PASS: light puzzles' settings"); else Fail("light puzzles' settings");
		}

		[ConsoleCommand(name: "CILightRun", docs: "Dev, in a world (host): spawns 'ciplayer' with three Lamps (one starts on): switches them with the use key (lights shown, lit event), the lights check, the whole group's alllit event and signal, the Light up step, the light action switching the group off")]
		public static void LightRunCommand() { StartTest(LightRunRoutine()); }

		static IEnumerator LightRunRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			var lamp = new List<int>();
			int gate = -1, lever = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				for (int i = 0; i < 3; i++)
				{
					Dictionary<string, string> p = ObjectProps.Defaults(Lights.ObjectName);
					if (i == 0) { p["on.lit"] = "signal||lamp-lit"; p["on.alllit"] = "signal||lamps-reward"; }
					if (i == 2) p[Lights.OnProp] = "1";
					lamp.Add(add(Lights.ObjectName, -3f + i * 3f, 4f, p));
				}
				gate = add("Log", 0f, 7f, P(BehaviourProps.Use, "Open", "if.use", "lights|Lamps|on", "on.use", "signal||lamp-gate"));
				lever = add("Log", 3f, 7f, P(BehaviourProps.Use, "Pull", "on.use", "light|group:Lamps|off"));
				file.Props[IslandQuest.KeyTitle] = "CI lamps";
				file.Props[IslandQuest.KeySteps] = "lights|Lamps|3|\nreach|nowhere|1|";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, v => e = v);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			Func<int, IslandObjectRef> obj = i => e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			Func<int, bool> shows = i => { IslandObjectRef r = obj(i); PuzzleLight pl = r != null ? r.GetComponent<PuzzleLight>() : null; return pl != null && pl.ShowsLit; };
			try
			{
				yield return new WaitForSeconds(1f);
				Check(ref ok, lamp.All(i => obj(i) != null && obj(i).GetComponent<PuzzleLight>() != null), "every lamp is a puzzle light in the world");
				Check(ref ok, !Lights.Lit(e, obj(lamp[0])) && !Lights.Lit(e, obj(lamp[1])) && Lights.Lit(e, obj(lamp[2])) && Lights.LitCount(e, "lamps") == 1, "two off, the third starts on (" + Lights.LitCount(e, "Lamps") + " lit)");
				Check(ref ok, !shows(lamp[0]) && shows(lamp[2]), "shown off / on");
				if (obj(lamp[2]) != null) { PutPlayerNear(obj(lamp[2]).transform, 3f); Screenshot(new[] { "light" }); }
				for (int n = 0; n < 2; n++)
				{
					IslandObjectRef r = obj(lamp[n]);
					UseInteract u = r != null ? r.GetComponentInChildren<UseInteract>(true) : null;
					Check(ref ok, u != null, "lamp " + (n + 1) + " can be switched");
					if (u == null) continue;
					Traverse.Create(u).Method("Use").GetValue();
					yield return new WaitForSeconds(0.8f);
					Check(ref ok, Lights.Lit(e, r) && shows(lamp[n]), "lamp " + (n + 1) + " is on and shows it");
					if (n == 0)
					{
						Check(ref ok, Behaviours.HasSignal(e, "lamp-lit"), "its lit event ran");
						Behaviours.Fire(e, gate, "use", true);
						yield return new WaitForSeconds(0.3f);
						Check(ref ok, !Behaviours.HasSignal(e, "lamp-gate"), "lights|Lamps|on fails with one lamp still off");
						Check(ref ok, !Behaviours.HasSignal(e, Lights.SignalOf("Lamps")) && QuestTracker.StepOf(e) == 0, "no group signal yet, the step waits");
					}
				}
				yield return new WaitForSeconds(1.2f);
				Check(ref ok, Behaviours.HasSignal(e, Lights.SignalOf("Lamps")), "the whole group sent lights:lamps");
				Check(ref ok, Behaviours.HasSignal(e, "lamps-reward"), "the whole group ran alllit (its reward)");
				Check(ref ok, QuestTracker.StepOf(e) >= 1, "the Light up step is done (step " + QuestTracker.StepOf(e) + ")");
				Behaviours.Fire(e, gate, "use", true);
				yield return new WaitForSeconds(0.3f);
				Check(ref ok, Behaviours.HasSignal(e, "lamp-gate"), "lights|Lamps|on passes");
				Behaviours.Fire(e, lever, "use", true);
				yield return new WaitForSeconds(0.8f);
				Check(ref ok, Lights.LitCount(e, "Lamps") == 0 && lamp.All(i => !shows(i)), "light|group:Lamps|off switched the group off (" + Lights.LitCount(e, "Lamps") + " lit)");
				bool v, open;
				Behaviours.StateNow(e, obj(lamp[2]), out v, out open);
				Check(ref ok, open, "the lamp that starts on is kept off as its open bit (saved)");
				Check(ref ok, Lights.Holds(e, lamp[0], ObjCheck.Parse("light||off")) && !Lights.Holds(e, lamp[0], ObjCheck.Parse("light||on")), "light||off holds for this lamp");
			}
			finally { RemovePlayerIsland(e); }
			if (ok) Log("PASS: light puzzles in a world"); else Fail("light puzzles in a world");
		}
	}
}
