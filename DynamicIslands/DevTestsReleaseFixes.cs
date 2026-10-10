using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandUndoRedo;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of what the user decided before the release (TEST_CATALOGUE UE1, UE2, UW1, UW7, UL8; tools\releasefix.ps1):
	/// the editor's autosave, the notice when saving an island saved worlds have, an edited plan in a saved world, and an
	/// older Raft save loading the mod's state of that save.
	/// </summary>
	public static partial class DevTests
	{
		const string AutosaveIsland = "ciautosave";

		class NoopCommand : ICommand { public void Execute() { } public void UnExecute() { } }

		[ConsoleCommand(name: "CIAutosave", docs: "Dev, editor: the autosave - nothing unsaved after Save; a change is autosaved after the interval (whole, with the change); it is offered (newer than the island), the offer window opens, picking it opens the unsaved work; Save removes it; leaving writes it at once; Throw away deletes it; saving an island saved worlds have says so once. Cleans up")]
		public static void AutosaveCommand() { StartTest(AutosaveRoutine()); }

		static IEnumerator AutosaveRoutine()
		{
			bool ok = true;
			string island = IslandSpawner.PathFor(AutosaveIsland), autosave = EditorAutosave.PathFor(AutosaveIsland);
			string worldFile = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), "ci-autosave-test.txt");
			float interval = EditorAutosave.IntervalSeconds;
			try
			{
				if (!DynamicIslands.InEditor()) { Fail("autosave: open the editor first"); yield break; }
				foreach (string f in new[] { island, autosave, worldFile }) if (File.Exists(f)) File.Delete(f);
				DynamicIslands.NewIsland();
				Check(ref ok, DynamicIslands.SaveIsland(AutosaveIsland) && !EditorAutosave.Unsaved, "saved: nothing unsaved");

				// A change (raised ground, one undo step): autosaved once the interval has passed
				TerrainData data = terraineditor.terrain.terrainData;
				data.SetHeights(10, 10, new float[,] { { 0.3f } });
				UndoRedoManager.Execute(new NoopCommand());
				Check(ref ok, EditorAutosave.Unsaved, "a change: unsaved");
				EditorAutosave.IntervalSeconds = 1f;
				for (float until = Time.unscaledTime + 10f; !File.Exists(autosave) && Time.unscaledTime < until; ) yield return new WaitForSecondsRealtime(0.5f);
				EditorAutosave.IntervalSeconds = interval;
				Check(ref ok, File.Exists(autosave) && !File.Exists(autosave + ".tmp"), "autosaved after the interval: " + autosave);
				IslandFile a = File.Exists(autosave) ? IslandFile.Load(autosave) : null;
				Check(ref ok, a != null && Mathf.Abs(a.Heights[10, 10] - 0.3f) < 0.01f && IslandFile.Load(island).Heights[10, 10] < 0.01f, "the autosave has the change, the island's file doesn't");
				Check(ref ok, EditorAutosave.Waiting().Contains(AutosaveIsland), "it is offered (newer than the island)");

				// The offer, and picking it: the unsaved work comes back (the ground raised), still unsaved
				DynamicIslands.LoadIsland(AutosaveIsland);
				yield return null;
				Check(ref ok, data.GetHeights(10, 10, 1, 1)[0, 0] < 0.01f && !EditorAutosave.Unsaved, "the island opened from its file: the change isn't there");
				yield return EditorAutosave.Offer();
				Check(ref ok, ChoiceWindow.IsOpen, "the editor offers the unsaved work in a window");
				ChoiceWindow.Close();
				EditorAutosave.Pick(AutosaveIsland);
				yield return null;
				Check(ref ok, Mathf.Abs(data.GetHeights(10, 10, 1, 1)[0, 0] - 0.3f) < 0.01f && EditorAutosave.Unsaved && DynamicIslands.currentIslandName == AutosaveIsland,
					"picking it opens the unsaved work under the island's name, still unsaved");

				// Save: the autosave goes
				Check(ref ok, DynamicIslands.SaveIsland(AutosaveIsland) && !File.Exists(autosave) && !EditorAutosave.Unsaved, "Save keeps it and removes the autosave");

				// Leaving with a change (the Main menu button): written at once; Throw them away deletes it
				UndoRedoManager.Execute(new NoopCommand());
				EditorAutosave.WriteNow();
				Check(ref ok, File.Exists(autosave), "leaving with unsaved changes writes the autosave at once");
				EditorAutosave.Pick(EditorAutosave.DiscardChoice);
				Check(ref ok, !File.Exists(autosave), "Throw them away deletes it");

				// The X on one island's row (TODO 4e): only that autosave goes; the window closes when none are left
				UndoRedoManager.Execute(new NoopCommand());
				EditorAutosave.WriteNow();
				int others = EditorAutosave.Waiting().Count(n => n != AutosaveIsland);
				yield return EditorAutosave.Offer();
				Check(ref ok, ChoiceWindow.IsOpen && ChoiceWindow.Shown.Any(c => c.Value == AutosaveIsland && c.OnDelete != null)
					&& ChoiceWindow.Shown.All(c => c.Value != EditorAutosave.DiscardChoice || c.OnDelete == null), "each island's row has an X, Throw them away / Not now don't");
				Check(ref ok, ChoiceWindow.DeleteValue(AutosaveIsland) && !File.Exists(autosave) && !ChoiceWindow.Values.Contains(AutosaveIsland),
					"the X throws away only that island's autosave and takes its row away");
				Check(ref ok, ChoiceWindow.IsOpen == (others > 0), "the window closes when no unsaved work is left (others waiting: " + others + ")");
				ChoiceWindow.Close();
				EditorAutosave.Opened(AutosaveIsland, false);

				// Saving an island that saved worlds have: said once (not at every save)
				File.WriteAllLines(worldFile, new[] { "# Custom islands in world 'CI Told': name|x|y|z", "@auto=on", AutosaveIsland + "|0|0|0|" });
				int told = DynamicIslands.WorldsNoticeCount;
				DynamicIslands.SaveIsland(AutosaveIsland);
				DynamicIslands.SaveIsland(AutosaveIsland);
				Check(ref ok, DynamicIslands.WorldsNoticeCount == told + 1, "saving an island a saved world has: told once (" + (DynamicIslands.WorldsNoticeCount - told) + " time(s) for two saves)");
			}
			finally
			{
				EditorAutosave.IntervalSeconds = interval;
				foreach (string f in new[] { island, autosave, worldFile }) try { if (File.Exists(f)) File.Delete(f); } catch { }
				if (DynamicIslands.InEditor()) DynamicIslands.NewIsland();
			}
			if (ok) Log("PASS: autosave"); else Fail("autosave");
		}

		[ConsoleCommand(name: "CINames", docs: "Dev, editor: names players type (TEST_CATALOGUE UP2) - Windows' device names (CON, nul.x, COM9), a trailing dot or space, 61 characters, \\ / : are refused with a reason and write nothing; Swedish letters, Chinese, an emoji and 60 characters save, open and delete; a pack id from the title 'Con' is not a device name. Cleans up")]
		public static void NamesCommand()
		{
			bool ok = true;
			if (!DynamicIslands.InEditor()) { Fail("names: open the editor first"); return; }
			string before = DynamicIslands.currentIslandName;
			foreach (string bad in new[] { "CON", "con", "Nul", "nul.x", "COM9", "lpt1", "aux.island", "island.", "island ", " island", new string('a', FileNames.MaxLength + 1), "a/b", "a:b", "" })
			{
				bool saved;
				try { saved = DynamicIslands.SaveIsland(bad); }
				catch (Exception e) { saved = true; Log("NAMES threw for '" + bad + "': " + e.Message); }
				string problem = FileNames.Problem(bad);
				Check(ref ok, !saved && problem != null && !TextPromptWindow.ValidName(bad), "refused: '" + bad + "' (" + problem + ")");
			}
			Check(ref ok, !File.Exists(Path.Combine(DynamicIslands.assetpath, "island..island")) && !File.Exists(Path.Combine(DynamicIslands.assetpath, "island .island")), "nothing written for the refused names");
			foreach (string good in new[] { "Ã…sa's Ã¶-land", "å°å³¶", "cove \U0001F3DD", new string('b', FileNames.MaxLength), "con island", "a.b" })
			{
				bool saved = false, opened = false;
				try { saved = DynamicIslands.SaveIsland(good); opened = saved && File.Exists(IslandSpawner.PathFor(good)) && IslandFile.Load(IslandSpawner.PathFor(good)) != null; }
				catch (Exception e) { Log("NAMES threw for '" + good + "': " + e.Message); }
				Check(ref ok, saved && opened && IslandSpawner.ListSavedIslands().Contains(good), "saved, listed and read back: '" + good + "'");
				try { if (File.Exists(IslandSpawner.PathFor(good))) File.Delete(IslandSpawner.PathFor(good)); } catch { }
			}
			Check(ref ok, !FileNames.IsReserved(LibraryPack.IdFrom("Con")) && !FileNames.IsReserved(LibraryPack.IdFrom("NUL")) && LibraryPack.IdFrom("Palm Cove") == "palm-cove", "pack ids: 'Con' -> " + LibraryPack.IdFrom("Con") + ", 'Palm Cove' -> " + LibraryPack.IdFrom("Palm Cove"));
			Check(ref ok, !LibraryPack.IsSafeFileName("com5.island") && !LibraryPack.IsSafeFileName("nul.plan") && LibraryPack.IsSafeFileName("Palm Cove.island"), "pack file names: device names refused");
			// An island of the player's named like a host's copy (12 digits: a date and time), and a real copy
			const string own = "camp_202609281530";
			string copy = null;
			try
			{
				Check(ref ok, DynamicIslands.SaveIsland(own), "saved '" + own + "'");
				string hash = IslandNetwork.HashOf(own);
				copy = IslandNetwork.DownloadName(own, hash);
				File.Copy(IslandSpawner.PathFor(own), IslandSpawner.PathFor(copy), true);
				Check(ref ok, !IslandNetwork.IsDownloadName(own) && IslandNetwork.IsDownloadName(copy), "'" + own + "' is the player's own, '" + copy + "' a host's copy");
				Check(ref ok, ChoiceWindow.Islands().Select(c => c.Value).Contains(own) && !ChoiceWindow.Islands().Select(c => c.Value).Contains(copy), "the own one is listed among the player's islands, the copy isn't");
				int removed = LibraryPack.RemoveUnusedHostCopies();
				Check(ref ok, File.Exists(IslandSpawner.PathFor(own)) && !File.Exists(IslandSpawner.PathFor(copy)), "Remove unused removes the copy (" + removed + "), never the player's own");
			}
			finally
			{
				foreach (string n in new[] { own, copy }) try { if (n != null && File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n)); } catch { }
			}
			DynamicIslands.currentIslandName = before;
			if (ok) Log("PASS: names"); else Fail("names");
		}

		[ConsoleCommand(name: "CISafeWrite", docs: "Dev, anywhere: a file written with SafeFile is whole or as it was - a write that fails half way (here: its .tmp can't be written) leaves the old file untouched; a good write replaces it and leaves no .tmp (TEST_CATALOGUE UP5)")]
		public static void SafeWriteCommand()
		{
			bool ok = true;
			string path = Path.Combine(DynamicIslands.assetpath, "cisafewrite.txt"), tmp = path + ".tmp";
			try
			{
				File.WriteAllText(path, "the old content");
				Directory.CreateDirectory(tmp); // (a folder where the .tmp would go: the write fails, as on a full disk)
				bool threw = false;
				try { SafeFile.WriteAllText(path, "new"); } catch { threw = true; }
				Check(ref ok, threw && File.ReadAllText(path) == "the old content", "a failed write leaves the old file whole");
				Directory.Delete(tmp);
				SafeFile.WriteAllLines(path, new[] { "line 1", "line 2" });
				Check(ref ok, File.ReadAllLines(path).SequenceEqual(new[] { "line 1", "line 2" }) && !File.Exists(tmp), "a good write replaces it, no .tmp left");
			}
			finally
			{
				try { if (Directory.Exists(tmp)) Directory.Delete(tmp); if (File.Exists(tmp)) File.Delete(tmp); if (File.Exists(path)) File.Delete(path); } catch { }
			}
			if (ok) Log("PASS: safe write"); else Fail("safe write");
		}

		[ConsoleCommand(name: "CICulture", docs: "Dev, anywhere: Raft runs as on a Windows set to another language (TEST_CATALOGUE UP3): CICulture tr-TR / th-TH / de-DE / sv-SE, or CICulture off. Then run the unit suites; CICulture check writes and reads back an island, a plan, the world rules and a library info under it")]
		public static void CultureCommand(string[] args)
		{
			string c = args != null && args.Length > 0 ? args[0] : "off";
			if (c == "check") { CultureCheck(); return; }
			var ci = c == "off" ? originalCulture ?? System.Globalization.CultureInfo.InvariantCulture : new System.Globalization.CultureInfo(c);
			if (originalCulture == null) originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
			System.Threading.Thread.CurrentThread.CurrentCulture = ci;
			System.Threading.Thread.CurrentThread.CurrentUICulture = ci;
			Log("Culture: " + ci.Name + " (1.5 is written '" + 1.5f.ToString() + "', 'I'.ToLower() is '" + "I".ToLower() + "', the year " + DateTime.Now.ToString("yyyy") + ")");
		}

		static System.Globalization.CultureInfo originalCulture;

		static void CultureCheck()
		{
			bool ok = true;
			string culture = System.Threading.Thread.CurrentThread.CurrentCulture.Name;
			// A plan with numbers in it, written and read back
			var plan = new WorldPlan { Name = "ci culture plan", Description = "Ã–lÃ§Ã¼ 1.5 km", Random = false };
			plan.Rules.Add(new IntroRule { Id = "Ä°sland", What = "type", WhatArg = "sandbar", When = "km", WhenArg = "1.5", Where = "ahead", Distance = 350.5f });
			plan.Save();
			WorldPlan back = WorldPlan.Load("ci culture plan");
			Check(ref ok, back != null && back.Rules.Count == 1 && back.Rules[0].WhenArg == "1.5" && Mathf.Abs(back.Rules[0].Distance - 350.5f) < 0.01f && back.Description == "Ã–lÃ§Ã¼ 1.5 km",
				"a plan with 1.5 km and 350.5 m reads back the same (" + (back != null && back.Rules.Count > 0 ? back.Rules[0].WhenArg + " km, " + back.Rules[0].Distance + " m" : "not read") + ")");
			try { File.Delete(WorldPlan.PathFor("ci culture plan")); } catch { }
			// Library info (numbers, dates) and version comparison
			var info = new LibraryInfo { id = LibraryPack.IdFrom("Ä°stanbul Island"), title = "Ä°stanbul Island", author = "CI", version = 3, summary = "s", minModVersion = "2.10" };
			LibraryInfo info2 = LibraryInfo.FromJson(info.ToJson());
			Check(ref ok, info2 != null && info2.version == 3 && info2.id == info.id && info.id.Length > 0, "a library info reads back (id '" + info.id + "')");
			Check(ref ok, LibraryPack.CompareVersions("2.10", "2.9") > 0 && LibraryPack.CompareVersions("3.0", "3.0") == 0, "versions compared as numbers");
			// A file name check with a dotted I (Turkish lower/upper case)
			Check(ref ok, FileNames.IsReserved("con") && FileNames.IsReserved("COM1.island") && !FileNames.IsReserved("Ä°sland"), "device names found in any culture");
			if (ok) Log("PASS: culture " + culture); else Fail("culture " + culture);
		}

		[ConsoleCommand(name: "CIFakeVersion", docs: "Dev, anywhere: this PC pretends to have another version of the mod (CIFakeVersion 2.9), or its own again (CIFakeVersion off)")]
		public static void FakeVersionCommand(string[] args)
		{
			string v = args != null && args.Length > 0 ? args[0] : "off";
			LibraryPack.TestVersion = v == "off" ? null : v;
			Log("Fake version: " + LibraryPack.ModVersion);
		}

		[ConsoleCommand(name: "CICaveRims", docs: "Dev, in a world: every cave piece on the loaded custom islands meets the ground all round - no rim floating over it to see into the shell under (AU83). Lists each piece and its largest gap")]
		public static void CaveRimsCommand()
		{
			bool ok = true;
			int pieces = 0;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading))
				foreach (Transform t in e.Root.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.activeInHierarchy && RaftProps.Get(t.name) != null && RaftProps.Get(t.name).IsCave))
				{
					pieces++;
					string at;
					float gap = DenRimGap(t, out at);
					Check(ref ok, gap < 0.6f, "'" + e.Name + "': " + t.name + " - " + at);
				}
			Check(ref ok, pieces > 0, pieces + " cave pieces on the loaded islands");
			if (ok) Log("PASS: cave rims meet the ground"); else Fail("cave rims meet the ground");
		}

		[ConsoleCommand(name: "CIAnimalsDry", docs: "Dev, in a world with a custom island with land animals loaded: no animal may walk in the sea - no NavMesh below the waterline at the islands, no land animal below it (AU84)")]
		public static void AnimalsDryCommand()
		{
			bool ok = true;
			UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
			int islands = 0;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(x => x.Root != null && !x.Loading && !WorldRandomizer.IsExtras(x)))
			{
				IslandSettings s = e.Root.GetComponent<IslandSettings>();
				if (s == null || e.Root.GetComponent<UnityEngine.AI.NavMeshSurface>() == null) continue;
				islands++;
				float sea = e.Root.transform.position.y + s.WaterLevel, r = Mathf.Max(60f, CustomIslandSpawner.LandRadius(e.Name) + 40f);
				int wet = tri.vertices.Count(v => new Vector2(v.x - e.Position.x, v.z - e.Position.z).magnitude < r && v.y < sea - 1f);
				Check(ref ok, wet == 0, "'" + e.Name + "': " + wet + " NavMesh point(s) more than 1 m under the sea");
			}
			int animals = 0;
			foreach (AI_NetworkBehaviour a in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>())
			{
				if (a == null || a.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>() == null) continue;
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Root != null && new Vector2(x.Position.x - a.transform.position.x, x.Position.z - a.transform.position.z).magnitude < Mathf.Max(60f, CustomIslandSpawner.LandRadius(x.Name) + 40f));
				IslandSettings s = e != null ? e.Root.GetComponent<IslandSettings>() : null;
				if (s == null) continue;
				animals++;
				float sea = e.Root.transform.position.y + s.WaterLevel;
				Check(ref ok, a.transform.position.y > sea - 1f, a.name + " on '" + e.Name + "' at " + (a.transform.position.y - sea).ToString("F1") + " m from the sea's surface");
			}
			Check(ref ok, islands > 0, islands + " island(s) with a NavMesh, " + animals + " land animal(s) looked at");
			if (ok) Log("PASS: animals dry"); else Fail("animals dry");
		}

		[ConsoleCommand(name: "CIFakeBuild", docs: "Dev, anywhere: this PC pretends to be another build of the same version (CIFakeBuild 0badbeef), or its own again (CIFakeBuild off) - AU81")]
		public static void FakeBuildCommand(string[] args)
		{
			string v = args != null && args.Length > 0 ? args[0] : "off";
			IslandNetwork.TestBuild = v == "off" ? null : v;
			Log("Fake build: " + IslandNetwork.Build);
		}

		[ConsoleCommand(name: "CIVersionCheck", docs: "Dev, in a world with two players: CIVersionCheck same = no version difference was reported; CIVersionCheck differ <version> = the difference with that version was reported here; CIVersionCheck build <build> = another build of the same version was reported (AU81)")]
		public static void VersionCheckCommand(string[] args)
		{
			bool ok = true;
			string mode = args != null && args.Length > 0 ? args[0] : "same";
			string notice = IslandNetwork.VersionNotice ?? "";
			if (mode == "same") Check(ref ok, notice.Length == 0, "no version difference reported (" + notice + ")");
			else if (mode == "build") Check(ref ok, args.Length > 1 && notice.Contains("another build") && notice.Contains(args[1]), "the other build was reported: " + notice);
			else Check(ref ok, args.Length > 1 && notice.Contains("Custom Islands " + args[1]), "the version difference was reported: " + notice);
			if (ok) Log("PASS: version check"); else Fail("version check");
		}

		[ConsoleCommand(name: "CIRaftHeight", docs: "Dev, world: logs RAFT <deck top above the sea> <raft object y> <player y> <player grounded> - the raft must float on the sea (about 0.3 m)")]
		public static void RaftHeightCommand()
		{
			Raft raftObj = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftObj == null) { Fail("raft height: no raft"); return; }
			var tops = raftObj.GetComponentsInChildren<Block>(true).Where(b => b != null && b.buildableItem != null && b.buildableItem.UniqueName.StartsWith("Block_Foundation"))
				.Select(b => b.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.enabled && c.gameObject.activeInHierarchy).Select(c => c.bounds.max.y).DefaultIfEmpty(float.MinValue).Max()).ToList();
			// (the highest foundation: which block, where on the raft, which collider reaches up)
			Block top = raftObj.GetComponentsInChildren<Block>(true).Where(b => b != null && b.buildableItem != null && b.buildableItem.UniqueName.StartsWith("Block_Foundation"))
				.OrderByDescending(b => b.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.enabled && c.gameObject.activeInHierarchy).Select(c => c.bounds.max.y).DefaultIfEmpty(float.MinValue).Max()).FirstOrDefault();
			if (top != null)
			{
				Collider hc = top.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).OrderByDescending(c => c.bounds.max.y).FirstOrDefault();
				Log("RAFTTOP " + top.name + " local " + raftObj.transform.InverseTransformPoint(top.transform.position).ToString("F1") + ", collider " + (hc != null ? hc.name + " (" + hc.GetType().Name + ", on " + hc.gameObject.name + ", bounds " + hc.bounds.min.y.ToString("F1") + ".." + hc.bounds.max.y.ToString("F1") + ", enabled " + hc.enabled + ")" : "none") +
					"; foundations " + raftObj.GetComponentsInChildren<Block>(true).Count(b => b != null && b.buildableItem != null && b.buildableItem.UniqueName.StartsWith("Block_Foundation")) + ", blocks " + raftObj.GetComponentsInChildren<Block>(true).Length);
			}
			Network_Player p = RAPI.GetLocalPlayer();
			Rigidbody body = raftObj.body;
			tops = tops.Where(y => y > -1000f).ToList(); // (Raft switches its blocks' own colliders off for one of the whole raft: then the raft's own height)
			Log("RAFT " + (tops.Count > 0 ? tops.Max() : raftObj.transform.position.y).ToString("F2") + " " + raftObj.transform.position.y.ToString("F2") + " " + (p != null ? p.transform.position.y.ToString("F2") : "?") + " " +
				(p != null && p.PersonController != null ? p.PersonController.controller.isGrounded.ToString() : "?") + " velocity " + (body != null ? body.velocity.ToString("F2") : "?") + " kinematic " + (body != null && body.isKinematic));
		}

		[ConsoleCommand(name: "CIRaftDiag", docs: "Dev, world: RAFTDIAG - the raft's body (position, speed, spin, gravity, kinematic, constraints), its buoyancy centre, the sea's height (Raft's water object), Raft's world shifts so far, the player - why a raft ends up far above or below the sea")]
		public static void RaftDiagCommand()
		{
			Raft raftObj = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftObj == null || raftObj.body == null) { Fail("raft diag: no raft"); return; }
			Rigidbody b = raftObj.body;
			string water = "?";
			try
			{
				Type wt = HarmonyLib.AccessTools.TypeByName("UltimateWater.Water");
				UnityEngine.Object w = wt != null ? UnityEngine.Object.FindObjectOfType(wt) : null;
				if (w != null) water = ((Component)w).transform.position.y.ToString("F2");
			}
			catch (Exception e) { water = e.GetType().Name; }
			string buoy = "?";
			try { Buoyancy by = raftObj.GetComponentInChildren<Buoyancy>(true); if (by != null) buoy = by.CenterPointY.ToString("F2") + (by.enabled ? "" : " (off)"); } catch (Exception e) { buoy = e.GetType().Name; }
			string shifts = "?";
			try { WorldShiftManager ws = UnityEngine.Object.FindObjectOfType<WorldShiftManager>(); if (ws != null) shifts = HarmonyLib.Traverse.Create(ws).Field("shiftCounter").GetValue<int>().ToString(); } catch (Exception e) { shifts = e.GetType().Name; }
			Network_Player p = RAPI.GetLocalPlayer();
			Log("RAFTDIAG body " + b.position.ToString("F1") + " vel " + b.velocity.ToString("F2") + " spin " + b.angularVelocity.ToString("F2") + " gravity " + b.useGravity + " kinematic " + b.isKinematic + " constraints " + b.constraints +
				" | raft object y " + raftObj.transform.position.y.ToString("F2") + " | buoyancy centre y " + buoy + " | sea (water object) y " + water + " | world shifts " + shifts +
				" | player " + (p != null ? p.transform.position.ToString("F1") + " " + (p.PersonController != null ? p.PersonController.controllerType.ToString() : "") : "none") + " | t " + Time.time.ToString("F0"));
		}

		[ConsoleCommand(name: "CIRaftCalm", docs:"Dev, world: the raft back on the sea at rest (no speed, no spin, upright at sea level) - after a test dragged it at test speed")]
		public static void RaftCalmCommand()
		{
			Raft raftObj = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftObj == null || raftObj.body == null) { Fail("raft calm: no raft"); return; }
			Rigidbody body = raftObj.body;
			body.velocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			Vector3 at = body.position;
			body.position = new Vector3(at.x, 0f, at.z);
			body.rotation = Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f);
			Network_Player p = RAPI.GetLocalPlayer();
			if (p != null) p.transform.position = body.position + Vector3.up * 3f;
			Log("Raft calm: was at y " + at.y.ToString("F2") + ", now on the sea at rest");
		}

		[ConsoleCommand(name: "CIMemory", docs: "Dev, anywhere: logs the memory in use (MEMORY <managed MB> <Unity allocated MB> <textures> <meshes> <game objects>) after a full collection")]
		public static void MemoryCommand()
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			long managed = GC.GetTotalMemory(true) / (1024 * 1024);
			long unity = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
			Log("MEMORY " + managed + " " + unity + " " + Resources.FindObjectsOfTypeAll<Texture>().Length + " " + Resources.FindObjectsOfTypeAll<Mesh>().Length + " " + Resources.FindObjectsOfTypeAll<GameObject>().Length);
		}

		[ConsoleCommand(name: "CIPlanEdit", docs: "Dev, anywhere: changes the test plan 'CI Copy Plan' (CIPlanCopyPrep make) as a player would in World Plans: CIPlanEdit add = a 4th rule; CIPlanEdit more = a 5th")]
		public static void PlanEditCommand(string[] args)
		{
			string path = WorldPlan.PathFor(LibPlanCopy);
			if (!File.Exists(path)) { Fail("plan edit: no plan '" + LibPlanCopy + "'"); return; }
			string text = File.ReadAllText(path);
			if (!text.Contains("rule = added")) text += "rule = added | type:sandbar | km:900 | ahead:500 | | Added later\n";
			if (args != null && args.Contains("more") && !text.Contains("rule = more")) text += "rule = more | type:wreck | km:950 | ahead:500 | | More\n";
			File.WriteAllText(path, text);
			WorldPlan p = WorldPlan.Load(LibPlanCopy);
			Log("Plan edit: '" + LibPlanCopy + "' has " + (p != null ? p.Rules.Count : 0) + " rule(s)");
		}

		[ConsoleCommand(name: "CIPlanEditCheck", docs: "Dev, world (host): CIPlanEditCheck edited <rules> = the changed plan file plays (the player was told) with that many rules; CIPlanEditCheck copy <rules> = the world's own copy plays (another player made the world)")]
		public static void PlanEditCheckCommand(string[] args)
		{
			bool ok = true;
			string mode = args != null && args.Length > 0 ? args[0] : "edited";
			int rules = args != null && args.Length > 1 ? int.Parse(args[1]) : 4;
			int count = WorldDirector.Plan != null ? WorldDirector.Plan.Rules.Count : 0;
			Check(ref ok, WorldDirector.PlanName == LibPlanCopy && count == rules, "the world plays '" + WorldDirector.PlanName + "' with " + count + " rule(s) (expected " + rules + ")");
			if (mode == "edited") Check(ref ok, WorldDirector.PlanWasEdited && !WorldDirector.PlanFromWorld && WorldDirector.PlanOwner != 0, "the changed plan file plays (edited " + WorldDirector.PlanWasEdited + ", owner " + WorldDirector.PlanOwner + ")");
			else Check(ref ok, !WorldDirector.PlanWasEdited && WorldDirector.PlanFromWorld, "the world's own copy plays (edited " + WorldDirector.PlanWasEdited + ", from the world " + WorldDirector.PlanFromWorld + ", owner " + WorldDirector.PlanOwner + ")");
			if (ok) Log("PASS: plan edit check"); else Fail("plan edit check");
		}

		[ConsoleCommand(name: "CIPlanOwner", docs: "Dev, world (host): pretends another player made this world (CIPlanOwner <steam id>), saved with the next save")]
		public static void PlanOwnerCommand(string[] args)
		{
			ulong id;
			if (args == null || args.Length == 0 || !ulong.TryParse(args[0], out id)) { Fail("plan owner: CIPlanOwner <steam id>"); return; }
			WorldDirector.PlanOwner = id;
			Log("Plan owner: " + id);
		}

		[ConsoleCommand(name: "CIRollbackMark", docs: "Dev, world (host): leaves the made-up island <mark> out of this world (a change of the mod's state to find after loading): CIRollbackMark [mark]")]
		public static void RollbackMarkCommand(string[] args)
		{
			string mark = args != null && args.Length > 0 ? args[0] : "cirollback";
			WorldIslands.Set(mark, false);
			Log("Rollback mark: '" + mark + "' left out");
		}

		[ConsoleCommand(name: "CIRollbackCheck", docs: "Dev, world (host): CIRollbackCheck saved <mark> = Raft's newest save folder has the mod's copy with Raft's stamp and the island <mark> left out, and an older save's copy doesn't; CIRollbackCheck older <mark> = an older save was loaded: <mark> takes part again; CIRollbackCheck newest <mark> = the newest: <mark> left out")]
		public static void RollbackCheckCommand(string[] args)
		{
			bool ok = true;
			string mode = args != null && args.Length > 0 ? args[0] : "saved";
			string mark = args != null && args.Length > 1 ? args[1] : "cirollback";
			string folder = WorldCopy.RaftWorldFolder;
			if (mode == "saved")
			{
				string[] saves = folder != null ? Directory.GetDirectories(folder) : new string[0];
				string latest = saves.FirstOrDefault(d => d.EndsWith("-Latest"));
				string[] lines = latest != null && File.Exists(Path.Combine(latest, WorldCopy.FileName)) ? File.ReadAllLines(Path.Combine(latest, WorldCopy.FileName)) : new string[0];
				Check(ref ok, lines.Any(l => l.StartsWith("@raftsave=")) && lines.Any(l => l.StartsWith("@islandsoff=") && l.Contains(mark)), "the newest save's folder has the mod's copy with Raft's stamp and '" + mark + "' left out");
				var older = saves.Where(d => d != latest && File.Exists(Path.Combine(d, WorldCopy.FileName))).Select(d => File.ReadAllLines(Path.Combine(d, WorldCopy.FileName))).ToList();
				Check(ref ok, older.Any(o => o.Any(l => l.StartsWith("@raftsave=")) && !o.Any(l => l.StartsWith("@islandsoff=") && l.Contains(mark))), "an older save's folder has its own copy, without '" + mark + "' (" + older.Count + " older copies)");
			}
			else if (mode == "absent")
				Check(ref ok, WorldIslands.TakesPart(mark) && WorldIslands.Off.Count == 0, "nothing of another world's islands left out here: '" + mark + "' takes part (" + WorldIslands.Describe() + ")");
			else if (mode == "older")
				Check(ref ok, WorldIslands.TakesPart(mark) && WorldCopy.LastOlderSave.Length > 0, "an older save loaded: its state (" + WorldCopy.LastSource + "), '" + mark + "' takes part again");
			else
				Check(ref ok, !WorldIslands.TakesPart(mark) && WorldCopy.LastOlderSave.Length == 0, "the newest save loaded (" + WorldCopy.LastSource + "): '" + mark + "' left out");
			if (ok) Log("PASS: rollback check"); else Fail("rollback check");
		}
	}
}
