using System;
using System.Collections.Generic;
using System.Linq;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The mouse for the mod's own windows in a world (dialogue, note reader, notebook, journal, stats page, code lock,
	/// credits, the missing islands box). The first window to open frees it with RML's RAPI.ToggleCursor(true), which also
	/// sets Raft's CanvasHelper.ActiveMenu to PauseMenu (Raft's movement, look and keys stop); the last one to close gives
	/// back what Raft had: the menu (None, or RML's 666) and a locked, hidden mouse.
	/// Before 11-04 every window read Cursor.visible when it opened and gave the mouse back only if it had been hidden then.
	/// A window that opened while another one (or a second Show of itself) had freed the mouse read "free" and never gave it
	/// back, and a window closing under another one locked the mouse while that one was still open: ActiveMenu stayed
	/// PauseMenu with a free mouse - the mod's keys (ModKeys.Blocked) and Raft's sign box treated it as the Esc menu.
	/// The main cause (CICursorTrace 11-04): RAPI.ToggleCursor(false) does nothing while RML's MainMenu.IsOpen is set, and that
	/// stays set in a world - every close left PauseMenu and a free mouse. Give now does ToggleCursor(false)'s work itself then.
	/// </summary>
	public static class ModCursor
	{
		static readonly List<object> holders = new List<object>();
		static bool freed;
		static MenuType menuBefore;

		/// <summary>The mod's windows holding the mouse now (tests).</summary>
		public static int Holders { get { return holders.Count; } }
		public static string HolderNames { get { return string.Join(", ", holders.Select(h => h is Component ? h.GetType().Name : h.ToString()).ToArray()); } }

		/// <summary>The mod freed the mouse itself (ToggleCursor(true)): ActiveMenu PauseMenu is the mod's, not Raft's Esc menu.</summary>
		public static bool Freed { get { return freed && holders.Count > 0; } }

		/// <summary>What the last give-back did (tests, the log).</summary>
		public static string LastGiveBack { get; private set; }

		/// <summary>A window opens: the mouse is free while it is open. Raft's own open menu (the inventory under the stats page,
		/// the Esc menu under Esc > Custom Islands) already has it free and gives it back itself.</summary>
		public static void Take(object who)
		{
			if (who == null) return;
			Prune();
			if (holders.Contains(who)) return; // (a second Show of the same window: still one hold)
			holders.Add(who);
			if (holders.Count > 1) return;
			MenuType m = CanvasHelper.ActiveMenu;
			freed = !RaftMenu(m);
			if (!freed) return;
			menuBefore = m;
			try { RAPI.ToggleCursor(true); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Freeing the mouse: " + e.Message); }
		}

		/// <summary>A window closes: the last one gives Raft back its menu and a locked mouse.</summary>
		public static void Give(object who)
		{
			if (who == null || !holders.Remove(who)) return;
			Prune();
			if (holders.Count > 0 || !freed) return;
			freed = false;
			// (only in a world: after a disconnect it locked the mouse on the main menu - CA17)
			if (!LoadSceneManager.IsGameSceneLoaded) { LastGiveBack = "not in a world"; return; }
			// (Raft's own menu opened over the window - Esc menu, inventory: Raft closes it and locks the mouse itself)
			MenuType now = CanvasHelper.ActiveMenu;
			if (now != MenuType.PauseMenu || RaftMenuOpen(MenuType.PauseMenu)) { LastGiveBack = "left to Raft's " + now; return; }
			// (only RML's own marker 666 goes back; a Raft value seen before - a stale PauseMenu left by an older build - becomes None)
			MenuType back = Enum.IsDefined(typeof(MenuType), menuBefore) ? MenuType.None : menuBefore;
			try { RAPI.ToggleCursor(false); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Locking the mouse: " + e.Message); }
			if (CanvasHelper.ActiveMenu == MenuType.PauseMenu && RmlConsoleOpen())
			{
				// (RML's console is open: the mouse stays with it, the menu goes back)
				CanvasHelper.ActiveMenu = back;
				LastGiveBack = "menu " + back + " (RML's console keeps the mouse)";
			}
			else if (CanvasHelper.ActiveMenu == MenuType.PauseMenu)
			{
				// (RML's ToggleCursor(false) also does nothing while its MainMenu.IsOpen is set - and that stays set in a world (seen
				// 11-04 on every close): the cause of the stuck PauseMenu and free mouse. Do what it does.)
				LockLikeRml();
				if (back != MenuType.None) CanvasHelper.ActiveMenu = back;
				LastGiveBack = "menu " + CanvasHelper.ActiveMenu + ", mouse locked by the mod (RML skipped it)";
			}
			else
			{
				// (ToggleCursor(false) leaves None: RML's own marker 666, set before the window opened, goes back for RML to clear)
				if (CanvasHelper.ActiveMenu == MenuType.None && back != MenuType.None) CanvasHelper.ActiveMenu = back;
				LastGiveBack = "menu " + CanvasHelper.ActiveMenu + ", mouse locked";
			}
		}

		/// <summary>What RAPI.ToggleCursor(false) does when it runs: a hidden, locked mouse, no menu, Raft's player controls.</summary>
		static void LockLikeRml()
		{
			try { Helper.SetCursorVisibleAndLockState(false, CursorLockMode.Locked); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Locking the mouse: " + e.Message); }
			CanvasHelper.ActiveMenu = MenuType.None;
			try { CanvasHelper ch = ComponentManager<CanvasHelper>.Value; if (ch != null) ch.SetUIState(true); } catch { }
			try { CustomInputConfig c = SimpleMonoBehaviourSingleton<CustomInputConfig>.Instance; if (c != null) c.SwitchCurrentActionMap("Player"); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Player keys back: " + e.Message); }
		}

		static System.Reflection.FieldInfo consoleOpen;
		static bool consoleLooked;

		/// <summary>RML's console is open (RConsole.isOpen): it has the mouse.</summary>
		public static bool RmlConsoleOpen()
		{
			try
			{
				if (!consoleLooked) { consoleLooked = true; consoleOpen = HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("RConsole"), "isOpen"); }
				return consoleOpen != null && (bool)consoleOpen.GetValue(null);
			}
			catch { return false; }
		}

		/// <summary>Windows destroyed while open (a scene change) let go.</summary>
		static void Prune()
		{
			for (int i = holders.Count - 1; i >= 0; i--)
			{
				Component c = holders[i] as Component;
				if (holders[i] == null || (holders[i] is Component && c == null)) holders.RemoveAt(i);
			}
		}

		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
		[System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
		static uint ownPid;
		static bool lockLetGo;
		static CursorLockMode lockBefore;
		static bool visibleBefore;
		static MenuType menuAtLetGo;
		static int letGoLogged;
		static int tickFrame = -1;
		static bool away;
		/// <summary>Dev tests only (CICursorMinFront, CICursorFix): while TestPlaceFaked, Raft's place is not read from Windows -
		/// TestMinimisedInFront true acts as "minimised / not in front", false as "shown and in front". Never set by the mod itself.</summary>
		internal static bool TestMinimisedInFront;
		internal static float TestPlaceFakedUntil;
		static bool TestPlaceFaked { get { return TestPlaceFakedUntil > Time.realtimeSinceStartup; } }
		/// <summary>Dev builds: a test Raft (CIBackground ran) never holds the mouse, even in front - unless a screen test raised it
		/// (ui.ps1 focus writes %TEMP%\raft_quiet_hold, needs tools\keys_ok.txt, the user's go) in the last 10 minutes.</summary>
		internal static bool TestRaft;
		static float holdLooked = -100f;
		static bool screenTest;
		internal static bool LockLetGo { get { return lockLetGo; } }
		internal static int LetGoCount { get { return letGoLogged; } }

		/// <summary>Raft holds no mouse lock while it is not the player's window in front (every frame, DynamicIslands.Update and
		/// again in LateUpdate, after Raft's own code).
		/// (11-07, the user: the mouse kept jumping to the screen's top-left corner while the tests ran on minimised Rafts:
		/// a minimised Raft left as the foreground window - Unity keeps a locked mouse at the middle of a focused window, at -32000.)
		/// (11-09, the user: the mouse still jumped - to the middle of the screen. The box Rafts sat windowed, not minimised, in the
		/// middle of the screen behind the user's windows; once one became the window in front (a window closed or minimised, the
		/// next in line is activated) Unity locked the mouse to its middle. Now the lock is let go - and the mouse shown - whenever
		/// Raft is minimised, or any other process's window is in front, or (dev) it is a test Raft; given back once Raft is the
		/// window in front again, if nothing changed it meanwhile. Normal play, Raft in front, is untouched.)</summary>
		public static void Tick()
		{
			if (Application.isEditor) return;
			if (Time.frameCount != tickFrame) { tickFrame = Time.frameCount; away = Away(); }
			if (away && Cursor.lockState != CursorLockMode.None)
			{
				if (!lockLetGo) { lockBefore = Cursor.lockState; visibleBefore = Cursor.visible; menuAtLetGo = CanvasHelper.ActiveMenu; }
				lockLetGo = true;
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
				if (letGoLogged++ < 5) Debug.Log("[CUSTOM ISLANDS] Mouse: Raft is not the window in front (or minimised, or a test Raft) - its mouse lock is let go meanwhile (the cursor isn't pulled to its window)");
			}
			else if (!away && lockLetGo)
			{
				lockLetGo = false;
				// (only when nothing changed it meanwhile: a menu or mod window opened in between keeps its free mouse)
				if (Cursor.lockState == CursorLockMode.None && Cursor.visible && CanvasHelper.ActiveMenu == menuAtLetGo && !Freed)
				{
					Cursor.lockState = lockBefore;
					Cursor.visible = visibleBefore;
				}
			}
		}

		/// <summary>Raft is minimised, another process's window is in front, or (dev) it is a test Raft with no screen test running.</summary>
		static bool Away()
		{
			if (TestPlaceFaked) return TestMinimisedInFront;
			if (TestRaft)
			{
				if (Time.realtimeSinceStartup - holdLooked > 2f)
				{
					holdLooked = Time.realtimeSinceStartup;
					try
					{
						string f = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "raft_quiet_hold");
						screenTest = System.IO.File.Exists(f) && (DateTime.Now - System.IO.File.GetLastWriteTime(f)).TotalMinutes < 10;
					}
					catch { screenTest = false; }
				}
				if (!screenTest) return true;
			}
			IntPtr h = GetForegroundWindow();
			if (h == IntPtr.Zero) return true;
			if (ownPid == 0) ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
			uint pid; GetWindowThreadProcessId(h, out pid);
			return pid != ownPid || IsIconic(h);
		}

		/// <summary>One of Raft's own menus (inventory, build menu, Esc menu, ...) is the active one - not None, not RML's 666.</summary>
		public static bool RaftMenu(MenuType m)
		{
			return m != MenuType.None && Enum.IsDefined(typeof(MenuType), m) && (m != MenuType.PauseMenu || RaftMenuOpen(MenuType.PauseMenu));
		}

		/// <summary>Raft's own menu of that type is open (its GameMenu): Raft's Esc menu, not the mod's PauseMenu marker.</summary>
		public static bool RaftMenuOpen(MenuType type)
		{
			try
			{
				CanvasHelper ch = ComponentManager<CanvasHelper>.Value;
				if (ch == null || ch.gameMenus == null) return false;
				foreach (GameMenu g in ch.gameMenus) if (g != null && g.menuType == type && g.IsOpen) return true;
			}
			catch { }
			return false;
		}
	}
}
