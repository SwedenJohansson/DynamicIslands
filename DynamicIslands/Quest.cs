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
	/// An island's quest (the quest editor, Island tab): a title, an introduction, steps done in order, a reward and a
	/// closing message. Stored with the island's settings (IslandFile.Props, "quest.*"). Steps point at things the
	/// island already has, by name:
	///   reach  - a trigger zone (its name)             read  - a note (its title)
	///   open   - a chest (its note title; empty = any)  kill  - creatures of a kind ("Warthog"), a number of them
	///   catch  - a catchable animal of a kind ("Llama")
	/// </summary>
	public class IslandQuest
	{
		public const string KeyTitle = "quest.title", KeyIntro = "quest.intro", KeySteps = "quest.steps", KeyReward = "quest.reward", KeyDone = "quest.done";
		public static readonly string[] Types = { "reach", "read", "open", "kill", "catch" };

		public class Step
		{
			public string Type = "reach", Target = "", Text = "";
			public int Count = 1;

			/// <summary>What the player is told to do: the builder's text, or one made from the step.</summary>
			public string Describe()
			{
				if (Text.Length > 0) return Text;
				switch (Type)
				{
					case "reach": return Target.Length > 0 ? "Go to " + Target : "Explore the island";
					case "read": return Target.Length > 0 ? "Read \"" + Target + "\"" : "Read a note";
					case "open": return Target.Length > 0 ? "Open \"" + Target + "\"" : "Open a chest";
					case "kill": return "Defeat " + (Count > 1 ? Count + " " : "a ") + (Target.Length > 0 ? Target.ToLowerInvariant() + (Count > 1 ? "s" : "") : "creature" + (Count > 1 ? "s" : ""));
					case "catch": return "Catch " + (Count > 1 ? Count + " " : "a ") + (Target.Length > 0 ? Target.ToLowerInvariant() + (Count > 1 ? "s" : "") : "animal" + (Count > 1 ? "s" : ""));
				}
				return Type;
			}
		}

		public string Title = "", Intro = "", Reward = "", Done = "";
		public List<Step> Steps = new List<Step>();

		public bool Exists { get { return Steps.Count > 0; } }

		static string Clean(string s) { return (s ?? "").Replace("|", "/").Replace("\n", " ").Trim(); }

		public static IslandQuest From(IDictionary<string, string> props)
		{
			var q = new IslandQuest
			{
				Title = ObjectProps.Get(props, KeyTitle), Intro = ObjectProps.Get(props, KeyIntro),
				Reward = ObjectProps.Get(props, KeyReward), Done = ObjectProps.Get(props, KeyDone)
			};
			foreach (string line in ObjectProps.Get(props, KeySteps).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] p = line.Split('|');
				if (p.Length < 1 || !Types.Contains(p[0])) continue;
				int count;
				q.Steps.Add(new Step { Type = p[0], Target = p.Length > 1 ? p[1] : "", Count = p.Length > 2 && int.TryParse(p[2], out count) ? Mathf.Clamp(count, 1, 99) : 1, Text = p.Length > 3 ? p[3] : "" });
			}
			return q;
		}

		/// <summary>Writes the quest into island settings (an empty quest removes the keys).</summary>
		public void To(IDictionary<string, string> props)
		{
			foreach (string k in new[] { KeyTitle, KeyIntro, KeySteps, KeyReward, KeyDone }) props.Remove(k);
			if (!Exists) return;
			if (Title.Trim().Length > 0) props[KeyTitle] = Title.Trim();
			if (Intro.Trim().Length > 0) props[KeyIntro] = Intro.Trim();
			if (Reward.Length > 0) props[KeyReward] = Reward;
			if (Done.Trim().Length > 0) props[KeyDone] = Done.Trim();
			props[KeySteps] = string.Join("\n", Steps.Select(s => s.Type + "|" + Clean(s.Target) + "|" + s.Count + "|" + Clean(s.Text)).ToArray());
		}

		public string ShownTitle { get { return Title.Trim().Length > 0 ? Title.Trim() : "Quest"; } }
	}

	/// <summary>
	/// Quests in a world. Every machine notices what its own player does (walks into a zone, reads a note, opens a
	/// chest); the host notices killed and caught animals. The step reached is kept in the island's state (saved with
	/// the world, sent to players who join) and sent to everyone (IslandNetMessage.QuestStep). A panel shows the quest
	/// while the player is near its island; when it's done, every player near the island gets the reward.
	/// </summary>
	public static class QuestTracker
	{
		/// <summary>State keys: the step reached (Yield), and progress on counted steps (Yield).</summary>
		public const int StepKey = 0x40000, ProgressKey = 0x40001;
		const float NearDistance = 150f;

		/// <summary>Raised on every machine when a quest moves on (tests listen): island id, new step (== steps count when done).</summary>
		public static event Action<int, int> Advanced;

		/// <summary>The last quest message shown (tests look at it).</summary>
		public static string LastMessage { get; private set; }

		/// <summary>The island's quest (also while it's unloaded here: a client may be at it while the host is far away).</summary>
		public static IslandQuest QuestOf(IslandWorldState.Entry e)
		{
			return e != null ? IslandQuest.From(IslandCache.PropsOf(e)) : new IslandQuest();
		}

		public static int StepOf(IslandWorldState.Entry e)
		{
			ObjectState s;
			return e != null && e.State.TryGetValue(StepKey, out s) ? s.Yield : 0;
		}

		static int ProgressOf(IslandWorldState.Entry e)
		{
			ObjectState s;
			return e != null && e.State.TryGetValue(ProgressKey, out s) ? s.Yield : 0;
		}

		static int Today { get { try { return WorldManager.DayCounter; } catch { return 0; } } }

		/// <summary>Something happened on an island that a quest step may be waiting for.</summary>
		public static void Event(IslandWorldState.Entry e, string type, string target, int amount = 1)
		{
			if (e == null) return;
			IslandQuest q = QuestOf(e);
			int step = StepOf(e);
			if (!q.Exists || step >= q.Steps.Count) return;
			IslandQuest.Step s = q.Steps[step];
			if (s.Type != type || (s.Target.Length > 0 && !string.Equals(s.Target.Trim(), (target ?? "").Trim(), StringComparison.OrdinalIgnoreCase))) return;
			int progress = ProgressOf(e) + amount;
			if (progress >= s.Count) Set(e, step + 1, 0, true);
			else Set(e, step, progress, true);
		}

		/// <summary>Records the quest's state here, tells the others (unless it came from them), and shows what changed.</summary>
		public static void Set(IslandWorldState.Entry e, int step, int progress, bool send)
		{
			int before = StepOf(e);
			if (step < before) return;
			e.State[StepKey] = new ObjectState { Active = true, Yield = step, Day = Today };
			if (progress > 0) e.State[ProgressKey] = new ObjectState { Active = true, Yield = progress, Day = Today };
			else e.State.Remove(ProgressKey);
			if (send) IslandNetwork.SendQuest(e.Id, step, progress);
			if (step == before) return;
			IslandQuest q = QuestOf(e);
			if (step >= q.Steps.Count) Completed(e, q);
			else Show(q.ShownTitle, "Next: " + q.Steps[step].Describe());
			if (Advanced != null) try { Advanced(e.Id, step); } catch { }
		}

		/// <summary>From the network (another player moved the quest on).</summary>
		public static void Apply(int islandId, int step, int progress)
		{
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == islandId);
			if (e != null) Set(e, step, progress, false);
		}

		static void Completed(IslandWorldState.Entry e, IslandQuest q)
		{
			Show("Quest complete: " + q.ShownTitle, q.Done);
			if (!Near(e) || q.Reward.Length == 0) return;
			List<string> given = TriggerZone.Give(ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, q.Reward } }));
			Debug.Log("[CUSTOM ISLANDS] Quest reward: " + string.Join(", ", given.ToArray()));
		}

		static void Show(string title, string text)
		{
			LastMessage = title + (text.Length > 0 ? " | " + text : "");
			IslandInfo.Show(title, "", text);
			Debug.Log("[CUSTOM ISLANDS] " + LastMessage);
		}

		static bool Near(IslandWorldState.Entry e)
		{
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null || e.Root == null) return false;
			IslandInfoTag tag = e.Root.GetComponent<IslandInfoTag>();
			Vector3 c = e.Root.transform.position + (tag != null ? tag.LocalCentre : Vector3.zero);
			float r = tag != null ? tag.Radius : 150f;
			return new Vector2(c.x - p.transform.position.x, c.z - p.transform.position.z).magnitude < r + NearDistance;
		}

		#region The quest panel

		static RectTransform panel;
		static Text titleText, stepsText;
		static float nextHud;
		static readonly HashSet<int> introduced = new HashSet<int>();

		/// <summary>Every frame from the mod: the panel for the quest of the island the player is at (and its introduction once).</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextHud) return;
			nextHud = Time.unscaledTime + 0.5f;
			if (!LoadSceneManager.IsGameSceneLoaded) { introduced.Clear(); if (panel != null) panel.gameObject.SetActive(false); return; }
			IslandWorldState.Entry at = IslandWorldState.Islands.FirstOrDefault(e => e.Root != null && QuestOf(e).Exists && Near(e));
			if (at == null) { if (panel != null) panel.gameObject.SetActive(false); return; }
			IslandQuest q = QuestOf(at);
			int step = StepOf(at);
			if (introduced.Add(at.Id) && step == 0 && q.Intro.Length > 0) Show(q.ShownTitle, q.Intro);
			if (panel == null) Build();
			panel.gameObject.SetActive(true);
			titleText.text = q.ShownTitle + (step >= q.Steps.Count ? "  <color=#8fdc8f>\u221A done</color>" : "");
			var lines = new List<string>();
			for (int i = 0; i < q.Steps.Count; i++)
			{
				string d = q.Steps[i].Describe();
				if (i < step) lines.Add("<color=#8fdc8f>\u221A</color> <color=#9aa7b4>" + d + "</color>");
				else if (i == step)
				{
					int progress = ProgressOf(at);
					lines.Add("<color=#ffc766>\u25BA</color> " + d + (q.Steps[i].Count > 1 ? " (" + progress + "/" + q.Steps[i].Count + ")" : ""));
				}
				else lines.Add("<color=#6b7580>\u2022 ?</color>");
			}
			stepsText.text = string.Join("\n", lines.ToArray());
		}

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_Quest", 380);
			UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
			panel = UIKit.Rect("Quest", canvas.transform);
			UIKit.Anchor(panel, new Vector2(1f, 1f), new Vector2(-20, -160), new Vector2(300, 0));
			UIKit.Background(panel.gameObject, new Color(0.05f, 0.06f, 0.08f, 0.6f), 8);
			UIKit.Vertical(panel.gameObject, 4f, new RectOffset(12, 12, 8, 10), true);
			titleText = UIKit.Label(panel, "", 16, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
			stepsText = UIKit.Label(panel, "", 13, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Steps");
			stepsText.lineSpacing = 1.15f;
			foreach (Graphic g in panel.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
		}

		#endregion
	}
}
