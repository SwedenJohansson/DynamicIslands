using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Buried treasure for the metal detector and the shovel (the user, 2026-10-05: "vines, ziplines, metal detection" on
	/// the islands, every Raft feature on at least one). A "Buried treasure" marker in the editor; in a world it becomes
	/// Raft's own treasure point (TreasurePointManager.AddTreasurePoint): the detector beeps near it, three digs with the
	/// shovel bring up the chest, and the chest gives Raft's treasure loot. Every machine makes the same points with the
	/// same network indexes (from the island's id, above its pickups'), so digging and picking reach the other players.
	/// Dug up = remembered like a picked pickup (its ordinal in the island's state) and back after the regrow days.
	/// </summary>
	public class BuriedTreasure : MonoBehaviour
	{
		/// <summary>Raft's treasure kind (its uniqueTreasureIndex: 0 = the common chest).</summary>
		public int Kind;
		/// <summary>Its place among the island's buried treasures.</summary>
		public int Number;
		/// <summary>The treasure point made for it (null: dug up before, or not made yet).</summary>
		public TreasurePoint Point;
		/// <summary>Whether a point was made for it on this load (not: dug up before and not grown back).</summary>
		public bool Made;

		/// <summary>The network indexes of the island's n-th treasure: the point's and its chest's (the chest's low 16 bits are the state ordinal).</summary>
		public static void Indexes(int islandId, int n, out uint point, out uint chest)
		{
			uint b = 0x40000000u | ((uint)(islandId & 0x3FFF) << 16) | 0xC000u;
			point = b + (uint)(n * 2);
			chest = point + 1;
		}

		public static int Ordinal(int islandId, int n) { uint p, c; Indexes(islandId, n, out p, out c); return (int)(c & 0xFFFF); }

		static TreasurePointManager Manager { get { return UnityEngine.Object.FindObjectOfType<TreasurePointManager>(); } }

		/// <summary>Makes the island's treasure points, except those dug up and not grown back (after its state was applied).</summary>
		public static void OnIslandReady(IslandWorldState.Entry e)
		{
			if (e == null || e.Root == null) return;
			BuriedTreasure[] all = e.Root.GetComponentsInChildren<BuriedTreasure>(true);
			if (all.Length == 0) return;
			TreasurePointManager tm = Manager;
			if (tm == null) { Debug.LogWarning("[CUSTOM ISLANDS] No TreasurePointManager: buried treasure left out"); return; }
			int made = 0;
			foreach (BuriedTreasure b in all)
			{
				ObjectState s;
				if (e.State.TryGetValue(Ordinal(e.Id, b.Number), out s) && !s.Active) continue;
				uint point, chest;
				Indexes(e.Id, b.Number, out point, out chest);
				try
				{
					tm.AddTreasurePoint(point, chest, e.Root.transform, e.Root.transform.InverseTransformPoint(b.transform.position), b.Kind);
					b.Point = PointsOf(tm, e.Root.transform).LastOrDefault();
					b.Made = true;
					made++;
				}
				catch (Exception ex) { Debug.LogError("[CUSTOM ISLANDS] Buried treasure on '" + e.HostName + "': " + ex.Message); }
			}
			if (made > 0) Debug.Log("[CUSTOM ISLANDS] '" + e.HostName + "': " + made + " buried treasure(s)");
		}

		/// <summary>Records the dug-up treasures in the island's state (with IslandObjectState.Capture).</summary>
		public static void Capture(IslandWorldState.Entry e, int today)
		{
			if (e == null || e.Root == null) return;
			foreach (BuriedTreasure b in e.Root.GetComponentsInChildren<BuriedTreasure>(true))
			{
				// (not made on this load: dug up before, its state stays as it is)
				if (!b.Made) continue;
				int ord = Ordinal(e.Id, b.Number);
				bool gone = b.Point == null || b.Point.pickupNetworked == null || !b.Point.pickupNetworked.gameObject.activeInHierarchy;
				ObjectState old;
				if (!gone) { e.State.Remove(ord); continue; }
				if (e.State.TryGetValue(ord, out old) && !old.Active) continue;
				e.State[ord] = new ObjectState { Active = false, Yield = -1, Day = today };
			}
		}

		/// <summary>Takes the island's treasure points out of Raft's manager (the island unloads).</summary>
		public static void OnDespawn(GameObject root)
		{
			if (root == null || root.GetComponentInChildren<BuriedTreasure>(true) == null) return;
			TreasurePointManager tm = Manager;
			if (tm == null) return;
			try { tm.RemoveTreasurePointsViaTransformReference(root.transform); }
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Removing buried treasure: " + ex.Message); }
		}

		static readonly FieldInfo pointsField = typeof(TreasurePointManager).GetField("treasurePoints", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

		/// <summary>Every treasure point Raft's manager keeps.</summary>
		public static List<TreasurePoint> AllPoints(TreasurePointManager tm)
		{
			var d = pointsField != null ? pointsField.GetValue(tm) as Dictionary<Transform, List<TreasurePoint>> : null;
			return d != null ? d.Values.SelectMany(l => l).Where(p => p != null).ToList() : new List<TreasurePoint>();
		}

		/// <summary>The treasure points Raft's manager keeps under a parent.</summary>
		public static List<TreasurePoint> PointsOf(TreasurePointManager tm, Transform parent)
		{
			var d = pointsField != null ? pointsField.GetValue(tm) as Dictionary<Transform, List<TreasurePoint>> : null;
			List<TreasurePoint> l;
			if (parent == null) return new List<TreasurePoint>();
			return d != null && d.TryGetValue(parent, out l) ? l.Where(p => p != null).ToList() : new List<TreasurePoint>();
		}
	}
}
