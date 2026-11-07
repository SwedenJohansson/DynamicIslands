using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>The mouse and Raft's CanvasHelper.ActiveMenu around the mod's windows (11-04 cursor fix): a trace of every
	/// RAPI.ToggleCursor call, the state line, and the test that closing any of the windows gives Raft back what it had.</summary>
	public static partial class DevTests
	{
		static Harmony cursorTrace;

		/// <summary>One line: Raft's menu, the mouse, focus, what RML's ToggleCursor(false) checks first, the mod's cursor holders.</summary>
		internal static string CursorState()
		{
			string menu, guard = "?", rmlMenu = "?";
			try { menu = CanvasHelper.ActiveMenu.ToString(); } catch { menu = "?"; }
			// (RAPI.ToggleCursor(false) does nothing while RML's console flag or its MainMenu.IsOpen is set)
			try
			{
				FieldInfo f = typeof(RAPI).Module.ResolveField(0x04000319);
				guard = f.DeclaringType.Name + "." + f.Name + " " + f.GetValue(null);
			}
			catch (Exception e) { guard = "(" + e.GetType().Name + ")"; }
			try { rmlMenu = AccessTools.Field(AccessTools.TypeByName("MainMenu"), "IsOpen").GetValue(null).ToString(); } catch { }
			return "ActiveMenu " + menu + ", cursor " + (Cursor.visible ? "shown" : "hidden") + " " + Cursor.lockState + ", focused " + Application.isFocused +
				", RML guard " + guard + ", RML MainMenu.IsOpen " + rmlMenu + ", Raft pause menu open " + ModCursor.RaftMenuOpen(MenuType.PauseMenu) +
				", mod cursor holders " + ModCursor.Holders + (ModCursor.Holders > 0 ? " (" + ModCursor.HolderNames + ")" : "") + ", keys blocked " + ModKeys.Blocked();
		}

		[ConsoleCommand(name: "CICursorState", docs: "Dev, in game: one line - CanvasHelper.ActiveMenu, the mouse, focus, RML's ToggleCursor guard, Raft's pause menu, the mod's cursor holders, ModKeys.Blocked()")]
		public static void CursorStateCommand() { Log("Cursor state: " + CursorState()); }

		[ConsoleCommand(name: "CICursorTrace", docs: "Dev, anywhere: CICursorTrace on|off - logs every RAPI.ToggleCursor / TogglePriorityCursor call and Raft's CanvasHelper.CloseMenu with its caller and the state before and after")]
		public static void CursorTraceCommand(string[] args)
		{
			bool on = args.Length == 0 || !args[0].Equals("off", StringComparison.OrdinalIgnoreCase);
			if (!on) { if (cursorTrace != null) cursorTrace.UnpatchAll("ci.cursortrace"); cursorTrace = null; Log("PASS: cursor trace off"); return; }
			if (cursorTrace != null) { Log("PASS: cursor trace already on"); return; }
			cursorTrace = new Harmony("ci.cursortrace");
			HarmonyMethod pre = new HarmonyMethod(AccessTools.Method(typeof(DevTests), "CursorTracePrefix"));
			HarmonyMethod post = new HarmonyMethod(AccessTools.Method(typeof(DevTests), "CursorTracePostfix"));
			cursorTrace.Patch(AccessTools.Method(typeof(RAPI), "ToggleCursor"), pre, post);
			cursorTrace.Patch(AccessTools.Method(typeof(RAPI), "TogglePriorityCursor"), pre, post);
			cursorTrace.Patch(AccessTools.Method(typeof(CanvasHelper), "CloseMenu"), pre, post);
			Log("PASS: cursor trace on - " + CursorState());
		}

		static void CursorTracePrefix(MethodBase __originalMethod, object[] __args)
		{
			string caller = "?";
			try
			{
				System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(2, false);
				for (int i = 0; i < st.FrameCount && i < 4; i++)
				{
					MethodBase m = st.GetFrame(i).GetMethod();
					if (m == null) continue;
					caller = (i == 0 ? "" : caller + " < ") + (m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name;
				}
			}
			catch { }
			Log("(cursor) " + __originalMethod.Name + "(" + (__args.Length > 0 ? __args[0] : "") + ") from " + caller + " - before: " + CursorState());
		}

		static void CursorTracePostfix(MethodBase __originalMethod) { Log("(cursor) ... after " + __originalMethod.Name + ": " + CursorState()); }

		[ConsoleCommand(name: "CICursorFix", docs: "Dev, in game (either player; run Levels on first): the mouse and Raft's menu around the mod's windows - talk to an islander and close (Esc's path), a second Show, a dialogue that opens the next one as it closes, dialogue + journal closed in both orders, a stale PauseMenu from an older build, K opens and closes the stats page, a page over Raft's Esc menu; each close must give back Raft's menu and a locked mouse and leave the mod's keys free: CICursorFix <island> <islander name>")]
		public static void CursorFixCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			StartTest(CursorFixRoutine(e, string.Join(" ", args.Skip(1).ToArray())));
		}

		/// <summary>One frame of a key through Input.GetKeyDown (CIKey's fake key), as the player presses it.</summary>
		static IEnumerator PressKey(KeyCode k)
		{
			InstallFakeKeys();
			fakeKey = k;
			fakeFrame = Time.frameCount + 1;
			yield return null;
			yield return null;
			yield return new WaitForSecondsRealtime(0.3f);
			fakeKey = KeyCode.None;
		}

		/// <summary>A mod window has the mouse: the mod's PauseMenu marker (not Raft's Esc menu), a free mouse, keys not blocked.</summary>
		static bool CursorHeld() { return CanvasHelper.ActiveMenu == MenuType.PauseMenu && !ModCursor.RaftMenuOpen(MenuType.PauseMenu) && Cursor.lockState == CursorLockMode.None && ModCursor.Holders > 0 && !ModKeys.Blocked(); }

		/// <summary>No mod window open: Raft's menu as before (None, or RML's 666), the mouse locked, keys free.</summary>
		static bool CursorBack(MenuType expect) { return CanvasHelper.ActiveMenu == expect && Cursor.lockState == CursorLockMode.Locked && ModCursor.Holders == 0 && !ModKeys.Blocked(); }

		static IEnumerator CursorFixRoutine(IslandWorldState.Entry e, string who)
		{
			bool ok = true;
			DialoguePanel.Close(); JournalWindow.Close(); LevelWindow.Close(); PlayerNotebookWindow.Close(); NoteReader.Close();
			yield return new WaitForSecondsRealtime(0.3f);
			MenuType start = CanvasHelper.ActiveMenu;
			// (what closing must give back: RML's 666 or None; a PauseMenu with Raft's Esc menu shut is the old bug's leftover - None)
			MenuType expect = Enum.IsDefined(typeof(MenuType), start) ? MenuType.None : start;
			Log("Cursor fix start: " + CursorState() + " - expected after each close: " + expect + " (" + (Raft_Network.IsHost ? "host" : "client") + ")");
			if (start == MenuType.PauseMenu && !ModCursor.RaftMenuOpen(MenuType.PauseMenu))
				Check(ref ok, !ModKeys.Blocked(), "stale PauseMenu (no Esc menu) at the start does not block the keys");

			yield return FaceRoutine(e, who);
			if (faceFound == null) yield break;
			Islander isl = faceFound.GetComponent<Islander>() ?? faceRef.GetComponentInChildren<Islander>(true);
			if (isl == null) { Fail("'" + who + "' on '" + e.HostName + "' is not an islander"); yield break; }

			// 1. talk, close as Esc does
			isl.Talk();
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, DialoguePanel.IsOpen && CursorHeld(), "dialogue open: mouse free, keys free - " + CursorState());
			DialoguePanel.Close();
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, !DialoguePanel.IsOpen && CursorBack(expect), "dialogue closed: menu and mouse back - " + CursorState());

			// 2. a second Show of the open dialogue (talking again while it is open)
			isl.Talk();
			yield return new WaitForSecondsRealtime(0.3f);
			isl.Talk();
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, DialoguePanel.IsOpen && CursorHeld() && ModCursor.Holders == 1, "talked twice: one hold - " + CursorState());
			DialoguePanel.Close();
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, !DialoguePanel.IsOpen && CursorBack(expect), "closed after talking twice: menu and mouse back - " + CursorState());

			// 3. a dialogue whose close opens the next one (an answer that leads on)
			Transform at = isl.transform;
			DialoguePanel.Open(at, "CI one", "First", c => DialoguePanel.Open(at, "CI two", "Second", null));
			yield return new WaitForSecondsRealtime(0.3f);
			DialoguePanel.Close();
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, DialoguePanel.IsOpen && DialoguePanel.ShownSpeaker == "CI two" && CursorHeld(), "next dialogue opened as the first closed: mouse still free - " + CursorState());
			DialoguePanel.Close();
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, !DialoguePanel.IsOpen && CursorBack(expect), "second dialogue closed: menu and mouse back - " + CursorState());

			// 4. two windows: dialogue + journal, closed in both orders
			isl.Talk(); yield return new WaitForSecondsRealtime(0.3f);
			JournalWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, DialoguePanel.IsOpen && JournalWindow.IsOpen && ModCursor.Holders == 2 && CursorHeld(), "dialogue + journal open: two holds - " + CursorState());
			DialoguePanel.Close(); yield return new WaitForSecondsRealtime(0.4f);
			Check(ref ok, JournalWindow.IsOpen && CursorHeld(), "dialogue closed under the journal: mouse still free - " + CursorState());
			JournalWindow.Close(); yield return new WaitForSecondsRealtime(0.4f);
			Check(ref ok, CursorBack(expect), "journal closed last: menu and mouse back - " + CursorState());
			JournalWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
			isl.Talk(); yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, DialoguePanel.IsOpen && JournalWindow.IsOpen && CursorHeld(), "journal + dialogue open - " + CursorState());
			JournalWindow.Close(); yield return new WaitForSecondsRealtime(0.4f);
			Check(ref ok, DialoguePanel.IsOpen && CursorHeld(), "journal closed under the dialogue: mouse still free - " + CursorState());
			DialoguePanel.Close(); yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, CursorBack(expect), "dialogue closed last: menu and mouse back - " + CursorState());

			// 5. the old bug's leftover (PauseMenu, free mouse, no window): keys free, and the next window's close tidies it
			try { RAPI.ToggleCursor(true); } catch { }
			yield return new WaitForSecondsRealtime(0.2f);
			Check(ref ok, CanvasHelper.ActiveMenu == MenuType.PauseMenu && !ModKeys.Blocked(), "stale PauseMenu: keys not blocked - " + CursorState());
			isl.Talk(); yield return new WaitForSecondsRealtime(0.3f);
			DialoguePanel.Close(); yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, CursorBack(MenuType.None), "stale PauseMenu: a dialogue's close gives a clean None and a locked mouse - " + CursorState());
			if (expect != MenuType.None) CanvasHelper.ActiveMenu = expect;

			// 6. K opens the stats page and closes it again
			if (!PlayerLevels.On) { Fail("cursor fix: the level up system is off - run 'Levels on' first"); yield break; }
			yield return PressKey(PlayerLevels.Key);
			Check(ref ok, LevelWindow.IsOpen && CursorHeld(), "K opens the stats page - " + CursorState());
			yield return PressKey(PlayerLevels.Key);
			Check(ref ok, !LevelWindow.IsOpen && CursorBack(expect), "K again closes it: menu and mouse back - " + CursorState());
			Screenshot(new[] { "cursorfix_back" });

			// 7. Raft's Esc menu: keys blocked; a page over it leaves the menu and mouse to Raft
			PauseMenu menu = UnityEngine.Object.FindObjectOfType<PauseMenu>();
			CanvasHelper ch = ComponentManager<CanvasHelper>.Value;
			if (menu != null && ch != null)
			{
				CanvasHelper.ActiveMenu = MenuType.None;
				CallPrivate(menu, "Pause");
				yield return new WaitForSecondsRealtime(0.6f);
				Check(ref ok, ModKeys.PauseOpen() && ModKeys.Blocked(), "Esc menu open: keys blocked - " + CursorState());
				LevelWindow.Open(); yield return new WaitForSecondsRealtime(0.3f);
				LevelWindow.Close(); yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, ModKeys.PauseOpen() && ModCursor.Holders == 0 && Cursor.lockState == CursorLockMode.None, "stats page opened and closed over it: the Esc menu keeps the mouse - " + CursorState());
				ch.CloseMenu(MenuType.PauseMenu);
				yield return new WaitForSecondsRealtime(0.6f);
				Check(ref ok, CanvasHelper.ActiveMenu == MenuType.None && Cursor.lockState == CursorLockMode.Locked && !ModKeys.Blocked(), "Esc menu closed: Raft locks the mouse, keys free - " + CursorState());
				if (expect != MenuType.None) CanvasHelper.ActiveMenu = expect;
			}
			else Log("(no pause menu: step 7 skipped)");

			if (ok) Log("PASS: cursor fix (" + (Raft_Network.IsHost ? "host" : "client") + ")"); else Fail("cursor fix");
		}

		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint p);
		struct CursorPoint { public int X, Y; }
		static string ScreenCursor() { CursorPoint p; return GetCursorPos(out p) ? p.X + "," + p.Y : "?"; }

		[ConsoleCommand(name: "CICursorMinFront", docs: "Dev, in game, no menu open, Levels on (either player): ModCursor.Tick with 'Raft minimised but in front' faked (no focus taken) - false: the lock is left alone; true: let go, logged once; false again: locked again; a mod window that showed the mouse meanwhile keeps it free, untouched by Tick, and its close locks it")]
		public static void CursorMinFrontCommand() { StartTest(CursorMinFrontRoutine()); }

		static IEnumerator CursorMinFrontRoutine()
		{
			bool ok = true;
			if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible) { Fail("cursor min front: needs a locked, hidden mouse (in game, no menu open) - " + CursorState()); yield break; }
			string before = ScreenCursor();
			Log("Cursor min front start: " + CursorState() + ", screen cursor " + before);
			try
			{
				int logged = ModCursor.LetGoCount;
				ModCursor.TestMinimisedInFront = false;
				yield return new WaitForSecondsRealtime(0.5f);
				Check(ref ok, Cursor.lockState == CursorLockMode.Locked && !ModCursor.LockLetGo && ModCursor.LetGoCount == logged, "condition false: Tick leaves the lock alone - " + CursorState());

				ModCursor.TestMinimisedInFront = true;
				yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, Cursor.lockState == CursorLockMode.None && ModCursor.LockLetGo && !Cursor.visible && ModCursor.LetGoCount > logged, "minimised in front: lock let go, mouse stays hidden, logged - " + CursorState());
				Check(ref ok, ScreenCursor() == before, "the screen cursor did not move (" + before + " -> " + ScreenCursor() + ")");

				ModCursor.TestMinimisedInFront = false;
				yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, Cursor.lockState == CursorLockMode.Locked && !ModCursor.LockLetGo, "shown again / another window in front: locked again - " + CursorState());

				// (a mod window that opened meanwhile shows the mouse: not locked again under it; Raft re-hides a mouse shown with no window open)
				ModCursor.TestMinimisedInFront = true;
				yield return new WaitForSecondsRealtime(0.3f);
				LevelWindow.Open();
				yield return new WaitForSecondsRealtime(0.3f);
				if (!LevelWindow.IsOpen) { Fail("cursor min front: the stats page didn't open - run Levels on first"); yield break; }
				ModCursor.TestMinimisedInFront = false;
				yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, Cursor.lockState == CursorLockMode.None && Cursor.visible && !ModCursor.LockLetGo, "a window showed the mouse meanwhile: it is not locked again - " + CursorState());

				int count = ModCursor.LetGoCount;
				yield return new WaitForSecondsRealtime(0.5f);
				Check(ref ok, Cursor.lockState == CursorLockMode.None && Cursor.visible && ModCursor.LetGoCount == count, "an unlocked mouse, condition false: Tick leaves it alone - " + CursorState());
				LevelWindow.Close();
				yield return new WaitForSecondsRealtime(0.4f);
				Check(ref ok, CursorBack(MenuType.None), "the window closed: menu and mouse back - " + CursorState());
			}
			finally
			{
				ModCursor.TestMinimisedInFront = false;
				if (LevelWindow.IsOpen) LevelWindow.Close();
			}
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, Cursor.lockState == CursorLockMode.Locked && !Cursor.visible, "back as it was - " + CursorState() + ", screen cursor " + ScreenCursor());
			if (ok) Log("PASS: cursor min front"); else Fail("cursor min front");
		}
	}
}
