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

		[ConsoleCommand(name: "CIGroundIslands", docs: "Dev, world (host, a test world 'CI ...'): saved islands brought next to the raft one by one and measured as CIGroundWorld does, then taken away (IG2/IG3). <which>: part of the name, 'plans' (every island of every plan - the story lines), 'gen' (the saved generated ones) or * (all but the test islands, kept copies and randomizer extras). Results in Mods\\DynamicIslands\\ground_islands.txt. CIGroundIslands <which> [most] [skip the first n] [keep: left there, where each is logged - to look at it]")]
		public static void GroundIslandsCommand(string[] args)
		{
			string which = args != null && args.Length > 0 ? args[0] : "*";
			int most = args != null && args.Length > 1 ? int.Parse(args[1]) : 9999;
			int skip = args != null && args.Length > 2 ? int.Parse(args[2]) : 0;
			bool keep = args != null && args.Length > 3 && args[3] == "keep";
			StartTest(GroundIslandsRoutine(which, most, skip, keep));
		}

		static IEnumerator GroundIslandsRoutine(string which, int most, int skip, bool keep = false)
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
				if (keep) { Log("  kept '" + name + "' at " + e.Root.transform.position.ToString("F1") + ", turned " + e.Root.transform.eulerAngles.y.ToString("F0") + "°"); continue; }
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
