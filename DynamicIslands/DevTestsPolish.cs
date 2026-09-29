using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of ROADMAP.md step 1: the level up system as a World setting and a host switch (T5), the editor's keys (E5),
	/// the editor's object limit (E7).
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CILevelsSwitch", docs: "Dev, world (host, a test world 'CI ...'): the level up system switched on and off by the host - off keeps everyone's levels (world file @levels=off with the records), an island made with levels doesn't switch it back on, on brings the levels back. What the world had is put back after")]
		public static void LevelsSwitchCommand() { DynamicIslands.instance.StartCoroutine(LevelsSwitchRoutine()); }

		static IEnumerator LevelsSwitchRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("levels switch: host, in a test world 'CI ...'"); yield break; }
			bool ok = true;
			bool wasOn = PlayerLevels.On, wasOff = PlayerLevels.OffByHost;
			try
			{
				PlayerLevels.SetEnabled(true);
				yield return null;
				LevelRecord mine = PlayerLevels.Mine;
				Check(ref ok, PlayerLevels.On && mine != null, "switched on by the host");
				int xpBefore = mine != null ? mine.Xp : 0;
				if (mine != null) { mine.Xp += 250; }
				int xp = mine != null ? mine.Xp : 0;
				PlayerLevels.SetEnabled(false);
				yield return null;
				List<string> lines = PlayerLevels.WriteLines().ToList();
				Check(ref ok, !PlayerLevels.On && PlayerLevels.OffByHost && PlayerLevels.Mine == null, "switched off: no level for anyone now");
				Check(ref ok, lines.Contains("@levels=off") && lines.Any(l => l.StartsWith("@level=")), "the world file keeps it off, with everyone's levels (" + string.Join(" ", lines.ToArray()) + ")");
				PlayerLevels.OnIslandSpawned(new Dictionary<string, string> { { IslandProps.Levels, "on" } });
				Check(ref ok, !PlayerLevels.On, "an island made with levels doesn't switch it back on over the host");
				PlayerLevels.SetEnabled(true);
				yield return null;
				Check(ref ok, PlayerLevels.On && PlayerLevels.Mine != null && PlayerLevels.Mine.Xp == xp, "on again: the level is back (" + (PlayerLevels.Mine != null ? PlayerLevels.Mine.Xp : -1) + " EXP)");
				if (PlayerLevels.Mine != null) PlayerLevels.Mine.Xp = xpBefore;
			}
			finally
			{
				if (wasOn) PlayerLevels.SetEnabled(true);
				else if (wasOff) PlayerLevels.SetEnabled(false);
				else PlayerLevels.TurnOff();
			}
			if (ok) Log("PASS: levels switch"); else Fail("levels switch");
		}

		[ConsoleCommand(name: "CILevelsChoice", docs: "Dev, main menu: the World settings window's Level up system switch - clicked on and off (its label, the box's count of changed settings), Raft's own switches it off; the choice is put back after")]
		public static void LevelsChoiceCommand() { DynamicIslands.instance.StartCoroutine(LevelsChoiceRoutine()); }

		static IEnumerator LevelsChoiceRoutine()
		{
			bool ok = true;
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("levels choice: at the main menu"); yield break; }
			bool before = PlayerLevels.Chosen;
			try
			{
				try { box.Close(); } catch { }
				box.Open();
				yield return new WaitForSecondsRealtime(0.5f);
				WorldSettingsWindow.Open();
				yield return null;
				UnityEngine.UI.Button toggle = WorldSettingsWindow.LevelsToggle;
				Check(ref ok, toggle != null && toggle.gameObject.activeInHierarchy, "World settings has a Level up system switch");
				PlayerLevels.Chosen = false;
				WorldSettingsWindow.Close(); WorldSettingsWindow.Open();
				int changed = WorldSettingsWindow.Changed;
				if (toggle != null) toggle.onClick.Invoke();
				yield return null;
				Check(ref ok, PlayerLevels.Chosen && UIKit.LabelOf(toggle).text.EndsWith("ON") && WorldSettingsWindow.Changed == changed + 1, "clicked: on, the label says so, one more setting changed (" + UIKit.LabelOf(toggle).text + ")");
				WorldSettingsWindow.RaftsOwn();
				yield return null;
				Check(ref ok, !PlayerLevels.Chosen && UIKit.LabelOf(toggle).text.EndsWith("off"), "Raft's own: off");
				WorldSettingsWindow.Close();
			}
			finally
			{
				PlayerLevels.Chosen = before;
				WorldSettingsWindow.Close();
				try { box.Close(); } catch { }
			}
			if (ok) Log("PASS: levels choice"); else Fail("levels choice");
		}

		[ConsoleCommand(name: "CIEditorLimits", docs: "Dev, editor: the object limit (placing past what an island file holds is refused, a few more are allowed) and the gizmo's keys (C isn't the gizmo's any more - it is the camera's down; the Objects tab lists X, P and Ctrl)")]
		public static void EditorLimitsCommand()
		{
			bool ok = true;
			if (!DynamicIslands.InEditor()) { Fail("editor limits: in the editor"); return; }
			int n = ObjectLimit.Count();
			Check(ref ok, ObjectLimit.Allow(1), "one more object is allowed (" + n + " now)");
			Check(ref ok, !ObjectLimit.Allow(IslandFile.MaxObjects + 1 - n), "past " + IslandFile.MaxObjects + " objects is refused");
			var gizmo = UnityEngine.Object.FindObjectOfType<RuntimeGizmos.TransformGizmo>();
			Check(ref ok, gizmo != null && gizmo.SetCenterTypeToggle == KeyCode.None && gizmo.SetSpaceToggle == KeyCode.X && gizmo.SetPivotModeToggle == KeyCode.P, "the gizmo's keys: C left to the camera, X and P kept");
			if (ok) Log("PASS: editor limits"); else Fail("editor limits");
		}
	}
}
