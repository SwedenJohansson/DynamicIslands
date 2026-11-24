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
	/// and the F10 command SkipStep (no arguments). Both act on the CURRENT quest (Current): the one chosen with the page's
	/// "Other quest", else the first of Candidates - the quest panel's quest when the panel shows one, the quest that last
	/// started or moved on (Noted, from QuestTracker.Set / Apply), the begun quests by the day their step changed (a saved
	/// world just loaded), then any other open quest of an island within 150 m of any player (11-24: a side quest behind
	/// an open main quest, a quest stuck at its first step, a client's island). Any quest of any island: storyline, plan,
	/// random islands, main or side. Host only; a client sees the button greyed and is told to ask the host. Beside it
	/// on the page, Spawn to raft (any player; the same box asks - AskSpawn). The step counts as done and the quest moves on as play moves it
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

		/// <summary>
		/// The quests a skip can be for, the likeliest first (11-24, "make sure that button always works"): the quest panel's,
		/// the one that last started or moved on, the begun quests by the day their step last changed (a saved world), then
		/// every other open quest of an island within 150 m of ANY player - its first step too, and a side quest while the main
		/// one is open (the panel shows the main one first), and an island only a client is at (the host picks it with "Other
		/// quest"). Before, only the first three: a quest stuck at its first step out of the panel's reach, a side quest
		/// behind an open main quest, or a client's quest far from the host could not be chosen.
		/// </summary>
		public static List<KeyValuePair<IslandWorldState.Entry, int>> Candidates()
		{
			List<KeyValuePair<IslandWorldState.Entry, int>> list = new List<KeyValuePair<IslandWorldState.Entry, int>>();
			if (!LoadSceneManager.IsGameSceneLoaded) return list;
			Action<IslandWorldState.Entry, int> add = (x, i) =>
			{
				if (x != null && Open(x, i) && !list.Any(k => k.Key == x && k.Value == i)) list.Add(new KeyValuePair<IslandWorldState.Entry, int>(x, i));
			};
			int island = QuestTracker.PanelIsland;
			if (island >= 0) add(IslandWorldState.Islands.FirstOrDefault(x => x.Id == island), QuestTracker.PanelQuest);
			if (lastIsland >= 0) add(IslandWorldState.Islands.FirstOrDefault(x => x.Id == lastIsland), lastQuest);
			List<KeyValuePair<int, KeyValuePair<IslandWorldState.Entry, int>>> begun = new List<KeyValuePair<int, KeyValuePair<IslandWorldState.Entry, int>>>();
			List<KeyValuePair<float, KeyValuePair<IslandWorldState.Entry, int>>> near = new List<KeyValuePair<float, KeyValuePair<IslandWorldState.Entry, int>>>();
			List<Vector3> players = UnityEngine.Object.FindObjectsOfType<Network_Player>().Where(p => p != null && p.transform != null).Select(p => p.transform.position).ToList();
			Network_Player me = RAPI.GetLocalPlayer();
			foreach (IslandWorldState.Entry x in IslandWorldState.Islands)
			{
				int quests = QuestTracker.QuestsOf(x);
				if (quests == 0) continue;
				float far = -1f;
				for (int i = 0; i < quests; i++)
				{
					if (!Open(x, i)) continue;
					KeyValuePair<IslandWorldState.Entry, int> k = new KeyValuePair<IslandWorldState.Entry, int>(x, i);
					if (QuestTracker.StepOf(x, i) > 0) { begun.Add(new KeyValuePair<int, KeyValuePair<IslandWorldState.Entry, int>>(QuestTracker.StepDayOf(x, i), k)); continue; }
					if (far < 0f) far = Distance(x, players, me);
					if (far < float.MaxValue) near.Add(new KeyValuePair<float, KeyValuePair<IslandWorldState.Entry, int>>(far + i * 0.001f, k));
				}
			}
			foreach (var b in begun.OrderByDescending(b => b.Key)) add(b.Value.Key, b.Value.Value);
			foreach (var b in near.OrderBy(b => b.Key)) add(b.Value.Key, b.Value.Value);
			return list;
		}

		/// <summary>How far the island is from the local player when any player is within 150 m of its land; else MaxValue.</summary>
		static float Distance(IslandWorldState.Entry e, List<Vector3> players, Network_Player me)
		{
			// (the island built here: its land's middle and reach; not built here - a client's island - its position, generously)
			IslandInfoTag tag = e.Root != null ? e.Root.GetComponent<IslandInfoTag>() : null;
			Vector3 c = tag != null ? e.Root.transform.position + tag.LocalCentre : e.Position;
			float r = tag != null ? tag.Radius : Mathf.Max(e.Root != null ? 30f : 150f, CustomIslandSpawner.LandRadius(e.Name));
			Func<Vector3, float> flat = at => new Vector2(c.x - at.x, c.z - at.z).magnitude;
			if (!players.Any(at => flat(at) < r + 150f)) return float.MaxValue;
			return me != null ? flat(me.transform.position) : 0f;
		}

		/// <summary>The quest chosen with "Other quest" (island id, quest n; -1 = none: the first candidate). Reset when the window opens.</summary>
		static int chosenIsland = -1, chosenQuest;

		public static void ResetChoice() { chosenIsland = -1; }

		/// <summary>Which candidate the current quest is (0-based) and how many there are.</summary>
		public static int CurrentIndex(out int count)
		{
			List<KeyValuePair<IslandWorldState.Entry, int>> list = Candidates();
			count = list.Count;
			return Math.Max(0, list.FindIndex(k => k.Key.Id == chosenIsland && k.Value == chosenQuest));
		}

		/// <summary>"Other quest": the next candidate becomes the current quest (round).</summary>
		public static void NextCandidate()
		{
			List<KeyValuePair<IslandWorldState.Entry, int>> list = Candidates();
			if (list.Count == 0) { chosenIsland = -1; return; }
			int i = Math.Max(0, list.FindIndex(k => k.Key.Id == chosenIsland && k.Value == chosenQuest));
			KeyValuePair<IslandWorldState.Entry, int> next = list[(i + 1) % list.Count];
			chosenIsland = next.Key.Id; chosenQuest = next.Value;
			WorldWindow.Refresh();
		}

		/// <summary>The current quest: the one chosen with "Other quest" while it is still open, else the first candidate
		/// (Candidates: the quest panel's, the last that started or moved on, ...). False = no quest in progress.</summary>
		public static bool Current(out IslandWorldState.Entry e, out int n)
		{
			e = null; n = 0;
			List<KeyValuePair<IslandWorldState.Entry, int>> list = Candidates();
			if (list.Count == 0) return false;
			int i = Math.Max(0, list.FindIndex(k => k.Key.Id == chosenIsland && k.Value == chosenQuest));
			e = list[i].Key; n = list[i].Value;
			return true;
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
		public const string ClientNote = "Only the host can skip a quest step: ask the host (Esc > Custom Islands)";

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
			if (!Raft_Network.IsHost) { DynamicIslands.Notify(ClientNote, true); return; }
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
		/// (Behaviours.QueueSkipped). An "open", "set" or "lights" step likewise (M8 limit closed 11-23): its chest opened for
		/// the host's player (its checks, loot, used mark, "open" event; one opened already is left alone), its set's pieces'
		/// "set" event and signal, its group's lights switched on (their "lit" events, then "alllit" and the signal) - a
		/// set or group done already (its signal) is left alone, and a step asking for part of one (2 of 5) sets off nothing.
		/// A step without a target ("read any note") or another kind of step: nothing.
		/// </summary>
		internal static string Replay(IslandWorldState.Entry e, IslandQuest.Step s)
		{
			string ev = s.Type == "read" ? "read" : s.Type == "reach" ? "enter" : s.Type == Islanders.QuestType ? Islanders.TalkEvent
				: s.Type == "open" ? "open" : s.Type == Collectibles.QuestType ? Collectibles.SetEvent : s.Type == Lights.QuestType ? Lights.AllLitEvent : null;
			string target = s.Target.Trim();
			if (ev == null || target.Length == 0 || e == null || !Raft_Network.IsHost) return "";
			try
			{
				IslandFile f = IslandFile.Load(IslandSpawner.PathFor(e.Name));
				if (f == null) return "";
				StableIds.Layout l = null;
				Func<IslandObject, string> nameOf = o =>
				{
					Dictionary<string, string> q = o.Props ?? new Dictionary<string, string>();
					string n = ev == "read" ? (ObjectProps.IsNote(o.Name, q) ? ObjectProps.Get(q, ObjectProps.NoteTitle) : null)
						: ev == "enter" ? (o.Name == ContentCatalog.TriggerZone ? ObjectProps.Get(q, ObjectProps.ZoneId) : null)
						: ev == "open" ? (ObjectProps.IsLoot(o.Name, q) ? ObjectProps.Get(q, ObjectProps.NoteTitle) : null)
						: ev == Collectibles.SetEvent ? (Collectibles.IsPiece(q) ? Collectibles.SetOf(q) : null)
						: ev == Lights.AllLitEvent ? (Lights.IsLight(o.Name, q) ? Lights.GroupOf(q) : null)
						: (Islanders.Is(o.Name) ? Islanders.NameOf(q) : null);
					return n != null && string.Equals(target, IslandQuest.Clean(n), StringComparison.OrdinalIgnoreCase) ? n : null;
				};
				// (a set or light group: the whole of it, once - done already: its signal; a step asking for part of it: nothing)
				if (ev == Collectibles.SetEvent || ev == Lights.AllLitEvent)
				{
					int total = f.Objects.Count(o => nameOf(o) != null);
					if (Behaviours.HasSignal(e, ev == Collectibles.SetEvent ? Collectibles.SignalOf(target) : Lights.SignalOf(target)) || (s.Count > 0 && s.Count < total)) return "";
				}
				for (int i = 0; i < f.Objects.Count; i++)
				{
					IslandObject o = f.Objects[i];
					Dictionary<string, string> p = o.Props ?? new Dictionary<string, string>();
					string name = nameOf(o);
					if (name == null) continue;
					if (ev == "open") { int k = Behaviours.RefusedKey(e, i, "open"); if (k >= 0 && ContentState.IsUsed(e, k)) continue; }
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

		/// <summary>Host, the island built here: object index's note read, zone walked into, islander talked to, chest opened,
		/// set or light group done by the host's player (Replay), unless done already. False when nothing was set off.</summary>
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
			else if (ev == "open")
			{
				// (as LootCrate.Open after its claim: the checks - a key taken - the used mark, the loot, the "open" event)
				LootCrate c = r.GetComponentInChildren<LootCrate>(true);
				if (c == null || c.Looted || !Behaviours.Allows(e, index, "open")) return false;
				ContentState.MarkUsed(c.transform, c.StateKey);
				TriggerZone.Give(c.ThisFill());
				Behaviours.Fire(e, index, "open", true, true);
			}
			else if (ev == Collectibles.SetEvent)
			{
				string set = Collectibles.SetOf(r.Props);
				if (set.Length == 0 || Behaviours.HasSignal(e, Collectibles.SignalOf(set))) return false;
				// (as Collectibles.Tick when the last piece is found: the signal - Tick won't do it again - and every piece's "set")
				Behaviours.SendSignal(e, Collectibles.SignalOf(set));
				foreach (IslandObjectRef x in Behaviours.RefsOf(e).Where(x => Collectibles.IsPiece(x.Props) && string.Equals(Collectibles.SetOf(x.Props), set, StringComparison.OrdinalIgnoreCase)).ToList())
					Behaviours.FireFromHost(e, x.Index, Collectibles.SetEvent);
			}
			else if (ev == Lights.AllLitEvent)
			{
				string g = Lights.GroupOf(r.Props);
				List<IslandObjectRef> all = g.Length > 0 ? Lights.GroupLights(e, g) : new List<IslandObjectRef>();
				if (all.Count == 0 || Behaviours.HasSignal(e, Lights.SignalOf(g)) || all.All(x => Lights.Lit(e, x))) return false;
				// (one by one as a player switching them: each one's "lit", the last one's Changed the group's "alllit" and
				// signal; its PuzzleLight told first, so it doesn't see the change again)
				foreach (IslandObjectRef x in all)
				{
					if (Lights.Lit(e, x)) continue;
					bool visible, open;
					Behaviours.StateNow(e, x, out visible, out open);
					Behaviours.SetState(e, x.Index, visible, !Lights.StartsOn(x.Props));
					PuzzleLight pl = x.GetComponentInChildren<PuzzleLight>(true);
					if (pl != null) pl.Sync();
					Lights.Changed(e, x, true);
				}
			}
			else return false;
			Replayed++;
			Debug.Log("[CUSTOM ISLANDS] Quest step skipped: '" + e.HostName + "' object " + index + "'s " + ev + " set off for the host's player");
			return true;
		}

		#region The confirm box

		static RectTransform box;
		static Text boxTitle, boxText;
		static Button yes, cancel;
		static int boxIsland = -1, boxQuest, boxStep;
		/// <summary>The box asks about Spawn to raft (11-24: the settings page's other way out), not a quest step.</summary>
		static bool boxSpawn;

		/// <summary>Tests: whether the box up is the Spawn to raft question.</summary>
		public static bool AskingSpawn { get { return ConfirmShown && boxSpawn; } }
		/// <summary>Tests: the box's Yes button.</summary>
		public static Button YesButton { get { return yes; } }
		/// <summary>Tests: the box's Cancel button.</summary>
		public static Button CancelButton { get { return cancel; } }

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
			boxIsland = e.Id; boxQuest = n; boxStep = step; boxSpawn = false;
			boxTitle.text = "Skip this quest step?";
			UIKit.LabelOf(yes).text = "Skip step";
			boxText.text = "Skip step " + (step + 1) + " of '" + q.ShownTitle + "'?\n<color=#ffc766>" + q.Steps[step].Describe() + "</color>\n\n" + ConfirmText;
			box.gameObject.SetActive(true);
		}

		/// <summary>
		/// The settings page's "Spawn to raft" (any player - each moves only themselves): the same box asks Raft's own Unstuck
		/// question; Yes closes the window and the pause menu and stands the local player on the raft (SpawnToRaft.FromMenu).
		/// Dead, asleep or on a zipline: not now, and why. "No raft block" asks anyway - Yes looks again (RK16: a moment's miss).
		/// </summary>
		public static void AskSpawn()
		{
			string why = SpawnToRaft.WhyNot();
			if (why != null && why != SpawnToRaft.NoBlock) { DynamicIslands.Notify("Spawn to raft: not now (" + why + ")", true); return; }
			if (box == null) Build();
			boxIsland = -1; boxSpawn = true;
			boxTitle.text = "Spawn to raft?";
			UIKit.LabelOf(yes).text = "Spawn to raft";
			boxText.text = SpawnToRaft.Question + "\n\nYou stand on your raft again. Only you move; the quests stay as they are.";
			box.gameObject.SetActive(true);
		}

		/// <summary>The box's Yes button (tests click it through here): skip the step, or spawn to the raft.</summary>
		public static bool Confirm()
		{
			if (!ConfirmShown) return false;
			if (boxSpawn)
			{
				CloseConfirm();
				WorldWindow.Close();
				return SpawnToRaft.FromMenu();
			}
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == boxIsland);
			int n = boxQuest, step = boxStep;
			CloseConfirm();
			if (e == null) return false;
			bool done = SkipAndTell(e, n, step);
			WorldWindow.Refresh();
			return done;
		}

		public static void CloseConfirm() { if (box != null) box.gameObject.SetActive(false); boxIsland = -1; boxSpawn = false; }

		/// <summary>From the quest tick: the box goes when the cursor is taken back (the menu closed) or its step is gone.</summary>
		internal static void Tick()
		{
			if (!ConfirmShown) return;
			if (boxSpawn)
			{
				string why = SpawnToRaft.WhyNot();
				if (!LoadSceneManager.IsGameSceneLoaded || (Cursor.lockState == CursorLockMode.Locked && !HoldOpen) || (why != null && why != SpawnToRaft.NoBlock)) CloseConfirm();
				return;
			}
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
			boxTitle = UIKit.Label(box, "Skip this quest step?", 17, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			boxText = UIKit.Label(box, "", 14, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Text");
			boxText.horizontalOverflow = HorizontalWrapMode.Wrap;
			boxText.verticalOverflow = VerticalWrapMode.Overflow;
			UIKit.Fit(boxText);
			RectTransform row = UIKit.Row(box, 32f, 8f, "Buttons");
			UIKit.Label(row, "", 12);
			cancel = UIKit.Button(row, "Cancel", CloseConfirm, "Nothing changes", 120, 32f, 14);
			cancel.name = "Cancel";
			yes = UIKit.Button(row, "Skip step", () => Confirm(), "", 140, 32f, 14);
			yes.name = "Skip";
			UIKit.DangerButton(yes);
			box.gameObject.SetActive(false);
		}

		#endregion
	}
}
