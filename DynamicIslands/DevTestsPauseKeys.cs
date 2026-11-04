using System.Collections;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>IL22: the mod's world keys (K, J, notebook, minimap) are gated by ModKeys.Blocked(): pause menu open, Esc > Custom Islands on
	/// top of it, chat typing (IL20). Runs the same gate the keys' Tick() methods use, with the window minimised (no real keys).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIPauseKeys", docs: "Dev, in a world: K/J/notebook/minimap keys blocked while the Esc menu (and its Custom Islands window) is open, free after; chat typing still blocks")]
		public static void PauseKeysCommand() { StartTest(PauseKeysRoutine()); }

		static IEnumerator PauseKeysRoutine()
		{
			bool ok = true;
			PauseMenu menu = Object.FindObjectOfType<PauseMenu>();
			CanvasHelper ch = ComponentManager<CanvasHelper>.Value ?? Object.FindObjectOfType<CanvasHelper>();
			if (menu == null || ch == null) { Fail("pause keys: no pause menu or canvas helper"); yield break; }
			JournalWindow.Close(); LevelWindow.Close(); PlayerNotebookWindow.Close(); WorldWindow.Close();
			if (CanvasHelper.ActiveMenu == MenuType.PauseMenu) ch.CloseMenu(MenuType.PauseMenu);
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, !ModKeys.Blocked(), "menu closed: keys free (active menu " + CanvasHelper.ActiveMenu + ")");

			CallPrivate(menu, "Pause");
			yield return new WaitForSecondsRealtime(0.6f);
			Check(ref ok, CanvasHelper.ActiveMenu == MenuType.PauseMenu, "Esc menu open");
			Check(ref ok, ModKeys.PauseOpen() && ModKeys.Blocked(), "Esc menu open: keys blocked");
			Screenshot(new[] { "pausekeys_menu" });

			WorldWindow.Open();
			yield return new WaitForSecondsRealtime(0.6f);
			Check(ref ok, WorldWindow.IsOpen, "Esc > Custom Islands window opens over the menu");
			Check(ref ok, ModKeys.Blocked(), "... keys still blocked over it");
			Screenshot(new[] { "pausekeys_window" });
			WorldWindow.Close();
			yield return new WaitForSecondsRealtime(0.4f);
			Check(ref ok, !WorldWindow.IsOpen && CanvasHelper.ActiveMenu == MenuType.PauseMenu, "window closed, menu still open");

			ch.CloseMenu(MenuType.PauseMenu);
			yield return new WaitForSecondsRealtime(0.6f);
			Check(ref ok, CanvasHelper.ActiveMenu != MenuType.PauseMenu && !ModKeys.Blocked(), "menu closed: keys free again (active menu " + CanvasHelper.ActiveMenu + ")");

			// the pages still open and close by their own Open/Close with the menu closed
			JournalWindow.Open(); yield return new WaitForSecondsRealtime(0.4f);
			Check(ref ok, JournalWindow.IsOpen, "journal opens");
			JournalWindow.Close(); yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, !JournalWindow.IsOpen, "journal closes");
			PlayerNotebookWindow.Open(); yield return new WaitForSecondsRealtime(0.4f);
			Check(ref ok, PlayerNotebookWindow.IsOpen, "notebook opens");
			PlayerNotebookWindow.Close(); yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, !PlayerNotebookWindow.IsOpen, "notebook closes");

			// IL20: chat typing still blocks
			HarmonyLib.Traverse.Create(typeof(ChatTextFieldController)).Property("IsChatWindowSelected").SetValue(true);
			bool typing = ModKeys.Blocked();
			HarmonyLib.Traverse.Create(typeof(ChatTextFieldController)).Property("IsChatWindowSelected").SetValue(false);
			Check(ref ok, typing, "chat typing: keys blocked (IL20)");
			Check(ref ok, !ModKeys.Blocked(), "chat closed: keys free");

			if (ok) Log("PASS: pause keys"); else Fail("pause keys");
		}
	}
}
