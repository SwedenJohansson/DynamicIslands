using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Everything on the islands as they are spawned in a world stands on something (TEST_CATALOGUE IG1-IG4, tools\groundall.ps1):
	/// no structure, cave, prop or plant floating over the land or the sea floor, no base half in the air on a slope, nothing
	/// buried whole, every cave rim on the ground. CIFloating measures the same in the editor; these measure the spawned result
	/// - the land as cropped and placed, the randomizer's extras on Raft's own islands, plan and story islands, new generated ones.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIGroundWorld", docs: "Dev, in a world: every object on the loaded custom islands (the randomizer's extras too) stands on something - nothing more than 0.5 m over what is under it and touching nothing, no base on the ground with part of it more than 0.6 m in the air, nothing buried whole, every cave rim on the ground (IG1). Problems listed per island; all of them in Mods\\DynamicIslands\\ground_world.txt. CIGroundWorld [part of the island name]")]
		public static void GroundWorldCommand(string[] args)
		{
			string only = args != null && args.Length > 0 && args[0] != "*" ? args[0] : "";
			var report = new StringBuilder();
			int islands = 0, bad = 0, problems = 0, objects = 0;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading).ToList())
			{
				if (only.Length > 0 && (e.HostName ?? e.Name ?? "").IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				islands++;
				int n;
				List<string> lines = GroundProblems(e, out n);
				objects += n;
				report.AppendLine(e.Name + ": " + n + " objects, " + lines.Count + " problems");
				foreach (string l in lines) report.AppendLine("  " + l);
				if (lines.Count == 0) continue;
				bad++;
				problems += lines.Count;
				Log("  '" + e.Name + "': " + lines.Count + " problem(s) of " + n + " objects");
				foreach (string l in lines.Take(5)) Log("    " + l);
			}
			File.WriteAllText(Path.Combine(DynamicIslands.assetpath, "ground_world.txt"), report.ToString());
			if (islands == 0) Fail("ground: no loaded custom island" + (only.Length > 0 ? " named *" + only + "*" : ""));
			else if (bad > 0) Fail("ground: " + bad + " of " + islands + " islands with " + problems + " problems (ground_world.txt)");
			else Log("PASS: ground - " + islands + " islands, " + objects + " objects, all on something");
		}

		[ConsoleCommand(name: "CIGroundIslands", docs: "Dev, world (host, a test world 'CI ...'): saved islands brought next to the raft one by one and measured as CIGroundWorld does, then taken away (IG2/IG3). <which>: part of the name, 'plans' (every island of every plan - the story lines), 'gen' (the saved generated ones) or * (all but the test islands, kept copies and randomizer extras). Results in Mods\\DynamicIslands\\ground_islands.txt. CIGroundIslands <which> [most] [skip the first n]")]
		public static void GroundIslandsCommand(string[] args)
		{
			string which = args != null && args.Length > 0 ? args[0] : "*";
			int most = args != null && args.Length > 1 ? int.Parse(args[1]) : 9999;
			int skip = args != null && args.Length > 2 ? int.Parse(args[2]) : 0;
			StartTest(GroundIslandsRoutine(which, most, skip));
		}

		static IEnumerator GroundIslandsRoutine(string which, int most, int skip)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("ground islands: host, in a world"); yield break; }
			var kept = new System.Text.RegularExpressions.Regex("_[0-9a-f]{12}$");
			var saved = new HashSet<string>(IslandSpawner.ListSavedIslands(), StringComparer.OrdinalIgnoreCase);
			IEnumerable<string> pick;
			if (which == "plans") pick = CustomIslandSpawner.AllPlansIslandNames().Where(n => saved.Contains(n));
			else pick = saved.Where(n => !n.StartsWith("ci", StringComparison.OrdinalIgnoreCase) && !kept.IsMatch(n) && !n.StartsWith(WorldRandomizer.ExtrasPrefix, StringComparison.OrdinalIgnoreCase) &&
				(which == "*" || n.IndexOf(which, StringComparison.OrdinalIgnoreCase) >= 0));
			List<string> names = pick.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Skip(skip).Take(most).ToList();
			Log("Ground check of " + names.Count + " saved islands (" + which + ", from " + skip + ")");
			var report = new StringBuilder();
			int done = 0, bad = 0, problems = 0, missed = 0;
			foreach (string name in names)
			{
				var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
				if (left.Count > 0) { IslandWorldState.RemoveIds(left, true); IslandCache.Forget(); yield return new WaitForSeconds(1f); }
				Vector3? spot = ScSpot(name, 400f);
				if (!spot.HasValue) { Log("  " + name + ": no open sea near the raft"); missed++; continue; }
				var made = new List<IslandWorldState.Entry>();
				yield return ScBring(name, spot.Value, made);
				IslandWorldState.Entry e = made.LastOrDefault();
				if (e == null || e.Root == null) { Log("  " + name + ": didn't come"); missed++; continue; }
				yield return new WaitForSeconds(2f); // (the cave mountains and Raft's physics settle)
				int n;
				List<string> lines = GroundProblems(e, out n);
				done++;
				report.AppendLine(name + ": " + n + " objects, " + lines.Count + " problems");
				foreach (string l in lines) report.AppendLine("  " + l);
				if (lines.Count > 0)
				{
					bad++;
					problems += lines.Count;
					Log("  " + name + ": " + lines.Count + " problem(s) of " + n + " objects");
					foreach (string l in lines.Take(3)) Log("    " + l);
				}
				IslandWorldState.RemoveIds(made.Select(x => x.Id).ToList(), true);
				IslandCache.Forget();
				yield return new WaitForSeconds(1f);
			}
			File.WriteAllText(Path.Combine(DynamicIslands.assetpath, "ground_islands.txt"), report.ToString());
			string sum = done + " of " + names.Count + " islands measured" + (missed > 0 ? " (" + missed + " didn't come)" : "");
			if (names.Count == 0) Fail("ground islands: none matched '" + which + "'");
			else if (bad > 0 || missed > 0) Fail("ground islands: " + bad + " with " + problems + " problems; " + sum + " (ground_islands.txt)");
			else Log("PASS: ground islands - " + sum + ", all on something");
		}

		[ConsoleCommand(name: "CIGroundTypes", docs: "Dev, world (host, a test world 'CI ...'): brand-new islands of each map type made next to the raft by the generator as it is now, measured as CIGroundWorld does, then taken away (IG6) - a saved generated island keeps what an older generator made. Results in Mods\\DynamicIslands\\ground_types.txt. CIGroundTypes [types, comma-separated, or *] [islands of each, 2]")]
		public static void GroundTypesCommand(string[] args)
		{
			string which = args != null && args.Length > 0 ? args[0] : "*";
			int each = args != null && args.Length > 1 ? int.Parse(args[1]) : 2;
			StartTest(GroundTypesRoutine(which, each));
		}

		static IEnumerator GroundTypesRoutine(string which, int each)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("ground types: host, in a world"); yield break; }
			List<string> types = (which == "*" ? MapTypes.All.Select(t => t.Name).Where(t => t != "random") : which.Split(',').Select(t => t.Trim()).Where(t => MapTypes.Get(t) != null)).ToList();
			if (types.Count == 0) { Fail("ground types: no map type '" + which + "'"); yield break; }
			Log("Ground check of new islands: " + string.Join(", ", types.ToArray()) + ", " + each + " of each");
			var report = new StringBuilder();
			int done = 0, bad = 0, problems = 0, missed = 0;
			foreach (string type in types)
				for (int i = 0; i < each; i++)
				{
					Network_Player player = RAPI.GetLocalPlayer();
					Vector3 raft = CustomIslandSpawner.RaftPosition ?? (player != null ? player.transform.position : Vector3.zero);
					IslandWorldState.Entry e = SpawnTypeNear(type, raft);
					for (float t = 0; e != null && t < 90f && e.Root == null && !e.Failed; t += 1f) yield return new WaitForSeconds(1f);
					if (e == null || e.Root == null)
					{
						Log("  " + type + ": " + (e == null ? "no open sea near the raft" : "didn't come"));
						missed++;
						if (e != null) IslandWorldState.RemoveIds(new[] { e.Id }, true);
						continue;
					}
					yield return new WaitForSeconds(2f); // (the cave mountains and Raft's physics settle)
					int n;
					List<string> lines = GroundProblems(e, out n);
					done++;
					report.AppendLine(type + " (" + e.Name + "): " + n + " objects, " + lines.Count + " problems");
					foreach (string l in lines) report.AppendLine("  " + l);
					if (lines.Count > 0)
					{
						bad++;
						problems += lines.Count;
						Log("  " + e.Name + ": " + lines.Count + " problem(s) of " + n + " objects");
						foreach (string l in lines.Take(3)) Log("    " + l);
					}
					IslandWorldState.RemoveIds(new[] { e.Id }, true);
					IslandCache.Forget();
					yield return new WaitForSeconds(1f);
				}
			File.WriteAllText(Path.Combine(DynamicIslands.assetpath, "ground_types.txt"), report.ToString());
			string sum = done + " of " + types.Count * each + " new islands measured" + (missed > 0 ? " (" + missed + " didn't come)" : "");
			if (bad > 0 || missed > 0) Fail("ground types: " + bad + " with " + problems + " problems; " + sum + " (ground_types.txt)");
			else Log("PASS: ground types - " + sum + ", all on something");
		}

		/// <summary>What stands on a mount narrower than itself: a dish on a mast, an antenna on a roof - one mesh, its box wider
		/// than its foot (Balboa Remade's and Coral Observatory's dishes on their masts read "partly in the air", 2026-10-12).</summary>
		static readonly Regex OnAMount = new Regex("Sattelite|Satellite|Dish|Antenna", RegexOptions.IgnoreCase);

		/// <summary>What on a spawned island doesn't stand on anything: one line per problem; <paramref name="count"/> = the objects measured.</summary>
		static List<string> GroundProblems(IslandWorldState.Entry e, out int count)
		{
			var lines = new List<string>();
			count = 0;
			Transform objects = e.Root.transform.Find("Objects");
			if (objects == null) return lines;
			// (a flying island's objects stand on its land like any other; only the sea under it is far away - it is measured the same)
			Terrain land = e.Root.GetComponentInChildren<Terrain>();
			Vector3 mid = e.Position;
			float sea = 0f; // (Raft's sea level in the world)
			Physics.SyncTransforms();
			foreach (Transform o in objects)
			{
				if (o == null || !o.gameObject.activeInHierarchy) continue;
				string n = o.name.Replace("(Clone)", "").Trim();
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Note_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_") ||
					n.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0 || Hangs.IsMatch(n)) continue;
				// (cave pieces: their rim, as CICaveRims - AU83)
				if (RaftProps.Get(n) != null && RaftProps.Get(n).IsCave)
				{
					count++;
					string at;
					float gap = DenRimGap(o, out at);
					if (gap >= 0.6f) lines.Add("cave rim off the ground: " + n + " - " + at);
					// (and from inside: nothing seen through its walls or roof - X7)
					Vector3 eye, look;
					if (CaveSeeThrough(o, RaftProps.Get(n), out at, out eye, out look) > 0) lines.Add("cave seen through from inside: " + n + " - " + at);
					continue;
				}
				Renderer[] rs = o.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (rs.Length == 0) continue;
				count++;
				float bottom = rs.Min(r => r.bounds.min.y);
				Bounds whole = rs[0].bounds;
				foreach (Renderer r in rs) whole.Encapsulate(r.bounds);
				// (its base: the parts that reach down to its bottom - not a windmill's sails above it)
				Renderer[] baseParts = rs.Where(r => r.bounds.min.y < bottom + 0.6f).ToArray();
				Bounds b = baseParts[0].bounds;
				foreach (Renderer r in baseParts) b.Encapsulate(r.bounds);
				string where = Num(b.center.x - mid.x) + " " + Num(b.center.z - mid.z) + " h=" + Num(b.min.y - sea);
				float groundMid = land != null ? land.SampleHeight(b.center) + land.transform.position.y : float.MinValue;
				// Buried whole: its top under the land (not Raft's rocks and corals, which are set into it; not what is under the sea)
				if (!NotABase.IsMatch(n) && land != null && whole.max.y < groundMid - 0.05f && whole.max.y > sea - 1f)
				{ lines.Add("buried: " + n + " at " + where + ", its top " + Num(groundMid - whole.max.y) + " m under the ground"); continue; }
				// Floating: nothing at its base (another object, a wall it hangs on, the land) and the first thing under it more than 0.5 m down
				float gapUnder = GapUnder(o, new Vector3(b.center.x, b.min.y, b.center.z), land);
				bool touches = Touches(o, b);
				bool inSea = b.min.y <= sea + 0.3f; // (a dock post, a buoy, a wreck: in the water, held by it)
				if (!touches && !inSea && gapUnder > 0.5f)
				{ lines.Add("floating: " + n + " at " + where + ", " + (gapUnder > 999f ? "nothing under it" : Num(gapUnder) + " m over what is under it")); continue; }
				// Partly in the air: on the ground with part of its base, the rest more than 0.6 m over it (a house on a slope)
				if (NotABase.IsMatch(n) || OnAMount.IsMatch(n) || land == null || Mathf.Max(b.size.x, b.size.z) < 1.5f || b.size.y < 0.6f || b.min.y < sea - 0.5f) continue;
				float lowest = float.MaxValue, highest = float.MinValue;
				Vector3 worst = Vector3.zero;
				int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(b.size.x, b.size.z)), 3, 20);
				// (its base in its own frame: a turned machine's world box reaches past its tracks - VP_Excavator read the ground
				// beside them, 2026-10-12)
				Vector3 lmin = Vector3.one * float.MaxValue, lmax = Vector3.one * float.MinValue;
				foreach (Renderer r in baseParts)
				{
					MeshFilter mf = r.GetComponent<MeshFilter>();
					if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name.StartsWith("Combined Mesh")) { lmin = Vector3.one; lmax = Vector3.zero; break; }
					Bounds mb = mf.sharedMesh.bounds;
					for (int c = 0; c < 8; c++)
					{
						Vector3 l = o.InverseTransformPoint(r.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f))));
						lmin = Vector3.Min(lmin, l); lmax = Vector3.Max(lmax, l);
					}
				}
				bool own = lmin.x <= lmax.x && lmin.z <= lmax.z;
				for (int i = 0; i <= steps; i++)
					for (int j = 0; j <= steps; j++)
					{
						var p = new Vector3(Mathf.Lerp(b.min.x + b.size.x * 0.2f, b.max.x - b.size.x * 0.2f, i / (float)steps), b.min.y, Mathf.Lerp(b.min.z + b.size.z * 0.2f, b.max.z - b.size.z * 0.2f, j / (float)steps));
						if (own)
						{
							Vector3 sx = lmax - lmin, w = o.TransformPoint(new Vector3(Mathf.Lerp(lmin.x + sx.x * 0.2f, lmax.x - sx.x * 0.2f, i / (float)steps), lmin.y, Mathf.Lerp(lmin.z + sx.z * 0.2f, lmax.z - sx.z * 0.2f, j / (float)steps)));
							p = new Vector3(w.x, b.min.y, w.z);
						}
						float g = GapUnder(o, p, land);
						// (a railing straddling a deck's edge, as Raft's own towers: held by the face over the deck)
						if (b.size.x < 0.3f) g = Mathf.Min(GapUnder(o, new Vector3(b.min.x, p.y, p.z), land), GapUnder(o, new Vector3(b.max.x, p.y, p.z), land));
						if (b.size.z < 0.3f) g = Mathf.Min(GapUnder(o, new Vector3(p.x, p.y, b.min.z), land), GapUnder(o, new Vector3(p.x, p.y, b.max.z), land));
						if (g < lowest) lowest = g;
						if (g > highest) { highest = g; worst = p; }
					}
				if (lowest <= 0.4f && highest > 0.6f)
					lines.Add("partly in the air: " + n + " at " + where + ", its base up to " + (highest > 999f ? "over nothing" : Num(highest) + " m over the ground") + " (at " + Num(worst.x - mid.x) + " " + Num(worst.z - mid.z) + ")");
			}
			return lines;
		}

		/// <summary>How far the first thing under <paramref name="foot"/> that isn't <paramref name="self"/> is (the land, another object,
		/// the sea floor); 9999 = nothing.</summary>
		[ConsoleCommand(name: "CICaveInside", docs: "Dev, editor or world: every cave piece (the editor's island, or the loaded custom islands) looked round from inside at eye height - nothing seen through: no face seen from its back (Raft draws the shells, the ground and the hill over them from one side only), no sky but out through an open end (X7). CICaveInside [part of the name] [shots: a picture of each piece's worst look, shot_caveinside_<n>.png]")]
		public static void CaveInsideCommand(string[] args)
		{
			string only = args != null && args.Length > 0 && args[0] != "*" ? args[0] : "";
			bool shots = args != null && args.Length > 1 && args[1] == "shots";
			StartTest(CaveInsideRoutine(only, shots));
		}

		static IEnumerator CaveInsideRoutine(string only, bool shots)
		{
			var caves = new List<KeyValuePair<string, Transform>>(); // (island: piece)
			if (DynamicIslands.InEditor())
			{
				foreach (EditorGameObject e in PlacedEditorObjects())
					if (e != null && RaftProps.Get(e.GameObjectName ?? "") != null && RaftProps.Get(e.GameObjectName).IsCave) caves.Add(new KeyValuePair<string, Transform>("editor", e.transform));
			}
			else
				foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading).ToList())
					foreach (Transform t in e.Root.GetComponentsInChildren<Transform>().Where(t => RaftProps.Get(t.name.Replace("(Clone)", "").Trim()) != null && RaftProps.Get(t.name.Replace("(Clone)", "").Trim()).IsCave))
						caves.Add(new KeyValuePair<string, Transform>(e.Name, t));
			caves = caves.Where(c => only.Length == 0 || c.Key.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0 || c.Value.name.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			int n = 0, bad = 0;
			foreach (var c in caves)
			{
				if (c.Value == null) continue;
				n++;
				string name = c.Value.name.Replace("(Clone)", "").Trim(), detail;
				Vector3 eye, look;
				int seen = CaveSeeThrough(c.Value, RaftProps.Get(name), out detail, out eye, out look);
				Log("  " + n + " '" + c.Key + "' " + name + " at " + c.Value.position.ToString("F0") + ": " + detail);
				if (seen > 0) bad++;
				if (shots && eye != Vector3.zero) yield return CameraShot(eye, eye + look * 5f, "caveinside_" + n);
				yield return null;
			}
			if (n == 0) Fail("cave inside: no cave pieces" + (only.Length > 0 ? " named *" + only + "*" : ""));
			else if (bad > 0) Fail("cave inside: " + bad + " of " + n + " cave pieces seen through from inside (above)");
			else Log("PASS: cave inside - " + n + " cave pieces, nothing seen through from inside");
		}

		/// <summary>
		/// A cave piece seen from where a player stands in it (X7): from eye height over every floor under its roof, every look
		/// round meets the shell's inside, the ground from above or something else from its front - not the back of a face (Raft
		/// draws its shells, the ground and the hill over them from one side only: the back of a face is a hole to the outside) -
		/// and no sky but out through an open end. The looks that see through (0: none); detail: how many, and the worst place.
		/// </summary>
		static int CaveSeeThrough(Transform cave, PropInfo p, out string detail, out Vector3 eye, out Vector3 look)
		{
			eye = look = Vector3.zero;
			List<Collider> own = cave.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger).ToList();
			if (p == null || !p.IsCave || own.Count == 0) { detail = "not measured: no colliders"; return 0; }
			Bounds b = own[0].bounds;
			foreach (Collider c in own) b.Encapsulate(c.bounds);
			Vector3 along = cave.rotation * (p.Axis == 0 ? Vector3.right : Vector3.forward);
			along.y = 0f; along.Normalize();
			// (what can be seen: not animals, players, or the editor's handles of zones)
			Func<Collider, bool> counts = c => c != null && !c.isTrigger && c.GetComponentInParent<AI_NetworkBehaviour>() == null && c.GetComponentInParent<Network_Player>() == null &&
				(c.GetComponentInParent<EditorGameObject>() == null || !(c.GetComponentInParent<EditorGameObject>().GameObjectName ?? "").StartsWith("Zone_"));
			Func<Vector3, Vector3, float, bool, RaycastHit?> first = (from, dir, len, backs) =>
			{
				Physics.queriesHitBackfaces = backs;
				RaycastHit? best = null;
				foreach (RaycastHit h in Physics.RaycastAll(from, dir, len, ~0, QueryTriggerInteraction.Ignore))
					if (counts(h.collider) && (best == null || h.distance < best.Value.distance)) best = h;
				return best;
			};
			var eyes = new List<Vector3>();
			var looks = new List<Vector3>();
			foreach (float pitch in new[] { -20f, 0f, 15f, 35f, 60f })
				for (int i = 0; i < 16; i++) looks.Add(Quaternion.Euler(-pitch, i * 22.5f, 0f) * Vector3.forward);
			looks.Add(Vector3.up);
			int bad = 0, worstBad = 0;
			string worst = "";
			var what = new Dictionary<string, int>();
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.SyncTransforms();
			try
			{
				for (float x = b.min.x + 0.5f; x < b.max.x; x += 1.5f)
					for (float z = b.min.z + 0.5f; z < b.max.z; z += 1.5f)
					{
						Physics.queriesHitBackfaces = false;
						RaycastHit[] downs = Physics.RaycastAll(new Vector3(x, b.max.y + 2f, z), Vector3.down, b.size.y + 4f, ~0, QueryTriggerInteraction.Ignore);
						foreach (RaycastHit h in downs)
						{
							if (!counts(h.collider) || h.normal.y < 0.6f) continue; // (somewhere to stand)
							// (room to stand: nothing within 1.7 m over the floor)
							if (first(h.point + Vector3.up * 0.1f, Vector3.up, 1.6f, true) != null) continue;
							Vector3 e = h.point + Vector3.up * 1.6f;
							// (in the cave: its roof over it, seen from below - on the shell's own floor, or on the ground with the
							// shell's inside over it; not in a gap under the shell, where its floor's back is over it)
							RaycastHit? up = first(e, Vector3.up, 30f, true), upFront = first(e, Vector3.up, 30f, false);
							if (up == null || !own.Contains(up.Value.collider)) continue;
							if (!own.Contains(h.collider) && (upFront == null || upFront.Value.distance > up.Value.distance + 0.05f)) continue;
							eyes.Add(e);
						}
					}
				if (eyes.Count > 150) eyes = eyes.Where((e, i) => i % (eyes.Count / 150 + 1) == 0).ToList();
				foreach (Vector3 e in eyes)
				{
					int here = 0;
					Vector3 hereLook = Vector3.zero;
					string hereWhat = "";
					foreach (Vector3 d in looks)
					{
						RaycastHit? any = first(e, d, 80f, true), front = first(e, d, 80f, false);
						bool sky = any == null;
						if (sky)
						{
							float o = Vector3.Dot(new Vector3(d.x, 0f, d.z), along);
							if ((p.OpenPlus && o > 0.25f) || (p.OpenMinus && o < -0.25f)) continue; // (out through an open end)
						}
						else if (any.Value.distance < 0.3f || (front != null && front.Value.distance <= any.Value.distance + 0.05f)) continue; // (a face from its front)
						string w = sky ? "sky" : (own.Contains(any.Value.collider) ? "the shell's back" : (any.Value.collider is TerrainCollider ? "the ground from below" : any.Value.collider.name + "'s back"));
						what[w] = what.ContainsKey(w) ? what[w] + 1 : 1;
						bad++;
						if (here++ == 0) { hereLook = d; hereWhat = w + (sky ? "" : " " + any.Value.distance.ToString("F1") + " m off"); }
					}
					if (here > worstBad)
					{
						worstBad = here;
						eye = e;
						look = hereLook;
						Vector3 l = cave.InverseTransformPoint(e);
						worst = here + " of " + looks.Count + " looks at " + e.ToString("F1") + " (in the piece " + l.ToString("F1") + "), e.g. " + hereWhat + " looking " +
							(Mathf.Atan2(hereLook.x, hereLook.z) * Mathf.Rad2Deg).ToString("F0") + "° round, " + (Mathf.Asin(hereLook.y) * Mathf.Rad2Deg).ToString("F0") + "° up";
					}
				}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			if (eyes.Count == 0) { detail = "no place to stand under its roof found"; return 0; }
			detail = eyes.Count + " places to stand, " + (eyes.Count * looks.Count) + " looks: " + bad + " see through" +
				(bad > 0 ? " (" + string.Join(", ", what.OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value).ToArray()) + "); worst " + worst : "");
			return bad;
		}

		static float GapUnder(Transform self, Vector3 foot, Terrain land)
		{
			float best = float.MaxValue;
			foreach (RaycastHit h in Physics.RaycastAll(foot + Vector3.up * 0.3f, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
				if (!h.collider.transform.IsChildOf(self) && foot.y - h.point.y < best) best = Mathf.Max(0f, foot.y - h.point.y);
			if (land != null)
			{
				float g = foot.y - (land.SampleHeight(foot) + land.transform.position.y);
				if (g > -0.3f && g < best) best = Mathf.Max(0f, g);
				// (its foot under the land's surface: set into the ground - a rock sunk into a beach. The ray starts under the
				// land then and finds nothing, which read as "nothing under it")
				Vector3 lp = land.transform.position, ls = land.terrainData.size;
				if (g <= -0.3f && foot.x >= lp.x && foot.x <= lp.x + ls.x && foot.z >= lp.z && foot.z <= lp.z + ls.z) best = 0f;
			}
			return best == float.MaxValue ? 9999f : best;
		}

		/// <summary>Whether <paramref name="self"/>'s base touches a collider of something else (stands on it, or hangs on a wall).</summary>
		static bool Touches(Transform self, Bounds b)
		{
			var half = new Vector3(Mathf.Max(0.05f, b.extents.x * 0.9f), 0.15f, Mathf.Max(0.05f, b.extents.z * 0.9f));
			if (Physics.OverlapBox(new Vector3(b.center.x, b.min.y, b.center.z), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(self))) return true;
			// (hung on a wall or a post: its sides touch something)
			return Physics.OverlapBox(b.center, b.extents + Vector3.one * 0.08f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(self) && c.GetComponent<Terrain>() == null);
		}
	}
}
