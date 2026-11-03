using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
	/// The full test sweep's checks (_ProjectDocs/plans/SWEEP_PLAN.md, 2026-11-03): one island at a time, each writing a file
	/// in Mods\DynamicIslands that tools/sweep.sh gathers into SWEEP_RESULTS.csv.
	///   CIReachTraps (T1)     the reach grid alone: places a player gets into but not out of, teleports
	///   CIInsideIslands (T2)  trees and rocks buried on slopes, props in props, quest targets inside solids
	///   CIQuestNeeds (T5)     every quest step has its target on the island in number, every key item can be got; live animals
	///   CIDensityIslands (T8) things per 1000 m2 against Raft's own islands, one line per island, from the saved files
	/// </summary>
	public static partial class DevTests
	{
		static string F1(float v) { return v.ToString("F1", Inv); }

		/// <summary>The saved islands a sweep command means: part of the name (several joined by |, _ for a space), 'plans', or *
		/// (all but the test islands, kept copies and randomizer extras) - as CIGroundIslands picks them.</summary>
		static List<string> SweepNames(string which, int most, int skip)
		{
			var kept = new Regex("_[0-9a-f]{12}$");
			var saved = new HashSet<string>(IslandSpawner.ListSavedIslands(), StringComparer.OrdinalIgnoreCase);
			string[] parts = which.Split('|').Select(p => p.Replace('_', ' ').Trim()).Where(p => p.Length > 0).ToArray();
			IEnumerable<string> pick;
			if (which == "plans") pick = CustomIslandSpawner.AllPlansIslandNames().Where(n => saved.Contains(n));
			else pick = saved.Where(n => !n.StartsWith("ci", StringComparison.Ordinal) && !kept.IsMatch(n) && !n.StartsWith(WorldRandomizer.ExtrasPrefix, StringComparison.OrdinalIgnoreCase) &&
				(which == "*" || parts.Any(p => n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)));
			return pick.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Skip(skip).Take(most).ToList();
		}

		static void SweepArgs(string[] args, out string which, out int most, out int skip)
		{
			which = args != null && args.Length > 0 ? args[0] : "*";
			most = args != null && args.Length > 1 ? int.Parse(args[1]) : 9999;
			skip = args != null && args.Length > 2 ? int.Parse(args[2]) : 0;
		}

		static void SweepWrite(string file, StringBuilder report)
		{
			try { File.WriteAllText(Path.Combine(DynamicIslands.assetpath, file), report.ToString()); }
			catch (Exception ex) { Log("  (" + file + ": " + ex.Message + ")"); }
		}

		// ---------------------------------------------------------------- T1: traps on the reach grid

		[ConsoleCommand(name: "CIReachTraps", docs: "Dev, world (host, a test world 'CI ...'): sweep T1 - CIReach's grid alone (no walks, as CIReachSpeed 0) over saved islands one by one: the places a player gets into but not out of (clusters of 6+ grid spots, island-relative x z, heights, size, the closest way out) and the used objects that teleport a player. One line 'TRAPS <island> <n>' per island. <which> as CIGroundIslands. Results in Mods\\DynamicIslands\\reach_traps.txt (and each island's reach\\<island>.txt). CIReachTraps <which> [most] [skip]")]
		public static void ReachTrapsCommand(string[] args)
		{
			string which; int most, skip;
			SweepArgs(args, out which, out most, out skip);
			StartTest(ReachTrapsRoutine(which, most, skip));
		}

		static IEnumerator ReachTrapsRoutine(string which, int most, int skip)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("CIReachTraps: host, in a world"); yield break; }
			List<string> names = SweepNames(which, most, skip);
			if (names.Count == 0) { Fail("CIReachTraps: no saved island matches '" + which + "'"); yield break; }
			yield return EnsureAlive();
			int speed0 = reachSpeed; bool controls0 = reachControls;
			reachSpeed = 0; reachControls = false;
			var report = new StringBuilder();
			int done = 0, trapped = 0, traps = 0, missed = 0;
			Log("reach traps of " + names.Count + " saved islands (" + which + ", from " + skip + "), the grid alone");
			foreach (string name in names)
			{
				var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
				if (left.Count > 0) yield return RemoveIslandsAndWait(left);
				Vector3? spot = ScSpot(name, 400f, IslandSpawner.ElevationOf(name));
				if (!spot.HasValue) { report.AppendLine("TRAPS " + name + " ? (no open sea near the raft)"); Log("  " + name + ": no open sea near the raft"); missed++; continue; }
				var made = new List<IslandWorldState.Entry>();
				yield return ScBring(name, spot.Value, made);
				IslandWorldState.Entry e = made.LastOrDefault();
				if (e == null || e.Root == null) { report.AppendLine("TRAPS " + name + " ? (didn't come)"); Log("  " + name + ": didn't come"); missed++; continue; }
				yield return WaitIslandSettled(e, 0.5f, 3f);
				// (the player next to it, so it stays loaded - as CIReach)
				PlayerMove.To(RAPI.GetLocalPlayer(), e.Position + new Vector3(0f, 2f, -LandRadiusOf(e) - 20f));
				yield return new WaitForSeconds(1f);
				string host = e.HostName ?? name;
				int fails = 0;
				yield return ReachAnalyze(e, ReachPlayOffset(name), "traps", n => fails = n);
				yield return RemoveIslandsAndWait(made.Select(x => x.Id).ToList());
				done++;
				// (ReachAnalyze's own file holds every line: the trap and teleport lines taken from it)
				string file = Path.Combine(Path.Combine(DynamicIslands.assetpath, "reach"), string.Concat(host.Split(Path.GetInvalidFileNameChars())) + ".txt");
				string[] lines = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
				List<string> trapLines = lines.Where(l => l.Contains("| trap at ")).ToList();
				List<string> tele = lines.Where(l => l.Contains("| teleports |")).ToList();
				report.AppendLine("TRAPS " + name + " " + trapLines.Count + (lines.Length == 0 ? " (no reach file)" : ""));
				foreach (string l in trapLines.Concat(tele)) report.AppendLine("  " + l);
				if (trapLines.Count > 0) { trapped++; traps += trapLines.Count; }
				Log("TRAPS " + name + " " + trapLines.Count);
				foreach (string l in trapLines.Take(3)) Log("    " + l);
			}
			reachSpeed = speed0; reachControls = controls0;
			OnRaftCommand();
			SweepWrite("reach_traps.txt", report);
			string sum = done + " of " + names.Count + " islands measured" + (missed > 0 ? " (" + missed + " didn't come)" : "");
			if (trapped > 0 || missed > 0) Fail("reach traps: " + traps + " trap(s) on " + trapped + " island(s); " + sum + " (reach_traps.txt)");
			else Log("PASS: reach traps - " + sum + ", no place a player gets into but not out of");
		}

		// ---------------------------------------------------------------- T2: things inside things

		static readonly IslandCheck InsideCheck = new IslandCheck { Label = "inside", File = "inside", Pass = "nothing buried on a slope, no prop in a prop, no target in a solid", Measure = InsideProblems };

		[ConsoleCommand(name: "CIInsideIslands", docs: "Dev, world (host, a test world 'CI ...'): sweep T2 - saved islands brought next to the raft one by one: trees with half their trunk or more in the ground and rocks with half their footprint under the land (buried on a slope); props, chests and furniture more than 12 cm into one another (their colliders, Physics.ComputePenetration - building pieces against things are CIOverlapIslands'); quest targets (notes, chests, used objects, islanders) under the ground or inside a solid. <which> as CIGroundIslands. Results in Mods\\DynamicIslands\\inside_islands.txt. CIInsideIslands <which> [most] [skip] [keep]")]
		public static void InsideIslandsCommand(string[] args) { IslandsCheck(args, InsideCheck); }

		[ConsoleCommand(name: "CIInsideWorld", docs: "Dev, in a world: sweep T2 on the loaded custom islands (as CIInsideIslands). Results in Mods\\DynamicIslands\\inside_world.txt. CIInsideWorld [part of the island name]")]
		public static void InsideWorldCommand(string[] args) { WorldCheck(args, InsideCheck); }

		static readonly Regex SweepTree = new Regex("Tree|Palm|Pine|Birch|Oak|Bamboo", RegexOptions.IgnoreCase);
		static readonly Regex SweepRock = new Regex("Rock|Boulder", RegexOptions.IgnoreCase);
		/// <summary>How far one prop must be inside another to count (a chest pushed against a crate: not).</summary>
		const float PropMargin = 0.12f;

		static string At(IslandWorldState.Entry e, Vector3 p) { Vector3 r = p - e.Position; return F1(r.x) + " " + F1(r.z) + " h=" + F1(r.y); }

		static List<string> InsideProblems(IslandWorldState.Entry e, out int count)
		{
			var lines = new List<string>();
			Physics.SyncTransforms();
			if (reachMask == 0) reachMask = ~0;
			Terrain ground = e.Root.GetComponentInChildren<Terrain>();
			List<IslandObjectRef> refs = e.Root.GetComponentsInChildren<IslandObjectRef>(false).ToList();
			count = refs.Count;
			Func<Vector3, float> groundY = p => ground.SampleHeight(p) + ground.transform.position.y;

			// trees and rocks buried on a slope: the trunk (colliders) half in the ground; half a rock's footprint with the land over its top
			if (ground != null)
				foreach (IslandObjectRef r in refs)
				{
					string n = r.ObjectName ?? "";
					bool tree = SweepTree.IsMatch(n), rock = !tree && SweepRock.IsMatch(n) && !n.StartsWith("Creature_");
					if (!tree && !rock) continue;
					List<OverlapBox> boxes = tree ? ColliderBoxes(r.transform) : MeshBoxes(r.transform, true);
					if (boxes == null || boxes.Count == 0) continue;
					Bounds b = WorldBounds(boxes);
					if (b.size.y < 0.3f) continue;
					if (tree)
					{
						float buried = Mathf.Clamp01((groundY(b.center) - b.min.y) / b.size.y);
						if (buried >= 0.5f) lines.Add("buried tree " + n + " at " + At(e, b.center) + ": " + (buried * 100f).ToString("F0") + "% of its trunk in the ground (" + F1(b.size.y) + " m tall)");
					}
					else
					{
						int under = 0;
						for (int i = 0; i < 5; i++)
							for (int j = 0; j < 5; j++)
								if (groundY(new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 4f), 0f, Mathf.Lerp(b.min.z, b.max.z, j / 4f))) >= b.max.y - 0.05f) under++;
						if (under >= 13) lines.Add("buried rock " + n + " at " + At(e, b.center) + ": the land over its top on " + (under * 4) + "% of its footprint (" + F1(b.size.x) + "x" + F1(b.size.z) + " m)");
					}
				}

			// props in props: colliders of things that are neither building pieces, the land's own nor plants
			var solids = new List<KeyValuePair<IslandObjectRef, Collider>>();
			foreach (IslandObjectRef r in refs)
			{
				string n = r.ObjectName ?? "";
				if (n.StartsWith("Creature_") || OverlapIgnore.IsMatch(n) || Structural.IsMatch(n) || Vegetation.IsMatch(n)) continue;
				foreach (Collider c in r.GetComponentsInChildren<Collider>(false))
				{
					if (!c.enabled || c.isTrigger || c is TerrainCollider || c.GetComponentInParent<IslandObjectRef>() != r) continue;
					Vector3 s = c.bounds.size;
					if (Mathf.Max(s.x, s.y, s.z) >= OverlapBig) continue;
					solids.Add(new KeyValuePair<IslandObjectRef, Collider>(r, c));
				}
			}
			const float hashCell = 4f;
			var cells = new Dictionary<long, List<int>>();
			for (int i = 0; i < solids.Count; i++)
			{
				Bounds b = solids[i].Value.bounds;
				for (int x = Mathf.FloorToInt(b.min.x / hashCell); x <= Mathf.FloorToInt(b.max.x / hashCell); x++)
					for (int z = Mathf.FloorToInt(b.min.z / hashCell); z <= Mathf.FloorToInt(b.max.z / hashCell); z++)
					{
						long key = ((long)(x + 100000) << 32) | (long)(z + 100000);
						List<int> l;
						if (!cells.TryGetValue(key, out l)) cells[key] = l = new List<int>();
						l.Add(i);
					}
			}
			var tried = new HashSet<long>();
			var deepest = new Dictionary<long, KeyValuePair<float, Vector3>>();
			foreach (List<int> l in cells.Values)
				for (int a = 0; a < l.Count; a++)
					for (int b2 = a + 1; b2 < l.Count; b2++)
					{
						int i = Mathf.Min(l[a], l[b2]), j = Mathf.Max(l[a], l[b2]);
						IslandObjectRef ra = solids[i].Key, rb = solids[j].Key;
						if (ra == rb || ra.transform.IsChildOf(rb.transform) || rb.transform.IsChildOf(ra.transform)) continue;
						if (!tried.Add(((long)i << 32) | (uint)j)) continue;
						Collider ca = solids[i].Value, cb = solids[j].Value;
						if (!ca.bounds.Intersects(cb.bounds)) continue;
						bool convexA = !(ca is MeshCollider) || ((MeshCollider)ca).convex, convexB = !(cb is MeshCollider) || ((MeshCollider)cb).convex;
						if (!convexA && !convexB) continue;
						Vector3 dir; float dist;
						if (!Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation, cb, cb.transform.position, cb.transform.rotation, out dir, out dist) || dist <= PropMargin) continue;
						long pair = ((long)Mathf.Min(ra.GetInstanceID(), rb.GetInstanceID()) << 32) ^ (uint)Mathf.Max(ra.GetInstanceID(), rb.GetInstanceID());
						KeyValuePair<float, Vector3> was;
						if (!deepest.TryGetValue(pair, out was) || dist > was.Key)
						{
							deepest[pair] = new KeyValuePair<float, Vector3>(dist, ca.bounds.center);
							pairNames[pair] = ra.ObjectName + " #" + ra.Index + " in " + rb.ObjectName + " #" + rb.Index;
						}
					}
			foreach (var kv in deepest.OrderByDescending(x => x.Value.Key).Take(40))
				lines.Add("prop in prop: " + pairNames[kv.Key] + " by " + kv.Value.Key.ToString("F2", Inv) + " m at " + At(e, kv.Value.Value));
			if (deepest.Count > 40) lines.Add("prop in prop: " + (deepest.Count - 40) + " more pairs");
			pairNames.Clear();

			// quest targets under the ground or inside a solid
			bool was0 = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				Vector3[] six = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
				foreach (ReachTarget t in ReachTargets(e))
				{
					if (t.Zone || t.Root == null) continue;
					Vector3 c = t.B.center;
					string where = null;
					Func<Collider, bool> other = o => !o.transform.IsChildOf(t.Root) && !(o is TerrainCollider) && o.GetComponentInParent<Network_Player>() == null && o.GetComponentInParent<AI_NetworkBehaviour>() == null;
					if (ground != null && groundY(c) > c.y + 0.2f && !CaveUnder(c, groundY(c)))
						where = "under the ground by " + F1(groundY(c) - c.y) + " m";
					if (where == null)
						foreach (Collider o in Physics.OverlapSphere(c, 0.05f, reachMask, QueryTriggerInteraction.Ignore))
						{
							if (!other(o)) continue;
							bool convex = !(o is MeshCollider) || ((MeshCollider)o).convex;
							if (convex && (o.ClosestPoint(c) - c).sqrMagnitude < 1e-6f) { where = "inside " + BlockerName(o); break; }
						}
					if (where == null)
					{
						// (a closed mesh round it: the first thing each of six rays meets is the inside (a back face) of one and the same collider)
						Collider encl = null; int inside = 0;
						foreach (Vector3 d in six)
						{
							RaycastHit first = new RaycastHit(); bool any = false;
							foreach (RaycastHit h in Physics.RaycastAll(c, d, 40f, reachMask, QueryTriggerInteraction.Ignore))
								if (other(h.collider) && (!any || h.distance < first.distance)) { first = h; any = true; }
							if (!any || Vector3.Dot(first.normal, d) <= 0f) break;
							if (encl == null) encl = first.collider; else if (encl != first.collider) break;
							inside++;
						}
						if (inside == 6) where = "inside " + BlockerName(encl) + " (closed all round)";
					}
					if (where != null) lines.Add("target " + t.Kind + " '" + t.Name + "' at " + At(e, c) + (t.Steps.Length > 0 ? " (" + t.Steps + ")" : "") + ": " + where);
				}
			}
			finally { Physics.queriesHitBackfaces = was0; }
			return lines;
		}

		static readonly Dictionary<long, string> pairNames = new Dictionary<long, string>();

		static Bounds WorldBounds(List<OverlapBox> boxes)
		{
			bool first = true;
			Bounds all = new Bounds();
			foreach (OverlapBox ob in boxes)
				for (int k = 0; k < 8; k++)
				{
					Vector3 p = ob.T.TransformPoint(new Vector3((k & 1) == 0 ? ob.Min.x : ob.Max.x, (k & 2) == 0 ? ob.Min.y : ob.Max.y, (k & 4) == 0 ? ob.Min.z : ob.Max.z));
					if (first) { all = new Bounds(p, Vector3.zero); first = false; } else all.Encapsulate(p);
				}
			return all;
		}

		// ---------------------------------------------------------------- T5: what each quest step needs

		[ConsoleCommand(name: "CIQuestNeeds", docs: "Dev, anywhere (the live part: host, a test world 'CI ...'): sweep T5 - every quest step of saved islands has its target on the island (the note, the chest, the zone, the animals - placed at least the count, and one spare), every story item a step collects or a lock checks (has/take) can be got (chests and give actions, enough of them, not only from behind the same lock); in a world each island is also brought and its live animals counted against its kill and catch steps (need + 1). <which> as CIGroundIslands. Results in Mods\\DynamicIslands\\quest_needs.txt. CIQuestNeeds <which> [most] [skip] [nolive]")]
		public static void QuestNeedsCommand(string[] args)
		{
			string which; int most, skip;
			SweepArgs(args, out which, out most, out skip);
			bool live = !(args != null && args.Length > 3 && args[3] == "nolive");
			StartTest(QuestNeedsRoutine(which, most, skip, live));
		}

		static readonly Regex StoryRef = new Regex(@"story:([A-Za-z0-9_\-]+)(?:\*(\d+))?");
		static readonly Regex LockLine = new Regex(@"^\s*(has|take)\|story:([A-Za-z0-9_\-]+)\|?(\d*)");

		static IEnumerator QuestNeedsRoutine(string which, int most, int skip, bool live)
		{
			List<string> names = SweepNames(which, most, skip);
			if (names.Count == 0) { Fail("CIQuestNeeds: no saved island matches '" + which + "'"); yield break; }
			live = live && LoadSceneManager.IsGameSceneLoaded && Raft_Network.IsHost;
			if (live) yield return EnsureAlive();
			Log("quest needs of " + names.Count + " saved islands (" + which + ", from " + skip + ")" + (live ? ", live animals counted" : ", from the files only"));
			var report = new StringBuilder();
			int islands = 0, bad = 0, problems = 0, steps = 0;
			foreach (string name in names)
			{
				IslandFile f;
				try { f = IslandFile.Load(IslandSpawner.PathFor(name)); }
				catch (Exception ex) { report.AppendLine("NEEDS " + name + " ? (" + ex.Message + ")"); problems++; bad++; continue; }
				int n;
				Dictionary<string, int> need;
				List<string> lines = QuestNeedsOf(f, out n, out need);
				steps += n;
				if (n == 0) continue;
				islands++;
				if (live && need.Count > 0)
				{
					var got = new List<string>();
					yield return QuestNeedsLive(name, need, f, got);
					lines.AddRange(got);
				}
				int p = lines.Count(l => !l.StartsWith("ok "));
				report.AppendLine("NEEDS " + name + " " + n + " steps, " + p + " problems");
				foreach (string l in lines) report.AppendLine("  " + l);
				if (p > 0) { bad++; problems += p; Log("  " + name + ": " + p + " problem(s) in " + n + " steps"); foreach (string l in lines.Where(x => !x.StartsWith("ok ")).Take(3)) Log("    " + l); }
				yield return null;
			}
			if (live) OnRaftCommand();
			SweepWrite("quest_needs.txt", report);
			if (bad > 0) Fail("quest needs: " + problems + " problem(s) on " + bad + " of " + islands + " islands with quests (" + steps + " steps; quest_needs.txt)");
			else Log("PASS: quest needs - " + islands + " islands with quests, " + steps + " steps, every target there with a spare animal and every key item to be had");
		}

		/// <summary>One island's quest steps against its objects (from its file). need: the animals its kill/catch steps need, by kind.</summary>
		static List<string> QuestNeedsOf(IslandFile f, out int stepCount, out Dictionary<string, int> need)
		{
			var lines = new List<string>();
			need = new Dictionary<string, int>();
			stepCount = 0;
			Func<IslandObject, string> title = o => ObjectProps.Get(o.Props, ObjectProps.NoteTitle);
			// story items: where each can be had (chests, give actions) and every lock that checks one
			var have = new Dictionary<string, int>();
			var haveFrom = new Dictionary<string, List<IslandObject>>();
			var locks = new List<KeyValuePair<IslandObject, string[]>>(); // object, {kind, item, count, event}
			Action<IslandObject, string> addHave = (o, v) =>
			{
				foreach (Match m in StoryRef.Matches(v))
				{
					string id = m.Groups[1].Value;
					int c = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
					have[id] = (have.ContainsKey(id) ? have[id] : 0) + c;
					if (o != null) { if (!haveFrom.ContainsKey(id)) haveFrom[id] = new List<IslandObject>(); haveFrom[id].Add(o); }
				}
			};
			Action<IslandObject, IDictionary<string, string>> scan = (o, p) =>
			{
				if (p == null) return;
				foreach (KeyValuePair<string, string> kv in p)
				{
					string v = (kv.Value ?? "").Replace("\\n", "\n");
					if (kv.Key == ObjectProps.LootItems) addHave(o, v);
					else if (kv.Key.StartsWith(BehaviourProps.EventPrefix) || kv.Key.StartsWith(BehaviourProps.ElsePrefix))
						foreach (string l in v.Split('\n')) { if (l.TrimStart().StartsWith("give|")) addHave(o, l); }
					else if (kv.Key.StartsWith(BehaviourProps.CheckPrefix) && o != null)
						foreach (string l in v.Split('\n'))
						{
							Match m = LockLine.Match(l);
							if (m.Success) locks.Add(new KeyValuePair<IslandObject, string[]>(o, new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : "1", kv.Key.Substring(BehaviourProps.CheckPrefix.Length) }));
						}
				}
			};
			scan(null, f.Props);
			foreach (IslandObject o in f.Objects) scan(o, o.Props);
			// animals placed, by label (creature.count each, beh.hidden ones apart)
			var placed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var hidden = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (IslandObject o in f.Objects)
			{
				ContentCatalog.CreatureKind ck = ContentCatalog.CreatureOf(o.Name ?? "");
				if (ck == null) continue;
				int c = ObjectProps.Count(o.Props ?? new Dictionary<string, string>());
				placed[ck.Label] = (placed.ContainsKey(ck.Label) ? placed[ck.Label] : 0) + c;
				string hv = ObjectProps.Get(o.Props, BehaviourProps.Hidden).Trim().ToLowerInvariant();
				if (hv == "1" || hv == "true" || hv == "yes")
					hidden[ck.Label] = (hidden.ContainsKey(ck.Label) ? hidden[ck.Label] : 0) + c;
			}
			var takes = new Dictionary<string, int>();
			for (int q = 0; q < IslandQuest.CountIn(f.Props); q++)
			{
				IslandQuest quest = IslandQuest.From(f.Props, q, true);
				for (int s = 0; s < quest.Steps.Count; s++)
				{
					IslandQuest.Step st = quest.Steps[s];
					stepCount++;
					string head = "q" + (q + 1) + " s" + (s + 1) + " " + st.Type + " " + st.Count + " '" + st.Target + "'";
					string why = null;
					switch (st.Type)
					{
						case "read": if (!f.Objects.Any(o => title(o) == st.Target && !ContentCatalog.IsLootObject(o.Name ?? ""))) why = "no note or readable titled so"; break;
						case "open": if (!f.Objects.Any(o => title(o) == st.Target && ContentCatalog.IsLootObject(o.Name ?? ""))) why = "no chest titled so"; break;
						case "reach": if (!f.Objects.Any(o => ContentCatalog.IsZone(o.Name ?? "") && ObjectProps.Get(o.Props, ObjectProps.ZoneId) == st.Target)) why = "no zone with that id"; break;
						case "kill":
						case "catch":
						{
							need[st.Target] = (need.ContainsKey(st.Target) ? need[st.Target] : 0) + st.Count;
							break;
						}
						case "collect":
						{
							int h = have.ContainsKey(st.Target) ? have[st.Target] : 0;
							if (h < st.Count) why = "only " + h + " of story item '" + st.Target + "' to be had (chests, give actions)";
							break;
						}
						default: lines.Add("ok " + head + " (not checked here)"); continue;
					}
					lines.Add(why == null ? "ok " + head : "MISSING " + head + ": " + why);
				}
			}
			// the animals: placed against what the steps need, all of a kind added up (a kill counts for a later step too)
			foreach (KeyValuePair<string, int> kv in need)
			{
				int pl = placed.ContainsKey(kv.Key) ? placed[kv.Key] : 0, hd = hidden.ContainsKey(kv.Key) ? hidden[kv.Key] : 0;
				string what = kv.Key + ": steps need " + kv.Value + ", placed " + pl + (hd > 0 ? " (" + hd + " hidden at first)" : "");
				if (pl < kv.Value) lines.Add("SHORT " + what);
				else if (pl < kv.Value + 1) lines.Add("NOSPARE " + what + " - no spare if one despawns");
				else lines.Add("ok " + what);
			}
			// the locks: each item a lock checks can be had, and not only from behind a lock on the same item
			foreach (KeyValuePair<IslandObject, string[]> lk in locks)
			{
				string kind = lk.Value[0], id = lk.Value[1], ev = lk.Value[3];
				int c; if (!int.TryParse(lk.Value[2], out c)) c = 1;
				if (kind == "take") takes[id] = (takes.ContainsKey(id) ? takes[id] : 0) + c;
				int h = have.ContainsKey(id) ? have[id] : 0;
				string head = "lock " + (lk.Key.Name ?? "?") + " uid " + lk.Key.Uid + " (" + ev + ": " + kind + " " + c + " " + id + ")";
				List<IslandObject> from = haveFrom.ContainsKey(id) ? haveFrom[id] : new List<IslandObject>();
				bool onlyBehind = from.Count > 0 && from.All(o => locks.Any(x => x.Key == o && x.Value[1] == id));
				if (h < c) lines.Add("MISSING " + head + ": only " + h + " to be had");
				else if (onlyBehind) lines.Add("LOCKED " + head + ": the item is only behind a lock that wants it");
				else lines.Add("ok " + head);
			}
			foreach (KeyValuePair<string, int> kv in takes)
			{
				int h = have.ContainsKey(kv.Key) ? have[kv.Key] : 0;
				if (h < kv.Value) lines.Add("MISSING locks take " + kv.Value + " " + kv.Key + " in all, " + h + " to be had");
			}
			return lines;
		}

		/// <summary>The island brought next to the raft, its animals given time to come, then counted alive within its reach.</summary>
		static IEnumerator QuestNeedsLive(string name, Dictionary<string, int> need, IslandFile f, List<string> into)
		{
			var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
			if (left.Count > 0) yield return RemoveIslandsAndWait(left);
			Vector3? spot = ScSpot(name, 400f, IslandSpawner.ElevationOf(name));
			if (!spot.HasValue) { into.Add("LIVE not measured: no open sea near the raft"); yield break; }
			var made = new List<IslandWorldState.Entry>();
			yield return ScBring(name, spot.Value, made);
			IslandWorldState.Entry e = made.LastOrDefault();
			if (e == null || e.Root == null) { into.Add("LIVE not measured: the island didn't come"); yield break; }
			yield return WaitIslandSettled(e, 0.5f, 3f);
			float radius = LandRadiusOf(e);
			PlayerMove.To(RAPI.GetLocalPlayer(), e.Position + new Vector3(0f, 2f, -radius - 20f));
			// (the creature spots spawn their animals when the player is near: a few seconds)
			yield return new WaitForSeconds(8f);
			var typeOf = new Dictionary<string, AI_NetworkBehaviourType>(StringComparer.OrdinalIgnoreCase);
			foreach (IslandObject o in f.Objects)
			{
				ContentCatalog.CreatureKind ck = ContentCatalog.CreatureOf(o.Name ?? "");
				if (ck != null) typeOf[ck.Label] = ck.Type;
			}
			AI_NetworkBehaviour[] all = UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>();
			foreach (KeyValuePair<string, int> kv in need)
			{
				AI_NetworkBehaviourType type;
				if (!typeOf.TryGetValue(kv.Key, out type)) { into.Add("LIVE " + kv.Key + ": no spot of this kind on the island"); continue; }
				int alive = all.Count(a => a != null && a.behaviourType == type && a.networkEntity != null && !a.networkEntity.IsDead &&
					new Vector2(a.transform.position.x - e.Position.x, a.transform.position.z - e.Position.z).magnitude < radius + 60f);
				string what = kv.Key + ": " + alive + " alive near the island, the steps need " + kv.Value;
				if (alive < kv.Value) into.Add("LIVE SHORT " + what);
				else if (alive < kv.Value + 1) into.Add("LIVE NOSPARE " + what);
				else into.Add("ok live " + what);
			}
			yield return RemoveIslandsAndWait(made.Select(x => x.Id).ToList());
		}

		// ---------------------------------------------------------------- T8: density against Raft's islands

		[ConsoleCommand(name: "CIDensityIslands", docs: "Dev, anywhere: sweep T8 - CIIslandDensity's measure over saved islands, from their files (nothing spawned): things per 1000 m2 of land (trees, bushes, rocks, harvestables, beach things) and of sea floor 0-40 m deep within 60 m of the land, against Raft's islands of the style; DENSE / THIN off by more than 1.6 times; Raft's sea finds lying on dry land. One line per island. <which> as CIGroundIslands. Results in Mods\\DynamicIslands\\density_islands.txt; fails when any island is DENSE or has sea finds on land. CIDensityIslands <which> [most] [skip]")]
		public static void DensityIslandsCommand(string[] args)
		{
			string which; int most, skip;
			SweepArgs(args, out which, out most, out skip);
			StartTest(DensityIslandsRoutine(which, most, skip));
		}

		static IEnumerator DensityIslandsRoutine(string which, int most, int skip)
		{
			List<string> names = SweepNames(which, most, skip);
			if (names.Count == 0) { Fail("CIDensityIslands: no saved island matches '" + which + "'"); yield break; }
			var report = new StringBuilder();
			int dense = 0, thin = 0, dry = 0, done = 0, failed = 0;
			foreach (string name in names)
			{
				string line;
				try
				{
					IslandFile f = IslandFile.Load(IslandSpawner.PathFor(name));
					line = DensityLine(name, f);
					done++;
				}
				catch (Exception ex) { line = "DENSITY " + name + " | ? | " + ex.Message; failed++; }
				if (line.Contains(" DENSE")) dense++;
				if (line.Contains(" THIN")) thin++;
				if (line.Contains("finds on land")) dry++;
				report.AppendLine(line);
				Log(line);
				yield return null;
			}
			SweepWrite("density_islands.txt", report);
			string sum = done + " of " + names.Count + " islands measured, " + dense + " with DENSE, " + thin + " with THIN, " + dry + " with sea finds on land";
			if (dense > 0 || dry > 0 || failed > 0) Fail("density: " + sum + (failed > 0 ? ", " + failed + " unreadable" : "") + " (density_islands.txt)");
			else Log("PASS: density - " + sum);
		}

		/// <summary>One island's things per 1000 m2 against Raft's (CIIslandDensity's measure, from the island's file).</summary>
		static string DensityLine(string name, IslandFile f)
		{
			int res = f.HeightmapResolution;
			Vector3 size = f.TerrainSize;
			float cx = size.x / (res - 1), cz = size.z / (res - 1), cellA = cx * cz, reach = 60f;
			var h = new float[res, res];
			var dist = new int[res, res];
			var queue = new Queue<int>();
			float land = 0f, beach = 0f, floor = 0f;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					float v = h[z, x] = f.Heights[z, x] * size.y - f.WaterLevel;
					dist[z, x] = v > 0f ? 0 : int.MaxValue;
					if (v > 0f) { land += cellA; if (v < 1.5f) beach += cellA; queue.Enqueue(z * res + x); }
				}
			int steps = Mathf.CeilToInt(reach / Mathf.Min(cx, cz));
			while (queue.Count > 0)
			{
				int c = queue.Dequeue(), cj = c / res, ci = c % res;
				if (dist[cj, ci] >= steps) continue;
				for (int k = 0; k < 4; k++)
				{
					int a = ci + (k == 0 ? 1 : k == 1 ? -1 : 0), b = cj + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (a < 0 || b < 0 || a >= res || b >= res || dist[b, a] <= dist[cj, ci] + 1) continue;
					dist[b, a] = dist[cj, ci] + 1;
					queue.Enqueue(b * res + a);
				}
			}
			bool flying = f.Elevation > 0f;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					if (h[z, x] <= 0f && h[z, x] > -40f && dist[z, x] <= steps) floor += cellA;
			Func<Vector3, int[]> cellOf = p => new[] { Mathf.Clamp(Mathf.RoundToInt(p.z / cz), 0, res - 1), Mathf.Clamp(Mathf.RoundToInt(p.x / cx), 0, res - 1) };
			int style = string.IsNullOrEmpty(f.Style) ? TerrainPainter.Tropical : TerrainPainter.StyleIndex(f.Style);
			bool small = Mathf.Sqrt(land / Mathf.PI) < (RaftIslands.SmallRadius + RaftIslands.LargeRadius) / 2f;
			LandStyle raft = RaftLand.For(style, small);
			var landKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int st = 0; st < TerrainPainter.Styles.Length; st++) foreach (LandThing t in RaftLand.For(st).Things) if (!landKind.ContainsKey(t.Name)) landKind[t.Name] = t.Category;
			foreach (LandThing t in raft.Things) landKind[t.Name] = t.Category;
			string[] landCats = { IslandGenerator.CatTrees, IslandGenerator.CatBushes, IslandGenerator.CatRocks, IslandGenerator.CatHarvest, IslandGenerator.CatBeach };
			string[] seaCats = { IslandGenerator.CatWater, IslandGenerator.CatSeaRocks, IslandGenerator.CatSeaFinds, IslandGenerator.CatSunken };
			var onLand = landCats.ToDictionary(c => c, c => 0);
			var underWater = seaCats.ToDictionary(c => c, c => 0);
			var findsDry = new SortedDictionary<string, int>();
			foreach (IslandObject o in f.Objects)
			{
				string n = o.Name ?? "";
				int[] cell = cellOf(o.Position);
				float g = h[cell[0], cell[1]], above = o.Position.y - f.WaterLevel;
				string seaCat = RaftUnderwater.CategoryOf(n), landCat;
				if (above < -0.3f && g < 0f)
				{
					if (dist[cell[0], cell[1]] <= steps && seaCat != null) underWater[seaCat]++;
				}
				else if (landKind.TryGetValue(n, out landCat)) onLand[landCat]++;
				else if (seaCat == IslandGenerator.CatSeaFinds && g > 0.3f && Regex.IsMatch(n, @"Iron|Copper|Scrap|GiantClam|SilverAlgae"))
				{
					string k = FindKind(n);
					findsDry[k] = findsDry.ContainsKey(k) ? findsDry[k] + 1 : 1;
				}
			}
			Func<float, float, string> judge = (mine, theirs) => theirs <= 0.01f ? (mine > 0.5f ? " (Raft none)" : "") : mine > theirs * 1.6f ? " DENSE" : mine < theirs / 1.6f ? " THIN" : "";
			float raftArea = Mathf.Max(1f, raft.Area.Sum());
			var parts = new List<string>();
			foreach (string c in landCats)
			{
				float mine = land > 1f ? onLand[c] * 1000f / land : 0f, theirs = raft.CountOf(c) * 1000f / raftArea;
				parts.Add(c + " " + F1(mine) + "/" + F1(theirs) + judge(mine, theirs));
			}
			if (!flying && floor > 1f)
				foreach (string c in seaCats)
				{
					float mine = underWater[c] * 1000f / floor, theirs = RaftUnderwater.DensityOf(style, c);
					parts.Add("sea " + c + " " + F1(mine) + "/" + F1(theirs) + judge(mine, theirs));
				}
			if (findsDry.Count > 0) parts.Add("sea finds on land: " + string.Join(", ", findsDry.Select(kv => kv.Key + " " + kv.Value).ToArray()));
			return "DENSITY " + name + " | " + TerrainPainter.StyleName(style) + (small ? " small" : " big") + (flying ? " flying" : "") + " | land " + land.ToString("F0", Inv) + " m2, beach " + beach.ToString("F0", Inv) +
				(flying ? "" : ", sea floor " + floor.ToString("F0", Inv)) + " m2 | per 1000 m2 mine/Raft: " + string.Join(", ", parts.ToArray());
		}
	}
}
