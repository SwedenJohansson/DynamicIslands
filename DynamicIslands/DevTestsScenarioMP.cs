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
			f.Objects.Add(ScObj("Loot_Chest", ScDry(f, new Vector2(12, 12), 5), ObjectProps.NoteTitle, "Box2", ObjectProps.LootItems, "Nail*4", ObjectProps.LootRefill, "0"));
			f.Objects.Add(ScObj(ContentCatalog.TriggerZone, ScDry(f, new Vector2(-12, 12), 6), ObjectProps.ZoneId, "gift2", ObjectProps.ZoneRadius, "4", ObjectProps.LootItems, "Scrap*3", ObjectProps.ZoneMessage, "Another gift"));
			f.Objects.Add(ScObj(ContentCatalog.TriggerZone, ScDry(f, new Vector2(-12, -12), 7), ObjectProps.ZoneId, "gift3", ObjectProps.ZoneRadius, "4", ObjectProps.LootItems, "Stone*3", ObjectProps.ZoneMessage, "A third gift"));
			f.Save(IslandSpawner.PathFor(ScMpIsland));
			var made = new List<IslandWorldState.Entry>();
			Vector3? spot = ScSpot(ScMpIsland, 400f);
			if (!spot.HasValue) { Fail("mp island: no open sea near the raft"); yield break; }
			yield return ScBring(ScMpIsland, spot.Value, made);
			IslandWorldState.Entry e = made.FirstOrDefault();
			if (e != null && e.Root != null) Log("PASS: mp island '" + ScMpIsland + "' " + ScFlat(e.Position, CustomIslandSpawner.RaftPosition.Value).ToString("F0") + " m from the raft");
			else Fail("mp island: it didn't come");
		}

		[ConsoleCommand(name: "CIScLair", docs: "Dev, world (host): spawns and keeps 'cisclair' for the two-player lair scenario (SC25): an arena zone that wakes a Boss warthog (x4 health, never coming back), a hoard chest; the level up system on")]
		public static void ScLairCommand() { DynamicIslands.instance.StartCoroutine(ScMpSpawn("cisclair", "Boar's Lair", f =>
		{
			f.Props[IslandProps.Levels] = "on";
			Vector3 arena = ScDry(f, new Vector2(0, 0), 1);
			f.Objects.Add(ScObj(ContentCatalog.TriggerZone, arena, ObjectProps.ZoneId, "arena", ObjectProps.ZoneRadius, "6", ObjectProps.ZoneMessage, "The ground shakes... the boar wakes!"));
			f.Objects.Add(ScObj("Creature_Boar", ScDry(f, new Vector2(8, 0), 2), ObjectProps.CreatureCount, "1", ObjectProps.CreatureRespawn, "0", ObjectProps.CreatureZone, "arena",
				ObjectProps.CreatureHealth, "4", ObjectProps.CreatureDamage, "0.2", ObjectProps.CreatureSize, "1.3"));
			f.Objects.Add(ScObj("Loot_Chest", ScDry(f, new Vector2(-10, 0), 3), ObjectProps.NoteTitle, "Hoard", ObjectProps.LootItems, "MetalIngot*2", ObjectProps.LootRefill, "0"));
		})); }

		[ConsoleCommand(name: "CIScSpots", docs: "Dev, world (host): spawns and keeps 'ciscspots' for SC26: a plain warthog spot and a Boss warthog spot (x4 health, red) 20 m apart, the same size")]
		public static void ScSpotsCommand() { DynamicIslands.instance.StartCoroutine(ScMpSpawn("ciscspots", "Two Spots", f =>
		{
			f.Objects.Add(ScObj("Creature_Boar", ScDry(f, new Vector2(-10, 0), 1), ObjectProps.CreatureCount, "1", ObjectProps.CreatureDamage, "0.1"));
			f.Objects.Add(ScObj("Creature_Boar", ScDry(f, new Vector2(10, 0), 2), ObjectProps.CreatureCount, "1", ObjectProps.CreatureHealth, "4", ObjectProps.CreatureDamage, "0.1",
				ObjectProps.TintColor, "#FF2020", ObjectProps.TintAmount, "1"));
		})); }

		[ConsoleCommand(name: "CIScChickens", docs: "Dev, world (host): spawns and keeps 'ciscchick' for SC28/SC45: one chicken that both players try to net in the same second")]
		public static void ScChickensCommand() { DynamicIslands.instance.StartCoroutine(ScMpSpawn("ciscchick", "Chicken Run", f =>
		{
			f.Objects.Add(ScObj("Creature_Chicken", ScDry(f, new Vector2(0, 0), 1), ObjectProps.CreatureCount, "1", ObjectProps.CreatureRespawn, "0"));
		})); }

		static IEnumerator ScMpSpawn(string name, string title, Action<IslandFile> fill)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail(name + ": run in a world, as the host"); yield break; }
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Where(x => x.HostName == name).Select(x => x.Id).ToList(), true);
			IslandFile f;
			try { f = ScIsland(name, title); fill(f); f.Save(IslandSpawner.PathFor(name)); } catch (Exception ex) { Fail(name + ": " + ex.Message); yield break; }
			var made = new List<IslandWorldState.Entry>();
			Vector3? spot = ScSpot(name, 400f);
			if (!spot.HasValue) { Fail(name + ": no open sea near the raft"); yield break; }
			yield return ScBring(name, spot.Value, made);
			IslandWorldState.Entry e = made.FirstOrDefault();
			if (e != null && e.Root != null) Log("PASS: mp island '" + name + "' " + ScFlat(e.Position, CustomIslandSpawner.RaftPosition.Value).ToString("F0") + " m from the raft");
			else Fail(name + ": it didn't come");
		}

		[ConsoleCommand(name: "CIScReadStoryNote", docs: "Dev, in game (either player, a test world): this player finds the first n frequency notes of the world's story order, through Raft's own network path (a player's note goes to the host): CIScReadStoryNote <n>")]
		public static void ScReadStoryNoteCommand(string[] args)
		{
			int n;
			if (args == null || args.Length == 0 || !int.TryParse(args[0], out n)) n = 1;
			NoteBook book = UnityEngine.Object.FindObjectOfType<NoteBook>();
			if (book == null) { Fail("read story note: no notebook"); return; }
			Dictionary<ChunkPointType, int> noteFor = StoryOrder.FrequencyNotes().ToDictionary(kv => kv.Value, kv => kv.Key);
			var notes = new List<int>();
			ChunkPointType at = ChunkPointType.None;
			for (int i = 0; i < n; i++)
			{
				int k = at == ChunkPointType.None ? -1 : Array.IndexOf(StoryOrder.Chain, at);
				if (k + 1 >= StoryOrder.Chain.Length || !noteFor.ContainsKey(StoryOrder.Chain[k + 1])) break;
				ChunkPointType raftType = StoryOrder.Chain[k + 1];
				notes.Add(noteFor[raftType]);
				at = StoryOrder.Map(raftType);
			}
			foreach (int note in notes) book.UnlockSpecificNoteNetworked(note, false);
			Log("Read the story notes " + string.Join(", ", notes.Select(x => x.ToString()).ToArray()) + " (" + (Raft_Network.IsHost ? "host" : "player") + ")");
			Log("PASS: read story note");
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

		[ConsoleCommand(name: "CIScDay", docs: "Dev, in game (either player): Raft's day counter here, and the day stamped on a chest's state: CIScDay [island] [chest title] - logs DAY <n> [STAMP <n>]")]
		public static void ScDayCommand(string[] args)
		{
			int? d = ScDay;
			string stamp = "";
			if (args != null && args.Length > 1)
			{
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.HostName.IndexOf(args[0], StringComparison.OrdinalIgnoreCase) >= 0);
				LootCrate c = ScChest(e, string.Join(" ", args.Skip(1).ToArray()));
				ObjectState st;
				if (c != null && e.State.TryGetValue(c.StateKey, out st)) stamp = " STAMP " + st.Day;
				else stamp = " STAMP none";
			}
			Log("DAY " + (d.HasValue ? d.Value.ToString() : "?") + stamp + " (" + (Raft_Network.IsHost ? "host" : "player") + ")");
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
