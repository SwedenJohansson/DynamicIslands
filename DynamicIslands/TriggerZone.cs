using System;
using System.Collections.Generic;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A trigger zone of a custom island in a world (invisible). Every machine watches its own player: walking in shows
	/// the builder's message, gives the zone's items, and (through the host) wakes up the creatures linked to the zone.
	/// A zone fires once per world (for everyone, until it resets after the island's regrow time) or every time a
	/// player walks in (then at most every half minute per player). Its "has fired" state is shared like a looted chest.
	/// </summary>
	public class TriggerZone : MonoBehaviour
	{
		public const int KeyBase = 0x30000;
		const float RepeatCooldown = 30f;

		public string Id = "", Message = "";
		public float Radius = 6f;
		public bool Repeats;
		/// <summary>An air pocket: a player inside breathes (ObjectProps.ZoneAir) - while the zone is shown.</summary>
		public bool Air;
		/// <summary>Fires once ever: never ready again after the regrow days (a story's once-zone fired again: a double
		/// ambush, a bridge flipped back).</summary>
		public bool OnceEver;
		public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>();
		/// <summary>Place among the island's zones (file order: the same on every machine).</summary>
		public int Ordinal;
		public int StateKey { get { return KeyBase + Ordinal; } }

		bool inside;
		float nextCheck, cooldownUntil;

		/// <summary>Raised on every machine when the local player sets a zone off (tests and quests listen).</summary>
		public static event Action<TriggerZone> Fired;

		public static TriggerZone Create(Transform parent, IslandObject o, int ordinal)
		{
			var go = new GameObject("TriggerZone_" + ordinal);
			go.transform.SetParent(parent, false);
			go.transform.position = parent.position + o.Position;
			var z = go.AddComponent<TriggerZone>();
			z.Id = ObjectProps.Get(o.Props, ObjectProps.ZoneId);
			z.Message = ObjectProps.Get(o.Props, ObjectProps.ZoneMessage);
			z.Radius = ObjectProps.Radius(o.Props);
			z.Repeats = ObjectProps.Repeats(o.Props);
			z.OnceEver = ObjectProps.Get(o.Props, ObjectProps.ZoneRepeat) == ObjectProps.ZoneOnceEver;
			z.Items = ObjectProps.Loot(o.Props);
			z.Air = ObjectProps.GetBool(o.Props, ObjectProps.ZoneAir, false);
			z.Ordinal = ordinal;
			return z;
		}

		public bool HasFired { get { return ContentState.IsUsed(ContentState.EntryOf(transform), StateKey); } }

		void Update()
		{
			if (Time.time < nextCheck) return;
			nextCheck = Time.time + 0.25f;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) return;
			bool now = (player.transform.position - transform.position).sqrMagnitude <= Radius * Radius;
			if (now && !inside) Enter();
			inside = now;
			if (now && Air) Breathe(player);
		}

		/// <summary>A player in an air pocket breathes: breath back to full.</summary>
		public static void Breathe(Network_Player player)
		{
			try { if (player.Stats != null && player.Stats.stat_oxygen != null) player.Stats.stat_oxygen.Value = player.Stats.stat_oxygen.Max; }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Breathing in an air pocket: " + e.Message); }
		}

		/// <summary>The local player walked in (tests call it directly). granted: again, now that the host said yes.</summary>
		public void Enter() { Enter(false); }

		void Enter(bool granted)
		{
			// A quest step "go to this zone" counts every time (even when the zone itself has fired already)
			if (!granted) QuestTracker.Event(ContentState.EntryOf(transform), "reach", Id);
			bool fired = HasFired;
			if (fired && !Repeats) return;
			if (!Repeats && !granted) Debug.Log("[CUSTOM ISLANDS] Trigger zone '" + Id + "' entered");
			if (Repeats && Time.time < cooldownUntil) return;
			// (a zone the player can't set off yet says why before it is claimed: claimed, it was held from the others)
			IslandObjectRef self = GetComponent<IslandObjectRef>();
			if (!fired && !Repeats && !granted && self != null && !Behaviours.WouldAllow(ContentState.EntryOf(transform), self.Index, "enter"))
			{
				Behaviours.Allows(ContentState.EntryOf(transform), self.Index, "enter");
				return;
			}
			// A zone that fires once fires for one player: a client asks the host first (Claims)
			if (!fired && !Repeats && !Claims.May(ContentState.EntryOf(transform), StateKey, yes => { if (yes && this != null) Enter(true); })) return;
			// Its "only if" checks first: when they fail the player is told why and the zone stays ready - not marked
			// as fired, no cooldown. (Before, a failed check still used the zone up: a treasure map's X crossed
			// without the map did nothing when the player came back with it within half a minute, and a zone that
			// fires once was spent for good. Found in the two-player test.)
			IslandObjectRef r = GetComponent<IslandObjectRef>();
			IslandWorldState.Entry entry = ContentState.EntryOf(transform);
			if (r != null && !Behaviours.Allows(entry, r.Index, "enter")) return;
			cooldownUntil = Time.time + RepeatCooldown;

			if (Message.Length > 0) IslandInfo.ShowMessage(Message);
			if (Items.Count > 0) Give(Items);
			if (!fired) ContentState.MarkUsed(transform, StateKey); // the host wakes up the linked creatures
			Debug.Log("[CUSTOM ISLANDS] Trigger zone '" + Id + "' set off" + (Message.Length > 0 ? ": " + Message : ""));
			if (r != null) Behaviours.Fire(entry, r.Index, "enter", true, true); // (checked above)
			if (Fired != null) try { Fired(this); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Zone listener: " + e.Message); }
		}

		/// <summary>Puts items in the local player's inventory; what doesn't fit is dropped in front of them. Story items go to the crew's journal.</summary>
		public static List<string> Give(IEnumerable<KeyValuePair<string, int>> items)
		{
			var given = new List<string>();
			Network_Player player = RAPI.GetLocalPlayer();
			PlayerInventory inv = player != null ? player.Inventory : null;
			if (inv == null) return given;
			foreach (KeyValuePair<string, int> l in items)
			{
				if (StoryItems.IsStory(l.Key)) { StoryBook.Give(l.Key, l.Value); given.Add(StoryItems.Label(l.Key) + (l.Value > 1 ? " \u00D7" + l.Value : "")); continue; }
				Item_Base item = ItemManager.GetItemByName(l.Key);
				if (item == null) { Debug.LogWarning("[CUSTOM ISLANDS] Raft has no item '" + l.Key + "'"); continue; }
				try
				{
					int before = inv.GetItemCount(l.Key);
					inv.AddItem(l.Key, l.Value);
					int left = l.Value - (inv.GetItemCount(l.Key) - before);
					if (left > 0) inv.DropItem(item, left);
					given.Add(ContentCatalog.ItemLabel(l.Key) + " \u00D7" + l.Value + (left > 0 ? " (" + left + " dropped)" : ""));
				}
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Giving " + l.Key + " failed: " + e.Message); }
			}
			return given;
		}
	}
}
