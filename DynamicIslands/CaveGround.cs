using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// In a world, the island's ground where it runs through a cave piece's rooms - the hill a cave was set into, the ground
	/// beside a passage - over the shell's floor and under its roof, is cut away (terrain holes; X7): the room is the rock's own
	/// floor, walls and roof. Raft draws the ground from above only: from inside a room it lay across, it was a window out.
	/// </summary>
	public class CaveGround : MonoBehaviour
	{
		readonly List<Transform> caves = new List<Transform>();

		/// <summary>Tests: how many ground cells the last cut took out.</summary>
		public int LastCut;

		/// <summary>A cave piece spawned in a world under this island's objects.</summary>
		public static void Add(Transform objects, GameObject piece, string name)
		{
			PropInfo p = RaftProps.Get(name ?? "");
			if (objects == null || piece == null || p == null || !p.IsCave) return;
			CaveGround g = objects.GetComponent<CaveGround>() ?? objects.gameObject.AddComponent<CaveGround>();
			g.caves.Add(piece.transform);
		}

		/// <summary>The island's objects all there and switched on (their colliders can be hit): the ground in its caves' rooms cut away.</summary>
		public static void Cut(Transform objects)
		{
			CaveGround g = objects != null ? objects.GetComponent<CaveGround>() : null;
			if (g == null) return;
			try { g.CutNow(); }
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Cave ground: " + e.Message); }
		}

		/// <summary>Cuts it again now (tests).</summary>
		public void CutNow()
		{
			caves.RemoveAll(t => t == null);
			LastCut = 0;
			if (caves.Count == 0) return;
			Transform island = transform.parent != null ? transform.parent : transform;
			float started = Time.realtimeSinceStartup;
			bool backs0 = Physics.queriesHitBackfaces;
			Physics.SyncTransforms();
			try
			{
				foreach (Terrain land in island.GetComponentsInChildren<Terrain>())
				{
					TerrainData data = land.terrainData;
					int res = data.heightmapResolution, cells = res - 1;
					float sx = data.size.x / cells, sz = data.size.z / cells;
					Vector3 o = land.transform.position;
					float[,] h = data.GetHeights(0, 0, res, res);
					// (each point of the ground: in one of the rooms - a piece's own roof over it and its floor under it, both
					// seen from inside)
					var inside = new bool[res, res];
					int lo = res, hi = -1, left = res, right = -1;
					foreach (Transform cave in caves)
					{
						var own = new HashSet<Collider>(cave.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger));
						if (own.Count == 0) continue;
						Bounds b = own.First().bounds;
						foreach (Collider c in own) b.Encapsulate(c.bounds);
						int x0 = Mathf.Max(0, Mathf.FloorToInt((b.min.x - o.x) / sx)), x1 = Mathf.Min(cells, Mathf.CeilToInt((b.max.x - o.x) / sx));
						int z0 = Mathf.Max(0, Mathf.FloorToInt((b.min.z - o.z) / sz)), z1 = Mathf.Min(cells, Mathf.CeilToInt((b.max.z - o.z) / sz));
						for (int z = z0; z <= z1; z++)
							for (int x = x0; x <= x1; x++)
							{
								if (inside[z, x]) continue;
								Vector3 p = new Vector3(o.x + x * sx, o.y + h[z, x] * data.size.y, o.z + z * sz);
								if (p.y <= b.min.y || p.y >= b.max.y) continue;
								if (!Inside(p, own, b)) continue;
								inside[z, x] = true;
								lo = Mathf.Min(lo, z); hi = Mathf.Max(hi, z); left = Mathf.Min(left, x); right = Mathf.Max(right, x);
							}
					}
					if (hi < 0) continue;
					// (a cell goes when all four of its corners are in a room: none is cut out past a wall, where it would open
					// a gap beside the rock)
					bool[,] solid = data.GetHoles(0, 0, cells, cells);
					int cut = 0;
					for (int z = lo; z < hi; z++)
						for (int x = left; x < right; x++)
							if (solid[z, x] && inside[z, x] && inside[z, x + 1] && inside[z + 1, x] && inside[z + 1, x + 1]) { solid[z, x] = false; cut++; }
					if (cut == 0) continue;
					data.SetHoles(0, 0, solid);
					LastCut += cut;
				}
			}
			finally { Physics.queriesHitBackfaces = backs0; }
			if (LastCut > 0)
				Debug.Log("[CUSTOM ISLANDS] '" + island.name + "': the ground cut out of " + caves.Count + " cave piece(s)' rooms (" + LastCut + " cells) in " +
					((Time.realtimeSinceStartup - started) * 1000f).ToString("F0") + " ms");
		}

		/// <summary>A point of the ground in the piece's room: its nearest faces straight up and straight down both seen from their fronts (the shells face inwards).</summary>
		static bool Inside(Vector3 p, HashSet<Collider> own, Bounds b)
		{
			Vector3 from = p + Vector3.up * 0.02f;
			return FrontFirst(from, Vector3.down, from.y - b.min.y + 1f, own) && FrontFirst(from, Vector3.up, b.max.y - from.y + 1f, own);
		}

		static bool FrontFirst(Vector3 from, Vector3 dir, float len, HashSet<Collider> own)
		{
			float front = Nearest(from, dir, len, own, false);
			return front < float.PositiveInfinity && front <= Nearest(from, dir, len, own, true) + 0.01f;
		}

		static float Nearest(Vector3 from, Vector3 dir, float len, HashSet<Collider> own, bool backs)
		{
			Physics.queriesHitBackfaces = backs;
			float best = float.PositiveInfinity;
			foreach (RaycastHit hit in Physics.RaycastAll(from, dir, len, ~0, QueryTriggerInteraction.Ignore))
				if (hit.distance < best && own.Contains(hit.collider)) best = hit.distance;
			return best;
		}
	}
}
