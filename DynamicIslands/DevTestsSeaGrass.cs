using System.Collections;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Raft's short sea grass on the sea floor (SeaGrass.cs, ROADMAP PF-D3): the brush in the editor, and the grass in a world.</summary>
	public static partial class DevTests
	{
		const string SeaGrassIsland = "citest-seagrass";

		[ConsoleCommand(name: "CISeaGrassEditor", docs: "Dev, editor: the Sea grass brush on a generated island - grass grows on the sea floor, none on land; Shift takes it away; one Ctrl+Z per stroke, redo; saved and loaded with the island (New clears it), Generate clears it (undo brings it back), mirrored with the island. Leaves the island citest-seagrass for CISeaGrassWorld")]
		public static void SeaGrassEditorCommand(string[] args) { StartTest(SeaGrassEditorRoutine()); }

		static IEnumerator SeaGrassEditorRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("sea grass: in the editor"); yield break; }
			var editor = Camera.main.GetComponent<terraineditor>();
			bool ok = true;
			DynamicIslands.NewIsland();
			yield return null;
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4243, Radius = 110f, Height = 25f, Style = TerrainPainter.Tropical });
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();
			Terrain t = terraineditor.terrain;
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorWaterLevel;
			System.Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };
			Check(ref ok, data.detailPrototypes.Length == 1 && data.detailResolution == SeaGrass.Resolution, "the terrain has the grass layer (" + data.detailPrototypes.Length + " layers, " + data.detailResolution + " cells across)");
			Log("  grass look: " + (data.detailPrototypes.Length > 0 && data.detailPrototypes[0].prototypeTexture != null ? data.detailPrototypes[0].prototypeTexture.name : "none") + (SeaGrass.FromRaft ? " (Raft's)" : " (drawn)"));
			Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == 0, "a new island has no grass");

			// (a spot of sea floor east of the island, 2 m or more under the sea, and the land in its middle)
			Vector3 floor = ground(c);
			for (int i = 0; i < 60 && floor.y > sea - 3f; i++) floor = ground(floor + Vector3.right * 4f);
			floor = ground(floor + Vector3.right * 12f);
			Vector3 land = ground(c);
			Check(ref ok, floor.y < sea - 2f && land.y > sea + 3f, "sea floor " + (sea - floor.y).ToString("0.0") + " m under the sea, land " + (land.y - sea).ToString("0.0") + " m above");

			var savedAction = terraineditor.modificationAction;
			float savedRadius = terraineditor.brushRadius, savedStrength = terraineditor.strength;
			terraineditor.modificationAction = terraineditor.TerrainModificationAction.SeaGrass;
			terraineditor.brushRadius = 10f; terraineditor.strength = 20f;
			try
			{
				editor.SimulateStroke(floor, 40, 0.05f);
				int[,] after1 = SeaGrass.Layer(t);
				int grown = SeaGrass.Count(after1);
				int dry = 0;
				for (int z = 0; z < after1.GetLength(0); z++)
					for (int x = 0; x < after1.GetLength(1); x++)
						if (after1[z, x] > 0 && t.SampleHeight(o + new Vector3((x + 0.5f) * data.size.x / data.detailWidth, 0f, (z + 0.5f) * data.size.z / data.detailHeight)) + o.y > sea - SeaGrass.MinDepth + 0.05f) dry++;
				Check(ref ok, grown > 40 && dry == 0 && after1.Cast<int>().Max() <= SeaGrass.MaxDensity, "a stroke on the sea floor grows grass (" + grown + " cells, " + dry + " out of the water)");

				editor.SimulateStroke(land, 40, 0.05f);
				Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == grown, "none on land");

				CommandUndoRedo.UndoRedoManager.Undo(); // (the land stroke: nothing)
				CommandUndoRedo.UndoRedoManager.Undo();
				Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == 0, "Ctrl+Z takes the stroke back");
				CommandUndoRedo.UndoRedoManager.Redo();
				Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == grown, "redo brings it");

				terraineditor.SubtractNoise = true;
				editor.SimulateStroke(floor, 40, 0.05f);
				terraineditor.SubtractNoise = false;
				int left = SeaGrass.Count(SeaGrass.Layer(t));
				Check(ref ok, left < grown / 3, "Shift takes it away (" + left + " of " + grown + " cells left)");
				CommandUndoRedo.UndoRedoManager.Undo();
				Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == grown, "and undo of that brings it back");
				int[,] painted = SeaGrass.Layer(t);

				// Saved and loaded with the island
				Check(ref ok, DynamicIslands.SaveIsland(SeaGrassIsland), "saved as " + SeaGrassIsland);
				IslandFile f = IslandFile.Load(IslandSpawner.PathFor(SeaGrassIsland));
				Check(ref ok, f != null && f.HasGrass && f.GrassResolution == SeaGrass.Resolution && f.GrassCells.Count(b => b > 0) == grown && !f.Tail.ContainsKey(SeaGrass.Tag), "the file holds it (" + (f != null && f.HasGrass ? f.GrassCells.Count(b => b > 0) : 0) + " cells)");
				DynamicIslands.NewIsland();
				yield return null;
				Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == 0, "New clears it");
				DynamicIslands.LoadIsland(SeaGrassIsland);
				yield return null;
				int[,] loaded = SeaGrass.Layer(t);
				Check(ref ok, loaded.Length == painted.Length && loaded.Cast<int>().SequenceEqual(painted.Cast<int>()), "loaded again: the same cells");

				// Mirrored with the island: the cells flipped left to right
				if (f != null && f.HasGrass)
				{
					int r = f.GrassResolution, gx = -1, gz = -1;
					for (int i = 0; i < f.GrassCells.Length && gx < 0; i++) if (f.GrassCells[i] > 0) { gx = i % r; gz = i / r; }
					byte v = f.GrassCells[gz * r + gx];
					MirrorIslands.Mirror(f);
					Check(ref ok, f.GrassCells[gz * r + (r - 1 - gx)] == v, "mirrored: the grass flips with the land");
				}

				// Generate clears it; undo brings it back
				IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4244, Radius = 90f, Height = 20f, Style = TerrainPainter.Tropical });
				yield return null;
				Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == 0, "Generate clears it");
				CommandUndoRedo.UndoRedoManager.Undo();
				yield return null;
				Check(ref ok, SeaGrass.Count(SeaGrass.Layer(t)) == grown, "undo of Generate brings it back");
			}
			finally
			{
				terraineditor.modificationAction = savedAction; terraineditor.brushRadius = savedRadius; terraineditor.strength = savedStrength;
				terraineditor.SubtractNoise = false;
			}

			// Pictures: the grass from just above the sea floor, and from the surface
			yield return CameraShot(floor + new Vector3(-14f, 2.5f, -4f), floor + Vector3.up * 0.3f, "seagrass_editor_low");
			yield return CameraShot(floor + new Vector3(-25f, sea - floor.y + 6f, 0f), floor, "seagrass_editor_high");
			if (ok) Log("PASS: sea grass editor"); else Fail("sea grass editor");
		}

		[ConsoleCommand(name: "CISeaGrassWorld", docs: "Dev, in a world (host): the island CISeaGrassEditor left spawned ahead of the raft - its terrain shows the sea grass (Raft's own short sea grass) on its sea floor only; pictures. The island and its file are taken away after (keep: kept)")]
		public static void SeaGrassWorldCommand(string[] args) { args0Keep = args.Contains("keep"); StartTest(SeaGrassWorldRoutine()); }

		static IEnumerator SeaGrassWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || DynamicIslands.InEditor()) { Fail("sea grass world: in a world"); yield break; }
			string path = IslandSpawner.PathFor(SeaGrassIsland);
			IslandFile f = File.Exists(path) ? IslandFile.Load(path) : null;
			if (f == null || !f.HasGrass) { Fail("sea grass world: run CISeaGrassEditor first (no " + SeaGrassIsland + " with grass)"); yield break; }
			bool ok = true;
			Vector3 pos = raftPos.Value + Vector3.forward * 300f; pos.y = 0f;
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(SeaGrassIsland, pos, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("sea grass world: the island did not spawn"); yield break; }
			yield return new WaitForSeconds(1f);
			Terrain t = e.Root.GetComponentInChildren<Terrain>();
			if (t == null) { Fail("sea grass world: no terrain"); yield break; }
			TerrainData d = t.terrainData;
			int[,] layer = d.detailPrototypes.Length > 0 ? d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, 0) : new int[0, 0];
			int cells = SeaGrass.Count(layer), dry = 0;
			Vector3 o = t.transform.position, first = Vector3.zero;
			float sea = 0f; // (Raft's sea level)
			for (int z = 0; z < layer.GetLength(0); z++)
				for (int x = 0; x < layer.GetLength(1); x++)
				{
					if (layer[z, x] == 0) continue;
					Vector3 p = o + t.transform.rotation * new Vector3((x + 0.5f) * d.size.x / d.detailWidth, 0f, (z + 0.5f) * d.size.z / d.detailHeight);
					p.y = t.SampleHeight(p) + o.y;
					if (p.y > sea - SeaGrass.MinDepth + 0.2f) dry++;
					if (first == Vector3.zero) first = p;
				}
			int saved = f.GrassCells.Count(b => b > 0);
			Check(ref ok, cells > 0 && cells <= saved && dry == 0, "the island's sea floor has grass (" + cells + " of the file's " + saved + " cells, " + dry + " out of the water)");
			string look = d.detailPrototypes.Length > 0 && d.detailPrototypes[0].prototypeTexture != null ? d.detailPrototypes[0].prototypeTexture.name : "none";
			Check(ref ok, look == "Sea_grass", "it is Raft's own short sea grass (" + look + ")");
			Check(ref ok, t.drawTreesAndFoliage && t.detailObjectDistance >= 80f, "drawn (" + t.detailObjectDistance + " m)");
			// The cards must show blades, not the whole card (opaque teal cards seen 10-26): the texture keeps clear pixels.
			Texture2D tex = d.detailPrototypes.Length > 0 ? d.detailPrototypes[0].prototypeTexture : null;
			float clear = SeaGrass.ClearShare(tex);
			Shader grassShader = Shader.Find("Hidden/TerrainEngine/Details/WavingDoublePass");
			Log("  grass texture " + (tex != null ? tex.name + " " + tex.width + "x" + tex.height + " " + tex.format + " readable " + tex.isReadable : "none") + ", clear " + (clear * 100f).ToString("0") + "%, shader " + (grassShader != null ? grassShader.name : "missing"));
			CompareWithRaft(t);
			Check(ref ok, clear > 0.1f && clear < 0.95f, "the grass texture has see-through parts round the blades (" + (clear * 100f).ToString("0") + "% clear)");
			if (cells > 0)
			{
				Vector3 away = (first - e.Position); away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
				yield return CameraShot(first + away * 10f + Vector3.up * 2.5f, first + Vector3.up * 0.3f, "seagrass_world_low");
				yield return CameraShot(first + away * 22f + new Vector3(0f, sea - first.y + 4f, 0f), first, "seagrass_world_high");
			}

			if (args0Keep) { Log("  (island kept: " + SeaGrassIsland + ")"); }
			else
			{
				IslandWorldState.RemoveIds(new[] { e.Id }, true);
				yield return null;
				try { File.Delete(path); } catch { }
			}
			if (ok) Log("PASS: sea grass world"); else Fail("sea grass world");
		}
			/// <summary>(probe) Where the island's terrain differs from a Raft terrain with the same sea grass: settings, its layer.</summary>
		static void CompareWithRaft(Terrain mine)
		{
			Log("  Unity " + Application.unityVersion);
			Terrain raft = Terrain.activeTerrains.Concat(Resources.FindObjectsOfTypeAll<Terrain>()).FirstOrDefault(x => x != mine && x.terrainData != null && x.terrainData.detailPrototypes.Any(p => p.prototypeTexture != null && p.prototypeTexture.name == "Sea_grass"));
			if (raft == null) { Log("  (no Raft terrain with sea grass loaded to compare)"); return; }
			DetailPrototype rp = raft.terrainData.detailPrototypes.First(p => p.prototypeTexture != null && p.prototypeTexture.name == "Sea_grass");
			DetailPrototype mp = mine.terrainData.detailPrototypes.Length > 0 ? mine.terrainData.detailPrototypes[0] : null;
			Log("  compare with Raft's " + raft.name + ": same texture object " + (mp != null && mp.prototypeTexture == rp.prototypeTexture));
			Diff("terrain", raft, mine); Diff("data", raft.terrainData, mine.terrainData); if (mp != null) Diff("layer", rp, mp);
			if (raft.materialTemplate != null || mine.materialTemplate != null) Log("  material " + (raft.materialTemplate != null ? raft.materialTemplate.shader.name : "-") + " / " + (mine.materialTemplate != null ? mine.materialTemplate.shader.name : "-"));
		}

		static void Diff(string what, object a, object b)
		{
			const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
			var names = new System.Collections.Generic.List<string>();
			foreach (var pr in a.GetType().GetProperties(F))
			{
				if (!pr.CanRead || pr.GetIndexParameters().Length > 0) continue;
				System.Type ty = pr.PropertyType;
				if (!(ty.IsPrimitive || ty.IsEnum || ty == typeof(string) || ty == typeof(Color) || ty == typeof(Vector3) || ty == typeof(Vector4) || ty.IsSubclassOf(typeof(Object)))) continue;
				try
				{
					object va = pr.GetValue(a, null), vb = pr.GetValue(b, null);
					string sa = va is Object ? (((Object)va) != null ? ((Object)va).name : "null") : (va is Color ? ((Color)va).ToString("F3") : va + "");
					string sb = vb is Object ? (((Object)vb) != null ? ((Object)vb).name : "null") : (vb is Color ? ((Color)vb).ToString("F3") : vb + "");
					if (sa != sb) names.Add(pr.Name + " " + sa + " / " + sb);
				}
				catch { }
			}
			foreach (var fi in a.GetType().GetFields(F))
			{
				object va = fi.GetValue(a), vb = fi.GetValue(b);
				if ((va + "") != (vb + "")) names.Add(fi.Name + " " + va + " / " + vb);
			}
			Log("  " + what + " differs (Raft / ours): " + (names.Count == 0 ? "nothing" : string.Join("; ", names.ToArray())));
		}
}
}
