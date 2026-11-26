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

		[ConsoleCommand(name: "CIStoryTakeOutGuard", docs: "Dev, world (host): T8 guard - a story item a quest still needs (a note's take check, a survivor cage's key on an island here; a has check on an island the plan still brings) can't be taken out of the journal as its Raft item: the Take out button is greyed out and says where, the host refuses a player's take-out (outno with where); the crew takes out what it holds beyond the needs; once the cage is opened and the note read, it is free again (TEST_CATALOGUE QSL6)")]
		public static void StoryTakeOutGuardCommand() { StartTest(StoryTakeOutGuardRoutine()); }

		static IEnumerator StoryTakeOutGuardRoutine()
		{
			for (float t = 0f; t < 60f && !(LoadSceneManager.IsGameSceneLoaded && WorldDirector.WorldHandled); t += Time.unscaledDeltaTime) yield return null;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const string id = "ci-qsl6-plank", raft = "Plank";
			var def = new StoryItemDef { Id = id, Name = "Old plank", RaftItem = raft };
			var asker = new Network_UserId(4242UL);
			var sent = new List<IslandNetMessage>();
			Func<int> count = () => StoryBook.Count(id);
			Action ask = () => { IslandNetwork.Loopback = sent.Add; try { StoryBook.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Story, Name = "out", Data = Uri.EscapeDataString(id) }, 4242UL, asker); } finally { IslandNetwork.Loopback = null; } };
			string far = "ciqsl6-far", farPath = IslandSpawner.PathFor(far), later = "ciqsl6-later", laterPath = IslandSpawner.PathFor(later);
			IslandWorldState.Entry fe = null;
			var rule = new IntroRule { Id = "ciqsl6", What = "island", WhatArg = later, When = "signal", WhenRef = "ciqsl6-never" };
			StoryBook.Take(id, 99);
			try
			{
				// An island here: a note whose read takes one, a survivor cage locked with one
				IslandFile ff = SampleIsland();
				ff.Name = far;
				int note = ff.Objects.Count;
				ff.Objects.Add(new IslandObject { Name = ff.Objects[0].Name, Position = ff.Objects[0].Position, Props = P(BehaviourProps.CheckKey("read"), "take|story:" + id + "|1") });
				int cage = ff.Objects.Count;
				ff.Objects.Add(new IslandObject { Name = Survivors.CageObject, Position = ff.Objects[0].Position, Props = P(Survivors.KeyProp, id) });
				ff.Save(farPath);
				IslandCache.ForgetFile(farPath);
				GiveDef(def, 2);
				Check(ref ok, StoryNeeds.KeptFor(id) == null, "no island needs it yet: free (" + StoryNeeds.KeptFor(id) + ")");
				Vector3 at = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				at.y = 0f;
				fe = IslandWorldState.Add(far, at, null, false);
				if (fe == null) { Fail("the far island was not added"); yield break; }
				string where;
				int need = StoryNeeds.Needed(id, out where);
				Check(ref ok, need == 2 && StoryNeeds.KeptFor(id) != null, "the note and the cage need both the crew holds (needed " + need + " on '" + where + "', held " + count() + ")");

				// The journal: Take out greyed out, saying where; a click and TakeOut do nothing
				int planks = player.Inventory.GetItemCount(raft);
				JournalWindow.Open();
				yield return null; yield return null;
				Check(ref ok, JournalWindow.ShowItemOf(id), "the journal shows the item");
				string label = JournalWindow.TakeOutLabel;
				Check(ref ok, label != null && label.StartsWith("Needed on") && !JournalWindow.TakeOutEnabled, "Take out is greyed out and says where (" + label + ", enabled " + JournalWindow.TakeOutEnabled + ")");
				JournalWindow.ClickTakeOut();
				Check(ref ok, !StoryBook.TakeOut(id), "TakeOut refuses it");
				yield return null; yield return null;
				Check(ref ok, count() == 2 && player.Inventory.GetItemCount(raft) == planks, "kept: 2 in the journal, no plank (" + count() + ", " + planks + " -> " + player.Inventory.GetItemCount(raft) + ")");

				// A player's take-out: the host says no, and why
				ask();
				IslandNetMessage no = sent.LastOrDefault(m => m.Name == "outno" || m.Name == "outok");
				Check(ref ok, count() == 2 && no != null && no.Name == "outno" && Uri.UnescapeDataString((no.Data ?? "").Split('|').Last()).Length > 0, "the host refuses a player's take-out, saying where (" + count() + ", " + (no != null ? no.Name + " " + no.Data : "none") + ")");

				// One more than the needs: that one can be taken out, then none again
				GiveDef(def, 1);
				JournalWindow.ShowItemOf(id);
				Check(ref ok, JournalWindow.TakeOutEnabled && JournalWindow.TakeOutLabel.StartsWith("Take out"), "with one beyond the needs, Take out works (" + JournalWindow.TakeOutLabel + ")");
				sent.Clear();
				ask();
				Check(ref ok, count() == 2 && sent.Any(m => m.Name == "outok"), "a player takes the spare one out (" + count() + ")");
				Check(ref ok, StoryNeeds.KeptFor(id) != null, "then the rest is kept again");

				// The cage opened: one is free; the note read too: all are
				fe.State[Survivors.KeyBase + (cage & 0xFFFF)] = new ObjectState { Active = false, Yield = -1 };
				Check(ref ok, StoryNeeds.Needed(id, out where) == 1 && StoryNeeds.KeptFor(id) == null, "the cage opened: one needed, one free (" + StoryNeeds.Needed(id, out where) + ")");
				fe.State[Behaviours.DoneBase + note] = new ObjectState { Active = false, Yield = -1 };
				Check(ref ok, StoryNeeds.Needed(id, out where) == 0, "the note read too: none needed (" + StoryNeeds.Needed(id, out where) + ")");
				JournalWindow.ShowItemOf(id);
				Check(ref ok, JournalWindow.TakeOutEnabled, "Take out works again");
				JournalWindow.ClickTakeOut();
				yield return null; yield return null;
				Check(ref ok, count() == 1 && player.Inventory.GetItemCount(raft) == planks + 1, "taken out: one left, a plank (" + count() + ", " + player.Inventory.GetItemCount(raft) + ")");

				// An island the plan still brings (not here yet): its has check holds
				IslandFile lf = SampleIsland();
				lf.Name = later;
				if (lf.Props == null) lf.Props = new Dictionary<string, string>();
				lf.Props[BehaviourProps.CheckKey("arrive")] = "has|story:" + id + "|1";
				lf.Save(laterPath);
				IslandCache.ForgetFile(laterPath);
				WorldDirector.Plan.Rules.Add(rule);
				Check(ref ok, StoryNeeds.Needed(id, out where) == 1 && StoryNeeds.KeptFor(id) != null && where == later, "an island the plan still brings needs the last one (" + where + ")");
				WorldDirector.Plan.Rules.Remove(rule);
				Check(ref ok, StoryNeeds.KeptFor(id) == null, "without that rule it is free");
			}
			finally
			{
				IslandNetwork.Loopback = null;
				WorldDirector.Plan.Rules.Remove(rule);
				JournalWindow.Close();
				if (fe != null) IslandWorldState.RemoveIds(new List<int> { fe.Id }, false);
				foreach (string path in new[] { farPath, laterPath }) { try { File.Delete(path); IslandCache.ForgetFile(path); } catch { } }
				StoryBook.Take(id, 99);
			}
			if (ok) Log("PASS: story take out guard"); else Fail("story take out guard");
		}

		[ConsoleCommand(name: "CIRefusedFreesFar", docs: "Dev, world (host): M3/M4 - a player's zone walk-in, chest opening and story-item pickup refused because their island isn't loaded on the host: the zone, chest and pickup the player marked used (or claimed) are ready again, read from the island's file (before: they stayed used with nothing done - the door or key the story needed never came) (TEST_CATALOGUE QSL7)")]
		public static void RefusedFreesFarCommand() { StartTest(RefusedFreesFarRoutine()); }

		static IEnumerator RefusedFreesFarRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			var player = new Network_UserId(4343UL);
			var sent = new List<IslandNetMessage>();
			string far = "ciqsl7-far", farPath = IslandSpawner.PathFor(far);
			IslandFile ff = SampleIsland();
			ff.Name = far;
			Vector3 p0 = ff.Objects.Count > 0 ? ff.Objects[0].Position : Vector3.zero;
			Func<string, Dictionary<string, string>, int> add = (name, props) => { ff.Objects.Add(new IslandObject { Name = name, Position = p0, Props = props, Scale = Vector3.one }); return ff.Objects.Count - 1; };
			int zi = add(ContentCatalog.TriggerZone, P(ObjectProps.ZoneId, "qsl7zone", ObjectProps.ZoneRadius, "2"));
			int ci = add("Loot_Chest", P(ObjectProps.LootItems, "Plank*1", ObjectProps.NoteTitle, "QSL chest"));
			int pi = add("QuestItemPickup_QSL", P(BehaviourProps.Name, "qsl7key"));
			ff.Save(farPath);
			IslandCache.ForgetFile(farPath);
			// (the keys a player's machine marks: the zone's by its place among the zones, the chest's among the loot containers)
			int zoneKey = TriggerZone.KeyBase + ff.Objects.Take(zi).Count(o => o.Name == ContentCatalog.TriggerZone);
			int chestKey = ContentState.LootKeyBase + StableIds.LayoutOf(ff, false).Loot.IndexOf(ci);
			int pickupKey = QuestItemPickups.KeyBase + pi;
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
					foreach (int k in new[] { zoneKey, chestKey, pickupKey }) fe.State[k] = new ObjectState { Active = false, Yield = 0, Day = 1 };
					int freed = Behaviours.FreedOnRefusal;
					Behaviours.OnEventMessage(fe.Id, zi, "enter", false, player, true);
					Check(ref ok, !ContentState.IsUsed(fe, zoneKey), "M3: the zone the player walked into is ready again (key " + zoneKey.ToString("X") + ")");
					Behaviours.OnEventMessage(fe.Id, ci, "open", false, player, true);
					Check(ref ok, !ContentState.IsUsed(fe, chestKey), "M3: the chest the player opened is full again (key " + chestKey.ToString("X") + ")");
					Behaviours.OnEventMessage(fe.Id, pi, "use", false, player, true);
					Check(ref ok, !ContentState.IsUsed(fe, pickupKey), "M4: the story item's pickup claim is free again (key " + pickupKey.ToString("X") + ")");
					Check(ref ok, Behaviours.FreedOnRefusal == freed + 3, "three freed (" + (Behaviours.FreedOnRefusal - freed) + ")");
					// (an object that is none of them: nothing freed)
					int other = 0, n = Behaviours.FreedOnRefusal;
					Behaviours.OnEventMessage(fe.Id, other, "enter", false, player, true);
					Check(ref ok, Behaviours.FreedOnRefusal == n, "an object that is no zone frees nothing");
				}
			}
			finally
			{
				IslandNetwork.Loopback = null;
				if (fe != null) IslandWorldState.RemoveIds(new List<int> { fe.Id }, false);
				try { File.Delete(farPath); IslandCache.ForgetFile(farPath); } catch { }
			}
			if (ok) Log("PASS: refused frees far"); else Fail("refused frees far");
		}

		[ConsoleCommand(name: "CIQueuedSurvivesSave", docs: "Dev, world (host): M5 - players' events on an island not loaded on the host are kept in its saved state: a save and quit (its state written and read back, this session's part forgotten) keeps them, and they are done when it loads; the same event from the same player once; at most MaxQueued per island, the rest left out with a warning (before: 64 in all, 10 minutes, this session only) (TEST_CATALOGUE QSL8)")]
		public static void QueuedSurvivesSaveCommand() { StartTest(QueuedSurvivesSaveRoutine()); }

		static IEnumerator QueuedSurvivesSaveRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
			var p1 = new Network_UserId(4301UL);
			var p2 = new Network_UserId(4302UL);
			string far = "ciqsl8-far", farPath = IslandSpawner.PathFor(far);
			IslandFile ff = SampleIsland();
			ff.Name = far;
			ff.Save(farPath);
			IslandCache.ForgetFile(farPath);
			IslandWorldState.Entry fe = null;
			GameObject fake = null;
			try
			{
				Vector3 at = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				at.y = 0f;
				fe = IslandWorldState.Add(far, at, null, false);
				Check(ref ok, fe != null && fe.Root == null, "a far island, not loaded here");
				if (fe != null)
				{
					IslandWorldState.Entry isl = fe;
					Func<List<KeyValuePair<int, ObjectState>>> queued = () => isl.State.Where(k => k.Key >= Behaviours.QueuedBase && k.Key < Behaviours.QueuedBase + 0x100000).OrderBy(k => k.Key).ToList();
					Behaviours.OnEventMessage(fe.Id, 9001, "use", false, p1, false);
					Behaviours.OnEventMessage(fe.Id, 9001, "use", false, p1, false);
					Behaviours.OnEventMessage(fe.Id, 9001, "use", false, p2, false);
					Behaviours.OnEventMessage(fe.Id, 9002, Islanders.TalkEvent, false, p1, false);
					Check(ref ok, queued().Count == 3 && Behaviours.NotLoadedCount >= 3, "kept in the island's state: 3 (the same player's same event once, another player's kept) - " + queued().Count);

					// (a save and quit: the state written and read back; what only this session knew is forgotten)
					string saved = IslandObjectState.Encode(fe.State);
					Dictionary<int, ObjectState> back = IslandObjectState.Decode(saved);
					var backQueued = back.Where(k => k.Key >= Behaviours.QueuedBase && k.Key < Behaviours.QueuedBase + 0x100000).OrderBy(k => k.Key).ToList();
					Check(ref ok, backQueued.Count == 3 && backQueued.Select(k => k.Value.Yield).SequenceEqual(new[] { 9001, 9001, 9002 }), "a save keeps them: " + string.Join(", ", backQueued.Select(k => k.Key.ToString("X") + "=" + k.Value.Yield).ToArray()));
					foreach (var kv in queued()) fe.State.Remove(kv.Key);
					foreach (var kv in backQueued) fe.State[kv.Key] = kv.Value;
					var qf = typeof(Behaviours).GetField("queuedFrom", priv) != null ? typeof(Behaviours).GetField("queuedFrom", priv).GetValue(null) as IDictionary : null;
					if (qf != null) foreach (object k in qf.Keys.Cast<object>().ToList()) if ((int)((long)k >> 32) == fe.Id) qf.Remove(k);
					var watch = typeof(Behaviours).GetMethod("WatchQueued", priv);
					if (watch != null) watch.Invoke(null, new object[] { fe });

					// (the island loads: done, as the host's player's - the save doesn't know who sent them)
					int done = Behaviours.NotLoadedDone;
					fake = new GameObject("ciqsl8-fake-root");
					fake.transform.position = new Vector3(0f, -2000f, 0f);
					fe.Root = fake;
					var tick = typeof(Behaviours).GetMethod("DoNotLoadedYet", priv);
					try { if (tick != null) tick.Invoke(null, null); } finally { fe.Root = null; }
					Check(ref ok, Behaviours.NotLoadedDone == done + 3 && queued().Count == 0, "after the save they are done when the island loads (" + (Behaviours.NotLoadedDone - done) + " done, " + queued().Count + " left)");

					// (the cap)
					int full = Behaviours.QueueFull;
					for (int i = 0; i < Behaviours.MaxQueued + 10; i++) Behaviours.OnEventMessage(fe.Id, 10000 + i, "use", false, p1, false);
					Check(ref ok, queued().Count == Behaviours.MaxQueued && Behaviours.QueueFull == full + 10, "at most " + Behaviours.MaxQueued + " per island (" + queued().Count + "), 10 left out (" + (Behaviours.QueueFull - full) + ")");
				}
			}
			finally
			{
				if (fe != null) { fe.Root = null; IslandWorldState.RemoveIds(new List<int> { fe.Id }, false); }
				if (fake != null) UnityEngine.Object.Destroy(fake);
				try { File.Delete(farPath); IslandCache.ForgetFile(farPath); } catch { }
			}
			if (ok) Log("PASS: queued survives save"); else Fail("queued survives save");
		}

		[ConsoleCommand(name: "CIStoryTreasureRegrows", docs: "Dev, world (host): M6 - a buried treasure that held a story item, dug up for good: while a check still needs the item and the crew holds none, it can be dug again when its island loads (keys freed); while the crew holds one, or once no check needs it, it stays dug (TEST_CATALOGUE QSL9)")]
		public static void StoryTreasureRegrowsCommand() { StartTest(StoryTreasureRegrowsRoutine()); }

		static IEnumerator StoryTreasureRegrowsRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const string coin = "qsl9coin";
			Action clear = () => { int c = StoryBook.Count(coin); if (c > 0) StoryBook.Take(coin, c); };
			clear();
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				add(ContentCatalog.TriggerZone, -8f, 8f, P(ObjectProps.ZoneId, "qsl9zone", ObjectProps.ZoneRadius, "2", BehaviourProps.CheckKey("enter"), "take|story:" + coin + "|1"));
				int ti = add(ContentCatalog.BuriedTreasure, 6f, -6f, P(ObjectProps.TreasureLoot, "Plank*2;" + StoryItems.Ref(coin) + "*1"));
				file.Objects[ti].Scale = Vector3.one;
				file.Props[StoryItems.Key] = coin + "|Old coin||A coin for a test.";
			});
			IslandWorldState.Entry isl = null;
			yield return SpawnPlayerIsland(f, x => isl = x);
			if (isl == null || isl.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(isl); yield break; }
			try
			{
				yield return new WaitForSeconds(1f);
				BuriedTreasure b = isl.Root.GetComponentInChildren<BuriedTreasure>(true);
				TriggerZone z = isl.Root.GetComponentInChildren<TriggerZone>(true);
				Check(ref ok, b != null && b.HoldsStory && z != null, "a story treasure and the zone that takes its coin");
				if (b != null && z != null)
				{
					int ord = BuriedTreasure.Ordinal(isl.Id, b.Number), keep = BuriedTreasure.KeepBase + b.Number;
					Action dug = () => { isl.State[ord] = new ObjectState { Active = false, Yield = 0, Day = 1 }; isl.State[keep] = new ObjectState { Active = false, Yield = 0, Day = 1 }; b.Made = false; };
					string where;
					Check(ref ok, StoryNeeds.Needed(coin, out where) == 1, "the zone's take check needs the coin (" + where + ")");
					int regrown = BuriedTreasure.RegrownForStory;

					// (a) the crew holds one: stays dug
					StoryBook.Give(coin, 1);
					dug();
					BuriedTreasure.OnIslandReady(isl);
					Check(ref ok, BuriedTreasure.RegrownForStory == regrown && ContentState.IsUsed(isl, keep) && !b.Made, "the crew holds the coin: the treasure stays dug for good");

					// (b) used up or lost: it can be dug again
					clear();
					BuriedTreasure.OnIslandReady(isl);
					Check(ref ok, BuriedTreasure.RegrownForStory == regrown + 1 && !ContentState.IsUsed(isl, keep) && !ContentState.IsUsed(isl, ord) && b.Made, "the crew has none and the zone still needs it: it can be dug again (regrown " + (BuriedTreasure.RegrownForStory - regrown) + ", made " + b.Made + ")");

					// (c) the zone fired: nothing needs the coin - stays dug
					isl.State[z.StateKey] = new ObjectState { Active = false, Yield = 0, Day = 1 };
					dug();
					BuriedTreasure.OnIslandReady(isl);
					Check(ref ok, BuriedTreasure.RegrownForStory == regrown + 1 && ContentState.IsUsed(isl, keep) && !b.Made, "once nothing needs the coin, it stays dug");
				}
			}
			finally { clear(); RemovePlayerIsland(isl); }
			if (ok) Log("PASS: story treasure regrows"); else Fail("story treasure regrows");
		}

		[ConsoleCommand(name: "CISkipReplays", docs: "Dev, world (host): M8 - skipping a read, reach or talk step sets off its note, zone or islander as the host's player would: their events give what they give, the note is marked read, the zone used, the islander talked to; read or walked into later for real, nothing is given twice; on an island not loaded here the skip is kept in its saved state and done when it loads (TEST_CATALOGUE QSL10)")]
		public static void SkipReplaysCommand() { StartTest(SkipReplaysRoutine()); }

		static IEnumerator SkipReplaysRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
			string[] items = { "qsl10read", "qsl10zone", "qsl10talk" };
			Action clear = () => { foreach (string it in items) { int c = StoryBook.Count(it); if (c > 0) StoryBook.Take(it, c); } };
			Func<string> counts = () => string.Join("/", items.Select(it => StoryBook.Count(it).ToString()).ToArray());
			clear();
			int ni = -1, zi = -1, ii = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				file.Props[IslandQuest.KeyTitle] = "QSL skip";
				file.Props[IslandQuest.KeySteps] = "read|QSL letter|1|Read the letter\nreach|qsl10gate|1|Walk through the gate\n" + Islanders.QuestType + "|QSL Pat|1|Talk to Pat\nsignal|qsl10-end|1|Ring the bell";
				ni = add("Note_Paper", 3f, 0f, P(ObjectProps.NoteTitle, "QSL letter", ObjectProps.NoteText, "The key is under the stone.", BehaviourProps.EventKey("read"), "give||" + StoryItems.Ref(items[0]) + "*1"));
				zi = add(ContentCatalog.TriggerZone, -8f, 8f, P(ObjectProps.ZoneId, "qsl10gate", ObjectProps.ZoneRadius, "2", BehaviourProps.EventKey("enter"), "give||" + StoryItems.Ref(items[1]) + "*1"));
				ii = add(Islanders.ObjectName, 0f, 6f, P(BehaviourProps.Name, "QSL Pat", BehaviourProps.EventKey(Islanders.TalkEvent), "give||" + StoryItems.Ref(items[2]) + "*1"));
			});
			IslandWorldState.Entry isl = null;
			yield return SpawnPlayerIsland(f, x => isl = x);
			if (isl == null || isl.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(isl); yield break; }
			string far = "ciqsl10-far", farPath = IslandSpawner.PathFor(far);
			IslandWorldState.Entry fe = null;
			GameObject fake = null;
			try
			{
				yield return new WaitForSeconds(1f);
				int replayed = QuestSkip.Replayed;
				TriggerZone z = Behaviours.RefsOf(isl).Where(r => r.Index == zi).Select(r => r.GetComponent<TriggerZone>()).FirstOrDefault();
				Check(ref ok, z != null && !z.HasFired && QuestTracker.StepOf(isl) == 0, "the quest is at its read step, the zone not set off");

				Check(ref ok, QuestSkip.Skip(isl, 0), "the read step skipped");
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, StoryBook.Count(items[0]) == 1 && isl.State.ContainsKey(Behaviours.DoneBase + ni), "its note's read event gave its item, the note is read (" + counts() + ")");
				Check(ref ok, QuestSkip.Skip(isl, 0), "the reach step skipped");
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, StoryBook.Count(items[1]) == 1 && z != null && z.HasFired, "its zone's enter event gave its item, the zone is used (" + counts() + ")");
				Check(ref ok, QuestSkip.Skip(isl, 0), "the talk step skipped");
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, StoryBook.Count(items[2]) == 1 && ContentState.IsUsed(isl, Islanders.TalkedBase + ii), "its islander's talk event gave its item, talked to (" + counts() + ")");
				Check(ref ok, QuestSkip.Replayed == replayed + 3 && QuestTracker.StepOf(isl) == 3, "three set off, the quest at its last step (" + (QuestSkip.Replayed - replayed) + ", step " + (QuestTracker.StepOf(isl) + 1) + ")");

				// (later done for real: nothing twice)
				Behaviours.Fire(isl, ni, "read", true);
				if (z != null) z.Enter();
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, StoryBook.Count(items[0]) == 1 && StoryBook.Count(items[1]) == 1, "read and walked into for real later: nothing given twice (" + counts() + ")");

				// (an island not loaded here: kept in its state, done when it loads)
				IslandFile ff = SampleIsland();
				ff.Name = far;
				ff.Props[IslandQuest.KeyTitle] = "QSL far";
				ff.Props[IslandQuest.KeySteps] = "read|QSL far letter|1|Read the letter";
				int fi = ff.Objects.Count;
				ff.Objects.Add(new IslandObject { Name = "Note_Paper", Position = ff.Objects.Count > 0 ? ff.Objects[0].Position : Vector3.zero, Scale = Vector3.one, Props = P(ObjectProps.NoteTitle, "QSL far letter", ObjectProps.NoteText, "Far away.") });
				ff.Save(farPath);
				IslandCache.ForgetFile(farPath);
				Vector3 at = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				at.y = 0f;
				fe = IslandWorldState.Add(far, at, null, false);
				Check(ref ok, fe != null && fe.Root == null, "a far island, not loaded here");
				if (fe != null)
				{
					IslandWorldState.Entry fisl = fe;
					Func<List<KeyValuePair<int, ObjectState>>> queued = () => fisl.State.Where(k => k.Key >= Behaviours.QueuedBase && k.Key < Behaviours.QueuedBase + 0x100000).ToList();
					int kept = QuestSkip.ReplayKept;
					string said = QuestSkip.Replay(fe, IslandQuest.From(ff.Props, 0).Steps[0]);
					Check(ref ok, QuestSkip.ReplayKept == kept + 1 && queued().Count == 1 && queued()[0].Value.Yield == fi, "far: the note's read is kept in the island's state (" + said + ")");
					int done = Behaviours.NotLoadedDone;
					fake = new GameObject("ciqsl10-fake-root");
					fake.transform.position = new Vector3(0f, -2000f, 0f);
					fe.Root = fake;
					var tick = typeof(Behaviours).GetMethod("DoNotLoadedYet", priv);
					try { if (tick != null) tick.Invoke(null, null); } finally { fe.Root = null; }
					Check(ref ok, Behaviours.NotLoadedDone == done + 1 && queued().Count == 0, "far: done when the island loads (" + (Behaviours.NotLoadedDone - done) + ")");
				}
			}
			finally
			{
				clear();
				RemovePlayerIsland(isl);
				if (fe != null) { fe.Root = null; IslandWorldState.RemoveIds(new List<int> { fe.Id }, false); }
				if (fake != null) UnityEngine.Object.Destroy(fake);
				try { File.Delete(farPath); IslandCache.ForgetFile(farPath); } catch { }
			}
			if (ok) Log("PASS: skip replays"); else Fail("skip replays");
		}

		[ConsoleCommand(name: "CISkipReplaysObjects", docs: "Dev, world (host): M8's limit closed - skipping an open, set or lights step sets off its chest, set or light group as the host's player would: the chest opened (its loot, used mark, \"open\" event), the set's \"set\" event and signal, the group's lights switched on (\"alllit\" and signal) - each once, nothing twice later; a step asking for part of a group sets off nothing; on an island not loaded here the chest's open is kept in its saved state and done when it loads (TEST_CATALOGUE QSL10b)")]
		public static void SkipReplaysObjectsCommand() { StartTest(SkipReplaysObjectsRoutine()); }

		static IEnumerator SkipReplaysObjectsRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const System.Reflection.BindingFlags priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
			string[] items = { "qsl10open", "qsl10set", "qsl10lit" };
			Action clear = () => { foreach (string it in items) { int c = StoryBook.Count(it); if (c > 0) StoryBook.Take(it, c); } };
			Func<string> counts = () => string.Join("/", items.Select(it => StoryBook.Count(it).ToString()).ToArray());
			clear();
			int ci = -1;
			var lamps = new List<int>();
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				file.Props[IslandQuest.KeyTitle] = "QSL skip objects";
				file.Props[IslandQuest.KeySteps] = "open|QSL box|1|Open the box\n" + Collectibles.QuestType + "|qslgems|2|Find the gems\n" + Lights.QuestType + "|qsllamps|1|Light a lamp\n"
					+ Lights.QuestType + "|qsllamps|2|Light the lamps\nsignal|qsl10b-end|1|Ring the bell";
				ci = add("Loot_Chest", 3f, 0f, P(ObjectProps.NoteTitle, "QSL box", ObjectProps.LootItems, "Plank*1", ObjectProps.LootRefill, "0", BehaviourProps.EventKey("open"), "give||" + StoryItems.Ref(items[0]) + "*1"));
				for (int i = 0; i < 2; i++)
				{
					Dictionary<string, string> p = ObjectProps.Defaults(Collectibles.ObjectName);
					p[Collectibles.SetProp] = "qslgems";
					if (i == 0) p[BehaviourProps.EventKey(Collectibles.SetEvent)] = "give||" + StoryItems.Ref(items[1]) + "*1";
					add(Collectibles.ObjectName, -6f + i * 3f, 6f, p);
				}
				for (int i = 0; i < 2; i++)
				{
					Dictionary<string, string> p = ObjectProps.Defaults(Lights.ObjectName);
					p[Lights.GroupProp] = "qsllamps";
					if (i == 0) p[BehaviourProps.EventKey(Lights.AllLitEvent)] = "give||" + StoryItems.Ref(items[2]) + "*1";
					lamps.Add(add(Lights.ObjectName, 3f + i * 3f, 6f, p));
				}
			});
			IslandWorldState.Entry isl = null;
			yield return SpawnPlayerIsland(f, x => isl = x);
			if (isl == null || isl.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(isl); yield break; }
			string far = "ciqsl10b-far", farPath = IslandSpawner.PathFor(far);
			IslandWorldState.Entry fe = null;
			GameObject fake = null;
			try
			{
				yield return new WaitForSeconds(1.5f);
				int replayed = QuestSkip.Replayed;
				LootCrate crate = Behaviours.RefsOf(isl).Where(r => r.Index == ci).Select(r => r.GetComponentInChildren<LootCrate>(true)).FirstOrDefault();
				Func<int> lit = () => Behaviours.RefsOf(isl).Count(r => lamps.Contains(r.Index) && Lights.Lit(isl, r));
				Check(ref ok, crate != null && !crate.Looted && lit() == 0 && QuestTracker.StepOf(isl) == 0, "the quest is at its open step, the box full, no lamp lit");

				Check(ref ok, QuestSkip.Skip(isl, 0), "the open step skipped");
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, StoryBook.Count(items[0]) == 1 && crate != null && crate.Looted, "its chest opened for the host's player: its open event gave its item, the chest used (" + counts() + ")");
				Check(ref ok, QuestSkip.Skip(isl, 0), "the set step skipped");
				yield return new WaitForSeconds(1f);
				Check(ref ok, StoryBook.Count(items[1]) == 1 && Behaviours.HasSignal(isl, Collectibles.SignalOf("qslgems")), "its set's \"set\" event gave its item once, the set's signal sent (" + counts() + ")");
				Check(ref ok, QuestSkip.Skip(isl, 0), "the 'light 1 of 2' step skipped");
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, lit() == 0 && StoryBook.Count(items[2]) == 0, "part of the group asked: nothing set off (" + lit() + " lit)");
				Check(ref ok, QuestSkip.Skip(isl, 0), "the 'light the lamps' step skipped");
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, lit() == 2 && StoryBook.Count(items[2]) == 1 && Behaviours.HasSignal(isl, Lights.SignalOf("qsllamps")), "its lamps switched on: \"alllit\" gave its item once, the group's signal sent (" + lit() + " lit, " + counts() + ")");
				Check(ref ok, QuestSkip.Replayed == replayed + 3 && QuestTracker.StepOf(isl) == 4, "three set off, the quest at its last step (" + (QuestSkip.Replayed - replayed) + ", step " + (QuestTracker.StepOf(isl) + 1) + ")");

				// (later done for real: nothing twice - the chest opened again, the set looked at again, the lamps watched)
				if (crate != null) crate.Open();
				Collectibles.Tick();
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, StoryBook.Count(items[0]) == 1 && StoryBook.Count(items[1]) == 1 && StoryBook.Count(items[2]) == 1, "nothing given twice afterwards (" + counts() + ")");

				// (an island not loaded here: the chest's open kept in its state, done when it loads)
				IslandFile ff = SampleIsland();
				ff.Name = far;
				ff.Props[IslandQuest.KeyTitle] = "QSL far";
				ff.Props[IslandQuest.KeySteps] = "open|QSL far box|1|Open the box";
				int fi = ff.Objects.Count;
				ff.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = ff.Objects.Count > 0 ? ff.Objects[0].Position : Vector3.zero, Scale = Vector3.one, Props = P(ObjectProps.NoteTitle, "QSL far box", ObjectProps.LootItems, "Plank*1") });
				ff.Save(farPath);
				IslandCache.ForgetFile(farPath);
				Vector3 at = CustomIslandSpawner.RaftPosition.Value - CustomIslandSpawner.SailDirection() * (CustomIslandSpawner.UnloadDistance + 3000f);
				at.y = 0f;
				fe = IslandWorldState.Add(far, at, null, false);
				Check(ref ok, fe != null && fe.Root == null, "a far island, not loaded here");
				if (fe != null)
				{
					IslandWorldState.Entry fisl = fe;
					Func<List<KeyValuePair<int, ObjectState>>> queued = () => fisl.State.Where(k => Behaviours.IsQueuedKey(k.Key)).ToList();
					int kept = QuestSkip.ReplayKept;
					string said = QuestSkip.Replay(fe, IslandQuest.From(ff.Props, 0).Steps[0]);
					Check(ref ok, QuestSkip.ReplayKept == kept + 1 && queued().Count == 1 && queued()[0].Value.Yield == fi, "far: the chest's open is kept in the island's state (" + said + ")");
					int done = Behaviours.NotLoadedDone;
					fake = new GameObject("ciqsl10b-fake-root");
					fake.transform.position = new Vector3(0f, -2000f, 0f);
					fe.Root = fake;
					var tick = typeof(Behaviours).GetMethod("DoNotLoadedYet", priv);
					try { if (tick != null) tick.Invoke(null, null); } finally { fe.Root = null; }
					Check(ref ok, Behaviours.NotLoadedDone == done + 1 && queued().Count == 0, "far: done when the island loads (" + (Behaviours.NotLoadedDone - done) + ")");
				}
			}
			finally
			{
				clear();
				RemovePlayerIsland(isl);
				if (fe != null) { fe.Root = null; IslandWorldState.RemoveIds(new List<int> { fe.Id }, false); }
				if (fake != null) UnityEngine.Object.Destroy(fake);
				try { File.Delete(farPath); IslandCache.ForgetFile(farPath); } catch { }
			}
			if (ok) Log("PASS: skip replays objects"); else Fail("skip replays objects");
		}
	}
}
