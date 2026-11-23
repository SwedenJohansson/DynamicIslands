using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Quest softlock audit 11-23 (docs/claude/QUEST_SOFTLOCK_AUDIT.md): ways a storyline could never finish.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIStoryNoteStale", docs: "Dev, world (host, 'CI ...'): a plan's 'note:' done-when whose number is no note with a text on the island (rebuilt after the plan - The Last Post's note:2495): the island's quest finishes the story step, another note read there doesn't; a one-note island: that note read finishes it; a note by its title; a real number as before; the plan check takes a title and checks a beside rule's done-when (TEST_CATALOGUE QSL1)")]
		public static void StoryNoteStaleCommand(string[] args) { StartTest(StoryNoteStaleRoutine()); }

		[ConsoleCommand(name: "CIRecipeJournalBreak", docs: @"Dev, anywhere: a recipe's prop value with \\n keeps a written \n, so an event's journal page with paragraphs stays one action and its page gets every paragraph - Camp Blackwater's, The Dead Letter Office's and Wreckers' Cove's islanders' pages were cut after their first line (TEST_CATALOGUE QSL2)")]
		public static void RecipeJournalBreakCommand(string[] args)
		{
			bool ok = true;
			// (as camp_blackwater.recipe's islander: a journal page, then a message, in one event - the recipe's own text)
			string line = @"journal|The boathouse cellar|Shogo:\\n\\nIn the knoll there is a cellar.\\n\\nThe hatch is the strongroom.\nmessage||Shogo looks at the boathouse.";
			List<ObjAction> acts = ObjAction.ParseLines(Unescape(line));
			Check(ref ok, acts.Count == 2 && acts[0].Verb == "journal" && acts[1].Verb == "message", "two actions, journal then message (" + string.Join(", ", acts.Select(a => a.Verb).ToArray()) + ")");
			string page = acts.Count > 0 ? acts[0].Arg.Replace(@"\n", "\n") : ""; // (as Behaviours' journal action)
			Check(ref ok, page.Contains("In the knoll") && page.Contains("strongroom") && page.Contains("\n\n"), "the page keeps every paragraph, with its new lines (" + page.Length + " letters)");
			Check(ref ok, ObjAction.ParseLines(Unescape(@"show|a\nhide|b")).Count == 2, @"a plain \n still splits actions");
			Check(ref ok, Unescape(@"a\nb") == "a\nb" && Unescape(@"a\\nb") == @"a\nb", @"\n is a new line, \\n a written \n");
			if (ok) Log("PASS: recipe journal break"); else Fail("recipe journal break");
		}

		/// <summary>Every blueprint a library storyline's "blueprint" step names (content/recipes, 11-23).</summary>
		static readonly string[] StepBlueprints = {
			"Blueprint_ZiplineTool", "Blueprint_Recycler", "Blueprint_HeadLight", "Blueprint_BatteryCharger", "Blueprint_ZiplineBase", "Blueprint_Machete",
			"Blueprint_Fueltank", "Blueprint_TitaniumTools", "Blueprint_Reciever", "Blueprint_Pipe_Fuel", "Blueprint_MotorWheel", "Blueprint_MetalDetector",
			"Blueprint_HeadLight_Advanced", "Blueprint_ElectricPurifier", "Blueprint_BiofuelExtractor", "Blueprint_BatteryAdvanced", "Blueprint_BackpackAdvanced",
			"Blueprint_Antenna", "Blueprint_ZiplineToolElectric", "Blueprint_WindTurbine", "Blueprint_WaterTank", "Blueprint_Storage_Large", "Blueprint_SteeringWheel",
			"Blueprint_SmelterElectric", "Blueprint_Pipe_Water", "Blueprint_Firework", "Blueprint_EngineControls", "Blueprint_ElectricGrill",
			"Blueprint_BiofuelExtractorAdvanced", "Blueprint_AnchorStationaryAdvanced", "Blueprint_Cropplot_Medium_Tier3", "Blueprint_Cropplot_Large_Tier3",
			"Blueprint_DetailPlank", "Blueprint_Cropplot_Small_Tier3", "Blueprint_Canteen" };

		[ConsoleCommand(name: "CIBlueprintTeaches", docs: "Dev, anywhere Raft's items are loaded: every blueprint a library 'blueprint' step or Raft's story islands name is a Raft item and teaches at least one recipe - a blueprint that teaches none counts only while carried, and once learned (used up) its step could never be done (TEST_CATALOGUE QSL3)")]
		public static void BlueprintTeachesCommand(string[] args)
		{
			if ((ItemManager.GetAllItems() ?? new List<Item_Base>()).Count == 0) { Fail("blueprint teaches: Raft's items are not loaded"); return; }
			bool ok = true;
			var all = new List<string>(StepBlueprints);
			foreach (string b in BlueprintSteps.StoryBlueprints()) if (!all.Contains(b)) all.Add(b);
			var none = all.Where(b => !BlueprintSteps.Exists(b)).ToList();
			var empty = all.Where(b => BlueprintSteps.Exists(b) && BlueprintSteps.Teaches(b).Count == 0).ToList();
			Check(ref ok, none.Count == 0, "every blueprint is a Raft item (" + all.Count + "; not: " + string.Join(", ", none.ToArray()) + ")");
			Check(ref ok, empty.Count == 0, "every blueprint teaches a recipe (none: " + string.Join(", ", empty.ToArray()) + ")");
			if (ok) Log("PASS: blueprint teaches"); else Fail("blueprint teaches");
		}

		[ConsoleCommand(name: "CIRefusedTakeBack", docs: "Dev, world (host): a player's event the host refuses gives back the crew's story item its take check used up (the player's machine took it before asking): refused on a loaded island (as when another player used it at the same moment) and on an island not loaded on the host; a yes keeps it used; the find count stays (TEST_CATALOGUE QSL4)")]
		public static void RefusedTakeBackCommand() { StartTest(RefusedTakeBackRoutine()); }

		static IEnumerator RefusedTakeBackRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const string card = "qslcard";
			string check = "take|story:" + card + "|1";
			var player = new Network_UserId(4242UL);
			var sent = new List<IslandNetMessage>();
			Func<int> count = () => StoryBook.Count(card);
			Action clientTakes = () => { if (count() == 0) StoryBook.Give(card, 1); StoryBook.Take(card, 1); }; // (the player's Passes: taken before asking)

			// (a) refused on a loaded island
			int zi = -1;
			IslandFile f = PlayerTestIsland((file, add) => { zi = add(ContentCatalog.TriggerZone, -8f, 8f, P(ObjectProps.ZoneId, "qsl4zone", ObjectProps.ZoneRadius, "2", BehaviourProps.CheckKey("enter"), check)); });
			IslandWorldState.Entry isl = null;
			yield return SpawnPlayerIsland(f, x => isl = x);
			if (isl == null || isl.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(isl); yield break; }
			try
			{
				IslandNetwork.Loopback = sent.Add;
				yield return new WaitForSeconds(1f);
				Check(ref ok, Behaviours.ChecksOf(isl, zi, "enter").Count == 1, "the zone has its take check");
				clientTakes();
				int found = StoryBook.FoundCount(card), given = Behaviours.GivenBackOnRefusal;
				Check(ref ok, count() == 0, "the player's take used the card up (" + count() + ")");
				Behaviours.RefuseForTests = true;
				Behaviours.OnEventMessage(isl.Id, zi, "enter", false, player, true);
				Behaviours.RefuseForTests = false;
				Check(ref ok, count() == 1 && Behaviours.GivenBackOnRefusal == given + 1, "refused: the card is the crew's again (" + count() + ")");
				Check(ref ok, StoryBook.FoundCount(card) == found, "found as often as before (" + found + " -> " + StoryBook.FoundCount(card) + ")");
				// (a yes: it stays used)
				clientTakes();
				Behaviours.OnEventMessage(isl.Id, zi, "enter", false, player, true);
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, count() == 0 && Behaviours.GivenBackOnRefusal == given + 1, "a yes keeps the card used (" + count() + ")");
			}
			finally { Behaviours.RefuseForTests = false; IslandNetwork.Loopback = null; RemovePlayerIsland(isl); }

			// (b) an island not loaded on the host
			string far = "ciqsl4-far", farPath = IslandSpawner.PathFor(far);
			IslandFile ff = SampleIsland();
			ff.Name = far;
			int oi = ff.Objects.Count > 1 ? 1 : 0;
			if (ff.Objects[oi].Props == null) ff.Objects[oi].Props = new Dictionary<string, string>();
			ff.Objects[oi].Props[BehaviourProps.CheckKey("use")] = check;
			ff.Save(farPath);
			IslandCache.ForgetFile(farPath);
			IslandWorldState.Entry fe = null;
			try
			{
				IslandNetwork.Loopback = sent.Add;
				Vector3 at = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				at.y = 0f;
				fe = IslandWorldState.Add(far, at, null, false);
				Check(ref ok, fe != null && fe.Root == null, "a far island, not loaded here");
				if (fe != null)
				{
					clientTakes();
					int given = Behaviours.GivenBackOnRefusal;
					Behaviours.OnEventMessage(fe.Id, oi, "use", false, player, true);
					Check(ref ok, count() == 1 && Behaviours.GivenBackOnRefusal == given + 1, "not loaded on the host: the player gets its otherwise and the card back (" + count() + ")");
					Behaviours.OnEventMessage(fe.Id, oi, "use!", false, player, true);
					Check(ref ok, count() == 1 && Behaviours.GivenBackOnRefusal == given + 1, "its otherwise part gives nothing (" + count() + ")");
				}
			}
			finally
			{
				IslandNetwork.Loopback = null;
				if (fe != null) IslandWorldState.RemoveIds(new List<int> { fe.Id }, false);
				try { File.Delete(farPath); IslandCache.ForgetFile(farPath); } catch { }
				if (count() > 0) StoryBook.Take(card, count());
			}
			if (ok) Log("PASS: refused take back"); else Fail("refused take back");
		}

		static IEnumerator StoryNoteStaleRoutine()
		{
			yield return null;
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("story note stale: needs a world, as the host"); yield break; }
			bool ok = true;
			const string two = "cinotestale", one = "cinotestale1";
			// (two islands: two notes and a quest, and one note and no quest; list entries far away, never loaded)
			int first, second, zone, only, oneZone;
			{
				var s = new IslandGenSettings { Seed = 8997, Radius = 25f, Height = 5f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
				IslandFile f = IslandGenerator.CreateFile(s, two);
				var k = new MapKit(f, 5);
				Vector2 at = k.Find(k.Mid, 15f, MapKit.Dry, 4f) ?? k.Mid;
				zone = f.Objects.IndexOf(k.Zone(at, "landing", 4f, ""));
				first = f.Objects.IndexOf(k.Note("Note_Paper", at + new Vector2(3f, 0f), "Letter addressed to your raft", "Come to the pier."));
				second = f.Objects.IndexOf(k.Note("Note_Paper", at + new Vector2(-3f, 0f), "Dead letter", "Sealed."));
				k.Quest("Stale note", "Walk to the landing.", "Done.", "", "reach|landing|1|Walk to the landing");
				f.Save(IslandSpawner.PathFor(two));
				IslandFile g = IslandGenerator.CreateFile(s, one);
				var k1 = new MapKit(g, 6);
				Vector2 at1 = k1.Find(k1.Mid, 15f, MapKit.Dry, 4f) ?? k1.Mid;
				oneZone = g.Objects.IndexOf(k1.Zone(at1, "landing", 4f, ""));
				only = g.Objects.IndexOf(k1.Note("Note_Paper", at1 + new Vector2(3f, 0f), "Only note", "The only one."));
				g.Save(IslandSpawner.PathFor(one));
			}
			yield return new WaitForSeconds(2.1f); // (IslandCache looks at a file again at most every 2 s)
			IslandWorldState.Remove(two);
			IslandWorldState.Remove(one);
			IslandWorldState.Entry e = IslandWorldState.Add(two, new Vector3(92000f, 0f, 90000f), null, false);
			IslandWorldState.Entry e1 = IslandWorldState.Add(one, new Vector3(93000f, 0f, 90000f), null, false);
			e.Rule = "cistale"; e1.Rule = "cistale1";
			string pi = StoryBook.PageIsland(e), pi1 = StoryBook.PageIsland(e1);
			Action<string, int> read = (island, n) => StoryBook.AddPage("note:" + island + ":" + n.ToString(CultureInfo.InvariantCulture), "ci", "ci", "ci");
			Action clear = () =>
			{
				foreach (StoryBook.Page p in StoryBook.Pages.Where(p => p.Key.StartsWith("note:" + pi + ":", StringComparison.OrdinalIgnoreCase) || p.Key.StartsWith("note:" + pi1 + ":", StringComparison.OrdinalIgnoreCase)).ToList())
					StoryBook.Pages.Remove(p);
			};
			Func<string, string, bool> done = (id, d) => StoryChain.IsDone(new IntroRule { Id = id, What = "island", WhatArg = id == "cistale" ? two : one, When = "start", StoryPlace = "first", StoryDone = d });
			try
			{
				clear();
				Check(ref ok, first >= 0 && second >= 0 && zone >= 0 && only >= 0 && IslandCache.NotesOf(two).Count == 2 && IslandCache.NotesOf(one).Count == 1,
					"set up: notes #" + first + ", #" + second + " (zone #" + zone + ") on '" + two + "', #" + only + " on '" + one + "'");

				// a) a stale number (the zone's - as The Last Post's 2495 is coral now): another note read doesn't finish it,
				// the island's quest does
				string stale = "note:" + zone.ToString(CultureInfo.InvariantCulture);
				Check(ref ok, !done("cistale", stale), "stale number, nothing read, quest not done: not done");
				read(pi, second);
				Check(ref ok, !done("cistale", stale), "stale number, another note read on a two-note island: not done (can't say it was the right one)");
				e.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 1 };
				Check(ref ok, done("cistale", stale), "stale number, the island's quest done: done (the story goes on - QSL1)");
				e.State.Remove(QuestTracker.StepKey);
				clear();

				// b) a one-note island with a stale number: its note read finishes it
				string stale1 = "note:" + oneZone.ToString(CultureInfo.InvariantCulture);
				Check(ref ok, !done("cistale1", stale1), "one-note island, stale number, nothing read: not done");
				read(pi1, only);
				Check(ref ok, done("cistale1", stale1), "one-note island, stale number, its only note read: done");
				clear();

				// c) a note by its title
				Check(ref ok, !done("cistale", "note:Letter addressed to your raft"), "by title, nothing read: not done");
				read(pi, second);
				Check(ref ok, !done("cistale", "note:Letter addressed to your raft"), "by title, the other note read: not done");
				read(pi, first);
				Check(ref ok, done("cistale", "note:letter addressed to your raft"), "by title (any case), that note read: done");
				clear();

				// d) a real number, as before
				string real = "note:" + first.ToString(CultureInfo.InvariantCulture);
				read(pi, second);
				e.State[QuestTracker.StepKey] = new ObjectState { Active = true, Yield = 1 };
				Check(ref ok, !done("cistale", real), "a real number: the other note read and the quest done don't finish it");
				e.State.Remove(QuestTracker.StepKey);
				read(pi, first);
				Check(ref ok, done("cistale", real), "a real number: that note read finishes it");
				clear();

				// e) the plan check: a title is fine, a wrong title and a stale number are problems
				var plan = new WorldPlan { Name = "CI stale note", Random = false };
				plan.Rules.Add(new IntroRule { Id = "cistale", What = "island", WhatArg = two, When = "start", Where = "receiver", Distance = 300, StoryPlace = "first", StoryDone = "note:Letter addressed to your raft" });
				List<PlanChecker.Finding> fs = PlanChecker.Check(plan, false, false);
				Check(ref ok, !fs.Any(x => x.Level == PlanChecker.Level.Problem && x.Text.Contains("note")), "plan check: a note by its title is no problem (" + string.Join(" | ", fs.Where(x => x.Text.Contains("note")).Select(x => x.Text).ToArray()) + ")");
				plan.Rules[0].StoryDone = "note:No such letter";
				fs = PlanChecker.Check(plan, false, false);
				Check(ref ok, fs.Any(x => x.Level == PlanChecker.Level.Problem && x.Text.Contains("No such letter")), "plan check: a title no note has is a problem");
				plan.Rules[0].StoryDone = stale;
				fs = PlanChecker.Check(plan, false, false);
				Check(ref ok, fs.Any(x => x.Level == PlanChecker.Level.Problem && x.Text.Contains("note #" + zone)), "plan check: a stale number is still a problem");
				// (a rule beside Raft's story: its done-when marks its notebook chapter - Effigy Shoals' note:3 went unseen)
				plan.Rules[0].StoryPlace = "beside";
				fs = PlanChecker.Check(plan, false, false);
				Check(ref ok, fs.Any(x => x.Level == PlanChecker.Level.Problem && x.Text.Contains("note #" + zone)), "plan check: a beside rule's stale note number is a problem too");
				plan.Rules[0].StoryDone = "note:Letter addressed to your raft";
				fs = PlanChecker.Check(plan, false, false);
				Check(ref ok, !fs.Any(x => x.Level == PlanChecker.Level.Problem && x.Text.Contains("note")), "plan check: a beside rule's note by its title is no problem");
			}
			finally
			{
				clear();
				IslandWorldState.Islands.Remove(e);
				IslandWorldState.Islands.Remove(e1);
				foreach (string n in new[] { two, one }) { try { File.Delete(IslandSpawner.PathFor(n)); } catch (Exception) { } }
			}
			if (ok) Log("PASS: story note stale"); else Fail("story note stale");
		}
	}
}
