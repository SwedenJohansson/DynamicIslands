using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Ready puzzle story sets (ROADMAP QE5, 2026-11-03), the Story items window's "Puzzles" row. Each places a working
	/// puzzle with today's behaviours (actions, checks, signals): levers in order (an order puzzle, OrderPuzzle.cs), three
	/// water wheels and a gate (state checks), a generator and a radio (Raft's generator part, the signal power), and a
	/// laser with two mirrors and a door panel on a wall (LaserBeam.cs). The pieces are placed from the view's middle
	/// along the world's axes; the laser set is lined up from the emitter's measured front and checks its beam's path.
	/// </summary>
	public static partial class StorySets
	{
		public static readonly string[] LeverPieces = { "TP_LaserControlPanel_lever", "TP_CoolingStation_Monitor_Lever", "TradingPost_Register_Lever" };
		public static readonly string[] WheelPieces = { "UT_WaterChallengeWheel1", "UT_WaterChallengeWheel2", "UT_WaterChallengeWheel3" };
		public static readonly string[] GeneratorPieces = { "EmergencyGenerator", "Generator", "UT_Generator01", "VG_DecorationPrefabBase_EmergencyGenerator Variant" };
		public static readonly string[] RadioPieces = { "Model_Radio", "RT_CommRadio", "Balboa_DecorationPrefabBase_RadioDevice" };
		public const string Emitter = "TP_LaserEmitter", Mirror = "TP_MirrorHousing_Rotating", LaserPanel = "TP_LaserDoorPanel";
		/// <summary>Metres between the laser set's pieces (emitter -> fixed mirror -> turning mirror -> panel).</summary>
		public const float LaserLeg = 7f;

		/// <summary>What the last set placed warns about (null = nothing): the laser set's beam blocked, off level ground.</summary>
		public static string Warning;

		// (Raft's pieces each set loads before placing: the first choice of each; the rest are used when already loaded)
		public static readonly string[] LeverNeeds = { LeverPieces[0] };
		public static readonly string[] WheelNeeds = WheelPieces;
		public static readonly string[] GeneratorNeeds = { GeneratorPieces[0], RadioPieces[0] };
		public static readonly string[] LaserNeeds = { Emitter, Mirror, LaserPanel };

		static string Pick(params string[] names) { return names.FirstOrDefault(PlaceableCatalog.IsLoaded); }

		static List<GameObject> Done(List<GameObject> list, int count)
		{
			if (list.Count < count) { foreach (GameObject go in list) UnityEngine.Object.Destroy(go); return new List<GameObject>(); }
			CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(list, true));
			return list;
		}

		/// <summary>An order puzzle name no placed object uses yet ("Levers", "Levers-2"...).</summary>
		static string FreeGroup(string baseName)
		{
			GameObject placed = GameObject.Find("PlacedObjects");
			Dictionary<string, List<int>> used = placed == null ? new Dictionary<string, List<int>>() : OrderPuzzle.EditorSteps(placed.GetComponentsInChildren<EditorGameObject>(true));
			string n = baseName;
			for (int i = 2; used.ContainsKey(n); i++) n = baseName + "-" + i;
			return n;
		}

		/// <summary>
		/// Three levers in a row, an order puzzle (each lever "order|group|n"): standing at the note, the right one first,
		/// then the left one, the middle one last. Solved, it shows a hidden chest behind the levers.
		/// </summary>
		public static List<GameObject> LeversInOrder(List<StoryItemDef> defs, Vector3 at)
		{
			Warning = null;
			var list = new List<GameObject>();
			string lever = Pick(LeverPieces);
			if (lever == null) return list;
			string group = FreeGroup("Levers"), chest = FreeName("lever-chest");
			string[] side = { "right", "left", "middle" };
			float[] x = { 3f, -3f, 0f };
			for (int n = 1; n <= 3; n++)
			{
				Dictionary<string, string> p = ObjectProps.Defaults(lever);
				p[BehaviourProps.Name] = FreeName("lever-" + side[n - 1]);
				p[BehaviourProps.EventKey("use")] = "message||Clunk.";
				p = OrderPuzzle.MakePiece(p, group, n);
				if (n == 1) p[BehaviourProps.EventKey(OrderPuzzle.SolvedEvent)] = "show|" + chest + "|\nmessage||The last lever locks with a heavy clunk. Something opened behind the levers!";
				Put(list, lever, at + new Vector3(x[n - 1], 0f, 0f), 0f, p);
			}
			Put(list, "Loot_Chest", at + new Vector3(0f, 0f, 5f), 180f, P(BehaviourProps.Name, chest, BehaviourProps.Hidden, "1", ObjectProps.LootItems, "Scrap*6;Bolt*4;Nail*12", ObjectProps.NoteTitle, "Chest"));
			Put(list, "Note_Paper", at + new Vector3(0f, 0f, -3f), 0f, P(ObjectProps.NoteTitle, "Scribbled note",
				ObjectProps.NoteText, "The levers only work in the right order. A wrong one and they all click back.\n\nStand here and face them: the right one first, then the left one, the middle one last."));
			return Done(list, 5);
		}

		/// <summary>
		/// Utopia's three water wheels in a row (each turns a quarter on use, open = turned) and a gate that opens only when
		/// the outer two are turned and the middle one is not (state checks); it sends "wheels". A note hints it.
		/// </summary>
		public static List<GameObject> ThreeWheels(List<StoryItemDef> defs, Vector3 at)
		{
			Warning = null;
			var list = new List<GameObject>();
			string door = DoorObject();
			if (door == null || Pick(WheelPieces) == null) return list;
			var wheel = new string[3];
			for (int i = 0; i < 3; i++)
			{
				wheel[i] = FreeName("wheel-" + (i + 1));
				// (Utopia's wheels are flat rings 9.3, 8.4 and 6.9 m across: 10 m apart, they don't overlap)
				Put(list, Pick(WheelPieces[i]) ?? Pick(WheelPieces), at + new Vector3(-10f + i * 10f, 0f, 0f), 0f, P(BehaviourProps.Name, wheel[i]));
			}
			Put(list, door, at + new Vector3(0f, 0f, 8f), 0f, P(BehaviourProps.Name, FreeName("wheel-gate"), BehaviourProps.Turn, "90", BehaviourProps.MoveTime, "1.5", BehaviourProps.Use, "Open the gate",
				BehaviourProps.CheckKey("use"), "state|" + wheel[0] + "|open\nstate|" + wheel[1] + "|closed\nstate|" + wheel[2] + "|open",
				BehaviourProps.ElseKey("use"), "message||The gate won't budge. The wheels have to be set right first.",
				BehaviourProps.EventKey("use"), "open|\nmessage||Water rushes through the channels and the gate swings open.\nsignal||wheels"));
			Put(list, "Note_Paper", at + new Vector3(0f, 0f, -7f), 0f, P(ObjectProps.NoteTitle, "Wet note",
				ObjectProps.NoteText, "The gate opens when the water runs right: turn the two outer wheels and leave the middle one as it is."));
			return Done(list, 5);
		}

		/// <summary>
		/// A broken generator (it wants Raft's generator part, used up; then it sends "power"), the part in a chest nearby,
		/// and a radio that only works with the power on (it sends "radio" and adds a journal page). A note tells the story.
		/// </summary>
		public static List<GameObject> GeneratorRadio(List<StoryItemDef> defs, Vector3 at)
		{
			Warning = null;
			var list = new List<GameObject>();
			string gen = Pick(GeneratorPieces), radio = Pick(RadioPieces);
			if (gen == null || radio == null || ReadyPieces.ItemOf(gen) == null) return list;
			Dictionary<string, string> g = ObjectProps.Defaults(gen);
			g[BehaviourProps.Name] = FreeName("generator");
			ReadyPieces.SetParts(gen, g, 1);
			Put(list, gen, at, 0f, g);
			Dictionary<string, string> r = ObjectProps.Defaults(radio);
			r[BehaviourProps.Name] = FreeName("radio");
			r[BehaviourProps.EventKey("use")] = ObjectProps.Get(r, BehaviourProps.EventKey("use")) + "\njournal|The radio|With the generator running, the old radio came to life. A voice repeated a frequency, over and over.";
			Put(list, radio, at + new Vector3(-4f, 0f, 0f), 0f, r);
			Put(list, "Loot_ChestSmall", at + new Vector3(6f, 0f, 3f), 200f, P(ObjectProps.LootItems, StoryItems.Ref(ReadyPieces.ItemOf(gen)) + "*1;Scrap*3", ObjectProps.NoteTitle, "Chest"));
			Put(list, "Note_Paper", at + new Vector3(2f, 0f, -3f), 0f, P(ObjectProps.NoteTitle, "Mechanic's note",
				ObjectProps.NoteText, "The generator died again. I kept a spare part in a chest nearby.\n\nGet the generator running first - the radio needs its power."));
			return Done(list, 4);
		}

		/// <summary>
		/// Temperance's laser: the emitter shoots along its front to a fixed mirror (7 m), which sends the beam aside to a
		/// turning mirror (7 m on); placed, that one sends it straight on (a miss), turned a quarter it sends it back to the
		/// laser door panel on a wall, which shows a hidden chest behind the wall (and sends "laser"). Lined up from the
		/// emitter's measured front and the middle of its model (where the beam starts); the beam's path is checked
		/// (Warning: blocked by the ground or another object - place it on level ground).
		/// </summary>
		public static List<GameObject> LaserMirrors(List<StoryItemDef> defs, Vector3 at)
		{
			Warning = null;
			var list = new List<GameObject>();
			string wallObject = DoorObject();
			if (wallObject == null || LaserNeeds.Any(n => !PlaceableCatalog.IsLoaded(n))) return list;
			string chest = FreeName("laser-chest");
			GameObject emitter = Put(list, Emitter, at, 0f, P(BehaviourProps.Name, FreeName("laser")));
			if (emitter == null) return list;
			// (its model hangs 1.8 m below its pivot, and the beam goes along its front: stood upright on the ground)
			Vector3 front = emitter.transform.forward;
			if (Mathf.Abs(front.y) > 0.05f) emitter.transform.rotation = Quaternion.FromToRotation(front, Flat(front)) * emitter.transform.rotation;
			SitOnGround(emitter);
			Vector3 o = ModelCentre(emitter), f = Flat(emitter.transform.forward), r = Quaternion.Euler(0f, 90f, 0f) * f;
			Vector3 m1 = o + f * LaserLeg, m2 = m1 + r * LaserLeg, panelAt = m2 - f * LaserLeg;
			GameObject fixedMirror = Put(list, Mirror, m1, 0f, P(BehaviourProps.Name, FreeName("mirror-fixed")));
			if (fixedMirror != null)
			{
				// (the turning mirror's housing, its model round its pivot - Raft's fixed mirror's model stands 4.6 m off it -
				// that doesn't turn)
				Dictionary<string, string> p = fixedMirror.GetComponent<EditorGameObject>().Props;
				foreach (string k in new[] { BehaviourProps.Turn, BehaviourProps.MoveTime, BehaviourProps.Use, BehaviourProps.EventKey("use") }) p.Remove(k);
				Face(fixedMirror, r);
			}
			GameObject turning = Put(list, Mirror, m2, 0f, P(BehaviourProps.Name, FreeName("mirror-turn")));
			if (turning != null) Face(turning, r);
			GameObject wall = Put(list, wallObject, panelAt - f * 0.25f, 0f, P());
			if (wall != null) Face(wall, f);
			Dictionary<string, string> pp = ObjectProps.Defaults(LaserPanel);
			pp[BehaviourProps.Name] = FreeName("laser-panel");
			pp[BehaviourProps.EventKey(LaserBeam.Event)] = ObjectProps.Get(pp, BehaviourProps.EventKey(LaserBeam.Event)) + "\nshow|" + chest + "|";
			GameObject panel = Put(list, LaserPanel, panelAt, 0f, pp);
			if (panel != null)
			{
				Face(panel, f);
				panel.transform.position += new Vector3(panelAt.x, o.y, panelAt.z) - ModelCentre(panel);
			}
			Put(list, "Loot_ChestSmall", panelAt - f * 2.5f, 0f, P(BehaviourProps.Name, chest, BehaviourProps.Hidden, "1", ObjectProps.LootItems, "Scrap*5;Bolt*3;CopperIngot*2", ObjectProps.NoteTitle, "Chest"));
			Put(list, "Note_Paper", o - f * 1.5f + r * 1.5f, 0f, P(ObjectProps.NoteTitle, "Engineer's note",
				ObjectProps.NoteText, "The beam has to reach the panel on the wall. The mirrors send it on - and one of them can be turned."));
			if (list.Count < 7) return Done(list, 7);
			Warning = LaserProblem(emitter, fixedMirror, turning, panel, wall, o, f, r);
			return Done(list, 7);
		}

		/// <summary>Whether the laser set's beam gets through: emitter -> fixed mirror -> turning mirror, and (turned) -> the
		/// panel; the panel on its wall. Null when it does.</summary>
		internal static string LaserProblem(GameObject emitter, GameObject fixedMirror, GameObject turning, GameObject panel, GameObject wall, Vector3 o, Vector3 f, Vector3 r)
		{
			Physics.SyncTransforms();
			if (FirstOnBeam(o, f, emitter.transform) != fixedMirror) return "the laser's beam doesn't reach the first mirror (" + What(FirstOnBeam(o, f, emitter.transform)) + " is in the way): place it on level ground";
			Vector3 p1 = new Vector3(fixedMirror.transform.position.x, o.y, fixedMirror.transform.position.z);
			if (FirstOnBeam(p1, r, fixedMirror.transform) != turning) return "the beam doesn't reach the turning mirror (" + What(FirstOnBeam(p1, r, fixedMirror.transform)) + " is in the way): place it on level ground";
			Vector3 p2 = new Vector3(turning.transform.position.x, o.y, turning.transform.position.z);
			GameObject last = FirstOnBeam(p2, -f, turning.transform);
			if (last != panel && last != wall) return "the turned mirror's beam doesn't reach the panel (" + What(last) + " is in the way): place it on level ground";
			Bounds pb = ModelBounds(panel), wb = ModelBounds(wall);
			if (pb.min.y < wb.min.y - 0.05f || pb.max.y > wb.max.y + 0.05f) return "the panel is not on its wall (the ground under them is not level)";
			return null;
		}

		static GameObject FirstOnBeam(Vector3 from, Vector3 dir, Transform skip)
		{
			foreach (RaycastHit h in Physics.RaycastAll(from, dir, LaserLeg * 2f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
			{
				if (h.collider.transform.IsChildOf(skip)) continue;
				EditorGameObject g = h.collider.GetComponentInParent<EditorGameObject>();
				return g != null ? g.gameObject : h.collider.gameObject;
			}
			return null;
		}

		static string What(GameObject go)
		{
			if (go == null) return "nothing - it goes past";
			EditorGameObject g = go.GetComponent<EditorGameObject>();
			return g != null ? PlaceableCatalog.DisplayName(g.GameObjectName) : go.GetComponent<Terrain>() != null ? "the ground" : go.name;
		}

		static Vector3 Flat(Vector3 v)
		{
			v.y = 0f;
			return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
		}

		/// <summary>Turns an object about the upright axis so its front (flattened) points along dir.</summary>
		static void Face(GameObject go, Vector3 dir)
		{
			go.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(BeamDir(go.transform), Flat(dir), Vector3.up), Vector3.up) * go.transform.rotation;
		}

		/// <summary>Where a mirror sends the beam (as LaserBeam.Trace): its front flattened, or its right side when its front
		/// points up or down.</summary>
		static Vector3 BeamDir(Transform t)
		{
			Vector3 f = t.forward;
			f.y = 0f;
			if (f.sqrMagnitude < 0.01f) { f = t.right; f.y = 0f; }
			return f.sqrMagnitude < 0.0001f ? Vector3.forward : f.normalized;
		}

		internal static Bounds ModelBounds(GameObject go)
		{
			Renderer[] rs = go.GetComponentsInChildren<Renderer>().Where(x => !(x is LineRenderer)).ToArray();
			if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
			Bounds b = rs[0].bounds;
			foreach (Renderer x in rs) b.Encapsulate(x.bounds);
			return b;
		}

		/// <summary>The middle of an object's model: where a laser emitter's beam starts (LaserBeam.Origin).</summary>
		internal static Vector3 ModelCentre(GameObject go) { return ModelBounds(go).center; }

		/// <summary>Moves an object up or down so its model's bottom is on the ground (a little in it).</summary>
		static void SitOnGround(GameObject go)
		{
			float ground = Ground(go.transform.position).y;
			go.transform.position += Vector3.up * (ground - 0.05f - ModelBounds(go).min.y);
		}
	}
}
