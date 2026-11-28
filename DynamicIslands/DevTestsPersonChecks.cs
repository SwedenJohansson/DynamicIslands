using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DynamicIslands
{
	/// <summary>
	/// person-checks 11-23: the checks ROADMAP R14 / TEST_CATALOGUE V4 left "by hand", done by a job script instead.
	/// CIKillSave: Raft is killed by pid while it saves an island, the next start must leave every island whole.
	/// CIBrokenPatch: a patch that no longer fits Raft (as after a Raft update), switched on by a flag file before the
	/// start - the main menu box names the feature, every other patch works.
	/// </summary>
	public static partial class DevTests
	{
		#region R14a / V4a - Raft killed while it saves an island

		const string KillSaveFolderName = "ci-killsave";
		const int KillSaveExtra = 7; // the second version of the island has this many objects more
		static string KillSaveFolder { get { return Path.Combine(DynamicIslands.assetpath, KillSaveFolderName); } }
		static string KillSaveMain { get { return Path.Combine(KillSaveFolder, "main" + IslandFile.Extension); } }

		[ConsoleCommand(name: "CIKillSave", docs: "Dev, anywhere (R14/V4 kill-while-saving, run by a job that kills Raft by pid): CIKillSave prep [objects] [seconds] - an island of <objects> (60000) in Mods\\DynamicIslands\\ci-killsave saved again and again for <seconds> (90), two versions in turn (objects, objects+7), logs 'KILLSAVE looping' then each 10th save; CIKillSave first [objects] [seconds] - each save a new island (first-<n>, a first save); CIKillSave check main|first [objects] [clean] - after the next start: no .tmp left anywhere in the mod's folder, main.island whole with one of the two versions / every first-<n>.island whole, the unfinished first save set aside; clean removes the test's files")]
		public static void KillSaveCommand(string[] args)
		{
			string mode = args != null && args.Length > 0 ? args[0] : "check";
			int objects = 60000, seconds = 90;
			if (mode == "prep" || mode == "first")
			{
				if (args.Length > 1) int.TryParse(args[1], out objects);
				if (args.Length > 2) int.TryParse(args[2], out seconds);
				StartTest(KillSaveLoop(mode == "first", objects, seconds));
			}
			else
			{
				int n;
				int baseCount = args != null && args.Length > 2 && int.TryParse(args[2], out n) ? n : objects;
				StartTest(KillSaveCheck(args != null && args.Length > 1 ? args[1] : "main", baseCount, args != null && args.Contains("clean")));
			}
		}

		static IslandFile KillSaveIsland(int objects)
		{
			const int res = 129;
			var heights = new float[res, res];
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++) heights[y, x] = KillSaveHeight(x, y);
			var rnd = new System.Random(1123);
			string[] names = { "ci_block", "ci_palm", "ci_rock", "ci_crate" };
			var list = new List<IslandObject>(objects + KillSaveExtra);
			for (int i = 0; i < objects; i++)
				list.Add(new IslandObject { Name = names[i % names.Length], Position = new Vector3((float)rnd.NextDouble() * 120f, (float)rnd.NextDouble() * 40f, (float)rnd.NextDouble() * 120f), EulerRotation = new Vector3(0, (float)rnd.NextDouble() * 360f, 0) });
			return new IslandFile { Name = "ci killsave", TerrainSize = new Vector3(120, 40, 120), HeightmapResolution = res, Heights = heights, WaterLevel = DynamicIslands.EditorWaterLevel, Objects = list };
		}

		static float KillSaveHeight(int x, int y) { return ((x * 7 + y * 13) % 100) / 100f; }

		static IEnumerator KillSaveLoop(bool first, int objects, int seconds)
		{
			Directory.CreateDirectory(KillSaveFolder);
			IslandFile a = KillSaveIsland(objects);
			IslandFile b = KillSaveIsland(objects);
			for (int i = 0; i < KillSaveExtra; i++) b.Objects.Add(new IslandObject { Name = "ci_extra", Position = new Vector3(i, 1, i) });
			var watch = Stopwatch.StartNew();
			if (!first) { a.Save(KillSaveMain); Log("KILLSAVE first save of main.island (" + objects + " objects): " + watch.ElapsedMilliseconds + " ms, " + new FileInfo(KillSaveMain).Length / 1024 + " KB"); }
			yield return null;
			int saves = 0, next = 0;
			while (next < 10000 && File.Exists(Path.Combine(KillSaveFolder, "first-" + next + IslandFile.Extension))) next++;
			Log("KILLSAVE looping (" + (first ? "first saves" : "main.island, " + objects + " / " + (objects + KillSaveExtra) + " objects") + ", up to " + seconds + " s)");
			var all = Stopwatch.StartNew();
			while (all.Elapsed.TotalSeconds < seconds)
			{
				// (saves back to back for half a second, then one frame: Raft spends nearly all its time inside a save, so a
				// kill at a random moment lands mid-write)
				var frame = Stopwatch.StartNew();
				while (frame.ElapsedMilliseconds < 500)
				{
					IslandFile island = saves % 2 == 0 ? b : a;
					if (first) island.Save(Path.Combine(KillSaveFolder, "first-" + (next++) + IslandFile.Extension));
					else island.Save(KillSaveMain);
					saves++;
					if (saves % 10 == 0) Log("KILLSAVE saved " + saves + " (last " + island.Objects.Count + " objects)");
				}
				yield return null;
			}
			Log("KILLSAVE loop ended without a kill after " + saves + " saves");
		}

		static IEnumerator KillSaveCheck(string mode, int objects, bool clean)
		{
			bool ok = true;
			yield return null;
			string[] tmps = Directory.GetFiles(DynamicIslands.assetpath, "*.tmp", SearchOption.AllDirectories);
			Check(ref ok, tmps.Length == 0, "no .tmp left anywhere in the mod's folder after the start (" + string.Join(", ", tmps.Select(Path.GetFileName).ToArray()) + ")");
			string unfinishedFolder = Path.Combine(Path.Combine(DynamicIslands.assetpath, IslandFilesWindow.DeletedFolderName), "unfinished");
			if (mode == "main")
			{
				IslandFile back = null;
				string err = null;
				try { back = IslandFile.Load(KillSaveMain); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
				Check(ref ok, back != null, "main.island loads whole" + (err != null ? " - " + err : ""));
				if (back != null)
				{
					int n = back.Objects.Count, extra = back.Objects.Count(o => o.Name == "ci_extra");
					Check(ref ok, (n == objects && extra == 0) || (n == objects + KillSaveExtra && extra == KillSaveExtra), "it is one whole version: " + n + " objects, " + extra + " extra (" + objects + " or " + (objects + KillSaveExtra) + " expected)");
					bool heights = back.HeightmapResolution == 129;
					for (int y = 0; heights && y < 129; y += 7)
						for (int x = 0; heights && x < 129; x += 5) heights = Mathf.Abs(back.Heights[y, x] - KillSaveHeight(x, y)) < 0.001f;
					Check(ref ok, heights, "its ground is the one saved (heights read back)");
				}
			}
			else
			{
				string[] files = Directory.Exists(KillSaveFolder) ? Directory.GetFiles(KillSaveFolder, "first-*" + IslandFile.Extension) : new string[0];
				var broken = new List<string>();
				foreach (string f in files)
					try { IslandFile.FromBytes(File.ReadAllBytes(f), Path.GetFileName(f)); } catch (Exception e) { broken.Add(Path.GetFileName(f) + " (" + e.GetType().Name + ")"); }
				Check(ref ok, files.Length > 0 && broken.Count == 0, files.Length + " first saves, every one whole" + (broken.Count > 0 ? " - broken: " + string.Join(", ", broken.ToArray()) : ""));
				string[] aside = Directory.Exists(unfinishedFolder) ? Directory.GetFiles(unfinishedFolder, "first-*.unfinished") : new string[0];
				Log("KILLSAVE set aside as unfinished: " + aside.Length + (aside.Length > 0 ? " (" + Path.GetFileName(aside.Last()) + ")" : ""));
			}
			if (clean)
			{
				// (only what this test wrote: its folder and the unfinished first saves set aside from it)
				try { if (Directory.Exists(KillSaveFolder)) Directory.Delete(KillSaveFolder, true); } catch (Exception e) { Log("clean: " + e.Message); }
				if (Directory.Exists(unfinishedFolder))
					foreach (string f in Directory.GetFiles(unfinishedFolder, "first-*.unfinished")) try { File.Delete(f); } catch { }
				Log("KILLSAVE cleaned up");
			}
			if (ok) Log("PASS: kill while saving (" + mode + ")"); else Fail("kill while saving (" + mode + ")");
		}

		#endregion

		#region R14b / V4b - a patch that no longer fits Raft

		public const string BreakPatchFlag = "ci_breakpatch.txt";

		[ConsoleCommand(name: "CIBrokenPatch", docs: "Dev, main menu (R14/V4, run by a job): CIBrokenPatch on - this start had Mods\\DynamicIslands\\ci_breakpatch.txt, so the test patch CIBrokenReceiverPatch aimed at a Raft method that isn't there failed like a patch after a Raft update: it alone is marked failed, every other patch applied, and the main menu shows 'Some parts of Custom Islands are off' naming the Receiver; CIBrokenPatch off - a start without the flag: nothing failed, no box")]
		public static void BrokenPatchCommand(string[] args) { StartTest(BrokenPatchRoutine(args != null && args.Length > 0 && args[0] == "on")); }

		static IEnumerator BrokenPatchRoutine(bool on)
		{
			bool ok = true;
			// (the main menu's box comes when the menu appears: give it a moment after a fresh start)
			for (int i = 0; i < 300 && on && !InfoWindow.IsOpen; i++) yield return null;
			int patchClasses = AccessTools.GetTypesFromAssembly(typeof(PatchHealth).Assembly).Count(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0);
			bool flag = File.Exists(Path.Combine(DynamicIslands.assetpath, BreakPatchFlag));
			Check(ref ok, flag == on, "the flag file is " + (flag ? "there" : "not there"));
			string failed = string.Join(", ", PatchHealth.FailedPatches.ToArray());
			if (on)
			{
				Check(ref ok, PatchHealth.FailedPatches.Count == 1 && PatchHealth.FailedPatches[0] == typeof(CIBrokenReceiverPatch).Name, "only the broken test patch failed (" + failed + ")");
				Check(ref ok, PatchHealth.Applied == patchClasses - 1, "every other patch applied (" + PatchHealth.Applied + " of " + patchClasses + " patch classes)");
				Check(ref ok, PatchHealth.Feature(typeof(CIBrokenReceiverPatch).Name) == "the Receiver", "it is named as '" + PatchHealth.Feature(typeof(CIBrokenReceiverPatch).Name) + "'");
				Check(ref ok, InfoWindow.IsOpen && string.Equals(InfoWindow.Title, "Some parts of Custom Islands are off", StringComparison.OrdinalIgnoreCase), "the main menu shows the box: '" + InfoWindow.Title + "'");
				string body = InfoWindow.Body ?? "";
				Check(ref ok, body.Contains("•  the Receiver") && body.Contains("Everything else works"), "the box names the Receiver and says everything else works");
				Check(ref ok, InfoWindow.ButtonNamed("Report a problem") != null && InfoWindow.ButtonNamed("Close") != null, "it has Report a problem and Close");
				if (InfoWindow.IsOpen) InfoWindow.Close();
				yield return null;
				Check(ref ok, !InfoWindow.IsOpen, "Close closes it");
			}
			else
			{
				Check(ref ok, PatchHealth.FailedPatches.Count == 0, "no patch failed (" + failed + ")");
				Check(ref ok, PatchHealth.Applied == patchClasses, "every patch applied (" + PatchHealth.Applied + " of " + patchClasses + ")");
				Check(ref ok, !(InfoWindow.IsOpen && InfoWindow.Title == "Some parts of Custom Islands are off"), "no 'parts are off' box");
			}
			// (a real Receiver patch still on: Harmony has our patches on Raft's methods)
			int ours = Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m) != null && Harmony.GetPatchInfo(m).Owners.Contains(DynamicIslands.HarmonyId));
			Check(ref ok, ours > 20, ours + " of Raft's methods carry the mod's patches");
			if (ok) Log("PASS: broken patch " + (on ? "on" : "off")); else Fail("broken patch " + (on ? "on" : "off"));
		}

		#endregion

		#region H7 picture check 11-23 - a "+n EXP" number stays behind the level box

		[ConsoleCommand(name: "CILevelFloatBehindBox", docs: "Dev, in a world (H7 picture check 11-23): a '+n EXP' number shown while the level up box is open draws behind the box, not over its title - every 'Exp' number comes before 'LevelBanner' on the levels canvas")]
		public static void LevelFloatBehindBoxCommand(string[] args) { StartTest(LevelFloatBehindBoxRoutine()); }

		static IEnumerator LevelFloatBehindBoxRoutine()
		{
			bool ok = true;
			LevelHud.Ensure();
			LevelHud.Announce("Level 2!", "test box", false);
			Vector3 at = Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 4f : Vector3.zero;
			LevelHud.Float("+7 EXP", at);
			LevelHud.Float("+3 EXP", at);
			yield return null;
			GameObject canvas = GameObject.Find("CustomIslands_Levels");
			Transform box = canvas != null ? canvas.transform.Find("LevelBanner") : null;
			Check(ref ok, box != null, "the levels canvas has its LevelBanner");
			if (box != null)
			{
				var exps = canvas.transform.Cast<Transform>().Where(c => c.name == "Exp").ToList();
				Check(ref ok, exps.Count >= 2, exps.Count + " EXP numbers on the canvas");
				int over = exps.Count(c => c.GetSiblingIndex() > box.GetSiblingIndex());
				Check(ref ok, over == 0, over + " EXP numbers drawn over the box (box at " + box.GetSiblingIndex() + ", numbers at " + string.Join(",", exps.Select(c => c.GetSiblingIndex().ToString()).ToArray()) + ")");
			}
			LevelHud.Clear();
			if (ok) Log("PASS: EXP numbers behind the level box"); else Fail("EXP numbers behind the level box");
		}

		#endregion
	}

	/// <summary>
	/// A Receiver patch aimed at a Raft method that isn't there - what a Raft update that renames a method does to a
	/// real patch. Only when Mods\DynamicIslands\ci_breakpatch.txt is there at the start (dev builds only: DevTests*.cs
	/// isn't in a release).
	/// </summary>
	[HarmonyPatch]
	static class CIBrokenReceiverPatch
	{
		static bool Prepare() { return File.Exists(Path.Combine(DynamicIslands.assetpath, DevTests.BreakPatchFlag)); }
		static MethodBase TargetMethod() { return typeof(Raft).GetMethod("MethodRemovedInARaftUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); }
		static void Postfix() { }
	}
}
