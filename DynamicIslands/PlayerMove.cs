using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Moves the local player somewhere at once (a "teleport to" action, Test in a world putting them on the island, a player
	/// held and set down on their island after loading). Standing on the raft, Raft keeps the player a child of the raft: moved
	/// far off like that, the raft's rocking swung them through the ground of an island 300 m away (a 1° tilt moves a child
	/// that far about 5 m) - seen as a player dropping through an island whose ground was there. So: off the raft first, and
	/// nothing left of a fall or a push from before.
	/// </summary>
	public static class PlayerMove
	{
		public static void To(Network_Player player, Vector3 at, ControllerType type = ControllerType.Ground)
		{
			if (player == null) return;
			PersonController pc = player.PersonController;
			CharacterController cc = pc != null ? pc.controller : null;
			if (cc != null) cc.enabled = false;
			// (off any ladder first: a player moved away while holding one stays in Raft's climbing state - no falling)
			if (pc != null && pc.climbing)
			{
				try { pc.DetachFromLadders(); }
				catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Letting go of a ladder: " + e.Message); }
			}
			if (pc != null && pc.HasRaftAsParent) player.transform.SetParent(null, true);
			player.transform.position = at;
			if (pc != null)
			{
				try
				{
					pc.ResetExternalVelocity();
					pc.ResetFallDuration();
					HarmonyLib.Traverse.Create(pc).Field("moveDirection").SetValue(Vector3.zero);
				}
				catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Moving the player: " + e.Message); }
				if (pc.controllerType != type) pc.SwitchControllerType(type);
			}
			if (cc != null) cc.enabled = true;
		}
	}

	/// <summary>
	/// Raft makes a player a child of the raft whenever the ground under them is not on its "not the raft" layers - meant for
	/// the raft's own floors, but a custom island's ground and its Raft blocks could count as the raft too. A player on an
	/// island then moved with a raft hundreds of metres away: its drift and rocking carried them off the spot (the two-player
	/// test saw 20-36 m within a second, and 80 m while the raft drifted 110 m). So on ground that is part of a custom island
	/// Raft's step is done as for its own islands: the player is no child of anything, and a landing still ends a push.
	/// </summary>
	[HarmonyLib.HarmonyPatch(typeof(PersonController), "SetCorrectGroundParent")]
	static class IslandGroundParentPatch
	{
		static HarmonyLib.AccessTools.FieldRef<PersonController, Transform> lockedPivot;
		static HarmonyLib.AccessTools.FieldRef<PersonController, float> resetTimer;
		static Collider last;
		static bool lastOnIsland;

		/// <summary>Times a player on island ground was taken off the raft (tests).</summary>
		public static int Kept { get; private set; }

		static bool Prepare()
		{
			// (a Raft update that renames these fails the patch on its own - PatchHealth tells the player - and Raft's step stays)
			lockedPivot = HarmonyLib.AccessTools.FieldRefAccess<PersonController, Transform>("lockedPivot");
			resetTimer = HarmonyLib.AccessTools.FieldRefAccess<PersonController, float>("resetVelocityOnGroundedTimer");
			return true;
		}

		static bool Prefix(PersonController __instance, RaycastHit groundHit)
		{
			Collider c = groundHit.collider;
			if (c == null || lockedPivot(__instance) == null) return true;
			if (c != last) { last = c; lastOnIsland = c.GetComponentInParent<IslandInfoTag>() != null; }
			if (!lastOnIsland) return true;
			if (groundHit.distance < 0.4f && resetTimer(__instance) <= 0f && (__instance.terrainRaycaster == null || !__instance.terrainRaycaster.IsCurrentTerrainIDUsingCustomFriction()))
			{
				__instance.externalVelocity.x = 0f;
				__instance.externalVelocity.z = 0f;
			}
			if (__instance.transform.parent != null)
			{
				if (__instance.HasRaftAsParent) Kept++;
				__instance.transform.SetParent(null);
			}
			return false;
		}
	}

	/// <summary>
	/// A charging warthog carries the player it hits a moment (Raft's AttachPlayer) and lets go when its charge ends - but
	/// one killed while carrying never ends its charge, and Raft's AttachPlayer puts the player back on it every frame: they
	/// hung on the dead animal for good, unable to move (the two-player test: player 2 held 7 m up beside a dead hog through
	/// every later step, their place saved there). So a dead carrier lets go, as Raft does when the carried player dies.
	/// </summary>
	[HarmonyLib.HarmonyPatch(typeof(AttachPlayer), "Update")]
	static class DeadCarrierPatch
	{
		/// <summary>Players a dead carrier let go of (tests).</summary>
		public static int Released { get; private set; }

		static void Prefix(AttachPlayer __instance)
		{
			if (__instance.carriedPlayer == null) return;
			AI_NetworkBehaviour carrier = __instance.GetComponentInParent<AI_NetworkBehaviour>();
			if (carrier == null || carrier.networkEntity == null || !carrier.networkEntity.IsDead) return;
			__instance.StopCarryingPlayer(true, true);
			Released++;
			Debug.Log("[CUSTOM ISLANDS] A dead " + carrier.behaviourType + " let go of the player it carried");
		}
	}

	/// <summary>
	/// PQ2: Raft's Unstuck (in the pause menu) on and round custom islands. Raft offers it only after 30 s within 3 m of one
	/// spot, and sends the player to the nearest of its own unstuck points - there are none on a custom island, so a player
	/// pacing the floor of a shaft, a cave or a dead end under water never saw the button, or was sent far off. Near a custom
	/// island (off the raft) the button shows after 20 s within 10 m of one spot, or at once below the sea; it takes the
	/// player back to the raft.
	/// </summary>
	static class IslandUnstuck
	{
		const float Radius = 10f, Seconds = 20f;
		static Vector3 anchor;
		static float since = -1f;
		static readonly System.Reflection.FieldInfo trackedField = HarmonyLib.AccessTools.Field(typeof(Unstuck_Tracker), "tracked");

		static Transform Tracked(Unstuck_Tracker t)
		{
			Component c = trackedField != null ? trackedField.GetValue(t) as Component : null;
			return c != null ? c.transform : null;
		}

		static bool Local(Unstuck_Tracker t)
		{
			Network_Player p = ComponentManager<Network_Player>.Value;
			return p != null && p.UnstuckTracker == t;
		}

		/// <summary>Whether the local player is stuck by this rule (near a custom island, off the raft).</summary>
		internal static bool Stuck(Unstuck_Tracker t)
		{
			if (t == null || !Local(t)) return false;
			Transform tr = Tracked(t);
			if (tr == null || tr.ParentedToRaft() || Minimap.IslandAt(tr.position) == null) return false;
			return tr.position.y < -1.5f || (since >= 0f && Time.time - since >= Seconds);
		}

		[HarmonyLib.HarmonyPatch(typeof(Unstuck_Tracker), "Update")]
		static class TrackPatch
		{
			static void Postfix(Unstuck_Tracker __instance)
			{
				if (!Local(__instance)) return;
				Transform tr = Tracked(__instance);
				if (tr == null) { since = -1f; return; }
				Vector3 at = tr.position;
				if (since < 0f || (at - anchor).sqrMagnitude > Radius * Radius) { anchor = at; since = Time.time; }
			}
		}

		[HarmonyLib.HarmonyPatch(typeof(PauseMenu), "HandleToggleUnstuckPanel")]
		static class PanelPatch
		{
			static void Postfix(PauseMenu __instance)
			{
				Network_Player p = ComponentManager<Network_Player>.Value;
				if (p == null || p.UnstuckTracker == null || p.UnstuckTracker.IsConsideredStuck || !Stuck(p.UnstuckTracker)) return;
				GameObject panel = HarmonyLib.Traverse.Create(__instance).Field("unstuckPanel").GetValue<GameObject>();
				if (panel != null) panel.SetActive(true);
			}
		}

		[HarmonyLib.HarmonyPatch(typeof(Unstuck_Tracker), "Unstuck")]
		static class UnstuckPatch
		{
			static bool Prefix(Unstuck_Tracker __instance)
			{
				if (!Stuck(__instance)) return true;
				try
				{
					HarmonyLib.AccessTools.Method(__instance.GetType(), "StuckFixTPRaft").Invoke(__instance, null);
					HarmonyLib.AccessTools.Method(typeof(Unstuck_Tracker), "SetIsStuck").Invoke(__instance, new object[] { false });
					since = -1f;
					Debug.Log("[CUSTOM ISLANDS] Unstuck near a custom island: back to the raft");
					return false;
				}
				catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Unstuck: " + e.Message); return true; }
			}
		}
	}
}
