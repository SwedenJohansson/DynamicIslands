using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Issue sweep 2026-11-14: tests for the fixes of the sweep's open items.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CILibCarryOlder", docs: "Dev: sweep 11-14 #1 - a library update also moves the worlds that play a kept name_hash copy of the island to the new version")]
		public static void LibCarryOlderCommand()
		{
			bool ok = true;
			string isl = "ci_libcarry_" + DateTime.Now.Ticks;
			string folder = Path.Combine(DynamicIslands.assetpath, "worlds");
			string world = Path.Combine(folder, isl + ".txt");
			var cleanup = new List<string> { IslandSpawner.PathFor(isl), world };
			try
			{
				if (!PlaceableCatalog.IsBuilt) { Fail("lib carry: the object catalog isn't loaded yet"); return; }
				Func<string, float, IslandObject> obj = (n, x) => new IslandObject { Name = n, Position = new Vector3(x, 5, 0), EulerRotation = Vector3.zero, Scale = Vector3.one };

				// Version 1, kept as the copy a world plays
				var f1 = new IslandFile { Name = isl, TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
				f1.Objects.AddRange(new[] { obj("Loot_Chest", 0), obj("Loot_Chest", 1) });
				f1.Save(IslandSpawner.PathFor(isl));
				IslandFile a = IslandFile.Load(IslandSpawner.PathFor(isl));
				string h0 = IslandNetwork.HashOf(isl);
				string copy = IslandSpawner.PathFor(IslandNetwork.DownloadName(isl, h0));
				File.Copy(IslandSpawner.PathFor(isl), copy, true);
				cleanup.Add(copy);
				cleanup.Add(Path.Combine(Path.Combine(Path.Combine(DynamicIslands.assetpath, IslandFilesWindow.DeletedFolderName), LibraryPack.KeptVersionsFolder), Path.GetFileName(copy)));

				// Version 2 (what a library update installs): one object put first
				IslandFile b = IslandFile.Load(IslandSpawner.PathFor(isl));
				b.Objects.Insert(0, obj("Loot_Chest", 9));
				StableIds.Prepare(b, a);
				b.Save(IslandSpawner.PathFor(isl));
				File.SetLastWriteTimeUtc(IslandSpawner.PathFor(isl), DateTime.UtcNow.AddSeconds(2)); // (HashOf caches by write time)
				string h1 = IslandNetwork.HashOf(isl);

				File.WriteAllLines(world, new[]
				{
					"# Custom islands in world 'CI lib carry': name|x|y|z|used objects|rule|receiver label|island file hash",
					"@auto=on",
					isl + "|0|0|0||||" + h0,
				});
				Check(ref ok, LibraryPack.WorldsOnOlderVersion(isl).Count == 1, "the world plays the kept copy before the update");

				var report = new LibraryPack.Report();
				int moved = LibraryPack.CarryOlderWorlds(isl, report);
				string[] back = File.ReadAllLines(world);
				string line = back.FirstOrDefault(l => l.StartsWith(isl + "|"));
				Check(ref ok, moved == 1, "one world was moved (" + moved + ")");
				Check(ref ok, line != null && line.EndsWith("|" + h1), "the world plays the new file: " + line);
				Check(ref ok, LibraryPack.WorldsOnOlderVersion(isl).Count == 0, "no world is left on the old copy");
				Check(ref ok, report.Lines.Any(l => l.Contains(isl)), "the install report says so");
				Check(ref ok, LibraryPack.CarryOlderWorlds(isl, null) == 0, "a second run changes nothing");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { foreach (string p in cleanup) try { if (File.Exists(p)) File.Delete(p); } catch { } IslandCache.Forget(); }
			if (ok) Log("PASS: library update carries kept copies (sweep #1)"); else Fail("library update carries kept copies (sweep #1)");
		}

		[ConsoleCommand(name: "CISpawnKnown", docs: "Dev: sweep 11-14 #5 - SpawnIsland of an island without a story rule that this world has already: no second copy; loaded it stays, left far behind it comes again as it was")]
		public static void SpawnKnownCommand(string[] args) { StartTest(SpawnKnownRoutine()); }

		static IEnumerator SpawnKnownRoutine()
		{
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (RAPI.GetLocalPlayer() == null || !raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			const string name = "cispawnknown";
			var s = new IslandGenSettings { Seed = 8997, Radius = 25f, Height = 5f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandGenerator.CreateFile(s, name).Save(IslandSpawner.PathFor(name));
			IslandWorldState.Remove(name);
			Func<int> copies = () => IslandWorldState.Islands.Count(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			try
			{
				DynamicIslands.SpawnIslandCommand(new[] { name, "250" });
				IslandWorldState.Entry e = null;
				for (float t = 0f; t < 40f && (e == null || e.Root == null); t += 0.5f) { e = CustomIslandSpawner.InWorld(name); yield return new WaitForSeconds(0.5f); }
				if (e == null || e.Root == null) { Fail("the island did not come"); yield break; }
				Check(ref ok, copies() == 1 && string.IsNullOrEmpty(e.Rule), "set up: one rule-less copy, loaded");

				// a) loaded: a second SpawnIsland adds nothing and moves nothing
				Vector3 pos = e.Position;
				DynamicIslands.SpawnIslandCommand(new[] { name.ToUpperInvariant(), "250" });
				yield return new WaitForSeconds(1f);
				Check(ref ok, copies() == 1 && e.Position == pos, "loaded: SpawnIsland adds no second copy (" + copies() + ")");

				// b) left far behind and unloaded: it comes again (the same entry, not counted as a return)
				Vector3 sail = CustomIslandSpawner.SailDirection();
				IslandObjectState.Capture(e);
				IslandSpawner.Despawn(e.Root);
				e.Root = null;
				e.Position = raft.Value - new Vector3(sail.x, 0f, sail.z).normalized * 4000f;
				Vector3 far = e.Position;
				int returns0 = ReturningIslands.Returns(e);
				DynamicIslands.handBrought.Remove(e.Id);
				DynamicIslands.SpawnIslandCommand(new[] { name });
				float away = new Vector2(e.Position.x - raft.Value.x, e.Position.z - raft.Value.z).magnitude;
				Check(ref ok, copies() == 1 && e.Position != far && away < 600f && ReturningIslands.Returns(e) == returns0,
					"unloaded far: the same copy comes again ahead (" + Gf(away, "F0") + " m), no second one (" + copies() + "), not counted");
				// c) twice in a row: the second is refused while the first is on its way
				Vector3 brought = e.Position;
				DynamicIslands.SpawnIslandCommand(new[] { name });
				Check(ref ok, copies() == 1 && e.Position == brought, "a second SpawnIsland right after: nothing moved or added");
			}
			finally
			{
				DynamicIslands.handBrought.Clear();
				IslandWorldState.Remove(name);
				try { File.Delete(IslandSpawner.PathFor(name)); } catch { }
			}
			if (ok) Log("PASS: spawn known island (sweep #5)"); else Fail("spawn known island (sweep #5)");
		}

		/// <summary>Sets a static bool of a Raft class (a field, a property's setter or its backing field); false if none.</summary>
		static bool SetStaticBool(Type t, string name, bool v)
		{
			System.Reflection.FieldInfo f = HarmonyLib.AccessTools.Field(t, name) ?? HarmonyLib.AccessTools.Field(t, "<" + name + ">k__BackingField");
			if (f != null && f.IsStatic) { f.SetValue(null, v); return true; }
			System.Reflection.PropertyInfo p = HarmonyLib.AccessTools.Property(t, name);
			System.Reflection.MethodInfo set = p != null ? p.GetSetMethod(true) : null;
			if (set != null && set.IsStatic) { set.Invoke(null, new object[] { v }); return true; }
			return false;
		}

		[ConsoleCommand(name: "CISweepSave", docs: "Dev, world (host): sweep 11-14 #7 - the island list isn't saved by the mod's own code while the world is left; a full journal makes room for a note by dropping its oldest popup page")]
		public static void SweepSaveCommand()
		{
			bool ok = true;
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("sweep save: run in a world, as the host"); return; }
			string file = HarmonyLib.Traverse.Create(typeof(IslandWorldState)).Property("FilePath").GetValue<string>();
			var backup = StoryBook.Pages.ToList();
			try
			{
				// 1. Leaving the world: Save() writes nothing; else it writes
				IslandWorldState.Save();
				Check(ref ok, file != null && File.Exists(file), "set up: the world's island file is there (" + file + ")");
				DateTime old = DateTime.UtcNow.AddHours(-1);
				File.SetLastWriteTimeUtc(file, old);
				bool set = SetStaticBool(typeof(GameManager), "IsLeavingGame", true);
				Check(ref ok, set && GameManager.IsLeavingGame, "set up: GameManager.IsLeavingGame can be set by the test");
				try { IslandWorldState.Save(); }
				finally { SetStaticBool(typeof(GameManager), "IsLeavingGame", false); }
				Check(ref ok, File.GetLastWriteTimeUtc(file) == old, "while the world is left: not written");
				WorldCopy.InRaftSave = true;
				SetStaticBool(typeof(GameManager), "IsLeavingGame", true);
				try { IslandWorldState.Save(); }
				finally { SetStaticBool(typeof(GameManager), "IsLeavingGame", false); WorldCopy.InRaftSave = false; }
				Check(ref ok, File.GetLastWriteTimeUtc(file) != old, "Raft's own save while leaving: written (SaveGuard vets that one)");
				File.SetLastWriteTimeUtc(file, old);
				IslandWorldState.Save();
				Check(ref ok, !GameManager.IsLeavingGame && File.GetLastWriteTimeUtc(file) != old, "in play: written");

				// 2. A full journal: a note drops the oldest popup page; a popup page is left out
				StoryBook.Pages.Clear();
				StoryBook.Pages.Add(new StoryBook.Page { Key = "note:cisweep:0", Title = "first note" });
				for (int i = 1; i < StoryBook.MaxPages; i++) StoryBook.Pages.Add(new StoryBook.Page { Key = "msg:cisweep:" + i.ToString("x8"), Title = "popup " + i });
				StoryBook.AddPage("note:cisweep:1", "new note", "text", "Island");
				Check(ref ok, StoryBook.Pages.Count == StoryBook.MaxPages && StoryBook.Pages.Any(p => p.Key == "note:cisweep:1") && StoryBook.Pages[0].Key == "note:cisweep:0" && !StoryBook.Pages.Any(p => p.Key == "msg:cisweep:00000001"),
					"full: a new note is added, the oldest popup page makes room, the old note stays (" + StoryBook.Pages.Count + " pages)");
				StoryBook.AddPage("msg:cisweep:ffffffff", "new popup", "text", "Island");
				Check(ref ok, StoryBook.Pages.Count == StoryBook.MaxPages && !StoryBook.Pages.Any(p => p.Key == "msg:cisweep:ffffffff"), "full: a new popup page is left out");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally
			{
				SetStaticBool(typeof(GameManager), "IsLeavingGame", false);
				WorldCopy.InRaftSave = false;
				StoryBook.Pages.Clear();
				foreach (StoryBook.Page p in backup) StoryBook.Pages.Add(p);
			}
			if (ok) Log("PASS: sweep save guard + journal room (sweep #7)"); else Fail("sweep save guard + journal room (sweep #7)");
		}

		[ConsoleCommand(name: "CIMpTrust", docs: "Dev, world (host): sweep 11-14 #6 - a player takes the two-way kinds (claims, Kraken, mechanisms, story, levels, upgrade table) only from the host; a claim grant runs out; a research item comes back on a refusal or no answer, only when one waits; taming food is taken as the feed goes out and comes back on a no")]
		public static void MpTrustCommand(string[] args) { StartTest(MpTrustRoutine()); }

		static IEnumerator MpTrustRoutine()
		{
			bool ok = true;
			Network_Player me = RAPI.GetLocalPlayer();
			if (me == null) { Fail("mp trust: not in a world"); yield break; }

			// 1. Two-way kinds: a player drops them from anyone but the host; the host takes them from all
			ulong was = IslandNetwork.HostIdOverride;
			IslandNetwork.HostIdOverride = 111UL;
			try
			{
				foreach (int kind in new[] { IslandNetMessage.Claim, IslandNetMessage.Kraken, IslandNetMessage.Mech, IslandNetMessage.Story, IslandNetMessage.Levels, IslandNetMessage.UpgradeTable })
				{
					var m = new IslandNetMessage { Kind = kind };
					Check(ref ok, IslandNetwork.FromHostOnPlayers.Contains(kind), "kind " + kind + " is a two-way kind");
					Check(ref ok, IslandNetwork.NotFromHost(m, 222UL, false), "kind " + kind + " from another player: dropped");
					Check(ref ok, !IslandNetwork.NotFromHost(m, 111UL, false), "kind " + kind + " from the host: taken");
					Check(ref ok, !IslandNetwork.NotFromHost(m, 222UL, true), "kind " + kind + " on the host: taken from a player");
				}
				Check(ref ok, !IslandNetwork.NotFromHost(new IslandNetMessage { Kind = IslandNetMessage.Claim }, 0UL, false), "unknown sender: passes");
			}
			finally { IslandNetwork.HostIdOverride = was; }

			// 2. A claim grant is used within its hold; one left unused runs out (asked again)
			Claims.TestGrant(987654, 5, 0.5f);
			Check(ref ok, Claims.TakeGrant(Claims.KeyOf(987654, 5)), "a fresh grant is used");
			Check(ref ok, !Claims.TakeGrant(Claims.KeyOf(987654, 5)), "a grant is used once");
			Claims.TestGrant(987654, 6, 0.5f);
			yield return new WaitForSecondsRealtime(1f);
			Check(ref ok, !Claims.TakeGrant(Claims.KeyOf(987654, 6)), "a grant left unused past its hold runs out");

			// 3. Research items: back only when a research of them waits; back on no answer
			Item_Base plank = ItemManager.GetItemByName("Plank");
			if (plank == null) { Fail("mp trust: no Plank item"); yield break; }
			int had = me.Inventory.GetItemCount("Plank");
			Check(ref ok, !global::DynamicIslands.Editor.UpgradeTable.GiveBack(plank.UniqueIndex, "test") && me.Inventory.GetItemCount("Plank") == had, "a refund with no research waiting gives nothing");
			global::DynamicIslands.Editor.UpgradeTable.PendForTest(plank.UniqueIndex, 0f);
			Check(ref ok, global::DynamicIslands.Editor.UpgradeTable.GiveBack(plank.UniqueIndex, "test") && me.Inventory.GetItemCount("Plank") == had + 1, "a refused research gives its item back");
			Check(ref ok, !global::DynamicIslands.Editor.UpgradeTable.GiveBack(plank.UniqueIndex, "test") && me.Inventory.GetItemCount("Plank") == had + 1, "only once");
			global::DynamicIslands.Editor.UpgradeTable.PendForTest(plank.UniqueIndex, 1f);
			yield return new WaitForSecondsRealtime(2f);
			Check(ref ok, me.Inventory.GetItemCount("Plank") == had + 2 && global::DynamicIslands.Editor.UpgradeTable.PendingResearchCount == 0, "no answer from the host: the item comes back (" + me.Inventory.GetItemCount("Plank") + ", had " + had + ")");

			// 4. Taming food: taken as the feed goes out, back on a no, kept on a yes, no feed without it
			var sent = new List<IslandNetMessage>();
			var e = new IslandWorldState.Entry { Id = 987655, Name = "ci_mptrust", HostName = "ci_mptrust" };
			string who = MechFeatures.LocalId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|Goat";
			int start = me.Inventory.GetItemCount("Plank");
			IslandNetwork.Loopback = m => sent.Add(m);
			try
			{
				Taming.SendFeed(e, 3, me.transform.position, "Plank");
				Check(ref ok, me.Inventory.GetItemCount("Plank") == start - 1 && sent.Count(m => m.Kind == IslandNetMessage.Mech && m.Name == "feed") == 1, "a feed takes its food as it goes out");
				Taming.OnTamed(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "tamed", Count = 0, Data = who });
				Check(ref ok, me.Inventory.GetItemCount("Plank") == start, "the host's no gives the food back");
				Taming.SendFeed(e, 3, me.transform.position, "Plank");
				Taming.OnTamed(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "tamed", Count = 1, Data = who });
				Check(ref ok, me.Inventory.GetItemCount("Plank") == start - 1, "the host's yes keeps it taken");
				me.Inventory.RemoveItem("Plank", me.Inventory.GetItemCount("Plank"));
				sent.Clear();
				Taming.SendFeed(e, 3, me.transform.position, "Plank");
				Check(ref ok, sent.Count == 0, "no food in the bag: no feed sent");
			}
			finally
			{
				IslandNetwork.Loopback = null;
				int now = me.Inventory.GetItemCount("Plank");
				if (now < had) me.Inventory.AddItem("Plank", had - now); else if (now > had) me.Inventory.RemoveItem("Plank", now - had);
			}
			if (ok) Log("PASS: mp trust (sweep #6)"); else Fail("mp trust (sweep #6)");
		}

		[ConsoleCommand(name: "CIMpTrustLive", docs: "Dev, joined player: sweep 11-14 #6 with a live host - Raft names the host; each host-only kind is taken from it and dropped from anyone else; nothing real was dropped in this session")]
		public static void MpTrustLiveCommand(string[] args)
		{
			bool ok = true;
			Check(ref ok, !Raft_Network.IsHost && RAPI.GetLocalPlayer() != null, "run on a joined player");
			ulong host = 0UL;
			try { Raft_Network network = ComponentManager<Raft_Network>.Value; host = network != null ? network.HostID.Id : 0UL; } catch { }
			Check(ref ok, host != 0UL && IslandNetwork.HostIdOverride == 0UL, "Raft names the host (" + host + "), no test override");
			foreach (int kind in IslandNetwork.HostOnlyKinds.Concat(IslandNetwork.FromHostOnPlayers).Distinct())
			{
				var m = new IslandNetMessage { Kind = kind };
				Check(ref ok, !IslandNetwork.NotFromHost(m, host, false), "kind " + kind + " from the host: taken");
				Check(ref ok, IslandNetwork.NotFromHost(m, host + 1UL, false), "kind " + kind + " from another player: dropped");
			}
			Check(ref ok, IslandNetwork.DroppedNotFromHost == 0, "messages dropped as not from the host this session: " + IslandNetwork.DroppedNotFromHost);
			if (ok) Log("PASS: mp trust live (sweep #6)"); else Fail("mp trust live (sweep #6)");
		}

		[ConsoleCommand(name: "CIUpgradeJoin", docs: "Dev, joined player: sweep 11-15 - the host's upgrade numbers came before any part of its world, and this player's numbers are the host's")]
		public static void UpgradeJoinCommand(string[] args)
		{
			bool ok = true;
			Check(ref ok, !Raft_Network.IsHost, "run on a joined player");
			Check(ref ok, UpgradeTable.LastJoinPartsBefore == 0, "world parts read before the numbers: " + UpgradeTable.LastJoinPartsBefore + " (-1: none came)");
			Check(ref ok, UpgradeTable.LastJoinData != null && ExtraUpgrades.IndexMapText() == UpgradeTable.LastJoinData, "numbers here '" + ExtraUpgrades.IndexMapText() + "', host's '" + UpgradeTable.LastJoinData + "'");
			if (ok) Log("PASS: upgrade numbers at join (sweep 11-15)"); else Fail("upgrade numbers at join (sweep 11-15)");
		}

		[ConsoleCommand(name: "CIUpgradeNumbers", docs: "Dev, world (host): sweep 11-14 #2 - a moved upgrade's number is kept in the world file and sent to joining players; a load or the host's numbers move it back to them where free, never onto another item")]
		public static void UpgradeNumbersCommand(string[] args) { StartTest(UpgradeNumbersRoutine()); }

		static IEnumerator UpgradeNumbersRoutine()
		{
			bool ok = true;
			Item_Base rope = ItemManager.GetItemByName("Rope");
			if (rope == null) { Fail("upgrade numbers: Raft has no Rope"); yield break; }
			var test = new ExtraUpgrades.Upgrade { Index = rope.UniqueIndex, Name = "DI_TestNumbers", BaseName = "Rope", Display = "Test numbers", Tint = Color.white, Description = "test" };
			string before = ExtraUpgrades.IndexMapText();
			try
			{
				Check(ref ok, ExtraUpgrades.Make(test) && test.Item != null && test.Index >= ExtraUpgrades.ClashBase, "an upgrade on a taken number is made, moved to " + test.Index);
				ExtraUpgrades.CosmeticItems.Add(test);
				int d = rope.UniqueIndex, f1 = test.Index;
				int f2 = Enumerable.Range(f1 + 1, 200).First(i => ItemManager.GetItemByIndex(i) == null && ExtraUpgrades.All.Concat(ExtraUpgrades.CosmeticItems).All(u => u.Declared != i && u.Index != i));
				string pair = d + ":" + f1;
				Check(ref ok, ExtraUpgrades.IndexMapText().Split(',').Contains(pair), "the world's numbers name it: " + ExtraUpgrades.IndexMapText());
				Check(ref ok, ExtraUpgrades.IndexLines().Any(l => l.StartsWith("@upgradeindex=") && l.Contains(pair)) && WorldOptions.WriteLines().Any(l => l.StartsWith("@upgradeindex=")), "written to the world file");
				IslandNetMessage join = UpgradeTable.IndexMessage();
				Check(ref ok, join != null && join.Kind == IslandNetMessage.UpgradeTable && join.Name == "indexes" && join.Data == ExtraUpgrades.IndexMapText(), "sent to a joining player");

				// A load with other numbers: moved there (free)
				Check(ref ok, ExtraUpgrades.ApplyIndexMap(ExtraUpgrades.ParseIndexMap(d + ":" + f2), "test") == 1, "moved to the world's number");
				Item_Base atF2 = ItemManager.GetItemByIndex(f2);
				Check(ref ok, test.Index == f2 && atF2 != null && atF2.UniqueName == "DI_TestNumbers" && ItemManager.GetItemByIndex(f1) == null, "its item answers to " + f2 + " and " + f1 + " is free (" + (atF2 != null ? atF2.UniqueName : "null") + ")");
				Check(ref ok, ExtraUpgrades.LiveIndex(d) == f2 && ExtraUpgrades.DeclaredIndex(f2) == d && ExtraUpgrades.FindMade(atF2) == test, "live/declared lookups follow it");
				// Never onto another item, never to a number upgrades don't move to
				Check(ref ok, ExtraUpgrades.ApplyIndexMap(ExtraUpgrades.ParseIndexMap(d + ":" + d), "test") == 0 && test.Index == f2 && ItemManager.GetItemByIndex(d) == rope, "its declared number, held by Rope: kept");
				Check(ref ok, ExtraUpgrades.ApplyIndexMap(ExtraUpgrades.ParseIndexMap(d + ":100"), "test") == 0 && test.Index == f2, "a number below the moved range: kept");
				// The world file's line moves it back
				Check(ref ok, ExtraUpgrades.ReadIndexLine("upgradeindex", pair) && test.Index == f1 && ItemManager.GetItemByIndex(f1) == test.Item, "the world file's line moves it back to " + f1);
				Check(ref ok, ExtraUpgrades.ParseIndexMap("1:2, 3:4,bad,5").Count == 2, "a hand-edited line: bad pairs left out");
				// Sweep 11-15: numbers from an earlier world or host don't stay on
				Check(ref ok, test.Fresh == f1, "its start number is kept (" + test.Fresh + ")");
				Check(ref ok, ExtraUpgrades.ApplyIndexMap(ExtraUpgrades.ParseIndexMap(d + ":" + f2), "test") == 1 && ExtraUpgrades.ApplyFullMap(null, "test", true) >= 1 && test.Index == f1 && ItemManager.GetItemByIndex(f2) == null,
					"a world without the line: back to the start number " + f1 + " (now " + test.Index + ")");
				Check(ref ok, ExtraUpgrades.ApplyIndexMap(ExtraUpgrades.ParseIndexMap(d + ":" + f2), "test") == 1 && ExtraUpgrades.ApplyFullMap(ExtraUpgrades.ParseIndexMap(""), "test", false) == 0 && test.Index == f2 && ItemManager.GetItemByIndex(d) == rope,
					"a host with none moved: its declared number is Rope's here, so kept (" + test.Index + ")");
				Check(ref ok, ExtraUpgrades.ApplyFullMap(ExtraUpgrades.ParseIndexMap(d + ":" + f1), "test", false) == 1 && test.Index == f1, "the host's list moves it");
				Check(ref ok, ExtraUpgrades.ApplyFullMap(ExtraUpgrades.ParseIndexMap(ExtraUpgrades.IndexMapText()), "test", false) == 0, "the same list again: nothing moves");
				foreach (string m in new[] { "SendWorld", "Platform_RequestWorldAsClient" })
				{
					var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(Raft_Network), m));
					Check(ref ok, info != null && info.Prefixes.Any(p => p.PatchMethod.DeclaringType.Name == "UpgradeNumbersJoinPatches"), "Raft_Network." + m + " is patched");
				}
				var part = Harmony.GetPatchInfo(AccessTools.Method(typeof(NetworkUpdateManager), "DeserializeSingleMessage"));
				Check(ref ok, part != null && part.Prefixes.Any(p => p.PatchMethod.DeclaringType.Name == "UpgradeNumbersJoinPatches"), "world parts are counted");
			}
			catch (Exception e) { Check(ref ok, false, "threw " + e); }
			finally { ExtraUpgrades.CosmeticItems.Remove(test); ExtraUpgrades.ForgetForTest(test); }
			Check(ref ok, ItemManager.GetItemByName("DI_TestNumbers") == null && ExtraUpgrades.IndexMapText() == before, "test item taken out again");
			IslandNetMessage none = UpgradeTable.IndexMessage();
			Check(ref ok, none != null && none.Name == "indexes" && none.Data == before, "the numbers are sent also with none moved ('" + before + "')");
			yield return null;
			if (ok) Log("PASS: upgrade numbers (sweep #2)"); else Fail("upgrade numbers (sweep #2)");
		}
	}
}
