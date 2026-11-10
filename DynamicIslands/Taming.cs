using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>QE11: the tame step "Goat@pen" (an animal kind or a creature spot's name, @ a trigger zone). While it is the
	/// current step, a player who holds the spot's food (tame.food, else a default per kind) close to one of its animals for
	/// a second feeds it: one food goes, and the host makes that animal follow its player (Raft's own walking: its
	/// AI_State_CirculateSpawn spot moves along, as a lure does). Inside the pen zone it is penned for good: a used key per
	/// pen slot (KeyBase + slot*0x10000 + spot object; saved, sent to everyone and joiners), and the step counts them.
	/// After a load the penned animals are put back in the pen. Raft has no hand-feeding of its own (its animals eat
	/// grass plots), so the food and the hold are ours; the net launcher still catches them as before.</summary>
	public static class Taming
	{
		public const string QuestType = "tame", FoodProp = "tame.food";
		/// <summary>Penned animals: KeyBase + slot * 0x10000 + creature spot object (slot 0..MaxPen-1; in StableIds.IndexBases).</summary>
		public const int KeyBase = 0x7D00000, MaxPen = 8;
		public const float FeedDistance = 3f, FeedSeconds = 1f, LoseDistance = 30f, FollowGap = 1.6f;
		internal static int Fed, Penned, Refused, Released;

		public static IEnumerable<int> KeyBases { get { return Enumerable.Range(0, MaxPen).Select(i => KeyBase + i * 0x10000); } }

		public static bool Tamable(AI_NetworkBehaviourType t) { return t == AI_NetworkBehaviourType.Chicken || t == AI_NetworkBehaviourType.Goat || t == AI_NetworkBehaviourType.Llama; }

		public static string DefaultFood(AI_NetworkBehaviourType t)
		{
			switch (t)
			{
				case AI_NetworkBehaviourType.Chicken: return "Raw_Beet";
				case AI_NetworkBehaviourType.Goat: return "Raw_Potato";
				default: return "Raw_Potato";
			}
		}

		public static string FoodOf(IDictionary<string, string> p, AI_NetworkBehaviourType t)
		{
			string f = ObjectProps.Get(p, FoodProp).Trim();
			return f.Length > 0 ? f : DefaultFood(t);
		}

		/// <summary>An animal kind's names a step may use: its label, Raft's type, Clucker for the chicken.</summary>
		public static bool KindIs(ContentCatalog.CreatureKind k, string who)
		{
			if (k == null || string.IsNullOrEmpty(who)) return false;
			return k.Label.Equals(who, StringComparison.OrdinalIgnoreCase) || k.Type.ToString().Equals(who, StringComparison.OrdinalIgnoreCase)
				|| (k.Type == AI_NetworkBehaviourType.Chicken && who.Equals("Clucker", StringComparison.OrdinalIgnoreCase));
		}

		public static string Describe(string target, int count)
		{
			string who, pen;
			IslanderWalks.SplitTarget(target, out who, out pen);
			string w = who.Length > 0 ? who.ToLowerInvariant() : "animal";
			return "Tame " + (count > 1 ? count + " " + w + (w.EndsWith("s") ? "" : "s") : "a " + w) + " and lead " + (count > 1 ? "them" : "it") + (pen.Length > 0 ? " to " + pen : " to the pen");
		}

		/// <summary>The plan check: who must be a tamable animal (kind or spot name) of the island, pen a zone; count &lt;= MaxPen.</summary>
		public static string StepProblem(Dictionary<string, int> tamable, ICollection<string> zones, string target, int count)
		{
			string who, pen;
			IslanderWalks.SplitTarget(target, out who, out pen);
			if (who.Length == 0 || pen.Length == 0) return "a tame step is Animal@zone (Goat@pen): the animal (Goat, Llama, Chicken or a creature spot's name) and its pen";
			int n;
			if (!tamable.TryGetValue(who, out n)) return "no goat, llama or chicken spot here is '" + who + "'" + (tamable.Count > 0 ? " (" + string.Join(", ", tamable.Keys.ToArray()) + ")" : " (place a Goat, Llama or Chicken creature spot)");
			if (!zones.Contains(pen)) return "no trigger zone is called '" + pen + "'" + (zones.Count > 0 ? " (" + string.Join(", ", zones.ToArray()) + ")" : "");
			if (count > MaxPen) return "a tame step takes at most " + MaxPen + " animals";
			if (count > n) return "it wants " + count + " of '" + who + "', the island has " + n;
			return null;
		}

		/// <summary>An island's creature spots this step's animal names.</summary>
		internal static List<KeyValuePair<IslandObjectRef, CreatureSpawnPoint>> Spots(IslandWorldState.Entry e, string who)
		{
			var list = new List<KeyValuePair<IslandObjectRef, CreatureSpawnPoint>>();
			if (e == null || e.Root == null) return list;
			foreach (IslandObjectRef r in Behaviours.RefsOf(e))
			{
				CreatureSpawnPoint s = r.GetComponent<CreatureSpawnPoint>();
				if (s == null || s.Kind == null || !Tamable(s.Kind.Type)) continue;
				if (KindIs(s.Kind, who) || r.Name.Equals(who, StringComparison.OrdinalIgnoreCase)) list.Add(new KeyValuePair<IslandObjectRef, CreatureSpawnPoint>(r, s));
			}
			return list;
		}

		static int PennedAt(IslandWorldState.Entry e, int spot) { int n = 0; for (int i = 0; i < MaxPen; i++) if (ContentState.IsUsed(e, KeyBase + i * 0x10000 + spot)) n++; return n; }

		/// <summary>The step's count: animals of it in their pen (kept for good).</summary>
		public static int Found(IslandWorldState.Entry e, string target)
		{
			string who, pen;
			IslanderWalks.SplitTarget(target, out who, out pen);
			return Spots(e, who).Sum(s => PennedAt(e, s.Key.Index));
		}

		#region Current steps

		static readonly Dictionary<int, KeyValuePair<float, List<string>>> steps = new Dictionary<int, KeyValuePair<float, List<string>>>();

		/// <summary>The current tame steps' targets of an island (every quest of it).</summary>
		internal static List<string> Current(IslandWorldState.Entry e)
		{
			KeyValuePair<float, List<string>> c;
			if (e == null) return new List<string>();
			if (!steps.TryGetValue(e.Id, out c) || Time.time - c.Key > 0.5f)
			{
				var list = new List<string>();
				for (int n = 0, count = QuestTracker.QuestsOf(e); n < count; n++)
				{
					IslandQuest q = QuestTracker.QuestOf(e, n);
					int step = QuestTracker.StepOf(e, n);
					if (q.Exists && step < q.Steps.Count && q.Steps[step].Type == QuestType) list.Add(q.Steps[step].Target);
				}
				steps[e.Id] = c = new KeyValuePair<float, List<string>>(Time.time, list);
			}
			return c.Value;
		}

		static bool OnIsland(IslandWorldState.Entry e, Vector3 p)
		{
			float r = Mathf.Max(20f, CustomIslandSpawner.LandRadius(e.Name)) + 5f;
			return new Vector2(p.x - e.Position.x, p.z - e.Position.z).magnitude < r;
		}

		#endregion

		#region Feeding (each machine, its own player)

		static float nextLocal, nextHost, held, hintAt = -99f, feedPause;
		static AI_NetworkBehaviour[] nearby = new AI_NetworkBehaviour[0];
		static float nearbyAt = -9f;

		/// <summary>Tests: why the last local tick did not feed.</summary>
		internal static string Why = "";

		static void LocalTick()
		{
			Why = "no island";
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null || p.Inventory == null) { held = 0f; return; }
			Vector3 me = p.transform.position;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null || !OnIsland(e, me)) continue;
				foreach (string t in Current(e))
				{
					string who, pen;
					IslanderWalks.SplitTarget(t, out who, out pen);
					foreach (var s in Spots(e, who))
					{
						AI_NetworkBehaviour ai = NearestAnimal(s.Value, me);
						if (ai == null) { Why = "no " + who + " within " + FeedDistance + " m (" + s.Value.Spawned.Count + " spawned)"; continue; }
						string food = FoodOf(s.Key.Props, s.Value.Kind.Type);
						ItemInstance inHand = p.Inventory.GetSelectedHotbarItem();
						if (inHand == null || inHand.UniqueName != food)
						{
							held = 0f;
							Why = "in hand: " + (inHand == null ? "nothing" : inHand.UniqueName) + ", wants " + food;
							if (Time.time - hintAt > 20f) { hintAt = Time.time; MechFeatures.Tell("Hold " + ContentCatalog.ItemLabel(food) + " close to the " + s.Value.Kind.Label.ToLowerInvariant() + " to feed it"); }
							return;
						}
						if (Time.time < feedPause) { Why = "pause"; return; }
						held += 0.25f;
						if (held < FeedSeconds) { Why = "holding " + held; return; }
						Why = "fed";
						held = 0f;
						feedPause = Time.time + 2f;
						p.Inventory.RemoveItem(food, 1);
						Fed++;
						Vector3 at = ai.transform.position;
						if (Raft_Network.IsHost) HostFeed(e, s.Key.Index, MechFeatures.LocalId, at);
						else IslandNetwork.SendToTheHost(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "feed", Ids = new[] { e.Id, s.Key.Index }, Offsets = new[] { at.x, at.y, at.z } });
						return;
					}
				}
			}
			held = 0f;
		}

		/// <summary>The spot's animal nearest this player within FeedDistance (host: its own list, not tamed yet; clients: by kind near the spot).</summary>
		static AI_NetworkBehaviour NearestAnimal(CreatureSpawnPoint s, Vector3 me)
		{
			IEnumerable<AI_NetworkBehaviour> pool;
			if (Raft_Network.IsHost) pool = s.Spawned.Where(a => a != null && !tamed.ContainsKey(a));
			else
			{
				if (Time.time - nearbyAt > 1f) { nearbyAt = Time.time; nearby = UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>(); }
				pool = nearby.Where(a => a != null && a.behaviourType == s.Kind.Type);
			}
			AI_NetworkBehaviour best = null;
			float bd = FeedDistance;
			foreach (AI_NetworkBehaviour a in pool)
			{
				if (a.networkEntity != null && a.networkEntity.IsDead) continue;
				float d = Vector3.Distance(a.transform.position, me);
				if (d < bd) { bd = d; best = a; }
			}
			return best;
		}

		#endregion

		#region Host: tamed animals

		class Tamed
		{
			public IslandWorldState.Entry E; public int Spot; public ulong Owner; public string Pen; public bool Penned; public string Target;
			public AI_State_CirculateSpawn St; public Vector3 Home; public float Radius, RadiusMin, PlayerDistance;
		}

		static readonly Dictionary<AI_NetworkBehaviour, Tamed> tamed = new Dictionary<AI_NetworkBehaviour, Tamed>();

		/// <summary>Host: a player fed an animal of this spot near this place - the nearest one not tamed yet follows them.</summary>
		internal static void HostFeed(IslandWorldState.Entry e, int spot, ulong player, Vector3 near)
		{
			if (e == null || e.Root == null) return;
			IslandObjectRef r = Behaviours.RefsOf(e).FirstOrDefault(x => x.Index == spot);
			CreatureSpawnPoint s = r != null ? r.GetComponent<CreatureSpawnPoint>() : null;
			string target = s == null ? null : Current(e).FirstOrDefault(t => { string w, z; IslanderWalks.SplitTarget(t, out w, out z); return KindIs(s.Kind, w) || r.Name.Equals(w, StringComparison.OrdinalIgnoreCase); });
			AI_NetworkBehaviour ai = target == null ? null : s.Spawned.Where(a => a != null && !tamed.ContainsKey(a) && (a.networkEntity == null || !a.networkEntity.IsDead))
				.OrderBy(a => Vector3.Distance(a.transform.position, near)).FirstOrDefault(a => Vector3.Distance(a.transform.position, near) < FeedDistance + 2f);
			Network_Player pl = Players.All.FirstOrDefault(x => x != null && x.steamID.Id == player);
			if (ai == null || pl == null || Vector3.Distance(pl.transform.position, near) > FeedDistance + 3f)
			{
				Refused++;
				Result(player, 0, s != null ? s.Kind.Label : "animal");
				return;
			}
			string who, pen;
			IslanderWalks.SplitTarget(target, out who, out pen);
			AI_State_CirculateSpawn st = ai.GetComponentsInChildren<AI_State_CirculateSpawn>(true).FirstOrDefault();
			if (st == null) { Refused++; Result(player, 0, s.Kind.Label); Debug.Log("[CUSTOM ISLANDS] Taming: a " + s.Kind.Label + " without a circulate state"); return; }
			Traverse tv = Traverse.Create(st);
			tamed[ai] = new Tamed { E = e, Spot = spot, Owner = player, Pen = pen, Target = target, St = st, Home = st.SpawnPointPosition,
				Radius = tv.Field("circulationRadius").GetValue<float>(), RadiusMin = tv.Field("circulationRadiusMin").GetValue<float>(), PlayerDistance = tv.Field("playerDistance").GetValue<float>() };
			Debug.Log("[CUSTOM ISLANDS] Taming: a " + s.Kind.Label + " of '" + e.HostName + "' follows player " + player);
			Result(player, 1, s.Kind.Label);
		}

		/// <summary>Host -> the player: 0 = nothing to feed there, 1 = it follows you, 2 = it is in the pen.</summary>
		static void Result(ulong player, int kind, string label)
		{
			if (player == MechFeatures.LocalId) Told(kind, label);
			else IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "tamed", Count = kind, Data = player.ToString(CultureInfo.InvariantCulture) + "|" + label });
		}

		static void Told(int kind, string label)
		{
			string a = label.ToLowerInvariant();
			MechFeatures.Tell(kind == 1 ? "The " + a + " eats from your hand. It follows you now - lead it to its pen" : kind == 2 ? "The " + a + " is in its pen" : "The " + a + " isn't hungry");
		}

		/// <summary>Clients: the host's answer (only the player it names shows it).</summary>
		internal static void OnTamed(IslandNetMessage msg)
		{
			string[] f = (msg.Data ?? "").Split('|');
			ulong who;
			if (f.Length < 2 || !ulong.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out who) || who != MechFeatures.LocalId) return;
			Told(msg.Count, f[1]);
		}

		static void Steer(Tamed t, Vector3 to, float radius)
		{
			try
			{
				t.St.SetNewSpawnPoint(to);
				Traverse tv = Traverse.Create(t.St);
				tv.Field("circulationRadius").SetValue(radius);
				tv.Field("circulationRadiusMin").SetValue(0f);
				tv.Field("playerDistance").SetValue(0f);
			}
			catch { }
		}

		static void Release(AI_NetworkBehaviour ai, Tamed t)
		{
			tamed.Remove(ai);
			Released++;
			if (ai == null || t.St == null) return;
			try
			{
				Traverse tv = Traverse.Create(t.St);
				tv.Field("circulationRadius").SetValue(t.Radius);
				tv.Field("circulationRadiusMin").SetValue(t.RadiusMin);
				tv.Field("playerDistance").SetValue(t.PlayerDistance);
				t.St.SetNewSpawnPoint(t.Home);
			}
			catch { }
		}

		static void HostTick()
		{
			foreach (var kv in tamed.ToList())
			{
				AI_NetworkBehaviour ai = kv.Key;
				Tamed t = kv.Value;
				// gone, dead, caught with the net (Raft let go of it), its island unloaded: forget it
				if (ai == null || t.E.Root == null || ai.connectedSpawner == null || (ai.networkEntity != null && ai.networkEntity.IsDead)) { tamed.Remove(ai); continue; }
				TriggerZone pen = IslanderWalks.ZoneOf(t.E, t.Pen);
				if (t.Penned) { if (pen != null) Steer(t, pen.transform.position, Mathf.Max(0.5f, pen.Radius - 1f)); continue; }
				// its step is over (done some other way): it goes home
				if (!Current(t.E).Contains(t.Target)) { Release(ai, t); continue; }
				if (pen != null && IslanderWalks.Inside(pen, ai.transform.position))
				{
					int slot = Enumerable.Range(0, MaxPen).FirstOrDefault(i => !ContentState.IsUsed(t.E, KeyBase + i * 0x10000 + t.Spot));
					int key = KeyBase + slot * 0x10000 + t.Spot;
					if (PennedAt(t.E, t.Spot) < MaxPen)
					{
						t.E.State[key] = new ObjectState { Active = false, Yield = 0, Day = Behaviours.Today };
						IslandNetwork.SendUsed(t.E.Id, key, Behaviours.Today);
					}
					t.Penned = true;
					Penned++;
					Debug.Log("[CUSTOM ISLANDS] Taming: a tamed animal of '" + t.E.HostName + "' is in the pen '" + t.Pen + "' (" + PennedAt(t.E, t.Spot) + ")");
					IslandObjectRef r = Behaviours.RefsOf(t.E).FirstOrDefault(x => x.Index == t.Spot);
					CreatureSpawnPoint s = r != null ? r.GetComponent<CreatureSpawnPoint>() : null;
					Result(t.Owner, 2, s != null ? s.Kind.Label : "animal");
					Steer(t, pen.transform.position, Mathf.Max(0.5f, pen.Radius - 1f));
					continue;
				}
				Network_Player pl = Players.All.FirstOrDefault(x => x != null && x.steamID.Id == t.Owner);
				Vector3 a = ai.transform.position;
				if (pl == null || Vector3.Distance(pl.transform.position, a) > LoseDistance || !OnIsland(t.E, pl.transform.position))
				{
					Steer(t, a, 1f); // (its player left: it stays where it is, still tame)
					continue;
				}
				// (on its own side of the player, a gap away: it trails them whichever way they look)
				Vector3 side = Flat(a - pl.transform.position);
				if (side.sqrMagnitude < 0.01f) side = -Flat(pl.transform.forward);
				Vector3 to = pl.transform.position + side.normalized * FollowGap;
				float gy;
				if (IslanderWalks.Ground(ai.transform, to, pl.transform.position.y, out gy)) to.y = gy; else to = pl.transform.position;
				Steer(t, to, 0.8f);
			}
			RePen();
		}

		static float nextRePen;

		/// <summary>Host: penned animals (saved) back in their pen after a load - the first ones of their spot not tamed now.</summary>
		static void RePen()
		{
			if (Time.time < nextRePen) return;
			nextRePen = Time.time + 2f;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null) continue;
				IslandQuest q;
				for (int n = 0, count = QuestTracker.QuestsOf(e); n < count; n++)
				{
					q = QuestTracker.QuestOf(e, n);
					if (!q.Exists) continue;
					foreach (IslandQuest.Step step in q.Steps.Where(x => x.Type == QuestType))
					{
						string who, penName;
						IslanderWalks.SplitTarget(step.Target, out who, out penName);
						TriggerZone pen = IslanderWalks.ZoneOf(e, penName);
						if (pen == null) continue;
						foreach (var s in Spots(e, who))
						{
							int want = PennedAt(e, s.Key.Index), have = tamed.Values.Count(t => t.Penned && t.E == e && t.Spot == s.Key.Index);
							foreach (AI_NetworkBehaviour ai in s.Value.Spawned.Where(a => a != null && !tamed.ContainsKey(a) && (a.networkEntity == null || !a.networkEntity.IsDead)).Take(Math.Max(0, want - have)).ToList())
							{
								AI_State_CirculateSpawn st = ai.GetComponentsInChildren<AI_State_CirculateSpawn>(true).FirstOrDefault();
								if (st == null) continue;
								Traverse tv = Traverse.Create(st);
								var t = new Tamed { E = e, Spot = s.Key.Index, Pen = penName, Target = step.Target, Penned = true, St = st, Home = st.SpawnPointPosition,
									Radius = tv.Field("circulationRadius").GetValue<float>(), RadiusMin = tv.Field("circulationRadiusMin").GetValue<float>(), PlayerDistance = tv.Field("playerDistance").GetValue<float>() };
								tamed[ai] = t;
								Vector3 at = pen.transform.position + new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), 0f, UnityEngine.Random.Range(-0.6f, 0.6f));
								float gy;
								if (IslanderWalks.Ground(ai.transform, at, pen.transform.position.y + 1f, out gy)) at.y = gy + 0.05f;
								ai.transform.position = at;
								Steer(t, pen.transform.position, Mathf.Max(0.5f, pen.Radius - 1f));
								Debug.Log("[CUSTOM ISLANDS] Taming: a penned " + s.Value.Kind.Label + " of '" + e.HostName + "' back in '" + penName + "'");
							}
						}
					}
				}
			}
		}

		static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

		/// <summary>Tests: animals following a player now, and penned ones.</summary>
		internal static int Following { get { return tamed.Values.Count(t => !t.Penned); } }
		internal static int PennedNow { get { return tamed.Values.Count(t => t.Penned); } }
		internal static bool IsTamed(AI_NetworkBehaviour ai) { return ai != null && tamed.ContainsKey(ai); }

		#endregion

		/// <summary>From MechFeatures.Tick, every 0.25 s.</summary>
		internal static void Tick()
		{
			if (Time.time < nextLocal) return;
			nextLocal = Time.time + 0.25f;
			try { LocalTick(); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Taming: " + ex.Message); }
			if (Raft_Network.IsHost && Time.time >= nextHost) { nextHost = Time.time + 0.5f; try { HostTick(); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Taming (host): " + ex.Message); } }
		}

		/// <summary>Host: a client's feed.</summary>
		internal static void OnFeed(IslandNetMessage msg, ulong from)
		{
			if (msg.Ids == null || msg.Ids.Length < 2 || msg.Offsets == null || msg.Offsets.Length < 3) return;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == msg.Ids[0]);
			if (e == null || !PlayerTrust.MayMark(from, e.Id, "a fed animal")) { Refused++; return; }
			HostFeed(e, msg.Ids[1], from, new Vector3(msg.Offsets[0], msg.Offsets[1], msg.Offsets[2]));
		}

		public static void Reset()
		{
			tamed.Clear();
			steps.Clear();
			held = 0f;
			nearby = new AI_NetworkBehaviour[0];
		}
	}
}
