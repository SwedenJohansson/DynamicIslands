using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>QE13: beh.laser on any object, set in the Behaviour & events window's Laser row (LaserBeam.cs, BehaviourWindow.LaserRow).</summary>
	public static partial class DevTests
	{
		const string LaserAnyIsland = "cilaserany";
		const string LaserAnyEmitter = "laserany-emitter", LaserAnyMirror = "laserany-mirror", LaserAnyTarget = "laserany-target", LaserAnySignal = "laserany";

		[ConsoleCommand(name: "CILaserAny", docs: "Dev, editor (QE13): three plain logs; the Behaviour & events window's Laser row makes one an emitter (range 12 m) and one a mirror, undo/redo, back to none; saved and loaded back; saves 'cilaserany' for CILaserAnyPlay")]
		public static void LaserAnyCommand() { StartTest(LaserAnyRoutine()); }

		static IEnumerator LaserAnyRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();

			// A flat plateau 3 m above the water, 30 m round the middle (sloping down to 45 m), so the island spawns as land
			Terrain terrain = terraineditor.terrain;
			TerrainData data = terrain.terrainData;
			int res = data.heightmapResolution;
			float[,] heights = data.GetHeights(0, 0, res, res);
			float top = (DynamicIslands.EditorWaterLevel + 3f) / data.size.y, cell = data.size.x / (res - 1);
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
				{
					float d = Mathf.Sqrt((x - res / 2) * (x - res / 2) + (y - res / 2) * (y - res / 2)) * cell;
					if (d < 45f) heights[y, x] = Mathf.Lerp(heights[y, x], top, d < 30f ? 1f : Mathf.SmoothStep(1f, 0f, (d - 30f) / 15f));
				}
			data.SetHeights(0, 0, heights);
			terraineditor.paintMask = null;
			TerrainPainter.Setup(terrain, DynamicIslands.EditorWaterLevel);
			terraineditor.paintMask = new float[data.alphamapResolution, data.alphamapResolution];
			yield return null;
			Vector3 c = terrain.transform.position + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			c.y = terrain.transform.position.y + terrain.SampleHeight(c);

			// Three plain logs (no ready laser piece)
			EditorGameObject em = PlaceForTest("Log", c, placed), mi = PlaceForTest("Log", c + new Vector3(0f, 0f, 6f), placed), ta = PlaceForTest("Log", c + new Vector3(6f, 0f, 6f), placed);
			Check(ref ok, em != null && mi != null && ta != null, "three plain logs placed");
			if (em == null || mi == null || ta == null) { Fail("laser on any object (editor)"); yield break; }
			PropsCommand.Change(em, P(BehaviourProps.Name, LaserAnyEmitter));
			PropsCommand.Change(mi, P(BehaviourProps.Name, LaserAnyMirror));
			PropsCommand.Change(ta, P(BehaviourProps.Name, LaserAnyTarget, BehaviourProps.EventKey(LaserBeam.Event), "signal||" + LaserAnySignal));
			Check(ref ok, BehaviourProps.EventsFor("Log", ta.Props).Any(kv => kv.Key == LaserBeam.Event), "a plain log has the event \"a laser beam reaches it\"");

			// The emitter: the window's Laser row, Shoots a beam, range 12
			BehaviourWindow.Open(em);
			yield return new WaitForSecondsRealtime(0.4f);
			Transform row = EditorUI.Canvas.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Laser" && t.gameObject.activeInHierarchy);
			Check(ref ok, BehaviourWindow.IsOpen && row != null, "the Behaviour & events window has the Laser row");
			BehaviourWindow.ChooseLaser(LaserBeam.Beam);
			yield return null;
			BehaviourWindow.LaserRange("12");
			yield return new WaitForSecondsRealtime(0.3f);
			Screenshot(new[] { "laserany_window" });
			yield return new WaitForSecondsRealtime(0.4f);
			BehaviourWindow.SaveNow();
			yield return null;
			Check(ref ok, ObjectProps.Get(em.Props, LaserBeam.Prop) == LaserBeam.Beam && ObjectProps.Get(em.Props, LaserBeam.RangeProp) == "12" && ObjectProps.Get(em.Props, BehaviourProps.Name) == LaserAnyEmitter,
				"Shoots a beam: beh.laser=beam, beh.laserRange=12, its name kept (" + ObjectProps.Get(em.Props, LaserBeam.Prop) + ", " + ObjectProps.Get(em.Props, LaserBeam.RangeProp) + ")");
			Check(ref ok, BehaviourWindow.Summary(em.Props, "Log").Contains("laser beam (12 m)"), "the summary tells (" + BehaviourWindow.Summary(em.Props, "Log") + ")");
			Check(ref ok, !BehaviourProps.EventsFor("Log", em.Props).Any(kv => kv.Key == LaserBeam.Event), "an emitter has no \"laser reaches it\" event");
			CommandUndoRedo.UndoRedoManager.Undo();
			Check(ref ok, ObjectProps.Get(em.Props, LaserBeam.Prop).Length == 0, "undo takes the laser off");
			CommandUndoRedo.UndoRedoManager.Redo();
			Check(ref ok, ObjectProps.Get(em.Props, LaserBeam.Prop) == LaserBeam.Beam, "redo puts it back");

			// A range out of bounds is kept within it
			BehaviourWindow.Open(em);
			yield return new WaitForSecondsRealtime(0.2f);
			BehaviourWindow.LaserRange("5000");
			string clamped = BehaviourWindow.Unsaved ? "changed" : "same";
			// Back to none in the window: the laser event comes back (no throw), then Cancel
			bool threw = false;
			try { BehaviourWindow.ChooseLaser(""); }
			catch (Exception ex) { threw = true; Log("  ChooseLaser(\"\") threw: " + ex.Message); }
			yield return null;
			Transform group = EditorUI.Canvas.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("a laser beam reaches it") && t.gameObject.activeInHierarchy);
			Check(ref ok, !threw && group != null && clamped == "changed", "No: the window shows the laser event again (" + (group != null ? group.name : "none") + ")");
			BehaviourWindow.Close();
			Check(ref ok, ObjectProps.Get(em.Props, LaserBeam.Prop) == LaserBeam.Beam && ObjectProps.Get(em.Props, LaserBeam.RangeProp) == "12", "Cancel leaves the emitter as it was");
			var probe = new Dictionary<string, string> { { LaserBeam.RangeProp, "5000" } };
			Check(ref ok, Mathf.Approximately(LaserBeam.RangeOf(probe), LaserBeam.MaxRange) && Mathf.Approximately(LaserBeam.RangeOf(new Dictionary<string, string>()), LaserBeam.Range), "range clamped to " + LaserBeam.MaxRange + ", default " + LaserBeam.Range);

			// The mirror
			BehaviourWindow.Open(mi);
			yield return new WaitForSecondsRealtime(0.3f);
			BehaviourWindow.ChooseLaser(LaserBeam.Mirror);
			yield return null;
			BehaviourWindow.SaveNow();
			yield return null;
			Check(ref ok, ObjectProps.Get(mi.Props, LaserBeam.Prop) == LaserBeam.Mirror && ObjectProps.Get(mi.Props, LaserBeam.RangeProp).Length == 0, "Mirror: beh.laser=mirror, no range");

			// Saved and loaded back
			DynamicIslands.SaveIsland(LaserAnyIsland);
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor(LaserAnyIsland));
			Func<IslandFile, string, IslandObject> inFile = (file, n) => file == null ? null : file.Objects.FirstOrDefault(o => ObjectProps.Get(o.Props, BehaviourProps.Name) == n);
			IslandObject fe = inFile(f, LaserAnyEmitter), fm = inFile(f, LaserAnyMirror), ft = inFile(f, LaserAnyTarget);
			Check(ref ok, fe != null && fe.Name == "Log" && ObjectProps.Get(fe.Props, LaserBeam.Prop) == LaserBeam.Beam && ObjectProps.Get(fe.Props, LaserBeam.RangeProp) == "12" &&
				fm != null && ObjectProps.Get(fm.Props, LaserBeam.Prop) == LaserBeam.Mirror && ft != null && ObjectProps.Get(ft.Props, LaserBeam.Prop).Length == 0,
				"saved in the .island: the emitter's and the mirror's laser settings");
			DynamicIslands.LoadIsland(LaserAnyIsland);
			float t0 = Time.realtimeSinceStartup;
			while (Time.realtimeSinceStartup - t0 < 5f && placed.GetComponentsInChildren<EditorGameObject>().Count(x => x.GameObjectName == "Log") < 3) yield return null;
			yield return new WaitForSecondsRealtime(0.3f);
			Func<string, EditorGameObject> named = n => placed.GetComponentsInChildren<EditorGameObject>().FirstOrDefault(x => ObjectProps.Get(x.Props, BehaviourProps.Name) == n);
			EditorGameObject be = named(LaserAnyEmitter), bm = named(LaserAnyMirror);
			Check(ref ok, be != null && ObjectProps.Get(be.Props, LaserBeam.Prop) == LaserBeam.Beam && ObjectProps.Get(be.Props, LaserBeam.RangeProp) == "12" && bm != null && ObjectProps.Get(bm.Props, LaserBeam.Prop) == LaserBeam.Mirror,
				"loaded back with the laser settings");
			if (be != null)
			{
				BehaviourWindow.Open(be);
				yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, BehaviourWindow.IsOpen && !BehaviourWindow.Unsaved, "the window opens the loaded emitter unchanged");
				BehaviourWindow.Close();
			}
			if (ok) Log("PASS: laser on any object (editor)"); else Fail("laser on any object (editor)");
		}

		[ConsoleCommand(name: "CILaserAnyPlay", docs: "Dev, in a world (host, QE13): spawns 'cilaserany' (made by CILaserAny): the plain log emitter has a beam of range 12, lined up in the air its beam bends at the plain log mirror and reaches the target log (event laser -> signal laserany, the line drawn); a target past the range is not reached; removes it")]
		public static void LaserAnyPlayCommand() { StartTest(LaserAnyPlayRoutine()); }

		static IEnumerator LaserAnyPlayRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			if (!File.Exists(IslandSpawner.PathFor(LaserAnyIsland))) { Fail("no island '" + LaserAnyIsland + "': run CILaserAny in the editor first"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Vector3? spot = CustomIslandSpawner.FindClearSpot(CustomIslandSpawner.RaftPosition.Value, CustomIslandSpawner.LandRadius(LaserAnyIsland), 300f);
			if (!spot.HasValue) { Fail("no free spot"); yield break; }
			int count = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(LaserAnyIsland, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(count).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("the island did not spawn"); yield break; }
			try
			{
				yield return new WaitForSeconds(1.5f);
				IslandObjectRef em = ScObjOf(e, LaserAnyEmitter), mi = ScObjOf(e, LaserAnyMirror), ta = ScObjOf(e, LaserAnyTarget);
				LaserBeam beam = em != null ? em.GetComponent<LaserBeam>() : null;
				Check(ref ok, beam != null && mi != null && ta != null && Mathf.Abs(beam.Reach - 12f) < 0.01f, "the plain log emitter has a beam of range 12 (" + (beam != null ? beam.Reach.ToString("F1") : "none") + ")");
				Check(ref ok, mi != null && LaserBeam.IsMirror(mi) && mi.GetComponent<LaserBeam>() == null && ta != null && ta.GetComponent<LaserBeam>() == null, "the mirror log is a mirror, no beam on it or the target");
				if (beam == null || mi == null || ta == null) { Fail("laser on any object in a world"); yield break; }

				// Lined up 4 m in the air (no ground or plants in the way): emitter -> 5 m -> mirror -> 5 m aside -> target
				foreach (IslandObjectRef r0 in new[] { em, mi, ta })
					foreach (Rigidbody rb in r0.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
				Vector3 fwd = em.transform.forward;
				fwd.y = 0f;
				if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
				fwd.Normalize();
				em.transform.rotation = Quaternion.LookRotation(fwd);
				em.transform.position += Vector3.up * 4f;
				Physics.SyncTransforms();
				Vector3 o = beam.Origin, side = Quaternion.Euler(0f, 90f, 0f) * fwd;
				mi.transform.rotation = Quaternion.LookRotation(side);
				mi.transform.position += o + fwd * 5f - StorySets.ModelCentre(mi.gameObject);
				Vector3 mp = mi.transform.position;
				ta.transform.position += new Vector3(mp.x, o.y, mp.z) + side * 5f - StorySets.ModelCentre(ta.gameObject);
				Physics.SyncTransforms();
				beam.Trace();
				Check(ref ok, beam.Mirrors.Contains(mi) && beam.Hit == ta, "the beam bends at the mirror log and reaches the target log (" + LaserPath(beam) + ")");
				yield return new WaitForSeconds(0.6f);
				Check(ref ok, Behaviours.HasSignal(e, LaserAnySignal), "the target's \"laser reaches it\" event ran (signal " + LaserAnySignal + ")");
				LineRenderer line = beam.GetComponentInChildren<LineRenderer>();
				Check(ref ok, line != null && line.gameObject.activeInHierarchy && line.positionCount == 3, "the beam is drawn (" + (line != null ? line.positionCount + " points" : "no line") + ")");
				yield return SetShot(e, new[] { LaserAnyEmitter, LaserAnyMirror, LaserAnyTarget }, 10f, "laserany_world");

				// Past the range: 20 m aside, the beam stops at 12 m
				ta.transform.position += side * 15f;
				Physics.SyncTransforms();
				beam.Trace();
				float last = beam.Points.Count >= 3 ? (beam.Points[beam.Points.Count - 1] - beam.Points[beam.Points.Count - 2]).magnitude : -1f;
				Check(ref ok, beam.Hit != ta && Mathf.Abs(last - 12f) < 0.1f, "a target 20 m past the mirror is out of the 12 m range (last stretch " + last.ToString("F1") + " m)");
			}
			finally { IslandWorldState.RemoveIds(new[] { e.Id }, true); }
			if (ok) Log("PASS: laser on any object in a world"); else Fail("laser on any object in a world");
		}
	}
}
