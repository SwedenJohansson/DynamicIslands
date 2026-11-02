using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A rock surface from a field over a grid (surface nets): below 0 is rock. Used by the dig brush's lining (DigTunnel) and
	/// the cave tool's mouth rock (CaveTool.Mouth). The surface is smoothed a little (the grid's steps showed as sheared
	/// spikes along the dug passage, 11-02), and each face takes the rock's texture from the side it faces most, its corners
	/// copied per side: corners shared by faces laid from different sides stretched the texture into streaks (11-02).
	/// </summary>
	public static class RockNets
	{
		/// <summary>
		/// The mesh of the field's surface. field: nx * ny * nz values, index x + y*nx + z*nx*ny, at o + (x, y, z) * h.
		/// faceSub(air, rock): the submesh of the face between those two grid points, -1 for none (still smoothed with the
		/// rest). smooth: rounds of smoothing; keep(before, after): where a smoothed corner goes (null: where smoothing put it).
		/// uvPerMetre: the texture's scale.
		/// </summary>
		public static Mesh Build(string name, float[] field, int nx, int ny, int nz, Vector3 o, float h, Func<int, int, int> faceSub, int subCount,
			int smooth, Func<Vector3, Vector3, Vector3> keep, float uvPerMetre, out int triangles)
		{
			int sy = nx, sz = nx * ny;
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

			// (a quad across each grid edge the surface crosses, its front - Unity's Cross(b - a, c - a) - toward the air)
			var quads = new List<int>();
			var subs = new List<int>();
			for (int z = 1; z < nz - 1; z++)
				for (int y = 1; y < ny - 1; y++)
					for (int x = 1; x < nx - 1; x++)
					{
						int i = x + y * sy + z * sz;
						bool rock = field[i] < 0f;
						for (int axis = 0; axis < 3; axis++)
						{
							int j = i + (axis == 0 ? 1 : axis == 1 ? sy : sz);
							bool rockJ = field[j] < 0f;
							if (rock == rockJ) continue;
							int c0, c1, c2, c3;
							if (axis == 0) { c0 = Cell(x, y - 1, z - 1, cx, cy); c1 = Cell(x, y, z - 1, cx, cy); c2 = Cell(x, y, z, cx, cy); c3 = Cell(x, y - 1, z, cx, cy); }
							else if (axis == 1) { c0 = Cell(x - 1, y, z - 1, cx, cy); c1 = Cell(x - 1, y, z, cx, cy); c2 = Cell(x, y, z, cx, cy); c3 = Cell(x, y, z - 1, cx, cy); }
							else { c0 = Cell(x - 1, y - 1, z, cx, cy); c1 = Cell(x, y - 1, z, cx, cy); c2 = Cell(x, y, z, cx, cy); c3 = Cell(x - 1, y, z, cx, cy); }
							int v0 = cellVert[c0], v1 = cellVert[c1], v2 = cellVert[c2], v3 = cellVert[c3];
							if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) continue;
							if (rock) { quads.Add(v0); quads.Add(v1); quads.Add(v2); quads.Add(v3); }
							else { quads.Add(v0); quads.Add(v3); quads.Add(v2); quads.Add(v1); }
							subs.Add(rock ? faceSub(j, i) : faceSub(i, j));
						}
					}
			cellVert = null;

			// (smoothed: each corner halfway to the middle of its neighbours, a few times)
			if (smooth > 0 && verts.Count > 0)
			{
				var sum = new Vector3[verts.Count];
				var cnt = new int[verts.Count];
				var start = verts.ToArray();
				var pos = verts.ToArray();
				for (int round = 0; round < smooth; round++)
				{
					for (int v = 0; v < pos.Length; v++) { sum[v] = Vector3.zero; cnt[v] = 0; }
					for (int q = 0; q < quads.Count; q += 4)
						for (int e = 0; e < 4; e++)
						{
							int a = quads[q + e], b = quads[q + (e + 1) % 4];
							sum[a] += pos[b]; cnt[a]++;
							sum[b] += pos[a]; cnt[b]++;
						}
					for (int v = 0; v < pos.Length; v++)
					{
						if (cnt[v] == 0) continue;
						Vector3 to = Vector3.Lerp(pos[v], sum[v] / cnt[v], 0.5f);
						pos[v] = keep != null ? keep(start[v], to) : to;
					}
				}
				verts = new List<Vector3>(pos);
			}

			// (normals from the faces shown, smooth across them)
			var norms = new Vector3[verts.Count];
			for (int q = 0; q < quads.Count; q += 4)
			{
				if (subs[q / 4] < 0) continue;
				Vector3 a = verts[quads[q]], b = verts[quads[q + 1]], c = verts[quads[q + 2]], d = verts[quads[q + 3]];
				Vector3 n = Vector3.Cross(c - a, d - b);
				for (int e = 0; e < 4; e++) norms[quads[q + e]] += n;
			}

			// (each face's texture laid on from the side it faces most: its corners copied per side)
			var copy = new int[verts.Count * 3];
			for (int k = 0; k < copy.Length; k++) copy[k] = -1;
			var outV = new List<Vector3>();
			var outN = new List<Vector3>();
			var outUV = new List<Vector2>();
			var tris = new List<int>[Mathf.Max(1, subCount)];
			for (int s = 0; s < tris.Length; s++) tris[s] = new List<int>();
			var idx = new int[4];
			for (int q = 0; q < quads.Count; q += 4)
			{
				int sub = subs[q / 4];
				if (sub < 0 || sub >= tris.Length) continue;
				Vector3 a = verts[quads[q]], b = verts[quads[q + 1]], c = verts[quads[q + 2]], d = verts[quads[q + 3]];
				Vector3 fn = Vector3.Cross(c - a, d - b);
				float ax = Mathf.Abs(fn.x), ay = Mathf.Abs(fn.y), az = Mathf.Abs(fn.z);
				int side = ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
				for (int e = 0; e < 4; e++)
				{
					int v = quads[q + e], key = v * 3 + side;
					if (copy[key] < 0)
					{
						copy[key] = outV.Count;
						Vector3 p = verts[v];
						outV.Add(p);
						outN.Add(norms[v].sqrMagnitude > 1e-12f ? norms[v].normalized : Vector3.up);
						outUV.Add((side == 1 ? new Vector2(p.x, p.z) : side == 0 ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y)) * uvPerMetre);
					}
					idx[e] = copy[key];
				}
				List<int> into = tris[sub];
				into.Add(idx[0]); into.Add(idx[1]); into.Add(idx[2]);
				into.Add(idx[0]); into.Add(idx[2]); into.Add(idx[3]);
			}
			var mesh = new Mesh { name = name };
			if (outV.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(outV);
			mesh.SetNormals(outN);
			mesh.SetUVs(0, outUV);
			mesh.subMeshCount = tris.Length;
			triangles = 0;
			for (int s = 0; s < tris.Length; s++) { mesh.SetTriangles(tris[s], s); triangles += tris[s].Count / 3; }
			mesh.RecalculateBounds();
			try { mesh.RecalculateTangents(); } catch { }
			return mesh;
		}

		static int Cell(int x, int y, int z, int cx, int cy) { return x + y * cx + z * cx * cy; }

		/// <summary>Signed distance (2D) from p to a closed outline: below 0 inside it.</summary>
		public static float PolygonDistance(List<Vector2> poly, Vector2 p)
		{
			float best = float.MaxValue;
			bool inside = false;
			for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
			{
				Vector2 a = poly[j], b = poly[i], e = b - a, w = p - a;
				float t = Mathf.Clamp01(Vector2.Dot(w, e) / Mathf.Max(1e-8f, e.sqrMagnitude));
				float d = (w - e * t).sqrMagnitude;
				if (d < best) best = d;
				if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) / (b.y - a.y) * e.x) inside = !inside;
			}
			best = Mathf.Sqrt(best);
			return inside ? -best : best;
		}
	}
}
