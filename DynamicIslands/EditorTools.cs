using System.Collections.Generic;
using CommandUndoRedo;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>Editor-wide keyboard state.</summary>
	public static class EditorInput
	{
		/// <summary>True while a text field (e.g. the island name) has focus: camera and tool shortcuts are ignored.</summary>
		public static bool IsTyping;

		public static bool Ctrl { get { return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl); } }
		public static bool Shift { get { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); } }

		/// <summary>Global editor shortcuts; called every frame by the terrain editor (which lives as long as the editor).</summary>
		public static void HandleShortcuts()
		{
			if (IsTyping || !Ctrl) return;
			if (Input.GetKeyDown(KeyCode.Z) && !Shift) UndoRedoManager.Undo();
			else if (Input.GetKeyDown(KeyCode.Y) || (Input.GetKeyDown(KeyCode.Z) && Shift)) UndoRedoManager.Redo();
			else if (Input.GetKeyDown(KeyCode.S)) IslandFilesWindow.QuickSave();
			else if (Input.GetKeyDown(KeyCode.O)) IslandFilesWindow.Open();
		}
	}

	/// <summary>
	/// Undo step for one terrain brush stroke (sculpt or paint): the heights, texture weights and paint mask of
	/// the area the stroke touched, before and after.
	/// </summary>
	public class TerrainStrokeCommand : ICommand
	{
		readonly TerrainData data;
		readonly RectInt heightRect, alphaRect;
		readonly float[,] heightsBefore, heightsAfter;
		readonly float[,,] alphaBefore, alphaAfter;
		readonly float[,] maskBefore, maskAfter;

		public TerrainStrokeCommand(TerrainData data, RectInt heightRect, float[,] heightsBefore, RectInt alphaRect, float[,,] alphaBefore, float[,] maskBefore, float[,] mask)
		{
			this.data = data;
			this.heightRect = heightRect;
			this.alphaRect = alphaRect;
			this.heightsBefore = heightsBefore;
			this.alphaBefore = alphaBefore;
			this.maskBefore = maskBefore;
			heightsAfter = data.GetHeights(heightRect.x, heightRect.y, heightRect.width, heightRect.height);
			alphaAfter = data.GetAlphamaps(alphaRect.x, alphaRect.y, alphaRect.width, alphaRect.height);
			maskAfter = mask != null ? Crop(mask, alphaRect) : null;
		}

		public void Execute() { Apply(heightsAfter, alphaAfter, maskAfter); }
		public void UnExecute() { Apply(heightsBefore, alphaBefore, maskBefore); }

		void Apply(float[,] heights, float[,,] alpha, float[,] mask)
		{
			if (data == null) return;
			data.SetHeights(heightRect.x, heightRect.y, heights);
			data.SetAlphamaps(alphaRect.x, alphaRect.y, alpha);
			float[,] target = terraineditor.paintMask;
			if (mask != null && target != null && target.GetLength(0) >= alphaRect.yMax && target.GetLength(1) >= alphaRect.xMax)
				for (int z = 0; z < alphaRect.height; z++)
					for (int x = 0; x < alphaRect.width; x++)
						target[alphaRect.y + z, alphaRect.x + x] = mask[z, x];
		}

		public static float[,] Crop(float[,] source, RectInt r)
		{
			var result = new float[r.height, r.width];
			for (int z = 0; z < r.height; z++)
				for (int x = 0; x < r.width; x++)
					result[z, x] = source[r.y + z, r.x + x];
			return result;
		}

		public static float[,,] Crop(float[,,] source, RectInt r)
		{
			int layers = source.GetLength(2);
			var result = new float[r.height, r.width, layers];
			for (int z = 0; z < r.height; z++)
				for (int x = 0; x < r.width; x++)
					for (int l = 0; l < layers; l++)
						result[z, x, l] = source[r.y + z, r.x + x, l];
			return result;
		}
	}

	/// <summary>Undo step for placing (visible after) or deleting (hidden after) objects. Deleted objects are only hidden.</summary>
	public class ObjectVisibilityCommand : ICommand
	{
		readonly List<GameObject> objects;
		readonly bool visibleAfter;

		public ObjectVisibilityCommand(IEnumerable<GameObject> objects, bool visibleAfter)
		{
			this.objects = new List<GameObject>(objects);
			this.visibleAfter = visibleAfter;
		}

		public void Execute() { Set(visibleAfter); }
		public void UnExecute() { Set(!visibleAfter); }

		void Set(bool visible)
		{
			if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.ClearTargets(false);
			foreach (GameObject go in objects)
				if (go != null) go.SetActive(visible);
		}
	}

	/// <summary>A ring on the terrain under the mouse showing the brush size, coloured by tool.</summary>
	public static class BrushCursor
	{
		const int Segments = 64;
		static LineRenderer ring;

		static readonly Color SculptColor = new Color(1f, 1f, 1f, 0.9f);
		static readonly Color AutoColor = new Color(0.4f, 0.9f, 1f, 0.9f);
		static readonly Color[] LayerColors =
		{
			new Color(0.55f, 0.45f, 0.3f, 0.95f), // seabed
			new Color(1f, 0.85f, 0.45f, 0.95f),   // sand
			new Color(0.45f, 0.9f, 0.3f, 0.95f),  // grass
			new Color(0.75f, 0.75f, 0.75f, 0.95f) // rock
		};

		public static void Update(Terrain terrain, bool visible, Vector3 centre)
		{
			if (ring == null)
			{
				if (!visible) return;
				var go = new GameObject("CustomIslands_BrushCursor");
				ring = go.AddComponent<LineRenderer>();
				ring.loop = true;
				ring.positionCount = Segments;
				ring.useWorldSpace = true;
				ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				ring.receiveShadows = false;
				Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
				if (shader != null) ring.material = new Material(shader);
			}
			ring.enabled = visible;
			if (!visible) return;

			Color c = terraineditor.modificationAction == terraineditor.TerrainModificationAction.PaintLayer ? LayerColors[Mathf.Clamp(terraineditor.paintLayer, 0, LayerColors.Length - 1)]
				: terraineditor.modificationAction == terraineditor.TerrainModificationAction.AutoPaint ? AutoColor : SculptColor;
			ring.startColor = ring.endColor = c;
			if (ring.material != null) ring.material.color = c;
			float radius = terraineditor.brushRadius;
			ring.startWidth = ring.endWidth = Mathf.Clamp(radius * 0.03f, 0.25f, 2f);

			float baseY = terrain.transform.position.y;
			for (int i = 0; i < Segments; i++)
			{
				float a = i / (float)Segments * Mathf.PI * 2f;
				Vector3 p = centre + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
				p.y = baseY + terrain.SampleHeight(p) + 0.4f;
				ring.SetPosition(i, p);
			}
		}

		public static void Hide()
		{
			if (ring != null) ring.enabled = false;
		}
	}
}
