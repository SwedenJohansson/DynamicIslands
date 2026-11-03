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
	/// RK3 + RK4, two players (mprk34.ps1): a client's once note the host refuses isn't marked used and gives back the items
	/// its checks took (RK3); a client's second use while the host's signal that bars it is on its way does nothing (RK4).
	/// </summary>
	public static partial class DevTests
	{
		const string Rk34Island = "cirk34";

		[ConsoleCommand(name: "CIRk34", docs: "Dev, two-player test (mprk34.ps1): CIRk34 setup (host) = island 'cirk34' near the raft with a once note that needs the crew's story key and takes 2 planks, and a lever that takes 1 plank, gives 1 scrap and sends the signal barring it after 3 s; CIRk34 away on|off (host) = the host's copy of the island unloaded (the host refuses a player's event there); CIRk34 read (player 2) = reads the note; CIRk34 double (player 2) = uses the lever twice 1 s apart; CIRk34 status (either) = one line 'RK34 status <role>: ...'; CIRk34 end (host)")]
		public static void Rk34Command(string[] args) { StartTest(Rk34Routine(args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status", args != null && args.Length > 1 ? args[1].ToLowerInvariant() : "")); }

		static IEnumerator Rk34Routine(string part, string arg)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || RAPI.GetLocalPlayer() == null) { Fail("rk34: run in a world"); yield break; }
			Network_Player p = RAPI.GetLocalPlayer();
			if (part == "setup" || part == "end")
			{
				if (!Raft_Network.IsHost) { Fail("rk34 " + part + ": on the host"); yield break; }
				CustomIslandSpawner.TestRaftFarAway = false;
				IslandWorldState.Remove(Rk34Island);
				StoryBook.Take("rk-key", StoryBook.Count("rk-key"));
				if (part == "end")
				{
					try { if (System.IO.File.Exists(IslandSpawner.PathFor(Rk34Island))) System.IO.File.Delete(IslandSpawner.PathFor(Rk34Island)); } catch { }
					IslandCache.Forget();
					Log("PASS: rk34 end");
					yield break;
				}
				PlayerTrust.ForTests = true;
				Rk34Build();
				Vector3? spot = ScSpot(Rk34Island, 390f);
				if (!spot.HasValue) { Fail("rk34: no open sea near the raft"); yield break; }
				yield return DynamicIslands.instance.SpawnIslandFile(Rk34Island, spot.Value, true);
				IslandWorldState.Entry made = IslandWorldState.Islands.LastOrDefault(i => i.HostName == Rk34Island);
				if (made == null || made.Root == null) { Fail("rk34: the island did not spawn"); yield break; }
				StoryBook.Give("rk-key", 1);
				Log("PASS: rk34 setup (island " + made.Id + ")");
				yield break;
			}
			if (part == "away")
			{
				if (!Raft_Network.IsHost) { Fail("rk34 away: on the host"); yield break; }
				CustomIslandSpawner.TestRaftFarAway = arg != "off";
				IslandWorldState.Entry h = IslandWorldState.Islands.FirstOrDefault(i => i.HostName == Rk34Island);
				for (float t = 0; h != null && (h.Root != null) == CustomIslandSpawner.TestRaftFarAway && t < 30f; t += 0.25f) yield return new WaitForSeconds(0.25f);
				bool done = h != null && (h.Root == null) == CustomIslandSpawner.TestRaftFarAway;
				if (done) Log("PASS: rk34 away " + (CustomIslandSpawner.TestRaftFarAway ? "on (unloaded on the host)" : "off (loaded on the host)")); else Fail("rk34 away: the host's copy did not " + (CustomIslandSpawner.TestRaftFarAway ? "unload" : "load"));
				yield break;
			}
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(i => i.HostName == Rk34Island);
			if (e == null) { Fail("rk34: no island '" + Rk34Island + "' here"); yield break; }
			IslandObjectRef note = e.Root != null ? ScObjOf(e, "rk-note") : null, lever = e.Root != null ? ScObjOf(e, "rk-lever") : null;
			if (part == "read")
			{
				if (note == null) { Fail("rk34 read: the note isn't loaded here"); yield break; }
				PutPlayerNear(note.transform);
				Behaviours.Fire(e, note.Index, "read", true);
				Log("read the note (" + GupRole + "), waiting for the host");
				Log("waiting for the host's answer: " + Behaviours.AwaitingForTest(e, note.Index, "read"));
				yield return new WaitForSeconds(3f);
				Log("answered: " + !Behaviours.AwaitingForTest(e, note.Index, "read"));
			}
			else if (part == "double")
			{
				if (lever == null) { Fail("rk34 double: the lever isn't loaded here"); yield break; }
				PutPlayerNear(lever.transform);
				Behaviours.Fire(e, lever.Index, "use", true);
				yield return new WaitForSeconds(1f);
				Log("barred until the signal comes: " + Behaviours.SignalLockedForTest(e, lever.Index, "use"));
				Behaviours.Fire(e, lever.Index, "use", true);
				yield return new WaitForSeconds(5f);
			}
			else if (part != "status") { Fail("rk34: unknown part " + part); yield break; }
			int noteIndex = note != null ? note.Index : -1;
			Log("RK34 status " + GupRole + ": planks=" + p.Inventory.GetItemCount("Plank") + " scrap=" + p.Inventory.GetItemCount("Scrap") +
				" noteUsed=" + GsYes(noteIndex >= 0 && e.State.ContainsKey(Behaviours.DoneBase + noteIndex)) + " signal=" + GsYes(Behaviours.HasSignal(e, "rk-done")) +
				" key=" + StoryBook.Count("rk-key") + " loaded=" + GsYes(e.Root != null) + " message=" + (Behaviours.LastMessage ?? "").Replace(' ', '_'));
			Log("PASS: rk34 " + part + " " + GupRole);
		}

		static void Rk34Build()
		{
			IslandFile f = SampleIsland();
			f.Name = Rk34Island;
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			float step = f.TerrainSize.x / (f.HeightmapResolution - 1);
			Func<float, float, Vector3> g = (dx, dz) =>
			{
				float x = c.x + dx, z = c.y + dz;
				return new Vector3(x, f.Heights[Mathf.RoundToInt(z / step), Mathf.RoundToInt(x / step)] * f.TerrainSize.y, z);
			};
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 25f);
			// RK3: a once note with the crew's story check (the host makes it again) and a take of the player's own planks
			f.Objects.Add(new IslandObject { Name = "Note_Paper", Position = g(-3f, 0f), Props = P(BehaviourProps.Name, "rk-note", ObjectProps.NoteTitle, "Toll note", ObjectProps.NoteText, "Pay two planks, show the key.",
				BehaviourProps.CheckKey("read"), "has|story:rk-key|1\ntake|Plank|2", BehaviourProps.ElseKey("read"), "message||Refused.",
				BehaviourProps.EventKey("read"), "give||Scrap*5\nmessage||Note paid.") });
			// RK4: a lever its own signal bars, the signal sent after a wait
			f.Objects.Add(new IslandObject { Name = "Log", Position = g(3f, 0f), Props = P(BehaviourProps.Name, "rk-lever", BehaviourProps.Use, "Pull",
				BehaviourProps.CheckKey("use"), "take|Plank|1\n!signal|rk-done", BehaviourProps.ElseKey("use"), "message||Already pulled.",
				BehaviourProps.EventKey("use"), "give||Scrap*1\nmessage||Pulled.\nwait||3\nsignal||rk-done") });
			f.Props[StoryItems.Key] = StoryItems.Text(new List<StoryItemDef> { GstDef("rk-key", "Toll key", "Vasagatan_BlueKey", "A key for the toll.") });
			f.Save(IslandSpawner.PathFor(Rk34Island));
		}
	}
}
