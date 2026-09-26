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
using UnityEngine.SceneManagement;

namespace DynamicIslands
{
	/// <summary>
	/// Where Raft puts things on the land of its islands (CIMeasureLand): every tree, bush, rock, harvestable and piece
	/// of driftwood with the habitat it stands in - how far inland from the coast, how steep, how high above the sea,
	/// on which ground texture - and the land's area per habitat, so the generator can place things as Raft does.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIMeasureLand", docs: "Dev, editor: where Raft puts every tree, bush, rock, harvestable and piece of driftwood on the land of its islands - how far inland, how steep, how high, on which ground texture, how far apart - and the land's area per habitat: raft_land.txt in Mods\\DynamicIslands. CIMeasureLand [part of a scene name]")]
		public static void MeasureLandCommand(string[] args)
		{
			string filter = args != null && args.Length > 0 ? string.Join(" ", args) : null;
			DynamicIslands.instance.StartCoroutine(MeasureLandRoutine(filter));
		}

		/// <summary>The generator's kind of a Raft object on land (trees, bushes, rocks, harvest, beach), or props / sea.</summary>
		internal static string LandKindOf(string name)
		{
			if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^(Log|TreeLog_\d+|Driftwood\w*|Plank\w*)$")) return IslandGenerator.CatBeach;
			string k = KindOfObject(name);
			if (k == RaftIslands.Trees) return IslandGenerator.CatTrees;
			if (k == RaftIslands.Bushes) return IslandGenerator.CatBushes;
			if (k == RaftIslands.Rocks) return IslandGenerator.CatRocks;
			if (k == RaftIslands.Harvest) return IslandGenerator.CatHarvest;
			return k;
		}

		static IEnumerator MeasureLandRoutine(string filter)
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("open the editor first"); yield break; }
			float t0 = Time.realtimeSinceStartup;
			List<string> scenes = PlaceableCatalog.LandmarkSceneNames().Where(s => filter == null || s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			Log("Measuring the land of " + scenes.Count + " of Raft's island scenes");
			Scene lab = SceneManager.CreateScene("CI_MeasureLab", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
			var text = new StringBuilder();
			text.Append("# Where Raft puts things on the land of its islands (CIMeasureLand, raft=").Append(Application.version).Append(")\n");
			text.Append("# Habitat bins (RaftLand.BinOf): inland from the coast 0-3-8-20-50+ m x slope 0-10-20-30-40+ deg x height above the sea 0-1.5-4-12-30+ m\n");
			text.Append("# land\t<island>\t<style>\t<top m>\tm² of land per bin (bin:m²)\n");
			text.Append("# lobj\t<island>\t<name>\t<kind>\tcount\tcount per bin\theight p10/p50/p90\tinland p10/p50/p90\tslope p10/p50/p90\ton sand/grass/rock/other\tsize p50\tabove ground p50\tnearest same p50\tnearest any p50\n");
			int measured = 0;
			try
			{
				for (int i = 0; i < scenes.Count; i++)
				{
					if (!DynamicIslands.InEditor()) { Fail("left the editor while measuring"); yield break; }
					if (i > 0 && i % 6 == 0) yield return Resources.UnloadUnusedAssets();
					string why = "not loaded";
					bool ok = false;
					yield return PlaceableCatalog.VisitScene(scenes[i], s => MeasureScene(s, lab, r => ok = true, w => why = w, (island, h, n, cell, origin) =>
					{
						try { text.Append(LandLines(s, island, h, n, cell, origin)); }
						catch (Exception e) { Log("  " + scenes[i] + ": " + e); }
					}));
					if (ok) { measured++; Log("  " + scenes[i] + " measured"); } else Log("  " + scenes[i] + ": " + why);
				}
			}
			finally
			{
				if (lab.IsValid()) SceneManager.UnloadSceneAsync(lab);
			}
			string path = Path.Combine(DynamicIslands.assetpath, RaftLand.FileName);
			File.WriteAllText(path, text.ToString());
			RaftLand.Reload();
			Log("Measured the land of " + measured + " island(s) in " + (Time.realtimeSinceStartup - t0).ToString("F0") + " s: " + Path.GetFullPath(path));
			if (measured > 0) Log("PASS: measured the land"); else Fail("measured nothing");
		}

		[ConsoleCommand(name: "CILandLikeRaft", docs: "Dev, editor: the generator puts trees, bushes, rocks and harvestables where Raft's islands of the style have them (raft_land.txt): per kind, how much of its spread over the habitats overlaps Raft's; no more trees on the beach or on cliffs than Raft has; big rocks sunk; pictures of each style (shot_land_*)")]
		public static void LandLikeRaftCommand()
		{
			DynamicIslands.instance.StartCoroutine(LandLikeRaftRoutine());
		}

		static IEnumerator LandLikeRaftRoutine()
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor() || !PlaceableCatalog.IsBuilt) { Fail("open the editor first"); yield break; }
			bool ok = true;
			Check(ref ok, RaftLand.Loaded && DynamicIslands.instance.modlistEntry.modinfo.modFiles.ContainsKey(RaftLand.FileName), "Raft's land is measured (" + RaftLand.FileName + " shipped): " +
				string.Join(", ", Enumerable.Range(0, TerrainPainter.Styles.Length).Select(i => TerrainPainter.StyleName(i) + " " + RaftLand.For(i).Things.Count + " kinds").ToArray()));
			Vector3 size = IslandGenerator.BuildArea;
			int res = IslandGenerator.BuildResolution;
			float step = size.x / (res - 1);
			var core = new HashSet<string>(PlaceableCatalog.CoreNames);
			// (the beach strip and the cliffs: where Raft has almost nothing but bamboo and boulders)
			Func<int, bool> beachStrip = b => { int i, sl, hb; RaftLand.Unbin(b, out i, out sl, out hb); return i == 0 || hb == 0; };
			Func<int, bool> cliff = b => { int i, sl, hb; RaftLand.Unbin(b, out i, out sl, out hb); return sl == RaftLand.Bins1 - 1; };
			foreach (int style in new[] { TerrainPainter.Tropical, TerrainPainter.Forest, TerrainPainter.Desert, TerrainPainter.Snowy })
			{
				LandStyle land = RaftLand.For(style);
				var s = new IslandGenSettings { Seed = 3100 + style, Radius = 80f, Height = 45f, Style = style, Cliffs = 0.3f, Trees = 0.45f, Bushes = 0.45f, Rocks = 0.45f, Harvest = 0.45f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
				float[,] m = IslandGenerator.HeightsMetres(s, size, res);
				int[] bins = IslandGenerator.HabitatBins(s, m, step);
				var genArea = new float[RaftLand.BinCount];
				foreach (int b in bins) if (b >= 0) genArea[b] += step * step;
				List<IslandObject> objs = IslandGenerator.PlanAll(s, m, size);
				Func<IslandObject, int> binOf = o => { int x = Mathf.Clamp(Mathf.RoundToInt(o.Position.x / step), 0, res - 1), z = Mathf.Clamp(Mathf.RoundToInt(o.Position.z / step), 0, res - 1); return bins[z * res + x]; };
				foreach (string cat in new[] { IslandGenerator.CatTrees, IslandGenerator.CatBushes, IslandGenerator.CatRocks, IslandGenerator.CatHarvest })
				{
					var kinds = land.Of(cat).Where(t => core.Contains(t.Name)).ToList();
					if (kinds.Sum(t => t.Count) < 25) { Log("  " + TerrainPainter.StyleName(style) + " " + cat + ": too few on Raft's islands (the zone rules place them)"); continue; }
					var names = new HashSet<string>(kinds.Select(t => t.Name));
					var placed = objs.Where(o => names.Contains(o.Name)).ToList();
					if (placed.Count < 20) { Check(ref ok, placed.Count > 0, TerrainPainter.StyleName(style) + " " + cat + ": only " + placed.Count + " placed"); continue; }
					// Raft's spread over this island's habitats (density x area) against the generated one
					var raft = new float[RaftLand.BinCount]; var gen = new float[RaftLand.BinCount];
					// (over the land that isn't cliff: the generator keeps plants off the rock its painting puts on slopes over
					// about 35° - on Balboa, Raft has trees on grass-painted 40-55° slopes; that part is checked on its own below)
					bool rockBound = cat == IslandGenerator.CatRocks;
					for (int b = 0; b < RaftLand.BinCount; b++) raft[b] = rockBound || !cliff(b) ? kinds.Sum(t => t.Density[b]) * genArea[b] : 0f;
					float rt = raft.Sum();
					foreach (IslandObject o in placed) { int b = binOf(o); if (b >= 0 && (rockBound || !cliff(b))) gen[b]++; }
					float gt = Mathf.Max(1f, gen.Sum());
					float overlap = rt > 0f ? Enumerable.Range(0, RaftLand.BinCount).Sum(b => Mathf.Min(raft[b] / rt, gen[b] / gt)) : 0f;
					// (big ones - trees but bamboo, big rocks - on the beach strip and on cliffs, against Raft's share)
					Func<string, bool> big = n => !n.StartsWith("Bamboo");
					float raftBeach = 0f, raftCliff = 0f, raftBig = 0f;
					for (int b = 0; b < RaftLand.BinCount; b++) { float v = kinds.Where(t => big(t.Name)).Sum(t => t.Density[b]) * genArea[b]; raftBig += v; if (beachStrip(b)) raftBeach += v; if (cliff(b)) raftCliff += v; }
					var bigPlaced = placed.Where(o => big(o.Name)).ToList();
					float genBeach = bigPlaced.Count(o => { int b = binOf(o); return b >= 0 && beachStrip(b); }) / Mathf.Max(1f, bigPlaced.Count);
					float genCliff = bigPlaced.Count(o => { int b = binOf(o); return b >= 0 && cliff(b); }) / Mathf.Max(1f, bigPlaced.Count);
					raftBeach /= Mathf.Max(1e-6f, raftBig); raftCliff /= Mathf.Max(1e-6f, raftBig);
					string label = TerrainPainter.StyleName(style) + " " + cat;
					// (measured far inland on Raft - Temperance's small islands sit on its big terrain, their "coast" 100 m
					// away - the spread can't be compared on an island this size: only where they may not be is checked)
					float inland50 = kinds.Sum(t => t.Inland[1] * t.Count) / Mathf.Max(1, kinds.Sum(t => t.Count));
					if (inland50 > 60f) Log("  " + label + ": measured " + inland50.ToString("F0") + " m inland on Raft (a bigger terrain): " + placed.Count + " placed, " + (overlap * 100f).ToString("F0") + " % overlap (not compared)");
					// (55 %: plants kept off the 35-40° slopes the painting turns to rock take a little of Raft's spread away)
					else Check(ref ok, overlap > 0.55f, label + ": " + placed.Count + " placed, " + (overlap * 100f).ToString("F0") + " % of their spread over the habitats overlaps Raft's");
					if (cat == IslandGenerator.CatTrees || cat == IslandGenerator.CatBushes)
						Check(ref ok, genBeach <= raftBeach + 0.05f && genCliff <= raftCliff + 0.05f, label + ": on the beach strip " + genBeach.ToString("P0") + " (Raft " + raftBeach.ToString("P0") + "), on cliffs over 40° " + genCliff.ToString("P0") + " (Raft " + raftCliff.ToString("P0") + ")");
				}
				// Rocks sunk as Raft's: its big boulders stand about a third of their size in the ground
				var sunk = objs.Where(o => land.Of(IslandGenerator.CatRocks).Any(t => t.Name == o.Name && t.Above < -1f)).ToList();
				if (sunk.Count > 0)
				{
					float below = sunk.Count(o => o.Position.y < IslandGenerator.SampleHeights(m, res, step, o.Position.x, o.Position.z) - 0.3f) / (float)sunk.Count;
					Check(ref ok, below > 0.9f, TerrainPainter.StyleName(style) + ": " + below.ToString("P0") + " of " + sunk.Count + " big rocks sunk into the ground as Raft's are");
				}
				// A picture of it
				IslandGenerator.GenerateInEditor(s);
				IslandGenerator.FrameCamera(s);
				yield return new WaitForSecondsRealtime(1.2f);
				Screenshot(new[] { "land_" + TerrainPainter.StyleName(style).ToLowerInvariant() });
				yield return new WaitForSecondsRealtime(0.6f);
			}
			if (ok) Log("PASS: land objects placed like Raft's"); else Fail("land objects placed like Raft's");
		}

		/// <summary>Which ground texture a Raft terrain layer is: 0 sand, 1 grass (snow, ground), 2 rock, 3 other.</summary>
		static int TextureSlotOf(string texture)
		{
			foreach (TerrainPainter.Style s in TerrainPainter.Styles)
			{
				int i = Array.IndexOf(s.Textures, texture);
				if (i == TerrainPainter.Sand) return 0;
				if (i == TerrainPainter.Grass) return 1;
				if (i == TerrainPainter.Rock) return 2;
			}
			string t = texture.ToLowerInvariant();
			if (t.Contains("sand") || t.Contains("beach")) return 0;
			if (t.Contains("grass") || t.Contains("snow") || t.Contains("ground") || t.Contains("moss") || t.Contains("dirt")) return 1;
			if (t.Contains("rock") || t.Contains("cliff") || t.Contains("stone") || t.Contains("gravel")) return 2;
			return 3;
		}

		/// <summary>One island's lines of raft_land.txt.</summary>
		static string LandLines(Scene src, RaftIsland island, float[,] h, int n, float cell, Vector2 origin)
		{
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			// Distance inland from the coast (m) of every land cell (two chamfer passes from the water cells)
			var d = new float[n, n];
			for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) d[z, x] = h[z, x] <= 0f ? 0f : 1e9f;
			for (int pass = 0; pass < 2; pass++)
			{
				for (int z = 0; z < n; z++)
					for (int x = 0; x < n; x++)
					{
						float v = d[z, x];
						if (x > 0) v = Mathf.Min(v, d[z, x - 1] + 1f); if (z > 0) v = Mathf.Min(v, d[z - 1, x] + 1f);
						if (x > 0 && z > 0) v = Mathf.Min(v, d[z - 1, x - 1] + 1.414f); if (x < n - 1 && z > 0) v = Mathf.Min(v, d[z - 1, x + 1] + 1.414f);
						d[z, x] = v;
					}
				for (int z = n - 1; z >= 0; z--)
					for (int x = n - 1; x >= 0; x--)
					{
						float v = d[z, x];
						if (x < n - 1) v = Mathf.Min(v, d[z, x + 1] + 1f); if (z < n - 1) v = Mathf.Min(v, d[z + 1, x] + 1f);
						if (x < n - 1 && z < n - 1) v = Mathf.Min(v, d[z + 1, x + 1] + 1.414f); if (x > 0 && z < n - 1) v = Mathf.Min(v, d[z + 1, x - 1] + 1.414f);
						d[z, x] = v;
					}
			}
			Func<int, int, float> slopeAt = (x, z) =>
			{
				int x0 = Mathf.Max(0, x - 1), x1 = Mathf.Min(n - 1, x + 1), z0 = Mathf.Max(0, z - 1), z1 = Mathf.Min(n - 1, z + 1);
				float gx = (h[z, x1] - h[z, x0]) / ((x1 - x0) * cell), gz = (h[z1, x] - h[z0, x]) / ((z1 - z0) * cell);
				return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
			};
			Func<float, float, float> sample = (wx, wz) =>
			{
				float fx = Mathf.Clamp((wx - origin.x) / cell, 0f, n - 1.001f), fz = Mathf.Clamp((wz - origin.y) / cell, 0f, n - 1.001f);
				int x0 = (int)fx, z0 = (int)fz; float tx = fx - x0, tz = fz - z0;
				return Mathf.Lerp(Mathf.Lerp(h[z0, x0], h[z0, x0 + 1], tx), Mathf.Lerp(h[z0 + 1, x0], h[z0 + 1, x0 + 1], tx), tz);
			};

			// The land's area per habitat bin
			var area = new float[RaftLand.BinCount];
			for (int z = 0; z < n; z++)
				for (int x = 0; x < n; x++)
					if (h[z, x] > 0f) area[RaftLand.BinOf(d[z, x] * cell, slopeAt(x, z), h[z, x])] += cell * cell;
			var sb = new StringBuilder();
			sb.Append("land\t").Append(island.Label).Append('\t').Append(island.Style).Append('\t').Append(island.Top.ToString("0.#", inv)).Append('\t')
				.Append(string.Join(" ", Enumerable.Range(0, RaftLand.BinCount).Where(b => area[b] > 0f).Select(b => b + ":" + area[b].ToString("0", inv)).ToArray())).Append('\n');

			// The ground textures (the scene's terrains)
			var terrains = src.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>(true)).Where(t => t.terrainData != null && t.terrainData.terrainLayers != null && t.terrainData.terrainLayers.Length > 0).ToList();
			Func<Vector3, float[]> textureAt = p =>
			{
				var shares = new float[4];
				foreach (Terrain t in terrains)
				{
					TerrainData td = t.terrainData;
					Vector3 local = p - t.transform.position;
					if (local.x < 0f || local.z < 0f || local.x > td.size.x || local.z > td.size.z) continue;
					int ar = td.alphamapResolution;
					int ax = Mathf.Clamp((int)(local.x / td.size.x * ar), 0, ar - 1), az = Mathf.Clamp((int)(local.z / td.size.z * ar), 0, ar - 1);
					float[,,] a = td.GetAlphamaps(ax, az, 1, 1);
					for (int l = 0; l < td.alphamapLayers; l++)
					{
						TerrainLayer tl = td.terrainLayers[l];
						shares[TextureSlotOf(tl != null && tl.diffuseTexture != null ? tl.diffuseTexture.name : "")] += a[0, 0, l];
					}
					return shares;
				}
				shares[3] = 1f; // (no terrain here: a mesh island)
				return shares;
			};

			// Everything placed on the land: the placeables, and every (not nested) pickup
			var found = new List<KeyValuePair<string, Transform>>(PlaceableCatalog.PlaceablesOf(src));
			foreach (GameObject root in src.GetRootGameObjects())
				foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
				{
					if (!t.name.StartsWith("Pickup_", StringComparison.OrdinalIgnoreCase)) continue;
					bool nested = false;
					for (Transform p = t.parent; p != null && !nested; p = p.parent) nested = p.name.StartsWith("Pickup_", StringComparison.OrdinalIgnoreCase);
					if (!nested) found.Add(new KeyValuePair<string, Transform>(PlaceableCatalog.CleanName(t.name), t));
				}
			var items = new List<KeyValuePair<string, float[]>>(); // bin, height, inland, slope, sand, grass, rock, other, size, above, x, z
			foreach (var kv in found)
			{
				string kind = LandKindOf(kv.Key);
				if (kind == RaftIslands.Sea || kind == RaftIslands.Props || kind == RaftIslands.Loot) continue;
				Vector3 pos = kv.Value.position;
				float gx = (pos.x - origin.x) / cell, gz = (pos.z - origin.y) / cell;
				if (gx < 0 || gz < 0 || gx > n - 1 || gz > n - 1) continue;
				float ground = sample(pos.x, pos.z);
				if (ground <= 0f) continue; // (under water: raft_underwater.txt)
				int ix = Mathf.RoundToInt(gx), iz = Mathf.RoundToInt(gz);
				float size = 0f;
				Renderer[] rs = kv.Value.GetComponentsInChildren<Renderer>(true);
				if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds); size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)); }
				float slope = slopeAt(ix, iz), inland = d[iz, ix] * cell;
				float[] tex = textureAt(pos);
				items.Add(new KeyValuePair<string, float[]>(kv.Key, new[] { RaftLand.BinOf(inland, slope, ground), ground, inland, slope, tex[0], tex[1], tex[2], tex[3], size, pos.y - ground, pos.x, pos.z }));
			}
			// The nearest other object, of the same kind and of any kind
			var nearSame = new float[items.Count]; var nearAny = new float[items.Count];
			for (int a = 0; a < items.Count; a++)
			{
				float same = 999f, any = 999f;
				for (int b = 0; b < items.Count; b++)
				{
					if (a == b) continue;
					float dx = items[a].Value[10] - items[b].Value[10], dz = items[a].Value[11] - items[b].Value[11], dd = Mathf.Sqrt(dx * dx + dz * dz);
					if (dd < any) any = dd;
					if (dd < same && items[a].Key == items[b].Key) same = dd;
				}
				nearSame[a] = same; nearAny[a] = any;
			}
			foreach (var g in items.Select((it, i) => new { it, i }).GroupBy(x => x.it.Key).OrderByDescending(g => g.Count()))
			{
				var l = g.Select(x => x.it.Value).ToList();
				var bins = new Dictionary<int, int>();
				foreach (float[] o in l) { int b = (int)o[0]; int c; bins.TryGetValue(b, out c); bins[b] = c + 1; }
				Func<int, List<float>> col = k => l.Select(o => o[k]).ToList();
				Func<int, string> trip = k => P(col(k), 0.1f) + "/" + P(col(k), 0.5f) + "/" + P(col(k), 0.9f);
				Func<int, string> mean = k => (l.Average(o => o[k])).ToString("0.00", inv);
				sb.Append("lobj\t").Append(island.Label).Append('\t').Append(g.Key).Append('\t').Append(LandKindOf(g.Key)).Append('\t').Append(l.Count)
					.Append('\t').Append(string.Join(" ", bins.OrderBy(b => b.Key).Select(b => b.Key + ":" + b.Value).ToArray()))
					.Append('\t').Append(trip(1)).Append('\t').Append(trip(2)).Append('\t').Append(trip(3))
					.Append('\t').Append(mean(4)).Append('/').Append(mean(5)).Append('/').Append(mean(6)).Append('/').Append(mean(7))
					.Append('\t').Append(P(col(8), 0.5f)).Append('\t').Append(P(col(9), 0.5f))
					.Append('\t').Append(P(g.Select(x => nearSame[x.i]).ToList(), 0.5f)).Append('\t').Append(P(g.Select(x => nearAny[x.i]).ToList(), 0.5f)).Append('\n');
			}
			return sb.ToString();
		}
	}
}
