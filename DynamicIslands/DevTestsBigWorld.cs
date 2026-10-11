using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DynamicIslands
{
	/// <summary>
	/// A 20-hour world (TEST_CATALOGUE US1, ROADMAP P5): a test world filled with the records of long play - 150 islands
	/// visited with used objects, a 200-page journal, levels 40 for four players, the randomizer's seen places - then the
	/// world file's size, the mod's save and read, Raft's whole save and the Receiver's dots timed.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIBigWorld", docs: "Dev, world (host, a 'CI ...' test world): US1 - CIBigWorld fill = 150 islands visited (far off, used objects), a 200-page journal, levels 40 for four players, 150 randomizer places, then times; CIBigWorld check = after a reload: the records still there, and the times again")]
		public static void BigWorldCommand(string[] args) { StartTest(BigWorldRoutine(args.Length > 0 && args[0] == "fill")); }

		static IEnumerator BigWorldRoutine(bool fill)
		{
			for (float t = 0f; t < 60f && !(LoadSceneManager.IsGameSceneLoaded && RAPI.GetLocalPlayer() != null && ComponentManager<Raft>.Value != null); t += Time.unscaledDeltaTime) yield return null;
			if (!LoadSceneManager.IsGameSceneLoaded || RAPI.GetLocalPlayer() == null || !Raft_Network.IsHost) { Fail("big world: host, in a world (scene " + LoadSceneManager.IsGameSceneLoaded + ", host " + Raft_Network.IsHost + ")"); yield break; }
			if (!SaveAndLoad.CurrentGameFileName.StartsWith("CI ")) { Fail("big world: only in a test world 'CI ...' (here: '" + SaveAndLoad.CurrentGameFileName + "')"); yield break; }
			bool ok = true;
			Vector3 raft = ComponentManager<Raft>.Value.transform.position;
			if (fill)
			{
				string[] names = IslandSpawner.ListSavedIslands().Where(n => !n.StartsWith("ci")).ToArray();
				if (names.Length == 0) { Fail("big world: no saved islands"); yield break; }
				System.Random rnd = new System.Random(1011);
				for (int i = 0; i < 150; i++)
				{
					// (1-30 km off: never near enough to be brought in; the first 15 within the Receiver's 2 km)
					float a = (float)(rnd.NextDouble() * Math.PI * 2), d = i < 15 ? 1000f + (float)rnd.NextDouble() * 900f : 2000f + (float)rnd.NextDouble() * 28000f;
					IslandWorldState.Entry e = IslandWorldState.Add(names[i % names.Length], raft + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), null, false);
					for (int k = 0; k < 20; k++) e.State[k] = new ObjectState { Active = false, Yield = 0, Day = 1 + i / 8 };
					for (int k = 0; k < 4; k++) e.State[ContentState.LootKeyBase + k] = new ObjectState { Active = false, Yield = 0, Day = 1 + i / 8 };
					for (int k = 0; k < 3; k++) e.State[TriggerZone.KeyBase + k] = new ObjectState { Active = false, Yield = 0, Day = 1 + i / 8 };
				}
				string text = string.Concat(Enumerable.Repeat("The crew wrote down what they saw on the island, and where they went next. ", 5));
				for (int i = 0; i < 200; i++)
				{
					string island = names[i % names.Length];
					StoryBook.ReadLine("story.page", string.Join("|", new[] { "note:" + island + ":big" + i, (1 + i / 10).ToString(), "Page " + (i + 1), island, text }.Select(Uri.EscapeDataString).ToArray()));
				}
				PlayerLevels.ReadLine("levels", "on");
				string points = string.Join(",", Enumerable.Repeat("0", LevelRules.StatCount).ToArray());
				int xp = LevelRules.TotalFor(40);
				foreach (ulong id in new[] { PlayerLevels.LocalPlayerId, 4201UL, 4202UL, 4203UL })
					PlayerLevels.ReadLine("level", id + "|" + xp + "|" + points + "|" + (xp / 10));
				WorldRandomizer.ReadLine("rndseen", string.Join(";", Enumerable.Range(0, 150).Select(i => ((int)(rnd.NextDouble() * 60000 - 30000)) + "," + ((int)(rnd.NextDouble() * 60000 - 30000))).ToArray()));
				Log("big world filled: " + IslandWorldState.Islands.Count + " islands, " + StoryBook.Pages.Count + " journal pages, level " + LevelRules.LevelOf(xp));
			}
			Check(ref ok, IslandWorldState.Islands.Count >= 150, "islands in the world: " + IslandWorldState.Islands.Count);
			Check(ref ok, StoryBook.Pages.Count >= 200, "journal pages: " + StoryBook.Pages.Count);
			Check(ref ok, PlayerLevels.RecordOf(4203UL) != null && PlayerLevels.RecordOf(4203UL).Level >= 40, "levels 40 kept (" + (PlayerLevels.RecordOf(4203UL) == null ? "none" : "level " + PlayerLevels.RecordOf(4203UL).Level) + ")");
			int used = IslandWorldState.Islands.Sum(e => e.State.Count(s => !s.Value.Active));
			Check(ref ok, used >= 150 * 27, "used objects kept: " + used);

			// The mod's own save (the world file and its copies, sent to nobody here) and read, a few times each
			Stopwatch sw = new Stopwatch();
			long saveMs = 0, readMs = 0;
			for (int i = 0; i < 3; i++)
			{
				sw.Reset(); sw.Start(); IslandWorldState.Save(); sw.Stop(); saveMs = Math.Max(saveMs, sw.ElapsedMilliseconds);
				yield return null;
			}
			string file = IslandWorldState.WorldFilePath;
			long size = File.Exists(file) ? new FileInfo(file).Length : -1;
			for (int i = 0; i < 3; i++)
			{
				sw.Reset(); sw.Start(); IslandWorldState.OnWorldLoaded(); sw.Stop(); readMs = Math.Max(readMs, sw.ElapsedMilliseconds);
				yield return null;
			}
			Check(ref ok, IslandWorldState.Islands.Count >= 150 && StoryBook.Pages.Count >= 200, "read back: " + IslandWorldState.Islands.Count + " islands, " + StoryBook.Pages.Count + " pages");
			Check(ref ok, size > 0 && size < 2 * 1024 * 1024, "world file " + (size / 1024) + " KB (under 2 MB)");
			Check(ref ok, saveMs < 1000, "the mod's save " + saveMs + " ms (under 1000; the first hashes every island file once)");
			Check(ref ok, readMs < 1000, "the mod's read " + readMs + " ms (under 1000)");

			// Raft's whole save (the mod's part in it): the save hitch a player feels
			sw.Reset(); sw.Start(); UnityEngine.Object.FindObjectOfType<SaveAndLoad>().SaveGame(false); sw.Stop();
			Check(ref ok, sw.ElapsedMilliseconds < 2000, "Raft's whole save " + sw.ElapsedMilliseconds + " ms (under 2000)");
			yield return null;

			// The Receiver's dots: drawn every frame over all the world's islands
			Reciever prefab = null;
			foreach (Item_Base item in ItemManager.GetAllItems())
			{
				try
				{
					if (item == null || item.settings_buildable == null || !item.settings_buildable.Placeable) continue;
					Block[] blocks = item.settings_buildable.GetBlockPrefabs();
					prefab = blocks != null ? blocks.Where(b => b != null).Select(b => b.GetComponentInChildren<Reciever>(true)).FirstOrDefault(rc => rc != null) : null;
					if (prefab != null) break;
				}
				catch { }
			}
			if (prefab == null) Check(ref ok, false, "Raft's receiver block found");
			else
			{
				GameObject go = UnityEngine.Object.Instantiate(prefab.transform.root.gameObject, raft + Vector3.up * 3f, Quaternion.identity);
				Reciever r = go.GetComponentInChildren<Reciever>(true);
				r.enabled = false;
				if (r.radarSection != null) r.radarSection.SetActive(true);
				yield return null;
				IslandRadar.Draw(r); // (the first makes the dots)
				sw.Reset(); sw.Start();
				for (int i = 0; i < 100; i++) IslandRadar.Draw(r);
				sw.Stop();
				double drawMs = sw.Elapsed.TotalMilliseconds / 100.0;
				Check(ref ok, drawMs < 1.0, "the Receiver's dots " + drawMs.ToString("F3") + " ms a frame (under 1; " + IslandRadar.Shown.Count + " shown)");
				UnityEngine.Object.Destroy(go);
			}

			// The pool's pick for the next island, with this many islands already in the world
			sw.Reset(); sw.Start();
			for (int i = 0; i < 20; i++) CustomIslandSpawner.PickFromPool();
			sw.Stop();
			Stopwatch parts = Stopwatch.StartNew();
			for (int i = 0; i < 20; i++) CustomIslandSpawner.Pool(false);
			double poolAll = parts.Elapsed.TotalMilliseconds / 20; parts.Reset(); parts.Start();
			for (int i = 0; i < 20; i++) CustomIslandSpawner.PlanIslandNames();
			double planned = parts.Elapsed.TotalMilliseconds / 20; parts.Reset(); parts.Start();
			for (int i = 0; i < 20; i++) foreach (var p in CustomIslandSpawner.Pool(false)) { CustomIslandSpawner.NotAgain(p.Key); }
			double notAgain = parts.Elapsed.TotalMilliseconds / 20 - poolAll; parts.Reset(); parts.Start();
			for (int i = 0; i < 20; i++) foreach (var p in CustomIslandSpawner.Pool(false)) { CustomIslandSpawner.TooEarly(p.Key); }
			double tooEarly = parts.Elapsed.TotalMilliseconds / 20 - poolAll;
			parts.Reset(); parts.Start();
			for (int i = 0; i < 20; i++) IslandSpawner.ListSavedIslands().ToList();
			double scan = parts.Elapsed.TotalMilliseconds / 20;
			var at = typeof(CustomIslandSpawner).GetField("allPlansAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			if (at != null) at.SetValue(null, -100f);
			parts.Reset(); parts.Start(); CustomIslandSpawner.AllPlansIslandNames(); double plansCold = parts.Elapsed.TotalMilliseconds;
			Log("the pool's pick in parts (ms): folder scan " + scan.ToString("F1") + ", all plans' islands (cold) " + plansCold.ToString("F1") + ", pool " + poolAll.ToString("F1") + ", plans' islands " + planned.ToString("F1") + ", not again " + notAgain.ToString("F1") + ", too early " + tooEarly.ToString("F1"));
			Check(ref ok, sw.ElapsedMilliseconds / 20.0 < 20.0, "the pool's pick " + (sw.ElapsedMilliseconds / 20.0).ToString("F1") + " ms (under 20)");
			Log((ok ? "PASS" : "FAIL") + ": big world " + (fill ? "fill" : "check"));
		}
	}
}
