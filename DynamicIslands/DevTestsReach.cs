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
		// (CIReachOnly: walk only targets whose name holds this)
		static string reachOnly = "";
		// (how many of the walker's moves a frame: 1 = a player's pace; every FAIL and every walk that crossed a collider is
		// walked again at 1)
		static int reachSpeed = 3;
		// (CI only: Raft's own walk and swim speeds times this while a walk runs above 1x; Raft moves the player with
		// CharacterController.Move, a swept move, so a faster pace still stops at every wall; off in the air, so jumps reach
		// no farther than a player's; 1 = off)
		static float reachPace = 1f, reachPaceOn = 1f, reachWalk0, reachSwim0;
		static int reachPrechecked, reachWalked, reachRewalked, reachCcNoted, reachRespawns, reachUnparented;
		// (spots walks got stuck at on this island (island-local) and how many targets each blocked; once one spot blocks 3
		// targets the island's other walks are not tried - each is a FAIL with that cause. Dead Letter Office: every chest walk
		// stuck at the same shore lip after 3 jumps, PQ11 10-28)
		static List<Vector3> reachStuckAt = new List<Vector3>();
		static List<int> reachStuckN = new List<int>();
		// spots where 3+ walks stuck: later walks whose every way in passes one are not tried (cm1ss 11-27: one ladder at
		// Sealkeeper's control room ended all 17 later walks, low ones with no ladder on their way among them)
		static List<Vector3> reachBlockedAt = new List<Vector3>();
		static List<string> reachBlockedWhy = new List<string>();

		[ConsoleCommand(name: "CIReachSpeed", docs: "Dev, anywhere: PQ11 - the reach walker's speed: moves a frame, each one swept by Raft's own CharacterController (1 = a player's pace, 0 = no walks, the grid alone); pace: Raft's walk/swim speed times p on the ground and in the water (not in the air) while above 1x. CIReachSpeed <n> [pace]")]
		public static void ReachSpeedCommand(string[] args)
		{
			if (args != null && args.Length > 0) reachSpeed = Mathf.Clamp((int)F(args[0]), 0, 10);
			if (args != null && args.Length > 1) reachPace = Mathf.Clamp(F(args[1]), 1f, 4f);
			Log("PASS: reach speed " + reachSpeed + ", pace " + reachPace.ToString("F1") + " (" + reachPrechecked + " prechecked, " + reachWalked + " walked, " + reachRewalked + " walked again at 1x so far)");
		}

		[ConsoleCommand(name: "CIReachOnly", docs: "Dev, anywhere: the next CIReach runs walk only the quest targets whose name holds this text (case aside), the rest reported 'skipped (CIReachOnly)' - to walk one failing target again without the whole island; several texts split by | walk each match in the island's order (a sequence that failed in the full run); no text = all again")]
		public static void ReachOnlyCommand(string[] args)
		{
			reachOnly = args == null ? "" : string.Join(" ", args).Trim();
			Log("PASS: reach only " + (reachOnly == "" ? "(all targets)" : "'" + reachOnly + "'"));
		}

		[ConsoleCommand(name: "CIReachDismountUnit", docs: "Dev, anywhere: the walker's jump off a ladder's top - after a climb up, a next floor 0.25 to 1.2 m above the feet (a hatch beside or behind the ladder) is jumped onto, and one level with the feet (-0.3 m) that is not ahead over the ladder's top; one ahead level with the feet, one out of a jump's reach, or a walk not off a ladder is walked; a next spot more than 0.75 m over the held feet is stepped to by the floor beside the ladder's top first; a ladder and the step off its top run at 1x")]
		public static void CIReachDismountUnit()
		{
			bool ok = true;
			Action<bool, string> chk = (c, w) => { Log((c ? "PASS: " : "FAIL: ") + w); ok &= c; };
			chk(ReachLadderDismount(true, 31.25f, 31.73f), "headframe: feet 0.48 m under the deck at the ladder's top - jump onto it");
			chk(!ReachLadderDismount(true, 31.7f, 31.73f), "feet level with the floor ahead, over the ladder's top - walk off");
			chk(ReachLadderDismount(true, 40.1f, 40.12f, false), "Utopia's mast: feet level with the deck beside the ladder - jump onto it");
			chk(!ReachLadderDismount(true, 40.6f, 40.12f, false), "the floor beside 0.5 m under the feet - walk off");
			chk(!ReachLadderDismount(true, 30.0f, 31.73f), "the floor 1.7 m up - out of a jump's reach, no jump");
			chk(!ReachLadderDismount(false, 31.25f, 31.73f), "not after a climb up - the walk's own jumps only");
			chk(!ReachLadderDismount(false, 40.1f, 40.12f, false), "level beside, not after a climb up - no jump");
			chk(ReachLadderStepFirst(36.07f, 37.2f), "Toll Hill: the locker's top 1.13 m over the held feet - onto the deck beside first");
			chk(!ReachLadderStepFirst(36.07f, 36.74f), "Brinelab's/Toll Hill's deck 0.67 m over the held feet - jump straight onto it");
			chk(ReachLadderClock == 1, "a ladder and the step off its top at 1x, whatever the walk's speed");
			Log(ok ? "PASS: reach dismount unit" : "FAIL: reach dismount unit");
		}

		[ConsoleCommand(name: "CIReachByPlayUnit", docs: "Dev, anywhere: the reach's 'checked by play' (island reach.byplay=...) - a target a play goes to (zone/open) is found in the plays, case aside; a made-up one is not")]
		public static void CIReachByPlayUnit()
		{
			bool ok = true;
			Action<bool, string> chk = (c, w) => { Log((c ? "PASS: " : "FAIL: ") + w); ok &= c; };
			chk(ByPlayFile("jump_om_finale") == "old_mine_islet.play", "a zone the play goes to: " + (ByPlayFile("jump_om_finale") ?? "none"));
			chk(ByPlayFile("slag STACK prize") == "old_mine_islet.play", "a chest it opens, any case");
			chk(ByPlayFile("No such chest anywhere 11-05") == null, "a target no play visits: none");
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Slag stack prize" };
			chk(ByPlayNote(set, new ReachTarget { Name = "Slag stack prize" }).StartsWith("CHECKED BY PLAY"), "listed + played: checked by play");
			chk(ByPlayNote(set, new ReachTarget { Name = "Ore barge's crate" }) == null, "not listed: the grid's own verdict");
			chk(ByPlayNote(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "No such chest anywhere 11-05" }, new ReachTarget { Name = "No such chest anywhere 11-05" }) == null, "listed, no play visits it: the grid's own verdict");
			chk(LadderHandReach(0) == 1.0f && LadderHandReach(1) == 1.5f && LadderHandReach(2) == 1.5f, "a ladder's hand reach: 1.0 m on foot, 1.5 m swimming or diving (cells 1 m apart)");
			// a ladder's room: none under the ground (a terrain at y -500, flat at 4 m)
			var td = new TerrainData { heightmapResolution = 33 };
			td.size = new Vector3(20f, 10f, 20f);
			var hts = new float[33, 33];
			for (int i = 0; i < 33; i++) for (int j = 0; j < 33; j++) hts[i, j] = 0.4f;
			td.SetHeights(0, 0, hts);
			GameObject tgo = Terrain.CreateTerrainGameObject(td);
			tgo.transform.position = new Vector3(0f, -500f, 0f);
			int was = reachMask;
			try
			{
				Physics.SyncTransforms();
				reachMask = 1 << tgo.layer;
				chk(!LadderRoom(new Bounds(new Vector3(10f, -499f, 10f), new Vector3(0.6f, 4f, 0.2f)), Vector3.forward), "a ladder's room under the ground: none");
				chk(LadderFree(new Bounds(new Vector3(10f, -496.5f, 10f), new Vector3(0.6f, 4f, 0.2f)), Vector3.forward) == 1, "a ladder half in the ground: 1 of its 3 spots free, over it (" + LadderFree(new Bounds(new Vector3(10f, -496.5f, 10f), new Vector3(0.6f, 4f, 0.2f)), Vector3.forward) + ")");
				chk(LadderRoom(new Bounds(new Vector3(10f, -490f, 10f), new Vector3(0.6f, 4f, 0.2f)), Vector3.forward), "a ladder's room in the air over it: room");
			}
			finally { reachMask = was; UnityEngine.Object.Destroy(tgo); }
			if (ok) Log("PASS: reach by play"); else Fail("reach by play");
		}

		/// <summary>Where in a 1 m swim cell round c the swimmer (a 0.4 m sphere) fits, out of solid: its middle, else nudged up
		/// to 0.45 m down, up or to a side. A cave mouth's low slot (Riverfork's, 11-08: 1.2 m high at -8.3 to -7.1) fell
		/// between the cells' middles 1 m apart, every cell there touched the rock and the den behind it was 'can't get
		/// there' though a player swims in; a gap too small for the sphere anywhere in the cell stays shut.</summary>
		static bool SwimSpot(Vector3 c, int mask, out Vector3 at)
		{
			foreach (Vector3 d in SwimNudges)
			{
				at = c + d;
				if (!Physics.CheckSphere(at, 0.4f, mask, QueryTriggerInteraction.Ignore) && !InsideSolid(at)) return true;
			}
			at = c;
			return false;
		}

		static readonly Vector3[] SwimNudges =
		{
			Vector3.zero, new Vector3(0f, -0.3f, 0f), new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 0f, 0f), new Vector3(-0.3f, 0f, 0f),
			new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, -0.3f), new Vector3(0f, -0.45f, 0f), new Vector3(0f, 0.45f, 0f)
		};

		[ConsoleCommand(name: "CIReachSwimSpotUnit", docs: "Dev, anywhere: the reach's swim cells (SwimSpot, 11-08) - an open cell keeps its middle; a 1.25 m slot off the middle (a cave mouth's low gap) is found by a nudge; a 0.55 m slot, a sphere too big anywhere in it, stays shut")]
		public static void ReachSwimSpotUnitCommand() { StartTest(ReachSwimSpotUnitRoutine()); }

		static IEnumerator ReachSwimSpotUnitRoutine()
		{
			bool ok = true;
			int was = reachMask;
			var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
			var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
			try
			{
				foreach (GameObject b in new[] { roof, floor }) { b.transform.localScale = new Vector3(6f, 2f, 6f); UnityEngine.Object.Destroy(b.GetComponent<Renderer>()); }
				int mask = 1 << roof.layer;
				reachMask = mask;
				Vector3 c = new Vector3(0f, -500f, 0f), at;
				roof.transform.position = new Vector3(0f, -499.75f + 1f, 0f);
				floor.transform.position = new Vector3(0f, -510f, 0f);
				Physics.SyncTransforms();
				Check(ref ok, SwimSpot(new Vector3(0f, -503f, 0f), mask, out at) && at == new Vector3(0f, -503f, 0f), "an open cell: its middle");
				floor.transform.position = new Vector3(0f, -501f - 1f, 0f);
				Physics.SyncTransforms();
				bool got = SwimSpot(c, mask, out at);
				Check(ref ok, Physics.CheckSphere(c, 0.4f, mask) && got && at.y < c.y, "a 1.25 m slot under the cell's middle (its middle touches the roof): found " + (got ? "at " + at.y.ToString("0.00") : "none"));
				floor.transform.position = new Vector3(0f, -500.3f - 1f, 0f);
				Physics.SyncTransforms();
				Check(ref ok, !SwimSpot(c, mask, out at), "a 0.55 m slot: shut");
				// (a swimmer's straight line between waypoints: past the block's lip it is blocked, under it clear - Waypoints keeps a
				// corner there, Riverfork's sea cave mouth)
				Check(ref ok, SwimBlocked(new Vector3(-5f, -498.5f, 0f), new Vector3(5f, -500.3f, 0f)) && !SwimBlocked(new Vector3(-5f, -497f, 0f), new Vector3(5f, -497f, 0f)), "a swim line through the block: blocked; over it: clear");
			}
			finally { reachMask = was; UnityEngine.Object.Destroy(roof); UnityEngine.Object.Destroy(floor); }
			if (ok) Log("PASS: reach swim spot"); else Fail("reach swim spot");
			yield break;
		}

		[ConsoleCommand(name: "CIReachSolidUnit", docs: "Dev, anywhere: the reach's 'inside solid' ray (OverFacesUp, 11-08) - a near-vertical wall met from behind over a cave floor, a ceiling above it: open (Riverfork's tunnel join); the same wall with a rock's top met from behind above it: solid; a rock's top alone: solid; a ceiling alone: open")]
		public static void ReachSolidUnitCommand() { StartTest(ReachSolidUnitRoutine()); }

		/// <summary>A one-sided quad (MeshCollider) at c spanning c +- u +- v, turned so a ray up from under c meets it with the
		/// normal's y of the sign given (+: its back, as from inside a rock; -: its front, a ceiling).</summary>
		static GameObject SolidUnitQuad(Vector3 c, Vector3 u, Vector3 v, bool up)
		{
			var go = new GameObject("ReachSolidUnitQuad");
			var m = new Mesh();
			m.vertices = new[] { c - u - v, c + u - v, c + u + v, c - u + v };
			Vector3 n = Vector3.Cross(u, v);
			// (Unity: a triangle 0 1 2 faces along the cross of 1-0 and 2-0 - here of u and v; a first try the other way round met
			// every quad from the wrong side)
			bool frontUp = n.y > 0f;
			m.triangles = frontUp == up ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
			m.RecalculateNormals();
			go.AddComponent<MeshCollider>().sharedMesh = m;
			return go;
		}

		static IEnumerator ReachSolidUnitRoutine()
		{
			bool ok = true;
			int was = reachMask;
			var made = new List<GameObject>();
			try
			{
				int mask = 1 << 0;
				reachMask = mask;
				// (four columns 20 m apart, under y -500; a steep wall (normal 1, 0.12, 0) 3 m over the point)
				Vector3 wn = new Vector3(1f, 0.12f, 0f).normalized;
				Vector3 wu = Vector3.Cross(wn, Vector3.forward).normalized * 2f, wv = Vector3.forward * 2f;
				Vector3 flatU = Vector3.right * 2f, flatV = Vector3.forward * 2f;
				for (int col = 0; col < 4; col++)
				{
					Vector3 b = new Vector3(col * 20f, -510f, 0f);
					if (col < 2) made.Add(SolidUnitQuad(b + Vector3.up * 3f, wu, wv, true));
					// (col 0 and 3: a ceiling (front faces down); col 1 and 2: a rock's top seen from inside (back face up))
					made.Add(SolidUnitQuad(b + Vector3.up * 6f, flatU, flatV, col == 1 || col == 2));
				}
				Physics.SyncTransforms();
				bool bw = Physics.queriesHitBackfaces;
				Physics.queriesHitBackfaces = true;
				RaycastHit h;
				bool wall = Physics.Raycast(new Vector3(0f, -510f, 0f), Vector3.up, out h, 4f, mask, QueryTriggerInteraction.Ignore);
				Physics.queriesHitBackfaces = bw;
				Check(ref ok, wall && h.normal.y > 0.1f && h.normal.y <= 0.3f, "set-up: the wall is met from behind, steep (normal y " + (wall ? h.normal.y.ToString("0.00") : "none") + ")");
				Check(ref ok, !BehindOver(new Vector3(0f, -510f, 0f)), "a steep wall from behind, a ceiling over it: open (a cave join)");
				Check(ref ok, BehindOver(new Vector3(20f, -510f, 0f)), "a steep wall from behind, a rock's top over it: solid");
				Check(ref ok, BehindOver(new Vector3(40f, -510f, 0f)), "a rock's top from behind alone: solid");
				Check(ref ok, !BehindOver(new Vector3(60f, -510f, 0f)), "a ceiling alone: open");
			}
			finally { reachMask = was; foreach (GameObject g in made) UnityEngine.Object.Destroy(g); }
			if (ok) Log("PASS: reach solid unit"); else Fail("reach solid unit");
			yield break;
		}

		[ConsoleCommand(name: "CIReachFallUnit", docs: "Dev, anywhere: the reach's drop check (FallCaught) - a roof whose top is a back face (a cave skin seen from above, Old Mine's wedge, round 4) catches a fall like a floor does; open air below does not")]
		public static void ReachFallUnitCommand()
		{
			bool ok = true;
			int was = reachMask;
			var go = new GameObject("CIReachFallUnit");
			try
			{
				go.transform.position = new Vector3(0f, -500f, 0f);
				var m = new Mesh();
				// (a quad at y 0 whose front faces down: from above only its back face shows)
				m.vertices = new[] { new Vector3(-2, 0, -2), new Vector3(2, 0, -2), new Vector3(2, 0, 2), new Vector3(-2, 0, 2) };
				m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
				m.RecalculateNormals();
				var mc = go.AddComponent<MeshCollider>();
				mc.sharedMesh = m;
				Physics.SyncTransforms();
				reachMask = 1 << go.layer;
				bool down = m.normals[0].y < 0f;
				Check(ref ok, down, "the test roof faces down (normal y " + m.normals[0].y.ToString("0.0") + ")");
				Check(ref ok, FallCaught(new Vector3(0f, -500f, 0f), -497.4f, -502.6f), "a drop through a back-facing roof: caught");
				Check(ref ok, !FallCaught(new Vector3(10f, -500f, 0f), -497.4f, -502.6f), "a drop beside it, open air: not caught");
				m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
				mc.sharedMesh = null; mc.sharedMesh = m;
				Physics.SyncTransforms();
				Check(ref ok, FallCaught(new Vector3(0f, -500f, 0f), -497.4f, -502.6f), "a drop onto a floor facing up: caught");
			}
			finally { reachMask = was; UnityEngine.Object.Destroy(go); }
			if (ok) Log("PASS: reach fall"); else Fail("reach fall");
		}

		[ConsoleCommand(name: "CIReachControls", docs: "Dev, anywhere: PQ11 - with each island measured, the walker also walks straight at walls (a floor beyond a wall too high to step or jump) at the reach speed: none may be got through (a walker slipping through walls would pass what a player can't). CIReachControls on|off")]
		public static void ReachControlsCommand(string[] args)
		{
			reachControls = args == null || args.Length == 0 || args[0] != "off";
			Log("PASS: reach controls " + (reachControls ? "on" : "off"));
		}

		/// <summary>Something solid on the line between two grid spots, from either end (a mesh is hit from its front only).</summary>
		static bool Between(Vector3 a, Vector3 b, int mask)
		{
			return Physics.Linecast(a, b, mask, QueryTriggerInteraction.Ignore) || Physics.Linecast(b, a, mask, QueryTriggerInteraction.Ignore);
		}
		/// <summary>Between, and no room for the swimmer's body on the way: a ray slips through a slit a body cannot - Hadal's
		/// grid swam into its houses by the gap at the roof's edge and the walker stuck on the roof, 'block quad', bugfix 11-25.</summary>
		static bool SwimBetween(Vector3 a, Vector3 b, int mask)
		{
			if (Between(a, b, mask)) return true;
			Vector3 d = b - a; float len = d.magnitude;
			if (len < 0.01f) return false;
			RaycastHit h;
			return Physics.SphereCast(a, SwimBody, d / len, out h, len, mask, QueryTriggerInteraction.Ignore) || Physics.SphereCast(b, SwimBody, -d / len, out h, len, mask, QueryTriggerInteraction.Ignore);
		}
		const float SwimBody = 0.2f;
		/// <summary>The swimmer's whole body in the way from a to b: Raft's controller stays upright under water (h 1.7, r 0.2), its
		/// middle at the swim cell's - a 0.2 m ball passed under a door's lintel where the body's top could not, and the walker stuck
		/// on the lip in front of house 3's door, Hadal bugfix 11-26. Cast both ways (a body overlapping a collider at the start
		/// does not see it).</summary>
		static bool SwimBodyBlocked(Vector3 a, Vector3 b, int mask)
		{
			Vector3 d = b - a; float len = d.magnitude;
			if (len < 0.01f) return false;
			Vector3 up = Vector3.up * (RHeight * 0.5f - SwimBody);
			return Physics.CapsuleCast(a - up, a + up, SwimBody, d / len, len, mask, QueryTriggerInteraction.Ignore) ||
				Physics.CapsuleCast(b - up, b + up, SwimBody, -d / len, len, mask, QueryTriggerInteraction.Ignore);
		}
		[ConsoleCommand(name: "CIReachSwim", docs: "Dev, world: PQ11 - the reach walker's swim alone: the player put in the sea 150 m off, Walk held 3 s with and without a look pitch; the metres moved")]
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

		[ConsoleCommand(name: "CIReachColumns", docs: "Dev, world (host): cave check 11-08 - what the reach grid makes of n columns along a line of an island (metres from the island's position): each surface a ray down meets (height, normal, what) and whether it is a floor (Stands, not InsideSolid), and the swim cells under the sea from -1.5 m down (o free, n free nudged off the middle (SwimSpot), s the swimmer's sphere touches, i inside solid, x both). CIReachColumns <island> x0 z0 x1 z1 n")]
		public static void ReachColumnsCommand(string[] args)
		{
			if (args == null || args.Length < 6) { Fail("CIReachColumns <island> x0 z0 x1 z1 n"); return; }
			float[] v = args.Skip(args.Length - 5).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
			StartTest(ReachColumnsRoutine(string.Join(" ", args.Take(args.Length - 5).ToArray()), v[0], v[1], v[2], v[3], Mathf.Clamp((int)v[4], 1, 60)));
		}

		/// <summary>The island by its name, or brought next to the raft when it is not in the world (CIReach and cavecheckrun take
		/// theirs away after them); got(null) after a Fail when it can't be had. CIReachColumns, CIReachHeights.</summary>
		static IEnumerator ReachFindOrBring(string island, string who, Action<IslandWorldState.Entry> got)
		{
			got(null);
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase) && x.Root != null);
			if (e != null) { got(e); yield break; }
			// (brought next to the raft when it is not in the world: cavecheckrun takes its island away after it)
			// (brought as CIReach brings it: a half-loaded entry of the same name taken away first, the island's own
			// elevation, a second try, settled before the columns - right after CIRecipe the first columns of sc7 and
			// th1 failed "didn't come" while CIReach a minute later brought it fine, bugfix 11-26)
			yield return EnsureAlive();
			var made = new List<IslandWorldState.Entry>();
			for (int attempt = 0; attempt < 2 && (e == null || e.Root == null); attempt++)
			{
				var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
				if (left.Count > 0) yield return RemoveIslandsAndWait(left);
				Vector3? spot = CaveGapsLookIslandMissing(island) ? null : ScSpot(island, 400f, IslandSpawner.ElevationOf(island));
				if (!spot.HasValue) { Fail(who + ": '" + island + "' is not here and can't be brought"); yield break; }
				made.Clear();
				yield return ScBring(island, spot.Value, made);
				e = made.LastOrDefault();
				if (e == null || e.Root == null) yield return new WaitForSeconds(3f);
			}
			if (e == null || e.Root == null) { Fail(who + ": " + island + " didn't come"); yield break; }
			yield return WaitIslandSettled(e, 0.5f, 3f);
			PlayerMove.To(RAPI.GetLocalPlayer(), e.Position + new Vector3(0f, 2f, -LandRadiusOf(e) - 20f));
			yield return new WaitForSeconds(1f);
			got(e);
		}

		static IEnumerator ReachColumnsRoutine(string island, float x0, float z0, float x1, float z1, int n)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CIReachColumns: host, in a world"); yield break; }
			IslandWorldState.Entry e = null;
			yield return ReachFindOrBring(island, "CIReachColumns", x => e = x);
			if (e == null) yield break;
			Physics.SyncTransforms();
			if (reachMask == 0) reachMask = ~0;
			for (int c = 0; c < n; c++)
			{
				float t = n == 1 ? 0f : (float)c / (n - 1);
				float x = Mathf.Lerp(x0, x1, t), z = Mathf.Lerp(z0, z1, t);
				Vector3 from = e.Position + new Vector3(x, 60f, z);
				var line = new System.Text.StringBuilder("  col " + x.ToString("F1") + " " + z.ToString("F1") + ":");
				RaycastHit h; int hits = 0;
				float lowHit = float.MaxValue, topHit = float.MinValue;
				while (hits < 14 && Physics.Raycast(from, Vector3.down, out h, from.y - (e.Position.y - 40f), reachMask, QueryTriggerInteraction.Ignore))
				{
					hits++;
					lowHit = Mathf.Min(lowHit, h.point.y); topHit = Mathf.Max(topHit, h.point.y);
					string what = h.collider is TerrainCollider ? "terrain" : BlockerName(h.collider);
					if (what.Length > 14) what = what.Substring(0, 14);
					standWhyOn = true; standWhy = "";
					bool st = Stands(h.point), ins = InsideSolid(h.point + Vector3.up * 0.5f);
					standWhyOn = false;
					line.Append(" " + (h.point.y - e.Position.y).ToString("F1") + "/n" + h.normal.y.ToString("F2") + "/" + what + (h.normal.y >= 0.64f && st && !ins ? "=FLOOR" : (!st ? "-nostand(" + standWhy + ")" : "") + (ins ? "-insolid(" + solidWhy + ")" : "")));
					from = h.point + Vector3.down * 0.03f;
				}
				var sw = new System.Text.StringBuilder();
				// (the grid's own 1 m swim cells, from the sea's surface down to the column's lowest surface: a sunken island's
				// ground lies 45 m down - Hadal Commons, 11-24; the first one shown is the one 2 m over its top surface)
				float yTop = RSea - 1.5f;
				if (topHit != float.MinValue && topHit + 2f < yTop) yTop -= Mathf.Floor(yTop - (topHit + 2f));
				if (yTop < RSea - 1.5f) sw.Append("(from " + (yTop - e.Position.y).ToString("F1") + ") ");
				for (float y = yTop; y > (lowHit != float.MaxValue ? Mathf.Max(lowHit - 0.5f, RSea - 80f) : RSea - 12f); y -= 1f)
				{
					Vector3 p = new Vector3(e.Position.x + x, y, e.Position.z + z);
					bool hit = Physics.CheckSphere(p, 0.4f, reachMask, QueryTriggerInteraction.Ignore), ins = InsideSolid(p);
					Vector3 nudged;
					sw.Append(!hit && !ins ? "o" : SwimSpot(p, reachMask, out nudged) ? "n" : hit && ins ? "x" : hit ? "s" : "i");
				}
				Log(line.ToString() + " | swim -1.5..: " + sw);
			}
			Log("PASS: reach columns");
		}

		static IEnumerator ReachHeightsRoutine(string island, float cx, float cz, float half, float step)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CIReachHeights: host, in a world"); yield break; }
			// (brought as CIReachColumns brings it: CIReach takes its island away after it, so a CIReachHeights after a CIReach
			// always failed "is not here" - Sealkeeper Spire sk6/sk7 11-27)
			IslandWorldState.Entry e = null;
			yield return ReachFindOrBring(island, "CIReachHeights", x => e = x);
			if (e == null) yield break;
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
					// (bugfix 11-26: b->a too, and both with back faces on - a one-sided skin faces one way only)
					var back = new System.Text.StringBuilder("  rays b->a:");
					var backs = new System.Text.StringBuilder("  rays a->b backs on:");
					bool bwas = Physics.queriesHitBackfaces;
					try
					{
						for (float hy = 0.05f; hy < 1.8f; hy += 0.15f)
						{
							RaycastHit rh;
							Physics.queriesHitBackfaces = false;
							back.Append(" " + hy.ToString("F2") + (Physics.Raycast(fb + Vector3.up * hy, -fd.normalized, out rh, fd.magnitude, reachMask, QueryTriggerInteraction.Ignore) ? "=" + BlockerName(rh.collider) + "@" + rh.distance.ToString("F2") + " n" + rh.normal.ToString("F2") : "=-"));
							Physics.queriesHitBackfaces = true;
							backs.Append(" " + hy.ToString("F2") + (Physics.Raycast(fa + Vector3.up * hy, fd.normalized, out rh, fd.magnitude, reachMask, QueryTriggerInteraction.Ignore) ? "=" + BlockerName(rh.collider) + "@" + rh.distance.ToString("F2") + " n" + rh.normal.ToString("F2") : "=-"));
						}
					}
					finally { Physics.queriesHitBackfaces = bwas; }
					Log(back.ToString());
					Log(backs.ToString());
					Log("  grid: blocked .45 " + Blocked(a, b, Mathf.Max(a.y, b.y) + 0.45f) + " 1.0 " + Blocked(a, b, Mathf.Max(a.y, b.y) + 1.0f) + " 1.55 " + Blocked(a, b, Mathf.Max(a.y, b.y) + 1.55f) + " swept " + Swept(a, b, Mathf.Max(a.y, b.y)));
					// (what rules a spot in or out of the grid at each end: a floor stands, a swimmer's 0.4 m sphere is free, not inside a rock)
					foreach (Vector3 q in new[] { a, b })
						Log("  grid at " + (q - e.Position).ToString("F1") + ": stands " + Stands(q) + ", inside solid " + InsideSolid(q + Vector3.up * 0.5f) + ", swim sphere free " + !Physics.CheckSphere(q + Vector3.up * 0.9f, 0.4f, reachMask, QueryTriggerInteraction.Ignore));
					{ string why; bool sw = SlopeWalk(a, b, out why); Log("  grid: slope walk " + sw + " (" + why + ")"); }
					{
						// (from the sea: the shore rule's walk up from the sea floor under a, as a swimmer climbs it)
						RaycastHit bh; float bed = Physics.Raycast(new Vector3(a.x, RSea - 0.1f, a.z), Vector3.down, out bh, 3f, reachMask, QueryTriggerInteraction.Ignore) ? bh.point.y : RSea - 3f;
						string why; bool sw = SlopeWalk(new Vector3(a.x, bed, a.z), b, out why, RSea + RSwimFeet);
						Log("  grid: sea floor under a at " + (bed - e.Position.y).ToString("F2") + ", swimmer's walk out " + sw + " (" + why + ")");
					}
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

		/// <summary>How far off a ladder a spot may be and still take hold of it: 1.0 m on foot; a swimmer 0.5 m more - its spots
		/// are 1 m cells' middles and it swims the rest of the way, so a ladder at the sea's edge 1.1 m from the nearest cell
		/// middle was "nothing can take hold of it" (Old Mine's barge ladder, round 5, 11-05).</summary>
		public static float LadderHandReach(int kind) { return kind == 1 || kind == 2 ? 1.5f : 1.0f; }

		/// <summary>" (nearest swim spot x m off at x z h)" - where the sea's grid comes nearest a ladder no one can grab.</summary>
		static string LadderNearestSwim(ReachGrid g, Bounds lb, IslandWorldState.Entry e)
		{
			float best = float.MaxValue; int bk = -1;
			for (int k = 0; k < g.Pos.Count; k++)
			{
				if (g.Kind[k] != 1 && g.Kind[k] != 2) continue;
				Vector3 p = g.Pos[k];
				Vector3 f = lb.ClosestPoint(new Vector3(p.x, lb.center.y, p.z));
				float d = new Vector2(f.x - p.x, f.z - p.z).magnitude;
				if (d < best) { best = d; bk = k; }
			}
			string sides = "";
			foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
			{
				Vector3 c = lb.center + dir * (Vector3.Scale(lb.extents, dir).magnitude + 0.9f);
				var hits = Physics.RaycastAll(new Vector3(c.x, lb.max.y + 30f, c.z), Vector3.down, 80f, reachMask, QueryTriggerInteraction.Ignore).Where(h => h.point.y > RSea - 1.1f).OrderByDescending(h => h.point.y).Select(h => h.collider.name + "@" + (h.point.y - e.Position.y).ToString("F1")).ToArray();
				Collider[] cap = Physics.OverlapCapsule(new Vector3(c.x, RSea - 1.1f, c.z), new Vector3(c.x, RSea + 0.3f, c.z), RRadius, reachMask, QueryTriggerInteraction.Ignore);
				sides += " " + (dir.x + dir.z * 2).ToString("F0") + ":" + (hits.Length > 0 ? "over " + string.Join(",", hits) : "") + (InsideSolid(new Vector3(c.x, RSea - 1.1f, c.z)) ? " inside" : "") + (cap.Length > 0 ? " capsule " + string.Join(",", cap.Select(x => x.name).ToArray()) : "") + ";";
			}
			if (bk < 0) return " (no swim spots at all;" + sides + ")";
			Vector3 q = g.Pos[bk] - e.Position;
			return " (nearest " + (g.Kind[bk] == 1 ? "swim" : "dive") + " spot " + best.ToString("F1") + " m off, at h=" + q.y.ToString("F1") + "; 0.9 m out" + sides + ")";
		}

		/// <summary>Targets the reach grid can't judge (a hop course: it has no jumps across gaps), from the island's
		/// "island reach.byplay=name;name" line - each one counts when the island's own play test goes to it (zone/open/reach
		/// &lt;name&gt;), and is "CHECKED BY PLAY" instead of a fail; one the play never visits stays a fail (Old Mine's slag stacks 11-05).</summary>
		static HashSet<string> ReachByPlay(IslandWorldState.Entry e, string island)
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string v;
			IDictionary<string, string> props = IslandCache.PropsOf(e);
			if (props == null || !props.TryGetValue("reach.byplay", out v) || string.IsNullOrEmpty(v)) return set;
			foreach (string n in v.Split(';')) if (n.Trim().Length > 0) set.Add(n.Trim());
			return set;
		}

		/// <summary>"island reach.sealed=lid:name;name" - targets behind a lid that only a gated use (if./else.: a quest step) takes
		/// away. While the lid is there (a fresh island) they are "SEALED" instead of a fail - the reach after the island's play checks
		/// them; with it gone they count as any other. Nothing is excused unless the lid stands and a gated on.use hides it.</summary>
		static string ReachSealed(IslandWorldState.Entry e, out HashSet<string> names, out Bounds lidB)
		{
			names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); lidB = new Bounds();
			string v;
			IDictionary<string, string> props = IslandCache.PropsOf(e);
			if (props == null || !props.TryGetValue("reach.sealed", out v) || string.IsNullOrEmpty(v)) return null;
			int c = v.IndexOf(':');
			if (c <= 0) return null;
			string lid = v.Substring(0, c).Trim(), by = null;
			GameObject lidGo = null;
			foreach (IslandObjectRef ir in e.Root.GetComponentsInChildren<IslandObjectRef>(true))
			{
				if (ir.Name == lid) { if (ir.gameObject.activeInHierarchy) lidGo = ir.gameObject; continue; }
				string onUse;
				if (!ir.Props.TryGetValue("on.use", out onUse) || !ir.Props.Keys.Any(k => k.StartsWith("if.") || k.StartsWith("else."))) continue;
				if (ObjAction.ParseLines(onUse.Replace("\\n", "\n")).Any(a => a.Verb == "hide" && a.Target == lid)) by = ir.Name.Length > 0 ? ir.Name : ir.ObjectName;
			}
			if (lidGo == null || by == null) return null;
			bool any = false;
			foreach (Collider col in lidGo.GetComponentsInChildren<Collider>(false))
			{
				if (col.isTrigger) continue;
				if (!any) { lidB = col.bounds; any = true; } else lidB.Encapsulate(col.bounds);
			}
			if (!any) return null;
			foreach (string n in v.Substring(c + 1).Split(';')) if (n.Trim().Length > 0) names.Add(n.Trim());
			return "SEALED behind " + lid + " until " + by + "'s gated use takes it away (a quest step) - the reach after the play checks it";
		}

		/// <summary>"CHECKED BY PLAY (file)" for a target listed as checked by play that the island's play goes to, null when none does (a plain fail)
		/// or it isn't listed.</summary>
		static string ByPlayNote(HashSet<string> byPlay, ReachTarget t)
		{
			if (byPlay == null || !byPlay.Contains(t.Name)) return null;
			string file = ByPlayFile(t.Name);
			return file != null ? "CHECKED BY PLAY (" + file + ": hops the grid can't jump)" : null;
		}

		/// <summary>The play test (file name) with a "zone/open/reach &lt;name&gt;" line, or null.</summary>
		public static string ByPlayFile(string target, string verb = null)
		{
			try
			{
				foreach (string f in Directory.GetFiles(RecipeFolder, "*.play"))
					foreach (string l in File.ReadAllLines(f))
					{
						string[] w = l.Trim().Split(new[] { ' ' }, 2);
						if (w.Length == 2 && (verb != null ? w[0] == verb : (w[0] == "zone" || w[0] == "open" || w[0] == "reach" || w[0] == "read")) &&string.Equals(w[1].Trim(), target, StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(f);
					}
			}
			catch (Exception) { }
			return null;
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

		static int dbgShoreHigh, dbgShoreLow, dbgShoreBlocked, dbgShoreOk; static int dbgOutBlocked, dbgOutFall, dbgOutOk, dbgWetRoof, dbgFloatOut, dbgLadderDrop;
		// (the island's ladders' climb colliders, for the drop rule in TryMove)
		static List<Bounds> reachDropLadders = new List<Bounds>();
		const float RSea = 0f, RStepUp = 0.55f, RJump = 1.1f, RDrop = 12f, RSwimFeet = -1.3f, RUse = 2.5f, RRadius = 0.3f, RHeight = 1.75f; // (RTop: the controller's top with its 0.08 m skin - a den mouth's sloping roof 1.68 m up stopped the walker where the grid stood, bugfix 11-26 bl8p Brinelab)
		const float RTop = RHeight + 0.05f;

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
			// (each climb collider of a stacked chain on its own: the walker heads for the one at its height)
			public Dictionary<int, List<Bounds>> LadderParts = new Dictionary<int, List<Bounds>>();
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
			foreach (List<Bounds> parts in g.LadderParts.Values) for (int j = 0; j < parts.Count; j++) { Bounds b = parts[j]; b.center -= s; parts[j] = b; }
			if (shiftTargets != null) foreach (ReachTarget t in shiftTargets) t.B.center -= s;
			Log("[REACH] world shift " + s + " followed");
		}

		/// <summary>The reach-and-escape measure of one island in the world, as it stands now. done(n): problems found.</summary>
		/// <summary>(bugfix 11-27) The island BlockerName measures from while a reach runs: from Play's island the reach logs gave
		/// a blocker on Lumen Shelf at z -764 (lu7).</summary>
		internal static IslandWorldState.Entry nameEntry;

		static IEnumerator ReachAnalyze(IslandWorldState.Entry e, Vector2 offset, string state, Action<int> done)
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			nameEntry = e;
			string name = e.HostName;
			var report = new List<string>();
			int problems = 0;
			reachStuckAt.Clear(); reachStuckN.Clear(); reachBlockedAt.Clear(); reachBlockedWhy.Clear(); reachHoles.Clear();
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
			// (a plain gate an islander's answer hides (on.choiceN ... hide|gate, the islander not gated by if./else.): Thalassa
			// Mooring's sea gate - any player asks Harbourmistress Elaine and it is gone; the grid saw it shut and the dry room
			// and its charter behind it as never reached, s6u 11-26. A gated islander's answers stay: only the play opens them)
			{
				var talked = new HashSet<string>();
				foreach (IslandObjectRef ir in e.Root.GetComponentsInChildren<IslandObjectRef>(false))
				{
					if (!ir.Props.ContainsKey("talk.text") || ir.Props.Keys.Any(k => k.StartsWith("if.") || k.StartsWith("else."))) continue;
					foreach (var kv in ir.Props)
					{
						if (!kv.Key.StartsWith("on.choice", StringComparison.Ordinal) || kv.Value.IndexOf("hide", StringComparison.Ordinal) < 0) continue;
						foreach (var act in ObjAction.ParseLines(kv.Value.Replace("\\n", "\n")))
							if (act.Verb == "hide" && act.Target.Length > 0 && !act.Target.Equals("self", StringComparison.OrdinalIgnoreCase)) talked.Add(act.Target);
					}
				}
				int gone = 0;
				if (talked.Count > 0)
					foreach (IslandObjectRef ir in e.Root.GetComponentsInChildren<IslandObjectRef>(false))
						if (ir.Name != null && talked.Contains(ir.Name)) { ir.gameObject.SetActive(false); gone++; }
				if (gone > 0) report.Add("REACH " + name + " | talked-away | - | NOTE " + gone + " object(s) an islander's answer takes away (" + string.Join(", ", talked.ToArray()) + ") taken away as a player asking would");
			}
			// (a store sealed until a quest step opens it: Caisson Yard's drowned store, capped until the crane key's lever drops the
			// weight - fresh, its chest, note, air pocket and ladder all failed "can't get there", while the reach after the play
			// passed them, cy4 11-26: island reach.sealed=lid:target;target)
			HashSet<string> sealedNames; Bounds sealedLid;
			string sealedNote = ReachSealed(e, out sealedNames, out sealedLid);
			if (sealedNote != null) report.Add("REACH " + name + " | sealed | - | NOTE " + sealedNote + ": " + string.Join(", ", sealedNames.ToArray()));
			// (Raft's machete vines with nothing of the island's on them (a cave kit's gate): cut, as a player with the machete
			// does - the grid saw them as a wall, so Tracker's Rise's cut and its cache were "can't get there" and both mouths
			// traps, 11-09. A gated one (if./else./on.use) stays as the play left it)
			{
				int cut = 0;
				// (every vine carries the machete check by default (ObjectProps.Defaults) - only a use line other than that
				// default gates it; the default ones were all skipped, and the grid saw the cut's vines as walls, 11-10)
				Dictionary<string, string> vdef = ObjectProps.Defaults(ContentCatalog.MacheteVines, false);
				foreach (IslandObjectRef ir in e.Root.GetComponentsInChildren<IslandObjectRef>(false))
				{
					if (ir.ObjectName != ContentCatalog.MacheteVines) continue;
					// (a use line that only hides the vine itself (and says something) is the cut too: Brinelab's atrium vines,
					// "hide|atriumvines" plus a message - the grid saw them as a wall at the stairwell hatch and the walker,
					// coming down the ladder past them, stuck on their box, 11-26 blx)
					string vown = ir.Name ?? "";
					if (ir.Props.Any(kv => (kv.Key.StartsWith("if.") || kv.Key.StartsWith("else.") || kv.Key.StartsWith("on.") || kv.Key.StartsWith("beh."))
						&& !(vdef.ContainsKey(kv.Key) && vdef[kv.Key] == kv.Value)
						&& !(kv.Key == BehaviourProps.EventKey("use") && VineUseIsCut(kv.Value, vown)))) continue;
					ir.gameObject.SetActive(false);
					cut++;
				}
				if (cut > 0) report.Add("REACH " + name + " | vines | - | NOTE " + cut + " machete vine(s) cut as a player with the machete would");
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
					if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { hbProg++; yield return null; slice.Reset(); slice.Start(); }
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
						Vector3 p;
						if (SwimSpot(new Vector3(x, y, z), mask, out p))
							g.Vox[VoxKey(i / vstep, j / vstep, k)] = g.Add(p, 2, -1);
						if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { hbProg++; yield return null; slice.Reset(); slice.Start(); }
					}
				}
			}
			Log("  reach grid '" + name + "': " + g.NX + "x" + g.NZ + " cells of " + g.Cell.ToString("F2") + " m, " + g.Pos.Count + " spots (" + sw.ElapsedMilliseconds + " ms)");

			// (each slice moves the heartbeat's prog on: Biome Seven's grid and links took over 20 min on box3, the heartbeat read
			// step=0 prog=0 all that time and the runner called it stalled and stopped the phase, bugfix 11-26 s6t)
			// moves between neighbouring columns
			int[] di = { 1, 0, 1, 1 }, dj = { 0, 1, 1, -1 };
			reachDropLadders = e.Root.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && c.name.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0).Select(c => c.bounds).ToList();
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
					if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { hbProg++; yield return null; slice.Reset(); slice.Start(); }
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
				// 0.2 m slab half way between cells 1 m apart - Hadal Commons' houses were swum into through their roofs, PQ11 10-28; and a ray
				// still slipped by the roof's edge where a body cannot: the body's 0.2 m is swept too, bugfix 11-25)
				if (g.Vox.TryGetValue(VoxKey(vi + 1, vj, vk), out other) && !SwimBetween(p, g.Pos[other], mask) && !SwimBodyBlocked(p, g.Pos[other], mask)) { g.Link(kv.Value, other); g.Link(other, kv.Value); }
				if (g.Vox.TryGetValue(VoxKey(vi, vj + 1, vk), out other) && !SwimBetween(p, g.Pos[other], mask) && !SwimBodyBlocked(p, g.Pos[other], mask)) { g.Link(kv.Value, other); g.Link(other, kv.Value); }
				if (g.Vox.TryGetValue(VoxKey(vi, vj, vk + 1), out other) && !SwimBetween(p, g.Pos[other], mask)) { g.Link(kv.Value, other); g.Link(other, kv.Value); }
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
				if (++work % 64 == 0 && slice.ElapsedMilliseconds >= 25) { hbProg++; yield return null; slice.Reset(); slice.Start(); }
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
				g.LadderParts[node] = ch.Select(c => c.bounds).ToList();
				{
					// (the side a player climbs it from: its front, or its back when only the back has room)
					Vector3 lf = LadderRoot(ch[0]).forward; lf.y = 0f;
					// (no room on either side, a ladder against a wall in a hatch's corner: the side its climb collider stands out to, where a
					// player touches it - the free-space count picked the wall's far side, outside the tower, and the swimmer waited there
					// behind the wall 41 s: Brinelab's flooded storey ladder, Data drive 2, bugfix 11-26 bl8q)
					if (lf.sqrMagnitude > 0.01f)
					{
						lf.Normalize();
						bool rf = LadderRoom(lb, lf), rb = !rf && LadderRoom(lb, -lf);
						float off = Vector3.Dot(ch[0].bounds.center - LadderRoot(ch[0]).position, lf);
						g.LadderF[node] = rf ? lf : rb ? -lf : Mathf.Abs(off) > 0.1f ? (off > 0f ? lf : -lf) : LadderFree(lb, lf) >= LadderFree(lb, -lf) ? lf : -lf;
					}
				}
				// (over its top end only when that end is open: a slab over a drowned building's roof hatch (Caisson Yard's cap, gone only
				// when the crane key's lever is pulled) counted as the ladder's top, and the walker stood on it and never took hold, PQ11 s6t 11-26)
				bool topOpen;
				{
					Bounds tb = ch.Select(c => c.bounds).OrderByDescending(cbx => cbx.max.y).First();
					Vector3 tf = g.LadderF.ContainsKey(node) ? g.LadderF[node] * 0.3f : Vector3.zero;
					Vector3 tc = new Vector3(tb.center.x, lb.max.y - 0.3f, tb.center.z);
					topOpen = new[] { tc, tc + tf, tc - tf }.Any(tq => LadderClear(tq, tq + Vector3.up * 1.3f, ch));
				}
				int grabs = 0, tops = 0;
				string why = null; float whyHd = float.MaxValue;
				for (int k = 0; k < node; k++)
				{
					Vector3 p = g.Pos[k];
					Vector3 flat = lb.ClosestPoint(new Vector3(p.x, lb.center.y, p.z));
					float hd = new Vector2(flat.x - p.x, flat.z - p.z).magnitude;
					if (hd > 1.6f) continue;
					float feet = g.Kind[k] == 2 ? p.y - 0.9f : p.y;
					// (the top: a floor at the ladder's top beside it - where the climb ends)
					// (or over its top end: a ladder up through a hole in a landing - the line to its middle crossed the landing, PQ11 10-27)
					// (and floor all the way from the ladder to it: Lumen Shelf's spar deck tile stood 1 m past the open shaft cell the
					// ladder's top is in - the walker stepped off into the shaft's water and swam up into the tile's underside, lu9 11-27)
					if (g.Kind[k] == 0 && feet >= lb.max.y - 1.0f && feet <= lb.max.y + 1.3f && LadderTopFloored(lb, p) && (LadderClear(p + Vector3.up * 1.0f, flat, ch) || topOpen && LadderClear(p + Vector3.up * 1.0f, new Vector3(flat.x, lb.max.y + 0.6f, flat.z), ch))) { g.Link(node, k); g.Link(k, node); tops++; continue; }
					// (the foot, and any spot beside it up its length: a hand reaches it with nothing solid in between)
					if (hd <= LadderHandReach(g.Kind[k]) && feet >= lb.min.y - 1.8f && feet <= lb.max.y - 1.0f)
					{
						Vector3 eye = g.Kind[k] == 0 ? p + Vector3.up * 1.3f : p + Vector3.up * 0.2f;
						Vector3 at = lb.ClosestPoint(new Vector3(eye.x, Mathf.Clamp(eye.y, lb.min.y + 0.2f, lb.max.y - 0.2f), eye.z));
						// (and to its middle: a spot behind the wall it is fixed to saw the climb box's back face poke through the wall
						// and counted as a foot spot - the walker stood outside the mast pressing on the wall, hw_mast PQ11 10-28)
						bool c1 = LadderClear(eye, at, ch), c2 = c1 && LadderClear(eye, new Vector3(lb.center.x, at.y, lb.center.z) + (g.LadderF.ContainsKey(node) ? g.LadderF[node] * 0.25f : Vector3.zero), ch);
						if (c1 && c2) { g.Link(k, node); g.Link(node, k); grabs++; }
						else if (hd < whyHd) { whyHd = hd; why = "nearest foot spot " + (g.Kind[k] == 0 ? "on foot" : g.Kind[k] == 1 ? "swimming" : "diving") + " " + hd.ToString("F1") + " m off, feet " + (feet - e.Position.y).ToString("F1") + ": " + (c1 ? "the line to its middle is blocked" : "the line to the ladder is blocked"); }
					}
					else if (hd < whyHd && feet < lb.min.y - 1.8f) { whyHd = hd; why = "nearest spot " + hd.ToString("F1") + " m off has its feet " + (lb.min.y - feet).ToString("F1") + " m under the ladder's foot"; }
					else if (hd < whyHd && hd > LadderHandReach(g.Kind[k])) { whyHd = hd; why = "nearest spot " + hd.ToString("F1") + " m off (a hand reaches " + LadderHandReach(g.Kind[k]).ToString("F1") + " m)"; }
				}
				Vector3 lr = Rel(e, lb.center, offset);
				string where = "ladder " + LadderName(ch[0], e) + " at " + lr.x.ToString("F1") + " " + lr.z.ToString("F1") + " h=" + (lb.min.y - e.Position.y).ToString("F1") + ".." + (lb.max.y - e.Position.y).ToString("F1");
				string facing = LadderFacing(ch, lb);
				// (a ladder up to the sealed store's lid: its top is under the lid until the quest step takes it away)
				if (grabs > 0 && tops == 0 && sealedNote != null && lb.max.y >= sealedLid.min.y - 2.5f && lb.max.y <= sealedLid.max.y + 0.5f
					&& lb.max.x >= sealedLid.min.x - 1.5f && lb.min.x <= sealedLid.max.x + 1.5f && lb.max.z >= sealedLid.min.z - 1.5f && lb.min.z <= sealedLid.max.z + 1.5f)
				{
					report.Add("REACH " + name + " | " + where + " | - | " + sealedNote + " - the grid: no floor to step off onto at its top (" + facing + ")");
					continue;
				}
				if (grabs == 0 || tops == 0)
				{
					problems++;
					report.Add("REACH " + name + " | " + where + " | - | FAIL " + (grabs == 0 ? "nothing can take hold of it from below" + (why != null ? " - " + why : " - no spot within 1.6 m") + LadderNearestSwim(g, lb, e) : "") + (grabs == 0 && tops == 0 ? ", " : "") + (tops == 0 ? "no floor to step off onto at its top" : "") + " (" + facing + ")");
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
			var openSea = new List<int>();
			if (reachSky)
			{
				openSea.AddRange(sea);
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
				if (reachTrace != null) ReachTraceLog(e, g, fwd, offset);
				int seaSwim = sea.Count(k => g.Kind[k] == 1);
				report.Add("REACH " + name + " | shore links | - | NOTE out of the water: ok " + dbgShoreOk + ", too high " + dbgShoreHigh + ", too low " + dbgShoreLow + ", blocked " + dbgShoreBlocked);
				report.Add("REACH " + name + " | shore links | - | NOTE into the water: ok " + dbgOutOk + ", blocked " + dbgOutBlocked + ", fall caught " + dbgOutFall + "; drops down a ladder's shaft left out " + dbgLadderDrop + "; under water, floor to floor through a roof/floor " + dbgWetRoof + "; afloat over a floor, up too high for a swimmer " + dbgFloatOut);
				dbgShoreOk = dbgShoreHigh = dbgShoreLow = dbgShoreBlocked = 0;
				dbgOutOk = dbgOutBlocked = dbgOutFall = dbgWetRoof = dbgFloatOut = dbgLadderDrop = 0;
				report.Add("REACH " + name + " | walk-in | - | NOTE sea " + sea.Count + " spots (" + seaSwim + " swim), grid " + g.NX + "x" + g.NZ + "; floor reached per side n/e/s/w " + string.Join("/", gotS.Select(x => x.ToString()).ToArray()));
			}
			var back = new bool[g.Pos.Count];
			var backParent = new int[g.Pos.Count];
			Flood(g, sea, g.In, back, backParent);
			// (a trap is a place with no way out at all: under a flying island the open sea is one too - a swimmer swims off to
			// the raft. Pendulum's whole sea under the island, 263062 spots, was a 'roofed trap' at h=-12, isl1126_f 11-27)
			bool[] outAny = back;
			if (reachSky && openSea.Count > 0)
			{
				outAny = new bool[g.Pos.Count];
				Flood(g, sea.Concat(openSea).ToList(), g.In, outAny, new int[g.Pos.Count]);
			}
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
			HashSet<string> byPlay = ReachByPlay(e, name);
			foreach (ReachTarget t in targets)
			{
				hbWhat = "reach " + name; hbStep = hbT++; hbProg++;
				var use = useOf[t];
				Vector3 tr = relOf[t];
				string head = "REACH " + name + " | " + t.Kind + " '" + t.Name + "' at " + tr.x.ToString("F1") + " " + tr.z.ToString("F1") + " h=" + tr.y.ToString("F1") + " | " + (t.Steps.Length > 0 ? t.Steps : "-") + " | ";
				if (reachOnly != "" && !reachOnly.Split('|').Any(o => o.Trim() != "" && (t.Name ?? "").IndexOf(o.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)) { report.Add(head + "skipped (CIReachOnly)"); continue; }
				// (a cage the play opened is hidden (Survivors.cs: open = gone for everyone), its model off and its bounds a 0.3 m box
				// at its root - "no spot" for Teo's and Oda's cages after their quests freed them, PQ11 10-28: not checkable now)
				if (use.Count == 0 && t.Kind == "cage")
				{
					SurvivorCage sc = t.Root.GetComponentInChildren<SurvivorCage>(false);
					if (sc != null && sc.IsOpen) { report.Add(head + "NOTE opened in the play (the cage is gone now): not checked"); continue; }
				}
				string playNote = ByPlayNote(byPlay, t) ?? (sealedNote != null && sealedNames.Contains(t.Name ?? "") ? sealedNote : null);
				if (use.Count == 0 && playNote != null) { report.Add(head + playNote + " - the grid: no spot to use it from (" + WhyNoSpot(t) + ")"); continue; }
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
					string blockedOn = reachSpeed > 0 ? ReachRouteBlocked(g, use, fwd, back, parent) : null;
					if (blockedOn != null) walked = "FAIL not walked: " + blockedOn + " - every way in passes there";
					else if (reachSpeed > 0) yield return ReachWalkTarget(e, g, t, use, fwd, back, parent, backParent, sideNames, r => walked = r);
					if (walked == null) report.Add(head + "PASS grid only, not walked (" + grid + ")");
					else if (walked.StartsWith("PASS")) report.Add(head + walked + " (" + grid + ")");
					// (a long ladder the walker sticks on at 1x but the island's play climbs: the play is the check - w02 11-10,
					// Ranger's training tower and Riverfork's crane, 20 m ladders)
					else if (playNote != null) report.Add(head + playNote + " - the walker: " + walked + " (the grid found a way: " + grid + ")");
					else { problems++; report.Add(head + walked + " (the grid found a way: " + grid + ")"); }
				}
				else if (playNote != null) report.Add(head + playNote + " - the grid: " + string.Join("; ", bad.Distinct().ToArray()) + NearestReached(e, g, fwd, use, offset));
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
			for (int k = 0; k < g.Pos.Count; k++) trapped[k] = !outAny[k] &&(fwd[0][k] || fwd[1][k] || fwd[2][k] || fwd[3][k]) && g.Kind[k] != 3;
			var seen = new bool[g.Pos.Count];
			int traps = 0;
			Bounds trapIsl;
			bool haveTb = ReachBounds(e.Root, out trapIsl);
			trapIsl.Expand(60f);
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
				// (only the island's own: the grid reaches some 260 m out and takes in a neighbouring island or world object - a
				// 'trap' 249 m east of Brinelab Tower (bl8q) and 257 m off Shuttlewreck Reef. A trap counts when one of its spots
				// touches a collider under the island's root (its terrain, its pieces) or lies in the island's bounds 30 m out -
				// pieces a world adds beside the island (a cave hill, a rope) stand there, bugfix 11-26)
				Bounds tb = new Bounds(g.Pos[cluster[0]], Vector3.zero);
				foreach (int a in cluster) tb.Encapsulate(g.Pos[a]);
				bool ours = haveTb && trapIsl.Intersects(tb);
				for (int ci = 0; ci < cluster.Count && !ours; ci += Mathf.Max(1, cluster.Count / 200))
					foreach (Collider c in Physics.OverlapSphere(g.Pos[cluster[ci]], 0.6f, ~0, QueryTriggerInteraction.Ignore))
						if (c.transform.IsChildOf(e.Root.transform)) { ours = true; break; }
				if (!ours)
				{
					Vector3 nr = Rel(e, tb.center, offset);
					report.Add("REACH " + name + " | info | a closed-in place at " + nr.x.ToString("F1") + " " + nr.z.ToString("F1") + " (" + cluster.Count + " spots) is not the island's - left out");
					continue;
				}
				traps++;
				problems++;
				Bounds cb = new Bounds(g.Pos[cluster[0]], Vector3.zero);
				foreach (int a in cluster) cb.Encapsulate(g.Pos[a]);
				Vector3 cr = Rel(e, cb.center, offset);
				bool roofed = Physics.Raycast(cb.center + Vector3.up * 2f, Vector3.up, 60f, mask, QueryTriggerInteraction.Ignore);
				report.Add("REACH " + name + " | trap at " + cr.x.ToString("F1") + " " + cr.z.ToString("F1") + " h=" + (cb.min.y - e.Position.y).ToString("F1") + ".." + (cb.max.y - e.Position.y).ToString("F1") +
					" (" + cb.size.x.ToString("F0") + "x" + cb.size.z.ToString("F0") + " m, " + cluster.Count + " spots" + (roofed ? ", roofed" : ", open sky") + (cluster.Any(a => g.Kind[a] == 2) ? ", under water" : "") + ") | - | FAIL a player gets in but not out" + TrapNearestOut(e, g, cluster, back) + TrapWayIn(e, g, cluster, trapped));
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
			ReachCreaturesBack();
			done(problems);
		}

		/// <summary>How a player gets into a trap (round 4, 11-05: the report gave only the way out it lacks): the links into it from spots
		/// outside it, as many as there are and the first one's ends (raw feet, island-relative, kinds).</summary>
		static string TrapWayIn(IslandWorldState.Entry e, ReachGrid g, List<int> cluster, bool[] trapped)
		{
			int n = 0, fa = -1, fb = -1;
			foreach (int b in cluster)
				foreach (int a in g.In[b])
					if (!trapped[a]) { n++; if (fa < 0) { fa = a; fb = b; } }
			if (n == 0) return " (way in: none from outside it)";
			Func<int, string> P = i => { Vector3 v = g.Pos[i] - g.Anchor; return v.x.ToString("F1", CultureInfo.InvariantCulture) + " " + v.y.ToString("F1", CultureInfo.InvariantCulture) + " " + v.z.ToString("F1", CultureInfo.InvariantCulture); };
			return " (way in: " + n + " links, e.g. " + P(fa) + " -> " + P(fb) + ", kinds " + g.Kind[fa] + "->" + g.Kind[fb] + ")";
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
				b.x.ToString("F1", CultureInfo.InvariantCulture) + " " + b.y.ToString("F1", CultureInfo.InvariantCulture) + " " + b.z.ToString("F1", CultureInfo.InvariantCulture) + ", kinds " + g.Kind[bt] + "->" + g.Kind[bo] + TrapOutWhy(g, bt, bo) + ")";
		}

		/// <summary>Why the grid has no link along the trap's closest way out: the lines and the body sweep its moves use, and what
		/// they meet (bugfix 11-27: Caisson Yard's drowned store roof, 0.9 m under the sea, a way in from the sea and none out -
		/// the report named no cause).</summary>
		static string TrapOutWhy(ReachGrid g, int a, int b)
		{
			Vector3 pa = g.Pos[a], pb = g.Pos[b];
			byte ka = g.Kind[a], kb = g.Kind[b];
			int ia = Mathf.FloorToInt((pa.x - g.X0) / g.Cell), ja = Mathf.FloorToInt((pa.z - g.Z0) / g.Cell), ib = Mathf.FloorToInt((pb.x - g.X0) / g.Cell), jb = Mathf.FloorToInt((pb.z - g.Z0) / g.Cell);
			if (Mathf.Abs(ia - ib) > 1 || Mathf.Abs(ja - jb) > 1) return "; why not: not neighbouring cells";
			var why = new List<string>();
			float top = Mathf.Max(Mathf.Max(pa.y, pb.y), RSea);
			foreach (float hy in new[] { top + 0.45f, top + 1.2f, RSea + 0.3f, RSea - 0.6f })
			{
				RaycastHit lh;
				Vector3 la = new Vector3(pa.x, hy, pa.z), lb = new Vector3(pb.x, hy, pb.z);
				if (Physics.Linecast(la, lb, out lh, reachMask, QueryTriggerInteraction.Ignore) || Physics.Linecast(lb, la, out lh, reachMask, QueryTriggerInteraction.Ignore))
					why.Add("line at " + hy.ToString("F2") + " meets " + BlockerName(lh.collider));
			}
			float sy = ka == 1 && kb == 1 ? RSea + RSwimFeet : ka == 0 && kb == 1 ? Mathf.Max(pa.y, RSea) : Mathf.Max(pa.y, pb.y);
			sweptLast = null;
			if (Swept(pa, pb, sy) && sweptLast != null) why.Add(sweptLastAt + " meets " + BlockerName(sweptLast));
			return "; why not: " + (why.Count == 0 ? "no line or body sweep meets anything - a step, drop or depth rule" : string.Join("; ", why.ToArray()));
		}

		static long VoxKey(int i, int j, int k) { return ((long)(i + 20000) << 40) | ((long)(j + 20000) << 20) | (long)k; }
		static void VoxSplit(long key, out int i, out int j, out int k) { i = (int)(key >> 40) - 20000; j = (int)((key >> 20) & 0xFFFFF) - 20000; k = (int)(key & 0xFFFFF); }

		/// <summary>Room for the player standing with the feet here.</summary>
		static bool Stands(Vector3 feet)
		{
			Vector3 top = feet + Vector3.up * (RTop - RRadius), low = feet + Vector3.up * (RRadius + 0.08f);
			if (!Physics.CheckCapsule(low, top, RRadius, reachMask, QueryTriggerInteraction.Ignore)) return true;
			// (on a slope over ~38 deg the 0.6 m wide capsule's foot touches the rising terrain itself - Raft's controller stands
			// and walks there (a 40 deg trail pad edge, PQ11 10-27): the terrain alone may touch it under 0.4 m, nothing else)
			if (Physics.CheckCapsule(feet + Vector3.up * (0.4f + RRadius), top, RRadius, reachMask, QueryTriggerInteraction.Ignore))
			{
				if (standWhyOn) { standWhy = ""; foreach (Collider c in Physics.OverlapCapsule(feet + Vector3.up * (0.4f + RRadius), top, RRadius, reachMask, QueryTriggerInteraction.Ignore)) standWhy += (standWhy.Length > 0 ? "," : "") + (c is TerrainCollider ? "terrain" : BlockerName(c)); }
				return false;
			}
			// (a dug passage's rock is ground as the terrain is: its mouth ramps up out of a den at 35-55 deg - every spot on
			// Hollowtrunk Grove's root den ramp was ruled out and the den called a trap, though Raft's walker came up it in 2 s, 11-08)
			foreach (Collider c in Physics.OverlapCapsule(low, top, RRadius, reachMask, QueryTriggerInteraction.Ignore))
				if (!StandGround(c)) { if (standWhyOn) standWhy = "low " + BlockerName(c); return false; }
			return true;
		}

		/// <summary>(CIReachColumns) What the last Stands that said no met: only while standWhyOn, the grid's own calls skip it.</summary>
		static string standWhy = "";
		static bool standWhyOn;

		/// <summary>Ground a standing player's foot may touch on a slope: the terrain, a dug passage's rock (DigTunnel.RockName),
		/// or a cave tunnel piece's round floor and the join plate between two pieces (bugfix 11-26, Thalassa Mooring's sea
		/// gate: the tube's floor curves up into its walls, and where two pieces meet the 0.6 m wide body's foot touched the
		/// curve and the plate a few cm under it - the cells across the join were no spots and the dry room behind it 'can't get
		/// there', though the caves play swims through).</summary>
		static bool StandGround(Collider c)
		{
			if (c == null) return false;
			return c is TerrainCollider || c.name.StartsWith(DigTunnel.RockName, StringComparison.Ordinal) ||
				c.name.StartsWith("TP_UnderwaterCaveTunnel", StringComparison.Ordinal) || c.name == CaveTool.JoinName;
		}

		/// <summary>The player's body (bar the bottom 0.45 m, which steps over) swept from a to b at floor height y:
		/// lines down the middle miss a bunk bed's corner or a stair's side that stops the 0.6 m wide player.</summary>
		static bool Swept(Vector3 a, Vector3 b, float y)
		{
			const float r = 0.2f;
			Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z);
			if (d.magnitude < 0.01f) return false;
			Vector3 p1 = new Vector3(a.x, y + 0.45f + r, a.z), p2 = new Vector3(a.x, y + RTop - r, a.z);
			Vector3 q1 = new Vector3(b.x, y + 0.45f + r, b.z), q2 = new Vector3(b.x, y + RTop - r, b.z);
			RaycastHit sh;
			if (Physics.CapsuleCast(p1, p2, r, d.normalized, out sh, d.magnitude, reachMask, QueryTriggerInteraction.Ignore) ||
				Physics.CapsuleCast(q1, q2, r, -d.normalized, out sh, d.magnitude, reachMask, QueryTriggerInteraction.Ignore)) { sweptLast = sh.collider; sweptLastAt = "cast with the feet at " + y.ToString("F2"); return true; }
			// (the body standing at points along the way, as Stands does at the spots: a cast skips a mesh it starts in and a
			// one-sided skin met from behind, an overlap does not. Brinelab's den mouth: the hill skin's sloping roof came down
			// to ~1.76 m between two spots 1.9 m apart that both stood - the walker stopped there with 'nothing found' in every
			// bf13 run while the grid's rays (to 1.7 m) and casts went through, bugfix 11-27)
			// (the feet at each point: a swimmer's at y; a walker's on the line between the spots, raised onto ground over it -
			// Brinelab's walker stood on a 0.08 m bump of the den mouth's floor and its head met the roof the line's body passed
			// under, bf13c 11-27)
			bool swim = y < Mathf.Min(a.y, b.y) - 0.5f;
			int n = Mathf.FloorToInt(d.magnitude / 0.3f);
			for (int i = 1; i <= n; i++)
			{
				float t = i / (n + 1f);
				Vector3 m = Vector3.Lerp(a, b, t); m.y = swim ? y : Mathf.Lerp(a.y, b.y, t);
				if (!swim && Physics.Raycast(m + Vector3.up * RStepUp, Vector3.down, out RaycastHit gh, RStepUp + 0.3f, reachMask, QueryTriggerInteraction.Ignore) && gh.point.y > m.y)
					m.y = gh.point.y;
				// (ground the feet may touch - a bump of terrain between the spots - is the walk's own business, not a roof)
				int c = Physics.OverlapCapsuleNonAlloc(m + Vector3.up * (0.45f + r), m + Vector3.up * (RTop - r), r, sweptHits, reachMask, QueryTriggerInteraction.Ignore);
				for (int j = 0; j < c; j++) if (!StandGround(sweptHits[j])) { sweptLast = sweptHits[j]; sweptLastAt = "body with the feet at " + m.y.ToString("F2") + ", " + (t * d.magnitude).ToString("F2") + " m along"; return true; }
			}
			return false;
		}
		static readonly Collider[] sweptHits = new Collider[16];
		/// <summary>What the last Swept that found a way blocked met, and how (the trap report's why).</summary>
		static Collider sweptLast; static string sweptLastAt = "";

		/// <summary>A surface swimmer's way from a to b: lines at 0.3 m over and 0.6 m under the sea, and the body swept with
		/// its feet 1.3 m down.</summary>
		static bool SurfaceSwimBlocked(Vector3 a, Vector3 b)
		{
			return Blocked(a, b, RSea + 0.3f) || Blocked(a, b, RSea - 0.6f) || Swept(a, b, RSea + RSwimFeet);
		}

		/// <summary>A point inside a rock's mesh: a ray up from it meets the inside of a surface that faces up (a mesh
		/// collider is a skin - a capsule wholly inside overlaps nothing; the grid swam inside Turtle Cave's rock, PQ11 10-27).
		/// An overhang's underside faces down and is no hit.</summary>
		static bool InsideSolid(Vector3 p)
		{
			// (a dug passage's open air is never solid: under the terrain it was - at the mouth the cut cells keep the ground's
			// height over the floor with no ground there, and a ray up in the bumpy passage met a wall, not a roof facing down;
			// Old Mine Islet's Miner's chest in Tunnel 1 was 'can't get there' though the play walks to it, 11-05)
			if (DigTunnel.InPassage(p, 0.3f)) return false;
			// (under a terrain: a heightfield has no inside to hit - unless p is in a cave of the island's own pieces under it, a
			// ceiling facing down over p and a floor facing up under it, both below the terrain: Temperance tunnels under a reef
			// (Drowned Labyrinth: every tunnel spot was ruled out, 'no spot' for all its targets, PQ11 10-28))
			foreach (Terrain t in Terrain.activeTerrains)
			{
				Vector3 tp = t.transform.position, ts = t.terrainData.size;
				if (p.x >= tp.x && p.x <= tp.x + ts.x && p.z >= tp.z && p.z <= tp.z + ts.z && tp.y + t.SampleHeight(p) > p.y)
				{
					// (a cut cell (a dug mouth's hole) has no ground: what is over p there is found by the ray below; only by a dug
					// passage - a cave piece's hole under its hill let the audit into the earth under it: a 'trap' in the rock, 11-05;
					// in the cave piece itself its roof may stand over the kept height map: a roof facing down over p, a floor under
					// it - Old Mine Islet's Last ore cart in the quarry cave was walled off and the walker sent through its rock)
					// (and first of all: the first thing over p met from behind - p in the rock or the earth, not in a cave; a cave's
					// ceiling faces down at it. Old Mine Islet's 'traps' at -31.3 24.2 (in the earth by a mouth's hole cell, the
					// terrain over it seen from below) and 4.3 -1.9 (under the den's floor in the mound) were points in the solid, 11-05)
					if (BehindOver(p))
					{
						// (unless it is the ground's underside, far over a cave piece's floor walled in all round by the pieces' insides:
						// a room or join with no roof of its own under a hill - Riverfork's den join at 44.4-46.7 30 (the ground 11.6 m
						// over its floor) cut the swim-in den off, 'can't get there', cave check 11-08. A point in the earth (Old Mine's
						// -31.3 24.2 by a mouth's hole) has no walls round it)
						if (overTerrain && OnPieceFloor(p) && WalledIn(p)) { solidWhy = ""; return false; }
						solidWhy = "behind " + overWhy; return true;
					}
					if (TerrainHoleAt(t, p)) { if (DigTunnel.InPassage(p, -1f)) break; bool hc = CaveUnder(p, p.y + 30f); solidWhy = "hole," + caveWhy; return !hc; }
					bool cu = CaveUnder(p, tp.y + t.SampleHeight(p));
					solidWhy = "earth," + caveWhy;
					return !cu;
				}
			}
			bool was = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				overWhy = "";
				bool over = OverFacesUp(p, 40f);
				solidWhy = "faces " + overWhy;
				return over;
			}
			finally { Physics.queriesHitBackfaces = was; }
		}

		/// <summary>The first surface over p (30 m up) is met from behind: it faces up, p is under its solid side.</summary>
		static bool BehindOver(Vector3 p)
		{
			bool was = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				return OverFacesUp(p, 30f);
			}
			finally { Physics.queriesHitBackfaces = was; }
		}

		/// <summary>(back faces on) The first surface over p (within dist) faces up: p is under its solid side. A near-vertical
		/// face met from behind (normal.y 0.1-0.3) says nothing - p is beside a wall, not under a rock - so the ray goes on past
		/// it: where two cave pieces overlap at a join, one piece's steep wall stands inside the other's passage (Riverfork's
		/// tunnel at 67.7 30: a wall with normal.y 0.10 over the floor called the tunnel solid, the den beyond 'can't get there',
		/// cave check 11-08).</summary>
		static bool OverFacesUp(Vector3 p, float dist)
		{
			Vector3 o = p;
			for (int i = 0; i < 8 && dist > 0f; i++)
			{
				RaycastHit h;
				if (!Physics.Raycast(o, Vector3.up, out h, dist, reachMask, QueryTriggerInteraction.Ignore)) return false;
				// (a cave hill's skin met from behind is rock under the hill: its faces all look out of the hill, and the space
				// between it and the terrain under it is the hill's inside - the grid walked into it and got stuck, Hadal Commons'
				// dives and Utopia's supplies, 11-24; a cave or a dug mouth under it has its own roof met first)
				// (but a steep bit of skin over a mouth is its lintel, not the hill: out from p along the skin's facing, at p's
				// height, no skin or ground closes the hill - inside the hill that ray must cross the skin to get out. Spirecrest's
				// spare chest cave mouth (the skin 4.4 m over its floor, n0.12) cut the way in, 'can't get there', sc4 11-26)
				if (h.normal.y > 0.02f && h.normal.y < 0.3f && h.collider.name == CaveMountain.SkinName && SkinOpenOut(p, h.normal, o.y + h.distance)) { float ss = h.distance + 0.02f; o += Vector3.up * ss; dist -= ss; continue; }
				if (h.normal.y > 0.02f && h.collider.name == CaveMountain.SkinName) { overTerrain = false; overWhy = "under the cave skin n" + h.normal.y.ToString("0.00") + " +" + (o.y + h.distance - p.y).ToString("0.0") + (h.normal.y < 0.3f ? " closed by " + skinClosedBy : ""); return true; }
				if (h.normal.y <= 0.1f) return false;
				// (a lone den's wall (CaveTool.PlugOpening) is one sheet of rock, not a body: over its way in it leans back into the
				// den as a funnel, so from the passage under it the ray meets its front from behind - the passage was called solid
				// though nothing stood in it from the floor to the wall 9 m up, and Spirecrest's west den was 'can't get there',
				// PQ11 sc3 11-26. The ray goes on past it: a point in the rock round the hole still meets the den's own rock)
				if (h.collider.name == CaveTool.PlugName || h.collider.name == CaveTool.PlugOutName) { float ps = h.distance + 0.02f; o += Vector3.up * ps; dist -= ps; continue; }
				if (h.normal.y > 0.3f) { overTerrain = h.collider is TerrainCollider; overWhy = BlockerName(h.collider) + " n" + h.normal.y.ToString("0.00") + " +" + (o.y + h.distance - p.y).ToString("0.0"); return true; }
				float step = h.distance + 0.02f;
				o += Vector3.up * step; dist -= step;
			}
			return false;
		}

		/// <summary>(back faces on) From p out along a cave skin's facing (level, 15 m) neither the skin nor the ground is met:
		/// the skin over p does not close round it, p is under the lip of an opening in the hill, not in the hill.
		/// (The ray goes at head height under the skin (skinY), not at p's: from a passage's floor a level ray grazed the floor's
		/// bumps further out, and the ground counts within 5 m only - past a mouth its passage runs on to the rising ground
		/// beyond. Inside a hill the skin closes round p near by. Spirecrest's spare chest cave: one row of its passage, 4.4 m
		/// under the lintel, called solid across, 'can't get there' sc6 11-26.)</summary>
		static bool SkinOpenOut(Vector3 p, Vector3 n, float skinY)
		{
			skinClosedBy = "";
			Vector3 d = new Vector3(n.x, 0f, n.z);
			if (d.sqrMagnitude < 0.01f) { skinClosedBy = "level"; return false; }
			Vector3 o = p + Vector3.up * Mathf.Min(1.5f, Mathf.Max(0f, (skinY - p.y) * 0.5f));
			foreach (RaycastHit r in Physics.RaycastAll(o, d.normalized, 15f, reachMask, QueryTriggerInteraction.Ignore))
				if (r.collider.name == CaveMountain.SkinName || (r.collider is TerrainCollider && r.distance < 5f))
				{ skinClosedBy = (r.collider is TerrainCollider ? "terrain" : "skin") + " at " + r.distance.ToString("0.0"); return false; }
			return true;
		}
		static string skinClosedBy = "";

		/// <summary>The terrain's holes (cut cells), read once per audit: ReachAnalyze clears them.</summary>
		static readonly Dictionary<Terrain, bool[,]> reachHoles = new Dictionary<Terrain, bool[,]>();

		/// <summary>p's column is a cut cell of t (a dug tunnel's mouth): no ground there, though the height map is kept.</summary>
		static bool TerrainHoleAt(Terrain t, Vector3 p)
		{
			TerrainData td = t.terrainData;
			bool[,] solid;
			if (!reachHoles.TryGetValue(t, out solid)) reachHoles[t] = solid = td.GetHoles(0, 0, td.holesResolution, td.holesResolution);
			int n = td.holesResolution;
			int x = Mathf.FloorToInt((p.x - t.transform.position.x) / td.size.x * n), z = Mathf.FloorToInt((p.z - t.transform.position.z) / td.size.z * n);
			return x >= 0 && z >= 0 && x < n && z < n && !solid[z, x];
		}

		/// <summary>p (under a terrain whose surface is at ground) lies in a hollow of the island's own pieces: the first thing over it
		/// is a ceiling facing down below the ground and the first thing under it a floor facing up - a tunnel, not the earth.
		/// (Front faces only: from inside a rock or a sunk boulder's skin a ray meets no face, so solid ground stays solid.)</summary>
		/// <summary>Why the last InsideSolid / CaveUnder said what it did (CIReachColumns prints it).</summary>
		static string solidWhy = "", caveWhy = "", overWhy = "";
		static bool overTerrain;

		/// <summary>p stands on one of the island's pieces (a front face under it within 1.1 m: InsideSolid is asked 0.5 m over a floor), not on the ground.</summary>
		static bool OnPieceFloor(Vector3 p)
		{
			RaycastHit h;
			return Physics.Raycast(p + Vector3.up * 0.1f, Vector3.down, out h, 1.2f, reachMask, QueryTriggerInteraction.Ignore) && !(h.collider is TerrainCollider) && h.normal.y > 0.3f;
		}

		/// <summary>Looks round from 1 m over p (8 ways, 8 m) meet a piece's inside - a front face, not the back of one or the
		/// ground - in at least 6 of them: p is in a hollow of the pieces, not in the earth beside them.</summary>
		static bool WalledIn(Vector3 p)
		{
			Vector3 e = p + Vector3.up * 1f;
			int walls = 0;
			bool was = Physics.queriesHitBackfaces;
			try
			{
				for (int i = 0; i < 8; i++)
				{
					Vector3 d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
					RaycastHit any, front;
					Physics.queriesHitBackfaces = true;
					bool a = Physics.Raycast(e, d, out any, 8f, reachMask, QueryTriggerInteraction.Ignore);
					Physics.queriesHitBackfaces = false;
					bool f = Physics.Raycast(e, d, out front, 8f, reachMask, QueryTriggerInteraction.Ignore);
					if (a && f && any.collider == front.collider && Mathf.Abs(any.distance - front.distance) < 0.02f && !(front.collider is TerrainCollider)) walls++;
				}
			}
			finally { Physics.queriesHitBackfaces = was; }
			return walls >= 6;
		}

		static bool CaveUnder(Vector3 p, float ground)
		{
			RaycastHit up, down;
			caveWhy = "no roof";
			// (a near-vertical wall met first (normal y within 0.3 of flat) is the passage's side, not its roof: the ray goes on
			// past it to the roof - Riverfork's tunnel at 67.7 30 met its upper wall (normal y -0.13) and was called earth, the
			// den beyond 'can't get there', cave check 11-08)
			Vector3 o = p; float left = ground - p.y;
			for (int i = 0; ; i++)
			{
				if (left <= 0f || !Physics.Raycast(o, Vector3.up, out up, left, reachMask, QueryTriggerInteraction.Ignore)) return false;
				if (i >= 7 || up.collider is TerrainCollider || Mathf.Abs(up.normal.y) > 0.3f) break;
				float step = up.distance + 0.02f; o += Vector3.up * step; left -= step;
			}
			caveWhy = "up " + (up.collider is TerrainCollider ? "terrain" : BlockerName(up.collider)) + " n" + up.normal.y.ToString("0.00") + " +" + up.distance.ToString("0.0");
			if (up.collider is TerrainCollider || up.normal.y > -0.3f) return false;
			caveWhy = "no floor";
			if (!Physics.Raycast(p, Vector3.down, out down, 30f, reachMask, QueryTriggerInteraction.Ignore)) return false;
			caveWhy = "down " + (down.collider is TerrainCollider ? "terrain" : BlockerName(down.collider)) + " n" + down.normal.y.ToString("0.00");
			return !(down.collider is TerrainCollider) && down.normal.y > 0.3f;
		}

		/// <summary>For a stuck walk: what the rays of a floor-to-floor link meet now (bugfix 11-25).</summary>
		static string LinkWhy(Vector3 pa, Vector3 pb)
		{
			float hi = Mathf.Max(pa.y, pb.y);
			Vector3 lo = pa.y < pb.y ? pa : pb;
			Func<Vector3, Vector3, string> ray = (a, b) =>
			{
				RaycastHit h;
				if (Physics.Linecast(a, b, out h, reachMask, QueryTriggerInteraction.Ignore) || Physics.Linecast(b, a, out h, reachMask, QueryTriggerInteraction.Ignore))
					return BlockerName(h.collider) + " n" + h.normal.y.ToString("0.00") + " at dy " + (h.point.y - lo.y).ToString("0.00");
				return "free";
			};
			return "; rise " + ray(lo + Vector3.up * 0.3f, new Vector3(lo.x, hi + 0.45f, lo.z)) + "; across 0.3 " + ray(pa + Vector3.up * 0.3f, pb + Vector3.up * 0.3f)
				+ "; across 1.0 " + ray(pa + Vector3.up * 1.0f, pb + Vector3.up * 1.0f) + "; flat " + (Blocked(pa, pb, hi + 0.45f) || Blocked(pa, pb, hi + 1.0f) || Blocked(pa, pb, hi + 1.55f) ? "blocked" : "free") + ", swept " + (Swept(pa, pb, hi) ? "blocked" : "free") + BacksWhy(pa, pb, hi);
		}

		/// <summary>(bugfix 11-26) The flat and swept checks again with back faces on: a one-sided skin met from behind
		/// (Brinelab's den mouth, bl7 - the walker stopped by CaveSkin where the grid's rays went through).</summary>
		static string BacksWhy(Vector3 pa, Vector3 pb, float hi)
		{
			bool was = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try { return "; backs on: flat " + (Blocked(pa, pb, hi + 0.45f) || Blocked(pa, pb, hi + 1.0f) || Blocked(pa, pb, hi + 1.55f) ? "blocked" : "free") + ", swept " + (Swept(pa, pb, hi) ? "blocked" : "free"); }
			finally { Physics.queriesHitBackfaces = was; }
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
				// (from a floor deep enough that the player floats over it - feet -1.1..-1.5 - up onto ground more than a step over
				// the floating feet: the swimmer's own way out, as off a swim spot (SwimOutWalk). Utopia 11-25: after the shore
				// rule, two walks still stuck in the water at -1.1, 'a step too high', the grid stepping floor to floor out of it)
				if (!wet && pa.y < RSea + RSwimFeet + 0.3f && pb.y > Mathf.Max(pa.y, RSea + RSwimFeet) + 0.3f && !SwimOutWalk(pa, pb)) { dbgFloatOut++; return; }
				if (wet && Mathf.Abs(dy) > 2f) return;
				float hi = Mathf.Max(pa.y, pb.y);
				bool flat = !(Blocked(pa, pb, hi + 0.45f) || Blocked(pa, pb, hi + 1.0f) || Blocked(pa, pb, hi + 1.55f) || Swept(pa, pb, hi));
				// (rising ground: the fixed-height rays above hit the slope itself - Raft walked a 1.2 m trail pad step
				// in 0.9 s that they called blocked, PQ11 10-27; follow the ground instead)
				bool slope = !flat && !wet && SlopeWalk(pa, pb);
				if (!flat && !slope) return;
				// (a swim between two floors under water: the swimmer rises in the lower spot's column and crosses over, or swims
				// straight across - one of the two must be free. The rays above only looked over the higher spot: Hadal's walker
				// swam from a roof down through the roof slab to the floor under it, 'block quad', bugfix 11-25)
				if (wet && Mathf.Abs(dy) > 0.3f)
				{
					Vector3 lo = pa.y < pb.y ? pa : pb;
					bool rise = !Between(lo + Vector3.up * 0.3f, new Vector3(lo.x, hi + 0.45f, lo.z), reachMask);
					bool across = !Between(pa + Vector3.up * 0.3f, pb + Vector3.up * 0.3f, reachMask) && !Between(pa + Vector3.up * 1.0f, pb + Vector3.up * 1.0f, reachMask);
					if (!rise && !across) { dbgWetRoof++; return; }
				}
				// (a jump: room over the head as the player rises)
				if (flat && dy > RStepUp && !wet && Physics.CheckCapsule(pa + Vector3.up * (dy + RRadius + 0.1f), pa + Vector3.up * (dy + RTop - RRadius), RRadius, reachMask, QueryTriggerInteraction.Ignore)) return;
				// (a drop: nothing to land on on the way down - a floor that reaches over the spot below catches the player)
				if (dy < -0.9f && !wet && FallCaught(pb, pa.y, pb.y)) return;
				// (not down a ladder's shaft: the ladder is the way down there. A fall beside one brushes its rungs and the hatch's rim
				// round it - the grid dropped 8 m from Sealkeeper's door tower deck into its ladder hole, 0.4 m from the climb collider,
				// and the walker hung on the ladder's top or the rim instead (lt3b at 3x, bs1 both speeds), bugfix 11-27)
				if (dy < -0.9f && !wet && DropDownLadder(pb, pa.y)) { dbgLadderDrop++; return; }
				g.Link(a, b);
				return;
			}
			if (ka == 1 && kb == 1)
			{
				if (same) return;
				// (and the swimmer's body swept, feet floating 1.3 m down, head ~0.5 m over the surface: Caisson Yard cy7 11-27 - a
				// block's underside 0.3 m over the sea, between the two lines, stopped the 1x swimmer's head on a linked step)
				if (!SurfaceSwimBlocked(pa, pb)) g.Link(a, b);
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
				if (pb.y > Mathf.Max(bed, RSea + RSwimFeet) + RStepUp && (pb.y > RSea + 0.6f || bed < RSea - 2.9f || !SwimOutWalk(new Vector3(pa.x, bed, pa.z), pb))) { dbgShoreHigh++; return; }
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

		static bool SlopeWalk(Vector3 a, Vector3 b, out string why) { return SlopeWalk(a, b, out why, float.NegativeInfinity); }

		/// <summary>A swimmer's way up out of the sea (bugfix 11-25, Utopia Remade: 8 walks stuck at shores the grid called
		/// slopes): the swimmer has no jump there (it rose 0-0.24 m) and its controller climbs what Raft's controller climbs
		/// without one - slopeLimit 45 deg, stepOffset 0.3 (CIPlayerJump) - not the 60 deg and 0.5 m steps a walker on land
		/// took. Ground under its floating feet it swims over.</summary>
		static bool SwimOutWalk(Vector3 bedPoint, Vector3 b) { string why; return SlopeWalk(bedPoint, b, out why, RSea + RSwimFeet); }

		static bool SlopeWalk(Vector3 a, Vector3 b, out string why, float swimFeet)
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
				// (a swimmer: over its floating feet only a 0.3 m step or a 45 deg slope - a 2 deg margin for the terrain's facets)
				if (g.y > swimFeet && (rise > 0.3f || (rise > 0.05f && h.normal.y < 0.68f))) { why = "swimmer: sample " + i + " rise " + rise.ToString("0.00") + " normal y " + h.normal.y.ToString("0.00"); return false; }
				// (the body over the bottom step's height: a terrain cell's edge rises like a step just ahead and the controller
				// steps up it - a capsule from 0.45 m touched the trail's next 0.5 m rise)
				Collider[] hit = Physics.OverlapCapsule(g + Vector3.up * (RStepUp + r), g + Vector3.up * (RTop - r), r, reachMask, QueryTriggerInteraction.Ignore);
				if (hit.Length > 0) { why = "no room for the body at sample " + i + " (" + hit[0].name + ")"; return false; }
				float y0 = Mathf.Max(prev.y, g.y);
				if (Blocked(prev, g, y0 + RStepUp) || Blocked(prev, g, y0 + 1.0f) || Blocked(prev, g, y0 + 1.55f)) { why = "blocked before sample " + i; return false; }
				prev = g;
			}
			why = "ends " + (prev.y - b.y).ToString("0.00") + " m from b's floor";
			return Mathf.Abs(prev.y - b.y) < 0.3f;
		}

		/// <summary>(bugfix 11-27) A fall in column at from the height from down whose body (its radius and 0.2 m) would pass a
		/// ladder's climb collider below that height.</summary>
		static bool DropDownLadder(Vector3 at, float from)
		{
			const float m = RRadius + 0.2f;
			foreach (Bounds lb in reachDropLadders)
				if (lb.max.y > at.y + 0.3f && lb.min.y < from - 0.3f && at.x > lb.min.x - m && at.x < lb.max.x + m && at.z > lb.min.z - m && at.z < lb.max.z + m) return true;
			return false;
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
			try
			{
				if (dist <= 0f) return false;
				Vector3 top = new Vector3(at.x, from + 0.6f, at.z);
				if (Physics.SphereCast(top, r, Vector3.down, out h, dist, reachMask, QueryTriggerInteraction.Ignore) && (!floorOnly || h.normal.y > 0.7f)) return true;
				// (sweeps miss a mesh's back faces whatever queriesHitBackfaces says - only rays honour it: a cave hill's skirt
				// skin, its roof a back face from above, let the grid drop through it where Raft's walker stood on it, Old Mine
				// Islet's wedge under Grey's den hill, round 4. Rays down the sphere's middle and its four sides as well)
				// (a skin's roof met from above catches up to 50 deg, as the grid's floors (n 0.64): Utopia's ridge skin over the sea off the
				// quarry, 46 deg, let the grid drop through it into the sea - the walker landed on it 2 m down and stuck there, bugfix 11-26
				// ua6. A back face's normal comes back turned toward the ray (n +0.68 from above, -0.68 from below), so either sign counts - ua7)
				foreach (Vector3 o in new[] { Vector3.zero, Vector3.right * r, Vector3.left * r, Vector3.forward * r, Vector3.back * r })
					if (Physics.Raycast(top + o, Vector3.down, out h, dist + r, reachMask, QueryTriggerInteraction.Ignore) && (!floorOnly || Mathf.Abs(h.normal.y) >= 0.64f)) return true;
				return false;
			}
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

		/// <summary>A floor under every step from a ladder's top to a top spot past the first 0.8 m (a step off a ladder, not a jump
		/// over a hole): rays down from 0.5 m over the spot's floor height, 1.1 m long.</summary>
		static bool LadderTopFloored(Bounds lb, Vector3 p)
		{
			Vector3 a = lb.ClosestPoint(new Vector3(p.x, lb.max.y, p.z)); a.y = p.y;
			Vector3 d = p - a; d.y = 0f;
			float len = d.magnitude;
			if (len <= 0.8f) return true;
			d /= len;
			for (float s = 0.8f; s < len - 0.1f; s += 0.2f)
				if (!Physics.Raycast(a + d * s + Vector3.up * 0.5f, Vector3.down, 1.1f, reachMask, QueryTriggerInteraction.Ignore)) return false;
			return true;
		}

		static bool LadderClear(Vector3 from, Vector3 to, List<Collider> ladder)
		{
			Vector3 d = to - from;
			if (d.magnitude < 0.05f) return true;
			// (both ways: a ray starting inside a collider misses it - on Lumen Shelf's spar the eye stood in a wall block's helper
			// box, and spots outside the shaft were linked as grabbing its ladder through the wall; the walker climbed outside it, lu7)
			for (int way = 0; way < 2; way++)
				foreach (RaycastHit h in Physics.RaycastAll(way == 0 ? from : to, (way == 0 ? d : -d).normalized, d.magnitude, reachMask, QueryTriggerInteraction.Ignore))
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

		/// <summary>A ladder that is a zipline's: the ladder itself under a zipline object, or a zipline's part (its mount, its
		/// rope's end) within 2.5 m of its top.</summary>
		static bool LadderZipTop(Collider c, Bounds lb)
		{
			Func<Transform, bool> zip = t => { for (; t != null; t = t.parent) if (t.name.IndexOf("zipline", StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; };
			if (zip(c.transform)) return true;
			foreach (Collider o in Physics.OverlapSphere(new Vector3(lb.center.x, lb.max.y, lb.center.z), 2.5f, ~0, QueryTriggerInteraction.Collide))
				if (o != null && zip(o.transform)) return true;
			return false;
		}

		static string LadderName(Collider c, IslandWorldState.Entry e)
		{
			IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>();
			return r != null ? r.ObjectName + (r.Name.Length > 0 ? " '" + r.Name + "'" : "") : c.name;
		}

		/// <summary>Which of the ladder's two faces has room for a climber, half way up: its object's forward side or its back.</summary>
		static Bounds LadderPart(List<Bounds> parts, float y)
		{
			Bounds best = parts[0]; float bd = float.MaxValue;
			foreach (Bounds b in parts) { float dd = y < b.min.y ? b.min.y - y : y > b.max.y ? y - b.max.y : 0f; if (dd < bd) { bd = dd; best = b; } }
			return best;
		}

		static string LadderFacing(List<Collider> ch, Bounds lb)
		{
			Transform t = LadderRoot(ch[0]);
			Vector3 f = t.forward; f.y = 0f;
			if (f.sqrMagnitude < 0.01f) return "?";
			f.Normalize();
			bool front = LadderRoom(lb, f), backside = LadderRoom(lb, -f);
			return "room at its front " + (front ? "yes" : "no") + ", at its back " + (backside ? "yes" : "no") + ", yaw " + t.eulerAngles.y.ToString("F0");
		}

		/// <summary>How many of a ladder's climbing spots on one side have room (LadderRoom's samples): with room on neither side -
		/// a sea ladder whose foot is by the cliff's sloping underwater face - it is climbed from the side with more (the open
		/// sea, not the rock: Old Mine's barge ladder faced into its cliff, round 5).</summary>
		static int LadderFree(Bounds lb, Vector3 dir)
		{
			int n = 0;
			for (float y = lb.min.y + 0.8f; y < lb.max.y - 0.4f; y += 1f)
			{
				Vector3 c = new Vector3(lb.center.x, y, lb.center.z) + dir * 0.75f;
				if (!Physics.CheckSphere(c, 0.28f, reachMask, QueryTriggerInteraction.Ignore) && !InsideSolid(c)) n++;
			}
			return n;
		}

		static bool LadderRoom(Bounds lb, Vector3 dir)
		{
			for (float y = lb.min.y + 0.8f; y < lb.max.y - 0.4f; y += 1f)
			{
				Vector3 c = new Vector3(lb.center.x, y, lb.center.z) + dir * 0.75f;
				if (Physics.CheckSphere(c, 0.28f, reachMask, QueryTriggerInteraction.Ignore)) return false;
				// (nor under the ground: a heightfield has no inside for a sphere to touch - a sea ladder on a cliff found "room" in
				// the rock behind it, faced into the cliff and nobody could take hold of it, Old Mine's two sea ladders round 5)
				if (InsideSolid(c)) return false;
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

		/// <summary>A blocked spot (3+ walks stuck there) that the way in from every side passes (2 m flat, 3 m up/down), or null
		/// when one side's way in misses them all - that target is walked.</summary>
		static string ReachRouteBlocked(ReachGrid g, List<int> use, bool[][] fwd, bool[] back, int[][] parent)
		{
			if (reachBlockedAt.Count == 0) return null;
			string first = null;
			for (int s = 0; s < fwd.Length; s++)
			{
				int k0 = use.Where(k => fwd[s][k] && back[k]).DefaultIfEmpty(-1).First();
				if (k0 < 0) continue;
				int hit = -1, n = 0;
				for (int a = k0; a >= 0 && hit < 0 && n < 100000; a = parent[s][a], n++)
				{
					Vector3 p = g.Pos[a] - g.Anchor;
					hit = reachBlockedAt.FindIndex(q => ScFlat(q, p) < 2f && Mathf.Abs(q.y - p.y) < 3f);
				}
				if (hit < 0) return null;
				if (first == null) first = reachBlockedWhy[hit];
			}
			return first;
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

		/// <summary>CIReachTrace: x0 z0 x1 z1 n (island metres, as the report gives them) - the next CIReach logs the grid's spots
		/// along that line; null: off.</summary>
		static float[] reachTrace;

		[ConsoleCommand(name: "CIReachTrace", docs: "Dev: cave check 11-08 - the next CIReach logs the grid's spots in n columns along a line (island metres): each spot's kind (f floor, w surface, u under water, l ladder), height, reached from the sea or not (+/-) and its links out. CIReachTrace x0 z0 x1 z1 n, or off")]
		public static void ReachTraceCommand(string[] args)
		{
			float[] v;
			reachTrace = args != null && args.Length == 5 && (v = args.Select(x => { float f; return float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : float.NaN; }).ToArray()).All(f => !float.IsNaN(f)) ? v : null;
			Log("PASS: reach trace " + (reachTrace == null ? "off" : string.Join(" ", args)));
		}

		static void ReachTraceLog(IslandWorldState.Entry e, ReachGrid g, bool[][] fwd, Vector2 offset)
		{
			float[] t = reachTrace;
			int n = Mathf.Clamp((int)t[4], 1, 80);
			var rel = new Vector3[g.Pos.Count];
			for (int k = 0; k < g.Pos.Count; k++) rel[k] = Rel(e, g.Pos[k], offset);
			for (int c = 0; c < n; c++)
			{
				float f = n == 1 ? 0f : (float)c / (n - 1);
				float x = Mathf.Lerp(t[0], t[2], f), z = Mathf.Lerp(t[1], t[3], f);
				var bits = new List<string>();
				foreach (int k in Enumerable.Range(0, g.Pos.Count).Where(k => Mathf.Abs(rel[k].x - x) <= 0.5f && Mathf.Abs(rel[k].z - z) <= 0.5f).OrderBy(k => rel[k].y))
					bits.Add("fwul"[Mathf.Min(3, (int)g.Kind[k])] + rel[k].y.ToString("0.0") + (fwd[0][k] || fwd[1][k] || fwd[2][k] || fwd[3][k] ? "+" : "-") + g.Out[k].Count
							+ "@" + rel[k].x.ToString("0.0") + "," + rel[k].z.ToString("0.0") + ">" + string.Join(",", g.Out[k].Select(o => rel[o].x.ToString("0.0") + "/" + rel[o].y.ToString("0.0")).ToArray()));
				Log("  trace " + x.ToString("F1") + " " + z.ToString("F1") + ": " + (bits.Count > 0 ? string.Join(" ", bits.ToArray()) : "no spots"));
			}
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
				// (a hazard zone - spikes, acid, a dark den - is a place to keep out of, not one to get to: the Stalker's Hollow's
				// bone pit stakes sit inside their spike models and came out "no spot to use it from", 11-09 w03. Its play walks it)
				if (!string.IsNullOrEmpty(z.Hazard)) continue;
				// (a hazard zone - spikes, acid, a dark den - is a place to keep out of, not one to get to: the Stalker's Hollow's
				// bone pit stakes sit inside their spike models and came out "no spot to use it from", 11-09 w03. Its play walks it)
				if (!string.IsNullOrEmpty(z.Hazard)) continue;
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
						// (a note a behaviour shows later - hidden at the start, so not here now - that the island's own play reads:
						// checked by play, not a fault of reach; Rootcore, Tracker's Rise, Rootweave 11-09)
						string readBy = st.Type == "read" ? ByPlayFile(tg, "read") : null;
						report.Add("REACH " + e.HostName + " | step target '" + tg + "' | " + label + " | " + (st.Type == "collect" || st.Type == "blueprint" ? "NOTE not in a chest or object found now (a behaviour, creature or dig gives it)" : readBy != null ? "NOTE hidden now (a behaviour shows it): CHECKED BY PLAY (" + readBy + " reads it)" : "FAIL not found on the island now"));
						if (st.Type != "collect" && st.Type != "blueprint" && readBy == null) problems++;
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
			nameEntry = e;
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
			// (a standing spot under the sea's surface counts as sea here too: the shallows' sea starts, PQ11 10-29)
			Func<int, bool> seaSpot = a => g.Kind[a] == 1 || (g.Kind[a] == 0 && g.Pos[a].y < RSea - 0.5f);
			int firstLand = pin.FindIndex(a => !seaSpot(a));
			if (firstLand > 0)
			{
				int st = firstLand - 1;
				while (st > 0 && ScFlat(g.Pos[pin[st - 1]], g.Pos[pin[firstLand]]) <= 15f) st--;
				pin = pin.GetRange(st, pin.Count - st);
			}
			int lastLand = pout.FindLastIndex(a => !seaSpot(a));
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
				// (the 1x walk again starts clean: off any ladder the first walk left the player on, no key held - sk7 11-27)
				if (attempt > 0) ReachUnclimb(pl);
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
				if (reachStuckN[i] >= 3 && !reachBlockedAt.Any(q => ScFlat(q, reachStuckAt[i]) < 2f && Mathf.Abs(q.y - reachStuckAt[i].y) < 3f))
				{
					string b = "blocked at " + reachStuckAt[i].x.ToString("F1", CultureInfo.InvariantCulture) + " " + reachStuckAt[i].z.ToString("F1", CultureInfo.InvariantCulture) + " h=" + reachStuckAt[i].y.ToString("F1", CultureInfo.InvariantCulture) + " (" + reachStuckN[i] + " targets)";
					reachBlockedAt.Add(reachStuckAt[i]); reachBlockedWhy.Add(b);
					Log("  FAIL " + b + " - later walks whose every way in passes there are not tried");
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
			string blocker = null;
			foreach (Vector3 aim in new[] { tb.center, tb.ClosestPoint(eye), tb.center + Vector3.up * tb.extents.y * 0.6f })
			{
				Vector3 d = aim - eye;
				if (d.magnitude > RUse + 0.6f) continue;
				foreach (RaycastHit h in Physics.RaycastAll(eye, d.normalized, RUse + 0.6f, UseMask(t), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
				{
					if (t.Root != null && h.collider.transform.IsChildOf(t.Root)) return null;
					if (h.collider.isTrigger || h.collider.transform.IsChildOf(pl.transform)) continue;
					if (blocker == null) blocker = h.collider.name + " (" + LayerMask.LayerToName(h.collider.gameObject.layer) + ") " + h.distance.ToString("F1") + " m out";
					break;
				}
			}
			// (bugfix 11-25: say what stood in the way - Hadal's toolmaker's chest failed 1.6 m from the eye with no clue why)
			return "it isn't in reach from where the walk ended (" + (tb.center - eye).magnitude.ToString("F1") + " m from the eye" + (blocker != null ? "; in the way: " + blocker : "") + ")";
		}

		/// <summary>The body's foot (a 0.2 m ball just over the feet) meets something on its way down from the feet to a dive cell:
		/// a ray slipped past the lip the walker stood on in front of Hadal's house 3 door, took the cells under it as reached
		/// and swam at the lintel, bugfix 11-26.</summary>
		static bool BodyDown(Vector3 feet, Vector3 aim)
		{
			Vector3 a = feet + Vector3.up * 0.3f, d = aim - a; float len = d.magnitude;
			return len > 0.01f && Physics.SphereCast(a, SwimBody, d / len, out RaycastHit h, len, reachMask, QueryTriggerInteraction.Ignore);
		}

		/// <summary>A swimmer can't go straight from a to b: something in the way of a 0.25 m ball, either way.</summary>
		static bool SwimBlocked(Vector3 a, Vector3 b)
		{
			Vector3 d = b - a;
			float len = d.magnitude;
			if (len < 0.01f) return false;
			return Physics.SphereCast(a, 0.25f, d / len, out RaycastHit h1, len, reachMask, QueryTriggerInteraction.Ignore) ||
				Physics.SphereCast(b, 0.25f, -d / len, out RaycastHit h2, len, reachMask, QueryTriggerInteraction.Ignore);
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
					// (and a swimmer's corner: no clear line (a 0.25 m ball) from the last waypoint to the next cell - the walker swam
					// straight from over a sea cave's mouth to a cell 6 m in and stuck on the mouth's lip, Riverfork's den, cave check 11-08)
					else if (!keep && i + 1 < path.Count && g.Kind[last] >= 1 && g.Kind[last] <= 2 && g.Kind[path[i + 1]] >= 1 && g.Kind[path[i + 1]] <= 2)
						keep = SwimBlocked(g.Pos[last], g.Pos[path[i + 1]]) || (g.Kind[last] == 2 && g.Kind[path[i + 1]] == 2 && SwimBodyBlocked(g.Pos[last], g.Pos[path[i + 1]], reachMask)) ||
							// (a surface swim's straight line checked as its steps are: Sealkeeper's swimmer cut a corner past a block
							// whose top stood 0.3 m over the sea and stuck on its side, sk13 11-27)
							(g.Kind[last] == 1 && g.Kind[path[i + 1]] == 1 && SurfaceSwimBlocked(g.Pos[last], g.Pos[path[i + 1]]));
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

		/// <summary>The walk's clock (game time a frame, clock/30 s) and pace for the next stretch: a ladder and the step off its top
		/// run at ReachLadderClock, the rest at the walk's speed.</summary>
		static void ReachClock(int clock, bool pace)
		{
			if (!mmFake) return;
			Time.captureDeltaTime = clock / 30f;
			reachPaceOn = pace && clock > 1 ? reachPace : 1f;
		}

		/// <summary>The clock on a ladder and the step off its top: 1x, a player's own frames, whatever the walk's speed. Raft holds a
		/// climber's feet ~0.33 m under the ladder's end and a jump while climbing only climbs, so a floor more than ~0.3 m over the
		/// held feet is out of a player's reach - but one frame of 3x or 4x carried the climber up to 0.5 m past the end: Sealkeeper
		/// Spire's summit deck (0.55 m over the held feet) passed at 3x (sk6), failed at 4x pace 2.5 and at 1x (sk7 11-27).
		/// A player who falls back down the shaft climbs again once (ReachWalkPath), stepping off at the nearest floor's edge
		/// (LadderTopFloor) after a longer rise; a second fall is the walk's FAIL.</summary>
		internal const int ReachLadderClock = 1;

		/// <summary>Off any ladder (Raft's PersonController.DetachFromLadders) and no key held.</summary>
		static void ReachUnclimb(Network_Player pl)
		{
			mmWalk = 0f; mmJump = false;
			PersonController pc = pl != null ? pl.PersonController : null;
			if (pc == null || !Climbing(pl)) return;
			try { System.Reflection.MethodInfo m = AccessTools.Method(typeof(PersonController), "DetachFromLadders"); if (m != null) m.Invoke(pc, null); }
			catch (Exception ex) { Log("(off the ladder: " + ex.Message + ")"); }
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
		// (the island's creatures, put away while its walks run: the walker tests the ground, not a fight - a boar awake in a mine
		// hit the walker 1 m through a beam mid-walk, and the walk after it set off from inside the rock. Old Mine Islet 11-06)
		static readonly List<GameObject> reachAway = new List<GameObject>();

		static void ReachCreaturesAway()
		{
			foreach (AI_NetworkBehaviour a in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>())
				if (a != null && a.gameObject.activeSelf) { reachAway.Add(a.gameObject); a.gameObject.SetActive(false); }
		}

		static void ReachCreaturesBack()
		{
			foreach (GameObject go in reachAway) if (go != null) go.SetActive(true);
			reachAway.Clear();
		}

		/// <summary>The player through a path of the grid, moved by Raft itself: the first point is the start in the sea (the
		/// one teleport); then the walker looks at the next point and holds Walk (swimming and diving look up or down at it),
		/// jumps at a ledge it doesn't get over, turns aside a little when stuck, and on a ladder looks up or down and holds Walk.
		/// Each frame the line from the player's middle before to after must cross nothing solid (else the walk is invalid).
		/// Null when it got to the end, else where it stopped.</summary>
		static IEnumerator ReachWalkPath(Network_Player pl, IslandWorldState.Entry e, ReachGrid g, List<int> path, int speed, Action<string> result)
		{
			ReachCreaturesAway();
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
			// (any standing start, not only the shallows': a walk in whose way begins on land set off from where the last target's
			// walk left the player - maybe inside a mine, its first steps then through rock to the way: Old Mine Islet INVALIDs 11-06)
			else if (reachWalkIn && g.Kind[way[0]] == 0 && (ReachFeet(pl) - W(way[0])).magnitude > 1.5f)
			{
				// (a sea start in the shallows - a standing spot at or under the sea's surface, PQ11 10-29 - was never moved to: the
				// walker set off from where the last walk left it. Coldhold on box/box3 10-30: every walk "stuck on the way to
				// (16.4, -4.4, 247.6)" from inside the vault. A raft lands there: the player starts there, swimming if it is under water)
				Vector3 s0 = W(way[0]);
				if (g.Pos[way[0]].y < RSea - 0.5f) PlayerMove.To(pl, new Vector3(s0.x, e.Position.y + RSea - g.Anchor.y - 0.4f, s0.z), ControllerType.Water);
				else PlayerMove.To(pl, s0 + Vector3.up * 0.95f);
				for (int f = 0; f < 10; f++) { MmHold(pl, false); yield return null; }
				if (ReachFree(pl)) for (int f = 0; f < 5; f++) { MmHold(pl, false); yield return null; }
			}
			if (reachOnly != "") Log("  walk start: " + way.Count + " waypoints, first kind " + g.Kind[way[0]] + " at " + (W(way[0]) - e.Position).ToString("F1") + ", player at " + (ReachFeet(pl) - e.Position).ToString("F1") + ", walk-in " + reachWalkIn + ReachCreatures(pl));
			Vector3 prev = ReachFeet(pl) + Vector3.up * 0.9f;
			string bad = null;
			int wpAt = 0; // (the waypoint walked to now: in an INVALID's details)
			// (stall rule, the user 10-28 - no multi-minute waits on one target: real seconds, whatever the speed. Under 0.5 m in a 10 s
			// window: one unstick (a jump and a turn). No waypoint for 30 s while stalled, or for 90 s with under 3 m in the last window:
			// FAIL "stuck" - it still counts as a fail to triage)
			float wdWin = Time.realtimeSinceStartup, wdWay = wdWin, wdMoved = 99f;
			Vector3 wdP = ReachFeet(pl);
			bool wdTried = false;
			Func<int> watchdog = () =>
			{
				// (w10fix3 11-14: a player made a child of Raft's FirstPersonParent did not move at all - The Listening Mirrors' walks)
				ReachUnparent(pl);
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
						// (a raft's block that is not the island's - the player's own raft or a trader raft drifting past the island
						// while the walker swims out to the sea: Thalassa Mooring's walks "crossed block collider" 94 m off its middle,
						// s6u 11-26. The island's ways are what the walk proves, a raft on the move is not part of them)
						if (!h.collider.transform.IsChildOf(e.Root.transform) && h.collider.GetComponentInParent<Block>() != null) continue;
						// (a collider Raft's own CharacterController never meets - a layer the player's layer ignores, or a pair
						// set to ignore - is passed through by a real player too, e.g. a landmark zipline's rope: not tunnelling, PQ11 10-27)
						CharacterController gcc = pl.GetComponent<CharacterController>();
						if (gcc != null && (Physics.GetIgnoreLayerCollision(gcc.gameObject.layer, h.collider.gameObject.layer) || Physics.GetIgnoreCollision(gcc, h.collider))) continue;
						r = "INVALID: the walker crossed " + BlockerName(h.collider) + " (layer " + LayerMask.LayerToName(h.collider.gameObject.layer) + ", player " + (gcc != null ? LayerMask.LayerToName(gcc.gameObject.layer) : "?") + ") at " + (h.point - e.Position).ToString("F1")
							+ " [chest line " + (prev - e.Position).ToString("F1") + " to " + (now - e.Position).ToString("F1") + ", " + d.magnitude.ToString("F2") + " m in a frame, waypoint " + wpAt + "/" + way.Count + ", " + pl.PersonController.controllerType + (gcc != null ? (gcc.enabled ? "" : ", controller off") + (gcc.isGrounded ? ", grounded" : "") : "") + "]"; break;
					}
				if (r != null) r += ReachCreatures(pl);
				prev = now;
				return r;
			};
			// (set when a ladder was climbed to its top: the next floor may be one to jump onto - ReachLadderDismount)
			bool ladderUpTop = false; Vector3 ladderFace = Vector3.zero;
			// (the ladder last climbed - its waypoint, its top - and the tries at stepping off its top, see ReachLadderClock)
			int ladI = -1, ladK = -1, dismountTry = 0; float ladTopY = 0f;
			int backs = 0, bj;
			for (int i = 1; i < way.Count; i++)
			{
				wdWay = Time.realtimeSinceStartup; wdTried = false; wdMoved = 99f; hbProg++; wpAt = i;
				int k = way[i];
				byte kind = g.Kind[k];
				// (a ladder and the step off it at the ladder's clock, the rest at the walk's)
				bool offLadder = i >= 2 && g.Kind[way[i - 1]] == 3 && kind != 3;
				if (kind == 3 || offLadder) ReachClock(ReachLadderClock, false); else ReachClock(speed, true);
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
					// (a stacked chain whose ladders step sideways storey by storey: the walker headed for the whole chain's middle, 0.6 m off
					// the top ladder, and stood on it at the lobby hatch - Brinelab PQ11 s6t 11-26. It heads for the ladder at its height)
					List<Bounds> lparts; g.LadderParts.TryGetValue(k, out lparts);
					float ladTop = e.Position.y + (lb.max.y - g.Anchor.y);
					if (k != ladK) { ladK = k; dismountTry = 0; }
					ladI = i; ladTopY = ladTop;
					float nextY = i + 1 < way.Count ? W(way[i + 1]).y : W(k).y;
					bool up = nextY > ReachFeet(pl).y + 0.3f;
					float held = 0f, since = 0f, bestY = ReachFeet(pl).y, walkSign = 1f;
					Vector3 lfront; bool hasF = g.LadderF.TryGetValue(k, out lfront), atFront = false;
					int flips = 0, sweeps = 0; bool otherSide = false; float bestH = float.MaxValue;
					float sweepT = 0f, sweepYaw = 0f, nearT = 0f; Vector3 lastD = Vector3.zero;
					// (down to the sea from a roof: Raft took hold of the ladder's top while the player still stood on the roof's edge,
					// and the climb down ran into the roof under the body - Spirecrest's coop ladder, PQ11 sc3 11-26. A player lets go
					// and steps off the edge past the ladder's front into the water below: dropT, once, when the next spot is the sea)
					float dropT = 0f; bool dropped = false;
					bool toSea = !up && i + 1 < way.Count && g.Kind[way[i + 1]] != 0 && g.Kind[way[i + 1]] != 3;
					for (int n = 0; n < 6000; n++)
					{
						Vector3 p = ReachFeet(pl);
						if (up ? p.y >= nextY + 0.1f : p.y <= nextY + 0.15f) break;
						if (lparts != null && lparts.Count > 1) lc = e.Position + (LadderPart(lparts, p.y - e.Position.y + g.Anchor.y + (up ? 0.9f : -0.5f)).center - g.Anchor);
						// (at the ladder's top Raft holds the player at its end - the next segment walks off it onto the floor;
						// the walker kept climbing to the floor's height and stuck there, radar mast PQ11 10-27)
						if (up && Climbing(pl) && p.y >= ladTop - 0.35f) break;
						Vector3 d = lc - p; d.y = 0f;
						bool climbing = Climbing(pl);
						// (going down from a ladder's top while standing over it: head for the spot at its foot, over the shaft -
						// the walker stood on the wall top behind the ladder and walked nowhere, radar mast PQ11 10-27)
						// (but a ladder down through a roof hatch: the walker stopped 0.4 m short at the hatch's rim, turned to the next
						// spot (away, over the roof) and back, and never dropped in - sk_drowned roofs on Lumen/Thalassa/Shuttlewreck
						// PQ11 10-30. It keeps on into the hole the way it came for 1 s first; a swimmer looks down into it)
						if (!up && !climbing && d.sqrMagnitude < 0.16f && i + 1 < way.Count)
						{
							nearT += Time.deltaTime;
							if (nearT > 1f) { d = W(way[i + 1]) - p; d.y = 0f; }
							else if (lastD.sqrMagnitude > 0.01f) d = lastD;
						}
						else if (!up && !climbing && d.sqrMagnitude > 0.01f) lastD = d.normalized;
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
						if (dropT > 0f) { dropT -= Time.deltaTime; d = lc + (hasF ? lfront : d.normalized) * 0.9f - p; d.y = 0f; }
						// (a swimmer at a dock ladder looks up and swims up into it, Jump held - Raft takes hold when the body touches its
						// "Ladder" trigger (PersonController.OnTriggerStay); the walker swam level under the rungs and never touched it,
						// Caisson Yard / Array Root PQ11 10-28)
						bool wet = up && !climbing && inWaterNow(pl);
						mmJump = wet;
						float lpitch = climbing ? (up ? -60f : 60f) : wet ? -35f : 0f;
						if (!up && !climbing && inWaterNow(pl)) lpitch = Mathf.Clamp(Mathf.Atan2(p.y - (ladTop - 0.5f), Mathf.Max(d.magnitude, 0.3f)) * Mathf.Rad2Deg, 0f, 80f);
						Look(pl, d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : pl.transform.eulerAngles.y, lpitch);
						// (the walk turned back for a climber that made no headway; off the ladder again it kept walking backwards, away from
						// the ladder - 5.5 m out from the coop ladder at Spirecrest, 5 m across Brinelab's lobby, PQ11 scp/s6u 11-26)
						mmWalk = climbing && dropT <= 0f ? walkSign : 1f;
						if (dropT > 1.2f) { mmJump = true; lpitch = 0f; Look(pl, d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : pl.transform.eulerAngles.y, 0f); }
						MmHold(pl, false);
						ReachTrace(pl, e, "ladder up=" + up + " top=" + (ladTop - e.Position.y).ToString("F2") + " next=" + (nextY - e.Position.y).ToString("F2"));
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
							// (the turned-back walk had stepped the player off the ladder onto the roof again, so the drop ("climbing" only)
							// never came: Spirecrest's coop ladder again, sc4 11-26 - a player off it steps off the edge just the same)
							if (toSea && !dropped && held > 0f) { dropped = true; dropT = 1.5f; since = 0f; walkSign = 1f; continue; }
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
					ladderUpTop = up && Climbing(pl);
					ladderFace = pl.transform.forward; ladderFace.y = 0f;
					mmJump = false;
					continue;
				}
				Vector3 aim = W(k);
				bool water = kind != 0;
				float best = float.MaxValue, since2 = 0f, turnLeft = 0f, turnDir = 1f, jumpT = 0f, riseT = 0f, lastFlat = float.MaxValue;
				// (a drop off a ledge into the sea or onto a floor below: the walker gets over the spot and then keeps going the
				// way it came in, over the edge, as a player walks off it)
				Vector3 seg = W(k) - W(way[i - 1]); seg.y = 0f;
				int hops = 0, turns = 0, steps = 0;
				float hopFrom = float.NaN, hopPeak = 0f;
				// (off a ladder's top onto a floor still above the feet - a hatch, the floor beside or behind the ladder: a player jumps
				// off toward it. The walker walked off at the ladder's end 0.5 m under the deck and fell back down the hatch, Old Mine's
				// headframe (hw_mast) 11-06; the jump is at once, not after 2.5 s stopped)
				// (and onto a floor level with the feet beside or behind the ladder: Raft holds a climber at the ladder's end and Walk
				// turned away from it climbs down - Utopia's mast (hw_mast deckdrop 0.45) walker stood at the top level with the deck
				// tile beside the hatch, turned to it and went down the whole ladder again, bugfix 11-26 ua7; ahead, over the ladder's
				// top, walking off works - radar mast PQ11 10-27)
				Vector3 offTo = W(k) - ReachFeet(pl); offTo.y = 0f;
				bool ahead = ladderFace.sqrMagnitude < 0.01f || offTo.sqrMagnitude < 0.01f || Vector3.Dot(offTo.normalized, ladderFace.normalized) > 0.7f;
				// (and off it backwards or sideways onto a floor above the feet: a player jumps up first and steps over once clear of the
				// hatch's rim - the walker jumped and walked at once, its body met the rim 0.3 m under the roof and it fell back down two
				// storeys: Brinelab's roof hatch, bugfix 11-26 bl8q)
				// (Sealkeeper Spire's summit: the deck 0.3 m over the ladder's top all round its hatch, the grid's spot across the hatch's
				// corner - the walker stepped off toward it, over the hatch, and fell back down the 14 m shaft, sk7 11-27. A player who
				// fell climbs again and steps off at the nearest edge of the floor, rising longer first)
				bool afterUp = ladderUpTop;
				// (Overpass Shoal's Toll Hill tower, cm1os 11-27: the grid's spot after the ladder was the locker's top on the deck,
				// 1.1 m over the feet Raft holds at the ladder's end - out of a jump's reach from there; the walker jumped for it and
				// fell back down the 14 m shaft twice. A player steps off onto the deck first and climbs onto the locker from it:
				// a next spot too high to jump onto (ReachLadderStepFirst) sends the step off to the nearest floor within reach)
				Vector3 hf = ReachFeet(pl);
				bool tooHigh = afterUp && ReachLadderStepFirst(hf.y, W(k).y);
				Vector3? edge = !afterUp ? (Vector3?)null : tooHigh ? LadderTopFloor(hf, W(k), hf.y - 0.3f, hf.y + 0.75f) : dismountTry > 0 ? LadderTopFloor(hf, W(k), W(k).y - 0.35f, W(k).y + 0.35f) : null;
				float stepY = tooHigh && edge.HasValue ? edge.Value.y : W(k).y;
				if (tooHigh && edge.HasValue) { offTo = edge.Value - hf; offTo.y = 0f; ahead = ladderFace.sqrMagnitude < 0.01f || offTo.sqrMagnitude < 0.01f || Vector3.Dot(offTo.normalized, ladderFace.normalized) > 0.7f; }
				if (ReachLadderDismount(ladderUpTop, hf.y, stepY, ahead))
				{
					hopFrom = hf.y;
					if (!ahead && stepY > hf.y + 0.05f) riseT = dismountTry == 1 ? 0.6f : 0.3f;
					jumpT = Mathf.Max(0.45f, riseT + 0.15f);
				}
				if (reachOnly != "" && afterUp) Log("  off the ladder's top (try " + (dismountTry + 1) + "): feet " + (ReachFeet(pl) - e.Position).ToString("F2") + ", floor " + (W(k).y - e.Position.y).ToString("F2") + ", ahead " + ahead + ", rise " + riseT.ToString("F2") + (edge.HasValue ? ", edge at " + (edge.Value - e.Position).ToString("F2") : "") + (tooHigh ? " (the next spot out of a jump's reach: onto the floor beside first)" : ""));
				ladderUpTop = false;
				for (int n = 0; ; n++)
				{
					// (aimed afresh each frame: a world shift mid-step moved the island but left the aim 1000 m off, PQ11 10-28)
					aim = W(k);
					Vector3 p = ReachFeet(pl);
					// (a dive cell is free room for the swimmer's body round its middle: the body's middle swims to it, not the feet -
					// with the feet at a cell by a sea cave's roof the body stood up into the mouth's lip, Riverfork's den, cave check 11-08)
					if (kind == 2 && inWaterNow(pl)) { CharacterController bcc = pl.GetComponent<CharacterController>(); if (bcc != null && bcc.enabled) p.y = bcc.bounds.center.y; }
					Vector3 d = aim - p;
					Vector3 flat = new Vector3(d.x, 0f, d.z);
					float flatLen = flat.magnitude;
					// (fell back down the ladder's shaft from its top: climb it again once, then the walk fails - see ReachLadderClock)
					if (afterUp && !Climbing(pl) && p.y < ladTopY - 1.5f && aim.y > p.y + 1.5f && ladI >= 1 && dismountTry >= 1)
					{
						mmWalk = 0f; mmJump = false;
						result("stuck: fell back down the ladder's shaft from its top (" + (ladTopY - e.Position.y).ToString("F1") + ") to " + (p - e.Position).ToString("F1") + " twice, stepping off to " + (aim - e.Position).ToString("F1") +
							" - the floor " + (aim.y - (ladTopY - 0.33f)).ToString("F2") + " m over the feet Raft holds at the ladder's end (a player reaches ~0.3 m)");
						yield break;
					}
					if (afterUp && !Climbing(pl) && p.y < ladTopY - 1.5f && aim.y > p.y + 1.5f && ladI >= 1)
					{
						dismountTry++;
						Log("  fell back down the ladder from its top (" + (ladTopY - e.Position.y).ToString("F1") + ") to " + (p - e.Position).ToString("F1") + " stepping off to " + (aim - e.Position).ToString("F1") + " - climbing again, try " + (dismountTry + 1));
						mmWalk = 0f; mmJump = false;
						i = ladI - 1;
						break;
					}
					// (progress counts the way down too: a drop's last metre is a fall, not a walk)
					float dist = water ? d.magnitude : flatLen + Mathf.Max(0f, d.y - 0.6f) + Mathf.Max(0f, -d.y - 0.9f);
					if (d.y < -0.9f && flatLen < 1.3f && seg.sqrMagnitude > 0.01f && !inWaterNow(pl)) flat = seg.normalized * Mathf.Max(flatLen, 0.8f);
					// (standing over a dive cell counts only with nothing between the feet and it: Hadal's walker stood on house 1's
					// roof over a cell inside, took it as reached and stuck at the next one under the roof, 'block quad', bugfix 11-25)
					// (a dive cell within 1 m counts only when the body can go on from where it is to the next spot: Hadal's walker stood on
					// the lip in front of house 3's door 0.6 m from the cell the grid set beside it, took it as reached and was held by
					// the lip on the way down to the next, bugfix 11-26 hd8 - then it swims on into the cell itself)
					bool nearDive = d.magnitude < 1.0f && (d.magnitude < 0.35f || kind != 2 || !inWaterNow(pl) || i + 1 >= way.Count || !SwimBodyBlocked(p, W(way[i + 1]), reachMask));
					// (a spot on the ground within 0.5 m counts only when the body can go straight on from where it is to the next
					// spot on the ground: Utopia's walker came along the mast's south wall, turned for the ladder 0.5 m short of the
					// corner spot before the door gap, caught the wall's end and slid off round the west wall, the keeper's box out of
					// reach, bugfix 11-26 ua6 - then it walks on to the spot itself)
					// (or once it comes no nearer: at 3x a step is 0.4 m and the spot itself may lie between two of them)
					bool nearGround = flatLen < 0.5f && d.y < 0.9f && d.y > -0.9f && (flatLen < 0.15f || flatLen > lastFlat - 0.01f || i + 1 >= way.Count || g.Kind[way[i + 1]] != 0 ||
						!Swept(p, W(way[i + 1]), Mathf.Max(p.y, W(way[i + 1]).y)));
					// (off a ladder's top the floor counts only once stood on: Overpass's op_summit walker jumped off backwards, still
					// 0.4 m under the deck over the hatch 0.4 m short of the spot, took it as reached and fell the 14 m shaft with its
					// afterUp gone - no climb again, stuck at the foot, bf13c 11-27)
					if (nearGround && afterUp)
					{
						CharacterController ucc = pl.GetComponent<CharacterController>();
						nearGround = ucc != null && ucc.isGrounded && d.y < 0.35f;
					}
					lastFlat = flatLen;
					if (water ? (kind == 1 ? flatLen < 0.8f && Mathf.Abs(d.y) < 1.4f : nearDive || (flatLen < 0.8f && d.y < 0f && pl.PersonController.controllerType == ControllerType.Water && pl.GetComponent<CharacterController>() != null && pl.GetComponent<CharacterController>().isGrounded && !Between(ReachFeet(pl) + Vector3.up * 0.1f, aim, reachMask) && !BodyDown(ReachFeet(pl), aim))) : nearGround) break;
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
						// (a swimmer swept off a beach's way into the deeper water beside it, the next spot up a bank it cannot climb: a
						// player swims back to where the way came out of the sea and walks up again. Spirecrest's annex way out: in the
						// waves the walker kept the water controller up to the feet 0.3 m over the sea, slid off the beach's side to -1.1
						// with the next spot 2 m up the bank, sc4 11-26 - it passed in sc3)
						else if (backs < 2 && kind == 0 && inWaterNow(pl) && d.y > RStepUp && (bj = WayBackToSea(g, way, i)) > 0) { backs++; mmJump = false; i = bj - 1; break; }
						else
						{
							string diag = ReachDiag(pl) + ReachCreatures(pl);
							mmWalk = 0f; mmJump = false;
							result("stuck at " + (p - e.Position).ToString("F1") + " on the way to " + (aim - e.Position).ToString("F1") + (hops > 0 ? " (" + hops + " jumps, rose up to " + hopPeak.ToString("F2") + " m)" : "") + " (in the way: " + ReachBlocker(pl, flat.sqrMagnitude > 0.01f ? flat.normalized : d.normalized) + ")" + diag + ReachUnder(pl, e) + " [the grid's step: from " + (W(way[i - 1]) - e.Position).ToString("F1") + " kind " + g.Kind[way[i - 1]] + " to kind " + kind + LinkWhy(W(way[i - 1]), W(k)) + "; the way: " + string.Join(" ", Enumerable.Range(Mathf.Max(0, i - 8), Mathf.Min(way.Count, i + 3) - Mathf.Max(0, i - 8)).Select(q => (q == i ? "*" : "") + g.Kind[way[q]] + (W(way[q]) - e.Position).ToString("F1")).ToArray()) + "]");
							yield break;
						}
					}
					// (Raft's MouseLook pitch is positive looking down. Swimming up to the surface Jump is held too, as a player would)
					mmJump = jumpT > 0f || (inWaterNow(pl) && d.y > 0.6f); jumpT -= Time.deltaTime;
					float yaw = flat.sqrMagnitude > 0.0001f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : pl.transform.eulerAngles.y;
					if (edge.HasValue)
					{
						Vector3 ef = edge.Value - p; ef.y = 0f;
						// (landed on it, not just risen past its height mid-jump over the shaft)
						CharacterController ecc = pl.GetComponent<CharacterController>();
						if (ef.magnitude < 0.25f || (!Climbing(pl) && p.y > edge.Value.y - 0.15f && (ecc == null || ecc.isGrounded))) edge = null;
						else yaw = Mathf.Atan2(ef.x, ef.z) * Mathf.Rad2Deg;
					}
					if (turnLeft > 0f) { yaw += 60f * turnDir; turnLeft -= Time.deltaTime; }
					bool inWater = pl.PersonController.controllerType == ControllerType.Water;
					float pitch = inWater ? -Mathf.Clamp(Mathf.Atan2(d.y + (kind == 1 ? 0.3f : 0f), Mathf.Max(flat.magnitude, 0.3f)) * Mathf.Rad2Deg, -80f, 80f) : 0f;
					// (going down under water: at least 45 degrees down - a shallow look keeps a swimmer at the surface, into a cave's rim)
					if (inWater && d.y < -0.5f) pitch = Mathf.Max(pitch, 45f);
					Look(pl, yaw, pitch);
					mmWalk = 1f;
					if (riseT > 0f) { mmWalk = 0f; riseT -= Time.deltaTime; }
					MmHold(pl, false);
					ReachTrace(pl, e, "walk to " + (aim - e.Position).ToString("F1") + " jump=" + mmJump);
					yield return null;
					if (!float.IsNaN(hopFrom)) hopPeak = Mathf.Max(hopPeak, ReachFeet(pl).y - hopFrom);
					if ((bad = guard()) != null) { mmWalk = 0f; result(bad); yield break; }
					int wd = watchdog();
					if (wd == 1) { jumpT = 0.4f; turnLeft = 0.7f; turnDir = -turnDir; }
					if (wd == 2) { mmWalk = 0f; mmJump = false; result(wdWhy() + " at " + (p - e.Position).ToString("F1") + " on the way to " + (aim - e.Position).ToString("F1") + " (in the way: " + ReachBlocker(pl, flat.sqrMagnitude > 0.01f ? flat.normalized : d.normalized) + ") [the way: " + string.Join(" ", Enumerable.Range(Mathf.Max(0, i - 8), Mathf.Min(way.Count, i + 3) - Mathf.Max(0, i - 8)).Select(q => (q == i ? "*" : "") + g.Kind[way[q]] + (W(way[q]) - e.Position).ToString("F1")).ToArray()) + "]"); yield break; }
					if (n > 60000) { mmWalk = 0f; result("gave up (too long)"); yield break; }
				}
			}
			mmWalk = 0f; mmJump = false;
			ReachClock(speed, true);
			result(null);
		}

		/// <summary>After a climb to a ladder's top: the nearest spot of a floor between lo and hi (the next spot's height within
		/// 0.35 m, or what a jump off the ladder reaches), at most 1.8 m round the feet with nothing in the way at the floor's
		/// height - a point 0.35 m in past it, to step off toward; of those at one distance, the one most toward the next spot.</summary>
		static Vector3? LadderTopFloor(Vector3 feet, Vector3 aim, float lo, float hi)
		{
			int mask = reachMask != 0 ? reachMask : ~0;
			Vector3 toAim = aim - feet; toAim.y = 0f;
			for (float r = 0.4f; r <= 1.81f; r += 0.2f)
			{
				Vector3? best = null; float bestDot = -2f;
				for (int a = 0; a < 16; a++)
				{
					Vector3 dir = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
					Vector3 q = feet + dir * r;
					RaycastHit h;
					if (!Physics.Raycast(new Vector3(q.x, hi + 0.65f, q.z), Vector3.down, out h, hi - lo + 0.8f, mask, QueryTriggerInteraction.Ignore)) continue;
					if (h.normal.y < 0.64f || h.point.y < lo || h.point.y > hi) continue;
					Vector3 la = new Vector3(feet.x, h.point.y + 0.5f, feet.z), lb = h.point + Vector3.up * 0.5f + dir * 0.35f;
					if (Physics.Linecast(la, lb, mask, QueryTriggerInteraction.Ignore)) continue;
					float dot = toAim.sqrMagnitude > 0.01f ? Vector3.Dot(dir, toAim.normalized) : 0f;
					if (dot > bestDot) { bestDot = dot; best = new Vector3(q.x, h.point.y, q.z) + dir * 0.35f; }
				}
				if (best.HasValue) return best;
			}
			return null;
		}

		/// <summary>The last spot before way[i] (at most 6 back) where the way is in the sea - a swim spot or a floor under the
		/// waves - to walk on again from there; 0 if there is none.</summary>
		static int WayBackToSea(ReachGrid g, List<int> way, int i)
		{
			for (int j = i - 1; j >= 1 && j >= i - 6; j--)
				if (g.Kind[way[j]] == 1 || (g.Kind[way[j]] == 0 && g.Pos[way[j]].y < RSea - 0.3f)) return j;
			return 0;
		}

		static float reachTraceT;
		/// <summary>A vine's use line that cuts it as the default does: every action hides the vine itself or only says something.</summary>
		static bool VineUseIsCut(string onUse, string own)
		{
			var acts = ObjAction.ParseLines((onUse ?? "").Replace("\\n", "\n"));
			bool hides = false;
			foreach (var a in acts)
			{
				if (a.Verb == "message") continue;
				if (a.Verb != "hide") return false;
				if (!(a.Target.Length == 0 || a.Target.Equals("self", StringComparison.OrdinalIgnoreCase) || (own.Length > 0 && a.Target == own))) return false;
				hides = true;
			}
			return hides;
		}

		/// <summary>With CIReachOnly set: the walker's state each 0.2 s (feet, climbing, grounded) - to see one target's walk.</summary>
		static void ReachTrace(Network_Player pl, IslandWorldState.Entry e, string what)
		{
			if (reachOnly == "" || Time.realtimeSinceStartup - reachTraceT < 0.2f) return;
			reachTraceT = Time.realtimeSinceStartup;
			CharacterController cc = pl.GetComponent<CharacterController>();
			Log("  trace " + what + " feet " + (ReachFeet(pl) - e.Position).ToString("F2") + " climb " + Climbing(pl) + " grounded " + (cc != null && cc.isGrounded) + " yaw " + pl.transform.eulerAngles.y.ToString("F0"));
		}

		static bool inWaterNow(Network_Player pl) { return pl.PersonController.controllerType == ControllerType.Water; }

		/// <summary>After a climb to a ladder's top: jump off toward the next spot when its floor is still above the feet (0.25 to
		/// 1.2 m: within a jump) - a hatch with its floor beside or behind the ladder - or, not ahead over the ladder's top, level with
		/// them (down to 0.3 m under). CIReachDismountUnit.</summary>
		internal static bool ReachLadderDismount(bool afterLadderUp, float feetY, float floorY, bool ahead = true)
		{
			float up = floorY - feetY;
			return afterLadderUp && up < 1.2f && (up > 0.25f || (!ahead && up > -0.3f));
		}

		/// <summary>After a climb to a ladder's top: the next spot is more than 0.75 m over the feet Raft holds there - more than a
		/// jump off the ladder reaches (Brinelab's deck 0.67 m up was made, Toll Hill's locker top 1.13 m up never) - so the
		/// walker steps off onto the floor beside the ladder's top first. CIReachDismountUnit.</summary>
		internal static bool ReachLadderStepFirst(float feetY, float nextY) { return nextY - feetY > 0.75f; }

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

		/// <summary>The player let go of Raft's "FirstPersonParent" (not the raft's pivot): as its child the walker did not move.</summary>
		static void ReachUnparent(Network_Player pl)
		{
			if (pl == null || !PlayerMove.IsFirstPersonParent(pl.transform.parent)) return;
			if (reachUnparented++ < 3) Log("(reach walker: the player was a child of FirstPersonParent" + ReachDiag(pl) + " - let go)");
			pl.transform.SetParent(null, true);
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

		/// <summary>Creatures within 6 m of the player (name, distance): a boar in a mine pushes the walker about.</summary>
		static string ReachCreatures(Network_Player pl)
		{
			string s = "";
			Vector3 f = ReachFeet(pl);
			foreach (AI_NetworkBehaviour a in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>())
			{
				if (a == null) continue;
				float dd = (a.transform.position - f).magnitude;
				if (dd < 6f) s += (s.Length > 0 ? ", " : "") + a.name + " " + dd.ToString("F1") + " m";
			}
			return s.Length > 0 ? " [creatures near: " + s + "]" : "";
		}

		/// <summary>(bugfix 11-27) What the stuck walker stands on: its name, the spot and the collider's box (island-local).</summary>
		static string ReachUnder(Network_Player pl, IslandWorldState.Entry e)
		{
			Vector3 f = ReachFeet(pl);
			foreach (RaycastHit h in Physics.SphereCastAll(f + Vector3.up * 0.5f, 0.19f, Vector3.down, 1.5f, reachMask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
			{
				if (h.collider.transform.IsChildOf(pl.transform)) continue;
				Bounds b = h.collider.bounds;
				return " [under the feet: " + BlockerName(h.collider) + " at " + (h.point - e.Position).ToString("F2") + ", its box " + (b.min - e.Position).ToString("F2") + ".." + (b.max - e.Position).ToString("F2") + "]";
			}
			return " [under the feet: nothing within 1 m]";
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
			// (bugfix 11-27: then with back faces on, then as the controller's own capsule from just over the feet, then what it
			// overlaps a little ahead - Brinelab's den mouth stopped the walker 3 times with 'nothing found' (bl7, bl9) - and the
			// ground's height ahead, whatever was found)
			Vector3 f = ReachFeet(pl);
			CharacterController cc = pl.PersonController != null ? pl.PersonController.controller : null;
			float r = cc != null ? cc.radius : 0.2f, top = cc != null ? cc.height : 1.7f;
			Vector3 p1 = f + Vector3.up * (0.12f + r), p2 = f + Vector3.up * Mathf.Max(0.12f + r, top - r);
			Func<RaycastHit, bool> other = h => h.collider != null && !h.collider.transform.IsChildOf(pl.transform);
			// (a cast that starts in a collider gives distance 0 and point (0,0,0): sk13 printed minus the feet's world position as the hit)
			Func<RaycastHit, string> at = h => h.distance <= 0f || h.point == Vector3.zero ?" (overlapping where the cast starts)" : " hit " + (h.point - f).ToString("F2") + " from the feet, normal " + h.normal.ToString("F2");
			string found = null;
			bool was = Physics.queriesHitBackfaces;
			try
			{
				for (int backs = 0; backs < 2 && found == null; backs++)
				{
					Physics.queriesHitBackfaces = backs == 1;
					string side = backs == 1 ? " (by its back face: a one-sided skin met from behind)" : "";
					foreach (float up in new[] { 0.9f, 0.35f, 1.5f })
					{
						RaycastHit h = Physics.SphereCastAll(f + Vector3.up * up, 0.25f, dir, 1.2f, reachMask, QueryTriggerInteraction.Ignore).Where(other).OrderBy(x => x.distance).FirstOrDefault();
						if (h.collider != null) { found = BlockerName(h.collider) + (up < 0.5f ? " (low)" : "") + at(h) + side; break; }
					}
					if (found != null) break;
					RaycastHit c = Physics.CapsuleCastAll(p1, p2, r, dir, 0.8f, reachMask, QueryTriggerInteraction.Ignore).Where(other).OrderBy(x => x.distance).FirstOrDefault();
					if (c.collider != null) { found = BlockerName(c.collider) + " (the body's capsule from " + (0.12f).ToString("F2") + " m up)" + at(c) + side; break; }
				}
				if (found == null)
				{
					Collider o = Physics.OverlapCapsule(p1 + dir * 0.15f, p2 + dir * 0.15f, r, reachMask, QueryTriggerInteraction.Ignore).FirstOrDefault(x => x != null && !x.transform.IsChildOf(pl.transform));
					if (o != null) found = BlockerName(o) + " (the body overlaps it 0.15 m ahead)";
				}
				// (what the controller meets that the grid's mask leaves out - a layer the players or the animals use)
				if (found == null && reachMask != 0 && reachMask != ~0)
				{
					Func<Collider, bool> outside = x => x != null && !x.isTrigger && !x.transform.IsChildOf(pl.transform) && (reachMask & (1 << x.gameObject.layer)) == 0;
					RaycastHit c = Physics.CapsuleCastAll(p1, p2, r, dir, 0.8f, ~0, QueryTriggerInteraction.Ignore).Where(h => outside(h.collider)).OrderBy(x => x.distance).FirstOrDefault();
					Collider o = c.collider ?? Physics.OverlapCapsule(p1 + dir * 0.15f, p2 + dir * 0.15f, r, ~0, QueryTriggerInteraction.Ignore).FirstOrDefault(outside);
					if (o != null) found = BlockerName(o) + " (layer " + LayerMask.LayerToName(o.gameObject.layer) + " " + o.GetType().Name + ", left out of the grid's mask" + (c.collider != null ? "," + at(c) : ", overlapped 0.15 m ahead") + ")";
				}
			}
			finally { Physics.queriesHitBackfaces = was; }
			return (found ?? "nothing found") + ReachGroundAhead(f, dir) + ReachHeadRoom(f, dir, top);
		}

		/// <summary>(bugfix 11-27) The room over the feet here and ahead where it is under 2.2 m (rays up from 0.5 m, back faces
		/// on): a roof a little over the body's top stops the controller (its skin 0.08 m) while every cast below it goes through.</summary>
		static string ReachHeadRoom(Vector3 f, Vector3 dir, float top)
		{
			dir.y = 0f;
			if (dir.sqrMagnitude < 1e-4f) return "";
			dir.Normalize();
			bool was = Physics.queriesHitBackfaces;
			var parts = new List<string>();
			try
			{
				Physics.queriesHitBackfaces = true;
				foreach (float d in new[] { 0f, 0.2f, 0.4f, 0.6f, 1.0f })
					if (Physics.Raycast(f + dir * d + Vector3.up * 0.5f, Vector3.up, out RaycastHit h, 1.7f, reachMask, QueryTriggerInteraction.Ignore))
						parts.Add("+" + d.ToString("F1") + " " + (h.point.y - f.y).ToString("F2"));
			}
			finally { Physics.queriesHitBackfaces = was; }
			return parts.Count == 0 ? "" : " [room over the feet (the body " + (top + 0.08f).ToString("F2") + " with its skin): " + string.Join(", ", parts) + "]";
		}

		/// <summary>(bugfix 11-27) The terrain's height ahead of the feet, as rises over them (a lip a heightfield raises reads here even
		/// where no cast meets it).</summary>
		static string ReachGroundAhead(Vector3 f, Vector3 dir)
		{
			dir.y = 0f;
			if (dir.sqrMagnitude < 1e-4f) return "";
			dir.Normalize();
			foreach (Terrain t in Terrain.activeTerrains)
			{
				if (t == null || t.terrainData == null) continue;
				Vector3 tp = t.transform.position, ts = t.terrainData.size;
				if (f.x < tp.x || f.z < tp.z || f.x > tp.x + ts.x || f.z > tp.z + ts.z) continue;
				var parts = new List<string>();
				foreach (float d in new[] { 0f, 0.2f, 0.4f, 0.6f, 1.0f, 1.5f })
				{
					Vector3 q = f + dir * d;
					// (and where the ground has a hole cut in it - a cave's room: SampleHeight reads the heightfield through it)
					bool hole = false;
					try
					{
						int hr = t.terrainData.holesResolution;
						int hx = Mathf.Clamp((int)((q.x - tp.x) / ts.x * hr), 0, hr - 1), hz = Mathf.Clamp((int)((q.z - tp.z) / ts.z * hr), 0, hr - 1);
						hole = !t.terrainData.GetHoles(hx, hz, 1, 1)[0, 0];
					}
					catch (Exception) { }
					parts.Add("+" + d.ToString("0.0") + " " + (t.SampleHeight(q) + tp.y - f.y).ToString("+0.00;-0.00") + (hole ? " hole" : ""));
				}
				return " [the ground ahead, over the feet: " + string.Join(", ", parts.ToArray()) + "]";
			}
			return "";
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
