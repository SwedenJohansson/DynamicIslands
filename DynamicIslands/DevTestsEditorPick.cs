using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>ROADMAP E11 (picking a small object by a big one, the outline under the mouse), E12 (1 m and 2 m terrain brushes),
	/// E13 (the grid size) and L2 (AZERTY keys).</summary>
	public static partial class DevTests
	{
		#region E11: picking

		[ConsoleCommand(name: "CIPickSmall", docs: "Dev, editor (ROADMAP E11): a flower beside a big palm is picked, not the palm; two objects on the same spot: clicking the spot again gives the next one, then the first again; Shift (no cycling) always the first; the outline goes on the object a click would select and comes off")]
		public static void PickSmallCommand() { StartTest(PickSmallRoutine()); }

		static IEnumerator PickSmallRoutine()
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("pick: open the editor first"); yield break; }
			bool ok = true;
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();
			const string Palm = "BigPalm1", Flower = "Pickup_Landmark_Flower_Yellow";
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { Palm, Flower });
			Terrain terrain = terraineditor.terrain;
			Vector3 o = terrain.transform.position;
			Func<float, float, Vector3> onGround = (x, z) => { Vector3 p = o + new Vector3(x, 0f, z); p.y = o.y + terrain.SampleHeight(p); return p; };
			Vector3 at = onGround(500f, 500f);
			EditorGameObject palm = PlaceForTest(Palm, at, placed);
			EditorGameObject flower = PlaceForTest(Flower, onGround(503f, 500f), placed);
			EditorGameObject twin = PlaceForTest(Flower, flower != null ? flower.transform.position : at, placed);
			if (palm == null || flower == null || twin == null) { Fail("pick: could not place " + Palm + " and two " + Flower); yield break; }
			twin.gameObject.SetActive(false); // (the second flower only for the same-spot part)
			Physics.SyncTransforms();
			yield return null;
			LayerMask mask = DynamicIslands.EditorGizmoHandler.selectionMask;
			// (the first run after the editor opened found no candidates at all: the objects' selection colliders come a few
			// frames after placing - up to 3 s for them, 10-26)
			{
				Vector3 c0 = flower.transform.position + Vector3.up * 0.2f;
				Ray probe = new Ray(c0 + new Vector3(6f, 4f, 0f), (c0 - (c0 + new Vector3(6f, 4f, 0f))).normalized);
				for (float w = 0f; w < 3f && ObjectPick.Candidates(probe, mask).Count == 0; w += 0.25f) { yield return new WaitForSeconds(0.25f); Physics.SyncTransforms(); }
			}
			try
			{
				// 1. The flower beside the palm (inside the palm's bounds): the smaller object wins
				Bounds fb = ObjectPick.SizeOf(flower.transform) < 999f ? flower.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds(flower.transform.position, Vector3.one);
				Vector3 aim = fb.center;
				Ray ray = new Ray(aim + new Vector3(6f, 4f, 0f), (aim - (aim + new Vector3(6f, 4f, 0f))).normalized);
				List<ObjectPick.Candidate> here = ObjectPick.Candidates(ray, mask);
				Transform first = ObjectPick.First(ray, mask);
				bool palmCovers = palm.GetComponentsInChildren<Renderer>().Any(r => r.bounds.Contains(aim)) || palm.GetComponentsInChildren<Collider>().Any(c => c.bounds.Contains(aim));
				Check(ref ok, first == flower.transform, "a ray at the flower 3 m from the palm's trunk picks " + (first != null ? first.name : "nothing") + " (the flower; candidates: " + string.Join(", ", here.Select(c => c.Root.name + " " + c.Size.ToString("F1") + " m" + (c.Exact ? " mesh" : " box")).ToArray()) + "; the palm's bounds cover the spot: " + palmCovers + ")");
				Check(ref ok, PlacementOptions.PickObject(ray, mask) == flower.transform, "PlacementOptions.PickObject (the console and tools) gives the same");
				Check(ref ok, ObjectPick.SizeOf(flower.transform) < ObjectPick.SizeOf(palm.transform), "sizes: flower " + ObjectPick.SizeOf(flower.transform).ToString("F1") + " m, palm " + ObjectPick.SizeOf(palm.transform).ToString("F1") + " m");

				// 2. Two objects on one spot: clicking again cycles; Shift/Ctrl (cycle false) stays on the first
				twin.gameObject.SetActive(true);
				Physics.SyncTransforms();
				Vector2 screen = new Vector2(400f, 300f);
				ObjectPick.ForgetClick();
				var got = new List<Transform>();
				for (int i = 0; i < 3; i++)
				{
					Transform t = ObjectPick.Click(ray, mask, screen, true);
					got.Add(t);
					if (t != null) TransformGizmoSelect(t);
				}
				int count = ObjectPick.Candidates(ray, mask).Count;
				Check(ref ok, count >= 2 && got[0] != null && got[1] != null && got[0] != got[1] && (count == 2 ? got[2] == got[0] : got[2] != got[1]),
					"same spot clicked three times (" + count + " objects there): " + string.Join(" > ", got.Select(t => t != null ? t.name + "#" + t.GetInstanceID() : "none").ToArray()) + " (the next each time, then round)");
				Transform shift = ObjectPick.Click(ray, mask, screen, false);
				Check(ref ok, shift == got[0], "Shift/Ctrl + click on the spot: always the first (" + (shift != null ? shift.name : "none") + ")");
				ObjectPick.ForgetClick();
				TransformGizmoSelect(got[1]);
				Check(ref ok, ObjectPick.Click(ray, mask, screen + new Vector2(40f, 0f), true) == got[0], "a click elsewhere starts again at the first");
				twin.gameObject.SetActive(false);
				Physics.SyncTransforms();
				DynamicIslands.EditorGizmoHandler.ClearTargets(false);

				// 3. The outline under the mouse: on the object a click would select, off again; the selection's own stays
				int index, n;
				Transform would = ObjectPick.WouldPick(ray, mask, screen + new Vector2(200f, 0f), out index, out n);
				ObjectPick.SetHover(would);
				Renderer fr = flower.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r is MeshRenderer || r is SkinnedMeshRenderer);
				Renderer pr = palm.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r is MeshRenderer || r is SkinnedMeshRenderer);
				bool on = ObjectPick.Hovered == flower.transform && ObjectPick.Outlined(fr) && !ObjectPick.Outlined(pr);
				ObjectPick.ClearHover();
				bool off = ObjectPick.Hovered == null && !ObjectPick.Outlined(fr);
				Check(ref ok, would == flower.transform && on && off, "hover: a click would select " + (would != null ? would.name : "nothing") + "; the outline goes on it (" + on + ", not on the palm) and comes off (" + off + ")");
			}
			finally
			{
				ObjectPick.ClearHover();
				ObjectPick.ForgetClick();
				if (DynamicIslands.EditorGizmoHandler != null) DynamicIslands.EditorGizmoHandler.ClearTargets(false);
				foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
				CommandUndoRedo.UndoRedoManager.Clear();
			}
			if (ok) Log("PASS: picking small objects (E11)"); else Fail("picking small objects (E11)");
		}

		#endregion

		#region E12: small brushes

		[ConsoleCommand(name: "CIBrushSmall", docs: "Dev, editor (ROADMAP E12): the terrain brush goes down to 1 m across (slider, ChangeWidth); a 1 m Raise moves one ground point, 2 m that point and a little of its neighbours, 4 m more; a 1 m paint changes one paint pixel; the ring is drawn at the brush's size; undo puts it back")]
		public static void BrushSmallCommand() { StartTest(BrushSmallRoutine()); }

		static IEnumerator BrushSmallRoutine()
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("small brush: open the editor first"); yield break; }
			bool ok = true;
			EditorUI.SetTab(TAB.TerrainEdit);
			terraineditor editor = UnityEngine.Object.FindObjectOfType<terraineditor>();
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4243, Radius = 70f, Height = 30f, SeaFloor = IslandGenSettings.SeaFloorShallow, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f });
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			int res = data.heightmapResolution;
			float spacing = data.size.x / (res - 1), sizeY = data.size.y;
			Vector3 o = terrain.transform.position;
			float radius = terraineditor.brushRadius, strength = terraineditor.strength;
			terraineditor.TerrainModificationAction action = terraineditor.modificationAction;
			int paintLayer = terraineditor.paintLayer;
			try
			{
				// 1. The sizes: 1 m to 160 m; the log slider gives whole metres across; ChangeWidth 1 works
				Check(ref ok, Mathf.Approximately(terraineditor.MinRadius * 2f, 1f) && Mathf.Approximately(terraineditor.MaxRadius * 2f, 160f), "brush sizes " + terraineditor.MinRadius * 2f + " to " + terraineditor.MaxRadius * 2f + " m across");
				float[] wantAcross = { 1f, 2f, 3f, 10f, 160f };
				float[] gotAcross = wantAcross.Select(a => EditorUI.SliderRadius(Mathf.Log(a / 2f)) * 2f).ToArray();
				Check(ref ok, gotAcross.Zip(wantAcross, (g, w) => Mathf.Approximately(g, w)).All(b => b) && Mathf.Approximately(EditorUI.SliderRadius(Mathf.Log(terraineditor.MinRadius) - 1f) * 2f, 1f),
					"the Size slider's positions for 1, 2, 3, 10, 160 m give " + string.Join(", ", gotAcross.Select(g => g.ToString("0.#")).ToArray()) + " m");
				float f1 = terraineditor.Footprint(0.5f / spacing), f2 = terraineditor.Footprint(1f / spacing), f4 = terraineditor.Footprint(2f / spacing), f8 = terraineditor.Footprint(4f / spacing), f10 = terraineditor.Footprint(5f / spacing);
				Check(ref ok, f1 < 1f && f2 > 1f && f2 < 1.5f && f4 > f2 && f8 > f4 && Mathf.Abs(f8 - 4f / spacing) < 0.05f && Mathf.Approximately(f10, 5f / spacing),
					"reach in ground points: 1 m " + f1.ToString("F2") + " (the nearest only), 2 m " + f2.ToString("F2") + ", 4 m " + f4.ToString("F2") + ", 8 m " + f8.ToString("F2") + ", 10 m " + f10.ToString("F2") + " (the brush itself)");

				// 2. Raise on the flat sea floor, on a ground point: how many points move
				terraineditor.modificationAction = terraineditor.TerrainModificationAction.Raise;
				terraineditor.strength = 10f;
				int px = Mathf.RoundToInt(150f / spacing), pz = Mathf.RoundToInt(150f / spacing);
				Vector3 point = o + new Vector3(px * spacing, 0f, pz * spacing);
				int[] moved = new int[3];
				float[] centre = new float[3];
				float[] across = { 1f, 2f, 4f };
				for (int k = 0; k < across.Length; k++)
				{
					terraineditor.brushRadius = across[k] / 2f;
					float[,] before = data.GetHeights(px - 6, pz - 6, 13, 13);
					editor.SimulateStroke(point, 10, 0.05f);
					float[,] after = data.GetHeights(px - 6, pz - 6, 13, 13);
					for (int z = 0; z < 13; z++)
						for (int x = 0; x < 13; x++)
							if (Mathf.Abs(after[z, x] - before[z, x]) * sizeY > 0.001f) moved[k]++;
					centre[k] = (after[6, 6] - before[6, 6]) * sizeY;
					CommandUndoRedo.UndoRedoManager.Undo();
					float back = MaxHeightDiff(data.GetHeights(px - 6, pz - 6, 13, 13), before, sizeY);
					Check(ref ok, back < 0.005f, across[k] + " m stroke undone (" + back.ToString("F4") + " m off at most)");
				}
				Check(ref ok, moved[0] == 1 && centre[0] > 0.05f && moved[1] >= 1 && moved[1] <= 9 && centre[1] > 0.05f && moved[2] > moved[1] - 1 && moved[2] <= 25,
					"Raise moves " + moved[0] + " point(s) at 1 m (+" + centre[0].ToString("F2") + " m), " + moved[1] + " at 2 m (+" + centre[1].ToString("F2") + "), " + moved[2] + " at 4 m (+" + centre[2].ToString("F2") + "); the ground has a point every " + spacing.ToString("F2") + " m");

				// 3. Paint at 1 m: one paint pixel
				int aw = data.alphamapWidth;
				float pixel = data.size.x / aw;
				int ax = Mathf.FloorToInt(150f / pixel), az = Mathf.FloorToInt(150f / pixel);
				Vector3 paintAt = o + new Vector3((ax + 0.5f) * pixel, 0f, (az + 0.5f) * pixel);
				terraineditor.modificationAction = terraineditor.TerrainModificationAction.PaintLayer;
				terraineditor.paintLayer = 2;
				terraineditor.brushRadius = 0.5f;
				float[,,] a0 = data.GetAlphamaps(ax - 5, az - 5, 11, 11);
				editor.SimulateStroke(paintAt, 10, 0.05f);
				float[,,] a1 = data.GetAlphamaps(ax - 5, az - 5, 11, 11);
				int changed = 0;
				for (int z = 0; z < 11; z++)
					for (int x = 0; x < 11; x++)
						for (int l = 0; l < a0.GetLength(2); l++)
							if (Mathf.Abs(a1[z, x, l] - a0[z, x, l]) > 0.01f) { changed++; break; }
				CommandUndoRedo.UndoRedoManager.Undo();
				Check(ref ok, changed == 1, "a 1 m paint stroke changes " + changed + " paint pixel(s) (one; a pixel is " + pixel.ToString("F2") + " m)");

				// 4. The ring at the brush's size
				string rings = "";
				bool ringOk = true;
				foreach (float r in new[] { 0.5f, 1f, 20f })
				{
					terraineditor.brushRadius = r;
					BrushCursor.Update(terrain, true, point);
					float w, drawn = BrushCursor.DrawnRadius(out w);
					rings += (r * 2f) + " m: " + (drawn * 2f).ToString("F2") + " m, line " + w.ToString("F2") + "; ";
					if (Mathf.Abs(drawn - r) > 0.02f + r * 0.01f || w > r) ringOk = false;
				}
				BrushCursor.Hide();
				Check(ref ok, ringOk, "the ring matches the brush (" + rings + "its line thinner than the brush)");
			}
			finally
			{
				terraineditor.brushRadius = radius; terraineditor.strength = strength;
				terraineditor.modificationAction = action; terraineditor.paintLayer = paintLayer;
				EditorUI.RefreshSliders();
				CommandUndoRedo.UndoRedoManager.Clear();
			}
			if (ok) Log("PASS: small terrain brushes (E12)"); else Fail("small terrain brushes (E12)");
		}

		#endregion

		#region E13: grid size

		[ConsoleCommand(name: "CIGridSizes", docs: "Dev, editor (ROADMAP E13): the Grid size buttons 0.5 / 1 / 1.5 / 2 m turn Grid on and set the size; placing snaps to it, a gizmo drag puts the object's x/z on it (a drag up leaves them); duplicate steps one grid; the size is saved in world_rules.txt (editorgrid) and read back; world_rules.txt put back")]
		public static void GridSizesCommand() { StartTest(GridSizesRoutine()); }

		static IEnumerator GridSizesRoutine()
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("grid sizes: open the editor first"); yield break; }
			bool ok = true;
			EditorUI.SetTab(TAB.ObjectPlace);
			yield return null;
			string rules = Path.Combine(DynamicIslands.assetpath, WorldRules.DefaultFileName);
			string rulesBefore = File.Exists(rules) ? File.ReadAllText(rules) : null;
			bool snapBefore = PlacementOptions.SnapToGrid;
			try
			{
				Check(ref ok, PlacementOptions.GridSizes.SequenceEqual(new[] { 0.5f, 1f, 1.5f, 2f }) && Mathf.Approximately(PlacementOptions.NearestGrid(0.7f), 0.5f) && Mathf.Approximately(PlacementOptions.NearestGrid(9f), 2f) && Mathf.Approximately(PlacementOptions.GridSize, 1.5f),
					"grid sizes 0.5 / 1 / 1.5 / 2 m (Raft's building grid stays 1.5 m for the generators)");
				Vector3 p = new Vector3(1.3f, 5f, 2.8f);
				foreach (float g in PlacementOptions.GridSizes)
				{
					string gt = g.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
					Button b = EditorUI.Canvas.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == "Button_Grid" + gt);
					PlacementOptions.SnapToGrid = false;
					if (b != null) b.onClick.Invoke(); else EditorUI.ChooseGrid(g);
					Vector3 s = PlacementOptions.Snap(p);
					Vector3 want = new Vector3(Mathf.Round(p.x / g) * g, p.y, Mathf.Round(p.z / g) * g);
					PlacementOptions.ReloadGrid();
					string saved = WorldRules.ReadDefault(PlacementOptions.GridKey);
					// The gizmo's move: dragged 1.1 m along x and 0.5 m up from (0.3, 1, 0.2)
					Vector3 start = new Vector3(0.3f, 1f, 0.2f), raw = new Vector3(1.1f, 0.5f, 0f);
					Vector3 moved = start + RuntimeGizmos.TransformGizmo.GridMove(start, start, raw);
					Vector3 up = start + RuntimeGizmos.TransformGizmo.GridMove(start, start, new Vector3(0f, 2f, 0f));
					Check(ref ok, b != null && PlacementOptions.SnapToGrid && Mathf.Approximately(PlacementOptions.GridStep, g) && (s - want).sqrMagnitude < 1e-6f && saved == gt
						&& Mathf.Abs(moved.x - Mathf.Round(1.4f / g) * g) < 1e-4f && Mathf.Abs(moved.y - 1.5f) < 1e-4f && Mathf.Abs(moved.z - 0.2f) < 1e-4f && (up - new Vector3(0.3f, 3f, 0.2f)).sqrMagnitude < 1e-6f,
						gt + " m button (" + (b != null) + "): Grid on " + PlacementOptions.SnapToGrid + ", placing snaps " + p + " to " + s + ", a drag to x 1.4 lands at " + moved.x.ToString("F2") + " (z stays, y free), a drag up leaves x/z; saved '" + saved + "'");
				}
				EditorUI.ChooseGrid(0.5f);
				Check(ref ok, Mathf.Approximately(PlacementOptions.GridStep, 0.5f), "the chosen size stays until changed (0.5 m)");
			}
			finally
			{
				PlacementOptions.SnapToGrid = snapBefore;
				try { if (rulesBefore != null) File.WriteAllText(rules, rulesBefore); else if (File.Exists(rules)) File.Delete(rules); } catch { }
				PlacementOptions.ReloadGrid();
				EditorUI.RefreshOptions();
			}
			Check(ref ok, rulesBefore == null || File.ReadAllText(rules) == rulesBefore, "world_rules.txt put back as it was");
			if (ok) Log("PASS: grid sizes (E13)"); else Fail("grid sizes (E13)");
		}

		#endregion

		#region L2: AZERTY keys

		[ConsoleCommand(name: "CIKeyLayout", docs: "Dev, editor (ROADMAP L2): the Camera window's Keys button right of Mod's own switches QWERTY (WASD, Q/E) and AZERTY (ZQSD, A/E): the camera's, the stamp's and the placed object's keys, the controls list and the tips; saved in world_rules.txt (camerakeys); Mod's own keeps it; world_rules.txt put back")]
		public static void KeyLayoutCommand() { StartTest(KeyLayoutRoutine()); }

		static IEnumerator KeyLayoutRoutine()
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("key layout: open the editor first"); yield break; }
			bool ok = true;
			string rules = Path.Combine(DynamicIslands.assetpath, WorldRules.DefaultFileName);
			string rulesBefore = File.Exists(rules) ? File.ReadAllText(rules) : null;
			try
			{
				CameraSettings.Azerty = false;
				CameraSettings.Save();
				Check(ref ok, CameraSettings.ForwardKey == KeyCode.W && CameraSettings.LeftKey == KeyCode.A && CameraSettings.DownKey == KeyCode.Q && CameraSettings.UpKey == KeyCode.E && CameraSettings.KeyNames("WASD, Q/E") == "WASD, Q/E",
					"QWERTY: WASD and Q/E");

				// The window's button, right of Mod's own
				CameraSettingsWindow.Open();
				yield return null;
				GameObject w = CameraSettingsWindow.Root;
				Button[] row = w != null ? w.GetComponentsInChildren<Button>(true).Where(b => b.transform.parent != null && b.transform.parent.name == "Buttons").ToArray() : new Button[0];
				int own = Array.FindIndex(row, b => b.GetComponentInChildren<Text>() != null && b.GetComponentInChildren<Text>().text == "Mod's own");
				Button layout = row.FirstOrDefault(b => b.name == "Button_KeyLayout");
				Check(ref ok, layout != null && own >= 0 && own + 1 < row.Length && row[own + 1] == layout && layout.GetComponentInChildren<Text>().text == "Keys: QWERTY",
					"the Keys button stands right of Mod's own (" + string.Join(", ", row.Select(b => b.name).ToArray()) + ")");
				if (layout != null) layout.onClick.Invoke();
				yield return null;
				Text[] keyTexts = w != null ? w.GetComponentsInChildren<Text>(true).Where(t => t.name == "Key").ToArray() : new Text[0];
				Check(ref ok, CameraSettings.Azerty && layout != null && layout.GetComponentInChildren<Text>().text == "Keys: AZERTY"
					&& CameraSettings.ForwardKey == KeyCode.Z && CameraSettings.LeftKey == KeyCode.Q && CameraSettings.BackKey == KeyCode.S && CameraSettings.RightKey == KeyCode.D && CameraSettings.DownKey == KeyCode.A && CameraSettings.UpKey == KeyCode.E,
					"AZERTY after a click: ZQSD move, A/E down / up and turn");
				Check(ref ok, keyTexts.Any(t => t.text == "Right mouse + ZQSD") && keyTexts.Any(t => t.text == "Right mouse + A / E") && !keyTexts.Any(t => t.text.Contains("WASD") || t.text.Contains("Q / E"))
					&& CameraSettingsWindow.ControlGroups.SelectMany(g => g).Contains("ZQSD / arrow keys"),
					"the window's list says ZQSD and A / E (" + string.Join("; ", keyTexts.Where(t => t.text.Contains("ZQSD") || t.text.Contains("A / E")).Select(t => t.text).ToArray()) + ")");
				Text[] tips = EditorUI.Canvas.GetComponentsInChildren<Text>(true).Where(t => t.name == "Tips").ToArray();
				Check(ref ok, tips.Any(t => t.text.Contains("A/E: turn")) && !tips.Any(t => t.text.Contains("WASD") || t.text.Contains("Q/E")),
					"the tabs' tips say A/E and ZQSD (" + tips.Length + " tips)");
				UIKit.HintTarget[] hints = w != null ? w.GetComponentsInChildren<UIKit.HintTarget>(true) : new UIKit.HintTarget[0];
				UIKit.HintTarget speedHint = hints.FirstOrDefault(h => h.Text != null && h.Text.StartsWith("How fast WASD"));
				UIKit.HintTarget layoutHint = layout != null ? layout.GetComponent<UIKit.HintTarget>() : null;
				Check(ref ok, EditorCamera.Help.Contains("ZQSD") && !EditorCamera.Help.Contains("WASD") && speedHint != null && speedHint.Shown.StartsWith("How fast ZQSD (and A/E")
					&& layoutHint != null && layoutHint.Shown == layoutHint.Text,
					"the camera help and the hover hints name ZQSD when shown (" + (speedHint != null ? speedHint.Shown : "no speed hint") + "); the Keys button's hint stays as written");

				// Saved, kept by Mod's own, read back
				CameraSettings.Reset();
				CameraSettings.Reload();
				Check(ref ok, CameraSettings.Azerty && WorldRules.ReadDefault(CameraSettings.KeysKey) == "azerty", "saved as camerakeys=" + WorldRules.ReadDefault(CameraSettings.KeysKey) + "; Mod's own keeps AZERTY and it is read back");
				if (layout != null) layout.onClick.Invoke();
				Check(ref ok, !CameraSettings.Azerty && WorldRules.ReadDefault(CameraSettings.KeysKey) == "qwerty" && keyTexts.Any(t => t.text == "Right mouse + WASD") && EditorCamera.Help.Contains("WASD") && (speedHint == null || speedHint.Shown == speedHint.Text), "a second click: QWERTY again, saved, the list, help and hints say WASD");
			}
			finally
			{
				CameraSettingsWindow.Close();
				CameraSettings.Save();
				try { if (rulesBefore != null) File.WriteAllText(rules, rulesBefore); else if (File.Exists(rules)) File.Delete(rules); } catch { }
				CameraSettings.Reload();
				EditorUI.RefreshKeyNames();
			}
			Check(ref ok, rulesBefore == null || File.ReadAllText(rules) == rulesBefore, "world_rules.txt put back as it was");
			if (ok) Log("PASS: key layout (L2)"); else Fail("key layout (L2)");
		}

		#endregion
	}
}
