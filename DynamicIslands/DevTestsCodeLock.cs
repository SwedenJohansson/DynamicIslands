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
