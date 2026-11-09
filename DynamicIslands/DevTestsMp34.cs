using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Two-player checks for release 3.4 (11-09): the minimap and quest panel with a second player, and the host's
	/// check of a client's story give (PlayerTrust).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIMp34", docs: "Dev, two players (11-09): CIMp34 mapset on|off (host: the world setting Island minimap) | mapcheck on|off (either: the setting and the map's availability follow it) | island (host: the map test island near the raft, host on it) | panel [dir] (player 2: on that island, the quest panel and the corner map show, picture) | end (host: island removed, setting off) | trustsend <near> <far> (player 2: story give for an item found on each island) | trustcheck <near> <far> (host: the far one refused, the near one taken). Run CITrust setup first for trust")]
		public static void Mp34Command(string[] args) { StartTest(Mp34Routine(args ?? new string[0])); }

		static IEnumerator Mp34Routine(string[] args)
		{
			string what = args.Length > 0 ? args[0] : "";
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) { Fail("mp34: run in a world"); yield break; }
			switch (what)
			{
				case "mapset":
				{
					if (!Raft_Network.IsHost) { Fail("mapset: as the host"); yield break; }
					bool on = args.Length > 1 && args[1] == "on";
					var cur = WorldOptions.Current.ToList();
					cur.Remove(WorldOptions.MinimapOn);
					if (on) cur.Add(WorldOptions.MinimapOn);
					WorldOptions.Set(cur);
					Log("PASS: mp34 mapset " + (on ? "on" : "off"));
					yield break;
				}
				case "mapcheck":
				{
					bool on = args.Length > 1 && args[1] == "on";
					Minimap.Enabled = true;
					yield return new WaitForSecondsRealtime(3f);
					Check(ref ok, WorldOptions.On(WorldOptions.MinimapOn) == on, "the world setting is " + (on ? "on" : "off") + " here (" + WorldOptions.On(WorldOptions.MinimapOn) + ", host " + Raft_Network.IsHost + ")");
					Check(ref ok, Minimap.Available == on, "the map is " + (on ? "available" : "not available") + " (" + Minimap.Available + ")");
					if (ok) Log("PASS: mp34 mapcheck " + (on ? "on" : "off") + (Raft_Network.IsHost ? " (host)" : " (player 2)")); else Fail("mp34 mapcheck");
					yield break;
				}
				case "island":
				{
					if (!Raft_Network.IsHost) { Fail("island: as the host"); yield break; }
					IslandWorldState.Entry e = null;
					yield return RfSpawn(MapBuild(), MapIsland, x => e = x);
					if (e == null) yield break;
					PutPlayerNear(ScObjOf(e, "cimapstart").transform);
					yield return new WaitForSecondsRealtime(1f);
					Log("PASS: mp34 island (id " + e.Id + ")");
					yield break;
				}
				case "panel":
				{
					if (Raft_Network.IsHost) { Fail("panel: as player 2"); yield break; }
					string dir = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : DynamicIslands.assetpath;
					IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == MapIsland);
					Check(ref ok, e != null, "player 2 has the map island in its list");
					if (e == null) { Fail("mp34 panel"); yield break; }
					// (the client is far from the island's loaded region: go to it, as a walk would)
					PlayerMove.To(player, e.Position + new Vector3(4f, 12f, 4f));
					float until = Time.realtimeSinceStartup + 40f;
					while ((e.Root == null || ScObjOf(e, "cimapstart") == null) && Time.realtimeSinceStartup < until) yield return null;
					Check(ref ok, e.Root != null, "the island loads on player 2's side");
					if (e.Root != null && ScObjOf(e, "cimapstart") != null) PutPlayerNear(ScObjOf(e, "cimapstart").transform);
					until = Time.realtimeSinceStartup + 10f;
					while ((!QuestTracker.PanelShown || !Minimap.CornerShown) && Time.realtimeSinceStartup < until) yield return null;
					yield return new WaitForSecondsRealtime(1f);
					Log("mp34 panel: panel " + QuestTracker.PanelShown + ", corner map " + Minimap.CornerShown + ", map island " + (Minimap.Island != null ? Minimap.Island.HostName : "none"));
					Check(ref ok, QuestTracker.PanelShown, "the quest panel shows for player 2");
					Check(ref ok, Minimap.CornerShown && Minimap.Island == e, "the corner map shows for player 2 on the island");
					Rect m;
					if (QuestTracker.PanelShown && Minimap.CornerScreenRect(out m)) Check(ref ok, !QuestTracker.PanelScreenRect().Overlaps(m), "the panel is not under the map");
					try { Directory.CreateDirectory(dir); ScreenCapture.CaptureScreenshot(Path.Combine(dir, "mp34_client_panel_map.png")); } catch { }
					yield return new WaitForSecondsRealtime(1f);
					if (ok) Log("PASS: mp34 panel (" + dir + ")"); else Fail("mp34 panel");
					yield break;
				}
				case "end":
				{
					if (!Raft_Network.IsHost) { Fail("end: as the host"); yield break; }
					IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == MapIsland);
					if (e != null) RfAway(e);
					yield return new WaitForSecondsRealtime(1f);
					RfRemove(MapIsland);
					var cur = WorldOptions.Current.ToList(); cur.Remove(WorldOptions.MinimapOn); WorldOptions.Set(cur);
					Log("PASS: mp34 end");
					yield break;
				}
				case "trustsend":
				{
					if (Raft_Network.IsHost) { Fail("trustsend: as player 2"); yield break; }
					int near = 0, far = 0;
					if (args.Length > 2) { int.TryParse(args[1], out near); int.TryParse(args[2], out far); }
					IslandWorldState.Entry en = IslandWorldState.Islands.FirstOrDefault(i => i.Id == near), ef = IslandWorldState.Islands.FirstOrDefault(i => i.Id == far);
					Check(ref ok, en != null && ef != null, "player 2 knows both islands");
					if (!ok) { Fail("mp34 trustsend"); yield break; }
					StoryBook.Give("ci-mp34-far", 3, ef);
					StoryBook.Give("ci-mp34-near", 2, en);
					yield return new WaitForSecondsRealtime(1f);
					Log("PASS: mp34 trustsend (far " + far + ", near " + near + ")");
					yield break;
				}
				case "trustcheck":
				{
					if (!Raft_Network.IsHost) { Fail("trustcheck: as the host"); yield break; }
					yield return new WaitForSecondsRealtime(3f);
					Check(ref ok, !PlayerTrust.ForTests, "the host's checks are on");
					Check(ref ok, StoryBook.Count("ci-mp34-far") == 0, "the far island's give is refused (" + StoryBook.Count("ci-mp34-far") + ")");
					Check(ref ok, StoryBook.Count("ci-mp34-near") == 2, "the near island's give is taken (" + StoryBook.Count("ci-mp34-near") + ")");
					Check(ref ok, PlayerTrust.Refused >= 1 && (PlayerTrust.LastRefused ?? "").Contains("story item"), "refusal counted (" + PlayerTrust.Refused + ": " + PlayerTrust.LastRefused + ")");
					StoryBook.Take("ci-mp34-near", 99);
					if (ok) Log("PASS: mp34 trustcheck"); else Fail("mp34 trustcheck");
					yield break;
				}
			}
			Fail("CIMp34 mapset|mapcheck|island|panel|end|trustsend|trustcheck");
		}
	}
}
