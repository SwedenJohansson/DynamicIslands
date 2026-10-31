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
using HMLLibrary;
using Debug = UnityEngine.Debug;

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
			// (the story features' events resume after a wait: their own pending keys, read back to the same event)
			var bt = Traverse.Create(typeof(Behaviours));
			bool resumes = PlayerFeatures.ResumableEvents.Length <= 8 && BehaviourProps.ObjectEvents.All(o => !PlayerFeatures.ResumableEvents.Contains(o));
			foreach (string ev in PlayerFeatures.ResumableEvents.SelectMany(v => new[] { v, v + "!" }))
			{
				int key = bt.Method("PendingKey", new[] { typeof(int), typeof(string) }).GetValue<int>(7, ev);
				bool kept = bt.Method("Resumable", new[] { typeof(string) }).GetValue<bool>(ev);
				string back = bt.Method("PendingEventOf", new[] { typeof(int) }).GetValue<string>(key);
				if (!kept || back != ev || key < Behaviours.MorePendingBase || ((key - Behaviours.MorePendingBase) >> 4) != 7) { resumes = false; Log("  " + ev + ": key " + key.ToString("X") + " reads back " + back); }
			}
			Check(ref ok, resumes, "talk, choice1-3, set, lit, unlit, alllit keep their actions after a wait (own pending keys)");
			Check(ref ok, bt.Method("PendingEventOf", new[] { typeof(int) }).GetValue<string>(Behaviours.PendingBase + (3 << 4) + 0) == "use", "the old pending keys still read as before");
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
					if (i == 2)
					{
						// (the last one as another player's: its key comes from the host; the count shows here too)
						ContentState.ApplyUsed(e.Id, Collectibles.KeyBase + piece[i], Behaviours.Today);
						yield return new WaitForSeconds(0.8f);
						Check(ref ok, Collectibles.LastShown != null && Collectibles.LastShown.StartsWith("Relics: 3 of 3"), "another player's piece shows the count here too (" + Collectibles.LastShown + ")");
						continue;
					}
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

		#region PF1 player notebook

		const string NotebookTestTitle = "CI notebook ";

		[ConsoleCommand(name: "CINotebook", docs: "Dev, world 'CI ...' (PF1): the player's own notebook. CINotebook unit - the file format round trip, the window (typing, new page, player held, saved on close, the file next to the world's save), copying a note in / write - adds a page 'CI notebook <world>' and saves / check - after loading the world again: its page is back, no other world's page / empty - a world with no 'CI notebook' page (another world's are not here)")]
		public static void NotebookCommand(string[] args) { StartTest(NotebookRoutine(args != null && args.Length > 0 ? args[0] : "unit")); }

		static IEnumerator NotebookRoutine(string what)
		{
			string world = SaveAndLoad.CurrentGameFileName ?? "";
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor() || !world.StartsWith("CI ")) { Fail("notebook: in a test world 'CI ...'"); yield break; }
			// (the notebook loads a frame after the world's id is known)
			float wait = Time.unscaledTime + 10f;
			while (PlayerNotebook.LoadedFor != SaveAndLoad.WorldGuid.ToString() && Time.unscaledTime < wait) yield return null;
			bool ok = true;
			Check(ref ok, PlayerNotebook.LoadedFor == SaveAndLoad.WorldGuid.ToString(), "the notebook belongs to this world (" + PlayerNotebook.LoadedFor + ")");
			string mine = NotebookTestTitle + world;
			if (what == "write")
			{
				PlayerNotebook.Pages.RemoveAll(p => p.Title == mine);
				PlayerNotebook.AddPage(mine, "Written in " + world + "\n=== not a new page\n\\ a backslash line\n");
				PlayerNotebook.Save();
				string to = PlayerNotebook.PathFor(PlayerNotebook.LoadedFor);
				Check(ref ok, File.Exists(to) && File.ReadAllText(to).Contains(mine), "saved to " + to);
				if (ok) Log("PASS: notebook write (" + world + ")"); else Fail("notebook write");
				yield break;
			}
			if (what == "check" || what == "empty")
			{
				List<PlayerNotebook.Page> ci = PlayerNotebook.Pages.Where(p => p.Title.StartsWith(NotebookTestTitle)).ToList();
				if (what == "empty") Check(ref ok, ci.Count == 0, "no test page in this world (" + string.Join(", ", ci.Select(p => p.Title).ToArray()) + ")");
				else
				{
					PlayerNotebook.Page p = ci.FirstOrDefault(x => x.Title == mine);
					Check(ref ok, p != null && p.Text == "Written in " + world + "\n=== not a new page\n\\ a backslash line\n", "this world's page is back, its text exact: " + (p == null ? "(missing)" : p.Text.Replace("\n", "|")));
					Check(ref ok, ci.All(x => x.Title == mine), "no other world's page: " + string.Join(", ", ci.Select(x => x.Title).ToArray()));
					Check(ref ok, (PlayerNotebook.LastPath ?? "").EndsWith(PlayerNotebook.FileName) && WorldCopy.RaftWorldFolder != null && Path.GetFullPath(Path.GetDirectoryName(PlayerNotebook.LastPath)) == Path.GetFullPath(WorldCopy.RaftWorldFolder), "read from the world's own folder: " + PlayerNotebook.LastPath);
				}
				if (ok) Log("PASS: notebook " + what + " (" + world + ")"); else Fail("notebook " + what);
				yield break;
			}

			// unit
			var sample = new List<PlayerNotebook.Page>
			{
				new PlayerNotebook.Page { Title = "First", Text = "one\n\ntwo\n" },
				new PlayerNotebook.Page { Title = "Tricky", Text = "=== looks like a page\n\\starts with a backslash\n# a hash" },
				new PlayerNotebook.Page { Title = "", Text = "" },
			};
			List<PlayerNotebook.Page> back = PlayerNotebook.Parse(PlayerNotebook.Serialize(sample));
			Check(ref ok, back.Count == 3 && Enumerable.Range(0, 3).All(i => back[i].Title == sample[i].Title && back[i].Text == sample[i].Text), "the file format gives back every page exactly: " + string.Join(" / ", back.Select(p => p.Title + ":" + p.Text.Replace("\n", "|")).ToArray()));
			string other = PlayerNotebook.PathFor(Guid.NewGuid().ToString());
			Check(ref ok, other != PlayerNotebook.PathFor(PlayerNotebook.LoadedFor) && !File.Exists(other), "another world has a file of its own (" + other + ")");

			int before = PlayerNotebook.Pages.Count;
			PlayerNotebookWindow.Open();
			yield return null;
			Check(ref ok, PlayerNotebookWindow.IsOpen && PlayerNotebookWindow.PlayerHeld, "the window opens and holds the player");
			PlayerNotebookWindow.NewPageForTest();
			PlayerNotebookWindow.TypeForTest("CI typed page", "typed line 1\ntyped line 2");
			yield return null;
			Check(ref ok, PlayerNotebook.Pages.Count == before + 1 && PlayerNotebook.Pages.Last().Title == "CI typed page" && PlayerNotebook.Pages.Last().Text == "typed line 1\ntyped line 2", "typing fills a new page");
			Check(ref ok, PlayerNotebookWindow.ListedPages == PlayerNotebook.Pages.Count, "the list shows every page (" + PlayerNotebookWindow.ListedPages + ")");
			Screenshot(new[] { "player_notebook" });
			yield return new WaitForSeconds(0.5f);
			PlayerNotebookWindow.Close();
			yield return null;
			string path = PlayerNotebook.PathFor(PlayerNotebook.LoadedFor);
			Check(ref ok, !PlayerNotebookWindow.IsOpen && File.Exists(path) && File.ReadAllText(path).Contains("=== CI typed page") && File.ReadAllText(path).Contains("typed line 2"), "saved on closing to " + path);
			Check(ref ok, WorldCopy.RaftWorldFolder == null || Path.GetFullPath(Path.GetDirectoryName(path)) == Path.GetFullPath(WorldCopy.RaftWorldFolder), "the file is next to the world's save (" + WorldCopy.RaftWorldFolder + ")");
			Check(ref ok, PlayerNotebook.CopyIn("CI copied note", "copied text") && PlayerNotebook.Pages.Last().Title == "CI copied note" && File.ReadAllText(path).Contains("copied text"), "a note copied in is a new page, saved at once");
			// (journal: the copy button on a page)
			PlayerNotebook.Pages.RemoveAll(p => p.Title == "CI typed page" || p.Title == "CI copied note");
			PlayerNotebook.MarkChanged();
			PlayerNotebook.Save();
			Check(ref ok, !File.ReadAllText(path).Contains("CI typed page"), "the test pages removed again");
			if (ok) Log("PASS: notebook unit"); else Fail("notebook unit");
		}

		#endregion

		#region PF4 camera fly-throughs

		const string FlyIsland = "cipf4fly";

		[ConsoleCommand(name: "CIFlythrough", docs: "Dev (PF4): camera fly-throughs. CIFlythrough unit - the curve passes every camera point in order with its waits, the action reads, Check warns on a path with fewer than 2 points / world - host, world 'CI ...': a lever flies the camera along a 3-point path (input held, Moment 'path' sent, the camera at the points, then back with the input given back), again and skipped (Esc), out of range nothing; picture shot_pf4_flythrough.png / editor - in the editor: 3 points placed, the path's line drawn, 'To my view' with undo, preview and back, a recording writes PNG frames (then deleted)")]
		public static void FlythroughCommand(string[] args) { StartTest(FlythroughRoutine(args != null && args.Length > 0 ? args[0] : "unit")); }

		static IEnumerator FlythroughRoutine(string what)
		{
			if (what == "world") { yield return FlyWorld(); yield break; }
			if (what == "editor") { yield return FlyEditor(); yield break; }
			bool ok = true;
			var keys = new List<CameraPath.Key>
			{
				new CameraPath.Key { Pos = new Vector3(0f, 10f, 0f), Look = Vector3.zero, Speed = 10f, Hold = 1f },
				new CameraPath.Key { Pos = new Vector3(30f, 12f, 0f), Look = new Vector3(30f, 0f, 10f), Speed = 5f, Hold = 0.5f },
				new CameraPath.Key { Pos = new Vector3(30f, 15f, 40f), Look = new Vector3(0f, 0f, 40f), Speed = 10f, Hold = 0f },
			};
			var fl = new CameraPath.Flight(keys);
			Vector3 pos, look;
			fl.Sample(0.5f, out pos, out look);
			Check(ref ok, pos == keys[0].Pos && look == keys[0].Look, "it waits at the first point for its hold");
			// (the second point is reached after the first wait and the first stretch)
			float firstLeg = fl.Duration - 1f - 0.5f;
			bool passes = false;
			for (float t = 1f; t < fl.Duration; t += 0.05f) { fl.Sample(t, out pos, out look); if (Vector3.Distance(pos, keys[1].Pos) < 0.01f) { passes = true; break; } }
			Check(ref ok, passes, "the curve passes the second point exactly");
			fl.Sample(fl.Duration + 5f, out pos, out look);
			Check(ref ok, pos == keys[2].Pos, "it ends at the last point");
			Check(ref ok, fl.Duration > 1.5f + 3f + 4f && fl.Duration < 1.5f + 3f * 1.3f + 8f * 1.3f + 1f, "its length from the distances and speeds (" + fl.Duration.ToString("F2") + " s, legs " + firstLeg.ToString("F2") + " s)");
			Vector3 prev = keys[0].Pos; float worst = 0f;
			for (float t = 0f; t <= fl.Duration; t += 0.02f) { fl.Sample(t, out pos, out look); worst = Mathf.Max(worst, Vector3.Distance(prev, pos)); prev = pos; }
			Check(ref ok, worst < 0.6f, "smooth: no jump between frames (largest step " + worst.ToString("F3") + " m)");
			Check(ref ok, CameraPath.RangeOf("") == CameraPath.DefaultRange && CameraPath.RangeOf("1") == 5f && CameraPath.RangeOf("300") == 300f, "the range reads (default " + CameraPath.DefaultRange + ", at least 5 m)");
			ObjAction act = ObjAction.Parse("flythrough|tour|300");
			Check(ref ok, act != null && act.Shared && BehaviourProps.Verbs.Contains("flythrough") && act.Describe().Contains("'tour'") && act.Describe().Contains("300 m"), "the action reads as '" + (act != null ? act.Describe() : "?") + "'");
			Check(ref ok, ObjAction.Parse("flythrough||") != null && ObjAction.Parse("flythrough||").Describe().Contains("'" + CameraPath.DefaultPath + "'"), "no path name: the path '" + CameraPath.DefaultPath + "'");
			Check(ref ok, PlaceableCatalog.Get(CameraPath.PointName) != null, "the object list has the camera point");
			// (once: an intro that doesn't replay)
			Check(ref ok, !CameraPath.OnceOf("300") && CameraPath.OnceOf("300|once") && CameraPath.OnceOf("|once") && CameraPath.RangeOf("300|once") == 300f, "'once' reads after the range");
			Check(ref ok, CameraPath.ArgOf("300", true) == "300|once" && CameraPath.ArgOf("300|once", false) == "300" && CameraPath.ArgOf("", true) == "|once", "... and is written back");
			ObjAction once = ObjAction.Parse("flythrough|tour|300|once");
			Check(ref ok, once != null && once.Arg == "300|once" && once.Describe().Contains("only the first time") && !act.Describe().Contains("only the first time"), "a once flight reads as '" + (once != null ? once.Describe() : "?") + "'");
			Check(ref ok, CameraPath.OnceKey("Tour") == CameraPath.OnceKey("tour") && CameraPath.OnceKey("tour") != CameraPath.OnceKey("intro") && CameraPath.OnceKey("tour") >= CameraPath.OnceKeyBase && CameraPath.OnceKey("") == CameraPath.OnceKey(CameraPath.DefaultPath), "its saved state key by path name");

			IslandFile bad = IslandGenerator.CreateFile(new IslandGenSettings { Seed = 1, Radius = 30f, Height = 6f }, "cipf4bad");
			bad.Objects.Add(new IslandObject { Name = "Note_Sign", Props = P(BehaviourProps.Name, "lever", BehaviourProps.EventPrefix + "use", "flythrough|tour|200") });
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.PathProp, "tour", CameraPath.OrderProp, "1") });
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.OrderProp, "1") });
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.PathProp, "Path", CameraPath.OrderProp, "2") });
			Check(ref ok, CameraPath.PointsIn(bad, "tour") == 1 && CameraPath.PointsIn(bad, "") == 2, "points counted by path (no path name = '" + CameraPath.DefaultPath + "', any case)");
			Check(ref ok, PlanChecker.FromFile(bad, "cipf4bad", false).Traps.Any(t => t.Text.Contains("fly-through") && t.Text.Contains("'tour'")), "Check warns on a path with one point");
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.PathProp, "tour", CameraPath.OrderProp, "2") });
			Check(ref ok, !PlanChecker.FromFile(bad, "cipf4bad", false).Traps.Any(t => t.Text.Contains("fly-through")), "... and not once it has two");
			if (ok) Log("PASS: flythrough unit"); else Fail("flythrough unit");
		}

		/// <summary>A test island: a lever flying the path 'tour' (3 points, the middle one looking at 'tower'), a lever out of range.</summary>
		static IslandFile FlyBuild()
		{
			var s = new IslandGenSettings { Seed = 4242, Radius = 50f, Height = 8f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, FlyIsland);
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(0f, 0f), 3), BehaviourProps.Name, "flylever", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "flythrough|tour|2000"));
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(4f, 0f), 3), BehaviourProps.Name, "farlever", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "flythrough|tour|5"));
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(-4f, 0f), 3), BehaviourProps.Name, "oncelever", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "flythrough|tour|2000|once"));
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(-10f, 10f), 3), BehaviourProps.Name, "tower"));
			Vector3[] at = { new Vector3(-20f, 25f, -20f), new Vector3(0f, 18f, 20f), new Vector3(25f, 22f, 0f) };
			for (int i = 0; i < at.Length; i++)
			{
				IslandObject o = ScObj(CameraPath.PointName, at[i], CameraPath.PathProp, "tour", CameraPath.OrderProp, (i + 1).ToString(), CameraPath.SpeedProp, "20", CameraPath.HoldProp, i == 1 ? "1" : "0");
				if (i == 1) o.Props[CameraPath.LookProp] = "tower";
				if (i == 2) o.Props[BehaviourProps.Use] = "Look"; // (no use on an unseen point)
				o.EulerRotation = new Vector3(30f, 45f, 0f);
				f.Objects.Add(o);
			}
			return f;
		}

		static IEnumerator FlyWorld()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("flythrough world: host, in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Note_Sign" });
			bool ok = true;
			IslandWorldState.Entry e = null;
			yield return RfSpawn(FlyBuild(), FlyIsland, x => e = x);
			if (e == null) yield break;
			Network_Player player = RAPI.GetLocalPlayer();
			List<CameraPath.Key> keys = CameraPath.WorldKeys(e, "tour");
			Check(ref ok, keys.Count == 3 && Behaviours.RefsOf(e).Count(r => CameraPath.IsPoint(r.ObjectName)) == 3, "the island's 3 camera points are in the world (" + keys.Count + ")");
			Check(ref ok, Behaviours.RefsOf(e).Where(r => CameraPath.IsPoint(r.ObjectName)).All(r => r.GetComponentsInChildren<Renderer>().Length == 0), "... and not seen (no marker in a world)");
			Transform tower = ScObjOf(e, "tower").transform;
			Check(ref ok, keys.Count == 3 && Vector3.Distance(keys[1].Look, tower.position) < 0.01f, "the middle point looks at 'tower'");

			int shown = CameraFlight.Shown, skipped = CameraFlight.Skipped;
			var sent = new List<IslandNetMessage>();
			IslandNetwork.Loopback = m => sent.Add(m);
			try { ScUse(e, "flylever"); }
			finally { IslandNetwork.Loopback = null; }
			yield return null;
			Check(ref ok, CameraFlight.Active && CameraFlight.Shown == shown + 1 && CameraFlight.InputHeld, "the lever starts the flight, the player's input held");
			Check(ref ok, sent.Any(m => m.Kind == IslandNetMessage.Moment && m.Name == "path" && m.Ids[0] == e.Id && m.Data.StartsWith("tour\n")), "every player is told (Moment 'path')");
			Transform cam = player.Camera != null ? player.Camera.transform : Camera.main.transform;
			// (the closest it came around the glide's end: a slow frame of a minimised Raft can step past the point, 10-26)
			float atFirst = 99f;
			for (float until = Time.unscaledTime + CameraMoment.Glide + 0.4f; Time.unscaledTime < until; )
			{
				if (keys.Count > 0) atFirst = Mathf.Min(atFirst, Vector3.Distance(cam.position, keys[0].Pos));
				yield return null;
			}
			Check(ref ok, atFirst < 1.5f, "the camera starts at the first point (" + atFirst.ToString("F2") + " m)");
			float best = 99f, bestLook = 180f, duration = CameraFlight.LastDuration;
			bool shot = false;
			for (float until = Time.unscaledTime + duration; Time.unscaledTime < until && CameraFlight.Active; )
			{
				if (keys.Count > 1)
				{
					float d = Vector3.Distance(cam.position, keys[1].Pos);
					if (d < best) { best = d; bestLook = Vector3.Angle(cam.forward, tower.position - cam.position); }
				}
				if (!shot && best < 0.3f && bestLook < 30f) { shot = true; Shot("pf4_flythrough"); }
				yield return null;
			}
			Check(ref ok, best < 0.3f && bestLook < 5f, "it passes the middle point (" + best.ToString("F2") + " m), looking at 'tower' (" + bestLook.ToString("F1") + " degrees)");
			yield return new WaitForSecondsRealtime(CameraMoment.Glide + 0.5f);
			float home = Vector3.Distance(cam.position, player.transform.position);
			Check(ref ok, !CameraFlight.Active && !CameraFlight.InputHeld && home < 3f, "over after " + duration.ToString("F1") + " s: the camera with the player (" + home.ToString("F1") + " m), the input given back");

			ScUse(e, "flylever");
			yield return new WaitForSecondsRealtime(1f);
			Check(ref ok, CameraFlight.Active && CameraFlight.InputHeld, "again: it flies (once per use, no loop)");
			EscKey.Press();
			yield return null;
			yield return null;
			yield return null;
			home = Vector3.Distance(cam.position, player.transform.position);
			Check(ref ok, !CameraFlight.Active && !CameraFlight.InputHeld && CameraFlight.Skipped == skipped + 1 && home < 3f, "Esc skips it: the camera back at once (" + home.ToString("F1") + " m), the input given back");
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, CanvasHelper.ActiveMenu != MenuType.PauseMenu, "... and Raft's pause menu is not left open");

			shown = CameraFlight.Shown;
			ScUse(e, "farlever");
			yield return new WaitForSeconds(0.3f);
			Check(ref ok, CameraFlight.Shown == shown && !CameraFlight.Active, "out of range (5 m of a point): no flight here");
			Check(ref ok, Behaviours.RefsOf(e).Where(r => CameraPath.IsPoint(r.ObjectName)).All(r => r.GetComponentInChildren<UseInteract>(true) == null), "a camera point with a use hint can't be used (nothing to look at)");

			// Once per world: the first use flies, the next doesn't, and the world keeps it
			int key = CameraPath.OnceKey("tour");
			Check(ref ok, !ContentState.IsUsed(e, key), "the once flight not flown yet");
			shown = CameraFlight.Shown;
			ScUse(e, "oncelever");
			yield return null;
			Check(ref ok, CameraFlight.Active && CameraFlight.Shown == shown + 1 && ContentState.IsUsed(e, key), "the once lever flies, and the island's state marks the path flown");
			// (as for the skip above: Esc a moment into the flight, not on its first frame, 10-26)
			yield return new WaitForSecondsRealtime(1f);
			EscKey.Press();
			yield return null;
			yield return null;
			yield return new WaitForSecondsRealtime(0.3f);
			ScUse(e, "oncelever");
			yield return new WaitForSeconds(0.3f);
			Check(ref ok, CameraFlight.Shown == shown + 1 && !CameraFlight.Active, "... and not a second time");
			ScUse(e, "flylever");
			yield return null;
			Check(ref ok, CameraFlight.Active && CameraFlight.Shown == shown + 2, "the every-time lever still flies the same path");
			EscKey.Press();
			yield return null;
			yield return null;
			yield return new WaitForSecondsRealtime(0.3f);
			RfAway(e);
			RfRemove(FlyIsland);
			if (ok) Log("PASS: flythrough world"); else Fail("flythrough world");
		}

		static IEnumerator FlyEditor()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 20f, 500f);
			var made = new List<EditorGameObject>();
			for (int i = 0; i < 3; i++)
			{
				EditorGameObject o = PlaceForTest(CameraPath.PointName, c0 + new Vector3(i * 15f, i * 3f, i % 2 * 10f), placed);
				if (o == null) { Fail("flythrough editor: no camera point placed"); yield break; }
				o.Props[CameraPath.PathProp] = "ci tour";
				o.Props[CameraPath.OrderProp] = (3 - i).ToString();
				o.Props[CameraPath.SpeedProp] = "30";
				made.Add(o);
			}
			yield return new WaitForSecondsRealtime(0.7f);
			Check(ref ok, CameraPath.PathsInEditor().Contains("ci tour") && CameraPath.EditorPoints("ci tour").Count == 3, "the path is listed for the action's ... button");
			Check(ref ok, CameraPath.LinesShown > 0, "its line is drawn (" + CameraPath.LinesShown + ")");
			List<CameraPath.Key> keys = CameraPath.EditorKeys("ci tour");
			Check(ref ok, keys.Count == 3 && Vector3.Distance(keys[0].Pos, made[2].transform.position) < 0.01f, "the points in their order (order 1 first)");

			Camera c = Camera.main;
			Vector3 camWas = c.transform.position;
			Vector3 pointWas = made[0].transform.position;
			Check(ref ok, ObjectInspector.CameraPointToView(made[0]) && Vector3.Distance(made[0].transform.position, camWas) < 0.01f, "'To my view' moves a point to the camera");
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			Check(ref ok, Vector3.Distance(made[0].transform.position, pointWas) < 0.01f, "... and Ctrl+Z puts it back");

			EditorCamera ec = c.GetComponent<EditorCamera>();
			Canvas ui = EditorUI.Canvas;
			bool uiWas = ui != null && ui.enabled;
			Check(ref ok, CameraPath.Preview("ci tour", false) == null && CameraFlight.Active && (ec == null || !ec.enabled), "Preview flies the editor camera (its own control off)");
			// (player 10-30: the inspector covered the preview)
			Check(ref ok, ui == null || !ui.enabled, "the editor's windows are hidden while it flies");
			yield return new WaitForSecondsRealtime(0.2f);
			Check(ref ok, keys.Count > 0 && Vector3.Distance(c.transform.position, keys[0].Pos) < 8f, "it starts at the first point");
			float wait = Time.unscaledTime + CameraFlight.LastDuration + 3f;
			while (CameraFlight.Active && Time.unscaledTime < wait) yield return null;
			yield return null;
			Check(ref ok, !CameraFlight.Active && Vector3.Distance(c.transform.position, camWas) < 0.01f && (ec == null || ec.enabled), "over: the editor camera back where it was, its control on");
			Check(ref ok, ui == null || ui.enabled == uiWas, "... and the editor's windows back");

			Check(ref ok, CameraPath.Preview("ci tour", true) == null && CameraFlight.Active, "Record trailer starts");
			for (int i = 0; i < 20; i++) yield return null;
			string folder = CameraFlight.LastFolder;
			EscKey.Press();
			yield return null;
			yield return null;
			int pngs = folder != null && Directory.Exists(folder) ? Directory.GetFiles(folder, "frame_*.png").Length : 0;
			Check(ref ok, !CameraFlight.Active && pngs >= 10 && pngs == CameraFlight.LastFrames && File.Exists(Path.Combine(folder, "frame_00001.png")), "it wrote " + pngs + " frames to " + folder + " (Esc stopped it)");
			Check(ref ok, Vector3.Distance(c.transform.position, camWas) < 0.01f, "... and the camera is back");
			Check(ref ok, ui == null || ui.enabled == uiWas, "... and the editor's windows after Esc");
			Check(ref ok, CameraPath.Preview("nothing here", false) != null && !CameraFlight.Active, "a path with no points says so, no flight");
			try { if (folder != null) Directory.Delete(folder, true); } catch { }
			foreach (EditorGameObject o in made) if (o != null) UnityEngine.Object.Destroy(o.gameObject);
			yield return new WaitForSecondsRealtime(0.7f);
			Check(ref ok, !CameraPath.PathsInEditor().Contains("ci tour"), "the test points removed");
			if (ok) Log("PASS: flythrough editor"); else Fail("flythrough editor");
		}

		#endregion

		#region PF5 island map and pings

		const string MapIsland = "cipf5map";

		[ConsoleCommand(name: "CIMinimap", docs: "Dev (PF5): the island map. CIMinimap unit - the map key's cycle (corner, full, off), zoom 1/2/4x and its view around the player, the map marker in the object list, the ping message / world - host, world 'CI ...': on a test island the map shows (picture taken), the quest step's target and a map marker on it, full screen and off, then pings: the message (island-local spot), at most one a second, a crew mate's ping shown and passed on, gone after its seconds; picture shot_pf5_map.png / editor - in the editor: a map marker placed, its inspector group, its text kept")]
		public static void MinimapCommand(string[] args) { StartTest(MinimapRoutine(args != null && args.Length > 0 ? args[0] : "unit")); }

		static IEnumerator MinimapRoutine(string what)
		{
			if (what == "world") { yield return MapWorld(); yield break; }
			if (what == "editor") { yield return MapEditor(); yield break; }
			bool ok = true;
			int mode = Minimap.Mode, zoom = Minimap.Zoom;
			Minimap.Mode = Minimap.Corner;
			Minimap.Cycle(); bool full = Minimap.Mode == Minimap.Full;
			Minimap.Cycle(); bool off = Minimap.Mode == Minimap.Off;
			Minimap.Cycle();
			Check(ref ok, full && off && Minimap.Mode == Minimap.Corner, "the map key: corner -> full -> off -> corner");
			Minimap.SetZoom(8); bool z4 = Minimap.Zoom == 4;
			Minimap.SetZoom(3); bool z2 = Minimap.Zoom == 2;
			Minimap.SetZoom(0);
			Check(ref ok, z4 && z2 && Minimap.Zoom == 1, "zoom is 1x, 2x or 4x");
			Rect all = Minimap.View(Vector3.zero);
			Check(ref ok, all.width == 1f && all.x == 0f && all.y == 0f, "at 1x the whole island shows");
			Minimap.SetZoom(4);
			Rect v = Minimap.View(Minimap.Center);
			Check(ref ok, Mathf.Abs(v.width - 0.25f) < 0.001f && Mathf.Abs(v.center.x - 0.5f) < 0.001f && Mathf.Abs(v.center.y - 0.5f) < 0.001f, "at 4x a quarter, around the player");
			Rect edge = Minimap.View(Minimap.Center + new Vector3(10000f, 0f, 10000f));
			Check(ref ok, edge.xMax <= 1.0001f && edge.yMax <= 1.0001f && edge.x >= 0f, "... kept inside the picture at its edge");
			Minimap.Mode = mode; Minimap.Zoom = zoom;
			Check(ref ok, PlaceableCatalog.Get(Minimap.MarkerName) != null, "the object list has the map marker");
			IslandNetMessage m = Minimap.MessageFor(new Vector3(1f, 2f, 3f), 77UL);
			Check(ref ok, m.Kind == IslandNetMessage.Ping && m.Kind == 40 && m.Offsets.Length == 3 && m.Data == "77", "the ping message: kind 40, a spot, the sender's id");
			if (ok) Log("PASS: minimap unit"); else Fail("minimap unit");
		}

		[ConsoleCommand(name: "CIMinimapOff", docs: "Dev (PF5): the Minimap checkbox. World 'CI ...' on a test island: off - the map key does nothing, the map is hidden at once and no picture is taken; on again - it shows. The setting is restored")]
		public static void MinimapOffCommand(string[] args) { StartTest(MinimapOffRoutine()); }

		static IEnumerator MinimapOffRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("minimap off: host, in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Note_Sign" });
			bool ok = true;
			bool was = Minimap.Enabled; int mode = Minimap.Mode, zoom = Minimap.Zoom;
			IslandWorldState.Entry e = null;
			yield return RfSpawn(MapBuild(), MapIsland, x => e = x);
			if (e == null) yield break;
			Minimap.Enabled = true;
			Minimap.Mode = Minimap.Corner;
			Minimap.SetZoom(1);
			PutPlayerNear(ScObjOf(e, "cimapstart").transform);
			yield return new WaitForSecondsRealtime(1.5f);
			Check(ref ok, Minimap.Island == e && Minimap.Shown, "on: the map shows on the island");
			Minimap.Enabled = false;
			Check(ref ok, !Minimap.Shown, "off: the open map is hidden at once");
			int renders = Minimap.RenderCount;
			Minimap.Cycle(); Minimap.SetZoom(4);
			Check(ref ok, Minimap.Mode == Minimap.Corner && Minimap.Zoom == 1, "off: the map key and zoom do nothing");
			Minimap.Mode = Minimap.Full;
			yield return new WaitForSecondsRealtime(1.5f);
			Check(ref ok, !Minimap.Shown && Minimap.RenderCount == renders && !Minimap.Render(), "off: never shown, no picture taken");
			Check(ref ok, WorldRules.ReadDefault(Minimap.SettingName) == "off", "off is saved as minimap=off");
			Minimap.Enabled = true;
			Minimap.Mode = Minimap.Corner;
			yield return new WaitForSecondsRealtime(1.5f);
			Check(ref ok, Minimap.Shown, "on again: the map shows");
			Check(ref ok, WorldRules.ReadDefault(Minimap.SettingName) == "on", "on is saved as minimap=on");
			// (the world option: no map for anyone, whatever this PC's box says)
			bool optWas = WorldOptions.On(WorldOptions.NoMinimap);
			var on = WorldOptions.Current.ToList(); on.Add(WorldOptions.NoMinimap);
			WorldOptions.Set(on);
			yield return new WaitForSecondsRealtime(1.5f);
			Check(ref ok, Minimap.Enabled && !Minimap.Available && !Minimap.Shown, "No minimap world option: the box is on, but the map is hidden");
			renders = Minimap.RenderCount; Minimap.Mode = Minimap.Corner; Minimap.Cycle(); Minimap.SetZoom(4);
			Check(ref ok, Minimap.Mode == Minimap.Corner && Minimap.Zoom == 1 && !Minimap.Render() && Minimap.RenderCount == renders, "No minimap: the map key and zoom do nothing, no picture");
			on.Remove(WorldOptions.NoMinimap); WorldOptions.Set(on);
			yield return new WaitForSecondsRealtime(1.5f);
			Check(ref ok, Minimap.Available && Minimap.Shown, "No minimap off again: the map shows");
			if (optWas) { on.Add(WorldOptions.NoMinimap); WorldOptions.Set(on); }
			Minimap.Enabled = was; Minimap.Mode = mode; Minimap.SetZoom(zoom);
			if (ok) Log("PASS: minimap off"); else Fail("minimap off");
		}

		[ConsoleCommand(name: "CIMinimapLook", docs: "Dev (PF5): how the island map looks. World 'CI ...': on the map test island logs the sea (Raft's water: height, layer, colours) and what is drawn on the picture by layer and height, saves the picture as it was before 10-31 (minimap_before.png) and now (minimap_after.png), the corner map and the full map on screen (minimap_corner.png, minimap_full.png). CIMinimapLook <folder> (default the mod folder)")]
		public static void MinimapLookCommand(string[] args) { StartTest(MinimapLook(args != null && args.Length > 0 ? string.Join(" ", args) : DynamicIslands.assetpath)); }

		static IEnumerator MinimapLook(string dir)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("minimap look: host, in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Note_Sign" });
			bool ok = true;
			IslandWorldState.Entry e = null;
			yield return RfSpawn(MapBuild(), MapIsland, x => e = x);
			if (e == null) yield break;
			int mode = Minimap.Mode;
			Minimap.Mode = Minimap.Corner;
			Minimap.SetZoom(1);
			PutPlayerNear(ScObjOf(e, "cimapstart").transform);
			yield return new WaitForSecondsRealtime(2f);
			Check(ref ok, Minimap.Island == e && Minimap.Shown && Minimap.Picture != null, "the map shows on the island");
			Vector3 c = Minimap.Center; float half = Minimap.HalfSize;
			Log("look: centre " + c.ToString("F1") + " half " + half.ToString("F0") + " root " + e.Root.transform.position.ToString("F1") + " SeaY " + Minimap.SeaY);
			UltimateWater.Water w = UnityEngine.Object.FindObjectOfType<UltimateWater.Water>();
			if (w == null) Log("look: no UltimateWater.Water");
			else
			{
				string h = "?"; try { h = w.GetHeightAt(c.x, c.z, w.Time).ToString("F2"); } catch (Exception ex) { h = ex.GetType().Name; }
				Log("look: water '" + w.name + "' y " + w.transform.position.y.ToString("F2") + " height here " + h + " layer " + w.gameObject.layer + " (" + LayerMask.LayerToName(w.gameObject.layer) + "), renderers " + string.Join(",", w.GetComponentsInChildren<Renderer>(true).Select(r => r.name + "@" + r.gameObject.layer).Take(6).ToArray()));
				try
				{
					foreach (object v in Enum.GetValues(typeof(UltimateWater.WaterMaterials.ColorParameter)))
						Log("look: water colour " + v + " = " + w.Materials.GetParameterValue((UltimateWater.WaterMaterials.ColorParameter)v));
				}
				catch (Exception ex) { Log("look: water colours: " + ex.Message); }
			}
			var rect = new Rect(c.x - half, c.z - half, half * 2f, half * 2f);
			var drawn = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy && rect.Overlaps(new Rect(r.bounds.min.x, r.bounds.min.z, r.bounds.size.x, r.bounds.size.z))).ToList();
			foreach (var g in drawn.GroupBy(r => r.gameObject.layer).OrderBy(g => g.Key))
				Log("look: layer " + g.Key + " (" + LayerMask.LayerToName(g.Key) + ") " + g.Count() + " renderers, y " + g.Min(r => r.bounds.min.y).ToString("F1") + ".." + g.Max(r => r.bounds.max.y).ToString("F1") + ": " + string.Join(", ", g.Select(r => r.name).Distinct().Take(5).ToArray()));
			var under = drawn.Where(r => r.bounds.max.y < Minimap.SeaY - Minimap.ShallowDepth).ToList();
			Log("look: wholly deeper than the sea plane (not on the map now): " + under.Count + ": " + string.Join(", ", under.Select(r => r.name).Distinct().Take(12).ToArray()));
			var terr = Terrain.activeTerrains.Where(t => t != null && rect.Overlaps(new Rect(t.transform.position.x, t.transform.position.z, t.terrainData.size.x, t.terrainData.size.z))).ToList();
			Log("look: terrains " + string.Join(", ", terr.Select(t => t.name + " y " + t.transform.position.y.ToString("F1") + ".." + (t.transform.position.y + t.terrainData.size.y).ToString("F1") + " layer " + t.gameObject.layer).ToArray()));
			try { Directory.CreateDirectory(dir); } catch { }
			// before 10-31: Raft's sea floor down to 900 m under the camera, a dark teal behind
			var go = new GameObject("CI_MapOld");
			Camera old = go.AddComponent<Camera>();
			old.enabled = false; old.orthographic = true; old.clearFlags = CameraClearFlags.SolidColor;
			old.backgroundColor = new Color(0.12f, 0.32f, 0.42f, 1f); old.cullingMask = ~(1 << 5); old.nearClipPlane = 1f; old.farClipPlane = 900f;
			old.orthographicSize = half; old.transform.position = c + Vector3.up * 400f; old.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
			var rt = new RenderTexture(512, 512, 16);
			old.targetTexture = rt;
			bool fog = RenderSettings.fog; RenderSettings.fog = false;
			try { old.Render(); } finally { RenderSettings.fog = fog; old.targetTexture = null; }
			SaveRt(rt, Path.Combine(dir, "minimap_before.png"));
			UnityEngine.Object.Destroy(go); rt.Release();
			Check(ref ok, Minimap.Render(), "the picture taken again");
			SaveRt(Minimap.Picture, Path.Combine(dir, "minimap_after.png"));
			Check(ref ok, GameObject.Find("CustomIslands_MapSea") == null && GameObject.Find("CustomIslands_MapShallows") == null, "the sea planes are hidden after the picture (no one else sees them)");
			yield return new WaitForSecondsRealtime(0.5f);
			ScreenCapture.CaptureScreenshot(Path.Combine(dir, "minimap_corner.png"));
			yield return new WaitForSecondsRealtime(1f);
			Minimap.Cycle();
			yield return new WaitForSecondsRealtime(1f);
			Check(ref ok, Minimap.Mode == Minimap.Full && Minimap.Shown, "full screen");
			ScreenCapture.CaptureScreenshot(Path.Combine(dir, "minimap_full.png"));
			yield return new WaitForSecondsRealtime(1f);
			Minimap.Mode = mode;
			RfAway(e);
			yield return new WaitForSecondsRealtime(1f);
			RfRemove(MapIsland);
			if (ok) Log("PASS: minimap look (" + dir + ")"); else Fail("minimap look");
		}

		static void SaveRt(RenderTexture rt, string path)
		{
			RenderTexture was = RenderTexture.active;
			RenderTexture.active = rt;
			var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
			t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
			t.Apply();
			RenderTexture.active = was;
			File.WriteAllBytes(path, t.EncodeToPNG());
			UnityEngine.Object.Destroy(t);
			Log("look: saved " + path);
		}

		/// <summary>A test island: a quest whose first step is to reach 'cimapgoal', a map marker 'Hut', a start spot.</summary>
		static IslandFile MapBuild()
		{
			var s = new IslandGenSettings { Seed = 5151, Radius = 50f, Height = 8f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, MapIsland);
			f.Props[IslandQuest.KeyTitle] = "CI map quest";
			f.Props[IslandQuest.KeySteps] = "reach|cimapgoal|1|Find the goal";
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(0f, 0f), 3), BehaviourProps.Name, "cimapstart"));
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(25f, 20f), 3), BehaviourProps.Name, "cimapgoal"));
			f.Objects.Add(ScObj(Minimap.MarkerName, ScDry(f, new Vector2(-20f, 15f), 3), Minimap.LabelProp, "Hut"));
			return f;
		}

		static IEnumerator MapWorld()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("minimap world: host, in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Note_Sign" });
			bool ok = true;
			IslandWorldState.Entry e = null;
			yield return RfSpawn(MapBuild(), MapIsland, x => e = x);
			if (e == null) yield break;
			int mode = Minimap.Mode;
			Minimap.Mode = Minimap.Corner;
			Minimap.SetZoom(1);
			Minimap.ClearPings();
			// (the island may spawn within its map reach of the raft)
			RfAway(e);
			yield return new WaitForSecondsRealtime(1f);
			Check(ref ok, Minimap.Island != e, "at sea (on the raft) not this island's map");
			Check(ref ok, Behaviours.RefsOf(e).Any(r => r.ObjectName == Minimap.MarkerName && r.GetComponentsInChildren<Renderer>().Length == 0), "the map marker is in the world, not seen");
			int renders = Minimap.RenderCount;
			PutPlayerNear(ScObjOf(e, "cimapstart").transform);
			yield return new WaitForSecondsRealtime(1.2f);
			Check(ref ok, Minimap.Island == e && Minimap.Shown && Minimap.RenderCount > renders, "on the island: its map in the corner, its picture taken (map island " + (Minimap.Island == null ? "none" : Minimap.Island.Name) + ", mode " + Minimap.Mode + ", renders " + (Minimap.RenderCount - renders) + ", player " + (e.Root == null ? "?" : new Vector2(RAPI.GetLocalPlayer().transform.position.x - e.Root.transform.position.x, RAPI.GetLocalPlayer().transform.position.z - e.Root.transform.position.z).magnitude.ToString("F0")) + " m from its root, " + Vector3.Distance(RAPI.GetLocalPlayer().transform.position, ScObjOf(e, "cimapstart").transform.position).ToString("F0") + " m from cimapstart, root at " + (e.Root == null ? "?" : e.Root.transform.position.ToString("F0")) + ", cimapstart at " + ScObjOf(e, "cimapstart").transform.position.ToString("F0") + ", land radius " + CustomIslandSpawner.LandRadius(e.Name).ToString("F0") + ")");
			Check(ref ok, Minimap.HalfSize >= CustomIslandSpawner.LandRadius(e.Name), "the picture holds the whole island (" + Minimap.HalfSize.ToString("F0") + " m each way)");
			Transform goal = ScObjOf(e, "cimapgoal").transform;
			Check(ref ok, Minimap.Marks.Any(k => k.Kind == Minimap.MarkQuest && Vector3.Distance(k.Pos, goal.position) < 0.1f), "the quest step's target on the map");
			Check(ref ok, Minimap.Marks.Any(k => k.Kind == Minimap.MarkMarker && k.Label == "Hut"), "the map marker 'Hut' on the map");
			Check(ref ok, Minimap.Marks.Any(k => k.Kind == Minimap.MarkMe), "the player on the map");
			Minimap.Cycle();
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, Minimap.Mode == Minimap.Full && Minimap.Shown && Minimap.RenderCount > renders + 1, "full screen: shown, the picture taken again");
			Shot("pf5_map");
			Minimap.Cycle();
			yield return null;
			yield return null;
			Check(ref ok, !Minimap.Shown, "off: no map");
			Minimap.Mode = Minimap.Corner;

			var sent = new List<IslandNetMessage>();
			int shown = Minimap.PingsShown, sounds = SoundLibrary.Played;
			Vector3 spot = goal.position;
			bool first, second;
			IslandNetwork.Loopback = x => sent.Add(x);
			try { first = Minimap.SendPing(spot); second = Minimap.SendPing(spot); }
			finally { IslandNetwork.Loopback = null; }
			IslandNetMessage msg = sent.FirstOrDefault(x => x.Kind == IslandNetMessage.Ping);
			Check(ref ok, first && !second && sent.Count(x => x.Kind == IslandNetMessage.Ping) == 1, "a ping is sent, a second right after is not (one a second)");
			Vector3 local = e.Root.transform.InverseTransformPoint(spot);
			Check(ref ok, msg != null && msg.Ids.Length == 1 && msg.Ids[0] == e.Id && Mathf.Abs(msg.Offsets[0] - local.x) < 0.01f && Mathf.Abs(msg.Offsets[2] - local.z) < 0.01f, "... with the island and the spot in its space");
			Check(ref ok, Minimap.PingsShown == shown + 1 && Minimap.Pings.Count == 1 && Minimap.Pings[0].World != null && SoundLibrary.Played > sounds && SoundLibrary.LastPlayed == Minimap.PingSound, "shown here: a beam, the sound");
			Check(ref ok, Minimap.Pings.Count == 1 && Vector3.Distance(Minimap.Pings[0].WorldPos, spot) < 0.05f, "... at the spot");

			// a crew mate's ping reaches the host: shown and passed to everyone
			IslandNetMessage mate = Minimap.MessageFor(spot + new Vector3(5f, 0f, 0f), 12345UL);
			sent.Clear();
			IslandNetwork.Loopback = x => sent.Add(x);
			try { Minimap.OnMessage(mate, 12345UL); }
			finally { IslandNetwork.Loopback = null; }
			Check(ref ok, Minimap.Pings.Count == 2 && Minimap.Pings.Any(p => p.From == 12345UL) && sent.Any(x => x.Kind == IslandNetMessage.Ping && x.Data == "12345"), "a crew mate's ping: shown here and passed on to everyone");
			yield return new WaitForSecondsRealtime(Minimap.PingSeconds + 0.5f);
			Check(ref ok, Minimap.Pings.Count == 0 && UnityEngine.Object.FindObjectsOfType<LineRenderer>().Count(l => l.name == "CustomIslands_Ping") == (Minimap.StartShown ? 1 : 0), "the pings are gone after " + Minimap.PingSeconds + " s");

			RfAway(e);
			yield return new WaitForSecondsRealtime(1.2f);
			Check(ref ok, Minimap.Island != e && !Minimap.Shown, "away from the island: no map");
			RfRemove(MapIsland);
			Minimap.Mode = mode;
			if (ok) Log("PASS: minimap world"); else Fail("minimap world");
		}

		static IEnumerator MapEditor()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			Check(ref ok, !Minimap.Shown, "no island map in the editor");
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Vector3 at = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 5f, 500f);
			EditorGameObject o = PlaceForTest(Minimap.MarkerName, at, placed);
			if (o == null) { Fail("minimap editor: no map marker placed"); yield break; }
			Check(ref ok, o.GetComponentsInChildren<Renderer>().Length > 0, "the marker is seen in the editor");
			// (the inspector is on the Objects tab: an earlier test may have left another tab open)
			EditorUI.SetTab(TAB.ObjectPlace);
			TransformGizmoSelect(o.transform);
			ObjectInspector.Refresh();
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>().Any(t => t.gameObject.activeInHierarchy && string.Equals(t.text, "Map marker (island map)", StringComparison.OrdinalIgnoreCase)), "its inspector group shows");
			PropsCommand.Change(o, P(Minimap.LabelProp, "CI hut"));
			Check(ref ok, ObjectProps.Get(o.Props, Minimap.LabelProp) == "CI hut", "its text is kept");
			DynamicIslands.EditorGizmoHandler.ClearTargets(false);
			UnityEngine.Object.Destroy(o.gameObject);
			if (ok) Log("PASS: minimap editor"); else Fail("minimap editor");
		}

		#endregion
	}
}
