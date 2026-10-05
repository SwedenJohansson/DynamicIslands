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

		static readonly FieldInfo pointA = typeof(MeshPath_Zipline_Landmark).GetField("pointA", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

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
			// (the floor under the connect point goes with it: the nearest of the line's two floors)
			Transform floor = go.GetComponentsInChildren<Transform>(true).Where(t => t != a && t.name.IndexOf("ZiplineBase", System.StringComparison.OrdinalIgnoreCase) >= 0)
				.OrderBy(t => (t.position - a.position).sqrMagnitude).FirstOrDefault();
			Vector3 target = islandOrigin + to;
			Vector3 delta = target - (floor != null ? floor.position : a.position);
			if (floor != null && !a.IsChildOf(floor)) floor.position += delta;
			a.position += delta;
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

		/// <summary>The far end's position in the island's coordinates (for the generator and recipes): zip.to as text.</summary>
		public static Vector3? FarEnd(IDictionary<string, string> props) { Vector3 v; return TryParse(ObjectProps.Get(props, ZipTo), out v) ? v : (Vector3?)null; }
	}
}
