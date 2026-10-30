using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Bug hunt 2026-10-30: one test per fix (each failed before its fix).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIBhStableKeys", docs: "Dev: bug hunt 10-30 - an island edit carries a world's dug-for-good treasure (KeepBase), trade stand units and a vending machine's package to the same objects")]
		public static void BhStableKeysCommand()
		{
			bool ok = true;
			string tmp = Path.Combine(DynamicIslands.assetpath, "ci_bhstable.island");
			try
			{
				if (!PlaceableCatalog.IsBuilt) { Fail("bh stable keys: the object catalog isn't loaded yet"); return; }
				Func<string, float, IslandObject> obj = (n, x) => new IslandObject { Name = n, Position = new Vector3(x, 5, 0), EulerRotation = Vector3.zero, Scale = Vector3.one };
				var f1 = new IslandFile { Name = "ci_bhstable", TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
				f1.Objects.AddRange(new[] { obj("Creature_Boar", 0), obj(ContentCatalog.BuriedTreasure, 1), obj(ContentCatalog.BuriedTreasure, 2), obj("Loot_Chest", 3), obj("Loot_Chest", 4) });
				f1.Save(tmp);
				IslandFile a = IslandFile.Load(tmp);
				// The edit: a new treasure first - every object's place and the treasures' numbers move by one
				IslandFile b = IslandFile.Load(tmp);
				b.Objects.Insert(0, obj(ContentCatalog.BuriedTreasure, 9));
				StableIds.Prepare(b, a);
				b.Save(tmp);
				IslandFile c = IslandFile.Load(tmp);
				Func<bool, ObjectState> st = active => new ObjectState { Active = active, Yield = -1, Day = 3 };
				var was = new Dictionary<int, ObjectState>
				{
					{ BuriedTreasure.KeepBase + 1, st(false) },                      // the second treasure, dug for good -> the third now
					{ 0xC003, st(false) },                                           // its treasure point (ordinal 1) -> ordinal 2
					{ TradeStand.KeyBase + 3 * TradeStand.MaxStock + 1, st(false) }, // a stand at place 3, unit 1 -> place 4
					{ RaftVending.PackageKeyBase + 4, new ObjectState { Active = true, Yield = 3, Day = 3 } }, // a machine at place 4 -> 5
				};
				var expect = new Dictionary<int, ObjectState>
				{
					{ BuriedTreasure.KeepBase + 2, was[BuriedTreasure.KeepBase + 1] },
					{ 0xC005, was[0xC003] },
					{ TradeStand.KeyBase + 4 * TradeStand.MaxStock + 1, was[TradeStand.KeyBase + 3 * TradeStand.MaxStock + 1] },
					{ RaftVending.PackageKeyBase + 5, was[RaftVending.PackageKeyBase + 4] },
				};
				int dropped;
				string unsure;
				string moved = StableIds.Carry(a, c, IslandObjectState.Encode(was), false, out dropped, out unsure);
				Check(ref ok, moved != null && moved == IslandObjectState.Encode(expect) && dropped == 0,
					"dug treasure, trade units and a waiting package follow their objects (dropped " + dropped + (unsure != null ? ", unsure: " + unsure : "") + ")\n    got    " + moved + "\n    wanted " + IslandObjectState.Encode(expect));
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { try { File.Delete(tmp); } catch { } }
			if (ok) Log("PASS: bh stable keys"); else Fail("bh stable keys");
		}

		[ConsoleCommand(name: "CIBhArriveAgain", docs: "Dev, in game (host): bug hunt 10-30 - an island's arrive checks fail (no plank), the player leaves and comes back with one: it fires then, without the island loading again")]
		public static void BhArriveAgainCommand() { StartTest(BhArriveAgainRoutine()); }

		static IEnumerator BhArriveAgainRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (player == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			const string name = "cibharrive";
			PlayerInventory inv = player.Inventory;
			int planks = inv.GetItemCount("Plank");
			if (planks > 0) inv.RemoveItem("Plank", planks);
			try
			{
				var s = new IslandGenSettings { Seed = 3031, Radius = 30f, Height = 6f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
				IslandFile f = IslandGenerator.CreateFile(s, name);
				f.Props[BehaviourProps.EventKey("arrive")] = "message||Welcome, plank bearer";
				f.Props[BehaviourProps.CheckKey("arrive")] = "has|Plank|1";
				f.Props[BehaviourProps.ElseKey("arrive")] = "message||Come back with a plank";
				IslandWorldState.Remove(name);
				f.Save(IslandSpawner.PathFor(name));
				Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(name), 450f);
				if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
				yield return DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true);
				IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name);
				if (e == null || e.Root == null) { Fail(name + " did not spawn"); yield break; }
				GameObject root = e.Root;
				player.transform.position = LandSpot(e) + Vector3.up * 1.5f;
				yield return new WaitForSeconds(2f);
				Check(ref ok, Behaviours.LastMessage == "Come back with a plank", "arriving without a plank: the 'otherwise' message (" + Behaviours.LastMessage + ")");
				inv.AddItem("Plank", 1);
				// (away past the land, the island still loaded)
				float r = CustomIslandSpawner.LandRadius(name);
				Vector3 dir = e.Position - raft.Value; dir.y = 0f;
				Vector3 away = e.Position + (dir.sqrMagnitude > 1f ? dir.normalized : Vector3.forward) * (r + 110f);
				player.transform.position = new Vector3(away.x, 0.5f, away.z);
				yield return new WaitForSeconds(1.5f);
				player.transform.position = LandSpot(e) + Vector3.up * 1.5f;
				yield return new WaitForSeconds(2f);
				Check(ref ok, e.Root == root, "the island stayed loaded");
				Check(ref ok, Behaviours.LastMessage == "Welcome, plank bearer", "coming back with a plank: the arrive actions run (" + Behaviours.LastMessage + ")");
				Check(ref ok, e.State.ContainsKey(Behaviours.DoneBase + Behaviours.IslandIndex), "... and count as done");
				// (review 10-30 C: tried again only when the "otherwise" actions give nothing and change no shared state)
				Dictionary<string, string> props = IslandCache.PropsOf(e);
				string elseKey = BehaviourProps.ElseKey("arrive"), elseBefore = props[elseKey];
				Check(ref ok, Behaviours.ArriveMayRepeat(e), "a message otherwise: tried again");
				props[elseKey] = "message||Come back with a plank\nplaysound|Speaker|x";
				Check(ref ok, Behaviours.ArriveMayRepeat(e), "a message and a sound otherwise: tried again");
				props[elseKey] = "message||Come back with a plank\ngive||Plank";
				Check(ref ok, !Behaviours.ArriveMayRepeat(e), "a give otherwise: not tried again (it was farmable)");
				props[elseKey] = "toggle|Door";
				Check(ref ok, !Behaviours.ArriveMayRepeat(e), "a shared toggle otherwise: not tried again");
				props[elseKey] = elseBefore;
			}
			finally
			{
				OnRaftCommand();
				IslandWorldState.Remove(name);
				try { File.Delete(IslandSpawner.PathFor(name)); } catch { }
				int now = inv.GetItemCount("Plank");
				if (now > 0) inv.RemoveItem("Plank", now);
				if (planks > 0) inv.AddItem("Plank", planks);
			}
			if (ok) Log("PASS: bh arrive again"); else Fail("bh arrive again");
		}

		[ConsoleCommand(name: "CIBhHiddenKill", docs: "Dev, in game (host): bug hunt 10-30 - an animal killed in the second its spot is hidden still counts (it doesn't come back when the spot is shown)")]
		public static void BhHiddenKillCommand() { StartTest(BhHiddenKillRoutine()); }

		static IEnumerator BhHiddenKillRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			Network_Host host = ComponentManager<Network_Host>.Value;
			if (player == null || !raft.HasValue || !Raft_Network.IsHost || host == null) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			const string name = "cibhhidden";
			try
			{
				var s = new IslandGenSettings { Seed = 3032, Radius = 30f, Height = 6f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
				IslandFile f = IslandGenerator.CreateFile(s, name);
				Vector2 c = IslandSpawner.LandCentre(f);
				float step = f.TerrainSize.x / (f.HeightmapResolution - 1);
				Vector3 at = new Vector3(c.x + 4f, f.Heights[Mathf.RoundToInt(c.y / step), Mathf.RoundToInt((c.x + 4f) / step)] * f.TerrainSize.y, c.y);
				f.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = at, EulerRotation = Vector3.zero, Scale = Vector3.one, Props = P(ObjectProps.CreatureDamage, "0") });
				IslandWorldState.Remove(name);
				f.Save(IslandSpawner.PathFor(name));
				Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(name), 450f);
				if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
				yield return DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true);
				IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name);
				if (e == null || e.Root == null) { Fail(name + " did not spawn"); yield break; }
				player.transform.position = LandSpot(e) + Vector3.up * 1.5f;
				CreatureSpawnPoint p = e.Root.GetComponentsInChildren<CreatureSpawnPoint>(true).FirstOrDefault();
				AI_NetworkBehaviour boar = null;
				for (float until = Time.realtimeSinceStartup + 30f; p != null && Time.realtimeSinceStartup < until; )
				{
					boar = p.RecordedAlive > 0 ? p.Spawned.FirstOrDefault(a => a != null && a.networkEntity != null && !a.networkEntity.IsDead) : null;
					if (boar != null) break;
					yield return new WaitForSeconds(0.5f);
				}
				if (p == null || boar == null) { Fail("bh hidden kill: the boar didn't come"); yield break; }
				yield return new WaitForSeconds(1.2f); // (a look at the spot with the boar alive)
				Check(ref ok, !e.State.ContainsKey(p.StateKey), "the spot is full before the kill");
				// The kill and the hide in one frame (as a zone's "hide" right after a hit)
				host.DamageEntity(boar.networkEntity, boar.transform, 9999f, boar.transform.position + Vector3.up, Vector3.up, EntityType.Player, null);
				p.gameObject.SetActive(false);
				yield return new WaitForSeconds(2.5f);
				ObjectState st;
				Check(ref ok, e.State.TryGetValue(p.StateKey, out st) && st.Yield == 0, "the kill counted although the spot was hidden at once (" + (e.State.ContainsKey(p.StateKey) ? "yield " + e.State[p.StateKey].Yield : "no record: it would come back") + ")");
			}
			finally
			{
				OnRaftCommand();
				IslandWorldState.Remove(name);
				try { File.Delete(IslandSpawner.PathFor(name)); } catch { }
			}
			if (ok) Log("PASS: bh hidden kill"); else Fail("bh hidden kill");
		}

		[ConsoleCommand(name: "CIBhMissingAsk", docs: "Dev, world (host, 'CI ...'): bug hunt 10-30 - a story island missing while the host is still asked about it (box open): its step waits; after 'Play without them' the story goes on. Also: StoryChain.Reset forgets the last world's failed makes")]
		public static void BhMissingAskCommand() { StartTest(BhMissingAskRoutine()); }

		static IEnumerator BhMissingAskRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("bh missing ask: host, in a world"); yield break; }
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("bh missing ask: only in a test world 'CI ...'"); yield break; }
			bool ok = true;
			List<string> linesBefore = StoryChain.WriteLines().ToList();
			var unlockedBefore = NoteBook.unlockedChunkPointType.ToList();
			var indexesBefore = NoteBook.unlockedNoteBookIndexes.ToList();
			string planBefore = WorldDirector.PlanName;
			var doneBefore = WorldDirector.Done.ToList();
			var lastBefore = MissingIslandsCheck.Last.ToList();
			bool withoutBefore = StoryChain.PlayingWithoutMissing;
			const string gone = "cibhgonechain";
			try
			{
				Dictionary<int, ChunkPointType> notes = StoryOrder.FrequencyNotes();
				// (Reset: the last world's failed makes and moved frequencies are forgotten)
				var failed = (Dictionary<string, int>)typeof(StoryChain).GetField("failedMaking", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
				var moved = (HashSet<string>)typeof(StoryChain).GetField("Moved", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
				failed["cibhx"] = 2;
				moved.Add("cibhx");
				NoteBook.unlockedNoteBookIndexes.RemoveAll(i => notes.ContainsKey(i));
				NoteBook.unlockedChunkPointType.RemoveAll(t => Chain.Contains(t) || (int)t >= StoryChain.ModTypeBase);
				StoryChain.Reset();
				Check(ref ok, !failed.ContainsKey("cibhx") && !moved.Contains("cibhx"), "Reset forgets the last world's failed makes and moved frequencies");

				if (File.Exists(IslandSpawner.PathFor(gone))) File.Delete(IslandSpawner.PathFor(gone));
				StoryChain.FromPlan(WorldPlan.Parse("CI bh ask", "story = on\nrule = h | island:" + gone + " | start | receiver:400 | | Harbor | instead:Vasagatan | visit\n"));
				// The missing-islands box is open, not answered yet
				MissingIslandsCheck.Last.Clear();
				MissingIslandsCheck.Last.Add(new MissingIslandsCheck.Missing { Name = gone, ByPlan = true });
				MissingIslandsCheck.Show();
				Check(ref ok, MissingIslandsCheck.Asking, "the box asks");
				PlayNote(ChunkPointType.Landmark_RadioTower);
				PlayNote(ChunkPointType.Landmark_Vasagatan); // (the Radio Tower's note: the harbor's step)
				StoryChain.Tick();
				yield return new WaitForSeconds(1f);
				int type = StoryChain.TypeOfRule("h");
				ChunkManager cm = ComponentManager<ChunkManager>.Value;
				if (cm != null && type >= StoryChain.ModTypeBase) cm.AddChunkPointForcibly((ChunkPointType)type);
				yield return new WaitForSeconds(3f);
				StoryChain.Tick();
				bool wentOn = ChainTypes().Contains(ChunkPointType.Landmark_CaravanIsland) || ChainTypes().Contains(ChunkPointType.Landmark_Vasagatan) || StoryChain.Done.Contains("rule:h");
				Check(ref ok, !wentOn, "while the host is asked, the missing island's step waits (" + Names(ChainTypes()) + ") - 'Main menu' would come back to it");
				MissingIslandsCheck.PlayWithout();
				Check(ref ok, !MissingIslandsCheck.Asking, "answered: no longer asking");
				yield return new WaitForSeconds(2.5f);
				StoryChain.Tick();
				yield return new WaitForSeconds(0.5f);
				StoryChain.Tick();
				Check(ref ok, ChainTypes().Contains(ChunkPointType.Landmark_CaravanIsland) || ChainTypes().Contains(ChunkPointType.Landmark_Vasagatan),
					"after 'Play without them' the story goes on (Raft's Vasagatan instead, or the next island): " + Names(ChainTypes()));
				// (review 10-30 A: "Main menu" (leaving) held the story for the rest of the session when the next world never
				// reached OnWorldRead - a new world, a client, a file that failed to read)
				FieldInfo leavingField = typeof(MissingIslandsCheck).GetField("leaving", BindingFlags.NonPublic | BindingFlags.Static);
				leavingField.SetValue(null, true);
				Check(ref ok, MissingIslandsCheck.Asking, "'Main menu' leaving: asking");
				MissingIslandsCheck.Tick();
				Check(ref ok, MissingIslandsCheck.Asking, "... still in the world: still asking");
				MissingIslandsCheck.Forget(); // (IslandWorldState.OnWorldLoaded, before its early return)
				Check(ref ok, !MissingIslandsCheck.Asking && !(bool)leavingField.GetValue(null), "the next world loading: no longer asking");
			}
			finally
			{
				if (MissingIslandsCheck.Asking) InfoWindow.Close();
				MissingIslandsCheck.Forget();
				MissingIslandsCheck.Last.Clear(); MissingIslandsCheck.Last.AddRange(lastBefore);
				StoryChain.Reset();
				foreach (string l in linesBefore) { int eq = l.IndexOf('='); StoryChain.ReadLine(l.Substring(1, eq - 1), l.Substring(eq + 1)); }
				NoteBook.unlockedNoteBookIndexes.Clear(); NoteBook.unlockedNoteBookIndexes.AddRange(indexesBefore);
				NoteBook.unlockedChunkPointType.Clear(); NoteBook.unlockedChunkPointType.AddRange(unlockedBefore);
				WorldDirector.Done.Clear();
				foreach (string d in doneBefore) WorldDirector.Done.Add(d);
				if (!WorldDirector.SetPlan(planBefore, false)) WorldDirector.SetPlan(WorldPlan.RandomName, false);
				StoryChain.OnWorldRead();
				StoryChain.PlayingWithoutMissing = withoutBefore;
				IslandWorldState.Save();
			}
			if (ok) Log("PASS: bh missing ask"); else Fail("bh missing ask");
		}
	}
}
