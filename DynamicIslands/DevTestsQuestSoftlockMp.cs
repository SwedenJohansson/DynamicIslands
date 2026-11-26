using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Quest softlock audit 11-23, two players (qslmp.ps1): the fixes of rows 8, M3/M4, M5 and M8 played for real - player 2
	/// sets off a take zone, a chest and a story pickup while the host has the island not loaded (refused: the card back,
	/// all three ready again), a lever and a plain zone (queued, kept over the host's save and reload), and the host skips
	/// read/reach/talk/open steps there (queued, done when it loads).
	/// </summary>
	public static partial class DevTests
	{
		const string QslMpIsland = "ciqslmp";
		static readonly string[] QslMpItems = { "qmcard", "qmlever", "qmplain", "qmread", "qmzone", "qmtalk", "qmcrane", "qmopen" };

		[ConsoleCommand(name: "CIQslMp", docs: "Dev, two-player test (qslmp.ps1, TEST_CATALOGUE QSL4/7/8/10): CIQslMp setup (host) = island 'ciqslmp' with a zone 'qmtake' that takes the crew's story card, a chest 'QM chest' that takes it, a story pickup 'qm-crane' that needs it, a lever 'qm-lever' and a zone 'qmplain' giving story items, and a quest read 'QM letter' / reach 'qmgate' / talk 'QM Pat'; the crew gets 1 card; CIQslMp away on|off (host: the host's copy unloads / loads); CIQslMp pickup (picks up 'qm-crane' as a player pressing E, with its claim); CIQslMp skip (host: skips the quest's next 4 steps: read, reach, talk, open 'QM box'); CIQslMp status (either: logs QSLMP status ...); CIQslMp end (host)")]
		public static void QslMpCommand(string[] args) { StartTest(QslMpRoutine(args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status", args != null && args.Length > 1 ? args[1].ToLowerInvariant() : "")); }

		static IEnumerator QslMpRoutine(string part, string arg)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || RAPI.GetLocalPlayer() == null) { Fail("qslmp: run in a world"); yield break; }
			if (part == "setup" || part == "end")
			{
				if (!Raft_Network.IsHost) { Fail("qslmp " + part + ": on the host"); yield break; }
				CustomIslandSpawner.TestRaftFarAway = CustomIslandSpawner.TestPlayersFarAway = false;
				foreach (string it in QslMpItems) { int c = StoryBook.Count(it); if (c > 0) StoryBook.Take(it, c); }
				if (part == "end")
				{
					IslandWorldState.RemoveIds(IslandWorldState.Islands.Where(x => x.HostName == QslMpIsland).Select(x => x.Id).ToList(), true);
					Log("PASS: qslmp end");
					yield break;
				}
				PlayerTrust.ForTests = true;
				yield return ScMpSpawn(QslMpIsland, "Softlock Isle", QslMpFill);
				IslandWorldState.Entry made = IslandWorldState.Islands.LastOrDefault(i => i.HostName == QslMpIsland);
				if (made == null || made.Root == null) { Fail("qslmp: the island did not spawn"); yield break; }
				StoryBook.Give("qmcard", 1);
				Log("PASS: qslmp setup (island " + made.Id + ", card " + StoryBook.Count("qmcard") + ")");
				yield break;
			}
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(i => i.HostName == QslMpIsland);
			if (e == null) { Fail("qslmp: no island '" + QslMpIsland + "' here"); yield break; }
			if (part == "away")
			{
				// (player 2 stands on it: PlayerHold would keep the host's copy - as CIRk34 away)
				if (!Raft_Network.IsHost) { Fail("qslmp away: on the host"); yield break; }
				CustomIslandSpawner.TestRaftFarAway = arg != "off";
				CustomIslandSpawner.TestPlayersFarAway = CustomIslandSpawner.TestRaftFarAway;
				for (float t = 0; (e.Root != null) == CustomIslandSpawner.TestRaftFarAway && t < 40f; t += 0.25f) yield return new WaitForSeconds(0.25f);
				if ((e.Root == null) == CustomIslandSpawner.TestRaftFarAway) Log("PASS: qslmp away " + (CustomIslandSpawner.TestRaftFarAway ? "on (unloaded on the host)" : "off (loaded on the host)"));
				else Fail("qslmp away: the host's copy did not " + (CustomIslandSpawner.TestRaftFarAway ? "unload" : "load"));
				yield break;
			}
			if (part == "pickup")
			{
				IslandObjectRef r = ScObjOf(e, "qm-crane");
				UseInteract u = r != null ? r.GetComponentInChildren<UseInteract>(true) : null;
				System.Reflection.MethodInfo use = typeof(UseInteract).GetMethod("Use", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (u == null || use == null) { Fail("qslmp pickup: no pickup 'qm-crane' loaded here"); yield break; }
				PutPlayerNear(r.transform);
				use.Invoke(u, null);
				yield return new WaitForSeconds(1f);
				use.Invoke(u, null); // (a client's claim: the first press asks, the answer presses again)
				yield return new WaitForSeconds(4f);
				Log("picked up 'qm-crane' (" + GupRole + ")");
			}
			else if (part == "skip")
			{
				if (!Raft_Network.IsHost) { Fail("qslmp skip: on the host"); yield break; }
				int kept = QuestSkip.ReplayKept, from = QuestTracker.StepOf(e);
				bool a = QuestSkip.Skip(e, 0), b = QuestSkip.Skip(e, 0), c = QuestSkip.Skip(e, 0), d = QuestSkip.Skip(e, 0);
				Log("skipped: " + a + "/" + b + "/" + c + "/" + d + ", step " + (from + 1) + " -> " + (QuestTracker.StepOf(e) + 1) + ", kept for the island " + (QuestSkip.ReplayKept - kept) + ", loaded " + GsYes(e.Root != null));
			}
			else if (part != "status") { Fail("qslmp: unknown part " + part); yield break; }
			yield return new WaitForSeconds(0.5f);
			Log("QSLMP status " + GupRole + ": " + QslMpStatus(e));
			Log("PASS: qslmp " + part + " " + GupRole);
		}

		static void QslMpFill(IslandFile f)
		{
			f.Props[IslandQuest.KeyTitle] = "QM skip";
			f.Props[IslandQuest.KeySteps] = "read|QM letter|1|Read the letter\nreach|qmgate|1|Walk through the gate\n" + Islanders.QuestType + "|QM Pat|1|Talk to Pat\nopen|QM box|1|Open the box\nsignal|qm-end|1|Ring the bell";
			string card = "story:qmcard|1";
			// row 8 + M3: a once zone and a chest whose take check uses the crew's only card; M4: a story pickup that needs it
			f.Objects.Add(ScObj(ContentCatalog.TriggerZone, ScDry(f, new Vector2(-14, 0), 1), ObjectProps.ZoneId, "qmtake", ObjectProps.ZoneRadius, "3",
				BehaviourProps.CheckKey("enter"), "take|" + card, BehaviourProps.EventKey("enter"), "message||The card opens the gate"));
			f.Objects.Add(ScObj("Loot_Chest", ScDry(f, new Vector2(0, 14), 2), ObjectProps.NoteTitle, "QM chest", ObjectProps.LootItems, "Plank*2", ObjectProps.LootRefill, "0",
				BehaviourProps.CheckKey("open"), "take|" + card, BehaviourProps.EventKey("open"), "message||The card unlocks the chest"));
			f.Objects.Add(ScObj("QuestItemPickup_VP_CraneKey", ScDry(f, new Vector2(14, 14), 3), BehaviourProps.Name, "qm-crane", BehaviourProps.Use, "Pick up the key",
				BehaviourProps.CheckKey("use"), "has|" + card, BehaviourProps.EventKey("use"), "give||" + StoryItems.Ref("qmcrane") + "*1"));
			// M5: a lever and a plain once zone whose story items the host gives (queued while it isn't loaded there)
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(14, -14), 4), BehaviourProps.Name, "qm-lever", BehaviourProps.Use, "Pull",
				BehaviourProps.EventKey("use"), "give||" + StoryItems.Ref("qmlever") + "*1"));
			f.Objects.Add(ScObj(ContentCatalog.TriggerZone, ScDry(f, new Vector2(-14, -14), 5), ObjectProps.ZoneId, "qmplain", ObjectProps.ZoneRadius, "3",
				BehaviourProps.EventKey("enter"), "give||" + StoryItems.Ref("qmplain") + "*1"));
			// M8: the quest's note, gate and islander, each giving a story item
			f.Objects.Add(ScObj("Note_Paper", ScDry(f, new Vector2(0, -14), 6), ObjectProps.NoteTitle, "QM letter", ObjectProps.NoteText, "The key is under the stone.",
				BehaviourProps.EventKey("read"), "give||" + StoryItems.Ref("qmread") + "*1"));
			f.Objects.Add(ScObj(ContentCatalog.TriggerZone, ScDry(f, new Vector2(14, 0), 7), ObjectProps.ZoneId, "qmgate", ObjectProps.ZoneRadius, "3",
				BehaviourProps.EventKey("enter"), "give||" + StoryItems.Ref("qmzone") + "*1"));
			f.Objects.Add(ScObj(Islanders.ObjectName, ScDry(f, new Vector2(-14, 14), 8), BehaviourProps.Name, "QM Pat",
				BehaviourProps.EventKey(Islanders.TalkEvent), "give||" + StoryItems.Ref("qmtalk") + "*1"));
			// M8's limit closed: the quest's chest, opened for the host's player when the skip is done
			f.Objects.Add(ScObj("Loot_Chest", ScDry(f, new Vector2(-7, 7), 9), ObjectProps.NoteTitle, "QM box", ObjectProps.LootItems, "Plank*1", ObjectProps.LootRefill, "0",
				BehaviourProps.EventKey("open"), "give||" + StoryItems.Ref("qmopen") + "*1"));
		}

		/// <summary>The story items, the used marks of the take zone, chest, pickup and plain zone (from the island's file: also
		/// while it isn't loaded here), the queue and the host's counters.</summary>
		static string QslMpStatus(IslandWorldState.Entry e)
		{
			var s = new List<string>();
			foreach (string it in QslMpItems) s.Add(it.Substring(2) + "=" + StoryBook.Count(it));
			IslandFile f = null;
			try { f = IslandFile.Load(IslandSpawner.PathFor(e.Name)); } catch { }
			Func<Func<IslandObject, bool>, int> idx = m => f == null ? -1 : f.Objects.FindIndex(o => m(o));
			Func<IslandObject, string, string> zone = (o, id) => o.Name == ContentCatalog.TriggerZone && ObjectProps.Get(o.Props, ObjectProps.ZoneId) == id ? id : null;
			Func<int, string, string> used = (i, ev) => { int k = i < 0 ? -1 : Behaviours.RefusedKey(e, i, ev); return k < 0 ? "?" : GsYes(ContentState.IsUsed(e, k)); };
			s.Add("takeZone=" + used(idx(o => zone(o, "qmtake") != null), "enter"));
			s.Add("chest=" + used(idx(o => o.Name == "Loot_Chest" && ObjectProps.Get(o.Props, ObjectProps.NoteTitle) == "QM chest"), "open"));
			s.Add("pickup=" + used(idx(o => ObjectProps.Get(o.Props, BehaviourProps.Name) == "qm-crane"), "use"));
			s.Add("plainZone=" + used(idx(o => zone(o, "qmplain") != null), "enter"));
			s.Add("gateZone=" + used(idx(o => zone(o, "qmgate") != null), "enter"));
			s.Add("box=" + used(idx(o => o.Name == "Loot_Chest" && ObjectProps.Get(o.Props, ObjectProps.NoteTitle) == "QM box"), "open"));
			int note = idx(o => ObjectProps.Get(o.Props, ObjectProps.NoteTitle) == "QM letter");
			s.Add("noteRead=" + GsYes(note >= 0 && e.State.ContainsKey(Behaviours.DoneBase + note)));
			s.Add("queued=" + e.State.Keys.Count(Behaviours.IsQueuedKey));
			s.Add("step=" + (QuestTracker.StepOf(e) + 1));
			s.Add("loaded=" + GsYes(e.Root != null));
			s.Add("givenBack=" + Behaviours.GivenBackOnRefusal + " freed=" + Behaviours.FreedOnRefusal + " notLoadedDone=" + Behaviours.NotLoadedDone + " replayed=" + QuestSkip.Replayed + " kept=" + QuestSkip.ReplayKept);
			return string.Join(" ", s.ToArray());
		}
	}
}
