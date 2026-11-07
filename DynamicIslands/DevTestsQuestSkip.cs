using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>3.2 (fix1107f): the quest panel's "Stuck? Skip this step" (QuestSkip).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIQuestSkip", docs: "Dev, in a world (host): the quest panel's Stuck? Skip this step - the button and its confirm box (picture qskip_confirm), a collect step skipped gives the missing story items, a signal step sends its signal, a changed step is not skipped, a side quest's step, the world saved and read again keeps it all, the last step skipped finishes the quest, the setting hides the button")]
		public static void QuestSkipCommand() { StartTest(QuestSkipRoutine()); }

		static IEnumerator QuestSkipRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			const string key = "ci_skip_key";
			int keysBefore = StoryBook.Count(key);
			bool settingWas = QuestSkip.Enabled;
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
			var made = new List<IslandWorldState.Entry> { e };
			try
			{
				QuestSkip.Enabled = true;
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1.5f);
				Button b = QuestTracker.SkipButton;
				Check(ref ok, QuestTracker.PanelShown && b != null && b.gameObject.activeInHierarchy && b.interactable && UIKit.LabelOf(b).text.Contains("Skip"),
					"the panel shows Stuck? Skip this step, clickable for the host (" + (b != null ? UIKit.LabelOf(b).text : "no button") + ")");

				// the button: the confirm box first, nothing skipped yet
				QuestSkip.HoldOpen = true;
				if (b != null) b.onClick.Invoke();
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, QuestSkip.ConfirmShown && QuestTracker.StepOf(e) == 0, "the button opens the confirm box; still at step 1");
				Screenshot(new[] { "qskip_confirm" });
				yield return new WaitForSeconds(1f);

				// collect: the crew gets the two keys, step 2 is current
				Check(ref ok, QuestSkip.Confirm(), "Skip step confirmed");
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, !QuestSkip.ConfirmShown && QuestTracker.StepOf(e) == 1 && StoryBook.Count(key) == keysBefore + 2,
					"collect skipped: at step 2, the crew has the 2 keys (" + StoryBook.Count(key) + ", step " + (QuestTracker.StepOf(e) + 1) + ")");

				// a box for a step that moved on meanwhile skips nothing
				Check(ref ok, !QuestSkip.Skip(e, 0, 0) && QuestTracker.StepOf(e) == 1, "a skip for step 1 (gone) skips nothing");

				// signal: sent, step 3 current
				Check(ref ok, QuestSkip.Skip(e, 0, 1), "signal step skipped");
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, Behaviours.HasSignal(e, "ci-skip-bell") && QuestTracker.StepOf(e) == 2, "signal skipped: the island has ci-skip-bell, at step 3");

				// a side quest's step
				Check(ref ok, QuestSkip.Skip(e, 1) && Behaviours.HasSignal(e, "ci-skip-drum") && QuestTracker.StepOf(e, 1) == 1, "side quest: its signal step skipped, at its step 2");

				// the world saved and read again
				yield return GstReload(made);
				e = made.FirstOrDefault();
				Check(ref ok, e != null && QuestTracker.StepOf(e) == 2 && QuestTracker.StepOf(e, 1) == 1 && Behaviours.HasSignal(e, "ci-skip-bell") && Behaviours.HasSignal(e, "ci-skip-drum") && StoryBook.Count(key) == keysBefore + 2,
					"after a save and a reload: main at step 3, side quest at step 2, both signals and the keys kept");
				if (e == null) yield break;

				// the last step: the quest is done
				Check(ref ok, QuestSkip.Skip(e, 0) && QuestTracker.IsDone(e, 0), "the last step skipped: the quest is done");
				Check(ref ok, !QuestSkip.Skip(e, 0), "a done quest has nothing to skip");

				// the setting hides the button (the side quest is shown now)
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1.2f);
				b = QuestTracker.SkipButton;
				bool shownOn = b != null && b.gameObject.activeInHierarchy;
				QuestSkip.Enabled = false;
				yield return new WaitForSeconds(1.2f);
				Check(ref ok, shownOn && !b.gameObject.activeInHierarchy, "the side quest's panel has the button; the setting off hides it");
			}
			finally
			{
				QuestSkip.HoldOpen = false;
				QuestSkip.CloseConfirm();
				QuestSkip.Enabled = settingWas;
				int extra = StoryBook.Count(key) - keysBefore;
				if (extra > 0) StoryBook.Take(key, extra);
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: quest step skip"); else Fail("quest step skip");
		}
	}
}
