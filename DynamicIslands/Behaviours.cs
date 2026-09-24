using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Behaviours and events of island objects (object settings, saved with the island):
	///   obj.name                        a name that actions refer to (several objects may share one: they act together)
	///   beh.spin                        turns around its up axis (degrees per second)
	///   beh.bob / beh.bobTime           floats up and down (metres, seconds per cycle)
	///   beh.move (x,y,z) / beh.turn / beh.moveTime / beh.moveMode
	///                                   moves by an offset and turns (degrees) over a time: "loop" = back and forth for
	///                                   ever, "switch" = a door, gate or lift that actions (or using it) open and close
	///   beh.hidden = 1                  not there at first; a "show" action makes it appear (creatures: an ambush)
	///   beh.use                         players can use it (interact key); the text is the hint ("Pull the lever")
	///   col.mode                        collision: "" = Raft's own, "none" = walk through, "box" = one box around it,
	///                                   "solid" = a box only if it has no collider of its own
	///   on.&lt;event&gt;                     actions, one per line "verb|target|argument", when: use (it is used), enter (a trigger
	///                                   zone fires), read (a note is read the first time), open (a chest is opened),
	///                                   defeat (all the animals of a creature spot are defeated). The island has
	///                                   on.arrive (players first come to it) and on.quest (its quest is done).
	/// Verbs: show, hide, toggle (whether objects are there), open, close, switch (movers), message, give (items),
	/// sound (one of Raft's sounds), teleport (the player to an object), signal (world plan and island rules can wait
	/// for it: "signal:&lt;island&gt;:&lt;name&gt;").
	/// </summary>
	public static class BehaviourProps
	{
		public const string Name = "obj.name", Spin = "beh.spin", Bob = "beh.bob", BobTime = "beh.bobTime", Move = "beh.move", Turn = "beh.turn",
			MoveTime = "beh.moveTime", MoveMode = "beh.moveMode", Hidden = "beh.hidden", Use = "beh.use", Collision = "col.mode";
		public const string EventPrefix = "on.";
		public static readonly string[] ObjectEvents = { "use", "enter", "read", "open", "defeat" };
		public static readonly string[] IslandEvents = { "arrive", "quest" };
		public static readonly string[] Verbs = { "show", "hide", "toggle", "open", "close", "switch", "message", "give", "sound", "teleport", "signal" };
		public static readonly string[] SharedVerbs = { "show", "hide", "toggle", "open", "close", "switch", "signal" };
		public static readonly string[] CollisionModes = { "", "none", "box", "solid" };

		public static string EventKey(string ev) { return EventPrefix + ev; }

		public static Vector3 Offset(IDictionary<string, string> p)
		{
			string[] v = ObjectProps.Get(p, Move).Split(',');
			float x, y, z;
			if (v.Length == 3 && float.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
				float.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return new Vector3(x, y, z);
			return Vector3.zero;
		}

		public static string OffsetText(Vector3 v)
		{
			return string.Join(",", new[] { v.x, v.y, v.z }.Select(f => f.ToString("0.##", CultureInfo.InvariantCulture)).ToArray());
		}

		public static bool Moves(IDictionary<string, string> p) { return Offset(p) != Vector3.zero || ObjectProps.GetFloat(p, Turn, 0f) != 0f; }
		public static bool Switches(IDictionary<string, string> p) { return Moves(p) && ObjectProps.Get(p, MoveMode) != "loop"; }
		public static bool StartsHidden(IDictionary<string, string> p) { return ObjectProps.GetBool(p, Hidden, false); }

		/// <summary>True when the object has anything this file handles (so it gets a behaviour in a world).</summary>
		public static bool Any(IDictionary<string, string> p)
		{
			return p != null && p.Keys.Any(k => k.StartsWith("beh.") || k.StartsWith("col.") || k.StartsWith(EventPrefix) || k == Name);
		}

		/// <summary>What the events a kind of object can have are: (event, what the builder reads).</summary>
		public static List<KeyValuePair<string, string>> EventsFor(string objectName, IDictionary<string, string> p)
		{
			var list = new List<KeyValuePair<string, string>>();
			if (ContentCatalog.IsHelper(objectName)) return list;
			if (objectName == ContentCatalog.TriggerZone) list.Add(new KeyValuePair<string, string>("enter", "a player walks into the zone"));
			else if (ContentCatalog.IsCreature(objectName)) list.Add(new KeyValuePair<string, string>("defeat", "all its animals are defeated"));
			else if (!ContentCatalog.IsZone(objectName))
			{
				if (ObjectProps.IsNote(objectName, p)) list.Add(new KeyValuePair<string, string>("read", "it is read (the first time)"));
				if (ObjectProps.IsLoot(objectName, p)) list.Add(new KeyValuePair<string, string>("open", "it is opened"));
				if (!ObjectProps.IsNote(objectName, p) && !ObjectProps.IsLoot(objectName, p)) list.Add(new KeyValuePair<string, string>("use", "a player uses it (" + ObjectProps.Get(p, Use, "needs \"Players can use it\"") + ")"));
			}
			return list;
		}
	}

	/// <summary>One action: a verb, a target (object name) and an argument (text, items, sound...).</summary>
	public class ObjAction
	{
		public string Verb = "show", Target = "", Arg = "";

		public static ObjAction Parse(string line)
		{
			string[] p = (line ?? "").Split('|');
			if (p.Length < 1 || !BehaviourProps.Verbs.Contains(p[0].Trim())) return null;
			return new ObjAction { Verb = p[0].Trim(), Target = p.Length > 1 ? p[1].Trim() : "", Arg = p.Length > 2 ? string.Join("|", p.Skip(2).ToArray()).Trim() : "" };
		}

		public static List<ObjAction> ParseLines(string text) { return (text ?? "").Split('\n').Select(Parse).Where(a => a != null).ToList(); }

		public static string ToLines(IEnumerable<ObjAction> actions)
		{
			return string.Join("\n", actions.Select(a => a.Verb + "|" + Clean(a.Target) + "|" + (a.Arg ?? "").Replace("\n", " ").Trim()).ToArray());
		}

		static string Clean(string s) { return (s ?? "").Replace("|", "/").Replace("\n", " ").Trim(); }

		public bool Shared { get { return BehaviourProps.SharedVerbs.Contains(Verb); } }

		/// <summary>Needs a target object (by name).</summary>
		public static bool HasTarget(string verb) { return verb == "show" || verb == "hide" || verb == "toggle" || verb == "open" || verb == "close" || verb == "switch" || verb == "teleport"; }
		public static bool HasArg(string verb) { return verb == "message" || verb == "give" || verb == "sound" || verb == "signal"; }

		public string Describe()
		{
			string t = Target.Length > 0 ? "'" + Target + "'" : "itself";
			switch (Verb)
			{
				case "show": return "show " + t;
				case "hide": return "hide " + t;
				case "toggle": return "show or hide " + t;
				case "open": return "open " + t;
				case "close": return "close " + t;
				case "switch": return "open or close " + t;
				case "message": return "say \"" + Arg + "\"";
				case "give": return "give " + string.Join(", ", ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, Arg } }).Select(l => ContentCatalog.ItemLabel(l.Key) + " \u00D7" + l.Value).ToArray());
				case "sound": return "play " + (Arg.Length > 0 ? Arg.Substring(Arg.LastIndexOf('/') + 1) : "a sound");
				case "teleport": return "move the player to " + t;
				case "signal": return "send the signal '" + Arg + "' (world plans can wait for it)";
			}
			return Verb;
		}
	}

	/// <summary>An island object with settings, in a world: its place in the island file (the same on every machine) and settings.</summary>
	public class IslandObjectRef : MonoBehaviour
	{
		public int Index;
		public string ObjectName = "";
		public Dictionary<string, string> Props = new Dictionary<string, string>();
		public string Name { get { return ObjectProps.Get(Props, BehaviourProps.Name); } }
	}

	/// <summary>
	/// An object's movement in a world: spin, bob, and a mover (door, gate, lift) between its placed pose and the pose
	/// moved by an offset and turned. A switch mover goes where its shared state says; a loop mover goes back and
	/// forth by itself. Every machine animates its own copy.
	/// </summary>
	public class IslandBehaviour : MonoBehaviour
	{
		public float SpinSpeed, BobHeight, BobTime = 3f, MoveTime = 2f;
		public bool Loop;
		public Vector3 Offset;
		public float TurnDegrees;
		/// <summary>0 = closed (placed pose), 1 = open.</summary>
		public float Target, Current;
		Vector3 startPos;
		Quaternion startRot;
		float phase;

		public void Configure(IDictionary<string, string> p)
		{
			SpinSpeed = ObjectProps.GetFloat(p, BehaviourProps.Spin, 0f);
			BobHeight = ObjectProps.GetFloat(p, BehaviourProps.Bob, 0f);
			BobTime = Mathf.Max(0.2f, ObjectProps.GetFloat(p, BehaviourProps.BobTime, 3f));
			Offset = BehaviourProps.Offset(p);
			TurnDegrees = ObjectProps.GetFloat(p, BehaviourProps.Turn, 0f);
			MoveTime = Mathf.Max(0.1f, ObjectProps.GetFloat(p, BehaviourProps.MoveTime, 2f));
			Loop = ObjectProps.Get(p, BehaviourProps.MoveMode) == "loop";
			startPos = transform.localPosition;
			startRot = transform.localRotation;
			phase = UnityEngine.Random.value * 10f;
		}

		/// <summary>Jumps to the open or closed pose (an island loading with its saved state).</summary>
		public void Snap(bool open) { Target = Current = open ? 1f : 0f; Pose(); }

		bool Animates { get { return SpinSpeed != 0f || BobHeight != 0f || Offset != Vector3.zero || TurnDegrees != 0f; } }

		void Update()
		{
			if (!Animates) return;
			float t = Time.time + phase;
			if (Loop) Current = Mathf.PingPong(t / MoveTime, 1f);
			else if (Current != Target) Current = Mathf.MoveTowards(Current, Target, Time.deltaTime / MoveTime);
			Pose();
		}

		void Pose()
		{
			float s = Mathf.SmoothStep(0f, 1f, Current);
			Transform parent = transform.parent;
			// The offset is in world directions (as the builder sees them); the island root isn't turned, so local = world
			Vector3 pos = startPos + Offset * s;
			if (BobHeight != 0f) pos += Vector3.up * BobHeight * Mathf.Sin((Time.time + phase) * Mathf.PI * 2f / BobTime);
			Quaternion rot = Quaternion.Euler(0f, TurnDegrees * s + SpinSpeed * (Time.time + phase), 0f) * startRot;
			transform.localPosition = pos;
			transform.localRotation = rot;
		}
	}

	/// <summary>"Players can use it": Raft's interact hint on the object, and the use event when the key is pressed.</summary>
	public class UseInteract : MonoBehaviour, IRaycastable
	{
		public string Hint = "Use";
		public IslandObjectRef Ref;
		float cooldown;

		void IRaycastable.OnIsRayed()
		{
			if (NoteReader.IsOpen) return;
			DisplayTextManager hints = CustomNote.Hints;
			if (hints != null) hints.ShowText(Hint, CustomNote.InteractKey, 0, 0, true);
			if (CustomNote.InteractPressed() && Time.time > cooldown)
			{
				cooldown = Time.time + 0.5f;
				if (hints != null) hints.HideDisplayTexts();
				Behaviours.Fire(ContentState.EntryOf(transform), Ref != null ? Ref.Index : -1, "use", true);
			}
		}

		void IRaycastable.OnRayEnter() { }

		void IRaycastable.OnRayExit()
		{
			DisplayTextManager hints = CustomNote.Hints;
			if (hints != null) hints.HideDisplayTexts();
		}
	}

	/// <summary>
	/// Events and actions in a world, and the shared state they change (which objects are there, which movers are
	/// open), kept in the island's state (saved with the world, sent to players who join) under 0x60000 + object.
	/// Multiplayer: the machine where something happens does the personal actions (a message, items, a sound, a
	/// teleport) for its own player; the host does the shared ones and tells everyone (IslandNetMessage.ObjectSet).
	/// A client asks the host with IslandNetMessage.EventFired. Events the host notices itself (defeated animals) are
	/// passed on so every player near the island gets the personal part.
	/// </summary>
	public static class Behaviours
	{
		public const int StateBase = 0x60000, DoneBase = 0x70000, SignalBase = 0x7F000, IslandIndex = 0xFFFF;
		const float NearDistance = 120f;

		/// <summary>Raised on every machine when an event's actions run here (tests listen): island id, object index (-1 = the island), event.</summary>
		public static event Action<int, int, string> Fired;
		/// <summary>The last message an action showed here (tests).</summary>
		public static string LastMessage { get; private set; }

		static int Today { get { try { return WorldManager.DayCounter; } catch { return 0; } } }

		/// <summary>The key a signal is kept under in the island's state.</summary>
		public static int SignalKey(string name)
		{
			int h = 17;
			foreach (char c in (name ?? "").Trim().ToLowerInvariant()) h = h * 31 + c;
			return SignalBase + (h & 0xFFF);
		}

		#region Spawning (IslandSpawner)

		/// <summary>A spawned object with settings: remembers its place and settings, and gets its behaviours (world only).</summary>
		public static void Attach(GameObject go, string objectName, IDictionary<string, string> props, int index)
		{
			if (go == null || props == null || props.Count == 0) return;
			IslandObjectRef r = go.AddComponent<IslandObjectRef>();
			r.Index = index;
			r.ObjectName = objectName;
			r.Props = new Dictionary<string, string>(props);
			if (!BehaviourProps.Any(props)) return;
			if (ObjectProps.GetFloat(props, BehaviourProps.Spin, 0f) != 0f || ObjectProps.GetFloat(props, BehaviourProps.Bob, 0f) != 0f || BehaviourProps.Moves(props))
				go.AddComponent<IslandBehaviour>().Configure(props);
			if (ObjectProps.Get(props, BehaviourProps.Use).Length > 0 && !ObjectProps.IsNote(objectName, props) && !ObjectProps.IsLoot(objectName, props) && !ContentCatalog.IsZone(objectName) && !ContentCatalog.IsCreature(objectName))
			{
				UseInteract u = CustomNote.InteractHolder(go).AddComponent<UseInteract>();
				u.Hint = ObjectProps.Get(props, BehaviourProps.Use);
				u.Ref = r;
			}
			ApplyCollision(go, ObjectProps.Get(props, BehaviourProps.Collision));
			if (BehaviourProps.StartsHidden(props)) go.SetActive(false);
		}

		/// <summary>Collision as the builder chose it (the interaction box of notes, chests and usable objects is left alone).</summary>
		public static void ApplyCollision(GameObject go, string mode)
		{
			if (string.IsNullOrEmpty(mode)) return;
			List<Collider> own = go.GetComponentsInChildren<Collider>(true).Where(c => !UnderHolder(c.transform, go.transform)).ToList();
			if (mode == "none") { foreach (Collider c in own) c.enabled = false; return; }
			if (mode == "solid" && own.Any(c => c.enabled && !c.isTrigger)) return;
			if (mode == "box") foreach (Collider c in own) c.enabled = false;
			Bounds b;
			if (!CustomNote.LocalBounds(go, out b)) return;
			var solid = new GameObject("CI_Solid");
			solid.transform.SetParent(go.transform, false);
			solid.layer = IslandSpawner.TerrainLayer;
			var box = solid.AddComponent<BoxCollider>();
			box.center = b.center;
			box.size = b.size;
		}

		static bool UnderHolder(Transform t, Transform top)
		{
			for (; t != null && t != top; t = t.parent) if (t.name == CustomNote.HolderName) return true;
			return false;
		}

		/// <summary>An island loaded (every machine): objects take their saved state before anything else looks at them.</summary>
		public static void OnIslandReady(IslandWorldState.Entry e)
		{
			if (e == null || e.Root == null) return;
			foreach (IslandObjectRef r in e.Root.GetComponentsInChildren<IslandObjectRef>(true))
			{
				bool visible, open;
				StateOf(e, r, out visible, out open);
				if (r.gameObject.activeSelf != visible) r.gameObject.SetActive(visible);
				IslandBehaviour b = r.GetComponent<IslandBehaviour>();
				if (b != null && !b.Loop) b.Snap(open);
			}
			arrived.Remove(e.Id);
		}

		#endregion

		#region State

		static void StateOf(IslandWorldState.Entry e, IslandObjectRef r, out bool visible, out bool open)
		{
			ObjectState s;
			if (e.State.TryGetValue(StateBase + r.Index, out s)) { visible = s.Active; open = s.Yield == 1; return; }
			visible = !BehaviourProps.StartsHidden(r.Props);
			open = false;
		}

		static void Apply(IslandWorldState.Entry e, int index, bool visible, bool open)
		{
			IslandObjectRef def = RefsOf(e).FirstOrDefault(r => r.Index == index);
			bool defaultVisible = def == null || !BehaviourProps.StartsHidden(def.Props);
			if (visible == defaultVisible && !open) e.State.Remove(StateBase + index);
			else e.State[StateBase + index] = new ObjectState { Active = visible, Yield = open ? 1 : 0, Day = Today };
			if (def == null) return;
			if (def.gameObject.activeSelf != visible) def.gameObject.SetActive(visible);
			IslandBehaviour b = def.GetComponent<IslandBehaviour>();
			if (b != null) b.Target = open ? 1f : 0f;
		}

		/// <summary>From the network (the host changed an object).</summary>
		public static void ApplyRemote(int islandId, int index, int bits)
		{
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == islandId);
			if (e != null) Apply(e, index, (bits & 1) != 0, (bits & 2) != 0);
		}

		static IEnumerable<IslandObjectRef> RefsOf(IslandWorldState.Entry e)
		{
			return e != null && e.Root != null ? e.Root.GetComponentsInChildren<IslandObjectRef>(true) : Enumerable.Empty<IslandObjectRef>();
		}

		#endregion

		#region Events

		static Dictionary<string, string> PropsOf(IslandWorldState.Entry e, int index)
		{
			if (index < 0 || index == IslandIndex) return IslandCache.PropsOf(e);
			IslandObjectRef r = RefsOf(e).FirstOrDefault(x => x.Index == index);
			return r != null ? r.Props : new Dictionary<string, string>();
		}

		public static List<ObjAction> ActionsOf(IslandWorldState.Entry e, int index, string ev)
		{
			Dictionary<string, string> p = PropsOf(e, index);
			List<ObjAction> list = ObjAction.ParseLines(ObjectProps.Get(p, BehaviourProps.EventKey(ev)));
			// A door or lever that is used without actions of its own opens and closes itself
			if (list.Count == 0 && ev == "use" && BehaviourProps.Switches(p)) list.Add(new ObjAction { Verb = "switch" });
			return list;
		}

		/// <summary>
		/// Something happened to an island object (index; -1 = the island itself). localPlayer: this machine's player
		/// did it (gets the personal actions). Shared actions run on the host (a client asks it).
		/// </summary>
		public static void Fire(IslandWorldState.Entry e, int index, string ev, bool localPlayer)
		{
			if (e == null) return;
			if (index < 0) index = IslandIndex;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			if (actions.Count == 0) return;
			// Reading a note counts once per world
			if (ev == "read" || ev == "arrive")
			{
				int key = DoneBase + index;
				if (e.State.ContainsKey(key)) { if (localPlayer) RunPersonal(e, index, actions, ev == "arrive"); return; }
				e.State[key] = new ObjectState { Active = false, Day = Today };
				IslandNetwork.SendUsed(e.Id, key, Today);
			}
			if (localPlayer) RunPersonal(e, index, actions, false);
			if (actions.Any(a => a.Shared))
			{
				if (Raft_Network.IsHost) RunShared(e, index, actions);
				else IslandNetwork.SendEvent(e.Id, index, ev, false);
			}
			if (Fired != null) try { Fired(e.Id, index, ev); } catch { }
		}

		/// <summary>The host noticed something no single player did (animals defeated): everyone near the island gets the personal part.</summary>
		public static void FireFromHost(IslandWorldState.Entry e, int index, string ev)
		{
			if (e == null || !Raft_Network.IsHost) return;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			if (actions.Count == 0) return;
			if (Near(e)) RunPersonal(e, index, actions, false);
			RunShared(e, index, actions);
			IslandNetwork.SendEvent(e.Id, index, ev, true);
			if (Fired != null) try { Fired(e.Id, index, ev); } catch { }
		}

		/// <summary>An event that happens on every machine by itself (a quest done): personal part here if near, shared part on the host.</summary>
		public static void FireEverywhere(IslandWorldState.Entry e, int index, string ev)
		{
			if (e == null) return;
			if (index < 0) index = IslandIndex;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			if (actions.Count == 0) return;
			if (Near(e)) RunPersonal(e, index, actions, false);
			if (Raft_Network.IsHost) RunShared(e, index, actions);
			if (Fired != null) try { Fired(e.Id, index, ev); } catch { }
		}

		/// <summary>From the network: a client's event (host: do the shared part), or the host's (client: the personal part if near).</summary>
		public static void OnEventMessage(int islandId, int index, string ev, bool fromHost)
		{
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == islandId);
			if (e == null) return;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			if (Raft_Network.IsHost && !fromHost) RunShared(e, index, actions);
			else if (fromHost && Near(e)) RunPersonal(e, index, actions, false);
		}

		static void RunShared(IslandWorldState.Entry e, int index, List<ObjAction> actions)
		{
			bool creaturesShown = false;
			foreach (ObjAction a in actions.Where(x => x.Shared))
			{
				if (a.Verb == "signal")
				{
					e.State[SignalKey(a.Arg)] = new ObjectState { Active = true, Day = Today };
					Debug.Log("[CUSTOM ISLANDS] Signal '" + a.Arg + "' on '" + e.HostName + "'");
					continue;
				}
				foreach (IslandObjectRef r in Targets(e, index, a.Target))
				{
					bool visible, open;
					StateOf(e, r, out visible, out open);
					switch (a.Verb)
					{
						case "show": visible = true; break;
						case "hide": visible = false; break;
						case "toggle": visible = !visible; break;
						case "open": open = true; break;
						case "close": open = false; break;
						case "switch": open = !open; break;
					}
					Apply(e, r.Index, visible, open);
					IslandNetwork.SendObjectSet(e.Id, r.Index, (visible ? 1 : 0) | (open ? 2 : 0));
					if (visible && r.GetComponent<CreatureSpawnPoint>() != null) creaturesShown = true;
				}
			}
			if (creaturesShown) CreatureSpawner.OnIslandReady(e); // hidden animals appear: an ambush
		}

		static void RunPersonal(IslandWorldState.Entry e, int index, List<ObjAction> actions, bool messagesOnly)
		{
			foreach (ObjAction a in actions.Where(x => !x.Shared))
			{
				try
				{
					switch (a.Verb)
					{
						case "message":
							LastMessage = a.Arg;
							IslandInfo.ShowMessage(a.Arg);
							break;
						case "give":
							if (messagesOnly) break;
							TriggerZone.Give(ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, a.Arg } }));
							break;
						case "sound":
							IslandObjectRef src = RefsOf(e).FirstOrDefault(r => r.Index == index);
							SoundLibrary.PlayOnce(a.Arg, src != null ? src.transform.position : (RAPI.GetLocalPlayer() != null ? RAPI.GetLocalPlayer().transform.position : Vector3.zero), 0.9f);
							break;
						case "teleport":
							if (messagesOnly) break;
							IslandObjectRef to = Targets(e, index, a.Target).FirstOrDefault(r => r.gameObject.activeInHierarchy);
							if (to != null) Teleport(to.transform.position + Vector3.up * 1.2f);
							break;
					}
				}
				catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Action '" + a.Verb + "': " + ex.Message); }
			}
		}

		/// <summary>The objects an action works on: those with the name, or the object itself when there's no name.</summary>
		static List<IslandObjectRef> Targets(IslandWorldState.Entry e, int index, string target)
		{
			target = (target ?? "").Trim();
			if (target.Length == 0 || target.Equals("self", StringComparison.OrdinalIgnoreCase)) return RefsOf(e).Where(r => r.Index == index).ToList();
			return RefsOf(e).Where(r => r.Name.Equals(target, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		public static void Teleport(Vector3 to)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) return;
			CharacterController cc = player.PersonController != null ? player.PersonController.controller : null;
			if (cc != null) cc.enabled = false;
			player.transform.position = to;
			if (player.PersonController != null) player.PersonController.SwitchControllerType(ControllerType.Ground);
			if (cc != null) cc.enabled = true;
		}

		static bool Near(IslandWorldState.Entry e)
		{
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null) return false;
			float r = Mathf.Max(0f, CustomIslandSpawner.LandRadius(e.Name));
			return new Vector2(p.transform.position.x - e.Position.x, p.transform.position.z - e.Position.z).magnitude < r + NearDistance;
		}

		#endregion

		#region Island events (arrive, quest)

		static readonly HashSet<int> arrived = new HashSet<int>();
		static float nextTick;

		/// <summary>Every frame from the mod: players arriving at islands with "on.arrive" actions (each machine its own player).</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextTick) return;
			nextTick = Time.unscaledTime + 0.5f;
			if (!LoadSceneManager.IsGameSceneLoaded) { arrived.Clear(); return; }
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null) return;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null || arrived.Contains(e.Id)) continue;
				if (!IslandCache.PropsOf(e).ContainsKey(BehaviourProps.EventKey("arrive"))) { arrived.Add(e.Id); continue; }
				float r = Mathf.Max(0f, CustomIslandSpawner.LandRadius(e.Name));
				if (new Vector2(p.transform.position.x - e.Position.x, p.transform.position.z - e.Position.z).magnitude > r + 30f) continue;
				arrived.Add(e.Id);
				Fire(e, -1, "arrive", true);
			}
		}

		/// <summary>Hooked to QuestTracker.Advanced (every machine): the island's quest is done.</summary>
		public static void OnQuestAdvanced(int islandId, int step)
		{
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == islandId);
			if (e == null) return;
			if (step >= QuestTracker.QuestOf(e).Steps.Count) FireEverywhere(e, -1, "quest");
		}

		#endregion
	}
}
