using System;
using System.Collections.Generic;
using System.Linq;
using HMLLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "Stuck? Skip step" (3.2, fix1107f): a way out of a soft-locked quest for players. The quest panel's button (host;
	/// players who joined see it greyed, "only the host can skip") asks first, then the step counts as done and the quest
	/// moves on as play moves it (QuestTracker.JumpTo forward = Set: the next step, events, rules, the reward at the end;
	/// players are told by the usual QuestStep message, the world is saved). Before that, what the step would have given
	/// where the mod can know it: a "collect" step's story items the crew is short of, a "signal" step's signal (or its
	/// object put in the state it waits for) - so later steps are not blocked by the skip itself. Also F10 "QuestTest skip".
	/// Per PC setting "questSkip" (world_rules.txt, Defaults > Keys): off hides the button.
	/// </summary>
	public static class QuestSkip
	{
		public const string SettingName = "questSkip";
		static bool? enabled;

		/// <summary>The "Skip step" button on the quest panel (this PC's world_rules.txt, "questSkip=on/off"; on by default).</summary>
		public static bool Enabled
		{
			get { if (enabled == null) enabled = (WorldRules.ReadDefault(SettingName) ?? "on").Trim().ToLowerInvariant() != "off"; return enabled.Value; }
			set { enabled = value; WorldRules.SaveDefault(SettingName, value ? "on" : "off"); if (!value) CloseConfirm(); }
		}

		public const string ConfirmText = "Only if you are stuck: this step counts as done and the quest moves on. Can't be undone.";

		/// <summary>Host: quest n of the island skips its current step (only when it is still step expect; -1 = any). What the
		/// step would have given is given first, then the quest moves on; the world is saved. False when nothing was skipped.</summary>
		public static bool Skip(IslandWorldState.Entry e, int n, int expect = -1)
		{
			if (e == null || !Raft_Network.IsHost) return false;
			IslandQuest q = QuestTracker.QuestOf(e, n);
			int step = QuestTracker.StepOf(e, n);
			if (!q.Exists || step >= q.Steps.Count || (expect >= 0 && step != expect)) return false;
			IslandQuest.Step s = q.Steps[step];
			string given = Produce(e, s);
			Debug.Log("[CUSTOM ISLANDS] Quest step skipped by host: " + e.HostName + " quest " + (n + 1) + " step " + (step + 1) + " (" + s.Type + (s.Target.Length > 0 ? " " + s.Target : "") + (given.Length > 0 ? "; " + given : "") + ")");
			// (the item or signal may have moved it on already - the counted check runs inside Give/Set; else as QuestTest step)
			if (QuestTracker.StepOf(e, n) == step) QuestTracker.JumpTo(e, n, step + 1);
			IslandWorldState.Save();
			return QuestTracker.StepOf(e, n) > step;
		}

		/// <summary>Host: what doing the step would have left behind, so the steps after it can be done. Returns what was done (for the log).</summary>
		internal static string Produce(IslandWorldState.Entry e, IslandQuest.Step s)
		{
			try
			{
				if (s.Type == "collect" && s.Target.Length > 0)
				{
					string id = StoryItems.IdOf(s.Target);
					int missing = Mathf.Max(1, s.Count) - QuestTracker.Found(e, s);
					if (id.Length == 0 || missing <= 0) return "";
					StoryBook.Give(id, missing, e);
					return "gave " + id + " x" + missing;
				}
				if (s.Type == QuestSteps.Signal && s.Target.Trim().Length > 0)
				{
					string obj, state;
					if (!QuestSteps.IsState(s.Target, out obj, out state))
					{
						if (!Behaviours.HasSignal(e, s.Target.Trim())) Behaviours.SendSignal(e, s.Target.Trim());
						return "signal " + s.Target.Trim();
					}
					// ("Gate=open": the object put in that state, as an open/close/show/hide action does - while the island is here)
					int changed = 0;
					foreach (IslandObjectRef r in Behaviours.TargetsOf(e, -1, obj))
					{
						bool visible, open;
						Behaviours.StateNow(e, r, out visible, out open);
						if (state == "open") open = true; else if (state == "closed") open = false;
						else if (state == "shown") visible = true; else if (state == "hidden") visible = false;
						Behaviours.SetState(e, r.Index, visible, open);
						changed++;
					}
					return changed > 0 ? obj + " " + state + " (" + changed + ")" : "";
				}
			}
			catch (Exception x) { Debug.LogWarning("[CUSTOM ISLANDS] Quest step skip: giving what the step gives failed: " + x.Message); }
			return "";
		}

		#region The confirm box

		static RectTransform box;
		static Text boxText;
		static Button yes;
		static int boxIsland = -1, boxQuest, boxStep;

		/// <summary>Tests: the box stays while the cursor is locked (a test has no inventory open).</summary>
		internal static bool HoldOpen;

		/// <summary>Whether the confirm box is on screen (tests, the picture).</summary>
		public static bool ConfirmShown { get { return box != null && box.gameObject.activeSelf; } }

		/// <summary>The quest panel's button: the confirm box for this island's quest n at this step.</summary>
		public static void AskFor(IslandWorldState.Entry e, int n, int step)
		{
			if (e == null || !Raft_Network.IsHost) return;
			if (box == null) Build();
			IslandQuest q = QuestTracker.QuestOf(e, n);
			if (!q.Exists || step >= q.Steps.Count) return;
			boxIsland = e.Id; boxQuest = n; boxStep = step;
			boxText.text = "Skip step " + (step + 1) + " of '" + q.ShownTitle + "'?\n<color=#ffc766>" + q.Steps[step].Describe() + "</color>\n\n" + ConfirmText;
			box.gameObject.SetActive(true);
		}

		/// <summary>The box's Skip button (tests click it through here).</summary>
		public static bool Confirm()
		{
			if (!ConfirmShown) return false;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == boxIsland);
			CloseConfirm();
			if (e == null) return false;
			bool done = Skip(e, boxQuest, boxStep);
			if (!done) DynamicIslands.Notify("That step has changed already: nothing was skipped", true);
			return done;
		}

		public static void CloseConfirm() { if (box != null) box.gameObject.SetActive(false); boxIsland = -1; }

		/// <summary>From the quest panel's tick: the box goes when the cursor is taken back (inventory closed) or its step is gone.</summary>
		internal static void Tick()
		{
			if (!ConfirmShown) return;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == boxIsland);
			if (e == null || !LoadSceneManager.IsGameSceneLoaded || (Cursor.lockState == CursorLockMode.Locked && !HoldOpen) || QuestTracker.StepOf(e, boxQuest) != boxStep || !Enabled)
				CloseConfirm();
		}

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_QuestSkip", 390);
			UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
			box = UIKit.Rect("SkipConfirm", canvas.transform);
			UIKit.Anchor(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 0));
			UIKit.Surface(box);
			UIKit.Vertical(box.gameObject, 10f, new RectOffset(18, 18, 14, 14), true);
			UIKit.Label(box, "Stuck on this step?", 17, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			boxText = UIKit.Label(box, "", 14, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Text");
			boxText.horizontalOverflow = HorizontalWrapMode.Wrap;
			boxText.verticalOverflow = VerticalWrapMode.Overflow;
			UIKit.Fit(boxText);
			RectTransform row = UIKit.Row(box, 32f, 8f, "Buttons");
			UIKit.Label(row, "", 12);
			Button no = UIKit.Button(row, "Cancel", CloseConfirm, "Keep the step", 120, 32f, 14);
			no.name = "Cancel";
			yes = UIKit.Button(row, "Skip step", () => Confirm(), "The step counts as done and the quest moves on (host; can't be undone)", 140, 32f, 14);
			yes.name = "Skip";
			UIKit.DangerButton(yes);
			box.gameObject.SetActive(false);
		}

		#endregion
	}
}
