using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's cave pieces (Balboa's dens, the tunnels) are rock shells seen from inside only: from outside you look through
	/// their sides and under their thin edges, and they hang over sloping ground. In a world, an island with cave pieces gets
	/// one rock mountain over them (AU83): a surface draped over the shells' roofs that slopes down to the ground, walled where
	/// it is cut off, with the passages' open ends kept clear and a skirt under their floors down to the ground.
	/// </summary>
	public class CaveMountain : MonoBehaviour
	{
		/// <summary>Name of the mountain's object (under the island).</summary>
		public const string SkinName = "CaveSkin";
		/// <summary>Grid step, how steep the sides fall (m down per m out), how far they reach, and how far above the roofs.</summary>
		const float Cell = 1.25f, Slope = 1.5f, Reach = 16f, Over = 0.3f;

		readonly List<Transform> caves = new List<Transform>();
		readonly List<PropInfo> infos = new List<PropInfo>();
		float due = -1f;
		GameObject skin;

		/// <summary>A cave piece spawned in a world under this island: (re)build the island's mountain once the island is all there.</summary>
		public static void Add(Transform island, GameObject piece, string name)
		{
			if (island == null || piece == null) return;
			if (!Wants(name)) return;
			PropInfo p = RaftProps.Get(name);
			CaveMountain m = island.GetComponent<CaveMountain>() ?? island.gameObject.AddComponent<CaveMountain>();
			m.caves.Add(piece.transform);
			m.infos.Add(p);
			m.due = Time.realtimeSinceStartup + 1f;
		}

		/// <summary>A cave piece that needs a mountain over it (the Vines cave is a whole rock with its own outside already).</summary>
		public static bool Wants(string name)
		{
			PropInfo p = RaftProps.Get(name);
			return p != null && p.IsCave && name != "BalboaCave_Vines";
		}

		/// <summary>Tests: a map of the last build (R roof, s side, c a way in, g lower than the ground, . nothing), one line per row.</summary>
		public string LastMap;

		/// <summary>Tests: builds it again now.</summary>
		public void Rebuild() { due = -1f; Build(); }

		void Update()
		{
			if (due < 0f || Time.realtimeSinceStartup < due) return;
			due = -1f;
			try { Build(); }
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Cave mountain: " + e.Message); }
		}

		/// <summary>One cave piece's open ends: where the mountain must leave the way in clear.</summary>
		struct Mouth { public Vector3 Pos, Along, Side; public float InsideAlong, InsideSide, HalfWidth, FloorY; public bool Plus, Minus; }

		void Build()
		{
			if (skin != null) { DestroyImmediate(skin); skin = null; } // (its collider must not count as ground)
			var cols = new HashSet<Collider>();
			var floorOf = new Dictionary<Collider, float>();
			var mouths = new List<Mouth>();
			Bounds b = new Bounds();
			bool any = false;
			Renderer look = null;
			float lookSize = 0f;
			for (int i = 0; i < caves.Count; i++)
			{
				Transform t = caves[i];
				if (t == null) continue;
				PropInfo p = infos[i];
				float floorY = t.position.y + p.Floor;
				foreach (Collider c in t.GetComponentsInChildren<Collider>())
					if (c.enabled && !c.isTrigger) { cols.Add(c); floorOf[c] = floorY; }
				foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
				{
					if (!r.enabled || !(r is MeshRenderer)) continue;
					if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
					float s = r.bounds.size.x * r.bounds.size.z;
					if (s > lookSize && r.sharedMaterial != null) { lookSize = s; look = r; }
				}
				Vector3 along = t.rotation * (p.Axis == 0 ? Vector3.right : Vector3.forward);
				along.y = 0f; along.Normalize();
				Vector3 side = new Vector3(along.z, 0f, -along.x);
				Vector3 inside = t.position + t.rotation * p.Inside;
				mouths.Add(new Mouth
				{
					Pos = t.position, Along = along, Side = side,
					InsideAlong = Vector3.Dot(inside - t.position, along), InsideSide = Vector3.Dot(inside - t.position, side),
					HalfWidth = p.Width / 2f + 1f, FloorY = floorY, Plus = p.OpenPlus, Minus = p.OpenMinus
				});
			}
			caves.RemoveAll(t => t == null);
			if (!any || look == null || cols.Count == 0) return;

			Physics.SyncTransforms();
			int nx = Mathf.CeilToInt((b.size.x + 2f * Reach) / Cell) + 1, nz = Mathf.CeilToInt((b.size.z + 2f * Reach) / Cell) + 1;
			float x0 = b.min.x - Reach, z0 = b.min.z - Reach, rayTop = b.max.y + 5f, rayLen = b.size.y + 60f;
			var top = new float[nx, nz];       // the shells' roofs (NaN: none)
			var ground = new float[nx, nz];    // what is under them otherwise
			var groundBy = new string[nx, nz]; // (tests: what that is)
			var corridor = new float[nx, nz];  // a way in: the floor height there (NaN: not one)
			int groundMask = (int)LayerMasks.MASK_GroundMask_NonRaft | (1 << IslandSpawner.TerrainLayer);
			bool backfaces = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true; // (the shells face inwards: from above, a roof is its back)
			try
			{
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						Vector3 at = new Vector3(x0 + i * Cell, rayTop, z0 + k * Cell);
						float roof = float.NaN, gr = float.NegativeInfinity;
						foreach (RaycastHit h in Physics.RaycastAll(at, Vector3.down, rayLen, ~0, QueryTriggerInteraction.Ignore))
						{
							float f;
							if (floorOf.TryGetValue(h.collider, out f))
							{
								// (only a roof counts: a floor open to the sky is the way in)
								if (h.point.y > f + 1.5f && !(h.point.y <= roof)) roof = h.point.y;
							}
							else if ((groundMask & (1 << h.collider.gameObject.layer)) != 0 && h.point.y > gr) { gr = h.point.y; groundBy[i, k] = h.collider.name; }
						}
						top[i, k] = roof;
						ground[i, k] = float.IsInfinity(gr) ? b.min.y - 2f : gr;
						corridor[i, k] = float.NaN;
						if (!float.IsNaN(roof)) continue;
						foreach (Mouth m in mouths)
						{
							Vector3 d = at - m.Pos;
							float a = Vector3.Dot(d, m.Along) - m.InsideAlong, s = Vector3.Dot(d, m.Side) - m.InsideSide;
							if (Mathf.Abs(s) < m.HalfWidth && ((m.Plus && a > 0f) || (m.Minus && a < 0f))) { corridor[i, k] = m.FloorY; break; }
						}
					}
			}
			finally { Physics.queriesHitBackfaces = backfaces; }

			// The surface: over a roof, the highest roof around it; elsewhere falling away from the roofs nearby
			var height = new float[nx, nz];
			var on = new bool[nx, nz];
			int reach = Mathf.CeilToInt(Reach / Cell);
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++) height[i, k] = float.NegativeInfinity;
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					if (float.IsNaN(top[i, k])) continue;
					float peak = top[i, k];
					for (int di = -1; di <= 1; di++)
						for (int dk = -1; dk <= 1; dk++)
						{
							int a = i + di, c = k + dk;
							if (a >= 0 && c >= 0 && a < nx && c < nz && top[a, c] > peak) peak = top[a, c];
						}
					peak += Over;
					for (int di = -reach; di <= reach; di++)
						for (int dk = -reach; dk <= reach; dk++)
						{
							int a = i + di, c = k + dk;
							if (a < 0 || c < 0 || a >= nx || c >= nz) continue;
							float h = peak - Slope * Cell * Mathf.Sqrt(di * di + dk * dk);
							if (h > height[a, c]) height[a, c] = h;
						}
				}
			// (rounded off, and a little uneven: a hill, not cones; never below a roof)
			for (int pass = 0; pass < 2; pass++)
			{
				var soft = new float[nx, nz];
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						float sum = 0f; int n = 0;
						for (int di = -2; di <= 2; di++)
							for (int dk = -2; dk <= 2; dk++)
							{
								int a = Mathf.Clamp(i + di, 0, nx - 1), c = Mathf.Clamp(k + dk, 0, nz - 1);
								sum += Mathf.Max(height[a, c], ground[a, c] - 2f); n++;
							}
						soft[i, k] = sum / n;
					}
				height = soft;
			}
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					float x = x0 + i * Cell, z = z0 + k * Cell;
					float rough = Mathf.Clamp01((height[i, k] - ground[i, k]) / 3f); // (none at its foot: no bumps through the ground around)
					height[i, k] += rough * ((Mathf.PerlinNoise(x * 0.13f, z * 0.13f) - 0.5f) * 2.2f + (Mathf.PerlinNoise(x * 0.4f + 7f, z * 0.4f) - 0.5f) * 0.6f);
					if (!float.IsNaN(top[i, k]) && height[i, k] < top[i, k] + Over) height[i, k] = top[i, k] + Over;
					// (its foot goes on a little under the ground: no edge to see where it meets it)
					on[i, k] = float.IsNaN(corridor[i, k]) && height[i, k] > ground[i, k] - 0.6f;
				}
			// (one more ring round it, sunk under the ground: its edge slopes into the ground, not a stepped wall over it)
			var rim = new List<int>();
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					if (on[i, k] || !float.IsNaN(corridor[i, k])) continue;
					bool next = false;
					for (int di = -1; di <= 1 && !next; di++)
						for (int dk = -1; dk <= 1 && !next; dk++)
						{
							int a = i + di, c = k + dk;
							next = a >= 0 && c >= 0 && a < nx && c < nz && on[a, c];
						}
					if (next) rim.Add(i * nz + k);
				}
			foreach (int r in rim)
			{
				int i = r / nz, k = r % nz;
				height[i, k] = ground[i, k] - 0.8f;
				on[i, k] = true;
			}

			var map = new System.Text.StringBuilder();
			for (int k = nz - 1; k >= 0; k--)
			{
				for (int i = 0; i < nx; i++)
					map.Append(!float.IsNaN(corridor[i, k]) ? 'c' : on[i, k] ? (!float.IsNaN(top[i, k]) ? 'R' : 's') : float.IsNegativeInfinity(height[i, k]) || height[i, k] < ground[i, k] - 3f ? '.' : 'g');
				map.Append('\n');
			}
			var tall = new Dictionary<string, int>();
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
					if (!on[i, k] && float.IsNaN(corridor[i, k]) && height[i, k] < ground[i, k] - 3f && groundBy[i, k] != null)
						tall[groundBy[i, k]] = (tall.ContainsKey(groundBy[i, k]) ? tall[groundBy[i, k]] : 0) + 1;
			LastMap = "higher ground: " + string.Join(", ", System.Linq.Enumerable.Select(tall, kv => kv.Key + " " + kv.Value)) + "\nx " + x0.ToString("0.0") + " z " + z0.ToString("0.0") + " step " + Cell + " (rows from +z)\n" + map;
			var verts = new List<Vector3>();
			var norms = new List<Vector3>();
			var uvs = new List<Vector2>();
			var tris = new List<int>();
			Vector3 origin = new Vector3(b.center.x, b.min.y, b.center.z);
			System.Func<int, int, Vector3> P = (i, k) => new Vector3(x0 + i * Cell, height[i, k], z0 + k * Cell);
			// Top: both sides (from inside a cave, a hole in its shell shows rock)
			for (int i = 0; i + 1 < nx; i++)
				for (int k = 0; k + 1 < nz; k++)
				{
					if (!on[i, k] || !on[i + 1, k] || !on[i, k + 1] || !on[i + 1, k + 1]) continue;
					Vector3 a = P(i, k), c = P(i + 1, k), d = P(i + 1, k + 1), e = P(i, k + 1);
					Vector3 n = Vector3.Cross(e - a, c - a).normalized;
					Quad(verts, norms, uvs, tris, origin, a, e, d, c, n, true);
				}
			// Walls where the surface is cut off: down to the ground; at a way in, only a skirt under its floor
			for (int i = 0; i + 1 < nx; i++)
				for (int k = 0; k + 1 < nz; k++)
				{
					if (!on[i, k] || !on[i + 1, k] || !on[i, k + 1] || !on[i + 1, k + 1]) continue;
					Edge(i, k, i + 1, k, 0, -1, nx, nz, on, top, height, ground, corridor, P, verts, norms, uvs, tris, origin);
					Edge(i, k + 1, i + 1, k + 1, 0, 1, nx, nz, on, top, height, ground, corridor, P, verts, norms, uvs, tris, origin);
					Edge(i, k, i, k + 1, -1, 0, nx, nz, on, top, height, ground, corridor, P, verts, norms, uvs, tris, origin);
					Edge(i + 1, k, i + 1, k + 1, 1, 0, nx, nz, on, top, height, ground, corridor, P, verts, norms, uvs, tris, origin);
				}
			if (tris.Count == 0) return;

			var mesh = new Mesh { name = SkinName };
			if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(verts);
			mesh.SetNormals(norms);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			skin = new GameObject(SkinName);
			skin.layer = IslandSpawner.TerrainLayer;
			skin.transform.position = origin;
			skin.transform.SetParent(transform, true);
			skin.AddComponent<MeshFilter>().sharedMesh = mesh;
			var mr = skin.AddComponent<MeshRenderer>();
			// (the island's own biggest rock, so a snow island gets snowy rock; else Raft's big rock - the shells' own is mostly dirt)
			Renderer rockLook = IslandRock();
			if (rockLook == null)
			{
				GameObject rock = PlaceableCatalog.Get("BigRock_1");
				rockLook = rock != null ? rock.GetComponentInChildren<MeshRenderer>(true) : null;
			}
			mr.sharedMaterial = rockLook != null && rockLook.sharedMaterial != null ? rockLook.sharedMaterial : look.sharedMaterial;
			mr.shadowCastingMode = look.shadowCastingMode;
			mr.receiveShadows = look.receiveShadows;
			skin.AddComponent<MeshCollider>().sharedMesh = mesh;
			Debug.Log("[CUSTOM ISLANDS] Cave mountain over " + caves.Count + " cave piece(s): " + verts.Count + " points, " + mr.sharedMaterial.name);
		}

		/// <summary>The biggest rock or boulder placed on this island (not a cave piece), or null.</summary>
		Renderer IslandRock()
		{
			Renderer best = null;
			float bestSize = 0f;
			foreach (Transform o in transform)
			{
				if (o == null || caves.Contains(o) || (skin != null && o == skin.transform)) continue;
				string n = o.name.ToLowerInvariant();
				if (!(n.Contains("rock") || n.Contains("boulder") || n.Contains("cliff")) || n.Contains("cave")) continue;
				foreach (Renderer r in o.GetComponentsInChildren<MeshRenderer>())
				{
					if (!r.enabled || r.sharedMaterial == null) continue;
					Vector3 z = r.bounds.size;
					float size = z.x * z.y * z.z;
					if (size > bestSize) { bestSize = size; best = r; }
				}
			}
			return best;
		}

		/// <summary>The edge (i1,k1)-(i2,k2) of a surface square; (di,dk) points to the square beside it.</summary>
		static void Edge(int i1, int k1, int i2, int k2, int di, int dk, int nx, int nz, bool[,] on, float[,] top, float[,] height,
			float[,] ground, float[,] corridor, System.Func<int, int, Vector3> P, List<Vector3> verts, List<Vector3> norms,
			List<Vector2> uvs, List<int> tris, Vector3 origin)
		{
			int a1 = i1 + di, c1 = k1 + dk, a2 = i2 + di, c2 = k2 + dk;
			bool inside1 = a1 >= 0 && c1 >= 0 && a1 < nx && c1 < nz, inside2 = a2 >= 0 && c2 >= 0 && a2 < nx && c2 < nz;
			if (inside1 && inside2 && on[a1, c1] && on[a2, c2]) return; // the surface goes on
			Vector3 n = new Vector3(di, 0f, dk);
			float g1 = ground[i1, k1] - 0.3f, g2 = ground[i2, k2] - 0.3f;
			float way = float.NaN;
			if (inside1 && !float.IsNaN(corridor[a1, c1])) way = corridor[a1, c1];
			if (inside2 && !float.IsNaN(corridor[a2, c2])) way = float.IsNaN(way) ? corridor[a2, c2] : Mathf.Min(way, corridor[a2, c2]);
			Vector3 p1 = P(i1, k1), p2 = P(i2, k2);
			if (!float.IsNaN(way) && (!float.IsNaN(top[i1, k1]) || !float.IsNaN(top[i2, k2])))
			{
				// The open end of a cave: its own arch is the face; under its floor, down to the ground
				if (Mathf.Min(g1, g2) > way - 0.15f) return;
				p1.y = way; p2.y = way;
				if (g1 > way) g1 = way;
				if (g2 > way) g2 = way;
			}
			if (p1.y <= g1 && p2.y <= g2) return;
			Vector3 q1 = new Vector3(p1.x, Mathf.Min(g1, p1.y), p1.z), q2 = new Vector3(p2.x, Mathf.Min(g2, p2.y), p2.z);
			Quad(verts, norms, uvs, tris, origin, q1, p1, p2, q2, n, true);
		}

		/// <summary>A four-sided face a-b-c-d with normal n (the other side too when both).</summary>
		static void Quad(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, Vector3 origin,
			Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, bool both)
		{
			for (int side = 0; side < (both ? 2 : 1); side++)
			{
				int s = verts.Count;
				Vector3 nn = side == 0 ? n : -n;
				foreach (Vector3 v in new[] { a, b, c, d })
				{
					verts.Add(v - origin);
					norms.Add(nn);
					// (mapped from the side it faces most, in world metres: the rock's texture lines up across squares)
					float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
					uvs.Add(ay >= ax && ay >= az ? new Vector2(v.x, v.z) / 14f : ax >= az ? new Vector2(v.z, v.y) / 14f : new Vector2(v.x, v.y) / 14f);
				}
				// (a-b-c-d winds so that n is its front; the back side winds the other way)
				Vector3 f = Vector3.Cross(b - a, c - a);
				bool front = Vector3.Dot(f, nn) > 0f;
				if (front) { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
				else { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
			}
		}
	}
}
