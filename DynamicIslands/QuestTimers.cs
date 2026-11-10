using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Timed challenges (ROADMAP QE9): a quest step with a time limit (the island setting quest.timers, "step=seconds",
	/// 1-based steps). The host runs the clock while a player is at the island (it waits while nobody is); run out, the
	/// step starts again (QuestTracker.JumpTo: its progress and the work done early for it are set back, on every player)
	/// and the clock starts over. The seconds left are kept in the island's state (0x7C00000 + quest), saved with the world.
	/// Every player sees a countdown near the island: the host sends it (Moment "timer", Ids {-1, island, quest}, Index =
	/// step, Count = seconds left, Data = run / wait / fail / stop) every 2 s and on each change, so a player who joins
	/// catches up; an older version's TriggerSounds finds no island -1 and does nothing.
	/// </summary>
	public static class QuestTimers
	{
		public const int KeyBase = 0x7C00000;
		public const int MaxSeconds = 3600;
		const float SendEvery = 2f;

		#region The setting

		/// <summary>quest.timers onto the steps read (bad or out-of-range entries are skipped).</summary>
		public static void Read(string text, List<IslandQuest.Step> steps)
		{
			foreach (string part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				int eq = part.IndexOf('=');
				int step, secs;
				if (eq <= 0 || !int.TryParse(part.Substring(0, eq).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out step)
					|| !int.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out secs)) continue;
				if (step >= 1 && step <= steps.Count) steps[step - 1].Seconds = Mathf.Clamp(secs, 0, MaxSeconds);
			}
		}

		public static string Write(List<IslandQuest.Step> steps)
		{
			var parts = new List<string>();
			for (int i = 0; i < steps.Count; i++)
				if (steps[i].Seconds > 0) parts.Add((i + 1).ToString(CultureInfo.InvariantCulture) + "=" + Mathf.Min(steps[i].Seconds, MaxSeconds).ToString(CultureInfo.InvariantCulture));
			return string.Join(";", parts.ToArray());
		}

		/// <summary>Steps a timer can't set back cleanly (what was done stays done: an animal killed, an item handed over...).</summary>
		public static bool Unsafe(string type)
		{
			return type == "kill" || type == "catch" || type == "collect" || type == "pages" || type == "blueprint" || type == "open" || type == "read"
				|| type == QuestSteps.Signal || type == QuestSteps.Bring || IslandQuest.Counted(type);
		}

		public static string Clock(int seconds)
		{
			seconds = Mathf.Max(0, seconds);
			return (seconds / 60).ToString(CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
		}

		#endregion

		#region The clock (host) and what every machine shows

		public const string Run = "run", Wait = "wait", Fail = "fail", Stop = "stop";

		/// <summary>A timer as this machine knows it: the host's own, or the last the host sent.</summary>
		public class Shown
		{
			public int Island, Quest, Step;
			public float Left; // seconds left when At was
			public float At;
			public string Mode = Stop;
			public float Full;
			public float LeftNow { get { return Mode == Run ? Mathf.Max(0f, Left - (Time.time - At)) : Left; } }
		}

		static readonly Dictionary<long, Shown> timers = new Dictionary<long, Shown>();
		static readonly Dictionary<long, float> lastSent = new Dictionary<long, float>();
		static float lastTick = -1f;

		static long Id(int island, int n) { return ((long)island << 8) | (uint)n; }

		/// <summary>Tests: the timer of an island's quest (null = none running or shown).</summary>
		public static Shown Of(int island, int n) { Shown s; return timers.TryGetValue(Id(island, n), out s) ? s : null; }

		/// <summary>How many times a timed step ran out here (tests).</summary>
		public static int Fails;

		/// <summary>Every half second (QuestTracker.Tick): the host's clocks, then the countdown on the screen.</summary>
		public static void Tick()
		{
			if (!LoadSceneManager.IsGameSceneLoaded) { timers.Clear(); lastSent.Clear(); lastTick = -1f; Hide(); return; }
			float dt = lastTick < 0f ? 0f : Mathf.Clamp(Time.time - lastTick, 0f, 2f);
			lastTick = Time.time;
			try { if (Raft_Network.IsHost) HostTick(dt); }
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Quest timers: " + ex.Message); }
			UpdateHud();
		}

		static void HostTick(float dt)
		{
			var seen = new HashSet<long>();
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.ToList())
			{
				if (e.Root == null) continue;
				int quests = QuestTracker.QuestsOf(e);
				for (int n = 0; n < quests; n++)
				{
					IslandQuest q = QuestTracker.QuestOf(e, n);
					int step = QuestTracker.StepOf(e, n);
					if (!q.Exists || step >= q.Steps.Count || q.Steps[step].Seconds <= 0) continue;
					long id = Id(e.Id, n);
					seen.Add(id);
					float full = q.Steps[step].Seconds;
					Shown t;
					if (!timers.TryGetValue(id, out t) || t.Step != step)
					{
						// (a new timed step, or the world loaded: the seconds saved for this step, else the full time)
						ObjectState saved;
						float left = e.State.TryGetValue(KeyBase + n, out saved) && saved.Day == step && saved.Yield > 0 ? Mathf.Min(saved.Yield, full) : full;
						t = new Shown { Island = e.Id, Quest = n, Step = step, Left = left, At = Time.time, Full = full, Mode = Wait };
						timers[id] = t;
						lastSent.Remove(id);
					}
					string mode = QuestTracker.AnyPlayerNear(e) ? Run : Wait;
					float now = t.LeftNow;
					if (t.Mode == Run) now = Mathf.Max(0f, t.Left - dt); // (the host counts its ticks: game time, paused with the game)
					t.Left = now; t.At = Time.time; t.Full = full;
					bool changed = t.Mode != mode;
					t.Mode = mode;
					if (now <= 0f && mode == Run)
					{
						// Time's up: the step starts again, for everyone, and so does its clock
						Fails++;
						Debug.Log("[CUSTOM ISLANDS] Quest " + (n + 1) + " of '" + e.HostName + "': step " + (step + 1) + " ran out of time");
						Send(t, Fail);
						QuestTracker.JumpTo(e, n, step);
						if (QuestTracker.Near(e, 150f)) Announce(q.Steps[step]);
						t.Left = full; t.At = Time.time;
						changed = true;
					}
					e.State[KeyBase + n] = new ObjectState { Active = true, Yield = Mathf.CeilToInt(t.Left), Day = step };
					float last;
					if (changed || !lastSent.TryGetValue(id, out last) || Time.time - last >= SendEvery) { Send(t, t.Mode); lastSent[id] = Time.time; }
				}
			}
			// Steps no longer timed (done, set back to an untimed step, the island gone): their clocks stop for everyone
			foreach (long id in timers.Keys.Where(k => !seen.Contains(k)).ToList())
			{
				Shown t = timers[id];
				timers.Remove(id);
				lastSent.Remove(id);
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == t.Island);
				if (e != null && e.Root != null) e.State.Remove(KeyBase + t.Quest); // (an island unloaded keeps its seconds left)
				t.Mode = Stop;
				Send(t, Stop);
			}
		}

		static void Send(Shown t, string mode)
		{
			IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Moment, Name = "timer", Ids = new[] { -1, t.Island, t.Quest }, Index = t.Step, Count = Mathf.CeilToInt(t.Left), Data = mode + "|" + Mathf.RoundToInt(t.Full).ToString(CultureInfo.InvariantCulture) });
		}

		static void Announce(IslandQuest.Step step)
		{
			IslandInfo.ShowMessage("Time's up! Try again: " + step.Describe());
		}

		/// <summary>A player: the host's timer (IslandNetwork, Moment "timer").</summary>
		public static void OnMessage(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost || msg.Ids == null || msg.Ids.Length < 3) return;
			long id = Id(msg.Ids[1], msg.Ids[2]);
			string[] d = (msg.Data ?? "").Split('|');
			string mode = d[0];
			float full;
			if (d.Length < 2 || !float.TryParse(d[1], NumberStyles.Float, CultureInfo.InvariantCulture, out full)) full = msg.Count;
			if (mode == Stop) { timers.Remove(id); return; }
			if (mode == Fail)
			{
				Fails++;
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == msg.Ids[1]);
				IslandQuest q = e != null ? QuestTracker.QuestOf(e, msg.Ids[2]) : null;
				if (q != null && msg.Index >= 0 && msg.Index < q.Steps.Count && QuestTracker.Near(e, 150f)) Announce(q.Steps[msg.Index]);
				mode = Run;
				msg.Count = Mathf.RoundToInt(full);
			}
			timers[id] = new Shown { Island = msg.Ids[1], Quest = msg.Ids[2], Step = msg.Index, Left = msg.Count, At = Time.time, Mode = mode, Full = full };
		}

		/// <summary>A world received (a player joining): the last world's timers go.</summary>
		public static void Reset() { timers.Clear(); lastSent.Clear(); lastTick = -1f; Hide(); }

		#endregion

		#region The countdown

		static RectTransform hud;
		static Text hudText;
		static Canvas canvas;

		/// <summary>Tests: the countdown's text ("" = hidden).</summary>
		public static string HudText { get { return hud != null && hud.gameObject.activeSelf && hudText != null ? hudText.text : ""; } }

		static void UpdateHud()
		{
			// The timer of the island this player is at (the first one running)
			Shown show = null;
			IslandQuest.Step step = null;
			foreach (Shown t in timers.Values)
			{
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == t.Island);
				if (e == null || e.Root == null || !QuestTracker.Near(e, 150f)) continue;
				IslandQuest q = QuestTracker.QuestOf(e, t.Quest);
				if (t.Step < 0 || t.Step >= q.Steps.Count || QuestTracker.StepOf(e, t.Quest) != t.Step) continue; // (the step was done here already)
				show = t; step = q.Steps[t.Step];
				break;
			}
			if (show == null) { Hide(); return; }
			if (hud == null) Build();
			int left = Mathf.CeilToInt(show.LeftNow);
			hudText.text = "Time left " + Clock(left) + (show.Mode == Wait ? " (waiting)" : "") + "  -  " + step.Describe();
			hudText.color = left <= 10 && show.Mode == Run ? UIKit.Danger : UIKit.Accent;
			hud.gameObject.SetActive(true);
		}

		static void Hide() { if (hud != null) hud.gameObject.SetActive(false); }

		static void Build()
		{
			canvas = UIKit.CreateCanvas("CustomIslands_QuestTimer", 381);
			UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
			hud = UIKit.Rect("Timer", canvas.transform);
			UIKit.Anchor(hud, new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(460, 34));
			UIKit.Surface(hud);
			UIKit.Horizontal(hud.gameObject, 4f, new RectOffset(12, 12, 4, 4));
			hudText = UIKit.Label(hud, "", 16, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, "Text");
			UIKit.Fit(hudText, 10);
			hudText.raycastTarget = false;
		}

		#endregion
	}
}
