using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The 11-04 gap features (GapWorld): GW1 trade stands with the island maker's own offer (CITradeStand1104) and GW8
	/// co-op zones (CICoopZone1104 alone, CIGw1104Mp for two players on host + box).
	/// </summary>
	public static partial class DevTests
	{
		/// <summary>The sample island with its middle cleared; at(dx, dz) is a spot on its ground.</summary>
		static IslandFile Gw4File(string name, out Func<float, float, Vector3> at)
		{
			IslandFile f = SampleIsland();
			f.Name = name;
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			float step = f.TerrainSize.x / (f.HeightmapResolution - 1);
			IslandFile ff = f;
			at = (dx, dz) =>
			{
				float x = c.x + dx, z = c.y + dz;
				return new Vector3(x, ff.Heights[Mathf.RoundToInt(z / step), Mathf.RoundToInt(x / step)] * ff.TerrainSize.y, z);
			};
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 25f);
			return f;
		}

		static TradeStand Gw4Stand(IslandWorldState.Entry e, string name)
		{
			IslandObjectRef r = ScObjOf(e, name);
			return r != null ? r.GetComponentInChildren<TradeStand>(true) : null;
		}

		static TriggerZone Gw4Zone(IslandWorldState.Entry e, string id)
		{
			return e != null && e.Root != null ? e.Root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase)) : null;
		}

		static int Gw4Index(TriggerZone z) { IslandObjectRef r = z != null ? z.GetComponent<IslandObjectRef>() : null; return r != null ? r.Index : -1; }

		static void Gw4Take(Network_Player player, string item)
		{
			int n = player.Inventory.GetItemCount(item);
			if (n > 0) player.Inventory.RemoveItem(item, n);
		}

		#region GW1 trade stand

		[ConsoleCommand(name: "CITradeStand1104", docs: "Dev, in game (host): GW1 trade stands with the island maker's own offer - a stand without a limit (stock 0) trades again and again, keeps no key and runs its 'trade' event each time (signal); a stand priced in a story item takes the crew's story items and sells out after its stock; both keep their state across a reload; the inspector's tidy-up of an offer and the offer text")]
		public static void TradeStand1104Command(string[] args) { StartTest(TradeStand1104Routine()); }

		static IEnumerator TradeStand1104Routine()
		{
			string where = GwWhere("CITradeStand1104");
			if (where != null) { Fail(where); yield break; }
			yield return EnsureAlive();
			const string island = "ci-gw-trade";
			Network_Player player = RAPI.GetLocalPlayer();
			var g = new GwIsland();
			int trades = 0;
			Action<int, int, string> heard = (id, index, ev) => { if (g.E != null && id == g.E.Id && ev == TradeStand.TradeEvent) trades++; };
			Behaviours.Fired += heard;
			bool ok = true;
			try
			{
				// (the inspector's tidy-up and the offer text: no world needed)
				Check(ref ok, ObjectInspector.Clean("Plank * 12 ; Rope;") == "Plank*12;Rope*1", "the inspector tidies 'Plank * 12 ; Rope;' to 'Plank*12;Rope*1' (" + ObjectInspector.Clean("Plank * 12 ; Rope;") + ")");
				var pa = new Dictionary<string, string> { { TradeStand.GiveProp, "Plank*2" }, { TradeStand.GetProp, "Rope*1" }, { TradeStand.StockProp, "0" } };
				var pb = new Dictionary<string, string> { { TradeStand.GiveProp, "Plank*2" }, { TradeStand.GetProp, "Rope*1" } };
				Check(ref ok, GapWorld.TradeText(pa).EndsWith("no limit") && TradeStand.StockIn(pa) == 0, "stock 0 reads as no limit (" + GapWorld.TradeText(pa) + ")");
				Check(ref ok, TradeStand.StockIn(pb) == 1 && GapWorld.TradeText(pb).EndsWith("1 in stock"), "no stock setting = 1, as before (" + GapWorld.TradeText(pb) + ")");
				Check(ref ok, BehaviourProps.EventsFor(TradeStand.StallObject, pa).Any(kv => kv.Key == TradeStand.TradeEvent), "a stand lists the 'trade' event");
				Check(ref ok, !BehaviourProps.EventsFor("Loot_ChestSmall", new Dictionary<string, string>(pa) { { ObjectProps.LootItems, "Plank*1" } }).Any(kv => kv.Key == TradeStand.TradeEvent), "a chest with trade settings lists no 'trade' event");

				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(island, out at);
				f.Objects.Add(new IslandObject { Name = TradeStand.StallObject, Position = at(-3f, 0f), Props = P(BehaviourProps.Name, "gw-stand-a", TradeStand.GiveProp, "Plank*2", TradeStand.GetProp, "Rope*1",
					TradeStand.StockProp, "0", BehaviourProps.EventKey(TradeStand.TradeEvent), "signal||gw-paid") });
				f.Objects.Add(new IslandObject { Name = TradeStand.StallObject, Position = at(3f, 0f), Props = P(BehaviourProps.Name, "gw-stand-b", TradeStand.GiveProp, "story:gw-coin*1", TradeStand.GetProp, "Scrap*3",
					TradeStand.StockProp, "2") });
				f.Props[StoryItems.Key] = "gw-coin|Old coin||A coin the stall keeper takes.";
				yield return GwSpawnFile(g, f, island);
				IslandWorldState.Entry e = g.E;
				if (e == null) yield break;
				TradeStand a = Gw4Stand(e, "gw-stand-a"), b = Gw4Stand(e, "gw-stand-b");
				if (a == null || b == null) { Fail("the stands did not get a TradeStand (a " + (a != null) + ", b " + (b != null) + ")"); yield break; }
				Check(ref ok, a.Unlimited && a.Left == int.MaxValue && a.NextUnit == 0, "stand A (stock 0) has no limit");
				Check(ref ok, !b.Unlimited && b.Stock == 2 && b.Left == 2, "stand B has 2 in stock (" + b.Stock + ", " + b.Left + " left)");

				// Stand A: three trades, no keys, the event each time
				Gw4Take(player, "Plank"); Gw4Take(player, "Rope"); Gw4Take(player, "Scrap");
				player.Inventory.AddItem("Plank", 6);
				int keysBefore = e.State.Count;
				for (int i = 0; i < 3; i++) { a.Trade(); yield return new WaitForSeconds(0.3f); }
				Check(ref ok, player.Inventory.GetItemCount("Rope") == 3 && player.Inventory.GetItemCount("Plank") == 0, "three trades at A: 6 planks paid, 3 ropes got (" + player.Inventory.GetItemCount("Rope") + " rope, " + player.Inventory.GetItemCount("Plank") + " plank)");
				Check(ref ok, a.Lacks() == "2 Plank" || a.Lacks().Length > 0, "with no planks left A says what is missing (" + a.Lacks() + ")");
				Check(ref ok, a.Trade().Count == 0 && player.Inventory.GetItemCount("Rope") == 3, "a trade without the price gives nothing");
				Check(ref ok, !e.State.Keys.Any(k => { int ix, u; return TradeStand.FromKey(k, out ix, out u) && ix == a.Index; }), "A keeps no sold key");
				Check(ref ok, trades == 3, "A ran its 'trade' event 3 times (" + trades + ")");
				Check(ref ok, Behaviours.HasSignal(e, "gw-paid"), "A's on.trade sent the signal 'gw-paid'");

				// Stand B: priced in a story item, sells out
				StoryBook.Take("gw-coin", StoryBook.Count("gw-coin"));
				Check(ref ok, b.Lacks().Length > 0 && b.Trade().Count == 0, "without the coin B won't trade (" + b.Lacks() + ")");
				StoryBook.Give("gw-coin", 3);
				yield return new WaitForSeconds(0.3f);
				b.Trade(); yield return new WaitForSeconds(0.5f);
				b.Trade(); yield return new WaitForSeconds(0.5f);
				Check(ref ok, b.Left == 0 && b.NextUnit < 0, "B sold out after 2 trades (" + b.Left + " left)");
				Check(ref ok, StoryBook.Count("gw-coin") == 1, "B took 2 of the crew's 3 coins (" + StoryBook.Count("gw-coin") + " left)");
				Check(ref ok, player.Inventory.GetItemCount("Scrap") == 6, "B gave 6 scrap (" + player.Inventory.GetItemCount("Scrap") + ")");
				b.Trade(); yield return new WaitForSeconds(0.3f);
				Check(ref ok, StoryBook.Count("gw-coin") == 1 && player.Inventory.GetItemCount("Scrap") == 6, "a sold-out B takes nothing more");

				// Reload: B stays sold out, A still has no limit
				yield return GwReload(e);
				a = Gw4Stand(e, "gw-stand-a"); b = Gw4Stand(e, "gw-stand-b");
				Check(ref ok, a != null && b != null, "the stands are back after the reload");
				if (a != null && b != null)
				{
					Check(ref ok, b.Left == 0, "B is still sold out after the reload (" + b.Left + " left)");
					player.Inventory.AddItem("Plank", 2);
					int rope = player.Inventory.GetItemCount("Rope");
					a.Trade(); yield return new WaitForSeconds(0.3f);
					Check(ref ok, a.Unlimited && player.Inventory.GetItemCount("Rope") == rope + 1 && trades == 4, "A trades again after the reload (event " + trades + ")");
				}
			}
			finally
			{
				Behaviours.Fired -= heard;
				Network_Player me = RAPI.GetLocalPlayer();
				if (me != null && me.Inventory != null) { Gw4Take(me, "Plank"); Gw4Take(me, "Rope"); Gw4Take(me, "Scrap"); }
				StoryBook.Take("gw-coin", StoryBook.Count("gw-coin"));
				GwDrop(g, island);
			}
			if (ok) Log("PASS: GW1 trade stand (no limit, story-item price, trade event, reload)");
			else Fail("GW1 trade stand: see the FAIL lines above");
		}

		#endregion

		#region GW8 co-op zone

		[ConsoleCommand(name: "CICoopZone1104", docs: "Dev, in game (host, alone): GW8 co-op zones - the number needed is the zone's but never more than the players online (one player alone is enough); a 'once' zone runs 'crowd' once and keeps it across a reload; a 'while they stand' zone runs 'crowd' each time it fills and 'crowdleft' when it empties (two players faked); the check players|zone|N")]
		public static void CoopZone1104Command(string[] args) { StartTest(CoopZone1104Routine()); }

		static IEnumerator CoopZone1104Routine()
		{
			string where = GwWhere("CICoopZone1104");
			if (where != null) { Fail(where); yield break; }
			yield return EnsureAlive();
			const string island = "ci-gw-coop";
			var g = new GwIsland();
			var crowd = new Dictionary<int, int>();
			var left = new Dictionary<int, int>();
			Action<int, int, string> heard = (id, index, ev) =>
			{
				if (g.E == null || id != g.E.Id) return;
				Dictionary<int, int> d = ev == GapWorld.CrowdEvent ? crowd : ev == GapWorld.CrowdLeftEvent ? left : null;
				if (d == null) return;
				int n; d.TryGetValue(index, out n); d[index] = n + 1;
			};
			Func<Dictionary<int, int>, int, int> times = (d, i) => { int n; return d.TryGetValue(i, out n) ? n : 0; };
			Behaviours.Fired += heard;
			bool ok = true;
			int fake = 0;
			try
			{
				// The rule: the set number, never more than the players online, at least 1
				Check(ref ok, GapWorld.Need(2, 1) == 1 && GapWorld.Need(3, 2) == 2 && GapWorld.Need(2, 2) == 2 && GapWorld.Need(1, 5) == 1 && GapWorld.Need(8, 3) == 3 && GapWorld.Need(4, 0) == 1,
					"need = the set number capped at the players online (2/1->1, 3/2->2, 2/2->2, 1/5->1, 8/3->3, 4/0->1)");
				int n0; bool h0;
				Check(ref ok, GapWorld.CoopOf(P(GapWorld.PlayersProp, "2|hold"), out n0, out h0) && n0 == 2 && h0 && !GapWorld.IsCoop(P(GapWorld.PlayersProp, "0")) && !GapWorld.IsCoop(P()) &&
					GapWorld.CoopOf(P(GapWorld.PlayersProp, "12"), out n0, out h0) && n0 == GapWorld.MaxPlayers && !h0, "zone.players reads N / N|hold, 0 or none = off, capped at 8");
				var hp = P(GapWorld.PlayersProp, "2|hold");
				Check(ref ok, BehaviourProps.EventsFor(ContentCatalog.TriggerZone, hp).Any(kv => kv.Key == GapWorld.CrowdEvent) && BehaviourProps.EventsFor(ContentCatalog.TriggerZone, hp).Any(kv => kv.Key == GapWorld.CrowdLeftEvent)
					&& !BehaviourProps.EventsFor(ContentCatalog.TriggerZone, P(GapWorld.PlayersProp, "2")).Any(kv => kv.Key == GapWorld.CrowdLeftEvent), "a co-op zone lists 'crowd' (+ 'crowdleft' while they stand)");

				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(island, out at);
				f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = at(-9f, 0f), Props = P(BehaviourProps.Name, "gw-plate", ObjectProps.ZoneId, "gwplate", ObjectProps.ZoneRadius, "4",
					GapWorld.PlayersProp, "2", BehaviourProps.EventKey(GapWorld.CrowdEvent), "signal||gw-crowd1") });
				f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = at(9f, 0f), Props = P(BehaviourProps.Name, "gw-hold", ObjectProps.ZoneId, "gwhold", ObjectProps.ZoneRadius, "4",
					GapWorld.PlayersProp, "2|hold", BehaviourProps.EventKey(GapWorld.CrowdEvent), "show|gw-flag|", BehaviourProps.EventKey(GapWorld.CrowdLeftEvent), "hide|gw-flag|") });
				f.Objects.Add(new IslandObject { Name = "Log", Position = at(9f, 9f), Props = P(BehaviourProps.Name, "gw-flag", BehaviourProps.Hidden, "1") });
				OnRaftCommand();
				yield return new WaitForSeconds(0.5f);
				yield return GwSpawnFile(g, f, island);
				IslandWorldState.Entry e = g.E;
				if (e == null) yield break;
				TriggerZone z1 = Gw4Zone(e, "gwplate"), z2 = Gw4Zone(e, "gwhold");
				if (z1 == null || z2 == null) { Fail("the zones did not spawn"); yield break; }
				int i1 = Gw4Index(z1), i2 = Gw4Index(z2);
				yield return new WaitForSeconds(3.5f); // (the zone list is read every 3 s)
				Func<string, bool> holds = check => Behaviours.CheckNow(e, i1, ObjCheck.Parse(check));
				Check(ref ok, times(crowd, i1) == 0 && !holds("players|gwplate|2"), "nobody on the plate: no crowd, the check is false");

				// One player online, a "2" zone: one player is enough
				PutPlayerNear(z1.transform, 0.3f);
				StartTest(StayThere(z1.transform));
				yield return new WaitForSeconds(2.5f);
				Check(ref ok, times(crowd, i1) == 1, "alone on a '2' plate (1 online): crowd ran once (" + times(crowd, i1) + ")");
				Check(ref ok, Behaviours.HasSignal(e, "gw-crowd1") && e.State.ContainsKey(GapWorld.CoopBase + i1), "its signal was sent and the zone's key kept");
				Check(ref ok, holds("players|gwplate|2") && holds("players|gwplate|") && holds("players|gw-plate|8"), "players|gwplate|2 (and the zone's own number, and 8, by object name) is true for one player alone");
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(crowd, i1) == 1, "standing on, a 'once' zone does not run again");
				OnRaftCommand();
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, !holds("players|gwplate|1"), "off the plate the check is false");

				// Two players online (faked): the check wants both
				GapWorld.FakeOnline = 2;
				GapWorld.FakeCount = z => z != null && z.Id == "gwhold" ? fake : 0;
				fake = 1;
				Check(ref ok, !Behaviours.CheckNow(e, i2, ObjCheck.Parse("players|gwhold|2")) && Behaviours.CheckNow(e, i2, ObjCheck.Parse("players|gwhold|1")), "2 online, 1 in the zone: players|gwhold|2 is false, |1 true");
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(crowd, i2) == 0 && !GstShown(e, "gw-flag"), "2 online, 1 on the hold plate: no crowd");
				fake = 2;
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(crowd, i2) == 1 && GstShown(e, "gw-flag"), "both on it: crowd, the flag shows (" + times(crowd, i2) + ")");
				fake = 1;
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(left, i2) == 1 && !GstShown(e, "gw-flag"), "one steps off: crowdleft, the flag hides (" + times(left, i2) + ")");
				fake = 2;
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(crowd, i2) == 2 && GstShown(e, "gw-flag"), "filled again: crowd again (" + times(crowd, i2) + ")");
				GapWorld.FakeOnline = 3;
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(left, i2) == 1 && GstShown(e, "gw-flag"), "a third player online: a '2' zone still needs 2, so it stays full");

				// Reload: the once key holds (no second crowd), the hold zone's saved mark holds while full
				GapWorld.FakeOnline = 2;
				yield return GwReload(e);
				z1 = Gw4Zone(e, "gwplate"); z2 = Gw4Zone(e, "gwhold");
				if (z1 == null || z2 == null) { Fail("the zones are not back after the reload"); yield break; }
				yield return new WaitForSeconds(3.5f);
				Check(ref ok, e.State.ContainsKey(GapWorld.CoopBase + i1) && e.State.ContainsKey(GapWorld.CoopBase + i2), "both zones' keys survive the reload");
				Check(ref ok, times(crowd, i2) == 2 && GstShown(e, "gw-flag"), "still full after the reload: no new crowd, the flag still shows (" + times(crowd, i2) + ")");
				GapWorld.FakeOnline = -1;
				PutPlayerNear(z1.transform, 0.3f);
				StartTest(StayThere(z1.transform));
				yield return new WaitForSeconds(2.5f);
				Check(ref ok, times(crowd, i1) == 1, "on the 'once' plate again after the reload: no second crowd (" + times(crowd, i1) + ")");
				OnRaftCommand();
				fake = 0;
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(left, i2) == 2 && !GstShown(e, "gw-flag") && !e.State.ContainsKey(GapWorld.CoopBase + i2), "emptied: crowdleft, the flag hides, the mark goes");
				fake = 2;
				GapWorld.FakeOnline = 2;
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, times(crowd, i2) == 3 && GstShown(e, "gw-flag"), "filled once more: crowd (" + times(crowd, i2) + ")");
				// (saved while full, loaded with nobody there: it closes again by itself)
				yield return GwReload(e);
				fake = 0;
				yield return new WaitForSeconds(4f);
				Check(ref ok, times(left, i2) == 3 && !GstShown(e, "gw-flag"), "loaded while marked full, nobody there: crowdleft closes it (" + times(left, i2) + ")");
			}
			finally
			{
				Behaviours.Fired -= heard;
				GapWorld.FakeCount = null;
				GapWorld.FakeOnline = -1;
				OnRaftCommand();
				GwDrop(g, island);
			}
			if (ok) Log("PASS: GW8 co-op zone (scaled to players online, once + while they stand, check, reload)");
			else Fail("GW8 co-op zone: see the FAIL lines above");
		}

		#endregion

		#region GW1 + GW8 two players (host + box)

		const string Gw4MpIsland = "ci-gw-mp";

		[ConsoleCommand(name: "CIGw1104Mp", docs: "Dev, two players: GW1/GW8 on host + box. setup (host): spawns 'ci-gw-mp' (co-op zone 'gwmp' for 2 players, signal gw-mpcrowd; a stand, 2 planks for a rope, stock 1, signal gw-mptrade). wait: until the island is here. expect <signal> <0|1>: waits up to 8 s for the signal to be there (1) or checks it stays away 3 s (0), with the zone's count. trade: this player trades at the stand (gets the planks first). sold: the stand is sold out here. state: logs both. drop (host): removes the island")]
		public static void Gw1104MpCommand(string[] args) { StartTest(Gw1104MpRoutine(args ?? new string[0])); }

		static IEnumerator Gw1104MpRoutine(string[] args)
		{
			string what = args.Length > 0 ? args[0] : "";
			string side = Raft_Network.IsHost ? "host" : "client";
			Func<IslandWorldState.Entry> entry = () => IslandWorldState.Islands.FirstOrDefault(i => i.Root != null && i.HostName == Gw4MpIsland);
			if (what == "setup")
			{
				if (!Raft_Network.IsHost) { Fail("setup: as the host"); yield break; }
				Func<float, float, Vector3> at;
				IslandFile f = Gw4File(Gw4MpIsland, out at);
				f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = at(-6f, 0f), Props = P(BehaviourProps.Name, "gw-mpzone", ObjectProps.ZoneId, "gwmp", ObjectProps.ZoneRadius, "4",
					GapWorld.PlayersProp, "2", BehaviourProps.EventKey(GapWorld.CrowdEvent), "signal||gw-mpcrowd") });
				f.Objects.Add(new IslandObject { Name = TradeStand.StallObject, Position = at(6f, 0f), Props = P(BehaviourProps.Name, "gw-mpstand", TradeStand.GiveProp, "Plank*2", TradeStand.GetProp, "Rope*1",
					TradeStand.StockProp, "1", BehaviourProps.EventKey(TradeStand.TradeEvent), "signal||gw-mptrade") });
				var g = new GwIsland();
				yield return GwSpawnFile(g, f, Gw4MpIsland);
				if (g.E != null) Log("PASS: GW mp setup - " + Gw4MpIsland + " is island " + g.E.Id);
				yield break;
			}
			if (what == "drop")
			{
				var g = new GwIsland { E = entry() };
				GwDrop(g, Gw4MpIsland);
				Log("PASS: GW mp drop");
				yield break;
			}
			if (what == "wait")
			{
				for (float t = 0f; t < 90f && entry() == null; t += 1f) yield return new WaitForSeconds(1f);
				if (entry() == null) Fail("GW mp: '" + Gw4MpIsland + "' did not come (" + side + ")");
				else Log("PASS: GW mp wait - the island is here (" + side + ")");
				yield break;
			}
			IslandWorldState.Entry e = entry();
			if (e == null) { Fail("GW mp: no '" + Gw4MpIsland + "' here (" + side + ")"); yield break; }
			TradeStand stand = Gw4Stand(e, "gw-mpstand");
			TriggerZone z = Gw4Zone(e, "gwmp");
			Func<string> count = () =>
			{
				List<Network_Player> players = GapWorld.LivingPlayers();
				int set; bool hold; GapWorld.CoopOf(z != null ? z.Props : null, out set, out hold);
				return "zone: " + (z != null ? GapWorld.CountIn(z, players) : -1) + " in it, " + players.Count + " online, needs " + GapWorld.Need(set, players.Count);
			};
			if (what == "expect" && args.Length > 2)
			{
				string signal = args[1];
				bool want = args[2] == "1";
				float until = want ? 8f : 3f;
				for (float t = 0f; t < until && Behaviours.HasSignal(e, signal) != want; t += 0.5f) yield return new WaitForSeconds(0.5f);
				if (!want) for (float t = 0f; t < until && !Behaviours.HasSignal(e, signal); t += 0.5f) yield return new WaitForSeconds(0.5f);
				bool has = Behaviours.HasSignal(e, signal);
				if (has == want) Log("PASS: GW mp expect " + signal + " " + (want ? "there" : "not there") + " (" + side + "; " + count() + ")");
				else Fail("GW mp expect " + signal + " " + (want ? "there" : "not there") + ", but it is " + (has ? "there" : "not there") + " (" + side + "; " + count() + ")");
				yield break;
			}
			if (what == "trade")
			{
				Network_Player player = RAPI.GetLocalPlayer();
				if (stand == null || player == null) { Fail("GW mp trade: no stand or player (" + side + ")"); yield break; }
				Gw4Take(player, "Rope");
				player.Inventory.AddItem("Plank", 2);
				yield return new WaitForSeconds(0.3f);
				stand.Trade();
				for (float t = 0f; t < 8f && player.Inventory.GetItemCount("Rope") == 0; t += 0.5f) yield return new WaitForSeconds(0.5f);
				int rope = player.Inventory.GetItemCount("Rope");
				Gw4Take(player, "Rope"); Gw4Take(player, "Plank");
				if (rope == 1) Log("PASS: GW mp trade - got a rope for 2 planks (" + side + ", " + stand.Left + " left here)");
				else Fail("GW mp trade: " + rope + " rope after trading (" + side + ")");
				yield break;
			}
			if (what == "sold")
			{
				for (float t = 0f; t < 6f && stand != null && stand.Left != 0; t += 0.5f) yield return new WaitForSeconds(0.5f);
				if (stand != null && stand.Left == 0) Log("PASS: GW mp sold - the stand is sold out (" + side + ")");
				else Fail("GW mp sold: " + (stand != null ? stand.Left + " left" : "no stand") + " (" + side + ")");
				yield break;
			}
			if (what == "state") { Log("GW mp state (" + side + "): " + count() + ", crowd " + Behaviours.HasSignal(e, "gw-mpcrowd") + ", trade " + Behaviours.HasSignal(e, "gw-mptrade")); Log("PASS: GW mp state"); yield break; }
			Fail("CIGw1104Mp setup|wait|expect <signal> <0|1>|trade|sold|state|drop");
		}

		#endregion
	}
}
