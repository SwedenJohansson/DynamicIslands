using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>Tests for the level up system (LevelSystem.cs, LevelWindow.cs).</summary>
	public static partial class DevTests
	{
		const string LevelIsland = "cilevels", LevelMPIsland = "cilevelmp";

		#region Editor

		[ConsoleCommand(name: "CILevelTest", docs: "Dev, editor: the level up system's numbers (EXP per level, a monster's EXP, 2 points a level, +1% a point, 10 at most), records written and read, the Island tab's Level up system switch, the generator's Level up choice and the island files it makes")]
		public static void LevelTest()
		{
			DynamicIslands.instance.StartCoroutine(LevelTestRoutine());
		}

		static IEnumerator LevelTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;

			// The curve: level 2 after about 5 kills of Bruce, level 3 about 10 more, level 4 about 20 more, level 5 about 30 more
			int bruce = LevelRules.XpOf(LevelRules.SharkHealth, LevelRules.SharkDamage);
			Check(ref ok, bruce == LevelRules.ReferenceXp, "Bruce is worth " + bruce + " EXP");
			int[] kills = Enumerable.Range(1, 5).Select(l => LevelRules.XpFor(l) / bruce).ToArray();
			Check(ref ok, kills.SequenceEqual(new[] { 5, 10, 20, 30, 40 }), "kills of Bruce from each level to the next: " + string.Join(", ", kills.Select(k => k.ToString()).ToArray()));
			Check(ref ok, LevelRules.TotalFor(1) == 0 && LevelRules.TotalFor(2) == 100 && LevelRules.TotalFor(3) == 300 && LevelRules.TotalFor(4) == 700 && LevelRules.TotalFor(5) == 1300,
				"EXP at which levels 2-5 start: " + string.Join(", ", Enumerable.Range(2, 4).Select(l => LevelRules.TotalFor(l).ToString()).ToArray()));
			Check(ref ok, LevelRules.LevelOf(0) == 1 && LevelRules.LevelOf(99) == 1 && LevelRules.LevelOf(100) == 2 && LevelRules.LevelOf(299) == 2 && LevelRules.LevelOf(300) == 3 && LevelRules.LevelOf(1300) == 5,
				"levels from EXP at the edges (99 -> 1, 100 -> 2, 299 -> 2, 300 -> 3, 1300 -> 5)");
			Check(ref ok, Mathf.Approximately(LevelRules.Factor(0), 1f) && Mathf.Approximately(LevelRules.Factor(4), 1.04f) && Mathf.Approximately(LevelRules.Factor(10), 1.1f) && Mathf.Approximately(LevelRules.Factor(15), 1.1f),
				"a point is +1%, 10 points at most (+10%)");
			// A monster's EXP: from its health and its damage, both count
			int weak = LevelRules.XpOf(50f, 5f), tough = LevelRules.XpOf(800f, 5f), biting = LevelRules.XpOf(50f, 40f);
			Check(ref ok, weak >= 1 && tough > weak && biting > weak && LevelRules.XpOf(0f, 0f) == 1, "EXP grows with health and with damage (" + weak + " / tougher " + tough + " / biting harder " + biting + "), at least 1");
			Check(ref ok, !LevelRules.IsMonster(AI_NetworkBehaviourType.Chicken) && !LevelRules.IsMonster(AI_NetworkBehaviourType.Turtle) && !LevelRules.IsMonster(AI_NetworkBehaviourType.NPC_Johan) &&
				LevelRules.IsMonster(AI_NetworkBehaviourType.Shark) && LevelRules.IsMonster(AI_NetworkBehaviourType.Boar) && LevelRules.IsMonster(AI_NetworkBehaviourType.MamaBear), "chickens, turtles and people aren't monsters; Bruce, warthogs and bears are");

			// Records
			var r = new LevelRecord { Xp = 750, Kills = 37 };
			r.Points[LevelRules.Jump] = 3; r.Points[LevelRules.Oxygen] = 2;
			LevelRecord back = LevelRecord.Decode(r.Encode());
			Check(ref ok, back.Xp == 750 && back.Kills == 37 && back.Points.SequenceEqual(r.Points) && back.Level == 4 && back.Unspent == 1, "a record written and read back (" + r.Encode() + ": level " + back.Level + ", " + back.Unspent + " point left, 37 monsters)");
			Check(ref ok, LevelRules.StatCount == 9 && LevelRules.StatNames[LevelRules.Thirst] == "Thirst" && LevelRecord.Decode("300|0,0,0,0,0,0,0,1,0").Points[LevelRules.Thirst] == 1, "nine stats, Thirst among them");
			Check(ref ok, LevelRecord.Decode("300|1,0,0,0,0,0,0,0").Kills == 0 && LevelRecord.Decode("300|1,0,0,0,0,0,0,0").Points[0] == 1, "an older record (eight stats, no kills) still reads");
			// All stats full: the levels go on, without points
			Check(ref ok, LevelRules.AllPoints == 90 && LevelRules.PointsAt(46) == 90 && LevelRules.PointsAt(47) == 90 && LevelRules.PointsAt(80) == 90 && LevelRules.PointsAt(45) == 88, "every stat full takes 90 points, at level 46; later levels give none");
			var full = new LevelRecord { Xp = LevelRules.TotalFor(60), Points = Enumerable.Repeat(10, LevelRules.StatCount).ToArray() };
			Check(ref ok, full.Level == 60 && full.Unspent == 0 && full.AllFull, "level 60 with every stat full: nothing to spend");
			var nearly = new LevelRecord { Xp = LevelRules.TotalFor(60), Points = new[] { 10, 10, 10, 10, 10, 10, 10, 10, 0 } };
			Check(ref ok, nearly.Unspent == 10 && !nearly.AllFull, "level 60 with 80 spent: the last 10 points are still there");
			LevelRecord cheat = LevelRecord.Decode("150|10,10,10,0,0,0,0,0");
			Check(ref ok, cheat.Level == 2 && cheat.Spent == 2, "no more points than the level gives (level 2 of a hand-edited file keeps " + cheat.Spent + ")");
			Check(ref ok, LevelRecord.Decode("").Xp == 0 && LevelRecord.Decode("x|y").Xp == 0 && LevelRecord.Decode("-5").Xp == 0, "broken records read as nothing");
			Check(ref ok, PlayerLevels.IsOn(P(IslandProps.Levels, "on")) && !PlayerLevels.IsOn(P(IslandProps.Title, "x")) && !PlayerLevels.IsOn(null), "an island's Level up system rule");

			// The Island tab's switch
			var saved = new Dictionary<string, string>(DynamicIslands.currentIslandProps);
			EditorUI.SetTab(TAB.Island);
			yield return null;
			Transform row = EditorUI.Canvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LevelUp");
			Button on = row != null ? row.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Button_On") : null;
			Button off = row != null ? row.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Button_Off") : null;
			Check(ref ok, on != null && off != null && on.gameObject.activeInHierarchy, "the Island tab has Level up system Off / On (Rules)");
			if (on != null && off != null)
			{
				DynamicIslands.currentIslandProps.Remove(IslandProps.Levels);
				on.onClick.Invoke();
				Check(ref ok, PlayerLevels.IsOn(DynamicIslands.currentIslandProps), "On sets the island's rule (" + ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.Levels) + ")");
				off.onClick.Invoke();
				Check(ref ok, !DynamicIslands.currentIslandProps.ContainsKey(IslandProps.Levels), "Off takes it away");
			}
			Screenshot(new[] { "levels_island_tab" });
			yield return new WaitForSecondsRealtime(0.5f);

			// The generator: the choice on its tabs, and the files it makes
			GeneratorWindow.Open();
			yield return new WaitForSecondsRealtime(0.3f);
			GameObject gw = GameObject.Find("GeneratorWindow");
			Transform[] rows = gw != null ? gw.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Level up").ToArray() : new Transform[0];
			Check(ref ok, rows.Length == 3, "the generator has a Level up choice on its three tabs (" + rows.Length + ")");
			Transform shown = rows.FirstOrDefault(t => t.gameObject.activeInHierarchy);
			Button genOn = shown != null ? shown.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.StartsWith("Button_On")) : null;
			if (genOn != null) genOn.onClick.Invoke();
			IslandGenSettings gs = GeneratorWindow.Current;
			Check(ref ok, gs != null && gs.Levels, "choosing On sets the generator's setting");
			GeneratorWindow.Close();
			if (gs != null)
			{
				gs.Radius = 40f; gs.Height = 12f; gs.ObjectDensity = 0.1f; gs.Hostiles = 1; gs.Seed = 777;
				IslandFile made = IslandGenerator.CreateFile(gs, "cilevels_gen");
				made.Save(IslandSpawner.PathFor("cilevels_gen"));
				IslandFile loaded = IslandFile.Load(IslandSpawner.PathFor("cilevels_gen"));
				Check(ref ok, PlayerLevels.IsOn(made.Props) && loaded != null && PlayerLevels.IsOn(loaded.Props), "an island file made with it keeps the rule after saving and loading");
				File.Delete(IslandSpawner.PathFor("cilevels_gen"));
				gs.Levels = false;
				Check(ref ok, !IslandGenerator.CreateFile(gs, "cilevels_gen").Props.ContainsKey(IslandProps.Levels), "Off: no rule in the file");
				// Generating in the editor gives the island the rule
				DynamicIslands.currentIslandProps.Remove(IslandProps.Levels);
				gs.Levels = true;
				IslandGenerator.GenerateInEditor(gs);
				EditorUI.RefreshIsland();
				Check(ref ok, PlayerLevels.IsOn(DynamicIslands.currentIslandProps), "generating in the editor with On gives the island its rule");
				CommandUndoRedo.UndoRedoManager.Undo();
			}
			DynamicIslands.currentIslandProps.Clear();
			foreach (var kv in saved) DynamicIslands.currentIslandProps[kv.Key] = kv.Value;
			EditorUI.RefreshIsland();
			Check(ref ok, !PlayerLevels.On, "the level up system is off in the editor");
			if (ok) Log("PASS: level up system (editor)"); else Fail("level up system (editor)");
		}

		#endregion

		#region World

		[ConsoleCommand(name: "CILevelWorld", docs: "Dev, in game (host): an island with the level up system and warthogs - it turns the system on, hits give their share of the monster's EXP (floating over it), a kill all of it, level up, the stats page (K), every stat on Raft's player, saving with the world, the host keeping other players' records")]
		public static void LevelWorld()
		{
			DynamicIslands.instance.StartCoroutine(LevelWorldRoutine());
		}

		static IEnumerator LevelWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			PlayerLevels.TurnOff();
			Network_Player player = RAPI.GetLocalPlayer();
			ulong me = player.steamID.Id;

			// Harmony: Raft's methods the stats work through
			string owner = "com.franzfischer.customislands";
			foreach (MethodBase m in new MethodBase[] { AccessTools.Method(typeof(PersonController), "GroundControll"), AccessTools.Method(typeof(PersonController), "WaterControll"),
				AccessTools.PropertyGetter(typeof(Stat_Consumable), "LostPerSecond"), AccessTools.Method(typeof(Stat_Oxygen), "Update"), AccessTools.Method(typeof(Network_Host), "DamageEntity"), AccessTools.Method(typeof(Network_Host_Entities), "Deserialize") })
			{
				Patches info = m != null ? Harmony.GetPatchInfo(m) : null;
				Check(ref ok, info != null && info.Prefixes.Concat(info.Postfixes).Any(p => p.owner == owner && p.PatchMethod.DeclaringType.Name.StartsWith("Level")), "Raft's " + (m != null ? m.DeclaringType.Name + "." + m.Name : "?") + " is patched");
			}

			// An island with the system on and three warthogs that don't bite
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor("generated_sample"));
			f.Name = LevelIsland;
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Func<float, float, Vector3> ground = (x, z) => new Vector3(x, f.Heights[Mathf.RoundToInt(z / step), Mathf.RoundToInt(x / step)] * f.TerrainSize.y, z);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 15f);
			f.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = ground(c.x + 6f, c.y + 4f), Props = P(ObjectProps.CreatureCount, "3", ObjectProps.CreatureDamage, "0") });
			f.Objects.Add(new IslandObject { Name = "Creature_Chicken", Position = ground(c.x - 6f, c.y - 4f), Props = P(ObjectProps.CreatureCount, "1") });
			f.Props[IslandProps.Levels] = "on";
			f.Props[IslandProps.Title] = "Level Isle";
			f.Save(IslandSpawner.PathFor(LevelIsland));

			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius(LevelIsland), 390f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			var sent = new List<IslandNetMessage>();
			IslandNetwork.Loopback = m => { if (m.Kind == IslandNetMessage.Levels) sent.Add(m); };
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(LevelIsland, spot.Value, true);
			IslandNetwork.Loopback = null;
			IslandWorldState.Entry entry = IslandWorldState.Islands.Skip(before).FirstOrDefault(e => e.HostName == LevelIsland);
			if (entry == null || entry.Root == null) { Fail("the level island did not spawn"); yield break; }
			Check(ref ok, PlayerLevels.On && PlayerLevels.Mine != null && PlayerLevels.Mine.Level == 1, "the island turns the level up system on (level " + (PlayerLevels.Mine != null ? PlayerLevels.Mine.Level : 0) + ")");
			Check(ref ok, sent.Any(m => m.Name == "on"), "the other players are told");
			Check(ref ok, LevelHud.LastAnnounce != null && LevelHud.LastAnnounce.StartsWith("LEVEL UP SYSTEM"), "a banner says so: " + LevelHud.LastAnnounce);

			// Wait for the warthogs; stand next to them
			float t0 = Time.realtimeSinceStartup;
			List<AI_NetworkBehaviour> boars = new List<AI_NetworkBehaviour>();
			while (Time.realtimeSinceStartup - t0 < 40f)
			{
				boars = entry.Root.GetComponentsInChildren<CreatureSpawnPoint>(true).Where(p => p.Kind != null && p.Kind.Type == AI_NetworkBehaviourType.Boar)
					.SelectMany(p => p.Spawned).Where(a => a != null && a.networkEntity != null && !a.networkEntity.IsDead && a.networkEntity.stat_health.Max > 0f).ToList();
				if (boars.Count >= 3) break;
				yield return new WaitForSeconds(0.5f);
			}
			if (boars.Count < 3) { Fail("the warthogs did not come (" + boars.Count + ")"); LevelCleanup(entry); yield break; }
			yield return StandRoutine(entry.Root);
			PutPlayerNear(boars[0].transform, 5f);
			Network_Host host = ComponentManager<Network_Host>.Value;
			float pve = 1f;
			SO_GameModeValue mode = GameModeValueManager.GetCurrentGameModeValue();
			if (mode != null && mode.playerSpecificVariables != null) pve = mode.playerSpecificVariables.negateOutgoingPlayerDamage ? 0f : mode.playerSpecificVariables.outgoingDamageMultiplierPVE;
			Log("  warthog: health " + boars[0].networkEntity.stat_health.Max + ", damage " + PlayerLevels.DamageOf(boars[0].gameObject, boars[0].behaviourType) + ", worth " + PlayerLevels.MonsterXp(boars[0]) + " EXP; PvE damage x" + pve);
			if (pve <= 0f) { Fail("this world's game mode takes away players' damage"); LevelCleanup(entry); yield break; }

			// Hits: each gives its share of the warthog's EXP, and the kill all of the rest
			AI_NetworkBehaviour boar = boars[0];
			int worth = PlayerLevels.MonsterXp(boar);
			float max = boar.networkEntity.stat_health.Max;
			int xp0 = PlayerLevels.Mine.Xp, kills0 = PlayerLevels.Mine.Kills;
			var gains = new List<int>();
			for (int i = 0; i < 6 && !boar.networkEntity.IsDead; i++)
			{
				if (i == 0)
				{
					// (the player looks at the warthog, so the picture shows the EXP floating over it)
					Vector3 eye = Camera.main != null ? Camera.main.transform.position : player.transform.position + Vector3.up * 1.6f;
					Vector3 to = boar.transform.position + Vector3.up * 0.8f - eye;
					Look(player, Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y, Mathf.Atan2(-to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
					yield return new WaitForSeconds(0.3f);
				}
				int x = PlayerLevels.Mine.Xp;
				host.DamageEntity(boar.networkEntity, boar.transform, max * 0.3f / pve, boar.transform.position + Vector3.up, Vector3.up, EntityType.Player, null);
				gains.Add(PlayerLevels.Mine.Xp - x);
				if (i == 0)
				{
					Check(ref ok, PlayerLevels.LastGain == gains[0] && LevelHud.LastFloat == "+" + gains[0] + " EXP" && LevelHud.Floating.Contains(LevelHud.LastFloat), "a hit floats its EXP over the warthog (" + LevelHud.LastFloat + ")");
					Check(ref ok, (PlayerLevels.LastGainAt - boar.transform.position).y > 0.2f && FlatDistance(PlayerLevels.LastGainAt, boar.transform.position) < 3f, "above the warthog (" + (PlayerLevels.LastGainAt - boar.transform.position).y.ToString("F1") + " m up)");
					Check(ref ok, LevelHud.BarShown, "the level bar shows");
					yield return new WaitForSeconds(0.3f);
					Screenshot(new[] { "levels_hit" });
				}
				yield return new WaitForSeconds(0.2f);
			}
			Check(ref ok, boar.networkEntity.IsDead, "four hits of 30% kill it");
			Check(ref ok, PlayerLevels.Mine.Xp - xp0 == worth && gains.All(g => g >= 0), "the hits give the warthog's EXP, no more (" + string.Join(" + ", gains.Select(g => g.ToString()).ToArray()) + " = " + (PlayerLevels.Mine.Xp - xp0) + " of " + worth + ")");
			int afterKill = PlayerLevels.Mine.Xp;
			host.DamageEntity(boar.networkEntity, boar.transform, 50f, boar.transform.position, Vector3.up, EntityType.Player, null);
			Check(ref ok, PlayerLevels.Mine.Xp == afterKill, "a dead warthog gives nothing more");
			Check(ref ok, PlayerLevels.Mine.Kills == kills0 + 1, "the kill is counted (" + PlayerLevels.Mine.Kills + " monster(s) defeated), once");

			// A chicken isn't a monster
			AI_NetworkBehaviour chicken = entry.Root.GetComponentsInChildren<CreatureSpawnPoint>(true).Where(p => p.Kind != null && p.Kind.Type == AI_NetworkBehaviourType.Chicken).SelectMany(p => p.Spawned).FirstOrDefault(a => a != null && a.networkEntity != null && !a.networkEntity.IsDead);
			if (chicken != null)
			{
				int x = PlayerLevels.Mine.Xp;
				host.DamageEntity(chicken.networkEntity, chicken.transform, 1f, chicken.transform.position, Vector3.up, EntityType.Player, null);
				Check(ref ok, PlayerLevels.Mine.Xp == x, "hitting a chicken gives no EXP");
			}
			else Log("  (no chicken came: not checked)");
			// A monster's hit on the player is left alone
			host.DamageEntity(player.Stats, player.transform, 5f, player.transform.position, Vector3.up, EntityType.Enemy, null);
			Check(ref ok, PlayerLevels.Mine.Xp == afterKill, "being bitten gives no EXP");
			KeepAlive(player);

			// Another player's hits: the host works out their EXP (it has the monster as built) and sends it back to them.
			// The host hits a warthog for 30%, "player 4242" (Raft's damage message from them) for the rest and kills it.
			AI_NetworkBehaviour shared = boars[2];
			int sharedWorth = PlayerLevels.MonsterXp(shared);
			float sharedMax = shared.networkEntity.stat_health.Max;
			var gainMsgs = new List<IslandNetMessage>();
			IslandNetwork.Loopback = m => { if (m.Kind == IslandNetMessage.Levels && m.Name == "gain") gainMsgs.Add(m); };
			int hostBefore = PlayerLevels.Mine.Xp, hostKills = PlayerLevels.Mine.Kills;
			host.DamageEntity(shared.networkEntity, shared.transform, sharedMax * 0.3f / pve, shared.transform.position, Vector3.up, EntityType.Player, null);
			int hostShare = PlayerLevels.Mine.Xp - hostBefore;
			for (int i = 0; i < 3 && !shared.networkEntity.IsDead; i++)
			{
				float hit = sharedMax * 0.3f;
				PlayerLevels.OnRemoteHit(shared.networkEntity, hit, 4242UL); // (as LevelRemoteHitPatch does, just before Raft applies it)
				shared.networkEntity.Damage(hit, shared.transform.position, Vector3.up, EntityType.Player, false);
				yield return new WaitForSeconds(0.2f);
			}
			IslandNetwork.Loopback = null;
			int remoteShare = gainMsgs.Sum(m => m.Count);
			Check(ref ok, shared.networkEntity.IsDead && gainMsgs.Count > 0 && gainMsgs.Last().FullList && !gainMsgs.Take(gainMsgs.Count - 1).Any(m => m.FullList) && gainMsgs.All(m => m.Index == unchecked((int)shared.networkEntity.ObjectIndex)),
				"another player's hits: the host sends them their EXP, the last one as the kill (" + string.Join(" + ", gainMsgs.Select(m => m.Count + (m.FullList ? " kill" : "")).ToArray()) + ")");
			Check(ref ok, hostShare > 0 && remoteShare > 0 && hostShare + remoteShare <= sharedWorth + 1 && hostShare + remoteShare >= sharedWorth - 1,
				"one warthog, two players: " + hostShare + " for the host + " + remoteShare + " for the other = " + (hostShare + remoteShare) + " of " + sharedWorth);
			Check(ref ok, PlayerLevels.Mine.Xp == hostBefore + hostShare && PlayerLevels.Mine.Kills == hostKills, "the other player's hits and kill give the host nothing");

			// The Damage stat: a hit takes 10% more with 10 points
			AI_NetworkBehaviour second = boars[1];
			PlayerLevels.SetMine(new LevelRecord { Xp = LevelRules.TotalFor(6), Points = new[] { 0, 0, 0, 0, 10, 0, 0, 0, 0 } });
			float hp0 = second.networkEntity.stat_health.Value;
			host.DamageEntity(second.networkEntity, second.transform, 10f / pve, second.transform.position, Vector3.up, EntityType.Player, null);
			float took = hp0 - second.networkEntity.stat_health.Value;
			Check(ref ok, Mathf.Abs(took - 11f) < 0.05f, "with 10 points in Damage a hit of 10 takes " + took.ToString("F2"));
			PlayerLevels.SetMine(new LevelRecord());

			// Level up: 99 EXP, then a hit's worth more
			PlayerLevels.SetMine(new LevelRecord { Xp = 99 });
			PlayerLevels.GiveXp(1, player.transform.position + player.transform.forward * 3f);
			Check(ref ok, PlayerLevels.Mine.Level == 2 && PlayerLevels.Mine.Unspent == 2, "100 EXP is level 2 with 2 stat points");
			Check(ref ok, LevelHud.LastAnnounce != null && LevelHud.LastAnnounce.StartsWith("LEVEL 2!") && LevelHud.LastAnnounce.Contains("Click here"), "a box: " + LevelHud.LastAnnounce);
			yield return new WaitForSeconds(1.2f);
			Screenshot(new[] { "levels_levelup" });
			yield return new WaitForSeconds(0.6f); // (the picture lands a few frames later; clicking the box below hides it)
			// The level bar next to Raft's health, thirst and hunger bars
			Check(ref ok, LevelHud.BarShown && LevelHud.BarText != null && LevelHud.BarText.StartsWith("LV 2") && LevelHud.BarPlace != null && LevelHud.BarPlace.Contains("Raft's 3 stat bars"),
				"the level bar sits by Raft's stat bars: \"" + LevelHud.BarText + "\", " + LevelHud.BarPlace);
			// The level up box opens the stats page when clicked (the mouse is free in a menu)
			Button box = LevelHud.BannerButton;
			Check(ref ok, box != null && box.interactable, "the level up box can be clicked");
			if (box != null) box.onClick.Invoke();
			for (int i = 0; i < 5 && !LevelWindow.IsOpen; i++) yield return null;
			Check(ref ok, LevelWindow.IsOpen, "clicking it opens the stats page");
			LevelWindow.Close();
			yield return null;
			// Raft's inventory (Tab) shows a Stats button by the level bar; it closes the inventory and opens the page
			CanvasHelper canvasHelper = ComponentManager<CanvasHelper>.Value ?? UnityEngine.Object.FindObjectOfType<CanvasHelper>();
			Check(ref ok, LevelHud.StatsButton != null && !LevelHud.StatsButton.gameObject.activeInHierarchy, "no Stats button while playing");
			if (canvasHelper != null) canvasHelper.OpenMenuCloseOther(MenuType.Inventory, true);
			for (int i = 0; i < 10 && (LevelHud.StatsButton == null || !LevelHud.StatsButton.gameObject.activeInHierarchy); i++) yield return null;
			Check(ref ok, CanvasHelper.ActiveMenu == MenuType.Inventory && LevelHud.StatsButton != null && LevelHud.StatsButton.gameObject.activeInHierarchy, "Raft's inventory open: the Stats button shows");
			yield return new WaitForSeconds(0.3f);
			Screenshot(new[] { "levels_inventory" });
			yield return new WaitForSeconds(0.5f);
			if (LevelHud.StatsButton != null) LevelHud.StatsButton.onClick.Invoke();
			for (int i = 0; i < 10 && !LevelWindow.IsOpen; i++) yield return null;
			// (with the page open RML's ToggleCursor marks "PauseMenu" as the active menu, to keep the game from taking the keys: no menu shows)
			Check(ref ok, LevelWindow.IsOpen && CanvasHelper.ActiveMenu != MenuType.Inventory, "the Stats button closes the inventory and opens the page (page " + (LevelWindow.IsOpen ? "open" : "closed") + ", Raft's menu " + CanvasHelper.ActiveMenu + ")");
			LevelWindow.Close();
			yield return null;

			// The stats page
			yield return new WaitForSeconds(0.4f);
			LevelWindow.Open();
			yield return null;
			Check(ref ok, LevelWindow.IsOpen && LevelWindow.Shown.StartsWith("LEVEL 2 |") && LevelWindow.Shown.Contains("2</color> stat point"), "the stats page opens: " + LevelWindow.Shown);
			Check(ref ok, LevelWindow.Click(LevelRules.Walk, true) && PlayerLevels.Mine.Points[LevelRules.Walk] == 1 && PlayerLevels.Mine.Unspent == 1, "+ puts a point into Walk speed");
			Check(ref ok, LevelWindow.Shown.Contains("Walk speed +1%"), "the page shows it: " + LevelWindow.Shown);
			Check(ref ok, LevelWindow.Click(LevelRules.Walk, false) && PlayerLevels.Mine.Points[LevelRules.Walk] == 0 && PlayerLevels.Mine.Unspent == 2, "- takes it back while the page is open");
			Check(ref ok, !LevelWindow.Click(LevelRules.Walk, false), "nothing to take back below what it had");
			LevelWindow.Click(LevelRules.Health, true);
			LevelWindow.Click(LevelRules.Oxygen, true);
			Check(ref ok, PlayerLevels.Mine.Unspent == 0 && !LevelWindow.Click(LevelRules.Swim, true), "no point left: + does nothing");
			yield return null;
			Screenshot(new[] { "levels_page" });
			yield return new WaitForSeconds(0.5f);
			LevelWindow.Close();
			LevelWindow.Open();
			Check(ref ok, !LevelWindow.Click(LevelRules.Health, false), "closed and opened again: the points stay");
			LevelWindow.Close();
			Check(ref ok, !LevelWindow.IsOpen, "the page closes");

			// At most 10 points in a stat
			PlayerLevels.SetMine(new LevelRecord { Xp = LevelRules.TotalFor(12) });
			for (int i = 0; i < 10; i++) PlayerLevels.Spend(LevelRules.Jump);
			Check(ref ok, PlayerLevels.Mine.Points[LevelRules.Jump] == 10 && !PlayerLevels.Spend(LevelRules.Jump) && PlayerLevels.Mine.Unspent == 12, "10 points in Jump height, not 11 (" + PlayerLevels.Mine.Unspent + " left for the others)");

			// Every stat full: still levelling up, without points
			PlayerLevels.SetMine(new LevelRecord { Xp = LevelRules.TotalFor(47) - 1, Points = Enumerable.Repeat(10, LevelRules.StatCount).ToArray(), Kills = 900 });
			Check(ref ok, PlayerLevels.Mine.Level == 46 && PlayerLevels.Mine.Unspent == 0 && PlayerLevels.Mine.AllFull, "level 46 with every stat full");
			PlayerLevels.GiveXp(1, null);
			Check(ref ok, PlayerLevels.Mine.Level == 47 && PlayerLevels.Mine.Unspent == 0 && LevelHud.LastAnnounce.StartsWith("LEVEL 47!") && LevelHud.LastAnnounce.Contains("no more points"),
				"level 47 still comes, with no points: " + LevelHud.LastAnnounce);
			LevelWindow.Open();
			yield return null;
			Check(ref ok, LevelWindow.Shown.StartsWith("LEVEL 47 | Every stat is full") && LevelWindow.Shown.Contains("Monsters defeated: 900") && LevelWindow.Shown.Contains("Thirst +10% max"), "the page says so, with the monsters defeated: " + LevelWindow.Shown);
			Screenshot(new[] { "levels_page_full" });
			yield return new WaitForSeconds(0.5f);
			LevelWindow.Close();

			// Every stat on Raft's player
			PlayerLevels.SetMine(new LevelRecord { Xp = LevelRules.TotalFor(46), Points = Enumerable.Repeat(10, LevelRules.StatCount).ToArray() });
			PersonController pc = player.PersonController;
			float walk = pc.normalSpeed, run = pc.sprintSpeed, swim = pc.swimSpeed, jump = pc.jumpSpeed;
			StatApply.Saved sv = StatApply.Boost(pc);
			Check(ref ok, Near(pc.normalSpeed, walk * 1.1f) && Near(pc.sprintSpeed, run * 1.1f) && Near(pc.swimSpeed, swim * 1.1f) && Near(pc.jumpSpeed * pc.jumpSpeed, jump * jump * 1.1f),
				"while Raft moves the player: walk " + walk.ToString("F2") + " -> " + pc.normalSpeed.ToString("F2") + ", run " + run.ToString("F2") + " -> " + pc.sprintSpeed.ToString("F2") + ", swim " + swim.ToString("F2") + " -> " + pc.swimSpeed.ToString("F2") +
				", jump " + jump.ToString("F2") + " -> " + pc.jumpSpeed.ToString("F2") + " m/s (10% higher)");
			StatApply.Restore(pc, sv);
			Check(ref ok, pc.normalSpeed == walk && pc.sprintSpeed == run && pc.swimSpeed == swim && pc.jumpSpeed == jump, "and Raft's own numbers after (Raft's flippers still change them as usual)");
			yield return new WaitForSeconds(0.6f);
			Stat_Health health = player.Stats.stat_health;
			float baseMax = StatApply.HealthBase;
			Check(ref ok, baseMax > 0f && Near(health.Max, baseMax * 1.1f), "maximum health " + baseMax.ToString("F0") + " -> " + health.Max.ToString("F1"));
			Stat_Consumable hunger = player.Stats.stat_hunger.normalConsumable;
			float hungerLost = hunger.LostPerSecond;
			PlayerLevels.Mine.Points[LevelRules.Hunger] = 0;
			float hungerRaft = hunger.LostPerSecond;
			Check(ref ok, hungerRaft > 0f && Near(hungerLost, hungerRaft / 1.1f), "hunger drains " + hungerRaft.ToString("F4") + " -> " + hungerLost.ToString("F4") + " a second (lasts 10% longer)");
			Stat_Consumable thirst = player.Stats.stat_thirst.normalConsumable;
			float thirstLost = thirst.LostPerSecond;
			PlayerLevels.Mine.Points[LevelRules.Thirst] = 0;
			float thirstRaft = thirst.LostPerSecond;
			Check(ref ok, thirstRaft > 0f && Near(thirstLost, thirstRaft / 1.1f), "thirst drains " + thirstRaft.ToString("F4") + " -> " + thirstLost.ToString("F4") + " a second (lasts 10% longer)");
			PlayerLevels.SetMine(new LevelRecord());
			yield return new WaitForSeconds(0.6f);
			Check(ref ok, Near(health.Max, baseMax) && health.Value <= baseMax, "no points: Raft's health again (" + health.Max.ToString("F0") + ")");
			Check(ref ok, Near(PlayerLevels.Factor(LevelRules.Oxygen), 1f), "no points: breath as Raft's");

			// Kept with the world; the host keeps the other players' records and gives them back
			PlayerLevels.SetMine(new LevelRecord { Xp = 345, Points = new[] { 1, 0, 0, 1, 0, 0, 0, 0, 0 }, Kills = 12 });
			PlayerLevels.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Levels, Name = "mine", Data = "1234|0,0,1,1,1,0,0,0,0|3" }, new Network_UserId(4242UL));
			LevelRecord guest = PlayerLevels.RecordOf(4242UL);
			Check(ref ok, guest != null && guest.Xp == 1234 && guest.Points[LevelRules.Damage] == 1 && guest.Kills == 3, "the host keeps another player's record");
			// An older message from that player (sent before EXP they earned since) arrives late: left out (EXP only grows)
			PlayerLevels.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Levels, Name = "mine", Data = "900|0,0,0,0,0,0,0,0,0|1" }, new Network_UserId(4242UL));
			guest = PlayerLevels.RecordOf(4242UL);
			Check(ref ok, guest != null && guest.Xp == 1234 && guest.Kills == 3, "a late, older record from that player doesn't take their progress away (" + (guest != null ? guest.Xp.ToString() : "none") + " EXP)");
			// Everyone's level goes to everyone (for the "Lv n" under each other player's name)
			var levelMsgs = new List<IslandNetMessage>();
			IslandNetwork.Loopback = m => { if (m.Kind == IslandNetMessage.Levels && m.Name == "levels") levelMsgs.Add(m); };
			yield return new WaitForSeconds(1.5f);
			IslandNetwork.Loopback = null;
			string list = levelMsgs.Count > 0 ? levelMsgs.Last().Data : "";
			Check(ref ok, list.Contains("4242=" + guest.Level) && list.Contains(me + "=" + PlayerLevels.Mine.Level) && PlayerLevels.LevelOf(4242UL) == guest.Level,
				"every player's level goes to everyone: " + list);
			IslandNetMessage back = PlayerLevels.StateFor(4242UL);
			Check(ref ok, back != null && back.Name == "state" && back.Count == 1 && back.Data == guest.Encode(), "and gives it back when they join");
			List<string> lines = PlayerLevels.WriteLines().ToList();
			Check(ref ok, lines.Contains("@levels=on") && lines.Contains("@level=" + me + "|345|1,0,0,1,0,0,0,0,0|12") && lines.Contains("@level=4242|1234|0,0,1,1,1,0,0,0,0|3"), "the world's lines: " + string.Join("  ", lines.ToArray()));
			IslandWorldState.Save();
			string worldFile = Directory.GetFiles(Path.Combine(DynamicIslands.assetpath, "worlds"), SaveAndLoad.WorldGuid + ".txt").FirstOrDefault();
			string[] saved = worldFile != null ? File.ReadAllLines(worldFile) : new string[0];
			Check(ref ok, saved.Contains("@levels=on") && saved.Contains("@level=" + me + "|345|1,0,0,1,0,0,0,0,0|12"), "saved in the world's island file");
			PlayerLevels.Reset();
			foreach (string line in lines) { int eq = line.IndexOf('='); PlayerLevels.ReadLine(line.Substring(1, eq - 1), line.Substring(eq + 1)); }
			Check(ref ok, PlayerLevels.On && PlayerLevels.Mine != null && PlayerLevels.Mine.Xp == 345 && PlayerLevels.Mine.Points[LevelRules.Jump] == 1 && PlayerLevels.RecordOf(4242UL) != null, "read back: on, level " + (PlayerLevels.Mine != null ? PlayerLevels.Mine.Level : 0));

			// K opens the page (Raft's own input, as a player presses it: the keys test does it with a real key)
			LevelCleanup(entry);
			IslandWorldState.Save();
			saved = worldFile != null && File.Exists(worldFile) ? File.ReadAllLines(worldFile) : new string[0];
			Check(ref ok, !PlayerLevels.On && !saved.Any(l => l.StartsWith("@level")), "turned off again for the test world");
			if (ok) Log("PASS: level up system in a world"); else Fail("level up system in a world");
		}

		static bool Near(float a, float b) { return Mathf.Abs(a - b) <= 0.002f * Mathf.Max(1f, Mathf.Abs(b)); }

		static void LevelCleanup(IslandWorldState.Entry entry)
		{
			IslandNetwork.Loopback = null;
			LevelWindow.Close();
			PlayerLevels.TurnOff();
			if (entry != null) IslandWorldState.RemoveIds(new[] { entry.Id }, true);
			if (File.Exists(IslandSpawner.PathFor(LevelIsland))) File.Delete(IslandSpawner.PathFor(LevelIsland));
		}

		[ConsoleCommand(name: "CILevelTable", docs: "Dev, in game: every monster Raft has - its health, damage and EXP (Bruce first), and how many kills of it make level 2; checks Bruce is worth " + "20" + " EXP")]
		public static void LevelTable()
		{
			Network_Host_Entities host = ComponentManager<Network_Host_Entities>.Value;
			if (host == null) { Fail("run in a world"); return; }
			bool ok = true;
			var rows = new List<string>();
			float sharkHp = -1f, sharkDmg = -1f;
			foreach (AI_NetworkBehavior_Shark s in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehavior_Shark>())
			{
				if (s == null || s.networkEntity == null || s.networkEntity.stat_health == null) continue;
				sharkHp = s.networkEntity.stat_health.Max;
				sharkDmg = PlayerLevels.DamageOf(s.gameObject, s.behaviourType);
				rows.Add(string.Format("  Shark (Bruce, in the sea): health {0:F0}, damage {1:F0}, {2} EXP", sharkHp, sharkDmg, LevelRules.XpOf(sharkHp, sharkDmg)));
				break;
			}
			foreach (AI_NetworkBehaviour a in host.AINetworkBehaviourPrefabs ?? new AI_NetworkBehaviour[0])
			{
				if (a == null) continue;
				Network_Entity ne = a.networkEntity != null ? a.networkEntity : a.GetComponentInChildren<Network_Entity>(true);
				// (a prefab's maximum is set only in Awake, from its serialized maxValue)
				float hp = ne != null && ne.stat_health != null ? ne.stat_health.Max : 0f, dmg = PlayerLevels.DamageOf(a.gameObject, a.behaviourType);
				if (hp <= 0f && ne != null && ne.stat_health != null) { FieldInfo mv = AccessTools.Field(typeof(Stat), "maxValue"); if (mv != null) hp = (float)mv.GetValue(ne.stat_health); }
				int xp = LevelRules.XpOf(hp, dmg);
				rows.Add(string.Format("  {0}: health {1:F0}, damage {2:F0}, {3}", a.behaviourType, hp, dmg, LevelRules.IsMonster(a.behaviourType) ? xp + " EXP, " + Mathf.CeilToInt(LevelRules.XpFor(1) / (float)xp) + " kills to level 2" : "not a monster"));
				if (a.behaviourType == AI_NetworkBehaviourType.Shark && sharkHp < 0f) { sharkHp = hp; sharkDmg = dmg; }
			}
			foreach (string r in rows) Log(r);
			Check(ref ok, sharkHp > 0f, "Bruce found (health " + sharkHp + ", damage " + sharkDmg + "; the rules use " + LevelRules.SharkHealth + " / " + LevelRules.SharkDamage + ")");
			if (sharkHp > 0f) Check(ref ok, Math.Abs(LevelRules.XpOf(sharkHp, sharkDmg) - LevelRules.ReferenceXp) <= 1, "Bruce is worth " + LevelRules.XpOf(sharkHp, sharkDmg) + " EXP (5 kills for level 2)");
			if (ok) Log("PASS: monster EXP table"); else Fail("monster EXP table");
		}

		#endregion

		#region Two players

		[ConsoleCommand(name: "CILevelMP", docs: "Dev, in game (host): spawns and keeps 'cilevelmp' 150 m ahead, an island with the level up system and harmless warthogs, for the two-player test (either player then CIHit cilevelmp Warthog, CILevelInfo)")]
		public static void LevelMP()
		{
			DynamicIslands.instance.StartCoroutine(LevelMPRoutine());
		}

		static IEnumerator LevelMPRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor("generated_sample"));
			f.Name = LevelMPIsland;
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 15f);
			f.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = new Vector3(c.x + 6f, f.Heights[Mathf.RoundToInt((c.y + 4f) / step), Mathf.RoundToInt((c.x + 6f) / step)] * f.TerrainSize.y, c.y + 4f), Props = P(ObjectProps.CreatureCount, "4", ObjectProps.CreatureDamage, "0") });
			f.Props[IslandProps.Levels] = "on";
			f.Save(IslandSpawner.PathFor(LevelMPIsland));
			IslandWorldState.Entry old = IslandWorldState.Islands.FirstOrDefault(e => e.HostName == LevelMPIsland);
			if (old != null) IslandWorldState.RemoveIds(new[] { old.Id }, true);
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius(LevelMPIsland), 150f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			yield return DynamicIslands.instance.SpawnIslandFile(LevelMPIsland, spot.Value, true);
			if (PlayerLevels.On) Log("PASS: '" + LevelMPIsland + "' spawned, the level up system is on");
			else Fail("'" + LevelMPIsland + "' did not turn the level up system on");
		}

		[ConsoleCommand(name: "CILevelInfo", docs: "Dev, in game (either player): the level up system here - on or off, this player's level, EXP and points; on the host also every player's record. CILevelInfo off = turn it off (host, tests)")]
		public static void LevelInfo(string[] args)
		{
			if (args != null && args.Length > 0 && args[0] == "off") { PlayerLevels.TurnOff(); Log("Levels turned off"); return; }
			LevelRecord r = PlayerLevels.Mine;
			Network_Player p = RAPI.GetLocalPlayer();
			Log("LEVELS " + (PlayerLevels.On ? "on" : "off") + (r != null ? ": level " + r.Level + ", xp " + r.Xp + ", points " + string.Join(",", r.Points.Select(x => x.ToString()).ToArray()) + ", unspent " + r.Unspent + ", kills " + r.Kills : "") +
				", tags [" + string.Join(" ", LevelTags.Shown.Select(kv => kv.Key + "=" + kv.Value.Replace(" ", "")).ToArray()) + "]" + ", bar " + (LevelHud.BarShown ? "shown" : "hidden") + ", stats button " + (LevelHud.StatsButton != null && LevelHud.StatsButton.gameObject.activeInHierarchy ? "shown" : "hidden") +
				(p != null && p.PersonController != null ? ", max health " + (p.Stats != null ? p.Stats.stat_health.Max.ToString("F1") : "?") : "") + ", stats page " + (LevelWindow.IsOpen ? "open" : "closed") + (Raft_Network.IsHost ? " (host)" : " (client)"));
			if (p != null)
				foreach (Network_Player other in UnityEngine.Object.FindObjectsOfType<Network_Player>().Where(x => x != null && x != p))
				{
					LevelRecord o = Raft_Network.IsHost ? PlayerLevels.RecordOf(other.steamID.Id) : null;
					Log("LEVELS player " + other.steamID.Id + ": " + (o != null ? "xp " + o.Xp + ", level " + o.Level + ", points " + string.Join(",", o.Points.Select(x => x.ToString()).ToArray()) : Raft_Network.IsHost ? "no record" : "level " + PlayerLevels.LevelOf(other.steamID.Id) + " (as the host sent it)") + ", max health here " + (other.Stats != null && other.Stats.stat_health != null ? other.Stats.stat_health.Max.ToString("F1") : "?"));
				}
		}

		[ConsoleCommand(name: "CILevelGive", docs: "Dev, in game (either player): gives this player EXP, as hits would: CILevelGive <exp>")]
		public static void LevelGive(string[] args)
		{
			int xp;
			if (args == null || args.Length == 0 || !int.TryParse(args[0], out xp) || xp <= 0) { Fail("CILevelGive <exp>"); return; }
			if (!PlayerLevels.On) { Fail("the level up system is off here"); return; }
			Network_Player p = RAPI.GetLocalPlayer();
			PlayerLevels.GiveXp(xp, p != null ? p.transform.position + p.transform.forward * 3f + Vector3.up : (Vector3?)null);
			Log("PASS: gave " + xp + " EXP: level " + PlayerLevels.Mine.Level + ", xp " + PlayerLevels.Mine.Xp);
		}

		[ConsoleCommand(name: "CILevelSpend", docs: "Dev, in game (either player): puts a stat point into a stat by its number (0 walk ... 7 oxygen) through the stats page, as clicking its +: CILevelSpend <stat>")]
		public static void LevelSpend(string[] args)
		{
			int stat;
			if (args == null || args.Length == 0 || !int.TryParse(args[0], out stat)) { Fail("CILevelSpend <0-7>"); return; }
			bool was = LevelWindow.IsOpen;
			LevelWindow.Open();
			bool done = LevelWindow.Click(stat, true);
			if (!was) LevelWindow.Close();
			if (done) Log("PASS: a point into " + LevelRules.StatNames[stat]); else Fail("no point to spend on " + stat);
		}

		#endregion
	}
}
