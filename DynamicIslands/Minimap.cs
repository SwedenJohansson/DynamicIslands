using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The island map (PF5). Near an island a small map of it sits in the top-right corner: a picture from straight
	/// above (an orthographic camera, rendered once per island and when the full map opens), the player as an arrow,
	/// the other players, the markers of every quest's current step (objects named as the step's target) and the
	/// builder's own map markers ("Map marker" objects, Zones &amp; triggers). The map key (M) goes corner -> full
	/// screen -> off; + and - zoom (1x, 2x, 4x, centred on the player). The ping key (G) marks the spot the player
	/// looks at for the crew: a beam with the name in the world, a dot on the map and a sound, for a few seconds.
	/// A player sends a ping to the host (IslandNetMessage.Ping), the host shows it and passes it to everyone.
	/// </summary>
	public static class Minimap
	{
		public const string MarkerName = "Map_Marker", LabelProp = "map.label";
		public const float PingSeconds = 6f, PingRange = 300f, PingAhead = 50f, PingGap = 1f, NearIsland = 80f;
		public const int Corner = 0, Full = 1, Off = 2;
		public const string PingSound = "event:/raft/door/door_unlock_keycard";
		/// <summary>Map dots: 0 me, 1 another player, 2 a quest step's target, 3 a builder's map marker, 4 a ping.</summary>
		public const int MarkMe = 0, MarkPlayer = 1, MarkQuest = 2, MarkMarker = 3, MarkPing = 4;
		const int TexSize = 512;

		public struct Mark { public Vector3 Pos; public string Label; public int Kind; public float Yaw; }

		public class Ping
		{
			public int Island = -1;   // -1: at sea, Local is then relative to the raft
			public Vector3 Local;
			public string Who = "";
			public ulong From;
			public float Until;
			public GameObject World;
			public Vector3 WorldPos
			{
				get
				{
					IslandWorldState.Entry e = Island >= 0 ? EntryOf(Island) : null;
					if (e != null && e.Root != null) return e.Root.transform.TransformPoint(Local);
					Raft raft = ComponentManager<Raft>.Value;
					return raft != null ? raft.transform.position + Local : Local;
				}
			}
		}

		public static int Mode = Corner;
		public static int Zoom = 1;
		public static IslandWorldState.Entry Island { get; private set; }
		public static float HalfSize { get; private set; }
		public static Vector3 Center { get; private set; }
		public static int RenderCount { get; private set; }
		public static readonly List<Mark> Marks = new List<Mark>();
		public static readonly List<Ping> Pings = new List<Ping>();
		/// <summary>Dev tests: pings sent (by this player) and shown.</summary>
		public static int PingsSent, PingsShown;
		public static bool Shown { get { return panel != null && panel.gameObject.activeSelf; } }

		static Camera cam;
		static RenderTexture tex;
		static Canvas canvas;
		static RectTransform panel, dots;
		static RawImage image;
		static Text title;
		static readonly List<RectTransform> dotPool = new List<RectTransform>();
		static float nextFind, nextMarks, nextRender, lastPing = -10f;
		static int lastMode = -1;
		static string islandTitle = "";

		#region Every frame

		public static void Tick()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor())
			{
				if (canvas != null) canvas.gameObject.SetActive(false);
				if (!LoadSceneManager.IsGameSceneLoaded) { ClearPings(); Island = null; }
				return;
			}
			Network_Player me = RAPI.GetLocalPlayer();
			if (me == null) return;
			if (KeysFree())
			{
				if (Input.GetKeyDown(ModKeys.MapKey.Key)) Cycle();
				if (Input.GetKeyDown(ModKeys.PingKey.Key)) PingLook();
				if (Mode != Off && (Input.GetKeyDown(ModKeys.ZoomInKey.Key) || Input.GetKeyDown(KeyCode.KeypadPlus))) SetZoom(Zoom * 2);
				if (Mode != Off && (Input.GetKeyDown(ModKeys.ZoomOutKey.Key) || Input.GetKeyDown(KeyCode.KeypadMinus))) SetZoom(Zoom / 2);
			}
			UpdatePings();
			if (Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + 0.5f; FindIsland(me.transform.position); }
			bool show = Mode != Off && Island != null && Island.Root != null;
			if (!show) { if (panel != null) panel.gameObject.SetActive(false); return; }
			EnsureUi();
			canvas.gameObject.SetActive(true);
			panel.gameObject.SetActive(true);
			if (Mode != lastMode) { Layout(); if (Mode == Full) nextRender = 0f; lastMode = Mode; }
			if (Time.unscaledTime >= nextRender) { Render(); nextRender = Time.unscaledTime + 30f; }
			if (Time.unscaledTime >= nextMarks) { nextMarks = Time.unscaledTime + 0.5f; CollectMarks(me); }
			Draw(me);
		}

		static bool KeysFree()
		{
			if (ModKeys.Listening || NoteReader.IsOpen || PlayerNotebookWindow.IsOpen) return false;
			GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			InputField f = sel != null ? sel.GetComponent<InputField>() : null;
			return f == null || !f.isFocused;
		}

		/// <summary>The map key: corner -> full screen -> off -> corner.</summary>
		public static void Cycle() { Mode = (Mode + 1) % 3; }

		public static void SetZoom(int z) { Zoom = z >= 4 ? 4 : z >= 2 ? 2 : 1; }

		#endregion

		#region The island and its picture

		static IslandWorldState.Entry EntryOf(int id)
		{
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands) if (e.Id == id) return e;
			return null;
		}

		/// <summary>The island the player is on or next to (within its land radius + 80 m), the nearest first; null at sea.</summary>
		public static IslandWorldState.Entry IslandAt(Vector3 at)
		{
			IslandWorldState.Entry best = null;
			float bestD = float.MaxValue;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null) continue;
				Vector3 c = e.Root.transform.position;
				float d = new Vector2(at.x - c.x, at.z - c.z).magnitude;
				if (d > Mathf.Max(0f, CustomIslandSpawner.LandRadius(e.Name)) + NearIsland || d >= bestD) continue;
				best = e; bestD = d;
			}
			return best;
		}

		static void FindIsland(Vector3 at)
		{
			IslandWorldState.Entry e = IslandAt(at);
			if (e == Island) return;
			Island = e;
			islandTitle = "";
			if (e != null)
			{
				islandTitle = !string.IsNullOrEmpty(e.Label) ? e.Label : ObjectProps.Get(IslandCache.PropsOf(e), IslandProps.Title);
				if (islandTitle.Length == 0) islandTitle = e.HostName ?? "";
			}
			nextRender = 0f;
			nextMarks = 0f;
		}

		/// <summary>Takes the island's picture from above (fog off for it, so the far side isn't white).</summary>
		public static bool Render()
		{
			if (Island == null || Island.Root == null) return false;
			if (cam == null)
			{
				var go = new GameObject("CustomIslands_MapCamera");
				UnityEngine.Object.DontDestroyOnLoad(go);
				cam = go.AddComponent<Camera>();
				cam.enabled = false;
				cam.orthographic = true;
				cam.clearFlags = CameraClearFlags.SolidColor;
				cam.backgroundColor = new Color(0.12f, 0.32f, 0.42f, 1f);
				cam.cullingMask = ~(1 << 5); // not the UI
				cam.nearClipPlane = 1f;
				cam.farClipPlane = 900f;
			}
			if (tex == null) { tex = new RenderTexture(TexSize, TexSize, 16); tex.name = "CustomIslands_Map"; }
			Center = Island.Root.transform.position;
			HalfSize = Mathf.Max(40f, CustomIslandSpawner.LandRadius(Island.Name) + 25f);
			cam.orthographicSize = HalfSize;
			cam.transform.position = Center + Vector3.up * 400f;
			cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // screen up = north (+z), right = +x
			cam.targetTexture = tex;
			bool fog = RenderSettings.fog;
			RenderSettings.fog = false;
			try { cam.Render(); }
			finally { RenderSettings.fog = fog; cam.targetTexture = null; }
			if (image != null) image.texture = tex;
			RenderCount++;
			return true;
		}

		/// <summary>Where a world point is on the whole island picture (0..1 each way; outside when off the picture).</summary>
		public static Vector2 MapPoint(Vector3 world)
		{
			float s = HalfSize > 0f ? HalfSize * 2f : 1f;
			return new Vector2((world.x - Center.x) / s + 0.5f, (world.z - Center.z) / s + 0.5f);
		}

		/// <summary>The part of the picture shown: all of it at 1x, a window around the player when zoomed in.</summary>
		public static Rect View(Vector3 player)
		{
			float w = 1f / Mathf.Max(1, Zoom);
			Vector2 p = MapPoint(player);
			return new Rect(Mathf.Clamp(p.x - w / 2f, 0f, 1f - w), Mathf.Clamp(p.y - w / 2f, 0f, 1f - w), w, w);
		}

		#endregion

		#region What the map shows

		static void CollectMarks(Network_Player me)
		{
			Marks.Clear();
			Marks.Add(new Mark { Pos = me.transform.position, Kind = MarkMe, Yaw = YawOf(me) });
			foreach (Network_Player p in UnityEngine.Object.FindObjectsOfType<Network_Player>())
				if (p != null && p != me) Marks.Add(new Mark { Pos = p.transform.position, Kind = MarkPlayer, Label = PrivateStorage.NameOf(p.steamID.Id) });
			Marks.AddRange(QuestMarks(Island));
			if (Island != null)
				foreach (IslandObjectRef r in Behaviours.RefsOf(Island))
					if (r != null && r.ObjectName == MarkerName) Marks.Add(new Mark { Pos = r.transform.position, Kind = MarkMarker, Label = ObjectProps.Get(r.Props, LabelProp) });
		}

		/// <summary>The objects every unfinished quest of the island wants now: named as its current step's target.</summary>
		public static List<Mark> QuestMarks(IslandWorldState.Entry e)
		{
			var list = new List<Mark>();
			if (e == null || e.Root == null) return list;
			int count = QuestTracker.QuestsOf(e);
			List<IslandObjectRef> refs = null;
			for (int n = 0; n < count; n++)
			{
				if (QuestTracker.IsDone(e, n)) continue;
				IslandQuest q = QuestTracker.QuestOf(e, n);
				int s = QuestTracker.StepOf(e, n);
				if (!q.Exists || s < 0 || s >= q.Steps.Count) continue;
				string target = (q.Steps[s].Target ?? "").Trim();
				if (target.Length == 0) continue;
				if (refs == null) refs = Behaviours.RefsOf(e).Where(r => r != null).ToList();
				foreach (IslandObjectRef r in refs)
					if (string.Equals(r.Name, target, StringComparison.OrdinalIgnoreCase) && r.gameObject.activeInHierarchy)
						list.Add(new Mark { Pos = r.transform.position, Kind = MarkQuest, Label = q.Steps[s].Text });
			}
			return list;
		}

		static float YawOf(Network_Player me)
		{
			Camera c = Camera.main;
			return c != null ? c.transform.eulerAngles.y : me.transform.eulerAngles.y;
		}

		#endregion

		#region Drawing

		static void EnsureUi()
		{
			if (canvas != null) return;
			canvas = UIKit.CreateCanvas("CustomIslands_Minimap", 480);
			UnityEngine.Object.DontDestroyOnLoad(canvas.gameObject);
			canvas.GetComponent<GraphicRaycaster>().enabled = false; // never takes the mouse
			panel = UIKit.Rect("MapPanel", canvas.transform);
			UIKit.Background(panel.gameObject, new Color(0.08f, 0.07f, 0.06f, 0.85f), 8);
			UIKit.Border(panel, UIKit.Accent, 8, 1.5f);
			RectTransform pic = UIKit.Rect("Picture", panel);
			UIKit.Stretch(pic, 4, 4, 4, 4);
			image = pic.gameObject.AddComponent<RawImage>();
			image.texture = tex;
			image.raycastTarget = false;
			dots = UIKit.Rect("Dots", pic);
			UIKit.Stretch(dots);
			title = UIKit.Label(panel, "", 12, UIKit.TextMuted, TextAnchor.UpperCenter, FontStyle.Normal, "MapTitle");
			title.raycastTarget = false;
			lastMode = -1;
		}

		static void Layout()
		{
			if (Mode == Full) UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 700f));
			else UIKit.Anchor(panel, new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(210f, 210f));
			UIKit.Anchor(title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -20f), new Vector2(Mode == Full ? 680f : 210f, 18f));
		}

		static void Draw(Network_Player me)
		{
			Rect view = View(me.transform.position);
			image.uvRect = view;
			float size = (Mode == Full ? 700f : 210f) - 8f;
			int used = 0;
			if (Marks.Count > 0) Marks[0] = new Mark { Pos = me.transform.position, Kind = MarkMe, Yaw = YawOf(me) };
			foreach (Mark m in Marks) Place(ref used, m, view, size);
			foreach (Ping p in Pings) Place(ref used, new Mark { Pos = p.WorldPos, Kind = MarkPing, Label = p.Who }, view, size);
			for (int i = used; i < dotPool.Count; i++) dotPool[i].gameObject.SetActive(false);
			title.text = islandTitle + (Zoom > 1 ? "  " + Zoom + "x" : "");
		}

		static void Place(ref int used, Mark m, Rect view, float size)
		{
			Vector2 p = MapPoint(m.Pos);
			Vector2 local = new Vector2((p.x - view.x) / view.width, (p.y - view.y) / view.height);
			bool edge = local.x < 0f || local.x > 1f || local.y < 0f || local.y > 1f;
			if (edge && m.Kind != MarkQuest && m.Kind != MarkPing) return; // only what the player looks for stays, at the edge
			local = new Vector2(Mathf.Clamp01(local.x), Mathf.Clamp01(local.y));
			RectTransform d = Dot(used++);
			Color c = m.Kind == MarkMe ? Color.white : m.Kind == MarkPlayer ? new Color(0.4f, 0.8f, 1f) : m.Kind == MarkQuest ? UIKit.Accent : m.Kind == MarkMarker ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.35f, 0.3f);
			float s = m.Kind == MarkMe ? 12f : m.Kind == MarkPing ? 12f : 9f;
			d.anchorMin = d.anchorMax = local;
			d.anchoredPosition = Vector2.zero;
			d.sizeDelta = new Vector2(s, s);
			d.localRotation = m.Kind == MarkMe ? Quaternion.Euler(0f, 0f, -m.Yaw) : Quaternion.identity;
			d.GetComponent<Image>().color = c;
			Transform nose = d.Find("Nose");
			nose.gameObject.SetActive(m.Kind == MarkMe);
			Text t = d.Find("Name").GetComponent<Text>();
			bool named = Mode == Full || m.Kind == MarkPing;
			t.text = named && !string.IsNullOrEmpty(m.Label) ? m.Label : "";
			t.color = c;
			t.transform.localRotation = Quaternion.Inverse(d.localRotation);
			d.gameObject.SetActive(true);
		}

		static RectTransform Dot(int i)
		{
			while (dotPool.Count <= i)
			{
				RectTransform d = UIKit.Rect("Dot", dots);
				Image img = UIKit.Background(d.gameObject, Color.white, 4);
				img.raycastTarget = false;
				RectTransform nose = UIKit.Rect("Nose", d);
				UIKit.Anchor(nose, new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(4f, 9f));
				nose.pivot = new Vector2(0.5f, 0f);
				Image ni = nose.gameObject.AddComponent<Image>();
				ni.color = new Color(1f, 0.85f, 0.4f);
				ni.raycastTarget = false;
				Text t = UIKit.Label(d, "", 12, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold, "Name");
				UIKit.Anchor(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(200f, 18f));
				t.rectTransform.pivot = new Vector2(0f, 0.5f);
				t.raycastTarget = false;
				dotPool.Add(d);
			}
			return dotPool[i];
		}

		#endregion

		#region Pings

		/// <summary>The ping key: the spot the camera looks at (up to 300 m; nothing hit: 50 m ahead).</summary>
		public static bool PingLook()
		{
			Camera c = Camera.main;
			if (c == null) return false;
			Ray ray = new Ray(c.transform.position, c.transform.forward);
			RaycastHit hit;
			Vector3 at = Physics.Raycast(ray, out hit, PingRange, ~(1 << 5), QueryTriggerInteraction.Ignore) ? hit.point : ray.GetPoint(PingAhead);
			return SendPing(at);
		}

		/// <summary>Pings a world spot for the crew (at most one a second): shown here at once, sent to the host (who passes it on).</summary>
		public static bool SendPing(Vector3 at)
		{
			if (Time.unscaledTime - lastPing < PingGap) return false;
			lastPing = Time.unscaledTime;
			Network_Player me = RAPI.GetLocalPlayer();
			ulong id = me != null ? me.steamID.Id : 0UL;
			IslandNetMessage msg = MessageFor(at, id);
			Show(msg, id);
			PingsSent++;
			if (Raft_Network.IsHost) IslandNetwork.SendToEveryone(msg);
			else IslandNetwork.SendToTheHost(msg);
			return true;
		}

		/// <summary>The ping message: Ids = { island } with Offsets in the island's space, or no Ids and Offsets from the raft.</summary>
		public static IslandNetMessage MessageFor(Vector3 at, ulong from)
		{
			IslandWorldState.Entry e = IslandAt(at);
			Vector3 local;
			if (e != null && e.Root != null) local = e.Root.transform.InverseTransformPoint(at);
			else { Raft raft = ComponentManager<Raft>.Value; local = raft != null ? at - raft.transform.position : at; }
			return new IslandNetMessage
			{
				Kind = IslandNetMessage.Ping,
				Ids = e != null ? new[] { e.Id } : new int[0],
				Offsets = new[] { local.x, local.y, local.z },
				Name = "ping",
				Data = from.ToString(CultureInfo.InvariantCulture)
			};
		}

		/// <summary>A ping arrived: the host shows it and passes it to everyone; a player shows it (but not their own, already shown).</summary>
		public static void OnMessage(IslandNetMessage msg, ulong from)
		{
			if (msg == null || msg.Offsets == null || msg.Offsets.Length < 3) return;
			ulong who;
			if (!ulong.TryParse(msg.Data ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out who)) who = from;
			Network_Player me = RAPI.GetLocalPlayer();
			if (Raft_Network.IsHost)
			{
				msg.Data = who.ToString(CultureInfo.InvariantCulture); // (the sender as the host saw it)
				Show(msg, who);
				IslandNetwork.SendToEveryone(msg);
				return;
			}
			if (me != null && me.steamID.Id == who) return;
			Show(msg, who);
		}

		static void Show(IslandNetMessage msg, ulong who)
		{
			if (!LoadSceneManager.IsGameSceneLoaded) return;
			var p = new Ping
			{
				Island = msg.Ids != null && msg.Ids.Length > 0 ? msg.Ids[0] : -1,
				Local = new Vector3(msg.Offsets[0], msg.Offsets[1], msg.Offsets[2]),
				From = who,
				Until = Time.unscaledTime + PingSeconds
			};
			if (p.Island >= 0 && EntryOf(p.Island) == null) return; // (an island this player doesn't have)
			string name = "";
			try { name = PrivateStorage.NameOf(who); } catch { }
			p.Who = string.IsNullOrEmpty(name) ? "Ping" : name;
			Pings.RemoveAll(x => { if (x.From == who) { Drop(x); return true; } return false; }); // one ping per player
			p.World = Beam(p.WorldPos, p.Who);
			Pings.Add(p);
			PingsShown++;
			SoundLibrary.PlayOnce(PingSound, p.WorldPos, 0.8f);
		}

		static GameObject Beam(Vector3 at, string who)
		{
			var go = new GameObject("CustomIslands_Ping");
			go.transform.position = at;
			LineRenderer lr = go.AddComponent<LineRenderer>();
			Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
			if (sh != null) lr.material = new Material(sh);
			lr.useWorldSpace = false;
			lr.positionCount = 2;
			lr.SetPosition(0, Vector3.zero);
			lr.SetPosition(1, Vector3.up * 25f);
			lr.startWidth = 0.35f; lr.endWidth = 0.05f;
			lr.startColor = new Color(1f, 0.35f, 0.3f, 0.95f); lr.endColor = new Color(1f, 0.35f, 0.3f, 0f);
			lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			var label = new GameObject("Name");
			label.transform.SetParent(go.transform, false);
			label.transform.localPosition = Vector3.up * 3f;
			TextMesh tm = label.AddComponent<TextMesh>();
			tm.font = UIKit.Font;
			tm.fontSize = 48;
			tm.characterSize = 0.06f;
			tm.anchor = TextAnchor.LowerCenter;
			tm.alignment = TextAlignment.Center;
			tm.color = new Color(1f, 0.85f, 0.8f);
			tm.text = who;
			MeshRenderer mr = label.GetComponent<MeshRenderer>();
			if (mr != null && UIKit.Font != null) mr.material = UIKit.Font.material;
			return go;
		}

		static void UpdatePings()
		{
			if (Pings.Count == 0) return;
			Camera c = Camera.main;
			for (int i = Pings.Count - 1; i >= 0; i--)
			{
				Ping p = Pings[i];
				if (Time.unscaledTime >= p.Until) { Drop(p); Pings.RemoveAt(i); continue; }
				if (p.World == null) continue;
				p.World.transform.position = p.WorldPos; // (follows its island or the raft)
				if (c != null)
				{
					Transform label = p.World.transform.Find("Name");
					float d = Vector3.Distance(c.transform.position, label.position);
					label.rotation = Quaternion.LookRotation(label.position - c.transform.position);
					label.localScale = Vector3.one * Mathf.Clamp(d / 15f, 1f, 12f); // readable from afar
				}
			}
		}

		static void Drop(Ping p) { if (p.World != null) UnityEngine.Object.Destroy(p.World); }

		public static void ClearPings() { foreach (Ping p in Pings) Drop(p); Pings.Clear(); lastPing = -10f; }

		#endregion

		#region Map markers (the builder's own)

		public static bool IsMarker(string name) { return name == MarkerName; }

		/// <summary>The object list's entry (ContentCatalog.Register).</summary>
		public static void Register()
		{
			PlaceableCatalog.AddCustom(MarkerName, BuildMarker(), ContentCatalog.ZoneCategory, "Map marker (island map)");
		}

		static GameObject BuildMarker()
		{
			var root = new GameObject(MarkerName);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			Material body = ContentCatalog.MarkerMaterial(new Color(0.4f, 0.9f, 0.4f));
			Material dark = ContentCatalog.MarkerMaterial(new Color(0.2f, 0.2f, 0.22f));
			ContentCatalog.Part(PrimitiveType.Cube, "Flag", root.transform, new Vector3(0.22f, 0.75f, 0f), Quaternion.identity, new Vector3(0.45f, 0.3f, 0.04f), body);
			ContentCatalog.Part(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(0.05f, 0.5f, 0.05f), dark);
			ContentCatalog.AddLabel(root.transform, "Map marker", 1.1f);
			return root;
		}

		#endregion
	}

	public static partial class ObjectInspector
	{
		/// <summary>A map marker (PF5): the text by its dot on the island map.</summary>
		static void MapMarkerGroup(EditorGameObject target)
		{
			RectTransform g = UIKit.Group(root, "Map marker (island map)");
			UIKit.Label(g, "Players see a green dot here on the island map (map key), with this text when the map is full screen. Quest targets show by themselves.", 12, UIKit.TextMuted);
			InputField lf = SurvivorField(g, "Text", ObjectProps.Get(target.Props, Minimap.LabelProp), 26f, "What the map says by the dot (empty = only the dot)");
			lf.onEndEdit.AddListener(v => Set(target, Minimap.LabelProp, v.Trim().Length > 0 ? v.Trim() : null));
			amountFields.Add(lf);
		}
	}
}
