using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CommandUndoRedo;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's dig brush (11-01): a tunnel painted into a hill or cliff, as a Sculpt brush is painted on the ground. Press on
	/// a hillside and hold: a passage is dug in, toward where the mouse points (still mouse: on into the hill the way you look);
	/// move the mouse to steer it, under the ground where you point. A press on a tunnel already dug goes on from it (a branch).
	/// Shift+drag fills a passage back in. One stroke is one undo step.
	///
	/// The ground is one height map and can't hold a hole, so a tunnel is a placed object ("DigTunnel") holding the passage's
	/// centre line (dig.path) and making from it a rock lining: everything within Shell metres of the passage, up to just under
	/// the ground, is rock, and its surface toward the passage (and the ground cut away over it) is drawn. Inside, the passage is
	/// closed rock wherever the ground over it stands; where it comes out of the ground (its mouth, or a passage dug shallow) the
	/// ground's cells over it are cut out (terrain holes), and the rock below them closes the cut. The lining is made the same
	/// way from the same line in the editor, a test and every machine of a world, so it saves, loads and plays in multiplayer as
	/// any placed object does.
	/// </summary>
	public class DigTunnel : MonoBehaviour
	{
		public const string ObjectName = "DigTunnel";
		public const string PathKey = "dig.path";
		public const string RockName = "DigRock";

		/// <summary>Rock this thick lines the passage (more than a ground cell's diagonal, so the ground cut over it shows rock).</summary>
		public const float Shell = 5f;
		/// <summary>The rock's top this far under the ground.</summary>
		public const float Under = 0.2f;
		/// <summary>The passage's middle stands this much of its radius over its floor (a flat floor, an arched roof).</summary>
		public const float CentreUp = 0.6f;
		public const float MinRadius = 1.4f, MaxRadius = 5f, MaxSlope = 25f;
		const int MaxPoints = 2000;
		/// <summary>A ground cell is cut when the passage comes this close to its ground (between the points looked at).</summary>
		const float CutMargin = 0.7f;

		/// <summary>The brush: the passage's radius (m) and how steeply it climbs (degrees; below 0 it goes down).</summary>
		public static float Radius = 1.75f, Slope = 0f;

		/// <summary>A point of the passage: its floor (object space) and radius.</summary>
		public struct Point
		{
			public Vector3 At;
			public float R;
			public Point(Vector3 at, float r) { At = at; R = r; }
			public Vector3 Centre { get { return At + Vector3.up * (CentreUp * R); } }
		}

		[NonSerialized] public List<List<Point>> Strokes = new List<List<Point>>();
		[NonSerialized] string built;
		[NonSerialized] Mesh ownMesh;
		[NonSerialized] float groundLook = float.NaN;
		[NonSerialized] public int Triangles;
		/// <summary>The mouth's rim: how many of the lining's columns are raised over the ground (0: no mouth), the ground
		/// texture laid on its top, and where the ground is cut (the middle of the cut cells, world space).</summary>
		[NonSerialized] public int RimColumns;
		[NonSerialized] public string RimTop = "";
		[NonSerialized] Vector3 mouthMin, mouthMax;

		/// <summary>The rim's rock over the ground at the mouth's floor (just over it), and how much higher round its sides and over it.</summary>
		const float RimBase = Under + 0.16f;
		static float RimHeight(float r) { return 0.3f + 0.25f * r; }
		/// <summary>How far past the cut ground the rim goes down into it.</summary>
		static float RimOut(float r) { return 1.4f + 0.15f * r; }

		#region Path text

		static string F(float v) { return v.ToString("F2", CultureInfo.InvariantCulture); }

		public static string Format(List<List<Point>> strokes)
		{
			var sb = new StringBuilder();
			foreach (List<Point> s in strokes)
			{
				if (s.Count == 0) continue;
				if (sb.Length > 0) sb.Append('|');
				for (int i = 0; i < s.Count; i++)
				{
					if (i > 0) sb.Append(';');
					Point p = s[i];
					sb.Append(F(p.At.x)).Append(',').Append(F(p.At.y)).Append(',').Append(F(p.At.z)).Append(',').Append(F(p.R));
				}
			}
			return sb.ToString();
		}

		public static List<List<Point>> Parse(string text)
		{
			var strokes = new List<List<Point>>();
			if (string.IsNullOrEmpty(text)) return strokes;
			foreach (string st in text.Split('|'))
			{
				var s = new List<Point>();
				foreach (string pt in st.Split(';'))
				{
					string[] v = pt.Split(',');
					if (v.Length < 4) continue;
					float x, y, z, r;
					if (!float.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
						!float.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z) || !float.TryParse(v[3], NumberStyles.Float, CultureInfo.InvariantCulture, out r)) continue;
					s.Add(new Point(new Vector3(x, y, z), Mathf.Clamp(r, 0.5f, MaxRadius * 2f)));
				}
				if (s.Count > 0) strokes.Add(s);
			}
			return strokes;
		}

		public int PointCount { get { int n = 0; foreach (List<Point> s in Strokes) n += s.Count; return n; } }

		#endregion

		#region Building

		/// <summary>An object's settings applied (ObjectProps.ApplyTint, editor and world): a dug tunnel's lining made from its path.</summary>
		public static void Apply(GameObject root, IDictionary<string, string> props)
		{
			if (root == null) return;
			string text;
			if (props == null || !props.TryGetValue(PathKey, out text))
			{
				DigTunnel had = root.GetComponent<DigTunnel>();
				if (had != null) had.SetPath("");
				return;
			}
			try { UIKit.Ensure<DigTunnel>(root).SetPath(text); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnel: " + e.Message); }
		}

		public void SetPath(string text, bool force = false)
		{
			text = text ?? "";
			if (!force && text == built) return;
			Strokes = Parse(text);
			built = text;
			Rebuild();
		}

		/// <summary>The ground the tunnel is dug into: the editor's, or in a world its island's terrain under it.</summary>
		public Terrain Land()
		{
			if (DynamicIslands.InEditor() && terraineditor.terrain != null) return terraineditor.terrain;
			Transform t = transform.parent;
			for (int i = 0; i < 4 && t != null; i++, t = t.parent)
			{
				Terrain best = null;
				foreach (Terrain land in t.GetComponentsInChildren<Terrain>(true))
				{
					if (land.terrainData == null) continue;
					Vector3 o = land.transform.position, size = land.terrainData.size, p = transform.position;
					if (p.x >= o.x && p.x <= o.x + size.x && p.z >= o.z && p.z <= o.z + size.z) return land;
					if (best == null) best = land;
				}
				if (best != null) return best;
			}
			return null;
		}

		struct Seg
		{
			public Vector3 A, D; // centre at a, and to b
			public float Ra, Rb, Fa, Fb, InvLen2;
			public Vector3 Min, Max;
		}

		List<Seg> Segments(float pad)
		{
			var segs = new List<Seg>();
			foreach (List<Point> s in Strokes)
				for (int i = 0; i < s.Count; i++)
				{
					Point a = s[i], b = i + 1 < s.Count ? s[i + 1] : s[i];
					if (i + 1 >= s.Count && s.Count > 1) break;
					var g = new Seg { A = a.Centre, D = b.Centre - a.Centre, Ra = a.R, Rb = b.R, Fa = a.At.y, Fb = b.At.y };
					float l2 = g.D.sqrMagnitude;
					g.InvLen2 = l2 > 1e-6f ? 1f / l2 : 0f;
					float m = Mathf.Max(a.R, b.R) + pad;
					g.Min = Vector3.Min(a.Centre, b.Centre) - Vector3.one * m;
					g.Max = Vector3.Max(a.Centre, b.Centre) + Vector3.one * m;
					g.Min.y = Mathf.Min(g.Min.y, Mathf.Min(a.At.y, b.At.y) - pad);
					segs.Add(g);
				}
			return segs;
		}

		/// <summary>One segment's distance: below 0 inside the passage (the round passage cut flat at its floor). floor, r: its floor and radius there.</summary>
		static float SegDistance(ref Seg g, Vector3 p, out float floor, out float r)
		{
			float t = g.InvLen2 > 0f ? Mathf.Clamp01(Vector3.Dot(p - g.A, g.D) * g.InvLen2) : 0f;
			r = g.Ra + (g.Rb - g.Ra) * t;
			floor = g.Fa + (g.Fb - g.Fa) * t;
			float d = (p - (g.A + g.D * t)).magnitude - r;
			float f = floor - p.y;
			return d > f ? d : f;
		}

		/// <summary>The rough rock: a few metres of bumps on the walls and roof, none on the floor.</summary>
		static float Bumps(Vector3 p, float floor, float r)
		{
			float w = Mathf.Clamp01((p.y - floor) / (0.4f * r));
			if (w <= 0f) return 0f;
			float n = Mathf.PerlinNoise(p.x * 0.33f + 17.3f, p.z * 0.33f + 5.1f) + Mathf.PerlinNoise(p.y * 0.41f + 3.7f, p.x * 0.29f + 41.2f)
				+ Mathf.PerlinNoise(p.z * 0.37f + 23.9f, p.y * 0.31f + 11.6f);
			return (n / 3f - 0.5f) * 0.3f * r * w;
		}

		/// <summary>How far a point (object space) is from the passage: below 0 inside it.</summary>
		public float Distance(Vector3 p)
		{
			return Distance(Segments(Shell), p);
		}

		static float Distance(List<Seg> segs, Vector3 p)
		{
			float best = float.MaxValue, floor = 0f, r = 1f;
			for (int i = 0; i < segs.Count; i++)
			{
				Seg g = segs[i];
				if (p.x < g.Min.x || p.y < g.Min.y || p.z < g.Min.z || p.x > g.Max.x || p.y > g.Max.y || p.z > g.Max.z) continue;
				float f, rr;
				float d = SegDistance(ref g, p, out f, out rr);
				if (d < best) { best = d; floor = f; r = rr; }
			}
			if (best == float.MaxValue) return Shell * 4f;
			return best + Bumps(p, floor, r);
		}

		/// <summary>The path's bounds (object space), the lining's thickness around it.</summary>
		public Bounds PathBounds()
		{
			bool any = false;
			var b = new Bounds();
			foreach (List<Point> s in Strokes)
				foreach (Point p in s)
				{
					var pb = new Bounds(p.Centre, Vector3.one * 2f * (p.R + Shell));
					if (!any) { b = pb; any = true; } else b.Encapsulate(pb);
				}
			return b;
		}

		/// <summary>The lining made again from the path and the ground as it is now.</summary>
		public void Rebuild()
		{
			Transform rockT = transform.Find(RockName);
			if (PointCount == 0)
			{
				if (rockT != null) Destroy(rockT.gameObject);
				if (ownMesh != null) Destroy(ownMesh);
				ownMesh = null;
				Triangles = 0;
				return;
			}
			Terrain land = Land();
			groundLook = GroundLook(land);
			Mesh mesh = BuildMesh(land);
			GameObject rock = rockT != null ? rockT.gameObject : null;
			if (rock == null)
			{
				rock = new GameObject(RockName);
				rock.transform.SetParent(transform, false);
			}
			rock.layer = IslandSpawner.TerrainLayer;
			MeshFilter mf = UIKit.Ensure<MeshFilter>(rock);
			MeshRenderer mr = UIKit.Ensure<MeshRenderer>(rock);
			MeshCollider mc = UIKit.Ensure<MeshCollider>(rock);
			if (ownMesh != null && ownMesh != mesh) Destroy(ownMesh);
			ownMesh = mesh;
			mf.sharedMesh = mesh;
			mc.sharedMesh = null;
			mc.sharedMesh = mesh;
			mc.enabled = true;
			mr.shadowCastingMode = ShadowCastingMode.TwoSided;
			mr.receiveShadows = true;
			skinLand = land;
			Skin(mr, land);
			if (RockMaterial() == rockFallback && !rockLoading && !PlaceableCatalog.IsLoaded(CaveTool.Tunnel) && PlaceableCatalog.IsIndexed(CaveTool.Tunnel))
				StartCoroutine(LoadRock());
		}

		Terrain skinLand;
		static bool rockLoading;

		void Skin(MeshRenderer mr, Terrain land)
		{
			Material m = RockMaterial();
			if (mr.sharedMaterials.Length != 2 || mr.sharedMaterials[0] != m || mr.sharedMaterials[1] != m) mr.sharedMaterials = new[] { m, m };
			mr.SetPropertyBlock(null); mr.SetPropertyBlock(null, 0); mr.SetPropertyBlock(null, 1);
			ObjectProps.ApplyRockSkin(mr.gameObject); // (Raft's brown island rock, as the cave pieces' "skin=rock")
			GroundTop(mr, land);
		}

		/// <summary>Raft's rock shader comes with the cave tunnel piece, which loads on demand: load it, then dress every
		/// dug tunnel in it (they wore the plain stand-in until then).</summary>
		IEnumerator LoadRock()
		{
			rockLoading = true;
			try { yield return PlaceableCatalog.EnsureLoaded(new[] { CaveTool.Tunnel }); }
			finally { rockLoading = false; }
			if (RockMaterial() == rockFallback) yield break;
			foreach (DigTunnel t in FindObjectsOfType<DigTunnel>())
			{
				Transform r = t.transform.Find(RockName);
				MeshRenderer mr = r != null ? r.GetComponent<MeshRenderer>() : null;
				if (mr != null) t.Skin(mr, t.skinLand);
			}
		}

		/// <summary>The rim's top (the lining's second part) takes the ground's own texture round the mouth on its top, so the
		/// rock rising from the ground there is grassed (or sanded) over as the ground is; its steep sides stay rock.</summary>
		void GroundTop(MeshRenderer mr, Terrain land)
		{
			RimTop = "";
			if (land == null || RimColumns == 0) return;
			Material[] mats = mr.sharedMaterials;
			if (mats.Length < 2 || mats[1] == null || !mats[1].HasProperty("_TopDiffuse")) return;
			try
			{
				TerrainData td = land.terrainData;
				TerrainLayer[] layers = td.terrainLayers;
				if (layers == null || layers.Length == 0) return;
				int aw = td.alphamapWidth, ah = td.alphamapHeight;
				Vector3 lo = land.transform.position;
				float pad = RimOut(MaxR()) + 1f;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((mouthMin.x - pad - lo.x) / td.size.x * aw), 0, aw - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((mouthMax.x + pad - lo.x) / td.size.x * aw), 0, aw - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((mouthMin.z - pad - lo.z) / td.size.z * ah), 0, ah - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((mouthMax.z + pad - lo.z) / td.size.z * ah), 0, ah - 1);
				float[,,] a = td.GetAlphamaps(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
				int n = Mathf.Min(layers.Length, a.GetLength(2));
				var sum = new float[n];
				for (int z = 0; z <= z1 - z0; z++)
					for (int x = 0; x <= x1 - x0; x++)
					{
						// (the flatter ground counts most: that is where the rim's top shows the ground's look)
						float steep = td.GetSteepness((x0 + x + 0.5f) / aw, (z0 + z + 0.5f) / ah);
						float w = steep < 30f ? 1f : 0.2f;
						for (int l = 0; l < n; l++) sum[l] += a[z, x, l] * w;
					}
				int best = 0;
				for (int l = 1; l < n; l++) if (sum[l] > sum[best]) best = l;
				TerrainLayer layer = layers[best];
				if (layer == null || layer.diffuseTexture == null) return;
				var block = new MaterialPropertyBlock();
				mr.GetPropertyBlock(block, 1);
				block.SetTexture("_TopDiffuse", layer.diffuseTexture);
				if (layer.normalMapTexture != null && mats[1].HasProperty("_TopNormal")) block.SetTexture("_TopNormal", layer.normalMapTexture);
				mr.SetPropertyBlock(block, 1);
				RimTop = layer.diffuseTexture.name;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnel's rim: " + e.Message); }
		}

		float MaxR()
		{
			float r = 0f;
			foreach (List<Point> s in Strokes) foreach (Point p in s) r = Mathf.Max(r, p.R);
			return r;
		}

		static Material rockMaterial;

		static Material rockFallback;

		/// <summary>Raft's island rock material (its top/side rock shader, as the cave pieces and big rocks have), from the
		/// cave tunnel piece or a big rock; until one is found, a plain material with Raft's rock texture (11-02: a big rock's
		/// first renderer had a plain Standard material, and the lining came out flat brown).</summary>
		static Material RockMaterial()
		{
			if (rockMaterial != null) return rockMaterial;
			foreach (string name in new[] { CaveTool.Tunnel, "BigRock_1", "BigRock_2", "BigRock_3" })
			{
				GameObject proto = PlaceableCatalog.Get(name);
				if (proto == null) continue;
				Material found = proto.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
					.FirstOrDefault(x => x != null && x.HasProperty("_TopDiffuse") && x.HasProperty("_SideDiffuse"));
				if (found != null) return rockMaterial = found;
			}
			// (or any loaded rock wearing it: the island's own rocks)
			Material any = Resources.FindObjectsOfTypeAll<Material>().Where(x => x != null && x.shader != null && x.HasProperty("_TopDiffuse") && x.HasProperty("_SideDiffuse"))
				.OrderBy(x => x.shader.name.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1).FirstOrDefault();
			if (any != null) return rockMaterial = any;
			if (rockFallback == null)
			{
				Shader s = Shader.Find("Standard") ?? Shader.Find("Diffuse");
				rockFallback = new Material(s) { name = "DigTunnelRock", color = new Color(0.8f, 0.72f, 0.64f) };
				Texture d, n;
				if (ObjectProps.RockTextures(out d, out n))
				{
					rockFallback.mainTexture = d;
					if (n != null && rockFallback.HasProperty("_BumpMap")) { rockFallback.SetTexture("_BumpMap", n); rockFallback.EnableKeyword("_NORMALMAP"); }
				}
			}
			return rockFallback;
		}

		/// <summary>A number standing for the ground's heights around the tunnel (it changed: the lining is made again).</summary>
		float GroundLook(Terrain land)
		{
			if (land == null || PointCount == 0) return 0f;
			TerrainData data = land.terrainData;
			RectInt r = HeightRect(land, WorldBounds());
			if (r.width <= 0 || r.height <= 0) return 0f;
			float[,] h = data.GetHeights(r.x, r.y, r.width, r.height);
			double sum = 0;
			for (int z = 0; z < r.height; z++)
				for (int x = 0; x < r.width; x++) sum += h[z, x] * (1 + ((x * 7 + z * 13) & 15));
			return (float)sum;
		}

		Bounds WorldBounds()
		{
			Bounds b = PathBounds();
			Vector3 c = transform.TransformPoint(b.center), e = b.extents;
			float m = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
			Vector3 s = transform.lossyScale;
			float sc = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
			Quaternion q = transform.rotation;
			bool upright = Quaternion.Angle(q, Quaternion.Euler(0f, q.eulerAngles.y, 0f)) < 0.5f && Mathf.Abs(q.eulerAngles.y) < 0.01f;
			return new Bounds(c, upright ? Vector3.Scale(b.size, s) : Vector3.one * (2f * m * sc * 1.74f));
		}

		static RectInt HeightRect(Terrain land, Bounds world)
		{
			TerrainData data = land.terrainData;
			int res = data.heightmapResolution;
			Vector3 o = land.transform.position, size = data.size;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((world.min.x - o.x) / size.x * (res - 1)), 0, res - 1), z0 = Mathf.Clamp(Mathf.FloorToInt((world.min.z - o.z) / size.z * (res - 1)), 0, res - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((world.max.x - o.x) / size.x * (res - 1)), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((world.max.z - o.z) / size.z * (res - 1)), 0, res - 1);
			return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
		}

		static float GroundY(Terrain land, Vector3 world)
		{
			return land != null ? land.SampleHeight(world) + land.transform.position.y : float.MaxValue / 4f;
		}

		/// <summary>
		/// The lining: the surface (surface nets over a grid) of the rock - within Shell of the passage, outside it, and under the
		/// ground by Under - toward the passage and the ground over it. Its outer face, buried in the hill, is left out.
		/// </summary>
		Mesh BuildMesh(Terrain land)
		{
			float rMax = 0f;
			foreach (List<Point> s in Strokes) foreach (Point p in s) rMax = Mathf.Max(rMax, p.R);
			float h = Mathf.Clamp(rMax * 0.3f, 0.35f, 0.6f);
			List<Seg> segs = Segments(Shell + 2f * h);
			Bounds b = PathBounds();
			b.Expand(4f * h);
			int nx = Mathf.CeilToInt(b.size.x / h) + 1, ny = Mathf.CeilToInt(b.size.y / h) + 1, nz = Mathf.CeilToInt(b.size.z / h) + 1;
			while ((long)nx * ny * nz > 3000000L)
			{
				h *= 1.25f;
				nx = Mathf.CeilToInt(b.size.x / h) + 1; ny = Mathf.CeilToInt(b.size.y / h) + 1; nz = Mathf.CeilToInt(b.size.z / h) + 1;
			}
			Vector3 o = b.min;
			int N = nx * ny * nz;
			int sy = nx, sz = nx * ny; // index = x + y*sy + z*sz
			var dist = new float[N];
			var floorAt = new float[N];
			var radAt = new float[N];
			for (int i = 0; i < N; i++) dist[i] = float.MaxValue;

			// (each segment fills the points around it: the passage's distance, nearest segment's floor and radius)
			for (int si = 0; si < segs.Count; si++)
			{
				Seg g = segs[si];
				int x0 = Mathf.Max(0, Mathf.FloorToInt((g.Min.x - o.x) / h)), x1 = Mathf.Min(nx - 1, Mathf.CeilToInt((g.Max.x - o.x) / h));
				int y0 = Mathf.Max(0, Mathf.FloorToInt((g.Min.y - o.y) / h)), y1 = Mathf.Min(ny - 1, Mathf.CeilToInt((g.Max.y - o.y) / h));
				int z0 = Mathf.Max(0, Mathf.FloorToInt((g.Min.z - o.z) / h)), z1 = Mathf.Min(nz - 1, Mathf.CeilToInt((g.Max.z - o.z) / h));
				for (int z = z0; z <= z1; z++)
					for (int y = y0; y <= y1; y++)
					{
						int row = y * sy + z * sz;
						for (int x = x0; x <= x1; x++)
						{
							var p = new Vector3(o.x + x * h, o.y + y * h, o.z + z * h);
							float f, r;
							float d = SegDistance(ref g, p, out f, out r);
							int i = row + x;
							if (d < dist[i]) { dist[i] = d; floorAt[i] = f; radAt[i] = r; }
						}
					}
			}

			// (the rock's field: below 0 rock; and which air is the hill's own, past the rock's outside)
			Vector3 scale = transform.lossyScale;
			float sY = Mathf.Abs(scale.y) > 1e-4f ? scale.y : 1f;
			bool upright = Vector3.Dot(transform.up, Vector3.up) > 0.9995f;
			var top = new float[nx * nz]; // the rock's top (object space y), per column when upright
			if (upright)
				for (int z = 0; z < nz; z++)
					for (int x = 0; x < nx; x++)
					{
						Vector3 w = transform.TransformPoint(new Vector3(o.x + x * h, 0f, o.z + z * h));
						top[x + z * nx] = (GroundY(land, w) - Under - transform.position.y) / sY;
					}
			// (the mouth's rim: round the cut ground, the rock's top over the ground - a lip round the mouth, low at its floor,
			// higher at its sides and over it - so the cut's square cells are under rock and the rock meets the ground in a
			// ragged round line; as far from the cut as RimOut it is back under the ground)
			float[] rimFade = null, rimH = null;
			RimColumns = 0;
			mouthMin = mouthMax = Vector3.zero;
			if (upright && land != null)
			{
				var cutSet = new HashSet<Vector2Int>();
				CellsToCut(land, cutSet);
				if (cutSet.Count > 0)
				{
					TerrainData td = land.terrainData;
					int cells = td.holesResolution;
					Vector3 lo = land.transform.position;
					float cw = td.size.x / cells, ch = td.size.z / cells;
					bool first = true;
					foreach (Vector2Int q in cutSet)
					{
						Vector3 a = new Vector3(lo.x + q.x * cw, 0f, lo.z + q.y * ch), bq = a + new Vector3(cw, 0f, ch);
						if (first) { mouthMin = a; mouthMax = bq; first = false; } else { mouthMin = Vector3.Min(mouthMin, a); mouthMax = Vector3.Max(mouthMax, bq); }
					}
					float wOut = RimOut(rMax), hTop = RimHeight(rMax);
					int reachX = Mathf.CeilToInt((wOut + 0.5f) / cw) + 1, reachZ = Mathf.CeilToInt((wOut + 0.5f) / ch) + 1;
					rimFade = new float[nx * nz];
					rimH = new float[nx * nz];
					for (int z = 0; z < nz; z++)
						for (int x = 0; x < nx; x++)
						{
							Vector3 w = transform.TransformPoint(new Vector3(o.x + x * h, 0f, o.z + z * h));
							float fx = (w.x - lo.x) / cw, fz = (w.z - lo.z) / ch;
							int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
							float best = float.MaxValue;
							for (int dz = -reachZ; dz <= reachZ && best > 0f; dz++)
								for (int dx = -reachX; dx <= reachX; dx++)
								{
									if (!cutSet.Contains(new Vector2Int(ix + dx, iz + dz))) continue;
									float ex = Mathf.Max(0f, Mathf.Max((ix + dx - fx) * cw, (fx - ix - dx - 1) * cw));
									float ez = Mathf.Max(0f, Mathf.Max((iz + dz - fz) * ch, (fz - iz - dz - 1) * ch));
									float d2 = ex * ex + ez * ez;
									if (d2 < best) best = d2;
								}
							if (best == float.MaxValue) continue;
							best = Mathf.Sqrt(best);
							float rag = (Mathf.PerlinNoise(w.x * 0.55f + 7.7f, w.z * 0.55f + 3.1f) - 0.5f) * 1.0f * Mathf.Clamp01(best / 0.6f);
							float f = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((best + rag) / wOut));
							if (f <= 0f) continue;
							rimFade[x + z * nx] = f;
							rimH[x + z * nx] = hTop * (0.55f + 0.9f * Mathf.PerlinNoise(w.x * 0.3f + 19.1f, w.z * 0.3f + 2.3f));
							RimColumns++;
						}
				}
			}

			var field = new float[N];
			var outer = new bool[N];
			var passage = new bool[N];
			for (int z = 0; z < nz; z++)
				for (int y = 0; y < ny; y++)
					for (int x = 0; x < nx; x++)
					{
						int i = x + y * sy + z * sz;
						float s = dist[i];
						if (s == float.MaxValue) { field[i] = 1e6f; outer[i] = true; continue; }
						var p = new Vector3(o.x + x * h, o.y + y * h, o.z + z * h);
						s += Bumps(p, floorAt[i], radAt[i]);
						passage[i] = s < 0f;
						float t;
						if (upright)
						{
							t = top[x + z * nx];
							float rf = rimFade != null ? rimFade[x + z * nx] : 0f;
							if (rf > 0f)
							{
								// (higher with the height over the passage's floor - never as fast as the height itself, so the top is one surface)
								float rh = rimH[x + z * nx];
								float wv = Mathf.Clamp01((p.y - floorAt[i] - 0.3f) / Mathf.Max(0.8f * radAt[i], 2f * rh));
								wv = wv * wv * (3f - 2f * wv);
								float lift = rf * (RimBase + rh * wv) * Mathf.Clamp01((Shell - 0.4f - s) / 1.2f);
								t += lift / sY;
							}
						}
						else
						{
							Vector3 w = transform.TransformPoint(p);
							t = p.y + (GroundY(land, w) - Under - w.y) / sY;
						}
						float a = -s, c = s - Shell, e = p.y - t;
						float m = a > c ? a : c;
						if (e > m) m = e;
						field[i] = m;
						outer[i] = c > 0f && e <= 0f; // (past the rock's outside, under its top: the hill's own, never seen)
					}
			dist = null; floorAt = null; radAt = null;

			// (a point in each cell the surface crosses: where it crosses the cell's edges, averaged)
			int cx = nx - 1, cy = ny - 1, cz = nz - 1;
			var cellVert = new int[cx * cy * cz];
			var verts = new List<Vector3>();
			int[] cornerOff = { 0, 1, sy, 1 + sy, sz, 1 + sz, sy + sz, 1 + sy + sz };
			Vector3[] cornerPos = { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 1), new Vector3(1, 1, 1) };
			int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
			var fc = new float[8];
			for (int z = 0; z < cz; z++)
				for (int y = 0; y < cy; y++)
					for (int x = 0; x < cx; x++)
					{
						int ci = x + y * cx + z * cx * cy;
						cellVert[ci] = -1;
						int i0 = x + y * sy + z * sz;
						int mask = 0;
						for (int k = 0; k < 8; k++) { fc[k] = field[i0 + cornerOff[k]]; if (fc[k] < 0f) mask |= 1 << k; }
						if (mask == 0 || mask == 255) continue;
						Vector3 sum = Vector3.zero;
						int n = 0;
						for (int e = 0; e < 12; e++)
						{
							int a = edges[e, 0], c = edges[e, 1];
							if ((fc[a] < 0f) == (fc[c] < 0f)) continue;
							float t = fc[a] / (fc[a] - fc[c]);
							sum += Vector3.Lerp(cornerPos[a], cornerPos[c], t);
							n++;
						}
						cellVert[ci] = verts.Count;
						verts.Add(o + (new Vector3(x, y, z) + sum / n) * h);
					}

			// (a quad across each grid edge the surface crosses, facing the air - not the hill's own air past the rock; facing the
			// passage, or the open air over the rock's top - the mouth's rim, which takes the ground's look on its top)
			var tris = new List<int>();
			var trisTop = new List<int>();
			for (int z = 1; z < nz - 1; z++)
				for (int y = 1; y < ny - 1; y++)
					for (int x = 1; x < nx - 1; x++)
					{
						int i = x + y * sy + z * sz;
						bool rock = field[i] < 0f;
						for (int axis = 0; axis < 3; axis++)
						{
							int j = i + (axis == 0 ? 1 : axis == 1 ? sy : sz);
							if (axis == 0 && x + 1 >= nx || axis == 1 && y + 1 >= ny || axis == 2 && z + 1 >= nz) continue;
							bool rockJ = field[j] < 0f;
							if (rock == rockJ) continue;
							if (rock ? outer[j] : outer[i]) continue;
							// (the four cells around the edge, in turn about the axis: u, v the other two axes, u x v = the axis)
							int c0, c1, c2, c3;
							if (axis == 0) // u = y, v = z
							{
								c0 = Cell(x, y - 1, z - 1, cx, cy); c1 = Cell(x, y, z - 1, cx, cy); c2 = Cell(x, y, z, cx, cy); c3 = Cell(x, y - 1, z, cx, cy);
							}
							else if (axis == 1) // u = z, v = x
							{
								c0 = Cell(x - 1, y, z - 1, cx, cy); c1 = Cell(x - 1, y, z, cx, cy); c2 = Cell(x, y, z, cx, cy); c3 = Cell(x, y, z - 1, cx, cy);
							}
							else // u = x, v = y
							{
								c0 = Cell(x - 1, y - 1, z, cx, cy); c1 = Cell(x, y - 1, z, cx, cy); c2 = Cell(x, y, z, cx, cy); c3 = Cell(x - 1, y, z, cx, cy);
							}
							int v0 = cellVert[c0], v1 = cellVert[c1], v2 = cellVert[c2], v3 = cellVert[c3];
							if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) continue;
							// (its front - Unity's Cross(b - a, c - a) - toward the air: the plus side when the rock is on the minus one)
							List<int> into = passage[rock ? j : i] ? tris : trisTop;
							if (rock) { into.Add(v0); into.Add(v1); into.Add(v2); into.Add(v0); into.Add(v2); into.Add(v3); }
							else { into.Add(v0); into.Add(v2); into.Add(v1); into.Add(v0); into.Add(v3); into.Add(v2); }
						}
					}

			// (only the points used, normals from the faces, the rock's texture laid on from the side it faces most)
			var remap = new int[verts.Count];
			for (int k = 0; k < remap.Length; k++) remap[k] = -1;
			var used = new List<Vector3>();
			foreach (List<int> list in new[] { tris, trisTop })
				for (int k = 0; k < list.Count; k++)
				{
					int v = list[k];
					if (remap[v] < 0) { remap[v] = used.Count; used.Add(verts[v]); }
					list[k] = remap[v];
				}
			var norms = new Vector3[used.Count];
			foreach (List<int> list in new[] { tris, trisTop })
				for (int k = 0; k + 2 < list.Count; k += 3)
				{
					Vector3 a = used[list[k]], bb = used[list[k + 1]], c = used[list[k + 2]];
					Vector3 nrm = Vector3.Cross(bb - a, c - a);
					norms[list[k]] += nrm; norms[list[k + 1]] += nrm; norms[list[k + 2]] += nrm;
				}
			var uvs = new Vector2[used.Count];
			for (int k = 0; k < used.Count; k++)
			{
				Vector3 nrm = norms[k].sqrMagnitude > 1e-12f ? norms[k].normalized : Vector3.up;
				norms[k] = nrm;
				Vector3 v = used[k];
				float ax = Mathf.Abs(nrm.x), ay = Mathf.Abs(nrm.y), az = Mathf.Abs(nrm.z);
				uvs[k] = (ay >= ax && ay >= az ? new Vector2(v.x, v.z) : ax >= az ? new Vector2(v.z, v.y) : new Vector2(v.x, v.y)) / 6f;
			}
			var mesh = new Mesh { name = "DigTunnelRock" };
			if (used.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(used);
			mesh.normals = norms;
			mesh.uv = uvs;
			mesh.subMeshCount = 2;
			mesh.SetTriangles(tris, 0);
			mesh.SetTriangles(trisTop, 1);
			mesh.RecalculateBounds();
			try { mesh.RecalculateTangents(); } catch { }
			Triangles = (tris.Count + trisTop.Count) / 3;
			return mesh;
		}

		static int Cell(int x, int y, int z, int cx, int cy) { return x + y * cx + z * cx * cy; }

		#endregion

		#region Ground cut

		/// <summary>
		/// The ground cells to cut over the passage: those whose ground (at any of 3 x 3 points over the cell) comes within
		/// CutMargin of it, from just over the ground down to just under the rock's top - where the passage meets the ground
		/// (its mouth) or would have only the ground's underside for a roof.
		/// </summary>
		public void CellsToCut(Terrain land, HashSet<Vector2Int> into)
		{
			if (land == null || PointCount == 0) return;
			TerrainData data = land.terrainData;
			int cells = data.holesResolution;
			Vector3 o = land.transform.position, size = data.size;
			float cw = size.x / cells, ch = size.z / cells;
			List<Seg> segs = Segments(Shell);
			Bounds wb = WorldBounds();
			int x0 = Mathf.Clamp(Mathf.FloorToInt((wb.min.x - o.x) / cw), 0, cells - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((wb.max.x - o.x) / cw), 0, cells - 1);
			int z0 = Mathf.Clamp(Mathf.FloorToInt((wb.min.z - o.z) / ch), 0, cells - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((wb.max.z - o.z) / ch), 0, cells - 1);
			float sY = Mathf.Abs(transform.lossyScale.y) > 1e-4f ? Mathf.Abs(transform.lossyScale.y) : 1f;
			for (int z = z0; z <= z1; z++)
				for (int x = x0; x <= x1; x++)
				{
					bool cut = false;
					for (int a = 0; a <= 2 && !cut; a++)
						for (int c = 0; c <= 2 && !cut; c++)
						{
							var w = new Vector3(o.x + (x + a * 0.5f) * cw, 0f, o.z + (z + c * 0.5f) * ch);
							float g = GroundY(land, w);
							for (int k = 0; k < 3 && !cut; k++)
							{
								w.y = g + 0.1f - k * (Under + 0.3f) * 0.5f;
								if (Distance(segs, transform.InverseTransformPoint(w)) * sY < CutMargin) cut = true;
							}
						}
					if (cut) into.Add(new Vector2Int(x, z));
				}
		}

		/// <summary>A world's island spawned and switched on: the ground cut over its dug tunnels (after CaveGround's cut).</summary>
		public static void Cut(Transform objects)
		{
			if (objects == null) return;
			try
			{
				var byLand = new Dictionary<Terrain, HashSet<Vector2Int>>();
				int tunnels = 0;
				foreach (DigTunnel t in objects.GetComponentsInChildren<DigTunnel>(true))
				{
					if (t.PointCount == 0) continue;
					Terrain land = t.Land();
					if (land == null) continue;
					if (Mathf.Abs(t.GroundLook(land) - t.groundLook) > 1e-4f) t.Rebuild(); // (the ground set after the lining was made)
					HashSet<Vector2Int> set;
					if (!byLand.TryGetValue(land, out set)) byLand[land] = set = new HashSet<Vector2Int>();
					t.CellsToCut(land, set);
					tunnels++;
				}
				int n = 0;
				foreach (KeyValuePair<Terrain, HashSet<Vector2Int>> kv in byLand)
				{
					TerrainData data = kv.Key.terrainData;
					int cells = data.holesResolution;
					bool[,] solid = data.GetHoles(0, 0, cells, cells);
					foreach (Vector2Int c in kv.Value) if (solid[c.y, c.x]) { solid[c.y, c.x] = false; n++; }
					if (n > 0) data.SetHoles(0, 0, solid);
				}
				LastWorldCut = n;
				if (tunnels > 0) Debug.Log("[CUSTOM ISLANDS] Dug tunnels: " + tunnels + ", " + n + " ground cells cut over them");
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnels' ground cut: " + e.Message); }
		}

		/// <summary>Tests: how many cells the last world cut took out.</summary>
		public static int LastWorldCut;

		// (the editor: the cells cut, each one solid before; the tunnels' state when last cut)
		static readonly HashSet<Vector2Int> editorCut = new HashSet<Vector2Int>();
		static HashSet<Vector2Int> editorWant = new HashSet<Vector2Int>();
		static string editorKey;
		static Terrain editorLand;
		static float nextTick;

		static List<DigTunnel> EditorTunnels()
		{
			var list = new List<DigTunnel>();
			GameObject placed = GameObject.Find("PlacedObjects");
			if (placed != null) list.AddRange(placed.GetComponentsInChildren<DigTunnel>(false).Where(t => t.PointCount > 0));
			return list;
		}

		/// <summary>The editor, twice a second when no stroke is under way: tunnels moved, undone, or the ground sculpted over them - made again and the ground cut again.</summary>
		public static void EditorTick(Terrain land, bool busy)
		{
			if (land == null || busy || Time.realtimeSinceStartup < nextTick) return;
			nextTick = Time.realtimeSinceStartup + 0.5f;
			try { EditorNow(land, false); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnels (editor): " + e.Message); }
		}

		/// <summary>The editor's cut made now if anything changed (force: anyway). How many cells are cut for the tunnels.</summary>
		public static int EditorNow(Terrain land, bool force = true)
		{
			if (land == null) return 0;
			TerrainData data = land.terrainData;
			int cells = data.holesResolution;
			if (land != editorLand) { editorLand = land; editorCut.Clear(); editorWant.Clear(); editorKey = null; }
			List<DigTunnel> tunnels = EditorTunnels();
			var key = new StringBuilder();
			foreach (DigTunnel t in tunnels)
			{
				float look = t.GroundLook(land);
				if (Mathf.Abs(look - t.groundLook) > 1e-4f) t.Rebuild(); // (the ground sculpted over it)
				Transform tr = t.transform;
				key.Append(t.GetInstanceID()).Append(':').Append((t.built ?? "").GetHashCode()).Append(':').Append(tr.position).Append(tr.rotation).Append(tr.lossyScale).Append(':').Append(t.groundLook).Append('/');
			}
			string k = key.ToString();
			bool[,] solid = null;
			if (!force && k == editorKey)
			{
				// (still as cut? CaveGround putting its own cells back can fill one of these)
				if (editorWant.Count == 0) return 0;
				solid = data.GetHoles(0, 0, cells, cells);
				bool fine = true;
				foreach (Vector2Int c in editorWant) if (solid[c.y, c.x]) { fine = false; break; }
				if (fine) return editorWant.Count;
			}
			editorKey = k;
			var want = new HashSet<Vector2Int>();
			foreach (DigTunnel t in tunnels) t.CellsToCut(land, want);
			if (solid == null) solid = data.GetHoles(0, 0, cells, cells);
			bool changed = false;
			foreach (Vector2Int c in editorCut.ToList())
				if (!want.Contains(c)) { if (c.x < cells && c.y < cells) solid[c.y, c.x] = true; editorCut.Remove(c); changed = true; }
			foreach (Vector2Int c in want)
				if (solid[c.y, c.x]) { solid[c.y, c.x] = false; editorCut.Add(c); changed = true; }
			if (changed) data.SetHoles(0, 0, solid);
			editorWant = want;
			return want.Count;
		}

		/// <summary>Tests: the cells the editor has cut for dug tunnels now.</summary>
		public static HashSet<Vector2Int> EditorCutCells { get { return new HashSet<Vector2Int>(editorWant); } }

		#endregion

		#region The brush

		/// <summary>A stroke under way (the other ground work waits).</summary>
		public static bool Digging;
		static DigTunnel current;
		static List<Point> head; // the stroke being dug (null: filling)
		static Dictionary<string, string> before;
		static bool madeNew, filling, changedSinceBuild;
		static HashSet<GameObject> insideBefore = new HashSet<GameObject>();
		static float carry, nextBuild;
		static LineRenderer ring;
		static string shownHint;

		/// <summary>The dig brush's place under the mouse: the nearest ground or dug rock; tunnel: the one it is in, if any.</summary>
		static bool Aim(Terrain land, out Vector3 point, out DigTunnel tunnel)
		{
			point = Vector3.zero; tunnel = null;
			Camera cam = Camera.main;
			if (cam == null) return false;
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			foreach (RaycastHit hit in Physics.RaycastAll(ray, 5000f).OrderBy(x => x.distance))
			{
				if (hit.collider.isTrigger) continue;
				if (hit.collider.GetComponent<Terrain>() == land) { point = hit.point; return true; } // (a ray through a cut cell goes on into the tunnel under it)
				DigTunnel t = hit.collider.GetComponentInParent<DigTunnel>();
				if (t != null) { point = hit.point; tunnel = t; return true; }
				return false; // (an object in the way)
			}
			return false;
		}

		/// <summary>The editor's Dig brush, each frame it is the tool.</summary>
		public static void Brush(Terrain land, bool canUse)
		{
			BrushCursor.Hide();
			Vector3 point;
			DigTunnel under;
			point = Vector3.zero; under = null;
			bool aimed = canUse && land != null && Aim(land, out point, out under);
			if (!aimed) { point = Vector3.zero; under = null; }
			ShowRing(aimed, point);

			if (Digging && (!Input.GetMouseButton(0) || current == null)) { EndStroke(); return; }
			if (!Digging)
			{
				if (!aimed || !Input.GetMouseButtonDown(0) || EditorCamera.UsingMouse) return;
				BeginStroke(land, point, under, EditorInput.Shift);
				if (!Digging) return;
			}
			if (aimed) DigToward(point, Mathf.Min(Time.deltaTime, 0.1f));
			if (changedSinceBuild && Time.realtimeSinceStartup >= nextBuild) BuildNow(land);
		}

		static void BuildNow(Terrain land)
		{
			if (current == null) return;
			nextBuild = Time.realtimeSinceStartup + 0.15f;
			changedSinceBuild = false;
			current.built = Format(current.Strokes);
			current.Rebuild();
			EditorNow(land, true);
		}

		/// <summary>The nearest point of a tunnel's passage (its floor point), and how far the given world point is from its middle.</summary>
		static float Nearest(DigTunnel t, Vector3 world, out int stroke, out int index)
		{
			stroke = index = -1;
			float best = float.MaxValue;
			Vector3 p = t.transform.InverseTransformPoint(world);
			for (int s = 0; s < t.Strokes.Count; s++)
				for (int i = 0; i < t.Strokes[s].Count; i++)
				{
					float d = (t.Strokes[s][i].Centre - p).magnitude;
					if (d < best) { best = d; stroke = s; index = i; }
				}
			return best;
		}

		static void BeginStroke(Terrain land, Vector3 point, DigTunnel under, bool fill)
		{
			filling = fill;
			carry = 0f;
			madeNew = false;
			current = null;
			head = null;
			// (on a tunnel, or by one: it goes on from there)
			DigTunnel near = under;
			if (near == null)
			{
				float best = float.MaxValue;
				foreach (DigTunnel t in EditorTunnels())
				{
					int s, i;
					float d = Nearest(t, point, out s, out i);
					if (s >= 0 && d < t.Strokes[s][i].R + 0.5f && d < best) { best = d; near = t; }
				}
			}
			if (fill)
			{
				if (near == null) { Hint("Shift+drag over a dug tunnel fills it back in", true); return; }
				current = near;
			}
			else if (near != null)
			{
				int s, i;
				Nearest(near, point, out s, out i);
				if (s < 0) return;
				current = near;
				Point from = near.Strokes[s][i];
				head = new List<Point> { new Point(from.At, Radius) };
				if (Mathf.Abs(from.R - Radius) > 0.01f) head.Insert(0, from); // (from its own size to the brush's)
				near.Strokes.Add(head);
			}
			else
			{
				GameObject placed = GameObject.Find("PlacedObjects");
				if (placed == null) return;
				GameObject go = PlaceableCatalog.Spawn(ObjectName, placed.transform);
				if (go == null) { go = new GameObject(ObjectName); go.transform.SetParent(placed.transform, false); }
				go.transform.position = point;
				go.transform.rotation = Quaternion.identity;
				go.transform.localScale = Vector3.one;
				EditorGameObject.Attach(go, ObjectName, new Dictionary<string, string> { { PathKey, "" } });
				current = UIKit.Ensure<DigTunnel>(go);
				head = new List<Point> { new Point(Vector3.zero, Radius) };
				current.Strokes.Add(head);
				madeNew = true;
			}
			EditorGameObject ego = current.GetComponent<EditorGameObject>();
			before = ego != null && ego.Props != null ? new Dictionary<string, string>(ego.Props) : new Dictionary<string, string>();
			if (!madeNew && ego != null && ego.Props != null) before[PathKey] = current.built ?? "";
			insideBefore = madeNew || fill ? new HashSet<GameObject>() : InsideObjects(current);
			Digging = true;
			changedSinceBuild = true;
			nextBuild = 0f;
		}

		/// <summary>The stroke dug on toward a point (filled there, Shift); false: it can't go on toward it.</summary>
		static bool DigToward(Vector3 world, float dt)
		{
			if (current == null) return false;
			if (filling) { FillAt(world); return false; }
			if (head == null || head.Count == 0 || current.PointCount >= MaxPoints) return false;
			Vector3 target = current.transform.InverseTransformPoint(world);
			Point last = head[head.Count - 1];
			float r = last.R;
			Vector3 flat = target - last.At;
			flat.y = 0f;
			float dist = flat.magnitude;
			if (dist < 0.6f * r) return false; // (the mouse on the end: hold on, move it on to dig on)
			// (pointing back into what this stroke has dug: nothing more)
			for (int i = 0; i < head.Count - 1; i++)
			{
				Vector3 d = target - head[i].At;
				d.y = 0f;
				if (d.magnitude < 0.6f * r) return false;
			}
			float step = Mathf.Max(0.4f, 0.4f * r), speed = Mathf.Max(3f, 1.5f * r);
			carry += Mathf.Min(speed * dt, dist);
			Vector3 dir = flat / dist;
			float climb = Mathf.Tan(Mathf.Clamp(Slope, -MaxSlope, MaxSlope) * Mathf.Deg2Rad) * step;
			while (carry >= step && current.PointCount < MaxPoints)
			{
				carry -= step;
				last = head[head.Count - 1];
				head.Add(new Point(last.At + dir * step + Vector3.up * climb, Radius));
				changedSinceBuild = true;
			}
			return true;
		}

		static void FillAt(Vector3 world)
		{
			Vector3 p = current.transform.InverseTransformPoint(world);
			var kept = new List<List<Point>>();
			bool any = false;
			foreach (List<Point> s in current.Strokes)
			{
				var run = new List<Point>();
				foreach (Point q in s)
				{
					if ((q.Centre - p).magnitude < q.R * 1.3f) { any = true; if (run.Count > 0) { kept.Add(run); run = new List<Point>(); } }
					else run.Add(q);
				}
				if (run.Count > 0) kept.Add(run);
			}
			if (!any) return;
			current.Strokes = kept;
			changedSinceBuild = true;
		}

		static void EndStroke()
		{
			Digging = false;
			DigTunnel t = current;
			current = null;
			if (t == null) return;
			if (head != null && head.Count == 1 && !madeNew) t.Strokes.Remove(head); // (a press on a tunnel without digging)
			head = null;
			string path = Format(t.Strokes);
			t.built = path;
			t.Rebuild();
			EditorGameObject ego = t.GetComponent<EditorGameObject>();
			if (ego == null) return;
			var after = new Dictionary<string, string>(ego.Props ?? new Dictionary<string, string>());
			after[PathKey] = path;
			ego.Props = new Dictionary<string, string>(after);
			if (terraineditor.terrain != null) EditorNow(terraineditor.terrain, true);
			// Objects the stroke dug into (a boulder, a bush in the way) go with the rock, in the same undo step
			var gone = new List<GameObject>();
			if (!filling && t.PointCount > 0)
				foreach (GameObject g in InsideObjects(t))
					if (!insideBefore.Contains(g)) { g.SetActive(false); gone.Add(g); }
			insideBefore = new HashSet<GameObject>();
			string cleared = gone.Count > 0 ? " (" + gone.Count + " object" + (gone.Count == 1 ? "" : "s") + " in its way taken away)" : "";
			if (madeNew)
			{
				if (t.PointCount == 0) { Destroy(t.gameObject); return; }
				var made = new CommandGroup();
				made.Add(new ObjectVisibilityCommand(new[] { t.gameObject }, true));
				if (gone.Count > 0) made.Add(new ObjectVisibilityCommand(gone, false));
				UndoRedoManager.Insert(made);
				Hint("Tunnel dug: " + t.PointCount + " points" + cleared + ". Ctrl+Z takes the stroke back", false);
			}
			else if (before != null && (before.ContainsKey(PathKey) ? before[PathKey] : "") != path)
			{
				if (t.PointCount == 0)
				{
					var group = new CommandGroup();
					group.Add(new PropsCommand(ego, before, after));
					group.Add(new ObjectVisibilityCommand(new[] { t.gameObject }, false));
					t.gameObject.SetActive(false);
					UndoRedoManager.Insert(group);
					if (terraineditor.terrain != null) EditorNow(terraineditor.terrain, true);
				}
				else if (gone.Count > 0)
				{
					var group = new CommandGroup();
					group.Add(new PropsCommand(ego, before, after));
					group.Add(new ObjectVisibilityCommand(gone, false));
					UndoRedoManager.Insert(group);
					Hint("Dug on" + cleared, false);
				}
				else UndoRedoManager.Insert(new PropsCommand(ego, before, after));
			}
			before = null;
		}

		/// <summary>
		/// Placed objects in the passage - a boulder, a bush, a buried rock the tunnel goes through: their foot in it, or their
		/// colliders reaching into its middle (a sphere of 0.8 of its size at every point). The ground and dug tunnels aside.
		/// </summary>
		public static HashSet<GameObject> InsideObjects(DigTunnel t)
		{
			var set = new HashSet<GameObject>();
			if (t == null || t.PointCount == 0) return set;
			var top = new HashSet<EditorGameObject>();
			Bounds pb = t.WorldBounds();
			pb.Expand(1f);
			foreach (EditorGameObject e in SelectionTools.Placed())
			{
				if (e == null || e.GameObjectName == ObjectName || !e.gameObject.activeInHierarchy) continue;
				top.Add(e);
				if (pb.Contains(e.transform.position) && t.Distance(t.transform.InverseTransformPoint(e.transform.position)) < 0f) set.Add(e.gameObject);
			}
			Physics.SyncTransforms();
			foreach (List<Point> stroke in t.Strokes)
				foreach (Point q in stroke)
					foreach (Collider c in Physics.OverlapSphere(t.transform.TransformPoint(q.Centre), q.R * 0.8f * Mathf.Abs(t.transform.lossyScale.y), ~0, QueryTriggerInteraction.Ignore))
					{
						if (c == null || c is TerrainCollider) continue;
						for (Transform x = c.transform; x != null; x = x.parent)
						{
							EditorGameObject e = x.GetComponent<EditorGameObject>();
							if (e != null && top.Contains(e)) { set.Add(e.gameObject); break; }
						}
					}
			// (and what the mouth's rim buries: a plant or rock standing where the rim rises out of the ground, 11-02)
			Transform rockT = t.transform.Find(RockName);
			Collider rc = rockT != null ? rockT.GetComponent<Collider>() : null;
			Terrain land = t.Land();
			if (rc != null && land != null && t.RimColumns > 0)
			{
				var rim = new Bounds((t.mouthMin + t.mouthMax) * 0.5f, t.mouthMax - t.mouthMin);
				rim.Expand(new Vector3(2f * RimOut(t.MaxR()) + 1f, 40f, 2f * RimOut(t.MaxR()) + 1f));
				foreach (EditorGameObject e in top)
				{
					if (set.Contains(e.gameObject)) continue;
					Vector3 p = e.transform.position;
					RaycastHit h;
					if (!rim.Contains(p) || !rc.Raycast(new Ray(new Vector3(p.x, p.y + 30f, p.z), Vector3.down), out h, 60f)) continue;
					float ground = land.SampleHeight(p) + land.transform.position.y;
					if (h.point.y > ground + 0.1f && h.point.y > p.y + 0.25f) set.Add(e.gameObject);
				}
			}
			return set;
		}

		/// <summary>Leaving the brush (another tool): a stroke under way is finished.</summary>
		public static void Cancel()
		{
			if (Digging) EndStroke();
			if (ring != null) ring.enabled = false;
		}

		static void Hint(string text, bool warn)
		{
			if (text == shownHint && !warn) return;
			shownHint = text;
			DynamicIslands.Notify(text, warn, 4);
		}

		/// <summary>The brush's ring: the passage's size where it would be dug, facing the camera.</summary>
		static void ShowRing(bool visible, Vector3 point)
		{
			if (ring == null)
			{
				if (!visible) return;
				var go = new GameObject("CustomIslands_DigCursor");
				ring = go.AddComponent<LineRenderer>();
				ring.loop = true;
				ring.positionCount = 48;
				ring.useWorldSpace = true;
				ring.shadowCastingMode = ShadowCastingMode.Off;
				ring.receiveShadows = false;
				Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
				if (shader != null) ring.material = new Material(shader);
			}
			ring.enabled = visible;
			if (!visible) return;
			Color c = EditorInput.Shift || filling && Digging ? new Color(1f, 0.55f, 0.45f, 0.95f) : new Color(0.75f, 0.95f, 0.55f, 0.95f);
			ring.startColor = ring.endColor = c;
			if (ring.material != null) ring.material.color = c;
			float r = Radius;
			ring.startWidth = ring.endWidth = Mathf.Clamp(r * 0.04f, 0.06f, 0.4f);
			Camera cam = Camera.main;
			Vector3 centre = point + Vector3.up * (CentreUp * r);
			if (Digging && current != null && head != null && head.Count > 0) centre = current.transform.TransformPoint(head[head.Count - 1].Centre);
			Vector3 f = cam != null ? (cam.transform.position - centre).normalized : Vector3.forward;
			Vector3 a = Vector3.Cross(f, Vector3.up);
			if (a.sqrMagnitude < 1e-4f) a = Vector3.right;
			a.Normalize();
			Vector3 b = Vector3.Cross(a, f).normalized;
			centre += f * 0.3f;
			for (int i = 0; i < 48; i++)
			{
				float ang = i / 48f * Mathf.PI * 2f;
				ring.SetPosition(i, centre + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r);
			}
		}

		/// <summary>Tests: a stroke dug without the mouse, through these world points in turn (the first: where it is pressed). Returns the tunnel.</summary>
		public static DigTunnel TestStroke(Terrain land, IList<Vector3> points, bool fill = false)
		{
			if (points == null || points.Count == 0) return null;
			Vector3 p0 = points[0];
			DigTunnel under = null;
			BeginStroke(land, p0, under, fill);
			if (!Digging) return null;
			DigTunnel t = current;
			for (int k = 1; k < points.Count; k++)
				for (int guard = 0; guard < 4000; guard++)
					if (!DigToward(points[k], 0.05f) || current.PointCount >= MaxPoints) break;
			BuildNow(land);
			EndStroke();
			return t;
		}

		#endregion

		/// <summary>The tunnel object in the catalog (hidden from the object list: the Dig brush makes it).</summary>
		public static void Register()
		{
			var proto = new GameObject(ObjectName);
			proto.transform.SetParent(PlaceableCatalog.Container.transform, false);
			PlaceableCatalog.AddCustom(ObjectName, proto, ContentCatalog.ZoneCategory, "Dug tunnel");
			PlaceableCatalog.Hide(ObjectName);
		}
	}
}
