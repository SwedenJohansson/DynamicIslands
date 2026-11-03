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
	/// <summary>
	/// RK1: a host whose own version of a world's island differs from the one the world was saved with (another host's)
	/// plays its own in its place, but the world keeps its version, asks the players for it and plays it once it comes.
	/// </summary>
	public static partial class DevTests
	{
		const string StandInIsland = "cistandin";
		/// <summary>Not a real player: the world looks saved by another PC.</summary>
		const ulong StandInOtherHost = 76561190000000077UL;
		static string StandInWorldFile { get { return Path.Combine(DynamicIslands.assetpath, "cistandin_world.bin"); } }
		/// <summary>This PC's own version, put in place by "swap" at the main menu (after leaving saved the world's).</summary>
		static string StandInOwnFile { get { return Path.Combine(DynamicIslands.assetpath, "cistandin_own.bin"); } }

		[ConsoleCommand(name: "CIStandIn", docs: "Dev, world (host, a test world 'CI ...'): RK1 - another host's version of an island. CIStandIn setup: saves the world as if another PC hosted it, with a version of the island this PC then lacks (then CIMainMenu, CIStandIn swap - at the main menu: this PC gets its own version -, CILoadWorld, CIStandIn load1, CIMainMenu, CILoadWorld, CIStandIn load2). load1: this PC's version plays in its place, the save keeps the world's. load2: still so after this PC saved it; the world's version comes from a player and plays from the island's next load. CIStandIn clean: removes the island")]
		public static void StandInCommand(string[] args)
		{
			string mode = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "setup";
			if (mode == "swap")
			{
				// (at the main menu: leaving the world saved it once more as the other PC, with the world's version - now this
				// PC gets only its own version of the island, as a player who edited it would have)
				Housekeeping.SavedByForTest = 0;
				try { File.Copy(StandInOwnFile, IslandSpawner.PathFor(StandInIsland), true); File.Delete(StandInOwnFile); }
				catch (Exception ex) { Fail("stand-in swap: run CIStandIn setup and CIMainMenu first (" + ex.Message + ")"); return; }
				Log("PASS: stand-in swap (this PC has its own version now); now CILoadWorld, CIStandIn load1");
				return;
			}
			StartTest(StandInRoutine(mode));
		}

		static IEnumerator StandInRoutine(string mode)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("stand-in: run in a world, as the host"); yield break; }
			bool ok = true;
			if (mode == "clean") { StandInClean(); Log("PASS: stand-in clean"); yield break; }
			if (mode == "setup")
			{
				yield return EnsureAlive();
				StandInClean();
				var made = new List<IslandWorldState.Entry>();
				// The world's version (another host's)
				try { ScIsland(StandInIsland, "Stand-in, the world's version").Save(IslandSpawner.PathFor(StandInIsland)); } catch (Exception ex) { Fail("stand-in: no sample island (" + ex.Message + ")"); yield break; }
				Vector3? spot = ScSpot(StandInIsland, 320f);
				if (!spot.HasValue) { Fail("stand-in: no open sea near the raft"); yield break; }
				yield return ScBring(StandInIsland, spot.Value, made);
				if (made.Count == 0 || made[0].Root == null) { ScRemove(made, StandInIsland); Fail("stand-in: the island did not come"); yield break; }
				string worldHash = IslandNetwork.HashOf(StandInIsland);
				File.WriteAllBytes(StandInWorldFile, File.ReadAllBytes(IslandSpawner.PathFor(StandInIsland)));
				// Saved by another PC
				// (kept through CIMainMenu's save too: "swap" at the main menu clears it)
				Housekeeping.SavedByForTest = StandInOtherHost;
				SaveNow();
				yield return new WaitForSeconds(3f);
				string saved = StandInLine();
				Check(ref ok, saved != null && saved.EndsWith("|" + worldHash), "the world is saved with its version of the island (" + worldHash + ")");
				Check(ref ok, File.ReadAllLines(IslandWorldState.WorldFilePath).Contains("@savedby=" + StandInOtherHost), "the world is saved as by another PC");
				// This PC's own version of the island, put in place at the main menu (CIStandIn swap)
				ScIsland(StandInIsland, "Stand-in, this PC's own version").Save(StandInOwnFile);
				string own = IslandNetwork.Hash(File.ReadAllBytes(StandInOwnFile));
				Check(ref ok, own != worldHash, "this PC's version differs (" + own + ")");
				Log("now CIMainMenu, CIStandIn swap, CILoadWorld, CIStandIn load1");
				if (ok) Log("PASS: stand-in set up"); else Fail("stand-in set up");
				yield break;
			}
			string world = null;
			try { world = IslandNetwork.Hash(File.ReadAllBytes(StandInWorldFile)); } catch { }
			if (world == null) { Fail("stand-in " + mode + ": run CIStandIn setup first"); yield break; }
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.HostName == StandInIsland);
			if (e == null) { Fail("stand-in " + mode + ": the island is not in the loaded world"); yield break; }
			Check(ref ok, e.StandIn, "this PC's version stands in for the world's");
			Check(ref ok, e.Name == StandInIsland && e.Hash == world, "it plays this PC's file; the world's hash is kept (" + e.Name + ", " + e.Hash + ")");
			Check(ref ok, IslandNetwork.Wanted.Contains(world), "the players are asked for the world's version");
			for (float t = 0; e.Root == null && t < 60f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			Check(ref ok, e.Root != null, "the island loads");
			Check(ref ok, e.Hash == world, "loading it doesn't take this PC's hash for the world's (" + e.Hash + ")");
			SaveNow();
			yield return new WaitForSeconds(3f);
			string line = StandInLine();
			string[] all = File.ReadAllLines(IslandWorldState.WorldFilePath);
			Check(ref ok, line != null && line.EndsWith("|" + world), "a save keeps the world's version (" + line + ")");
			Check(ref ok, all.Contains("@foreign=" + StandInIsland + "|" + world), "and marks it another host's version (saved by this PC now)");
			if (mode == "load1")
			{
				Log("now CIMainMenu, CILoadWorld, CIStandIn load2");
				if (ok) Log("PASS: stand-in load1"); else Fail("stand-in load1");
				yield break;
			}

			// load2: a player sends the world's version while the stand-in is loaded
			byte[] bytes = File.ReadAllBytes(StandInWorldFile);
			string want = IslandNetwork.DownloadName(StandInIsland, world);
			IslandNetwork.ReceiveChunk(new IslandNetMessage { Kind = IslandNetMessage.FileChunk, Name = StandInIsland, Hash = world, Index = 0, Count = 1, Data = Convert.ToBase64String(bytes) });
			Check(ref ok, File.Exists(IslandSpawner.PathFor(want)), "the world's version is kept on this PC (" + want + ")");
			Check(ref ok, e.Name == StandInIsland && e.SwitchTo == want, "the loaded stand-in stays until the island's next load");
			Check(ref ok, !IslandNetwork.Wanted.Contains(world), "the players aren't asked any more");
			float unloadWas = CustomIslandSpawner.UnloadDistance;
			try
			{
				CustomIslandSpawner.UnloadDistance = 1f;
				for (float t = 0; e.Root != null && t < 15f; t += 0.25f) yield return new WaitForSeconds(0.25f);
				Check(ref ok, e.Root == null, "the island unloads");
				Check(ref ok, e.Name == want && !e.StandIn && e.SwitchTo == null, "it plays the world's version from now on (" + e.Name + ")");
			}
			finally { CustomIslandSpawner.UnloadDistance = unloadWas; }
			for (float t = 0; e.Root == null && t < 30f; t += 0.25f) yield return new WaitForSeconds(0.25f);
			Check(ref ok, e.Root != null && IslandNetwork.HashOf(e.Name) == world && e.Hash == world, "it loads again as the world's version");
			SaveNow();
			yield return new WaitForSeconds(3f);
			line = StandInLine();
			all = File.ReadAllLines(IslandWorldState.WorldFilePath);
			Check(ref ok, line != null && line.EndsWith("|" + world) && !all.Any(l => l.StartsWith("@foreign=" + StandInIsland + "|")), "the save has the world's version, no longer marked as missing");
			StandInClean();
			if (ok) Log("PASS: stand-in load2"); else Fail("stand-in load2");
		}

		/// <summary>The island's line in the saved world file (null: none).</summary>
		static string StandInLine()
		{
			if (!File.Exists(IslandWorldState.WorldFilePath)) return null;
			return File.ReadAllLines(IslandWorldState.WorldFilePath).FirstOrDefault(l => l.StartsWith(StandInIsland + "|"));
		}

		static void StandInClean()
		{
			ScRemove(IslandWorldState.Islands.Where(x => x.HostName == StandInIsland).ToList(), StandInIsland);
			try
			{
				foreach (string f in Directory.GetFiles(DynamicIslands.assetpath, StandInIsland + "_*" + IslandFile.Extension)) File.Delete(f);
				File.Delete(StandInWorldFile);
			}
			catch { }
		}
	}
}
