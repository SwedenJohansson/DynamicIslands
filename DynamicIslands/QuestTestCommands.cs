using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The QuestTest console command (gap idea 9, 11-04): an island maker tries their quest in a world without playing it all
	/// through - the quests of the island nearest the player and their steps, a jump to any step (back too), a signal sent, a
	/// story item given, the island set back to new. Changes are the host's (players who joined see the same).
	/// </summary>
	public static class QuestTestCommands
	{
		const string Usage = "QuestTest [show] | step <n|done> [quest] | skip [quest] | signal <name> | give <story item> [count] | reset";

		[ConsoleCommand(name: "QuestTest", docs: "Try a quest (the custom island nearest you): QuestTest = its quests and steps, the current one marked; QuestTest step <n|done> [quest] = jump to step n of the main quest (or quest 2, 3...), back too (host); QuestTest skip [quest] = skip the current step as the quest panel's Stuck? button does: what it gives is given, then the next step (host); QuestTest signal <name> = send a signal on it (host); QuestTest give <story item> [count] = the crew gets a story item (host); QuestTest reset = the island as new: quests, chests, pickups, signals (host; it is made again)")]
		public static void QuestTestCommand(string[] args)
		{
			if (!LoadSceneManager.IsGameSceneLoaded) { DynamicIslands.Notify("QuestTest works in a world, near a custom island", true); return; }
			string what = args != null && args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "show";
			if (what != "show" && what != "step" && what != "skip" && what != "signal" && what != "give" && what != "reset") { DynamicIslands.Notify(Usage, true); return; }
			if (what != "show" && !Raft_Network.IsHost) { DynamicIslands.Notify("Only the host changes an island's quest (QuestTest " + what + ")", true); return; }
			IslandWorldState.Entry e = Nearest(what == "show" || what == "step" || what == "skip");
			if (e == null) { DynamicIslands.Notify(what == "show" || what == "step" || what == "skip" ? "No custom island with a quest is loaded near you" : "No custom island is loaded near you", true); return; }
			switch (what)
			{
				case "show": DynamicIslands.Notify(Describe(e), false, 8); break;
				case "step": Step(e, args); break;
				case "skip": SkipStep(e, args); break;
				case "signal":
					string name = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()).Trim() : "";
					if (name.Length == 0) { DynamicIslands.Notify("QuestTest signal <name>", true); return; }
					Behaviours.SendSignal(e, name);
					IslandWorldState.Save();
					DynamicIslands.Notify("Signal '" + name + "' sent on '" + e.HostName + "'");
					break;
				case "give":
					int count = 1;
					if (args.Length < 2 || (args.Length > 2 && (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1))) { DynamicIslands.Notify("QuestTest give <story item> [count]", true); return; }
					string id = StoryItems.IdOf(args[1]);
					if (StoryItems.Find(id) == null) DynamicIslands.Notify("'" + id + "' isn't a story item of any island here: given all the same (a quest may still ask for it)", true);
					StoryBook.Give(id, count, e);
					break;
				case "reset": Reset(e); break;
			}
		}

		/// <summary>The custom island loaded nearest this player (with a quest, when asked).</summary>
		internal static IslandWorldState.Entry Nearest(bool withQuest)
		{
			Network_Player me = RAPI.GetLocalPlayer();
			Vector3 at = me != null ? me.transform.position : (CustomIslandSpawner.RaftPosition ?? Vector3.zero);
			return IslandWorldState.Islands
				.Where(x => x.Root != null && (!withQuest || QuestTracker.QuestsOf(x) > 0))
				.OrderBy(x => Mathf.Max(0f, new Vector2(x.Position.x - at.x, x.Position.z - at.z).magnitude - CustomIslandSpawner.LandRadius(x.Name)))
				.FirstOrDefault();
		}

		/// <summary>The island's quests, each step on its own line, the current one marked "&gt;".</summary>
		internal static string Describe(IslandWorldState.Entry e)
		{
			var lines = new List<string> { "'" + e.HostName + "':" };
			int quests = QuestTracker.QuestsOf(e);
			for (int n = 0; n < quests; n++)
			{
				IslandQuest q = QuestTracker.QuestOf(e, n);
				int step = QuestTracker.StepOf(e, n);
				lines.Add("Quest " + (n + 1) + " '" + q.ShownTitle + "': " + (step >= q.Steps.Count ? "done" : "step " + (step + 1) + " of " + q.Steps.Count + " (" + QuestTracker.ProgressOf(e, n) + " done)"));
				for (int s = 0; s < q.Steps.Count; s++) lines.Add((s == step ? " > " : "   ") + (s + 1) + ". " + q.Steps[s].Describe());
			}
			foreach (string l in lines) Debug.Log("[CUSTOM ISLANDS] [QuestTest] " + l);
			return string.Join("\n", lines.ToArray());
		}

		static void Step(IslandWorldState.Entry e, string[] args)
		{
			int quest = 1, step;
			string to = args.Length > 1 ? args[1].Trim().ToLowerInvariant() : "";
			if (args.Length > 2 && (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out quest) || quest < 1 || quest > QuestTracker.QuestsOf(e)))
			{ DynamicIslands.Notify("'" + e.HostName + "' has " + QuestTracker.QuestsOf(e) + " quest(s): QuestTest step <n|done> [1-" + QuestTracker.QuestsOf(e) + "]", true); return; }
			IslandQuest q = QuestTracker.QuestOf(e, quest - 1);
			if (to == "done") step = q.Steps.Count + 1;
			else if (!int.TryParse(to, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step < 1 || step > q.Steps.Count + 1)
			{ DynamicIslands.Notify("QuestTest step <1-" + q.Steps.Count + " or done> [quest]", true); return; }
			if (!QuestTracker.JumpTo(e, quest - 1, step - 1)) { DynamicIslands.Notify("Quest " + quest + " of '" + e.HostName + "' didn't change", true); return; }
			IslandWorldState.Save();
			int now = QuestTracker.StepOf(e, quest - 1);
			DynamicIslands.Notify("Quest " + quest + " of '" + e.HostName + "': " + (now >= q.Steps.Count ? "done" : "step " + (now + 1) + ": " + q.Steps[now].Describe()));
		}

		/// <summary>QuestTest skip [quest]: the current step skipped (QuestSkip.Skip - the quest panel's button without its box).</summary>
		static void SkipStep(IslandWorldState.Entry e, string[] args)
		{
			int quest = 1;
			if (args.Length > 1 && (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out quest) || quest < 1 || quest > QuestTracker.QuestsOf(e)))
			{ DynamicIslands.Notify("QuestTest skip [1-" + QuestTracker.QuestsOf(e) + "]", true); return; }
			if (!QuestSkip.Skip(e, quest - 1)) { DynamicIslands.Notify("Quest " + quest + " of '" + e.HostName + "' has no step to skip", true); return; }
			IslandQuest q = QuestTracker.QuestOf(e, quest - 1);
			int now = QuestTracker.StepOf(e, quest - 1);
			DynamicIslands.Notify("Quest " + quest + " of '" + e.HostName + "': skipped, " + (now >= q.Steps.Count ? "done" : "step " + (now + 1) + ": " + q.Steps[now].Describe()));
		}

		/// <summary>Host: the island as new - its state (quests, chests, pickups, signals, used things) gone, made again here
		/// (it loads again at once: streaming) and on every player. Rewards a player had stay had (QuestRewards).</summary>
		internal static void Reset(IslandWorldState.Entry e)
		{
			if (!Raft_Network.IsHost || e == null) return;
			KeepOnTop(e);
			if (e.Root != null)
			{
				// (loose items dropped on it wait in the air until it is back: RK2; its state is not captured - it goes)
				try { ItemHold.HoldOn(e); } catch (Exception x) { Debug.LogWarning("[CUSTOM ISLANDS] Holding dropped items failed: " + x.Message); }
				IslandSpawner.Despawn(e.Root);
				e.Root = null;
			}
			e.State.Clear();
			e.Regrown.Clear();
			IslandNetwork.SendReset(e);
			IslandWorldState.Save();
			DynamicIslands.Notify("'" + e.HostName + "' is set back to new: it is made again (you are put back on it; rewards had stay had)", false, 6);
		}

		/// <summary>An island set back to new (reset, here or by the host): when this machine's player is on it, they are put
		/// back on its ground once it is made again (it goes away under them for a moment - not left in the sea under it).</summary>
		internal static void KeepOnTop(IslandWorldState.Entry e)
		{
			Network_Player me = RAPI.GetLocalPlayer();
			if (e == null || e.Root == null || me == null || DynamicIslands.instance == null) return;
			Vector3 at = me.transform.position;
			if (new Vector2(at.x - e.Position.x, at.z - e.Position.z).magnitude > CustomIslandSpawner.LandRadius(e.Name) + 10f || at.y < -2f) return;
			DynamicIslands.instance.StartCoroutine(BackOnTop(e, me, at));
		}

		static System.Collections.IEnumerator BackOnTop(IslandWorldState.Entry e, Network_Player me, Vector3 at)
		{
			yield return null; // (started before it goes: gone by now)
			for (float t = 0f; t < 60f && (e.Root == null || e.Loading); t += 0.25f) yield return new WaitForSeconds(0.25f);
			yield return new WaitForSeconds(0.5f);
			if (e.Root == null || me == null) yield break;
			// (the highest ground there that is not the player)
			RaycastHit[] hits = Physics.RaycastAll(new Vector3(at.x, at.y + 300f, at.z), Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore);
			RaycastHit[] ground = hits.Where(h => h.collider != null && !h.collider.transform.IsChildOf(me.transform)).OrderByDescending(h => h.point.y).Take(1).ToArray();
			if (ground.Length == 0 || ground[0].point.y < -1f || me.transform.position.y > ground[0].point.y - 0.5f) yield break;
			PlayerMove.To(me, ground[0].point + Vector3.up * PlayerMove.StandHeight(me));
			Debug.Log("[CUSTOM ISLANDS] [QuestTest] put back on '" + e.HostName + "' after its reset");
		}
	}
}
