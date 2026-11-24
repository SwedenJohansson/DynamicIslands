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
using UnityEngine.SceneManagement;

namespace DynamicIslands
{
	/// <summary>Dev tools and tests for the Raft buildings (prebuilt structures from Raft's story islands, ROADMAP/TODO 11-24).</summary>
	public static partial class DevTests
	{
		#region Scene tree (finding the buildings)

		[ConsoleCommand(name: "CISceneTree", docs: "Dev, editor (Raft buildings): writes the tree of one of Raft's island scenes to Mods\\DynamicIslands\\scenetree_<scene>.txt - per node its children and the catalog objects under it (known, taken). CISceneTree <part of a scene name> [depth] [name regex to list in full]")]
		public static void SceneTreeCommand(string[] args)
		{
			if (args == null || args.Length < 1) { Log("Usage: CISceneTree <part of a scene name> [depth] [name regex]"); return; }
			string scene = PlaceableCatalog.LandmarkSceneNames().FirstOrDefault(s => s.IndexOf(args[0], StringComparison.OrdinalIgnoreCase) >= 0);
			if (scene == null) { Fail("no scene matching " + args[0]); return; }
			int depth = 5;
			if (args.Length > 1) int.TryParse(args[1], out depth);
			Regex rx = args.Length > 2 ? new Regex(string.Join(" ", args.Skip(2).ToArray()), RegexOptions.IgnoreCase) : null;
			StartTest(SceneTreeRoutine(scene, depth, rx));
		}

		static IEnumerator SceneTreeRoutine(string scene, int depth, Regex rx)
		{
			yield return WaitForEditor(false);
			var sb = new StringBuilder();
			yield return PlaceableCatalog.VisitScene(scene, s => SceneTree(s, depth, rx, sb));
			string label = Regex.Replace(scene, @"[^A-Za-z0-9_]+", "_").Trim('_');
			string path = Path.Combine(DynamicIslands.assetpath, "scenetree_" + label + ".txt");
			File.WriteAllText(path, sb.ToString());
			Log("PASS: scene tree of " + scene + " written to " + path + " (" + sb.ToString().Count(c => c == '\n') + " lines)");
		}

		static IEnumerator SceneTree(Scene s, int depth, Regex rx, StringBuilder sb)
		{
			sb.AppendLine("# " + s.name + " - node: (off) chN = children, pN kN tN = catalog objects under it, known to the catalog, taken by its scan; span = where they sit");
			foreach (GameObject g in s.GetRootGameObjects()) TreeNode(g.transform, 0, depth, sb);
			if (rx != null)
			{
				sb.AppendLine();
				sb.AppendLine("# every node matching " + rx);
				foreach (Transform t in s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
				{
					if (!rx.IsMatch(t.name)) continue;
					Renderer r = t.GetComponent<Renderer>();
					MeshFilter mf = t.GetComponent<MeshFilter>();
					string clean = PlaceableCatalog.CleanName(t.name);
					sb.AppendLine(PathOf(t) + (t.gameObject.activeSelf ? "" : " (off)") + " ch" + t.childCount + " at " + t.position.ToString("F1") + " scale " + t.lossyScale.ToString("F2")
						+ (r != null ? " " + r.GetType().Name + (r.enabled ? "" : "(disabled)") + (r.isPartOfStaticBatch ? " batched" : "") : "")
						+ (mf != null && mf.sharedMesh != null ? " mesh '" + mf.sharedMesh.name + "' " + mf.sharedMesh.vertexCount + "v" : "")
						+ " renderersBelow " + t.GetComponentsInChildren<Renderer>(true).Length
						+ " known " + PlaceableCatalog.Known(clean) + " taken " + PlaceableCatalog.TakesObject(t, clean));
				}
			}
			yield break;
		}

		static void TreeNode(Transform t, int d, int depth, StringBuilder sb)
		{
			string indent = new string(' ', d * 2);
			string off = t.gameObject.activeSelf ? "" : " (off)";
			if (t.GetComponent<Renderer>() != null || t.GetComponent<LODGroup>() != null)
			{
				string c = PlaceableCatalog.CleanName(t.name);
				sb.AppendLine(indent + t.name + off + " [renders] known " + PlaceableCatalog.Known(c) + " taken " + PlaceableCatalog.TakesObject(t, c) + " at " + t.position.ToString("F1"));
				return;
			}
			List<KeyValuePair<string, Transform>> picks = PlaceableCatalog.PicksUnder(t).ToList();
			if (picks.Count == 0) return;
			int known = picks.Count(p => PlaceableCatalog.Known(p.Key)), taken = picks.Count(p => PlaceableCatalog.TakesObject(p.Value, p.Key));
			Vector3 min = picks[0].Value.position, max = min;
			foreach (var p in picks) { min = Vector3.Min(min, p.Value.position); max = Vector3.Max(max, p.Value.position); }
			sb.AppendLine(indent + t.name + off + " ch" + t.childCount + " p" + picks.Count + " k" + known + " t" + taken + " span " + (max - min).ToString("F0") + " from " + min.ToString("F0"));
			if (d >= depth || picks.Count <= 8)
			{
				var names = picks.GroupBy(p => p.Key).OrderByDescending(gr => gr.Count()).Take(24)
					.Select(gr => gr.Key + (gr.Count() > 1 ? " x" + gr.Count() : "") + (PlaceableCatalog.Known(gr.Key) ? "" : " (unknown)"));
				sb.AppendLine(indent + "  = " + string.Join(", ", names.ToArray()));
				return;
			}
			foreach (Transform c in t) TreeNode(c, d + 1, depth, sb);
		}

		static string PathOf(Transform t)
		{
			string path = t.name;
			for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
			return path;
		}

		#endregion

		#region Capturing Raft buildings (CICaptureBuildings) and their test (CIRaftBuildings)

		/// <summary>One catalog object of a building in Raft's scene, as plain data (the scene is unloaded after reading).</summary>
		class ScenePiece
		{
			public string Name, Path, Mats;
			public Vector3 Pos, Scale;
			public Quaternion Rot;
			public bool Taken;
		}

		/// <summary>
		/// The catalog objects under a building's nodes: paths "root/child/.../node" split by ";", each optionally with
		/// "@x0,z0,x1,z1" (only the objects standing in that box, world x/z). Problems (a node not found) are added.
		/// </summary>
		static List<ScenePiece> CollectPieces(Scene s, string paths, List<string> problems)
		{
			var pieces = new List<ScenePiece>();
			List<Transform> all = s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
			foreach (string entry in paths.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0))
			{
				string path = entry;
				float[] box = null;
				int at = entry.LastIndexOf('@');
				if (at > 0)
				{
					path = entry.Substring(0, at);
					box = entry.Substring(at + 1).Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
				}
				Transform node = all.FirstOrDefault(t => PathOf(t) == path);
				if (node == null) { problems.Add("node not found: " + path); continue; }
				IEnumerable<KeyValuePair<string, Transform>> picks = node.GetComponent<Renderer>() != null || node.GetComponent<LODGroup>() != null
					? new[] { new KeyValuePair<string, Transform>(PlaceableCatalog.CleanName(node.name), node) } : PlaceableCatalog.PicksUnder(node);
				foreach (var p in picks)
				{
					Vector3 pos = p.Value.position;
					if (box != null && (pos.x < box[0] || pos.z < box[1] || pos.x > box[2] || pos.z > box[3])) continue;
					pieces.Add(new ScenePiece { Name = p.Key, Path = PathOf(p.Value), Pos = pos, Rot = p.Value.rotation, Scale = p.Value.lossyScale, Mats = MatsOf(p.Value.gameObject), Taken = PlaceableCatalog.TakesObject(p.Value, p.Key) });
				}
			}
			return pieces;
		}

		static string MatsOf(GameObject go)
		{
			return string.Join(",", go.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
				.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray());
		}

		/// <summary>
		/// The pieces a building keeps (the catalog has them, its scan takes them, not excluded, and the catalog's copy wears
		/// the same materials - not another island's object of the same name); why each other was left out goes to skipped.
		/// Call with the pieces' objects loaded (EnsureLoaded).
		/// </summary>
		static List<ScenePiece> KeptPieces(List<ScenePiece> pieces, Regex exclude, Dictionary<string, int> skipped)
		{
			var kept = new List<ScenePiece>();
			Action<string> skip = why => { int n; skipped.TryGetValue(why, out n); skipped[why] = n + 1; };
			foreach (ScenePiece p in pieces)
			{
				if (exclude != null && exclude.IsMatch(p.Name)) { skip("excluded " + p.Name); continue; }
				if (!p.Taken) { skip("not an object of the catalog (scan leaves it out) " + p.Name); continue; }
				GameObject proto = PlaceableCatalog.Get(p.Name);
				if (proto == null) { skip("not in the catalog " + p.Name); continue; }
				string mine = MatsOf(proto);
				if (mine != p.Mats && !mine.Split(',').Intersect(p.Mats.Split(',')).Any()) { skip("another object of that name in the catalog " + p.Name); continue; }
				kept.Add(p);
			}
			return kept;
		}

		/// <summary>A building's pivot: the middle of its objects (x/z), at the lowest one's height (GroupLibrary's rule).</summary>
		static Vector3 PivotOf(List<ScenePiece> kept)
		{
			Vector3 min = kept[0].Pos, max = min;
			foreach (ScenePiece p in kept) { min = Vector3.Min(min, p.Pos); max = Vector3.Max(max, p.Pos); }
			return new Vector3((min.x + max.x) / 2f, min.y, (min.z + max.z) / 2f);
		}

		static List<GroupLibrary.Member> MembersOf(List<ScenePiece> kept)
		{
			Vector3 pivot = PivotOf(kept);
			return kept.Select(p => new GroupLibrary.Member { Name = p.Name, Position = p.Pos - pivot, Euler = p.Rot.eulerAngles, Scale = p.Scale }).ToList();
		}

		/// <summary>A line of the capture spec (and of the shipped buildings.txt): file|name|scene|paths|exclude regex.</summary>
		class BuildingSpec
		{
			public string File, Name, ScenePart, Scene, Paths;
			public Regex Exclude;
			public string ExcludeText = "";
		}

		static BuildingSpec ParseSpec(string file, string name, string scenePart, string paths, string exclude)
		{
			var b = new BuildingSpec { File = file, Name = name, ScenePart = scenePart, Paths = paths, ExcludeText = exclude ?? "" };
			b.Scene = PlaceableCatalog.LandmarkSceneNames().FirstOrDefault(s => s == scenePart) ?? PlaceableCatalog.LandmarkSceneNames().FirstOrDefault(s => s.IndexOf(scenePart, StringComparison.OrdinalIgnoreCase) >= 0);
			if (b.ExcludeText.Length > 0) b.Exclude = new Regex("^(" + b.ExcludeText + ")$", RegexOptions.IgnoreCase);
			return b;
		}

		[ConsoleCommand(name: "CICaptureBuildings", docs: "Dev, editor (Raft buildings): makes the Raft buildings from Raft's scenes as the spec says (Mods\\DynamicIslands\\raft_buildings_spec.txt: file|name|scene|node paths;...[@x0,z0,x1,z1]|exclude regex) into Mods\\DynamicIslands\\raft_buildings_capture (copy to the mod's raft_buildings). CICaptureBuildings [only files containing]. CICaptureBuildings auto <scene part> [max parts 250] [max span m 30] [most 25]: the nodes that look like one building each, as .group files, spec lines and pictures (shot_cand_*) in raft_buildings_capture\auto")]
		public static void CaptureBuildingsCommand(string[] args)
		{
			if (args != null && args.Length > 1 && args[0] == "auto")
			{
				int maxParts = 250, most = 25;
				float maxSpan = 30f;
				if (args.Length > 2) int.TryParse(args[2], out maxParts);
				if (args.Length > 3) float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out maxSpan);
				if (args.Length > 4) int.TryParse(args[4], out most);
				StartTest(AutoCaptureRoutine(args[1], maxParts, maxSpan, most));
				return;
			}
			string only = args != null && args.Length > 0 ? string.Join(" ", args) : null;
			StartTest(CaptureBuildingsRoutine(only));
		}

		static IEnumerator CaptureBuildingsRoutine(string only)
		{
			yield return WaitForEditor(false);
			string specFile = Path.Combine(DynamicIslands.assetpath, "raft_buildings_spec.txt");
			if (!File.Exists(specFile)) { Fail("no " + specFile); yield break; }
			var specs = new List<BuildingSpec>();
			foreach (string raw in File.ReadAllLines(specFile))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#")) continue;
				string[] f = line.Split('|');
				if (f.Length < 4) { Fail("spec line not understood: " + line); continue; }
				BuildingSpec b = ParseSpec(f[0], f[1], f[2], f[3], f.Length > 4 ? string.Join("|", f.Skip(4).ToArray()) : "");
				if (b.Scene == null) { Fail("no scene matching " + f[2] + " for " + f[1]); continue; }
				if (only == null || b.File.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0) specs.Add(b);
			}
			string outDir = Path.Combine(DynamicIslands.assetpath, "raft_buildings_capture");
			Directory.CreateDirectory(outDir);
			var lines = new List<string> { "# Raft buildings: file|name|scene|parts|node paths in the scene|left out (regex) - made by CICaptureBuildings, tested by CIRaftBuildings" };
			int made = 0;
			foreach (var byScene in specs.GroupBy(b => b.Scene))
			{
				var collected = new Dictionary<BuildingSpec, List<ScenePiece>>();
				var problems = new List<string>();
				yield return PlaceableCatalog.VisitScene(byScene.Key, s =>
				{
					foreach (BuildingSpec b in byScene) collected[b] = CollectPieces(s, b.Paths, problems);
					return NoWork();
				});
				foreach (string p in problems) Fail(byScene.Key + ": " + p);
				yield return PlaceableCatalog.EnsureLoaded(collected.Values.SelectMany(l => l.Select(p => p.Name)).Distinct().ToList());
				foreach (var kv in collected)
				{
					BuildingSpec b = kv.Key;
					var skipped = new Dictionary<string, int>();
					List<ScenePiece> kept = KeptPieces(kv.Value, b.Exclude, skipped);
					if (kept.Count == 0) { Fail(b.Name + ": nothing to keep of " + kv.Value.Count + " objects"); continue; }
					List<GroupLibrary.Member> members = MembersOf(kept);
					File.WriteAllBytes(Path.Combine(outDir, b.File + ".group"), GroupLibrary.Write(members));
					lines.Add(b.File + "|" + b.Name + "|" + b.Scene + "|" + members.Count + "|" + b.Paths + "|" + b.ExcludeText);
					made++;
					Vector3 min = members[0].Position, max = min;
					foreach (var m in members) { min = Vector3.Min(min, m.Position); max = Vector3.Max(max, m.Position); }
					Log("CAPTURED " + b.File + " '" + b.Name + "': " + members.Count + " of " + kv.Value.Count + " objects, size " + (max - min).ToString("F1")
						+ ", " + members.Select(m => m.Name).Distinct().Count() + " kinds" + (skipped.Count > 0 ? "; left out: " + string.Join(", ", skipped.Select(x => x.Key + (x.Value > 1 ? " x" + x.Value : "")).ToArray()) : ""));
				}
			}
			File.WriteAllLines(Path.Combine(outDir, "buildings.txt"), lines.ToArray());
			if (made == specs.Count) Log("PASS: captured " + made + " Raft buildings to " + outDir); else Fail("captured " + made + " of " + specs.Count + " Raft buildings");
		}

		static IEnumerator NoWork() { yield break; }

		/// <summary>A flat plateau 3 m above the water, 70 m round the terrain's middle (smoothed out to 90 m); returns its middle on the ground.</summary>
		static Vector3 MakePlateau()
		{
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			int res = data.heightmapResolution;
			float[,] heights = data.GetHeights(0, 0, res, res);
			float top = (DynamicIslands.EditorWaterLevel + 3f) / data.size.y, cell = data.size.x / (res - 1);
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
				{
					float d = Mathf.Sqrt((x - res / 2) * (x - res / 2) + (y - res / 2) * (y - res / 2)) * cell;
					if (d < 90f) heights[y, x] = Mathf.Lerp(heights[y, x], top, d < 70f ? 1f : Mathf.SmoothStep(1f, 0f, (d - 70f) / 20f));
				}
			data.SetHeights(0, 0, heights);
			terraineditor.paintMask = null;
			TerrainPainter.Setup(terrain, DynamicIslands.EditorWaterLevel);
			terraineditor.paintMask = new float[data.alphamapResolution, data.alphamapResolution];
			Vector3 c = terrain.transform.position + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			c.y = terrain.transform.position.y + terrain.SampleHeight(c);
			return c;
		}

		/// <summary>A picture of what is in the box: the editor's panels hidden, the camera looking from the front-left, a little above.</summary>
		static IEnumerator Picture(Bounds bb, string shot)
		{
			float dist = Mathf.Clamp(bb.extents.magnitude * 2.2f, 8f, 160f);
			Camera.main.transform.rotation = Quaternion.Euler(22f, 35f, 0f);
			Camera.main.transform.position = bb.center - Camera.main.transform.forward * dist;
			bool canvas = EditorUI.Canvas != null && EditorUI.Canvas.enabled;
			if (EditorUI.Canvas != null) EditorUI.Canvas.enabled = false;
			yield return new WaitForSecondsRealtime(1f);
			Screenshot(new[] { shot });
			yield return new WaitForEndOfFrame();
			yield return null;
			if (EditorUI.Canvas != null) EditorUI.Canvas.enabled = canvas;
		}

		/// <summary>A node of a scene that may be one building: the scene objects under it.</summary>
		class Candidate
		{
			public Transform Node;
			public string Path, NodeName;
			public int Taken;
			public Vector3 Min = Vector3.positiveInfinity, Max = Vector3.negativeInfinity;
			public List<ScenePiece> Pieces = new List<ScenePiece>();
		}

		/// <summary>
		/// The scene's nodes that look like one building each: 5..maxParts objects (70% of them taken by the catalog's
		/// scan), 3+ kinds, at most maxSpan m across (x/z) - and whose parent node isn't such a node too.
		/// </summary>
		static List<Candidate> FindCandidates(Scene s, int maxParts, float maxSpan)
		{
			var picks = new List<KeyValuePair<string, Transform>>();
			foreach (GameObject g in s.GetRootGameObjects())
			{
				if (g.GetComponent<Renderer>() != null || g.GetComponent<LODGroup>() != null) picks.Add(new KeyValuePair<string, Transform>(PlaceableCatalog.CleanName(g.name), g.transform));
				else picks.AddRange(PlaceableCatalog.PicksUnder(g.transform));
			}
			var stats = new Dictionary<Transform, Candidate>();
			foreach (var p in picks)
			{
				var piece = new ScenePiece { Name = p.Key, Path = PathOf(p.Value), Pos = p.Value.position, Rot = p.Value.rotation, Scale = p.Value.lossyScale, Mats = MatsOf(p.Value.gameObject), Taken = PlaceableCatalog.Known(p.Key) && PlaceableCatalog.TakesObject(p.Value, p.Key) };
				for (Transform a = p.Value.parent; a != null; a = a.parent)
				{
					Candidate c;
					if (!stats.TryGetValue(a, out c)) stats[a] = c = new Candidate { Node = a, Path = PathOf(a), NodeName = a.name };
					c.Pieces.Add(piece);
					if (piece.Taken) c.Taken++;
					c.Min = Vector3.Min(c.Min, piece.Pos);
					c.Max = Vector3.Max(c.Max, piece.Pos);
				}
			}
			Func<Transform, bool> fits = t =>
			{
				Candidate c;
				if (t == null || !stats.TryGetValue(t, out c)) return false;
				int n = c.Pieces.Count;
				return n >= 5 && n <= maxParts && c.Taken >= 0.7f * n && Mathf.Max(c.Max.x - c.Min.x, c.Max.z - c.Min.z) <= maxSpan
					&& c.Pieces.Where(x => x.Taken).Select(x => x.Name).Distinct().Count() >= 3;
			};
			return stats.Values.Where(c => fits(c.Node) && !fits(c.Node.parent)).OrderByDescending(c => c.Pieces.Count).ToList();
		}

		static IEnumerator AutoCaptureRoutine(string part, int maxParts, float maxSpan, int most)
		{
			yield return WaitForEditor(false);
			List<string> scenes = PlaceableCatalog.LandmarkSceneNames();
			string scene = scenes.FirstOrDefault(x => x == part) ?? scenes.FirstOrDefault(x => x.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
			if (scene == null) { Fail("no scene matching " + part); yield break; }
			List<Candidate> cands = null;
			yield return PlaceableCatalog.VisitScene(scene, s => { cands = FindCandidates(s, maxParts, maxSpan); return NoWork(); });
			if (cands == null) { Fail("scene " + scene + " not read"); yield break; }
			cands = cands.Take(most).ToList();
			yield return PlaceableCatalog.EnsureLoaded(cands.SelectMany(c => c.Pieces.Where(x => x.Taken).Select(x => x.Name)).Distinct().ToList());
			EditorUI.SetTab(TAB.ObjectPlace);
			Vector3 centre = MakePlateau();
			yield return null;
			string label = Regex.Replace(scene, @"[^A-Za-z0-9_]+", "_").Trim('_');
			string outDir = System.IO.Path.Combine(System.IO.Path.Combine(DynamicIslands.assetpath, "raft_buildings_capture"), "auto");
			System.IO.Directory.CreateDirectory(outDir);
			var lines = new List<string>();
			int i = 0;
			foreach (Candidate c in cands)
			{
				i++;
				var skipped = new Dictionary<string, int>();
				List<ScenePiece> kept = KeptPieces(c.Pieces, null, skipped);
				if (kept.Count < 5) { Log("CAND " + label + "_" + i + " " + c.Path + ": only " + kept.Count + " kept"); continue; }
				List<GroupLibrary.Member> members = MembersOf(kept);
				string file = label + "_" + i;
				System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, file + ".group"), GroupLibrary.Write(members));
				Vector3 min = members[0].Position, max = min;
				foreach (var m in members) { min = Vector3.Min(min, m.Position); max = Vector3.Max(max, m.Position); }
				string kinds = string.Join(", ", members.GroupBy(m => m.Name).OrderByDescending(g => g.Count()).Take(8).Select(g => g.Key + (g.Count() > 1 ? " x" + g.Count() : "")).ToArray());
				string line = file + "|" + c.NodeName + "|" + scene + "|" + members.Count + "|" + c.Path + "|"
					+ "\t# " + c.Pieces.Count + " objects, size " + (max - min).ToString("F1") + ", at " + c.Min.ToString("F0") + "; " + kinds
					+ (skipped.Count > 0 ? "; left out: " + string.Join(", ", skipped.Select(x => x.Key + (x.Value > 1 ? " x" + x.Value : "")).ToArray()) : "");
				lines.Add(line);
				Log("CAND " + line);
				// Its picture: the objects only (not placed), on the plateau
				var root = new GameObject("CIAutoBuilding");
				root.transform.position = centre;
				foreach (var m in members)
				{
					GameObject go = PlaceableCatalog.Spawn(m.Name, root.transform);
					if (go == null) continue;
					go.transform.localPosition = m.Position;
					go.transform.localRotation = Quaternion.Euler(m.Euler);
					go.transform.localScale = m.Scale;
				}
				yield return null;
				Bounds bb = new Bounds(centre, Vector3.zero);
				foreach (Renderer r in root.GetComponentsInChildren<Renderer>()) if (r is MeshRenderer || r is SkinnedMeshRenderer) bb.Encapsulate(r.bounds);
				yield return Picture(bb, "cand_" + file);
				UnityEngine.Object.Destroy(root);
				yield return null;
			}
			System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, "auto_" + label + ".txt"), lines.ToArray());
			Log("PASS: " + lines.Count + " building candidates of " + scene + " in " + outDir);
		}

		/// <summary>The shipped buildings with their spec (buildings.txt's node paths and exclude regex).</summary>
		static List<BuildingSpec> ShippedSpecs()
		{
			var list = new List<BuildingSpec>();
			byte[] bytes = RaftIslands.ModFile(RaftBuildings.Folder + "/buildings.txt");
			if (bytes == null) return list;
			foreach (string raw in Encoding.UTF8.GetString(bytes).Split('\n'))
			{
				string line = raw.Trim().TrimStart('﻿');
				if (line.Length == 0 || line.StartsWith("#")) continue;
				string[] f = line.Split('|');
				if (f.Length >= 5 && !f[0].StartsWith("whole:")) list.Add(ParseSpec(f[0], f[1], f[2], f[4], f.Length > 5 ? string.Join("|", f.Skip(5).ToArray()) : ""));
			}
			return list;
		}

		[ConsoleCommand(name: "CIRaftBuildings", docs: "Dev, editor (Raft buildings): every shipped Raft building loads with all its parts, matches Raft's scene, places as one undo step with its parts where the scene has them; a picture of each (shot_rb_<file>.png). CIRaftBuildings [only files containing]")]
		public static void RaftBuildingsCommand(string[] args)
		{
			string only = args != null && args.Length > 0 ? string.Join(" ", args) : null;
			StartTest(RaftBuildingsRoutine(only));
		}

		static IEnumerator RaftBuildingsRoutine(string only)
		{
			yield return WaitForEditor(false);
			bool ok = true;
			List<RaftBuildings.Building> shipped = RaftBuildings.All.Where(b => only == null || b.File.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			Dictionary<string, BuildingSpec> specs = ShippedSpecs().ToDictionary(s => s.File, s => s);
			Check(ref ok, shipped.Count > 0, shipped.Count + " Raft buildings ship with the mod");
			var cats = PlaceableCatalog.Browse();
			var listed = cats.FirstOrDefault(k => k.Key == RaftBuildings.Category);
			Check(ref ok, listed.Value != null && RaftBuildings.All.All(b => listed.Value.Any(e => e.Name == b.CatalogName)), "all are listed under " + RaftBuildings.Category);
			int gi = cats.FindIndex(k => k.Key == GroupLibrary.Category), ri = cats.FindIndex(k => k.Key == RaftBuildings.Category);
			Check(ref ok, ri >= 0 && (gi < 0 ? ri == 0 : ri == gi + 1), RaftBuildings.Category + " comes next to " + GroupLibrary.Category + " (" + gi + ", " + ri + ")");

			EditorUI.SetTab(TAB.ObjectPlace);
			Vector3 c = MakePlateau();
			yield return null;
			Transform placed = GameObject.Find("PlacedObjects").transform;

			foreach (RaftBuildings.Building b in shipped)
			{
				string tag = b.File + " (" + b.Name + ")";
				if (b.Whole != null)
				{
					// A whole piece of Raft's scene (the Vasagatan's hull): an island saved with it loads its scene first; one
					// object with all its meshes, standing on its bottom centre; placed and undone as one object
					if (!PlaceableCatalog.IsLoaded(b.CatalogName))
					{
						List<string> need = PlaceableCatalog.ScenesNeededFor(new[] { b.CatalogName });
						Check(ref ok, need.Contains(b.Scene), tag + ": an island with it loads Raft's " + b.Scene + " scene first (" + string.Join(", ", need.ToArray()) + ")");
					}
					yield return PlaceableCatalog.EnsureLoaded(new[] { b.CatalogName });
					GameObject wp = PlaceableCatalog.Get(b.CatalogName);
					int meshes = wp != null ? wp.GetComponentsInChildren<Renderer>(true).Length : 0;
					Check(ref ok, wp != null && meshes == b.Count, tag + ": loads as one object with all its meshes (" + meshes + " of " + b.Count + ")");
					Check(ref ok, PlaceableCatalog.DisplayName(b.CatalogName) == b.Name, tag + ": listed as '" + PlaceableCatalog.DisplayName(b.CatalogName) + "'");
					if (wp == null) continue;
					foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
					yield return null;
					CommandUndoRedo.UndoRedoManager.Clear();
					var wAt = new Vector3[1];
					yield return PlaceLikeAClick(b.CatalogName, c, wAt);
					List<EditorGameObject> one = placed.GetComponentsInChildren<EditorGameObject>().ToList();
					Check(ref ok, one.Count == 1 && one[0].GameObjectName == b.CatalogName, tag + ": placed as one object (" + string.Join(", ", one.Select(e => e.GameObjectName).ToArray()) + ")");
					Bounds wb = new Bounds(wAt[0], Vector3.zero);
					bool anyR = false;
					foreach (EditorGameObject e in one) foreach (Renderer r in e.GetComponentsInChildren<Renderer>()) { if (!anyR) { wb = r.bounds; anyR = true; } else wb.Encapsulate(r.bounds); }
					Vector3 dc = wb.center - wAt[0];
					Check(ref ok, anyR && Mathf.Abs(wb.min.y - wAt[0].y) < 0.3f && new Vector2(dc.x, dc.z).magnitude < 0.5f, tag + ": stands on its bottom centre (size " + wb.size.ToString("F1") + ", bottom " + (wb.min.y - wAt[0].y).ToString("F2") + ", centre off " + new Vector2(dc.x, dc.z).magnitude.ToString("F2") + ")");
					yield return Picture(wb, "rb_" + b.File);
					CommandUndoRedo.UndoRedoManager.Undo();
					yield return null;
					Check(ref ok, one.All(e => e == null || !e.gameObject.activeSelf), tag + ": one undo takes it away");
					foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
					CommandUndoRedo.UndoRedoManager.Clear();
					continue;
				}
				List<GroupLibrary.Member> members = RaftBuildings.Members(b);
				if (members == null) { Check(ref ok, false, tag + ": its group file reads"); continue; }
				Check(ref ok, members.Count == b.Count, tag + ": " + members.Count + " parts, buildings.txt says " + b.Count);

				// Its parts are where Raft's scene has them (the same objects at the same places, within 5 cm and 1 degree)
				BuildingSpec spec;
				if (!specs.TryGetValue(b.File, out spec) || spec.Scene == null) Check(ref ok, false, tag + ": its scene and node paths are in buildings.txt");
				else
				{
					List<ScenePiece> pieces = null;
					var problems = new List<string>();
					yield return PlaceableCatalog.VisitScene(spec.Scene, s => { pieces = CollectPieces(s, spec.Paths, problems); return NoWork(); });
					yield return PlaceableCatalog.EnsureLoaded(members.Select(m => m.Name).Distinct().ToList());
					List<GroupLibrary.Member> now = pieces != null && problems.Count == 0 ? MembersOf(KeptPieces(pieces, spec.Exclude, new Dictionary<string, int>())) : new List<GroupLibrary.Member>();
					int same = 0;
					string first = null;
					for (int i = 0; i < Math.Min(now.Count, members.Count); i++)
					{
						bool match = now[i].Name == members[i].Name && (now[i].Position - members[i].Position).magnitude < 0.05f
							&& Quaternion.Angle(Quaternion.Euler(now[i].Euler), Quaternion.Euler(members[i].Euler)) < 1f && (now[i].Scale - members[i].Scale).magnitude < 0.01f;
						if (match) same++;
						else if (first == null) first = members[i].Name + " at " + members[i].Position.ToString("F2") + " vs scene " + now[i].Name + " at " + now[i].Position.ToString("F2");
					}
					Check(ref ok, now.Count == members.Count && same == members.Count, tag + ": matches Raft's " + PlaceableCatalog.SceneLabel(spec.Scene) + " scene (" + same + " of " + members.Count + " parts, scene has " + now.Count + (problems.Count > 0 ? "; " + string.Join(", ", problems.ToArray()) : "") + (first != null ? "; first difference " + first : "") + ")");
				}

				// It loads as one object of the list, with every part
				yield return PlaceableCatalog.EnsureLoaded(new[] { b.CatalogName });
				GameObject proto = PlaceableCatalog.Get(b.CatalogName);
				int parts = proto != null ? proto.GetComponentsInChildren<GroupMember>(true).Count(g => g.transform.parent == proto.transform) : 0;
				Check(ref ok, proto != null && parts == members.Count, tag + ": loads with all its parts (" + parts + " of " + members.Count + ")");
				if (proto == null) continue;

				// Placed through the object placer (as a click does): its separate parts, at the group's places, one undo step
				foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
				yield return null;
				CommandUndoRedo.UndoRedoManager.Clear();
				var atOut = new Vector3[1];
				yield return PlaceLikeAClick(b.CatalogName, c, atOut);
				Vector3 at = atOut[0];
				List<EditorGameObject> copies = placed.GetComponentsInChildren<EditorGameObject>().ToList();
				int where = 0;
				string off = null;
				for (int i = 0; i < Math.Min(copies.Count, members.Count); i++)
				{
					Transform t = copies[i].transform;
					bool match = copies[i].GameObjectName == members[i].Name && (t.position - (at + members[i].Position)).magnitude < 0.05f && Quaternion.Angle(t.rotation, Quaternion.Euler(members[i].Euler)) < 1f;
					if (match) where++;
					else if (off == null) off = copies[i].GameObjectName + " at " + (t.position - at).ToString("F2") + ", expected " + members[i].Name + " at " + members[i].Position.ToString("F2");
				}
				Check(ref ok, copies.Count == members.Count && where == members.Count, tag + ": placed, its " + copies.Count + " parts stand where the scene has them (" + where + " in place" + (off != null ? "; first off: " + off : "") + ")");

				Bounds bb = new Bounds(at, Vector3.zero);
				foreach (EditorGameObject e in copies) foreach (Renderer r in e.GetComponentsInChildren<Renderer>()) if (r is MeshRenderer || r is SkinnedMeshRenderer) bb.Encapsulate(r.bounds);
				yield return Picture(bb, "rb_" + b.File);
				Log(tag + ": size " + bb.size.ToString("F1") + ", bottom " + (bb.min.y - at.y).ToString("F1") + " m from its pivot, ground " + (c.y - at.y).ToString("F1"));

				CommandUndoRedo.UndoRedoManager.Undo();
				yield return null;
				Check(ref ok, copies.All(e => e == null || !e.gameObject.activeSelf), tag + ": one undo takes the whole building away");
				foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
				CommandUndoRedo.UndoRedoManager.Clear();
			}
			if (ok) Log("PASS: Raft buildings (" + shipped.Count + ")"); else Fail("Raft buildings");
		}

		/// <summary>Places an object through the object placer at c's x/z resting on the ground, as a click does (at[0]: where).</summary>
		static IEnumerator PlaceLikeAClick(string name, Vector3 c, Vector3[] at)
		{
			GameObject preview = PlaceableCatalog.Spawn(name, null);
			preview.transform.position = c;
			preview.transform.rotation = Quaternion.identity;
			ObjectPlacer placer = preview.AddComponent<ObjectPlacer>();
			placer.GameObjectName = name;
			DynamicIslands.EditorGizmoHandler.placingObject = true;
			yield return null; // (Start)
			// (back over the plateau: the placer follows the mouse, and a headless test's mouse can point anywhere - the relay
			// tower, a lab shelf and the rocket wreck were set down on the sea floor 23 m below it, 2026-11-24)
			preview.transform.position = c;
			at[0] = new Vector3(c.x, PlacementOptions.RestingPivotY(preview), c.z);
			preview.transform.position = at[0];
			preview.transform.rotation = Quaternion.identity;
			typeof(ObjectPlacer).GetMethod("Place", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(placer, null);
			yield return null;
		}

		#endregion
	}
}
