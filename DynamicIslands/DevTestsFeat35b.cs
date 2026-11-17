using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Release 3.5 island-maker features (feat35b), host only: QE14 random chests, QE8 notes and actions that unlock a
	/// Receiver frequency, QE9 timed quest steps.
	/// </summary>
	public static partial class DevTests
	{
		#region QE14 random chest

		[ConsoleCommand(name: "CIRandomLoot", docs: "Dev, in game (host): QE14 random chests - each-rolls and pick-N rolls, odds, the recipe @ form, the same roll for the same fill (seeded by island, chest and day), a chest without the mode still gives its whole list")]
		public static void RandomLootCommand(string[] args) { StartTest(RandomLootRoutine()); }

		static IEnumerator RandomLootRoutine()
		{
			string where = GwWhere("CIRandomLoot");
			if (where != null) { Fail(where); yield break; }
			yield return EnsureAlive();
			const string island = "ci-qe14-loot";
			Network_Player player = RAPI.GetLocalPlayer();
			var g = new GwIsland();
			bool ok = true;
			try
			{
				var items = new List<KeyValuePair<string, int>> { Kv("Plank", 2), Kv("Rope", 1), Kv("Nail", 3), Kv("Scrap", 1) };
				// (no world needed: the rolls, the odds, the recipe form)
				var all = P(RandomLoot.Mode, "random", RandomLoot.Chance, "Plank=0;Rope=100");
				List<KeyValuePair<string, int>> r0 = RandomLoot.Roll(all, items, 1);
				Check(ref ok, !r0.Any(x => x.Key == "Plank") && r0.Any(x => x.Key == "Rope") && r0.Any(x => x.Key == "Nail"), "each-rolls: 0% never, 100% (and unnamed) always (" + Txt(r0) + ")");
				Check(ref ok, RandomLoot.Roll(new Dictionary<string, string>(), items, 5).Count == 4, "no mode = the whole list");
				var half = P(RandomLoot.Mode, "random", RandomLoot.Chance, "Plank=50;Rope=50;Nail=50;Scrap=50");
				var seen = new HashSet<string>();
				for (int s = 0; s < 40; s++) seen.Add(Txt(RandomLoot.Roll(half, items, RandomLoot.Seed(island, 0, s))));
				Check(ref ok, seen.Count >= 5, "50% each: different days give different fills (" + seen.Count + " kinds in 40 days)");
				Check(ref ok, Txt(RandomLoot.Roll(half, items, RandomLoot.Seed(island, 2, 7))) == Txt(RandomLoot.Roll(half, items, RandomLoot.Seed(island, 2, 7))), "the same island, chest and day roll the same");
				float none;
				List<float> odds = RandomLoot.Odds(half, items, out none);
				Check(ref ok, odds.All(o => Mathf.Abs(o - 50f) < 0.01f) && Mathf.Abs(none - 6.25f) < 0.01f, "each-rolls odds are exact: 50% each, empty 6.25% (" + none + ")");
				var pick = P(RandomLoot.Mode, "random", RandomLoot.Chance, "Plank=10;Rope=30", RandomLoot.Pick, "2");
				bool twoAlways = true;
				for (int s = 0; s < 60; s++) { var got = RandomLoot.Roll(pick, items, s * 31 + 1); if (got.Count != 2 || got.Select(x => x.Key).Distinct().Count() != 2) twoAlways = false; }
				Check(ref ok, twoAlways, "pick 2: always 2 different items");
				odds = RandomLoot.Odds(pick, items, out none);
				Check(ref ok, Mathf.Abs(odds.Sum() - 200f) < 1f && odds[0] < odds[1] && odds[1] < odds[2] && none == 0f, "pick 2 odds add up to 200%, weighted (" + string.Join("/", odds.Select(o => o.ToString("0.#")).ToArray()) + ")");
				Check(ref ok, RandomLoot.Summary(pick, items).StartsWith("Picks 2 of 4") && RandomLoot.Summary(half, items).Contains("empty 6.3%"), "the inspector's summary (" + RandomLoot.Summary(half, items) + ")");
				string ch;
				Check(ref ok, RandomLoot.SplitRecipe("Plank*4@25;Nail*2;Rope@50%", out ch) == "Plank*4;Nail*2;Rope" && ch == "Plank=25;Rope=50", "recipe 'Plank*4@25;Nail*2;Rope@50%' (" + ch + ")");
				Check(ref ok, RandomLoot.SplitRecipe("Plank*4;Nail*2", out ch) == "Plank*4;Nail*2" && ch == null, "an old recipe line stays as it was");
				Check(ref ok, RandomLoot.ChanceOf(RandomLoot.Chances(RandomLoot.WithChance(pick, "Rope", 100)), "Rope") == 100 && !ObjectProps.Get(RandomLoot.WithChance(pick, "Rope", 100), RandomLoot.Chance).Contains("Rope"), "100% drops the item's entry");

				// In the world: a random chest and an old one
				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(island, out at);
				f.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = at(-3f, 0f), Props = P(BehaviourProps.Name, "qe-rand", ObjectProps.LootItems, "Plank*2;Rope*1;Nail*3;Scrap*1", ObjectProps.NoteTitle, "Random chest",
					RandomLoot.Mode, "random", RandomLoot.Chance, "Plank=10;Rope=30", RandomLoot.Pick, "2") });
				f.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = at(3f, 0f), Props = P(BehaviourProps.Name, "qe-old", ObjectProps.LootItems, "Plank*2;Rope*1", ObjectProps.NoteTitle, "Old chest") });
				yield return GwSpawnFile(g, f, island);
				IslandWorldState.Entry e = g.E;
				if (e == null) yield break;
				LootCrate rand = QbCrate(e, "qe-rand"), old = QbCrate(e, "qe-old");
				if (rand == null || old == null) { Fail("the chests have no LootCrate (" + (rand != null) + ", " + (old != null) + ")"); yield break; }
				Check(ref ok, rand.RandomProps != null && old.RandomProps == null, "the random chest keeps its roll settings, the old one none");
				string fill = Txt(rand.ThisFill());
				Check(ref ok, rand.ThisFill().Count == 2 && Txt(rand.ThisFill()) == fill, "the random chest's fill is 2 items, the same each time asked (" + fill + ")");
				Check(ref ok, fill == Txt(RandomLoot.Roll(rand.RandomProps, rand.Items, RandomLoot.Seed(e.HostName, rand.Ordinal, ContentState.Today))), "the fill is the roll of island, chest and day (what every player computes)");
				Check(ref ok, Txt(old.ThisFill()) == "Plank*2;Rope*1", "the old chest gives its whole list (" + Txt(old.ThisFill()) + ")");
				PutPlayerNear(rand.transform);
				yield return new WaitForSeconds(0.3f);
				List<string> given = rand.Open();
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, given.Count == 2, "opening the random chest gave 2 entries (" + string.Join(", ", given.ToArray()) + ")");
				given = old.Open();
				Check(ref ok, given.Count == 2, "opening the old chest gave both entries (" + string.Join(", ", given.ToArray()) + ")");
			}
			finally
			{
				Network_Player me = RAPI.GetLocalPlayer();
				if (me != null && me.Inventory != null) foreach (string it in new[] { "Plank", "Rope", "Nail", "Scrap" }) Gw4Take(me, it);
				GwDrop(g, island);
			}
			if (ok) Log("PASS: QE14 random chest (rolls, odds, recipe @ form, same fill everywhere, old chests unchanged)");
			else Fail("QE14 random chest: see the FAIL lines above");
		}

		static KeyValuePair<string, int> Kv(string k, int v) { return new KeyValuePair<string, int>(k, v); }
		static string Txt(List<KeyValuePair<string, int>> l) { return string.Join(";", l.Select(x => x.Key + "*" + x.Value).ToArray()); }
		static LootCrate QbCrate(IslandWorldState.Entry e, string name) { IslandObjectRef r = ScObjOf(e, name); return r != null ? r.GetComponentInChildren<LootCrate>(true) : null; }

		#endregion

		#region QE8 frequency

		[ConsoleCommand(name: "CIFrequencyUnlock", docs: "Dev, in game (host): QE8 - a note whose reading unlocks a Receiver frequency unlocks Raft's own note for that story island (once), the action text, the note editor's read-event round trip. Unlocks one story frequency in the world it runs in")]
		public static void FrequencyUnlockCommand(string[] args) { StartTest(FrequencyUnlockRoutine()); }

		static IEnumerator FrequencyUnlockRoutine()
		{
			string where = GwWhere("CIFrequencyUnlock");
			if (where != null) { Fail(where); yield break; }
			yield return EnsureAlive();
			const string island = "ci-qe8-freq";
			var g = new GwIsland();
			bool ok = true;
			try
			{
				// The note editor's read event: the frequency set, changed and removed; other actions stay
				var np = P(BehaviourProps.EventKey("read"), "give||Scrap*1");
				var with = ReceiverUnlock.WithNoteFrequency(np, "Vasagatan");
				Check(ref ok, ReceiverUnlock.OfNote(with) == "Vasagatan" && ObjectProps.Get(with, BehaviourProps.EventKey("read")).Contains("give||Scrap*1"), "a note's frequency joins its read event (" + ObjectProps.Get(with, BehaviourProps.EventKey("read")).Replace("\n", " / ") + ")");
				var back = ReceiverUnlock.WithNoteFrequency(with, "");
				Check(ref ok, ReceiverUnlock.OfNote(back) == "" && ObjectProps.Get(back, BehaviourProps.EventKey("read")).Trim() == "give||Scrap*1", "removed, only the frequency goes");
				Check(ref ok, ReceiverUnlock.Options("(none)").Count == StoryOrder.Chain.Length + 1, "the drop list: none + every story island");
				Check(ref ok, ObjAction.ParseLines("frequency|Vasagatan|").Count == 1 && BehaviourProps.Verbs.Contains(ReceiverUnlock.Verb), "'frequency' is an action verb");

				ChunkPointType stop = StoryOrder.Chain.FirstOrDefault(t => !ReceiverUnlock.IsUnlocked(t) && ReceiverUnlock.NoteFor(t) >= 0);
				if (stop == ChunkPointType.None) { Log("  (every story frequency is unlocked in this world: only the no-op checked)"); stop = StoryOrder.Chain.Last(); }
				int note = ReceiverUnlock.NoteFor(stop);
				Check(ref ok, note >= 0, "Raft's notebook has a note for " + StoryOrder.Name(stop) + " (" + note + ")");
				bool was = NoteBook.unlockedNoteBookIndexes != null && NoteBook.unlockedNoteBookIndexes.Contains(note);
				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(island, out at);
				f.Objects.Add(new IslandObject { Name = "Note_Paper", Position = at(0f, 0f), Props = P(ObjectProps.NoteTitle, "Radio log", ObjectProps.NoteText, "The signal came from the north again.",
					BehaviourProps.EventKey("read"), "frequency|" + StoryOrder.Key(stop) + "|") });
				yield return GwSpawnFile(g, f, island);
				IslandWorldState.Entry e = g.E;
				if (e == null) yield break;
				int before = NoteBook.unlockedNoteBookIndexes != null ? NoteBook.unlockedNoteBookIndexes.Count : 0;
				ScReadNote(e, "Radio log");
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, NoteBook.unlockedNoteBookIndexes != null && NoteBook.unlockedNoteBookIndexes.Contains(note), "reading the note unlocked note " + note + " (" + StoryOrder.Name(stop) + ")" + (was ? " - it was already" : ""));
				Check(ref ok, ReceiverUnlock.IsUnlocked(StoryOrder.Active ? stop : stop) || was || StoryChain.Active, "the Receiver has " + StoryOrder.Name(stop) + " now");
				int after = NoteBook.unlockedNoteBookIndexes != null ? NoteBook.unlockedNoteBookIndexes.Count : 0;
				Check(ref ok, after - before == (was ? 0 : 1), "exactly one note unlocked (" + (after - before) + ")");
				// Again (the action run a second time): nothing more
				ReceiverUnlock.HostRun(e, new ObjAction { Verb = ReceiverUnlock.Verb, Target = StoryOrder.Key(stop), Arg = "" });
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, (NoteBook.unlockedNoteBookIndexes != null ? NoteBook.unlockedNoteBookIndexes.Count : 0) == after, "unlocking it again does nothing");
				Check(ref ok, ReceiverUnlock.Describe(new ObjAction { Verb = ReceiverUnlock.Verb, Target = "nowhere" }).Contains("no story island"), "a bad island name is told in the action text");
			}
			finally { GwDrop(g, island); }
			if (ok) Log("PASS: QE8 frequency unlock (note read unlocks Raft's note once, editor round trip)");
			else Fail("QE8 frequency unlock: see the FAIL lines above");
		}

		#endregion

		#region QE9 timed step

		[ConsoleCommand(name: "CIQuestTimer", docs: "Dev, in game (host): QE9 timed quest steps - the setting's round trip, the countdown runs while a player is near and shows, running out sets the step back and starts the clock again, the seconds left survive an island reload, an untimed step clears the clock")]
		public static void QuestTimerCommand(string[] args) { StartTest(QuestTimerRoutine()); }

		static IEnumerator QuestTimerRoutine()
		{
			string where = GwWhere("CIQuestTimer");
			if (where != null) { Fail(where); yield break; }
			yield return EnsureAlive();
			const string island = "ci-qe9-timer";
			var g = new GwIsland();
			bool ok = true;
			try
			{
				// The setting: write and read back
				var q = new IslandQuest { Title = "Beat the clock" };
				q.Steps.Add(new IslandQuest.Step { Type = "reach", Target = "qt-nowhere", Text = "Reach the far rock in time", Seconds = 4 });
				q.Steps.Add(new IslandQuest.Step { Type = "reach", Target = "qt-nowhere2", Text = "Rest" });
				var props = new Dictionary<string, string>();
				q.To(props, 0);
				Check(ref ok, ObjectProps.Get(props, IslandQuest.Key(IslandQuest.KeyTimers, 0)) == "1=4", "quest.timers written as '1=4' (" + ObjectProps.Get(props, IslandQuest.Key(IslandQuest.KeyTimers, 0)) + ")");
				IslandQuest r = IslandQuest.From(props, 0);
				Check(ref ok, r.Steps.Count == 2 && r.Steps[0].Seconds == 4 && r.Steps[1].Seconds == 0, "read back: step 1 4 s, step 2 none");
				var bad = new List<IslandQuest.Step> { new IslandQuest.Step(), new IslandQuest.Step() };
				QuestTimers.Read("x=3;2=abc;9=5;2=99999;", bad);
				Check(ref ok, bad[0].Seconds == 0 && bad[1].Seconds == QuestTimers.MaxSeconds, "bad entries skipped, too long clamped");
				Check(ref ok, QuestTimers.Clock(83) == "1:23" && QuestTimers.Unsafe("kill") && !QuestTimers.Unsafe("reach"), "clock text, unsafe kinds");

				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(island, out at);
				f.Objects.Add(new IslandObject { Name = "Log", Position = at(0f, 0f), Props = P(BehaviourProps.Name, "qt-spot") });
				foreach (var kv in props) f.Props[kv.Key] = kv.Value;
				yield return GwSpawnFile(g, f, island);
				IslandWorldState.Entry e = g.E;
				if (e == null) yield break;
				IslandObjectRef spot = ScObjOf(e, "qt-spot");
				if (spot == null) { Fail("no spot object"); yield break; }
				PutPlayerNear(spot.transform, 2f);
				yield return new WaitForSeconds(1.5f);
				QuestTimers.Shown t = QuestTimers.Of(e.Id, 0);
				Check(ref ok, t != null && t.Mode == QuestTimers.Run && t.Step == 0, "near the island the clock runs (" + (t != null ? t.Mode + ", " + t.LeftNow.ToString("0.0") : "none") + ")");
				Check(ref ok, QuestTimers.HudText.StartsWith("Time left 0:0") && QuestTimers.HudText.Contains("Reach the far rock"), "the countdown shows (" + QuestTimers.HudText + ")");
				Screenshot(new[] { "qe9_countdown" });
				int fails = QuestTimers.Fails;
				// (questfix 11-14: work done early for step 2 - a chest opened meanwhile - must outlive the time-out)
				e.State[QuestTracker.EarlyKeyBase + 1] = new ObjectState { Active = true, Yield = 1, Day = 0 };
				yield return new WaitForSeconds(4.0f);
				t = QuestTimers.Of(e.Id, 0);
				Check(ref ok, QuestTimers.Fails == fails + 1, "the time ran out once (" + (QuestTimers.Fails - fails) + ")");
				Check(ref ok, QuestTracker.StepOf(e, 0) == 0 && t != null && t.LeftNow > 1.5f, "the step started again with a fresh clock (" + (t != null ? t.LeftNow.ToString("0.0") : "none") + " s)");
				Check(ref ok, e.State.ContainsKey(QuestTracker.EarlyKeyBase + 1), "the time-out kept the early credit for step 2 (questfix)");
				Check(ref ok, (IslandInfo.LastMessage ?? "").StartsWith("Time's up"), "a message told the player (" + IslandInfo.LastMessage + ")");
				ObjectState st;
				Check(ref ok, e.State.TryGetValue(QuestTimers.KeyBase, out st) && st.Day == 0 && st.Yield > 0 && st.Yield <= 4, "the seconds left are kept in the island state (" + (e.State.ContainsKey(QuestTimers.KeyBase) ? e.State[QuestTimers.KeyBase].Yield.ToString() : "none") + ")");

				// A reload keeps the seconds left (set to 3 to see it)
				e.State[QuestTimers.KeyBase] = new ObjectState { Active = true, Yield = 3, Day = 0 };
				yield return GwReload(e);
				Check(ref ok, IslandWorldState.Islands.Contains(e) && e.Root != null, "the island is back after the reload");
				Check(ref ok, e.State.ContainsKey(QuestTimers.KeyBase), "unloading the island kept the seconds left");
				PutPlayerNear(ScObjOf(e, "qt-spot").transform, 2f);
				QuestTimers.Reset(); // (as a world loaded: the clock from the state)
				e.State[QuestTimers.KeyBase] = new ObjectState { Active = true, Yield = 3, Day = 0 };
				yield return new WaitForSeconds(0.8f);
				t = QuestTimers.Of(e.Id, 0);
				Check(ref ok, t != null && t.LeftNow <= 3.01f && t.LeftNow > 1.5f, "the clock carries on from the saved 3 s (" + (t != null ? t.LeftNow.ToString("0.0") : "none") + ")");

				// Done in time: the next step has no clock - it stops, the state key goes, the countdown hides
				QuestTracker.JumpTo(e, 0, 1);
				yield return new WaitForSeconds(1.2f);
				Check(ref ok, QuestTimers.Of(e.Id, 0) == null && !e.State.ContainsKey(QuestTimers.KeyBase), "on an untimed step the clock and its key are gone");
				Check(ref ok, QuestTimers.HudText == "", "the countdown hides (" + QuestTimers.HudText + ")");
			}
			finally { GwDrop(g, island); }
			if (ok) Log("PASS: QE9 timed step (setting, countdown, time out sets back, reload keeps the clock, untimed clears)");
			else Fail("QE9 timed step: see the FAIL lines above");
		}

		#endregion
	}
}
