using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A world's extra options, ticked in the New Game box's World settings window (WorldSettingsWindow) and the same for
	/// every player in the world:
	///   - blueprints: Raft's blueprints on its story islands are found on other islands than usual (ScrambledBlueprints);
	///   - storyorder: Raft's story islands come in another order (StoryOrder);
	///   - ghostrafts: abandoned rafts drift by while sailing, some large and guarded (GhostRafts);
	///   - privatestorage: a storage opens only for the player who built it (PrivateStorage);
	///   - longvoyage: random custom islands come half as often and further apart (WorldIslands.NewTarget, CustomIslandSpawner);
	///   - ironraft: the raft's blocks take half damage from the shark (IronRaft);
	///   - sharedxp: players near a kill get 60 % of its EXP too (LevelSystem.ShareKill);
	///   - nightdanger: monsters tougher and the shark keener at night, both calmer by day (NightDanger);
	///   - dailyquest: a small task each day, a small reward when done before dark (DailyQuest).
	/// Saved in the world file ("@options=", "@optionseed=" - the seed every shuffle of the world comes from), sent by the host
	/// to every player who joins and when they change (network kind 17: Data = "on=a,b;seed=n", Name = the private
	/// storages' builders). The last choice in the box is kept in world_rules.txt ("options=").
	/// </summary>
	public static class WorldOptions
	{
		public const string Blueprints = "blueprints", StoryOrder = "storyorder", GhostRafts = "ghostrafts", PrivateStorage = "privatestorage", LongVoyage = "longvoyage", IronRaft = "ironraft", SharedXp = "sharedxp", NightDanger = "nightdanger", DailyQuest = "dailyquest";
		public static readonly string[] All = { Blueprints, StoryOrder, GhostRafts, PrivateStorage, LongVoyage, IronRaft, SharedXp, NightDanger, DailyQuest };
		/// <summary>
		/// Options no longer offered (the user, 2026-10-09): not in the New Game box, left out of the remembered choice, and in
		/// World settings only while a world still has them on (so the host can switch them off). A world that has one on keeps it.
		/// </summary>
		public static readonly string[] Retired = { Blueprints, StoryOrder };
		public static bool IsRetired(string option) { return Array.IndexOf(Retired, option) >= 0; }
		public static string[] Offered { get { return All.Where(o => !IsRetired(o)).ToArray(); } }
		public static readonly string[] Labels = { "Scrambled blueprints", "Story islands in a new order", "Ghost rafts", "Private storages", "Long voyage", "Iron raft", "Shared EXP", "Night is dangerous", "Daily quest" };
		public static readonly string[] Hints =
		{
			"The blueprints lying on Raft's story islands are found on other story islands than usual. What the story needs (the steering wheel, the engine and its fuel, the machete) is never moved: the story can always be finished.",
			"Raft's story islands come in another order: the Receiver's frequencies and the notes that lead on follow the new order. The ending stays last. For players who know the way by heart.",
			"Abandoned rafts drift by while sailing: small ones with a little loot and a note, and now and then a large one guarded by rats and screechers, with a better hoard.",
			"A storage opens only for the player who built it: each player keeps their own things. Storages built before the option was on (or by nobody) open for everyone.",
			"Random custom islands come half as often and lie further apart, so food, water and the raft matter more between stops. Raft's own islands, and quest and plan islands, keep their places.",
			"The raft's blocks take half damage from shark bites: fewer repairs. Taking pieces down with the hammer or the axe works as always, and shark bait is eaten as fast as ever.",
			"When a player defeats a monster, every other player within 50 m gets 60 % of its EXP too (less what their own hits on it already earned). Only defeated monsters count; playing alone nothing changes.",
			"After dark monsters are tougher (x1.3 health and damage on top of the monster difficulty) and the shark comes for the raft more often; by day both are calmer (x0.85, the shark less often).",
			"Each morning the crew gets a small task for the day: gather some planks, plastic, palm leaves or scrap, catch a few fish or defeat a few monsters. Done before dark, everyone gets a small reward (basic resources or food); not done, it runs out.",
		};

		/// <summary>The current world's options (clients get the host's).</summary>
		public static readonly HashSet<string> Current = new HashSet<string>();
		/// <summary>The seed every shuffle of this world comes from (the same on every machine).</summary>
		public static int Seed;
		/// <summary>Chosen in the New Game box for the world being created (null = the last choice).</summary>
		public static HashSet<string> Pending;
		/// <summary>Raised on every machine when the options (or the seed) change.</summary>
		public static event Action Changed;

		public static bool On(string option) { return Current.Contains(option); }

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [options] " + msg); }

		public static string Label(string option) { int i = Array.IndexOf(All, option); return i >= 0 ? Labels[i] : option; }

		/// <summary>"on=a,b;seed=n" (only known options, in their order).</summary>
		public static string Encode(IEnumerable<string> on, int seed)
		{
			return "on=" + string.Join(",", All.Where(o => on.Contains(o)).ToArray()) + ";seed=" + seed.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>The options of a list like "a,b" (unknown names left out).</summary>
		public static HashSet<string> Parse(string list)
		{
			var set = new HashSet<string>();
			foreach (string p in (list ?? "").Split(',', ' ', ';'))
			{
				string o = p.Trim().ToLowerInvariant();
				if (All.Contains(o)) set.Add(o);
			}
			return set;
		}

		public static string Describe(IEnumerable<string> on)
		{
			string[] l = All.Where(o => on.Contains(o)).Select(Label).ToArray();
			return l.Length == 0 ? "none" : string.Join(", ", l);
		}

		public static string Describe() { return Describe(Current) + " (seed " + Seed + ")"; }

		#region The last choice (world_rules.txt)

		public static HashSet<string> Defaults { get { var d = Parse(WorldRules.ReadDefault("options")); d.RemoveWhere(IsRetired); return d; } }

		public static void SaveDefaults(IEnumerable<string> on) { WorldRules.SaveDefault("options", string.Join(",", All.Where(o => on.Contains(o)).ToArray())); }

		#endregion

		#region The world file

		/// <summary>Before a world's island list is read (host) or when joining one (client: the host's come).</summary>
		/// <summary>An option was on at some point in this world: its seed stays in the world file even with every option off
		/// (the file was deleted then, and an option switched on again got another seed - another story order, other
		/// blueprint pairs). A world that never had one writes no file for it.</summary>
		static bool used;

		internal static void Reset()
		{
			Current.Clear();
			Seed = 0;
			used = false;
			global::DynamicIslands.Editor.StoryOrder.Reset();
			if (!Raft_Network.IsHost) { Notify(); return; }
			bool isNew = false;
			try { isNew = GameManager.IsInNewGame; } catch { }
			if (isNew)
			{
				foreach (string o in Pending ?? Defaults) Current.Add(o);
				Seed = new System.Random().Next(1, int.MaxValue);
				used = Current.Count > 0;
				Log("New world: " + Describe());
				// (the level up system, when it was chosen in World settings)
				global::DynamicIslands.Editor.PlayerLevels.OnNewWorld();
			}
			Pending = null;
			// (a saved world loaded: every unsaved New Game choice goes back to the remembered one, as the options' and the
			// islands' did - the level up system, the randomizer and the rules box kept theirs)
			if (!isNew) { PlayerLevels.Pending = null; WorldRandomizer.Pending = null; NewWorldRulesBox.Forget(); }
			global::DynamicIslands.Editor.PrivateStorage.Reset();
			global::DynamicIslands.Editor.IronRaft.Reset();
			global::DynamicIslands.Editor.NightDanger.Reset();
			global::DynamicIslands.Editor.DailyQuest.Reset();
			ScrambledBlueprints.Reset();
			global::DynamicIslands.Editor.GhostRafts.Reset();
			Notify();
			if (isNew) Broadcast();
		}

		internal static bool ReadLine(string key, string value)
		{
			switch (key)
			{
				case "options": Current.Clear(); foreach (string o in Parse(value)) Current.Add(o); if (Current.Count > 0) used = true; Notify(); return true;
				case "optionsused": used = true; return true;
				case "optionseed": int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out Seed); Notify(); return true;
			}
			return global::DynamicIslands.Editor.PrivateStorage.ReadLine(key, value) || global::DynamicIslands.Editor.GhostRafts.ReadLine(key, value) || global::DynamicIslands.Editor.DailyQuest.ReadLine(key, value);
		}

		internal static IEnumerable<string> WriteLines()
		{
			if (Current.Count == 0 && Seed == 0) yield break;
			yield return "@options=" + string.Join(",", All.Where(On).ToArray());
			yield return "@optionseed=" + Seed.ToString(CultureInfo.InvariantCulture);
			if (used) yield return "@optionsused=1";
			foreach (string l in global::DynamicIslands.Editor.PrivateStorage.WriteLines()) yield return l;
			foreach (string l in global::DynamicIslands.Editor.GhostRafts.WriteLines()) yield return l;
			foreach (string l in global::DynamicIslands.Editor.DailyQuest.WriteLines()) yield return l;
		}

		internal static bool HasState { get { return Current.Count > 0 || used || global::DynamicIslands.Editor.PrivateStorage.HasState; } }

		#endregion

		#region Network (kind 17)

		internal static IslandNetMessage Message()
		{
			return new IslandNetMessage { Kind = IslandNetMessage.WorldOptions, Data = Encode(Current, Seed), Name = global::DynamicIslands.Editor.PrivateStorage.Encode() };
		}

		internal static void Broadcast()
		{
			if (Raft_Network.IsHost) IslandNetwork.SendToEveryone(Message());
		}

		internal static void OnMessage(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost) return;
			HashSet<string> on = new HashSet<string>();
			int seed = 0;
			foreach (string part in (msg.Data ?? "").Split(';'))
			{
				int eq = part.IndexOf('=');
				if (eq < 1) continue;
				string k = part.Substring(0, eq), v = part.Substring(eq + 1);
				if (k == "on") on = Parse(v);
				else if (k == "seed") int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
			}
			Current.Clear();
			foreach (string o in on) Current.Add(o);
			Seed = seed;
			if (msg.Name != null) global::DynamicIslands.Editor.PrivateStorage.Decode(msg.Name);
			Log("The host's options: " + Describe());
			Notify();
			// (the blueprints swapped now, not at the next look 1.5 s on - AU65)
			try { ScrambledBlueprints.ApplyNow(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [options] Blueprints: " + e.Message); }
		}

		/// <summary>The world was left (every machine): none until the next world's are read or come.</summary>
		internal static void Forget()
		{
			Current.Clear();
			Seed = 0;
			used = false;
			ScrambledBlueprints.Reset();
		}

		/// <summary>Client: a host's world arrived: none until the host's come.</summary>
		internal static void OnWorldReceived()
		{
			Current.Clear();
			Seed = 0;
			global::DynamicIslands.Editor.PrivateStorage.Reset();
			global::DynamicIslands.Editor.IronRaft.Reset();
			global::DynamicIslands.Editor.NightDanger.Reset();
			global::DynamicIslands.Editor.DailyQuest.Reset();
			ScrambledBlueprints.Reset();
			Notify();
		}

		static void Notify()
		{
			// (the story order and the blueprints follow at once, on every machine)
			// (also while a world is still loading: Raft restores the notebook before the mod reads the world file's options)
			global::DynamicIslands.Editor.StoryOrder.Rebuild();
			ScrambledBlueprints.Rebuild();
			if (Changed == null) return;
			try { Changed(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [options] " + e); }
		}

		#endregion

		/// <summary>Host (and tests): changes the current world's options; every player gets them. Keeps the seed (a world without one gets one).</summary>
		public static void Set(IEnumerable<string> on)
		{
			Current.Clear();
			foreach (string o in on) if (All.Contains(o)) Current.Add(o);
			if (Seed == 0) Seed = new System.Random().Next(1, int.MaxValue);
			if (Current.Count > 0) used = true;
			Log("Set: " + Describe());
			Notify();
			Broadcast();
			IslandWorldState.Save();
		}

		[ConsoleCommand(name: "WorldOptions", docs: "The world's options (chosen in the New Game box's World settings): WorldOptions = what this world has; WorldOptions +option / -option (ghostrafts, privatestorage, longvoyage, ironraft, sharedxp, nightdanger, dailyquest) = change them for this world (host)")]
		public static void WorldOptionsCommand(string[] args)
		{
			if (args != null && args.Length > 0 && !LoadSceneManager.IsGameSceneLoaded)
			{
				// (the main menu: the next new world, remembered and shown in the World settings window - the words were ignored)
				HashSet<string> next = WorldSettingsWindow.Chosen;
				foreach (string a in args)
				{
					string o = a.TrimStart('+', '-').ToLowerInvariant();
					if (!All.Contains(o)) { Debug.Log("[CUSTOM ISLANDS] Unknown option '" + a + "' (" + string.Join(", ", All) + ")"); continue; }
					if (a.StartsWith("-")) next.Remove(o); else next.Add(o);
				}
				SaveDefaults(next);
				try { WorldSettingsWindow.Show(); } catch { }
			}
			else if (args != null && args.Length > 0 && LoadSceneManager.IsGameSceneLoaded)
			{
				if (!Raft_Network.IsHost) { Debug.Log("[CUSTOM ISLANDS] Only the host changes the world's options"); return; }
				var on = new HashSet<string>(Current);
				foreach (string a in args)
				{
					string o = a.TrimStart('+', '-').ToLowerInvariant();
					if (!All.Contains(o)) { Debug.Log("[CUSTOM ISLANDS] Unknown option '" + a + "' (" + string.Join(", ", All) + ")"); continue; }
					if (a.StartsWith("-")) on.Remove(o); else on.Add(o);
				}
				Set(on);
			}
			Debug.Log("[CUSTOM ISLANDS] World options: " + Describe() + (LoadSceneManager.IsGameSceneLoaded ? "" : " (next new world: " + Describe(Pending ?? Defaults) + ")"));
		}
	}
}
