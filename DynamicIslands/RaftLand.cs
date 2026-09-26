using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>One kind of object on the land of Raft's islands of a style (measured by CIMeasureLand).</summary>
	public class LandThing
	{
		public string Name, Category;
		public int Count;
		/// <summary>Objects per 1000 m² of land, per habitat bin (RaftLand.BinOf).</summary>
		public float[] Density = new float[RaftLand.BinCount];
		/// <summary>Where they stand: height above the sea, distance inland from the coast, slope (p10 / p50 / p90).</summary>
		public float[] Height = new float[3], Inland = new float[3], Slope = new float[3];
		/// <summary>Share standing on sand, grass (snow, ground), rock.</summary>
		public float OnSand, OnGrass, OnRock;
		/// <summary>Size (m) and how far above the ground their pivot is (negative: sunk), p50; the nearest other object of the same kind (m, p50).</summary>
		public float Size, Above, NearestSame;
	}

	/// <summary>Raft's islands of a style, pooled: the land's area per habitat bin and every kind on it.</summary>
	public class LandStyle
	{
		public string Name;
		public float[] Area = new float[RaftLand.BinCount];
		public List<LandThing> Things = new List<LandThing>();
		public IEnumerable<LandThing> Of(string category) { return Things.Where(t => t.Category == category); }
		/// <summary>Objects of a category per 1000 m² in a bin (0 where Raft has none).</summary>
		public float DensityOf(string category, int bin) { return Of(category).Sum(t => t.Density[bin]); }
		public int CountOf(string category) { return Of(category).Sum(t => t.Count); }
	}

	/// <summary>
	/// Where Raft puts things on the land of its islands, by habitat: how far inland from the coast, how steep, how
	/// high above the sea (raft_land.txt, measured by the dev command CIMeasureLand, shipped with the mod). The
	/// generator places land objects where Raft has them and picks kinds as Raft mixes them there.
	/// </summary>
	public static class RaftLand
	{
		public const string FileName = "raft_land.txt";

		/// <summary>Habitat bins: metres inland from the coast, slope in degrees, metres above the sea.</summary>
		public static readonly float[] InlandBins = { 0f, 3f, 8f, 20f, 50f, 1e6f };
		public static readonly float[] SlopeBins = { 0f, 10f, 20f, 30f, 40f, 91f };
		public static readonly float[] HeightBins = { 0f, 1.5f, 4f, 12f, 30f, 1e6f };
		public const int Bins1 = 5, BinCount = Bins1 * Bins1 * Bins1;

		static int Band(float[] edges, float v) { for (int i = edges.Length - 2; i >= 0; i--) if (v >= edges[i]) return i; return 0; }

		public static int BinOf(float inland, float slope, float height) { return (Band(InlandBins, inland) * Bins1 + Band(SlopeBins, slope)) * Bins1 + Band(HeightBins, Mathf.Max(0f, height)); }

		public static void Unbin(int bin, out int inland, out int slope, out int height) { height = bin % Bins1; slope = bin / Bins1 % Bins1; inland = bin / (Bins1 * Bins1); }

		public static string DescribeBin(int bin)
		{
			int i, s, h;
			Unbin(bin, out i, out s, out h);
			Func<float[], int, string> r = (e, k) => e[k].ToString("0.#", CultureInfo.InvariantCulture) + (e[k + 1] > 1e5f ? "+" : "-" + e[k + 1].ToString("0.#", CultureInfo.InvariantCulture));
			return "inland " + r(InlandBins, i) + " m, slope " + r(SlopeBins, s) + "°, height " + r(HeightBins, h) + " m";
		}

		/// <summary>The islands that count (as RaftUnderwater): Raft's natural islands.</summary>
		static readonly System.Text.RegularExpressions.Regex Natural = new System.Text.RegularExpressions.Regex(@"^(Big island .+|Big|Small island \d+|Pilot|Boat|Balboa .+|Caravan .+|Temperance Small \d+)$");

		static LandStyle[] styles;

		public static void Reload() { styles = null; }

		public static bool Loaded { get { if (styles == null) Load(); return styles.Any(s => s != null && s.Things.Count > 0); } }

		/// <summary>Raft's islands of a style (Volcanic: the desert's); never null (empty without the file).</summary>
		public static LandStyle For(int style)
		{
			if (styles == null) Load();
			LandStyle s = styles[Mathf.Clamp(style, 0, styles.Length - 1)];
			return s ?? new LandStyle { Name = TerrainPainter.StyleName(style) };
		}

		static float Num(string s) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f; }
		static float[] Triple(string s) { string[] p = s.Split('/'); return p.Length == 3 ? new[] { Num(p[0]), Num(p[1]), Num(p[2]) } : new float[3]; }

		class Acc
		{
			public string Name, Category;
			public int Count;
			public float[] Counts = new float[BinCount];
			public double W, H0, H1, H2, I0, I1, I2, S0, S1, S2, Sand, Grass, Rock, Size, Above, Near;
		}

		static void Load()
		{
			styles = new LandStyle[TerrainPainter.Styles.Length];
			byte[] bytes = RaftIslands.ModFile(FileName);
			if (bytes == null) { Debug.LogWarning("[CUSTOM ISLANDS] " + FileName + " is missing: land objects are placed by the generator's own rules"); return; }
			var islandStyle = new Dictionary<string, int>();
			var areas = new Dictionary<int, Dictionary<string, float[]>>();
			var acc = new Dictionary<int, Dictionary<string, Acc>>();
			try
			{
				foreach (string raw in Encoding.UTF8.GetString(bytes).Split('\n'))
				{
					string line = raw.TrimEnd('\r');
					if (line.Length == 0 || line.StartsWith("#")) continue;
					string[] f = line.Split('\t');
					// (Raft's natural islands only: towns, the radio tower and the story islands' insides aren't how islands grow)
					if (f.Length > 1 && !Natural.IsMatch(f[1])) continue;
					if (f[0] == "land" && f.Length >= 5)
					{
						int st = TerrainPainter.StyleIndex(f[2]);
						islandStyle[f[1]] = st;
						var a = new float[BinCount];
						foreach (string pair in f[4].Split(' ')) { int c = pair.IndexOf(':'); if (c > 0) { int b; if (int.TryParse(pair.Substring(0, c), out b) && b >= 0 && b < BinCount) a[b] = Num(pair.Substring(c + 1)); } }
						if (!areas.ContainsKey(st)) areas[st] = new Dictionary<string, float[]>();
						// (islands sharing one terrain - Raft's small islands share a few - count once)
						areas[st][f[4]] = a;
					}
					else if (f[0] == "lobj" && f.Length >= 14 && islandStyle.ContainsKey(f[1]))
					{
						int st = islandStyle[f[1]];
						string name = f[2], cat = f[3];
						int n = (int)Num(f[4]);
						if (n <= 0) continue;
						if (!acc.ContainsKey(st)) acc[st] = new Dictionary<string, Acc>();
						Acc a;
						if (!acc[st].TryGetValue(name, out a)) acc[st][name] = a = new Acc { Name = name, Category = cat };
						foreach (string pair in f[5].Split(' ')) { int c = pair.IndexOf(':'); if (c > 0) { int b; if (int.TryParse(pair.Substring(0, c), out b) && b >= 0 && b < BinCount) a.Counts[b] += Num(pair.Substring(c + 1)); } }
						float[] h = Triple(f[6]), i = Triple(f[7]), s = Triple(f[8]), t = f[9].Split('/').Select(Num).ToArray();
						a.Count += n; a.W += n;
						a.H0 += h[0] * n; a.H1 += h[1] * n; a.H2 += h[2] * n;
						a.I0 += i[0] * n; a.I1 += i[1] * n; a.I2 += i[2] * n;
						a.S0 += s[0] * n; a.S1 += s[1] * n; a.S2 += s[2] * n;
						if (t.Length >= 3) { a.Sand += t[0] * n; a.Grass += t[1] * n; a.Rock += t[2] * n; }
						a.Size += Num(f[10]) * n; a.Above += Num(f[11]) * n; a.Near += Num(f[12]) * n;
					}
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + FileName + ": " + e.Message); }

			foreach (var kv in acc)
			{
				var style = new LandStyle { Name = TerrainPainter.StyleName(kv.Key) };
				if (areas.ContainsKey(kv.Key)) foreach (float[] a in areas[kv.Key].Values) for (int b = 0; b < BinCount; b++) style.Area[b] += a[b];
				foreach (Acc a in kv.Value.Values)
				{
					double w = Math.Max(1, a.W);
					var t = new LandThing
					{
						Name = a.Name, Category = a.Category, Count = a.Count,
						Height = new[] { (float)(a.H0 / w), (float)(a.H1 / w), (float)(a.H2 / w) },
						Inland = new[] { (float)(a.I0 / w), (float)(a.I1 / w), (float)(a.I2 / w) },
						Slope = new[] { (float)(a.S0 / w), (float)(a.S1 / w), (float)(a.S2 / w) },
						OnSand = (float)(a.Sand / w), OnGrass = (float)(a.Grass / w), OnRock = (float)(a.Rock / w),
						Size = (float)(a.Size / w), Above = (float)(a.Above / w), NearestSame = (float)(a.Near / w),
					};
					for (int b = 0; b < BinCount; b++) t.Density[b] = style.Area[b] > 1f ? a.Counts[b] * 1000f / style.Area[b] : 0f;
					style.Things.Add(t);
				}
				// (a stable order: the generator's islands must not depend on the file's order)
				style.Things.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
				styles[kv.Key] = style;
			}
			if (styles[TerrainPainter.Volcanic] == null && styles[TerrainPainter.Desert] != null) styles[TerrainPainter.Volcanic] = styles[TerrainPainter.Desert];
			Debug.Log("[CUSTOM ISLANDS] On the land of Raft's islands: " + string.Join(", ", Enumerable.Range(0, styles.Length).Where(i => styles[i] != null)
				.Select(i => TerrainPainter.StyleName(i) + " " + styles[i].Things.Count + " kinds").ToArray()));
		}
	}
}
