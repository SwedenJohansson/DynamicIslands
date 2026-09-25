using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Keeps a player who was standing on a custom island there when a world is loaded or joined again.
	/// Raft puts the player back where they were before the mod has spawned its islands (a moment later, streamed
	/// by distance), so they fell through into the sea - and Raft's own SetToValidSpawnPointLate, 3 s after
	/// loading, then moved a swimming player more than 100 m from the raft onto the raft (it leaves players in its
	/// own islands' Regions alone; custom islands have none). Found by the persistence test (alltests.ps1 persist).
	/// Now the player is held where they were while the island under them loads, then set down on it.
	/// </summary>
	public static class PlayerHold
	{
		/// <summary>How long to wait for the island: the host has the file; a client may still be downloading it.</summary>
		const float HostWait = 20f, ClientWait = 60f;
		/// <summary>Nothing this close below the player: they are standing on something that isn't there yet.</summary>
		const float SupportRay = 3.5f;
		/// <summary>Islands whose centre is this close may be the one under the player (the largest land is about 500 m across).</summary>
		const float NearIsland = 600f;

		/// <summary>After setting a player down, watch this long: an island that has just come up can still let them through.</summary>
		const float SettleTime = 5f;

		static Vector3? spot;
		static float until;
		static bool holding;
		// Set down, still watched: where, until when, how often put back
		static Vector3? settled;
		static float settleUntil;
		static int putBack;

		/// <summary>A world was loaded (host) or received (client): remember where the local player is.</summary>
		public static void Begin()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			holding = false;
			spot = null;
			settled = null;
			if (player == null) return;
			Vector3 p = player.transform.position;
			// (a swimmer, or someone on the raft or on Raft's own islands, is left alone)
			if (p.y < 1f || Supported(p)) return;
			spot = p;
			until = Time.unscaledTime + (Raft_Network.IsHost ? HostWait : ClientWait);
			Debug.Log("[CUSTOM ISLANDS] The player is back at " + p.ToString("F1") + " with nothing under them yet: looking for the island they stood on");
		}

		/// <summary>Called every frame by the mod.</summary>
		public static void Tick()
		{
			if (settled.HasValue) { Settle(); return; }
			if (!spot.HasValue) return;
			if (Time.unscaledTime > until) { Release(null, "gave up waiting for the island"); return; }
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) return;
			Vector3 at = spot.Value;
			// The island under them is there (its own ground, loaded): set them down where they were, and watch a moment
			IslandWorldState.Entry under = IslandUnder(at);
			if (under != null)
			{
				settled = at;
				settleUntil = Time.unscaledTime + SettleTime;
				putBack = 0;
				Release(player, "set down on '" + under.Name + "'");
				return;
			}
			// (the first frames after loading the scene isn't marked loaded and the islands aren't streaming yet: just hold)
			if (IslandNetwork.HasList && LoadSceneManager.IsGameSceneLoaded)
			{
				// (every custom island around is there and still nothing under them: they weren't on one - let go)
				bool waiting = IslandWorldState.Islands.Any(e => !e.Failed && (e.Root == null || e.Loading) && Flat(e.Position - at) < NearIsland);
				if (!waiting) { Release(null, "was not on a custom island"); return; }
			}
			// Waiting: keep the player where they were (they would fall through into the sea)
			if (!holding) { holding = true; Debug.Log("[CUSTOM ISLANDS] Holding the player at " + at.ToString("F1") + " until the island under them has loaded"); }
			Put(player, at);
		}

		/// <summary>Just set down: if they drop through (an island that has just come up), back to where they stood.</summary>
		static void Settle()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || Time.unscaledTime > settleUntil) { settled = null; return; }
			Vector3 at = settled.Value, p = player.transform.position;
			if (p.y < at.y - 2.5f && Flat(p - at) < 6f && putBack < 5)
			{
				putBack++;
				Put(player, at);
				settleUntil = Time.unscaledTime + SettleTime;
				Debug.Log("[CUSTOM ISLANDS] The player dropped through after being set down: back at " + at.ToString("F1") + " (" + putBack + ")");
			}
		}

		public static void OnWorldShift(Vector3 shift)
		{
			if (spot.HasValue) spot = spot.Value - shift;
			if (settled.HasValue) settled = settled.Value - shift;
		}

		static void Release(Network_Player player, string why)
		{
			if (player != null && spot.HasValue) Put(player, spot.Value);
			if (why != null) Debug.Log("[CUSTOM ISLANDS] Player " + why + (spot.HasValue ? " at " + spot.Value.ToString("F1") : ""));
			spot = null;
			holding = false;
		}

		static void Put(Network_Player player, Vector3 at)
		{
			CharacterController cc = player.PersonController.controller;
			cc.enabled = false;
			player.transform.position = at;
			if (player.PersonController.controllerType != ControllerType.Ground) player.PersonController.SwitchControllerType(ControllerType.Ground);
			cc.enabled = true;
		}

		static bool Supported(Vector3 p)
		{
			int mask = ~(1 << LayerMask.NameToLayer("LocalPlayer"));
			return Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, SupportRay + 0.5f, mask, QueryTriggerInteraction.Ignore);
		}

		/// <summary>The loaded custom island whose own colliders are right under this spot, or null.</summary>
		static IslandWorldState.Entry IslandUnder(Vector3 p)
		{
			int mask = ~(1 << LayerMask.NameToLayer("LocalPlayer"));
			foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, SupportRay + 0.5f, mask, QueryTriggerInteraction.Ignore))
			{
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Root != null && !x.Loading && hit.collider.transform.IsChildOf(x.Root.transform));
				if (e != null) return e;
			}
			return null;
		}

		static float Flat(Vector3 v) { return new Vector2(v.x, v.z).magnitude; }
	}

	/// <summary>
	/// Raft's GameManager.OnWorldRecieved restores the world (the mod's LoadComplete handlers run in there), then puts
	/// the player where they were (SaveAndLoad.RestoreUser) and calls SetToValidSpawnPoint: from then on the player's
	/// place is known - on loading and on joining.
	/// </summary>
	[HarmonyLib.HarmonyPatch(typeof(Network_Player), "SetToValidSpawnPoint")]
	static class SetToValidSpawnPointPatch
	{
		static void Postfix(Network_Player __instance) { if (__instance != null && __instance.IsLocalPlayer) PlayerHold.Begin(); }
	}
}
