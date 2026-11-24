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
				float top = RayTop(e);
				// (from just above the spot first: a spot inside a cave or under a roof keeps its floor; then from above the island)
				Vector3? g = GroundBelow(p + Vector3.up * 2.5f, 30f) ?? GroundBelow(new Vector3(p.x, top, p.z), top - p.y + 400f);
				if (g.HasValue) { how = "the island's test start"; return g.Value + Vector3.up * StandAbove; }
			}
			return Fallback(e, out how);
		}

		/// <summary>Ground steeper than this (degrees) isn't a place to put a player: they slide off it (Raft's player walks up to 45).</summary>
		const float FlatSlope = 30f;
		/// <summary>The middle is kept when it is no higher than this above the island's sea (a hill top). A peak gives way to low land.</summary>
		const float HighMiddle = 60f;
		/// <summary>A beach or a low flat spot: dry land up to this high above the sea.</summary>
		const float LowLand = 25f;
		/// <summary>The flat ground found reaches this far round the spot (m): a player put at the foot of a cliff touched it, not grounded.</summary>
		const float FlatRound = 3f;

		/// <summary>Where the rays down onto the island start: above everything it can have (PF-D7, 2026-10-26: the ray from 400 m
		/// above the sea started inside a peak higher than that - land goes up to 580 m above the sea - and found the inside
		/// of the mountain; the tester stood under its ground and Raft moved them off the island).</summary>
		internal static float RayTop(IslandWorldState.Entry e)
		{
			float top = e.Position.y + 400f;
			if (e.Root != null)
				foreach (Terrain t in e.Root.GetComponentsInChildren<Terrain>(false))
					if (t.terrainData != null) top = Mathf.Max(top, t.transform.position.y + t.terrainData.size.y + 20f);
			return top;
		}

		/// <summary>No test start: the ground at the island's middle (as before) when it is dry, not steep and not a high peak;
		/// otherwise the low flat land (a beach) nearest the middle, else any flat dry land nearest it (PF-D7); and when the
		/// middle is under the sea and no flat land is found, the dry land nearest the middle (an atoll, a crescent).</summary>
		public static Vector3 Fallback(IslandWorldState.Entry e, out string how)
		{
			Vector3 centre = e.Position;
			float sea = e.Position.y; // (the island's own sea: its elevation in the world - 0 at sea, 60 flying, -30 sunken)
			Transform root = e.Root != null ? e.Root.transform : null;
			float top = RayTop(e), depth = top - sea + 400f;
			Vector3? mid = GroundBelow(new Vector3(centre.x, top, centre.z), depth, root);
			bool dry = mid.HasValue && mid.Value.y > sea + 0.2f;
			if (dry && mid.Value.y <= sea + HighMiddle && !Steep(root, mid.Value)) { how = "the island's middle"; return mid.Value + Vector3.up * StandAbove; }
			if (root != null)
			{
				Vector3? flat = FlatLand(root, centre, sea + 1.5f, sea + LowLand) ?? FlatLand(root, centre, sea + 1.5f, float.MaxValue);
				if (flat.HasValue)
				{
					Vector3? g = GroundBelow(new Vector3(flat.Value.x, top, flat.Value.z), depth, root);
					how = dry ? "the low flat land nearest the island's middle (its middle is " + (mid.Value.y > sea + HighMiddle ? "a peak " + (mid.Value.y - sea).ToString("0") + " m high" : "too steep") + ")"
						: "the flat land nearest the island's middle";
					return (g.HasValue && g.Value.y > sea ? g.Value : flat.Value) + Vector3.up * StandAbove;
				}
			}
			if (dry) { how = "the island's middle"; return mid.Value + Vector3.up * StandAbove; }
			// (a little inland first, so the tester stands on the land and not at its wet edge)
			Vector3? land = root != null ? NearestLand(root, centre, sea + 1.5f) ?? NearestLand(root, centre, sea + 0.3f) : null;
			if (land.HasValue)
			{
				Vector3? g = GroundBelow(new Vector3(land.Value.x, top, land.Value.z), depth, root);
				how = "the land nearest the island's middle";
				return (g.HasValue && g.Value.y > sea ? g.Value : land.Value) + Vector3.up * StandAbove;
			}
			how = "the island's middle (no land above its sea)";
			return mid.HasValue ? mid.Value + Vector3.up * StandAbove : centre + Vector3.up * 5f;
		}

		/// <summary>Whether the island's ground at this point is too steep to stand on. A point on an object (a deck, a roof) or
		/// off the island's land isn't.</summary>
		internal static bool Steep(Transform root, Vector3 at)
		{
			if (root == null) return false;
			foreach (Terrain t in root.GetComponentsInChildren<Terrain>(false))
			{
				TerrainData d = t.terrainData;
				if (d == null) continue;
				Vector3 p = at - t.transform.position;
				float u = p.x / d.size.x, v = p.z / d.size.z;
				if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
				if (Mathf.Abs(t.SampleHeight(at) + t.transform.position.y - at.y) > 1f) return false; // (standing on something on the ground)
				return d.GetSteepness(u, v) > FlatSlope;
			}
			return false;
		}

		/// <summary>The point of the island's terrain between <paramref name="low"/> and <paramref name="high"/> (world heights),
		/// with dry ground no steeper than <see cref="FlatSlope"/> all round it (<see cref="FlatRound"/> m), nearest
		/// <paramref name="centre"/> (flat distance); null when there is none.</summary>
		internal static Vector3? FlatLand(Transform root, Vector3 centre, float low, float high)
		{
			Vector3? best = null;
			float bestD = float.MaxValue, maxRise = Mathf.Tan(FlatSlope * Mathf.Deg2Rad);
			foreach (Terrain t in root.GetComponentsInChildren<Terrain>(false))
			{
				TerrainData d = t.terrainData;
				if (d == null) continue;
				int res = d.heightmapResolution;
				float[,] h = d.GetHeights(0, 0, res, res);
				Vector3 size = d.size, at = t.transform.position;
				float sx = size.x / (res - 1), sz = size.z / (res - 1), lowN = (low - at.y) / size.y, highN = (high - at.y) / size.y;
				int k = Mathf.Clamp(Mathf.CeilToInt(FlatRound / Mathf.Min(sx, sz)), 1, 6);
				for (int z = k; z < res - k; z++)
					for (int x = k; x < res - k; x++)
					{
						float y = h[z, x];
						if (y < lowN || y > highN) continue;
						// (every point round it: dry - not at the wet edge - and no higher or lower than the slope allows)
						bool flat = true;
						for (int dz = -k; dz <= k && flat; dz++)
							for (int dx = -k; dx <= k && flat; dx++)
							{
								if (dx == 0 && dz == 0) continue;
								float n = h[z + dz, x + dx], dist = Mathf.Sqrt(dx * sx * dx * sx + dz * sz * dz * sz);
								if (n < lowN || Mathf.Abs(n - y) * size.y > maxRise * dist) flat = false;
							}
						if (!flat) continue;
						float wx = at.x + x * sx, wz = at.z + z * sz;
						float dd = (wx - centre.x) * (wx - centre.x) + (wz - centre.z) * (wz - centre.z);
						if (dd < bestD) { bestD = dd; best = new Vector3(wx, at.y + y * size.y, wz); }
					}
			}
			return best;
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
