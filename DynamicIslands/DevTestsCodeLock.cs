using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIMapReach", docs: "Dev, anywhere: ROADMAP LM3 - every map type's islands (3 seeds each): no chest or note left where players can't get without building (the reach check moves them)")]
		public static void MapReachCommand(string[] args) { DynamicIslands.instance.StartCoroutine(MapReachRoutine()); }

		static IEnumerator MapReachRoutine()
		{
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			int total = 0, movedAll = 0;
			foreach (MapType type in MapTypes.All)
				for (int seed = 1; seed <= 3; seed++)
				{
					float elevation;
					IslandGenSettings s = MapTypes.Roll(type, new System.Random(seed * 7919 + type.Name.Length), out elevation);
					IslandFile f = MapTypes.Create(type, s, elevation, "cimapreach");
					if (elevation != 0f) continue;
					// (the check again on the result: what is still out of reach)
					int moved = IslandReach.MoveContentWithinReach(f);
					int content = f.Objects.Count(o => ContentCatalog.IsLootObject(o.Name) || ContentCatalog.IsNoteObject(o.Name));
					total += content; movedAll += moved;
					if (moved > 0) Check(ref ok, false, type.Name + " seed " + seed + ": " + moved + " of " + content + " chests/notes still out of reach after Create");
					yield return null;
				}
			Check(ref ok, total > 0, total + " chests and notes on the map types' islands, all where players reach them");
			if (ok) Log("PASS: map reach"); else Fail("map reach");
		}

		[ConsoleCommand(name: "CIRewardLater", docs: "Dev, in game (host): ROADMAP LM8 - a quest done while this player is far away: no reward then; coming to the island gives it, once (done again or coming again gives nothing)")]
		public static void RewardLaterCommand(string[] args) { DynamicIslands.instance.StartCoroutine(RewardLaterRoutine()); }

		static IEnumerator RewardLaterRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (player == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			const string name = "cirewardlater";
			var s = new IslandGenSettings { Seed = 8787, Radius = 30f, Height = 6f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, name);
			f.Props[IslandQuest.KeyTitle] = "CI reward later";
			f.Props[IslandQuest.KeySteps] = "reach|cirewardzone|1|";
			f.Props[IslandQuest.KeyReward] = "Plank*7";
			IslandWorldState.Remove(name);
			f.Save(IslandSpawner.PathFor(name));
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(name), 450f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			yield return DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name);
			if (e == null || e.Root == null) { Fail(name + " did not spawn"); yield break; }
			OnRaftCommand();
			yield return new WaitForSeconds(1f);
			Dictionary<string, int> before = Items(player);
			QuestTracker.Set(e, 1, 0, true);
			yield return new WaitForSeconds(1f);
			string got = Gained(before, Items(player));
			Check(ref ok, got.Length == 0 && QuestRewards.Owed(e.HostName), "done while far away: nothing given yet, the reward kept (" + (got.Length > 0 ? got : "nothing") + ")");
			PutPlayerNear(e.Root.transform, 2f);
			Vector3 c = e.Position + new Vector3(0f, 0f, 0f);
			IslandInfoTag tag = e.Root.GetComponent<IslandInfoTag>();
			if (tag != null) player.transform.position = e.Root.transform.position + tag.LocalCentre + Vector3.up * 3f;
			yield return new WaitForSeconds(2.5f);
			got = Gained(before, Items(player));
			Check(ref ok, got.Contains("Plank") && !QuestRewards.Owed(e.HostName) && QuestRewards.Rewarded(e.HostName), "coming to the island gives it: " + (got.Length > 0 ? got : "nothing"));
			Dictionary<string, int> mid = Items(player);
			OnRaftCommand();
			yield return new WaitForSeconds(1f);
			if (tag != null) player.transform.position = e.Root.transform.position + tag.LocalCentre + Vector3.up * 3f;
			yield return new WaitForSeconds(2f);
			Check(ref ok, Gained(mid, Items(player)).Length == 0, "coming again gives nothing more");
			OnRaftCommand();
			IslandWorldState.Remove(name);
			if (ok) Log("PASS: reward later"); else Fail("reward later");
		}

		[ConsoleCommand(name: "CILightColours", docs: "Dev, in game (host): ROADMAP LM5 - four real warthogs: as they are, the randomizer's light snow and cream (brightened textures) and its charcoal; a picture shot_light_colours.png")]
		public static void LightColoursCommand(string[] args) { DynamicIslands.instance.StartCoroutine(LightColoursRoutine()); }

		static IEnumerator LightColoursRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (player == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			const string name = "cilightcolours";
			var s = new IslandGenSettings { Seed = 8888, Radius = 40f, Height = 4f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, name);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Vector2 c = IslandSpawner.LandCentre(f);
			Func<float, float, float> ground = (x, z) => IslandGenerator.SampleHeights(f.Heights, res, step, x, z) * f.TerrainSize.y;
			for (int i = 0; i < 4; i++)
			{
				float x = c.x + (i - 1.5f) * 3f, z = c.y + 6f;
				f.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = new Vector3(x, ground(x, z), z), Props = new Dictionary<string, string> { { ObjectProps.CreatureCount, "1" } } });
			}
			IslandWorldState.Remove(name);
			f.Save(IslandSpawner.PathFor(name));
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(name), 450f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			yield return DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name);
			if (e == null || e.Root == null) { Fail(name + " did not spawn"); yield break; }
			IslandInfoTag tag = e.Root.GetComponent<IslandInfoTag>();
			Vector3 mid = e.Root.transform.position + (tag != null ? tag.LocalCentre : Vector3.zero);
			player.transform.position = mid + Vector3.up * 2f;
			yield return ScWaitAnimals(e, "Warthog", 4, 15f);
			List<AI_NetworkBehaviour> hogs = ScAnimals(e, "Warthog").OrderBy(a => a.transform.position.x).ToList();
			if (hogs.Count < 4) { Fail("only " + hogs.Count + " warthogs"); yield break; }
			string[] looks = { null, "snow", "cream", "charcoal" };
			bool ok = true;
			for (int i = 0; i < 4; i++)
			{
				AI_NetworkBehaviour a = hogs[i];
				// (still for the picture: in a row in front of the player)
				foreach (MonoBehaviour m in a.GetComponents<MonoBehaviour>()) if (m != a && m.GetType().Name.StartsWith("AI_State")) m.enabled = false;
				a.transform.position = mid + new Vector3((i - 1.5f) * 2.6f, 0.2f, 6f);
				a.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
				if (looks[i] != null) ok &= WorldRandomizer.ApplyColourForTest(a, looks[i]);
			}
			player.transform.position = mid + new Vector3(0f, 0.5f, 0f);
			player.transform.rotation = Quaternion.LookRotation(Vector3.forward);
			Camera cam = Camera.main;
			if (cam != null) cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.15f, 1f));
			yield return new WaitForSeconds(0.4f);
			Screenshot(new[] { "light_colours" });
			yield return new WaitForSeconds(0.8f);
			OnRaftCommand();
			IslandWorldState.Remove(name);
			if (ok) Log("PASS: light colours"); else Fail("light colours");
		}

		const string CodeLockIsland = "cicodelock";

		[ConsoleCommand(name: "CICodeLock", docs: "Dev, in game (host): a keypad code lock (lock.code) - used, the keypad opens; a wrong code keeps it shut, the right one runs the use (shows a hidden chest); unlocked it stays so after a reload")]
		public static void CodeLockCommand(string[] args) { DynamicIslands.instance.StartCoroutine(CodeLockRoutine()); }

		static IEnumerator CodeLockRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (player == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureBuilt();
			var s = new IslandGenSettings { Seed = 8686, Radius = 40f, Height = 6f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, CodeLockIsland);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Vector2 c = IslandSpawner.LandCentre(f);
			Func<float, float, float> ground = (x, z) => IslandGenerator.SampleHeights(f.Heights, res, step, x, z) * f.TerrainSize.y;
			f.Objects.Add(new IslandObject { Name = "RT_PowerBox", Position = new Vector3(c.x, ground(c.x, c.y), c.y), Props = new Dictionary<string, string> {
				{ BehaviourProps.Name, "keypad" }, { BehaviourProps.Use, "Type the code" }, { CodeLock.Code, "4711" }, { BehaviourProps.EventKey("use"), "show|safe" } } });
			f.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = new Vector3(c.x + 3f, ground(c.x + 3f, c.y), c.y), Props = new Dictionary<string, string> {
				{ BehaviourProps.Name, "safe" }, { BehaviourProps.Hidden, "1" }, { ObjectProps.LootItems, "Scrap*3" }, { ObjectProps.NoteTitle, "Safe" } } });
			IslandWorldState.Remove(CodeLockIsland);
			f.Save(IslandSpawner.PathFor(CodeLockIsland));
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(CodeLockIsland), 450f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			yield return DynamicIslands.instance.SpawnIslandFile(CodeLockIsland, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == CodeLockIsland);
			if (e == null || e.Root == null) { Fail(CodeLockIsland + " did not spawn"); yield break; }
			yield return new WaitForSeconds(1f);
			IslandObjectRef pad = ScObjOf(e, "keypad"), safe = ScObjOf(e, "safe");
			if (pad == null || safe == null) { Fail("keypad or safe missing"); yield break; }
			PutPlayerNear(pad.transform, 1.2f);
			Check(ref ok, !safe.gameObject.activeInHierarchy, "the safe is hidden at first");
			CodeLock.Use(e, pad.Index, ObjectProps.Get(pad.Props, CodeLock.Code), pad.transform);
			yield return new WaitForSeconds(0.4f);
			Check(ref ok, CodeLock.IsOpen, "using it opens the keypad");
			foreach (string k in new[] { "1", "2", "3", "4", "OK" }) CodeLock.Press(k);
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, CodeLock.IsOpen && !safe.gameObject.activeInHierarchy && !CodeLock.Unlocked(e, pad.Index), "a wrong code keeps it shut");
			foreach (string k in new[] { "4", "7", "1", "1", "OK" }) CodeLock.Press(k);
			yield return new WaitForSeconds(0.8f);
			Check(ref ok, !CodeLock.IsOpen && safe.gameObject.activeInHierarchy && CodeLock.Unlocked(e, pad.Index), "the right code opens it: the safe shows");
			OnRaftCommand();
			yield return new WaitForSeconds(0.5f);
			yield return ReloadIslandRoutine(e);
			yield return new WaitForSeconds(1f);
			IslandObjectRef safe2 = ScObjOf(e, "safe");
			Check(ref ok, CodeLock.Unlocked(e, pad.Index) && safe2 != null && safe2.gameObject.activeInHierarchy, "after a reload it stays unlocked, the safe there");
			IslandWorldState.Remove(CodeLockIsland);
			if (ok) Log("PASS: code lock"); else Fail("code lock");
		}
	}
}
