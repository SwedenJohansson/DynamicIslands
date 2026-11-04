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
	/// The world option "Trader raft" (WorldOptions.TraderRaft): very rarely a merchant's raft comes up ahead while sailing
	/// (host; the map type "traderraft", so every player gets the same file), the way ghost rafts do. On its deck stand a few
	/// stalls, each one offer - basic resources for seeds, rare plants or now and then a blueprint, at fair prices - with a
	/// little stock (TradeStand). Not within the first 3 km of a world; then about one per 7 km.
	///   - The offers come from the raft's seed: the same file, the same offers on every machine.
	///   - The stock is shared: each trade uses up one unit for everyone (ContentState, one player per unit - Claims), saved
	///     with the world like an opened chest. It doesn't fill up again.
	/// </summary>
	public static class TraderRaft
	{
		public const string TypeName = "traderraft";
		const float FirstAfter = 3000f, ChancePerKm = 0.14f, RetryMetres = 200f;

		static float sailed, sinceTry;
		static bool due;

		/// <summary>Tests: how many were brought in this world since it loaded.</summary>
		public static int Brought;

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [trader] " + msg); }

		internal static void Reset() { sailed = sinceTry = 0f; due = false; Brought = 0; }

		internal static bool ReadLine(string key, string value)
		{
			if (key != "tradersailed") return false;
			float v;
			// (a NaN or Infinity in a hand-edited world file would stick: NaN plus the distance sailed stays NaN)
			sailed = float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f ? v : 0f;
			return true;
		}

		internal static IEnumerable<string> WriteLines()
		{
			if (sailed > 0f) yield return "@tradersailed=" + sailed.ToString("F0", CultureInfo.InvariantCulture);
		}

		/// <summary>Host: the raft sailed this far; very rarely a trader comes up ahead.</summary>
		internal static void OnSailed(float metres, Vector3 raftPos)
		{
			if (!Raft_Network.IsHost || !WorldOptions.On(WorldOptions.TraderRaft)) return;
			sailed += metres;
			if (!due && sailed > FirstAfter && UnityEngine.Random.value < 1f - Mathf.Pow(1f - ChancePerKm, metres / 1000f)) { due = true; sinceTry = RetryMetres; }
			if (!due) return;
			sinceTry += metres;
			if (sinceTry < RetryMetres) return;
			sinceTry = 0f;
			string r = CustomIslandSpawner.TrySpawn(raftPos, false, CustomIslandSpawner.TypePrefix + TypeName);
			Log(r);
			if (r.StartsWith("Spawning")) { due = false; sailed = FirstAfter - 3000f; Brought++; }
		}

		#region The offers

		/// <summary>One stall's offer: what the player gives, what they get, how many times.</summary>
		public class Offer
		{
			public List<KeyValuePair<string, int>> Give = new List<KeyValuePair<string, int>>(), Get = new List<KeyValuePair<string, int>>();
			public int Stock;
			public string Kind = "";
		}

		static readonly string[] Fruit = { "Seed_Mango", "Seed_Pineapple", "Seed_Banana", "Seed_Watermelon", "Seed_Strawberry" };
		static readonly string[] Flowers = { "Seed_Flower_Black", "Seed_Flower_Blue", "Seed_Flower_Red", "Seed_Flower_White", "Seed_Flower_Yellow" };
		static readonly string[] Trees = { "Seed_Birch", "Seed_Pine", "Seed_Palm" };
		/// <summary>Blueprints a trader may sell: handy, none of the story's.</summary>
		static readonly string[] Blueprints = { "Blueprint_Canteen", "Blueprint_DetailPlank", "Blueprint_Firework", "Blueprint_Machete", "Blueprint_HeadLight", "Blueprint_Storage_Large", "Blueprint_MetalDetector" };

		static KeyValuePair<string, int> P(string item, int n) { return new KeyValuePair<string, int>(item, n); }

		/// <summary>The offers of the trader with this seed (3 or 4; the same on every machine).</summary>
		public static List<Offer> OffersOf(int seed)
		{
			var r = new System.Random(seed ^ 0x7EAD);
			var list = new List<Offer>();
			// A fruit seed for planks, flower seeds for plastic, a tree for planks and rope
			list.Add(new Offer { Kind = "fruit", Give = { P("Plank", 10 + r.Next(5)) }, Get = { P(Fruit[r.Next(Fruit.Length)], 1) }, Stock = 2 + r.Next(2) });
			list.Add(new Offer { Kind = "flowers", Give = { P("Plastic", 6 + r.Next(4)) }, Get = { P(Flowers[r.Next(Flowers.Length)], 2) }, Stock = 2 + r.Next(2) });
			list.Add(new Offer { Kind = "tree", Give = { P("Plank", 8 + r.Next(5)), P("Rope", 2 + r.Next(2)) }, Get = { P(Trees[r.Next(Trees.Length)], 1) }, Stock = 1 + r.Next(2) });
			// Now and then a blueprint, dear: one only
			if (r.NextDouble() < 0.4)
				list.Add(new Offer { Kind = "blueprint", Give = { P("Plank", 16 + r.Next(6)), P("Plastic", 8 + r.Next(5)), P("Scrap", 4 + r.Next(3)) }, Get = { P(Blueprints[r.Next(Blueprints.Length)], 1) }, Stock = 1 });
			return list;
		}

		public static string Text(IEnumerable<KeyValuePair<string, int>> items)
		{
			return string.Join(" and ", items.Select(l => l.Value + " " + ContentCatalog.ItemLabel(l.Key)).ToArray());
		}

		#endregion

		#region The map type

		/// <summary>Settings of a new trader raft (its reach, for finding room at sea).</summary>
		internal static IslandGenSettings Settings(System.Random rnd)
		{
			return new IslandGenSettings { Seed = rnd.Next(1, 999999), Height = 2f, Radius = 16f };
		}

		static readonly string[] Greetings =
		{
			"Fair prices, honest goods. Take what you need, leave what it costs. - the trader",
			"Seeds from every island I've passed. Planks and plastic welcome. Don't touch the hut.",
			"I trade, I don't haggle. What's on the stalls is what there is.",
		};

		/// <summary>A merchant's raft on open water: a hut, a lantern, a note and a stall per offer.</summary>
		internal static IslandFile Build(IslandGenSettings s, string name)
		{
			var f = new IslandFile
			{
				Name = name, TerrainSize = IslandGenerator.BuildArea, HeightmapResolution = IslandGenerator.BuildResolution,
				Heights = new float[IslandGenerator.BuildResolution, IslandGenerator.BuildResolution],
			};
			var k = new MapKit(f, s.Seed);
			var rnd = new System.Random(s.Seed);
			float g = PlacementOptions.GridSize, sea = k.Sea;
			float floatY = sea + PlacementOptions.FoundationFloat, deck = floatY + PlacementOptions.FoundationPlanks;
			const int w = 5, d = 4;
			Vector3 o = new Vector3(k.Mid.x - w * g / 2f, 0f, k.Mid.y - d * g / 2f);
			Func<int, int, Vector3> at = (x, z) => o + new Vector3(x * g, deck, z * g);
			for (int x = 0; x < w; x++)
				for (int z = 0; z < d; z++)
					k.Add("Block_Foundation", o + new Vector3(x * g, floatY, z * g), 0f, null, 0f);
			// The trader's hut at the back (2 x 2), the stalls along the front row
			Hut(k, at, g, 3, 2);
			List<Offer> offers = OffersOf(s.Seed);
			for (int i = 0; i < offers.Count; i++)
				k.Add(TradeStand.StallObject, at(i, 0), 180f, TradeStand.Props(offers[i]), 0f);
			k.Add("Placeable_Lantern_Basic", at(1, 2), 0f, null, 0f);
			k.Add("Note_Paper", at(0, 2) + new Vector3(0.3f, 0f, 0.3f), 0f, new Dictionary<string, string> { { ObjectProps.NoteTitle, "Trader" }, { ObjectProps.NoteText, Greetings[rnd.Next(Greetings.Length)] } }, 0f);
			k.Add("Block_Ladder", at(w, d / 2) + new Vector3(-g / 2, 0f, 0f), 90f, null, 0f);
			f.Props[IslandProps.Title] = "Trader raft";
			return f;
		}

		static void Hut(MapKit k, Func<int, int, Vector3> at, float g, int x, int z)
		{
			Vector3 c = at(x, z);
			foreach (Vector3 p in new[] { new Vector3(-g / 2, 0f, -g / 2), new Vector3(g * 1.5f, 0f, -g / 2), new Vector3(-g / 2, 0f, g * 1.5f), new Vector3(g * 1.5f, 0f, g * 1.5f) })
				k.Add("Block_Pillar_Wood", c + p, 0f, null, 0f);
			k.Add("Block_Wall_Thatch", c + new Vector3(g, 0f, g * 1.5f), 180f, null, 0f);
			k.Add("Block_Wall_Thatch", c + new Vector3(g * 1.5f, 0f, g), 270f, null, 0f);
			RaftRoof.Hip((n, p, ry) => k.Add(n, p, ry, null, 0f), c + new Vector3(0f, RaftRoof.OnWalls, 0f), 2, 2, false, i => false);
		}

		#endregion

		[ConsoleCommand(name: "Traders", docs: "The world option Trader raft: Traders = the trader rafts loaded now and their stalls' stock; Traders offers <seed> = what a trader raft with that seed sells; Traders bring = (host) a trader raft ahead now, even beside one of Raft's islands")]
		public static void TradersCommand(string[] args)
		{
			int seed;
			if (args != null && args.Length > 1 && args[0] == "offers" && int.TryParse(args[1], out seed))
			{
				Debug.Log("[CUSTOM ISLANDS] Traders: seed " + seed + ": " + string.Join(" | ", OffersOf(seed).Select(x => Text(x.Give) + " -> " + Text(x.Get) + " x" + x.Stock).ToArray()));
				return;
			}
			if (args != null && args.Length > 0 && args[0] == "bring")
			{
				if (!Raft_Network.IsHost || !CustomIslandSpawner.RaftPosition.HasValue) { Debug.Log("[CUSTOM ISLANDS] Traders: bring - the host, in a world"); return; }
				Debug.Log("[CUSTOM ISLANDS] Traders: bring - " + CustomIslandSpawner.TrySpawn(CustomIslandSpawner.RaftPosition.Value, true, CustomIslandSpawner.TypePrefix + TypeName)); // (also beside one of Raft's islands)
				return;
			}
			TradeStand[] stands = UnityEngine.Object.FindObjectsOfType<TradeStand>();
			Debug.Log("[CUSTOM ISLANDS] Traders: " + (WorldOptions.On(WorldOptions.TraderRaft) ? "on" : "off") + ", sailed " + Mathf.RoundToInt(sailed) + " m, " + stands.Length + " stalls" +
				(stands.Length == 0 ? "" : ": " + string.Join(" | ", stands.OrderBy(t => t.Index).Select(t => "#" + t.Index + " " + Text(t.Give) + " -> " + Text(t.Get) + " " + t.Left + "/" + t.Stock).ToArray())));
		}
	}

	/// <summary>
	/// A stall with one offer ("trade.give", "trade.get", "trade.stock" in its settings): looking at it shows "[E] Trade
	/// 12 Plank for 1 Mango seed (2 left)"; trading takes the price from the player's inventory and gives the goods. Each
	/// unit of stock is a key of the island's state (KeyFor: KeyBase + object index * 8 + unit; past object 8191 WideKeyBase): used for everyone, one player
	/// gets each (Claims). Stock 0 (GW1, the editor's "no limit"): never sells out, no keys. The price may hold story
	/// items ("story:id*1"); a trade fires the stand's "trade" event (its behaviours: on.trade=signal|...).
	/// </summary>
	public class TradeStand : MonoBehaviour, IRaycastable
	{
		public const int KeyBase = 0xC0000, MaxStock = 8; // (after QuestItemPickups.KeyBase 0xB0000 + 0xFFFF)
		/// <summary>RK9: stands past object 8191 (0x2000 * 8 keys fill KeyBase's range): their own range, after
		/// StoryItems.StoryGivenBase 0x170000 + 0xFFFF, so a big island's stands don't share keys. Indices below keep the old keys.</summary>
		public const int WideKeyBase = 0x180000, NarrowCount = 0x2000;
		public const string GiveProp = "trade.give", GetProp = "trade.get", StockProp = "trade.stock";
		/// <summary>The behaviour event a trade fires on the stand (GW1).</summary>
		public const string TradeEvent = "trade";
		/// <summary>The stall's object: the floating raft's table.</summary>
		public const string StallObject = "Table_Floor";

		public List<KeyValuePair<string, int>> Give = new List<KeyValuePair<string, int>>(), Get = new List<KeyValuePair<string, int>>();
		public int Stock = 1, Index;

		public static bool IsTrade(IDictionary<string, string> props) { return props != null && props.ContainsKey(GiveProp) && props.ContainsKey(GetProp); }

		public static Dictionary<string, string> Props(TraderRaft.Offer o)
		{
			Func<List<KeyValuePair<string, int>>, string> s = l => string.Join(";", l.Select(x => x.Key + "*" + x.Value).ToArray());
			return new Dictionary<string, string> { { GiveProp, s(o.Give) }, { GetProp, s(o.Get) }, { StockProp, o.Stock.ToString(CultureInfo.InvariantCulture) } };
		}

		static List<KeyValuePair<string, int>> Items(IDictionary<string, string> props, string key)
		{
			return ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, ObjectProps.Get(props, key) } });
		}

		public static TradeStand Attach(GameObject go, IDictionary<string, string> props, int index)
		{
			GameObject holder = CustomNote.InteractHolder(go);
			TradeStand t = holder.AddComponent<TradeStand>();
			t.Give = Items(props, GiveProp);
			t.Get = Items(props, GetProp);
			int n;
			t.Stock = StockOf(props, out n) ? (n <= 0 ? 0 : Mathf.Clamp(n, 1, MaxStock)) : 1;
			t.Index = index;
			return t;
		}

		static bool StockOf(IDictionary<string, string> props, out int n)
		{
			return int.TryParse(ObjectProps.Get(props, StockProp).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
		}

		/// <summary>The stock a stand's settings give (0 = no limit; no setting = 1).</summary>
		public static int StockIn(IDictionary<string, string> props)
		{
			int n;
			return StockOf(props, out n) ? (n <= 0 ? 0 : Mathf.Clamp(n, 1, MaxStock)) : 1;
		}

		/// <summary>Stock 0: the stand never sells out (no unit keys, no claims).</summary>
		public bool Unlimited { get { return Stock <= 0; } }

		public int KeyOf(int unit) { return KeyFor(Index, unit); }

		/// <summary>A stand's unit key from its object index (0..0xFFFF).</summary>
		public static int KeyFor(int index, int unit)
		{
			index &= 0xFFFF;
			return index < NarrowCount ? KeyBase + index * MaxStock + unit : WideKeyBase + index * MaxStock + unit;
		}

		/// <summary>The object index and unit of a stand key, or false when the key isn't one.</summary>
		public static bool FromKey(int key, out int index, out int unit)
		{
			index = unit = -1;
			if (key >= KeyBase && key < KeyBase + NarrowCount * MaxStock) { index = (key - KeyBase) / MaxStock; unit = (key - KeyBase) % MaxStock; return true; }
			if (key >= WideKeyBase + NarrowCount * MaxStock && key < WideKeyBase + 0x10000 * MaxStock) { index = (key - WideKeyBase) / MaxStock; unit = (key - WideKeyBase) % MaxStock; return true; }
			return false;
		}

		IslandWorldState.Entry Entry { get { return ContentState.EntryOf(transform); } }

		/// <summary>The first unit not sold yet (-1: sold out).</summary>
		public int NextUnit
		{
			get
			{
				if (Unlimited) return 0;
				IslandWorldState.Entry e = Entry;
				for (int i = 0; i < Stock; i++) if (!ContentState.IsUsed(e, KeyOf(i))) return i;
				return -1;
			}
		}

		public int Left
		{
			get { if (Unlimited) return int.MaxValue; IslandWorldState.Entry e = Entry; int n = 0; for (int i = 0; i < Stock; i++) if (!ContentState.IsUsed(e, KeyOf(i))) n++; return n; }
		}

		/// <summary>What the local player lacks for this trade ("" = nothing).</summary>
		public string Lacks()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || player.Inventory == null) return "no player";
			var lack = Give.Where(l => Have(player, l.Key) < l.Value).ToList();
			return lack.Count == 0 ? "" : TraderRaft.Text(lack);
		}

		/// <summary>How many of an item the player has (a story item: the crew's).</summary>
		static int Have(Network_Player player, string item)
		{
			return StoryItems.IsStory(item) ? StoryBook.Count(StoryItems.IdOf(item)) : player.Inventory.GetItemCount(item);
		}

		void IRaycastable.OnIsRayed()
		{
			if (NoteReader.IsOpen) return;
			DisplayTextManager hints = CustomNote.Hints;
			int left = Left;
			string offer = "Trade " + TraderRaft.Text(Give) + " for " + TraderRaft.Text(Get);
			string lacks = left > 0 ? Lacks() : "";
			if (hints != null)
			{
				if (left == 0) hints.ShowText("Sold out (" + TraderRaft.Text(Get) + ")", 0, true, 0);
				else if (lacks.Length > 0) hints.ShowText(offer + " - you need " + lacks, 0, true, 0);
				else hints.ShowText(offer + (Unlimited ? "" : " (" + left + " left)"), CustomNote.InteractKey, 0, 0, true);
			}
			if (left > 0 && lacks.Length == 0 && CustomNote.InteractPressed())
			{
				if (hints != null) hints.HideDisplayTexts();
				Trade();
			}
		}

		void IRaycastable.OnRayEnter() { }

		void IRaycastable.OnRayExit()
		{
			DisplayTextManager hints = CustomNote.Hints;
			if (hints != null) hints.HideDisplayTexts();
		}

		/// <summary>What the last trade here gave this player (tests: a client's trade waits for the host's yes).</summary>
		public List<string> LastGiven = new List<string>();

		/// <summary>The local player trades once: pays, gets the goods, one unit gone for everyone. Returns what was given.</summary>
		public List<string> Trade()
		{
			int unit = NextUnit;
			if (unit < 0 || Lacks().Length > 0) return new List<string>();
			if (Unlimited) return Pay(-1);
			int key = KeyOf(unit);
			if (!Claims.May(Entry, key, yes => { if (this == null) return; if (yes) Trade(); else Beaten(); }))
			{
				if (Raft_Network.IsHost) Beaten();
				return new List<string>();
			}
			// (granted, but the goods to pay with went meanwhile: the hold is let go at once - RK6)
			if (Lacks().Length > 0) { Claims.Release(Entry, key); return new List<string>(); }
			return Pay(key);
		}

		/// <summary>Pays, marks the unit (key; -1: a stand without a limit) and gives the goods; then the "trade" event.</summary>
		List<string> Pay(int key)
		{
			// (paid and marked first, so a second press in the same moment can't trade twice)
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || player.Inventory == null || Lacks().Length > 0) return new List<string>();
			foreach (KeyValuePair<string, int> l in Give)
			{
				if (StoryItems.IsStory(l.Key)) StoryBook.Take(StoryItems.IdOf(l.Key), l.Value);
				else player.Inventory.RemoveItem(l.Key, l.Value);
			}
			if (key >= 0) ContentState.MarkUsed(transform, key);
			List<string> given = TriggerZone.Give(Get);
			LastGiven = given;
			Debug.Log("[CUSTOM ISLANDS] [trader] Traded " + TraderRaft.Text(Give) + " for " + string.Join(", ", given.ToArray()) + (Unlimited ? " (no limit)" : " (" + Left + " left)"));
			IslandInfo.ShowMessage("Traded for " + TraderRaft.Text(Get));
			// (GW1: the island maker's quests hear of it - "on.trade=signal|paid")
			try { Behaviours.Fire(Entry, Index, TradeEvent, true); }
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] [trader] trade event: " + ex.Message); }
			return given;
		}

		void Beaten()
		{
			Debug.Log("[CUSTOM ISLANDS] [trader] That one went to another player");
			IslandInfo.ShowMessage("Someone else bought that one first");
		}
	}
}
