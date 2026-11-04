using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// 11-04 gap features (gap-world): the island maker's own trade stands (GW1: TradeStand's "trade" event; the inspector's
	/// page is ObjectInspectorGapWorld) and co-op zones (GW8): a trigger zone with "zone.players=N[|hold]" counts the living
	/// players standing in it. The number needed scales with the players online: need = N, but never more than the players
	/// online (alive) - one player alone is always enough. The host counts (every 0.5 s) and fires the zone's "crowd" event
	/// (once ever, or with |hold every time it fills, and "crowdleft" when it no longer holds enough). The check
	/// "players|zone|N" asks the same on any object.
	/// </summary>
	public static class GapWorld
	{
		#region Trade stands (GW1)

		/// <summary>The behaviour events these features add to an object (Behaviours.EventsFor via PlayerFeatures.Events).</summary>
		internal static void Events(List<KeyValuePair<string, string>> list, string objectName, IDictionary<string, string> p)
		{
			if (objectName == ContentCatalog.TriggerZone)
			{
				int n; bool hold;
				if (CoopOf(p, out n, out hold))
				{
					list.Add(new KeyValuePair<string, string>(CrowdEvent, "enough players stand in it (" + DescribeCoop(p) + ")" + (hold ? ": every time it fills" : ": once ever")));
					if (hold) list.Add(new KeyValuePair<string, string>(CrowdLeftEvent, "it no longer holds enough players (someone stepped off)"));
				}
				return;
			}
			if (TradeStand.IsTrade(p) && !ContentCatalog.IsZone(objectName) && !ObjectProps.IsLoot(objectName, p) && !ObjectProps.IsNote(objectName, p))
				list.Add(new KeyValuePair<string, string>(TradeStand.TradeEvent, "a player traded at this stand (every trade: " + TradeText(p) + ")"));
		}

		/// <summary>"12 Plank for 1 Mango seed, 3 in stock".</summary>
		public static string TradeText(IDictionary<string, string> p)
		{
			Func<string, string> items = key => TraderRaft.Text(ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, ObjectProps.Get(p, key) } }));
			string give = items(TradeStand.GiveProp), get = items(TradeStand.GetProp);
			int stock = TradeStand.StockIn(p);
			return (give.Length > 0 ? give : "nothing") + " for " + (get.Length > 0 ? get : "nothing") + ", " + (stock <= 0 ? "no limit" : stock + " in stock");
		}

		#endregion

		#region Co-op zones (GW8)

		public const string PlayersProp = "zone.players", HoldMode = "hold", CrowdEvent = "crowd", CrowdLeftEvent = "crowdleft", PlayersCheck = "players";
		public const int MaxPlayers = 8;
		/// <summary>Island state: CoopBase + the zone's object index = the zone fired (once) or is full now (hold, host only).
		/// 0x10000 wide; in StableIds.IndexBases, so it follows the zone when the island is edited.</summary>
		public const int CoopBase = 0x6A00000;

		/// <summary>A zone's co-op setting: "N" or "N|hold" (N 1-8). False when it has none (or 0).</summary>
		public static bool CoopOf(IDictionary<string, string> p, out int n, out bool hold)
		{
			string[] v = ObjectProps.Get(p, PlayersProp).Split('|');
			hold = v.Length > 1 && v[1].Trim().Equals(HoldMode, StringComparison.OrdinalIgnoreCase);
			if (!int.TryParse(v[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n <= 0) { n = 0; return false; }
			n = Mathf.Clamp(n, 1, MaxPlayers);
			return true;
		}

		public static bool IsCoop(IDictionary<string, string> p) { int n; bool h; return CoopOf(p, out n, out h); }

		public static string Join(int n, bool hold) { return n <= 0 ? null : Mathf.Clamp(n, 1, MaxPlayers).ToString(CultureInfo.InvariantCulture) + (hold ? "|" + HoldMode : ""); }

		/// <summary>" · co-op 2 players (while they stand)" for the catalog and behaviour window, "" without.</summary>
		public static string DescribeZone(IDictionary<string, string> p) { return IsCoop(p) ? " · co-op " + DescribeCoop(p) : ""; }

		static string DescribeCoop(IDictionary<string, string> p)
		{
			int n; bool hold;
			CoopOf(p, out n, out hold);
			return n + " player" + (n > 1 ? "s" : "") + (n > 1 ? " (fewer if fewer are online)" : "") + (hold ? ", while they stand" : "");
		}

		/// <summary>The players in the world who are alive (a downed player neither stands on a plate nor counts as online).</summary>
		public static List<Network_Player> LivingPlayers()
		{
			return UnityEngine.Object.FindObjectsOfType<Network_Player>().Where(p => p != null && p.transform != null && !Dead(p)).ToList();
		}

		static bool Dead(Network_Player p)
		{
			return (p.PlayerScript != null && p.PlayerScript.IsDead) || (p.Stats != null && (p.Stats.IsDead || (p.Stats.stat_health != null && p.Stats.stat_health.Value <= 0f)));
		}

		/// <summary>The user's rule (11-04): need = the set number, but never more than the players online - with 1 online, 1
		/// in the zone is enough; with 2 online a "2" zone needs both; a "3" zone with 2 online needs 2.</summary>
		public static int Need(int set, int online) { return Mathf.Clamp(set, 1, Mathf.Max(1, online)); }

		/// <summary>How many of these players stand in the zone (its sphere).</summary>
		public static int CountIn(TriggerZone z, List<Network_Player> players)
		{
			if (z == null) return 0;
			float r2 = z.Radius * z.Radius;
			Vector3 c = z.transform.position;
			return players.Count(p => (p.transform.position - c).sqrMagnitude <= r2);
		}

		/// <summary>Tests: players counted in place of the real ones (null: the real ones).</summary>
		public static Func<TriggerZone, int> FakeCount;
		public static int FakeOnline = -1;

		static int Count(TriggerZone z, List<Network_Player> players) { return FakeCount != null ? FakeCount(z) : CountIn(z, players); }
		static int Online(List<Network_Player> players) { return FakeOnline >= 0 ? FakeOnline : players.Count; }

		static List<KeyValuePair<IslandWorldState.Entry, TriggerZone>> zones = new List<KeyValuePair<IslandWorldState.Entry, TriggerZone>>();
		static readonly Dictionary<TriggerZone, int> lastCount = new Dictionary<TriggerZone, int>();
		static float nextCache, nextTick;

		/// <summary>Every frame (DynamicIslands.Update): every 3 s the island's co-op zones, every 0.5 s their count.</summary>
		public static void Tick()
		{
			if (!LoadSceneManager.IsGameSceneLoaded) return;
			if (Time.time >= nextCache)
			{
				nextCache = Time.time + 3f;
				zones = IslandWorldState.Islands.Where(e => e.Root != null)
					.SelectMany(e => e.Root.GetComponentsInChildren<TriggerZone>(true).Where(z => IsCoop(z.Props)).Select(z => new KeyValuePair<IslandWorldState.Entry, TriggerZone>(e, z))).ToList();
				foreach (TriggerZone gone in lastCount.Keys.Where(z => z == null).ToList()) lastCount.Remove(gone);
			}
			if (zones.Count == 0 || Time.time < nextTick) return;
			nextTick = Time.time + 0.5f;
			List<Network_Player> players = LivingPlayers();
			Network_Player me = RAPI.GetLocalPlayer();
			int online = Online(players);
			foreach (KeyValuePair<IslandWorldState.Entry, TriggerZone> kv in zones)
			{
				TriggerZone z = kv.Value;
				IslandWorldState.Entry e = kv.Key;
				if (z == null || e.Root == null || !z.gameObject.activeInHierarchy) continue;
				IslandObjectRef zr = z.GetComponent<IslandObjectRef>();
				if (zr == null) continue;
				int set; bool hold;
				CoopOf(z.Props, out set, out hold);
				int need = Need(set, online), count = Count(z, players), was;
				if (!lastCount.TryGetValue(z, out was)) was = -1;
				lastCount[z] = count;
				int key = CoopBase + (zr.Index & 0xFFFF);
				bool on = e.State.ContainsKey(key);
				// (each machine: its own player near the zone hears how many stand in it - only where it takes more than one)
				if (count != was && was >= 0 && need > 1 && (hold || !on) && me != null && (me.transform.position - z.transform.position).sqrMagnitude < (z.Radius + 12f) * (z.Radius + 12f))
					IslandInfo.ShowMessage(count >= need ? "Co-op zone: all " + need + " players are here" : "Co-op zone: " + count + " of " + need + " players here");
				if (!Raft_Network.IsHost) continue;
				bool full = count >= need;
				if (!hold)
				{
					if (on || !full) continue;
					e.State[key] = new ObjectState { Active = true, Day = Behaviours.Today };
					IslandNetwork.SendUsed(e.Id, key, Behaviours.Today);
					Debug.Log("[CUSTOM ISLANDS] Co-op zone on '" + e.HostName + "': " + count + " of " + need + " players (" + online + " online) - its crowd event");
					Behaviours.FireFromHost(e, zr.Index, CrowdEvent);
				}
				else if (full && !on)
				{
					// (the host's own mark, saved: a world loaded while it was full closes again when nobody stands there)
					e.State[key] = new ObjectState { Active = true, Day = Behaviours.Today };
					Debug.Log("[CUSTOM ISLANDS] Co-op zone on '" + e.HostName + "': " + count + " of " + need + " players (" + online + " online) - crowd");
					Behaviours.FireFromHost(e, zr.Index, CrowdEvent);
				}
				else if (!full && on)
				{
					e.State.Remove(key);
					Debug.Log("[CUSTOM ISLANDS] Co-op zone on '" + e.HostName + "': " + count + " of " + need + " players - crowdleft");
					Behaviours.FireFromHost(e, zr.Index, CrowdLeftEvent);
				}
			}
		}

		/// <summary>Leaving a world.</summary>
		public static void Reset()
		{
			zones.Clear();
			lastCount.Clear();
			nextCache = nextTick = 0f;
			FakeCount = null;
			FakeOnline = -1;
		}

		/// <summary>The zone a check means: by zone name (zone.id) or object name (obj.name); empty = the object itself.</summary>
		public static TriggerZone FindZone(IslandWorldState.Entry e, int index, string target)
		{
			if (e == null || e.Root == null) return null;
			target = (target ?? "").Trim();
			foreach (TriggerZone z in e.Root.GetComponentsInChildren<TriggerZone>(true))
			{
				IslandObjectRef r = z.GetComponent<IslandObjectRef>();
				if (target.Length == 0) { if (r != null && r.Index == index) return z; continue; }
				if (z.Id.Trim().Equals(target, StringComparison.OrdinalIgnoreCase) || (r != null && r.Name.Trim().Equals(target, StringComparison.OrdinalIgnoreCase))) return z;
			}
			return null;
		}

		/// <summary>The check "players|zone|N": at least N living players (fewer when fewer are online) stand in the zone. N
		/// empty: the zone's own co-op number (1 without one).</summary>
		public static bool PlayersHold(IslandWorldState.Entry e, int index, ObjCheck c)
		{
			TriggerZone z = FindZone(e, index, c.Target);
			if (z == null) return false;
			int set, n; bool hold;
			if (int.TryParse((c.Arg ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0) set = Mathf.Min(n, MaxPlayers);
			else set = CoopOf(z.Props, out set, out hold) ? set : 1;
			List<Network_Player> players = LivingPlayers();
			return Count(z, players) >= Need(set, Online(players));
		}

		#endregion

		#region Check hooks (PlayerFeatures)

		internal static string What(ObjCheck c)
		{
			if (c.Kind != PlayersCheck) return null;
			int n;
			string many = int.TryParse((c.Arg ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0 ? n + " player" + (n > 1 ? "s" : "") : "the zone's number of players";
			return many + " (fewer if fewer are online) stand in " + (c.Target.Length > 0 ? "'" + c.Target + "'" : "this zone");
		}

		internal static bool Holds(IslandWorldState.Entry e, int index, ObjCheck c, out bool result)
		{
			result = false;
			if (c.Kind != PlayersCheck) return false;
			result = PlayersHold(e, index, c);
			return true;
		}

		internal static string CheckPlaceholder(string kind) { return kind == PlayersCheck ? "zone name (empty = this zone)" : null; }

		internal static string CheckHint(string kind)
		{
			return kind == PlayersCheck ? "A trigger zone's name (or its object name; empty = this zone): true while that many living players stand in it" : null;
		}

		internal static string CheckArgPlaceholder(string kind) { return kind == PlayersCheck ? "how many (empty = the zone's)" : null; }

		internal static string CheckArgHint(string kind)
		{
			return kind == PlayersCheck ? "How many players (1-8; empty = the zone's co-op number). It scales with the players online: never more than are online, so one player alone is always enough" : null;
		}

		/// <summary>Editor: "players|old zone name|N" checks name the new one (EditorRenames.ZoneId). The number changed.</summary>
		public static int ZoneRefs(IDictionary<string, string> props, string from, string to)
		{
			from = (from ?? "").Trim(); to = (to ?? "").Trim();
			if (props == null || from.Length == 0 || to.Length == 0 || from.Equals(to, StringComparison.OrdinalIgnoreCase) || to.IndexOf('|') >= 0) return 0;
			int count = 0;
			foreach (string k in props.Keys.ToList())
			{
				if (!k.StartsWith(BehaviourProps.CheckPrefix)) continue;
				string[] lines = (props[k] ?? "").Split('\n');
				bool any = false;
				for (int i = 0; i < lines.Length; i++)
				{
					string[] p = lines[i].Split('|');
					if (p.Length < 2 || p[0].Trim().TrimStart('!') != PlayersCheck || !p[1].Trim().Equals(from, StringComparison.OrdinalIgnoreCase)) continue;
					p[1] = to;
					lines[i] = string.Join("|", p);
					any = true; count++;
				}
				if (any) props[k] = string.Join("\n", lines);
			}
			return count;
		}

		#endregion
	}
}
