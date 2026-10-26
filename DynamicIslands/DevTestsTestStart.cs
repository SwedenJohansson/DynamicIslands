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
	/// <summary>The island setting test.start (TestStart.cs): where Test in a world puts the player.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CITestStartUnit", docs: "Dev, anywhere: the test start setting - read from the island settings (missing or bad text = none), kept by an island file's save and load, files without it still load without it")]
		public static void TestStartUnitCommand(string[] args)
		{
			bool ok = true;
			var props = new Dictionary<string, string>();
			Check(ref ok, !TestStart.Get(props).HasValue && !TestStart.Get(null).HasValue, "no setting: no test start");
			props[TestStart.Key] = "not a spot";
			Check(ref ok, !TestStart.Get(props).HasValue, "bad text: no test start");
			props[TestStart.Key] = ZiplineEnds.Text(new Vector3(12.5f, 31f, -4f));
			Vector3? got = TestStart.Get(props);
			Check(ref ok, got.HasValue && (got.Value - new Vector3(12.5f, 31f, -4f)).magnitude < 0.05f, "'" + props[TestStart.Key] + "' reads back as " + got);

			string path = Path.Combine(DynamicIslands.assetpath, "citest-teststart-unit" + IslandFile.Extension);
			try
			{
				IslandFile f = SmallFile("citest-teststart-unit", false);
				f.Props[TestStart.Key] = ZiplineEnds.Text(new Vector3(40f, 25f, 60f));
				f.Save(path);
				Vector3? back = TestStart.Get(IslandFile.Load(path).Props);
				Check(ref ok, back.HasValue && (back.Value - new Vector3(40f, 25f, 60f)).magnitude < 0.05f, "saved and loaded with the island file: " + back);
				f.Props.Remove(TestStart.Key);
				f.Save(path);
				IslandFile g = IslandFile.Load(path);
				Check(ref ok, g != null && !TestStart.Get(g.Props).HasValue, "a file without it loads without it");
			}
			finally { try { File.Delete(path); } catch { } }
			if (ok) Log("PASS: test start unit"); else Fail("test start unit");
		}

		/// <summary>A 200 m square island file: an atoll (a ring of land 10 m high, sea in the middle) or a flat low disc.</summary>
		static IslandFile SmallFile(string name, bool atoll)
		{
			const int res = 129;
			var f = new IslandFile { Name = name, TerrainSize = new Vector3(200f, 60f, 200f), HeightmapResolution = res, Heights = new float[res, res], WaterLevel = IslandFile.DefaultWaterLevel };
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
				{
					float r = new Vector2(x - (res - 1) / 2f, z - (res - 1) / 2f).magnitude * 200f / (res - 1);
					bool land = atoll ? r > 50f && r < 75f : r < 60f;
					f.Heights[z, x] = land ? 30f / 60f : 12f / 60f; // (land 10 m above the sea at 20, sea floor 8 m below it)
				}
			return f;
		}

		[ConsoleCommand(name: "CITestStartEditor", docs: "Dev, editor: the Island tab's 'Test start: where I look' and Clear - a small island is generated, the camera looks at its land, the button sets the spot and shows the marker there; undo and redo, save and load keep it right; Clear takes it away. The test island is deleted after")]
		public static void TestStartEditorCommand(string[] args) { StartTest(TestStartEditorRoutine()); }

		static IEnumerator TestStartEditorRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("test start: in the editor"); yield break; }
			bool ok = true;
			const string name = "citest-teststart";
			DynamicIslands.NewIsland();
			yield return null;
			GeneratorWindow.Open();
			yield return null;
			IslandGenerator.GenerateInEditor(new IslandGenSettings { Seed = 4242, Radius = 60f, Height = 25f, Style = TerrainPainter.Tropical });
			GeneratorWindow.Close();
			yield return null;
			EditorUI.SetTab(TAB.Island);
			yield return null;
			Check(ref ok, !TestStart.Get(DynamicIslands.currentIslandProps).HasValue && TestStart.Marker == null, "a new island has no test start and no marker");

			Terrain t = terraineditor.terrain;
			Vector3 c = t.transform.position + new Vector3(t.terrainData.size.x / 2f, 0f, t.terrainData.size.z / 2f);
			Vector3 aim = c + new Vector3(15f, 0f, 10f);
			aim.y = t.SampleHeight(aim) + t.transform.position.y;
			UnityEngine.UI.Button setButton = IslandTabButton("Button_TestStartHere"), clearButton = IslandTabButton("Button_TestStartClear");
			Check(ref ok, setButton != null && clearButton != null && !clearButton.interactable, "the Island tab has 'Test start: where I look' and Clear (greyed out)");
			Camera cam = Camera.main;
			if (cam != null) { cam.transform.position = aim + new Vector3(0f, 30f, -20f); cam.transform.LookAt(aim); }
			// (what the camera sees there: the ground, or a plant or rock on it)
			Vector3 seen = TestStart.LookPoint() ?? Vector3.zero;
			if (setButton != null) setButton.onClick.Invoke(); // (the same frame: the editor's camera can't move first)
			yield return null;
			Check(ref ok, (seen - aim).magnitude < 3f, "the camera looks at the land (" + seen + ", aimed at " + aim + ")");
			Vector3? local = TestStart.Get(DynamicIslands.currentIslandProps);
			Vector3 expect = seen - t.transform.position;
			Check(ref ok, local.HasValue && (local.Value - expect).magnitude < 0.05f, "the spot looked at is the test start: " + local + " (looked at " + expect + ")");
			GameObject m = TestStart.Marker;
			Check(ref ok, m != null && (m.transform.position - seen).magnitude < 0.05f && m.GetComponentsInChildren<Collider>().Length == 0, "the marker stands there, with no collider");
			Check(ref ok, clearButton != null && clearButton.interactable && m != null && m.transform.parent == null, "Clear can be used; the marker is not a placed object");

			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			Check(ref ok, !TestStart.Get(DynamicIslands.currentIslandProps).HasValue && TestStart.Marker == null, "undo takes it and the marker away");
			CommandUndoRedo.UndoRedoManager.Redo();
			yield return null;
			Check(ref ok, TestStart.Get(DynamicIslands.currentIslandProps).HasValue && TestStart.Marker != null, "redo brings them back");

			Check(ref ok, DynamicIslands.SaveIsland(name), "saved as '" + name + "'");
			DynamicIslands.NewIsland();
			yield return null;
			Check(ref ok, TestStart.Marker == null, "a new island: no marker");
			DynamicIslands.LoadIsland(name);
			yield return null;
			yield return null;
			Vector3? loaded = TestStart.Get(DynamicIslands.currentIslandProps);
			Check(ref ok, loaded.HasValue && (loaded.Value - expect).magnitude < 0.1f && TestStart.Marker != null, "loaded again: the test start " + loaded + " and its marker");

			EditorUI.SetTab(TAB.Island);
			yield return null;
			clearButton = IslandTabButton("Button_TestStartClear");
			if (clearButton != null) clearButton.onClick.Invoke();
			yield return null;
			Check(ref ok, !TestStart.Get(DynamicIslands.currentIslandProps).HasValue && TestStart.Marker == null && !clearButton.interactable, "Clear takes it and the marker away");

			DynamicIslands.SaveIsland(name); // (no unsaved changes left for the autosave)
			try { DynamicIslands.NewIsland(); IslandFilesWindow.MoveToDeleted(name); File.Delete(Path.Combine(Path.Combine(DynamicIslands.assetpath, IslandFilesWindow.DeletedFolderName), name + IslandFile.Extension)); } catch { }
			if (ok) Log("PASS: test start editor"); else Fail("test start editor");
		}

		/// <summary>An editor button by its object name (the Island tab's, built once).</summary>
		static UnityEngine.UI.Button IslandTabButton(string objectName)
		{
			return Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>().FirstOrDefault(b => b.name == objectName && b.gameObject.scene.IsValid());
		}

		[ConsoleCommand(name: "CITestStartWorld", docs: "Dev, in a world (host): where Test puts the player - an atoll (sea in its middle) is spawned ahead of the raft; without a test start the player goes to the land nearest its middle, not the sea floor; with one, on the ground at it. No marker in the world. The island and its file are taken away after")]
		public static void TestStartWorldCommand(string[] args) { StartTest(TestStartWorldRoutine()); }

		static IEnumerator TestStartWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || DynamicIslands.InEditor()) { Fail("test start world: in a world"); yield break; }
			bool ok = true;
			const string name = "citest-atoll";
			string path = IslandSpawner.PathFor(name);
			SmallFile(name, true).Save(path);
			Vector3 pos = raftPos.Value + Vector3.forward * 300f; pos.y = 0f;
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(name, pos, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("the atoll did not spawn"); try { File.Delete(path); } catch { } yield break; }
			yield return new WaitForSeconds(0.5f);
			float sea = e.Position.y;

			RaycastHit hit;
			Vector3? mid = TestStart.GroundBelow(e.Position + Vector3.up * 400f, 800f, e.Root.transform);
			Check(ref ok, mid.HasValue && mid.Value.y < sea, "the atoll's middle is sea floor (" + (mid.HasValue ? mid.Value.y.ToString("0.0") : "nothing") + ", sea " + sea.ToString("0.0") + "): the old Test stood you there");
			string how;
			Vector3 fb = TestStart.StandPoint(e, out how);
			float r = new Vector2(fb.x - e.Position.x, fb.z - e.Position.z).magnitude;
			Check(ref ok, fb.y > sea + 1f && r > 45f && r < 80f && how.Contains("nearest"), "no test start: on the ring's land (" + how + ", " + r.ToString("0") + " m from the middle, " + (fb.y - sea).ToString("0.0") + " m above the sea)");

			Vector3 spotWorld = e.Position + new Vector3(-62f, 0f, 5f);
			Vector3 local = spotWorld - e.Root.transform.position;
			local.y = 30f; // (the ring's top in the island's own coordinates: 10 m above its sea at 20)
			IslandCache.PropsOf(e)[TestStart.Key] = ZiplineEnds.Text(local);
			Vector3 st = TestStart.StandPoint(e, out how);
			float off = new Vector2(st.x - spotWorld.x, st.z - spotWorld.z).magnitude;
			bool ground = Physics.Raycast(st, Vector3.down, out hit, 5f, ~0, QueryTriggerInteraction.Ignore);
			Check(ref ok, how.Contains("test start") && off < 1f && ground && hit.distance < 2.5f && st.y > sea + 1f, "with a test start: on the ground at it (" + how + ", " + off.ToString("0.0") + " m off, " + (ground ? hit.distance.ToString("0.0") : "no") + " m above the ground)");
			Check(ref ok, GameObject.Find(TestStart.MarkerName) == null, "no test start marker in the world");

			IslandWorldState.RemoveIds(new[] { e.Id }, true);
			yield return null;
			try { File.Delete(path); } catch { }
			if (ok) Log("PASS: test start world"); else Fail("test start world");
		}
	}
}
