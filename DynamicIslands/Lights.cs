using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Light puzzles (PF6, 2026-10-25): a light (the editor's "Puzzle light", or any object with a light group) players
	/// switch on and off with the use key. Lit is kept as the object's open bit (Behaviours' shown/open state: saved with
	/// the world, sent to every player), turned round for a light that starts on. The host runs its "lit"/"unlit"
	/// events, and "alllit" on every light of its group (plus the signal "lights:group") when the whole group is on.
	/// The action "light|name or group:G|on/off/toggle", the checks "light|name|on" and "lights|group|on" and the
	/// quest step "Light the group" build puzzles from them.
	/// </summary>
	public static class Lights
	{
		public const string ObjectName = "Story_Light", GroupProp = "light.group", OnProp = "light.on";
		public const string Verb = "light", LightCheck = "light", GroupCheck = "lights", QuestType = "lights";
		public const string LitEvent = "lit", UnlitEvent = "unlit", AllLitEvent = "alllit", GroupPrefix = "group:";
		public const string Hint = "a light players switch on and off (use key): its group, \"lit\"/\"unlit\"/\"alllit\" events, the light action and checks make light puzzles";

		public static bool Is(string name) { return name == ObjectName; }
		public static string GroupOf(IDictionary<string, string> p) { return ObjectProps.Get(p, GroupProp).Trim(); }
		public static bool StartsOn(IDictionary<string, string> p) { string v = ObjectProps.Get(p, OnProp).Trim().ToLowerInvariant(); return v == "1" || v == "true" || v == "on"; }
		public static bool IsLight(string name, IDictionary<string, string> p) { return Is(name) || (p != null && GroupOf(p).Length > 0); }
		static bool IsLight(IslandObjectRef r) { return r != null && IsLight(r.ObjectName, r.Props); }

		/// <summary>The signal a group sends when all its lights are on.</summary>
		public static string SignalOf(string group) { return "lights:" + (group ?? "").Trim().ToLowerInvariant(); }

		public static void Defaults(string name, Dictionary<string, string> props)
		{
			if (!Is(name)) return;
			props[GroupProp] = "Lamps";
			props[BehaviourProps.Use] = "Switch the light";
		}

		/// <summary>A light's use lines as if the builder had written them: the use key switches it (unless its own lines
		/// already switch it).</summary>
		public static IDictionary<string, string> Expand(string objectName, IDictionary<string, string> props)
		{
			if (props == null || !IsLight(objectName, props)) return props;
			List<ObjAction> use = ObjAction.ParseLines(ObjectProps.Get(props, "on.use"));
			bool self = use.Any(a => (a.Verb == "switch" || a.Verb == "open" || a.Verb == "close" || a.Verb == Verb) && (a.Target.Trim().Length == 0 || a.Target.Trim().Equals("self", StringComparison.OrdinalIgnoreCase)));
			bool label = ObjectProps.Get(props, BehaviourProps.Use).Length > 0;
			if (self && label) return props;
			var d = new Dictionary<string, string>(props);
			if (!self) { string u = ObjectProps.Get(props, "on.use"); d["on.use"] = "switch|" + (u.Length > 0 ? "\n" + u : ""); }
			if (!label) d[BehaviourProps.Use] = "Switch the light";
			return d;
		}

		public static GameObject BuildMarker()
		{
			var root = new GameObject(ObjectName);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			Transform marker = new GameObject("Marker").transform;
			marker.SetParent(root.transform, false);
			ContentCatalog.Part(PrimitiveType.Cylinder, "Pole", marker, new Vector3(0f, 0.6f, 0f), Quaternion.identity, new Vector3(0.08f, 0.6f, 0.08f), ContentCatalog.MarkerMaterial(new Color(0.3f, 0.25f, 0.2f)));
			ContentCatalog.Part(PrimitiveType.Cylinder, "Cap", marker, new Vector3(0f, 1.48f, 0f), Quaternion.identity, new Vector3(0.32f, 0.03f, 0.32f), ContentCatalog.MarkerMaterial(new Color(0.25f, 0.25f, 0.25f)));
			ContentCatalog.Part(PrimitiveType.Sphere, BulbName, marker, new Vector3(0f, 1.3f, 0f), Quaternion.identity, Vector3.one * 0.28f, OffMaterial);
			ContentCatalog.AddLabel(root.transform, "Light", 1.7f);
			return root;
		}

		internal const string BulbName = "Bulb";
		internal static Material OnMaterial { get { return ContentCatalog.MarkerMaterial(new Color(1f, 0.92f, 0.55f)); } }
		internal static Material OffMaterial { get { return ContentCatalog.MarkerMaterial(new Color(0.35f, 0.35f, 0.32f)); } }

		/// <summary>World: a light gets its switching (Behaviours.Attach); its editor name tag goes.</summary>
		internal static void Attach(GameObject go, IslandObjectRef r)
		{
			foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
				if (t.name == ContentCatalog.MarkerOnly && t.GetComponent<TextMesh>() != null) t.gameObject.SetActive(false);
			go.AddComponent<PuzzleLight>().Ref = r;
		}

		/// <summary>Whether a light is on now (its open bit, turned round for one that starts on).</summary>
		public static bool Lit(IslandWorldState.Entry e, IslandObjectRef r)
		{
			if (e == null || r == null) return false;
			bool visible, open;
			Behaviours.StateNow(e, r, out visible, out open);
			return open != StartsOn(r.Props);
		}

		/// <summary>The island's lights of a group (all loaded ones).</summary>
		public static List<IslandObjectRef> GroupLights(IslandWorldState.Entry e, string group)
		{
			string g = (group ?? "").Trim();
			return Behaviours.RefsOf(e).Where(r => IsLight(r) && string.Equals(GroupOf(r.Props), g, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		public static int LitCount(IslandWorldState.Entry e, string group) { return GroupLights(e, group).Count(r => Lit(e, r)); }

		/// <summary>The lights an action or check means: "group:G" = the group's, else by name (empty = this object).</summary>
		internal static List<IslandObjectRef> Targets(IslandWorldState.Entry e, int index, string target)
		{
			string t = (target ?? "").Trim();
			if (t.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase)) return GroupLights(e, t.Substring(GroupPrefix.Length));
			return Behaviours.TargetsOf(e, index, t).Where(IsLight).ToList();
		}

		/// <summary>Host: the shared action "light|target|on/off/toggle" (empty = toggle).</summary>
		internal static void HostRun(IslandWorldState.Entry e, int index, ObjAction a)
		{
			string how = (a.Arg ?? "").Trim().ToLowerInvariant();
			foreach (IslandObjectRef r in Targets(e, index, a.Target))
			{
				bool on = how == "on" ? true : how == "off" ? false : !Lit(e, r);
				bool visible, open;
				Behaviours.StateNow(e, r, out visible, out open);
				Behaviours.SetState(e, r.Index, visible, on != StartsOn(r.Props));
			}
		}

		/// <summary>The checks: "light|name|on" (every light of that name; empty = this one) and "lights|group|on".</summary>
		internal static bool Holds(IslandWorldState.Entry e, int index, ObjCheck c)
		{
			bool on = (c.Arg ?? "").Trim().ToLowerInvariant() != "off";
			List<IslandObjectRef> lights = c.Kind == GroupCheck ? GroupLights(e, c.Target) : Targets(e, index, c.Target);
			return lights.Count > 0 && lights.All(r => Lit(e, r) == on);
		}

		/// <summary>Host: a light changed (PuzzleLight saw it): its event, and the group's when all of it is on now.</summary>
		internal static void Changed(IslandWorldState.Entry e, IslandObjectRef r, bool lit)
		{
			if (!Raft_Network.IsHost || e == null) return;
			Behaviours.FireFromHost(e, r.Index, lit ? LitEvent : UnlitEvent);
			string group = GroupOf(r.Props);
			if (!lit || group.Length == 0) return;
			List<IslandObjectRef> all = GroupLights(e, group);
			if (all.Count == 0 || !all.All(x => Lit(e, x))) return;
			if (!Behaviours.HasSignal(e, SignalOf(group))) Behaviours.SendSignal(e, SignalOf(group));
			foreach (IslandObjectRef x in all) Behaviours.FireFromHost(e, x.Index, AllLitEvent);
		}
	}

	/// <summary>A puzzle light in a world: shows its state (its lights, flames, bulb) and tells the host of a change.</summary>
	public class PuzzleLight : MonoBehaviour
	{
		public IslandObjectRef Ref;
		Light[] lights;
		ParticleSystem[] flames;
		Renderer bulb;
		bool shown, ready;
		float next;

		void Start()
		{
			lights = GetComponentsInChildren<Light>(true);
			if (lights.Length == 0)
			{
				// (an object without a light of its own gets one, where its bulb or its top is)
				var go = new GameObject("CI_PuzzleLight");
				go.transform.SetParent(transform, false);
				Transform b = transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == Lights.BulbName);
				go.transform.localPosition = b != null ? transform.InverseTransformPoint(b.position) : Vector3.up * 1.5f;
				Light l = go.AddComponent<Light>();
				l.type = LightType.Point;
				l.range = 9f;
				l.intensity = 1.6f;
				l.color = new Color(1f, 0.85f, 0.6f);
				l.shadows = LightShadows.None;
				lights = new[] { l };
			}
			flames = GetComponentsInChildren<ParticleSystem>(true);
			Transform bt = transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == Lights.BulbName);
			bulb = bt != null ? bt.GetComponent<Renderer>() : null;
		}

		void Update()
		{
			if (Time.time < next || Ref == null) return;
			next = Time.time + 0.25f;
			IslandWorldState.Entry e = ContentState.EntryOf(transform);
			if (e == null) return;
			bool lit = Lights.Lit(e, Ref);
			if (ready && lit == shown) return;
			bool changed = ready;
			shown = lit;
			ready = true;
			Show(lit);
			if (changed) Lights.Changed(e, Ref, lit);
		}

		void Show(bool lit)
		{
			if (lights != null) foreach (Light l in lights) if (l != null) l.enabled = lit;
			if (flames != null) foreach (ParticleSystem p in flames) if (p != null) { if (lit) p.Play(true); else p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
			if (bulb != null) bulb.sharedMaterial = lit ? Lights.OnMaterial : Lights.OffMaterial;
		}

		/// <summary>Whether it shows lit now (tests).</summary>
		public bool ShowsLit { get { return ready && shown && lights != null && lights.Any(l => l != null && l.enabled); } }
	}
}
