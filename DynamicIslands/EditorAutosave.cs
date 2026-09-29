using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CommandUndoRedo;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Keeps unsaved editor work when Raft closes before it was saved (a crash, Alt+F4, the task ended, the editor left
	/// without saving). While the island in the editor has changes that aren't saved, it is written every few minutes to
	/// Mods\DynamicIslands\autosave\&lt;name&gt;.island (outside the islands folder: never spawned, listed or shared), and
	/// when leaving to the main menu or quitting. Saving the island removes its autosave. When the editor opens and an
	/// autosave is newer than its island (or the island was never saved), the player is offered to open it, throw it away,
	/// or be asked next time.
	/// </summary>
	public static class EditorAutosave
	{
		/// <summary>Seconds between autosaves while there are unsaved changes (tests shorten it).</summary>
		public static float IntervalSeconds = 180f;
		const float CheckSeconds = 2f;

		public static string Folder { get { return Path.Combine(DynamicIslands.assetpath, "autosave"); } }
		public static string PathFor(string name) { return Path.Combine(Folder, name + IslandFile.Extension); }

		static string current;       // the island being edited
		static int savedAt;          // UndoRedoManager.Changes when it was opened or saved
		static bool restored;        // opened from its autosave: unsaved until the player saves it
		static float lastWrite, nextCheck;
		static bool quitHooked;

		/// <summary>The island in the editor has changes that aren't in its file.</summary>
		public static bool Unsaved { get { return restored || UndoRedoManager.Changes != savedAt; } }

		/// <summary>The last autosave written (tests): its path, or null.</summary>
		public static string LastWritten { get; private set; }

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [autosave] " + msg); }

		/// <summary>An island was opened (or a new one started): nothing unsaved yet (unless it came from its autosave).</summary>
		public static void Opened(string name, bool fromAutosave)
		{
			current = name;
			savedAt = UndoRedoManager.Changes;
			restored = fromAutosave;
			lastWrite = Time.unscaledTime;
		}

		/// <summary>The island was saved: its autosave (and the one under its old name, after Save as) isn't needed any more.</summary>
		public static void Saved(string name)
		{
			if (!string.IsNullOrEmpty(current)) Delete(current);
			Delete(name);
			Opened(name, false);
		}

		static void Delete(string name)
		{
			try { string p = PathFor(name); if (File.Exists(p)) { File.Delete(p); Log("'" + name + "' is saved: its autosave is removed"); } }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not remove the autosave of '" + name + "': " + e.Message); }
		}

		/// <summary>Every frame (cheap: looks every 2 s).</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextCheck) return;
			nextCheck = Time.unscaledTime + CheckSeconds;
			if (current == null || !Unsaved || Time.unscaledTime - lastWrite < IntervalSeconds) return;
			// (not in the middle of a drag or a sculpt stroke)
			if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)) return;
			if (!DynamicIslands.InEditor()) return;
			Write();
		}

		/// <summary>Writes the autosave now if there are unsaved changes (leaving the editor, quitting Raft, tests).</summary>
		public static void WriteNow()
		{
			if (current == null || !Unsaved || !DynamicIslands.InEditor()) return;
			Write();
		}

		static void Write()
		{
			lastWrite = Time.unscaledTime;
			string name = current;
			try
			{
				Directory.CreateDirectory(Folder);
				string path = PathFor(name);
				// (IslandFile.Save writes aside first and replaces in one step: a crash never leaves a broken autosave behind)
				DynamicIslands.CaptureIsland(name).Save(path);
				LastWritten = path;
				Log("'" + name + "' has unsaved changes: kept in " + path + " until it is saved");
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Autosaving '" + name + "' failed: " + e.Message); }
		}

		static void OnQuit()
		{
			try { WriteNow(); } catch { }
		}

		/// <summary>The autosaves to offer: newer than their island file, or of an island that was never saved.</summary>
		public static List<string> Waiting()
		{
			var result = new List<string>();
			try
			{
				if (!Directory.Exists(Folder)) return result;
				foreach (string f in Directory.GetFiles(Folder, "*" + IslandFile.Extension))
				{
					string name = Path.GetFileNameWithoutExtension(f);
					string island = IslandSpawner.PathFor(name);
					if (File.Exists(island) && File.GetLastWriteTimeUtc(island) >= File.GetLastWriteTimeUtc(f))
					{
						// (saved since, e.g. by a test or by hand: nothing to give back)
						try { File.Delete(f); } catch { }
						continue;
					}
					result.Add(name);
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Looking at the autosaves: " + e.Message); }
			return result.OrderByDescending(n => File.GetLastWriteTimeUtc(PathFor(n))).ToList();
		}

		/// <summary>The editor has opened: offers unsaved work from last time.</summary>
		public static void OnEditorReady()
		{
			Opened(DynamicIslands.currentIslandName, false);
			if (!quitHooked) { Application.quitting += OnQuit; quitHooked = true; }
			if (Waiting().Count > 0) DynamicIslands.instance.StartCoroutine(Offer());
		}

		/// <summary>The window offering the autosaves (public for tests), once Raft's objects are loaded.</summary>
		public static IEnumerator Offer()
		{
			float until = Time.unscaledTime + 120f;
			while (!PlaceableCatalog.IsBuilt && Time.unscaledTime < until) yield return null;
			yield return new WaitForSecondsRealtime(1f);
			List<string> waiting = Waiting();
			if (waiting.Count == 0 || !DynamicIslands.InEditor()) yield break;
			var choices = waiting.Take(8).Select(n => new ChoiceWindow.Choice(n, "Open '" + n + "'",
				"autosaved " + File.GetLastWriteTime(PathFor(n)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
				(File.Exists(IslandSpawner.PathFor(n)) ? " - newer than its saved island" : " - never saved"))).ToList();
			choices.Add(new ChoiceWindow.Choice(DiscardChoice, "Throw them away", "the unsaved changes are deleted"));
			choices.Add(new ChoiceWindow.Choice(LaterChoice, "Not now", "asked again the next time the editor opens"));
			Log("Unsaved work from last time: " + string.Join(", ", waiting.ToArray()));
			ChoiceWindow.Open("Unsaved work from last time", choices, Pick);
		}

		public const string DiscardChoice = "*discard*", LaterChoice = "*later*";

		/// <summary>What the player picked in the offer (public for tests).</summary>
		public static void Pick(string value)
		{
			if (value == null || value == LaterChoice) return;
			if (value == DiscardChoice)
			{
				foreach (string n in Waiting()) { try { File.Delete(PathFor(n)); } catch { } }
				Log("The autosaves were thrown away");
				return;
			}
			if (DynamicIslands.LoadIsland(value, PathFor(value)))
				DynamicIslands.Notify("Opened the unsaved work on '" + value + "' - Save to keep it");
		}
	}
}
