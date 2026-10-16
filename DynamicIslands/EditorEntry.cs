using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The ways into and out of the editor (TODO 5b): the main menu's EDITOR opens the start window (CreateWindow), which
	/// opens the 3D editor on an island (new, or one to open once it is ready) or a world plan on the menu itself, with no
	/// island loaded. It remembers the last island, the last plan and which of the two was made last (editor_start.txt),
	/// so the start window offers "Continue" on them. The editor's Main menu comes back to the start window; an island
	/// opened from a plan's card (Edit island) gets "Back to the plan" in the editor's top bar.
	/// </summary>
	public static class EditorEntry
	{
		public const string Island = "island", Plan = "plan";
		static string FilePath { get { return Path.Combine(DynamicIslands.assetpath, "editor_start.txt"); } }

		/// <summary>The island to open once the editor is ready (null: a new island).</summary>
		public static string OpenIsland;
		/// <summary>The editor's Main menu was used: the start window opens on the menu again.</summary>
		public static bool ReturnToCreate;
		/// <summary>The plan an island was opened from (Edit island on its card): the editor's top bar offers Back to the plan.</summary>
		public static string BackToPlan;

		static Dictionary<string, string> Read()
		{
			var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				if (File.Exists(FilePath))
					foreach (string line in File.ReadAllLines(FilePath))
					{
						int eq = line.IndexOf('=');
						if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
					}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] editor_start.txt: " + e.Message); }
			return d;
		}

		static void Write(string key, string value)
		{
			if (string.IsNullOrEmpty(value)) return;
			Dictionary<string, string> d = Read();
			string old, last;
			if (d.TryGetValue(key, out old) && old == value && d.TryGetValue("last", out last) && last == key) return;
			d[key] = value;
			d["last"] = key;
			try { File.WriteAllLines(FilePath, d.Select(kv => kv.Key + "=" + kv.Value).ToArray()); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] editor_start.txt: " + e.Message); }
		}

		/// <summary>The island open in the editor now (named and saved ones only).</summary>
		public static void RememberIsland(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Equals(DynamicIslands.UnnamedIsland, StringComparison.OrdinalIgnoreCase)) return;
			Write(Island, name);
		}

		/// <summary>The world plan shown in the plan window now.</summary>
		public static void RememberPlan(string name)
		{
			if (string.IsNullOrEmpty(name) || WorldPlan.IsBuiltIn(name)) return;
			Write(Plan, name);
		}

		/// <summary>The last island, when its file is still there.</summary>
		public static string LastIsland
		{
			get { string n; return Read().TryGetValue(Island, out n) && n.Length > 0 && File.Exists(IslandSpawner.PathFor(n)) ? n : null; }
		}

		/// <summary>The last world plan, when its file is still there.</summary>
		public static string LastPlan
		{
			get { string n; return Read().TryGetValue(Plan, out n) && n.Length > 0 && !WorldPlan.IsBuiltIn(n) && File.Exists(WorldPlan.PathFor(n)) ? n : null; }
		}

		/// <summary>"island" or "plan": what was worked on last (the start window's card for it is the highlighted one).</summary>
		public static string Last { get { string n; return Read().TryGetValue("last", out n) && n == Plan ? Plan : Island; } }

		/// <summary>The start window's New island / Open an island: the 3D editor, on that island once it is ready.</summary>
		public static void StartIsland(string name)
		{
			OpenIsland = name;
			BackToPlan = null;
			if (name != null) RememberIsland(name);
			DynamicIslands.LoadEditor(new string[0]);
		}

		/// <summary>The plan card's Edit island: the island in the 3D editor, with Back to the plan in its top bar.</summary>
		public static void EditFromPlan(string island, string plan)
		{
			OpenIsland = island;
			BackToPlan = plan;
			RememberIsland(island);
			if (DynamicIslands.InEditor()) { DynamicIslands.LoadIsland(island); OpenIsland = null; EditorUI.RefreshIsland(); return; }
			DynamicIslands.LoadEditor(new string[0]);
		}

		/// <summary>OpenEditor, once Raft's objects are loaded: the island asked for is opened.</summary>
		public static void OnEditorReady()
		{
			string name = OpenIsland;
			OpenIsland = null;
			if (string.IsNullOrEmpty(name)) return;
			if (!File.Exists(IslandSpawner.PathFor(name))) { DynamicIslands.Notify("There is no saved island named '" + name + "' any more", true); return; }
			DynamicIslands.LoadIsland(name);
		}

		/// <summary>The editor's Main menu: back to Raft's menu, where the start window opens again.</summary>
		public static void ToMainMenu()
		{
			if (!DynamicIslands.IsUnnamed) RememberIsland(DynamicIslands.currentIslandName);
			ReturnToCreate = true;
			BackToPlan = null;
			EditorAutosave.WriteNow();
			UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
		}

		/// <summary>The editor's Back to the plan: the island is saved (when it has changes), then the plan opens on the menu.</summary>
		public static void ToPlan()
		{
			string plan = BackToPlan;
			if (plan == null) return;
			if (!DynamicIslands.IsUnnamed && EditorAutosave.Unsaved && !DynamicIslands.SaveIsland(DynamicIslands.currentIslandName)) return;
			if (!DynamicIslands.IsUnnamed) RememberIsland(DynamicIslands.currentIslandName);
			BackToPlan = null;
			ReturnToCreate = true;
			reopenPlan = plan;
			UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
		}

		static string reopenPlan;

		/// <summary>The main menu was built (HookUI): the start window (and the plan, after Back to the plan) open again.</summary>
		public static void OnMainMenu()
		{
			if (!ReturnToCreate) return;
			ReturnToCreate = false;
			string plan = reopenPlan;
			reopenPlan = null;
			CreateWindow.Open();
			if (plan != null) WorldPlanWindow.Open(plan);
		}
	}
}
