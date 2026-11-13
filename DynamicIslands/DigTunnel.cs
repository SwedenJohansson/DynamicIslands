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
		/// <summary>Another tunnel's passage is cut this much wider out of a tunnel's rock where it comes into it (its own walls in front).</summary>
		const float JoinGap = 0.15f;
		public const float MinRadius = 1.4f, MaxRadius = 5f, MaxSlope = 25f;
		const int MaxPoints = 2000;
		/// <summary>A ground cell is cut when the passage comes this close to its ground (between the points looked at).</summary>
		const float CutMargin = 0.7f;
		/// <summary>Ground no higher than this over the passage's floor is its floor, not cut (CellsToCut).</summary>
		const float FloorGround = 0.15f;

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

		/// <summary>Where the passage opens to the sky: the cut ground cells' centres (world x, z), from the last build.</summary>
		[NonSerialized] public readonly List<Vector2> Openings = new List<Vector2>();

		static readonly List<DigTunnel> live = new List<DigTunnel>();
		void OnEnable() { if (!live.Contains(this)) live.Add(this); AtmosphereZone.Hook(); }
		void OnDisable() { live.Remove(this); }

		/// <summary>Light in a dug passage fades out from this far (horizontally) from its open mouth...</summary>
		const float DarkFrom = 1.5f;
		/// <summary>...to the passage's own dim light this far in.</summary>
		const float DarkFull = 7f;

		/// <summary>How dark it is at a point (the camera) in a dug tunnel, in the editor or a world: 0 outside every passage
		/// or at its open mouth, rising to 1 at DarkFull m in. (11-02: in a world Raft's sun and its blue sky light reached the
		/// passage's far end as brightly as the hillside - Raft draws no shadow there - so it came out pale blue-white;
		/// AmbienceZones dims the light for the camera by this.)</summary>
		public static float DarkAt(Vector3 world)
		{
			float dark = 0f;
			for (int i = 0; i < live.Count; i++)
			{
				DigTunnel t = live[i];
				if (t == null || !t.isActiveAndEnabled || t.ownMesh == null) continue;
				float sY = Mathf.Abs(t.transform.lossyScale.y) > 1e-4f ? Mathf.Abs(t.transform.lossyScale.y) : 1f;
				if (t.Distance(t.transform.InverseTransformPoint(world)) * sY >= 0f) continue;
				float near = float.MaxValue;
				var at = new Vector2(world.x, world.z);
				for (int k = 0; k < t.Openings.Count; k++) near = Mathf.Min(near, (t.Openings[k] - at).sqrMagnitude);
				float d = near == float.MaxValue ? DarkFull : Mathf.Sqrt(near);
				dark = Mathf.Max(dark, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DarkFrom, DarkFull, d)));
			}
			return dark;
		}

		/// <summary>A world point in the open air of a dug passage (in the editor or a world), at least margin metres in from
		/// its walls, floor and roof. The reach audit (CIReach) asks this: under a terrain it took a dug passage for solid
		/// ground - at the mouth the cut cells leave the ground's height over the floor with nothing there, and in a bumpy
		/// passage a ray up met a wall, not a roof facing down - so a chest in a dug tunnel was never reached (11-05).</summary>
		public static bool InPassage(Vector3 world, float margin)
		{
			for (int i = 0; i < live.Count; i++)
			{
				DigTunnel t = live[i];
				if (t == null || !t.isActiveAndEnabled || t.PointCount == 0) continue;
				Bounds b = t.WorldBounds();
				b.Expand(1f);
				if (!b.Contains(world)) continue;
				float sY = Mathf.Abs(t.transform.lossyScale.y) > 1e-4f ? Mathf.Abs(t.transform.lossyScale.y) : 1f;
				// (the walls and roof stand up to Bumps (0.15 r) inside the passage's round: the margin is past them)
				float d = Distance(t.Segments(Shell), t.transform.InverseTransformPoint(world)) * sY;
				if (d < -margin) return true;
			}
			return false;
		}

		/// <summary>How far toward the passage's own dim light (AmbienceZones' TunnelLight, 1 = all the way) the lining's sky
		/// light is in a world, wherever the camera is: DarkAt dims the light for the camera only from DarkFrom m in, so seen
		/// from the mouth or just inside it the passage took the world's strong blue sky light (ambient intensity 8) and came out
		/// pale blue-white, far paler than in the editor (TN3, 11-03).</summary>
		public const float LiningDark = 0.75f;
		/// <summary>The lining's shine in a world (its rock's smoothness times this; the cave tool's smoothed rock takes 0.45 too).</summary>
		const float LiningShine = 0.45f;
		/// <summary>The lining's rock colour in a world, times this (its diffuse textures copied darker, LiningTextures).</summary>
		const float LiningAlbedo = 0.7f;
		[NonSerialized] bool madeDark;
		static readonly string[] LiningTextures = { "_TopDiffuse", "_SideDiffuse" };
		static readonly Dictionary<Texture, Texture2D> darkened = new Dictionary<Texture, Texture2D>();

		/// <summary>A copy of a rock texture LiningAlbedo as bright (read back by Readable, at its size up to 1024), with
		/// mip maps, cached; null if it can't be read (the lining then keeps its own).</summary>
		static Texture2D Darkened(Texture src)
		{
			if (src == null) return null;
			Texture2D got;
			if (darkened.TryGetValue(src, out got)) return got;
			got = Scaled(src, new Color(LiningAlbedo, LiningAlbedo, LiningAlbedo, 1f), " (lining)");
			darkened[src] = got;
			return got;
		}
		// (keyed by the factor in hundredths: a Color32 key held every factor over 1 as 255)
		static readonly Dictionary<KeyValuePair<Texture, Vector3Int>, Texture2D> scaled = new Dictionary<KeyValuePair<Texture, Vector3Int>, Texture2D>();

		/// <summary>A copy of a texture with its colour multiplied by factor (read back by Readable, at its size up to 1024), with
		/// mip maps, cached; null if it can't be read. For a shader with no colour to tint (Raft's island rock). A factor over 1
		/// brightens (up to 4; a channel stops at full), as a lone den's wall needs to wear the den's lighter shell.</summary>
		internal static Texture2D Scaled(Texture src, Color factor, string tag)
		{
			if (src == null) return null;
			var key = new KeyValuePair<Texture, Vector3Int>(src, new Vector3Int(Mathf.RoundToInt(factor.r * 100f), Mathf.RoundToInt(factor.g * 100f), Mathf.RoundToInt(factor.b * 100f)));
			Texture2D got;
			if (scaled.TryGetValue(key, out got)) return got;
			scaled[key] = null;
			int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(src.width, src.height)), 32, 1024);
			Texture2D c = Readable(src, size);
			if (c == null) return null;
			Color32[] px = c.GetPixels32();
			float fr = Mathf.Clamp(factor.r, 0f, 4f), fg = Mathf.Clamp(factor.g, 0f, 4f), fb = Mathf.Clamp(factor.b, 0f, 4f);
			for (int i = 0; i < px.Length; i++) { Color32 p = px[i]; px[i] = new Color32((byte)Mathf.Min(255f, p.r * fr), (byte)Mathf.Min(255f, p.g * fg), (byte)Mathf.Min(255f, p.b * fb), p.a); }
			got = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = src.name + (tag ?? " (tinted)"), wrapMode = src.wrapMode, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
			got.SetPixels32(px);
			got.Apply(true, true);
			scaled[key] = got;
			return got;
		}
		/// <summary>The lining's own texture for tp: the rock skin's (Raft's brown island rock, which ApplyRockSkin sets in the
		/// lining part's own block) where there is one, else its material's; taken before LightLining puts its darker copies
		/// there. (11-03: the material's was darkened and the part's block - the skin - read from the renderer's, so in a world
		/// the floor - the shader's top texture - wore Raft's big rock's snow, a pale floor that read as sunlit.)</summary>
		Texture LiningSource(Material m, string tp)
		{
			Texture t;
			if (liningSrc.TryGetValue(tp, out t)) return t;
			if (litRenderer == null) return m.GetTexture(tp);
			var b = new MaterialPropertyBlock();
			litRenderer.GetPropertyBlock(b, 0);
			t = b.isEmpty ? null : b.GetTexture(tp);
			if (t == null) { litRenderer.GetPropertyBlock(b); t = b.isEmpty ? null : b.GetTexture(tp); }
			if (t == null || t.name.EndsWith(" (lining)")) t = m.GetTexture(tp);
			liningSrc[tp] = t;
			return t;
		}
		[NonSerialized] readonly Dictionary<string, Texture> liningSrc = new Dictionary<string, Texture>();
		public static bool AnyLive { get { return live.Count > 0; } }
		[NonSerialized] MeshRenderer litRenderer;
		[NonSerialized] MaterialPropertyBlock lightBlock;
		static readonly SphericalHarmonicsL2[] oneLight = new SphericalHarmonicsL2[1];
		static readonly List<Material> litMats = new List<Material>();

		/// <summary>In a world, for the camera about to draw (AmbienceZones): every dug tunnel's lining (part 0) gets the sky
		/// light deep (its own light probe, dimmed), its rim and the rim's top (parts 1, 2: out on the hillside) open, the light
		/// the world round them gets. Each part by its own property block (the renderer's block - the rock's skin - copied in).
		/// The editor's light is right as it is: left alone there.</summary>
		public static void LightLinings(SphericalHarmonicsL2 deep, SphericalHarmonicsL2 open)
		{
			if (live.Count == 0 || DynamicIslands.InEditor() || LiningLightOff) return;
			for (int i = 0; i < live.Count; i++)
			{
				DigTunnel t = live[i];
				if (t == null || !t.isActiveAndEnabled) continue;
				try { t.LightLining(deep, open); }
				catch (Exception e) { if (!toldLight) Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnel's light: " + e.Message); toldLight = true; }
			}
		}
		static bool toldLight;
		/// <summary>Dev probes: leave the linings' light alone.</summary>
		public static bool LiningLightOff;

		void LightLining(SphericalHarmonicsL2 deep, SphericalHarmonicsL2 open)
		{
			if (litRenderer == null)
			{
				Transform r = transform.Find(RockName);
				litRenderer = r != null ? r.GetComponent<MeshRenderer>() : null;
				if (litRenderer == null) return;
			}
			MeshRenderer mr = litRenderer;
			if (mr.lightProbeUsage != LightProbeUsage.CustomProvided) mr.lightProbeUsage = LightProbeUsage.CustomProvided;
			if (lightBlock == null) lightBlock = new MaterialPropertyBlock();
			mr.GetSharedMaterials(litMats);
			// (rim walls dressed as the cave hill: lit as the hill is (CaveLit), not by Raft's open sky - under the sky's light the
			// same rock showed as a bright patterned sand-rock patch round the mouth in the hill's soft grey; Stalker's SW mouth, 11-09)
			SphericalHarmonicsL2 hillSh = default(SphericalHarmonicsL2); float hillShine = 1f;
			bool asHill = !string.IsNullOrEmpty(RimWalls) && CaveLit.HillLight(out hillSh, out hillShine);
			for (int i = 0; i < litMats.Count; i++)
			{
				// (the part's own block: the rock skin is in it)
				mr.GetPropertyBlock(lightBlock, i);
				oneLight[0] = i == 0 ? deep : i == 1 && asHill ? hillSh : open;
				lightBlock.CopySHCoefficientArraysFrom(oneLight);
				if (i == 1 && asHill && litMats[1] != null && litMats[1].HasProperty("_Smoothness_Multiplyer")) lightBlock.SetFloat("_Smoothness_Multiplyer", litMats[1].GetFloat("_Smoothness_Multiplyer") * hillShine);
				// (and less of the sky's shine on the lining: its blue reflections washed the walls out by the mouth)
				if (i == 0 && litMats[0] != null && litMats[0].HasProperty("_Smoothness_Multiplyer")) lightBlock.SetFloat("_Smoothness_Multiplyer", LiningShine);
				// (and darker rock: Raft casts no sun shadow in the passage, so from the mouth its floor was in full sun - only
				// the rock's own colour can take that down; deep in DarkAt dims the sun as well)
				if (i == 0 && litMats[0] != null)
					foreach (string tp in LiningTextures)
						if (litMats[0].HasProperty(tp)) { Texture2D d; Texture src = LiningSource(litMats[0], tp); if (src != null && darkened.TryGetValue(src, out d) && d != null) lightBlock.SetTexture(tp, d); }
				mr.SetPropertyBlock(lightBlock, i);
			}
			// (the renderer's own block too: a part drawn without its own block would be lit by nothing)
			mr.GetPropertyBlock(lightBlock);
			oneLight[0] = open;
			lightBlock.CopySHCoefficientArraysFrom(oneLight);
			mr.SetPropertyBlock(lightBlock);
		}
		[NonSerialized] Vector3 mouthMin, mouthMax;

		/// <summary>The rim's rock over the cut ground: a skin just over the ground (RimSkin), so the cut's square cells are
		/// under rock but its top is the slope's own shape - it stood up to 1.3 m over the slope, a raised blob with a sharp
		/// outline that read as a sticker (11-02); and how high its rocks stand round the opening (RimHeight, in patches).</summary>
		const float RimSkin = 0.12f;
		static float RimHeight(float r) { return 0.3f + 0.25f * r; }
		/// <summary>How far past the cut ground the rim goes down into it (gently, so it meets the ground in a soft line).</summary>
		static float RimOut(float r) { return 0.8f + 0.1f * r; }
		/// <summary>How far past the cut ground the rim's rocks stand (none further: loose shards in the grass, 11-06), and
		/// their highest (0.8 m read as a pipe's end sticking out of the hill, 11-06).</summary>
		const float RimLip = 0.5f, RimRocksTop = 0.45f;

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
			float floor;
			return Distance(segs, p, out floor);
		}

		/// <summary>The passage's distance, and the floor (object space y) of its nearest part.</summary>
		static float Distance(List<Seg> segs, Vector3 p, out float floor)
		{
			float best = float.MaxValue, r = 1f;
			floor = 0f;
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
		/// <summary>Tests: what the rim walls (part 1) wear - the cave hill skin's material (CaveMountain.DressRimWalls); "" when left as the rock.</summary>
		[NonSerialized] public string RimWalls = "";

		void Skin(MeshRenderer mr, Terrain land)
		{
			Material m = RockMaterial();
			// (rock on every part until the rim's top takes the ground's look: an earlier split mesh's top part as well)
			MeshFilter mf = mr.GetComponent<MeshFilter>();
			int parts = mf != null && mf.sharedMesh != null && mf.sharedMesh.subMeshCount > 2 ? 3 : 2;
			mr.sharedMaterials = parts == 3 ? new[] { m, m, m } : new[] { m, m };
			mr.SetPropertyBlock(null); for (int i = 0; i < parts; i++) mr.SetPropertyBlock(null, i);
			ObjectProps.ApplyRockSkin(mr.gameObject); // (Raft's brown island rock, as the cave pieces' "skin=rock")
			// (the rim walls out in the open as the cave hill's skin: its rock and tint - 11-05, Raft's island rock went blue-grey
			// in the shade beside the hill)
			RimWalls = CaveMountain.DressRimWalls(mr) ?? "";
			SideRockOnly(mr, 1);
			GroundTop(mr, land);
			RimMatch = MatchSkin(mr); // (after SplitTop: what it leaves in part 1 is the rim beside the hill's skin)
		}

		/// <summary>Texture pairs of Raft's top/side rock shader: the top one is drawn on faces turned up.</summary>
		static readonly string[][] TopSidePairs = { new[] { "_TopDiffuse", "_SideDiffuse" }, new[] { "_TopNormal", "_SideNormal" }, new[] { "_TopBump", "_SideBump" },
			// (Raft's Mountains shader - Balboa_BigRocks(_Sand), the cave hill's rock: a rock layer and a grass/sand layer blended by the
			// normal, its baked _MainNormal laid on our uvs tipping patches up into sand: the rim walls patterned sand-rock, 11-09;
			// the sand layer over both came out a bright sand patch - the hill's steep faces are its rock layer, runbx12)
			new[] { "_GrassDiffuse", "_RockDiffuse" }, new[] { "_GrassNormal", "_RockNormal" } };

		/// <summary>The rim walls (part 1, since 11-11 only the faces beside a cave hill's skin - SplitTop) wear the rock's side
		/// texture on every face, turned up or not (11-09 w03: Raft's rock shader drew the up-turned faces with its sandy
		/// patterned top texture, a big patch up the cliff over Stalker's Hollow's south-west mouth). By a property block on that
		/// part, so the shared material stays as it is.</summary>
		public static void SideRockOnly(MeshRenderer mr, int part)
		{
			if (mr == null) return;
			Material[] mats = mr.sharedMaterials;
			if (part >= mats.Length || mats[part] == null) return;
			Material m = mats[part];
			var b = new MaterialPropertyBlock();
			mr.GetPropertyBlock(b, part);
			bool any = false;
			foreach (string[] p in TopSidePairs)
			{
				if (!m.HasProperty(p[0]) || !m.HasProperty(p[1])) continue;
				Texture side = b.isEmpty ? null : b.GetTexture(p[1]);
				if (side == null) side = m.GetTexture(p[1]);
				if (side == null) continue;
				b.SetTexture(p[0], side);
				if (m.HasProperty(p[0] + "_ST"))
				{
					Vector2 s = m.GetTextureScale(p[1]), o = m.GetTextureOffset(p[1]);
					b.SetVector(p[0] + "_ST", new Vector4(s.x, s.y, o.x, o.y));
				}
				any = true;
			}
			if (any && m.HasProperty("_GrassTile") && m.HasProperty("_RockTile")) b.SetFloat("_GrassTile", m.GetFloat("_RockTile")); // (the rock at one scale over both layers)
			if (any) mr.SetPropertyBlock(b, part);
		}

		/// <summary>Tests: what MatchSkin last did to the rim walls ("" when not run).</summary>
		[NonSerialized] public string RimMatch = "";
		static bool toldMatch;

		/// <summary>
		/// The rim walls (part 1) shaded as the cave hill's skin round them: each corner's normal blended into the skin's (its
		/// nearby corners', weighted by distance) - the whole way at the rim's outer edge, its own again ~4 m in toward the
		/// opening - and each face mapped as the skin maps it (CaveMountain's Quad: world metres / 14 from the side it faces most,
		/// in the skin's own frame). 11-09 w03: with the skin's material, tint and light the dug mouths at Stalker's Hollow and Lumen
		/// Shelf still sat in a brown patterned patch with a toothed outline in the soft blue-grey rock round them (11-11: that rock
		/// is the ground's own cliff, not the hill's skin - SplitTop now gives those faces the ground's look): Raft's Mountains shader
		/// blends rock and sand by the normal and lays its texture by the uvs, and the rim's own rounded normals and voxel uvs
		/// differ from the skin's smooth heightfield normals and planar uvs. Part 1 gets corners of its own (the lining keeps its);
		/// the rim's own normals are kept in uv channel 3, so a second run (the hill dressing its tunnels again) starts from them.
		/// Run after SplitTop, which leaves in part 1 only the faces beside the hill's skin.
		/// </summary>
		public static string MatchSkin(MeshRenderer mr)
		{
			try
			{
				CaveMountain hill = mr != null ? mr.GetComponentInParent<CaveMountain>() : null;
				MeshRenderer sk = hill != null ? hill.SkinRenderer : null;
				MeshFilter smf = sk != null ? sk.GetComponent<MeshFilter>() : null;
				MeshFilter mf = mr != null ? mr.GetComponent<MeshFilter>() : null;
				Mesh skin = smf != null ? smf.sharedMesh : null, mesh = mf != null ? mf.sharedMesh : null;
				if (skin == null || mesh == null || mesh.subMeshCount < 2) return "";
				Transform rt = mr.transform, st = sk.transform;
				var sv = new List<Vector3>(); var sn = new List<Vector3>(); var su = new List<Vector2>();
				skin.GetVertices(sv); skin.GetNormals(sn); skin.GetUVs(0, su);
				if (sv.Count == 0 || sn.Count != sv.Count || su.Count != sv.Count) return "";
				// (the skin's outside only: CaveMountain.TwoSided adds its faces again turned the other way after them - those
				// normals face into the hill)
				int front = skin.name.EndsWith("(both sides)", StringComparison.Ordinal) ? sv.Count / 2 : sv.Count;
				Vector3 ob = SkinUvOrigin(sv, sn, su, st.position);
				// (the skin's corners in the world, by 2 m squares over x/z)
				const float Bin = 2f, Reach = 8f, Full = 3f, Fade = 4f;
				var bins = new Dictionary<long, List<int>>();
				var sw = new Vector3[front]; var swn = new Vector3[front];
				for (int i = 0; i < front; i++)
				{
					sw[i] = st.TransformPoint(sv[i]); swn[i] = st.TransformDirection(sn[i]).normalized;
					long key = BinKey(sw[i], Bin);
					List<int> l; if (!bins.TryGetValue(key, out l)) bins[key] = l = new List<int>();
					l.Add(i);
				}
				var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var own = new List<Vector3>();
				mesh.GetVertices(v); mesh.GetNormals(n); mesh.GetUVs(0, u);
				if (n.Count != v.Count || u.Count != v.Count) return "";
				// (the rim's own normals, for a second run: kept here, not in a uv channel of the mesh - Raft's rock shaders may
				// read every channel)
				List<Vector3> kept;
				foreach (Mesh gone in rimOwn.Keys.Where(k => k == null).ToList()) rimOwn.Remove(gone);
				if (rimOwn.TryGetValue(mesh, out kept) && kept.Count == v.Count) own = new List<Vector3>(kept);
				else { mesh.GetUVs(3, own); if (own.Count != v.Count) own = new List<Vector3>(n); }
				var parts = new List<int>[mesh.subMeshCount];
				var elsewhere = new bool[v.Count];
				for (int p = 0; p < parts.Length; p++)
				{
					parts[p] = new List<int>(); mesh.GetTriangles(parts[p], p);
					if (p != 1) foreach (int i in parts[p]) elsewhere[i] = true;
				}
				List<int> rim = parts[1];
				var owner = new Dictionary<int, int>();
				int blended = 0, full = 0, none = 0;
				for (int t = 0; t + 2 < rim.Count; t += 3)
				{
					// (corners of its own: one mapping per face, as the skin's)
					for (int e = 0; e < 3; e++)
					{
						int i = rim[t + e], was;
						bool mine = !elsewhere[i] && (!owner.TryGetValue(i, out was) || was == t);
						if (!mine) { int c = v.Count; v.Add(v[i]); n.Add(n[i]); u.Add(u[i]); own.Add(own[i]); rim[t + e] = c; i = c; }
						owner[i] = t;
					}
					Vector3 sum = Vector3.zero;
					var wp = new Vector3[3];
					for (int e = 0; e < 3; e++)
					{
						int i = rim[t + e];
						Vector3 w = rt.TransformPoint(v[i]), o = rt.TransformDirection(own[i]);
						o = o.sqrMagnitude > 1e-12f ? o.normalized : Vector3.up;
						wp[e] = w;
						Vector3 acc = Vector3.zero; float best = float.MaxValue;
						int bx = Mathf.FloorToInt(w.x / Bin), bz = Mathf.FloorToInt(w.z / Bin), r = Mathf.CeilToInt(Reach / Bin);
						for (int dx = -r; dx <= r; dx++)
							for (int dz = -r; dz <= r; dz++)
							{
								List<int> l; if (!bins.TryGetValue(((long)(bx + dx) << 32) ^ (uint)(bz + dz), out l)) continue;
								foreach (int s in l)
								{
									float d = (sw[s] - w).magnitude;
									if (d > Reach) continue;
									best = Mathf.Min(best, d);
									acc += swn[s] / (d * d + 0.25f);
								}
							}
						float k = acc.sqrMagnitude > 1e-12f ? Mathf.Clamp01(1f - (best - Full) / Fade) : 0f;
						Vector3 nn = k > 0f ? Vector3.Slerp(o, acc.normalized, k) : o;
						if (nn.sqrMagnitude < 1e-12f) nn = o;
						if (k > 0f) blended++;
						if (k >= 1f) full++;
						if (acc.sqrMagnitude <= 1e-12f) none++;
						n[i] = rt.InverseTransformDirection(nn).normalized;
						sum += nn;
					}
					// (mapped from the side the corners face most, unless that would stretch the face: CaveMountain.Quad)
					Vector3 pn = Vector3.Cross(wp[1] - wp[0], wp[2] - wp[0]);
					float len = pn.magnitude;
					float[] bs = { Mathf.Abs(sum.x), Mathf.Abs(sum.y), Mathf.Abs(sum.z) }, of = { Mathf.Abs(pn.x), Mathf.Abs(pn.y), Mathf.Abs(pn.z) };
					int axis = of[1] >= of[0] && of[1] >= of[2] ? 1 : of[0] >= of[2] ? 0 : 2;
					for (int pick = 0; pick < 2; pick++)
					{
						int b = -1;
						for (int q = 0; q < 3; q++) if (bs[q] >= 0f && (b < 0 || bs[q] > bs[b])) b = q;
						if (b < 0 || bs[b] <= 0f) break;
						if (of[b] > 0.35f * len) { axis = b; break; }
						bs[b] = -1f;
					}
					for (int e = 0; e < 3; e++)
					{
						Vector3 bw = st.InverseTransformPoint(wp[e]) + ob;
						u[rim[t + e]] = (axis == 1 ? new Vector2(bw.x, bw.z) : axis == 0 ? new Vector2(bw.z, bw.y) : new Vector2(bw.x, bw.y)) / 14f;
					}
				}
				if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
				mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, u);
				mesh.uv4 = null; rimOwn[mesh] = own;
				for (int p = 0; p < parts.Length; p++) mesh.SetTriangles(parts[p], p);
				string said = "rim walls shaded as the hill's skin: " + blended + " of " + rim.Count + " corners blended, " + full + " fully, " + none + " with no skin near";
				if (!toldMatch) Debug.Log("[CUSTOM ISLANDS] Dug tunnel's " + said);
				toldMatch = true;
				return said;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnel's rim (skin match): " + e.Message); return ""; }
		}

		static readonly Dictionary<Mesh, List<Vector3>> rimOwn = new Dictionary<Mesh, List<Vector3>>();

		static long BinKey(Vector3 w, float bin) { return ((long)Mathf.FloorToInt(w.x / bin) << 32) ^ (uint)Mathf.FloorToInt(w.z / bin); }

		/// <summary>Where the skin's mapping starts (CaveMountain.Quad maps the world at its build, the skin set at its origin):
		/// read back from a flat corner (x, z) and a corner facing x or z (y); else the skin's place now.</summary>
		static Vector3 SkinUvOrigin(List<Vector3> sv, List<Vector3> sn, List<Vector2> su, Vector3 now)
		{
			Vector3 ob = now;
			bool xz = false;
			for (int i = 0; i < sv.Count && !xz; i++)
				if (sn[i].y > 0.97f) { ob.x = su[i].x * 14f - sv[i].x; ob.z = su[i].y * 14f - sv[i].z; xz = true; }
			if (!xz) return now;
			for (int i = 0; i < sv.Count; i++)
			{
				if (Mathf.Abs(sn[i].z) > 0.97f && Mathf.Abs(su[i].x * 14f - sv[i].x - ob.x) < 0.05f) { ob.y = su[i].y * 14f - sv[i].y; break; }
				if (Mathf.Abs(sn[i].x) > 0.97f && Mathf.Abs(su[i].x * 14f - sv[i].z - ob.z) < 0.05f) { ob.y = su[i].y * 14f - sv[i].y; break; }
			}
			return ob;
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

		/// <summary>The rim's top (its faces turned up, split off the rim's part as the lining's third part) wears the ground's
		/// own look: the terrain's shader with its splat map and layers, laid on by where it lies over the ground - the same
		/// textures, mix and light as the ground round it, so the rim melts into the slope (11-02: Raft's rock shader with the
		/// main layer's texture on top came out darker and greener than the ground beside it, a patch with a sharp outline).
		/// Its steeper sides stay rock: bare rock showing round the opening.</summary>
		void GroundTop(MeshRenderer mr, Terrain land)
		{
			RimTop = "";
			MeshFilter mf = mr.GetComponent<MeshFilter>();
			Mesh mesh = mf != null ? mf.sharedMesh : null;
			if (land == null || RimColumns == 0 || mesh == null || mesh.subMeshCount < 2) return;
			// (11-02: in a world - Raft's camera draws deferred there - the terrain shader on a mesh came out a flat grey-blue
			// patch, its textures set and lit or not; the editor's is right. TN2, 11-03: in a world the top wears the ground's
			// look baked into a texture of its own - the layers mixed by the splat map - on the legacy diffuse shader (Standard
			// until 11-13: its shine laid a grey veil on it), which draws in the deferred camera as the ground does.)
			bool world = !DynamicIslands.InEditor();
			try
			{
				Material ground = world ? null : GroundMaterial(land);
				if (!world && ground == null) { if (!toldNoGround) Debug.Log("[CUSTOM ISLANDS] Dug tunnel's rim: the ground's shader (" + (land.materialTemplate != null ? land.materialTemplate.shader.name : "built-in") + ") is not a terrain shader; its top stays rock"); toldNoGround = true; return; }
				if (!SplitTop(mesh, mr.transform, land, Openings)) return;
				if (world)
				{
					topRect = TopRect(mesh, Openings, land);
					bakedLimit = ReadLimit();
					ground = BakedGround(land, topRect, ref bakedTop, true);
					if (ground == null) { if (!toldDeferred) Debug.Log("[CUSTOM ISLANDS] Dug tunnel's rim: the ground has no layers to bake; its top stays rock"); toldDeferred = true; return; }
				}
				Material[] mats = mr.sharedMaterials;
				mr.sharedMaterials = new[] { mats[0], mats[1], ground };
				RimTop = world ? "the ground's baked (" + ground.shader.name + ", " + bakedTop.mainTexture.width + "x" + bakedTop.mainTexture.height + (bakedTop.HasProperty("_BumpMap") && bakedTop.GetTexture("_BumpMap") != null ? ", normals" : bakedTop.mainTexture.name == ReliefName ? ", relief" : ", flat") + ")" : "the ground's own (" + ground.shader.name + ")";
				Dressed(land);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnel's rim: " + e.Message); }
		}

		/// <summary>Moves the rim's faces (part 1) to a third part of their own, with their own corners, and lays those corners
		/// on the ground's splat map (uv: where they are over the terrain, 0..1), lit as the ground there - all but the faces
		/// beside a cave hill's skin (NearHillSkin), which keep the hill's rock. False when there are none.</summary>
		static bool SplitTop(Mesh mesh, Transform at, Terrain land, List<Vector2> cut = null)
		{
			var verts = new List<Vector3>();
			var norms = new List<Vector3>();
			var uvs = new List<Vector2>();
			var tans = new List<Vector4>();
			mesh.GetVertices(verts);
			mesh.GetNormals(norms);
			mesh.GetUVs(0, uvs);
			mesh.GetTangents(tans);
			if (tans.Count != verts.Count) tans.Clear();
			if (mesh.subMeshCount == 2)
			{
				var rim = new List<int>();
				var top = new List<int>();
				var side = new List<int>();
				mesh.GetTriangles(rim, 1);
				var copy = new Dictionary<int, int>();
				Func<Vector3, bool> nearSkin = NearHillSkin(at);
				var lining = new List<int>();
				mesh.GetTriangles(lining, 0);
				// (11-13: the lining's floor at the foot of each mouth lay out under the open sky, in full sun - a pale grey lip
				// and ring of Raft's rock round the opening, brightest at its foot, on Cinderfall's dark ash: those faces wear the
				// ground's look too, as the ground running in)
				bool[] lip = OpenLining(verts, lining, rim, at, land, cut, norms);
				var linCopy = new Dictionary<int, int>();
				var keep = new List<int>(lining.Count);
				for (int i = 0; i + 2 < lining.Count; i += 3)
				{
					if (!lip[i / 3]) { keep.Add(lining[i]); keep.Add(lining[i + 1]); keep.Add(lining[i + 2]); continue; }
					for (int e = 0; e < 3; e++)
					{
						int v = lining[i + e], to;
						if (!linCopy.TryGetValue(v, out to))
						{
							to = verts.Count;
							linCopy[v] = to;
							verts.Add(verts[v]);
							norms.Add(norms[v]);
							uvs.Add(uvs[v]);
							if (tans.Count > 0) tans.Add(tans[v]);
						}
						top.Add(to);
					}
				}
				lining = keep;
				for (int i = 0; i + 2 < rim.Count; i += 3)
				{
					// (every face fills the ground's cut cells in the ground's place - 11-11: by their own normals and slope, the
					// faces over a steep face stayed rock, Raft's hill rock in a dark brown or tan patterned patch with a toothed
					// outline round each mouth in Stalker's Hollow's and Lumen Shelf's cliffs - the cliff there is the ground's own,
					// drawn by its shader from the splat map stretched down the slope, which the baked top, laid on the same map from
					// above and lit by the ground's normals, matches; the hill's rock never did. Only beside a cave hill's skin the rim
					// keeps the hill's rock)
					Vector3 wa = at.TransformPoint(verts[rim[i]]), wb = at.TransformPoint(verts[rim[i + 1]]), wc = at.TransformPoint(verts[rim[i + 2]]);
					bool up = nearSkin == null || !nearSkin((wa + wb + wc) / 3f);
					if (!up) { side.Add(rim[i]); side.Add(rim[i + 1]); side.Add(rim[i + 2]); continue; }
					for (int e = 0; e < 3; e++)
					{
						int v = rim[i + e], to;
						if (!copy.TryGetValue(v, out to))
						{
							to = verts.Count;
							copy[v] = to;
							verts.Add(verts[v]);
							norms.Add(norms[v]);
							uvs.Add(uvs[v]);
							if (tans.Count > 0) tans.Add(tans[v]);
						}
						top.Add(to);
					}
				}
				if (top.Count == 0) return false;
				if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
				mesh.SetVertices(verts);
				mesh.SetNormals(norms);
				mesh.subMeshCount = 3;
				mesh.SetTriangles(lining, 0);
				mesh.SetTriangles(side, 1);
				mesh.SetTriangles(top, 2);
			}
			// (where each of the top's corners lies over the ground)
			var tops = new List<int>();
			mesh.GetTriangles(tops, 2);
			Vector3 lo = land.transform.position, size = land.terrainData.size;
			// (and turned as the ground there is: lit as the slope beside it, not as the rim's own rounded shape - in a world
			// its outline showed as a lighter patch, TN2 11-03. The lining's floor run out of the mouth too - 11-13: flat, facing
			// the sun, it was a pale crescent at the foot of The Frontier's quarry face, 24 lighter than the ground beside it)
			if (norms.Count != verts.Count) { norms.Clear(); mesh.GetNormals(norms); }
			if (tans.Count != verts.Count) { tans.Clear(); mesh.GetTangents(tans); }
			foreach (int v in tops)
			{
				Vector3 w = at.TransformPoint(verts[v]);
				uvs[v] = new Vector2((w.x - lo.x) / size.x, (w.z - lo.z) / size.z);
				if (v < norms.Count) norms[v] = at.InverseTransformDirection(land.terrainData.GetInterpolatedNormal(uvs[v].x, uvs[v].y)).normalized;
				// (turned for the baked ground's normal map as the terrain turns its own: along x, its binormal along z)
				if (v < tans.Count && v < norms.Count)
				{
					Vector3 n = at.TransformDirection(norms[v]);
					Vector3 t = Vector3.Cross(n, Vector3.forward);
					t = t.sqrMagnitude > 1e-4f ? t.normalized : Vector3.right;
					t = at.InverseTransformDirection(t).normalized;
					tans[v] = new Vector4(t.x, t.y, t.z, -1f);
				}
			}
			mesh.SetUVs(0, uvs);
			if (norms.Count == verts.Count) mesh.SetNormals(norms);
			if (tans.Count == verts.Count) mesh.SetTangents(tans);
			return tops.Count > 0;
		}

		/// <summary>How far under the ground (m) the lining's floor at a mouth still takes the ground's look (OpenLining).</summary>
		const float LipDepth = 2f;

		/// <summary>Which of the lining's faces (part 0, by face) lie at the foot of a mouth under the open sky: over or round
		/// the ground's cut cells (cut: their centres, world x and z), turned up, at most LipDepth under the ground there, and
		/// with nothing of the tunnel's own (lining or rim) over them - the floor running out of the opening, in full sun.</summary>
		static bool[] OpenLining(List<Vector3> verts, List<int> lining, List<int> rim, Transform at, Terrain land, List<Vector2> cut, List<Vector3> norms)
		{
			var lip = new bool[lining.Count / 3];
			if (land == null || land.terrainData == null || cut == null || cut.Count == 0 || at == null) return lip;
			float half = 0.5f * land.terrainData.size.x / Mathf.Max(1, land.terrainData.holesResolution) + 0.75f;
			// (every face of the tunnel, binned by where it lies (x, z), for the look straight up)
			const float Bin = 1f;
			var world = new Vector3[verts.Count];
			for (int i = 0; i < verts.Count; i++) world[i] = at.TransformPoint(verts[i]);
			var bins = new Dictionary<long, List<int>>();
			var all = new List<int>(lining.Count + rim.Count);
			all.AddRange(lining); all.AddRange(rim);
			for (int f = 0; f + 2 < all.Count; f += 3)
			{
				Vector3 a = world[all[f]], b = world[all[f + 1]], c = world[all[f + 2]];
				int bx0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / Bin), bx1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / Bin);
				int bz0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / Bin), bz1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / Bin);
				for (int bx = bx0; bx <= bx1; bx++)
					for (int bz = bz0; bz <= bz1; bz++)
					{
						long key = ((long)bx << 32) ^ (uint)bz;
						List<int> l; if (!bins.TryGetValue(key, out l)) bins[key] = l = new List<int>();
						l.Add(f);
					}
			}
			for (int i = 0; i + 2 < lining.Count; i += 3)
			{
				Vector3 a = world[lining[i]], b = world[lining[i + 1]], c = world[lining[i + 2]];
				Vector3 mid = (a + b + c) / 3f;
				if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-8f) continue;
				Vector3 n = norms.Count == verts.Count ? at.TransformDirection(norms[lining[i]] + norms[lining[i + 1]] + norms[lining[i + 2]]).normalized : Vector3.up;
				if (n.y < 0.2f || mid.y < GroundY(land, mid) - LipDepth) continue;
				bool inCut = false;
				foreach (Vector2 q in cut) if (Mathf.Abs(mid.x - q.x) <= half && Mathf.Abs(mid.z - q.y) <= half) { inCut = true; break; }
				if (!inCut) continue;
				// (nothing of the tunnel over it: the look straight up meets none of its faces)
				bool covered = false;
				List<int> near;
				if (bins.TryGetValue(((long)Mathf.FloorToInt(mid.x / Bin) << 32) ^ (uint)Mathf.FloorToInt(mid.z / Bin), out near))
					foreach (int f in near)
					{
						if (f == i) continue;
						Vector3 p = world[all[f]], q = world[all[f + 1]], r = world[all[f + 2]];
						float d = (q.z - r.z) * (p.x - r.x) + (r.x - q.x) * (p.z - r.z);
						if (Mathf.Abs(d) < 1e-6f) continue;
						float u = ((q.z - r.z) * (mid.x - r.x) + (r.x - q.x) * (mid.z - r.z)) / d;
						float v = ((r.z - p.z) * (mid.x - r.x) + (p.x - r.x) * (mid.z - r.z)) / d;
						if (u < 0f || v < 0f || u + v > 1f) continue;
						if (u * p.y + v * q.y + (1f - u - v) * r.y > mid.y + 0.1f) { covered = true; break; }
					}
				lip[i / 3] = !covered;
			}
			return lip;
		}

		/// <summary>How close to a cave hill's skin a rim face stays the hill's rock (SplitTop).</summary>
		const float NearSkin = 1.5f;

		/// <summary>Whether a point (world) lies within NearSkin of the cave hill's skin round this tunnel (its outside); null
		/// when there is no hill.</summary>
		static Func<Vector3, bool> NearHillSkin(Transform at)
		{
			CaveMountain hill = at != null ? at.GetComponentInParent<CaveMountain>() : null;
			MeshRenderer sk = hill != null ? hill.SkinRenderer : null;
			MeshFilter smf = sk != null ? sk.GetComponent<MeshFilter>() : null;
			Mesh skin = smf != null ? smf.sharedMesh : null;
			if (skin == null) return null;
			var sv = new List<Vector3>();
			skin.GetVertices(sv);
			// (CaveMountain.TwoSided: the outside's corners, then the same again for the inside)
			int front = skin.name.EndsWith("(both sides)", StringComparison.Ordinal) ? sv.Count / 2 : sv.Count;
			Transform st = sk.transform;
			const float Bin = 2f;
			var bins = new Dictionary<long, List<Vector3>>();
			for (int i = 0; i < front; i++)
			{
				Vector3 w = st.TransformPoint(sv[i]);
				long key = BinKey(w, Bin);
				List<Vector3> l; if (!bins.TryGetValue(key, out l)) bins[key] = l = new List<Vector3>();
				l.Add(w);
			}
			if (bins.Count == 0) return null;
			return p =>
			{
				int bx = Mathf.FloorToInt(p.x / Bin), bz = Mathf.FloorToInt(p.z / Bin);
				for (int dx = -1; dx <= 1; dx++)
					for (int dz = -1; dz <= 1; dz++)
					{
						List<Vector3> l; if (!bins.TryGetValue(((long)(bx + dx) << 32) ^ (uint)(bz + dz), out l)) continue;
						foreach (Vector3 w in l) if ((w - p).sqrMagnitude < NearSkin * NearSkin) return true;
					}
				return false;
			};
		}

		static bool toldNoGround, toldDeferred;

		/// <summary>In a world: the rim top's own material and the ground baked for it, and where that lies on the terrain (0..1).</summary>
		[NonSerialized] Material bakedTop;
		/// <summary>The texture limit bakedTop was baked under (ReadLimit).</summary>
		[NonSerialized] int bakedLimit;
		[NonSerialized] Rect topRect;

		void OnDestroy()
		{
			if (bakedTop == null) return;
			DropBaked(bakedTop);
		}

		/// <summary>Where the rim's top (part 2) lies over the terrain: its corners' uvs (SplitTop lays them 0..1 over the
		/// terrain), with half a metre's margin - and of that only round the ground's cut cells (cut: their centres, world x
		/// and z), the only place it shows: the rest lies under the ground. (11-13: the whole top, mostly under the ground, took
		/// 30-60 m a side, so the baked texture's BakeMax pixels came to a third of the ground's sharpness or less - a blurred
		/// sheet over each mouth, smeared down The Frontier's quarry face.)</summary>
		static Rect TopRect(Mesh mesh, List<Vector2> cut = null, Terrain land = null)
		{
			var uvs = new List<Vector2>();
			var tops = new List<int>();
			mesh.GetUVs(0, uvs);
			mesh.GetTriangles(tops, 2);
			float x0 = 1f, z0 = 1f, x1 = 0f, z1 = 0f;
			foreach (int v in tops) { Vector2 u = uvs[v]; x0 = Mathf.Min(x0, u.x); x1 = Mathf.Max(x1, u.x); z0 = Mathf.Min(z0, u.y); z1 = Mathf.Max(z1, u.y); }
			if (x1 < x0) return new Rect(0f, 0f, 1f, 1f);
			const float m = 0.002f;
			if (cut != null && cut.Count > 0 && land != null)
			{
				// (the cut cells' own rect, with TopMargin round them for the faces that rise over the ground beside a hole)
				Vector3 lo = land.transform.position, size = land.terrainData.size;
				float cx0 = float.MaxValue, cz0 = float.MaxValue, cx1 = float.MinValue, cz1 = float.MinValue;
				foreach (Vector2 q in cut) { cx0 = Mathf.Min(cx0, q.x); cx1 = Mathf.Max(cx1, q.x); cz0 = Mathf.Min(cz0, q.y); cz1 = Mathf.Max(cz1, q.y); }
				float half = 0.5f * size.x / Mathf.Max(1, land.terrainData.holesResolution) + TopMargin;
				x0 = Mathf.Max(x0, (cx0 - half - lo.x) / size.x); x1 = Mathf.Min(x1, (cx1 + half - lo.x) / size.x);
				z0 = Mathf.Max(z0, (cz0 - half - lo.z) / size.z); z1 = Mathf.Min(z1, (cz1 + half - lo.z) / size.z);
				if (x1 < x0 || z1 < z0) return new Rect(0f, 0f, 1f, 1f);
			}
			return Rect.MinMaxRect(x0 - m, z0 - m, x1 + m, z1 + m);
		}

		/// <summary>Pixels per metre of the baked ground and the layers' copies, as the ground's own (a grass layer's 1024 pixels
		/// over 8 m), up to BakeMax a side. (11-03: 48 was a third of that, so the top came out a blurred, smoother patch than
		/// the ground round it, its grass blades gone.)</summary>
		const float BakePerMetre = 128f;

		/// <summary>How far round the ground's cut cells the baked top reaches (TopRect).</summary>
		const float TopMargin = 2f;
		const int BakeMax = 2048;

		/// <summary>
		/// The ground's look over part of the terrain (r: 0..1 over it) baked into a texture: each layer's diffuse texture
		/// tiled as the terrain tiles it, mixed by the splat map's weights there - on the legacy diffuse shader, as the
		/// ground's (no shine), which the world's deferred camera lights as it lights the ground. With normals: the layers'
		/// normal maps mixed the same way into a normal map of its own, on a bumped shader without shine (BumpedShader; the mesh's
		/// tangents along x, as the terrain's - SplitTop). The layers' textures can't be read by the mod: each is
		/// copied back from the graphics card once (Graphics.Blit to a small RenderTexture). mat: made once, its textures
		/// replaced. Null when the ground has no layers.
		/// </summary>
		internal static Material BakedGround(Terrain land, Rect r, ref Material mat, bool normals = false)
		{
			TerrainData td = land != null ? land.terrainData : null;
			TerrainLayer[] layers = td != null ? td.terrainLayers : null;
			if (layers == null || layers.Length == 0 || r.width <= 0f || r.height <= 0f) return null;
			Vector3 size = td.size;
			int tw = Mathf.Clamp(Mathf.CeilToInt(r.width * size.x * BakePerMetre), 16, BakeMax), th = Mathf.Clamp(Mathf.CeilToInt(r.height * size.z * BakePerMetre), 16, BakeMax);
			int ar = td.alphamapResolution;
			int ax0 = Mathf.Clamp(Mathf.FloorToInt(r.xMin * ar - 1f), 0, ar - 1), az0 = Mathf.Clamp(Mathf.FloorToInt(r.yMin * ar - 1f), 0, ar - 1);
			int ax1 = Mathf.Clamp(Mathf.CeilToInt(r.xMax * ar + 1f), ax0 + 1, ar), az1 = Mathf.Clamp(Mathf.CeilToInt(r.yMax * ar + 1f), az0 + 1, ar);
			float[,,] w = td.GetAlphamaps(ax0, az0, ax1 - ax0, az1 - az0);
			int nl = Mathf.Min(layers.Length, w.GetLength(2));
			var pix = new Color32[nl][];
			var pw = new int[nl];
			var ph = new int[nl];
			// (the layers' normal maps, read as stored - not as colours)
			var npix = new Color32[nl][];
			var nw = new int[nl];
			var nh = new int[nl];
			bool anyNormal = false;
			// (the bumped look's shader; without it - Raft's build has none - the normal maps' relief is baked into the colour)
			Shader bumped = !normals ? null : TestLook == 1 ? Shader.Find("Standard") : TestLook == 2 ? null : BumpedShader();
			for (int l = 0; l < nl; l++)
			{
				TerrainLayer tl = layers[l];
				Texture d = tl != null ? tl.diffuseTexture : null;
				if (d == null) continue;
				int want = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Max(tl.tileSize.x, tl.tileSize.y) * BakePerMetre)), 32, Mathf.Min(BakeMax, Mathf.Max(d.width, d.height)));
				Texture2D c = Readable(d, want);
				if (c == null) continue;
				pix[l] = c.GetPixels32(); pw[l] = c.width; ph[l] = c.height;
				// (without a bumped shader the normal maps still count: their relief is shaded into the colour - Relief)
				Texture nm = normals ? tl.normalMapTexture : null;
				if (nm == null) continue;
				int wantN = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(Mathf.Max(tl.tileSize.x, tl.tileSize.y) * BakePerMetre)), 32, Mathf.Min(BakeMax, Mathf.Max(nm.width, nm.height)));
				Texture2D cn = Readable(nm, wantN, true);
				if (cn == null) continue;
				npix[l] = cn.GetPixels32(); nw[l] = cn.width; nh[l] = cn.height;
				anyNormal = true;
			}
			var outPix = new Color32[tw * th];
			Color32[] outNrm = anyNormal ? new Color32[tw * th] : null;
			int wz = w.GetLength(0), wx = w.GetLength(1);
			for (int j = 0; j < th; j++)
				for (int i = 0; i < tw; i++)
				{
					float u = r.xMin + (i + 0.5f) / tw * r.width, v = r.yMin + (j + 0.5f) / th * r.height;
					// (the splat map's weights there, bilinear between its texels' centres)
					float fx = Mathf.Clamp(u * ar - 0.5f - ax0, 0f, wx - 1), fz = Mathf.Clamp(v * ar - 0.5f - az0, 0f, wz - 1);
					int x0 = Mathf.Min((int)fx, wx - 1), z0 = Mathf.Min((int)fz, wz - 1), x1 = Mathf.Min(x0 + 1, wx - 1), z1 = Mathf.Min(z0 + 1, wz - 1);
					float tx = fx - x0, tz = fz - z0;
					float cr = 0f, cg = 0f, cb = 0f, sum = 0f;
					float nx = 0f, ny = 0f, nz = 0f;
					float lx = u * size.x, lz = v * size.z;
					for (int l = 0; l < nl; l++)
					{
						if (pix[l] == null) continue;
						float wt = Mathf.Lerp(Mathf.Lerp(w[z0, x0, l], w[z0, x1, l], tx), Mathf.Lerp(w[z1, x0, l], w[z1, x1, l], tx), tz);
						if (wt <= 0.001f) continue;
						TerrainLayer tl = layers[l];
						float su = (lx + tl.tileOffset.x) / Mathf.Max(0.01f, tl.tileSize.x), sv = (lz + tl.tileOffset.y) / Mathf.Max(0.01f, tl.tileSize.y);
						int px = ((int)Mathf.Floor(su * pw[l]) % pw[l] + pw[l]) % pw[l], py = ((int)Mathf.Floor(sv * ph[l]) % ph[l] + ph[l]) % ph[l];
						Color32 c = pix[l][py * pw[l] + px];
						cr += c.r * wt; cg += c.g * wt; cb += c.b * wt; sum += wt;
						if (outNrm == null) continue;
						// (as the terrain's shader mixes them: each unpacked - x from red times alpha, the DXT5nm way or plain -
						// scaled by the layer's normal scale, weighted; a layer without one counts as flat)
						if (npix[l] == null) { nz += wt; continue; }
						int qx = ((int)Mathf.Floor(su * nw[l]) % nw[l] + nw[l]) % nw[l], qy = ((int)Mathf.Floor(sv * nh[l]) % nh[l] + nh[l]) % nh[l];
						Color32 q = npix[l][qy * nw[l] + qx];
						float ex = (q.r * q.a / 65025f * 2f - 1f) * tl.normalScale, ey = (q.g / 255f * 2f - 1f) * tl.normalScale;
						float ez = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Clamp01(ex * ex + ey * ey)));
						nx += ex * wt; ny += ey * wt; nz += ez * wt;
					}
					if (sum <= 0f) { outPix[j * tw + i] = new Color32(128, 128, 128, 255); if (outNrm != null) outNrm[j * tw + i] = new Color32(128, 128, 255, 255); continue; }
					outPix[j * tw + i] = new Color32((byte)Mathf.Clamp(cr / sum, 0f, 255f), (byte)Mathf.Clamp(cg / sum, 0f, 255f), (byte)Mathf.Clamp(cb / sum, 0f, 255f), 255);
					if (outNrm == null) continue;
					float len = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
					if (len < 1e-5f) { outNrm[j * tw + i] = new Color32(128, 128, 255, 255); continue; }
					nx /= len; ny /= len; nz /= len;
					outNrm[j * tw + i] = new Color32((byte)Mathf.Clamp(nx * 127.5f + 127.5f, 0f, 255f), (byte)Mathf.Clamp(ny * 127.5f + 127.5f, 0f, 255f), (byte)Mathf.Clamp(nz * 127.5f + 127.5f, 0f, 255f), 255);
				}
			// (no bumped shader in the game: the normal maps' relief shaded into the colour instead, the top on plain diffuse)
			bool relief = bumped == null && outNrm != null;
			if (relief) { Relief(outPix, outNrm, TestLook == 2); outNrm = null; }
			Texture tex = Upload(outPix, tw, th, relief ? ReliefName : "DigTunnelGroundBaked", false);
			Texture ntex = outNrm != null ? Upload(outNrm, tw, th, "DigTunnelGroundBakedNormal", true) : null;
			// (made for the other look - with or without normals: made again)
			if (mat != null && (ntex != null) != mat.HasProperty("_BumpMap")) { DropBaked(mat); mat = null; }
			if (mat == null)
			{
				// (11-13: the legacy diffuse shader, lit as the ground's own Nature/Terrain/Diffuse is - no shine. On Standard,
				// even matt, the deferred camera added the sky's grey reflection: round every mouth a pale grey veil over the
				// dark ash, its albedo hardly mattering - Cinderfall's lava tubes, The Frontier's quarry. Its bumped twin with
				// the layers' normal maps, 11-13: flat, the top was a soft pale sheet on The Frontier's quarry face beside the
				// ground's crisp, blue-shadowed rock)
				Shader s = (TestLook == 2 ? Shader.Find("Standard") : null) ?? (ntex != null ? bumped : null) ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse") ?? Shader.Find("Standard");
				if (s == null) { Destroy(tex); if (ntex != null) Destroy(ntex); return null; }
				mat = new Material(s) { name = "DigTunnelGroundBaked" };
				mat.SetFloat("_Glossiness", 0f);
				mat.SetFloat("_GlossMapScale", 0f);
				mat.SetFloat("_Metallic", 0f);
				mat.SetFloat("_SpecularHighlights", 0f);
				mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
				mat.SetFloat("_GlossyReflections", 0f);
				mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
				// (Standard's specular twin with a black specular colour: no shine at all, in the deferred camera's reflections
				// too - they take the specular colour from the G-buffer, black there)
				if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", Color.black);
				if (s.name.StartsWith("Standard", StringComparison.Ordinal) && ntex != null) mat.EnableKeyword("_NORMALMAP");
			}
			else
			{
				if (mat.mainTexture != null) Destroy(mat.mainTexture);
				if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null) Destroy(mat.GetTexture("_BumpMap"));
			}
			mat.mainTexture = tex;
			var scale = new Vector2(1f / r.width, 1f / r.height);
			var offset = new Vector2(-r.xMin / r.width, -r.yMin / r.height);
			mat.mainTextureScale = scale;
			mat.mainTextureOffset = offset;
			if (mat.HasProperty("_BumpMap"))
			{
				mat.SetTexture("_BumpMap", ntex);
				mat.SetTextureScale("_BumpMap", scale);
				mat.SetTextureOffset("_BumpMap", offset);
			}
			else if (ntex != null) Destroy(ntex);
			return mat;
		}

		/// <summary>The baked top's shader with normal maps and no shine, the first of these Raft has (11-13: Raft's build has no
		/// legacy bumped diffuse - the top stayed flat); null when none.</summary>
		static Shader BumpedShader()
		{
			if (bumpedLooked) return bumpedShader;
			bumpedLooked = true;
			foreach (string n in new[] { "Legacy Shaders/Bumped Diffuse", "Bumped Diffuse", "Mobile/Bumped Diffuse", "Standard (Specular setup)" })
			{
				Shader s = Shader.Find(n);
				if (s != null && s.isSupported) return bumpedShader = s;
			}
			return null;
		}
		static Shader bumpedShader;
		static bool bumpedLooked;

		/// <summary>The baked top's texture name when its normal maps' relief is shaded into its colour.</summary>
		internal const string ReliefName = "DigTunnelGroundBakedRelief";

		/// <summary>A baked texture on the card at its full size, with mip maps: a render texture. The texture limit
		/// (QualitySettings.masterTextureLimit, Raft's texture quality) drops the top mip levels of every texture with mip maps,
		/// the mod's made ones too - at limit 3 a 2024 px top showed 253 px, the rim soft beside the ground (11-13). Render
		/// textures are left whole; the pixels go up once without mip maps (no limit on those), the card makes the mips.</summary>
		static Texture Upload(Color32[] pix, int w, int h, string name, bool linear)
		{
			var flat = new Texture2D(w, h, TextureFormat.RGBA32, false, linear) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
			flat.SetPixels32(pix);
			flat.Apply(false, true);
			var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB)
				{ name = name, useMipMap = true, autoGenerateMips = true, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
			if (!rt.Create()) { Destroy(rt); flat.name = name; flat.filterMode = FilterMode.Trilinear; flat.anisoLevel = 4; return flat; }
			RenderTexture prev = RenderTexture.active;
			Graphics.Blit(flat, rt);
			RenderTexture.active = prev;
			Destroy(flat);
			return rt;
		}

		/// <summary>11-13: Raft's build has no bumped shader without shine, so the top on the legacy diffuse shader came out
		/// soft beside the ground, whose normal maps draw the cracks and pebbles in light and shade (CIDigMouthLook's
		/// sharpness 0.5 against 2.8 on Cinderfall). The mixed normal maps' relief is shaded into the colour instead: lit
		/// from high up (the sun's side when there is one, 55 degrees up) half and half with light from all round, each
		/// texel against the mean of all - the cracks dark, the mean colour as baked.</summary>
		/// <summary>Test only (CIDigMouthLook's comparisons): 1 - the top on Standard with its baked normal map; 2 - on Standard,
		/// the relief shaded in against flat ground (absolute) rather than its mean. 0: the game's look.</summary>
		internal static int TestLook;

		/// <summary>Test only: the rim top baked again in a look to compare (TestLook), a material of its own; null when none.</summary>
		internal Material BakeForTest(int look)
		{
			Material m = null;
			int was = TestLook;
			TestLook = look;
			try { return BakedGround(skinLand, topRect, ref m, true); }
			finally { TestLook = was; }
		}

		/// <summary>Test only: a material BakeForTest made, with its textures.</summary>
		internal static void DropTestBake(Material m) { if (m != null) DropBaked(m); }

		static void Relief(Color32[] pix, Color32[] nrm, bool absolute = false)
		{
			Vector3 l = new Vector3(0f, 0f, 1f);
			Light sun = RenderSettings.sun;
			if (sun != null && sun.type == LightType.Directional)
			{
				Vector3 to = -sun.transform.forward;
				var flat = new Vector2(to.x, to.z);
				if (flat.sqrMagnitude > 1e-4f) { flat.Normalize(); float c = Mathf.Cos(55f * Mathf.Deg2Rad); l = new Vector3(flat.x * c, flat.y * c, Mathf.Sin(55f * Mathf.Deg2Rad)); }
			}
			var f = new float[pix.Length];
			double sum = 0;
			for (int k = 0; k < pix.Length; k++)
			{
				float x = nrm[k].r / 127.5f - 1f, y = nrm[k].g / 127.5f - 1f, z = nrm[k].b / 127.5f - 1f;
				float s = 0.5f * (0.5f + 0.5f * z) + 0.5f * Mathf.Max(0f, x * l.x + y * l.y + z * l.z);
				f[k] = s; sum += s;
			}
			float mean = (float)(sum / Mathf.Max(1, pix.Length));
			// (absolute: against a flat normal's shading - the relief only darkens/lightens as the light on bumpy ground would)
			if (absolute) mean = 0.5f + 0.5f * Mathf.Max(0f, l.z);
			if (mean < 1e-3f) return;
			for (int k = 0; k < pix.Length; k++)
			{
				float s = Mathf.Clamp(f[k] / mean, 0.35f, 1.6f);
				Color32 c = pix[k];
				pix[k] = new Color32((byte)Mathf.Clamp(c.r * s, 0f, 255f), (byte)Mathf.Clamp(c.g * s, 0f, 255f), (byte)Mathf.Clamp(c.b * s, 0f, 255f), 255);
			}
		}

		/// <summary>Drops a baked material with its textures.</summary>
		static void DropBaked(Material m)
		{
			if (m == null) return;
			if (m.mainTexture != null) Destroy(m.mainTexture);
			if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) Destroy(m.GetTexture("_BumpMap"));
			Destroy(m);
		}

		static readonly Dictionary<KeyValuePair<Texture, int>, Texture2D> readable = new Dictionary<KeyValuePair<Texture, int>, Texture2D>();
		static readonly Dictionary<KeyValuePair<Texture, int>, Texture2D> readableLinear = new Dictionary<KeyValuePair<Texture, int>, Texture2D>();
		
		/// <summary>The texture limit (QualitySettings.masterTextureLimit) the copies were read under: the card holds every
		/// game texture that many mip steps smaller, and a copy read from it is that soft. 11-13: lifting the limit for each
		/// read (302115ef, 003033a2) made Unity load every texture of the game again at full size, each time - a Raft building
		/// an island with dens and dug mouths grew to 12-23 GB and hung. Now never touched: a top baked under a limit is baked
		/// again when the limit comes down (Update), the copies cached per limit.</summary>
		/// <summary>Tests (CIMemory): the readable copies cached, how many and their size.</summary>
		internal static string CacheReport()
		{
			long b = 0; int n = 0;
			foreach (var d in new[] { readable, readableLinear }) foreach (Texture2D t in d.Values) if (t != null) { n++; b += (long)t.width * t.height * 4; }
			return "readable copies " + n + " (" + (b >> 20) + " MB)";
		}

		static int ReadLimit() { return Mathf.Clamp(QualitySettings.masterTextureLimit, 0, 7); }

		/// <summary>A copy of a texture the mod can read, size x size: drawn into a RenderTexture (halved step by step, so
		/// nothing is skipped) and read back. Cached. linear: the values as stored, not as colours (a normal map).</summary>
		internal static Texture2D Readable(Texture src, int size, bool linear = false)
		{
			var key = new KeyValuePair<Texture, int>(src, size * 8 + ReadLimit());
			var cache = linear ? readableLinear : readable;
			RenderTextureReadWrite rw = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
			Texture2D got;
			if (cache.TryGetValue(key, out got) && got != null) return got;
			RenderTexture prev = RenderTexture.active;
			var temps = new List<RenderTexture>();
			try
			{
				Texture cur = src;
				int cw = src.width, ch = src.height;
				while (cw > size * 2 || ch > size * 2)
				{
					cw = Mathf.Max(size, cw / 2); ch = Mathf.Max(size, ch / 2);
					RenderTexture h = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32, rw);
					temps.Add(h);
					Graphics.Blit(cur, h);
					cur = h;
				}
				RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, rw);
				temps.Add(rt);
				Graphics.Blit(cur, rt);
				RenderTexture.active = rt;
				got = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
				got.ReadPixels(new Rect(0, 0, size, size), 0, 0);
				got.Apply();
				cache[key] = got;
				return got;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnel's rim: the ground's layer couldn't be read (" + e.Message + ")"); return null; }
			finally
			{
				RenderTexture.active = prev;
				foreach (RenderTexture t in temps) RenderTexture.ReleaseTemporary(t);
			}
		}

		float nextLookCheck;
		static bool toldLookBack;
		static bool toldSharper;
		// (what the top was dressed with: the terrain shaders don't declare their splat maps, so the material can't be asked)
		Texture lookControl, lookSplat;
		/// <summary>For the tests: the splat map and first layer the rim's top was last dressed with, and whether the ground
		/// still has them.</summary>
		public string TopTextures
		{
			get
			{
				Texture c, s0; Wanted(skinLand, out c, out s0);
				return "control " + (lookControl != null ? lookControl.name + " " + lookControl.width : "none") + ", splat0 " + (lookSplat != null ? lookSplat.name : "none") + (c == lookControl && s0 == lookSplat ? " (current)" : " (stale)");
			}
		}

		static void Wanted(Terrain land, out Texture control0, out Texture splat0)
		{
			control0 = null; splat0 = null;
			TerrainData td = land != null ? land.terrainData : null;
			if (td == null) return;
			Texture2D[] control = td.alphamapTextures;
			TerrainLayer[] layers = td.terrainLayers;
			control0 = control != null && control.Length > 0 ? control[0] : null;
			splat0 = layers != null && layers.Length > 0 && layers[0] != null ? layers[0].diffuseTexture : null;
		}

		void Dressed(Terrain land) { Wanted(land, out lookControl, out lookSplat); }

		/// <summary>The terrain this tunnel's rim top was dressed from (tests).</summary>
		public Terrain SkinLand { get { return skinLand; } }

		/// <summary>Every second: the rim's top still wears the ground's splat map and layers. (11-02: in a world the top came
		/// out a flat grey-blue patch - its splat map and layer textures were gone by the time it was drawn, the island's
		/// ground paint set after the tunnel was made; the editor's stayed.) Put back when they changed.</summary>
		void Update()
		{
			// (the lining's darker rock for a world made here, not while a camera draws: LightLining only uses it)
			if (litRenderer != null && !madeDark && !DynamicIslands.InEditor())
			{
				madeDark = true;
				Material m = litRenderer.sharedMaterial;
				if (m != null) foreach (string tp in LiningTextures) if (m.HasProperty(tp)) Darkened(LiningSource(m, tp));
			}
			if (Time.unscaledTime < nextLookCheck || RimTop == "" || skinLand == null) return;
			nextLookCheck = Time.unscaledTime + 1f;
			try
			{
				Transform r = transform.Find(RockName);
				MeshRenderer mr = r != null ? r.GetComponent<MeshRenderer>() : null;
				if (mr == null) return;
				Material[] mats = mr.sharedMaterials;
				if (mats.Length < 3 || mats[2] == null) return;
				Texture want0, wantS;
				Wanted(skinLand, out want0, out wantS);
				bool editor = DynamicIslands.InEditor();
				// (or the texture limit came down since the top was baked: baked again from the sharper textures)
				bool sharper = !editor && bakedTop != null && bakedLimit > ReadLimit();
				if (want0 == lookControl && wantS == lookSplat && !sharper) return;
				if (!editor) bakedLimit = ReadLimit();
				Material ground = editor ? GroundMaterial(skinLand) : BakedGround(skinLand, topRect, ref bakedTop, true);
				if (ground == null) return;
				mats[2] = ground;
				mr.sharedMaterials = mats;
				Dressed(skinLand);
				if (sharper) { if (!toldSharper) Debug.Log("[CUSTOM ISLANDS] Dug tunnel's rim: the texture limit came down - its top baked again from the sharper textures"); toldSharper = true; }
				else { if (!toldLookBack) Debug.Log("[CUSTOM ISLANDS] Dug tunnel's rim: the ground's paint changed after the rim was made - its top takes it again"); toldLookBack = true; }
			}
			catch (Exception e) { nextLookCheck = Time.unscaledTime + 30f; Debug.LogWarning("[CUSTOM ISLANDS] Dug tunnel's rim: " + e.Message); }
		}

		/// <summary>A face of the rim turned up at least this much (its normal's height) takes the ground's look.</summary>
		const float TopUp = 0.6f;
		/// <summary>A face of the rim with its middle this close over the ground (m, across the slope; a little under it too)
		/// and turned up out of it (its own face, not its corners' smoothed normals) lies on the ground: it takes the ground's look,
		/// whatever the slope or however steeply it crosses the ground (the saw-tooth along the line where the rim's skin meets it).
		/// A face crossing the ground counts too - its middle under it, a corner out of it (11-05: on the steep slope under Old Mine
		/// Islet's man-way the skin dipped in and out of the ground between the rim's samples, and the rock's corners poking out
		/// were pale angular shards the rule, the middle within OnGroundUnder, never saw): down to OnGroundCross under.</summary>
		const float OnGroundUnder = 0.05f, OnGroundBy = 0.3f, OnGroundCross = 0.6f;

		/// <summary>Ground this steep (its normal's height under this, about 60 degrees and more) Raft draws as its cliff rock:
		/// a rim face lying on it stays rock (SplitTop), not a shard (RimShards).</summary>
		const float SteepGround = 0.5f;

		/// <summary>The ground at this point (world) is a steep face - SteepGround.</summary>
		public static bool OnSteepGround(Terrain land, Vector3 at)
		{
			if (land == null || land.terrainData == null) return false;
			Vector3 lo = land.transform.position, size = land.terrainData.size;
			return land.terrainData.GetInterpolatedNormal((at.x - lo.x) / size.x, (at.z - lo.z) / size.z).y < SteepGround;
		}

		/// <summary>The face (corners in the world) lies on the ground or crosses it - see OnGroundBy. The one rule for SplitTop
		/// and RimShards.</summary>
		static bool FaceOnGround(Terrain land, Vector3 a, Vector3 b, Vector3 c)
		{
			if (land == null || land.terrainData == null) return false;
			Vector3 cross = Vector3.Cross(b - a, c - a);
			if (cross.magnitude < 2e-4f) return false;
			Vector3 lo = land.transform.position, size = land.terrainData.size;
			Vector3 mid = (a + b + c) / 3f;
			Vector3 gn = land.terrainData.GetInterpolatedNormal((mid.x - lo.x) / size.x, (mid.z - lo.z) / size.z);
			if (Vector3.Dot(cross, gn) <= 0f) return false;
			float off = (mid.y - GroundY(land, mid)) * gn.y;
			if (off > OnGroundBy || off < -OnGroundCross) return false;
			if (off >= -OnGroundUnder) return true;
			// (under the ground at its middle: on it only when a corner pokes out)
			float top = Mathf.Max(a.y - GroundY(land, a), Mathf.Max(b.y - GroundY(land, b), c.y - GroundY(land, c))) * gn.y;
			return top > 0f;
		}

		/// <summary>Tests: the rim's rock faces (part 1) that lie on the ground (FaceOnGround) - pale shards on the ground beside
		/// the mouth, the rock's look where the ground's belongs - left after SplitTop. (Part 0, the lining, isn't the rim; with no
		/// ground look - SplitTop not run - the whole rim is part 1 and every such face counts.) where: the first few.</summary>
		public int RimShards(out string where)
		{
			where = "";
			Transform r = transform.Find(RockName);
			MeshFilter mf = r != null ? r.GetComponent<MeshFilter>() : null;
			Terrain land = skinLand;
			if (mf == null || mf.sharedMesh == null || land == null || mf.sharedMesh.subMeshCount < 2) return 0;
			Mesh mesh = mf.sharedMesh;
			var verts = new List<Vector3>();
			mesh.GetVertices(verts);
			int[] tris = mesh.GetTriangles(1);
			int n = 0;
			var seen = new List<string>();
			for (int i = 0; i + 2 < tris.Length; i += 3)
			{
				Vector3 a = r.TransformPoint(verts[tris[i]]), b = r.TransformPoint(verts[tris[i + 1]]), c = r.TransformPoint(verts[tris[i + 2]]);
				if (!FaceOnGround(land, a, b, c) || OnSteepGround(land, (a + b + c) / 3f)) continue;
				n++;
				if (seen.Count < 4) seen.Add(((a + b + c) / 3f).ToString("0.0"));
			}
			where = string.Join(" ", seen.ToArray());
			return n;
		}

		/// <summary>Tests: the rim's top faces (part 2, the ground's look) over steep ground (OnSteepGround) - a
		/// patterned patch of the baked layers where Raft draws its cliff rock. where: the first few.</summary>
		public int RimSteepTop(out string where)
		{
			where = "";
			Transform r = transform.Find(RockName);
			MeshFilter mf = r != null ? r.GetComponent<MeshFilter>() : null;
			Terrain land = skinLand;
			if (mf == null || mf.sharedMesh == null || land == null || mf.sharedMesh.subMeshCount < 3) return 0;
			Mesh mesh = mf.sharedMesh;
			var verts = new List<Vector3>();
			mesh.GetVertices(verts);
			int[] tris = mesh.GetTriangles(2);
			int n = 0;
			var seen = new List<string>();
			for (int i = 0; i + 2 < tris.Length; i += 3)
			{
				Vector3 a = r.TransformPoint(verts[tris[i]]), b = r.TransformPoint(verts[tris[i + 1]]), c = r.TransformPoint(verts[tris[i + 2]]);
				if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-8f || !OnSteepGround(land, (a + b + c) / 3f)) continue;
				n++;
				if (seen.Count < 4) seen.Add(((a + b + c) / 3f).ToString("0.0"));
			}
			where = string.Join(" ", seen.ToArray());
			return n;
		}

		static readonly Dictionary<Terrain, Material> groundMaterials = new Dictionary<Terrain, Material>();

		/// <summary>The terrain's own shader on a mesh: its splat map and its first four layers as the terrain lays them
		/// (tiling over the terrain's size, offsets, normals, smoothness); null when the shader doesn't take them.</summary>
		static Material GroundMaterial(Terrain land)
		{
			TerrainData td = land.terrainData;
			TerrainLayer[] layers = td.terrainLayers;
			Texture2D[] control = td.alphamapTextures;
			if (layers == null || layers.Length == 0 || control == null || control.Length == 0) return null;
			Shader s = land.materialTemplate != null ? land.materialTemplate.shader : Shader.Find("Nature/Terrain/Diffuse");
			if (s == null) return null;
			Material m;
			if (!groundMaterials.TryGetValue(land, out m) || m == null || m.shader != s)
			{
				m = new Material(s) { name = "DigTunnelGround" };
				groundMaterials[land] = m;
			}
			// (Unity's terrain shaders since 2018.3 don't declare their splat maps as properties - the terrain sets them -
			// so they're set here unasked: a shader not made for terrain draws them not, and the rim's top stays rock)
			if (s.name.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) < 0) return null;
			m.shaderKeywords = new string[0];
			m.SetTexture("_Control", control[0]);
			bool normals = false;
			for (int i = 0; i < 4; i++)
			{
				string splat = "_Splat" + i;
				TerrainLayer l = i < layers.Length ? layers[i] : null;
				m.SetTexture(splat, l != null ? l.diffuseTexture : null);
				if (l == null) continue;
				Vector2 tile = new Vector2(Mathf.Max(0.01f, l.tileSize.x), Mathf.Max(0.01f, l.tileSize.y));
				m.SetVector(splat + "_ST", new Vector4(td.size.x / tile.x, td.size.z / tile.y, l.tileOffset.x / tile.x, l.tileOffset.y / tile.y));
				if (l.normalMapTexture != null)
				{
					m.SetTexture("_Normal" + i, l.normalMapTexture);
					m.SetFloat("_NormalScale" + i, l.normalScale);
					normals = true;
				}
				m.SetFloat("_Smoothness" + i, l.smoothness);
				m.SetFloat("_Metallic" + i, l.metallic);
			}
			if (normals) m.EnableKeyword("_NORMALMAP");
			return m;
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

		/// <summary>The other dug tunnels beside this one (the same parent - the editor's placed objects or the island's - and their
		/// rock's bounds meeting this one's): where their passages come into this rock, it is open (a joined system, 11-04).</summary>
		List<DigTunnel> Neighbours(IList<DigTunnel> all = null)
		{
			var list = new List<DigTunnel>();
			if (transform.parent == null || PointCount == 0) return list;
			Bounds wb = WorldBounds();
			foreach (DigTunnel t in all ?? transform.parent.GetComponentsInChildren<DigTunnel>(false))
				if (t != this && t.PointCount > 0 && t.transform.parent == transform.parent && t.WorldBounds().Intersects(wb)) list.Add(t);
			return list;
		}

		/// <summary>What this tunnel's lining was made against: its neighbours' paths and places (changed: make it again).</summary>
		string JoinKey(List<DigTunnel> near)
		{
			if (near.Count == 0) return "";
			var k = new StringBuilder();
			foreach (DigTunnel t in near.OrderBy(t => t.GetInstanceID()))
				k.Append(t.GetInstanceID()).Append(':').Append((t.built ?? "").GetHashCode()).Append(t.transform.position).Append(t.transform.rotation).Append(t.transform.lossyScale).Append('/');
			return k.ToString();
		}

		[NonSerialized] string joinBuilt = "";

		/// <summary>The neighbours moved, dug on or newly there since the lining was made (a world's island: built one by one).</summary>
		public bool JoinsChanged(IList<DigTunnel> all = null) { return PointCount > 0 && JoinKey(Neighbours(all)) != joinBuilt; }

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
			// (the cut cells first: a hole takes the ground's whole column in its cell - on a cliff over the mouth, the face up to
			// its top - so the grid reaches up to the ground's top over them, and the rock fills them up to it (11-02: a strip of
			// the cliff cut away over the mouth, the sky through it))
			Vector3 scale = transform.lossyScale;
			float sY = Mathf.Abs(scale.y) > 1e-4f ? scale.y : 1f;
			bool upright = Vector3.Dot(transform.up, Vector3.up) > 0.9995f;
			var cutSet = new HashSet<Vector2Int>();
			Openings.Clear();
			if (upright && land != null)
			{
				CellsToCut(land, cutSet);
				TerrainData td0 = land.terrainData;
				Vector3 lo0 = land.transform.position;
				float cw0 = td0.size.x / td0.holesResolution, ch0 = td0.size.z / td0.holesResolution, high = float.MinValue;
				foreach (Vector2Int q in cutSet) Openings.Add(new Vector2(lo0.x + (q.x + 0.5f) * cw0, lo0.z + (q.y + 0.5f) * ch0));
				foreach (Vector2Int q in cutSet)
					for (int a = -1; a <= 5; a++)
						for (int c = -1; c <= 5; c++)
							high = Mathf.Max(high, GroundY(land, new Vector3(lo0.x + (q.x + a * 0.25f) * cw0, 0f, lo0.z + (q.y + c * 0.25f) * ch0)));
				if (high > float.MinValue)
				{
					float top0 = Mathf.Min((high - transform.position.y) / sY + RimHeight(rMax) + 2f * h, b.max.y + 30f);
					if (top0 > b.max.y) b.max = new Vector3(b.max.x, top0, b.max.z);
				}
			}
			int nx =Mathf.CeilToInt(b.size.x / h) + 1, ny = Mathf.CeilToInt(b.size.y / h) + 1, nz = Mathf.CeilToInt(b.size.z / h) + 1;
			while ((long)nx * ny * nz > 3000000L)
			{
				h *= 1.25f;
				nx = Mathf.CeilToInt(b.size.x / h) + 1; ny = Mathf.CeilToInt(b.size.y / h) + 1; nz = Mathf.CeilToInt(b.size.z / h) + 1;
			}
			Vector3 o = b.min;
			int N = nx * ny * nz;
			int sy = nx, sz = nx * ny; // index = x + y*sy + z*sz
			// (other tunnels' passages coming into this rock (11-04: a second tunnel dug into the first met its closed lining, a
			// wall across the way): their passage, a little wider, cut out of this rock; their own lining makes the walls there)
			List<DigTunnel> joined = Neighbours();
			joinBuilt = JoinKey(joined);
			var joinSegs = new List<List<Seg>>();
			var joinBox = new List<Bounds>();
			foreach (DigTunnel t in joined) { joinSegs.Add(t.Segments(Shell)); joinBox.Add(t.WorldBounds()); }
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
			var top = new float[nx * nz]; // the rock's top (object space y), per column when upright
			float[] groundAt = upright && land != null ? new float[nx * nz] : null; // the ground's own height (object space y)
			if (upright)
				for (int z = 0; z < nz; z++)
					for (int x = 0; x < nx; x++)
					{
						Vector3 w = transform.TransformPoint(new Vector3(o.x + x * h, 0f, o.z + z * h));
						top[x + z * nx] = (GroundY(land, w) - Under - transform.position.y) / sY;
						if (groundAt != null) groundAt[x + z * nx] = (GroundY(land, w) - transform.position.y) / sY;
					}
			// (the mouth's rim: over the cut ground the rock's top is a skin just over the ground, so the cut's square cells are
			// under rock; past the cut it sinks gently back under the ground by RimOut, along a ragged line; round the opening,
			// patches of rock stand up out of it)
			float[] rimFade = null, rimH = null;
			bool[] fillUp = null; // (columns over the cut cells, and h round them: rock up to the ground's top, past Shell)
			RimColumns = 0;
			mouthMin = mouthMax = Vector3.zero;
			if (upright && land != null)
			{
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
					float wOut = RimOut(rMax), hTop = Mathf.Min(RimRocksTop, RimHeight(rMax));
					int reachX = Mathf.CeilToInt((wOut + 0.5f) / cw) + 1, reachZ = Mathf.CeilToInt((wOut + 0.5f) / ch) + 1;
					rimFade = new float[nx * nz];
					rimH = new float[nx * nz];
					fillUp = new bool[nx * nz];
					for (int z = 0; z < nz; z++)
						for (int x = 0; x < nx; x++)
						{
							Vector3 w = transform.TransformPoint(new Vector3(o.x + x * h, 0f, o.z + z * h));
							// (the noise on the tunnel's own ground plan, not the world's: on the world's, the rim came out
							// differently wherever the island lay - 11-06, Ironreef's tunnel B grew loose shards one day, not the day before)
							float nxL = (o.x + x * h) * Mathf.Abs(scale.x), nzL = (o.z + z * h) * Mathf.Abs(scale.z);
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
							if (best <= h) fillUp[x + z * nx] = true;
							float rag = (Mathf.PerlinNoise(nxL * 0.55f + 7.7f, nzL * 0.55f + 3.1f) - 0.5f) * 0.6f * Mathf.Clamp01(best / 0.4f);
							float f = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((best + rag) / wOut));
							if (f <= 0f) continue;
							rimFade[x + z * nx] = f;
							// (the rocks round the opening: in patches, none between - and only at its lip: further out, where the rim
							// is all but under the ground, a patch's top stood out of the grass as a loose flat shard - 11-06)
							float patch = Mathf.PerlinNoise(nxL * 0.45f + 19.1f, nzL * 0.45f + 2.3f) * 0.7f + Mathf.PerlinNoise(nxL * 1.1f + 5.3f, nzL * 1.1f + 13.7f) * 0.3f;
							float lip = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(best / RimLip));
							rimH[x + z * nx] = hTop * lip * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((patch - 0.42f) * 3.5f));
							RimColumns++;
						}
				}
			}

			// (over the cut cells the rock's top is the ground's highest within half a step: on a rounded hilltop the ground
			// between the samples stood over the flat top's skin, a cut square showing it 6 cm down - 11-02)
			if (fillUp != null)
				for (int z = 0; z < nz; z++)
					for (int x = 0; x < nx; x++)
					{
						if (!fillUp[x + z * nx]) continue;
						float g = float.MinValue;
						for (int dz = -1; dz <= 1; dz++)
							for (int dx = -1; dx <= 1; dx++)
								g = Mathf.Max(g, GroundY(land, transform.TransformPoint(new Vector3(o.x + (x + dx * 0.5f) * h, 0f, o.z + (z + dz * 0.5f) * h))));
						top[x + z * nx] = Mathf.Max(top[x + z * nx], (g - Under - transform.position.y) / sY);
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
						bool fu = fillUp != null && fillUp[x + z * nx];
						if (s == float.MaxValue && !fu) { field[i] = 1e6f; outer[i] = true; continue; }
						var p = new Vector3(o.x + x * h, o.y + y * h, o.z + z * h);
						if (s == float.MaxValue) s = 1e3f; else s += Bumps(p, floorAt[i], radAt[i]);
						passage[i] = s < 0f;
						float t;
						if (upright)
						{
							t = top[x + z * nx];
							float rf = rimFade != null ? rimFade[x + z * nx] : 0f;
							if (rf > 0f)
							{
								// (the skin; and the rocks, highest at the opening, lower further from the passage - never as fast as the
								// height itself, so the top is one surface)
								float near = Mathf.Clamp01((1.8f - s) / 1.8f);
								float shellIn = Mathf.Clamp01((Shell - 0.4f - s) / 1.2f);
								float lift = fu ? rf * (Under + RimSkin) + rf * rimH[x + z * nx] * near * near * shellIn
									: rf * (Under + RimSkin + rimH[x + z * nx] * near * near) * shellIn;
								t += lift / sY;
							}
						}
						else
						{
							Vector3 w = transform.TransformPoint(p);
							t = p.y + (GroundY(land, w) - Under - w.y) / sY;
						}
						float a = -s, c = fu ? -1e6f : s - Shell, e = p.y - t;
						float m = a > c ? a : c;
						if (e > m) m = e;
						field[i] = m;
						outer[i] = c > 0f && e <= 0f; // (past the rock's outside, under its top: the hill's own, never seen)
						if (joined.Count > 0 && m < JoinGap / sY)
						{
							Vector3 w = transform.TransformPoint(p);
							for (int j = 0; j < joined.Count; j++)
							{
								if (!joinBox[j].Contains(w)) continue;
								DigTunnel t2 = joined[j];
								float s2 = Mathf.Abs(t2.transform.lossyScale.y) > 1e-4f ? Mathf.Abs(t2.transform.lossyScale.y) : 1f;
								float d2 = (Distance(joinSegs[j], t2.transform.InverseTransformPoint(w)) * s2 - JoinGap) / sY;
								if (d2 < 0f && -d2 > field[i]) { field[i] = -d2; passage[i] = true; outer[i] = false; }
							}
						}
					}
			dist = null; floorAt = null; radAt = null;

			// (the passage's air out in the open, over the ground's own height - in front of a steep face, where the dig starts
			// outside it: the rock there is the rim's, the ground's look, not the lining's. 11-13: the lining's floor came out of
			// every mouth in a steep face as a pale slab on the sand or ash with a sawtooth edge - the cut cells' squares -
			// Cinderfall's lava tubes, The Frontier's quarry, Listening Mirrors)
			if (groundAt != null)
				for (int z = 0; z < nz; z++)
					for (int x = 0; x < nx; x++)
					{
						float gy = groundAt[x + z * nx];
						for (int y = 0; y < ny; y++)
						{
							int i = x + y * sy + z * sz;
							if (passage[i] && o.y + y * h > gy) passage[i] = false;
						}
					}

			// (the surface, smoothed: toward the passage, or the open air over the rock's top - the mouth's rim, which takes the
			// ground's look on its top; not toward the hill's own air past the rock)
			int tri;
			Mesh mesh = RockNets.Build("DigTunnelRock", field, nx, ny, nz, o, h, (air, rock) => outer[air] ? -1 : passage[air] ? 0 : 1, 2, 2, null, 1f / 6f, out tri);
			Triangles = tri;
			RimTextureScale(mesh, 1, 6f / 14f);
			return mesh;
		}

		/// <summary>The rim walls (part 1) take the cave hill skin's texture scale (1/14 per metre, CaveMountain's Quad) instead of the
		/// lining's 1/6: at 1/6 the same rock texture showed 2.3 times denser, a patterned sand-rock patch round the mouth in the hill's
		/// smooth rock (Stalker's SW mouth, w03 11-09). Corners the lining shares get it too (one row at the mouth's edge).</summary>
		static void RimTextureScale(Mesh mesh, int part, float scale)
		{
			if (mesh == null || part >= mesh.subMeshCount) return;
			var uvs = new List<Vector2>();
			mesh.GetUVs(0, uvs);
			var done = new bool[uvs.Count];
			foreach (int v in mesh.GetTriangles(part))
				if (!done[v]) { done[v] = true; uvs[v] *= scale; }
			mesh.SetUVs(0, uvs);
		}

		static int Cell(int x, int y, int z, int cx, int cy) { return x + y * cx + z * cx * cy; }

		#endregion

		#region Ground cut

		/// <summary>
		/// The ground cells to cut over the passage: those whose ground (at any of 3 x 3 points over the cell) is inside it -
		/// where the passage comes out through the ground (its mouth) - and the cells next to those whose ground comes within
		/// CutMargin of it (11-02: a thin roof over a buried part was cut too: see-through squares on the hill). Not where the ground is the passage's floor (level
		/// with it, before a cliff): cut there, the rim lay over flat ground before the mouth as a green apron (11-02).
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
			// (only where the passage comes up through the ground - its mouth - and the cells right round it; never over a
			// buried part: a thin roof was cut too, and the hill over the mouth showed see-through squares (11-02))
			var core = new HashSet<Vector2Int>();
			var near = new List<Vector2Int>();
			for (int z = z0; z <= z1; z++)
				for (int x = x0; x <= x1; x++)
				{
					bool cut = false, through = false;
					for (int a = 0; a <= 2 && !through; a++)
						for (int c = 0; c <= 2 && !through; c++)
						{
							var w = new Vector3(o.x + (x + a * 0.5f) * cw, 0f, o.z + (z + c * 0.5f) * ch);
							w.y = GroundY(land, w);
							Vector3 pg = transform.InverseTransformPoint(w);
							float d = Distance(segs, pg) * sY;
							if (d >= CutMargin) continue;
							// (the ground here level with the passage's floor: it is the floor, left as it is)
							float floor;
							Distance(segs, pg + Vector3.up * (0.5f / sY), out floor);
							float over = (pg.y - floor) * sY;
							if (over > FloorGround || over < -0.4f) { cut = true; if (d < 0.05f) through = true; }
						}
					var cell = new Vector2Int(x, z);
					if (through) core.Add(cell); else if (cut) near.Add(cell);
				}
			foreach (Vector2Int cell in core) into.Add(cell);
			foreach (Vector2Int cell in near)
			{
				bool next = false;
				for (int dz = -1; dz <= 1 && !next; dz++)
					for (int dx = -1; dx <= 1 && !next; dx++)
						if (core.Contains(new Vector2Int(cell.x + dx, cell.y + dz))) next = true;
				if (next) into.Add(cell);
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
					if (Mathf.Abs(t.GroundLook(land) - t.groundLook) > 1e-4f || t.JoinsChanged()) t.Rebuild(); // (the ground set after the lining was made; a tunnel joining it came after it)
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

		static List<DigTunnel> insideList;
		static float insideListAt = -1f;

		/// <summary>The editor camera: a world point inside a dug tunnel's passage (margin: its distance from the rock below this, metres).
		/// fresh: the tunnels looked up now, not the list kept for half a second (a recipe digs and places in one frame).</summary>
		public static bool InsideAny(Vector3 world, float margin, bool fresh = false)
		{
			if (fresh || insideList == null || Time.unscaledTime > insideListAt + 0.5f || Time.unscaledTime < insideListAt) { insideList = EditorTunnels(); insideListAt = Time.unscaledTime; }
			for (int i = 0; i < insideList.Count; i++)
			{
				DigTunnel t = insideList[i];
				if (t == null || !t.isActiveAndEnabled) continue;
				float sY = Mathf.Abs(t.transform.lossyScale.y) > 1e-4f ? Mathf.Abs(t.transform.lossyScale.y) : 1f;
				if (t.Distance(t.transform.InverseTransformPoint(world)) * sY < margin) return true;
			}
			return false;
		}

		/// <summary>A world point inside a live dug tunnel's passage, in a world or the editor (tests: a play's walk started in a dug
		/// cave - 11-05).</summary>
		public static bool InPassage(Vector3 world)
		{
			for (int i = 0; i < live.Count; i++)
			{
				DigTunnel t = live[i];
				if (t == null || !t.isActiveAndEnabled || t.PointCount == 0) continue;
				float sY = Mathf.Abs(t.transform.lossyScale.y) > 1e-4f ? Mathf.Abs(t.transform.lossyScale.y) : 1f;
				if (t.Distance(t.transform.InverseTransformPoint(world)) * sY < 0f) return true;
			}
			return false;
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
				if (Mathf.Abs(look - t.groundLook) > 1e-4f || t.JoinsChanged(tunnels)) t.Rebuild(); // (the ground sculpted over it; a tunnel joining it dug or moved)
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
		/// <summary>The stroke goes down into flat ground (-1: not known yet; 0 no; 1 yes) - see DigToward.</summary>
		static int autoDown = -1;
		/// <summary>Digging down into flat ground: rock this thick over the passage (between its roof and the lining's top) before it levels out.</summary>
		public const float DownCover = 1.0f;
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
			autoDown = -1;
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
			// (the slope level and the mouse not pointing up into a hill - on flat ground or a gentle slope: the passage
			// goes down into the ground at the steepest slope, a walkable ramp, until it has DownCover of rock over it, then
			// on level (11-02: dug level there it was an open trench))
			if (autoDown < 0) autoDown = Mathf.Abs(Slope) < 0.5f && target.y - last.At.y < (CentreUp + 1f) * r ? 1 : 0;
			Terrain land = autoDown == 1 ? current.Land() : null;
			float down = -Mathf.Tan(MaxSlope * Mathf.Deg2Rad) * step;
			// (steered into another passage - another tunnel, or another stroke of this one: its floor met on a ramp at most
			// JoinSlope, begun as late as that allows (11-04: a second tunnel dug into the first came in 1.6 m under its floor, a
			// step no one climbs))
			float? joinY = JoinFloor(world);
			while (carry >= step && current.PointCount < MaxPoints)
			{
				carry -= step;
				last = head[head.Count - 1];
				float c = climb;
				if (joinY.HasValue)
				{
					Vector3 lw = current.transform.TransformPoint(last.At);
					float left = new Vector2(world.x - lw.x, world.z - lw.z).magnitude;
					float dy = (joinY.Value - lw.y) / (Mathf.Abs(current.transform.lossyScale.y) > 1e-4f ? Mathf.Abs(current.transform.lossyScale.y) : 1f);
					if (Mathf.Abs(dy) > 0.05f && Mathf.Abs(dy) / JoinSlopeTan >= left - 2f * step - 0.6f * r)
					{
						head.Add(new Point(last.At + dir * step + Vector3.up * Mathf.Clamp(dy * step / Mathf.Max(step, left - 0.6f * r), down, -down), Radius));
						changedSinceBuild = true;
						continue;
					}
				}
				if (land != null)
				{
					Vector3 next = last.At + dir * step;
					Vector3 nw = current.transform.TransformPoint(next);
					float g = current.transform.InverseTransformPoint(new Vector3(nw.x, GroundY(land, nw), nw.z)).y;
					float roof = next.y + (CentreUp + 1f) * Radius;
					if (g - Under - roof < DownCover) c = Mathf.Max(down, (g - Under - DownCover) - roof);
				}
				head.Add(new Point(last.At + dir * step + Vector3.up * c, Radius));
				changedSinceBuild = true;
			}
			return true;
		}

		const float JoinSlopeTan = 0.36f; // (20 degrees)

		/// <summary>The floor (world y) of a dug passage over or under a point - another tunnel's, or another stroke's of the
		/// tunnel being dug - within its radius across; null: none.</summary>
		static float? JoinFloor(Vector3 world)
		{
			float? best = null;
			float bestD = float.MaxValue;
			foreach (DigTunnel t in EditorTunnels())
				foreach (List<Point> s in t.Strokes)
				{
					if (ReferenceEquals(s, head)) continue;
					foreach (Point q in s)
					{
						Vector3 a = t.transform.TransformPoint(q.At);
						float d = new Vector2(a.x - world.x, a.z - world.z).magnitude;
						if (d < q.R * Mathf.Abs(t.transform.lossyScale.x) && d < bestD) { bestD = d; best = a.y; }
					}
				}
			return best;
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
					foreach (Collider c in Physics.OverlapSphere(t.transform.TransformPoint(q.Centre), q.R * Mathf.Abs(t.transform.lossyScale.y), ~0, QueryTriggerInteraction.Ignore))
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

		/// <summary>The middle of the editor's dug passage under (or over) a point, within its radius across: where a recipe's
		/// stroke pressed there branches from it (null: none).</summary>
		public static Vector3? PassageAt(Vector3 world)
		{
			Vector3? best = null;
			float bestD = float.MaxValue;
			foreach (DigTunnel t in EditorTunnels())
				foreach (List<Point> s in t.Strokes)
					foreach (Point q in s)
					{
						Vector3 c = t.transform.TransformPoint(q.Centre);
						float d = new Vector2(c.x - world.x, c.z - world.z).magnitude;
						if (d < q.R && d < bestD) { bestD = d; best = c; }
					}
			return best;
		}

		/// <summary>
		/// A recipe's dig step (G1, 11-04): a stroke as TestStroke digs it, the brush's radius set to radii[k] from points[k] on and
		/// its slope to slope (degrees) for the stroke, both put back after. fill: the stroke fills the tunnel it is pressed on back
		/// in at every point. Returns the tunnel (null: none made, or none to fill).
		/// </summary>
		/// <summary>A recipe's dig stroke (or fill). shaft (11-08): the passage laid straight through the points as given, up
		/// or down at any angle - a ladder shaft from a dug room up to the ground (the brush itself climbs at most MaxSlope).</summary>
		public static DigTunnel RecipeStroke(Terrain land, IList<Vector3> points, IList<float> radii, float slope, bool fill, bool shaft = false)
		{
			if (land == null || points == null || points.Count == 0 || Digging) return null;
			float r0 = Radius, s0 = Slope;
			try
			{
				Func<int, float> rad = k => Mathf.Clamp(radii != null && radii.Count > k ? radii[k] : r0, MinRadius, MaxRadius);
				Radius = rad(0);
				Slope = Mathf.Clamp(slope, -MaxSlope, MaxSlope);
				BeginStroke(land, points[0], null, fill);
				if (!Digging) return null;
				DigTunnel t = current;
				for (int k = fill ? 0 : 1; k < points.Count; k++)
				{
					if (fill) { DigToward(points[k], 0.05f); continue; }
					Radius = rad(k);
					if (shaft && head != null && head.Count > 0)
					{
						Point last = head[head.Count - 1];
						Vector3 to = current.transform.InverseTransformPoint(points[k]);
						float step = Mathf.Max(0.4f, 0.4f * Radius);
						int n = Mathf.Max(1, Mathf.CeilToInt((to - last.At).magnitude / step));
						for (int i = 1; i <= n && current.PointCount < MaxPoints; i++)
							head.Add(new Point(Vector3.Lerp(last.At, to, i / (float)n), Radius));
						changedSinceBuild = true;
						continue;
					}
					for (int guard = 0; guard < 4000; guard++)
						if (!DigToward(points[k], 0.05f) || current.PointCount >= MaxPoints) break;
				}
				BuildNow(land);
				EndStroke();
				return t != null && t.gameObject != null ? t : null;
			}
			finally
			{
				if (Digging) EndStroke();
				Radius = r0; Slope = s0;
			}
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
