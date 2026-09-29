using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommandUndoRedo;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of the audit's leftovers (ROADMAP §2b A1-A8) and the release cases they closed: a file another program holds
	/// open (UP7) and a folder with a thousand islands (UP10).
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CILockFile", docs: "Dev, editor (TEST_CATALOGUE UP7): an island file another program holds open (a FileShare.None stream) - saving over it says 'in use by another program' and leaves the file as it was with no .tmp beside it, Delete says so and moves nothing, a settings write the same; once let go, saving works. Cleans up")]
		public static void LockFileCommand() { DynamicIslands.instance.StartCoroutine(LockFileRoutine()); }

		static IEnumerator LockFileRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("lock file: in the editor"); yield break; }
			bool ok = true;
			const string name = "citest-lock";
			string path = IslandSpawner.PathFor(name);
			int told = 0;
			Application.LogCallback counter = (msg, trace, type) => { if (msg.Contains("in use by another program")) told++; };
			FileStream held = null;
			try
			{
				DynamicIslands.NewIsland();
				Check(ref ok, DynamicIslands.SaveIsland(name), "a test island saved as '" + name + "'");
				byte[] before = File.ReadAllBytes(path);
				Application.logMessageReceived += counter;
				held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); // (as a program that locks it)
				terraineditor.terrain.terrainData.SetHeights(20, 20, new float[,] { { 0.4f } });
				UndoRedoManager.Execute(new NoopCommand());
				int t0 = told;
				bool saved = DynamicIslands.SaveIsland(name);
				Check(ref ok, !saved && told > t0, "saving over it is refused and says 'in use by another program'");
				held.Dispose(); held = null;
				Check(ref ok, File.ReadAllBytes(path).SequenceEqual(before) && !File.Exists(path + ".tmp"), "the saved island is as it was, no .tmp beside it");
				held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
				bool moved = true;
				try { IslandFilesWindow.MoveToDeleted(name); } catch (Exception e) { moved = false; Check(ref ok, SafeFile.InUse(e), "Delete: the error is 'in use' (" + e.GetType().Name + ")"); }
				Check(ref ok, !moved, "Delete moves nothing while it is held");
				string settings = Path.Combine(DynamicIslands.assetpath, "citest-lock.txt");
				File.WriteAllText(settings, "old");
				using (new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					bool refused = false;
					try { SafeFile.WriteAllText(settings, "new"); } catch (Exception e) { refused = SafeFile.InUse(e); }
					Check(ref ok, refused && !File.Exists(settings + ".tmp"), "a settings file held open: the write says 'in use', no .tmp left");
				}
				Check(ref ok, File.ReadAllText(settings) == "old", "... and the file is as it was");
				File.Delete(settings);
				held.Dispose(); held = null;
				Check(ref ok, DynamicIslands.SaveIsland(name) && !File.ReadAllBytes(path).SequenceEqual(before), "let go: saving works again (with the change)");
			}
			catch (Exception e) { Check(ref ok, false, "no errors: " + e.Message); }
			finally
			{
				Application.logMessageReceived -= counter;
				if (held != null) held.Dispose();
				try { if (DynamicIslands.InEditor()) DynamicIslands.NewIsland(); } catch { }
				foreach (string f in new[] { path, path + ".tmp", EditorAutosave.PathFor(name) }) try { if (File.Exists(f)) File.Delete(f); } catch { }
			}
			if (ok) Log("PASS: lock file"); else Fail("lock file");
		}

		[ConsoleCommand(name: "CILeftoversUnit", docs: "Dev, anywhere (ROADMAP §2b A1, A4): Tidy up's rules on test files - a world file this PC hosted whose Raft world is gone counts as deleted, one saved by another host (a joined world) doesn't; a gen- island no world uses is unused, one a world uses isn't; a copy no world uses is unused; a rule finds an island from its copy (name_hash) when only that is here. Cleans up")]
		public static void LeftoversUnit()
		{
			bool ok = true;
			string worlds = Path.Combine(DynamicIslands.assetpath, "worlds");
			Directory.CreateDirectory(worlds);
			string mine = Path.Combine(worlds, "citest-left-mine.txt"), joined = Path.Combine(worlds, "citest-left-joined.txt"), user = Path.Combine(worlds, "citest-left-user.txt");
			const string genUsed = "gen-citest-left-used", genFree = "gen-citest-left-free", copyBase = "citest-left-copy";
			string copyName = null;
			try
			{
				string by = Housekeeping.SavedByLine();
				Check(ref ok, by != null, "this PC's hosting line: " + by);
				File.WriteAllLines(mine, new[] { "# Custom islands in world 'CI Gone World 7x': ...", "@savedat=1", by ?? "@savedby=1" });
				File.WriteAllLines(joined, new[] { "# Custom islands in world 'CI Joined World 7x': ...", "@savedat=1", "@savedby=76561190000000001" });
				var small = new IslandFile { TerrainSize = new Vector3(32f, 60f, 32f), HeightmapResolution = 17, Heights = new float[17, 17] };
				foreach (string g in new[] { genUsed, genFree, copyBase }) { small.Name = g; small.Heights[8, 8] = g.Length / 100f; small.Save(IslandSpawner.PathFor(g)); }
				string hash = IslandNetwork.HashOf(copyBase);
				copyName = IslandNetwork.DownloadName(copyBase, hash);
				File.Move(IslandSpawner.PathFor(copyBase), IslandSpawner.PathFor(copyName));
				File.WriteAllLines(user, new[] { "# Custom islands in world 'CI Left User 7x': ...", genUsed + "|0|0|0||||" });
				IslandCache.Forget();

				List<KeyValuePair<string, string>> gone = Housekeeping.DeletedWorldFiles();
				Check(ref ok, gone.Any(g => g.Value == "CI Gone World 7x"), "a world this PC hosted whose Raft world is gone counts as deleted");
				Check(ref ok, !gone.Any(g => g.Value == "CI Joined World 7x"), "a world another host saved (joined here) is kept");
				Housekeeping.Scan scan = Housekeeping.Look();
				Check(ref ok, scan.UnusedGenerated.Contains(genFree) && !scan.UnusedGenerated.Contains(genUsed), "a gen- island no world uses is unused, one a world uses isn't");
				Check(ref ok, scan.Copies.Any(c => c.Key == copyName && c.Value.Count == 0), "a copy no world uses is unused: " + copyName);
				Log("  " + scan.Describe());
				Check(ref ok, WorldDirector.FileFor(copyBase) == copyName && WorldDirector.FileFor("citest-left-none") == null, "a rule finds '" + copyBase + "' from its copy when only that is here");
			}
			catch (Exception e) { Check(ref ok, false, "no errors: " + e.Message); }
			finally
			{
				foreach (string f in new[] { mine, joined, user }) try { if (File.Exists(f)) File.Delete(f); } catch { }
				foreach (string n in new[] { genUsed, genFree, copyBase, copyName }) try { if (n != null && File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n)); } catch { }
				IslandCache.Forget();
			}
			if (ok) Log("PASS: leftovers unit"); else Fail("leftovers unit");
		}

		[ConsoleCommand(name: "CIHotkeyTabs", docs: "Dev, world (host, a test world 'CI ...'): the hotbar's key tabs - the journal's (J) beside Raft's notebook tab, the stats page's (K) only while the level up system is on (switched on and back); picture shot_hotkey_tabs.png")]
		public static void HotkeyTabsCommand() { DynamicIslands.instance.StartCoroutine(HotkeyTabsRoutine()); }

		static IEnumerator HotkeyTabsRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("hotkey tabs: host in a test world 'CI ...'"); yield break; }
			bool ok = true;
			bool levels = PlayerLevels.On;
			yield return new WaitForSeconds(1f);
			GameObject j = GameObject.Find(HotkeyHints.JournalName), note = GameObject.Find("UI_Hotkey_Element_NoteBook");
			Func<GameObject, string> text = g => { if (g == null) return null; Component t = g.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == "TextMeshProUGUI"); return t != null ? HarmonyLib.Traverse.Create(t).Property("text").GetValue<string>() : null; };
			Check(ref ok, j != null && note != null && j.transform.parent == note.transform.parent, "the journal's tab is beside Raft's notebook tab");
			Check(ref ok, text(j) == JournalWindow.Key.ToString(), "it says the journal's key: " + text(j));
			Check(ref ok, j != null && note != null && j.transform.position.x > note.transform.position.x, "... after the notebook's tab (right of it)");
			PlayerLevels.SetEnabled(true);
			yield return new WaitForSeconds(1.5f);
			GameObject k = GameObject.Find(HotkeyHints.StatsName);
			Check(ref ok, k != null && k.activeInHierarchy && text(k) == PlayerLevels.Key.ToString(), "the level up system on: the stats tab shows its key " + text(k));
			Screenshot(new[] { "hotkey_tabs" });
			yield return new WaitForSeconds(0.5f);
			PlayerLevels.SetEnabled(false);
			yield return new WaitForSeconds(1.5f);
			GameObject k2 = GameObject.Find(HotkeyHints.StatsName); // (Find skips inactive objects)
			Check(ref ok, k2 == null, "off: the stats tab is gone again");
			if (levels) PlayerLevels.SetEnabled(true);
			if (ok) Log("PASS: hotkey tabs"); else Fail("hotkey tabs");
		}

		[ConsoleCommand(name: "CIDropOpen", docs: "Dev: opens the first visible drop-down list named so (e.g. Drop_When), as a click on it would: CIDropOpen <name>")]
		public static void DropOpenCommand(string[] args)
		{
			string name = args != null && args.Length > 0 ? args[0] : "";
			// (the top-most one: cards further down a list are there too, scrolled out of view)
			DropdownButton d = UnityEngine.Object.FindObjectsOfType<DropdownButton>().Where(x => x.name == name && x.isActiveAndEnabled).OrderByDescending(x => x.transform.position.y).FirstOrDefault();
			if (d == null) { Fail("no visible drop-down '" + name + "'"); return; }
			DropList.Open(d.GetComponent<UnityEngine.UI.Button>(), d);
			Log("Opened the drop-down " + name + " (" + d.Options.Count + " options)");
			DynamicIslands.instance.StartCoroutine(DropReport());
		}

		static IEnumerator DropReport()
		{
			for (int i = 0; i < 3; i++)
			{
				GameObject list = GameObject.Find("DropdownList");
				RectTransform r = list != null ? (RectTransform)list.transform : null;
				Log("  frame " + i + ": open " + DropList.IsOpen + (r != null ? ", list at " + r.anchoredPosition + " size " + r.rect.size + " under " + r.parent.parent.name + ", screen " + RectTransformUtility.WorldToScreenPoint(null, r.position) : ", no list"));
				yield return null;
			}
		}

		const string BulkPrefix = "bulk-";

		[ConsoleCommand(name: "CIBulkIslands", docs: "Dev, anywhere (editor for the Islands window) (TEST_CATALOGUE UP10): CIBulkIslands [n] - n small islands (bulk-0001 ..., default 1000) in the folder: listing them, the spawn pool, CHOOSE ISLANDS' list, the library's Tidy up look and (in the editor) the Islands window each take under 2 s, and the search finds one. The islands are removed after (CIBulkIslands keep leaves them; CIBulkIslands clean removes them)")]
		public static void BulkIslandsCommand(string[] args)
		{
			string a = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (a == "clean") { Log("Removed " + BulkClean() + " bulk islands"); return; }
			int n;
			if (!int.TryParse(a, out n)) n = 1000;
			DynamicIslands.instance.StartCoroutine(BulkRoutine(Mathf.Clamp(n, 1, 5000), args != null && args.Contains("keep")));
		}

		static int BulkClean()
		{
			int n = 0;
			foreach (string f in Directory.GetFiles(DynamicIslands.assetpath, BulkPrefix + "*" + IslandFile.Extension)) { try { File.Delete(f); n++; } catch { } }
			IslandCache.Forget();
			CustomIslandSpawner.LoadPool(true);
			return n;
		}

		static IEnumerator BulkRoutine(int count, bool keep)
		{
			bool ok = true;
			try
			{
				// A small island: 64 m, a hill in the middle, nothing on it (a thousand of them are ~2 MB)
				int res = 33;
				var h = new float[res, res];
				for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) { float d = new Vector2(x - 16, y - 16).magnitude / 16f; h[y, x] = Mathf.Max(0f, 0.45f - 0.3f * d); }
				var file = new IslandFile { TerrainSize = new Vector3(64f, 60f, 64f), HeightmapResolution = res, Heights = h };
				var sw = Stopwatch.StartNew();
				for (int i = 1; i <= count; i++) { file.Name = BulkPrefix + i.ToString("0000"); file.Save(IslandSpawner.PathFor(file.Name)); }
				Log(count + " small islands written in " + sw.ElapsedMilliseconds + " ms");
				IslandCache.Forget();

				Func<string, Action, long> time = (what, act) => { var t = Stopwatch.StartNew(); act(); t.Stop(); Log("  " + what + ": " + t.ElapsedMilliseconds + " ms"); return t.ElapsedMilliseconds; };
				List<string> listed = null;
				long listMs = time("listing the islands", () => listed = IslandSpawner.ListSavedIslands().ToList());
				Check(ref ok, listMs < 2000 && listed.Count(x => x.StartsWith(BulkPrefix)) == count, "the islands are listed (" + listed.Count + " in all) in " + listMs + " ms");
				Check(ref ok, listed.Contains(BulkPrefix + (count / 2).ToString("0000")), "the search finds one of them: " + BulkPrefix + (count / 2).ToString("0000"));
				long poolMs = time("the spawn pool", () => CustomIslandSpawner.LoadPool(true));
				Check(ref ok, poolMs < 2000, "the spawn pool builds in " + poolMs + " ms");
				List<string> candidates = null;
				long pickMs = time("CHOOSE ISLANDS' list", () => candidates = WorldIslands.Candidates());
				Check(ref ok, pickMs < 2000 && candidates.Count(c => c.StartsWith(BulkPrefix)) == count, "CHOOSE ISLANDS lists them in " + pickMs + " ms");
				Housekeeping.Scan scan = null;
				// (the Installed tab no longer looks through everything when it opens: Tidy up does, on its first click)
				long tidyMs = time("Tidy up's look (its first click)", () => scan = Housekeeping.Look());
				Check(ref ok, tidyMs < 10000 && scan != null, "Tidy up looks through them and every world's saves in " + tidyMs + " ms (on its click)");
				if (DynamicIslands.InEditor())
				{
					long winMs = time("the Islands window", () => { IslandFilesWindow.Open(); IslandFilesWindow.Close(); });
					Check(ref ok, winMs < 2000, "the Islands window opens in " + winMs + " ms");
				}
				long mem = GC.GetTotalMemory(false) / (1024 * 1024);
				Log("  managed memory now " + mem + " MB");
			}
			catch (Exception e) { Check(ref ok, false, "no errors: " + e.Message); }
			finally { if (!keep) Log("Removed " + BulkClean() + " bulk islands"); }
			yield return null;
			if (ok) Log("PASS: bulk islands"); else Fail("bulk islands");
		}
	}
}
