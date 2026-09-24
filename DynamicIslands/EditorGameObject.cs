using RuntimeGizmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace DynamicIslands.Editor
{



	public class EditorGameObject : MonoBehaviour
	{
		public string GameObjectName;
		public float arrowLength = 5;
		/// <summary>The object's extra data (creature settings, note text, tint...), saved with the island; see ObjectProps.</summary>
		public Dictionary<string, string> Props = new Dictionary<string, string>();

		/// <summary>
		/// Marks a spawned catalog object as placed in the editor. Props are copied (null = the object's defaults: a
		/// creature or note from the list starts with its own settings).
		/// </summary>
		public static EditorGameObject Attach(GameObject go, string name, Dictionary<string, string> props = null)
		{
			EditorGameObject ego = UIKit.Ensure<EditorGameObject>(go);
			ego.GameObjectName = name;
			ego.Props = props != null ? new Dictionary<string, string>(props) : ObjectProps.Defaults(name);
			ObjectProps.ApplyInEditor(go, ego.Props);
			return ego;
		}

		void Start()
		{


		}


		public void ShowGizmos(GizmosType GizmoType)
		{
			switch (GizmoType)
			{
				case GizmosType.Position:
					break;

				case GizmosType.Rotation:
					break;

				case GizmosType.Scale:
					break;
			}
		}

		public enum GizmosType
		{
			Position = 0,
			Rotation =1,
			Scale = 2
		}


	}


}
