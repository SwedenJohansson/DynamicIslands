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
}
