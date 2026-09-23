using DynamicIslands.Editor;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Editor terrain: creates the 1000 x 600 x 1000 terrain and sculpts it with a round, soft-edged brush
	/// while the Terrain tab is selected. Textures are repainted automatically when a stroke ends.
	/// </summary>
	public class terraineditor : MonoBehaviour
	{
		public static Terrain terrain;
		public static TerrainData terrainData;

		public Vector3 terrainSize = new Vector3(1000, 600, 1000);
		public int heightmapResolution = 513;

		public Text CamPos;

		public enum TerrainModificationAction
		{
			Raise,
			Lower,
			Flatten,
			Sample,
			SampleAverage,
			Smooth,
			PaintLayer, // paint terraineditor.paintLayer by hand
			AutoPaint,  // brush back to automatic texturing
		}

		/// <summary>Texture layer used by PaintLayer (TerrainPainter.Seabed/Sand/Grass/Rock).</summary>
		public static int paintLayer = TerrainPainter.Sand;

		/// <summary>Alphamap-sized mask, 1 where the texture was painted by hand (auto texturing leaves those pixels alone).</summary>
		public static float[,] paintMask;

		public static TerrainModificationAction modificationAction = TerrainModificationAction.Raise;

		/// <summary>Brush radius in metres.</summary>
		public static float brushRadius = 15f;
		/// <summary>How fast Raise/Lower change the ground at the brush centre, in metres per second.
		/// For Flatten/Smooth it scales how quickly the ground converges.</summary>
		public static float strength = 4f;

		public const float MinRadius = 2f, MaxRadius = 80f;
		public const float MinStrength = 0.5f, MaxStrength = 20f;

		public static bool allowEditing = true;

		float flattenTarget; // normalised height sampled when a Flatten stroke starts
		bool stroking;
		Vector3 dirtyMin, dirtyMax; // world-space area touched by the current stroke, repainted on release

		void Start()
		{
			CamPos = GameObject.Find("CamPos").GetComponent<Text>();

			terrainData = new TerrainData();
			// Resolution first: changing it afterwards rescales the size
			terrainData.heightmapResolution = heightmapResolution;
			terrainData.size = terrainSize;

			terrain = Terrain.CreateTerrainGameObject(terrainData).GetComponent<Terrain>();
			terrain.transform.position = Vector3.zero;
			terrain.gameObject.layer = IslandSpawner.TerrainLayer;
			// CreateTerrainGameObject already adds a TerrainCollider bound to the same data

			paintMask = null;
			TerrainPainter.Setup(terrain, IslandFile.DefaultWaterLevel);
			paintMask = new float[terrainData.alphamapResolution, terrainData.alphamapResolution];
		}

		void Update()
		{
			try
			{
				Vector3 cam = Camera.main.transform.position;
				CamPos.text = "X" + cam.x.ToString("F0") + " Y" + cam.y.ToString("F0") + " Z" + cam.z.ToString("F0");

				ModifyTerrain();
			}
			catch (Exception e)
			{
				Debug.LogWarning("[CUSTOM ISLANDS] Terrain editor: " + e.Message);
			}
		}

		bool CanSculpt()
		{
			if (!allowEditing || terrain == null) return false;
			if (TabSelector.instance != null && TabSelector.instance.SelectedTab != TAB.TerrainEdit) return false;
			if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
			if (FindObjectOfType<ObjectPlacer>() != null) return false;
			if (DynamicIslands.EditorGizmoHandler != null && DynamicIslands.EditorGizmoHandler.isTransforming) return false;
			return true;
		}

		void ModifyTerrain()
		{
			if (stroking && !Input.GetMouseButton(0)) EndStroke();
			if (!Input.GetMouseButton(0) || !CanSculpt()) return;

			RaycastHit hit;
			if (!Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 5000f) || hit.collider.GetComponent<Terrain>() != terrain)
				return;

			if (!stroking)
			{
				stroking = true;
				dirtyMin = hit.point; dirtyMax = hit.point;
				flattenTarget = SampleNormalizedHeight(hit.point);
			}

			switch (modificationAction)
			{
				case TerrainModificationAction.Raise: ApplyBrush(hit.point, +1f); break;
				case TerrainModificationAction.Lower: ApplyBrush(hit.point, -1f); break;
				case TerrainModificationAction.Flatten: ApplyFlatten(hit.point); break;
				case TerrainModificationAction.Smooth: ApplySmooth(hit.point); break;
				case TerrainModificationAction.PaintLayer: ApplyPaint(hit.point, false); break;
				case TerrainModificationAction.AutoPaint: ApplyPaint(hit.point, true); break;
				case TerrainModificationAction.Sample:
				case TerrainModificationAction.SampleAverage:
					flattenTarget = SampleNormalizedHeight(hit.point);
					modificationAction = TerrainModificationAction.Flatten;
					break;
			}

			Vector3 r = new Vector3(brushRadius, 0, brushRadius);
			dirtyMin = Vector3.Min(dirtyMin, hit.point - r);
			dirtyMax = Vector3.Max(dirtyMax, hit.point + r);
		}

		void EndStroke()
		{
			stroking = false;
			// Sculpting changes heights/slopes, so refresh the automatic texturing (hand-painted pixels are kept)
			if (modificationAction != TerrainModificationAction.PaintLayer && modificationAction != TerrainModificationAction.AutoPaint)
				TerrainPainter.PaintWorldArea(terrain, IslandFile.DefaultWaterLevel, dirtyMin, dirtyMax, paintMask);
		}

		/// <summary>
		/// Texture brush in alphamap space. Hand painting blends towards the chosen layer and marks the pixels
		/// in the paint mask; AutoPaint blends back to the automatic weights and clears the mask.
		/// </summary>
		void ApplyPaint(Vector3 world, bool auto)
		{
			int res = terrainData.alphamapResolution;
			if (paintMask == null || paintMask.GetLength(0) != res) paintMask = new float[res, res];
			float pixel = terrainData.size.x / res;
			Vector3 local = world - terrain.transform.position;
			float cx = local.x / pixel - 0.5f, cz = local.z / pixel - 0.5f, rs = Mathf.Max(1f, brushRadius / pixel);

			int x0 = Mathf.Clamp(Mathf.FloorToInt(cx - rs), 0, res - 1), z0 = Mathf.Clamp(Mathf.FloorToInt(cz - rs), 0, res - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt(cx + rs), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt(cz + rs), 0, res - 1);
			if (x1 < x0 || z1 < z0) return;
			int cols = x1 - x0 + 1, rows = z1 - z0 + 1;

			float[,,] maps = terrainData.GetAlphamaps(x0, z0, cols, rows);
			int layers = maps.GetLength(2);
			float rate = Mathf.Clamp01(strength * 0.25f * Time.deltaTime);
			var target = new float[layers];

			for (int z = 0; z < rows; z++)
				for (int x = 0; x < cols; x++)
				{
					float dx = (x0 + x - cx) / rs, dz = (z0 + z - cz) / rs, d2 = dx * dx + dz * dz;
					if (d2 >= 1f) continue;
					float w = (1f - d2) * (1f - d2);

					if (auto) TerrainPainter.AutoWeights(terrain, IslandFile.DefaultWaterLevel, x0 + x, z0 + z, target);
					else for (int l = 0; l < layers; l++) target[l] = l == paintLayer ? 1f : 0f;

					float t = Mathf.Clamp01(rate * w * 4f);
					for (int l = 0; l < layers; l++) maps[z, x, l] = Mathf.Lerp(maps[z, x, l], target[l], t);

					if (auto) { if (w > 0.3f) paintMask[z0 + z, x0 + x] = 0f; }
					else if (w > 0.05f) paintMask[z0 + z, x0 + x] = 1f;
				}
			terrainData.SetAlphamaps(x0, z0, maps);
		}

		float SampleNormalizedHeight(Vector3 world)
		{
			return (terrain.SampleHeight(world)) / terrainData.size.y;
		}

		/// <summary>Heightmap block under the brush plus per-sample weights (1 at the centre, smoothly 0 at the rim).</summary>
		bool GetBrushArea(Vector3 world, out int x0, out int z0, out float[,] weights)
		{
			int res = terrainData.heightmapResolution;
			float spacing = terrainData.size.x / (res - 1);
			Vector3 local = world - terrain.transform.position;
			float cx = local.x / spacing, cz = local.z / spacing, rs = Mathf.Max(1f, brushRadius / spacing);

			x0 = Mathf.Clamp(Mathf.FloorToInt(cx - rs), 0, res - 1);
			z0 = Mathf.Clamp(Mathf.FloorToInt(cz - rs), 0, res - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt(cx + rs), 0, res - 1);
			int z1 = Mathf.Clamp(Mathf.CeilToInt(cz + rs), 0, res - 1);
			weights = new float[z1 - z0 + 1, x1 - x0 + 1];
			if (x1 <= x0 || z1 <= z0) return false;

			for (int z = 0; z <= z1 - z0; z++)
				for (int x = 0; x <= x1 - x0; x++)
				{
					float dx = (x0 + x - cx) / rs, dz = (z0 + z - cz) / rs;
					float d2 = dx * dx + dz * dz;
					if (d2 < 1f) { float f = 1f - d2; weights[z, x] = f * f; } // smooth falloff
				}
			return true;
		}

		void ApplyBrush(Vector3 world, float direction)
		{
			int x0, z0; float[,] w;
			if (!GetBrushArea(world, out x0, out z0, out w)) return;
			float[,] h = terrainData.GetHeights(x0, z0, w.GetLength(1), w.GetLength(0));
			float delta = direction * strength * Time.deltaTime / terrainData.size.y;
			for (int z = 0; z < w.GetLength(0); z++)
				for (int x = 0; x < w.GetLength(1); x++)
					h[z, x] = Mathf.Clamp01(h[z, x] + delta * w[z, x]);
			terrainData.SetHeights(x0, z0, h);
		}

		void ApplyFlatten(Vector3 world)
		{
			int x0, z0; float[,] w;
			if (!GetBrushArea(world, out x0, out z0, out w)) return;
			float[,] h = terrainData.GetHeights(x0, z0, w.GetLength(1), w.GetLength(0));
			float rate = Mathf.Clamp01(strength * 0.5f * Time.deltaTime);
			for (int z = 0; z < w.GetLength(0); z++)
				for (int x = 0; x < w.GetLength(1); x++)
					h[z, x] = Mathf.Lerp(h[z, x], flattenTarget, rate * w[z, x]);
			terrainData.SetHeights(x0, z0, h);
		}

		void ApplySmooth(Vector3 world)
		{
			int x0, z0; float[,] w;
			if (!GetBrushArea(world, out x0, out z0, out w)) return;
			int rows = w.GetLength(0), cols = w.GetLength(1);
			float[,] h = terrainData.GetHeights(x0, z0, cols, rows);
			float[,] result = (float[,])h.Clone();
			float rate = Mathf.Clamp01(strength * 0.5f * Time.deltaTime);
			for (int z = 1; z < rows - 1; z++)
				for (int x = 1; x < cols - 1; x++)
				{
					float avg = (h[z - 1, x] + h[z + 1, x] + h[z, x - 1] + h[z, x + 1] + h[z, x]) / 5f;
					result[z, x] = Mathf.Lerp(h[z, x], avg, rate * w[z, x] * 4f);
				}
			terrainData.SetHeights(x0, z0, result);
		}
	}
}
