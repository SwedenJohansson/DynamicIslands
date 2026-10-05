using System;
using System.Collections.Generic;
using System.Linq;
using CommandUndoRedo;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's scatter (ROADMAP LM11, the user 2026-10-05): many copies of one object - a thing to gather, a bush, a
	/// rock - spread at random round a point, on land or under water, never within a few metres of anything already on the
	/// island (its buildings, its quest's objects), each set down on the ground as the placer does. One undo takes them all
	/// back. The recipes' "scatter ... free=" does the same for the library's islands.
	/// </summary>
	public static class ScatterTool
	{
		/// <summary>Raft's finds under water (sand, clay, stone and ores lie on the sea floor too; these only there).</summary>
		public static bool OnlyUnderWater(string name)
		{
			return name.EndsWith("_OceanBottom") || name.Contains("GiantClam") || name.Contains("SilverAlgae") || name.StartsWith("SeaVine") || name.StartsWith("Seavine");
		}

		/// <summary>Places up to count copies of name within radius of centre: under water (underWater) or on land, at least
		/// free metres from any object already placed. Returns the placed objects (fewer when the area has no room).</summary>
		public static List<GameObject> Scatter(string name, Vector3 centre, int count, float radius, float free, bool underWater, int seed)
		{
			var made = new List<GameObject>();
			if (!DynamicIslands.InEditor() || string.IsNullOrEmpty(name) || count <= 0) return made;
			if (!ObjectLimit.Allow(count)) return made;
			GameObject rootObject = GameObject.Find("PlacedObjects");
			if (rootObject == null) return made;
			float sea = DynamicIslands.EditorWaterLevel;
			var rnd = new System.Random(seed);
			// (also by their pivots: plants and small things without a collider count too)
			List<Vector2> there = rootObject.GetComponentsInChildren<EditorGameObject>(false).Select(e => new Vector2(e.transform.position.x, e.transform.position.z)).ToList();
			for (int k = 0; k < count * 80 && made.Count < count; k++)
			{
				double ang = rnd.NextDouble() * Math.PI * 2, dist = Math.Sqrt(rnd.NextDouble()) * radius;
				var at = new Vector3(centre.x + (float)(Math.Cos(ang) * dist), 0f, centre.z + (float)(Math.Sin(ang) * dist));
				Vector3 ground, normal;
				if (!PlacementOptions.GroundAt(new Vector3(at.x, sea + 200f, at.z), out ground, out normal)) continue;
				if (underWater ? ground.y > sea - 0.6f : ground.y < sea + 0.3f) continue;
				if (underWater && ground.y < sea - 40f) continue;
				Physics.SyncTransforms();
				if (free > 0f && Physics.OverlapSphere(ground + Vector3.up, free, ~0, QueryTriggerInteraction.Ignore).Any(c => c.GetComponentInParent<EditorGameObject>() != null)) continue;
				var flat = new Vector2(ground.x, ground.z);
				if (free > 0f && there.Any(p => (p - flat).sqrMagnitude < free * free)) continue;
				GameObject go = PlaceableCatalog.Spawn(name, rootObject.transform);
				if (go == null) break;
				go.transform.rotation = Quaternion.Euler(0f, (float)(rnd.NextDouble() * 360.0), 0f) * PlacementOptions.Straight(go.transform.rotation, true);
				go.transform.position = ground;
				float y = Mathf.Min(ground.y, PlacementOptions.LowestGroundUnder(go));
				go.transform.position = new Vector3(ground.x, y + PlacementOptions.PivotAboveBottom(go), ground.z);
				Collider own = go.GetComponent<Collider>();
				if (own != null) own.enabled = true;
				EditorGameObject.Attach(go, name);
				made.Add(go);
				there.Add(new Vector2(ground.x, ground.z));
			}
			if (made.Count > 0) UndoRedoManager.Insert(new ObjectVisibilityCommand(made, true));
			return made;
		}

		/// <summary>The point the editor's camera looks at (on the ground or the sea floor), or null.</summary>
		public static Vector3? ViewCentre()
		{
			Camera cam = Camera.main;
			if (cam == null) return null;
			RaycastHit hit;
			Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
			if (Physics.Raycast(ray, out hit, 2000f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
			// (looking at open water: where the ray meets the sea's level)
			float sea = DynamicIslands.EditorWaterLevel;
			if (Mathf.Abs(ray.direction.y) > 0.01f)
			{
				float t = (sea - ray.origin.y) / ray.direction.y;
				if (t > 0f) return ray.origin + ray.direction * t;
			}
			return null;
		}
	}
}
