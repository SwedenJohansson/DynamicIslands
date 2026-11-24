using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The world setting "Head start raft" (World settings in the New Game box, levels 1-10): a new world starts on a bigger
	/// raft that is already set up, built once by the host on the first load (through the host's BlockCreator, so it is
	/// Raft's own blocks, saved with the world like any built ones - joiners get them with the world).
	///   - The layout (every level): the stations and storages stand in rows in the middle, a walkway between the rows and
	///     an aisle down the centre, the ring of edge foundations left bare (Bruce bites those first); a free deck round
	///     the start raft, the sail in front of it (level 2 up), the item nets in the water round the edges.
	///   - Level 1: 7 x 10, a simple grill, a purifier, the research table, a bed and a small storage of first supplies.
	///     Level 2: 7 x 14, also 2 smelters, 2 medium storages (island resources, batteries, flippers), a sail and 10 nets.
	///     Level 3: 9 x 14, also the Receiver with its 3 antennas and a battery, 2 more storages. Levels 4-10 add
	///     stations, large storages and nets, and supplies up to Tangaroa (Stations, Adds): no titanium, nothing from
	///     Varuna on; engines, steering, anchor, water tank and the like come as items in the storages, to place.
	///   - Every level: 15 planks per level on top. The supplies go in only as far as the storages have room (nothing is
	///     dropped); what doesn't fit is logged "left out".
	///   - Never twice: "@headstartdone=1" in the world file; a world whose raft is already big when it would be built
	///     (a crash before Raft saved it, then played on) is marked done instead.
	/// </summary>
	public static class HeadStart
	{
		public const float Cell = 1.5f;
		/// <summary>The highest level.</summary>
		public const int Max = 10;
		/// <summary>Above this many foundations the raft isn't the start raft any more: never built on.</summary>
		const int SmallRaft = 12;
		/// <summary>The free deck round the start raft: this many rows.</summary>
		const int Deck = 4;

		/// <summary>This world's level (host; 0 = none).</summary>
		public static int Level;
		/// <summary>This world's head start was built (or given up on).</summary>
		public static bool Done;
		/// <summary>Tests: what the last build placed ("" before one).</summary>
		public static string LastBuild = "";

		/// <summary>The next new world's level as the World settings window shows it (null = the remembered one).</summary>
		public static int? Pending;
		public static int Default { get { int l; int.TryParse((WorldRules.ReadDefault("headstart") ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out l); return Mathf.Clamp(l, 0, Max); } }
		public static void SaveDefault(int level) { WorldRules.SaveDefault("headstart", level.ToString(CultureInfo.InvariantCulture)); }
		public static int Chosen { get { if (!Pending.HasValue) Pending = Default; return Pending.Value; } set { Pending = Mathf.Clamp(value, 0, Max); } }

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [head start] " + msg); }

		static readonly int[] Widths = { 0, 7, 7, 9, 9, 9, 9, 11, 11, 11, 11 };
		/// <summary>The item nets round the raft per level (as many as fit, at most).</summary>
		static readonly int[] Nets = { 0, 0, 10, 12, 16, 20, 26, 32, 38, 44, 51 };

		public static int Width(int level) { return Widths[Mathf.Clamp(level, 1, Max)]; }
		public static int Length(int level) { return Plan(level).L; }
		public static int NetCount(int level) { return Plan(level).Pieces.Count(p => p.Item.Contains("CollectionNet")); }
		public static string Describe(int level) { return level <= 0 ? "off" : "level " + level + " (" + Width(level) + " x " + Length(level) + ")"; }
		/// <summary>Tests: how many of each placeable a level's raft has (the receiver's 3 antennas too).</summary>
		public static Dictionary<string, int> PieceCounts(int level)
		{
			var counts = Plan(level).Pieces.GroupBy(p => p.Item).ToDictionary(g => g.Key, g => g.Count());
			if (counts.ContainsKey(Receiver)) counts["Placeable_Reciever_Antenna"] = 3;
			return counts;
		}

		/// <summary>What a level gives, a line or two for the World settings window (each level has what the ones below it have).</summary>
		public static string Details(int level)
		{
			if (level <= 0) return "Off: Raft's own start raft of 4 foundations.";
			string[] what =
			{
				"",
				"A simple grill, a purifier, the research table, a bed and a small storage of planks, plastic, rope, stones, fish and potatoes.",
				"Also 2 smelters, a sail, 2 medium storages of island resources with 2 batteries and 2 flippers.",
				"Also the Receiver with its 3 antennas and a battery, 2 more storages of resources.",
				"Also an advanced purifier and the first tools: axe, scrap hook, metal rod, spear, bow, shovel; oxygen bottles, ingots, nails.",
				"Also an advanced grill, a 3rd smelter, large storages; an engine, steering wheel and anchor to place; head light, leather armour.",
				"Also a 4th smelter; Balboa's machete, biofuel extractor and fuel tank, a 2nd engine, a backpack, crop plots and seeds.",
				"Also a 2nd advanced purifier; engine controls, zipline, metal detector, a 2nd sail, more flippers and oxygen, metal for armour.",
				"Also a 5th smelter; the net gun and canisters, metal arrows, good salves, beehives, bricks, glass; better item nets.",
				"Also a 2nd advanced grill; Tangaroa's water tank, electric purifier and pipes, 4 engines in all, plenty of everything.",
				"The lot: 6 smelters, 3 advanced purifiers, 2 advanced grills, 19 storages of everything short of titanium.",
			};
			int l = Mathf.Clamp(level, 1, Max);
			int nets = NetCount(l);
			return Width(l) + " x " + Length(l) + " foundations. " + what[l] + (nets > 0 ? " " + nets + " item nets." : "") + " +" + 15 * l + " planks.";
		}

		#region The world file

		/// <summary>Before a world's file is read (WorldOptions.Reset): a new one gets the chosen level.</summary>
		// (the build waiting for the new raft: stopped when another world loads - review 10-30)
		static Coroutine building;

		internal static void Reset(bool isNew)
		{
			if (building != null && DynamicIslands.instance != null) DynamicIslands.instance.StopCoroutine(building);
			building = null;
			Level = 0;
			Done = false;
			LastBuild = "";
			if (isNew) { Level = Chosen; if (Level > 0) Log("New world: " + Describe(Level)); }
			Pending = null;
		}

		internal static bool ReadLine(string key, string value)
		{
			switch (key)
			{
				case "headstart": int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out Level); Level = Mathf.Clamp(Level, 0, Max); return true;
				case "headstartdone": Done = value.Trim() == "1"; return true;
			}
			return false;
		}

		internal static IEnumerable<string> WriteLines()
		{
			if (Level <= 0) yield break;
			yield return "@headstart=" + Level.ToString(CultureInfo.InvariantCulture);
			if (Done) yield return "@headstartdone=1";
		}

		internal static bool HasState { get { return Level > 0; } }

		/// <summary>The world's file was read (or a new world started): the host builds the head start when it is due.</summary>
		internal static void OnWorldRead()
		{
			if (!Raft_Network.IsHost || Level <= 0 || Done || DynamicIslands.instance == null) return;
			if (building != null) DynamicIslands.instance.StopCoroutine(building);
			building = DynamicIslands.instance.StartCoroutine(BuildWhenReady());
		}

		#endregion

		#region The layout

		/// <summary>One thing the head start places: its item, where (in cells from the new raft's corner; x across, z along)
		/// and the turn about y.</summary>
		struct Piece
		{
			public string Item; public float X, Z, Turn;
			public Piece(string item, float x, float z, float turn = 0f) { Item = item; X = x; Z = z; Turn = turn; }
		}

		/// <summary>A level's raft: its size, what goes where, the free station cells (antennas first go there) and the rows
		/// of the free deck (the start raft's two in its middle).</summary>
		class Layout
		{
			public int W, L, Rows, DeckFrom;
			public List<Piece> Pieces = new List<Piece>();
			public List<Vector2Int> Spare = new List<Vector2Int>();
		}

		const string Food1 = "Placeable_CookingStand_Food_One", Food2 = "Placeable_CookingStand_Food_Two", Purifier1 = "Placeable_CookingStand_Purifier_One",
			Purifier2 = "Placeable_CookingStand_Purifier_Two", Smelter = "Placeable_CookingStand_Smelter", Small = "Placeable_Storage_Small",
			Medium = "Placeable_Storage_Medium", Large = "Placeable_Storage_Large", Receiver = "Placeable_Reciever";

		/// <summary>The stations and storages each level adds, in the order they are placed (the storages' order is the
		/// supplies' order: Stock).</summary>
		static readonly string[][] Stations =
		{
			new string[0],
			new[] { Food1, Purifier1, "Placeable_ResearchTable", "Placeable_Bed_Basic", Small },
			new[] { Smelter, Smelter, Medium, Medium },
			new[] { Receiver, Medium, Medium },
			new[] { Purifier2, Medium },
			new[] { Food2, Smelter, Large, Large },
			new[] { Smelter, Large, Large },
			new[] { Purifier2, Large, Large },
			new[] { Smelter, Large, Large },
			new[] { Food2, Large, Large },
			new[] { Smelter, Purifier2, Large, Large, Large },
		};

		/// <summary>Cells across a station takes (the bed and the advanced grill turned across, the receiver as it is).</summary>
		static int Across(string item) { return item.EndsWith("Bed_Basic") || item == Food2 || item == Receiver ? 2 : 1; }
		static float TurnOf(string item) { return item.EndsWith("Bed_Basic") || item == Food2 ? 90f : 0f; }

		static readonly Dictionary<int, Layout> layouts = new Dictionary<int, Layout>();

		/// <summary>
		/// A level's raft, its cells from the back (z 0) to the front: the bare edge row; station rows (odd z) with a walkway
		/// row after each, each station row two halves either side of the centre aisle, filled from the aisle out; the free
		/// deck (the start raft); level 2 up the sail's row and a free row; the bare edge row. The edge columns stay bare too.
		/// The nets: the front row, the back row (its middle 3 left for engines), then the sides from the front, the start
		/// raft's two rows left open to climb up.
		/// </summary>
		static Layout Plan(int level)
		{
			level = Mathf.Clamp(level, 1, Max);
			Layout lay;
			if (layouts.TryGetValue(level, out lay)) return lay;
			lay = new Layout { W = Widths[level] };
			int w = lay.W, a = (w - 1) / 2, half = a - 1;
			var free = new List<int[]>(); // (per station row: cells left in the left and right half)
			for (int lv = 1; lv <= level; lv++)
				foreach (string item in Stations[lv])
				{
					int c = Across(item), row = -1, side = 0;
					for (int r = 0; r < free.Count && row < 0; r++)
						for (int s = 0; s < 2 && row < 0; s++)
							if (free[r][s] >= c) { row = r; side = s; }
					if (row < 0) { free.Add(new[] { half, half }); row = free.Count - 1; side = 0; }
					int used = half - free[row][side];
					// (left half from the aisle out: x a-1 down to 1; right half a+1 up to w-2)
					float x = side == 0 ? a - 1 - used - (c - 1) * 0.5f : a + 1 + used + (c - 1) * 0.5f;
					free[row][side] -= c;
					lay.Pieces.Add(new Piece(item, x, 1 + 2 * row, TurnOf(item)));
				}
			lay.Rows = free.Count;
			for (int r = 0; r < free.Count; r++)
			{
				for (int i = 0; i < free[r][0]; i++) lay.Spare.Add(new Vector2Int(1 + i, 1 + 2 * r));
				for (int i = 0; i < free[r][1]; i++) lay.Spare.Add(new Vector2Int(w - 2 - i, 1 + 2 * r));
			}
			lay.DeckFrom = 2 * lay.Rows + 1;
			bool sail = level >= 2;
			lay.L = lay.DeckFrom + Deck + (sail ? 2 : 0) + 1;
			int l = lay.L;
			if (sail) lay.Pieces.Add(new Piece("Placeable_Sail", a, l - 3));
			// (the nets in the water one cell past the edge, facing out)
			var spots = new List<Piece>();
			string net = level >= 8 ? "Placeable_CollectionNet_Advanced" : "Placeable_CollectionNet_Basic";
			var across = Enumerable.Range(0, w).OrderBy(i => Mathf.Abs(i - a)).ThenBy(i => i).ToList();
			foreach (int i in across) spots.Add(new Piece(net, i, l, 0f));
			foreach (int i in across) if (Mathf.Abs(i - a) > 1) spots.Add(new Piece(net, i, -1, 180f));
			int open = lay.DeckFrom + 1; // (the start raft's rows)
			for (int j = l - 1; j >= 0; j--)
			{
				if (j == open || j == open + 1) continue;
				spots.Add(new Piece(net, -1, j, 270f));
				spots.Add(new Piece(net, w, j, 90f));
			}
			lay.Pieces.AddRange(spots.Take(Nets[level]));
			layouts[level] = lay;
			return lay;
		}

		#endregion

		#region Building

		static IEnumerator BuildWhenReady()
		{
			float until = Time.realtimeSinceStartup + 120f;
			while (Time.realtimeSinceStartup < until)
			{
				Network_Player p = RAPI.GetLocalPlayer();
				Raft r = UnityEngine.Object.FindObjectOfType<Raft>();
				if (p != null && p.BlockCreator != null && r != null && Foundations(r).Count > 0 && LoadSceneManager.IsGameSceneLoaded) break;
				yield return new WaitForSecondsRealtime(0.5f);
			}
			// (the raft and the player settle a moment first - the start raft is still being made a frame or two)
			yield return new WaitForSecondsRealtime(2f);
			try { Build(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [head start] " + e); }
		}

		internal static List<Block> Foundations(Raft raft)
		{
			return raft.GetComponentsInChildren<Block>().Where(x => x != null && (x.name.Contains("Foundation") || (x.buildableItem != null && x.buildableItem.UniqueName.Contains("Foundation")))).ToList();
		}

		/// <summary>Host: builds this world's head start now, unless it was built already. Returns what happened.</summary>
		public static string Build()
		{
			if (!Raft_Network.IsHost) return "only the host builds the head start";
			if (Level <= 0 || Done) return "nothing to build (" + Describe(Level) + (Done ? ", built already" : "") + ")";
			Network_Player player = RAPI.GetLocalPlayer();
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (player == null || raft == null) return "no player or raft yet";
			List<Block> floors = Foundations(raft);
			if (floors.Count == 0) return "no foundation on the raft";
			if (floors.Count > SmallRaft)
			{
				Done = true;
				Log("The raft has " + floors.Count + " foundations already: no head start (marked done)");
				return LastBuild = "skipped: the raft is big already (" + floors.Count + " foundations)";
			}
			Layout lay = Plan(Level);
			int w = lay.W, l = lay.L;
			Vector3 o = floors[0].transform.localPosition;
			// (the start raft's cells, the new raft round them: across on the aisle, along in the middle of the free deck)
			var taken = new HashSet<Vector2Int>();
			foreach (Block b in floors) taken.Add(CellOf(b.transform.localPosition, o));
			float cx = (float)taken.Average(c => c.x), cz = (float)taken.Average(c => c.y);
			int x0 = Mathf.RoundToInt(cx - (w - 1) / 2f), z0 = Mathf.RoundToInt(cz - (lay.DeckFrom + (Deck - 1) / 2f));
			float top = FoundationTop(floors[0], raft) - o.y;

			int made = 0, failed = 0;
			Item_Base foundation = ItemManager.GetItemByName("Block_Foundation");
			for (int i = 0; i < w; i++)
				for (int j = 0; j < l; j++)
				{
					if (taken.Contains(new Vector2Int(x0 + i, z0 + j))) continue;
					if (Place(player, foundation, o + new Vector3((x0 + i) * Cell, 0f, (z0 + j) * Cell), 0f) != null) made++; else failed++;
				}

			var placed = new List<Block>();
			foreach (Piece pc in lay.Pieces)
			{
				// (on the floor: Raft puts a placeable's pivot where the player points on the foundation's top; a net hangs
				// in the water beside the raft, its pivot level with the top too)
				Block b = Place(player, ItemManager.GetItemByName(pc.Item), o + new Vector3((x0 + pc.X) * Cell, top, (z0 + pc.Z) * Cell), pc.Turn);
				if (b != null) placed.Add(b); else { failed++; Log("not placed: " + pc.Item + " at " + pc.X + "," + pc.Z); }
			}
			string stocked = Stock(placed);
			string receiver = Level >= 3 ? SetUpReceiver(player, placed, o, top, x0, z0, lay, taken) : "";
			Done = true;
			LastBuild = "level " + Level + ": " + made + " foundations, " + placed.Count + " placed" + (failed > 0 ? ", " + failed + " failed" : "") + "; " + stocked + receiver;
			Log(LastBuild);
			DynamicIslands.Notify("Head start raft (" + Describe(Level) + ") is built: the storages hold your first supplies.", false);
			return LastBuild;
		}

		static Vector2Int CellOf(Vector3 local, Vector3 o) { return new Vector2Int(Mathf.RoundToInt((local.x - o.x) / Cell), Mathf.RoundToInt((local.z - o.z) / Cell)); }

		/// <summary>The foundation's walking surface, in the raft's space (its solid colliders' top; 0.6 above it without).</summary>
		internal static float FoundationTop(Block f, Raft raft)
		{
			float best = float.MinValue;
			foreach (Collider c in f.GetComponentsInChildren<Collider>())
			{
				if (c == null || c.isTrigger || !c.enabled) continue;
				Vector3 t = raft.transform.InverseTransformPoint(new Vector3(c.bounds.center.x, c.bounds.max.y, c.bounds.center.z));
				if (t.y > best) best = t.y;
			}
			return best > float.MinValue ? best : f.transform.localPosition.y + 0.6f;
		}

		static Block Place(Network_Player player, Item_Base item, Vector3 local, float turn)
		{
			if (item == null) return null;
			try { return player.BlockCreator.CreateBlockCheat(item, local, new Vector3(0f, turn, 0f), DPS.Default, 0); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [head start] " + item.UniqueName + ": " + e.Message); return null; }
		}

		#endregion

		#region The supplies

		static KeyValuePair<string, int> Kv(string k, int v) { return new KeyValuePair<string, int>(k, v); }

		/// <summary>Levels 1-3: each storage's list, in the order they are placed.</summary>
		static List<KeyValuePair<string, int>[]> Lists(int level)
		{
			var lists = new List<KeyValuePair<string, int>[]>
			{
				new[] { Kv("Plank", 10), Kv("Plastic", 10), Kv("Rope", 10), Kv("Stone", 10), Kv("Raw_Mackerel", 5), Kv("Raw_Potato", 5) },
			};
			if (level >= 2)
			{
				lists.Add(new[] { Kv("Sand", 10), Kv("Clay", 10), Kv("Stone", 10), Kv("Scrap", 10), Kv("MetalOre", 10), Kv("Battery", 2) });
				lists.Add(new[] { Kv("CopperOre", 10), Kv("SeaVine", 10), Kv("Thatch", 10), Kv("Dirt", 10), Kv("VineGoo", 10), Kv("Flipper", 2) });
			}
			if (level >= 3)
			{
				lists.Add(new[] { Kv("Plank", 40), Kv("Plastic", 40), Kv("Rope", 20), Kv("Scrap", 20), Kv("MetalOre", 20), Kv("CopperOre", 10) });
				lists.Add(new[] { Kv("Sand", 20), Kv("Clay", 20), Kv("Stone", 20), Kv("SeaVine", 20), Kv("Thatch", 20), Kv("Raw_Potato", 10) });
			}
			return lists;
		}

		/// <summary>What levels 4-10 add (each level has the ones below it too): up to Tangaroa, nothing of titanium or from
		/// Varuna on. Placeables here (engines, steering, anchor, water tank...) are items to place where the player likes.</summary>
		static readonly KeyValuePair<string, int>[][] Adds =
		{
			new KeyValuePair<string, int>[0], new KeyValuePair<string, int>[0], new KeyValuePair<string, int>[0], new KeyValuePair<string, int>[0],
			new[] // 4: the first tools
			{
				Kv("Axe", 1), Kv("Hook_Scrap", 1), Kv("FishingRod_Metal", 1), Kv("Spear_Scrap", 1), Kv("Bow", 1), Kv("Arrow_Stone", 20), Kv("Shovel", 1), Kv("Hammer", 1),
				Kv("Bucket", 1), Kv("Paddle", 1), Kv("OxygenBottle", 2), Kv("Canteen_Water", 2), Kv("MetalIngot", 20), Kv("Nail", 40), Kv("Glass", 10), Kv("Plank", 40),
				Kv("Plastic", 40), Kv("Rope", 20), Kv("Scrap", 20), Kv("Sand", 20), Kv("Clay", 20), Kv("Raw_Beet", 10), Kv("Coconut", 10), Kv("Placeable_Lantern_Basic", 2),
			},
			new[] // 5: Vasagatan - sailing under power
			{
				Kv("HeadLight", 1), Kv("Battery", 2), Kv("Placeable_MotorWheel", 1), Kv("Placeable_SteeringWheel", 1), Kv("Placeable_Anchor_Stationary", 1), Kv("Binoculars", 1),
				Kv("CopperIngot", 10), Kv("Bolt", 10), Kv("Hinge", 10), Kv("CircuitBoard", 4), Kv("MetalIngot", 20), Kv("Nail", 40), Kv("Plank", 40), Kv("Plastic", 40),
				Kv("Thatch", 20), Kv("Leather", 10), Kv("Feather", 20), Kv("HealingSalve", 5), Kv("Equipment_LeatherHelmet", 1), Kv("Equipment_LeatherChest", 1),
				Kv("Equipment_LeatherLegs", 1), Kv("Cooked_Mackerel", 10),
			},
			new[] // 6: Balboa - fuel, farming
			{
				Kv("Machete", 1), Kv("Placeable_BiofuelExtractor", 1), Kv("Placeable_FuelTank", 1), Kv("Placeable_Pipe_Fuel", 10), Kv("BioFuel", 10), Kv("Placeable_MotorWheel", 1),
				Kv("Backpack", 1), Kv("Placeable_Cropplot_Medium", 2), Kv("Placeable_Scarecrow", 1), Kv("Placeable_CookingPot", 1), Kv("Seed_Pineapple", 4), Kv("Seed_Watermelon", 4),
				Kv("Seed_Mango", 2), Kv("Seed_Banana", 2), Kv("Pineapple", 5), Kv("Watermelon", 5), Kv("Banana", 5), Kv("Mango", 5), Kv("SeaVine", 20), Kv("Dirt", 20),
				Kv("Stone", 20), Kv("MetalOre", 20), Kv("Scrap", 20), Kv("Plank", 40),
			},
			new[] // 7: Caravan Town - engine controls, ziplines; metal for armouring the edges
			{
				Kv("Placeable_EngineControls", 1), Kv("ZiplineTool", 1), Kv("Placeable_ZiplineBase", 4), Kv("MetalDetector", 1), Kv("OxygenBottle", 2), Kv("Flipper", 2),
				Kv("Shear", 1), Kv("Wool", 20), Kv("Berries_Red", 10), Kv("Battery", 2), Kv("Placeable_Sail", 1), Kv("Placeable_Lantern_Basic", 4), Kv("CircuitBoard", 4),
				Kv("Bolt", 10), Kv("Hinge", 10), Kv("MetalIngot", 40), Kv("CopperIngot", 20), Kv("Nail", 60), Kv("Glass", 20), Kv("Plank", 60), Kv("Plastic", 60), Kv("Rope", 40),
			},
			new[] // 8: hunting and the deep
			{
				Kv("NetGun", 1), Kv("NetCanister", 20), Kv("Arrow_Metal", 40), Kv("HealingSalve_Good", 10), Kv("Placeable_BeeHive", 2), Kv("FishingBait_Advanced", 10),
				Kv("Brick_Dry", 40), Kv("Glass", 20), Kv("Scrap", 40), Kv("MetalOre", 40), Kv("CopperOre", 20), Kv("SilverAlgae", 10), Kv("Claybowl_SimpleFishStew", 5),
				Kv("ClayPlate_BBQ", 5), Kv("Cooked_Salmon", 10),
			},
			new[] // 9: Tangaroa - water by pipe
			{
				Kv("Placeable_WaterTank", 1), Kv("Placeable_Electric_Purifier", 1), Kv("Placeable_Pipe_Water", 20), Kv("Placeable_MotorWheel", 2), Kv("CircuitBoard", 6),
				Kv("Battery", 2), Kv("Plank", 80), Kv("Plastic", 80), Kv("Nail", 80), Kv("MetalIngot", 40), Kv("Scrap", 40), Kv("Rope", 40), Kv("Leather", 20), Kv("Wool", 20),
				Kv("Feather", 20),
			},
			new[] // 10: plenty of everything
			{
				Kv("Flipper", 2), Kv("OxygenBottle", 4), Kv("NetCanister", 40), Kv("Battery", 4), Kv("MetalIngot", 60), Kv("Scrap", 60), Kv("Plank", 100), Kv("Plastic", 100),
				Kv("Rope", 60), Kv("Nail", 100), Kv("Bolt", 20), Kv("Hinge", 20), Kv("CopperIngot", 20), Kv("CircuitBoard", 6), Kv("Glass", 20), Kv("Thatch", 40),
				Kv("SeaVine", 40), Kv("VineGoo", 20), Kv("Sand", 40), Kv("Clay", 40), Kv("Stone", 40), Kv("Leather", 20), Kv("Feather", 20), Kv("BioFuel", 20),
				Kv("Placeable_Lantern_Basic", 4), Kv("HealingSalve_Good", 10), Kv("ClayPlate_SharkDinner", 5), Kv("Claybowl_HeartyStew", 5), Kv("DrinkingGlass_SimpleSmoothie", 5),
			},
		};

		/// <summary>On top of the storages' own lists: 15 planks per level, then levels 4-10's adds.</summary>
		static List<KeyValuePair<string, int>> Extras(int level)
		{
			var list = new List<KeyValuePair<string, int>> { Kv("Plank", 15 * level) };
			for (int lv = 4; lv <= level && lv < Adds.Length; lv++) list.AddRange(Adds[lv]);
			return list;
		}

		/// <summary>Tests: how many items a level's storages hold in all.</summary>
		public static int SupplyTotal(int level) { level = Mathf.Clamp(level, 1, Max); return Lists(level).Sum(x => x.Sum(kv => kv.Value)) + Extras(level).Sum(kv => kv.Value); }
		/// <summary>Tests: how many of one item a level's storages hold.</summary>
		public static int SupplyOf(int level, string item) { level = Mathf.Clamp(level, 1, Max); return Lists(level).Sum(x => x.Where(kv => kv.Key == item).Sum(kv => kv.Value)) + Extras(level).Where(kv => kv.Key == item).Sum(kv => kv.Value); }

		/// <summary>The supplies in the storages: levels 1-3's lists each to its storage, the extras from the next storage on;
		/// each spills over into the next storage when one is full, and only as much goes in as there is room for.</summary>
		static string Stock(List<Block> placed)
		{
			List<Storage_Small> storages = placed.Select(b => b.GetComponentInChildren<Storage_Small>(true)).Where(s => s != null).ToList();
			List<Inventory> invs = storages.Select(s => Traverse.Create(s).Field("inventoryReference").GetValue<Inventory>()).ToList();
			Log("storage slots: " + string.Join(", ", storages.Select((s, i) => s.name.Replace("(Clone)", "") + " " + (invs[i] != null ? invs[i].allSlots.Count : 0)).ToArray()));
			int items = 0, left = 0;
			var leftKinds = new List<string>();
			Action<KeyValuePair<string, int>, int> put = (kv, start) =>
			{
				Item_Base item = ItemManager.GetItemByName(kv.Key);
				int got = item != null ? Put(invs, start, item, kv.Value) : 0;
				if (item == null) Log("no item " + kv.Key);
				items += got;
				if (got < kv.Value) { left += kv.Value - got; if (!leftKinds.Contains(kv.Key)) leftKinds.Add(kv.Key); }
			};
			List<KeyValuePair<string, int>[]> lists = Lists(Level);
			for (int i = 0; i < lists.Count; i++)
				foreach (var kv in lists[i]) put(kv, i);
			foreach (var kv in Extras(Level)) put(kv, lists.Count);
			if (left > 0) Log("left out (no room): " + left + " of " + string.Join(", ", leftKinds.ToArray()));
			return storages.Count + " storages hold " + items + " items" + (left > 0 ? " (" + left + " left out: " + string.Join(", ", leftKinds.ToArray()) + ")" : "");
		}

		static int StackOf(Item_Base item)
		{
			if (item.settings_Inventory == null || !item.settings_Inventory.Stackable) return 1;
			return Math.Max(1, item.settings_Inventory.StackSize);
		}

		/// <summary>How many of the item still fit in the inventory: its empty slots and its own stacks' rest.</summary>
		static int Room(Inventory inv, Item_Base item)
		{
			int stack = StackOf(item), room = 0;
			foreach (Slot s in inv.allSlots)
			{
				if (s == null) continue;
				if (s.IsEmpty) room += stack;
				else if (s.itemInstance != null && s.itemInstance.UniqueName == item.UniqueName) room += Math.Max(0, stack - s.itemInstance.Amount);
			}
			return room;
		}

		/// <summary>Puts up to amount of the item in the storages from start on (round to the first), a stack at a time and no
		/// more than there is room for, so nothing drops; checks each by the count. Returns how many went in.</summary>
		static int Put(List<Inventory> invs, int start, Item_Base item, int amount)
		{
			int got = 0;
			for (int n = 0; n < invs.Count && got < amount; n++)
			{
				Inventory inv = invs[(start + n) % invs.Count];
				if (inv == null) continue;
				while (got < amount)
				{
					int chunk = Math.Min(Math.Min(Room(inv, item), amount - got), StackOf(item));
					if (chunk <= 0) break;
					int before = inv.GetItemCount(item.UniqueName);
					inv.AddItem(item.UniqueName, chunk);
					int added = inv.GetItemCount(item.UniqueName) - before;
					if (added > 0) got += added;
					if (added != chunk) { Log(item.UniqueName + ": " + chunk + " put in, " + added + " counted"); break; }
				}
			}
			return got;
		}

		#endregion

		#region The receiver

		/// <summary>Level 3 up: the Receiver's 3 antennas on the raft as far apart as Raft asks, a battery in it. The antennas
		/// go on free station cells first, then the free deck's outer cells, last the bare edge (as far as Raft's distances
		/// allow on each).</summary>
		static string SetUpReceiver(Network_Player player, List<Block> placed, Vector3 o, float top, int x0, int z0, Layout lay, HashSet<Vector2Int> taken)
		{
			Block rb = placed.FirstOrDefault(b => b.buildableItem != null && b.buildableItem.UniqueName == Receiver);
			Reciever rec = rb != null ? rb.GetComponentInChildren<Reciever>(true) : null;
			if (rec == null) return "; no receiver";
			Vector3 at = rb.transform.localPosition;
			float min = 0f, max = 1000f, apart = 0f;
			try
			{
				object d = Traverse.Create(rec).Field("requiredAntennaDistance").GetValue();
				if (d != null) { min = Traverse.Create(d).Field("minValue").GetValue<float>(); max = Traverse.Create(d).Field("maxValue").GetValue<float>(); }
				apart = Traverse.Create(rec).Field("minDistanceBetweenAntennas").GetValue<float>();
			}
			catch (Exception e) { Log("receiver distances: " + e.Message); }
			int w = lay.W, l = lay.L;
			Func<Vector2Int, Vector3> pos = c => o + new Vector3((x0 + c.x) * Cell, top, (z0 + c.y) * Cell);
			var tiers = new List<List<Vector2Int>> { lay.Spare.ToList(), new List<Vector2Int>(), new List<Vector2Int>() };
			for (int j = lay.DeckFrom; j < lay.DeckFrom + Deck; j++) { tiers[1].Add(new Vector2Int(1, j)); tiers[1].Add(new Vector2Int(w - 2, j)); }
			for (int i = 0; i < w; i++)
				for (int j = 0; j < l; j++)
					if (i == 0 || i == w - 1 || j == 0 || j == l - 1) tiers[2].Add(new Vector2Int(i, j));
			var chosen = new List<Vector3>();
			var cells = new List<Vector3>();
			foreach (List<Vector2Int> tier in tiers)
			{
				// (the cells of this tier and the ones before, the farthest from each other first, within Raft's distance)
				foreach (Vector2Int c in tier)
				{
					if (taken.Contains(new Vector2Int(x0 + c.x, z0 + c.y))) continue;
					Vector3 p = pos(c);
					float d = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(at.x, 0f, at.z));
					if (d > min + 0.5f && d < max - 0.5f && placed.All(b => (b.transform.localPosition - p).sqrMagnitude > 1f)) cells.Add(p);
				}
				chosen.Clear();
				var left = cells.ToList();
				while (chosen.Count < 3 && left.Count > 0)
				{
					Vector3 best = left.OrderByDescending(c => chosen.Count == 0 ? (c - at).sqrMagnitude : chosen.Min(x => (x - c).sqrMagnitude)).First();
					left.Remove(best);
					if (chosen.All(x => Vector3.Distance(x, best) > apart + 0.5f)) chosen.Add(best);
				}
				if (chosen.Count >= 3) break;
			}
			Log("receiver: antennas " + min.ToString("F1") + "-" + max.ToString("F1") + " m from it, " + apart.ToString("F1") + " m apart; " + cells.Count + " cells fit");
			Item_Base antenna = ItemManager.GetItemByName("Placeable_Reciever_Antenna");
			int antennas = 0;
			foreach (Vector3 c in chosen) { Block a = Place(player, antenna, c, 0f); if (a != null) { placed.Add(a); antennas++; } }
			Item_Base battery = ItemManager.GetItemByName("Battery");
			Battery slot = rb.GetComponentInChildren<Battery>(true);
			bool charged = slot != null && battery != null && slot.Insert(null, battery.MaxUses, battery.UniqueIndex); // (no player: it would take it from a hand)
			return "; the receiver with " + antennas + " antennas" + (charged ? " and a battery" : ", no battery");
		}

		#endregion

		[ConsoleCommand(name: "HeadStart", docs: "The world setting Head start raft: HeadStart = this world's level and the next new world's; HeadStart 0-10 (main menu) = the next new world's level (0 = off)")]
		public static void HeadStartCommand(string[] args)
		{
			int n;
			if (args != null && args.Length > 0 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
			{
				if (LoadSceneManager.IsGameSceneLoaded) { Debug.Log("[CUSTOM ISLANDS] The head start is chosen for a new world, in the main menu"); }
				else { Chosen = n; SaveDefault(Chosen); try { WorldSettingsWindow.Show(); } catch { } }
			}
			Debug.Log("[CUSTOM ISLANDS] Head start raft: this world " + Describe(Level) + (Done ? " (built)" : "") + ", the next new world " + Describe(Chosen) + (LastBuild.Length > 0 ? " - " + LastBuild : ""));
		}
	}
}
