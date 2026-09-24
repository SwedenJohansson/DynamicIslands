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
	///   if.&lt;event&gt;                     checks that must all pass before the actions run, one per line "kind|target|argument":
	///                                   has (the player has items; "story:&lt;id&gt;" = a story item of the crew), take (has
	///                                   them, and they are used up), state (objects with the name are open / closed / shown
	///                                   / hidden), signal (was sent on this island), quest (the island's quest reached a step).
	///                                   "!kind|..." = NOT so; a line "any" = one passing check is enough. A chest checks
	///                                   "if.open" before it gives its loot (a locked chest)
	///   else.&lt;event&gt;                   actions when a check fails (usually a message: "It's locked.")
	/// Verbs: show, hide, toggle (whether objects are there), open, close, switch (movers), message, give (items, story
	/// items too), sound (one of Raft's sounds), teleport (the player to an object), signal (world plan and island rules can
	/// wait for it: "signal:&lt;island&gt;:&lt;name&gt;"), journal (a page in the crew's journal), wait (the actions after it
	/// run that many seconds later).
	/// </summary>
	public static class BehaviourProps
	{
		public const string Name = "obj.name", Spin = "beh.spin", Bob = "beh.bob", BobTime = "beh.bobTime", Move = "beh.move", Turn = "beh.turn",
			MoveTime = "beh.moveTime", MoveMode = "beh.moveMode", Hidden = "beh.hidden", Use = "beh.use", Collision = "col.mode";
		public const string EventPrefix = "on.";
		public static readonly string[] ObjectEvents = { "use", "enter", "read", "open", "defeat" };
		public static readonly string[] IslandEvents = { "arrive", "quest" };
		public static readonly string[] Verbs = { "show", "hide", "toggle", "open", "close", "switch", "message", "give", "sound", "teleport", "signal", "journal", "wait" };
		public static readonly string[] SharedVerbs = { "show", "hide", "toggle", "open", "close", "switch", "signal", "journal" };
		public const string CheckPrefix = "if.", ElsePrefix = "else.";
		public static readonly string[] CheckKinds = { "has", "take", "state", "signal", "quest" };
		public static readonly string[] States = { "open", "closed", "shown", "hidden" };
		public static readonly string[] CollisionModes = { "", "none", "box", "solid" };

		/// <summary>The setting with an event's actions; "use!" = the actions when its checks fail.</summary>
		public static string EventKey(string ev) { return ev.EndsWith("!") ? ElsePrefix + ev.TrimEnd('!') : EventPrefix + ev; }
		public static string CheckKey(string ev) { return CheckPrefix + ev.TrimEnd('!'); }
		public static string ElseKey(string ev) { return ElsePrefix + ev.TrimEnd('!'); }

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
			return p != null && p.Keys.Any(k => k.StartsWith("beh.") || k.StartsWith("col.") || k.StartsWith(EventPrefix) || k.StartsWith(CheckPrefix) || k.StartsWith(ElsePrefix) || k == Name);
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
				if (ObjectProps.IsLoot(objectName, p)) list.Add(new KeyValuePair<string, string>("open", "it is opened (with checks: its loot stays locked until they pass)"));
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
			var a = new ObjAction { Verb = p[0].Trim(), Target = p.Length > 1 ? p[1].Trim() : "", Arg = p.Length > 2 ? string.Join("|", p.Skip(2).ToArray()).Trim() : "" };
			if (a.Verb == "wait" && a.Arg.Length == 0) { a.Arg = a.Target; a.Target = ""; } // "wait|5" as well as "wait||5"
			return a;
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
		public static bool HasArg(string verb) { return verb == "message" || verb == "give" || verb == "sound" || verb == "signal" || verb == "journal" || verb == "wait"; }

		/// <summary>A wait's seconds (0..3600).</summary>
		public float Seconds
		{
			get
			{
				float s;
				return Verb == "wait" && float.TryParse(Arg, NumberStyles.Float, CultureInfo.InvariantCulture, out s) ? Mathf.Clamp(s, 0f, 3600f) : 0f;
			}
		}

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
				case "journal": return "write \"" + (Target.Length > 0 ? Target : "a page") + "\" in the journal";
				case "wait": return "wait " + Seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s, then...";
			}
			return Verb;
		}
	}

	/// <summary>
	/// One check before an event's actions run: a kind and what it looks at.
	///   has|&lt;item&gt;|&lt;count&gt;     the player has items (Raft's unique item name, or story:&lt;id&gt; for a story item of the crew)
	///   take|&lt;item&gt;|&lt;count&gt;    the same, and they are used up when every check passes
	///   state|&lt;name&gt;|open        objects with that name are open / closed / shown / hidden
	///   signal|&lt;name&gt;            the signal was sent on this island
	///   quest|&lt;steps&gt;            the island's quest has that many steps done ("done" = all of them)
	/// </summary>
	public class ObjCheck
	{
		public string Kind = "take", Target = "", Arg = "";
		/// <summary>The check passes when what it looks for is NOT so ("!has|story:key|1": the player hasn't got the key).</summary>
		public bool Not;
		/// <summary>A line "any" among the checks: one passing check is enough (otherwise all must pass).</summary>
		public const string AnyLine = "any";

		public static ObjCheck Parse(string line)
		{
			string[] p = (line ?? "").Split('|');
			string kind = p[0].Trim();
			bool not = kind.StartsWith("!");
			kind = kind.TrimStart('!').Trim();
			if (p.Length < 2 || !BehaviourProps.CheckKinds.Contains(kind)) return null;
			return new ObjCheck { Kind = kind, Not = not, Target = p[1].Trim(), Arg = p.Length > 2 ? p[2].Trim() : "" };
		}

		public static List<ObjCheck> ParseLines(string text) { return (text ?? "").Split('\n').Select(Parse).Where(c => c != null).ToList(); }

		/// <summary>Whether the checks are "any of" (one passing is enough).</summary>
		public static bool IsAny(string text) { return (text ?? "").Split('\n').Any(l => l.Trim().Equals(AnyLine, StringComparison.OrdinalIgnoreCase)); }

		public static string ToLines(IEnumerable<ObjCheck> checks, bool any = false)
		{
			return (any ? AnyLine + "\n" : "") + string.Join("\n", checks.Select(c => (c.Not ? "!" : "") + c.Kind + "|" + (c.Target ?? "").Replace("|", "/").Trim() + "|" + (c.Arg ?? "").Replace("|", "/").Trim()).ToArray());
		}

		/// <summary>How many items it wants (at least 1).</summary>
		public int Count
		{
			get
			{
				int n;
				return int.TryParse(Arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0 ? n : 1;
			}
		}

		public bool IsItem { get { return Kind == "has" || Kind == "take"; } }

		public string Describe() { return (Not ? "NOT: " : "") + What(); }

		string What()
		{
			switch (Kind)
			{
				case "has": return "the player has " + ItemText();
				case "take": return "the player has " + ItemText() + (Not ? "" : " (used up)");
				case "state": return "'" + Target + "' is " + (BehaviourProps.States.Contains(Arg) ? Arg : "open");
				case "signal": return "the signal '" + Target + "' was sent";
				case "quest": return Target == "done" || Target.Length == 0 ? "the island's quest is done" : "quest step " + Target + " is done";
			}
			return Kind;
		}

		string ItemText() { return (Count > 1 ? Count + " \u00D7 " : "") + (Target.Length > 0 ? ContentCatalog.ItemLabel(Target) : "(no item)"); }
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
	/// forth by itself. Every machine animates its own copy, on the clock the players share (<see cref="SharedClock"/>),
	/// so loops, spins and bobbing are in step for everyone.
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

		public void Configure(IDictionary<string, string> p, int index)
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
			// Objects start at different points of their cycles, the same on every machine (from their place in the island file)
			phase = Mathf.Repeat(index * 0.618034f, 1f) * 10f;
		}

		/// <summary>Jumps to the open or closed pose (an island loading with its saved state).</summary>
		public void Snap(bool open) { Target = Current = open ? 1f : 0f; Pose(); }

		bool Animates { get { return SpinSpeed != 0f || BobHeight != 0f || Offset != Vector3.zero || TurnDegrees != 0f; } }

		void Update()
		{
			if (!Animates) return;
			float t = SharedClock.Now + phase;
			if (Loop) Current = Mathf.PingPong(t / MoveTime, 1f);
			else if (Current != Target) Current = Mathf.MoveTowards(Current, Target, Time.deltaTime / MoveTime);
			Pose();
		}

		void Pose()
		{
			float now = SharedClock.Now + phase;
			float s = Mathf.SmoothStep(0f, 1f, Current);
			Transform parent = transform.parent;
			// The offset is in world directions (as the builder sees them); the island root isn't turned, so local = world
			Vector3 pos = startPos + Offset * s;
			if (BobHeight != 0f) pos += Vector3.up * BobHeight * Mathf.Sin(now * Mathf.PI * 2f / BobTime);
			Quaternion rot = Quaternion.Euler(0f, TurnDegrees * s + Mathf.Repeat(SpinSpeed * now, 360f), 0f) * startRot;
			transform.localPosition = pos;
			transform.localRotation = rot;
		}
	}

	/// <summary>
	/// A clock that runs the same on every machine of a game: Raft's water time, which the host sends to everyone
	/// (Network_Water, in its regular world update), followed smoothly from this machine's own time so it never
	/// jumps from frame to frame. Without water (the editor, the main menu) it is the local time.
	/// </summary>
	public static class SharedClock
	{
		static float offset;
		static bool synced;
		static int frame = -1;

		/// <summary>Seconds on the shared clock.</summary>
		public static float Now { get { Follow(); return Time.time + offset; } }

		/// <summary>Whether the clock follows the host's (false: only this machine's time).</summary>
		public static bool Synced { get { Follow(); return synced; } }

		static void Follow()
		{
			if (frame == Time.frameCount) return;
			frame = Time.frameCount;
			Network_Water water = ComponentManager<Network_Water>.Value;
			if (water == null) { synced = false; offset = 0f; return; }
			float target;
			try { target = water.WaterTime - Time.time; }
			catch { return; }
			// A new game or a big correction: jump there; small differences (network delay) are eased out
			if (!synced || Mathf.Abs(target - offset) > 2f) offset = target;
			else offset = Mathf.Lerp(offset, target, Mathf.Min(1f, Time.deltaTime * 0.5f));
			synced = true;
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
				go.AddComponent<IslandBehaviour>().Configure(props, index);
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

		public static List<ObjCheck> ChecksOf(IslandWorldState.Entry e, int index, string ev)
		{
			return ObjCheck.ParseLines(ObjectProps.Get(PropsOf(e, index), BehaviourProps.CheckKey(ev)));
		}

		/// <summary>The last check that failed here, described (tests).</summary>
		public static string LastFailedCheck { get; private set; }

		/// <summary>Whether the event's checks are "any of" (one passing is enough).</summary>
		public static bool AnyOf(IslandWorldState.Entry e, int index, string ev)
		{
			return ObjCheck.IsAny(ObjectProps.Get(PropsOf(e, index), BehaviourProps.CheckKey(ev)));
		}

		/// <summary>
		/// Whether the checks pass for this machine's player: all of them, or with any, at least one. Only then "take"
		/// checks use their items up (Raft's items from this player's inventory, story items from the crew's): all of
		/// them, or with any, only the one that passed first.
		/// </summary>
		public static bool Passes(IslandWorldState.Entry e, int index, List<ObjCheck> checks, bool any = false)
		{
			PlayerInventory inv = RAPI.GetLocalPlayer() != null ? RAPI.GetLocalPlayer().Inventory : null;
			var passed = new List<ObjCheck>();
			foreach (ObjCheck c in checks)
			{
				bool ok;
				try { ok = Holds(e, index, c, inv) != c.Not; }
				catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Check '" + c.Kind + "': " + ex.Message); ok = false; }
				if (ok) { passed.Add(c); if (any) break; }
				else if (!any) { LastFailedCheck = c.Describe(); return false; }
			}
			if (passed.Count == 0) { LastFailedCheck = "none of: " + string.Join(" / ", checks.Select(c => c.Describe()).ToArray()); return false; }
			foreach (ObjCheck c in passed.Where(x => x.Kind == "take" && !x.Not))
			{
				if (StoryItems.IsStory(c.Target)) StoryBook.Take(StoryItems.IdOf(c.Target), c.Count);
				else if (inv != null) inv.RemoveItem(c.Target, c.Count);
			}
			return true;
		}

		/// <summary>
		/// Checks an event before it happens (a chest checks before it gives its loot): true when there are no checks or
		/// they pass; otherwise the "otherwise" actions run and it is false. The event is then fired with skipChecks.
		/// </summary>
		public static bool Allows(IslandWorldState.Entry e, int index, string ev)
		{
			if (e == null) return true;
			if (index < 0) index = IslandIndex;
			List<ObjCheck> checks = ChecksOf(e, index, ev);
			if (checks.Count == 0 || Passes(e, index, checks, AnyOf(e, index, ev))) return true;
			Otherwise(e, index, ev, true);
			return false;
		}

		static bool Holds(IslandWorldState.Entry e, int index, ObjCheck c, PlayerInventory inv)
		{
			switch (c.Kind)
			{
				case "has":
				case "take":
					if (StoryItems.IsStory(c.Target)) return StoryBook.Count(StoryItems.IdOf(c.Target)) >= c.Count;
					return inv != null && c.Target.Length > 0 && inv.GetItemCount(c.Target) >= c.Count;
				case "state":
					List<IslandObjectRef> refs = Targets(e, index, c.Target);
					if (refs.Count == 0) return false;
					foreach (IslandObjectRef r in refs)
					{
						bool visible, open;
						StateOf(e, r, out visible, out open);
						bool holds = c.Arg == "closed" ? !open : c.Arg == "shown" ? visible : c.Arg == "hidden" ? !visible : open;
						if (!holds) return false;
					}
					return true;
				case "signal":
					return e.State.ContainsKey(SignalKey(c.Target));
				case "quest":
					int steps;
					int need = int.TryParse(c.Target, NumberStyles.Integer, CultureInfo.InvariantCulture, out steps) ? steps : QuestTracker.QuestOf(e).Steps.Count;
					return QuestTracker.StepOf(e) >= Mathf.Max(1, need);
			}
			return false;
		}

		/// <summary>
		/// Something happened to an island object (index; -1 = the island itself). localPlayer: this machine's player
		/// did it (gets the personal actions). The checks are made here first; when one fails, the "otherwise" actions
		/// run instead. Shared actions run on the host (a client asks it). A wait puts the actions after it off.
		/// </summary>
		public static void Fire(IslandWorldState.Entry e, int index, string ev, bool localPlayer, bool skipChecks = false)
		{
			if (e == null) return;
			if (index < 0) index = IslandIndex;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			List<ObjCheck> checks = ChecksOf(e, index, ev);
			if (actions.Count == 0 && checks.Count == 0) return;
			// Reading a note counts once per world
			bool once = ev == "read" || ev == "arrive";
			int key = DoneBase + index;
			if (once && e.State.ContainsKey(key)) { if (localPlayer) Schedule(e, index, actions, false, ev == "arrive"); return; }
			if (!skipChecks && checks.Count > 0 && !Passes(e, index, checks, AnyOf(e, index, ev)))
			{
				Otherwise(e, index, ev, localPlayer);
				return;
			}
			if (once)
			{
				e.State[key] = new ObjectState { Active = false, Day = Today };
				IslandNetwork.SendUsed(e.Id, key, Today);
			}
			Run(e, index, ev, actions, localPlayer);
			if (Fired != null) try { Fired(e.Id, index, ev); } catch { }
		}

		/// <summary>A check failed: the event's "otherwise" actions (usually a message for the player who tried).</summary>
		static void Otherwise(IslandWorldState.Entry e, int index, string ev, bool localPlayer)
		{
			string otherwise = ev.TrimEnd('!') + "!";
			Debug.Log("[CUSTOM ISLANDS] '" + ev + "' on '" + e.HostName + "': not yet (" + LastFailedCheck + ")");
			Run(e, index, otherwise, ActionsOf(e, index, otherwise), localPlayer);
			if (Fired != null) try { Fired(e.Id, index, otherwise); } catch { }
		}

		/// <summary>The personal part here (for this machine's player), the shared part on the host.</summary>
		static void Run(IslandWorldState.Entry e, int index, string ev, List<ObjAction> actions, bool localPlayer)
		{
			if (actions.Count == 0) return;
			if (localPlayer) Schedule(e, index, actions, false, false);
			if (actions.Any(a => a.Shared))
			{
				if (Raft_Network.IsHost) Schedule(e, index, actions, true, false);
				else IslandNetwork.SendEvent(e.Id, index, ev, false);
			}
		}

		/// <summary>The host noticed something no single player did (animals defeated): everyone near the island gets the personal part.</summary>
		public static void FireFromHost(IslandWorldState.Entry e, int index, string ev)
		{
			if (e == null || !Raft_Network.IsHost) return;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			List<ObjCheck> checks = ChecksOf(e, index, ev);
			if (actions.Count == 0 && checks.Count == 0) return;
			if (checks.Count > 0 && !Passes(e, index, checks, AnyOf(e, index, ev))) ev += "!";
			actions = ActionsOf(e, index, ev);
			if (Near(e)) Schedule(e, index, actions, false, false);
			Schedule(e, index, actions, true, false);
			IslandNetwork.SendEvent(e.Id, index, ev, true);
			if (Fired != null) try { Fired(e.Id, index, ev); } catch { }
		}

		/// <summary>An event that happens on every machine by itself (a quest done): personal part here if near, shared part on the host.</summary>
		public static void FireEverywhere(IslandWorldState.Entry e, int index, string ev)
		{
			if (e == null) return;
			if (index < 0) index = IslandIndex;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			List<ObjCheck> checks = ChecksOf(e, index, ev);
			if (actions.Count == 0 && checks.Count == 0) return;
			if (checks.Count > 0 && !Passes(e, index, checks, AnyOf(e, index, ev))) actions = ActionsOf(e, index, ev + "!");
			if (Near(e)) Schedule(e, index, actions, false, false);
			if (Raft_Network.IsHost) Schedule(e, index, actions, true, false);
			if (Fired != null) try { Fired(e.Id, index, ev); } catch { }
		}

		/// <summary>From the network: a client's event (host: do the shared part), or the host's (client: the personal part if near).</summary>
		public static void OnEventMessage(int islandId, int index, string ev, bool fromHost)
		{
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == islandId);
			if (e == null) return;
			List<ObjAction> actions = ActionsOf(e, index, ev);
			// (the client made the checks already)
			if (Raft_Network.IsHost && !fromHost) Schedule(e, index, actions, true, false);
			else if (fromHost && Near(e)) Schedule(e, index, actions, false, false);
		}

		/// <summary>
		/// Runs one part of the actions (shared or personal), the ones after a wait that many seconds later. Waiting
		/// actions need the island to stay loaded: when it unloads (the raft sails away), what's left does nothing.
		/// </summary>
		static void Schedule(IslandWorldState.Entry e, int index, List<ObjAction> actions, bool shared, bool messagesOnly)
		{
			float delay = 0f;
			var part = new List<ObjAction>();
			foreach (ObjAction a in actions.Concat(new[] { new ObjAction { Verb = "wait", Arg = "0" } }))
			{
				if (a.Verb != "wait") { part.Add(a); continue; }
				if (part.Count > 0)
				{
					List<ObjAction> now = part;
					if (delay <= 0f) RunPart(e, index, now, shared, messagesOnly);
					else DynamicIslands.instance.StartCoroutine(Later(delay, () => { if (IslandWorldState.Contains(e) && LoadSceneManager.IsGameSceneLoaded) RunPart(e, index, now, shared, messagesOnly); }));
				}
				part = new List<ObjAction>();
				delay += a.Seconds;
			}
		}

		static System.Collections.IEnumerator Later(float seconds, Action then)
		{
			yield return new WaitForSeconds(seconds);
			try { then(); }
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Waiting actions: " + ex.Message); }
		}

		static void RunPart(IslandWorldState.Entry e, int index, List<ObjAction> actions, bool shared, bool messagesOnly)
		{
			if (shared) RunShared(e, index, actions); else RunPersonal(e, index, actions, messagesOnly);
		}

		static void RunShared(IslandWorldState.Entry e, int index, List<ObjAction> actions)
		{
			bool creaturesShown = false;
			foreach (ObjAction a in actions.Where(x => x.Shared))
			{
				if (a.Verb == "signal")
				{
					int key = SignalKey(a.Arg);
					e.State[key] = new ObjectState { Active = true, Day = Today };
					IslandNetwork.SendUsed(e.Id, key, Today); // (clients check signals too)
					Debug.Log("[CUSTOM ISLANDS] Signal '" + a.Arg + "' on '" + e.HostName + "'");
					continue;
				}
				if (a.Verb == "journal")
				{
					StoryBook.AddPage("act:" + e.HostName + ":" + index + ":" + a.Target, a.Target, a.Arg.Replace("\\n", "\n"), IslandTitle(e));
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

		/// <summary>The island's name as players see it (its title, or the file name).</summary>
		public static string IslandTitle(IslandWorldState.Entry e)
		{
			string t = ObjectProps.Get(IslandCache.PropsOf(e), IslandProps.Title);
			return t.Length > 0 ? t : e.Label.Length > 0 ? e.Label : e.HostName;
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
