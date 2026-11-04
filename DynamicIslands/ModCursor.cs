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
			if (CanvasHelper.ActiveMenu == MenuType.PauseMenu)
			{
				// (RML skips ToggleCursor(false) while its console or mod menu is open: the mouse stays with that, the menu goes back)
				CanvasHelper.ActiveMenu = back;
				LastGiveBack = "menu " + back + " (RML kept the mouse)";
			}
			else
			{
				// (ToggleCursor(false) leaves None: RML's own marker 666, set before the window opened, goes back for RML to clear)
				if (CanvasHelper.ActiveMenu == MenuType.None && back != MenuType.None) CanvasHelper.ActiveMenu = back;
				LastGiveBack = "menu " + CanvasHelper.ActiveMenu + ", mouse locked";
			}
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
