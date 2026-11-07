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
		[ConsoleCommand(name: "CIQuestSkip", docs: "Dev, in a world (host): skipping the current quest step - F10 SkipStep on the quest panel's quest (a collect step gives the missing story items), Esc > Custom Islands > Skip current quest step asks first (pictures qskip_settings, qskip_confirm) and skips (a signal step sends its signal), the last step finishes the quest, then the side quest is current")]
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
	}
}
