using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>3.2: skipping the current quest step (QuestSkip) - F10 SkipStep and Esc > Custom Islands > Skip current quest step.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIQuestSkip", docs: "Dev, in a world (host): SK1-4 + FU3 - skipping the current quest step - F10 SkipStep on the quest panel's quest (a collect step gives the missing story items), Esc > Custom Islands > Skip current quest step asks first (pictures qskip_settings, qskip_confirm) and skips (a signal step sends its signal), the last step finishes the quest, then the side quest is current; QuestTest skip 2 finishes it; with no quest left SkipStep says No quest in progress; no old Stuck? button")]
		public static void QuestSkipCommand() { StartTest(QuestSkipRoutine()); }

		static IEnumerator QuestSkipRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const string key = "ci_skip_key";
			int keysBefore = StoryBook.Count(key);
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				file.Props[IslandQuest.KeyTitle] = "CI stuck";
				file.Props[IslandQuest.KeySteps] = "collect|story:" + key + "|2|Find the two cell keys\nsignal|ci-skip-bell|1|Ring the bell\nread|CI note nobody wrote|1|Read the note";
				file.Props[IslandQuest.Key(IslandQuest.KeyTitle, 1)] = "CI side";
				file.Props[IslandQuest.Key(IslandQuest.KeySteps, 1)] = "signal|ci-skip-drum|1|Beat the drum\nreach|ci-skip-zone|1|Go nowhere";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			try
			{
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1f);

				// the current quest = the panel's (the main quest); F10 SkipStep skips its collect step
				IslandWorldState.Entry cur; int n;
				Check(ref ok, QuestSkip.Current(out cur, out n) && cur == e && n == 0, "the current quest is the island's main quest (" + QuestSkip.DescribeCurrent() + ")");
				QuestSkip.SkipStepCommand(new string[0]);
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, QuestTracker.StepOf(e) == 1 && StoryBook.Count(key) == keysBefore + 2,
					"SkipStep: at step 2, the crew has the 2 keys (" + StoryBook.Count(key) + ", step " + (QuestTracker.StepOf(e) + 1) + ")");

				// Esc > Custom Islands: Skip current quest step asks first, Skip skips (the signal step sends its signal)
				WorldWindow.Open();
				yield return new WaitForSeconds(1.2f);
				Button b = WorldWindow.ButtonNamed("SkipStep");
				Check(ref ok, b != null && b.gameObject.activeInHierarchy && b.interactable, "the settings page has Skip current quest step, clickable for the host");
				QuestSkip.HoldOpen = true;
				Screenshot(new[] { "qskip_settings" });
				yield return new WaitForSeconds(1.5f);
				if (b != null) b.onClick.Invoke();
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, QuestSkip.ConfirmShown && QuestTracker.StepOf(e) == 1, "the button opens the confirm box; still at step 2");
				Screenshot(new[] { "qskip_confirm" });
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, QuestSkip.Confirm(), "Skip step confirmed");
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, !QuestSkip.ConfirmShown && Behaviours.HasSignal(e, "ci-skip-bell") && QuestTracker.StepOf(e) == 2, "settings skip: the island has ci-skip-bell, at step 3");
				WorldWindow.Close();

				// the last step: the quest finishes; then the side quest is the current one
				QuestSkip.SkipStepCommand(new string[0]);
				Check(ref ok, QuestTracker.IsDone(e, 0), "SkipStep on the last step: the main quest is done");
				yield return new WaitForSeconds(1.2f);
				QuestSkip.SkipStepCommand(new string[0]);
				Check(ref ok, Behaviours.HasSignal(e, "ci-skip-drum") && QuestTracker.StepOf(e, 1) == 1, "SkipStep again: the side quest (now current) at its step 2, its signal sent");

				// FU3 (11-19): F10 QuestTest skip 2 skips quest 2's current step the same way (its last: quest 2 done)
				yield return new WaitForSeconds(0.6f);
				QuestTestCommands.QuestTestCommand(new[] { "skip", "2" });
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, QuestTracker.IsDone(e, 1), "QuestTest skip 2: the side quest's last step skipped, the side quest is done");

				// SK3 (11-19): no quest under way - SkipStep says so and changes nothing
				List<string> said = new List<string>();
				Application.LogCallback grab = (text, trace, type) => { if (text != null && text.StartsWith("[CUSTOM ISLANDS]")) said.Add(text); };
				Application.logMessageReceived += grab;
				try { QuestSkip.SkipStepCommand(new string[0]); }
				finally { Application.logMessageReceived -= grab; }
				Check(ref ok, said.Any(s => s.Contains(QuestSkip.NoQuest)) && QuestTracker.IsDone(e, 0) && QuestTracker.IsDone(e, 1),
					"SkipStep with no quest under way: '" + QuestSkip.NoQuest + "' (" + string.Join(" / ", said.ToArray()) + ")");

				// SK4 (11-19): the old "Stuck? Skip this step" panel button and its setting are gone (panel + Esc page texts)
				WorldWindow.Open();
				yield return new WaitForSeconds(1.2f);
				string[] old = Object.FindObjectsOfType<Text>().Where(t => t != null && t.gameObject.activeInHierarchy && t.text != null
					&& (t.text.Contains("Stuck?") || t.text.Contains("Skip this step"))).Select(t => t.text).ToArray();
				Check(ref ok, old.Length == 0, "no old 'Stuck? Skip this step' button or setting on the panel or the Esc page (" + string.Join(" / ", old) + ")");
				WorldWindow.Close();
			}
			finally
			{
				QuestSkip.HoldOpen = false;
				QuestSkip.CloseConfirm();
				WorldWindow.Close();
				int extra = StoryBook.Count(key) - keysBefore;
				if (extra > 0) StoryBook.Take(key, extra);
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: quest step skip"); else Fail("quest step skip");
		}

		[ConsoleCommand(name: "CITickGuards", docs: "Dev, in a world (host): QD7 - one quest part that throws doesn't stop the other quest ticks: the quest timers and the daily quest throw in turn (QuestTracker.TestThrow) and the quest checks go on, each logged under its own name; the quest book's layout throws once and is tried again 5 s later; a talk action that throws still counts the talk step")]
		public static void TickGuardsCommand() { StartTest(TickGuardsRoutine()); }

		static IEnumerator TickGuardsRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			List<string> errors = new List<string>();
			Application.LogCallback grab = (text, trace, type) => { if (text != null && text.StartsWith("[CUSTOM ISLANDS]") && (type == LogType.Error || type == LogType.Exception)) errors.Add(text); };
			Application.logMessageReceived += grab;
			IslandWorldState.Entry e = null;
			try
			{
				// the quest timers (inside the quest tick) and the daily quest (beside it) throw until the quest checks have run 3
				// more times: they go on. (Counted in ticks, not seconds: a minimised Raft runs a frame or two a second and stalls
				// now and then, so "3 in 2.5 s" failed with the guards working - 11-19. Unguarded, the count would stop: 20 s cap.)
				foreach (string[] part in new[] { new[] { "timers", "Quest timers:" }, new[] { "daily", "Daily quest:" } })
				{
					int before = QuestTracker.TickCount;
					float t0 = Time.unscaledTime;
					QuestTracker.TestThrow = part[0];
					while (QuestTracker.TickCount - before < 3 && Time.unscaledTime - t0 < 20f) yield return null;
					// (one more second of throwing, so the daily part - every 1 s - has surely thrown too)
					for (float t1 = Time.unscaledTime; Time.unscaledTime - t1 < 1.2f;) yield return null;
					QuestTracker.TestThrow = null;
					int ticks = QuestTracker.TickCount - before;
					bool logged = errors.Any(x => x.Contains(part[1]) && x.Contains("CI test throw"));
					Check(ref ok, ticks >= 3 && logged, part[0] + " throwing: the quest checks went on (" + ticks + " in " + (Time.unscaledTime - t0).ToString("F1") + " s), the error " + (logged ? "" : "NOT ") + "logged as '" + part[1] + "'");
				}

				// the quest book: its layout throws once, the book tries again 5 s later (not every half second, not never).
				// (Watched every frame by the book's own clock: Time.unscaledTime is the same all through a frame, so the frame the
				// layout comes back in is the frame of the retry - 11-19; slow frames made fixed waits miss it)
				object ui = HarmonyLib.Traverse.Create(typeof(QuestBook)).Field("ui").GetValue();
				if (ui == null) Log("  (no notebook for the local player yet: the book part skipped)");
				else
				{
					int errs = errors.Count(x => x.Contains("Quest book:"));
					QuestTracker.TestThrow = "book";
					QuestBook.Refresh();
					QuestTracker.TestThrow = null;
					float threwAt = Time.unscaledTime;
					System.Func<string> sig = () => HarmonyLib.Traverse.Create(typeof(QuestBook)).Field("lastSig").GetValue<string>();
					Check(ref ok, sig() == null && errors.Count(x => x.Contains("Quest book:")) > errs, "the book's layout threw: logged, to be tried again");
					float retryIn = HarmonyLib.Traverse.Create(typeof(QuestBook)).Field("nextCheck").GetValue<float>() - threwAt;
					while (sig() == null && Time.unscaledTime - threwAt < 20f) yield return null;
					float after = Time.unscaledTime - threwAt;
					Check(ref ok, retryIn > 4f && after >= retryIn - 0.05f && sig() != null, "the book waited (retry set for " + retryIn.ToString("F1") + " s) and laid out again " + (sig() != null ? "after " + after.ToString("F1") + " s" : "never (20 s)"));
				}

				// a talk action that throws: the talk step still counts
				IslandFile f = PlayerTestIsland((file, add) =>
				{
					file.Props[IslandQuest.KeyTitle] = "CI talk guard";
					file.Props[IslandQuest.KeySteps] = Islanders.QuestType + "|CI Pat|1|Talk to Pat\nread|CI note nobody wrote|1|Read the note";
				});
				yield return SpawnPlayerIsland(f, x => e = x);
				if (e == null || e.Root == null) { Check(ref ok, false, "the talk island spawned"); }
				else
				{
					yield return StandRoutine(e.Root);
					yield return new WaitForSeconds(1f);
					QuestTracker.TestThrow = "talk";
					try { Islanders.Talked(e, e.Root.transform, 0, "CI Pat", 0); }
					finally { QuestTracker.TestThrow = null; }
					yield return new WaitForSeconds(0.6f);
					Check(ref ok, QuestTracker.StepOf(e) == 1 && errors.Any(x => x.Contains("Talk actions of 'CI Pat'")),
						"a talk action that throws: logged, and the talk step still counts (step " + (QuestTracker.StepOf(e) + 1) + ")");
				}
			}
			finally
			{
				QuestTracker.TestThrow = null;
				Application.logMessageReceived -= grab;
				if (e != null) RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: tick guards"); else Fail("tick guards");
		}
	}
}
