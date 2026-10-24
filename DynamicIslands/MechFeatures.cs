using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Quest mechanics (2026-10-24, RAFT_QUEST_MECHANICS M36/M37/M38 and the cheap Partial rows):
	///   zone.push=dx,dy,dz,force[,kick]   a trigger zone pushes the player standing in it (a fan, wind, an updraft) every frame;
	///                                     kick = a one-off shove on entering (a knockback). Each machine pushes its own player.
	///   zone.pulse=on,off[,offset]        the zone is on for "on" seconds, off for "off", on the clock the players share: its push,
	///                                     hazard and air only while on (a firebomb, a steam vent).
	///   zone.stack=name|count             the zone counts the pieces with that name standing in it; with enough, the host fires its
	///                                     "stack" event once (on.stack=...). Players near it read "Stacked 2 of 4".
	///   beh.lure=spot[|radius]            while this object is shown and open, the animals of the creature spot with that name walk
	///                                     to it and stay around it (radius, 3 m) - calm, ignoring players (host).
	///   beh.lureItem=item[|count]         the lure wants an item put in: used with it, it takes it and opens (its use lines are made
	///                                     as if written: if.use=take|item|count, on.use=open, else.use=message). M5 food lure too.
	///   on.use=carry|[name]               the player picks the piece up and carries it (in front of them): no jumping or ziplines
	///                                     while carrying; the use key puts it down in front (on the ground or on another piece),
	///                                     a hit drops it. The host says who holds what; where it was left is saved (0x5000000).
	///   beh.carrySnap=0.5                 a carried piece is put down on this grid (island space) and turned in 90° steps.
	///   collapse|name|seconds             (shared action) the objects rumble, shake and fall away, then are gone (hidden, saved).
	///   moveto|name|marker[,seconds]      (shared action) the objects travel to a marker object's place (a cart to a ledge, an
	///                                     antagonist to the next arena - a creature spot's animals follow), carrying players on
	///                                     them; the new place is saved.
	///   boss.shieldIf=check lines         a creature spot's animals can't be hurt by players while all the checks hold (a boss
	///                                     guarded while its feeder runs: state|Feeder|open).
	/// Multiplayer: IslandNetMessage.Mech (kind 39); the host owns who carries what, collapses and moves.
	/// </summary>
	public static class MechFeatures
	{
		public const string PushProp = "zone.push", PulseProp = "zone.pulse", StackProp = "zone.stack";
		public const string LureProp = "beh.lure", LureItem = "beh.lureItem", CarrySnap = "beh.carrySnap", ShieldIf = "boss.shieldIf";
		public const string StackEvent = "stack";
		/// <summary>Where a carried or moved object was left: 0x5000000 + object * 4 + (x, y, z in cm; yaw in tenths of a degree).
		/// A stack zone that fired: StackDoneBase + zone.</summary>
		public const int PlaceBase = 0x5000000, StackDoneBase = 0x5F00000;
		public const float MaxForce = 40f, DefaultLureRadius = 3f;

		/// <summary>Tests: pushes done, lures on, the last drop's place.</summary>
		internal static int Pushes, Lured;
		internal static Vector3 LastDrop;

		#region Push and pulse

		public class PushSpec
		{
			public Vector3 Dir;
			public float Force, Kick;
			public bool Inside;

			public static PushSpec Parse(IDictionary<string, string> p)
			{
				string v = ObjectProps.Get(p, PushProp).Trim();
				if (v.Length == 0) return null;
				float[] f = Floats(v);
				if (f.Length < 3) return null;
				var s = new PushSpec { Dir = new Vector3(f[0], f[1], f[2]), Force = f.Length > 3 ? Mathf.Clamp(f[3], 0f, MaxForce) : 8f, Kick = f.Length > 4 ? Mathf.Clamp(f[4], 0f, MaxForce) : 0f };
				if (s.Dir.sqrMagnitude < 1e-4f || (s.Force <= 0f && s.Kick <= 0f)) return null;
				s.Dir.Normalize();
				return s;
			}

			public static string Join(Vector3 dir, float force, float kick)
			{
				string t = F(dir.x) + "," + F(dir.y) + "," + F(dir.z) + "," + F(force);
				return kick > 0f ? t + "," + F(kick) : t;
			}

			public string Describe()
			{
				string d = DirName(Dir);
				return (Force > 0f ? "pushes " + d + " (" + F(Force) + ")" : "") + (Kick > 0f ? (Force > 0f ? ", " : "") + "knocks " + d + " (" + F(Kick) + ")" : "");
			}
		}

		static string F(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

		static float[] Floats(string v)
		{
			var list = new List<float>();
			foreach (string part in v.Split(','))
			{
				float f;
				if (!float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) break;
				list.Add(f);
			}
			return list.ToArray();
		}

		public static string DirName(Vector3 d)
		{
			float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
			if (ay >= ax && ay >= az) return d.y > 0 ? "up" : "down";
			if (ax >= az) return d.x > 0 ? "east (+x)" : "west (-x)";
			return d.z > 0 ? "north (+z)" : "south (-z)";
		}

		/// <summary>Whether a zone is on now (no zone.pulse: always).</summary>
		public static bool PulseOn(IDictionary<string, string> p) { return PulseOn(p, SharedClock.Now); }

		public static bool PulseOn(IDictionary<string, string> p, float now)
		{
			float[] f = Floats(ObjectProps.Get(p, PulseProp));
			if (f.Length < 2 || f[0] <= 0f || f[1] <= 0f) return true;
			float offset = f.Length > 2 ? f[2] : 0f;
			return Mathf.Repeat(now + offset, f[0] + f[1]) < f[0];
		}

		/// <summary>Every frame for a push zone: this machine's player inside it (and the zone on) is pushed.</summary>
		public static void PushTick(TriggerZone z)
		{
			PushSpec s = z.Push;
			Network_Player pl = RAPI.GetLocalPlayer();
			if (s == null || pl == null) return;
			bool inside = (pl.transform.position - z.transform.position).sqrMagnitude <= z.Radius * z.Radius && PulseOn(z.Props);
			if (!inside) { s.Inside = false; return; }
			PersonController pc = pl.PersonController;
			if (pc == null || !pc.enabled) return;
			if (!s.Inside && s.Kick > 0f) pc.AddExternalVelocity(s.Dir * s.Kick, 0.2f);
			s.Inside = true;
			if (s.Force <= 0f) return;
			Pushes++;
			Vector3 v = s.Dir * s.Force;
			CharacterController cc = pc.controller;
			// (sideways and down: moved by the character controller itself - Raft damps an outside velocity on the ground;
			// up: an outside velocity, so the updraft beats gravity and lets go smoothly)
			Vector3 move = new Vector3(v.x, Mathf.Min(0f, v.y), v.z);
			if (move.sqrMagnitude > 0f && cc != null && cc.enabled) cc.Move(move * Time.deltaTime);
			if (v.y > 0f)
			{
				Vector3 ev = pc.externalVelocity;
				if (ev.y < v.y) { ev.y = v.y; pc.SetExternalVelocity(ev, 0.3f); }
			}
		}

		#endregion

		#region Descriptions (catalog, behaviour window)

		public static string DescribeZone(IDictionary<string, string> p)
		{
			string t = "";
			PushSpec s = PushSpec.Parse(p);
			if (s != null) t += " · " + s.Describe();
			float[] f = Floats(ObjectProps.Get(p, PulseProp));
			if (f.Length >= 2 && f[0] > 0f && f[1] > 0f) t += " · on " + F(f[0]) + " s / off " + F(f[1]) + " s";
			if (IsStack(p)) t += " · stack " + DescribeStack(p);
			return t;
		}

		public static bool IsStack(IDictionary<string, string> p) { string n; int c; return StackOf(p, out n, out c); }

		public static bool StackOf(IDictionary<string, string> p, out string name, out int count)
		{
			string[] v = ObjectProps.Get(p, StackProp).Split('|');
			name = v[0].Trim();
			count = 1;
			if (v.Length > 1 && !int.TryParse(v[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) count = 1;
			count = Mathf.Clamp(count, 1, 50);
			return name.Length > 0;
		}

		public static string DescribeStack(IDictionary<string, string> p)
		{
			string n; int c;
			return StackOf(p, out n, out c) ? c + " × '" + n + "'" : "";
		}

		public static string DescribeMoveTo(string t, string arg)
		{
			string marker; float s;
			MoveArg(arg, out marker, out s);
			return "move " + t + " to the marker '" + marker + "'" + (s > 0f ? " (" + F(s) + " s)" : "") + ", carrying players on it";
		}

		static void MoveArg(string arg, out string marker, out float seconds)
		{
			string[] v = (arg ?? "").Split(',');
			marker = v[0].Trim();
			seconds = 4f;
			if (v.Length > 1 && !float.TryParse(v[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) seconds = 4f;
			seconds = Mathf.Clamp(seconds, 0f, 120f);
		}

		static float Seconds(string arg, float def, float min, float max)
		{
			float s;
			return float.TryParse((arg ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out s) ? Mathf.Clamp(s, min, max) : def;
		}

		#endregion

		#region Expand (lure items, carry hints)

		/// <summary>An object's settings with the lines a lure item or a carry needs, made as if the builder had written them
		/// (every machine makes the same; the builder's own lines win).</summary>
		public static IDictionary<string, string> Expand(string objectName, IDictionary<string, string> props)
		{
			if (props == null) return props;
			Dictionary<string, string> np = null;
			Func<Dictionary<string, string>> copy = () => np ?? (np = new Dictionary<string, string>(props));
			string lureItem = ObjectProps.Get(props, LureItem).Trim();
			if (lureItem.Length > 0 && ObjectProps.Get(props, "on.use").Length == 0 && ObjectProps.Get(props, "if.use").Length == 0)
			{
				string[] v = lureItem.Split('|');
				string item = v[0].Trim();
				int count = 1;
				if (v.Length > 1 && !int.TryParse(v[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) count = 1;
				count = Mathf.Clamp(count, 1, 99);
				string label = item.StartsWith("story:") ? item.Substring(6) : ContentCatalog.ItemLabel(item);
				string name = ObjectProps.Get(props, BehaviourProps.Name).Trim();
				var d = copy();
				// (its own name: only while it is closed - a second use doesn't take the bait again)
				d["if.use"] = (name.Length > 0 ? "state|" + name + "|closed\n" : "") + "take|" + item + "|" + count;
				d["on.use"] = "open|\nmessage||The bait is out. Wait for the animals to come.";
				d["else.use"] = "message||" + (count > 1 ? count + " × " : "") + label + " goes in here";
				if (ObjectProps.Get(props, BehaviourProps.Use).Length == 0) d[BehaviourProps.Use] = "Put in " + label;
			}
			if (ObjectProps.Get(props, BehaviourProps.Use).Length == 0 && ObjAction.ParseLines(ObjectProps.Get(props, "on.use")).Any(a => a.Verb == "carry"))
				copy()[BehaviourProps.Use] = "Pick up";
			return (IDictionary<string, string>)np ?? props;
		}

		#endregion

		#region Carry

		class Carried
		{
			public int Island, Index;
			public ulong Who;
			public IslandObjectRef Ref;
			public List<Collider> Off = new List<Collider>();
			public Network_Player Holder;
			public float LookedAt, Missing = -1f;
		}

		static readonly Dictionary<string, Carried> carried = new Dictionary<string, Carried>();
		static Carried mine;
		static float pickedAt, lastHealth, toldAt = -10f;

		static string Key(int island, int index) { return island + "/" + index; }

		internal static ulong LocalId { get { try { Network_Player p = RAPI.GetLocalPlayer(); return p != null ? p.steamID.Id : 0UL; } catch { return 0UL; } } }

		/// <summary>This machine's player carries something (no jumping, no ziplines).</summary>
		public static bool CarryingLocal { get { return mine != null; } }

		/// <summary>Tests: what this machine's player carries (its name), and how many pieces are carried.</summary>
		internal static string CarriedName { get { return mine != null && mine.Ref != null ? mine.Ref.Name : ""; } }
		internal static int CarriedCount { get { return carried.Count; } }

		internal static void Tell(string text)
		{
			if (Time.time - toldAt < 2f) return;
			toldAt = Time.time;
			IslandInfo.ShowMessage(text);
		}

		/// <summary>The carry action (this machine's player): ask the host for the piece.</summary>
		public static void AskCarry(IslandWorldState.Entry e, IslandObjectRef r)
		{
			if (e == null || r == null) return;
			if (mine != null) { Tell("Your hands are full - put it down first (use key)"); return; }
			if (ContentCatalog.IsZone(r.ObjectName) || ContentCatalog.IsCreature(r.ObjectName)) return;
			if (carried.ContainsKey(Key(e.Id, r.Index))) { Tell("Someone else is carrying that"); return; }
			if (Raft_Network.IsHost) HostCarry(e.Id, r.Index, LocalId);
			else IslandNetwork.SendToTheHost(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "carry", Ids = new[] { e.Id, r.Index } });
		}

		static IslandWorldState.Entry EntryById(int id) { return IslandWorldState.Islands.FirstOrDefault(x => x.Id == id); }

		static IslandObjectRef RefOf(IslandWorldState.Entry e, int index) { return Behaviours.RefsOf(e).FirstOrDefault(r => r.Index == index); }

		static void HostCarry(int island, int index, ulong who)
		{
			IslandWorldState.Entry e = EntryById(island);
			IslandObjectRef r = e != null ? RefOf(e, index) : null;
			bool ok = r != null && r.gameObject.activeInHierarchy && !carried.ContainsKey(Key(island, index)) && !carried.Values.Any(c => c.Who == who);
			var msg = new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = ok ? "held" : "refused", Ids = new[] { island, index }, Data = who.ToString(CultureInfo.InvariantCulture) };
			IslandNetwork.SendToEveryone(msg);
			if (ok) OnHeld(island, index, who); else if (who == LocalId) Tell("Someone else is carrying that");
		}

		static void OnHeld(int island, int index, ulong who)
		{
			string k = Key(island, index);
			Carried c;
			if (carried.TryGetValue(k, out c)) Release(c);
			c = new Carried { Island = island, Index = index, Who = who };
			carried[k] = c;
			Resolve(c);
			if (who == LocalId)
			{
				mine = c;
				pickedAt = Time.time;
				Network_Player p = RAPI.GetLocalPlayer();
				lastHealth = p != null && p.Stats != null && p.Stats.stat_health != null ? p.Stats.stat_health.Value : 0f;
				Tell("Carrying " + (c.Ref != null && c.Ref.Name.Length > 0 ? c.Ref.Name : "it") + " - the use key puts it down");
			}
		}

		/// <summary>The carried piece and its holder found (again: an island loaded again, a player who came back).</summary>
		static void Resolve(Carried c)
		{
			if (c.Ref == null)
			{
				IslandWorldState.Entry e = EntryById(c.Island);
				c.Ref = e != null ? RefOf(e, c.Index) : null;
				c.Off.Clear();
				if (c.Ref != null)
					foreach (Collider col in c.Ref.GetComponentsInChildren<Collider>(true))
						if (col.enabled) { col.enabled = false; c.Off.Add(col); }
			}
			if (c.Holder == null && Time.time - c.LookedAt > 1f)
			{
				c.LookedAt = Time.time;
				Network_Player local = RAPI.GetLocalPlayer();
				if (local != null && local.steamID.Id == c.Who) c.Holder = local;
				else foreach (Network_Player p in UnityEngine.Object.FindObjectsOfType<Network_Player>()) if (p != null && p.steamID.Id == c.Who) { c.Holder = p; break; }
			}
		}

		static void Release(Carried c)
		{
			foreach (Collider col in c.Off) if (col != null) col.enabled = true;
			c.Off.Clear();
			if (mine == c) mine = null;
		}

		/// <summary>This machine's player puts the piece down in front of them (on the ground, or on top of another piece).</summary>
		internal static void DropMine()
		{
			Carried c = mine;
			if (c == null) return;
			mine = null;
			Network_Player p = RAPI.GetLocalPlayer();
			if (c.Ref == null || p == null) { AskDrop(c, Vector3.zero, 0f, false); return; }
			Transform t = c.Ref.transform;
			Vector3 fwd = p.transform.forward; fwd.y = 0f;
			if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
			fwd.Normalize();
			Vector3 at = p.transform.position + fwd * 1.3f;
			float snap = ObjectProps.GetFloat(c.Ref.Props, CarrySnap, 0f);
			Transform parent = t.parent;
			if (snap > 0.05f && parent != null)
			{
				Vector3 l = parent.InverseTransformPoint(at);
				l.x = Mathf.Round(l.x / snap) * snap; l.z = Mathf.Round(l.z / snap) * snap;
				at = parent.TransformPoint(l);
			}
			float bottom = BottomOffset(c.Ref.gameObject);
			Vector3 pos = at;
			RaycastHit hit;
			if (Physics.Raycast(at + Vector3.up * 2.5f, Vector3.down, out hit, 8f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point + Vector3.up * bottom;
			else pos = new Vector3(at.x, p.transform.position.y + bottom, at.z);
			float yaw = p.transform.eulerAngles.y;
			if (snap > 0.05f) yaw = Mathf.Round(yaw / 90f) * 90f;
			Vector3 local = parent != null ? parent.InverseTransformPoint(pos) : pos;
			float localYaw = parent != null ? Mathf.Repeat(yaw - parent.eulerAngles.y, 360f) : yaw;
			LastDrop = pos;
			Place(c.Ref, local, localYaw);
			AskDrop(c, local, localYaw, true);
		}

		/// <summary>How far the object's lowest point is below its pivot (to stand it on the ground).</summary>
		static float BottomOffset(GameObject go)
		{
			Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
			if (rs.Length == 0) return 0f;
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			return Mathf.Max(0f, go.transform.position.y - b.min.y);
		}

		static void AskDrop(Carried c, Vector3 local, float yaw, bool placed)
		{
			var msg = new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "drop", Ids = new[] { c.Island, c.Index }, Offsets = placed ? new[] { local.x, local.y, local.z, yaw } : null };
			if (Raft_Network.IsHost) HostDrop(c.Island, c.Index, LocalId, msg.Offsets);
			else
			{
				Release(c);
				carried.Remove(Key(c.Island, c.Index));
				IslandNetwork.SendToTheHost(msg);
			}
		}

		static void HostDrop(int island, int index, ulong who, float[] at)
		{
			Carried c;
			string k = Key(island, index);
			if (!carried.TryGetValue(k, out c) || c.Who != who) return;
			IslandWorldState.Entry e = EntryById(island);
			// (no place given - a holder who left, a piece not loaded there: where it is now)
			if ((at == null || at.Length < 4) && c.Ref != null)
				at = new[] { c.Ref.transform.localPosition.x, c.Ref.transform.localPosition.y, c.Ref.transform.localPosition.z, c.Ref.transform.localEulerAngles.y };
			if (at == null || at.Length < 4) { Release(c); carried.Remove(k); IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "at", Ids = new[] { island, index } }); return; }
			IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "at", Ids = new[] { island, index }, Offsets = at });
			OnAt(e, island, index, at);
		}

		static void OnAt(IslandWorldState.Entry e, int island, int index, float[] at)
		{
			Carried c;
			string k = Key(island, index);
			if (carried.TryGetValue(k, out c)) { Release(c); carried.Remove(k); }
			if (e == null || at == null || at.Length < 4) return;
			var local = new Vector3(at[0], at[1], at[2]);
			Save(e, index, local, at[3]);
			IslandObjectRef r = RefOf(e, index);
			if (r != null) Place(r, local, at[3]);
		}

		static void Place(IslandObjectRef r, Vector3 local, float yaw)
		{
			Transform t = r.transform;
			Vector3 eu = t.localEulerAngles;
			t.localPosition = local;
			t.localRotation = Quaternion.Euler(eu.x, yaw, eu.z);
			Physics.SyncTransforms();
		}

		/// <summary>Where an object was left, kept in the island's state (saved, sent to joiners).</summary>
		static void Save(IslandWorldState.Entry e, int index, Vector3 local, float yaw)
		{
			int k = PlaceBase + index * 4, day = Behaviours.Today;
			e.State[k] = new ObjectState { Active = true, Yield = Mathf.RoundToInt(local.x * 100f), Day = day };
			e.State[k + 1] = new ObjectState { Active = true, Yield = Mathf.RoundToInt(local.y * 100f), Day = day };
			e.State[k + 2] = new ObjectState { Active = true, Yield = Mathf.RoundToInt(local.z * 100f), Day = day };
			e.State[k + 3] = new ObjectState { Active = true, Yield = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) * 10f), Day = day };
		}

		internal static bool Saved(IslandWorldState.Entry e, int index, out Vector3 local, out float yaw)
		{
			int k = PlaceBase + index * 4;
			ObjectState x, y, z, w;
			local = Vector3.zero; yaw = 0f;
			if (!e.State.TryGetValue(k, out x) || !e.State.TryGetValue(k + 1, out y) || !e.State.TryGetValue(k + 2, out z) || !e.State.TryGetValue(k + 3, out w)) return false;
			local = new Vector3(x.Yield / 100f, y.Yield / 100f, z.Yield / 100f);
			yaw = w.Yield / 10f;
			return true;
		}

		/// <summary>For a player who joins: who carries what now.</summary>
		public static IslandNetMessage HeldMessage()
		{
			if (carried.Count == 0) return null;
			return new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "heldlist", Data = string.Join(";", carried.Values.Select(c => c.Island + "," + c.Index + "," + c.Who.ToString(CultureInfo.InvariantCulture)).ToArray()) };
		}

		static void FollowHolders()
		{
			foreach (Carried c in carried.Values.ToList())
			{
				Resolve(c);
				if (c.Ref == null || c.Holder == null) continue;
				Transform h = c.Holder.transform;
				Vector3 fwd = h.forward; fwd.y = 0f;
				if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
				c.Ref.transform.position = h.position + fwd.normalized * 0.9f + Vector3.up * 0.9f;
				c.Ref.transform.rotation = Quaternion.Euler(0f, h.eulerAngles.y, 0f);
			}
			if (mine == null) return;
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null) return;
			float hp = p.Stats != null && p.Stats.stat_health != null ? p.Stats.stat_health.Value : lastHealth;
			bool hurt = hp < lastHealth - 0.5f;
			lastHealth = hp;
			bool dead = p.PlayerScript != null && p.PlayerScript.IsDead;
			if (hurt || dead) { Tell("Hit - you dropped it"); DropMine(); return; }
			if (Time.time - pickedAt > 0.5f && !NoteReader.IsOpen && CustomNote.InteractPressed()) DropMine();
		}

		/// <summary>Host: a holder who left the game (or the island) puts the piece down where it is.</summary>
		static void HostWatchHolders()
		{
			foreach (Carried c in carried.Values.ToList())
			{
				if (c.Holder != null) { c.Missing = -1f; continue; }
				if (c.Missing < 0f) c.Missing = Time.time;
				else if (Time.time - c.Missing > 6f) HostDrop(c.Island, c.Index, c.Who, null);
			}
		}

		#endregion

		#region Collapse and moveto

		class Anim
		{
			public IslandObjectRef Ref;
			public bool Collapse;
			public Vector3 From, To;
			public Quaternion RotFrom, RotTo;
			public float Start, Seconds;
			public Vector3 Last;
		}

		static readonly List<Anim> anims = new List<Anim>();
		internal static int Collapses, Moves;

		/// <summary>Host: the collapse action - hidden for good (saved now), every machine animates its fall.</summary>
		public static void HostCollapse(IslandWorldState.Entry e, List<IslandObjectRef> targets, string arg)
		{
			float s = Seconds(arg, 3f, 0.5f, 30f);
			var idx = new List<int> { e.Id };
			foreach (IslandObjectRef r in targets)
			{
				bool visible, open;
				Behaviours.StateNow(e, r, out visible, out open);
				if (!visible) continue;
				idx.Add(r.Index);
			}
			if (idx.Count == 1) return;
			IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "collapse", Ids = idx.ToArray(), Offsets = new[] { s } });
			OnCollapse(e, idx.Skip(1), s);
		}

		static void OnCollapse(IslandWorldState.Entry e, IEnumerable<int> indexes, float s)
		{
			if (e == null) return;
			bool rumbled = false;
			foreach (int i in indexes)
			{
				IslandObjectRef r = RefOf(e, i);
				bool visible = true, open = false;
				if (r != null) Behaviours.StateNow(e, r, out visible, out open);
				Behaviours.StoreOnly(e, i, false, open);
				if (r == null || !r.gameObject.activeInHierarchy) { if (r != null) r.gameObject.SetActive(false); continue; }
				Collapses++;
				anims.RemoveAll(a => a.Ref == r);
				anims.Add(new Anim { Ref = r, Collapse = true, From = r.transform.localPosition, RotFrom = r.transform.localRotation, Start = Time.time, Seconds = s });
				if (!rumbled)
				{
					rumbled = true;
					SoundLibrary.PlayOnce("event:/wildlife/boss/boss_rumble", r.transform.position, 1f);
					Network_Player p = RAPI.GetLocalPlayer();
					if (p != null && (p.transform.position - r.transform.position).sqrMagnitude < 40f * 40f)
						try { if (EZCameraShake.CameraShaker.Instance != null) EZCameraShake.CameraShaker.Instance.ShakeOnce(2f, 6f, 0.3f, s); } catch { }
				}
			}
		}

		/// <summary>Host: the moveto action - the objects travel to the marker's place (saved), every machine animates it.</summary>
		public static void HostMoveTo(IslandWorldState.Entry e, int index, List<IslandObjectRef> targets, string arg)
		{
			string markerName; float s;
			MoveArg(arg, out markerName, out s);
			IslandObjectRef marker = Behaviours.TargetsOf(e, index, markerName).FirstOrDefault();
			if (marker == null || markerName.Length == 0) { Debug.LogWarning("[CUSTOM ISLANDS] moveto on '" + e.HostName + "': no marker named '" + markerName + "'"); return; }
			foreach (IslandObjectRef r in targets)
			{
				if (r == marker) continue;
				Transform parent = r.transform.parent;
				Vector3 local = parent != null ? parent.InverseTransformPoint(marker.transform.position) : marker.transform.position;
				float yaw = Mathf.Repeat(marker.transform.eulerAngles.y - (parent != null ? parent.eulerAngles.y : 0f), 360f);
				var at = new[] { local.x, local.y, local.z, yaw, s };
				IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "move", Ids = new[] { e.Id, r.Index }, Offsets = at });
				OnMove(e, r.Index, at);
			}
		}

		static void OnMove(IslandWorldState.Entry e, int index, float[] at)
		{
			if (e == null || at == null || at.Length < 5) return;
			var to = new Vector3(at[0], at[1], at[2]);
			Save(e, index, to, at[3]);
			IslandObjectRef r = RefOf(e, index);
			if (r == null) return;
			Moves++;
			Vector3 eu = r.transform.localEulerAngles;
			Quaternion rotTo = Quaternion.Euler(eu.x, at[3], eu.z);
			anims.RemoveAll(a => a.Ref == r);
			if (at[4] <= 0f || !r.gameObject.activeInHierarchy) { Place(r, to, at[3]); Arrived(e, r); return; }
			anims.Add(new Anim { Ref = r, From = r.transform.localPosition, To = to, RotFrom = r.transform.localRotation, RotTo = rotTo, Start = Time.time, Seconds = at[4], Last = r.transform.position });
		}

		/// <summary>A creature spot that moved: its animals' home is the new place (host - they walk there).</summary>
		static void Arrived(IslandWorldState.Entry e, IslandObjectRef r)
		{
			CreatureSpawnPoint spot = r.GetComponent<CreatureSpawnPoint>();
			if (spot == null || !Raft_Network.IsHost) return;
			foreach (AI_NetworkBehaviour ai in spot.Spawned.Concat(spot.Summoned).ToList())
			{
				if (ai == null) continue;
				foreach (AI_State_CirculateSpawn st in ai.GetComponentsInChildren<AI_State_CirculateSpawn>(true))
				{
					try { st.SetNewSpawnPoint(r.transform.position); } catch { }
					LureOrig o;
					if (lured.TryGetValue(st, out o)) o.Home = r.transform.position;
				}
			}
		}

		static void Animate()
		{
			for (int i = anims.Count - 1; i >= 0; i--)
			{
				Anim a = anims[i];
				if (a.Ref == null) { anims.RemoveAt(i); continue; }
				float t = Mathf.Clamp01((Time.time - a.Start) / Mathf.Max(0.01f, a.Seconds));
				Transform tr = a.Ref.transform;
				if (a.Collapse)
				{
					// (shaking for the first 60 %, then it falls and tips over)
					if (t < 0.6f) tr.localPosition = a.From + new Vector3(UnityEngine.Random.Range(-0.06f, 0.06f), UnityEngine.Random.Range(-0.03f, 0.03f), UnityEngine.Random.Range(-0.06f, 0.06f));
					else
					{
						float f = (t - 0.6f) / 0.4f;
						tr.localPosition = a.From + Vector3.down * (6f * f * f);
						tr.localRotation = a.RotFrom * Quaternion.Euler(25f * f, 0f, 12f * f);
					}
					if (t >= 1f)
					{
						tr.localPosition = a.From;
						tr.localRotation = a.RotFrom;
						a.Ref.gameObject.SetActive(false);
						anims.RemoveAt(i);
					}
					continue;
				}
				float s = Mathf.SmoothStep(0f, 1f, t);
				tr.localPosition = Vector3.Lerp(a.From, a.To, s);
				tr.localRotation = Quaternion.Slerp(a.RotFrom, a.RotTo, s);
				CarryStanding(a.Ref.transform, tr.position - a.Last);
				a.Last = tr.position;
				if (t >= 1f)
				{
					anims.RemoveAt(i);
					IslandWorldState.Entry e = ContentState.EntryOf(tr);
					if (e != null) Arrived(e, a.Ref);
				}
			}
		}

		/// <summary>The local player standing on a moving object goes along (as a lift carries them).</summary>
		static void CarryStanding(Transform obj, Vector3 delta)
		{
			if (delta.sqrMagnitude < 1e-10f) return;
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null) return;
			foreach (RaycastHit hit in Physics.RaycastAll(p.transform.position + Vector3.up * 1.2f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
				if (hit.collider != null && hit.collider.transform.IsChildOf(obj))
				{
					p.transform.position += delta;
					Physics.SyncTransforms();
					return;
				}
		}

		#endregion

		#region Lure

		class LureOrig { public Vector3 Home; public float Radius, RadiusMin, PlayerDistance; }

		static readonly Dictionary<AI_State_CirculateSpawn, LureOrig> lured = new Dictionary<AI_State_CirculateSpawn, LureOrig>();

		static void LureTick()
		{
			var keep = new HashSet<AI_State_CirculateSpawn>();
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null) continue;
				foreach (IslandObjectRef r in Behaviours.RefsOf(e))
				{
					string v = ObjectProps.Get(r.Props, LureProp).Trim();
					if (v.Length == 0) continue;
					bool visible, open;
					Behaviours.StateNow(e, r, out visible, out open);
					if (!open || !r.gameObject.activeInHierarchy) continue;
					string[] parts = v.Split('|');
					float radius = parts.Length > 1 ? Seconds(parts[1], DefaultLureRadius, 0.5f, 20f) : DefaultLureRadius;
					foreach (IslandObjectRef s in Behaviours.TargetsOf(e, r.Index, parts[0].Trim()))
					{
						CreatureSpawnPoint spot = s.GetComponent<CreatureSpawnPoint>();
						if (spot == null) continue;
						foreach (AI_NetworkBehaviour ai in spot.Spawned.Concat(spot.Summoned))
						{
							if (ai == null) continue;
							foreach (AI_State_CirculateSpawn st in ai.GetComponentsInChildren<AI_State_CirculateSpawn>(true))
							{
								keep.Add(st);
								if (lured.ContainsKey(st)) continue;
								Traverse tv = Traverse.Create(st);
								lured[st] = new LureOrig { Home = st.SpawnPointPosition, Radius = tv.Field("circulationRadius").GetValue<float>(), RadiusMin = tv.Field("circulationRadiusMin").GetValue<float>(), PlayerDistance = tv.Field("playerDistance").GetValue<float>() };
								st.SetNewSpawnPoint(r.transform.position);
								tv.Field("circulationRadius").SetValue(radius);
								tv.Field("circulationRadiusMin").SetValue(0f);
								tv.Field("playerDistance").SetValue(0f);
								Lured++;
							}
						}
					}
				}
			}
			foreach (AI_State_CirculateSpawn st in lured.Keys.Where(k => !keep.Contains(k)).ToList())
			{
				LureOrig o = lured[st];
				lured.Remove(st);
				if (st == null) continue;
				try
				{
					Traverse tv = Traverse.Create(st);
					tv.Field("circulationRadius").SetValue(o.Radius);
					tv.Field("circulationRadiusMin").SetValue(o.RadiusMin);
					tv.Field("playerDistance").SetValue(o.PlayerDistance);
					st.SetNewSpawnPoint(o.Home);
				}
				catch { }
			}
		}

		/// <summary>Tests: animals following a lure now.</summary>
		internal static int LuredNow { get { return lured.Keys.Count(k => k != null); } }

		#endregion

		#region Stack zones

		static readonly Dictionary<TriggerZone, int> stackCounts = new Dictionary<TriggerZone, int>();
		static List<KeyValuePair<IslandWorldState.Entry, TriggerZone>> stackZones = new List<KeyValuePair<IslandWorldState.Entry, TriggerZone>>();
		static float nextCache, nextSlow;

		/// <summary>How many pieces with the zone's stack name stand in it (not carried).</summary>
		internal static int StackCount(IslandWorldState.Entry e, TriggerZone z)
		{
			string n; int need;
			if (!StackOf(z.Props, out n, out need)) return 0;
			float r2 = z.Radius * z.Radius;
			return Behaviours.RefsOf(e).Count(r => r.gameObject.activeInHierarchy && r.Name.Equals(n, StringComparison.OrdinalIgnoreCase) &&
				!carried.Values.Any(c => c.Ref == r) && (r.transform.position - z.transform.position).sqrMagnitude <= r2);
		}

		static void StackTick()
		{
			Network_Player p = RAPI.GetLocalPlayer();
			foreach (KeyValuePair<IslandWorldState.Entry, TriggerZone> kv in stackZones)
			{
				TriggerZone z = kv.Value;
				IslandWorldState.Entry e = kv.Key;
				if (z == null || e.Root == null || !z.gameObject.activeInHierarchy) continue;
				string n; int need;
				StackOf(z.Props, out n, out need);
				int count = StackCount(e, z), was;
				if (!stackCounts.TryGetValue(z, out was)) was = count;
				stackCounts[z] = count;
				IslandObjectRef zr = z.GetComponent<IslandObjectRef>();
				if (zr == null) continue;
				int key = StackDoneBase + zr.Index;
				bool done = e.State.ContainsKey(key);
				if (count != was && p != null && (p.transform.position - z.transform.position).sqrMagnitude < (z.Radius + 12f) * (z.Radius + 12f) && !done)
					IslandInfo.ShowMessage("Stacked " + Mathf.Min(count, need) + " of " + need);
				if (Raft_Network.IsHost && !done && count >= need)
				{
					e.State[key] = new ObjectState { Active = true, Day = Behaviours.Today };
					IslandNetwork.SendUsed(e.Id, key, Behaviours.Today);
					Debug.Log("[CUSTOM ISLANDS] Stack zone on '" + e.HostName + "': " + count + " of " + need + " '" + n + "' - its stack event");
					Behaviours.FireFromHost(e, zr.Index, StackEvent);
				}
			}
		}

		#endregion

		#region Boss shield by checks

		/// <summary>Host: a spot's boss.shieldIf checks all hold (players' weapons glance off).</summary>
		internal static bool ShieldHolds(IslandWorldState.Entry e, CreatureSpawnPoint p)
		{
			string lines = ObjectProps.Get(p.Props, ShieldIf).Trim();
			if (lines.Length == 0) return false;
			IslandObjectRef r = p.GetComponent<IslandObjectRef>();
			List<ObjCheck> checks = ObjCheck.ParseLines(lines);
			return checks.Count > 0 && checks.All(c => Behaviours.CheckNow(e, r != null ? r.Index : -1, c));
		}

		#endregion

		#region Tick, island ready, network

		public static void Tick()
		{
			if (!LoadSceneManager.IsGameSceneLoaded) return;
			if (carried.Count > 0) FollowHolders();
			if (anims.Count > 0) Animate();
			if (Time.time >= nextCache)
			{
				nextCache = Time.time + 3f;
				stackZones = IslandWorldState.Islands.Where(e => e.Root != null)
					.SelectMany(e => e.Root.GetComponentsInChildren<TriggerZone>(true).Where(z => IsStack(z.Props)).Select(z => new KeyValuePair<IslandWorldState.Entry, TriggerZone>(e, z))).ToList();
				foreach (TriggerZone gone in stackCounts.Keys.Where(z => z == null).ToList()) stackCounts.Remove(gone);
			}
			if (Time.time < nextSlow) return;
			nextSlow = Time.time + 0.5f;
			if (stackZones.Count > 0) StackTick();
			if (Raft_Network.IsHost)
			{
				LureTick();
				if (carried.Count > 0) HostWatchHolders();
			}
		}

		/// <summary>An island loaded (every machine): carried and moved objects where they were left.</summary>
		public static void OnIslandReady(IslandWorldState.Entry e)
		{
			if (e == null || e.Root == null) return;
			foreach (IslandObjectRef r in Behaviours.RefsOf(e))
			{
				Vector3 local; float yaw;
				if (Saved(e, r.Index, out local, out yaw)) Place(r, local, yaw);
			}
			foreach (Carried c in carried.Values) if (c.Island == e.Id) { c.Ref = null; Resolve(c); }
		}

		public static void OnMessage(IslandNetMessage msg, ulong from)
		{
			if (msg == null) return;
			int island = msg.Ids != null && msg.Ids.Length > 0 ? msg.Ids[0] : 0, index = msg.Ids != null && msg.Ids.Length > 1 ? msg.Ids[1] : -1;
			ulong who;
			switch (msg.Name)
			{
				case "carry":
					if (!Raft_Network.IsHost || index < 0) return;
					if (!PlayerTrust.MayMark(from, island, "a carried piece")) { IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "refused", Ids = msg.Ids, Data = from.ToString(CultureInfo.InvariantCulture) }); return; }
					HostCarry(island, index, from);
					return;
				case "drop":
					if (Raft_Network.IsHost && index >= 0) HostDrop(island, index, from, msg.Offsets);
					return;
			}
			if (Raft_Network.IsHost) return;
			switch (msg.Name)
			{
				case "held":
					if (index >= 0 && ulong.TryParse(msg.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out who)) OnHeld(island, index, who);
					break;
				case "refused":
					if (ulong.TryParse(msg.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out who) && who == LocalId) Tell("Someone else is carrying that");
					break;
				case "at":
					OnAt(EntryById(island), island, index, msg.Offsets);
					break;
				case "move":
					OnMove(EntryById(island), index, msg.Offsets);
					break;
				case "collapse":
					if (msg.Ids != null && msg.Ids.Length > 1) OnCollapse(EntryById(island), msg.Ids.Skip(1), msg.Offsets != null && msg.Offsets.Length > 0 ? msg.Offsets[0] : 3f);
					break;
				case "heldlist":
					foreach (string item in (msg.Data ?? "").Split(';'))
					{
						string[] f = item.Split(',');
						int i, x;
						if (f.Length == 3 && int.TryParse(f[0], out i) && int.TryParse(f[1], out x) && ulong.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out who)) OnHeld(i, x, who);
					}
					break;
			}
		}

		/// <summary>Leaving a world: nothing is carried.</summary>
		public static void Reset()
		{
			foreach (Carried c in carried.Values) Release(c);
			carried.Clear();
			mine = null;
			anims.Clear();
			lured.Clear();
			stackCounts.Clear();
		}

		#endregion

		/// <summary>Placed from the list in the editor: sensible starting values.</summary>
		public static void EditorDefaults(string name, Dictionary<string, string> props) { }
	}

	/// <summary>No jumping while carrying a piece.</summary>
	[HarmonyPatch(typeof(PersonController), "AllowJump")]
	static class CarryJumpPatch
	{
		static void Postfix(PersonController __instance, ref bool __result)
		{
			try
			{
				if (!__result || !MechFeatures.CarryingLocal) return;
				Network_Player p = RAPI.GetLocalPlayer();
				if (p == null || p.PersonController != __instance) return;
				__result = false;
				if (MyInput.GetButtonDown("Jump")) MechFeatures.Tell("Too heavy to jump with - put it down first (use key)");
			}
			catch { }
		}
	}

	/// <summary>No ziplines while carrying a piece.</summary>
	[HarmonyPatch(typeof(ZiplinePlayer), "AttachToZipline")]
	static class CarryZiplinePatch
	{
		static bool Prefix(ZiplinePlayer __instance, ref bool __result)
		{
			try
			{
				if (!MechFeatures.CarryingLocal) return true;
				Network_Player p = RAPI.GetLocalPlayer();
				if (p == null || __instance.GetComponentInParent<Network_Player>() != p) return true;
				MechFeatures.Tell("Your hands are full - put it down first (use key)");
				__result = false;
				return false;
			}
			catch { return true; }
		}
	}
}
