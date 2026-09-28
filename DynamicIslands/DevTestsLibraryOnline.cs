using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of the island library window (LibraryWindow, LibraryClient): against a small library written to disk
	/// (file://) - the list, pictures, Download, Update, Remove, a damaged file, no connection, a newer mod's entry - and
	/// once against the real library on GitHub (libraryonline.ps1, TEST_CATALOGUE G45-G47).
	/// </summary>
	public static partial class DevTests
	{
		static string TestLibraryFolder { get { return Path.GetFullPath(Path.Combine(LibraryPack.LibraryFolder, "citest-lib")); } }

		static byte[] TestJpg(Color c)
		{
			var tex = new Texture2D(64, 64, TextureFormat.RGB24, false);
			tex.SetPixels(Enumerable.Repeat(c, 64 * 64).ToArray());
			tex.Apply();
			byte[] b = tex.EncodeToJPG(80);
			UnityEngine.Object.Destroy(tex);
			return b;
		}

		/// <summary>Writes an entry folder into the test library and returns its index.json object.</summary>
		static Dictionary<string, object> TestEntry(string kind, LibraryInfo info, Dictionary<string, byte[]> files)
		{
			string path = kind + "s/" + info.id;
			string dir = Path.Combine(TestLibraryFolder, path.Replace('/', Path.DirectorySeparatorChar));
			if (Directory.Exists(dir)) Directory.Delete(dir, true);
			Directory.CreateDirectory(dir);
			foreach (var f in files) File.WriteAllBytes(Path.Combine(dir, f.Key), f.Value);
			var o = LibraryJson.Parse(info.ToJson()) as Dictionary<string, object>;
			o["path"] = path;
			o["islands"] = files.Keys.Count(k => k.EndsWith(".island"));
			o["size"] = files.Values.Sum(b => (long)b.Length);
			o["updated"] = "2026-09-28";
			o["files"] = files.OrderBy(f => f.Key).Select(f => (object)new Dictionary<string, object> { { "name", f.Key }, { "size", f.Value.Length }, { "sha256", LibraryPack.Sha256(f.Value) } }).ToList();
			return o;
		}

		static void WriteTestIndex(params Dictionary<string, object>[] entries)
		{
			File.WriteAllText(Path.Combine(TestLibraryFolder, "index.json"), LibraryJson.Write(new Dictionary<string, object> { { "format", 1 }, { "library", "CI test library" }, { "download", "" }, { "entries", entries.Cast<object>().ToList() } }));
		}

		static IEnumerator WaitNotBusy(float seconds)
		{
			for (float t = 0; LibraryWindow.Busy && t < seconds; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
		}

		static IEnumerator LoadList(bool force)
		{
			bool doneLoading = false;
			yield return LibraryClient.LoadIndex(force, e => doneLoading = true);
			for (float t = 0; !doneLoading && t < 30f; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
		}

		[ConsoleCommand(name: "CILibraryBrowse", docs: "Dev, main menu: the ISLAND LIBRARY window against a library written to disk (file://): the main menu's button opens it, the plans and islands tabs, icons and pictures, Download installs, the button says Installed; a newer version in the list says Update and updates; a damaged file is refused and nothing installed; an entry from a newer mod says Download anyway; Remove asks first; no connection says so; the window fits the screen. Cleans up")]
		public static void LibraryBrowseCommand() { DynamicIslands.instance.StartCoroutine(LibraryBrowseRoutine()); }

		static IEnumerator LibraryBrowseRoutine()
		{
			bool ok = true;
			CleanLibTests();
			if (Directory.Exists(TestLibraryFolder)) Directory.Delete(TestLibraryFolder, true);
			Directory.CreateDirectory(TestLibraryFolder);
			try
			{
				// A plan (a brings b, b brings c) and an island, made and exported as a creator would
				MakeLibIsland(LibC, 3); MakeLibIsland(LibB, 2, LibC); MakeLibIsland(LibA, 1, LibB);
				var plan = new WorldPlan { Name = LibPlan, Description = "a test plan", Random = false };
				plan.Rules.Add(new IntroRule { Id = "start", What = "island", WhatArg = LibA, When = "start", Where = "ahead", Distance = 300 });
				plan.Save();
				MakeLibIsland("cilib-solo", 9);
				string error;
				string planZip = LibraryPack.Export(new LibraryInfo { id = "ci-lib-pack", title = "CI Lib Pack", author = "CI Tester", summary = "a test plan", description = "Three islands in a chain.", tags = new[] { "test", "quest" } }, null, WorldPlan.Load(LibPlan), TestJpg(Color.green), TestJpg(Color.blue), out error);
				string soloZip = LibraryPack.Export(new LibraryInfo { id = "ci-lib-solo", title = "CI Lib Solo", author = "CI Tester", summary = "a test island" }, "cilib-solo", null, TestJpg(Color.red), TestJpg(Color.yellow), out error);
				LibraryPackContents pp = LibraryPack.Read(planZip, out error), sp = LibraryPack.Read(soloZip, out error);
				Check(ref ok, pp != null && sp != null, "two packs exported (a plan with 3 islands, an island)");
				if (pp == null || sp == null) yield break;
				Func<LibraryPackContents, Dictionary<string, byte[]>> filesOf = p => p.Files.Where(f => f.Key != "info.json").ToDictionary(f => f.Key, f => f.Value);
				Dictionary<string, object> planEntry = TestEntry(LibraryPack.KindPlan, pp.Info, filesOf(pp)), soloEntry = TestEntry(LibraryPack.KindIsland, sp.Info, filesOf(sp));
				WriteTestIndex(planEntry, soloEntry);
				// (a PC without them)
				DeleteLib(LibA, LibB, LibC, "cilib-solo");
				File.Delete(WorldPlan.PathFor(LibPlan));
				foreach (string z in new[] { planZip, soloZip }) File.Delete(z);

				LibraryClient.TestAddress = "file:///" + TestLibraryFolder.Replace('\\', '/') + "/";
				LibraryClient.Forget();
				// The main menu's button
				GameObject menuButton = GameObject.Find("MainMenuCanvas/MenuButtons/LIBRARY");
				Check(ref ok, menuButton != null && menuButton.activeInHierarchy, "the main menu has ISLAND LIBRARY");
				if (menuButton != null) menuButton.GetComponent<Button>().onClick.Invoke(); else LibraryWindow.Open();
				yield return LoadList(false);
				yield return new WaitForSecondsRealtime(1f);
				Check(ref ok, LibraryWindow.IsOpen && LibraryClient.Entries != null && LibraryClient.Entries.Count == 2, "the window opens and lists the library's 2 entries (" + LibraryWindow.Status + ")");
				Check(ref ok, LibraryWindow.Shown().SequenceEqual(new[] { "ci-lib-pack" }), "the plans tab shows the plan: " + string.Join(", ", LibraryWindow.Shown().ToArray()));
				Button pr = LibraryWindow.Row("ci-lib-pack");
				RawImage icon = pr != null ? pr.GetComponentsInChildren<RawImage>(true).FirstOrDefault(i => i.name == "Icon") : null;
				Check(ref ok, icon != null && icon.texture != null, "its row has its icon");
				Check(ref ok, LibraryWindow.Selected != null && LibraryWindow.Selected.Info.id == "ci-lib-pack" && LibraryWindow.Picture != null, "it is selected, with its picture");
				Check(ref ok, UIKit.LabelOf(LibraryWindow.MainButton).text == "Download" && !LibraryWindow.RemoveButton.gameObject.activeSelf, "the button says Download (no Remove yet)");
				Screenshot(new[] { "library_window" });
				yield return new WaitForSecondsRealtime(0.6f);
				var off = OffScreen(LibraryWindow.Root.gameObject, Screen.width, Screen.height);
				Check(ref ok, off.Count == 0, "the window fits the screen" + (off.Count > 0 ? " - off: " + string.Join(", ", off.Take(4).ToArray()) : ""));

				// Download
				LibraryWindow.MainButton.onClick.Invoke();
				yield return WaitNotBusy(30f);
				Check(ref ok, new[] { LibA, LibB, LibC }.All(n => File.Exists(IslandSpawner.PathFor(n))) && WorldPlan.Load(LibPlan) != null && LibraryPack.Installed().Any(e => e.id == "ci-lib-pack" && e.source == LibraryPack.SourceLibrary),
					"Download installs the plan and its 3 islands: " + LibraryWindow.Progress);
				Check(ref ok, UIKit.LabelOf(LibraryWindow.MainButton).text == "Installed" && !LibraryWindow.MainButton.interactable && LibraryWindow.RemoveButton.gameObject.activeSelf, "then the button says Installed, and Remove is there");

				// A newer version in the library: Update
				pp.Info.version = 2; pp.Info.summary = "version 2";
				planEntry = TestEntry(LibraryPack.KindPlan, pp.Info, filesOf(pp));
				WriteTestIndex(planEntry, soloEntry);
				yield return LoadList(true);
				LibraryWindow.Select("ci-lib-pack");
				yield return null;
				Check(ref ok, LibraryClient.StateOf(LibraryWindow.Selected) == LibraryClient.State.Update && UIKit.LabelOf(LibraryWindow.MainButton).text == "Update", "a newer version in the list: the button says Update");
				LibraryWindow.MainButton.onClick.Invoke();
				yield return WaitNotBusy(30f);
				Check(ref ok, LibraryPack.Installed().Any(e => e.id == "ci-lib-pack" && e.version == 2) && UIKit.LabelOf(LibraryWindow.MainButton).text == "Installed", "Update installs version 2: " + LibraryWindow.Progress);

				// A damaged file: refused, nothing installed
				var files = LibraryJson.Objects(soloEntry, "files");
				Dictionary<string, object> islandFile = files.First(f => LibraryJson.Str(f, "name").EndsWith(".island"));
				islandFile["sha256"] = new string('0', 64);
				WriteTestIndex(planEntry, soloEntry);
				yield return LoadList(true);
				LibraryWindow.Select("ci-lib-solo");
				LibraryWindow.MainButton.onClick.Invoke();
				yield return WaitNotBusy(30f);
				Check(ref ok, !File.Exists(IslandSpawner.PathFor("cilib-solo")) && !LibraryPack.Installed().Any(e => e.id == "ci-lib-solo") && LibraryWindow.Progress.Contains("Nothing was installed"),
					"a file that isn't what the list says is refused, nothing installed: " + LibraryWindow.Progress);

				// An entry made with a newer mod: Download anyway
				soloEntry = TestEntry(LibraryPack.KindIsland, sp.Info, filesOf(sp));
				soloEntry["minModVersion"] = "99.0";
				WriteTestIndex(planEntry, soloEntry);
				yield return LoadList(true);
				LibraryWindow.Select("ci-lib-solo");
				yield return null;
				Check(ref ok, UIKit.LabelOf(LibraryWindow.MainButton).text == "Download anyway", "an entry from a newer mod version: 'Download anyway' with a warning");

				// Remove asks first
				LibraryWindow.Select("ci-lib-pack");
				yield return null;
				LibraryWindow.RemoveButton.onClick.Invoke();
				yield return null;
				Check(ref ok, UIKit.LabelOf(LibraryWindow.RemoveButton).text == "Sure? Remove" && File.Exists(IslandSpawner.PathFor(LibA)), "Remove asks first");
				LibraryWindow.RemoveButton.onClick.Invoke();
				yield return null;
				Check(ref ok, !File.Exists(IslandSpawner.PathFor(LibA)) && !LibraryPack.Installed().Any(e => e.id == "ci-lib-pack") && UIKit.LabelOf(LibraryWindow.MainButton).text == "Download", "then removes it (Download again)");

				// No connection
				LibraryClient.TestAddress = "file:///" + Path.Combine(TestLibraryFolder, "nothing-here").Replace('\\', '/') + "/";
				yield return LoadList(true);
				Check(ref ok, LibraryClient.Entries == null && LibraryClient.LastError != null && LibraryClient.LastError.Contains("Can't reach"), "no connection: " + LibraryClient.LastError);
				LibraryWindow.Close();
			}
			finally
			{
				LibraryClient.TestAddress = null;
				LibraryClient.Forget();
				if (LibraryWindow.IsOpen) LibraryWindow.Close();
				CleanLibTests();
				foreach (string n in IslandSpawner.ListSavedIslands().Where(n => n.StartsWith("cilib-")).ToList()) DeleteLib(n);
				RemovePoolTestLines();
				try { if (Directory.Exists(TestLibraryFolder)) Directory.Delete(TestLibraryFolder, true); } catch { }
			}
			if (ok) Log("PASS: library browse"); else Fail("library browse");
		}

		[ConsoleCommand(name: "CIMakeMapIsland", docs: "Dev, in a world or the editor (the object catalog built): saves a new island of a map type from a seed under a name, with a title for its arrival banner - for the island library's entries: CIMakeMapIsland <map type>|<seed>|<name>|<title>")]
		public static void MakeMapIslandCommand(string[] args)
		{
			string[] p = string.Join(" ", args ?? new string[0]).Split('|').Select(x => x.Trim()).ToArray();
			int seed;
			if (p.Length < 4 || !int.TryParse(p[1], out seed)) { Fail("CIMakeMapIsland <map type>|<seed>|<name>|<title>"); return; }
			MapType type = MapTypes.Get(p[0]);
			if (type == null) { Fail("no map type '" + p[0] + "'"); return; }
			DynamicIslands.instance.StartCoroutine(MakeMapIslandRoutine(type, seed, p[2], p[3]));
		}

		static IEnumerator MakeMapIslandRoutine(MapType type, int seed, string name, string title)
		{
			// (without the object catalog the generator places no plants, trees or rocks: a bare island)
			yield return PlaceableCatalog.EnsureBuilt();
			string[] p = { type.Name, seed.ToString(), name, title };
			try
			{
				float elevation;
				IslandGenSettings s = MapTypes.Roll(type, new System.Random(seed), out elevation);
				s.Seed = seed;
				IslandFile file = MapTypes.Create(type, s, elevation, p[2]);
				file.Name = p[2];
				file.Props[IslandProps.Title] = p[3];
				file.Save(IslandSpawner.PathFor(p[2]));
				IslandCache.Forget();
				Log("MADE '" + p[2] + "' (" + type.Name + ", seed " + seed + ", " + file.Objects.Count + " objects, elevation " + elevation.ToString("F0") + ", quest '" + (file.Props.ContainsKey(IslandQuest.KeyTitle) ? file.Props[IslandQuest.KeyTitle] : "") + "')");
				Log("PASS: map island made");
			}
			catch (Exception e) { Fail("making the island: " + e.Message); }
		}

		[ConsoleCommand(name: "CILibraryDownload", docs: "Dev, main menu: downloads entries from the real island library as the window's Download does (each file checked, installed like Import) and checks their files are there: CILibraryDownload <id,id,...>")]
		public static void LibraryDownloadCommand(string[] args) { DynamicIslands.instance.StartCoroutine(LibraryDownloadRoutine(string.Join(" ", args ?? new string[0]).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList())); }

		static IEnumerator LibraryDownloadRoutine(List<string> ids)
		{
			bool ok = true;
			LibraryClient.TestAddress = null;
			yield return LoadList(true);
			Check(ref ok, LibraryClient.Entries != null, "the library's list: " + (LibraryClient.Entries != null ? LibraryClient.Entries.Count + " entries" : LibraryClient.LastError));
			if (LibraryClient.Entries == null) { Fail("library download"); yield break; }
			foreach (string id in ids)
			{
				LibraryEntry e = LibraryClient.Entries.FirstOrDefault(x => x.Info.id == id);
				if (e == null) { Check(ref ok, false, "'" + id + "' is in the library"); continue; }
				LibraryPack.Report report = null; string error = null; bool finished = false;
				yield return LibraryClient.Download(e, false, false, _ => { }, (r, err) => { report = r; error = err; finished = true; });
				for (float t = 0; !finished && t < 60f; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
				bool files = report != null && report.Islands.All(n => File.Exists(IslandSpawner.PathFor(n))) && (report.PlanName == null || File.Exists(WorldPlan.PathFor(report.PlanName)));
				Check(ref ok, error == null && files && LibraryClient.StateOf(e) == LibraryClient.State.Installed,
					"'" + e.Info.title + "' downloaded and installed: " + (error ?? (report.Islands.Count + " island(s)" + (report.PlanName != null ? " + the plan '" + report.PlanName + "'" : ""))));
			}
			if (ok) Log("PASS: library download"); else Fail("library download");
		}

		[ConsoleCommand(name: "CIIslandPicture", docs: "Dev, in a world: a picture of a spawned island with Raft's own camera from out at sea (Raft's water, no HUD, no held tool): <file>-pic.jpg 1280x720 and <file>-icon.jpg 256x256 in Mods\\DynamicIslands. CIIslandPicture <island>|<file>|<angle 0-360 from north>|<distance x land radius, default 2.2>|<height x land radius, default 0.45>|<aim above the sea x height of the top, default 0.35>")]
		public static void IslandPictureCommand(string[] args) { DynamicIslands.instance.StartCoroutine(IslandPictureRoutine(string.Join(" ", args ?? new string[0]).Split('|').Select(x => x.Trim()).ToArray())); }

		static IEnumerator IslandPictureRoutine(string[] p)
		{
			if (p.Length < 2) { Fail("CIIslandPicture <island>|<file>|<angle>|<distance>|<height>|<aim>"); yield break; }
			Func<int, float, float> num = (i, d) => { float v; return p.Length > i && float.TryParse(p[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : d; };
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.HostName.Equals(p[0], StringComparison.OrdinalIgnoreCase) && x.Root != null);
			Camera cam = Camera.main;
			if (e == null || cam == null) { Fail("no spawned island '" + p[0] + "' (or no camera)"); yield break; }
			Terrain terrain = e.Root.GetComponentInChildren<Terrain>();
			Vector3 top = terrain != null ? HighestPoint(terrain) : e.Root.transform.position;
			float radius = Mathf.Max(30f, CustomIslandSpawner.LandRadius(e.Name));
			float angle = num(2, 200f) * Mathf.Deg2Rad, dist = num(3, 1.3f) * radius, height = num(4, 0.3f) * radius, aim = num(5, 0.3f);
			Vector3 centre = new Vector3(top.x, e.Root.transform.position.y, top.z);
			float sea = e.Position.y;
			Vector3 pos = new Vector3(centre.x + Mathf.Sin(angle) * dist, Mathf.Max(sea, 0f) + height + Mathf.Max(0f, top.y - sea) * 0.25f, centre.z + Mathf.Cos(angle) * dist);
			Vector3 look = new Vector3(centre.x, sea + (top.y - sea) * aim, centre.z);
			yield return new WaitForEndOfFrame();
			// (the camera's own children - the arms and the held tool - left out of the picture)
			int mask = cam.cullingMask;
			foreach (Renderer r in cam.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.layer != 0) cam.cullingMask &= ~(1 << r.gameObject.layer);
			Vector3 lp = cam.transform.localPosition; Quaternion lr = cam.transform.localRotation;
			float far = cam.farClipPlane;
			// (Raft's haze hides and tints anything a few hundred metres off: off for the picture)
			bool fog = RenderSettings.fog;
			// (and Raft's own haze on the camera - its effects named like fog, scattering or atmosphere)
			var hazes = cam.GetComponents<Behaviour>().Where(b => b != null && b.enabled && System.Text.RegularExpressions.Regex.IsMatch(b.GetType().Name, "Fog|Scatter|Atmos|Haze|Mist", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
			Log("Camera effects: " + string.Join(", ", cam.GetComponents<Behaviour>().Where(b => b != null).Select(b => b.GetType().Name + (b.enabled ? "" : " (off)")).ToArray()) + "; switched off for the picture: " + (hazes.Count == 0 ? "none" : string.Join(", ", hazes.Select(b => b.GetType().Name).ToArray())));
			byte[] pic = null, icon = null;
			try
			{
				cam.transform.position = pos;
				cam.transform.LookAt(look);
				cam.farClipPlane = Mathf.Max(far, dist * 3f);
				RenderSettings.fog = false;
				foreach (Behaviour b in hazes) b.enabled = false;
				pic = RenderCam(cam, 1280, 720).EncodeToJPG(86);
				icon = RenderCam(cam, 256, 256).EncodeToJPG(88);
			}
			finally
			{
				cam.transform.localPosition = lp; cam.transform.localRotation = lr;
				cam.cullingMask = mask; cam.farClipPlane = far;
				RenderSettings.fog = fog;
				foreach (Behaviour b in hazes) if (b != null) b.enabled = true;
			}
			string stem = Path.Combine(DynamicIslands.assetpath, p[1]);
			File.WriteAllBytes(stem + "-pic.jpg", pic);
			File.WriteAllBytes(stem + "-icon.jpg", icon);
			Log("PICTURE " + Path.GetFullPath(stem + "-pic.jpg") + " (from " + dist.ToString("F0") + " m, " + pos.y.ToString("F0") + " m up)");
			Log("PASS: island picture");
		}

		static Texture2D RenderCam(Camera cam, int w, int h)
		{
			var rt = new RenderTexture(w, h, 24);
			RenderTexture before = cam.targetTexture, active = RenderTexture.active;
			cam.targetTexture = rt;
			cam.aspect = w / (float)h;
			cam.Render();
			cam.targetTexture = before;
			cam.ResetAspect();
			RenderTexture.active = rt;
			var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
			tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
			tex.Apply();
			RenderTexture.active = active;
			UnityEngine.Object.Destroy(rt);
			return tex;
		}

		[ConsoleCommand(name: "CIExportEntry", docs: "Dev: exports a library entry through the Share path (LibraryPack.Export) from a JSON file: an info.json plus \"source\" (the island's or plan's name), \"iconFile\" and \"pictureFiles\" (JPG paths). Logs EXPORTED <zip>: CIExportEntry <path to the JSON>")]
		public static void ExportEntryCommand(string[] args)
		{
			string path = string.Join(" ", args ?? new string[0]).Trim();
			try
			{
				var o = LibraryJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
				LibraryInfo info = LibraryInfo.From(o);
				string source = LibraryJson.Str(o, "source");
				byte[] icon = File.ReadAllBytes(LibraryJson.Str(o, "iconFile"));
				var pictures = LibraryJson.Strings(o, "pictureFiles").Select(File.ReadAllBytes).ToList();
				WorldPlan plan = info.IsPlan ? WorldPlan.Load(source) : null;
				if (info.IsPlan && plan == null) { Fail("no plan '" + source + "'"); return; }
				string error;
				string zip = LibraryPack.ExportWith(info, info.IsPlan ? null : source, plan, icon, pictures, out error);
				if (zip == null) { Fail("export: " + error); return; }
				Log("EXPORTED " + Path.GetFullPath(zip));
				Log("PASS: entry exported");
			}
			catch (Exception e) { Fail("export: " + e.Message); }
		}

		[ConsoleCommand(name: "CILibraryOnline", docs: "Dev, main menu: the real island library on GitHub: its list reads (at least the sample plan and island), icons and pictures download, and the sample island downloads and installs (the same file is already here, so it's shared) and is removed again (the file stays)")]
		public static void LibraryOnlineCommand() { DynamicIslands.instance.StartCoroutine(LibraryOnlineRoutine()); }

		static IEnumerator LibraryOnlineRoutine()
		{
			bool ok = true;
			LibraryClient.TestAddress = null;
			LibraryClient.Forget();
			LibraryWindow.Open(1);
			yield return LoadList(true);
			yield return new WaitForSecondsRealtime(3f);
			Check(ref ok, LibraryClient.Entries != null && LibraryClient.Entries.Count >= 2 && LibraryClient.Entries.Any(e => e.Info.id == "palm-cove") && LibraryClient.Entries.Any(e => e.Info.id == "first-voyage"),
				"the library's list reads from " + LibraryClient.Address + ": " + (LibraryClient.Entries != null ? string.Join(", ", LibraryClient.Entries.Select(e => e.Info.id).ToArray()) : LibraryClient.LastError));
			if (LibraryClient.Entries == null) { Fail("library online"); yield break; }
			LibraryWindow.Select("palm-cove");
			yield return new WaitForSecondsRealtime(4f);
			Button row = LibraryWindow.Row("palm-cove");
			RawImage icon = row != null ? row.GetComponentsInChildren<RawImage>(true).FirstOrDefault(i => i.name == "Icon") : null;
			Check(ref ok, icon != null && icon.texture != null && LibraryWindow.Picture != null && LibraryWindow.Picture.width == 1280, "its icon and picture download (" + (LibraryWindow.Picture != null ? LibraryWindow.Picture.width + "x" + LibraryWindow.Picture.height : "none") + ")");
			Screenshot(new[] { "library_online" });
			yield return new WaitForSecondsRealtime(0.6f);
			bool had = File.Exists(IslandSpawner.PathFor("Palm Cove"));
			string before = had ? LibraryPack.Sha256(File.ReadAllBytes(IslandSpawner.PathFor("Palm Cove"))) : null;
			if (LibraryClient.StateOf(LibraryWindow.Selected) == LibraryClient.State.NotInstalled)
			{
				LibraryWindow.MainButton.onClick.Invoke();
				yield return WaitNotBusy(60f);
			}
			Check(ref ok, File.Exists(IslandSpawner.PathFor("Palm Cove")) && LibraryPack.Installed().Any(e => e.id == "palm-cove"), "Palm Cove downloads and installs: " + LibraryWindow.Progress);
			LibraryWindow.RemoveButton.onClick.Invoke(); yield return null;
			LibraryWindow.RemoveButton.onClick.Invoke(); yield return null;
			bool stays = !had || (File.Exists(IslandSpawner.PathFor("Palm Cove")) && LibraryPack.Sha256(File.ReadAllBytes(IslandSpawner.PathFor("Palm Cove"))) == before);
			Check(ref ok, !LibraryPack.Installed().Any(e => e.id == "palm-cove") && stays, "removed again" + (had ? " (the Palm Cove that was here before stays)" : ""));
			LibraryWindow.Close();
			if (ok) Log("PASS: library online"); else Fail("library online");
		}
	}
}
