using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's zipline lines with a far end of the builder's choosing (the user, 2026-10-05: ziplines on the islands). Raft's
	/// line (MeshPath_Zipline_Landmark) runs between two connect points over two small floors: the object stands at one end,
	/// and the setting zip.to ("x y z", the island's own coordinates, like an object's position) is where the other end's
	/// floor stands. In a world its floor and connect point move there and Raft makes the line again (ForceCreatePath);
	/// without the setting the line is Raft's own (Tangaroa's 17.6 m, Caravan Town's 31 m down a 14.5 m drop).
	/// </summary>
	public static class ZiplineEnds
	{
		public const string ZipTo = "zip.to";
		/// <summary>The rope's slack (Raft's own: the line's slack field; its rope sags about 3.4 m on a 20 m line). The recipe's
		/// zipto sets a tighter one where raising the near end alone would take a tower (11-20).</summary>
		public const string ZipSlack = "zip.slack";
		/// <summary>The ground check fails a zipline whose rope comes under LineLow m over the ground, leaving out the ends'
		/// stretches (NearSkip, FarSkip) where it comes down to its floors (11-20: Outer Breakwater's ridge line ran up to
		/// 1.1 m inside the ridge's shoulder, both its floors on the ground).</summary>
		public const float LineLow = 0.5f, NearSkip = 1.5f, FarSkip = 3f;
		/// <summary>The ground check also fails a zipline whose rope, in the last FarSkip m down to its far end, comes under
		/// FarEndLow m over the ground (11-21 c3z13: Caisson Yard's summit line ended in the cave hill's sand, its far floor on
		/// the land under the hill's skin; the left-out stretch hid it).</summary>
		/// FarEndLow is a little under 0: a rope meeting its own far floor touches it by a few mm (11-21 c3z17/18: Thalassa
		/// Mooring 0.0096 m, Engine Court 0.0038 m at the floor itself), not a cut like Caisson Yard's 0.24 m.
		public const float FarEndLow = -0.05f;
		/// <summary>zipto keeps the rope's last FarSkip m at least FarEndClear m over the cave hill's skin (a world's hill comes out a
		/// little off the editor's measuring one: 11-21 c3z21, 0.2 m at Caisson Yard's summit floor) and out of the land and what
		/// stands on it (FarEndLow), raising the far end on a deck up to FarMaxRaise m (11-21 c3z15: Caisson Yard's summit rope,
		/// its far end on the hill's skin, ran 0.24 m into the dune's slope just before it).</summary>
		public const float FarEndClear = 0.3f, FarMaxRaise = 3f;

		/// <summary>The rope's lowest over the ground in its last FarSkip m, its far end included (FarEndLow).</summary>
		public static float FarStretchLowest(List<Vector3> rope, Vector3 near, Vector3 far, System.Func<float, float, float> ground, out Vector3 at)
		{
			float t, len = new Vector2(far.x - near.x, far.z - near.z).magnitude;
			return RopeLowest(rope, near, far, ground, Mathf.Max(0f, len - FarSkip), 0f, null, out at, out t);
		}
		/// <summary>The rope's room over the ground a rider needs (zipride 11-20: Raft's rider hangs about 2 m under the rope,
		/// and lets go - or is stopped - where the ground comes up to him: Engine Court's and Caisson Yard's riders stopped halfway,
		/// Outer Breakwater's went 3 m through the ridge's shoulder with the rope 1 m over it).</summary>
		public const float RiderClear = 2.6f;
		/// <summary>zipto's raise (the recipe's ZipClearGround): the rope's own room when a rider's would take more than
		/// MaxRaise m of deck (a 20 m deck on Engine Court's ridge); the last RiderFarSkip m left out of a rider's room (zipride
		/// counts a rider stopped there as carried).</summary>
		public const float LineClear = 1f, MaxRaise = 8f, RiderFarSkip = 5f;

		/// <summary>The room a rider wants d m before the far end (RopeLowest's want), on a line falling drop m over len m:
		/// RiderClear, less where the rope's own fall to its far floor gives less - but full again RiderFarSkip m past the
		/// left-out last stretch, whatever the fall (11-21: Thalassa Mooring's ridge line fell 1.6 m over 23 m, so the fall
		/// alone asked 1.2 m of room 2 m from its start, and the rider let go there 0.2 m over the ground).</summary>
		public static System.Func<float, float> RiderWant(float drop, float len)
		{
			float slope = 0.8f * Mathf.Max(0f, drop) / Mathf.Max(1f, len);
			return d => Mathf.Min(RiderClear, Mathf.Max(slope * d, RiderClear * (d - RiderFarSkip) / RiderFarSkip));
		}

		static readonly FieldInfo pointA = typeof(MeshPath_Zipline_Landmark).GetField("pointA", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		static readonly FieldInfo pointB = typeof(MeshPath_Zipline_Landmark).GetField("pointB", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

		public static bool TryParse(string text, out Vector3 v)
		{
			v = Vector3.zero;
			string[] p = (text ?? "").Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
			float x, y, z;
			if (p.Length != 3 || !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
				!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
			v = new Vector3(x, y, z);
			return true;
		}

		public static string Text(Vector3 v) { return v.x.ToString("0.##", CultureInfo.InvariantCulture) + " " + v.y.ToString("0.##", CultureInfo.InvariantCulture) + " " + v.z.ToString("0.##", CultureInfo.InvariantCulture); }

		/// <summary>Moves a spawned zipline's far end to its zip.to (island origin: the island's root position). Returns whether it moved.</summary>
		public static bool Apply(GameObject go, IDictionary<string, string> props, Vector3 islandOrigin)
		{
			Vector3 to;
			if (go == null || props == null || !TryParse(ObjectProps.Get(props, ZipTo), out to)) return false;
			MeshPath_Zipline_Landmark line = go.GetComponentInChildren<MeshPath_Zipline_Landmark>(true);
			Transform a = line != null && pointA != null ? pointA.GetValue(line) as Transform : null;
			if (a == null) return false;
			// (everything of the far end goes with it: each ZiplineBase piece nearer the far connect point than the near one -
			// the floor and its stand. Before 2026-10-20 only the nearest piece moved, and not even
			// that when it held the connect point: the Lodestone Array's far-end floor stayed on the slope where Raft's own 31 m line ends, 1.7 m in the air)
			List<Transform> pieces = FarPieces(go, line, a);
			Transform floor = pieces.OrderBy(t => (t.position - a.position).sqrMagnitude).FirstOrDefault();
			Vector3 target = islandOrigin + to;
			Vector3 delta = target - (floor != null ? floor.position : a.position);
			foreach (Transform t in pieces) t.position += delta;
			if (!pieces.Any(t => a.IsChildOf(t))) a.position += delta;
			float slack;
			if (slackF != null && float.TryParse(ObjectProps.Get(props, ZipSlack) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out slack) && !float.IsNaN(slack) && !float.IsInfinity(slack))
				slackF.SetValue(line, slack);
			try { line.ForceCreatePath(); }
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Zipline end: " + e.Message); return false; }
			// (Raft's own collider follows the made path: re-enable so it is rebuilt where the line now runs)
			foreach (MeshCollider mc in go.GetComponentsInChildren<MeshCollider>(true))
			{
				MeshFilter mf = mc.GetComponent<MeshFilter>();
				if (mf != null && mf.sharedMesh != null) mc.sharedMesh = mf.sharedMesh;
				mc.enabled = false; mc.enabled = true;
			}
			return true;
		}

		/// <summary>The far end's pieces (Apply moves them): each top ZiplineBase piece nearer the far connect point than the near one.</summary>
		static List<Transform> FarPieces(GameObject go, MeshPath_Zipline_Landmark line, Transform a)
		{
			Transform bEnd = pointB != null ? pointB.GetValue(line) as Transform : null;
			Vector3 near = bEnd != null ? bEnd.position : line.transform.position;
			List<Transform> pieces = go.GetComponentsInChildren<Transform>(true).Where(t => t != a && t != go.transform && (bEnd == null || !bEnd.IsChildOf(t)) && !line.transform.IsChildOf(t) &&
				t.name.IndexOf("ZiplineBase", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			return pieces.Where(t => !pieces.Any(o => o != t && t.IsChildOf(o)) && (t.position - a.position).sqrMagnitude < (t.position - near).sqrMagnitude).ToList();
		}

		/// <summary>The line's two connect points (world) as a world makes them: near (with the object) and far (where zip.to
		/// moves it, when set - as Apply does). False when the object has no zipline line.</summary>
		public static bool Ends(GameObject go, IDictionary<string, string> props, Vector3 islandOrigin, out Vector3 near, out Vector3 far)
		{
			near = far = Vector3.zero;
			MeshPath_Zipline_Landmark line = go != null ? go.GetComponentInChildren<MeshPath_Zipline_Landmark>(true) : null;
			Transform a = line != null && pointA != null ? pointA.GetValue(line) as Transform : null;
			if (a == null) return false;
			Transform bEnd = pointB != null ? pointB.GetValue(line) as Transform : null;
			near = bEnd != null ? bEnd.position : line.transform.position;
			far = a.position;
			Vector3 to;
			if (props != null && TryParse(ObjectProps.Get(props, ZipTo), out to))
			{
				Transform floor = FarPieces(go, line, a).OrderBy(t => (t.position - a.position).sqrMagnitude).FirstOrDefault();
				far += islandOrigin + to - (floor != null ? floor.position : a.position);
			}
			return true;
		}

		static readonly FieldInfo pathPoints = typeof(MeshPath).GetField("points", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		static readonly FieldInfo slackF = typeof(MeshPath_Zipline_Landmark).GetField("slack", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		static readonly FieldInfo minPointsF = typeof(MeshPath_Zipline_Landmark).GetField("minPoints", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		static readonly FieldInfo maxPointsF = typeof(MeshPath_Zipline_Landmark).GetField("maxPoints", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

		/// <summary>The rope as Raft made it (world points, its far end first: Raft's rope sags between its ends - the
		/// line's slack - so it runs lower than the straight line). Empty when it has none.</summary>
		public static List<Vector3> Rope(MeshPath line)
		{
			var list = new List<Vector3>();
			int n = line != null && pathPoints != null ? ((pathPoints.GetValue(line) as List<Vector3>)?.Count ?? 0) : 0;
			for (int i = 0; i < n; i++) list.Add(line.GetWorldPointPositionFromIndex(i));
			return list;
		}

		/// <summary>The line's own slack (Raft's, or zip.slack's once Apply has set it; Raft's is negative - the rope's sag - and a tighter rope's nearer 0); NaN when it can't be read.</summary>
		public static float Slack(MeshPath_Zipline_Landmark line)
		{
			try { return line != null && slackF != null ? (float)slackF.GetValue(line) : float.NaN; } catch { return float.NaN; }
		}

		/// <summary>The rope Raft makes between far and near (world), as a world makes it once zip.to has moved the far end
		/// (CreatePath with the line's own slack, or the given one, and its point counts), then the line's own rope made again. Empty when it can't.</summary>
		public static List<Vector3> RopeBetween(MeshPath_Zipline_Landmark line, Vector3 far, Vector3 near, float slack = float.NaN)
		{
			var list = new List<Vector3>();
			if (line == null || slackF == null) return list;
			try
			{
				line.CreatePath(far, near, !float.IsNaN(slack) ? slack : (float)slackF.GetValue(line), false, minPointsF != null ? (int)minPointsF.GetValue(line) : 10, maxPointsF != null ? (int)maxPointsF.GetValue(line) : 60);
				list = Rope(line);
			}
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Zipline rope: " + e.Message); }
			try { line.ForceCreatePath(); } catch { }
			return list;
		}

		/// <summary>The ground under a rope at x z: <paramref name="land"/>'s, or the top of a cave hill's skin there when higher
		/// (CaveMountain: a world builds it, the land's height leaves it out - 11-21 c3z9, Caisson Yard's summit rope 0.32 m in it).</summary>
		public static System.Func<float, float, float> OverSkins(System.Func<float, float, float> land, IList<Collider> skins)
		{
			if (skins == null || skins.Count == 0) return land;
			return (x, z) =>
			{
				float g = land(x, z);
				bool backs = Physics.queriesHitBackfaces;
				Physics.queriesHitBackfaces = true;
				try
				{
					foreach (Collider c in skins)
					{
						if (c == null || !c.enabled) continue;
						Bounds b = c.bounds;
						RaycastHit h;
						if (x >= b.min.x && x <= b.max.x && z >= b.min.z && z <= b.max.z &&
							c.Raycast(new Ray(new Vector3(x, b.max.y + 1f, z), Vector3.down), out h, b.size.y + 2f) && h.point.y > g) g = h.point.y;
					}
				}
				finally { Physics.queriesHitBackfaces = backs; }
				return g;
			};
		}

		/// <summary>The cave hills' skins (CaveMountain) under an island's root.</summary>
		public static List<Collider> CaveSkins(Transform root)
		{
			return root == null ? new List<Collider>() : root.GetComponentsInChildren<MeshCollider>(true).Where(c => c.name == CaveMountain.SkinName).Cast<Collider>().ToList();
		}

		/// <summary>Where a rope (points, either end first) comes nearest the ground: how far over it (negative: under), every
		/// 0.25 m, leaving out the first nearSkip m (along the ground) from near and the last farSkip m to far, less want(metres
		/// to the far end): the room asked there (null: none). at, atT: that point and how far along (0 near, 1 far). 999 when nothing is left.</summary>
		public static float RopeLowest(List<Vector3> rope, Vector3 near, Vector3 far, System.Func<float, float, float> ground, float nearSkip, float farSkip, System.Func<float, float> want, out Vector3 at, out float atT)
		{
			at = near; atT = 0f;
			float len = new Vector2(far.x - near.x, far.z - near.z).magnitude, low = 999f;
			if (rope == null || rope.Count < 2 || len < 0.5f) return low;
			for (int i = 0; i + 1 < rope.Count; i++)
			{
				Vector3 a = rope[i], b = rope[i + 1];
				int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(b.x - a.x, b.z - a.z).magnitude / 0.25f));
				for (int k = 0; k <= steps; k++)
				{
					Vector3 p = Vector3.Lerp(a, b, k / (float)steps);
					float fromNear = new Vector2(p.x - near.x, p.z - near.z).magnitude, toFar = new Vector2(far.x - p.x, far.z - p.z).magnitude;
					if (fromNear < nearSkip || toFar < farSkip) continue;
					float t = Mathf.Clamp01(fromNear / len), over = p.y - ground(p.x, p.z) - (want != null ? want(toFar) : 0f);
					if (over < low) { low = over; at = p; atT = t; }
				}
			}
			return low;
		}

		/// <summary>The far end's position in the island's coordinates (for the generator and recipes): zip.to as text.</summary>
		public static Vector3? FarEnd(IDictionary<string, string> props) { Vector3 v; return TryParse(ObjectProps.Get(props, ZipTo), out v) ? v : (Vector3?)null; }

		/// <summary>Where the near end's floor stands (world), the end that stays with the object: the ZiplineBase piece
		/// nearest the near connect point (that point itself when there is none). Null when the object has no zipline line.</summary>
		public static Vector3? NearFloor(GameObject go)
		{
			MeshPath_Zipline_Landmark line = go != null ? go.GetComponentInChildren<MeshPath_Zipline_Landmark>(true) : null;
			Transform a = line != null && pointA != null ? pointA.GetValue(line) as Transform : null;
			if (line == null) return null;
			Transform bEnd = pointB != null ? pointB.GetValue(line) as Transform : null;
			Vector3 near = bEnd != null ? bEnd.position : line.transform.position;
			Transform floor = go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && t.name.IndexOf("ZiplineBase", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
				(a == null || (t.position - near).sqrMagnitude < (t.position - a.position).sqrMagnitude)).OrderBy(t => (t.position - near).sqrMagnitude).FirstOrDefault();
			return floor != null ? floor.position : near;
		}
	}
}
