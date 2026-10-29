using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// PQ11 (2026-10-27): can a player get from the water to every quest target of an island (note, chest, zone, lever,
	/// islander) and back out to the water again, without building and without teleports? A grid over the island measured
	/// with Raft's own colliders: standing spots (a floor with room for the player), the sea's surface, the water under it
	/// (1 m cells), ladders (their climb colliders); moves between them as Raft's controller makes them (a 0.55 m step, a
	/// 1.1 m jump, drops of up to 12 m, out of the water onto 1.2 m ledges, swimming anywhere there is room). From the
	/// open sea on four sides to a spot where the target can be used (2.5 m from the eye, nothing in between), and from that
	/// spot back to the open sea. Also lists ladders that can't be taken hold of or lead nowhere, and every place a player can
	/// get into but not out of (a pit, a shaft, a sealed room).
	///   CIReach &lt;island&gt;     the saved island brought into the world (as a play's island step), measured, removed
	///   CIReachOn [label] / CIReachOff   while on, every play measures each island it played as it leaves it - in the state
	///                         the quest left it (doors open, bridges down): a plan's test measures its whole storyline
	/// Lines: "REACH &lt;island&gt; | &lt;target&gt; | &lt;steps&gt; | PASS|FAIL ..." and Mods\DynamicIslands\reach\&lt;island&gt;.txt.
	/// </summary>
	public static partial class DevTests
	{
		static bool reachHook, reachControls;
		static string reachLabel = "";
		// (how many of the walker's moves a frame: 1 = a player's pace; every FAIL and every walk that crossed a collider is
		// walked again at 1)
		static int reachSpeed = 3;
		// (CI only: Raft's own walk and swim speeds times this while a walk runs above 1x; Raft moves the player with
		// CharacterController.Move, a swept move, so a faster pace still stops at every wall; off in the air, so jumps reach
		// no farther than a player's; 1 = off)
		static float reachPace = 1f, reachPaceOn = 1f, reachWalk0, reachSwim0;
		static int reachPrechecked, reachWalked, reachRewalked, reachCcNoted, reachRespawns;
		// (spots walks got stuck at on this island (island-local) and how many targets each blocked; once one spot blocks 3
		// targets the island's other walks are not tried - each is a FAIL with that cause. Dead Letter Office: every chest walk
		// stuck at the same shore lip after 3 jumps, PQ11 10-28)
		static List<Vector3> reachStuckAt = new List<Vector3>();
		static List<int> reachStuckN = new List<int>();
		static string reachBlocked;

		[ConsoleCommand(name: "CIReachSpeed", docs: "Dev, anywhere: PQ11 - the reach walker's speed: moves a frame, each one swept by Raft's own CharacterController (1 = a player's pace, 0 = no walks, the grid alone); pace: Raft's walk/swim speed times p on the ground and in the water (not in the air) while above 1x. CIReachSpeed <n> [pace]")]
		public static void ReachSpeedCommand(string[] args)
		{
			if (args != null && args.Length > 0) reachSpeed = Mathf.Clamp((int)F(args[0]), 0, 10);
			if (args != null && args.Length > 1) reachPace = Mathf.Clamp(F(args[1]), 1f, 4f);
			Log("PASS: reach speed " + reachSpeed + ", pace " + reachPace.ToString("F1") + " (" + reachPrechecked + " prechecked, " + reachWalked + " walked, " + reachRewalked + " walked again at 1x so far)");
		}

		[ConsoleCommand(name: "CIReachControls", docs: "Dev, anywhere: PQ11 - with each island measured, the walker also walks straight at walls (a floor beyond a wall too high to step or jump) at the reach speed: none may be got through (a walker slipping through walls would pass what a player can't). CIReachControls on|off")]
		public static void ReachControlsCommand(string[] args)
		{
			reachControls = args == null || args.Length == 0 || args[0] != "off";
			Log("PASS: reach controls " + (reachControls ? "on" : "off"));
		}

		[ConsoleCommand(name: "CIReachSwim", docs: "Dev, world: PQ11 - the reach walker's swim alone: the player put in the sea 150 m off, Walk held 3 s with and without a look pitch; the metres moved")]
		/// <summary>Something solid on the line between two grid spots, from either end (a mesh is hit from its front only).</summary>
		static bool Between(Vector3 a, Vector3 b, int mask)
		{
			return Physics.Linecast(a, b, mask, QueryTriggerInteraction.Ignore) || Physics.Linecast(b, a, mask, QueryTriggerInteraction.Ignore);
		}
		public static void ReachSwimCommand(string[] args) { StartTest(ReachSwimRoutine()); }

		static IEnumerator ReachSwimRoutine()
		{
			Network_Player pl = RAPI.GetLocalPlayer();
			if (pl == null) { Fail("no player"); yield break; }
			Vector3 start = pl.transform.position + new Vector3(150f, 0f, 0f); start.y = -0.4f;
			ReachInputOn(1);
			try
			{
				foreach (float pitch in new[] { 0f, 20f, -20f })
				{
					PlayerMove.To(pl, start, ControllerType.Water);
					mmWalk = 0f;
					for (float t = 0f; t < 1.5f; t += Time.deltaTime) { MmHold(pl, false); yield return null; }
					Vector3 p0 = pl.transform.position;
					for (float t = 0f; t < 3f; t += Time.deltaTime) { Look(pl, 90f, pitch); mmWalk = 1f; MmHold(pl, false); yield return null; }
					Vector3 d = pl.transform.position - p0;
					Log("swim pitch " + pitch + ": moved " + d.ToString("F2") + " walk axis " + MyInput.GetAxis("Walk").ToString("F1") + ", free " + pl.PersonController.IsMovementFree + ", " + pl.PersonController.controllerType + ReachDiag(pl));
				}
			}
			finally { ReachInputOff(); }
			Log("PASS: reach swim measured");
		}

		[ConsoleCommand(name: "CIReachWhy", docs: "Dev: PQ11 - the player's movement state: every bool field and property of PersonController and Network_Player")]
		public static void ReachWhyCommand(string[] args)
		{
			Network_Player pl = RAPI.GetLocalPlayer();
			if (pl == null) { Fail("no player"); return; }
			const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
			foreach (object o in new object[] { pl.PersonController, pl })
			{
				var bits = new List<string>();
				foreach (var f in o.GetType().GetFields(bf)) if (f.FieldType == typeof(bool)) try { bits.Add(f.Name + "=" + f.GetValue(o)); } catch { }
				foreach (var pr in o.GetType().GetProperties(bf)) if (pr.PropertyType == typeof(bool) && pr.GetIndexParameters().Length == 0) try { bits.Add(pr.Name + "()=" + pr.GetValue(o, null)); } catch { }
				Log(o.GetType().Name + ": " + string.Join(", ", bits.ToArray()));
			}
			Log("PASS: reach why");
		}

		[ConsoleCommand(name: "CIReachProbe", docs: "Dev, world (host): PQ11 - the walker alone between two points of an island (as the walk failures give them: metres from the island's position), Raft's own Walk at 1x for 8 s (jumping once when stopped), logging where it is each 0.25 s and what is around its feet. The island is brought once and kept for more probes. CIReachProbe <island> x y z tx ty tz")]
		public static void ReachProbeCommand(string[] args)
		{
			if (args == null || args.Length < 7) { Fail("CIReachProbe <island> x y z tx ty tz"); return; }
			float[] v = args.Skip(args.Length - 6).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
			StartTest(ReachProbeRoutine(string.Join(" ", args.Take(args.Length - 6).ToArray()), new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5])));
		}

		[ConsoleCommand(name: "CIReachHeights", docs: "Dev, world (host): PQ11 - the height of what a player would stand on (a ray down, triggers ignored), as a grid of numbers round a point of an island (metres from the island's position, as the reach report gives them), to plan a ramp. CIReachHeights <island> x z half step")]
		public static void ReachHeightsCommand(string[] args)
		{
			if (args == null || args.Length < 5) { Fail("CIReachHeights <island> x z half step"); return; }
			float[] v = args.Skip(args.Length - 4).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
			StartTest(ReachHeightsRoutine(string.Join(" ", args.Take(args.Length - 4).ToArray()), v[0], v[1], v[2], Mathf.Max(0.5f, v[3])));
		}

		static IEnumerator ReachHeightsRoutine(string island, float cx, float cz, float half, float step)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CIReachHeights: host, in a world"); yield break; }
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase) && x.Root != null);
			if (e == null) { Fail("CIReachHeights: '" + island + "' is not here"); yield break; }
			Physics.SyncTransforms();
			int mask = reachMask != 0 ? reachMask : ~0;
			var head = new System.Text.StringBuilder("  heights x:");
			for (float x = cx - half; x <= cx + half + 0.01f; x += step) head.Append(" " + x.ToString("F0").PadLeft(5));
			Log(head.ToString());
			for (float z = cz + half; z >= cz - half - 0.01f; z -= step)
			{
				var row = new System.Text.StringBuilder("  z " + z.ToString("F0").PadLeft(5) + ":");
				for (float x = cx - half; x <= cx + half + 0.01f; x += step)
				{
					RaycastHit h;
					Vector3 o = e.Position + new Vector3(x, 120f, z);
					row.Append(Physics.Raycast(o, Vector3.down, out h, 400f, mask, QueryTriggerInteraction.Ignore) ? " " + (h.point.y - e.Position.y).ToString("F1").PadLeft(5) : "     -");
				}
				Log(row.ToString());
			}
			Log("PASS: reach heights");
		}

		static IEnumerator ReachProbeRoutine(string island, Vector3 from, Vector3 to)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CIReachProbe: host, in a world"); yield break; }
			yield return EnsureAlive();
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase) && x.Root != null);
			if (e == null)
			{
				var made = new List<IslandWorldState.Entry>();
				Vector3? spot = ScSpot(island, 400f, IslandSpawner.ElevationOf(island));
				if (!spot.HasValue) { Fail("CIReachProbe: no open sea"); yield break; }
				yield return ScBring(island, spot.Value, made);
				e = made.LastOrDefault();
				if (e == null || e.Root == null) { Fail("CIReachProbe: '" + island + "' didn't come"); yield break; }
				yield return WaitIslandSettled(e, 0.5f, 3f);
			}
			Network_Player pl = RAPI.GetLocalPlayer();
			Physics.SyncTransforms();
			if (reachMask == 0) reachMask = ~0;
			Vector3 a = e.Position + from, b = e.Position + to;
			ReachInputOn(1);
			try
			{
				// (what the grid's checks see on the way a->b, before the player stands there (its own collider is in the mask): a flat ray every 0.15 m up from the feet, and the swept body)
				{
					Vector3 fa = a, fb = b; fb.y = fa.y; Vector3 fd = fb - fa;
					var rays = new System.Text.StringBuilder("  rays a->b:");
					for (float hy = 0.05f; hy < 1.8f; hy += 0.15f)
					{
						RaycastHit rh;
						rays.Append(" " + hy.ToString("F2") + (Physics.Raycast(fa + Vector3.up * hy, fd.normalized, out rh, fd.magnitude, reachMask, QueryTriggerInteraction.Ignore) ? "=" + BlockerName(rh.collider) + "@" + rh.distance.ToString("F2") : "=-"));
					}
					Log(rays.ToString());
					Log("  grid: blocked .45 " + Blocked(a, b, Mathf.Max(a.y, b.y) + 0.45f) + " 1.0 " + Blocked(a, b, Mathf.Max(a.y, b.y) + 1.0f) + " 1.55 " + Blocked(a, b, Mathf.Max(a.y, b.y) + 1.55f) + " swept " + Swept(a, b, Mathf.Max(a.y, b.y)));
					// (what rules a spot in or out of the grid at each end: a floor stands, a swimmer's 0.4 m sphere is free, not inside a rock)
					foreach (Vector3 q in new[] { a, b })
						Log("  grid at " + (q - e.Position).ToString("F1") + ": stands " + Stands(q) + ", inside solid " + InsideSolid(q + Vector3.up * 0.5f) + ", swim sphere free " + !Physics.CheckSphere(q + Vector3.up * 0.9f, 0.4f, reachMask, QueryTriggerInteraction.Ignore));
					{ string why; bool sw = SlopeWalk(a, b, out why); Log("  grid: slope walk " + sw + " (" + why + ")"); }
				}
				// (a and b are feet, as the walker's reports give them; Raft's player transform sits 0.85 m over the feet)
				PlayerMove.To(pl, a + Vector3.up * 0.95f);
				ReachFree(pl);
				for (int f = 0; f < 15; f++) { MmHold(pl, false); yield return null; }
				foreach (Collider c in Physics.OverlapSphere(ReachFeet(pl) + Vector3.up * 0.3f, 1.2f, ~0, QueryTriggerInteraction.Ignore))
					if (!c.transform.IsChildOf(pl.transform)) Log("  near: " + BlockerName(c) + " layer " + LayerMask.LayerToName(c.gameObject.layer) + " " + c.GetType().Name + " bounds " + (c.bounds.min - e.Position).ToString("F2") + " to " + (c.bounds.max - e.Position).ToString("F2") + (reachMask != 0 && (reachMask & (1 << c.gameObject.layer)) == 0 ? " (not in the grid's mask)" : ""));
				float next = 0f, still = 0f; bool jumped = false; Vector3 last = ReachFeet(pl);
				Vector3 dir = b - a; dir.y = 0f;
				for (float t = 0f; t < 8f; t += Time.deltaTime)
				{
					Vector3 d = b - ReachFeet(pl); d.y = 0f;
					if (d.magnitude < 0.3f && Mathf.Abs(b.y - ReachFeet(pl).y) < 0.9f) { Log("  arrived at " + t.ToString("F2") + " s"); break; }
					Vector3 go = d.magnitude > 0.3f ? d : dir;
					Look(pl, Mathf.Atan2(go.x, go.z) * Mathf.Rad2Deg, 0f);
					mmWalk = 1f;
					if ((ReachFeet(pl) - last).magnitude < 0.01f) still += Time.deltaTime; else still = 0f;
					last = ReachFeet(pl);
					mmJump = still > 1f && !jumped; if (mmJump) { jumped = true; Log("  jump"); }
					MmHold(pl, false);
					yield return null;
					if (t >= next)
					{
						next += 0.25f;
						CharacterController cc = pl.GetComponent<CharacterController>();
						RaycastHit gh; string under = Physics.Raycast(ReachFeet(pl) + Vector3.up * 0.5f, Vector3.down, out gh, 3f, ~0, QueryTriggerInteraction.Ignore) ? BlockerName(gh.collider) + " at y " + (gh.point.y - e.Position.y).ToString("F2") : "nothing";
						Log("  t " + t.ToString("F2") + " at " + (ReachFeet(pl) - e.Position).ToString("F2") + " " + pl.PersonController.controllerType + " grounded " + (cc != null && cc.isGrounded) + " vel " + (cc != null ? cc.velocity.ToString("F1") : "-") + " under " + under + " ahead " + ReachBlocker(pl, go.normalized));
					}
				}
			}
			finally { ReachInputOff(); }
			Log("PASS: reach probe");
		}

		[ConsoleCommand(name: "CIReachOn", docs: "Dev, anywhere: PQ11 - every play (CIPlay) now measures each island it leaves with the reach-and-escape grid (CIReach), in the state the quest left it. CIReachOn [label]")]
		public static void ReachOnCommand(string[] args)
		{
			reachHook = true;
			reachLabel = args != null && args.Length > 0 ? string.Join(" ", args) : "";
			Log("PASS: reach hook on" + (reachLabel.Length > 0 ? " (" + reachLabel + ")" : ""));
		}

		[ConsoleCommand(name: "CIReachOff", docs: "Dev, anywhere: PQ11 - plays no longer measure islands (CIReachOn)")]
		public static void ReachOffCommand(string[] args) { reachHook = false; Log("PASS: reach hook off"); }

		[ConsoleCommand(name: "CIReach", docs: "Dev, world (host, a test world 'CI ...'): PQ11 - brings a saved island into the world and measures, with Raft's colliders, whether every quest target (note, chest, zone, used object, islander) can be got to from the open sea on four sides and left again to the sea; lists ladders that can't be taken hold of or lead nowhere and places a player gets into but not out of. CIReach <island>")]
		public static void ReachCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Fail("CIReach <island>"); return; }
			StartTest(ReachRoutine(string.Join(" ", args)));
		}

		static IEnumerator ReachRoutine(string island)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CIReach: host, in a world"); yield break; }
			yield return EnsureAlive();
			var made = new List<IslandWorldState.Entry>();
			var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
			if (left.Count > 0) yield return RemoveIslandsAndWait(left);
			Vector3? spot = ScSpot(island, 400f, IslandSpawner.ElevationOf(island));
			if (!spot.HasValue) { Fail("CIReach: no open sea for '" + island + "'"); yield break; }
			yield return ScBring(island, spot.Value, made);
			IslandWorldState.Entry e = made.LastOrDefault();
			if (e == null || e.Root == null) { Fail("CIReach: '" + island + "' didn't come"); yield break; }
			yield return WaitIslandSettled(e, 0.5f, 3f);
			// (the player next to it, so it stays loaded and what is near the player is all there)
			PlayerMove.To(RAPI.GetLocalPlayer(), e.Position + new Vector3(0f, 2f, -LandRadiusOf(e) - 20f));
			yield return new WaitForSeconds(1f);
			int fails = 0;
			yield return ReachAnalyze(e, ReachPlayOffset(island), "fresh", n => fails = n);
			ScRemove(made);
			OnRaftCommand();
			if (fails == 0) Log("PASS: reach " + island); else Fail("reach " + island + ": " + fails + " problem(s)");
		}

		static float LandRadiusOf(IslandWorldState.Entry e)
		{
			Bounds b;
			return ReachBounds(e.Root, out b) ? Mathf.Max(b.extents.x, b.extents.z) : 40f;
		}

		/// <summary>The offset line of the island's own play test (its recipe's coordinates), or zero.</summary>
		static Vector2 ReachPlayOffset(string island)
		{
			try
			{
				foreach (string f in Directory.GetFiles(RecipeFolder, "*.play"))
				{
					if (f.EndsWith("_pics.play") || Path.GetFileName(f).StartsWith("plan_")) continue;
					string[] lines = File.ReadAllLines(f);
					string head = lines.FirstOrDefault(l => l.StartsWith("island "));
					if (head == null || !string.Equals(head.Substring(7).Replace(" keep", "").Trim(), island, StringComparison.OrdinalIgnoreCase)) continue;
					string off = lines.FirstOrDefault(l => l.StartsWith("offset "));
					if (off == null) return Vector2.zero;
					string[] t = off.Split(' ');
					return new Vector2(F(t[1]), F(t[2]));
				}
			}
			catch { }
			return Vector2.zero;
		}

		static bool ReachBounds(GameObject root, out Bounds b)
		{
			b = new Bounds();
			bool any = false;
			foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
			{
				if (c.isTrigger || !c.enabled) continue;
				if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
			}
			return any;
		}

		// ---------------------------------------------------------------- the grid

		static int dbgShoreHigh, dbgShoreLow, dbgShoreBlocked, dbgShoreOk; static int dbgOutBlocked, dbgOutFall, dbgOutOk;
		const float RSea = 0f, RStepUp = 0.55f, RJump = 1.1f, RDrop = 12f, RSwimFeet = -1.3f, RUse = 2.5f, RRadius = 0.3f, RHeight = 1.75f;

		class ReachTarget
		{
			public string Kind, Name, Steps = "";
			public Transform Root;
			public Bounds B;
			public bool Zone;
			public List<Collider> Colliders = new List<Collider>();
		}

		class ReachGrid
		{
			public float Cell, X0, Z0;
			public Vector3 Anchor;
			public Dictionary<int, Bounds> LadderB = new Dictionary<int, Bounds>();
			public Dictionary<int, Vector3> LadderF = new Dictionary<int, Vector3>();
			// (a used object's teleport (a lift, a hatch down): from a spot a player uses it from to the spot it puts the player on)
			public HashSet<long> Tele = new HashSet<long>();
			public static long TeleKey(int a, int b) { return ((long)a << 32) | (uint)b; }
			public int NX, NZ;
			public List<Vector3> Pos = new List<Vector3>();
			// 0 standing (feet), 1 the sea's surface (swimming), 2 under water (a 1 m cell's middle), 3 a ladder
			public List<byte> Kind = new List<byte>();
			public List<List<int>> Out = new List<List<int>>();
			public List<List<int>> In = new List<List<int>>();
			public Dictionary<int, List<int>> Col = new Dictionary<int, List<int>>();
			public Dictionary<long, int> Vox = new Dictionary<long, int>();
			public int Add(Vector3 p, byte k, int col)
			{
				int i = Pos.Count;
				Pos.Add(p); Kind.Add(k); Out.Add(None); In.Add(None);
				if (col >= 0) { List<int> l; if (!Col.TryGetValue(col, out l)) Col[col] = l = new List<int>(); l.Add(i); }
				return i;
			}
			// (a spot's lists are made on its first link: millions of spots on a big island, PQ11 10-27 RAM)
			public static readonly List<int> None = new List<int>(0);
			public void Link(int a, int b) { if (a == b || Out[a].Contains(b)) return; if (Out[a] == None) Out[a] = new List<int>(4); if (In[b] == None) In[b] = new List<int>(4); Out[a].Add(b); In[b].Add(a); }
			public List<int> At(int i, int j) { List<int> l; return i >= 0 && j >= 0 && i < NX && j < NZ && Col.TryGetValue(i * NZ + j, out l) ? l : null; }
		}

		static int reachMask;
		static bool reachSky, reachWalkIn;
		// (the grid, the targets' boxes and the walks follow a world shift: a walk far off shifts the world (0 0 1000) and the
		// spots made before it stood ~1000 m off the island after it (box runs, Colossus body PQ11 10-28))
		static ReachGrid shiftGrid;
		static List<ReachTarget> shiftTargets;
				static void ReachFollowShift(Vector3 s)
		{
			ReachGrid g = shiftGrid;
			if (g == null) return;
			for (int i = 0; i < g.Pos.Count; i++) g.Pos[i] -= s;
			g.X0 -= s.x; g.Z0 -= s.z; g.Anchor -= s;
			foreach (int k in g.LadderB.Keys.ToList()) { Bounds b = g.LadderB[k]; b.center -= s; g.LadderB[k] = b; }
			if (shiftTargets != null) foreach (ReachTarget t in shiftTargets) t.B.center -= s;
			Log("[REACH] world shift " + s + " followed");
		}

		/// <summary>The reach-and-escape measure of one island in the world, as it stands now. done(n): problems found.</summary>
		static IEnumerator ReachAnalyze(IslandWorldState.Entry e, Vector2 offset, string state, Action<int> done)
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			string name = e.HostName;
			var report = new List<string>();
			int problems = 0;
			reachStuckAt.Clear(); reachStuckN.Clear(); reachBlocked = null;
			hbWhat = "reach " + name; // (from the start: the grid takes a minute, and the HB keeps the player alive while it is "reach")
			// (a player opens a plain door with E: Raft's own doors (Door) with no use check of the island's on them are opened
			// first - every drowned house's back room failed behind its closed Block_Wall_Door, PQ11 10-28; a gate or key door
			// (an if./else./beh. prop, a ready-made key door) stays as the play left it)
			{
				int opened = 0, stuck = 0;
				foreach (Door dr in e.Root.GetComponentsInChildren<Door>(false))
				{
					if (dr.open) continue;
					IslandObjectRef ir = dr.GetComponentInParent<IslandObjectRef>();
					if (ir != null && (KeycardDoor.Is(ir.ObjectName) || ir.Props.Keys.Any(k => k.StartsWith("if.") || k.StartsWith("else.") || k.StartsWith("beh.")))) continue;
					dr.Open();
					if (dr.open) opened++; else stuck++;
				}
				if (opened + stuck > 0) report.Add("REACH " + name + " | doors | - | NOTE " + opened + " plain door(s) opened as a player would" + (stuck > 0 ? ", " + stuck + " would not open (not placed: a player can't open them either)" : ""));
				if (opened > 0) yield return new WaitForSeconds(1.5f);
			}
			// (an object a player uses that hides itself (on.use ... hide| with no name, or its own name): Biome Seven's airlock
			// hatch - a player uses it and it is gone; the grid saw it as a lid on the airlock, PQ11 10-28. A gated one (if./else.)
			// stays: only the play's own steps open it)
			{
				int gone = 0;
				foreach (IslandObjectRef ir in e.Root.GetComponentsInChildren<IslandObjectRef>(false))
				{
					string onUse;
					if (!ir.Props.TryGetValue("on.use", out onUse) || onUse.IndexOf("hide", StringComparison.Ordinal) < 0) continue;
					if (ir.Props.Keys.Any(k => k.StartsWith("if.") || k.StartsWith("else."))) continue;
					string own = ir.Name ?? "";
					bool self = ObjAction.ParseLines(onUse.Replace("\\n", "\n")).Any(a => a.Verb == "hide"
						&& (a.Target.Length == 0 || a.Target.Equals("self", StringComparison.OrdinalIgnoreCase) || (own.Length > 0 && a.Target == own)));
					if (!self) continue;
					ir.gameObject.SetActive(false);
					gone++;
				}
				if (gone > 0) report.Add("REACH " + name + " | used-away | - | NOTE " + gone + " object(s) a player uses away (a hatch that hides itself) taken away as a player would");
			}
			Physics.SyncTransforms();
			// (what the grid looks through: the players and the animals - they move about)
			int mask = ~0;
			var skipLayers = new HashSet<int>();
			foreach (Network_Player p in UnityEngine.Object.FindObjectsOfType<Network_Player>())
				foreach (Collider c in p.GetComponentsInChildren<Collider>(true)) skipLayers.Add(c.gameObject.layer);
			foreach (AI_NetworkBehaviour a in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>())
				foreach (Collider c in a.GetComponentsInChildren<Collider>(true)) skipLayers.Add(c.gameObject.layer);
			// (never the island's own layers)
			var islandLayers = new HashSet<int>(e.Root.GetComponentsInChildren<Collider>(false).Where(c => !c.isTrigger && c.GetComponentInParent<AI_NetworkBehaviour>() == null).Select(c => c.gameObject.layer));
			foreach (int l in skipLayers) if (!islandLayers.Contains(l)) mask &= ~(1 << l);
			// (nor a layer Raft's physics never lets touch the player: it can't stand in a player's way - an air pocket's bubble
			// blocked the door of every drowned house in the grid, PQ11 10-27)
			{
				Network_Player me = ComponentManager<Network_Player>.Value;
				CharacterController pcc = me != null ? me.GetComponentInChildren<CharacterController>() : null;
				if (pcc != null)
					for (int l = 0; l < 32; l++)
						if (Physics.GetIgnoreLayerCollision(pcc.gameObject.layer, l)) mask &= ~(1 << l);
				foreach (Collider c in e.Root.GetComponentsInChildren<Collider>(false).Where(c => !c.isTrigger && c.name.IndexOf("AirPocket", StringComparison.OrdinalIgnoreCase) >= 0 || (c.transform.parent != null && c.transform.root.name.IndexOf("AirPocket", StringComparison.OrdinalIgnoreCase) >= 0)).Take(1))
					report.Add("REACH " + name + " | air pocket collider '" + c.name + "' | - | NOTE layer " + LayerMask.LayerToName(c.gameObject.layer) + ", trigger " + c.isTrigger + ", player ignores it " + (pcc != null && (Physics.GetIgnoreLayerCollision(pcc.gameObject.layer, c.gameObject.layer) || Physics.GetIgnoreCollision(pcc, c))));
			}
			reachMask = mask;

			Bounds ib;
			if (!ReachBounds(e.Root, out ib)) { Log("REACH " + name + " | - | - | FAIL no colliders"); done(1); yield break; }
			var g = new ReachGrid { Anchor = e.Position };
			// (Raft drops the event's handlers with each world: hooked once, the next world's shift went unfollowed, PQ11 10-28)
			Action<Vector3> follow = ReachFollowShift;
			if (WorldShiftManager.OnWorldShift == null || Array.IndexOf(WorldShiftManager.OnWorldShift.GetInvocationList(), follow) < 0) WorldShiftManager.OnWorldShift += follow;
			shiftGrid = g; shiftTargets = null;
			float margin = 10f;
			float w = ib.size.x + 2f * margin, d = ib.size.z + 2f * margin;
			g.Cell = Mathf.Max(0.5f, Mathf.Sqrt(w * d / 300000f));
			g.X0 = ib.min.x - margin; g.Z0 = ib.min.z - margin;
			g.NX = Mathf.CeilToInt(w / g.Cell); g.NZ = Mathf.CeilToInt(d / g.Cell);
			float top = ib.max.y + 3f, bottom = Mathf.Max(ib.min.y - 1f, RSea - 80f);
			var underCols = new HashSet<int>();
			var lowest = new Dictionary<int, float>();

			// columns: every floor down the column with room to stand; the sea's surface where a swimmer fits
			int work = 0;
			// (yield by time, not by count: a frame every 3000 cells left most of each frame idle - Rootweave Caves' grid took 54 s, PQ11 10-28)
			var slice = System.Diagnostics.Stopwatch.StartNew();
			for (int i = 0; i < g.NX; i++)
			{
				for (int j = 0; j < g.NZ; j++)
				{
					float x = g.X0 + (i + 0.5f) * g.Cell, z = g.Z0 + (j + 0.5f) * g.Cell;
					Vector3 from = new Vector3(x, top, z);
					RaycastHit h;
					int hits = 0;
					float low = float.MaxValue;
					bool landAbove = false;
					while (hits < 40 && from.y > bottom && Physics.Raycast(from, Vector3.down, out h, from.y - bottom, mask, QueryTriggerInteraction.Ignore))
					{
						hits++;
						low = h.point.y;
						// (a terrain is a surface with nothing under it: where it stands over the swim depth there is no sea below -
						// a capsule under it overlaps nothing, so without this the grid swam under the hills, PQ11 10-27)
						if (h.collider is TerrainCollider && h.point.y > RSea - 1.1f) landAbove = true;
						// (a floor buried under the terrain or inside a rock is no floor: a ray carries on under the skin, PQ11 10-27)
						if (h.normal.y >= 0.64f && Stands(h.point) && !InsideSolid(h.point + Vector3.up * 0.5f)) g.Add(h.point, 0, i * g.NZ + j);
						from = h.point + Vector3.down * 0.03f;
					}
					if (hits > 0) lowest[i * g.NZ + j] = low;
					if (low < RSea - 1.6f && hits > 0) underCols.Add(i * g.NZ + j);
					if (!landAbove && !InsideSolid(new Vector3(x, RSea - 1.1f, z)) && !Physics.CheckCapsule(new Vector3(x, RSea - 1.1f, z), new Vector3(x, RSea + 0.3f, z), RRadius, mask, QueryTriggerInteraction.Ignore))
						g.Add(new Vector3(x, RSea, z), 1, i * g.NZ + j);
					if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { yield return null; slice.Reset(); slice.Start(); }
				}
			}
			// under water: 1 m cells with room for a swimmer, where something of the island lies below the surface
			float vc = 1f;
			int vstep = Mathf.Max(1, Mathf.RoundToInt(vc / g.Cell));
			for (int i = 0; i < g.NX; i += vstep)
			{
				for (int j = 0; j < g.NZ; j += vstep)
				{
					float deep = float.MaxValue;
					for (int a = 0; a < vstep; a++) for (int c = 0; c < vstep; c++)
					{
						float lo;
						if (underCols.Contains((i + a) * g.NZ + j + c) && lowest.TryGetValue((i + a) * g.NZ + j + c, out lo)) deep = Mathf.Min(deep, lo);
					}
					if (deep == float.MaxValue) continue;
					float x = g.X0 + (i + vstep * 0.5f) * g.Cell, z = g.Z0 + (j + vstep * 0.5f) * g.Cell;
					for (int k = 0; ; k++)
					{
						float y = RSea - 1.5f - k * vc;
						if (y < deep - 0.5f) break;
						Vector3 p = new Vector3(x, y, z);
						if (!Physics.CheckSphere(p, 0.4f, mask, QueryTriggerInteraction.Ignore) && !InsideSolid(p))
							g.Vox[VoxKey(i / vstep, j / vstep, k)] = g.Add(p, 2, -1);
						if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { yield return null; slice.Reset(); slice.Start(); }
					}
				}
			}
			Log("  reach grid '" + name + "': " + g.NX + "x" + g.NZ + " cells of " + g.Cell.ToString("F2") + " m, " + g.Pos.Count + " spots (" + sw.ElapsedMilliseconds + " ms)");

			// moves between neighbouring columns
			int[] di = { 1, 0, 1, 1 }, dj = { 0, 1, 1, -1 };
			for (int i = 0; i < g.NX; i++)
			{
				for (int j = 0; j < g.NZ; j++)
				{
					List<int> here = g.At(i, j);
					if (here == null) continue;
					// in the same column: a swimmer onto a shallow floor, a wader into the sea
					foreach (int a in here) foreach (int b in here) if (a != b) TryMove(g, a, b, true);
					for (int n = 0; n < 4; n++)
					{
						List<int> there = g.At(i + di[n], j + dj[n]);
						if (there == null) continue;
						foreach (int a in here) foreach (int b in there) { TryMove(g, a, b, false); TryMove(g, b, a, false); }
					}
					if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { yield return null; slice.Reset(); slice.Start(); }
				}
			}
			// swimming under water: cell to cell, up to the surface, onto floors under water
			foreach (KeyValuePair<long, int> kv in g.Vox)
			{
				int vi, vj, vk;
				VoxSplit(kv.Key, out vi, out vj, out vk);
				int other;
				Vector3 p = g.Pos[kv.Value];
				// (not through a thin floor, roof or wall between two free cells: the swimmer's 0.4 m spheres miss a block's
				// 0.2 m slab half way between cells 1 m apart - Hadal Commons' houses were swum into through their roofs, PQ11 10-28)
				if (g.Vox.TryGetValue(VoxKey(vi + 1, vj, vk), out other) && !Between(p, g.Pos[other], mask)) { g.Link(kv.Value, other); g.Link(other, kv.Value); }
				if (g.Vox.TryGetValue(VoxKey(vi, vj + 1, vk), out other) && !Between(p, g.Pos[other], mask)) { g.Link(kv.Value, other); g.Link(other, kv.Value); }
				if (g.Vox.TryGetValue(VoxKey(vi, vj, vk + 1), out other) && !Between(p, g.Pos[other], mask)) { g.Link(kv.Value, other); g.Link(other, kv.Value); }
				for (int a = 0; a < vstep; a++) for (int c = 0; c < vstep; c++)
				{
					List<int> col = g.At(vi * vstep + a, vj * vstep + c);
					if (col == null) continue;
					foreach (int f in col)
					{
						Vector3 q = g.Pos[f];
						if (g.Kind[f] == 1 && vk == 0 && !Between(p, q, mask)) { g.Link(kv.Value, f); g.Link(f, kv.Value); }
						else if (g.Kind[f] == 0 && q.y < RSea - 0.5f && Mathf.Abs(q.y + 0.9f - p.y) <= 1.1f && !Between(p, q + Vector3.up * 0.9f, mask)) { g.Link(kv.Value, f); g.Link(f, kv.Value); }
					}
				}
				if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { yield return null; slice.Reset(); slice.Start(); }
			}

			Log("  reach grid '" + name + "': moves linked (" + sw.ElapsedMilliseconds + " ms)");
			// ladders: a climb collider (stacked ones as one), from spots at its foot that can take hold of it to spots at its top
			var ladders = e.Root.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && c.name.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			var chains = new List<List<Collider>>();
			foreach (Collider c in ladders)
			{
				Bounds cb = c.bounds; cb.Expand(new Vector3(0.2f, 0.6f, 0.2f));
				List<List<Collider>> touching = chains.Where(ch => ch.Any(o => { Bounds ob = o.bounds; ob.Expand(new Vector3(0.2f, 0.6f, 0.2f)); return ob.Intersects(cb); })).ToList();
				var merged = new List<Collider> { c };
				foreach (var t in touching) { merged.AddRange(t); chains.Remove(t); }
				chains.Add(merged);
			}
			foreach (List<Collider> ch in chains)
			{
				Bounds lb = ch[0].bounds;
				foreach (Collider c in ch) lb.Encapsulate(c.bounds);
				int node = g.Add(lb.center, 3, -1);
				g.LadderB[node] = lb;
				{
					// (the side a player climbs it from: its front, or its back when only the back has room)
					Vector3 lf = LadderRoot(ch[0]).forward; lf.y = 0f;
					if (lf.sqrMagnitude > 0.01f) { lf.Normalize(); g.LadderF[node] = LadderRoom(lb, lf) || !LadderRoom(lb, -lf) ? lf : -lf; }
				}
				int grabs = 0, tops = 0;
				for (int k = 0; k < node; k++)
				{
					Vector3 p = g.Pos[k];
					Vector3 flat = lb.ClosestPoint(new Vector3(p.x, lb.center.y, p.z));
					float hd = new Vector2(flat.x - p.x, flat.z - p.z).magnitude;
					if (hd > 1.6f) continue;
					float feet = g.Kind[k] == 2 ? p.y - 0.9f : p.y;
					// (the top: a floor at the ladder's top beside it - where the climb ends)
					// (or over its top end: a ladder up through a hole in a landing - the line to its middle crossed the landing, PQ11 10-27)
					if (g.Kind[k] == 0 && feet >= lb.max.y - 1.0f && feet <= lb.max.y + 1.3f && (LadderClear(p + Vector3.up * 1.0f, flat, ch) || LadderClear(p + Vector3.up * 1.0f, new Vector3(flat.x, lb.max.y + 0.6f, flat.z), ch))) { g.Link(node, k); g.Link(k, node); tops++; continue; }
					// (the foot, and any spot beside it up its length: a hand reaches it with nothing solid in between)
					if (hd <= 1.0f && feet >= lb.min.y - 1.8f && feet <= lb.max.y - 1.0f)
					{
						Vector3 eye = g.Kind[k] == 0 ? p + Vector3.up * 1.3f : p + Vector3.up * 0.2f;
						Vector3 at = lb.ClosestPoint(new Vector3(eye.x, Mathf.Clamp(eye.y, lb.min.y + 0.2f, lb.max.y - 0.2f), eye.z));
						// (and to its middle: a spot behind the wall it is fixed to saw the climb box's back face poke through the wall
						// and counted as a foot spot - the walker stood outside the mast pressing on the wall, hw_mast PQ11 10-28)
						if (LadderClear(eye, at, ch) && LadderClear(eye, new Vector3(lb.center.x, at.y, lb.center.z) + (g.LadderF.ContainsKey(node) ? g.LadderF[node] * 0.25f : Vector3.zero), ch)) { g.Link(k, node); g.Link(node, k); grabs++; }
					}
				}
				Vector3 lr = Rel(e, lb.center, offset);
				string where = "ladder " + LadderName(ch[0], e) + " at " + lr.x.ToString("F1") + " " + lr.z.ToString("F1") + " h=" + (lb.min.y - e.Position.y).ToString("F1") + ".." + (lb.max.y - e.Position.y).ToString("F1");
				string facing = LadderFacing(ch, lb);
				if (grabs == 0 || tops == 0)
				{
					problems++;
					report.Add("REACH " + name + " | " + where + " | - | FAIL " + (grabs == 0 ? "nothing can take hold of it from below" : "") + (grabs == 0 && tops == 0 ? ", " : "") + (tops == 0 ? "no floor to step off onto at its top" : "") + " (" + facing + ")");
				}
				else report.Add("REACH " + name + " | " + where + " | - | PASS " + grabs + " foot spots, " + tops + " top spots (" + facing + ")");
			}
			yield return null;

			// (an object a player uses that teleports them (on.use ... teleport|<name>): Colossus's lift up the leg, a hatch down
			// into a vault - the grid can't walk it, and the whole colossus above the lift failed, PQ11 10-28. Every spot the
			// object is used from links to the standing spot where the player lands. Use checks (a key) are taken as met, as
			// the play left the island; the walker uses the object at that step, as a player would)
			{
				int links = 0, objs = 0;
				foreach (IslandObjectRef r in e.Root.GetComponentsInChildren<IslandObjectRef>(false))
				{
					string onUse;
					if (!r.Props.TryGetValue("on.use", out onUse) || onUse.IndexOf("teleport", StringComparison.Ordinal) < 0) continue;
					ObjAction ta = ObjAction.ParseLines(onUse).FirstOrDefault(a => a.Verb == "teleport");
					if (ta == null) continue;
					IslandObjectRef to = Behaviours.TargetsOf(e, r.Index, ta.Target).FirstOrDefault(x => x.gameObject.activeInHierarchy);
					if (to == null) continue;
					Vector3 land = to.transform.position;
					int dst = -1; float best = 9f;
					foreach (int k in NearNodes(g, new Bounds(land, Vector3.one), 2.5f))
					{
						if (g.Kind[k] != 0) continue;
						float dl = (g.Pos[k] - land).sqrMagnitude;
						if (dl < best) { best = dl; dst = k; }
					}
					if (dst < 0) { report.Add("REACH " + name + " | teleport '" + (r.Name.Length > 0 ? r.Name : r.ObjectName) + "' -> " + ta.Target + " | - | NOTE no standing spot within 3 m of where it puts the player"); continue; }
					var ut = new ReachTarget { Kind = "use", Root = r.transform };
					Renderer[] rs = r.GetComponentsInChildren<Renderer>(false).Where(x => (x.bounds.center - r.transform.position).sqrMagnitude < 900f).ToArray();
					ut.B = new Bounds(r.transform.position, Vector3.one * 0.3f);
					foreach (Renderer x in rs) ut.B.Encapsulate(x.bounds);
					ut.Colliders = r.GetComponentsInChildren<Collider>(false).ToList();
					int n0 = links;
					foreach (int k in NearNodes(g, ut.B, RUse + 0.6f))
						if (k != dst && g.Kind[k] != 3 && CanUseFrom(g, k, ut)) { g.Link(k, dst); g.Tele.Add(ReachGrid.TeleKey(k, dst)); links++; }
					if (links > n0) objs++;
				}
				if (objs > 0) report.Add("REACH " + name + " | teleports | - | NOTE " + objs + " used object(s) that teleport the player (a lift, a hatch) linked: " + links + " use spots");
			}
			// where the open sea is: the grid's edge
			var sea = new List<int>();
			for (int k = 0; k < g.Pos.Count; k++)
			{
				// (and a standing spot in the shallows at the grid's edge, at the sea's surface or under it: a raft lands there too -
				// PQ11 10-29 diagnosis for the big remade islands)
				if (g.Kind[k] != 1 && !(g.Kind[k] == 0 && g.Pos[k].y < RSea + 0.3f)) continue;
				int ci = Mathf.FloorToInt((g.Pos[k].x - g.X0) / g.Cell), cj = Mathf.FloorToInt((g.Pos[k].z - g.Z0) / g.Cell);
				if (ci <= 1 || cj <= 1 || ci >= g.NX - 2 || cj >= g.NZ - 2) sea.Add(k);
			}
			// (a flying island (higher than a climb out of the sea): players build stairs or pillars up from the raft to its edge
			// (GUIDE "Flying islands") - the arrivals and the ways back are the rim: a standing spot under the open sky with
			// a drop of 5 m or more (or nothing at all) beside it. PQ11 10-27)
			reachSky = e.Position.y > 6f;
			if (reachSky)
			{
				sea.Clear();
				for (int k = 0; k < g.Pos.Count; k++)
				{
					if (g.Kind[k] != 0) continue;
					Vector3 p = g.Pos[k];
					if (Physics.Raycast(p + Vector3.up * 1.7f, Vector3.up, 60f, mask, QueryTriggerInteraction.Ignore)) continue;
					int ci = Mathf.FloorToInt((p.x - g.X0) / g.Cell), cj = Mathf.FloorToInt((p.z - g.Z0) / g.Cell);
					bool edge = false;
					for (int dd = 0; dd < 4 && !edge; dd++)
					{
						List<int> col = g.At(ci + (dd == 0 ? 1 : dd == 1 ? -1 : 0), cj + (dd == 2 ? 1 : dd == 3 ? -1 : 0));
						edge = col == null || !col.Any(o => g.Kind[o] == 0 && g.Pos[o].y > p.y - 5f);
					}
					if (edge) sea.Add(k);
				}
				report.Add("REACH " + name + " | flying island (" + e.Position.y.ToString("F0") + " m up) | - | NOTE arrivals and ways out: " + sea.Count + " rim spots (stairs built up from the raft), not the sea");
			}
			if (sea.Count == 0) { report.Add("REACH " + name + " | - | - | FAIL no open sea round it on the grid"); problems++; }
			// the four arrival sides: the open sea nearest the middle of each edge
			string[] sideNames = { "north", "east", "south", "west" };
			Vector3 mid = ib.center;
			Vector3[] edgeMid = { new Vector3(mid.x, 0f, g.Z0 + g.NZ * g.Cell), new Vector3(g.X0 + g.NX * g.Cell, 0f, mid.z), new Vector3(mid.x, 0f, g.Z0), new Vector3(g.X0, 0f, mid.z) };
			var fwd = new bool[4][];
			var parent = new int[4][];
			for (int s = 0; s < 4; s++)
			{
				int start = sea.Count > 0 ? sea.OrderBy(k => ScFlat(g.Pos[k], edgeMid[s])).First() : -1;
				fwd[s] = new bool[g.Pos.Count];
				parent[s] = new int[g.Pos.Count];
				if (start >= 0) Flood(g, new List<int> { start }, g.Out, fwd[s], parent[s]);
				// (a flying island: the rim spot nearest a side can be a roof or a post top with a drop all round - stairs from the
				// raft go to the island's edge, not onto a lamp post: Altitude Market's north/south starts reached nothing, PQ11 10-27.
				// Take the nearest rim spot whose walk-in reaches at least a quarter of the island's floor)
				if (reachSky && start >= 0)
				{
					int floor = 0; for (int k = 0; k < g.Pos.Count; k++) if (g.Kind[k] == 0) floor++;
					// (every rim spot already covered by a tried walk-in is skipped, and the best walk-in tried is kept: the last
					// one tried was kept before, and Altitude Market's north start, 25 roof tops, reached nothing - 22 false
					// "north: can't get there" with the target's spot reached from the other sides, PQ11 10-28)
					List<int> cands = sea.OrderBy(k => ScFlat(g.Pos[k], edgeMid[s])).ToList();
					var rimDone = new bool[g.Pos.Count];
					bool[] bestF = fwd[s]; int[] bestP = parent[s]; int bestGot = -1;
					for (int c = 0, tried = 0; c < cands.Count && tried < 80; c++)
					{
						int got = 0; for (int k = 0; k < g.Pos.Count; k++) if (fwd[s][k]) { rimDone[k] = true; if (g.Kind[k] == 0) got++; }
						if (got > bestGot) { bestGot = got; bestF = fwd[s]; bestP = parent[s]; }
						if (got * 4 >= floor) break;
						if (c == 0) continue;
						if (rimDone[cands[c]]) continue;
						tried++;
						fwd[s] = new bool[g.Pos.Count]; parent[s] = new int[g.Pos.Count];
						Flood(g, new List<int> { cands[c] }, g.Out, fwd[s], parent[s]);
					}
					fwd[s] = bestF; parent[s] = bestP;
				}
			}
			// (a flying island: stairs from the raft can come up at any part of the rim, so a side whose 80 rim tries all found
			// roofs or posts takes the best side's walk-in - Pendulum Isle's north reached nothing, 22 false "north: can't get
			// there" with every spot reached from the other sides, PQ11 10-28)
			// (and a sea island: the raft sails round to a side it can land on - Primeval Park's north start lay off a cliff and
			// reached nothing, 45 false "north: can't get there" with every spot reached 0.0 m off from the other sides, PQ11 10-29)
			{
				int[] gotS = new int[4]; int bs = 0;
				for (int s = 0; s < 4; s++) { for (int k = 0; k < g.Pos.Count; k++) if (fwd[s][k] && g.Kind[k] == 0) gotS[s]++; if (gotS[s] > gotS[bs]) bs = s; }
				for (int s = 0; s < 4; s++) if (gotS[s] * 4 < gotS[bs]) { fwd[s] = fwd[bs]; parent[s] = parent[bs]; }
				int seaSwim = sea.Count(k => g.Kind[k] == 1);
				report.Add("REACH " + name + " | shore links | - | NOTE out of the water: ok " + dbgShoreOk + ", too high " + dbgShoreHigh + ", too low " + dbgShoreLow + ", blocked " + dbgShoreBlocked);
				report.Add("REACH " + name + " | shore links | - | NOTE into the water: ok " + dbgOutOk + ", blocked " + dbgOutBlocked + ", fall caught " + dbgOutFall);
				dbgShoreOk = dbgShoreHigh = dbgShoreLow = dbgShoreBlocked = 0;
				dbgOutOk = dbgOutBlocked = dbgOutFall = 0;
				report.Add("REACH " + name + " | walk-in | - | NOTE sea " + sea.Count + " spots (" + seaSwim + " swim), grid " + g.NX + "x" + g.NZ + "; floor reached per side n/e/s/w " + string.Join("/", gotS.Select(x => x.ToString()).ToArray()));
			}
			var back = new bool[g.Pos.Count];
			var backParent = new int[g.Pos.Count];
			Flood(g, sea, g.In, back, backParent);
			yield return null;

			// the targets: what the quests point at, and everything a player uses
			List<ReachTarget> targets = ReachTargets(e);
			shiftTargets = targets;
			ReachStepNames(e, targets, report, ref problems);
			int hbT = 0;
			// (every target's use spots and place first, while the grid, the targets and the world agree: a walk that takes the
			// player far off sets off a world shift (0 0 1000), and every target after it came out ~1000 m off with "no spot to
			// use it from" - the grid's spots and the targets' bounds are from before the shift, the colliders the rays hit after it.
			// Gannet Cliff / Foundation Piles PQ11 10-28)
			var useOf = new Dictionary<ReachTarget, List<int>>();
			var relOf = new Dictionary<ReachTarget, Vector3>();
			foreach (ReachTarget t in targets)
			{
				var u = new List<int>();
				foreach (int k in NearNodes(g, t.B, 3.2f))
					if (CanUseFrom(g, k, t)) u.Add(k);
				useOf[t] = u;
				relOf[t] = Rel(e, t.B.center, offset);
			}
			foreach (ReachTarget t in targets)
			{
				hbWhat = "reach " + name; hbStep = hbT++; hbProg++;
				var use = useOf[t];
				Vector3 tr = relOf[t];
				string head = "REACH " + name + " | " + t.Kind + " '" + t.Name + "' at " + tr.x.ToString("F1") + " " + tr.z.ToString("F1") + " h=" + tr.y.ToString("F1") + " | " + (t.Steps.Length > 0 ? t.Steps : "-") + " | ";
				// (a cage the play opened is hidden (Survivors.cs: open = gone for everyone), its model off and its bounds a 0.3 m box
				// at its root - "no spot" for Teo's and Oda's cages after their quests freed them, PQ11 10-28: not checkable now)
				if (use.Count == 0 && t.Kind == "cage")
				{
					SurvivorCage sc = t.Root.GetComponentInChildren<SurvivorCage>(false);
					if (sc != null && sc.IsOpen) { report.Add(head + "NOTE opened in the play (the cage is gone now): not checked"); continue; }
				}
				if (use.Count == 0)
				{
					problems++;
					report.Add(head + "FAIL no spot to use it from (" + WhyNoSpot(t) + ")");
					continue;
				}
				var bad = new List<string>();
				for (int s = 0; s < 4; s++)
				{
					if (use.Any(k => fwd[s][k] && back[k])) continue;
					if (use.Any(k => fwd[s][k])) bad.Add(sideNames[s] + ": got there, no way back out to the sea");
					else bad.Add(sideNames[s] + ": can't get there");
				}
				if (bad.Count == 0)
				{
					int k0 = use.First(k => fwd[0][k] && back[k]);
					string grid = use.Count + " spots; from the north " + RouteText(g, parent[0], k0);
					// open ground (a floor under the sky, low, nothing but walking and swimming on the way, from 2+ sides): the grid
					// alone; all else is walked by the player, in from the sea and out again
					if (OpenTarget(g, use, fwd, back, parent)) { reachPrechecked++; report.Add(head + "PASS (precheck) (" + grid + ")"); continue; }
					string walked = null;
					if (reachSpeed > 0 && reachBlocked != null) walked = "FAIL not walked: " + reachBlocked + " - the island's walks ended there";
					else if (reachSpeed > 0) yield return ReachWalkTarget(e, g, t, use, fwd, back, parent, backParent, sideNames, r => walked = r);
					if (walked == null) report.Add(head + "PASS grid only, not walked (" + grid + ")");
					else if (walked.StartsWith("PASS")) report.Add(head + walked + " (" + grid + ")");
					else { problems++; report.Add(head + walked + " (the grid found a way: " + grid + ")"); }
				}
				else
				{
					problems++;
					// (where the nearest reachable spots are, to fix it: the spot of the use list nearest to anything reached)
					report.Add(head + "FAIL " + string.Join("; ", bad.Distinct().ToArray()) + NearestReached(e, g, fwd, use, offset));
				}
			}
			if (reachControls && reachSpeed > 0) yield return ReachControls(e, g, fwd, report, n => problems += n);
			// places a player gets into but not out of (from any side)
			var trapped = new bool[g.Pos.Count];
			for (int k = 0; k < g.Pos.Count; k++) trapped[k] = !back[k] && (fwd[0][k] || fwd[1][k] || fwd[2][k] || fwd[3][k]) && g.Kind[k] != 3;
			var seen = new bool[g.Pos.Count];
			int traps = 0;
			for (int k = 0; k < g.Pos.Count; k++)
			{
				if (!trapped[k] || seen[k]) continue;
				var cluster = new List<int>();
				var q = new Queue<int>(); q.Enqueue(k); seen[k] = true;
				while (q.Count > 0)
				{
					int a = q.Dequeue(); cluster.Add(a);
					foreach (int b in g.Out[a].Concat(g.In[a])) if (trapped[b] && !seen[b]) { seen[b] = true; q.Enqueue(b); }
				}
				if (cluster.Count < 6) continue;
				traps++;
				problems++;
				Bounds cb = new Bounds(g.Pos[cluster[0]], Vector3.zero);
				foreach (int a in cluster) cb.Encapsulate(g.Pos[a]);
				Vector3 cr = Rel(e, cb.center, offset);
				bool roofed = Physics.Raycast(cb.center + Vector3.up * 2f, Vector3.up, 60f, mask, QueryTriggerInteraction.Ignore);
				report.Add("REACH " + name + " | trap at " + cr.x.ToString("F1") + " " + cr.z.ToString("F1") + " h=" + (cb.min.y - e.Position.y).ToString("F1") + ".." + (cb.max.y - e.Position.y).ToString("F1") +
					" (" + cb.size.x.ToString("F0") + "x" + cb.size.z.ToString("F0") + " m, " + cluster.Count + " spots" + (roofed ? ", roofed" : ", open sky") + (cluster.Any(a => g.Kind[a] == 2) ? ", under water" : "") + ") | - | FAIL a player gets in but not out" + TrapNearestOut(e, g, cluster, back));
			}
			int pass = report.Count(r => r.Contains("| PASS"));
			report.Add("REACH " + name + " | summary | " + state + " | " + (problems == 0 ? "PASS" : "FAIL") + " " + targets.Count + " targets, " + chains.Count + " ladders, " + traps + " traps, " + problems + " problems, " + pass + " pass (" + (sw.ElapsedMilliseconds / 1000f).ToString("F1") + " s)");
			foreach (string r in report) Log(r);
			try
			{
				string dir = Path.Combine(DynamicIslands.assetpath, "reach");
				Directory.CreateDirectory(dir);
				string file = Path.Combine(dir, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".txt");
				File.WriteAllLines(file, new[] { "# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " " + state + (reachLabel.Length > 0 ? " " + reachLabel : "") + " offset " + offset.x.ToString("F2", CultureInfo.InvariantCulture) + " " + offset.y.ToString("F2", CultureInfo.InvariantCulture) }.Concat(report).ToArray());
			}
			catch (Exception ex) { Log("  (reach file: " + ex.Message + ")"); }
			// (the grid freed before the next island: Raft's heap is reused, not grown)
			g.Pos = null; g.Kind = null; g.Out = null; g.In = null; g.Col = null; g.Vox = null; fwd = null; back = null; parent = null; backParent = null; trapped = null; seen = null;
			GC.Collect();
			shiftGrid = null; shiftTargets = null;
			done(problems);
		}

		/// <summary>The closest a trap comes to a spot with a way back out (raw feet, island-relative: CIReachProbe's frame).</summary>
		static string TrapNearestOut(IslandWorldState.Entry e, ReachGrid g, List<int> cluster, bool[] back)
		{
			float best = 3f; int bt = -1, bo = -1;
			foreach (int t in cluster)
			{
				if (g.Kind[t] > 1) continue;
				int ci = Mathf.FloorToInt((g.Pos[t].x - g.X0) / g.Cell), cj = Mathf.FloorToInt((g.Pos[t].z - g.Z0) / g.Cell);
				for (int di = -3; di <= 3; di++) for (int dj = -3; dj <= 3; dj++)
				{
					List<int> col = g.At(ci + di, cj + dj);
					if (col == null) continue;
					foreach (int o in col)
					{
						if (!back[o] || g.Kind[o] > 1) continue;
						float d = (g.Pos[o] - g.Pos[t]).magnitude;
						if (d < best) { best = d; bt = t; bo = o; }
					}
				}
			}
			if (bt < 0) return " (no spot with a way out within 3 m)";
			Vector3 a = e.Position + (g.Pos[bt] - g.Anchor) - e.Position, b = e.Position + (g.Pos[bo] - g.Anchor) - e.Position;
			return " (closest way out: " + a.x.ToString("F1", CultureInfo.InvariantCulture) + " " + a.y.ToString("F1", CultureInfo.InvariantCulture) + " " + a.z.ToString("F1", CultureInfo.InvariantCulture) + " -> " +
				b.x.ToString("F1", CultureInfo.InvariantCulture) + " " + b.y.ToString("F1", CultureInfo.InvariantCulture) + " " + b.z.ToString("F1", CultureInfo.InvariantCulture) + ", kinds " + g.Kind[bt] + "->" + g.Kind[bo] + ")";
		}

		static long VoxKey(int i, int j, int k) { return ((long)(i + 20000) << 40) | ((long)(j + 20000) << 20) | (long)k; }
		static void VoxSplit(long key, out int i, out int j, out int k) { i = (int)(key >> 40) - 20000; j = (int)((key >> 20) & 0xFFFFF) - 20000; k = (int)(key & 0xFFFFF); }

		/// <summary>Room for the player standing with the feet here.</summary>
		static bool Stands(Vector3 feet)
		{
			Vector3 top = feet + Vector3.up * (RHeight - RRadius), low = feet + Vector3.up * (RRadius + 0.08f);
			if (!Physics.CheckCapsule(low, top, RRadius, reachMask, QueryTriggerInteraction.Ignore)) return true;
			// (on a slope over ~38 deg the 0.6 m wide capsule's foot touches the rising terrain itself - Raft's controller stands
			// and walks there (a 40 deg trail pad edge, PQ11 10-27): the terrain alone may touch it under 0.4 m, nothing else)
			if (Physics.CheckCapsule(feet + Vector3.up * (0.4f + RRadius), top, RRadius, reachMask, QueryTriggerInteraction.Ignore)) return false;
			foreach (Collider c in Physics.OverlapCapsule(low, top, RRadius, reachMask, QueryTriggerInteraction.Ignore))
				if (!(c is TerrainCollider)) return false;
			return true;
		}

		/// <summary>The player's body (bar the bottom 0.45 m, which steps over) swept from a to b at floor height y:
		/// lines down the middle miss a bunk bed's corner or a stair's side that stops the 0.6 m wide player.</summary>
		static bool Swept(Vector3 a, Vector3 b, float y)
		{
			const float r = 0.2f;
			Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z);
			if (d.magnitude < 0.01f) return false;
			Vector3 p1 = new Vector3(a.x, y + 0.45f + r, a.z), p2 = new Vector3(a.x, y + RHeight - r, a.z);
			Vector3 q1 = new Vector3(b.x, y + 0.45f + r, b.z), q2 = new Vector3(b.x, y + RHeight - r, b.z);
			return Physics.CapsuleCast(p1, p2, r, d.normalized, d.magnitude, reachMask, QueryTriggerInteraction.Ignore) ||
				Physics.CapsuleCast(q1, q2, r, -d.normalized, d.magnitude, reachMask, QueryTriggerInteraction.Ignore);
		}

		/// <summary>A point inside a rock's mesh: a ray up from it meets the inside of a surface that faces up (a mesh
		/// collider is a skin - a capsule wholly inside overlaps nothing; the grid swam inside Turtle Cave's rock, PQ11 10-27).
		/// An overhang's underside faces down and is no hit.</summary>
		static bool InsideSolid(Vector3 p)
		{
			// (under a terrain: a heightfield has no inside to hit - unless p is in a cave of the island's own pieces under it, a
			// ceiling facing down over p and a floor facing up under it, both below the terrain: Temperance tunnels under a reef
			// (Drowned Labyrinth: every tunnel spot was ruled out, 'no spot' for all its targets, PQ11 10-28))
			foreach (Terrain t in Terrain.activeTerrains)
			{
				Vector3 tp = t.transform.position, ts = t.terrainData.size;
				if (p.x >= tp.x && p.x <= tp.x + ts.x && p.z >= tp.z && p.z <= tp.z + ts.z && tp.y + t.SampleHeight(p) > p.y)
					return !CaveUnder(p, tp.y + t.SampleHeight(p));
			}
			bool was = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				RaycastHit h;
				return Physics.Raycast(p, Vector3.up, out h, 40f, reachMask, QueryTriggerInteraction.Ignore) && h.normal.y > 0.1f;
			}
			finally { Physics.queriesHitBackfaces = was; }
		}

		/// <summary>p (under a terrain whose surface is at ground) lies in a hollow of the island's own pieces: the first thing over it
		/// is a ceiling facing down below the ground and the first thing under it a floor facing up - a tunnel, not the earth.
		/// (Front faces only: from inside a rock or a sunk boulder's skin a ray meets no face, so solid ground stays solid.)</summary>
		static bool CaveUnder(Vector3 p, float ground)
		{
			RaycastHit up, down;
			if (!Physics.Raycast(p, Vector3.up, out up, ground - p.y, reachMask, QueryTriggerInteraction.Ignore)) return false;
			if (up.collider is TerrainCollider || up.normal.y > -0.3f) return false;
			if (!Physics.Raycast(p, Vector3.down, out down, 30f, reachMask, QueryTriggerInteraction.Ignore)) return false;
			return !(down.collider is TerrainCollider) && down.normal.y > 0.3f;
		}

		static bool Blocked(Vector3 a, Vector3 b, float y)
		{
			return Physics.Linecast(new Vector3(a.x, y, a.z), new Vector3(b.x, y, b.z), reachMask, QueryTriggerInteraction.Ignore) ||
				Physics.Linecast(new Vector3(b.x, y, b.z), new Vector3(a.x, y, a.z), reachMask, QueryTriggerInteraction.Ignore);
		}

		/// <summary>A move from spot a to spot b (a neighbouring or the same column) as Raft's controller makes it.</summary>
		static void TryMove(ReachGrid g, int a, int b, bool same)
		{
			byte ka = g.Kind[a], kb = g.Kind[b];
			if (ka >= 2 || kb >= 2) return;
			Vector3 pa = g.Pos[a], pb = g.Pos[b];
			if (ka == 0 && kb == 0)
			{
				if (same) return;
				float dy = pb.y - pa.y;
				// (a floor under water: swum to, any height, if the way is free)
				bool wet = pa.y < RSea - 1.6f && pb.y < RSea - 1.6f;
				if (!wet && (dy > RJump || dy < -RDrop)) return;
				// (no jump with the feet in the sea: jumps there rose 0.2-0.4 m)
				if (!wet && dy > RStepUp && pa.y < RSea - 0.3f) return;
				if (wet && Mathf.Abs(dy) > 2f) return;
				float hi = Mathf.Max(pa.y, pb.y);
				bool flat = !(Blocked(pa, pb, hi + 0.45f) || Blocked(pa, pb, hi + 1.0f) || Blocked(pa, pb, hi + 1.55f) || Swept(pa, pb, hi));
				// (rising ground: the fixed-height rays above hit the slope itself - Raft walked a 1.2 m trail pad step
				// in 0.9 s that they called blocked, PQ11 10-27; follow the ground instead)
				bool slope = !flat && !wet && SlopeWalk(pa, pb);
				if (!flat && !slope) return;
				// (a jump: room over the head as the player rises)
				if (flat && dy > RStepUp && !wet && Physics.CheckCapsule(pa + Vector3.up * (dy + RRadius + 0.1f), pa + Vector3.up * (dy + RHeight - RRadius), RRadius, reachMask, QueryTriggerInteraction.Ignore)) return;
				// (a drop: nothing to land on on the way down - a floor that reaches over the spot below catches the player)
				if (dy < -0.9f && !wet && FallCaught(pb, pa.y, pb.y)) return;
				g.Link(a, b);
				return;
			}
			if (ka == 1 && kb == 1)
			{
				if (same) return;
				if (!Blocked(pa, pb, RSea + 0.3f) && !Blocked(pa, pb, RSea - 0.6f)) g.Link(a, b);
				return;
			}
			if (ka == 1 && kb == 0)
			{
				// out of the water: a step up from where the swimmer's feet are (the sea floor, or about 1.3 m under the
				// surface out of depth) - a beach or a slope, not a lip: at the shore boulder a ledge level with the sea stood
				// 1.1 m over the sea floor and stopped the walker (jumps in the water rise 0.1-0.3 m)
				RaycastHit bh;
				float bed = Physics.Raycast(new Vector3(pa.x, RSea - 0.1f, pa.z), Vector3.down, out bh, 3f, reachMask, QueryTriggerInteraction.Ignore) ? bh.point.y : RSea - 3f;
				// (or up a slope out of the water, as on land: Tangaroa's shores rose 0.6-1 m from one spot to the next all
				// round, every one called a lip - 2 ways in for the whole island, PQ11 10-29)
				if (pb.y > Mathf.Max(bed, RSea + RSwimFeet) + RStepUp && (pb.y > RSea + 0.6f || bed < RSea - 2.9f || !SlopeWalk(new Vector3(pa.x, bed, pa.z), pb))) { dbgShoreHigh++; return; }
				if (pb.y < RSea - 1.7f) { dbgShoreLow++; return; }
				if (!same && (Blocked(pa, pb, Mathf.Max(pb.y, RSea) + 0.45f) || Blocked(pa, pb, Mathf.Max(pb.y, RSea) + 1.2f))) { dbgShoreBlocked++; return; }
				dbgShoreOk++;
				g.Link(a, b);
				return;
			}
			if (ka == 0 && kb == 1)
			{
				// into the water: wading out, or off a ledge of any height
				if (pa.y < RSea - 1.7f) return;
				// (the sea under the same spot's floor: not where a player standing on that floor drops to)
				if (same && pa.y > RSea + 0.6f) return;
				if (!same && (Blocked(pa, pb, Mathf.Max(pa.y, RSea) + 0.45f) || Blocked(pa, pb, Mathf.Max(pa.y, RSea) + 1.2f) || Swept(pa, pb, Mathf.Max(pa.y, RSea))))
				{
					// (or down a slope into the water, as on land - the way in above, back out)
					RaycastHit oh;
					if (pa.y > RSea + 0.6f || !Physics.Raycast(new Vector3(pb.x, RSea - 0.1f, pb.z), Vector3.down, out oh, 2.8f, reachMask, QueryTriggerInteraction.Ignore) || !SlopeWalk(pa, oh.point)) { dbgOutBlocked++; return; }
				}
				// (a slope under the drop only slides the player on into the sea: Tangaroa's shore ledges, 2736 drops 'caught' by the
				// rock face below them, no way off the island, PQ11 10-29)
				if (!same && pa.y > RSea + 0.9f && FallCaught(pb, pa.y, RSea, true)) { dbgOutFall++; return; }
				dbgOutOk++;
				g.Link(a, b);
			}
		}

		/// <summary>Walking from a to b over the ground between them: every 0.25 m the ground is found under the last
		/// one (at most 0.5 m up per sample, a step Raft's controller takes, and a step down), is walkable (not steeper than 60 deg where it rises: Raft walked a 48 deg pad edge), the body
		/// (bar the bottom 0.45 m) has room over it, and nothing stands between two samples. It ends at b's floor.</summary>
		static bool SlopeWalk(Vector3 a, Vector3 b) { string why; return SlopeWalk(a, b, out why); }

		static bool SlopeWalk(Vector3 a, Vector3 b, out string why)
		{
			const float r = 0.2f, stepLen = 0.25f;
			why = "";
			Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z);
			float len = d.magnitude;
			if (len < 0.01f) { why = "same spot"; return false; }
			int n = Mathf.Max(1, Mathf.CeilToInt(len / stepLen));
			Vector3 prev = a;
			for (int i = 1; i <= n; i++)
			{
				float t = (float)i / n;
				Vector3 c = new Vector3(a.x + d.x * t, prev.y, a.z + d.z * t);
				RaycastHit h;
				if (!Physics.Raycast(c + Vector3.up * RStepUp, Vector3.down, out h, RStepUp * 2f, reachMask, QueryTriggerInteraction.Ignore)) { why = "no ground at sample " + i; return false; }
				Vector3 g = h.point;
				float rise = g.y - prev.y;
				if (rise > 0.5f || (rise > 0.05f && h.normal.y < 0.5f)) { why = "sample " + i + " rise " + rise.ToString("0.00") + " normal y " + h.normal.y.ToString("0.00"); return false; }
				// (the body over the bottom step's height: a terrain cell's edge rises like a step just ahead and the controller
				// steps up it - a capsule from 0.45 m touched the trail's next 0.5 m rise)
				Collider[] hit = Physics.OverlapCapsule(g + Vector3.up * (RStepUp + r), g + Vector3.up * (RHeight - r), r, reachMask, QueryTriggerInteraction.Ignore);
				if (hit.Length > 0) { why = "no room for the body at sample " + i + " (" + hit[0].name + ")"; return false; }
				float y0 = Mathf.Max(prev.y, g.y);
				if (Blocked(prev, g, y0 + RStepUp) || Blocked(prev, g, y0 + 1.0f) || Blocked(prev, g, y0 + 1.55f)) { why = "blocked before sample " + i; return false; }
				prev = g;
			}
			why = "ends " + (prev.y - b.y).ToString("0.00") + " m from b's floor";
			return Mathf.Abs(prev.y - b.y) < 0.3f;
		}

		/// <summary>Something in column at between the heights from (over a floor) and to that a falling player lands on.</summary>
		static bool FallCaught(Vector3 at, float from, float to, bool floorOnly = false)
		{
			const float r = 0.2f;
			float dist = from + 0.6f - to - r - 0.25f;
			RaycastHit h;
			// (back faces too: a cave's skin faces inwards, so from above its roof is a back face - Biome Seven's overflow cave:
			// the grid dropped through the seamount's skin into the cave, the walk stood on it, PQ11 10-28)
			bool was = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try { return dist > 0f && Physics.SphereCast(new Vector3(at.x, from + 0.6f, at.z), r, Vector3.down, out h, dist, reachMask, QueryTriggerInteraction.Ignore) && (!floorOnly || h.normal.y > 0.7f); }
			finally { Physics.queriesHitBackfaces = was; }
		}

		static void Flood(ReachGrid g, List<int> starts, List<List<int>> edges, bool[] seen, int[] parent)
		{
			var q = new Queue<int>();
			for (int i = 0; i < parent.Length; i++) parent[i] = -1;
			foreach (int s in starts) { if (s < 0 || seen[s]) continue; seen[s] = true; q.Enqueue(s); }
			while (q.Count > 0)
			{
				int a = q.Dequeue();
				foreach (int b in edges[a]) if (!seen[b]) { seen[b] = true; parent[b] = a; q.Enqueue(b); }
			}
		}

		static bool LadderClear(Vector3 from, Vector3 to, List<Collider> ladder)
		{
			Vector3 d = to - from;
			if (d.magnitude < 0.05f) return true;
			foreach (RaycastHit h in Physics.RaycastAll(from, d.normalized, d.magnitude, reachMask, QueryTriggerInteraction.Ignore))
			{
				if (ladder.Any(c => h.collider.transform.IsChildOf(c.transform.root == c.transform ? c.transform : LadderRoot(c)))) continue;
				return false;
			}
			return true;
		}

		static Transform LadderRoot(Collider c)
		{
			IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>();
			return r != null ? r.transform : c.transform.parent ?? c.transform;
		}

		static string LadderName(Collider c, IslandWorldState.Entry e)
		{
			IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>();
			return r != null ? r.ObjectName + (r.Name.Length > 0 ? " '" + r.Name + "'" : "") : c.name;
		}

		/// <summary>Which of the ladder's two faces has room for a climber, half way up: its object's forward side or its back.</summary>
		static string LadderFacing(List<Collider> ch, Bounds lb)
		{
			Transform t = LadderRoot(ch[0]);
			Vector3 f = t.forward; f.y = 0f;
			if (f.sqrMagnitude < 0.01f) return "?";
			f.Normalize();
			bool front = LadderRoom(lb, f), backside = LadderRoom(lb, -f);
			return "room at its front " + (front ? "yes" : "no") + ", at its back " + (backside ? "yes" : "no") + ", yaw " + t.eulerAngles.y.ToString("F0");
		}

		static bool LadderRoom(Bounds lb, Vector3 dir)
		{
			for (float y = lb.min.y + 0.8f; y < lb.max.y - 0.4f; y += 1f)
			{
				Vector3 c = new Vector3(lb.center.x, y, lb.center.z) + dir * 0.75f;
				if (Physics.CheckSphere(c, 0.28f, reachMask, QueryTriggerInteraction.Ignore)) return false;
			}
			return true;
		}

		static List<int> NearNodes(ReachGrid g, Bounds b, float r)
		{
			var list = new List<int>();
			int i0 = Mathf.FloorToInt((b.min.x - r - g.X0) / g.Cell), i1 = Mathf.FloorToInt((b.max.x + r - g.X0) / g.Cell);
			int j0 = Mathf.FloorToInt((b.min.z - r - g.Z0) / g.Cell), j1 = Mathf.FloorToInt((b.max.z + r - g.Z0) / g.Cell);
			for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) { List<int> c = g.At(i, j); if (c != null) list.AddRange(c); }
			for (int k = 0; k < g.Pos.Count; k++)
				if (g.Kind[k] == 2) { Vector3 p = g.Pos[k]; if (p.x >= b.min.x - r && p.x <= b.max.x + r && p.z >= b.min.z - r && p.z <= b.max.z + r && p.y >= b.min.y - r && p.y <= b.max.y + r) list.Add(k); }
			return list;
		}

		static Vector3 EyeOf(ReachGrid g, int k)
		{
			Vector3 p = g.Pos[k];
			return g.Kind[k] == 0 ? p + Vector3.up * 1.6f : g.Kind[k] == 1 ? p + Vector3.up * 0.3f : p;
		}

		/// <summary>From this spot a player uses the target: in a zone (the body inside it); anything else within Raft's use reach of
		/// the eye with nothing solid in between.</summary>
		static bool CanUseFrom(ReachGrid g, int k, ReachTarget t)
		{
			if (g.Kind[k] == 3) return false;
			Vector3 p = g.Pos[k];
			if (t.Zone)
			{
				Vector3 feet = g.Kind[k] == 0 ? p : p - Vector3.up * 0.9f;
				return (feet - t.B.center).magnitude <= t.B.extents.x - 1f;
			}
			Vector3 eye = EyeOf(g, k);
			foreach (Vector3 aim in new[] { t.B.center, t.B.ClosestPoint(eye), t.B.center + Vector3.up * t.B.extents.y * 0.6f })
			{
				Vector3 d = aim - eye;
				if (d.magnitude > RUse + 0.6f) continue;
				foreach (RaycastHit h in Physics.RaycastAll(eye, d.normalized, RUse, UseMask(t), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
				{
					if (h.collider.transform.IsChildOf(t.Root)) return true;
					if (h.collider.isTrigger) continue;
					break;
				}
			}
			return false;
		}

		/// <summary>The grid's mask plus the target's own layers: a note's or an islander's collider can be on a layer the
		/// players' colliders use too (left out of the grid), and Raft's use ray still finds it.</summary>
		static int UseMask(ReachTarget t)
		{
			int m = reachMask;
			foreach (Collider c in t.Colliders) if (c != null) m |= 1 << c.gameObject.layer;
			return m;
		}

		static string WhyNoSpot(ReachTarget t)
		{
			foreach (Collider c in Physics.OverlapSphere(t.B.center, 0.05f, reachMask, QueryTriggerInteraction.Ignore))
				if (!c.transform.IsChildOf(t.Root)) return "its middle is inside " + c.name + (c.GetComponentInParent<IslandObjectRef>() != null ? " (" + c.GetComponentInParent<IslandObjectRef>().ObjectName + ")" : "") +
						" (it: y " + t.B.min.y.ToString("F2") + "-" + t.B.max.y.ToString("F2") + "; that collider: y " + c.bounds.min.y.ToString("F2") + "-" + c.bounds.max.y.ToString("F2") + ", " + c.GetType().Name + ")";
			if (!t.Zone && t.Colliders.Count == 0) return "it has no collider to aim at";
			return "nowhere to stand or swim within " + RUse + " m with a clear line to it";
		}

		static string RouteText(ReachGrid g, int[] parent, int k)
		{
			int swims = 0, dives = 0, ladders = 0, jumps = 0, n = 0;
			for (int a = k; a >= 0 && parent[a] >= 0 && n < 100000; a = parent[a], n++)
			{
				int b = parent[a];
				if (g.Kind[a] == 1) swims++;
				if (g.Kind[a] == 2) dives++;
				if (g.Kind[a] == 3) ladders++;
				if (g.Kind[a] == 0 && g.Kind[b] == 0 && g.Pos[a].y - g.Pos[b].y > RStepUp) jumps++;
			}
			return n + " moves, " + jumps + " jumps, " + ladders + " ladders, " + dives + " m dived";
		}

		static string NearestReached(IslandWorldState.Entry e, ReachGrid g, bool[][] fwd, List<int> use, Vector2 offset)
		{
			int best = -1; float bd = float.MaxValue;
			Vector3 c = g.Pos[use[0]];
			for (int k = 0; k < g.Pos.Count; k++)
			{
				if (!(fwd[0][k] || fwd[1][k] || fwd[2][k] || fwd[3][k]) || g.Kind[k] != 0) continue;
				float dd = (g.Pos[k] - c).sqrMagnitude;
				if (dd < bd) { bd = dd; best = k; }
			}
			if (best < 0) return "";
			Vector3 r = Rel(e, g.Pos[best], offset);
			Vector3 u = Rel(e, c, offset);
			return " [a use spot at " + u.x.ToString("F1") + " " + u.z.ToString("F1") + " h=" + u.y.ToString("F1") + "; nearest reached " + r.x.ToString("F1") + " " + r.z.ToString("F1") + " h=" + r.y.ToString("F1") + ", " + Mathf.Sqrt(bd).ToString("F1") + " m off]";
		}

		/// <summary>A world point in the island's recipe coordinates (x, z) and height above its entry (y).</summary>
		static Vector3 Rel(IslandWorldState.Entry e, Vector3 p, Vector2 offset)
		{
			Vector3 r = p - e.Position;
			return new Vector3(r.x + offset.x, r.y, r.z + offset.y);
		}

		/// <summary>A collider's world box, from its shape when it is switched off (Unity gives a switched-off one no bounds).</summary>
		static Bounds ShapeBounds(Collider c)
		{
			if (c.enabled && c.gameObject.activeInHierarchy) return c.bounds;
			Transform t = c.transform;
			Vector3 sc = t.lossyScale;
			var box = c as BoxCollider;
			if (box != null) return new Bounds(t.TransformPoint(box.center), Vector3.Scale(box.size, new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z))));
			var sph = c as SphereCollider;
			if (sph != null) return new Bounds(t.TransformPoint(sph.center), Vector3.one * 2f * sph.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z)));
			var cap = c as CapsuleCollider;
			if (cap != null) { float r = cap.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z)); return new Bounds(t.TransformPoint(cap.center), new Vector3(2f * r, Mathf.Max(cap.height * Mathf.Abs(sc.y), 2f * r), 2f * r)); }
			return new Bounds(t.position, Vector3.one);
		}

		static List<ReachTarget> ReachTargets(IslandWorldState.Entry e)
		{
			var list = new List<ReachTarget>();
			Func<Transform, Bounds> boundsOf = tr =>
			{
				// (renderers far off the object left out: some notes on Biome Seven and Foundation Piles carry one ~2 km away,
				// and the target's middle came out ~1000 m off - "no spot" for every one of them, PQ11 10-28)
				Renderer[] rs = tr.GetComponentsInChildren<Renderer>(false).Where(r => (r.bounds.center - tr.position).sqrMagnitude < 900f).ToArray();
				Bounds b = new Bounds(tr.position, Vector3.one * 0.3f);
				if (rs.Length > 0) { b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds); }
				return b;
			};
			// (with the inactive ones: a zone the quest has passed may be switched off by now)
			foreach (TriggerZone z in e.Root.GetComponentsInChildren<TriggerZone>(true))
			{
				// (a zone has no collider: the player's feet within Radius of its centre set it off - TriggerZone.Update)
				if (string.IsNullOrEmpty(z.Id)) continue;
				list.Add(new ReachTarget { Kind = "zone", Name = z.Id, Root = z.transform, B = new Bounds(z.transform.position, Vector3.one * 2f * z.Radius), Zone = true });
			}
			var done = new HashSet<Transform>();
			foreach (LootCrate l in e.Root.GetComponentsInChildren<LootCrate>(false))
			{
				IslandObjectRef r = l.GetComponentInParent<IslandObjectRef>();
				Transform root = r != null ? r.transform : l.transform;
				if (!done.Add(root)) continue;
				string title = r != null ? ObjectProps.Get(r.Props, ObjectProps.NoteTitle) : "";
				list.Add(new ReachTarget { Kind = "chest", Name = title.Length > 0 ? title : (r != null ? r.ObjectName : l.name), Root = root, B = boundsOf(root) });
			}
			foreach (CustomNote n in e.Root.GetComponentsInChildren<CustomNote>(false))
			{
				if (n.GetComponent<LootCrate>() != null || n.GetComponentInParent<LootCrate>() != null) continue;
				IslandObjectRef r = n.GetComponentInParent<IslandObjectRef>();
				Transform root = r != null ? r.transform : n.transform;
				if (!done.Add(root)) continue;
				// (some notes come out ~1000 m off their island after a world shift, PQ11 10-28: the chain up to the island says which part stayed behind)
				if ((root.position - e.Position).sqrMagnitude > 250000f)
				{
					string chain = "";
					for (Transform t = root; t != null && t != e.Root.transform.parent; t = t.parent)
						chain += " < " + t.name + " " + (t.position - e.Position).ToString("F0") + " [" + string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name).ToArray()) + "]";
					Log("  far note '" + n.Title + "':" + chain + " | shifted by " + IslandWorldState.ShiftedBy.ToString("F0"));
				}
				list.Add(new ReachTarget { Kind = "note", Name = n.Title ?? "", Root = root, B = boundsOf(root) });
			}
			foreach (IslandObjectRef r in e.Root.GetComponentsInChildren<IslandObjectRef>(false))
			{
				if (done.Contains(r.transform)) continue;
				string kind = null;
				if (r.GetComponentInChildren<Islander>(false) != null) kind = "islander";
				else if (r.GetComponentInChildren<SurvivorCage>(false) != null) kind = "cage";
				else if (r.GetComponentInChildren<CodeLock>(false) != null) kind = "keypad";
				else if (Collectibles.ClaimKey(r) >= 0) kind = "collectible";
				else if (Behaviours.AnyOf(e, r.Index, "use")) kind = "use";
				if (kind == null) continue;
				done.Add(r.transform);
				list.Add(new ReachTarget { Kind = kind, Name = r.Name.Length > 0 ? r.Name : r.ObjectName, Root = r.transform, B = boundsOf(r.transform) });
			}
			foreach (ReachTarget t in list) t.Colliders = t.Root.GetComponentsInChildren<Collider>(false).ToList();
			return list;
		}

		/// <summary>Each quest step's target marked on the targets ("q1.3 read"); steps whose target isn't there are listed.</summary>
		static void ReachStepNames(IslandWorldState.Entry e, List<ReachTarget> targets, List<string> report, ref int problems)
		{
			IDictionary<string, string> props = IslandCache.PropsOf(e);
			int quests = IslandQuest.CountIn(props);
			for (int q = 0; q < Mathf.Max(quests, 1); q++)
			{
				IslandQuest quest = IslandQuest.From(props, q, true);
				for (int s = 0; s < quest.Steps.Count; s++)
				{
					IslandQuest.Step st = quest.Steps[s];
					string label = "q" + (q + 1) + "." + (s + 1) + " " + st.Type;
					string tg = st.Target ?? "";
					List<ReachTarget> hit = new List<ReachTarget>();
					switch (st.Type)
					{
						case "reach": hit = targets.Where(t => t.Zone && string.Equals(t.Name, tg, StringComparison.OrdinalIgnoreCase)).ToList(); break;
						// (a note on a chest is listed as the chest, under the note's title - Canopy Stair's pilot's locker, PQ11 10-28)
						case "read": hit = targets.Where(t => (t.Kind == "note" || t.Kind == "chest") && t.Name.IndexOf(tg, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); break;
						case "open": hit = targets.Where(t => t.Kind == "chest" && (tg.Length == 0 || string.Equals(t.Name, tg, StringComparison.OrdinalIgnoreCase))).ToList(); break;
						case "talk": hit = targets.Where(t => t.Kind == "islander" && string.Equals(t.Name, tg, StringComparison.OrdinalIgnoreCase)).ToList(); if (hit.Count == 0) hit = targets.Where(t => t.Kind == "islander").ToList(); break;
						case "collect":
						case "blueprint":
							hit = targets.Where(t => t.Kind == "chest" && ChestHolds(t, tg)).ToList();
							if (hit.Count == 0) hit = targets.Where(t => t.Kind != "chest" && t.Kind != "zone" && ObjectHolds(t, tg)).ToList();
							break;
						default: continue; // kill, catch, pages, set, lights: not one place
					}
					if (hit.Count == 0)
					{
						// (an item a step wants may come from a creature, a dig, or a behaviour's give - not a fault of reach)
						report.Add("REACH " + e.HostName + " | step target '" + tg + "' | " + label + " | " + (st.Type == "collect" || st.Type == "blueprint" ? "NOTE not in a chest or object found now (a behaviour, creature or dig gives it)" : "FAIL not found on the island now"));
						if (st.Type != "collect" && st.Type != "blueprint") problems++;
						continue;
					}
					foreach (ReachTarget t in hit) t.Steps += (t.Steps.Length > 0 ? ", " : "") + label;
				}
			}
		}

		static bool ChestHolds(ReachTarget t, string target)
		{
			IslandObjectRef r = t.Root.GetComponent<IslandObjectRef>();
			if (r == null) return false;
			string loot = ObjectProps.Get(r.Props, ObjectProps.LootItems);
			return target.Length > 0 && loot.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static bool ObjectHolds(ReachTarget t, string target)
		{
			IslandObjectRef r = t.Root.GetComponent<IslandObjectRef>();
			return r != null && target.Length > 0 && r.Props.Values.Any(v => v != null && v.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0);
		}
	
		// ---------------------------------------------------------------- the walker

		static List<int> PathIn(int[] parent, int k)
		{
			var p = new List<int>();
			for (int a = k; a >= 0 && p.Count < 200000; a = parent[a]) p.Add(a);
			p.Reverse();
			return p;
		}

		static List<int> PathOut(int[] backParent, int k)
		{
			var p = new List<int>();
			for (int a = k; a >= 0 && p.Count < 200000; a = backParent[a]) p.Add(a);
			return p;
		}

		/// <summary>A target in the open: a spot to use it from on a floor under the sky, at most 3 m above the sea, got to and
		/// left by walking and swimming on the surface only (no ladder, no diving), from two sides or more.</summary>
		static bool OpenTarget(ReachGrid g, List<int> use, bool[][] fwd, bool[] back, int[][] parent)
		{
			foreach (int k in use)
			{
				if (g.Kind[k] != 0 || g.Pos[k].y > RSea + 3f || !back[k]) continue;
				if (Physics.Raycast(g.Pos[k] + Vector3.up * 1.7f, Vector3.up, 60f, reachMask, QueryTriggerInteraction.Ignore)) continue;
				int sides = 0;
				for (int s = 0; s < 4; s++)
					if (fwd[s][k] && PathIn(parent[s], k).All(a => g.Kind[a] <= 1)) sides++;
				if (sides >= 2) return true;
			}
			return false;
		}

		/// <summary>The player walks to the target from the sea (the side with the shortest way), uses it from where the walk
		/// ended, and walks back out into the sea. Any FAIL, or a walk that crossed a collider, is walked again at 1x.</summary>
		static IEnumerator ReachWalkTarget(IslandWorldState.Entry e, ReachGrid g, ReachTarget t, List<int> use, bool[][] fwd, bool[] back,
			int[][] parent, int[] backParent, string[] sideNames, Action<string> result)
		{
			Network_Player pl = RAPI.GetLocalPlayer();
			if (pl == null) { result(null); yield break; }
			int bestS = -1, bestK = -1, bestLen = int.MaxValue;
			for (int s = 0; s < 4; s++)
				foreach (int k in use)
				{
					if (!fwd[s][k] || !back[k]) continue;
					List<int> pi = PathIn(parent[s], k);
					int len = pi.Count + PathOut(backParent, k).Count;
					// (a way past a spot a walk already stuck at comes last: another sea side, or round it)
					if (reachStuckAt.Count > 0 && pi.Any(a => reachStuckAt.Any(q => ScFlat(g.Pos[a] - g.Anchor, q) < 2f))) len += 100000;
					if (len < bestLen) { bestLen = len; bestS = s; bestK = k; }
				}
			if (bestK < 0) { result(null); yield break; }
			List<int> pin = PathIn(parent[bestS], bestK), pout = PathOut(backParent, bestK);
			// (the start in the sea: the open water's last 15 m before the island (the user 10-27: a teleport to open water 15 m off), not the grid's far edge; the way out ends
			// once the player swims in water that goes on to the open sea, 15 m from the island)
			int firstLand = pin.FindIndex(a => g.Kind[a] != 1);
			if (firstLand > 0)
			{
				int st = firstLand - 1;
				while (st > 0 && ScFlat(g.Pos[pin[st - 1]], g.Pos[pin[firstLand]]) <= 15f) st--;
				pin = pin.GetRange(st, pin.Count - st);
			}
			int lastLand = pout.FindLastIndex(a => g.Kind[a] != 1);
			if (lastLand >= 0 && lastLand < pout.Count - 1)
			{
				int en = lastLand + 1;
				while (en < pout.Count - 1 && ScFlat(g.Pos[pout[en]], g.Pos[pout[lastLand]]) < 15f) en++;
				pout = pout.GetRange(0, en + 1);
			}
			string why = null;
			int speed = reachSpeed;
			for (int attempt = 0; attempt < 2; attempt++)
			{
				reachWalked++;
				why = null;
				ReachInputOn(speed);
				try
				{
					reachWalkIn = true;
					yield return ReachWalkPath(pl, e, g, pin, speed, r => why = r);
					reachWalkIn = false;
					if (why == null)
					{
						string u = ReachUseNow(pl, e, g, t);
						// (the walk ends within a cell of the use spot: a player then steps or swims the last metre to it)
						if (u != null) { yield return ReachApproach(pl, e, g, t); u = ReachUseNow(pl, e, g, t); }
						if (u != null) why = "walked in, but " + u;
					}
					List<int> wayOut = pout;
					// (stopped by something the grid didn't see, but already where it is used - in the zone, in reach of the
					// chest: a player is done there too. The way out then starts from the grid spot under the player, PQ11 10-27)
					if (why != null && why.StartsWith("stuck") && ReachUseNow(pl, e, g, t) == null)
					{
						Vector3 feet = ReachFeet(pl) - e.Position + g.Anchor;
						int near = -1; float nd = 1.6f;
						for (int k = 0; k < g.Pos.Count; k++)
						{
							if (g.Kind[k] == 3 || !back[k]) continue;
							float dk = (g.Pos[k] - feet).magnitude;
							if (dk < nd) { nd = dk; near = k; }
						}
						if (near >= 0)
						{
							Log("  walk to " + t.Kind + " '" + t.Name + "' at " + speed + "x: " + why + " - but usable there; out from the grid spot " + nd.ToString("F1") + " m off");
							why = null;
							wayOut = PathOut(backParent, near);
							int ll = wayOut.FindLastIndex(a => g.Kind[a] != 1);
							if (ll >= 0 && ll < wayOut.Count - 1)
							{
								int en = ll + 1;
								while (en < wayOut.Count - 1 && ScFlat(g.Pos[wayOut[en]], g.Pos[wayOut[ll]]) < 15f) en++;
								wayOut = wayOut.GetRange(0, en + 1);
							}
						}
					}
					if (why == null) yield return ReachWalkPath(pl, e, g, wayOut, speed, r => why = r == null ? null : "used it, then on the way out: " + r);
				}
				finally { ReachInputOff(); }
				if (why == null) { result("PASS walked (in from the " + sideNames[bestS] + ", " + pin.Count + " cells; out " + pout.Count + " cells; " + speed + "x)"); yield break; }
				if (speed == 1) break;
				Log("  walk to " + t.Kind + " '" + t.Name + "' at " + speed + "x: " + why + " - walked again at 1x");
				speed = 1;
				reachRewalked++;
			}
			if (why != null && why.StartsWith("stuck"))
			{
				Vector3 at = ReachFeet(pl) - e.Position;
				int i = reachStuckAt.FindIndex(q => ScFlat(q, at) < 2f);
				if (i < 0) { reachStuckAt.Add(at); reachStuckN.Add(1); i = reachStuckAt.Count - 1; }
				else reachStuckN[i]++;
				if (reachStuckN[i] >= 3 && reachBlocked == null)
				{
					reachBlocked = "blocked at " + reachStuckAt[i].x.ToString("F1", CultureInfo.InvariantCulture) + " " + reachStuckAt[i].z.ToString("F1", CultureInfo.InvariantCulture) + " (" + reachStuckN[i] + " targets)";
					Log("  FAIL " + reachBlocked + " - the other walks of this island are not tried");
				}
			}
			result("FAIL walked at 1x: " + why);
		}

		/// <summary>The last metre: up to 3 s toward the target (its middle; a zone's centre), Raft's own Walk, looking at it,
		/// stopping as soon as it can be used.</summary>
		static IEnumerator ReachApproach(Network_Player pl, IslandWorldState.Entry e, ReachGrid g, ReachTarget t)
		{
			Vector3 c = t.Zone ? t.Root.position : t.B.center + e.Position - g.Anchor;
			for (float s = 0f; s < 3f && ReachUseNow(pl, e, g, t) != null; s += Time.deltaTime)
			{
				Vector3 d = c - (ReachFeet(pl) + Vector3.up * (t.Zone ? 0f : 1.6f));
				Vector3 flat = new Vector3(d.x, 0f, d.z);
				bool inWater = inWaterNow(pl);
				Look(pl, flat.sqrMagnitude > 0.0001f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : pl.transform.eulerAngles.y,
					inWater ? -Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Max(flat.magnitude, 0.3f)) * Mathf.Rad2Deg, -80f, 80f) : 0f);
				mmWalk = flat.magnitude > 0.4f || inWater ? 1f : 0f;
				mmJump = inWater && d.y > 0.6f;
				MmHold(pl, false);
				yield return null;
			}
			mmWalk = 0f; mmJump = false;
		}

		/// <summary>A target used from where the player stands now: in its zone, or within Raft's use reach of the camera with
		/// nothing solid in between. Null when it can be, else why not.</summary>
		static string ReachUseNow(Network_Player pl, IslandWorldState.Entry e, ReachGrid g, ReachTarget t)
		{
			Bounds tb = new Bounds(t.B.center + e.Position - g.Anchor, t.B.size);
			Vector3 feet = ReachFeet(pl);
			if (t.Zone)
			{
				return (feet - t.Root.position).magnitude <= t.B.extents.x ? null : "not inside the zone (" + (feet - t.Root.position).magnitude.ToString("F1") + " m from its centre, radius " + t.B.extents.x.ToString("F1") + ")";
			}
			Camera cam = Camera.main;
			Vector3 eye = cam != null && (cam.transform.position - feet).magnitude < 3f ? cam.transform.position : feet + Vector3.up * 1.6f;
			foreach (Vector3 aim in new[] { tb.center, tb.ClosestPoint(eye), tb.center + Vector3.up * tb.extents.y * 0.6f })
			{
				Vector3 d = aim - eye;
				if (d.magnitude > RUse + 0.6f) continue;
				foreach (RaycastHit h in Physics.RaycastAll(eye, d.normalized, RUse + 0.6f, UseMask(t), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
				{
					if (t.Root != null && h.collider.transform.IsChildOf(t.Root)) return null;
					if (h.collider.isTrigger || h.collider.transform.IsChildOf(pl.transform)) continue;
					break;
				}
			}
			return "it isn't in reach from where the walk ended (" + (tb.center - eye).magnitude.ToString("F1") + " m from the eye)";
		}

		/// <summary>The waypoints of a path: every change of kind or height, every 6th cell, ladders, corners, the last.</summary>
		static List<int> Waypoints(ReachGrid g, List<int> path)
		{
			var w = new List<int>();
			int lastI = 0;
			for (int i = 0; i < path.Count; i++)
			{
				int k = path[i];
				bool keep = i == 0 || i == path.Count - 1 || g.Kind[k] == 3 || (i + 1 < path.Count && g.Tele.Contains(ReachGrid.TeleKey(k, path[i + 1]))) || (i > 0 && g.Tele.Contains(ReachGrid.TeleKey(path[i - 1], k))) || (i + 1 < path.Count && g.Kind[path[i + 1]] == 3) || (i > 0 && g.Kind[path[i - 1]] == 3);
				if (!keep && w.Count > 0)
				{
					int last = w[w.Count - 1];
					keep = g.Kind[k] != g.Kind[last] || Mathf.Abs(g.Pos[k].y - g.Pos[last].y) > 0.4f || (i - lastI) >= 6 ||
						(i + 1 < path.Count && Mathf.Abs(g.Pos[path[i + 1]].y - g.Pos[k].y) > 0.4f);
					// (a corner: no straight way from the last waypoint to the next cell - the walker cut a door's corner into
					// the wall beside it, PQ11 10-27)
					if (!keep && i + 1 < path.Count && g.Kind[last] < 2 && g.Kind[path[i + 1]] < 2)
					{
						Vector3 pl0 = g.Pos[last], pn = g.Pos[path[i + 1]];
						float hy = Mathf.Max(pl0.y, pn.y);
						keep = Blocked(pl0, pn, hy + 1.0f) || Swept(pl0, pn, hy);
					}
				}
				if (keep) { w.Add(k); lastI = i; }
			}
			return w;
		}

		/// <summary>Raft's own controller moves the player: its Walk axis and Jump key faked (IL9's patches), the look turned
		/// toward the next spot. One game step a frame of 1/30 s times the speed (Time.captureDeltaTime: physics keeps its own
		/// fixed step), so at 3x a step is 0.1 s.</summary>
		static Harmony reachHarmony;
		static float reachCapture0;

		static void ReachInputOn(int speed)
		{
			if (reachHarmony == null)
			{
				reachHarmony = new Harmony("ci.reachwalk");
				reachHarmony.Patch(AccessTools.Method(typeof(MyInput), "GetAxis", new[] { typeof(string) }), postfix: new HarmonyMethod(AccessTools.Method(typeof(DevTests), "MmAxisPostfix")));
				reachHarmony.Patch(AccessTools.Method(typeof(MyInput), "GetButton", new[] { typeof(string) }), postfix: new HarmonyMethod(AccessTools.Method(typeof(DevTests), "MmButtonPostfix")));
				if (MmIsPressed != null) reachHarmony.Patch(MmIsPressed, postfix: new HarmonyMethod(AccessTools.Method(typeof(DevTests), "MmPressedPostfix")));
				if (MmIsPressed != null && MmGroundControll != null) reachHarmony.Patch(MmGroundControll, transpiler: new HarmonyMethod(AccessTools.Method(typeof(DevTests), "MmGroundTranspiler")));
				reachCapture0 = Time.captureDeltaTime;
			}
			mmFake = true; mmWalk = 0f; mmSprint = mmJump = false;
			Time.captureDeltaTime = speed / 30f;
			reachPaceOn = speed > 1 ? reachPace : 1f;
		}

		/// <summary>Each frame of a walk (from MmHold): Raft's walk and swim speeds at the pace - a player's in the air.</summary>
		static void ReachPace(PersonController pc)
		{
			if (reachWalk0 <= 0f) { reachWalk0 = pc.normalSpeed; reachSwim0 = pc.swimSpeed; }
			float m = mmFake && reachPaceOn > 1f && (pc.controllerType == ControllerType.Water || pc.IsGrounded) ? reachPaceOn : 1f;
			pc.normalSpeed = reachWalk0 * m;
			pc.swimSpeed = reachSwim0 * m;
		}

		static void ReachInputOff()
		{
			mmFake = false; mmWalk = 0f; mmSprint = mmJump = false;
			reachPaceOn = 1f;
			Network_Player rp = RAPI.GetLocalPlayer();
			if (rp != null && reachWalk0 > 0f) { rp.PersonController.normalSpeed = reachWalk0; rp.PersonController.swimSpeed = reachSwim0; }
			if (reachHarmony == null) return;
			Time.captureDeltaTime = reachCapture0;
			try { reachHarmony.UnpatchAll("ci.reachwalk"); } catch (Exception ex) { Log("(unpatching the reach walker: " + ex.Message + ")"); }
			reachHarmony = null;
		}

		/// <summary>The player through a path of the grid, moved by Raft itself: the first point is the start in the sea (the
		/// one teleport); then the walker looks at the next point and holds Walk (swimming and diving look up or down at it),
		/// jumps at a ledge it doesn't get over, turns aside a little when stuck, and on a ladder looks up or down and holds Walk.
		/// Each frame the line from the player's middle before to after must cross nothing solid (else the walk is invalid).
		/// Null when it got to the end, else where it stopped.</summary>
		static IEnumerator ReachWalkPath(Network_Player pl, IslandWorldState.Entry e, ReachGrid g, List<int> path, int speed, Action<string> result)
		{
			List<int> way = Waypoints(g, path);
			Func<int, Vector3> W = k => e.Position + (g.Pos[k] - g.Anchor);
			// (a player who died in the play - drowned on a dive step - is "dead" with full health: Raft's PersonController then
			// keeps the controller off and movement not free every frame. Respawned first, as a player would be)
			if (ReachDead(pl)) { reachRespawns++; yield return EnsureAlive(); }
			if (g.Kind[way[0]] == 1)
			{
				PlayerMove.To(pl, W(way[0]) + Vector3.down * 0.4f, ControllerType.Water);
				for (int f = 0; f < 10; f++) { MmHold(pl, false); yield return null; }
				// (after a play the character controller was found off - the player didn't move at all: noted once, then on again)
				// (the play's last opened chest leaves Raft's "movement not free" state and the controller off - a player closes
				// the chest's window; the walker frees them the same way, noted)
				if (ReachFree(pl)) for (int f = 0; f < 5; f++) { MmHold(pl, false); yield return null; }
			}
			else if (reachSky && reachWalkIn && g.Kind[way[0]] == 0 && (ReachFeet(pl) - W(way[0])).magnitude > 1.5f)
			{
				// (a flying island: the walk in starts on its rim, where a player's stairs from the raft come up)
				PlayerMove.To(pl, W(way[0]) + Vector3.up * 0.95f);
				for (int f = 0; f < 10; f++) { MmHold(pl, false); yield return null; }
				if (ReachFree(pl)) for (int f = 0; f < 5; f++) { MmHold(pl, false); yield return null; }
			}
			Vector3 prev = ReachFeet(pl) + Vector3.up * 0.9f;
			string bad = null;
			// (stall rule, the user 10-28 - no multi-minute waits on one target: real seconds, whatever the speed. Under 0.5 m in a 10 s
			// window: one unstick (a jump and a turn). No waypoint for 30 s while stalled, or for 90 s with under 3 m in the last window:
			// FAIL "stuck" - it still counts as a fail to triage)
			float wdWin = Time.realtimeSinceStartup, wdWay = wdWin, wdMoved = 99f;
			Vector3 wdP = ReachFeet(pl);
			bool wdTried = false;
			Func<int> watchdog = () =>
			{
				float now = Time.realtimeSinceStartup;
				if (now - wdWin >= 10f)
				{
					Vector3 q = ReachFeet(pl);
					wdMoved = (q - wdP).magnitude; wdP = q; wdWin = now;
					if (wdMoved >= 0.5f) wdTried = false;
					else if (!wdTried) { wdTried = true; return 1; }
				}
				if ((wdMoved < 0.5f && now - wdWay >= 30f) || (wdMoved < 3f && now - wdWay >= 90f)) return 2;
				return 0;
			};
			Func<string> wdWhy = () => "stuck (no waypoint for " + (Time.realtimeSinceStartup - wdWay).ToString("F0") + " s real time, moved " + wdMoved.ToString("F1") + " m in the last 10 s)";
			Func<string> guard = () =>
			{
				Vector3 now = ReachFeet(pl) + Vector3.up * 0.9f;
				Vector3 d = now - prev;
				string r = null;
				if (d.sqrMagnitude > 1e-6f && d.magnitude < 30f)
					foreach (RaycastHit h in Physics.RaycastAll(prev, d.normalized, d.magnitude, reachMask, QueryTriggerInteraction.Ignore))
					{
						if (h.collider.transform.IsChildOf(pl.transform)) continue;
						// (a collider Raft's own CharacterController never meets - a layer the player's layer ignores, or a pair
						// set to ignore - is passed through by a real player too, e.g. a landmark zipline's rope: not tunnelling, PQ11 10-27)
						CharacterController gcc = pl.GetComponent<CharacterController>();
						if (gcc != null && (Physics.GetIgnoreLayerCollision(gcc.gameObject.layer, h.collider.gameObject.layer) || Physics.GetIgnoreCollision(gcc, h.collider))) continue;
						r = "INVALID: the walker crossed " + BlockerName(h.collider) + " (layer " + LayerMask.LayerToName(h.collider.gameObject.layer) + ", player " + (gcc != null ? LayerMask.LayerToName(gcc.gameObject.layer) : "?") + ") at " + (h.point - e.Position).ToString("F1"); break;
					}
				prev = now;
				return r;
			};
			for (int i = 1; i < way.Count; i++)
			{
				wdWay = Time.realtimeSinceStartup; wdTried = false; wdMoved = 99f; hbProg++;
				int k = way[i];
				byte kind = g.Kind[k];
				// (a teleport step: the player at the object uses it and is put where it puts them - the grid's link, see above)
				if (g.Tele.Contains(ReachGrid.TeleKey(way[i - 1], k)))
				{
					PlayerMove.To(pl, W(k) + Vector3.up * 0.1f, ControllerType.Ground);
					for (int f = 0; f < 20; f++) { MmHold(pl, false); yield return null; }
					prev = ReachFeet(pl) + Vector3.up * 0.9f;
					continue;
				}
				if (kind == 3)
				{
					// a ladder: walk into it (Raft takes hold), look up or down and hold Walk to the next spot's height
					// (pitch: Raft's MouseLook is positive looking down - the walker looked down going up and never rose, PQ11 10-27)
					Bounds lb;
					if (!g.LadderB.TryGetValue(k, out lb)) lb = new Bounds(g.Pos[k], Vector3.one * 0.5f);
					Vector3 lc = e.Position + (lb.center - g.Anchor);
					float ladTop = e.Position.y + (lb.max.y - g.Anchor.y);
					float nextY = i + 1 < way.Count ? W(way[i + 1]).y : W(k).y;
					bool up = nextY > ReachFeet(pl).y + 0.3f;
					float held = 0f, since = 0f, bestY = ReachFeet(pl).y, walkSign = 1f;
					Vector3 lfront; bool hasF = g.LadderF.TryGetValue(k, out lfront), atFront = false;
					int flips = 0, sweeps = 0; bool otherSide = false; float bestH = float.MaxValue;
					float sweepT = 0f, sweepYaw = 0f;
					for (int n = 0; n < 6000; n++)
					{
						Vector3 p = ReachFeet(pl);
						if (up ? p.y >= nextY + 0.1f : p.y <= nextY + 0.15f) break;
						// (at the ladder's top Raft holds the player at its end - the next segment walks off it onto the floor;
						// the walker kept climbing to the floor's height and stuck there, radar mast PQ11 10-27)
						if (up && Climbing(pl) && p.y >= ladTop - 0.35f) break;
						Vector3 d = lc - p; d.y = 0f;
						bool climbing = Climbing(pl);
						// (going down from a ladder's top while standing over it: head for the spot at its foot, over the shaft -
						// the walker stood on the wall top behind the ladder and walked nowhere, radar mast PQ11 10-27)
						if (!up && !climbing && d.sqrMagnitude < 0.16f && i + 1 < way.Count) { d = W(way[i + 1]) - p; d.y = 0f; }
						if (climbing) held += Time.deltaTime;
						// (a player stands in front of a ladder and walks into it facing it; the walker went to the ladder's middle -
						// inside its climb collider, facing wherever - and Raft never took hold: tower ladders PQ11 10-27)
						if (up && hasF && !climbing && held <= 0f && sweepT <= 0f)
						{
							Vector3 ds = lc + lfront * 0.55f - p; ds.y = 0f;
							// (still walking round to that side: getting nearer counts as progress)
							if (!atFront && ds.magnitude < bestH - 0.1f) { bestH = ds.magnitude; since = 0f; }
							if (!atFront && ds.sqrMagnitude > 0.04f) d = ds; else { atFront = true; d = -lfront; }
						}
						// (a player turns to face the ladder; the walker stood on the ladder's spot facing along the wall and Raft never
						// took hold - tower ladders at Array Root/Caisson Yard/Brinelab PQ11 10-27: sweep four headings into it)
						if (climbing) sweepT = 0f;
						if (sweepT > 0f) { sweepT -= Time.deltaTime; d = Quaternion.Euler(0f, sweepYaw, 0f) * Vector3.forward; }
						// (a swimmer at a dock ladder looks up and swims up into it, Jump held - Raft takes hold when the body touches its
						// "Ladder" trigger (PersonController.OnTriggerStay); the walker swam level under the rungs and never touched it,
						// Caisson Yard / Array Root PQ11 10-28)
						bool wet = up && !climbing && inWaterNow(pl);
						mmJump = wet;
						Look(pl, d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : pl.transform.eulerAngles.y, climbing ? (up ? -60f : 60f) : wet ? -35f : 0f);
						mmWalk = walkSign;
						MmHold(pl, false);
						yield return null;
						if ((bad = guard()) != null) { result(bad); yield break; }
						int wd = watchdog();
						if (wd == 1 && !climbing) { sweepYaw = 45f + 90f * sweeps; sweepT = 0.35f; }
						if (wd == 2) { mmWalk = 0f; result(wdWhy() + " at the ladder at " + (p - e.Position).ToString("F1") + " going " + (up ? "up" : "down") + " (in the way: " + ReachBlocker(pl, up ? Vector3.up : Vector3.down) + ")"); yield break; }
						float y = ReachFeet(pl).y;
						if (up ? y > bestY + 0.1f : y < bestY - 0.1f) { bestY = y; since = 0f; } else since += Time.deltaTime;
						if (since > 2.5f)
						{
							if (flips < 1 && climbing) { walkSign = -walkSign; flips++; since = 0f; continue; }
							if (held <= 0f && !climbing && sweeps < 4) { sweepYaw = sweeps * 90f; sweeps++; sweepT = 0.35f; since = 1.2f; continue; }
							// (a player who can't take hold from one side tries the other: the dp_tower ladders took hold only from their back,
							// the walker came from the front, walked through them and on 11 m - PQ11 10-28)
							if (held <= 0f && !climbing && hasF && !otherSide) { otherSide = true; lfront = -lfront; atFront = false; sweeps = 0; since = 0f; bestH = float.MaxValue; continue; }
							mmWalk = 0f;
							result("stuck at the ladder at " + (p - e.Position).ToString("F1") + " going " + (up ? "up" : "down") + " to h=" + (nextY - e.Position.y).ToString("F1") +
								(held > 0f ? " (Raft held the ladder " + held.ToString("F1") + " s)" : " (Raft never took hold of it)") + " (in the way: " + ReachBlocker(pl, up ? Vector3.up : Vector3.down) + ")");
							yield break;
						}
					}
					mmJump = false;
					continue;
				}
				Vector3 aim = W(k);
				bool water = kind != 0;
				float best = float.MaxValue, since2 = 0f, turnLeft = 0f, turnDir = 1f, jumpT = 0f;
				// (a drop off a ledge into the sea or onto a floor below: the walker gets over the spot and then keeps going the
				// way it came in, over the edge, as a player walks off it)
				Vector3 seg = W(k) - W(way[i - 1]); seg.y = 0f;
				int hops = 0, turns = 0, steps = 0;
				float hopFrom = float.NaN, hopPeak = 0f;
				for (int n = 0; ; n++)
				{
					// (aimed afresh each frame: a world shift mid-step moved the island but left the aim 1000 m off, PQ11 10-28)
					aim = W(k);
					Vector3 p = ReachFeet(pl);
					Vector3 d = aim - p;
					Vector3 flat = new Vector3(d.x, 0f, d.z);
					float flatLen = flat.magnitude;
					// (progress counts the way down too: a drop's last metre is a fall, not a walk)
					float dist = water ? d.magnitude : flatLen + Mathf.Max(0f, d.y - 0.6f) + Mathf.Max(0f, -d.y - 0.9f);
					if (d.y < -0.9f && flatLen < 1.3f && seg.sqrMagnitude > 0.01f && !inWaterNow(pl)) flat = seg.normalized * Mathf.Max(flatLen, 0.8f);
					if (water ? (kind == 1 ? flatLen < 0.8f && Mathf.Abs(d.y) < 1.4f : d.magnitude < 1.0f || (flatLen < 0.8f && d.y < 0f && pl.PersonController.controllerType == ControllerType.Water && pl.GetComponent<CharacterController>() != null && pl.GetComponent<CharacterController>().isGrounded)) : flatLen < 0.5f && d.y < 0.9f && d.y > -0.9f) break;
					if (dist < best - 0.15f) { best = dist; since2 = 0f; } else since2 += Time.deltaTime;
					if (since2 > 2.5f)
					{
						// (a jump at whatever stops it - a ledge, a rock's lip at the shore - as a player would)
						if (hops < 3 && d.y > -0.9f) { hops++; hopFrom = p.y; jumpT = 0.4f; since2 = 1.2f; }
						// (a swimmer at a lip: a player climbs out where the lip top is near the surface (up to 0.6 m over the sea, within
						// 1.6 m of the feet), or stands on the bed by the lip (standing depth, 0.8 m short of it) and steps or jumps up
						// (RJump). Raft's water controller cannot rise out of the sea, so the walker lifts the feet onto the lip - varuna and
						// spirecrest 0.7-2 m lips, PQ11 10-29)
						else if (steps < 3 && inWaterNow(pl) && d.y > 0f && flatLen < 2.6f && ((aim.y < RSea + 0.6f && d.y < 1.6f) || (StandBed(aim - flat.normalized * 0.8f) > RSea - 1.6f && aim.y - StandBed(aim - flat.normalized * 0.8f) < RJump)) &&
							!Physics.Linecast(new Vector3(p.x, Mathf.Max(aim.y, RSea) + 0.5f, p.z), new Vector3(aim.x, Mathf.Max(aim.y, RSea) + 0.5f, aim.z), reachMask, QueryTriggerInteraction.Ignore))
						{
							steps++;
							CharacterController scc = pl.GetComponent<CharacterController>();
							Vector3 to = new Vector3(aim.x, aim.y + 0.05f, aim.z) + (pl.transform.position - p);
							if (scc != null) scc.enabled = false;
							pl.transform.position = to;
							if (scc != null) scc.enabled = true;
							since2 = 1.2f;
						}
						else if (turns < 2) { turns++; turnLeft = 0.7f; turnDir = turns == 1 ? 1f : -1f; since2 = 0f; }
						else
						{
							string diag = ReachDiag(pl);
							mmWalk = 0f; mmJump = false;
							result("stuck at " + (p - e.Position).ToString("F1") + " on the way to " + (aim - e.Position).ToString("F1") + (hops > 0 ? " (" + hops + " jumps, rose up to " + hopPeak.ToString("F2") + " m)" : "") + " (in the way: " + ReachBlocker(pl, flat.sqrMagnitude > 0.01f ? flat.normalized : d.normalized) + ")" + diag);
							yield break;
						}
					}
					// (Raft's MouseLook pitch is positive looking down. Swimming up to the surface Jump is held too, as a player would)
					mmJump = jumpT > 0f || (inWaterNow(pl) && d.y > 0.6f); jumpT -= Time.deltaTime;
					float yaw = flat.sqrMagnitude > 0.0001f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : pl.transform.eulerAngles.y;
					if (turnLeft > 0f) { yaw += 60f * turnDir; turnLeft -= Time.deltaTime; }
					bool inWater = pl.PersonController.controllerType == ControllerType.Water;
					float pitch = inWater ? -Mathf.Clamp(Mathf.Atan2(d.y + (kind == 1 ? 0.3f : 0f), Mathf.Max(flat.magnitude, 0.3f)) * Mathf.Rad2Deg, -80f, 80f) : 0f;
					// (going down under water: at least 45 degrees down - a shallow look keeps a swimmer at the surface, into a cave's rim)
					if (inWater && d.y < -0.5f) pitch = Mathf.Max(pitch, 45f);
					Look(pl, yaw, pitch);
					mmWalk = 1f;
					MmHold(pl, false);
					yield return null;
					if (!float.IsNaN(hopFrom)) hopPeak = Mathf.Max(hopPeak, ReachFeet(pl).y - hopFrom);
					if ((bad = guard()) != null) { mmWalk = 0f; result(bad); yield break; }
					int wd = watchdog();
					if (wd == 1) { jumpT = 0.4f; turnLeft = 0.7f; turnDir = -turnDir; }
					if (wd == 2) { mmWalk = 0f; mmJump = false; result(wdWhy() + " at " + (p - e.Position).ToString("F1") + " on the way to " + (aim - e.Position).ToString("F1") + " (in the way: " + ReachBlocker(pl, flat.sqrMagnitude > 0.01f ? flat.normalized : d.normalized) + ")"); yield break; }
					if (n > 60000) { mmWalk = 0f; result("gave up (too long)"); yield break; }
				}
			}
			mmWalk = 0f; mmJump = false;
			result(null);
		}

		static bool inWaterNow(Network_Player pl) { return pl.PersonController.controllerType == ControllerType.Water; }

		/// <summary>The sea floor under a swimmer (where a player would stand), or far down when there is none within 3 m.</summary>
		static float StandBed(Vector3 p)
		{
			RaycastHit h;
			return Physics.Raycast(new Vector3(p.x, Mathf.Min(p.y + 0.5f, RSea), p.z), Vector3.down, out h, 3f, reachMask, QueryTriggerInteraction.Ignore) ? h.point.y : RSea - 9f;
		}

		/// <summary>The player's feet: Raft's player transform sits about 0.9 m above them (the grid's spots are feet).</summary>
		static Vector3 ReachFeet(Network_Player pl)
		{
			Vector3 p = pl.transform.position;
			CharacterController cc = pl.GetComponent<CharacterController>();
			return cc != null && cc.enabled ? new Vector3(p.x, cc.bounds.min.y, p.z) : p;
		}

		static bool ReachDead(Network_Player pl)
		{
			Player p = pl.GetComponentInChildren<Player>(true);
			return (p != null && p.IsDead) || (pl.Stats != null && pl.Stats.stat_health.Value <= 0f);
		}

		/// <summary>True when the player was held (controller off or Raft's movement not free) and was let go.</summary>
		static bool ReachFree(Network_Player pl)
		{
			CharacterController cc0 = pl.PersonController.controller;
			if ((cc0 == null || cc0.enabled) && pl.PersonController.IsMovementFree) return false;
			if (reachCcNoted++ < 3) Log("(reach walker: the player was held" + ReachDiag(pl) + " - let go)");
			Traverse.Create(pl.PersonController).Field("isMovementFree").SetValue(true);
			if (cc0 != null) cc0.enabled = true;
			return true;
		}

		static string ReachDiag(Network_Player pl)
		{
			PersonController pc = pl.PersonController;
			return " [controller " + pc.controllerType + ", pc " + pc.enabled + ", cc " + (pc.controller != null && pc.controller.enabled) + ", grounded " + (pc.controller != null && pc.controller.isGrounded) +
				", walk axis " + MyInput.GetAxis("Walk").ToString("F1") + ", timeScale " + Time.timeScale.ToString("F1") + ", dt " + Time.deltaTime.ToString("F3") + ", menu " + CanvasHelper.ActiveMenu + ", shim " + mmShimCalls + ", free " + pc.IsMovementFree + ", held " + PlayerHold.Busy + ", parent " + (pl.transform.parent != null ? pl.transform.parent.name : "none") + ", dead " + ReachDead(pl) + ", health " + (pl.Stats != null ? pl.Stats.stat_health.Value.ToString("F0") : "?") +
				(pc.controller != null ? ", body h " + pc.controller.height.ToString("F2") + " r " + pc.controller.radius.ToString("F2") + " step " + pc.controller.stepOffset.ToString("F2") : "") + "]";
		}

		static string ReachBlocker(Network_Player pl, Vector3 dir)
		{
			foreach (float up in new[] { 0.9f, 0.35f, 1.5f })
				foreach (RaycastHit h in Physics.SphereCastAll(ReachFeet(pl) + Vector3.up * up, 0.25f, dir, 1.2f, reachMask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
					if (!h.collider.transform.IsChildOf(pl.transform)) return BlockerName(h.collider) + (up < 0.5f ? " (low)" : "");
			return "nothing found";
		}

		/// <summary>Negative controls: the walker, at the reach speed, walks straight (Raft's own Walk, no jumps, no turning) from a
		/// floor to a floor 1.2-3 m beyond a wall too high to step or jump. It must never get there. failed(n): controls that failed.</summary>
		static IEnumerator ReachControls(IslandWorldState.Entry e, ReachGrid g, bool[][] fwd, List<string> report, Action<int> failed)
		{
			Network_Player pl = RAPI.GetLocalPlayer();
			if (pl == null) yield break;
			var pairs = new List<KeyValuePair<int, int>>();
			var rnd = new System.Random(1);
			// (every reached floor spot in a shuffled order, every column 1.2-3 m round it: the first 4 walls found, 6 m apart)
			var cands = Enumerable.Range(0, g.Pos.Count).Where(a => g.Kind[a] == 0 && (fwd[0][a] || fwd[1][a] || fwd[2][a] || fwd[3][a]) && g.Pos[a].y >= RSea + 0.3f).OrderBy(a => rnd.Next()).ToList();
			int rr = Mathf.CeilToInt(3f / g.Cell), looked = 0;
			foreach (int a in cands)
			{
				if (pairs.Count >= 4 || looked > 4000) break;
				looked++;
				int ci = Mathf.FloorToInt((g.Pos[a].x - g.X0) / g.Cell), cj = Mathf.FloorToInt((g.Pos[a].z - g.Z0) / g.Cell);
				bool found = false;
				for (int di2 = -rr; di2 <= rr && !found; di2 += 2) for (int dj2 = -rr; dj2 <= rr && !found; dj2 += 2)
				{
				List<int> col = g.At(ci + di2, cj + dj2);
				if (col == null) continue;
				foreach (int b in col)
				{
					Vector3 pa = g.Pos[a], pb = g.Pos[b];
					float fd = ScFlat(pa, pb);
					if (g.Kind[b] != 0 || fd < 1.2f || fd > 3f || Mathf.Abs(pa.y - pb.y) > 0.2f) continue;
					float hi = Mathf.Max(pa.y, pb.y);
					// (a wall, not a post: blocked on lines 0.6 m to both sides too - walking straight at a post Raft's
					// controller slides round it, as a player's does)
					Vector3 side = Vector3.Cross(Vector3.up, pb - pa).normalized;
					if (new[] { 0f, 0.6f, -0.6f }.All(o => Blocked(pa + side * o, pb + side * o, hi + 0.45f) && Blocked(pa + side * o, pb + side * o, hi + 1.0f) && Blocked(pa + side * o, pb + side * o, hi + 1.55f)) && Blocked(pa, pb, hi + 2.2f) &&
						!pairs.Any(q => ScFlat(g.Pos[q.Key], pa) < 6f)) { pairs.Add(new KeyValuePair<int, int>(a, b)); found = true; break; }
				}
				}
			}
			int bad = 0;
			ReachInputOn(reachSpeed);
			try
			{
				foreach (var pr in pairs)
				{
					Vector3 from = e.Position + (g.Pos[pr.Key] - g.Anchor), to = e.Position + (g.Pos[pr.Value] - g.Anchor);
					if (ReachDead(pl)) { reachRespawns++; yield return EnsureAlive(); }
					PlayerMove.To(pl, from + Vector3.up * 0.1f);
					ReachFree(pl);
					mmWalk = 0f;
					for (int f = 0; f < 10; f++) { MmHold(pl, false); yield return null; }
					bool got = false, invalid = false;
					float moved = 0f;
					Vector3 start = ReachFeet(pl);
					Vector3 prev = ReachFeet(pl) + Vector3.up * 0.9f;
					Vector3 d0 = to - from; d0.y = 0f;
					for (float t = 0f; t < 6f; t += Time.deltaTime)
					{
						Vector3 d = to - ReachFeet(pl); d.y = 0f;
						if (d.magnitude < 0.5f) { got = true; break; }
						Look(pl, Mathf.Atan2(d0.x, d0.z) * Mathf.Rad2Deg, 0f);
						mmWalk = 1f;
						MmHold(pl, false);
						yield return null;
						Vector3 now = ReachFeet(pl) + Vector3.up * 0.9f;
						if ((now - prev).sqrMagnitude > 1e-6f && Physics.Linecast(prev, now, reachMask, QueryTriggerInteraction.Ignore)) invalid = true;
						prev = now;
						moved = Mathf.Max(moved, Vector3.ProjectOnPlane(ReachFeet(pl) - start, Vector3.up).magnitude);
					}
					mmWalk = 0f;
					Vector3 rf = from - e.Position, rt = to - e.Position;
					// (only a crossing is a fault: reaching the far side round the wall's end is a player's walk too)
					if (invalid) bad++;
					report.Add("REACH " + e.HostName + " | control: straight at a wall " + rf.ToString("F1") + " -> " + rt.ToString("F1") + " | " + reachSpeed + "x | " +
						(invalid ? "FAIL the walker went through it (the guard caught a crossing)" : got ? "PASS (no crossing; went round it)" : moved < 0.3f ? "FAIL the walker never moved (" + moved.ToString("F2") + " m)" + ReachDiag(pl) : "PASS stays blocked (moved " + moved.ToString("F1") + " m to it)") + "");
					if (!got && !invalid && moved < 0.3f) bad++;
				}
			}
			finally { ReachInputOff(); }
			if (pairs.Count == 0) report.Add("REACH " + e.HostName + " | control | " + reachSpeed + "x | NOTE no wall to walk at found");
			failed(bad);
		}
	}
}
