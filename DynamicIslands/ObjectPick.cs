using System.Collections.Generic;
using System.Linq;
using RuntimeGizmos;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Which placed object a click selects (ROADMAP E11), and the outline on the one under the mouse that shows it.
	/// A flower beside a big palm could not be picked: the palm's collider and the bounds of its leaves cover the flower,
	/// and the first thing the ray met won. Now every object the ray meets in front of the ground is a candidate: by its
	/// real mesh where the mesh can be read (a ray through the gaps between the leaves misses the palm), by a plain mesh
	/// collider, else by its renderers' bounds or its collider. Whatever lies behind the first real surface is out (it is
	/// hidden there). Of the rest the smallest object comes first; a second click on the same spot selects the next one.
	/// </summary>
	public static class ObjectPick
	{
		/// <summary>An object under the ray: how far (metres) and how big (its shape's diagonal); Exact = its real surface was hit.</summary>
		public sealed class Candidate
		{
			public Transform Root;
			public float Distance, Size;
			public bool Exact;
			/// <summary>Its real surface or one of its own colliders was hit (not only the box round its meshes).</summary>
			public bool Solid;
		}

		/// <summary>An object bigger than this (diagonal, metres) counts a click by its meshes' bounds only through the middle
		/// of them (Loose), and an object hit only that way comes after every object whose surface or collider was hit: a big
		/// palm's crown spans 10 m and otherwise took every click near it (ROADMAP PF-D4, 2026-10-26).</summary>
		public const float BigObject = 6f;
		/// <summary>The part of a big object's bounds (across, x and z) a bounds-only click must go through.</summary>
		public const float Loose = 0.6f;

		/// <summary>Past the first real surface by more than this: hidden behind it.</summary>
		public const float BehindMargin = 0.3f;
		/// <summary>A click this close (pixels) to the last one on the same objects selects the next of them.</summary>
		public const float SameSpot = 6f;
		/// <summary>Meshes with more triangles than this are tested by their bounds only.</summary>
		const int MaxExactTriangles = 60000;

		/// <summary>The objects under a ray, the one a click selects first.</summary>
		public static List<Candidate> Candidates(Ray ray, LayerMask mask)
		{
			var found = new Dictionary<Transform, Candidate>();
			float terrainDistance = float.MaxValue;
			foreach (RaycastHit hit in Physics.RaycastAll(ray, 5000f, mask).OrderBy(h => h.distance))
			{
				if (hit.collider.GetComponent<Terrain>() != null) { terrainDistance = hit.distance; break; }
				EditorGameObject owner = hit.collider.GetComponentInParent<EditorGameObject>();
				if (!SelectionTools.Pickable(owner)) continue;
				MeshCollider mc = hit.collider as MeshCollider;
				Add(found, owner.transform, hit.distance, mc != null && !mc.convex, true);
			}

			GameObject placedRoot = GameObject.Find("PlacedObjects");
			if (placedRoot != null)
				foreach (EditorGameObject o in placedRoot.GetComponentsInChildren<EditorGameObject>(false))
				{
					if (!SelectionTools.Pickable(o)) continue; // (hidden or locked - ROADMAP E3)
					float size = -1f;
					foreach (Renderer r in o.GetComponentsInChildren<Renderer>())
					{
						float d;
						if (r == null || !r.enabled || r.name == ContentCatalog.MarkerOnly || r is ParticleSystemRenderer || !r.bounds.IntersectRay(ray, out d) || d >= terrainDistance) continue;
						bool exact;
						float surface;
						if (MeshHit(r, ray, out exact, out surface))
						{
							if (exact) { if (surface < terrainDistance) Add(found, o.transform, surface, true, true); }
							else
							{
								if (size < 0f) size = SizeOf(o.transform);
								if (size > BigObject && !TightBounds(r.bounds).IntersectRay(ray)) continue;
								Add(found, o.transform, d, false, false);
							}
						}
					}
				}

			List<Candidate> list = found.Values.ToList();
			if (list.Count == 0) return list;
			float firstSurface = list.Where(c => c.Exact).Select(c => c.Distance).DefaultIfEmpty(float.MaxValue).Min();
			if (firstSurface < float.MaxValue) list.RemoveAll(c => c.Distance > firstSurface + BehindMargin);
			foreach (Candidate c in list) c.Size = SizeOf(c.Root);
			return list.OrderBy(c => !c.Solid && c.Size > BigObject).ThenBy(c => c.Size).ThenBy(c => c.Distance).ToList();
		}

		/// <summary>Bounds narrowed to their middle Loose part across (x and z); the full height kept.</summary>
		public static Bounds TightBounds(Bounds b)
		{
			return new Bounds(b.center, new Vector3(b.size.x * Loose, b.size.y, b.size.z * Loose));
		}

		static void Add(Dictionary<Transform, Candidate> found, Transform root, float distance, bool exact, bool solid)
		{
			Candidate c;
			if (!found.TryGetValue(root, out c)) { found[root] = new Candidate { Root = root, Distance = distance, Exact = exact, Solid = solid }; return; }
			if (solid) c.Solid = true;
			// (a real surface counts over a bounds box; of two of a kind, the nearer)
			if (exact && !c.Exact) { c.Exact = true; c.Distance = distance; }
			else if (exact == c.Exact && distance < c.Distance) c.Distance = distance;
		}

		/// <summary>How big an object looks: the diagonal of its meshes' bounds (else its colliders'; 1000 with neither).</summary>
		public static float SizeOf(Transform root)
		{
			Renderer[] rs = PlacementOptions.ShapeRenderers(root.gameObject);
			Bounds b = new Bounds();
			bool any = false;
			foreach (Renderer r in rs) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
			if (!any)
				foreach (Collider c in root.GetComponentsInChildren<Collider>())
				{
					if (!c.enabled) continue;
					if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
				}
			return any ? b.size.magnitude : 1000f;
		}

		#region Ray against a mesh

		sealed class MeshData { public Vector3[] Vertices; public int[] Triangles; }
		static readonly Dictionary<Mesh, MeshData> meshes = new Dictionary<Mesh, MeshData>();

		/// <summary>
		/// The ray against a renderer's own mesh. False: the mesh can be read and the ray misses it (it only went through the
		/// bounds). True with exact: the surface's distance. True without exact: the mesh can't be read (or is skinned, or too
		/// big) - the bounds are all there is.
		/// </summary>
		static bool MeshHit(Renderer r, Ray ray, out bool exact, out float distance)
		{
			exact = false; distance = 0f;
			if (!(r is MeshRenderer)) return true;
			MeshFilter mf = r.GetComponent<MeshFilter>();
			Mesh mesh = mf != null ? mf.sharedMesh : null;
			MeshData data = Data(mesh);
			if (data == null) return true;

			Matrix4x4 toLocal = r.transform.worldToLocalMatrix, toWorld = r.transform.localToWorldMatrix;
			Vector3 o = toLocal.MultiplyPoint3x4(ray.origin), dir = toLocal.MultiplyVector(ray.direction);
			float best = float.MaxValue;
			Vector3[] v = data.Vertices;
			int[] t = data.Triangles;
			for (int i = 0; i + 2 < t.Length; i += 3)
			{
				float k;
				if (RayTriangle(o, dir, v[t[i]], v[t[i + 1]], v[t[i + 2]], out k) && k < best) best = k;
			}
			if (best == float.MaxValue) return false;
			exact = true;
			distance = Vector3.Distance(ray.origin, toWorld.MultiplyPoint3x4(o + dir * best));
			return true;
		}

		static MeshData Data(Mesh mesh)
		{
			if (mesh == null) return null;
			MeshData data;
			if (meshes.TryGetValue(mesh, out data)) return data;
			if (meshes.Count > 2000) meshes.Clear();
			data = null;
			try
			{
				if (mesh.isReadable)
				{
					int[] tris = mesh.triangles;
					if (tris.Length / 3 <= MaxExactTriangles) data = new MeshData { Vertices = mesh.vertices, Triangles = tris };
				}
			}
			catch { data = null; }
			meshes[mesh] = data;
			return data;
		}

		/// <summary>Möller-Trumbore, both faces: k = how far along dir (in dir's own length).</summary>
		internal static bool RayTriangle(Vector3 o, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float k)
		{
			k = 0f;
			Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(dir, e2);
			float det = Vector3.Dot(e1, p);
			if (Mathf.Abs(det) < 1e-9f) return false;
			float inv = 1f / det;
			Vector3 s = o - a;
			float u = Vector3.Dot(s, p) * inv;
			if (u < 0f || u > 1f) return false;
			Vector3 q = Vector3.Cross(s, e1);
			float w = Vector3.Dot(dir, q) * inv;
			if (w < 0f || u + w > 1f) return false;
			k = Vector3.Dot(e2, q) * inv;
			return k > 0f;
		}

		#endregion

		#region Click and cycle

		static Vector2 lastClick = new Vector2(-1000f, -1000f);
		static List<Transform> lastList;
		static int lastIndex;

		/// <summary>The first object under the ray (no cycling).</summary>
		public static Transform First(Ray ray, LayerMask mask)
		{
			List<Candidate> list = Candidates(ray, mask);
			return list.Count > 0 ? list[0].Root : null;
		}

		/// <summary>What a click at this screen point selects: the first object, or - clicked again on the same spot while the
		/// one it gave is selected - the next of the objects there.</summary>
		public static Transform WouldPick(Ray ray, LayerMask mask, Vector2 screen, out int index, out int count)
		{
			List<Transform> list = Candidates(ray, mask).Select(c => c.Root).ToList();
			count = list.Count;
			index = NextIndex(list, screen);
			return count > 0 ? list[index] : null;
		}

		static int NextIndex(List<Transform> list, Vector2 screen)
		{
			if (list.Count < 2 || lastList == null || (screen - lastClick).magnitude > SameSpot || lastIndex >= lastList.Count) return 0;
			if (list.Count != lastList.Count || list.Any(t => !lastList.Contains(t))) return 0;
			TransformGizmo g = DynamicIslands.EditorGizmoHandler;
			Transform shown = lastList[lastIndex];
			if (g == null || shown == null || g.SelectedRoots.Count != 1 || g.SelectedRoots[0] != shown) return 0;
			int at = list.IndexOf(shown);
			return at < 0 ? 0 : (at + 1) % list.Count;
		}

		/// <summary>A click (TransformGizmo.GetTarget): picks, and remembers it so the same spot clicked again gives the next one.
		/// cycle false (Shift or Ctrl held): always the first.</summary>
		public static Transform Click(Ray ray, LayerMask mask, Vector2 screen, bool cycle)
		{
			List<Transform> list = Candidates(ray, mask).Select(c => c.Root).ToList();
			if (list.Count == 0) { lastList = null; return null; }
			int index = cycle ? NextIndex(list, screen) : 0;
			lastList = list; lastIndex = index; lastClick = screen;
			if (list.Count > 1 && cycle)
			{
				EditorGameObject e = list[index].GetComponent<EditorGameObject>();
				EditorUI.Flash((e != null ? PlaceableCatalog.DisplayName(e.GameObjectName) : list[index].name) + " (" + (index + 1) + " of " + list.Count + " here: click again for the next)", 3f);
			}
			return list[index];
		}

		/// <summary>Forgets the last click (tests).</summary>
		internal static void ForgetClick() { lastList = null; lastClick = new Vector2(-1000f, -1000f); }

		#endregion

		#region The outline under the mouse

		static Material hoverMaterial;
		static Transform hovered;
		static readonly List<Renderer> hoverRenderers = new List<Renderer>();
		static Vector2 lastMouse = new Vector2(-1000f, -1000f);
		static float nextLook, nextMoveLook;

		/// <summary>The object outlined under the mouse (what a click would select), or null.</summary>
		public static Transform Hovered { get { return hovered; } }
		/// <summary>The outline's colour: light blue, the selection's own outline stays as it is.</summary>
		public static readonly Color HoverColor = new Color(0.35f, 0.85f, 1f, 1f);

		/// <summary>Every frame in the Objects tab: outlines the object a click would select (none while placing, dragging,
		/// over a panel or with a window open).</summary>
		public static void Tick()
		{
			TransformGizmo g = DynamicIslands.EditorGizmoHandler;
			Camera cam = Camera.main;
			bool can = g != null && cam != null && !g.placingObject && g.translatingAxis == RuntimeGizmos.Axis.None && !SelectionTools.Boxing
				&& !EditorCamera.UsingMouse && !EditorCamera.Looking && !EditorInput.WindowOpen && !EditorInput.IsTyping
				&& !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2)
				&& (UnityEngine.EventSystems.EventSystem.current == null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
			if (!can) { ClearHover(); lastMouse = new Vector2(-1000f, -1000f); nextLook = nextMoveLook = 0f; return; }
			Vector2 mouse = Input.mousePosition;
			bool moved = (mouse - lastMouse).sqrMagnitude > 4f;
			float now = Time.unscaledTime;
			// (at most ten looks a second while the mouse moves, two while it rests: objects move, undo brings some back)
			if (moved ? now < nextMoveLook : now < nextLook) return;
			lastMouse = mouse;
			nextMoveLook = now + 0.1f; nextLook = now + 0.5f;
			int index, count;
			Transform t = WouldPick(cam.ScreenPointToRay(mouse), g.selectionMask, mouse, out index, out count);
			if (t != null && g.SelectedRoots.Contains(t)) t = null; // (it has the selection's outline)
			SetHover(t);
		}

		/// <summary>Outlines one object (null: none), taking the outline off the one before.</summary>
		public static void SetHover(Transform t)
		{
			if (t == hovered && (t == null || hoverRenderers.Count > 0)) return;
			ClearHover();
			if (t == null) return;
			if (hoverMaterial == null)
			{
				if (DynamicIslands._shaders.Count < 2 || DynamicIslands._shaders[1] == null) return;
				hoverMaterial = new Material(DynamicIslands._shaders[1]) { name = "CustomIslands_HoverOutline" };
				if (hoverMaterial.HasProperty("_OutlineColor")) hoverMaterial.SetColor("_OutlineColor", HoverColor);
				if (hoverMaterial.HasProperty("_Color")) hoverMaterial.SetColor("_Color", HoverColor);
			}
			hovered = t;
			foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
			{
				if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.name == ContentCatalog.MarkerOnly) continue;
				List<Material> mats = r.sharedMaterials.ToList();
				if (mats.Contains(hoverMaterial)) continue;
				mats.Add(hoverMaterial);
				r.sharedMaterials = mats.ToArray();
				hoverRenderers.Add(r);
			}
		}

		/// <summary>Takes the outline off (only the hover's own material: the selection's outline stays).</summary>
		public static void ClearHover()
		{
			foreach (Renderer r in hoverRenderers)
			{
				if (r == null) continue;
				Material[] mats = r.sharedMaterials;
				if (mats.Contains(hoverMaterial)) r.sharedMaterials = mats.Where(m => m != hoverMaterial).ToArray();
			}
			hoverRenderers.Clear();
			hovered = null;
		}

		/// <summary>The outline is on this renderer (tests).</summary>
		internal static bool Outlined(Renderer r) { return r != null && hoverMaterial != null && r.sharedMaterials.Contains(hoverMaterial); }

		#endregion
	}
}
