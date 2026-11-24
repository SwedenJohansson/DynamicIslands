using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Camera fly-throughs (PF4). The builder places "Camera point" objects (Zones &amp; triggers) on the island: each
	/// belongs to a path by name (cam.path), has its place in it (cam.order), looks at an object with a name (cam.look;
	/// empty = the way the point faces), sets the speed to the next point (cam.speed, m/s) and how long the camera waits
	/// there (cam.hold, s). The when/then action "flythrough" (a shared verb: Target = the path, Arg = the range in metres)
	/// flies the camera of every player near the path through the points once, on a smooth curve (Catmull-Rom), with the
	/// player held; Esc skips it. The host runs it and tells everyone (IslandNetMessage.Moment, Name "path"). In the
	/// editor the inspector of a camera point previews its path and can record it as trailer frames (PNG pictures).
	/// </summary>
	public static class CameraPath
	{
		public const string PointName = "Camera_Point";
		public const string PathProp = "cam.path", OrderProp = "cam.order", LookProp = "cam.look", SpeedProp = "cam.speed", HoldProp = "cam.hold";
		public const string DefaultPath = "path";
		public const float DefaultSpeed = 6f, DefaultRange = 200f, MinSpeed = 0.5f, MaxSpeed = 60f, MaxHold = 30f;
		/// <summary>Trailer frames per second (each frame steps the flight by 1/FrameRate s) and the most frames one recording makes.</summary>
		public const int FrameRate = 30, MaxFrames = FrameRate * 180;

		public static bool IsPoint(string name) { return name == PointName; }

		/// <summary>The object list's entry (ContentCatalog.Register).</summary>
		public static void Register()
		{
			PlaceableCatalog.AddCustom(PointName, BuildMarker(), ContentCatalog.ZoneCategory, "Camera point (fly-through)");
		}

		static GameObject BuildMarker()
		{
			var root = new GameObject(PointName);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			Material body = ContentCatalog.MarkerMaterial(new Color(0.95f, 0.55f, 0.15f));
			Material dark = ContentCatalog.MarkerMaterial(new Color(0.2f, 0.2f, 0.22f));
			ContentCatalog.Part(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0f, -0.1f), Quaternion.identity, new Vector3(0.45f, 0.35f, 0.6f), body);
			ContentCatalog.Part(PrimitiveType.Cylinder, "Lens", root.transform, new Vector3(0f, 0f, 0.3f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.24f, 0.12f, 0.24f), dark);
			ContentCatalog.Part(PrimitiveType.Cube, "Pole", root.transform, new Vector3(0f, -0.6f, -0.1f), Quaternion.identity, new Vector3(0.06f, 0.9f, 0.06f), dark);
			ContentCatalog.AddLabel(root.transform, "Camera point", 0.5f);
			return root;
		}

		/// <summary>A world copy's settings always name a path (so the point is found by it).</summary>
		public static Dictionary<string, string> WorldProps(IDictionary<string, string> props)
		{
			var p = props != null ? new Dictionary<string, string>(props) : new Dictionary<string, string>();
			if (ObjectProps.Get(p, PathProp).Trim().Length == 0) p[PathProp] = DefaultPath;
			return p;
		}

		public static string PathOf(IDictionary<string, string> p) { string s = ObjectProps.Get(p, PathProp).Trim(); return s.Length > 0 ? s : DefaultPath; }
		public static int OrderOf(IDictionary<string, string> p) { int o; return int.TryParse(ObjectProps.Get(p, OrderProp), NumberStyles.Integer, CultureInfo.InvariantCulture, out o) ? o : 0; }
		public static float SpeedOf(IDictionary<string, string> p) { return Mathf.Clamp(ObjectProps.GetFloat(p, SpeedProp, DefaultSpeed), MinSpeed, MaxSpeed); }
		public static float HoldOf(IDictionary<string, string> p) { return Mathf.Clamp(ObjectProps.GetFloat(p, HoldProp, 0f), 0f, MaxHold); }

		/// <summary>The action's Arg: "range" or "range|once" (range empty = the default).</summary>
		public static float RangeOf(string arg)
		{
			float v;
			string r = (arg ?? "").Split('|')[0].Trim();
			return float.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? Mathf.Clamp(v, 5f, 5000f) : DefaultRange;
		}

		/// <summary>Whether the flight plays only the first time in a world (an intro), not on every trigger.</summary>
		public static bool OnceOf(string arg) { return (arg ?? "").Split('|').Skip(1).Any(x => x.Trim().Equals("once", StringComparison.OrdinalIgnoreCase)); }

		public static string ArgOf(string range, bool once) { string r = (range ?? "").Split('|')[0].Trim(); return once ? r + "|once" : r; }

		/// <summary>The island state key that marks a once-per-world path as flown (0x6000000 + the path name's hash).</summary>
		public const int OnceKeyBase = 0x6000000;
		public static int OnceKey(string path)
		{
			string s = ((path ?? "").Trim().Length > 0 ? path.Trim() : DefaultPath).ToLowerInvariant();
			unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return OnceKeyBase + (h & 0xFFFFF); }
		}

		public static string Describe(string path, string arg)
		{
			string p = (path ?? "").Trim();
			return "fly the camera along the path '" + (p.Length > 0 ? p : DefaultPath) + "' (players within " + RangeOf(arg).ToString("0", CultureInfo.InvariantCulture) + " m of it; Esc skips" + (OnceOf(arg) ? "; only the first time in a world" : "") + ")";
		}

		/// <summary>The camera points of an island file on a path (the plan's Check).</summary>
		public static int PointsIn(IslandFile f, string path)
		{
			string want = (path ?? "").Trim().Length > 0 ? path.Trim() : DefaultPath;
			return f == null || f.Objects == null ? 0 : f.Objects.Count(o => IsPoint(o.Name) && PathOf(o.Props).Equals(want, StringComparison.OrdinalIgnoreCase));
		}

		#region The curve

		/// <summary>One point of a flight: where the camera is, what it looks at, the speed to the next one, the wait there.</summary>
		public struct Key
		{
			public Vector3 Pos, Look;
			public float Speed, Hold;
		}

		/// <summary>A flight through keys: a wait at each, then a smooth curve to the next one at its speed.</summary>
		public class Flight
		{
			public readonly List<Key> Keys;
			readonly List<float> travel = new List<float>();
			public float Duration { get; private set; }

			public Flight(List<Key> keys)
			{
				Keys = keys ?? new List<Key>();
				Duration = 0f;
				for (int i = 0; i < Keys.Count; i++)
				{
					Duration += Keys[i].Hold;
					if (i == Keys.Count - 1) break;
					float len = 0f;
					Vector3 last = Keys[i].Pos;
					for (int s = 1; s <= 16; s++) { Vector3 at = PosOn(i, s / 16f); len += Vector3.Distance(last, at); last = at; }
					float d = Mathf.Max(0.05f, len / Mathf.Max(MinSpeed, Keys[i].Speed));
					travel.Add(d);
					Duration += d;
				}
			}

			Vector3 P(int i, bool look)
			{
				int n = Keys.Count;
				Func<int, Vector3> v = k => look ? Keys[k].Look : Keys[k].Pos;
				if (i < 0) return n > 1 ? 2f * v(0) - v(1) : v(0);
				if (i >= n) return n > 1 ? 2f * v(n - 1) - v(n - 2) : v(n - 1);
				return v(i);
			}

			Vector3 On(int seg, float t, bool look) { return CatmullRom(P(seg - 1, look), P(seg, look), P(seg + 1, look), P(seg + 2, look), t); }
			Vector3 PosOn(int seg, float t) { return On(seg, t, false); }

			/// <summary>The camera's place and look-at point at a time (0..Duration).</summary>
			public void Sample(float time, out Vector3 pos, out Vector3 look)
			{
				pos = look = Vector3.zero;
				if (Keys.Count == 0) return;
				float t = Mathf.Max(0f, time);
				for (int i = 0; i < Keys.Count; i++)
				{
					if (t <= Keys[i].Hold || i == Keys.Count - 1) { pos = Keys[i].Pos; look = Keys[i].Look; return; }
					t -= Keys[i].Hold;
					if (t <= travel[i]) { float k = t / travel[i]; pos = On(i, k, false); look = On(i, k, true); return; }
					t -= travel[i];
				}
			}

			public Quaternion RotationAt(float time)
			{
				Vector3 pos, look;
				Sample(time, out pos, out look);
				Vector3 d = look - pos;
				return d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : Quaternion.identity;
			}

			/// <summary>Points along the whole curve (the editor's line).</summary>
			public List<Vector3> Line(int perSegment)
			{
				var list = new List<Vector3>();
				for (int i = 0; i < Keys.Count - 1; i++)
					for (int s = 0; s < perSegment; s++) list.Add(PosOn(i, s / (float)perSegment));
				if (Keys.Count > 0) list.Add(Keys[Keys.Count - 1].Pos);
				return list;
			}
		}

		public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
		{
			float t2 = t * t, t3 = t2 * t;
			return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
		}

		/// <summary>The keys of the points of a path (sorted by their order), each looking at its named object or ahead.</summary>
		public static List<Key> KeysOf(IEnumerable<KeyValuePair<Transform, IDictionary<string, string>>> points, Func<string, Transform> named)
		{
			var keys = new List<Key>();
			foreach (var pt in points.OrderBy(x => OrderOf(x.Value)).ThenBy(x => x.Key.GetSiblingIndex()))
			{
				string lookName = ObjectProps.Get(pt.Value, LookProp).Trim();
				Transform look = lookName.Length > 0 && named != null ? named(lookName) : null;
				Vector3 pos = pt.Key.position;
				keys.Add(new Key
				{
					Pos = pos,
					Look = look != null && (look.position - pos).sqrMagnitude > 0.01f ? look.position : pos + pt.Key.forward * 20f,
					Speed = SpeedOf(pt.Value),
					Hold = HoldOf(pt.Value),
				});
			}
			return keys;
		}

		#endregion

		#region In a world

		/// <summary>Host: the action ran on an island.</summary>
		public static void HostRun(IslandWorldState.Entry e, string path, string arg)
		{
			if (e == null) return;
			if (OnceOf(arg))
			{
				// (once per world: the host marks the path flown in the island's saved state and tells everyone)
				int key = OnceKey(path);
				if (ContentState.IsUsed(e, key)) { Debug.Log("[CUSTOM ISLANDS] Fly-through '" + path + "' was flown in this world already (once)"); return; }
				int day = ContentState.Today;
				e.State[key] = new ObjectState { Active = false, Yield = 0, Day = day };
				IslandNetwork.SendUsed(e.Id, key, day);
			}
			IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Moment, Ids = new[] { e.Id }, Index = -1, Name = "path", Data = (path ?? "") + "\n" + (arg ?? "") });
			Here(e, path, arg);
		}

		public static void OnMessage(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost || msg.Ids == null || msg.Ids.Length == 0) return;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == msg.Ids[0]);
			string[] d = (msg.Data ?? "").Split(new[] { '\n' }, 2);
			if (e != null) Here(e, d[0], d.Length > 1 ? d[1] : "");
		}

		/// <summary>The keys of a path on an island in a world (empty: no such path).</summary>
		public static List<Key> WorldKeys(IslandWorldState.Entry e, string path)
		{
			string want = (path ?? "").Trim().Length > 0 ? path.Trim() : DefaultPath;
			List<IslandObjectRef> refs = Behaviours.RefsOf(e).ToList();
			var points = refs.Where(r => IsPoint(r.ObjectName) && PathOf(r.Props).Equals(want, StringComparison.OrdinalIgnoreCase))
				.Select(r => new KeyValuePair<Transform, IDictionary<string, string>>(r.transform, r.Props));
			return KeysOf(points, n => { IslandObjectRef r = refs.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)); return r != null ? r.transform : null; });
		}

		/// <summary>This machine: flies the path if its player is within range of one of its points.</summary>
		static void Here(IslandWorldState.Entry e, string path, string arg)
		{
			if (e.Root == null) return;
			List<Key> keys = WorldKeys(e, path);
			if (keys.Count < 2) { Debug.LogWarning("[CUSTOM ISLANDS] Fly-through: the path '" + path + "' on '" + e.HostName + "' has " + keys.Count + " camera point(s), 2 needed"); return; }
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null) return;
			float range = RangeOf(arg), far = keys.Min(k => Vector3.Distance(p.transform.position, k.Pos));
			if (far > range) { Debug.Log("[CUSTOM ISLANDS] Fly-through '" + path + "': this player is " + far.ToString("F0") + " m away (range " + range.ToString("F0") + " m)"); return; }
			Transform cam = p.Camera != null ? p.Camera.transform : Camera.main != null ? Camera.main.transform : null;
			if (cam == null) return;
			if (CameraMoment.Active) CameraMoment.Skip();
			CameraFlight.Begin(cam, new Flight(keys), path, false, null);
		}

		#endregion

		#region In the editor

		static IEnumerable<EditorGameObject> EditorObjects()
		{
			GameObject placed = GameObject.Find("PlacedObjects");
			return placed == null ? Enumerable.Empty<EditorGameObject>() : placed.GetComponentsInChildren<EditorGameObject>(true);
		}

		/// <summary>Editor: the names of the paths placed (for the action's ... button).</summary>
		public static List<string> PathsInEditor()
		{
			return EditorObjects().Where(o => IsPoint(o.GameObjectName)).Select(o => PathOf(o.Props)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
		}

		public static List<EditorGameObject> EditorPoints(string path)
		{
			string want = (path ?? "").Trim().Length > 0 ? path.Trim() : DefaultPath;
			return EditorObjects().Where(o => IsPoint(o.GameObjectName) && PathOf(o.Props).Equals(want, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		public static List<Key> EditorKeys(string path)
		{
			List<EditorGameObject> all = EditorObjects().ToList();
			var points = EditorPoints(path).Select(o => new KeyValuePair<Transform, IDictionary<string, string>>(o.transform, o.Props));
			return KeysOf(points, n => { EditorGameObject o = all.FirstOrDefault(x => ObjectProps.Get(x.Props, BehaviourProps.Name).Equals(n, StringComparison.OrdinalIgnoreCase)); return o != null ? o.transform : null; });
		}

		/// <summary>Editor: flies the editor camera along the path (record: saves each frame as a picture). Null = it started, else why not.</summary>
		public static string Preview(string path, bool record)
		{
			List<Key> keys = EditorKeys(path);
			if (keys.Count < 2) return "The path '" + path + "' has " + keys.Count + " camera point(s): place at least two with this path name.";
			Camera c = Camera.main;
			if (c == null) return "No camera.";
			string folder = null;
			if (record)
			{
				folder = Path.Combine(Path.Combine(DynamicIslands.assetpath, "trailers"), SafeName(path) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
				try { Directory.CreateDirectory(folder); } catch (Exception e) { return "Can't make the folder " + folder + ": " + e.Message; }
			}
			EditorCamera ec = c.GetComponent<EditorCamera>();
			CameraFlight.Begin(c.transform, new Flight(keys), path, true, folder, ec);
			return null;
		}

		static string SafeName(string s)
		{
			string n = new string((s ?? "path").Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_').ToArray());
			return n.Length > 0 ? n : "path";
		}

		static GameObject lines;
		static float nextLines;

		/// <summary>Every frame: in the editor, each path is drawn as a line through its points (refreshed twice a second).</summary>
		public static void Tick()
		{
			bool editor = DynamicIslands.InEditor();
			if (!editor) { if (lines != null) { UnityEngine.Object.Destroy(lines); lines = null; } return; }
			if (Time.unscaledTime < nextLines) return;
			nextLines = Time.unscaledTime + 0.5f;
			List<string> paths = PathsInEditor();
			if (paths.Count == 0 && lines == null) return;
			if (lines == null) lines = new GameObject("CustomIslands_CameraPaths");
			bool flying = CameraFlight.Active;
			var keep = new HashSet<string>();
			foreach (string p in paths)
			{
				List<Key> keys = EditorKeys(p);
				if (keys.Count < 2) continue;
				string n = "Path_" + p;
				keep.Add(n);
				Transform t = lines.transform.Find(n);
				LineRenderer lr = t != null ? t.GetComponent<LineRenderer>() : null;
				if (lr == null)
				{
					var go = new GameObject(n);
					go.transform.SetParent(lines.transform, false);
					lr = go.AddComponent<LineRenderer>();
					lr.useWorldSpace = true;
					lr.widthMultiplier = 0.15f;
					lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					lr.receiveShadows = false;
					Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
					lr.sharedMaterial = new Material(sh) { color = new Color(1f, 0.6f, 0.15f, 0.9f) };
					lr.startColor = lr.endColor = new Color(1f, 0.6f, 0.15f, 0.9f);
				}
				List<Vector3> pts = new Flight(keys).Line(12);
				lr.positionCount = pts.Count;
				lr.SetPositions(pts.ToArray());
				lr.enabled = !flying;
			}
			foreach (Transform child in lines.transform.Cast<Transform>().ToList())
				if (!keep.Contains(child.name)) UnityEngine.Object.Destroy(child.gameObject);
		}

		/// <summary>Tests: the lines drawn now.</summary>
		public static int LinesShown { get { return lines == null ? 0 : lines.GetComponentsInChildren<LineRenderer>().Count(l => l.enabled); } }

		#endregion
	}

	/// <summary>A camera flight running (world: the player's camera, held player; editor: the editor camera, maybe recorded).</summary>
	public class CameraFlight : MonoBehaviour
	{
		const float Glide = CameraMoment.Glide;
		static CameraFlight current;
		public static bool Active { get { return current != null; } }
		/// <summary>The player's look and walking held now (a world flight).</summary>
		public static bool InputHeld { get { return current != null && current.held.Count > 0; } }
		/// <summary>Tests: flights shown and skipped, the last one's path and length, frames written by the last recording and where.</summary>
		public static int Shown, Skipped, LastFrames;
		public static string LastPath, LastFolder;
		public static float LastDuration;

		Transform cam;
		CameraPath.Flight flight;
		bool editor;
		/// <summary>The editor's windows hidden for this flight (player 10-30: the inspector and the other panels covered the
		/// preview, as the window did the behaviour preview); shown again when it ends.</summary>
		Canvas hiddenUi;
		string folder;
		EditorCamera editorCam;
		Vector3 localPos, fromPos;
		Quaternion localRot, fromRot;
		float started, clock;
		int frames;
		readonly List<Behaviour> held = new List<Behaviour>();

		public static void Begin(Transform cam, CameraPath.Flight flight, string path, bool editor, string recordFolder, EditorCamera editorCam = null)
		{
			if (cam == null || flight == null || flight.Keys.Count < 2) return;
			if (current != null) current.End(false);
			var go = new GameObject("CustomIslands_CameraFlight");
			DontDestroyOnLoad(go);
			CameraFlight f = go.AddComponent<CameraFlight>();
			f.cam = cam;
			f.flight = flight;
			f.editor = editor;
			f.folder = recordFolder;
			f.localPos = cam.localPosition;
			f.localRot = cam.localRotation;
			f.fromPos = cam.position;
			f.fromRot = cam.rotation;
			f.started = Time.unscaledTime;
			if (editorCam != null && editorCam.enabled) { editorCam.enabled = false; f.editorCam = editorCam; }
			if (editor && EditorUI.Canvas != null && EditorUI.Canvas.enabled) { EditorUI.Canvas.enabled = false; f.hiddenUi = EditorUI.Canvas; }
			if (!editor)
			{
				Network_Player p = RAPI.GetLocalPlayer();
				if (p != null)
				{
					foreach (MouseLook ml in p.GetComponentsInChildren<MouseLook>(true).Where(x => x.enabled)) { ml.enabled = false; f.held.Add(ml); }
					if (p.PersonController != null && p.PersonController.enabled) { p.PersonController.enabled = false; f.held.Add(p.PersonController); }
				}
			}
			current = f;
			Shown++;
			LastPath = path;
			LastDuration = flight.Duration;
			LastFrames = 0;
			LastFolder = recordFolder;
			Debug.Log("[CUSTOM ISLANDS] Fly-through '" + path + "': " + flight.Keys.Count + " points, " + flight.Duration.ToString("0.#", CultureInfo.InvariantCulture) + " s" + (recordFolder != null ? ", recording to " + recordFolder : ""));
		}

		/// <summary>Esc (and tests): back at once.</summary>
		public static void Skip()
		{
			if (current == null) return;
			Skipped++;
			current.End(true);
		}

		bool Recording { get { return folder != null; } }
		/// <summary>The glide from the player's view in and back (a world only: the editor starts at the first point).</summary>
		float GlideTime { get { return editor ? 0f : Glide; } }

		void Pose()
		{
			if (cam == null) { End(false); return; }
			float t = Recording ? clock : Time.unscaledTime - started;
			float g = GlideTime, d = flight.Duration;
			Vector3 home = editor ? fromPos : cam.parent != null ? cam.parent.TransformPoint(localPos) : localPos;
			Quaternion homeRot = editor ? fromRot : cam.parent != null ? cam.parent.rotation * localRot : localRot;
			Vector3 pos, look;
			if (t < g)
			{
				flight.Sample(0f, out pos, out look);
				float k = Mathf.SmoothStep(0f, 1f, t / g);
				cam.position = Vector3.Lerp(fromPos, pos, k);
				cam.rotation = Quaternion.Slerp(fromRot, flight.RotationAt(0f), k);
			}
			else if (t < g + d)
			{
				flight.Sample(t - g, out pos, out look);
				cam.position = pos;
				cam.rotation = flight.RotationAt(t - g);
			}
			else if (t < g * 2f + d && g > 0f)
			{
				flight.Sample(d, out pos, out look);
				float k = Mathf.SmoothStep(0f, 1f, (t - g - d) / g);
				cam.position = Vector3.Lerp(pos, home, k);
				cam.rotation = Quaternion.Slerp(flight.RotationAt(d), homeRot, k);
			}
			else End(false);
		}

		void Update()
		{
			if (current != this) return;
			if ((Input.GetKeyDown(KeyCode.Escape) || EscKey.Pressed) && Time.unscaledTime - started > 0.1f) { EscKey.Swallow(); Skip(); return; }
			if (!editor && (!LoadSceneManager.IsGameSceneLoaded || RAPI.GetLocalPlayer() == null)) { End(false); return; }
			if (editor && !DynamicIslands.InEditor()) { End(false); return; }
			Pose();
		}

		void LateUpdate()
		{
			if (current != this) return;
			Pose();
			if (current != this || !Recording) return;
			if (frames >= CameraPath.MaxFrames) { Debug.Log("[CUSTOM ISLANDS] Fly-through recording: stopped at " + frames + " frames"); End(false); return; }
			if (WriteFrame()) frames++;
			LastFrames = frames;
			clock += 1f / CameraPath.FrameRate;
		}

		/// <summary>The camera's picture (without the editor's windows) as frame_00001.png...</summary>
		bool WriteFrame()
		{
			Camera c = cam != null ? cam.GetComponent<Camera>() : null;
			if (c == null) return false;
			int w = Mathf.Min(Screen.width, 1920), h = Mathf.RoundToInt(w * (Screen.height / (float)Mathf.Max(1, Screen.width)));
			RenderTexture rt = RenderTexture.GetTemporary(w, h, 24);
			RenderTexture before = c.targetTexture, active = RenderTexture.active;
			Texture2D tex = null;
			try
			{
				c.targetTexture = rt;
				c.Render();
				RenderTexture.active = rt;
				tex = new Texture2D(w, h, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
				tex.Apply();
				File.WriteAllBytes(Path.Combine(folder, "frame_" + (frames + 1).ToString("00000", CultureInfo.InvariantCulture) + ".png"), tex.EncodeToPNG());
				return true;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Fly-through recording: " + e.Message); End(false); return false; }
			finally
			{
				c.targetTexture = before;
				RenderTexture.active = active;
				RenderTexture.ReleaseTemporary(rt);
				if (tex != null) Destroy(tex);
			}
		}

		void ShowUi()
		{
			if (hiddenUi != null) hiddenUi.enabled = true;
			hiddenUi = null;
		}

		// (gone some other way: the editor's windows never stay hidden)
		void OnDestroy() { ShowUi(); if (current == this) current = null; }

		void End(bool skipped)
		{
			if (current == this) current = null;
			if (cam != null)
			{
				if (editor) { cam.position = fromPos; cam.rotation = fromRot; }
				else { cam.localPosition = localPos; cam.localRotation = localRot; }
			}
			if (editorCam != null) { editorCam.enabled = true; editorCam.Sync(); }
			ShowUi();
			foreach (Behaviour b in held) if (b != null) b.enabled = true;
			held.Clear();
			Debug.Log("[CUSTOM ISLANDS] Fly-through " + (skipped ? "skipped" : "over") + (Recording ? " (" + frames + " frames in " + folder + ")" : ""));
			if (Recording && DynamicIslands.InEditor()) IslandInfo.ShowMessage("Trailer: " + frames + " pictures (" + CameraPath.FrameRate + " a second) in " + folder);
			if (skipped && !editor && DynamicIslands.instance != null) DynamicIslands.instance.StartCoroutine(CameraMoment.ClosePause());
			Destroy(gameObject);
		}
	}

	public static partial class ObjectInspector
	{
		/// <summary>A camera point (PF4): its path, order, look-at, speed and wait; preview and record the path.</summary>
		static void CameraPointGroup(EditorGameObject target)
		{
			Dictionary<string, string> p = target.Props;
			RectTransform g = UIKit.Group(root, "Camera point (fly-through)");
			string path = CameraPath.PathOf(p);
			int count = CameraPath.EditorPoints(path).Count;
			UIKit.Label(g, "The camera passes the points of a path in their order, on a smooth curve, looking at the object named (or the way the point faces). A when/then action \"camera fly-through\" plays it. This path has " + count + " point" + (count == 1 ? "" : "s") + ".", 12, UIKit.TextMuted);
			InputField pf = SurvivorField(g, "Path", path, 26f, "The path's name: every camera point with this name is on it");
			pf.onEndEdit.AddListener(v => Set(target, CameraPath.PathProp, v.Trim().Length > 0 && v.Trim() != CameraPath.DefaultPath ? v.Trim().Replace("|", "/") : null));
			amountFields.Add(pf);
			InputField of = SurvivorField(g, "Order", CameraPath.OrderOf(p).ToString(CultureInfo.InvariantCulture), 26f, "Its place in the path: lower first (1, 2, 3...)");
			of.contentType = InputField.ContentType.IntegerNumber;
			of.onEndEdit.AddListener(v => { int n; Set(target, CameraPath.OrderProp, int.TryParse(v, out n) && n != 0 ? n.ToString(CultureInfo.InvariantCulture) : null); });
			amountFields.Add(of);
			InputField lf = SurvivorField(g, "Look at", ObjectProps.Get(p, CameraPath.LookProp), 26f, "The name of the object the camera looks at here (empty = the way the point faces)");
			lf.onEndEdit.AddListener(v => Set(target, CameraPath.LookProp, v.Trim().Length > 0 ? v.Trim() : null));
			amountFields.Add(lf);
			InputField sf = SurvivorField(g, "Speed m/s", CameraPath.SpeedOf(p).ToString("0.#", CultureInfo.InvariantCulture), 26f, "How fast the camera flies from here to the next point (0.5-60 m/s)");
			sf.contentType = InputField.ContentType.DecimalNumber;
			sf.onEndEdit.AddListener(v => { float f; Set(target, CameraPath.SpeedProp, float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && Mathf.Abs(f - CameraPath.DefaultSpeed) > 0.01f ? ObjectProps.Format(Mathf.Clamp(f, CameraPath.MinSpeed, CameraPath.MaxSpeed)) : null); });
			amountFields.Add(sf);
			InputField hf = SurvivorField(g, "Wait s", CameraPath.HoldOf(p).ToString("0.#", CultureInfo.InvariantCulture), 26f, "Seconds the camera stays at this point (0-30)");
			hf.contentType = InputField.ContentType.DecimalNumber;
			hf.onEndEdit.AddListener(v => { float f; Set(target, CameraPath.HoldProp, float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && f > 0f ? ObjectProps.Format(Mathf.Clamp(f, 0f, CameraPath.MaxHold)) : null); });
			amountFields.Add(hf);
			RectTransform row = UIKit.Row(g, 26f, 4f, "CamPointRow");
			UIKit.Button(row, "To my view", () => { if (CameraPointToView(target)) Refresh(); }, "Moves this point to where the editor camera is, facing the way it looks (Ctrl+Z undoes it)", -1, 26f, 12).name = "Button_CamToView";
			RectTransform row2 = UIKit.Row(g, 26f, 4f, "CamPreviewRow");
			UIKit.Button(row2, "Preview path", () => { string why = CameraPath.Preview(path, false); if (why != null) IslandInfo.ShowMessage(why); }, "Flies the camera along the path as players will see it, with the editor's windows hidden (Esc stops)", -1, 26f, 12).name = "Button_CamPreview";
			UIKit.Button(row2, "Record trailer", () => { string why = CameraPath.Preview(path, true); if (why != null) IslandInfo.ShowMessage(why); }, "Flies the path and saves every frame (" + CameraPath.FrameRate + " a second, without the editor's windows) as PNG pictures in the mod's trailers folder, for a video (Esc stops)", -1, 26f, 12).name = "Button_CamRecord";
		}

		internal static bool CameraPointToView(EditorGameObject target)
		{
			Camera c = Camera.main;
			if (target == null || c == null) return false;
			var gizmo = DynamicIslands.EditorGizmoHandler;
			var cmd = new RuntimeGizmos.TransformCommand(gizmo, target.transform);
			target.transform.position = c.transform.position;
			target.transform.rotation = c.transform.rotation;
			cmd.StoreNewTransformValues();
			CommandUndoRedo.UndoRedoManager.Insert(cmd);
			if (gizmo != null) gizmo.SetPivotPoint();
			return true;
		}
	}
}
