using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The recipe player (the library content, NextTask_LibraryContent.md): an island built step by step through the
	/// island editor's own operations - the generator, terrain strokes, the placer, the inspector's property changes, the
	/// note, quest, story item and rule editors, Save - each an undo step as when a person works in the editor. A recipe
	/// is a text file in Mods\DynamicIslands\recipes\ (the sources are in the repository's content\recipes): one step a
	/// line, # for comments. Positions are metres from the island's middle ("at x z"), on the ground unless said
	/// otherwise. Every object a recipe names must be one the editor has, or the recipe stops there (a missing object
	/// would only be a log line in a world).
	///
	/// Writing them: "set name value" and $name, {arithmetic} (+ - * / % ^, sin cos tan atan2 in degrees, sqrt abs min max
	/// floor ceil round, rnd(a,b) from the recipe's seed), "for v from to [step]" ... "next", "macro name params" ...
	/// "end" and "call name args" (params with =default, args by position or name=value), "include file" (macros
	/// shared by recipes). "push at x z [y=|h=] [yaw=]" ... "pop": a frame - positions inside are turned with it and
	/// heights are from its floor (y= above the ground there, h= above the sea), for buildings put together from pieces.
	///
	/// World plans are made the same way, in the editor's World plans window: "plan new <name>", "plan description",
	/// "plan random on|off", "plan story on|off", "plan leaveout <Raft story island>", "plan rule <rule line>" (its card
	/// filled in) and "plan save" (Save - Check finding a problem stops the recipe).
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIRecipe", docs: "Dev, the editor (opened when needed): builds an island (or a world plan, in World plans) from Mods\\DynamicIslands\\recipes\\<name>.recipe through the editor's operations (generator, terrain strokes, placer, properties, notes, quests, story items, rules, Save) - the library content's builder. Loads the saved island instead when the recipe, its includes and the mod build are what the last build saved (CIRecipeCache; 'fresh' builds anyway). A patch recipe starts with \"load <island name>\" (the saved island opened, a few ops, save <same name>) instead of new + generate. CIRecipe <name> [fresh]")]
		public static void RecipeCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Fail("recipe: name a recipe (Mods\\DynamicIslands\\recipes\\<name>.recipe)"); return; }
			// (the last word 'fresh': built even when the cache holds it - speedups 11-19)
			bool fresh = args.Length > 1 && args[args.Length - 1].Equals("fresh", StringComparison.OrdinalIgnoreCase);
			StartTest(CachedRecipeRoutine(string.Join(" ", fresh ? args.Take(args.Length - 1).ToArray() : args), fresh));
		}

		public static string RecipeFolder { get { return Path.Combine(DynamicIslands.assetpath, "recipes"); } }

		[ConsoleCommand(name: "CIMeasureObjects", docs: "Dev, main menu or editor: measures every object of an object browser category (or all of them) - size, centre, bottom, colliders - into Mods\\DynamicIslands\\recipes\\objects_<category>.txt (what recipes fit pieces together with). CIMeasureObjects <category|all>")]
		public static void MeasureObjectsCommand(string[] args)
		{
			string want = args != null && args.Length > 0 ? string.Join(" ", args) : "all";
			StartTest(MeasureObjects(want));
		}

		static IEnumerator MeasureObjects(string want)
		{
			yield return PlaceableCatalog.EnsureBuilt();
			yield return PlaceableCatalog.EnsureIndex();
			Directory.CreateDirectory(RecipeFolder);
			var cats = PlaceableCatalog.Browse().Where(c => want == "all" || c.Key.Equals(want, StringComparison.OrdinalIgnoreCase)).ToList();
			if (cats.Count == 0) { Fail("measure: no category '" + want + "' (" + string.Join(", ", PlaceableCatalog.Browse().Select(c => c.Key).ToArray()) + ")"); yield break; }
			foreach (var cat in cats)
			{
				yield return PlaceableCatalog.EnsureCategory(cat.Key);
				string file = Path.Combine(RecipeFolder, "objects_" + string.Join("_", cat.Key.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_') + ".txt");
				yield return MeasureProps(cat.Value.Select(e => e.Name).ToArray(), file);
			}
		}

		/// <summary>What CIFloating doesn't measure the base of: the generator's plants and rocks (a crown or a boulder is wider
		/// than what touches the ground), caves and tunnels (set into the land on purpose) - not the players' buildables (a
		/// grass crop plot on Tide Farm's deck was a plant 8 m over the sea floor, the review 2026-10-03).</summary>
		static readonly Regex NotABase = new Regex("^(?!Placeable_).*(Tree|Bush|Fern|Palm|Plant|Grass|Flower|Kelp|Coral|Rock|Boulder|Stone|Log|Shell|Cave|Tunnel|Vine|Seaweed|Cactus|Reed|Bamboo|Monstera|Drift|Stalagmite|Icicle)", RegexOptions.IgnoreCase);
		/// <summary>What hangs or lies on purpose: a buoy's chain, pipes, cables, lamps, flags; decks, floors and ramps on their
		/// posts; a crane's jib reaching out over the water.</summary>
		static readonly Regex FloorLike = new Regex("Floor|Deck|Foundation|Platform", RegexOptions.IgnoreCase);
		static readonly Regex Container = new Regex("^Loot_|Chest|Crate|Locker|Box|Barrel|Storage", RegexOptions.IgnoreCase);

		/// <summary>A floor or deck over nothing (CA30): its box and the corners of its underside nothing holds.</summary>
		class FloorGap { public EditorGameObject Floor; public Bounds Box; public List<Vector3> Corners; public bool Big; }

		/// <summary>
		/// Floors and decks over nothing (ROADMAP CA30, the user 2026-10-08: Saltpan Delta's pump house stood out over the
		/// water's edge on no posts): each corner of a floor's underside looks down for what holds it - the ground, a post, a
		/// wall or the floor under it - within gap + 0.2 m (not a floor beside it, nor a wall standing on it). A big deck
		/// (3 m and more) needs every corner held; a small floor one corner, or a held floor beside it (Raft's one-tile reach:
		/// a jetty's planks between its posts). A raft's foundation on the sea floats: one at the waterline is left out.
		/// CIFloating names them; a recipe's save stands posts under them (StandPosts).
		/// </summary>
		static List<FloorGap> FloorsOverNothing(IEnumerable<EditorGameObject> placed, float gap, Terrain ground, float sea, out int floors)
		{
			var decks = new List<FloorGap>();
			var held = new HashSet<EditorGameObject>();
			List<Bounds> building = null, rails = null;
			// (an island sunk by 'elevation -45' has the real sea 45 m over the editor's water line: a deck under it is under
			// the sea in the game - Hadal Commons' air platforms and drowned block, 11-05)
			if (DynamicIslands.currentElevation < 0f) sea -= DynamicIslands.currentElevation;
			foreach (EditorGameObject e in placed)
			{
				string n = e.GameObjectName ?? "";
				if (!FloorLike.IsMatch(n) || ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || ground == null || !e.gameObject.activeInHierarchy) continue;
				// (one hidden at first - a drawbridge's span, shown when it is lowered - rests on its ends then: posts under it
				// stood in the water with no deck on them while it was up, The Great Fen)
				if (BehaviourProps.StartsHidden(e.Props)) continue;
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (parts.Length == 0) continue;
				Bounds whole = parts[0].bounds;
				foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
				if (Mathf.Min(whole.size.x, whole.size.z) < 0.8f || Mathf.Abs(whole.min.y - sea) < 0.6f || whole.max.y < sea) continue;
				var d = new FloorGap { Floor = e, Box = whole, Corners = new List<Vector3>(), Big = Mathf.Max(whole.size.x, whole.size.z) >= 3f };
				decks.Add(d);
				// (turned off the grid, its box's corners are outside it: the corners of a box 30 % in. A post beside the corner
				// holds it too - 0.45 m round: a jetty's and a drawbridge's posts stand along its edges, The Great Fen)
				float turn = e.transform.eulerAngles.y % 90f;
				bool square = turn < 3f || turn > 87f;
				float inX = square ? 0.1f : whole.size.x * 0.3f, inZ = square ? 0.1f : whole.size.z * 0.3f;
				// (turned, a long floor's corners are its own: the box's 30 %-in corners of a 6 x 9 m roof turned 59 degrees lie
				// outside it, over nothing - Twin Forges' rotated radio tower rooms, their roofs on their walls read "over nothing"
				// x3, w16 11-04; IG5 11-18)
				Vector3[] turnedAt = square ? null : TurnedCorners(e, parts, 0.15f);
				for (int c = 0; c < 4; c++)
				{
					var p = turnedAt != null ? new Vector3(turnedAt[c].x, whole.min.y, turnedAt[c].z) :
						new Vector3(c % 2 == 0 ? whole.min.x + inX : whole.max.x - inX, whole.min.y, c < 2 ? whole.min.z + inZ : whole.max.z - inZ);
					float land = ground.SampleHeight(p) + ground.transform.position.y;
					bool ok = p.y - land <= gap + 0.2f;
					if (!ok)
						foreach (RaycastHit h in Physics.SphereCastAll(p + Vector3.up * 0.5f, 0.45f, Vector3.down, gap + 0.7f, ~0, QueryTriggerInteraction.Ignore))
						{
							EditorGameObject o = h.collider.GetComponentInParent<EditorGameObject>();
							// (what holds it is under it: reaching 0.1 m below its underside, or a thin floor whose top is at it -
							// Brinelab's greenhouse planters, foundations set on the storey's 0.1 m floor with their underside 2 mm
							// into it, read "held nowhere", w03 11-09; a wall standing on it starts above its underside)
							Bounds hb = h.collider.bounds;
							if (o == e || (hb.min.y > p.y - 0.1f && !(hb.max.y <= p.y + 0.06f && hb.min.y < p.y - 0.02f))) continue;
							// (nor the ladders zipto stood up a raised deck's side: 11-21 c3y9, Engine Court's 6.6 m ridge deck read as held
							// by them - a small floor needs one held corner - and stood on its ladders alone)
							if (o != null && zipDeckLadders.Contains(o.gameObject)) continue;
							if (o != null && FloorLike.IsMatch(o.GameObjectName ?? "") && Mathf.Abs(h.collider.bounds.max.y - whole.max.y) < 0.3f) continue;
							ok = true;
							break;
						}
					// (a building piece drawn under it holds it too, if its top is within 1.5 m: a roof slab over a room whose
					// ceiling has no collider - Shelter Atoll's storeroom, RF5 11-05)
					if (!ok)
					{
						if (building == null) building = PostBuilding(placed);
						Bounds own = whole;
						ok = building.Any(b => !own.Contains(b.center) && b.max.y < p.y - 0.05f && b.max.y > p.y - gap - 1.5f &&
							p.x > b.min.x + 0.1f && p.x < b.max.x - 0.1f && p.z > b.min.z + 0.1f && p.z < b.max.z - 0.1f);
					}
					// (or a pole or beam laid right under it that has no collider in the editor - Caisson Yard's mast bridges lie on
					// two Antenna_pole rails, their top 2 cm under the deck: the middle decks read "held nowhere", w03 11-09)
					if (!ok)
					{
						if (rails == null) rails = CollisionlessParts(placed);
						Bounds own = whole;
						ok = rails.Any(b => !own.Contains(b.center) && b.max.y <= p.y + 0.05f && b.max.y > p.y - 0.15f &&
							p.x > b.min.x - 0.15f && p.x < b.max.x + 0.15f && p.z > b.min.z - 0.15f && p.z < b.max.z + 0.15f);
					}
					if (ok) held.Add(e);
					else d.Corners.Add(p);
				}
			}
			floors = decks.Count;
			var over = new List<FloorGap>();
			foreach (FloorGap d in decks)
			{
				if (d.Corners.Count == 0) continue;
				if (!d.Big)
				{
					if (held.Contains(d.Floor)) continue;
					Bounds near = d.Box;
					near.Expand(0.2f);
					if (decks.Any(o => o != d && held.Contains(o.Floor) && Mathf.Abs(o.Box.max.y - d.Box.max.y) < 0.3f && o.Box.Intersects(near))) continue;
				}
				over.Add(d);
			}
			return over;
		}
		/// <summary>A turned piece's four corners in its own frame (its meshes' bounds, <paramref name="inset"/> m in from its
		/// edges), in the world; null when a part has no mesh to read.</summary>
		static Vector3[] TurnedCorners(EditorGameObject e, Renderer[] parts, float inset)
		{
			Transform f = e.transform;
			bool any = false;
			Vector3 lo = Vector3.zero, hi = Vector3.zero;
			foreach (Renderer r in parts)
			{
				MeshFilter mf = r.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null) return null;
				Bounds mb = mf.sharedMesh.bounds;
				for (int k = 0; k < 8; k++)
				{
					Vector3 c = new Vector3((k & 1) == 0 ? mb.min.x : mb.max.x, (k & 2) == 0 ? mb.min.y : mb.max.y, (k & 4) == 0 ? mb.min.z : mb.max.z);
					Vector3 l = f.InverseTransformPoint(r.transform.TransformPoint(c));
					if (!any) { lo = hi = l; any = true; } else { lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); }
				}
			}
			if (!any) return null;
			Vector3 s = f.lossyScale;
			float ix = Mathf.Min(inset / Mathf.Max(1e-3f, Mathf.Abs(s.x)), (hi.x - lo.x) / 2f), iz = Mathf.Min(inset / Mathf.Max(1e-3f, Mathf.Abs(s.z)), (hi.z - lo.z) / 2f);
			var corners = new Vector3[4];
			for (int c = 0; c < 4; c++)
				corners[c] = f.TransformPoint(new Vector3(c % 2 == 0 ? lo.x + ix : hi.x - ix, lo.y, c < 2 ? lo.z + iz : hi.z - iz));
			return corners;
		}

		static readonly Regex Hangs = new Regex("Buoy|Water|Pipe|Chain|Rope|Cable|Lamp|Light|Flag|Banner|Floor|Deck|Plank|Bridge|Ramp|Stair|Crane|Roof|Scaffold", RegexOptions.IgnoreCase);

		[ConsoleCommand(name: "CIGroundingTest", docs: "Dev, editor: nothing stands in the air on a slope - the generator's rocks and bushes go down to the lowest ground under their base and its snow drifts lie along gentle slopes only; Ground and placing put a wide object down by its base; objects go up and down with the ground a brush stroke changes (one undo step with it); the save sets a rock in the air down and a buried barrel up; a zipline rope is measured over the cave hill a world builds; a rope no raise clears gets the most room zipto can give it; a zipto far end under the cave hill goes onto its skin and the ground check sees a rope's last metres; a deck ladder's climbing side is cleared of the generator's boulders; a rope into the hill's slope before its far end gets that end raised on a deck; a rope touching its own far floor by a few mm is no cut and gets no deck")]
		public static void GroundingTestCommand()
		{
			StartTest(GroundingTestRoutine());
		}

		static IEnumerator GroundingTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			// 1. The generator on a steep, rocky snow island: rocks stand with all of their base on the ground
			int count = IslandGenerator.GenerateInEditor(IslandGenSettings.FromText("Seed=777\nRadius=70\nHeight=30\nRoughness=0.6\nPeaks=3\nShape=0\nStyle=1\nCliffs=0.5\nObjectDensity=0.7\nRocks=0.8\nBushes=0.6\nTrees=0.4"));
			yield return null;
			Physics.SyncTransforms();
			Terrain ground = terraineditor.terrain;
			float sea = DynamicIslands.EditorWaterLevel;
			int rocks = 0, standing = 0, drifts = 0, flatDrifts = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string n = e.GameObjectName ?? "";
				if (n.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0 && e.transform.position.y > sea + 0.5f)
				{
					rocks++;
					// (its pivot no higher than the ground under the middle of its base: on a slope, down to the low side)
					Bounds b;
					if (!PlaceableCatalog.LocalBounds(n, out b)) continue;
					float w = Mathf.Max(b.size.x * e.transform.lossyScale.x, b.size.z * e.transform.lossyScale.z) * 0.3f;
					float low = float.MaxValue;
					for (int i = 0; i < 8; i++)
					{
						Vector3 q = e.transform.position + new Vector3(Mathf.Cos(i * Mathf.PI / 4f) * w, 0f, Mathf.Sin(i * Mathf.PI / 4f) * w);
						low = Mathf.Min(low, ground.SampleHeight(q) + ground.transform.position.y);
					}
					// (or as deep as "never out of sight" lets it - D62, 6494a3d: its top a third of its height and 10 cm over the
					// terrain at its middle; on a steep slope that holds a flat rock or a stone up, its low side off the ground,
					// rather than sink its top under it)
					float g = ground.SampleHeight(e.transform.position) + ground.transform.position.y;
					float ly = Mathf.Abs(e.transform.lossyScale.y), height = b.size.y * ly;
					bool showing = e.transform.position.y + b.max.y * ly - g <= Mathf.Min(Mathf.Max(0.35f * height, 0.15f) + 0.1f, 0.8f * height) + 0.05f;
					if (e.transform.position.y <= low + 0.05f || showing) standing++;
				}
				if (n.StartsWith("TP_SnowDrift"))
				{
					drifts++;
					if (Vector3.Angle(e.transform.up, Vector3.up) <= 20.5f) flatDrifts++;
				}
			}
			Check(ref ok, count > 0 && rocks > 10 && standing >= rocks * 0.97f, "the generator: " + standing + " of " + rocks + " rocks on land stand with all of their base on the ground or as deep as never out of sight lets them (" + count + " objects)");
			Check(ref ok, drifts > 0 && flatDrifts == drifts, "snow drifts lie along gentle slopes only: " + flatDrifts + " of " + drifts + " lean 20 degrees or less");

			// 2. Ground puts a wide object down by its base: on the slope of a hill, its low side doesn't stand in the air
			IslandGenerator.GenerateInEditor(IslandGenSettings.FromText("Seed=5\nRadius=60\nHeight=26\nRoughness=0.2\nPeaks=1\nShape=0\nObjectDensity=0"));
			yield return null;
			Vector2 mid = EditorLandCentre();
			Vector3 slope = Vector3.zero;
			float steepest = 0f;
			for (float x = -30f; x <= 30f; x += 3f)
				for (float z = -30f; z <= 30f; z += 3f)
				{
					Vector3 point, normal;
					if (PlacementOptions.GroundAt(new Vector3(mid.x + x, 0f, mid.y + z), out point, out normal) && point.y > sea + 3f && Vector3.Angle(normal, Vector3.up) > steepest && Vector3.Angle(normal, Vector3.up) < 35f) { steepest = Vector3.Angle(normal, Vector3.up); slope = point; }
				}
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Van_1" });
			GameObject van = PlaceableCatalog.Spawn("Van_1", GameObject.Find("PlacedObjects").transform);
			if (van == null) { Fail("grounding test: no Van_1"); yield break; }
			EditorGameObject.Attach(van, "Van_1");
			van.transform.position = slope + Vector3.up * 30f;
			RuntimeGizmos.TransformGizmo gizmo = DynamicIslands.EditorGizmoHandler;
			gizmo.ClearTargets(false);
			gizmo.AddTarget(van.transform, false);
			PlacementOptions.AlignToSlope = false;
			PlacementOptions.DropSelectionToGround();
			gizmo.ClearTargets(false);
			Physics.SyncTransforms();
			float under = PlacementOptions.LowestGroundUnder(van);
			// (its bottom, not its pivot: the van's pivot is in its middle, 1.5 m up, and Ground rests such an object on its
			// bottom - RestingPivotY, 6494a3d; set down by its pivot it sank 1.5 m into the slope)
			float bottom = van.transform.position.y - PlacementOptions.PivotAboveBottom(van);
			Vector3 at;
			Vector3 up;
			PlacementOptions.GroundAt(van.transform.position, out at, out up);
			Check(ref ok, Mathf.Abs(bottom - under) < 0.05f && bottom < at.y - 0.1f, "Ground on a " + steepest.ToString("F0") + " degree slope puts the van down by its base: its bottom " + (at.y - bottom).ToString("F2") + " m below the ground under its middle, at the lowest ground under it (" + (bottom - under).ToString("F2") + ")");

			// 3. A brush stroke lowers the ground under it: the van goes down with it; one undo puts both back
			float before = van.transform.position.y;
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Lower;
			terraineditor.brushRadius = 12f;
			terraineditor.strength = 6f;
			terraineditor editor = UnityEngine.Object.FindObjectOfType<terraineditor>();
			editor.SimulateStroke(van.transform.position, 20, 0.05f);
			yield return null;
			float lowered = van.transform.position.y;
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			Check(ref ok, lowered < before - 1f && Mathf.Abs(van.transform.position.y - before) < 0.01f, "lowering the ground under it took the van down " + (before - lowered).ToString("F2") + " m with it; Ctrl+Z put the ground and the van back (" + (van.transform.position.y - before).ToString("F3") + ")");
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.Raise;
			UnityEngine.Object.Destroy(van);

			// 4. The save's settle step (11-21): a boulder 0.7 m in the air over flat ground goes down onto it; a barrel buried
			// 0.4 m under the ground goes up onto it
			Vector3 flat = Vector3.zero;
			for (float x = -30f; x <= 30f && flat == Vector3.zero; x += 3f)
				for (float z = -30f; z <= 30f; z += 3f)
				{
					Vector3 point, normal;
					if (PlacementOptions.GroundAt(new Vector3(mid.x + x, 0f, mid.y + z), out point, out normal) && point.y > sea + 3f && Vector3.Angle(normal, Vector3.up) < 6f) { flat = point; break; }
				}
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "SmallBoulder2", "TP_Moontown_Barrel01" });
			GameObject boulder = PlaceableCatalog.Spawn("SmallBoulder2", GameObject.Find("PlacedObjects").transform);
			GameObject barrel = PlaceableCatalog.Spawn("TP_Moontown_Barrel01", GameObject.Find("PlacedObjects").transform);
			GameObject treasure = PlaceableCatalog.Spawn(ContentCatalog.BuriedTreasure, GameObject.Find("PlacedObjects").transform);
			if (flat == Vector3.zero || boulder == null || barrel == null || treasure == null) { Fail("grounding test: no flat ground, SmallBoulder2, TP_Moontown_Barrel01 or " + ContentCatalog.BuriedTreasure); yield break; }
			EditorGameObject.Attach(boulder, "SmallBoulder2");
			EditorGameObject.Attach(barrel, "TP_Moontown_Barrel01");
			EditorGameObject.Attach(treasure, ContentCatalog.BuriedTreasure);
			boulder.transform.position = flat;
			barrel.transform.position = flat + new Vector3(6f, 0f, 0f);
			treasure.transform.position = flat + new Vector3(0f, -0.6f, 6f);
			Vector3 treasureAt = treasure.transform.position;
			Physics.SyncTransforms();
			Bounds rb = MeshBox(boulder), bb = MeshBox(barrel);
			boulder.transform.position += Vector3.up * (GroundY(rb.center.x, rb.center.z) + 0.7f - rb.min.y);
			barrel.transform.position += Vector3.up * (GroundY(bb.center.x, bb.center.z) - 0.4f - bb.max.y);
			Physics.SyncTransforms();
			int seen, high; float most, least;
			bool floatedBefore = RockFloats(boulder.transform, MeshBox(boulder), ground, out seen, out high, out most, out least);
			SettleAtSave(sea);
			Physics.SyncTransforms();
			rb = MeshBox(boulder); bb = MeshBox(barrel);
			bool floatsAfter = RockFloats(boulder.transform, rb, ground, out seen, out high, out most, out least);
			Check(ref ok, floatedBefore && !floatsAfter && boulder.activeSelf, "the save sets a boulder in the air down: it floated " + floatedBefore + ", after the settle it floats " + floatsAfter + " (shown " + boulder.activeSelf + ", its underside " + (rb.min.y - GroundY(rb.center.x, rb.center.z)).ToString("F2") + " m over the ground)");
			Check(ref ok, bb.max.y > GroundY(bb.center.x, bb.center.z) + 0.05f, "the save sets a buried barrel up on the ground: its top " + (bb.max.y - GroundY(bb.center.x, bb.center.z)).ToString("F2") + " m over it");
			Check(ref ok, (treasure.transform.position - treasureAt).magnitude < 0.01f, "the save leaves buried treasure under the ground: moved " + (treasure.transform.position - treasureAt).magnitude.ToString("F2") + " m");
			// (a barrel on a den's floor 1 m under the land round it stays on the floor: the land there is the room's, cut in the
			// world - Old Mine Islet's carts and crates were set up onto it and floated, c3z40 11-21)
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "BalboaCave_DeadEnd" });
			GameObject mineDen = PlaceableCatalog.Spawn("BalboaCave_DeadEnd", GameObject.Find("PlacedObjects").transform);
			GameObject denBarrel = PlaceableCatalog.Spawn("TP_Moontown_Barrel01", GameObject.Find("PlacedObjects").transform);
			if (mineDen == null || denBarrel == null) { Fail("grounding test: no BalboaCave_DeadEnd"); yield break; }
			EditorGameObject.Attach(mineDen, "BalboaCave_DeadEnd");
			EditorGameObject.Attach(denBarrel, "TP_Moontown_Barrel01");
			foreach (Collider c in mineDen.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) c.enabled = true;
			mineDen.transform.position = flat + new Vector3(-14f, 0f, 14f);
			Physics.SyncTransforms();
			var denOwn = new HashSet<Collider>(mineDen.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.name != CaveTool.MouthName && c.name != CaveTool.JoinName));
			Bounds mineBox = denOwn.Count > 0 ? denOwn.First().bounds : new Bounds(mineDen.transform.position, Vector3.zero);
			foreach (Collider c in denOwn) mineBox.Encapsulate(c.bounds);
			// (the mineDen's floor: the room point with the most room over a floor near the middle of the piece)
			Vector3 denFloor = Vector3.zero; float bestRoom = 0f;
			for (float fx = 0.2f; fx <= 0.8f; fx += 0.1f)
				for (float fz = 0.2f; fz <= 0.8f; fz += 0.1f)
					for (float fy = 0.15f; fy <= 0.6f; fy += 0.15f)
					{
						Vector3 p = new Vector3(Mathf.Lerp(mineBox.min.x, mineBox.max.x, fx), Mathf.Lerp(mineBox.min.y, mineBox.max.y, fy), Mathf.Lerp(mineBox.min.z, mineBox.max.z, fz));
						float fUnder, fOver;
						bool backs = Physics.queriesHitBackfaces;
						bool inside = CaveGround.Inside(p, denOwn, mineBox, out fUnder, out fOver);
						Physics.queriesHitBackfaces = backs;
						if (inside && fUnder < 1.5f && fOver > 2.5f && fOver > bestRoom) { bestRoom = fOver; denFloor = p - Vector3.up * fUnder; }
					}
			Check(ref ok, denFloor != Vector3.zero, "the test den has a floor to stand a barrel on (" + denOwn.Count + " colliders, room over it " + bestRoom.ToString("F2") + " m)");
			if (denFloor != Vector3.zero)
			{
				denBarrel.transform.position = denFloor;
				Physics.SyncTransforms();
				Bounds db = MeshBox(denBarrel);
				denBarrel.transform.position += Vector3.up * (denFloor.y - db.min.y);
				// (the den and its barrel moved up or down together till the barrel's top is 1 m under the land there)
				Physics.SyncTransforms();
				db = MeshBox(denBarrel);
				Vector3 shift = Vector3.up * (GroundY(db.center.x, db.center.z) - 1f - db.max.y);
				mineDen.transform.position += shift;
				denBarrel.transform.position += shift;
				Physics.SyncTransforms();
				Vector3 denBarrelAt = denBarrel.transform.position;
				db = MeshBox(denBarrel);
				float buried = GroundY(db.center.x, db.center.z) - db.max.y;
				SettleAtSave(sea);
				Physics.SyncTransforms();
				float moved = (denBarrel.transform.position - denBarrelAt).magnitude;
				Check(ref ok, moved < 0.01f && denBarrel.activeSelf, "the save leaves a barrel on a den's floor under the land round it (its top " + buried.ToString("F2") + " m under the land before the den's cut): moved " + moved.ToString("F2") + " m, shown " + denBarrel.activeSelf);
			}
			mineDen.SetActive(false);
			int denDug;
			CaveGround.EditorNow(ground, out denDug);
			UnityEngine.Object.Destroy(denBarrel);
			UnityEngine.Object.Destroy(mineDen);
			// (a deck 6 m up over a lower floor 1 m up: its posts stand on that floor's top, not left out - 11-21 c3y11, Engine Court's
			// ridge deck over the court's posts stood on its ladders alone)
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Block_Floor_Wood", "RT_Pillar" });
			Transform root = GameObject.Find("PlacedObjects").transform;
			var pillarsBefore = new HashSet<GameObject>(root.GetComponentsInChildren<EditorGameObject>().Where(x => x.GameObjectName == "RT_Pillar").Select(x => x.gameObject));
			GameObject deckHi = PlaceableCatalog.Spawn("Block_Floor_Wood", root), deckLo = PlaceableCatalog.Spawn("Block_Floor_Wood", root);
			if (deckHi == null || deckLo == null) { Fail("grounding test: no Block_Floor_Wood"); yield break; }
			EditorGameObject.Attach(deckHi, "Block_Floor_Wood");
			EditorGameObject.Attach(deckLo, "Block_Floor_Wood");
			foreach (GameObject f in new[] { deckHi, deckLo }) { Collider fc = f.GetComponent<Collider>(); if (fc != null) fc.enabled = true; }
			Vector3 deckAt = flat + new Vector3(-6f, 0f, -6f);
			deckHi.transform.position = deckAt;
			deckLo.transform.position = deckAt + new Vector3(0.6f, 0f, 0f);
			Physics.SyncTransforms();
			Bounds hb = MeshBox(deckHi), lb = MeshBox(deckLo);
			deckHi.transform.position += Vector3.up * (GroundY(hb.center.x, hb.center.z) + 6f - hb.min.y);
			deckLo.transform.position += Vector3.up * (GroundY(lb.center.x, lb.center.z) + 1f - lb.min.y);
			Physics.SyncTransforms();
			hb = MeshBox(deckHi); lb = MeshBox(deckLo);
			StandPosts();
			Physics.SyncTransforms();
			List<GameObject> newPillars = root.GetComponentsInChildren<EditorGameObject>().Where(x => x.GameObjectName == "RT_Pillar" && !pillarsBefore.Contains(x.gameObject)).Select(x => x.gameObject).ToList();
			int onLow = newPillars.Count(x => { Bounds pb = MeshBox(x); return Mathf.Abs(pb.max.y - hb.min.y) < 0.15f && Mathf.Abs(pb.min.y - lb.max.y) < 0.15f; });
			int deckPosts = newPillars.Count(x => { Bounds pb = MeshBox(x); return Mathf.Abs(pb.max.y - hb.min.y) < 0.15f; });
			Check(ref ok, onLow >= 1 && deckPosts >= 4, "a deck 6 m up over a lower floor gets its posts, those over the floor on its top: " + deckPosts + " posts under the deck, " + onLow + " on the lower floor");
			foreach (GameObject x in newPillars) UnityEngine.Object.Destroy(x);
			UnityEngine.Object.Destroy(deckHi);
			UnityEngine.Object.Destroy(deckLo);
			// (a raised zipline deck stands its own four posts into the ground when it is made - 11-21 c3y11/c3y12)
			var pillarsBefore2 = new HashSet<GameObject>(root.GetComponentsInChildren<EditorGameObject>().Where(x => x.GameObjectName == "RT_Pillar").Select(x => x.gameObject));
			GameObject zipDeck = PlaceableCatalog.Spawn("Block_Floor_Wood", root);
			if (zipDeck == null) { Fail("grounding test: no Block_Floor_Wood"); yield break; }
			EditorGameObject.Attach(zipDeck, "Block_Floor_Wood");
			zipDeck.transform.position = flat + new Vector3(-6f, 0f, 6f);
			Physics.SyncTransforms();
			Bounds zb = MeshBox(zipDeck);
			zipDeck.transform.position += Vector3.up * (GroundY(zb.center.x, zb.center.z) + 4f - zb.min.y);
			Physics.SyncTransforms();
			zb = MeshBox(zipDeck);
			string zipPosts = ZipDeckPosts(zb, root);
			Physics.SyncTransforms();
			List<GameObject> zipPillars = root.GetComponentsInChildren<EditorGameObject>().Where(x => x.GameObjectName == "RT_Pillar" && !pillarsBefore2.Contains(x.gameObject)).Select(x => x.gameObject).ToList();
			int grounded = zipPillars.Count(x => { Bounds pb = MeshBox(x); return Mathf.Abs(pb.max.y - zb.min.y) < 0.15f && pb.min.y < GroundY(pb.center.x, pb.center.z); });
			Check(ref ok, grounded == 4, "a raised zipline deck 4 m up stands four posts into the ground: " + grounded + " (" + zipPosts.TrimStart(',', ' ') + ")");
			foreach (GameObject x in zipPillars) UnityEngine.Object.Destroy(x);
			UnityEngine.Object.Destroy(zipDeck);
			UnityEngine.Object.Destroy(boulder);
			UnityEngine.Object.Destroy(barrel);
			UnityEngine.Object.Destroy(treasure);
			// 5. A zipline rope over a cave piece is measured over the hill a world builds over it (11-21 c3z9: Caisson Yard's summit
			// rope ran 0.32 m into the world's hill and stopped riders 6.5 m short; the editor and the ground check measured the land only)
			yield return null;
			GameObject den = PlaceableCatalog.Spawn(CaveTool.Room, root);
			if (den == null) { Fail("grounding test: no " + CaveTool.Room); yield break; }
			EditorGameObject.Attach(den, CaveTool.Room);
			Vector3 dp = flat; // (the test's other things are gone by now)
			den.transform.position = new Vector3(dp.x, GroundY(dp.x, dp.z), dp.z);
			Physics.SyncTransforms();
			Bounds denBox = MeshBox(den);
			GameObject hill = CaveMountain.Preview(new[] { den.GetComponent<EditorGameObject>() }, terraineditor.terrain.transform.position);
			Check(ref ok, hill != null && hill.transform.Find(CaveMountain.SkinName) != null, "the editor builds the cave hill a world builds over a bear den, to measure ropes against");
			if (hill != null)
			{
				float cx = denBox.center.x, cz = denBox.center.z, landY = GroundY(cx, cz);
				List<Collider> skins = ZiplineEnds.CaveSkins(hill.transform);
				Func<float, float, float> landOnly = (x, z) => GroundY(x, z), withHill = ZiplineEnds.OverSkins(landOnly, skins);
				float hillY = withHill(cx, cz), zipY = ZipGround(null)(cx, cz);
				Check(ref ok, skins.Count == 1 && hillY > landY + 1f, "the hill's skin over the den's middle stands over the land: " + Num(hillY - landY) + " m");
				Check(ref ok, Mathf.Abs(zipY - hillY) < 0.3f, "zipto's ground there is the hill's top: " + Num(zipY - landY) + " m over the land (the skin " + Num(hillY - landY) + ")");
				// (a rope 0.5 m under the hill's top over the den: in the ground by the hill, clear of the land)
				var rope = new List<Vector3> { new Vector3(cx - 3f, hillY - 0.5f, cz), new Vector3(cx + 3f, hillY - 0.5f, cz) };
				Vector3 la; float lt;
				float overLand = ZiplineEnds.RopeLowest(rope, rope[0], rope[1], landOnly, 0f, 0f, null, out la, out lt);
				float overHill = ZiplineEnds.RopeLowest(rope, rope[0], rope[1], withHill, 0f, 0f, null, out la, out lt);
				Check(ref ok, overHill < 0f && overLand > overHill + 0.5f, "a rope through the cave hill counts as in the ground: " + Num(overHill) + " m over the hill (" + Num(overLand) + " m over the land alone)");
				UnityEngine.Object.DestroyImmediate(hill);
				Physics.SyncTransforms();
				Check(ref ok, GameObject.Find(CaveMountain.PreviewName) == null && skins.All(c => c == null), "the measuring hill is gone after (zipto's ground there now " + Num(ZipGround(null)(cx, cz) - landY) + " m over the land)");
			}
			UnityEngine.Object.Destroy(den);
			// 6. zipto when no raise nor tighter rope clears (11-21 c3z11: Caisson Yard's ridge line was left at Raft's slack, 3.2 m
			// into the hill, where a partial raise had nearly cleared it): the best partial raise and rope, more room than as it is
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "ZiplinePath_Landmark" });
			GameObject zl = PlaceableCatalog.Spawn("ZiplinePath_Landmark", root);
			MeshPath_Zipline_Landmark zline = zl != null ? zl.GetComponentInChildren<MeshPath_Zipline_Landmark>(true) : null;
			if (zline == null) { Fail("grounding test: no ZiplinePath_Landmark line"); yield break; }
			Vector3 zn = new Vector3(flat.x, GroundY(flat.x, flat.z) + 3f, flat.z), zf = new Vector3(flat.x + 24f, GroundY(flat.x + 24f, flat.z), flat.z);
			Vector2 zm = new Vector2(flat.x + 12f, flat.z);
			// (a wall 5 m thick midway, 3 m over the highest the near end can go: nothing clears it)
			float wallTop = Mathf.Max(zn.y + ZiplineEnds.MaxRaise, zf.y) + 3f;
			Func<float, float, float> wall = (x, z) => Vector2.Distance(new Vector2(x, z), zm) < 2.5f ? wallTop : GroundY(x, z);
			float zown = ZiplineEnds.Slack(zline);
			float[] zsl = !float.IsNaN(zown) && zown != 0f ? new[] { zown, zown * 0.5f, zown * 0.25f, zown * 0.1f } : new[] { zown };
			Vector3 za; float zt;
			float asIs = ZiplineEnds.RopeLowest(ZiplineEnds.RopeBetween(zline, zf, zn, zown), zn, zf, wall, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, null, out za, out zt);
			float pr, ps, pl; Vector3 pa;
			ZipBestPartial(zline, zn, zf, wall, zsl, ZiplineEnds.MaxRaise, out pr, out ps, out pl, out pa);
			Check(ref ok, pl < ZiplineEnds.LineClear && pl > asIs + 1f && (pr > 0f || ps != zown), "a rope through a wall no raise clears gets the most room it can: raised " + Num(pr) + " m, slack " + Num(ps) + " (own " + Num(zown) + "), the least " + Num(pl) + " m over the wall's top (" + Num(asIs) + " as it was)");
			// 6b. ...and then its far end goes back along the line, short of a hump the rope runs through (11-21 c3z29: lines on 15
			// islands ran into a hump between their ends, only warned), but not past a hump by its near end
			EditorGameObject ze6 = EditorGameObject.Attach(zl, "ZiplinePath_Landmark");
			if (ze6 != null)
			{
				float g6 = GroundY(flat.x, flat.z);
				Vector3 n6 = new Vector3(flat.x, g6 + 6f, flat.z), fl6 = new Vector3(flat.x + 24f, GroundY(flat.x + 24f, flat.z), flat.z), f6 = fl6 + Vector3.up * 2f;
				PropsCommand.Change(ze6, ObjectProps.With(ze6.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(fl6)));
				Vector3 to6;
				ZiplineEnds.TryParse(ObjectProps.Get(ze6.Props, ZiplineEnds.ZipTo), out to6);
				f6 = to6 + Vector3.up * 2f;
				Vector2 hump6 = new Vector2(flat.x + 18f, flat.z), wall6 = new Vector2(flat.x + 4f, flat.z);
				// (the hump's top over the straight line there, whatever the land does: 4.5 m over the near end's ground left the rope
				// 0.94 m over it where the far end's ground stood higher, c3z47 - the test's rope never ran through it)
				float humpTop = Mathf.Max(g6 + 4.5f, Mathf.Lerp(n6.y, f6.y, 0.75f) + 0.5f);
				Func<float, float, float> humped = (x, z) => Vector2.Distance(new Vector2(x, z), hump6) < 2.5f ? humpTop : GroundY(x, z);
				Func<float, float, float> walled = (x, z) => Vector2.Distance(new Vector2(x, z), wall6) < 2.5f ? n6.y + 3f : GroundY(x, z);
				Vector3 nt6, nf6, a6, za6; float part6, r6, s6, l6, zt6; bool rd6;
				float before6 = ZiplineEnds.RopeLowest(ZiplineEnds.RopeBetween(zline, f6, n6, zsl[zsl.Length - 1]), n6, f6, humped, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, null, out za6, out zt6);
				bool pulled6 = ZipPullIn(ze6, zline, Vector3.zero, n6, f6, humped, zsl, false, out nt6, out nf6, out part6, out r6, out s6, out l6, out a6, out rd6);
				float after6 = pulled6 ? ZiplineEnds.RopeLowest(ZiplineEnds.RopeBetween(zline, nf6, n6, s6), n6, nf6, humped, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, null, out za6, out zt6) : float.NaN;
				Check(ref ok, before6 < 0f && pulled6 && part6 > 0f && part6 <= ZiplineEnds.PullMost + 0.001f && Vector2.Distance(new Vector2(nf6.x, nf6.z), hump6) > 2.5f && nf6.x < hump6.x && after6 >= ZiplineEnds.LineLow &&
					Mathf.Abs(nt6.y - GroundY(nt6.x, nt6.z)) < 0.05f,
					"a rope through a hump by its far end (" + Num(before6) + " m over it at the tightest) gets its far end pulled back " + Num(part6 * 24f) + " m, short of the hump, its floor on the ground (" + Num(nt6.y - GroundY(nt6.x, nt6.z)) + " m), the rope " + Num(after6) + " m over the ground");
				Check(ref ok, !ZipPullIn(ze6, zline, Vector3.zero, n6, f6, walled, zsl, false, out nt6, out nf6, out part6, out r6, out s6, out l6, out a6, out rd6), "a rope through a hump by its near end keeps its far end (no pull clears it)");
			}
			else Check(ref ok, false, "grounding test: no ZiplinePath_Landmark for part 6b");
			UnityEngine.Object.Destroy(zl);
			// 7. A zipto far end over the cave hill goes onto the hill's skin, and the ground check sees a rope's last metres in the
			// ground (11-21 c3z13: Caisson Yard's summit line ended in the hill's sand, its far floor on the land under the skin)
			yield return null;
			GameObject den7 = PlaceableCatalog.Spawn(CaveTool.Room, root);
			GameObject zl7 = PlaceableCatalog.Spawn("ZiplinePath_Landmark", root);
			if (den7 == null || zl7 == null) { Fail("grounding test: no " + CaveTool.Room + " or ZiplinePath_Landmark"); yield break; }
			EditorGameObject.Attach(den7, CaveTool.Room);
			EditorGameObject ze7 = EditorGameObject.Attach(zl7, "ZiplinePath_Landmark");
			den7.transform.position = new Vector3(flat.x, GroundY(flat.x, flat.z), flat.z);
			zl7.transform.position = new Vector3(flat.x + 30f, GroundY(flat.x + 30f, flat.z), flat.z);
			Physics.SyncTransforms();
			Bounds d7 = MeshBox(den7);
			GameObject hill7 = CaveMountain.Preview(new[] { den7.GetComponent<EditorGameObject>() }, terraineditor.terrain.transform.position);
			if (hill7 != null && ze7 != null)
			{
				Physics.SyncTransforms();
				Vector3 org = terraineditor.terrain.transform.position;
				float fx = d7.center.x, fz = d7.center.z, land7 = GroundY(fx, fz), skin7 = ZiplineEnds.OverSkins((x, z) => GroundY(x, z), ZiplineEnds.CaveSkins(hill7.transform))(fx, fz);
				PropsCommand.Change(ze7, ObjectProps.With(ze7.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(new Vector3(fx, land7, fz) - org)));
				bool moved = ZipFarOnSkin(ze7, org);
				Vector3 to7;
				float now7 = ZiplineEnds.TryParse(ObjectProps.Get(ze7.Props, ZiplineEnds.ZipTo), out to7) ? to7.y + org.y : float.NaN;
				Check(ref ok, moved && skin7 > land7 + 1f && Mathf.Abs(now7 - skin7) < 0.1f, "a zipto far end under the cave hill's skin goes onto it: " + Num(now7 - land7) + " m over the land (the skin " + Num(skin7 - land7) + ")");
				Check(ref ok, !ZipFarOnSkin(ze7, org), "a zipto far end on the skin stays there");
				// 9. Its rope's last metres clear of the hill's slope (11-21 c3z15: Caisson Yard's summit rope, its far end on the skin,
				// ran 0.24 m into the dune just before it): the near end as high as the hill's top, its rope sagging into the slope
				// before the far end; zipto raises the far end on a deck till the last metres clear it, as the ground check measures
				zl7.transform.position += Vector3.up * (skin7 - zl7.transform.position.y);
				Physics.SyncTransforms();
				Func<float, float, float> g9 = ZiplineEnds.OverSkins((x, z) => GroundY(x, z), ZiplineEnds.CaveSkins(hill7.transform));
				MeshPath_Zipline_Landmark line9 = zl7.GetComponentInChildren<MeshPath_Zipline_Landmark>(true);
				Func<float> end9 = () => { Vector3 n9, f9, a9; Physics.SyncTransforms(); return line9 != null && ZiplineEnds.Ends(zl7, ze7.Props, org, out n9, out f9) ? ZiplineEnds.FarStretchLowest(ZiplineEnds.RopeBetween(line9, f9, n9, Slack9(ze7)), n9, f9, g9, out a9) : float.NaN; };
				float before9 = end9();
				GameObject deck9, again9;
				var lad9 = new HashSet<GameObject>(zipDeckLadders);
				string w9 = ZipFarClear(ze7, out deck9);
				float after9 = end9();
				Check(ref ok, before9 < ZiplineEnds.FarEndLow && w9 != null && after9 >= ZiplineEnds.FarEndLow && after9 > before9, "a zipto rope into the hill's slope before its far end: its last " + Num(ZiplineEnds.FarSkip) + " m " + Num(before9) + " -> " + Num(after9) + " m over the ground" + (w9 != null ? " (" + w9.Trim() + ")" : ""));
				Check(ref ok, deck9 != null, "the raised far end stands on a deck");
				Check(ref ok, ZipFarClear(ze7, out again9) == null && again9 == null, "a zipto far end clear stays as it is");
				foreach (GameObject x in zipDeckLadders.Where(y => y != null && !lad9.Contains(y)).ToList()) { zipDeckLadders.Remove(x); UnityEngine.Object.Destroy(x); }
				if (deck9 != null) { recipePlaced.Remove(deck9); UnityEngine.Object.Destroy(deck9); }
				UnityEngine.Object.DestroyImmediate(hill7);
				Physics.SyncTransforms();
			}
			else Check(ref ok, false, "the editor builds the cave hill over a bear den for a zipto far end");
			UnityEngine.Object.Destroy(zl7);
			UnityEngine.Object.Destroy(den7);
			{
				Func<float, float, float> g7 = (x, z) => GroundY(x, z);
				Vector3 n7 = new Vector3(flat.x, GroundY(flat.x, flat.z) + 6f, flat.z), fIn = new Vector3(flat.x + 12f, GroundY(flat.x + 12f, flat.z) - 0.6f, flat.z), fOn = new Vector3(fIn.x, fIn.y + 1.6f, fIn.z);
				Vector3 a7;
				float inEnd = ZiplineEnds.FarStretchLowest(new List<Vector3> { fIn, n7 }, n7, fIn, g7, out a7), onEnd = ZiplineEnds.FarStretchLowest(new List<Vector3> { fOn, n7 }, n7, fOn, g7, out a7);
				Check(ref ok, inEnd < ZiplineEnds.FarEndLow && onEnd >= ZiplineEnds.FarEndLow, "the ground check sees a rope's last " + Num(ZiplineEnds.FarSkip) + " m: ending 0.6 m in the ground " + Num(inEnd) + ", ending 1 m over it " + Num(onEnd));
				// 10. A rope touching its own far floor by a few mm is no cut (11-21 c3z17/18: Thalassa Mooring 0.0096 m, Engine Court
				// 0.0038 m under at the floor itself): the ground check passes it, a 0.24 m cut still fails (c3z15 Caisson Yard)
				Vector3 fTouch = new Vector3(fIn.x, fIn.y + 0.6f - 0.01f, fIn.z), fCut = new Vector3(fIn.x, fIn.y + 0.6f - 0.24f, fIn.z);
				float touchEnd = ZiplineEnds.FarStretchLowest(new List<Vector3> { fTouch, n7 }, n7, fTouch, g7, out a7), cutEnd = ZiplineEnds.FarStretchLowest(new List<Vector3> { fCut, n7 }, n7, fCut, g7, out a7);
				Check(ref ok, touchEnd >= ZiplineEnds.FarEndLow && cutEnd < ZiplineEnds.FarEndLow, "the ground check: a rope touching its far floor " + Num(touchEnd) + " m passes, one 0.24 m in " + Num(cutEnd) + " fails");
			}
			// 10c. A rider's room never asks less than the ground check's rope room outside the last FarSkip m (11-21 c3z30: The Rain
			// Engine's raised lines, their last 5 m left out of it, came 0.03 and 0.35 m over the ground 3.2 m before their far ends)
			{
				float wFlat = 999f, wFall = 999f;
				Func<float, float> rwFlat = ZiplineEnds.RiderWant(0f, 20f), rwFall = ZiplineEnds.RiderWant(2.2f, 16f);
				for (float d = ZiplineEnds.FarSkip; d <= 12f; d += 0.25f) { wFlat = Mathf.Min(wFlat, rwFlat(d)); wFall = Mathf.Min(wFall, rwFall(d)); }
				Check(ref ok, wFlat > ZiplineEnds.LineLow && wFall > ZiplineEnds.LineLow && Mathf.Abs(rwFlat(20f) - ZiplineEnds.RiderClear) < 0.01f,
					"a rider's room asks at least the rope's " + Num(ZiplineEnds.LineLow) + " m from " + Num(ZiplineEnds.FarSkip) + " m before the far end: a flat line " + Num(wFlat) + ", Rain Engine's east line's fall " + Num(wFall) + " (full " + Num(rwFlat(20f)) + " m further back)");
			}
			// 10b. ... and zipto builds no deck for a flat-ground far end whose rope touches its floor by a few mm
			yield return null;
			GameObject zl10 = PlaceableCatalog.Spawn("ZiplinePath_Landmark", root);
			EditorGameObject ze10 = zl10 != null ? EditorGameObject.Attach(zl10, "ZiplinePath_Landmark") : null;
			if (ze10 != null)
			{
				Vector3 org = terraineditor.terrain.transform.position;
				zl10.transform.position = new Vector3(flat.x + 30f, GroundY(flat.x + 30f, flat.z) + 4f, flat.z);
				Physics.SyncTransforms();
				MeshPath_Zipline_Landmark line10 = zl10.GetComponentInChildren<MeshPath_Zipline_Landmark>(true);
				Func<float, float, float> g10 = ZipGround(ze10);
				Vector3 to10 = new Vector3(flat.x + 12f, GroundY(flat.x + 12f, flat.z), flat.z) - org;
				Func<float> end10 = () => { Vector3 n, f, a; Physics.SyncTransforms(); return line10 != null && ZiplineEnds.Ends(zl10, ze10.Props, org, out n, out f) ? ZiplineEnds.FarStretchLowest(ZiplineEnds.RopeBetween(line10, f, n), n, f, g10, out a) : float.NaN; };
				// (the far end set so its rope touches the ground 8 mm under, as Raft's own floor contact)
				for (int i = 0; i < 3; i++)
				{
					PropsCommand.Change(ze10, ObjectProps.With(ze10.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(to10)));
					float l10 = end10();
					if (float.IsNaN(l10) || Mathf.Abs(l10 + 0.008f) < 0.002f) break;
					to10.y -= l10 + 0.008f;
				}
				PropsCommand.Change(ze10, ObjectProps.With(ze10.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(to10)));
				float touch10 = end10();
				string set10 = ObjectProps.Get(ze10.Props, ZiplineEnds.ZipTo);
				GameObject deck10;
				var lad10 = new HashSet<GameObject>(zipDeckLadders);
				string w10 = ZipFarClear(ze10, out deck10);
				Check(ref ok, touch10 < 0f && touch10 >= ZiplineEnds.FarEndLow && w10 == null && deck10 == null && ObjectProps.Get(ze10.Props, ZiplineEnds.ZipTo) == set10, "a flat-ground zipto far end whose rope touches its floor " + Num(touch10) + " m keeps its place, no deck" + (w10 != null ? " (" + w10.Trim() + ")" : ""));
				foreach (GameObject x in zipDeckLadders.Where(y => y != null && !lad10.Contains(y)).ToList()) { zipDeckLadders.Remove(x); UnityEngine.Object.Destroy(x); }
				if (deck10 != null) { recipePlaced.Remove(deck10); UnityEngine.Object.Destroy(deck10); }
			}
			else Check(ref ok, false, "grounding test: no ZiplinePath_Landmark for part 10");
			if (zl10 != null) UnityEngine.Object.Destroy(zl10);
			// 10d. A near floor buried in the land goes up onto it before the rope is measured (11-21 c3z30: The Rootcore's
			// mouth-ledge line, its floor 8.3 m under the ledge, came out on a deck 0.6 m under the ground, its rope through the ledge)
			yield return null;
			GameObject zl11 = PlaceableCatalog.Spawn("ZiplinePath_Landmark", root);
			EditorGameObject ze11 = zl11 != null ? EditorGameObject.Attach(zl11, "ZiplinePath_Landmark") : null;
			if (ze11 != null)
			{
				zl11.transform.position = new Vector3(flat.x + 30f, GroundY(flat.x + 30f, flat.z) + 1.35f, flat.z);
				Physics.SyncTransforms();
				Vector3 nf0 = ZiplineEnds.NearFloor(zl11) ?? zl11.transform.position;
				float sink = 3f + (nf0.y - GroundY(nf0.x, nf0.z));
				zl11.transform.position += Vector3.down * sink;
				Physics.SyncTransforms();
				Vector3 nf1 = ZiplineEnds.NearFloor(zl11) ?? zl11.transform.position;
				float under1 = GroundY(nf1.x, nf1.z) - nf1.y;
				var placed11 = new HashSet<GameObject>(recipePlaced);
				var lad11 = new HashSet<GameObject>(zipDeckLadders);
				float lift11 = ZipNearOutOfLand(ze11, terraineditor.terrain);
				Vector3 nf2 = ZiplineEnds.NearFloor(zl11) ?? zl11.transform.position;
				float under2 = GroundY(nf2.x, nf2.z) - nf2.y;
				Check(ref ok, under1 > 2.5f && lift11 > 2.5f && Mathf.Abs(under2) < 0.1f, "zipto lifts a near floor buried " + Num(under1) + " m in the land onto it: lifted " + Num(lift11) + " m, now " + Num(-under2) + " m over it");
				// (and one on the land is left where it is)
				Check(ref ok, ZipNearOutOfLand(ze11, terraineditor.terrain) == 0f, "zipto leaves a near floor on the land where it is");
				foreach (GameObject x in recipePlaced.Where(y => y != null && !placed11.Contains(y)).ToList()) { recipePlaced.Remove(x); UnityEngine.Object.Destroy(x); }
				foreach (GameObject x in zipDeckLadders.Where(y => y != null && !lad11.Contains(y)).ToList()) { zipDeckLadders.Remove(x); UnityEngine.Object.Destroy(x); }
			}
			else Check(ref ok, false, "grounding test: no ZiplinePath_Landmark for part 10d");
			if (zl11 != null) UnityEngine.Object.Destroy(zl11);
			// 8. zipto's deck ladders: a boulder of the generator's on a ladder's climbing side is removed (11-21 c3z13: Caisson Yard's
			// ridge deck ladder shut by a SmallBoulder2, CILadderSide FAIL), the recipe's own kept
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Block_Floor_Wood", "Block_Ladder", "SmallBoulder2" });
			GameObject deck8 = PlaceableCatalog.Spawn("Block_Floor_Wood", root);
			GameObject rock8 = PlaceableCatalog.Spawn("SmallBoulder2", root);
			if (deck8 == null || rock8 == null) { Fail("grounding test: no Block_Floor_Wood or SmallBoulder2"); yield break; }
			EditorGameObject.Attach(deck8, "Block_Floor_Wood");
			EditorGameObject.Attach(rock8, "SmallBoulder2");
			foreach (Collider c in rock8.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) c.enabled = true;
			Vector3 p8 = flat + new Vector3(8f, 0f, -8f);
			deck8.transform.position = p8;
			Physics.SyncTransforms();
			Bounds b8 = MeshBox(deck8);
			deck8.transform.position += Vector3.up * (GroundY(b8.center.x, b8.center.z) + 3f - b8.min.y);
			Physics.SyncTransforms();
			b8 = MeshBox(deck8);
			// (the ladders go up its +x side: a boulder 1 m out from that edge, at the ground)
			Bounds r8 = MeshBox(rock8);
			Vector3 at8 = new Vector3(b8.max.x + 1f, 0f, b8.center.z);
			rock8.transform.position += new Vector3(at8.x - r8.center.x, GroundY(at8.x, at8.z) + 0.3f - r8.min.y, at8.z - r8.center.z);
			Physics.SyncTransforms();
			var ladBefore = new HashSet<GameObject>(zipDeckLadders);
			string lad8 = ZipDeckLadders(b8, Vector3.right, root);
			List<GameObject> made8 = zipDeckLadders.Where(x => x != null && !ladBefore.Contains(x)).ToList();
			Check(ref ok, made8.Count > 0 && !rock8.activeInHierarchy, "a deck ladder's climbing side is cleared of the generator's boulder: " + (rock8.activeInHierarchy ? "still there" : "removed") + " (" + lad8.TrimStart(',', ' ') + ")");
			foreach (GameObject x in made8) { zipDeckLadders.Remove(x); UnityEngine.Object.Destroy(x); }
			UnityEngine.Object.Destroy(rock8);
			UnityEngine.Object.Destroy(deck8);
			if (ok) Log("PASS: grounding test"); else Fail("grounding test");
		}

		/// <summary>CIFloating's lines of detail per kind (the sweep logs fewer), and its last run's count of problems.</summary>
		static int floatDetail = 30, floatProblems;
		/// <summary>The sweep: CIFloating's lines are kept here instead of logged.</summary>
		static List<string> floatLines;

		static void FLog(string line) { if (floatLines != null) floatLines.Add(line); else Log(line); }

		[ConsoleCommand(name: "CIFloatingSweep", docs: "Dev, editor: CIFloating on every saved island (ROADMAP CA30) - each opened in the editor and measured; the islands with problems are named with their first <details> (3) lines; all of them in Mods\\DynamicIslands\\floating_sweep.txt. Leaves out the test islands (ci...), the world-kept copies and the randomizer's extras (rnd-...). CIFloatingSweep [part of the name|*] [details]")]
		public static void FloatingSweepCommand(string[] args)
		{
			// (a name may have spaces: "CIFloatingSweep Old Vine" read "Vine" as the details count and threw - hollow-tests 11-24;
			//  a number last is the details count, the words before it the name)
			var parts = args ?? new string[0];
			int details = 3, n;
			if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out n)) { details = n; parts = parts.Take(parts.Length - 1).ToArray(); }
			string only = parts.Length > 0 && parts[0] != "*" ? string.Join(" ", parts) : "";
			StartTest(FloatingSweepRoutine(only, details));
		}

		static IEnumerator FloatingSweepRoutine(string only, int details)
		{
			yield return WaitForEditor(false);
			var kept = new Regex("_[0-9a-f]{12}$");
			List<string> names = IslandSpawner.ListSavedIslands().Where(n => !n.StartsWith("ci", StringComparison.Ordinal) && !kept.IsMatch(n) &&
				// (the randomizer's extras lie over Raft's own islands, whose land is not in the file: their finds stand on it)
				!n.StartsWith(WorldRandomizer.ExtrasPrefix, StringComparison.OrdinalIgnoreCase) &&
				(only.Length == 0 || n.IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(n => n).ToList();
			var report = new StringBuilder();
			int bad = 0, problems = 0;
			var unread = new List<string>();
			foreach (string name in names)
			{
				DynamicIslands.currentIslandName = "";
				bool loaded = false;
				try { loaded = DynamicIslands.LoadIsland(name); } catch (Exception e) { Log("  " + name + ": loading failed: " + e.Message); unread.Add(name); continue; }
				if (!loaded) { Log("  " + name + ": the editor didn't load it"); unread.Add(name); continue; }
				for (float t = 0; t < 120f && DynamicIslands.currentIslandName != name; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
				if (DynamicIslands.currentIslandName != name) { Log("  " + name + ": still not loaded after 2 minutes"); unread.Add(name); continue; }
				yield return null; yield return null;
				var lines = new List<string>();
				floatLines = lines;
				floatDetail = 1000;
				try { FloatingCommand(null); }
				catch (Exception e) { lines.Add("FAIL: " + e.Message); floatProblems = 1; }
				finally { floatLines = null; floatDetail = 30; }
				report.AppendLine(name + ": " + floatProblems);
				foreach (string l in lines) report.AppendLine("  " + l);
				if (floatProblems == 0) continue;
				bad++;
				problems += floatProblems;
				Log("  " + name + ": " + floatProblems);
				foreach (string l in lines.Where(l => l.StartsWith("  ")).Take(details)) Log("  " + l);
			}
			File.WriteAllText(Path.Combine(DynamicIslands.assetpath, "floating_sweep.txt"), report.ToString());
			Log((bad == 0 && unread.Count == 0 && names.Count > 0 ? "PASS" : "FAIL") + ": floating sweep - " + bad + " of " + names.Count + " islands with " + problems + " problems (floating_sweep.txt)" +
				(unread.Count > 0 ? "; not measured: " + string.Join(", ", unread.ToArray()) : ""));
		}

		[ConsoleCommand(name: "CIRemoveBuried", docs: "Dev, editor: takes out the objects buried whole under the ground (CIFloating's \"buried\" ones: nobody sees them) of the named islands and saves each; the old file goes to Mods\\DynamicIslands\\deleted\\ first (ROADMAP CA30): CIRemoveBuried <island>[,<island>...]")]
		public static void RemoveBuriedCommand(string[] args)
		{
			string all = args != null ? string.Join(" ", args) : "";
			StartTest(RemoveBuriedRoutine(all.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()));
		}

		static IEnumerator RemoveBuriedRoutine(List<string> names)
		{
			yield return WaitForEditor(false);
			string backup = Path.Combine(DynamicIslands.assetpath, "deleted");
			Directory.CreateDirectory(backup);
			foreach (string name in names)
			{
				DynamicIslands.currentIslandName = "";
				if (!DynamicIslands.LoadIsland(name)) { Fail(name + ": the editor didn't load it"); continue; }
				for (float t = 0; t < 120f && DynamicIslands.currentIslandName != name; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
				if (DynamicIslands.currentIslandName != name) { Fail(name + ": not loaded after 2 minutes"); continue; }
				yield return null; yield return null;
				Terrain ground = terraineditor.terrain;
				Physics.SyncTransforms();
				List<EditorGameObject> placed = PlacedEditorObjects().ToList();
				var buried = new List<EditorGameObject>();
				foreach (EditorGameObject e in placed)
				{
					string n = e.GameObjectName ?? "";
					if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n == ContentCatalog.MarkerOnly || ground == null || n.Contains("Cave") || n.Contains("Tunnel")) continue;
					Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
					if (parts.Length == 0) continue;
					Bounds whole = parts[0].bounds;
					foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
					if (!ShowsAboveGround(whole, ground) && !placed.Any(o => o != e && o.GetComponentsInChildren<Collider>(false).Any(c => !c.isTrigger && c.bounds.Contains(whole.center))))
						buried.Add(e);
				}
				if (buried.Count == 0) { Log("  " + name + ": nothing buried"); continue; }
				string file = IslandSpawner.PathFor(name);
				File.Copy(file, Path.Combine(backup, Path.GetFileNameWithoutExtension(file) + "-buried-" + DateTime.Now.ToString("yyyyMMddHHmmss") + IslandFile.Extension), true);
				foreach (EditorGameObject e in buried) e.gameObject.SetActive(false);
				bool saved = DynamicIslands.SaveIsland(name);
				Log((saved ? "PASS: " : "FAIL: ") + name + ": " + buried.Count + " buried objects taken out (" + string.Join(", ", buried.GroupBy(e => e.GameObjectName).Select(g => g.Key + " x" + g.Count()).ToArray()) + ")");
			}
		}

		[ConsoleCommand(name: "CISettle", docs: "Dev, editor: lowers the rocks, ice spikes and corals of the named islands that hang over the ground (on a slope: their base up to more than 0.6 m above it; a plant or coral whose foot is above it) down onto it - by at most half their height - and saves each; the old file goes to Mods\\DynamicIslands\\deleted\\ first (ROADMAP CA30): CISettle <island>[,<island>...]")]
		public static void SettleCommand(string[] args)
		{
			string all = args != null ? string.Join(" ", args) : "";
			StartTest(SettleRoutine(all.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()));
		}

		static readonly Regex Settles = new Regex("Stalagmite|Rock|Boulder|Coral|Stone", RegexOptions.IgnoreCase);

		static IEnumerator SettleRoutine(List<string> names)
		{
			yield return WaitForEditor(false);
			string backup = Path.Combine(DynamicIslands.assetpath, "deleted");
			Directory.CreateDirectory(backup);
			foreach (string name in names)
			{
				DynamicIslands.currentIslandName = "";
				if (!DynamicIslands.LoadIsland(name)) { Fail(name + ": the editor didn't load it"); continue; }
				for (float t = 0; t < 120f && DynamicIslands.currentIslandName != name; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
				if (DynamicIslands.currentIslandName != name) { Fail(name + ": not loaded after 2 minutes"); continue; }
				yield return null; yield return null;
				Terrain ground = terraineditor.terrain;
				if (ground == null) { Fail(name + ": no ground"); continue; }
				float sea = DynamicIslands.EditorWaterLevel;
				Physics.SyncTransforms();
				var moves = new List<KeyValuePair<EditorGameObject, float>>();
				foreach (EditorGameObject e in PlacedEditorObjects())
				{
					string n = e.GameObjectName ?? "";
					if (!Settles.IsMatch(n) || n.StartsWith("Pickup_") || n.Contains("Cave") || n.Contains("Tunnel")) continue;
					Renderer[] rs = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
					if (rs.Length == 0) continue;
					Bounds b = rs[0].bounds;
					foreach (Renderer r in rs) b.Encapsulate(r.bounds);
					// (as CIFloating: its foot above the ground under it, or its base above the ground somewhere 20 % in from its box)
					float drop = e.transform.position.y - (ground.SampleHeight(e.transform.position) + ground.transform.position.y);
					if (!NotABase.IsMatch(n) || drop <= 0.5f || b.min.y - (ground.SampleHeight(b.center) + ground.transform.position.y) <= 0.5f) drop = 0f;
					RaycastHit under;
					if (drop > 0f && Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.1f, b.center.z), Vector3.down, out under, 0.8f, ~0, QueryTriggerInteraction.Ignore) && under.collider.GetComponent<Terrain>() == null &&
						under.collider.GetComponentInParent<EditorGameObject>() != null && under.collider.GetComponentInParent<EditorGameObject>() != e) drop = 0f;
					else if (drop > 0f) drop = b.min.y - (ground.SampleHeight(b.center) + ground.transform.position.y);
					// (the base check's own limits: its base parts only; not small, flat or under the sea; not on another object)
					float bottom = rs.Min(r => r.bounds.min.y);
					rs = rs.Where(r => r.bounds.min.y < bottom + 0.6f).ToArray();
					b = rs[0].bounds;
					foreach (Renderer r in rs) b.Encapsulate(r.bounds);
					RaycastHit on;
					bool baseChecked = !NotABase.IsMatch(n) && !Hangs.IsMatch(n) && Mathf.Max(b.size.x, b.size.z) >= 1.5f && b.size.y >= 0.6f && b.min.y >= sea - 0.5f &&
						!(Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.1f, b.center.z), Vector3.down, out on, 0.6f, ~0, QueryTriggerInteraction.Ignore) && on.collider.GetComponent<Terrain>() == null &&
						on.collider.GetComponentInParent<EditorGameObject>() != null && on.collider.GetComponentInParent<EditorGameObject>() != e);
					if (!baseChecked) { if (drop > 0f) moves.Add(new KeyValuePair<EditorGameObject, float>(e, Mathf.Min(drop, 0.5f * b.size.y))); continue; }
					int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(b.size.x, b.size.z)), 3, 30);
					float lowest = float.MaxValue, highest = float.MinValue;
					for (int i = 0; i <= steps; i++)
						for (int j = 0; j <= steps; j++)
						{
							var p = new Vector3(Mathf.Lerp(b.min.x + b.size.x * 0.2f, b.max.x - b.size.x * 0.2f, i / (float)steps), 0f, Mathf.Lerp(b.min.z + b.size.z * 0.2f, b.max.z - b.size.z * 0.2f, j / (float)steps));
							float g = b.min.y - (ground.SampleHeight(p) + ground.transform.position.y);
							lowest = Mathf.Min(lowest, g); highest = Mathf.Max(highest, g);
						}
					if (lowest <= 0.4f && highest > 0.6f) drop = Mathf.Max(drop, highest - 0.3f);
					drop = Mathf.Min(drop, 0.5f * b.size.y);
					if (drop > 0.05f) moves.Add(new KeyValuePair<EditorGameObject, float>(e, drop));
				}
				if (moves.Count == 0) { Log("  " + name + ": nothing to settle"); continue; }
				string file = IslandSpawner.PathFor(name);
				File.Copy(file, Path.Combine(backup, Path.GetFileNameWithoutExtension(file) + "-settle-" + DateTime.Now.ToString("yyyyMMddHHmmss") + IslandFile.Extension), true);
				foreach (var m in moves) m.Key.transform.position += Vector3.down * m.Value;
				bool saved = DynamicIslands.SaveIsland(name);
				Log((saved ? "PASS: " : "FAIL: ") + name + ": " + moves.Count + " lowered (" + string.Join(", ", moves.Select(m => m.Key.GameObjectName + " " + Num(m.Value) + " m").ToArray()) + ")");
			}
		}

		[ConsoleCommand(name: "CIReseat", docs: "Dev, editor: <names joined by |, _ for space | *> [most] [skip] [except=names|...] - the saved islands' sunk and floating trees set back on the ground, as a recipe's save does since IG5 (11-18), and saved; the old file goes to 'deleted'")]
		public static void ReseatCommand(string[] args)
		{
			string which = args != null && args.Length > 0 ? args[0] : "*";
			int most = args != null && args.Length > 1 ? int.Parse(args[1]) : 9999, skip = args != null && args.Length > 2 ? int.Parse(args[2]) : 0;
			string except = args != null && args.Length > 3 && args[3].StartsWith("except=") ? args[3].Substring(7) : "";
			Func<string, string[]> split = w => w.Split('|').Select(x => x.Replace('_', ' ').Trim()).Where(x => x.Length > 0).ToArray();
			string[] parts = split(which), outs = split(except);
			var kept = new Regex("_[0-9a-f]{12}$");
			List<string> names = IslandSpawner.ListSavedIslands().Where(n => !n.StartsWith("ci", StringComparison.Ordinal) && !kept.IsMatch(n) &&
				!n.StartsWith(WorldRandomizer.ExtrasPrefix, StringComparison.OrdinalIgnoreCase) && (which == "*" || parts.Any(x => n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) &&
				!outs.Any(x => n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Skip(skip).Take(most).ToList();
			StartTest(ReseatRoutine(names));
		}

		static IEnumerator ReseatRoutine(List<string> names)
		{
			yield return WaitForEditor(false);
			string backup = Path.Combine(DynamicIslands.assetpath, "deleted");
			Directory.CreateDirectory(backup);
			int changed = 0, trees = 0;
			Log("reseat of " + names.Count + " saved islands");
			foreach (string name in names)
			{
				DynamicIslands.currentIslandName = "";
				if (!DynamicIslands.LoadIsland(name)) { Fail(name + ": the editor didn't load it"); continue; }
				for (float t = 0; t < 120f && DynamicIslands.currentIslandName != name; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
				if (DynamicIslands.currentIslandName != name) { Fail(name + ": not loaded after 2 minutes"); continue; }
				yield return null; yield return null;
				Physics.SyncTransforms();
				string file = IslandSpawner.PathFor(name);
				byte[] old = File.ReadAllBytes(file);
				int n = ReseatSunkTrees(DynamicIslands.EditorWaterLevel);
				if (n == 0) continue;
				File.WriteAllBytes(Path.Combine(backup, Path.GetFileNameWithoutExtension(file) + "-reseat-" + DateTime.Now.ToString("yyyyMMddHHmmss") + IslandFile.Extension), old);
				bool saved = DynamicIslands.SaveIsland(name);
				Log((saved ? "  " : "FAIL: ") + name + ": " + n + " trees reseated" + (saved ? "" : ", not saved"));
				if (saved) { changed++; trees += n; }
			}
			Log("PASS: reseat - " + trees + " trees on " + changed + " of " + names.Count + " islands set back on the ground");
		}

		[ConsoleCommand(name: "CIFloating", docs: "Dev, editor: legs, posts and pillars of the island's objects that don't reach down to anything - a slim upright part whose foot is more than <gap> m (default 0.3) above the ground or what is under it - and objects that stand on the ground with part of their base only (a stilt house set on a slope: the legs over its low side end in the air), floors and decks over nothing (a big deck needs every corner held, a small floor one corner or a held neighbour) and chests and crates with nothing under them (ROADMAP CA30): CIFloating [gap] [name part]")]
		public static void FloatingConsoleCommand(string[] args) { StartTest(OnceAsTest(() => FloatingCommand(args))); }

		/// <summary>A plain command run as a test, so ci.ps1 -done gets the IDLE line it waits for: CIFloating and CIChestReach
		/// printed their PASS or FAIL and no done marker, and the run waited out its timeout (11-05).</summary>
		static IEnumerator OnceAsTest(Action run) { run(); yield break; }

		public static void FloatingCommand(string[] args)
		{
			if (!DynamicIslands.InEditor()) { floatProblems = 1; Fail("CIFloating [gap] (in the editor)"); return; }
			float gap = args != null && args.Length > 0 ? F(args[0]) : 0.3f;
			string only = args != null && args.Length > 1 ? args[1] : "";
			Vector2 mid = EditorLandCentre();
			float sea = DynamicIslands.EditorWaterLevel;
			Terrain ground = terraineditor.terrain;
			Physics.SyncTransforms();
			int found = 0, legs = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string n = e.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Note_") || n.StartsWith("Loot_") || n.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0 || NotABase.IsMatch(n) || Hangs.IsMatch(n)) continue;
				foreach (Renderer r in e.GetComponentsInChildren<Renderer>(false))
				{
					if (!(r is MeshRenderer) || !r.enabled) continue;
					Bounds b = r.bounds;
					// (a leg, post or pillar: upright and slim - a brace or a beam lying down is wider than it is tall)
					if (b.size.y < 1.2f || b.size.y < 2.5f * Mathf.Max(b.size.x, b.size.z)) continue;
					legs++;
					Vector3 foot = new Vector3(b.center.x, b.min.y, b.center.z);
					float land = ground != null ? ground.SampleHeight(foot) + ground.transform.position.y : float.MinValue;
					// (on something right at its foot: a pillar stacked on another starts inside the one below it, a pole or a
					// locker set down a few centimetres into the roof or floor it stands on)
					if (Physics.OverlapSphere(foot + Vector3.up * 0.03f, 0.12f, ~0, QueryTriggerInteraction.Ignore).Any(c => c.gameObject != r.gameObject && !c.transform.IsChildOf(r.transform) && c.GetComponent<Terrain>() == null)) continue;
				RaycastHit hit;
					bool under = Physics.Raycast(foot + Vector3.down * 0.02f, Vector3.down, out hit, 300f, ~0, QueryTriggerInteraction.Ignore);
					float support = Mathf.Max(land, under ? hit.point.y : float.MinValue);
					if (foot.y - support <= gap) continue;
					// (above another part of the same object - a chimney on its roof - it is attached, not standing)
					if (under && hit.point.y >= land && hit.collider.GetComponentInParent<EditorGameObject>() == e) continue;
					// (or inside one with no collider in the editor: a gate's leaves in its own frame, on its landing - UT_MainEntranceDoor,
					// Hadal Commons 11-05)
					if (e.GetComponentsInChildren<Renderer>(false).Any(o => o != r && o is MeshRenderer && o.enabled && o.GetComponent<Collider>() == null && o.bounds.min.y < foot.y - 0.05f &&
						o.bounds.max.y >= foot.y - gap && foot.x > o.bounds.min.x && foot.x < o.bounds.max.x && foot.z > o.bounds.min.z && foot.z < o.bounds.max.z)) continue;
					// (on the hill a world drapes over a cave piece's roof (CaveMountain): the editor has no such hill to stand on)
					if (under && UnderCaveHill(foot, hit.collider, gap)) continue;
					found++;
					FLog("  floating: " + n + " (" + r.name + ") at " + Num(foot.x - mid.x) + " " + Num(foot.z - mid.y) + ": its foot h=" + Num(foot.y - sea) + " is " + Num(foot.y - support) + " m above " +
						(under && hit.point.y >= land && hit.collider != null ? hit.collider.name : "the ground"));
				}
			}
			// Objects standing on the ground with part of their base only (a stilt house on a slope: the legs over the low side
			// end in the air - its legs are one mesh with the house, so the ground under its whole footprint is measured). The
			// editor's name tags over chests (CI_NoTint) aren't the object: a chest's 14 m label made Hightide Harbor's chest on a
			// stilt house's floor "partly in the air" (2026-10-03).
			int bases = 0, partly = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string n = e.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_") || n.StartsWith("Note_") || NotABase.IsMatch(n) || Hangs.IsMatch(n) || CameraPath.IsPoint(n)) continue; // (camera points: unseen in a world)
				// (a zipline with a zip.to: the editor shows Raft's own far end, which ZiplineEnds moves when the island is spawned -
				// the Lodestone Array's north line read "partly in the air" off the cliff where Raft's 27 m line ends, 2026-10-20;
				// CIGroundIslands measures each of its ends in the world)
				if (n.StartsWith("ZiplinePath") && ObjectProps.Get(e.Props, ZiplineEnds.ZipTo).Length > 0) continue;
				// (Raft's air pocket is a bubble of air in the water - it stands on nothing by design: Lumen Shelf's under its arch
				// read "partly in the air" on the slope, w03 11-09)
				if (n == ScatterTool.AirPocketObject) continue;
				Renderer[] rs = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (rs.Length == 0 || ground == null) continue;
				// (its base: the parts that reach down to its bottom - not a windmill's sails or a telescope's tube above it)
				float bottom = rs.Min(r => r.bounds.min.y);
				rs = rs.Where(r => r.bounds.min.y < bottom + 0.6f).ToArray();
				Bounds b = rs[0].bounds;
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				// (a deck, floor or plank lies on its posts: its base is the posts')
				if (Mathf.Max(b.size.x, b.size.z) < 1.5f || b.size.y < 0.6f || b.min.y < sea - 0.5f) continue;
				float lowest = float.MaxValue, highest = float.MinValue;
				Vector3 at = Vector3.zero;
				int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(b.size.x, b.size.z)), 3, 30);
				for (int i = 0; i <= steps; i++)
					for (int j = 0; j <= steps; j++)
					{
						// (20 % in from the box around it: a rounded base, or one turned in its box, doesn't reach the box's corners)
						var p = new Vector3(Mathf.Lerp(b.min.x + b.size.x * 0.2f, b.max.x - b.size.x * 0.2f, i / (float)steps), 0f, Mathf.Lerp(b.min.z + b.size.z * 0.2f, b.max.z - b.size.z * 0.2f, j / (float)steps));
						float g = b.min.y - (ground.SampleHeight(p) + ground.transform.position.y);
						if (g < lowest) lowest = g;
						if (g > highest) { highest = g; at = p; }
					}
				// (only what stands on the ground somewhere: decks on posts, roofs and things on tables touch no ground; and not
				// what stands on another object - a wall on its floor, rubble on a roof)
				if (lowest > 0.4f) continue;
				if (OnAnotherObject(b, e)) continue;
				bases++;
				if (highest <= 0.6f) continue;
				partly++;
				FLog("  partly in the air: " + n + " at " + Num(b.center.x - mid.x) + " " + Num(b.center.z - mid.y) + ": its base h=" + Num(b.min.y - sea) + " is up to " + Num(highest) + " m above the ground (at " + Num(at.x - mid.x) + " " + Num(at.z - mid.y) + ")");
			}
			// The land's and the sea's plants, rocks and corals (the generator's): their pivot is their foot - none above the
			// ground under it (a stroke that lowered the ground under them, or a slope they were set on by their middle)
			int nature = 0, lifted = 0;
			// (the hill a world drapes over the island's cave pieces, built here to stand things on: OnCaveSkin looked for a skin
			// the editor never had - only a stray measuring hill some earlier step left, so the check measured against a hill
			// other than the world's. Rootweave's 22 flagged plants and rocks were not hill items: they hang over the trench a
			// stroke lowered, and its recipe now clears them, bugfix 11-25)
			GameObject floatHill = GameObject.Find(CaveMountain.PreviewName) == null && ground != null ? CaveMountain.Preview(PlacedEditorObjects(), ground.transform.position) : null;
			if (floatHill != null) FLog("  (the cave hill a world builds over the island's caves is measured against too)");
			try {
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string n = e.GameObjectName ?? "";
				if (!NotABase.IsMatch(n) || ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.IndexOf("Cave", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Tunnel", StringComparison.OrdinalIgnoreCase) >= 0 || ground == null) continue;
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				nature++;
				Vector3 p = e.transform.position;
				float land = ground.SampleHeight(p) + ground.transform.position.y;
				if (p.y - land <= 0.5f) continue;
				// (a cave hill's skin is the ground where it lies: a world's mountain over a cave piece is draped over the cut terrain,
				// and what the generator set on the old ground stands on it - Rootweave's west side over the Flooded Passage, its
				// palms and boulders 17 m over the trench under the hill, isl-fix 11-24)
				if (OnCaveSkin(e, p)) continue;
				// (set up on purpose: a standing stone reaches down to the ground, a flowerpot stands on a roof, a rockfall's
				// boulder on the one under it - only what hangs over the ground with nothing under it is lifted)
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (parts.Length > 0)
				{
					Bounds whole = parts[0].bounds;
					foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
					if (whole.min.y - (ground.SampleHeight(whole.center) + ground.transform.position.y) <= 0.5f) continue;
					RaycastHit on;
					if (Physics.Raycast(new Vector3(whole.center.x, whole.min.y + 0.1f, whole.center.z), Vector3.down, out on, 0.8f, ~0, QueryTriggerInteraction.Ignore) && on.collider.GetComponent<Terrain>() == null &&
						on.collider.GetComponentInParent<EditorGameObject>() != null && on.collider.GetComponentInParent<EditorGameObject>() != e) continue;
					// (its foot - the pivot - on a floor: a plant whose mesh reaches under its foot starts the ray above inside the
						// floor tile and misses it - Brinelab's lobby vines, 0.4 m of leaves under a tile, w03 11-09)
						if (Physics.Raycast(new Vector3(p.x, p.y + 0.3f, p.z), Vector3.down, out on, 1.1f, ~0, QueryTriggerInteraction.Ignore) && on.collider.GetComponent<Terrain>() == null &&
							on.collider.GetComponentInParent<EditorGameObject>() != null && on.collider.GetComponentInParent<EditorGameObject>() != e) continue;
						// (an icicle hangs from what is over it - a cave's roof, an overhang: its top against it is set right)
					if (n.IndexOf("Icicle", StringComparison.OrdinalIgnoreCase) >= 0 && Physics.Raycast(new Vector3(whole.center.x, whole.max.y - 0.3f, whole.center.z), Vector3.up, out on, 1.1f, ~0, QueryTriggerInteraction.Ignore) &&
						on.collider.GetComponentInParent<EditorGameObject>() != e) continue;
				}
				lifted++;
				if (lifted <= floatDetail) FLog("  above the ground: " + n + " at " + Num(p.x - mid.x) + " " + Num(p.z - mid.y) + ": h=" + Num(p.y - sea) + ", " + Num(p.y - land) + " m above the ground" + UnderIt(e, p));
			}
			}
			finally { if (floatHill != null) UnityEngine.Object.DestroyImmediate(floatHill); Physics.SyncTransforms(); }
			// Objects in the air touching nothing, and objects buried whole under the ground (the user, 2026-10-02: the Stranded
			// Gull's boat was set down by its middle and sank under the beach - its locker and crate hung in the air over it)
			int all = 0, alone = 0, buried = 0;
			List<EditorGameObject> placed = PlacedEditorObjects().ToList();
			// (ladders stacked into one tall ladder - dp_tower's shaft, Ashfall's, Glacier Mouth's crevasse: a ladder's colliders
			// are triggers, so the middle one of three touched nothing. One that meets a held ladder end to end is held, up the
			// whole stack, as long as its bottom or top one rests on something: fix1107, Beacon Saddle)
			List<KeyValuePair<EditorGameObject, Bounds>> laddersHeld = new List<KeyValuePair<EditorGameObject, Bounds>>();
			List<KeyValuePair<EditorGameObject, Bounds>> laddersAir = new List<KeyValuePair<EditorGameObject, Bounds>>();
			foreach (EditorGameObject e in placed)
			{
				string n = e.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				// (a fly-through's camera points hang in the air on purpose: unseen in play, never set down - w11, The Rain Engine)
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n == ContentCatalog.MarkerOnly || CameraPath.IsPoint(n) || ground == null) continue;
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (parts.Length == 0) continue;
				Bounds whole = parts[0].bounds;
				foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
				all++;
				float land = ground.SampleHeight(whole.center) + ground.transform.position.y;
				// (things on the water - a buoy, a raft's foundation - float; the sea holds them)
				bool onWater = whole.min.y < sea && whole.max.y > sea - 0.3f;
				// (a string of lights hangs the same way: each light's collider is a trigger, so the middle of a laundry line touched
				// nothing - held when it meets a held light or a post, cn4 11-19, Raft Row Bazaar and Rookery Stacks)
				bool ladder = n.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("StringLight", StringComparison.OrdinalIgnoreCase) >= 0;
				bool inAir = false;
				if (!onWater && whole.min.y - land > 0.5f)
				{
					Collider[] touching = Physics.OverlapBox(whole.center, whole.extents + Vector3.one * 0.15f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
					// (the terrain counts where the box meets its surface: a beam laid across a gap rests on the rock at its ends)
					if (!touching.Any(c => !c.transform.IsChildOf(e.transform)))
					{
						inAir = true;
						if (ladder) laddersAir.Add(new KeyValuePair<EditorGameObject, Bounds>(e, whole));
						else
						{
							alone++;
							if (alone <= floatDetail) FLog("  in the air, touching nothing: " + n + " at " + Num(whole.center.x - mid.x) + " " + Num(whole.center.z - mid.y) + ": its bottom h=" + Num(whole.min.y - sea) + " is " + Num(whole.min.y - land) + " m above the ground");
						}
					}
				}
				if (ladder && !inAir) laddersHeld.Add(new KeyValuePair<EditorGameObject, Bounds>(e, whole));
				// (buried whole: its top under the ground everywhere over its footprint - a rock sunk into a slope shows on its low
				// side and is meant so - and not inside a cave or a building of other objects)
				if (!ShowsAboveGround(whole, ground) && !n.Contains("Cave") && !n.Contains("Tunnel") &&
					!placed.Any(o => o != e && o.GetComponentsInChildren<Collider>(false).Any(c => !c.isTrigger && c.bounds.Contains(whole.center))))
				{
					buried++;
					if (buried <= floatDetail) FLog("  buried under the ground: " + n + " at " + Num(whole.center.x - mid.x) + " " + Num(whole.center.z - mid.y) + ": its top h=" + Num(whole.max.y - sea) + " is " + Num(land - whole.max.y) + " m under the ground");
				}
			}
			for (bool more = true; more; )
			{
				more = false;
				foreach (KeyValuePair<EditorGameObject, Bounds> l in laddersAir.ToList())
				{
					Bounds near = l.Value;
					near.Expand(0.3f);
					if (!laddersHeld.Any(h => h.Value.Intersects(near))) continue;
					laddersAir.Remove(l); laddersHeld.Add(l); more = true;
				}
			}
			foreach (KeyValuePair<EditorGameObject, Bounds> l in laddersAir)
			{
				Bounds whole = l.Value;
				float land = ground.SampleHeight(whole.center) + ground.transform.position.y;
				alone++;
				if (alone <= floatDetail) FLog("  in the air, touching nothing: " + (l.Key.GameObjectName ?? "") + " at " + Num(whole.center.x - mid.x) + " " + Num(whole.center.z - mid.y) + ": its bottom h=" + Num(whole.min.y - sea) + " is " + Num(whole.min.y - land) + " m above the ground (no held ladder above or below it)");
			}
			// Things on the sea floor under a deck that stands above the sea - more than 4 m under it, so not a flooded room:
			// set down "on the ground", Tide Farm's crop beds, scarecrow and table lay 7-10 m down under its farm raft (the
			// review, 2026-10-03). Named, not failed: a trench's floor under a raft can be meant
			int underDeck = 0;
			foreach (EditorGameObject e in placed)
			{
				string n = e.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (RaftUnderwater.CategoryOf(n) != null || n.StartsWith("Zone_") || n == ContentCatalog.MarkerOnly || ContentCatalog.IsCreature(n)) continue;
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (parts.Length == 0) continue;
				Bounds whole = parts[0].bounds;
				foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
				if (whole.max.y > sea - 0.5f) continue;
				RaycastHit up;
				if (!Physics.Raycast(new Vector3(whole.center.x, whole.max.y + 0.05f, whole.center.z), Vector3.up, out up, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;
				EditorGameObject over = up.collider.GetComponentInParent<EditorGameObject>();
				if (over == null || over == e || up.point.y < sea - 0.3f || up.point.y - whole.max.y < 4f) continue;
				// (a cave's rock over it is no deck: Rootweave's drowned crate and air pocket lie in its sea cave by design, 11-09)
				if ((over.GameObjectName ?? "").IndexOf("Cave", StringComparison.Ordinal) >= 0) continue;
				underDeck++;
				if (underDeck <= floatDetail) FLog("  on the sea floor under a deck: " + n + " at " + Num(whole.center.x - mid.x) + " " + Num(whole.center.z - mid.y) + ", top h=" + Num(whole.max.y - sea) + " - " + over.GameObjectName + " over it at h=" + Num(up.point.y - sea));
			}
			if (underDeck > 0) FLog("  (" + underDeck + " on the sea floor under a deck - meant for the deck? set them down in a frame with its floor)");
			// Floors and decks over nothing (CA30): FloorsOverNothing
			int floors;
			List<FloorGap> gapsOf = FloorsOverNothing(placed, gap, ground, sea, out floors);
			if (only.Length > 0) floors = placed.Count(e => FloorLike.IsMatch(e.GameObjectName ?? "") && (e.GameObjectName ?? "").IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0);
			int corners = 0;
			foreach (FloorGap d in gapsOf)
			{
				string n = d.Floor.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				corners++;
				string where = string.Join(", ", d.Corners.Select(p => { float land = ground.SampleHeight(p) + ground.transform.position.y; return Num(p.x - mid.x) + " " + Num(p.z - mid.y) + " (" + Num(p.y - Mathf.Max(land, sea)) + " m over the " + (land < sea ? "water" : "ground") + ")"; }).ToArray());
				if (corners <= floatDetail) FLog("  floor over nothing: " + n + " at " + Num(d.Box.center.x - mid.x) + " " + Num(d.Box.center.z - mid.y) + ", floor h=" + Num(d.Box.min.y - sea) + (d.Big ? ": " + d.Corners.Count + " of its corners - " : ", held nowhere and by no floor beside it - corners ") + where);
			}
			// Chests, crates, lockers and boxes with nothing under them (CA30: Ghost Raft's chest sat in the sea between two
			// foundations - "on the water" it was left out above): the middle of its underside looks down 0.3 m
			int loose = 0, boxes = 0;
			foreach (EditorGameObject e in placed)
			{
				string n = e.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (!Container.IsMatch(n) || Hangs.IsMatch(n) || ground == null) continue;
				if (n.IndexOf("PowerBox", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Ventilation", StringComparison.OrdinalIgnoreCase) >= 0) continue;   // a wall-mounted box (a reader, a fuse box, a vent)
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (parts.Length == 0) continue;
				Bounds whole = parts[0].bounds;
				foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
				boxes++;
				var foot = new Vector3(whole.center.x, whole.min.y, whole.center.z);
				float land = ground.SampleHeight(foot) + ground.transform.position.y;
				if (foot.y - land <= 0.3f) continue;
				float r0 = Mathf.Clamp(Mathf.Min(whole.size.x, whole.size.z) * 0.3f, 0.1f, 0.3f);
				RaycastHit[] hits = Physics.SphereCastAll(foot + Vector3.up * (r0 + 0.1f), r0, Vector3.down, 0.4f, ~0, QueryTriggerInteraction.Ignore);
				if (hits.Any(h => h.collider.GetComponentInParent<EditorGameObject>() != e)) continue;
				loose++;
				if (loose <= floatDetail) FLog("  nothing under it: " + n + " at " + Num(foot.x - mid.x) + " " + Num(foot.z - mid.y) + ": its bottom h=" + Num(foot.y - sea) + (land < sea && foot.y < sea + 0.3f ? ", in the sea " + Num(sea - land) + " m deep" : ", " + Num(foot.y - land) + " m above the ground"));
			}
			floatProblems = found + partly + lifted + alone + buried + corners + loose;
			FLog((floatProblems == 0 ? "PASS" : "FAIL") + ": floating legs and posts: " + found + " of " + legs + "; standing on part of their base: " + partly + " of " + bases + "; plants and rocks above the ground: " + lifted + " of " + nature +
				"; in the air touching nothing: " + alone + " of " + all + "; buried under the ground: " + buried + " of " + all + "; under a deck: " + underDeck + "; floors over nothing: " + corners + " of " + floors + "; chests and crates with nothing under them: " + loose + " of " + boxes);
		}

		[ConsoleCommand(name: "CIHeightMap", docs: "Dev, editor: the island's ground as text, to place a recipe's pieces on - a character every <step> m (8) out to <half> m (120) from the land's middle in recipe coordinates (x left to right, z top to bottom): ~ sea, . under 2 m, : 2-5, - 5-10, = 10-20, + 20-35, # 35-60, @ higher; then its highest points and level spots by height. CIHeightMap [step] [half]")]
		public static void HeightMapCommand(string[] args)
		{
			if (terraineditor.terrain == null) { Fail("no terrain here (the editor, an island open)"); return; }
			float step = args != null && args.Length > 0 ? F(args[0]) : 8f, half = args != null && args.Length > 1 ? F(args[1]) : 120f;
			Vector2 mid = EditorLandCentre();
			float sea = DynamicIslands.EditorWaterLevel;
			Func<float, float, float> above = (x, z) => GroundY(mid.x + x, mid.y + z) - sea;
			const string chars = "~.:-=+#@";
			float[] edges = { 0f, 2f, 5f, 10f, 20f, 35f, 60f };
			int n = Mathf.Max(1, Mathf.RoundToInt(half / step));
			var cells = new List<KeyValuePair<Vector2, float>>();
			for (int j = n; j >= -n; j--)
			{
				var row = new StringBuilder();
				for (int i = -n; i <= n; i++)
				{
					float v = above(i * step, j * step);
					cells.Add(new KeyValuePair<Vector2, float>(new Vector2(i * step, j * step), v));
					int k = 0;
					while (k < edges.Length && v >= edges[k]) k++;
					row.Append(chars[k]);
				}
				Log("MAP " + (j * step).ToString("F0", CultureInfo.InvariantCulture).PadLeft(5) + " " + row);
			}
			Log("MAP columns: x from " + (-n * step).ToString("F0", CultureInfo.InvariantCulture) + " to " + (n * step).ToString("F0", CultureInfo.InvariantCulture) + " every " + step.ToString("0.#", CultureInfo.InvariantCulture) + " m");
			// (its peaks: the highest cells at least 3 steps apart)
			var peaks = new List<KeyValuePair<Vector2, float>>();
			foreach (var c in cells.OrderByDescending(c => c.Value))
			{
				if (c.Value <= 2f || peaks.Count >= 5) break;
				if (peaks.All(p => Vector2.Distance(p.Key, c.Key) >= 3f * step)) peaks.Add(c);
			}
			Log("MAP highest: " + string.Join(", ", peaks.Select(p => p.Key.x.ToString("F0", CultureInfo.InvariantCulture) + " " + p.Key.y.ToString("F0", CultureInfo.InvariantCulture) + " (" + p.Value.ToString("F1", CultureInfo.InvariantCulture) + " m)").ToArray()));
			// (level spots for a building: the ground within 1 m of its middle's height over the step around it, by height band)
			foreach (float[] band in new[] { new[] { 0.5f, 3f }, new[] { 3f, 10f }, new[] { 10f, 25f }, new[] { 25f, 1000f } })
			{
				var level = cells.Where(c => c.Value >= band[0] && c.Value < band[1]).Where(c =>
				{
					float r = step * 0.5f;
					return new[] { new Vector2(r, 0f), new Vector2(-r, 0f), new Vector2(0f, r), new Vector2(0f, -r) }.All(o => Mathf.Abs(above(c.Key.x + o.x, c.Key.y + o.y) - c.Value) < 1f);
				}).Take(8).ToList();
				Log("MAP level " + band[0].ToString("0.#", CultureInfo.InvariantCulture) + "-" + (band[1] > 999f ? "up" : band[1].ToString("0.#", CultureInfo.InvariantCulture)) + " m: " +
					(level.Count == 0 ? "none" : string.Join(", ", level.Select(c => c.Key.x.ToString("F0", CultureInfo.InvariantCulture) + " " + c.Key.y.ToString("F0", CultureInfo.InvariantCulture) + " (" + c.Value.ToString("F1", CultureInfo.InvariantCulture) + ")").ToArray())));
			}
			Log("PASS: height map");
		}

		[ConsoleCommand(name: "CISeaSpots", docs: "Dev, editor: level spots of sea floor <min> to <max> m down (3 to 8), for a recipe's crates and wrecks to lie on - the ground within 0.8 m of the spot's height 1.5 m around it, spots at least 10 m apart, nearest the land first, in recipe coordinates. CISeaSpots [min] [max] [step] [half]")]
		public static void SeaSpotsCommand(string[] args)
		{
			// (a generated island's sea floor is steep in most places: crates set on its slopes lay half inside them or hung
			// over the drop - Varuna Point Remade needed 108 probe crates, Temperance Remade 198, to find a few level spots)
			if (terraineditor.terrain == null) { Fail("no terrain here (the editor, an island open)"); return; }
			float min = args != null && args.Length > 0 ? F(args[0]) : 3f, max = args != null && args.Length > 1 ? F(args[1]) : 8f;
			float step = args != null && args.Length > 2 ? F(args[2]) : 2f, half = args != null && args.Length > 3 ? F(args[3]) : 120f;
			Vector2 mid = EditorLandCentre();
			float sea = DynamicIslands.EditorWaterLevel;
			Func<float, float, float> above = (x, z) => GroundY(mid.x + x, mid.y + z) - sea;
			int n = Mathf.Max(1, Mathf.RoundToInt(half / step));
			var land = new List<Vector2>();
			var level = new List<KeyValuePair<Vector2, float>>();
			for (int j = -n; j <= n; j++)
				for (int i = -n; i <= n; i++)
				{
					float x = i * step, z = j * step, v = above(x, z);
					if (v > 0f) { if ((i + j) % 2 == 0) land.Add(new Vector2(x, z)); continue; }
					if (-v < min || -v > max) continue;
					bool flat = true;
					for (int a = 0; a < 8 && flat; a++)
					{
						float ang = a * Mathf.PI / 4f;
						flat = Mathf.Abs(above(x + 1.5f * Mathf.Cos(ang), z + 1.5f * Mathf.Sin(ang)) - v) < 0.8f;
					}
					if (flat) level.Add(new KeyValuePair<Vector2, float>(new Vector2(x, z), v));
				}
			Func<Vector2, float> fromLand = p => land.Count == 0 ? 0f : land.Min(l => Vector2.Distance(l, p));
			var spots = new List<KeyValuePair<Vector2, float>>();
			foreach (var c in level.OrderBy(c => fromLand(c.Key)))
			{
				if (spots.Count >= 16) break;
				if (spots.All(s => Vector2.Distance(s.Key, c.Key) >= 10f)) spots.Add(c);
			}
			foreach (var s in spots)
				Log("SPOT " + s.Key.x.ToString("F0", CultureInfo.InvariantCulture) + " " + s.Key.y.ToString("F0", CultureInfo.InvariantCulture) + ": " + (-s.Value).ToString("F1", CultureInfo.InvariantCulture) + " m down, " + fromLand(s.Key).ToString("F0", CultureInfo.InvariantCulture) + " m from the land");
			Log((spots.Count > 0 ? "PASS" : "FAIL") + ": sea spots " + min.ToString("0.#", CultureInfo.InvariantCulture) + "-" + max.ToString("0.#", CultureInfo.InvariantCulture) + " m down: " + spots.Count + " (" + level.Count + " level cells)");
		}

		[ConsoleCommand(name: "CIBoundsOf", docs: "Dev, editor: where the island's objects whose name contains <part> are and how far they reach - pivot (m from the island's middle, h above the sea), turn, the box around their meshes (bottom and top h), the ground under its middle and the colliders it touches: fixing a recipe's floating or buried things. CIBoundsOf <part>")]
		public static void BoundsOfCommand(string[] args)
		{
			if (args == null || args.Length < 1 || !DynamicIslands.InEditor()) { Fail("CIBoundsOf <part> (in the editor)"); return; }
			string part = string.Join(" ", args);
			Vector2 mid = EditorLandCentre();
			float sea = DynamicIslands.EditorWaterLevel;
			Terrain ground = terraineditor.terrain;
			Physics.SyncTransforms();
			int n = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string name = e.GameObjectName ?? "";
				if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
				Renderer[] rs = PlacementOptions.ShapeRenderers(e.gameObject);
				if (rs.Length == 0) continue;
				Bounds b = rs[0].bounds;
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				Vector3 p = e.transform.position;
				float land = ground != null ? ground.SampleHeight(b.center) + ground.transform.position.y : sea;
				Collider[] touching = Physics.OverlapBox(b.center, b.extents + Vector3.one * 0.15f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
				string others = string.Join(", ", touching.Where(c => !c.transform.IsChildOf(e.transform)).Select(c => c.GetComponent<Terrain>() != null ? "terrain" : c.GetComponentInParent<EditorGameObject>() != null ? c.GetComponentInParent<EditorGameObject>().GameObjectName : c.name).Distinct().Take(6).ToArray());
				Log("  " + name + " at " + Num(p.x - mid.x) + " " + Num(p.z - mid.y) + " h=" + Num(p.y - sea) + " turn " + e.transform.eulerAngles.ToString("F0") + ": box " + Num(b.min.x - mid.x) + ".." + Num(b.max.x - mid.x) + " x " +
					Num(b.min.z - mid.y) + ".." + Num(b.max.z - mid.y) + ", h " + Num(b.min.y - sea) + ".." + Num(b.max.y - sea) + "; ground under its middle h=" + Num(land - sea) + "; touches " + (others.Length > 0 ? others : "nothing"));
				n++;
			}
			Log("PASS: " + n + " objects with '" + part + "'");
		}

		[ConsoleCommand(name: "CIChestReach", docs: "Dev, editor: can every chest (Loot_*) of the island be used? For each, spots round it 0.9-1.8 m out with a floor and room to stand, and from a standing eye there a clear line within 2.5 m to it - a chest under a table or wedged in a wall has none (Whalebone Bay's altar box, PQ5, 2026-10-27). CIChestReach")]
		public static void ChestReachConsoleCommand(string[] args) { StartTest(OnceAsTest(() => ChestReachCommand(args))); }

		public static void ChestReachCommand(string[] args)
		{
			if (!DynamicIslands.InEditor()) { Fail("CIChestReach (in the editor)"); return; }
			Vector2 mid = EditorLandCentre();
			float sea = DynamicIslands.EditorWaterLevel;
			Physics.SyncTransforms();
			int n = 0, bad = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string name = e.GameObjectName ?? "";
				if (!name.StartsWith("Loot_", StringComparison.OrdinalIgnoreCase)) continue;
				Renderer[] rs = PlacementOptions.ShapeRenderers(e.gameObject);
				if (rs.Length == 0) continue;
				Bounds b = rs[0].bounds;
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				n++;
				int good = 0;
				string why = "no floor to stand on near it";
				// (each spot's reason counted: the last one alone hid what most of the ring met)
				var whys = new Dictionary<string, int>();
				for (int ring = 0; ring < 3; ring++)
					for (int k = 0; k < 16; k++)
					{
						float a = k * Mathf.PI / 8f, rad = 0.9f + 0.45f * ring;
						Vector3 at = b.center + new Vector3(Mathf.Sin(a) * rad, 0f, Mathf.Cos(a) * rad);
						RaycastHit floor;
						if (!Physics.Raycast(new Vector3(at.x, b.max.y + 1.2f, at.z), Vector3.down, out floor, b.max.y - b.min.y + 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
						if (floor.collider.transform.IsChildOf(e.transform)) continue;
						Vector3 foot = floor.point;
						Collider block = Physics.OverlapCapsule(foot + Vector3.up * 0.45f, foot + Vector3.up * 1.6f, 0.28f, ~0, QueryTriggerInteraction.Ignore)
							.FirstOrDefault(c => !c.transform.IsChildOf(e.transform));
						if (block != null)
						{
							EditorGameObject o = block.GetComponentInParent<EditorGameObject>();
							why = "no room to stand round it (" + (o != null ? o.GameObjectName : block.GetComponent<Terrain>() != null ? "terrain" : block.name) + ")";
							whys[why] = (whys.ContainsKey(why) ? whys[why] : 0) + 1; continue;
						}
						Vector3 eye = foot + Vector3.up * 1.6f;
						float d = Vector3.Distance(eye, b.center);
						if (d > 2.5f) { why = "too far from any floor (" + d.ToString("F1") + " m)"; whys[why] = (whys.ContainsKey(why) ? whys[why] : 0) + 1; continue; }
						RaycastHit[] hits = Physics.RaycastAll(eye, (b.center - eye).normalized, d + 0.3f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
						if (hits.Length > 0 && !hits[0].collider.transform.IsChildOf(e.transform))
						{
							EditorGameObject o = hits[0].collider.GetComponentInParent<EditorGameObject>();
							why = (o != null ? o.GameObjectName : hits[0].collider.name) + " in the way";
							whys[why] = (whys.ContainsKey(why) ? whys[why] : 0) + 1; continue;
						}
						good++;
					}
				Vector3 p = e.transform.position;
				if (good == 0) bad++;
				Log("  " + (good == 0 ? "UNUSABLE " : "") + name + " at " + Num(p.x - mid.x) + " " + Num(p.z - mid.y) + " h=" + Num(p.y - sea) + ": " + (good > 0 ? good + " spots to use it from" : whys.Count == 0 ? why : string.Join(", ", whys.OrderByDescending(w => w.Value).Select(w => w.Key + " x" + w.Value).ToArray())));
			}
			if (bad > 0) Fail(bad + " of " + n + " chests can't be used");
			else Log("PASS: " + n + " chests, each usable");
		}

		[ConsoleCommand(name: "CILadderSide", docs: "Dev, editor: is each ladder's climbing side free? Raft's ladder takes hold of a player only on the side of its climb trigger (Block_Ladder: its local -z); for every ladder, balls up that side - a ladder whose pillar or rock is on the climbing side can't be climbed (Twin Horns, PQ6, 2026-10-27). CILadderSide")]
		public static void LadderSideCommand(string[] args)
		{
			if (!DynamicIslands.InEditor()) { Fail("CILadderSide (in the editor)"); return; }
			Vector2 mid = EditorLandCentre();
			float sea = DynamicIslands.EditorWaterLevel;
			Physics.SyncTransforms();
			int n = 0, bad = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string name = e.GameObjectName ?? "";
				if (name.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) < 0) continue;
				Collider climb = e.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => c.isTrigger && c.name.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0);
				Renderer[] rs = PlacementOptions.ShapeRenderers(e.gameObject);
				if (climb == null || rs.Length == 0) continue;
				Bounds b = rs[0].bounds;
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				Vector3 side = climb.bounds.center - b.center;
				side.y = 0f;
				if (side.sqrMagnitude < 0.0001f) continue;
				side.Normalize();
				n++;
				int blocked = 0, samples = 0;
				string by = "";
				Bounds cb = climb.bounds;
				for (float y = cb.min.y + 0.4f; y <= cb.max.y - 0.2f; y += 0.4f)
				{
					samples++;
					Vector3 at = new Vector3(cb.center.x, y, cb.center.z) + side * 0.3f;
					Collider hit = Physics.OverlapSphere(at, 0.22f, ~0, QueryTriggerInteraction.Ignore).FirstOrDefault(c => !c.transform.IsChildOf(e.transform));
					if (hit == null) continue;
					blocked++;
					if (by.Length == 0) { EditorGameObject o = hit.GetComponentInParent<EditorGameObject>(); by = o != null ? o.GameObjectName : hit.GetComponent<Terrain>() != null ? "terrain" : hit.name; }
				}
				Vector3 p = e.transform.position;
				// (a ladder's foot in the ground is fine; most of its climbing side shut is not)
				bool shut = samples > 0 && blocked * 2 > samples;
				if (shut) bad++;
				if (shut || args != null && args.Length > 0)
					Log("  " + (shut ? "BLOCKED " : "") + name + " at " + Num(p.x - mid.x) + " " + Num(p.z - mid.y) + " h=" + Num(p.y - sea) + " turn " + e.transform.eulerAngles.y.ToString("F0") + ": climbing side (" + Num(side.x) + "," + Num(side.z) + ") shut " + blocked + "/" + samples + (by.Length > 0 ? " by " + by : ""));
			}
			if (bad > 0) Fail(bad + " of " + n + " ladders have their climbing side shut");
			else Log("PASS: " + n + " ladders, each climbable");
		}

		[ConsoleCommand(name: "CIProfile", docs: "Dev, editor: the island's profile along a line - at <n> points from <x1> <z1> to <x2> <z2> (recipe coordinates) the first thing a ray down from 40 m over the sea meets and its height above the sea: where a hull's deck and sides are, to set stairs to them. CIProfile <x1> <z1> <x2> <z2> <n>")]
		public static void ProfileCommand(string[] args)
		{
			if (args == null || args.Length < 5 || !DynamicIslands.InEditor()) { Fail("CIProfile <x1> <z1> <x2> <z2> <n> (in the editor)"); return; }
			Physics.SyncTransforms();
			Vector2 mid = EditorLandCentre();
			float sea = DynamicIslands.EditorWaterLevel;
			int n = Mathf.Clamp(int.Parse(args[4]), 2, 200);
			for (int i = 0; i < n; i++)
			{
				float f = i / (float)(n - 1);
				float x = Mathf.Lerp(F(args[0]), F(args[2]), f), z = Mathf.Lerp(F(args[1]), F(args[3]), f);
				RaycastHit hit;
				string what = "nothing";
				if (Physics.Raycast(new Vector3(mid.x + x, sea + 40f, mid.y + z), Vector3.down, out hit, 120f, ~0, QueryTriggerInteraction.Ignore))
				{
					EditorGameObject e = hit.collider.GetComponentInParent<EditorGameObject>();
					what = (hit.collider.GetComponent<Terrain>() != null ? "ground" : e != null ? e.GameObjectName : hit.collider.name) + " h=" + Num(hit.point.y - sea);
				}
				Log("  " + Num(x) + " " + Num(z) + ": " + what);
			}
			Log("PASS: profile");
		}

		[ConsoleCommand(name: "CIColliders", docs: "Dev, editor: the colliders of the island's objects whose name contains <part> (kind, size, layer, trigger, convex), and for the first one how much of its top a ray straight down meets - every 0.1 m over its box: a recipe's 'sit' and a play test's walk look for floors so (Hightide Harbor's chest fell through a floor of Raft's blocks onto the sand, 2026-10-03). CIColliders <part>")]
		public static void CollidersCommand(string[] args)
		{
			if (args == null || args.Length < 1 || !DynamicIslands.InEditor()) { Fail("CIColliders <part> (in the editor)"); return; }
			string part = string.Join(" ", args);
			Physics.SyncTransforms();
			EditorGameObject first = null;
			int n = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				if ((e.GameObjectName ?? "").IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (first == null) first = e;
				if (n++ >= 3) continue;
				foreach (Collider c in e.GetComponentsInChildren<Collider>(true))
				{
					string kind = c is BoxCollider ? "box " + ((BoxCollider)c).size.ToString("F2") : c is MeshCollider ? "mesh" + (((MeshCollider)c).convex ? " convex" : "") + " (" + (((MeshCollider)c).sharedMesh != null ? ((MeshCollider)c).sharedMesh.vertexCount + " vertices" : "no mesh") + ")" : c.GetType().Name;
					Vector2 cm = EditorLandCentre();
					Log("  " + e.GameObjectName + " / " + c.name + ": " + kind + ", middle " + Num(c.bounds.center.x - cm.x) + " " + Num(c.bounds.center.z - cm.y) + ", bounds " + c.bounds.size.ToString("F2") + " h " + Num(c.bounds.min.y - DynamicIslands.EditorWaterLevel) + ".." + Num(c.bounds.max.y - DynamicIslands.EditorWaterLevel) + ", layer " + LayerMask.LayerToName(c.gameObject.layer) + (c.isTrigger ? ", trigger" : "") + (c.enabled ? "" : ", off"));
				}
				// (and its renderers: a part far off its shape - a glow, a decal - makes the checks' boxes wrong)
				Vector2 mid = EditorLandCentre();
				foreach (Renderer r in e.GetComponentsInChildren<Renderer>(true))
					Log("  " + e.GameObjectName + " / renderer " + r.name + " (" + r.GetType().Name + (r.enabled ? "" : ", off") + (r.gameObject.activeInHierarchy ? "" : ", hidden") + "): middle " + Num(r.bounds.center.x - mid.x) + " " + Num(r.bounds.center.z - mid.y) + ", size " + r.bounds.size.ToString("F2") + ", h " + Num(r.bounds.min.y - DynamicIslands.EditorWaterLevel) + ".." + Num(r.bounds.max.y - DynamicIslands.EditorWaterLevel));
			}
			if (first != null)
			{
				Renderer[] rs = PlacementOptions.ShapeRenderers(first.gameObject);
				Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(first.transform.position, Vector3.one);
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				int rays = 0, hits = 0;
				for (float x = b.min.x + 0.05f; x < b.max.x; x += 0.1f)
					for (float z = b.min.z + 0.05f; z < b.max.z; z += 0.1f)
					{
						rays++;
						RaycastHit hit;
						if (Physics.Raycast(new Vector3(x, b.max.y + 2f, z), Vector3.down, out hit, 3f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(first.transform)) hits++;
					}
				Log("  rays down onto the first " + first.GameObjectName + ": " + hits + " of " + rays + " meet it");
			}
			Log("PASS: " + n + " objects with '" + part + "'");
		}

		[ConsoleCommand(name: "CIViewAt", docs: "Dev, editor: a picture of one spot of the island - the camera <dist> m from the point <x> <z> (m from the island's middle) <h> (above the sea), from <yaw> and <pitch> degrees: shot_<name>.png. CIViewAt <x> <z> <h> <yaw> <pitch> <dist> <name>")]
		public static void ViewAtCommand(string[] args)
		{
			if (args == null || args.Length < 7 || !DynamicIslands.InEditor() || Camera.main == null) { Fail("CIViewAt <x> <z> <h> <yaw> <pitch> <dist> <name> (in the editor)"); return; }
			Vector2 mid = EditorLandCentre();
			Vector3 at = new Vector3(mid.x + F(args[0]), DynamicIslands.EditorWaterLevel + F(args[2]), mid.y + F(args[1]));
			Quaternion look = Quaternion.Euler(F(args[4]), F(args[3]), 0f);
			EditorCamera ec = Camera.main.GetComponent<EditorCamera>();
			if (ec != null) ec.enabled = false; // (it would move the camera back to where it was steering)
			Camera.main.transform.SetPositionAndRotation(at - look * Vector3.forward * F(args[5]), look);
			StartTest(ViewAtShot(args[6], ec));
		}

		static IEnumerator ViewAtShot(string name, EditorCamera ec)
		{
			yield return new WaitForSecondsRealtime(0.8f);
			Screenshot(new[] { name });
			yield return new WaitForSecondsRealtime(0.5f);
			if (ec != null) ec.enabled = true;
			Log("PASS: view " + name);
		}

		[ConsoleCommand(name: "CIStandOn", docs:"Dev, editor: what a player would stand on along a line - rays down at n points from x1 z1 to x2 z2 (metres from the island's middle): the height above the sea and the object hit. CIStandOn <x1> <z1> <x2> <z2> [n] [from this height above the sea: under a roof]")]
		public static void ProbeCommand(string[] args)
		{
			if (args == null || args.Length < 4 || !DynamicIslands.InEditor()) { Fail("CIStandOn <x1> <z1> <x2> <z2> [n] (in the editor)"); return; }
			Vector2 mid = EditorLandCentre();
			float x1 = F(args[0]), z1 = F(args[1]), x2 = F(args[2]), z2 = F(args[3]);
			int n = args.Length > 4 ? Mathf.Clamp((int)F(args[4]), 2, 200) : 11;
			float sea = DynamicIslands.EditorWaterLevel, from0 = args.Length > 5 ? F(args[5]) : 300f;
			Physics.SyncTransforms();
			for (int i = 0; i < n; i++)
			{
				float f = i / (float)(n - 1);
				Vector3 from = new Vector3(mid.x + x1 + (x2 - x1) * f, sea + from0, mid.y + z1 + (z2 - z1) * f);
				RaycastHit hit;
				string what = Physics.Raycast(from, Vector3.down, out hit, 600f, ~0, QueryTriggerInteraction.Ignore)
					? (hit.point.y - sea).ToString("F2", CultureInfo.InvariantCulture) + " " + (hit.collider.GetComponentInParent<EditorGameObject>() != null ? hit.collider.GetComponentInParent<EditorGameObject>().GameObjectName : hit.collider.name)
					: "nothing";
				Log("  stand at " + (x1 + (x2 - x1) * f).ToString("F1", CultureInfo.InvariantCulture) + "," + (z1 + (z2 - z1) * f).ToString("F1", CultureInfo.InvariantCulture) + ": " + what);
			}
			Log("PASS: probe");
		}

		[ConsoleCommand(name: "CIGroundSplit", docs: "Dev, editor: whether the generator sets things down on the terrain's own surface - the editor terrain's heights at random points (Unity's SampleHeight) against IslandGenerator.TerrainSurface (its cells' triangles) and against smooth blending: the largest differences (m)")]
		public static void GroundSplitCommand(string[] args)
		{
			Terrain ground = terraineditor.terrain;
			if (ground == null || !DynamicIslands.InEditor()) { Fail("CIGroundSplit (in the editor)"); return; }
			TerrainData data = ground.terrainData;
			int res = data.heightmapResolution;
			float[,] m = data.GetHeights(0, 0, res, res);
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) m[z, x] *= data.size.y;
			float step = data.size.x / (res - 1);
			var rnd = new System.Random(7);
			float[] worst = new float[3];
			for (int i = 0; i < 4000; i++)
			{
				float x = (float)rnd.NextDouble() * data.size.x, z = (float)rnd.NextDouble() * data.size.z;
				float unity = ground.SampleHeight(ground.transform.position + new Vector3(x, 0f, z));
				for (int split = 0; split < 3; split++) worst[split] = Mathf.Max(worst[split], Mathf.Abs(IslandGenerator.TerrainSurface(m, step, x, z, split) - unity));
			}
			Log("  triangles (0,0)-(1,1): " + Num(worst[0]) + " m; the other diagonal: " + Num(worst[1]) + " m; blended: " + Num(worst[2]) + " m");
			Log((worst[0] < 0.01f ? "PASS" : "FAIL") + ": the generator's ground is the terrain's (largest difference " + Num(worst[0]) + " m)");
		}

		[ConsoleCommand(name: "CIProtoInfo", docs: "Dev, editor: how catalog objects spawn - their own rotation and scale (Raft's scene objects keep the turn and lean of the copy found in Raft's scene): CIProtoInfo <name>,<name>,...")]
		public static void ProtoInfoCommand(string[] args)
		{
			string[] names = string.Join(" ", args ?? new string[0]).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
			StartTest(ProtoInfoRoutine(names));
		}

		static IEnumerator ProtoInfoRoutine(string[] names)
		{
			yield return PlaceableCatalog.EnsureLoaded(names);
			foreach (string n in names)
			{
				GameObject p = PlaceableCatalog.Get(n);
				Log("  proto " + n + ": " + (p == null ? "not in the catalog" : "rot " + p.transform.rotation.eulerAngles.ToString("F0") + " scale " + p.transform.localScale.ToString("F2")));
			}
			Log("PASS: proto info");
		}

		[ConsoleCommand(name: "CIScene", docs: "Dev, anywhere: logs WHERE world / editor / menu (the content tools ask before building or playing)")]
		public static void WhereCommand()
		{
			Log("WHERE " + (DynamicIslands.InEditor() ? "editor" : LoadSceneManager.IsGameSceneLoaded ? "world" : "menu"));
		}

		[ConsoleCommand(name: "CIInstances", docs: "Dev: logs every object named <name> in the scene (editor or world): where it is, its parent, and where its parts are")]
		public static void InstancesCommand(string[] args)
		{
			string name = string.Join(" ", args ?? new string[0]);
			int n = 0;
			foreach (Transform tr in UnityEngine.Object.FindObjectsOfType<Transform>().Where(x => x.name == name || x.name == name + "(Clone)"))
			{
				n++;
				Log("instance " + tr.name + " at " + tr.position.ToString("F1") + " rot " + tr.eulerAngles.ToString("F0") + " in " + (tr.parent != null ? tr.parent.name : "-"));
				foreach (Transform c in tr) Log("   part " + c.name + " at " + c.position.ToString("F1") + " (local " + c.localPosition.ToString("F1") + ")");
			}
			Log("PASS: " + n + " instances of " + name);
		}

		[ConsoleCommand(name: "CIAtmoInfo", docs: "Dev: logs how Raft lights the scene where the camera is (ambient, fog, probes, directional lights, the atmosphere zone there)")]
		public static void AtmoInfoCommand()
		{
			Camera cam = Camera.main;
			if (cam == null) { Log("FAIL: no main camera"); return; }
			Log("camera " + cam.name + " at " + cam.transform.position + " path " + cam.actualRenderingPath + " hdr " + cam.allowHDR + " clear " + cam.clearFlags);
			Log("ambient " + RenderSettings.ambientMode + " intensity " + RenderSettings.ambientIntensity + " light " + RenderSettings.ambientLight + " sky " + RenderSettings.ambientSkyColor
				+ " | sun " + (RenderSettings.sun != null ? RenderSettings.sun.name : "none") + " | fog " + RenderSettings.fog + " " + RenderSettings.fogMode + " " + RenderSettings.fogDensity.ToString("F4")
				+ " | reflections " + RenderSettings.defaultReflectionMode + " " + RenderSettings.reflectionIntensity + " | probes " + (LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0)
				+ " | lightmaps " + LightmapSettings.lightmaps.Length);
			foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>().Where(l => l.type == LightType.Directional))
				Log("directional " + l.name + " i=" + l.intensity.ToString("F2") + " c=" + l.color + " shadows " + l.shadows + " on " + l.isActiveAndEnabled + " mode " + l.renderMode);
			float w;
			AtmosphereZone z = AtmosphereZone.Strongest(cam.transform.position, out w);
			Log("zone at the camera: " + (z != null ? z.name + " fog " + z.FogAmount + " light " + z.LightAmount + " " + z.Light : "none") + " weight " + w.ToString("F2"));
			Renderer near = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r => r.isVisible).OrderBy(r => (r.bounds.center - cam.transform.position).sqrMagnitude).FirstOrDefault();
			if (near != null) Log("nearest renderer " + near.name + " shader " + (near.sharedMaterial != null ? near.sharedMaterial.shader.name : "-") + " probes " + near.lightProbeUsage + " reflprobes " + near.reflectionProbeUsage);
			Log("PASS: atmo info");
		}

		[ConsoleCommand(name: "CIQuestIcons", docs: "Dev: writes the pictures of Raft's quest items (story items' icons \"quest:<type>\") to Mods\\DynamicIslands\\recipes\\questicons.txt")]
		public static void QuestIconsCommand()
		{
			Directory.CreateDirectory(RecipeFolder);
			string[] names = Enum.GetNames(typeof(QuestItemType));
			File.WriteAllLines(Path.Combine(RecipeFolder, "questicons.txt"), names.Select(n => StoryItems.QuestIcon + n).ToArray());
			Log("PASS: " + names.Length + " quest item pictures written to questicons.txt");
		}

		[ConsoleCommand(name: "CISounds", docs: "Dev, editor: writes Raft's sounds (the sound zone's list: FMOD events, [loop] marked) to Mods\\DynamicIslands\\recipes\\sounds.txt")]
		public static void SoundsCommand()
		{
			Directory.CreateDirectory(RecipeFolder);
			string[] lines = SoundLibrary.Events.Select(e => (SoundLibrary.IsLooping(e) ? "[loop] " : "") + e).ToArray();
			File.WriteAllLines(Path.Combine(RecipeFolder, "sounds.txt"), lines);
			if (lines.Length > 0) Log("PASS: " + lines.Length + " sounds written to sounds.txt"); else Fail("no sounds (Raft's sound banks aren't loaded)");
		}

		/// <summary>What the last recipe made: its island's name, the objects it placed by alias (the last one of each).</summary>
		public static string RecipeSaved;
		public static readonly Dictionary<string, EditorGameObject> RecipeObjects = new Dictionary<string, EditorGameObject>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Every object placed under an alias: prop, loot and note set them all (seven lamps that a generator
		/// shows, seven dark zones it hides - before, only the last lamp got its settings).</summary>
		static readonly Dictionary<string, List<EditorGameObject>> recipeGroups = new Dictionary<string, List<EditorGameObject>>(StringComparer.OrdinalIgnoreCase);
		/// <summary>The cave tool's caves laid by this recipe, by their as= group (caveloot, cavegate).</summary>
		static readonly Dictionary<string, CaveTool.Layout> recipeCaves = new Dictionary<string, CaveTool.Layout>(StringComparer.OrdinalIgnoreCase);
		static readonly HashSet<GameObject> recipePlaced = new HashSet<GameObject>();
		static readonly HashSet<GameObject> recipeScattered = new HashSet<GameObject>();
		/// <summary>The quest the recipe is writing (the quest window's fields before Save).</summary>
		static IslandQuest pendingQuest;
		/// <summary>The ziplines zipto aimed, cleared of the ground at the island's save, once every stroke and kit has shaped the land
		/// (11-20: Thalassa Mooring's ridge line was clear at its zipto, then kit_st's hill came up 6 m round its middle).</summary>
		static readonly List<EditorGameObject> zipPending = new List<EditorGameObject>();
		/// <summary>The ladders zipto's raise stood up its decks: not a building that holds the deck (PostBuilding) - a Block_'s
		/// box is the whole block, and 11-21 Engine Court's 6.6 m ridge deck stood on its ladders alone, its posts left out.</summary>
		static readonly HashSet<GameObject> zipDeckLadders = new HashSet<GameObject>();
		static readonly Dictionary<int, IslandQuest> pendingMore = new Dictionary<int, IslandQuest>();

		/// <summary>The recipe's quest n (0 = the main one), kept until it has steps.</summary>
		static IslandQuest PendingQuest(int n)
		{
			if (n == 0) { if (pendingQuest == null) pendingQuest = IslandQuest.From(DynamicIslands.currentIslandProps, 0, true); return pendingQuest; }
			IslandQuest q;
			if (!pendingMore.TryGetValue(n, out q)) pendingMore[n] = q = IslandQuest.From(DynamicIslands.currentIslandProps, n, true);
			return q;
		}

		#region Writing recipes: variables, arithmetic, loops, macros, includes

		class RLine { public string Text, Where; }
		class Macro { public List<string> Params; public List<RLine> Body; }

		static System.Random recipeRandom = new System.Random(1);

		static List<RLine> ReadRecipe(string path)
		{
			string[] lines = File.ReadAllLines(path);
			recipeFilesRead.Add(path);
			string file = Path.GetFileName(path);
			return lines.Select((l, i) => new RLine { Text = l, Where = file + ":" + (i + 1) }).ToList();
		}

		static List<RLine> ExpandRecipe(string path)
		{
			recipeRandom = new System.Random(Path.GetFileNameWithoutExtension(path).Aggregate(17, (h, c) => h * 31 + c));
			recipeFilesRead.Clear();
			var output = new List<RLine>();
			ExpandLines(ReadRecipe(path), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, Macro>(StringComparer.OrdinalIgnoreCase), output, 0);
			return output;
		}

		static string FirstWord(string line)
		{
			int sp = line.IndexOf(' ');
			return (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant();
		}

		/// <summary>The line that closes a block (macro ... end, for ... next), nested blocks skipped.</summary>
		static int BlockEnd(List<RLine> src, int start, string open, string close)
		{
			int depth = 0;
			for (int i = start; i < src.Count; i++)
			{
				string w = FirstWord(src[i].Text.Trim());
				if (w == open) depth++;
				else if (w == close && --depth == 0) return i;
			}
			throw new Exception(src[start].Where + ": '" + open + "' without its '" + close + "'");
		}

		static void ExpandLines(List<RLine> src, Dictionary<string, string> vars, Dictionary<string, Macro> macros, List<RLine> output, int depth)
		{
			if (depth > 40) throw new Exception("macros or loops nested too deep");
			for (int i = 0; i < src.Count; i++)
			{
				string raw = src[i].Text.Trim();
				if (raw.Length == 0 || raw.StartsWith("#")) continue;
				string first = FirstWord(raw);
				try
				{
					if (first == "macro")
					{
						string[] t = Tokens(raw);
						int end = BlockEnd(src, i, "macro", "end");
						macros[t[1]] = new Macro { Params = t.Skip(2).ToList(), Body = src.GetRange(i + 1, end - i - 1) };
						i = end;
						continue;
					}
					if (first == "for")
					{
						string[] t = Tokens(Subst(raw, vars));
						int end = BlockEnd(src, i, "for", "next");
						List<RLine> body = src.GetRange(i + 1, end - i - 1);
						double a = Eval(t[2]), b = Eval(t[3]), step = t.Length > 4 ? Eval(t[4]) : 1.0;
						if (step == 0 || (b - a) / step > 5000) throw new Exception("a loop of too many rounds");
						for (double v = a; step > 0 ? v <= b + 1e-9 : v >= b - 1e-9; v += step)
						{
							var inner = new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase);
							inner[t[1]] = Num(v);
							ExpandLines(body, inner, macros, output, depth + 1);
						}
						i = end;
						continue;
					}
					string line = Subst(raw, vars);
					if (first == "set")
					{
						string[] t = Tokens(line);
						string value = Rest(line, 2);
						double d;
						vars[t[1]] = TryEval(value, out d) ? Num(d) : value;
						continue;
					}
					if (first == "include")
					{
						string file = Path.Combine(RecipeFolder, Rest(line, 1).Trim());
						if (!file.EndsWith(".recipe") && !file.EndsWith(".play")) file += ".recipe"; // (a play test may include another: a plan's test plays its islands' own)
						if (!File.Exists(file)) throw new Exception("no file " + file);
						ExpandLines(ReadRecipe(file), vars, macros, output, depth + 1);
						continue;
					}
					if (first == "call")
					{
						string[] t = Tokens(line);
						Macro m;
						if (t.Length < 2 || !macros.TryGetValue(t[1], out m)) throw new Exception("no macro '" + (t.Length > 1 ? t[1] : "") + "'");
						var inner = new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase);
						var names = m.Params.Select(p => p.Split('=')[0]).ToList();
						var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						int position = 0;
						foreach (string arg in t.Skip(2))
						{
							int eq = arg.IndexOf('=');
							if (eq > 0 && names.Contains(arg.Substring(0, eq), StringComparer.OrdinalIgnoreCase)) given[arg.Substring(0, eq)] = arg.Substring(eq + 1);
							else if (position < names.Count) given[names[position++]] = arg;
							else throw new Exception("macro " + t[1] + ": one argument too many (" + arg + ")");
						}
						foreach (string p in m.Params)
						{
							int eq = p.IndexOf('=');
							string pname = eq > 0 ? p.Substring(0, eq) : p;
							string v;
							if (given.TryGetValue(pname, out v)) inner[pname] = v;
							else if (eq > 0) inner[pname] = Subst(p.Substring(eq + 1), inner);
							else throw new Exception("macro " + t[1] + " needs " + pname);
						}
						ExpandLines(m.Body, inner, macros, output, depth + 1);
						continue;
					}
					output.Add(new RLine { Text = line, Where = src[i].Where });
				}
				catch (Exception e) when (!(e.Message.Contains(".recipe:")))
				{
					throw new Exception(src[i].Where + " (" + raw + "): " + e.Message);
				}
			}
		}

		static readonly Regex VarRef = new Regex(@"\$([A-Za-z_][A-Za-z0-9_]*)");
		static readonly Regex Braces = new Regex(@"\{([^{}]*)\}");

		/// <summary>$variables put in, then {arithmetic} worked out.</summary>
		static string Subst(string line, Dictionary<string, string> vars)
		{
			string s = VarRef.Replace(line, m => { string v; return vars.TryGetValue(m.Groups[1].Value, out v) ? v : m.Value; });
			for (int guard = 0; guard < 20 && Braces.IsMatch(s); guard++)
				s = Braces.Replace(s, m => Num(Eval(m.Groups[1].Value)));
			return s;
		}

		static string Num(double v) { return Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture); }

		static bool TryEval(string s, out double v)
		{
			try { v = Eval(s); return true; }
			catch { v = 0; return false; }
		}

		static double Eval(string text)
		{
			var p = new ExprParser { s = text.Trim(), i = 0 };
			double v = p.Expr();
			p.Skip();
			if (p.i < p.s.Length) throw new Exception("can't work out '" + text + "'");
			return v;
		}

		class ExprParser
		{
			public string s; public int i;
			public void Skip() { while (i < s.Length && s[i] == ' ') i++; }
			bool Eat(char c) { Skip(); if (i < s.Length && s[i] == c) { i++; return true; } return false; }
			public double Expr()
			{
				double v = Term();
				while (true) { if (Eat('+')) v += Term(); else if (Eat('-')) v -= Term(); else return v; }
			}
			double Term()
			{
				double v = Power();
				while (true) { if (Eat('*')) v *= Power(); else if (Eat('/')) v /= Power(); else if (Eat('%')) v %= Power(); else return v; }
			}
			double Power()
			{
				double v = Unary();
				return Eat('^') ? Math.Pow(v, Power()) : v;
			}
			double Unary()
			{
				if (Eat('-')) return -Unary();
				if (Eat('+')) return Unary();
				return Primary();
			}
			double Primary()
			{
				Skip();
				if (Eat('(')) { double v = Expr(); if (!Eat(')')) throw new Exception("a ')' missing"); return v; }
				int start = i;
				if (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
				{
					while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
					return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
				}
				while (i < s.Length && (char.IsLetter(s[i]) || (i > start && char.IsDigit(s[i])))) i++;	// (digits after the first letter: atan2)
				string name = s.Substring(start, i - start).ToLowerInvariant();
				if (name.Length == 0) throw new Exception("a number missing at '" + s.Substring(start) + "'");
				if (name == "pi") return Math.PI;
				var args = new List<double>();
				if (!Eat('(')) throw new Exception("unknown '" + name + "'");
				if (!Eat(')'))
				{
					do args.Add(Expr()); while (Eat(','));
					if (!Eat(')')) throw new Exception("a ')' missing");
				}
				Func<int, double> a = k => k < args.Count ? args[k] : 0;
				const double D = Math.PI / 180.0;
				switch (name)
				{
					case "sin": return Math.Sin(a(0) * D);
					case "cos": return Math.Cos(a(0) * D);
					case "tan": return Math.Tan(a(0) * D);
					case "atan2": return Math.Atan2(a(0), a(1)) / D;
					case "sqrt": return Math.Sqrt(a(0));
					case "abs": return Math.Abs(a(0));
					case "min": return args.Min();
					case "max": return args.Max();
					case "floor": return Math.Floor(a(0));
					case "ceil": return Math.Ceiling(a(0));
					case "round": return Math.Round(a(0));
					case "rnd": return a(0) + recipeRandom.NextDouble() * (a(1) - a(0));
					default: throw new Exception("unknown function '" + name + "'");
				}
			}
		}

		#endregion

		#region Frames: where positions are, turned and raised for buildings

		class Frame { public Vector2 Origin; public float Yaw; public bool HasFloor; public float FloorY; }
		static readonly List<Frame> frames = new List<Frame>();
		static Vector2 recipeOrigin;

		static Frame TopFrame { get { return frames[frames.Count - 1]; } }

		static void SetOrigin(Vector2 origin)
		{
			recipeOrigin = origin;
			frames.Clear();
			frames.Add(new Frame { Origin = origin });
		}

		/// <summary>A point of the current frame (x across, z ahead) in the editor's world.</summary>
		static Vector2 WorldXZ(float x, float z)
		{
			Frame f = TopFrame;
			Vector3 v = Quaternion.Euler(0f, f.Yaw, 0f) * new Vector3(x, 0f, z);
			return new Vector2(f.Origin.x + v.x, f.Origin.y + v.z);
		}

		/// <summary>How high something goes at a point: h= metres above the sea; else y= above the frame's floor, or
		/// above the ground ("ground", or a frame without a floor).</summary>
		static float HeightAt(Vector2 w, Dictionary<string, string> opt)
		{
			if (opt.ContainsKey("h")) return DynamicIslands.EditorWaterLevel + F(opt["h"]);
			float y = opt.ContainsKey("y") ? F(opt["y"]) : 0f;
			if (TopFrame.HasFloor && !opt.ContainsKey("ground")) return TopFrame.FloorY + y;
			CaveGround.EditorCatchUp(terraineditor.terrain); // (the ground as the world will have it: the caves' cut made)
			return GroundY(w.x, w.y) + y;
		}

		#endregion

		/// <summary>The targets of the objects' actions and state checks that no object is named (obj.name), or null.</summary>
		static string UnnamedTargets(EditorGameObject[] objs)
		{
			var names = new HashSet<string>(objs.Select(o => ObjectProps.Get(o.Props, BehaviourProps.Name).Trim()).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
			// (a prop line takes one key=value: "prop a obj.name=a col.mode=box" names the object "a col.mode=box")
			string joined = names.FirstOrDefault(n => Regex.IsMatch(n, @"\s[\w.]+="));
			if (joined != null) return "obj.name '" + joined + "' holds a second setting: one key=value per prop line";
			var missing = new List<string>();
			foreach (EditorGameObject o in objs.Where(x => x.Props != null))
				foreach (KeyValuePair<string, string> kv in o.Props)
				{
					IEnumerable<string> targets = kv.Key.StartsWith(BehaviourProps.CheckPrefix) ? ObjCheck.ParseLines(kv.Value).Where(c => c.Kind == "state").Select(c => c.Target)
						: kv.Key.StartsWith(BehaviourProps.EventPrefix) || kv.Key.StartsWith(BehaviourProps.ElsePrefix) ? ObjAction.ParseLines(kv.Value).Where(a => ObjAction.HasTarget(a.Verb) && a.Verb != OrderPuzzle.Verb).Select(a => a.Target)
						: Enumerable.Empty<string>();
					foreach (string t in targets.Where(x => x.Length > 0 && !x.Equals("self", StringComparison.OrdinalIgnoreCase) && !names.Contains(x)))
						missing.Add("'" + t + "' (" + kv.Key + " of " + o.GameObjectName + ")");
				}
			return missing.Count == 0 ? null : "no object is named " + string.Join(", ", missing.Distinct().Take(6).ToArray()) + (missing.Distinct().Count() > 6 ? " and more" : "") + ": give it obj.name";
		}

		/// <summary>The plays (name, text) whose 'offset' under 'island &lt;this island&gt;' is more than 0.5 m from the land
		/// centre's (none written counts as 0 0), each as "name (offset x z, d m out)", or empty. (fix-open 11-19: the land of
		/// Old Mine, Ranger's Rest and Ironreef moved after their plays' offsets were set; their walks ran 0.6-3.7 m off and
		/// stuck on posts and a den wall - the recipe printed the new offset each time but nothing compared it.)</summary>
		internal static List<string> StaleOffsetPlays(string island, Vector2 moved, IEnumerable<KeyValuePair<string, string>> plays)
		{
			var stale = new List<string>();
			foreach (var play in plays)
			{
				bool ours = false, seen = false; Vector2 off = Vector2.zero;
				foreach (string raw in play.Value.Split('\n'))
				{
					string line = raw.Trim();
					if (line.StartsWith("island ", StringComparison.OrdinalIgnoreCase))
					{
						if (ours) break; // (a second island: its offset is that island's)
						ours = string.Equals(line.Substring(7).Replace(" keep", "").Trim(), island, StringComparison.OrdinalIgnoreCase);
						continue;
					}
					string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
					float x, z;
					if (ours && t.Length >= 3 && t[0] == "offset" && float.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
						&& float.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) { off = new Vector2(x, z); seen = true; break; }
				}
				if (!ours) continue;
				float d = (off - moved).magnitude;
				if (d > 0.5f) stale.Add(play.Key + " (offset " + (seen ? Num(off.x) + " " + Num(off.y) : "none") + ", " + d.ToString("0.0", CultureInfo.InvariantCulture) + " m out)");
			}
			return stale;
		}

		[ConsoleCommand(name: "CIStaleOffsetUnit", docs: "Dev, anywhere: CIRecipe's stale-offset check - a play of the island whose 'offset' is over 0.5 m from the land centre's is named, a close one, another island's and an island's second play section are not")]
		public static void CIStaleOffsetUnit()
		{
			bool ok = true;
			Action<bool, string> chk = (c, w) => { Log((c ? "PASS: " : "FAIL: ") + w); ok &= c; };
			var plays = new[]
			{
				new KeyValuePair<string, string>("om_caves.play", "# test\r\nisland Old Mine Islet keep\r\noffset 2.7895 -1.945\r\nwalk 1 2 to 3 4\r\n"),
				new KeyValuePair<string, string>("om_pics.play", "island Old Mine Islet\noffset -0.1 0.6\n"),
				new KeyValuePair<string, string>("om_none.play", "island Old Mine Islet\nwalk 1 2 to 3 4\n"),
				new KeyValuePair<string, string>("other.play", "island Tide Farm\noffset 5 5\n"),
				new KeyValuePair<string, string>("two.play", "island Tide Farm\nwalk 0 0 to 1 1\nisland Old Mine Islet\noffset -0.0075 0.5381\n"),
			};
			var stale = StaleOffsetPlays("Old Mine Islet", new Vector2(-0.0075f, 0.5381f), plays);
			chk(stale.Count == 2, "two stale plays: " + string.Join("; ", stale.ToArray()));
			chk(stale.Any(s => s.StartsWith("om_caves.play (offset 2.7895 -1.945, 3.7 m out")), "the old Old Mine offset, 3.7 m out");
			chk(stale.Any(s => s.StartsWith("om_none.play (offset none")), "no offset line while the centre moved 0.5 m+");
			chk(!stale.Any(s => s.StartsWith("om_pics") || s.StartsWith("other") || s.StartsWith("two")), "a close offset, another island's, a second section's right one: not named");
			chk(StaleOffsetPlays("Tide Farm", Vector2.zero, plays).Count == 1, "Tide Farm: only its 5 5 play (the two-island play's Tide part has none, 0 0 is right)");
			Log(ok ? "PASS: stale offset unit" : "FAIL: stale offset unit");
		}

		/// <summary>The island's quest steps "kill|kind" and "catch|kind", and the 'kill kind' lines of the plays (in the recipe
		/// folder) under 'island &lt;this island&gt;', that name a creature label the island doesn't place, or null. A step
		/// counts only the kind its spot spawns (Quest.Matches, CreatureSpawner) and a play kills only that kind's animals.</summary>
		static string UnplacedCreatures(string island, EditorGameObject[] objs)
		{
			List<ContentCatalog.CreatureKind> kinds = objs.Select(o => ContentCatalog.CreatureOf(o.GameObjectName)).Where(k => k != null).ToList();
			var labels = new HashSet<string>(kinds.Select(k => k.Label), StringComparer.OrdinalIgnoreCase);
			var catchable = new HashSet<string>(kinds.Where(k => k.Category == ContentCatalog.CatchableCategory).Select(k => k.Label), StringComparer.OrdinalIgnoreCase);
			var bad = new List<string>();
			Dictionary<string, string> props = DynamicIslands.currentIslandProps;
			int quests = props != null ? IslandQuest.CountIn(props) : 0;
			for (int q = 0; q < quests; q++)
				foreach (IslandQuest.Step s in IslandQuest.From(props, q).Steps.Where(x => x.Type == "kill" || x.Type == "catch"))
				{
					HashSet<string> can = s.Type == "catch" ? catchable : labels;
					string target = (s.Target ?? "").Trim();
					if (target.Length > 0 ? !can.Contains(target) : can.Count == 0) bad.Add("step " + s.Type + "|" + target + (quests > 1 ? " (quest " + (q + 1) + ")" : ""));
				}
			// (the plays: a 'kill' under this island's 'island' line - the play's own or an included one, its variables filled in -
			// waits 15 s for animals of a kind no spot spawns and fails; "kill Warthog 1.4" names a size after the kind)
			System.Random keepRandom = recipeRandom;
			try
			{
				foreach (string play in Directory.Exists(RecipeFolder) ? Directory.GetFiles(RecipeFolder, "*.play") : new string[0])
				{
					string playText;
					try { playText = File.ReadAllText(play); } catch (IOException) { continue; } // (a play deleted while the folder is read)
					if (playText.IndexOf(island, StringComparison.OrdinalIgnoreCase) < 0) continue;
					List<RLine> lines;
					try { lines = ExpandRecipe(play); } catch (Exception) { continue; }
					string at = null;
					foreach (RLine rl in lines)
					{
						string line = rl.Text.Trim();
						string verb = FirstWord(line);
						if (verb == "island") at = Rest(line, 1).Replace(" keep", "").Trim();
						if (verb != "kill" || !string.Equals(at, island, StringComparison.OrdinalIgnoreCase)) continue;
						string[] k = Rest(line, 1).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
						float size;
						string label = string.Join(" ", (k.Length > 1 && float.TryParse(k[k.Length - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out size) ? k.Take(k.Length - 1) : k).ToArray());
						if (label.Length > 0 ? !labels.Contains(label) : labels.Count == 0) bad.Add(rl.Where + " 'kill " + label + "'");
					}
				}
			}
			finally { recipeRandom = keepRandom; }
			if (bad.Count == 0) return null;
			List<string> distinct = bad.Distinct().ToList();
			return string.Join(", ", distinct.Take(6).ToArray()) + (distinct.Count > 6 ? " and more" : "") + " name" + (distinct.Count == 1 ? "s" : "") +
				" a creature the island doesn't place (" + (labels.Count > 0 ? "it places " + string.Join(", ", labels.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToArray()) : "it places none") +
				"): a step counts only the kind its spot spawns, by its label";
		}

		static IEnumerator RecipeRoutine(string name)
		{
			string path = Path.Combine(RecipeFolder, name.EndsWith(".recipe") ? name : name + ".recipe");
			if (!File.Exists(path)) { Fail("recipe: no file " + path); yield break; }
			List<RLine> lines;
			try { lines = ExpandRecipe(path); }
			catch (Exception e) { Fail("recipe " + name + ": " + e.Message); yield break; }
			RecipeSaved = null;
			recipePassed = false;
			recipeOutputs.Clear();
			recipeLandLine = null;
			RecipeObjects.Clear();
			recipeGroups.Clear();
			recipeCaves.Clear();
			recipePlaced.Clear();
			recipeScattered.Clear();
			recipeNoPosts = false;
			recipeReseat = true;
			leftOutBuried = 0;
			CaveTool.KeptBuilding = 0;
			CaveTool.KeptBuildingNames.Clear();
			pendingQuest = null;
			pendingMore.Clear();
			zipPending.Clear();
			zipDeckLadders.Clear();
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("recipe " + name + ": the editor didn't open"); yield break; }
			SetOrigin(new Vector2(500f, 500f));
			// (the objects the recipe places, loaded first - Raft's scenes for the on-demand ones)
			var wanted = lines.Select(l => Tokens(l.Text)).Where(t => t.Length > 1 && (t[0] == "place" || t[0] == "scatter" || t[0] == "line" || t[0] == "ramp" || t[0] == "beside")).Select(t => t[1])
				.Concat(lines.Select(l => Tokens(l.Text)).Where(t => t.Length > 1 && t[0] == "cliffs").SelectMany(t => t.Where(w => w.StartsWith("rocks=", StringComparison.Ordinal)).SelectMany(w => w.Substring(6).Split('|'))))
				.Concat(lines.Any(l => Tokens(l.Text).FirstOrDefault() == "store") ? new[] { RaftStore.ObjectName } : new string[0])
				.Concat(lines.Any(l => Tokens(l.Text).FirstOrDefault() == "vending") ? new[] { RaftVending.ObjectName } : new string[0]).Where(n => n != "none").Distinct().ToList();
			yield return PlaceableCatalog.EnsureLoaded(wanted);
			string missing = wanted.FirstOrDefault(w => !PlaceableCatalog.IsLoaded(w));
			if (missing != null) { Fail("recipe " + name + ": '" + missing + "' isn't an object the editor has"); yield break; }
			var gen = new StringBuilder();
			float started = Time.realtimeSinceStartup;
			int placed = 0, steps = 0;
			foreach (RLine rl in lines)
			{
				string line = rl.Text.Trim();
				string error = null;
				string[] t = Tokens(line);
				string verb = t[0].ToLowerInvariant();
				steps++;
				hbWhat = "recipe " + name; hbStep = steps; hbProg++;
				// (quest2 ... / step2 ...: the island's further quests - ROADMAP LM4)
				int questNo = 0;
				System.Text.RegularExpressions.Match qm = System.Text.RegularExpressions.Regex.Match(verb, @"^(quest|step)([2-9])$");
				if (qm.Success) { questNo = int.Parse(qm.Groups[2].Value) - 1; verb = qm.Groups[1].Value; }
				if (verb == "wait") { for (int i = 0; i < Math.Max(1, t.Length > 1 ? (int)F(t[1]) : 1); i++) yield return null; continue; }
				if (verb == "cave")
				{
					// cave from x z to x z [slope=<deg>] [end=room|open] [hill=1|0] [size=0.7-1.5] [as=<group>]: the editor's cave tool (PF-D2) - a
					// tunnel of cave pieces from the mouth (on the ground at the first point) the way of the second, a room at its end
					int cf = Array.IndexOf(t, "from"), ct = Array.IndexOf(t, "to");
					if (cf < 0 || ct < 0 || ct + 2 >= t.Length) { Fail("recipe " + name + ", " + rl.Where + " (" + line + "): cave from <x> <z> to <x> <z> [slope=] [end=room|open] [hill=1|0] [size=] [as=]"); yield break; }
					var copt = Options(t.Skip(ct + 3));
					Vector2 ca = WorldXZ(F(t[cf + 1]), F(t[cf + 2])), cb = WorldXZ(F(t[ct + 1]), F(t[ct + 2]));
					Vector3 mouth = new Vector3(ca.x, GroundY(ca.x, ca.y), ca.y);
					List<GameObject> laid = null;
					yield return CaveTool.Lay(mouth, new Vector3(cb.x, mouth.y, cb.y), copt.ContainsKey("slope") ? F(copt["slope"]) : 0f,
						!copt.ContainsKey("end") || copt["end"] != "open", !copt.ContainsKey("hill") || copt["hill"] != "0", copt.ContainsKey("size") ? F(copt["size"]) : 1f, m => laid = m);
					if (laid == null || laid.Count == 0) { Fail("recipe " + name + ", " + rl.Where + " (" + line + "): the cave wasn't laid"); yield break; }
					foreach (GameObject g in laid) recipePlaced.Add(g);
					if (copt.ContainsKey("as"))
					{
						List<EditorGameObject> cg;
						if (!recipeGroups.TryGetValue(copt["as"], out cg)) recipeGroups[copt["as"]] = cg = new List<EditorGameObject>();
						cg.AddRange(laid.Select(g => g.GetComponent<EditorGameObject>()));
						if (CaveTool.LastLayout != null) recipeCaves[copt["as"]] = CaveTool.LastLayout;
					}
					placed += laid.Count;
					continue;
				}
				if (verb == "caveloot" || verb == "cavegate")
				{
					// caveloot <group> [dirt=3] [mush=3] [stone=2] [vines=2] [titanium=0] [light=0] [wet=0]: Raft's cave loot on the floor of the
					// cave (cave tool or Dig brush) laid as=<group>, cave vines from its roof; cavegate <group> [scale=1]: Raft's machete
					// vines across its mouth (G6 kits, 11-04); wet=1 lets the flooded floor count (a sunk island's cave, w03 11-09)
					if (t.Length < 2) { Fail("recipe " + name + ", " + rl.Where + " (" + line + "): " + verb + " <group> [options]"); yield break; }
					yield return PlaceableCatalog.EnsureLoaded(CaveKitObjects);
					Physics.SyncTransforms();
					string kerr = verb == "caveloot" ? CaveLootStep(t[1], Options(t.Skip(2)), ref placed) : CaveGateStep(t[1], Options(t.Skip(2)), ref placed);
					if (kerr != null) { Fail("recipe " + name + ", " + rl.Where + " (" + line + "): " + kerr); yield break; }
					continue;
				}
				if (verb == "dig")
				{
					// dig <x,z | x,y,z | x,y,z,r> ... [| <next stroke> ...] [r=1.75] [slope=0] [fill] [as=<group>]: the editor's Dig brush
					// (G1, 11-04) - each stroke pressed at its first point and dug on toward the others in turn, as the mouse steers it
					string derr;
					List<GameObject> dug = DigStep(t, out derr);
					if (derr != null) { Fail("recipe " + name + ", " + rl.Where + " (" + line + "): " + derr); yield break; }
					var dopt = Options(t.Skip(1).Where(x => x.Contains("=")));
					foreach (GameObject g in dug) recipePlaced.Add(g);
					if (dopt.ContainsKey("as"))
					{
						List<EditorGameObject> dg;
						if (!recipeGroups.TryGetValue(dopt["as"], out dg)) recipeGroups[dopt["as"]] = dg = new List<EditorGameObject>();
						dg.AddRange(dug.Select(g => g.GetComponent<EditorGameObject>()).Where(e => e != null && !dg.Contains(e)));
					}
					placed += dug.Count;
					yield return null;
					continue;
				}
				if (verb == "load")
				{
					// load <island name>: a patch recipe - the saved island opened in the editor (its land, objects, quests, notes,
					// story items as saved), a few ops on it, then save <same name> writes it back; no ~9 min rebuild for a small fix.
					// The origin is the saved land centre (a recipe's origin after generate/randomize); a recipe without them adds
					// "origin 500 500", one whose build said "the land centre is dx dz from the recipe's origin" adds "origin shift -dx -dz"
					string island = Rest(line, 1).Trim();
					if (island.Length == 0) { Fail("recipe " + name + ", " + rl.Where + ": load <island name>"); yield break; }
					pendingQuest = null;
					pendingMore.Clear();
					gen.Length = 0;
					DynamicIslands.currentIslandName = "";
					if (!DynamicIslands.LoadIsland(island)) { Fail("recipe " + name + ", " + rl.Where + ": no saved island '" + island + "' (or the editor didn't load it)"); yield break; }
					for (float w = 0; w < 120f && DynamicIslands.currentIslandName != island; w += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
					if (DynamicIslands.currentIslandName != island) { Fail("recipe " + name + ", " + rl.Where + ": '" + island + "' not loaded after 2 minutes"); yield break; }
					yield return null; yield return null;
					Physics.SyncTransforms();
					SetOrigin(EditorLandCentre());
					Log("  loaded '" + island + "': " + PlacedEditorObjects().Count() + " objects - origin (the land centre) " + Num(recipeOrigin.x) + " " + Num(recipeOrigin.y));
					continue;
				}
				// (a generate with buildings or caves: their objects of Raft's islands loaded first)
				if (verb == "generate") yield return PlaceableCatalog.EnsureLoaded(GenBuildings.NeededNames(IslandGenSettings.FromText(gen.ToString())));
				try
				{
					switch (verb)
					{
						case "new":
							pendingQuest = null;
							pendingMore.Clear();
							zipPending.Clear();
							DynamicIslands.NewIsland();
							SetOrigin(new Vector2(500f, 500f));
							break;
						case "gen":
							// (generator settings, as the generator's saved presets write them: Name=value)
							foreach (string kv in t.Skip(1)) gen.Append(kv).Append('\n');
							break;
						case "generate":
						{
							IslandGenSettings s = IslandGenSettings.FromText(gen.ToString());
							gen.Length = 0;
							int count = IslandGenerator.GenerateInEditor(s);
							SetOrigin(EditorLandCentre());
							Log("  generated: " + count + " objects, style " + s.Style + ", radius " + s.Radius.ToString("F0") + " m - middle " + recipeOrigin.ToString("F0"));
							break;
						}
						case "randomize":
						{
							// (Raft's own island remade: the generator's Randomize existing - the island by part of its name; "like" picks
							// "something new like it" instead of "a variation of it")
							string part = t.Length > 1 ? t[1] : "";
							RaftIsland source = RaftIslands.Offered.FirstOrDefault(i => i.Scene.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
							if (source == null) { error = "no Raft island like '" + part + "' (" + string.Join(", ", RaftIslands.Offered.Select(i => i.Scene).ToArray()) + ")"; break; }
							bool like = t.Skip(2).Contains("like");
							string[] sliders = t.Skip(2).Where(x => x != "like").ToArray();
							IslandGenSettings from = IslandGenSettings.FromText(gen.ToString() + string.Join("\n", sliders));
							IslandGenSettings s = like ? RaftIslands.LikeIt(source, from) : RaftIslands.VariationOf(source, from);
							// (then its sliders changed, as in the window after picking the island: randomize Balboa Radius=140 Height=60)
							if (sliders.Length > 0) s = IslandGenSettings.FromText(s.ToText() + string.Join("\n", sliders));
							gen.Length = 0;
							int count = IslandGenerator.GenerateInEditor(s);
							SetOrigin(EditorLandCentre());
							Log("  remade " + source.Scene + ": " + count + " objects - middle " + recipeOrigin.ToString("F0"));
							break;
						}
						case "origin":
							// (origin shift dx dz: the origin moved - a patch recipe's land centre to the full recipe's origin)
							if (t.Length > 3 && t[1].ToLowerInvariant() == "shift") SetOrigin(recipeOrigin + new Vector2(F(t[2]), F(t[3])));
							else SetOrigin(t.Length > 2 ? new Vector2(F(t[1]), F(t[2])) : EditorLandCentre());
							break;
						case "push":
						{
							// push at x z [y=|h=] [yaw=] [ground]: a frame for a building (positions turned with it, heights from its floor)
							int at = Array.IndexOf(t, "at");
							if (at < 0 || at + 2 >= t.Length) { error = "push at <x> <z> [y=] [h=] [yaw=]"; break; }
							var opt = Options(t.Skip(at + 3));
							Vector2 w = WorldXZ(F(t[at + 1]), F(t[at + 2]));
							var f = new Frame { Origin = w, Yaw = TopFrame.Yaw + (opt.ContainsKey("yaw") ? F(opt["yaw"]) : 0f) };
							if (opt.ContainsKey("h") || opt.ContainsKey("y") || (TopFrame.HasFloor && !opt.ContainsKey("ground")))
							{
								f.HasFloor = true;
								f.FloorY = HeightAt(w, opt);
							}
							frames.Add(f);
							break;
						}
						case "pop":
							if (frames.Count < 2) { error = "pop without push"; break; }
							frames.RemoveAt(frames.Count - 1);
							break;
						case "style":
						{
							int style = StyleOf(t[1]);
							IslandSettingsUndo.Change(() => DynamicIslands.SetEditorStyle(style));
							break;
						}
						case "elevation":
						{
							float h = Mathf.Clamp(F(t[1]), IslandSpawner.MinElevation, IslandSpawner.MaxElevation);
							IslandSettingsUndo.Change(() => DynamicIslands.currentElevation = h);
							break;
						}
						case "island":
						case "info":
						{
							// (an island-wide setting: key=value, the value to the line's end; "info title=..." is the same as "island info.title=...")
							string rest = Rest(line, 1);
							int eq = rest.IndexOf('=');
							if (eq <= 0) { error = "island: key=value"; break; }
							string key = (verb == "info" ? "info." : "") + rest.Substring(0, eq).Trim();
							string value = Unescape(rest.Substring(eq + 1));
							IslandSettingsUndo.Change(() => { if (value.Length == 0) DynamicIslands.currentIslandProps.Remove(key); else DynamicIslands.currentIslandProps[key] = value; });
							break;
						}
						case "brush":
							error = Brush(t);
							break;
						case "clear":
						{
							// clear at x z r=<m> [all] | clear from x1 z1 to x2 z2 r=<m>: the generator's objects there (in a circle, or along
							// a trail) removed - deleted as one undo step; "all" also this recipe's own; "body" by how near its body comes (its
							// renderers' box), not its pivot (a tower or a shaft whose pivot stands metres off it - SL6, 2026-10-19); only=<name pattern> just those whose
							// name matches (only=Rock|Boulder), below=<m> / above=<m> just those under / over that height above the sea,
							// on=sand|grass|rock|seabed just those on ground painted so (a beach's boulders, not the sea's or the hills' -
							// Balboa Remade's wide sandy flats, 2-5 m up, were strewn with them, 2026-10-03)
							int at = Array.IndexOf(t, "at"), from = Array.IndexOf(t, "from"), to = Array.IndexOf(t, "to");
							if ((at < 0 || at + 2 >= t.Length) && (from < 0 || to < 0 || to + 2 >= t.Length)) { error = "clear at <x> <z> r=<m> | clear from <x1> <z1> to <x2> <z2> r=<m>"; break; }
							var opt = Options(t.Skip(at >= 0 ? at + 3 : to + 3));
							Vector2 w = at >= 0 ? WorldXZ(F(t[at + 1]), F(t[at + 2])) : WorldXZ(F(t[from + 1]), F(t[from + 2]));
							Vector2 w2 = at >= 0 ? w : WorldXZ(F(t[to + 1]), F(t[to + 2]));
							float r = opt.ContainsKey("r") ? F(opt["r"]) : 5f;
							Regex only = opt.ContainsKey("only") ? new Regex(opt["only"], RegexOptions.IgnoreCase) : null;
							float below = opt.ContainsKey("below") ? F(opt["below"]) : float.MaxValue, above = opt.ContainsKey("above") ? F(opt["above"]) : float.MinValue;
							float seaY = DynamicIslands.EditorWaterLevel;
							int onLayer = opt.ContainsKey("on") ? Array.IndexOf(new[] { "seabed", "sand", "grass", "rock" }, opt["on"].ToLowerInvariant()) : -1;
							if (opt.ContainsKey("on") && onLayer < 0) { error = "clear: on=sand|grass|rock|seabed"; break; }
							Terrain land = terraineditor.terrain;
							Func<Vector3, bool> painted = p =>
							{
								if (onLayer < 0 || land == null) return true;
								TerrainData td = land.terrainData;
								Vector3 l = p - land.transform.position;
								int ax = Mathf.Clamp(Mathf.FloorToInt(l.x / td.size.x * td.alphamapResolution), 0, td.alphamapResolution - 1);
								int az = Mathf.Clamp(Mathf.FloorToInt(l.z / td.size.z * td.alphamapResolution), 0, td.alphamapResolution - 1);
								return td.GetAlphamaps(ax, az, 1, 1)[0, 0, onLayer] >= 0.5f;
							};
							Transform root = GameObject.Find("PlacedObjects").transform;
							var gone = root.GetComponentsInChildren<EditorGameObject>()
								.Where(e => (only == null || only.IsMatch(e.GameObjectName ?? "")) && e.transform.position.y - seaY < below && e.transform.position.y - seaY >= above && painted(e.transform.position))
								.Select(e => e.gameObject)
								.Where(g => (opt.ContainsKey("all") || !recipePlaced.Contains(g)) && (t.Contains("body") ? BodyReach(g, w, w2) : DistanceToSegment(new Vector2(g.transform.position.x, g.transform.position.z), w, w2)) <= r).ToList();
							if (gone.Count > 0) CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(gone, false));
							break;
						}
						case "store":
						{
							// store [alias|-] at <x> <z> [place's options] - Raft's trading post, its store working in a world (SL7: RaftStore)
							var st = t.ToList();
							st[0] = "place";
							st.Insert(1, RaftStore.ObjectName);
							if (st.Count > 2 && st[2] == "at") st.Insert(2, "-");
							t = st.ToArray();
							goto case "place";
						}
						case "vending":
						{
							// vending [alias|-] at <x> <z> [place's options] - Raft's vending machine, working in a world (SL7b: RaftVending)
							var vm = t.ToList();
							vm[0] = "place";
							vm.Insert(1, RaftVending.ObjectName);
							if (vm.Count > 2 && vm[2] == "at") vm.Insert(2, "-");
							t = vm.ToArray();
							goto case "place";
						}
						case "place":
						{
							// place <Object> <alias|-> at <x> <z> [y=|h=] [ground] [yaw=] [tilt=] [roll=] [scale=] [sit]
							string obj = t[1], alias = t.Length > 2 ? t[2] : "-";
							int at = Array.IndexOf(t, "at");
							if (at < 0 || at + 2 >= t.Length) { error = "place: <Object> <alias> at <x> <z>"; break; }
							if (obj == "none") break; // (a macro's slot left empty: no wall there)
							var opt = Options(t.Skip(at + 3));
							if (alias == "-") opt[SkipBuried] = "1";
							EditorGameObject e;
							error = Place(obj, F(t[at + 1]), F(t[at + 2]), opt, out e);
							if (e != null)
							{
								placed++;
								if (alias != "-")
								{
									RecipeObjects[alias] = e;
									ReadyDefaults(e);
									List<EditorGameObject> group;
									if (!recipeGroups.TryGetValue(alias, out group)) recipeGroups[alias] = group = new List<EditorGameObject>();
									group.Add(e);
								}
							}
							break;
						}
						case "roof":
						{
							// roof <w> <d> at <x> <z> [y=|h=] [wood] - a hipped roof of Raft's roof blocks over w x d cells of the 1.5 m grid,
							// its -x -z corner at x z, resting on walls and pillars that stand on a deck at y (over the frame's floor) or h
							// (over the sea): RaftRoof.Hip, as the generator's huts have it (the user, 2026-10-03: no roof floating over its
							// pillars). Without a floor or h, the deck is the ground at its corner.
							int at = Array.IndexOf(t, "at");
							int rw, rd;
							if (at != 3 || at + 2 >= t.Length || !int.TryParse(t[1], out rw) || !int.TryParse(t[2], out rd) || rw < 1 || rd < 1) { error = "roof <w> <d> at <x> <z> [y=|h=] [wood]"; break; }
							var opt = Options(t.Skip(at + 3));
							float rx = F(t[at + 1]), rz = F(t[at + 2]), deckY = opt.ContainsKey("y") ? F(opt["y"]) : 0f;
							bool overSea = opt.ContainsKey("h") || !TopFrame.HasFloor;
							Vector2 corner = WorldXZ(rx, rz);
							float deckH = opt.ContainsKey("h") ? F(opt["h"]) : GroundY(corner.x, corner.y) - DynamicIslands.EditorWaterLevel + deckY;
							string err = null;
							RaftRoof.Hip((n, p, ry) =>
							{
								if (err != null) return;
								var o = new Dictionary<string, string> { { overSea ? "h" : "y", Num((overSea ? deckH : deckY) + p.y) }, { "yaw", Num(ry) } };
								EditorGameObject e;
								err = Place(n, p.x, p.z, o, out e);
								if (e != null) placed++;
							}, new Vector3(rx + PlacementOptions.GridSize / 2f, RaftRoof.OnWalls, rz + PlacementOptions.GridSize / 2f), rw, rd, opt.ContainsKey("wood"));
							error = err;
							break;
						}
						case "scatter":
						{
							// scatter <Object> <count> at x z r=<m> [seed=] [scale=a-b] [y=] [wet] [deep=<m>] [any] [yaw=] (yaw: none = random)
							int at = Array.IndexOf(t, "at");
							if (at < 0 || at + 2 >= t.Length || t.Length < 3) { error = "scatter <Object> <count> at <x> <z> r=<m>"; break; }
							var opt = Options(t.Skip(at + 3));
							float cx = F(t[at + 1]), cz = F(t[at + 2]), r = opt.ContainsKey("r") ? F(opt["r"]) : 5f;
							var rnd = new System.Random(opt.ContainsKey("seed") ? (int)F(opt["seed"]) : recipeRandom.Next());
							float s0 = 1f, s1 = 1f;
							if (opt.ContainsKey("scale")) { string[] sc = opt["scale"].Split('-'); s0 = F(sc[0]); s1 = sc.Length > 1 ? F(sc[1]) : s0; }
							int want = (int)F(t[2]), made = 0;
							for (int k = 0; k < want * 25 && made < want; k++)
							{
								double ang = rnd.NextDouble() * Math.PI * 2, dist = Math.Sqrt(rnd.NextDouble()) * r;
								float px = cx + (float)(Math.Cos(ang) * dist), pz = cz + (float)(Math.Sin(ang) * dist);
								Vector2 w = WorldXZ(px, pz);
								float ground = GroundY(w.x, w.y), sea = DynamicIslands.EditorWaterLevel;
								if (!opt.ContainsKey("any") && (opt.ContainsKey("wet") ? ground > sea - 0.6f : ground < sea + 0.3f)) continue;
								// (deep=: no deeper than this under the sea - Raft's finds in the shallows round an island, lib_sea's
								// shallows: the user, 2026-10-04)
								if (opt.ContainsKey("deep") && ground < sea - F(opt["deep"])) continue;
								// (free=: not within that many metres of anything the recipe placed - things to gather kept off the
								// buildings, the paths' props and the quest's objects: the user, 2026-10-05)
								if (opt.ContainsKey("free") && Physics.OverlapSphere(new Vector3(w.x, ground + 1f, w.y), F(opt["free"]), ~0, QueryTriggerInteraction.Ignore)
									.Any(c => c.GetComponentInParent<EditorGameObject>() != null)) continue;
								var o = new Dictionary<string, string>(opt, StringComparer.OrdinalIgnoreCase);
								o.Remove("deep"); o.Remove("free"); o.Remove("as");
								o["yaw"] = opt.ContainsKey("yaw") ? opt["yaw"] : Num(rnd.NextDouble() * 360);
								o["scale"] = Num(s0 + rnd.NextDouble() * (s1 - s0));
								o.Remove("r"); o.Remove("seed");
								// (on the terrain it was picked on, also in a frame with a floor: Ironreef's Drowned Shaft scattered its
								// boulders at its floor 4 m under the sea - under the banks they were picked on, 2026-10-02)
								if (!o.ContainsKey("y") && !o.ContainsKey("h")) o["ground"] = "1";
								EditorGameObject e;
								error = Place(t[1], px, pz, o, out e);
								if (error != null) break;
								made++; placed++;
								// (a scattered one with no alias is the recipe's dressing: zipto clears it from a rider's way like the
								// generator's - 11-21, Caisson Yard's ridge line ran 7.4 m under an sk_plants boulder)
								if (e != null && !opt.ContainsKey("as")) recipeScattered.Add(e.gameObject);
								// (as=<alias>: the scattered objects can be given settings, loot and behaviours like placed ones)
								if (e != null && opt.ContainsKey("as"))
								{
									RecipeObjects[opt["as"]] = e;
									ReadyDefaults(e);
									List<EditorGameObject> sg;
									if (!recipeGroups.TryGetValue(opt["as"], out sg)) recipeGroups[opt["as"]] = sg = new List<EditorGameObject>();
									sg.Add(e);
								}
							}
							break;
						}
						case "line":
						{
							// line <Object> from x1 z1 to x2 z2 step=<m> [ends] [yaw=offset] [y=|h=] [sit]: a row along a line, each turned along it
							// (follow: end to end over the ground instead - LineFollow)
							int from = Array.IndexOf(t, "from"), to = Array.IndexOf(t, "to");
							if (from < 0 || to < 0 || to + 2 >= t.Length) { error = "line <Object> from <x1> <z1> to <x2> <z2> step=<m>"; break; }
							var opt = Options(t.Skip(to + 3));
							opt[SkipBuried] = "1";
							float x1 = F(t[from + 1]), z1 = F(t[from + 2]), x2 = F(t[to + 1]), z2 = F(t[to + 2]);
							float len = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1)), step = opt.ContainsKey("step") ? F(opt["step"]) : 1.5f;
							int n = Mathf.Max(1, Mathf.RoundToInt(len / step));
							// (the yaw that turns an object's x axis along the line, in the frame)
							float yaw = Mathf.Atan2(-(z2 - z1), x2 - x1) * Mathf.Rad2Deg + (opt.ContainsKey("yaw") ? F(opt["yaw"]) : 0f);
							bool ends = opt.ContainsKey("ends");
							if (opt.ContainsKey("follow")) { error = LineFollow(t[1], x1, z1, x2, z2, yaw, opt, ref placed); break; }
							for (int k = 0; k < n && error == null; k++)
							{
								float f = len > 0 ? (k + (ends ? 0f : 0.5f)) * step / len : 0f;
								var o = new Dictionary<string, string>(opt, StringComparer.OrdinalIgnoreCase);
								o["yaw"] = Num(yaw);
								o.Remove("step"); o.Remove("ends");
								EditorGameObject e;
								error = Place(t[1], x1 + (x2 - x1) * f, z1 + (z2 - z1) * f, o, out e);
								if (e != null) placed++;
							}
							break;
						}
						case "ramp":
						{
							// ramp <Object> from x1 z1 h1 to x2 z2 h2 [len=6] [top=1]: pieces end to end from one height to another (h above the
							// sea, or above the frame's floor in a frame with one), each turned along the way and tilted with it, stretched
							// to fill it exactly (their long side is their x; top = their walking surface above their pivot) - walkways,
							// ramps and bridges between platforms
							int from = Array.IndexOf(t, "from"), to = Array.IndexOf(t, "to");
							if (from < 0 || to < 0 || from + 3 >= t.Length || to + 3 >= t.Length) { error = "ramp <Object> from <x1> <z1> <h1> to <x2> <z2> <h2> [len=] [top=]"; break; }
							var opt = Options(t.Skip(to + 4));
							float x1 = F(t[from + 1]), z1 = F(t[from + 2]), h1 = F(t[from + 3]), x2 = F(t[to + 1]), z2 = F(t[to + 2]), h2 = F(t[to + 3]);
							float piece = opt.ContainsKey("len") ? F(opt["len"]) : 6f, top = opt.ContainsKey("top") ? F(opt["top"]) : 1f;
							float run = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1)), rise = h2 - h1;
							float slope = Mathf.Sqrt(run * run + rise * rise);
							int n = Mathf.Max(1, Mathf.CeilToInt(slope / piece - 0.05f));
							float yaw = Mathf.Atan2(-(z2 - z1), x2 - x1) * Mathf.Rad2Deg, roll = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
							float floor = TopFrame.HasFloor ? TopFrame.FloorY - DynamicIslands.EditorWaterLevel : 0f;
							for (int k = 0; k < n && error == null; k++)
							{
								float f = (k + 0.5f) / n;
								// (the pivot under the walking surface, along the tilted piece's up)
								float hh = h1 + rise * f + floor - top * Mathf.Cos(roll * Mathf.Deg2Rad);
								var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
								{
									{ "h", Num(hh) }, { "yaw", Num(yaw) }, { "roll", Num(roll) }, { "sx", Num(slope / n / piece) }
								};
								if (opt.ContainsKey("rot")) o["rot"] = opt["rot"];
								o[SkipBuried] = "1"; // (a bridge's first plank inside the bank: left out, as line's)
								EditorGameObject e;
								error = Place(t[1], x1 + (x2 - x1) * f, z1 + (z2 - z1) * f, o, out e);
								if (e != null) placed++;
							}
							break;
						}
						case "prop":
						case "beh":
						{
							// prop <alias> key=value (the value to the line's end, \n for new lines; empty removes the key) - on
							// every object placed under the alias
							List<EditorGameObject> group = Group(t[1]);
							if (group.Count == 0) { error = "no object '" + t[1] + "' placed by this recipe"; break; }
							string rest = Rest(line, 2);
							int eq = rest.IndexOf('=');
							if (eq <= 0) { error = "prop: key=value"; break; }
							string key = rest.Substring(0, eq).Trim(), value = Unescape(rest.Substring(eq + 1));
							foreach (EditorGameObject e in group) PropsCommand.Change(e, ObjectProps.With(e.Props, key, value.Length == 0 ? null : value));
							break;
						}
						case "beside":
						{
							// beside <Object> <alias|-> <of-alias> d=<m> [yaw=<deg>] [turn=<deg>] - next to an object the recipe placed:
							// d metres in front of it (its facing turned by yaw), on the ground there, facing it (+turn)
							List<EditorGameObject> of = t.Length > 3 ? Group(t[3]) : new List<EditorGameObject>();
							if (of.Count == 0) { error = "beside <Object> <alias> <of-alias> d=<m>: no object '" + (t.Length > 3 ? t[3] : "") + "'"; break; }
							var opt = Options(t.Skip(4));
							float d = opt.ContainsKey("d") ? F(opt["d"]) : 2f, turn = opt.ContainsKey("turn") ? F(opt["turn"]) : 0f;
							Transform b = of[0].transform;
							Vector3 dir = Quaternion.Euler(0f, opt.ContainsKey("yaw") ? F(opt["yaw"]) : 0f, 0f) * Vector3.ProjectOnPlane(b.forward, Vector3.up).normalized;
							Vector3 at = b.position + dir * d;
							EditorGameObject e;
							var po = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "ground", "1" } };
							error = Place(t[1], 0f, 0f, po, out e);
							if (error != null || e == null) break;
							e.transform.position = new Vector3(at.x, GroundY(at.x, at.z), at.z);
							e.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(0f, turn, 0f);
							placed++;
							if (t[2] != "-")
							{
								RecipeObjects[t[2]] = e;
								ReadyDefaults(e);
								List<EditorGameObject> g2;
								if (!recipeGroups.TryGetValue(t[2], out g2)) recipeGroups[t[2]] = g2 = new List<EditorGameObject>();
								g2.Add(e);
							}
							break;
						}
						case "zipfrom":
						{
							// zipfrom <alias> <x> <z> [h=|y=|ground] - moves a zipline line placed by this recipe so its near end's floor (where
							// players step onto it) stands at x z, as the editor's "Near end: where I look": the line's pivot is not that floor -
							// placed by its pivot on a tower's deck, the floor hung 3-7 m off the deck in the air (IG5, 11-18)
							List<EditorGameObject> zg = Group(t[1]);
							if (zg.Count == 0 || t.Length < 4) { error = "zipfrom <alias> <x> <z> [h=|y=|ground] (a zipline placed by this recipe)"; break; }
							Vector2 zw = WorldXZ(F(t[2]), F(t[3]));
							Vector3 zat = new Vector3(zw.x, HeightAt(zw, Options(t.Skip(4))), zw.y);
							foreach (EditorGameObject e in zg)
							{
								Vector3? nf = ZiplineEnds.NearFloor(e.gameObject);
								if (!nf.HasValue) { error = "zipfrom: " + t[1] + " is not a zipline line"; break; }
								e.transform.position += zat - nf.Value;
							}
							break;
						}
						case "zipto":
						{
							// zipto <alias> <x> <z> - a zipline line's far end (its lower floor) on the ground at x z (ZiplineEnds)
							List<EditorGameObject> group = Group(t[1]);
							if (group.Count == 0 || t.Length < 4) { error = "zipto <alias> <x> <z> (a zipline placed by this recipe)"; break; }
							Vector2 w = WorldXZ(F(t[2]), F(t[3]));
							Vector3 end = new Vector3(w.x, GroundY(w.x, w.y), w.y) - terraineditor.terrain.transform.position;
							foreach (EditorGameObject e in group)
							{
								PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(end)));
								if (!zipPending.Contains(e)) zipPending.Add(e);
							}
							break;
						}
						case "loot":
						{
							List<EditorGameObject> group = Group(t[1]);
							if (group.Count == 0) { error = "no object '" + t[1] + "' placed by this recipe"; break; }
							// loot <alias> Plank*4@25;Nail*2 [pick=N]: an @ chance (percent) or pick= makes it a random chest (QE14)
							string text = Rest(line, 2);
							int pick = -1;
							System.Text.RegularExpressions.Match pm = System.Text.RegularExpressions.Regex.Match(text, @"\s+pick=(\d+)\s*$");
							if (pm.Success) { pick = int.Parse(pm.Groups[1].Value); text = text.Substring(0, pm.Index); }
							string chances;
							string items = RandomLoot.SplitRecipe(text, out chances);
							foreach (EditorGameObject e in group)
							{
								Dictionary<string, string> p = ObjectProps.With(e.Props, ObjectProps.LootItems, items);
								if (chances != null || pick >= 0)
								{
									p = ObjectProps.With(p, RandomLoot.Mode, RandomLoot.RandomMode);
									p = ObjectProps.With(p, RandomLoot.Chance, string.IsNullOrEmpty(chances) ? null : chances);
									p = ObjectProps.With(p, RandomLoot.Pick, pick > 0 ? pick.ToString() : null);
								}
								PropsCommand.Change(e, p);
							}
							break;
						}
						case "note":
						{
							// note <alias> <title> | <text>  (the note editor's Apply)
							List<EditorGameObject> group = Group(t[1]);
							if (group.Count == 0) { error = "no object '" + t[1] + "' placed by this recipe"; break; }
							string rest = Rest(line, 2);
							int bar = rest.IndexOf('|');
							foreach (EditorGameObject e in group) NoteEditorWindow.Apply(e, (bar < 0 ? rest : rest.Substring(0, bar)).Trim(), bar < 0 ? "" : Unescape(rest.Substring(bar + 1)).Trim());
							break;
						}
						case "story":
						{
							// story id|name|icon|description (the story items window: one item added or replaced)
							StoryItemDef def = StoryItemDef.Parse(Rest(line, 1));
							if (def == null) { error = "story: id|name|icon|description"; break; }
							IslandSettingsUndo.Change(() =>
							{
								List<StoryItemDef> defs = StoryItems.Of(DynamicIslands.currentIslandProps).Where(d => d.Id != def.Id).ToList();
								defs.Add(def);
								DynamicIslands.currentIslandProps[StoryItems.Key] = StoryItems.Text(defs);
							});
							break;
						}
						case "quest":
						{
							// quest title=... | intro=... | done=... | reward=...  (one field a line; the quest editor's Apply)
							string rest = Rest(line, 1);
							int eq = rest.IndexOf('=');
							if (eq <= 0) { error = "quest: field=text"; break; }
							string field = rest.Substring(0, eq).Trim().ToLowerInvariant(), value = Unescape(rest.Substring(eq + 1)).Trim();
							// (as the quest window: its fields are kept until the quest has a step - one without steps is no quest)
							IslandQuest q = PendingQuest(questNo);
							if (field == "title") q.Title = value; else if (field == "intro") q.Intro = value; else if (field == "done") q.Done = value; else if (field == "reward") q.Reward = value;
							else { error = "quest: title, intro, done or reward"; break; }
							int qn = questNo;
							if (q.Exists) IslandSettingsUndo.Change(() => { q.To(DynamicIslands.currentIslandProps, qn); EditorUI.RefreshIsland(); });
							break;
						}
						case "step":
						{
							// step type|target|count|text
							string[] p = Rest(line, 1).Split('|');
							if (p.Length < 4) { error = "step: type|target|count|text"; break; }
							if (!IslandQuest.Types.Contains(p[0].Trim())) { error = "step: no step type '" + p[0].Trim() + "' (" + string.Join(", ", IslandQuest.Types) + ")"; break; }
							var step = new IslandQuest.Step { Type = p[0].Trim(), Target = p[1].Trim(), Count = Math.Max(1, (int)F(p[2])), Text = Unescape(string.Join("|", p.Skip(3).ToArray())).Trim() };
							IslandQuest sq = PendingQuest(questNo);
							sq.Steps.Add(step);
							int sn = questNo;
							IslandSettingsUndo.Change(() => { sq.To(DynamicIslands.currentIslandProps, sn); EditorUI.RefreshIsland(); });
							break;
						}
						case "questbring":
						{
							IntroRule r = IntroRule.Parse(Rest(line, 1));
							if (r == null) { error = "questbring: a rule line"; break; }
							IslandSettingsUndo.Change(() => QuestEditorWindow.SetQuestBringRule(DynamicIslands.currentIslandProps, r));
							break;
						}
						case "rule":
						{
							IntroRule r = IntroRule.Parse(Rest(line, 1));
							if (r == null) { error = "rule: a rule line (id | what | when | where | message | label)"; break; }
							IslandSettingsUndo.Change(() =>
							{
								List<IntroRule> rules = WorldDirector.RulesFromProps(DynamicIslands.currentIslandProps).Where(x => x.Id != r.Id).ToList();
								rules.Add(r);
								WorldDirector.SetRulesInProps(DynamicIslands.currentIslandProps, rules);
							});
							break;
						}
						case "posts":
						{
							// posts [off] - posts under the floors and decks over nothing now (StandPosts; a save does it too), or
							// "off": not at the save either (a deck meant to hang - a cable car's, a crane's)
							if (t.Length > 1 && t[1].ToLowerInvariant() == "off") recipeNoPosts = true;
							else StandPosts();
							break;
						}
						case "reseat":
						{
							// reseat trees | off - at the save, trees and palms whose trunk foot is more than 1 m under the land or more than
							// 0.4 m over it set back on it. Every recipe does it since IG5 (11-18: 905 sunk trunks on 152 saved islands);
							// "off" keeps them where the recipe put them
							string rw = t.Length < 2 ? "" : t[1].ToLowerInvariant();
							if (rw != "trees" && rw != "off") { error = "reseat trees | off"; break; }
							recipeReseat = rw == "trees";
							break;
						}
						case "save":
						{
							if (pendingQuest != null && !pendingQuest.Exists && pendingQuest.Title.Length > 0) { error = "the quest '" + pendingQuest.Title + "' has no steps"; break; }
							string island = Rest(line, 1).Trim();
							string why = FileNames.IslandProblem(island);
							if (why != null) { error = why; break; }
							// (the ziplines zipto aimed: their ropes over the land as it ends up - ZipClearGround)
							// (over the cave hill a world builds over the island's cave pieces, made now to measure against - 11-21 c3z9)
							// (a far end set on the ground before a later step raised it - the cliffs talus under Caisson Yard's crane line
							// left its last 3 m of rope 0.07 m in the ground, c3z61 - goes back up onto the ground as it ends up)
							foreach (EditorGameObject ze in zipPending)
							{
								if (ze == null || terraineditor.terrain == null) continue;
								Vector3? zf = ZiplineEnds.FarEnd(ze.Props);
								if (!zf.HasValue) continue;
								Vector3 ztp = terraineditor.terrain.transform.position, zw = zf.Value + ztp;
								float zg = GroundY(zw.x, zw.z);
								if (zg <= zw.y + 0.02f) continue;
								PropsCommand.Change(ze, ObjectProps.With(ze.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(new Vector3(zw.x, zg, zw.z) - ztp)));
								Log("  zipto: " + ze.GameObjectName + "'s far end raised " + Num(zg - zw.y) + " m onto the ground a later step raised");
							}
							GameObject zipHill = zipPending.Any(ze => ze != null) ? CaveMountain.Preview(PlacedEditorObjects(), terraineditor.terrain != null ? terraineditor.terrain.transform.position : Vector3.zero) : null;
							if (zipHill != null) Log("  zipto: ropes measured over the cave hill a world builds over the island's caves too");
							try { foreach (EditorGameObject ze in zipPending) if (ze != null) { ZipClearGround(ze); GameObject fd; ZipFarClear(ze, out fd); ZipShaveGround(ze, zipHill); ZipClearThrough(ze); } }
							finally { if (zipHill != null) UnityEngine.Object.DestroyImmediate(zipHill); Physics.SyncTransforms(); }
							zipPending.Clear();
							// (Raft's sea finds the recipe's own terrain strokes carried out of the water - a mountain raised over the shelf
							// the generator had dressed: Ironreef's ore lay 30-57 m up its slope - are the sea's: removed, the review 2026-10-03)
							float seaY = DynamicIslands.EditorWaterLevel;
							var dried = GameObject.Find("PlacedObjects").transform.GetComponentsInChildren<EditorGameObject>()
								.Where(e => RaftLand.UnderWaterOnly(e.GameObjectName) && e.transform.position.y > seaY - 0.3f && GroundY(e.transform.position.x, e.transform.position.z) > seaY)
								.Select(e => e.gameObject).ToList();
							if (dried.Count > 0)
							{
								CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(dried, false));
								Log("  " + dried.Count + " of Raft's sea finds out of the water (carried up by the ground) left out");
							}
							// (and trees and palms whose trunk foot ended more than 1 m under the land - the generator's, set down by the
							// lowest ground under their crown, then the recipe's strokes raised the ground round them: Old Mine Islet's pines
							// and birches 1-3 m into the slope, the ground check 2026-10-31; up to the ground round the pivot, as Place sets them)
							// (every recipe since IG5, 11-18 - before it only with "reseat trees", the release 2026-10-31; "reseat off" keeps them)
							if (recipeReseat) ReseatSunkTrees(seaY);
							// (and plants, rocks, corals and finds the recipe's own strokes buried whole - a footing flattened over the shelf:
							// Signal Rock's table coral 0.3 m under its platform's sea floor; nothing showed of them, the review 2026-10-03)
							KeptByMiddle.Clear();
							var covered = GameObject.Find("PlacedObjects").transform.GetComponentsInChildren<EditorGameObject>()
								.Where(e => e.gameObject.activeSelf && (NotABase.IsMatch(e.GameObjectName ?? "") || RaftUnderwater.CategoryOf(e.GameObjectName ?? "") != null) &&
									(e.GameObjectName ?? "").IndexOf("Cave", StringComparison.OrdinalIgnoreCase) < 0 && (e.GameObjectName ?? "").IndexOf("Tunnel", StringComparison.OrdinalIgnoreCase) < 0 && BuriedWhole(e.gameObject))
								.Select(e => e.gameObject).ToList();
							if (covered.Count > 0)
							{
								CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(covered, false));
								Log("  " + covered.Count + " plants, rocks, corals or finds under the ground (buried by the recipe's strokes) left out");
							}
							// (building pieces a cave laid after them would have cleared - the cave tool keeps them now and counts them; a
							// deck or rail in a cave's way usually means the cave line belongs before the deck, as Overpass Shoal, c3z29 11-21)
							if (CaveTool.KeptBuilding > 0) Log("  WARNING: " + CaveTool.KeptBuilding + " building pieces in a cave's way kept (placed before the cave): " + string.Join(" | ", CaveTool.KeptBuildingNames.ToArray()));
							if (KeptByMiddle.Count > 0) Log("  " + KeptByMiddle.Count + " kept though under the ground, their middle in a dug passage: " + string.Join("; ", KeptByMiddle.Take(12).ToArray()));
							// (and posts under the floors and decks over nothing - CA30: the corners nothing holds, down to the ground or
							// the sea floor; "posts off" in the recipe leaves them out)
							// (and loose rocks in the air set down, things buried in the ground set up on it - what the ground check would
							// call floating rocks and buried things: SettleAtSave, 11-21)
							SettleAtSave(seaY);
							if (!recipeNoPosts) StandPosts();
							// (actions and checks that name an object nothing is called: Rig Seventeen's lever looked at three valves
							// with no obj.name and could never pass, the review 2026-10-10)
							string unnamed = UnnamedTargets(GameObject.Find("PlacedObjects").transform.GetComponentsInChildren<EditorGameObject>(true));
							if (unnamed != null) { error = unnamed; break; }
							// (and kill and catch steps, and the plays' 'kill' lines, that name a creature the island doesn't place:
							// Caravan Town's "kill|Screecher" waited for Raft's screecher by its Caravan screecher - af74662, the full
							// run 2026-10-20; only what is saved counts, the active objects)
							string unplaced = UnplacedCreatures(island, GameObject.Find("PlacedObjects").transform.GetComponentsInChildren<EditorGameObject>());
							if (unplaced != null) { error = unplaced; break; }
							if (!DynamicIslands.SaveIsland(island)) { error = "the island didn't save as '" + island + "'"; break; }
							RecipeSaved = island;
							recipeOutputs.Add(IslandSpawner.PathFor(island));
							Log("  saved '" + island + "'");
							// (a world puts an island's land centre - the land above the sea, as saved - where it spawns; the recipe's
							// points are from its origin, the land centre when it was generated: land changed since moves them)
							Vector2 moved = EditorLandCentre(true) - frames[0].Origin;
							// (logged however small: land added under 0.5 m from the origin made it say nothing, and the island's plays
							// kept an old 'offset' 1 m out - The Abyss's vent pool, 11-05)
							recipeLandLine = "  the land centre is " + Num(moved.x) + " " + Num(moved.y) + " from the recipe's origin: play tests of '" + island + "' need 'offset " + Num(moved.x) + " " + Num(moved.y) + "'" + (moved.magnitude > 0.5f ? "" : " (or none: under 0.5 m)");
							Log(recipeLandLine);
							// (and names the plays that still have another: they walk and aim that far off - fix-open 11-19)
							var playTexts = new List<KeyValuePair<string, string>>();
							foreach (string pf in Directory.Exists(RecipeFolder) ? Directory.GetFiles(RecipeFolder, "*.play") : new string[0])
							{
								string pt;
								try { pt = File.ReadAllText(pf); } catch (IOException) { continue; }
								if (pt.IndexOf(island, StringComparison.OrdinalIgnoreCase) >= 0) playTexts.Add(new KeyValuePair<string, string>(Path.GetFileName(pf), pt));
							}
							var stalePlays = StaleOffsetPlays(island, moved, playTexts);
							if (stalePlays.Count > 0) Log("  WARNING: plays whose offset is not the land centre (if their points are the recipe's, set 'offset " + Num(moved.x) + " " + Num(moved.y) + "'; a play passing with it wrote its points in an older frame): " + string.Join("; ", stalePlays.ToArray()));
							break;
						}
						case "plan":
						{
							// (a world plan made in the World plans window: plan new <name> / description <text> / random on|off /
							// story on|off / leaveout <Raft story island> / rule <rule line> / save - Check finding a problem stops it)
							string what = t.Length > 1 ? t[1].ToLowerInvariant() : "", arg = Rest(line, 2).Trim();
							if (what != "new" && WorldPlanWindow.Plan == null) { error = "plan " + what + ": no plan yet (plan new <name>)"; break; }
							switch (what)
							{
								case "new":
								{
									string why = FileNames.IslandProblem(arg);
									if (why != null || WorldPlan.IsBuiltIn(arg)) { error = why ?? "'" + arg + "' is a built-in plan"; break; }
									WorldPlanWindow.RecipeNew(arg);
									if (!WorldPlanWindow.IsOpen || WorldPlanWindow.Plan == null || WorldPlanWindow.Plan.Name != arg) error = "World plans didn't open on a new plan '" + arg + "'";
									break;
								}
								case "description": WorldPlanWindow.RecipeDescription(Unescape(arg)); break;
								case "ending": WorldPlanWindow.RecipeEnding(IntroRule.UnMulti(Unescape(arg))); break;
								case "credits":
								{
									int bar = arg.IndexOf('|');
									if (bar < 0) WorldPlanWindow.RecipeCredits(Unescape(arg), "");
									else WorldPlanWindow.RecipeCredits(Unescape(arg.Substring(0, bar)), Unescape(arg.Substring(bar + 1)));
									break;
								}
								case "random": WorldPlanWindow.RecipeRandom(arg == "on"); break;
								case "story": WorldPlanWindow.RecipeStory(arg == "on"); break;
								case "leaveout":
								{
									ChunkPointType st = StoryOrder.Parse(arg);
									if (st == ChunkPointType.None || !WorldPlanWindow.RecipeLeaveOut(StoryOrder.Key(st))) error = "no story island '" + arg + "' to leave out";
									break;
								}
								case "rule":
								{
									IntroRule r = IntroRule.Parse(arg);
									if (r == null) { error = "plan rule: a rule line (id | what | when | where | message | label [| story place | done when])"; break; }
									WorldPlanWindow.RecipeRule(r);
									break;
								}
								case "save":
								{
									string planName = WorldPlanWindow.Plan.Name;
									List<string> problems = WorldPlanWindow.RecipeSave();
									foreach (string f in WorldPlanWindow.RecipeFindings()) Log("  check: " + f);
									if (problems.Count > 0) { error = "Check found " + problems.Count + " problem(s): " + string.Join(" | ", problems.ToArray()); break; }
									if (!File.Exists(WorldPlan.PathFor(planName))) { error = "the plan didn't save as '" + planName + "'"; break; }
									WorldPlanWindow.Close();
									RecipeSaved = planName;
									recipeOutputs.Add(WorldPlan.PathFor(planName));
									Log("  saved plan '" + planName + "' (" + WorldPlan.Load(planName).Rules.Count + " rules)");
									break;
								}
								default: error = "plan new|description|ending|random|story|leaveout|rule|save"; break;
							}
							break;
						}
						case "log":
							Log("  " + Rest(line, 1));
							break;
						case "ground":
						{
							// ground x z [x z ...]: logs the land's height above the sea at those points (where a stroke's h= did or didn't reach)
							// (ground cut x z ...: the caves' ground cut made first, as a world spawns it)
							int g0 = 1, gd;
							if (t.Length > 1 && t[1] == "cut") { g0 = 2; CaveGround.EditorNow(terraineditor.terrain, out gd); }
							var sb = new System.Text.StringBuilder(g0 == 2 ? "  ground (cut):" : "  ground:");
							for (int gi = g0; gi + 1 < t.Length; gi += 2)
							{
								Vector2 gp = WorldXZ(F(t[gi]), F(t[gi + 1]));
								sb.Append(" " + t[gi] + "," + t[gi + 1] + "=" + Num(GroundY(gp.x, gp.y) - DynamicIslands.EditorWaterLevel));
							}
							Log(sb.ToString());
							break;
						}
						case "cliffs":
						{
							// cliffs [rise=7] [step=3] [depth=10] [reach=12] [at x z r=20] [rocks=piece] [keep=x,z,r|..] [walls=x,z,r|..]: bare cliff faces at the waterline
							// sloped from their low side (CliffsStep, c3 round 3 - Raft draws them as one stretched sheet; CISheetIslands
							// finds them); rocks=: that rock piece stood in the waterline at the faces the slope can't reach (a pit's wall)
							var co = Options(t.Skip(1).Where(w => w.Contains("=")));
							float crise = co.ContainsKey("rise") ? F(co["rise"]) : SheetRise, cstep = co.ContainsKey("step") ? F(co["step"]) : 3f;
							float cdepth = co.ContainsKey("depth") ? F(co["depth"]) : 10f, cr = co.ContainsKey("r") ? F(co["r"]) : 20f;
							int creach = co.ContainsKey("reach") ? Mathf.Clamp(int.Parse(co["reach"]), 1, 40) : 12;
							int cat = Array.IndexOf(t, "at");
							Vector2? cwhere = null;
							if (cat > 0 && cat + 2 < t.Length) cwhere = WorldXZ(F(t[cat + 1]), F(t[cat + 2]));
							if (cstep < 0.5f || crise < 2f) { error = "cliffs: step= 0.5 or more, rise= 2 or more"; break; }
							// (keep=x,z,r|x,z,r: circles in the recipe's coordinates the talus leaves as they are - a dock pool, a dive site)
							// (walls=x,z,r|..: circles no talus is raised in either, but whose bare faces get the rocks= - a dive site's pit walls
							// rocked, its floor and the boat on it kept - lib 3.5.1 11-23: keep left Cistern Hall's and the dive sites' pits bare sheets)
							List<Vector3> ckeep = null, cwalls = null;
							foreach (string ck in new[] { "keep", "walls" })
							{
								if (!co.ContainsKey(ck)) continue;
								var circles = new List<Vector3>();
								foreach (string kc in co[ck].Split('|'))
								{
									string[] kp = kc.Split(',');
									if (kp.Length != 3) { error = "cliffs: " + ck + "=x,z,r|x,z,r (" + kc + ")"; break; }
									Vector2 kw = WorldXZ(F(kp[0]), F(kp[1]));
									circles.Add(new Vector3(kw.x, kw.y, F(kp[2])));
								}
								if (ck == "keep") ckeep = circles; else cwalls = circles;
							}
							if (error != null) break;
							if (cwalls != null && !co.ContainsKey("rocks")) { error = "cliffs: walls= needs rocks="; break; }
							Log("  " + CliffsStep(crise, cstep, cdepth, creach, cwhere, cr, co.ContainsKey("rocks") ? co["rocks"] : null, ckeep, cwalls));
							break;
						}
						case "where":
						{
							// where <alias>: logs where the objects placed under it ended up (island x z, height above the sea, turn)
							Frame root = frames[0];
							foreach (EditorGameObject e in Group(t[1]))
							{
								Vector3 p = e.transform.position;
								Log("  where " + t[1] + ": " + Num(p.x - root.Origin.x) + " " + Num(p.z - root.Origin.y) + " h=" + Num(p.y - DynamicIslands.EditorWaterLevel) + " rot " + e.transform.eulerAngles.ToString("F0"));
							}
							break;
						}
						default:
							error = "unknown step '" + verb + "'";
							break;
					}
				}
				catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
				if (error != null) { EditorUI.RefreshIsland(); Fail("recipe " + name + ", " + rl.Where + " (" + line + "): " + error); yield break; }
				if (steps % 40 == 0) yield return null;
			}
			EditorUI.RefreshIsland();
			recipePassed = true;
			Log("PASS: recipe " + name + " (" + steps + " steps, " + placed + " objects placed, " + (leftOutBuried > 0 ? leftOutBuried + " left out under the ground, " : "") + (Time.realtimeSinceStartup - started).ToString("F0") + " s" + (RecipeSaved != null ? ", saved '" + RecipeSaved + "'" : "") + ")");
		}

		[ConsoleCommand(name: "CIView", docs: "Dev, editor: pictures of the editor's island without the editor's panels (Mods\\DynamicIslands\\recipes\\view_<name>_<n>.jpg, 1280x720): from four sides and from above, framed on the land - or one view from a point to a point (metres from the island's middle, heights above the sea): CIView <name> [x y z lookx looky lookz]")]
		public static void ViewCommand(string[] args) { StartTest(ViewRoutine(args ?? new string[0])); }

		static IEnumerator ViewRoutine(string[] args)
		{
			Camera cam = Camera.main;
			if (!DynamicIslands.InEditor() || cam == null) { Fail("view: open the editor first"); yield break; }
			string name = args.Length > 0 ? args[0] : "island";
			Vector2 mid; float radius, top;
			EditorLand(out mid, out radius, out top);
			float sea = DynamicIslands.EditorWaterLevel;
			var views = new List<KeyValuePair<Vector3, Vector3>>();
			Func<float, float, float, Vector3> at = (x, y, z) => new Vector3(mid.x + x, sea + y, mid.y + z);
			if (args.Length >= 7)
			{
				float[] f = args.Skip(1).Take(6).Select(F).ToArray();
				views.Add(new KeyValuePair<Vector3, Vector3>(at(f[0], f[1], f[2]), at(f[3], f[4], f[5])));
			}
			else
			{
				float r = Mathf.Max(radius, 12f), h = Mathf.Max(top - sea, 2f);
				foreach (float deg in new[] { 200f, 290f, 20f, 110f })
				{
					float a = deg * Mathf.Deg2Rad;
					views.Add(new KeyValuePair<Vector3, Vector3>(at(Mathf.Sin(a) * r * 1.7f, r * 0.55f + h * 0.6f, Mathf.Cos(a) * r * 1.7f), at(0f, h * 0.3f, 0f)));
				}
				views.Add(new KeyValuePair<Vector3, Vector3>(at(0f, r * 2.4f + h, -r * 0.35f), at(0f, 0f, 0f)));
			}
			yield return new WaitForEndOfFrame();
			Vector3 pos = cam.transform.position; Quaternion rot = cam.transform.rotation;
			float far = cam.farClipPlane;
			bool fog = RenderSettings.fog;
			var files = new List<string>();
			// (the editor's own markers - zone spheres, rings, labels - left out: the island as players will see it)
			List<Renderer> markers = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r => r.enabled && (r.name == ContentCatalog.MarkerOnly || (r.transform.parent != null && r.transform.parent.name == ContentCatalog.MarkerOnly))).ToList();
			try
			{
				foreach (Renderer r in markers) r.enabled = false;
				RenderSettings.fog = false;
				cam.farClipPlane = Mathf.Max(far, radius * 8f + 500f);
				for (int i = 0; i < views.Count; i++)
				{
					cam.transform.position = views[i].Key;
					cam.transform.LookAt(views[i].Value);
					string file = Path.Combine(RecipeFolder, "view_" + name + "_" + (i + 1) + ".jpg");
					File.WriteAllBytes(file, RenderCam(cam, 1280, 720).EncodeToJPG(88));
					files.Add(Path.GetFullPath(file));
				}
			}
			finally
			{
				foreach (Renderer r in markers) if (r != null) r.enabled = true;
				cam.transform.SetPositionAndRotation(pos, rot);
				cam.farClipPlane = far;
				RenderSettings.fog = fog;
			}
			foreach (string f in files) Log("VIEW " + f);
			Log("PASS: views of the island (land radius " + radius.ToString("F0") + " m, top " + (top - sea).ToString("F0") + " m above the sea, middle " + mid.ToString("F0") + ")");
		}

		/// <summary>The editor's land above the sea: its middle, how far it reaches from there, its highest point.</summary>
		static void EditorLand(out Vector2 mid, out float radius, out float top)
		{
			mid = EditorLandCentre();
			TerrainData data = terraineditor.terrain.terrainData;
			int res = data.heightmapResolution;
			float[,] h = data.GetHeights(0, 0, res, res);
			Vector3 o = terraineditor.terrain.transform.position;
			float water = (DynamicIslands.EditorWaterLevel - o.y) / data.size.y;
			radius = 0f; top = DynamicIslands.EditorWaterLevel;
			for (int y = 0; y < res; y += 2)
				for (int x = 0; x < res; x += 2)
				{
					if (h[y, x] <= water) continue;
					float wx = o.x + x / (float)(res - 1) * data.size.x, wz = o.z + y / (float)(res - 1) * data.size.z;
					radius = Mathf.Max(radius, Vector2.Distance(mid, new Vector2(wx, wz)));
					top = Mathf.Max(top, o.y + h[y, x] * data.size.y);
				}
		}

		/// <summary>The middle of the editor's land (above the sea), as the spawner centres a saved island.</summary>
		static Vector2 EditorLandCentre(bool asSaved = false)
		{
			TerrainData data = terraineditor.terrain.terrainData;
			int res = data.heightmapResolution;
			float[,] h = data.GetHeights(0, 0, res, res);
			// (asSaved: the ground's own heights, not as cut in its dug caves - the world finds the land centre from the file's;
			// Gravewell Shaft said 'offset 0 0' while its plays stood 2.8 m off, the walk into the bore met the pit wall and the
			// stakes' zone was never reached, CP10/CP11 11-20. Not for 'origin' and the rest: their points stay where they were)
			if (asSaved) CaveGround.EditorOwnHeights(terraineditor.terrain, h);
			float water = (DynamicIslands.EditorWaterLevel - terraineditor.terrain.transform.position.y) / data.size.y;
			double sx = 0, sz = 0; long count = 0;
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
					if (h[y, x] > water) { sx += x; sz += y; count++; }
			Vector3 o = terraineditor.terrain.transform.position;
			if (count == 0) return new Vector2(o.x + data.size.x / 2f, o.z + data.size.z / 2f);
			return new Vector2(o.x + (float)(sx / count) / (res - 1) * data.size.x, o.z + (float)(sz / count) / (res - 1) * data.size.z);
		}

		/// <summary>clear ... body: how near an object's body (its renderers' box, flat) comes to the segment a-b.</summary>
		static float BodyReach(GameObject g, Vector2 a, Vector2 b)
		{
			Vector2 p = new Vector2(g.transform.position.x, g.transform.position.z);
			float best = DistanceToSegment(p, a, b);
			foreach (Renderer rd in g.GetComponentsInChildren<Renderer>())
			{
				Bounds bb = rd.bounds;
				float d = DistanceToSegment(new Vector2(bb.center.x, bb.center.z), a, b) - Mathf.Min(bb.extents.x, bb.extents.z);
				if (d < best) best = d;
			}
			return Mathf.Max(0f, best);
		}

		static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float f = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
			return Vector2.Distance(p, a + ab * f);
		}

		/// <summary>The objects placed under an alias (still there).</summary>
		static List<EditorGameObject> Group(string alias)
		{
			List<EditorGameObject> group;
			return recipeGroups.TryGetValue(alias, out group) ? group.Where(e => e != null).ToList() : new List<EditorGameObject>();
		}

		/// <summary>
		/// zipto's ground check of the line (11-20): a line whose near end stands on the ground and whose rope (as Raft makes it
		/// in a world, sagging between its ends) runs too near the ground (a ridge's shoulder bulging over it: Outer Breakwater's
		/// ridge line ran 1.1 m inside the ridge, its floors both on the ground) has its near end raised, its floor on a wooden
		/// deck with ladders up to it (the save stands the deck's posts on the ground): first for a rider's room
		/// (ZiplineEnds.RiderClear, less near the far end, where the rope comes down to its floor), and when that would take
		/// more than ZiplineEnds.MaxRaise, for the rope alone (ZiplineEnds.LineClear). A near end that is not on the ground (a
		/// tower's deck) is only logged. Returns the metres raised.
		/// </summary>
		static float ZipClearGround(EditorGameObject e)
		{
			Terrain land = terraineditor.terrain;
			Vector3 near, far, at = Vector3.zero, lineAt;
			MeshPath_Zipline_Landmark line = e.GetComponentInChildren<MeshPath_Zipline_Landmark>(true);
			if (land == null || line == null || !ZiplineEnds.Ends(e.gameObject, e.Props, land.transform.position, out near, out far)) return 0f;
			// (the far end's floor on the cave hill's skin where a world builds the hill over it - 11-21 c3z13: Caisson Yard's summit
			// line's far end stood on the land 0.7 m under the skin, its rope ending in the sand)
			if (ZipFarOnSkin(e, land.transform.position)) ZiplineEnds.Ends(e.gameObject, e.Props, land.transform.position, out near, out far);
			// (a near floor buried in the land - the land raised round it after its place - goes up onto the land first, unless it
			// stands in a dug passage's mouth (a hole in the land): 11-21 c3z30, The Rootcore's mouth-ledge line, its floor 8.3 m
			// under the ledge, was raised 8 m for a rider's room onto a deck still 0.6 m under the ground, its rope through the ledge)
			float lifted = ZipNearOutOfLand(e, land);
			if (lifted > 0f) ZiplineEnds.Ends(e.gameObject, e.Props, land.transform.position, out near, out far);
			// (the ground under the rope: the land, or what stands solid on it - a rock, a cliff, a block - as a world's rider meets
			// it: 11-20, Caisson Yard's line ran into a rock pillar the land's own height left out)
			Func<float, float, float> ground = ZipGround(e);
			float len = Mathf.Max(1f, new Vector2(far.x - near.x, far.z - near.z).magnitude), atT, lt;
			// (the recipe's own slack, when it set one (prop zip.slack, which Apply sets in a world): the rope starts from it and is never
			// made looser - 11-21, Engine Court's tower lines set -0.692 and the save loosened them to -3.46 from Raft's -6.92)
			float setSlack;
			if (!float.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipSlack) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out setSlack) || float.IsNaN(setSlack) || float.IsInfinity(setSlack) || setSlack == 0f) setSlack = float.NaN;
			List<Vector3> rope0 = ZiplineEnds.RopeBetween(line, far, near, setSlack);
			if (rope0.Count < 2) return 0f;
			ZipClearStrays(e, rope0, near, far);
			float lineLow =ZiplineEnds.RopeLowest(rope0, near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, null, out lineAt, out lt);
			Func<Vector3, string> xyz = v => Num(v.x - TopFrame.Origin.x) + " " + Num(v.z - TopFrame.Origin.y) + " h=" + Num(v.y);
			Log("  zipto rope: " + rope0.Count + " points, " + xyz(rope0[0]) + " .. " + xyz(rope0[rope0.Count / 2]) + " .. " + xyz(rope0[rope0.Count - 1]) + "; near " + xyz(near) + ", far " + xyz(far) +
				", slack " + Num(float.IsNaN(setSlack) ? ZiplineEnds.Slack(line) : setSlack) + "; lowest " + Num(lineLow) + " m over the ground (" + Num(ground(lineAt.x, lineAt.z)) + "" + ZipGroundWhat(e, lineAt.x, lineAt.z) + ") at " + xyz(lineAt));
			Vector3 corner = new Vector3(TopFrame.Origin.x, 0f, TopFrame.Origin.y);
			Vector3 floor = ZiplineEnds.NearFloor(e.gameObject) ?? near;
			if (floor.y - GroundY(floor.x, floor.z) > 0.6f)
			{
				// (a tower's line, whose rope sags under its far floor where the tower is low: a tighter rope - 11-20, Engine Court's
				// tower lines dipped 1.4 m into the court's flat ground before their far ends)
				if (lineLow >= ZiplineEnds.LineClear)
				{
					// (the rope clear, a rider under it maybe not: logged - 11-20 review, the tower lines' riders were only checked once tightened)
					Vector3 oa; float ot;
					float ol = ZiplineEnds.RopeLowest(rope0, near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.RiderFarSkip, ZiplineEnds.RiderWant(near.y - far.y, len), out oa, out ot);
					if (ol < 0f)
						Log("  WARNING: zipto: a rider on that rope comes " + Num(-ol) + " m short of a rider's room at " + Num(oa.x - corner.x) + " " + Num(oa.z - corner.z) + " (ground " + Num(ground(oa.x, oa.z)) + ZipGroundWhat(e, oa.x, oa.z) + "): move what is under it or an end");
					return 0f;
				}
				float own0 = float.IsNaN(setSlack) ? ZiplineEnds.Slack(line) : setSlack;
				if (!float.IsNaN(own0) && own0 != 0f)
					foreach (float sl in new[] { own0 * 0.5f, own0 * 0.25f, own0 * 0.1f })
					{
						Vector3 ta; float tt;
						float tl = ZiplineEnds.RopeLowest(ZiplineEnds.RopeBetween(line, far, near, sl), near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, d => ZiplineEnds.LineClear, out ta, out tt);
						if (tl < 0f) continue;
						PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipSlack, sl.ToString("0.###", CultureInfo.InvariantCulture)));
						// (a rider hangs about 1.2-2.3 m under the rope: the rope's 1 m can still leave a rider walking into what is
						// under it - 11-20, Engine Court's middle tower line, tightened, stopped its rider at a loot chest under it)
						float tlen = Mathf.Max(1f, new Vector2(far.x - near.x, far.z - near.z).magnitude);
						Vector3 ra; float rt;
						float rl = ZiplineEnds.RopeLowest(ZiplineEnds.RopeBetween(line, far, near, sl), near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.RiderFarSkip, ZiplineEnds.RiderWant(near.y - far.y, tlen), out ra, out rt);
						Log("  zipto: the rope came " + Num(lineLow) + " m over the ground at " + Num(lineAt.x - corner.x) + " " + Num(lineAt.z - corner.z) + " (its near end on a deck) - its rope tightened to slack " +
							sl.ToString("0.###", CultureInfo.InvariantCulture) + " (Raft's " + own0.ToString("0.###", CultureInfo.InvariantCulture) + "), the least now " + Num(tl + ZiplineEnds.LineClear) + " m over the ground");
						if (rl < 0f)
							Log("  WARNING: zipto: a rider on that rope comes " + Num(-rl) + " m short of a rider's room at " + Num(ra.x - corner.x) + " " + Num(ra.z - corner.z) + " (ground " + Num(ground(ra.x, ra.z)) + ZipGroundWhat(e, ra.x, ra.z) + "): move what is under it or an end");
						return 0f;
					}
				// (none clears: its far end back along the line, short of the hump the rope runs through - 11-21 c3z29: tower lines
				// on 9 islands ran 0.03-3.3 m into the ground between their ends, only warned)
				{
					float[] sl0 = !float.IsNaN(own0) && own0 != 0f ? new[] { own0, own0 * 0.5f, own0 * 0.25f, own0 * 0.1f } : new[] { own0 };
					Vector3 nt, nfar, pa; float part, pr, ps, pl; bool prider;
					if (ZipPullIn(e, line, land.transform.position, near, far, ground, sl0, false, out nt, out nfar, out part, out pr, out ps, out pl, out pa, out prider))
					{
						ZipPullSet(e, line, land, far, nt, ps, own0, "the rope came " + Num(lineLow) + " m over the ground at " + Num(lineAt.x - corner.x) + " " + Num(lineAt.z - corner.z) + " (its near end on a deck) and no tighter rope clears it",
							prider, pa, corner);
						return 0f;
					}
				}
				// (none clears: the tightest rope that leaves it more room than its own, still warned - 11-21 c3z11, Caisson Yard's
				// lines left as they were came out worse than a partial fix)
				float bestL = lineLow, bestS = float.NaN; Vector3 bestA = lineAt;
				if (!float.IsNaN(own0) && own0 != 0f)
					foreach (float sl in new[] { own0 * 0.5f, own0 * 0.25f, own0 * 0.1f })
					{
						Vector3 ta; float tt;
						float tl = ZiplineEnds.RopeLowest(ZiplineEnds.RopeBetween(line, far, near, sl), near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, null, out ta, out tt);
						if (tl > bestL + 0.05f) { bestL = tl; bestS = sl; bestA = ta; }
					}
				if (!float.IsNaN(bestS))
				{
					PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipSlack, bestS.ToString("0.###", CultureInfo.InvariantCulture)));
					ZiplineEnds.RopeBetween(line, far, near, bestS);
					Log("  WARNING: zipto: the rope comes " + Num(lineLow) + " m over the ground at " + Num(lineAt.x - corner.x) + " " + Num(lineAt.z - corner.z) + " and its near end is not on the ground (so not raised), nor does a tighter rope keep it " + Num(ZiplineEnds.LineClear) + " m over it: move an end. The tightest that helps set: slack " +
						bestS.ToString("0.###", CultureInfo.InvariantCulture) + " (Raft's " + own0.ToString("0.###", CultureInfo.InvariantCulture) + "), the least now " + Num(bestL) + " m over the ground at " + Num(bestA.x - corner.x) + " " + Num(bestA.z - corner.z));
				}
				else
					Log("  WARNING: zipto: the rope comes " + Num(lineLow) + " m over the ground at " + Num(lineAt.x - corner.x) + " " + Num(lineAt.z - corner.z) + " and its near end is not on the ground (so not raised), nor does a tighter rope keep it " + Num(ZiplineEnds.LineClear) + " m over it, nor give it more room: move an end. Left as it is");
				return 0f;
			}
			// (the room a rider wants d m before the far end: RiderClear, less where the rope's own fall to its floor gives less;
			// the last RiderFarSkip m left out - zipride counts a rider stopped there as carried)
			Func<float, Func<float, float>> riderWant = rr => ZiplineEnds.RiderWant(near.y + rr - far.y, len);
			Func<float, Func<float, float>> ropeWant = rr => d => ZiplineEnds.LineClear;
			// (Raft's own rope first; a tighter one - zip.slack, which Apply sets in a world - when the own one would want a
			// tower: its rope sags about 3.4 m on a 20 m line, so a raise lifts it little where the ground bulges midway)
			float own = float.IsNaN(setSlack) ? ZiplineEnds.Slack(line) : setSlack, r = 0f, low = 0f, slack = own, best = 999f;
			string why = null, tried = "";
			float[] slacks = !float.IsNaN(own) && own != 0f ? new[] { own, own * 0.5f, own * 0.25f, own * 0.1f } : new[] { own };
			for (int pass = 0; pass < 2 && why == null; pass++)
				foreach (float sl in slacks)
				{
					float rr, rl, rt; Vector3 ra;
					// (a rider's room too down to the ground check's last FarSkip m - RiderWant asks LineLow there at least: 11-21 c3z30,
					// The Rain Engine's lines, raised with their last 5 m left out, came 0.03 m over the ground 3.2 m before their far ends)
					bool ok = ZipRaise(line, near, far, ground, pass == 0 ? riderWant : ropeWant, ZiplineEnds.FarSkip, sl, out rr, out rl, out ra, out rt);
					if (!ok) tried += (tried.Length > 0 ? "; " : "") + (pass == 0 ? "rider" : "rope") + " slack " + Num(sl) + ": " + Num(rl) + " m short at " + Num(ra.x - corner.x) + " " + Num(ra.z - corner.z) + " h=" + Num(ra.y) + " (ground " + Num(ground(ra.x, ra.z)) + ZipGroundWhat(e, ra.x, ra.z) + ")";
					if (!ok || rr >= best) continue;
					best = rr; r = rr; low = rl; at = ra; slack = sl;
					why = pass == 0 ? "a rider's room (" + Num(ZiplineEnds.RiderClear) + " m)" : "the rope's " + Num(ZiplineEnds.LineClear) + " m (a rider's room would take over " + Num(ZiplineEnds.MaxRaise) + " m)";
					if (rr <= ZiplineEnds.MaxRaise / 2f) break;
				}
			if (why == null)
			{
				// (no raise clears it where it is: its far end back along the line, short of the hump - 11-21 c3z29: lines on 8
				// islands ran 1-4 m into a hill between their ends with their near ends raised the most)
				Vector3 nt, nfar, pa2; float part, pr2, ps2, pl2; bool prider;
				if (ZipPullIn(e, line, land.transform.position, near, far, ground, slacks, true, out nt, out nfar, out part, out pr2, out ps2, out pl2, out pa2, out prider))
				{
					float wasFar = far.y;
					ZipPullSet(e, line, land, far, nt, float.NaN, own, "the rope came " + Num(lineLow) + " m over the ground at " + Num(lineAt.x - corner.x) + " " + Num(lineAt.z - corner.z) + " and no raise of its near end up to " +
						Num(ZiplineEnds.MaxRaise) + " m clears it", prider, pa2, corner);
					ZiplineEnds.Ends(e.gameObject, e.Props, land.transform.position, out near, out far);
					r = pr2; slack = ps2; low = pl2; at = pa2;
					why = (prider ? "a rider's room (" + Num(ZiplineEnds.RiderClear) + " m)" : "the rope's " + Num(ZiplineEnds.LineClear) + " m") + " with its far end moved back (" + Num(far.y - wasFar) + " m higher)";
				}
			}
			if (why == null)
			{
				// (none clears: the raise and rope that leave it the most room, still warned - 11-21 c3z11, Caisson Yard's ridge
				// line left as it was rode 3.2 m into the hill where c3z9's partial raise had cleared it)
				float pr, ps, pl; Vector3 pa;
				ZipBestPartial(line, near, far, ground, slacks, ZiplineEnds.MaxRaise, out pr, out ps, out pl, out pa);
				if (pl <= lineLow + 0.05f)
				{
					Log("  WARNING: zipto: the rope comes " + Num(lineLow) + " m over the ground at " + Num(lineAt.x - corner.x) + " " + Num(lineAt.z - corner.z) + " and no raise of its near end up to " + Num(ZiplineEnds.MaxRaise) +
						" m, nor a tighter rope, keeps it " + Num(ZiplineEnds.LineClear) + " m over it or gives it more room - a line through a hill: move an end. Left as it is (raised " + Num(ZiplineEnds.MaxRaise) + " m: " + tried + ")");
					return 0f;
				}
				Log("  WARNING: zipto: the rope comes " + Num(lineLow) + " m over the ground at " + Num(lineAt.x - corner.x) + " " + Num(lineAt.z - corner.z) + " and no raise of its near end up to " + Num(ZiplineEnds.MaxRaise) +
					" m, nor a tighter rope, keeps it " + Num(ZiplineEnds.LineClear) + " m over it - a line through a hill: move an end. The most room it can get set: raised " + Num(pr) + " m, slack " + Num(ps) +
					", the least " + Num(pl) + " m over the ground at " + Num(pa.x - corner.x) + " " + Num(pa.z - corner.z) + " (" + tried + ")");
				r = pr; slack = ps; low = pl; at = pa;
				why = "the most room it can get (short of the rope's " + Num(ZiplineEnds.LineClear) + " m)";
			}
			if (!float.IsNaN(slack) && slack != own)
			{
				PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipSlack, slack.ToString("0.###", CultureInfo.InvariantCulture)));
				ZiplineEnds.RopeBetween(line, far, near, slack);
				why += ", its rope tightened to slack " + slack.ToString("0.###", CultureInfo.InvariantCulture) + " (Raft's " + own.ToString("0.###", CultureInfo.InvariantCulture) + ")";
			}
			if (r <= 0f) return 0f;
			// (the floor's underside, where the deck's top goes: the line's own small pieces around the near floor)
			float bottom = floor.y - 0.05f;
			foreach (Renderer rd in e.gameObject.GetComponentsInChildren<Renderer>(true))
				if (rd.bounds.size.x < 2f && rd.bounds.size.z < 2f && new Vector2(rd.bounds.center.x - floor.x, rd.bounds.center.z - floor.z).magnitude < 0.8f && rd.bounds.max.y < floor.y + 0.6f)
					bottom = Mathf.Min(bottom, rd.bounds.min.y);
			e.transform.position += Vector3.up * r;
			bottom += r;
			string deck = "";
			Transform placedRoot = GameObject.Find("PlacedObjects").transform;
			GameObject go = ObjectLimit.Allow(1) ? PlaceableCatalog.Spawn("Block_Floor_Wood", placedRoot) : null;
			if (go != null)
			{
				go.transform.rotation = PlacementOptions.Straight(go.transform.rotation, true);
				Renderer[] rs = PlacementOptions.ShapeRenderers(go);
				if (rs.Length > 0)
				{
					Bounds b = rs[0].bounds;
					foreach (Renderer rr in rs) b.Encapsulate(rr.bounds);
					go.transform.position += new Vector3(floor.x - b.center.x, bottom - b.max.y, floor.z - b.center.z);
					Collider deckCol = go.GetComponent<Collider>();
					if (deckCol != null) deckCol.enabled = true;
					EditorGameObject.Attach(go, "Block_Floor_Wood");
					recipePlaced.Add(go);
					CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(new[] { go }, true));
					deck = ", its floor on a deck " + Num(bottom - GroundY(floor.x, floor.z)) + " m over the ground";
					// (a way up: ladders at the deck's back edge, away from the line, from the deck's top down into the ground)
					Bounds db = rs[0].bounds;
					foreach (Renderer rr in rs) db.Encapsulate(rr.bounds);
					deck += ZipDeckPosts(db, placedRoot);
					deck += ZipDeckLadders(db, new Vector3(near.x - far.x, 0f, near.z - far.z), placedRoot);
				}
				else UnityEngine.Object.Destroy(go);
			}
			Log("  zipto: the rope came " + Num(lineLow) + " m over the ground - its near end raised " + Num(r) + " m for " + why + ", the least now " + Num(low) + " m over that at " + Num(at.x - corner.x) + " " + Num(at.z - corner.z) + deck);
			return r;
		}

		/// <summary>A zipline's near floor more than NearBuried m under the land (a hole in the land - a dug passage's mouth - aside)
		/// goes up onto it, the whole line with it (its far end stays at zip.to); logged. Returns the metres lifted (0: none).</summary>
		static float ZipNearOutOfLand(EditorGameObject e, Terrain land)
		{
			Vector3? nf = ZiplineEnds.NearFloor(e.gameObject);
			if (nf == null || land == null) return 0f;
			Vector3 f = nf.Value;
			float buried = GroundY(f.x, f.z) - f.y;
			if (buried <= NearBuried) return 0f;
			TerrainData td = land.terrainData;
			int hn = td.holesResolution, hx = Mathf.FloorToInt((f.x - land.transform.position.x) / td.size.x * hn), hz = Mathf.FloorToInt((f.z - land.transform.position.z) / td.size.z * hn);
			if (hx >= 0 && hz >= 0 && hx < hn && hz < hn && !td.GetHoles(hx, hz, 1, 1)[0, 0]) return 0f;
			e.transform.position += Vector3.up * buried;
			Physics.SyncTransforms();
			Log("  zipto: its near floor stood " + Num(buried) + " m under the land at " + Num(f.x - TopFrame.Origin.x) + " " + Num(f.z - TopFrame.Origin.y) + " - the line lifted onto it");
			return buried;
		}

		/// <summary>How far under the land a zipline's near floor may stand before zipto lifts it (ZipNearOutOfLand).</summary>
		const float NearBuried = 0.5f;

		/// <summary>zipto's far end clear for its whole last stretch (11-21 c3z15: Caisson Yard's summit line, its far end set on the
		/// cave hill's skin, still ran 0.24 m into the dune's slope just before it and ended in the sand): when the rope's last
		/// FarSkip m cut the ground (the land, what stands solid on it: under FarEndLow m over it) or come under FarEndClear m over
		/// the cave hill's skin, zip.to goes up until they don't (FarMaxRaise at most; the rope tightened when no raise does), its
		/// floor on a deck on posts to the ground - the rope not through that deck either - ladders down its far side when it
		/// stands over 1 m up. Null when it was clear; else the log's words. deck: the deck made (null when none).</summary>
		/// <summary>The grounding test: a zipline's zip.slack (NaN: none set - the line's own).</summary>
		static float Slack9(EditorGameObject e)
		{
			float s;
			return float.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipSlack) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out s) && s != 0f ? s : float.NaN;
		}

		/// <summary>Half the width of the deck ZipFarClear stands a raised far floor on (Raft's floor block, 1.5 m), a little over.</summary>
		const float FarDeckHalf = 0.8f;

		/// <summary>How far before its last FarSkip m ZipFarClear also wants the ground check's LineLow over the cave hill's skin.</summary>
		const float FarSkinBefore = 3f;

		static string ZipFarClear(EditorGameObject e, out GameObject deck)
		{
			deck = null;
			Terrain land = terraineditor.terrain;
			MeshPath_Zipline_Landmark line = e != null ? e.GetComponentInChildren<MeshPath_Zipline_Landmark>(true) : null;
			Vector3 near, far, to, at;
			if (land == null || line == null || !ZiplineEnds.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipTo), out to)) return null;
			Vector3 origin = land.transform.position;
			Physics.SyncTransforms();
			if (!ZiplineEnds.Ends(e.gameObject, e.Props, origin, out near, out far)) return null;
			Func<float, float, float> ground = ZipGround(e);
			float slack;
			if (!float.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipSlack) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out slack) || float.IsNaN(slack) || float.IsInfinity(slack) || slack == 0f) slack = float.NaN;
			float low0 = ZiplineEnds.FarStretchLowest(ZiplineEnds.RopeBetween(line, far, near, slack), near, far, ground, out at), low = low0, r = 0f;
			// The room the rope's last FarSkip m want (11-21 c3z21): over the cave hill's skin FarEndClear m (a world's hill comes out
			// a little off the measuring one: Caisson Yard's summit floor, set on the measuring skin, stood 0.2 m in the world's
			// sand, its rope ending in it), so an end on the skin always stands on a deck; over the land and what stands on it only
			// no cut (FarEndLow, the ground check's own: a rope meeting its floor on flat ground or a roof touches it by a few mm -
			// 11-21 c3z17/18 Thalassa Mooring, Engine Court - and is left as it is); over the deck a raise stands the floor on, no
			// cut either (c3z21: a rope rising into its far end ran 0.4 m through the deck made under it - a tighter rope then).
			Vector3 fw = origin + to;
			Func<List<Vector3>, Vector3, float, float> worst = (rope, f, deckTop) =>
			{
				float w = float.PositiveInfinity, len = new Vector2(f.x - near.x, f.z - near.z).magnitude;
				for (int i = 0; i + 1 < rope.Count; i++)
				{
					int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(rope[i + 1].x - rope[i].x, rope[i + 1].z - rope[i].z).magnitude / 0.25f));
					for (int k = 0; k <= steps; k++)
					{
						Vector3 p = Vector3.Lerp(rope[i], rope[i + 1], k / (float)steps);
						float dn = new Vector2(p.x - near.x, p.z - near.z).magnitude;
						if (dn < len - ZiplineEnds.FarSkip - FarSkinBefore) continue;
						Collider c;
						float g = ZipGroundAt(e, p.x, p.z, out c);
						bool skin = c != null && c.name == CaveMountain.SkinName;
						// (the FarSkinBefore m before them, over the cave hill's skin: the ground check's LineLow - ZipShaveGround cuts
						// the land under a rope, never the hill; 11-21 c3z38 Highpine's rope 0.36 m over the skin 3 m from its end)
						if (dn < len - ZiplineEnds.FarSkip) { if (skin) w = Mathf.Min(w, p.y - g - ZiplineEnds.LineLow - 0.05f); continue; }
						w = Mathf.Min(w, p.y - g - (skin ? ZiplineEnds.FarEndClear : ZiplineEnds.FarEndLow));
						if (!float.IsNaN(deckTop) && Mathf.Abs(p.x - fw.x) <= FarDeckHalf && Mathf.Abs(p.z - fw.z) <= FarDeckHalf) w = Mathf.Min(w, p.y - deckTop - ZiplineEnds.FarEndLow);
					}
				}
				return w;
			};
			float w0 = worst(ZiplineEnds.RopeBetween(line, far, near, slack), far, float.NaN);
			if (w0 >= 0f) return null;
			Vector3 corner = new Vector3(TopFrame.Origin.x, 0f, TopFrame.Origin.y), at0 = at;
			string where0 = Num(at0.x - corner.x) + " " + Num(at0.z - corner.z) + " (ground " + Num(ground(at0.x, at0.z)) + ZipGroundWhat(e, at0.x, at0.z) + ")";
			// (the least raise that gives the room, the deck it then stands on counted; the rope tightened step by step when none
			// does - a rope sagging into its far end rises into it from under, and a raise lifts the deck with it)
			float slack0 = !float.IsNaN(slack) ? slack : ZiplineEnds.Slack(line), bestW = float.NegativeInfinity, bestR = 0f, bestS = slack0, under0 = ground(fw.x, fw.z);
			float[] tighter = float.IsNaN(slack0) ? new[] { float.NaN } : new[] { slack0, slack0 * 0.5f, slack0 * 0.25f, slack0 * 0.1f };
			bool found = false;
			foreach (float sl in tighter)
			{
				for (float rr = 0.05f; rr <= ZiplineEnds.FarMaxRaise + 0.001f; rr += 0.05f)
				{
					Vector3 f2 = far + Vector3.up * rr;
					float wr = worst(ZiplineEnds.RopeBetween(line, f2, near, sl), f2, fw.y + rr - under0 > 0.15f ? fw.y + rr - 0.05f : float.NaN);
					if (wr > bestW + 0.005f) { bestW = wr; bestR = rr; bestS = sl; }
					if (wr >= 0f) { found = true; break; }
				}
				if (found) break;
			}
			r = Mathf.Round(bestR * 20f) / 20f;
			low = ZiplineEnds.FarStretchLowest(ZiplineEnds.RopeBetween(line, far + Vector3.up * r, near, bestS), near, far + Vector3.up * r, ground, out at);
			Vector3 floorW = origin + to + Vector3.up * r;
			float under = ground(floorW.x, floorW.z);
			PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(to + Vector3.up * r)));
			bool tightened = !float.IsNaN(bestS) && !Mathf.Approximately(bestS, slack0);
			if (tightened) PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipSlack, bestS.ToString("0.###", CultureInfo.InvariantCulture)));
			string words = "  zipto: its rope came " + Num(low0) + " m over the ground in its last " + Num(ZiplineEnds.FarSkip) + " m at " + where0 + " (" + Num(-w0) + " m short of its room there) - its far end raised " + Num(r) + " m" +
				(tightened ? ", its rope tightened to slack " + Num(bestS) + " (was " + Num(slack0) + ")" : "") + ", the least there now " + Num(low) + " m";
			if (!found)
				words = "  WARNING:" + words.Substring(1) + ", still " + Num(-bestW) + " m short of its room (" + Num(ZiplineEnds.FarEndClear) + " m over the cave hill, no cut elsewhere) with the most raise (" + Num(ZiplineEnds.FarMaxRaise) + " m) and the tightest rope: move the far end onto the top or the flat";
			// (its floor on a deck where it stands off the ground: nothing floating - the far pieces stand on its top)
			GameObject placedGo = GameObject.Find("PlacedObjects");
			if (floorW.y - under > 0.15f && placedGo != null && ObjectLimit.Allow(1))
			{
				Transform placedRoot = placedGo.transform;
				GameObject go = PlaceableCatalog.Spawn("Block_Floor_Wood", placedRoot);
				Renderer[] rs = go != null ? PlacementOptions.ShapeRenderers(go) : new Renderer[0];
				if (go != null && rs.Length > 0)
				{
					go.transform.rotation = PlacementOptions.Straight(go.transform.rotation, true);
					Bounds b = rs[0].bounds;
					foreach (Renderer rr in rs) b.Encapsulate(rr.bounds);
					float top = floorW.y - 0.05f;
					go.transform.position += new Vector3(floorW.x - b.center.x, top - b.max.y, floorW.z - b.center.z);
					Collider deckCol = go.GetComponent<Collider>();
					if (deckCol != null) deckCol.enabled = true;
					EditorGameObject.Attach(go, "Block_Floor_Wood");
					recipePlaced.Add(go);
					CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(new[] { go }, true));
					Physics.SyncTransforms();
					Bounds db = rs[0].bounds;
					foreach (Renderer rr in rs) db.Encapsulate(rr.bounds);
					words += ", its floor on a deck " + Num(top - under) + " m over the ground" + ZipDeckPosts(db, placedRoot);
					if (top - under > 1f) words += ZipDeckLadders(db, new Vector3(far.x - near.x, 0f, far.z - near.z), placedRoot);
					deck = go;
				}
				else if (go != null) UnityEngine.Object.Destroy(go);
			}
			Log(words);
			return words;
		}

		/// <summary>zipto's far end on the cave hill (11-21 c3z13): when the ground at its floor (zip.to) is the skin of the cave
		/// hill a world builds over the island's cave pieces (CaveMountain.Preview, made by the save to measure against), zip.to
		/// goes up onto the skin's top. True when it moved.</summary>
		static bool ZipFarOnSkin(EditorGameObject e, Vector3 origin)
		{
			Vector3 to;
			if (e == null || !ZiplineEnds.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipTo), out to)) return false;
			Physics.SyncTransforms();
			Vector3 w = origin + to;
			Collider c;
			float g = ZipGroundAt(e, w.x, w.z, out c);
			if (c == null || c.name != CaveMountain.SkinName || g <= w.y + 0.05f) return false;
			PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(new Vector3(to.x, g - origin.y, to.z))));
			Log("  zipto: its far end's floor stood " + Num(g - w.y) + " m under the skin of the cave hill a world builds over it: set on the skin's top");
			return true;
		}

		/// <summary>
		/// zipto when no raise of its near end (none for a near end on a deck) nor a tighter rope clears (11-21 c3z29: lines on 15
		/// islands ran through a hump of the ground between their ends, the save only warning "move an end"): its far end moved
		/// back along the line toward the near end (ZiplineEnds.PullStep of it at a time, at most PullMost), its floor on the
		/// ground there, never nearer the near end than PullShortest m nor less than PullDrop m under it - the first spot where the
		/// rope clears: a rider's room, else the rope's LineClear (with a raise of the near end up to MaxRaise when raise). True
		/// with the new zip.to (island frame), far end, part pulled, raise, slack, room left (over the want) and where; false
		/// leaves the rope as Raft makes it at slacks[0].
		/// </summary>
		static bool ZipPullIn(EditorGameObject e, MeshPath_Zipline_Landmark line, Vector3 origin, Vector3 near, Vector3 far, Func<float, float, float> ground, float[] slacks, bool raise,
			out Vector3 newTo, out Vector3 newFar, out float part, out float r, out float slack, out float low, out Vector3 at, out bool rider)
		{
			newTo = Vector3.zero; newFar = far; part = 0f; r = 0f; slack = slacks.Length > 0 ? slacks[0] : float.NaN; low = 0f; at = near; rider = false;
			Vector3 to;
			if (e == null || line == null || !ZiplineEnds.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipTo), out to)) return false;
			// (the rope's end stands off its floor: the floor goes onto the ground, the rope's end with it)
			Vector3 off = far - (origin + to);
			Vector2 n2 = new Vector2(near.x, near.z), f2 = new Vector2(far.x, far.z);
			for (int k = 1; k * ZiplineEnds.PullStep <= ZiplineEnds.PullMost + 0.001f; k++)
			{
				float s = k * ZiplineEnds.PullStep;
				Vector2 p = Vector2.Lerp(f2, n2, s);
				float len = Vector2.Distance(p, n2);
				if (len < ZiplineEnds.PullShortest) break;
				float fx = p.x - off.x, fz = p.y - off.z;
				Vector3 fl = new Vector3(fx, ground(fx, fz), fz), f = fl + off;
				if (near.y - f.y < ZiplineEnds.PullDrop) continue;
				for (int pass = 0; pass < 2; pass++)
					foreach (float sl in slacks)
					{
						Func<float, Func<float, float>> want = pass == 0 ? (Func<float, Func<float, float>>)(x => ZiplineEnds.RiderWant(near.y + x - f.y, len)) : x => d => ZiplineEnds.LineClear;
						float rr = 0f, rl = -1f, rt = 0f; Vector3 ra = near;
						bool ok;
						if (raise) ok = ZipRaise(line, near, f, ground, want, ZiplineEnds.FarSkip, sl, out rr, out rl, out ra, out rt);
						else
						{
							List<Vector3> rope = ZiplineEnds.RopeBetween(line, f, near, sl);
							if (rope.Count >= 2) rl = ZiplineEnds.RopeLowest(rope, near, f, ground, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, want(0f), out ra, out rt);
							ok = rope.Count >= 2 && rl >= 0f;
						}
						if (!ok) continue;
						newTo = fl - origin; newFar = f; part = s; r = rr; slack = sl; low = rl; at = ra; rider = pass == 0;
						return true;
					}
			}
			ZiplineEnds.RopeBetween(line, far, near, slacks.Length > 0 ? slacks[0] : float.NaN);
			return false;
		}

		/// <summary>ZipPullIn's far end set (zip.to; zip.slack too when slack is given and not the line's own) and logged.</summary>
		static void ZipPullSet(EditorGameObject e, MeshPath_Zipline_Landmark line, Terrain land, Vector3 oldFar, Vector3 newTo, float slack, float own, string what, bool rider, Vector3 at, Vector3 corner)
		{
			Vector3 org = land.transform.position, near, far;
			PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipTo, ZiplineEnds.Text(newTo)));
			string tight = "";
			if (!float.IsNaN(slack) && slack != own)
			{
				PropsCommand.Change(e, ObjectProps.With(e.Props, ZiplineEnds.ZipSlack, slack.ToString("0.###", CultureInfo.InvariantCulture)));
				tight = ", its rope tightened to slack " + slack.ToString("0.###", CultureInfo.InvariantCulture) + " (Raft's " + own.ToString("0.###", CultureInfo.InvariantCulture) + ")";
			}
			if (!ZiplineEnds.Ends(e.gameObject, e.Props, org, out near, out far)) far = oldFar;
			else if (!float.IsNaN(slack)) ZiplineEnds.RopeBetween(line, far, near, slack);
			Log("  zipto: " + what + " - its far end moved back " + Num(new Vector2(far.x - oldFar.x, far.z - oldFar.z).magnitude) + " m along the line to " + Num(far.x - corner.x) + " " + Num(far.z - corner.z) +
				" (zip.to " + ZiplineEnds.Text(newTo) + ", its floor on the ground " + Num(far.y - oldFar.y) + " m higher than before)" + tight + ", for " + (rider ? "a rider's room" : "the rope's " + Num(ZiplineEnds.LineClear) + " m") +
				" (the least at " + Num(at.x - corner.x) + " " + Num(at.z - corner.z) + "): set the recipe's zipto there");
		}

		/// <summary>zipto when no raise up to maxRaise nor a tighter rope clears (11-21): the raise (0.5 m steps) and slack of
		/// <paramref name="slacks"/> that leave the rope the most room over the ground (the first NearSkip m and last FarSkip m
		/// left out), the smaller raise and the earlier slack when within 5 cm. low: that room (under 0 when in the ground), at: where.</summary>
		static void ZipBestPartial(MeshPath_Zipline_Landmark line, Vector3 near, Vector3 far, Func<float, float, float> ground, float[] slacks, float maxRaise, out float r, out float slack, out float low, out Vector3 at)
		{
			r = 0f; slack = slacks.Length > 0 ? slacks[0] : float.NaN; low = float.NegativeInfinity; at = near;
			foreach (float sl in slacks)
				for (int i = 0; i * 0.5f <= maxRaise + 0.01f; i++)
				{
					float rr = i * 0.5f;
					List<Vector3> rope = ZiplineEnds.RopeBetween(line, far, near + Vector3.up * rr, sl);
					if (rope.Count < 2) continue;
					Vector3 a; float t;
					float l = ZiplineEnds.RopeLowest(rope, near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, null, out a, out t);
					if (l > low + 0.05f) { low = l; r = rr; slack = sl; at = a; }
				}
		}

		/// <summary>zipto's raise: how far the near end goes up (0.1 m steps of at most 1 m, to ZiplineEnds.MaxRaise) for the
		/// rope Raft makes to leave want(raise)(metres to the far end) over the ground, leaving out the first NearSkip m and the
		/// last farSkip m. False when MaxRaise isn't enough (r is then MaxRaise). low, at, atT: the least room left, where.</summary>
		static bool ZipRaise(MeshPath_Zipline_Landmark line, Vector3 near, Vector3 far, Func<float, float, float> ground, Func<float, Func<float, float>> want, float farSkip, float slack, out float r, out float low, out Vector3 at, out float atT)
		{
			r = 0f; low = 0f; at = near; atT = 0f;
			for (int i = 0; i < 80; i++)
			{
				List<Vector3> rope = ZiplineEnds.RopeBetween(line, far, near + Vector3.up * r, slack);
				if (rope.Count < 2) return false;
				low = ZiplineEnds.RopeLowest(rope, near, far, ground, ZiplineEnds.NearSkip, farSkip, want(r), out at, out atT);
				if (low >= 0f) { r = Mathf.Ceil(r * 10f) / 10f; return true; }
				if (r >= ZiplineEnds.MaxRaise) return false;
				r = Mathf.Min(ZiplineEnds.MaxRaise, r + Mathf.Clamp(-low / Mathf.Max(0.05f, 1f - atT), 0.1f, 1f));
			}
			return false;
		}

		/// <summary>
		/// zipto's clearing (11-20): the generator's objects (not this recipe's) that stand in a rider's way under the rope Raft
		/// makes - their top within a rider's room (ZiplineEnds.RiderClear; the rope's own 1 m near the far end, where a rider
		/// comes down to its floor) - are removed as the recipe's clear removes them. A clear along the line takes them by their
		/// pivots, and a big boulder's pivot can stand metres off its body: Caisson Yard's ridge line ran into a 5 m
		/// SmallBoulder2 the clear r=3 left. Bodies over 10 m across stay (the island's own cliffs). Returns the count removed.
		/// </summary>
		static int ZipClearStrays(EditorGameObject e, List<Vector3> rope, Vector3 near, Vector3 far)
		{
			Transform root = GameObject.Find("PlacedObjects")?.transform;
			if (root == null) return 0;
			var gone = new List<GameObject>();
			var names = new List<string>();
			var kept = new HashSet<GameObject>();
			var keptNames = new List<string>();
			Vector2 n2 = new Vector2(near.x, near.z), f2 = new Vector2(far.x, far.z);
			for (int round = 0; round < 4; round++)
			{
				Physics.SyncTransforms();
				var now = new List<GameObject>();
				for (int i = 0; i + 1 < rope.Count; i++)
				{
					Vector3 a = rope[i], b = rope[i + 1];
					int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(b.x - a.x, b.z - a.z).magnitude / 0.5f));
					for (int s = 0; s <= steps; s++)
					{
						Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
						Vector2 p2 = new Vector2(p.x, p.z);
						float toNear = Vector2.Distance(p2, n2), toFar = Vector2.Distance(p2, f2);
						if (toNear < ZiplineEnds.NearSkip || toFar < ZiplineEnds.FarSkip) continue;
						Collider c;
						float g = ZipGroundAt(e, p.x, p.z, out c);
						if (c == null || p.y - g >= (toFar < ZiplineEnds.RiderFarSkip ? ZiplineEnds.LineClear : ZiplineEnds.RiderClear)) continue;
						EditorGameObject owner = c.GetComponentInParent<EditorGameObject>();
						if (owner == null || owner == e || (recipePlaced.Contains(owner.gameObject) && !recipeScattered.Contains(owner.gameObject)) || !owner.transform.IsChildOf(root) || now.Contains(owner.gameObject)) continue;
						Bounds bb = c.bounds;
						foreach (Renderer rd in owner.GetComponentsInChildren<Renderer>()) bb.Encapsulate(rd.bounds);
						// (a boulder goes whatever its size: 11-21 c3z5, Caisson Yard's ridge line still ran 7.4 m under a scaled-up
						// SmallBoulder2 the 10 m rule kept as a cliff - logged once when a body that big is kept)
						bool boulder = (owner.GameObjectName ?? "").IndexOf("Boulder", StringComparison.OrdinalIgnoreCase) >= 0;
						if (!boulder && Mathf.Max(bb.size.x, bb.size.z) > 10f)
						{
							if (!kept.Contains(owner.gameObject)) { kept.Add(owner.gameObject); keptNames.Add(owner.GameObjectName + " (" + Num(Mathf.Max(bb.size.x, bb.size.z)) + " m across)"); }
							continue;
						}
						now.Add(owner.gameObject);
						Vector3 op = owner.transform.position;
						names.Add(owner.GameObjectName + " at " + Num(op.x - TopFrame.Origin.x) + " " + Num(op.z - TopFrame.Origin.y) + " (the rope " + Num(p.y - g) + " m over its top)");
					}
				}
				if (now.Count == 0) break;
				CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(now, false));
				gone.AddRange(now);
			}
			if (gone.Count > 0)
				Log("  zipto: " + gone.Count + " of the generator's (or the recipe's scattered) objects in a rider's way under the rope removed: " + string.Join("; ", names.ToArray()));
			if (keptNames.Count > 0)
				Log("  zipto: kept in a rider's way under the rope (over 10 m across, taken for the island's own cliffs): " + string.Join("; ", keptNames.ToArray()));
			return gone.Count;
		}

		/// <summary>The ground under a zipline's rope at x z (world): the land's height, or the top of what stands solid on it on the
		/// land's layer higher up (a rock, a cliff, a block: what a world's rider meets), the line's own object left out.</summary>
		static Func<float, float, float> ZipGround(EditorGameObject e)
		{
			Physics.SyncTransforms();
			return (x, z) => { Collider c; return ZipGroundAt(e, x, z, out c); };
		}

		static float ZipGroundAt(EditorGameObject e, float x, float z, out Collider what)
		{
			float g = GroundY(x, z);
			what = null;
			foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, g + 60f, z), Vector3.down, 60.5f, 1 << IslandSpawner.TerrainLayer, QueryTriggerInteraction.Ignore))
				if (h.point.y > g + 0.05f && (e == null || !h.transform.IsChildOf(e.transform)) && !(h.collider is TerrainCollider)) { g = h.point.y; what = h.collider; }
			return g;
		}

		/// <summary>For the log: ", on <object>" when the ground at x z is something standing on the land.</summary>
		static string ZipGroundWhat(EditorGameObject e, float x, float z)
		{
			Collider c;
			ZipGroundAt(e, x, z, out c);
			if (c == null) return "";
			EditorGameObject owner = c.GetComponentInParent<EditorGameObject>();
			return ", on " + (owner != null ? owner.GameObjectName : c.transform.root.name) + "'s " + c.name;
		}

		/// <summary>One of Raft's posts (RT_Pillar) stretched from <paramref name="foot"/> up to <paramref name="top"/> (StandPosts,
		/// a raised zipline deck's corners). False when none was stood; <paramref name="spawned"/> false when the catalog had none.</summary>
		static bool StandPost(Transform placedRoot, Vector3 top, float foot, out bool spawned)
		{
			GameObject go = PlaceableCatalog.Spawn("RT_Pillar", placedRoot);
			spawned = go != null;
			if (go == null) return false;
			go.transform.rotation = PlacementOptions.Straight(go.transform.rotation, true);
			Renderer[] rs = PlacementOptions.ShapeRenderers(go);
			if (rs.Length == 0) { UnityEngine.Object.Destroy(go); return false; }
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			go.transform.localScale = new Vector3(go.transform.localScale.x, go.transform.localScale.y * (top.y - foot) / Mathf.Max(0.1f, b.size.y), go.transform.localScale.z);
			b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			go.transform.position += new Vector3(top.x - b.center.x, foot - b.min.y, top.z - b.center.z);
			Collider ownCollider = go.GetComponent<Collider>();
			if (ownCollider != null) ownCollider.enabled = true;
			EditorGameObject.Attach(go, "RT_Pillar");
			recipePlaced.Add(go);
			CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(new[] { go }, true));
			return true;
		}

		/// <summary>Posts at a raised zipline deck's four corners, from its underside into the ground, stood when the deck is made
		/// (11-21 c3y11/c3y12: Engine Court's and Thalassa Mooring's ridge decks were left to the save's StandPosts and stood
		/// on their ladders alone). A corner whose post would go through a thing is left to the save's StandPosts, which moves it in.</summary>
		static string ZipDeckPosts(Bounds deck, Transform placedRoot)
		{
			List<Bounds> things = PostObstacles(placedRoot);
			int n = 0;
			const float inset = 0.1f;
			foreach (Vector3 c in new[] { new Vector3(deck.min.x + inset, deck.min.y, deck.min.z + inset), new Vector3(deck.max.x - inset, deck.min.y, deck.min.z + inset),
				new Vector3(deck.min.x + inset, deck.min.y, deck.max.z - inset), new Vector3(deck.max.x - inset, deck.min.y, deck.max.z - inset) })
			{
				float foot = GroundY(c.x, c.z) - 0.3f;
				if (c.y - foot < 0.5f || PostBlocked(things, c, foot) || !ObjectLimit.Allow(1)) continue;
				bool spawned;
				if (StandPost(placedRoot, c, foot, out spawned)) n++;
				else if (!spawned) break;
			}
			return ", " + n + " post" + (n == 1 ? "" : "s") + " under it";
		}

		/// <summary>Ladders (Block_Ladder) up a raised zipline deck's side that faces away (back: the axis nearest it) from the
		/// line, stacked from the deck's top down until the lowest reaches the ground; their climb side (the climb trigger,
		/// Block_Ladder's local -z, CILadderSide) out from the deck. Returns the log's words.</summary>
		static string ZipDeckLadders(Bounds deck, Vector3 back, Transform placedRoot)
		{
			Vector3 d = Mathf.Abs(back.x) >= Mathf.Abs(back.z) ? new Vector3(Mathf.Sign(back.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(back.z));
			Vector3 edge = deck.center + Vector3.Scale(d, deck.extents);
			float top = deck.max.y, foot = GroundY(edge.x, edge.z), climbed = 0f;
			int n = 0;
			var made = new List<GameObject>();
			while (top - climbed > foot + 0.3f && n < 6 && ObjectLimit.Allow(1))
			{
				GameObject l = PlaceableCatalog.Spawn("Block_Ladder", placedRoot);
				if (l == null) break;
				// (its local -z, the climb side, out from the deck)
				l.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);
				Renderer[] lr = PlacementOptions.ShapeRenderers(l);
				if (lr.Length == 0) { UnityEngine.Object.Destroy(l); break; }
				Bounds b = lr[0].bounds;
				foreach (Renderer rr in lr) b.Encapsulate(rr.bounds);
				float h = Mathf.Max(0.5f, b.size.y), out_ = Mathf.Abs(Vector3.Dot(b.extents, d));
				Vector3 want = edge + d * out_;
				l.transform.position += new Vector3(want.x - b.center.x, top - climbed - b.max.y, want.z - b.center.z);
				foreach (Collider c in l.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) c.enabled = true;
				EditorGameObject.Attach(l, "Block_Ladder");
				recipePlaced.Add(l);
				zipDeckLadders.Add(l);
				made.Add(l);
				CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(new[] { l }, true));
				climbed += h;
				n++;
			}
			return n > 0 ? ", " + n + " ladder" + (n > 1 ? "s" : "") + " up its " + (d.x > 0 ? "east" : d.x < 0 ? "west" : d.z > 0 ? "north" : "south") + " side" + ZipClearLadderSides(made, d) : ", no ladder (" + Num(top - foot) + " m up)";
		}

		/// <summary>
		/// zipto's deck ladders (11-21 c3z13: a ladder up Caisson Yard's ridge deck had its climbing side shut by a SmallBoulder2,
		/// CILadderSide): the generator's objects (not the recipe's, except what a scatter placed) in the way of a player on each
		/// ladder's climbing side (<paramref name="side"/>: balls 0.3 m and 0.8 m out from its face, up its height, as CILadderSide
		/// looks) are removed, as ZipClearStrays removes them under the rope: bodies over 10 m across stay (the island's own cliffs)
		/// but boulders go whatever their size. The recipe's own rocks there are kept and warned. Returns the log's words.
		/// </summary>
		static string ZipClearLadderSides(List<GameObject> ladders, Vector3 side)
		{
			Transform root = GameObject.Find("PlacedObjects")?.transform;
			if (root == null || ladders.Count == 0) return "";
			Physics.SyncTransforms();
			var gone = new List<GameObject>();
			var names = new List<string>();
			var kept = new List<string>();
			foreach (GameObject l in ladders)
			{
				if (l == null) continue;
				Bounds b = MeshBox(l);
				float face = Mathf.Abs(Vector3.Dot(b.extents, side));
				for (float y = b.min.y + 0.4f; y <= b.max.y - 0.2f; y += 0.4f)
					foreach (float o in new[] { 0.3f, 0.8f })
						foreach (Collider c in Physics.OverlapSphere(new Vector3(b.center.x, y, b.center.z) + side * (face + o), 0.3f, ~0, QueryTriggerInteraction.Ignore))
						{
							EditorGameObject owner = c.GetComponentInParent<EditorGameObject>();
							if (owner == null || !owner.transform.IsChildOf(root) || gone.Contains(owner.gameObject) || ladders.Contains(owner.gameObject)) continue;
							string nm = owner.GameObjectName ?? "";
							bool rock = Regex.IsMatch(nm, "Boulder|Rock|Stone", RegexOptions.IgnoreCase);
							if (recipePlaced.Contains(owner.gameObject) && !recipeScattered.Contains(owner.gameObject))
							{
								if (rock && !kept.Contains(nm)) kept.Add(nm);
								continue;
							}
							Bounds bb = c.bounds;
							foreach (Renderer rd in owner.GetComponentsInChildren<Renderer>()) bb.Encapsulate(rd.bounds);
							if (nm.IndexOf("Boulder", StringComparison.OrdinalIgnoreCase) < 0 && Mathf.Max(bb.size.x, bb.size.z) > 10f) continue;
							gone.Add(owner.gameObject);
							Vector3 op = owner.transform.position;
							names.Add(nm + " at " + Num(op.x - TopFrame.Origin.x) + " " + Num(op.z - TopFrame.Origin.y));
						}
			}
			if (gone.Count > 0) CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(gone, false));
			if (kept.Count > 0) Log("  WARNING: zipto: the recipe's own " + string.Join(", ", kept.ToArray()) + " on a deck ladder's climbing side - kept: move it");
			return gone.Count > 0 ? " (" + gone.Count + " of the generator's objects off their climbing side removed: " + string.Join("; ", names.ToArray()) + ")" : "";
		}

		static float GroundY(float x, float z)
		{
			Terrain terrain = terraineditor.terrain;
			return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
		}

		/// <summary>
		/// The dig step (G1): strokes of the editor's Dig brush, split by "|"; a point is x,z (on the ground), x,y,z (y metres
		/// above the ground there, below 0 under it) or x,y,z,r (r: the passage's radius as it is dug to that point, 1.4-5 m). Each stroke
		/// is pressed at its first point - a first point x,z over a dug passage is pressed in it there: a branch from it - and dug on
		/// toward each next point as the mouse would be; slope= the up/down in degrees (0: level - a level stroke on flat ground goes
		/// down until it has rock over it), r= the radius where a point names none (default 1.75), fill: Shift - the strokes fill
		/// dug tunnels back in at their points. The tunnels made or changed (null error) or why not.
		/// </summary>
		static List<GameObject> DigStep(string[] t, out string error)
		{
			error = null;
			var made = new List<GameObject>();
			Terrain land = terraineditor.terrain;
			if (land == null) { error = "dig: no island ground"; return made; }
			var opt = Options(t.Skip(1).Where(x => x.Contains("=") || x == "fill" || x == "shaft"));
			float r = opt.ContainsKey("r") ? F(opt["r"]) : 1.75f, slope = opt.ContainsKey("slope") ? F(opt["slope"]) : 0f;
			bool fill = opt.ContainsKey("fill"), shaft = opt.ContainsKey("shaft");
			if (fill && shaft) { error = "dig: shaft or fill, not both"; return made; }
			if (r < DigTunnel.MinRadius || r > DigTunnel.MaxRadius) { error = "dig: r= " + DigTunnel.MinRadius + "-" + DigTunnel.MaxRadius; return made; }
			if (Mathf.Abs(slope) > DigTunnel.MaxSlope) { error = "dig: slope= -" + DigTunnel.MaxSlope + ".." + DigTunnel.MaxSlope; return made; }
			var strokes = new List<List<string>> { new List<string>() };
			foreach (string tok in t.Skip(1))
			{
				if (tok == "|") { strokes.Add(new List<string>()); continue; }
				if (tok.Contains("=") || tok == "fill" || tok == "shaft") continue;
				strokes[strokes.Count - 1].Add(tok);
			}
			strokes = strokes.Where(s => s.Count > 0).ToList();
			const string usage = "dig <x,z | x,y,z | x,y,z,r> ... [| <next stroke>] [r=1.75] [slope=0] [fill | shaft] [as=<group>]";
			if (strokes.Count == 0) { error = usage; return made; }
			foreach (List<string> s in strokes)
			{
				if (s.Count < (fill ? 1 : 2)) { error = "dig: a stroke needs two points or more (" + string.Join(" ", s.ToArray()) + "): " + usage; return made; }
				var pts = new List<Vector3>();
				var radii = new List<float>();
				foreach (string p in s)
				{
					string[] v = p.Split(',');
					if (v.Length < 2 || v.Length > 4) { error = "dig: '" + p + "' is no point: " + usage; return made; }
					float x = F(v[0]), z = F(v[v.Length == 2 ? 1 : 2]), y = v.Length >= 3 ? F(v[1]) : 0f;
					float pr = v.Length == 4 ? F(v[3]) : r;
					if (pr < DigTunnel.MinRadius || pr > DigTunnel.MaxRadius) { error = "dig: radius " + pr + " in '" + p + "': " + DigTunnel.MinRadius + "-" + DigTunnel.MaxRadius; return made; }
					Vector2 w = WorldXZ(x, z);
					var at = new Vector3(w.x, GroundY(w.x, w.y) + y, w.y);
					// (x,z over a dug passage: pressed in it - a stroke's first point branches from it there, a fill's points fill it)
					Vector3? passage = v.Length == 2 && (fill || pts.Count == 0) ? DigTunnel.PassageAt(at) : null;
					pts.Add(passage ?? at);
					radii.Add(pr);
				}
				DigTunnel tun = DigTunnel.RecipeStroke(land, pts, radii, slope, fill, shaft);
				if (tun == null) { error = fill ? "dig fill: no dug tunnel at " + s[0] : "dig: the stroke from " + s[0] + " dug nothing"; return made; }
				if (!fill && tun.PointCount < 2) { error = "dig: the stroke from " + s[0] + " dug only " + tun.PointCount + " point(s)"; return made; }
				if (!made.Contains(tun.gameObject)) made.Add(tun.gameObject);
				// (11-08, The Rootcore) where the stroke ends, in recipe x z and metres over the sea, with its floor - to set a
				// ladder, a rat or a zone down in a dug room (a ray from over the island meets the ground above it)
				if (!fill && tun.Strokes.Count > 0 && tun.Strokes[tun.Strokes.Count - 1].Count > 0)
				{
					var endP = tun.Strokes[tun.Strokes.Count - 1][tun.Strokes[tun.Strokes.Count - 1].Count - 1];
					Vector3 e = tun.transform.TransformPoint(endP.At);
					Frame f = TopFrame;
					Vector3 rz = Quaternion.Euler(0f, -f.Yaw, 0f) * new Vector3(e.x - f.Origin.x, 0f, e.z - f.Origin.y);
					Log("dig: stroke from " + s[0] + " ends at " + rz.x.ToString("0.0") + " " + rz.z.ToString("0.0") + ", floor h=" + (e.y - DynamicIslands.EditorWaterLevel).ToString("0.0") + " r=" + endP.R.ToString("0.0") + " (ground h=" + (GroundY(e.x, e.z) - DynamicIslands.EditorWaterLevel).ToString("0.0") + ")");
				}
			}
			return made;
		}

		/// <summary>A terrain stroke through the editor's brush: brush raise|lower|flatten|smooth|sample|sand|grass|rock|seabed|auto|stamp:i
		/// at x z (a stroke there) or from x1 z1 to x2 z2 (dragged) [r=radius] [s=strength] [frames=n] [rot=degrees] [h=: flatten to
		/// that height above the sea] [line: the brush's own reach (pre-E12), for strokes along one row of ground points].</summary>
		static string Brush(string[] t)
		{
			int at = Array.IndexOf(t, "at"), from = Array.IndexOf(t, "from"), to = Array.IndexOf(t, "to");
			int last = at >= 0 ? at + 2 : to + 2;
			if ((at < 0 && (from < 0 || to < 0)) || last >= t.Length) return "brush: <tool> at <x> <z> | from <x1> <z1> to <x2> <z2> [r=] [s=] [frames=] [rot=]";
			var opt = Options(t.Skip(last + 1));
			string tool = t[1].ToLowerInvariant();
			switch (tool)
			{
				case "raise": terraineditor.modificationAction = terraineditor.TerrainModificationAction.Raise; break;
				case "lower": terraineditor.modificationAction = terraineditor.TerrainModificationAction.Lower; break;
				case "flatten": terraineditor.modificationAction = terraineditor.TerrainModificationAction.Flatten; break;
				case "sample": terraineditor.modificationAction = terraineditor.TerrainModificationAction.Sample; break;
				case "smooth": terraineditor.modificationAction = terraineditor.TerrainModificationAction.Smooth; break;
				case "auto": terraineditor.modificationAction = terraineditor.TerrainModificationAction.AutoPaint; break;
				case "sand": case "grass": case "rock": case "seabed":
					terraineditor.modificationAction = terraineditor.TerrainModificationAction.PaintLayer;
					terraineditor.paintLayer = tool == "sand" ? TerrainPainter.Sand : tool == "grass" ? TerrainPainter.Grass : tool == "rock" ? TerrainPainter.Rock : TerrainPainter.Seabed;
					break;
				default:
					if (!tool.StartsWith("stamp:")) return "brush: unknown tool '" + tool + "'";
					EditorUI.SetStamp((int)F(tool.Substring(6)));
					terraineditor.modificationAction = terraineditor.TerrainModificationAction.Stamp;
					TerrainStamps.Rotation = (opt.ContainsKey("rot") ? F(opt["rot"]) : 0f) + TopFrame.Yaw;
					break;
			}
			terraineditor.brushRadius = Mathf.Clamp(opt.ContainsKey("r") ? F(opt["r"]) : 15f, 2f, 80f);
			terraineditor.strength = Mathf.Clamp(opt.ContainsKey("s") ? F(opt["s"]) : 4f, 0.5f, 20f);
			int frames = opt.ContainsKey("frames") ? Mathf.Clamp((int)F(opt["frames"]), 1, 600) : 30;
			terraineditor te = Camera.main != null ? Camera.main.GetComponent<terraineditor>() : null;
			if (te == null) return "brush: the editor's terrain tool isn't there";
			// (flatten h=: to that height above the sea instead of the ground's where the stroke starts - a sunken building's
			// pit dug down to its floor)
			float? flattenTo = opt.ContainsKey("h") ? DynamicIslands.EditorWaterLevel + F(opt["h"]) : (float?)null;
			// (line: the brush's own reach, as before E12 - a stroke along one row of ground points moves that row only)
			terraineditor.LineReach = opt.ContainsKey("line");
			try
			{
				if (at >= 0)
				{
					Vector2 p = WorldXZ(F(t[at + 1]), F(t[at + 2]));
					te.SimulateStroke(new Vector3(p.x, GroundY(p.x, p.y), p.y), frames, 0.05f, flattenTo);
				}
				else
				{
					Vector2 a = WorldXZ(F(t[from + 1]), F(t[from + 2])), b = WorldXZ(F(t[to + 1]), F(t[to + 2]));
					te.SimulateDrag(new Vector3(a.x, GroundY(a.x, a.y), a.y), new Vector3(b.x, GroundY(b.x, b.y), b.y), frames, 0.05f, flattenTo);
				}
			}
			finally { terraineditor.LineReach = false; }
			return null;
		}

		/// <summary>Place's option (set by the place step for an unnamed object, and by line): leave it out if it is under the ground whole.</summary>
		const string SkipBuried = "\u0001skipburied";

		/// <summary>The cut ZipShaveGround makes under a rope: the land LineClear m under the rope within ShaveCore m of it, its
		/// sides rising ShaveSide m a metre out from there, never more than ShaveMost m deep (c3 round 4, 11-21).</summary>
		const float ShaveCore = 1.5f, ShaveSide = 0.7f, ShaveMost = 6f;

		/// <summary>What lies loose on the land and rides ZipShaveGround's cut down instead of holding the land (c3z48, 11-21).</summary>
		static readonly Regex ShaveRiders = new Regex(@"^(Pickup_|QuestItemPickup|Note_|Loot_)");

		/// <summary>A lever's or a mechanism's works reach far from where it stands; land under the rope more than ShaveFarPart m
		/// from such an object isn't held by it (c3z58: The Coldhold Vault's floodlever, UT_JumpPuzzle_LiftMechanism_Lever, held 10-11
		/// points 44 m off).</summary>
		static readonly Regex ShaveFarOwner = new Regex(@"Lever|Mechanism");
		const float ShaveFarPart = 12f;

		/// <summary>
		/// zipto's last step (c3 round 4, 11-21 c3z38: ropes on 10 islands 0.03-3.97 m into a hump between their ends, or 0.1-0.4 m
		/// short of the ground check's LineLow, after ZipClearGround had raised their near ends the most it does or found them on a
		/// tower, and pulled their far ends in as far as they go): the land under the rope, measured as the ground check measures it
		/// (the land and the cave hill a world builds - not what stands on them - its first NearSkip and last FarSkip m left out),
		/// is cut down to LineClear m under the rope where it comes nearer than LineLow: a pass through the hump, its sides sloping
		/// out, painted as the painter paints a slope. The land under what stands on it (the line's own floors, decks, posts, a cave,
		/// a building) and under the cave hill's skin stays as it is - trees and bushes don't hold it when the recipe reseats them
		/// (their reseat after this sets them on the cut); nothing goes under the sea. Null when the rope was clear; else the log's
		/// words (a WARNING when the rope still comes under LineLow).
		/// </summary>
		static string ZipShaveGround(EditorGameObject e, GameObject hill)
		{
			Terrain land = terraineditor.terrain;
			MeshPath_Zipline_Landmark line = e != null ? e.GetComponentInChildren<MeshPath_Zipline_Landmark>(true) : null;
			Vector3 near, far;
			if (land == null || land.terrainData == null || line == null) return null;
			Physics.SyncTransforms();
			if (!ZiplineEnds.Ends(e.gameObject, e.Props, land.transform.position, out near, out far)) return null;
			float slack;
			if (!float.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipSlack) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out slack) || float.IsNaN(slack) || float.IsInfinity(slack) || slack == 0f) slack = float.NaN;
			List<Vector3> rope = ZiplineEnds.RopeBetween(line, far, near, slack);
			if (rope.Count < 2) return null;
			List<Collider> skins = hill != null ? ZiplineEnds.CaveSkins(hill.transform) : new List<Collider>();
			Func<float, float, float> landY = (x, z) => land.SampleHeight(new Vector3(x, 0f, z)) + land.transform.position.y;
			Func<float, float, float> ground = ZiplineEnds.OverSkins(landY, skins);
			Vector3 at; float atT;
			// (measured by the ground check's rule: LineLow, coming down to FarEndLow over the metres before the far stretch - c3 round 8)
			float low0 = ZiplineEnds.RopeLowest(rope, near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, ZiplineEnds.CheckWant, out at, out atT);
			if (low0 >= ZiplineEnds.LineLow + 0.1f) return null;
			float real0 = at.y - ground(at.x, at.z);
			TerrainData td = land.terrainData;
			int res = td.heightmapResolution, ares = td.alphamapResolution;
			Vector3 tp = land.transform.position, size = td.size;
			float sea = tp.y + DynamicIslands.EditorWaterLevel, sx = size.x / (res - 1), sz = size.z / (res - 1), core = Mathf.Max(ShaveCore, 1.5f * Mathf.Max(sx, sz));
			int reachX = Mathf.CeilToInt((core + ShaveMost / ShaveSide) / sx) + 1, reachZ = Mathf.CeilToInt((core + ShaveMost / ShaveSide) / sz) + 1;
			// (what stands on the land holds it: every shown object whose foot is at or in the land, its bounds widened 1 m (a
			// heightmap step and a half when that is more; 2 m till c3 round 8, which held a cut 1.5 m off a sign) - the line's own
			// rope left out, its small pieces (floors, posts) not; trees and bushes left out when the reseat follows. Each kept
			// box knows its object, so a cut held off the rope can say by what - c3 round 8)
			float margin = Mathf.Max(1f, 1.5f * Mathf.Max(sx, sz));
			var keep = new List<KeyValuePair<Bounds, EditorGameObject>>();
			var keepPart = new List<string>();
			// (what lies loose on the land - a pickup, a find, a note, a crate - rides the cut down instead of holding it, and a
			// zone or a creature's spawn (nothing a world shows) never holds it: 11-21 c3z48, the cuts under 5 islands' ropes were held
			// by Zone_Trigger, Zone_Sound, Zone_Atmosphere and the quests' token pickups, leaving the ropes 0.03-0.76 m in the ground)
			var riders = new List<KeyValuePair<EditorGameObject, float>>();
			foreach (EditorGameObject o in UnityEngine.Object.FindObjectsOfType<EditorGameObject>())
			{
				if (o == null || !o.gameObject.activeInHierarchy) continue;
				if (recipeReseat && o != e && (Tuft.IsMatch(o.GameObjectName ?? "") || (Trunk.IsMatch(o.GameObjectName ?? "") && o.transform.up.y >= 0.5f))) continue;
				string on = o.GameObjectName ?? "";
				if (o != e && (on.StartsWith("Zone_") || ContentCatalog.IsCreature(on))) continue;
				if (o != e && ShaveRiders.IsMatch(on))
				{
					Vector3 op = o.transform.position;
					riders.Add(new KeyValuePair<EditorGameObject, float>(o, landY(op.x, op.z)));
					continue;
				}
				IEnumerable<Renderer> rs = o.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled);
				if (o == e) rs = rs.Where(r => Mathf.Max(r.bounds.size.x, r.bounds.size.z) < 4f);
				IEnumerable<Collider> cs = o == e ? Enumerable.Empty<Collider>() : o.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger);
				foreach (KeyValuePair<Bounds, Component> pb in rs.Select(r => new KeyValuePair<Bounds, Component>(r.bounds, r)).Concat(cs.Select(c => new KeyValuePair<Bounds, Component>(c.bounds, c))))
				{
					Bounds b = pb.Key;
					if (b.min.y > landY(b.center.x, b.center.z) + 0.5f) continue;
					b.Expand(new Vector3(2f * margin, 0f, 2f * margin));
					keep.Add(new KeyValuePair<Bounds, EditorGameObject>(b, o));
					keepPart.Add(pb.Value.GetType().Name + " " + pb.Value.name + " " + Num(b.size.x) + " x " + Num(b.size.z) + " m");
				}
			}
			float[,] before = td.GetHeights(0, 0, res, res), after = (float[,])before.Clone();
			float len = new Vector2(far.x - near.x, far.z - near.z).magnitude;
			int onHill = 0;
			var deep = new HashSet<int>();
			var cut = new HashSet<int>();
			var held = new HashSet<int>();
			var free = new HashSet<int>();
			var holders = new Dictionary<EditorGameObject, int>();
			var holderPart = new Dictionary<EditorGameObject, string>();
			var farParts = new Dictionary<string, int>();
			for (int i = 0; i + 1 < rope.Count; i++)
			{
				Vector3 a = rope[i], b = rope[i + 1];
				int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(b.x - a.x, b.z - a.z).magnitude / 0.5f));
				for (int k = 0; k <= steps; k++)
				{
					Vector3 p = Vector3.Lerp(a, b, k / (float)steps);
					float fromNear = new Vector2(p.x - near.x, p.z - near.z).magnitude;
					if (fromNear < ZiplineEnds.NearSkip || fromNear > len - ZiplineEnds.FarSkip) continue;
					float top = p.y - ZiplineEnds.LineClear;
					if (ground(p.x, p.z) <= p.y - ZiplineEnds.LineWant(new Vector2(far.x - p.x, far.z - p.z).magnitude) - 0.1f) continue;
					if (ground(p.x, p.z) > landY(p.x, p.z) + 0.01f) { onHill++; continue; }
					int cx = Mathf.RoundToInt((p.x - tp.x) / sx), cz = Mathf.RoundToInt((p.z - tp.z) / sz);
					for (int dz = -reachZ; dz <= reachZ; dz++)
						for (int dx = -reachX; dx <= reachX; dx++)
						{
							int x = cx + dx, z = cz + dz, id = z * res + x;
							if (x < 1 || z < 1 || x > res - 2 || z > res - 2 || held.Contains(id)) continue;
							float wx = tp.x + x * sx, wz = tp.z + z * sz, d = new Vector2(wx - p.x, wz - p.z).magnitude;
							float h0 = before[z, x] * size.y + tp.y, now = after[z, x] * size.y + tp.y;
							float target = Mathf.Max(top + Mathf.Max(0f, d - core) * ShaveSide, sea + 0.3f);
							if (h0 - target > ShaveMost) { target = h0 - ShaveMost; deep.Add(id); }
							if (target >= now - 0.005f) continue;
							if (!free.Contains(id))
							{
								EditorGameObject by = null;
								for (int ki = 0; ki < keep.Count; ki++)
								{
									KeyValuePair<Bounds, EditorGameObject> kb = keep[ki];
									if (wx < kb.Key.min.x || wx > kb.Key.max.x || wz < kb.Key.min.z || wz > kb.Key.max.z) continue;
									Vector3 op = kb.Value.transform.position;
									if (kb.Value != e && ShaveFarOwner.IsMatch(kb.Value.GameObjectName ?? "") && new Vector2(wx - op.x, wz - op.z).magnitude > ShaveFarPart)
									{
										string fk = kb.Value.GameObjectName + "'s " + keepPart[ki];
										int fn; farParts.TryGetValue(fk, out fn); farParts[fk] = fn + 1;
										continue;
									}
									by = kb.Value;
									if (!holderPart.ContainsKey(by)) holderPart[by] = keepPart[ki];
									break;
								}
								if (by != null) { held.Add(id); int hn; holders.TryGetValue(by, out hn); holders[by] = hn + 1; continue; }
								free.Add(id);
							}
							after[z, x] = (target - tp.y) / size.y;
							cut.Add(id);
						}
				}
			}
			Vector3 corner = new Vector3(TopFrame.Origin.x, 0f, TopFrame.Origin.y);
			string where0 = Num(at.x - corner.x) + " " + Num(at.z - corner.z);
			// (which objects held the land, the most first: the recipe's own, its scattered ones, or the island's - c3 round 8)
			string heldBy = holders.Count == 0 ? "" : " - held by " + string.Join("; ", holders.OrderByDescending(kv => kv.Value).Take(4).Select(kv => kv.Key.GameObjectName + " at " +
				Num(kv.Key.transform.position.x - corner.x) + " " + Num(kv.Key.transform.position.z - corner.z) + " (" + kv.Value + " points, " +
				(recipePlaced.Contains(kv.Key.gameObject) ? "the recipe's" : recipeScattered.Contains(kv.Key.gameObject) ? "the recipe's scattered" : kv.Key == e ? "the line's own" : "the island's") +
				(holderPart.ContainsKey(kv.Key) ? ", its " + holderPart[kv.Key] : "") + ")").ToArray()) +
				(holders.Count > 4 ? " and " + (holders.Count - 4) + " more" : "");
			string farNote = farParts.Count == 0 ? "" : "; not held by " + string.Join("; ", farParts.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key + " (" + kv.Value + " points, over " + Num(ShaveFarPart) + " m off it)").ToArray());
			if (cut.Count == 0)
				return LogWords("  WARNING: zipto: its rope comes " + Num(real0) + " m over the ground at " + where0 + " and the land under it can't be cut (" + held.Count + " points under objects, " + onHill +
					" rope points over the cave hill's skin)" + heldBy + farNote + ": move an end");
			float[,,] alphaBefore = td.GetAlphamaps(0, 0, ares, ares);
			float[,] maskBefore = terraineditor.paintMask != null ? (float[,])terraineditor.paintMask.Clone() : null;
			td.SetHeights(0, 0, after);
			Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
			float most = 0f;
			foreach (int i in cut)
			{
				int x = i % res, z = i / res;
				float wx = tp.x + x * sx, wz = tp.z + z * sz;
				most = Mathf.Max(most, (before[z, x] - after[z, x]) * size.y);
				min = Vector3.Min(min, new Vector3(wx, 0f, wz)); max = Vector3.Max(max, new Vector3(wx, 0f, wz));
				if (terraineditor.paintMask == null) continue;
				int ax = Mathf.FloorToInt((wx - tp.x) / size.x * ares), az = Mathf.FloorToInt((wz - tp.z) / size.z * ares);
				for (int az2 = az - 1; az2 <= az + 1; az2++)
					for (int ax2 = ax - 1; ax2 <= ax + 1; ax2++)
						if (ax2 >= 0 && az2 >= 0 && ax2 < terraineditor.paintMask.GetLength(1) && az2 < terraineditor.paintMask.GetLength(0)) terraineditor.paintMask[az2, ax2] = 0f;
			}
			TerrainPainter.PaintWorldArea(land, sea, min - new Vector3(3f, 0f, 3f), max + new Vector3(3f, 0f, 3f), terraineditor.paintMask);
			CommandUndoRedo.UndoRedoManager.Insert(new TerrainStrokeCommand(td, new RectInt(0, 0, res, res), before, new RectInt(0, 0, ares, ares), alphaBefore, maskBefore, terraineditor.paintMask));
			// (the loose things on the cut down with it: by the land's drop under their pivot, when they lay on it - within 0.5 m)
			int rode = 0;
			foreach (KeyValuePair<EditorGameObject, float> kr in riders)
			{
				Vector3 op = kr.Key.transform.position;
				float drop = kr.Value - landY(op.x, op.z);
				if (drop < 0.01f || op.y > kr.Value + 0.5f || op.y < kr.Value - 1f) continue;
				kr.Key.transform.position = op - Vector3.up * drop;
				rode++;
			}
			if (rode > 0) Log("  zipto: " + rode + " pickup(s), note(s) or crate(s) on the cut under the rope set down with it");
			Physics.SyncTransforms();
			Vector3 at1; float t1;
			float low1 = ZiplineEnds.RopeLowest(rope, near, far, ground, ZiplineEnds.NearSkip, ZiplineEnds.FarSkip, ZiplineEnds.CheckWant, out at1, out t1);
			float real1 = at1.y - ground(at1.x, at1.z);
			string words = "zipto: its rope came " + Num(real0) + " m over the ground at " + where0 + " - the land under it cut down up to " + Num(most) + " m (" + cut.Count + " points, " +
				Num(min.x - corner.x) + " " + Num(min.z - corner.z) + " .. " + Num(max.x - corner.x) + " " + Num(max.z - corner.z) + ")" + (held.Count > 0 ? ", " + held.Count + " points held by objects on them" + heldBy : "") +
				(onHill > 0 ? ", " + onHill + " rope points over the cave hill's skin" : "") + (deep.Count > 0 ? ", " + deep.Count + " points held at " + Num(ShaveMost) + " m deep" : "") + farNote + "; the least now " + Num(real1) + " m at " + Num(at1.x - corner.x) + " " + Num(at1.z - corner.z);
			return LogWords(low1 < ZiplineEnds.LineLow ? "  WARNING: " + words + ": still under the ground check's " + Num(ZiplineEnds.LineWant(new Vector2(far.x - at1.x, far.z - at1.z).magnitude)) + " m there - move an end" : "  " + words);
		}

		static string LogWords(string words) { Log(words); return words; }

		/// <summary>A building's thin pieces a rope may pass out through: a wall, fence, rail, window or door piece.</summary>
		static readonly Regex ThinPiece = new Regex("Wall|Fence|Rail|Window|Door|Banister", RegexOptions.IgnoreCase);

		/// <summary>
		/// zipto's very last step (11-21 c3z48: Cooling Spire's and Hookline Stack's tower ropes left their decks through the
		/// top storey's RT_Wall1 3.5 m out, which the ground check fails as a rope through an object): the rope as it ends up
		/// (its zip.slack), tested as the ground check tests it (DevTestsHang.RopeThrough - its first NearSkip and last FarSkip m
		/// left out); a wall, fence, rail, window or door piece it passes through is left out, an opening where it leaves (up to
		/// 6). Anything else it passes through is warned. Returns the pieces left out.
		/// </summary>
		static int ZipClearThrough(EditorGameObject e)
		{
			Terrain land = terraineditor.terrain;
			MeshPath_Zipline_Landmark line = e != null ? e.GetComponentInChildren<MeshPath_Zipline_Landmark>(true) : null;
			Transform root = GameObject.Find("PlacedObjects")?.transform;
			Vector3 near, far;
			if (land == null || line == null || root == null || !ZiplineEnds.Ends(e.gameObject, e.Props, land.transform.position, out near, out far)) return 0;
			float slack;
			if (!float.TryParse(ObjectProps.Get(e.Props, ZiplineEnds.ZipSlack) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out slack) || float.IsNaN(slack) || float.IsInfinity(slack) || slack == 0f) slack = float.NaN;
			Vector3 corner = new Vector3(TopFrame.Origin.x, 0f, TopFrame.Origin.y);
			var gone = new List<string>();
			for (int round = 0; round < 6; round++)
			{
				Physics.SyncTransforms();
				List<Vector3> rope = ZiplineEnds.RopeBetween(line, far, near, slack);
				Vector3 at = near; Transform hit = null;
				string what = rope.Count >= 2 ? RopeThrough(rope, near, far, e.transform, root, out at, out hit) : null;
				if (what == null) break;
				EditorGameObject owner = hit.GetComponent<EditorGameObject>();
				string n = owner != null ? owner.GameObjectName ?? hit.name : hit.name;
				string where = Num(at.x - corner.x) + " " + Num(at.z - corner.z) + " h=" + Num(at.y);
				// (a building's piece, not a cliff or a rock wall: at most 7 m across and 5 m high)
				Renderer[] hr = hit.GetComponentsInChildren<Renderer>(false);
				Bounds hb = hr.Length > 0 ? hr[0].bounds : new Bounds(hit.position, Vector3.zero);
				foreach (Renderer rd in hr) hb.Encapsulate(rd.bounds);
				if (owner == null || !ThinPiece.IsMatch(n) || Mathf.Max(hb.size.x, hb.size.z) > 7f || hb.size.y > 5f)
				{
					Log("  WARNING: zipto: its rope runs through " + what + " at " + where + ": move an end or that");
					break;
				}
				CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(new[] { hit.gameObject }, false));
				gone.Add(n + " at " + where);
			}
			if (gone.Count > 0) Log("  zipto: " + gone.Count + " wall or fence piece(s) its rope ran through left out (an opening where it passes): " + string.Join("; ", gone.ToArray()));
			return gone.Count;
		}

		/// <summary>Pieces the recipe left out because they were under the ground whole (this run).</summary>
		static int leftOutBuried;
		static bool recipeNoPosts, recipeReseat;

		/// <summary>Grass tufts and bushes the generator scattered: a recipe's strokes lowered the ground under some (Firedance Isle's
		/// pits: tufts 0.5-1 m in the air; Ironreef's peak: Monstera 6-9 m up, IG5 11-18) - the reseat drops the floating ones onto the ground too, and a zipline's cut under the rope goes under them (c3z62: a scattered Balboa_Bush_03 held 13 points of Tracker's Rise's cut, its rope 0.12 m in the ground).</summary>
		static readonly Regex Tuft = new Regex(@"^(CaravanIsland_\w*Grass|Bush2?|SmallBush_\d+|Balboa_Bush_0[13] Variant|Monstera_\d+|Banana_Bush_\d+)$");

		/// <summary>At a recipe's save (unless "reseat off"): trees and palms whose trunk foot is more than 1 m under the land (the
		/// ground check's "sunk trunk") or more than 0.4 m over it with nothing placed under it ("trunk foot floating": the
		/// recipe's strokes lowered the ground under it) set back on the ground round their pivot, as Place sets them. The foot
		/// stays within 1 m under the land (lower ground under it is a cave floor or a ledge: the land then), and a tree whose
		/// foot would be inside a placed rock or building stays where it is - unless it is sunk: then it is left out (The General
		/// Post Office's two snowy pines 1.4-1.5 m into Postmaster's Hill, their foot in a rock's bounds, c3z29 11-21). A tree whose foot is on a steep drop's rim
		/// (the ground check's "trunk over a drop": 3 of 8 points a trunk's width round it 0.8 m or more lower) moves up to 3 m
		/// onto the nearest spot that is not (27 such trees on 20 islands, IG5 11-18).</summary>
		static int ReseatSunkTrees(float seaY)
		{
			EditorGameObject[] all = GameObject.Find("PlacedObjects").transform.GetComponentsInChildren<EditorGameObject>();
			int reseated = 0, kept = 0, moved = 0;
			var sunkKept = new List<GameObject>();
			foreach (EditorGameObject e in all)
			{
				if (!e.gameObject.activeSelf) continue;
				bool tuft = Tuft.IsMatch(e.GameObjectName ?? "");
				if (!tuft && !Trunk.IsMatch(e.GameObjectName ?? "")) continue;
				// (a trunk laid down by the recipe - tilted more than 60 degrees - is no standing tree: its pivot is its cut end, not a
				// foot, and "on its bottom" stands it on its branch tips. Rootcellar Deep's root trees, laid with their cut end in
				// the seabed, went back up 2-4 m into the water at the save - CP4 11-20)
				if (!tuft && e.transform.up.y < 0.5f) continue;
				Vector3 p = e.transform.position;
				float land = GroundY(p.x, p.z);
				if (land <= seaY) continue;
				bool sunk = !tuft && land - p.y > 1f, floating = p.y - land > 0.4f && !PlacedUnder(e, p, land, all);
				float rt = 0.6f * Mathf.Max(0.5f, e.transform.lossyScale.x);
				bool rim = !tuft && !floating && OnDropRim(p.x, p.z, rt);
				if (rim)
				{
					Vector2? off = OffDropRim(p.x, p.z, rt, seaY);
					if (off.HasValue) { p = new Vector3(off.Value.x, p.y, off.Value.y); land = GroundY(p.x, p.z); sunk = true; }
				}
				if (!sunk && !floating) continue;
				float foot = PlacementOptions.LowestGroundUnder(new Bounds(p, new Vector3(1f, 0f, 1f)));
				if (foot > land || foot < land - 1f) foot = land;
				Vector3 to = new Vector3(p.x, foot + PlacementOptions.PivotAboveBottom(e.gameObject), p.z);
				if (InsidePlaced(to, e, all))
				{
					// (sunk more than 1 m with nowhere to stand it: the ground check fails it - left out, as a buried plant is)
					if (!tuft && GroundY(e.transform.position.x, e.transform.position.z) - e.transform.position.y > 1f) sunkKept.Add(e.gameObject);
					else kept++;
					continue;
				}
				e.transform.position = to;
				reseated++;
				if (rim) moved++;
			}
			if (sunkKept.Count > 0)
			{
				CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(sunkKept, false));
				Log("  reseat trees: " + sunkKept.Count + " sunk trees whose foot would be inside a placed object left out");
			}
			if (reseated + kept > 0) Log("  reseat trees: " + reseated + " sunk or floating trees and palms (and floating grass tufts and bushes) set back on the ground" + (moved > 0 ? ", " + moved + " of them moved off a drop's rim" : "") + (kept > 0 ? ", " + kept + " left (their foot would be inside a placed object)" : ""));
			return reseated;
		}

		/// <summary>3 or more of 8 points round x z, <paramref name="rt"/> out, 0.8 m or more under the land at x z (the ground
		/// check's "trunk over a drop", by the land alone).</summary>
		static bool OnDropRim(float x, float z, float rt)
		{
			float y = GroundY(x, z);
			int over = 0;
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI / 4f;
				if (y - GroundY(x + rt * Mathf.Cos(a), z + rt * Mathf.Sin(a)) > 0.8f) over++;
			}
			return over >= 3;
		}

		/// <summary>The nearest spot within 3 m of x z (rings 0.5 m apart, 16 ways) over the sea and off a drop's rim; null = none.</summary>
		static Vector2? OffDropRim(float x, float z, float rt, float seaY)
		{
			for (float d = 0.5f; d <= 3.01f; d += 0.5f)
				for (int k = 0; k < 16; k++)
				{
					float a = k * Mathf.PI / 8f, nx = x + d * Mathf.Cos(a), nz = z + d * Mathf.Sin(a);
					if (GroundY(nx, nz) > seaY + 0.3f && !OnDropRim(nx, nz, rt)) return new Vector2(nx, nz);
				}
			return null;
		}

		/// <summary>Something placed (a deck, a rock, a planter) under a tree's foot, above the land: it stands on that, not on the land.</summary>
		static bool PlacedUnder(EditorGameObject self, Vector3 foot, float land, EditorGameObject[] all)
		{
			foreach (RaycastHit h in Physics.RaycastAll(foot + Vector3.up * 0.3f, Vector3.down, foot.y - land + 0.3f, ~0, QueryTriggerInteraction.Ignore))
				if (!h.collider.transform.IsChildOf(self.transform) && h.collider.GetComponent<Terrain>() == null) return true;
			// (and by what shows, its colliders maybe switched off in the editor: a deck, a roof garden's planter)
			foreach (EditorGameObject o in all)
			{
				if (o == self || !o.gameObject.activeInHierarchy || Trunk.IsMatch(o.GameObjectName ?? "")) continue;
				foreach (Renderer c in o.GetComponentsInChildren<Renderer>())
				{
					Bounds b = c.bounds;
					if (foot.x >= b.min.x && foot.x <= b.max.x && foot.z >= b.min.z && foot.z <= b.max.z && b.max.y > land + 0.1f && b.max.y <= foot.y + 0.3f) return true;
				}
			}
			return false;
		}

		/// <summary>A point inside the bounds of a placed object's solid colliders (not a tree's or a palm's).</summary>
		static bool InsidePlaced(Vector3 at, EditorGameObject self, EditorGameObject[] all)
		{
			Vector3 q = at + Vector3.up * 0.1f;
			foreach (EditorGameObject o in all)
			{
				if (o == self || !o.gameObject.activeInHierarchy || Trunk.IsMatch(o.GameObjectName ?? "")) continue;
				foreach (Collider c in o.GetComponentsInChildren<Collider>())
					if (c.enabled && !c.isTrigger && c.bounds.Contains(q)) return true;
			}
			return false;
		}

		/// <summary>
		/// Posts under the floors and decks over nothing (ROADMAP CA30, FloorsOverNothing): at each corner nothing holds, one
		/// of Raft's posts (RT_Pillar) stretched from 0.3 m into the ground or sea floor up to the floor's underside, as Raft's
		/// stilt houses stand. A corner with a post within 0.4 m already (the check's reach) is left as it is. A post never goes
		/// through a thing (furniture, a prop, a chest, a tree's trunk - IG7, the user 2026-10-18: a grid of posts under The Isobar
		/// Club's flat roof went through its pianos and its pool table): it moves in under the floor up to 0.6 m, or is left out.
		/// Returns how many were stood.
		/// </summary>
		static int StandPosts()
		{
			Terrain ground = terraineditor.terrain;
			if (ground == null) return 0;
			Physics.SyncTransforms();
			Transform placedRoot = GameObject.Find("PlacedObjects").transform;
			int floors;
			List<FloorGap> gaps = FloorsOverNothing(placedRoot.GetComponentsInChildren<EditorGameObject>(), 0.3f, ground, DynamicIslands.EditorWaterLevel, out floors);
			var stood = new List<Vector3>();
			List<Bounds> things = gaps.Count > 0 ? PostObstacles(placedRoot) : new List<Bounds>();
			List<Bounds> building = gaps.Count > 0 ? PostBuilding(placedRoot.GetComponentsInChildren<EditorGameObject>()) : new List<Bounds>();
			int dodged = 0, blocked = 0, overBuilding = 0;
			foreach (FloorGap d in gaps)
				foreach (Vector3 corner in d.Corners)
				{
					if (stood.Any(s => new Vector2(s.x - corner.x, s.z - corner.z).magnitude < 0.4f && Mathf.Abs(s.y - corner.y) < 0.5f)) continue;
					float foot = GroundY(corner.x, corner.z) - 0.3f;
					if (corner.y - foot < 0.5f || !ObjectLimit.Allow(1)) continue;
					// (not down through a building under the floor: a roof slab's corner over a room's ceiling with no collider
					// read as over nothing, and its post went through the room from the roof to the pit's floor - Shelter
					// Atoll's storeroom, RF5 11-05. The building holds the floor there; no post)
					// (only a building whose top comes within 1.5 m of the floor holds it, as FloorsOverNothing reads it; one lower down
					// takes the post on its top instead - 11-21 c3y11, Engine Court's 6.6 m ridge deck over the court's posts had all four
					// posts left out and stood on its ladders alone)
					List<Bounds> under = building.Where(bb => bb.max.y < corner.y - 0.3f && PostBlocked(new List<Bounds> { bb }, corner, foot)).ToList();
					float standOn = float.MinValue;
					if (under.Count > 0)
					{
						float onTop = under.Max(bb => bb.max.y);
						if (onTop > corner.y - 1.5f) { overBuilding++; continue; }
						foot = standOn = onTop;
						if (corner.y - foot < 0.5f) continue;
					}
					// (not through a thing: in under the floor, towards its middle, until the post's line is clear)
					Vector3 p = corner;
					if (PostBlocked(things, p, foot))
					{
						bool found = false;
						Vector3 toMid = new Vector3(d.Box.center.x - corner.x, 0f, d.Box.center.z - corner.z);
						foreach (float step in new[] { 0.2f, 0.4f, 0.6f })
							foreach (Vector3 dir in new[] { new Vector3(Mathf.Sign(toMid.x), 0f, 0f), new Vector3(0f, 0f, Mathf.Sign(toMid.z)), new Vector3(Mathf.Sign(toMid.x), 0f, Mathf.Sign(toMid.z)).normalized })
							{
								Vector3 c = corner + dir * step;
								if (found || c.x < d.Box.min.x + 0.05f || c.x > d.Box.max.x - 0.05f || c.z < d.Box.min.z + 0.05f || c.z > d.Box.max.z - 0.05f) continue;
								float f = Mathf.Max(GroundY(c.x, c.z) - 0.3f, standOn);
								if (PostBlocked(things, c, f)) continue;
								p = c; foot = f; found = true;
							}
						if (!found) { blocked++; continue; }
						dodged++;
					}
					bool spawned;
					if (!StandPost(placedRoot, p, foot, out spawned)) { if (!spawned) return stood.Count; continue; }
					stood.Add(p);
				}
			if (stood.Count > 0) Log("  " + stood.Count + " posts stood under " + gaps.Count + " floors and decks over nothing" + (dodged > 0 ? ", " + dodged + " moved off things" : ""));
			if (blocked > 0) Log("  " + blocked + " posts left out: a thing under the floor's corner (furniture, a prop, a trunk)");
			if (overBuilding > 0) Log("  " + overBuilding + " posts left out: a building under the floor's corner (a room, a floor below)");
			return stood.Count;
		}

		/// <summary>What a post must not go through (StandPosts): the boxes of the placed things - not building pieces, the land's
		/// own, pipes and ladders (the overlap check's kinds, IG7) - a tree or a plant by its colliders (its trunk), the rest by its meshes.</summary>
		static List<Bounds> PostObstacles(Transform placedRoot)
		{
			var boxes = new List<Bounds>();
			foreach (EditorGameObject e in placedRoot.GetComponentsInChildren<EditorGameObject>())
			{
				string n = e.GameObjectName ?? "";
				if (!e.gameObject.activeInHierarchy || ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Note_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_") ||
					Structural.IsMatch(n) || FloorLike.IsMatch(n) || OverlapIgnore.IsMatch(n)) continue;
				if (Vegetation.IsMatch(n))
				{
					foreach (Collider c in e.GetComponentsInChildren<Collider>(false))
						if (c.enabled && !c.isTrigger) boxes.Add(c.bounds);
					continue;
				}
				foreach (Renderer r in e.GetComponentsInChildren<Renderer>(false))
					if (r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly && Mathf.Max(r.bounds.size.x, r.bounds.size.y, r.bounds.size.z) < OverlapBig) boxes.Add(r.bounds);
			}
			return boxes;
		}

		/// <summary>What a post must not go down through either (StandPosts): the building pieces' boxes - floors, walls, ceilings and
		/// roofs (the land's own, pipes and ladders left out as in PostObstacles). Only the ones wholly under the floor count.</summary>
		/// <summary>The parts of placed things with no solid collider in the editor (a pole, a beam): what a floor may lie on that
		/// a sphere cast can't find.</summary>
		static List<Bounds> CollisionlessParts(IEnumerable<EditorGameObject> placed)
		{
			var boxes = new List<Bounds>();
			foreach (EditorGameObject e in placed)
			{
				string n = e.GameObjectName ?? "";
				if (!e.gameObject.activeInHierarchy || n.StartsWith("Zone_") || ContentCatalog.IsCreature(n) || FloorLike.IsMatch(n)) continue;
				if (e.GetComponentsInChildren<Collider>(false).Any(c => c.enabled && !c.isTrigger)) continue;
				foreach (Renderer r in e.GetComponentsInChildren<Renderer>(false))
					if (r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly) boxes.Add(r.bounds);
			}
			return boxes;
		}

		static List<Bounds> PostBuilding(IEnumerable<EditorGameObject> placed)
		{
			var boxes = new List<Bounds>();
			foreach (EditorGameObject e in placed)
			{
				string n = e.GameObjectName ?? "";
				if (!e.gameObject.activeInHierarchy || zipDeckLadders.Contains(e.gameObject) || n.StartsWith("Zone_") || OverlapIgnore.IsMatch(n) || n.IndexOf("Pillar", StringComparison.OrdinalIgnoreCase) >= 0 ||
					!(Structural.IsMatch(n) || FloorLike.IsMatch(n) || n.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
				foreach (Renderer r in e.GetComponentsInChildren<Renderer>(false))
					if (r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly) boxes.Add(r.bounds);
			}
			return boxes;
		}

		/// <summary>Whether a post at <paramref name="top"/> (the floor's underside) down to <paramref name="foot"/> goes more than the
		/// overlap check's margin into one of <paramref name="things"/> (its radius ~0.1 m).</summary>
		static bool PostBlocked(List<Bounds> things, Vector3 top, float foot)
		{
			const float r = 0.1f;
			foreach (Bounds b in things)
			{
				if (b.max.y - OverlapMargin < foot || b.min.y + OverlapMargin > top.y) continue;
				if (top.x + r > b.min.x + OverlapMargin && top.x - r < b.max.x - OverlapMargin && top.z + r > b.min.z + OverlapMargin && top.z - r < b.max.z - OverlapMargin) return true;
			}
			return false;
		}

		/// <summary>The box round an object's shown meshes, as the ground check measures it.</summary>
		static Bounds MeshBox(GameObject go)
		{
			Renderer[] rs = go.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
			if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			return b;
		}

		/// <summary>
		/// The save's last grounding step (11-21): what the ground check would call a floating rock or a buried thing, set right
		/// in the editor. A loose rock or boulder whose underside is over what is under it (RockFloats) goes down till most of
		/// its nine points rest - or is left out when that would sink it more than half its height; a prop whose whole top is
		/// under the ground (the check's "buried" rule) goes up onto the ground at its middle, when it is no more than 2.5 m
		/// down (deeper is a thing set in a cave or a tunnel on purpose: left, the check reports it). Strokes after the scatter
		/// or the place moved the ground under them: Engine Court's, Thalassa Mooring's and Caisson Yard's boulders hung 0.5-2 m
		/// in the air, Caisson Yard's barrels and forklift 0.2-1.4 m in the yard (the ground check 11-21).
		/// </summary>
		static void SettleAtSave(float sea)
		{
			Terrain land = terraineditor.terrain;
			GameObject placed = GameObject.Find("PlacedObjects");
			if (land == null || placed == null) return;
			// (the ground as the world will have it - the caves' rooms cut - and a thing in a cave piece's room left alone: the
			// land under a den's floor is not its ground. Old Mine Islet's rails, carts and crates on its den's 4.25 m floor were
			// set up 1.8-2.6 m onto the land the editor had not cut yet, and floated in the world, c3z40 11-21)
			CaveGround.EditorCatchUp(land);
			Physics.SyncTransforms();
			Func<Vector3, bool> inRoom = CaveGround.EditorRoomTest();
			var lowered = new List<string>(); var lifted = new List<string>(); var hide = new List<GameObject>();
			int inRooms = 0;
			foreach (EditorGameObject e in placed.transform.GetComponentsInChildren<EditorGameObject>())
			{
				if (e == null || !e.gameObject.activeInHierarchy) continue;
				Transform o = e.transform;
				string n = (e.GameObjectName ?? o.name).Replace("(Clone)", "").Trim();
				bool rock = LooseRock.IsMatch(n);
				if (!rock && NotABase.IsMatch(n)) continue;
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Note_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_") ||
					n.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0 || Hangs.IsMatch(n) || ChainLink.IsMatch(n) || n.StartsWith("ZiplinePath", StringComparison.Ordinal) ||
					n.StartsWith("VP_Skylift", StringComparison.Ordinal) || n == "Elevator" || CaveBacks.Wants(n) || Trunk.IsMatch(n) ||
					ContentCatalog.IsTreasure(n)) continue; // (buried treasure is meant to be under the ground: Outer Breakwater's two were lifted 11-21)
				Renderer[] rs = o.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				if (rs.Length == 0) continue;
				float bottom = rs.Min(r => r.bounds.min.y);
				Bounds whole = rs[0].bounds;
				foreach (Renderer r in rs) whole.Encapsulate(r.bounds);
				Renderer[] baseParts = rs.Where(r => r.bounds.min.y < bottom + 0.6f).ToArray();
				Bounds b = baseParts[0].bounds;
				foreach (Renderer r in baseParts) b.Encapsulate(r.bounds);
				string at = n + " at " + Num(b.center.x) + " " + Num(b.center.z);
				if (rock)
				{
					int seen, high; float most, least;
					if (b.min.y <= sea + 0.3f || !RockFloats(o, b, land, out seen, out high, out most, out least)) continue;
					var gaps = new List<float>();
					for (int k = 0; k < 9; k++)
					{
						float g = RockGap(o, Mathf.Lerp(b.min.x, b.max.x, 0.25f + 0.25f * (k % 3)), Mathf.Lerp(b.min.z, b.max.z, 0.25f + 0.25f * (k / 3)), b, land);
						if (!float.IsNaN(g)) gaps.Add(g);
					}
					gaps.Sort();
					float allow = 0.3f + 0.15f * Mathf.Max(b.extents.x, b.extents.z);
					float d = gaps.Count == 0 ? 0f : Mathf.Max(0f, gaps[gaps.Count / 2] - allow * 0.5f);
					if (d > 0f && d <= 0.5f * whole.size.y)
					{
						o.position += Vector3.down * d;
						Physics.SyncTransforms();
						if (!RockFloats(o, new Bounds(b.center + Vector3.down * d, b.size), land, out seen, out high, out most, out least)) { lowered.Add(at + " (" + Num(d) + " m)"); continue; }
						o.position += Vector3.up * d;
						Physics.SyncTransforms();
					}
					hide.Add(e.gameObject);
					continue;
				}
				float ground = GroundY(b.center.x, b.center.z);
				float under = ground - whole.max.y;
				if (under <= 0.05f || whole.max.y <= sea - 1f || under > 2.5f || InDugPassage(whole)) continue;
				if (inRoom(whole.center) || inRoom(new Vector3(b.center.x, b.min.y + 0.1f, b.center.z)) || inRoom(new Vector3(b.center.x, whole.max.y - 0.05f, b.center.z))) { inRooms++; continue; }
				o.position += Vector3.up * (ground - b.min.y);
				lifted.Add(at + " (" + Num(ground - b.min.y) + " m)");
			}
			if (hide.Count > 0) CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(hide, false));
			if (lowered.Count + hide.Count > 0) Log("  " + lowered.Count + " rocks in the air set down on what is under them" + (lowered.Count > 0 ? ": " + string.Join("; ", lowered.Take(12).ToArray()) : "") + (hide.Count > 0 ? "; " + hide.Count + " left out (too far up to set down)" : ""));
			if (lifted.Count > 0) Log("  " + lifted.Count + " things under the ground set up on it: " + string.Join("; ", lifted.Take(12).ToArray()));
			if (inRooms > 0) Log("  " + inRooms + " things under the land but in a cave piece's room left on its floor");
			Physics.SyncTransforms();
		}

		/// <summary>What BuriedWhole kept only because its middle is in a dug passage (its top in the rock): the save logs them.</summary>
		static readonly List<string> KeptByMiddle = new List<string>();

		/// <summary>Whether an object is under the ground whole (ShowsAboveGround).</summary>
		static bool BuriedWhole(GameObject go)
		{
			Terrain ground = terraineditor.terrain;
			Renderer[] rs = PlacementOptions.ShapeRenderers(go);
			if (ground == null || rs.Length == 0) return false;
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			// (its top in a dug tunnel's passage: under the hill's terrain but in the open - 11-04, Ironreef's planks and spikes
			// placed with h= in the spike drift were all left out as buried)
			// (or its middle: a thing taller than the passage - cavegate's vines in a gallery's neck, their top in the roof's rock -
			// was left out at save, 11-05)
			if (DigTunnel.InsideAny(new Vector3(b.center.x, b.max.y, b.center.z), 0f, true)) return false;
			if (DigTunnel.InsideAny(b.center, 0f, true))
			{
				// (the save logs what only the middle kept, so a recipe can see what the rule keeps - the review 11-05)
				if (!ShowsAboveGround(b, ground)) KeptByMiddle.Add(go.name + " at " + Num(b.center.x) + " " + Num(b.center.y) + " " + Num(b.center.z));
				return false;
			}
			// (or in a placed cave's room: a cave piece's roof over its middle and its floor under it - Sinkmarket's bonded cellar,
			// kit_st under the raised mound, lost both its caveloot stones at the save as buried, cv43 11-21)
			if (!ShowsAboveGround(b, ground) && InPlacedCave(b.center))
			{
				KeptByMiddle.Add(go.name + " at " + Num(b.center.x) + " " + Num(b.center.y) + " " + Num(b.center.z) + " (in a placed cave)");
				return false;
			}
			return !ShowsAboveGround(b, ground);
		}

		/// <summary>Whether a point is in a placed cave piece's passage: the nearest cave piece met going up is a roof (facing down,
		/// within 12 m) and the nearest going down a floor (facing up, within 3 m).</summary>
		static bool InPlacedCave(Vector3 p)
		{
			System.Func<Collider, bool> cave = c =>
			{
				EditorGameObject eg = c.GetComponentInParent<EditorGameObject>();
				string n = eg != null ? eg.GameObjectName ?? "" : "";
				return n.IndexOf("Cave", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Tunnel", StringComparison.OrdinalIgnoreCase) >= 0;
			};
			System.Func<Vector3, float, RaycastHit?> first = (d, len) =>
			{
				RaycastHit? best = null;
				foreach (RaycastHit h in Physics.RaycastAll(p, d, len, ~0, QueryTriggerInteraction.Ignore))
					if (cave(h.collider) && (!best.HasValue || h.distance < best.Value.distance)) best = h;
				return best;
			};
			RaycastHit? up = first(Vector3.up, 12f), down = first(Vector3.down, 3f);
			return up.HasValue && up.Value.normal.y < -0.3f && down.HasValue && down.Value.normal.y > 0.3f;
		}

		/// <summary>
		/// Whether the box around an object's meshes shows above the terrain somewhere over its footprint (5 x 5 points, 15 %
		/// in): its top at least 3 cm over the ground there (a third of a very flat thing's height). A floor laid flush with
		/// the ground shows 4 cm, a flat flower 5 cm; a boulder sunk to 1 cm doesn't.
		/// </summary>
		/// <summary>
		/// CIFloating: whether an object's base rests on another placed object (a wall on its deck) rather than on the ground -
		/// a ray down from the middle of its base, then a ball 0.15 m round: a wall on a deck's very edge (Balboa's relay house:
		/// its walls' pivots on the deck's rim) has its middle a hair outside the deck, and the plain ray fell to the ground
		/// below - Saltpan Delta's and Paradise Archipelago's relay houses on posts read "partly in the air" (2026-11-05).
		/// </summary>
		// A foot over a cave piece and no higher than its roof: in a world it stands on the rock hill CaveMountain drapes
		// over the cave (its skin), which the editor does not build.
		internal static bool UnderCaveHill(Vector3 foot, Collider c, float gap)
		{
			EditorGameObject cave = c != null ? c.GetComponentInParent<EditorGameObject>() : null;
			if (cave == null || !CaveMountain.Wants(cave.GameObjectName ?? "")) return false;
			Renderer[] rs = cave.GetComponentsInChildren<Renderer>(false);
			if (rs.Length == 0) return false;
			Bounds cb = rs[0].bounds;
			foreach (Renderer r in rs) cb.Encapsulate(r.bounds);
			return foot.x >= cb.min.x && foot.x <= cb.max.x && foot.z >= cb.min.z && foot.z <= cb.max.z && foot.y <= cb.max.y + gap;
		}

		internal static bool OnAnotherObject(Bounds b, EditorGameObject e)
		{
			Func<RaycastHit, bool> other = h => h.collider.GetComponent<Terrain>() == null && h.collider.GetComponentInParent<EditorGameObject>() != null &&
				h.collider.GetComponentInParent<EditorGameObject>() != e;
			RaycastHit on;
			if (Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.1f, b.center.z), Vector3.down, out on, 0.6f, ~0, QueryTriggerInteraction.Ignore) && other(on)) return true;
			// (the ball starts 0.35 m up, clear of a deck's top just under the base - a cast misses what it starts in)
			if (Physics.SphereCast(new Vector3(b.center.x, b.min.y + 0.35f, b.center.z), 0.15f, Vector3.down, out on, 0.7f, ~0, QueryTriggerInteraction.Ignore) && other(on)) return true;
			// (a door wall's middle is its doorway - nothing of it there to stand on: its two sides each on what is under
			// them, a third of the way in from its ends - Brinelab Tower's lobby door on the storey under it, w03 11-09)
			bool alongX = b.size.x >= b.size.z;
			float reach = 0.33f * (alongX ? b.size.x : b.size.z);
			if (reach < 0.3f) return false;
			// (each end tried at five points across the base's depth: a door wall's box takes in its open door leaf, so its
			// middle line lies 0.5 m off the wall - Brinelab's lobby door read "partly in the air" over the pit, w03 11-09)
			float depth = alongX ? b.size.z : b.size.x;
			for (int s = -1; s <= 1; s += 2)
			{
				bool held = false;
				for (int k = -2; k <= 2 && !held; k++)
				{
					float across = k * 0.25f * Mathf.Max(0f, depth - 0.2f);
					Vector3 side = new Vector3(b.center.x + (alongX ? s * reach : across), b.min.y + 0.35f, b.center.z + (alongX ? across : s * reach));
					if (Physics.SphereCast(side, 0.15f, Vector3.down, out on, 0.7f, ~0, QueryTriggerInteraction.Ignore) && other(on)) { held = true; break; }
					// (or set into what is there - an arch's end in its rock: a cast that starts inside a rock misses it, a ray from
					// above meets its top at the base's height or over it - Lumen Shelf's arch, w03 11-09)
					Vector3 over = new Vector3(side.x, b.max.y + 1.5f, side.z);
					// (what it is set into reaches down under its base - not the storey over it, whose floor the ray meets first)
					if (Physics.RaycastAll(over, Vector3.down, over.y - (b.min.y - 0.7f), ~0, QueryTriggerInteraction.Ignore).Any(hh => other(hh) && hh.point.y >= b.min.y - 0.7f &&
						hh.collider.bounds.min.y < b.min.y - 0.05f)) held = true;
				}
				if (!held) return false;
			}
			return true;
		}

		// (what is under a lifted plant, for the log: the first thing below its foot that is not itself, and how far down)
		static string UnderIt(EditorGameObject e, Vector3 p)
		{
			foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, p.y + 0.3f, p.z), Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore).OrderBy(x => x.distance))
				if (h.collider != null && h.collider.GetComponentInParent<EditorGameObject>() != e) return " (under it: " + h.collider.name + " " + Num(h.distance - 0.3f) + " m down)";
			return " (under it: nothing in 40 m)";
		}

		/// <summary>The thing's foot or its body's bottom over a cave hill's skin (CaveMountain.SkinName) within 1.1 m (rays from
		/// 0.3 m over them): it stands on the mountain the world drapes over the island's caves - ground to a player, though the
		/// terrain under it lies lower.</summary>
		static bool OnCaveSkin(EditorGameObject e, Vector3 p)
		{
			float bottom = p.y;
			Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled).ToArray();
			if (parts.Length > 0) { Bounds whole = parts[0].bounds; foreach (Renderer r in parts) whole.Encapsulate(r.bounds); bottom = Mathf.Min(bottom, whole.min.y); }
			foreach (Vector3 from in new[] { new Vector3(p.x, p.y + 0.3f, p.z), new Vector3(p.x, bottom + 0.3f, p.z) })
				foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 1.4f, ~0, QueryTriggerInteraction.Ignore))
					if (h.collider != null && h.collider.name == CaveMountain.SkinName) return true;
			return false;
		}

		static bool ShowsAboveGround(Bounds b, Terrain ground)
		{
			float need = Mathf.Min(0.03f, 0.3f * b.size.y);
			for (int i = 0; i <= 4; i++)
				for (int j = 0; j <= 4; j++)
				{
					var p = new Vector3(Mathf.Lerp(b.min.x + b.size.x * 0.15f, b.max.x - b.size.x * 0.15f, i / 4f), 0f, Mathf.Lerp(b.min.z + b.size.z * 0.15f, b.max.z - b.size.z * 0.15f, j / 4f));
					if (b.max.y > ground.SampleHeight(p) + ground.transform.position.y + need) return true;
				}
			return false;
		}

		/// <summary>
		/// line ... follow [y=0.1] [step=]: pieces end to end over the ground under the line - each from the ground (+y) at its
		/// start to the ground at its end, tilted and stretched to reach it (their long side is their x; step: how long each is
		/// along the ground, else its own length) - a pipe or a cable down a cliff. Laid level at their own heights, Stilt
		/// Hollow's red pipe was steps hanging over the reef wall, its down-pipe inside the rock (2026-10-02).
		/// </summary>
		static string LineFollow(string name, float x1, float z1, float x2, float z2, float yaw, Dictionary<string, string> opt, ref int placed)
		{
			Bounds lb;
			GameObject proto = PlaceableCatalog.Get(name);
			if (proto == null || !PlaceableCatalog.LocalBounds(name, out lb)) return "line ... follow: no shape for '" + name + "'";
			float own = Mathf.Max(0.05f, lb.size.x * Mathf.Abs(proto.transform.localScale.x));
			float piece = opt.ContainsKey("step") ? Mathf.Max(0.2f, F(opt["step"])) : own;
			float above = opt.ContainsKey("y") ? F(opt["y"]) : 0.1f;
			float len = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
			if (len < 0.01f) return "line ... follow: the line has no length";
			Vector2 dir = new Vector2(x2 - x1, z2 - z1) / len;
			Func<float, float> ground = d => { Vector2 w = WorldXZ(x1 + dir.x * d, z1 + dir.y * d); return GroundY(w.x, w.y) + above; };
			Func<float, float, float> reach = (a, run) => { float r = ground(a + run) - ground(a); return Mathf.Sqrt(run * run + r * r); };
			float at = 0f;
			for (int k = 0; k < 400 && at < len - 0.01f; k++)
			{
				// (as far along as one piece reaches over the ground there - less down a cliff)
				float run = Mathf.Min(piece, len - at);
				if (reach(at, run) > piece)
				{
					float lo = 0f, hi = run;
					for (int i = 0; i < 24; i++) { float m = (lo + hi) / 2f; if (reach(at, m) > piece) hi = m; else lo = m; }
					run = Mathf.Max(0.02f, lo);
				}
				// (a last bit shorter than a quarter piece: this piece reaches the end instead)
				if (len - (at + run) < piece * 0.25f) run = len - at;
				float ya = ground(at), rise = ground(at + run) - ya;
				var o = new Dictionary<string, string>(opt, StringComparer.OrdinalIgnoreCase);
				o.Remove("step"); o.Remove("follow"); o.Remove("y"); o.Remove("sit"); o.Remove("ends"); o.Remove(SkipBuried);
				o["yaw"] = Num(yaw);
				o["roll"] = Num(Mathf.Atan2(rise, run) * Mathf.Rad2Deg);
				o["sx"] = Num(Mathf.Sqrt(run * run + rise * rise) / own);
				o["h"] = Num(ya - DynamicIslands.EditorWaterLevel);
				EditorGameObject e;
				string error = Place(name, x1 + dir.x * at, z1 + dir.y * at, o, out e);
				if (error != null) return error;
				if (e != null)
				{
					// (its start - the middle of its end face - on the ground at the start of its stretch)
					Vector2 w = WorldXZ(x1 + dir.x * at, z1 + dir.y * at);
					Vector3 start = e.transform.TransformPoint(new Vector3(lb.min.x, lb.center.y, lb.center.z));
					e.transform.position += new Vector3(w.x, ya, w.y) - start;
					placed++;
				}
				at += run;
			}
			return null;
		}

		/// <summary>CB1: a recipe object given an alias is a working piece - it gets the Ready pieces' settings (a cage chained
		/// shut, a usable generator, ...) for keys it does not have yet. One placed without an alias stays decoration.</summary>
		internal static void ReadyDefaults(EditorGameObject e)
		{
			if (e == null) return;
			if (e.Props == null) e.Props = new Dictionary<string, string>();
			var ready = new Dictionary<string, string>();
			ReadyPieces.Defaults(e.GameObjectName, ready);
			Survivors.Defaults(e.GameObjectName, ready);
			if (ready.Count == 0) return;
			foreach (var kv in ready) if (!e.Props.ContainsKey(kv.Key)) e.Props[kv.Key] = kv.Value;
			ObjectProps.ApplyInEditor(e.gameObject, e.Props);
		}

		/// <summary>Places an object as the editor's placer does: the object from the catalog, turned (standing straight,
		/// as with the grid on) and scaled, its collider on, made an editor object with its default settings, shown as one
		/// undo step. "sit": its bottom rests at the height (else its pivot is there). "drop": then set down on whatever is
		/// under it - a porch, a table, a crate - from where it was put.</summary>
		static string Place(string name, float x, float z, Dictionary<string, string> opt, out EditorGameObject result)
		{
			result = null;
			if (!ObjectLimit.Allow(1)) return "the island is full (the object limit)";
			Transform placedRoot = GameObject.Find("PlacedObjects").transform;
			GameObject go = PlaceableCatalog.Spawn(name, placedRoot);
			if (go == null) return "'" + name + "' isn't an object the editor has";
			Vector2 w = WorldXZ(x, z);
			float y = HeightAt(w, opt);
			go.transform.position = new Vector3(w.x, y, w.y);
			float yaw = TopFrame.Yaw + (opt.ContainsKey("yaw") ? F(opt["yaw"]) : 0f);
			// (rot=x,y,z: the object's own turn instead of the one it spawns with - Raft's scene objects keep the lean of the
			// copy found in Raft's scene, e.g. a ladder leaning on a wall; the editor's rotate tool does the same)
			// (as the placer: standing straight - and facing north, its own axes the world's, so pieces fit by their measures)
			Quaternion own = opt.ContainsKey("keep") ? go.transform.rotation : PlacementOptions.Straight(go.transform.rotation, true);
			if (opt.ContainsKey("rot")) { string[] r = opt["rot"].Split(','); own = Quaternion.Euler(F(r[0]), r.Length > 1 ? F(r[1]) : 0f, r.Length > 2 ? F(r[2]) : 0f); }
			go.transform.rotation = Quaternion.Euler(opt.ContainsKey("tilt") ? F(opt["tilt"]) : 0f, yaw, opt.ContainsKey("roll") ? F(opt["roll"]) : 0f) * own;
			if (opt.ContainsKey("scale")) go.transform.localScale = go.transform.localScale * Mathf.Clamp(F(opt["scale"]), 0.05f, 20f);
			// (sx/sy/sz: stretched along one of its own axes, as the editor's scale tool's handles do)
			if (opt.ContainsKey("sx") || opt.ContainsKey("sy") || opt.ContainsKey("sz"))
				go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(opt.ContainsKey("sx") ? Mathf.Clamp(F(opt["sx"]), 0.05f, 20f) : 1f,
					opt.ContainsKey("sy") ? Mathf.Clamp(F(opt["sy"]), 0.05f, 20f) : 1f, opt.ContainsKey("sz") ? Mathf.Clamp(F(opt["sz"]), 0.05f, 20f) : 1f));
			if (opt.ContainsKey("centred"))
			{
				// (its visible middle at the point, not its pivot - some of Raft's scene objects have their pivot tens of metres away)
				Renderer[] rs = PlacementOptions.ShapeRenderers(go);
				if (rs.Length > 0)
				{
					Bounds b = rs[0].bounds;
					foreach (Renderer r in rs) b.Encapsulate(r.bounds);
					go.transform.position += new Vector3(w.x - b.center.x, 0f, w.y - b.center.z);
				}
			}
			// (on the ground, no height given: down to the lowest ground under its base, as the editor's placer and Ground put
			// it - set down by its middle on a slope, a stilt house's or a van's low side stood in the air)
			if (!opt.ContainsKey("h") && !opt.ContainsKey("y") && (!TopFrame.HasFloor || opt.ContainsKey("ground")))
			{
				if (!opt.ContainsKey("sit")) go.transform.position = new Vector3(go.transform.position.x, y, go.transform.position.z);
				// (a tree or a palm: set down by its trunk's foot, the ground just round its pivot - Raft's are one mesh, so their
				// "base" is the whole crown, and on a slope or a coast they went down to the lowest ground under it: Stranded
				// Gull's hill palm grew out of the cliff 4 m down, 1200 trunks on the library islands sunk, the user, 2026-10-30.
				// Over the sea there is no trunk foot to keep: as before)
				Vector3 gp, gn;
				if (Trunk.IsMatch(name) && PlacementOptions.GroundAt(go.transform.position, out gp, out gn) && gp.y > DynamicIslands.EditorWaterLevel)
					y = Mathf.Min(y, PlacementOptions.LowestGroundUnder(new Bounds(go.transform.position, new Vector3(1f, 0f, 1f))));
				else
					y = Mathf.Min(y, PlacementOptions.LowestGroundUnder(go));
				// (as the placer: an object whose pivot is well above its bottom rests on its bottom - RestingPivotY)
				if (!opt.ContainsKey("sit")) go.transform.position = new Vector3(go.transform.position.x, y + PlacementOptions.PivotAboveBottom(go), go.transform.position.z);
			}
			if (opt.ContainsKey("sit"))
			{
				Renderer[] rs = PlacementOptions.ShapeRenderers(go);
				if (rs.Length > 0)
				{
					Bounds b = rs[0].bounds;
					foreach (Renderer r in rs) b.Encapsulate(r.bounds);
					go.transform.position += Vector3.up * (y - b.min.y);
				}
			}
			// ("drop": set down on whatever is under it, from where it was put - the rangers' locker and logbook on Balboa
			// Remade's porch: "sit" at a guessed height left them floating beyond the porch's ends, 2026-10-03)
			if (opt.ContainsKey("drop"))
			{
				Renderer[] rs = PlacementOptions.ShapeRenderers(go);
				if (rs.Length > 0)
				{
					Bounds b = rs[0].bounds;
					foreach (Renderer r in rs) b.Encapsulate(r.bounds);
					// (what the recipe put down a line before is not in the physics scene until it is synced: Varuna Point
					// Remade's log dropped through the table placed for it onto the ground, 2026-10-03)
					Physics.SyncTransforms();
					RaycastHit[] under = Physics.RaycastAll(new Vector3(b.center.x, b.min.y + 0.05f, b.center.z), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore)
						.Where(h => !h.collider.transform.IsChildOf(go.transform)).OrderBy(h => h.distance).ToArray();
					if (under.Length > 0) go.transform.position += Vector3.up * (under[0].point.y - b.min.y);
				}
			}
			bool onGround = !opt.ContainsKey("h") && !opt.ContainsKey("y") && (!TopFrame.HasFloor || opt.ContainsKey("ground"));
			// (set down on the lowest ground under it, a small thing on a steep slope can be under the ground whole - Ranger's
			// Rest's firewood: on the ground at its middle instead)
			if (onGround && BuriedWhole(go))
			{
				Renderer[] rs = PlacementOptions.ShapeRenderers(go);
				Bounds b = rs[0].bounds;
				foreach (Renderer r in rs) b.Encapsulate(r.bounds);
				go.transform.position += Vector3.up * (GroundY(b.center.x, b.center.z) - b.min.y);
			}
			// (an unnamed piece at a set height buried whole - a stilt's lower segment where the sea floor is higher than the
			// recipe guessed, a walkway's planks inside the beach - is left out: nobody sees it, and CIFloating counts it as
			// buried, 2026-10-02)
			else if (!onGround && opt.ContainsKey(SkipBuried) && BuriedWhole(go))
			{
				go.SetActive(false);
				UnityEngine.Object.Destroy(go);
				leftOutBuried++;
				Log("  left out, under the ground: " + name + " at " + Num(x) + " " + Num(z));
				return null;
			}
			Collider ownCollider = go.GetComponent<Collider>();
			if (ownCollider != null) ownCollider.enabled = true;
			// CB1: a recipe object starts as decoration; one given an alias gets the Ready pieces' settings (ReadyDefaults)
			result = EditorGameObject.Attach(go, name, ObjectProps.Defaults(name, false));
			recipePlaced.Add(go);
			CommandUndoRedo.UndoRedoManager.Insert(new ObjectVisibilityCommand(new[] { go }, true));
			return null;
		}

		static int StyleOf(string s)
		{
			switch (s.ToLowerInvariant())
			{
				case "snowy": return TerrainPainter.Snowy;
				case "desert": return TerrainPainter.Desert;
				case "forest": return TerrainPainter.Forest;
				case "volcanic": return TerrainPainter.Volcanic;
				case "tropical": return TerrainPainter.Tropical;
				default: return (int)F(s);
			}
		}

		static float F(string s) { return float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture); }

		// (\\n stays a written \n: a new line inside a journal or dialogue text in an event, whose actions a real
		// line break splits - quest softlock audit 11-23: three islanders' journal pages were cut after their first line)
		static string Unescape(string s) { return (s ?? "").Replace("\\\\n", "\u0001").Replace("\\n", "\n").Replace("\u0001", "\\n"); }

		static Dictionary<string, string> Options(IEnumerable<string> tokens)
		{
			var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string tok in tokens)
			{
				int eq = tok.IndexOf('=');
				if (eq > 0) o[tok.Substring(0, eq)] = tok.Substring(eq + 1); else o[tok] = "";
			}
			return o;
		}

		/// <summary>The line split at spaces ("quoted parts" kept together).</summary>
		static string[] Tokens(string line)
		{
			var list = new List<string>();
			var sb = new StringBuilder();
			bool quoted = false;
			foreach (char c in line)
			{
				if (c == '"') { quoted = !quoted; continue; }
				if (c == ' ' && !quoted) { if (sb.Length > 0) { list.Add(sb.ToString()); sb.Length = 0; } continue; }
				sb.Append(c);
			}
			if (sb.Length > 0) list.Add(sb.ToString());
			return list.Count > 0 ? list.ToArray() : new[] { "" };
		}

		/// <summary>The line after its first n words (spaces kept).</summary>
		static string Rest(string line, int words)
		{
			int i = 0;
			for (int w = 0; w < words; w++)
			{
				while (i < line.Length && line[i] == ' ') i++;
				bool quoted = false;
				while (i < line.Length && (quoted || line[i] != ' ')) { if (line[i] == '"') quoted = !quoted; i++; }
			}
			return i < line.Length ? line.Substring(i).TrimStart() : "";
		}
	}
}
