using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DynamicIslands
{
	/// <summary>Tests of the player features PF1 (own notebook), PF4 (camera fly-throughs) and PF5 (minimap and pings).</summary>
	public static partial class DevTests
	{
		#region PF1 player notebook

		const string NotebookTestTitle = "CI notebook ";

		[ConsoleCommand(name: "CINotebook", docs: "Dev, world 'CI ...' (PF1): the player's own notebook. CINotebook unit - the file format round trip, the window (typing, new page, player held, saved on close, the file next to the world's save), copying a note in / write - adds a page 'CI notebook <world>' and saves / check - after loading the world again: its page is back, no other world's page / empty - a world with no 'CI notebook' page (another world's are not here)")]
		public static void NotebookCommand(string[] args) { StartTest(NotebookRoutine(args != null && args.Length > 0 ? args[0] : "unit")); }

		static IEnumerator NotebookRoutine(string what)
		{
			string world = SaveAndLoad.CurrentGameFileName ?? "";
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor() || !world.StartsWith("CI ")) { Fail("notebook: in a test world 'CI ...'"); yield break; }
			// (the notebook loads a frame after the world's id is known)
			float wait = Time.unscaledTime + 10f;
			while (PlayerNotebook.LoadedFor != SaveAndLoad.WorldGuid.ToString() && Time.unscaledTime < wait) yield return null;
			bool ok = true;
			Check(ref ok, PlayerNotebook.LoadedFor == SaveAndLoad.WorldGuid.ToString(), "the notebook belongs to this world (" + PlayerNotebook.LoadedFor + ")");
			string mine = NotebookTestTitle + world;
			if (what == "write")
			{
				PlayerNotebook.Pages.RemoveAll(p => p.Title == mine);
				PlayerNotebook.AddPage(mine, "Written in " + world + "\n=== not a new page\n\\ a backslash line\n");
				PlayerNotebook.Save();
				string to = PlayerNotebook.PathFor(PlayerNotebook.LoadedFor);
				Check(ref ok, File.Exists(to) && File.ReadAllText(to).Contains(mine), "saved to " + to);
				if (ok) Log("PASS: notebook write (" + world + ")"); else Fail("notebook write");
				yield break;
			}
			if (what == "check" || what == "empty")
			{
				List<PlayerNotebook.Page> ci = PlayerNotebook.Pages.Where(p => p.Title.StartsWith(NotebookTestTitle)).ToList();
				if (what == "empty") Check(ref ok, ci.Count == 0, "no test page in this world (" + string.Join(", ", ci.Select(p => p.Title).ToArray()) + ")");
				else
				{
					PlayerNotebook.Page p = ci.FirstOrDefault(x => x.Title == mine);
					Check(ref ok, p != null && p.Text == "Written in " + world + "\n=== not a new page\n\\ a backslash line\n", "this world's page is back, its text exact: " + (p == null ? "(missing)" : p.Text.Replace("\n", "|")));
					Check(ref ok, ci.All(x => x.Title == mine), "no other world's page: " + string.Join(", ", ci.Select(x => x.Title).ToArray()));
					Check(ref ok, (PlayerNotebook.LastPath ?? "").EndsWith(PlayerNotebook.FileName) && WorldCopy.RaftWorldFolder != null && Path.GetDirectoryName(PlayerNotebook.LastPath) == WorldCopy.RaftWorldFolder, "read from the world's own folder: " + PlayerNotebook.LastPath);
				}
				if (ok) Log("PASS: notebook " + what + " (" + world + ")"); else Fail("notebook " + what);
				yield break;
			}

			// unit
			var sample = new List<PlayerNotebook.Page>
			{
				new PlayerNotebook.Page { Title = "First", Text = "one\n\ntwo\n" },
				new PlayerNotebook.Page { Title = "Tricky", Text = "=== looks like a page\n\\starts with a backslash\n# a hash" },
				new PlayerNotebook.Page { Title = "", Text = "" },
			};
			List<PlayerNotebook.Page> back = PlayerNotebook.Parse(PlayerNotebook.Serialize(sample));
			Check(ref ok, back.Count == 3 && Enumerable.Range(0, 3).All(i => back[i].Title == sample[i].Title && back[i].Text == sample[i].Text), "the file format gives back every page exactly: " + string.Join(" / ", back.Select(p => p.Title + ":" + p.Text.Replace("\n", "|")).ToArray()));
			string other = PlayerNotebook.PathFor(Guid.NewGuid().ToString());
			Check(ref ok, other != PlayerNotebook.PathFor(PlayerNotebook.LoadedFor) && !File.Exists(other), "another world has a file of its own (" + other + ")");

			int before = PlayerNotebook.Pages.Count;
			PlayerNotebookWindow.Open();
			yield return null;
			Check(ref ok, PlayerNotebookWindow.IsOpen && PlayerNotebookWindow.PlayerHeld, "the window opens and holds the player");
			PlayerNotebookWindow.NewPageForTest();
			PlayerNotebookWindow.TypeForTest("CI typed page", "typed line 1\ntyped line 2");
			yield return null;
			Check(ref ok, PlayerNotebook.Pages.Count == before + 1 && PlayerNotebook.Pages.Last().Title == "CI typed page" && PlayerNotebook.Pages.Last().Text == "typed line 1\ntyped line 2", "typing fills a new page");
			Check(ref ok, PlayerNotebookWindow.ListedPages == PlayerNotebook.Pages.Count, "the list shows every page (" + PlayerNotebookWindow.ListedPages + ")");
			Screenshot(new[] { "player_notebook" });
			yield return new WaitForSeconds(0.5f);
			PlayerNotebookWindow.Close();
			yield return null;
			string path = PlayerNotebook.PathFor(PlayerNotebook.LoadedFor);
			Check(ref ok, !PlayerNotebookWindow.IsOpen && File.Exists(path) && File.ReadAllText(path).Contains("=== CI typed page") && File.ReadAllText(path).Contains("typed line 2"), "saved on closing to " + path);
			Check(ref ok, WorldCopy.RaftWorldFolder == null || Path.GetDirectoryName(path) == WorldCopy.RaftWorldFolder, "the file is next to the world's save (" + WorldCopy.RaftWorldFolder + ")");
			Check(ref ok, PlayerNotebook.CopyIn("CI copied note", "copied text") && PlayerNotebook.Pages.Last().Title == "CI copied note" && File.ReadAllText(path).Contains("copied text"), "a note copied in is a new page, saved at once");
			// (journal: the copy button on a page)
			PlayerNotebook.Pages.RemoveAll(p => p.Title == "CI typed page" || p.Title == "CI copied note");
			PlayerNotebook.MarkChanged();
			PlayerNotebook.Save();
			Check(ref ok, !File.ReadAllText(path).Contains("CI typed page"), "the test pages removed again");
			if (ok) Log("PASS: notebook unit"); else Fail("notebook unit");
		}

		#endregion

		#region PF4 camera fly-throughs

		const string FlyIsland = "cipf4fly";

		[ConsoleCommand(name: "CIFlythrough", docs: "Dev (PF4): camera fly-throughs. CIFlythrough unit - the curve passes every camera point in order with its waits, the action reads, Check warns on a path with fewer than 2 points / world - host, world 'CI ...': a lever flies the camera along a 3-point path (input held, Moment 'path' sent, the camera at the points, then back with the input given back), again and skipped (Esc), out of range nothing; picture shot_pf4_flythrough.png / editor - in the editor: 3 points placed, the path's line drawn, 'To my view' with undo, preview and back, a recording writes PNG frames (then deleted)")]
		public static void FlythroughCommand(string[] args) { StartTest(FlythroughRoutine(args != null && args.Length > 0 ? args[0] : "unit")); }

		static IEnumerator FlythroughRoutine(string what)
		{
			if (what == "world") { yield return FlyWorld(); yield break; }
			if (what == "editor") { yield return FlyEditor(); yield break; }
			bool ok = true;
			var keys = new List<CameraPath.Key>
			{
				new CameraPath.Key { Pos = new Vector3(0f, 10f, 0f), Look = Vector3.zero, Speed = 10f, Hold = 1f },
				new CameraPath.Key { Pos = new Vector3(30f, 12f, 0f), Look = new Vector3(30f, 0f, 10f), Speed = 5f, Hold = 0.5f },
				new CameraPath.Key { Pos = new Vector3(30f, 15f, 40f), Look = new Vector3(0f, 0f, 40f), Speed = 10f, Hold = 0f },
			};
			var fl = new CameraPath.Flight(keys);
			Vector3 pos, look;
			fl.Sample(0.5f, out pos, out look);
			Check(ref ok, pos == keys[0].Pos && look == keys[0].Look, "it waits at the first point for its hold");
			// (the second point is reached after the first wait and the first stretch)
			float firstLeg = fl.Duration - 1f - 0.5f;
			bool passes = false;
			for (float t = 1f; t < fl.Duration; t += 0.05f) { fl.Sample(t, out pos, out look); if (Vector3.Distance(pos, keys[1].Pos) < 0.01f) { passes = true; break; } }
			Check(ref ok, passes, "the curve passes the second point exactly");
			fl.Sample(fl.Duration + 5f, out pos, out look);
			Check(ref ok, pos == keys[2].Pos, "it ends at the last point");
			Check(ref ok, fl.Duration > 1.5f + 3f + 4f && fl.Duration < 1.5f + 3f * 1.3f + 8f * 1.3f + 1f, "its length from the distances and speeds (" + fl.Duration.ToString("F2") + " s, legs " + firstLeg.ToString("F2") + " s)");
			Vector3 prev = keys[0].Pos; float worst = 0f;
			for (float t = 0f; t <= fl.Duration; t += 0.02f) { fl.Sample(t, out pos, out look); worst = Mathf.Max(worst, Vector3.Distance(prev, pos)); prev = pos; }
			Check(ref ok, worst < 0.6f, "smooth: no jump between frames (largest step " + worst.ToString("F3") + " m)");
			Check(ref ok, CameraPath.RangeOf("") == CameraPath.DefaultRange && CameraPath.RangeOf("1") == 5f && CameraPath.RangeOf("300") == 300f, "the range reads (default " + CameraPath.DefaultRange + ", at least 5 m)");
			ObjAction act = ObjAction.Parse("flythrough|tour|300");
			Check(ref ok, act != null && act.Shared && BehaviourProps.Verbs.Contains("flythrough") && act.Describe().Contains("'tour'") && act.Describe().Contains("300 m"), "the action reads as '" + (act != null ? act.Describe() : "?") + "'");
			Check(ref ok, ObjAction.Parse("flythrough||") != null && ObjAction.Parse("flythrough||").Describe().Contains("'" + CameraPath.DefaultPath + "'"), "no path name: the path '" + CameraPath.DefaultPath + "'");
			Check(ref ok, PlaceableCatalog.Get(CameraPath.PointName) != null, "the object list has the camera point");

			IslandFile bad = IslandGenerator.CreateFile(new IslandGenSettings { Seed = 1, Radius = 30f, Height = 6f }, "cipf4bad");
			bad.Objects.Add(new IslandObject { Name = "Note_Sign", Props = P(BehaviourProps.Name, "lever", BehaviourProps.EventPrefix + "use", "flythrough|tour|200") });
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.PathProp, "tour", CameraPath.OrderProp, "1") });
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.OrderProp, "1") });
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.PathProp, "Path", CameraPath.OrderProp, "2") });
			Check(ref ok, CameraPath.PointsIn(bad, "tour") == 1 && CameraPath.PointsIn(bad, "") == 2, "points counted by path (no path name = '" + CameraPath.DefaultPath + "', any case)");
			Check(ref ok, PlanChecker.FromFile(bad, "cipf4bad", false).Traps.Any(t => t.Text.Contains("fly-through") && t.Text.Contains("'tour'")), "Check warns on a path with one point");
			bad.Objects.Add(new IslandObject { Name = CameraPath.PointName, Props = P(CameraPath.PathProp, "tour", CameraPath.OrderProp, "2") });
			Check(ref ok, !PlanChecker.FromFile(bad, "cipf4bad", false).Traps.Any(t => t.Text.Contains("fly-through")), "... and not once it has two");
			if (ok) Log("PASS: flythrough unit"); else Fail("flythrough unit");
		}

		/// <summary>A test island: a lever flying the path 'tour' (3 points, the middle one looking at 'tower'), a lever out of range.</summary>
		static IslandFile FlyBuild()
		{
			var s = new IslandGenSettings { Seed = 4242, Radius = 50f, Height = 8f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(s, FlyIsland);
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(0f, 0f), 3), BehaviourProps.Name, "flylever", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "flythrough|tour|2000"));
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(4f, 0f), 3), BehaviourProps.Name, "farlever", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "flythrough|tour|5"));
			f.Objects.Add(ScObj("Note_Sign", ScDry(f, new Vector2(-10f, 10f), 3), BehaviourProps.Name, "tower"));
			Vector3[] at = { new Vector3(-20f, 25f, -20f), new Vector3(0f, 18f, 20f), new Vector3(25f, 22f, 0f) };
			for (int i = 0; i < at.Length; i++)
			{
				IslandObject o = ScObj(CameraPath.PointName, at[i], CameraPath.PathProp, "tour", CameraPath.OrderProp, (i + 1).ToString(), CameraPath.SpeedProp, "20", CameraPath.HoldProp, i == 1 ? "1" : "0");
				if (i == 1) o.Props[CameraPath.LookProp] = "tower";
				o.EulerRotation = new Vector3(30f, 45f, 0f);
				f.Objects.Add(o);
			}
			return f;
		}

		static IEnumerator FlyWorld()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("flythrough world: host, in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Note_Sign" });
			bool ok = true;
			IslandWorldState.Entry e = null;
			yield return RfSpawn(FlyBuild(), FlyIsland, x => e = x);
			if (e == null) yield break;
			Network_Player player = RAPI.GetLocalPlayer();
			List<CameraPath.Key> keys = CameraPath.WorldKeys(e, "tour");
			Check(ref ok, keys.Count == 3 && Behaviours.RefsOf(e).Count(r => CameraPath.IsPoint(r.ObjectName)) == 3, "the island's 3 camera points are in the world (" + keys.Count + ")");
			Check(ref ok, Behaviours.RefsOf(e).Where(r => CameraPath.IsPoint(r.ObjectName)).All(r => r.GetComponentsInChildren<Renderer>().Length == 0), "... and not seen (no marker in a world)");
			Transform tower = ScObjOf(e, "tower").transform;
			Check(ref ok, keys.Count == 3 && Vector3.Distance(keys[1].Look, tower.position) < 0.01f, "the middle point looks at 'tower'");

			int shown = CameraFlight.Shown, skipped = CameraFlight.Skipped;
			var sent = new List<IslandNetMessage>();
			IslandNetwork.Loopback = m => sent.Add(m);
			try { ScUse(e, "flylever"); }
			finally { IslandNetwork.Loopback = null; }
			yield return null;
			Check(ref ok, CameraFlight.Active && CameraFlight.Shown == shown + 1 && CameraFlight.InputHeld, "the lever starts the flight, the player's input held");
			Check(ref ok, sent.Any(m => m.Kind == IslandNetMessage.Moment && m.Name == "path" && m.Ids[0] == e.Id && m.Data.StartsWith("tour\n")), "every player is told (Moment 'path')");
			Transform cam = player.Camera != null ? player.Camera.transform : Camera.main.transform;
			yield return new WaitForSecondsRealtime(CameraMoment.Glide + 0.05f);
			float atFirst = keys.Count > 0 ? Vector3.Distance(cam.position, keys[0].Pos) : 99f;
			Check(ref ok, atFirst < 1.5f, "the camera starts at the first point (" + atFirst.ToString("F2") + " m)");
			float best = 99f, bestLook = 180f, duration = CameraFlight.LastDuration;
			bool shot = false;
			for (float until = Time.unscaledTime + duration; Time.unscaledTime < until && CameraFlight.Active; )
			{
				if (keys.Count > 1)
				{
					float d = Vector3.Distance(cam.position, keys[1].Pos);
					if (d < best) { best = d; bestLook = Vector3.Angle(cam.forward, tower.position - cam.position); }
				}
				if (!shot && best < 0.3f && bestLook < 30f) { shot = true; Shot("pf4_flythrough"); }
				yield return null;
			}
			Check(ref ok, best < 0.3f && bestLook < 5f, "it passes the middle point (" + best.ToString("F2") + " m), looking at 'tower' (" + bestLook.ToString("F1") + " degrees)");
			yield return new WaitForSecondsRealtime(CameraMoment.Glide + 0.5f);
			float home = Vector3.Distance(cam.position, player.transform.position);
			Check(ref ok, !CameraFlight.Active && !CameraFlight.InputHeld && home < 3f, "over after " + duration.ToString("F1") + " s: the camera with the player (" + home.ToString("F1") + " m), the input given back");

			ScUse(e, "flylever");
			yield return new WaitForSecondsRealtime(1f);
			Check(ref ok, CameraFlight.Active && CameraFlight.InputHeld, "again: it flies (once per use, no loop)");
			EscKey.Press();
			yield return null;
			yield return null;
			yield return null;
			home = Vector3.Distance(cam.position, player.transform.position);
			Check(ref ok, !CameraFlight.Active && !CameraFlight.InputHeld && CameraFlight.Skipped == skipped + 1 && home < 3f, "Esc skips it: the camera back at once (" + home.ToString("F1") + " m), the input given back");
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, CanvasHelper.ActiveMenu != MenuType.PauseMenu, "... and Raft's pause menu is not left open");

			shown = CameraFlight.Shown;
			ScUse(e, "farlever");
			yield return new WaitForSeconds(0.3f);
			Check(ref ok, CameraFlight.Shown == shown && !CameraFlight.Active, "out of range (5 m of a point): no flight here");
			RfAway(e);
			RfRemove(FlyIsland);
			if (ok) Log("PASS: flythrough world"); else Fail("flythrough world");
		}

		static IEnumerator FlyEditor()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 20f, 500f);
			var made = new List<EditorGameObject>();
			for (int i = 0; i < 3; i++)
			{
				EditorGameObject o = PlaceForTest(CameraPath.PointName, c0 + new Vector3(i * 15f, i * 3f, i % 2 * 10f), placed);
				if (o == null) { Fail("flythrough editor: no camera point placed"); yield break; }
				o.Props[CameraPath.PathProp] = "ci tour";
				o.Props[CameraPath.OrderProp] = (3 - i).ToString();
				o.Props[CameraPath.SpeedProp] = "30";
				made.Add(o);
			}
			yield return new WaitForSecondsRealtime(0.7f);
			Check(ref ok, CameraPath.PathsInEditor().Contains("ci tour") && CameraPath.EditorPoints("ci tour").Count == 3, "the path is listed for the action's ... button");
			Check(ref ok, CameraPath.LinesShown > 0, "its line is drawn (" + CameraPath.LinesShown + ")");
			List<CameraPath.Key> keys = CameraPath.EditorKeys("ci tour");
			Check(ref ok, keys.Count == 3 && Vector3.Distance(keys[0].Pos, made[2].transform.position) < 0.01f, "the points in their order (order 1 first)");

			Camera c = Camera.main;
			Vector3 camWas = c.transform.position;
			Vector3 pointWas = made[0].transform.position;
			Check(ref ok, ObjectInspector.CameraPointToView(made[0]) && Vector3.Distance(made[0].transform.position, camWas) < 0.01f, "'To my view' moves a point to the camera");
			CommandUndoRedo.UndoRedoManager.Undo();
			yield return null;
			Check(ref ok, Vector3.Distance(made[0].transform.position, pointWas) < 0.01f, "... and Ctrl+Z puts it back");

			EditorCamera ec = c.GetComponent<EditorCamera>();
			Check(ref ok, CameraPath.Preview("ci tour", false) == null && CameraFlight.Active && (ec == null || !ec.enabled), "Preview flies the editor camera (its own control off)");
			yield return new WaitForSecondsRealtime(0.2f);
			Check(ref ok, keys.Count > 0 && Vector3.Distance(c.transform.position, keys[0].Pos) < 8f, "it starts at the first point");
			float wait = Time.unscaledTime + CameraFlight.LastDuration + 3f;
			while (CameraFlight.Active && Time.unscaledTime < wait) yield return null;
			yield return null;
			Check(ref ok, !CameraFlight.Active && Vector3.Distance(c.transform.position, camWas) < 0.01f && (ec == null || ec.enabled), "over: the editor camera back where it was, its control on");

			Check(ref ok, CameraPath.Preview("ci tour", true) == null && CameraFlight.Active, "Record trailer starts");
			for (int i = 0; i < 20; i++) yield return null;
			string folder = CameraFlight.LastFolder;
			EscKey.Press();
			yield return null;
			yield return null;
			int pngs = folder != null && Directory.Exists(folder) ? Directory.GetFiles(folder, "frame_*.png").Length : 0;
			Check(ref ok, !CameraFlight.Active && pngs >= 10 && pngs == CameraFlight.LastFrames && File.Exists(Path.Combine(folder, "frame_00001.png")), "it wrote " + pngs + " frames to " + folder + " (Esc stopped it)");
			Check(ref ok, Vector3.Distance(c.transform.position, camWas) < 0.01f, "... and the camera is back");
			Check(ref ok, CameraPath.Preview("nothing here", false) != null && !CameraFlight.Active, "a path with no points says so, no flight");
			try { if (folder != null) Directory.Delete(folder, true); } catch { }
			foreach (EditorGameObject o in made) if (o != null) UnityEngine.Object.Destroy(o.gameObject);
			yield return new WaitForSecondsRealtime(0.7f);
			Check(ref ok, !CameraPath.PathsInEditor().Contains("ci tour"), "the test points removed");
			if (ok) Log("PASS: flythrough editor"); else Fail("flythrough editor");
		}

		#endregion
	}
}
