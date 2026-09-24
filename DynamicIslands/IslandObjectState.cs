using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>What happened to one harvestable tree or pickup on a custom island.</summary>
	public class ObjectState
	{
		/// <summary>False once picked up (or a tree that hides when chopped down).</summary>
		public bool Active;
		/// <summary>Harvests left on a tree (its yield), or -1 for objects without a yield.</summary>
		public int Yield;
		/// <summary>In-game day it last changed, for regrowing.</summary>
		public int Day;
	}

	/// <summary>
	/// Remembers harvested trees and picked-up items per island, so they stay gone when the island is unloaded and
	/// loaded again, when the world is saved and loaded, and for players who join later. Objects are identified
	/// by their place in the island (the low bits of the network index IslandSpawner.RegisterNetworkIds gives them).
	/// Raft itself has no timed regrowth (its islands reset when they respawn); ours grow back after
	/// CustomIslandSpawner.RegrowDays in-game days, checked whenever the island is loaded.
	/// </summary>
	public static class IslandObjectState
	{
		static int Ordinal(PickupItem_Networked pn) { return (int)(pn.ObjectIndex & 0xFFFF); }

		static int Today { get { try { return WorldManager.DayCounter; } catch { return 0; } } }

		static YieldHandler YieldOf(PickupItem_Networked pn)
		{
			PickupItem pi = pn.GetComponent<PickupItem>();
			return pi != null ? pi.yieldHandler : null;
		}

		static int FullYield(YieldHandler yh)
		{
			return yh != null && yh.yieldAsset != null && yh.yieldAsset.yieldAssets != null ? yh.yieldAsset.yieldAssets.Count : 0;
		}

		/// <summary>Records the state of a loaded island's objects into its entry (only objects that were used).</summary>
		public static void Capture(IslandWorldState.Entry e)
		{
			if (e.Root == null) return;
			int today = Today;
			foreach (PickupItem_Networked pn in e.Root.GetComponentsInChildren<PickupItem_Networked>(true))
			{
				int ord = Ordinal(pn);
				if (ord == 0) continue; // not registered
				YieldHandler yh = YieldOf(pn);
				int full = FullYield(yh);
				int left = yh != null && yh.Yield != null ? yh.Yield.Count : 0;
				bool active = pn.gameObject.activeSelf;
				if (active && (full == 0 || left >= full)) { e.State.Remove(ord); continue; }
				int yield = full > 0 ? left : -1;
				ObjectState old;
				int day = e.State.TryGetValue(ord, out old) && old.Active == active && old.Yield == yield ? old.Day : today;
				e.State[ord] = new ObjectState { Active = active, Yield = yield, Day = day };
			}
		}

		/// <summary>Puts a freshly spawned island's objects into the remembered state, without effects or network messages.</summary>
		public static void Apply(IslandWorldState.Entry e, int regrowDays)
		{
			if (e.Root == null || e.State.Count == 0) return;
			DropRegrown(e.State, regrowDays);
			int applied = 0;
			foreach (PickupItem_Networked pn in e.Root.GetComponentsInChildren<PickupItem_Networked>(true))
			{
				ObjectState s;
				if (!e.State.TryGetValue(Ordinal(pn), out s)) continue;
				YieldHandler yh = YieldOf(pn);
				if (s.Yield >= 0 && yh != null && yh.Yield != null)
					while (yh.Yield.Count > s.Yield) yh.Yield.RemoveAt(yh.Yield.Count - 1); // what Raft's own restore does
				pn.gameObject.SetActive(s.Active);
				pn.OnRestored();
				applied++;
			}
			if (applied > 0) Debug.Log("[CUSTOM ISLANDS] '" + e.HostName + "': " + applied + " harvested/picked-up object(s) restored");
		}

		/// <summary>Forgets objects used at least regrowDays in-game days ago (0 = never), so they come back. Creatures (CreatureSpawner) decide for themselves. Returns how many.</summary>
		public static int DropRegrown(Dictionary<int, ObjectState> state, int regrowDays)
		{
			if (regrowDays <= 0) return 0;
			int today = Today;
			List<int> old = state.Where(s => s.Key < CreatureSpawner.StateKeyBase && today - s.Value.Day >= regrowDays).Select(s => s.Key).ToList();
			foreach (int ord in old) state.Remove(ord);
			return old.Count;
		}

		/// <summary>"ordinal,active,yield,day;..." for the world file and network messages.</summary>
		public static string Encode(Dictionary<int, ObjectState> state)
		{
			var sb = new StringBuilder();
			foreach (var kv in state.OrderBy(k => k.Key))
			{
				if (sb.Length > 0) sb.Append(';');
				sb.Append(kv.Key).Append(',').Append(kv.Value.Active ? 1 : 0).Append(',').Append(kv.Value.Yield).Append(',').Append(kv.Value.Day);
			}
			return sb.ToString();
		}

		public static Dictionary<int, ObjectState> Decode(string text)
		{
			var state = new Dictionary<int, ObjectState>();
			if (string.IsNullOrEmpty(text)) return state;
			foreach (string item in text.Split(';'))
			{
				string[] p = item.Split(',');
				int ord, active, yield, day;
				if (p.Length == 4 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ord) &&
					int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out active) &&
					int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out yield) &&
					int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out day))
					state[ord] = new ObjectState { Active = active != 0, Yield = yield, Day = day };
			}
			return state;
		}
	}
}
