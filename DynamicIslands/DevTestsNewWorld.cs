using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests of the New Game box's world settings as a whole: every setting chosen for a new world reaches that world,
	/// stays across a save and reload, and gets to a joining player (the ones the host sends).</summary>
	public static partial class DevTests
	{
		/// <summary>key=value pairs (monsters, build, looks, levels, headstart, rnd, gap, off).</summary>
		static Dictionary<string, string> NewWorldArgs(string[] args)
		{
			var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string a in args ?? new string[0])
			{
				int i = a.IndexOf('=');
				if (i > 0) d[a.Substring(0, i).Trim()] = a.Substring(i + 1).Trim(); else d[a.Trim()] = "";
			}
			return d;
		}

		static int LooksOf(string text) { return Array.FindIndex(AnimalLooks.Names, n => n.Equals(text, StringComparison.OrdinalIgnoreCase)); }
		static int RandomizerOf(string text) { return Array.FindIndex(RandomizerSettings.LevelNames, n => n.Equals(text, StringComparison.OrdinalIgnoreCase)); }
		/// <summary>The first N islands of the sailing list, by name (the same on every run).</summary>
		static List<string> FirstIslands(int n) { return WorldIslands.Candidates().OrderBy(e => e, StringComparer.OrdinalIgnoreCase).Take(n).ToList(); }

		[ConsoleCommand(name: "CINewWorldPick", docs: "Dev, main menu: the next new world's settings, as the World settings window sets them: CINewWorldPick [monsters=<level>] [build=<%>] [looks=normal|more|lots] [levels=on|off] [headstart=0-10] [rnd=off|light|normal|wild] [gap=<a-b>] [off=<n> (the first n islands of the sailing list left out)]; the extra options: CIWorldSettingsBox")]
		public static void NewWorldPickCommand(string[] args)
		{
			if (LoadSceneManager.IsGameSceneLoaded) { Fail("new world pick: main menu only"); return; }
			var d = NewWorldArgs(args);
			string v;
			int n;
			if (d.TryGetValue("monsters", out v)) { n = MonsterDifficulty.Parse(v); if (n < 0) { Fail("unknown monster level " + v); return; } NewWorldRulesBox.MonsterLevel = n; }
			if (d.TryGetValue("build", out v)) { if (!int.TryParse(v.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { Fail("build=" + v); return; } NewWorldRulesBox.BuildPercent = n; }
			if (d.TryGetValue("looks", out v)) { n = LooksOf(v); if (n < 0) { Fail("unknown looks " + v); return; } NewWorldRulesBox.LooksLevel = n; }
			if (d.TryGetValue("levels", out v)) PlayerLevels.Chosen = v.Equals("on", StringComparison.OrdinalIgnoreCase);
			if (d.TryGetValue("headstart", out v)) { if (!int.TryParse(v, out n)) { Fail("headstart=" + v); return; } HeadStart.Chosen = n; }
			if (d.TryGetValue("rnd", out v)) { n = RandomizerOf(v); if (n < 0) { Fail("unknown randomizer level " + v); return; } NewWorldOptions.Randomizer.Level = n; NewWorldOptions.Randomizer.Disabled.Clear(); }
			if (d.TryGetValue("gap", out v)) { int[] g = WorldIslands.ParseGap(v); WorldIslands.SetChosenGap(g[0], g[1]); }
			if (d.TryGetValue("off", out v))
			{
				if (!int.TryParse(v, out n)) { Fail("off=" + v); return; }
				WorldIslands.Chosen.Clear();
				foreach (string e in FirstIslands(n)) WorldIslands.Chosen.Add(e);
			}
			try { NewWorldOptions.Refresh(); WorldSettingsWindow.Show(); } catch { }
			Log("The next new world: monsters " + MonsterDifficulty.Describe(NewWorldRulesBox.MonsterLevel) + ", build cost " + BuildCost.Describe(NewWorldRulesBox.BuildPercent) + ", looks " + AnimalLooks.Describe(NewWorldRulesBox.LooksLevel) +
				", levels " + (PlayerLevels.Chosen ? "on" : "off") + ", head start " + HeadStart.Chosen + ", randomizer " + NewWorldOptions.Randomizer.Describe() + ", gap " + WorldIslands.GapText(WorldIslands.ChosenGap[0], WorldIslands.ChosenGap[1]) +
				", islands left out " + WorldIslands.Chosen.Count + ", options " + WorldOptions.Describe(WorldSettingsWindow.Chosen));
			Log("PASS: new world pick");
		}

		[ConsoleCommand(name: "CINewWorldSettingsCheck", docs: "Dev, world (host, or 'client' on a joiner): this world has the settings CINewWorldPick chose (the same arguments; a setting not named is not checked). A joiner checks what the host sends: monsters, build cost, looks, levels, randomizer level (head start, gap and islands left out are the host's alone)")]
		public static void NewWorldSettingsCheckCommand(string[] args)
		{
			if (!LoadSceneManager.IsGameSceneLoaded) { Fail("new world settings check: in a world"); return; }
			var d = NewWorldArgs(args);
			bool client = d.ContainsKey("client");
			string who = Raft_Network.IsHost ? "host" : "player 2";
			bool ok = true;
			string v;
			int n;
			if (d.TryGetValue("monsters", out v)) Check(ref ok, MonsterDifficulty.Current == MonsterDifficulty.Parse(v), "monsters " + MonsterDifficulty.Describe(MonsterDifficulty.Current) + " (wanted " + v + ", " + who + ")");
			if (d.TryGetValue("build", out v) && int.TryParse(v.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
				Check(ref ok, BuildCost.Current == n && BuildCost.Applied == n, "build cost " + BuildCost.Current + "%, applied " + BuildCost.Applied + "% (wanted " + n + "%, " + who + ")");
			if (d.TryGetValue("looks", out v)) Check(ref ok, AnimalLooks.Current == LooksOf(v), "rare animal looks " + AnimalLooks.Describe(AnimalLooks.Current) + " (wanted " + v + ", " + who + ")");
			if (d.TryGetValue("levels", out v)) Check(ref ok, PlayerLevels.On == v.Equals("on", StringComparison.OrdinalIgnoreCase), "level up system " + (PlayerLevels.On ? "on" : "off") + " (wanted " + v + ", " + who + ")");
			if (d.TryGetValue("rnd", out v)) Check(ref ok, WorldRandomizer.Current.Level == RandomizerOf(v), "randomizer " + WorldRandomizer.Current.Describe() + " (wanted " + v + ", " + who + ")");
			if (!client)
			{
				if (d.TryGetValue("headstart", out v) && int.TryParse(v, out n)) Check(ref ok, HeadStart.Level == n, "head start raft level " + HeadStart.Level + " (wanted " + n + ")");
				if (d.TryGetValue("gap", out v)) { int[] g = WorldIslands.ParseGap(v); Check(ref ok, WorldIslands.GapMin == g[0] && WorldIslands.GapMax == g[1], "a random custom island after " + WorldIslands.GapText(WorldIslands.GapMin, WorldIslands.GapMax) + " of Raft's islands (wanted " + v + ")"); }
				if (d.TryGetValue("off", out v) && int.TryParse(v, out n))
				{
					var want = FirstIslands(n);
					Check(ref ok, WorldIslands.Off.SetEquals(want), "islands left out: " + WorldIslands.Off.Count + " (" + string.Join(", ", WorldIslands.Off.Take(4).ToArray()) + ") - wanted " + want.Count);
				}
			}
			if (ok) Log("PASS: new world settings check"); else Fail("new world settings check");
		}
	}
}
