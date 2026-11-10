using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Two-player checks for release 3.4 (11-10): the new wait steps (QW3), random chests, frequency notes and timed
	/// steps (FB4), walking islanders, taming, dialogue flags and escort (FC6) with a second player. Driver:
	/// D:\Temp\scratch_8e762ddb\final34\mp34b.ps1 (host = player 1, client = player 2 already joined).</summary>
	public static partial class DevTests
	{
		// (kept between the parts: what the host set up for the client to check)
		static int m4Note = -1, m4Fails0, m4Refused0;

		[ConsoleCommand(name: "CIMp34b", docs: "Dev, two players (11-10, driver mp34b.ps1): CIMp34b <part>. QW3: wait (host: 'ciplayer' with the wait quest, noon, clear) | waitc (client: step 1 on its panel) | sky (host: night, storm -> step 3) | bells (client: panel at step 3, rings the bell 3x -> step 4, alone in the zone = 1 of 2) | zone (host: 1 of 2 shown, joins -> done) | waitend (client: done). FB4: fb (host: random chest, frequency note, timed step; FILL line) | fbc <fill> (client: same fill, note unlocks, countdown and time-out) | fbcheck (host). FC6: fc (host: walker, Finn, goats, Mia; WALK line) | walk <t> <x> <z> (client) | talk (client: answer sets asked) | talkh (host: the asked answer shows) | tame (client: feeds a goat, food taken once, refusals keep it, it follows) | tameh (host) | escort (client leads Mia) | escorth (host) | done (host: removed, hooks off)")]
		public static void Mp34bCommand(string[] args) { StartTest(Mp34bRoutine(args ?? new string[0])); }

		static IEnumerator M4Wait(Func<bool> cond, float seconds)
		{
			float t0 = Time.realtimeSinceStartup;
			while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return new WaitForSeconds(0.25f);
		}

		/// <summary>Client: the test island (with this object) once it is loaded here.</summary>
		static IEnumerator M4Island(string marker, Action<IslandWorldState.Entry> got)
		{
			IslandWorldState.Entry e = null;
			float t0 = Time.realtimeSinceStartup;
			while (Time.realtimeSinceStartup - t0 < 60f)
			{
				e = IslandWorldState.Islands.FirstOrDefault(x => x.Root != null && (x.HostName ?? "").IndexOf(PlayerIsland, StringComparison.OrdinalIgnoreCase) >= 0 && ScObjOf(x, marker) != null);
				if (e != null) break;
				yield return new WaitForSeconds(0.5f);
			}
			if (e == null) Fail("no loaded '" + PlayerIsland + "' with '" + marker + "' here");
			got(e);
		}

		static IslandWorldState.Entry M4Host() { return IslandWorldState.Islands.FirstOrDefault(x => x.HostName == PlayerIsland && x.Root != null); }

		static void M4Stand(IslandWorldState.Entry e, string at, float dx = 0f)
		{
			IslandObjectRef r = ScObjOf(e, at);
			if (r != null) MovePlayerTo(r.transform.position + new Vector3(dx, 0f, 0f));
		}

		static string M4Panel() { return QuestTracker.PanelCurrent.Replace("\n", " "); }

		static Network_Player M4Other() { Network_Player me = RAPI.GetLocalPlayer(); return Players.All.FirstOrDefault(x => x != null && x != me); }

		static string F(float v) { return v.ToString("F2", CultureInfo.InvariantCulture); }

		static IEnumerator Mp34bRoutine(string[] args)
		{
			string part = args.Length > 0 ? args[0].ToLowerInvariant() : "";
			bool ok = true, host = Raft_Network.IsHost;
			Network_Player me = RAPI.GetLocalPlayer();
			if (me == null) { Fail("mp34b: run in a world"); yield break; }
			bool hostPart = part == "wait" || part == "sky" || part == "zone" || part == "fb" || part == "fbcheck" || part == "fc" || part == "talkh" || part == "tameh" || part == "escorth" || part == "done";
			if (hostPart != host) { Fail("mp34b " + part + ": run as " + (hostPart ? "the host" : "player 2")); yield break; }
			string name = "mp34b " + part;
			IslandWorldState.Entry e = null;
			switch (part)
			{
				// ---------------- QW3: wait steps with two players ----------------
				case "wait":
				{
					int bell = -1;
					GapBehaviours.TestHour = 12f;
					GapBehaviours.TestWeather = "clear";
					yield return GapIsland((file, add) =>
					{
						bell = add("Note_Sign", -4f, 4f, P(BehaviourProps.Name, "Bell", BehaviourProps.Use, "Ring", BehaviourProps.EventPrefix + "use", "count|ci-bells|+1"));
						add(ContentCatalog.TriggerZone, 8f, 0f, P(BehaviourProps.Name, "Plate", ObjectProps.ZoneId, "ciwait", ObjectProps.ZoneRadius, "3"));
						add("Log", -10f, -4f, P(BehaviourProps.Name, "mp-host"));
						add("Log", -4f, -6f, P(BehaviourProps.Name, "mp-client"));
						file.Props[IslandQuest.KeyTitle] = "CI wait two";
						file.Props[IslandQuest.KeySteps] = WaitSteps;
					}, x => e = x);
					if (e == null) yield break;
					M4Stand(e, "mp-host");
					yield return new WaitForSeconds(2f);
					Check(ref ok, QuestTracker.StepOf(e) == 0, "at noon the host has step 1 (night) waiting");
					Check(ref ok, GapWorld.FakeCount == null && GapWorld.FakeOnline < 0, "no faked player counts");
					break;
				}
				case "waitc":
				{
					yield return M4Island("Bell", x => e = x);
					if (e == null) yield break;
					M4Stand(e, "mp-client");
					yield return M4Wait(() => QuestTracker.PanelShown && QuestTracker.PanelIsland == e.Id && M4Panel() != "", 20f);
					IslandQuest q = QuestTracker.QuestOf(e);
					Check(ref ok, QuestTracker.StepOf(e) == 0, "player 2 has step 1 too (" + QuestTracker.StepOf(e) + ")");
					Check(ref ok, q.Steps.Count == 4 && M4Panel().Contains(q.Steps[0].Describe()), "player 2's quest panel shows step 1 (" + M4Panel() + ")");
					break;
				}
				case "sky":
				{
					e = M4Host();
					if (e == null) { Fail(name + ": no ciplayer"); yield break; }
					GapBehaviours.TestHour = 23f;
					yield return WaitStep(e, 1, 8f);
					Check(ref ok, QuestTracker.StepOf(e) == 1, "night (host's hour 23): step 1 done");
					GapBehaviours.TestWeather = "storm";
					yield return WaitStep(e, 2, 8f);
					Check(ref ok, QuestTracker.StepOf(e) == 2, "a storm: step 2 done");
					break;
				}
				case "bells":
				{
					yield return M4Island("Bell", x => e = x);
					if (e == null) yield break;
					IslandQuest q = QuestTracker.QuestOf(e);
					yield return M4Wait(() => QuestTracker.StepOf(e) == 2 && M4Panel().Contains(q.Steps[2].Describe()), 10f);
					Check(ref ok, QuestTracker.StepOf(e) == 2, "player 2 got the host's night and storm: step 3 (" + QuestTracker.StepOf(e) + ")");
					Check(ref ok, M4Panel().Contains(q.Steps[2].Describe()), "player 2's panel shows step 3, the counter (" + M4Panel() + ")");
					IslandObjectRef bell = ScObjOf(e, "Bell");
					if (bell == null) { Fail(name + ": no bell"); yield break; }
					PutPlayerNear(bell.transform, 1.5f);
					yield return new WaitForSeconds(0.5f);
					for (int i = 0; i < 3; i++) { Behaviours.Fire(e, bell.Index, "use", true); yield return new WaitForSeconds(1.5f); }
					yield return M4Wait(() => QuestTracker.StepOf(e) == 3 && M4Panel().Contains(q.Steps[3].Describe()), 10f);
					Check(ref ok, GapBehaviours.Counter(e, "ci-bells") == 3 && QuestTracker.StepOf(e) == 3, "player 2 rang 3 times: the host's counter at 3, step 4 (counter " + GapBehaviours.Counter(e, "ci-bells") + ", step " + QuestTracker.StepOf(e) + ")");
					Check(ref ok, M4Panel().Contains(q.Steps[3].Describe()), "player 2's panel shows step 4 (" + M4Panel() + ")");
					TriggerZone z = IslanderWalks.ZoneOf(e, "ciwait");
					if (z == null) { Fail(name + ": no zone ciwait"); yield break; }
					MovePlayerTo(z.transform.position);
					yield return M4Wait(() => M4Panel().Contains("(1/2)"), 8f);
					Check(ref ok, QuestTracker.StepOf(e) == 3 && M4Panel().Contains("(1/2)"), "player 2 alone in the zone counts as 1 of 2: step 4 waits (" + M4Panel() + ")");
					break;
				}
				case "zone":
				{
					e = M4Host();
					if (e == null) { Fail(name + ": no ciplayer"); yield break; }
					yield return M4Wait(() => QuestTestCommands.Describe(e).Contains("1 of 2 players in 'ciwait'"), 8f);
					string show = QuestTestCommands.Describe(e);
					Check(ref ok, QuestTracker.StepOf(e) == 3 && show.Contains("1 of 2 players in 'ciwait'"), "the host counts player 2 in the zone as 1 of 2 (" + show.Replace("\n", " / ") + ")");
					TriggerZone z = IslanderWalks.ZoneOf(e, "ciwait");
					if (z == null) { Fail(name + ": no zone ciwait"); yield break; }
					MovePlayerTo(z.transform.position + new Vector3(1f, 0f, 0f));
					yield return WaitStep(e, 4, 8f);
					Check(ref ok, QuestTracker.StepOf(e) == 4, "both in the zone: step 4 done, the quest done (" + QuestTracker.StepOf(e) + ")");
					M4Stand(e, "mp-host");
					break;
				}
				case "waitend":
				{
					yield return M4Island("Bell", x => e = x);
					if (e == null) yield break;
					yield return M4Wait(() => QuestTracker.StepOf(e) == 4, 10f);
					Check(ref ok, QuestTracker.StepOf(e) == 4, "player 2 has the quest done too (" + QuestTracker.StepOf(e) + ")");
					M4Stand(e, "mp-client");
					break;
				}

				// ---------------- FB4: random chest, frequency note, timed step ----------------
				case "fb":
				{
					GapBehaviours.TestHour = null;
					GapBehaviours.TestWeather = null;
					ChunkPointType stop = StoryOrder.Chain.FirstOrDefault(t => !ReceiverUnlock.IsUnlocked(t) && ReceiverUnlock.NoteFor(t) >= 0 && (NoteBook.unlockedNoteBookIndexes == null || !NoteBook.unlockedNoteBookIndexes.Contains(ReceiverUnlock.NoteFor(t))));
					if (stop == ChunkPointType.None) { Fail(name + ": every story frequency is unlocked in this world (use a new world)"); yield break; }
					m4Note = ReceiverUnlock.NoteFor(stop);
					var q = new IslandQuest { Title = "Beat the clock" };
					q.Steps.Add(new IslandQuest.Step { Type = "reach", Target = "qt-nowhere", Text = "Reach the far rock in time", Seconds = 10 });
					q.Steps.Add(new IslandQuest.Step { Type = "reach", Target = "qt-nowhere2", Text = "Rest" });
					var props = new Dictionary<string, string>();
					q.To(props, 0);
					yield return GapIsland((file, add) =>
					{
						add("Loot_Chest", -3f, 0f, P(BehaviourProps.Name, "qe-rand", ObjectProps.LootItems, "Plank*2;Rope*1;Nail*3;Scrap*1", ObjectProps.NoteTitle, "Random chest",
							RandomLoot.Mode, "random", RandomLoot.Chance, "Plank=10;Rope=30", RandomLoot.Pick, "2"));
						add("Note_Paper", 3f, 0f, P(BehaviourProps.Name, "radio", ObjectProps.NoteTitle, "Radio log", ObjectProps.NoteText, "The signal came from the north again.",
							BehaviourProps.EventKey("read"), "frequency|" + StoryOrder.Key(stop) + "|"));
						add("Log", -10f, -4f, P(BehaviourProps.Name, "mp-host"));
						add("Log", 0f, -6f, P(BehaviourProps.Name, "mp-client"));
						foreach (var kv in props) file.Props[kv.Key] = kv.Value;
					}, x => e = x);
					if (e == null) yield break;
					M4Stand(e, "mp-host");
					LootCrate rand = QbCrate(e, "qe-rand");
					if (rand == null) { Fail(name + ": the random chest has no LootCrate"); yield break; }
					string fill = Txt(rand.ThisFill()).Replace(";", ",");
					Check(ref ok, rand.ThisFill().Count == 2, "the host's fill: 2 items (" + fill + ")");
					m4Fails0 = QuestTimers.Fails;
					Log("MP34B FILL " + fill);
					Log("MP34B NOTE " + m4Note + " (" + StoryOrder.Name(stop) + ")");
					break;
				}
				case "fbc":
				{
					string want = args.Length > 1 ? args[1] : "";
					yield return M4Island("qe-rand", x => e = x);
					if (e == null) yield break;
					LootCrate rand = QbCrate(e, "qe-rand");
					if (rand == null) { Fail(name + ": no random chest here"); yield break; }
					List<KeyValuePair<string, int>> mine = rand.ThisFill();
					string mineTxt = Txt(mine).Replace(";", ",");
					Check(ref ok, want != "" && mineTxt == want, "player 2 computes the host's fill (" + mineTxt + " vs host " + want + ")");
					var before = mine.ToDictionary(kv => kv.Key, kv => me.Inventory.GetItemCount(kv.Key));
					PutPlayerNear(rand.transform);
					yield return new WaitForSeconds(0.5f);
					rand.LastGiven = new List<string>();
					rand.Open();
					yield return M4Wait(() => rand.LastGiven.Count > 0, 10f);
					string got = string.Join(";", mine.Select(kv => kv.Key + "+" + (me.Inventory.GetItemCount(kv.Key) - before[kv.Key])).ToArray());
					Check(ref ok, mine.All(kv => me.Inventory.GetItemCount(kv.Key) - before[kv.Key] == kv.Value), "opened on player 2 (after the host's yes) it gave that fill (" + got + ", given " + string.Join(", ", rand.LastGiven.ToArray()) + ")");
					foreach (var kv in mine) { int n = me.Inventory.GetItemCount(kv.Key) - before[kv.Key]; if (n > 0) me.Inventory.RemoveItem(kv.Key, n); }
					// the frequency note: its read event names the story island; Raft's note for it unlocks here too
					IslandObjectRef radio = ScObjOf(e, "radio");
					ObjAction fa = radio != null ? ObjAction.ParseLines(ObjectProps.Get(radio.Props, BehaviourProps.EventKey("read"))).FirstOrDefault(a => a.Verb == ReceiverUnlock.Verb) : null;
					int note = fa != null ? ReceiverUnlock.NoteFor(StoryOrder.Parse(fa.Target)) : -1;
					Check(ref ok, note >= 0 && (NoteBook.unlockedNoteBookIndexes == null || !NoteBook.unlockedNoteBookIndexes.Contains(note)), "player 2 has the note's frequency (note " + note + ") still locked");
					if (note >= 0)
					{
						ScReadNote(e, "Radio log");
						yield return M4Wait(() => NoteBook.unlockedNoteBookIndexes != null && NoteBook.unlockedNoteBookIndexes.Contains(note), 10f);
						Check(ref ok, NoteBook.unlockedNoteBookIndexes != null && NoteBook.unlockedNoteBookIndexes.Contains(note), "player 2 read the note: Raft's note " + note + " is unlocked on player 2");
					}
					// the timed step: the host's countdown shows here, and running out sets player 2's step back
					M4Stand(e, "mp-client");
					yield return M4Wait(() => { QuestTimers.Shown t = QuestTimers.Of(e.Id, 0); return t != null && t.Mode == QuestTimers.Run && QuestTimers.HudText != ""; }, 12f);
					Check(ref ok, QuestTimers.HudText.StartsWith("Time left") && QuestTimers.HudText.Contains("Reach the far rock"), "player 2 sees the countdown (" + QuestTimers.HudText + ")");
					int fails = QuestTimers.Fails;
					yield return M4Wait(() => QuestTimers.Fails > fails, 16f);
					yield return new WaitForSeconds(0.5f);
					QuestTimers.Shown tt = QuestTimers.Of(e.Id, 0);
					Check(ref ok, QuestTimers.Fails > fails, "player 2 got the time-out (" + (QuestTimers.Fails - fails) + ")");
					Check(ref ok, (IslandInfo.LastMessage ?? "").StartsWith("Time's up"), "player 2 was told (" + IslandInfo.LastMessage + ")");
					Check(ref ok, QuestTracker.StepOf(e) == 0 && QuestTracker.ProgressOf(e) == 0 && tt != null && tt.LeftNow > 6f, "player 2's step started again with a fresh clock (step " + QuestTracker.StepOf(e) + ", " + (tt != null ? tt.LeftNow.ToString("0.0") : "none") + " s)");
					break;
				}
				case "fbcheck":
				{
					e = M4Host();
					if (e == null) { Fail(name + ": no ciplayer"); yield break; }
					LootCrate rand = QbCrate(e, "qe-rand");
					Check(ref ok, rand != null && rand.Looted, "the host has the random chest emptied by player 2");
					Check(ref ok, m4Note >= 0 && NoteBook.unlockedNoteBookIndexes != null && NoteBook.unlockedNoteBookIndexes.Contains(m4Note), "the frequency note player 2 read is unlocked on the host (note " + m4Note + ")");
					Check(ref ok, QuestTimers.Fails > m4Fails0 && QuestTracker.StepOf(e) == 0, "the host's timer ran out (" + (QuestTimers.Fails - m4Fails0) + "x) and the step is still 1");
					break;
				}

				// ---------------- FC6: walking islander, dialogue flag, taming, escort ----------------
				case "fc":
				{
					GapBehaviours.TestHour = null;
					GapBehaviours.TestWeather = null;
					yield return GapIsland((file, add) =>
					{
						add(Islanders.ObjectName, 0f, 4f, P(BehaviourProps.Name, "Walker", Islanders.CharacterProp, "3", IslanderWalks.PathProp, "wp-a, wp-b", IslanderWalks.PauseProp, "1"));
						add("Log", 8f, 4f, P(BehaviourProps.Name, "wp-a"));
						add(ContentCatalog.TriggerZone, 8f, -4f, P(ObjectProps.ZoneId, "wp-b", ObjectProps.ZoneRadius, "2"));
						add(Islanders.ObjectName, -4f, -8f, P(BehaviourProps.Name, "Finn", Islanders.PoseProp, "stand", Islanders.CharacterProp, "2", Islanders.TextProp, BranchText));
						add("Creature_Goat", 6f, -10f, P(BehaviourProps.Name, "goats", ObjectProps.CreatureCount, "2"));
						add(ContentCatalog.TriggerZone, 14f, -14f, P(ObjectProps.ZoneId, "pen", ObjectProps.ZoneRadius, "2"));
						add(Islanders.ObjectName, -6f, 0f, P(BehaviourProps.Name, "Mia", Islanders.CharacterProp, "4"));
						add(ContentCatalog.TriggerZone, -6f, 14f, P(ObjectProps.ZoneId, "camp", ObjectProps.ZoneRadius, "3"));
						add("Log", 12f, 10f, P(BehaviourProps.Name, "mp-host"));
						add("Log", -2f, -3f, P(BehaviourProps.Name, "mp-client"));
						file.Props[IslandQuest.KeyTitle] = "CI crew";
						file.Props[IslandQuest.KeySteps] = "tame|Goat@pen|1|\nescort|Mia@camp|1|\nreach|nowhere|1|";
					}, x => e = x);
					if (e == null) yield break;
					IslandObjectRef gr = ScObjOf(e, "goats"), wr = ScObjOf(e, "Walker");
					CreatureSpawnPoint sp = gr != null ? gr.GetComponent<CreatureSpawnPoint>() : null;
					IslanderWalk ww = wr != null ? wr.GetComponent<IslanderWalk>() : null;
					if (sp == null || ww == null) { Fail(name + ": no goats' spot or walker"); yield break; }
					PutPlayerNear(gr.transform, 6f);
					yield return M4Wait(() => sp.Spawned.Count(a => a != null) >= 2, 30f);
					Check(ref ok, sp.Spawned.Count(a => a != null) == 2, "two goats came (" + sp.Spawned.Count(a => a != null) + ")");
					M4Stand(e, "mp-host");
					yield return new WaitForSeconds(2f);
					Check(ref ok, ww.HasPath && ww.State == "path", "the walker walks its path (" + ww.State + ")");
					float now = SharedClock.Now;
					Vector3 at = wr.transform.localPosition, want = ww.PlaceAt(now);
					Check(ref ok, Flat(at - want).magnitude < 1.5f, "on the host it is where the shared clock puts it (" + Flat(at - want).magnitude.ToString("F2") + " m off)");
					m4Refused0 = Taming.Refused;
					Log("MP34B WALK " + F(now) + " " + F(at.x) + " " + F(at.z));
					break;
				}
				case "walk":
				{
					float t, x, z;
					if (args.Length < 4 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out t) || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
						|| !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) { Fail(name + ": CIMp34b walk <t> <x> <z> (the host's WALK line)"); yield break; }
					yield return M4Island("Walker", v => e = v);
					if (e == null) yield break;
					M4Stand(e, "mp-client");
					IslandObjectRef wr = ScObjOf(e, "Walker");
					IslanderWalk ww = wr != null ? wr.GetComponent<IslanderWalk>() : null;
					if (ww == null) { Fail(name + ": no walker here"); yield break; }
					yield return new WaitForSeconds(2f);
					Vector3 then = ww.PlaceAt(t);
					float off = new Vector2(then.x - x, then.z - z).magnitude;
					Check(ref ok, SharedClock.Synced, "player 2's clock follows the host's");
					Check(ref ok, off < 1.5f, "at the host's moment player 2's walker is where the host had it (" + off.ToString("F2") + " m apart; " + F(then.x) + "," + F(then.z) + " vs " + F(x) + "," + F(z) + ")");
					float worst = 0f, moved = 0f;
					Vector3 start = wr.transform.localPosition;
					for (int i = 0; i < 12; i++)
					{
						yield return new WaitForSeconds(0.25f);
						worst = Mathf.Max(worst, Flat(wr.transform.localPosition - ww.PlaceAt(SharedClock.Now)).magnitude);
						moved = Mathf.Max(moved, Flat(wr.transform.localPosition - start).magnitude);
					}
					Check(ref ok, moved > 0.5f && worst < 1.5f, "player 2 sees it walk the same path (moved " + moved.ToString("F1") + " m, worst " + worst.ToString("F2") + " m off its clock place; " + ww.State + ")");
					Check(ref ok, OffGround(wr.transform) < 0.2f, "on the ground on player 2 (" + OffGround(wr.transform).ToString("F2") + ")");
					break;
				}
				case "talk":
				{
					yield return M4Island("Finn", v => e = v);
					if (e == null) yield break;
					IslandObjectRef fr = ScObjOf(e, "Finn");
					Islander isl = fr != null ? fr.GetComponentInChildren<Islander>(true) : null;
					if (isl == null) { Fail(name + ": no Finn here"); yield break; }
					Check(ref ok, GapBehaviours.Counter(e, "asked") == 0, "the flag asked starts unset");
					PutPlayerNear(fr.transform, 2f);
					yield return new WaitForSeconds(0.5f);
					isl.Talk();
					yield return new WaitForSeconds(0.3f);
					DialoguePanel.Next();
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, DialoguePanel.IsOpen && DialoguePanel.ShownAnswers.SequenceEqual(new[] { "Who are you?", "Bye", "Help" }), "player 2: three answers, ?asked hidden (" + string.Join(", ", DialoguePanel.ShownAnswers.ToArray()) + ")");
					DialoguePanel.Pick(1);
					yield return new WaitForSeconds(0.5f);
					Check(ref ok, DialoguePanel.ShownSection == "who", "player 2's answer 1 goes to #who (" + DialoguePanel.ShownSection + ")");
					DialoguePanel.Close();
					yield return M4Wait(() => GapBehaviours.Counter(e, "asked") == 1, 8f);
					Check(ref ok, GapBehaviours.Counter(e, "asked") == 1, "the host set the flag asked for player 2's answer (" + GapBehaviours.Counter(e, "asked") + ")");
					M4Stand(e, "mp-client");
					break;
				}
				case "talkh":
				{
					e = M4Host();
					if (e == null) { Fail(name + ": no ciplayer"); yield break; }
					yield return M4Wait(() => GapBehaviours.Counter(e, "asked") == 1, 8f);
					Check(ref ok, GapBehaviours.Counter(e, "asked") == 1, "the host has asked set (" + GapBehaviours.Counter(e, "asked") + ")");
					IslandObjectRef fr = ScObjOf(e, "Finn");
					Islander isl = fr != null ? fr.GetComponentInChildren<Islander>(true) : null;
					if (isl == null) { Fail(name + ": no Finn"); yield break; }
					PutPlayerNear(fr.transform, 2f);
					yield return new WaitForSeconds(0.5f);
					isl.Talk();
					yield return new WaitForSeconds(0.3f);
					DialoguePanel.Next();
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, DialoguePanel.IsOpen && DialoguePanel.ShownAnswers.Contains("You said a light?"), "the host's answers show ?asked now (" + string.Join(", ", DialoguePanel.ShownAnswers.ToArray()) + ")");
					DialoguePanel.Close();
					M4Stand(e, "mp-host");
					break;
				}
				case "tame":
				{
					yield return M4Island("goats", v => e = v);
					if (e == null) yield break;
					IslandObjectRef gr = ScObjOf(e, "goats");
					string food = Taming.DefaultFood(AI_NetworkBehaviourType.Goat);
					Func<AI_NetworkBehaviour[]> goats = () => UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>()
						.Where(a => a != null && a.behaviourType == AI_NetworkBehaviourType.Goat && Vector3.Distance(a.transform.position, gr.transform.position) < 25f).ToArray();
					yield return M4Wait(() => goats().Length >= 2, 20f);
					Check(ref ok, goats().Length >= 2, "player 2 sees the two goats (" + goats().Length + ")");
					if (goats().Length == 0) yield break;
					Check(ref ok, QuestTracker.StepOf(e) == 0 && Taming.Current(e).Contains("Goat@pen"), "the tame step is the current one on player 2");
					Gw4Take(me, food);
					me.Inventory.AddItem(food, 3);
					Check(ref ok, GrHold(me, food), "player 2 holds " + food);
					int had = me.Inventory.GetItemCount(food), taken = Taming.FoodTaken;
					Taming.LastAnswer = -1;
					AI_NetworkBehaviour goat = null;
					float t0 = Time.realtimeSinceStartup;
					while (Taming.LastAnswer != 1 && Time.realtimeSinceStartup - t0 < 12f)
					{
						goat = goats().OrderBy(a => Vector3.Distance(a.transform.position, me.transform.position)).First();
						if (Vector3.Distance(goat.transform.position, me.transform.position) > 2f) { PlayerMove.To(me, goat.transform.position + Vector3.up * 1.2f + Vector3.right * 1.2f); KeepAlive(me); }
						yield return new WaitForSeconds(0.25f);
					}
					Check(ref ok, Taming.LastAnswer == 1, "the host answered player 2's feed: it follows (answer " + Taming.LastAnswer + ", last: " + Taming.Why + ")");
					Check(ref ok, me.Inventory.GetItemCount(food) == had - 1 && Taming.FoodTaken == taken + 1, "one " + food + " taken, on the host's yes (" + had + " -> " + me.Inventory.GetItemCount(food) + ")");
					// still holding the food by the tamed goat: the host refuses those feeds, the food stays
					yield return new WaitForSeconds(6f);
					Check(ref ok, me.Inventory.GetItemCount(food) == had - 1, "refused feeds by the tamed goat take no food (" + me.Inventory.GetItemCount(food) + ", fed " + Taming.Fed + ", last answer " + Taming.LastAnswer + ")");
					// a feed the host must refuse (too far off): answered, food kept
					Taming.LastAnswer = -1;
					Taming.SendFeed(e, gr.Index, me.transform.position + new Vector3(40f, 0f, 0f), food);
					yield return M4Wait(() => Taming.LastAnswer == 0, 8f);
					Check(ref ok, Taming.LastAnswer == 0 && me.Inventory.GetItemCount(food) == had - 1, "a refused feed is answered and keeps the food (answer " + Taming.LastAnswer + ", food " + me.Inventory.GetItemCount(food) + ")");
					Gw4Take(me, food);
					// it follows player 2
					goat = goats().OrderBy(a => Vector3.Distance(a.transform.position, me.transform.position)).First();
					IslandObjectRef spot = ScObjOf(e, "mp-client");
					if (spot != null) MovePlayerTo(spot.transform.position);
					yield return M4Wait(() => Vector3.Distance(Flat(goat.transform.position), Flat(me.transform.position)) < 4.5f, 12f);
					Check(ref ok, Vector3.Distance(Flat(goat.transform.position), Flat(me.transform.position)) < 4.5f, "the goat followed player 2 (" + Vector3.Distance(Flat(goat.transform.position), Flat(me.transform.position)).ToString("F1") + " m behind)");
					break;
				}
				case "tameh":
				{
					e = M4Host();
					if (e == null) { Fail(name + ": no ciplayer"); yield break; }
					Network_Player other = M4Other();
					IslandObjectRef gr = ScObjOf(e, "goats");
					CreatureSpawnPoint sp = gr != null ? gr.GetComponent<CreatureSpawnPoint>() : null;
					AI_NetworkBehaviour goat = sp != null ? sp.Spawned.FirstOrDefault(Taming.IsTamed) : null;
					Check(ref ok, Taming.Following == 1 && goat != null, "the host has one goat following (" + Taming.Following + ")");
					Check(ref ok, goat != null && other != null && Vector3.Distance(Flat(goat.transform.position), Flat(other.transform.position)) < 5f, "it follows player 2 (" + (goat != null && other != null ? Vector3.Distance(Flat(goat.transform.position), Flat(other.transform.position)).ToString("F1") : "?") + " m)");
					Check(ref ok, Taming.Refused > m4Refused0, "the host refused player 2's other feeds (" + (Taming.Refused - m4Refused0) + ")");
					// on to the escort step (leading the goat to its pen is the single-player test's)
					QuestTracker.JumpTo(e, 0, 1);
					yield return new WaitForSeconds(1f);
					Check(ref ok, QuestTracker.StepOf(e) == 1 && IslanderWalks.EscortZone(e, "Mia") == "camp", "the escort step is the current one");
					M4Stand(e, "mp-host");
					break;
				}
				case "escort":
				{
					yield return M4Island("Mia", v => e = v);
					if (e == null) yield break;
					yield return M4Wait(() => QuestTracker.StepOf(e) == 1, 8f);
					Check(ref ok, QuestTracker.StepOf(e) == 1, "player 2 is at the escort step (" + QuestTracker.StepOf(e) + ")");
					IslandObjectRef mr = ScObjOf(e, "Mia");
					TriggerZone camp = IslanderWalks.ZoneOf(e, "camp");
					if (mr == null || camp == null) { Fail(name + ": no Mia or camp here"); yield break; }
					int got0 = IslanderWalks.Received;
					Vector3 home = mr.transform.position, toCamp = camp.transform.position - home; toCamp.y = 0f; toCamp.Normalize();
					MovePlayerTo(home + toCamp * 6f);
					yield return new WaitForSeconds(3f);
					Check(ref ok, Vector3.Distance(Flat(home), Flat(mr.transform.position)) > 1.5f, "Mia follows player 2 (" + Vector3.Distance(Flat(home), Flat(mr.transform.position)).ToString("F1") + " m walked, places got " + (IslanderWalks.Received - got0) + ")");
					for (int i = 0; i < 14 && QuestTracker.StepOf(e) < 2; i++)
					{
						Vector3 next = mr.transform.position + toCamp * 4f;
						if (Vector3.Distance(Flat(next), Flat(camp.transform.position)) < 4f) next = camp.transform.position + toCamp * 1f;
						MovePlayerTo(next);
						yield return new WaitForSeconds(2f);
					}
					yield return M4Wait(() => QuestTracker.StepOf(e) >= 2, 6f);
					yield return new WaitForSeconds(1f);
					Check(ref ok, QuestTracker.StepOf(e) >= 2, "player 2 led Mia into camp: the escort step is done (step " + QuestTracker.StepOf(e) + ")");
					Check(ref ok, IslanderWalks.Received > got0 && IslanderWalks.Inside(camp, mr.transform.position), "player 2 got her places from the host and sees her in camp (" + (IslanderWalks.Received - got0) + " places)");
					M4Stand(e, "mp-client");
					break;
				}
				case "escorth":
				{
					e = M4Host();
					if (e == null) { Fail(name + ": no ciplayer"); yield break; }
					IslandObjectRef mr = ScObjOf(e, "Mia");
					TriggerZone camp = IslanderWalks.ZoneOf(e, "camp");
					Vector3 l; float yaw;
					Check(ref ok, QuestTracker.StepOf(e) >= 2, "the host has the escort step done (" + QuestTracker.StepOf(e) + ")");
					Check(ref ok, mr != null && camp != null && IslanderWalks.Inside(camp, mr.transform.position) && MechFeatures.Saved(e, mr.Index, out l, out yaw), "Mia is in camp on the host, her place saved");
					break;
				}
				case "done":
				{
					GapBehaviours.TestHour = null;
					GapBehaviours.TestWeather = null;
					GapWorld.FakeCount = null;
					GapWorld.FakeOnline = -1;
					DialoguePanel.Close();
					RemovePlayerIsland(IslandWorldState.Islands.FirstOrDefault(x => x.HostName == PlayerIsland));
					break;
				}
				default:
					Fail("CIMp34b wait|waitc|sky|bells|zone|waitend|fb|fbc <fill>|fbcheck|fc|walk <t> <x> <z>|talk|talkh|tame|tameh|escort|escorth|done");
					yield break;
			}
			if (ok) Log("PASS: " + name); else Fail(name);
		}
	}
}
