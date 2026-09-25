using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

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

		/// <summary>Raft's place and the one the mod saw differ by more than this: Raft's is wrong (it was 200 m once).</summary>
		const float Misplaced = 20f;

		/// <summary>Dev tests (CIMisplace): the next time Raft puts the player back, move them this far off - as Raft once did.</summary>
		public static Vector3 TestOffset;

		/// <summary>A world was loaded (host) or received (client): remember where the local player is.</summary>
		public static void Begin()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			holding = false;
			spot = null;
			settled = null;
			if (player == null) return;
			Vector3 p = player.transform.position;
			// Where the host saw this player stand on a custom island (a joining player learns it from the host: GoTo)
			Vector3? known = Raft_Network.IsHost ? PlayerPlaces.PlaceOf(player.steamID.Id) : null;
			if (known.HasValue && (known.Value - p).magnitude > Misplaced)
			{
				Hold(known.Value, "Raft put the player " + (known.Value - p).magnitude.ToString("F0") + " m from where they stood on a custom island: back there");
				return;
			}
			// (a swimmer, or someone on the raft or on Raft's own islands, is left alone)
			if (p.y < 1f || Supported(p)) return;
			Hold(p, "The player is back at " + p.ToString("F1") + " with nothing under them yet: looking for the island they stood on");
		}

		/// <summary>A joining player: the host says where they stood on a custom island. Raft may have put them elsewhere
		/// (after the host loaded the world again, once 200 m off): then they go there, held until the island is in.</summary>
		public static void GoTo(Vector3 target, string island)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) return;
			Vector3 now = spot ?? player.transform.position;
			if ((now - target).magnitude < Misplaced) return;
			settled = null;
			Hold(target, "The host saw the player on '" + island + "', " + (now - target).magnitude.ToString("F0") + " m from where Raft put them: back there");
		}

		static void Hold(Vector3 at, string why)
		{
			spot = at;
			holding = false;
			until = Time.unscaledTime + (Raft_Network.IsHost ? HostWait : ClientWait);
			Debug.Log("[CUSTOM ISLANDS] " + why);
		}

		/// <summary>True while a player is held or just set down (their place isn't settled yet).</summary>
		public static bool Busy { get { return spot.HasValue || settled.HasValue; } }

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
		internal static IslandWorldState.Entry IslandUnder(Vector3 p)
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
		static void Postfix(Network_Player __instance)
		{
			if (__instance == null || !__instance.IsLocalPlayer) return;
			if (PlayerHold.TestOffset != Vector3.zero)
			{
				CharacterController cc = __instance.PersonController.controller;
				cc.enabled = false;
				__instance.transform.position += PlayerHold.TestOffset;
				cc.enabled = true;
				Debug.Log("[CUSTOM ISLANDS] Dev: the player put " + PlayerHold.TestOffset.ToString("F0") + " off, as Raft once did");
				PlayerHold.TestOffset = Vector3.zero;
			}
			PlayerHold.Begin();
		}
	}

	/// <summary>
	/// Host: where each player stands on a custom island (relative to it), seen every second and kept in the world's
	/// island file. Raft keeps players' places itself, but once put a guest returning after the host loaded the world
	/// again about 200 m off; the host's own record puts them back (PlayerHold). A player on the raft, swimming or on
	/// Raft's islands has no record: Raft's own place stands.
	/// </summary>
	public static class PlayerPlaces
	{
		class Place { public string Island; public Vector3 IslandAt; public Vector3 Offset; }

		const float CaptureInterval = 1f;
		/// <summary>A player who has just come (loaded, joined) isn't recorded yet: Raft or the hold may still be moving them.</summary>
		const float SettleIn = 30f;

		static readonly Dictionary<ulong, Place> places = new Dictionary<ulong, Place>();
		static readonly Dictionary<ulong, float> seenSince = new Dictionary<ulong, float>();
		static float nextCapture;

		public static void Reset() { places.Clear(); seenSince.Clear(); }

		/// <summary>Called every frame by the mod.</summary>
		public static void Tick()
		{
			if (!Raft_Network.IsHost || !LoadSceneManager.IsGameSceneLoaded || Time.unscaledTime < nextCapture) return;
			nextCapture = Time.unscaledTime + CaptureInterval;
			Capture();
		}

		/// <summary>Host: records where each player stands (on a custom island, or nowhere).</summary>
		public static void Capture()
		{
			if (!Raft_Network.IsHost) return;
			var present = new HashSet<ulong>();
			foreach (Network_Player p in Object.FindObjectsOfType<Network_Player>())
			{
				if (p == null) continue;
				ulong id = p.steamID.Id;
				present.Add(id);
				float since;
				if (!seenSince.TryGetValue(id, out since)) { seenSince[id] = Time.unscaledTime; continue; }
				if (Time.unscaledTime - since < SettleIn || (p.IsLocalPlayer && PlayerHold.Busy)) continue;
				IslandWorldState.Entry e = PlayerHold.IslandUnder(p.transform.position);
				if (e != null) places[id] = new Place { Island = e.HostName, IslandAt = e.Position, Offset = p.transform.position - e.Position };
				else places.Remove(id);
			}
			// (someone who left: their place stays; when they come back they settle in again first)
			foreach (ulong gone in seenSince.Keys.Where(k => !present.Contains(k)).ToList()) seenSince.Remove(gone);
		}

		/// <summary>Where this player stood on a custom island, now; null if they weren't on one (or it's gone).</summary>
		public static Vector3? PlaceOf(ulong id)
		{
			Place pl;
			IslandWorldState.Entry e = places.TryGetValue(id, out pl) ? IslandOf(pl) : null;
			return e != null ? e.Position + pl.Offset : (Vector3?)null;
		}

		static IslandWorldState.Entry IslandOf(Place pl)
		{
			return IslandWorldState.Islands.Where(e => e.HostName == pl.Island && (e.Position - pl.IslandAt).magnitude < 50f)
				.OrderBy(e => (e.Position - pl.IslandAt).magnitude).FirstOrDefault();
		}

		/// <summary>Host -> a player who just joined: where they stood (island id, offset from its middle), or null.</summary>
		public static IslandNetMessage PlaceMessage(ulong id)
		{
			Place pl;
			IslandWorldState.Entry e = places.TryGetValue(id, out pl) ? IslandOf(pl) : null;
			if (e == null) return null;
			return new IslandNetMessage { Kind = IslandNetMessage.PlayerPlace, Ids = new[] { e.Id }, Offsets = new[] { pl.Offset.x, pl.Offset.y, pl.Offset.z } };
		}

		public static void OnWorldShift(Vector3 shift) { foreach (Place pl in places.Values) pl.IslandAt -= shift; }

		/// <summary>The world file's lines: "@place=steamid|island|x|y|z of the island|x|y|z from it".</summary>
		public static IEnumerable<string> WriteLines()
		{
			Capture();
			foreach (var kv in places)
				yield return string.Format(CultureInfo.InvariantCulture, "@place={0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}", kv.Key, kv.Value.Island.Replace("|", "/"),
					kv.Value.IslandAt.x, kv.Value.IslandAt.y, kv.Value.IslandAt.z, kv.Value.Offset.x, kv.Value.Offset.y, kv.Value.Offset.z);
		}

		public static bool ReadLine(string key, string value)
		{
			if (!key.Equals("place", StringComparison.OrdinalIgnoreCase)) return false;
			string[] p = value.Split('|');
			ulong id;
			float[] f = new float[6];
			if (p.Length != 8 || !ulong.TryParse(p[0], out id)) return true;
			for (int i = 0; i < 6; i++) if (!float.TryParse(p[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return true;
			places[id] = new Place { Island = p[1], IslandAt = new Vector3(f[0], f[1], f[2]), Offset = new Vector3(f[3], f[4], f[5]) };
			return true;
		}
	}
}
