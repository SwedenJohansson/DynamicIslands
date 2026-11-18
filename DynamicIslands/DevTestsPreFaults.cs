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
				// (a radio tower room turned 59 degrees, its roof on its walls - Twin Forges' Anvil, IG5 11-18: the roof's box
				// corners 30 % in lay inside the room, over nothing)
				"include lib_rt",
				"push at -75 -75 h=10 yaw=59",
				"call rt_storey f1=none f2=none",
				"place RT_Floor - at 0 4.5 y=3",
				"pop",
				// (rocks, IG5 11-18: a boulder resting on the ground - its round underside curves up from its middle; one 3 m in
				// the air; one set 0.3 m into the ground)
				// on a bank raised out of the sea (on the sea floor "ground" is the water's top), wide enough that a big rock's
				// edge isn't over its falling side (one stroke r=9: the boulder's quarter points read 95 m over the sea floor)
				"brush flatten from -48 -58 to -12 -58 r=9 s=20 frames=400 h=1.5",
				"brush flatten from -48 -66 to -12 -66 r=9 s=20 frames=400 h=1.5",
				"brush flatten from -48 -74 to -12 -74 r=9 s=20 frames=400 h=1.5",
				"place SmallBoulder2 restrock at -20 -62 ground scale=3",
				"place SmallBoulder2 airrock at -30 -62 ground y=3 scale=1.5",
				"place SmallBoulder2 setrock at -40 -62 ground y=-0.3 scale=1.5",
				// (trees, IG5 11-18: a palm sunk 2 m into the ground and one 2 m over it - both go back on the ground at a save)
				"place BigPalm1 sunkpalm at -20 -70 ground y=-2",
				"place BigPalm1 airpalm at -32 -70 ground y=2",
				// (a grass tuft 1 m over the ground - the generator's, over a pit a stroke dug: Firedance Isle, IG5 11-18)
				"place CaravanIsland_YellowGrass airgrass at -42 -70 ground y=1",
				// (a palm the test moves onto the bank's steep south rim - the reseat moves it off: "trunk over a drop", IG5 11-18)
				"place BigPalm1 rimpalm at -36 -70 ground",
				// (a zipline moved by its near end's floor onto a spot - its pivot is elsewhere: summit ziplines hung off their towers, IG5 11-18)
				"place ZiplinePath_Landmark pfzip at -14 -62 ground",
				"place SmallRock_02 pfzipmark at -16 -68 ground scale=0.3",
				"zipfrom pfzip -16 -68 ground",
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
			// (the turned room's roof is held by its walls; its own floor, 10 m up on nothing, is not)
			var turned = FloorsOverNothing(PlacedEditorObjects(), 0.3f, t, sea, out nFloors).Where(g => g.Floor.GameObjectName == "RT_Floor").ToList();
			Check(ref ok, turned.Count == 1 && turned[0].Box.min.y < sea + 11f, "a room turned 59 degrees: its roof on its walls is held, its floor in the air is not (" +
				string.Join(", ", turned.Select(g => "h=" + (g.Box.min.y - sea).ToString("0.0")).ToArray()) + ")");
			// (the rocks: the resting boulder and the set-in one are not in the air, the one 3 m up is; set in, its middle's
			// gap is 0 - the ray up from the ground starts inside it - not "not measured", which let one corner over a dip
			// make a sunk rock "floating, 1 of 1 points")
			Physics.SyncTransforms();
			float bank = GroundY(origin.x - 30f, origin.z - 66f) - sea;
			Check(ref ok, bank > 0.5f, "the bank for the rocks and palms is out of the sea (h=" + bank.ToString("0.00") + ")");
			foreach (string rn in new[] { "restrock", "airrock", "setrock" })
			{
				EditorGameObject r; RecipeObjects.TryGetValue(rn, out r);
				if (r == null) { Check(ref ok, false, "rock '" + rn + "' placed"); continue; }
				Renderer[] rrs = r.GetComponentsInChildren<Renderer>(false).Where(x => x is MeshRenderer && x.enabled).ToArray();
				Bounds rb = rrs[0].bounds;
				foreach (Renderer x in rrs) rb.Encapsulate(x.bounds);
				int seen, high; float most, least;
				bool floats = RockFloats(r.transform, rb, t, out seen, out high, out most, out least);
				float mid = RockGap(r.transform, rb.center.x, rb.center.z, rb, t);
				string got = rn + ": " + high + " of " + seen + " points high (up to " + most.ToString("0.00") + " m), the least gap " + least.ToString("0.00") + ", its middle's " + mid.ToString("0.00")
					+ ", under its middle: " + string.Join(", ", Physics.RaycastAll(rb.center, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore)
						.Where(hh => !hh.collider.transform.IsChildOf(r.transform)).OrderBy(hh => hh.distance).Take(2)
						.Select(hh => hh.collider.name + "@" + (hh.point.y - sea).ToString("0.00")).ToArray()) + " (rock h " + (rb.min.y - sea).ToString("0.00") + ")";
				if (rn == "airrock") Check(ref ok, floats, "a rock 3 m in the air floats (" + got + ")");
				else Check(ref ok, !floats, "a rock " + (rn == "restrock" ? "resting on the ground" : "set into the ground") + " does not float (" + got + ")");
				if (rn == "setrock") Check(ref ok, mid == 0f, "a rock set into the ground: its middle's gap is 0 (" + got + ")");
			}
			// (the rim palm onto the first spot south of it on the bank's rim)
			EditorGameObject rp; RecipeObjects.TryGetValue("rimpalm", out rp);
			Vector3? rimAt = null;
			if (rp != null)
			{
				Vector3 r0 = rp.transform.position;
				float rrt = 0.6f * Mathf.Max(0.5f, rp.transform.lossyScale.x);
				for (float dz = 0f; dz < 40f && !rimAt.HasValue; dz += 0.1f)
					if (GroundY(r0.x, r0.z - dz) > sea + 0.3f && OnDropRim(r0.x, r0.z - dz, rrt)) rimAt = new Vector3(r0.x, GroundY(r0.x, r0.z - dz) + PlacementOptions.PivotAboveBottom(rp.gameObject), r0.z - dz);
				if (rimAt.HasValue) rp.transform.position = rimAt.Value;
			}
			Check(ref ok, rimAt.HasValue, "the rim palm put on the bank's rim");
			// (the save's reseat sets the sunk and the floating palm back on the ground, and the rim palm off the rim)
			ReseatSunkTrees(sea);
			Physics.SyncTransforms();
			if (rimAt.HasValue)
			{
				Vector3 rq = rp.transform.position;
				float moved = new Vector2(rq.x - rimAt.Value.x, rq.z - rimAt.Value.z).magnitude, roff = rq.y - PlacementOptions.PivotAboveBottom(rp.gameObject) - GroundY(rq.x, rq.z);
				Check(ref ok, moved > 0.1f && moved < 3.1f && !OnDropRim(rq.x, rq.z, 0.6f * Mathf.Max(0.5f, rp.transform.lossyScale.x)) && Mathf.Abs(roff) < 0.5f,
					"reseat: the palm on the rim moved off it (" + moved.ToString("0.00") + " m, its foot " + roff.ToString("0.00") + " m off the ground)");
			}
			foreach (string pn in new[] { "sunkpalm", "airpalm", "airgrass" })
			{
				EditorGameObject pe; RecipeObjects.TryGetValue(pn, out pe);
				if (pe == null) { Check(ref ok, false, "'" + pn + "' placed"); continue; }
				Vector3 pp = pe.transform.position;
				float gy = GroundY(pp.x, pp.z), off = pp.y - PlacementOptions.PivotAboveBottom(pe.gameObject) - gy;
				Check(ref ok, Mathf.Abs(off) < 0.5f, "reseat: the " + (pn == "sunkpalm" ? "sunk palm" : pn == "airpalm" ? "floating palm" : "floating grass tuft") + "'s foot is on the ground (" + off.ToString("0.00") + " m off)");
			}
			// (zipfrom put the zipline's near end's floor on the marked spot)
			{
				EditorGameObject ze, zm; RecipeObjects.TryGetValue("pfzip", out ze); RecipeObjects.TryGetValue("pfzipmark", out zm);
				Vector3? nf = ze != null ? ZiplineEnds.NearFloor(ze.gameObject) : null;
				if (!nf.HasValue || zm == null) Check(ref ok, false, "zipfrom: the zipline and its marker placed");
				else
				{
					Vector3 m = zm.transform.position;
					float dxz = new Vector2(nf.Value.x - m.x, nf.Value.z - m.z).magnitude, dy = nf.Value.y - GroundY(m.x, m.z);
					Check(ref ok, dxz < 0.1f && Mathf.Abs(dy) < 0.1f, "zipfrom: the zipline's near floor is on the spot (" + dxz.ToString("0.00") + " m off it, " + dy.ToString("0.00") + " m over the ground)");
				}
			}
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
