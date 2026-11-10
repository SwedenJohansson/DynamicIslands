using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// QE12 (3.5): islanders that walk. Two parts:
	///   A walking path (islander.path): the islander walks from where it was placed through named objects or trigger
	///   zones and back (loop) or there and back (pingpong), pausing at each. Every machine works its place out from the
	///   shared clock (SharedClock), so nothing is sent: all players see it at the same spot.
	///   An escort quest step ("escort", target Name@zone): while it is the current step, the islander follows the nearest
	///   player (the host decides and sends "walk" places; clients ease to them). It waits while that player is more than
	///   WaitDistance away, walks only on the island's ground above the sea (never onto a raft, off a cliff or into the air),
	///   and when it is stuck a while it steps to the ground behind the player. In the zone it stays: the host saves its
	///   place there (MechFeatures.HostPlace, sent to everyone and to joiners) and the step is done.
	/// An islander with a saved place (escorted, or moved by a "move to" action) stands there: its path is over.
	/// </summary>
	public static class IslanderWalks
	{
		public const string PathProp = "islander.path", ModeProp = "islander.pathmode", SpeedProp = "islander.speed", PauseProp = "islander.pause";
		public const string QuestType = "escort";
		public static readonly string[] Modes = { "loop", "pingpong" };
		public const float DefaultSpeed = 1.4f, DefaultPause = 2f, FollowDistance = 2.5f, WaitDistance = 12f, StuckSeconds = 6f;
		/// <summary>Tests: escort places the host sent / took.</summary>
		public static int Sent, Received, Unstuck;

		public static string PathOf(IDictionary<string, string> p) { return ObjectProps.Get(p, PathProp).Trim(); }
		public static string ModeOf(IDictionary<string, string> p) { string m = ObjectProps.Get(p, ModeProp).Trim().ToLowerInvariant(); return Modes.Contains(m) ? m : "loop"; }
		public static float SpeedOf(IDictionary<string, string> p) { return Mathf.Clamp(ObjectProps.GetFloat(p, SpeedProp, DefaultSpeed), 0.3f, 4f); }
		public static float PauseOf(IDictionary<string, string> p) { return Mathf.Clamp(ObjectProps.GetFloat(p, PauseProp, DefaultPause), 0f, 60f); }

		/// <summary>The path's names, in order ("a, b, c").</summary>
		public static List<string> Names(string path)
		{
			return (path ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
		}

		/// <summary>An escort step's target: "Name@zone" (the zone's name; no @: the islander's name only).</summary>
		public static void SplitTarget(string target, out string name, out string zone)
		{
			string t = (target ?? "").Trim();
			int at = t.LastIndexOf('@');
			name = at >= 0 ? t.Substring(0, at).Trim() : t;
			zone = at >= 0 ? t.Substring(at + 1).Trim() : "";
		}

		internal static string Describe(string target)
		{
			string name, zone;
			SplitTarget(target, out name, out zone);
			return "Bring " + (name.Length > 0 ? name : "the islander") + (zone.Length > 0 ? " to " + zone : " along");
		}

		/// <summary>What is wrong with an escort step (null = fine): the plan check and the quest editor.</summary>
		internal static string StepProblem(HashSet<string> islanders, HashSet<string> zones, string target)
		{
			string name, zone;
			SplitTarget(target, out name, out zone);
			if (name.Length == 0 || zone.Length == 0) return "it needs Name@zone: the islander and the trigger zone to bring it to";
			if (!islanders.Contains(name)) return "no islander is called '" + name + "'" + (islanders.Count > 0 ? " (" + string.Join(", ", islanders.ToArray()) + ")" : "");
			if (!zones.Contains(zone)) return "no trigger zone is called '" + zone + "'" + (zones.Count > 0 ? " (" + string.Join(", ", zones.ToArray()) + ")" : "");
			return null;
		}

		/// <summary>The path's names no object or zone of the island has (the editor's warning).</summary>
		internal static List<string> Missing(string path, IEnumerable<EditorGameObject> all)
		{
			var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (EditorGameObject g in all)
			{
				string n = ObjectProps.Get(g.Props, BehaviourProps.Name).Trim(), z = ObjectProps.Get(g.Props, ObjectProps.ZoneId).Trim();
				if (n.Length > 0) known.Add(n);
				if (z.Length > 0) known.Add(z);
			}
			return Names(path).Where(n => !known.Contains(n)).ToList();
		}

		#region World

		/// <summary>A zone of the island by its name.</summary>
		internal static TriggerZone ZoneOf(IslandWorldState.Entry e, string id)
		{
			if (e == null || e.Root == null || string.IsNullOrEmpty(id)) return null;
			return e.Root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		internal static bool Inside(TriggerZone z, Vector3 p)
		{
			if (z == null) return false;
			Vector3 d = p - z.transform.position;
			return new Vector2(d.x, d.z).magnitude <= z.Radius && Mathf.Abs(d.y) <= z.Radius + 3f;
		}

		static IslanderWalk WalkerOf(IslandWorldState.Entry e, string name)
		{
			return Behaviours.RefsOf(e).Where(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
				.Select(r => r.GetComponentInChildren<IslanderWalk>(true)).FirstOrDefault(w => w != null);
		}

		/// <summary>Quest: 1 when the escort step's islander is in its zone.</summary>
		public static int Found(IslandWorldState.Entry e, string target)
		{
			string name, zone;
			SplitTarget(target, out name, out zone);
			IslanderWalk w = WalkerOf(e, name);
			return w != null && Inside(ZoneOf(e, zone), w.transform.position) ? 1 : 0;
		}

		static readonly Dictionary<int, KeyValuePair<float, List<string>>> escorts = new Dictionary<int, KeyValuePair<float, List<string>>>();

		/// <summary>The zone an escort step that is current now wants this islander in (null: none). Read twice a second at most.</summary>
		internal static string EscortZone(IslandWorldState.Entry e, string name)
		{
			if (e == null || string.IsNullOrEmpty(name)) return null;
			KeyValuePair<float, List<string>> c;
			if (!escorts.TryGetValue(e.Id, out c) || Time.time - c.Key > 0.5f)
			{
				var list = new List<string>();
				for (int n = 0, count = QuestTracker.QuestsOf(e); n < count; n++)
				{
					IslandQuest q = QuestTracker.QuestOf(e, n);
					int step = QuestTracker.StepOf(e, n);
					if (q.Exists && step < q.Steps.Count && q.Steps[step].Type == QuestType) list.Add(q.Steps[step].Target);
				}
				escorts[e.Id] = c = new KeyValuePair<float, List<string>>(Time.time, list);
			}
			foreach (string t in c.Value)
			{
				string who, zone;
				SplitTarget(t, out who, out zone);
				if (who.Equals(name, StringComparison.OrdinalIgnoreCase) && zone.Length > 0) return zone;
			}
			return null;
		}

		/// <summary>The ground under a point near height nearY: the island's (or its objects') surface above the sea, not
		/// a raft, a player or the islander itself. The surface nearest nearY wins (a floor, not the roof over it).</summary>
		internal static bool Ground(Transform self, Vector3 at, float nearY, out float y)
		{
			y = nearY;
			float best = float.MaxValue;
			foreach (RaycastHit h in Physics.RaycastAll(new Vector3(at.x, nearY + 1.8f, at.z), Vector3.down, 4.6f, ~0, QueryTriggerInteraction.Ignore))
			{
				Collider c = h.collider;
				if (c == null || (self != null && c.transform.IsChildOf(self))) continue;
				if (c.GetComponentInParent<Network_Player>() != null || c.GetComponentInParent<Raft>() != null) continue;
				if (h.point.y < 0.05f) continue; // (the sea)
				float d = Mathf.Abs(h.point.y - nearY);
				if (d < best) { best = d; y = h.point.y; }
			}
			return best < float.MaxValue;
		}

		/// <summary>Clients: the host moved an escorted islander.</summary>
		internal static void OnWalk(IslandNetMessage msg)
		{
			if (msg.Ids == null || msg.Ids.Length < 2 || msg.Offsets == null || msg.Offsets.Length < 4) return;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == msg.Ids[0]);
			IslandObjectRef r = Behaviours.RefsOf(e).FirstOrDefault(x => x.Index == msg.Ids[1]);
			IslanderWalk w = r != null ? r.GetComponentInChildren<IslanderWalk>(true) : null;
			if (w == null) return;
			Received++;
			w.Remote(new Vector3(msg.Offsets[0], msg.Offsets[1], msg.Offsets[2]), msg.Offsets[3]);
		}

		internal static void Send(IslandWorldState.Entry e, int index, Vector3 local, float yaw)
		{
			Sent++;
			IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "walk", Ids = new[] { e.Id, index }, Offsets = new[] { local.x, local.y, local.z, yaw } });
		}

		public static void Reset() { escorts.Clear(); }

		#endregion
	}

	/// <summary>On an islander in a world (Islanders.Attach): its walk - its path, or an escort step's following.</summary>
	public class IslanderWalk : MonoBehaviour
	{
		public IslandObjectRef Ref;
		IslandWorldState.Entry entry;
		readonly List<Vector3> route = new List<Vector3>();
		readonly List<float> legStart = new List<float>();
		float cycle, speed, pause, nextLook, nextSend, stuck, shownSpeed, remoteUntil, remoteYaw;
		bool built, saved, arrived;
		Vector3 home, remoteAt, lastSent;
		string zone;
		Animator anim;

		/// <summary>What it is doing now (tests): "path", "follow", "wait", "stuck", "arrived", "home", "idle".</summary>
		public string State = "idle";
		public bool HasPath { get { return route.Count > 1; } }
		public float Speed { get { return shownSpeed; } }

		Transform Parent { get { return transform.parent; } }

		void Build()
		{
			built = true;
			entry = ContentState.EntryOf(transform);
			home = transform.localPosition;
			Vector3 l; float yaw;
			saved = entry != null && Ref != null && MechFeatures.Saved(entry, Ref.Index, out l, out yaw);
			speed = IslanderWalks.SpeedOf(Ref.Props);
			pause = IslanderWalks.PauseOf(Ref.Props);
			route.Clear(); legStart.Clear(); cycle = 0f;
			var pts = new List<Vector3> { home };
			foreach (string n in IslanderWalks.Names(IslanderWalks.PathOf(Ref.Props)))
			{
				Transform t = Behaviours.TargetsOf(entry, Ref.Index, n).Select(r => r.transform).FirstOrDefault(x => x != transform)
					?? (IslanderWalks.ZoneOf(entry, n) != null ? IslanderWalks.ZoneOf(entry, n).transform : null);
				if (t == null) { Debug.LogWarning("[CUSTOM ISLANDS] Islander '" + Ref.Name + "': no object or zone '" + n + "' for its path"); continue; }
				pts.Add(Parent != null ? Parent.InverseTransformPoint(t.position) : t.position);
			}
			if (pts.Count < 2) return;
			route.AddRange(pts);
			if (IslanderWalks.ModeOf(Ref.Props) == "pingpong") for (int i = pts.Count - 2; i >= 0; i--) route.Add(pts[i]);
			else route.Add(pts[0]);
			for (int i = 0; i + 1 < route.Count; i++)
			{
				legStart.Add(cycle);
				cycle += Flat(route[i + 1] - route[i]).magnitude / speed + pause;
			}
		}

		static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

		/// <summary>Where the path has it now (parent space) and which way it faces.</summary>
		Vector3 PathAt(float t, out Vector3 dir)
		{
			t = Mathf.Repeat(t, cycle);
			int i = legStart.Count - 1;
			while (i > 0 && legStart[i] > t) i--;
			Vector3 a = route[i], b = route[i + 1];
			dir = Flat(b - a);
			float walk = dir.magnitude / speed, into = t - legStart[i];
			return into >= walk ? b : Vector3.Lerp(a, b, walk > 0f ? into / walk : 1f);
		}

		internal void Remote(Vector3 local, float yaw)
		{
			remoteAt = local; remoteYaw = yaw; remoteUntil = Time.time + 3f;
		}

		void Update()
		{
			if (Ref == null) return;
			if (!built) Build();
			if (entry == null) return;
			if (Time.time >= nextLook)
			{
				nextLook = Time.time + 0.5f;
				Vector3 l; float yaw;
				bool was = saved;
				saved = MechFeatures.Saved(entry, Ref.Index, out l, out yaw);
				if (saved && !was) home = l;
				zone = Raft_Network.IsHost ? IslanderWalks.EscortZone(entry, Ref.Name) : null;
				if (zone == null) arrived = false;
				if (anim == null) anim = GetComponentInChildren<Animator>();
			}
			Vector3 before = transform.localPosition;
			if (!Raft_Network.IsHost && Time.time < remoteUntil) { Ease(remoteAt, remoteYaw); State = "follow"; }
			else if (zone != null) Escort();
			else if (HasPath && !saved) FollowPath();
			else if (!saved && Flat(transform.localPosition - home).magnitude > 0.3f && State != "idle") WalkTo(home, speed, "home"); // (an escort that ended short: back)
			else State = "idle";
			float moved = Flat(transform.localPosition - before).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
			shownSpeed = Mathf.Lerp(shownSpeed, moved, Mathf.Clamp01(Time.deltaTime * 8f));
			Animate(shownSpeed);
		}

		void FollowPath()
		{
			// (a player talking to it close by: it waits for them; the shared clock goes on, so it catches up afterwards)
			Network_Player me = RAPI.GetLocalPlayer();
			if (DialoguePanel.IsOpen && me != null && (me.transform.position - transform.position).sqrMagnitude < 25f) { State = "path"; return; }
			Vector3 dir;
			Vector3 want = PathAt(SharedClock.Now, out dir);
			float far = Flat(want - transform.localPosition).magnitude;
			if (far > 40f) { Put(want, dir); State = "path"; return; } // (an island just come in sight)
			WalkTo(want, far > 1.5f ? speed * 2f : speed * 1.2f, "path");
			if (far < 0.05f && dir.sqrMagnitude > 1e-4f) Face(dir);
		}

		/// <summary>Host: the escort step - follow the nearest player, wait, get unstuck, stay in the zone.</summary>
		void Escort()
		{
			TriggerZone z = IslanderWalks.ZoneOf(entry, zone);
			if (arrived || IslanderWalks.Inside(z, transform.position))
			{
				if (!arrived)
				{
					arrived = true;
					Vector3 l = transform.localPosition;
					MechFeatures.HostPlace(entry, Ref.Index, l, transform.localEulerAngles.y);
					home = l; saved = true;
					Debug.Log("[CUSTOM ISLANDS] Islander '" + Ref.Name + "' reached zone '" + zone + "' on '" + entry.HostName + "'");
				}
				State = "arrived";
				return;
			}
			Network_Player p = UnityEngine.Object.FindObjectsOfType<Network_Player>().Where(x => x != null)
				.OrderBy(x => (x.transform.position - transform.position).sqrMagnitude).FirstOrDefault();
			if (p == null) { State = "wait"; return; }
			Vector3 to = Flat(p.transform.position - transform.position);
			float d = to.magnitude;
			if (d > IslanderWalks.WaitDistance || d <= IslanderWalks.FollowDistance)
			{
				if (to.sqrMagnitude > 1e-4f) Face(Parent != null ? Parent.InverseTransformDirection(to) : to);
				State = "wait"; stuck = 0f;
				SendMaybe();
				return;
			}
			float spd = Mathf.Clamp(d - IslanderWalks.FollowDistance + 1.2f, 1.2f, 5f);
			Vector3 step = to / d * Mathf.Min(spd * Time.deltaTime, d - IslanderWalks.FollowDistance);
			Vector3 next = transform.position + step;
			float y = 0f;
			bool ok =OnIsland(next) && IslanderWalks.Ground(transform, next, transform.position.y, out y) && Mathf.Abs(y - transform.position.y) <= 0.35f + step.magnitude * 1.2f;
			if (ok)
			{
				transform.position = new Vector3(next.x, y, next.z);
				Face(Parent != null ? Parent.InverseTransformDirection(to) : to);
				stuck = 0f; State = "follow";
			}
			else
			{
				stuck += Time.deltaTime; State = "stuck";
				if (stuck > IslanderWalks.StuckSeconds) Unstick(p);
			}
			SendMaybe();
		}

		/// <summary>Stuck behind a cliff or a wall: onto the ground behind the player (or beside them), if there is some.</summary>
		void Unstick(Network_Player p)
		{
			Vector3 back = Flat(-p.transform.forward).normalized, side = Vector3.Cross(Vector3.up, back);
			foreach (Vector3 off in new[] { back * 1.8f, side * 1.8f, -side * 1.8f, back * 3f })
			{
				Vector3 at = p.transform.position + off;
				float y;
				if (!OnIsland(at) || !IslanderWalks.Ground(transform, at, p.transform.position.y, out y)) continue;
				transform.position = new Vector3(at.x, y, at.z);
				stuck = 0f; IslanderWalks.Unstuck++;
				SendMaybe(true);
				return;
			}
			stuck = IslanderWalks.StuckSeconds * 0.5f; // (nowhere yet: tried again in a while)
		}

		bool OnIsland(Vector3 p)
		{
			float r = Mathf.Max(20f, CustomIslandSpawner.LandRadius(entry.Name)) + 5f;
			return new Vector2(p.x - entry.Position.x, p.z - entry.Position.z).magnitude < r;
		}

		void SendMaybe(bool now = false)
		{
			if (!Raft_Network.IsHost || entry == null) return;
			Vector3 l = transform.localPosition;
			if (!now && (Time.time < nextSend || (l - lastSent).sqrMagnitude < 0.0025f)) return;
			nextSend = Time.time + 0.25f; lastSent = l;
			IslanderWalks.Send(entry, Ref.Index, l, transform.localEulerAngles.y);
		}

		/// <summary>Walks toward a place (parent space) on the ground; a step onto no ground or a drop: it stands.</summary>
		void WalkTo(Vector3 want, float spd, string state)
		{
			State = state;
			Vector3 cur = transform.localPosition, d = Flat(want - cur);
			float len = d.magnitude;
			if (len < 0.02f) return;
			Vector3 nextLocal = cur + d / len * Mathf.Min(len, spd * Time.deltaTime);
			nextLocal.y = Mathf.Lerp(cur.y, want.y, len > 0f ? Mathf.Min(1f, spd * Time.deltaTime / len) : 1f);
			Vector3 world = Parent != null ? Parent.TransformPoint(nextLocal) : nextLocal;
			float y;
			if (IslanderWalks.Ground(transform, world, transform.position.y, out y)) world.y = y;
			else world.y = Mathf.Max(world.y, 0.05f);
			transform.position = world;
			Face(d);
		}

		/// <summary>Client: to the host's escort place.</summary>
		void Ease(Vector3 local, float yaw)
		{
			Vector3 cur = transform.localPosition;
			transform.localPosition = (local - cur).sqrMagnitude > 100f ? local : Vector3.MoveTowards(cur, local, 6f * Time.deltaTime);
			Vector3 eu = transform.localEulerAngles;
			transform.localRotation = Quaternion.Euler(eu.x, Mathf.MoveTowardsAngle(eu.y, yaw, 360f * Time.deltaTime), eu.z);
		}

		void Put(Vector3 local, Vector3 dir)
		{
			Vector3 world = Parent != null ? Parent.TransformPoint(local) : local;
			float y;
			if (IslanderWalks.Ground(transform, world, world.y, out y)) world.y = y;
			transform.position = world;
			if (dir.sqrMagnitude > 1e-4f) Face(dir, true);
		}

		/// <summary>Turns toward a direction (parent space), a turn at a time.</summary>
		void Face(Vector3 dir, bool now = false)
		{
			if (dir.sqrMagnitude < 1e-4f) return;
			float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
			Vector3 eu = transform.localEulerAngles;
			transform.localRotation = Quaternion.Euler(eu.x, now ? want : Mathf.MoveTowardsAngle(eu.y, want, 300f * Time.deltaTime), eu.z);
		}

		/// <summary>Raft's character animator: its move parameters (what the third-person controller has).</summary>
		void Animate(float v)
		{
			if (anim == null || anim.runtimeAnimatorController == null) return;
			bool moving = v > 0.15f;
			try
			{
				foreach (AnimatorControllerParameter par in anim.parameters)
				{
					if (par.type == AnimatorControllerParameterType.Float && (par.name == "Velocity" || par.name == "VelocityZ" || par.name == "Speed" || par.name == "MoveSpeed" || par.name == "Forward" || par.name == "Vertical")) anim.SetFloat(par.name, v);
					else if (par.type == AnimatorControllerParameterType.Bool && (par.name == "Moving" || par.name == "Walk" || par.name == "Walking" || par.name == "IsMoving" || par.name == "Grounded" || par.name == "OnGround")) anim.SetBool(par.name, par.name.IndexOf("round", StringComparison.Ordinal) >= 0 || moving);
				}
			}
			catch { anim = null; }
		}

		/// <summary>Tests: the animator's parameter names.</summary>
		public string AnimatorParameters()
		{
			Animator a = GetComponentInChildren<Animator>();
			if (a == null || a.runtimeAnimatorController == null) return "(no animator)";
			return string.Join(",", a.parameters.Select(x => x.name + ":" + x.type).ToArray());
		}
	}
}
