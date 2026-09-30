using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "Test in a world" (the editor's top bar): the island being edited, saved, is tried in a world - the mod goes to the
	/// main menu, loads the test world "Custom Islands test" (or makes it the first time, with the plan "No custom islands"),
	/// puts the island beside the raft and the player on it. "Back to the editor" (Esc > Custom Islands) leaves without
	/// saving - the test world stays as it was - and opens the editor on the island again. Before, trying an island meant
	/// saving, leaving, loading a world and typing SpawnIsland.
	/// </summary>
	public static class IslandTest
	{
		public const string WorldName = "Custom Islands test";
		enum State { None, ToMenu, Opening, InWorld, Testing, Returning, OpeningEditor }
		static State state;
		static float waitUntil, giveUpAt;
		/// <summary>The main menu Test asked for has loaded. (The editor's own main menu is still there for a moment: with a big
		/// island the new one took longer, and Raft's New Game box found then was the old one being destroyed - its Create threw.)</summary>
		static bool menuLoaded;

		/// <summary>The island being tried (null when none).</summary>
		public static string Island { get; private set; }
		/// <summary>In the test world with the island (the world window offers Back to the editor).</summary>
		public static bool Testing { get { return state == State.Testing; } }
		public static bool Busy { get { return state != State.None; } }
		/// <summary>What happened last (tests, and the player when something didn't work).</summary>
		public static string LastStep { get; private set; }

		static void Step(string s) { LastStep = s; Debug.Log("[CUSTOM ISLANDS] [test] " + s); }

		static void Stop(string why, bool warn)
		{
			Step(why);
			if (warn) DynamicIslands.Notify(why, true);
			state = State.None;
		}

		/// <summary>The editor's Test in a world: saves the island (when it has a name) and goes to try it.</summary>
		public static void Start()
		{
			if (!DynamicIslands.InEditor() || Busy) return;
			string name = DynamicIslands.currentIslandName;
			if (DynamicIslands.IsUnnamed)
			{
				DynamicIslands.Notify("Give the island a name and save it first (Save as), then Test in a world", true);
				IslandFilesWindow.Open();
				return;
			}
			if (EditorAutosave.Unsaved || !System.IO.File.Exists(IslandSpawner.PathFor(name)))
				if (!DynamicIslands.SaveIsland(name)) return;
			Island = name;
			state = State.ToMenu;
			giveUpAt = Time.unscaledTime + 240f;
			Step("Trying '" + name + "' in the world '" + WorldName + "'");
			DynamicIslands.Notify("Trying '" + name + "' in a world...");
			menuLoaded = false;
			UnityEngine.SceneManagement.SceneManager.sceneLoaded -= MenuLoaded;
			UnityEngine.SceneManagement.SceneManager.sceneLoaded += MenuLoaded;
			UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
		}

		static void MenuLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			if (scene.name != "MainMenuScene") return;
			UnityEngine.SceneManagement.SceneManager.sceneLoaded -= MenuLoaded;
			menuLoaded = true;
			waitUntil = Time.unscaledTime + 2f;
		}

		/// <summary>The world window's Back to the editor: leaves the test world without saving it.</summary>
		public static void Back()
		{
			if (state != State.Testing) return;
			state = State.Returning;
			giveUpAt = Time.unscaledTime + 120f;
			Step("Back to the editor with '" + Island + "'");
			WorldWindow.Close();
			// (Raft's leave without saving; the pause menu's own button leaves for no scene unless its exit box chose one)
			Raft_Network net = ComponentManager<Raft_Network>.Value;
			if (net != null) net.LeaveGame(DisconnectReason.SelfDisconnected, SceneName.Lobby, false, false);
			else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
		}

		/// <summary>Every frame from the mod.</summary>
		public static void Tick()
		{
			if (state == State.None) return;
			if (Time.unscaledTime > giveUpAt) { Stop("Trying the island took too long - stopped (" + LastStep + ")", true); return; }
			bool menu = !LoadSceneManager.IsGameSceneLoaded && GameObject.Find("MainMenuCanvas") != null;
			switch (state)
			{
				case State.ToMenu:
					if (menu && menuLoaded && Time.unscaledTime >= waitUntil) { state = State.Opening; DynamicIslands.instance.StartCoroutine(OpenWorld()); }
					break;
				case State.InWorld:
					if (LoadSceneManager.IsGameSceneLoaded && CustomIslandSpawner.RaftPosition.HasValue && RAPI.GetLocalPlayer() != null && Time.unscaledTime >= waitUntil)
					{ state = State.Testing; DynamicIslands.instance.StartCoroutine(BringIsland()); }
					break;
				case State.Testing:
					if (!LoadSceneManager.IsGameSceneLoaded && menu) Stop("Left the test world", false); // (by Raft's own menu)
					break;
				case State.Returning:
					if (menu) { state = State.OpeningEditor; DynamicIslands.LoadEditor(new string[0]); }
					break;
				case State.OpeningEditor:
					if (DynamicIslands.InEditor() && PlaceableCatalog.IsBuilt)
					{
						string n = Island;
						state = State.None;
						if (DynamicIslands.LoadIsland(n)) Step("Back in the editor with '" + n + "'");
					}
					break;
			}
		}

		/// <summary>The test world: loaded when it exists, made the first time (plan "No custom islands", Raft's default mode).</summary>
		static IEnumerator OpenWorld()
		{
			yield return new WaitForSecondsRealtime(1f);
			LoadGameBox load = Resources.FindObjectsOfTypeAll<LoadGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid() && b.gameObject.scene.isLoaded);
			LoadGame_Selection pick = null;
			// (the world is on disk or not; when it is, Raft's list shows it after a while - right after the editor it took
			// over 10 s to start filling, and with many saved worlds it is long)
			bool saved = false;
			try { saved = !string.IsNullOrEmpty(SaveAndLoad.WorldPath) && System.IO.Directory.Exists(System.IO.Path.Combine(SaveAndLoad.WorldPath, WorldName)); } catch { }
			if (saved && load != null)
			{
				load.gameObject.SetActive(true);
				try { load.Close(); } catch { }
				load.Open();
				int count = -1;
				for (float until = Time.realtimeSinceStartup + 90f; Time.realtimeSinceStartup < until && pick == null; )
				{
					yield return new WaitForSecondsRealtime(1f);
					int now = load.loadGameSelections != null ? load.loadGameSelections.Count : 0;
					if (now > 0 && now != count)
						pick = load.loadGameSelections.FirstOrDefault(s => s.text_GameName != null && s.text_GameName.text.Equals(WorldName, StringComparison.OrdinalIgnoreCase)
							|| s.directoryInfo != null && s.directoryInfo.Name.Equals(WorldName, StringComparison.OrdinalIgnoreCase));
					count = now;
				}
				Step("Raft's Load list: " + count + " world(s), the test world " + (pick != null ? "is there" : "isn't there"));
				if (pick == null) { Stop("The test world '" + WorldName + "' is saved but Raft's Load list doesn't show it - the island wasn't tried", true); yield break; }
			}
			if (pick != null)
			{
				load.Button_SelectLoad(pick);
				yield return null;
				for (float t = 0; t < 10f && load.loadButton != null && !load.loadButton.interactable; t += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
				if (load.loadButton != null && !load.loadButton.interactable) { Stop("Raft's Load button is off (is Steam online?) - the island wasn't tried", true); yield break; }
				Step("Loading the test world");
				try { load.Button_LoadGame(); }
				catch (Exception e) { Stop("Raft's Load didn't work (" + e.GetType().Name + ") - the island wasn't tried; try Test again", true); Debug.LogWarning("[CUSTOM ISLANDS] [test] " + e); yield break; }
			}
			else
			{
				try { if (load != null) load.Close(); } catch { }
				NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid() && b.gameObject.scene.isLoaded);
				if (box == null) { Stop("Raft's New Game box wasn't found - the island wasn't tried", true); yield break; }
				box.gameObject.SetActive(true);
				try { box.Close(); } catch { }
				box.Open();
				yield return new WaitForSecondsRealtime(1f);
				box.inputfield_GameName.text = WorldName;
				box.GameNameEndEdit(WorldName);
				for (float t = 0; t < 5f && box.createGameButton != null && !box.createGameButton.interactable; t += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
				if (box.createGameButton != null && !box.createGameButton.interactable) { Stop("Raft's Create button is off - the island wasn't tried", true); yield break; }
				WorldDirector.PendingPlan = WorldPlan.NoneName; // (only the island being tried)
				Step("Making the test world");
				try { box.Button_CreateNewGame(); }
				catch (Exception e) { Stop("Raft's Create didn't work (" + e.GetType().Name + ") - the island wasn't tried; try Test again", true); Debug.LogWarning("[CUSTOM ISLANDS] [test] " + e); yield break; }
			}
			state = State.InWorld;
			waitUntil = Time.unscaledTime + 6f;
		}

		/// <summary>The island beside the raft, the player on it.</summary>
		static IEnumerator BringIsland()
		{
			yield return new WaitForSeconds(2f);
			// (islands tried before stay in the test world when Raft saved it meanwhile: only the one being tried now)
			int old = IslandWorldState.Remove(null);
			if (old > 0) Step("Took away " + old + " island(s) tried before");
			Vector3 raft = CustomIslandSpawner.RaftPosition.Value;
			float radius = Mathf.Max(20f, CustomIslandSpawner.LandRadius(Island));
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft, radius, radius + 400f);
			if (!spot.HasValue) spot = raft + CustomIslandSpawner.SailDirection() * (radius + 80f);
			Vector3 at = spot.Value;
			at.y = CustomIslandSpawner.Elevation(Island);
			yield return DynamicIslands.instance.SpawnIslandFile(Island, at, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(x => x.Name.Equals(Island, StringComparison.OrdinalIgnoreCase));
			if (e == null || e.Root == null) { Stop("'" + Island + "' couldn't be placed in the test world", true); yield break; }
			yield return new WaitForSeconds(1f);
			// (the player on the island's highest ground near its middle)
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3 top = e.Position + Vector3.up * 400f;
			RaycastHit hit;
			Vector3 stand = Physics.Raycast(top, Vector3.down, out hit, 800f, ~0, QueryTriggerInteraction.Ignore) ? hit.point + Vector3.up * 1.5f : e.Position + Vector3.up * 5f;
			PlayerMove.To(player, stand);
			Step("Testing '" + Island + "' (" + e.Position + ")");
			IslandInfo.Show("Testing '" + Island + "'", "", "Esc > Custom Islands > Back to the editor (the test world isn't saved)");
		}
	}
}
