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
		[ConsoleCommand(name: "CIPreFaults", docs: "Dev, the editor (opened when needed): a recipe 'brush flatten ... line' stroke along a row of ground points moves that row only (without 'line' the next row moves too); a wall on a deck's edge counts as standing on the deck, a wall in the air does not")]
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
			try { File.Delete(pa); File.Delete(pb); } catch { }
			Log((ok ? "PASS" : "FAIL") + ": prefaults (line strokes, walls on a deck's edge)");
		}
	}
}
