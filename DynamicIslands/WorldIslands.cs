using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Which islands take part in a world's random islands while sailing (the spawn pool of spawnpool.txt: the saved
	/// islands, the map types, brand-new generated islands). Every entry of the pool does, unless it is left out for this
	/// world. The choice is made when the world is created - "Choose islands..." in the New Game box's World settings opens
	/// a list with a tick box for each (IslandPickerWindow) - and can be changed later with the WorldIslands command (host).
	/// Only the entries left out are kept, so islands made or downloaded later take part in older worlds too, and a world
	/// without the line (every world made before this) has them all.
	/// Host only (only the host places islands): saved in the world file ("@islandsoff=a|b" - island names can't hold a |),
	/// so it goes with the world to another host (WorldCopy). The last choice in the box is kept in world_rules.txt
	/// ("islandsoff=").
	/// </summary>
	public static class WorldIslands
	{
		public const char Separator = '|';
		const string DefaultKey = "islandsoff";

		/// <summary>This world's pool entries that are left out (host; empty on clients and in older worlds).</summary>
		public static readonly HashSet<string> Off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Left out for the world being created (null = the last choice).</summary>
		public static HashSet<string> Pending;

		/// <summary>The entries left out for the world being created, as shown in the New Game box.</summary>
		public static HashSet<string> Chosen { get { if (Pending == null) Pending = Defaults; return Pending; } }

		public static bool TakesPart(string entry) { return !Off.Contains(entry); }

		#region How often: one random custom island per so many of Raft's own islands

		// (the user, 2026-10-04: random custom islands come between every 3-6 of Raft's normal islands by default; the
		// world's creator chooses from 2-4 up to 20-50 - however many islands are ticked, they never crowd the sea)
		public const int GapLowest = 2, GapLowestTop = 4, GapHighest = 20, GapHighestTop = 50, GapDefaultMin = 3, GapDefaultMax = 6;
		const string GapKey = "raftgap", CountKey = "raftgapcount";

		/// <summary>This world: a random custom island comes after GapMin to GapMax of Raft's own islands met (host).</summary>
		public static int GapMin = GapDefaultMin, GapMax = GapDefaultMax;
		/// <summary>This world: Raft's islands met since the last random custom island, and how many this time (host; saved).</summary>
		public static int RaftIslandsSince, Target;
		/// <summary>The world being created (null = the last choice).</summary>
		static int[] pendingGap;

		public static int[] ChosenGap { get { if (pendingGap == null) pendingGap = ParseGap(WorldRules.ReadDefault(GapKey)); return pendingGap; } }

		/// <summary>"3-6" -> {3, 6}, kept inside the allowed spans (min 2-20, max 4-50, max at least min); the default when unreadable.</summary>
		public static int[] ParseGap(string text)
		{
			int lo = GapDefaultMin, hi = GapDefaultMax;
			string[] parts = (text ?? "").Split('-');
			int a, b;
			if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out a) && int.TryParse(parts[1].Trim(), out b)) { lo = a; hi = b; }
			return ClampGap(lo, hi);
		}

		public static int[] ClampGap(int lo, int hi)
		{
			lo = Mathf.Clamp(lo, GapLowest, GapHighest);
			hi = Mathf.Clamp(hi, Mathf.Max(GapLowestTop, lo), GapHighestTop);
			return new[] { lo, hi };
		}

		public static string GapText(int lo, int hi) { return lo + "-" + hi; }

		/// <summary>The World settings window: the next world's span changed (saved as the last choice at once).</summary>
		public static void SetChosenGap(int lo, int hi)
		{
			pendingGap = ClampGap(lo, hi);
			WorldRules.SaveDefault(GapKey, GapText(pendingGap[0], pendingGap[1]));
		}

		/// <summary>A new number of Raft's islands to wait for, inside this world's span.</summary>
		public static void NewTarget() { Target = UnityEngine.Random.Range(GapMin, GapMax + 1); RaftIslandsSince = 0; }

		#endregion

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [islands] " + msg); }

		public static HashSet<string> Parse(string list)
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string p in (list ?? "").Split(Separator))
				if (p.Trim().Length > 0) set.Add(p.Trim());
			return set;
		}

		public static string Join(IEnumerable<string> entries)
		{
			return string.Join(Separator.ToString(), entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray());
		}

		#region The pool's entries

		/// <summary>Every entry of the pool as spawnpool.txt makes it (before any world's choice): what the list shows.</summary>
		public static List<string> Candidates()
		{
			CustomIslandSpawner.LoadPool(false);
			return CustomIslandSpawner.Pool(false).Select(p => p.Key).ToList();
		}

		public static bool IsIsland(string entry)
		{
			return entry != CustomIslandSpawner.GeneratedEntry && !entry.StartsWith(CustomIslandSpawner.TypePrefix, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>An entry as a player reads it: the island's name, "New wreck islands", "Brand-new generated islands".</summary>
		public static string Label(string entry)
		{
			if (entry == CustomIslandSpawner.GeneratedEntry) return "Brand-new generated islands";
			if (entry.StartsWith(CustomIslandSpawner.TypePrefix, StringComparison.OrdinalIgnoreCase))
			{
				MapType t = MapTypes.Get(entry.Substring(CustomIslandSpawner.TypePrefix.Length));
				return "New " + (t != null ? t.Label.ToLowerInvariant() : entry.Substring(CustomIslandSpawner.TypePrefix.Length)) + " islands";
			}
			return entry;
		}

		/// <summary>A few words on an entry (right of it in the list).</summary>
		public static string Detail(string entry)
		{
			if (entry == CustomIslandSpawner.GeneratedEntry) return "a new random island each time";
			if (!IsIsland(entry)) return "map type - a new one each time";
			try
			{
				var info = new FileInfo(IslandSpawner.PathFor(entry));
				if (info.Exists) return "your island - " + Math.Max(1, info.Length / 1024) + " KB, " + info.LastWriteTime.ToString("yyyy-MM-dd");
			}
			catch { }
			return "your island";
		}

		#endregion

		#region The last choice (world_rules.txt)

		public static HashSet<string> Defaults { get { return Parse(WorldRules.ReadDefault(DefaultKey)); } }

		public static void SaveDefaults(IEnumerable<string> off) { WorldRules.SaveDefault(DefaultKey, Join(off)); }

		#endregion

		#region The world file

		/// <summary>Before a world's island list is read: a new world gets the choice made in the New Game box.</summary>
		internal static void Reset()
		{
			Off.Clear();
			GapMin = GapDefaultMin; GapMax = GapDefaultMax; Target = 0; RaftIslandsSince = 0;
			if (!Raft_Network.IsHost) { Pending = null; pendingGap = null; return; }
			bool isNew = false;
			try { isNew = GameManager.IsInNewGame; } catch { }
			if (isNew)
			{
				foreach (string o in Pending ?? Defaults) Off.Add(o);
				int[] gap = ChosenGap;
				GapMin = gap[0]; GapMax = gap[1];
				Log("New world: " + Describe() + "; a random custom island after every " + GapText(GapMin, GapMax) + " of Raft's islands");
			}
			Pending = null;
			pendingGap = null;
		}

		internal static bool ReadLine(string key, string value)
		{
			if (key == GapKey) { int[] g = ParseGap(value); GapMin = g[0]; GapMax = g[1]; return true; }
			if (key == CountKey)
			{
				string[] parts = (value ?? "").Split('/');
				int n, t;
				if (parts.Length == 2 && int.TryParse(parts[0], out n) && int.TryParse(parts[1], out t)) { RaftIslandsSince = Mathf.Max(0, n); Target = Mathf.Max(0, t); }
				return true;
			}
			if (key != DefaultKey) return false;
			Off.Clear();
			foreach (string o in Parse(value)) Off.Add(o);
			return true;
		}

		internal static IEnumerable<string> WriteLines()
		{
			if (Off.Count > 0) yield return "@" + DefaultKey + "=" + Join(Off);
			if (GapMin != GapDefaultMin || GapMax != GapDefaultMax) yield return "@" + GapKey + "=" + GapText(GapMin, GapMax);
			if (Target > 0 || RaftIslandsSince > 0) yield return "@" + CountKey + "=" + RaftIslandsSince + "/" + Target;
		}

		internal static bool HasState { get { return Off.Count > 0 || GapMin != GapDefaultMin || GapMax != GapDefaultMax || Target > 0 || RaftIslandsSince > 0; } }

		/// <summary>A player who joined: the islands the host left out of this world (from the host's copy of the world file).</summary>
		public static string DescribeForPlayer()
		{
			if (WorldCopy.HostLines == null) return "Which islands turn up by chance is the host's list (it comes with the host's next save of the world).";
			HashSet<string> off = Parse(WorldCopy.HostValue(DefaultKey));
			return "Islands while sailing are the host's: " + (off.Count == 0 ? "every island of the host's pool takes part." : off.Count + " left out: " + string.Join(", ", off.OrderBy(o => o, StringComparer.OrdinalIgnoreCase).Select(Label).ToArray()) + ".");
		}

		#endregion

		public static string DescribeGap()
		{
			return "a random custom island after every " + GapText(GapMin, GapMax) + " of Raft's own islands (" + RaftIslandsSince + " met since the last" + (Target > 0 ? ", this time " + Target : "") + ")";
		}

		[ConsoleCommand(name: "WorldIslandsGap", docs: "How often random custom islands come: one after every <min>-<max> of Raft's own islands met (default 3-6; min 2-20, max 4-50). WorldIslandsGap = this world's (or, in the main menu, the next new world's); WorldIslandsGap 5-12 = change it (host in a world)")]
		public static void GapCommand(string[] args)
		{
			string arg = args != null ? string.Join("", args).Trim() : "";
			bool inWorld = LoadSceneManager.IsGameSceneLoaded;
			if (arg.Length > 0)
			{
				if (inWorld && !Raft_Network.IsHost) { Debug.Log("[CUSTOM ISLANDS] Only the host chooses how often islands come"); return; }
				int[] g = ParseGap(arg);
				if (inWorld) { GapMin = g[0]; GapMax = g[1]; if (Target < GapMin || Target > GapMax) Target = UnityEngine.Random.Range(GapMin, GapMax + 1); IslandWorldState.Save(); }
				else { SetChosenGap(g[0], g[1]); try { WorldSettingsWindow.Show(); } catch { } }
			}
			if (inWorld) Debug.Log("[CUSTOM ISLANDS] This world: " + DescribeGap());
			else Debug.Log("[CUSTOM ISLANDS] The next new world: a random custom island after every " + GapText(ChosenGap[0], ChosenGap[1]) + " of Raft's own islands");
		}

		public static string Describe()
		{
			return Off.Count == 0 ? "every island of the pool takes part" : Off.Count + " left out: " + string.Join(", ", Off.OrderBy(o => o, StringComparer.OrdinalIgnoreCase).Select(Label).ToArray());
		}

		/// <summary>Host (and tests): leaves an entry out of this world, or lets it take part again; saved at once.</summary>
		public static void Set(string entry, bool takesPart)
		{
			if (takesPart) Off.Remove(entry); else Off.Add(entry);
			Log((takesPart ? "Takes part again: " : "Left out: ") + Label(entry));
			IslandWorldState.Save();
		}

		[ConsoleCommand(name: "WorldIslands", docs: "Which islands turn up by chance while sailing in this world (chosen in the New Game box's World settings): WorldIslands = the list; WorldIslands -<island> / +<island> = leave one out / let it take part again (host; also type:<map type>); WorldIslands all = every island takes part")]
		public static void WorldIslandsCommand(string[] args)
		{
			string arg = args != null ? string.Join(" ", args).Trim() : "";
			if (arg.Length > 0 && !LoadSceneManager.IsGameSceneLoaded)
			{
				// (the main menu: the next new world, remembered and shown in the World settings window - the words were ignored)
				if (arg.Equals("all", StringComparison.OrdinalIgnoreCase)) Chosen.Clear();
				else if (arg[0] == '-' || arg[0] == '+')
				{
					string name = arg.Substring(1).Trim();
					string entry = Candidates().Concat(Chosen).FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
					if (entry == null) { Debug.Log("[CUSTOM ISLANDS] '" + name + "' isn't in the spawn pool (see SpawnPool)"); return; }
					if (arg[0] == '-') Chosen.Add(entry); else Chosen.Remove(entry);
				}
				else { Debug.Log("[CUSTOM ISLANDS] WorldIslands -<island> or +<island>, or WorldIslands all"); return; }
				SaveDefaults(Chosen);
				try { WorldSettingsWindow.Show(); } catch { }
			}
			else if (arg.Length > 0 && LoadSceneManager.IsGameSceneLoaded)
			{
				if (!Raft_Network.IsHost) { Debug.Log("[CUSTOM ISLANDS] Only the host chooses the world's islands"); return; }
				if (arg.Equals("all", StringComparison.OrdinalIgnoreCase)) { Off.Clear(); Log("Every island of the pool takes part again"); IslandWorldState.Save(); }
				else if (arg[0] == '-' || arg[0] == '+')
				{
					string name = arg.Substring(1).Trim();
					string entry = Candidates().Concat(Off).FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
					if (entry == null) { Debug.Log("[CUSTOM ISLANDS] '" + name + "' isn't in the spawn pool (see SpawnPool)"); return; }
					Set(entry, arg[0] == '+');
				}
				else { Debug.Log("[CUSTOM ISLANDS] WorldIslands -<island> or +<island>, or WorldIslands all"); return; }
			}
			if (LoadSceneManager.IsGameSceneLoaded && !Raft_Network.IsHost)
			{
				// (a player: the host's list, from the host's copy of the world file - this PC's own list is empty in a
				// world it joined, so it said every island takes part)
				Debug.Log("[CUSTOM ISLANDS] " + DescribeForPlayer());
				return;
			}
			List<string> all = Candidates();
			bool inWorld = LoadSceneManager.IsGameSceneLoaded;
			HashSet<string> off = inWorld ? Off : Chosen;
			Debug.Log("[CUSTOM ISLANDS] " + (inWorld ? "This world" : "The next new world") + ": " + all.Count(c => !off.Contains(c)) + " of " + all.Count + " take part while sailing" +
				(off.Count > 0 ? "; left out: " + string.Join(", ", off.OrderBy(o => o, StringComparer.OrdinalIgnoreCase).Select(Label).ToArray()) : ""));
		}
	}
}
