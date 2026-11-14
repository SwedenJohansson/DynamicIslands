using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIQuestDeepCheck", docs: "Dev, anywhere (the main menu is quickest): questdeepB 11-14 - Check tells what the game drops without a word: an unreadable rule line, a renamed duplicate rule, a story place / done-when / distance it can't read, story places in a circle, Raft items that don't exist, action lines cut by a real line break, an island's own rule line it can't read")]
		public static void QuestDeepCheckCommand(string[] args) { StartTest(QuestDeepCheckRoutine()); }

		static IslandFile QdIsland(string name)
		{
			int res = 17;
			var heights = new float[res, res]; for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) heights[y, x] = 0.4f;
			return new IslandFile { TerrainSize = new Vector3(64f, 60f, 64f), HeightmapResolution = res, Heights = heights, Name = name };
		}

		static IslandObject QdChest(string title, string loot)
		{
			return new IslandObject { Name = "Placeable_Storage_Small", Position = Vector3.zero, Props = new Dictionary<string, string> { { ObjectProps.NoteTitle, title }, { ObjectProps.LootItems, loot } } };
		}

		static IEnumerator QuestDeepCheckRoutine()
		{
			yield return null;
			bool ok = true;
			Func<IEnumerable<PlanChecker.Finding>, string, bool> has = (fs, bit) => fs.Any(t => t.Text.IndexOf(bit, StringComparison.OrdinalIgnoreCase) >= 0);
			Func<IEnumerable<PlanChecker.Finding>, string> show = fs => fs.Count() + ": " + string.Join(" | ", fs.Select(t => t.Text.Length > 90 ? t.Text.Substring(0, 90) : t.Text).ToArray());

			// A plan with every parse leftover
			string text = "story=on\n" +
				"rule=a | island:ciqd-none | start | ahead | | | after:b | quest\n" +
				"rule=b | island:ciqd-none | start | ahead | | | after:a | quest\n" +
				"rule=loop | island:ciqd-none | start | ahead | | | after:loop | quest\n" +
				"rule=c | island:ciqd-none | start | ahead:far | | | sideways | finished\n" +
				"rule=c | island:ciqd-none | start | ahead\n" +
				"rule=d | island:ciqd-none | whenever | ahead\n";
			WorldPlan plan = WorldPlan.Parse("ciquestdeep", text);
			Check(ref ok, plan.UnreadRules.Count == 1 && plan.Renamed.Count == 1 && plan.Renamed[0] == "c -> cb", "Parse keeps the unreadable rule and the renamed duplicate (" + plan.UnreadRules.Count + ", " + string.Join(",", plan.Renamed.ToArray()) + ")");
			IntroRule c = plan.Rules.FirstOrDefault(r => r.Id == "c");
			Check(ref ok, c != null && c.BadPlace == "sideways" && c.BadDone == "finished" && c.BadDistance == "far", "Parse keeps what it couldn't read of rule c (" + (c == null ? "no rule" : c.BadPlace + "/" + c.BadDone + "/" + c.BadDistance) + ")");
			List<PlanChecker.Finding> found = null;
			string thrown = null;
			try { found = PlanChecker.Check(plan, false, false); } catch (Exception e) { thrown = e.GetType().Name + " " + e.Message; }
			Check(ref ok, thrown == null && found != null, "Check runs on the plan" + (thrown != null ? " - " + thrown : ""));
			if (found != null)
			{
				Func<string, bool> problem = bit => found.Any(t => t.Level == PlanChecker.Level.Problem && t.Text.IndexOf(bit, StringComparison.OrdinalIgnoreCase) >= 0);
				Check(ref ok, problem("rule line the game can't read"), "an unreadable rule line is a Problem (" + show(found) + ")");
				Check(ref ok, problem("c -> cb"), "a renamed duplicate rule is a Problem");
				Check(ref ok, problem("'sideways') can't be read"), "an unreadable story place is a Problem");
				Check(ref ok, has(found, "'finished') can't be read"), "an unreadable done-when is told");
				Check(ref ok, has(found, "('far') isn't a number"), "an unreadable distance is told");
				Check(ref ok, problem("(a circle)"), "two islands after each other in the story are a Problem");
				Check(ref ok, problem("after 'loop', itself"), "an island after itself in the story is a Problem");
			}
			// A good plan: none of them
			WorldPlan good = WorldPlan.Parse("ciquestdeepok", "story=on\nrule=a | island:ciqd-none | start | ahead:300 | | | first | quest\nrule=b | island:ciqd-none | start | ahead | | | after:a | quest\n");
			found = null; thrown = null;
			try { found = PlanChecker.Check(good, false, false); } catch (Exception e) { thrown = e.GetType().Name + " " + e.Message; }
			Check(ref ok, thrown == null && found != null && good.UnreadRules.Count == 0 && good.Renamed.Count == 0 && !has(found, "can't be read") && !has(found, "isn't a number") && !has(found, "a circle") && !has(found, "rule line the game can't read"),
				"a plan with readable rules has none of these findings (" + (thrown ?? show(found)) + ")");

			// An island with data the game drops
			IslandFile bad = QdIsland("ciquestdeep");
			bad.Objects.Add(QdChest("Box", "Copper*2;Plank*1"));
			bad.Objects.Add(new IslandObject { Name = "Placeable_Storage_Small", Position = Vector3.zero, Props = new Dictionary<string, string> {
				{ ObjectProps.NoteTitle, "Note" }, { ObjectProps.NoteText, "x" }, { BehaviourProps.EventPrefix + "use", "journal|Page|First line\nsecond line of the page\ngive||Firework*1" } } });
			bad.Props["quest.title"] = "Main";
			bad.Props["quest.steps"] = "open|Box|1|open it";
			bad.Props["quest.reward"] = "Salt*3";
			bad.Props[WorldDirector.IslandRulesKey] = "next | island:ciqd-none | quest | near\nnext2 | island:ciqd-none | whenever | near";
			PlanChecker.Facts x = null; thrown = null;
			try { x = PlanChecker.FromFile(bad, bad.Name, false); } catch (Exception e) { thrown = e.GetType().Name + " " + e.Message; }
			Check(ref ok, thrown == null && x != null, "Facts of the island" + (thrown != null ? " - " + thrown : ""));
			if (x != null)
			{
				bool items = ItemManager.GetAllItems() != null && ItemManager.GetAllItems().Count > 0;
				PlanChecker.Finding it = x.Traps.FirstOrDefault(t => t.Text.Contains("items Raft doesn't have"));
				Check(ref ok, !items || (it != null && it.Text.Contains("'Copper'") && it.Text.Contains("'Firework'") && it.Text.Contains("'Salt'") && !it.Text.Contains("'Plank'")),
					"loot, give and reward items Raft doesn't have are told (" + show(x.Traps) + ")" + (items ? "" : " - Raft's items not loaded, skipped"));
				Check(ref ok, has(x.Traps, "second line of the page"), "an action line cut by a real line break is told");
				Check(ref ok, x.Traps.Any(t => t.Level == PlanChecker.Level.Problem && t.Text.Contains("whenever")), "an island's own rule line the game can't read is a Problem");
				Check(ref ok, !has(x.Traps, "\"next |"), "the island's readable rule isn't told");
			}

			if (ok) Log("PASS: CIQuestDeepCheck - Check tells what the game drops without a word");
			else Fail("CIQuestDeepCheck: see the FAIL lines above");
		}
	}
}
