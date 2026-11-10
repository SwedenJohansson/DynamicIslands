using System;
using System.Collections;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The two tool faults behind the 11-05 library findings: a recipe's "line" stroke moves only the row of ground points it
	/// is drawn along (Crater Lair's hangar), and CIFloating takes a wall standing on a deck's very edge as standing on the
	/// deck (Saltpan Delta's and Paradise Archipelago's relay houses on posts).
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIPreFaults", docs: "Dev, the editor (opened when needed): a recipe 'brush flatten ... line' stroke along a row of ground points moves that row only (without 'line' the next row moves too); a wall on a deck's edge counts as standing on the deck, a wall in the air does not; a deck on nothing under the real sea of an island sunk by elevation is not checked; a gate's leaves on its own landing are not floating")]
		public static void PreFaultsCommand(string[] args) { StartTest(PreFaultsRoutine()); }

		static IEnumerator PreFaultsRoutine()
		{
			bool ok = true;
			string pa = Path.Combine(RecipeFolder, "ci_prefaults_a.recipe"), pb = Path.Combine(RecipeFolder, "ci_prefaults_b.recipe");
			try
			{
				Directory.CreateDirectory(RecipeFolder);
				File.WriteAllLines(pa, new[] { "# CIPreFaults: the island", "new", "gen Seed=4242 Radius=60 Height=5 Style=0", "generate" });
			}
			catch (Exception e) { Fail("prefaults: writing " + pa + ": " + e.Message); yield break; }
			yield return RecipeRoutine("ci_prefaults_a");
			yield return new WaitForSecondsRealtime(0.6f);
			Terrain t = terraineditor.terrain;
			if (t == null) { Fail("prefaults: no island after the first recipe"); yield break; }
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position;
			float spacing = data.size.x / (data.heightmapResolution - 1), sea = DynamicIslands.EditorWaterLevel;
			// (out on the flat sea floor, 200 m west of the middle, on a ground point: the strokes run along rows i = row and row + 6)
			int col = Mathf.RoundToInt((data.size.x / 2f - 200f) / spacing), row = Mathf.RoundToInt(data.size.z / 2f / spacing);
			Vector3 origin = o + new Vector3(col * spacing, 0f, row * spacing);
			Func<int, int, float> h = (dx, dz) => data.GetHeight(col + dx, row + dz) + o.y - sea;
			float floor0 = h(4, 1), floor6 = h(4, 7);
			string inv(float f) { return f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
			float far = 8f * spacing, zb = 6f * spacing;
			try
			{
				File.WriteAllLines(pb, new[]
				{
					"# CIPreFaults: a line stroke, a plain one, a relay deck with a wall on its north edge, a wall in the air",
					"origin " + inv(origin.x) + " " + inv(origin.z),
					"brush flatten from 0 0 to " + inv(far) + " 0 r=1.9 s=20 frames=120 h=-2 line",
					"brush flatten from 0 " + inv(zb) + " to " + inv(far) + " " + inv(zb) + " r=1.9 s=20 frames=120 h=-2",
					"place Relay_Floor_deck - at -20 -20 h=1.852",
					"place Relay_Wall - at -20 -20 h=2",
					"place Relay_Wall - at -40 -20 h=2",
					"place BalboaCave_Bear - at 30 -40 h=-2",
					"place Block_Foundation sunkdeck at -60 -40 h=10",
					"brush flatten at -40 40 r=8 s=20 frames=200 h=-3",
					"place UT_MainEntranceDoor - at -40 40 ground sit",
					// (a planter on a storey's floor: a foundation set 0.68 m over the floor's height, its underside a hair into
					// the thin floor tiles - Brinelab's greenhouse, w03 11-09)
					"place Block_Floor_Tier3 - at -60.75 39.25 h=5",
					"place Block_Floor_Tier3 - at -59.25 39.25 h=5",
					"place Block_Floor_Tier3 - at -60.75 40.75 h=5",
					"place Block_Floor_Tier3 - at -59.25 40.75 h=5",
					"place Block_Foundation - at -60 40 h=5.68",
					// (a mast bridge's deck on two poles with no collider, over nothing else - Caisson Yard, w03 11-09)
					"place Antenna_pole - at -82 -40.55 h=9.55 rot=0,0,-90 sy=0.75",
					"place Antenna_pole - at -82 -39.45 h=9.55 rot=0,0,-90 sy=0.75",
					"place Block_Floor_Tier3 - at -79.5 -40 h=10",
				// (a door wall on the wall under it, in the air: its doorway over the wall's top - Brinelab Tower's lobby, w03 11-09)
				"place RT_Wall1 - at -100 -40 h=10",
				"place RT_WallDoor1 - at -100 -40 h=13",
				// (and the storey's floor tiles beside it, their edge under its pivot line, as in the lobby)
				"place Block_Floor_Tier3 - at -99.25 -40.75 h=13",
				"place Block_Floor_Tier3 - at -97.75 -40.75 h=13",
				// (an air pocket half over the gate's pit slope: a bubble in the water, not a thing on a base - Lumen Shelf, w03 11-09)
				"place AirPocket_1 - at -32.5 40 h=-1.5",
				// (an arch: a deck over a gap, its ends set into two big rocks - Lumen Shelf, w03 11-09)
				"place SmallRock_02 - at -100 -60 h=10 scale=2",
				"place SmallRock_02 - at -95 -60 h=10 scale=2",
				"place Block_Foundation archdeck at -97.5 -60 h=11.6 sx=3.2 sz=1.6",
				});
			}
			catch (Exception e) { Fail("prefaults: writing " + pb + ": " + e.Message); yield break; }
			yield return RecipeRoutine("ci_prefaults_b");
			yield return new WaitForSecondsRealtime(0.3f);

			// 1. The line stroke: its row at -2, the rows beside it as they were; the plain stroke pulls its neighbours too
			float onLine = h(4, 0), besideLine = h(4, 1), plain = h(4, 6), besidePlain = h(4, 7);
			Check(ref ok, Mathf.Abs(onLine + 2f) < 0.15f && Mathf.Abs(besideLine - floor0) < 0.02f * Mathf.Abs(floor0 + 2f),
				"line stroke: its row at h=" + onLine.ToString("0.00") + " (wanted -2), the next row " + besideLine.ToString("0.00") + " (was " + floor0.ToString("0.00") + ": under 2 % of the way)");
			Check(ref ok, Mathf.Abs(plain + 2f) < 0.15f && Mathf.Abs(besidePlain - floor6) > 0.3f * Mathf.Abs(floor6 + 2f),
				"plain stroke (the eased reach, unchanged): its row at h=" + plain.ToString("0.00") + ", the next row moved from " + floor6.ToString("0.00") + " to " + besidePlain.ToString("0.00"));

			// 2. The walls: the one on the deck's north edge stands on it, the lone one does not
			var walls = PlacedEditorObjects().Where(e => e.GameObjectName == "Relay_Wall").ToArray();
			Check(ref ok, walls.Length == 2, "two walls placed (" + walls.Length + ")");
			if (walls.Length == 2)
			{
				Func<EditorGameObject, Bounds> baseOf = e =>
				{
					Renderer[] rs = e.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
					float bottom = rs.Min(r => r.bounds.min.y);
					rs = rs.Where(r => r.bounds.min.y < bottom + 0.6f).ToArray();
					Bounds b = rs[0].bounds;
					foreach (Renderer r in rs) b.Encapsulate(r.bounds);
					return b;
				};
				var byX = walls.OrderByDescending(w => w.transform.position.x).ToArray();
				Bounds onDeck = baseOf(byX[0]), alone = baseOf(byX[1]);
				Check(ref ok, OnAnotherObject(onDeck, byX[0]), "the wall on the deck's edge stands on the deck (its base h=" + (onDeck.min.y - sea).ToString("0.00") + ", middle at z " + (onDeck.center.z - origin.z).ToString("0.000") + ")");
				Check(ref ok, !OnAnotherObject(alone, byX[1]), "the wall in the air stands on nothing");
				EditorGameObject den = PlacedEditorObjects().FirstOrDefault(e => e.GameObjectName == "BalboaCave_Bear");
				Check(ref ok, den != null, "a cave piece placed");
				if (den != null)
				{
					Collider dc = den.GetComponentInChildren<Collider>();
					Bounds db = den.GetComponentInChildren<Renderer>().bounds;
					foreach (Renderer r in den.GetComponentsInChildren<Renderer>()) db.Encapsulate(r.bounds);
					Check(ref ok, UnderCaveHill(new Vector3(db.center.x, db.max.y - 1f, db.center.z), dc, 0.3f), "a foot 1 m under a cave's top stands on its hill");
					Check(ref ok, !UnderCaveHill(new Vector3(db.center.x, db.max.y + 2f, db.center.z), dc, 0.3f), "a foot 2 m over its top does not");
					Check(ref ok, !UnderCaveHill(new Vector3(db.max.x + 2f, db.max.y - 1f, db.center.z), dc, 0.3f), "nor one beside it");
				}
			}
			// 3. A deck 10 m over the editor's water line, on nothing: over nothing on an island at the sea, under the real sea
			// (and so not checked) on one sunk by 'elevation -45' - Hadal Commons' air platforms
			float wasElevation = DynamicIslands.currentElevation;
			int nFloors;
			Func<bool> deckFlagged = () => FloorsOverNothing(PlacedEditorObjects(), 0.3f, t, sea, out nFloors).Any(g => g.Floor.GameObjectName == "Block_Foundation" && g.Box.max.y > sea + 8f);
			DynamicIslands.currentElevation = 0f;
			bool atSea = deckFlagged();
			DynamicIslands.currentElevation = -45f;
			bool sunk = deckFlagged();
			DynamicIslands.currentElevation = wasElevation;
			Check(ref ok, atSea, "a deck 10 m over the water on nothing is over nothing at elevation 0");
			Check(ref ok, !sunk, "the same deck at elevation -45 is under the real sea and not checked");
			// (the planter: held by the floor tiles under it, at elevation 0 too)
			bool planterOver = FloorsOverNothing(PlacedEditorObjects(), 0.3f, t, sea, out nFloors).Any(g => g.Floor.GameObjectName == "Block_Foundation" && g.Box.max.y < sea + 8f);
			Check(ref ok, !planterOver, "a foundation set on a storey's thin floor tiles is held by them (a planter, Brinelab)");
			bool railOver = FloorsOverNothing(PlacedEditorObjects(), 0.3f, t, sea, out nFloors).Any(g => g.Floor.GameObjectName == "Block_Floor_Tier3" && g.Box.center.x < origin.x - 70f);
			Check(ref ok, PlacedEditorObjects().Count(e => e.GameObjectName == "Antenna_pole") == 2 && !railOver, "a deck on two poles laid under it (no collider) is held by them (Caisson Yard's mast bridges)");
			// (the door wall stands on the wall under it, though its middle is its doorway)
			EditorGameObject dw = PlacedEditorObjects().FirstOrDefault(e => e.GameObjectName == "RT_WallDoor1");
			Check(ref ok, dw != null, "a door wall placed");
			if (dw != null)
			{
				Renderer[] drs = dw.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled && r.name != ContentCatalog.MarkerOnly).ToArray();
				float dbot = drs.Min(r => r.bounds.min.y);
				Bounds dbase = drs[0].bounds; bool first = true;
				foreach (Renderer r in drs.Where(r => r.bounds.min.y < dbot + 0.6f)) { if (first) { dbase = r.bounds; first = false; } else dbase.Encapsulate(r.bounds); }
				RaycastHit under;
				string what = Physics.Raycast(new Vector3(dbase.center.x, dbase.min.y + 0.1f, dbase.center.z), Vector3.down, out under, 4f, ~0, QueryTriggerInteraction.Ignore)
					? under.collider.name + " " + (under.distance - 0.1f).ToString("0.00") + " m under it" : "nothing within 4 m under its middle";
				Check(ref ok, OnAnotherObject(dbase, dw), "a door wall on the wall under it stands on it (its middle is its doorway; base h=" + (dbase.min.y - sea).ToString("0.00") +
					", size " + dbase.size.x.ToString("0.0") + " x " + dbase.size.z.ToString("0.0") + ", " + what + ")");
			}
			// (the arch's deck: its middle over the gap, its ends in the rocks)
			EditorGameObject arch = PlacedEditorObjects().FirstOrDefault(e => e.GameObjectName == "Block_Foundation" && e.transform.position.x < origin.x - 90f);
			Check(ref ok, arch != null, "an arch deck placed");
			if (arch != null)
			{
				Bounds ab = arch.GetComponentsInChildren<Renderer>(false).Where(r => r is MeshRenderer && r.enabled).Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
				Check(ref ok, OnAnotherObject(ab, arch), "an arch's deck with its ends in two rocks stands on them (its base h=" + (ab.min.y - sea).ToString("0.00") + ")");
			}
			// 4. A gate on flat ground: its leaves stand on its own landing (no collider in the editor), not in the air
			FloatingCommand(new[] { "0.3", "UT_MainEntranceDoor" });
			Check(ref ok, floatProblems == 0, "a gate's leaves on its own landing are not floating (" + floatProblems + " found)");
			FloatingCommand(new[] { "0.3", "AirPocket" });
			Check(ref ok, floatProblems == 0, "an air pocket over a slope is not a base partly in the air (" + floatProblems + " found)");
			try { File.Delete(pa); File.Delete(pb); } catch { }
			Log((ok ? "PASS" : "FAIL") + ": prefaults (line strokes, walls on a deck's edge, sunk decks, gate leaves)");
		}
	}
}
