using System.Collections.Generic;
using System.Linq;
using CommandUndoRedo;
using RuntimeGizmos;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>Editor options for placing objects (Objects tab buttons "Random" and "Slope").</summary>
	public static class PlacementOptions
	{
		/// <summary>Each placed object gets a random turn and a size between 80 and 125 %.</summary>
		public static bool RandomTurnAndSize;
		/// <summary>Placed and grounded objects lean with the slope of the ground instead of standing straight up.</summary>
		public static bool AlignToSlope;
		/// <summary>Positions snap to Raft's 1.5 m building grid and Q/E turn in 90° steps (for Raft blocks).</summary>
		public static bool SnapToGrid;
		public const float GridSize = 1.5f;
		/// <summary>A Raft foundation's top sits this far above the water when floating, like on the player's raft.</summary>
		public const float FloatDepth = 0.35f;

		/// <summary>Grid-snapped position (x/z only) when "Grid" is on.</summary>
		public static Vector3 Snap(Vector3 p)
		{
			if (!SnapToGrid) return p;
			return new Vector3(Mathf.Round(p.x / GridSize) * GridSize, p.y, Mathf.Round(p.z / GridSize) * GridSize);
		}

		/// <summary>Raft's building blocks float: over water they sit at the sea surface instead of on the seabed.</summary>
		public static Vector3 FloatIfBlock(string objectName, Vector3 p)
		{
			if (PlaceableCatalog.CategoryOf(objectName) != PlaceableCatalog.RaftBlocksCategory) return p;
			float sea = (terraineditor.terrain != null ? terraineditor.terrain.transform.position.y : 0f) + IslandFile.DefaultWaterLevel;
			if (p.y < sea - FloatDepth) p.y = sea - FloatDepth;
			return p;
		}

		/// <summary>The editor terrain's surface below a point (ignores objects). False if there is no terrain there.</summary>
		public static bool GroundAt(Vector3 position, out Vector3 point, out Vector3 normal)
		{
			point = position; normal = Vector3.up;
			Terrain terrain = terraineditor.terrain;
			TerrainCollider collider = terrain != null ? terrain.GetComponent<TerrainCollider>() : null;
			RaycastHit hit;
			if (collider == null || !collider.Raycast(new Ray(new Vector3(position.x, 5000f, position.z), Vector3.down), out hit, 10000f)) return false;
			point = hit.point; normal = hit.normal;
			return true;
		}

		/// <summary>Rotation for an object standing on ground with this normal: its turn around the vertical, tilted with the slope if wanted.</summary>
		public static Quaternion Upright(float yaw, Quaternion baseRotation, Vector3 normal)
		{
			Quaternion turn = Quaternion.Euler(0f, yaw, 0f) * baseRotation;
			return AlignToSlope ? Quaternion.FromToRotation(Vector3.up, normal) * turn : turn;
		}

		/// <summary>Puts the selected objects on the terrain surface (and tilts them with the slope if "Slope" is on), as one undo step.</summary>
		public static int DropSelectionToGround()
		{
			TransformGizmo gizmo = DynamicIslands.EditorGizmoHandler;
			if (gizmo == null) return 0;
			var group = new CommandGroup();
			int n = 0;
			foreach (Transform t in gizmo.SelectedRoots.Where(t => t != null).ToList())
			{
				Vector3 point, normal;
				if (!GroundAt(t.position, out point, out normal)) continue;
				var command = new TransformCommand(gizmo, t);
				t.position = point;
				if (AlignToSlope)
				{
					// Keep the object's turn around its own up axis, lean it with the ground
					Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up);
					if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
					t.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.LookRotation(forward.normalized, Vector3.up);
				}
				command.StoreNewTransformValues();
				group.Add(command);
				n++;
			}
			if (n > 0)
			{
				UndoRedoManager.Insert(group);
				gizmo.SetPivotPoint();
			}
			return n;
		}

		/// <summary>
		/// The placed object (its EditorGameObject root) under a ray, or null for the terrain, the sea or nothing.
		/// Objects are found by their colliders, else (decorations without colliders, colliders on other layers) by their meshes' bounds.
		/// </summary>
		public static Transform PickObject(Ray ray, LayerMask mask)
		{
			float terrainDistance = float.MaxValue;
			foreach (RaycastHit hit in Physics.RaycastAll(ray, 5000f, mask).OrderBy(h => h.distance))
			{
				if (hit.collider.GetComponent<Terrain>() != null) { terrainDistance = hit.distance; break; }
				EditorGameObject owner = hit.collider.GetComponentInParent<EditorGameObject>();
				if (owner != null && owner.gameObject.activeInHierarchy) return owner.transform;
			}
			GameObject placedRoot = GameObject.Find("PlacedObjects");
			if (placedRoot == null) return null;
			Transform best = null;
			float bestDistance = terrainDistance;
			foreach (EditorGameObject o in placedRoot.GetComponentsInChildren<EditorGameObject>(false))
			{
				foreach (Renderer r in o.GetComponentsInChildren<Renderer>())
				{
					float d;
					if (r.enabled && r.name != ContentCatalog.MarkerOnly && r.bounds.IntersectRay(ray, out d) && d < bestDistance) { bestDistance = d; best = o.transform; }
				}
			}
			return best;
		}

		/// <summary>
		/// Copies the selected objects (next to the originals: one grid step with Grid on, else 2 m) as one undo step,
		/// and selects the copies so they can be moved straight away.
		/// </summary>
		public static int DuplicateSelection()
		{
			TransformGizmo gizmo = DynamicIslands.EditorGizmoHandler;
			GameObject placedRoot = GameObject.Find("PlacedObjects");
			if (gizmo == null || placedRoot == null) return 0;
			Vector3 offset = SnapToGrid ? new Vector3(GridSize, 0, 0) : new Vector3(2f, 0, 0);
			var copies = new List<GameObject>();
			foreach (Transform t in gizmo.SelectedRoots.Where(t => t != null).ToList())
			{
				EditorGameObject info = t.GetComponent<EditorGameObject>();
				if (info == null) continue;
				GameObject copy = PlaceableCatalog.Spawn(info.GameObjectName, placedRoot.transform);
				if (copy == null) continue;
				copy.transform.position = t.position + offset;
				copy.transform.rotation = t.rotation;
				copy.transform.localScale = t.lossyScale;
				foreach (Collider c in copy.GetComponentsInChildren<Collider>()) c.enabled = true;
				EditorGameObject.Attach(copy, info.GameObjectName, info.Props); // a copy keeps the settings (creature stats, note text, tint)
				copies.Add(copy);
			}
			if (copies.Count == 0) return 0;
			UndoRedoManager.Insert(new ObjectVisibilityCommand(copies, true));
			gizmo.ClearTargets(false);
			foreach (GameObject c in copies) gizmo.AddTarget(c.transform, false);
			return copies.Count;
		}
	}

}
