using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of the islands a world gets while sailing (WorldIslands, IslandPickerWindow): the list in the New Game box's
	/// World settings clicked as a player does, and a world made with some islands left out (worldislands.ps1).
	/// </summary>
	public static partial class DevTests
	{
		static HashSet<string> EntriesArg(string[] args)
		{
			return WorldIslands.Parse(args != null ? string.Join(" ", args) : "");
		}

		[ConsoleCommand(name: "CIIslandPickerBox", docs: "Dev, main menu: the World settings' 'Choose islands' list clicked as a player does - every entry of the spawn pool has a row with a tick box, a click unticks and ticks it again, the search narrows the rows, Untick shown / Tick shown do the rows shown, the counts follow, Done closes it. Leaves the entries named unticked for the next world: CIIslandPickerBox [entry|entry...] (default none)")]
		public static void IslandPickerBoxCommand(string[] args) { DynamicIslands.instance.StartCoroutine(IslandPickerBoxRoutine(EntriesArg(args))); }

		static IEnumerator IslandPickerBoxRoutine(HashSet<string> want)
		{
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("island picker: no New Game box (main menu?)"); yield break; }
			bool ok = true;
			box.gameObject.SetActive(true);
			try { box.Close(); } catch { } box.Open(); // (Raft's Open subscribes to input changes each time, Close unsubscribes: never open twice)
			yield return new WaitForSecondsRealtime(1f);
			WorldSettingsWindow.Open();
			yield return null;
			Button entry = IslandPickerWindow.EntryButton;
			Check(ref ok, entry != null && entry.gameObject.activeInHierarchy, "the World settings window has 'Islands while sailing' ('" + (entry != null ? UIKit.LabelOf(entry).text : "") + "')");
			if (entry == null) { Fail("island picker"); yield break; }
			var unknown = want.Where(w => !WorldIslands.Candidates().Contains(w, StringComparer.OrdinalIgnoreCase)).ToList();
			Check(ref ok, unknown.Count == 0, "the entries to leave out are in the pool" + (unknown.Count > 0 ? " - not: " + string.Join(", ", unknown.ToArray()) : ""));
			// (from a clean start: everything ticked)
			WorldIslands.Chosen.Clear();
			entry.onClick.Invoke();
			yield return null;
			Check(ref ok, IslandPickerWindow.IsOpen, "the button opens the list");
			List<string> all = WorldIslands.Candidates();
			var missing = all.Where(e => IslandPickerWindow.Row(e) == null).ToList();
			Check(ref ok, missing.Count == 0 && all.Count > 0, "a row for each of the pool's " + all.Count + " entries" + (missing.Count > 0 ? " - missing: " + string.Join(", ", missing.Take(5).ToArray()) : ""));
			Check(ref ok, all.All(IslandPickerWindow.IsTicked), "every row ticked at first");
			Check(ref ok, IslandPickerWindow.Count.text.StartsWith(all.Count + " of " + all.Count), "the count: '" + IslandPickerWindow.Count.text + "'");
			Screenshot(new[] { "islandpicker" });
			yield return new WaitForSecondsRealtime(0.6f);

			// A click unticks a row, another ticks it again
			string first = all.FirstOrDefault(WorldIslands.IsIsland) ?? all[0];
			IslandPickerWindow.Row(first).onClick.Invoke(); yield return null;
			Check(ref ok, !IslandPickerWindow.IsTicked(first) && WorldIslands.Chosen.Contains(first) && IslandPickerWindow.Count.text.StartsWith((all.Count - 1) + " of " + all.Count),
				"a click unticks '" + first + "' (" + IslandPickerWindow.Count.text + "; the World settings button: '" + UIKit.LabelOf(entry).text + "')");
			IslandPickerWindow.Row(first).onClick.Invoke(); yield return null;
			Check(ref ok, IslandPickerWindow.IsTicked(first) && WorldIslands.Chosen.Count == 0, "another click ticks it again");

			// The search, and Untick shown / Tick shown on what it found
			string part = first.Length > 3 ? first.Substring(0, 3) : first;
			IslandPickerWindow.Search.text = part; yield return null;
			List<string> shown = IslandPickerWindow.Shown();
			List<string> expect = all.Where(e => WorldIslands.Label(e).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			Check(ref ok, shown.Count > 0 && new HashSet<string>(shown).SetEquals(expect), "searching '" + part + "' shows the " + expect.Count + " matching rows (shown " + shown.Count + ")");
			Check(ref ok, Click(IslandPickerWindow.Window.gameObject, "Untick shown"), "Untick shown clicked");
			yield return null;
			Check(ref ok, WorldIslands.Chosen.Count == shown.Count && shown.All(e => !IslandPickerWindow.IsTicked(e)) && all.Except(shown).All(IslandPickerWindow.IsTicked),
				"Untick shown unticks exactly the rows shown (" + WorldIslands.Chosen.Count + ")");
			Check(ref ok, Click(IslandPickerWindow.Window.gameObject, "Tick shown"), "Tick shown clicked");
			yield return null;
			Check(ref ok, WorldIslands.Chosen.Count == 0 && all.All(IslandPickerWindow.IsTicked), "Tick shown ticks them again");
			IslandPickerWindow.Search.text = ""; yield return null;
			Check(ref ok, IslandPickerWindow.Shown().Count == all.Count, "an empty search shows every row again");

			// The entries asked for, left out
			foreach (string w in want) { Button r = IslandPickerWindow.Row(w); if (r != null) { r.onClick.Invoke(); yield return null; } }
			var off = IslandPickerWindow.Window.gameObject.activeSelf ? OffScreen(IslandPickerWindow.Window.gameObject, Screen.width, Screen.height) : new List<string>();
			Check(ref ok, off.Count == 0, "the list fits the screen (" + Screen.width + "x" + Screen.height + ")" + (off.Count > 0 ? " - off: " + string.Join(", ", off.Take(4).ToArray()) : ""));
			Check(ref ok, Click(IslandPickerWindow.Window.gameObject, "Done"), "Done clicked");
			yield return null;
			Check(ref ok, !IslandPickerWindow.IsOpen && WorldIslands.Chosen.SetEquals(want) && WorldIslands.Pending != null && WorldIslands.Pending.SetEquals(want),
				"Done closes it; left out of the next world: " + (want.Count == 0 ? "none" : string.Join(", ", want.ToArray())) + " (the button: '" + UIKit.LabelOf(entry).text + "')");
			WorldSettingsWindow.Close();
			box.gameObject.SetActive(false);
			if (ok) Log("PASS: island picker box"); else Fail("island picker box");
		}

		[ConsoleCommand(name: "CIWorldIslandsCheck", docs: "Dev, world (host): the world leaves out exactly these entries of the spawn pool - the world file's line, the pool the spawner picks from (400 picks: never one left out, every other one possible), the WorldIslands command (-/+ an entry, saved at once), SpawnPool's text: CIWorldIslandsCheck [entry|entry...]")]
		public static void WorldIslandsCheckCommand(string[] args)
		{
			HashSet<string> want = EntriesArg(args);
			bool ok = true;
			Check(ref ok, LoadSceneManager.IsGameSceneLoaded && Raft_Network.IsHost, "in a world, as its host");
			Check(ref ok, WorldIslands.Off.SetEquals(want), "the world leaves out: " + WorldIslands.Describe() + " (wanted " + (want.Count == 0 ? "none" : string.Join(", ", want.ToArray())) + ")");
			List<string> all = WorldIslands.Candidates();
			List<string> pool = CustomIslandSpawner.Pool().Select(p => p.Key).ToList();
			Check(ref ok, pool.All(WorldIslands.TakesPart) && all.Where(WorldIslands.TakesPart).All(pool.Contains), "the spawner's pool is the whole pool less those (" + pool.Count + " of " + all.Count + ")");
			var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < 400; i++) { string p = CustomIslandSpawner.PickFromPool(); if (p != null) picked.Add(p); }
			Check(ref ok, (CustomIslandSpawner.Pool().Sum(p => p.Value) <= 0f || picked.Count > 0) && !picked.Any(p => want.Contains(p)), "400 picks: none left out (" + picked.Count + " different ones picked)");

			// The world file
			IslandWorldState.Save();
			string file = IslandWorldState.WorldFilePath;
			string line = File.Exists(file) ? File.ReadAllLines(file).FirstOrDefault(l => l.StartsWith("@islandsoff=")) : null;
			Check(ref ok, want.Count == 0 ? line == null : line != null && WorldIslands.Parse(line.Substring("@islandsoff=".Length)).SetEquals(want), "the world file: " + (line ?? "(no @islandsoff line)"));

			// The command: leave one more out, then let it take part again
			string extra = all.FirstOrDefault(e => !want.Contains(e) && WorldIslands.IsIsland(e));
			if (extra != null)
			{
				WorldIslands.WorldIslandsCommand(new[] { "-" + extra });
				string l2 = File.ReadAllLines(file).FirstOrDefault(l => l.StartsWith("@islandsoff="));
				Check(ref ok, !WorldIslands.TakesPart(extra) && !CustomIslandSpawner.Pool().Any(p => p.Key.Equals(extra, StringComparison.OrdinalIgnoreCase)) && l2 != null && l2.Contains(extra),
					"WorldIslands -" + extra + ": left out, and saved");
				WorldIslands.WorldIslandsCommand(new[] { "+" + extra });
				Check(ref ok, WorldIslands.TakesPart(extra) && WorldIslands.Off.SetEquals(want), "WorldIslands +" + extra + ": takes part again");
			}
			Check(ref ok, CustomIslandSpawner.Describe().Contains("This world's choice"), "SpawnPool says what the world left out");
			Log("WORLDISLANDS " + WorldIslands.Describe());
			if (ok) Log("PASS: world islands check"); else Fail("world islands check");
		}
	}
}
