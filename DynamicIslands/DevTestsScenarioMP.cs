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
	/// Helpers for the two-player scenarios (tools\mpscenarios.ps1, TEST_PROTOCOL §5j SC41-SC50): an island with a lever
	/// that shows a hidden bridge, a once-zone with items, a chest and trees; an item count; a player sent far away.
	/// </summary>
	public static partial class DevTests
	{
		public const string ScMpIsland = "ciscmp";

		[ConsoleCommand(name: "CIScMpIsland", docs: "Dev, world (host): spawns and keeps 'ciscmp' for the two-player scenarios: a lever showing a hidden bridge, a once-zone 'gift' giving 3 ropes, a chest 'Box' (2 planks, never refilling), a sign, trees")]
		public static void ScMpIslandCommand() { DynamicIslands.instance.StartCoroutine(ScMpIslandRoutine()); }

		static IEnumerator ScMpIslandRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("mp island: run in a world, as the host"); yield break; }
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Where(x => x.HostName == ScMpIsland).Select(x => x.Id).ToList(), true);
			IslandFile f;
			try { f = ScIsland(ScMpIsland, "Two Player Isle"); } catch (Exception ex) { Fail("mp island: " + ex.Message); yield break; }
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(0, 0), 1), BehaviourProps.Name, "lever", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "show|bridge|"));
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(12, 0), 2), BehaviourProps.Name, "bridge", BehaviourProps.Hidden, "1"));
			f.Objects.Add(ScObj(ContentCatalog.TriggerZone, ScDry(f, new Vector2(-12, 0), 3), ObjectProps.ZoneId, "gift", ObjectProps.ZoneRadius, "4", ObjectProps.LootItems, "Rope*3", ObjectProps.ZoneMessage, "A gift"));
			f.Objects.Add(ScObj("Loot_Chest", ScDry(f, new Vector2(0, 12), 4), ObjectProps.NoteTitle, "Box", ObjectProps.LootItems, "Plank*2", ObjectProps.LootRefill, "0"));
			f.Save(IslandSpawner.PathFor(ScMpIsland));
			var made = new List<IslandWorldState.Entry>();
			Vector3? spot = ScSpot(ScMpIsland, 400f);
			if (!spot.HasValue) { Fail("mp island: no open sea near the raft"); yield break; }
			yield return ScBring(ScMpIsland, spot.Value, made);
			IslandWorldState.Entry e = made.FirstOrDefault();
			if (e != null && e.Root != null) Log("PASS: mp island '" + ScMpIsland + "' " + ScFlat(e.Position, CustomIslandSpawner.RaftPosition.Value).ToString("F0") + " m from the raft");
			else Fail("mp island: it didn't come");
		}

		[ConsoleCommand(name: "CIScCount", docs: "Dev, in game (either player): how many of an item this player has: CIScCount <unique item name> - logs ITEMS <name> <n>")]
		public static void ScCountCommand(string[] args)
		{
			string name = args != null && args.Length > 0 ? args[0] : "Plank";
			Network_Player p = RAPI.GetLocalPlayer();
			Log("ITEMS " + name + " " + (p != null && p.Inventory != null ? p.Inventory.GetItemCount(name) : -1));
		}

		[ConsoleCommand(name: "CIScFarAway", docs: "Dev, in game (either player): puts this player in the sea 1.5 km from the raft (their copies of the islands there unload), or back on the raft: CIScFarAway [back]")]
		public static void ScFarAwayCommand(string[] args)
		{
			Network_Player p = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (p == null || !raft.HasValue) { Fail("far away: in a world"); return; }
			if (args != null && args.Length > 0 && args[0] == "back") { OnRaftCommand(); Log("Back on the raft"); return; }
			Vector3 far = raft.Value + new Vector3(-1500f, 0f, -300f);
			far.y = 0.5f;
			PlayerMove.To(p, far, ControllerType.Water);
			KeepAlive(p);
			Log("Far away: " + ScFlat(p.transform.position, raft.Value).ToString("F0") + " m from the raft");
		}

		[ConsoleCommand(name: "CIScLoaded", docs: "Dev, in game (either player): whether this machine has an island loaded: CIScLoaded <island> - logs LOADED <island> yes|no")]
		public static void ScLoadedCommand(string[] args)
		{
			string name = args != null && args.Length > 0 ? args[0] : ScMpIsland;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.HostName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
			Log("LOADED " + name + " " + (e == null ? "unknown" : e.Root != null ? "yes" : "no"));
		}
	}
}
