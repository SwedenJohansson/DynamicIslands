using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The world option "Scrambled blueprints" (WorldOptions.Blueprints): the blueprints lying on Raft's story islands are
	/// found on other story islands than usual. The blueprints on those islands (measured from their scenes, CIMeasureBlueprints,
	/// shipped as raft_blueprints.txt) are paired with each other from the world's seed - never one with itself - and every
	/// blueprint pickup of Raft's islands gives its partner instead (its item and its "pick up" name: the pickup's own yield
	/// is rewritten when the island appears, on every machine alike, so what the host and every player see is the same).
	/// The blueprints the story needs are never moved (Keep): the Receiver and antenna, the steering wheel, the engine and
	/// its fuel, the machete, the zipline and the headlight - so the story can always be finished, in any order.
	/// Only a pickup's contents change: which pickups there are, and which were taken, is Raft's (saved and sent by Raft).
	/// </summary>
	public static class ScrambledBlueprints
	{
		public const string FileName = "raft_blueprints.txt";

		/// <summary>Never moved: what the story needs to be finished (and to find its islands).</summary>
		public static readonly HashSet<string> Keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Blueprint_Reciever", "Blueprint_Antenna", "Blueprint_SteeringWheel", "Blueprint_MotorWheel", "Blueprint_EngineControls",
			"Blueprint_Fueltank", "Blueprint_Pipe_Fuel", "Blueprint_BiofuelExtractor", "Blueprint_Machete", "Blueprint_ZiplineTool",
			"Blueprint_ZiplineBase", "Blueprint_HeadLight", "Blueprint_BatteryCharger",
		};

		/// <summary>Blueprint -> the story island(s) it lies on (from raft_blueprints.txt).</summary>
		public static readonly Dictionary<string, List<string>> OnIslands = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		static bool read;

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [blueprints] " + msg); }

		public static bool Active { get { return WorldOptions.On(WorldOptions.Blueprints) && WorldOptions.Seed != 0 && Movable.Count > 1; } }

		/// <summary>raft_blueprints.txt: "island|Blueprint_Name" lines.</summary>
		public static void Read(bool force = false)
		{
			if (read && !force) return;
			read = true;
			OnIslands.Clear();
			byte[] bytes = RaftIslands.ModFile(FileName); // (Mods\DynamicIslands first, then the one shipped with the mod)
			if (bytes == null || bytes.Length == 0) { Debug.LogWarning("[CUSTOM ISLANDS] [blueprints] " + FileName + " is missing: blueprints aren't scrambled"); return; }
			foreach (string raw in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
			{
				string line = raw.TrimEnd('\r');
				if (line.StartsWith("#") || line.Trim().Length == 0) continue;
				string[] p = line.Split('|');
				if (p.Length < 2) continue;
				string island = p[0].Trim(), item = p[1].Trim();
				List<string> list;
				if (!OnIslands.TryGetValue(item, out list)) OnIslands[item] = list = new List<string>();
				if (!list.Contains(island)) list.Add(island);
			}
			movable = null;
		}

		static List<string> movable;

		/// <summary>The blueprints of the story islands that may move, in a fixed order (those Raft has).</summary>
		public static List<string> Movable
		{
			get
			{
				Read();
				if (movable == null)
					movable = OnIslands.Keys.Where(b => !Keep.Contains(b) && ItemManager.GetItemByName(b) != null).OrderBy(b => b, StringComparer.Ordinal).ToList();
				return movable;
			}
		}

		/// <summary>The pairs a seed gives: a permutation of the movable blueprints with nothing in its own place (and, where it
		/// can, never onto a blueprint of the same island).</summary>
		public static Dictionary<string, string> PairsFor(int seed, List<string> all)
		{
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			int n = all.Count;
			if (n < 2) return map;
			var rnd = new System.Random(seed ^ 0x3B1E);
			var to = all.ToList();
			for (int i = n - 1; i > 0; i--) { int j = rnd.Next(i + 1); string t = to[i]; to[i] = to[j]; to[j] = t; }
			// (no blueprint stays: a fixed point swaps with the next; then, where both allow it, away from its own island)
			for (int i = 0; i < n; i++) if (to[i] == all[i]) { int j = (i + 1) % n; string t = to[i]; to[i] = to[j]; to[j] = t; }
			Func<string, string, bool> sameIsland = (a, b) => { List<string> ia, ib; return OnIslands.TryGetValue(a, out ia) && OnIslands.TryGetValue(b, out ib) && ia.Intersect(ib).Any(); };
			for (int i = 0; i < n; i++)
			{
				if (!sameIsland(all[i], to[i])) continue;
				for (int j = 0; j < n; j++)
				{
					if (j == i || to[j] == all[i] || to[i] == all[j]) continue;
					if (!sameIsland(all[i], to[j]) && !sameIsland(all[j], to[i])) { string t = to[i]; to[i] = to[j]; to[j] = t; break; }
				}
			}
			for (int i = 0; i < n; i++) map[all[i]] = to[i];
			return map;
		}

		static int pairsSeed = int.MinValue;
		static Dictionary<string, string> pairs;

		/// <summary>What a blueprint found gives in this world (itself when the option is off or it isn't moved).</summary>
		public static string Map(string blueprint)
		{
			if (!Active || blueprint == null) return blueprint;
			if (pairs == null || pairsSeed != WorldOptions.Seed) { pairs = PairsFor(WorldOptions.Seed, Movable); pairsSeed = WorldOptions.Seed; }
			string to;
			return pairs.TryGetValue(blueprint, out to) ? to : blueprint;
		}

		#region Raft's pickups

		// Each pickup looked at: what it had (Raft's) - to give it back when the option goes off
		class Original { public ItemInstance Instance; public List<Cost> Yield; public int Seed; }
		static readonly Dictionary<PickupItem, Original> originals = new Dictionary<PickupItem, Original>();
		static readonly Dictionary<Landmark, int> looked = new Dictionary<Landmark, int>();
		static float nextTick;

		/// <summary>Tests: the pickups changed now (their island and "from > to").</summary>
		public static readonly List<string> Changed = new List<string>();

		internal static void Reset() { originals.Clear(); looked.Clear(); Changed.Clear(); }

		/// <summary>Every machine, twice a second: Raft's islands that appeared (or grew) get their blueprints' partners.</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextTick) return;
			nextTick = Time.unscaledTime + 1.5f; // (islands are looked at again only when they appear or grow)
			if (!LoadSceneManager.IsGameSceneLoaded || (!Active && originals.Count == 0)) return;
			try
			{
				foreach (Landmark l in WorldManager.AllLandmarks)
				{
					if (l == null || !l.isSpawned) continue;
					int count = l.GetComponentsInChildren<PickupItem>(true).Length;
					int seen;
					if (looked.TryGetValue(l, out seen) && seen == count) continue;
					looked[l] = count;
					Apply(l);
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [blueprints] " + e); }
		}

		/// <summary>The options changed (every machine): the islands there now are looked at again.</summary>
		public static void Rebuild()
		{
			looked.Clear();
			pairs = null;
			nextTick = 0f;
		}

		static void Apply(Landmark l)
		{
			foreach (PickupItem p in l.GetComponentsInChildren<PickupItem>(true))
			{
				if (p == null || p.pickupItemType == PickupItemType.QuestItem || p.pickupItemType == PickupItemType.NoteBookNote) continue;
				Original o;
				bool known = originals.TryGetValue(p, out o);
				int want = Active ? WorldOptions.Seed : 0;
				if (known && o.Seed == want) continue;
				// Raft's own contents first (a pickup already changed for another seed goes back)
				if (known) Restore(p, o);
				else
				{
					if (!HasMovable(p)) continue;
					o = new Original { Instance = p.itemInstance, Yield = YieldOf(p) != null ? YieldOf(p).ToList() : null };
					originals[p] = o;
				}
				o.Seed = want;
				if (want == 0) continue;
				if (p.itemInstance != null && p.itemInstance.baseItem != null)
				{
					string to = Map(p.itemInstance.baseItem.UniqueName);
					Item_Base item = to != p.itemInstance.baseItem.UniqueName ? ItemManager.GetItemByName(to) : null;
					if (item != null) { Changed.Add(l.name + ": " + p.itemInstance.baseItem.UniqueName + " > " + to); p.itemInstance = new ItemInstance(item, p.itemInstance.Amount, item.MaxUses, ""); }
				}
				List<Cost> yield = YieldOf(p);
				if (yield != null)
					for (int i = 0; i < yield.Count; i++)
					{
						Cost c = yield[i];
						if (c == null || c.item == null) continue;
						string to = Map(c.item.UniqueName);
						Item_Base item = to != c.item.UniqueName ? ItemManager.GetItemByName(to) : null;
						// (a new Cost: the old one may be shared with Raft's yield asset)
						if (item != null) { Changed.Add(l.name + ": " + c.item.UniqueName + " > " + to); yield[i] = new Cost { item = item, amount = c.amount }; }
					}
			}
		}

		static bool HasMovable(PickupItem p)
		{
			Func<Item_Base, bool> movable = i => i != null && Movable.Contains(i.UniqueName);
			if (p.itemInstance != null && movable(p.itemInstance.baseItem)) return true;
			List<Cost> y = YieldOf(p);
			return y != null && y.Any(c => c != null && movable(c.item));
		}

		static void Restore(PickupItem p, Original o)
		{
			p.itemInstance = o.Instance;
			List<Cost> y = YieldOf(p);
			if (y != null && o.Yield != null) { y.Clear(); y.AddRange(o.Yield); }
		}

		static List<Cost> YieldOf(PickupItem p)
		{
			if (p.yieldHandler == null) return null;
			return Traverse.Create(p.yieldHandler).Field("yield").GetValue<List<Cost>>();
		}

		#endregion
	}
}
