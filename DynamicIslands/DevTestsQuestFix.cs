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
			Log("PASS: CIQuestScan done");
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
	}
}
