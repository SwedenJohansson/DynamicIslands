using System.Collections.Generic;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Where the editor's Test in a world puts the player (a Discord user, 2026-10-26: an island whose land doesn't cover its
	/// middle dropped the tester in the sea, or on the sea floor, to swim to it). The island setting test.start ("x y z", the
	/// island's own coordinates, like zip.to and an object's position) is set in the Island tab by "Test start: where I
	/// look" and cleared by Clear; a small marker shows it in the editor only. Islands without it - every island made before -
	/// get the middle as before, and when nothing stands above the sea there, the dry land nearest the middle. Only Test
	/// uses it: players arriving in a world come by raft, wherever they like.
	/// </summary>
	public static class TestStart
	{
		public const string Key = "test.start";
		public const string MarkerName = "CustomIslands_TestStartMarker";
		/// <summary>The player's feet go this far above the ground found (as Test always did).</summary>
		const float StandAbove = 1.5f;

		/// <summary>The spot in the island's coordinates, or null when none is set (or the text is not three numbers).</summary>
		public static Vector3? Get(IDictionary<string, string> props)
		{
			Vector3 v;
			return props != null && ZiplineEnds.TryParse(ObjectProps.Get(props, Key), out v) ? v : (Vector3?)null;
		}

		#region The editor

		static Vector3 Origin { get { Terrain t = terraineditor.terrain; return t != null ? t.transform.position : Vector3.zero; } }

		/// <summary>The ground, deck or roof the editor's camera looks at (the screen's middle), or null when it looks at open water or sky.</summary>
		public static Vector3? LookPoint()
		{
			Camera cam = Camera.main;
			if (cam == null) return null;
			RaycastHit hit;
			return Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 2000f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : (Vector3?)null;
		}

		/// <summary>Island tab's "Test start: where I look": the spot (editor world position) as the island's test start, one undo step.</summary>
		public static bool SetAt(Vector3? at)
		{
			if (!DynamicIslands.InEditor()) return false;
			if (!at.HasValue) { DynamicIslands.Notify("Look at the ground (or a deck, a roof) where Test should put you, then click again", true); return false; }
			string text = ZiplineEnds.Text(at.Value - Origin);
			IslandSettingsUndo.Change(() => DynamicIslands.currentIslandProps[Key] = text);
			EditorUI.RefreshIsland();
			bool wet = at.Value.y < DynamicIslands.EditorWaterLevel - 0.3f;
			DynamicIslands.Notify("Test start set" + (wet ? " (under water: Test puts you there)" : "") + ": Test in a world puts you here. Ctrl+Z undoes", wet);
			return true;
		}

		/// <summary>Island tab's Clear: no test start (Test puts you on the island's middle again), one undo step.</summary>
		public static void Clear()
		{
			if (!DynamicIslands.InEditor()) return;
			if (!DynamicIslands.currentIslandProps.ContainsKey(Key)) { DynamicIslands.Notify("No test start set: Test puts you on the island's middle"); return; }
			IslandSettingsUndo.Change(() => DynamicIslands.currentIslandProps.Remove(Key));
			EditorUI.RefreshIsland();
			DynamicIslands.Notify("Test start cleared: Test puts you on the island's middle");
		}

		/// <summary>What the Island tab says about it.</summary>
		public static string Describe()
		{
			Vector3? s = Get(DynamicIslands.currentIslandProps);
			return s.HasValue ? "Test puts you at the marker (" + (s.Value.y + Origin.y - DynamicIslands.EditorWaterLevel).ToString("+0;-0") + " m from the sea)."
				: "Not set: Test puts you on the island's middle (or the land nearest it).";
		}

		static GameObject marker;

		/// <summary>The marker where the test start is: shown in the editor while one is set, else hidden. No collider, not a
		/// placed object (never saved, never picked); it goes with the editor's scene.</summary>
		public static void RefreshMarker()
		{
			Vector3? local = DynamicIslands.InEditor() ? Get(DynamicIslands.currentIslandProps) : null;
			if (!local.HasValue) { if (marker != null) marker.SetActive(false); return; }
			if (marker == null) marker = MakeMarker();
			marker.transform.position = Origin + local.Value;
			marker.SetActive(true);
		}

		/// <summary>Tests: the marker, when shown.</summary>
		internal static GameObject Marker { get { return marker != null && marker.activeSelf ? marker : null; } }

		/// <summary>A player-high pole (1.8 m) on a flat ring, with a ball on top: bright green, unlit.</summary>
		static GameObject MakeMarker()
		{
			GameObject root = new GameObject(MarkerName);
			Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
			Material mat = shader != null ? new Material(shader) { color = new Color(0.25f, 1f, 0.35f, 1f) } : null;
			Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.9f, 0f), new Vector3(0.12f, 0.9f, 0.12f), mat);
			Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, 1.9f, 0f), new Vector3(0.4f, 0.4f, 0.4f), mat);
			Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(1.2f, 0.03f, 1.2f), mat);
			return root;
		}

		static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
		{
			GameObject g = GameObject.CreatePrimitive(type);
			Collider c = g.GetComponent<Collider>();
			if (c != null) Object.DestroyImmediate(c); // (camera and placing rays pass through it)
			g.transform.SetParent(parent, false);
			g.transform.localPosition = pos;
			g.transform.localScale = scale;
			Renderer r = g.GetComponent<Renderer>();
			if (r != null && mat != null) { r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
		}

		#endregion

		#region In the test world

		/// <summary>The first thing below <paramref name="from"/> (within <paramref name="depth"/> m), or null.
		/// With <paramref name="only"/>, the first thing of that island (Raft's own island or rock where a custom island was
		/// put over it - a test world's randomizer landmark - doesn't count).</summary>
		internal static Vector3? GroundBelow(Vector3 from, float depth, Transform only = null)
		{
			RaycastHit hit;
			if (only == null) return Physics.Raycast(from, Vector3.down, out hit, depth, ~0, QueryTriggerInteraction.Ignore) ? hit.point : (Vector3?)null;
			RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, depth, ~0, QueryTriggerInteraction.Ignore);
			Vector3? best = null;
			float bestD = float.MaxValue;
			foreach (RaycastHit h in hits)
				if (h.distance < bestD && h.collider.transform.IsChildOf(only)) { bestD = h.distance; best = h.point; }
			return best;
		}

		/// <summary>Where Test stands the player on a spawned island: on the ground at its test start; without one, as
		/// <see cref="Fallback"/>. <paramref name="how"/> says which (the test's log).</summary>
		public static Vector3 StandPoint(IslandWorldState.Entry e, out string how)
		{
			Vector3? local = e != null && e.Root != null ? Get(IslandCache.PropsOf(e)) : null;
			if (local.HasValue)
			{
				Vector3 p = e.Root.transform.position + local.Value;
				// (from just above the spot first: a spot inside a cave or under a roof keeps its floor; then from high above)
				Vector3? g = GroundBelow(p + Vector3.up * 2.5f, 30f) ?? GroundBelow(p + Vector3.up * 400f, 800f);
				if (g.HasValue) { how = "the island's test start"; return g.Value + Vector3.up * StandAbove; }
			}
			return Fallback(e, out how);
		}

		/// <summary>No test start: the ground at the island's middle (as before), unless nothing there stands above the
		/// island's sea - then the dry land nearest the middle (an atoll, a crescent), not the sea floor.</summary>
		public static Vector3 Fallback(IslandWorldState.Entry e, out string how)
		{
			Vector3 centre = e.Position;
			float sea = e.Position.y; // (the island's own sea: its elevation in the world - 0 at sea, 60 flying, -30 sunken)
			Transform root = e.Root != null ? e.Root.transform : null;
			Vector3? mid = GroundBelow(centre + Vector3.up * 400f, 800f, root);
			if (mid.HasValue && mid.Value.y > sea + 0.2f) { how = "the island's middle"; return mid.Value + Vector3.up * StandAbove; }
			// (a little inland first, so the tester stands on the land and not at its wet edge)
			Vector3? land = e.Root != null ? NearestLand(e.Root.transform, centre, sea + 1.5f) ?? NearestLand(e.Root.transform, centre, sea + 0.3f) : null;
			if (land.HasValue)
			{
				Vector3? g = GroundBelow(land.Value + Vector3.up * 400f, 800f, root);
				how = "the land nearest the island's middle";
				return (g.HasValue && g.Value.y > sea ? g.Value : land.Value) + Vector3.up * StandAbove;
			}
			how = "the island's middle (no land above its sea)";
			return mid.HasValue ? mid.Value + Vector3.up * StandAbove : centre + Vector3.up * 5f;
		}

		/// <summary>The point of the island's terrain higher than <paramref name="above"/> nearest <paramref name="centre"/>
		/// (flat distance), or null when there is none.</summary>
		internal static Vector3? NearestLand(Transform root, Vector3 centre, float above)
		{
			Vector3? best = null;
			float bestD = float.MaxValue;
			foreach (Terrain t in root.GetComponentsInChildren<Terrain>(false))
			{
				TerrainData d = t.terrainData;
				if (d == null) continue;
				int res = d.heightmapResolution;
				float[,] h = d.GetHeights(0, 0, res, res);
				Vector3 size = d.size, at = t.transform.position;
				for (int z = 0; z < res; z++)
					for (int x = 0; x < res; x++)
					{
						float y = at.y + h[z, x] * size.y;
						if (y < above) continue;
						float wx = at.x + x * size.x / (res - 1), wz = at.z + z * size.z / (res - 1);
						float dd = (wx - centre.x) * (wx - centre.x) + (wz - centre.z) * (wz - centre.z);
						if (dd < bestD) { bestD = dd; best = new Vector3(wx, y, wz); }
					}
			}
			return best;
		}

		#endregion
	}
}
