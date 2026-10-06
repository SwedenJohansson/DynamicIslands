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
	/// <summary>Raft's doors, hatches, crank wheels and levers as ready pieces (ReadyPieces), and the plan Check's story item order.</summary>
	public static partial class DevTests
	{
		const string ReadyIsland = "cireadypieces";

		[ConsoleCommand(name: "CIReadyPieces", docs: "Dev, in game (host): Raft's ready pieces on a custom island - a Tangaroa keycard door (stays shut without the keycard, goes with it, the keycard kept), a hatch (opens), a crank wheel and a lever (send the signals crank and lever)")]
		public static void ReadyPiecesCommand(string[] args) { DynamicIslands.instance.StartCoroutine(ReadyPiecesRoutine()); }

		static IEnumerator ReadyPiecesRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (player == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			var pieces = new[] { new[] { "PlantationDoor", "door" }, new[] { "TangaroaHatchRoom_Hatch", "hatch" }, new[] { "TP_Selene_DoorCrankWheel", "crank" }, new[] { "TP_CoolingStation_Monitor_Lever", "lever" } };
			yield return PlaceableCatalog.EnsureLoaded(ReadyPieces.All.Select(p => p.Name).ToList());
			Check(ref ok, ReadyPieces.All.All(p => PlaceableCatalog.Get(p.Name) != null), "every ready piece loads from Raft's islands" + string.Join("", ReadyPieces.All.Where(p => PlaceableCatalog.Get(p.Name) == null).Select(p => ", " + p.Name + " missing").ToArray()));

			var s = new IslandGenSettings { Seed = 8383, Radius = 50f, Height = 14f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, ReadyIsland);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Vector2 c = IslandSpawner.LandCentre(f);
			Func<float, float, float> ground = (x, z) => IslandGenerator.SampleHeights(f.Heights, res, step, x, z) * f.TerrainSize.y;
			for (int i = 0; i < pieces.Length; i++)
			{
				Dictionary<string, string> p = ObjectProps.Defaults(pieces[i][0]);
				p[BehaviourProps.Name] = pieces[i][1];
				float x = c.x - 6f + i * 4f;
				f.Objects.Add(new IslandObject { Name = pieces[i][0], Position = new Vector3(x, ground(x, c.y), c.y), Props = p });
			}
			IslandWorldState.Remove(ReadyIsland);
			f.Save(IslandSpawner.PathFor(ReadyIsland));
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(ReadyIsland), 450f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			yield return DynamicIslands.instance.SpawnIslandFile(ReadyIsland, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == ReadyIsland);
			if (e == null || e.Root == null) { Fail(ReadyIsland + " did not spawn"); yield break; }
			yield return new WaitForSeconds(1f);
			int missing = e.Root.GetComponentsInChildren<Transform>(true).Count(t => t.name.Contains(IslandSpawner.MissingTag));
			Check(ref ok, missing == 0, "the pieces spawn (" + missing + " missing)");

			// The keycard door
			string card = StoryItems.Ref(ReadyPieces.ItemOf("PlantationDoor"));
			StoryBook.Take(card, 99);
			IslandObjectRef door = ScObjOf(e, "door");
			Check(ref ok, door != null, "the keycard door is on the island");
			if (door != null)
			{
				ScUse(e, "door");
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, door.gameObject.activeInHierarchy, "without the keycard it stays shut");
				StoryBook.Give(card, 1);
				ScUse(e, "door");
				yield return new WaitForSeconds(0.8f);
				Check(ref ok, !door.gameObject.activeInHierarchy, "with Raft's Tangaroa keycard (" + card + ") it opens");
				Check(ref ok, StoryBook.Count(card) == 1, "the crew keeps the keycard for the next door (" + StoryBook.Count(card) + ")");
			}
			IslandObjectRef hatch = ScObjOf(e, "hatch");
			if (hatch != null) { ScUse(e, "hatch"); yield return new WaitForSeconds(0.6f); }
			Check(ref ok, hatch != null && !hatch.gameObject.activeInHierarchy, "the hatch opens when used");
			foreach (string sig in new[] { "crank", "lever" })
			{
				ScUse(e, sig);
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, e.State.ContainsKey(Behaviours.SignalKey(sig)), "the " + sig + " sends the signal '" + sig + "'");
			}
			StoryBook.Take(card, 99);
			if (ok) Log("PASS: ready pieces"); else Fail("ready pieces");
		}

		[ConsoleCommand(name: "CIStoryOrderCheck", docs: "Dev, anywhere: the plan Check's story item order - an island with a keycard door brought before the island that gives the keycard is warned about, after it is fine, and with no giver at all the warning says so")]
		public static void StoryOrderCheckCommand(string[] args)
		{
			bool ok = true;
			const string lockIsland = "citest-so-lock", keyIsland = "citest-so-key";
			string card = StoryItems.Ref(ReadyPieces.ItemOf("PlantationDoor"));
			try
			{
				string source = IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == "Crowfield Farm") ?? IslandSpawner.ListSavedIslands().First(n => !n.StartsWith("ci") && !IslandNetwork.IsDownloadName(n));
				IslandFile lf = IslandFile.Load(IslandSpawner.PathFor(source));
				lf.Objects.Clear(); lf.Props = new Dictionary<string, string>();
				lf.Objects.Add(new IslandObject { Name = "PlantationDoor", Position = Vector3.zero, Props = ObjectProps.Defaults("PlantationDoor") });
				IslandFile kf = IslandFile.Load(IslandSpawner.PathFor(source));
				kf.Objects.Clear(); kf.Props = new Dictionary<string, string>();
				kf.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = Vector3.zero, Props = new Dictionary<string, string> { { ObjectProps.LootItems, card + "*1;Plank*4" } } });
				lf.Save(IslandSpawner.PathFor(lockIsland));
				kf.Save(IslandSpawner.PathFor(keyIsland));
				PlanChecker.Facts lockFacts = PlanChecker.FromFile(lf, lockIsland, false), keyFacts = PlanChecker.FromFile(kf, keyIsland, false);
				Check(ref ok, lockFacts.NeedsStory.Contains(StoryItems.IdOf(card)) && keyFacts.GivesStory.Contains(StoryItems.IdOf(card)), "the door wants the keycard, the chest gives it");
				Func<string[], List<PlanChecker.Finding>> check = order =>
				{
					var plan = new WorldPlan { Name = "CI Story Order", Random = false };
					for (int i = 0; i < order.Length; i++) plan.Rules.Add(new IntroRule { Id = "r" + i, What = "island", WhatArg = order[i], When = "start" });
					return PlanChecker.Check(plan, false, true);
				};
				Func<List<PlanChecker.Finding>, string> warn = fs => string.Join(" / ", fs.Where(x => x.Text.Contains("story item")).Select(x => x.Text).ToArray());
				string early = warn(check(new[] { lockIsland, keyIsland })), late = warn(check(new[] { keyIsland, lockIsland })), none = warn(check(new[] { lockIsland }));
				Check(ref ok, early.Contains("later rule"), "the lock before the key: warned (" + early + ")");
				Check(ref ok, late.Length == 0, "the key before the lock: fine (" + late + ")");
				Check(ref ok, none.Contains("no island of the plan gives"), "no key anywhere: warned (" + none + ")");
			}
			catch (Exception ex) { Check(ref ok, false, "no errors: " + ex); }
			finally
			{
				foreach (string n in new[] { lockIsland, keyIsland }) try { if (System.IO.File.Exists(IslandSpawner.PathFor(n))) System.IO.File.Delete(IslandSpawner.PathFor(n)); } catch { }
			}
			if (ok) Log("PASS: story item order check"); else Fail("story item order check");
		}
	}
}
