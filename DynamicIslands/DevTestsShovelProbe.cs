using System;
using System.Linq;
using System.Reflection;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>How Raft's shovel finds dirt (TODO: dirt on islands): the fields of the types with a diggable layer/tag or
		/// a terrain id for dirt, their values on loaded objects, and the tag, layer and textures of the terrains around.</summary>
		[ConsoleCommand(name: "CIProbeShovel", docs: "Dev: how Raft's shovel finds dirt - the diggable layer and tag, terrain ids, and the terrains' tags and layers")]
		public static void ProbeShovel(string[] args)
		{
			const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
			Assembly raft = typeof(Network_Player).Assembly;
			Type[] types;
			try { types = raft.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
			foreach (Type t in types)
			{
				FieldInfo[] fs = t.GetFields(all).Where(f => f.Name.IndexOf("diggable", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.StartsWith("TerrainID_") || f.Name == "Shovelable" || f.Name.IndexOf("TreasurePointDirt", StringComparison.Ordinal) >= 0).ToArray();
				if (fs.Length == 0) continue;
				Log("type " + t.FullName + " (base " + (t.BaseType != null ? t.BaseType.Name : "-") + ")");
				foreach (FieldInfo f in fs)
				{
					string v = "";
					try
					{
						if (f.IsStatic) v = Show(f.GetValue(null));
						else if (typeof(UnityEngine.Object).IsAssignableFrom(t))
						{
							UnityEngine.Object o = Resources.FindObjectsOfTypeAll(t).FirstOrDefault();
							v = o != null ? Show(f.GetValue(o)) + " (on " + o.name + ")" : "(none loaded)";
						}
					}
					catch (Exception e) { v = "? " + e.Message; }
					Log("  " + f.FieldType.Name + " " + f.Name + " = " + v);
				}
			}
			foreach (MethodInfo m in types.SelectMany(t => t.GetMethods(all).Where(x => x.DeclaringType == t && (x.Name.IndexOf("Shovel", StringComparison.Ordinal) >= 0 || x.Name.IndexOf("Dig", StringComparison.Ordinal) >= 0))))
				Log("method " + m.DeclaringType.FullName + "." + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray()) + ")");
			foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.CompareTag("Pickup_Shovel")).Take(40))
			{
				Transform r = g.transform; while (r.parent != null) r = r.parent;
				Component[] cs = g.GetComponents<Component>();
				Log("shovel spot " + g.name + " in " + r.name + " scene '" + g.scene.name + "' layer " + LayerMask.LayerToName(g.layer) + " comps: " + string.Join(", ", cs.Select(c => c != null ? c.GetType().Name : "-").ToArray()));
				foreach (Component c in cs.Where(c => c != null && c.GetType().Assembly == raft))
					foreach (FieldInfo f in c.GetType().GetFields(all).Where(f => !f.IsStatic).Take(12))
					{ try { Log("    " + c.GetType().Name + "." + f.Name + " = " + Show(f.GetValue(c))); } catch { } }
			}
			foreach (string n in global::DynamicIslands.Editor.PlaceableCatalog.Names.Where(n => { GameObject p = global::DynamicIslands.Editor.PlaceableCatalog.Get(n); return p != null && p.GetComponentsInChildren<Transform>(true).Any(x => x.CompareTag("Pickup_Shovel")); }).Take(30))
				Log("catalog has a shovel spot: " + n);
			foreach (Terrain t in Terrain.activeTerrains)
			{
				TerrainData d = t.terrainData;
				Log("terrain " + t.name + " tag=" + t.gameObject.tag + " layer=" + LayerMask.LayerToName(t.gameObject.layer) + " (" + t.gameObject.layer + ") at " + t.transform.position + " layers: " +
					(d != null && d.terrainLayers != null ? string.Join(", ", d.terrainLayers.Select(l => l != null ? l.name : "-").ToArray()) : "-") + " parent " + (t.transform.parent != null ? t.transform.parent.name : "-"));
			}
		}

		static string Show(object v)
		{
			if (v == null) return "null";
			if (v is LayerMask) { int m = ((LayerMask)v).value; return "mask " + m + " [" + string.Join(", ", Enumerable.Range(0, 32).Where(i => (m & (1 << i)) != 0).Select(i => LayerMask.LayerToName(i)).ToArray()) + "]"; }
			if (v is Array) return "[" + string.Join(", ", ((Array)v).Cast<object>().Select(Show).ToArray()) + "]";
			return v.ToString();
		}
	}
}
