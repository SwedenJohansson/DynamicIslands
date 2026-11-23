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
		public static void GroundWorldCommand(string[] args) { WorldCheck(args, GroundCheck); }

		[ConsoleCommand(name: "CIRenderers", docs: "Dev: an object's parts as spawned - each renderer (kind, on/off, shadow mode, its box from the pivot) and each collider - to see what 'sit' and the ground checks measure. CIRenderers <object> [scale]")]
		public static void RenderersCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Fail("CIRenderers <object> [scale]"); return; }
			StartTest(RenderersRoutine(args[0], args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 1f));
		}

		static IEnumerator RenderersRoutine(string name, float scale)
		{
			yield return PlaceableCatalog.EnsureLoaded(new[] { name });
			GameObject g = PlaceableCatalog.Spawn(name, null);
			if (g == null) { Fail("CIRenderers: no object " + name); yield break; }
			g.transform.position = new Vector3(0f, 500f, 0f);
			g.transform.localScale = Vector3.one * scale;
			yield return null;
			Physics.SyncTransforms();
			Vector3 p = g.transform.position;
			foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
				Log("  renderer " + r.GetType().Name + " '" + r.name + "' " + (r.enabled && r.gameObject.activeInHierarchy ? "on" : "off") + " shadows=" + r.shadowCastingMode +
					" min " + Num(r.bounds.min.y - p.y) + " max " + Num(r.bounds.max.y - p.y) + " size " + Num(r.bounds.size.x) + "x" + Num(r.bounds.size.y) + "x" + Num(r.bounds.size.z));
			foreach (Collider c in g.GetComponentsInChildren<Collider>(true))
				Log("  collider " + c.GetType().Name + " '" + c.name + "' " + (c.enabled && c.gameObject.activeInHierarchy ? "on" : "off") + (c.isTrigger ? " trigger" : "") +
					" min " + Num(c.bounds.min.y - p.y) + " max " + Num(c.bounds.max.y - p.y));
			UnityEngine.Object.Destroy(g);
			Log("PASS: renderers of " + name + " listed");
		}

		/// <summary>A check of the objects on spawned islands (CIGround* / CIOverlap*): its name in the results, its files'
		/// prefix, what a pass says, the measure.</summary>
		class IslandCheck { public string Label, File, Pass; public IslandMeasure Measure; }
		delegate List<string> IslandMeasure(IslandWorldState.Entry e, out int count);
		static readonly IslandCheck GroundCheck = new IslandCheck { Label = "ground", File = "ground", Pass = "all on something", Measure = GroundProblems };

		static void WorldCheck(string[] args, IslandCheck c)
		{
			string only = args != null && args.Length > 0 && args[0] != "*" ? args[0] : "";
			var report = new StringBuilder();
			int islands = 0, bad = 0, problems = 0, objects = 0;
			checkShots = false; // (no pictures here: this check runs in one frame)
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading).ToList())
			{
				if (only.Length > 0 && (e.HostName ?? e.Name ?? "").IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				islands++;
				int n;
				List<string> lines = c.Measure(e, out n);
				objects += n;
				report.AppendLine(e.Name + ": " + n + " objects, " + lines.Count + " problems");
				foreach (string l in lines) report.AppendLine("  " + l);
				if (lines.Count == 0) continue;
				bad++;
				problems += lines.Count;
				Log("  '" + e.Name + "': " + lines.Count + " problem(s) of " + n + " objects");
				foreach (string l in lines.Take(5)) Log("    " + l);
			}
			File.WriteAllText(Path.Combine(DynamicIslands.assetpath, c.File + "_world.txt"), report.ToString());
			if (islands == 0) Fail(c.Label + ": no loaded custom island" + (only.Length > 0 ? " named *" + only + "*" : ""));
			else if (bad > 0) Fail(c.Label + ": " + bad + " of " + islands + " islands with " + problems + " problems (" + c.File + "_world.txt)");
			else Log("PASS: " + c.Label + " - " + islands + " islands, " + objects + " objects, " + c.Pass);
		}

		[ConsoleCommand(name: "CIGroundIslands", docs: "Dev, world (host, a test world 'CI ...'): saved islands brought next to the raft one by one and measured as CIGroundWorld does, then taken away (IG2/IG3). <which>: part of the name (several joined by | - Geiger|Gannet_Rock, _ for a space), 'plans' (every island of every plan - the story lines), 'gen' (the saved generated ones) or * (all but the test islands, kept copies and randomizer extras). Results in Mods\\DynamicIslands\\ground_islands.txt. CIGroundIslands <which> [most] [skip the first n] [keep: left there, where each is logged - to look at it] [shots: a picture of each cave rim gap (from outside and from the rim) and each see-through look, shot_cave_*.png, with what the look ray meets] [as=<tag>: the results copied to ground_islands_<tag>.txt too, so two runs in one job keep both]")]
		public static void GroundIslandsCommand(string[] args) { IslandsCheck(args, GroundCheck); }

		static void IslandsCheck(string[] args, IslandCheck c)
		{
			// (the word 'shots' anywhere: the pictures the check asks for - its worst sheets, cave looks - taken while each island is there)
			checkShots = args != null && args.Contains("shots");
			if (checkShots) args = args.Where(a => a != "shots").ToArray();
			// (rocks=<piece>: with shots, CISheetIslands also pictures up to 4 of that piece standing in the waterline on each
			// island - the cliffs step's rocks=, to see them standing, not floating or stretched oddly; c3z63 11-22)
			checkRockShots = args == null ? null : args.Where(a => a.StartsWith("rocks=", StringComparison.Ordinal)).Select(a => a.Substring(6)).LastOrDefault();
			if (checkRockShots != null) args = args.Where(a => !a.StartsWith("rocks=", StringComparison.Ordinal)).ToArray();
			// (as=<tag>: the report also copied to <file>_islands_<tag>.txt - two phases of one job wrote the same file and the
			// first one's lines were lost, c3z46 11-21)
			string tag = args == null ? null : args.Where(a => a.StartsWith("as=", StringComparison.Ordinal)).Select(a => a.Substring(3)).LastOrDefault();
			if (tag != null) { args = args.Where(a => !a.StartsWith("as=", StringComparison.Ordinal)).ToArray(); tag = new string(tag.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray()); if (tag.Length == 0) tag = null; }
			string which = args != null && args.Length > 0 ? args[0] : "*";
			int most = args != null && args.Length > 1 ? int.Parse(args[1]) : 9999;
			int skip = args != null && args.Length > 2 ? int.Parse(args[2]) : 0;
			bool keep = args != null && args.Length > 3 && args[3] == "keep";
			StartTest(GroundIslandsRoutine(c, which, most, skip, keep, tag));
		}

		static IEnumerator GroundIslandsRoutine(IslandCheck c, string which, int most, int skip, bool keep = false, string tag = null)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail(c.Label + " islands: host, in a world"); yield break; }
			var kept = new System.Text.RegularExpressions.Regex("_[0-9a-f]{12}$");
			var saved = new HashSet<string>(IslandSpawner.ListSavedIslands(), StringComparer.OrdinalIgnoreCase);
			IEnumerable<string> pick;
			// (several names in one run, joined by | - SPEED_RULES 6, gap check 2026-10-20; _ stands for a space, as a
			// command's words are split at spaces)
			string[] parts = which.Split('|').Select(p => p.Replace('_', ' ').Trim()).Where(p => p.Length > 0).ToArray();
			if (which == "plans") pick = CustomIslandSpawner.AllPlansIslandNames().Where(n => saved.Contains(n));
			else pick = saved.Where(n => !n.StartsWith("ci", StringComparison.Ordinal) && !kept.IsMatch(n) && !n.StartsWith(WorldRandomizer.ExtrasPrefix, StringComparison.OrdinalIgnoreCase) &&
				(which == "*" || parts.Any(p => n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)));
			List<string> names = pick.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Skip(skip).Take(most).ToList();
			Log(c.Label + " check of " + names.Count + " saved islands (" + which + ", from " + skip + ")");
			var report = new StringBuilder();
			int done = 0, bad = 0, problems = 0, missed = 0, at = 0;
			foreach (string name in names)
			{
				// (IG8 11-20: progress for the heartbeat - a sweep of 200 islands runs half an hour, and ci.ps1 ends a wait
				//  whose HB step/prog stands still for 20 min)
				hbWhat = c.Label + " islands"; hbStep = at++; hbProg++;
				// (the world left under the sweep - the runner moved on: every later island said "no open sea", 77 of them)
				if (!LoadSceneManager.IsGameSceneLoaded || !CustomIslandSpawner.RaftPosition.HasValue)
				{
					Fail(c.Label + " islands: the world was left after " + (at - 1) + " of " + names.Count + " islands (" + done + " measured) - next was '" + name + "'");
					yield break;
				}
				var left =IslandWorldState.Islands.Where(x => string.Equals(x.HostName, name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
				if (left.Count > 0) yield return RemoveIslandsAndWait(left);
				Vector3? spot = ScSpot(name, 400f);
				if (!spot.HasValue) { Log("  " + name + ": no open sea near the raft"); report.AppendLine(name + ": NOT MEASURED - no open sea near the raft"); missed++; continue; }
				var made = new List<IslandWorldState.Entry>();
				yield return ScBring(name, spot.Value, made);
				IslandWorldState.Entry e = made.LastOrDefault();
				if (e == null || e.Root == null) { Log("  " + name + ": didn't come"); report.AppendLine(name + ": NOT MEASURED - didn't come" + (e != null && e.Failed ? " (its spawn failed)" : "")); missed++; if (made.Count > 0) yield return RemoveIslandsAndWait(made.Select(x => x.Id).ToList()); continue; }
				yield return WaitIslandSettled(e, 0.5f, 2f); // (the cave mountains and Raft's physics settle: until they have, 2 s at most)
				// (a night ghost ship brought by day sinks away at once - IG5 run 11-18 threw on it, ending the check)
				if (e.Root == null) { Log("  " + name + ": went away again (a ghost ship by day)"); report.AppendLine(name + ": NOT MEASURED - went away again"); missed++; continue; }
				int n;
				checkShotQueue.Clear();
				List<string> lines = c.Measure(e, out n);
				done++;
				report.AppendLine(name + ": " + n + " objects, " + lines.Count + " problems");
				// (the pictures the measure asked for, before the island goes)
				foreach (var s in checkShotQueue.ToList())
				{
					report.AppendLine("  (picture shot_" + s.Key + ".png from " + s.Value[0].ToString("F1") + " towards " + s.Value[1].ToString("F1") + ")");
					yield return CameraShot(s.Value[0], s.Value[1], s.Key);
				}
				checkShotQueue.Clear();
				foreach (string l in lines) report.AppendLine("  " + l);
				if (lines.Count > 0)
				{
					bad++;
					problems += lines.Count;
					Log("  " + name + ": " + lines.Count + " problem(s) of " + n + " objects");
					foreach (string l in lines.Take(3)) Log("    " + l);
				}
				if (keep) { Log("  kept '" + name + "' at " + e.Root.transform.position.ToString("F1") + ", turned " + e.Root.transform.eulerAngles.y.ToString("F0") + "°"); continue; }
				yield return RemoveIslandsAndWait(made.Select(x => x.Id).ToList());
			}
			hbWhat = "idle";
			File.WriteAllText(Path.Combine(DynamicIslands.assetpath, c.File + "_islands.txt"), report.ToString());
			string reportName = c.File + "_islands.txt";
			if (tag != null) { reportName = c.File + "_islands_" + tag + ".txt"; File.WriteAllText(Path.Combine(DynamicIslands.assetpath, reportName), report.ToString()); }
			string sum = done + " of " + names.Count + " islands measured" + (missed > 0 ? " (" + missed + " didn't come)" : "");
			if (names.Count == 0) Fail(c.Label + " islands: none matched '" + which + "'");
			else if (bad > 0 || missed > 0) Fail(c.Label + " islands: " + bad + " with " + problems + " problems; " + sum + " (" + reportName + ")");
			else Log("PASS: " + c.Label + " islands - " + sum + ", " + c.Pass);
		}

		[ConsoleCommand(name: "CIGroundTypes", docs: "Dev, world (host, a test world 'CI ...'): brand-new islands of each map type made next to the raft by the generator as it is now, measured as CIGroundWorld does, then taken away (IG6) - a saved generated island keeps what an older generator made. Results in Mods\\DynamicIslands\\ground_types.txt. CIGroundTypes [types, comma-separated, or *] [islands of each, 2]")]
		public static void GroundTypesCommand(string[] args) { TypesCheck(args, GroundCheck); }

		static void TypesCheck(string[] args, IslandCheck c)
		{
			checkShots = false;
			string which = args != null && args.Length > 0 ? args[0] : "*";
			int each = args != null && args.Length > 1 ? int.Parse(args[1]) : 2;
			StartTest(GroundTypesRoutine(c, which, each));
		}

		static IEnumerator GroundTypesRoutine(IslandCheck c, string which, int each)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail(c.Label + " types: host, in a world"); yield break; }
			List<string> types = (which == "*" ? MapTypes.All.Select(t => t.Name).Where(t => t != "random") : which.Split(',').Select(t => t.Trim()).Where(t => MapTypes.Get(t) != null)).ToList();
			if (types.Count == 0) { Fail(c.Label + " types: no map type '" + which + "'"); yield break; }
			Log(c.Label + " check of new islands: " + string.Join(", ", types.ToArray()) + ", " + each + " of each");
			var report = new StringBuilder();
			int done = 0, bad = 0, problems = 0, missed = 0;
			foreach (string type in types)
				for (int i = 0; i < each; i++)
				{
					hbWhat = c.Label + " types"; hbStep = done + missed; hbProg++; // (progress for the heartbeat, as in GroundIslandsRoutine)
					Network_Player player = RAPI.GetLocalPlayer();
					Vector3 raft = CustomIslandSpawner.RaftPosition ?? (player != null ? player.transform.position : Vector3.zero);
					IslandWorldState.Entry e = SpawnTypeNear(type, raft);
					for (float t = 0; e != null && t < 90f && e.Root == null && !e.Failed; t += 0.1f) yield return new WaitForSeconds(0.1f);
					if (e == null || e.Root == null)
					{
						Log("  " + type + ": " + (e == null ? "no open sea near the raft" : "didn't come"));
						missed++;
						if (e != null) IslandWorldState.RemoveIds(new[] { e.Id }, true);
						continue;
					}
					yield return WaitIslandSettled(e, 0.5f, 2f); // (the cave mountains and Raft's physics settle: until they have, 2 s at most)
					int n;
					List<string> lines = c.Measure(e, out n);
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
					yield return RemoveIslandsAndWait(new[] { e.Id });
				}
			hbWhat = "idle";
			File.WriteAllText(Path.Combine(DynamicIslands.assetpath, c.File + "_types.txt"), report.ToString());
			string sum = done + " of " + types.Count * each + " new islands measured" + (missed > 0 ? " (" + missed + " didn't come)" : "");
			if (bad > 0 || missed > 0) Fail(c.Label + " types: " + bad + " with " + problems + " problems; " + sum + " (" + c.File + "_types.txt)");
			else Log("PASS: " + c.Label + " types - " + sum + ", " + c.Pass);
		}

		[ConsoleCommand(name: "CIScrapyardExcavator", docs: "Dev, world (host, a test world 'CI ...'): large islands (the only map type with themed scenes) made by the generator as it is now, one after another, until one has the scrapyard scene with the excavator as its centrepiece (a large island has a scrapyard in about one of four and its centrepiece is one of excavator / forklift / dumpster, so a dozen fresh islands may have none - GAP22). That island is brought next to the raft, measured as CIGroundTypes does (the excavator's own lines decide) and for how deep its tracks sit (fails when even the shallowest of 5 spots is over 0.35 m in - GAP25), the excavator pictured (scrapyard_excavator) and taken away. CIScrapyardExcavator [islands to try, 80] [keep]")]
		public static void ScrapyardExcavatorCommand(string[] args)
		{
			int tries = 80, t;
			if (args != null && args.Length > 0 && int.TryParse(args[0], out t)) tries = t;
			StartTest(ScrapyardExcavatorRoutine(tries, args != null && args.Contains("keep")));
		}

		static IEnumerator ScrapyardExcavatorRoutine(int tries, bool keep)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("scrapyard excavator: host, in a world"); yield break; }
			Theme yard = RandomizerIslands.ThemeOf("scrapyard");
			if (yard == null || !yard.Anchors.Contains("VP_Excavator")) { Fail("scrapyard excavator: the scrapyard scene has no excavator centrepiece any more"); yield break; }
			if (!RaftProps.Standable("VP_Excavator")) { Fail("scrapyard excavator: VP_Excavator isn't standable, so the generator never picks it"); yield break; }
			yield return PlaceableCatalog.EnsureBuilt();
			// (only a large island gets themed scenes - RandomizerIslands.Large -> Dress - and no map type sets IslandGenSettings.Buildings,
			// so the other types never have the scrapyard: with the tries spread over every type, most were wasted and a run found none, 2026-10-21)
			List<MapType> types = MapTypes.All.Where(x => x.Name == "large" || x.ContentFrom == "large").ToList();
			if (types.Count == 0) { Fail("scrapyard excavator: no map type makes large islands, the only ones with themed scenes"); yield break; }
			var rnd = new System.Random();
			var seen = new Dictionary<string, int>();
			IslandFile found = null;
			MapType foundType = null;
			IslandGenSettings foundS = null;
			float foundEl = 0f, start = Time.realtimeSinceStartup;
			int made = 0, failed = 0;
			Log("scrapyard excavator: making up to " + tries + " islands until one has the scrapyard's excavator");
			for (int i = 0; i < tries && found == null && Time.realtimeSinceStartup - start < 300f; i++)
			{
				MapType type = types[i % types.Count];
				float el;
				IslandGenSettings s = MapTypes.Roll(type, rnd, out el);
				IslandFile f = null;
				try { f = MapTypes.Create(type, s, el, MapTypes.FileName(type, s)); made++; }
				catch (Exception ex) { failed++; Log("  " + type.Name + ": generating failed - " + ex.Message); }
				if (f != null)
				{
					// (the scene's centrepiece is a set piece; the Varuna look's excavator stands without one)
					foreach (IslandObject o in f.Objects.Where(o => o.Props != null && o.Props.ContainsKey("set.piece") && yard.Anchors.Contains(o.Name)))
					{
						int c;
						seen[o.Name] = (seen.TryGetValue(o.Name, out c) ? c : 0) + 1;
						if (o.Name == "VP_Excavator" && found == null) { found = f; foundType = type; foundS = s; foundEl = el; }
					}
				}
				yield return null;
			}
			string stats = made + " island(s) made" + (failed > 0 ? ", " + failed + " failed" : "") + "; scrapyard centrepieces: " +
				(seen.Count == 0 ? "none" : string.Join(", ", seen.Select(x => x.Key + " " + x.Value).ToArray()));
			Log("  " + stats);
			if (found == null) { Fail("scrapyard excavator: none in " + stats); yield break; }
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? (player != null ? player.transform.position : Vector3.zero);
			string name = MapTypes.FileName(foundType, foundS);
			float radius = MapTypes.EstimatedRadius(foundS);
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft, radius, 780f);
			if (!spot.HasValue) { Fail("scrapyard excavator: no open sea near the raft for " + name); yield break; }
			CustomIslandSpawner.CacheSize(name, radius, foundEl);
			IslandWorldState.Entry e = IslandWorldState.Add(name, spot.Value, null, false);
			e.Loading = true;
			IslandFile ready = found;
			StartTest(CustomIslandSpawner.GenerateAndSpawn(() => ready, name, e));
			for (float w = 0; w < 90f && e.Root == null && !e.Failed; w += 0.1f) yield return new WaitForSeconds(0.1f);
			if (e.Root == null) { Fail("scrapyard excavator: " + name + " didn't come"); IslandWorldState.RemoveIds(new[] { e.Id }, true); yield break; }
			yield return WaitIslandSettled(e, 0.5f, 2f);
			int n;
			List<string> lines = GroundProblems(e, out n);
			List<string> mine = lines.Where(l => l.Contains("VP_Excavator")).ToList();
			Transform exc = e.Root.GetComponentsInChildren<IslandObjectRef>(true).Where(r => r.ObjectName == "VP_Excavator" && r.Props != null && r.Props.ContainsKey("set.piece")).Select(r => r.transform).FirstOrDefault();
			if (exc != null && player != null) { SetHour(11f); yield return ShotOf(player, exc, "scrapyard_excavator", 12f); }
			foreach (string l in lines.Take(5)) Log("    " + l);
			// How deep its tracks sit: they start 0.8 m over the pivot (RaftProps.SeenBottoms); measured under the middle and 1.5 m
			// out to each side, so a slope counts. Sunk = even the shallowest spot over 0.35 m in (Raft's own Varuna Point: 0-0.1 m;
			// the full run of 2026-10-21 saw gen-large-233873's pivot 1.1 m under the sand, GAP25)
			string sinkNote = "";
			bool sunk = false;
			Terrain land = exc != null ? e.Root.GetComponentInChildren<Terrain>() : null;
			if (land != null)
			{
				float tracks = exc.position.y + 0.8f * exc.lossyScale.y;
				List<float> depths = new[] { Vector3.zero, exc.right, -exc.right, exc.forward, -exc.forward }
					.Select(d => land.SampleHeight(exc.position + d * 1.5f) + land.transform.position.y - tracks).ToList();
				sinkNote = "; tracks " + string.Join("/", depths.Select(x => Num(x)).ToArray()) + " m in the ground (middle, sides)";
				sunk = depths.Min() > 0.35f;
				Log("  excavator" + sinkNote.Substring(1));
				// (where its parts start, from the pivot: is SeenBottoms' 0.8 right?)
				float groundMid = land.SampleHeight(exc.position) + land.transform.position.y;
				Log("  excavator pivot " + Num(groundMid - exc.position.y) + " m under the ground, scale " + Num(exc.lossyScale.y) + "; parts' bottoms over the pivot: " +
					string.Join(", ", exc.GetComponentsInChildren<Renderer>(true).Select(r => r.name + (r.enabled && r.gameObject.activeInHierarchy ? "" : " (hidden)") + " " +
						Num(r.bounds.min.y - exc.position.y)).Take(12).ToArray()));
			}
			// (read before the removal: a destroyed Transform compares equal to null, 2026-10-21)
			bool hasExc = exc != null;
			if (!keep) yield return RemoveIslandsAndWait(new[] { e.Id });
			if (!hasExc) Fail("scrapyard excavator: " + name + " came without its excavator (" + n + " objects)");
			else if (mine.Count > 0) Fail("scrapyard excavator (" + name + "): " + string.Join("; ", mine.ToArray()));
			else if (sunk) Fail("scrapyard excavator (" + name + "): sunk into the ground" + sinkNote);
			else Log("PASS: scrapyard excavator - on " + name + " (" + foundType.Name + "), on the ground" + sinkNote +
				(lines.Count > 0 ? "; " + lines.Count + " other problem(s) on the island, not the excavator's (CIGroundTypes covers them)" : "") + "; " + stats);
		}

		[ConsoleCommand(name: "CIShackDish", docs: "Dev, world (host, a test world 'CI ...'): a hermit's shack island made by the generator as it is now, with the bear scene's satellite dish forced in as the first of the props sprinkled round the shack (by chance only about one shack in four gets one - 78f903d), brought next to the raft and measured: the dish's pivot on its antenna pole's axis (within 0.05 m) and 6.0 m (within 0.1) over the pole's base, the pole's base on the ground (0.2 m over to 0.6 m under it at most) and no ground-check line about the dish or the pole. Taken away after. CIShackDish [shacks to try for one with the dish placed, 6] [keep]")]
		public static void ShackDishCommand(string[] args)
		{
			int tries = 6, t;
			if (args != null && args.Length > 0 && int.TryParse(args[0], out t)) tries = t;
			StartTest(ShackDishRoutine(tries, args != null && args.Contains("keep")));
		}

		static IEnumerator ShackDishRoutine(int tries, bool keep)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("shack dish: host, in a world"); yield break; }
			MapType shack = MapTypes.BuiltIn("shack");
			string themeName;
			Theme bear = RandomizerIslands.OddityTheme.TryGetValue("shack", out themeName) ? RandomizerIslands.ThemeOf(themeName) : null;
			if (shack == null || bear == null || !bear.Medium.Concat(bear.Small).Contains("Antenna_dish")) { Fail("shack dish: the shack island, or the dish among its theme's props, is gone"); yield break; }
			if (!RaftProps.Standable("Antenna_dish")) { Fail("shack dish: Antenna_dish isn't standable, so the generator never places it"); yield break; }
			yield return PlaceableCatalog.EnsureBuilt();
			// (the shack type's own content, with the dish forced as the first prop of the touch of its theme - Oddity -> Sprinkle -> DishOnPole)
			var forced = new MapType { Name = shack.Name, Label = shack.Label, Title = shack.Title,
				Content = (mk, ms) => { RandomizerContent.Oddity(mk, ms, "shack", "Antenna_dish"); RandomizerIslands.TreesToCut(mk, ms, true); } };
			var rnd = new System.Random();
			IslandFile found = null;
			IslandGenSettings foundS = null;
			float foundEl = 0f;
			int made = 0;
			for (int i = 0; i < tries && found == null; i++)
			{
				float el;
				IslandGenSettings gs = MapTypes.Roll(shack, rnd, out el);
				IslandFile f = null;
				try { f = MapTypes.Create(forced, gs, el, MapTypes.FileName(shack, gs)); made++; }
				catch (Exception ex) { Log("  shack: generating failed - " + ex.Message); }
				// (the dish is a set piece; the sprinkle can find no level spot in its ring round the shack - then the next shack)
				if (f != null && f.Objects.Any(o => o.Name == "Antenna_dish" && o.Props != null && o.Props.ContainsKey("set.piece"))) { found = f; foundS = gs; foundEl = el; }
				yield return null;
			}
			if (found == null) { Fail("shack dish: none of " + made + " shack island(s) got the forced dish placed"); yield break; }
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? (player != null ? player.transform.position : Vector3.zero);
			string name = MapTypes.FileName(shack, foundS);
			float radius = MapTypes.EstimatedRadius(foundS);
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft, radius, 780f);
			if (!spot.HasValue) { Fail("shack dish: no open sea near the raft for " + name); yield break; }
			CustomIslandSpawner.CacheSize(name, radius, foundEl);
			IslandWorldState.Entry e = IslandWorldState.Add(name, spot.Value, null, false);
			e.Loading = true;
			IslandFile ready = found;
			StartTest(CustomIslandSpawner.GenerateAndSpawn(() => ready, name, e));
			for (float w = 0; w < 90f && e.Root == null && !e.Failed; w += 0.1f) yield return new WaitForSeconds(0.1f);
			if (e.Root == null) { Fail("shack dish: " + name + " didn't come"); IslandWorldState.RemoveIds(new[] { e.Id }, true); yield break; }
			yield return WaitIslandSettled(e, 0.5f, 2f);
			Physics.SyncTransforms();
			Terrain land = e.Root.GetComponentInChildren<Terrain>();
			IslandObjectRef[] refs = e.Root.GetComponentsInChildren<IslandObjectRef>(true);
			List<IslandObjectRef> dishes = refs.Where(x => x.ObjectName == "Antenna_dish" && x.Props != null && x.Props.ContainsKey("set.piece")).ToList();
			Func<Vector3, Vector3, float> flat = (a, b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
			var bad = new List<string>();
			var good = new List<string>();
			int count;
			List<string> lines = GroundProblems(e, out count);
			foreach (string l in lines.Where(q => q.Contains("Antenna_")).Take(5)) bad.Add("ground check: " + l);
			if (dishes.Count == 0) bad.Add("the dish didn't come with the island");
			foreach (IslandObjectRef d in dishes)
			{
				Vector3 dp = d.transform.position;
				IslandObjectRef pole = refs.Where(x => x.ObjectName == "Antenna_pole").OrderBy(x => flat(x.transform.position, dp)).FirstOrDefault();
				if (pole == null || flat(pole.transform.position, dp) > 0.5f)
				{ bad.Add("no antenna pole under the dish" + (pole == null ? "" : " (the nearest " + Num(Math.Round(flat(pole.transform.position, dp), 2)) + " m off)")); continue; }
				// (the pole's base: its lowest mesh; its pivot when it has none)
				Renderer[] parts = pole.GetComponentsInChildren<Renderer>(false).Where(x => x is MeshRenderer && x.enabled && x.name != ContentCatalog.MarkerOnly).ToArray();
				float baseY = parts.Length > 0 ? parts.Min(x => x.bounds.min.y) : pole.transform.position.y;
				float off = flat(pole.transform.position, dp), up = dp.y - baseY;
				float gap = land != null ? baseY - (land.SampleHeight(pole.transform.position) + land.transform.position.y) : float.NaN;
				string msg = "dish " + Num(Math.Round(off, 2)) + " m off the pole's axis, " + Num(Math.Round(up, 2)) + " m over its base, base " + (float.IsNaN(gap) ? "no ground" : Num(Math.Round(Mathf.Abs(gap), 2)) + (gap > 0f ? " m over" : " m under") + " the ground");
				if (off > 0.05f || Mathf.Abs(up - 6f) > 0.1f || float.IsNaN(gap) || gap > 0.2f || gap < -0.6f) bad.Add(msg); else good.Add(msg);
			}
			if (!keep) yield return RemoveIslandsAndWait(new[] { e.Id });
			if (bad.Count > 0) Fail("shack dish (" + name + "): " + string.Join("; ", bad.ToArray()));
			else Log("PASS: shack dish - on " + name + ": " + string.Join("; ", good.ToArray()) + "; " + count + " objects measured, " + lines.Count + " other ground problem(s), not the dish's");
		}

		static readonly IslandCheck OverlapCheck = new IslandCheck { Label = "overlap", File = "overlap", Pass = "nothing through anything", Measure = OverlapProblems };

		static readonly IslandCheck OneSidedCheck = new IslandCheck { Label = "one-sided", File = "onesided", Pass = "no wall seen through from behind", Measure = OneSidedProblems };

		[ConsoleCommand(name: "CIOneSidedWorld", docs: "Dev, in a world: no wall-like piece on the loaded custom islands is drawn from one side only with nothing behind its open side (a facade panel seen from inside its room: see-through, IG8). Results in Mods\\DynamicIslands\\onesided_world.txt.")]
		public static void OneSidedWorldCommand(string[] args) { WorldCheck(args, OneSidedCheck); }

		[ConsoleCommand(name: "CIOneSidedIslands", docs: "Dev, world (host, a test world 'CI ...'): saved islands brought next to the raft one by one and measured as CIOneSidedWorld does, then taken away (IG8). Same arguments as CIOverlapIslands. Results in Mods\\DynamicIslands\\onesided_islands.txt.")]
		public static void OneSidedIslandsCommand(string[] args) { IslandsCheck(args, OneSidedCheck); }

		static readonly IslandCheck SheetCheck = new IslandCheck { Label = "sheet", File = "sheet", Pass = "no bare cliff sheet at the waterline", Measure = SheetProblems };

		[ConsoleCommand(name: "CISheetIslands", docs: "Dev, world (host, a test world 'CI ...'): saved islands brought next to the raft one by one; no bare land cliff at the waterline - the land's own face rising 7 m or more in one heightmap step from under the sea to over it, nothing standing in front of it, along 3 or more 8 m cells: Raft draws it as one pale stretched sheet (Caisson Yard's yard rock, c3z37 11-21). Each line says whether the face is a pit's wall (seen from its rim) or the open coast (seen from the sea) and how it is painted. Fix: the recipe step cliffs. Same arguments as CIOverlapIslands, plus 'shots' (a picture of every sheet per island, largest first, from the low side, shot_sheet_<island>_<n>.png). Results in Mods\\DynamicIslands\\sheet_islands.txt.")]
		public static void SheetIslandsCommand(string[] args) { IslandsCheck(args, SheetCheck); }

		[ConsoleCommand(name: "CISheetWorld", docs: "Dev, in a world: CISheetIslands on the loaded custom islands. CISheetWorld [part of the island name]. Results in Mods\\DynamicIslands\\sheet_world.txt.")]
		public static void SheetWorldCommand(string[] args) { WorldCheck(args, SheetCheck); }

		[ConsoleCommand(name: "CISheetShotsUnit", docs: "Dev, anywhere: CISheetIslands' 'shots' picture every sheet, not just the 2 largest (lib 3.5.1 11-23: Take(2) left smaller new sheets unpictured) and past the 8-picture cap - a made-up terrain far off with 9 square plateaus 10 m over the sea in 20 m deep water: 9 sheets, 9 sheet pictures queued, largest first (_1 .. _9)")]
		public static void CISheetShotsUnit()
		{
			var td = new TerrainData();
			td.heightmapResolution = 129;
			td.size = new Vector3(256f, 60f, 256f);
			int res = td.heightmapResolution;
			var h = new float[res, res];
			float low = 10f / 60f, high = 40f / 60f; // (terrain at y -30: the sea floor 20 m under the sea, the plateaus 10 m over it)
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) h[z, x] = low;
			int[] starts = { 14, 54, 94 };
			int k = 0;
			foreach (int bz in starts)
				foreach (int bx in starts)
				{
					int side = 8 + 2 * k++; // (sizes 8..24 points: each sheet a different size, so 'largest first' is checkable)
					for (int z = bz; z < bz + side && z < res; z++) for (int x = bx; x < bx + side && x < res; x++) h[z, x] = high;
				}
			td.SetHeights(0, 0, h);
			GameObject go = Terrain.CreateTerrainGameObject(td);
			go.transform.position = new Vector3(6000f, -30f, 6000f);
			bool was = checkShots;
			string wasRocks = checkRockShots;
			try
			{
				Physics.SyncTransforms();
				checkShots = true; checkRockShots = null; checkShotQueue.Clear();
				var e = new IslandWorldState.Entry { Name = "ci sheet unit", Root = go, Position = go.transform.position + new Vector3(128f, 30f, 128f) };
				int n;
				List<string> lines = SheetProblems(e, out n);
				var names = checkShotQueue.Select(s => s.Key).ToList();
				var want = Enumerable.Range(1, 9).Select(i => "sheet_ci_sheet_unit_" + i).ToList();
				var cells = lines.Select(l => int.Parse(Regex.Match(l, @", (\d+) cells of").Groups[1].Value)).ToList();
				bool order = cells.Zip(cells.Skip(1), (a, b) => a >= b).All(x => x);
				if (lines.Count == 9 && names.SequenceEqual(want) && order)
					Log("PASS: sheet shots - 9 sheets (" + string.Join(" ", cells.Select(c => c.ToString()).ToArray()) + " cells, " + n + " faces), 9 pictures queued in that order");
				else Fail("sheet shots: " + lines.Count + " sheets (cells " + string.Join(" ", cells.Select(c => c.ToString()).ToArray()) + ", largest first " + order + "), queued " + names.Count + ": " + string.Join(", ", names.ToArray()));
			}
			catch (Exception ex) { Fail("sheet shots: " + ex.GetType().Name + " " + ex.Message); }
			finally
			{
				checkShotQueue.Clear(); checkShots = was; checkRockShots = wasRocks;
				UnityEngine.Object.Destroy(go);
			}
		}

		[ConsoleCommand(name: "CICliffsKeepUnit", docs: "Dev, the editor (opened when needed): the recipe's cliffs step keeps keep= circles as they are and carries the loot and creatures its talus would bury (lib 3.5.1 11-23: the talus filled Crater Lair's dock pool, buried the dive sites' boats and Sundown's angler). A generated island's ground is set to a 20 m deep sea floor with two 40 m plateaus 10 m over the sea; a crate and a puffer fish in front of plateau A, a fish far off, a sunk wall piece in front of A, cliffs keep= round plateau B: A sloped, nothing in B's circle changed, the crate on the new ground, the fish still in the water over it, the far fish and the wall not moved")]
		public static void CliffsKeepUnitCommand(string[] args) { StartTest(CliffsKeepUnitRoutine()); }

		static IEnumerator CliffsKeepUnitRoutine()
		{
			bool ok = true;
			string pa = Path.Combine(RecipeFolder, "ci_cliffkeep_a.recipe"), pb = Path.Combine(RecipeFolder, "ci_cliffkeep_b.recipe"), pc = Path.Combine(RecipeFolder, "ci_cliffkeep_c.recipe");
			Func<float, string> inv = f => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
			try { Directory.CreateDirectory(RecipeFolder); File.WriteAllLines(pa, new[] { "# CICliffsKeepUnit: the island", "new", "gen Seed=4242 Radius=60 Height=20 Style=0", "generate" }); }
			catch (Exception e) { Fail("cliffs keep: writing " + pa + ": " + e.Message); yield break; }
			yield return RecipeRoutine("ci_cliffkeep_a");
			yield return new WaitForSecondsRealtime(0.5f);
			Terrain t = terraineditor.terrain;
			if (t == null) { Fail("cliffs keep: no island after the first recipe"); yield break; }
			TerrainData td = t.terrainData;
			int res = td.heightmapResolution;
			Vector3 tp = t.transform.position, size = td.size;
			float sea = tp.y + DynamicIslands.EditorWaterLevel;
			// (the ground: a sea floor 20 m under the sea, plateau A 300 m west of the middle and B 300 m east, 40 m square, 10 m over it)
			Vector3 c = tp + new Vector3(size.x / 2f, 0f, size.z / 2f);
			Vector2 A = new Vector2(c.x - 300f, c.z), B = new Vector2(c.x + 300f, c.z);
			var h = new float[res, res];
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					float wx = tp.x + x * size.x / (res - 1), wz = tp.z + z * size.z / (res - 1);
					bool up = (Mathf.Abs(wx - A.x) <= 20f && Mathf.Abs(wz - A.y) <= 20f) || (Mathf.Abs(wx - B.x) <= 20f && Mathf.Abs(wz - B.y) <= 20f);
					h[z, x] = ((up ? sea + 10f : sea - 20f) - tp.y) / size.y;
				}
			td.SetHeights(0, 0, h);
			Physics.SyncTransforms();
			yield return null;
			// (in front of A's east face: a crate on the floor 6 m out, a puffer fish 10 m out 14 m under the sea - the talus reaches 10 m out
			// there, 3 m a point down to 10 m under the sea: the first test's crate 12 m out stood past it; the fish 8 m out (ground 5 m under
			// the sea after) had no room under the sea for its box and sat on the new ground - 11-23 mnu3; one far off)
			try
			{
				File.WriteAllLines(pb, new[]
				{
					"# CICliffsKeepUnit: the things on the sea floor",
					"origin " + inv(c.x) + " " + inv(c.z),
					"place Loot_Crate cikcrate at " + inv(A.x - c.x + 26f) + " 0",
					"place Creature_PufferFish cikfish at " + inv(A.x - c.x + 30f) + " 6 h=-14",
					"place Creature_PufferFish cikfar at 0 -200 h=-14",
					"place Block_Wall_Tier3 cikwall at " + inv(A.x - c.x) + " 26",
				});
				File.WriteAllLines(pc, new[] { "# CICliffsKeepUnit: the cliffs step", "origin " + inv(c.x) + " " + inv(c.z), "cliffs keep=" + inv(B.x - c.x) + ",0,50" });
			}
			catch (Exception e) { Fail("cliffs keep: writing the recipes: " + e.Message); yield break; }
			yield return RecipeRoutine("ci_cliffkeep_b");
			yield return null;
			Func<string, Vector2, EditorGameObject> find = (n, near) => SelectionTools.Placed().Where(x => x != null && x.GameObjectName == n)
				.OrderBy(x => new Vector2(x.transform.position.x - near.x, x.transform.position.z - near.y).sqrMagnitude).FirstOrDefault();
			Func<EditorGameObject, Bounds> box = o =>
			{
				Renderer[] rs = o.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
				Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(o.transform.position, Vector3.one);
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				return b;
			};
			EditorGameObject crate = find("Loot_Crate", A + new Vector2(26f, 0f)), fish = find("Creature_PufferFish", A + new Vector2(30f, 6f)), far = find("Creature_PufferFish", new Vector2(c.x, c.z - 200f));
			if (crate == null || fish == null || far == null || fish == far) { Fail("cliffs keep: the crate and two fish not placed"); yield break; }
			EditorGameObject wall = find("Block_Wall_Tier3", A + new Vector2(0f, 26f));
			if (wall == null) { Fail("cliffs keep: the sunk wall not placed"); yield break; }
			float wallY = wall.transform.position.y;
			Bounds crate0 = box(crate), fish0 = box(fish);
			float farY = far.transform.position.y, crateSunk = crate0.min.y - (t.SampleHeight(crate0.center) + tp.y), fishGap = fish0.min.y - (t.SampleHeight(fish0.center) + tp.y);
			float[,] before = td.GetHeights(0, 0, res, res);
			yield return RecipeRoutine("ci_cliffkeep_c");
			yield return null;
			try { File.Delete(pa); File.Delete(pb); File.Delete(pc); } catch { }
			float[,] after = td.GetHeights(0, 0, res, res);
			int inB = 0, changedB = 0, raisedA = 0;
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					float wx = tp.x + x * size.x / (res - 1), wz = tp.z + z * size.z / (res - 1);
					bool changed = Mathf.Abs(after[z, x] - before[z, x]) * size.y > 0.01f;
					if (new Vector2(wx - B.x, wz - B.y).magnitude <= 50f) { inB++; if (changed) changedB++; }
					else if (changed && new Vector2(wx - A.x, wz - A.y).magnitude <= 70f) raisedA++;
				}
			Check(ref ok, raisedA > 100, "plateau A's faces sloped: " + raisedA + " points raised");
			Check(ref ok, inB > 1000 && changedB == 0, "nothing changed in B's keep circle: " + changedB + " of " + inB + " points");
			Bounds crate1 = box(crate), fish1 = box(fish);
			float g1 = t.SampleHeight(crate1.center) + tp.y, f1 = t.SampleHeight(fish1.center) + tp.y;
			Check(ref ok, crate1.min.y > crate0.min.y + 3f && Mathf.Abs(crate1.min.y - g1 - crateSunk) < 0.5f,
				"the crate on the new ground: " + inv(crate0.min.y - sea) + " -> " + inv(crate1.min.y - sea) + " m, the ground " + inv(g1 - sea) + " (its bottom " + inv(crateSunk) + " m from the ground before)" +
				" (it stands at A + " + inv(crate1.center.x - A.x) + " " + inv(crate1.center.z - A.y) + "; the ground in front of A's east face, 2-16 m out, before -> after:" +
				string.Join(",", Enumerable.Range(1, 8).Select(i => { int gx = Mathf.RoundToInt((A.x + 20f + 2f * i - tp.x) / (size.x / (res - 1))), gz = Mathf.RoundToInt((A.y - tp.z) / (size.z / (res - 1)));
					return " " + inv(before[gz, gx] * size.y + tp.y - sea) + ">" + inv(after[gz, gx] * size.y + tp.y - sea); }).ToArray()) + ")");
			Check(ref ok, fish1.min.y > fish0.min.y + 3f && fish1.min.y >= f1 && fish1.max.y < sea,
				"the fish over the new ground, still in the water: bottom " + inv(fish0.min.y - sea) + " -> " + inv(fish1.min.y - sea) + " m, top " + inv(fish0.max.y - sea) + " -> " + inv(fish1.max.y - sea) + " m (it was " + inv(fishGap) + " m over the floor), the ground " + inv(f1 - sea));
			Check(ref ok, Mathf.Abs(far.transform.position.y - farY) < 0.01f, "the far fish not moved");
			// (a building piece on the sea floor holds the talus back, never rides up on it: Skyhook Mast's sunk room - 11-23)
			Check(ref ok, Mathf.Abs(wall.transform.position.y - wallY) < 0.01f, "the sunk wall in front of A's north face not moved: " + inv(wallY - sea) + " -> " + inv(wall.transform.position.y - sea) + " m");
			if (ok) Log("PASS: cliffs keep - A sloped (" + raisedA + " points), B's circle as it was, the crate and the fish carried up");
			else Fail("cliffs keep");
		}

		const float SheetRise = 7f;   // (one heightmap step this tall: the land's skin stretched up a wall)
		const float SheetCellM = 8f;  // (faces are gathered in cells this wide; neighbouring cells make one sheet)
		const int SheetCells = 3;     // (a sheet this many cells or more is a problem - a single steep notch is not)
		const int SheetEncWays = 6;   // (land round the face's foot in this many of 8 ways or more: a pit's wall, not the open coast)

		/// <summary>One bare cliff face: a heightmap step from Hx,Hz (high) to Lx,Lz (low); Top and Bot above the sea (m).</summary>
		struct SheetFace { public int Hx, Hz, Lx, Lz; public float Top, Bot, Rise; public Vector3 Hi, Lo; }

		/// <summary>The land's bare cliff faces at the waterline (SheetProblems, the recipe's cliffs step): one heightmap step
		/// that rises minRise or more from under the sea (2 m down) to over it (0.5 m up), seen from its low side - a ray at
		/// the waterline hits the land first, not a rock or a wall set in front of it. sea: the sea's world height.</summary>
		static List<SheetFace> BareFaces(Terrain land, float sea, float minRise, float[,] hm)
		{
			var list = new List<SheetFace>();
			TerrainData td = land.terrainData;
			int res = td.heightmapResolution;
			Vector3 tp = land.transform.position, size = td.size;
			float sx = size.x / (res - 1), sz = size.z / (res - 1);
			Physics.SyncTransforms();
			for (int z = 0; z < res - 1; z++)
				for (int x = 0; x < res - 1; x++)
					for (int k = 0; k < 2; k++)
					{
						int nx = k == 0 ? x + 1 : x, nz = k == 0 ? z : z + 1;
						float a = hm[z, x] * size.y + tp.y, b = hm[nz, nx] * size.y + tp.y;
						float rise = Mathf.Abs(a - b), top = Mathf.Max(a, b) - sea, bot = Mathf.Min(a, b) - sea;
						if (rise < minRise || top < 0.5f || bot > -2f) continue;
						var f = new SheetFace { Hx = a >= b ? x : nx, Hz = a >= b ? z : nz, Lx = a >= b ? nx : x, Lz = a >= b ? nz : z, Top = top, Bot = bot, Rise = rise };
						f.Hi = new Vector3(tp.x + f.Hx * sx, sea, tp.z + f.Hz * sz);
						f.Lo = new Vector3(tp.x + f.Lx * sx, sea, tp.z + f.Lz * sz);
						Vector3 outward = (f.Lo - f.Hi).normalized;
						float y = sea + Mathf.Clamp(top * 0.5f, 0.25f, 3f);
						Vector3 from = f.Lo + outward * 0.5f; from.y = y;
						// (the low side's ground over the ray's start: a trench, not a face seen from the sea)
						if (land.SampleHeight(from) + tp.y > y) continue;
						if (FaceCover(from, -outward, sx + 1f, null) == null) list.Add(f);
					}
			return list;
		}

		/// <summary>What stands in front of a face: from the ray's start (0.5 m out of the face's low point, halfway up its part over
		/// the sea) back towards the face for len m, the first collider before the land - or one the start lies in. A rock stood half
		/// in the wall holds the start inside it, and a ray never meets the collider it starts in: c3z64 counted Caisson Yard's yard
		/// rock bare where a stood rock (and the logs it stood over) covered it - a new 7-face sheet. So the colliders round the
		/// start count, and with back faces on a mesh's inside does (a natural rock's or a log's only: a one-sided skin seen from
		/// behind is no cover). counts: only these count (null: all but the land). Null when the face is bare.</summary>
		static Collider FaceCover(Vector3 from, Vector3 back, float len, System.Func<Collider, bool> counts)
		{
			foreach (Collider c in Physics.OverlapSphere(from, 0.05f, ~0, QueryTriggerInteraction.Ignore))
				if (!(c is TerrainCollider) && (counts != null ? counts(c) : CoversFromInside(c))) return c;
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				foreach (RaycastHit h in Physics.RaycastAll(from, back, len, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
				{
					if (h.collider is TerrainCollider) return null;
					bool inside = Vector3.Dot(h.normal, back) > 0f;
					if (counts != null ? counts(h.collider) : !inside || CoversFromInside(h.collider)) return h.collider;
				}
				return null;
			}
			finally { Physics.queriesHitBackfaces = backs; }
		}

		/// <summary>A collider whose inside a face's ray may start in and still be cover: a natural rock's or a log's.</summary>
		static bool CoversFromInside(Collider c)
		{
			string n = PieceName(c);
			return NaturalRock(n) || Regex.IsMatch(n, @"^Log(_|\s|$)|Driftwood", RegexOptions.IgnoreCase);
		}

		/// <summary>The piece a collider belongs to: the editor's name, or in a spawned island the child of its Objects, "(Clone)" off.</summary>
		static string PieceName(Collider c)
		{
			EditorGameObject by = c.GetComponentInParent<EditorGameObject>();
			if (by != null) return (by.GameObjectName ?? by.name).Replace("(Clone)", "").Trim();
			for (Transform t = c.transform; t != null; t = t.parent)
				if (t.parent != null && t.parent.name == "Objects") return t.name.Replace("(Clone)", "").Trim();
			return c.name.Replace("(Clone)", "").Trim();
		}

		/// <summary>In how many of 8 ways the land rises over the sea within 80 m of a face's foot: 6 or more is a pit's wall
		/// (a blue hole, a dive shaft - seen from its rim), fewer the open coast (seen from the sea as the raft comes in).</summary>
		static int SheetEnclosed(float[,] hm, int res, float sizeY, float tpY, float sea, int lx, int lz)
		{
			int ways = 0;
			for (int w = 0; w < 8; w++)
			{
				float ang = w * Mathf.PI / 4f;
				float dx = Mathf.Cos(ang), dz = Mathf.Sin(ang);
				for (int s = 2; s <= 40; s++)
				{
					int x = lx + Mathf.RoundToInt(dx * s), z = lz + Mathf.RoundToInt(dz * s);
					if (x < 0 || z < 0 || x >= res || z >= res) break;
					if (hm[z, x] * sizeY + tpY > sea) { ways++; break; }
				}
			}
			return ways;
		}

		/// <summary>The land's bare cliff faces at the waterline (BareFaces), gathered into sheets: Raft paints the land from
		/// above, so such a face is drawn as the ground's texture smeared down a wall - Caisson Yard's crane-end picture showed a
		/// pale sheet half the frame wide that no check caught (c3z37 11-21). Each sheet says whether it is the open coast or a
		/// pit's wall (SheetEnclosed), how high it stands over the sea, and what the ground there is painted; with the check's
		/// 'shots' word every one is pictured from its low side, largest first (shot_sheet_<island>_<n>). count = the faces measured.</summary>
		static List<string> SheetProblems(IslandWorldState.Entry e, out int count)
		{
			var lines = new List<string>();
			count = 0;
			Terrain land = e.Root != null ? e.Root.GetComponentInChildren<Terrain>() : null;
			if (land == null || land.terrainData == null) return lines;
			TerrainData td = land.terrainData;
			int res = td.heightmapResolution;
			float[,] hm = td.GetHeights(0, 0, res, res);
			Vector3 tp = land.transform.position, size = td.size, mid = e.Position;
			float sea = 0f, sx = size.x / (res - 1);
			int cell = Mathf.Max(1, Mathf.RoundToInt(SheetCellM / sx));
			// (cell -> the faces in it)
			var cells = new Dictionary<long, List<SheetFace>>();
			foreach (SheetFace f in BareFaces(land, sea, SheetRise, hm))
			{
				count++;
				long key = (long)(f.Lx / cell) * 100000L + (f.Lz / cell);
				List<SheetFace> c;
				if (!cells.TryGetValue(key, out c)) cells[key] = c = new List<SheetFace>();
				c.Add(f);
			}
			// (the paint at a face: the alphamap halfway down it - what Raft stretches over the wall)
			int ares = td.alphamapResolution, layers = td.alphamapLayers;
			float[,,] alpha = layers > 0 ? td.GetAlphamaps(0, 0, ares, ares) : null;
			string[] layerName = { "seabed", "sand", "grass", "rock" };
			// (neighbouring cells, the corners too, make one sheet)
			var seen = new HashSet<long>();
			var sheets = new List<KeyValuePair<int, string>>();
			var shots = new List<KeyValuePair<int, Vector3[]>>();
			foreach (long start in cells.Keys)
			{
				if (seen.Contains(start)) continue;
				var stack = new Stack<long>(); stack.Push(start); seen.Add(start);
				int n = 0, faces = 0, encSum = 0;
				float tallest = 0f, highest = 0f, deepest = 0f;
				Vector3 sum = Vector3.zero, outSum = Vector3.zero;
				var paint = new float[TerrainPainter.LayerCount];
				while (stack.Count > 0)
				{
					long q = stack.Pop(); List<SheetFace> c = cells[q];
					n++;
					foreach (SheetFace f in c)
					{
						faces++; tallest = Mathf.Max(tallest, f.Rise); highest = Mathf.Max(highest, f.Top); deepest = Mathf.Min(deepest, f.Bot);
						sum += (f.Hi + f.Lo) * 0.5f; outSum += (f.Lo - f.Hi).normalized;
						if (alpha != null)
						{
							Vector3 m = (f.Hi + f.Lo) * 0.5f;
							int ax = Mathf.Clamp(Mathf.FloorToInt((m.x - tp.x) / size.x * ares), 0, ares - 1), az = Mathf.Clamp(Mathf.FloorToInt((m.z - tp.z) / size.z * ares), 0, ares - 1);
							for (int l = 0; l < layers; l++) paint[l % TerrainPainter.LayerCount] += alpha[az, ax, l];
						}
					}
					SheetFace one = c[0];
					encSum += SheetEnclosed(hm, res, size.y, tp.y, sea, one.Lx, one.Lz);
					long cx = q / 100000L, cz = q % 100000L;
					for (long dx = -1; dx <= 1; dx++)
						for (long dz = -1; dz <= 1; dz++)
						{
							long r = (cx + dx) * 100000L + cz + dz;
							if (cells.ContainsKey(r) && seen.Add(r)) stack.Push(r);
						}
				}
				if (n < SheetCells) continue;
				Vector3 centre = sum / faces;
				float enc = encSum / (float)n;
				int best = 0;
				for (int l = 1; l < paint.Length; l++) if (paint[l] > paint[best]) best = l;
				float share = paint.Sum() > 0f ? paint[best] / paint.Sum() : 0f;
				bool pit = enc >= SheetEncWays;
				sheets.Add(new KeyValuePair<int, string>(n, "bare cliff sheet: the land's own face at the waterline round " + Num(centre.x - mid.x) + " " + Num(centre.z - mid.z) +
					", " + n + " cells of " + Num(SheetCellM) + " m (" + faces + " faces), up to " + Num(tallest) + " m in one step, its top h=" + Num(highest) + ", its foot h=" + Num(deepest) +
					"; " + (pit ? "a pit's wall (land round it in " + enc.ToString("F1") + " of 8 ways: a blue hole or a dive shaft, seen from its rim)" : "the open coast (land round it in " + enc.ToString("F1") + " of 8 ways: seen from the sea)") +
					", painted " + layerName[best] + " " + Mathf.RoundToInt(share * 100f) + "%" +
					" - drawn as the ground's texture stretched down a wall; slope it (the recipe step cliffs), or stand rock or a wall in front of it"));
				// (a picture from its low side, 3.5 m over the sea, as far out as the land lets - 22 m, less in a pit)
				Vector3 outward = new Vector3(outSum.x, 0f, outSum.z).normalized;
				if (outward == Vector3.zero) outward = Vector3.forward;
				Vector3 at = new Vector3(centre.x, sea + Mathf.Clamp(highest * 0.4f, 0.5f, 4f), centre.z);
				Vector3 eye = SheetEye(land, tp, sea, centre, outward, at, null, null);
				shots.Add(new KeyValuePair<int, Vector3[]>(n, new[] { eye, at }));
			}
			foreach (var s in sheets.OrderByDescending(s => s.Key)) lines.Add(s.Value);
			// (every sheet pictured, largest first, past the queue's cap of 8: with only the 2 largest a new or changed smaller
			// sheet went unpictured - lib 3.5.1 11-23, Varuna Point's 4 sheets, Balboa's 3)
			int shot = 0;
			foreach (var s in shots.OrderByDescending(s => s.Key)) QueueCheckShot("sheet_" + e.Name + "_" + ++shot, s.Value[0], s.Value[1], true);
			if (checkShots && checkRockShots != null) foreach (var rs in RockShots(e, land, tp, sea, checkRockShots)) QueueCheckShot(rs.Key, rs.Value[0], rs.Value[1]);
			return lines;
		}

		/// <summary>A natural rock (a sea stack, a boulder, coral, kelp), not a built piece of stone: the cliffs step may sink it
		/// into its talus up to its middle.</summary>
		static bool NaturalRock(string n)
		{
			string[] built = { "Wall", "Stair", "Floor", "Pillar", "Arch", "Bridge", "Cave", "Tunnel", "Foundation", "Block" };
			string[] rock = { "Rock", "Boulder", "Stone", "Coral", "SeaVine", "Kelp", "Stack" };
			return rock.Any(w => n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) && !built.Any(w => n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		/// <summary>The recipe's cliffs step: every bare cliff face at the waterline (BareFaces, rise or more in one step)
		/// sloped from its low side - the sea floor in front of it raised into a talus no steeper than `step` m per heightmap
		/// point (1.95 m; 3 m = 57 degrees, which Raft paints rock and draws without the stretched sheet), from the face's top
		/// down to `depth` m under the sea. Deeper the wall stays a wall: a blue hole or a dive shaft keeps its depth, only its
		/// rim is sloped. A tall face's talus reaches past `reach` points as far as it needs to get down to `depth`. Only ground
		/// at most 1 m over the sea is raised, nothing within 3 points of the terrain's edge, nothing a placed piece's body
		/// stands in (its colliders; a piece without one, its bounds), so no piece is buried - a natural rock (NaturalRock) may
		/// sink in up to its middle or 1 m under its top (a rock wholly under the sea may be buried; the talus stops at a rock's
		/// cap rather than at its side) - and nothing between two opposite walls, judged in front of each face only (a narrow shaft
		/// or channel stays open: it stays as it was and is still reported). A small thing wholly under the sea (4 m wide, 8 m
		/// tall at most: coral, kelp, a sea-floor rock) is carried up onto the new ground instead. The log line names the pieces
		/// that held most of the talus back and why each face left is still bare, with up to 3 of them in the recipe's
		/// coordinates. at x z r=: only faces in that circle. rocks=piece: that rock stood in the waterline at the faces still
		/// bare (StandCliffRocks - a pit's wall the talus can't slope). keep: circles (world x, z, radius) the talus leaves as
		/// they are - no point raised in them, no face in them sloped or rocked (a dock pool, a dive site, a fish pool). Loot,
		/// pickups, notes and creatures the talus would bury ride up with it, keeping their height over the ground (under the sea if they
		/// were). The ground's hand paint
		/// is dropped on the raised points and the area painted again (the steep talus goes rock); one undo step for the ground
		/// (the carried things stay where they went).</summary>
		static string CliffsStep(float rise, float step, float depth, int reach, Vector2? at, float atR, string rocks, List<Vector3> keep = null)
		{
			Terrain land = terraineditor.terrain;
			if (land == null || land.terrainData == null) return "cliffs: no terrain";
			TerrainData td = land.terrainData;
			int res = td.heightmapResolution, ares = td.alphamapResolution;
			Vector3 tp = land.transform.position, size = td.size;
			float sea = tp.y + DynamicIslands.EditorWaterLevel, sx = size.x / (res - 1), sz = size.z / (res - 1);
			float[,] before = td.GetHeights(0, 0, res, res);
			List<SheetFace> faces = BareFaces(land, sea, rise, before);
			if (at.HasValue) faces = faces.Where(f => (new Vector2(f.Hi.x, f.Hi.z) - at.Value).magnitude <= atR).ToList();
			// (keep: what the island was built round at the waterline - a dock pool under a hangar door, a sunk boat on the sea floor,
			// a fish pool - stays as it is: the talus filled Crater Lair's dock pool and buried the dive sites' boats - lib 3.5.1 11-23)
			Func<float, float, bool> kept0 = (wx, wz) => keep != null && keep.Any(k => (wx - k.x) * (wx - k.x) + (wz - k.y) * (wz - k.y) <= k.z * k.z);
			int keptFaces = faces.Count;
			faces = faces.Where(f => !kept0(f.Hi.x, f.Hi.z) && !kept0(f.Lo.x, f.Lo.z)).ToList();
			keptFaces -= faces.Count;
			if (faces.Count == 0) return "cliffs: no bare cliff face" + (at.HasValue ? " in the circle" : "") + (keptFaces > 0 ? " (" + keptFaces + " kept as they are)" : "");
			// (what stands there keeps the ground under it as it is. A big piece with a body is measured by its body - a column of
			// the talus that meets one of its colliders is left - not by its box: the boxes of sea stacks, wrecks and long rocks
			// round a face left 1641-4853 points of the talus out and the faces bare (c3z43: Frontier Beacon's slab, The Abyss's
			// band, Shelter Atoll's and Storm Spire's sheets, Old Mine Islet's cliffs round the boat). A small thing wholly under
			// the sea - coral, kelp, a sea-floor rock, a pickup - does not hold the talus back: it rides up onto the new ground)
			var keepBox = new List<KeyValuePair<Bounds, string>>();
			var keepBody = new List<KeyValuePair<Bounds, string>>();
			var bodyOf = new Dictionary<Collider, string>();
			var keepBoxCap = new List<float>();
			var rockCapOf = new Dictionary<Collider, float>();
			var carry = new List<KeyValuePair<Transform, Bounds>>();
			// (loot, pickups, notes and creatures the talus would bury ride up with it - Vasagatan's clay and mango tree, Cistern
			// Hall's mushrooms, Sundown's angler, The Foundation Piles' notes on the pool floor went under the talus - lib 3.5.1 11-23)
			var loose = new List<KeyValuePair<Transform, Bounds>>();
			foreach (EditorGameObject o in UnityEngine.Object.FindObjectsOfType<EditorGameObject>())
			{
				if (o == null || !o.gameObject.activeInHierarchy) continue;
				string n0 = (o.GameObjectName ?? o.name).Replace("(Clone)", "").Trim();
				if (ContentCatalog.IsCreature(n0) || n0.StartsWith("Loot_", StringComparison.Ordinal) || n0.StartsWith("Pickup_", StringComparison.Ordinal) || n0.StartsWith("Note_", StringComparison.Ordinal))
				{
					Renderer[] lr = o.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
					Bounds lb = lr.Length > 0 ? lr[0].bounds : new Bounds(o.transform.position, Vector3.one);
					foreach (Renderer r in lr) lb.Encapsulate(r.bounds);
					loose.Add(new KeyValuePair<Transform, Bounds>(o.transform, lb));
					continue;
				}
				Renderer[] rs = o.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
				Collider[] cs = o.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger && !(c is TerrainCollider)).ToArray();
				if (rs.Length == 0 && cs.Length == 0) continue;
				Bounds b = rs.Length > 0 ? rs[0].bounds : cs[0].bounds;
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				foreach (Collider c in cs) b.Encapsulate(c.bounds);
				string n = (o.GameObjectName ?? o.name).Replace("(Clone)", "").Trim();
				// (zones, creatures, notes, loot, pickups and markers are no ground anyone stands on: a zone's editor box held 343 of
				// Old Mine Islet's 486 kept points and left its sheets bare - c3z52/z57)
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_", StringComparison.Ordinal) || n.StartsWith("Note_", StringComparison.Ordinal) || n.StartsWith("Loot_", StringComparison.Ordinal) ||
					n.StartsWith("Pickup_", StringComparison.Ordinal) || n == ContentCatalog.MarkerOnly || CameraPath.IsPoint(n)) continue;
				bool fixedPiece = n.StartsWith("ZiplinePath", StringComparison.Ordinal) || n.StartsWith("VP_Skylift", StringComparison.Ordinal) || n == "Elevator" ||
					n.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0 || CaveBacks.Wants(n);
				// (a building piece - a sunk room's walls, a pier's stairs - is never carried: it holds the talus back. Carried, Skyhook
				// Mast's sunk room's walls rode up 0.1-1.1 m apart and its floor filled 2 m deep round the note - lib 3.5.1 11-23)
				bool builtPiece = CaveTool.IsBuildingPiece(n) || n.StartsWith("Relay_", StringComparison.Ordinal) || n.StartsWith("Placeable_", StringComparison.Ordinal);
				if (!fixedPiece && !builtPiece && b.max.y < sea - 0.3f && b.size.x <= 4f && b.size.z <= 4f && b.size.y <= 8f) { carry.Add(new KeyValuePair<Transform, Bounds>(o.transform, b)); continue; }
				// (a natural rock - a sea stack, a boulder, coral - may sink into the talus up to its middle or 1 m under its top,
				// whichever is higher, and one wholly under the sea may be buried: it then looks set in the slope. Whole, the big
				// rocks round a face held its talus back and left it a sheet - c3z59: Old Mine Islet; c3z61: The Abyss's south coast
				// behind a sunken BigSharpRock_5, 263 points)
				float mid = !fixedPiece && NaturalRock(n) ? (b.max.y < sea ? float.PositiveInfinity : Mathf.Max(b.center.y, b.max.y - 1f)) : float.NaN;
				b.Expand(new Vector3(2f, 0f, 2f));
				if (cs.Length == 0) { keepBox.Add(new KeyValuePair<Bounds, string>(b, n)); keepBoxCap.Add(mid); continue; }
				keepBody.Add(new KeyValuePair<Bounds, string>(b, n));
				foreach (Collider c in cs) { bodyOf[c] = n; if (!float.IsNaN(mid)) rockCapOf[c] = mid; }
			}
			var blockers = new Dictionary<string, int>();
			Physics.SyncTransforms();
			// (the piece standing in the talus column at wx,wz from the old ground h0 up to the talus's top, or null)
			// (rockLimit: after a call, the highest the talus may go there under the natural rocks that held it, or infinity)
			float rockLimit = float.PositiveInfinity;
			Func<float, float, float, float, string> standing = (wx, wz, h0, top) =>
			{
				rockLimit = float.PositiveInfinity;
				string rockWho = null;
				for (int k = 0; k < keepBox.Count; k++)
				{
					Bounds kb = keepBox[k].Key;
					if (wx >= kb.min.x + 1f && wx <= kb.max.x - 1f && wz >= kb.min.z + 1f && wz <= kb.max.z - 1f && kb.min.y < top + 0.2f)
					{
						if (float.IsNaN(keepBoxCap[k])) return keepBox[k].Value;
						if (top > keepBoxCap[k] && keepBoxCap[k] < rockLimit) { rockLimit = keepBoxCap[k]; rockWho = keepBox[k].Value; }
					}
				}
				if (!keepBody.Any(k => wx >= k.Key.min.x && wx <= k.Key.max.x && wz >= k.Key.min.z && wz <= k.Key.max.z && k.Key.min.y < top + 0.2f)) return rockWho;
				Vector3 half = new Vector3(sx * 0.5f + 1f, (top + 0.2f - h0) * 0.5f + 0.1f, sz * 0.5f + 1f);
				foreach (Collider c in Physics.OverlapBox(new Vector3(wx, (h0 + top + 0.2f) * 0.5f, wz), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
				{
					string who;
					float rm;
					if (rockCapOf.TryGetValue(c, out rm))
					{
						if (top > rm && rm < rockLimit) { rockLimit = rm; bodyOf.TryGetValue(c, out rockWho); }
						continue;
					}
					if (bodyOf.TryGetValue(c, out who)) return who;
				}
				return rockWho;
			};
			float[,] after = (float[,])before.Clone();
			float floor = sea - depth;
			int blocked = 0;
			var raised = new HashSet<int>();
			// (the way out of the wall each raised point was raised from; raised from two opposite walls it lies between them - a
			// shaft or a channel - and stays as it was, with a point round it, so a narrow dive shaft is never plugged)
			var from = new Dictionary<int, Vector2>();
			var between = new HashSet<int>();
			var blockedAt = new Dictionary<int, string>();
			var keptAt = new HashSet<int>();
			foreach (SheetFace f in faces)
			{
				float apex = sea + f.Top;
				Vector2 way = new Vector2(f.Lo.x - f.Hi.x, f.Lo.z - f.Hi.z).normalized;
				// (a tall face's talus reaches as far as it needs to get down to the floor: cut short at reach it would end in a new
				// wall at the waterline)
				int fr = Mathf.Clamp(Mathf.CeilToInt((apex - floor) / step) + 1, reach, 40);
				for (int dz = -fr; dz <= fr; dz++)
					for (int dx = -fr; dx <= fr; dx++)
					{
						int x = f.Hx + dx, z = f.Hz + dz;
						if (x < 3 || z < 3 || x > res - 4 || z > res - 4) continue;
						float d = Mathf.Sqrt(dx * dx + dz * dz);
						if (d < 0.5f || d > fr) continue;
						float target = apex - step * d;
						if (target < floor) continue;
						float h0 = before[z, x] * size.y + tp.y;
						if (h0 > sea + 1f || target <= h0) continue;
						if (keep != null && kept0(tp.x + x * sx, tp.z + z * sz)) continue;
						// (only a point in front of the face - within 60 degrees of straight out - says which way it was raised from.
						// The talus is a full disc round the face's top, so beside and behind a face its rim met the next face's talus
						// coming the other way along every bay, cape and ridge: 574 of Old Mine Islet's points kept as "between", each
						// leaving a fresh 8-10 m wall at the waterline - c3z59)
						Vector2 had;
						if (Vector2.Dot(new Vector2(dx, dz) / d, way) >= 0.5f)
						{
							if (from.TryGetValue(z * res + x, out had)) { if (Vector2.Dot(had, way) < -0.3f) between.Add(z * res + x); }
							else from[z * res + x] = way;
						}
						if (target <= after[z, x] * size.y + tp.y) continue;
						float wx = tp.x + x * sx, wz = tp.z + z * sz;
						string who = standing(wx, wz, h0, target);
						// (held by natural rocks only: raised as far as they let it - a lower column meets nothing the full one did not)
						if (who != null && !float.IsPositiveInfinity(rockLimit) && rockLimit > h0 + 0.3f && rockLimit > after[z, x] * size.y + tp.y)
						{
							after[z, x] = (rockLimit - tp.y) / size.y;
							raised.Add(z * res + x);
							blockedAt[z * res + x] = who;
							continue;
						}
						if (who != null) { blocked++; blockedAt[z * res + x] = who; int bn; blockers.TryGetValue(who, out bn); blockers[who] = bn + 1; continue; }
						blockedAt.Remove(z * res + x);
						after[z, x] = (target - tp.y) / size.y;
						raised.Add(z * res + x);
					}
			}
			int kept = 0;
			foreach (int i in raised.ToList())
			{
				int x = i % res, z = i / res;
				bool near = false;
				for (int dz = -1; dz <= 1 && !near; dz++)
					for (int dx = -1; dx <= 1 && !near; dx++) near = between.Contains((z + dz) * res + x + dx);
				if (!near) continue;
				after[z, x] = before[z, x];
				raised.Remove(i);
				keptAt.Add(i);
				kept++;
			}
			string most3 = blockers.Count == 0 ? "" : " (most under " + string.Join(", ", blockers.OrderByDescending(k => k.Value).Take(4).Select(k => k.Key + " " + k.Value).ToArray()) + ")";
			if (raised.Count == 0) return "cliffs: " + faces.Count + " bare faces, nothing raised (" + blocked + " points under objects" + most3 + ")";
			float[,,] alphaBefore = td.GetAlphamaps(0, 0, ares, ares);
			float[,] maskBefore = terraineditor.paintMask != null ? (float[,])terraineditor.paintMask.Clone() : null;
			// (the small things on the sea floor ride up with it: their ground before, at their middle)
			var carryFrom = carry.Select(c => land.SampleHeight(c.Value.center) + tp.y).ToList();
			var looseFrom = loose.Select(c => land.SampleHeight(c.Value.center) + tp.y).ToList();
			td.SetHeights(0, 0, after);
			int carried = 0;
			for (int i = 0; i < carry.Count; i++)
			{
				Bounds cb = carry[i].Value;
				float low = float.MaxValue;
				foreach (Vector3 p in new[] { cb.center, new Vector3(cb.min.x, 0f, cb.min.z), new Vector3(cb.min.x, 0f, cb.max.z), new Vector3(cb.max.x, 0f, cb.min.z), new Vector3(cb.max.x, 0f, cb.max.z) })
					low = Mathf.Min(low, land.SampleHeight(p) + tp.y);
				float lift = low - carryFrom[i];
				if (lift < 0.05f || carry[i].Key == null) continue;
				carry[i].Key.position += Vector3.up * lift;
				carried++;
			}
			// (a loose thing moves only where the ground at its middle rose and came up past its bottom, by as much as the ground
			// rose there - a fish keeps its water under it, a crate or a tree sits in the new ground as it sat in the old)
			int lifted = 0;
			for (int i = 0; i < loose.Count; i++)
			{
				Bounds lb = loose[i].Value;
				float now = land.SampleHeight(lb.center) + tp.y;
				if (now - looseFrom[i] < 0.05f || now <= lb.min.y + 0.05f || loose[i].Key == null) continue;
				// (as far as the ground rose, but one under the sea stays under it if it can - only what keeping it no deeper in the
				// ground than it was needs may lift it out)
				float lift = now - looseFrom[i];
				if (lb.max.y < sea) lift = Mathf.Min(lift, sea - 0.5f - lb.max.y);
				lift = Mathf.Max(lift, now - lb.min.y - Mathf.Max(0f, looseFrom[i] - lb.min.y));
				loose[i].Key.position += Vector3.up * lift;
				lifted++;
			}
			carried += lifted;
			if (carried > 0) Physics.SyncTransforms();
			Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
			float most = 0f;
			foreach (int i in raised)
			{
				int x = i % res, z = i / res;
				float wx = tp.x + x * sx, wz = tp.z + z * sz;
				most = Mathf.Max(most, (after[z, x] - before[z, x]) * size.y);
				min = Vector3.Min(min, new Vector3(wx, 0f, wz)); max = Vector3.Max(max, new Vector3(wx, 0f, wz));
				if (terraineditor.paintMask == null) continue;
				int ax = Mathf.FloorToInt((wx - tp.x) / size.x * ares), az = Mathf.FloorToInt((wz - tp.z) / size.z * ares);
				for (int az2 = az - 1; az2 <= az + 1; az2++)
					for (int ax2 = ax - 1; ax2 <= ax + 1; ax2++)
						if (ax2 >= 0 && az2 >= 0 && ax2 < terraineditor.paintMask.GetLength(1) && az2 < terraineditor.paintMask.GetLength(0)) terraineditor.paintMask[az2, ax2] = 0f;
			}
			min -= new Vector3(3f, 0f, 3f); max += new Vector3(3f, 0f, 3f);
			TerrainPainter.PaintWorldArea(land, sea, min, max, terraineditor.paintMask);
			CommandUndoRedo.UndoRedoManager.Insert(new TerrainStrokeCommand(td, new RectInt(0, 0, res, res), before, new RectInt(0, 0, ares, ares), alphaBefore, maskBefore, terraineditor.paintMask));
			// (why each face left is still there, by its low point: kept open between two walls, under a piece, at the terrain's
			// edge, at the talus's own side (its high point raised: the talus meets ground it could not raise), or other)
			List<SheetFace> leftFaces = BareFaces(land, sea, rise, after).Where(f => (!at.HasValue || (new Vector2(f.Hi.x, f.Hi.z) - at.Value).magnitude <= atR) &&
				!kept0(f.Hi.x, f.Hi.z) && !kept0(f.Lo.x, f.Lo.z)).ToList();
			int lBetween = 0, lUnder = 0, lEdge = 0, lSide = 0, lOther = 0;
			var lUnderBy = new Dictionary<string, int>();
			var examples = new List<string>();
			var toRock = new List<SheetFace>();
			foreach (SheetFace f in leftFaces)
			{
				int li = f.Lz * res + f.Lx, hi = f.Hz * res + f.Hx;
				string why, who;
				if (keptAt.Contains(li) || between.Contains(li)) { lBetween++; why = "between"; }
				else if (blockedAt.TryGetValue(li, out who)) { lUnder++; int un; lUnderBy.TryGetValue(who, out un); lUnderBy[who] = un + 1; why = "under " + who; }
				else if (f.Lx < 3 || f.Lz < 3 || f.Lx > res - 4 || f.Lz > res - 4) { lEdge++; why = "edge"; }
				else if (raised.Contains(hi)) { lSide++; why = "talus side"; }
				else { lOther++; why = "other"; }
				if (why != "edge") toRock.Add(f);
				if (examples.Count < 3 && why != "edge")
				{
					Frame tf = frames.Count > 0 ? TopFrame : new Frame { Origin = recipeOrigin };
					Vector3 r = Quaternion.Euler(0f, -tf.Yaw, 0f) * new Vector3(f.Hi.x - tf.Origin.x, 0f, f.Hi.z - tf.Origin.y);
					examples.Add(why + " at " + Num(r.x) + " " + Num(r.z) + " (" + Num(f.Top) + " to " + Num(f.Bot) + " m)");
				}
			}
			string whyLeft = leftFaces.Count == 0 ? "" : " (" + lBetween + " between two walls, " + lUnder + " under objects" +
				(lUnderBy.Count == 0 ? "" : " [" + string.Join(", ", lUnderBy.OrderByDescending(k => k.Value).Take(4).Select(k => k.Key + " " + k.Value).ToArray()) + "]") +
				", " + lEdge + " at the terrain's edge, " + lSide + " on the talus's side, " + lOther + " other; e.g. " + string.Join("; ", examples.ToArray()) + ")";
			string rockNote = "";
			if (rocks != null && toRock.Count > 0)
			{
				int covered;
				var rockBlocked = new Dictionary<string, int>();
				int stood = StandCliffRocks(rocks, toRock, sea, out covered, rockBlocked);
				rockNote = "; " + stood + " " + rocks + " stood in the waterline at " + covered + " of the " + toRock.Count + " faces left (not at the terrain's edge)" +
					(rockBlocked.Count == 0 ? "" : ", none where they would meet " + string.Join(", ", rockBlocked.OrderByDescending(k => k.Value).Take(4).Select(k => k.Key + " " + k.Value).ToArray())) + rockFillNote;
			}
			return "cliffs: " + faces.Count + " bare faces, " + raised.Count + " points raised (up to " + Num(most) + " m), " + carried + " small things on the sea floor carried up with it (" + lifted + " loot, pickups or creatures), " + blocked + " left under objects" + most3 + ", " + kept + " left open between two walls" + (keep == null ? "" : ", " + keptFaces + " faces kept as they are") + "; " + leftFaces.Count + " bare faces left" + whyLeft + rockNote;
		}

		/// <summary>CISheetIslands' rocks=<piece> pictures (c3z63 11-22: the cliffs step's stood rocks are to be seen before a merge -
		/// not floating, not see-through, not stretched oddly, not in a path): up to 4 of that piece reaching from under the sea to
		/// over it - the tallest, then each the farthest from those already chosen - each from the way the land falls lowest 8 m
		/// round it, the eye picked as a sheet's (the rock itself not counted in its way). The names say the island, the piece and
		/// its top over the sea; the log lists them.</summary>
		static List<KeyValuePair<string, Vector3[]>> RockShots(IslandWorldState.Entry e, Terrain land, Vector3 tp, float sea, string piece)
		{
			var list = new List<KeyValuePair<string, Vector3[]>>();
			var rocks = new List<KeyValuePair<Transform, Bounds>>();
			// (the island's Objects by name, "(Clone)" off: an IslandObjectRef is only on an object with props, and a plain rock
			// has none - c3z64 found no rock on either island though 24 and 18 stood)
			Transform objects = e.Root.transform.Find("Objects");
			int named = 0;
			float lowTop = float.MaxValue, highFoot = float.MinValue;
			if (objects != null)
				foreach (Transform r in objects)
				{
					if (r == null || !r.gameObject.activeInHierarchy || !piece.Split('|').Any(pc => string.Equals(r.name.Replace("(Clone)", "").Trim(), pc, StringComparison.OrdinalIgnoreCase))) continue;
					named++;
					Renderer[] rr = r.GetComponentsInChildren<Renderer>();
					if (rr.Length == 0) continue;
					Bounds b = rr[0].bounds;
					foreach (Renderer x in rr) b.Encapsulate(x.bounds);
					lowTop = Mathf.Min(lowTop, b.max.y - sea); highFoot = Mathf.Max(highFoot, b.min.y - sea);
					// (the stood ones: their feet 2.3-3 m under the sea, where the cliffs step sets them - c3z67 pictured the
					// generator's own BigBoulder1_Low on a dirt mound and on a sand shelf, once the step took them too)
					if (b.min.y < sea - 3.05f || b.min.y > sea - 2.25f || b.max.y < sea + 1f) continue;
					rocks.Add(new KeyValuePair<Transform, Bounds>(r, b));
				}
			if (rocks.Count == 0)
			{
				Log("  rock pictures: no " + piece + " standing in the waterline on " + e.Name + " (" + named + " of that name among " + (objects != null ? objects.childCount : 0) + " objects" +
					(named > 0 ? "; tops from " + Num(lowTop) + " m, feet up to " + Num(highFoot) + " m over the sea" : "; e.g. " + string.Join(", ", (objects != null ? objects.Cast<Transform>() : Enumerable.Empty<Transform>())
					.Select(t => t.name).Where(n => n.IndexOf("Boulder", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0).Distinct().Take(4).ToArray())) + ")");
				return list;
			}
			// (the tallest, then each the farthest from those before it; up to 4 with a clear eye, the ones without one skipped -
			// c3z68: 4 of 8 pictures were the fallback eye, a rock filling the frame; at most 20 tried - c3z69: The Abyss 2 of 12)
			var order = new List<KeyValuePair<Transform, Bounds>> { rocks.OrderByDescending(k => k.Value.max.y).First() };
			while (order.Count < 20 && order.Count < rocks.Count)
				order.Add(rocks.Where(k => !order.Contains(k)).OrderByDescending(k => order.Min(q => (q.Value.center - k.Value.center).magnitude)).First());
			var said = new List<string>();
			var skipped = new List<string>();
			foreach (var k in order)
			{
				if (list.Count >= 4) break;
				Vector3 c = k.Value.center;
				Vector3 at = new Vector3(c.x, Mathf.Clamp((k.Value.max.y + sea) * 0.5f, sea + 0.5f, sea + 6f), c.z);
				bool clearEye, fromPit;
				Vector3 eye = RockEye(land, tp, sea, k.Value, new HashSet<Collider>(k.Key.GetComponentsInChildren<Collider>(true)), at, out clearEye, out fromPit);
				if (!clearEye) { skipped.Add(Num(c.x - e.Position.x) + " " + Num(c.z - e.Position.z)); continue; }
				string name = "rock_" + e.Name + "_" + (list.Count + 1) + "_top" + Mathf.RoundToInt(k.Value.max.y - sea);
				list.Add(new KeyValuePair<string, Vector3[]>(name, new[] { eye, at }));
				said.Add(name + " at " + Num(c.x - e.Position.x) + " " + Num(c.z - e.Position.z) + " (" + Num(k.Value.size.x) + " x " + Num(k.Value.size.z) + " m, " + Num(k.Value.min.y - sea) + ".." + Num(k.Value.max.y - sea) + " m over the sea" + (fromPit ? ", from across the pit's own water" : "") + ")");
			}
			Log("  rock pictures: " + rocks.Count + " " + piece + " in the waterline on " + e.Name + "; " + (said.Count > 0 ? string.Join("; ", said.ToArray()) : "none with a clear eye") +
				(skipped.Count > 0 ? "; no clear eye for the rock at " + string.Join(", ", skipped.ToArray()) : ""));
			return list;
		}

		/// <summary>The eye for a stood rock's picture (RockShots): over open water out the way the water is most open round it
		/// (8-16 m, farther for a big rock: 1-2.5 times as its width is 5-12.5 m), 4.5-12 m up looking down at it, turned up
		/// to 80 degrees either way; clear when nothing is within 1.5 m, the rock itself is the first thing met on the looks at
		/// its middle and its top, and nothing else lies in the near part of the view (looks 12 and 24 degrees aside and 8
		/// up and down: nothing in their first 85%, the land not in their first 60%), each look with back faces on - an eye
		/// inside another rock meets that rock's inside. (c3z65: the sheet's eye stood 4 m from the rock; c3z67: with the other
		/// rocks not counted, they filled it; c3z68: 4 of 8 were the fallback, two of them just rock texture.) clear false:
		/// the one with the least in the way. With none clear, a rock in a pit's waterline (The Abyss's blue hole - c3z69: 10 of its
		/// 12 rocks had no clear eye; the land all round fills every side look) is seen across the pit from its own water: 55-85% of
		/// the way to its far side (8 m of water at least, 6 m from the rock at least), 3-9 m up; clear when nothing is within 1.5 m,
		/// the rock is the first thing met on the looks at its middle and its top, and the looks 12 degrees aside and 8 up and down
		/// meet nothing placed in their first 85% and no land in their first 35% (fromPit true).</summary>
		static Vector3 RockEye(Terrain land, Vector3 tp, float sea, Bounds b, HashSet<Collider> target, Vector3 at, out bool clear, out bool fromPit)
		{
			clear = false;
			fromPit = false;
			Vector3 c = b.center, outward = Vector3.forward;
			float bestOpen = float.MinValue;
			for (int a = 0; a < 360; a += 15)
			{
				Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
				float open = 0f;
				foreach (float s in new[] { 4f, 8f, 12f, 16f })
					if (land.SampleHeight(c + d * s) + tp.y < sea - 0.3f) open += 1f;
				open -= (land.SampleHeight(c + d * 8f) + tp.y - sea) * 0.01f;
				if (open > bestOpen) { bestOpen = open; outward = d; }
			}
			float far = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) / 5f, 1f, 2.5f);
			Vector3 bestEye = Vector3.zero;
			int bestScore = int.MaxValue;
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				foreach (float up in new[] { 6f, 9f, 4.5f, 12f })
					foreach (float turn in new[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 80f, -80f })
						foreach (float d0 in new[] { 12f, 10f, 14f, 8f, 16f })
						{
							Vector3 way = Quaternion.Euler(0f, turn, 0f) * outward;
							Vector3 p = new Vector3(c.x, sea + up, c.z) + way * d0 * far;
							if (land.SampleHeight(p) + tp.y >= sea - 0.3f) continue;
							int score = Physics.OverlapSphere(p, 1.5f, ~0, QueryTriggerInteraction.Ignore).Any(x => !(x is TerrainCollider)) ? 100 : 0;
							foreach (Vector3 aim in new[] { at, new Vector3(c.x, b.max.y - 0.5f, c.z) })
							{
								Vector3 look = aim - p;
								RaycastHit first = Physics.RaycastAll(p, look.normalized, look.magnitude + 2f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).FirstOrDefault();
								if (first.collider != null && !target.Contains(first.collider)) score += first.collider is TerrainCollider ? 20 : 10;
							}
							Vector3 main = at - p;
							float dist = main.magnitude;
							Vector3 side = Vector3.Cross(Vector3.up, main).normalized;
							foreach (var tilt in new[] { new Vector2(12f, 0f), new Vector2(-12f, 0f), new Vector2(24f, 0f), new Vector2(-24f, 0f), new Vector2(0f, 8f), new Vector2(0f, -8f) })
							{
								Vector3 dir = Quaternion.AngleAxis(tilt.y, side) * Quaternion.AngleAxis(tilt.x, Vector3.up) * main.normalized;
								foreach (RaycastHit h in Physics.RaycastAll(p, dir, dist * 0.85f, ~0, QueryTriggerInteraction.Ignore))
									if (!target.Contains(h.collider) && (!(h.collider is TerrainCollider) || h.distance < dist * 0.6f)) { score += 3; break; }
							}
							if (score < bestScore) { bestScore = score; bestEye = p; }
							if (score == 0) { clear = true; return p; }
						}
				// (a pit: across it from its own water)
				for (int a2 = 0; a2 < 360; a2 += 15)
				{
					Vector3 d = Quaternion.Euler(0f, a2, 0f) * Vector3.forward;
					float span = 0f;
					bool seen = false;
					for (float s = 2f; s <= 40f; s += 1f)
					{
						if (land.SampleHeight(c + d * s) + tp.y < sea - 0.3f) { span = s; seen = true; }
						else if (seen || s > 6f) break;
					}
					if (span < 8f) continue;
					foreach (float up in new[] { 3f, 4.5f, 6f, 9f })
						foreach (float fr in new[] { 0.7f, 0.85f, 0.55f })
						{
							Vector3 p = new Vector3(c.x, sea + up, c.z) + d * (span * fr);
							if (new Vector2(p.x - c.x, p.z - c.z).magnitude < 6f || land.SampleHeight(p) + tp.y >= sea - 0.3f) continue;
							if (Physics.OverlapSphere(p, 1.5f, ~0, QueryTriggerInteraction.Ignore).Any(x => !(x is TerrainCollider))) continue;
							bool ok = true;
							foreach (Vector3 aim in new[] { at, new Vector3(c.x, b.max.y - 0.5f, c.z) })
							{
								Vector3 look = aim - p;
								RaycastHit first = Physics.RaycastAll(p, look.normalized, look.magnitude + 2f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).FirstOrDefault();
								if (first.collider == null || !target.Contains(first.collider)) { ok = false; break; }
							}
							if (!ok) continue;
							Vector3 main = at - p;
							float dist = main.magnitude;
							Vector3 side = Vector3.Cross(Vector3.up, main).normalized;
							foreach (var tilt in new[] { new Vector2(12f, 0f), new Vector2(-12f, 0f), new Vector2(0f, 8f), new Vector2(0f, -8f) })
							{
								Vector3 dir = Quaternion.AngleAxis(tilt.y, side) * Quaternion.AngleAxis(tilt.x, Vector3.up) * main.normalized;
								foreach (RaycastHit h in Physics.RaycastAll(p, dir, dist * 0.85f, ~0, QueryTriggerInteraction.Ignore))
									if (!target.Contains(h.collider) && (!(h.collider is TerrainCollider) || h.distance < dist * 0.35f)) { ok = false; break; }
								if (!ok) break;
							}
							if (ok) { clear = true; fromPit = true; return p; }
						}
				}
			}
			finally { Physics.queriesHitBackfaces = backs; }
			return bestEye != Vector3.zero ? bestEye : new Vector3(c.x, sea + 6f, c.z) + outward * 12f * far;
		}

		/// <summary>The eye for a picture of a face (or a stood rock) at 'at', seen from 'outward' of centre over the sea: the first
		/// clear one, else the one with the least in its view. ignore: colliders that don't count (the rock pictured).</summary>
		static Vector3 SheetEye(Terrain land, Vector3 tp, float sea, Vector3 centre, Vector3 outward, Vector3 at, HashSet<Collider> ignore, float[] dists)
		{
			if (dists == null) dists = new[] { 22f, 16f, 11f, 7f, 4f };
			// (the eye over open water with nothing built between it and the face - Shelter Atoll's second picture stood behind a
			// metal wall (c3z52): straight out first, then turned up to 60 degrees either way; none clear, the first over water)
			// (clear = nothing built in the view cone near the eye either: a post or pillar 1-3 m beside the eye filled a third of
			// Caisson Yard's and The Abyss's pictures (c3z59) - any collider within 12 m whose nearest point lies within 45 degrees
			// of the look, or within 1.5 m whichever way; the land across the look or beside the eye too (c3z61); none clear,
			// the wet eye with the fewest)
			// (c3z62: Caisson Yard's first picture still looked along a channel, a brown land face 3-6 m beside and across the
			// look filling the frame - the 2 m check missed it. The land in the picture is counted too: dry ground in a 60
			// degree fan between the eye and the face, the more the worse, and any rising over the eye's sight line; an eye 7 m
			// up and turns of 90 degrees and 4 m out tried after the 3.5 m ones; none clear, the eye with the least in its view)
			Vector3 eye = at;
			Vector3 bestWet = Vector3.zero;
			int bestCount = int.MaxValue;
			bool found = false;
			foreach (float up in new[] { 3.5f, 7f })
			{
				foreach (float turn in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f })
				{
					Vector3 way = Quaternion.Euler(0f, turn, 0f) * outward;
					foreach (float d in dists)
					{
						Vector3 p = centre + way * d; p.y = sea + up;
						// (over the water: the ground under the eye under the sea - c3z64b: Shelter Atoll's picture was taken 7 m over
						// grass, which passed the old test of land 1 m under the eye)
						if (land.SampleHeight(p) + tp.y >= sea - 0.3f) continue;
						Vector3 look = at - p;
						float near = Mathf.Min(12f, look.magnitude - 2f);
						int inCone = 0;
						foreach (Collider c in Physics.OverlapSphere(p, 12f, ~0, QueryTriggerInteraction.Ignore))
						{
							if (c is TerrainCollider || (ignore != null && ignore.Contains(c))) continue;
							Vector3 v = c.ClosestPointOnBounds(p) - p;
							if (v.magnitude < 1.5f || (v.magnitude < near && Vector3.Angle(v, look) < 45f)) inCone += 1000;
						}
						foreach (RaycastHit h in Physics.RaycastAll(p, look.normalized, look.magnitude - 1.5f, ~0, QueryTriggerInteraction.Ignore))
							if ((ignore == null || !ignore.Contains(h.collider)) && (!(h.collider is TerrainCollider) || h.distance < look.magnitude - 4f)) { inCone += 1000; break; }
						// (nor the land itself: c3z61's Caisson Yard picture stood pressed against a channel's other wall - the ground
						// across the look before the face, or a wall within 2 m of the eye, makes it not clear)
						foreach (Vector3 side in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
							if (land.SampleHeight(p + side * 2f) + tp.y >= p.y - 0.5f) { inCone += 500; break; }
						Vector3 flat = new Vector3(look.x, 0f, look.z);
						float span = flat.magnitude - 3f;
						flat.Normalize();
						for (float a = -30f; a <= 30f; a += 10f)
							for (float s = 1.5f; s <= Mathf.Min(span, 20f); s += 1.5f)
							{
								Vector3 q = p + Quaternion.Euler(0f, a, 0f) * flat * s;
								float g = land.SampleHeight(q) + tp.y;
								if (g >= p.y + (at.y - p.y) * s / Mathf.Max(1f, span + 3f) - 0.5f) inCone += 5;
								else if (g >= sea + 0.3f) inCone += 1;
							}
						if (inCone < bestCount) { bestCount = inCone; bestWet = p; }
						if (inCone > 0) continue;
						eye = p; found = true; break;
					}
					if (found) break;
				}
				if (found) break;
			}
			if (!found) eye = bestWet != Vector3.zero ? bestWet : centre + outward * Mathf.Min(7f, dists[0]) + Vector3.up * (sea + 3.5f - centre.y);
			return eye;
		}

		/// <summary>A piece a stood cliff rock may stand in front of (StandCliffRocks): a log, a natural rock, a bush.</summary>
		static bool RockStandsBefore(Collider c)
		{
			string n = PieceName(c);
			return Regex.IsMatch(n, @"^Log(_|\s|$)|Driftwood", RegexOptions.IgnoreCase) || NaturalRock(n) || Tuft.IsMatch(n);
		}

		/// <summary>The cliffs step's rocks=: a rock piece stood in the waterline at each bare face the talus could not reach
		/// (a pit's or a shaft's wall, a channel, one held back by a piece - not at the terrain's edge), half in the wall, on its
		/// longest side and its wide side along the wall, from 3 m under the sea (or the face's foot when that is higher) up to the
		/// face's top (stretched up along the world's up, at most 3 times its height); one rock for the faces within 2 m of it, the
		/// rocks overlapping each other, none where it would meet a placed piece (blocked: what it met, by name). Raft then draws a
		/// rock at the waterline in front of the stretched wall (c3z61: The Abyss's blue hole; c3z62: the rocks lay on their side
		/// as the placer turns them, 2.3 m tall, all under the water, and the ones beside a rock already stood were left out as
		/// "meeting a piece"). How many were stood.</summary>
		static int StandCliffRocks(string rocks, List<SheetFace> todo, float sea, out int covered, Dictionary<string, int> blocked)
		{
			covered = 0;
			rockFillNote = "";
			if (frames.Count == 0) return 0;
			Frame tf = TopFrame;
			// (rocks=A|B|C: the pieces taken in turn by place; each rock turned up to 25 degrees off the wall's line, leaned up to
			// 8 degrees, 0.8-1.2 times its size and reaching 0-1.2 m over the face's top, from its place - c3z64b: Old Mine Islet's
			// channel lined with one BigBoulder4_Low after another, all the same size and turn, read as a palisade)
			string[] pieces = rocks.Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
			if (pieces.Length == 0) return 0;
			var stood = new List<Vector3>();
			var own = new HashSet<Collider>();
			// (the ziplines' ropes, near end to far end (zip.to): a rock stretched up under one met the rope - c3z64: Caisson
			// Yard's cyzip, its far end raised 3 m over a stood rock. A rock by a rope stays 2.5 m under it, else is left out)
			var ropes = new List<KeyValuePair<Vector3, Vector3>>();
			Vector3 landAt = terraineditor.terrain != null ? terraineditor.terrain.transform.position : Vector3.zero;
			foreach (EditorGameObject z in UnityEngine.Object.FindObjectsOfType<EditorGameObject>())
			{
				Vector3 to;
				if (z != null && z.Props != null && ZiplineEnds.TryParse(ObjectProps.Get(z.Props, ZiplineEnds.ZipTo), out to)) ropes.Add(new KeyValuePair<Vector3, Vector3>(z.transform.position, landAt + to));
			}
			System.Func<Vector3, float> ropeCap = q =>
			{
				float cap = float.PositiveInfinity;
				foreach (var rp in ropes)
				{
					Vector2 a = new Vector2(rp.Key.x, rp.Key.z), ab = new Vector2(rp.Value.x, rp.Value.z) - a;
					float u = ab.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector2.Dot(new Vector2(q.x, q.z) - a, ab) / ab.sqrMagnitude) : 0f;
					if ((a + ab * u - new Vector2(q.x, q.z)).magnitude < 3.5f) cap = Mathf.Min(cap, Mathf.Lerp(rp.Key.y, rp.Value.y, u) - 2.5f);
				}
				return cap;
			};
			Terrain land = terraineditor.terrain;
			float landY = land != null ? land.transform.position.y : 0f;
			// (where a rock may stand: nothing placed in its box (a log, a natural rock or a bush it may stand in front of; the
			// rocks this step stood), 1 m under a zipline's rope at least, and water in front of it - c3z65b: Shelter Atoll's rocks
			// stood on a sand shelf over a hollow, high and dry. Null when it may, else what is in the way)
			System.Func<Vector3, Quaternion, float, Vector3, string> standIn = (q, turn, half, outward) =>
			{
				Collider h = Physics.OverlapBox(new Vector3(q.x, sea + 0.5f, q.z), new Vector3(half, 3.4f, 1.1f), turn, ~0, QueryTriggerInteraction.Ignore)
					.FirstOrDefault(c => !(c is TerrainCollider) && !own.Contains(c) && !RockStandsBefore(c));
				if (h != null) return PieceName(h);
				if (ropeCap(q) < sea + 1f) return "a zipline's rope";
				if (land != null && land.SampleHeight(q + outward * 1.5f) + landY > sea - 0.5f) return "dry ground in front";
				return null;
			};
			// (how high a short rock may reach at q: 1.2 m under a zipline's rope and 0.3 m under what is placed over the water there -
			// a floor, a fence, a deck; never at a dug tunnel's mouth, which stays open; with water in front unless 'front' is false.
			// c3z68: Caisson Yard's pit wall bare under its zipline's rope, a rope fence and a floor - a full rock would meet them)
			System.Func<Vector3, Quaternion, float, Vector3, bool, float> shortRoom = (q, turn, half, outward, front) =>
			{
				if (front && land != null && land.SampleHeight(q + outward * 1.5f) + landY > sea - 0.5f) return float.NegativeInfinity;
				float top = ropeCap(q) + 1.3f;
				foreach (Collider c in Physics.OverlapBox(new Vector3(q.x, sea + 0.5f, q.z), new Vector3(half, 3.4f, 1.1f), turn, ~0, QueryTriggerInteraction.Ignore))
				{
					if (c is TerrainCollider || own.Contains(c) || RockStandsBefore(c)) continue;
					if (PieceName(c).IndexOf("Tunnel", System.StringComparison.OrdinalIgnoreCase) >= 0) return float.NegativeInfinity;
					top = Mathf.Min(top, c.bounds.min.y - 0.3f);
				}
				return top;
			};
			// (one rock shaped at its place: on its longest side, its wide side along the wall, turned and leaned, sized,
			// stretched up to 'want' (at most 3 times) and wider by 'widen' of that, its middle at c, its foot at 'low')
			System.Action<EditorGameObject, Vector3, Vector3, Vector3, float, float, float, float, float, float, float> shapeRock = (e, c, along, outward, low, want, spin, lean, size, widen, cap) =>
			{
				Renderer[] rr = PlacementOptions.ShapeRenderers(e.gameObject);
				if (rr.Length == 0) return;
				System.Func<Bounds> shape = () => { Bounds b = rr[0].bounds; foreach (Renderer x in rr) b.Encapsulate(x.bounds); return b; };
				// (on its longest side: the placer stands a piece with its own axes the world's, which lays the _Low boulders
				// down - 2.3 m tall; turned a quarter about the wall's line or the way out, whichever stands it tallest)
				Quaternion best = e.transform.rotation;
				float tall = shape().size.y;
				foreach (Quaternion q in new[] { Quaternion.AngleAxis(90f, along) * best, Quaternion.AngleAxis(90f, outward) * best })
				{
					e.transform.rotation = q;
					float hq = shape().size.y;
					if (hq > tall + 0.01f) { tall = hq; best = q; }
				}
				// (its wide side along the wall, its thin side out)
				e.transform.rotation = best;
				Bounds b0 = shape();
				float w0 = Mathf.Abs(along.x) * b0.size.x + Mathf.Abs(along.z) * b0.size.z;
				e.transform.rotation = Quaternion.AngleAxis(90f, Vector3.up) * best;
				Bounds b1 = shape();
				if (Mathf.Abs(along.x) * b1.size.x + Mathf.Abs(along.z) * b1.size.z <= w0) e.transform.rotation = best;
				// (each its own: turned off the wall's line, leaned, bigger or smaller)
				e.transform.rotation = Quaternion.AngleAxis(lean, along) * Quaternion.AngleAxis(spin, Vector3.up) * e.transform.rotation;
				e.transform.localScale *= size;
				// (stretched up to the face's top where that is over the rock's - Old Mine Islet's channel walls stand 4-9 m over
				// the sea, The Abyss's blue hole 17 m: along its own axis that points most up, at most 3 times)
				Bounds sb = shape();
				Transform t = e.transform;
				if (want - low > sb.size.y && sb.size.y > 0.5f)
				{
					float k = Mathf.Min(3f, (want - low) / sb.size.y);
					float ax = Mathf.Abs(t.right.y), ay = Mathf.Abs(t.up.y), az = Mathf.Abs(t.forward.y);
					int upAxis = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
					Vector3 ls = t.localScale;
					t.localScale = upAxis == 0 ? new Vector3(ls.x * k, ls.y, ls.z) : upAxis == 1 ? new Vector3(ls.x, ls.y * k, ls.z) : new Vector3(ls.x, ls.y, ls.z * k);
					// (and wider along the wall by 'widen' of that, unless it then meets a piece or a rope: c3z65's stretched rocks
					// stood like posts, one every 3-4 m; c3z67's at a third were 16 m monoliths - a quarter now)
					float wk = 1f + (k - 1f) * widen;
					if (wk > 1.05f)
					{
						float dx = upAxis == 0 ? -1f : Mathf.Abs(Vector3.Dot(t.right, along)), dy = upAxis == 1 ? -1f : Mathf.Abs(Vector3.Dot(t.up, along)), dz = upAxis == 2 ? -1f : Mathf.Abs(Vector3.Dot(t.forward, along));
						Vector3 narrow = t.localScale;
						t.localScale = dx >= dy && dx >= dz ? new Vector3(narrow.x * wk, narrow.y, narrow.z) : dy >= dz ? new Vector3(narrow.x, narrow.y * wk, narrow.z) : new Vector3(narrow.x, narrow.y, narrow.z * wk);
						Bounds wb = shape();
						t.position += new Vector3(c.x - wb.center.x, low - wb.min.y, c.z - wb.center.z);
						Physics.SyncTransforms();
						var mine = new HashSet<Collider>(e.GetComponentsInChildren<Collider>(true));
						wb = shape();
						if (Physics.OverlapBox(wb.center, wb.extents * 0.85f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
							.Any(x => !(x is TerrainCollider) && !mine.Contains(x) && !own.Contains(x) && !RockStandsBefore(x)) || ropeCap(wb.center) < wb.max.y)
							t.localScale = narrow;
					}
				}
				// (its middle at c, its foot at the low point)
				Bounds nb = shape();
				t.position += new Vector3(c.x - nb.center.x, low - nb.min.y, c.z - nb.center.z);
				Physics.SyncTransforms();
				// (a short rock: made smaller till its top is under 'cap' - under a rope or a piece over the water)
				nb = shape();
				if (nb.max.y > cap && nb.size.y > 0.1f)
				{
					t.localScale *= Mathf.Max(0.2f, (cap - low) / nb.size.y);
					nb = shape();
					t.position += new Vector3(c.x - nb.center.x, low - nb.min.y, c.z - nb.center.z);
					Physics.SyncTransforms();
				}
			};
			int notOwn = 0, filled = 0, shortN = 0;
			var stillBare = new List<string>();
			// (pass 0: a rock at each bare face no rock already covers; pass 1 (c3z67): at each face the sheet check would still
			// find bare - the faces the first pass took as covered by a rock that missed them, or blocked for a full-size rock -
			// a smaller one, 0.55-0.7 times, half as far along and not widened: The Abyss's blue hole kept 8 faces bare between
			// two rocks and a buoy, Caisson Yard's pit 3)
			for (int pass = 0; pass < 2; pass++)
				foreach (SheetFace f in todo)
				{
					Vector3 mid = (f.Hi + f.Lo) * 0.5f;
					Vector3 outward = new Vector3(f.Lo.x - f.Hi.x, 0f, f.Lo.z - f.Hi.z).normalized;
					if (outward.sqrMagnitude < 0.5f) continue;
					// (a face a rock already stood covers - its ray meets the rock, as the sheet check's does; the faces between two
					// rocks get one of their own - c3z64: Caisson Yard's 4.6 m wall bare for 2/3 of the width between two rocks)
					Vector3 from = f.Lo + outward * 0.5f; from.y = sea + Mathf.Clamp(f.Top * 0.5f, 0.25f, 3f);
					float len = (f.Lo - f.Hi).magnitude + 1f;
					if (pass == 0 ? own.Count > 0 && FaceCover(from, -outward, len, c => own.Contains(c)) != null : FaceCover(from, -outward, len, null) != null)
					{
						if (pass == 0) covered++;
						continue;
					}
					float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
					Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
					Vector3 along = turn * Vector3.right;
					var rnd = new System.Random(Mathf.RoundToInt(mid.x * 10f) * 73856093 ^ Mathf.RoundToInt(mid.z * 10f) * 19349663 ^ pass);
					string piece = pieces[rnd.Next(pieces.Length)];
					float spin = (float)(rnd.NextDouble() * 50.0 - 25.0), lean = (float)(rnd.NextDouble() * 16.0 - 8.0);
					float size = pass == 0 ? 0.8f + (float)rnd.NextDouble() * 0.4f : 0.55f + (float)rnd.NextDouble() * 0.15f;
					float want = sea + f.Top + 0.3f + (float)rnd.NextDouble() * 1.2f;
					// (up to 0.9 m along the wall from the face's middle, from its place: not evenly spaced (c3z65: The Abyss's pillar
					// fronted by a straight row of 8 rocks) - kept only where the rock still covers its own face, below; where a
					// piece is in or on the wall, along it up to 4 m either way, then 0.7 or 1.4 m out of it, still holding the face's
					// ray start - c3z65: Caisson Yard's pit wall bare 10 m between two rocks, its faces all meeting a dug tunnel, a
					// fence or a floor)
					float jit = pass == 0 ? (float)(rnd.NextDouble() * 1.8 - 0.9) : 0f;
					float half = 2.3f * size;
					string hit = null;
					Vector3 at = mid;
					bool clear = false;
					foreach (float push in new[] { 0f, 0.7f, 1.4f })
					{
						foreach (float off in pass == 0 ? new[] { 0f, 1.5f, -1.5f, 2.5f, -2.5f, 4f, -4f } : new[] { 0f, 0.8f, -0.8f, 1.6f, -1.6f })
						{
							Vector3 q = mid + along * (off + jit) + outward * push;
							string why = standIn(q, turn, half, outward);
							if (why == null) { at = q; clear = true; break; }
							if (off == 0f && push == 0f) hit = why;
						}
						if (clear) break;
					}
					// (pass 1, where no full rock may stand: a short one, its top under the rope or the piece over the water, at the face
					// or slid up to 2.5 m along the wall, or 0.7 m out - c3z68)
					float shortCap = float.PositiveInfinity;
					if (!clear && pass == 1)
						foreach (float push in new[] { 0f, 0.7f })
						{
							foreach (float off in new[] { 0f, 0.8f, -0.8f, 1.6f, -1.6f, 2.5f, -2.5f })
							{
								Vector3 q = mid + along * off + outward * push;
								float room = shortRoom(q, turn, half, outward, true);
								if (room >= sea + 0.8f) { at = q; clear = true; shortCap = room; break; }
							}
							if (clear) break;
						}
					if (!clear)
					{
						if (pass == 0) { int bn; blocked.TryGetValue(hit, out bn); blocked[hit] = bn + 1; }
						else if (stillBare.Count < 6)
						{
							Vector3 r0 = Quaternion.Euler(0f, -tf.Yaw, 0f) * new Vector3(mid.x - tf.Origin.x, 0f, mid.z - tf.Origin.y);
							stillBare.Add(hit + " at " + Num(r0.x) + " " + Num(r0.z));
						}
						continue;
					}
					Vector3 plain = at - along * jit;
					if (jit != 0f && standIn(plain, turn, half, outward) != null) plain = at;
					float low = Mathf.Max(sea - 3f, sea + f.Bot - 0.3f);
					foreach (Vector3 pt in new[] { at, plain }) want = Mathf.Min(want, ropeCap(pt));
					want = Mathf.Min(want, shortCap);
					Vector3 r = Quaternion.Euler(0f, -tf.Yaw, 0f) * new Vector3(at.x - tf.Origin.x, 0f, at.z - tf.Origin.y);
					var opt = new Dictionary<string, string> { { "h", "-3" }, { "sit", "1" }, { "centred", "1" }, { "yaw", Num(yaw - tf.Yaw) } };
					EditorGameObject e;
					Place(piece, r.x, r.z, opt, out e);
					if (e == null) continue;
					// (the rock must cover its own face as the sheet check sees it: else again turned and leaned less and without the
					// jitter, then straight at the face's middle, unwidened - c3z67: a 44 m wall in The Abyss bare between two
					// rocks the first pass had counted as covering it; then 0.6 and 1.2 m in toward the wall, leaned 8 and 12 degrees
					// to it, where nothing placed is in the way - c3z68: 15 of The Abyss's 45 rocks missed their own face. A rock no
					// try covers its face with is left as the straight try stood it)
					Quaternion rot0 = e.transform.rotation;
					Vector3 sc0 = e.transform.localScale;
					var mineAll = new HashSet<Collider>(e.GetComponentsInChildren<Collider>(true));
					bool ownFace = false;
					float bigger = Mathf.Max(size, pass == 0 ? 1f : size);
					int lastTry = -1;
					for (int tryN = 0; tryN < 5 && !ownFace; tryN++)
					{
						float v = tryN == 0 ? 1f : tryN == 1 ? 0.4f : 0f;
						Vector3 c0 = tryN == 0 ? at : plain;
						float wantN = want, capN = shortCap;
						if (tryN >= 3)
						{
							c0 = plain - outward * (tryN == 3 ? 0.6f : 1.2f);
							foreach (Collider mc in mineAll) mc.enabled = false;
							if (float.IsPositiveInfinity(shortCap))
							{
								string w = standIn(c0, turn, half, outward);
								if (w != null && w != "dry ground in front") { foreach (Collider mc in mineAll) mc.enabled = true; continue; }
							}
							else capN = Mathf.Min(shortCap, shortRoom(c0, turn, half, outward, false));
							foreach (Collider mc in mineAll) mc.enabled = true;
							if (capN < sea + 0.8f) continue;
							wantN = Mathf.Min(want, Mathf.Min(capN, ropeCap(c0)));
						}
						e.transform.rotation = rot0;
						e.transform.localScale = sc0;
						shapeRock(e, c0, along, outward, low, wantN, tryN < 3 ? spin * v : 0f, tryN < 3 ? lean * v : tryN == 3 ? -8f : -12f, tryN >= 2 ? bigger : size, pass == 0 && tryN < 2 ? 0.25f : 0f, capN);
						ownFace = FaceCover(from, -outward, len, c => mineAll.Contains(c)) != null;
						lastTry = tryN;
					}
					if (!ownFace && lastTry != 2)
					{
						e.transform.rotation = rot0;
						e.transform.localScale = sc0;
						shapeRock(e, plain, along, outward, low, want, 0f, 0f, bigger, 0f, shortCap);
					}
					if (!float.IsPositiveInfinity(shortCap)) shortN++;
					foreach (Collider c in mineAll) own.Add(c);
					stood.Add(at);
					if (!ownFace) notOwn++;
					else if (pass == 0) covered++;
					else filled++;
					if (pass == 1 && !ownFace && stillBare.Count < 6)
					{
						Vector3 r0 = Quaternion.Euler(0f, -tf.Yaw, 0f) * new Vector3(mid.x - tf.Origin.x, 0f, mid.z - tf.Origin.y);
						stillBare.Add("a rock not covering it at " + Num(r0.x) + " " + Num(r0.z));
					}
				}
			rockFillNote = (filled > 0 ? "; " + filled + " smaller ones in the gaps left" : "") + (shortN > 0 ? " (" + shortN + " kept short under a rope or a piece)" : "") + (notOwn > 0 ? "; " + notOwn + " not covering their own face" : "") +
				(stillBare.Count > 0 ? "; faces still bare: " + string.Join(", ", stillBare.ToArray()) : "");
			return stood.Count;
		}
		/// <summary>StandCliffRocks' second pass and what it could not cover, for the cliffs step's log line.</summary>
		static string rockFillNote = "";

		/// <summary>Everything a look meets, back faces too, nearest first: "name d m (back)" - what the see-through ray went
		/// through and what it saw (c3 round 3, for the cave fixes).</summary>
		static string LookHits(Vector3 eye, Vector3 look)
		{
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				RaycastHit[] hs = Physics.RaycastAll(eye, look.normalized, 60f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(8).ToArray();
				return "the worst look from " + eye.ToString("F1") + " meets " + (hs.Length == 0 ? "nothing (the sky)" : string.Join(", ", hs.Select(h => h.collider.name + " " + h.distance.ToString("F1") + " m" +
					(Vector3.Dot(h.normal, look) > 0f ? " (back)" : "") + (h.collider is TerrainCollider ? "" : " [" + h.collider.GetType().Name.Replace("Collider", "") + "]")).ToArray()));
			}
			finally { Physics.queriesHitBackfaces = backs; }
		}

		/// <summary>At DenRimGap's worst gap: the hits straight down through it from 4 m over the rim, and the land's height
		/// there - whether the ground under the rim is the island's terrain, a hill's rock or nothing (c3 round 3).</summary>
		static string RimDiagnosis(Transform den)
		{
			if (rimWorstUp <= 0f) return "no gap point";
			Vector3 top = rimWorstAt + Vector3.up * (rimWorstUp + 4f);
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				RaycastHit[] hs = Physics.RaycastAll(top, Vector3.down, rimWorstUp + 12f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(8).ToArray();
				return "the gap at " + rimWorstAt.ToString("F1") + " (in the piece " + den.InverseTransformPoint(rimWorstAt).ToString("F1") + "), the way out " + rimWorstOut.ToString("F2") +
					", straight down from " + Num(top.y) + ": " + (hs.Length == 0 ? "nothing" : string.Join(", ", hs.Select(h => h.collider.name + " at " + Num(h.point.y) + (h.normal.y < 0f ? " (underside)" : "")).ToArray()));
			}
			finally { Physics.queriesHitBackfaces = backs; }
		}

		/// <summary>Pictures a check asks for (SheetProblems' sheets, GroundProblems' cave looks), taken by GroundIslandsRoutine
		/// while the island is still there; only with the check's 'shots' word, at most 8 an island - but every sheet (all: a
		/// sheet's picture is queued whatever the count).</summary>
		static bool checkShots;
		/// <summary>CISheetIslands' rocks=: the piece whose stood rocks it pictures too (RockShots), else null.</summary>
		static string checkRockShots;
		static readonly List<KeyValuePair<string, Vector3[]>> checkShotQueue = new List<KeyValuePair<string, Vector3[]>>();
		static void QueueCheckShot(string name, Vector3 from, Vector3 at, bool all = false)
		{
			if (!checkShots || (!all && checkShotQueue.Count >= 8)) return;
			checkShotQueue.Add(new KeyValuePair<string, Vector3[]>(Regex.Replace(name, "[^A-Za-z0-9_]+", "_"), new[] { from, at }));
		}

		/// <summary>A piece's look across its thinnest side, per mesh + materials: +1 drawn only towards +axis, -1 only towards -axis, 0 both (or too little drawn).</summary>
		static readonly Dictionary<string, int> OneSidedLook = new Dictionary<string, int>();
		static Camera sideCam;
		static RenderTexture sideRt;
		static Texture2D sideTex;
		const int SideLayer = 31, SidePx = 32;

		/// <summary>How the renderer looks drawn alone from +dir and from -dir (an orthographic camera, its own layer, a 32 px
		/// picture): what isn't the background colour counts. Works for any mesh or shader (most meshes can't be read).</summary>
		static int RenderSide(Renderer r, Vector3 centre, Vector3 dir, Vector3 up, float size)
		{
			if (sideCam == null)
			{
				var go = new GameObject("CI one-sided camera");
				UnityEngine.Object.DontDestroyOnLoad(go);
				sideCam = go.AddComponent<Camera>();
				sideCam.enabled = false;
				sideCam.orthographic = true;
				sideCam.cullingMask = 1 << SideLayer;
				sideCam.clearFlags = CameraClearFlags.SolidColor;
				sideCam.backgroundColor = new Color(1f, 0f, 1f, 1f);
				sideCam.nearClipPlane = 0.01f;
				sideCam.farClipPlane = 20f;
				sideRt = new RenderTexture(SidePx, SidePx, 16, RenderTextureFormat.ARGB32);
				sideTex = new Texture2D(SidePx, SidePx, TextureFormat.RGBA32, false);
				sideCam.targetTexture = sideRt;
			}
			int layer = r.gameObject.layer;
			r.gameObject.layer = SideLayer;
			sideCam.orthographicSize = size / 2f * 1.05f;
			var seen = new int[2];
			try
			{
				for (int k = 0; k < 2; k++)
				{
					Vector3 from = k == 0 ? dir : -dir;
					sideCam.transform.position = centre + from * 8f;
					sideCam.transform.rotation = Quaternion.LookRotation(-from, up);
					sideCam.Render();
					RenderTexture old = RenderTexture.active;
					RenderTexture.active = sideRt;
					sideTex.ReadPixels(new Rect(0, 0, SidePx, SidePx), 0, 0);
					sideTex.Apply();
					RenderTexture.active = old;
					foreach (Color32 c in sideTex.GetPixels32())
						if (!(c.r > 240 && c.g < 15 && c.b > 240)) seen[k]++;
				}
			}
			finally { r.gameObject.layer = layer; }
			int most = Math.Max(seen[0], seen[1]), least = Math.Min(seen[0], seen[1]);
			if (most < SidePx * SidePx / 8 || least > most * 0.15f) return 0;
			return seen[0] > seen[1] ? 1 : -1;
		}

		/// <summary>
		/// Walls seen through from behind (IG8): each wall-like mesh (thin across a level direction, at least 1 m each other way)
		/// whose faces all look one way and whose material doesn't draw both sides, with no building piece or other solid within
		/// 0.3 m behind the middle of its open side. One line each.
		/// </summary>
		static List<string> OneSidedProblems(IslandWorldState.Entry e, out int count)
		{
			var lines = new List<string>();
			count = 0;
			Transform objects = e.Root.transform.Find("Objects");
			if (objects == null) return lines;
			Physics.SyncTransforms();
			foreach (Transform o in objects)
			{
				if (o == null || !o.gameObject.activeInHierarchy) continue;
				string n = o.name.Replace("(Clone)", "").Trim();
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Note_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_")) continue;
				if (Vegetation.IsMatch(n) || (OverlapIgnore.IsMatch(n) && !Structural.IsMatch(n))) continue;
				count++;
				foreach (Renderer r in o.GetComponentsInChildren<Renderer>(false))
				{
					if (!(r is MeshRenderer) || !r.enabled || r.name == ContentCatalog.MarkerOnly) continue;
					MeshFilter mf = r.GetComponent<MeshFilter>();
					if (mf == null || mf.sharedMesh == null) continue;
					if (r.name == CaveBacks.Name || r.transform.Find(CaveBacks.Name) != null) continue; // (its back drawn already: Selene's and cave pieces)
					Bounds lb = mf.sharedMesh.bounds;
					Vector3[] ax = { r.transform.TransformVector(new Vector3(lb.size.x, 0, 0)), r.transform.TransformVector(new Vector3(0, lb.size.y, 0)), r.transform.TransformVector(new Vector3(0, 0, lb.size.z)) };
					int t = 0;
					for (int i = 1; i < 3; i++) if (ax[i].magnitude < ax[t].magnitude) t = i;
					int a1 = (t + 1) % 3, a2 = (t + 2) % 3;
					if (ax[t].magnitude > 0.4f || ax[a1].magnitude < 1f || ax[a2].magnitude < 1f) continue;
					Vector3 dir = ax[t].magnitude > 1e-5f ? ax[t].normalized : r.transform.TransformDirection(Vector3.forward);
					if (Mathf.Abs(dir.y) > 0.5f) continue;
					string key = (r.transform.lossyScale.x * r.transform.lossyScale.y * r.transform.lossyScale.z < 0 ? "-" : "") + mf.sharedMesh.GetInstanceID() + ":" + string.Join(",", r.sharedMaterials.Select(m => m == null ? "0" : m.GetInstanceID().ToString()).ToArray());
					Vector3 centre = r.transform.TransformPoint(lb.center);
					int side;
					if (!OneSidedLook.TryGetValue(key, out side))
						OneSidedLook[key] = side = RenderSide(r, centre, dir, ax[a1].normalized, Mathf.Max(ax[a1].magnitude, ax[a2].magnitude));
					if (side == 0) continue;
					// (the open side: the one it isn't drawn towards)
					Vector3 open = -dir * side;
					float reach = (ax[t].magnitude / 2f + 0.32f) / 2f;
					Vector3 face = centre + open * reach;
					// (a box from its middle to 32 cm behind the open side - a twin turned round in its place, a lining - over the middle
					// 60% of the piece: in the frame thin / up / along)
					Quaternion rot = Quaternion.LookRotation(ax[a2].normalized, ax[a1].normalized);
					Vector3 half = new Vector3(reach, ax[a1].magnitude * 0.3f, ax[a2].magnitude * 0.3f);
					bool covered = Physics.OverlapBox(face, half, rot, ~0, QueryTriggerInteraction.Ignore).Any(c => c.enabled && !c.transform.IsChildOf(o));
					// (the piece's own colliders cover it too where they reach 3 cm past its open side - a sign board nailed onto its own
					// cabin's logs, Balboa_Shack's 'IF YOU LIVED HERE' - but not a collider on the board's own GameObject, and not the
					// board's own thickness: a lone panel's collider, on a parent or a sibling, stays inside that)
					if (!covered)
					{
						float near = ax[t].magnitude / 2f + 0.03f, far = ax[t].magnitude / 2f + 0.32f;
						Vector3 behind = centre + open * ((near + far) / 2f);
						Vector3 halfBehind = new Vector3((far - near) / 2f, half.y, half.z);
						covered = Physics.OverlapBox(behind, halfBehind, rot, ~0, QueryTriggerInteraction.Ignore).Any(c => c.enabled && c.transform.IsChildOf(o) && c.gameObject != r.gameObject);
					}
					if (covered) continue;
					lines.Add("one-sided: " + n + " at " + Num(Math.Round(face.x, 2)) + " " + Num(Math.Round(face.z, 2)) + " y=" + Num(Math.Round(face.y, 2)) + " - drawn from one side only, nothing behind its open side (facing " + Num(Math.Round(open.x, 2)) + " " + Num(Math.Round(open.z, 2)) + ")");
					break;
				}
			}
			return lines;
		}

		[ConsoleCommand(name: "CIOneSidedSelf", docs: "Dev, world (host, a test world 'CI ...'): the one-sided check (IG8) on an island made for it: a lone Apartment_Ext_BaseMidMidWall facade panel, nothing behind it, is found; a Balboa_Shack (its sign board nailed onto the shack's own logs) isn't (TEST_CATALOGUE GAP21). The island is taken away after.")]
		public static void OneSidedSelfCommand(string[] args) { StartTest(OneSidedSelfRoutine()); }

		static IEnumerator OneSidedSelfRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !CustomIslandSpawner.RaftPosition.HasValue) { Fail("one-sided self: host, in a world"); yield break; }
			const string isl = "cionesided", wall = "Apartment_Ext_BaseMidMidWall", shack = "Balboa_Shack";
			yield return PlaceableCatalog.EnsureLoaded(new[] { wall, shack });
			if (PlaceableCatalog.Get(wall) == null || PlaceableCatalog.Get(shack) == null) { Fail("one-sided self: " + wall + " or " + shack + " isn't in this Raft's catalogue"); yield break; }
			var made = new List<IslandWorldState.Entry>();
			IslandFile f = ScIsland(isl, "One-Sided Self");
			f.Objects.Clear();
			Vector3 b = ScDry(f, Vector2.zero, 1);
			// (16 m apart, on the ground: neither covers the other)
			foreach (var put in new[] { new { Name = wall, Dx = -8f }, new { Name = shack, Dx = 8f } })
			{
				Vector3 p = b + new Vector3(put.Dx, 0f, 0f);
				p.y = ScGround(f, new Vector2(p.x, p.z));
				f.Objects.Add(ScObj(put.Name, p));
			}
			f.Save(IslandSpawner.PathFor(isl));
			Vector3? spot = ScSpot(isl, 400f);
			if (!spot.HasValue) { ScRemove(made, isl); Fail("one-sided self: no open sea near the raft"); yield break; }
			yield return ScBring(isl, spot.Value, made);
			IslandWorldState.Entry e = made.LastOrDefault();
			if (e == null || e.Root == null) { ScRemove(made, isl); Fail("one-sided self: the island didn't come"); yield break; }
			int n = 0;
			List<string> lines;
			try
			{
				yield return WaitIslandSettled(e, 0.5f, 2f);
				lines = OneSidedProblems(e, out n);
			}
			finally { ScRemove(made, isl); }
			foreach (string l in lines) Log("  " + l);
			bool ok = true;
			Check(ref ok, n >= 2, "both pieces measured (" + n + " objects)");
			Check(ref ok, lines.Count(l => l.IndexOf(wall, StringComparison.Ordinal) >= 0) == 1, "the lone " + wall + ", nothing behind it, is found");
			Check(ref ok, !lines.Any(l => l.IndexOf(shack, StringComparison.Ordinal) >= 0), "the " + shack + "'s sign board, against the shack's own logs, isn't");
			if (ok) Log("PASS: one-sided self"); else Fail("one-sided self");
		}

		[ConsoleCommand(name: "CIWallTopsSelf", docs: "Dev, world (host, a test world 'CI ...'): the ground check (CIGroundWorld) on an island made for it: a stone house as the library's stonehouse macro builds it (lib_sunken.recipe: Block_Wall_Tier3 walls 2.42 m tall, a flat roof of Block_Floor_Tier3 tiles laid at 2.635 on their tops) - its roof tiles are held up by the walls, though the walls' colliders stop short of them and the rays down from the tiles' corners miss the walls at their edges (IG5: 102 such lines on 15 library islands, 11-18); a lone roof tile 4 m up, 10 m off, is still found. Taken away after.")]
		public static void WallTopsSelfCommand(string[] args) { StartTest(WallTopsSelfRoutine()); }

		static IEnumerator WallTopsSelfRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !CustomIslandSpawner.RaftPosition.HasValue) { Fail("wall tops self: host, in a world"); yield break; }
			const string isl = "ciwalltops", wall = "Block_Wall_Tier3", tile = "Block_Floor_Tier3", door = "Block_Wall_Door_Tier3";
			yield return PlaceableCatalog.EnsureLoaded(new[] { wall, tile, door });
			if (PlaceableCatalog.Get(wall) == null || PlaceableCatalog.Get(tile) == null || PlaceableCatalog.Get(door) == null) { Fail("wall tops self: " + wall + ", " + tile + " or " + door + " isn't in this Raft's catalogue"); yield break; }
			var made = new List<IslandWorldState.Entry>();
			IslandFile f = ScIsland(isl, "Wall Tops Self");
			f.Objects.Clear();
			Vector3 b = ScDry(f, Vector2.zero, 1);
			// (the house's floor at the highest ground under its four cells, as a recipe's push puts it)
			float y0 = new[] { 0.75f, 2.25f }.SelectMany(x => new[] { 0.75f, 2.25f }.Select(z => ScGround(f, new Vector2(b.x + x, b.z + z)))).Max();
			Func<string, float, float, float, float, IslandObject> put = (nm, x, z, y, yaw) =>
			{
				IslandObject o = ScObj(nm, new Vector3(b.x + x, y0 + y, b.z + z));
				o.EulerRotation = new Vector3(0f, yaw, 0f);
				return o;
			};
			// (a 2 x 2 stonehouse: floor and roof tiles, walls round it - the macro's door wall a plain wall here)
			foreach (float x in new[] { 0.75f, 2.25f })
				foreach (float z in new[] { 0.75f, 2.25f }) { f.Objects.Add(put(tile, x, z, 0f, 0f)); f.Objects.Add(put(tile, x, z, 2.635f, 0f)); }
			foreach (float c in new[] { 0.75f, 2.25f })
			{
				f.Objects.Add(put(wall, c, 0f, 0f, 0f)); f.Objects.Add(put(wall, c, 3f, 0f, 0f));
				f.Objects.Add(put(wall, 0f, c, 0f, 90f)); f.Objects.Add(put(wall, 3f, c, 0f, 90f));
			}
			// (a tower's side as Sealkeeper Spire's: a door wall across the house's floor, two plain walls stacked on it up through
			// the roof - the rays down from the upper ones slip through to the floor, IG5 11-19)
			f.Objects.Add(put(door, 0.75f, 1.5f, 0f, 0f));
			f.Objects.Add(put(wall, 0.75f, 1.5f, 2.42f, 0f)); f.Objects.Add(put(wall, 0.75f, 1.5f, 4.84f, 0f));
			// (a lone roof tile 4 m up, 10 m off: nothing under it)
			f.Objects.Add(put(tile, 12f, 1.5f, 4f, 0f));
			// (a lift 4 m up, clear of everything: a lift rides, it is never "floating")
			f.Objects.Add(put("VP_Skylift", 12f, 10f, 4f, 0f));
			f.Save(IslandSpawner.PathFor(isl));
			Vector3? spot = ScSpot(isl, 400f);
			if (!spot.HasValue) { ScRemove(made, isl); Fail("wall tops self: no open sea near the raft"); yield break; }
			yield return ScBring(isl, spot.Value, made);
			IslandWorldState.Entry e = made.LastOrDefault();
			if (e == null || e.Root == null) { ScRemove(made, isl); Fail("wall tops self: the island didn't come"); yield break; }
			int n = 0;
			bool lift = false;
			List<string> lines;
			try
			{
				yield return WaitIslandSettled(e, 0.5f, 2f);
				Physics.SyncTransforms();
				lines = GroundProblems(e, out n);
				lift = e.Root.GetComponentsInChildren<Transform>(true).Any(x => x.name.StartsWith("VP_Skylift", StringComparison.Ordinal));
			}
			finally { ScRemove(made, isl); }
			foreach (string l in lines) Log("  " + l);
			bool ok = true;
			Check(ref ok, lift && !lines.Any(l => l.IndexOf("VP_Skylift", StringComparison.Ordinal) >= 0), "the lift 4 m up is not floating (" + (lift ? "there" : "not there") + ")");
			Check(ref ok, n >= 20, "the house, the tower and the lone tile measured (" + n + " objects)");
			Check(ref ok, !lines.Any(l => l.IndexOf(wall, StringComparison.Ordinal) >= 0 || l.IndexOf(door, StringComparison.Ordinal) >= 0), "the walls stand, the ones stacked on the door wall too");
			Check(ref ok, lines.Count(l => l.IndexOf(tile, StringComparison.Ordinal) >= 0) == 1, "the roof on the walls' tops is held up; only the lone tile 4 m up is found (" + lines.Count(l => l.IndexOf(tile, StringComparison.Ordinal) >= 0) + " tile lines)");
			if (ok) Log("PASS: wall tops self"); else Fail("wall tops self");
		}

		[ConsoleCommand(name: "CIOverlapWorld", docs: "Dev, in a world: nothing on the loaded custom islands passes through anything else - no post, pillar, wall, floor or roof piece more than 6 cm inside a piece of furniture, a prop, a chest, a machine, a tree or a plant (IG7; the user 2026-10-18: posts through a piano and a pool table). Things resting on floors, roofs on their posts, things on tables don't count. Also no themed scene's prop, chest or note more than 6 cm into another of the scenes (their collider boxes as they stand turned; lines 'scene <theme>: ...'; two metal crates, gen-large-37435). Problems per island; all in Mods\\DynamicIslands\\overlap_world.txt. CIOverlapWorld [part of the island name]")]
		public static void OverlapWorldCommand(string[] args) { WorldCheck(args, OverlapCheck); }

		[ConsoleCommand(name: "CIOverlapIslands", docs: "Dev, world (host, a test world 'CI ...'): saved islands brought next to the raft one by one and measured as CIOverlapWorld does, then taken away (IG7). <which>: part of the name, 'plans' (every island of every plan), 'gen' or * (all but the test islands, kept copies and randomizer extras). Results in Mods\\DynamicIslands\\overlap_islands.txt. CIOverlapIslands <which> [most] [skip] [keep] [as=<tag>: the results copied to overlap_islands_<tag>.txt too - the other Islands checks take it as well]")]
		public static void OverlapIslandsCommand(string[] args) { IslandsCheck(args, OverlapCheck); }

		[ConsoleCommand(name: "CIOverlapTypes", docs: "Dev, world (host, a test world 'CI ...'): brand-new islands of each map type made next to the raft by the generator as it is now, measured as CIOverlapWorld does, then taken away (IG7). Results in Mods\\DynamicIslands\\overlap_types.txt. CIOverlapTypes [types, comma-separated, or *] [islands of each, 2]")]
		public static void OverlapTypesCommand(string[] args) { TypesCheck(args, OverlapCheck); }

		/// <summary>Building pieces: what must not pass through the things below (posts, pillars, walls, floors, decks, roofs).</summary>
		static readonly Regex Structural = new Regex("Pillar|Post(?!er|card|box)|Column|Wall|Roof|Beam|Fence|Pole|Railing|Floor|Foundation|Block_|Deck|Plank|Pier|Stilt|Scaffold|Girder|Truss|Apartment_Ext", RegexOptions.IgnoreCase);
		/// <summary>Not measured either way: the land's own (rocks, ground plants, caves), what goes through things on purpose (pipes,
		/// cables, ladders, doors in their walls), flat or loose things (rugs, puddles), lights and effects.</summary>
		static readonly Regex OverlapIgnore = new Regex("Rock|Boulder|Stone|Cliff|Cave|Tunnel|Sand|Ground|Terrain|Water|Mountain|Hill|Kelp|Coral|Seaweed|Grass|Fern|Flower|Reed|Vine|Moss|Shell|Drift|Pipe|Cable|Rope|Chain|Wire|Ladder|Door|Hatch|Gate|Window|Curtain|Rug|Carpet|Decal|Puddle|Stain|Light|Lamp|Fire|Smoke|Steam|Splash|Bubble|Cobweb|Hammock|Banner|Flag|Tent(?!acle)|Awning|Canopy|Stair|Zipline|Log(?!o)|(^|_)Ice|Dangly|Antenna|Sat+eliteDisc|Dish|Scrap", RegexOptions.IgnoreCase);
		/// <summary>A thing's box this big (any side, metres) is a building or a set piece, not furniture: measured by its colliders
		/// instead, and a mesh collider this big (not a solid box) not at all.</summary>
		const float OverlapBig = 3.5f;
		/// <summary>Trees and big plants: measured by their colliders (the trunk), not by the box round the crown.</summary>
		static readonly Regex Vegetation = new Regex("Tree|Bush|Palm|Plant|Cactus|Bamboo|Monstera|Shrub|Hedge|Pine|Birch|Oak", RegexOptions.IgnoreCase);
		/// <summary>How far one solid must be inside another to count (a chair pushed against a wall, a pot sunk into a floor: not).</summary>
		const float OverlapMargin = 0.06f;

		/// <summary>A solid box of a thing: a renderer's mesh box or a collider's box, in its own frame.</summary>
		class OverlapBox { public Transform T; public Vector3 Min, Max; }

		/// <summary>A thing's boxes from its meshes; null when one is <see cref="OverlapBig"/> or more (a set piece: use its colliders), unless withBig.</summary>
		static List<OverlapBox> MeshBoxes(Transform o, bool withBig = false)
		{
			var boxes = new List<OverlapBox>();
			foreach (Renderer r in o.GetComponentsInChildren<Renderer>(false))
			{
				if (!(r is MeshRenderer) || !r.enabled || r.name == ContentCatalog.MarkerOnly) continue;
				MeshFilter mf = r.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null) continue;
				Bounds mb = mf.sharedMesh.bounds;
				if (!withBig && Big(mb.size, r.transform)) return null;
				boxes.Add(new OverlapBox { T = r.transform, Min = mb.min, Max = mb.max });
			}
			return boxes;
		}

		/// <summary>A thing's boxes from its solid colliders (a tree's trunk; a set piece's parts) - a big mesh collider left out, unless withBig.</summary>
		static List<OverlapBox> ColliderBoxes(Transform o, bool withBig = false)
		{
			var boxes = new List<OverlapBox>();
			foreach (Collider c in o.GetComponentsInChildren<Collider>(false))
			{
				if (!c.enabled || c.isTrigger) continue;
				Vector3 lc, ls;
				if (c is BoxCollider) { lc = ((BoxCollider)c).center; ls = ((BoxCollider)c).size; }
				else if (c is CapsuleCollider)
				{
					var cc = (CapsuleCollider)c;
					lc = cc.center;
					ls = Vector3.one * cc.radius * 2f;
					ls[cc.direction] = Mathf.Max(cc.height, cc.radius * 2f);
				}
				else if (c is SphereCollider) { lc = ((SphereCollider)c).center; ls = Vector3.one * ((SphereCollider)c).radius * 2f; }
				else if (c is MeshCollider && ((MeshCollider)c).sharedMesh != null)
				{
					lc = ((MeshCollider)c).sharedMesh.bounds.center; ls = ((MeshCollider)c).sharedMesh.bounds.size;
					if (!withBig && Big(ls, c.transform)) continue;
				}
				else continue;
				boxes.Add(new OverlapBox { T = c.transform, Min = lc - ls / 2f, Max = lc + ls / 2f });
			}
			return boxes;
		}

		static bool Big(Vector3 size, Transform t)
		{
			Vector3 s = t.lossyScale;
			return Mathf.Max(Mathf.Abs(size.x * s.x), Mathf.Abs(size.y * s.y), Mathf.Abs(size.z * s.z)) >= OverlapBig;
		}

		/// <summary>
		/// What passes through what on a spawned island (IG7): the middle of each building piece's material - rays across its
		/// thinnest side against its own colliders, half-way between where they go in and come out (a post's axis, a wall's or a
		/// floor's middle, a roof slab's middle; nothing in a doorway) - and every point of it more than <see cref="OverlapMargin"/>
		/// inside a thing's solid box (furniture, props, chests, machines; a tree's trunk). One line per pair, the deepest point.
		/// </summary>
		/// <summary>Pieces a thing may stand on top of (the overlap check, IG7).</summary>
		static readonly Regex OnAPost = new Regex("Pillar|Post|Pole|Column", RegexOptions.IgnoreCase);

		static List<string> OverlapProblems(IslandWorldState.Entry e, out int count)
		{
			List<string> lines = PieceOverlaps(e, out count);
			Transform objects = e.Root.transform.Find("Objects");
			if (objects != null) lines.AddRange(SceneOverlaps(objects, e.Root.transform));
			return lines;
		}

		static List<string> PieceOverlaps(IslandWorldState.Entry e, out int count)
		{
			var lines = new List<string>();
			count = 0;
			Transform objects = e.Root.transform.Find("Objects");
			if (objects == null) return lines;
			Transform frame = e.Root.transform;
			Physics.SyncTransforms();
			var pieces = new List<Transform>();
			var things = new List<KeyValuePair<Transform, List<OverlapBox>>>();
			var thingBox = new List<Bounds>();
			foreach (Transform o in objects)
			{
				if (o == null || !o.gameObject.activeInHierarchy) continue;
				string n = o.name.Replace("(Clone)", "").Trim();
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Note_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_") || OverlapIgnore.IsMatch(n)) continue;
				if (RaftProps.Get(n) != null && RaftProps.Get(n).IsCave) continue;
				count++;
				if (Structural.IsMatch(n)) { pieces.Add(o); continue; }
				List<OverlapBox> boxes = Vegetation.IsMatch(n) ? null : MeshBoxes(o);
				if (boxes == null) boxes = ColliderBoxes(o);
				// (shrunk by the margin, in each box's own scale; too thin to hold a margin: no solid to pass through)
				var solid = new List<OverlapBox>();
				foreach (OverlapBox b in boxes)
				{
					Vector3 s = b.T.lossyScale, m = new Vector3(OverlapMargin / Mathf.Max(1e-4f, Mathf.Abs(s.x)), OverlapMargin / Mathf.Max(1e-4f, Mathf.Abs(s.y)), OverlapMargin / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
					Vector3 lo = b.Min + m, hi = b.Max - m;
					if (lo.x < hi.x && lo.y < hi.y && lo.z < hi.z) solid.Add(new OverlapBox { T = b.T, Min = lo, Max = hi });
				}
				if (solid.Count == 0) continue;
				Bounds w = new Bounds(solid[0].T.TransformPoint((solid[0].Min + solid[0].Max) / 2f), Vector3.zero);
				foreach (OverlapBox b in solid)
					for (int k = 0; k < 8; k++)
						w.Encapsulate(b.T.TransformPoint(new Vector3((k & 1) == 0 ? b.Min.x : b.Max.x, (k & 2) == 0 ? b.Min.y : b.Max.y, (k & 4) == 0 ? b.Min.z : b.Max.z)));
				things.Add(new KeyValuePair<Transform, List<OverlapBox>>(o, solid));
				thingBox.Add(w);
			}
			if (things.Count == 0) return lines;
			foreach (Transform piece in pieces)
			{
				string pn = piece.name.Replace("(Clone)", "").Trim();
				Collider[] own = piece.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger).ToArray();
				var deepest = new Dictionary<int, float>();
				var at = new Dictionary<int, Vector3>();
				foreach (Renderer r in piece.GetComponentsInChildren<Renderer>(false))
				{
					if (!(r is MeshRenderer) || !r.enabled || r.name == ContentCatalog.MarkerOnly) continue;
					MeshFilter mf = r.GetComponent<MeshFilter>();
					if (mf == null || mf.sharedMesh == null) continue;
					Bounds lb = mf.sharedMesh.bounds;
					Bounds rb = r.bounds;
					rb.Expand(0.1f);
					List<int> near = Enumerable.Range(0, things.Count).Where(i => thingBox[i].Intersects(rb) && things[i].Key != piece).ToList();
					if (near.Count == 0) continue;
					// (its box's sides in the world; the thinnest one is crossed by the rays)
					Vector3[] ax = { r.transform.TransformVector(new Vector3(lb.size.x, 0, 0)), r.transform.TransformVector(new Vector3(0, lb.size.y, 0)), r.transform.TransformVector(new Vector3(0, 0, lb.size.z)) };
					int t = 0;
					for (int i = 1; i < 3; i++) if (ax[i].magnitude < ax[t].magnitude) t = i;
					int a1 = (t + 1) % 3, a2 = (t + 2) % 3;
					int n1 = Mathf.Clamp(Mathf.CeilToInt(ax[a1].magnitude / 0.25f), 1, 40), n2 = Mathf.Clamp(Mathf.CeilToInt(ax[a2].magnitude / 0.25f), 1, 40);
					Vector3 centre = r.transform.TransformPoint(lb.center), dir = ax[t].magnitude > 1e-4f ? ax[t].normalized : Vector3.up;
					float thick = ax[t].magnitude;
					for (int i = 0; i < n1; i++)
						for (int j = 0; j < n2; j++)
						{
							Vector3 q = centre + ax[a1] * ((i + 0.5f) / n1 - 0.5f) + ax[a2] * ((j + 0.5f) / n2 - 0.5f);
							Vector3 p = q;
							if (own.Length > 0)
							{
								// (in and out of its own colliders across the thin side: the middle of the material, or nothing - a doorway)
								float inD = float.MaxValue, outD = float.MaxValue;
								RaycastHit h;
								Ray fwd = new Ray(q - dir * (thick / 2f + 0.2f), dir), back = new Ray(q + dir * (thick / 2f + 0.2f), -dir);
								foreach (Collider c in own)
								{
									if (c.Raycast(fwd, out h, thick + 0.4f) && h.distance < inD) inD = h.distance;
									if (c.Raycast(back, out h, thick + 0.4f) && h.distance < outD) outD = h.distance;
								}
								if (inD == float.MaxValue || outD == float.MaxValue) continue;
								Vector3 pin = fwd.GetPoint(inD), pout = back.GetPoint(outD);
								if (Vector3.Dot(pout - pin, dir) < 0f) continue;
								p = (pin + pout) / 2f;
							}
							foreach (int k in near)
							{
								if (!thingBox[k].Contains(p)) continue;
								foreach (OverlapBox b in things[k].Value)
								{
									Vector3 l = b.T.InverseTransformPoint(p), s = b.T.lossyScale;
									if (l.x <= b.Min.x || l.x >= b.Max.x || l.y <= b.Min.y || l.y >= b.Max.y || l.z <= b.Min.z || l.z >= b.Max.z) continue;
									float d = OverlapMargin + Mathf.Min(Mathf.Min((l.x - b.Min.x) * Mathf.Abs(s.x), (b.Max.x - l.x) * Mathf.Abs(s.x)),
										Mathf.Min(Mathf.Min((l.y - b.Min.y) * Mathf.Abs(s.y), (b.Max.y - l.y) * Mathf.Abs(s.y)), Mathf.Min((l.z - b.Min.z) * Mathf.Abs(s.z), (b.Max.z - l.z) * Mathf.Abs(s.z))));
									float was;
									if (!deepest.TryGetValue(k, out was) || d > was) { deepest[k] = d; at[k] = p; }
								}
							}
						}
				}
				// (a post's top in a thing that stands on it - a lever on its stand, a bell on its pole: the thing's box, not the thing, round the top)
				bool post = OnAPost.IsMatch(pn);
				float top = float.MinValue;
				if (post) foreach (Renderer r in piece.GetComponentsInChildren<Renderer>(false)) if (r.enabled) top = Mathf.Max(top, r.bounds.max.y);
				foreach (KeyValuePair<int, float> hit in deepest.OrderByDescending(x => x.Value))
				{
					if (post && at[hit.Key].y > top - 0.3f && thingBox[hit.Key].center.y > top) continue;
					Transform th = things[hit.Key].Key;
					// (in the island's own frame, as its file and the editor have it)
					Vector3 p = frame.InverseTransformPoint(at[hit.Key]), pp = frame.InverseTransformPoint(piece.position), tp = frame.InverseTransformPoint(th.position);
					lines.Add("through: " + pn + " at " + Num(Math.Round(pp.x, 2)) + " " + Num(Math.Round(pp.z, 2)) + " goes " + Num(Math.Round(hit.Value, 2)) + " m into " +
						th.name.Replace("(Clone)", "").Trim() + " at " + Num(Math.Round(tp.x, 2)) + " " + Num(Math.Round(tp.z, 2)) + " (the point " + Num(Math.Round(p.x, 2)) + " " + Num(Math.Round(p.z, 2)) + " y=" + Num(Math.Round(p.y, 2)) + ")");
				}
			}
			return lines;
		}

		/// <summary>
		/// A themed scene's pieces against each other (the generator's and the randomizer's scenes, marked with
		/// <see cref="RandomizerIslands.SceneProp"/>; chest and note too): each one's solid boxes as they stand - all its colliders,
		/// a big mesh collider's box too (the generator spaces them by their measured boxes), its meshes when it has none - and a
		/// pair more than <see cref="OverlapMargin"/> into each other, the deepest of their boxes (separating axes). Two metal
		/// crates 0.35 m into each other, gen-large-37435 2026-10-21. An island made before scenes were marked: the set pieces that
		/// are the themes' props.
		/// </summary>
		static List<string> SceneOverlaps(Transform objects, Transform frame)
		{
			var lines = new List<string>();
			var all = new List<IslandObjectRef>();
			foreach (Transform o in objects)
			{
				if (o == null || !o.gameObject.activeInHierarchy) continue;
				IslandObjectRef r = o.GetComponent<IslandObjectRef>();
				if (r != null && r.Props != null) all.Add(r);
			}
			bool marked = all.Any(r => r.Props.ContainsKey(RandomizerIslands.SceneProp));
			var themed = new HashSet<string>(RandomizerIslands.Themes.SelectMany(t => t.Anchors.Concat(t.Medium).Concat(t.Small)).Concat(new[] { "Antenna_pole" }));
			List<IslandObjectRef> scene = all.Where(r => marked ? r.Props.ContainsKey(RandomizerIslands.SceneProp)
				: r.Props.ContainsKey("set.piece") && !r.Props.ContainsKey("cave") && themed.Contains(r.ObjectName)).ToList();
			var boxes = new List<List<OverlapBox>>();
			var near = new List<Bounds>();
			foreach (IslandObjectRef r in scene)
			{
				List<OverlapBox> b = ColliderBoxes(r.transform, true);
				if (b.Count == 0) b = MeshBoxes(r.transform, true);
				boxes.Add(b);
				Bounds w = new Bounds(r.transform.position, Vector3.zero);
				foreach (OverlapBox x in b)
					for (int k = 0; k < 8; k++)
						w.Encapsulate(x.T.TransformPoint(new Vector3((k & 1) == 0 ? x.Min.x : x.Max.x, (k & 2) == 0 ? x.Min.y : x.Max.y, (k & 4) == 0 ? x.Min.z : x.Max.z)));
				near.Add(w);
			}
			for (int a = 0; a < scene.Count; a++)
				for (int b = a + 1; b < scene.Count; b++)
				{
					if (boxes[a].Count == 0 || boxes[b].Count == 0 || !near[a].Intersects(near[b])) continue;
					string na = scene[a].ObjectName, nb = scene[b].ObjectName;
					if (na != nb && (na == "Antenna_dish" || na == "Antenna_pole") && (nb == "Antenna_dish" || nb == "Antenna_pole")) continue; // (the dish hangs on its pole)
					float deepest = 0f;
					foreach (OverlapBox x in boxes[a])
						foreach (OverlapBox y in boxes[b]) deepest = Mathf.Max(deepest, BoxDepth(x, y));
					if (deepest <= OverlapMargin) continue;
					Vector3 pa = frame.InverseTransformPoint(scene[a].transform.position), pb = frame.InverseTransformPoint(scene[b].transform.position);
					string what;
					scene[a].Props.TryGetValue(RandomizerIslands.SceneProp, out what);
					lines.Add("scene" + (what != null ? " " + what : "") + ": " + na + " at " + Num(Math.Round(pa.x, 2)) + " " + Num(Math.Round(pa.z, 2)) + " goes " + Num(Math.Round(deepest, 2)) +
						" m into " + nb + " at " + Num(Math.Round(pb.x, 2)) + " " + Num(Math.Round(pb.z, 2)));
				}
			return lines;
		}

		/// <summary>How far two boxes (each in its own frame) are inside each other: the shortest way out of all the separating
		/// axes (their sides and the crossings of their edges); 0 when apart.</summary>
		static float BoxDepth(OverlapBox x, OverlapBox y)
		{
			Vector3 hx = (x.Max - x.Min) / 2f, hy = (y.Max - y.Min) / 2f;
			Vector3[] ux = { x.T.TransformVector(new Vector3(hx.x, 0f, 0f)), x.T.TransformVector(new Vector3(0f, hx.y, 0f)), x.T.TransformVector(new Vector3(0f, 0f, hx.z)) };
			Vector3[] uy = { y.T.TransformVector(new Vector3(hy.x, 0f, 0f)), y.T.TransformVector(new Vector3(0f, hy.y, 0f)), y.T.TransformVector(new Vector3(0f, 0f, hy.z)) };
			Vector3 d = y.T.TransformPoint((y.Min + y.Max) / 2f) - x.T.TransformPoint((x.Min + x.Max) / 2f);
			var axes = new List<Vector3>(ux.Concat(uy));
			foreach (Vector3 p in ux) foreach (Vector3 q in uy) axes.Add(Vector3.Cross(p, q));
			float depth = float.MaxValue;
			foreach (Vector3 axis in axes)
			{
				float len = axis.magnitude;
				if (len < 1e-6f) continue;
				Vector3 n = axis / len;
				float o = -Mathf.Abs(Vector3.Dot(d, n));
				foreach (Vector3 v in ux) o += Mathf.Abs(Vector3.Dot(v, n));
				foreach (Vector3 v in uy) o += Mathf.Abs(Vector3.Dot(v, n));
				if (o <= 0f) return 0f;
				depth = Mathf.Min(depth, o);
			}
			return depth == float.MaxValue ? 0f : depth;
		}

		/// <summary>What stands on a mount narrower than itself: a dish on a mast, an antenna on a roof - one mesh, its box wider
		/// than its foot (Balboa Remade's and Coral Observatory's dishes on their masts read "partly in the air", 2026-10-12; Raft's
		/// Utopia tower top room and its glass on their tower, 152 m over the ground beside it - a false alarm FLOAT2 checked
		/// with a picture, Utopia Remade, 2026-10-20).</summary>
		/// <summary>Rocks and stones placed as objects (not a Placeable_, not a cave or a stalagmite): measured under their whole base.</summary>
		static readonly Regex LooseRock = new Regex("^(?!Placeable_)(?!.*(Cave|Tunnel|Stalag|Wall|Floor|Block_)).*(Rock|Boulder|Stone)", RegexOptions.IgnoreCase);
		static readonly Regex OnAMount =new Regex("Sattelite|Satellite|Dish|Antenna|Tower_EndRoom", RegexOptions.IgnoreCase);
		/// <summary>What stands on one trunk: its pivot is its foot (the generator's trees and palms).</summary>
		static readonly Regex Trunk = new Regex("^(?!Placeable_)(?!.*(Ladder|Zipline|Spine|Street)).*(Tree|Palm|Bamboo|Pine|Birch)", RegexOptions.IgnoreCase);
		/// <summary>Named like a tree but not standing on a trunk foot at its pivot: a lying log (its pivot mid-log, half its
		/// thickness up) and a stump (its pivot 0.6 m over its bottom). Measured by their base as any object instead - Timberline
		/// Haul's log piles and Twin Forges' vice stump read "trunk foot floating" 0.45-1.3 m, resting where they were put, c3z34.</summary>
		static readonly Regex NotATrunk = new Regex(@"^(TreeLog_\d+|Log|Tree_Stump)$");
		static bool OnATrunk(string n) { return Trunk.IsMatch(n) && !NotATrunk.IsMatch(n); }

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
			// (what holds what: an object is on the ground only when a chain of supports reaches the land, the sea or the
			// island's rock - a pillar is not held by the floor that rests on it, a dish not by the table it floats over. Before
			// 2026-10-19 any collider beside an object counted, so pillars and their floor held each other up in the air)
			var nodes = new Dictionary<Transform, SupportNode>();
			var banners = new List<BannerEnd>();
			foreach (Transform o in objects)
			{
				if (o == null || !o.gameObject.activeInHierarchy) continue;
				string n = o.name.Replace("(Clone)", "").Trim();
				// (a tree or a palm, the Pickup_ landmark ones too, which are skipped below: its trunk's foot on the ground)
				string trunkLine = TrunkFootProblem(o, n, land, sea, mid);
				if (trunkLine != null) { lines.Add(trunkLine); if (!n.StartsWith("Pickup_")) count++; continue; }
				bool structural = ChainLink.IsMatch(n), banner = n.IndexOf("Banner", StringComparison.OrdinalIgnoreCase) >= 0;
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Note_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_") ||
					n.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0 || (Hangs.IsMatch(n) && !structural && !banner)) continue;
				// (a lift rides: its platform hangs in a deck's hole clear of the deck round it, held by its own works - Thalassa
				// Mooring's diving bell over its shaft read "floating, 24 m over what is under it", IG5 11-18)
				if (n.StartsWith("VP_Skylift", StringComparison.Ordinal) || n == "Elevator") continue;
				// (cave pieces: their rim, as CICaveRims - AU83)
				if (RaftProps.Get(n) != null && RaftProps.Get(n).IsCave)
				{
					count++;
					string at;
					float gap = DenRimGap(o, out at);
					if (gap >= 0.6f)
					{
						// (c3 round 3: what is at the gap - the hits down through it - and pictures from outside and from the den)
						lines.Add("cave rim off the ground: " + n + " - " + at + "; " + RimDiagnosis(o));
						Vector3 gapAt = rimWorstAt + Vector3.up * (rimWorstUp * 0.5f);
						QueueCheckShot("cave_" + e.Name + "_" + n + "_rim_out", rimWorstAt + rimWorstOut * 6f + Vector3.up * 1.6f, gapAt);
						QueueCheckShot("cave_" + e.Name + "_" + n + "_rim_in", new Vector3(rimMid.x, gapAt.y + 0.3f, rimMid.z), gapAt);
					}
					// (and from inside: nothing seen through its walls or roof - X7)
					Vector3 eye, look;
					if (CaveSeeThrough(o, RaftProps.Get(n), out at, out eye, out look) > 0)
					{
						lines.Add("cave seen through from inside: " + n + " - " + at + "; " + LookHits(eye, look));
						QueueCheckShot("cave_" + e.Name + "_" + n + "_look", eye, eye + look * 5f);
					}
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
				// A banner's rope: each end tied to a post, a pole, a wall or a roof that stands itself (the rope is the banner's
				// top edge; Gannet Cliff's banners hung from nothing, 2026-10-19)
				if (banner)
				{
					BannerEnd[] ends = RopeEnds(o, rs, n, where, objects);
					if (ends != null) banners.AddRange(ends);
					continue;
				}
				var node = new SupportNode { Name = n, Where = where, Box = whole, Link = structural };
				nodes[o] = node;
				Supports(o, b, whole, objects, land, sea, node);
				// Floors, decks, roofs, stairs: measured only as links of the chain (they lie on posts and walls, not on the ground)
				if (structural) continue;
				float groundMid = land != null ? land.SampleHeight(b.center) + land.transform.position.y : float.MinValue;
				// Buried whole: its top under the land (not Raft's rocks and corals, which are set into it; not what is under the sea)
				// (nor what stands in a dug passage, under the land but in its air: Stalker's Hollow's cave lanterns, IG5 11-18)
				// (nor Selene's interior pieces, built into a cave kit's tunnel under its hill like the cave pieces: Thalassa Mooring's
				// sea-gate bars read "buried" 11-21)
				// (nor what stands where the ground is cut away for a cave piece's room: SampleHeight reads the cut cell's old height,
				// the hill over the den - Marrow Key Museum's vault hatch read "buried 12.2 m" in its den, z32 11-21)
				if (!NotABase.IsMatch(n) && !CaveBacks.Wants(n) && land != null && whole.max.y < groundMid - 0.05f && whole.max.y > sea - 1f && !InDugPassage(whole) && !GroundCutAt(land, b.center) && !UnderCaveRoof(whole, groundMid))
				{ node.Reported = node.Direct = true; lines.Add("buried: " + n + " at " + where + ", its top " + Num(groundMid - whole.max.y) + " m under the ground"); continue; }
				// A zipline: each end on its own (its base reaches from one end's floor to the other's, over the dip between them -
				// the Lodestone Array's lines read "partly in the air" 3.7 m over their moat, 2026-10-20): every piece no wider than
				// 4 m (not the rope) with nothing at its foot within 0.5 m
				if (n.StartsWith("ZiplinePath"))
				{
					foreach (Renderer r in rs.Where(r => Mathf.Max(r.bounds.size.x, r.bounds.size.z) < 4f))
					{
						Bounds rb = r.bounds;
						if (rs.Any(q => q != r && Mathf.Max(q.bounds.size.x, q.bounds.size.z) < 4f && q.bounds.min.y < rb.min.y - 0.3f && Mathf.Abs(q.bounds.center.x - rb.center.x) < 2f && Mathf.Abs(q.bounds.center.z - rb.center.z) < 2f)) continue;
						float g = GapUnder(o, new Vector3(rb.center.x, rb.min.y, rb.center.z), land);
						if (g > 0.5f && rb.min.y > sea + 0.3f)
						{ node.Reported = true; lines.Add("floating: " + n + " end " + r.name + " at " + Num(rb.center.x - mid.x) + " " + Num(rb.center.z - mid.z) + " h=" + Num(rb.min.y - sea) + ", " + (g > 999f ? "nothing under it" : Num(g) + " m over what is under it")); break; }
					}
					continue;
				}
				// A rock or stone in the air: its underside measured over what is under it at nine points, not "does it touch
				// something" - a row of big rocks touching each other and a wall held each other up over the snow, their shadows
				// apart from them (Coldhold Vault, 2026-10-23)
				if (LooseRock.IsMatch(n) && b.min.y > sea + 0.3f)
				{
					int seen, high; float most, least;
					if (RockFloats(o, b, land, out seen, out high, out most, out least))
					{ node.Reported = true; lines.Add("floating rock: " + n + " at " + where + ", its underside up to " + Num(most) + " m over what is under it (" + high + " of " + seen + " points)"); continue; }
				}
				// A trunk over a drop: its foot on a pit's or a cliff's rim, the trunk hanging out over the slope - a stroke lowered
				// the ground beside it (the user, 2026-10-30: Reefmachine Atoll's palm over its valve-bank pit; the foot itself was
				// on the ground). Eight points round the foot, a trunk's width out: three or more 0.8 m over what is under them
				if (OnATrunk(n) && land != null && o.position.y > sea + 0.3f)
				{
					Vector3 foot = o.position;
					float tg = TrunkGround(foot, land);
					if (!float.IsNaN(tg)) foot.y = Mathf.Max(foot.y, tg);
					float rt = 0.6f * Mathf.Max(0.5f, o.lossyScale.x);
					int over = 0;
					float most = 0f;
					for (int k = 0; k < 8; k++)
					{
						float a = k * Mathf.PI / 4f;
						float g = GapUnder(o, new Vector3(foot.x + rt * Mathf.Cos(a), foot.y, foot.z + rt * Mathf.Sin(a)), land);
						if (g > 0.8f) { over++; most = Mathf.Max(most, g); }
					}
					if (over >= 3)
					{ node.Reported = true; lines.Add("trunk over a drop: " + n + " at " + Num(foot.x - mid.x) + " " + Num(foot.z - mid.z) + " h=" + Num(foot.y - sea) + ", " + over + " of 8 points round its foot over a drop (up to " + (most > 999f ? "nothing under it" : Num(most) + " m") + ")"); continue; }
				}
				// Floating: nothing at its base (another object, a wall it hangs on, the land) and the first thing under it more than 0.5 m down
				float gapUnder = GapUnder(o, new Vector3(b.center.x, b.min.y, b.center.z), land);
				bool touches = Touches(o, b, objects);
				bool inSea = b.min.y <= sea + 0.3f; // (a dock post, a buoy, a wreck: in the water, held by it)
				if (!touches && !inSea && gapUnder > 0.5f)
				{ node.Reported = true; lines.Add("floating: " + n + " at " + where + ", " + (gapUnder > 999f ? "nothing under it" : Num(gapUnder) + " m over what is under it")); continue; }
				// On a steep wall: a small thing (a chest, a box, a crate) held by nothing but the land, with most of its base over
				// the drop - its foot set into the slope reads "on the ground" (the Listening Mirrors' altar on its chasm wall); a
				// tall, thin leg or post (three times taller than wide) is driven into the slope, as the tower legs on Varuna Point
				if (!NotABase.IsMatch(n) && land != null && Mathf.Max(b.size.x, b.size.z) < 1.5f && b.size.y < Mathf.Max(b.size.x, b.size.z) * 3f && node.Under.Count == 0 && node.Over.Count == 0 && b.min.y > sea + 0.3f)
				{
					int over = 0;
					float most = 0f;
					for (int c = 0; c < 4; c++)
					{
						var p = new Vector3((c & 1) == 0 ? Mathf.Lerp(b.min.x, b.max.x, 0.1f) : Mathf.Lerp(b.min.x, b.max.x, 0.9f), b.min.y, (c & 2) == 0 ? Mathf.Lerp(b.min.z, b.max.z, 0.1f) : Mathf.Lerp(b.min.z, b.max.z, 0.9f));
						float g = GapUnder(o, p, land);
						if (g > 0.6f) { over++; most = Mathf.Max(most, g); }
					}
					if (over >= 2)
					{ node.Reported = true; lines.Add("on a steep wall: " + n + " at " + where + ", " + over + " of its 4 corners over the drop (up to " + (most > 999f ? "nothing under it" : Num(most) + " m") + ")"); continue; }
				}
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
				// (a wall on a floor's edge, as Raft builds: half of it over the floor, half over the drop outside - its middle
				// line is the floor's edge, where a ray down slips past the floor. Held by the face over the floor, as a railing
				// below: Brinelab Tower's and Sealkeeper Spire's walls read 2.4-15 m "in the air", IG5 11-18)
				float thinX = own ? (lmax.x - lmin.x) * Mathf.Abs(o.lossyScale.x) : 9f, thinZ = own ? (lmax.z - lmin.z) * Mathf.Abs(o.lossyScale.z) : 9f;
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
						if (own && (thinX < 0.5f || thinZ < 0.5f))
						{
							Vector3 sx = lmax - lmin, l = o.InverseTransformPoint(p);
							Vector3 f1 = l, f2 = l;
							if (thinX < 0.5f) { f1.x = lmin.x + sx.x * 0.1f; f2.x = lmax.x - sx.x * 0.1f; }
							else { f1.z = lmin.z + sx.z * 0.1f; f2.z = lmax.z - sx.z * 0.1f; }
							Vector3 w1 = o.TransformPoint(f1), w2 = o.TransformPoint(f2);
							g = Mathf.Min(GapUnder(o, new Vector3(w1.x, b.min.y, w1.z), land), GapUnder(o, new Vector3(w2.x, b.min.y, w2.z), land));
						}
						// (a railing straddling a deck's edge, as Raft's own towers: held by the face over the deck)
						if (b.size.x < 0.3f) g = Mathf.Min(GapUnder(o, new Vector3(b.min.x, p.y, p.z), land), GapUnder(o, new Vector3(b.max.x, p.y, p.z), land));
						if (b.size.z < 0.3f) g = Mathf.Min(GapUnder(o, new Vector3(p.x, p.y, b.min.z), land), GapUnder(o, new Vector3(p.x, p.y, b.max.z), land));
						g = GapToWater(g, p.y, sea);
						if (g < lowest) lowest = g;
						if (g > highest) { highest = g; worst = p; }
					}
				// (a wall stacked on a wall, as Raft builds a tower: held by the one under it all along its foot, though the rays down
				// slip through that one's collider - over a door wall's opening, or past the plain wall's open-topped mesh to the floor
				// 2.4-9.7 m down: Sealkeeper Spire's walls over its door, Highmoor Lodge's, IG5 11-19)
				if (lowest <= 0.4f && highest > 0.6f && own && (thinX < 0.5f || thinZ < 0.5f) && Structural.IsMatch(n) && OnABuiltPiece(o, lmin, lmax)) continue;
				if (lowest <= 0.4f && highest > 0.6f)
					lines.Add("partly in the air: " + n + " at " + where + ", its base up to " + (highest > 999f ? "over nothing" : Num(highest) + " m over the ground") + " (at " + Num(worst.x - mid.x) + " " + Num(worst.z - mid.z) + ")"
						+ " [base " + (own ? Num(thinX) + " x " + Num(thinZ) + " own" : Num(b.size.x) + " x " + Num(b.size.z)) + "; under it: " + WhatIsUnder(o, worst) + "; under its middle: " + WhatIsUnder(o, new Vector3(b.center.x, b.min.y, b.center.z)) + "]");
			}
			// The chain of supports: grounded = on the land, the sea or the island's rock, or on something grounded (what isn't
			// measured - a ladder, a lamp, a cave piece - counts as grounded)
			for (bool changed = true; changed;)
			{
				changed = false;
				foreach (SupportNode s in nodes.Values)
					if (!s.Grounded && (s.Direct || s.Under.Any(u => { SupportNode t; return !nodes.TryGetValue(u, out t) || t.Grounded; }) ||
						s.Over.Any(u => { SupportNode t; return !nodes.TryGetValue(u, out t) || t.Grounded; }) ||
						// (a floor or roof piece joined edge to edge to one that is held, no higher than it: a hip roof's sides on its
						// corners - Vaporhold Reservoir, 2026-10-20)
						(s.Link && nodes.Values.Any(t => t != s && t.Link && t.Grounded && t.Box.min.y <= s.Box.min.y + 0.3f && Grow(t.Box, 0.1f).Intersects(s.Box)))))
					{ s.Grounded = true; changed = true; }
			}
			foreach (SupportNode s in nodes.Values.Where(x => !x.Grounded && !x.Reported))
				lines.Add("held up by nothing that reaches the ground: " + s.Name + " at " + s.Where +
					(s.Under.Count > 0 ? " (rests on " + string.Join(", ", s.Under.Select(u => u.name.Replace("(Clone)", "").Trim()).Distinct().Take(3).ToArray()) + ", not grounded either)" : ""));
			foreach (BannerEnd be in banners)
			{
				bool held = be.ByLand || be.By.Any(u => { SupportNode t; return !nodes.TryGetValue(u, out t) || t.Grounded; });
				if (!held) lines.Add("banner rope end held by nothing: " + be.Name + " at " + be.Where + ", its " + be.Side + " end (" + (be.By.Count == 0 ? "no post or wall there" : "on " + string.Join(", ", be.By.Select(u => u.name.Replace("(Clone)", "").Trim()).Distinct().Take(3).ToArray()) + ", not grounded") + ")");
			}
			// What hangs: zipline and wire ends, hanging props, solid things that show nothing (IG9)
			lines.AddRange(HangProblems(objects, land, mid, sea));
			lines.AddRange(FlyingLeftOut(e.Name));
			return lines;
		}

		/// <summary>Floors, decks, roofs, stairs: links of the support chain, reported when the chain doesn't reach the ground.</summary>
		static readonly Regex ChainLink = new Regex("Floor|Deck|Plank|Bridge|Ramp|Stair|Roof|Scaffold", RegexOptions.IgnoreCase);

		static Bounds Grow(Bounds b, float by) { b.Expand(by * 2f); return b; }
		static readonly Regex FixedAbove = new Regex("Floor|Deck|Roof|Beam|Wall|Ceiling", RegexOptions.IgnoreCase);
		class SupportNode { public string Name, Where; public bool Direct, Grounded, Reported, Link; public Bounds Box; public HashSet<Transform> Under = new HashSet<Transform>(), Over = new HashSet<Transform>(); }
		class BannerEnd { public string Name, Where, Side; public bool ByLand; public HashSet<Transform> By = new HashSet<Transform>(); }

		/// <summary>The placed object (a child of <paramref name="objects"/>) a collider belongs to; null = not a placed object
		/// (the land, the island's rock, the sea floor).</summary>
		static Transform PlacedRoot(Transform t, Transform objects)
		{
			for (; t != null; t = t.parent) if (t.parent == objects) return t;
			return null;
		}

		/// <summary>What holds <paramref name="o"/> up: on the land, the sea or the island's own rock (<see cref="SupportNode.Direct"/>),
		/// and the placed objects at its base or at its sides that reach down at least as low as its base (a table under a box, a
		/// wall a lamp hangs on - not a floor resting on a pillar's top).</summary>
		static void Supports(Transform o, Bounds b, Bounds whole, Transform objects, Terrain land, float sea, SupportNode node)
		{
			// (fixed under a floor, roof, beam or wall above it - a rib under a deck, a ceiling lamp: held if that is held - Colossus of
			// the Strait, Coral Observatory, 2026-10-20)
			// (from 0.15 m under its top to 0.5 m over it: a lamp's mount may stop short of the ceiling's collider)
			var top = new Vector3(Mathf.Max(0.05f, whole.extents.x * 0.8f), 0.325f, Mathf.Max(0.05f, whole.extents.z * 0.8f));
			foreach (Collider c in Physics.OverlapBox(new Vector3(whole.center.x, whole.max.y + 0.175f, whole.center.z), top, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
			{
				if (c.transform.IsChildOf(o) || c.bounds.max.y < whole.max.y - 0.1f || c.bounds.min.y < whole.min.y) continue; // (its top in a thin ceiling)
				Transform r = PlacedRoot(c.transform, objects);
				if (r != null && FixedAbove.IsMatch(r.name)) node.Over.Add(r);
			}
			node.Direct = b.min.y <= sea + 0.3f;
			float reach = b.min.y + 0.3f;
			var half = new Vector3(Mathf.Max(0.05f, b.extents.x * 0.9f), 0.15f, Mathf.Max(0.05f, b.extents.z * 0.9f));
			foreach (Collider c in Physics.OverlapBox(new Vector3(b.center.x, b.min.y, b.center.z), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
			{
				if (c.transform.IsChildOf(o)) continue;
				Transform r = PlacedRoot(c.transform, objects);
				if (r == null) node.Direct = true;
				else if (c.bounds.min.y < reach) node.Under.Add(r);
			}
			foreach (Collider c in Physics.OverlapBox(b.center, b.extents + Vector3.one * 0.08f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
			{
				if (c.transform.IsChildOf(o) || c.GetComponent<Terrain>() != null || c.bounds.min.y >= reach) continue;
				Transform r = PlacedRoot(c.transform, objects);
				if (r == null) node.Direct = true; else node.Under.Add(r);
			}
			// (a floor or roof laid on the tops of walls round its edges: what shows of a wall's top within 0.35 m under its base -
			// the walls' colliders stop 0.1-0.2 m short of a storey's roof tiles, and the rays down from its corners miss the
			// walls at its edges: Biome Seven's lab roofs and The Stalker's Hollow's stone houses read "held up by nothing", IG5)
			if (node.Link) foreach (Transform r in TopsUnder(o, b, objects)) node.Under.Add(r);
			// (right under its middle and its corners: the land within 0.5 m, or the first thing there)
			for (int i = 0; i < 5; i++)
			{
				var p = i == 4 ? new Vector3(b.center.x, b.min.y, b.center.z) :
					new Vector3((i & 1) == 0 ? Mathf.Lerp(b.min.x, b.max.x, 0.15f) : Mathf.Lerp(b.min.x, b.max.x, 0.85f), b.min.y, (i & 2) == 0 ? Mathf.Lerp(b.min.z, b.max.z, 0.15f) : Mathf.Lerp(b.min.z, b.max.z, 0.85f));
				if (land != null)
				{
					float g = p.y - (land.SampleHeight(p) + land.transform.position.y);
					Vector3 lp = land.transform.position, ls = land.terrainData.size;
					if (g <= 0.5f && (g > -0.3f || (p.x >= lp.x && p.x <= lp.x + ls.x && p.z >= lp.z && p.z <= lp.z + ls.z))) { node.Direct = true; continue; }
				}
				RaycastHit best = default(RaycastHit);
				float bestGap = float.MaxValue;
				foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 0.3f, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Ignore))
					if (!h.collider.transform.IsChildOf(o) && p.y - h.point.y < bestGap) { bestGap = p.y - h.point.y; best = h; }
				if (bestGap > 0.5f) continue;
				Transform r = PlacedRoot(best.collider.transform, objects);
				if (r == null) node.Direct = true; else node.Under.Add(r);
			}
		}

		static readonly List<KeyValuePair<Transform, Bounds>> tops = new List<KeyValuePair<Transform, Bounds>>();
		static Transform topsOf;
		static int topsFrame = -1;

		/// <summary>The placed objects (children of <paramref name="objects"/>) that reach up under <paramref name="b"/>'s base
		/// from below: a shown mesh whose top is from 0.35 m under its base to 0.3 m over it, over its ground plan, and whose
		/// bottom is more than 0.3 m under its base (a wall under a roof tile - not the tile beside it).</summary>
		static List<Transform> TopsUnder(Transform o, Bounds b, Transform objects)
		{
			if (topsOf != objects || topsFrame != Time.frameCount)
			{
				tops.Clear(); topsOf = objects; topsFrame = Time.frameCount;
				foreach (Transform root in objects)
					foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false))
						if (r.enabled) tops.Add(new KeyValuePair<Transform, Bounds>(root, r.bounds));
			}
			var found = new List<Transform>();
			foreach (KeyValuePair<Transform, Bounds> t in tops)
			{
				Bounds r = t.Value;
				if (t.Key == o || found.Contains(t.Key) || t.Key.IsChildOf(o)) continue;
				if (r.max.y < b.min.y - 0.35f || r.max.y > b.min.y + 0.3f || r.min.y > b.min.y - 0.3f) continue;
				if (r.min.x > b.max.x + 0.05f || r.max.x < b.min.x - 0.05f || r.min.z > b.max.z + 0.05f || r.max.z < b.min.z - 0.05f) continue;
				found.Add(t.Key);
			}
			return found;
		}

		/// <summary>A banner's two rope ends (the ends of its top edge, along its long side) and what each is tied to: a collider
		/// within 0.35 m beside it and 0.6 m above or below. Null when its mesh can't be read.</summary>
		static BannerEnd[] RopeEnds(Transform o, Renderer[] rs, string n, string where, Transform objects)
		{
			Vector3 lmin = Vector3.one * float.MaxValue, lmax = Vector3.one * float.MinValue;
			foreach (Renderer r in rs)
			{
				MeshFilter mf = r.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name.StartsWith("Combined Mesh")) return null;
				Bounds mb = mf.sharedMesh.bounds;
				for (int c = 0; c < 8; c++)
				{
					Vector3 l = o.InverseTransformPoint(r.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f))));
					lmin = Vector3.Min(lmin, l); lmax = Vector3.Max(lmax, l);
				}
			}
			bool alongX = lmax.x - lmin.x >= lmax.z - lmin.z;
			var ends = new BannerEnd[2];
			for (int k = 0; k < 2; k++)
			{
				Vector3 l = alongX ? new Vector3(k == 0 ? lmin.x + 0.1f : lmax.x - 0.1f, lmax.y, (lmin.z + lmax.z) * 0.5f)
					: new Vector3((lmin.x + lmax.x) * 0.5f, lmax.y, k == 0 ? lmin.z + 0.1f : lmax.z - 0.1f);
				var e = new BannerEnd { Name = n, Where = where, Side = k == 0 ? "left" : "right" };
				foreach (Collider c in Physics.OverlapBox(o.TransformPoint(l), new Vector3(0.35f, 0.6f, 0.35f), o.rotation, ~0, QueryTriggerInteraction.Ignore))
				{
					if (c.transform.IsChildOf(o)) continue;
					Transform r = PlacedRoot(c.transform, objects);
					if (r == null) e.ByLand = true; else e.By.Add(r);
				}
				ends[k] = e;
			}
			return ends;
		}

		/// <summary>How far the first thing under <paramref name="foot"/> that isn't <paramref name="self"/> is (the land, another object,
		/// the sea floor); 9999 = nothing.</summary>
		[ConsoleCommand(name: "CICaveInside", docs: "Dev, editor or world: every cave piece and dug tunnel (the editor's island, or the loaded custom islands; a dug tunnel from eyes along each of its strokes, joined tunnels and cave pieces counted as inside) looked round from inside at eye height - nothing seen through: no face seen from its back (Raft draws the shells, the ground and the hill over them from one side only), no sky but out through an open end (X7). CICaveInside [part of the name: in a world, a saved island of that name not loaded here is brought next to the raft first (host)] [shots: a picture of each piece's worst look, shot_caveinside_<n>.png]")]
		public static void CaveInsideCommand(string[] args)
		{
			// (an island's name may have spaces - "Shelter Atoll shots": the last word shots, the rest the name; it took the
			// first word alone and no pictures until 11-04)
			List<string> words = (args ?? new string[0]).ToList();
			bool shots = words.Count > 1 && words[words.Count - 1] == "shots";
			if (shots) words.RemoveAt(words.Count - 1);
			string only = string.Join(" ", words.ToArray());
			if (only == "*") only = "";
			StartTest(CaveInsideRoutine(only, shots));
		}

		static IEnumerator CaveInsideRoutine(string only, bool shots)
		{
			var caves = new List<KeyValuePair<string, Transform>>(); // (island: piece)
			// (a saved island named that and not loaded here: brought next to the raft first, as CICaveIslands does - it was found
			// only after a CIPlay had loaded it until 11-05)
			if (!DynamicIslands.InEditor() && only.Length > 0 && LoadSceneManager.IsGameSceneLoaded
				&& !IslandWorldState.Islands.Any(x => x.Root != null && !x.Loading && x.Name.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0))
			{
				var kept = new System.Text.RegularExpressions.Regex("_[0-9a-f]{12}$");
				List<string> saved = IslandSpawner.ListSavedIslands().Where(s => !kept.IsMatch(s)).ToList();
				string file = saved.FirstOrDefault(s => s.Equals(only, StringComparison.OrdinalIgnoreCase))
					?? saved.FirstOrDefault(s => s.Replace("_", " ").Equals(only, StringComparison.OrdinalIgnoreCase))
					?? saved.FirstOrDefault(s => s.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0);
				if (file != null)
				{
					if (!Raft_Network.IsHost) { Fail("cave inside: '" + file + "' is not loaded here (the host brings it: CICaveInside " + only + " there)"); yield break; }
					var left = IslandWorldState.Islands.Where(x => string.Equals(x.Name, file, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
					if (left.Count > 0) { IslandWorldState.RemoveIds(left, true); IslandCache.Forget(); yield return new WaitForSeconds(1f); }
					Vector3? spot = ScSpot(file, 400f);
					if (!spot.HasValue) { Fail("cave inside: no open sea near the raft for '" + file + "'"); yield break; }
					var made = new List<IslandWorldState.Entry>();
					yield return ScBring(file, spot.Value, made);
					IslandWorldState.Entry be = made.LastOrDefault();
					if (be == null || be.Root == null) { Fail("cave inside: '" + file + "' didn't come"); yield break; }
					Log("  '" + file + "' was not loaded: brought next to the raft at " + spot.Value.ToString("F0"));
					yield return new WaitForSeconds(4f);
					// (its dug tunnels' linings built, as CICaveIslands waits)
					var bd = be.Root.GetComponentsInChildren<DigTunnel>().ToList();
					for (float w = 0f; w < 10f && bd.Any(x => x.Triangles == 0 || x.Land() == null); w += 0.5f) yield return new WaitForSeconds(0.5f);
				}
			}
			if (DynamicIslands.InEditor())
			{
				int dug, cut = CaveGround.EditorNow(terraineditor.terrain, out dug); // (the ground out of the rooms now, not in a second)
				Log("  the ground taken out of the caves' rooms: " + dug + " points let down, " + cut + " cells cut");
				foreach (EditorGameObject e in PlacedEditorObjects())
					if (e != null && RaftProps.Get(e.GameObjectName ?? "") != null && RaftProps.Get(e.GameObjectName).IsCave) caves.Add(new KeyValuePair<string, Transform>("editor", e.transform));
			}
			else
				foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading).ToList())
					foreach (Transform t in e.Root.GetComponentsInChildren<Transform>().Where(t => RaftProps.Get(t.name.Replace("(Clone)", "").Trim()) != null && RaftProps.Get(t.name.Replace("(Clone)", "").Trim()).IsCave))
						caves.Add(new KeyValuePair<string, Transform>(e.Name, t));
			caves = caves.Where(c => only.Length == 0 || c.Key.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0 || c.Value.name.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			// (and the dug tunnels - G3, 11-04: the Dig brush's caves, alone or joined to others and to cave pieces)
			var digs = new List<KeyValuePair<string, DigTunnel>>();
			if (DynamicIslands.InEditor())
			{
				foreach (EditorGameObject e in PlacedEditorObjects())
					if (e != null && e.GetComponent<DigTunnel>() != null && e.gameObject.activeInHierarchy) digs.Add(new KeyValuePair<string, DigTunnel>("editor", e.GetComponent<DigTunnel>()));
			}
			else
				foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading).ToList())
					foreach (DigTunnel d in e.Root.GetComponentsInChildren<DigTunnel>()) digs.Add(new KeyValuePair<string, DigTunnel>(e.Name, d));
			digs = digs.Where(c => only.Length == 0 || c.Key.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0 || "DigTunnel".IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
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
				// (a den's front wall under the rock: it stood out of a low hill's top as a dark board - 11-05)
				int open = DynamicIslands.InEditor() ? -1 : CaveTool.PlugInOpen(c.Value);
				if (open >= 0) Log("    its front wall: " + (open == 0 ? "under the rock" : open + " points of it with open sky over them"));
				if (open > 0 && seen == 0) bad++;
				if (shots && eye != Vector3.zero) yield return CameraShot(eye, eye + look * 5f, "caveinside_" + n);
				yield return null;
			}
			int dn = 0;
			if (digs.Count > 0 && DynamicIslands.InEditor()) DigTunnel.EditorNow(terraineditor.terrain);
			foreach (var c in digs)
			{
				if (c.Value == null) continue;
				n++; dn++;
				string detail;
				Vector3 eye, look;
				int seen = DugSeeThrough(c.Value, digs.Where(x => x.Key == c.Key).Select(x => x.Value).ToList(), out detail, out eye, out look);
				Log("  " + n + " '" + c.Key + "' DigTunnel at " + c.Value.transform.position.ToString("F0") + ": " + detail);
				if (seen > 0) bad++;
				// (and its rim: no loose bits of it standing in the grass apart from the mouth - 11-06)
				string rimDetail;
				int loose = LooseRim(c.Value, out rimDetail);
				Log("    its rim: " + rimDetail);
				if (loose > 0 && seen == 0) bad++;
				if (shots && eye != Vector3.zero) yield return CameraShot(eye, eye + look * 5f, "caveinside_" + n);
				yield return null;
			}
			if (n == 0) Fail("cave inside: no cave pieces or dug tunnels" + (only.Length > 0 ? " named *" + only + "*" : ""));
			else if (bad > 0) Fail("cave inside: " + bad + " of " + n + " cave pieces and dug tunnels seen through from inside, or with loose rim rock (above)");
			else Log("PASS: cave inside - " + (n - dn) + " cave pieces, " + dn + " dug tunnels, nothing seen through from inside");
		}

		[ConsoleCommand(name: "CIShotFrom", docs: "Dev, editor or world: a picture through Raft's camera from a point, and what the looks round from it meet - for checking a see-through CICaveInside found (X7). CIShotFrom <x> <y> <z> <yaw> <pitch> [name]: shot_from_<name>.png; logs the first things hit (front or back, metres) every 45 degrees round at that pitch, and straight up and down")]
		public static void ShotFromCommand(string[] args)
		{
			var v = new float[5];
			for (int i = 0; i < 5; i++)
				if (args == null || args.Length <= i || !float.TryParse(args[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) { Fail("shot from: CIShotFrom <x> <y> <z> <yaw> <pitch> [name]"); return; }
			StartTest(ShotFromRoutine(new Vector3(v[0], v[1], v[2]), v[3], v[4], args.Length > 5 ? args[5] : "1"));
		}

		[ConsoleCommand(name: "CISeleneBacks", docs: "Dev, editor: Selene's room pieces (wall, entrance, ceiling, floor, door frame, large door, tunnel) placed get their backs - an outer wall or a ceiling seen from outside no longer shows the room through it - and a radio tower wall (drawn from both sides already) gets none (X7)")]
		public static void SeleneBacksCommand()
		{
			StartTest(SeleneBacksRoutine());
		}

		static IEnumerator SeleneBacksRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 2f, 500f);
			string[] selene = { "TP_Selene_Int_Room_Wall", "TP_Selene_Int_Room_Entrance", "TP_Selene_Int_Room_Ceiling", "TP_Selene_Int_Room_Floor_Plain", "TP_Selene_Int_DoorFrame", "TP_Selene_Int_DoorLargeL", "TP_Selene_Int_Tunnel_Straight" };
			yield return PlaceableCatalog.EnsureLoaded(selene.Concat(new[] { "RT_Wall1" })); // (Selene's pieces come from its scene, loaded when first wanted)
			var made = new List<EditorGameObject>();
			for (int i = 0; i < selene.Length; i++)
			{
				EditorGameObject ego = PlaceForTest(selene[i], c0 + new Vector3(i * 8f, 0f, 0f), placed);
				if (ego == null) { Check(ref ok, false, selene[i] + ": could not be placed"); continue; }
				made.Add(ego);
				yield return null;
				int backs = ego.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name == CaveBacks.Name && r.enabled);
				Check(ref ok, CaveBacks.Has(ego.transform) && backs > 0, selene[i] + ": drawn from both sides (" + backs + " backs)");
			}
			EditorGameObject rt = PlaceForTest("RT_Wall1", c0 + new Vector3(0f, 0f, 10f), placed);
			if (rt != null)
			{
				made.Add(rt);
				Check(ref ok, !rt.GetComponentsInChildren<Transform>(true).Any(t => t.name == CaveBacks.Name), "RT_Wall1: no backs added (Raft draws it from both sides)");
			}
			foreach (EditorGameObject ego in made) UnityEngine.Object.Destroy(ego.gameObject);
			if (ok) Log("PASS: Selene pieces drawn from both sides"); else Fail("Selene pieces drawn from both sides");
		}

		[ConsoleCommand(name: "CIRoomLooks", docs: "Dev, in game: a room of a custom island seen from inside (X7) - from eye height over the floor at <x> <z> (m from its middle, the island's own axes) and four places 1.5 m round it, 81 looks each: those that meet nothing (the sky) or the back of a face first (seen through). Pictures to four corners and toward the highest of those looks (shot_room_<name>_<n>.png). CIRoomLooks <island> <x> <z> <floor h> [name]")]
		public static void RoomLooksCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			var v = new float[3];
			for (int i = 0; i < 3; i++)
				if (args.Length <= i + 1 || !float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) { Fail("room looks: CIRoomLooks <island> <x> <z> <floor h> [name]"); return; }
			StartTest(RoomLooksRoutine(e, v[0], v[1], v[2], args.Length > 4 ? args[4] : args[0]));
		}

		static IEnumerator RoomLooksRoutine(IslandWorldState.Entry e, float x, float z, float h, string name)
		{
			float turnY = e.Root.transform.eulerAngles.y;
			Quaternion turn = Quaternion.Euler(0f, turnY, 0f);
			Vector3 mid = e.Position + turn * new Vector3(x, 0f, z);
			mid.y = h;
			// (what can be seen: not animals or players)
			Func<Collider, bool> counts = c => c != null && !c.isTrigger && c.GetComponentInParent<AI_NetworkBehaviour>() == null && c.GetComponentInParent<Network_Player>() == null;
			var looks = new List<Vector3>();
			foreach (float pitch in new[] { -20f, 0f, 15f, 35f, 60f })
				for (int i = 0; i < 16; i++) looks.Add(Quaternion.Euler(-pitch, turnY + i * 22.5f, 0f) * Vector3.forward);
			looks.Add(Vector3.up);
			var eyes = new List<Vector3>();
			var what = new Dictionary<string, int>();
			int sky = 0, back = 0;
			Vector3 worstEye = Vector3.zero, worstLook = Vector3.zero;
			float worstUp = -2f;
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.SyncTransforms();
			try
			{
				foreach (Vector3 off in new[] { Vector3.zero, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
				{
					Vector3 p = mid + turn * off * 1.5f;
					// (the floor: the nearest face up under the given height + 1 m)
					Physics.queriesHitBackfaces = false;
					RaycastHit? floor = null;
					foreach (RaycastHit fh in Physics.RaycastAll(p + Vector3.up, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
						if (counts(fh.collider) && fh.normal.y > 0.6f && (floor == null || fh.distance < floor.Value.distance)) floor = fh;
					if (floor == null) { Log("  no floor at " + p.ToString("F1")); continue; }
					Vector3 eye = floor.Value.point + Vector3.up * 1.6f;
					eyes.Add(eye);
					foreach (Vector3 d in looks)
					{
						Physics.queriesHitBackfaces = true;
						List<RaycastHit> all = Physics.RaycastAll(eye, d, 150f, ~0, QueryTriggerInteraction.Ignore).Where(hh => counts(hh.collider)).OrderBy(hh => hh.distance).ToList();
						Physics.queriesHitBackfaces = false;
						RaycastHit[] fronts = Physics.RaycastAll(eye, d, 150f, ~0, QueryTriggerInteraction.Ignore);
						string w = null;
						if (all.Count == 0) { w = "sky"; sky++; }
						else if (all[0].distance > 0.3f && !fronts.Any(f => f.collider == all[0].collider && Mathf.Abs(f.distance - all[0].distance) < 0.02f) && !CaveBacks.DrawnBothSides(all[0].collider))
						{
							w = (all[0].collider is TerrainCollider ? "the ground from below" : all[0].collider.name + "'s back");
							back++;
						}
						if (w == null) continue;
						float yaw = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - turnY, 360f);
						string k = w + " at " + yaw.ToString("F0") + "° round, " + (Mathf.Asin(d.y) * Mathf.Rad2Deg).ToString("F0") + "° up";
						what[k] = what.ContainsKey(k) ? what[k] + 1 : 1;
						if (d.y > worstUp) { worstUp = d.y; worstEye = eye; worstLook = d; }
					}
				}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			if (eyes.Count == 0) { Fail("room looks: " + name + " - no floor near " + mid.ToString("F1")); yield break; }
			Log("ROOM " + name + ": " + eyes.Count + " places, " + (eyes.Count * looks.Count) + " looks: " + sky + " to the sky, " + back + " through a back (island turned " + turnY.ToString("F0") + "°, eye " + eyes[0].ToString("F1") + ")");
			foreach (var kv in what.OrderByDescending(q => q.Key.StartsWith("sky") ? 0 : 1).ThenBy(q => q.Key).Take(40)) Log("  " + kv.Key + " (" + kv.Value + "x)");
			for (int i = 0; i < 4; i++)
				yield return CameraShot(eyes[0], eyes[0] + Quaternion.Euler(-25f, turnY + 45f + i * 90f, 0f) * Vector3.forward * 5f, "room_" + name + "_" + (i + 1));
			if (worstUp > -2f) yield return CameraShot(worstEye, worstEye + worstLook * 5f, "room_" + name + "_worst");
			Log("PASS: room looks " + name);
		}

		static IEnumerator ShotFromRoutine(Vector3 at, float yaw, float pitch, string name)
		{
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.SyncTransforms();
			try
			{
				var dirs = new List<KeyValuePair<string, Vector3>>();
				for (int i = 0; i < 8; i++) dirs.Add(new KeyValuePair<string, Vector3>((yaw + i * 45f).ToString("F0") + "°", Quaternion.Euler(-pitch, yaw + i * 45f, 0f) * Vector3.forward));
				dirs.Add(new KeyValuePair<string, Vector3>("up", Vector3.up));
				dirs.Add(new KeyValuePair<string, Vector3>("down", Vector3.down));
				foreach (var d in dirs)
				{
					Physics.queriesHitBackfaces = true;
					List<RaycastHit> all = Physics.RaycastAll(at, d.Value, 80f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(4).ToList();
					Physics.queriesHitBackfaces = false;
					RaycastHit[] fronts = Physics.RaycastAll(at, d.Value, 80f, ~0, QueryTriggerInteraction.Ignore);
					Log("  " + d.Key + ": " + (all.Count == 0 ? "nothing" : string.Join(", ", all.Select(h => h.collider.name + (h.collider.isTrigger ? " (trigger)" : "") +
						(fronts.Any(f => f.collider == h.collider && Mathf.Abs(f.distance - h.distance) < 0.02f) ? "" : " (back)") + " " + h.distance.ToString("F1")).ToArray())));
				}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			yield return CameraShot(at, at + Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward * 5f, "from_" + name);
			Log("PASS: shot from " + at.ToString("F1"));
		}

		/// <summary>A cave piece's rock straight over the top of this box, under the land's height there: it stands in the cave's
		/// room, not in the ground (Rootweave Caves' hollow ladder pillar read "buried 10.4 m" under the Fungus Hall's roof, the
		/// land cell there whole, c3z37 11-21).</summary>
		static bool UnderCaveRoof(Bounds whole, float groundY)
		{
			Vector3 from = new Vector3(whole.center.x, whole.max.y + 0.05f, whole.center.z);
			float len = groundY - from.y - 0.3f; // (short of the land's top: not the mountain skin laid on it)
			if (len <= 0f) return false;
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try { return Physics.RaycastAll(from, Vector3.up, len, ~0, QueryTriggerInteraction.Ignore).Any(h => CaveRock(h.collider)); }
			finally { Physics.queriesHitBackfaces = backs0; }
		}

		/// <summary>A collider of a cave piece's rock (any piece's).</summary>
		static bool CaveRock(Collider c)
		{
			for (Transform t = c != null ? c.transform : null; t != null; t = t.parent)
			{
				PropInfo q = RaftProps.Get(t.name.Replace("(Clone)", "").Trim());
				if (q != null && q.IsCave) return true;
			}
			return false;
		}

		/// <summary>
		/// A cave piece seen from where a player stands in it (X7): from eye height over every floor under its roof, every look
		/// round meets the shell's inside, the ground from above or something else from its front - Raft draws the ground and
		/// most things from one side only: a look goes on through the back of a face, and that is a window only when it then
		/// leaves the room (not through an open end) or meets the shell's own back where it isn't drawn - and no sky but out
		/// through an open end. The looks that see through (0: none); detail: how many, and the worst place.
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
			Func<Vector3, Vector3, bool, List<RaycastHit>> hits = (from, dir, backs) =>
			{
				Physics.queriesHitBackfaces = backs;
				return Physics.RaycastAll(from, dir, 80f, ~0, QueryTriggerInteraction.Ignore).Where(h => counts(h.collider)).OrderBy(h => h.distance).ToList();
			};
			// (drawn from both sides: a cave piece's shell once it has its outside, and the mountain over the caves - X7)
			Func<Collider, bool> bothSides = c =>
			{
				return c.name == CaveMountain.SkinName || CaveBacks.DrawnBothSides(c);
			};
			var eyes = new List<Vector3>();
			var looks = new List<Vector3>();
			foreach (float pitch in new[] { -20f, 0f, 15f, 35f, 60f })
				for (int i = 0; i < 16; i++) looks.Add(Quaternion.Euler(-pitch, i * 22.5f, 0f) * Vector3.forward);
			looks.Add(Vector3.up);
			int bad = 0, worstBad = 0, inSolid = 0;
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
							// (not inside something solid that isn't the piece's own: a boulder set into the gap beside a mouth fills it, its
							// collider holds the place - no player stands there, and from inside it the look round met the shell's back: RF3, 11-05)
							if (Physics.OverlapSphere(e, 0.1f, ~0, QueryTriggerInteraction.Ignore).Concat(Physics.OverlapSphere(h.point + Vector3.up * 0.6f, 0.1f, ~0, QueryTriggerInteraction.Ignore))
								.Any(c => counts(c) && !own.Contains(c) && !(c is TerrainCollider))) { inSolid++; continue; }
							// (in the cave: its roof over it, seen from below - on the shell's own floor or on the ground; not in a gap
							// under the shell, where its floor's back is over it, nor on a rock of the piece's own that stands out of the
							// shell, under its outside - Balboa's dead end has one, under the ground and in the sea)
							RaycastHit? up = first(e, Vector3.up, 30f, true), upFront = first(e, Vector3.up, 30f, false);
							if (up == null || !own.Contains(up.Value.collider)) continue;
							if (upFront == null || upFront.Value.distance > up.Value.distance + 0.05f) continue;
							// (and walled in by it: most looks round meet its inside first, not its back - not on top of its rock,
							// under an overhang of its outside)
							int walls = 0;
							for (int i = 0; i < 16; i++)
							{
								Vector3 wd = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
								RaycastHit? w = null;
								Physics.queriesHitBackfaces = true;
								foreach (RaycastHit x2 in Physics.RaycastAll(e, wd, 30f, ~0, QueryTriggerInteraction.Ignore))
									if (own.Contains(x2.collider) && (w == null || x2.distance < w.Value.distance)) w = x2;
								RaycastHit? wf = first(e, wd, 30f, false);
								if (w != null && wf != null && wf.Value.collider == w.Value.collider && Mathf.Abs(wf.Value.distance - w.Value.distance) < 0.02f) walls++;
							}
							if (walls < 8) continue;
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
						// (what is seen that way: the nearest face from its front, or drawn from both sides - a face from its back
						// isn't drawn at all: the look goes on through it)
						List<RaycastHit> all = hits(e, d, true), fronts = hits(e, d, false);
						RaycastHit? seen = null, through = null;
						foreach (RaycastHit h in all)
						{
							if (fronts.Any(f => f.collider == h.collider && Mathf.Abs(f.distance - h.distance) < 0.02f) || bothSides(h.collider)) { seen = h; break; }
							if (through == null) through = h;
						}
						float o = Vector3.Dot(new Vector3(d.x, 0f, d.z), along);
						bool openEnd = (p.OpenPlus && o > 0.25f) || (p.OpenMinus && o < -0.25f);
						// (nothing at all that way: out of a doorway - one beside the place too, where a passage bends to its open
						// end; not one behind it, towards a closed end)
						bool doorway = (p.OpenPlus && o > -0.25f) || (p.OpenMinus && o < 0.25f);
						bool sky = seen == null;
						if (through == null || through.Value.distance < 0.3f)
						{
							if (!sky || openEnd || doorway) continue; // (a face from its front; out through an open end)
						}
						else if (!own.Contains(through.Value.collider))
						{
							// (through the ground from below, or another one-sided face, to the piece's own rock - or something in
							// front of it, in the room: the ground's underside hides nothing that should be seen. Only when the look
							// then goes out of the room, not through an open end, does the room have a window there)
							if (!sky && all.Any(h => own.Contains(h.collider) && h.distance >= seen.Value.distance - 0.05f)) continue;
							// (an air bell's skin or another thing's one-sided face, a joined passage's rock behind it: nothing of
							// the outside either)
							if (!sky && !(through.Value.collider is TerrainCollider) && CaveRock(seen.Value.collider)) continue;
							if (openEnd) continue;
						}
						Collider wc = through != null ? through.Value.collider : null;
						string w = wc == null ? "sky" : (own.Contains(wc) ? "the shell's back" : (wc is TerrainCollider ? "the ground from below" : wc.name + "'s back") + (sky ? " to the sky" : " to " + seen.Value.collider.name));
						what[w] = what.ContainsKey(w) ? what[w] + 1 : 1;
						bad++;
						if (here++ == 0) { hereLook = d; hereWhat = w + (wc == null ? "" : " " + through.Value.distance.ToString("F1") + " m off"); }
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
			detail = eyes.Count + " places to stand" + (inSolid > 0 ? " (" + inSolid + " inside a solid thing left out)" : "") + ", " + (eyes.Count * looks.Count) + " looks: " + bad + " see through" +
				(bad > 0 ? " (" + string.Join(", ", what.OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value).ToArray()) + "); worst " + worst : "");
			return bad;
		}

		/// <summary>A loose rock in the air: its underside over what is under it at nine points (25/50/75 % of its base),
		/// more than 0.3 m + 0.15 m per metre of its half width over it at most of the points where it is over something.
		/// (Not "3 points high": a round boulder resting on flat ground curves up 0.29 r at its box's quarter points, and every
		/// big one read floating - IG5 11-18.) least: the smallest gap measured (0 where it is set in).</summary>
		/// <summary>An object in a dug passage's air: its middle, its top or its foot within half a metre of the passage (a lantern
		/// hung on its wall, a beam holding its roof). The land's height there is the hill over the cave, so "buried" asked
		/// the wrong question (IG5 11-18: Stalker's Hollow's lanterns read 10-11 m under the ground).</summary>
		/// <summary>The land's hole cell at p is cut (no ground there: a cave piece's room or a dug passage).</summary>
		internal static bool GroundCutAt(Terrain land, Vector3 p)
		{
			if (land == null) return false;
			TerrainData td = land.terrainData;
			int res = td.holesResolution;
			Vector3 l = p - land.transform.position;
			int hx = Mathf.FloorToInt(l.x / td.size.x * res), hz = Mathf.FloorToInt(l.z / td.size.z * res);
			if (hx < 0 || hz < 0 || hx >= res || hz >= res) return false;
			return !td.GetHoles(hx, hz, 1, 1)[0, 0];
		}

		static bool InDugPassage(Bounds whole)
		{
			Vector3 c = whole.center;
			return DigTunnel.InPassage(c, -0.5f) || DigTunnel.InPassage(new Vector3(c.x, whole.max.y - 0.05f, c.z), -0.5f) ||
				DigTunnel.InPassage(new Vector3(c.x, whole.min.y + 0.05f, c.z), -0.5f);
		}

		static bool RockFloats(Transform o, Bounds b, Terrain land, out int seen, out int high, out float most, out float least)
		{
			seen = 0; high = 0; most = 0f; least = float.MaxValue;
			float allow = 0.3f + 0.15f * Mathf.Max(b.extents.x, b.extents.z);
			for (int k = 0; k < 9; k++)
			{
				float px = Mathf.Lerp(b.min.x, b.max.x, 0.25f + 0.25f * (k % 3)), pz = Mathf.Lerp(b.min.z, b.max.z, 0.25f + 0.25f * (k / 3));
				float g = RockGap(o, px, pz, b, land);
				if (float.IsNaN(g)) continue;
				seen++;
				least = Mathf.Min(least, g);
				if (g > allow) { high++; most = Mathf.Max(most, g); }
			}
			return seen > 0 && high * 2 > seen;
		}

		/// <summary>A rock's underside over what is under it at x z: NaN where the rock isn't over that point; 0 where it is set
		/// into the ground there; else the gap in metres (the land or the first other thing under it).</summary>
		static float RockGap(Transform self, float x, float z, Bounds b, Terrain land)
		{
			float under = float.MinValue;
			if (land != null) under = land.SampleHeight(new Vector3(x, 0f, z)) + land.transform.position.y;
			foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, b.max.y + 0.1f, z), Vector3.down, b.size.y + 400f, ~0, QueryTriggerInteraction.Ignore))
				if (!h.collider.transform.IsChildOf(self) && h.point.y < b.max.y && h.point.y > under) under = h.point.y;
			if (under == float.MinValue || under >= b.max.y) return float.NaN;
			float underside = float.MaxValue;
			foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, under + 0.01f, z), Vector3.up, b.max.y - under + 0.1f, ~0, QueryTriggerInteraction.Ignore))
				if (h.collider.transform.IsChildOf(self) && h.point.y < underside) underside = h.point.y;
			if (underside == float.MaxValue)
			{
				// (not over this point, or set into the ground here: the ray up starts inside it. Set in, it rests there - a gap of
				// 0, not a point left out: a boulder sunk into a slope with one corner over the dip read "1 of 1 points" floating,
				// 241 of the 504 floating rocks of the ground check 11-14, IG5)
				foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, b.max.y + 0.1f, z), Vector3.down, b.size.y + 0.2f, ~0, QueryTriggerInteraction.Ignore))
					if (h.collider.transform.IsChildOf(self)) return 0f;
				return float.NaN;
			}
			return Mathf.Max(0f, underside - under);
		}

		/// <summary>The ground a trunk's foot stands on: the land's height there, or - its foot in a dug passage's air, the land's
		/// height there being the hill over the cave - the passage's floor under it (NaN: none found, not measured). The land's
		/// height ignores the ground's holes, so a tree on a dug cave's floor read sunk by the hill over it (11-21).</summary>
		static float TrunkGround(Vector3 foot, Terrain land)
		{
			float ground = land.SampleHeight(foot) + land.transform.position.y;
			if (ground - foot.y <= 1f || !DigTunnel.InPassage(foot + Vector3.up * 0.6f, -0.5f)) return ground;
			RaycastHit fh;
			return DigFloor(foot + Vector3.up * 1.5f, out fh) ? fh.point.y : float.NaN;
		}

		/// <summary>A tree's or a palm's trunk foot (its pivot) more than 1 m under the land at it ("sunk trunk": set down by the
		/// lowest ground under its crown, Stranded Gull's hill palm grew out of the cliff, the user, 2026-10-30 - "buried" needs
		/// the whole top under the ground and a crown sticks out) or more than 0.4 m over what is under it ("trunk foot
		/// floating"); null when it is fine, or its foot is over the sea.</summary>
		static string TrunkFootProblem(Transform o, string n, Terrain land, float sea, Vector3 mid)
		{
			if (land == null || !OnATrunk(n)) return null;
			Vector3 foot = o.position;
			float ground = TrunkGround(foot, land);
			if (float.IsNaN(ground)) return null;
			Vector3 lp = land.transform.position, ls = land.terrainData.size;
			if (foot.x < lp.x || foot.x > lp.x + ls.x || foot.z < lp.z || foot.z > lp.z + ls.z || ground <= sea) return null;
			string at = n + " at " + Num(foot.x - mid.x) + " " + Num(foot.z - mid.z) + " h=" + Num(foot.y - sea);
			if (ground - foot.y > 1f) return "sunk trunk: " + at + ", its foot " + Num(ground - foot.y) + " m under the ground";
			if (foot.y > sea + 0.3f)
			{
				float g = GapUnder(o, foot, land);
				if (g > 0.4f) return "trunk foot floating: " + at + ", " + (g > 999f ? "nothing under it" : Num(g) + " m over what is under it");
			}
			return null;
		}

		/// <summary>A base's gap <paramref name="g"/> at height <paramref name="y"/> over the sea with only the sea's bed under
		/// it: to the water's top, which holds it there (a sluice's low end in a creek: Frostgold Creek's half walls read
		/// 1.2-2.2 m "in the air" over the creek's bed, IG5 11-18).</summary>
		internal static float GapToWater(float g, float y, float sea)
		{
			// (a base a little under the water's top: afloat in it - Fleetbreak's filter-crate rafts, foundations 0.3 m down over
			// a sea cave 2-6 m under, read "partly in the air", IG5 11-19)
			return y - g < sea ? Mathf.Max(0f, y - sea) : g;
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

		/// <summary>Whether a building piece (not <paramref name="self"/>) fills the space just under <paramref name="self"/>'s base
		/// (its own-frame box <paramref name="lmin"/>..<paramref name="lmax"/>): a box 0.3 m tall under the middle 80 % of its foot.</summary>
		static bool OnABuiltPiece(Transform self, Vector3 lmin, Vector3 lmax)
		{
			Vector3 c = self.TransformPoint(new Vector3((lmin.x + lmax.x) * 0.5f, lmin.y, (lmin.z + lmax.z) * 0.5f)) + Vector3.down * 0.2f;
			Vector3 s = Vector3.Scale(lmax - lmin, new Vector3(Mathf.Abs(self.lossyScale.x), 0f, Mathf.Abs(self.lossyScale.z))) * 0.4f;
			s.y = 0.15f;
			foreach (Collider k in Physics.OverlapBox(c, s, self.rotation, ~0, QueryTriggerInteraction.Ignore))
			{
				if (k.transform.IsChildOf(self)) continue;
				// (the piece itself or up to two parents up: not the island's root, whose name may hold "wall")
				Transform t = k.transform;
				for (int up = 0; up < 3 && t != null && t != self.parent; up++, t = t.parent)
					if (Structural.IsMatch(t.name) && !LooseRock.IsMatch(t.name)) return true;
			}
			return false;
		}

		/// <summary>The nearest thing under a point (not <paramref name="self"/>) and how far down it is - a ground line's detail.</summary>
		static string WhatIsUnder(Transform self, Vector3 foot)
		{
			RaycastHit best = default(RaycastHit); bool any = false;
			foreach (RaycastHit h in Physics.RaycastAll(foot + Vector3.up * 0.3f, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
				if (!h.collider.transform.IsChildOf(self) && (!any || h.point.y > best.point.y)) { best = h; any = true; }
			return any ? best.collider.name + " " + Num(foot.y - best.point.y) + " m" : "nothing";
		}

		/// <summary>Whether <paramref name="self"/>'s base touches a collider of something else (stands on it, or hangs on a wall).</summary>
		static bool Touches(Transform self, Bounds b, Transform objects = null)
		{
			// (only what shows holds: a crane that never showed but left its colliders held Altitude Market's ballast stone, 2026-10-22)
			if (objects != null) return TouchesShown(self, b, objects);
			var half = new Vector3(Mathf.Max(0.05f, b.extents.x * 0.9f), 0.15f, Mathf.Max(0.05f, b.extents.z * 0.9f));
			if (Physics.OverlapBox(new Vector3(b.center.x, b.min.y, b.center.z), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(self))) return true;
			// (hung on a wall or a post: its sides touch something)
			return Physics.OverlapBox(b.center, b.extents + Vector3.one * 0.08f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(self) && c.GetComponent<Terrain>() == null);
		}
	}
}
