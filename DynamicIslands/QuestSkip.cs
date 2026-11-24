using System;
using System.Collections.Generic;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "Skip current quest step" (3.2): a way out of a soft-locked quest. Not on the quest panel (the user, 11-07) but in two
	/// places a player looks for on purpose: a button in the world's settings (Esc > Custom Islands, WorldWindow; asks first)
	/// and the F10 command SkipStep (no arguments). Both act on the CURRENT quest (Current): the quest panel's quest when the
	/// panel shows one, else the quest that last started or moved on (Noted, from QuestTracker.Set / Apply), else - a saved
	/// world just loaded - the begun quest whose step changed on the latest day. Any quest of any island: storyline, plan,
	/// random islands, main or side. Host only. The step counts as done and the quest moves on as play moves it
	/// (QuestTracker.JumpTo forward = Set: the next step, events, rules, the reward and finished state at the end; players
	/// are told by the usual QuestStep message, the world is saved). Before that, what the step would have given where the mod
	/// can know it: a "collect" step's story items the crew is short of, a "signal" step's signal (or its object put in the
	/// state it waits for), and a "read", "reach" or "talk" step's note, zone or islander set off as the host's player
	/// would (Replay: its events, items and used mark - later done for real it gives nothing twice; on an island not built
	/// here when it loads). Also F10 "QuestTest skip [quest]" (tests: the nearest island's quest n).
	/// </summary>
	public static class QuestSkip
	{
		public const string ConfirmText = "Only if you are stuck: this step counts as done and the quest moves on. Can't be undone.";

		/// <summary>The quest that last started or moved on in this world (island id, quest n; -1 = none).</summary>
		static int lastIsland = -1, lastQuest;

		/// <summary>From QuestTracker.Set / Apply: quest n of the island started or moved on.</summary>
		internal static void Noted(int island, int n) { lastIsland = island; lastQuest = n; }

		/// <summary>Whether quest n of the island has a step to do.</summary>
		static bool Open(IslandWorldState.Entry e, int n)
		{
			if (e == null) return false;
			IslandQuest q = QuestTracker.QuestOf(e, n);
			return q.Exists && QuestTracker.StepOf(e, n) < q.Steps.Count;
		}

		/// <summary>The current quest: the quest panel's, else the last one that started or moved on, else the begun quest
		/// whose step changed on the latest day (a saved world). False = no quest in progress.</summary>
		public static bool Current(out IslandWorldState.Entry e, out int n)
		{
			e = null; n = 0;
			if (!LoadSceneManager.IsGameSceneLoaded) return false;
			int island = QuestTracker.PanelIsland, quest = QuestTracker.PanelQuest;
			e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == island);
			if (island >= 0 && Open(e, quest)) { n = quest; return true; }
			e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == lastIsland);
			if (lastIsland >= 0 && Open(e, lastQuest)) { n = lastQuest; return true; }
			int bestDay = -1;
			IslandWorldState.Entry best = null;
			foreach (IslandWorldState.Entry x in IslandWorldState.Islands)
			{
				int quests = QuestTracker.QuestsOf(x);
				for (int i = 0; i < quests; i++)
				{
					int day = QuestTracker.StepDayOf(x, i);
					if (day > bestDay && QuestTracker.StepOf(x, i) > 0 && Open(x, i)) { bestDay = day; best = x; n = i; }
				}
			}
			e = best;
			return best != null;
		}

		/// <summary>"'Quest title' (island), step 2: Find the keys" for the current quest, or "" when there is none.</summary>
		public static string DescribeCurrent()
		{
			IslandWorldState.Entry e; int n;
			if (!Current(out e, out n)) return "";
			IslandQuest q = QuestTracker.QuestOf(e, n);
			int step = QuestTracker.StepOf(e, n);
			return "'" + q.ShownTitle + "' (" + e.HostName + "), step " + (step + 1) + ": " + q.Steps[step].Describe();
		}

		public const string NoQuest = "No quest in progress";

		/// <summary>F10 SkipStep: the current quest's step counts as done and the quest moves on (host).</summary>
		[ConsoleCommand(name: "SkipStep", docs: "Stuck on a quest? SkipStep = the current quest's step counts as done and the quest moves on (host; can't be undone). Also Esc > Custom Islands > Skip current quest step")]
		public static void SkipStepCommand(string[] args)
		{
			if (!LoadSceneManager.IsGameSceneLoaded) { DynamicIslands.Notify("SkipStep works in a world", true); return; }
			if (!Raft_Network.IsHost) { DynamicIslands.Notify("Only the host can skip a quest step (SkipStep)", true); return; }
			IslandWorldState.Entry e; int n;
			if (!Current(out e, out n)) { DynamicIslands.Notify(NoQuest, true); return; }
			SkipAndTell(e, n, QuestTracker.StepOf(e, n));
		}

		/// <summary>The settings page's button: the confirm box for the current quest's step (host).</summary>
		public static void AskCurrent()
		{
			if (!Raft_Network.IsHost) { DynamicIslands.Notify("Only the host can skip a quest step", true); return; }
			IslandWorldState.Entry e; int n;
			if (!Current(out e, out n)) { DynamicIslands.Notify(NoQuest, true); return; }
			AskFor(e, n, QuestTracker.StepOf(e, n));
		}

		/// <summary>Skip that quest's step (if still that step) and say where the quest is now.</summary>
		static bool SkipAndTell(IslandWorldState.Entry e, int n, int step)
		{
			if (!Skip(e, n, step)) { DynamicIslands.Notify("That step has changed already: nothing was skipped", true); return false; }
			IslandQuest q = QuestTracker.QuestOf(e, n);
			int now = QuestTracker.StepOf(e, n);
			DynamicIslands.Notify("Step skipped: '" + q.ShownTitle + "' " + (now >= q.Steps.Count ? "is done" : "is at step " + (now + 1) + ": " + q.Steps[now].Describe()));
			return true;
		}

		/// <summary>Host: quest n of the island skips its current step (only when it is still step expect; -1 = any). What the
		/// step would have given is given first, then the quest moves on; the world is saved. False when nothing was skipped.</summary>
		public static bool Skip(IslandWorldState.Entry e, int n, int expect = -1)
		{
			if (e == null || !Raft_Network.IsHost) return false;
			IslandQuest q = QuestTracker.QuestOf(e, n);
			int step = QuestTracker.StepOf(e, n);
			if (!q.Exists || step >= q.Steps.Count || (expect >= 0 && step != expect)) return false;
			IslandQuest.Step s = q.Steps[step];
			string given = Produce(e, s), replayed = Replay(e, s);
			if (replayed.Length > 0) given += (given.Length > 0 ? "; " : "") + replayed;
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

		/// <summary>Host: steps replayed now (an island built here) and kept for their island's load (the CI checks read them).</summary>
		internal static int Replayed, ReplayKept;

		/// <summary>
		/// Host (quest softlock audit M8): a "read", "reach" or "talk" step skipped - the note, zone or islander it names is
		/// set off as the host's player doing it would: the note's "read" event (its checks, items, doors; marked read for the
		/// crew) and its page in the book, the zone's walk-in (its checks, message, loot, used mark, "enter" event), the
		/// islander's talk (marked talked to, its "talk" event). Before, only the step moved on: what the note, zone or talk
		/// gave or opened - a key, a door, the next island's signal - never came. Its "done" mark first: a note read, a zone
		/// that fires once fired or an islander talked to already is left alone (later done for real, the same marks keep
		/// it from giving twice). On an island not built here: kept in its saved state, done when it loads
		/// (Behaviours.QueueSkipped). A step without a target ("read any note") or another kind of step: nothing.
		/// </summary>
		internal static string Replay(IslandWorldState.Entry e, IslandQuest.Step s)
		{
			string ev = s.Type == "read" ? "read" : s.Type == "reach" ? "enter" : s.Type == Islanders.QuestType ? Islanders.TalkEvent : null;
			string target = s.Target.Trim();
			if (ev == null || target.Length == 0 || e == null || !Raft_Network.IsHost) return "";
			try
			{
				IslandFile f = IslandFile.Load(IslandSpawner.PathFor(e.Name));
				if (f == null) return "";
				StableIds.Layout l = null;
				for (int i = 0; i < f.Objects.Count; i++)
				{
					IslandObject o = f.Objects[i];
					Dictionary<string, string> p = o.Props ?? new Dictionary<string, string>();
					string name = ev == "read" ? (ObjectProps.IsNote(o.Name, p) ? ObjectProps.Get(p, ObjectProps.NoteTitle) : null)
						: ev == "enter" ? (o.Name == ContentCatalog.TriggerZone ? ObjectProps.Get(p, ObjectProps.ZoneId) : null)
						: (Islanders.Is(o.Name) ? Islanders.NameOf(p) : null);
					if (name == null || !string.Equals(target, IslandQuest.Clean(name), StringComparison.OrdinalIgnoreCase)) continue;
					// (done already - by a player, or an earlier skip: the next one of that name, if any)
					if (ev == "read" && e.State.ContainsKey(Behaviours.DoneBase + i)) continue;
					if (ev == Islanders.TalkEvent && ContentState.IsUsed(e, Islanders.TalkedBase + i)) continue;
					if (ev == "enter" && !ObjectProps.Repeats(p))
					{
						if (l == null) l = StableIds.LayoutOf(f, e.Position.y > IslandSpawner.FlyingThreshold);
						int z = l.Zones.IndexOf(i);
						if (z >= 0 && ContentState.IsUsed(e, TriggerZone.KeyBase + z)) continue;
					}
					if (e.Root != null) return ReplayLoaded(e, i, ev) ? ev + " of " + name + " set off" : "";
					Behaviours.QueueSkipped(e, i, ev);
					ReplayKept++;
					return ev + " of " + name + " kept for when the island loads here";
				}
			}
			catch (Exception x) { Debug.LogWarning("[CUSTOM ISLANDS] Quest step skip: setting off the step's " + ev + " failed: " + x.Message); }
			return "";
		}

		/// <summary>Host, the island built here: object index's note read, zone walked into or islander talked to by the host's
		/// player (Replay), unless done already. False when nothing was set off.</summary>
		internal static bool ReplayLoaded(IslandWorldState.Entry e, int index, string ev)
		{
			IslandObjectRef r = Behaviours.RefsOf(e).FirstOrDefault(x => x.Index == index);
			if (r == null) return false;
			if (ev == "read")
			{
				if (e.State.ContainsKey(Behaviours.DoneBase + index)) return false;
				CustomNote n = r.GetComponentInChildren<CustomNote>(true);
				Behaviours.Fire(e, index, "read", true);
				if (n != null && n.Text.Trim().Length > 0)
					StoryBook.AddPage("note:" + StoryBook.PageIsland(e) + ":" + index, n.Title.Length > 0 ? n.Title : "Note", n.Text, Behaviours.IslandTitle(e));
			}
			else if (ev == "enter")
			{
				TriggerZone z = r.GetComponent<TriggerZone>();
				if (z == null || (!z.Repeats && z.HasFired)) return false;
				z.Enter();
			}
			else if (ev == Islanders.TalkEvent)
			{
				if (ContentState.IsUsed(e, Islanders.TalkedBase + index)) return false;
				Islanders.Talked(e, r.transform, index, Islanders.NameOf(r.Props), 0);
			}
			else return false;
			Replayed++;
			Debug.Log("[CUSTOM ISLANDS] Quest step skipped: '" + e.HostName + "' object " + index + "'s " + ev + " set off for the host's player");
			return true;
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

		/// <summary>The settings page's "Skip this step": the confirm box for this island's quest n at this step.</summary>
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
			int n = boxQuest, step = boxStep;
			CloseConfirm();
			if (e == null) return false;
			bool done = SkipAndTell(e, n, step);
			WorldWindow.Refresh();
			return done;
		}

		public static void CloseConfirm() { if (box != null) box.gameObject.SetActive(false); boxIsland = -1; }

		/// <summary>From the quest tick: the box goes when the cursor is taken back (the menu closed) or its step is gone.</summary>
		internal static void Tick()
		{
			if (!ConfirmShown) return;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == boxIsland);
			if (e == null || !LoadSceneManager.IsGameSceneLoaded || (Cursor.lockState == CursorLockMode.Locked && !HoldOpen) || QuestTracker.StepOf(e, boxQuest) != boxStep)
				CloseConfirm();
		}

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_QuestSkip", 880); // (over the world's settings, 870)
			UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
			box = UIKit.Rect("SkipConfirm", canvas.transform);
			UIKit.Anchor(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 0));
			UIKit.Surface(box);
			UIKit.Vertical(box.gameObject, 10f, new RectOffset(18, 18, 14, 14), true);
			UIKit.Label(box, "Skip this quest step?", 17, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
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
