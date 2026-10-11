using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
				if (NotABase.IsMatch(n) || land == null || Mathf.Max(b.size.x, b.size.z) < 1.5f || b.size.y < 0.6f || b.min.y < sea - 0.5f) continue;
				float lowest = float.MaxValue, highest = float.MinValue;
				Vector3 worst = Vector3.zero;
				int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(b.size.x, b.size.z)), 3, 20);
				for (int i = 0; i <= steps; i++)
					for (int j = 0; j <= steps; j++)
					{
						var p = new Vector3(Mathf.Lerp(b.min.x + b.size.x * 0.2f, b.max.x - b.size.x * 0.2f, i / (float)steps), b.min.y, Mathf.Lerp(b.min.z + b.size.z * 0.2f, b.max.z - b.size.z * 0.2f, j / (float)steps));
						float g = GapUnder(o, p, land);
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
