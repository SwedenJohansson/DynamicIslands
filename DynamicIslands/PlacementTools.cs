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
	}

	/// <summary>
	/// A search field above the editor's object list: typing shows only objects whose label (or category) contains
	/// the text. Created in code on top of the list from the UI bundle.
	/// </summary>
	public class ObjectListSearch : MonoBehaviour
	{
		InputField field;
		Transform content;

		/// <summary>Adds the search field at the top of the list's scroll view (which is shortened to make room).</summary>
		public static void Create(RectTransform scrollView, Transform content, Font font)
		{
			const float Height = 30f;
			GameObject go = DefaultControls.CreateInputField(new DefaultControls.Resources());
			go.name = "ObjectSearch";
			go.transform.SetParent(scrollView.parent, false);
			RectTransform r = go.GetComponent<RectTransform>();
			// Same horizontal placement as the list, directly above its (new) top edge
			r.anchorMin = new Vector2(scrollView.anchorMin.x, scrollView.anchorMax.y);
			r.anchorMax = scrollView.anchorMax;
			r.pivot = new Vector2(0.5f, 1f);
			r.offsetMin = new Vector2(scrollView.offsetMin.x, scrollView.offsetMax.y - Height);
			r.offsetMax = scrollView.offsetMax;
			scrollView.offsetMax -= new Vector2(0, Height + 4f);
			go.transform.SetSiblingIndex(scrollView.GetSiblingIndex() + 1);

			go.GetComponent<Image>().color = new Color(0.93f, 0.87f, 0.72f);
			InputField f = go.GetComponent<InputField>();
			foreach (Text t in go.GetComponentsInChildren<Text>(true))
			{
				if (font != null) t.font = font;
				t.fontSize = 14; t.color = new Color(0.15f, 0.1f, 0.05f); t.verticalOverflow = VerticalWrapMode.Overflow;
			}
			f.placeholder.GetComponent<Text>().text = "Search objects...";
			f.placeholder.GetComponent<Text>().color = new Color(0.4f, 0.35f, 0.3f, 0.8f);

			ObjectListSearch search = go.AddComponent<ObjectListSearch>();
			search.field = f;
			search.content = content;
			f.onValueChanged.AddListener(search.Filter);
		}

		void Update()
		{
			bool typing = field != null && field.isFocused;
			if (typing) EditorInput.IsTyping = true;
			else if (wasTyping) EditorInput.IsTyping = false;
			wasTyping = typing;
			if (typing && Input.GetKeyDown(KeyCode.Escape)) { field.text = ""; field.DeactivateInputField(); }
		}

		bool wasTyping;

		void Filter(string query)
		{
			query = (query ?? "").Trim().ToLowerInvariant();
			GameObject header = null;
			string headerName = "";
			bool headerHasVisible = false;
			var headers = new List<KeyValuePair<GameObject, bool>>();
			foreach (Transform child in content)
			{
				if (child.name.StartsWith("Header_"))
				{
					if (header != null) headers.Add(new KeyValuePair<GameObject, bool>(header, headerHasVisible));
					header = child.gameObject;
					headerName = child.name.Substring("Header_".Length).ToLowerInvariant();
					headerHasVisible = false;
					continue;
				}
				if (child.name == "Button") continue; // the hidden template
				Text label = child.GetComponentInChildren<Text>(true);
				bool visible = query.Length == 0 || headerName.Contains(query) || (label != null && label.text.ToLowerInvariant().Contains(query));
				child.gameObject.SetActive(visible);
				headerHasVisible |= visible;
			}
			if (header != null) headers.Add(new KeyValuePair<GameObject, bool>(header, headerHasVisible));
			foreach (var h in headers) h.Key.SetActive(h.Value);
		}
	}
}
