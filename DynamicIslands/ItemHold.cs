using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Loose items a player dropped on a custom island wait in the air while their ground isn't there (ROADMAP RK2).
	/// Raft drops an item as a free rigidbody in the world (PickupObjectManager.DropItem), not on the island. An island
	/// that unloads (the raft far away) takes its ground away, so the items fell into the sea and were lost. Now such an
	/// item is made kinematic (held where it lies) and let go a moment after its island stands again. Every machine does
	/// it for its own copies: dropped items are simulated on each machine.
	/// Raft keeps no dropped items in a save at all (PickupObjectManager's save message is always empty), so those on a
	/// custom island are written to the world's island file ("@dropped=" lines, host) and dropped again where they lay
	/// once their island stands after the world is loaded.
	/// </summary>
	public static class ItemHold
	{
		class Held
		{
			public PickupItem Item;
			public Rigidbody Body;
			/// <summary>The island it lies on; null while that isn't known yet (just loaded, the island list not here).</summary>
			public IslandWorldState.Entry Entry;
			/// <summary>When its island was first seen standing again (-1: not yet).</summary>
			public float StandingSince = -1f;
			public float Since;
		}

		static readonly List<Held> held = new List<Held>();
		/// <summary>Seconds an island stands before its items are let go (its colliders are in).</summary>
		const float SettleSeconds = 1f;
		/// <summary>Items not on any island's ground are let go after this long anyway.</summary>
		const float PendingMaxSeconds = 90f;
		/// <summary>Metres past the land radius that still count as the island's ground (its beaches, piers).</summary>
		const float Margin = 12f;
		/// <summary>The land radius used for an island whose file isn't here yet (a client's download).</summary>
		const float UnknownRadius = 100f;

		/// <summary>Tests: how many items are held now (on this island, or any).</summary>
		internal static int Count(IslandWorldState.Entry e = null) { return held.Count(h => h.Item != null && (e == null || h.Entry == e)); }

		/// <summary>Tests: the items held now.</summary>
		internal static IEnumerable<PickupItem> Items { get { return held.Where(h => h.Item != null).Select(h => h.Item); } }

		/// <summary>A dropped item read from the world file, waiting for its island to stand (host).</summary>
		class Saved
		{
			public string Island;
			public Vector3 Offset, Euler;
			public string Item, Exclusive;
			public int Amount, Uses;
			public float StandingSince = -1f;
		}
		static readonly List<Saved> saved = new List<Saved>();

		/// <summary>Tests: items read from the loaded world file, and how many of them were dropped again.</summary>
		internal static int SavedRead, SavedPlaced;
		/// <summary>Tests: items from the world file still waiting for their island.</summary>
		internal static int SavedWaiting { get { return saved.Count; } }

		/// <summary>True while this dropped item is held (kinematic, waiting for its island).</summary>
		internal static bool IsHeld(PickupItem p) { return p != null && held.Any(h => h.Item == p); }

		/// <summary>The island is unloading: holds the dropped items on its ground (before its colliders go).</summary>
		public static void HoldOn(IslandWorldState.Entry e)
		{
			if (e == null) return;
			int n = 0;
			foreach (PickupItem p in Dropped())
				if (Inside(e, p.transform.position) && Hold(p, e)) n++;
			if (n > 0) Debug.Log("[CUSTOM ISLANDS] " + n + " dropped item(s) on '" + e.Name + "' wait for the island to load again");
		}

		/// <summary>The world's island file is about to be read: forgets the items of the world before.</summary>
		internal static void ClearSaved() { saved.Clear(); SavedRead = 0; SavedPlaced = 0; }

		/// <summary>The "@dropped=" lines for the world file: every dropped item lying on (or held over) a custom island,
		/// and those from the file not dropped again yet.</summary>
		internal static IEnumerable<string> WriteLines()
		{
			var lines = new List<string>();
			foreach (PickupItem p in Dropped(true))
			{
				if (p.itemInstance == null || p.itemInstance.baseItem == null) continue;
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => Inside(x, p.transform.position));
				if (e == null) continue;
				lines.Add(Line(e.HostName, p.transform.position - e.Position, p.transform.eulerAngles, p.itemInstance.UniqueName,
					p.itemInstance.Amount, p.itemInstance.Uses, p.itemInstance.exclusiveString));
			}
			foreach (Saved s in saved) lines.Add(Line(s.Island, s.Offset, s.Euler, s.Item, s.Amount, s.Uses, s.Exclusive));
			return lines;
		}

		static string Line(string island, Vector3 o, Vector3 r, string item, int amount, int uses, string exclusive)
		{
			string ex = (exclusive ?? "").Replace("|", "/").Replace("\n", " ").Replace("\r", " ");
			return string.Format(CultureInfo.InvariantCulture, "@dropped={0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}", island.Replace("|", "/"),
				o.x, o.y, o.z, r.x, r.y, r.z, item, amount, uses, ex);
		}

		/// <summary>Reads one "@dropped=" line of the world file (key without the "@").</summary>
		internal static bool ReadLine(string key, string value)
		{
			if (!key.Equals("dropped", StringComparison.OrdinalIgnoreCase)) return false;
			string[] f = value.Split('|');
			if (f.Length < 10) return true;
			Func<int, float> num = i => float.Parse(f[i], CultureInfo.InvariantCulture);
			int amount, uses;
			if (!int.TryParse(f[8], out amount) || !int.TryParse(f[9], out uses) || amount < 1) return true;
			saved.Add(new Saved { Island = f[0], Offset = new Vector3(num(1), num(2), num(3)), Euler = new Vector3(num(4), num(5), num(6)),
				Item = f[7], Amount = amount, Uses = uses, Exclusive = f.Length > 10 ? f[10] : "" });
			SavedRead++;
			return true;
		}

		/// <summary>Called every frame by the mod.</summary>
		public static void Tick()
		{
			if (saved.Count > 0) PlaceSaved();
			if (held.Count == 0) return;
			if (!LoadSceneManager.IsGameSceneLoaded) { held.Clear(); return; }
			float now = Time.unscaledTime;
			bool known = Raft_Network.IsHost ? IslandWorldState.ForThisWorld : IslandNetwork.HasList;
			for (int i = held.Count - 1; i >= 0; i--)
			{
				Held h = held[i];
				// (picked up, or gone with the world)
				if (h.Item == null || h.Body == null || !h.Item.gameObject.activeInHierarchy) { Let(h, false); held.RemoveAt(i); continue; }
				if (h.Entry != null && !IslandWorldState.Contains(h.Entry)) { h.Entry = null; h.StandingSince = -1f; }
				if (h.Entry == null)
				{
					h.Entry = known ? IslandWorldState.Islands.FirstOrDefault(e => Inside(e, h.Item.transform.position)) : null;
					if (h.Entry == null)
					{
						// (on no custom island: Raft's own ground, the raft, the sea - nothing to wait for)
						if (known || now - h.Since > PendingMaxSeconds) { Let(h, true); held.RemoveAt(i); }
						continue;
					}
				}
				IslandWorldState.Entry en = h.Entry;
				// (an island that will never load here: let go)
				if (en.Failed || en.Excluded) { Let(h, true); held.RemoveAt(i); continue; }
				if (en.Root == null || en.Loading) { h.StandingSince = -1f; continue; }
				if (h.StandingSince < 0f) { h.StandingSince = now; continue; }
				if (now - h.StandingSince >= SettleSeconds) { Let(h, true); held.RemoveAt(i); }
			}
		}

		/// <summary>Host: drops the items from the world file again once their island has stood a moment.</summary>
		static void PlaceSaved()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !IslandWorldState.ForThisWorld) return;
			float now = Time.unscaledTime;
			for (int i = saved.Count - 1; i >= 0; i--)
			{
				Saved s = saved[i];
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => string.Equals(x.HostName, s.Island, StringComparison.OrdinalIgnoreCase));
				// (its island is gone from the world: the item goes with it)
				if (e == null) { saved.RemoveAt(i); continue; }
				// (an island that can't load here keeps them in the file for a host that can)
				if (e.Failed || e.Excluded || e.Root == null || e.Loading) { s.StandingSince = -1f; continue; }
				if (s.StandingSince < 0f) { s.StandingSince = now; continue; }
				if (now - s.StandingSince < SettleSeconds) continue;
				saved.RemoveAt(i);
				Item_Base b = ItemManager.GetItemByName(s.Item);
				if (b == null) { Debug.LogWarning("[CUSTOM ISLANDS] A dropped item on '" + e.Name + "' is no item here: " + s.Item); continue; }
				Vector3 at = e.Position + s.Offset;
				var before = new HashSet<int>(UnityEngine.Object.FindObjectsOfType<PickupItem>().Select(p => p.GetInstanceID()));
				Helper.DropItem(new ItemInstance(b, s.Amount, s.Uses, s.Exclusive ?? ""), at + Vector3.up * 0.2f, Vector3.zero, false);
				// (Raft throws a dropped item up a little: this one lies where it lay)
				foreach (PickupItem p in UnityEngine.Object.FindObjectsOfType<PickupItem>())
				{
					if (p == null || before.Contains(p.GetInstanceID())) continue;
					p.transform.eulerAngles = s.Euler;
					Rigidbody rb = p.GetComponent<Rigidbody>();
					if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
				}
				SavedPlaced++;
				Debug.Log("[CUSTOM ISLANDS] A dropped item (" + s.Item + ") from the save lies on '" + e.Name + "' again");
			}
		}

		static bool Hold(PickupItem p, IslandWorldState.Entry e)
		{
			Rigidbody rb = p.GetComponent<Rigidbody>();
			if (rb == null || rb.isKinematic) return false;
			if (held.Any(h => h.Item == p)) return false;
			rb.velocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
			rb.isKinematic = true;
			held.Add(new Held { Item = p, Body = rb, Entry = e, Since = Time.unscaledTime });
			return true;
		}

		static void Let(Held h, bool log)
		{
			if (h.Body != null) h.Body.isKinematic = false;
			if (log && h.Item != null && h.Entry != null) Debug.Log("[CUSTOM ISLANDS] A dropped item on '" + h.Entry.Name + "' is let go: the island stands");
		}

		/// <summary>Dropped items lying free in the world (not on the raft); with inactiveToo also switched-off ones.</summary>
		static IEnumerable<PickupItem> Dropped(bool inactiveToo = false)
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			Transform raftT = raft != null ? raft.transform : null;
			foreach (PickupItem p in UnityEngine.Object.FindObjectsOfType<PickupItem>())
			{
				if (p == null || !p.isDropped || (!inactiveToo && !p.gameObject.activeInHierarchy)) continue;
				if (raftT != null && p.transform.IsChildOf(raftT)) continue;
				yield return p;
			}
		}

		/// <summary>Over this island's ground (to its land radius and a margin, at any height).</summary>
		static bool Inside(IslandWorldState.Entry e, Vector3 at)
		{
			if (e == null || e.Excluded || e.Failed) return false;
			float r = e.WaitingForFile ? -1f : CustomIslandSpawner.LandRadius(e.Name);
			if (r < 0f) r = UnknownRadius;
			Vector3 d = at - e.Position;
			d.y = 0f;
			return d.magnitude <= r + Margin;
		}
	}
}
