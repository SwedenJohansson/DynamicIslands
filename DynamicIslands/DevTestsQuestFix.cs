using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIQuestCheck", docs: "Dev, anywhere (the main menu is quickest): questfix 11-14 - Check finds the quests that can't be done: the island's own quests (main and 2-9), a step line the game skips, a trimmed zone name, catching a creature the net can't catch, opening an empty chest, collecting a story item nothing gives")]
		public static void QuestCheckCommand(string[] args) { StartTest(QuestCheckRoutine()); }

		static IslandFile QcIsland(string name)
		{
			int res = 17;
			var heights = new float[res, res]; for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) heights[y, x] = 0.4f;
			return new IslandFile { TerrainSize = new Vector3(64f, 60f, 64f), HeightmapResolution = res, Heights = heights, Name = name };
		}

		static IslandObject QcChest(string title, string loot)
		{
			return new IslandObject { Name = "Placeable_Storage_Small", Position = Vector3.zero, Props = new Dictionary<string, string> { { ObjectProps.NoteTitle, title }, { ObjectProps.LootItems, loot } } };
		}

		static IEnumerator QuestCheckRoutine()
		{
			yield return null;
			bool ok = true;
			Func<List<PlanChecker.Finding>, string, bool> has = (fs, bit) => fs.Any(x => x.Text.IndexOf(bit, StringComparison.OrdinalIgnoreCase) >= 0);
			Func<List<PlanChecker.Finding>, string> show = fs => fs.Count + ": " + string.Join(" | ", fs.Select(x => x.Text.Length > 90 ? x.Text.Substring(0, 90) : x.Text).ToArray());

			// A broken island: every kind of step that can't be done, in the main quest and in quest 2
			IslandFile bad = QcIsland("ciquestcheck");
			bad.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = Vector3.zero, Props = new Dictionary<string, string> { { ObjectProps.ZoneId, "gate " } } });
			bad.Objects.Add(QcChest("Empty box", ""));
			bad.Props["quest.title"] = "Main";
			bad.Props["quest.steps"] = "reach|gate|1|walk in\nopen|Empty box|1|open the empty one\nReach|gate|1|capital kind is skipped";
			bad.Props["quest2.title"] = "Second";
			bad.Props["quest2.steps"] = "catch|Bear|1|catch a bear\ncollect|story:ciqc-missing|1|get the missing key";
			List<PlanChecker.Finding> found = null;
			string thrown = null;
			try { found = PlanChecker.QuestFindings(bad); } catch (Exception e) { thrown = e.GetType().Name + " " + e.Message; }
			Check(ref ok, thrown == null && found != null, "Check runs on the broken island" + (thrown != null ? " - " + thrown : ""));
			if (found != null)
			{
				Check(ref ok, !has(found, "trigger zone 'gate'"), "a zone id with a trailing space still matches the step (no false Problem) (" + show(found) + ")");
				Check(ref ok, has(found, "nothing in it"), "an open step on an empty chest is a Problem");
				Check(ref ok, has(found, "the game skips"), "a step line with an unknown kind ('Reach') is a Warning");
				Check(ref ok, has(found, "net launcher can't catch"), "catching a bear is a Problem (quest 2 is checked too)");
				Check(ref ok, has(found, "nothing on the island gives it"), "collecting a story item nothing gives is a Warning (the quest's own line no longer counts as a giver)");
				Check(ref ok, found.Any(x => x.Text.Contains("quest 2")), "findings name quest 2");
			}

			// A good island: the same steps, all doable - no findings
			IslandFile good = QcIsland("ciquestcheckok");
			good.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = Vector3.zero, Props = new Dictionary<string, string> { { ObjectProps.ZoneId, "gate" } } });
			good.Objects.Add(QcChest("Full box", "Plank*2;" + StoryItems.Ref("ciqc-key") + "*1"));
			good.Props["quest.title"] = "Main";
			good.Props["quest.steps"] = "reach|gate|1|walk in\nopen|Full box|1|open it";
			good.Props["quest2.title"] = "Second";
			good.Props["quest2.steps"] = "collect|story:ciqc-key|1|get the key";
			found = null; thrown = null;
			try { found = PlanChecker.QuestFindings(good); } catch (Exception e) { thrown = e.GetType().Name + " " + e.Message; }
			Check(ref ok, thrown == null && found != null && !found.Any(x => x.Level == PlanChecker.Level.Problem) && !has(found, "nothing on the island gives") && !has(found, "the game skips"), "a doable island has no quest Problems (" + (thrown ?? show(found)) + ")");

			if (ok) Log("PASS: CIQuestCheck - Check finds the quests that can't be done");
			else Fail("CIQuestCheck: see the FAIL lines above");
		}

		[ConsoleCommand(name: "CIQuestScan", docs: "Dev, anywhere: questfix 11-14 - Check's quest findings for every saved island (Mods\\DynamicIslands\\*.island). Writes Mods\\DynamicIslands\\quest_scan.txt: island<TAB>level<TAB>finding")]
		public static void QuestScanCommand(string[] args) { StartTest(QuestScanRoutine()); }

		static IEnumerator QuestScanRoutine()
		{
			string dir = Path.GetDirectoryName(IslandSpawner.PathFor("x"));
			string[] files = Directory.GetFiles(dir, "*" + Path.GetExtension(IslandSpawner.PathFor("x")));
			var lines = new List<string> { "# island\tlevel\tfinding" };
			int read = 0, failed = 0, withQuest = 0, flagged = 0;
			for (int i = 0; i < files.Length; i++)
			{
				string name = Path.GetFileNameWithoutExtension(files[i]);
				try
				{
					IslandFile f = IslandFile.Load(files[i]);
					read++;
					if (IslandQuest.CountIn(f.Props) == 0) continue;
					withQuest++;
					if (string.IsNullOrEmpty(f.Name)) f.Name = name;
					var fs = PlanChecker.QuestFindings(f).Where(x => x.Level != PlanChecker.Level.Tip).ToList();
					if (fs.Count > 0) flagged++;
					foreach (var x in fs) lines.Add(name + "\t" + x.Level + "\t" + x.Text.Replace('\n', ' ').Replace('\t', ' '));
				}
				catch (Exception e) { failed++; lines.Add(name + "\tError\t" + e.GetType().Name + " " + e.Message); }
				if (i % 20 == 19) yield return null;
			}
			string outPath = Path.Combine(dir, "quest_scan.txt");
			File.WriteAllLines(outPath, lines.ToArray());
			Log("quest scan: " + files.Length + " files, " + read + " read, " + failed + " failed, " + withQuest + " with a quest, " + flagged + " flagged -> " + outPath);
			if (read > 0 && failed == 0) Log("PASS: CIQuestScan done"); else Fail("CIQuestScan: " + read + " files read, " + failed + " failed");
		}

		[ConsoleCommand(name: "CIQuestNet", docs: "Dev, world (host): questfix 11-14 - a player drops quest steps not from the host; the host takes a player's plain step one on at most; a daily quest count from someone not in the session is refused and a player's is capped; a main story island with its quest open comes back past the 3-return cap")]
		public static void QuestNetCommand(string[] args) { StartTest(QuestNetRoutine()); }

		static IEnumerator QuestNetRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !CustomIslandSpawner.RaftPosition.HasValue) { Fail("CIQuestNet: host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			// (c) a player takes quest steps from the host only
			ulong overrideBefore = IslandNetwork.HostIdOverride;
			IslandNetwork.HostIdOverride = 777UL;
			Check(ref ok, IslandNetwork.NotHostSender(888UL) && !IslandNetwork.NotHostSender(777UL) && !IslandNetwork.NotHostSender(0UL), "a quest step from another player (not the host) is dropped; the host's is taken");
			IslandNetwork.HostIdOverride = overrideBefore;

			// (b) the daily quest: a count from someone not in the session refused, a player's capped
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			string taskBefore = DailyQuest.Current.Encode();
			bool trustWas = PlayerTrust.ForTests;
			try
			{
				PlayerTrust.ForTests = false;
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.DailyQuest });
				DailyQuest.DailyQuestCommand(new[] { "new", DailyQuest.Fish, "50" });
				int have = DailyQuest.Current.Have;
				DailyQuest.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.DailyQuest, Name = "add", Index = DailyQuest.Current.Day, Count = 999 }, new Network_UserId(4242UL));
				Check(ref ok, DailyQuest.Current.Have == have, "a daily quest count from someone not in the session is refused (" + have + " -> " + DailyQuest.Current.Have + ")");
				ulong me = RAPI.GetLocalPlayer().steamID.Id;
				DailyQuest.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.DailyQuest, Name = "add", Index = DailyQuest.Current.Day, Count = 999 }, new Network_UserId(me));
				Check(ref ok, DailyQuest.Current.Have == have + 10 && DailyQuest.Current.Open, "a count of 999 adds 10 at most: one message doesn't finish the task (" + DailyQuest.Current.Have + " of " + DailyQuest.Current.Need + ")");
			}
			finally { PlayerTrust.ForTests = trustWas; WorldOptions.Set(optionsBefore); DailyQuest.Current = DailyQuest.Task.Decode(taskBefore); }

			// (e) a main story island (no frequency) with its quest open comes back past MaxReturns; (c) the host takes a plain step one on at most
			string[] names = { "ciqf-story", "ciqf-plain" };
			IslandFile f = SampleIsland();
			f.Props[IslandQuest.KeyTitle] = "Come back";
			f.Props[IslandQuest.KeySteps] = "reach|cizone|1|walk in\nread|CI note|1|read it\nreach|cizone|1|walk in again";
			foreach (string n in names) { f.Name = n; f.Save(IslandSpawner.PathFor(n)); }
			WorldPlan planBefore = WorldDirector.Plan;
			var made = new List<IslandWorldState.Entry>();
			try
			{
				Vector3 far = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				far.y = 0f;
				for (int i = 0; i < names.Length; i++) made.Add(IslandWorldState.Add(names[i], far + Vector3.right * 2000f * i, null, false));
				foreach (var e in made)
				{
					e.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
					QuestTracker.Set(e, 1, 0, false);
					e.State[ReturningIslands.ReturnsKey] = new ObjectState { Active = true, Yield = ReturningIslands.MaxReturns, Day = 0 };
				}
				made[0].Rule = "qfstory"; made[1].Rule = "qfplain";
				var plan = new WorldPlan { Name = "CI questfix", Random = false };
				plan.Rules.Add(IntroRule.Parse("qfstory | island:ciqf-story | start | ahead:300 | | | beside"));
				plan.Rules.Add(IntroRule.Parse("qfplain | island:ciqf-plain | start | ahead:300 | |"));
				WorldDirector.Plan = plan;
				IntroRule sr = ReturningIslands.RuleThatBrought(made[0]), pr = ReturningIslands.RuleThatBrought(made[1]);
				Check(ref ok, sr != null && sr.MainStory && pr != null && !pr.MainStory, "the test rules: one main story, one not (" + (sr != null ? sr.ToLine() : "none") + " / " + (pr != null ? pr.ToLine() : "none") + ")");
				string whyStory = ReturningIslands.Why(made[0]), whyPlain = ReturningIslands.Why(made[1]);
				Check(ref ok, whyStory == "quest" && whyPlain == null, "after 3 returns with the quest open: the main story island still comes back (" + (whyStory ?? "no") + "), another island doesn't (" + (whyPlain ?? "no") + ")");
				Check(ref ok, QuestTracker.MayTakeStep(made[0].Id, 0, 2) && !QuestTracker.MayTakeStep(made[0].Id, 0, 3) && QuestTracker.MayTakeStep(made[0].Id, 0, 0), "the host takes a player's plain step one on (2), not two on (3)");
			}
			finally
			{
				WorldDirector.Plan = planBefore;
				IslandWorldState.RemoveIds(made.Select(e => e.Id).ToList(), false);
				foreach (string n in names) try { File.Delete(IslandSpawner.PathFor(n)); } catch { }
			}
			if (ok) Log("PASS: CIQuestNet - host-only steps, plain step cap, daily count checks, main story returns");
			else Fail("CIQuestNet: see the FAIL lines above");
		}

		/// <summary>A static dictionary by reflection: the last world's quest state that must not reach the next (questfix 11-14).</summary>
		static IDictionary QcStatic(Type t, string field)
		{
			var fi = t.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			return fi != null ? fi.GetValue(null) as IDictionary : null;
		}

		[ConsoleCommand(name: "CIQuestLeak", docs: "Dev: questfix 11-14 - 'CIQuestLeak set' in a world leaves a quest clock, a chest used by others and a signal lock; after another world loads 'CIQuestLeak check' finds none of them")]
		public static void QuestLeakCommand(string[] args) { StartTest(QuestLeakRoutine(args != null && args.Length > 0 ? args[0] : "")); }

		static IEnumerator QuestLeakRoutine(string mode)
		{
			yield return null;
			IDictionary timers = QcStatic(typeof(QuestTimers), "timers"), used = QcStatic(typeof(ContentState), "usedByOthersAt"), locks = QcStatic(typeof(Behaviours), "signalLocks");
			if (timers == null || used == null || locks == null) { Fail("CIQuestLeak: a field wasn't found (timers " + (timers != null) + ", usedByOthersAt " + (used != null) + ", signalLocks " + (locks != null) + ")"); yield break; }
			if (mode == "set")
			{
				timers[123456789L] = new QuestTimers.Shown { Island = 1, Quest = 0, Step = 0, Left = 30f, At = Time.time, Mode = QuestTimers.Run, Full = 30f };
				used[123456789L] = Time.time;
				locks["ciqf-lock"] = Time.time + 999f;
				Log("PASS: CIQuestLeak set (" + timers.Count + " clock(s), " + used.Count + " used, " + locks.Count + " lock(s))");
				yield break;
			}
			bool ok = true;
			Check(ref ok, !timers.Contains(123456789L), "the last world's quest clock is gone (" + timers.Count + " left)");
			Check(ref ok, !used.Contains(123456789L), "the last world's 'chest used by others' is gone");
			Check(ref ok, !locks.Contains("ciqf-lock"), "the last world's signal lock is gone");
			if (ok) Log("PASS: CIQuestLeak check - nothing of the last world's quests is left");
			else Fail("CIQuestLeak check: see the FAIL lines above");
		}

		[ConsoleCommand(name: "CIQuestFix2", docs: "Dev, anywhere (the main menu is quickest): questfix round 2 - a library update with fewer or reordered steps keeps the quest where it was; each quest step has its own 'done early' key; the editor's Save keeps each quest's number; animals wait for the nearest zone of their name; the checker warns of two zones with one name; the blueprint pickup patch is there. PASS/FAIL.")]
		public static void QuestFix2Command(string[] args) { StartTest(QuestFix2Routine()); }

		static IEnumerator QuestFix2Routine()
		{
			yield return null;
			bool ok = true;
			const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

			// (1) a library update: A,B,C -> A,C on C keeps C (step 1, progress kept); finished stays finished; reordered goes back
			Func<string, Dictionary<string, string>> props = steps => new Dictionary<string, string> { { "quest.title", "Q" }, { "quest.steps", steps }, { "quest2.title", "Q2" }, { "quest2.steps", steps } };
			var abc = props("reach|a|1|\nreach|b|1|\nkill|Boar|3|");
			var ac = props("reach|a|1|\nkill|Boar|3|");
			var cab = props("kill|Boar|3|\nreach|a|1|\nreach|b|1|");
			var st = new Dictionary<int, ObjectState> { { QuestTracker.StepKey, new ObjectState { Active = true, Yield = 2 } }, { QuestTracker.ProgressKey, new ObjectState { Active = true, Yield = 2 } }, { QuestTracker.EarlyKeyBase + 2, new ObjectState { Active = true, Yield = 1 } } };
			int changed = QuestTracker.RemapSteps(abc, ac, st);
			Check(ref ok, changed == 1 && st[QuestTracker.StepKey].Yield == 1 && st.ContainsKey(QuestTracker.ProgressKey) && st[QuestTracker.ProgressKey].Yield == 2 && !st.ContainsKey(QuestTracker.EarlyKeyBase + 2),
				"A,B,C -> A,C on step C: step 1 (C), its 2 of 3 kept, early credits cleared (" + changed + ", step " + st[QuestTracker.StepKey].Yield + ")");
			st = new Dictionary<int, ObjectState> { { QuestTracker.StepKey, new ObjectState { Active = true, Yield = 3 } } };
			QuestTracker.RemapSteps(abc, ac, st);
			Check(ref ok, st[QuestTracker.StepKey].Yield == 2, "a finished quest stays finished with fewer steps (step " + st[QuestTracker.StepKey].Yield + " of 2)");
			st = new Dictionary<int, ObjectState> { { QuestTracker.StepKey, new ObjectState { Active = true, Yield = 1 } }, { QuestTracker.ProgressKey, new ObjectState { Active = true, Yield = 1 } } };
			QuestTracker.RemapSteps(abc, cab, st);
			Check(ref ok, st[QuestTracker.StepKey].Yield == 0 && !st.ContainsKey(QuestTracker.ProgressKey), "reordered (the new first step not done): back to it, the old step's progress dropped (step " + st[QuestTracker.StepKey].Yield + ")");
			st = new Dictionary<int, ObjectState> { { QuestTracker.StepKey, new ObjectState { Active = true, Yield = 1 } } };
			Check(ref ok, QuestTracker.RemapSteps(abc, abc, st) == 0 && st[QuestTracker.StepKey].Yield == 1, "the same steps: nothing changes");
			st = new Dictionary<int, ObjectState> { { 0x40202, new ObjectState { Active = true, Yield = 9 } } };
			QuestTracker.RemapSteps(abc, ac, st);
			Check(ref ok, st[0x40202].Yield == 2, "quest 2 past its end stays finished (step " + st[0x40202].Yield + ")");

			// (8) every quest step has its own early key: none shared, none on a step or progress key, all below the visit key
			var early = typeof(QuestTracker).GetMethod("EarlyKeyOf", priv);
			if (early == null) Check(ref ok, false, "QuestTracker.EarlyKeyOf found");
			else
			{
				var seen = new HashSet<int>();
				for (int n = 0; n < IslandQuest.MaxQuests; n++) { seen.Add(n == 0 ? QuestTracker.StepKey : 0x40200 + n * 2); seen.Add(n == 0 ? QuestTracker.ProgressKey : 0x40201 + n * 2); }
				int shared = 0, outside = 0;
				for (int n = 0; n < IslandQuest.MaxQuests; n++)
					for (int k = 0; k < IslandQuest.MaxStepCount; k++)
					{
						int key = (int)early.Invoke(null, new object[] { n, k });
						if (!seen.Add(key)) shared++;
						if (key < 0x40000 || key >= WorldDirector.VisitKey) outside++;
					}
				int k63 = (int)early.Invoke(null, new object[] { 2, 63 }), k64 = (int)early.Invoke(null, new object[] { 2, 64 }), k70 = (int)early.Invoke(null, new object[] { 2, 70 });
				Check(ref ok, shared == 0 && outside == 0 && k63 != k64 && k64 != k70, "each of 9 quests x 99 steps has its own early key (shared " + shared + ", outside " + outside + "; quest 3 steps 64/65/71: " + k63.ToString("X") + "/" + k64.ToString("X") + "/" + k70.ToString("X") + ")");
			}

			// (6) the editor's Save: an emptied Quest 2 leaves its number empty, Quest 3 stays Quest 3
			var island = new Dictionary<string, string> { { "quest2.title", "Two" }, { "quest2.steps", "reach|a|1|" }, { "quest3.title", "Three" }, { "quest3.steps", "reach|c|1|" } };
			var working = new Dictionary<int, IslandQuest> { { 1, new IslandQuest() }, { 2, IslandQuest.From(island, 2, true) } };
			QuestEditorWindow.WriteFurther(island, working);
			Check(ref ok, !IslandQuest.From(island, 1, true).Exists && IslandQuest.From(island, 2, true).Exists && IslandQuest.From(island, 2, true).Steps[0].Target == "c", "Save: an emptied Quest 2 stays empty, Quest 3 keeps its number (" + string.Join(",", island.Keys.ToArray()) + ")");

			// (7) animals wait for the nearest zone of their name
			var made = new List<GameObject>();
			try
			{
				Func<string, float, TriggerZone> zone = (id, x) => { var g = new GameObject("ciqf2-zone"); made.Add(g); g.transform.position = new Vector3(x, -900f, 0f); var z = g.AddComponent<TriggerZone>(); z.Id = id; return z; };
				TriggerZone z1 = zone("cave", 0f), z2 = zone("cave", 100f), z3 = zone("other", 92f);
				var sg = new GameObject("ciqf2-spot"); made.Add(sg); sg.transform.position = new Vector3(90f, -900f, 0f);
				var spot = sg.AddComponent<CreatureSpawnPoint>();
				TriggerZone got = CreatureSpawner.ZoneOf(new[] { z1, z2, z3 }, spot, "cave");
				Check(ref ok, got == z2, "a spot waits for the nearest zone of its name (" + (got == z1 ? "the first" : got == z2 ? "the nearest" : got == null ? "none" : "another") + ")");
			}
			finally { foreach (GameObject g in made) UnityEngine.Object.Destroy(g); }

			// (7) the checker warns of two zones with one name that animals wait for - and not of one zone each
			IslandFile two = QcIsland("ciqf2-zones");
			two.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = Vector3.zero, Props = new Dictionary<string, string> { { ObjectProps.ZoneId, "cave" } } });
			two.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = Vector3.one * 20f, Props = new Dictionary<string, string> { { ObjectProps.ZoneId, "cave" } } });
			two.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = Vector3.one * 18f, Props = new Dictionary<string, string> { { ObjectProps.CreatureZone, "cave" } } });
			List<PlanChecker.Finding> traps = null;
			string thrown = null;
			try { traps = PlanChecker.FromFile(two, "ciqf2-zones", false).Traps; } catch (Exception ex) { thrown = ex.GetType().Name + " " + ex.Message; }
			Check(ref ok, traps != null && traps.Any(t => t.Text.Contains("more than one trigger zone is called 'cave'")), "two zones called 'cave' with a guard: a Warning" + (thrown != null ? " - " + thrown : ""));
			two.Objects[1].Props[ObjectProps.ZoneId] = "cave2";
			traps = null;
			try { traps = PlanChecker.FromFile(two, "ciqf2-zones", false).Traps; } catch (Exception ex) { thrown = ex.GetType().Name + " " + ex.Message; }
			Check(ref ok, traps != null && !traps.Any(t => t.Text.Contains("more than one trigger zone")), "zones with their own names: no Warning");

			// (8) the scrambled-blueprint pickup patch finds its method (its Prepare would skip it with a warning)
			var prepare = typeof(ScrambledBlueprintsPickup).GetMethod("Prepare", priv);
			bool prepared = false;
			try { prepared = prepare != null && (bool)prepare.Invoke(null, null); } catch { }
			Check(ref ok, prepared, "the scrambled-blueprint pickup patch's target is found (Prepare true)");

			if (ok) Log("PASS: CIQuestFix2 - library step remap, own early keys, editor quest numbers, nearest zone, duplicate-zone warning, pickup patch");
			else Fail("CIQuestFix2: see the FAIL lines above");
		}

		[ConsoleCommand(name: "CIQuestFix2W", docs: "Dev, world (host): questfix round 2 - a player's event on an island not loaded on the host is kept and done when it loads; the host's quest event with shared actions on an unloaded island is kept until it loads; a player's daily-quest counts are limited per 2 seconds. PASS/FAIL.")]
		public static void QuestFix2WorldCommand(string[] args) { StartTest(QuestFix2WorldRoutine()); }

		static IEnumerator QuestFix2WorldRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !CustomIslandSpawner.RaftPosition.HasValue) { Fail("CIQuestFix2W: host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
			string name = "ciqf2-far";
			string path = IslandSpawner.PathFor(name);
			IslandFile f = SampleIsland();
			f.Name = name;
			f.Props[BehaviourProps.EventKey("quest")] = "show|ciqf2-door";
			f.Save(path);
			IslandCache.ForgetFile(path);
			IslandWorldState.Entry e = null;
			GameObject fake = null;
			try
			{
				Vector3 far = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				far.y = 0f;
				e = IslandWorldState.Add(name, far, null, false);
				Check(ref ok, e != null && e.Root == null, "a far island, not loaded");
				if (e != null)
				{
					// (3) a player's event while the island isn't loaded here: kept, done when it is
					int queued = Behaviours.NotLoadedCount, done = Behaviours.NotLoadedDone;
					Behaviours.OnEventMessage(e.Id, 9999, "use", false, null, false); // (an object number the island has not: nothing runs)
					Check(ref ok, Behaviours.NotLoadedCount == queued + 1, "a player's event on an island not loaded here is kept (" + queued + " -> " + Behaviours.NotLoadedCount + ")");
					fake = new GameObject("ciqf2-fake-root");
					fake.transform.position = new Vector3(0f, -2000f, 0f);
					e.Root = fake;
					var tick = typeof(Behaviours).GetMethod("DoNotLoadedYet", priv);
					try { if (tick != null) tick.Invoke(null, null); } finally { e.Root = null; }
					Check(ref ok, tick != null && Behaviours.NotLoadedDone == done + 1 && Behaviours.NotLoadedCount == queued, "it's done once the island is there (" + done + " -> " + Behaviours.NotLoadedDone + ")");

					// (3b) the host's quest event with a "show" on an island not loaded: kept as pending, not lost
					Func<int> pending = () => e.State.Keys.Count(k => k >= Behaviours.PendingBase && k < Behaviours.PendingBase + 0x100000);
					int had = pending(), kept = Behaviours.KeptUnloaded;
					Behaviours.FireFromHost(e, Behaviours.IslandIndex, "quest");
					Check(ref ok, Behaviours.KeptUnloaded == kept + 1 && pending() == had + 1, "the host's quest event that shows something on an unloaded island is kept until it loads (pending " + had + " -> " + pending() + ")");
				}

				// (8) a player's daily-quest counts: 10 in 2 seconds at most, the rest dropped
				var allowed = typeof(DailyQuest).GetMethod("Allowed", priv);
				if (allowed == null) Check(ref ok, false, "DailyQuest.Allowed found");
				else
				{
					int dropped = DailyQuest.Dropped;
					ulong who = 919191UL;
					int a = (int)allowed.Invoke(null, new object[] { who, 6 }), b = (int)allowed.Invoke(null, new object[] { who, 6 }), c = (int)allowed.Invoke(null, new object[] { who + 1, 6 });
					Check(ref ok, a == 6 && b == 4 && c == 6 && DailyQuest.Dropped == dropped + 2, "a player's counts: 6 + 6 in 2 s take 6 + 4, another player's 6 are theirs (" + a + "/" + b + "/" + c + ", dropped " + (DailyQuest.Dropped - dropped) + ")");
				}
			}
			finally
			{
				if (e != null) { e.Root = null; IslandWorldState.RemoveIds(new List<int> { e.Id }, false); }
				if (fake != null) UnityEngine.Object.Destroy(fake);
				try { File.Delete(path); IslandCache.ForgetFile(path); } catch { }
			}
			if (ok) Log("PASS: CIQuestFix2W - events kept for unloaded islands, daily count rate limit");
			else Fail("CIQuestFix2W: see the FAIL lines above");
		}

		const string Qf15Island = "ciqf15";

		[ConsoleCommand(name: "CIQF15", docs: "Dev, two players (QF15, questfix): the host's answer for a player's quest steps, on host + box. setup (host): spawns 'ciqf15' near the raft (quest: read CI qf15 a / b / c, reward 3 planks). wait: until it is loaded here. at <s> (host): the quest to step s (0-based). step <s>: waits up to 20 s for step s here. drift (player): this player's step ahead, then behind, of the host's; each put right by the host's full list (Resync). last (player, at the last step): the step is sent and left to the host - not done here at once - and done, with one reward, when the host answers. drop (host): removes the island")]
		public static void Qf15Command(string[] args) { StartTest(Qf15Routine(args ?? new string[0])); }

		static IEnumerator Qf15Routine(string[] args)
		{
			string what = args.Length > 0 ? args[0] : "";
			string side = Raft_Network.IsHost ? "host" : "player";
			Func<IslandWorldState.Entry> entry = () => IslandWorldState.Islands.FirstOrDefault(i => i.Root != null && i.HostName == Qf15Island);
			if (what == "setup" || what == "drop")
			{
				if (!Raft_Network.IsHost) { Fail("QF15 " + what + ": as the host"); yield break; }
				if (what == "drop") { GwDrop(new GwIsland { E = entry() }, Qf15Island); Log("PASS: QF15 drop"); yield break; }
				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(Qf15Island, out at);
				f.Objects.Add(new IslandObject { Name = "Log", Position = at(0f, 4f), Props = P(BehaviourProps.Name, "qf15spot") });
				f.Props[IslandQuest.KeyTitle] = "CI qf15";
				f.Props[IslandQuest.KeySteps] = "read|CI qf15 a|1|\nread|CI qf15 b|1|\nread|CI qf15 c|1|";
				f.Props[IslandQuest.KeyReward] = "Plank*3";
				var g = new GwIsland();
				yield return GwSpawnFile(g, f, Qf15Island);
				if (g.E != null) Log("PASS: QF15 setup - " + Qf15Island + " is island " + g.E.Id);
				else Fail("QF15 setup: the island did not spawn");
				yield break;
			}
			if (what == "wait")
			{
				for (float t = 0f; t < 90f && entry() == null; t += 1f) yield return new WaitForSeconds(1f);
				if (entry() == null) Fail("QF15: '" + Qf15Island + "' did not come (" + side + ")");
				else Log("PASS: QF15 wait - the island is here (" + side + ", quest step " + QuestTracker.StepOf(entry()) + ")");
				yield break;
			}
			IslandWorldState.Entry e = entry();
			Network_Player player = RAPI.GetLocalPlayer();
			if (e == null || player == null) { Fail("QF15: no '" + Qf15Island + "' loaded or no player here (" + side + ")"); yield break; }
			IslandQuest q = QuestTracker.QuestOf(e, 0);
			if (!q.Exists || q.Steps.Count != 3) { Fail("QF15: the island's quest isn't here (" + side + ", " + q.Steps.Count + " steps)"); yield break; }
			int s0;
			if (what == "at" && args.Length > 1 && int.TryParse(args[1], out s0))
			{
				bool moved = QuestTracker.JumpTo(e, 0, s0);
				if (moved && QuestTracker.StepOf(e) == s0) Log("PASS: QF15 at - the host's quest at step " + s0);
				else Fail("QF15 at " + s0 + ": the host's step is " + QuestTracker.StepOf(e));
				yield break;
			}
			if (what == "step" && args.Length > 1 && int.TryParse(args[1], out s0))
			{
				for (float t = 0f; t < 20f && QuestTracker.StepOf(e) != s0; t += 0.5f) yield return new WaitForSeconds(0.5f);
				if (QuestTracker.StepOf(e) == s0) Log("PASS: QF15 step - step " + s0 + " here (" + side + ")");
				else Fail("QF15 step " + s0 + ": step " + QuestTracker.StepOf(e) + " here (" + side + ")");
				yield break;
			}
			if (Raft_Network.IsHost) { Fail("QF15 " + what + ": as a player (the box)"); yield break; }
			bool ok = true;
			if (what == "drift")
			{
				int host = QuestTracker.StepOf(e);
				if (host < 1 || host + 1 >= q.Steps.Count) { Fail("QF15 drift: needs the host's quest at a middle step (here " + host + ")"); yield break; }
				int[] drifts = { host + 1, host - 1 };
				foreach (int d in drifts)
				{
					// (this player's own view moved off: ahead as a step done alone, behind as a stale copy)
					e.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = d, Day = 0 };
					e.State[QuestTracker.ProgressKey] = new ObjectState { Active = true, Yield = 1, Day = 0 };
					Check(ref ok, QuestTracker.StepOf(e) == d, "this player's step set to " + d + " (the host has " + host + ")");
					IslandNetwork.Resync();
					for (float t = 0f; t < 30f && (QuestTracker.StepOf(e) != host || QuestTracker.ProgressOf(e, 0) != 0); t += 0.5f) yield return new WaitForSeconds(0.5f);
					Check(ref ok, e.Root != null && QuestTracker.StepOf(e) == host && QuestTracker.ProgressOf(e, 0) == 0,
						"a loaded island's step " + d + " put right by the host's full list: step " + QuestTracker.StepOf(e) + ", " + QuestTracker.ProgressOf(e, 0) + " counted (want " + host + ", 0)");
				}
				if (ok) Log("PASS: QF15 drift - this player's quest step, ahead and behind, put right by the host's full list");
				else Fail("QF15 drift: see the FAIL lines above");
				yield break;
			}
			if (what == "last")
			{
				int last = q.Steps.Count - 1;
				if (QuestTracker.StepOf(e) != last) { Fail("QF15 last: needs the last step here (" + QuestTracker.StepOf(e) + ")"); yield break; }
				Check(ref ok, IslandNetwork.HostAddsCounts, "the host counts players' steps (HostAddsCounts)");
				IslandObjectRef spot = ScObjOf(e, "qf15spot");
				if (spot != null) PutPlayerNear(spot.transform);
				yield return new WaitForSeconds(1.5f);
				int planks = player.Inventory.GetItemCount("Plank"), waiting = QuestTracker.WaitingForHost, done = 0;
				Action<int, int, int> onDone = (id, n, s) => { if (id == e.Id && n == 0 && s >= q.Steps.Count) done++; };
				QuestTracker.AdvancedAny += onDone;
				try
				{
					QuestTracker.Event(e, "read", "CI qf15 c");
					Check(ref ok, QuestTracker.StepOf(e) == last && done == 0 && QuestTracker.WaitingForHost == waiting + 1 && player.Inventory.GetItemCount("Plank") == planks,
						"the last step is sent to the host and not done here yet: step " + QuestTracker.StepOf(e) + ", done " + done + ", waiting " + (QuestTracker.WaitingForHost - waiting) + ", planks +" + (player.Inventory.GetItemCount("Plank") - planks));
					for (float t = 0f; t < 20f && QuestTracker.StepOf(e) <= last; t += 0.25f) yield return new WaitForSeconds(0.25f);
					yield return new WaitForSeconds(3f); // (a second answer or a second reward would come in this time)
					Check(ref ok, QuestTracker.StepOf(e) == q.Steps.Count && done == 1, "the host's answer finished the quest here, once (step " + QuestTracker.StepOf(e) + " of " + q.Steps.Count + ", done " + done + ")");
					Check(ref ok, player.Inventory.GetItemCount("Plank") == planks + 3, "the reward came once with the host's answer: planks +" + (player.Inventory.GetItemCount("Plank") - planks) + " (want +3)");
				}
				finally { QuestTracker.AdvancedAny -= onDone; }
				if (ok) Log("PASS: QF15 last - the last step waited for the host, then done with one reward");
				else Fail("QF15 last: see the FAIL lines above");
				yield break;
			}
			Fail("QF15: setup | wait | at <s> | step <s> | drift | last | drop");
		}
	}
}
