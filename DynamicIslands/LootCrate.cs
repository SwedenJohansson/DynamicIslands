using System;
using System.Collections.Generic;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Things on an island that players use up and that must stay used for everyone: looted chests (and later
	/// triggers). They live in the island's object state (saved with the world, sent to players who join) under
	/// keys above the 16 bits Raft's pickups use, and a change travels as one IslandNetMessage.ObjectUsed.
	/// </summary>
	public static class ContentState
	{
		public const int LootKeyBase = 0x20000;

		static int Today { get { try { return WorldManager.DayCounter; } catch { return 0; } } }

		/// <summary>The world's entry of the island this object is part of (null in the editor).</summary>
		public static IslandWorldState.Entry EntryOf(Transform t)
		{
			if (t == null) return null;
			GameObject root = t.root.gameObject;
			return IslandWorldState.Islands.FirstOrDefault(e => e.Root == root);
		}

		public static bool IsUsed(IslandWorldState.Entry e, int key)
		{
			ObjectState s;
			return e != null && e.State.TryGetValue(key, out s) && !s.Active;
		}

		/// <summary>This player used it: remember it and tell the others.</summary>
		public static void MarkUsed(Transform t, int key)
		{
			IslandWorldState.Entry e = EntryOf(t);
			if (e == null) return;
			int day = Today;
			e.State[key] = new ObjectState { Active = false, Yield = 0, Day = day };
			IslandNetwork.SendUsed(e.Id, key, day);
			AfterChange(e, key, day);
		}

		static bool IsZoneKey(int key) { return key >= TriggerZone.KeyBase && key < TriggerZone.KeyBase + 0x10000; }

		/// <summary>Host: a zone that fired wakes up the creatures waiting for it.</summary>
		static void AfterChange(IslandWorldState.Entry e, int key, int day)
		{
			if (day >= 0 && IsZoneKey(key) && Raft_Network.IsHost) CreatureSpawner.OnIslandReady(e);
		}

		/// <summary>From the network: day &lt; 0 = available again (refilled).</summary>
		public static void ApplyUsed(int islandId, int key, int day)
		{
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == islandId);
			if (e == null) return;
			if (day < 0) e.State.Remove(key);
			else e.State[key] = new ObjectState { Active = false, Yield = 0, Day = day };
			AfterChange(e, key, day);
		}

		/// <summary>Host: an island loaded; chests that were looted long enough ago fill up again (unless the builder said never).</summary>
		public static void OnIslandReady(IslandWorldState.Entry e)
		{
			if (e == null || e.Root == null || !Raft_Network.IsHost) return;
			int days = IslandRules.RegrowDays(e);
			if (days <= 0) return;
			var keys = e.Root.GetComponentsInChildren<LootCrate>(true).Where(c => c.Refills).Select(c => c.StateKey)
				.Concat(e.Root.GetComponentsInChildren<TriggerZone>(true).Select(z => z.StateKey)).ToList();
			foreach (int key in keys)
			{
				ObjectState s;
				if (!e.State.TryGetValue(key, out s) || Today - s.Day < days) continue;
				e.State.Remove(key);
				IslandNetwork.SendUsed(e.Id, key, -1);
			}
		}
	}

	/// <summary>
	/// A container on a custom island with the builder's loot. Looking at it shows Raft's "[E] Open ..." hint; opening
	/// gives the items to the player's inventory (what doesn't fit is dropped in front of them, as Raft does) and
	/// leaves it empty for everyone until it fills up again after CustomIslandSpawner.RegrowDays in-game days.
	/// </summary>
	public class LootCrate : MonoBehaviour, IRaycastable
	{
		public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>();
		public bool Refills = true;
		public string Label = "chest";
		/// <summary>Place among the island's loot containers (file order: the same on every machine).</summary>
		public int Ordinal;
		public int StateKey { get { return ContentState.LootKeyBase + Ordinal; } }

		public static LootCrate Attach(GameObject go, string name, IDictionary<string, string> props, int ordinal)
		{
			GameObject holder = CustomNote.InteractHolder(go);
			LootCrate c = holder.AddComponent<LootCrate>();
			c.Items = ObjectProps.Loot(props);
			c.Refills = ObjectProps.LootRefills(props);
			c.Ordinal = ordinal;
			c.Label = PlaceableCatalog.DisplayName(name).ToLowerInvariant();
			return c;
		}

		public bool Looted { get { return ContentState.IsUsed(ContentState.EntryOf(transform), StateKey); } }

		void IRaycastable.OnIsRayed()
		{
			if (NoteReader.IsOpen) return;
			DisplayTextManager hints = CustomNote.Hints;
			bool looted = Looted || Items.Count == 0;
			CustomNote note = GetComponent<CustomNote>();
			if (hints != null)
			{
				if (!looted) hints.ShowText("Open the " + Label, CustomNote.InteractKey, 0, 0, true);
				else if (note != null) hints.ShowText("Read " + (note.Title.Length > 0 ? "\"" + note.Title + "\"" : "the note") + " (the " + Label + " is empty)", CustomNote.InteractKey, 0, 0, true);
				else hints.ShowText("The " + Label + " is empty", 0, true, 0);
			}
			if (!looted && CustomNote.InteractPressed())
			{
				if (hints != null) hints.HideDisplayTexts();
				Open();
			}
		}

		void IRaycastable.OnRayEnter() { }

		void IRaycastable.OnRayExit()
		{
			DisplayTextManager hints = CustomNote.Hints;
			if (hints != null) hints.HideDisplayTexts();
		}

		/// <summary>Gives the loot to the local player and marks the container empty. Returns what was given ("Plank x12").</summary>
		public List<string> Open()
		{
			if (Looted) return new List<string>();
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || player.Inventory == null) return new List<string>();
			// Marked first, so a second press in the same moment can't give it twice
			ContentState.MarkUsed(transform, StateKey);
			List<string> given = TriggerZone.Give(Items); // a full inventory: the rest lands in front of the player
			Debug.Log("[CUSTOM ISLANDS] Opened a " + Label + ": " + string.Join(", ", given.ToArray()));
			// A note inside: show it
			CustomNote note = GetComponent<CustomNote>();
			if (note != null) NoteReader.Open(note);
			return given;
		}
	}
}
