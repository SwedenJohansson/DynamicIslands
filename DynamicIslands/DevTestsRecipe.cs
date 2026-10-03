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
		[ConsoleCommand(name: "CIRecipe", docs: "Dev, the editor (opened when needed): builds an island (or a world plan, in World plans) from Mods\\DynamicIslands\\recipes\\<name>.recipe through the editor's operations (generator, terrain strokes, placer, properties, notes, quests, story items, rules, Save) - the library content's builder. CIRecipe <name>")]
		public static void RecipeCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Fail("recipe: name a recipe (Mods\\DynamicIslands\\recipes\\<name>.recipe)"); return; }
			DynamicIslands.instance.StartCoroutine(RecipeRoutine(string.Join(" ", args)));
		}

		public static string RecipeFolder { get { return Path.Combine(DynamicIslands.assetpath, "recipes"); } }

		[ConsoleCommand(name: "CIMeasureObjects", docs: "Dev, main menu or editor: measures every object of an object browser category (or all of them) - size, centre, bottom, colliders - into Mods\\DynamicIslands\\recipes\\objects_<category>.txt (what recipes fit pieces together with). CIMeasureObjects <category|all>")]
		public static void MeasureObjectsCommand(string[] args)
		{
			string want = args != null && args.Length > 0 ? string.Join(" ", args) : "all";
			DynamicIslands.instance.StartCoroutine(MeasureObjects(want));
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
		static readonly Regex NotABase = new Regex("^(?!Placeable_).*(Tree|Bush|Fern|Palm|Plant|Grass|Flower|Kelp|Coral|Rock|Boulder|Stone|Log|Shell|Cave|Tunnel|Vine|Seaweed|Cactus|Reed|Bamboo|Monstera|Drift)", RegexOptions.IgnoreCase);
		/// <summary>What hangs or lies on purpose: a buoy's chain, pipes, cables, lamps, flags; decks, floors and ramps on their
		/// posts; a crane's jib reaching out over the water.</summary>
		static readonly Regex Hangs = new Regex("Buoy|Water|Pipe|Chain|Rope|Cable|Lamp|Light|Flag|Banner|Floor|Deck|Plank|Bridge|Ramp|Stair|Crane|Roof|Scaffold", RegexOptions.IgnoreCase);

		[ConsoleCommand(name: "CIGroundingTest", docs: "Dev, editor: nothing stands in the air on a slope - the generator's rocks and bushes go down to the lowest ground under their base and its snow drifts lie along gentle slopes only; Ground and placing put a wide object down by its base; objects go up and down with the ground a brush stroke changes (one undo step with it)")]
		public static void GroundingTestCommand()
		{
			DynamicIslands.instance.StartCoroutine(GroundingTestRoutine());
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
					if (e.transform.position.y <= low + 0.05f) standing++;
				}
				if (n.StartsWith("TP_SnowDrift"))
				{
					drifts++;
					if (Vector3.Angle(e.transform.up, Vector3.up) <= 20.5f) flatDrifts++;
				}
			}
			Check(ref ok, count > 0 && rocks > 10 && standing >= rocks * 0.97f, "the generator: " + standing + " of " + rocks + " rocks on land stand with all of their base on the ground (" + count + " objects)");
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
			Vector3 at;
			Vector3 up;
			PlacementOptions.GroundAt(van.transform.position, out at, out up);
			Check(ref ok, Mathf.Abs(van.transform.position.y - under) < 0.05f && van.transform.position.y < at.y - 0.1f, "Ground on a " + steepest.ToString("F0") + " degree slope puts the van down by its base: " + (at.y - van.transform.position.y).ToString("F2") + " m below the ground under its middle, at the lowest ground under it");

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
			if (ok) Log("PASS: grounding test"); else Fail("grounding test");
		}

		[ConsoleCommand(name: "CIFloating", docs: "Dev, editor: legs, posts and pillars of the island's objects that don't reach down to anything - a slim upright part whose foot is more than <gap> m (default 0.3) above the ground or what is under it - and objects that stand on the ground with part of their base only (a stilt house set on a slope: the legs over its low side end in the air): CIFloating [gap] [name part]")]
		public static void FloatingCommand(string[] args)
		{
			if (!DynamicIslands.InEditor()) { Fail("CIFloating [gap] (in the editor)"); return; }
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
					found++;
					Log("  floating: " + n + " (" + r.name + ") at " + Num(foot.x - mid.x) + " " + Num(foot.z - mid.y) + ": its foot h=" + Num(foot.y - sea) + " is " + Num(foot.y - support) + " m above " +
						(under && hit.point.y >= land && hit.collider != null ? hit.collider.name : "the ground"));
				}
			}
			// Objects standing on the ground with part of their base only (a stilt house on a slope: the legs over the low side
			// end in the air - its legs are one mesh with the house, so the ground under its whole footprint is measured)
			int bases = 0, partly = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string n = e.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.StartsWith("Loot_") || n.StartsWith("Pickup_") || n.StartsWith("Note_") || NotABase.IsMatch(n) || Hangs.IsMatch(n)) continue;
				Renderer[] rs = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled).ToArray();
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
				RaycastHit on;
				if (Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.1f, b.center.z), Vector3.down, out on, 0.6f, ~0, QueryTriggerInteraction.Ignore) && on.collider.GetComponent<Terrain>() == null &&
					on.collider.GetComponentInParent<EditorGameObject>() != null && on.collider.GetComponentInParent<EditorGameObject>() != e) continue;
				bases++;
				if (highest <= 0.6f) continue;
				partly++;
				Log("  partly in the air: " + n + " at " + Num(b.center.x - mid.x) + " " + Num(b.center.z - mid.y) + ": its base h=" + Num(b.min.y - sea) + " is up to " + Num(highest) + " m above the ground (at " + Num(at.x - mid.x) + " " + Num(at.z - mid.y) + ")");
			}
			// The land's and the sea's plants, rocks and corals (the generator's): their pivot is their foot - none above the
			// ground under it (a stroke that lowered the ground under them, or a slope they were set on by their middle)
			int nature = 0, lifted = 0;
			foreach (EditorGameObject e in PlacedEditorObjects())
			{
				string n = e.GameObjectName ?? "";
				if (!NotABase.IsMatch(n) || ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n.IndexOf("Cave", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Tunnel", StringComparison.OrdinalIgnoreCase) >= 0 || ground == null) continue;
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				nature++;
				Vector3 p = e.transform.position;
				float land = ground.SampleHeight(p) + ground.transform.position.y;
				if (p.y - land <= 0.5f) continue;
				// (set up on purpose: a standing stone reaches down to the ground, a flowerpot stands on a roof, a rockfall's
				// boulder on the one under it - only what hangs over the ground with nothing under it is lifted)
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled).ToArray();
				if (parts.Length > 0)
				{
					Bounds whole = parts[0].bounds;
					foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
					if (whole.min.y - (ground.SampleHeight(whole.center) + ground.transform.position.y) <= 0.5f) continue;
					RaycastHit on;
					if (Physics.Raycast(new Vector3(whole.center.x, whole.min.y + 0.1f, whole.center.z), Vector3.down, out on, 0.8f, ~0, QueryTriggerInteraction.Ignore) && on.collider.GetComponent<Terrain>() == null &&
						on.collider.GetComponentInParent<EditorGameObject>() != null && on.collider.GetComponentInParent<EditorGameObject>() != e) continue;
				}
				lifted++;
				if (lifted <= 30) Log("  above the ground: " + n + " at " + Num(p.x - mid.x) + " " + Num(p.z - mid.y) + ": h=" + Num(p.y - sea) + ", " + Num(p.y - land) + " m above the ground");
			}
			// Objects in the air touching nothing, and objects buried whole under the ground (the user, 2026-10-02: the Stranded
			// Gull's boat was set down by its middle and sank under the beach - its locker and crate hung in the air over it)
			int all = 0, alone = 0, buried = 0;
			List<EditorGameObject> placed = PlacedEditorObjects().ToList();
			foreach (EditorGameObject e in placed)
			{
				string n = e.GameObjectName ?? "";
				if (only.Length > 0 && n.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
				if (ContentCatalog.IsCreature(n) || n.StartsWith("Zone_") || n == ContentCatalog.MarkerOnly || ground == null) continue;
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled).ToArray();
				if (parts.Length == 0) continue;
				Bounds whole = parts[0].bounds;
				foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
				all++;
				float land = ground.SampleHeight(whole.center) + ground.transform.position.y;
				// (things on the water - a buoy, a raft's foundation - float; the sea holds them)
				bool onWater = whole.min.y < sea && whole.max.y > sea - 0.3f;
				if (!onWater && whole.min.y - land > 0.5f)
				{
					Collider[] touching = Physics.OverlapBox(whole.center, whole.extents + Vector3.one * 0.15f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
					// (the terrain counts where the box meets its surface: a beam laid across a gap rests on the rock at its ends)
					if (!touching.Any(c => !c.transform.IsChildOf(e.transform)))
					{
						alone++;
						if (alone <= 30) Log("  in the air, touching nothing: " + n + " at " + Num(whole.center.x - mid.x) + " " + Num(whole.center.z - mid.y) + ": its bottom h=" + Num(whole.min.y - sea) + " is " + Num(whole.min.y - land) + " m above the ground");
					}
				}
				// (buried whole: its top under the ground everywhere over its footprint - a rock sunk into a slope shows on its low
				// side and is meant so - and not inside a cave or a building of other objects)
				if (!ShowsAboveGround(whole, ground) && !n.Contains("Cave") && !n.Contains("Tunnel") &&
					!placed.Any(o => o != e && o.GetComponentsInChildren<Collider>(false).Any(c => !c.isTrigger && c.bounds.Contains(whole.center))))
				{
					buried++;
					if (buried <= 30) Log("  buried under the ground: " + n + " at " + Num(whole.center.x - mid.x) + " " + Num(whole.center.z - mid.y) + ": its top h=" + Num(whole.max.y - sea) + " is " + Num(land - whole.max.y) + " m under the ground");
				}
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
				Renderer[] parts = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled).ToArray();
				if (parts.Length == 0) continue;
				Bounds whole = parts[0].bounds;
				foreach (Renderer r in parts) whole.Encapsulate(r.bounds);
				if (whole.max.y > sea - 0.5f) continue;
				RaycastHit up;
				if (!Physics.Raycast(new Vector3(whole.center.x, whole.max.y + 0.05f, whole.center.z), Vector3.up, out up, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;
				EditorGameObject over = up.collider.GetComponentInParent<EditorGameObject>();
				if (over == null || over == e || up.point.y < sea - 0.3f || up.point.y - whole.max.y < 4f) continue;
				underDeck++;
				if (underDeck <= 20) Log("  on the sea floor under a deck: " + n + " at " + Num(whole.center.x - mid.x) + " " + Num(whole.center.z - mid.y) + ", top h=" + Num(whole.max.y - sea) + " - " + over.GameObjectName + " over it at h=" + Num(up.point.y - sea));
			}
			if (underDeck > 0) Log("  (" + underDeck + " on the sea floor under a deck - meant for the deck? set them down in a frame with its floor)");
			Log((found + partly + lifted + alone + buried == 0 ? "PASS" : "FAIL") + ": floating legs and posts: " + found + " of " + legs + "; standing on part of their base: " + partly + " of " + bases + "; plants and rocks above the ground: " + lifted + " of " + nature +
				"; in the air touching nothing: " + alone + " of " + all + "; buried under the ground: " + buried + " of " + all + "; under a deck: " + underDeck);
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
			DynamicIslands.instance.StartCoroutine(ViewAtShot(args[6], ec));
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
			DynamicIslands.instance.StartCoroutine(ProtoInfoRoutine(names));
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
		static readonly HashSet<GameObject> recipePlaced = new HashSet<GameObject>();
		/// <summary>The quest the recipe is writing (the quest window's fields before Save).</summary>
		static IslandQuest pendingQuest;

		#region Writing recipes: variables, arithmetic, loops, macros, includes

		class RLine { public string Text, Where; }
		class Macro { public List<string> Params; public List<RLine> Body; }

		static System.Random recipeRandom = new System.Random(1);

		static List<RLine> ReadRecipe(string path)
		{
			string[] lines = File.ReadAllLines(path);
			string file = Path.GetFileName(path);
			return lines.Select((l, i) => new RLine { Text = l, Where = file + ":" + (i + 1) }).ToList();
		}

		static List<RLine> ExpandRecipe(string path)
		{
			recipeRandom = new System.Random(Path.GetFileNameWithoutExtension(path).Aggregate(17, (h, c) => h * 31 + c));
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
				while (i < s.Length && char.IsLetter(s[i])) i++;
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
			return GroundY(w.x, w.y) + y;
		}

		#endregion

		static IEnumerator RecipeRoutine(string name)
		{
			string path = Path.Combine(RecipeFolder, name.EndsWith(".recipe") ? name : name + ".recipe");
			if (!File.Exists(path)) { Fail("recipe: no file " + path); yield break; }
			List<RLine> lines;
			try { lines = ExpandRecipe(path); }
			catch (Exception e) { Fail("recipe " + name + ": " + e.Message); yield break; }
			RecipeSaved = null;
			RecipeObjects.Clear();
			recipeGroups.Clear();
			recipePlaced.Clear();
			leftOutBuried = 0;
			pendingQuest = null;
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("recipe " + name + ": the editor didn't open"); yield break; }
			SetOrigin(new Vector2(500f, 500f));
			// (the objects the recipe places, loaded first - Raft's scenes for the on-demand ones)
			var wanted = lines.Select(l => Tokens(l.Text)).Where(t => t.Length > 1 && (t[0] == "place" || t[0] == "scatter" || t[0] == "line" || t[0] == "ramp")).Select(t => t[1]).Where(n => n != "none").Distinct().ToList();
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
				if (verb == "wait") { for (int i = 0; i < Math.Max(1, t.Length > 1 ? (int)F(t[1]) : 1); i++) yield return null; continue; }
				try
				{
					switch (verb)
					{
						case "new":
							pendingQuest = null;
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
							SetOrigin(t.Length > 2 ? new Vector2(F(t[1]), F(t[2])) : EditorLandCentre());
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
							// a trail) removed - deleted as one undo step; "all" also this recipe's own; only=<name pattern> just those whose
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
								.Where(g => (opt.ContainsKey("all") || !recipePlaced.Contains(g)) && DistanceToSegment(new Vector2(g.transform.position.x, g.transform.position.z), w, w2) <= r).ToList();
							if (gone.Count > 0) CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(gone, false));
							break;
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
									List<EditorGameObject> group;
									if (!recipeGroups.TryGetValue(alias, out group)) recipeGroups[alias] = group = new List<EditorGameObject>();
									group.Add(e);
								}
							}
							break;
						}
						case "scatter":
						{
							// scatter <Object> <count> at x z r=<m> [seed=] [scale=a-b] [y=] [wet] [any] [yaw=] (yaw: none = random)
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
								var o = new Dictionary<string, string>(opt, StringComparer.OrdinalIgnoreCase);
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
						case "loot":
						{
							List<EditorGameObject> group = Group(t[1]);
							if (group.Count == 0) { error = "no object '" + t[1] + "' placed by this recipe"; break; }
							foreach (EditorGameObject e in group) PropsCommand.Change(e, ObjectProps.With(e.Props, ObjectProps.LootItems, Rest(line, 2)));
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
							if (pendingQuest == null) pendingQuest = IslandQuest.From(DynamicIslands.currentIslandProps);
							IslandQuest q = pendingQuest;
							if (field == "title") q.Title = value; else if (field == "intro") q.Intro = value; else if (field == "done") q.Done = value; else if (field == "reward") q.Reward = value;
							else { error = "quest: title, intro, done or reward"; break; }
							if (q.Exists) IslandSettingsUndo.Change(() => QuestEditorWindow.Apply(q));
							break;
						}
						case "step":
						{
							// step type|target|count|text
							string[] p = Rest(line, 1).Split('|');
							if (p.Length < 4) { error = "step: type|target|count|text"; break; }
							if (!IslandQuest.Types.Contains(p[0].Trim())) { error = "step: no step type '" + p[0].Trim() + "' (" + string.Join(", ", IslandQuest.Types) + ")"; break; }
							var step = new IslandQuest.Step { Type = p[0].Trim(), Target = p[1].Trim(), Count = Math.Max(1, (int)F(p[2])), Text = Unescape(string.Join("|", p.Skip(3).ToArray())).Trim() };
							if (pendingQuest == null) pendingQuest = IslandQuest.From(DynamicIslands.currentIslandProps);
							IslandQuest sq = pendingQuest;
							sq.Steps.Add(step);
							IslandSettingsUndo.Change(() => QuestEditorWindow.Apply(sq));
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
						case "save":
						{
							if (pendingQuest != null && !pendingQuest.Exists && pendingQuest.Title.Length > 0) { error = "the quest '" + pendingQuest.Title + "' has no steps"; break; }
							string island = Rest(line, 1).Trim();
							string why = FileNames.IslandProblem(island);
							if (why != null) { error = why; break; }
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
							// (and plants, rocks, corals and finds the recipe's own strokes buried whole - a footing flattened over the shelf:
							// Signal Rock's table coral 0.3 m under its platform's sea floor; nothing showed of them, the review 2026-10-03)
							var covered = GameObject.Find("PlacedObjects").transform.GetComponentsInChildren<EditorGameObject>()
								.Where(e => e.gameObject.activeSelf && (NotABase.IsMatch(e.GameObjectName ?? "") || RaftUnderwater.CategoryOf(e.GameObjectName ?? "") != null) &&
									(e.GameObjectName ?? "").IndexOf("Cave", StringComparison.OrdinalIgnoreCase) < 0 && (e.GameObjectName ?? "").IndexOf("Tunnel", StringComparison.OrdinalIgnoreCase) < 0 && BuriedWhole(e.gameObject))
								.Select(e => e.gameObject).ToList();
							if (covered.Count > 0)
							{
								CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(covered, false));
								Log("  " + covered.Count + " plants, rocks, corals or finds under the ground (buried by the recipe's strokes) left out");
							}
							if (!DynamicIslands.SaveIsland(island)) { error = "the island didn't save as '" + island + "'"; break; }
							RecipeSaved = island;
							Log("  saved '" + island + "'");
							// (a world puts an island's land centre - the land above the sea, as saved - where it spawns; the recipe's
							// points are from its origin, the land centre when it was generated: land changed since moves them)
							Vector2 moved = EditorLandCentre() - frames[0].Origin;
							if (moved.magnitude > 0.5f) Log("  the land centre is " + Num(moved.x) + " " + Num(moved.y) + " from the recipe's origin: play tests of '" + island + "' need 'offset " + Num(moved.x) + " " + Num(moved.y) + "'");
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
									Log("  saved plan '" + planName + "' (" + WorldPlan.Load(planName).Rules.Count + " rules)");
									break;
								}
								default: error = "plan new|description|random|story|leaveout|rule|save"; break;
							}
							break;
						}
						case "log":
							Log("  " + Rest(line, 1));
							break;
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
			Log("PASS: recipe " + name + " (" + steps + " steps, " + placed + " objects placed, " + (leftOutBuried > 0 ? leftOutBuried + " left out under the ground, " : "") + (Time.realtimeSinceStartup - started).ToString("F0") + " s" + (RecipeSaved != null ? ", saved '" + RecipeSaved + "'" : "") + ")");
		}

		[ConsoleCommand(name: "CIView", docs: "Dev, editor: pictures of the editor's island without the editor's panels (Mods\\DynamicIslands\\recipes\\view_<name>_<n>.jpg, 1280x720): from four sides and from above, framed on the land - or one view from a point to a point (metres from the island's middle, heights above the sea): CIView <name> [x y z lookx looky lookz]")]
		public static void ViewCommand(string[] args) { DynamicIslands.instance.StartCoroutine(ViewRoutine(args ?? new string[0])); }

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
		static Vector2 EditorLandCentre()
		{
			TerrainData data = terraineditor.terrain.terrainData;
			int res = data.heightmapResolution;
			float[,] h = data.GetHeights(0, 0, res, res);
			float water = (DynamicIslands.EditorWaterLevel - terraineditor.terrain.transform.position.y) / data.size.y;
			double sx = 0, sz = 0; long count = 0;
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
					if (h[y, x] > water) { sx += x; sz += y; count++; }
			Vector3 o = terraineditor.terrain.transform.position;
			if (count == 0) return new Vector2(o.x + data.size.x / 2f, o.z + data.size.z / 2f);
			return new Vector2(o.x + (float)(sx / count) / (res - 1) * data.size.x, o.z + (float)(sz / count) / (res - 1) * data.size.z);
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

		static float GroundY(float x, float z)
		{
			Terrain terrain = terraineditor.terrain;
			return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
		}

		/// <summary>A terrain stroke through the editor's brush: brush raise|lower|flatten|smooth|sample|sand|grass|rock|seabed|auto|stamp:i
		/// at x z (a stroke there) or from x1 z1 to x2 z2 (dragged) [r=radius] [s=strength] [frames=n] [rot=degrees] [h=: flatten to
		/// that height above the sea].</summary>
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
			return null;
		}

		/// <summary>Place's option (set by the place step for an unnamed object, and by line): leave it out if it is under the ground whole.</summary>
		const string SkipBuried = "\u0001skipburied";

		/// <summary>Pieces the recipe left out because they were under the ground whole (this run).</summary>
		static int leftOutBuried;

		/// <summary>Whether an object is under the ground whole (ShowsAboveGround).</summary>
		static bool BuriedWhole(GameObject go)
		{
			Terrain ground = terraineditor.terrain;
			Renderer[] rs = PlacementOptions.ShapeRenderers(go);
			if (ground == null || rs.Length == 0) return false;
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			return !ShowsAboveGround(b, ground);
		}

		/// <summary>
		/// Whether the box around an object's meshes shows above the terrain somewhere over its footprint (5 x 5 points, 15 %
		/// in): its top at least 3 cm over the ground there (a third of a very flat thing's height). A floor laid flush with
		/// the ground shows 4 cm, a flat flower 5 cm; a boulder sunk to 1 cm doesn't.
		/// </summary>
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
			result = EditorGameObject.Attach(go, name);
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

		static string Unescape(string s) { return (s ?? "").Replace("\\n", "\n"); }

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
